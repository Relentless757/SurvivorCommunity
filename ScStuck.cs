using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using HarmonyLib;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round 29b (M29; the wreck findings' options b and c): why residents got wedged at a truck wreck and stayed there.
    ///  - Wreck-aware spots (option b): a truck wreck is an oversized block - it exists in the block data only at its origin
    ///    cell, so every other cell of its hull reads as air, and SC's spot test (ScCommunity.GoodPost) happily put roam spots
    ///    and posts inside it. NearOversized asks vanilla's MultiBlockManager (TryGetOversizedBlock: POI-placed and
    ///    player-placed oversized blocks, binned per chunk) for the cell and its 8 neighbors at feet and head height;
    ///    GoodPost then fails "in or beside &lt;block&gt;", which covers roam spots, posts, the post check on load, Unstick and
    ///    enclave homes (FindPost never offers a cell inside one at all).
    ///  - The watchdog's no-gain signal (option c): a body pushed back and forth against a hull moves faster than the still
    ///    rule's 0.2 m/s and never counts as still, and the roam's pauses reset the still count. SampleGain (ScCommunity's
    ///    StuckSample, once a second) counts the seconds a body wants to move without getting 1 m closer to its path's end
    ///    (or 1 m less of its path left: Progress), across walks and pauses, until a meter is gained, it arrives, or it stops
    ///    wanting to move for IdleResetSeconds; the watchdog calls 2 x StuckSeconds of it stuck (ScConfig.StuckNoGain).
    ///  - The move back only out of sight: MayTeleport holds a watchdog teleport while the player looks toward the body or
    ///    the post (the path is dropped once, so the AI plans again), until they look away or StuckSeenWaitSeconds pass.
    ///  - The roam's unreachable spots: UAITaskWanderHomeSC ends a walk with no 1 m gain (Progress) in RoamNoProgressSeconds
    ///    and marks the target (MarkUnreachable); for RoamUnreachableMinutes that body is not sent to it, or next to it, again.
    ///  - Work shifts (3.4): OnShift runs the watchdog on a shift only where the job allows it (WatchdogDuringShift).
    ///  - Round nv1: the gaps a body cannot walk through. A walk that ends with no progress remembers the cell ahead of her on
    ///    her path and where she stood, per base (RememberSpot); the path planner refuses those cells for that base's bodies
    ///    (Patch_PlannerGrid, Patch_PlannerLink), and a path that still runs through one is dropped or set aside.
    /// Shared by both games (MultiBlockManager.TryGetOversizedBlock is the same in 3.2 and 3.4); main thread (round nv1's two
    /// planner postfixes run on the path finder's threads and read only PlannerMap).
    /// </summary>
    public static class ScStuck
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // ------------------------------------------------------------------ oversized blocks (truck wrecks) ----

        /// <summary>The cell (feet) or the one above it (head) lies inside an oversized block's bounds.</summary>
        public static bool InsideOversized(Vector3i p)
        {
            string name;
            return Oversized(p, out name) || Oversized(new Vector3i(p.x, p.y + 1, p.z), out name);
        }

        /// <summary>
        /// An oversized block (a truck wreck, a big vehicle prop) in this cell or one of its 8 neighbors, at feet or head height.
        /// name: the block's name; inside: the cell itself is in it. Vanilla's lookup is per chunk and locked, and cheap when
        /// the chunk has no oversized block.
        /// </summary>
        public static bool NearOversized(Vector3i p, out string name, out bool inside)
        {
            inside = false;
            if (Oversized(p, out name) || Oversized(new Vector3i(p.x, p.y + 1, p.z), out name)) { inside = true; return true; }
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (dx == 0 && dz == 0) continue;
                    if (Oversized(new Vector3i(p.x + dx, p.y, p.z + dz), out name) || Oversized(new Vector3i(p.x + dx, p.y + 1, p.z + dz), out name)) return true;
                }
            }
            name = null;
            return false;
        }

        public static bool NearOversized(Vector3i p, out string name)
        {
            bool inside;
            return NearOversized(p, out name, out inside);
        }

        private static bool Oversized(Vector3i cell, out string name)
        {
            name = null;
            try
            {
                BlockValue bv;
                Vector3i parent, offset;
                if (!MultiBlockManager.Instance.TryGetOversizedBlock(cell, out bv, out parent, out offset)) return false;
                Block b = bv.Block;
                name = (b != null ? b.GetBlockName() : "an oversized block") + " at " + parent;
                return true;
            }
            catch (Exception e)
            {
                ScLog.Once("stuck-oversized", e);
                return false;
            }
        }

        // ------------------------------------------------------------------ the watchdog's no-gain signal ----

        /// <summary>A body that has not wanted to move for this many sampled seconds starts its no-gain count again.</summary>
        private const int IdleResetSeconds = 90;
        /// <summary>Within this many meters of its path's end a body is there: nothing left to gain.</summary>
        private const float ArriveDistance = 1.5f;
        /// <summary>A path end that moved this far (squared: 2 m) is a new goal: its distance is the new mark.</summary>
        private const float NewGoalSqr = 4f;

        /// <summary>
        /// One walk's progress toward where it is going (the roam's walk, the watchdog's goal): the straight distance at the
        /// last gain, and the path left to walk at the last gain on the path it follows. A gain is 1 m closer in a straight line
        /// or 1 m less of the same path left - so a detour round a wall, which leads away from the spot for a while, still
        /// gains on its path, while a body pushing at a hull or sidestepping against it gains on neither.
        /// </summary>
        public sealed class Progress
        {
            /// <summary>The straight distance at the last gain (-1: no mark yet).</summary>
            public float Best = -1f;
            /// <summary>The path left at the last gain, on PathRef (-1: none).</summary>
            public float BestPath = -1f;
            public GamePath.PathEntity PathRef;
            /// <summary>The roam's clock: Time.time of the last gain.</summary>
            public float GainAt;

            /// <summary>
            /// d: the straight distance now. True on the first look (the marks are set) and on a gain; a new path to the same
            /// goal (a re-plan) sets a new path mark without counting as one.
            /// </summary>
            public bool Gained(EntityAlive npc, float d)
            {
                GamePath.PathEntity path;
                float left = PathLeft(npc, out path);
                if (Best < 0f) { Best = d; PathRef = path; BestPath = left; return true; }
                bool gained = d <= Best - 1f;
                if (path != null && left >= 0f)
                {
                    if (!ReferenceEquals(path, PathRef) || BestPath < 0f) { PathRef = path; BestPath = left; }
                    else if (left <= BestPath - 1f) gained = true;
                }
                if (gained)
                {
                    Best = Mathf.Min(Best, d);
                    if (path != null && left >= 0f) { PathRef = path; BestPath = Mathf.Min(BestPath, left); }
                }
                return gained;
            }

            public void Clear()
            {
                Best = -1f;
                BestPath = -1f;
                PathRef = null;
            }
        }

        /// <summary>
        /// The path a body still has to walk: from where it stands to the path's current point, then point to point to the
        /// end. -1 (and path null) without a path being followed.
        /// </summary>
        public static float PathLeft(EntityAlive npc, out GamePath.PathEntity path)
        {
            path = null;
            GamePath.PathNavigate nav = npc != null ? npc.getNavigator() : null;
            GamePath.PathEntity p = nav != null ? nav.getPath() : null;
            if (p == null || p.points == null || p.isFinished()) return -1f;
            int i = p.getCurrentPathIndex(), n = Math.Min(p.getCurrentPathLength(), p.points.Length);
            if (i < 0 || i >= n || p.points[i] == null) return -1f;
            float left = Vector3.Distance(npc.position, p.points[i].projectedLocation);
            for (int k = i; k + 1 < n; k++)
            {
                if (p.points[k + 1] == null) return -1f;
                left += Vector3.Distance(p.points[k].projectedLocation, p.points[k + 1].projectedLocation);
            }
            path = p;
            return left;
        }

        /// <summary>Per member (residents, enclave people), session only: the no-gain count and the held move back.</summary>
        private sealed class Gain
        {
            public bool HasEnd;
            public Vector3 End;
            public readonly Progress P = new Progress();
            public int Seconds;
            public int Idle;
            /// <summary>Real time a watchdog move back was first held back because the player was watching; -1: none held.</summary>
            public float HeldSince = -1f;
            /// <summary>The last hold NoVerdict dropped and when: a verdict again within ResumeSeconds picks its wait up again.</summary>
            public float PrevHeld = -1f, ClearedAt = -1f;
            /// <summary>Round nv1: the last path CheckPath looked at, and when it last dropped one that ran through a gap (real time).</summary>
            public GamePath.PathEntity GapChecked;
            public float GapDroppedAt = -1f;
        }

        /// <summary>A hold dropped this recently (real seconds) is resumed by the next verdict: a body stuck in a block with no path
        /// to try loses its still count whenever the hold drops its path, and the wait must not start over each time.</summary>
        private const float ResumeSeconds = 60f;

        private static readonly ConditionalWeakTable<Member, Gain> gains = new ConditionalWeakTable<Member, Gain>();

        private static Gain Of(Member m) { return gains.GetValue(m, k => new Gain()); }

        /// <summary>
        /// ScCommunity.StuckSample, once per sampled second (never while the game is paused or the body did not tick): wants =
        /// a move target more than 1.5 m off or a path (or one being planned). Counts the seconds it wants to move with no 1 m
        /// gain toward the path's end (the move helper's target without a path; a gain as Progress counts it: closer in a
        /// straight line, or less of its path left); a new goal is a new mark, not a gain; a second with no goal yet (a path
        /// being planned) does not count. Reset by a meter gained, arriving, IdleResetSeconds without wanting to move, or a
        /// cure (ResetGain).
        /// </summary>
        internal static void SampleGain(Member m, EntityAlive npc, Vector3 pos, EntityMoveHelper mh, GamePath.PathNavigate nav, bool wants)
        {
            if (m == null) return;
            Gain g = Of(m);
            if (!wants)
            {
                if (++g.Idle >= IdleResetSeconds) { g.Seconds = 0; g.HasEnd = false; }
                return;
            }
            g.Idle = 0;
            Vector3 end;
            if (!GoalOf(mh, nav, out end)) return;
            float d = Vector3.Distance(pos, end);
            if (d <= ArriveDistance) { g.Seconds = 0; g.HasEnd = false; return; }
            if (!g.HasEnd || (end - g.End).sqrMagnitude > NewGoalSqr)
            {
                g.HasEnd = true;
                g.End = end;
                g.P.Clear();
                g.P.Gained(npc, d);                 // the new goal's marks
                return;
            }
            if (g.P.Gained(npc, d)) { g.Seconds = 0; return; }
            g.Seconds++;
        }

        /// <summary>Where the body is trying to get: its path's end, else the move helper's target while it is active.</summary>
        private static bool GoalOf(EntityMoveHelper mh, GamePath.PathNavigate nav, out Vector3 end)
        {
            GamePath.PathEntity path = nav != null ? nav.getPath() : null;
            if (path != null && !path.isFinished()) { end = path.rawEndPos; return true; }
            if (mh != null && mh.IsActive) { end = mh.moveToPos; return true; }
            end = Vector3.zero;
            return false;
        }

        /// <summary>The seconds counted toward the no-gain verdict (0 for a member never sampled).</summary>
        internal static int GainSeconds(Member m)
        {
            Gain g;
            return m != null && gains.TryGetValue(m, out g) ? g.Seconds : 0;
        }

        /// <summary>After a cure (Unstick, Repost) the count starts again.</summary>
        internal static void ResetGain(Member m)
        {
            Gain g;
            if (m == null || !gains.TryGetValue(m, out g)) return;
            g.Seconds = 0;
            g.Idle = 0;
            g.HasEnd = false;
            g.P.Clear();
            g.HeldSince = -1f;
            g.PrevHeld = -1f;
        }

        /// <summary>For the console's post line and the stuck line: the no-gain count and a held move back.</summary>
        internal static string GainLine(Member m)
        {
            Gain g;
            if (m == null || !gains.TryGetValue(m, out g)) return "no gain 0 s";
            return "no gain " + g.Seconds + " s" + (g.HasEnd ? " (mark " + g.P.Best.ToString("0.0", Inv) + " m" + (g.P.BestPath >= 0f ? ", path left " + g.P.BestPath.ToString("0.0", Inv) + " m" : "") + ")" : "")
                   + (g.HeldSince >= 0f ? ", move back held " + (Time.unscaledTime - g.HeldSince).ToString("0", Inv) + " s (you were watching)" : "");
        }

        // ------------------------------------------------------------------ the move back only out of sight ----

        /// <summary>A move back is being held because the player was watching (Stuck asks first: no Repost or back-off meanwhile).</summary>
        internal static bool Held(Member m)
        {
            Gain g;
            return m != null && gains.TryGetValue(m, out g) && g.HeldSince >= 0f;
        }

        /// <summary>
        /// The watchdog saw no verdict this second: a held move back is dropped once the body got going by itself - a meter
        /// gained, arrived, or IdleResetSeconds without trying (its no-gain count back to 0). Not merely because the dropped
        /// path restarted the still count: the hold's wait keeps running across that.
        /// </summary>
        internal static void NoVerdict(Member m)
        {
            Gain g;
            if (m == null || !gains.TryGetValue(m, out g) || g.HeldSince < 0f || g.Seconds > 0) return;
            g.PrevHeld = g.HeldSince;
            g.ClearedAt = Time.unscaledTime;
            g.HeldSince = -1f;
            Count("move back held, then not needed");
            ScLog.Verbose(m.Name + " got going again while the move back was held - no teleport");
        }

        /// <summary>
        /// May the watchdog teleport this body to "to" now? Yes when the player is not looking toward the body or the spot
        /// (Unseen), when StuckSeenWaitSeconds is 0 (the old rule: at once), or once the move back has been held that long.
        /// The first time it is held, the path is dropped (the AI plans again from where it stands) and a line logged.
        /// </summary>
        internal static bool MayTeleport(Member m, EntityAlive npc, Vector3 to, string why)
        {
            if (m == null || npc == null) return true;
            Gain g = Of(m);
            float wait = ScConfig.StuckSeenWaitSeconds;
            float now = Time.unscaledTime;
            if (wait <= 0f || UnseenByAll(npc.position, to))     // round MP0 (D 4.7): unseen by every player, not only the host
            {
                if (g.HeldSince >= 0f)
                {
                    Count("move back held, then done out of sight");
                    ScLog.Verbose(m.Name + ": the held move back goes ahead - you looked away after " + (now - g.HeldSince).ToString("0", Inv) + " s");
                }
                g.HeldSince = -1f;
                return true;
            }
            if (g.HeldSince < 0f)
            {
                ScCommunity.DropPath(npc);
                if (g.PrevHeld >= 0f && now - g.ClearedAt < ResumeSeconds)
                {
                    g.HeldSince = g.PrevHeld;
                    ScLog.Verbose(m.Name + " is stuck again (" + why + ") - the held move back's wait goes on (" + (now - g.HeldSince).ToString("0", Inv) + " s)");
                }
                else
                {
                    g.HeldSince = now;
                    Count("move back held: you were watching");
                    ScLog.Out(m.Name + " is stuck (" + why + ") but you are looking that way - the path is dropped, and the move back waits until you look away (at most "
                              + wait.ToString("0", Inv) + " s)");
                }
                g.PrevHeld = -1f;
                if (now - g.HeldSince < wait) return false;
            }
            if (now - g.HeldSince < wait) return false;
            Count("move back in sight after the wait");
            ScLog.Out(m.Name + " is still stuck after " + wait.ToString("0", Inv) + " s in your sight - moved back anyway");
            g.HeldSince = -1f;
            return true;
        }

        /// <summary>
        /// Neither point in the player's view cone (vanilla's IsInViewCone: the player's 180 degrees, so only what is behind
        /// them), at any distance; or no live player. ScRunners' skip-ahead asks this AND 40 m (RunSkipMinPlayerDist): a
        /// resident's move back lands at her post in the base, where the player usually is within 40 m.
        /// </summary>
        internal static bool Unseen(EntityPlayer p, Vector3 a, Vector3 b)     // round MP0: any player, not only the local one
        {
            if (p == null || p.IsDead()) return true;
            return !p.IsInViewCone(a) && !p.IsInViewCone(b);
        }

        /// <summary>
        /// Round MP0 (D 4.7): Unseen by every player in the world - a remote player looking that way holds the move back as the
        /// host does. No player at all: unseen. Single player: you alone, as before.
        /// </summary>
        internal static bool UnseenByAll(Vector3 a, Vector3 b)
        {
            List<EntityPlayer> all = ScPlayers.All;
            for (int i = 0; i < all.Count; i++)
                if (!Unseen(all[i], a, b)) return false;
            return true;
        }

        // ------------------------------------------------------------------ the roam's unreachable spots ----

        private struct Spot
        {
            public Vector3i Cell;
            public float Until;
        }

        /// <summary>Per body (entity id): roam targets it could not get closer to, with the Time.time they are forgotten.</summary>
        private static readonly Dictionary<int, List<Spot>> unreachable = new Dictionary<int, List<Spot>>();
        private const int MaxSpots = 16;

        /// <summary>
        /// The roam ended a walk that gained no meter in RoamNoProgressSeconds (UAITaskWanderHomeSC): the target is not
        /// offered to this body again for RoamUnreachableMinutes (nor a cell next to it - Unreachable). Round nv1: why, when
        /// given, is another reason the walk was dropped (its path runs through a remembered gap).
        /// </summary>
        public static void MarkUnreachable(EntityAlive self, Vector3 target, float left, string why = null)
        {
            if (self == null) return;
            Count(why == null ? "roam walk ended: no 1 m gain" : "roam walk dropped: path through a gap");     // round nv1: the reason
            float minutes = ScConfig.RoamUnreachableMinutes;
            Vector3i cell = World.worldToBlockPos(target);
            ScLog.Verbose(ScCommunity.DisplayName(self) + ": roam walk to " + cell
                          + (why == null ? " ended - no 1 m gain in " + ScConfig.RoamNoProgressSeconds.ToString("0", Inv) + " s" : " dropped - " + why)     // round nv1
                          + " (" + left.ToString("0.0", Inv) + " m left)" + (minutes > 0f ? "; not sent there again for " + minutes.ToString("0.#", Inv) + " min" : ""));
            if (minutes <= 0f) return;
            List<Spot> list;
            if (!unreachable.TryGetValue(self.entityId, out list)) { list = new List<Spot>(); unreachable[self.entityId] = list; }
            Prune(list, Time.time);
            if (list.Count >= MaxSpots) list.RemoveAt(0);
            list.Add(new Spot { Cell = cell, Until = Time.time + minutes * 60f });
        }

        /// <summary>A roam target this body could not reach lately: the cell, or one beside it (one cell either way across, two up or down).</summary>
        public static bool Unreachable(int entityId, Vector3i cell)
        {
            List<Spot> list;
            if (!unreachable.TryGetValue(entityId, out list)) return false;
            Prune(list, Time.time);
            if (list.Count == 0) { unreachable.Remove(entityId); return false; }
            for (int i = 0; i < list.Count; i++)
            {
                Vector3i c = list[i].Cell;
                if (Math.Abs(c.x - cell.x) <= 1 && Math.Abs(c.z - cell.z) <= 1 && Math.Abs(c.y - cell.y) <= 2) return true;
            }
            return false;
        }

        private static void Prune(List<Spot> list, float now)
        {
            for (int i = list.Count - 1; i >= 0; i--) if (now >= list[i].Until) list.RemoveAt(i);
        }

        /// <summary>For the console: the roam targets a body will not be sent to just now.</summary>
        internal static string UnreachableLine(int entityId)
        {
            List<Spot> list;
            if (!unreachable.TryGetValue(entityId, out list)) return "none";
            Prune(list, Time.time);
            if (list.Count == 0) return "none";
            var sb = new StringBuilder();
            foreach (Spot s in list) sb.Append(sb.Length > 0 ? " " : "").Append(s.Cell).Append(" (").Append(((s.Until - Time.time) / 60f).ToString("0.0", Inv)).Append(" min)");
            return sb.ToString();
        }

#if SC_V34
        // ------------------------------------------------------------------ work shifts (3.4) ----

        /// <summary>
        /// ScTicker's Resident branch for a member on a work shift (ScWork.Resident ticked her): the stuck rescue runs only
        /// where the job allows it (WatchdogDuringShift) - then the residents' watchdog without the far rule, and a move back
        /// that keeps the job's own Guard spot (ScCommunity.Watchdog's onShift). Otherwise its counts start again each second, so
        /// nothing stale is left when the shift ends.
        /// </summary>
        internal static void OnShift(Member m, EntityAlive npc)
        {
            if (m == null || npc == null) return;
            if (!WatchdogDuringShift(m))
            {
                ScCommunity.ResetStill(m);
                ResetGain(m);
                m.AwaySeconds = 0;
                return;
            }
            ScCommunity.Watchdog(m, npc, true);
        }

        // round 29 merge: 29a's hook - the stuck rescue runs on a shift only during a Base/Combat repair duty
        private static bool WatchdogDuringShift(Member m) { return ScWork.AllowsWatchdog(m); }
#endif

        // ------------------------------------------------------------------ round nv1: gaps a body cannot walk through ----

        /// <summary>Round nv1: a remembered gap is avoided this long (real minutes, Time.time) after the last no-progress end at it.</summary>
        private const float GapMinutes = 20f;
        /// <summary>The cell ahead is looked for this far along her path from where she stands (meters, horizontal).</summary>
        private const float AheadMeters = 2.5f;
        /// <summary>The path is sampled this often (meters) when it is checked against the gaps.</summary>
        private const float GapSampleStep = 0.25f;
        /// <summary>PathCrossesGap counts a sample only this far (meters) inside a gap cell's sides.</summary>
        private const float GapEdge = 0.15f;
        /// <summary>A cell the blocks do not explain counts its second no-progress end only this long (real seconds) after the
        /// first: the watchdog's verdict comes again each second while a move back is held.</summary>
        private const float HitSpacingSeconds = 10f;
        private const int MaxGapsPerBase = 16;
        /// <summary>A low gap takes in the low cells beside it (the rest of the same opening), this many cells in all at most.</summary>
        private const int MaxGapCells = 5;
        /// <summary>A member's path that runs through a gap is dropped at most this often (real seconds; CheckPath).</summary>
        private const float DropSpacingSeconds = 30f;
        /// <summary>A body not noted for this long (Time.time seconds) leaves the planner's list.</summary>
        private const float BodySeenSeconds = 60f;

        /// <summary>One remembered gap of a base (session only).</summary>
        private sealed class GapSpot
        {
            /// <summary>The cell ahead of her on her path, and the cell she stood in.</summary>
            public Vector3i Ahead, At;
            /// <summary>The cells the planner refuses: Ahead, and for a low gap the low cells beside it.</summary>
            public readonly List<Vector3i> Cells = new List<Vector3i>();
            /// <summary>Low: a cell too low to walk through (LowGap), remembered at once and dropped once it is no longer low.
            /// Else a cell the blocks do not explain, remembered (Active) on its second no-progress end.</summary>
            public bool Low, Active;
            public int Hits;
            public float LastHit, Until;
            public string What = "";
        }

        /// <summary>Round nv1: per base (BaseOf: a community's id, or -1 - an enclave's id) its remembered gaps.</summary>
        private static readonly Dictionary<int, List<GapSpot>> gaps = new Dictionary<int, List<GapSpot>>();

        private struct BodyNote
        {
            public int Base;
            public float Seen;
        }

        /// <summary>Round nv1: the bodies of each base seen lately (entity id), for the planner's list.</summary>
        private static readonly Dictionary<int, BodyNote> gapBodies = new Dictionary<int, BodyNote>();
        private static readonly List<int> gapScratch = new List<int>();
        private static readonly List<Vector3> gapPts = new List<Vector3>(64);
        private static bool gapsDirty;
        private static float nextGapTick;
        private static Vector3 plannerOrigin;
        private static int plannerRefused;
        /// <summary>0 not looked yet, 1 the planner postfix is in place, -1 it is not (PlannerHooked).</summary>
        private static int plannerHooked;

        /// <summary>
        /// Round nv1: entity id -> the boxes the path planner refuses on that body's paths (6 ints a cell, graph space in mm: min x,
        /// y, z, max x, y, z). Null when no base has a gap. Built on the main thread (RebuildPlanner) and swapped whole; the planner
        /// postfixes read it on the path finder's threads.
        /// </summary>
        internal static volatile Dictionary<int, int[]> PlannerMap;

        /// <summary>Round nv1: whose base a body's gaps belong to - a member's community (0 and up), an enclave person's enclave (-1 - its id).</summary>
        private static bool BaseOf(EntityAlive npc, out int key)
        {
            key = 0;
            if (npc == null) return false;
            EncPerson p = ScEnclavePeople.PersonOf(npc);
            if (p != null) { key = -1 - p.EnclaveId; return true; }
            Member m = ScRegistry.GetMember(npc.entityId);
            if (m == null) return false;
            key = m.CommunityId;
            return true;
        }

        /// <summary>Round nv1: the cell a body's lower half is in, standing at p (a path point is its floor + 0.05).</summary>
        internal static Vector3i BodyCell(Vector3 p)
        {
            return new Vector3i(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y + 0.5f), Mathf.FloorToInt(p.z));
        }

        /// <summary>
        /// Round nv1: a cell a standing body cannot walk through, though the path planner may send it there - its lower body cell
        /// is free and the cell above blocks movement (any collider: BlockShapeNew's IsMovementBlocked). A door does not count
        /// (ScDoors opens those). The planner's voxel grid notes a blocked head only by four short sphere casts from the cell's
        /// center; under a half-thick wall or a corner piece they start on the shape's inner face and miss it, so a hole 1 m high
        /// under such a wall reads as open along the wall's line (research nv1_wall_gap.md).
        /// </summary>
        internal static bool LowGap(World w, Vector3i c)
        {
            if (w == null) return false;
            Vector3i up = new Vector3i(c.x, c.y + 1, c.z);
            return !Blocks(w, c, w.GetBlock(c)) && Blocks(w, up, w.GetBlock(up));
        }

        private static bool Blocks(World w, Vector3i p, BlockValue v)
        {
            if (v.isair || v.isWater) return false;
            Block b = v.Block;
            if (b == null || b.HasTag(BlockTags.Door)) return false;
            return b.IsMovementBlocked(w, p, v, BlockFace.None);
        }

        private static bool IsDoor(World w, Vector3i c)
        {
            BlockValue a = w.GetBlock(c), b = w.GetBlock(new Vector3i(c.x, c.y + 1, c.z));
            return (!a.isair && a.Block != null && a.Block.HasTag(BlockTags.Door)) || (!b.isair && b.Block != null && b.Block.HasTag(BlockTags.Door));
        }

        private static string BlockLabel(BlockValue v)
        {
            if (v.isair) return "air";
            Block b = v.Block;
            return b != null ? b.GetBlockName() : "?";
        }

        /// <summary>For the log: what fills the cell and the one above it.</summary>
        private static string CellWhat(World w, Vector3i c)
        {
            return BlockLabel(w.GetBlock(c)) + " under " + BlockLabel(w.GetBlock(new Vector3i(c.x, c.y + 1, c.z)));
        }

        /// <summary>Where she stands, then the points of the path from its current one on (at most max in all).</summary>
        private static void CollectPoints(EntityAlive npc, GamePath.PathEntity p, List<Vector3> pts, int max)
        {
            pts.Clear();
            pts.Add(npc.position);
            if (p == null || p.points == null || p.isFinished()) return;
            int n = Math.Min(p.getCurrentPathLength(), p.points.Length);
            for (int i = Math.Max(0, p.getCurrentPathIndex()); i < n && pts.Count < max; i++)
            {
                if (p.points[i] == null) break;
                pts.Add(p.points[i].projectedLocation);
            }
        }

        /// <summary>
        /// Round nv1: the cell she could not get into - along her path from where she stands (no path: toward the move helper's
        /// target), the first low gap (LowGap) within AheadMeters; else the first cell 0.5 to 1.5 m ahead that is not her own.
        /// low: it is a low gap.
        /// </summary>
        private static bool AheadCell(EntityAlive npc, out Vector3i ahead, out bool low)
        {
            ahead = Vector3i.zero;
            low = false;
            World w = npc.world;
            if (w == null) return false;
            GamePath.PathNavigate nav = npc.getNavigator();
            CollectPoints(npc, nav != null ? nav.getPath() : null, gapPts, 12);
            if (gapPts.Count < 2 && npc.moveHelper != null && npc.moveHelper.IsActive) gapPts.Add(npc.moveHelper.moveToPos);
            if (gapPts.Count < 2) return false;
            Vector3i own = BodyCell(npc.position);
            bool near = false;
            float walked = 0f;
            for (int i = 0; i + 1 < gapPts.Count && walked <= AheadMeters; i++)
            {
                Vector3 a = gapPts[i], b = gapPts[i + 1];
                float len = Mathf.Sqrt((b.x - a.x) * (b.x - a.x) + (b.z - a.z) * (b.z - a.z));
                int steps = Mathf.Max(1, Mathf.CeilToInt(len / GapSampleStep));
                for (int s = 1; s <= steps; s++)
                {
                    float t = (float)s / steps, along = walked + len * t;
                    if (along > AheadMeters) break;
                    Vector3 q = Vector3.Lerp(a, b, t);
                    q.y = t < 0.5f ? a.y : b.y;              // a point's own floor, never one between two heights
                    Vector3i c = BodyCell(q);
                    if (c.x == own.x && c.z == own.z) continue;
                    if (LowGap(w, c)) { ahead = c; low = true; return true; }
                    if (!near && along >= 0.5f && along <= 1.5f) { ahead = c; near = true; }
                }
                walked += len;
            }
            return near;
        }

        /// <summary>Round nv1: the low gap at start and the low cells beside it at its height over a floor (the rest of the opening).</summary>
        private static void LowCells(World w, Vector3i start, List<Vector3i> into)
        {
            into.Clear();
            into.Add(start);
            for (int i = 0; i < into.Count && into.Count < MaxGapCells; i++)
            {
                Vector3i c = into[i];
                for (int d = 0; d < 4 && into.Count < MaxGapCells; d++)
                {
                    Vector3i n = new Vector3i(c.x + (d == 0 ? 1 : d == 1 ? -1 : 0), c.y, c.z + (d == 2 ? 1 : d == 3 ? -1 : 0));
                    if (into.Contains(n) || !LowGap(w, n)) continue;
                    BlockValue floor = w.GetBlock(new Vector3i(n.x, n.y - 1, n.z));
                    if (floor.isair || floor.isWater) continue;
                    into.Add(n);
                }
            }
        }

        private static bool HasActive(List<GapSpot> list)
        {
            for (int i = 0; i < list.Count; i++) if (list[i].Active) return true;
            return false;
        }

        private static bool HasActive(int key)
        {
            List<GapSpot> list;
            return gaps.TryGetValue(key, out list) && HasActive(list);
        }

        /// <summary>
        /// Round nv1: a walk ended with no progress - the roam's no-gain or blocked end, the watchdog's stuck verdict, a shelter or
        /// revive walk that gained nothing. The cell ahead of her on her path (AheadCell) and the cell she stood in go on her base's
        /// list: a low gap at once, with the low cells beside it; any other cell on its second end there (HitSpacingSeconds apart).
        /// Not a door's cell (ScDoors), nor while another body blocks her. A gap is avoided for GapMinutes after the last end at it:
        /// the planner refuses its cells on this base's bodies' paths (Patch_PlannerGrid, Patch_PlannerLink), a path that still runs
        /// through it is dropped (CheckPath) or set aside (the roam, the hr1 walks), and it is logged once, when it is remembered.
        /// walk: what she was doing, for the log. Main thread.
        /// </summary>
        internal static void RememberSpot(EntityAlive npc, string walk)
        {
            try
            {
                if (npc == null || npc.world == null) return;
                int key;
                if (!BaseOf(npc, out key)) return;
                if (npc.moveHelper != null && npc.moveHelper.BlockedEntity != null) return;     // a body in the way, not the blocks
                Vector3i ahead;
                bool low;
                if (!AheadCell(npc, out ahead, out low)) return;
                World w = npc.world;
                if (IsDoor(w, ahead)) return;
                float now = Time.time;
                List<GapSpot> list;
                if (!gaps.TryGetValue(key, out list)) { list = new List<GapSpot>(); gaps[key] = list; }
                GapSpot g = null;
                for (int i = 0; i < list.Count; i++) if (list[i].Ahead == ahead || list[i].Cells.Contains(ahead)) { g = list[i]; break; }
                if (g == null)
                {
                    if (list.Count >= MaxGapsPerBase)
                    {
                        int oldest = 0;
                        for (int i = 1; i < list.Count; i++) if (list[i].Until < list[oldest].Until) oldest = i;
                        if (list[oldest].Active) gapsDirty = true;
                        list.RemoveAt(oldest);
                    }
                    g = new GapSpot { Ahead = ahead };
                    list.Add(g);
                }
                else if (now - g.LastHit < HitSpacingSeconds) { g.Until = now + GapMinutes * 60f; return; }
                g.Hits++;
                g.LastHit = now;
                g.Until = now + GapMinutes * 60f;
                g.At = BodyCell(npc.position);
                string who = ScCommunity.DisplayName(npc);
                if (g.Active)
                {
                    Count("gap: stuck there again");
                    ScLog.Verbose(who + " got stuck at the remembered gap at " + g.Ahead + " again (" + walk + ") - avoided " + GapMinutes.ToString("0", Inv) + " min more (round nv1)");
                    return;
                }
                if (!low && g.Hits < 2)
                {
                    Count("gap: noted once");
                    ScLog.Verbose(who + " made no progress into " + ahead + " from " + g.At + " (" + walk + "; " + CellWhat(w, ahead)
                                  + ") - noted; a second time there makes it a gap the walks go round (round nv1)");
                    return;
                }
                g.Low = low;
                g.Active = true;
                if (low) LowCells(w, ahead, g.Cells);
                else { g.Cells.Clear(); g.Cells.Add(ahead); }
                g.What = CellWhat(w, ahead) + (low ? ": too low to walk through" : ": no progress there twice");
                gapsDirty = true;
                Count(low ? "gap remembered: too low" : "gap remembered: stuck twice");
                ScLog.Out(who + " got stuck walking into " + ahead + " from " + g.At + " (" + walk + "; " + g.What
                          + (g.Cells.Count > 1 ? ", " + g.Cells.Count + " cells wide" : "") + ") - the " + (key >= 0 ? "community's" : "enclave's")
                          + " walks go round it for " + GapMinutes.ToString("0", Inv) + " min (round nv1)");
                RebuildPlanner(now);                                                // the planner's list at once
            }
            catch (Exception e)
            {
                ScLog.Once("stuck-gap-remember", e);
            }
        }

        /// <summary>
        /// Round nv1: a body of a base is loaded (the ticker's residents, the watchdog's sample, enclave people included) - the planner
        /// refuses its base's gaps on its paths. Also runs GapTick once a second. Main thread.
        /// </summary>
        internal static void NoteBody(EntityAlive npc)
        {
            try
            {
                int key;
                if (npc == null || !BaseOf(npc, out key)) return;
                float now = Time.time;
                BodyNote b;
                if ((!gapBodies.TryGetValue(npc.entityId, out b) || b.Base != key) && HasActive(key)) gapsDirty = true;
                gapBodies[npc.entityId] = new BodyNote { Base = key, Seen = now };
                if (now >= nextGapTick) GapTick(now);
            }
            catch (Exception e)
            {
                ScLog.Once("stuck-gap-body", e);
            }
        }

        /// <summary>
        /// Round nv1, once a second: gaps no one got stuck at for GapMinutes go; a low gap's cells that are no longer low (a block put
        /// in the hole, or the block over it taken away) go, and the gap with them once none is left; bodies not noted for
        /// BodySeenSeconds leave the planner's list; then the planner's list is built again if anything changed.
        /// </summary>
        private static void GapTick(float now)
        {
            nextGapTick = now + 1f;
            World w = GameManager.Instance != null ? GameManager.Instance.World : null;
            foreach (KeyValuePair<int, List<GapSpot>> kv in gaps)
            {
                List<GapSpot> list = kv.Value;
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    GapSpot g = list[i];
                    if (now >= g.Until)
                    {
                        if (g.Active)
                        {
                            gapsDirty = true;
                            ScLog.Verbose("the gap at " + g.Ahead + " is no longer avoided - nobody got stuck there for " + GapMinutes.ToString("0", Inv) + " min (round nv1)");
                        }
                        list.RemoveAt(i);
                        continue;
                    }
                    if (!g.Active || !g.Low || w == null) continue;
                    int before = g.Cells.Count;
                    for (int c = g.Cells.Count - 1; c >= 0; c--)
                    {
                        Vector3i cell = g.Cells[c];
                        if (w.GetChunkFromWorldPos(cell) == null) continue;          // not loaded: unknown, kept
                        if (!LowGap(w, cell)) g.Cells.RemoveAt(c);
                    }
                    if (g.Cells.Count == before) continue;
                    gapsDirty = true;
                    if (g.Cells.Count > 0) continue;
                    Count("gap gone: filled or opened");
                    ScLog.Out("the gap at " + g.Ahead + " is gone (" + CellWhat(w, g.Ahead) + " now) - walks may go there again (round nv1)");
                    list.RemoveAt(i);
                }
            }
            gapScratch.Clear();
            foreach (KeyValuePair<int, BodyNote> b in gapBodies) if (now - b.Value.Seen > BodySeenSeconds) gapScratch.Add(b.Key);
            for (int i = 0; i < gapScratch.Count; i++)
            {
                BodyNote b = gapBodies[gapScratch[i]];
                if (HasActive(b.Base)) gapsDirty = true;
                gapBodies.Remove(gapScratch[i]);
            }
            gapScratch.Clear();
            if (Origin.position != plannerOrigin) gapsDirty = true;
            if (gapsDirty) RebuildPlanner(now);
        }

        /// <summary>Round nv1: PlannerMap from the active gaps and the bodies seen lately (null with none). Main thread.</summary>
        private static void RebuildPlanner(float now)
        {
            gapsDirty = false;
            Vector3 o = Origin.position;
            plannerOrigin = o;
            Dictionary<int, int[]> perBase = null;
            var boxes = new List<int>();
            foreach (KeyValuePair<int, List<GapSpot>> kv in gaps)
            {
                boxes.Clear();
                foreach (GapSpot g in kv.Value)
                {
                    if (!g.Active) continue;
                    foreach (Vector3i c in g.Cells)
                    {
                        // a node in the cell: x and z in it, its floor where BodyCell puts it in this cell (y - 0.5 to y + 0.5)
                        boxes.Add(Mathf.RoundToInt((c.x - o.x) * 1000f));
                        boxes.Add(Mathf.RoundToInt((c.y - 0.5f - o.y) * 1000f));
                        boxes.Add(Mathf.RoundToInt((c.z - o.z) * 1000f));
                        boxes.Add(Mathf.RoundToInt((c.x + 1f - o.x) * 1000f));
                        boxes.Add(Mathf.RoundToInt((c.y + 0.5f - o.y) * 1000f));
                        boxes.Add(Mathf.RoundToInt((c.z + 1f - o.z) * 1000f));
                    }
                }
                if (boxes.Count == 0) continue;
                if (perBase == null) perBase = new Dictionary<int, int[]>();
                perBase[kv.Key] = boxes.ToArray();
            }
            if (perBase == null) { PlannerMap = null; return; }
            var map = new Dictionary<int, int[]>();
            foreach (KeyValuePair<int, BodyNote> b in gapBodies)
            {
                int[] arr;
                if (now - b.Value.Seen <= BodySeenSeconds && perBase.TryGetValue(b.Value.Base, out arr)) map[b.Key] = arr;
            }
            PlannerMap = map.Count > 0 ? map : null;
        }

        /// <summary>
        /// Round nv1: does this path - from where she stands, its points from the current one on - run through one of her base's
        /// remembered gaps? cell: the first gap cell on it. The cell she stands in does not count (walking out of it is fine).
        /// Main thread.
        /// </summary>
        internal static bool PathCrossesGap(EntityAlive npc, GamePath.PathEntity path, out Vector3i cell)
        {
            cell = Vector3i.zero;
            try
            {
                int key;
                List<GapSpot> list;
                if (npc == null || path == null || gaps.Count == 0 || !BaseOf(npc, out key) || !gaps.TryGetValue(key, out list) || !HasActive(list)) return false;
                CollectPoints(npc, path, gapPts, 1024);
                Vector3i own = BodyCell(npc.position);
                for (int i = 0; i + 1 < gapPts.Count; i++)
                {
                    Vector3 a = gapPts[i], b = gapPts[i + 1];
                    float len = Mathf.Sqrt((b.x - a.x) * (b.x - a.x) + (b.z - a.z) * (b.z - a.z));
                    int steps = Mathf.Max(1, Mathf.CeilToInt(len / GapSampleStep));
                    for (int s = 1; s <= steps; s++)
                    {
                        float t = (float)s / steps;
                        Vector3 q = Vector3.Lerp(a, b, t);
                        q.y = t < 0.5f ? a.y : b.y;
                        Vector3i c = BodyCell(q);
                        if (c == own) continue;
                        // well inside the cell, not on its edge: the path's smoothing moves its points a little toward their
                        // neighbors, and a path that only skirts a gap's corner does not go through it
                        float fx = q.x - c.x, fz = q.z - c.z;
                        if (fx < GapEdge || fx > 1f - GapEdge || fz < GapEdge || fz > 1f - GapEdge) continue;
                        for (int k = 0; k < list.Count; k++)
                        {
                            if (!list[k].Active || !list[k].Cells.Contains(c)) continue;
                            cell = c;
                            return true;
                        }
                    }
                }
                return false;
            }
            catch (Exception e)
            {
                ScLog.Once("stuck-gap-path", e);
                return false;
            }
        }

        /// <summary>
        /// Round nv1, from the watchdog's sample: once per new path a member follows - whoever asked for it, SC's walks or
        /// 0-XNPCCore's Guard to her post or her job's spot - a path that runs through a remembered gap (planned before the gap was
        /// known) is dropped, so the next plan goes round it (the planner refuses the gap's cells), at most once in
        /// DropSpacingSeconds per member. Not while the planner postfix is missing: the same path would only come back.
        /// </summary>
        internal static void CheckPath(Member m, EntityAlive npc, GamePath.PathNavigate nav)
        {
            if (m == null || npc == null || nav == null || PlannerMap == null || !PlannerHooked()) return;
            GamePath.PathEntity path = nav.getPath();
            if (path == null || path.isFinished()) return;
            Gain g = Of(m);
            if (ReferenceEquals(path, g.GapChecked)) return;
            g.GapChecked = path;
            float now = Time.unscaledTime;
            if (g.GapDroppedAt >= 0f && now - g.GapDroppedAt < DropSpacingSeconds) return;
            Vector3i cell;
            if (!PathCrossesGap(npc, path, out cell)) return;
            g.GapDroppedAt = now;
            Count("gap: a path through it dropped");
            ScLog.Verbose(ScCommunity.DisplayName(npc) + "'s path runs through the remembered gap at " + cell + " - dropped, the next one goes round it (round nv1)");
            ScCommunity.DropPath(npc);
        }

        /// <summary>Round nv1: the planner postfix (Patch_PlannerGrid) is in place - looked up once, after the patching.</summary>
        internal static bool PlannerHooked()
        {
            if (plannerHooked != 0) return plannerHooked > 0;
            try
            {
                MethodBase t = PlannerGridTarget();
                HarmonyLib.Patches info = t != null ? HarmonyLib.Harmony.GetPatchInfo(t) : null;
                plannerHooked = -1;
                if (info != null)
                    foreach (HarmonyLib.Patch p in info.Postfixes)
                        if (p.PatchMethod != null && p.PatchMethod.DeclaringType == typeof(Patch_PlannerGrid)) plannerHooked = 1;
            }
            catch (Exception e)
            {
                plannerHooked = -1;
                ScLog.Once("stuck-gap-hooked", e);
            }
            return plannerHooked > 0;
        }

        /// <summary>
        /// Round nv1, on the path finder's threads: true when the node lies in a remembered gap of the base whose body this path is
        /// for. The body is the path's own: ASPPathFinder hands A* its OnPathFinished as the callback, so the callback's target is
        /// the finder and its entity. A zombie's or a player's path, or any other NPC's, is never in PlannerMap. Reads only
        /// PlannerMap (swapped whole, never changed in place) and plain fields.
        /// </summary>
        internal static bool Refuses(Pathfinding.Path path, Pathfinding.GraphNode node)
        {
            try
            {
                Dictionary<int, int[]> map = PlannerMap;
                if (map == null || (object)path == null || (object)node == null || path.callback == null) return false;
                GamePath.ASPPathFinder f = path.callback.Target as GamePath.ASPPathFinder;
                object body = f != null ? f.entity : null;
                int[] b;
                if (body == null || !map.TryGetValue(((EntityAlive)body).entityId, out b)) return false;
                Pathfinding.Int3 q = node.position;
                for (int i = 0; i + 5 < b.Length; i += 6)
                {
                    if (q.x < b[i] || q.x >= b[i + 3] || q.z < b[i + 2] || q.z >= b[i + 5] || q.y < b[i + 1] || q.y >= b[i + 4]) continue;
                    Interlocked.Increment(ref plannerRefused);
                    return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        internal static MethodBase PlannerGridTarget()
        {
            return AccessTools.Method(typeof(Pathfinding.Path), "CanTraverse", new[] { typeof(Pathfinding.GraphNode), typeof(int) });
        }

        internal static MethodBase PlannerLinkTarget()
        {
            return AccessTools.Method(typeof(Pathfinding.Path), "CanTraverse", new[] { typeof(Pathfinding.Connection) });
        }

        /// <summary>
        /// Round nv1: the planner's grid step into a neighbor (LevelGridNode.Open asks Path.CanTraverse(node, direction) for each
        /// of the four, whatever traversal provider the path has - vanilla's or 0-XNPCCore's): refused into a remembered gap of
        /// this body's base. A path's start and end nodes are asked with direction -1 and are never refused, so a body standing in
        /// a gap still finds its way out. Only true turns false; nothing else of the provider's verdict changes.
        /// </summary>
        [HarmonyPatch(typeof(Pathfinding.Path), "CanTraverse", new Type[] { typeof(Pathfinding.GraphNode), typeof(int) })]
        public static class Patch_PlannerGrid
        {
            // the arguments by position (__0 the node, __1 the direction): their names are the A* library's, not ours
            public static void Postfix(Pathfinding.Path __instance, Pathfinding.GraphNode __0, int __1, ref bool __result)
            {
                if (__result && __1 >= 0 && PlannerMap != null && Refuses(__instance, __0)) __result = false;
            }
        }

        /// <summary>Round nv1: the same for the grid's extra links (a step up, a drop, a ladder, a door's link) into a gap cell.</summary>
        [HarmonyPatch(typeof(Pathfinding.Path), "CanTraverse", new Type[] { typeof(Pathfinding.Connection) })]
        public static class Patch_PlannerLink
        {
            public static void Postfix(Pathfinding.Path __instance, Pathfinding.Connection __0, ref bool __result)
            {
                if (__result && PlannerMap != null && Refuses(__instance, __0.node)) __result = false;
            }
        }

        /// <summary>Round nv1, for the console's post line: this body's base's remembered gaps.</summary>
        internal static string GapLine(EntityAlive npc)
        {
            int key;
            List<GapSpot> list;
            if (npc == null || !BaseOf(npc, out key) || !gaps.TryGetValue(key, out list) || !HasActive(list)) return "none";
            var sb = new StringBuilder();
            float now = Time.time;
            foreach (GapSpot g in list)
            {
                if (!g.Active) continue;
                sb.Append(sb.Length > 0 ? " " : "").Append(g.Ahead).Append(" (").Append(g.Cells.Count).Append(g.Cells.Count == 1 ? " cell, " : " cells, ")
                  .Append(g.Low ? "too low" : "stuck twice").Append(", ").Append(((g.Until - now) / 60f).ToString("0.0", Inv)).Append(" min)");
            }
            return sb.ToString();
        }

        /// <summary>Round nv1, for sc xnpc: every base's remembered gaps, the planner postfix and its refusals.</summary>
        internal static string GapReport()
        {
            var sb = new StringBuilder();
            int n = 0;
            float now = Time.time;
            foreach (KeyValuePair<int, List<GapSpot>> kv in gaps)
            {
                foreach (GapSpot g in kv.Value)
                {
                    if (!g.Active) continue;
                    n++;
                    sb.Append(n > 1 ? "; " : "").Append(kv.Key >= 0 ? "community " + kv.Key : "enclave " + (-1 - kv.Key)).Append(": ").Append(g.Ahead)
                      .Append(" from ").Append(g.At).Append(" (").Append(g.What).Append(", ").Append(g.Cells.Count).Append(g.Cells.Count == 1 ? " cell, " : " cells, ")
                      .Append(((g.Until - now) / 60f).ToString("0.0", Inv)).Append(" min left)");
                }
            }
            Dictionary<int, int[]> map = PlannerMap;
            return (n == 0 ? "none remembered" : n + " remembered: " + sb) + "; planner postfix " + (PlannerHooked() ? "on" : "MISSING")
                   + ", bodies it guards " + (map != null ? map.Count : 0) + ", steps refused " + Volatile.Read(ref plannerRefused);
        }

        // ------------------------------------------------------------------ counters and reset ----

        private static readonly Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);

        internal static void Count(string what)
        {
            int n;
            counts.TryGetValue(what, out n);
            counts[what] = n + 1;
        }

        /// <summary>This session's counters (sc xnpc's stuck line).</summary>
        internal static string Counters()
        {
            if (counts.Count == 0) return "nothing yet";
            var keys = new List<string>(counts.Keys);
            keys.Sort(StringComparer.Ordinal);
            var sb = new StringBuilder();
            foreach (string k in keys) sb.Append(sb.Length > 0 ? ", " : "").Append(k).Append('=').Append(counts[k]);
            return sb.ToString();
        }

        /// <summary>Per world (ScConfig.Reset at GameStarting): the remembered spots and the counters go.</summary>
        public static void Reset()
        {
            unreachable.Clear();
            counts.Clear();
            // round nv1: the remembered gaps and the planner's list
            gaps.Clear();
            gapBodies.Clear();
            PlannerMap = null;
            gapsDirty = false;
            nextGapTick = 0f;
            plannerRefused = 0;
        }
    }
}
