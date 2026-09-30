using System.Collections.Generic;
using SurvivorCommunity;
using UnityEngine;

// Utility AI classes must be discoverable as UAI.UAITask<Name> / UAI.UAIConsideration<Name>:
//   <task class="WanderHomeSC, SurvivorCommunity"/>   <consideration class="ScIsMember, SurvivorCommunity"/>
// Task objects are SHARED SINGLETONS - one per <task> element, used by every NPC of the package - so every bit
// of per-entity state below is keyed by entityId.
namespace UAI
{
    /// <summary>
    /// Per-entity roam bookkeeping shared by the task and its readiness consideration. The AI re-scores every
    /// 0.2 s and switches away from an action the moment it stops scoring, so a cooldown that is consumed
    /// during scoring would end a roam after one decision window. Instead: the consideration stays true while
    /// the task is running for that entity, and the cooldown (real seconds) starts when the task stops.
    /// </summary>
    public static class ScRoamState
    {
        public static readonly HashSet<int> Active = new HashSet<int>();
        public static readonly Dictionary<int, float> NextAllowed = new Dictionary<int, float>();

        public static bool Ready(int id)
        {
            if (Active.Contains(id)) return true;
            float t;
            return !NextAllowed.TryGetValue(id, out t) || Time.time >= t;
        }
    }

    /// <summary>Roams a few blocks around the member's post, never leaving the home zone. radius="12" cooldown="45" (real seconds).</summary>
    public class UAITaskWanderHomeSC : UAITaskBase
    {
        private int radius = 12;
        private float cooldown = 45f;
        private readonly Dictionary<int, Vector3> targets = new Dictionary<int, Vector3>();
        private readonly Dictionary<int, float> deadlines = new Dictionary<int, float>();
        private const float MaxSeconds = 20f;   // of walking before giving up on a target (real time - ticks are frame-rate bound)
        private const float RetryBackoff = 5f;  // when no target could be found

        public override void initializeParameters()
        {
            base.initializeParameters();
            if (Parameters.ContainsKey("radius")) radius = Mathf.Max(2, StringParsers.ParseSInt32(Parameters["radius"]));
            if (Parameters.ContainsKey("cooldown")) cooldown = Mathf.Max(0f, StringParsers.ParseFloat(Parameters["cooldown"]));
        }

        public override void Start(Context _context)
        {
            base.Start(_context);
            EntityAlive self = _context.Self;
            int id = self.entityId;
            Member m = ScRegistry.GetMember(id);
            if (m == null || m.Post == Vector3i.zero) { Stop(_context); return; }
            // SCore's updateAction re-Starts a finished task on the next AI tick, before chooseAction sees the
            // consideration: a Start during the cooldown must fail without touching the cooldown.
            if (!ScRoamState.Ready(id)) { Stop(_context); return; }

            Vector3 post = new Vector3(m.Post.x + 0.5f, m.Post.y, m.Post.z + 0.5f);
            Vector3 target = Vector3.zero;
            World w = self.world;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                // CalcNear validates nothing - check the cell ourselves
                Vector3 p = RandomPositionGenerator.CalcNear(self, post, radius, 2);
                Vector3i bp = World.worldToBlockPos(p);
                if (!ScZone.Contains(bp)) continue;
                if (w != null && !ScCommunity.Standable(w, bp.x, bp.y, bp.z)) continue;
                target = new Vector3(bp.x + 0.5f, bp.y, bp.z + 0.5f);
                break;
            }
            if (target == Vector3.zero)
            {
                ScRoamState.NextAllowed[id] = Time.time + RetryBackoff;   // hand the entity to Guard instead of retrying every tick
                Stop(_context);
                return;
            }

            targets[id] = target;
            deadlines[id] = Time.time + MaxSeconds;
            ScRoamState.Active.Add(id);
            self.moveHelper.CanBreakBlocks = false;
            self.FindPath(target, self.GetMoveSpeed(), false, null);
        }

        public override void Update(Context _context)
        {
            EntityAlive self = _context.Self;
            Vector3 target;
            if (!targets.TryGetValue(self.entityId, out target)) { Stop(_context); return; }
            float deadline;
            deadlines.TryGetValue(self.entityId, out deadline);
            if (Time.time > deadline || self.getNavigator().noPathAndNotPlanningOne() || Vector3.Distance(self.position, target) < 1.5f
                || (self.moveHelper != null && self.moveHelper.BlockedTime > 2f))     // furniture / doorway: stop pushing
            {
                Stop(_context);
                return;
            }
            self.SetLookPosition(target);
        }

        public override void Stop(Context _context)
        {
            EntityAlive self = _context.Self;
            int id = self.entityId;
            targets.Remove(id);
            deadlines.Remove(id);
            if (ScRoamState.Active.Remove(id)) ScRoamState.NextAllowed[id] = Time.time + cooldown;
            self.getNavigator().clearPath();
            if (self.moveHelper != null) { self.moveHelper.Stop(); self.moveHelper.CanBreakBlocks = true; }
            base.Stop(_context);
        }
    }

    /// <summary>1 when the entity is a community member (registry lookup - cheaper and truer than the cvar).</summary>
    public class UAIConsiderationScIsMember : UAIConsiderationBase
    {
        public override float GetScore(Context _context, object target)
        {
            return ScRegistry.GetMember(_context.Self.entityId) != null ? 1f : 0f;
        }
    }

    public class UAIConsiderationScIsDaytime : UAIConsiderationBase
    {
        public override float GetScore(Context _context, object target)
        {
            World w = GameManager.Instance != null ? GameManager.Instance.World : null;
            return (w != null && w.IsDaytime()) ? 1f : 0f;
        }
    }

    public class UAIConsiderationScIsNight : UAIConsiderationBase
    {
        public override float GetScore(Context _context, object target)
        {
            World w = GameManager.Instance != null ? GameManager.Instance.World : null;
            return (w != null && !w.IsDaytime()) ? 1f : 0f;
        }
    }

    /// <summary>1 while a roam is running for this entity, or once its cooldown has elapsed; 0 during the cooldown.</summary>
    public class UAIConsiderationScRoamReady : UAIConsiderationBase
    {
        public override float GetScore(Context _context, object target)
        {
            return ScRoamState.Ready(_context.Self.entityId) ? 1f : 0f;
        }
    }

    /// <summary>1 when the member stands within distance="4" blocks of its post (so Guard walks it home first).</summary>
    /// <summary>1 while the member is further from their post than "distance" (the leash).</summary>
    public class UAIConsiderationScFarFromPost : UAIConsiderationBase
    {
        private float distance = 25f;

        public override void Init(Dictionary<string, string> parameters)
        {
            base.Init(parameters);
            if (parameters.ContainsKey("distance")) distance = StringParsers.ParseFloat(parameters["distance"]);
        }

        public override float GetScore(Context _context, object target)
        {
            Member m = ScRegistry.GetMember(_context.Self.entityId);
            if (m == null || m.Post == Vector3i.zero) return 0f;
            Vector3 post = new Vector3(m.Post.x + 0.5f, m.Post.y, m.Post.z + 0.5f);
            return Vector3.Distance(_context.Self.position, post) > distance ? 1f : 0f;
        }
    }

    public class UAIConsiderationScNearPost : UAIConsiderationBase
    {
        private float distance = 4f;

        public override void Init(Dictionary<string, string> parameters)
        {
            base.Init(parameters);
            if (parameters.ContainsKey("distance")) distance = StringParsers.ParseFloat(parameters["distance"]);
        }

        public override float GetScore(Context _context, object target)
        {
            Member m = ScRegistry.GetMember(_context.Self.entityId);
            if (m == null || m.Post == Vector3i.zero) return 0f;
            Vector3 post = new Vector3(m.Post.x + 0.5f, m.Post.y, m.Post.z + 0.5f);
            return Vector3.Distance(_context.Self.position, post) <= distance ? 1f : 0f;
        }
    }

    /// <summary>1 while the entity is a physical runner that is in the world (Outbound / AtSite / Inbound / Back).</summary>
    public class UAIConsiderationScTravelling : UAIConsiderationBase
    {
        public override float GetScore(Context _context, object target)
        {
            Member m = ScRegistry.GetMember(_context.Self.entityId);
            return ScRunners.IsPhysical(m) && !m.Offline ? 1f : 0f;
        }
    }

    /// <summary>
    /// Walks a physical supply runner along its leg, one waypoint (RunWaypointSpacing) at a time: a single path
    /// to a POI hundreds of metres away cannot exist, the A* grid only covers the player's surroundings plus the
    /// location lines the runner keeps alive around itself. At the site it wanders the POI's ground floor; pinned
    /// by a distress call it holds its spot. Shared singleton: all state is keyed by entityId (ScRunners.Session).
    /// </summary>
    public class UAITaskTravelSC : UAITaskBase
    {
        private readonly Dictionary<int, float> pauseUntil = new Dictionary<int, float>();
        private readonly Dictionary<int, int> repaths = new Dictionary<int, int>();

        public override void Start(Context _context)
        {
            base.Start(_context);
            EntityAlive self = _context.Self;
            Member m = ScRegistry.GetMember(self.entityId);
            if (!ScRunners.IsPhysical(m) || m.Offline) { Stop(_context); return; }
            if (self.moveHelper != null) self.moveHelper.CanBreakBlocks = false;
            PathTo(self, m, !ScRunners.S(self.entityId).HasWaypoint);
        }

        private void PathTo(EntityAlive self, Member m, bool newWaypoint)
        {
            ScRunners.Session s = ScRunners.S(self.entityId);
            World w = self.world;
            if (newWaypoint || !s.HasWaypoint)
            {
                Vector3 goal = ScRunners.LegGoal(m);
                if (m.RunLeg == RunLeg.AtSite && !ScRunners.Pinned(m)) goal = ScRunners.SiteWanderGoal(w, self, m);
                Vector3 wp;
                if (!ScRunners.NextWaypoint(w, self.position, s.Sidestep, goal, out wp)) { s.HasWaypoint = false; return; }   // the watchdog sidesteps, skips or hands off
                s.Waypoint = wp;
                s.HasWaypoint = true;
                s.WaypointSince = Time.unscaledTime;
            }
            EntityPlayer p = w.GetClosestPlayer(self, -1f, false);
            if (AstarManager.Instance != null && (p == null || ScRunners.XZ(p.position, self.position) > ScConfig.RunGridFarFromPlayer))
                AstarManager.Instance.AddLocationLine(self.position, s.Waypoint, ScConfig.RunGridSize);
            self.FindPath(s.Waypoint, self.GetMoveSpeed(), false, null);                   // walking pace
        }

        public override void Update(Context _context)
        {
            EntityAlive self = _context.Self;
            Member m = ScRegistry.GetMember(self.entityId);
            if (!ScRunners.IsPhysical(m) || m.Offline) { Stop(_context); return; }
            ScRunners.Session s = ScRunners.S(self.entityId);
            // XNPCCore's MoveToFindTarget (weight 5) turns a ranged NPC toward any noise the escorting player makes and
            // keeps doing it for a minute: a runner only reacts to fresh noise, or it flip-flops with this task
            if (self.HasInvestigatePosition) self.ClearInvestigatePosition();
            self.noisePlayer = null;
            self.noisePlayerVolume = 0f;
            float pu;
            if (pauseUntil.TryGetValue(self.entityId, out pu) && Time.time < pu) return;      // looking around at the site
            if (!s.HasWaypoint) { PathTo(self, m, true); return; }

            Vector3 d = s.Waypoint - self.position;
            d.y = 0f;
            if (d.magnitude <= ScConfig.RunArriveRadius)
            {
                if (ScRunners.Pinned(m)) { self.getNavigator().clearPath(); if (self.moveHelper != null) self.moveHelper.Stop(); return; }   // hold the spot; attacks still win
                if (m.RunLeg == RunLeg.AtSite) pauseUntil[self.entityId] = Time.time + 4f + (self.rand != null ? self.rand.RandomFloat * 8f : 4f);
                repaths[self.entityId] = 0;
                s.HasWaypoint = false;
                return;
            }
            if (self.getNavigator().noPathAndNotPlanningOne())
            {
                // SCore's human Stop patch clears the path on every stall: re-path, then accept where a partial path ended
                int n;
                repaths.TryGetValue(self.entityId, out n);
                repaths[self.entityId] = ++n;
                if (d.magnitude <= ScConfig.RunPartialArrive || n > 4) { repaths[self.entityId] = 0; s.HasWaypoint = false; PathTo(self, m, true); }
                else PathTo(self, m, false);
                return;
            }
            self.SetLookPosition(s.Waypoint);
        }

        public override void Stop(Context _context)
        {
            EntityAlive self = _context.Self;
            pauseUntil.Remove(self.entityId);
            repaths.Remove(self.entityId);
            if (self.getNavigator() != null) self.getNavigator().clearPath();
            if (self.moveHelper != null) { self.moveHelper.Stop(); self.moveHelper.CanBreakBlocks = true; }
            base.Stop(_context);
        }
    }
}
