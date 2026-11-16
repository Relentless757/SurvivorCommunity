using System.Collections.Generic;
using SurvivorCommunity;
using UnityEngine;

// The retreat's UAI classes (rounds rt1 and k5, research/flee_meds/DESIGN.md Part A): four considerations, each a PURE READ
// of the state ScRetreat.Update keeps in the X5 tick (trap 1: UAI runs every consideration for every candidate several times
// a second, whatever the gates above it scored), each with a logged-once try/catch that falls back to the behavior before
// rt1; and the shelter task, a shared singleton with per-entity state (UaiOps.cs's note), whose Start resumes rather than
// re-picks when SCore re-starts a finished task. Config/utilityai.xml wires them; RetreatHome=false makes every one of them
// answer as before (the gates 1, ScRetreat 0). Round k5 removed rt1's stand-off (ScStandOff, StandOffSC): a hurt member with
// a gun kites with the posture's wide band (ScPosture action="wide" on SCResident's ScKiteWide, 0-XNPCCore's own back-off
// task) - one back-off machinery, not two.
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
    /// On ScFleeHurt (for="hurt", the default) and ScFlee (for="coward"): 0 where the retreat owns her or would - a member
    /// at home, checked live (trap 9: never a FleeFromTarget out of the base through a stale state); away from home a
    /// follower who kites with her gun - else 1: away from home as before, RetreatHome off, and for the cowards'
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
    /// 0 while the retreat owns her (Shelter, or KITE hurt), else 1 - one owner of her distance. On SCResident's ScDefendBase,
    /// ScDefendHold, ScInvestigateWalk and ScReturn (the leash would walk her to her post mid-retreat), and (the Fable review,
    /// finding 4) 0-XNPCCore's Guard (3), which walked her to her post through every reload and fight back. A throw is logged
    /// once and means 1. only="shelter": 0 only while she SHELTERS (kept for a config that still uses it; round k4 folded the
    /// back-off gates into UAIConsiderationScPosture, which reads ScRetreat.Shelters itself).
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
}
