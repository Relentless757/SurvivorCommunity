using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round rt1 (I5, design 2.2 and 2.3): where a hurt member shelters and where she backs off to.
    ///
    /// The cache. A list of candidate floor cells inside the home zone, each with its roof (any non-air block 2 to 12 cells
    /// over it, the rule ScEnclavePeople used for an enclave leader, now shared here) and its walls (of the 8 horizontal
    /// directions at head height, how many meet a block that stops movement, or a door, within WallReach cells). Seeds: the
    /// cells round every bed and work station ScScan counted (I6; beds are nearly always indoors), every member's post, the
    /// ring round the claim block, and RandomSeeds random zone floors (so a base with no beds still gets rooms). Built on the
    /// host when the home is loaded, every ShelterRescanSeconds and when the zone changes, PerFrame seeds a frame (ScTicker's
    /// per-frame hook: about 70 block reads a seed), loaded chunks only - a candidate in a chunk that is not loaded keeps its
    /// last result. Picks read the last finished scan; a scan in progress never disturbs them.
    ///
    /// The pick (the shelter task's Start, C# - never from a consideration): her own cell when it passes and the threat
    /// cannot see her; else the best valid candidate - roofed, at least ShelterWalls walls, inside the zone, no enemy (awake or
    /// asleep) within SpotEnemyClear, at least ShelterMinThreatDist from the threat and a walk there that keeps PassClear from
    /// it, not one she failed to reach, not another's spot - scored 3 x (8 - walls) + walk - 0.5 x min(threat distance, 30)
    /// - 2 for company within 6 m; then the fallback chain of decision 9: (a) a walled spot with ShelterWalls - 1 walls (+20
    /// with no roof), (b) the zone floor farthest from the threat within FallbackRadius of her, near another member or you,
    /// (c) hold where she stands and fight back on reach.
    ///
    /// Round MP6 (part H): one cache per community, each scanned in its own scope and picked from for its own members.
    /// </summary>
    public static class ScShelter
    {
        public const byte SrcBed = 1, SrcStation = 2, SrcPost = 3, SrcClaim = 4, SrcRandom = 5;
        private static readonly string[] SrcNames = { "-", "beside a bed", "beside a work station", "a member's post", "by the claim block", "a zone floor" };

        public enum PickKind { None, Here, Room, Walled, Yard, Hold }

        public struct PickResult
        {
            public PickKind Kind;
            public Vector3 Spot;
            public string Source;
            public int Walls;
            public bool Roofed;
            public string Note;
        }

        private struct Cand
        {
            public Vector3i Cell;
            public bool Roofed;
            public byte Walls;
            public byte Source;
            /// <summary>The Fable review (finding 7): a door within DoorClear of it (NearDoor).</summary>
            public bool NearDoor;
            /// <summary>
            /// Round hr1: the enclosable room it stands in (this scan's room id, 1 and up - RoomInfo), -1 none (the fill with doors as
            /// walls leaked: open sky, too big, outside the base), 0 not checked yet (or kept from an unloaded chunk).
            /// </summary>
            public int Room;
        }

        /// <summary>
        /// Round hr1: one enclosable room of a scan - ScSafeRoom's fill from a candidate with every door and hatch as a wall stayed
        /// inside MaxCells cells, the base and under a roof - its cell count and its doors (parent cells; their live state is read
        /// at a pick: shut, the room is enclosed now).
        /// </summary>
        private sealed class RoomInfo
        {
            public int Id, Cells;
            public List<Vector3i> Doors;
        }

        private struct Seed
        {
            public int X, Y, Z;
            public byte Source;
        }

        /// <summary>Seeds evaluated a frame (about 70 block reads each).</summary>
        private const int PerFrame = 4;
        private const int RandomSeeds = 200;
        private const int MaxSeeds = 2000;
        private const int MaxCands = 800;
        /// <summary>A wall counts within this many cells in its direction.</summary>
        private const int WallReach = 6;
        /// <summary>A seed's floor is looked for within this many cells of its level.</summary>
        private const int SeedDy = 3;
        /// <summary>No enemy (awake or asleep) this close to a spot.</summary>
        public const float SpotEnemyClear = 8f;
        /// <summary>The straight walk to a spot keeps this far from the threat (ScRunners.PassDistance).</summary>
        public const float PassClear = 4f;
        /// <summary>A candidate farther than this walk (XZ) is not considered: she is hurt.</summary>
        public const float MaxWalk = 40f;
        /// <summary>Fallback (b): zone floors within this of her.</summary>
        public const float FallbackRadius = 25f;
        private const float CompanyRadius = 6f, ReserveRadius = 1.5f, AllyRadius = 8f;
        /// <summary>After this many spots she could not reach in one retreat, the cache is skipped (fallback b, then c).</summary>
        public const int MaxFails = 3;
        /// <summary>
        /// The Fable review (finding 7): a spot this close (XZ) to a door keeps that door open up to ScDoors' MaxHold (15 s: she
        /// is "beside it"), so such a room spot is taken only when no room spot without one is valid (DoorPenalty).
        /// </summary>
        private const float DoorClear = 2.5f;
        private const float DoorPenalty = 100f;
        /// <summary>Round hr1: an enclosable room's spot scores this much better (lower) in the shelter pick...</summary>
        private const float RoomBonus = 8f;
        /// <summary>...and this much worse for each of its doors standing open now beyond the one she shuts behind her.</summary>
        private const float OpenDoorPenalty = 3f;
        /// <summary>Round hr1: the scan's room pass fills at most this many rooms a frame (one fill: at most about 3,000 block reads).</summary>
        private const int RoomFillsPerFrame = 1;
        /// <summary>Round hr1: the room pass looks at most this many candidates a frame (a candidate inside a room already filled costs one lookup).</summary>
        private const int RoomLooksPerFrame = 200;

        /// <summary>
        /// Round MP6 (part H): one home's shelter cache - its candidates, the scan in progress and its clocks and figures. One per
        /// community (ScHomeState): each community's home has its own cache, scanned in its own scope (Step) and picked from for
        /// its own members (Pick).
        /// </summary>
        private sealed class ShelterState
        {
            public List<Cand> Cands = new List<Cand>();
            public List<Cand> Next = new List<Cand>();
            public readonly HashSet<Vector3i> NextCells = new HashSet<Vector3i>();
            public readonly Queue<Seed> Queue = new Queue<Seed>();
            public readonly HashSet<Vector3i> Queued = new HashSet<Vector3i>();
            public bool Scanning, Wanted, Force;
            public float NextScan, NextCheck, LastDone = -1f, Started, HomeSeenAt = -1f;
            public string ZoneSig;
            public int SeedsTotal, LastSeeds, Frames, LastFrames, Scans;
            public double ScanMs, LastMs;
            public readonly int[] BySource = new int[6];
            /// <summary>
            /// Round hr1: the enclosable rooms of the last finished scan and of the scan in progress, the scan's cell -> room map (-1:
            /// a cell a leaking fill passed - no second fill for a candidate in the same space), its room pass (after the seeds) and
            /// where it is, the next room id and the fills it made.
            /// </summary>
            public Dictionary<int, RoomInfo> Rooms = new Dictionary<int, RoomInfo>();
            public Dictionary<int, RoomInfo> NextRooms = new Dictionary<int, RoomInfo>();
            public readonly Dictionary<Vector3i, int> RoomOf = new Dictionary<Vector3i, int>();
            public bool RoomPass;
            public int RoomIndex, NextRoomId = 1, RoomFills, LastRoomFills;
            /// <summary>Round cb1: the scan's wall rays that passed furniture, a container or a wreck (obstacles, not walls).</summary>
            public int ObstacleRays;
        }
        private static readonly ScHomeState<ShelterState> homes = new ScHomeState<ShelterState>();

        // round MP6 (part H): the old statics, now the row of the community in scope (the per-pick lists below stay shared)
        private static List<Cand> cands { get { return homes.Current.Cands; } set { homes.Current.Cands = value; } }
        private static List<Cand> next { get { return homes.Current.Next; } set { homes.Current.Next = value; } }
        private static HashSet<Vector3i> nextCells { get { return homes.Current.NextCells; } }
        private static Queue<Seed> queue { get { return homes.Current.Queue; } }
        private static HashSet<Vector3i> queued { get { return homes.Current.Queued; } }
        private static readonly List<int[]> rects = new List<int[]>();
        private static readonly List<Entity> near = new List<Entity>();
        private static readonly List<Vector3> enemies = new List<Vector3>();
        private static readonly List<Vector3> others = new List<Vector3>();
        private static readonly List<Vector3> allies = new List<Vector3>();
        private static readonly Stopwatch sw = new Stopwatch();
        private static bool scanning { get { return homes.Current.Scanning; } set { homes.Current.Scanning = value; } }
        private static bool wanted { get { return homes.Current.Wanted; } set { homes.Current.Wanted = value; } }
        private static bool force { get { return homes.Current.Force; } set { homes.Current.Force = value; } }
        private static float nextScan { get { return homes.Current.NextScan; } set { homes.Current.NextScan = value; } }
        private static float nextCheck { get { return homes.Current.NextCheck; } set { homes.Current.NextCheck = value; } }
        private static float lastDone { get { return homes.Current.LastDone; } set { homes.Current.LastDone = value; } }
        private static float started { get { return homes.Current.Started; } set { homes.Current.Started = value; } }
        private static float homeSeenAt { get { return homes.Current.HomeSeenAt; } set { homes.Current.HomeSeenAt = value; } }
        /// <summary>The first scan of a session waits at most this long (real seconds) for ScScan's first count with the home loaded.</summary>
        private const float FirstWait = 20f;
        private static string zoneSig { get { return homes.Current.ZoneSig; } set { homes.Current.ZoneSig = value; } }
        private static int seedsTotal { get { return homes.Current.SeedsTotal; } set { homes.Current.SeedsTotal = value; } }
        private static int lastSeeds { get { return homes.Current.LastSeeds; } set { homes.Current.LastSeeds = value; } }
        private static int frames { get { return homes.Current.Frames; } set { homes.Current.Frames = value; } }
        private static int lastFrames { get { return homes.Current.LastFrames; } set { homes.Current.LastFrames = value; } }
        private static int scans { get { return homes.Current.Scans; } set { homes.Current.Scans = value; } }
        private static double scanMs { get { return homes.Current.ScanMs; } set { homes.Current.ScanMs = value; } }
        private static double lastMs { get { return homes.Current.LastMs; } set { homes.Current.LastMs = value; } }
        private static int[] bySource { get { return homes.Current.BySource; } }
        // round hr1: the room ids and door lists (one row per community, as above)
        private static Dictionary<int, RoomInfo> rooms { get { return homes.Current.Rooms; } set { homes.Current.Rooms = value; } }
        private static Dictionary<int, RoomInfo> nextRooms { get { return homes.Current.NextRooms; } set { homes.Current.NextRooms = value; } }
        private static Dictionary<Vector3i, int> roomOf { get { return homes.Current.RoomOf; } }
        private static readonly HashSet<Vector3i> roomCells = new HashSet<Vector3i>();
        private static readonly Dictionary<int, int> openDoorsOf = new Dictionary<int, int>();

        public static void Reset()
        {
            homes.Reset();                        // round MP6 (part H): every community's cache, scan and figures
        }

        /// <summary>A retreat began: scan now if the cache has nothing.</summary>
        public static void Want() { wanted = true; }

        /// <summary>sc retreat shelters rescan.</summary>
        public static void Rescan() { force = true; nextCheck = 0f; }

        // ------------------------------------------------------------------ the scan (every frame, host) ----

        /// <summary>
        /// ScTicker.OnGameUpdate, every frame before its 1 s gate: starts a scan when one is due (the zone changed, the
        /// rescan time, a retreat with an empty cache, or sc retreat shelters rescan) and the home is loaded, and evaluates
        /// PerFrame seeds of a running one. Nothing at all with RetreatHome off. Never throws.
        /// Round MP6 (part H): every community's cache in turn, each in its own scope (it runs before the ticker's scopes); with
        /// one community exactly as before.
        /// </summary>
        public static void Step()
        {
            if (ScCommunities.Count < 2 || !ScRegistry.Loaded) { StepOne(); return; }
            IReadOnlyList<CommunityState> all = ScCommunities.All;
            for (int i = 0; i < all.Count; i++)
                using (ScRegistry.Use(all[i].Id)) StepOne();
        }

        /// <summary>Round MP6 (part H): Step for the community in scope (the body Step always had).</summary>
        private static void StepOne()
        {
            try
            {
                if (!ScConfig.RetreatHome || !ScRegistry.Loaded || !ScCommunity.IsHost) return;
                World w = ScCommunity.World;
                if (w == null || !ScZone.HasHome) return;
                float now = Time.unscaledTime;
                if (!scanning)
                {
                    if (now < nextCheck) return;
                    nextCheck = now + 1f;                 // the zone's signature at most once a second
                    string sig = ZoneSig();
                    bool due = force || sig != zoneSig || now >= nextScan || (wanted && cands.Count == 0 && now - lastDone > 30f);
                    // the op1 re-check: a posting's body fetch loads a few home chunks with the player away - not a scan's view
                    bool homeUp = ScZone.HomeLoaded && !ScGarrison.Fetching;
                    if (!due || !homeUp) { if (!homeUp) homeSeenAt = -1f; return; }
                    // the first scan of a session waits for ScScan's first count of loaded chunks (the beds), at most FirstWait
                    if (homeSeenAt < 0f) homeSeenAt = now;
                    if (scans == 0 && !force && !wanted && ScScan.LastLoaded == 0 && now - homeSeenAt < FirstWait) return;
                    wanted = false;
                    Begin(w, sig, now);
                    return;
                }
                sw.Reset();
                sw.Start();
                for (int i = 0; i < PerFrame && queue.Count > 0; i++) Evaluate(w, queue.Dequeue());
                // round hr1: after the seeds, the room pass (a room id and door list for each candidate), then the scan is done
                bool done = queue.Count == 0 && RoomStep(w);
                sw.Stop();
                scanMs += sw.Elapsed.TotalMilliseconds;
                frames++;
                if (done) Finish(w, now);
            }
            catch (Exception e)
            {
                scanning = false;
                queue.Clear();
                nextScan = Time.unscaledTime + 60f;
                ScLog.Once("shelter-step", e);
            }
        }

        private static string ZoneSig()
        {
            ScZone.Rects(rects);
            var sb = new System.Text.StringBuilder();
            sb.Append(ScZone.Home.ToString());
            for (int i = 0; i < rects.Count; i++) sb.Append('|').Append(rects[i][0]).Append(',').Append(rects[i][1]).Append(',').Append(rects[i][2]).Append(',').Append(rects[i][3]);
            return sb.ToString();
        }

        private static void Begin(World w, string sig, float now)
        {
            zoneSig = sig;
            force = false;
            scanning = true;
            started = now;
            next = new List<Cand>();
            nextCells.Clear();
            queue.Clear();
            queued.Clear();
            seedsTotal = 0;
            frames = 0;
            scanMs = 0;
            // round hr1: the room pass starts afresh
            homes.Current.RoomPass = false;
            homes.Current.RoomIndex = 0;
            homes.Current.NextRoomId = 1;
            homes.Current.RoomFills = 0;
            homes.Current.ObstacleRays = 0;               // round cb1
            nextRooms = new Dictionary<int, RoomInfo>();
            roomOf.Clear();
            // 1. beds and work stations (ScScan's last counts): the 8 columns round each, at its level
            var beds = new List<Vector3i>();
            var stations = new List<Vector3i>();
            ScScan.SeedCells(beds, stations);
            for (int i = 0; i < beds.Count; i++) Ring(beds[i], 1, SrcBed);
            for (int i = 0; i < stations.Count; i++) Ring(stations[i], 1, SrcStation);
            // 2. every member's post, and the ring round the claim block
            foreach (Member m in ScRegistry.Members.Values)
                if (m.Post != Vector3i.zero) Enqueue(m.Post.x, m.Post.y, m.Post.z, SrcPost);
            Ring(ScZone.Home, 2, SrcClaim);
            // 3. random zone floors at the claim's level (a yard a step or two off it is found by the floor search)
            int y0 = ScZone.Home.y;
            for (int i = 0; i < RandomSeeds; i++)
            {
                Vector3 p;
                if (!ScZone.RandomPoint(out p)) break;
                Enqueue(Mathf.FloorToInt(p.x), y0, Mathf.FloorToInt(p.z), SrcRandom);
            }
        }

        private static void Ring(Vector3i c, int r, byte src)
        {
            for (int dx = -r; dx <= r; dx++)
                for (int dz = -r; dz <= r; dz++)
                    if (dx != 0 || dz != 0) Enqueue(c.x + dx, c.y, c.z + dz, src);
        }

        private static void Enqueue(int x, int y, int z, byte src)
        {
            if (seedsTotal >= MaxSeeds || !queued.Add(new Vector3i(x, y, z))) return;
            queue.Enqueue(new Seed { X = x, Y = y, Z = z, Source = src });
            seedsTotal++;
        }

        private static void Evaluate(World w, Seed s)
        {
            if (w.GetChunkSync(World.toChunkXZ(s.X), World.toChunkXZ(s.Z)) == null) return;   // not loaded: its old result is kept (Finish)
            int y;
            if (!ScCommunity.FindStandable(w, s.X, s.Z, s.Y, out y) || Math.Abs(y - s.Y) > SeedDy) return;
            Vector3i cell = new Vector3i(s.X, y, s.Z);
            if (nextCells.Contains(cell) || !InZone(cell)) return;
            nextCells.Add(cell);
            string why;
            if (!ScCommunity.GoodPost(w, cell, false, out why)) return;
            int obstacles;
            int walls = Walls(w, cell, out obstacles);
            homes.Current.ObstacleRays += obstacles;                                 // round cb1: the scan's log line
            if (walls < Math.Max(1, ScConfig.ShelterWalls - 1) || next.Count >= MaxCands) return;   // too open even for the fallback
            next.Add(new Cand { Cell = cell, Roofed = Roofed(w, cell), Walls = (byte)walls, Source = s.Source, NearDoor = NearDoor(w, cell) });   // round hr1: Room 0, the room pass fills it in
        }

        /// <summary>
        /// Round hr1: the scan's room pass, after its seeds - each candidate gets the enclosable room it stands in (ScSafeRoom's fill
        /// with every door and hatch as a wall: at most MaxCells cells, inside the base, no open sky), a candidate in a space a fill
        /// already passed gets that fill's answer without a fill of its own, one with no roof none at all. At most RoomFillsPerFrame
        /// fills and RoomLooksPerFrame candidates a frame. True once every candidate has its answer.
        /// </summary>
        private static bool RoomStep(World w)
        {
            ShelterState hs = homes.Current;
            hs.RoomPass = true;
            int fills = 0, looks = 0;
            while (hs.RoomIndex < next.Count && looks < RoomLooksPerFrame)
            {
                Cand c = next[hs.RoomIndex];
                looks++;
                if (c.Room == 0)
                {
                    int id;
                    if (!c.Roofed) c.Room = -1;                                  // nothing over it: open sky, no room
                    else if (roomOf.TryGetValue(c.Cell, out id)) c.Room = id;
                    else
                    {
                        if (fills >= RoomFillsPerFrame) break;                     // the next frame's
                        fills++;
                        c.Room = FillRoom(w, c.Cell);
                    }
                    next[hs.RoomIndex] = c;
                }
                hs.RoomIndex++;
            }
            return hs.RoomIndex >= next.Count;
        }

        /// <summary>Round hr1: one room fill from a candidate's cell (doors as walls); every cell it passed is mapped to its answer. The room id, or -1.</summary>
        private static int FillRoom(World w, Vector3i cell)
        {
            ShelterState hs = homes.Current;
            var doors = new List<Vector3i>();
            ScSafeRoom.FillResult f = ScSafeRoom.FillFrom(w, cell, true, roomCells, doors);
            hs.RoomFills++;
            int id = -1;
            if (f.Enclosed)
            {
                id = hs.NextRoomId++;
                nextRooms[id] = new RoomInfo { Id = id, Cells = f.Cells, Doors = doors };
            }
            foreach (Vector3i c in roomCells) roomOf[c] = id;
            roomCells.Clear();
            return id;
        }

        /// <summary>Round hr1: how many of this room's doors stand open now (a door's live state; cached for one pick, openDoorsOf).</summary>
        private static int OpenDoors(World w, int roomId)
        {
            int n;
            if (openDoorsOf.TryGetValue(roomId, out n)) return n;
            n = 0;
            RoomInfo ri;
            if (rooms.TryGetValue(roomId, out ri) && ri.Doors != null)
            {
                for (int i = 0; i < ri.Doors.Count; i++)
                {
                    BlockValue bv = w.GetBlock(ri.Doors[i]);
                    Block b = bv.isair ? null : bv.Block;
                    if (b != null && !b.IsMovementBlocked(w, ri.Doors[i], bv, BlockFace.None)) n++;
                }
            }
            openDoorsOf[roomId] = n;
            return n;
        }

        /// <summary>Round hr1: a candidate's room in a few words for a pick's note ("" with none).</summary>
        private static string RoomText(World w, Cand c)
        {
            RoomInfo ri;
            if (c.Room < 1 || !rooms.TryGetValue(c.Room, out ri)) return "";
            int open = OpenDoors(w, c.Room);
            return ", an enclosable room (" + ri.Cells + " cells, " + (ri.Doors != null ? ri.Doors.Count : 0) + " door(s)" + (open > 0 ? ", " + open + " open now" : ", all shut") + ")";
        }

        private static void Finish(World w, float now)
        {
            // a candidate whose chunk is not loaded now keeps its last result
            for (int i = 0; i < cands.Count && next.Count < MaxCands; i++)
            {
                Cand c = cands[i];
                if (!nextCells.Contains(c.Cell) && w.GetChunkSync(World.toChunkXZ(c.Cell.x), World.toChunkXZ(c.Cell.z)) == null) { c.Room = 0; next.Add(c); }   // round hr1: its old room id means nothing in this scan
            }
            cands = next;
            next = new List<Cand>();
            nextCells.Clear();
            queued.Clear();
            // round hr1: this scan's rooms (a candidate kept from an unloaded chunk had the last scan's room id: not checked again)
            rooms = nextRooms;
            nextRooms = new Dictionary<int, RoomInfo>();
            roomOf.Clear();
            homes.Current.LastRoomFills = homes.Current.RoomFills;
            homes.Current.RoomPass = false;
            scanning = false;
            lastDone = now;
            nextScan = now + ScConfig.ShelterRescanSeconds;
            lastMs = scanMs;
            lastSeeds = seedsTotal;
            lastFrames = frames;
            scans++;
            Array.Clear(bySource, 0, bySource.Length);
            int roomsN = 0;                                  // round hr1: renamed (rooms is the room table now)
            for (int i = 0; i < cands.Count; i++)
            {
                bySource[cands[i].Source]++;
                if (cands[i].Roofed && cands[i].Walls >= ScConfig.ShelterWalls) roomsN++;
            }
            ScLog.Verbose("shelter scan: " + cands.Count + " candidate(s), " + roomsN + " roofed with " + ScConfig.ShelterWalls + "+ walls, from " + lastSeeds + " seeds in "
                          + lastMs.ToString("0.0", CultureInfo.InvariantCulture) + " ms over " + lastFrames + " frames (" + (now - started).ToString("0.0", CultureInfo.InvariantCulture) + " s); "
                          + rooms.Count + " enclosable room(s) from " + homes.Current.LastRoomFills + " fill(s) (round hr1); "
                          + homes.Current.ObstacleRays + " wall ray(s) passed furniture, a container or a wreck - obstacles, not walls (round cb1)");
        }

        /// <summary>
        /// Inside the home zone, on ScZone's cached rectangles (the Fable review, finding 9: ScZone.Contains asks
        /// LandClaimUpgrade by reflection on every call). The cache rescans whenever the zone's signature changes.
        /// </summary>
        public static bool InZone(Vector3i c)
        {
            return ScZone.HasHome && ScZone.DistanceOutside(new Vector3(c.x + 0.5f, c.y, c.z + 0.5f)) <= 0f;
        }

        /// <summary>A door (a Door-tagged block, any leaf) within DoorClear (XZ) of the cell, at its feet or head.</summary>
        public static bool NearDoor(World w, Vector3i p)
        {
            for (int dx = -2; dx <= 2; dx++)
            {
                for (int dz = -2; dz <= 2; dz++)
                {
                    if (dx * dx + dz * dz > DoorClear * DoorClear) continue;
                    for (int dy = 0; dy <= 1; dy++)
                    {
                        BlockValue bv = w.GetBlock(new Vector3i(p.x + dx, p.y + dy, p.z + dz));
                        if (bv.isair) continue;
                        Block b = bv.Block;
                        if (b != null && b.HasTag(BlockTags.Door)) return true;
                    }
                }
            }
            return false;
        }

        /// <summary>Anything solid 2 to 12 cells over the spot: under a roof (the rule ScEnclavePeople's leader spot uses).</summary>
        public static bool Roofed(World w, Vector3i p)
        {
            for (int k = 2; k <= 12; k++) if (!w.GetBlock(new Vector3i(p.x, p.y + k, p.z)).isair) return true;
            return false;
        }

        private static readonly int[] DirX = { 1, 1, 0, -1, -1, -1, 0, 1 };
        private static readonly int[] DirZ = { 0, 1, 1, 1, 0, -1, -1, -1 };

        /// <summary>
        /// Of the 8 horizontal directions at head height (y + 1), how many meet a movement-stopping block or a door within WallReach
        /// cells. Block data only: an entity (a zombie, a drivable vehicle) never counts. Round cb1: furniture, a container, a car or
        /// truck wreck or a decoration model (ObstacleNotWall) is an obstacle, not a wall - the ray goes on past it to the real wall
        /// (a spot hemmed in by a truck and coffins is no shelter); fences, bars and posts still count (hr1's decision 3).
        /// </summary>
        public static int Walls(World w, Vector3i p)
        {
            int obstacles;
            return Walls(w, p, out obstacles);
        }

        /// <summary>Round cb1: Walls, and how many of its rays passed an obstacle that is not a wall (the scan's log line).</summary>
        public static int Walls(World w, Vector3i p, out int obstacles)
        {
            int n = 0;
            obstacles = 0;
            for (int d = 0; d < 8; d++)
            {
                bool passed = false;
                for (int k = 1; k <= WallReach; k++)
                {
                    BlockValue bv = w.GetBlock(new Vector3i(p.x + DirX[d] * k, p.y + 1, p.z + DirZ[d] * k));
                    if (bv.isair) continue;
                    Block b = bv.Block;
                    if (b == null) continue;
                    if (b.HasTag(BlockTags.Door)) { n++; break; }
                    if (ObstacleNotWall(bv, b)) { passed = true; continue; }        // round cb1
                    if (b.IsCollideMovement) { n++; break; }
                }
                if (passed) obstacles++;
            }
            return n;
        }

        /// <summary>
        /// Round cb1: a model block a body walks round rather than shelters behind - furniture, a loot container (a coffin, a
        /// cabinet), a multi-block or one of its cells, an oversized block (a car or truck wreck: its hull reads as air, its origin
        /// cell is the block) or a terrain decoration - never a door (counted before this). A plain one-cell model (a fence segment,
        /// a post) is not one: it still counts as a wall, as bars and glass do (hr1's decision 3). A read.
        /// </summary>
        internal static bool ObstacleNotWall(BlockValue bv, Block b)
        {
            if (b == null || !(b.shape is BlockShapeModelEntity)) return false;
            return bv.ischild || b.isMultiBlock || b.isOversized || b.IsTerrainDecoration || b is BlockCompositeTileEntity;
        }

        // ------------------------------------------------------------------ the pick (the shelter task, C#) ----

        /// <summary>
        /// The spot for this body's shelter now: her own cell, a room, or the fallback chain (see the class summary). bad and
        /// fails: the spots she could not reach this retreat. The console asks it too (nothing is written). Never None for a
        /// body at home: (c) holds where she stands.
        /// </summary>
        public static PickResult Pick(EntityAlive self, List<Vector3i> bad, int fails)
        {
            using (ScHomeScope.Of(self)) return PickIn(self, bad, fails);     // round MP6 (part H): her own community's cache and zone
        }

        /// <summary>Round MP6 (part H): Pick in the scope it set (the body Pick always had).</summary>
        private static PickResult PickIn(EntityAlive self, List<Vector3i> bad, int fails)
        {
            var r = new PickResult { Kind = PickKind.None };
            World w = self != null ? self.world : null;
            if (w == null) return r;
            Vector3 me = self.position;
            Vector3 threat;
            EntityAlive t;
            bool hasThreat = ScRetreat.ThreatPlace(self, out threat, out t);
            Gather(self, me, MaxWalk + SpotEnemyClear);
            // 1. her own cell, when it passes and the threat cannot see her (asleep in a bedroom when the zombie comes)
            Vector3i own = World.worldToBlockPos(me);
            // round cb1: never a hold on top of a model (a bed, a truck's bed) or inside a wreck's hull - real floor only
            string onWhat;
            bool onObstacle = ScBackOff.OnObstacle(w, me, out onWhat);
            // (finding 7: not beside a door - she would hold it open)
            if (!onObstacle && InZone(own) && Roofed(w, own) && Walls(w, own) >= ScConfig.ShelterWalls && !EnemyNear(me, SpotEnemyClear)
                && (!hasThreat || ScRunners.XZ(me, threat) >= ScConfig.ShelterMinThreatDist) && (t == null || !t.CanEntityBeSeen(self)) && !NearDoor(w, own))
            {
                r.Kind = PickKind.Here;
                r.Spot = me;
                r.Walls = Walls(w, own);
                r.Roofed = true;
                r.Source = "where " + ScRetreat.She(self) + " stood";
                r.Note = "holds where " + ScRetreat.She(self) + " stands (roofed, " + r.Walls + " of 8 walls, the threat cannot see " + ScRetreat.HerObj(self) + ")";
                return r;
            }
            // 2. a room; (a) a walled spot
            if (fails < MaxFails)
            {
                if (BestCand(self, w, me, threat, hasThreat, bad, true, ScConfig.ShelterWalls, ref r)) { r.Kind = PickKind.Room; return r; }
                if (BestCand(self, w, me, threat, hasThreat, bad, false, ScConfig.ShelterWalls - 1, ref r))
                {
                    r.Kind = PickKind.Walled;
                    r.Note = "fell back to a walled spot " + (r.Roofed ? "" : "without a roof ") + "(" + r.Walls + " of 8 walls, " + r.Source + ")";
                    return r;
                }
            }
            // (b) the zone floor farthest from the threat within FallbackRadius of her, near another member or you (not once
            // the yard has failed her as often as the rooms did: then she holds)
            if (fails < 2 * MaxFails && Yard(self, w, me, threat, hasThreat, bad, ref r)) return r;
            string she = ScRetreat.She(self);
            // round cb1: she stands on a model or in a wreck's hull (a truck's bed) - the nearest real floor within StepMax first, so
            // the hold below never pins her up there
            Vector3 down;
            string downWhy;
            if (onObstacle && ScBackOff.StepSpot(self, hasThreat ? t : null, false, ScBackOff.StepMax, bad, out down, out downWhy))
            {
                r.Kind = PickKind.Yard;
                r.Spot = down;
                r.Source = "the floor beside it";
                r.Note = "steps down to the floor at " + ScRetreat.Cell(down) + ", " + ScRunners.XZ(me, down).ToString("0.#", CultureInfo.InvariantCulture)
                         + " m off (" + she + " stood " + onWhat + "; " + downWhy + "; round cb1)";
                Patches_Xnpc.Count("shelter-step-off-model");
                return r;
            }
            // (c) hold where she stands and fight back on reach
            r.Kind = PickKind.Hold;
            r.Spot = me;
            r.Source = "where " + she + " stood";
            r.Note = "fell back to holding where " + she + " stands (no floor of the zone to go to" + (fails >= MaxFails ? ", " + fails + " spots " + she + " could not reach" : "")
                     + (onObstacle ? "; " + she + " stands " + onWhat + " and no real floor within " + ScBackOff.StepMax.ToString("0.#", CultureInfo.InvariantCulture) + " m is free (round cb1)" : "") + ") - "
                     + she + " fights back on reach";
            return r;
        }

        /// <summary>One sweep: the enemies (awake or asleep) round her, the others' spots, and the members and you (allies).</summary>
        private static void Gather(EntityAlive self, Vector3 me, float radius)
        {
            enemies.Clear();
            others.Clear();
            allies.Clear();
            World w = self.world;
            near.Clear();
            w.GetEntitiesInBounds(typeof(EntityAlive), new Bounds(me, new Vector3(2f * radius, 2f * radius, 2f * radius)), near);
            for (int i = 0; i < near.Count; i++)
            {
                EntityAlive a = near[i] as EntityAlive;
                if (a == null || a == self || a.IsDead()) continue;
                if (EntityTargetingUtilities.IsEnemy(self, a)) { if (!global::Harmony.ZombieFeatures.InertEntity.IsInert(a)) enemies.Add(a.position); continue; }
#if SC_V34
                if (ScDowned.IsDown(a)) continue;       // round U2: a downed member shelters nobody (no "beside another member" spot by her)
#endif
                if (a is EntityPlayer || ScRegistry.GetMember(a.entityId) != null) allies.Add(a.position);
            }
            near.Clear();
            ScRetreat.OtherSpots(self.entityId, others);
        }

        private static bool EnemyNear(Vector3 p, float r)
        {
            for (int i = 0; i < enemies.Count; i++) if ((enemies[i] - p).sqrMagnitude < r * r) return true;
            return false;
        }

        private static bool Near(List<Vector3> list, Vector3 p, float r)
        {
            for (int i = 0; i < list.Count; i++) if (ScRunners.XZ(list[i], p) < r && Mathf.Abs(list[i].y - p.y) < 3f) return true;
            return false;
        }

        private static bool BestCand(EntityAlive self, World w, Vector3 me, Vector3 threat, bool hasThreat, List<Vector3i> bad, bool needRoof, int minWalls, ref PickResult r)
        {
            var scored = new List<KeyValuePair<float, Cand>>();
            openDoorsOf.Clear();                                   // round hr1: the doors' live state, read once per room per pick
            for (int i = 0; i < cands.Count; i++)
            {
                Cand c = cands[i];
                if (c.Walls < minWalls || (needRoof && !c.Roofed)) continue;
                Vector3 p = new Vector3(c.Cell.x + 0.5f, c.Cell.y, c.Cell.z + 0.5f);
                float walk = ScRunners.XZ(me, p);
                if (walk > MaxWalk || !InZone(c.Cell)) continue;
                if (bad != null && bad.Contains(c.Cell)) continue;
                if (hasThreat && (ScRunners.XZ(threat, p) < ScConfig.ShelterMinThreatDist || ScRunners.PassDistance(me, p, threat) < PassClear)) continue;
                if (EnemyNear(p, SpotEnemyClear) || Near(others, p, ReserveRadius)) continue;
                float score = 3f * (8 - c.Walls) + (c.Roofed ? 0f : 20f) + walk - (hasThreat ? 0.5f * Mathf.Min(ScRunners.XZ(threat, p), 30f) : 0f)
                              - (Near(others, p, CompanyRadius) ? 2f : 0f) + (c.NearDoor ? DoorPenalty : 0f);
                // round hr1: an enclosable room first (its doors shut, it is a safe room once nothing is inside), the fewer of its doors
                // stand open now the better (she shuts the one she passes)
                if (c.Room >= 1) score += -RoomBonus + OpenDoorPenalty * Mathf.Max(0, OpenDoors(w, c.Room) - 1);
                scored.Add(new KeyValuePair<float, Cand>(score, c));
            }
            if (scored.Count == 0) return false;
            scored.Sort((a, b) => a.Key.CompareTo(b.Key));
            // block reads only for the best few: the floor still there, not on her unreachable list (ScStuck, the roam's)
            for (int i = 0; i < scored.Count && i < 8; i++)
            {
                Cand c = scored[i].Value;
                string why;
                if (ScStuck.Unreachable(self.entityId, c.Cell) || !ScCommunity.GoodPost(w, c.Cell, false, out why)) continue;
                r.Spot = new Vector3(c.Cell.x + 0.5f, c.Cell.y, c.Cell.z + 0.5f);
                r.Walls = c.Walls;
                r.Roofed = c.Roofed;
                r.Source = SrcNames[c.Source];
                r.Note = (c.Roofed ? "a room" : "a walled spot") + " " + SrcNames[c.Source] + " (" + c.Walls + " of 8 walls" + (c.Roofed ? ", roofed" : ", no roof")
                         + (c.NearDoor ? ", BESIDE A DOOR (no other was valid)" : "")
                         + (hasThreat ? ", " + ScRunners.XZ(threat, r.Spot).ToString("0", CultureInfo.InvariantCulture) + " m from the threat" : "")
                         + RoomText(w, c) + ")";                     // round hr1: its room
                return true;
            }
            return false;
        }

        /// <summary>
        /// Round hr1 (step 3 of the heal order, ScHealBackOff): the spot for a hurt member's walk INTO an enclosable room within
        /// maxWalk (XZ) - a candidate of the last scan in a room (Room 1 and up), at least DoorClear from every door (NearDoor: she
        /// would hold a door open beside her), inside the base, not one she failed to reach this wait (bad, within 1.2 m), no enemy
        /// within 4 m of it, not another's shelter spot, and a straight walk that never takes her nearer the threat than she is now
        /// (PassClear at most). Best: the shortest walk, more walls, fewer open doors, farther from the threat. False with why. The
        /// shelter task's spots and the shelter retreat are not touched. Main thread, a read.
        /// </summary>
        public static bool PickRoom(EntityAlive self, EntityAlive threat, float maxWalk, List<Vector3> bad, out Vector3 spot, out string note)
        {
            using (ScHomeScope.Of(self)) return PickRoomIn(self, threat, maxWalk, bad, out spot, out note);     // her own community's cache and zone
        }

        private static bool PickRoomIn(EntityAlive self, EntityAlive threat, float maxWalk, List<Vector3> bad, out Vector3 spot, out string note)
        {
            spot = Vector3.zero;
            note = null;
            World w = self != null ? self.world : null;
            if (w == null) { note = "no world"; return false; }
            if (scans == 0) { note = "the shelter scan has not run yet"; return false; }
            Vector3 me = self.position;
            Vector3 tp = threat != null ? threat.position : me;
            float meFromThreat = threat != null ? ScRunners.XZ(me, tp) : 0f;
            Gather(self, me, maxWalk + 6f);
            openDoorsOf.Clear();
            var scored = new List<KeyValuePair<float, Cand>>();
            int inRooms = 0;
            string lastWhy = "no candidate of the shelter scan stands in an enclosable room";
            for (int i = 0; i < cands.Count; i++)
            {
                Cand c = cands[i];
                if (c.Room < 1) continue;
                inRooms++;
                Vector3 p = new Vector3(c.Cell.x + 0.5f, c.Cell.y, c.Cell.z + 0.5f);
                float walk = ScRunners.XZ(me, p);
                if (walk > maxWalk) { lastWhy = "every room is farther than " + maxWalk.ToString("0", CultureInfo.InvariantCulture) + " m"; continue; }
                if (c.NearDoor) { lastWhy = "a spot within " + DoorClear.ToString("0.#", CultureInfo.InvariantCulture) + " m of a door"; continue; }
                if (!UAI.UAIConsiderationScNotHomeMember.AtHome(p)) { lastWhy = "outside the base"; continue; }
                if (bad != null && NearAny(bad, p, 1.2f)) { lastWhy = "a room " + ScRetreat.She(self) + " already tried"; continue; }
                if (threat != null && ScRunners.PassDistance(me, p, tp) < Mathf.Min(PassClear, meFromThreat - 0.25f)) { lastWhy = "the walk passes the threat"; continue; }
                if (EnemyNear(p, 4f)) { lastWhy = "an enemy within 4 m of it"; continue; }
                if (Near(others, p, ReserveRadius)) { lastWhy = "another member's spot"; continue; }
                float score = walk + 3f * (8 - c.Walls) + OpenDoorPenalty * Mathf.Max(0, OpenDoors(w, c.Room) - 1)
                              - (threat != null ? 0.3f * Mathf.Min(ScRunners.XZ(tp, p), 30f) : 0f);
                scored.Add(new KeyValuePair<float, Cand>(score, c));
            }
            if (scored.Count == 0) { note = inRooms == 0 ? lastWhy : lastWhy + " (" + inRooms + " spot(s) in rooms looked at)"; return false; }
            scored.Sort((a, b) => a.Key.CompareTo(b.Key));
            for (int i = 0; i < scored.Count && i < 8; i++)
            {
                Cand c = scored[i].Value;
                string why;
                if (ScStuck.Unreachable(self.entityId, c.Cell) || !ScCommunity.GoodPost(w, c.Cell, false, out why)) { lastWhy = "not standable now"; continue; }
                spot = new Vector3(c.Cell.x + 0.5f, c.Cell.y, c.Cell.z + 0.5f);
                note = "a room " + SrcNames[c.Source] + " at " + c.Cell.x + " " + c.Cell.y + " " + c.Cell.z + ", " + ScRunners.XZ(me, spot).ToString("0", CultureInfo.InvariantCulture) + " m off ("
                       + c.Walls + " of 8 walls" + RoomText(w, c) + (threat != null ? ", " + ScRunners.XZ(tp, spot).ToString("0", CultureInfo.InvariantCulture) + " m from the threat" : "") + ")";
                return true;
            }
            note = lastWhy;
            return false;
        }

        private static bool NearAny(List<Vector3> list, Vector3 p, float r)
        {
            for (int i = 0; i < list.Count; i++) if (ScRunners.XZ(list[i], p) <= r && Mathf.Abs(list[i].y - p.y) < 1.5f) return true;
            return false;
        }

        /// <summary>Fallback (b): 24 tries for a zone floor within FallbackRadius of her, the farthest from the threat, near an ally.</summary>
        private static bool Yard(EntityAlive self, World w, Vector3 me, Vector3 threat, bool hasThreat, List<Vector3i> bad, ref PickResult r)
        {
            int yRef = Mathf.FloorToInt(me.y + 0.05f);
            float best = float.MaxValue;
            Vector3 bestP = Vector3.zero;
            bool bestAlly = false;
            float meFromThreat = hasThreat ? ScRunners.XZ(me, threat) : 0f;
            for (int i = 0; i < 24; i++)
            {
                float a = UnityEngine.Random.Range(0f, Mathf.PI * 2f), d = UnityEngine.Random.Range(4f, FallbackRadius);
                int x = Mathf.FloorToInt(me.x + Mathf.Cos(a) * d), z = Mathf.FloorToInt(me.z + Mathf.Sin(a) * d);
                int y;
                if (!ScCommunity.FindStandable(w, x, z, yRef, out y) || Math.Abs(y - yRef) > SeedDy) continue;
                Vector3i cell = new Vector3i(x, y, z);
                if (!InZone(cell) || (bad != null && bad.Contains(cell)) || ScStuck.Unreachable(self.entityId, cell)) continue;
                Vector3 p = new Vector3(x + 0.5f, y, z + 0.5f);
                if (hasThreat && (ScRunners.XZ(threat, p) <= meFromThreat || ScRunners.PassDistance(me, p, threat) < 2f)) continue;
                if (EnemyNear(p, 4f) || Near(others, p, ReserveRadius)) continue;
                string why;
                if (!ScCommunity.GoodPost(w, cell, false, out why)) continue;
                bool ally = Near(allies, p, AllyRadius);
                float score = (hasThreat ? -ScRunners.XZ(threat, p) : 0f) - (ally ? 8f : 0f) + 0.2f * ScRunners.XZ(me, p);
                if (score < best) { best = score; bestP = p; bestAlly = ally; }
            }
            if (best == float.MaxValue) return false;
            r.Kind = PickKind.Yard;
            r.Spot = bestP;
            r.Source = "a zone floor";
            r.Note = "fell back to the zone floor farthest from the threat within " + FallbackRadius.ToString("0", CultureInfo.InvariantCulture) + " m"
                     + (bestAlly ? ", beside another member or you" : "") + (hasThreat ? " (" + ScRunners.XZ(threat, bestP).ToString("0", CultureInfo.InvariantCulture) + " m from it)" : "");
            return true;
        }

        /// <summary>
        /// The held spot still holds (every 2 s while she holds it): its floor; for a room or a walled spot its roof and walls;
        /// and no enemy within SpotEnemyClear that can see her (a sleeper woken in the room, one that followed her in - not
        /// one banging on the shut door). Yard and Hold only need their floor.
        /// </summary>
        public static bool StillGood(EntityAlive self, Vector3 spot, PickKind kind, int walls, bool roofed, out string why)
        {
            why = null;
            World w = self.world;
            if (w == null) return true;
            Vector3i cell = World.worldToBlockPos(spot);
            if (kind != PickKind.Hold && kind != PickKind.Here && !ScCommunity.GoodPost(w, cell, false, out why)) { why = "no longer a floor (" + why + ")"; return false; }
            if (kind == PickKind.Room || kind == PickKind.Walled || kind == PickKind.Here)
            {
                if (roofed && !Roofed(w, cell)) { why = "lost its roof"; return false; }
                int now = Walls(w, cell);
                if (now < Mathf.Min(walls, ScConfig.ShelterWalls - 1)) { why = "lost walls (" + now + " of 8 now)"; return false; }
                near.Clear();
                w.GetEntitiesInBounds(typeof(EntityAlive), new Bounds(spot, new Vector3(2f * SpotEnemyClear, 2f * SpotEnemyClear, 2f * SpotEnemyClear)), near);
                try
                {
                    for (int i = 0; i < near.Count; i++)
                    {
                        EntityAlive a = near[i] as EntityAlive;
                        if (a == null || a == self || a.IsDead() || (a.position - spot).sqrMagnitude > SpotEnemyClear * SpotEnemyClear) continue;
                        if (!EntityTargetingUtilities.IsEnemy(self, a) || !a.CanEntityBeSeen(self)) continue;
                        why = a.EntityName + " (" + a.entityId + ") is within " + SpotEnemyClear.ToString("0", CultureInfo.InvariantCulture) + " m and sees " + ScRetreat.HerObj(self);
                        return false;
                    }
                }
                finally { near.Clear(); }
            }
            return true;
        }

        /// <summary>ClearLine steps past at most this many entity colliders on the way (round k5: 8, was 4).</summary>
        private const int EntitySteps = 8;

        /// <summary>
        /// No block between the two points (round rt1; the Fable review, finding 1), end to end - a zombie pressed against the
        /// far side of a shut door is still behind it. Vanilla's ray (its layer mask, and its hit mask: bit 1 with transparent,
        /// so glass and bars stop it, as SCore's CanShoot), and an entity's collider met on the way (either end's own, or
        /// anyone in between) is stepped past, at most EntitySteps times (round k5, rt1 follow-up C: 8, it was 4 - a crowd of
        /// zombies between them ran out the steps and read as a clear line), so only a block or terrain makes the line not
        /// clear.
        /// </summary>
        internal static bool ClearLine(World w, Vector3 from, Vector3 to, bool transparent)
        {
            Vector3 d = to - from;
            float left = d.magnitude;
            if (w == null || left < 0.05f) return true;
            d /= left;
            Vector3 o = from;
            for (int k = 0; k < EntitySteps; k++)
            {
                if (!Voxel.Raycast(w, new Ray(o, d), left, transparent, false)) return true;
                WorldRayHitInfo info = Voxel.voxelRayHitInfo;
                if (info == null) return true;
                if (GameUtils.IsBlockOrTerrain(info.tag)) return false;
                float t = Mathf.Max(0.15f, Vector3.Dot(info.hit.pos - o, d) + 0.15f);
                o += d * t;
                left -= t;
                if (left <= 0.05f) return true;
            }
            return true;
        }

        // ------------------------------------------------------------------ console ----

        /// <summary>The cache in one line (sc retreat, the fallback log line).</summary>
        public static string Summary()
        {
            int roomsN = 0, walled = 0;                               // round hr1: renamed (rooms is the room table now)
            for (int i = 0; i < cands.Count; i++)
            {
                if (cands[i].Roofed && cands[i].Walls >= ScConfig.ShelterWalls) roomsN++;
                else walled++;
            }
            float now = Time.unscaledTime;
            if (!ScConfig.RetreatHome) return "off (RetreatHome false: no shelter scan)";
            int inRooms = 0;                                          // round hr1: candidates in an enclosable room
            for (int i = 0; i < cands.Count; i++) if (cands[i].Room >= 1) inRooms++;
            string s = cands.Count + " shelter candidate(s): " + roomsN + " roofed with " + ScConfig.ShelterWalls + "+ walls, " + walled + " walled or open-topped; "
                       + homes.Current.Rooms.Count + " enclosable room(s) (doors as walls), " + inRooms + " candidate(s) in one (round hr1)";
            if (scans == 0) s += scanning ? ", the first scan is running" : ", not scanned yet (the home must be loaded)";
            else s += ", last scan " + (now - lastDone).ToString("0", CultureInfo.InvariantCulture) + " s ago (" + lastSeeds + " seeds, " + lastMs.ToString("0.0", CultureInfo.InvariantCulture) + " ms over "
                      + lastFrames + " frames)" + (scanning ? ", a scan is running (" + queue.Count + " seeds left)" : ", next in " + Mathf.Max(0f, nextScan - now).ToString("0", CultureInfo.InvariantCulture) + " s");
            return s;
        }

        /// <summary>sc retreat shelters [n]: the best n candidates by walls and roof, nearest you first among equals.</summary>
        public static void ListLines(List<string> lines, int n)
        {
            lines.Add("[SC] " + Summary());
            int[] counts = new int[6];
            for (int i = 0; i < cands.Count; i++) counts[cands[i].Source]++;
            lines.Add("[SC] by seed: beds " + counts[SrcBed] + ", work stations " + counts[SrcStation] + ", posts " + counts[SrcPost] + ", claim block " + counts[SrcClaim] + ", random floors " + counts[SrcRandom]);
            EntityPlayerLocal lp = ScCommunity.LocalPlayer;
            Vector3 at = lp != null ? lp.position : Vector3.zero;
            var sorted = new List<Cand>(cands);
            sorted.Sort((a, b) =>
            {
                int ka = (a.Roofed ? 10 : 0) + a.Walls, kb = (b.Roofed ? 10 : 0) + b.Walls;
                if (ka != kb) return kb.CompareTo(ka);
                return ScRunners.XZ(at, a.Cell).CompareTo(ScRunners.XZ(at, b.Cell));
            });
            World w = ScCommunity.World;
            openDoorsOf.Clear();                                      // round hr1: the doors' live state now
            for (int i = 0; i < sorted.Count && i < n; i++)
            {
                Cand c = sorted[i];
                string why = null;
                bool ok = w == null || ScCommunity.GoodPost(w, c.Cell, false, out why);
                lines.Add("[SC]   " + c.Cell.x + " " + c.Cell.y + " " + c.Cell.z + ": " + (c.Roofed ? "roofed" : "NO ROOF") + ", " + c.Walls + " of 8 walls, " + SrcNames[c.Source]
                          + (c.NearDoor ? ", beside a door (taken last)" : "")
                          + (w != null && c.Room >= 1 ? RoomText(w, c) + " #" + c.Room : c.Room < 0 ? ", no enclosable room" : "")     // round hr1
                          + (lp != null ? ", " + ScRunners.XZ(at, c.Cell).ToString("0", CultureInfo.InvariantCulture) + " m from you" : "") + (ok ? "" : " - NOT STANDABLE NOW (" + why + ")"));
            }
            if (sorted.Count > n) lines.Add("[SC]   ... " + (sorted.Count - n) + " more (sc retreat shelters <n>)");
        }
    }
}
