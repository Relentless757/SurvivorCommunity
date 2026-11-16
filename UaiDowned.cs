using System.Collections.Generic;
using SurvivorCommunity;
using UnityEngine;

// Round U2's UAI classes (2026-11-16; the user's decision D5: resident medics revive): SCResident's ScRevive (Config/utilityai.xml,
// 8.25) walks a resident medic to a downed member at home. The consideration is a PURE READ of ScDowned's state, which only
// ScDowned.Tick writes (ScTicker, once a second) - apart from the task's write-back NoteFight, as UAITaskRetreatSC writes back to
// ScRetreat. The task only walks and holds; in reach ScDowned.Tick hands her to 0-XNPCCore's own medic sequence (SdcsMedic.Begin,
// whose busy clip at 12 then outranks this action), so the item, the get-up and the revive's record are 0-XNPCCore's.
namespace UAI
{
    /// <summary>
    /// SCResident's ScRevive (8.25, IsSelf): 1 while ScDowned has sent her to a downed member who is still down, the treatment
    /// has not started and she is not fighting back at what is after her; else 0. A throw is logged once and means 0.
    /// </summary>
    public class UAIConsiderationScReviving : UAIConsiderationBase
    {
        public override float GetScore(Context _context, object target)
        {
            try
            {
                return ScDowned.Walking(_context.Self) ? 1f : 0f;
            }
            catch (System.Exception e)
            {
                ScLog.Once("uai-reviving", e);
                return 0f;
            }
        }
    }

    /// <summary>
    /// ScRevive's task: run to the downed member and stand beside her, facing her. FindPath at ScRetreat.RunSpeed (trap 11),
    /// never an SCore move task; the move helper breaks no block. Re-pathed every RepathSeconds, or at once when the path is
    /// gone (SCore clears a stalled human's path). Four times a second it looks for an enemy after her within
    /// ScDowned.FightBackRange: then it notes the fight (ScDowned.NoteFight, 2.5 s) and stops - the consideration is 0 meanwhile
    /// and the pack's attacks take her - and she goes on after. Progress, the reach and the treatment are ScDowned.Tick's.
    /// </summary>
    public class UAITaskReviveSC : UAITaskBase
    {
        private sealed class Walk
        {
            public float NextPath, NextScan, LastPath;
            public bool Holding;
        }

        private readonly Dictionary<int, Walk> walks = new Dictionary<int, Walk>();
        private const float RepathSeconds = 2f, ScanSeconds = 0.25f, ArriveXZ = 1.6f, ArriveDy = 1.5f;

        public override void Start(Context _context)
        {
            base.Start(_context);
            EntityAlive self = _context.Self;
            try
            {
                EntityAlive p;
                // SCore re-starts a finished task before the action is scored again: bail out here when it no longer holds
                if (!ScDowned.Walking(self) || !ScDowned.WalkTarget(self, out p)) { Stop(_context); return; }
                Walk wk = new Walk();
                walks[self.entityId] = wk;
                if (self.moveHelper != null) self.moveHelper.CanBreakBlocks = false;
                Go(self, p, wk, Time.time);
            }
            catch (System.Exception e)
            {
                ScLog.Once("revive-task-start", e);
                Stop(_context);
            }
        }

        private static void Go(EntityAlive self, EntityAlive p, Walk wk, float now)
        {
            wk.NextPath = now + RepathSeconds;
            wk.LastPath = now;
            if (Arrived(self, p))
            {
                if (!wk.Holding) ScCommunity.DropPath(self);
                wk.Holding = true;
                return;
            }
            wk.Holding = false;
            self.FindPath(p.position, ScRetreat.RunSpeed(self), false, null);
        }

        private static bool Arrived(EntityAlive self, EntityAlive p)
        {
            return ScRunners.XZ(self.position, p.position) <= ArriveXZ && Mathf.Abs(self.position.y - p.position.y) < ArriveDy;
        }

        public override void Update(Context _context)
        {
            EntityAlive self = _context.Self;
            try
            {
                Walk wk;
                EntityAlive p;
                if (!ScDowned.Walking(self) || !ScDowned.WalkTarget(self, out p) || !walks.TryGetValue(self.entityId, out wk)) { Stop(_context); return; }
                float now = Time.time;
                if (now >= wk.NextScan)
                {
                    wk.NextScan = now + ScanSeconds;
                    EntityAlive foe = ScDowned.FoeOnMe(self);
                    if (foe != null)
                    {
                        ScDowned.NoteFight(self, foe);
                        Stop(_context);
                        return;
                    }
                }
                if (Arrived(self, p))
                {
                    if (!wk.Holding) { ScCommunity.DropPath(self); wk.Holding = true; }
                    else if (self.moveHelper != null && self.moveHelper.IsActive) self.moveHelper.Stop();
                    Vector3 look = p.position + new Vector3(0f, 0.5f, 0f);
                    self.SetLookPosition(look);
                    self.RotateTo(look.x, look.y, look.z, 30f, 0f);
                    return;
                }
                bool noPath = self.getNavigator() != null && self.getNavigator().noPathAndNotPlanningOne();
                if (wk.Holding || now >= wk.NextPath || (noPath && now >= wk.LastPath + 0.5f)) Go(self, p, wk, now);
            }
            catch (System.Exception e)
            {
                ScLog.Once("revive-task-update", e);
                Stop(_context);
            }
        }

        public override void Stop(Context _context)
        {
            EntityAlive self = _context.Self;
            try
            {
                if (self != null)
                {
                    walks.Remove(self.entityId);
                    ScCommunity.DropPath(self);
                    if (self.moveHelper != null) self.moveHelper.CanBreakBlocks = true;
                }
            }
            catch (System.Exception e) { ScLog.Once("revive-task-stop", e); }
            base.Stop(_context);
        }
    }
}
