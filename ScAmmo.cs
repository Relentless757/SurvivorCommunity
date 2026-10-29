using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using HarmonyLib;
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
    ///
    /// SC's raiders on the same body (M10-D2) get a starting stack in the bag too (RaiderSpawnAmmo / RaiderSpawnArrows,
    /// ScLoadout.AfterPostInit) and nothing ever again: Tick walks members only, and a raider has no record. What is left
    /// goes into the backpack it drops (Patches_Npc).
    ///
    /// A remote player's open inventory window (M10 round 3, design section 5). Ammo left in the window moves to the bag
    /// (MoveWindowAmmoToBag), never while somebody has the window open. The host's own open sets the container's
    /// user-accessing flag; a remote player's does not: SCore sends them a copy (NetPackageHarvestInventoryRequest ->
    /// ...Data) and, when they close it, writes their copy back over every slot (...Update). So the two package handlers
    /// are hooked here and the remote opens kept in a table of SC's own (RemoteOpened / RemoteClosed): while a mark is
    /// up, nothing is moved out of that window, ScMedic neither takes from it nor stocks it and ScArms keeps no weapon
    /// out of it, or the close would bring a moved arrow, a used bandage or a kept weapon back; an NPC that dies under a
    /// mark keeps its window until the mark ends (Patches_Npc DropWindow / Release). A mark ends with the close, the
    /// player's disconnect, a new game, or RemoteOpenStaleSeconds after the open.
    /// The 3.4 build (SCore 3.4): SCore lets one copy out at a time (a refused request opens nothing and is not marked)
    /// and merges a closed copy as a per-item delta on what the server holds; SC's hold-offs stay (the delta merge cannot
    /// tell a round SC moved from one the player took).
    ///
    /// Magazines (M11, with Locked &amp; Loaded 1.4; ScMags). An NPC that carries a magazine for its gun reloads like a
    /// player; one that carries none reloads from loose rounds. So with L&amp;L loaded a gun that takes magazines is armed
    /// with its tier's loaded magazines instead of a loose stack (ScLoadout), Rounds counts the loose rounds plus L&amp;L's
    /// (its magazines and the gun's own count), a member is resupplied through Refill and the stockpile pays only for the
    /// rounds that went in, loose rounds of its gun's ammo in its bag are loaded into its magazines whenever they have
    /// room (LoadLoose: the window's handed-over rounds, an old save's), and the member record carries L&amp;L's text of
    /// every magazine (Member.Mags) beside the loose rounds, so a re-created body gets the same magazines back. A member
    /// whose gun takes magazines but who has none is issued its tier's empty ones where the stockpile reaches (Issue).
    /// Bows, crossbows and guns without magazines - and every gun without L&amp;L - keep the loose rounds as before.
    ///
    /// Rounds in a gun SCore's weapon swap writes over (Patches_Xnpc X12) or that a dead NPC still holds come back as
    /// loose rounds (ReturnLoose); a seated L&amp;L magazine is L&amp;L's to give back, and it empties the gun in doing so.
    /// The gun in the hand - rounds and seated magazine - is saved with the body and put back when it loads (X13,
    /// WriteHeldGun / RestoreHeldGun): SCore saves only its name, so every chunk unload cost it.
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
        /// <summary>The same for the record's magazines (L&amp;L's text, Member.Mags), by entity id.</summary>
        private static readonly Dictionary<int, string> restoreMags = new Dictionary<int, string>();

        /// <summary>
        /// Set by ScSpawn.Create around the creation of a body for a survivor the community already knows, with that
        /// member's record (null = never recorded) and its magazines (null = none). Taken by the next managed body's
        /// PostInit, like ScLoadout.Pending.
        /// </summary>
        public static bool PendingKnown;
        public static Dictionary<string, int> PendingRecord;
        public static string PendingMags;

        // counters for sc xnpc (main thread only)
        private static int resupplies, roundsGiven, bundlesOpened, refusedEmpty, refusedFull, outsideWaits, restored, seeded, raidersSeeded;
        // magazines (M11) and the rounds given back from a gun that was put away or held at death
        private static int armed, raidersArmed, armedRounds, refills, refillRounds, magsFull, looseLoaded, issued, swapReturns, swapRounds, deathRounds;
        // the held gun across a save (review F1): times written, put back in the hand, rounds given back loose instead
        private static int gunsSaved, gunsRestored, gunsLoose;
        /// <summary>
        /// Of gunsSaved, the records written for a body whose chunk had unloaded (its GameObject already destroyed: the
        /// save-on-drop write, M11 fix). Both are bumped by WriteHeldGun, which runs on the chunk-save thread for an unloaded
        /// chunk and on the main thread for a world save - Interlocked, never ++.
        /// </summary>
        private static int gunsSavedUnloaded;

        public static void Reset()
        {
            ammoTypes = null;
            nextCheck = 0f;
            nextResupply.Clear();
            nextRefusalLog.Clear();
            waitingOutside.Clear();
            restore.Clear();
            restoreMags.Clear();
            issuedTo.Clear();
            savedGuns.Clear();
            deferredLoose.Clear();
            remoteOpen.Clear();
            PendingKnown = false;
            PendingRecord = null;
            PendingMags = null;
            resupplies = roundsGiven = bundlesOpened = refusedEmpty = refusedFull = outsideWaits = restored = seeded = raidersSeeded = 0;
            armed = raidersArmed = armedRounds = refills = refillRounds = magsFull = looseLoaded = issued = swapReturns = swapRounds = deathRounds = 0;
            gunsSaved = gunsRestored = gunsLoose = gunsSavedUnloaded = 0;
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
            ItemClass ic = l != null && l.HasRanged ? ItemClass.GetItemClass(l.Ranged, true) : null;
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
        /// swap count as its reserve) plus its magazine while it is in the hand. With Locked &amp; Loaded the gun's own count
        /// comes from L&amp;L's Rounds instead, which adds the rounds in the NPC's magazines for it (XNPC's own counts include
        /// them too); asking both for the gun would count its magazine twice.
        /// </summary>
        public static int Rounds(EntityAlive e, ItemClass gun)
        {
            ItemActionRanged r = RangedAction(gun);
            if (e == null || r == null) return 0;
            int n = Loose(e, r), mags;
            if (ScMags.Active && ScMags.TryRounds(e, gun.GetItemName(), out mags)) return n + mags;
            ItemValue held = e.inventory != null ? e.inventory.holdingItemItemValue : null;
            if (held != null && !held.IsEmpty() && held.type == gun.Id) n += Math.Max(0, (int)held.Meta);
            return n;
        }

        /// <summary>Loose rounds of every one of the gun's ammo types in the stores (toolbelt, bag, inventory window).</summary>
        private static int Loose(EntityAlive e, ItemActionRanged r)
        {
            int n = 0;
            foreach (string name in r.MagazineItemNames)
            {
                ItemValue iv = ItemClass.GetItem(name, true);
                if (iv != null && !iv.IsEmpty()) n += InStores(e, iv.type);
            }
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
            string mags;
            return Carried(e, out mags);
        }

        /// <summary>
        /// The same, and its magazines (M11): when the body reloads this gun by L&amp;L magazine (ScMags.Carries), mags is
        /// L&amp;L's text of every magazine it has for it - the one in the gun included, which is why the gun's own rounds
        /// then stay out of the loose rounds (they would be restored twice). Null mags: no magazines to record.
        /// heldLoose false leaves the gun's own rounds out in any case (Record, while it keeps older magazines that hold them).
        /// </summary>
        public static Dictionary<string, int> Carried(EntityAlive e, out string mags, bool heldLoose = true)
        {
            mags = null;
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
            if (ScMags.Active && ScMags.Carries(e, ScMags.GunValue(e, gun)))
            {
                string text = ScMags.Record(e, gun.GetItemName());
                if (!string.IsNullOrEmpty(text)) mags = text;
            }
            ItemValue held = e.inventory != null ? e.inventory.holdingItemItemValue : null;
            if (heldLoose && mags == null && held != null && !held.IsEmpty() && held.type == gun.Id && held.Meta > 0)
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

        /// <summary>
        /// Writes what this body carries into the member record now (the ticker, and a stand-in about to be removed).
        /// After an L&amp;L exception has switched the adapter off (ScMags.Threw, the review's F2) SC cannot read the magazines
        /// any more, though the body still has them: the record keeps the last magazines it could name instead of losing
        /// them (they can only have been fired from since, so a re-created body gets at most those rounds back), and the
        /// gun's own rounds stay out of the loose part, as they are in those magazines. Without L&amp;L at all (never bound,
        /// nothing thrown) the magazines are cleared as before, once RestoreMagazines has turned them into loose rounds.
        /// </summary>
        public static void Record(Member m, EntityAlive e)
        {
            try
            {
                if (m == null || e == null || !ScXnpc.IsBody(e)) return;
                bool keep = ScMags.Threw && !string.IsNullOrEmpty(m.Mags);
                string mags;
                Dictionary<string, int> rec = Carried(e, out mags, !keep);
                if (rec == null) return;
                m.Ammo = rec;
                if (!keep) m.Mags = mags;
            }
            catch (Exception ex)
            {
                ScLog.Once("ammo-record", ex);
            }
        }

        /// <summary>The marker of the magazines part of the registry form: no item name starts with '@'.</summary>
        private const string MagsPart = "@mags:";

        /// <summary>
        /// The registry form: "item:n,item:n", or "none" for a record of nothing. Magazines (M11) ride in the same value as
        /// one more part, "@mags:" and L&amp;L's text with every ':' ',' ';' '=' '%' and line break %-escaped - the registry
        /// writer turns ';' and '=' into other characters, and an SC from before M11 reading it finds no number after the
        /// part's last ':' and skips it, so the loose rounds of a newer save still load there.
        /// </summary>
        public static string Encode(Dictionary<string, int> rec, string mags)
        {
            var parts = new List<string>();
            if (rec != null) foreach (var kv in rec) if (kv.Value > 0) parts.Add(kv.Key + ":" + kv.Value.ToString(CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(mags)) parts.Add(MagsPart + Escape(mags));
            return parts.Count > 0 ? string.Join(",", parts.ToArray()) : "none";
        }

        public static string Encode(Dictionary<string, int> rec) { return Encode(rec, null); }

        /// <summary>The loose rounds of a registry form; mags gets its magazines (null for none, as in every save from before M11).</summary>
        public static Dictionary<string, int> Decode(string s, out string mags)
        {
            mags = null;
            var rec = new Dictionary<string, int>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(s) || s.Trim() == "none") return rec;
            foreach (string part in s.Split(','))
            {
                string p = part.Trim();
                if (p.StartsWith(MagsPart, StringComparison.Ordinal))
                {
                    string text = Unescape(p.Substring(MagsPart.Length));
                    if (text.Length > 0) mags = text;
                    continue;
                }
                int i = p.LastIndexOf(':');
                int n;
                if (i <= 0 || !int.TryParse(p.Substring(i + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) continue;
                Add(rec, p.Substring(0, i).Trim(), n);
            }
            return rec;
        }

        public static Dictionary<string, int> Decode(string s)
        {
            string mags;
            return Decode(s, out mags);
        }

        private const string EscapedChars = "%:,;=\r\n";

        private static string Escape(string s)
        {
            var sb = new StringBuilder(s.Length + 16);
            foreach (char c in s)
            {
                if (EscapedChars.IndexOf(c) >= 0) sb.Append('%').Append(((int)c).ToString("X2", CultureInfo.InvariantCulture));
                else sb.Append(c);
            }
            return sb.ToString();
        }

        private static string Unescape(string s)
        {
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                int code;
                if (s[i] == '%' && i + 2 < s.Length
                    && int.TryParse(s.Substring(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                {
                    sb.Append((char)code);
                    i += 2;
                }
                else sb.Append(s[i]);
            }
            return sb.ToString();
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
            string mags = PendingMags;
            PendingKnown = false;
            PendingRecord = null;
            PendingMags = null;
            if (!known || !managed || e == null) return;
            restore[e.entityId] = rec;
            if (!string.IsNullOrEmpty(mags)) restoreMags[e.entityId] = mags;
            else restoreMags.Remove(e.entityId);
        }

        /// <summary>
        /// ScLoadout.AfterPostInit, before the starting stack: a body re-created for a survivor the community knows gets
        /// the record's rounds (none when nothing was ever recorded) and its magazines (RestoreMagazines), and is marked as
        /// given its ammo, so it never gets the starting stack - or a new survivor's magazines - a new survivor gets. False
        /// for every other body (a new survivor, a body loaded from the save).
        /// </summary>
        public static bool RestoreRecorded(EntityAlive e)
        {
            Dictionary<string, int> rec;
            if (e == null || e.Buffs == null || !restore.TryGetValue(e.entityId, out rec)) return false;
            restore.Remove(e.entityId);
            string mags;
            if (restoreMags.TryGetValue(e.entityId, out mags)) restoreMags.Remove(e.entityId);
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
            int k = !string.IsNullOrEmpty(mags) ? RestoreMagazines(e, mags) : 0;
            restored++;
            ScLog.Verbose("re-created body " + e.entityId + ": " + (rec == null ? "no ammo record - starts empty" : n + " round(s) from the record (" + Encode(rec) + ")")
                          + (string.IsNullOrEmpty(mags) ? "" : ", " + ScMags.Entries(mags).Count + " magazine(s) holding " + k)
                          + ", no starting stack");
            return true;
        }

        /// <summary>
        /// A record's magazines back as magazine items, one L&amp;L Restore per magazine, so a magazine L&amp;L no longer knows (a
        /// removed kind) - or every one of them, when L&amp;L is not loaded now - comes back as its loose rounds instead of
        /// being lost. The rounds restored either way.
        /// </summary>
        private static int RestoreMagazines(EntityAlive e, string mags)
        {
            int n = 0;
            foreach (ScMags.Entry x in ScMags.Entries(mags))
            {
                int got = ScMags.Active ? ScMags.Restore(e, x.Text) : -1;
                if (got >= x.Rounds) { n += got; continue; }
                int left = x.Rounds - Math.Max(0, got);
                ItemValue ammo = x.Ammo.Length > 0 ? ItemClass.GetItem(x.Ammo, true) : null;
                int loose = ammo != null && !ammo.IsEmpty() ? AddRounds(e, ammo, left) : 0;
                n += Math.Max(0, got) + loose;
                if (loose < left)
                    ScLog.Warn(ScCommunity.DisplayName(e) + " (id " + e.entityId + "): " + (left - loose) + " round(s) of a recorded " + x.Magazine
                               + " could not be given back (no such magazine or ammo now, or no room in the bag)");
            }
            return n;
        }

        /// <summary>A new survivor's (or a raider's, M10-D2) starting stack was given (ScLoadout.AfterPostInit): counted for sc xnpc.</summary>
        public static void NoteSeeded(bool raider) { if (raider) raidersSeeded++; else seeded++; }

        /// <summary>A new survivor or raider got its tier's loaded magazines instead (M11, ScLoadout.AfterPostInit): counted for sc xnpc.</summary>
        public static void NoteArmed(bool raider, int rounds)
        {
            if (raider) raidersArmed++; else armed++;
            armedRounds += Math.Max(0, rounds);
        }

        /// <summary>Rounds of this ammo the bag still has room for: partly filled stacks and empty slots.</summary>
        private static int Room(EntityAlive e, ItemValue ammo)
        {
#if SC_V34
            ItemStack[] items = e.bag != null ? e.bag.ItemGrid.items : null;      // 3.4: the bag's grid (read only)
#else
            ItemStack[] items = e.bag != null ? e.bag.GetSlots() : null;
#endif
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
        /// Ammo left in the inventory window (where SC kept it before 2026-10-01, or put there by hand) goes into the bag, the
        /// one store a bow or crossbow can actually shoot from (see AddRounds); with Locked &amp; Loaded so do magazines for
        /// the pair gun (MoveWindowMagazines). Cheap when there is nothing to move. Never while the window is open, on the
        /// host's screen or a remote player's (IsRemoteOpen).
        /// </summary>
        public static void MoveWindowAmmoToBag(EntityAlive e)
        {
            try
            {
                if (e == null || e.bag == null || e.IsDead() || !ScXnpc.IsBody(e) || !HarvestManager.Has(e.entityId)) return;
                SCoreLootContainer window = HarvestManager.GetOrCreate(e.entityId);
                // never while someone has the window open: the loot grid shows copies and refreshes only on a change
                // notice, so arrows moved under it would stay on screen and could be dragged out a second time
                if (window == null || window.IsUserAccessing()) return;
                // nor while a remote player has their copy of it open: closing it writes that copy back over every slot
                if (IsRemoteOpen(e.entityId)) { Patches_Xnpc.Count("window-move-held"); return; }
                ItemStack[] items = window.items;
                if (items == null) return;
                int moved = 0;
                for (int i = 0; i < items.Length; i++)
                {
                    ItemStack s = items[i];
                    if (s == null || s.IsEmpty() || s.itemValue.ItemClass == null || !IsAmmo(s.itemValue.ItemClass)) continue;
                    int k = Math.Min(s.count, Room(e, s.itemValue));
                    if (k <= 0) continue;
                    if (!e.bag.AddItem(new ItemStack(s.itemValue.Clone(), k))) continue;
                    moved += k;
#if SC_V34
                    // 3.4: the window's slots are bound to its grid - emptied in place (SCore's own SetSlot), never replaced in
                    // the array, which would cut the slot off from the grid with no change event
                    if (k >= s.count) HarvestManager.SetSlot(items, i, null);
#else
                    if (k >= s.count) items[i] = ItemStack.Empty.Clone();
#endif
                    else s.count -= k;
                }
                int mags = MoveWindowMagazines(e, items);
                if (moved > 0 || mags > 0)
                {
#if SC_V34
                    window.Parent?.NotifyListeners();      // 3.4: SCoreLootContainer is a TEFeatureStorage; its listeners are the parent's
#else
                    window.NotifyListeners();
#endif
                    ScLog.Verbose("moved " + moved + " round(s) and " + mags + " magazine(s) of " + ScCommunity.DisplayName(e) + "'s from the inventory window into the bag");
                }
            }
            catch (Exception ex)
            {
                ScLog.Once("ammo-window-to-bag", ex);
            }
        }

        /// <summary>
        /// Locked &amp; Loaded magazines for the pair gun that were handed over through the window go into the bag too (the
        /// review's open item 2): L&amp;L looks for an NPC's magazines in its bag and toolbelt only, so in the window they were
        /// never used. Each stack moves as it is (a magazine is Stacknumber 1; the clone keeps its rounds, ammo type and
        /// L&amp;L's metadata) while the bag takes it; magazines of other guns stay. Before the first one lands, a body that
        /// does not reload by magazine yet gives the rounds loaded in its gun back to the bag (UnloadHeld, as Issue does):
        /// once a magazine is in the bag, L&amp;L takes rounds in an unflagged gun for a found gun's seated magazine, one more
        /// than was handed over. If the bag has no room for those rounds, no magazine moves this time; one the bag has no
        /// room for stays in the window. The caller's open-window guards apply; LoadLoose, run next at every call site,
        /// fills the magazines from the loose rounds. Returns the magazines moved.
        /// </summary>
        private static int MoveWindowMagazines(EntityAlive e, ItemStack[] items)
        {
            if (!ScMags.Active) return 0;
            ItemClass gun = PairGun(e);
            HashSet<string> names = gun != null ? ScMags.MagazineNames(gun.GetItemName()) : null;
            if (names == null || names.Count == 0) return 0;
            int moved = 0;
            bool checkedGun = false;
            for (int i = 0; i < items.Length; i++)
            {
                ItemStack s = items[i];
                if (s == null || s.IsEmpty() || s.itemValue.ItemClass == null || !names.Contains(s.itemValue.ItemClass.GetItemName())) continue;
                if (!checkedGun)
                {
                    checkedGun = true;
                    if (!ScMags.Carries(e, ScMags.GunValue(e, gun)))
                    {
                        UnloadHeld(e, gun, "magazines handed over");
                        ItemValue held = e.inventory != null ? e.inventory.holdingItemItemValue : null;
                        if (held != null && held.type == gun.Id && held.Meta > 0) return 0;     // the bag is full: not now
                    }
                }
                if (!e.bag.AddItem(new ItemStack(s.itemValue.Clone(), s.count))) break;
                moved += s.count;
#if SC_V34
                HarvestManager.SetSlot(items, i, null);      // 3.4: emptied in place (see MoveWindowAmmoToBag)
#else
                items[i] = ItemStack.Empty.Clone();
#endif
            }
            if (moved > 0) Patches_Xnpc.Count("mags-window-to-bag");
            return moved;
        }

        /// <summary>A magazine item of some gun, bow or launcher (what a ranged weapon loads), not any consumable.</summary>
        private static bool IsAmmo(ItemClass ic)
        {
            if (ammoTypes == null)
            {
                ammoTypes = new HashSet<int>();
                foreach (ItemClass c in ItemClass.list)
                {
                    ItemActionRanged r = c != null ? RangedAction(c) : null;
                    if (r == null || r.MagazineItemNames == null) continue;
                    foreach (string n in r.MagazineItemNames)
                    {
                        ItemClass a = ItemClass.GetItemClass(n, true);
                        if (a != null) ammoTypes.Add(a.Id);
                    }
                }
            }
            return ammoTypes.Contains(ic.Id);
        }
        private static HashSet<int> ammoTypes;

        /// <summary>
        /// Puts rounds into the BAG in stack-sized pieces; returns how many went in. Not the inventory window: SCore's NPC
        /// reload does draw from the window, but vanilla's third-person reload state (Animator3PRangedReloadState) makes the
        /// arrow or bolt a bow/crossbow actually shoots only for ammo it counts in the bag and the toolbelt - so with the
        /// rounds in the window a reload used one up and the release fired nothing (measured 2026-10-01: arrows drained, no
        /// hits, while 0-XNPCCore's own bow survivor, ammo in its bag, hit). Guns don't need the object, but one store for
        /// all keeps the counts simple. V4 saves the bag with the entity (EntityCreationData).
        /// </summary>
        internal static int AddRounds(EntityAlive e, ItemValue ammo, int count)
        {
            if (ammo.ItemClass == null || e == null || e.bag == null) return 0;
            int stack = Math.Max(1, ammo.ItemClass.Stacknumber.Value);
            count = Math.Min(count, Room(e, ammo));
            int added = 0;
            while (added < count)
            {
                int k = Math.Min(stack, count - added);
                if (!e.bag.AddItem(new ItemStack(ammo.Clone(), k))) break;
                added += k;
            }
            return added;
        }

        // ------------------------------------------------------------------ magazines and a gun's own rounds (M11) ----

        /// <summary>
        /// Loose rounds of the pair gun's ammo in the bag go into the body's L&amp;L magazines for it (Refill: same-ammo part-
        /// loaded ones first, then empty ones), and exactly what went in leaves the bag - a body that reloads by magazine never
        /// touches loose rounds again, so they would be dead weight: rounds handed over through the window (moved to the bag
        /// first, MoveWindowAmmoToBag), an old save's loose stack, a gun's rounds given back by a swap. Runs where the window
        /// is moved: ScAmmo.Tick every 5 s for members, the remote window's close, and once at load for every body
        /// (ScLoadout.AfterPostInit). Never while the window is open anywhere (IsWindowOpen, as for the move), never for a
        /// body that has no magazines for the gun (it reloads from the loose rounds), host only. The bag is counted, the
        /// magazines take at most that, and the bag gives up exactly what they took, so no round is made or lost.
        /// </summary>
        public static int LoadLoose(EntityAlive e)
        {
            try
            {
                if (e == null || e.bag == null || e.IsDead() || !ScCommunity.IsHost || !ScXnpc.IsBody(e) || !ScMags.Active) return 0;
                ItemClass gun = PairGun(e);
                ItemActionRanged r = RangedAction(gun);
                if (r == null || !ScMags.Carries(e, ScMags.GunValue(e, gun))) return 0;
                if (IsWindowOpen(e.entityId)) { Patches_Xnpc.Count("mags-load-held"); return 0; }
                int total = 0;
                var seen = new HashSet<int>();
                foreach (string name in r.MagazineItemNames)
                {
                    ItemValue ammo = ItemClass.GetItem(name, true);
                    if (ammo == null || ammo.IsEmpty() || ammo.ItemClass == null || !seen.Add(ammo.type)) continue;
                    int have = InBag(e, ammo.type);
                    if (have <= 0) continue;
                    int k = ScMags.Refill(e, gun.GetItemName(), ammo.ItemClass.GetItemName(), have);
                    if (k <= 0) continue;
                    int taken = e.bag.DecItem(ammo, k);
                    if (taken != k) ScLog.Err(ScCommunity.DisplayName(e) + " (id " + e.entityId + "): " + k + " " + ammo.ItemClass.GetItemName()
                                              + " went into magazines but the bag gave up " + taken);
                    total += k;
                }
                if (total > 0)
                {
                    looseLoaded += total;
                    ScLog.Verbose("loaded " + total + " loose round(s) from " + ScCommunity.DisplayName(e) + "'s bag into their " + gun.GetItemName() + " magazines");
                }
                return total;
            }
            catch (Exception ex)
            {
                ScLog.Once("ammo-load-loose", ex);
                return 0;
            }
        }

        /// <summary>Empty slots in the bag (a magazine takes one each).</summary>
        private static int FreeBagSlots(EntityAlive e)
        {
            int n = 0;
#if SC_V34
            ItemStack[] slots = e.bag != null ? e.bag.ItemGrid.items : null;
#else
            ItemStack[] slots = e.bag != null ? e.bag.GetSlots() : null;
#endif
            if (slots != null) foreach (ItemStack s in slots) if (s == null || s.IsEmpty()) n++;
            return n;
        }

        /// <summary>Rounds of one ammo item in the bag alone (what Bag.DecItem takes from).</summary>
        private static int InBag(EntityAlive e, int type)
        {
            int n = 0;
#if SC_V34
            ItemStack[] slots = e.bag != null ? e.bag.ItemGrid.items : null;
#else
            ItemStack[] slots = e.bag != null ? e.bag.GetSlots() : null;
#endif
            if (slots == null) return 0;
            foreach (ItemStack s in slots)
                if (s != null && !s.IsEmpty() && s.itemValue != null && s.itemValue.type == type) n += s.count;
            return n;
        }

        /// <summary>
        /// The rounds loaded in a ranged weapon (its Meta) go to the body's bag as loose rounds of its selected ammo, and the
        /// weapon's count drops by what went in - in place, on this very ItemValue, so whoever still holds it (SCore's
        /// thrown-charge stand-in, ScMedic's displaced weapon) cannot bring them back a second time. gone: the weapon is
        /// leaving for good (written over by a swap, or held by a dead NPC), so what the bag has no room for drops at the
        /// body and the count goes to 0; otherwise it stays in the weapon. Returns the rounds given back.
        /// </summary>
        public static int ReturnLoose(EntityAlive e, ItemValue gun, string why, bool gone)
        {
            if (e == null || gun == null || gun.IsEmpty() || gun.Meta <= 0) return 0;
            ItemActionRanged r = RangedAction(gun.ItemClass);
            if (r == null) return 0;
            int idx = gun.SelectedAmmoTypeIndex < r.MagazineItemNames.Length ? gun.SelectedAmmoTypeIndex : 0;
            ItemValue ammo = ItemClass.GetItem(r.MagazineItemNames[idx], true);
            if (ammo == null || ammo.IsEmpty() || ammo.ItemClass == null) return 0;
            int n = gun.Meta;
            int added = AddRounds(e, ammo, n);
            int dropped = 0;
            if (gone && added < n)
            {
                dropped = n - added;
                GameManager.Instance.ItemDropServer(new ItemStack(ammo.Clone(), dropped), e.position + Vector3.up, Vector3.zero, -1, 60f);
            }
            gun.Meta = gone ? 0 : n - added;
            if (added + dropped > 0)
                ScLog.Verbose(why + ": " + ScCommunity.DisplayName(e) + " (id " + e.entityId + ")'s " + gun.ItemClass.GetItemName() + " gave back " + added
                              + " " + ammo.ItemClass.GetItemName() + " into the bag" + (dropped > 0 ? " and dropped " + dropped + " (the bag is full)" : ""));
            return added + dropped;
        }

        /// <summary>A weapon swap gave back the rounds of the gun it put away (Patches_Xnpc X12): counted for sc xnpc.</summary>
        public static void NoteSwapReturn(int n) { swapReturns++; swapRounds += n; }

        /// <summary>A dead NPC's held gun gave back its rounds before the death drop (Patches_Npc): counted for sc xnpc.</summary>
        public static void NoteDeathReturn(int n) { deathRounds += n; }

        /// <summary>
        /// Before a body is given magazines for its gun (armed at spawn, or issued empties): rounds loaded in the gun in its
        /// hand go back to the bag as loose rounds (LoadLoose then puts them into the magazines). L&amp;L treats a gun that
        /// holds rounds and no seated magazine as a found gun whose rounds are a magazine, so leaving them would hand out
        /// one magazine more than the tier says. Left alone when L&amp;L already has the body reloading this gun by magazine
        /// (ScMags.Carries: a seated magazine's rounds are that magazine's); neither caller gets there with one.
        /// </summary>
        public static int UnloadHeld(EntityAlive e, ItemClass gun, string why)
        {
            ItemValue held = e != null && e.inventory != null ? e.inventory.holdingItemItemValue : null;
            if (gun == null || held == null || held.IsEmpty() || held.type != gun.Id || held.Meta <= 0) return 0;
            if (ScMags.Active && ScMags.Carries(e, held)) return 0;
            return ReturnLoose(e, held, why, false);
        }

        // ------------------------------------------------------------------ the held gun across a save (review F1) ----

        /// <summary>"SCM1", little-endian: the start of SC's held-gun record after SCore's V4 section in an entity's save data.</summary>
        internal const uint SavedGunMarker = 0x314D4353;
        internal const byte SavedGunVersion = 1;
        /// <summary>marker (4) + version (1) + payload length (4).</summary>
        internal const int SavedGunHeaderBytes = 9;

        /// <summary>What a body's save said about its hand: the gun (null: none worth keeping) and a medical item in use.</summary>
        private sealed class SavedHandEntry
        {
            public int Frame;
            public ItemValue Gun;
            public string UseItem;
        }

        /// <summary>
        /// Held guns read from the save, by entity id, with the frame they were read in (Patches_Xnpc X13). An entry is good
        /// only for the PostInit that follows its Read in the same EntityFactory.CompleteEntity call - the same frame - so
        /// AfterPostInit takes its own and drops any from an earlier frame (a body whose creation stopped before PostInit).
        /// Cleared per game. Host only, main thread.
        /// </summary>
        private static readonly Dictionary<int, SavedHandEntry> savedGuns = new Dictionary<int, SavedHandEntry>();

        /// <summary>
        /// Runner re-creations whose bag ScRunners replaces with the snapshot's right after PostInit (the re-check's R1): the
        /// saved gun's loose fallback waits for that (ScLoadout.AfterRunnerRestore) instead of going into a bag about to be thrown
        /// away. Main thread.
        /// </summary>
        private static readonly Dictionary<int, ItemValue> deferredLoose = new Dictionary<int, ItemValue>();

        /// <summary>
        /// X13's Write postfix: the gun in an SC body's hand - with its loaded rounds and, with L&amp;L, its seated magazine
        /// (Meta and the gun's metadata) - goes into the entity's save data after SCore's own section, which saves only the
        /// gun's name (so every load, a chunk's as well as a game's, gave back an empty gun and lost a magazine per body).
        /// Framed as marker, version, length and a payload of ItemValue.Write's bytes plus the medical item in use (""
        /// for none): SCore's Read stops at the end of its own length-framed section and never sees them, vanilla keeps an
        /// entity's data as one length-prefixed blob, and an SC without this, or SCore alone, ignores them.
        /// While a medical use is in progress (ScMedic.TryDisplaced: the hand holds the item) the record is the weapon the
        /// use displaced, and the item - so the load can put both back (ScMedic.FixLoadedHand). Otherwise nothing for a melee
        /// weapon (the gun's rounds are in the bag then: X12, L&amp;L) or an empty, unflagged gun. Written on network writes
        /// too (chunk saves and unloads use the same flag, see the report); a client's Read skips it. Skipped, with a
        /// warning, if it would take the record over vanilla's 65535-byte limit. Reads only the entity's own inventory,
        /// SC's immutable class sets and ScMedic's concurrent mirror, no Unity API: an unloaded chunk is saved on the save
        /// thread.
        ///
        /// The M11 fix: an unloaded chunk is written frames AFTER Entity.OnEntityUnload destroyed the body's GameObject
        /// (Object.Destroy; the chunk only reaches RegionFileManager's save queue on a later DetermineChunksToLoad), and
        /// Unity's == null is true for a destroyed MonoBehaviour. The first guard used to be that test, so every chunk
        /// unload silently skipped the record and the gun came back empty; it is a reference test now, like ScXnpc.IsBody.
        /// Everything read below is managed data that outlives the GameObject. The two Unity operators that remain on this
        /// path - ConnectionManager's != null in IsHost and !e for the unloaded counter - are UnityEngine.Object's
        /// CompareBaseObjects / op_Implicit, which only compare the cached native pointer (m_CachedPtr): pure managed code
        /// that never throws and is safe on any thread. No Patches_Xnpc.Count here (a plain Dictionary, main thread only).
        /// </summary>
        public static void WriteHeldGun(EntityAlive e, System.IO.BinaryWriter bw)
        {
            if ((object)e == null || bw == null || e.inventory == null || !ScCommunity.IsHost || !ScXnpc.IsBody(e)) return;
            ItemValue gun = e.inventory.holdingItemItemValue;
            string useItem = null;
            ScMedic.Displaced d;
            if ((gun == null || gun.IsEmpty() || RangedAction(gun.ItemClass) == null) && ScMedic.TryDisplaced(e.entityId, out d))
            {
                gun = d.Weapon;
                useItem = d.Item;
            }
            bool keepGun = gun != null && !gun.IsEmpty() && RangedAction(gun.ItemClass) != null
                           && (gun.Meta > 0 || (gun.Metadata != null && gun.Metadata.Count > 0));
            if (!keepGun && useItem == null) return;
            byte[] payload;
            using (var ms = new System.IO.MemoryStream())
            {
#if SC_V34
                // 3.4: ItemValue writes to the game's pooled writer only - the same bytes as 3.2's (ItemValue version 9,
                // UTF-8 strings with a 7-bit length), so a 3.2 save's record reads back. AllocSync: this can run on the
                // chunk-save thread.
                using (PooledBinaryWriter w = MemoryPools.poolBinaryWriter.AllocSync(false))
                {
                    w.SetBaseStream(ms);
                    ItemValue.Write(keepGun ? gun : null, w);
                    w.Write(useItem ?? "");
                    w.Flush();
                }
#else
                var w = new System.IO.BinaryWriter(ms);
                ItemValue.Write(keepGun ? gun : null, w);
                w.Write(useItem ?? "");
                w.Flush();
#endif
                payload = ms.ToArray();
            }
            long used = bw.BaseStream != null && bw.BaseStream.CanSeek ? bw.BaseStream.Position : 0;
            if (used + SavedGunHeaderBytes + payload.Length > ushort.MaxValue)
            {
                // e.EntityName, not ScCommunity.DisplayName: that one's Unity null tests answer "?" for a destroyed body
                ScLog.Once("ammo-held-gun-size-" + e.entityId, e.EntityName + " (id " + e.entityId + "): the gun in hand is not saved with its rounds - the save record is too big");
                return;
            }
            WriteSavedGun(bw, payload);
            System.Threading.Interlocked.Increment(ref gunsSaved);
            if (!e) System.Threading.Interlocked.Increment(ref gunsSavedUnloaded);   // destroyed = its chunk unloaded (see above)
        }

        /// <summary>The record's bytes after SCore's section (split out for the offline check).</summary>
        internal static void WriteSavedGun(System.IO.BinaryWriter bw, byte[] payload)
        {
            bw.Write(SavedGunMarker);
            bw.Write(SavedGunVersion);
            bw.Write(payload.Length);
            bw.Write(payload);
        }

        /// <summary>
        /// The held gun written by WriteHeldGun (null when there was none worth keeping) and the medical item in use (null
        /// for none), read where SCore's Read stopped. False when the next bytes are not one (a save from before this, a
        /// network read, SCore's data alone). The stream is the entity's own data blob, so a record that does not parse costs
        /// only this gun: a later SC's version is skipped, a bad length ends the read.
        /// </summary>
        internal static bool ReadSavedGun(System.IO.BinaryReader br, out ItemValue gun, out string useItem)
        {
            gun = null;
            useItem = null;
            System.IO.Stream s = br != null ? br.BaseStream : null;
            if (s == null || !s.CanSeek) return false;
            long start = s.Position;
            if (s.Length - start < SavedGunHeaderBytes) return false;
            if (br.ReadUInt32() != SavedGunMarker) { s.Position = start; return false; }
            byte version = br.ReadByte();
            int len = br.ReadInt32();
            if (len <= 0 || len > s.Length - s.Position) { s.Position = s.Length; return false; }
            byte[] payload = br.ReadBytes(len);
            if (version != SavedGunVersion) return false;
            var iv = new ItemValue();
            try
            {
                using (var ms = new System.IO.MemoryStream(payload))
                {
#if SC_V34
                    // 3.4: the pooled reader (it reads exactly what is asked, so ms.Position still marks the end of the gun)
                    using (PooledBinaryReader r = MemoryPools.poolBinaryReader.AllocSync(false))
                    {
                        r.SetBaseStream(ms);
                        iv.Read(r);
                        string item = ms.Position < ms.Length ? r.ReadString() : "";
                        if (item.Length > 0) useItem = item;
                    }
#else
                    var r = new System.IO.BinaryReader(ms);
                    iv.Read(r);
                    string item = ms.Position < ms.Length ? r.ReadString() : "";
                    if (item.Length > 0) useItem = item;
#endif
                }
            }
            catch (Exception ex)
            {
                ScLog.Once("ammo-held-gun-read", "a saved held gun could not be read (" + ex.GetType().Name + ") - the NPC loads with an empty gun");
                return false;
            }
            gun = iv.IsEmpty() ? null : iv;
            return gun != null || useItem != null;
        }

        /// <summary>X13's Read postfix: keeps a held gun read from an SC body's save for its PostInit (host only).</summary>
        public static void KeepHeldGun(EntityAlive e, System.IO.BinaryReader br)
        {
            if (e == null || br == null || !ScCommunity.IsHost || !ScXnpc.IsBody(e)) return;
            ItemValue gun;
            string useItem;
            if (ReadSavedGun(br, out gun, out useItem))
                savedGuns[e.entityId] = new SavedHandEntry { Frame = Time.frameCount, Gun = gun, UseItem = useItem };
        }

        /// <summary>
        /// What this body's save said about its hand, for ScMedic.FixLoadedHand (it runs before RestoreHeldGun, which then
        /// consumes the entry): false when it was not loaded from a save with X13's record in this frame.
        /// </summary>
        internal static bool SavedHand(int id, out ItemValue gun, out string useItem)
        {
            gun = null;
            useItem = null;
            SavedHandEntry entry;
            if (!savedGuns.TryGetValue(id, out entry) || entry.Frame != Time.frameCount) return false;
            gun = entry.Gun;
            useItem = entry.UseItem;
            return true;
        }

        /// <summary>
        /// ScLoadout.AfterPostInit, before the starting ammo (after ScMedic.FixLoadedHand): the gun this body held when it
        /// was saved goes back into its hand, with its rounds and L&amp;L's seated magazine, in place of the empty one SCore's
        /// load re-equipped by name - when that is the same gun and still empty (Inventory.SetItem stores a copy, L&amp;L's
        /// SetItem prefix sees an empty unflagged gun and does nothing). Otherwise (the hand holds something else, or a gun
        /// with rounds already) its rounds come back loose into the bag (ReturnLoose; a seated magazine's item cannot be
        /// rebuilt then, its rounds can) - for a runner whose bag ScRunners is about to replace, once it has (runnerRestore).
        /// Each saved gun is used once: re-created bodies (ScSpawn) are never read from a save and have no entry, and the
        /// bag, X12 and the death drop act on the live gun only afterwards.
        /// </summary>
        public static void RestoreHeldGun(EntityAlive e, bool runnerRestore)
        {
            if (e == null || e.inventory == null) return;
            deferredLoose.Remove(e.entityId);        // one from an earlier attempt at this body that never got its flush
            if (savedGuns.Count == 0) return;
            SavedHandEntry entry;
            bool got = savedGuns.TryGetValue(e.entityId, out entry);
            savedGuns.Remove(e.entityId);
            int frame = Time.frameCount;
            if (savedGuns.Count > 0)
                foreach (int id in new List<int>(savedGuns.Keys)) if (savedGuns[id].Frame != frame) savedGuns.Remove(id);
            if (!got || entry.Frame != frame || entry.Gun == null) return;
            ItemValue saved = entry.Gun;
            ItemValue held = e.inventory.holdingItemItemValue;
            bool emptySameGun = held != null && !held.IsEmpty() && held.type == saved.type && held.Meta <= 0
                                && (held.Metadata == null || held.Metadata.Count == 0);
            if (emptySameGun)
            {
                e.inventory.SetItem(e.inventory.holdingItemIdx, saved, 1);
#if SC_V34
                // 3.4: the slot write itself re-seats the held item (the toolbelt grid's SlotChanged -> Hand); OnUpdate and
                // ForceHoldingItemUpdate are gone
#else
                e.inventory.OnUpdate();
                e.inventory.ForceHoldingItemUpdate();
#endif
                gunsRestored++;
                ScLog.Verbose(ScCommunity.DisplayName(e) + " (id " + e.entityId + ") holds its saved " + saved.ItemClass.GetItemName() + " again, " + saved.Meta + " round(s) in it");
                return;
            }
            if (runnerRestore) { deferredLoose[e.entityId] = saved; return; }
            int n = ReturnLoose(e, saved, "loaded from the save", true);
            gunsLoose += n;
        }

        /// <summary>
        /// ScLoadout.AfterRunnerRestore (the re-check's R1): the saved gun's loose fallback RestoreHeldGun held back for a
        /// runner, into the snapshot bag ScRunners has just put back.
        /// </summary>
        public static void FlushDeferredLoose(EntityAlive e)
        {
            ItemValue saved;
            if (e == null || !deferredLoose.TryGetValue(e.entityId, out saved)) return;
            deferredLoose.Remove(e.entityId);
            gunsLoose += ReturnLoose(e, saved, "loaded from the save", true);
        }

        // ------------------------------------------------------------------ a remote player's open window ----

        /// <summary>
        /// How long a remote open counts without a close (s). SCore sends the close on every way the client's loot window
        /// shuts - by hand, vanilla's auto-close 32 m from the container (XUiC_LootWindow.Update), the player's death - and
        /// a disconnect ends the mark through ModEvents.PlayerDisconnected, so this only ends a mark whose close never comes
        /// while the player stays connected: their window never opened (SCore's Data handler found no local player) or
        /// SCore's close patch skipped it. Ten minutes is far longer than anyone keeps an NPC's inventory open, so a live
        /// window is not let go early (that would bring the duplication back), and a stuck mark only delays that NPC's
        /// window-to-bag move, its medical items and its weapon-menu swap - or, for an NPC that died meanwhile, the drop
        /// of its window at the body (swept once a second, Patches_Npc) - for that long; a resupply goes to the bag and is
        /// never held.
        /// </summary>
        public const float RemoteOpenStaleSeconds = 600f;

        /// <summary>
        /// NPC entity id -> (remote player entity id -> real time of the open). Written by the server's package handlers
        /// only, so it stays empty on a client and in single player. Main thread: packages are processed there.
        /// </summary>
        private static readonly Dictionary<int, Dictionary<int, float>> remoteOpen = new Dictionary<int, Dictionary<int, float>>();

        /// <summary>A remote player asked the server for this NPC's inventory: their copy opens on their screen.</summary>
        public static void RemoteOpened(int npc, int player)
        {
            Dictionary<int, float> by;
            if (!remoteOpen.TryGetValue(npc, out by)) remoteOpen[npc] = by = new Dictionary<int, float>();
            by[player] = Time.unscaledTime;
            Patches_Xnpc.Count("window-remote-open");
            ScLog.Verbose("remote window: player " + player + " opened NPC " + npc + "'s inventory - nothing is taken out of it until they close it");
        }

        /// <summary>That player's copy came back and is the window now (player &lt; 0: every mark on the NPC).</summary>
        public static void RemoteClosed(int npc, int player)
        {
            Patches_Xnpc.Count("window-remote-close");
            Dictionary<int, float> by;
            if (!remoteOpen.TryGetValue(npc, out by)) return;
            if (player < 0) by.Clear();
            else by.Remove(player);
            if (by.Count == 0) remoteOpen.Remove(npc);
        }

        /// <summary>
        /// Does a remote player have this NPC's inventory window open? A mark older than RemoteOpenStaleSeconds is dropped
        /// here, with a log line. Always false on a client and in single player (see remoteOpen).
        /// </summary>
        public static bool IsRemoteOpen(int npc)
        {
            Dictionary<int, float> by;
            if (remoteOpen.Count == 0 || !remoteOpen.TryGetValue(npc, out by)) return false;
            float now = Time.unscaledTime;
            List<int> stale = null;
            foreach (var kv in by)
                if (now - kv.Value >= RemoteOpenStaleSeconds) (stale ?? (stale = new List<int>())).Add(kv.Key);
            if (stale != null)
            {
                foreach (int player in stale)
                {
                    by.Remove(player);
                    Patches_Xnpc.Count("window-remote-expired");
                    ScLog.Warn("remote window: player " + player + " opened NPC " + npc + "'s inventory " + RemoteOpenStaleSeconds.ToString("0")
                               + " s ago and never closed it - taken as closed");
                }
            }
            if (by.Count > 0) return true;
            remoteOpen.Remove(npc);
            return false;
        }

        /// <summary>
        /// Is this NPC's inventory window open anywhere: on the host's own screen (the container's user-accessing flag) or
        /// on a remote player's (IsRemoteOpen)? False without a window. SC's own writes to a window (ScNpc.SetItem /
        /// AddItem -&gt; SCore's UpdateSlot) send no change notice, so even the host's open grid would keep showing - and
        /// let the host take - an item ScMedic or ScArms just took out: both hold off while this is true.
        /// </summary>
        public static bool IsWindowOpen(int npc)
        {
            if (IsRemoteOpen(npc)) return true;
            if (!HarvestManager.Has(npc)) return false;
            SCoreLootContainer w = HarvestManager.GetOrCreate(npc);
            return w != null && w.IsUserAccessing();
        }

        /// <summary>
        /// ModEvents.PlayerDisconnected: a player who left with a window open will never close it. A dead NPC's window
        /// they held (Patches_Npc DropWindow) drops at the body now if nobody else holds it.
        /// </summary>
        public static void ForgetPlayer(int player)
        {
            if (remoteOpen.Count == 0) return;
            var lost = new List<int>();
            foreach (var kv in remoteOpen)
            {
                if (!kv.Value.Remove(player)) continue;
                Patches_Xnpc.Count("window-remote-disconnect");
                ScLog.Verbose("remote window: player " + player + " left with NPC " + kv.Key + "'s inventory open - mark dropped");
                lost.Add(kv.Key);
            }
            foreach (int npc in lost)
            {
                Dictionary<int, float> by;
                if (remoteOpen.TryGetValue(npc, out by) && by.Count == 0) remoteOpen.Remove(npc);
                Patches_Npc.Patch_SetDead.Release(npc, false);
            }
        }

        /// <summary>Remote opens still marked (stale ones dropped first), for sc xnpc.</summary>
        private static int RemoteOpenCount()
        {
            int n = 0;
            foreach (int npc in new List<int>(remoteOpen.Keys))
            {
                Dictionary<int, float> by;
                if (IsRemoteOpen(npc) && remoteOpen.TryGetValue(npc, out by)) n += by.Count;
            }
            return n;
        }

        /// <summary>Who has this NPC's window open remotely and for how long, for sc xnpc &lt;id&gt;: "none" or "player 171 for 12 s".</summary>
        public static string RemoteOpenState(int npc)
        {
            Dictionary<int, float> by;
            if (!IsRemoteOpen(npc) || !remoteOpen.TryGetValue(npc, out by)) return "none";
            var parts = new List<string>();
            foreach (var kv in by) parts.Add("player " + kv.Key + " for " + (Time.unscaledTime - kv.Value).ToString("0") + " s");
            return string.Join(", ", parts.ToArray());
        }

        /// <summary>SCore's handler of a remote player's request for an NPC's inventory (runs on the server, sends them the copy).</summary>
        public static MethodBase RemoteOpenTarget()
        {
            return AccessTools.DeclaredMethod(typeof(NetPackageHarvestInventoryRequest), "ProcessPackage", new[] { typeof(World), typeof(GameManager) });
        }

        /// <summary>SCore's handler of the copy a remote player sends back on close (runs on the server, writes it over every slot).</summary>
        public static MethodBase RemoteCloseTarget()
        {
            return AccessTools.DeclaredMethod(typeof(NetPackageHarvestInventoryUpdate), "ProcessPackage", new[] { typeof(World), typeof(GameManager) });
        }

        /// <summary>
        /// Resolves a target for a Prepare: an error line when it is gone (an SCore update), and never a throw inside
        /// PatchAll, which would silently drop every patch class after this one (CLAUDE.md pattern #5).
        /// </summary>
        private static bool Bound(Func<MethodBase> target, string label)
        {
            try
            {
                if (target() != null) return true;
                ScLog.Err(label + " not found (SCore update?) - a remote player's open NPC inventory window no longer holds off the ammo move and"
                          + " medical use out of it, so items in it can be duplicated on a dedicated server");
            }
            catch (Exception e)
            {
                ScLog.Once("ammo-bind-" + label, e);
            }
            return false;
        }

        /// <summary>The remote player behind a package: its sender (the server's own record of the connection), else the id it carries.</summary>
        private static int PlayerOf(NetPackage p, int carried)
        {
            return p != null && p.Sender != null && p.Sender.entityId >= 0 ? p.Sender.entityId : carried;
        }

        /// <summary>
        /// A remote player opens an NPC's inventory window (a dedicated server's client, or another player on a listen
        /// server: SCore's EntityUtilities sends the request whenever the opener is not the server). Marked for whatever
        /// NPC it is; MoveWindowAmmoToBag and ScMedic only ask about our bodies. ____npcEntityId and ____playerId are
        /// SCore's private fields _npcEntityId and _playerId (Harmony's three-underscore field injection).
        /// </summary>
        [HarmonyPatch]
        public static class Patch_RemoteWindowOpen
        {
            [HarmonyPrepare]
            public static bool Prepare() { return Bound(RemoteOpenTarget, "NetPackageHarvestInventoryRequest.ProcessPackage(World, GameManager)"); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return RemoteOpenTarget(); }

            [HarmonyPostfix]
            public static void Postfix(NetPackage __instance, World world, int ____npcEntityId, int ____playerId)
            {
                try
                {
                    if (world == null || !ScCommunity.IsHost) return;
#if SC_V34
                    // 3.4 (SCore 3.4): one copy out at a time - a request the server refused (somebody else, or the host's own
                    // grid, has it open: NetPackageHarvestInventoryData.SetupInUse) opens nothing, and marking it would hold
                    // that NPC's ammo move, medical use and weapon swap for RemoteOpenStaleSeconds. Marked only when this
                    // player's session is the one SCore just began.
                    int player = PlayerOf(__instance, ____playerId);
                    if (!HarvestManager.HasRemoteSession(____npcEntityId) || HarvestManager.IsOpenByOther(____npcEntityId, player))
                    {
                        Patches_Xnpc.Count("window-remote-refused");
                        return;
                    }
                    RemoteOpened(____npcEntityId, player);
#else
                    RemoteOpened(____npcEntityId, PlayerOf(__instance, ____playerId));
#endif
                }
                catch (Exception ex)
                {
                    ScLog.Once("ammo-remote-open", ex);
                }
            }
        }

        /// <summary>
        /// The remote player closed it: SCore has just written their copy over every slot, so the mark comes down and the
        /// ammo they left in it (handed over, say) moves to the bag at once - unless somebody else still has it open.
        /// The NPC died while it was open (Patches_Npc DropWindow kept the window): what is left drops at the body now,
        /// once nobody holds it. And when SCore's handler had to make a new container because the NPC's window was
        /// already gone - its owner dead or no longer in the world, its window dropped (after an expiry, say) - that copy
        /// has nowhere to go and is removed again, so no orphan entry for a dead id is left in HarvestManager.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_RemoteWindowClose
        {
            [HarmonyPrepare]
            public static bool Prepare() { return Bound(RemoteCloseTarget, "NetPackageHarvestInventoryUpdate.ProcessPackage(World, GameManager)"); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return RemoteCloseTarget(); }

            /// <summary>Did the NPC have a window before SCore's handler ran (its GetOrCreate makes one otherwise)?</summary>
            [HarmonyPrefix]
            public static void Prefix(int ____npcEntityId, out bool __state)
            {
                __state = true;
                try { __state = HarvestManager.Has(____npcEntityId); }
                catch (Exception ex) { ScLog.Once("ammo-remote-close-pre", ex); }
            }

            [HarmonyPostfix]
            public static void Postfix(NetPackage __instance, World world, int ____npcEntityId, bool __state)
            {
                try
                {
                    if (world == null || !ScCommunity.IsHost) return;
                    int id = ____npcEntityId;
                    RemoteClosed(id, PlayerOf(__instance, -1));
                    if (Patches_Npc.Patch_SetDead.Release(id, false)) return;
                    EntityAlive e = world.GetEntity(id) as EntityAlive;
                    if (!__state && HarvestManager.Has(id) && (e == null || e.IsDead()))
                    {
                        HarvestManager.Remove(id);
                        HarvestManager.Save();
                        Patches_Xnpc.Count("window-dead-orphan-removed");
                        ScLog.Verbose("remote window: a closed copy of NPC " + id + "'s inventory came back after its window was gone - dropped, not kept");
                        return;
                    }
                    MoveWindowAmmoToBag(e);
                    LoadLoose(e);        // and on into its magazines (M11), as the ticker does
                }
                catch (Exception ex)
                {
                    ScLog.Once("ammo-remote-close", ex);
                }
            }
        }

        // ------------------------------------------------------------------ resupply ----

        /// <summary>
        /// Where the stockpile reaches (D3): "home" inside the home zone, an outpost's name at one of the community's held
        /// outposts (inside its POI, or inside the land claim square around its flag), else null.
        /// </summary>
        public static string Where(Vector3i p)
        {
            if (ScZone.Contains(p)) return "home";
            // a live home attack (base defence D2): the defenders fight up to BaseAttackDefendMargin outside the claim
            if (ScBaseAttack.WideDefence && ScBaseAttack.InDefenceArea(new Vector3(p.x + 0.5f, p.y, p.z + 0.5f))) return "home";
            int vr = ScZone.VanillaRadius;
            foreach (Outpost o in ScRegistry.Outposts)
            {
                if (!o.Holding) continue;
                if (o.Contains(p.x, p.z) || (Math.Abs(p.x - o.Flag.x) <= vr && Math.Abs(p.z - o.Flag.z) <= vr))
                    return string.IsNullOrEmpty(o.Name) ? "outpost " + o.Id : o.Name;
            }
            return null;
        }

        /// <summary>
        /// ScTicker, every second (works every CheckSeconds): moves every member's window ammo to the bag and on into their
        /// magazines (LoadLoose), records what they carry and resupplies the ones who qualify.
        /// </summary>
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
                    MoveWindowAmmoToBag(e);
                    LoadLoose(e);
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
            // M11: a body that reloads this gun by L&L magazine is resupplied into its magazines; one whose gun takes them
            // but who has none is issued them first, where the stockpile reaches (Issue)
            bool mags = ScMags.Active && ScMags.Carries(e, ScMags.GunValue(e, gun));
            if (!mags && ScMags.Active && !issuedTo.Contains(IssueKey(e, gun)) && ScMags.TakesMagazines(gun.GetItemName()))
            {
                string at = Where(e.GetBlockPosition());
                if (at != null) mags = Issue(m, e, gun, standIn, at);
            }
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
            if (mags) { ResupplyMagazines(m, e, gun, ammo, standIn, where, now); return; }
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
            // a guard on watch spends what the shift prepaid first (base defence D3, decision Q10); 0 for everyone else
            int pre = ScWatch.Prepaid(m);
            int opened = 0;
            while (pre + c.AmmoOpenPoints < want * each && c.Get(Resource.Ammo) > 0)
            {
                c.Add(Resource.Ammo, -1);
                c.AmmoOpenPoints += perBundle;
                opened++;
            }
            int give = Math.Min(want, (pre + c.AmmoOpenPoints) / each);
            if (give < want) c.AmmoShort = true;        // tonight's ledger: the stockpile could not cover a resupply
            int added = give > 0 ? AddRounds(e, ammo, give) : 0;
            int fromShift = ScWatch.SpendPrepaid(m, added * each);
            int fromStock = added * each - fromShift;
            c.AmmoOpenPoints = Math.Max(0, c.AmmoOpenPoints - fromStock);
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
            // a home attack's bill (base defence D2): what the stockpile paid now, not what a watch shift paid at its start
            if (where == "home" && fromStock > 0) ScBaseAttack.NoteResupply((fromStock + each - 1) / each, fromStock);
            Record(m, e);       // the stockpile just paid for these: a body that unloads before the next tick keeps them
            ScLog.Out((standIn ? m.Name + "'s stand-in" : m.Name) + " (id " + e.entityId + ") resupplied " + (where == "home" ? "at home" : "at " + where) + ": "
                      + added + " " + ammo.ItemClass.GetItemName() + " for " + gun.GetItemName() + " at " + each + " point(s) each"
                      + (fromShift > 0 ? " - " + fromShift + " point(s) from the watch shift, " + m.WatchPrepaid + " left" : "")
                      + (opened > 0 ? " - opened " + opened + " ammo bundle(s), " + c.Get(Resource.Ammo) + " left" : "")
                      + " (" + c.AmmoOpenPoints + " point(s) left in the open bundle)");
            ScRegistry.SaveAtomic();
        }

        /// <summary>
        /// The resupply of a body that reloads its gun by L&amp;L magazine (M11): up to XnpcResupplyAmmo rounds of the gun's
        /// basic ammo go into its magazines (ScMags.Refill), and the stockpile pays for exactly the rounds that went in -
        /// what it can pay for is offered, and bundles are opened only for what was taken. Magazines that take nothing are
        /// full: nothing to resupply, the stockpile untouched (the count is low only when its magazines hold fewer than
        /// XnpcResupplyBelow in all).
        /// </summary>
        private static void ResupplyMagazines(Member m, EntityAlive e, ItemClass gun, ItemValue ammo, bool standIn, string where, float now)
        {
            Community c = ScRegistry.Community;
            int each = PointsPerRound(ammo.ItemClass);
            int perBundle = Math.Max(1, ScConfig.PointsPerBundle[(int)Resource.Ammo]);
            int want = ScConfig.XnpcResupplyAmmo;
            // a guard on watch spends what the shift prepaid first (base defence D3, decision Q10); 0 for everyone else
            long canPay = (long)ScWatch.Prepaid(m) + (long)c.AmmoOpenPoints + (long)Math.Max(0, c.Get(Resource.Ammo)) * perBundle;
            int give = (int)Math.Min(want, canPay / each);
            nextResupply[e.entityId] = now + ScConfig.XnpcResupplyCooldown;
            string who = (standIn ? m.Name + "'s stand-in" : m.Name) + " (id " + e.entityId + ")";
            if (give <= 0)
            {
                refusedEmpty++;
                c.AmmoShort = true;        // tonight's ledger: the stockpile could not cover a resupply
                Refused(e, m.Name + " needs ammo " + (where == "home" ? "at home" : "at " + where) + " but the stockpile has none (pack some at the packing station)");
                return;
            }
            int added = ScMags.Refill(e, gun.GetItemName(), ammo.ItemClass.GetItemName(), give);
            if (added <= 0)
            {
                magsFull++;
                ScLog.Verbose(who + " is low on ammo but their " + gun.GetItemName() + " magazines are full - nothing to resupply");
                return;
            }
            if (added == give && give < want) c.AmmoShort = true;    // the stockpile, not the magazines, set the limit
            int cost = added * each, opened = 0;
            int fromShift = ScWatch.SpendPrepaid(m, cost);
            int fromStock = cost - fromShift;
            while (c.AmmoOpenPoints < fromStock && c.Get(Resource.Ammo) > 0)
            {
                c.Add(Resource.Ammo, -1);
                c.AmmoOpenPoints += perBundle;
                opened++;
            }
            c.AmmoOpenPoints = Math.Max(0, c.AmmoOpenPoints - fromStock);
            c.AmmoOpened += opened;
            bundlesOpened += opened;
            resupplies++;
            roundsGiven += added;
            refills++;
            refillRounds += added;
            // a home attack's bill (base defence D2): what the stockpile paid now, not what a watch shift paid at its start
            if (where == "home" && fromStock > 0) ScBaseAttack.NoteResupply((fromStock + each - 1) / each, fromStock);
            Record(m, e);
            ScLog.Out(who + " resupplied " + (where == "home" ? "at home" : "at " + where) + ": " + added + " " + ammo.ItemClass.GetItemName()
                      + " into their " + gun.GetItemName() + " magazines at " + each + " point(s) each"
                      + (fromShift > 0 ? " - " + fromShift + " point(s) from the watch shift, " + m.WatchPrepaid + " left" : "")
                      + (opened > 0 ? " - opened " + opened + " ammo bundle(s), " + c.Get(Resource.Ammo) + " left" : "")
                      + " (" + c.AmmoOpenPoints + " point(s) left in the open bundle)");
            ScRegistry.SaveAtomic();
        }

        /// <summary>Body and gun pairs issued magazines this game (Issue): at most once each, so a failed issue cannot repeat every tick.</summary>
        private static readonly HashSet<string> issuedTo = new HashSet<string>(StringComparer.Ordinal);

        private static string IssueKey(EntityAlive e, ItemClass gun) { return e.entityId + ":" + gun.GetItemName(); }

        /// <summary>
        /// A member whose gun takes L&amp;L magazines but who has none for it - one from before M11, or one whose magazines
        /// are gone - is issued its tier's count (ScMags.TierMagazines) of the gun's standard magazine, EMPTY, the first time
        /// it stands where the stockpile reaches with that gun and room in its bag this game. The gun's own loaded rounds go
        /// back to the bag first (UnloadHeld), the member's loose rounds go into the new magazines at once (LoadLoose), and
        /// the stockpile fills the rest through the ordinary resupply, paying only for what goes in. Empty rather than Arm's
        /// loaded set: Arm's rounds would come from nowhere, and charging them afterwards fails whenever the stockpile is
        /// short; this way no round is made. The tier's extended and drum rolls are a new survivor's only. False (loose
        /// rounds as before) when L&amp;L made no magazine.
        /// </summary>
        private static bool Issue(Member m, EntityAlive e, ItemClass gun, bool standIn, string where)
        {
            string std = ScMags.StandardMagazine(gun.GetItemName());
            if (std == null) return false;
            int count = ScMags.TierMagazines(ScMags.TierOf(e));
            string who = (standIn ? m.Name + "'s stand-in" : m.Name) + " (id " + e.entityId + ")";
            // the review's F4: not while the bag is too full to take the magazines and the gun's loaded rounds - rounds left
            // in the unflagged gun would count as a found gun's seated magazine, one more than issued. Not marked, so it is
            // tried again on a later tick once there is room. The slot test comes first (the re-check): a member with a
            // short bag is not emptied into it, and made to reload, every 5 s for nothing.
            if (FreeBagSlots(e) < count + 1)
            {
                Patches_Xnpc.Count("mags-issue-bag-full");
                return false;
            }
            UnloadHeld(e, gun, "magazines issued");
            ItemValue held = e.inventory != null ? e.inventory.holdingItemItemValue : null;
            if (held != null && held.type == gun.Id && held.Meta > 0)
            {
                Patches_Xnpc.Count("mags-issue-bag-full");
                return false;
            }
            issuedTo.Add(IssueKey(e, gun));
            var parts = new List<string>();
            for (int i = 0; i < count; i++) parts.Add(std + "::0");
            ScMags.Restore(e, string.Join(";", parts.ToArray()));
            if (!ScMags.Carries(e, ScMags.GunValue(e, gun)))
            {
                ScLog.Warn(who + " could not be issued " + std + " magazines (Locked & Loaded made none) - loose rounds for their " + gun.GetItemName() + " as before");
                return false;
            }
            issued++;
            int loaded = LoadLoose(e);
            Record(m, e);
            ScLog.Out(who + " was issued " + count + " empty " + std + " for their " + gun.GetItemName() + " " + (where == "home" ? "at home" : "at " + where)
                      + (loaded > 0 ? " and loaded " + loaded + " of their own loose rounds into them" : "") + " - the stockpile fills the rest as it resupplies");
            return true;
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
                   + seeded + " (raiders " + raidersSeeded + ", never resupplied), re-created bodies given their record " + restored
                   + "; magazines (Locked & Loaded): " + ScMags.State + " - armed " + armed + " survivor(s) and " + raidersArmed + " raider(s) with "
                   + armedRounds + " round(s), issued to " + issued + " member(s), " + refills + " refill(s) of " + refillRounds + " round(s), "
                   + magsFull + " time(s) full, " + looseLoaded + " loose round(s) loaded"
                   + "; guns put away gave back " + swapRounds + " round(s) in " + swapReturns + " swap(s), dead NPCs' guns " + deathRounds
                   + "; held guns saved " + gunsSaved + " time(s) (unloaded " + gunsSavedUnloaded + "), back in the hand on load " + gunsRestored + ", given back loose " + gunsLoose + " round(s)"
                   + "; inventory windows open by remote players now " + RemoteOpenCount()
                   + ", dead NPCs' windows waiting for them to close " + Patches_Npc.Patch_SetDead.HeldCount;
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
                string mags = ScMags.Describe(e, gun);
                sb.Append(gun.GetItemName()).Append(' ').Append(Rounds(e, gun)).Append(" round(s)")
                  .Append(mags.Length > 0 ? " (" + Loose(e, r) + " loose, " + mags + ")" : "")
                  .Append(" (resupplied below ").Append(ScConfig.XnpcResupplyBelow).Append(')');
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
            if (m != null)
                sb.Append("; record ammo=").Append(m.Ammo != null ? Encode(m.Ammo) : "never recorded")
                  .Append(" magazines=").Append(string.IsNullOrEmpty(m.Mags) ? "none" : m.Mags + " (" + ScMags.RoundsIn(m.Mags) + " round(s))");
            sb.Append("; window open remotely: ").Append(RemoteOpenState(e.entityId));
            return sb.ToString();
        }
    }
}
