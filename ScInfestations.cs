using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>The guaranteed special (Q14 (b)): owed until a room is picked to bring it, pending while that room spawns, met once it came.</summary>
    public enum SpecialState { None, Owed, Pending, Met }

    /// <summary>
    /// An infested POI (Enclaves_Plan.md 2.12, milestone I1): a registry record keyed by the POI. The world itself changes
    /// only when one of its rooms spawns (Patches_Infestations), so it works in POIs never visited and survives a reload.
    /// </summary>
    public sealed class Infestation
    {
        public int Id;
        /// <summary>The POI's footprint: min corner (PrefabInstance.boundingBoxPosition, the key used everywhere) and rotated size.</summary>
        public Vector3i Poi;
        public Vector3i Size;
        /// <summary>The POI is found again by (x, z) and this prefab name on every load: a POI pack update can move the key's y.</summary>
        public string Prefab = "";
        public string PoiName = "";
        /// <summary>The prefab's DifficultyTier (from InfestHighTier up the rooms get InfestMultiplierHighTier).</summary>
        public int Tier;
        public int StartDay;
        /// <summary>How it began: "roll" (the daily roll), "spread" (from Source) or "console". Later milestones add their own (a lost siege).</summary>
        public string How = "roll";
        /// <summary>The infestation it spread from (0 = none: the daily roll or the console).</summary>
        public int Source;
        /// <summary>Known: its marker shows. KnownDay = the day it became known (-1 = not yet).</summary>
        public bool Known;
        public int KnownDay = -1;
        /// <summary>The special: its state, the room bringing it while Pending (that volume's BoxMin) and the class that came once Met.</summary>
        public SpecialState Special = SpecialState.None;
        public Vector3i SpecialHost;
        public string SpecialClass = "";
        /// <summary>The day it last spread (-1 = never): at most one spread a day.</summary>
        public int LastSpreadDay = -1;
        /// <summary>
        /// The rooms (their BoxMin) it found cleared and made due, until each rolls again with the hooks on: till then the
        /// POI is not cleared, though vanilla's Touch keeps a room a player stands in from coming due. Never a trigger room:
        /// its trigger may never fire again.
        /// </summary>
        public readonly List<Vector3i> Rearmed = new List<Vector3i>();

        /// <summary>The POI footprint as an inclusive box {xMin, xMax, zMin, zMax}.</summary>
        public int[] Box { get { return new[] { Poi.x, Poi.x + Math.Max(1, Size.x) - 1, Poi.z, Poi.z + Math.Max(1, Size.z) - 1 }; } }

        public Vector3 Centre { get { return new Vector3(Poi.x + Size.x * 0.5f, Poi.y, Poi.z + Size.z * 0.5f); } }

        /// <summary>XZ distance from a point to the footprint (0 inside it), with no array (the per-second tick).</summary>
        public float DistanceXZ(float x, float z)
        {
            float x0 = Poi.x, x1 = Poi.x + Math.Max(1, Size.x), z0 = Poi.z, z1 = Poi.z + Math.Max(1, Size.z);
            float dx = x < x0 ? x0 - x : (x > x1 ? x - x1 : 0f);
            float dz = z < z0 ? z0 - z : (z > z1 ? z - z1 : 0f);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }

    /// <summary>
    /// Infestations, milestone I1 (I1_SPEC.md, Enclaves_Plan.md 2.12-2.13, research infestations.md): POIs overrun by more
    /// and worse zombies, kept as registry records.
    ///
    /// When and where: at each day rollover (ScDaily) the infestations are polled for cleared ones, each one old enough
    /// may spread to the nearest eligible POI, and a daily roll may start a new one - never past a cap of about
    /// InfestPerKm2 per km2. Every POI goes through one filter (Refuse): a real POI with sleeper rooms that count, away
    /// from traders, nobody's home, claim, outpost or enclave, not quest-locked, nobody near and no room live.
    ///
    /// Inside (Patches_Infestations), all applied when a room spawns: about twice the zombies (vanilla's infested-quest
    /// multiplier, carried on PrefabInstance.LastQuestClass), vanilla's infested spawn points (LastRefreshType), a share of
    /// normal zombies swapped for their ferals (ScLoadout's GetRandomFromGroupList postfix, scoped to the room's
    /// UpdateSpawn) and one special in one room (a radiated zombie below InfestSpecialRadiatedBelowGS, then charged or
    /// infernal). Infesting a POI re-arms the rooms already fought (Rearm); never World.ResetPOIS.
    ///
    /// Clearing: every room of the POI cleared (ScSecurity.IsCleared on the persisted rooms, a trigger room's respawn time
    /// aside: Cleared) ends it, checked when a room is cleared and in the daily poll. Seeing them (Q15 (a)): a purple
    /// marker once known - found within InfestDiscoverRange, reported by a powered radio when it starts within
    /// InfestRadioRange of home, or spread from a known one.
    ///
    /// Host only, main thread; the lookups the patches use read volatile arrays rebuilt here (the ScEnclaves pattern).
    /// </summary>
    public static class ScInfestations
    {
        public const string NavClass = "sc_infestation";
        /// <summary>
        /// The vanilla gamestage groups the special comes from (gamestages.xml:176-178, one stage-1 spawner each), given to the
        /// room cleaned of their leading digit (GameStageGroup.CleanName, as SleeperVolume.Create cleans its own group): what
        /// GameStageGroup.TryGet looks up, and what the world saves - the game knows them with SurvivorCommunity gone.
        /// </summary>
        private const string RadiatedGroup = "3GroupAllRadiated", ChargedGroup = "3GroupAllCharged", InfernalGroup = "3GroupAllInfernal";
        /// <summary>
        /// The most zombies an infested room may roll unless its prefab asks for more on its own: SleeperVolume.Write saves a
        /// room's counts as bytes (infestations.md section 8). The largest installed room (30) doubled is 60.
        /// </summary>
        private const int RoomCap = 120;
        private static readonly FastTags<TagGroup.Global> zombieTag = FastTags<TagGroup.Global>.Parse("zombie");

        /// <summary>The keys and records of every infestation while Infestations is on: read by the spawn patches.</summary>
        private static volatile Vector3i[] keys = new Vector3i[0];
        private static volatile Infestation[] live = new Infestation[0];

        /// <summary>
        /// The infested room inside its UpdateSpawn right now (the prefix sets it, the finalizer clears it; main thread). The
        /// GetRandomFromGroupList postfix reads it first: null for every other pick - biome spawns, hordes, SC's own rolls.
        /// </summary>
        public static SleeperVolume CurrentVolume;
        private static Infestation current;

        private static readonly Dictionary<int, Vector3> markers = new Dictionary<int, Vector3>();
        private static bool markersRestored;
        /// <summary>The frame the daily pass last ran: the ticker's catch-up runs ScDaily once per missed day in one frame.</summary>
        private static int passFrame = -1;
        /// <summary>
        /// Set once OnGameStart has run: the ticker starts a frame or more before GameStartDone, and must not find, mark or
        /// roll anything against records that OnGameStart is about to check (E1's lesson).
        /// </summary>
        private static bool ready;
        /// <summary>The carriers of the rooms' count (vanilla reads nothing but SpawnMultiplier from LastQuestClass), low and high tier.</summary>
        private static QuestClass carrier, carrierHigh;
        /// <summary>POIs whose LastRefreshType got the infested tag from us this session: it comes off again when the infestation ends.</summary>
        private static readonly HashSet<Vector3i> tagged = new HashSet<Vector3i>();
        /// <summary>Normal zombie class id -> its feral's class id (0 = none), filled as picks come.</summary>
        private static readonly Dictionary<int, int> ferals = new Dictionary<int, int>();
        /// <summary>
        /// A record changed inside a spawn hook (the special's room picked, its special come or lost, a re-armed room rolled
        /// again): saved by the next tick, not in the middle of a spawn (or with the world, where no tick runs).
        /// </summary>
        private static bool dirty;

        public static List<Infestation> All { get { return ScRegistry.Infestations; } }

        public static void Reset()
        {
            markers.Clear();
            markersRestored = false;
            passFrame = -1;
            ready = false;
            keys = new Vector3i[0];
            live = new Infestation[0];
            CurrentVolume = null;
            current = null;
            carrier = carrierHigh = null;
            tagged.Clear();
            ferals.Clear();
            dirty = false;
        }

        /// <summary>The registry was (re)loaded or reset (ScOutposts.OnRegistryChanged): ids squared, lookups rebuilt, markers restored on the next tick.</summary>
        public static void OnRegistryChanged()
        {
            markersRestored = false;
            markers.Clear();
            Community c = ScRegistry.Community;
            var ids = new HashSet<int>();
            int max = 0;
            foreach (Infestation inf in All) if (inf.Id > max) max = inf.Id;
            foreach (Infestation inf in All)
            {
                if (inf.Id <= 0 || !ids.Add(inf.Id))
                {
                    int old = inf.Id;
                    inf.Id = Math.Max(c.NextInfestationId, max) + 1;
                    max = inf.Id;
                    ids.Add(inf.Id);
                    ScLog.Warn("infestation at " + inf.PoiName + " had a duplicate id " + old + " - now " + inf.Id);
                }
                if (inf.Id > c.NextInfestationId) c.NextInfestationId = inf.Id;
            }
            Rebuild();
        }

        /// <summary>Rebuilds the lookup arrays (after any change to the list; empty while Infestations is off).</summary>
        public static void Rebuild()
        {
            ScConfig.EnsureParsed();        // the registry loads before GameStartDone parses it: Infestations=false must hold from the start
            var k = new List<Vector3i>();
            var l = new List<Infestation>();
            if (ScConfig.Infestations)
            {
                foreach (Infestation inf in All)
                {
                    k.Add(inf.Poi);
                    l.Add(inf);
                }
            }
            live = l.ToArray();
            keys = k.ToArray();
        }

        // ------------------------------------------------------------------ queries ----

        /// <summary>Is the POI with this key infested (while Infestations is on)? Thread-safe.</summary>
        public static bool IsInfestedPoiKey(Vector3i key)
        {
            Vector3i[] k = keys;
            for (int i = 0; i < k.Length; i++) if (k[i] == key) return true;
            return false;
        }

        /// <summary>The infestation of this POI while Infestations is on, or null (the spawn patches; main thread).</summary>
        public static Infestation At(PrefabInstance pi)
        {
            if (pi == null) return null;
            Infestation[] l = live;
            if (l.Length == 0) return null;
            Vector3i key = pi.boundingBoxPosition;
            for (int i = 0; i < l.Length; i++) if (l[i].Poi == key) return l[i];
            return null;
        }

        public static Infestation ById(int id)
        {
            foreach (Infestation inf in All) if (inf.Id == id) return inf;
            return null;
        }

        /// <summary>The record on this POI key, whether Infestations is on or not.</summary>
        public static Infestation ByPoi(Vector3i key)
        {
            foreach (Infestation inf in All) if (inf.Poi == key) return inf;
            return null;
        }

        /// <summary>The live POI of an infestation (found by x, z and prefab; the exact key as a fallback), or null.</summary>
        private static PrefabInstance PoiOf(Infestation inf)
        {
            PoiEntry pe = ScPoiIndex.Built ? ScPoiIndex.Find(inf.Poi.x, inf.Poi.z, inf.Prefab) : null;
            return pe != null ? pe.Pi : ScSecurity.PoiByKey(inf.Poi);
        }

        /// <summary>The POI's generated sleeper areas, copied: the chunk thread appends to pi.sleeperVolumes.</summary>
        private static SleeperVolume[] Rooms(PrefabInstance pi)
        {
            if (pi == null) return new SleeperVolume[0];
            try { return pi.sleeperVolumes.ToArray(); }
            catch (Exception) { return new SleeperVolume[0]; }
        }

        /// <summary>
        /// A trigger room (flags &amp; 7 = Trigger) rolls only when its trigger fires - a door opened, a switch pressed - never
        /// on approach: CheckTouching skips one with no sleepers tracked (IsTriggerAndNoRespawn, V:SleeperVolume.cs:302-313,
        /// :657), so once cleared it comes back only if its trigger fires again, which a door left open or broken never does.
        /// </summary>
        private static bool IsTriggerRoom(SleeperVolume v)
        {
            return (v.flags & 7) == (int)SleeperVolume.ETriggerType.Trigger;
        }

        /// <summary>XZ distance from a point to an inclusive box {xMin, xMax, zMin, zMax} (0 inside it).</summary>
        private static float ToBox(float x, float z, int[] b)
        {
            float dx = x < b[0] ? b[0] - x : (x > b[1] + 1 ? x - b[1] - 1 : 0f);
            float dz = z < b[2] ? b[2] - z : (z > b[3] + 1 ? z - b[3] - 1 : 0f);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>XZ distance from a point to a POI's footprint (0 inside it).</summary>
        private static float ToFootprint(float x, float z, Vector3i at, Vector3i size)
        {
            float x0 = at.x, x1 = at.x + Math.Max(1, size.x), z0 = at.z, z1 = at.z + Math.Max(1, size.z);
            float dx = x < x0 ? x0 - x : (x > x1 ? x - x1 : 0f);
            float dz = z < z0 ? z0 - z : (z > z1 ? z - z1 : 0f);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private static float Dist(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx, dz = az - bz;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>Where the roll's distances and the radio measure from: home, else the local player.</summary>
        private static bool From(out Vector3 at)
        {
            if (ScZone.HasHome) { Vector3i h = ScZone.Home; at = new Vector3(h.x, h.y, h.z); return true; }
            EntityPlayerLocal pl = ScCommunity.LocalPlayer;
            at = pl != null ? pl.position : Vector3.zero;
            return pl != null;
        }

        /// <summary>The cap: clamp(round(InfestPerKm2 x km2), InfestMin, InfestMax) over the world's XZ extent (8 on a 6k map, 13 on 8k).</summary>
        public static int TargetCap(World w)
        {
            Vector3i min, max;
            if (w == null || !w.GetWorldExtent(out min, out max) || max.x <= min.x || max.z <= min.z) return ScConfig.InfestMin;
            float km2 = (max.x - min.x) / 1000f * ((max.z - min.z) / 1000f);
            return Mathf.Clamp(Mathf.RoundToInt(ScConfig.InfestPerKm2 * km2), ScConfig.InfestMin, ScConfig.InfestMax);
        }

        public static string SpecialWord(Infestation inf)
        {
            switch (inf.Special)
            {
                case SpecialState.Owed: return "owed";
                case SpecialState.Pending: return "pending in the room at " + inf.SpecialHost;
                case SpecialState.Met: return "came (" + inf.SpecialClass + ")";
                default: return "none";
            }
        }

        // ------------------------------------------------------------------ which POIs ----

        /// <summary>What the filter reads from the world once per pass.</summary>
        private sealed class Rules
        {
            public World W;
            /// <summary>Trader protect boxes, inclusive {xMin, xMax, zMin, zMax}.</summary>
            public readonly List<int[]> Traders = new List<int[]>();
            /// <summary>Every player in the world (remote ones too, on the host).</summary>
            public readonly List<Vector3> Players = new List<Vector3>();
            /// <summary>POIs already in use: radio contacts, open POI favours, supply runs' destinations, radio scouting marks, the player's quests in progress (E1's rule).</summary>
            public readonly HashSet<Vector3i> Taken = new HashSet<Vector3i>();
        }

        private static Rules RulesFor(World w)
        {
            var r = new Rules { W = w };
            DynamicPrefabDecorator dec = GameManager.Instance.GetDynamicPrefabDecorator();
            List<TraderArea> areas = dec != null ? dec.GetTraderAreas() : null;
            if (areas != null)
                foreach (TraderArea t in areas)
                    if (t != null) r.Traders.Add(new[] { t.ProtectPosition.x, t.ProtectPosition.x + Math.Max(1, t.ProtectSize.x) - 1, t.ProtectPosition.z, t.ProtectPosition.z + Math.Max(1, t.ProtectSize.z) - 1 });
            List<EntityPlayer> players = w.Players.list;
            for (int i = 0; i < players.Count; i++) if (players[i] != null) r.Players.Add(players[i].position);
            // a survivor waiting at a radio contact, a recruit's "secure this POI" favour, a supply run's destination, a radio
            // scouting mark, a POI one of the player's quests leads to: an infestation there would change a task already taken on
            foreach (Contact ct in ScRegistry.Contacts) r.Taken.Add(ct.PoiKey);
            foreach (Prospect p in ScRegistry.Prospects.Values) if (p.Favour == FavourKind.Poi && !p.Done) r.Taken.Add(p.PoiPos);
            foreach (Member m in ScRegistry.Members.Values) if (ScRunners.IsPhysical(m)) r.Taken.Add(m.RunDest);
            foreach (Scout s in ScRegistry.Community.Scouts) r.Taken.Add(s.Poi);     // a building the radio told the player to go to
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
            return r;
        }

        /// <summary>
        /// Has the prefab a sleeper area that counts for clearing (not quest-exclude) and that coming near rolls (not a
        /// trigger room)? With none, the POI would read cleared at once: a cleared trigger room counts as cleared (Cleared),
        /// so a building of trigger rooms alone, already fought, would end with no fight.
        /// </summary>
        private static bool Fightable(PrefabInstance pi)
        {
            var pl = pi != null && pi.prefab != null && pi.prefab.SleeperVolumeList != null ? pi.prefab.SleeperVolumeList.List : null;
            if (pl == null) return false;
            for (int i = 0; i < pl.Count; i++)
                if (pl[i] != null && !pl[i].isQuestExclude && (pl[i].flags & 7) != (int)SleeperVolume.ETriggerType.Trigger) return true;
            return false;
        }

        /// <summary>Is any generated room of the POI live (spawned, spawning, or holding sleepers)? Someone is in it, or was a moment ago.</summary>
        private static bool Live(PrefabInstance pi)
        {
            foreach (SleeperVolume v in Rooms(pi))
                if (v != null && (v.isSpawned || v.isSpawning || v.respawnMap.Count > 0)) return true;
            return false;
        }

        /// <summary>
        /// Why this POI cannot be infested (a short reason the console prints), or null when it can. forced (the console)
        /// keeps only the rules no test may break: sleepers that count, not already infested, never an enclave's building,
        /// the home zone or anything claimed. The roll and spread add: away from traders, not quest-locked, not in use (a
        /// radio contact, an open POI favour, a supply run, a scouting mark, a quest in progress), no player within
        /// InfestPlayerMargin, no room live - nobody watches it turn.
        /// </summary>
        private static string Refuse(PoiEntry e, Rules r, bool forced)
        {
            if (e == null || e.Pi == null) return "no POI";
            if (e.Sleepers <= 0 || !Fightable(e.Pi)) return "no sleeper area that counts for clearing";
            if (ByPoi(e.Key) != null) return "already infested";
            if (ScEnclaves.IsEnclavePoi(e.Pi)) return "an enclave's building";
            if (ScSecurity.OverlapsHome(e.Pi)) return "your home zone";
            // bedrolls, claims (LandClaimUpgrade zones too), outposts and enclave footprints: the one "someone lives here" test
            if (e.Pi.CheckForAnyPlayerHome(r.W) != GameUtils.EPlayerHomeType.None) return "claimed (a land claim, a bedroll, an outpost or an enclave)";
            if (forced) return null;
            foreach (int[] t in r.Traders) if (ToBox(e.X, e.Z, t) < ScConfig.InfestTraderDistance) return "near a trader";
            if (e.Pi.lockInstance != null && !e.Pi.lockInstance.CheckQuestLock()) return "quest-locked";
            if (r.Taken.Contains(e.Key)) return "in use (a radio contact, a recruit's favour, a supply run, a scouting mark or one of your quests)";
            foreach (Vector3 p in r.Players) if (ToFootprint(p.x, p.z, e.Key, e.Size) < ScConfig.InfestPlayerMargin) return "a player within " + ScConfig.InfestPlayerMargin + " m";
            if (Live(e.Pi)) return "a room is live";
            return null;
        }

        /// <summary>
        /// The daily roll's POI: the filter, the tier window of the day (InfestTierByDay), weighted 1 within
        /// InfestMinDist..InfestMaxDist of home (of the player without one) and InfestOutsideWeight outside. Null: none fits.
        /// </summary>
        private static PoiEntry PickNew(int day, Rules r, GameRandom rnd, out int candidates)
        {
            int tmin, tmax;
            ScConfig.InfestTierWindow(day, out tmin, out tmax);
            Vector3 from;
            bool hasFrom = From(out from);
            var cands = new List<PoiEntry>();
            var weights = new List<float>();
            double sum = 0;
            foreach (PoiEntry e in ScPoiIndex.All)
            {
                if (e.Sleepers <= 0 || e.Tier < tmin || e.Tier > tmax) continue;
                float wt = 1f;
                if (hasFrom)
                {
                    float d = Dist(e.X, e.Z, from.x, from.z);
                    if (d < ScConfig.InfestMinDist || d > ScConfig.InfestMaxDist) wt = ScConfig.InfestOutsideWeight;
                }
                if (wt <= 0f || Refuse(e, r, false) != null) continue;
                cands.Add(e);
                weights.Add(wt);
                sum += wt;
            }
            candidates = cands.Count;
            if (cands.Count == 0 || sum <= 0) return null;
            double x = rnd.RandomDouble * sum;
            for (int i = 0; i < cands.Count; i++)
            {
                x -= weights[i];
                if (x < 0) return cands[i];
            }
            return cands[cands.Count - 1];
        }

        // ------------------------------------------------------------------ infesting a POI ----

        /// <summary>vanilla's own wait for a room a player is inside of (SleeperVolume.cPlayerInsideDelayTime); Touch keeps pushing it while they stay.</summary>
        private const ulong PlayerInsideDelay = 1000;

        /// <summary>Is a player within InfestRearmPlayerRange of the POI's footprint?</summary>
        private static bool PlayerNear(PrefabInstance pi, World w)
        {
            List<EntityPlayer> players = w.Players.list;
            for (int i = 0; i < players.Count; i++)
            {
                EntityPlayer p = players[i];
                if (p != null && ToFootprint(p.position.x, p.position.z, pi.boundingBoxPosition, pi.boundingBoxSize) < ScConfig.InfestRearmPlayerRange) return true;
            }
            return false;
        }

        /// <summary>
        /// When a room made due again may roll: at once with nobody near (infestations.md 2.3), else after vanilla's
        /// player-inside wait - world time moves every few ticks, and a room due at once could re-roll around a player standing
        /// in it before Touch next pushes it.
        /// </summary>
        private static ulong DueTime(bool near, World w)
        {
            return w.worldTime + (near ? PlayerInsideDelay : 1UL);
        }

        /// <summary>
        /// A POI's rooms made dangerous again (infestations.md 2.3): a cleared room is due (vanilla re-rolls it at the next
        /// approach, with the hooks on; DueTime and Touch keep a room quiet while a player stays in it) and stays on the
        /// record's Rearmed list until it rolls - a trigger room is due as well, should its trigger fire again, but not
        /// listed; a room with sleepers left from before is despawned and reset, but only with nobody within
        /// InfestRearmPlayerRange (awake zombies would stay, untracked, under a fresh wave) - else it is left as it is, and its
        /// next roll still gets the count and the spawn points. A room never generated needs nothing. Never
        /// World.ResetPOIS; no world volume is added or removed.
        /// </summary>
        private static void Rearm(Infestation inf, PrefabInstance pi, World w, out int due, out int reset, out int left)
        {
            due = reset = left = 0;
            if (pi == null || w == null) return;
            bool near = PlayerNear(pi, w);
            ulong at = DueTime(near, w);
            foreach (SleeperVolume v in Rooms(pi))
            {
                if (v == null || v.isQuestExclude) continue;
                if (v.wasCleared && !v.isSpawning && v.respawnMap.Count == 0)
                {
                    if (v.respawnTime > at) v.respawnTime = at;
                    if (!IsTriggerRoom(v) && !inf.Rearmed.Contains(v.BoxMin)) inf.Rearmed.Add(v.BoxMin);
                    due++;
                    continue;
                }
                if (v.respawnMap.Count == 0 && !v.isSpawned && !v.isSpawning) continue;    // never spawned: it rolls with the hooks on
                if (near) { left++; continue; }
                v.DespawnAndReset(w);
                reset++;
            }
        }

        /// <summary>
        /// Starts an infestation (the roll, a spread, the console): the record (the special owed when InfestSpecial is on
        /// and the sandbox's max enemy tier reaches radiated), the lookups, the re-arm, then who learns of it - the console
        /// and a spread from a known one are known; otherwise a powered radio reports it within InfestRadioRange of home.
        /// Saves; one log line. Main thread.
        /// </summary>
        private static Infestation Infest(PoiEntry pe, World w, int day, Infestation from, string how, EntityPlayerLocal player, out string rooms)
        {
            Community c = ScRegistry.Community;
            var inf = new Infestation
            {
                Id = ++c.NextInfestationId,
                Poi = pe.Key,
                Size = pe.Size,
                Prefab = pe.Prefab,
                PoiName = pe.Display,
                Tier = pe.Tier,
                StartDay = day,
                How = how,
                Source = from != null ? from.Id : 0,
                Special = ScConfig.InfestSpecial && EntityFactory.MaxEntityTier >= EntityClass.EntityTierTypes.Radiated ? SpecialState.Owed : SpecialState.None
            };
            All.Add(inf);
            Rebuild();
            int due, reset, left;
            Rearm(inf, pe.Pi, w, out due, out reset, out left);
            rooms = due + " cleared room(s) due again, " + reset + " reset" + (left > 0 ? ", " + left + " left as they are (a player within " + ScConfig.InfestRearmPlayerRange + " m)" : "");
            string heard = "";
            if (how == "console")
            {
                inf.Known = true;
                inf.KnownDay = day;
                ScOutposts.NoteLedger(string.Format(Localization.Get("scInfestLedgerFound"), inf.PoiName));
            }
            else if (from != null && from.Known)
            {
                inf.Known = true;
                inf.KnownDay = day;
                heard = ", known (spread from a known one)";
                if (player != null) ScCommunity.Tell(player, string.Format(Localization.Get("scInfestSpread"), from.PoiName, inf.PoiName), "");
                ScOutposts.NoteLedger(string.Format(Localization.Get("scInfestLedgerSpread"), from.PoiName, inf.PoiName));
            }
            else
            {
                Vector3 at;
                if (ScRadio.Online() && From(out at))
                {
                    Vector3 cen = inf.Centre;
                    if (Dist(cen.x, cen.z, at.x, at.z) <= ScConfig.InfestRadioRange)
                    {
                        inf.Known = true;
                        inf.KnownDay = day;
                        string where = ScEnclaves.Where(at, cen);
                        heard = ", heard on the radio (" + where + " from " + (ScZone.HasHome ? "home" : "you") + ")";
                        if (player != null) ScCommunity.Tell(player, string.Format(Localization.Get("scInfestRadio"), inf.PoiName, where), "");
                        ScOutposts.NoteLedger(string.Format(Localization.Get("scInfestLedgerRadio"), inf.PoiName));
                    }
                }
            }
            if (inf.Known && player != null) AddMarker(inf, player);
            ScLog.Out("infestation #" + inf.Id + " at " + inf.PoiName + " (" + inf.Prefab + ", T" + inf.Tier + ") " + inf.Poi + " [" + how
                      + (from != null ? " from #" + from.Id : "") + "]: " + rooms + "; special " + SpecialWord(inf) + (inf.Known ? heard : ", not known yet"));
            ScRegistry.SaveAtomic();
            return inf;
        }

        /// <summary>The nearest POI (by centre) within InfestSpreadRange of the source that the filter allows, infested from it. Null: none fits.</summary>
        private static Infestation Spread(Infestation src, World w, Rules r, int day, EntityPlayerLocal player, out string why)
        {
            why = null;
            Vector3 c = src.Centre;
            var near = new List<PoiEntry>();
            ScPoiIndex.Near(c.x, c.z, ScConfig.InfestSpreadRange, near);
            foreach (PoiEntry pe in near)
            {
                if (pe.Key == src.Poi || Refuse(pe, r, false) != null) continue;
                src.LastSpreadDay = day;
                string rooms;
                return Infest(pe, w, day, src, "spread", player, out rooms);
            }
            why = "no POI within " + ScConfig.InfestSpreadRange + " m can take it";
            return null;
        }

        // ------------------------------------------------------------------ clearing ----

        /// <summary>
        /// A room vanilla closed with nobody spawned while it has spawn points and something to roll at its gamestage: the
        /// enemy cap was full when it started (infestations.md 1.5), so it was never fought. A room whose group has no stage at
        /// its gamestage, or that rolls nothing (its max is 0, or a min-0 roll came out 0), is empty by design and counts as
        /// cleared; so does a trigger room, which approaching it would never roll again.
        /// </summary>
        private static bool Unfought(SleeperVolume v)
        {
            if (!v.wasCleared || v.numSpawned != 0 || v.respawnMap.Count > 0 || v.isSpawning || v.spawnCountMax <= 0 || IsTriggerRoom(v)) return false;
            if (v.spawnPointList == null || v.spawnPointList.Count == 0) return false;
            // a room whose roll came out 0 (spawnCountMin 0) keeps no GroupCount: empty by design, as vanilla's clear quest
            // counts it (SleeperEventData drops any wasCleared room); the cap and blocked spawn points leave the roll there
            if (Rolled(v) <= 0) return false;
            GameStageGroup g = GameStageGroup.TryGet(v.groupName ?? "") ?? GameStageGroup.TryGet(GameStageGroup.cDefaultGroupName);
            return g != null && g.spawner != null && g.spawner.GetStage(v.gameStage) != null;
        }

        /// <summary>
        /// The rooms that still owe a roll with the infestation on, made due (again, DueTime): each room it re-armed that is
        /// not due within the player-inside wait (a world saved before the registry - a crash - or a roll while Infestations
        /// was off; a room Touch is pushing is left to it), and each unfought one. A re-armed room no longer in the world is
        /// forgotten. Returns how many are unfought.
        /// </summary>
        private static int DueAgain(Infestation inf, PrefabInstance pi, World w)
        {
            SleeperVolume[] rooms = Rooms(pi);
            ulong now = w.worldTime, at = 0;
            if (inf.Rearmed.Count > 0 && rooms.Length > 0)
                inf.Rearmed.RemoveAll(b => Array.FindIndex(rooms, v => v != null && v.BoxMin == b) < 0);
            int unfought = 0;
            foreach (SleeperVolume v in rooms)
            {
                if (v == null || v.isQuestExclude || !v.wasCleared || v.isSpawning || v.respawnMap.Count > 0) continue;
                bool rearmed = inf.Rearmed.Contains(v.BoxMin);
                if (!rearmed && !Unfought(v)) continue;
                if (!rearmed) unfought++;
                if (v.respawnTime <= now + PlayerInsideDelay) continue;
                if (at == 0) at = DueTime(PlayerNear(pi, w), w);
                v.respawnTime = at;
            }
            return unfought;
        }

        /// <summary>
        /// Is the POI cleared? ScSecurity.IsCleared on the persisted rooms (every room generated, none alive, all cleared,
        /// none due), except that a cleared trigger room past its respawn time is not due: approaching it never rolls it
        /// (IsTriggerRoom), and one whose trigger will not fire again would hold the POI for good. Recounted only when
        /// IsCleared says no.
        /// </summary>
        private static bool Cleared(World w, PrefabInstance pi, out string why)
        {
            if (ScSecurity.IsCleared(w, pi, out why, false)) return true;
            var pl = pi.prefab != null && pi.prefab.SleeperVolumeList != null ? pi.prefab.SleeperVolumeList.List : null;
            if (pl == null) return false;
            int defined = 0, generated = 0, triggers = 0;
            foreach (var psv in pl) if (psv != null && !psv.isQuestExclude) defined++;
            ulong now = w.worldTime;
            foreach (SleeperVolume v in Rooms(pi))
            {
                if (v == null) continue;
                if (v.respawnMap.Count > 0) return false;           // sleepers alive
                if (v.isQuestExclude) continue;
                generated++;
                if (!v.wasCleared) return false;
                if (now < v.respawnTime || GameUtils.CheckForAnyPlayerHome(w, v.BoxMin, v.BoxMax) != GameUtils.EPlayerHomeType.None) continue;
                if (!IsTriggerRoom(v)) return false;                 // due: coming near it rolls it again
                triggers++;
            }
            if (generated < defined || triggers == 0) return false;
            why = null;
            return true;
        }

        /// <summary>
        /// Ends the infestation when its POI is cleared (Cleared) and every room it re-armed has rolled again. An unfought
        /// room is made due again instead of counting. Main thread.
        /// </summary>
        private static bool Evaluate(Infestation inf, PrefabInstance pi, World w, EntityPlayerLocal player)
        {
            if (inf == null || pi == null || w == null) return false;
            int unfought = DueAgain(inf, pi, w);
            if (unfought > 0)
            {
                ScLog.Verbose("infestation #" + inf.Id + " at " + inf.PoiName + ": " + unfought + " room(s) closed with nobody spawned (the enemy cap) - due again");
                return false;
            }
            if (inf.Rearmed.Count > 0) return false;      // rooms cleared before it began have not rolled again yet
            string why;
            if (!Cleared(w, pi, out why)) return false;
            End(inf, "cleared", player);
            return true;
        }

        /// <summary>
        /// Takes the infestation's state off the POI: our carrier off LastQuestClass, and the infested tag off LastRefreshType
        /// when we put it there and no infested quest has rebuilt the POI since (that quest's tag stays).
        /// </summary>
        private static void Restore(PrefabInstance pi)
        {
            if (pi == null) return;
            QuestClass q = pi.LastQuestClass;
            bool ours = q != null && (q == carrier || q == carrierHigh);
            bool questInfested = q != null && !ours && q.ExtraTags.Test_AnySet(QuestEventManager.infestedTag);
            if (tagged.Remove(pi.boundingBoxPosition) && !questInfested) pi.LastRefreshType = pi.LastRefreshType.Remove(QuestEventManager.infestedTag);
            if (ours) pi.LastQuestClass = null;
        }

        /// <summary>
        /// Ends an infestation: cleared in play (a ledger line, and a tell when the player is near or knew of it), or from the
        /// console (record and marker only; the rooms are left as they are). Saves.
        /// </summary>
        private static void End(Infestation inf, string how, EntityPlayerLocal player)
        {
            RemoveMarker(inf);
            All.Remove(inf);
            Rebuild();
            Restore(PoiOf(inf));
            if (how == "cleared")
            {
                bool near = player != null && inf.DistanceXZ(player.position.x, player.position.z) <= ScConfig.InfestDiscoverRange;
                ScOutposts.NoteLedger(string.Format(Localization.Get("scInfestLedgerCleared"), inf.PoiName));
                if (player != null && (near || inf.Known)) ScCommunity.Tell(player, string.Format(Localization.Get("scInfestCleared"), inf.PoiName), "");
            }
            ScLog.Out("infestation #" + inf.Id + " at " + inf.PoiName + " ended (" + how + ") after " + Math.Max(0, ScCommunity.Today - inf.StartDay) + " day(s)"
                      + (inf.Special == SpecialState.Met ? "; its special was " + inf.SpecialClass : ""));
            ScRegistry.SaveAtomic();
        }

        // ------------------------------------------------------------------ the spawn hooks (Patches_Infestations; main thread) ----

        private static QuestClass Carrier(bool high)
        {
            if (carrier == null) carrier = new QuestClass("scinfestation");
            if (carrierHigh == null) carrierHigh = new QuestClass("scinfestationhigh");
            QuestClass q = high ? carrierHigh : carrier;
            q.SpawnMultiplier = high ? ScConfig.InfestMultiplierHighTier : ScConfig.InfestMultiplier;
            return q;
        }

        /// <summary>
        /// The POI's infested state, what vanilla reads when a room rolls (infestations.md 3): the infested tag on
        /// LastRefreshType opens vanilla's infested spawn points; our carrier on LastQuestClass multiplies the count - unless a
        /// real quest's class is there with a multiplier at least as large (a quest's is never lowered). Both are runtime only.
        /// </summary>
        private static void Apply(PrefabInstance pi)
        {
            bool high = pi.prefab != null && pi.prefab.DifficultyTier >= ScConfig.InfestHighTier;
            QuestClass c = Carrier(high);
            QuestClass q = pi.LastQuestClass;
            if (q == null || q == carrier || q == carrierHigh || q.SpawnMultiplier < c.SpawnMultiplier) pi.LastQuestClass = c;
            if (!pi.LastRefreshType.Test_AnySet(QuestEventManager.infestedTag))
            {
                pi.LastRefreshType = pi.LastRefreshType | QuestEventManager.infestedTag;
                tagged.Add(pi.boundingBoxPosition);
            }
        }

        /// <summary>
        /// UpdatePlayerTouched prefix: a room of an infested POI about to roll (past vanilla's own early return,
        /// V:SleeperVolume.cs:555) gets the POI's infested state first. Returns 0 (nothing), 1 (a roll that restores the
        /// sleepers left from before) or 2 (a fresh roll: a due room is Reset by vanilla, or the room never spawned; a
        /// re-armed room has now rolled again). Never for an enclave's POI (Patches_Enclaves owns those rooms).
        /// </summary>
        public static int BeforeRoll(SleeperVolume v, World w)
        {
            if (v == null || w == null) return 0;
            PrefabInstance pi = v.PrefabInstance;
            Infestation inf = At(pi);
            if (inf == null || ScEnclaves.IsEnclavePoi(pi)) return 0;
            if (v.isSpawned || (w.worldTime < v.respawnTime && v.wasCleared)) return 0;
            Apply(pi);
            if (w.worldTime < v.respawnTime && (v.numSpawned != 0 || v.respawnMap.Count != 0)) return 1;
            if (inf.Rearmed.Remove(v.BoxMin)) dirty = true;
            return 2;
        }

        /// <summary>UpdatePlayerTouched postfix: the roll capped well under 255; a fresh roll may make this room the one that brings the special.</summary>
        public static void AfterRoll(SleeperVolume v, World w, int state)
        {
            PrefabInstance pi = v != null ? v.PrefabInstance : null;
            Infestation inf = At(pi);
            if (inf == null) return;
            CapRoom(v);
            if (state == 2) MaybeHost(v, pi, inf, w);
        }

        /// <summary>How many the room's roll will spawn in all (every GroupCount of it).</summary>
        private static int Rolled(SleeperVolume v)
        {
            List<SleeperVolume.GroupCount> l = v.groupCountList;
            int total = 0;
            if (l != null) for (int i = 0; i < l.Count; i++) total += l[i].count;
            return total;
        }

        private static void CapRoom(SleeperVolume v)
        {
            List<SleeperVolume.GroupCount> l = v.groupCountList;
            if (l == null || l.Count == 0) return;
            int total = Rolled(v);
            int cap = Math.Max(RoomCap, (int)v.spawnCountMax);
            if (total <= cap) return;
            // from the last entry back (a script room holds several), until the room is under the cap
            int excess = total - cap;
            for (int i = l.Count - 1; i >= 0 && excess > 0; i--)
            {
                SleeperVolume.GroupCount g = l[i];
                int cut = Math.Min(excess, g.count);
                if (cut <= 0) continue;
                g.count -= cut;
                l[i] = g;
                excess -= cut;
            }
            ScLog.Out("infested room at " + v.BoxMin + ": " + total + " zombies rolled - capped at " + cap);
        }

        /// <summary>
        /// Can this room bring the special: counted for clearing, not a trigger room (it may never fire), with spawn points,
        /// and one the prefab fills at all (a 0,0 room - a hall, a loot room - stays empty)?
        /// </summary>
        private static bool Eligible(SleeperVolume u)
        {
            return u != null && !u.isQuestExclude && !IsTriggerRoom(u) && u.spawnCountMax > 0 && u.spawnPointList != null && u.spawnPointList.Count > 0;
        }

        /// <summary>Has this room still to roll afresh: never spawned, or cleared and due?</summary>
        private static bool Unspawned(SleeperVolume u, ulong now)
        {
            if (u.isSpawning || u.isSpawned || u.respawnMap.Count > 0) return false;
            if (u.wasCleared) return now >= u.respawnTime;
            return u.numSpawned == 0;
        }

        /// <summary>
        /// The special (Q14 (b)) goes to one room, picked lazily as the rooms start: an eligible room starting a fresh roll
        /// brings it with probability 1 / (eligible generated rooms of the POI still to roll, this one counted), so the last
        /// one is certain. The host gets AddSpawnCount(group, 1, 1) after its own roll: a radiated zombie below
        /// InfestSpecialRadiatedBelowGS on the room's gamestage (or when the sandbox stops below elite), else charged or
        /// infernal. Pending until it spawns (OnSpawned); owed again if the room ends without it (EndSpawn).
        /// </summary>
        private static void MaybeHost(SleeperVolume v, PrefabInstance pi, Infestation inf, World w)
        {
            if (!ScConfig.InfestSpecial || EntityFactory.MaxEntityTier < EntityClass.EntityTierTypes.Radiated) return;
            SleeperVolume[] rooms = Rooms(pi);
            if (inf.Special == SpecialState.Pending)
            {
                // another room is bringing it (still spawning); a host reset or re-rolled since lost it
                if (inf.SpecialHost != v.BoxMin)
                    foreach (SleeperVolume u in rooms)
                        if (u != null && u.BoxMin == inf.SpecialHost && u.isSpawning) return;
                inf.Special = SpecialState.Owed;
                inf.SpecialHost = Vector3i.zero;
            }
            if (inf.Special != SpecialState.Owed || !Eligible(v) || !v.isSpawning) return;
            if (Rolled(v) + 1 >= byte.MaxValue) return;     // only a prefab's own huge room: the world saves its counts as bytes
            ulong now = w.worldTime;
            int left = 1;
            foreach (SleeperVolume u in rooms) if (u != null && u != v && Eligible(u) && Unspawned(u, now)) left++;
            GameRandom rnd = w.GetGameRandom();
            if (left > 1 && rnd.RandomRange(left) != 0) return;
            bool radiated = v.gameStage < ScConfig.InfestSpecialRadiatedBelowGS || EntityFactory.MaxEntityTier < EntityClass.EntityTierTypes.Elite;
            string group = GameStageGroup.CleanName(radiated ? RadiatedGroup : (rnd.RandomFloat < 0.5f ? ChargedGroup : InfernalGroup));
            if (GameStageGroup.TryGet(group) == null)
            {
                ScLog.Once("infest-nogroup-" + group, "the gamestage group " + group + " is missing (gamestages.xml) - infestations bring no special from it");
                return;
            }
            v.AddSpawnCount(group, 1f, 1f);
            inf.Special = SpecialState.Pending;
            inf.SpecialHost = v.BoxMin;
            ScLog.Out("infestation #" + inf.Id + " at " + inf.PoiName + ": the room at " + v.BoxMin + " brings the special (" + group + ", gamestage " + v.gameStage
                      + ", 1 in " + left + ")");
            dirty = true;
        }

        /// <summary>UpdateSpawn prefix: scopes the step to an infested room (the feral share, the special's check). Cheap with none.</summary>
        public static void BeginSpawn(SleeperVolume v)
        {
            if (v == null || keys.Length == 0) return;
            Infestation inf = At(v.PrefabInstance);
            if (inf == null) return;
            current = inf;
            CurrentVolume = v;
        }

        /// <summary>UpdateSpawn finalizer: the scope cleared; a host room done spawning without its special owes it to the next fresh room.</summary>
        public static void EndSpawn(SleeperVolume v)
        {
            Infestation inf = current;
            SleeperVolume cv = CurrentVolume;
            CurrentVolume = null;
            current = null;
            if (inf == null || cv != v || v.isSpawning) return;
            if (inf.Special != SpecialState.Pending || inf.SpecialHost != v.BoxMin) return;
            // a spawn block's own group took its turn, the enemy cap cut the room short, or the class could not stand there
            inf.Special = SpecialState.Owed;
            inf.SpecialHost = Vector3i.zero;
            ScLog.Out("infestation #" + inf.Id + " at " + inf.PoiName + ": the room at " + v.BoxMin + " finished without its special - the next fresh room owes it");
            dirty = true;
        }

        /// <summary>Spawn postfix (a spawn started): a radiated, charged or infernal class in the host room meets the guarantee.</summary>
        public static void OnSpawned(SleeperVolume v, int classId)
        {
            Infestation inf = current;
            if (inf == null || CurrentVolume != v || inf.Special != SpecialState.Pending || inf.SpecialHost != v.BoxMin) return;
            EntityClass ec;
            if (!EntityClass.list.TryGetValue(classId, out ec) || ec == null) return;
            if (!ec.Tags.Test_AnySet(EntityClass.radiatedTag) && !ec.Tags.Test_AnySet(EntityClass.eliteTag)) return;
            inf.Special = SpecialState.Met;
            inf.SpecialClass = ec.entityClassName ?? "";
            inf.SpecialHost = Vector3i.zero;
            ScLog.Out("infestation #" + inf.Id + " at " + inf.PoiName + ": its special came - " + inf.SpecialClass + " in the room at " + v.BoxMin);
            dirty = true;
        }

        /// <summary>ClearedUpdate postfix: a room of an infested POI just cleared - is the whole POI?</summary>
        public static void OnRoomCleared(SleeperVolume v, World w)
        {
            PrefabInstance pi = v.PrefabInstance;
            Infestation inf = At(pi);
            if (inf == null) return;
            Evaluate(inf, pi, w, ScCommunity.LocalPlayer);
        }

        /// <summary>
        /// The feral share (inside ScLoadout's GetRandomFromGroupList postfix, after its CurrentVolume check): in an infested
        /// room's spawn step, a Normal-tier zombie class comes as its "&lt;name&gt;Feral" InfestFeralShare of the time, when
        /// that class exists. Named, not PreviousTier (infestations.md 4b). A restore keeps the swapped class: the room
        /// records the class it spawned.
        /// </summary>
        public static void FeralShare(ref int classId, GameRandom random)
        {
            if (current == null || random == null || ScConfig.InfestFeralShare <= 0f || !ThreadManager.IsMainThread()) return;
            int feral = FeralOf(classId);
            if (feral == 0 || random.RandomFloat >= ScConfig.InfestFeralShare) return;
            classId = feral;
        }

        private static int FeralOf(int classId)
        {
            int f;
            if (ferals.TryGetValue(classId, out f)) return f;
            f = 0;
            EntityClass ec;
            if (EntityClass.list.TryGetValue(classId, out ec) && ec != null && ec.EntityTier == EntityClass.EntityTierTypes.Normal
                && ec.Tags.Test_AnySet(zombieTag) && !string.IsNullOrEmpty(ec.entityClassName))
            {
                int id = EntityClass.FromString(ec.entityClassName + "Feral");
                EntityClass fc;
                if (EntityClass.list.TryGetValue(id, out fc) && fc != null && fc.EntityTier == EntityClass.EntityTierTypes.Feral) f = id;
            }
            ferals[classId] = f;
            return f;
        }

        // ------------------------------------------------------------------ world start, the day, the tick ----

        /// <summary>
        /// GameStartDone (host, after the enclaves): every infestation's POI is found again by (x, z) and prefab. One whose
        /// POI is gone is dropped (not worth moving), as is one on an enclave's POI (a hand-edited registry) or a second one on
        /// the same POI. Nothing is seeded. Lookups rebuilt; one log line.
        /// </summary>
        public static void OnGameStart()
        {
            try
            {
                if (!ScCommunity.IsHost || !ScRegistry.Loaded) return;
                World w = ScCommunity.World;
                if (w == null) return;
                if (!ScConfig.Infestations)
                {
                    Rebuild();
                    if (All.Count > 0) ScLog.Out("infestations: off (Infestations=false) - " + All.Count + " kept in the registry, their rooms spawn as vanilla's and no marker shows");
                    return;
                }
                if (!ScPoiIndex.EnsureBuilt())
                {
                    // never drop anything on a world whose POI list did not come up
                    Rebuild();
                    ScLog.Warn("infestations: no POI list on this world - nothing checked this session (" + All.Count + " kept)");
                    return;
                }
                int loaded = All.Count, dropped = 0;
                var seen = new HashSet<Vector3i>();
                foreach (Infestation inf in new List<Infestation>(All))
                {
                    PoiEntry pe = ScPoiIndex.Find(inf.Poi.x, inf.Poi.z, inf.Prefab);
                    string gone = null;
                    if (pe == null) gone = "its POI " + inf.Prefab + " at " + inf.Poi + " is gone";
                    else if (ScEnclaves.IsEnclavePoiKey(pe.Key)) gone = "its POI is an enclave's";
                    else if (!seen.Add(pe.Key)) gone = "its POI already has an infestation";
                    if (gone != null)
                    {
                        RemoveMarker(inf);
                        All.Remove(inf);
                        dropped++;
                        ScLog.Warn("infestation #" + inf.Id + " at " + inf.PoiName + ": " + gone + " - dropped");
                        continue;
                    }
                    if (pe.Key != inf.Poi) ScLog.Out("infestation #" + inf.Id + " at " + inf.PoiName + ": its POI's key moved from " + inf.Poi + " to " + pe.Key + " (a POI pack update?)");
                    inf.Poi = pe.Key;
                    inf.Size = pe.Size;
                    inf.PoiName = pe.Display;
                    inf.Tier = pe.Tier;
                    DueAgain(inf, pe.Pi, w);     // its re-armed rooms are due, whatever the world was saved with
                }
                Rebuild();
                markersRestored = false;
                int known = 0;
                foreach (Infestation inf in All) if (inf.Known) known++;
                ScLog.Out("infestations: " + loaded + " loaded, " + dropped + " dropped - " + All.Count + " now (" + known + " known), at most " + TargetCap(w) + " on this world");
                if (dropped > 0) ScRegistry.SaveAtomic();
            }
            catch (Exception e)
            {
                ScLog.Once("infest-gamestart", e);
            }
            finally
            {
                ready = true;
            }
        }

        /// <summary>The day pass's random: the world seed and the day, with the clock mixed in so a repeated "sc day" rolls anew.</summary>
        private static int SeedFor(World w, int day)
        {
            return w.Seed ^ ("sc.infest:" + day.ToString(CultureInfo.InvariantCulture)).GetStableHashCode() ^ Environment.TickCount;
        }

        /// <summary>
        /// ScDaily.Run, after the enclaves, once a frame (the ticker's catch-up runs ScDaily once per missed day in one frame):
        /// 1. the poll for cleared POIs (their rooms are in memory, loaded or not); 2. each infestation InfestSpreadDays old
        /// rolls InfestSpreadChance to spread; 3. InfestDailyChance for a new one. Nothing starts or spreads at the cap.
        /// ScDaily saves.
        /// </summary>
        public static void Daily(int day, EntityPlayerLocal player)
        {
            try
            {
                if (!ready || !ScConfig.Infestations || Time.frameCount == passFrame) return;
                World w = ScCommunity.World;
                if (w == null || !ScPoiIndex.EnsureBuilt()) return;
                passFrame = Time.frameCount;
                foreach (Infestation inf in new List<Infestation>(All)) Evaluate(inf, PoiOf(inf), w, player);
                int cap = TargetCap(w);
                Rules r = RulesFor(w);
                GameRandom rnd = GameRandomManager.Instance.CreateGameRandom(SeedFor(w, day));
                try
                {
                    foreach (Infestation src in new List<Infestation>(All))
                    {
                        if (All.Count >= cap) break;
                        if (day - src.StartDay < ScConfig.InfestSpreadDays || src.LastSpreadDay >= day) continue;
                        if (rnd.RandomFloat >= ScConfig.InfestSpreadChance) continue;
                        string why;
                        if (Spread(src, w, r, day, player, out why) == null) ScLog.Verbose("infestation #" + src.Id + " at " + src.PoiName + " rolled to spread: " + why);
                    }
                    if (All.Count < cap && rnd.RandomFloat < ScConfig.InfestDailyChance)
                    {
                        int candidates;
                        PoiEntry pe = PickNew(day, r, rnd, out candidates);
                        string rooms;
                        if (pe != null) Infest(pe, w, day, null, "roll", player, out rooms);
                        else ScLog.Out("infestations: day " + day + " rolled a new one, but no POI fits (tiers and the filter)");
                    }
                }
                finally
                {
                    GameRandomManager.Instance.FreeGameRandom(rnd);
                }
            }
            catch (Exception e)
            {
                ScLog.Once("infest-daily", e);
            }
        }

        /// <summary>
        /// ScTicker, every second: a change the spawn hooks made is saved; markers back after a load; an unknown infestation
        /// within InfestDiscoverRange of its POI is found.
        /// </summary>
        public static void Tick(World w, EntityPlayerLocal player)
        {
            try
            {
                if (dirty)
                {
                    dirty = false;
                    ScRegistry.SaveAtomic();
                }
                if (!ready || w == null || player == null || !ScConfig.Infestations || All.Count == 0) return;
                if (!markersRestored)
                {
                    markersRestored = true;
                    RefreshMarkers(player);
                }
                Vector3 at = player.position;
                foreach (Infestation inf in All)
                {
                    if (inf.Known || inf.DistanceXZ(at.x, at.z) > ScConfig.InfestDiscoverRange) continue;
                    inf.Known = true;
                    inf.KnownDay = ScCommunity.Today;
                    AddMarker(inf, player);
                    ScCommunity.Tell(player, string.Format(Localization.Get("scInfestFound"), inf.PoiName), "ui_denied");
                    ScOutposts.NoteLedger(string.Format(Localization.Get("scInfestLedgerFound"), inf.PoiName));
                    ScLog.Out("infestation #" + inf.Id + " found at " + inf.PoiName);
                    ScRegistry.SaveAtomic();
                }
            }
            catch (Exception e)
            {
                ScLog.Once("infest-tick", e);
            }
        }

        // ------------------------------------------------------------------ markers ----

        /// <summary>The marker's spot: the POI's centre on the ground (the static heightmap works with the chunk unloaded).</summary>
        private static Vector3 MarkerSpot(Infestation inf)
        {
            Vector3 c = inf.Centre;
            World w = ScCommunity.World;
            float y = w != null ? w.GetHeightAt(c.x, c.z) : 0f;
            if (y > 0f) c.y = y;
            return c;
        }

        private static void AddMarker(Infestation inf, EntityPlayer owner)
        {
            try
            {
                if (!inf.Known || !ScConfig.Infestations) return;
                NavObjectManager nav = NavObjectManager.Instance;
                if (nav == null || owner == null || NavObjectClass.GetNavObjectClass(NavClass) == null) return;
                RemoveMarker(inf);
                Vector3 at = MarkerSpot(inf);
                nav.UnRegisterNavObjectByPosition(at, NavClass);
                NavObject obj = nav.RegisterNavObject(NavClass, at, "", false, -1, owner);
                if (obj != null) obj.name = string.Format(Localization.Get("scInfestMarker"), inf.PoiName);
                markers[inf.Id] = at;
            }
            catch (Exception ex)
            {
                ScLog.Once("infest-marker", ex);
            }
        }

        private static void RemoveMarker(Infestation inf)
        {
            try
            {
                Vector3 at;
                if (!markers.TryGetValue(inf.Id, out at)) return;
                markers.Remove(inf.Id);
                NavObjectManager nav = NavObjectManager.Instance;
                if (nav != null) nav.UnRegisterNavObjectByPosition(at, NavClass);
            }
            catch (Exception ex)
            {
                ScLog.Once("infest-unmarker", ex);
            }
        }

        /// <summary>Re-registers the marker of every known infestation (and drops the rest).</summary>
        public static void RefreshMarkers(EntityPlayer owner)
        {
            if (owner == null) return;
            foreach (Infestation inf in All)
            {
                if (inf.Known && ScConfig.Infestations) AddMarker(inf, owner);
                else RemoveMarker(inf);
            }
        }

        // ------------------------------------------------------------------ console ----

        private static string Line(Infestation inf, Vector3? from)
        {
            int age = Math.Max(0, ScCommunity.Today - inf.StartDay);
            return "#" + inf.Id + " " + inf.PoiName + " (" + inf.Prefab + ") T" + inf.Tier + " - since day " + inf.StartDay + " (" + age + " day(s), " + inf.How
                   + (inf.Source > 0 ? " from #" + inf.Source : "") + ")" + (inf.Known ? " - known" : " - not found") + " - special " + SpecialWord(inf)
                   + (from.HasValue ? " - " + ScEnclaves.Where(from.Value, inf.Centre) : "");
        }

        /// <summary>sc infest [list]</summary>
        public static void ListLines(List<string> into)
        {
            World w = ScCommunity.World;
            EntityPlayerLocal pl = ScCommunity.LocalPlayer;
            into.Add(All.Count + " infestation(s), at most " + TargetCap(w) + " on this world" + (ScConfig.Infestations ? "" : " - Infestations is OFF in the config"));
            foreach (Infestation inf in All) into.Add("  " + Line(inf, pl != null ? pl.position : (Vector3?)null));
        }

        /// <summary>sc infest info &lt;id&gt;</summary>
        public static void InfoLines(Infestation inf, List<string> into)
        {
            EntityPlayerLocal pl = ScCommunity.LocalPlayer;
            World w = ScCommunity.World;
            int[] b = inf.Box;
            Vector3 cen = inf.Centre;
            into.Add(Line(inf, pl != null ? pl.position : (Vector3?)null) + (inf.Known ? " (known since day " + inf.KnownDay + ")" : "")
                     + (inf.LastSpreadDay >= 0 ? ", last spread day " + inf.LastSpreadDay : ""));
            into.Add("  key " + inf.Poi + ", footprint x " + b[0] + ".." + b[1] + " z " + b[2] + ".." + b[3] + " (" + inf.Size.x + "x" + inf.Size.z + "), centre "
                     + Mathf.RoundToInt(cen.x) + "," + Mathf.RoundToInt(cen.z));
            PrefabInstance pi = PoiOf(inf);
            if (pi == null || w == null) { into.Add("  its POI is not in this world (the next load drops it)"); return; }
            int defined = 0, excluded = 0;
            var pl2 = pi.prefab != null && pi.prefab.SleeperVolumeList != null ? pi.prefab.SleeperVolumeList.List : null;
            if (pl2 != null) foreach (var psv in pl2) { if (psv == null) continue; defined++; if (psv.isQuestExclude) excluded++; }
            int made = 0, triggers = 0, cleared = 0, alive = 0, due = 0, spawning = 0, unfought = 0;
            ulong now = w.worldTime;
            foreach (SleeperVolume v in Rooms(pi))
            {
                if (v == null) continue;
                made++;
                if (IsTriggerRoom(v)) triggers++;
                if (!v.isQuestExclude && !inf.Rearmed.Contains(v.BoxMin) && Unfought(v)) unfought++;     // read only: Evaluate makes them due
                if (v.wasCleared) cleared++;
                if (v.wasCleared && now >= v.respawnTime) due++;
                if (v.isSpawning) spawning++;
                alive += v.respawnMap.Count;
            }
            into.Add("  sleeper areas: " + defined + " in the prefab (" + excluded + " quest-exclude), " + made + " generated (" + triggers + " trigger), " + cleared
                     + " cleared (" + due + " due to respawn), " + spawning + " spawning, " + alive + " sleeper(s) still tracked; " + inf.Rearmed.Count
                     + " room(s) cleared before it began still to roll again; " + unfought + " closed with nobody spawned (the enemy cap)");
            string why;
            bool done = Cleared(w, pi, out why);
            if (done && unfought > 0) { done = false; why = unfought + " room(s) closed with nobody spawned (the enemy cap) - due again, they must be fought"; }
            if (done && inf.Rearmed.Count > 0) { done = false; why = "a room cleared before it began has not rolled again"; }
            QuestClass q = pi.LastQuestClass;
            into.Add("  cleared: " + (done ? "yes (it ends at the next check)" : "no - " + why) + " | this session: multiplier "
                     + (q != null ? q.SpawnMultiplier.ToString("0.##", CultureInfo.InvariantCulture) + (q == carrier || q == carrierHigh ? " (ours)" : " (a quest's)") : "not applied yet")
                     + ", infested spawn points " + (pi.LastRefreshType.Test_AnySet(QuestEventManager.infestedTag) ? "open" : "not opened yet"));
        }

        /// <summary>sc infest add [x z]: the POI you stand in (or at x z) infested now - no roll, no distance or tier rules, the console's filter; known at once.</summary>
        public static string ConsoleAdd(bool at, int x, int z, EntityPlayerLocal player)
        {
            World w = ScCommunity.World;
            if (w == null || !ScCommunity.IsHost) return "host only, with a world";
            if (!ScConfig.Infestations) return "Infestations is off in the config";
            if (!at && player == null) return "no local player: give x z";
            if (!ScPoiIndex.EnsureBuilt()) return "no POI list on this world";
            Vector3i pos = at ? new Vector3i(x, 0, z) : player.GetBlockPosition();
            PrefabInstance pi = ScSecurity.PoiAt(pos);
            if (pi == null) return at ? "no POI at " + x + " " + z : "you are not standing in a POI (sc infest add <x> <z>)";
            PoiEntry pe = pi.prefab != null ? ScPoiIndex.Find(pi.boundingBoxPosition.x, pi.boundingBoxPosition.z, pi.prefab.PrefabName) : null;
            if (pe == null) return ScSecurity.DisplayName(pi) + " is not a real POI (sc poi)";
            Rules r = RulesFor(w);
            string why = Refuse(pe, r, true);
            if (why != null) return "refused: " + pe.Display + " - " + why;
            string rooms;
            Infestation inf = Infest(pe, w, ScCommunity.Today, null, "console", player, out rooms);
            int cap = TargetCap(w);
            return "#" + inf.Id + " " + inf.PoiName + " (T" + inf.Tier + ") infested and known: " + rooms + "; special " + SpecialWord(inf)
                   + (All.Count > cap ? " - over the cap of " + cap + ": the roll and spread wait" : "");
        }

        /// <summary>sc infest clear &lt;id|all&gt;: ends them - records and markers only, the rooms are left as they are. Returns how many.</summary>
        public static int ConsoleClear(Infestation only, EntityPlayerLocal player)
        {
            int n = 0;
            foreach (Infestation inf in new List<Infestation>(All))
            {
                if (only != null && inf != only) continue;
                End(inf, "console", player);
                n++;
            }
            return n;
        }

        /// <summary>sc infest spread &lt;id&gt;: spreads now (no age, chance or once-a-day rule; the cap holds).</summary>
        public static string ConsoleSpread(Infestation src, EntityPlayerLocal player)
        {
            World w = ScCommunity.World;
            if (w == null || !ScCommunity.IsHost) return "host only, with a world";
            if (!ScConfig.Infestations) return "Infestations is off in the config";
            if (!ScPoiIndex.EnsureBuilt()) return "no POI list on this world";
            int cap = TargetCap(w);
            if (All.Count >= cap) return "at the cap (" + All.Count + " of " + cap + "): nothing spreads";
            string why;
            Infestation to = Spread(src, w, RulesFor(w), ScCommunity.Today, player, out why);
            return to != null ? "#" + src.Id + " spread to #" + to.Id + " " + to.PoiName + " (T" + to.Tier + ")" + (to.Known ? ", known" : ", not known") : "no spread: " + why;
        }

        /// <summary>sc infest roll: the daily roll now - no chance, but the cap and every rule of the roll.</summary>
        public static string ConsoleRoll(EntityPlayerLocal player)
        {
            World w = ScCommunity.World;
            if (w == null || !ScCommunity.IsHost) return "host only, with a world";
            if (!ScConfig.Infestations) return "Infestations is off in the config";
            if (!ScPoiIndex.EnsureBuilt()) return "no POI list on this world";
            int cap = TargetCap(w), day = ScCommunity.Today;
            if (All.Count >= cap) return "at the cap (" + All.Count + " of " + cap + "): nothing new starts";
            Rules r = RulesFor(w);
            GameRandom rnd = GameRandomManager.Instance.CreateGameRandom(SeedFor(w, day));
            try
            {
                int candidates, tmin, tmax;
                ScConfig.InfestTierWindow(day, out tmin, out tmax);
                PoiEntry pe = PickNew(day, r, rnd, out candidates);
                if (pe == null) return "no POI fits (tiers " + tmin + "-" + tmax + " on day " + day + ", and the filter)";
                string rooms;
                Infestation inf = Infest(pe, w, day, null, "roll", player, out rooms);
                return "#" + inf.Id + " " + inf.PoiName + " (T" + inf.Tier + ") infested, 1 of " + candidates + " candidate(s) (tiers " + tmin + "-" + tmax + "): " + rooms
                       + (inf.Known ? "; the radio reported it" : "; not known (no powered radio, or beyond " + ScConfig.InfestRadioRange + " m)");
            }
            finally
            {
                GameRandomManager.Instance.FreeGameRandom(rnd);
            }
        }

        /// <summary>sc infest reveal|hide &lt;id|all&gt;: known on / off, with the marker. Returns how many changed.</summary>
        public static int SetKnown(Infestation only, bool known, EntityPlayer owner)
        {
            int n = 0;
            foreach (Infestation inf in All)
            {
                if (only != null && inf != only) continue;
                if (inf.Known == known) continue;
                inf.Known = known;
                inf.KnownDay = known ? ScCommunity.Today : -1;
                if (known) AddMarker(inf, owner); else RemoveMarker(inf);
                n++;
            }
            if (n > 0) ScRegistry.SaveAtomic();
            return n;
        }

        /// <summary>sc poi's line for an infested POI (null when it is not).</summary>
        public static string PoiLine(PrefabInstance pi)
        {
            Infestation inf = pi != null ? ByPoi(pi.boundingBoxPosition) : null;
            if (inf == null) return null;
            return "infestation #" + inf.Id + " since day " + inf.StartDay + (inf.Known ? "" : ", not found yet") + ", special " + SpecialWord(inf)
                   + (ScConfig.Infestations ? "" : " (Infestations is off)") + " (sc infest info " + inf.Id + ")";
        }
    }
}
