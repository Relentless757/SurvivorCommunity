using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
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
    /// Shared by both games (MultiBlockManager.TryGetOversizedBlock is the same in 3.2 and 3.4); main thread.
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
            if (wait <= 0f || Unseen(ScCommunity.LocalPlayer, npc.position, to))
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
        internal static bool Unseen(EntityPlayerLocal p, Vector3 a, Vector3 b)
        {
            if (p == null || p.IsDead()) return true;
            return !p.IsInViewCone(a) && !p.IsInViewCone(b);
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
        /// offered to this body again for RoamUnreachableMinutes (nor a cell next to it - Unreachable).
        /// </summary>
        public static void MarkUnreachable(EntityAlive self, Vector3 target, float left)
        {
            if (self == null) return;
            Count("roam walk ended: no 1 m gain");
            float minutes = ScConfig.RoamUnreachableMinutes;
            Vector3i cell = World.worldToBlockPos(target);
            ScLog.Verbose(ScCommunity.DisplayName(self) + ": roam walk to " + cell + " ended - no 1 m gain in " + ScConfig.RoamNoProgressSeconds.ToString("0", Inv)
                          + " s (" + left.ToString("0.0", Inv) + " m left)" + (minutes > 0f ? "; not sent there again for " + minutes.ToString("0.#", Inv) + " min" : ""));
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
        }
    }
}
