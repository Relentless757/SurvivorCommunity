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
            return InBeat(self) && !(reloadPass && Reloading(self));
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
            string gw;
            if (ScStuck.Unreachable(self.entityId, cell) || !ScCommunity.GoodPost(w, cell, false, out gw)) { why = "not standable"; return false; }
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
        /// The bounded point for her back-off from threat (C#: the task's Start, k2b's heal back-off, R2's room probe): the BackOffSteps up to
        /// maxStep, longest first; on each the seven directions; the valid point (Candidate) with the lowest score - its angle
        /// (25 degrees count 1) plus each other enemy within CrowdRadius of it - on the first step that has one. last (the bound
        /// task's per-member memory, else null) sets aside the points she just failed to reach. False with why: none at all.
        /// </summary>
        internal static bool Pick(EntityAlive self, EntityAlive threat, float maxStep, ScRetreat.Region region, EntityPlayer leader, Last last,
                                  out Vector3 point, out float step, out float angle, out string why)
        {
            point = Vector3.zero;
            step = 0f;
            angle = 0f;
            why = null;
            World w = self != null ? self.world : null;
            if (w == null || threat == null) { why = "no threat"; return false; }
            List<float> steps = ScConfig.BackOffStepList();
            float longest = LongestStep(maxStep);
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
            why = "none of " + tried + " points works (steps " + ScConfig.BackOffStepsText() + " m, 7 directions; last: " + lastWhy + ")";
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
            if (Pick(self, t, max, region, leader, l, out p, out step, out angle, out why))
            {
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
                bool shorter = step < LongestStep(max) - 0.01f;
                if (shorter) { shortened++; Patches_Xnpc.Count("backoff-shortened"); }
                if (Mathf.Abs(angle) > 25.5f) { wide++; Patches_Xnpc.Count("backoff-wide"); }
                if ((shorter || Mathf.Abs(angle) > 25.5f) && now >= l.NextLog)
                {
                    l.NextLog = now + LogSeconds;
                    ScLog.Verbose(ScRetreat.Name(self) + " backs off from " + Who(t) + (region == ScRetreat.Region.Leader ? " near you: " : " inside the base: ") + step.ToString("0.#", CultureInfo.InvariantCulture) + " m"
                                  + (shorter ? " (the longer steps leave the base or meet a wall)" : "") + (Mathf.Abs(angle) > 0.5f ? ", " + angle.ToString("0", CultureInfo.InvariantCulture) + " degrees off straight back" : "")
                                  + " (round k1)");
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
            if (now < l.NextLog) return;
            l.NextLog = now + LogSeconds;
            ScLog.Verbose(ScRetreat.Name(self) + " has no point " + (UAI.UAIConsiderationScNotHomeMember.AtHome(self.position) ? "inside the base" : "near you") + " to back off to from " + Who(t) + " (" + why + ") - the shot takes over for "
                          + ScConfig.BackOffNoPointSeconds.ToString("0.#", CultureInfo.InvariantCulture) + " s (round k1)");
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
                // F5: ended on its own with her not StallMove on and not at the point - that point had no path: set aside
                if (l.Bounded && ScRunners.XZ(self.position, l.From) < StallMove && ScRunners.XZ(self.position, l.Point) > 1f) SetAside(l, now);
                // round k5 (rt1's "shoot between steps"): a wide run that gained but left the enemy inside StandOffMin - she shoots
                // StandOffShootSeconds before the next step
                if (GainTest(self, t, l, now, "at its end") && l.Wide && ScConfig.StandOffShootSeconds > 0f
                    && ScRunners.XZ(self.position, t.position) < ScConfig.StandOffMin)
                {
                    HandToShot(self, t, ScConfig.StandOffShootSeconds);
                    Patches_Xnpc.Count("backoff-wide-shoot");
                }
                return true;                                                  // its own Update stops it
            }
            if (l.GainChecked || now - l.At < GainSeconds) return true;
            l.GainChecked = true;
            if (GainTest(self, t, l, now, F1(GainSeconds) + " s in")) return true;
            l.Running = false;
            task.Stop(ctx);
            return false;
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
            if (!ScConfig.BackOffHome) return "OFF (BackOffHome=false: 0-XNPCCore's own back-off at home, unbounded; " + noGain + " that gained nothing, "
                                              + handed + " member-wide beat(s) handed to the shot); members back off within " + F1(ScConfig.BackupKeepDistanceMax) + " m";
            return (bindOk ? "on" : "NOT BOUND (see the log)") + " - members at home back off inside the base, steps " + ScConfig.BackOffStepsText() + " m on 7 directions; "
                   + bounded + " bounded (" + shortened + " shortened, " + wide + " wider than 25 degrees), " + noPoint + " with no point (the shot took over for "
                   + F1(ScConfig.BackOffNoPointSeconds) + " s), " + noGain + " that gained nothing (the shot took over for " + F1(ScConfig.StandOffShootSeconds) + " s), "
                   + handed + " member-wide beat(s) handed to the shot in all, " + unreached + " point(s) not reached, " + unapplied + " not applied"
                   + (updateTarget == null ? " (NO GAIN TEST: the task's Update not bound)" : "") + "; members back off within "
                   + F1(ScConfig.BackupKeepDistanceMax) + " m (" + F1(ScConfig.BackupKeepDistanceMax + BandHysteresis) + " once backing off; 0-XNPCCore's own band is 4) - counters backoff-* in the hook list above";
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
}
