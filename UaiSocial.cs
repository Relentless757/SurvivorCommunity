#if SC_V34
using System.Collections.Generic;
using SurvivorCommunity;
using UnityEngine;

// Round U3: the two UAI actions of the community's social life (Config/utilityai.xml, SCResident's ScChat and ScGather and
// SCEnclave's ScEncChat and ScEncGather). The considerations only READ ScSocial, whose state the 1 Hz ticker writes; the
// tasks move and turn the body. Task objects are shared singletons (one per <task> element): per-entity state by entityId.
namespace UAI
{
    /// <summary>
    /// ScSmallTalkHeld: 1 while ScSocial holds her for small talk - in an exchange, or waiting, turned to a neighbor who walks
    /// over to talk to her. A pure read.
    /// </summary>
    public class UAIConsiderationScSmallTalkHeld : UAIConsiderationBase
    {
        public override float GetScore(Context _context, object target)
        {
            return ScSocial.Holds(_context.Self) ? 1f : 0f;
        }
    }

    /// <summary>ScGathering: 1 while she has a place at a campfire gathering (walking to it or sitting there). A pure read.</summary>
    public class UAIConsiderationScGathering : UAIConsiderationBase
    {
        public override float GetScore(Context _context, object target)
        {
            return ScSocial.Gathering(_context.Self) ? 1f : 0f;
        }
    }

    /// <summary>
    /// ScChat's task: she stands where she is (the path and the move dropped) and keeps turned to the one she talks with, or
    /// to the neighbor walking over (0-XNPCCore's SdcsResidentChat.Face, every update). Stops when ScSocial lets her go; the
    /// look is released as 0-XNPCCore releases it.
    /// </summary>
    public class UAITaskChatHoldSC : UAITaskBase
    {
        public override void Start(Context _context)
        {
            base.Start(_context);
            ScCommunity.DropPath(_context.Self);
        }

        public override void Update(Context _context)
        {
            EntityAlive self = _context.Self;
            EntityAlive other = ScSocial.FaceTarget(self);
            if (other == null) { Stop(_context); return; }
            ScSocial.Face(self, other);
        }

        public override void Stop(Context _context)
        {
            EntityAlive self = _context.Self;
            if (self != null && !self.IsDead()) self.SetLookPosition(Vector3.zero);
            base.Stop(_context);
        }
    }

    /// <summary>
    /// ScGather's task: a walk to her place on the ring around the fire, then standing there turned to it until ScSocial lets
    /// her go (CampfireMinSeconds..MaxSeconds after she got there). A walk with no path asks again every 2 s; the ticker gives
    /// her GatherWalkSeconds to get there.
    /// </summary>
    public class UAITaskGatherSC : UAITaskBase
    {
        private const float ArriveMeters = 0.9f;
        private readonly Dictionary<int, float> retryAt = new Dictionary<int, float>();
        private readonly HashSet<int> standing = new HashSet<int>();

        public override void Start(Context _context)
        {
            base.Start(_context);
            EntityAlive self = _context.Self;
            int id = self.entityId;
            standing.Remove(id);
            retryAt.Remove(id);
            Vector3 spot;
            Vector3i fire;
            bool arrived;
            if (!ScSocial.GatherSpotOf(self, out spot, out fire, out arrived)) { Stop(_context); return; }
            if (Near(self, spot)) { Stand(self); return; }
            self.moveHelper.CanBreakBlocks = false;
            self.FindPath(spot, self.GetMoveSpeed(), false, null);
            retryAt[id] = Time.time + 2f;
        }

        public override void Update(Context _context)
        {
            EntityAlive self = _context.Self;
            int id = self.entityId;
            Vector3 spot;
            Vector3i fire;
            bool arrived;
            if (!ScSocial.GatherSpotOf(self, out spot, out fire, out arrived)) { Stop(_context); return; }
            if (standing.Contains(id) || Near(self, spot))
            {
                if (!standing.Contains(id)) Stand(self);
                Vector3 look = new Vector3(fire.x + 0.5f, fire.y + 0.6f, fire.z + 0.5f);
                self.SetLookPosition(look);
                self.RotateTo(look.x, look.y, look.z, 30f, 0f);
                return;
            }
            float t;
            if (self.getNavigator().noPathAndNotPlanningOne() && (!retryAt.TryGetValue(id, out t) || Time.time >= t))
            {
                retryAt[id] = Time.time + 2f;
                self.FindPath(spot, self.GetMoveSpeed(), false, null);
            }
            self.SetLookPosition(spot);
        }

        private static bool Near(EntityAlive self, Vector3 spot)
        {
            Vector3 d = self.position - spot;
            return d.x * d.x + d.z * d.z <= ArriveMeters * ArriveMeters && Mathf.Abs(d.y) < 1.6f;
        }

        private void Stand(EntityAlive self)
        {
            standing.Add(self.entityId);
            ScCommunity.DropPath(self);
        }

        public override void Stop(Context _context)
        {
            EntityAlive self = _context.Self;
            int id = self.entityId;
            standing.Remove(id);
            retryAt.Remove(id);
            self.getNavigator().clearPath();
            if (self.moveHelper != null) { self.moveHelper.Stop(); self.moveHelper.CanBreakBlocks = true; }
            if (!self.IsDead()) self.SetLookPosition(Vector3.zero);
            base.Stop(_context);
        }
    }
}
#endif
