using System.Collections.Generic;
using SurvivorCommunity;
using UnityEngine;

// Round rt1 (2026-11-15, research/flee_meds/DESIGN.md Part A): the retreat's UAI classes - five considerations, each a PURE
// READ of the state ScRetreat.Update keeps in the X5 tick (trap 1: UAI runs every consideration for every candidate several
// times a second, whatever the gates above it scored), each with a logged-once try/catch that falls back to the behavior
// before this round; and two tasks, shared singletons with per-entity state (UaiOps.cs's note), whose Start resumes rather
// than re-picks when SCore re-starts a finished task. Config/utilityai.xml wires them; RetreatHome=false makes every one of
// them answer as before (the gates 1, the two new actions 0).
namespace UAI
{
    /// <summary>
    /// SCResident's ScRetreat (8.5, IsSelf): 1 while ScRetreat has her in Shelter mode (at home: hurt with no usable gun or
    /// against armed enemies, or Cowardly and not hanging back) and she is not fighting back, else 0.
    /// </summary>
    public class UAIConsiderationScRetreating : UAIConsiderationBase
    {
        public override float GetScore(Context _context, object target)
        {
            try
            {
                return ScRetreat.ShelterScores(_context.Self) ? 1f : 0f;
            }
            catch (System.Exception e)
            {
                ScLog.Once("uai-retreating", e);
                return 0f;
            }
        }
    }

    /// <summary>
    /// SCResident's ScStandOffHurt (8.2, IsEnemy): 1 for an enemy while ScRetreat has her standing off with her gun, not
    /// cornered, and the enemy inside StandOffMin - or her back-off already running and the enemy still nearer than her
    /// target gap - else 0.
    /// </summary>
    public class UAIConsiderationScStandOff : UAIConsiderationBase
    {
        public override float GetScore(Context _context, object target)
        {
            try
            {
                return ScRetreat.StandOffScores(_context.Self, target as EntityAlive) ? 1f : 0f;
            }
            catch (System.Exception e)
            {
                ScLog.Once("uai-standoff", e);
                return 0f;
            }
        }
    }

    /// <summary>
    /// On ScFleeHurt (for="hurt", the default) and ScFlee (for="coward"): 0 where the retreat owns her or would - a member
    /// at home, checked live (trap 9: never a FleeFromTarget out of the base through a stale state); away from home a
    /// follower who stands off with her gun - else 1: away from home as before, RetreatHome off, and for the cowards'
    /// flee RetreatCowards off. A throw is logged once and means 1 (the flight as before).
    /// </summary>
    public class UAIConsiderationScNotRetreatHome : UAIConsiderationBase
    {
        private bool coward;

        public override void Init(Dictionary<string, string> parameters)
        {
            base.Init(parameters);
            string v;
            if (parameters.TryGetValue("for", out v) && v != null) coward = v.Trim().ToLowerInvariant() == "coward";
        }

        public override float GetScore(Context _context, object target)
        {
            try
            {
                return ScRetreat.FleeAllowed(_context.Self, coward) ? 1f : 0f;
            }
            catch (System.Exception e)
            {
                ScLog.Once("uai-not-retreat-home", e);
                return 1f;
            }
        }
    }

    /// <summary>
    /// 0 while the retreat owns her (Shelter or StandOff), else 1 - one owner of her distance. On 0-XNPCCore's
    /// MeleeApproach (5: no chase out of her room while she fights back), and SCResident's ScDefendBase, ScDefendHold and
    /// ScReturn (the leash would walk her to her post mid-retreat), and (the Fable review, finding 4) 0-XNPCCore's Guard (3),
    /// which walked her to her post through every reload and fight back. A throw is logged once and means 1.
    /// Round k1: only="shelter" (on 0-XNPCCore's BackupKeepDistance and BackupRanged) is 0 only while she SHELTERS. A
    /// stand-off no longer takes the back-offs (they kept her still whenever ScStandOffHurt did not score - "cornered" for 3 s,
    /// 212 s of it before Paul died): ScStandOffHurt (8.2) wins while it scores, and otherwise 0-XNPCCore's back-off (kept
    /// inside the base, ScBackOff) and the shot take over - one owner at a time.
    /// </summary>
    public class UAIConsiderationScNotRetreating : UAIConsiderationBase
    {
        private bool shelterOnly;

        public override void Init(Dictionary<string, string> parameters)
        {
            base.Init(parameters);
            string v;
            if (parameters.TryGetValue("only", out v) && v != null) shelterOnly = v.Trim().ToLowerInvariant() == "shelter";
        }

        public override float GetScore(Context _context, object target)
        {
            try
            {
                return (shelterOnly ? ScRetreat.Shelters(_context.Self) : ScRetreat.Owns(_context.Self)) ? 0f : 1f;
            }
            catch (System.Exception e)
            {
                ScLog.Once("uai-not-retreating", e);
                return 1f;
            }
        }
    }

    /// <summary>
    /// Decision 10, the other half of the flee/return loop (trap 7): 0 for one of the community's members who is hurt with
    /// nothing to heal with (ScMedic.HurtNoMeds at FleeBelowHealth, ScFleeHurt's own rule) - she is never walked to a fight,
    /// at home or away - else 1, and always 1 with RetreatHome off. On ScDefendBase, ScDefendHold and 0-XNPCCore's
    /// MeleeApproach. Her shot and swing at what reaches her still run. A throw is logged once and means 1.
    /// </summary>
    public class UAIConsiderationScNotHurtNoMeds : UAIConsiderationBase
    {
        public override float GetScore(Context _context, object target)
        {
            try
            {
                return ScRetreat.HurtNoMedsMember(_context.Self) ? 0f : 1f;
            }
            catch (System.Exception e)
            {
                ScLog.Once("uai-not-hurt-no-meds", e);
                return 1f;
            }
        }
    }

    /// <summary>
    /// ScRetreat's task: run to the shelter spot and hold it. Start takes the spot this retreat already has (back from a
    /// fight, or SCore re-starting the task), else picks one (ScShelter.Pick: her own cell, a room, the fallback chain). The
    /// walk: FindPath at ScRetreat.RunSpeed (trap 11), never an SCore move task (trap 5: SCore's door opener would race
    /// ScDoors). The move helper's door opener runs for members (ScDoors.TryOpen opens and remembers the closed doors); every
    /// half second the open doors she passed go to ScDoors.TrackPassed (decision 7). No 1 m gain in NoProgressSeconds (the
    /// roam's rule, ScStuck.Progress), no path after one retry, or out of time: the spot is marked for this retreat and the
    /// next is picked; after ScShelter.MaxFails the fallback chain. Arrived (within 1 m): she holds, facing the last door she
    /// came through (else the threat), the spot re-validated every 2 s; pushed more than 1.5 m off it she walks back.
    /// </summary>
    public class UAITaskRetreatSC : UAITaskBase
    {
        private sealed class Walk
        {
            public Vector3 Spot;
            public bool Holding;
            public float Deadline, NextCheck, NextDoors, GainAt, NextUpgrade;
            public bool Retried;
            public readonly ScStuck.Progress Prog = new ScStuck.Progress();
            public GamePath.PathEntity DoorPath;
            public int DoorIndex;
            public Vector3 LastDoor;
            public bool HasDoor;
        }

        private readonly Dictionary<int, Walk> walks = new Dictionary<int, Walk>();
        private const float NoProgressSeconds = 8f, ArriveXZ = 1f, ArriveDy = 1.5f, OffSpot = 1.5f, CheckSeconds = 2f, DoorSeconds = 0.5f;
        /// <summary>Holding a fallback spot, she looks for a room again this often.</summary>
        private const float UpgradeSeconds = 10f;

        public override void Start(Context _context)
        {
            base.Start(_context);
            EntityAlive self = _context.Self;
            try
            {
                if (!ScRetreat.ShelterScores(self)) { Stop(_context); return; }
                Walk wk;
                if (!walks.TryGetValue(self.entityId, out wk)) walks[self.entityId] = wk = new Walk();
                Vector3 spot;
                bool hold;
                if (ScRetreat.SpotOf(self, out spot, out hold)) wk.Spot = hold ? self.position : spot;   // resume: the same spot (a hold is where she stands now)
                else Pick(self, wk);
                Go(self, wk);
            }
            catch (System.Exception e)
            {
                ScLog.Once("retreat-task-start", e);
                Stop(_context);
            }
        }

        private static void Pick(EntityAlive self, Walk wk)
        {
            int fails;
            List<Vector3i> bad = ScRetreat.BadSpots(self, out fails);
            ScShelter.PickResult r = ScShelter.Pick(self, bad, fails);
            if (r.Kind == ScShelter.PickKind.None) { r.Kind = ScShelter.PickKind.Hold; r.Spot = self.position; r.Source = "where " + ScRetreat.She(self) + " stood"; }
            ScRetreat.NoteSpot(self, r);
            wk.Spot = r.Spot;
        }

        private static void Go(EntityAlive self, Walk wk)
        {
            float now = Time.time;
            wk.Retried = false;
            wk.HasDoor = false;
            wk.DoorPath = null;
            wk.DoorIndex = 0;
            if (Arrived(self, wk.Spot))
            {
                ScCommunity.DropPath(self);
                wk.Holding = true;
                wk.NextCheck = now + CheckSeconds;
                return;
            }
            wk.Holding = false;
            wk.Prog.Clear();
            wk.GainAt = now;
            wk.Deadline = now + Mathf.Clamp(ScRunners.XZ(self.position, wk.Spot) * 1.5f + 10f, 15f, 60f);
            if (self.moveHelper != null) self.moveHelper.CanBreakBlocks = false;
            self.FindPath(wk.Spot, ScRetreat.RunSpeed(self), false, null);
        }

        private static bool Arrived(EntityAlive self, Vector3 spot)
        {
            return ScRunners.XZ(self.position, spot) <= ArriveXZ && Mathf.Abs(self.position.y - spot.y) < ArriveDy;
        }

        public override void Update(Context _context)
        {
            EntityAlive self = _context.Self;
            try
            {
                Walk wk;
                if (!ScRetreat.ShelterScores(self) || !walks.TryGetValue(self.entityId, out wk)) { Stop(_context); return; }
                float now = Time.time;
                if (wk.Holding)
                {
                    if (self.moveHelper != null && self.moveHelper.IsActive) self.moveHelper.Stop();
                    if (ScRunners.XZ(self.position, wk.Spot) > OffSpot || Mathf.Abs(self.position.y - wk.Spot.y) > ArriveDy) { Go(self, wk); return; }   // pushed off it
                    if (now >= wk.NextCheck)
                    {
                        wk.NextCheck = now + CheckSeconds;
                        string why;
                        if (!ScRetreat.SpotHolds(self, out why))
                        {
                            ScRetreat.SpotFailed(self, wk.Spot, why, false);
                            Pick(self, wk);
                            Go(self, wk);
                            return;
                        }
                        // on a fallback (the cache was still empty, or the threat stood between her and every room): a room again?
                        if (now >= wk.NextUpgrade && ScRetreat.OnFallback(self))
                        {
                            wk.NextUpgrade = now + UpgradeSeconds;
                            int fails;
                            List<Vector3i> bad = ScRetreat.BadSpots(self, out fails);
                            ScShelter.PickResult r = ScShelter.Pick(self, bad, fails);
                            if (r.Kind == ScShelter.PickKind.Room || r.Kind == ScShelter.PickKind.Here)
                            {
                                Patches_Xnpc.Count("retreat-fallback-upgraded");
                                ScRetreat.NoteSpot(self, r);
                                wk.Spot = r.Spot;
                                Go(self, wk);
                                return;
                            }
                        }
                    }
                    Vector3 look;
                    EntityAlive t;
                    if (wk.HasDoor) look = wk.LastDoor + new Vector3(0f, 1.5f, 0f);
                    else if (ScRetreat.ThreatPlace(self, out look, out t)) look += new Vector3(0f, 1.5f, 0f);
                    else return;
                    self.SetLookPosition(look);
                    self.RotateTo(look.x, look.y, look.z, 30f, 0f);
                    return;
                }
                if (now >= wk.NextDoors)
                {
                    wk.NextDoors = now + DoorSeconds;
                    ScRetreat.NoteDoors(self, ScDoors.TrackPassed(self, ref wk.DoorPath, ref wk.DoorIndex, ref wk.LastDoor, ref wk.HasDoor));
                }
                if (Arrived(self, wk.Spot))
                {
                    // the doors of the last half second too, before the path is dropped (the last one passed is the one she faces)
                    ScRetreat.NoteDoors(self, ScDoors.TrackPassed(self, ref wk.DoorPath, ref wk.DoorIndex, ref wk.LastDoor, ref wk.HasDoor));
                    ScCommunity.DropPath(self);
                    wk.Holding = true;
                    wk.NextCheck = now + CheckSeconds;
                    Patches_Xnpc.Count("retreat-arrived");
                    return;
                }
                bool noPath = self.getNavigator() != null && self.getNavigator().noPathAndNotPlanningOne();
                if (noPath && !wk.Retried && now <= wk.Deadline)
                {
                    // SCore clears a stalled human's path (a door leaf still swinging): once more
                    wk.Retried = true;
                    self.FindPath(wk.Spot, ScRetreat.RunSpeed(self), false, null);
                    return;
                }
                string fail = noPath ? "has no path to it" : now > wk.Deadline ? "took too long" : null;
                if (fail == null && self.moveHelper != null && self.moveHelper.BlockedTime > 3f) fail = "is blocked (3 s)";
                if (fail == null)
                {
                    float d = Vector3.Distance(self.position, wk.Spot);
                    if (wk.Prog.Gained(self, d)) wk.GainAt = now;
                    else if (now - wk.GainAt >= NoProgressSeconds) fail = "gained no 1 m in " + NoProgressSeconds.ToString("0") + " s";
                }
                if (fail != null)
                {
                    ScRetreat.SpotFailed(self, wk.Spot, fail, true);
                    Pick(self, wk);
                    Go(self, wk);
                }
            }
            catch (System.Exception e)
            {
                ScLog.Once("retreat-task-update", e);
                Stop(_context);
            }
        }

        public override void Stop(Context _context)
        {
            EntityAlive self = _context.Self;
            walks.Remove(self.entityId);
            ScCommunity.DropPath(self);
            if (self.moveHelper != null) self.moveHelper.CanBreakBlocks = true;
            base.Stop(_context);
        }
    }

    /// <summary>
    /// ScStandOffHurt's task (design 2.3): back her off from the nearest threat to a point that makes the gap about her
    /// target gap (ScShelter.BackOff), at ScRetreat.RunSpeed, then stop: the stand-off stops scoring and the shot (6) turns
    /// her and fires. Stops when the gap reaches G - 1 or the point; a threat that gains on her (the gap not 1 m larger after
    /// 2 s of walking: a feral, a dog), no point at all, or no path: cornered (ScRetreat.Cornered).
    /// Round k1: no full-gap point - one of the BackOffSteps (10, 6, 3 m) instead (ScShelter.BackOff; counted
    /// retreat-backoff-shortened); a step that ends with the threat still inside StandOffMin - or a walk a feral gains on - is
    /// followed by StandOffShootSeconds of shooting (ScRetreat.ShootBetweenSteps); cornered lasts one decision
    /// (BackOffNoPointSeconds, it was 3 s), and meanwhile 0-XNPCCore's back-off (no longer gated off in a stand-off, kept
    /// inside the base) and the shot take over.
    /// </summary>
    public class UAITaskStandOffSC : UAITaskBase
    {
        private sealed class Walk
        {
            public Vector3 Point;
            public int ThreatId;
            public float CheckAt, GapAtCheck, Deadline;
            /// <summary>When another action stopped it (0: running): a resume only within ResumeSeconds of that.</summary>
            public float StoppedAt;
            public bool Retried;
        }

        private readonly Dictionary<int, Walk> walks = new Dictionary<int, Walk>();
        private const float GainSeconds = 2f, FirstCheck = 2.5f, GainNeed = 1f, ArriveXZ = 1f;
        /// <summary>A walk stopped by another action (a busy animation, a jump order) is resumed only this soon after; else a fresh point.</summary>
        private const float ResumeSeconds = 2f;

        public override void Start(Context _context)
        {
            base.Start(_context);
            EntityAlive self = _context.Self;
            try
            {
                if (!ScRetreat.StandOffMayRun(self)) { Stop(_context); return; }
                float now = Time.time;
                Walk wk;
                if (walks.TryGetValue(self.entityId, out wk) && now < wk.Deadline && now - wk.StoppedAt <= ResumeSeconds)
                {
                    // resumed (a higher action was over within ResumeSeconds): the same point while the same threat is still
                    // short of her gap, the gain clock afresh
                    EntityAlive rt = Threat(self, wk.ThreatId);
                    if (rt != null && rt.entityId == wk.ThreatId && ScRunners.XZ(self.position, rt.position) < ScRetreat.GapOf(self) - 1f)
                    {
                        wk.CheckAt = now + FirstCheck;
                        wk.GapAtCheck = ScRunners.XZ(self.position, rt.position);
                        wk.Retried = false;
                        wk.StoppedAt = 0f;
                        ScRetreat.BackingOff(self, true, wk.Point);
                        if (self.moveHelper != null) self.moveHelper.CanBreakBlocks = false;
                        self.FindPath(wk.Point, ScRetreat.RunSpeed(self), false, null);
                        return;
                    }
                    walks.Remove(self.entityId);
                }
                // the Fable review (findings 2, 3): a new point only for the scan's threat - the one the action scores - inside
                // StandOffMin with a clear line. Anything else (SCore re-starting the task in the frames after a finished
                // back-off, the threat already past StandOffMin) stops at once, with no pick, no path and nothing counted.
                EntityAlive t = ScRetreat.StandOffThreat(self);
                if (t == null) { Stop(_context); return; }
                float gap = ScRetreat.GapOf(self);
                Vector3 p;
                string why;
                float step;
                if (!ScShelter.BackOff(self, t, gap, ScRetreat.RegionOf(self), out p, out why, out step))
                {
                    ScRetreat.Cornered(self, "no point to back off to: " + why);
                    Stop(_context);
                    return;
                }
                if (step > 0f) Patches_Xnpc.Count("retreat-backoff-shortened");   // round k1: a graded step, not the full gap
                walks[self.entityId] = wk = new Walk
                {
                    Point = p,
                    ThreatId = t.entityId,
                    CheckAt = now + FirstCheck,
                    GapAtCheck = ScRunners.XZ(self.position, t.position),
                    Deadline = now + 15f
                };
                ScRetreat.BackingOff(self, true, p);
                Patches_Xnpc.Count("retreat-backoff");                   // a new back-off (a resume is not counted again)
                if (self.moveHelper != null) self.moveHelper.CanBreakBlocks = false;
                self.FindPath(p, ScRetreat.RunSpeed(self), false, null);
            }
            catch (System.Exception e)
            {
                ScLog.Once("standoff-task-start", e);
                Stop(_context);
            }
        }

        /// <summary>
        /// The threat whose gap she is opening: the scan's (ScRetreat) - the one ScStandOff scores (finding 2) - else, with no
        /// threat in this scan, the one the walk began from.
        /// </summary>
        private static EntityAlive Threat(EntityAlive self, int remembered)
        {
            Vector3 pos;
            EntityAlive t;
            if (ScRetreat.ThreatPlace(self, out pos, out t) && t != null) return t;
            t = remembered >= 0 && self.world != null ? self.world.GetEntity(remembered) as EntityAlive : null;
            return t != null && !t.IsDead() ? t : null;
        }

        public override void Update(Context _context)
        {
            EntityAlive self = _context.Self;
            try
            {
                Walk wk;
                if (!ScRetreat.StandOffMayRun(self) || !walks.TryGetValue(self.entityId, out wk)) { Stop(_context); return; }
                float now = Time.time;
                EntityAlive t = Threat(self, wk.ThreatId);
                if (t == null) { walks.Remove(self.entityId); Stop(_context); return; }
                if (t.entityId != wk.ThreatId)
                {
                    // the scan's threat changed: measure (and judge the gain) against the new one from here
                    wk.ThreatId = t.entityId;
                    wk.GapAtCheck = ScRunners.XZ(self.position, t.position);
                    wk.CheckAt = now + GainSeconds;
                }
                float gap = ScRunners.XZ(self.position, t.position);
                if (gap >= ScRetreat.GapOf(self) - 1f || ScRunners.XZ(self.position, wk.Point) <= ArriveXZ)
                {
                    Patches_Xnpc.Count("retreat-backoff-done");
                    ScRetreat.BackedOff(self, gap);
                    walks.Remove(self.entityId);
                    Stop(_context);
                    return;
                }
                if (now >= wk.CheckAt)
                {
                    if (gap < wk.GapAtCheck + GainNeed)
                    {
                        ScRetreat.Cornered(self, t.EntityName + " (" + t.entityId + ") gains on " + ScRetreat.HerObj(self) + ": the gap " + gap.ToString("0.#") + " m did not grow 1 m in " + GainSeconds.ToString("0") + " s");
                        ScRetreat.ShootBetweenSteps(self, gap);               // round k1: running from a feral is lost time - she shoots
                        walks.Remove(self.entityId);
                        Stop(_context);
                        return;
                    }
                    wk.GapAtCheck = gap;
                    wk.CheckAt = now + GainSeconds;
                }
                if (self.getNavigator() != null && self.getNavigator().noPathAndNotPlanningOne())
                {
                    if (!wk.Retried && now < wk.Deadline)
                    {
                        wk.Retried = true;
                        self.FindPath(wk.Point, ScRetreat.RunSpeed(self), false, null);
                        return;
                    }
                    ScRetreat.Cornered(self, "no path to the back-off point");
                    walks.Remove(self.entityId);
                    Stop(_context);
                    return;
                }
                if (now > wk.Deadline) { walks.Remove(self.entityId); Stop(_context); }
            }
            catch (System.Exception e)
            {
                ScLog.Once("standoff-task-update", e);
                Stop(_context);
            }
        }

        public override void Stop(Context _context)
        {
            EntityAlive self = _context.Self;
            Walk wk;
            // kept on a stop by another action (resume within its deadline); dropped when it is done, cornered or out of time
            if (walks.TryGetValue(self.entityId, out wk))
            {
                if (Time.time >= wk.Deadline) walks.Remove(self.entityId);
                else wk.StoppedAt = Time.time;
            }
            ScRetreat.BackingOff(self, false, Vector3.zero);
            ScCommunity.DropPath(self);
            if (self.moveHelper != null) self.moveHelper.CanBreakBlocks = true;
            base.Stop(_context);
        }
    }
}
