using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Ammo for the community's 0-XNPCCore survivors (migration M7.3, decision D3: real weapons with finite ammo, and the
    /// stockpile refills them only in the base or at an outpost).
    ///
    /// Where the rounds are: in the NPC's inventory window (SCore's HarvestManager container). V4 saves it with the NPC,
    /// SCore reloads an EntityAliveSDXV4's gun from it (ItemActionRangedHarvestAmmo) and XNPC's dry-gun failover counts it
    /// (EntityUtilities.GetItemStores). A new survivor gets a starting stack once (ScLoadout.AfterPostInit); what it
    /// shoots is gone.
    ///
    /// Resupply (Tick, every 5 s). A member at home or with the player (Resident, Following, PendingStation) or a member's
    /// garrison stand-in, standing inside the home zone or at one of the community's held outposts (its POI or its land
    /// claim), whose gun is down to fewer than XnpcResupplyBelow rounds (magazine plus every store), is handed
    /// XnpcResupplyAmmo rounds of the gun's basic ammo (XnpcResupplyArrows for a bow or crossbow), at most once per
    /// XnpcResupplyCooldown. Nowhere else - out there 0-XNPCCore's failover has put the melee weapon in their hand, and
    /// that is where it stays - and never for strangers, runners, hidden bodies or a rescue stand-in. 0-XNPCCore's
    /// failover restores the gun by itself once its reserve is above 0 again (FailoverRestoreRules.ShouldRestoreRanged).
    ///
    /// The conversion from the stockpile's ammo to rounds is the packing station's own arithmetic run backwards: a round
    /// costs its packing points (ScCategories.PointsEach: EconomicValue, or its ValueOverrides entry) times
    /// XnpcResupplyPointsMult, and an ammo bundle holds PointsPerBundleAmmo points. A bundle is opened - taken off the
    /// stockpile - only when the points left in the last one (Community.AmmoOpenPoints, saved) do not cover the resupply,
    /// so a bundle is used up round by round instead of being lost to one top-up. That is the only way the stockpile's
    /// ammo reaches the survivors (there is no daily upkeep), and the daily ledger's "ammo used" is the bundles opened.
    ///
    /// The record (P1 of the round 2 review). Every 5 s, for each member's current body - the stand-in while one stands
    /// in for them - what they carry for their gun is written to the member record (Member.Ammo, saved as ammo=). A body
    /// the community re-creates for a survivor it knows (ScSpawn: a call-back, a garrison or rescue stand-in, a runner's
    /// fresh body) starts with exactly that instead of a new survivor's starting stack, so re-creating somebody never
    /// makes ammo; a record that was never written (they never stood loaded with a pair gun) starts them empty, and the
    /// next resupply in the base or at an outpost arms them. Host only.
    /// </summary>
    public static class ScAmmo
    {
        private const float CheckSeconds = 5f;
        /// <summary>A refused resupply (empty stockpile, full window) is logged at most this often per NPC.</summary>
        private const float RefusalLogSeconds = 300f;

        private static float nextCheck;
        /// <summary>Real time each body may next be resupplied (XnpcResupplyCooldown), by entity id.</summary>
        private static readonly Dictionary<int, float> nextResupply = new Dictionary<int, float>();
        private static readonly Dictionary<int, float> nextRefusalLog = new Dictionary<int, float>();
        /// <summary>Bodies that are low but stand outside the base and the outposts: counted once per dry spell.</summary>
        private static readonly HashSet<int> waitingOutside = new HashSet<int>();
        /// <summary>ScSpawn -&gt; ScLoadout.BeforePostInit -&gt; AfterPostInit: the record a re-created body starts with, by entity id.</summary>
        private static readonly Dictionary<int, Dictionary<string, int>> restore = new Dictionary<int, Dictionary<string, int>>();

        /// <summary>
        /// Set by ScSpawn.Create around the creation of a body for a survivor the community already knows, with that
        /// member's record (null = never recorded). Taken by the next managed body's PostInit, like ScLoadout.Pending.
        /// </summary>
        public static bool PendingKnown;
        public static Dictionary<string, int> PendingRecord;

        // counters for sc xnpc (main thread only)
        private static int resupplies, roundsGiven, bundlesOpened, refusedEmpty, refusedFull, outsideWaits, restored, seeded;

        public static void Reset()
        {
            nextCheck = 0f;
            nextResupply.Clear();
            nextRefusalLog.Clear();
            waitingOutside.Clear();
            restore.Clear();
            PendingKnown = false;
            PendingRecord = null;
            resupplies = roundsGiven = bundlesOpened = refusedEmpty = refusedFull = outsideWaits = restored = seeded = 0;
        }

        // ------------------------------------------------------------------ the gun and its rounds ----

        /// <summary>A weapon's ranged action that runs on finite ammo (0-XNPCCore fires and fails over on Actions[0]), or null.</summary>
        public static ItemActionRanged RangedAction(ItemClass ic)
        {
            if (ic == null || ic.Actions == null || ic.Actions.Length == 0) return null;
            ItemActionRanged r = ic.Actions[0] as ItemActionRanged;
            return r != null && !r.InfiniteAmmo && r.MagazineItemNames != null && r.MagazineItemNames.Length > 0 ? r : null;
        }

        /// <summary>The gun of a body's pair (ScLoadout), or null.</summary>
        public static ItemClass PairGun(EntityAlive e)
        {
            Loadout l = ScLoadout.Current(e);
            ItemClass ic = l != null && l.Ranged != null ? ItemClass.GetItemClass(l.Ranged, true) : null;
            return RangedAction(ic) != null ? ic : null;
        }

        /// <summary>Rounds of one ammo item in every store SCore reloads an NPC from (toolbelt, bag, inventory window).</summary>
        private static int InStores(EntityAlive e, int type)
        {
            int n = 0;
            foreach (EntityUtilities.EntityItemStore store in EntityUtilities.GetItemStores(e))
            {
                if (store.Stacks == null) continue;
                foreach (ItemStack s in store.Stacks)
                    if (s != null && !s.IsEmpty() && s.itemValue != null && s.itemValue.type == type) n += s.count;
            }
            return n;
        }

        /// <summary>
        /// Rounds the gun can still fire: every one of its ammo types in the stores (what 0-XNPCCore's failover and close-range
        /// swap count as its reserve) plus its magazine while it is in the hand.
        /// </summary>
        public static int Rounds(EntityAlive e, ItemClass gun)
        {
            ItemActionRanged r = RangedAction(gun);
            if (e == null || r == null) return 0;
            int n = 0;
            foreach (string name in r.MagazineItemNames)
            {
                ItemValue iv = ItemClass.GetItem(name, true);
                if (iv != null && !iv.IsEmpty()) n += InStores(e, iv.type);
            }
            ItemValue held = e.inventory != null ? e.inventory.holdingItemItemValue : null;
            if (held != null && !held.IsEmpty() && held.type == gun.Id) n += Math.Max(0, (int)held.Meta);
            return n;
        }

        /// <summary>
        /// The fullest magazine of this gun outside the hand - its copies on itemsOnEnterGame and in the stores - which is
        /// how 0-XNPCCore's own dry test reads a weapon it is not holding (SdcsStoredWeaponMagazine).
        /// </summary>
        public static float StoredMagazine(EntityAlive e, ItemClass gun)
        {
            if (e == null || gun == null) return 0f;
            float best = 0f;
            if (e.itemsOnEnterGame != null)
                foreach (ItemStack s in e.itemsOnEnterGame)
                    if (s != null && !s.IsEmpty() && s.itemValue.type == gun.Id) best = Math.Max(best, s.itemValue.Meta);
            foreach (EntityUtilities.EntityItemStore store in EntityUtilities.GetItemStores(e))
            {
                if (store.Stacks == null) continue;
                foreach (ItemStack s in store.Stacks)
                    if (s != null && !s.IsEmpty() && s.itemValue != null && s.itemValue.type == gun.Id) best = Math.Max(best, s.itemValue.Meta);
            }
            return best;
        }

        /// <summary>Stockpile points one round costs: its packing value times XnpcResupplyPointsMult, at least 1.</summary>
        public static int PointsPerRound(ItemClass ammo)
        {
            return Math.Max(1, (int)Math.Round(ScCategories.PointsEach(ammo) * ScConfig.XnpcResupplyPointsMult));
        }

        // ------------------------------------------------------------------ the record (P1) ----

        /// <summary>
        /// What a body carries for its pair's gun, by ammo item name: every store's stacks of the gun's ammo types, plus a
        /// loaded magazine as rounds of the type it holds (a re-created body starts with an empty magazine, so those rounds
        /// go into its window). Null when the body has no pair gun - nothing to record.
        /// </summary>
        public static Dictionary<string, int> Carried(EntityAlive e)
        {
            ItemClass gun = PairGun(e);
            ItemActionRanged r = RangedAction(gun);
            if (e == null || r == null) return null;
            var rec = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string name in r.MagazineItemNames)
            {
                ItemValue iv = ItemClass.GetItem(name, true);
                if (iv == null || iv.IsEmpty() || iv.ItemClass == null) continue;
                Add(rec, iv.ItemClass.GetItemName(), InStores(e, iv.type));
            }
            ItemValue held = e.inventory != null ? e.inventory.holdingItemItemValue : null;
            if (held != null && !held.IsEmpty() && held.type == gun.Id && held.Meta > 0)
            {
                int idx = held.SelectedAmmoTypeIndex < r.MagazineItemNames.Length ? held.SelectedAmmoTypeIndex : 0;
                ItemValue loaded = ItemClass.GetItem(r.MagazineItemNames[idx], true);
                if (loaded != null && !loaded.IsEmpty() && loaded.ItemClass != null) Add(rec, loaded.ItemClass.GetItemName(), held.Meta);
            }
            return rec;
        }

        private static void Add(Dictionary<string, int> rec, string name, int n)
        {
            if (string.IsNullOrEmpty(name) || n <= 0) return;
            int had;
            rec.TryGetValue(name, out had);
            rec[name] = had + n;
        }

        public static int Total(Dictionary<string, int> rec)
        {
            int n = 0;
            if (rec != null) foreach (int v in rec.Values) n += v;
            return n;
        }

        /// <summary>Writes what this body carries into the member record now (the ticker, and a stand-in about to be removed).</summary>
        public static void Record(Member m, EntityAlive e)
        {
            try
            {
                if (m == null || e == null || !ScXnpc.IsBody(e)) return;
                Dictionary<string, int> rec = Carried(e);
                if (rec != null) m.Ammo = rec;
            }
            catch (Exception ex)
            {
                ScLog.Once("ammo-record", ex);
            }
        }

        /// <summary>The registry form: "item:n,item:n", or "none" for a record of nothing.</summary>
        public static string Encode(Dictionary<string, int> rec)
        {
            var parts = new List<string>();
            if (rec != null) foreach (var kv in rec) if (kv.Value > 0) parts.Add(kv.Key + ":" + kv.Value.ToString(CultureInfo.InvariantCulture));
            return parts.Count > 0 ? string.Join(",", parts.ToArray()) : "none";
        }

        public static Dictionary<string, int> Decode(string s)
        {
            var rec = new Dictionary<string, int>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(s) || s.Trim() == "none") return rec;
            foreach (string part in s.Split(','))
            {
                int i = part.LastIndexOf(':');
                int n;
                if (i <= 0 || !int.TryParse(part.Substring(i + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) continue;
                Add(rec, part.Substring(0, i).Trim(), n);
            }
            return rec;
        }

        public static Dictionary<string, int> Copy(Dictionary<string, int> rec)
        {
            return rec != null ? new Dictionary<string, int>(rec, StringComparer.Ordinal) : null;
        }

        // ------------------------------------------------------------------ re-created bodies ----

        /// <summary>
        /// ScLoadout.BeforePostInit (the PostInit prefix): takes ScSpawn's pending record for this body - consumed by every
        /// body that runs it, kept only for one of the managed survivor bodies.
        /// </summary>
        public static void TakePending(EntityAlive e, bool managed)
        {
            bool known = PendingKnown;
            Dictionary<string, int> rec = PendingRecord;
            PendingKnown = false;
            PendingRecord = null;
            if (known && managed && e != null) restore[e.entityId] = rec;
        }

        /// <summary>
        /// ScLoadout.AfterPostInit, before the starting stack: a body re-created for a survivor the community knows gets
        /// the record's rounds (none when nothing was ever recorded) and is marked as given its ammo, so it never gets the
        /// starting stack a new survivor gets. False for every other body (a new survivor, a body loaded from the save).
        /// </summary>
        public static bool RestoreRecorded(EntityAlive e)
        {
            Dictionary<string, int> rec;
            if (e == null || e.Buffs == null || !restore.TryGetValue(e.entityId, out rec)) return false;
            restore.Remove(e.entityId);
            e.Buffs.SetCustomVar(ScLoadout.AmmoSeededCvar, 1f);
            int n = 0;
            if (rec != null)
            {
                foreach (var kv in rec)
                {
                    ItemValue iv = ItemClass.GetItem(kv.Key, true);
                    if (iv == null || iv.IsEmpty() || kv.Value <= 0) continue;
                    n += AddRounds(e, iv, kv.Value);
                }
            }
            restored++;
            ScLog.Verbose("re-created body " + e.entityId + ": " + (rec == null ? "no ammo record - starts empty" : n + " round(s) from the record (" + Encode(rec) + ")")
                          + ", no starting stack");
            return true;
        }

        /// <summary>A new survivor's starting stack was given (ScLoadout.AfterPostInit): counted for sc xnpc.</summary>
        public static void NoteSeeded() { seeded++; }

        /// <summary>Rounds of this ammo the inventory window still has room for: partly filled stacks and empty slots.</summary>
        private static int Room(EntityAlive e, ItemValue ammo)
        {
            ItemStack[] items = HarvestManager.GetOrCreate(e.entityId).items;
            int stack = Math.Max(1, ammo.ItemClass.Stacknumber.Value);
            int room = 0;
            if (items == null) return 0;
            foreach (ItemStack s in items)
            {
                if (s == null || s.IsEmpty()) room += stack;
                else if (s.itemValue.type == ammo.type) room += Math.Max(0, stack - s.count);
            }
            return room;
        }

        /// <summary>
        /// Puts rounds into the inventory window in stack-sized pieces (HarvestManager.AddItem files an oversized stack in one
        /// slot as it is); returns how many went in. Straight into the container, as ScNpc.AddItem does for a framework NPC.
        /// </summary>
        private static int AddRounds(EntityAlive e, ItemValue ammo, int count)
        {
            if (ammo.ItemClass == null) return 0;
            int stack = Math.Max(1, ammo.ItemClass.Stacknumber.Value);
            count = Math.Min(count, Room(e, ammo));
            int added = 0;
            while (added < count)
            {
                int k = Math.Min(stack, count - added);
                if (!HarvestManager.AddItem(e.entityId, new ItemStack(ammo.Clone(), k))) break;
                added += k;
            }
            return added;
        }

        // ------------------------------------------------------------------ resupply ----

        /// <summary>
        /// Where the stockpile reaches (D3): "home" inside the home zone, an outpost's name at one of the community's held
        /// outposts (inside its POI, or inside the land claim square around its flag), else null.
        /// </summary>
        public static string Where(Vector3i p)
        {
            if (ScZone.Contains(p)) return "home";
            int vr = ScZone.VanillaRadius;
            foreach (Outpost o in ScRegistry.Outposts)
            {
                if (!o.Holding) continue;
                if (o.Contains(p.x, p.z) || (Math.Abs(p.x - o.Flag.x) <= vr && Math.Abs(p.z - o.Flag.z) <= vr))
                    return string.IsNullOrEmpty(o.Name) ? "outpost " + o.Id : o.Name;
            }
            return null;
        }

        /// <summary>ScTicker, every second (works every CheckSeconds): records every member's ammo and resupplies the ones who qualify.</summary>
        public static void Tick(World w)
        {
            try
            {
                float now = Time.unscaledTime;
                if (now < nextCheck || w == null) return;
                nextCheck = now + CheckSeconds;
                foreach (Member m in ScRegistry.Members.Values)
                {
                    bool standIn;
                    EntityAlive e = BodyOf(w, m, out standIn);
                    if (e == null || !ScXnpc.IsBody(e)) continue;
                    Record(m, e);
                    if (ScConfig.XnpcResupply) Resupply(m, e, standIn, now);
                }
            }
            catch (Exception ex)
            {
                ScLog.Once("ammo-tick", ex);
            }
        }

        /// <summary>
        /// The body that is this member right now: their stand-in while one stands in for them (an outpost posting, a
        /// rescue), else their own entity while it is in the world, visible and theirs - not an offline runner's stale copy
        /// (FindNpc), not a body parked hidden for a run or a posting, not an id a stranger of another class inherited.
        /// </summary>
        private static EntityAlive BodyOf(World w, Member m, out bool standIn)
        {
            standIn = false;
            if (m.StandInId >= 0)
            {
                EntityAlive s = ScNpc.As(w.GetEntity(m.StandInId));
                if (s != null && !s.IsDead() && s.Buffs != null && s.Buffs.HasCustomVar(ScRadio.StandInCvar)
                    && (int)s.Buffs.GetCustomVar(ScRadio.StandInCvar) == m.Id)
                {
                    standIn = true;
                    return s;
                }
            }
            EntityAlive e = ScCommunity.FindNpc(m.Id);
            if (e == null || e.IsDead() || ScNpc.IsOnMission(e)) return null;
            if (!string.IsNullOrEmpty(m.Class) && !string.Equals(ScCommunity.ClassName(e), m.Class, StringComparison.OrdinalIgnoreCase)) return null;
            return e;
        }

        /// <summary>Is this body one the stockpile resupplies at all (where it stands aside)? The why for sc xnpc.</summary>
        private static bool Eligible(Member m, EntityAlive e, bool standIn, out string why)
        {
            if (m == null) { why = "not a member (strangers live on what they carry)"; return false; }
            if (standIn)
            {
                // a garrison stand-in holds an outpost; a rescue stand-in is out at a POI on the run
                bool garrison = e.Buffs != null && e.Buffs.HasCustomVar(ScGarrison.StandInOutpostCvar);
                why = garrison ? "garrison stand-in" : "rescue stand-in (out on a run)";
                return garrison;
            }
            bool ok = m.Status == MemberStatus.Resident || m.Status == MemberStatus.Following || m.Status == MemberStatus.PendingStation;
            why = ok ? m.Status.ToString() : m.Status + " (out on the community's business with what they took)";
            return ok;
        }

        private static void Resupply(Member m, EntityAlive e, bool standIn, float now)
        {
            string why;
            if (!Eligible(m, e, standIn, out why) || ScMedic.IsUsing(e.entityId)) return;
            ItemClass gun = PairGun(e);
            ItemActionRanged r = RangedAction(gun);
            if (r == null) return;
            if (Rounds(e, gun) >= ScConfig.XnpcResupplyBelow) { waitingOutside.Remove(e.entityId); return; }
            float due;
            if (nextResupply.TryGetValue(e.entityId, out due) && now < due) return;
            string where = Where(e.GetBlockPosition());
            if (where == null)
            {
                // D3: out in the world they stay as they are - 0-XNPCCore's failover has the melee weapon in their hand
                if (waitingOutside.Add(e.entityId))
                {
                    outsideWaits++;
                    ScLog.Verbose(m.Name + " (id " + e.entityId + ") is low on ammo outside the base and the outposts - no resupply out there");
                }
                return;
            }
            waitingOutside.Remove(e.entityId);
            ItemValue ammo = ItemClass.GetItem(r.MagazineItemNames[0], true);
            if (ammo == null || ammo.IsEmpty() || ammo.ItemClass == null) return;
            bool bow = !ScLoadout.IsFirearm(gun.GetItemName());
            int want = Math.Min(bow ? ScConfig.XnpcResupplyArrows : ScConfig.XnpcResupplyAmmo, Room(e, ammo));
            if (want <= 0)
            {
                refusedFull++;
                nextResupply[e.entityId] = now + ScConfig.XnpcResupplyCooldown;
                Refused(e, m.Name + "'s inventory window is full - no room for the " + ammo.ItemClass.GetItemName() + " they need");
                return;
            }
            Community c = ScRegistry.Community;
            int each = PointsPerRound(ammo.ItemClass);
            int perBundle = Math.Max(1, ScConfig.PointsPerBundle[(int)Resource.Ammo]);
            int opened = 0;
            while (c.AmmoOpenPoints < want * each && c.Get(Resource.Ammo) > 0)
            {
                c.Add(Resource.Ammo, -1);
                c.AmmoOpenPoints += perBundle;
                opened++;
            }
            int give = Math.Min(want, c.AmmoOpenPoints / each);
            if (give < want) c.AmmoShort = true;        // tonight's ledger: the stockpile could not cover a resupply
            int added = give > 0 ? AddRounds(e, ammo, give) : 0;
            c.AmmoOpenPoints = Math.Max(0, c.AmmoOpenPoints - added * each);
            c.AmmoOpened += opened;
            bundlesOpened += opened;
            nextResupply[e.entityId] = now + ScConfig.XnpcResupplyCooldown;
            if (added <= 0)
            {
                refusedEmpty++;
                Refused(e, m.Name + " needs ammo " + (where == "home" ? "at home" : "at " + where) + " but the stockpile has none (pack some at the packing station)");
                if (opened > 0) ScRegistry.SaveAtomic();
                return;
            }
            resupplies++;
            roundsGiven += added;
            Record(m, e);       // the stockpile just paid for these: a body that unloads before the next tick keeps them
            ScLog.Out((standIn ? m.Name + "'s stand-in" : m.Name) + " (id " + e.entityId + ") resupplied " + (where == "home" ? "at home" : "at " + where) + ": "
                      + added + " " + ammo.ItemClass.GetItemName() + " for " + gun.GetItemName() + " at " + each + " point(s) each"
                      + (opened > 0 ? " - opened " + opened + " ammo bundle(s), " + c.Get(Resource.Ammo) + " left" : "")
                      + " (" + c.AmmoOpenPoints + " point(s) left in the open bundle)");
            ScRegistry.SaveAtomic();
        }

        private static void Refused(EntityAlive e, string msg)
        {
            float now = Time.unscaledTime, due;
            if (nextRefusalLog.TryGetValue(e.entityId, out due) && now < due) return;
            nextRefusalLog[e.entityId] = now + RefusalLogSeconds;
            ScLog.Out(msg);
        }

        // ------------------------------------------------------------------ diagnostics ----

        /// <summary>The counters, the open bundle and the switches, for sc xnpc.</summary>
        public static string Report()
        {
            Community c = ScRegistry.Community;
            return "resupply " + (ScConfig.XnpcResupply ? "on" : "off") + ": " + resupplies + " resuppl" + (resupplies == 1 ? "y" : "ies") + ", " + roundsGiven
                   + " round(s), " + bundlesOpened + " bundle(s) opened this session (" + c.AmmoOpened + " since the last rollover), refused "
                   + refusedEmpty + " (stockpile empty) / " + refusedFull + " (window full), " + outsideWaits + " ran low outside; "
                   + c.Get(Resource.Ammo) + " ammo bundle(s) in the stockpile, " + c.AmmoOpenPoints + " point(s) in the open one; starting stacks "
                   + seeded + ", re-created bodies given their record " + restored;
        }

        /// <summary>One NPC's ammo as the resupply sees it, for sc xnpc &lt;id&gt;.</summary>
        public static string Describe(EntityAlive e)
        {
            if (e == null) return "-";
            var sb = new StringBuilder();
            ItemClass gun = PairGun(e);
            Member m = ScRegistry.Loaded ? ScRegistry.GetMember(e.entityId) : null;
            bool standIn = false;
            if (m == null && e.Buffs != null && e.Buffs.HasCustomVar(ScRadio.StandInCvar) && ScRegistry.Loaded)
            {
                m = ScRegistry.GetMember((int)e.Buffs.GetCustomVar(ScRadio.StandInCvar));
                standIn = m != null;
            }
            if (gun == null) sb.Append("no pair gun");
            else
            {
                ItemActionRanged r = RangedAction(gun);
                ItemValue ammo = ItemClass.GetItem(r.MagazineItemNames[0], true);
                sb.Append(gun.GetItemName()).Append(' ').Append(Rounds(e, gun)).Append(" round(s) (resupplied below ").Append(ScConfig.XnpcResupplyBelow).Append(')');
                if (ammo != null && !ammo.IsEmpty() && ammo.ItemClass != null)
                    sb.Append(", ").Append(ammo.ItemClass.GetItemName()).Append(" costs ").Append(PointsPerRound(ammo.ItemClass)).Append(" point(s)");
            }
            string why;
            bool eligible = Eligible(m, e, standIn, out why);
            string where = Where(e.GetBlockPosition());
            sb.Append("; ").Append(eligible ? "resupplied as " : "not resupplied: ").Append(why)
              .Append(", here: ").Append(where ?? "outside the base and the outposts (never resupplied here)");
            float due;
            if (nextResupply.TryGetValue(e.entityId, out due) && due > Time.unscaledTime) sb.Append(", next resupply in ").Append((due - Time.unscaledTime).ToString("0")).Append(" s");
            if (m != null) sb.Append("; record ammo=").Append(m.Ammo != null ? Encode(m.Ammo) : "never recorded");
            return sb.ToString();
        }
    }
}
