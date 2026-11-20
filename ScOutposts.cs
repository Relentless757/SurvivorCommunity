using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace SurvivorCommunity
{
    public enum OutpostState
    {
        /// <summary>Producing (at the network's run-down / on-foot rate when those are set).</summary>
        Active,
        /// <summary>Over the slot limit (members left, Leadership dropped): no yield, no upkeep, nothing lost.</summary>
        Idle,
        /// <summary>The POI's sleepers are back (a console or Twitch reset): no yield until it is cleared again (round ie1: and no upkeep).</summary>
        Contested,
        /// <summary>The land claim is gone: the record stays a few days for the ledger and the window, then drops.</summary>
        Lost
    }

    /// <summary>A cleared POI the community holds with a land claim; it sends bundles home at every rollover.</summary>
    public sealed class Outpost
    {
        public int Id;
        /// <summary>The land claim block the outpost was established on.</summary>
        public Vector3i Flag;
        /// <summary>The POI's footprint: min corner (PrefabInstance.boundingBoxPosition, the key used everywhere) and size.</summary>
        public Vector3i Poi;
        public Vector3i Size;
        public string Prefab = "";
        public string Name = "";
        /// <summary>
        /// Row id at establish time, or "scavenge". The row's weights are stored with it, so a later config change
        /// never re-types an existing outpost behind the player's back ("sc outpost reclassify" does it on purpose).
        /// </summary>
        public string Row = "";
        public readonly float[] Weight = new float[ScNames.ResourceCount];
        public int Tier;
        public int Day;
        /// <summary>Whole bundles produced while the home radio was silent; delivered the next day it reports in.</summary>
        public readonly int[] Pending = new int[ScNames.ResourceCount];
        public OutpostState State = OutpostState.Active;
        public int LostDay = -1;
        /// <summary>The last rollover's production in whole bundles (round ie1; fractions before it): the "+n/day" the UI shows.</summary>
        public readonly int[] LastYield = new int[ScNames.ResourceCount];
        /// <summary>1..3, bought from the stockpile (ScOutposts.Upgrade); LevelDay = the day it last went up.</summary>
        public int Level = 1;
        public int LevelDay = -1;
        /// <summary>The last day the player was near it (ScGarrison.Tick) - posted members' mood.</summary>
        public int LastVisitDay = -1;
        /// <summary>Output mode: 0 normal, 1 quiet (less yield, less risk), 2 push (more of both).</summary>
        public int Mode;
        /// <summary>Bundles produced over its life (depletion, when OutpostDepletesAfter is on); whole since round ie1.</summary>
        public int Produced;
        /// <summary>What the player built inside it (ScScan.CountArea while it is loaded): beds, facilities, crops, defenses.</summary>
        public readonly int[] Invest = new int[4];
        /// <summary>What the building had when it was established (prefab furniture and traps): Invest counts above it.</summary>
        public readonly int[] InvestBase = new int[4];
        public bool HasInvestBase;
        /// <summary>The last day neglect re-armed a sleeper area (-1 = never); its own clock, apart from visits.</summary>
        public int NeglectDay = -1;
        /// <summary>Whether the home radio heard the open attack (no radio, no warning, no marker).</summary>
        public bool AttackHeard;
        /// <summary>Stash and haul: bundles past the day's haul, waiting in the claim's box for the player.</summary>
        public readonly int[] Stash = new int[ScNames.ResourceCount];
        public int StashTotal { get { int n = 0; for (int i = 0; i < ScNames.ResourceCount; i++) n += Stash[i]; return n; } }

        /// <summary>v2 risk: an open attack's deadline (world time, 0 = none) and whether raiders; damaged through day;
        /// security won in fights (fades); an open raider demand (answer by day, -1 = none) and its price.</summary>
        public ulong AttackUntil;
        public bool AttackRaiders;
        public int DamagedUntil = -1;
        public float SecBonus;
        public int DemandUntil = -1;
        public int DemandAmmo, DemandFood;
        /// <summary>
        /// E10: the hostile enclave behind the open raider demand or raid (-1: a nameless one, or none) - its people come
        /// (ScEncRaids). Saved as [outpost] raidenc=N while set.
        /// </summary>
        public int RaidEnclave = -1;
        /// <summary>
        /// E10: how many of that group died raiding it so far (told when the raid ends; 0 with no raid of theirs open). Saved
        /// as [outpost] raidlost=N while set, so a reload mid-raid keeps the count (the E10 review's L1).
        /// </summary>
        public int RaidLost;

        /// <summary>The POI footprint as an inclusive box {xMin, xMax, zMin, zMax}.</summary>
        public int[] Box { get { return new[] { Poi.x, Poi.x + Math.Max(1, Size.x) - 1, Poi.z, Poi.z + Math.Max(1, Size.z) - 1 }; } }

        public bool Scavenge { get { return Row == "scavenge"; } }
        public bool Holding { get { return State != OutpostState.Lost; } }

        public int PendingTotal
        {
            get { int n = 0; for (int i = 0; i < ScNames.ResourceCount; i++) n += Pending[i]; return n; }
        }

        /// <summary>The resource with the largest weight (scavenge outposts pick theirs each day).</summary>
        public Resource Primary
        {
            get
            {
                Resource best = Resource.Food;
                float w = -1f;
                for (int i = 0; i < ScNames.ResourceCount; i++) if (Weight[i] > w) { w = Weight[i]; best = (Resource)i; }
                return best;
            }
        }

        public Vector3 Centre { get { return new Vector3(Poi.x + Size.x * 0.5f, Flag.y, Poi.z + Size.z * 0.5f); } }

        public bool Contains(int x, int z) { return x >= Poi.x && x < Poi.x + Size.x && z >= Poi.z && z < Poi.z + Size.z; }
    }

    /// <summary>
    /// Outposts (Outposts_Plan.md): a cleared POI with an ordinary land claim is made an outpost from the claim's
    /// radial ("Establish outpost"). At every rollover it sends bundles home - pure registry math, so it works with
    /// the outpost and the home both unloaded, like supply runs. The claim already gives it vanilla's protection
    /// (no sleeper respawn, no trader-quest or rally reset); the patches add the rest (a POI poking out of a vanilla
    /// claim, the quiet ClearSleepers reset, the claim limit that would deactivate the home).
    ///
    /// Economy (all ScConfig; round ie1: whole units, the formulas in ScEconomy): an establish fee (materials + fuel),
    /// upkeep in materials per producing outpost per day by level, haulage in fuel per OutpostHaulPerFuel bundles delivered;
    /// no debt - a day the upkeep is not paid in full runs the network down (each makes OutpostRunDownLess less), one the
    /// haulage is not makes it carry on foot (each at most OutpostOnFootMax). Deliveries need the home radio to have reported in that day;
    /// otherwise the bundles wait at the outpost (capped at OutpostPendingDays of yield). Slots grow with the
    /// community and Leadership; outposts over the limit go Idle, never destroyed. Host only, main thread.
    /// </summary>
    public static class ScOutposts
    {
        public const string NavPrefix = "sc_outpost_";
        public const string ScavengeRow = "scavenge";
        /// <summary>Days a Lost outpost stays listed before its record is dropped.</summary>
        private const int LostKeepDays = 5;

        /// <summary>Footprints {xMin, xMax, zMin, zMax} of every held outpost: read by patches that may run off the main thread.</summary>
        private static volatile int[][] boxes = new int[0][];
        private static volatile Vector3i[] keys = new Vector3i[0];
        private static volatile Vector3i[] flags = new Vector3i[0];

        private static readonly Dictionary<int, Vector3> markers = new Dictionary<int, Vector3>();
        private static bool markersRestored;
        private static float nextTick;
        /// <summary>The Outpost Claim Block block: a claim that establishes itself when placed.</summary>
        public const string FlagBlock = "scOutpostFlag";
        private static readonly List<Vector3i> flagQueue = new List<Vector3i>();
        private static readonly Dictionary<Vector3i, int> flagTries = new Dictionary<Vector3i, int>();

        /// <summary>TEFeatureSCCommunity.OnAdded: an Outpost Claim Block went down; established on the next ticker second.</summary>
        public static void QueueFlag(Vector3i claim)
        {
            if (!flagQueue.Contains(claim)) flagQueue.Add(claim);
        }

        /// <summary>ScTicker, every second: establish queued flags once their claim is active.</summary>
        public static void TickFlags(EntityPlayerLocal player)
        {
            if (flagQueue.Count == 0 || player == null) return;
            var pp = GameManager.Instance != null ? GameManager.Instance.persistentPlayers : null;
            for (int i = flagQueue.Count - 1; i >= 0; i--)
            {
                Vector3i claim = flagQueue[i];
                int tries;
                flagTries.TryGetValue(claim, out tries);
                bool active = pp != null && pp.m_lpBlockMap != null && pp.m_lpBlockMap.ContainsKey(claim);
                if (!active && tries < 5) { flagTries[claim] = tries + 1; continue; }   // the claim map fills a tick later
                flagQueue.RemoveAt(i);
                flagTries.Remove(claim);
                if (!active) { ScCommunity.Tell(player, Localization.Get("scFlagNotActive"), "ui_denied"); continue; }
                string msg;
                Outpost o = Establish(claim, player, false, out msg);
                ScCommunity.Tell(player, o != null ? msg : string.Format(Localization.Get("scFlagPlain"), msg), o != null ? "" : "ui_denied");
            }
        }

        /// <summary>Two-step disband: the claim armed and when (Time.unscaledTime).</summary>
        private static Vector3i disbandArmed;
        private static float disbandUntil;

        public static List<Outpost> All { get { return ScRegistry.Outposts; } }

        public static void Reset()
        {
            flagQueue.Clear();
            flagTries.Clear();
            markers.Clear();
            markersRestored = false;
            nextTick = 0f;
            disbandUntil = 0f;
            boxes = new int[0][];
            keys = new Vector3i[0];
            flags = new Vector3i[0];
        }

        /// <summary>The registry was (re)loaded or reset: rebuild the lookup tables and restore markers on the next tick.</summary>
        public static void OnRegistryChanged()
        {
            markersRestored = false;
            markers.Clear();
            ScScouting.OnRegistryChanged();       // scout marks are restored from the registry too
            ScOutpostRisk.OnRegistryChanged();
            ScBaseAttack.OnRegistryChanged();     // the home attack's session state is squared with the loaded counts
            ScWatch.OnRegistryChanged();          // the guards' session state and the call-out marker start again
            ScEnclaves.OnRegistryChanged();       // enclave ids, their POI lookups, markers restored on the next tick
            ScInfestations.OnRegistryChanged();   // the same for the infestations (I1)
            ScBands.OnRegistryChanged();          // bands on the road: ids squared, no body known, markers restored on the next tick (E13a)
            // ids must be unique (yields, selection and staff key on them) and the counter ahead of all of them
            Community c = ScRegistry.Community;
            var ids = new HashSet<int>();
            foreach (Outpost o in ScRegistry.Outposts)
            {
                if (o.Id <= 0 || !ids.Add(o.Id))
                {
                    int old = o.Id;
                    o.Id = Math.Max(c.NextOutpostId, MaxId()) + 1;
                    ids.Add(o.Id);
                    ScLog.Warn("outpost '" + o.Name + "' had a duplicate id " + old + " - now " + o.Id);
                }
                if (o.Id > c.NextOutpostId) c.NextOutpostId = o.Id;
            }
            Rebuild();
        }

        private static int MaxId()
        {
            int n = 0;
            foreach (Outpost o in ScRegistry.Outposts) if (o.Id > n) n = o.Id;
            return n;
        }

        /// <summary>Rebuilds the thread-safe lookup arrays (call after any change to the list or a state).</summary>
        public static void Rebuild()
        {
            var b = new List<int[]>();
            var k = new List<Vector3i>();
            var f = new List<Vector3i>();
            foreach (Outpost o in ScRegistry.Outposts)
            {
                if (!o.Holding) continue;
                b.Add(new[] { o.Poi.x, o.Poi.x + Math.Max(1, o.Size.x) - 1, o.Poi.z, o.Poi.z + Math.Max(1, o.Size.z) - 1 });
                k.Add(o.Poi);
                f.Add(o.Flag);
            }
            boxes = b.ToArray();
            keys = k.ToArray();
            flags = f.ToArray();
        }

        // ------------------------------------------------------------------ queries (thread-safe) ----

        public static bool IsOutpostClaim(Vector3i claim)
        {
            Vector3i[] f = flags;
            for (int i = 0; i < f.Length; i++) if (f[i] == claim) return true;
            return false;
        }

        public static bool IsOutpostPoiKey(Vector3i key)
        {
            Vector3i[] k = keys;
            for (int i = 0; i < k.Length; i++) if (k[i] == key) return true;
            return false;
        }

        /// <summary>SleeperEventData.position is the POI key as a Vector3.</summary>
        public static bool IsOutpostPoi(Vector3 poiPosition)
        {
            return keys.Length > 0 && IsOutpostPoiKey(World.worldToBlockPos(poiPosition));
        }

        /// <summary>Does the box (inclusive XZ) touch a held outpost's POI footprint? (CheckForAnyPlayerHome postfix.)</summary>
        public static bool BoxHitsOutpost(Vector3i min, Vector3i max)
        {
            int[][] b = boxes;
            for (int i = 0; i < b.Length; i++)
            {
                int[] r = b[i];
                if (min.x <= r[1] && max.x >= r[0] && min.z <= r[3] && max.z >= r[2]) return true;
            }
            return false;
        }

        public static Outpost AtClaim(Vector3i claim)
        {
            foreach (Outpost o in ScRegistry.Outposts) if (o.Holding && o.Flag == claim) return o;
            return null;
        }

        public static Outpost ByPoi(Vector3i key)
        {
            foreach (Outpost o in ScRegistry.Outposts) if (o.Holding && o.Poi == key) return o;
            return null;
        }

        public static Outpost ById(int id)
        {
            foreach (Outpost o in ScRegistry.Outposts) if (o.Id == id) return o;
            return null;
        }

        /// <summary>Held outposts (not Lost) - what counts against the slots.</summary>
        public static int HeldCount()
        {
            int n = 0;
            foreach (Outpost o in ScRegistry.Outposts) if (o.Holding) n++;
            return n;
        }

        /// <summary>slots = min(OutpostMax, 1 + members / OutpostMembersPerSlot + Leadership / OutpostLeadershipPerSlot).</summary>
        public static int Slots()
        {
            int members = ScRegistry.Members.Count;
            int bySize = ScConfig.OutpostMembersPerSlot > 0 ? members / ScConfig.OutpostMembersPerSlot : 0;
            int byLead = ScConfig.OutpostLeadershipPerSlot > 0 ? ScLeadership.MoraleBonus() / ScConfig.OutpostLeadershipPerSlot : 0;
            return Math.Max(0, Math.Min(ScConfig.OutpostMax, 1 + bySize + byLead));
        }

        // ------------------------------------------------------------------ the radio ----

        /// <summary>
        /// A persisted radio inside the home zone with power in the power graph. The power graph ticks on the
        /// server for every source whether or not its chunk is loaded (PowerManager.Update), so this answers
        /// from anywhere - ScRadio.Online needs the tile entity and so only works at home.
        /// </summary>
        public static bool RadioOnlineAnywhere()
        {
            try
            {
                if (!ScZone.HasHome || !PowerManager.HasInstance) return false;
                PowerManager pm = PowerManager.Instance;
                foreach (Vector3i pos in ScRegistry.Community.Radios)
                {
                    if (!ScZone.Contains(pos)) continue;
                    PowerItem item = pm.GetPowerItemByWorldPos(pos);
                    if (item != null && item.IsPowered) return true;
                }
                foreach (Vector3i pos in ScRadio.Radios)
                {
                    if (!ScZone.Contains(pos)) continue;
                    PowerItem item = pm.GetPowerItemByWorldPos(pos);
                    if (item != null && item.IsPowered) return true;
                }
            }
            catch (Exception e)
            {
                ScLog.Once("outpost-radio", e);
            }
            return false;
        }

        /// <summary>Remembers a radio for good (ScRadio.NoteRadio): only radios inside the home zone count.</summary>
        public static void RememberRadio(Vector3i pos)
        {
            try
            {
                if (!ScCommunity.IsHost || !ScRegistry.Loaded || !ScZone.HasHome) return;
                if (!ScZone.Contains(pos)) { RememberRelay(pos); return; }
                List<Vector3i> radios = ScRegistry.Community.Radios;
                if (radios.Contains(pos)) return;
                // one powered radio is all it takes: past the cap, new ones are simply not remembered (evicting
                // would make every scan swap them in and out, each with a save)
                if (radios.Count >= 16) { ScLog.Once("radios-full", "16 radios remembered in the home zone - further ones are not tracked"); return; }
                radios.Add(pos);
                ScLog.Out("radio at " + pos + " remembered: outposts can report in to it from anywhere");
                ScRegistry.SaveAtomic();
            }
            catch (Exception e)
            {
                ScLog.Once("outpost-remember-radio", e);
            }
        }

        /// <summary>A radio outside the home zone: kept as a relay (it counts for any outpost whose POI it stands in).</summary>
        private static void RememberRelay(Vector3i pos)
        {
            List<Vector3i> relays = ScRegistry.Community.Relays;
            if (relays.Contains(pos)) return;
            if (relays.Count >= 16) { ScLog.Once("relays-full", "16 radio relays remembered - further ones are not tracked"); return; }
            relays.Add(pos);
            Outpost o = OutpostAt(pos.x, pos.z);
            ScLog.Out("radio relay at " + pos + " remembered" + (o != null ? " for the outpost at " + o.Name : " (no outpost there yet)"));
            ScRegistry.SaveAtomic();
        }

        /// <summary>BlockScRadio.OnBlockRemoved: a radio picked up or destroyed is forgotten at once.</summary>
        public static void ForgetRadio(Vector3i pos)
        {
            try
            {
                if (!ScCommunity.IsHost || !ScRegistry.Loaded) return;
                Community c = ScRegistry.Community;
                bool any = c.Radios.Remove(pos) | c.Relays.Remove(pos);
                ScRadio.Radios.Remove(pos);
                if (any) { ScLog.Out("radio at " + pos + " removed - forgotten"); ScRegistry.SaveAtomic(); }
            }
            catch (Exception e)
            {
                ScLog.Once("outpost-forget-radio", e);
            }
        }

        /// <summary>The held outpost whose POI footprint holds this column, or null.</summary>
        public static Outpost OutpostAt(int x, int z)
        {
            foreach (Outpost o in ScRegistry.Outposts) if (o.Holding && o.Contains(x, z)) return o;
            return null;
        }

        /// <summary>A powered radio relay stands inside this outpost's POI.</summary>
        public static bool RelayOnline(Outpost o)
        {
            if (o == null || !PowerManager.HasInstance) return false;
            PowerManager pm = PowerManager.Instance;
            foreach (Vector3i pos in ScRegistry.Community.Relays)
            {
                if (!o.Contains(pos.x, pos.z)) continue;
                PowerItem item = pm.GetPowerItemByWorldPos(pos);
                if (item != null && item.IsPowered) return true;
            }
            return false;
        }

        /// <summary>A powered radio inside a POI that is not an outpost yet (establishing in the field).</summary>
        private static bool PoweredRadioIn(PrefabInstance pi)
        {
            if (pi == null || !PowerManager.HasInstance) return false;
            PowerManager pm = PowerManager.Instance;
            var all = new List<Vector3i>(ScRegistry.Community.Relays);
            all.AddRange(ScRadio.Radios);
            foreach (Vector3i pos in all)
            {
                if (!ScSecurity.FootprintContains(pi, pos)) continue;
                PowerItem item = pm.GetPowerItemByWorldPos(pos);
                if (item != null && item.IsPowered) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ establish ----

        /// <summary>Why "Establish outpost" is refused, as a finished tooltip; null = it may go ahead.</summary>
        public static string CanEstablish(Vector3i claim, EntityPlayerLocal player, out PrefabInstance poi, out PoiClass cls)
        {
            poi = null;
            cls = null;
            if (!ScCommunity.IsHost) return Localization.Get("scHostOnly");
            // the home must be live as it is: EnsureHome would adopt the first other claim - out here, usually this one
            if (!ScZone.HasHome) return Localization.Get("scNeedHome");
            if (ScZone.Home == claim) return Localization.Get("scOpIsHome");
            if (!HomeClaimLive()) return Localization.Get("scOpHomeGone");
            World w = ScCommunity.World;
            poi = ScSecurity.PoiAt(claim);
            if (poi == null || ScSecurity.OverlapsHome(poi)) return Localization.Get("scOpNotInPoi");
            string name = ScSecurity.DisplayName(poi);
            Enclave enclave = ScEnclaves.Holding(poi);
            if (enclave != null) return string.Format(Localization.Get("scOutpostEnclave"), name, enclave.Name);   // their building, not ours to hold
            string why;
            if (!ScSecurity.IsCleared(w, poi, out why)) return string.Format(Localization.Get("scOpNotCleared"), name, why);
            cls = ScPoiTypes.Classify(poi);
            if (!cls.Producing) return string.Format(Localization.Get("scOpNothing"), name);
            float d = DistanceToHome(poi.boundingBoxPosition, poi.boundingBoxSize);
            if (d < ScConfig.OutpostMinDistance) return string.Format(Localization.Get("scOpTooClose"), Mathf.RoundToInt(d), ScConfig.OutpostMinDistance);
            if (ByPoi(poi.boundingBoxPosition) != null) return string.Format(Localization.Get("scOpAlready"), name);
            int held = HeldCount(), slots = Slots();
            if (held >= slots) return string.Format(Localization.Get(held >= ScConfig.OutpostMax ? "scOpMaxed" : "scOpNoSlot"), held, slots);
            if (!RadioOnlineAnywhere() && !PoweredRadioIn(poi)) return Localization.Get("scOpNoRadio");   // home radio, or one on site
            Community c = ScRegistry.Community;
            int feeMats = ScOutpostPerks.FeeMats(), feeFuel = ScOutpostPerks.FeeFuel();   // a materials outpost's salvage yard cuts it
            if (c.Get(Resource.Materials) < feeMats || c.Get(Resource.Fuel) < feeFuel)
                return string.Format(Localization.Get("scOpNoFee"), feeMats, feeFuel, c.Get(Resource.Materials), c.Get(Resource.Fuel));
            return null;
        }

        /// <summary>The home claim is still an active claim.</summary>
        public static bool HomeClaimLive()
        {
            var pp = GameManager.Instance != null ? GameManager.Instance.persistentPlayers : null;
            return ScZone.HasHome && pp != null && pp.m_lpBlockMap != null && pp.m_lpBlockMap.ContainsKey(ScZone.Home);
        }

        /// <summary>XZ distance from a point to the outpost's POI footprint (0 inside it).</summary>
        public static float DistanceToBox(Outpost o, Vector3 p)
        {
            int[] b = o.Box;
            float dx = p.x < b[0] ? b[0] - p.x : (p.x > b[1] + 1 ? p.x - b[1] - 1 : 0f);
            float dz = p.z < b[2] ? b[2] - p.z : (p.z > b[3] + 1 ? p.z - b[3] - 1 : 0f);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        public static float DistanceToHome(Vector3i a, Vector3i s)
        {
            if (!ScZone.HasHome) return float.MaxValue;
            Vector3i h = ScZone.Home;
            float cx = a.x + s.x * 0.5f, cz = a.z + s.z * 0.5f;
            return Mathf.Sqrt((cx - h.x) * (cx - h.x) + (cz - h.z) * (cz - h.z));
        }

        /// <summary>The claim radial's "Establish outpost". force = the console's debug add (no gates, no fee).</summary>
        public static Outpost Establish(Vector3i claim, EntityPlayerLocal player, bool force, out string message)
        {
            message = null;
            try
            {
                PrefabInstance pi;
                PoiClass cls;
                if (!force)
                {
                    message = CanEstablish(claim, player, out pi, out cls);
                    if (message != null) return null;
                }
                else
                {
                    if (!ScZone.HasHome || ScZone.Home == claim) { message = Localization.Get(ScZone.HasHome ? "scOpIsHome" : "scNeedHome"); return null; }
                    pi = ScSecurity.PoiAt(claim);
                    if (pi == null) { message = Localization.Get("scOpNotInPoi"); return null; }
                    if (ByPoi(pi.boundingBoxPosition) != null) { message = string.Format(Localization.Get("scOpAlready"), ScSecurity.DisplayName(pi)); return null; }
                    Enclave enclave = ScEnclaves.Holding(pi);
                    if (enclave != null) { message = string.Format(Localization.Get("scOutpostEnclave"), ScSecurity.DisplayName(pi), enclave.Name); return null; }
                    cls = ScPoiTypes.Classify(pi);
                }

                Community c = ScRegistry.Community;
                var o = new Outpost
                {
                    Id = ++c.NextOutpostId,
                    Flag = claim,
                    Poi = pi.boundingBoxPosition,
                    Size = pi.boundingBoxSize,
                    Prefab = pi.prefab.PrefabName ?? "",
                    Name = ScSecurity.DisplayName(pi),
                    Tier = Math.Max(0, (int)pi.prefab.DifficultyTier),
                    Day = ScCommunity.Today,
                    LastVisitDay = ScCommunity.Today,
                    State = OutpostState.Active
                };
                SetType(o, cls);
                {
                    // what the building already had (prefab beds, workbenches, traps) is the baseline, not an investment;
                    // counted now if the area is loaded, else on the first visit
                    int[] box = o.Box, n = new int[4];
                    if (ScScan.CountArea(box[0], box[1], box[2], box[3], n)) { for (int i = 0; i < 4; i++) o.InvestBase[i] = n[i]; o.HasInvestBase = true; }
                }
                ScRegistry.Outposts.RemoveAll(x => x.State == OutpostState.Lost && x.Poi == o.Poi);   // retaken: the old record goes
                ScRegistry.Outposts.Add(o);
                if (!force)
                {
                    c.Add(Resource.Materials, -ScOutpostPerks.FeeMats());
                    c.Add(Resource.Fuel, -ScOutpostPerks.FeeFuel());
                }
                Rebuild();
                AddMarker(o, player);
                ScLeadership.Count(ScLeadership.OutpostEvent);
                string yield = YieldText(Estimate(o));
                message = string.Format(Localization.Get("scOpEstablished"), o.Name, yield);
                Note(string.Format(Localization.Get("scOpLedgerNew"), o.Name, TypeLabel(o)));
                ScLog.Out("outpost " + o.Id + " established at " + o.Name + " (" + o.Prefab + ", T" + o.Tier + ", " + TypeLabel(o) + ") flag " + claim
                          + (force ? " [console, no gates]" : "") + ": " + yield);
                ScRegistry.SaveAtomic();
                return o;
            }
            catch (Exception e)
            {
                ScLog.Once("outpost-establish", e);
                message = Localization.Get("scInternal");
                return null;
            }
        }

        /// <summary>Stores the row (or scavenge) and its weights on the outpost.</summary>
        public static void SetType(Outpost o, PoiClass cls)
        {
            for (int i = 0; i < ScNames.ResourceCount; i++) o.Weight[i] = 0f;
            if (cls != null && cls.Kind == "row" && cls.Row != null)
            {
                o.Row = cls.Row.Id;
                for (int i = 0; i < ScNames.ResourceCount; i++) o.Weight[i] = cls.Row.Weight[i];
            }
            else o.Row = ScavengeRow;
        }

        public static bool Disband(Outpost o, EntityPlayerLocal player)
        {
            if (o == null) return false;
            ScGarrison.OnOutpostGone(o, player);
            ScOutpostRisk.Forget(o);
            RemoveMarker(o);
            ScRegistry.Outposts.Remove(o);
            Rebuild();
            Note(string.Format(Localization.Get("scOpLedgerDisband"), o.Name));
            if (player != null) ScCommunity.Tell(player, string.Format(Localization.Get("scOpDisbanded"), o.Name), "");
            ScLog.Out("outpost " + o.Id + " at " + o.Name + " disbanded");
            ScRegistry.SaveAtomic();
            return true;
        }

        /// <summary>Radial "Disband outpost": the first press arms it for 5 seconds, the second disbands.</summary>
        public static void DisbandPressed(Vector3i claim, EntityPlayerLocal player)
        {
            Outpost o = AtClaim(claim);
            if (o == null) return;
            float now = Time.unscaledTime;
            if (disbandArmed == claim && now < disbandUntil)
            {
                disbandUntil = 0f;
                Disband(o, player);
                return;
            }
            disbandArmed = claim;
            disbandUntil = now + 5f;
            ScCommunity.Tell(player, string.Format(Localization.Get("scOpDisbandConfirm"), o.Name), "ui_denied");
        }

        /// <summary>
        /// A claim block's tile entity was destroyed (TEFeatureSCCommunity.OnDestroy): check the claims on the next
        /// ticker second instead of the next ten. The claim map decides, not this call - a feature's OnDestroy also
        /// runs for a tile entity replaced in place, and that must not lose the outpost.
        /// </summary>
        public static void OnClaimDestroyed(Vector3i claim)
        {
            if (IsOutpostClaim(claim)) nextTick = 0f;
        }

        private static void MarkLost(Outpost o, EntityPlayerLocal player, string key)
        {
            if (o.State == OutpostState.Lost) return;
            o.State = OutpostState.Lost;
            o.LostDay = ScCommunity.Today;
            for (int i = 0; i < ScNames.ResourceCount; i++) o.Pending[i] = 0;
            ScGarrison.OnOutpostGone(o, player);
            ScOutpostRisk.Forget(o);
            ScRegistry.Community.OutpostLoss += ScConfig.OutpostLostPenalty;
            RemoveMarker(o);
            Rebuild();
            Note(string.Format(Localization.Get("scOpLedgerLost"), o.Name));
            if (player != null) ScCommunity.Tell(player, string.Format(Localization.Get(key), o.Name), "ui_denied");
            ScLog.Warn("outpost " + o.Id + " at " + o.Name + " lost (" + key + ")");
        }

        /// <summary>
        /// Every held outpost's claim must still be an active claim (m_lpBlockMap: vanilla removes deactivated and
        /// destroyed claims from it, including ones handled while their chunk was unloaded).
        /// </summary>
        public static bool CheckClaims(EntityPlayerLocal player)
        {
            var pp = GameManager.Instance != null ? GameManager.Instance.persistentPlayers : null;
            if (pp == null || pp.m_lpBlockMap == null) return false;
            bool changed = false;
            foreach (Outpost o in ScRegistry.Outposts)
            {
                if (!o.Holding || pp.m_lpBlockMap.ContainsKey(o.Flag)) continue;
                MarkLost(o, player, "scOpLostClaim");
                changed = true;
            }
            return changed;
        }

        // ------------------------------------------------------------------ the day ----

        /// <summary>
        /// ScDaily.Run, right after the supply runs: every held outpost produces, the network pays upkeep and
        /// haulage, and - if the home radio reported in - the bundles land in the stockpile.
        /// </summary>
        public static void Daily(int day, EntityPlayerLocal player, StringBuilder summary)
        {
            List<Outpost> list = ScRegistry.Outposts;
            Community c = ScRegistry.Community;
            if (c.OutpostLoss > 0f) c.OutpostLoss = Math.Max(0f, c.OutpostLoss - ScConfig.OutpostLostDecay);
            if (c.OutpostTriumph > 0f) c.OutpostTriumph = Math.Max(0f, c.OutpostTriumph - 1f);   // fades with or without outposts
            if (list.Count == 0)
            {
                // no network left: a new outpost must not start run-down or on foot
                c.OutpostRunDown = c.OutpostOnFoot = false;
                return;
            }
            World w = ScCommunity.World;
            try
            {
                list.RemoveAll(o => o.State == OutpostState.Lost && day - o.LostDay >= LostKeepDays);
                list.Sort((a, b) => a.Id.CompareTo(b.Id));
                CheckClaims(player);
                ScGarrison.Validate(player);

                // slots: the newest outposts over the limit idle (nothing destroyed) - round fx2: ApplySlots, which Tick runs too
                int slots = Slots();
                ApplySlots(w, player);

                // contested: the persisted sleeper volumes say the POI filled up again
                foreach (Outpost o in list) ContestCheck(o, w, player);
                // round fx3: one retaken just now while over the limit idles before it produces
                ApplySlots(w, player);

                // production (round ie1): whole units per outpost from ScEconomy.NetworkYields, the numbers the window, the Home
                // Status panel and the console read too; nothing is carried to the next day
                Dictionary<int, int[]> yields = ScEconomy.NetworkYields(day);
                bool homeReported = c.RadioOnlineDay >= day - 1 || RadioOnlineAnywhere();
                var delivered = new int[ScNames.ResourceCount];
                int waiting = Deliver(list, yields, homeReported, true, delivered);
                int total = 0;
                for (int r = 0; r < ScNames.ResourceCount; r++) { if (delivered[r] > 0) c.Add((Resource)r, delivered[r]); total += delivered[r]; }

                // costs (round ie1): no debt. The day's upkeep (the producing outposts by level: idle, contested and damaged ones
                // pay nothing) and haulage (1 fuel per OutpostHaulPerFuel units delivered) come out of the stockpile as far as it
                // goes; a day it cannot pay in full runs the network down / sends it on foot until a day it pays in full
                int dueMats = ScEconomy.Upkeep(day), dueFuel = ScEconomy.Haulage(total);
                int paidMats = Math.Min(dueMats, c.Get(Resource.Materials));
                c.Add(Resource.Materials, -paidMats);
                int paidFuel = Math.Min(dueFuel, c.Get(Resource.Fuel));
                c.Add(Resource.Fuel, -paidFuel);
                bool wasRunDown = c.OutpostRunDown, wasOnFoot = c.OutpostOnFoot;
                c.OutpostRunDown = paidMats < dueMats;
                c.OutpostOnFoot = paidFuel < dueFuel;
                if (player != null)
                {
                    if (c.OutpostRunDown && !wasRunDown) ScCommunity.Tell(player, Localization.Get("scOpRunDown"), "ui_denied");
                    else if (!c.OutpostRunDown && wasRunDown) ScCommunity.Tell(player, Localization.Get("scOpRecovered"), "");
                    if (c.OutpostOnFoot && !wasOnFoot) ScCommunity.Tell(player, Localization.Get("scOpOnFoot"), "ui_denied");
                }

                // the ledger / rollover tooltip
                var sb = new StringBuilder();
                sb.Append(string.Format(Localization.Get("scOpDaily"), total > 0 ? Amounts(delivered) : Localization.Get("scOpNothingToday")));
                if (paidMats > 0 || paidFuel > 0) sb.Append(' ').Append(string.Format(Localization.Get("scOpDailyCosts"), paidMats, paidFuel));
                if (waiting > 0) sb.Append(' ').Append(string.Format(Localization.Get("scOpDailySilent"), waiting));
                if (c.OutpostRunDown) sb.Append(' ').Append(Localization.Get("scOpDailyRunDown"));
                if (c.OutpostOnFoot) sb.Append(' ').Append(Localization.Get("scOpDailyOnFoot"));
                if (summary.Length > 0) summary.Append(' ');
                summary.Append(sb);
                ScLog.Out("outposts day " + day + ": " + yields.Count + " producing of " + HeldCount() + " held (" + slots + " slot(s)), delivered " + Amounts(delivered)
                          + ", upkeep paid " + paidMats + " of " + dueMats + " mats, haulage paid " + paidFuel + " of " + dueFuel + " fuel"
                          + (c.OutpostRunDown ? " (run-down)" : "") + (c.OutpostOnFoot ? " (on foot)" : "")
                          + (homeReported ? "" : ", home radio silent") + (waiting > 0 ? " (" + waiting + " waiting)" : ""));
                ScGarrison.Workshops(day, player, summary);
                ScOutpostRisk.Daily(day, player, summary);
                ScGarrison.WalkIns(day, player, summary);
                Rebuild();
                RefreshMarkers(player);
            }
            catch (Exception e)
            {
                ScLog.Once("outpost-daily", e);
            }
        }

        /// <summary>The primary resource an outpost produces today (a scavenge outpost's is whatever is shortest).</summary>
        public static Resource PrimaryToday(Outpost o)
        {
            return o.Scavenge ? Shortest() : o.Primary;
        }

        /// <summary>
        /// Round ie1: the delivery loop, one for the rollover (apply: true - LastYield, Produced, the stash and the pending bundles
        /// change, delivered is what lands in the stockpile) and for the projection (apply: false, a pure read: what the next
        /// rollover would bring home), so the two can never drift apart. Yields are ScEconomy.NetworkYields; an outpost delivers
        /// when the home radio reported in that day or its own relay is online, within the haul cap (stash and haul); otherwise
        /// its whole units wait, at most OutpostPendingDays of that day's yield. Returns the bundles left waiting.
        /// </summary>
        private static int Deliver(List<Outpost> list, Dictionary<int, int[]> yields, bool homeReported, bool apply, int[] delivered)
        {
            int waiting = 0;
            foreach (Outpost o in list)
            {
                if (apply) for (int i = 0; i < ScNames.ResourceCount; i++) o.LastYield[i] = 0;
                if (!o.Holding) continue;
                int[] y;
                yields.TryGetValue(o.Id, out y);
                bool reported = homeReported || RelayOnline(o);   // its own radio relay reports in without the home one
                int haulRoom = ScOutpostPerks.HaulCap(o);
                for (int r = 0; r < ScNames.ResourceCount; r++)
                {
                    int v = y != null ? y[r] : 0;
                    if (apply) { o.LastYield[r] = v; o.Produced += v; }
                    if (reported)
                    {
                        int amount = v + o.Pending[r];
                        int take = Math.Min(amount, haulRoom);          // stash and haul (off: no limit)
                        if (haulRoom != int.MaxValue) haulRoom -= take;
                        delivered[r] += take;
                        if (!apply) continue;
                        o.Pending[r] = 0;
                        if (amount > take) o.Stash[r] = Math.Min(ScConfig.OutpostStashMax, o.Stash[r] + amount - take);
                    }
                    else if (apply && v > 0)
                    {
                        int cap = Math.Max(1, ScConfig.OutpostPendingDays * v);
                        o.Pending[r] = Math.Min(o.Pending[r] + v, Math.Max(cap, o.Pending[r]));
                    }
                }
                waiting += o.PendingTotal;
            }
            return waiting;
        }

        /// <summary>
        /// The bundles the next rollover (day) would bring home - Daily's delivery loop (Deliver), changing nothing. Slot changes
        /// at the rollover are not foreseen. The Home Status panel and the window turn it into the day's haulage (ScEconomy.Haulage).
        /// </summary>
        public static int ProjectedDelivery(int day)
        {
            Community c = ScRegistry.Community;
            List<Outpost> list = ScRegistry.Outposts;
            if (list.Count == 0) return 0;
            var delivered = new int[ScNames.ResourceCount];
            Deliver(list, ScEconomy.NetworkYields(day), c.RadioOnlineDay >= day - 1 || RadioOnlineAnywhere(), false, delivered);
            int total = 0;
            for (int r = 0; r < ScNames.ResourceCount; r++) total += delivered[r];
            return total;
        }

        /// <summary>
        /// The resource with the fewest days of cover: stock over what the community uses a day. Water counts what the
        /// home's wells and dew collectors do not cover; luxuries are never needed, so never the shortest. Round ie1: food,
        /// water and materials read the rollover's own figures (ScEconomy); meds, ammo and fuel stay planning estimates.
        /// </summary>
        public static Resource Shortest()
        {
            Community c = ScRegistry.Community;
            int injured = 0, ranged = 0;
            foreach (var m in ScRegistry.Members.Values) { if (m.Injured) injured++; if (m.Ranged && m.IsHome) ranged++; }
            int members = Math.Max(1, ScRegistry.Members.Count);
            float[] need =
            {
                Math.Max(0.1f, ScEconomy.FoodEaten()),
                Math.Max(0.1f, injured > 0 ? injured : members * 0.05f),
                Math.Max(0.1f, ranged * ScConfig.AmmoPerRangedDay),
                Math.Max(0.1f, ScEconomy.Upkeep(ScCommunity.Today + 1)),
                Math.Max(0.1f, 0.1f * Math.Max(1, HeldCount())),
                ScEconomy.WaterShortfall() > 0 ? ScEconomy.WaterShortfall() : 0f,    // 0 = covered, never the shortest
                0f
            };
            Resource best = Resource.Food;
            float bestDays = float.MaxValue;
            for (int i = 0; i < ScNames.ResourceCount; i++)
            {
                if (need[i] <= 0f) continue;
                float days = c.Get((Resource)i) / need[i];
                if (days < bestDays) { bestDays = days; best = (Resource)i; }
            }
            return best;
        }

        /// <summary>What an outpost makes on the next rollover, in whole units (ScEconomy.NetworkYields: duplicates and the network included).</summary>
        public static int[] Estimate(Outpost target)
        {
            int[] y;
            if (target != null && ScEconomy.NetworkYields(ScCommunity.Today + 1).TryGetValue(target.Id, out y)) return y;
            return new int[ScNames.ResourceCount];
        }

        /// <summary>
        /// The row's and the map marker's yield: what it makes on the next rollover (Estimate), the detail line's figure. The
        /// review of ie1 (M2): it showed the last rollover's until the next one, so an upgrade, new staff or a mode change read
        /// one number on the row and another in the detail; an outpost that makes nothing tonight shows nothing.
        /// </summary>
        public static int[] ShownYield(Outpost o)
        {
            return Estimate(o);
        }

        // ------------------------------------------------------------------ text ----

        /// <summary>"+2 food, +1 fuel a day" (or "nothing"): whole bundles since round ie1.</summary>
        public static string YieldText(int[] y)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < ScNames.ResourceCount; i++)
            {
                if (y[i] <= 0) continue;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append('+').Append(y[i].ToString(CultureInfo.InvariantCulture)).Append(' ').Append(Localization.Get(ScNames.LocKey((Resource)i)));
            }
            return sb.Length > 0 ? string.Format(Localization.Get("scOpPerDay"), sb) : Localization.Get("scOpNothingToday");
        }

        /// <summary>"2 food, 1 building material" (whole bundles).</summary>
        public static string Amounts(int[] n)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < ScNames.ResourceCount; i++)
            {
                if (n[i] <= 0) continue;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append('+').Append(n[i]).Append(' ').Append(Localization.Get(ScNames.LocKey((Resource)i)));
            }
            return sb.Length > 0 ? sb.ToString() : "0";
        }

        public static string TypeLabel(Outpost o)
        {
            string key = "scOpType_" + o.Row;
            string s = Localization.Get(key);
            return string.IsNullOrEmpty(s) || s == key ? o.Row : s;
        }

        public static string StateLabel(Outpost o)
        {
            Community c = ScRegistry.Community;
            if (o.State == OutpostState.Active && c.OutpostRunDown) return Localization.Get("scOpStateRunDown");
            return Localization.Get("scOpState" + o.State);
        }

        /// <summary>The radial "Outpost" report: one tooltip line.</summary>
        public static string Report(Outpost o)
        {
            string s = string.Format(Localization.Get("scOpReport"), o.Name, TypeLabel(o), StateLabel(o), YieldText(Estimate(o)));
            if (o.PendingTotal > 0) s += " " + string.Format(Localization.Get("scOpReportPending"), o.PendingTotal);
            if (!RadioOnlineAnywhere()) s += " " + Localization.Get("scOpReportSilent");
            return s;
        }

        private static void Note(string line) { NoteLedger(line); }

        public static void NoteLedger(string line)
        {
            Community c = ScRegistry.Community;
            c.Ledger.Insert(0, "day " + ScCommunity.Today + ": " + line);
            while (c.Ledger.Count > 7) c.Ledger.RemoveAt(c.Ledger.Count - 1);
        }

        // ------------------------------------------------------------------ ticker, markers ----

        /// <summary>ScTicker, every second (works every 10): radio report-in stamp, claim check, markers, radio list upkeep.</summary>
        public static void Tick(World w, EntityPlayerLocal player)
        {
            try
            {
                if (w == null || player == null) return;
                float now = Time.unscaledTime;
                if (now < nextTick) return;
                nextTick = now + 10f;
                Community c = ScRegistry.Community;
                int today = ScCommunity.Today;
                if (c.RadioOnlineDay < today && RadioOnlineAnywhere()) c.RadioOnlineDay = today;   // saved with the next save
                PruneRadios(w);
                if (ScRegistry.Outposts.Count == 0) return;
                if (!markersRestored)
                {
                    markersRestored = true;
                    Rebuild();
                    RefreshMarkers(player);
                }
                if (CheckClaims(player)) ScRegistry.SaveAtomic();
                // round fx2: the slot limit now, not only at the rollover - before the retake check, the rollover's order
                if (ApplySlots(w, player))
                {
                    RefreshMarkers(player);       // (no Rebuild: its boxes follow Holding, which slots don't change)
                    ScRegistry.SaveAtomic();
                }
                RetakeCheck(w, player);
            }
            catch (Exception e)
            {
                ScLog.Once("outpost-tick", e);
            }
        }

        /// <summary>
        /// Round fx2 (2026-11-20, found in Claude's self-test): the slot limit. The oldest Slots() held outposts (by id) work and
        /// the newer ones idle - nothing is lost, and they come back the same way when there is room again. It used to run only
        /// at the rollover, so after a death (or Leadership) lowered the slots, the window, the Home Status panel and sc outpost
        /// yield went on counting an outpost the rollover then idled - delivering and charging less than they said. Tick runs it
        /// every 10 s now, and the rollover first. One coming back from idle is checked for sleepers at once (ContestCheck), as
        /// the rollover does right after its slots. Round fx3: only an active outpost goes idle; a contested one keeps its state
        /// until it is retaken, then idles on the next pass (RetakeCheck and the rollover run one straight after a retake).
        /// Returns true when an outpost's state changed.
        /// </summary>
        internal static bool ApplySlots(World w, EntityPlayerLocal player)
        {
            List<Outpost> list = ScRegistry.Outposts;
            list.Sort((a, b) => a.Id.CompareTo(b.Id));
            int slots = Slots(), rank = 0, nowIdle = 0;
            bool changed = false;
            List<Outpost> back = null;
            foreach (Outpost o in list)
            {
                if (!o.Holding) continue;
                rank++;
                if (rank > slots)
                {
                    // round fx3 (the user, 2026-11-20: clearing it should still give the "clear again" message and the
                    // Leadership XP): a contested outpost stays contested over the limit - it makes nothing and pays no upkeep
                    // either way - so clearing it retakes it as usual (RetakeCheck), and only then does it go idle here
                    if (o.State != OutpostState.Active) continue;
                    nowIdle++;
                    ScLog.Out("outpost " + o.Id + " at " + o.Name + " goes idle: " + HeldCount() + " held, " + slots + " slot(s)");
                    o.State = OutpostState.Idle;
                    changed = true;
                }
                else if (o.State == OutpostState.Idle)
                {
                    o.State = OutpostState.Active;
                    changed = true;
                    (back ?? (back = new List<Outpost>())).Add(o);
                    ScLog.Out("outpost " + o.Id + " at " + o.Name + " works again: " + HeldCount() + " held, " + slots + " slot(s)");
                }
            }
            if (nowIdle > 0 && player != null) ScCommunity.Tell(player, string.Format(Localization.Get("scOpIdle"), nowIdle, slots), "ui_denied");
            if (back != null)
            {
                foreach (Outpost o in back)
                {
                    ContestCheck(o, w, player);    // sleepers came back while it idled: contested at once, with its own message
                    // the review's L2: coming back is told too, as going idle is
                    if (o.State == OutpostState.Active && player != null) ScCommunity.Tell(player, string.Format(Localization.Get("scOpWorking"), o.Name, slots), "");
                }
            }
            return changed;
        }

        /// <summary>
        /// The rollover's contested check for one outpost (round fx2: shared with ApplySlots): an active one whose building's
        /// persisted sleeper volumes say it filled up again turns contested; a contested one that reads clear is retaken.
        /// </summary>
        private static void ContestCheck(Outpost o, World w, EntityPlayerLocal player)
        {
            if (o == null || w == null || (o.State != OutpostState.Active && o.State != OutpostState.Contested)) return;
            PrefabInstance pi = ScSecurity.PoiByKey(o.Poi);
            if (pi == null) return;
            string why;
            bool cleared = ScSecurity.IsCleared(w, pi, out why, false, true);   // round oc1: an unwoken trigger room doesn't count
            if (!cleared && o.State == OutpostState.Active)
            {
                o.State = OutpostState.Contested;
                if (player != null) ScCommunity.Tell(player, string.Format(Localization.Get("scOpContested"), o.Name, why), "ui_denied");
                ScLog.Out("outpost " + o.Id + " at " + o.Name + " is contested: " + why);
            }
            else if (cleared && o.State == OutpostState.Contested) Retake(o, pi, player);
        }

        /// <summary>
        /// Round oc1 (2026-11-19, the user's report: "When an outpost becomes occupied by sleepers again, clearing it never
        /// clears the 'contested' status"): a contested outpost was only looked at again at the day's rollover, so after the
        /// player cleared it the tab kept saying contested until the next day. Every 10 s (the ticker's beat) each contested
        /// outpost is checked the rollover's way (ScSecurity.IsCleared on the sleeper volumes) and is retaken the moment it
        /// reads clear.
        /// </summary>
        private static void RetakeCheck(World w, EntityPlayerLocal player)
        {
            bool changed = false;
            foreach (Outpost o in ScRegistry.Outposts)
            {
                if (o.State != OutpostState.Contested) continue;
                PrefabInstance pi = ScSecurity.PoiByKey(o.Poi);
                if (pi == null) continue;
                string why;
                if (!ScSecurity.IsCleared(w, pi, out why, false, true)) continue;
                Retake(o, pi, player);
                changed = true;
            }
            if (!changed) return;
            ApplySlots(w, player);    // round fx3: retaken while over the limit - it idles now, with the usual message
            Rebuild();
            RefreshMarkers(player);
            ScRegistry.SaveAtomic();
        }

        /// <summary>Round oc1: what still keeps a contested outpost's building from reading clear ("" when nothing, or not contested).</summary>
        public static string ContestedWhy(Outpost o)
        {
            World w = ScCommunity.World;
            if (o == null || o.State != OutpostState.Contested || w == null) return "";
            PrefabInstance pi = ScSecurity.PoiByKey(o.Poi);
            string why;
            if (pi == null || ScSecurity.IsCleared(w, pi, out why, false, true)) return "";
            return why ?? "";
        }

        /// <summary>Round oc1 (the review's F2): ContestedWhy, short, for the window's Produces line ("" when not contested or nothing is left).</summary>
        public static string ContestedLeftShort(Outpost o)
        {
            World w = ScCommunity.World;
            if (o == null || o.State != OutpostState.Contested || w == null) return "";
            PrefabInstance pi = ScSecurity.PoiByKey(o.Poi);
            return pi != null ? ScSecurity.RemainingShort(pi, true) : "";
        }

        /// <summary>A contested outpost reads clear again: it is ours again (oc1: told, not only logged).</summary>
        private static void Retake(Outpost o, PrefabInstance pi, EntityPlayerLocal player)
        {
            o.State = OutpostState.Active;
            if (pi.sleeperVolumes != null && pi.sleeperVolumes.Count > 0) ScLeadership.Count(ScLeadership.OutpostRetakenEvent);
            if (player != null) ScCommunity.Tell(player, string.Format(Localization.Get("scOpRetaken"), o.Name), "");
            NoteLedger(string.Format(Localization.Get("scOpRetaken"), o.Name));
            ScLog.Out("outpost " + o.Id + " at " + o.Name + " is ours again");
        }

        /// <summary>Forgets remembered radios that are gone (loaded and no longer a radio) or outside a moved home.</summary>
        private static void PruneRadios(World w)
        {
            List<Vector3i> relays = ScRegistry.Community.Relays;
            for (int i = relays.Count - 1; i >= 0; i--)
            {
                Vector3i pos = relays[i];
                if (!ScZone.IsLoaded(pos)) continue;
                BlockValue rv = w.GetBlock(pos);
                if (!rv.isair && rv.Block is BlockScRadio) continue;
                relays.RemoveAt(i);
                ScRadio.Radios.Remove(pos);
                ScLog.Out("radio relay at " + pos + " is gone - forgotten");
                ScRegistry.SaveAtomic();
            }
            List<Vector3i> radios = ScRegistry.Community.Radios;
            if (radios.Count == 0) return;
            bool changed = false;
            for (int i = radios.Count - 1; i >= 0; i--)
            {
                Vector3i pos = radios[i];
                bool drop = ScZone.HasHome && !ScZone.Contains(pos);
                if (!drop && ScZone.IsLoaded(pos))
                {
                    BlockValue bv = w.GetBlock(pos);
                    drop = bv.isair || !(bv.Block is BlockScRadio);
                }
                if (!drop) continue;
                radios.RemoveAt(i);
                ScRadio.Radios.Remove(pos);
                changed = true;
                ScLog.Out("radio at " + pos + " is gone - forgotten");
            }
            if (changed) ScRegistry.SaveAtomic();
        }

        public static string NavClassFor(Outpost o)
        {
            return NavPrefix + (o.Scavenge ? "scavenge" : ScNames.Key(o.Primary));
        }

        private static void AddMarker(Outpost o, EntityPlayer owner)
        {
            try
            {
                if (!o.Holding) return;
                NavObjectManager nav = NavObjectManager.Instance;
                string cls = NavClassFor(o);
                if (nav == null || owner == null || NavObjectClass.GetNavObjectClass(cls) == null) return;
                RemoveMarker(o);
                Vector3 at = o.Centre;
                NavObject obj = nav.RegisterNavObject(cls, at, "", false, -1, owner);
                if (obj != null) obj.name = string.Format(Localization.Get("scOpMarker"), o.Name, YieldText(ShownYield(o)));
                markers[o.Id] = at;
            }
            catch (Exception e)
            {
                ScLog.Once("outpost-marker", e);
            }
        }

        private static void RemoveMarker(Outpost o)
        {
            try
            {
                Vector3 at;
                if (!markers.TryGetValue(o.Id, out at)) return;
                markers.Remove(o.Id);
                NavObjectManager nav = NavObjectManager.Instance;
                if (nav == null) return;
                // the class may have changed (reclassify, scavenge): drop every outpost class at that spot
                foreach (string k in ScNames.ResourceKeys) nav.UnRegisterNavObjectByPosition(at, NavPrefix + k);
                nav.UnRegisterNavObjectByPosition(at, NavPrefix + "scavenge");
            }
            catch (Exception e)
            {
                ScLog.Once("outpost-unmarker", e);
            }
        }

        /// <summary>Re-registers every held outpost's marker (names carry the latest yield).</summary>
        public static void RefreshMarkers(EntityPlayer owner)
        {
            if (owner == null) return;
            foreach (Outpost o in ScRegistry.Outposts)
            {
                if (o.Holding) AddMarker(o, owner);
                else RemoveMarker(o);
            }
        }
    }
}
