// round hr1: the safe-room test (new file)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round hr1 (2026-11-27; the user: "If they are in an enclosed space with doors closed and no zombies inside they should be
    /// considered safe to heal."; research/combat/HR1_DESIGN.md, decision 3): is the room a hurt member stands in a safe place to
    /// heal? Server only, pure reads (the shared scratch below is main thread only).
    ///
    /// The fill: a 3D breadth-first fill from her feet and head cells through every cell whose crossed face does not block
    /// movement (vanilla's Block.IsMovementBlocked: a multi-block child resolves to its parent, a closed composite door or hatch
    /// - TEFeatureDoor - and a closed BlockPoweredDoor block, an open one passes, a step over 0.5 m blocks). All six neighbors,
    /// so stairs, ladders, holes and open hatches join the room; glass, bars and fences block movement, so they are walls.
    /// Not enclosed (no verdict: the normal rules): more than MaxCells cells, a cell outside the home zone + its margin (round
    /// hl1's AtHome), an open-sky cell (World.IsOpenSkyAbove) or a cell in a chunk that is not loaded. A one-block wall leaks over
    /// its top, correctly (zombies jump it).
    ///
    /// Safe = enclosed, no enemy (awake or asleep) in a room cell or the cell above one, no armed enemy (one holding a ranged
    /// weapon) that can see her within ArmedSight, and no enemy within NearSight (round hr1 review, HR1-m5: the gap rule's 2.5 m
    /// while she has a usable weapon, so a safe room always passes the gap rule) that can see her (vanilla's EntityAlive.CanSee:
    /// through bars and glass). At most about 3,000 block reads (MaxCells x 6), only at a heal decision, cached CacheSeconds per
    /// body (SafeToHeal). ScShelter's scan uses the same fill with doors as walls (FillFrom, doorsAsWalls) for its room ids.
    /// sc saferoom &lt;name|id&gt; prints the verdict.
    /// </summary>
    public static class ScSafeRoom
    {
        /// <summary>A fill that passes this many cells is no room (a hall, a roofed compound, stairs to everywhere): no verdict.</summary>
        public const int MaxCells = 512;
        /// <summary>An armed enemy that can see her within this many meters makes the room unsafe (it shoots through glass and bars).</summary>
        public const float ArmedSight = 25f;
        /// <summary>Any enemy within this many meters that can see her makes the room unsafe (bars and glass: its hand reaches her).</summary>
        public const float NearSight = 2f;
        /// <summary>A body's verdict is kept this long (SafeToHeal is asked at about 1 Hz per hurt member).</summary>
        private const float CacheSeconds = 1f;

        internal enum Leak { None = 0, TooBig, OpenSky, OutsideBound, Unloaded, NoWorld }

        /// <summary>A fill's outcome: enclosed or why not, the cells, the block reads, the doors it met.</summary>
        internal struct FillResult
        {
            public bool Enclosed;
            public Leak Leak;
            public Vector3i LeakCell;
            public int Cells, Reads, DoorsShut, DoorsOpen;
            public Vector3i Min, Max;
        }

        /// <summary>One body's verdict (cached, and what the console prints).</summary>
        public sealed class Verdict
        {
            public float At;
            public bool Enclosed, Safe;
            public int Cells, Reads, DoorsShut, DoorsOpen;
            /// <summary>"safe", "open" (not enclosed) or "unsafe" (enclosed, an enemy) - and the detail for the log.</summary>
            public string Kind = "";
            public string Reason = "";
            /// <summary>What the verdict is about, for the log's "once per change" (not the cell count): "open:sky", "unsafe:inside" ...</summary>
            public string Key = "";
            /// <summary>Round hr1 (review HR1-m6): her feet cell when it was taken - the cache answers only for the same cell.</summary>
            public Vector3i Cell;
        }

        private static readonly Vector3i[] Dirs = { new Vector3i(1, 0, 0), new Vector3i(-1, 0, 0), new Vector3i(0, 0, 1), new Vector3i(0, 0, -1), new Vector3i(0, 1, 0), new Vector3i(0, -1, 0) };
        /// <summary>The face of the neighbor a step in Dirs[i] crosses (+X enters through its West face, +Y through its Bottom).</summary>
        private static readonly BlockFace[] Faces = { BlockFace.West, BlockFace.East, BlockFace.South, BlockFace.North, BlockFace.Bottom, BlockFace.Top };

        private static readonly Queue<Vector3i> open = new Queue<Vector3i>();
        private static readonly HashSet<Vector3i> seen = new HashSet<Vector3i>();
        private static readonly List<Vector3i> doorScratch = new List<Vector3i>();
        private static readonly HashSet<Vector3i> openDoorSeen = new HashSet<Vector3i>();
        private static readonly List<Entity> near = new List<Entity>();
        private static readonly Dictionary<int, Verdict> cache = new Dictionary<int, Verdict>();
        /// <summary>The last verdict key logged per body (the log says a verdict once, when it changes).</summary>
        private static readonly Dictionary<int, string> logged = new Dictionary<int, string>();
        private static int tests, hits, enclosedN, safeN, tooBig, openSky, outside, unloadedN, enemyInside, armedSees, nearSees;
        private static long readsTotal;
        private static int readsMax;
        private static float nextPrune;

        public static void Reset()
        {
            open.Clear();
            seen.Clear();
            doorScratch.Clear();
            openDoorSeen.Clear();
            near.Clear();
            cache.Clear();
            logged.Clear();
            tests = hits = enclosedN = safeN = tooBig = openSky = outside = unloadedN = enemyInside = armedSees = nearSees = 0;
            readsTotal = 0;
            readsMax = 0;
            nextPrune = 0f;
        }

        // ------------------------------------------------------------------ the fill ----

        /// <summary>
        /// The fill from feet (and the cell over it): every connected cell movement passes through, up to MaxCells. doorsAsWalls
        /// (ScShelter's room ids): a Door-tagged block (or a powered door) is a wall whatever its state, and its parent cell goes
        /// into doors (once). Otherwise a door is what its state says (closed: a wall, counted DoorsShut, its parent into doors;
        /// open: a gap, counted DoorsOpen). cells (optional) receives the visited cells. Main thread only.
        /// </summary>
        internal static FillResult FillFrom(World w, Vector3i feet, bool doorsAsWalls, HashSet<Vector3i> cells, List<Vector3i> doors)
        {
            var r = new FillResult { Min = feet, Max = feet };
            if (w == null) { r.Leak = Leak.NoWorld; return r; }
            HashSet<Vector3i> set = cells ?? seen;
            List<Vector3i> dl = doors ?? doorScratch;
            set.Clear();
            dl.Clear();
            open.Clear();
            openDoorSeen.Clear();
            r.Enclosed = FillCore(w, feet, doorsAsWalls, set, dl, ref r);
            open.Clear();
            r.Cells = set.Count;
            if (doorsAsWalls) r.DoorsShut = dl.Count;
            readsTotal += r.Reads;
            if (r.Reads > readsMax) readsMax = r.Reads;
            return r;
        }

        /// <summary>FillFrom's breadth-first walk: true when it ran out of cells inside every limit (enclosed), else r.Leak says why.</summary>
        private static bool FillCore(World w, Vector3i feet, bool doorsAsWalls, HashSet<Vector3i> set, List<Vector3i> dl, ref FillResult r)
        {
            int lcx = int.MinValue, lcz = int.MinValue;
            bool lLoaded = false;
            // her feet and head cells start the room whatever stands in them (she is there)
            for (int k = 0; k < 2; k++)
            {
                Vector3i s = new Vector3i(feet.x, feet.y + k, feet.z);
                if (!Loaded(w, s, ref lcx, ref lcz, ref lLoaded)) { r.Leak = Leak.Unloaded; r.LeakCell = s; return false; }
                if (k == 1)
                {
                    BlockValue hv = w.GetBlock(s);
                    r.Reads++;
                    if (!hv.isair && hv.Block != null && hv.Block.IsMovementBlocked(w, s, hv, BlockFace.Bottom)) break;   // a low ceiling: her head cell is solid
                }
                Leak lk = CellLeak(w, s);
                if (lk != Leak.None) { r.Leak = lk; r.LeakCell = s; return false; }
                set.Add(s);
                open.Enqueue(s);
                Grow(ref r, s);
            }
            while (open.Count > 0)
            {
                Vector3i c = open.Dequeue();
                for (int i = 0; i < Dirs.Length; i++)
                {
                    Vector3i n = new Vector3i(c.x + Dirs[i].x, c.y + Dirs[i].y, c.z + Dirs[i].z);
                    if (set.Contains(n)) continue;
                    if (n.y < 1) continue;                                       // bedrock
                    if (n.y > 253) { r.Leak = Leak.OpenSky; r.LeakCell = n; return false; }
                    if (!Loaded(w, n, ref lcx, ref lcz, ref lLoaded)) { r.Leak = Leak.Unloaded; r.LeakCell = n; return false; }
                    BlockValue bv = w.GetBlock(n);
                    r.Reads++;
                    if (!bv.isair)
                    {
                        Block b = bv.Block;
                        if (b == null) continue;
                        bool door = b.HasTag(BlockTags.Door) || b is BlockPoweredDoor;
                        if (door && doorsAsWalls)
                        {
                            Vector3i dp = ParentOf(b, n, bv);
                            if (!dl.Contains(dp)) dl.Add(dp);
                            continue;
                        }
                        if (b.IsMovementBlocked(w, n, bv, Faces[i]))
                        {
                            if (door) { Vector3i pp = ParentOf(b, n, bv); if (!dl.Contains(pp)) { dl.Add(pp); r.DoorsShut++; } }
                            continue;
                        }
                        if (door && openDoorSeen.Add(ParentOf(b, n, bv))) r.DoorsOpen++;
                    }
                    Leak lk = CellLeak(w, n);
                    if (lk != Leak.None) { r.Leak = lk; r.LeakCell = n; return false; }
                    set.Add(n);
                    if (set.Count > MaxCells) { r.Leak = Leak.TooBig; r.LeakCell = n; return false; }
                    Grow(ref r, n);
                    open.Enqueue(n);
                }
            }
            return true;
        }

        private static void Grow(ref FillResult r, Vector3i c)
        {
            if (c.x < r.Min.x) r.Min.x = c.x;
            if (c.y < r.Min.y) r.Min.y = c.y;
            if (c.z < r.Min.z) r.Min.z = c.z;
            if (c.x > r.Max.x) r.Max.x = c.x;
            if (c.y > r.Max.y) r.Max.y = c.y;
            if (c.z > r.Max.z) r.Max.z = c.z;
        }

        /// <summary>A multi-block child's parent cell (a door is one door whichever of its cells the fill met).</summary>
        private static Vector3i ParentOf(Block b, Vector3i p, BlockValue bv)
        {
            return b.isMultiBlock && bv.ischild && b.multiBlockPos != null ? b.multiBlockPos.GetParentPos(p, bv) : p;
        }

        /// <summary>Why this open cell ends the room: outside the home zone + margin, or open sky over it; None when it may join.</summary>
        private static Leak CellLeak(World w, Vector3i c)
        {
            if (!UAI.UAIConsiderationScNotHomeMember.AtHome(new Vector3(c.x + 0.5f, c.y, c.z + 0.5f))) return Leak.OutsideBound;
            if (w.IsOpenSkyAbove(c.x, c.y, c.z)) return Leak.OpenSky;
            return Leak.None;
        }

        /// <summary>Its chunk is loaded (the last chunk asked is remembered: neighbors are nearly always in the same one).</summary>
        private static bool Loaded(World w, Vector3i p, ref int lcx, ref int lcz, ref bool lLoaded)
        {
            int cx = World.toChunkXZ(p.x), cz = World.toChunkXZ(p.z);
            if (cx == lcx && cz == lcz) return lLoaded;
            lcx = cx;
            lcz = cz;
            lLoaded = w.GetChunkSync(cx, cz) != null;
            return lLoaded;
        }

        // ------------------------------------------------------------------ the verdict ----

        /// <summary>
        /// SafeToHeal's test, uncached: the fill (doors as their state says), then the enemies. Server only; a client, or a
        /// body that is not loaded, gets "open". Main thread.
        /// </summary>
        public static Verdict Test(EntityAlive e)
        {
            var v = new Verdict { At = Time.time, Kind = "open" };
            World w = e != null ? e.world : null;
            if (w == null || e.IsDead()) { v.Reason = "no body"; return v; }
            if (!ScCommunity.IsHost || e.isEntityRemote) { v.Reason = "not the server's body (the server decides)"; return v; }
            tests++;
            Vector3i feet = World.worldToBlockPos(e.position);
            v.Cell = feet;                                         // round hr1 (review HR1-m6)
            doorScratch.Clear();
            FillResult f = FillFrom(w, feet, false, seen, doorScratch);
            v.Cells = seen.Count;
            v.Reads = f.Reads;
            v.DoorsShut = f.DoorsShut;
            v.DoorsOpen = f.DoorsOpen;
            if (!f.Enclosed)
            {
                switch (f.Leak)
                {
                    case Leak.TooBig: tooBig++; v.Key = "open:big"; v.Reason = "no room: more than " + MaxCells + " cells (a hall, a roofed yard or stairs to the rest of the base)"; break;
                    case Leak.OpenSky: openSky++; v.Key = "open:sky"; v.Reason = "no room: open sky over " + Cell(f.LeakCell) + " (the fill leaked after " + v.Cells + " cells)"; break;
                    case Leak.OutsideBound: outside++; v.Key = "open:outside"; v.Reason = "no room: " + Cell(f.LeakCell) + " is outside the home zone + margin (the fill leaked after " + v.Cells + " cells)"; break;
                    case Leak.Unloaded: unloadedN++; v.Key = "open:unloaded"; v.Reason = "no room: the chunk of " + Cell(f.LeakCell) + " is not loaded"; break;
                    default: v.Key = "open:none"; v.Reason = "no world"; break;
                }
                if (f.DoorsOpen > 0) v.Reason += "; " + f.DoorsOpen + " open door(s) on the way";
                seen.Clear();
                return v;
            }
            v.Enclosed = true;
            enclosedN++;
            string key;
            string unsafeWhy = Enemies(e, w, f, out key);
            seen.Clear();
            string room = v.Cells + " cells, " + v.DoorsShut + " door(s) shut";
            if (unsafeWhy != null)
            {
                v.Kind = "unsafe";
                v.Key = "unsafe:" + key;
                v.Reason = "an enclosed room (" + room + ") but " + unsafeWhy;
                return v;
            }
            v.Safe = true;
            v.Kind = "safe";
            v.Key = "safe";
            safeN++;
            v.Reason = "a safe room: enclosed (" + room + "), no enemy inside, none armed in sight within " + F0(ArmedSight) + " m, none within " + F0(NearSight) + " m that sees " + ScRetreat.HerObj(e);
            return v;
        }

        /// <summary>Why the enclosed room in seen is not safe (an enemy inside, an armed one in sight, one close that sees her), or null.</summary>
        private static string Enemies(EntityAlive e, World w, FillResult f, out string key)
        {
            key = "";
            // the room's own box first (enemies awake or asleep inside), then the sight tests round her
            near.Clear();
            try
            {
                Vector3 min = new Vector3(f.Min.x, f.Min.y, f.Min.z), max = new Vector3(f.Max.x + 1f, f.Max.y + 2f, f.Max.z + 1f);
                Bounds box = new Bounds((min + max) * 0.5f, max - min);
                w.GetEntitiesInBounds(typeof(EntityAlive), box, near);
                for (int i = 0; i < near.Count; i++)
                {
                    EntityAlive a = near[i] as EntityAlive;
                    if (!Foe(e, a)) continue;
                    Vector3i c = World.worldToBlockPos(a.position);
                    if (seen.Contains(c) || seen.Contains(new Vector3i(c.x, c.y - 1, c.z)))
                    {
                        enemyInside++;
                        key = "inside";                                // round hr1 (review HR1-n1): the kind, not the id (one line per change)
                        return Who(a) + " is inside" + (a.IsSleeping ? " (asleep)" : "") + " at " + Cell(c);
                    }
                }
                near.Clear();
                // round hr1 (review HR1-m5): with a usable weapon the near test is the gap rule's 2.5 m (so a safe room contains it); without one, decision 3's 2 m
                float nearR = ScHealBackOff.UsableWeapon(e) != null ? Mathf.Max(NearSight, ScHealBackOff.GapRange) : NearSight;
                w.GetEntitiesInBounds(typeof(EntityAlive), new Bounds(e.position, new Vector3(2f * ArmedSight, 2f * ArmedSight, 2f * ArmedSight)), near);
                for (int i = 0; i < near.Count; i++)
                {
                    EntityAlive a = near[i] as EntityAlive;
                    if (!Foe(e, a) || a.IsSleeping) continue;
                    float d2 = (a.position - e.position).sqrMagnitude;
                    if (d2 <= nearR * nearR && a.CanSee(e))
                    {
                        nearSees++;
                        key = "near";
                        return Who(a) + " at " + F1(Mathf.Sqrt(d2)) + " m sees " + ScRetreat.HerObj(e) + " (bars, glass or a gap: within " + F1(nearR) + " m)";
                    }
                    if (d2 <= ArmedSight * ArmedSight && HoldsGun(a) && a.CanSee(e))
                    {
                        armedSees++;
                        key = "armed";
                        return Who(a) + ", armed, sees " + ScRetreat.HerObj(e) + " at " + F0(Mathf.Sqrt(d2)) + " m";
                    }
                }
                return null;
            }
            finally { near.Clear(); }
        }

        /// <summary>An enemy of hers that counts: alive, SCore's IsEnemy, not inert (awake or asleep: the caller decides).</summary>
        private static bool Foe(EntityAlive e, EntityAlive a)
        {
            return a != null && a != e && !a.IsDead() && EntityTargetingUtilities.IsEnemy(e, a) && !global::Harmony.ZombieFeatures.InertEntity.IsInert(a);
        }

        /// <summary>A ranged weapon in its hand (decision 3's "armed").</summary>
        private static bool HoldsGun(EntityAlive a)
        {
            ItemValue held = a.inventory != null ? a.inventory.holdingItemItemValue : null;
            ItemClass ic = held != null && !held.IsEmpty() ? held.ItemClass : null;
            if (ic == null || ic.Actions == null) return false;
            for (int i = 0; i < ic.Actions.Length && i < 2; i++) if (ic.Actions[i] is ItemActionRanged) return true;
            return false;
        }

        /// <summary>
        /// Round hr1 (step 1 of the heal order): is she in a safe room now? The verdict is cached CacheSeconds per body; a change
        /// of verdict is logged once (SC's log: "safe room", "no room: open sky ...", "an enclosed room ... but ..."). text: the
        /// verdict's reason. Server only (false on a client). Main thread.
        /// </summary>
        public static bool SafeToHeal(EntityAlive e, out string text)
        {
            string key;
            return SafeToHeal(e, false, out text, out key);
        }

        /// <summary>
        /// Round hr1 (review HR1-M2, HR1-m6): SafeToHeal with the verdict's key ("safe", "open:sky", "unsafe:inside" ...); fresh: a new
        /// test whatever the cache holds (a room walk's arrival). The cache answers only for the cell the verdict was taken in.
        /// </summary>
        public static bool SafeToHeal(EntityAlive e, bool fresh, out string text, out string key)
        {
            text = "";
            key = "";
            if (e == null || !ScCommunity.IsHost || e.isEntityRemote) return false;
            float now = Time.time;
            if (now >= nextPrune) Prune(now);
            Verdict v;
            if (!fresh && cache.TryGetValue(e.entityId, out v) && now - v.At < CacheSeconds && v.Cell == World.worldToBlockPos(e.position))
            {
                hits++;
                text = v.Reason;
                key = v.Key;
                return v.Safe;
            }
            v = Test(e);
            cache[e.entityId] = v;
            text = v.Reason;
            key = v.Key;
            string had;
            if (!logged.TryGetValue(e.entityId, out had) || had != v.Key)
            {
                logged[e.entityId] = v.Key;
                Patches_Xnpc.Count("saferoom-" + v.Kind);
                ScLog.Out(ScMedic.BodyName(e) + " safe-room verdict: " + v.Reason + " (" + v.Reads + " block reads; round hr1)");
            }
            return v.Safe;
        }

        private static void Prune(float now)
        {
            nextPrune = now + 30f;
            var gone = new List<int>();
            foreach (var kv in cache) if (now - kv.Value.At > 60f) gone.Add(kv.Key);
            foreach (int id in gone) { cache.Remove(id); logged.Remove(id); }
        }

        /// <summary>A body SC lets go of (entity ids are recycled).</summary>
        public static void Forget(int entityId)
        {
            cache.Remove(entityId);
            logged.Remove(entityId);
        }

        // ------------------------------------------------------------------ console ----

        /// <summary>The totals (sc saferoom, sc meds).</summary>
        public static string Report()
        {
            return tests + " test(s) (" + hits + " answered from the 1 s cache): " + enclosedN + " enclosed, " + safeN + " safe; not enclosed: " + tooBig + " too big, "
                   + openSky + " open sky, " + outside + " outside the base, " + unloadedN + " unloaded; unsafe: " + enemyInside + " enemy inside, " + armedSees
                   + " armed enemy in sight, " + nearSees + " enemy within " + F0(NearSight) + " m in sight; block reads " + readsTotal + " in all, at most " + readsMax + " in one fill";
        }

        /// <summary>sc saferoom [name|id]: the verdict for one member now (a fresh test, nothing cached or logged), or the totals.</summary>
        public static void ConsoleCmd(List<string> p, Action<string> output)
        {
            ScConfig.EnsureParsed();
            if (!ScRegistry.Loaded) { output("[SC] the registry is not loaded"); return; }
            if (p.Count < 2)
            {
                output("[SC] saferoom (round hr1): a hurt member at home heals at once in an enclosed room (solid blocks, glass, bars and SHUT doors/hatches; at most "
                       + MaxCells + " cells, no open sky, inside the home zone + margin) with no enemy inside, no armed enemy in sight within " + F0(ArmedSight)
                       + " m and none within " + F0(NearSight) + " m that sees her");
                output("[SC] " + Report());
                output("[SC] sc saferoom <id|name> for one member's verdict now");
                return;
            }
            Member m = FindMember(p[1]);
            EntityAlive e = m != null ? ScCommunity.FindNpc(m.Id) : null;
            if (e == null || e.IsDead()) { output("[SC] no loaded member '" + p[1] + "' - sc saferoom <id|name>"); return; }
            if (!ScCommunity.IsHost) { output("[SC] host only (the server tests the room)"); return; }
            using (ScHomeScope.Of(e))     // her own community's home zone (round MP6)
            {
                Verdict v = Test(e);
                Vector3i feet = World.worldToBlockPos(e.position);
                output("[SC] " + m.Name + " (" + e.entityId + ") at " + Cell(feet) + (UAI.UAIConsiderationScNotHomeMember.AtHome(e.position) ? ", at home" : ", AWAY FROM HOME (the heal order is for home only)")
                       + ": " + (v.Safe ? "SAFE" : v.Enclosed ? "ENCLOSED BUT NOT SAFE" : "NOT ENCLOSED") + " - " + v.Reason);
                output("[SC]   cells " + v.Cells + ", doors shut " + v.DoorsShut + ", doors open on the way " + v.DoorsOpen + ", block reads " + v.Reads
                       + (ScShelter.NearDoor(e.world, feet) ? "; within 2.5 m of a door (a room walk's spot keeps clear of doors)" : ""));
            }
        }

        private static Member FindMember(string key)
        {
            int id;
            if (int.TryParse(key, out id))
            {
                Member byId = ScRegistry.GetMember(id);
                if (byId != null) return byId;
            }
            foreach (Member m in ScRegistry.Members.Values)
                if (m.Name != null && m.Name.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0) return m;
            return null;
        }

        private static string Who(EntityAlive t) { return t.EntityName + " (" + t.entityId + ")"; }
        private static string Cell(Vector3i c) { return c.x + " " + c.y + " " + c.z; }
        private static string F0(float f) { return f.ToString("0", CultureInfo.InvariantCulture); }
        private static string F1(float f) { return f.ToString("0.#", CultureInfo.InvariantCulture); }
    }
}
