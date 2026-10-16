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
        /// <summary>A raider enclave by nature (seeded hostile). Hostility earned in play comes with the people (E2).</summary>
        public bool Hostile;
        /// <summary>Standing points: Neutral below 30, Friendly 30-69, Allied 70 and up. 0 until requests exist (E4).</summary>
        public int Standing;
        /// <summary>Discovered: its marker shows. KnownDay = the day it was found (-1 = not yet).</summary>
        public bool Known;
        public int KnownDay = -1;
        public int SeededDay;
        /// <summary>How it came: "new" (a new save's first start), "late" (an existing save), "top" (a raised target), "moved" (its POI went).</summary>
        public string Mode = "new";
        /// <summary>
        /// This load found neither its POI nor a free one to move to, though one may come free (Validate): kept and tried
        /// again on the next load, off the map and out of the lookups meanwhile. Not saved.
        /// </summary>
        public bool Lost;

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
    /// no outposts (ScOutposts.CanEstablish), and SC's own POI pickers pass them by. There are no people yet.
    ///
    /// Seeding runs on the host at GameStartDone, when the whole POI list is there and only the chunks round the player
    /// have started loading (StartAsServer adds the player's chunk observer just before): a new save at its first start;
    /// an existing save the first time this build loads it (the late seed: nowhere near a claim, nothing in loaded chunks,
    /// POIs nobody has visited preferred); on every later load each enclave's POI is found again (or the
    /// enclave moves to the nearest free one) and a raised target is topped up. Everything is drawn from the world seed in
    /// (x, z) order, so a save always gets the same enclaves from the same POIs. Host only, main thread; the lookups the
    /// patches use read volatile arrays rebuilt here (the ScOutposts pattern).
    /// </summary>
    public static class ScEnclaves
    {
        public const string NavPrefix = "sc_enclave_";
        /// <summary>Every marker class (nav_objects.xml): a marker is re-registered under the class of its current standing.</summary>
        private static readonly string[] NavKinds = { "neutral", "friendly", "allied", "hostile" };
        private const string Salt = "sc.enclaves.v1";
        /// <summary>Prefab or POI-name words that make a likely raider den (the hostile ranking): a word of the name that starts with one.</summary>
        private static readonly string[] HostileWords = { "army", "military", "prison", "police", "fort", "junk", "scrap", "industrial", "factory", "camp" };
        private static readonly string[] DirKeys = { "scDirN", "scDirNE", "scDirE", "scDirSE", "scDirS", "scDirSW", "scDirW", "scDirNW" };

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
        private static PrefabInstance PoiOf(Enclave e)
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
            int sector = Mathf.RoundToInt(Mathf.Repeat(Mathf.Atan2(dx, dz) * Mathf.Rad2Deg, 360f) / 45f) % 8;   // 0 = north (+z), 2 = east (+x)
            return string.Format(Localization.Get("scEnclaveWhere"), Mathf.RoundToInt(Mathf.Sqrt(dx * dx + dz * dz)), Localization.Get(DirKeys[sector]));
        }

        public static string StandingKey(Enclave e)
        {
            return e.Hostile ? "hostile" : e.Standing >= 70 ? "allied" : e.Standing >= 30 ? "friendly" : "neutral";
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
            int ax = e.Key.x, az = e.Key.z, bx = e.Key.x + Math.Max(1, e.Size.x) - 1, bz = e.Key.z + Math.Max(1, e.Size.z) - 1;
            if (ScZone.IsLoaded(new Vector3i(ax, 0, az)) || ScZone.IsLoaded(new Vector3i(bx, 0, az)) || ScZone.IsLoaded(new Vector3i(ax, 0, bz))
                || ScZone.IsLoaded(new Vector3i(bx, 0, bz)) || ScZone.IsLoaded(new Vector3i((int)e.X, 0, (int)e.Z))) return true;
            return r.HasPlayer && ToBox(r.Player.x, r.Player.z, new[] { ax, bx, az, bz }) < r.PlayerRange;
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
        /// </summary>
        private static Plan MakePlan(World w, string mode, int want)
        {
            Vector3i min, max;
            if (!WorldExtent(w, out min, out max)) return null;
            bool late = mode != "new";
            var p = new Plan { Mode = mode, Late = late, Ring = mode != "top" };
            p.Target = TargetCount(min, max, out p.Km2);
            p.Want = want < 0 ? p.Target : want;
            p.Width = Math.Min(max.x - min.x, max.z - min.z);
            p.Seed = SeedFor(w, mode, ScCommunity.Today);
            p.Start = StartPoint(late, min, max, out p.StartWhat);
            Rules r = RulesFor(w, late, min, max);
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
            foreach (Pick k in p.Picks)
            {
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
                    Tier = k.E.Tier,
                    Hostile = k.Hostile,
                    SeededDay = today,
                    Mode = mode
                };
                All.Add(en);
                ScLog.Verbose("enclave " + en.Id + " '" + en.Name + "' at " + en.PoiName + " (" + en.Prefab + ", T" + en.Tier + ", " + en.Biome + ") " + en.Poi
                              + (k.Hostile ? ", hostile" : "") + (k.Ring ? ", start ring" : "") + " [" + mode + "]");
            }
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
                    if (pe.Key != e.Poi) ScLog.Out("enclave " + e.Id + " '" + e.Name + "': its POI's key moved from " + e.Poi + " to " + pe.Key + " (a POI pack update?)");
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
                    RemoveMarker(e);
                    All.Remove(e);
                    dropped++;
                    ScLog.Warn("enclave " + e.Id + " '" + e.Name + "': its POI " + e.Prefab + " at " + e.Poi + " is gone and no POI within "
                               + ScConfig.EnclaveRelocateRange + " m can take them - dropped");
                    continue;
                }
                ScLog.Out("enclave " + e.Id + " '" + e.Name + "': its POI " + e.Prefab + " at " + e.Poi + " is gone - moved to " + to.Display + " (" + to.Prefab + ") " + to.Key);
                r.Enclaves.Remove(e.Poi);
                r.Enclaves.Add(to.Key);
                e.Poi = to.Key;
                e.Size = to.Size;
                e.Prefab = to.Prefab;
                e.PoiName = to.Display;
                e.Biome = to.Biome;
                e.Row = to.Row;
                e.Tier = to.Tier;
                e.Mode = "moved";
                e.Lost = false;
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
        /// loaded chunks, near a claim, or in use by a run, contact, favour, scouting mark or quest.
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
                    if (why == "loaded" || why == "near a claim" || why == "in use") passing = true;
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
                    ScCommunity.Tell(player, string.Format(Localization.Get("scEnclaveFound"), e.Name, e.PoiName, StandingWord(e)), e.Hostile ? "ui_denied" : "");
                    ScOutposts.NoteLedger(string.Format(Localization.Get("scEnclaveLedgerFound"), e.Name, e.PoiName));
                    ScLog.Out("enclave " + e.Id + " '" + e.Name + "' found at " + e.PoiName + (e.Hostile ? " (hostile)" : ""));
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

        public static string NavClassFor(Enclave e) { return NavPrefix + StandingKey(e); }

        /// <summary>The marker's spot: the POI's centre on the ground (the static heightmap works with the chunk unloaded).</summary>
        private static Vector3 MarkerSpot(Enclave e)
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

        // ------------------------------------------------------------------ console ----

        private static string Line(Enclave e, Vector3? from)
        {
            return "#" + e.Id + " " + e.Name + (e.Hostile ? " [hostile]" : "") + (e.Known ? " - known" : " - not found") + " - " + e.PoiName + " (" + e.Prefab + ") T" + e.Tier
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
                     + "; the radio can reveal one " + (c.EnclaveRadioDay > ScCommunity.Today ? "from day " + c.EnclaveRadioDay : "now (a powered radio, an unfound one in range)"));
            foreach (Enclave e in All) into.Add("  " + Line(e, pl != null ? pl.position : (Vector3?)null));
        }

        /// <summary>sc enclave info &lt;id&gt;</summary>
        public static void InfoLines(Enclave e, List<string> into)
        {
            EntityPlayerLocal pl = ScCommunity.LocalPlayer;
            int[] b = e.Box;
            Vector3 cen = e.Centre;
            into.Add("#" + e.Id + " " + e.Name + ": " + (e.Hostile ? "hostile by nature" : "survivors") + ", standing " + e.Standing + " (" + StandingWord(e) + "), "
                     + (e.Known ? "known since day " + e.KnownDay : "not found yet") + ", seeded day " + e.SeededDay + " (" + e.Mode + ")");
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
                RemoveMarker(e);
                released += Release(PoiOf(e), w);
            }
            All.Clear();
            Community c = ScRegistry.Community;
            c.EnclaveSeeded = false;
            c.EnclaveSeedCount = 0;
            Rebuild();      // the old footprints must not count as homes for the new run
            string line = Seed(w, "late");
            int cleared = PreClearAll(w);
            Rebuild();
            RefreshMarkers(owner);
            ScRegistry.SaveAtomic();
            ScLog.Out("enclaves: reseeded from the console - " + old + " dropped (" + released + " sleeper area(s) respawn again), " + line);
            return old + " dropped (" + released + " sleeper area(s) given back their respawn), " + line + "; " + cleared + " sleeper area(s) kept clear";
        }
    }
}
