using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>A group of survivors holed up in a POI (Enclaves_Plan.md). E1 keeps it as a record on the map; its people come in E2.</summary>
    public sealed class Enclave
    {
        public int Id;
        public string Name = "";
        /// <summary>The POI's footprint: min corner (PrefabInstance.boundingBoxPosition, the key used everywhere) and rotated size.</summary>
        public Vector3i Poi;
        public Vector3i Size;
        /// <summary>The POI is found again by (x, z) and this prefab name on every load: a POI pack update can move the key's y.</summary>
        public string Prefab = "";
        public string PoiName = "";
        /// <summary>The biome at the POI's centre when it was seeded (World.GetBiomeInWorld).</summary>
        public string Biome = "";
        /// <summary>The ScPoiTypes row at seeding ("" = none), frozen like Outpost.Row: the group's archetype in later milestones.</summary>
        public string Row = "";
        public int Tier;
        /// <summary>A raider enclave by nature (seeded hostile, raider classes). Hostility earned in play is TurnedHostile (E2).</summary>
        public bool Hostile;
        /// <summary>
        /// E2: the group's kind (ScEnclaves.Kinds) from the POI's Row through EnclaveKindRows - what they live on and, in a
        /// threat, what they hand over most. Derived when missing (E1 records) and kept when the enclave moves.
        /// </summary>
        public string Kind = "";
        /// <summary>E2: hostile for good since HostileDay (a second hit within the warning window, a kill, a threat). Never undone.</summary>
        public bool TurnedHostile;
        public int HostileDay = -1;
        /// <summary>E2: its group was made (ScEnclavePeople.Generate); until then the next approach makes the rest.</summary>
        public bool PeopleRolled;
        /// <summary>
        /// E3: the leader's trade stock, the enclave's own whoever leads: their body's TraderData as TraderData.Write wrote it
        /// ("1:&lt;bytes&gt;:&lt;base64url&gt;", ScEnclaveDeals.EncodeStock), taken when the body goes and when a trade window with
        /// them closes, and a copy of it given to every leader's body. "" until their first trade (that one stocks them).
        /// </summary>
        public string Stock = "";
        /// <summary>E3: the day the last of their people left to join the player (-1: never) - one every EnclaveRecruitCooldownDays.</summary>
        public int LastRecruitDay = -1;
        /// <summary>
        /// E4: the first day (the request clock's: ScEncRequests.Now) the daily roll may give them a new request - set when one
        /// closes: the day after, plus EnclaveRequestCooldownDays (0: any day).
        /// </summary>
        public int NextRequestDay;
        /// <summary>
        /// E5: the kind of a request done while the player was away (an infestation cleared, a siege beaten, a survivor brought
        /// home who ran ahead): the leader thanks the player for it at the next talk, once ("": nothing owed).
        /// </summary>
        public string Thanks = "";
        /// <summary>
        /// E6: what the group does for the player while Allied (ScEncAllies.Benefits): set by its kind (growers "harvest",
        /// medics "pharmacy", militia "munitions", builders "construction", a road crew "filtration"), a scavengers' group
        /// rolls one of the other eight once from the world seed and its POI; "" for raiders. Fixed and saved.
        /// </summary>
        public string Benefit = "";
        /// <summary>
        /// E6: the first day (the request clock's: ScEncRequests.Now) this group, Allied, may tell the player about one more
        /// enclave - set EnclaveAllyRevealDays ahead each time it does (0: any day).
        /// </summary>
        public int AllyRevealDay;
        /// <summary>
        /// E7: the first day (the request clock's: ScEncRequests.Now) the leader takes another gift from the player - set
        /// EnclaveGiftDays ahead by each one (0: any day). ScEncGifts.
        /// </summary>
        public int GiftDay;
        /// <summary>
        /// E7: the first day (the request clock's) this group, Allied, trades over the radio again - set EnclaveBarterDays ahead
        /// by each trade (0: any day). ScEncBarter.
        /// </summary>
        public int BarterDay;
        /// <summary>
        /// E9: the first day (the request clock's) this group, Allied, answers another radio call - set EnclaveCallCooldownDays
        /// ahead by each call, given back (0) when a call is called off before it came (0: any day). ScEncCalls.
        /// </summary>
        public int CallDay;
        /// <summary>
        /// E9: the radio call of theirs that is on ("" none): CallKind "fighter" or "drop", CallAt the world time it arrives,
        /// CallPid the fighter's person, CallUntil the world time the fighter goes home once they came (0: not come yet).
        /// Saved as [enclave] call=kind:at:pid:until. ScEncCalls.
        /// </summary>
        public string CallKind = "";
        public ulong CallAt;
        public int CallPid;
        public ulong CallUntil;
        /// <summary>
        /// E10: the first day (the request clock's) this hostile group may lean on an outpost again - set EnclaveRaidRestDays
        /// ahead when a demand of theirs is paid or a raid of theirs ends (0: any day). Saved as [enclave] raidday=N. ScEncRaids.
        /// </summary>
        public int RaidDay;
        /// <summary>
        /// E10: people an unattended raid the outpost held cost them before they were ever made (a group never met): the group
        /// is made that many smaller (ScEnclavePeople.GroupSize). Saved as [enclave] raidlost=N. ScEncRaids.
        /// </summary>
        public int RaidLost;
        /// <summary>
        /// E11: the request-clock day they last moved of their own accord (-1: never), the building they left (its key and
        /// name) and their dead then (ScEncMoves.Losses; -1: not known, a move an older build saved). Saved as [enclave]
        /// moved=day:x,y,z:dead:name. ScEncMoves.
        /// </summary>
        public int MovedDay = -1;
        public Vector3i MovedFrom;
        public string MovedFromName = "";
        public int MovedDead = -1;
        /// <summary>E11: the request-clock day of their last death (-1: none since E11 came - older deaths start no move). Saved as [enclave] lossday=N. ScEncMoves.</summary>
        public int LossDay = -1;
        /// <summary>
        /// E11: the request-clock day an infestation has been within EnclaveMoveInfestRange m of their building since, as the daily
        /// pass counts it (-1: none). Saved as [enclave] nestday=N. ScEncMoves.
        /// </summary>
        public int NestDay = -1;
        /// <summary>Standing points: Neutral below 30 (down to EnclaveStandingMin), Friendly 30-69, Allied 70 and up.</summary>
        public int Standing;
        /// <summary>Discovered: its marker shows. KnownDay = the day it was found (-1 = not yet).</summary>
        public bool Known;
        public int KnownDay = -1;
        public int SeededDay;
        /// <summary>
        /// How it came: "new" (a new save's first start), "late" (an existing save), "top" (a raised target), "moved" (its POI
        /// went; E11: or it moved of its own accord), "refill" (E6: a new group after a lost one).
        /// </summary>
        public string Mode = "new";
        /// <summary>
        /// This load found neither its POI nor a free one to move to, though one may come free (Validate): kept and tried
        /// again on the next load, off the map and out of the lookups meanwhile. Not saved.
        /// </summary>
        public bool Lost;

        // ---- E2, session only (never saved) ----
        /// <summary>The attack rule's clock (real time): the last hit that counted, and the warning it gave (-1: none).</summary>
        public float LastHitAt = -1f, WarnedAt = -1f;
        /// <summary>A threat's grace (real time): its hostility is on only from then (ScEnclavePeople.RebuildHostile).</summary>
        public float GraceUntil;

        /// <summary>A raider enclave, or one turned hostile: what standing, markers and talk act on.</summary>
        public bool IsHostile { get { return Hostile || TurnedHostile; } }

        /// <summary>The POI footprint as an inclusive box {xMin, xMax, zMin, zMax}.</summary>
        public int[] Box { get { return new[] { Poi.x, Poi.x + Math.Max(1, Size.x) - 1, Poi.z, Poi.z + Math.Max(1, Size.z) - 1 }; } }

        public Vector3 Centre { get { return new Vector3(Poi.x + Size.x * 0.5f, Poi.y, Poi.z + Size.z * 0.5f); } }

        public bool Contains(int x, int z) { return x >= Poi.x && x < Poi.x + Size.x && z >= Poi.z && z < Poi.z + Size.z; }

        /// <summary>XZ distance from a point to the footprint (0 inside it), measured as Box is, with no array (the per-second tick).</summary>
        public float DistanceXZ(float x, float z)
        {
            float x0 = Poi.x, x1 = Poi.x + Math.Max(1, Size.x), z0 = Poi.z, z1 = Poi.z + Math.Max(1, Size.z);
            float dx = x < x0 ? x0 - x : (x > x1 ? x - x1 : 0f);
            float dz = z < z0 ? z0 - z : (z > z1 ? z - z1 : 0f);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }

    /// <summary>
    /// Enclaves, milestone E1 (E1_SPEC.md, Enclaves_Plan.md 2.1-2.2 and 2.13): groups of survivors holed up in POIs across
    /// the map, kept as registry records. E1 puts them on the map - seeding, names, discovery, markers - and makes their
    /// POIs theirs: no sleeper zombies (Patches_Enclaves and the pre-clear here), no trader quests or vanilla respawns (the
    /// CheckForAnyPlayerHome postfix), no quest re-arm (Patches_Quest), no land claims (the claim gate in Patches_Outposts),
    /// no outposts (ScOutposts.CanEstablish), and SC's own POI pickers pass them by. E2 gives them people (ScEnclavePeople)
    /// and adds here the kind, the hostility earned in play and its standing helpers, and Fall (a group gone).
    ///
    /// Seeding runs on the host at GameStartDone, when the whole POI list is there and only the chunks round the player
    /// have started loading (StartAsServer adds the player's chunk observer just before): a new save at its first start;
    /// an existing save the first time this build loads it (the late seed: nowhere near a claim, nothing in loaded chunks,
    /// POIs nobody has visited preferred); on every later load each enclave's POI is found again (or the
    /// enclave moves to the nearest free one) and a raised target is topped up. Everything is drawn from the world seed in
    /// (x, z) order, so a save always gets the same enclaves from the same POIs. Host only, main thread; the lookups the
    /// patches use read volatile arrays rebuilt here (the ScOutposts pattern). E11 (ScEncMoves) moves a group of its own
    /// accord through the same building swap as Validate's (TakeBuilding: MoveTarget, MoveTo).
    /// </summary>
    public static class ScEnclaves
    {
        public const string NavPrefix = "sc_enclave_";
        /// <summary>Every marker class (nav_objects.xml): a marker is re-registered under the class of its current standing.</summary>
        private static readonly string[] NavKinds = { "neutral", "friendly", "allied", "hostile" };
        private const string Salt = "sc.enclaves.v1";
        /// <summary>E6: the seeding mode (and Enclave.Mode) of a new group that moved in after a lost one (Refill).</summary>
        public const string RefillMode = "refill";
        /// <summary>E12: the cause Disband gives Fallen for a group that joined the player's community whole (ScEncJoin).</summary>
        public const string CauseJoined = "joined";
        /// <summary>Prefab or POI-name words that make a likely raider den (the hostile ranking): a word of the name that starts with one.</summary>
        private static readonly string[] HostileWords = { "army", "military", "prison", "police", "fort", "junk", "scrap", "industrial", "factory", "camp" };
        private static readonly string[] DirKeys = { "scDirN", "scDirNE", "scDirE", "scDirSE", "scDirS", "scDirSW", "scDirW", "scDirNW" };
        /// <summary>E2: the kinds of group (Enclaves_Plan.md 2.7; EnclaveKindRows maps POI rows onto them, any other row is general).</summary>
        public static readonly string[] Kinds = { "growers", "medics", "militia", "builders", "roadcrew", "general" };

        /// <summary>
        /// E6's seam: an enclave fell (Fall: wiped out, or E5's lost siege; E12's Disband: it joined the player, CauseJoined) -
        /// its record is gone and its POI free again. The refill of lost enclaves hooks in here. Main thread, after the save.
        /// </summary>
        public static event Action<Enclave, string> Fallen;

        /// <summary>Footprints {xMin, xMax, zMin, zMax} and keys of every enclave POI: read by patches that may run off the main thread.</summary>
        private static volatile int[][] boxes = new int[0][];
        private static volatile Vector3i[] keys = new Vector3i[0];
        /// <summary>
        /// Set on the main thread while "sc enclave sim" plans a fresh seeding: the lookups (and so CheckForAnyPlayerHome) then
        /// ignore the enclaves already there. Per thread, so a patch running elsewhere meanwhile still sees them.
        /// </summary>
        [ThreadStatic] private static bool ignoreExisting;

        private static readonly Dictionary<int, Vector3> markers = new Dictionary<int, Vector3>();
        private static bool markersRestored;
        /// <summary>The frame the radio last revealed one: a catch-up of several missed days reveals one, not one per day.</summary>
        private static int radioFrame = -1;
        /// <summary>
        /// Set once OnGameStart has run: the ticker starts a frame or more before GameStartDone, and must not find, mark or
        /// reveal an enclave that Validate is about to move or drop.
        /// </summary>
        private static bool ready;

        /// <summary>OnGameStart has run this session (E2's people wait for it too).</summary>
        public static bool Ready { get { return ready; } }

        public static List<Enclave> All { get { return ScRegistry.Enclaves; } }

        public static void Reset()
        {
            markers.Clear();
            markersRestored = false;
            radioFrame = -1;
            ready = false;
            boxes = new int[0][];
            keys = new Vector3i[0];
        }

        /// <summary>The registry was (re)loaded or reset (ScOutposts.OnRegistryChanged): ids squared, lookups rebuilt, markers restored on the next tick.</summary>
        public static void OnRegistryChanged()
        {
            markersRestored = false;
            markers.Clear();
            // ids must be unique (later records - people, requests - will point at them) and the counter ahead of all of them
            Community c = ScRegistry.Community;
            var ids = new HashSet<int>();
            int max = 0;
            foreach (Enclave e in All) if (e.Id > max) max = e.Id;
            foreach (Enclave e in All)
            {
                if (e.Id <= 0 || !ids.Add(e.Id))
                {
                    int old = e.Id;
                    e.Id = Math.Max(c.NextEnclaveId, max) + 1;
                    max = e.Id;
                    ids.Add(e.Id);
                    ScLog.Warn("enclave '" + e.Name + "' had a duplicate id " + old + " - now " + e.Id);
                }
                if (e.Id > c.NextEnclaveId) c.NextEnclaveId = e.Id;
            }
            Rebuild();
            ScEnclavePeople.OnRegistryChanged();    // E2: person ids, no body known, the hostility lookup
            ScEncRequests.OnRegistryChanged();      // E4: request ids, unknown kinds dropped, the session's pick and journal pass
            ScEncCalls.OnRegistryChanged();         // E9: no called fighter is out; one loaded live comes back after the start
        }

        /// <summary>Rebuilds the thread-safe lookup arrays (after any change to the list; empty while Enclaves is off).</summary>
        public static void Rebuild()
        {
            var b = new List<int[]>();
            var k = new List<Vector3i>();
            if (ScConfig.Enclaves)
            {
                foreach (Enclave e in All)
                {
                    if (e.Lost) continue;       // no building this load: nothing to protect
                    b.Add(e.Box);
                    k.Add(e.Poi);
                }
            }
            boxes = b.ToArray();
            keys = k.ToArray();
            ScEnclavePeople.RebuildHostile();       // E2: a lost or gone enclave's hostility goes with its lookups
        }

        // ------------------------------------------------------------------ queries (thread-safe) ----

        public static bool IsEnclavePoiKey(Vector3i key)
        {
            if (ignoreExisting) return false;
            Vector3i[] k = keys;
            for (int i = 0; i < k.Length; i++) if (k[i] == key) return true;
            return false;
        }

        public static bool IsEnclavePoi(PrefabInstance pi)
        {
            return pi != null && keys.Length > 0 && IsEnclavePoiKey(pi.boundingBoxPosition);
        }

        /// <summary>SleeperEventData.position is the POI key as a Vector3.</summary>
        public static bool IsEnclavePoi(Vector3 poiPosition)
        {
            return keys.Length > 0 && IsEnclavePoiKey(World.worldToBlockPos(poiPosition));
        }

        /// <summary>Does the box (inclusive XZ) touch an enclave POI's footprint? (CheckForAnyPlayerHome postfix, the claim gate.)</summary>
        public static bool BoxHitsEnclave(Vector3i min, Vector3i max)
        {
            if (ignoreExisting) return false;
            int[][] b = boxes;
            for (int i = 0; i < b.Length; i++)
            {
                int[] r = b[i];
                if (min.x <= r[1] && max.x >= r[0] && min.z <= r[3] && max.z >= r[2]) return true;
            }
            return false;
        }

        /// <summary>Is the point within margin metres (XZ) of a living enclave's footprint? (E2's wild-spawn rule.)</summary>
        public static bool NearEnclave(Vector3 p, float margin)
        {
            int[][] b = boxes;
            for (int i = 0; i < b.Length; i++) if (ToBox(p.x, p.z, b[i]) <= margin) return true;
            return false;
        }

        // ------------------------------------------------------------------ records (main thread) ----

        public static Enclave ById(int id)
        {
            foreach (Enclave e in All) if (e.Id == id) return e;
            return null;
        }

        public static Enclave ByPoi(Vector3i key)
        {
            foreach (Enclave e in All) if (e.Poi == key) return e;
            return null;
        }

        /// <summary>The enclave holding this POI while enclaves are on (null when none, or Enclaves is off).</summary>
        public static Enclave Holding(PrefabInstance pi)
        {
            Enclave e = pi != null && ScConfig.Enclaves ? ByPoi(pi.boundingBoxPosition) : null;
            return e != null && !e.Lost ? e : null;
        }

        /// <summary>The first enclave whose footprint the box (inclusive XZ) touches: the name for a refusal.</summary>
        public static Enclave FirstIn(Vector3i min, Vector3i max)
        {
            if (!ScConfig.Enclaves) return null;
            foreach (Enclave e in All)
            {
                if (e.Lost) continue;
                int[] r = e.Box;
                if (min.x <= r[1] && max.x >= r[0] && min.z <= r[3] && max.z >= r[2]) return e;
            }
            return null;
        }

        /// <summary>The live POI of an enclave (found by x, z and prefab; the exact key as a fallback), or null.</summary>
        internal static PrefabInstance PoiOf(Enclave e)
        {
            PoiEntry pe = ScPoiIndex.Built ? ScPoiIndex.Find(e.Poi.x, e.Poi.z, e.Prefab) : null;
            return pe != null ? pe.Pi : ScSecurity.PoiByKey(e.Poi);
        }

        /// <summary>XZ distance from a point to an inclusive box {xMin, xMax, zMin, zMax} (0 inside it).</summary>
        private static float ToBox(float x, float z, int[] b)
        {
            float dx = x < b[0] ? b[0] - x : (x > b[1] + 1 ? x - b[1] - 1 : 0f);
            float dz = z < b[2] ? b[2] - z : (z > b[3] + 1 ? z - b[3] - 1 : 0f);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        public static float DistanceToBox(Enclave e, Vector3 p) { return e.DistanceXZ(p.x, p.z); }

        private static float Dist(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx, dz = az - bz;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>"900 m north-east": from one point to another (XZ).</summary>
        public static string Where(Vector3 from, Vector3 to)
        {
            float dx = to.x - from.x, dz = to.z - from.z;
            return Where(from, to, Mathf.RoundToInt(Mathf.Sqrt(dx * dx + dz * dz)));
        }

        /// <summary>As Where, with the distance given (I3: to a footprint's nearest edge, the direction still to its centre).</summary>
        public static string Where(Vector3 from, Vector3 to, int metres)
        {
            float dx = to.x - from.x, dz = to.z - from.z;
            int sector = Mathf.RoundToInt(Mathf.Repeat(Mathf.Atan2(dx, dz) * Mathf.Rad2Deg, 360f) / 45f) % 8;   // 0 = north (+z), 2 = east (+x)
            return string.Format(Localization.Get("scEnclaveWhere"), metres, Localization.Get(DirKeys[sector]));
        }

        public static string StandingKey(Enclave e)
        {
            return e.IsHostile ? "hostile" : e.Standing >= 70 ? "allied" : e.Standing >= 30 ? "friendly" : "neutral";
        }

        /// <summary>E2: the kind of group a POI row makes (EnclaveKindRows; any other row, or none, is general).</summary>
        public static string KindFor(string row)
        {
            string k;
            return !string.IsNullOrEmpty(row) && ScConfig.EnclaveKindRows.TryGetValue(row, out k) ? k : "general";
        }

        /// <summary>Neutral below 30, Friendly 30-69, Allied 70+; Hostile whatever the points (Localization).</summary>
        public static string StandingWord(Enclave e)
        {
            switch (StandingKey(e))
            {
                case "hostile": return Localization.Get("scEnclaveHostile");
                case "allied": return Localization.Get("scEnclaveAllied");
                case "friendly": return Localization.Get("scEnclaveFriendly");
                default: return Localization.Get("scEnclaveNeutral");
            }
        }

        // ------------------------------------------------------------------ world start ----

        /// <summary>
        /// GameStartDone (host): seed a new save (or, with EnclaveLateSeed, an existing one that has none yet), else find
        /// every enclave's POI again and top up a raised target; then keep every enclave POI's sleeper areas clear, rebuild
        /// the lookups and save. One log line per run.
        /// </summary>
        public static void OnGameStart()
        {
            try
            {
                if (!ScCommunity.IsHost || !ScRegistry.Loaded) return;
                World w = ScCommunity.World;
                if (w == null) return;
                // E2: a kind for every enclave seeded before E2 (its POI's row through EnclaveKindRows)
                foreach (Enclave e in All) if (string.IsNullOrEmpty(e.Kind)) e.Kind = KindFor(e.Row);
                if (!ScConfig.Enclaves)
                {
                    Rebuild();
                    if (All.Count > 0)
                    {
                        // the rooms they kept clear are saved with the world: given back, they respawn after vanilla's own wait
                        int freed = 0;
                        foreach (Enclave e in All) freed += Release(PoiOf(e), w);
                        ScLog.Out("enclaves: off (Enclaves=false) - " + All.Count + " kept in the registry, not on the map and not protected"
                                  + (freed > 0 ? "; " + freed + " sleeper area(s) they kept clear respawn after the loot respawn wait" : ""));
                    }
                    return;
                }
                if (!ScPoiIndex.EnsureBuilt())
                {
                    // never drop or move anything on a world whose POI list did not come up
                    Rebuild();
                    ScLog.Warn("enclaves: no POI list on this world - nothing seeded or checked this session (" + All.Count + " kept)");
                    return;
                }
                Community c = ScRegistry.Community;
                string line;
                if (!c.EnclaveSeeded)
                {
                    if (ScRegistry.SaveIsNew) line = Seed(w, "new");
                    else if (ScConfig.EnclaveLateSeed) line = Seed(w, "late");
                    else line = "none in this save yet, and EnclaveLateSeed is off - none seeded";
                }
                else line = Validate(w);
                ScEncAllies.EnsureBenefits(w);      // E6: each group's benefit (seeded just now, or before E6)
                int cleared = PreClearAll(w);
                Rebuild();
                markersRestored = false;
                ScLog.Out("enclaves: " + line + (cleared > 0 ? "; " + cleared + " generated sleeper area(s) in their POIs kept clear" : ""));
                ScRegistry.SaveAtomic();
            }
            catch (Exception e)
            {
                ScLog.Once("enclave-gamestart", e);
            }
            finally
            {
                ready = true;
            }
        }

        /// <summary>A seeding run's plan: the rules it ran under, what it saw and what it picked (saved, or printed by "sc enclave sim").</summary>
        private sealed class Plan
        {
            public string Mode = "new";
            /// <summary>The late-seed rules: no claim within EnclaveClaimBuffer, nothing in loaded chunks, unvisited POIs preferred.</summary>
            public bool Late;
            /// <summary>Take the start ring first (a new or late seed; a top-up already has its neighbours).</summary>
            public bool Ring;
            public int Seed;
            /// <summary>N for this world, and how many this run places (a top-up places what the raised target lacks).</summary>
            public int Target, Want;
            public float Width, Km2, D0, D;
            public Vector3 Start;
            public string StartWhat = "";
            public int Real, Candidates;
            public readonly Dictionary<string, int> Refused = new Dictionary<string, int>();
            public readonly List<Pick> Picks = new List<Pick>();
            public int HostileWanted;

            public int Hostiles { get { int n = 0; foreach (Pick p in Picks) if (p.Hostile) n++; return n; } }
            public int Rings { get { int n = 0; foreach (Pick p in Picks) if (p.Ring) n++; return n; } }
        }

        private sealed class Pick
        {
            public PoiEntry E;
            public float Weight;
            public int Index;
            public double Order;
            public bool Chosen, Ring, Hostile;
            public int Score;
            public double Tie;
            public string Name = "";
        }

        /// <summary>What the candidate filter reads from the world once per run.</summary>
        private sealed class Rules
        {
            public World W;
            public Vector3i Min, Max;
            public bool Late;
            /// <summary>Trader protect boxes and every claim's rectangles, inclusive {xMin, xMax, zMin, zMax}.</summary>
            public readonly List<int[]> Traders = new List<int[]>();
            public readonly List<int[]> Claims = new List<int[]>();
            public readonly List<Vector2> Spawns = new List<Vector2>();
            /// <summary>POIs already in use (radio contacts, recruit favours, physical runners, radio scouting marks, the player's quests).</summary>
            public readonly HashSet<Vector3i> Taken = new HashSet<Vector3i>();
            /// <summary>The enclaves' own POIs.</summary>
            public readonly HashSet<Vector3i> Enclaves = new HashSet<Vector3i>();
            public bool HasPlayer;
            public Vector3 Player;
            public float PlayerRange;
        }

        private static Rules RulesFor(World w, bool late, Vector3i min, Vector3i max)
        {
            var r = new Rules { W = w, Min = min, Max = max, Late = late };
            DynamicPrefabDecorator dec = GameManager.Instance.GetDynamicPrefabDecorator();
            List<TraderArea> areas = dec != null ? dec.GetTraderAreas() : null;
            if (areas != null)
                foreach (TraderArea t in areas)
                    if (t != null) r.Traders.Add(new[] { t.ProtectPosition.x, t.ProtectPosition.x + Math.Max(1, t.ProtectSize.x) - 1, t.ProtectPosition.z, t.ProtectPosition.z + Math.Max(1, t.ProtectSize.z) - 1 });
            SpawnPointList sp = GameManager.Instance.GetSpawnPointList();
            if (sp != null) foreach (SpawnPoint s in sp) if (s != null) r.Spawns.Add(new Vector2(s.spawnPosition.position.x, s.spawnPosition.position.z));
            foreach (Contact ct in ScRegistry.Contacts) r.Taken.Add(ct.PoiKey);
            foreach (Prospect p in ScRegistry.Prospects.Values) if (p.Favour == FavourKind.Poi && !p.Done) r.Taken.Add(p.PoiPos);
            foreach (Member m in ScRegistry.Members.Values) if (ScRunners.IsPhysical(m)) r.Taken.Add(m.RunDest);
            foreach (Scout s in ScRegistry.Community.Scouts) r.Taken.Add(s.Poi);     // a radio scouting mark still on the map
            ScEncRequests.AddTargets(r.Taken);       // E5: a missing survivor's building, an infestation an enclave asked to clear
            // the POIs the player's quests in progress lead to: an enclave there would lock a trader quest already taken out
            EntityPlayerLocal qp = ScCommunity.LocalPlayer;
            if (qp != null && qp.QuestJournal != null && qp.QuestJournal.quests != null)
            {
                foreach (Quest q in qp.QuestJournal.quests)
                {
                    Vector3 qpos;
                    if (q != null && q.CurrentState == Quest.QuestState.InProgress && q.GetPositionData(out qpos, Quest.PositionDataTypes.POIPosition))
                        r.Taken.Add(World.worldToBlockPos(qpos));
                }
            }
            if (!ignoreExisting) foreach (Enclave e in All) r.Enclaves.Add(e.Poi);
            if (late)
            {
                var pp = GameManager.Instance.persistentPlayers;
                if (pp != null && pp.m_lpBlockMap != null) foreach (Vector3i claim in pp.m_lpBlockMap.Keys) ScZone.ClaimRects(claim, r.Claims);
                // the chunks round the player may still be loading at GameStartDone: their view distance counts as loaded
                EntityPlayerLocal pl = ScCommunity.LocalPlayer;
                if (pl != null) { r.HasPlayer = true; r.Player = pl.position; r.PlayerRange = (GameUtils.GetViewDistance() + 1) * 16f; }
            }
            return r;
        }

        /// <summary>A district's weight (EnclaveKindWeights; a district it does not name weighs as "other").</summary>
        private static float KindWeight(string kind)
        {
            float w;
            if (ScConfig.EnclaveKindWeights.TryGetValue(kind, out w)) return w;
            return ScConfig.EnclaveKindWeights.TryGetValue("other", out w) ? w : 0f;
        }

        /// <summary>The POI's weight by its district; a weight-0 district rules it out whatever else it is tagged (downtown).</summary>
        private static float KindWeight(PoiEntry e)
        {
            foreach (string k in e.Kinds) if (KindWeight(k) <= 0f) return 0f;
            return KindWeight(e.Kind);
        }

        private static bool ExcludedName(string lower)
        {
            foreach (string x in ScConfig.EnclavePoiExcludes) if (x.Length > 0 && lower.IndexOf(x, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        /// <summary>Why this POI cannot hold an enclave under these rules (a short reason "sc enclave sim" counts), or null when it can.</summary>
        private static string Refuse(PoiEntry e, Rules r)
        {
            if (e.Sleepers <= 0) return "no sleepers";
            if (e.Tier < ScConfig.EnclaveTierMin || e.Tier > ScConfig.EnclaveTierMax) return "tier";
            if (e.Side < ScConfig.EnclaveSizeMin || e.Side > ScConfig.EnclaveSizeMax) return "size";
            if (KindWeight(e) <= 0f) return "district";
            if (ExcludedName(e.Lower)) return "name";
            float edge = ScConfig.EnclaveEdgeDistance;
            if (e.X - r.Min.x < edge || r.Max.x - e.X < edge || e.Z - r.Min.z < edge || r.Max.z - e.Z < edge) return "edge";
            foreach (int[] t in r.Traders) if (ToBox(e.X, e.Z, t) < ScConfig.EnclaveTraderDistance) return "trader";
            foreach (Vector2 s in r.Spawns) if (Dist(e.X, e.Z, s.x, s.y) < ScConfig.EnclaveSpawnDistance) return "spawn";
            if (r.Enclaves.Contains(e.Key)) return "enclave";
            if (ScInfestations.IsInfestedPoiKey(e.Key)) return "infested";     // I1: never an infested building (until it is cleared)
            if (ScOutposts.IsOutpostPoiKey(e.Key)) return "outpost";
            if (ScSecurity.OverlapsHome(e.Pi)) return "home";
            if (r.Taken.Contains(e.Key)) return "in use";
            // bedrolls, claims (LandClaimUpgrade zones too), outposts and other enclaves: the one "someone lives here" test
            if (e.Pi.CheckForAnyPlayerHome(r.W) != GameUtils.EPlayerHomeType.None) return "claimed";
            if (!r.Late) return null;
            int b = ScConfig.EnclaveClaimBuffer;
            int x0 = e.Key.x - b, x1 = e.Key.x + Math.Max(1, e.Size.x) - 1 + b, z0 = e.Key.z - b, z1 = e.Key.z + Math.Max(1, e.Size.z) - 1 + b;
            foreach (int[] c in r.Claims) if (x0 <= c[1] && x1 >= c[0] && z0 <= c[3] && z1 >= c[2]) return "near a claim";
            if (Loaded(e, r)) return "loaded";
            return null;
        }

        /// <summary>Any corner or the centre of the footprint in a loaded chunk, or the footprint within the player's view: someone would see the enclave appear.</summary>
        private static bool Loaded(PoiEntry e, Rules r)
        {
            return LoadedBox(e.Key, e.Size, e.X, e.Z, r.HasPlayer, r.Player, r.PlayerRange);
        }

        private static bool LoadedBox(Vector3i key, Vector3i size, float cx, float cz, bool hasPlayer, Vector3 player, float range)
        {
            int ax = key.x, az = key.z, bx = key.x + Math.Max(1, size.x) - 1, bz = key.z + Math.Max(1, size.z) - 1;
            if (ScZone.IsLoaded(new Vector3i(ax, 0, az)) || ScZone.IsLoaded(new Vector3i(bx, 0, az)) || ScZone.IsLoaded(new Vector3i(ax, 0, bz))
                || ScZone.IsLoaded(new Vector3i(bx, 0, bz)) || ScZone.IsLoaded(new Vector3i((int)cx, 0, (int)cz))) return true;
            return hasPlayer && ToBox(player.x, player.z, new[] { ax, bx, az, bz }) < range;
        }

        /// <summary>
        /// E11 (ScEncMoves): the enclave's building is in loaded chunks or within the local player's view distance - the late
        /// seed's "loaded" rule (Loaded) for the building a group would leave: someone could see them go.
        /// </summary>
        internal static bool InView(Enclave e)
        {
            EntityPlayerLocal pl = ScCommunity.LocalPlayer;
            Vector3 c = e.Centre;
            return LoadedBox(e.Poi, e.Size, c.x, c.z, pl != null, pl != null ? pl.position : Vector3.zero, (GameUtils.GetViewDistance() + 1) * 16f);
        }

        private static bool WorldExtent(World w, out Vector3i min, out Vector3i max)
        {
            min = max = Vector3i.zero;
            return w != null && w.GetWorldExtent(out min, out max) && max.x > min.x && max.z > min.z;
        }

        /// <summary>N = clamp(round(EnclavesPerKm2 x km2), EnclaveMin, EnclaveMax) over the world's XZ extent.</summary>
        private static int TargetCount(Vector3i min, Vector3i max, out float km2)
        {
            km2 = (max.x - min.x) / 1000f * ((max.z - min.z) / 1000f);
            return Mathf.Clamp(Mathf.RoundToInt(ScConfig.EnclavesPerKm2 * km2), ScConfig.EnclaveMin, ScConfig.EnclaveMax);
        }

        /// <summary>E6 (sc enclave refill): this world's target count now, -1 without a world.</summary>
        public static int TargetNow()
        {
            Vector3i min, max;
            float km2;
            World w = ScCommunity.World;
            return w != null && WorldExtent(w, out min, out max) ? TargetCount(min, max, out km2) : -1;
        }

        /// <summary>
        /// Where the start ring and the hostile rule measure from: a late run (and a top-up) prefers the home claim; then
        /// the local player (on a new save, where they just spawned), the first spawn point, the world centre.
        /// </summary>
        private static Vector3 StartPoint(bool late, Vector3i min, Vector3i max, out string what)
        {
            if (late && ScZone.HasHome)
            {
                what = "home";
                Vector3i h = ScZone.Home;
                return new Vector3(h.x, h.y, h.z);
            }
            EntityPlayerLocal pl = ScCommunity.LocalPlayer;
            if (pl != null) { what = "you"; return pl.position; }
            SpawnPointList sp = GameManager.Instance.GetSpawnPointList();
            if (sp != null && sp.Count > 0 && sp[0] != null) { what = "the first spawn point"; return sp[0].spawnPosition.position; }
            what = "the world centre";
            return new Vector3((min.x + max.x) * 0.5f, 0f, (min.z + max.z) * 0.5f);
        }

        /// <summary>
        /// The run's seed: World.Seed with SC's salt; a late run and a top-up mix in the mode and the day, so a top-up does
        /// not replay the first run's draws.
        /// </summary>
        private static int SeedFor(World w, string mode, int day)
        {
            int s = w.Seed ^ Salt.GetStableHashCode();
            return mode == "new" ? s : s ^ (mode + ":" + day.ToString(CultureInfo.InvariantCulture)).GetStableHashCode();
        }

        /// <summary>
        /// Plans one seeding run (nothing is saved; pois_and_seeding.md section 6): the candidates, a weighted random order
        /// (key u^(1/w) over the candidates in (x, z) order, one RNG from the run's seed), the start ring, greedy spacing
        /// relaxed 15% a pass down to EnclaveSpacingFloor, the hostiles, then each pick's name from its own stream.
        /// E6's refill (mode "refill": the late rules, no start ring) brings its own seed, the POI of the group it replaces
        /// (never picked again) and that group's name (never given again).
        /// </summary>
        private static Plan MakePlan(World w, string mode, int want, int? seed = null, Vector3i? exclude = null, string avoidName = null)
        {
            Vector3i min, max;
            if (!WorldExtent(w, out min, out max)) return null;
            bool late = mode != "new";
            var p = new Plan { Mode = mode, Late = late, Ring = mode != "top" && mode != RefillMode };
            p.Target = TargetCount(min, max, out p.Km2);
            p.Want = want < 0 ? p.Target : want;
            p.Width = Math.Min(max.x - min.x, max.z - min.z);
            p.Seed = seed ?? SeedFor(w, mode, ScCommunity.Today);
            p.Start = StartPoint(late, min, max, out p.StartWhat);
            Rules r = RulesFor(w, late, min, max);
            if (exclude.HasValue) r.Taken.Add(exclude.Value);      // E6: a refill never moves into the lost group's building
            var existing = ignoreExisting ? new List<Enclave>() : All;

            var cands = new List<Pick>();
            foreach (PoiEntry e in ScPoiIndex.All)
            {
                string why = Refuse(e, r);
                float weight = why == null ? KindWeight(e) : 0f;
                // a POI never generated in this save (its sleeper areas do not exist yet): nobody has looted it
                if (why == null && late && e.Pi.sleeperVolumes.Count == 0) weight *= ScConfig.EnclaveUnvisitedWeight;
                if (why == null && weight <= 0f) why = "weight";
                if (why != null) { int n; p.Refused.TryGetValue(why, out n); p.Refused[why] = n + 1; continue; }
                cands.Add(new Pick { E = e, Weight = weight });
            }
            p.Real = ScPoiIndex.Count;
            p.Candidates = cands.Count;
            // (x, z) order first: load order and ids depend on which POI packs loaded
            cands.Sort((a, b) =>
            {
                int k = a.E.Key.x.CompareTo(b.E.Key.x);
                if (k != 0) return k;
                k = a.E.Key.z.CompareTo(b.E.Key.z);
                return k != 0 ? k : string.CompareOrdinal(a.E.Prefab, b.E.Prefab);
            });
            for (int i = 0; i < cands.Count; i++) cands[i].Index = i;

            var anchors = new List<Vector2>();
            foreach (Enclave e in existing) { Vector3 c = e.Centre; anchors.Add(new Vector2(c.x, c.z)); }
            GameRandom rng = GameRandomManager.Instance.CreateGameRandom(p.Seed);
            try
            {
                foreach (Pick c in cands) c.Order = Math.Pow(rng.RandomDouble, 1.0 / c.Weight);
                var order = new List<Pick>(cands);
                order.Sort((a, b) => a.Order != b.Order ? b.Order.CompareTo(a.Order) : a.Index.CompareTo(b.Index));

                // the start ring: a couple of friendly neighbours within reach of where the player starts
                if (p.Ring && ScConfig.EnclaveStartRing > 0)
                {
                    int ring = 0;
                    foreach (Pick c in order)
                    {
                        if (ring >= ScConfig.EnclaveStartRing || p.Picks.Count >= p.Want) break;
                        float d = Dist(c.E.X, c.E.Z, p.Start.x, p.Start.z);
                        if (d < ScConfig.EnclaveStartRingMin || d > ScConfig.EnclaveStartRingMax) continue;
                        if (TooClose(c, p.Picks, anchors, ScConfig.EnclaveStartRingSpacing)) continue;
                        c.Chosen = c.Ring = true;
                        p.Picks.Add(c);
                        ring++;
                    }
                }

                // greedy Poisson-disk over the weighted order, D relaxed 15% a pass when the world has no room for N
                p.D0 = Math.Max(ScConfig.EnclaveSpacingMin, ScConfig.EnclaveSpacingFactor * Mathf.Sqrt((max.x - min.x) * (float)(max.z - min.z) / Math.Max(1, p.Target)));
                float dd = p.D0, floor = Math.Max(1f, ScConfig.EnclaveSpacingFloor);    // a floor of 0 would end the loop only by float underflow
                while (true)
                {
                    foreach (Pick c in order)
                    {
                        if (p.Picks.Count >= p.Want) break;
                        if (c.Chosen || TooClose(c, p.Picks, anchors, dd)) continue;
                        c.Chosen = true;
                        p.Picks.Add(c);
                    }
                    if (p.Picks.Count >= p.Want || dd <= floor) break;
                    dd = Math.Max(floor, dd * 0.85f);
                }
                p.D = dd;

                // hostiles: a share of all enclaves, never a ring member, never near the start or a spawn point (half that on
                // a small map), the likeliest raider dens first: tier 3-4, military / prison / junk words, wasteland or burnt
                int hostileBefore = 0;
                foreach (Enclave e in existing) if (e.Hostile) hostileBefore++;
                p.HostileWanted = Math.Max(0, Mathf.RoundToInt(ScConfig.EnclaveHostileShare * (existing.Count + p.Picks.Count)) - hostileBefore);
                float minStart = ScConfig.EnclaveHostileMinStart * (p.Width < 4000f ? 0.5f : 1f);
                var eligible = new List<Pick>();
                foreach (Pick c in p.Picks)
                {
                    if (c.Ring || Dist(c.E.X, c.E.Z, p.Start.x, p.Start.z) < minStart) continue;
                    bool nearSpawn = false;
                    foreach (Vector2 s in r.Spawns) if (Dist(c.E.X, c.E.Z, s.x, s.y) < minStart) { nearSpawn = true; break; }
                    if (nearSpawn) continue;
                    c.Score = HostileScore(c.E);
                    c.Tie = rng.RandomDouble;
                    eligible.Add(c);
                }
                eligible.Sort((a, b) => a.Score != b.Score ? b.Score.CompareTo(a.Score) : a.Tie.CompareTo(b.Tie));
                for (int i = 0; i < eligible.Count && i < p.HostileWanted; i++) eligible[i].Hostile = true;
            }
            finally
            {
                GameRandomManager.Instance.FreeGameRandom(rng);
            }

            // names: each its own stream (a re-rolled name changes no other), unique in the save
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Enclave e in existing) if (!string.IsNullOrEmpty(e.Name)) taken.Add(e.Name);
            if (!string.IsNullOrEmpty(avoidName)) taken.Add(avoidName);
            foreach (Pick c in p.Picks)
            {
                GameRandom nr = GameRandomManager.Instance.CreateGameRandom(p.Seed ^ unchecked(c.E.Key.x * 73856093) ^ unchecked(c.E.Key.z * 19349663));
                try { c.Name = ScEnclaveNames.Unique(nr, taken, c.Hostile, ScEnclaveNames.BiomeKey(c.E.Biome), ScEnclaveNames.PoiKind(c.E.Row, c.E.Prefab, c.E.Display)); }
                finally { GameRandomManager.Instance.FreeGameRandom(nr); }
            }
            return p;
        }

        private static bool TooClose(Pick c, List<Pick> picks, List<Vector2> anchors, float d)
        {
            float d2 = d * d;
            foreach (Pick o in picks)
            {
                float dx = o.E.X - c.E.X, dz = o.E.Z - c.E.Z;
                if (dx * dx + dz * dz < d2) return true;
            }
            foreach (Vector2 a in anchors)
            {
                float dx = a.x - c.E.X, dz = a.y - c.E.Z;
                if (dx * dx + dz * dz < d2) return true;
            }
            return false;
        }

        private static int HostileScore(PoiEntry e)
        {
            int s = e.Tier >= 3 && e.Tier <= 4 ? 2 : 0;
            if (HasHostileWord(e.Lower) || HasHostileWord(e.Display)) s += 2;
            if (e.Biome == "wasteland" || e.Biome == "burnt_forest") s += 1;
            return s;
        }

        /// <summary>Does a word of the text (a run of letters) start with a hostile word? "fort" finds "Fort Bragg" and "fortress", not "Comfort Inn".</summary>
        private static bool HasHostileWord(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            string s = text.ToLowerInvariant();
            int i = 0;
            while (i < s.Length)
            {
                while (i < s.Length && !char.IsLetter(s[i])) i++;
                int start = i;
                while (i < s.Length && char.IsLetter(s[i])) i++;
                foreach (string w in HostileWords)
                    if (i - start >= w.Length && string.CompareOrdinal(s, start, w, 0, w.Length) == 0) return true;
            }
            return false;
        }

        /// <summary>Runs and saves a seeding (new / late / top): the records, the stamp. Returns the run's log line.</summary>
        private static string Seed(World w, string mode)
        {
            int want = -1;
            if (mode == "top")
            {
                Vector3i min, max;
                float km2;
                if (!WorldExtent(w, out min, out max)) return "no world extent - nothing added";
                want = Math.Max(0, TargetCount(min, max, out km2) - All.Count);
            }
            Plan p = MakePlan(w, mode, want);
            if (p == null) return "no world extent - nothing seeded";
            Community c = ScRegistry.Community;
            int today = ScCommunity.Today;
            foreach (Pick k in p.Picks) NewRecord(k, mode, today);
            if (mode == "top") c.EnclaveSeedCount = p.Target;
            else
            {
                c.EnclaveSeeded = true;
                c.EnclaveSeedDay = today;
                c.EnclaveSeedMode = mode;
                c.EnclaveSeed = p.Seed;
                c.EnclaveSeedCount = p.Target;
            }
            return Summary(p);
        }

        /// <summary>A pick made a record (Seed; E6's Refill): the next id, the POI, its kind from the row; unknown, standing 0.</summary>
        private static Enclave NewRecord(Pick k, string mode, int today)
        {
            Community c = ScRegistry.Community;
            var en = new Enclave
            {
                Id = ++c.NextEnclaveId,
                Name = k.Name,
                Poi = k.E.Key,
                Size = k.E.Size,
                Prefab = k.E.Prefab,
                PoiName = k.E.Display,
                Biome = k.E.Biome,
                Row = k.E.Row,
                Kind = KindFor(k.E.Row),
                Tier = k.E.Tier,
                Hostile = k.Hostile,
                SeededDay = today,
                Mode = mode
            };
            All.Add(en);
            ScLog.Verbose("enclave " + en.Id + " '" + en.Name + "' at " + en.PoiName + " (" + en.Prefab + ", T" + en.Tier + ", " + en.Biome + ") " + en.Poi
                          + (k.Hostile ? ", hostile" : "") + (k.Ring ? ", start ring" : "") + " [" + mode + "]");
            return en;
        }

        /// <summary>
        /// E6 (Q4 a): one new group into another POI - one pick with the late seed's rules (MakePlan "refill": nothing loaded,
        /// nowhere near a claim, unvisited POIs preferred, spaced from the others, the hostile share kept), never the lost
        /// group's building or name, never above the target count. Named, unknown, its benefit set, the lookups rebuilt and
        /// its rooms kept clear; the seeding stamp is left alone. Null when none moved in: full (at the target, nothing owed)
        /// or not (no POI fits now - try again later), and why. The caller saves.
        /// </summary>
        public static Enclave Refill(World w, int seed, Vector3i exclude, string lostName, out bool full, out string why)
        {
            full = false;
            why = null;
            if (w == null || !ScCommunity.IsHost) { why = "no world"; return null; }
            if (!ScConfig.Enclaves) { why = "Enclaves is off"; return null; }
            if (!ScPoiIndex.EnsureBuilt()) { why = "no POI list on this world"; return null; }
            Vector3i min, max;
            float km2;
            if (!WorldExtent(w, out min, out max)) { why = "no world extent"; return null; }
            int target = TargetCount(min, max, out km2);
            if (All.Count >= target) { full = true; why = "at the target count (" + All.Count + " of " + target + ")"; return null; }
            Plan p = MakePlan(w, RefillMode, 1, seed, exclude, lostName);
            if (p == null || p.Picks.Count == 0)
            {
                why = "no POI fits (" + (p != null ? p.Candidates + " candidate(s) of " + p.Real : "no plan") + ")";
                return null;
            }
            Enclave en = NewRecord(p.Picks[0], RefillMode, ScCommunity.Today);
            ScEncAllies.EnsureBenefit(en, w);
            Rebuild();
            PreClear(PoiOf(en), w);
            why = p.Candidates + " candidate(s), D " + Mathf.RoundToInt(p.D) + " m" + (p.Picks[0].Hostile ? ", hostile (the share)" : "");
            return en;
        }

        /// <summary>"seeded 11 (3 hostile) for a new save on a 6144 m world, D 1019 m, nearest 714 m".</summary>
        private static string Summary(Plan p)
        {
            var sb = new StringBuilder();
            sb.Append(p.Mode == "top" ? "added " : "seeded ").Append(p.Picks.Count);
            if (p.Picks.Count < p.Want) sb.Append(" of ").Append(p.Want);
            sb.Append(" (").Append(p.Hostiles).Append(" hostile)");
            sb.Append(p.Mode == "new" ? " for a new save" : p.Mode == "late" ? " late into this save" : " to reach the raised target " + p.Target);
            sb.Append(" on a ").Append(Mathf.RoundToInt(p.Width)).Append(" m world, D ").Append(Mathf.RoundToInt(p.D0)).Append(" m");
            if (p.D < p.D0 - 0.5f) sb.Append(" (relaxed to ").Append(Mathf.RoundToInt(p.D)).Append(" m)");
            float nearest = float.MaxValue;
            foreach (Pick k in p.Picks) nearest = Math.Min(nearest, Dist(k.E.X, k.E.Z, p.Start.x, p.Start.z));
            if (p.Picks.Count > 0) sb.Append(", nearest ").Append(Mathf.RoundToInt(nearest)).Append(" m from ").Append(p.StartWhat);
            sb.Append(" (").Append(p.Candidates).Append(" candidate POIs of ").Append(p.Real).Append(')');
            return sb.ToString();
        }

        /// <summary>
        /// A load of a seeded save: every enclave's POI is found again by (x, z) and prefab (the key's y may have moved). One
        /// whose POI is gone moves to the nearest free POI within EnclaveRelocateRange (keeping its id, name, nature,
        /// standing and whether it is known). When the free ones are only loaded, near a claim or in use just now, it waits off
        /// the map for the next load; with none at all it is dropped. Then a raised target is topped up with the late-seed
        /// rules; a lower one never removes any.
        /// </summary>
        private static string Validate(World w)
        {
            int loaded = All.Count, moved = 0, dropped = 0, waiting = 0;
            Vector3i min, max;
            Rules r = WorldExtent(w, out min, out max) ? RulesFor(w, true, min, max) : null;
            foreach (Enclave e in new List<Enclave>(All))
            {
                PoiEntry pe = ScPoiIndex.Find(e.Poi.x, e.Poi.z, e.Prefab);
                if (pe != null)
                {
                    e.Lost = false;
                    if (pe.Key != e.Poi)
                    {
                        ScLog.Out("enclave " + e.Id + " '" + e.Name + "': its POI's key moved from " + e.Poi + " to " + pe.Key + " (a POI pack update?)");
                        ScEnclavePeople.ClearSpots(e);      // E2: the home spots were in the old placement
                    }
                    e.Poi = pe.Key;
                    e.Size = pe.Size;
                    e.PoiName = pe.Display;
                    continue;
                }
                // its POI is gone (a POI pack removed): off the map and out of the lookups until it has a building again
                e.Lost = true;
                if (r == null) { waiting++; continue; }     // no world extent to judge a new POI by: kept for a load that has one
                bool passing;
                PoiEntry to = Relocation(e, r, out passing);
                if (to == null)
                {
                    if (passing)
                    {
                        // the free POIs in reach are only loaded, near a claim or in use just now: a passing state must not
                        // delete the record, so it waits for the next load
                        waiting++;
                        ScLog.Out("enclave " + e.Id + " '" + e.Name + "': its POI " + e.Prefab + " at " + e.Poi + " is gone, and the free POIs within "
                                  + ScConfig.EnclaveRelocateRange + " m are loaded, near a claim or in use just now - kept, tried again on the next load");
                        continue;
                    }
                    ScEncRequests.Withdraw(e, "their building is gone");    // E4: their open request with them
                    RemoveMarker(e);
                    ScEnclavePeople.DropPeople(e);          // E2: its people's records go with it
                    All.Remove(e);
                    dropped++;
                    ScLog.Warn("enclave " + e.Id + " '" + e.Name + "': its POI " + e.Prefab + " at " + e.Poi + " is gone and no POI within "
                               + ScConfig.EnclaveRelocateRange + " m can take them - dropped");
                    continue;
                }
                ScLog.Out("enclave " + e.Id + " '" + e.Name + "': its POI " + e.Prefab + " at " + e.Poi + " is gone - moved to " + to.Display + " (" + to.Prefab + ") " + to.Key);
                r.Enclaves.Remove(e.Poi);
                r.Enclaves.Add(to.Key);
                TakeBuilding(e, to);
                moved++;
            }
            Rebuild();      // the moves count for the top-up (its spacing, and CheckForAnyPlayerHome)
            string added = "0 added";
            Community c = ScRegistry.Community;
            float km2;
            int target = r != null ? TargetCount(min, max, out km2) : 0;
            if (r != null && target > c.EnclaveSeedCount)
            {
                // tried once per raised target: a top-up that found no room does not run again on every load
                if (target > All.Count) added = Seed(w, "top");
                else c.EnclaveSeedCount = target;
            }
            return loaded + " loaded, " + moved + " moved, " + (waiting > 0 ? waiting + " waiting for a free POI, " : "") + dropped + " dropped, " + added;
        }

        /// <summary>
        /// The nearest POI a late seed would take within EnclaveRelocateRange of a vanished one, at least EnclaveSpacingFloor
        /// from every other enclave. When there is none, passing says whether a POI in range was refused only for now: in
        /// loaded chunks, near a claim, in use by a run, contact, favour, scouting mark or quest, or infested (I1).
        /// </summary>
        private static PoiEntry Relocation(Enclave e, Rules r, out bool passing)
        {
            passing = false;
            Vector3 c = e.Centre;
            var near = new List<PoiEntry>();
            ScPoiIndex.Near(c.x, c.z, ScConfig.EnclaveRelocateRange, near);
            float floor2 = ScConfig.EnclaveSpacingFloor * (float)ScConfig.EnclaveSpacingFloor;
            foreach (PoiEntry pe in near)
            {
                string why = Refuse(pe, r);
                if (why != null)
                {
                    if (why == "loaded" || why == "near a claim" || why == "in use" || why == "infested") passing = true;
                    continue;
                }
                bool close = false;
                foreach (Enclave o in All)
                {
                    if (o == e || o.Lost) continue;
                    Vector3 oc = o.Centre;
                    float dx = oc.x - pe.X, dz = oc.z - pe.Z;
                    if (dx * dx + dz * dz < floor2) { close = true; break; }
                }
                if (!close) return pe;
            }
            return null;
        }

        /// <summary>
        /// The group takes another building (Validate: its POI is gone; E11's ScEncMoves: of its own accord): the footprint,
        /// prefab, name, biome, row and tier - Mode "moved", on the map again - and its people's spots are chosen again in the
        /// new one when they next spawn (E2). Everything else stays: id, name, kind, nature, standing, stock, thanks, the dead.
        /// </summary>
        private static void TakeBuilding(Enclave e, PoiEntry to)
        {
            e.Poi = to.Key;
            e.Size = to.Size;
            e.Prefab = to.Prefab;
            e.PoiName = to.Display;
            e.Biome = to.Biome;
            e.Row = to.Row;
            e.Tier = to.Tier;
            e.Mode = "moved";
            e.Lost = false;
            ScEnclavePeople.ClearSpots(e);              // E2: new spots in the new POI when they next spawn (their kind stays)
        }

        // ------------------------------------------------------------------ moves of their own accord (E11) ----

        /// <summary>E11: what MoveTarget saw - the buildings in the move ring, the candidates left, why the rest were refused.</summary>
        internal sealed class MoveSearch
        {
            public int InRing, Candidates;
            public readonly Dictionary<string, int> Refused = new Dictionary<string, int>();

            public void Note(string why)
            {
                int n;
                Refused.TryGetValue(why, out n);
                Refused[why] = n + 1;
            }

            /// <summary>"loaded 2, near a claim 1, tier 7" (by reason, ordinal), or "none".</summary>
            public string RefusedText()
            {
                var l = new List<string>();
                foreach (var kv in Refused) l.Add(kv.Key + " " + kv.Value);
                l.Sort(StringComparer.Ordinal);
                return l.Count > 0 ? string.Join(", ", l.ToArray()) : "none";
            }
        }

        /// <summary>
        /// E11 (ScEncMoves): the building a group moving of its own accord goes to, or null. The late seed's filter (RulesFor
        /// late, Refuse: sleepers, tier, size, district, name, edge, traders, spawn points, no other enclave, not infested, not an
        /// outpost, not home, not in use, nobody's home, no claim within EnclaveClaimBuffer, nothing loaded or in the player's
        /// view); its centre EnclaveMoveMin..EnclaveMoveMax m from theirs; never the building they left last; at least
        /// EnclaveSpacingFloor m from every other enclave's centre (Relocation's rule); no infestation within
        /// EnclaveMoveInfestRange m of its footprint; no player within EnclaveSpawnRange + EnclaveDespawnMargin m of it.
        /// Weighted as the late seed weighs (the district; a building never
        /// generated in this save x EnclaveUnvisitedWeight), x2 for one their kind's rows make (EnclaveKindRows; a general group
        /// has none); one weighted draw (the key u^(1/w), as MakePlan's) over the candidates in (x, z) order from the seed.
        /// </summary>
        internal static PoiEntry MoveTarget(Enclave e, int seed, MoveSearch s)
        {
            World w = ScCommunity.World;
            Vector3i min, max;
            if (w == null || e == null || !ScPoiIndex.Built || !WorldExtent(w, out min, out max)) return null;
            Rules r = RulesFor(w, true, min, max);
            Vector3 c = e.Centre;
            var near = new List<PoiEntry>();
            ScPoiIndex.Near(c.x, c.z, ScConfig.EnclaveMoveMax, near);
            float floor2 = ScConfig.EnclaveSpacingFloor * (float)ScConfig.EnclaveSpacingFloor;
            float keep = ScConfig.EnclaveSpawnRange + ScConfig.EnclaveDespawnMargin;
            bool ownRows = e.Kind.Length > 0 && e.Kind != "general";
            var cands = new List<Pick>();
            foreach (PoiEntry pe in near)
            {
                if (Dist(pe.X, pe.Z, c.x, c.z) < ScConfig.EnclaveMoveMin) continue;
                s.InRing++;
                string why = Refuse(pe, r);
                if (why == null && e.MovedDay >= 0 && pe.Key == e.MovedFrom) why = "the building they left";
                if (why == null && NearOtherEnclave(e, pe, floor2)) why = "near another enclave";
                if (why == null && ScEncMoves.NestNear(pe.Key, pe.Size) != null) why = "near an infestation";
                if (why == null && PlayerWithin(w, pe.Key, pe.Size, keep)) why = "a player near";
                float weight = 0f;
                if (why == null)
                {
                    weight = KindWeight(pe);
                    if (pe.Pi.sleeperVolumes.Count == 0) weight *= ScConfig.EnclaveUnvisitedWeight;
                    if (ownRows && KindFor(pe.Row) == e.Kind) weight *= 2f;
                    if (weight <= 0f) why = "weight";
                }
                if (why != null) { s.Note(why); continue; }
                cands.Add(new Pick { E = pe, Weight = weight });
            }
            s.Candidates = cands.Count;
            if (cands.Count == 0) return null;
            cands.Sort((a, b) =>
            {
                int k = a.E.Key.x.CompareTo(b.E.Key.x);
                if (k != 0) return k;
                k = a.E.Key.z.CompareTo(b.E.Key.z);
                return k != 0 ? k : string.CompareOrdinal(a.E.Prefab, b.E.Prefab);
            });
            GameRandom rng = GameRandomManager.Instance.CreateGameRandom(seed);
            try
            {
                Pick best = null;
                double bestKey = -1.0;
                foreach (Pick k in cands)
                {
                    double key = Math.Pow(rng.RandomDouble, 1.0 / k.Weight);
                    if (key > bestKey) { bestKey = key; best = k; }
                }
                return best != null ? best.E : null;
            }
            finally
            {
                GameRandomManager.Instance.FreeGameRandom(rng);
            }
        }

        /// <summary>Another living enclave's centre within EnclaveSpacingFloor m of the POI's (Relocation's rule; the group itself left out).</summary>
        private static bool NearOtherEnclave(Enclave self, PoiEntry pe, float floor2)
        {
            foreach (Enclave o in All)
            {
                if (o == self || o.Lost) continue;
                Vector3 oc = o.Centre;
                float dx = oc.x - pe.X, dz = oc.z - pe.Z;
                if (dx * dx + dz * dz < floor2) return true;
            }
            return false;
        }

        /// <summary>Any player within range m (XZ) of the footprint (every player: NearestPlayer's rule).</summary>
        internal static bool PlayerWithin(World w, Vector3i key, Vector3i size, float range)
        {
            List<EntityPlayer> list = w != null && w.Players != null ? w.Players.list : null;
            if (list == null) return false;
            int[] b = { key.x, key.x + Math.Max(1, size.x) - 1, key.z, key.z + Math.Max(1, size.z) - 1 };
            for (int i = 0; i < list.Count; i++)
            {
                EntityPlayer p = list[i];
                if (p != null && ToBox(p.position.x, p.position.z, b) <= range) return true;
            }
            return false;
        }

        /// <summary>
        /// E11 (ScEncMoves): the group moves to another building now - Validate's move (TakeBuilding) of a group whose POI is
        /// still there: the old building's marker off, the new one's footprint, prefab, name, biome, row and tier taken (Mode
        /// "moved"), the lookups rebuilt (the old building's loot, claims, quests, infestations and wild spawns allowed again; the
        /// new one protected), the rooms they kept clear at the old one given back their respawn (Release: after the loot
        /// respawn wait, as for a group that fell) and the new one's generated rooms kept clear (PreClear). told: known, the
        /// marker at the new building; otherwise unknown and off the map until found again. Returns the sleeper areas released;
        /// cleared: those kept clear at the new building. The caller saves.
        /// </summary>
        internal static int MoveTo(Enclave e, PoiEntry to, bool told, EntityPlayer owner, out int cleared)
        {
            World w = ScCommunity.World;
            PrefabInstance old = PoiOf(e);
            RemoveMarker(e);
            TakeBuilding(e, to);
            Rebuild();
            int released = Release(old, w);
            cleared = PreClear(to.Pi, w);
            if (told)
            {
                if (!e.Known) { e.Known = true; e.KnownDay = ScCommunity.Today; }
                if (owner != null) AddMarker(e, owner);
                else markersRestored = false;       // the next tick puts it on
            }
            else
            {
                e.Known = false;
                e.KnownDay = -1;
            }
            return released;
        }

        // ------------------------------------------------------------------ sleeper areas ----

        /// <summary>
        /// One sleeper area of an enclave POI kept clear for good (infestations.md section 5): its living or pending sleepers
        /// despawned and reset first, then cleared with no respawn (both saved with the world). Awake zombies already in it
        /// are not tracked any more and stay to be fought. Main thread.
        /// </summary>
        public static void KeepCleared(SleeperVolume v, World w)
        {
            if (v == null) return;
            if (w == null) w = ScCommunity.World;
            if (w != null && (v.respawnMap.Count > 0 || v.isSpawning)) v.DespawnAndReset(w);
            if (!v.wasCleared || v.respawnTime != ulong.MaxValue) ScLog.Verbose("enclave POI sleeper area at " + v.BoxMin + " kept clear");
            v.wasCleared = true;
            v.respawnTime = ulong.MaxValue;
        }

        /// <summary>Every generated sleeper area of the POI kept clear (a copy: the chunk thread may be adding one). Returns how many.</summary>
        public static int PreClear(PrefabInstance pi, World w)
        {
            if (pi == null || w == null) return 0;
            SleeperVolume[] vols;
            try { vols = pi.sleeperVolumes.ToArray(); }
            catch (Exception) { return 0; }
            int n = 0;
            foreach (SleeperVolume v in vols)
            {
                if (v == null) continue;
                KeepCleared(v, w);
                n++;
            }
            return n;
        }

        private static int PreClearAll(World w)
        {
            int n = 0;
            foreach (Enclave e in All) if (!e.Lost) n += PreClear(PoiOf(e), w);
            return n;
        }

        /// <summary>
        /// A POI given back (sc enclave reseed): the sleeper areas the enclave kept clear respawn after vanilla's own wait,
        /// as if they had been cleared now (LootRespawnDays; never when that is off).
        /// </summary>
        private static int Release(PrefabInstance pi, World w)
        {
            if (pi == null || w == null) return 0;
            SleeperVolume[] vols;
            try { vols = pi.sleeperVolumes.ToArray(); }
            catch (Exception) { return 0; }
            int days = GamePrefs.GetInt(EnumGamePrefs.LootRespawnDays);
            ulong at = days > 0 ? w.worldTime + (ulong)days * 24000UL : uint.MaxValue;
            int n = 0;
            foreach (SleeperVolume v in vols)
            {
                if (v == null || !v.wasCleared || v.respawnTime != ulong.MaxValue) continue;
                v.respawnTime = at;
                n++;
            }
            return n;
        }

        // ------------------------------------------------------------------ discovery, the radio, markers ----

        /// <summary>ScTicker, every second: markers back after a load, and an unknown enclave within EnclaveDiscoverRange of its POI is found.</summary>
        public static void Tick(World w, EntityPlayerLocal player)
        {
            try
            {
                if (!ready || w == null || player == null || !ScConfig.Enclaves || All.Count == 0) return;
                if (!markersRestored)
                {
                    markersRestored = true;
                    RefreshMarkers(player);
                }
                Vector3 at = player.position;
                foreach (Enclave e in All)
                {
                    if (e.Known || e.Lost || e.DistanceXZ(at.x, at.z) > ScConfig.EnclaveDiscoverRange) continue;
                    e.Known = true;
                    e.KnownDay = ScCommunity.Today;
                    AddMarker(e, player);
                    ScCommunity.Tell(player, string.Format(Localization.Get("scEnclaveFound"), e.Name, e.PoiName, StandingWord(e)), e.IsHostile ? "ui_denied" : "");
                    ScOutposts.NoteLedger(string.Format(Localization.Get("scEnclaveLedgerFound"), e.Name, e.PoiName));
                    ScLog.Out("enclave " + e.Id + " '" + e.Name + "' found at " + e.PoiName + (e.IsHostile ? " (hostile)" : ""));
                    ScRegistry.SaveAtomic();
                }
            }
            catch (Exception e)
            {
                ScLog.Once("enclave-tick", e);
            }
        }

        /// <summary>
        /// ScDaily.Run: once the day reaches the next radio reveal, a powered radio picks up the nearest unknown enclave within
        /// EnclaveRadioRange of home (of the player without one), and the next reveal is EnclaveRadioRevealDays later. None in
        /// range: the radio tries again the next day. A catch-up of missed days reveals one (radioFrame). ScDaily saves.
        /// </summary>
        public static void Daily(int day, EntityPlayerLocal player)
        {
            try
            {
                if (!ready || !ScConfig.Enclaves || All.Count == 0) return;
                Community c = ScRegistry.Community;
                if (day < c.EnclaveRadioDay || Time.frameCount == radioFrame || !ScRadio.Online()) return;
                Vector3 from;
                if (ScZone.HasHome) { Vector3i h = ScZone.Home; from = new Vector3(h.x, h.y, h.z); }
                else if (player != null) from = player.position;
                else return;
                Enclave best = null;
                float bestD = float.MaxValue;
                foreach (Enclave e in All)
                {
                    if (e.Known || e.Lost) continue;
                    Vector3 ec = e.Centre;
                    float d = Dist(ec.x, ec.z, from.x, from.z);
                    if (d <= ScConfig.EnclaveRadioRange && d < bestD) { bestD = d; best = e; }
                }
                if (best == null) return;
                radioFrame = Time.frameCount;
                c.EnclaveRadioDay = day + Math.Max(1, ScConfig.EnclaveRadioRevealDays);
                best.Known = true;
                best.KnownDay = day;
                if (player != null) AddMarker(best, player);
                string where = Where(from, best.Centre);
                if (player != null) ScCommunity.Tell(player, string.Format(Localization.Get("scEnclaveRadio"), best.Name, best.PoiName, where), "");
                ScOutposts.NoteLedger(string.Format(Localization.Get("scEnclaveLedgerRadio"), best.Name, best.PoiName));
                ScLog.Out("enclave " + best.Id + " '" + best.Name + "' heard on the radio: " + best.PoiName + ", " + where + " from " + (ScZone.HasHome ? "home" : "you")
                          + "; the next reveal from day " + c.EnclaveRadioDay);
            }
            catch (Exception e)
            {
                ScLog.Once("enclave-daily", e);
            }
        }

        /// <summary>
        /// The world's day went back from fromDay to day (ScTicker, on a real jump only: an older save, a time command - never
        /// "sc day"'s own end; the E4 re-check's R2, E6). The one hook for every forward-only enclave stamp, so none of them
        /// freezes until the world catches up: each keeps its remaining span, shifted back by its own clock's drop and never
        /// below 0. The request clock's stamps (E4's open requests and cooldowns, E6's refills and ally reveals) by the request
        /// clock's drop - from where it stood (the world's last day at the least) to the world's day; the world clock's (E1's
        /// next radio reveal, E3's recruit cooldowns and enlistments, E5's scheduled siege call, I1's infestation ages) by the
        /// world's. E7's next gift and next ally barter days are the request clock's. E9's next radio call is the request
        /// clock's, a call's arrival and its fighter's end the world's (as E3's enlistments). E10's raid rest is the request
        /// clock's. E11's move stamps (the day they last moved, their last death, an infestation next door since) are the
        /// request clock's. E12's arrivals (when a member on the way may turn up at home) are the world's. One log line; the
        /// ticker saves.
        /// </summary>
        public static void OnWorldDayBack(int day, int fromDay)
        {
            try
            {
                Community c = ScRegistry.Community;
                int worldBack = Math.Max(0, fromDay - day);
                int reqBack = Math.Max(0, Math.Max(fromDay, c.EncRequestDay) - day);
                if (worldBack == 0 && reqBack == 0) return;
                int requests = ScEncRequests.ShiftDays(day, reqBack);      // E4: the request clock, open requests, cooldowns
                int allies = ScEncAllies.ShiftDays(reqBack);               // E6: queued refills, ally reveals
                int gifts = ScEncGifts.ShiftDays(reqBack);                 // E7: the next gift and the next ally barter
                int stamps = 0, infest = 0;
                if (worldBack > 0)
                {
                    ulong ticks = (ulong)worldBack * 24000UL;
                    if (c.EnclaveRadioDay > 0) { c.EnclaveRadioDay = Math.Max(0, c.EnclaveRadioDay - worldBack); stamps++; }          // E1
                    foreach (Enclave e in All)
                        if (e.LastRecruitDay > 0) { e.LastRecruitDay = Math.Max(0, e.LastRecruitDay - worldBack); stamps++; }         // E3
                    foreach (EncPerson p in ScRegistry.EncPeople)
                        if (p.EnlistedUntil > 0UL) { p.EnlistedUntil = p.EnlistedUntil > ticks ? p.EnlistedUntil - ticks : 1UL; stamps++; }   // E3
                    if (c.EncSiegeAt > 0UL) { c.EncSiegeAt = c.EncSiegeAt > ticks ? c.EncSiegeAt - ticks : 0UL; stamps++; }             // E5
                    infest = ScInfestations.ShiftDays(worldBack);                                                                      // I1
                }
                int calls = ScEncCalls.ShiftDays(reqBack, worldBack);     // E9: the next radio call (request clock); a call's arrival and its fighter's end (world)
                int raids = ScEncRaids.ShiftDays(reqBack);                 // E10: a raiding group's rest (request clock)
                int moves = ScEncMoves.ShiftDays(reqBack);                 // E11: the last move, the last death, a nest next door since (request clock)
                int arrivals = ScEncJoin.ShiftDays(worldBack);             // E12: members on the way, when they may turn up at home (world clock)
                ScLog.Out("enclaves: the world's day went back from " + fromDay + " to " + day + " - the request clock, " + requests + " open request(s), "
                          + allies + " refill/ally-reveal stamp(s), " + gifts + " gift/barter stamp(s) moved back " + reqBack + " day(s); " + stamps
                          + " world-clock stamp(s) (radio reveal, recruit cooldowns, "
                          + "enlistments, the siege call) and " + infest + " infestation(s) moved back " + worldBack + " day(s); " + calls
                          + " radio-call stamp(s) moved back (the next call " + reqBack + " day(s), an arrival or a fighter's end " + worldBack + " day(s)); "
                          + raids + " raid rest stamp(s) moved back " + reqBack + " day(s); " + moves + " move stamp(s) moved back " + reqBack + " day(s); "
                          + arrivals + " arrival stamp(s) moved back " + worldBack + " day(s)");
            }
            catch (Exception ex)
            {
                ScLog.Once("enclave-dayback", ex);
            }
        }

        public static string NavClassFor(Enclave e) { return NavPrefix + StandingKey(e); }

        /// <summary>The marker's spot: the POI's centre on the ground (the static heightmap works with the chunk unloaded). E11: a moved group's favours point there.</summary>
        internal static Vector3 MarkerSpot(Enclave e)
        {
            Vector3 c = e.Centre;
            World w = ScCommunity.World;
            float y = w != null ? w.GetHeightAt(c.x, c.z) : 0f;
            if (y > 0f) c.y = y;
            return c;
        }

        private static void AddMarker(Enclave e, EntityPlayer owner)
        {
            try
            {
                if (!e.Known || e.Lost) return;
                NavObjectManager nav = NavObjectManager.Instance;
                string cls = NavClassFor(e);
                if (nav == null || owner == null || NavObjectClass.GetNavObjectClass(cls) == null) return;
                RemoveMarker(e);
                Vector3 at = MarkerSpot(e);
                foreach (string k in NavKinds) nav.UnRegisterNavObjectByPosition(at, NavPrefix + k);   // whatever class it had before
                NavObject obj = nav.RegisterNavObject(cls, at, "", false, -1, owner);
                if (obj != null) obj.name = string.Format(Localization.Get("scEnclaveMarker"), e.Name, StandingWord(e));
                markers[e.Id] = at;
            }
            catch (Exception ex)
            {
                ScLog.Once("enclave-marker", ex);
            }
        }

        private static void RemoveMarker(Enclave e)
        {
            try
            {
                Vector3 at;
                if (!markers.TryGetValue(e.Id, out at)) return;
                markers.Remove(e.Id);
                NavObjectManager nav = NavObjectManager.Instance;
                if (nav == null) return;
                // the standing may have changed since: drop every enclave class at that spot
                foreach (string k in NavKinds) nav.UnRegisterNavObjectByPosition(at, NavPrefix + k);
            }
            catch (Exception ex)
            {
                ScLog.Once("enclave-unmarker", ex);
            }
        }

        /// <summary>Re-registers the marker of every known enclave (and drops the rest).</summary>
        public static void RefreshMarkers(EntityPlayer owner)
        {
            if (owner == null) return;
            foreach (Enclave e in All)
            {
                if (e.Known && !e.Lost && ScConfig.Enclaves) AddMarker(e, owner);
                else RemoveMarker(e);
            }
        }

        // ------------------------------------------------------------------ standing, hostility, a group gone (E2) ----

        /// <summary>
        /// Standing changes (E2; E4's requests use it): clamped to EnclaveStandingMin..100, the marker re-registered when
        /// the band changes (Neutral / Friendly / Allied - the dialog's greetings follow on the next talk), a ledger line, the
        /// log, a save. A hostile enclave's standing still moves, but it shows and acts as hostile whatever the points.
        /// ledger=false: the caller writes its own ledger line with the change in it (E4's ScEncRequests.Close).
        /// </summary>
        public static void AddStanding(Enclave e, int delta, string reason, bool ledger = true)
        {
            if (e == null || delta == 0) return;
            int old = e.Standing;
            string before = StandingKey(e);
            e.Standing = Mathf.Clamp(e.Standing + delta, Math.Min(0, ScConfig.EnclaveStandingMin), 100);
            if (e.Standing == old) return;
            string after = StandingKey(e);
            EntityPlayerLocal pl = ScCommunity.LocalPlayer;
            if (after != before && e.Known && pl != null) AddMarker(e, pl);
            if (ledger) ScOutposts.NoteLedger(string.Format(Localization.Get("scEncLedgerStanding"), e.Name, (e.Standing > old ? "+" : "") + (e.Standing - old), StandingWord(e)));
            ScLog.Out("enclave " + e.Id + " '" + e.Name + "': standing " + old + " -> " + e.Standing + " (" + after + ", " + reason + ")");
            if (after != before) ScEncAllies.OnBandChanged(e, before, after);      // E6: the benefit on or off, an ally's first word of another
            ScRegistry.SaveAtomic();
        }

        /// <summary>
        /// The group turns hostile for good (E2, Q6 a: a second hit within the warning window, a kill, a threat): TurnedHostile
        /// and HostileDay, live bodies stamped (X9 hides talk, X10 turrets shoot), the faction lookup rebuilt - after grace
        /// seconds, so a threat's dialog can close first - the marker red, a ledger line and a tell, the log, a save. It never
        /// improves: nothing turns it back except "sc enclave reseed". A raider enclave is hostile already.
        /// </summary>
        public static void TurnHostile(Enclave e, string cause, float grace = 0f)
        {
            if (e == null || e.IsHostile) return;
            string before = StandingKey(e);
            // E3: one of theirs enlisted with the player goes home first - released before the flip, so they never turn on
            // the player at their side
            ScEnclaveDeals.ReleaseEnlisted(e, "their people turned hostile");
            ScEncRequests.Withdraw(e, "they turned hostile");    // E4: their open request closes, no standing change
            e.TurnedHostile = true;
            e.HostileDay = ScCommunity.Today;
            ScEnclavePeople.OnTurnedHostile(e, grace);
            EntityPlayerLocal pl = ScCommunity.LocalPlayer;
            if (e.Known && pl != null) AddMarker(e, pl);
            ScOutposts.NoteLedger(string.Format(Localization.Get("scEncLedgerHostile"), e.Name));
            if (pl != null) ScCommunity.Tell(pl, string.Format(Localization.Get("scEncTurnedHostile"), e.Name), "ui_denied");
            ScLog.Out("enclave " + e.Id + " '" + e.Name + "' turned hostile for good (" + cause + ")" + (grace > 0f ? ", " + grace.ToString("0.#", CultureInfo.InvariantCulture) + " s grace" : ""));
            ScEncAllies.OnBandChanged(e, before, StandingKey(e));      // E6: an ally's benefit stops
            ScRegistry.SaveAtomic();
        }

        /// <summary>
        /// A group is gone (E2: its last person died; E5: a lost siege): its bodies and records go, the marker comes off and
        /// its POI is given back - the sleeper areas it kept clear respawn after the loot respawn wait, and claims, quests,
        /// infestations, wild spawns and containers are allowed again once the lookups rebuild. A ledger line and a tell
        /// ("is gone", for raiders "you wiped out"), the log, a save; then the Fallen seam (E6's refill). E5 (Q13 a): a siege
        /// still open there is lost with them (ScEncSiege.OnFalling), and their building is infested once its record is out of
        /// the lookups (ScEncSiege.AfterFall).
        /// </summary>
        public static void Fall(Enclave e, string cause)
        {
            if (e == null || !All.Contains(e)) return;
            World w = ScCommunity.World;
            bool siege = ScEncSiege.OnFalling(e, cause);                          // E5: before the request is withdrawn
            ScEncRequests.Withdraw(e, "the group is gone (" + cause + ")");     // E4: before the record goes
            ScEnclavePeople.DropPeople(e);
            RemoveMarker(e);
            int released = Release(PoiOf(e), w);
            All.Remove(e);
            Rebuild();
            EntityPlayerLocal pl = ScCommunity.LocalPlayer;
            if (pl != null) ScCommunity.Tell(pl, string.Format(Localization.Get(e.Hostile ? "scEncWipedOut" : "scEncGone"), e.Name), "");
            ScOutposts.NoteLedger(string.Format(Localization.Get(e.Hostile ? "scEncLedgerWipedOut" : "scEncLedgerGone"), e.Name, e.PoiName));
            ScLog.Out("enclave " + e.Id + " '" + e.Name + "' at " + e.PoiName + " is gone (" + cause + ")"
                      + (released > 0 ? " - " + released + " sleeper area(s) respawn after the loot respawn wait" : ""));
            if (siege) ScEncSiege.AfterFall(e);                                 // E5: the dead have their building now
            ScRegistry.SaveAtomic();
            Action<Enclave, string> fallen = Fallen;
            if (fallen != null)
            {
                try { fallen(e, cause); }
                catch (Exception ex) { ScLog.Once("enclave-fallen", ex); }
            }
        }

        /// <summary>
        /// E12: a group that joined the player's community whole (ScEncJoin; its people have left the records already) ends as
        /// Fall ends a group, without Fall's tell and ledger line (the join has its own) and without a siege (none may be open):
        /// an open request withdrawn, the hold, lookups and favours dropped (DropPeople), the marker off, the building given back
        /// - its sleeper areas respawn after the loot respawn wait, and claims, outposts and infestations are allowed there
        /// again once the lookups rebuild - the log, a save, then the Fallen seam with the cause (CauseJoined: E6 owes a refill,
        /// with no Enclave-lost morale). Returns the sleeper areas given back.
        /// </summary>
        public static int Disband(Enclave e, string cause)
        {
            if (e == null || !All.Contains(e)) return 0;
            World w = ScCommunity.World;
            ScEncRequests.Withdraw(e, "the group is gone (" + cause + ")");
            ScEnclavePeople.DropPeople(e);
            RemoveMarker(e);
            int released = Release(PoiOf(e), w);
            All.Remove(e);
            Rebuild();
            ScLog.Out("enclave " + e.Id + " '" + e.Name + "' at " + e.PoiName + " is gone (" + cause + ")"
                      + (released > 0 ? " - " + released + " sleeper area(s) respawn after the loot respawn wait" : ""));
            ScRegistry.SaveAtomic();
            Action<Enclave, string> fallen = Fallen;
            if (fallen != null)
            {
                try { fallen(e, cause); }
                catch (Exception ex) { ScLog.Once("enclave-fallen", ex); }
            }
            return released;
        }

        // ------------------------------------------------------------------ console ----

        private static string Line(Enclave e, Vector3? from)
        {
            return "#" + e.Id + " " + e.Name + (e.Hostile ? " [hostile]" : e.TurnedHostile ? " [turned hostile]" : "") + (e.Known ? " - known" : " - not found") + " - " + e.PoiName + " (" + e.Prefab + ") T" + e.Tier
                   + " " + e.Biome + (from.HasValue ? " - " + Where(from.Value, e.Centre) : "") + " - " + e.Mode
                   + (e.Lost ? " - its POI is gone, waiting for a free one" : "");
        }

        /// <summary>sc enclave [list]</summary>
        public static void ListLines(List<string> into)
        {
            Community c = ScRegistry.Community;
            EntityPlayerLocal pl = ScCommunity.LocalPlayer;
            into.Add(All.Count + " enclave(s)" + (c.EnclaveSeeded ? ", seeded day " + c.EnclaveSeedDay + " (" + c.EnclaveSeedMode + ", seed " + c.EnclaveSeed + ", target "
                     + c.EnclaveSeedCount + ")" : ", never seeded in this save") + (ScConfig.Enclaves ? "" : " - Enclaves is OFF in the config")
                     + "; the radio can reveal one " + (c.EnclaveRadioDay > ScCommunity.Today ? "from day " + c.EnclaveRadioDay : "now (a powered radio, an unfound one in range)")
                     + "; " + ScRegistry.EncPeople.Count + " person record(s); wild NPC spawns kept off their grounds this session: " + ScEnclavePeople.WildRefused);
            into.Add("  " + ScEncAllies.SummaryLine());     // E6: the allies, their benefits, the refills owed
            foreach (Enclave e in All) into.Add("  " + Line(e, pl != null ? pl.position : (Vector3?)null));
        }

        /// <summary>sc enclave info &lt;id&gt;</summary>
        public static void InfoLines(Enclave e, List<string> into)
        {
            EntityPlayerLocal pl = ScCommunity.LocalPlayer;
            int[] b = e.Box;
            Vector3 cen = e.Centre;
            into.Add("#" + e.Id + " " + e.Name + ": " + (e.Hostile ? "hostile by nature" : e.TurnedHostile ? "survivors, hostile since day " + e.HostileDay : "survivors")
                     + ", kind " + (e.Kind.Length > 0 ? e.Kind : "-") + ", standing " + e.Standing + " (" + StandingWord(e) + "), "
                     + (e.Known ? "known since day " + e.KnownDay : "not found yet") + ", seeded day " + e.SeededDay + " (" + e.Mode + ")");
            into.Add("  people: " + ScEnclavePeople.Summary(e) + " (sc enclave people " + e.Id + ")");
            into.Add("  " + ScEncAllies.InfoLine(e));       // E6: the benefit, whether it applies, the next word of another
            into.Add("  " + ScEncGifts.InfoLine(e));        // E7: the next gift, the ally barter
            into.Add("  " + ScEncCalls.InfoLine(e));        // E9: the radio calls, the call that is on
            into.Add("  " + ScEncRaids.InfoLine(e));        // E10: their raids on your outposts, the one that is on
            into.Add("  " + ScEncMoves.InfoLine(e));        // E11: why they might move, what holds them, where they came from
            into.Add("  " + ScEncJoin.InfoLine(e));         // E12: whether the whole group would join you, and why not
            into.Add("  POI " + e.PoiName + " (" + e.Prefab + "), T" + e.Tier + ", row " + (e.Row.Length > 0 ? e.Row : "-") + ", biome " + e.Biome
                     + ", key " + e.Poi + ", footprint x " + b[0] + ".." + b[1] + " z " + b[2] + ".." + b[3] + " (" + e.Size.x + "x" + e.Size.z + ")"
                     + ", centre " + Mathf.RoundToInt(cen.x) + "," + Mathf.RoundToInt(cen.z) + (pl != null ? ", " + Where(pl.position, cen) + " from you" : ""));
            PrefabInstance pi = PoiOf(e);
            if (e.Lost || pi == null) { into.Add("  its POI is not in this world: off the map until a load finds it a free one, or drops it"); return; }
            int defined = pi.prefab != null && pi.prefab.SleeperVolumeList != null ? pi.prefab.SleeperVolumeList.Count : 0;
            int made = 0, cleared = 0, alive = 0, never = 0;
            SleeperVolume[] vols;
            try { vols = pi.sleeperVolumes.ToArray(); } catch (Exception) { vols = new SleeperVolume[0]; }
            foreach (SleeperVolume v in vols)
            {
                if (v == null) continue;
                made++;
                if (v.wasCleared) cleared++;
                alive += v.respawnMap.Count;
                if (v.respawnTime == ulong.MaxValue) never++;
            }
            into.Add("  sleeper areas: " + defined + " in the prefab, " + made + " generated, " + cleared + " cleared, " + never + " never respawn, "
                     + alive + " sleeper(s) still tracked" + (made < defined ? " (the rest are kept clear when their chunks first load)" : ""));
        }

        /// <summary>sc enclave reveal|hide &lt;id|all&gt;: known on / off, with the marker. Returns how many changed.</summary>
        public static int SetKnown(Enclave only, bool known, EntityPlayer owner)
        {
            int n = 0;
            foreach (Enclave e in All)
            {
                if (only != null && e != only) continue;
                if (e.Known == known) continue;
                e.Known = known;
                e.KnownDay = known ? ScCommunity.Today : -1;
                if (known) AddMarker(e, owner); else RemoveMarker(e);
                n++;
            }
            if (n > 0) ScRegistry.SaveAtomic();
            return n;
        }

        /// <summary>
        /// sc enclave sim [late]: plans a fresh seeding of this world (new-save rules from the player, or the late rules from
        /// home) as if it had no enclaves yet, and prints it. Nothing is saved or changed.
        /// </summary>
        public static void Simulate(bool late, List<string> into)
        {
            World w = ScCommunity.World;
            if (w == null || !ScCommunity.IsHost) { into.Add("host only, with a world"); return; }
            if (!ScPoiIndex.EnsureBuilt()) { into.Add("no POI list on this world"); return; }
            Plan p;
            ignoreExisting = true;
            try { p = MakePlan(w, late ? "late" : "new", -1); }
            finally { ignoreExisting = false; }
            if (p == null) { into.Add("no world extent"); return; }
            into.Add("sim (" + (late ? "late-seed" : "new-save") + " rules, from " + p.StartWhat + " at " + Mathf.RoundToInt(p.Start.x) + "," + Mathf.RoundToInt(p.Start.z)
                     + ", seed " + p.Seed + "): " + Mathf.RoundToInt(p.Width) + " m world (" + p.Km2.ToString("0.#", CultureInfo.InvariantCulture) + " km2), target "
                     + p.Target + ", D " + Mathf.RoundToInt(p.D0) + " m" + (p.D < p.D0 - 0.5f ? " relaxed to " + Mathf.RoundToInt(p.D) + " m" : ""));
            var refused = new List<string>();
            foreach (var kv in p.Refused) refused.Add(kv.Key + " " + kv.Value);
            refused.Sort(StringComparer.Ordinal);
            into.Add("  " + p.Candidates + " candidate(s) of " + p.Real + " real POI(s); refused: " + (refused.Count > 0 ? string.Join(", ", refused.ToArray()) : "none"));
            var dists = new List<int>();
            var biomes = new Dictionary<string, int>();
            foreach (Pick k in p.Picks)
            {
                dists.Add(Mathf.RoundToInt(Dist(k.E.X, k.E.Z, p.Start.x, p.Start.z)));
                int n;
                biomes.TryGetValue(k.E.Biome, out n);
                biomes[k.E.Biome] = n + 1;
            }
            dists.Sort();
            var bl = new List<string>();
            foreach (var kv in biomes) bl.Add((kv.Key.Length > 0 ? kv.Key : "?") + " " + kv.Value);
            bl.Sort(StringComparer.Ordinal);
            into.Add("  placed " + p.Picks.Count + (p.Picks.Count < p.Want ? " of " + p.Want : "") + " (" + p.Rings + " start ring, " + p.Hostiles + " hostile of "
                     + p.HostileWanted + " wanted); nearest from the start " + string.Join(", ", dists.GetRange(0, Math.Min(4, dists.Count)).ConvertAll(x => x + " m").ToArray())
                     + "; biomes " + string.Join(", ", bl.ToArray()));
            int i = 0;
            foreach (Pick k in p.Picks)
                into.Add("  " + (++i) + ". " + k.Name + " - " + k.E.Display + " (" + k.E.Prefab + ") T" + k.E.Tier + " " + k.E.Biome + " " + k.E.Kind
                         + " - " + Where(p.Start, new Vector3(k.E.X, 0f, k.E.Z)) + (k.Ring ? " [ring]" : "") + (k.Hostile ? " [hostile]" : ""));
        }

        /// <summary>sc enclave names [n] [hostile]: sample names over random biomes and POI kinds (unique among themselves).</summary>
        public static void SampleNames(int n, bool hostile, List<string> into)
        {
            var kinds = new List<string>(ScEnclaveNames.PoiKinds) { "", "" };
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            GameRandom rng = GameRandomManager.Instance.CreateGameRandom(Environment.TickCount);
            try
            {
                for (int i = 0; i < n; i++)
                {
                    string biome = ScEnclaveNames.BiomeKeys[rng.RandomRange(ScEnclaveNames.BiomeKeys.Length)];
                    string kind = hostile ? "" : kinds[rng.RandomRange(kinds.Count)];
                    into.Add("  " + ScEnclaveNames.Unique(rng, taken, hostile, biome, kind) + "  (" + biome + (kind.Length > 0 ? ", " + kind : "") + ")");
                }
            }
            finally
            {
                GameRandomManager.Instance.FreeGameRandom(rng);
            }
        }

        /// <summary>
        /// sc enclave reseed confirm (testing): every enclave dropped - markers off, their POIs given back - and the save seeded
        /// again with the late-seed rules. Returns the outcome.
        /// </summary>
        public static string Reseed(EntityPlayer owner)
        {
            World w = ScCommunity.World;
            if (w == null || !ScCommunity.IsHost) return "host only, with a world";
            if (!ScConfig.Enclaves) return "Enclaves is off in the config";
            if (!ScPoiIndex.EnsureBuilt()) return "no POI list on this world";
            int old = All.Count, released = 0;
            foreach (Enclave e in All)
            {
                ScEncRequests.Withdraw(e, "the enclaves were reseeded");    // E4: their requests (and journal quests) close first
                ScEnclavePeople.DropPeople(e);       // E2: bodies out of the world, records gone (a hostile one too: reseed is the only way back)
                RemoveMarker(e);
                released += Release(PoiOf(e), w);
            }
            All.Clear();
            Community c = ScRegistry.Community;
            c.EnclaveSeeded = false;
            c.EnclaveSeedCount = 0;
            int refills = c.EncRefills.Count;
            c.EncRefills.Clear();       // E6: a fresh seeding owes no refills
            Rebuild();      // the old footprints must not count as homes for the new run
            string line = Seed(w, "late");
            ScEncAllies.EnsureBenefits(w);     // E6
            if (refills > 0) line += "; " + refills + " queued refill(s) dropped";
            int cleared = PreClearAll(w);
            Rebuild();
            RefreshMarkers(owner);
            ScRegistry.SaveAtomic();
            ScLog.Out("enclaves: reseeded from the console - " + old + " dropped (" + released + " sleeper area(s) respawn again), " + line);
            return old + " dropped (" + released + " sleeper area(s) given back their respawn), " + line + "; " + cleared + " sleeper area(s) kept clear";
        }
    }
}
