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
    /// Round k1 (2026-11-15, research/combat/SC_VS_NPCCORE.md sections 1.4, 1.6, 4.1, 4.2 and 4.4; the scope the user approved):
    /// back-offs work again, inside the base. In the session that asked for it Paul stood and shot point-blank in every state he
    /// held his .44 in - some SC gate scored 0-XNPCCore's two back-offs (BackupKeepDistance 8, BackupRanged 5) to 0 in each of
    /// them - and rt1's stand-off, which only took a perfect point about 15 m off, called him "cornered" five times.
    ///
    /// Those gates were really keeping members from backing out of the base (round hl1). This class keeps the back-off itself
    /// inside it instead, so the gates can go:
    ///   - 0-XNPCCore's task (Voice Control's UAITaskBackupFromTargetPaced, on both actions) draws up to six points 2 to 10 m off
    ///     (vanilla's CalcAway), takes the first within 25 degrees of straight back and paths there with no home bound (it even
    ///     detaches the vanilla home). For one of the community's members at home (round hl1's AtHome: the home zone +
    ///     ResidentRoamZoneMargin, or the defended area during a base attack) Patch_BoundStart picks the point instead (Pick):
    ///     the BackOffSteps (10, 6 and 3 m; never past the task's own max_distance), longest first, on seven directions -
    ///     straight back, then 25, 50 and 75 degrees either side - the straightest valid point of the first step that has one.
    ///     Valid (Candidate): a floor within 2 cells of her level, at home, standable and not on her unreachable list, a clear
    ///     straight walk chest high (no wall, door or glass in it), out of reach (BackupKeepDistanceMax) of every enemy, and a
    ///     walk that passes no other enemy ahead of her within 1.5 m. A point she failed to reach (its run ended on its own - the
    ///     navigator had no path - with her not 0.3 m on and not at the point) is set aside for 10 s.
    ///   - 0-XNPCCore's own Start then runs untouched (its bookkeeping, SCore's Start postfix, its pace); Patch_BoundPoint answers
    ///     SC's point at the one place the task turns its point into the path's goal, SCore's GetMoveToLocation, so the path
    ///     goes there.
    ///   - No valid point at all: the task ends at once with no path, as 0-XNPCCore's own failure does, and ScBackOffRoom scores
    ///     both back-offs 0 against that enemy for BackOffNoPointSeconds - one decision: the shot (6) takes it, then the
    ///     back-off is asked again.
    ///   - The Fable review of k1+k2 (F1): a back-off that gains nothing hands the beat to the shot. BackupKeepDistance (8) beats
    ///     the shot (6) inside its band and its task holds no shot, so against a feral that keeps pace she walked backward
    ///     forever. Patch_BoundUpdate (the task's Update) tests the gain GainSeconds (1.5 s) into a bounded back-off and when it
    ///     ends: the enemy still inside the band, or the gap not GainNeed (1 m) larger than at its start - ScBackOffRoom scores
    ///     0 for StandOffShootSeconds (HandToShot; backoff-no-gain) and the walk stops: she shoots, then the back-off is asked
    ///     again. A wide step (round k5, below) that ends with the enemy still inside StandOffMin stamps the same beat. The
    ///     re-review (R1): the beat is the MEMBER's, against every
    ///     enemy (key target AnyTarget) - per target, two pursuers in the band took turns (back off from A, A stamped; from B,
    ///     B stamped; A's expired) and the shot never had a beat. Only the true "no point from T" verdict stays per target.
    ///     R3: the gain test judges every member's back-off, bounded or not (a follower away from home with a feral on her
    ///     walked backward without firing). R4: BackupRanged (5, not the band action) is let through a member-wide beat while
    ///     she reloads - its walk away beats standing idle through the reload.
    ///   - F5: a point counts as not reached only when its run ended on its own (its Update found no path, seen by
    ///     Patch_BoundUpdate, which sets it aside there and then) - not when another action took the decision, as the old test
    ///     (a restart within 1 s) also counted; that test is kept only when the Update is not bound.
    ///   - F6: the walk needs a floor under it, sampled every FloorStep (1 m), each within 1 cell of the last (a pit, a drop or
    ///     a ledge between her and the point fails it).
    /// Round k5 (rt1's stand-off folded into KITE): SCResident's ScKiteWide (8.2) runs this same task for a hurt member who
    /// kites (ScRetreat Kite: hurt with nothing to heal with, a gun with rounds) from any visible enemy within StandOffMin, so
    /// every rule above judges it too. Its own extras here: a run of it that ends on its own with the enemy still inside
    /// StandOffMin is followed by StandOffShootSeconds of shooting (counted backoff-wide-shoot: step, shoot, step); its
    /// member-wide beats let it through while she reloads, as BackupRanged's; and away from home, KITE with you (a follower)
    /// bounds both of her back-offs to within StandOffFollowerLeash of you (Region Leader) - "no point" there counts toward
    /// ScRetreat's cornered-away rule (NoPointStreak). HasRoom (round k5, the final Fable check's C1) lets R2 ask whether a
    /// step back exists while she holds her melee weapon (on melee neither back-off scores, so nothing renewed "no room").
    /// Followers away from home (but a hurt one who kites), strangers, enclave people, raiders and every other NPC keep
    /// 0-XNPCCore's back-off as it is.
    /// The Start and GetMoveToLocation patches are bound by name (no reference to Voice Control's DLL), resolved in Prepare and
    /// off together, with one log line, when either is missing; the Update patch (F1, F5) is off on its own, with one warning.
    /// BackOffHome=false: no bound (0-XNPCCore's own back-off at home too) - the gain test still judges it (R3).
    /// The task's Start and Update are the only writers of this class's state (the UAI considerations at the end of this file
    /// only read it; HasRoom only uses the shared scratch lists). Main thread only.
    /// </summary>
    public static class ScBackOff
    {
        /// <summary>Straight away from the threat, then 25, 50 and 75 degrees either side (0-XNPCCore's own spread is 25).</summary>
        internal static readonly float[] Angles = { 0f, 25f, -25f, 50f, -50f, 75f, -75f };
        /// <summary>A step's floor is looked for within this many cells of her level.</summary>
        private const int MaxDy = 2;
        /// <summary>The straight walk is tested this high over both floors (chest high: a wall, a door, glass).</summary>
        private const float WalkLineHeight = 1.2f;
        /// <summary>
        /// The walk keeps this far from every enemy ahead of her (more than PassAhead along it) but the one she backs off from.
        /// </summary>
        private const float PassClear = 1.5f, PassAhead = 0.5f;
        /// <summary>Other enemies within this of a point count against it (the pick's score).</summary>
        private const float CrowdRadius = 6f;
        /// <summary>
        /// A run that ended on its own with her this little farther on (F5) - or, without the Update patch, a start this soon after
        /// the last - means the last point was not reached.
        /// </summary>
        private const float StallSeconds = 1f, StallMove = 0.3f;
        /// <summary>A point she did not reach is set aside this long, and so is every point within BadRadius of it.</summary>
        private const float BadSeconds = 10f, BadRadius = 1.2f;
        /// <summary>A member's shortened or no-point line is logged (verbose) at most this often.</summary>
        private const float LogSeconds = 2f;
        /// <summary>
        /// The Fable review (F1): a bounded back-off is judged this long after its start (and again when it ends) - the enemy still
        /// inside the band, or the gap not GainNeed larger than at the start, hands the beat to the shot.
        /// </summary>
        private const float GainSeconds = 1.5f, GainNeed = 1f;
        /// <summary>
        /// F5: BackupKeepDistance's band (round k4: UAIConsiderationScPosture action="keep", was ScBackupKeepBand) is this much
        /// wider for the back-off already running against that enemy (hysteresis: it starts within BackupKeepDistanceMax and lets
        /// go this much beyond it, so it does not flip with the shot at the edge).
        /// </summary>
        internal const float BandHysteresis = 0.5f;
        /// <summary>F6: the floor under the walk is sampled this often (meters), each sample within 1 cell of the last.</summary>
        private const float FloorStep = 1f;
        /// <summary>
        /// R1: the target of a member-wide beat's key (no entity has id -1; 0 might be one). HandToShot writes it; NoPointFor and
        /// ScBackOffRoom read it beside the per-target "no point" key.
        /// </summary>
        private const int AnyTarget = -1;
        /// <summary>R4: 0-XNPCCore's reload cvar (ScArms reads the same one).</summary>
        private const string ReloadingCvar = "SdcsReloading";
        /// <summary>R4: the action let through a member-wide beat while she reloads (0-XNPCCore's own name for it).</summary>
        private const string RangedBackOffAction = "BackupRanged";
        /// <summary>Round k5: SCResident's action that runs this task for KITE's wide band (Config/utilityai.xml).</summary>
        internal const string WideBackOffAction = "ScKiteWide";
        /// <summary>Round k5: a "no point" verdict older than this no longer counts toward NoPointStreak.</summary>
        private const float StreakSeconds = 5f;

        /// <summary>Her last bounded back-off (Start), the points she did not reach, and when she was last logged.</summary>
        internal sealed class Last
        {
            public Vector3 Point, From;
            public float At, Step, Angle, NextLog;
            public int Target = -1;
            public string Why = "";
            public bool NoPoint;
            /// <summary>
            /// F1/F5: the gap to Target when the bounded run started; it is running (its Start set it, its Update clears it at
            /// its end); judged already at GainSeconds; and whether its last run ended on its own (its Update found no path),
            /// when.
            /// </summary>
            public float GapAtStart, EndedAt;
            public bool Running, GainChecked, EndedOwn;
            /// <summary>R3: the last run took SC's point (Point, Step, Angle are its); false: 0-XNPCCore's own point, unbounded.</summary>
            public bool Bounded;
            /// <summary>Round k5: the last run was ScKiteWide's (KITE's wide band); "no point" verdicts in a row (a pick that worked clears it), the last one's time.</summary>
            public bool Wide;
            public int NoPointStreak;
            public float NoPointAt;
            public readonly List<Vector3> Bad = new List<Vector3>();
            public readonly List<float> BadUntil = new List<float>();
            /// <summary>
            /// Round cb1: the last run was bounded at home (it runs: RunPace); a shooting run (Far, FarMeters long) whose point is
            /// beyond k1's steps (Long: no straight-line test - its path is checked once the navigator has it, PathChecked, the path
            /// the last action left set aside, StalePath); its deadline; its pace applied (RunCounted); far runs whose path failed
            /// (LongFails since LongFailAt; LongOffUntil: k1's steps only until then).
            /// </summary>
            public bool Home, Far, Long, PathChecked, RunCounted;
            public float FarMeters, Deadline, LongFailAt, LongOffUntil;
            public int LongFails;
            public GamePath.PathEntity StalePath;
            /// <summary>Round cb1 (review CB1-M1): the UAI action that runs this run (RunCutWithGain: another action took over).</summary>
            public string Action = "";
        }

        private static readonly Dictionary<int, Last> lasts = new Dictionary<int, Last>();
        /// <summary>No point to back off to from this enemy: (self id, target id) -> until when ScBackOffRoom scores 0.</summary>
        private static readonly Dictionary<long, float> noPointUntil = new Dictionary<long, float>();
        private static readonly List<Entity> near = new List<Entity>();
        private static readonly List<Vector3> enemies = new List<Vector3>();
        private static float nextPrune;
        private static int bounded, shortened, wide, noPoint, unreached, unapplied, noGain, handed;

        /// <summary>The point Patch_BoundStart picked, for Patch_BoundPoint within the same Start call (main thread).</summary>
        private static int scopeId = -1;
        private static Vector3 scopePoint;
        private static bool scopeUsed;

        public static void Reset()
        {
            lasts.Clear();
            noPointUntil.Clear();
            near.Clear();
            enemies.Clear();
            nextPrune = 0f;
            bounded = shortened = wide = noPoint = unreached = unapplied = noGain = handed = 0;
            scopeId = -1;
            scopeUsed = false;
            stepStates.Clear();                                               // round cb1
            farRuns = farFailed = farTimeouts = runs = 0;
            stepsAsked = stepsReached = stepsNone = 0;
        }

        // ------------------------------------------------------------------ who ----

        /// <summary>
        /// One of the community's members on 0-XNPCCore's body (never a raider, a stand-in, an enclave person or a stranger) whose
        /// body is at home right now (round hl1's AtHome, checked live). A pure read.
        /// </summary>
        public static bool AppliesTo(EntityAlive self)
        {
            return IsMember(self) && UAI.UAIConsiderationScNotHomeMember.AtHome(self.position);
        }

        /// <summary>R3: one of the community's members on 0-XNPCCore's body (not a raider, a stand-in, an enclave person or a stranger), anywhere. A pure read.</summary>
        public static bool IsMember(EntityAlive self)
        {
            return self != null && ScRegistry.Loaded && ScXnpc.IsBody(self) && !ScXnpc.IsRaider(self) && ScRegistry.GetMember(self.entityId) != null;
        }

        // ------------------------------------------------------------------ the pure reads (the considerations) ----

        private static long Key(int self, int target) { return ((long)self << 32) | (uint)target; }

        /// <summary>
        /// The shot has this enemy's beat: SC found no point inside the base to back off to from it less than
        /// BackOffNoPointSeconds ago (per target), or the member is in a beat against every enemy (the re-review, R1: her last
        /// back-off gained nothing - F1 - or a wide step's shooting beat, round k5; HandToShot). ScArms reads it too (F7).
        /// </summary>
        public static bool NoPointFor(EntityAlive self, EntityAlive target)
        {
            return self != null && target != null && (TargetNoPoint(self, target) || InBeat(self));
        }

        /// <summary>The per-target verdict alone: no point inside the base to back off to from target (MarkNoPoint). A pure read.</summary>
        private static bool TargetNoPoint(EntityAlive self, EntityAlive target)
        {
            float until;
            return noPointUntil.TryGetValue(Key(self.entityId, target.entityId), out until) && Time.time < until;
        }

        /// <summary>R1: the member is in a shooting beat against every enemy (HandToShot). A pure read.</summary>
        public static bool InBeat(EntityAlive self)
        {
            float until;
            return self != null && noPointUntil.TryGetValue(Key(self.entityId, AnyTarget), out until) && Time.time < until;
        }

        /// <summary>R4: 0-XNPCCore says she is reloading (SdcsReloading). A pure read.</summary>
        internal static bool Reloading(EntityAlive self)
        {
            return self != null && self.Buffs != null && self.Buffs.GetCustomVar(ReloadingCvar) >= 0.5f;
        }

        /// <summary>
        /// ScBackOffRoom and the task's Start: the back-off from target waits - a per-target no point, or a member-wide beat
        /// (R1); reloadPass (R4, BackupRanged): a member-wide beat lets it through while she reloads. A pure read.
        /// </summary>
        public static bool Blocked(EntityAlive self, EntityAlive target, bool reloadPass)
        {
            if (self == null || target == null) return false;
            if (TargetNoPoint(self, target)) return true;
            // round hr1: in a last stand no reload pass - its beat is the member's, one probe every StandBackOffSeconds
            return InBeat(self) && !(reloadPass && Reloading(self) && !ScPosture.InStand(self));
        }

        /// <summary>
        /// The Fable review (F1), member-wide since the re-review (R1): the shot has the next seconds against EVERY enemy -
        /// ScBackOffRoom scores both of 0-XNPCCore's back-offs 0 for her until then (never shortening a window already set;
        /// R4: BackupRanged and ScKiteWide are let through while she reloads). The gain test, and a wide step that ends inside
        /// StandOffMin (round k5: step, shoot, step). target: only that the beat has one.
        /// </summary>
        internal static void HandToShot(EntityAlive self, EntityAlive target, float seconds)
        {
            if (self == null || target == null || seconds <= 0f) return;
            long k = Key(self.entityId, AnyTarget);
            float until = Time.time + seconds, had;
            if (noPointUntil.TryGetValue(k, out had) && had >= until) return;
            noPointUntil[k] = until;
            handed++;
        }

        private static object wideCheckedPkg, wideCheckedWorld;

        /// <summary>
        /// The Fable review of k5 (finding 4): Config/utilityai.xml appends ScKiteWide behind an xpath() guard on 0-XNPCCore's
        /// BackupKeepDistance task, so a 0-XNPCCore update could skip it without a word. Once per parsed package and world
        /// (ScCommunity.ApplyAiPackage calls it beside its own check that SCResident is registered): a warning when SCResident
        /// has no ScKiteWide action - a badly hurt member with a gun then falls back to the 2.5 m back-off. Main thread.
        /// </summary>
        internal static void CheckWideAction(UAI.UAIPackage pkg)
        {
            try
            {
                World w = GameManager.Instance != null ? GameManager.Instance.World : null;
                if (pkg == null || (ReferenceEquals(pkg, wideCheckedPkg) && ReferenceEquals(w, wideCheckedWorld))) return;
                wideCheckedPkg = pkg;
                wideCheckedWorld = w;
                List<UAI.UAIAction> actions = pkg.GetActions();
                if (actions != null)
                    for (int i = 0; i < actions.Count; i++)
                        if (actions[i] != null && actions[i].Name == WideBackOffAction) return;
                ScLog.Once("no-kite-wide", "ai_package " + ScCommunity.ResidentPackage + " has no " + WideBackOffAction + " action - Config/utilityai.xml's"
                           + " xpath() guard skipped it (0-XNPCCore's BackupKeepDistance no longer runs Voice Control's BackupFromTargetPaced?): a"
                           + " badly hurt member with nothing to heal with and a gun falls back to the " + F1(ScConfig.BackupKeepDistanceMax)
                           + " m back-off (BackupKeepDistance) and the shot - no " + F1(ScConfig.StandOffMin) + " m wide band (round k5)");
            }
            catch (Exception ex)
            {
                ScLog.Once("backoff-wide-check", ex);
            }
        }

        /// <summary>
        /// Round k5: her "no point" verdicts in a row (BoundStart's; a pick that worked clears it), counted only while the last
        /// is under StreakSeconds old - ScRetreat's cornered-away rule for KITE away from home reads it. A pure read.
        /// </summary>
        internal static int NoPointStreak(EntityAlive self)
        {
            Last l;
            if (self == null || !lasts.TryGetValue(self.entityId, out l)) return 0;
            return Time.time - l.NoPointAt <= StreakSeconds ? l.NoPointStreak : 0;
        }

        /// <summary>
        /// Round k5 (the final Fable check's C1): would a step back from t exist now - the same pick BoundStart makes (inside the
        /// base at home with BackOffHome, within StandOffFollowerLeash of you for KITE away from home, else k2b's short free
        /// steps), up to 10 m? R2 asks it about once a second while she holds her melee weapon: on melee neither back-off scores,
        /// so nothing else renews "no room". Nothing is written but the shared scratch lists (main thread, X5 only).
        /// </summary>
        internal static bool HasRoom(EntityAlive self, EntityAlive t, out string why)
        {
            why = null;
            if (self == null || t == null) { why = "no target"; return false; }
            EntityPlayer leader = null;
            ScRetreat.Region region = ScRetreat.Region.Free;
            if (UAI.UAIConsiderationScNotHomeMember.AtHome(self.position)) region = ScConfig.BackOffHome ? ScRetreat.Region.Home : ScRetreat.Region.Free;
            else if (ScRetreat.KitesAway(self, out leader)) region = ScRetreat.Region.Leader;
            Last l;
            lasts.TryGetValue(self.entityId, out l);
            Vector3 p;
            float step, angle;
            return Pick(self, t, 10f, region, leader, l, out p, out step, out angle, out why);
        }

        // ------------------------------------------------------------------ the pick ----

        /// <summary>The direction straight away from the threat (XZ); her own back when they stand on top of each other.</summary>
        internal static Vector3 AwayDir(EntityAlive self, Vector3 from)
        {
            Vector3 rel = self.position - from;
            rel.y = 0f;
            Vector3 dir = rel.sqrMagnitude > 0.01f ? rel.normalized : -self.GetForwardVector();
            dir.y = 0f;
            return dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.forward;
        }

        /// <summary>
        /// The awake, living enemies within r of her (SCore's IsEnemy), the threat's place first (index 0), into the shared list
        /// the candidates read. Gathered once per pick.
        /// </summary>
        private static void Gather(EntityAlive self, EntityAlive threat, float r)
        {
            enemies.Clear();
            enemies.Add(threat.position);
            World w = self.world;
            if (w == null) return;
            near.Clear();
            w.GetEntitiesInBounds(typeof(EntityAlive), new Bounds(self.position, new Vector3(2f * r, 2f * r, 2f * r)), near);
            try
            {
                for (int i = 0; i < near.Count; i++)
                {
                    EntityAlive a = near[i] as EntityAlive;
                    if (a == null || a == self || a == threat || a.IsDead() || a.IsSleeping) continue;
                    if (!EntityTargetingUtilities.IsEnemy(self, a)) continue;
                    enemies.Add(a.position);
                }
            }
            finally { near.Clear(); }
        }

        /// <summary>
        /// One step: walk s along d from me. Valid (round k1): a floor within MaxDy of her level (yRef); inside her bound - at home
        /// (round hl1's AtHome) for region Home, within StandOffFollowerLeash of leader for region Leader (rt1's follower away
        /// from home), no bound for region Free (round k2b: a back-off to heal away from home, its steps short); standable and not
        /// on her unreachable list; out of reach (BackupKeepDistanceMax, XZ) of every enemy
        /// gathered, the threat included; the straight walk passing no other enemy ahead of her within PassClear; a floor under
        /// that walk every FloorStep, each within 1 cell of the last (F6: no pit, drop or ledge on the way); and no block in that
        /// walk chest high (ScShelter.ClearLine with glass and bars: she backs into the room she is in, never through a shut door).
        /// The enemies are Gather's list (index 0 the threat). why: the first rule it fails.
        /// </summary>
        internal static bool Candidate(World w, EntityAlive self, Vector3 me, Vector3 d, float s, int yRef, ScRetreat.Region region, EntityPlayer leader,
                                       List<Vector3> foes, out Vector3 pc, out string why)
        {
            pc = Vector3.zero;
            why = null;
            Vector3 p = me + d * s;
            int x = Mathf.FloorToInt(p.x), z = Mathf.FloorToInt(p.z), y;
            if (!ScCommunity.FindStandable(w, x, z, yRef, out y) || Math.Abs(y - yRef) > MaxDy) { why = "no floor near her level"; return false; }
            pc = new Vector3(x + 0.5f, y, z + 0.5f);
            if (region == ScRetreat.Region.Leader)
            {
                if (leader == null || ScRunners.XZ(pc, leader.position) > ScConfig.StandOffFollowerLeash)
                {
                    why = "farther than " + ScConfig.StandOffFollowerLeash.ToString("0", CultureInfo.InvariantCulture) + " m from you";
                    return false;
                }
            }
            else if (region != ScRetreat.Region.Free && !UAI.UAIConsiderationScNotHomeMember.AtHome(pc)) { why = "outside the home zone + margin"; return false; }
            Vector3i cell = new Vector3i(x, y, z);
            if (!Standable(w, self, cell, out why)) return false;          // round cb1: the reason in the log (a wreck beside it, a model under it)
            float reach = ScConfig.BackupKeepDistanceMax;
            for (int i = 0; i < foes.Count; i++)
                if (ScRunners.XZ(foes[i], pc) < reach) { why = i == 0 ? "still within reach of it" : "within reach of another enemy"; return false; }
            // the walk passes an enemy AHEAD of her (one beside or behind her - two on her at once - is what she walks away from)
            Vector3 walk = pc - me;
            walk.y = 0f;
            float len = walk.magnitude;
            if (len > 0.01f)
            {
                walk /= len;
                for (int i = 1; i < foes.Count; i++)
                {
                    Vector3 v = foes[i] - me;
                    v.y = 0f;
                    if (Vector3.Dot(v, walk) > PassAhead && ScRunners.PassDistance(me, pc, foes[i]) < PassClear) { why = "the walk passes another enemy"; return false; }
                }
            }
            if (!FloorUnderWalk(w, me, pc, yRef, y)) { why = "no floor under the walk (a pit, a drop or a ledge)"; return false; }
            Vector3 up = new Vector3(0f, WalkLineHeight, 0f);
            if (!ScShelter.ClearLine(w, me + up, pc + up, true)) { why = "a wall, door or glass in the way"; return false; }
            return true;
        }

        /// <summary>
        /// F6: a standable floor under the straight walk from me to the point every FloorStep, each sample within 1 cell of the
        /// last (her own level first, then one down, then one up), and the point's own floor within 1 cell of the last sample.
        /// </summary>
        private static bool FloorUnderWalk(World w, Vector3 me, Vector3 pc, int yRef, int yEnd)
        {
            Vector3 walk = pc - me;
            walk.y = 0f;
            float len = walk.magnitude;
            int n = Mathf.CeilToInt(len / FloorStep);
            int yPrev = yRef, lx = Mathf.FloorToInt(me.x), lz = Mathf.FloorToInt(me.z);
            for (int k = 1; k < n; k++)
            {
                Vector3 q = me + walk * (k / (float)n);
                int x = Mathf.FloorToInt(q.x), z = Mathf.FloorToInt(q.z);
                if (x == lx && z == lz) continue;
                lx = x;
                lz = z;
                if (ScCommunity.Standable(w, x, yPrev, z)) continue;
                if (ScCommunity.Standable(w, x, yPrev - 1, z)) { yPrev--; continue; }
                if (ScCommunity.Standable(w, x, yPrev + 1, z)) { yPrev++; continue; }
                return false;
            }
            return Math.Abs(yEnd - yPrev) <= 1;
        }

        /// <summary>
        /// Round cb1: a cell she may be sent to - not on her unreachable list and a real floor (ScCommunity.GoodPost relaxed: two air
        /// cells over terrain or a solid block, never a model - a bed, a coffin, a truck - and not in or beside a wreck's hull). why:
        /// "not standable (" + GoodPost's reason + ")", so "last: not standable" in the log says what stood there.
        /// </summary>
        internal static bool Standable(World w, EntityAlive self, Vector3i cell, out string why)
        {
            why = null;
            if (ScStuck.Unreachable(self.entityId, cell)) { why = "not standable (on " + ScRetreat.Her(self) + " unreachable list)"; return false; }
            string gw;
            if (!ScCommunity.GoodPost(w, cell, false, out gw)) { why = "not standable (" + gw + ")"; return false; }
            return true;
        }

        // ------------------------------------------------------------------ round cb1: the shooting back-off goes far ----

        /// <summary>Round cb1: a far run's point is tried at these shares of her far distance, longest first (each beyond k1's longest step).</summary>
        private static readonly float[] FarShares = { 1f, 0.75f, 0.5f };
        /// <summary>Round cb1: a far point's floor within this many cells of her level (a path, not a straight walk, reaches it).</summary>
        private const int FarMaxDy = 3;
        /// <summary>Round cb1: no enemy within this of a far point (ScShelter.PassClear: she keeps that far from the threat).</summary>
        private const float FarClear = ScShelter.PassClear;
        /// <summary>Round cb1: a far run never goes past this, whatever the settings.</summary>
        private const float FarCap = 30f;
        /// <summary>Round cb1: a far run's path must end at least this much farther from the threat than she is when it is checked.</summary>
        private const float FarGainNeed = 3f;
        /// <summary>Round cb1 (hr1's CheckPath rules): a far run's path passes no enemy within PathPassClear once PathPassFrom of it is walked.</summary>
        private const float PathPassClear = 1.5f, PathPassFrom = 2f;
        /// <summary>Round cb1: far runs whose path failed this many times within LongFailWindow are off for LongOffSeconds (k1's steps then).</summary>
        private const int LongFailMax = 3;
        private const float LongFailWindow = 10f, LongOffSeconds = 10f;
        private static int farRuns, farFailed, farTimeouts, runs;

        /// <summary>
        /// Round cb1 (the user: "go much further away, distance based on shooting skill"): how far a member backs off to shoot from -
        /// BackOffBaseMeters + BackOffPerShootingStar x her Shooting stars (1 to 7), never past her engage range minus 1 m
        /// (ScShooting) or her gun's range minus 1 m (she must still shoot from there), nor FarCap. 0: not a member, or off. A read.
        /// </summary>
        internal static float FarMeters(EntityAlive self)
        {
            if (self == null || ScConfig.BackOffBaseMeters <= 0f || !ScRegistry.Loaded) return 0f;
            Member m = ScRegistry.GetMember(self.entityId);
            if (m == null) return 0f;
            float d = ScConfig.BackOffBaseMeters + ScConfig.BackOffPerShootingStar * m.Stars(Skill.Shooting);
            float cap = FarCap;
            if (ScShooting.Applies(self))
            {
                string src;
                cap = Mathf.Min(cap, ScShooting.EngageRange(ScShooting.Stars(self, out src)) - 1f);
            }
            Loadout lo = ScLoadout.Current(self);
            ItemClass gun = lo != null && lo.HasRanged ? ItemClass.GetItemClass(lo.Ranged, true) : null;
            ItemActionRanged ra = ScAmmo.RangedAction(gun);
            if (ra != null && ra.Range > 1f) cap = Mathf.Min(cap, ra.Range - 1f);
            return Mathf.Max(0f, Mathf.Min(d, cap));
        }

        /// <summary>
        /// Round cb1: her ScKiteWide run at home against t is going (ScKiteWide's band keeps it to its end, sight or not). Round cb1
        /// (review CB1-m8): every bounded ScKiteWide run at home, far or not - during "far runs off" too, so k1's steps keep the band.
        /// Round cb1 (re-check CB1R-n3): named WideRunning (was FarRunning); every such run has a deadline (CB1R-m1). A pure read.
        /// </summary>
        internal static bool WideRunning(EntityAlive self, EntityAlive t)
        {
            Last l;
            return self != null && t != null && lasts.TryGetValue(self.entityId, out l) && l.Running && l.Home && l.Wide && l.Target == t.entityId;
        }

        /// <summary>
        /// Round cb1: UAIConsiderationScPosture action="wide" (SCResident's ScKiteWide) for a member at home who is not kiting hurt -
        /// the shooting back-off: with BackOffHome and BackOffBaseMeters on, an enemy within BackupKeepDistanceMax (3D, the band
        /// BackupKeepDistance used; runningOnT: her far run against it, held to its end), a posture that allows a back-off from it
        /// (ScPosture.AllowsBackOff) and her pair's gun with rounds. ScKiteWide (8.2) then beats 0-XNPCCore's BackupKeepDistance
        /// (8), whose own 4 m band would cut the run short. A pure read (the cheap tests first).
        /// </summary>
        internal static bool FarBand(EntityAlive self, EntityAlive t, bool runningOnT)
        {
            if (self == null || t == null || !ScConfig.BackOffHome || ScConfig.BackOffBaseMeters <= 0f) return false;
            if (!(runningOnT && WideRunning(self, t)))
            {
                float band = ScConfig.BackupKeepDistanceMax;
                if ((t.position - self.position).sqrMagnitude > band * band) return false;
            }
            return AppliesTo(self) && ScPosture.AllowsBackOff(self, t) && ScRetreat.GunReady(self);
        }

        /// <summary>
        /// Round cb1: a far point s along d - a floor within FarMaxDy cells of her level, inside the base, a real floor she may be sent
        /// to (Standable), no enemy within FarClear of it, a line of fire from it to the threat (round cb1 review), and its straight
        /// line passing no other enemy ahead of her within PassClear.
        /// No clear-line or floor-under-walk test: the navigator's path is what she runs, checked once she has it (CheckLongPath).
        /// </summary>
        private static bool CandidateFar(World w, EntityAlive self, Vector3 me, Vector3 d, float s, int yRef, List<Vector3> foes, out Vector3 pc, out string why)
        {
            pc = Vector3.zero;
            why = null;
            Vector3 p = me + d * s;
            int x = Mathf.FloorToInt(p.x), z = Mathf.FloorToInt(p.z), y;
            if (!ScCommunity.FindStandable(w, x, z, yRef, out y) || Math.Abs(y - yRef) > FarMaxDy) { why = "no floor near her level"; return false; }
            pc = new Vector3(x + 0.5f, y, z + 0.5f);
            if (!UAI.UAIConsiderationScNotHomeMember.AtHome(pc)) { why = "outside the home zone + margin"; return false; }
            if (!Standable(w, self, new Vector3i(x, y, z), out why)) return false;
            for (int i = 0; i < foes.Count; i++)
                if (ScRunners.XZ(foes[i], pc) < FarClear) { why = "an enemy within " + F1(FarClear) + " m of it"; return false; }
            // round cb1 (review CB1-M1): she shoots from there - a clear line chest high from it to the threat's chest (foes[0]), over
            // terrain and solid blocks (glass and bars stop it too, as SCore's shot test)
            Vector3 chest = new Vector3(0f, WalkLineHeight, 0f);
            if (foes.Count > 0 && !ScShelter.ClearLine(w, pc + chest, foes[0] + chest, true)) { why = "no line of fire to the threat from it"; return false; }
            Vector3 walk = pc - me;
            walk.y = 0f;
            float len = walk.magnitude;
            if (len > 0.01f)
            {
                walk /= len;
                for (int i = 1; i < foes.Count; i++)
                {
                    Vector3 v = foes[i] - me;
                    v.y = 0f;
                    if (Vector3.Dot(v, walk) > PassAhead && ScRunners.PassDistance(me, pc, foes[i]) < PassClear) { why = "the way passes another enemy"; return false; }
                }
            }
            return true;
        }

        /// <summary>
        /// Round cb1: a far run's path, once the navigator has it (not the path the last action left, not while the path thread still
        /// works on hers) - every point inside the base, no enemy within PathPassClear once PathPassFrom of it is walked, and its end at
        /// least FarGainNeed farther from the threat than she is now and out of every enemy's reach. A path that ends short of the
        /// point is the farthest she can reach: kept. Null while it waits or when it passes (the deadline then follows its length);
        /// else why it fails.
        /// </summary>
        private static string CheckLongPath(EntityAlive self, Last l, EntityAlive t, float now)
        {
            GamePath.PathNavigate nav = self.getNavigator();
            GamePath.PathEntity path = nav != null ? nav.getPath() : null;
            if (path == null || !path.HasPoints() || path.getCurrentPathLength() <= 0) return null;
            if (ReferenceEquals(path, l.StalePath)) return null;
            if (GamePath.PathFinderThread.Instance != null && GamePath.PathFinderThread.Instance.IsCalculatingPath(self.entityId)) return null;
            l.PathChecked = true;
            l.StalePath = null;
            Gather(self, t, 40f);
            try
            {
                float len = 0f;
                Vector3 prev = self.position;
                int n = path.getCurrentPathLength();
                for (int i = Math.Max(0, path.getCurrentPathIndex()); i < n; i++)
                {
                    GamePath.PathPoint pt = path.getPathPointFromIndex(i);
                    if (pt == null) break;
                    Vector3 q = pt.projectedLocation;
                    len += Vector3.Distance(prev, q);
                    prev = q;
                    if (!UAI.UAIConsiderationScNotHomeMember.AtHome(q)) return "it leaves the base at " + ScRetreat.Cell(q);
                    if (len < PathPassFrom) continue;
                    for (int f = 0; f < enemies.Count; f++)
                        if (ScRunners.XZ(enemies[f], q) < PathPassClear && Mathf.Abs(enemies[f].y - q.y) <= 2f) return "it passes an enemy at " + ScRetreat.Cell(enemies[f]);
                }
                Vector3 end = path.GetEndPos();
                float gapEnd = ScRunners.XZ(end, t.position), gapNow = ScRunners.XZ(self.position, t.position);
                if (gapEnd < gapNow + FarGainNeed) return "it ends only " + F1(gapEnd) + " m from " + Who(t) + " (" + F1(gapNow) + " m now)";
                for (int f = 0; f < enemies.Count; f++)
                    if (ScRunners.XZ(enemies[f], end) < ScConfig.BackupKeepDistanceMax) return "it ends within reach of an enemy";
                float shortBy = ScRunners.XZ(end, l.Point);
                l.Deadline = now + len / Mathf.Max(1f, ScConfig.RunnerRunMps) * 1.6f + 3f;
                ScLog.Verbose(ScRetreat.Name(self) + "'s run back is " + F1(len) + " m on the navigator's path" + (shortBy > 1.5f ? ", ending " + F1(shortBy) + " m short of "
                              + ScRetreat.Cell(l.Point) + " - the farthest " + ScRetreat.She(self) + " can reach" : "") + " (round cb1)");
                return null;
            }
            finally { enemies.Clear(); }
        }

        /// <summary>Round cb1: a far run's path failed - after LongFailMax within LongFailWindow, k1's steps only for LongOffSeconds.</summary>
        private static void LongFailed(EntityAlive self, Last l, float now)
        {
            if (now - l.LongFailAt > LongFailWindow) l.LongFails = 0;
            l.LongFails++;
            l.LongFailAt = now;
            farFailed++;
            if (l.LongFails < LongFailMax) return;
            l.LongFails = 0;
            l.LongOffUntil = now + LongOffSeconds;
            Patches_Xnpc.Count("backoff-far-off");
            ScLog.Verbose(ScRetreat.Name(self) + ": " + LongFailMax + " far runs back had no usable path - k1's steps (" + ScConfig.BackOffStepsText() + " m) only for "
                          + F1(LongOffSeconds) + " s (round cb1)");
        }

        /// <summary>
        /// Round cb1 (item 1, "run rather than briskly walk"): the Update postfix - a member's bounded run at home against its own
        /// target runs at her run speed (ScRetreat.RunSpeed, as rt1's shelter walk): 0-XNPCCore's task caps its pace at 2.4 m/s
        /// (max_speed), so the navigator's speed is set again after its own Update, every tick of the run, once the run's own path is
        /// there. BackupKeepDistance, BackupRanged (the reload back-off) and ScKiteWide alike.
        /// </summary>
        private static void RunPace(UAI.Context ctx)
        {
            if (ctx == null) return;
            EntityAlive self = ctx.Self;
            Last l;
            if (self == null || self.isEntityRemote || !lasts.TryGetValue(self.entityId, out l) || !l.Running || !l.Bounded || !l.Home) return;
            EntityAlive t = UAI.UAIUtils.ConvertToEntityAlive(ctx.ActionData.Target);
            if (t == null || t.entityId != l.Target) return;
            GamePath.PathNavigate nav = self.getNavigator();
            GamePath.PathEntity path = nav != null ? nav.getPath() : null;
            if (path == null || path.isFinished() || ReferenceEquals(path, l.StalePath)) return;
            nav.setMoveSpeed(ScRetreat.RunSpeed(self));
            if (l.RunCounted) return;
            l.RunCounted = true;
            runs++;
            Patches_Xnpc.Count("backoff-run");
        }

        // ------------------------------------------------------------------ round cb1: a short step to open ground ----

        /// <summary>Round cb1: a step to open ground goes at most this far (ScStepClear; the shelter's step off a model too).</summary>
        internal const float StepMax = 3.5f;
        private static readonly float[] StepRings = { 1.5f, 2.5f, 3.5f };
        /// <summary>
        /// Round cb1: a step's landing keeps StepEnemyClear from every enemy and its straight walk StepWalkClear; it is reached within
        /// StepArrive; it may take StepSeconds; the request waits StepWantSeconds for ScStepClear; after a step (or none found) the
        /// next waits StepCooldown.
        /// </summary>
        private const float StepEnemyClear = 1.5f, StepWalkClear = 1f, StepArrive = 0.7f, StepSeconds = 3.5f, StepWantSeconds = 1.5f, StepCooldown = 4f;

        private sealed class StepState
        {
            public Vector3 Point;
            public float WantedUntil, Deadline, CooldownUntil;
            public bool Walking, Retried;
            public string Why = "";
        }

        private static readonly Dictionary<int, StepState> stepStates = new Dictionary<int, StepState>();
        private static int stepsAsked, stepsReached, stepsNone;

        /// <summary>
        /// Round cb1: she stands on a model (furniture, a coffin: a colliding model block in her feet cell, or one under her feet) or
        /// inside an oversized block's hull (a truck's bed: its cells read as air) - what, else false. Doors and hatches are floor.
        /// A read.
        /// </summary>
        internal static bool OnObstacle(World w, Vector3 pos, out string what)
        {
            what = null;
            if (w == null) return false;
            Vector3i feet = World.worldToBlockPos(pos);
            if (ScStuck.InsideOversized(feet))
            {
                string n;
                ScStuck.NearOversized(feet, out n);
                what = "inside " + (n ?? "an oversized block");
                return true;
            }
            BlockValue at = w.GetBlock(feet);
            if (ModelFloor(at, true)) { what = "on " + at.Block.GetBlockName(); return true; }
            BlockValue below = w.GetBlock(new Vector3i(feet.x, feet.y - 1, feet.z));
            if (at.isair && ModelFloor(below, false)) { what = "on " + below.Block.GetBlockName(); return true; }
            return false;
        }

        private static bool ModelFloor(BlockValue bv, bool colliding)
        {
            if (bv.isair) return false;
            Block b = bv.Block;
            if (b == null || !(b.shape is BlockShapeModelEntity) || b.HasTag(BlockTags.Door)) return false;
            return !colliding || b.IsCollideMovement;
        }

        /// <summary>
        /// Round cb1: open ground for a short step - on StepRings up to maxR, 12 directions starting straight away from "from" (any
        /// order without one): a floor 1 up to 3 down from her level, inside the base, not in bad, a real floor she may be sent to
        /// with nothing round it (GoodPost strict: no furniture or wreck beside it, room on most sides; a relaxed real floor only
        /// when no strict one is free), every enemy StepEnemyClear off it, a straight walk with a floor under it, no wall or glass
        /// chest high and no enemy within StepWalkClear of it, and with needLine a clear line from it to from's chest (a swing).
        /// Best: the shortest, with the fewest enemies within 3 m of it. why: what it is, or why none. A read (the shared lists).
        /// </summary>
        internal static bool StepSpot(EntityAlive self, EntityAlive from, bool needLine, float maxR, List<Vector3i> bad, out Vector3 spot, out string why)
        {
            spot = Vector3.zero;
            why = null;
            World w = self != null ? self.world : null;
            if (w == null) { why = "no world"; return false; }
            Vector3 me = self.position;
            int yRef = Mathf.FloorToInt(me.y + 0.05f);
            Vector3 dir0 = from != null ? AwayDir(self, from.position) : self.GetForwardVector();
            dir0.y = 0f;
            if (dir0.sqrMagnitude < 0.0001f) dir0 = Vector3.forward;
            dir0.Normalize();
            Vector3 up = new Vector3(0f, WalkLineHeight, 0f);
            Vector3 chest = from != null ? from.position + up : Vector3.zero;
            GatherAll(self, maxR + 4f, from);                                 // round cb1 (review CB1-M2): "from" apart
            string lastWhy = "no floor near " + ScRetreat.Her(self) + " level";
            try
            {
                for (int pass = 0; pass < 2; pass++)
                {
                    bool strict = pass == 0;
                    float best = float.MaxValue;
                    for (int ri = 0; ri < StepRings.Length; ri++)
                    {
                        float r = StepRings[ri];
                        if (r > maxR + 0.01f) continue;
                        for (int k = 0; k < 12; k++)
                        {
                            // straight away first, then 30 degrees either side in turn
                            float ang = (k % 2 == 1 ? 1f : -1f) * ((k + 1) / 2) * 30f;
                            Vector3 p = me + Quaternion.Euler(0f, ang, 0f) * dir0 * r;
                            int x = Mathf.FloorToInt(p.x), z = Mathf.FloorToInt(p.z), y;
                            if (!ScCommunity.FindStandable(w, x, z, yRef, out y) || y - yRef > 1 || yRef - y > 3) continue;
                            Vector3i cell = new Vector3i(x, y, z);
                            Vector3 pc = new Vector3(x + 0.5f, y, z + 0.5f);
                            if (!UAI.UAIConsiderationScNotHomeMember.AtHome(pc)) { lastWhy = "outside the home zone + margin"; continue; }
                            if (bad != null && bad.Contains(cell)) { lastWhy = "a spot " + ScRetreat.She(self) + " could not reach"; continue; }
                            if (ScStuck.Unreachable(self.entityId, cell)) { lastWhy = "on " + ScRetreat.Her(self) + " unreachable list"; continue; }
                            string gw;
                            if (!ScCommunity.GoodPost(w, cell, strict, out gw)) { lastWhy = gw; continue; }
                            // round cb1 (review CB1-M2): the landing keeps StepEnemyClear from every enemy, "from" included; the walk is
                            // tested only against enemies AHEAD of her along it (as k1's Candidate), "from" among them - one at her elbow,
                            // or the one she steps away from, no longer fails every spot
                            bool near = from != null && ScRunners.XZ(from.position, pc) < StepEnemyClear;
                            int crowd = from != null && ScRunners.XZ(from.position, pc) < 3f ? 1 : 0;
                            Vector3 sw = pc - me;
                            sw.y = 0f;
                            float slen = sw.magnitude;
                            if (slen > 0.01f) sw /= slen;
                            if (!near && from != null && slen > 0.01f)
                            {
                                // "from" too, only when the step would go through it (ahead of her along the walk)
                                Vector3 vf = from.position - me;
                                vf.y = 0f;
                                if (Vector3.Dot(vf, sw) > PassAhead && ScRunners.PassDistance(me, pc, from.position) < StepWalkClear) near = true;
                            }
                            for (int i = 0; i < enemies.Count && !near; i++)
                            {
                                float fd = ScRunners.XZ(enemies[i], pc);
                                if (fd < StepEnemyClear) { near = true; break; }
                                Vector3 v = enemies[i] - me;
                                v.y = 0f;
                                if (slen > 0.01f && Vector3.Dot(v, sw) > PassAhead && ScRunners.PassDistance(me, pc, enemies[i]) < StepWalkClear) { near = true; break; }
                                if (fd < 3f) crowd++;
                            }
                            if (near) { lastWhy = "an enemy at it or ahead on the way"; continue; }
                            if (!StepFloor(w, me, pc, yRef, y)) { lastWhy = "no floor under the step"; continue; }
                            if (!ScShelter.ClearLine(w, me + up, pc + up, true)) { lastWhy = "a wall, door or glass in the way"; continue; }
                            if (needLine && !ScShelter.ClearLine(w, pc + up, chest, true)) { lastWhy = "no clear swing at " + Who(from) + " from there"; continue; }
                            float score = r + 2f * crowd;
                            if (score < best) { best = score; spot = pc; }
                        }
                    }
                    if (best < float.MaxValue)
                    {
                        why = (strict ? "open ground" : "a real floor") + (needLine ? " with a clear swing at " + Who(from) : "");
                        return true;
                    }
                }
            }
            finally { enemies.Clear(); }
            why = "no open ground within " + F1(maxR) + " m (last: " + lastWhy + ")";
            return false;
        }

        /// <summary>
        /// Round cb1: the awake, living enemies within r of her (SCore's IsEnemy, not inert) into the shared list - no threat first.
        /// Round cb1 (review CB1-M2): but not "except" (the step tests that one apart).
        /// </summary>
        private static void GatherAll(EntityAlive self, float r, EntityAlive except = null)
        {
            enemies.Clear();
            World w = self.world;
            if (w == null) return;
            near.Clear();
            w.GetEntitiesInBounds(typeof(EntityAlive), new Bounds(self.position, new Vector3(2f * r, 2f * r, 2f * r)), near);
            try
            {
                for (int i = 0; i < near.Count; i++)
                {
                    EntityAlive a = near[i] as EntityAlive;
                    if (a == null || a == self || a == except || a.IsDead() || a.IsSleeping) continue;
                    if (!EntityTargetingUtilities.IsEnemy(self, a) || global::Harmony.ZombieFeatures.InertEntity.IsInert(a)) continue;
                    enemies.Add(a.position);
                }
            }
            finally { near.Clear(); }
        }

        /// <summary>
        /// Round cb1 (review CB1-m7): FloorUnderWalk for a step - the first new cell may be up to 3 lower than hers (down off a truck's
        /// bed or cab, stacked coffins), every later one within 1 cell of the last, and the landing within 1 of the last sample (3 down
        /// when the step has no sample between).
        /// </summary>
        private static bool StepFloor(World w, Vector3 me, Vector3 pc, int yRef, int yEnd)
        {
            Vector3 walk = pc - me;
            walk.y = 0f;
            float len = walk.magnitude;
            int n = Mathf.CeilToInt(len / FloorStep);
            int yPrev = yRef, lx = Mathf.FloorToInt(me.x), lz = Mathf.FloorToInt(me.z);
            bool first = true;
            for (int k = 1; k < n; k++)
            {
                Vector3 q = me + walk * (k / (float)n);
                int x = Mathf.FloorToInt(q.x), z = Mathf.FloorToInt(q.z);
                if (x == lx && z == lz) continue;
                lx = x;
                lz = z;
                int down = first ? 3 : 1;
                first = false;
                bool ok = false;
                for (int dy = 0; dy >= -down && !ok; dy--)
                    if (ScCommunity.Standable(w, x, yPrev + dy, z)) { yPrev += dy; ok = true; }
                if (!ok && ScCommunity.Standable(w, x, yPrev + 1, z)) { yPrev++; ok = true; }
                if (!ok) return false;
            }
            return yEnd - yPrev <= 1 && yPrev - yEnd <= (first ? 3 : 1);
        }

        /// <summary>Round cb1 (review CB1-m4): a body's step state goes with the body (ScPosture.Forget; entity ids are recycled).</summary>
        internal static void ForgetStep(int entityId) { stepStates.Remove(entityId); }

        /// <summary>
        /// Round cb1 (review CB1-M1, part c): the X5 tick's look at her bounded run at home - another UAI action took over (its own end,
        /// the gain test, the path check and its deadline clear Running in BoundUpdate first). The run is over; true when it gained
        /// (out of its enemy's reach and GainNeed farther than at its start): she holds that spot. Counted backoff-cut. Main thread.
        /// </summary>
        internal static bool RunCutWithGain(EntityAlive e)
        {
            Last l;
            // round cb1 (re-check CB1R-n1): ScKiteWide's runs only - BackupKeepDistance's own band cuts its runs at 3 m every time,
            // and a hold the 3 m break ends a tick later is only noise
            if (e == null || !lasts.TryGetValue(e.entityId, out l) || !l.Running || !l.Bounded || !l.Home || !l.Wide) return false;
            float now = Time.time;
            if (now - l.At < 0.5f) return false;                              // just started: its task may not be executing yet
            UAI.Context ctx = e.utilityAIContext;
            if (ctx == null) return false;
            UAI.ActionData ad = ctx.ActionData;                               // a struct: a copy to read
            if (ad.Executing && ad.Action != null && ad.Action.Name == l.Action)
            {
                EntityAlive at = UAI.UAIUtils.ConvertToEntityAlive(ad.Target);
                if (at != null && at.entityId == l.Target) return false;      // still her run
            }
            l.Running = false;
            l.StalePath = null;
            Patches_Xnpc.Count("backoff-cut");
            EntityAlive t = e.world != null ? e.world.GetEntity(l.Target) as EntityAlive : null;
            if (t == null || t.IsDead()) return false;
            float band = ScConfig.BackupKeepDistanceMax;
            return (t.position - e.position).sqrMagnitude > band * band && ScRunners.XZ(e.position, t.position) >= l.GapAtStart + GainNeed;
        }

        private static StepState StepOf(int id)
        {
            StepState s;
            if (!stepStates.TryGetValue(id, out s)) stepStates[id] = s = new StepState();
            return s;
        }

        /// <summary>
        /// Round cb1: ask for a short step to open ground (ScStepClear runs it) - her swing blocked in a last stand, or a fight with her
        /// standing on a model or in a wreck's hull. A member at home only; not while a step runs or within StepCooldown of the last.
        /// Logged once per request (found or not). True: a step is wanted or running.
        /// </summary>
        internal static bool RequestStep(EntityAlive self, EntityAlive from, bool needLine, string why)
        {
            if (self == null || self.isEntityRemote || !AppliesTo(self)) return false;
            float now = Time.time;
            StepState s = StepOf(self.entityId);
            // round cb1 (review CB1-m4): a step past its deadline is over (its body unloaded mid-step), and a request ScStepClear did not
            // take up in time (a higher action held her) waits StepCooldown as a step does
            if (s.Walking && now > s.Deadline) { s.Walking = false; s.CooldownUntil = s.Deadline + StepCooldown; }
            if (!s.Walking && s.WantedUntil > 0f && now >= s.WantedUntil) { s.CooldownUntil = Mathf.Max(s.CooldownUntil, s.WantedUntil + StepCooldown); s.WantedUntil = 0f; }
            if (s.Walking || now < s.WantedUntil) return true;
            if (now < s.CooldownUntil) return false;
            Vector3 spot;
            string sw;
            if (!StepSpot(self, from, needLine, StepMax, null, out spot, out sw))
            {
                s.CooldownUntil = now + StepCooldown;
                stepsNone++;
                Patches_Xnpc.Count("step-no-ground");
                ScLog.Verbose(ScRetreat.Name(self) + " would step to open ground (" + why + ") but finds none: " + sw + " (round cb1)");
                return false;
            }
            s.Point = spot;
            s.WantedUntil = now + StepWantSeconds;
            s.Walking = false;
            s.Retried = false;
            s.Why = why;
            stepsAsked++;
            Patches_Xnpc.Count("step-asked");
            ScLog.Out(ScRetreat.Name(self) + " steps to " + sw + " at " + ScRetreat.Cell(spot) + ", " + F1(ScRunners.XZ(self.position, spot)) + " m off, at a run: " + why + " (round cb1)");
            return true;
        }

        /// <summary>Round cb1: SCResident's ScStepClear scores - a step is asked for (not yet stale) or running, and she is at home. A pure read.</summary>
        internal static bool StepWanted(EntityAlive self)
        {
            StepState s;
            if (self == null || !stepStates.TryGetValue(self.entityId, out s)) return false;
            float now = Time.time;
            return (s.Walking ? now <= s.Deadline : now < s.WantedUntil) && UAI.UAIConsiderationScNotHomeMember.AtHome(self.position);
        }

        /// <summary>Round cb1: ScStepClear's Start - the path to the step's spot at her run (the old path dropped first). True: stepping.</summary>
        internal static bool StepStart(EntityAlive self)
        {
            StepState s;
            if (self == null || !stepStates.TryGetValue(self.entityId, out s)) return false;
            float now = Time.time;
            if (s.Walking) return now <= s.Deadline;                         // SCore re-started the task: the step goes on
            if (now >= s.WantedUntil) return false;
            s.Walking = true;
            s.Retried = false;
            s.WantedUntil = 0f;
            s.Deadline = now + StepSeconds;
            ScCommunity.DropPath(self);
            if (self.moveHelper != null) self.moveHelper.CanBreakBlocks = false;
            self.FindPath(s.Point, ScRetreat.RunSpeed(self), false, null);
            return true;
        }

        /// <summary>Round cb1: ScStepClear's Update - arrived, out of time or no path (after one retry) ends the step. True: it goes on.</summary>
        internal static bool StepTick(EntityAlive self)
        {
            StepState s;
            if (self == null || !stepStates.TryGetValue(self.entityId, out s) || !s.Walking) return false;
            float now = Time.time;
            if (ScRunners.XZ(self.position, s.Point) <= StepArrive && Mathf.Abs(self.position.y - s.Point.y) < 1.5f) { EndStep(self, s, "reached it", true); return false; }
            if (now > s.Deadline) { EndStep(self, s, "out of time (" + F1(StepSeconds) + " s)", false); return false; }
            GamePath.PathNavigate nav = self.getNavigator();
            if (nav != null && nav.noPathAndNotPlanningOne())
            {
                if (!s.Retried)
                {
                    s.Retried = true;
                    self.FindPath(s.Point, ScRetreat.RunSpeed(self), false, null);
                    return true;
                }
                EndStep(self, s, "no path to it", false);
                return false;
            }
            return true;
        }

        private static void EndStep(EntityAlive self, StepState s, string how, bool reached)
        {
            s.Walking = false;
            s.CooldownUntil = Time.time + StepCooldown;
            if (reached) { stepsReached++; Patches_Xnpc.Count("step-reached"); }
            else Patches_Xnpc.Count("step-ended");
            ScLog.Verbose(ScRetreat.Name(self) + "'s step to open ground at " + ScRetreat.Cell(s.Point) + " ends: " + how + " (" + s.Why + "; round cb1)");
        }

        /// <summary>Round cb1: ScStepClear stopped (done, or a higher action took her): the path goes, the next step waits StepCooldown.</summary>
        internal static void StepStopped(EntityAlive self)
        {
            if (self == null) return;
            StepState s;
            if (stepStates.TryGetValue(self.entityId, out s) && s.Walking)
            {
                s.Walking = false;
                s.CooldownUntil = Time.time + StepCooldown;
            }
            ScCommunity.DropPath(self);
            if (self.moveHelper != null) self.moveHelper.CanBreakBlocks = true;
        }

        /// <summary>
        /// The bounded point for her back-off from threat (C#: the task's Start, k2b's heal back-off, R2's room probe): the BackOffSteps up to
        /// maxStep, longest first; on each the seven directions; the valid point (Candidate) with the lowest score - its angle
        /// (25 degrees count 1) plus each other enemy within CrowdRadius of it - on the first step that has one. last (the bound
        /// task's per-member memory, else null) sets aside the points she just failed to reach. False with why: none at all.
        /// </summary>
        internal static bool Pick(EntityAlive self, EntityAlive threat, float maxStep, ScRetreat.Region region, EntityPlayer leader, Last last,
                                  out Vector3 point, out float step, out float angle, out string why)
        {
            bool longStep;
            return PickEx(self, threat, maxStep, region, leader, last, 0f, out point, out step, out angle, out longStep, out why);
        }

        /// <summary>
        /// Round cb1: Pick, with a far distance first (far above k1's longest step + 1 m, region Home only): the FarShares of it on the
        /// seven directions (CandidateFar, scored as k1's), the first share with a valid point wins (longStep true: its path is
        /// checked once the navigator has it); none - k1's steps as before. far 0: Pick exactly.
        /// </summary>
        internal static bool PickEx(EntityAlive self, EntityAlive threat, float maxStep, ScRetreat.Region region, EntityPlayer leader, Last last, float far,
                                    out Vector3 point, out float step, out float angle, out bool longStep, out string why)
        {
            point = Vector3.zero;
            step = 0f;
            angle = 0f;
            longStep = false;
            why = null;
            World w = self != null ? self.world : null;
            if (w == null || threat == null) { why = "no threat"; return false; }
            List<float> steps = ScConfig.BackOffStepList();
            float longest = LongestStep(maxStep);
            string farWhy = null;
            if (far > longest + 1f && region == ScRetreat.Region.Home)
            {
                Vector3 me0 = self.position;
                Vector3 dir0 = AwayDir(self, threat.position);
                int yRef0 = Mathf.FloorToInt(me0.y + 0.05f);
                float now0 = Time.time;
                Gather(self, threat, far + CrowdRadius + 2f);
                farWhy = "no floor near her level";
                for (int fi = 0; fi < FarShares.Length; fi++)
                {
                    float s = far * FarShares[fi];
                    if (s <= longest + 1f) continue;
                    float best = float.MaxValue;
                    for (int k = 0; k < Angles.Length; k++)
                    {
                        Vector3 d = Quaternion.Euler(0f, Angles[k], 0f) * dir0;
                        Vector3 pc;
                        string cw;
                        if (!CandidateFar(w, self, me0, d, s, yRef0, enemies, out pc, out cw)) { farWhy = cw; continue; }
                        if (last != null && IsBad(last, pc, now0)) { farWhy = "a point " + ScRetreat.She(self) + " just failed to reach"; continue; }
                        int crowd = 0;
                        for (int i = 1; i < enemies.Count; i++) if (ScRunners.XZ(enemies[i], pc) < CrowdRadius) crowd++;
                        float score = Mathf.Abs(Angles[k]) / 25f + crowd;
                        if (score < best) { best = score; point = pc; angle = Angles[k]; }
                    }
                    if (best < float.MaxValue)
                    {
                        step = s;
                        longStep = true;
                        enemies.Clear();
                        return true;
                    }
                }
                enemies.Clear();
            }
            if (longest <= 0f) { why = "no step of " + ScConfig.BackOffStepsText() + " fits its " + maxStep.ToString("0.#", CultureInfo.InvariantCulture) + " m"; return false; }
            Vector3 me = self.position;
            Vector3 dir = AwayDir(self, threat.position);
            Gather(self, threat, longest + CrowdRadius + 2f);
            int yRef = Mathf.FloorToInt(me.y + 0.05f);
            float now = Time.time;
            string lastWhy = "no floor near her level";
            int tried = 0;
            for (int si = 0; si < steps.Count; si++)
            {
                float s = steps[si];
                if (s > maxStep + 0.01f) continue;
                float best = float.MaxValue;
                for (int k = 0; k < Angles.Length; k++)
                {
                    Vector3 d = Quaternion.Euler(0f, Angles[k], 0f) * dir;
                    Vector3 pc;
                    string cw;
                    tried++;
                    if (!Candidate(w, self, me, d, s, yRef, region, leader, enemies, out pc, out cw)) { lastWhy = cw; continue; }
                    if (last != null && IsBad(last, pc, now)) { lastWhy = "a point " + ScRetreat.She(self) + " just failed to reach"; continue; }
                    int crowd = 0;
                    for (int i = 1; i < enemies.Count; i++) if (ScRunners.XZ(enemies[i], pc) < CrowdRadius) crowd++;
                    float score = Mathf.Abs(Angles[k]) / 25f + crowd;
                    if (score < best) { best = score; point = pc; angle = Angles[k]; }
                }
                if (best < float.MaxValue)
                {
                    step = s;
                    enemies.Clear();
                    return true;
                }
            }
            enemies.Clear();
            why = "none of " + tried + " points works (steps " + ScConfig.BackOffStepsText() + " m, 7 directions; last: " + lastWhy + ")"
                  + (farWhy != null ? "; no far point at " + F1(far) + " m either (" + farWhy + "; round cb1)" : "");
            return false;
        }

        /// <summary>The longest of the BackOffSteps that fits within maxStep (0: none does) - the step that is not "shortened".</summary>
        internal static float LongestStep(float maxStep)
        {
            List<float> steps = ScConfig.BackOffStepList();
            float longest = 0f;
            for (int i = 0; i < steps.Count; i++) if (steps[i] <= maxStep + 0.01f) longest = Mathf.Max(longest, steps[i]);
            return longest;
        }

        private static bool IsBad(Last l, Vector3 p, float now)
        {
            for (int i = l.Bad.Count - 1; i >= 0; i--)
            {
                if (now >= l.BadUntil[i]) { l.Bad.RemoveAt(i); l.BadUntil.RemoveAt(i); continue; }
                if (ScRunners.XZ(l.Bad[i], p) <= BadRadius && Mathf.Abs(l.Bad[i].y - p.y) < 1.5f) return true;
            }
            return false;
        }

        private static Last LastOf(int id)
        {
            Last l;
            if (!lasts.TryGetValue(id, out l)) lasts[id] = l = new Last();
            return l;
        }

        private static void Prune(float now)
        {
            nextPrune = now + 30f;
            var gone = new List<long>();
            foreach (var kv in noPointUntil) if (now >= kv.Value) gone.Add(kv.Key);
            foreach (long k in gone) noPointUntil.Remove(k);
            var old = new List<int>();
            foreach (var kv in lasts) if (now - kv.Value.At > 60f && kv.Value.Bad.Count == 0) old.Add(kv.Key);
            foreach (int id in old) lasts.Remove(id);
        }

        // ------------------------------------------------------------------ the task's Start (the only writer) ----

        private static MethodBase startTarget, pointTarget, updateTarget;
        private static FieldInfo maxField;
        private static bool bindTried, bindOk;

        /// <summary>Voice Control's back-off task's Start (UAI.UAITaskBackupFromTargetPaced.Start(Context), 1-XNPCVoiceControl.dll), or null.</summary>
        public static MethodBase StartTarget()
        {
            Type t = ScTypes.Find("UAI.UAITaskBackupFromTargetPaced");
            return t != null ? AccessTools.DeclaredMethod(t, "Start", new[] { typeof(UAI.Context) }) : null;
        }

        /// <summary>F1/F5: the same task's Update(Context) - where it stops itself when the navigator has no path - or null.</summary>
        public static MethodBase UpdateTarget()
        {
            Type t = ScTypes.Find("UAI.UAITaskBackupFromTargetPaced");
            return t != null ? AccessTools.DeclaredMethod(t, "Update", new[] { typeof(UAI.Context) }) : null;
        }

        /// <summary>SCore's GetMoveToLocation(Context, Vector3, float), where that task turns its point into the path's goal (SCore 3.4).</summary>
        public static MethodBase PointTarget()
        {
            MethodInfo m = AccessTools.DeclaredMethod(typeof(UAI.SCoreUtils), "GetMoveToLocation", new[] { typeof(UAI.Context), typeof(Vector3), typeof(float) });
            return m != null && m.IsStatic && m.ReturnType == typeof(Vector3) ? m : null;
        }

        /// <summary>
        /// Both targets and the task's max_distance field, once (both Prepares call it). Either target missing: both patches are
        /// off, with one error line while Voice Control is loaded (without it the task does not exist and nothing is said).
        /// </summary>
        internal static bool Bind()
        {
            if (bindTried) return bindOk;
            bindTried = true;
            try
            {
                startTarget = StartTarget();
                if (startTarget == null)
                {
                    if (Patches_VoiceControl.ChatType != null)
                        ScLog.Err("UAITaskBackupFromTargetPaced.Start(Context) not found (Voice Control update?) - 0-XNPCCore's back-off is NOT kept inside the base for members at home (round k1)");
                    return false;
                }
                pointTarget = PointTarget();
                if (pointTarget == null)
                {
                    ScLog.Err("SCoreUtils.GetMoveToLocation(Context, Vector3, float) not found (SCore update?) - 0-XNPCCore's back-off is NOT kept inside the base for members at home (round k1)");
                    return false;
                }
                updateTarget = UpdateTarget();
                if (updateTarget == null)
                    ScLog.Warn("UAITaskBackupFromTargetPaced.Update(Context) not found (Voice Control update?) - members' back-offs at home are not judged for gain, and a point counts as not reached on any quick restart");
                maxField = AccessTools.DeclaredField(startTarget.DeclaringType, "maxFleeDistance");
                if (maxField == null || maxField.IsStatic || maxField.FieldType != typeof(float))
                {
                    maxField = null;
                    ScLog.Warn("UAITaskBackupFromTargetPaced.maxFleeDistance not found (Voice Control update?) - members' bounded back-offs take steps up to 10 m");
                }
                bindOk = true;
            }
            catch (Exception e)
            {
                ScLog.Once("backoff-bind", e);
            }
            return bindOk;
        }

        /// <summary>The task's own max_distance (10 on both of 0-XNPCCore's actions), 10 when it cannot be read.</summary>
        private static float MaxStep(UAI.UAITaskBase task)
        {
            try
            {
                if (maxField != null && task != null) return (float)maxField.GetValue(task);
            }
            catch (Exception e) { ScLog.Once("backoff-maxstep", e); }
            return 10f;
        }

        /// <summary>
        /// Patch_BoundStart's work, for one of the community's members (everyone else: the task as it is). True: the task's own
        /// Start runs - with SC's point in scope at home (BackOffHome), with its own point elsewhere; either way the run is noted
        /// for the gain test (R3). False: the shot has this beat (no point from this enemy, or a member-wide beat - R4:
        /// BackupRanged is let through it while she reloads), or no point now - the task ends at once with no path.
        /// </summary>
        private static bool BoundStart(UAI.UAITaskBase task, UAI.Context ctx)
        {
            if (ctx == null) return true;
            EntityAlive self = ctx.Self;
            if (self == null) return true;
            Last prev;
            if (lasts.TryGetValue(self.entityId, out prev)) prev.Running = false;    // a new run: the gain test is the new one's
            if (self.isEntityRemote || !IsMember(self)) return true;
            EntityAlive t = UAI.UAIUtils.ConvertToEntityAlive(ctx.ActionData.Target);
            if (t == null || t == self) return true;                          // its own "no target" failure, as it is
            // round wp1 (decision 1): an empty gun is no gun - no back-off of any kind while dry (the posture gate's second net)
            // (review WP1-m5) only with a melee weapon in her pair - with none, MELEE lets a back-off from a melee-unsafe or armed
            // enemy through, as before wp1 (re-check WP1R-m3)
            if (ScArms.DryWithMelee(self)) { Patches_Xnpc.Count("backoff-dry-gun"); EndAtOnce(ctx); return false; }
            float now = Time.time;
            if (now >= nextPrune) Prune(now);
            // the shot still has this beat: SCore starts a finished task again every other frame until the next decision
            // (ScBackOffRoom 0) moves on - end it again, with no new search
            UAI.UAIAction act = ctx.ActionData.Action;
            bool wideRun = act != null && act.Name == WideBackOffAction;
            if (Blocked(self, t, act != null && (act.Name == RangedBackOffAction || wideRun))) { EndAtOnce(ctx); return false; }
            float max = MaxStep(task);
            if (max < 5f) return true;                                        // its 3 m "steer" branch (max_distance under 5): as it is
            Last l = LastOf(self.entityId);
            l.Wide = wideRun;
            // round k5: KITE away from home with you - both back-offs stay within StandOffFollowerLeash of you (the old stand-off's bound)
            EntityPlayer leader;
            bool atHome = UAI.UAIConsiderationScNotHomeMember.AtHome(self.position);
            ScRetreat.Region region = ScRetreat.Region.Home;
            if (!atHome && ScRetreat.KitesAway(self, out leader)) region = ScRetreat.Region.Leader;
            else leader = null;
            if (region == ScRetreat.Region.Home && (!ScConfig.BackOffHome || !atHome))
            {
                // R3: away from home (or BackOffHome false) 0-XNPCCore picks the point, unbounded - the gain test still judges the run
                l.From = self.position;
                l.At = now;
                l.Target = t.entityId;
                l.NoPoint = false;
                l.Bounded = false;
                l.Why = "";
                l.GapAtStart = ScRunners.XZ(self.position, t.position);
                l.Running = true;
                l.GainChecked = false;
                l.EndedOwn = false;
                ClearRun(l);                                                 // round cb1: no run pace, no far run, away from home
                return true;
            }
            // F5: a point she did not reach is set aside where its run ends on its own (BoundUpdate). Only without that patch
            // the old test stands: started again from the same enemy within StallSeconds with her not StallMove on
            if (updateTarget == null && l.At > 0f && l.Bounded && !l.NoPoint && l.Target == t.entityId && now - l.At < StallSeconds
                && ScRunners.XZ(self.position, l.From) < StallMove && ScRunners.XZ(self.position, l.Point) > 1f)
                SetAside(l, now);
            Vector3 p;
            float step, angle;
            string why;
            // round cb1 (item 2): ScKiteWide's run at home with a gun that has rounds goes far - FarMeters by her Shooting stars, on a
            // real path; ScKiteWide's band (FarBand to start, WideRunning while it goes) holds every run of it at home to its end or
            // its deadline - round cb1 (re-check CB1R-n3). 0-XNPCCore's own two back-offs keep k1's steps (their 4 m and
            // 10 m bands would cut a far run short). The path the last action left is noted: neither the pace nor the path check
            // judges it
            float far = 0f;
            if (wideRun && region == ScRetreat.Region.Home && now >= l.LongOffUntil && ScRetreat.GunReady(self)) far = FarMeters(self);
            GamePath.PathNavigate nav0 = self.getNavigator();
            GamePath.PathEntity stale = nav0 != null ? nav0.getPath() : null;
            bool longStep;
            if (PickEx(self, t, max, region, leader, l, far, out p, out step, out angle, out longStep, out why))
            {
                l.Action = act != null ? act.Name : "";                       // round cb1 (review CB1-M1): RunCutWithGain's test
                l.Home = region == ScRetreat.Region.Home;                    // round cb1: the run's pace and the hold after it
                l.Far = far > 0f;
                l.FarMeters = far;
                l.Long = longStep;
                l.PathChecked = !longStep;
                l.StalePath = stale;
                l.RunCounted = false;
                // round cb1 (re-check CB1R-m1): every bounded run at home has a deadline (a far run's is re-set from its path), so a held
                // k1 step under ScKiteWide that gets wedged ends too
                l.Deadline = l.Home ? now + step / Mathf.Max(1f, ScConfig.RunnerRunMps) * 2f + 4f : 0f;
                if (longStep)
                {
                    farRuns++;
                    Patches_Xnpc.Count("backoff-far");
                    ScLog.Verbose(ScRetreat.Name(self) + " backs off far from " + Who(t) + " to shoot: " + F1(step) + " m (" + F1(far) + " m from " + ScRetreat.Her(self) + " Shooting stars)"
                                  + (Mathf.Abs(angle) > 0.5f ? ", " + angle.ToString("0", CultureInfo.InvariantCulture) + " degrees off straight back" : "") + ", at a run on a real path inside the base (round cb1)");
                }
                l.NoPointStreak = 0;
                scopeId = self.entityId;
                scopePoint = p;
                l.Point = p;
                l.From = self.position;
                l.At = now;
                l.Step = step;
                l.Angle = angle;
                l.Target = t.entityId;
                l.NoPoint = false;
                l.Why = "";
                l.GapAtStart = ScRunners.XZ(self.position, t.position);
                l.Running = true;
                l.GainChecked = false;
                l.EndedOwn = false;
                l.Bounded = true;
                bounded++;
                Patches_Xnpc.Count("backoff-bounded");
                bool shorter = !longStep && step < LongestStep(max) - 0.01f;
                if (shorter) { shortened++; Patches_Xnpc.Count("backoff-shortened"); }
                if (Mathf.Abs(angle) > 25.5f) { wide++; Patches_Xnpc.Count("backoff-wide"); }
                if (!longStep && (shorter || Mathf.Abs(angle) > 25.5f) && now >= l.NextLog)
                {
                    l.NextLog = now + LogSeconds;
                    ScLog.Verbose(ScRetreat.Name(self) + " backs off from " + Who(t) + (region == ScRetreat.Region.Leader ? " near you: " : " inside the base: ") + step.ToString("0.#", CultureInfo.InvariantCulture) + " m"
                                  + (shorter ? " (the longer steps leave the base or meet a wall)" : "") + (Mathf.Abs(angle) > 0.5f ? ", " + angle.ToString("0", CultureInfo.InvariantCulture) + " degrees off straight back" : "")
                                  + (l.Home ? ", at a run (round cb1)" : "") + " (round k1)");
                }
                return true;
            }
            MarkNoPoint(self, t, why, now, l);
            EndAtOnce(ctx);
            return false;
        }

        /// <summary>No point: ScBackOffRoom scores the back-offs 0 against t for BackOffNoPointSeconds (one decision: the shot takes it).</summary>
        private static void MarkNoPoint(EntityAlive self, EntityAlive t, string why, float now, Last l)
        {
            noPointUntil[Key(self.entityId, t.entityId)] = now + ScConfig.BackOffNoPointSeconds;
            // round hr1 (the last stand): "no point" is the member's for StandBackOffSeconds - one probe every 4 s, not one per target
            // (Nora's back-off cycled 704, 706, 704 and the shot got only the gaps)
            if (ScPosture.InStand(self))
            {
                noPointUntil[Key(self.entityId, t.entityId)] = now + ScPosture.StandBackOffSeconds;
                HandToShot(self, t, ScPosture.StandBackOffSeconds);
                Patches_Xnpc.Count("backoff-stand-no-point");
            }
            noPoint++;
            Patches_Xnpc.Count("backoff-no-point");
            l.NoPointStreak = now - l.NoPointAt <= StreakSeconds ? l.NoPointStreak + 1 : 1;
            l.NoPointAt = now;
            l.At = now;
            l.From = self.position;
            l.Target = t.entityId;
            l.NoPoint = true;
            l.Running = false;
            l.EndedOwn = false;
            l.Bounded = true;
            l.Why = why ?? "";
            ClearRun(l);                                                     // round cb1
            if (now < l.NextLog) return;
            l.NextLog = now + LogSeconds;
            ScLog.Verbose(ScRetreat.Name(self) + " has no point " + (UAI.UAIConsiderationScNotHomeMember.AtHome(self.position) ? "inside the base" : "near you") + " to back off to from " + Who(t) + " (" + why + ") - the shot takes over for "
                          + (ScPosture.InStand(self) ? ScPosture.StandBackOffSeconds.ToString("0.#", CultureInfo.InvariantCulture) + " s against every enemy (her last stand, round hr1)"
                             : ScConfig.BackOffNoPointSeconds.ToString("0.#", CultureInfo.InvariantCulture) + " s") + " (round k1)");
        }

        /// <summary>A task that is done the moment it starts: SCore's updateAction resets it, and the next decision moves on.</summary>
        private static void EndAtOnce(UAI.Context ctx)
        {
            ctx.ActionData.Started = true;
            ctx.ActionData.Executing = false;
        }

        /// <summary>
        /// Patch_BoundUpdate's work, for a member's run against its own enemy only (the Fable review, F1 and F5; R3: bounded or
        /// not - a point is set aside only for a bounded one, the only kind whose point is known). Its end - the
        /// task's own Update stops it when the navigator has no path (arrived, or none found) - is noted (EndedOwn: the stall
        /// test) and judged; GainSeconds in, it is judged once more and, gaining nothing, stopped here (the task's own Stop
        /// cancels the path) with the beat handed to the shot. True: the task's Update runs.
        /// </summary>
        private static bool BoundUpdate(UAI.UAITaskBase task, UAI.Context ctx)
        {
            if (ctx == null) return true;
            EntityAlive self = ctx.Self;
            Last l;
            if (self == null || self.isEntityRemote || !lasts.TryGetValue(self.entityId, out l) || !l.Running) return true;
            EntityAlive t = UAI.UAIUtils.ConvertToEntityAlive(ctx.ActionData.Target);
            if (t == null || t.entityId != l.Target) return true;
            float now = Time.time;
            var nav = self.getNavigator();
            if (nav != null && nav.noPathAndNotPlanningOne())
            {
                l.Running = false;
                l.EndedOwn = true;
                l.EndedAt = now;
                l.StalePath = null;                                           // round cb1: no destructed path kept
                // F5: ended on its own with her not StallMove on and not at the point - that point had no path: set aside
                if (l.Bounded && ScRunners.XZ(self.position, l.From) < StallMove && ScRunners.XZ(self.position, l.Point) > 1f)
                {
                    SetAside(l, now);
                    if (l.Long) LongFailed(self, l, now);                     // round cb1: a far point with no path at all
                }
                // round k5 (rt1's "shoot between steps"): a wide run that gained but left the enemy inside StandOffMin - she shoots
                // StandOffShootSeconds before the next step
                bool gained = GainTest(self, t, l, now, "at its end");
                if (gained && l.Wide && ScConfig.StandOffShootSeconds > 0f
                    && ScRunners.XZ(self.position, t.position) < ScConfig.StandOffMin)
                {
                    HandToShot(self, t, ScConfig.StandOffShootSeconds);
                    Patches_Xnpc.Count("backoff-wide-shoot");
                }
                // round cb1 (item 3): a back-off at home that ended on its own with a gain - she holds that spot and shoots from it
                if (gained && l.Bounded && l.Home) ScPosture.StartHold(self, l.Long ? "her run back" : "her back-off");   // a back-off: re-arms (CB1R-m3)
                return true;                                                  // its own Update stops it
            }
            // round cb1: a far run's path, once the navigator has it - unusable: set aside, the next decision picks again
            if (l.Long && !l.PathChecked)
            {
                string bad = CheckLongPath(self, l, t, now);
                if (bad != null)
                {
                    l.Running = false;
                    l.StalePath = null;
                    SetAside(l, now);
                    LongFailed(self, l, now);
                    Patches_Xnpc.Count("backoff-far-bad-path");
                    if (now >= l.NextLog)
                    {
                        l.NextLog = now + LogSeconds;
                        ScLog.Verbose(ScRetreat.Name(self) + "'s run back to " + ScRetreat.Cell(l.Point) + " is set aside: " + bad + " (round cb1)");
                    }
                    task.Stop(ctx);
                    return false;
                }
            }
            // round cb1: a far run that takes much longer than its path's length at a run (pinned, pushed about) ends - round cb1
            // (re-check CB1R-m1): every bounded run at home, far or not
            if (l.Home && l.Deadline > 0f && now > l.Deadline)
            {
                l.Running = false;
                l.StalePath = null;
                if (l.Long) { farTimeouts++; Patches_Xnpc.Count("backoff-far-timeout"); }
                else Patches_Xnpc.Count("backoff-timeout");
                if (now >= l.NextLog)
                {
                    l.NextLog = now + LogSeconds;
                    ScLog.Verbose(ScRetreat.Name(self) + "'s run back to " + ScRetreat.Cell(l.Point) + " took too long - " + ScRetreat.She(self) + " stops at "
                                  + F1(ScRunners.XZ(self.position, t.position)) + " m from " + Who(t) + " (round cb1)");
                }
                if (ScRunners.XZ(self.position, t.position) >= l.GapAtStart + GainNeed) ScPosture.StartHold(self, "her run back");
                task.Stop(ctx);
                return false;
            }
            if (l.GainChecked || now - l.At < GainSeconds) return true;
            l.GainChecked = true;
            if (GainTest(self, t, l, now, F1(GainSeconds) + " s in")) return true;
            l.Running = false;
            l.StalePath = null;                                               // round cb1
            task.Stop(ctx);
            return false;
        }

        /// <summary>Round cb1: a run that is not a bounded run at home - no run pace, no far run, no path check.</summary>
        private static void ClearRun(Last l)
        {
            l.Home = false;
            l.Far = false;
            l.Long = false;
            l.FarMeters = 0f;
            l.PathChecked = true;
            l.StalePath = null;
            l.RunCounted = false;
            l.Deadline = 0f;
        }

        /// <summary>A point she did not reach: set aside BadSeconds (with every point within BadRadius of it), counted backoff-unreached.</summary>
        private static void SetAside(Last l, float now)
        {
            l.Bad.Add(l.Point);
            l.BadUntil.Add(now + BadSeconds);
            unreached++;
            Patches_Xnpc.Count("backoff-unreached");
        }

        /// <summary>
        /// F1: did the bounded back-off from t gain - t out of the band (BackupKeepDistanceMax, 3D as the band measures) and the
        /// gap (XZ) at least GainNeed larger than at its start? No: HandToShot for StandOffShootSeconds (BackOffNoPointSeconds
        /// when that is 0), counted backoff-no-gain, and false.
        /// </summary>
        private static bool GainTest(EntityAlive self, EntityAlive t, Last l, float now, string when)
        {
            if (t.IsDead()) return true;
            float band = ScConfig.BackupKeepDistanceMax;
            bool inBand = (t.position - self.position).sqrMagnitude <= band * band;
            float gap = ScRunners.XZ(self.position, t.position);
            if (!inBand && gap >= l.GapAtStart + GainNeed) return true;
            float secs = ScConfig.StandOffShootSeconds > 0f ? ScConfig.StandOffShootSeconds : ScConfig.BackOffNoPointSeconds;
            if (ScPosture.InStand(self)) secs = Mathf.Max(secs, ScPosture.StandBackOffSeconds);     // round hr1: the stand's member-wide 4 s
            HandToShot(self, t, secs);
            noGain++;
            Patches_Xnpc.Count("backoff-no-gain");
            if (now >= l.NextLog)
            {
                l.NextLog = now + LogSeconds;
                ScLog.Verbose(ScRetreat.Name(self) + "'s back-off from " + Who(t) + " gained nothing " + when + " (" + F1(l.GapAtStart) + " m to " + F1(gap) + " m"
                              + (inBand ? ", still within " + F1(band) + " m" : "") + ") - the shot takes over against every enemy for " + F1(secs) + " s (the Fable review, F1, R1)");
            }
            return false;
        }

        private static string Who(EntityAlive t) { return t.EntityName + " (" + t.entityId + ")"; }

        /// <summary>
        /// Round k1: 0-XNPCCore's back-off task (BackupKeepDistance, BackupRanged) - for one of the community's members at home its
        /// point is SC's, inside the base (see the class summary). Declared on Voice Control's own type, bound by name in Prepare.
        /// The finalizer clears the scope whatever happens, and counts a point that was picked but never reached GetMoveToLocation
        /// (backoff-unapplied: 0-XNPCCore changed its Start - the path then went where it chose).
        /// </summary>
        [HarmonyPatch]
        public static class Patch_BoundStart
        {
            [HarmonyPrepare]
            public static bool Prepare() { return Bind(); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return StartTarget(); }

            [HarmonyPrefix]
            public static bool Prefix(UAI.UAITaskBase __instance, UAI.Context _context)
            {
                scopeId = -1;
                scopeUsed = false;
                try
                {
                    return BoundStart(__instance, _context);
                }
                catch (Exception e)
                {
                    ScLog.Once("backoff-start", e);
                    scopeId = -1;
                    return true;
                }
            }

            [HarmonyFinalizer]
            public static Exception Finalizer(Exception __exception)
            {
                if (scopeId >= 0 && !scopeUsed)
                {
                    unapplied++;
                    Patches_Xnpc.Count("backoff-unapplied");
                    ScLog.Once("backoff-unapplied", "a member's bounded back-off point never reached SCore's GetMoveToLocation (Voice Control update?) - that back-off went where 0-XNPCCore chose");
                }
                scopeId = -1;
                scopeUsed = false;
                return __exception;
            }
        }

        /// <summary>
        /// The Fable review (F1, F5): 0-XNPCCore's back-off task's Update (bound by name with the others; off on its own, with
        /// one warning, when only it is missing) - BoundUpdate notes a bounded run's own end and judges its gain.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_BoundUpdate
        {
            [HarmonyPrepare]
            public static bool Prepare() { return Bind() && updateTarget != null; }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return updateTarget; }

            [HarmonyPrefix]
            public static bool Prefix(UAI.UAITaskBase __instance, UAI.Context _context)
            {
                try
                {
                    return BoundUpdate(__instance, _context);
                }
                catch (Exception e)
                {
                    ScLog.Once("backoff-update", e);
                    return true;
                }
            }

            /// <summary>Round cb1 (item 1): after the task's own Update, a member's bounded run at home runs (RunPace). Same target, no new one.</summary>
            [HarmonyPostfix]
            public static void Postfix(UAI.Context _context)
            {
                try
                {
                    RunPace(_context);
                }
                catch (Exception e)
                {
                    ScLog.Once("backoff-run-pace", e);
                }
            }
        }

        /// <summary>
        /// Round k1: SCore's GetMoveToLocation - inside Patch_BoundStart's scope, for that member only, the answer is SC's point as
        /// it is (a standable cell's center at her feet: no ground snap needed), and SCore's body is skipped - it would stop a
        /// point within 5 m of her 1 m short, which turns a 3 m step into 2 m and can end it back inside the enemy's reach. One
        /// int compare for every other call.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_BoundPoint
        {
            [HarmonyPrepare]
            public static bool Prepare() { return Bind(); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return PointTarget(); }

            [HarmonyPrefix]
            public static bool Prefix(UAI.Context _context, ref Vector3 __result)
            {
                if (scopeId < 0) return true;
                try
                {
                    if (_context == null || _context.Self == null || _context.Self.entityId != scopeId) return true;
                    __result = scopePoint;
                    scopeUsed = true;
                    return false;
                }
                catch (Exception e)
                {
                    ScLog.Once("backoff-point", e);
                    return true;
                }
            }
        }

        // ------------------------------------------------------------------ console ----

        /// <summary>The totals for sc xnpc.</summary>
        public static string Report()
        {
            // round cb1: the run pace, the far runs and the steps to open ground
            string cb1 = "; round cb1: " + runs + " run(s) at her run pace, " + farRuns + " far run(s) to shoot from (" + farFailed + " path(s) unusable, " + farTimeouts
                         + " too long; " + F1(ScConfig.BackOffBaseMeters) + " m + " + F1(ScConfig.BackOffPerShootingStar) + " m per Shooting star), steps to open ground "
                         + stepsAsked + " asked (" + stepsReached + " reached, " + stepsNone + " with none found)";
            if (!ScConfig.BackOffHome) return "OFF (BackOffHome=false: 0-XNPCCore's own back-off at home, unbounded; " + noGain + " that gained nothing, "
                                              + handed + " member-wide beat(s) handed to the shot); members back off within " + F1(ScConfig.BackupKeepDistanceMax) + " m" + cb1;
            return (bindOk ? "on" : "NOT BOUND (see the log)") + " - members at home back off inside the base, steps " + ScConfig.BackOffStepsText() + " m on 7 directions; "
                   + bounded + " bounded (" + shortened + " shortened, " + wide + " wider than 25 degrees), " + noPoint + " with no point (the shot took over for "
                   + F1(ScConfig.BackOffNoPointSeconds) + " s), " + noGain + " that gained nothing (the shot took over for " + F1(ScConfig.StandOffShootSeconds) + " s), "
                   + handed + " member-wide beat(s) handed to the shot in all, " + unreached + " point(s) not reached, " + unapplied + " not applied"
                   + (updateTarget == null ? " (NO GAIN TEST: the task's Update not bound)" : "") + "; members back off within "
                   + F1(ScConfig.BackupKeepDistanceMax) + " m (" + F1(ScConfig.BackupKeepDistanceMax + BandHysteresis) + " once backing off; 0-XNPCCore's own band is 4) - counters backoff-* in the hook list above" + cb1;
        }

        /// <summary>One line for sc xnpc &lt;id&gt;.</summary>
        public static string Line(EntityAlive e)
        {
            if (e == null) return "back-off: -";
            var sb = new StringBuilder("back-off (round k1): ");
            if (ScRegistry.GetMember(e.entityId) == null) return sb.Append("not a member - 0-XNPCCore's own").ToString();
            EntityPlayer leader;
            bool away = ScRetreat.KitesAway(e, out leader);
            sb.Append(away ? "away from home, KITE hurt - within " + F1(ScConfig.StandOffFollowerLeash) + " m of you"
                      : !ScConfig.BackOffHome ? "unbounded (BackOffHome=false)" : AppliesTo(e) ? "at home - kept inside the base" : "away from home - 0-XNPCCore's own, unbounded");
            sb.Append(", within ").Append(F1(ScConfig.BackupKeepDistanceMax)).Append(" m");
            if (ScRetreat.Kites(e)) sb.Append(" (KITE's wide band: ScKiteWide within ").Append(F1(ScConfig.StandOffMin)).Append(" m)");
            Last l;
            float now = Time.time;
            if (lasts.TryGetValue(e.entityId, out l) && l.At > 0f)
            {
                sb.Append("; last ").Append((now - l.At).ToString("0.0", CultureInfo.InvariantCulture)).Append(" s ago from #").Append(l.Target).Append(": ");
                if (l.NoPoint) sb.Append("NO POINT (").Append(l.Why).Append(')');
                else if (!l.Bounded) sb.Append("0-XNPCCore's own point (unbounded)");
                else sb.Append(F1(l.Step)).Append(" m, ").Append(l.Angle.ToString("0", CultureInfo.InvariantCulture)).Append(" degrees, to ").Append(ScRetreat.Cell(l.Point));
                if (l.Wide) sb.Append(" (ScKiteWide)");
                if (l.Long) sb.Append(" FAR (" + F1(l.FarMeters) + " m by her Shooting stars, path " + (l.PathChecked ? "checked" : "not checked yet") + "; round cb1)");
                if (l.Home && !l.NoPoint) sb.Append(" at a run (round cb1)");
                if (now < l.LongOffUntil) sb.Append("; far runs off for " + F1(l.LongOffUntil - now) + " s (round cb1)");
                if (!l.NoPoint) sb.Append(l.Running ? ", RUNNING (from " + F1(l.GapAtStart) + " m)" : l.EndedOwn ? ", ended on its own" : ", stopped");
                if (l.Bad.Count > 0) sb.Append("; ").Append(l.Bad.Count).Append(" point(s) set aside (not reached)");
            }
            int waiting = 0;
            float beat = 0f;
            foreach (var kv in noPointUntil)
            {
                if ((int)(kv.Key >> 32) != e.entityId || now >= kv.Value) continue;
                if ((int)(kv.Key & 0xFFFFFFFFL) == AnyTarget) beat = kv.Value - now;
                else waiting++;
            }
            if (beat > 0f) sb.Append("; the shot has the next ").Append(F1(beat)).Append(" s against every enemy (no gain, or a shooting beat between wide steps; BackupRanged and ScKiteWide only while reloading)");
            if (waiting > 0) sb.Append("; no point from ").Append(waiting).Append(" enemy(ies): the shot has those");
            StepState ss;
            if (stepStates.TryGetValue(e.entityId, out ss) && (ss.Walking || now < ss.WantedUntil))
                sb.Append("; STEPPING to open ground at ").Append(ScRetreat.Cell(ss.Point)).Append(" (").Append(ss.Why).Append("; round cb1)");
            return sb.ToString();
        }

        private static string F1(float f) { return f.ToString("0.#", CultureInfo.InvariantCulture); }
    }
}

namespace UAI
{
    using SurvivorCommunity;

    /// <summary>
    /// Round k1: 0 against an enemy for BackOffNoPointSeconds after SC found no point inside the base to back off to from it
    /// (ScBackOff, on 0-XNPCCore's task's Start), else 1 - everyone but the community's members is always 1. On both of
    /// 0-XNPCCore's back-offs: the shot (6) takes that decision instead of a back-off that cannot go anywhere, then the back-off
    /// is asked again. The Fable review (F1): also for StandOffShootSeconds after a back-off gained nothing, and after a wide
    /// step (round k5) - member-wide since the re-review (R1: against every enemy, or two pursuers took turns).
    /// reload="pass" (R4: BackupRanged's and ScKiteWide's): that member-wide beat lets it through while she reloads
    /// (ScBackOff.Blocked). Also on SCResident's ScKiteWide (round k5).
    /// A pure read; a throw is logged once and means 1.
    /// </summary>
    public class UAIConsiderationScBackOffRoom : UAIConsiderationBase
    {
        /// <summary>R4: reload="pass" (on BackupRanged): a member-wide beat lets this back-off through while she reloads.</summary>
        private bool reloadPass;

        public override void Init(System.Collections.Generic.Dictionary<string, string> parameters)
        {
            base.Init(parameters);
            string v;
            if (parameters != null && parameters.TryGetValue("reload", out v) && v != null) reloadPass = v.Trim().ToLowerInvariant() == "pass";
        }

        public override float GetScore(Context _context, object target)
        {
            try
            {
                return ScBackOff.Blocked(_context.Self, UAIUtils.ConvertToEntityAlive(target), reloadPass) ? 0f : 1f;
            }
            catch (System.Exception e)
            {
                ScLog.Once("uai-backoff-room", e);
                return 1f;
            }
        }
    }

    /// <summary>
    /// Round cb1: SCResident's ScStepClear (8.55, IsSelf) - 1 while ScBackOff has a short step to open ground asked for or running
    /// for one of the community's members at home (her swing blocked in a last stand, or a fight with her on a model or in a wreck's
    /// hull), else 0. A pure read; a throw is logged once and means 0 (no step).
    /// </summary>
    public class UAIConsiderationScStepClear : UAIConsiderationBase
    {
        public override float GetScore(Context _context, object target)
        {
            try
            {
                return ScBackOff.StepWanted(_context.Self) ? 1f : 0f;
            }
            catch (System.Exception e)
            {
                ScLog.Once("uai-step-clear", e);
                return 0f;
            }
        }
    }

    /// <summary>
    /// Round cb1: ScStepClear's task - a path at her run to the step's spot (ScBackOff.StepStart), ended on arrival, after
    /// StepSeconds or with no path (StepTick). A shared singleton: its state is per entity, in ScBackOff.
    /// </summary>
    public class UAITaskStepClearSC : UAITaskBase
    {
        public override void Start(Context _context)
        {
            base.Start(_context);
            try
            {
                if (!ScBackOff.StepStart(_context.Self)) Stop(_context);
            }
            catch (System.Exception e)
            {
                ScLog.Once("step-clear-start", e);
                Stop(_context);
            }
        }

        public override void Update(Context _context)
        {
            try
            {
                if (!ScBackOff.StepTick(_context.Self)) Stop(_context);
            }
            catch (System.Exception e)
            {
                ScLog.Once("step-clear-update", e);
                Stop(_context);
            }
        }

        public override void Stop(Context _context)
        {
            try { ScBackOff.StepStopped(_context.Self); }
            catch (System.Exception e) { ScLog.Once("step-clear-stop", e); }
            base.Stop(_context);
        }
    }
}
