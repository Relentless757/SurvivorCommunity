using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round MP3 (multiplayer, part T: stockpile and packing): the server's side of the packing station strip and of opening a
    /// bundle (ScActions' [ScAction] ops). An open box belongs to the player who has it open: while a client has the station
    /// open, a server-side write into it is thrown away and then overwritten by the client's copy. So the ITEM side always
    /// runs on the machine that has the box open, in its own open grid (the edit syncs to the server by itself), and the
    /// server changes only the stockpile and the XP:
    /// - Pack: the client turns the loose items into bundles in its grid (ScStockpile.Pack, the threshold from its copy's
    ///   Craftsmanship stars), then stock.packed tells the server, which pays the craftsmen's XP.
    /// - Deposit: the client takes the bundles out of its grid (ScStockpile.TakeBundles), then stock.deposit asks the server to
    ///   credit them. A refusal puts them back (ScStockpile.PutBack: their slots, any empty slot, else the bag, else the feet).
    /// - Withdraw: stock.withdraw asks first; the server debits one unit and answers with a ticket; the client then puts one
    ///   Small bundle into its grid. When the box filled up or was closed in between, stock.refund (the ticket) gives the unit
    ///   back to the stockpile; a refused refund puts the bundle into the player's bag instead (ScItemTx.GiveToLocal). Exact
    ///   either way: the stockpile or the player has it.
    /// On the host and in single player the handler runs inline, with the host's own box, so both halves run back to back with
    /// the old checks, lines and order. Requests are handled one after the other on the server's main thread, so two players
    /// (two stations, or the last unit) are judged against the stockpile the first one left: the second gets scStockEmpty.
    /// A reply that never comes (the client disconnects in between, or answers after ScActions' 30 s) loses what was in flight:
    /// a deposit's bundles to the player, a withdrawal's unit to the community. Documented, not handled.
    ///
    /// What the server checks (every stock.* op but the refund): the position holds a packing station or a held outpost claim's
    /// box (ScStockpile.StationOf), a remote sender is within the loot window's own reach of it (Constants.cCollectItemDistance
    /// + 30 m, where vanilla closes the window, plus slack for lag) and holds its open lock (LockManager's single lock on that
    /// storage feature, channel 0). The amounts are the client's (trusted in co-op), checked for plausibility only: a resource
    /// in range; Pack's sizes add up to its units (at most one Medium or Small); Deposit's units lie between its bundle count
    /// and three times it, and the bundle count fits the box (its slots x the largest bundle stack; a remote sender only). A
    /// withdrawal's ticket is single-use, the sender's own and at most TicketSeconds old. Not checked: that the bundles really
    /// were in the box (the server's copy of it is the client's, and already without them when the request lands).
    ///
    /// Ops (whole numbers unless marked s:; x, y, z = the station's block position):
    ///   stock.packed    x, y, z, resource, units, large, medium, small, threshold   the craftsmen's XP for one pack
    ///   stock.deposit   x, y, z, bundles, units per Resource (7)                    the stockpile credited; hint stock
    ///   stock.withdraw  x, y, z, resource, room (1: the box had an empty slot)       one unit debited; hints ticket, stock
    ///   stock.refund    ticket                                                       the unit back (box full or closed); hint stock
    ///   bundle.opened   s: the bundle's item name                                    the Logistics member's XP (ScLifeSkills)
    /// Hints: stock = the stockpile after the op ("food,meds,ammo,mats,fuel,water,luxury"), shown by a client's strip until its
    /// copy catches up; ticket = the withdrawal's refund ticket.
    /// </summary>
    public static class ScActionsStock
    {
        /// <summary>Reply hints: the stockpile after the op (7 numbers), and a withdrawal's refund ticket.</summary>
        public const string HintStock = "stock", HintTicket = "ticket";

        /// <summary>Meters beyond the loot window's own closing distance that a remote sender may stand (their position lags).</summary>
        private const float NearSlack = 8f;
        /// <summary>A withdrawal's refund ticket lives this many real seconds (the refund is sent the moment the answer lands).</summary>
        private const float TicketSeconds = 120f;

        private struct Ticket
        {
            public int EntityId;
            public Resource Res;
            public float At;
            public World World;
        }

        /// <summary>The server's open withdrawal tickets, by id (main thread).</summary>
        private static readonly Dictionary<int, Ticket> tickets = new Dictionary<int, Ticket>();
        private static readonly List<int> expired = new List<int>();
        private static int nextTicket;

        private static string Fmt(string key, params object[] args)
        {
            return string.Format(Localization.Get(key), args);
        }

        // ------------------------------------------------------------------ asking (the machine that has the box open) ----

        private static int[] At(TEFeatureStorage st, params int[] rest)
        {
            Vector3i p = st != null ? st.ToWorldPos() : Vector3i.zero;
            var a = new int[3 + (rest != null ? rest.Length : 0)];
            a[0] = p.x; a[1] = p.y; a[2] = p.z;
            if (rest != null) Array.Copy(rest, 0, a, 3, rest.Length);
            return a;
        }

        /// <summary>Pack done in this machine's grid: the server pays the craftsmen (inline on the host). No answer is needed.</summary>
        public static void RequestPacked(TEFeatureStorage st, Resource r, int units, int large, int medium, int small, int threshold)
        {
            ScActions.Request("stock.packed", At(st, (int)r, units, large, medium, small, threshold), null, reply =>
            {
                if (!reply.Ok) ScLog.Verbose("stock.packed refused (" + reply.Message + "): the bundles stay, no Craftsmanship XP");
            });
        }

        /// <summary>The bundles taken out of this machine's grid: credit them (units per Resource).</summary>
        public static void RequestDeposit(TEFeatureStorage st, int bundles, int[] counts, Action<ScActionReply> onResult)
        {
            var rest = new int[1 + ScNames.ResourceCount];
            rest[0] = bundles;
            for (int i = 0; i < ScNames.ResourceCount; i++) rest[1 + i] = counts != null && i < counts.Length ? counts[i] : 0;
            ScActions.Request("stock.deposit", At(st, rest), null, onResult);
        }

        /// <summary>One unit of r, asked before the bundle goes into this machine's grid (room: the box has an empty slot now).</summary>
        public static void RequestWithdraw(TEFeatureStorage st, Resource r, bool room, Action<ScActionReply> onResult)
        {
            ScActions.Request("stock.withdraw", At(st, (int)r, room ? 1 : 0), null, onResult);
        }

        /// <summary>A withdrawn unit that could not go into the box goes back to the stockpile (its ticket).</summary>
        public static void RequestRefund(int ticket, Action<ScActionReply> onResult)
        {
            ScActions.Request("stock.refund", new[] { ticket }, null, onResult);
        }

        /// <summary>A bundle was opened on this machine (ScBundleOpen): the server pays the Logistics member (inline on the host).</summary>
        public static void RequestBundleOpened(string bundleItem)
        {
            ScActions.Request("bundle.opened", null, new[] { bundleItem ?? "" }, null);
        }

        /// <summary>The stockpile figures a reply carries (HintStock), or null.</summary>
        public static int[] StockFromHint(ScActionReply reply)
        {
            string s = reply != null ? reply.HintStr(HintStock, null) : null;
            if (string.IsNullOrEmpty(s)) return null;
            string[] parts = s.Split(',');
            if (parts.Length < ScNames.ResourceCount) return null;
            var n = new int[ScNames.ResourceCount];
            for (int i = 0; i < ScNames.ResourceCount; i++)
                if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out n[i])) return null;
            return n;
        }

        // ------------------------------------------------------------------ checks (the server) ----

        private static void SetStockHint(ScActionReply reply)
        {
            Community c = ScRegistry.Community;
            var parts = new string[ScNames.ResourceCount];
            for (int i = 0; i < ScNames.ResourceCount; i++) parts[i] = c.Get((Resource)i).ToString(CultureInfo.InvariantCulture);
            reply.SetHint(HintStock, string.Join(",", parts));
        }

        private static bool ResourceArg(int v, out Resource r)
        {
            r = Resource.Food;
            if (v < 0 || v >= ScNames.ResourceCount) return false;
            r = (Resource)v;
            return true;
        }

        /// <summary>
        /// The station a stock op names, for this sender - or null with the reason (a localization key) set on reply. The host's
        /// own player has it open right now (the strip only shows on its open loot window): no new test there beyond what the
        /// station is. A remote sender must be within the loot window's reach and hold the box's open lock.
        /// </summary>
        private static TEFeatureStorage StationFor(EntityPlayer sender, Vector3i pos, ScActionReply reply)
        {
            World w = ScCommunity.World;
            TileEntityComposite tec = w != null ? w.GetTileEntity(pos) as TileEntityComposite : null;
            TEFeatureStorage st = tec != null ? ScStockpile.StationOf(tec.GetFeature<TEFeatureStorage>()) : null;
            if (st == null) { reply.Fail("scStockNotStation", ""); return null; }
            if (sender is EntityPlayerLocal) return st;
            // XUiC_LootWindow's own rule (it closes the window beyond cCollectItemDistance + 30 m, and skips a zero center)
            float reach = Constants.cCollectItemDistance + 30f + NearSlack;
            Vector3 center = st.ToWorldCenterPos();
            if (center != Vector3.zero && (center - sender.position).sqrMagnitude > reach * reach) { reply.Fail("scStockNotNear", ""); return null; }
            if (!HoldsLock(sender, st)) { reply.Fail("scStockNotOpen", ""); return null; }
            return st;
        }

        /// <summary>Whether the sender holds the box's open lock (vanilla's Search takes LockManager's single lock, channel 0).</summary>
        private static bool HoldsLock(EntityPlayer sender, TEFeatureStorage st)
        {
            try
            {
                LockManager lm = LockManager.Instance;
                int holder;
                return lm != null && sender != null && lm.singleLocks.TryGetByValue(new LockManager.LockEntry(st, 0), out holder) && holder == sender.entityId;
            }
            catch (Exception e)
            {
                ScLog.Once("stock-lock", e);
                return false;
            }
        }

        /// <summary>The most bundles one box can hold: its slots x the largest stack of any bundle item.</summary>
        private static long BoxCapacity(TEFeatureStorage st)
        {
            int slots = st != null && st.ItemGrid != null ? st.ItemGrid.Length : 0;
            int stack = 1;
            for (int i = 0; i < ScNames.ResourceCount; i++)
                for (int s = 0; s <= 3; s++)
                {
                    ItemClass ic = ItemClass.GetItemClass(ScNames.BundleItem((Resource)i, (BundleSize)s));
                    if (ic != null) stack = Math.Max(stack, ic.Stacknumber.Value);
                }
            return (long)slots * stack;
        }

        private static string Who(EntityPlayer p)
        {
            return p != null ? p.PlayerDisplayName : "?";
        }

        // ------------------------------------------------------------------ the ops (the server) ----

        /// <summary>
        /// stock.packed: a pack was done in the sender's open grid - the craftsmen at home learn (1 XP each while any are
        /// stationed), as ScStockpile.Pack always paid. Nothing else changes on the server.
        /// </summary>
        [ScAction("stock.packed", ScActionAccess.Community)]
        private static void Packed(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            if (StationFor(sender, args.Pos(0), reply) == null) return;
            Resource r;
            int units = args.Int(4), large = args.Int(5), medium = args.Int(6), small = args.Int(7);
            if (!ResourceArg(args.Int(3, -1), out r) || units <= 0 || large < 0 || medium < 0 || small < 0 || medium + small > 1
                || (long)large * 3 + medium * 2 + small != units)
            {
                ScLog.Once("stock-packed-bad-" + sender.entityId, "stock.packed from " + Who(sender) + " does not add up (" + string.Join(",", Array.ConvertAll(args.Ints, x => x.ToString(CultureInfo.InvariantCulture))) + ") - refused");
                reply.Fail("scInternal", "");
                return;
            }
            int craftStars = ScSkills.SpecialistStars(Specialty.Craftsmanship);
            bool paid = false;
            if (craftStars > 0)
            {
                foreach (var cm in ScRegistry.Members.Values)
                    if (cm.Specialty == Specialty.Craftsmanship && cm.IsHome && cm.Status != MemberStatus.OnRun) { ScSkills.AddSpecialtyXp(cm, 1f); paid = true; }
            }
            if (!(sender is EntityPlayerLocal))
            {
                // a remote pack: its own log line here (the host's is ScStockpile.Pack's), and a save so the clients' copy shows the XP
                // (the host never saved for it: its own window reads the registry itself)
                int threshold = args.Int(8);
                ScLog.Out("packed " + units + " " + r + " unit(s) (" + large + " large, " + medium + " medium, " + small + " small) at the station by " + Who(sender)
                          + (threshold != ScStockpile.PackThreshold(r, craftStars) ? " (its copy's threshold " + threshold + ", the server's " + ScStockpile.PackThreshold(r, craftStars) + ")" : ""));
                if (paid) ScRegistry.SaveAtomic();
            }
            reply.Done();
        }

        /// <summary>stock.deposit: the bundles the sender took out of its open grid go into the stockpile.</summary>
        [ScAction("stock.deposit", ScActionAccess.Community)]
        private static void Deposit(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            TEFeatureStorage st = StationFor(sender, args.Pos(0), reply);
            if (st == null) return;
            int moved = args.Int(3);
            var counts = new int[ScNames.ResourceCount];
            long sum = 0;
            bool bad = moved <= 0;
            for (int i = 0; i < ScNames.ResourceCount; i++)
            {
                counts[i] = args.Int(4 + i);
                if (counts[i] < 0) bad = true;
                sum += counts[i];
            }
            // the host's own box was just counted by this very build (no new test there); a remote sender's figures are its own
            if (!bad && !(sender is EntityPlayerLocal) && (sum < moved || sum > 3L * moved || moved > BoxCapacity(st))) bad = true;
            if (bad)
            {
                ScLog.Once("stock-deposit-bad-" + sender.entityId, "stock.deposit from " + Who(sender) + " is not plausible (" + string.Join(",", Array.ConvertAll(args.Ints, x => x.ToString(CultureInfo.InvariantCulture))) + ") - refused, the bundles go back");
                reply.Fail("scInternal", "");
                return;
            }
            ScStockpile.CreditDeposit(counts, moved, sender is EntityPlayerLocal ? null : Who(sender));
            reply.Done();
            SetStockHint(reply);
        }

        /// <summary>
        /// stock.withdraw: one unit of the resource out of the stockpile, for one Small bundle the sender's machine puts into its
        /// open grid. The answer carries a refund ticket (stock.refund) for when it no longer fits.
        /// </summary>
        [ScAction("stock.withdraw", ScActionAccess.Community)]
        private static void Withdraw(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            if (StationFor(sender, args.Pos(0), reply) == null) return;
            Resource r;
            if (!ResourceArg(args.Int(3, -1), out r)) { reply.Fail("scInternal", ""); return; }
            string why;
            if (!ScStockpile.DebitWithdraw(r, args.Int(4) != 0, out why)) { reply.Fail(why, ""); return; }
            reply.Done();
            reply.SetHint(HintTicket, Issue(sender, r));
            SetStockHint(reply);
        }

        /// <summary>stock.refund: a withdrawn unit that did not fit (or found the box closed) goes back, once per ticket.</summary>
        [ScAction("stock.refund", ScActionAccess.Community)]
        private static void Refund(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            Purge();
            int id = args.Int(0);
            Ticket t;
            if (!tickets.TryGetValue(id, out t) || t.EntityId != sender.entityId || !ReferenceEquals(t.World, ScCommunity.World))
            {
                reply.Fail("scInternal", "");      // the client then puts the bundle into the player's bag instead
                return;
            }
            tickets.Remove(id);
            ScRegistry.Community.Add(t.Res, 1);
            ScRegistry.SaveAtomic();
            reply.Done();
            SetStockHint(reply);
        }

        /// <summary>
        /// bundle.opened: a bundle was opened on the sender's machine (ScBundleOpen.Open; the assortment is that machine's, as
        /// vanilla) - the best Logistics member at home learns (ScLifeSkills.OnBundleOpened). Checked: the name is an SC bundle.
        /// </summary>
        [ScAction("bundle.opened", ScActionAccess.Community)]
        private static void BundleOpened(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            Resource r;
            int units;
            if (!ScNames.TryBundle(args.Str(0), out r, out units)) { reply.Fail("", ""); return; }
            ScLifeSkills.OnBundleOpened();
            reply.Done();
        }

        // ------------------------------------------------------------------ withdrawal tickets (the server) ----

        private static int Issue(EntityPlayer sender, Resource r)
        {
            Purge();
            int id = ++nextTicket;
            if (id <= 0) id = nextTicket = 1;
            tickets[id] = new Ticket { EntityId = sender.entityId, Res = r, At = Time.realtimeSinceStartup, World = ScCommunity.World };
            return id;
        }

        private static void Purge()
        {
            if (tickets.Count == 0) return;
            float now = Time.realtimeSinceStartup;
            World w = ScCommunity.World;
            expired.Clear();
            foreach (var kv in tickets)
                if (now - kv.Value.At > TicketSeconds || !ReferenceEquals(kv.Value.World, w)) expired.Add(kv.Key);
            for (int i = 0; i < expired.Count; i++) tickets.Remove(expired[i]);
        }
    }
}
