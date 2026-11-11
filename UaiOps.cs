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

    /// <summary>
    /// The ground a body lives on (E2): the home zone for residents (ScZone: the claim's rectangles), an enclave's POI
    /// footprint for its people. The roam, the leash and the defence read it instead of the static zone, so one set of
    /// actions serves both; for residents every call is exactly the ScZone call it replaced. One instance for the zone,
    /// one per enclave (ScEnclavePeople.AreaOf), reading the enclave record's live footprint.
    /// </summary>
    public sealed class ScArea
    {
        public static readonly ScArea HomeZone = new ScArea(null);
        private readonly Enclave enclave;

        public ScArea(Enclave e) { enclave = e; }

        /// <summary>The enclave whose footprint this is (null for the home zone).</summary>
        public Enclave Enclave { get { return enclave; } }

        public bool Contains(Vector3i p) { return enclave == null ? ScZone.Contains(p) : enclave.Contains(p.x, p.z); }

        /// <summary>Horizontal metres outside the ground (0 inside it).</summary>
        public float DistanceOutside(Vector3 p) { return enclave == null ? ScZone.DistanceOutside(p) : enclave.DistanceXZ(p.x, p.z); }

        /// <summary>A random point of the ground (y 0): ScZone's for the zone, a cell of the footprint for an enclave.</summary>
        public bool RandomPoint(out Vector3 p)
        {
            if (enclave == null) return ScZone.RandomPoint(out p);
            Vector3i a = enclave.Poi, s = enclave.Size;
            p = new Vector3(Random.Range(a.x, a.x + Mathf.Max(1, s.x)) + 0.5f, 0f, Random.Range(a.z, a.z + Mathf.Max(1, s.z)) + 0.5f);
            return true;
        }
    }

    /// <summary>
    /// Where a body belongs (E2), resolved per body for the shared actions: a member - the resident's post and the home
    /// zone, looked up first and unchanged - or an enclave person's current body - their home spot and the enclave's
    /// footprint (ScEnclavePeople.PersonOf: one dictionary hit). A struct: the AI asks on every decision.
    /// </summary>
    public struct ScHome
    {
        /// <summary>The member's record, or the enclave person's Member (Who).</summary>
        public Member Member;
        /// <summary>The enclave person; null for a member.</summary>
        public EncPerson Person;
        public ScArea Area;
        /// <summary>The post's (the spot's) cell centre on its floor.</summary>
        public Vector3 Post;

        public bool IsEnclave { get { return Person != null; } }

        /// <summary>False with no member with a post and no enclave person with a spot.</summary>
        public static bool Of(EntityAlive self, out ScHome h)
        {
            h = default(ScHome);
            if (self == null) return false;
            Member m = ScRegistry.GetMember(self.entityId);
            if (m != null)
            {
                if (m.Post == Vector3i.zero) return false;
                h.Member = m;
                h.Area = ScArea.HomeZone;
                h.Post = ScCommunity.PostCenter(m);
                return true;
            }
            EncPerson p = ScEnclavePeople.PersonOf(self);
            if (p == null || !p.HasHome) return false;
            h.Area = ScEnclavePeople.AreaOf(p.EnclaveId);
            if (h.Area == null) return false;
            h.Member = p.Who;
            h.Person = p;
            h.Post = p.HomeCentre;
            return true;
        }
    }

    /// <summary>
    /// The daytime roam. With ResidentRoamZone (round hz, the default; the user: "They should be able to walk about inside
    /// the base rather than find somewhere to stand"): a walk to a random standable floor anywhere in the home zone, then
    /// ResidentRoamPauseMin..Max real seconds standing where the walk ended, then the next walk at once (no cooldown: a gap
    /// of even a second scored ScRoam 0, and NPCModCore's Guard set off running for the post - the review's F1); radius and
    /// cooldown are not used, and a walk with no spot to go to is a pause too. Without it: a few blocks around the member's
    /// post, never leaving the home zone,
    /// radius="12" cooldown="45" (real seconds), and Guard walks them back to the post in between.
    /// E2: an enclave's people (SCEnclave's ScEncRoam) roam their POI's footprint the same way, from their home spot, always
    /// with the zone roam's walks and pauses (ScHome resolves which ground a body has; a resident's is the zone as before).
    /// Round 29b (M29, the wreck findings' option c): a walk that gets no 1 m closer to its spot, and no 1 m further along its
    /// path, in RoamNoProgressSeconds (8 s; a body jittering against a wreck's hull is neither blocked 2 s nor out of time;
    /// ScStuck.Progress) ends as a blocked one does, and
    /// the spot goes on the body's unreachable list for RoamUnreachableMinutes (ScStuck.MarkUnreachable): it is not picked
    /// again, nor a spot beside it.
    /// </summary>
    public class UAITaskWanderHomeSC : UAITaskBase
    {
        private int radius = 12;
        private float cooldown = 45f;
        private readonly Dictionary<int, Vector3> targets = new Dictionary<int, Vector3>();
        private readonly Dictionary<int, float> deadlines = new Dictionary<int, float>();
        private readonly Dictionary<int, float> pauseUntil = new Dictionary<int, float>();
        private readonly HashSet<int> retried = new HashSet<int>();
        // round 29b: each walk's progress (NoProgress)
        private readonly Dictionary<int, ScStuck.Progress> walks = new Dictionary<int, ScStuck.Progress>();
        private const float MaxSeconds = 20f;   // of walking before giving up on a target (real time - ticks are frame-rate bound)
        private const float MaxZoneSeconds = 90f;   // zone roaming: the walk's limit grows with the distance, up to this
        private const float RetryBackoff = 5f;  // when no target could be found
        private const float ZoneCooldown = 0f;  // zone roaming: the pause is the rest between walks (F1: any gap hands over to Guard)
        private const float MinZoneStep = 6f;   // zone roaming: a walk is at least this long (horizontal metres)

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
            pauseUntil.Remove(id);   // a pause left by a body that unloaded mid-pause (no Stop; the same id comes back)
            // a resident's post and the home zone, or an enclave person's spot and the POI's footprint (E2)
            ScHome h;
            if (!ScHome.Of(self, out h)) { Stop(_context); return; }
            // SCore's updateAction re-Starts a finished task on the next AI tick, before chooseAction sees the
            // consideration: a Start during the cooldown must fail without touching the cooldown.
            if (!ScRoamState.Ready(id)) { Stop(_context); return; }

            Vector3 post = h.Post;
            Vector3 target = Vector3.zero;
            World w = self.world;
            bool zone = h.IsEnclave || ScConfig.ResidentRoamZone;
            if (zone) target = ZoneTarget(self, post, w, h.Area);
            for (int attempt = 0; !zone && attempt < 6 && target == Vector3.zero; attempt++)
            {
                // CalcNear validates nothing - check the cell ourselves. A real floor (the M11 fix 2, STUCK_DIAG: the old
                // "two air cells over anything" aimed roams at furniture tops), at the post's level or up to 2 off it
                Vector3 p = RandomPositionGenerator.CalcNear(self, post, radius, 0);
                for (int step = 0; step <= 4; step++)
                {
                    Vector3i bp = World.worldToBlockPos(p) + new Vector3i(0, (step + 1) / 2 * (step % 2 == 0 ? 1 : -1), 0);
                    if (!h.Area.Contains(bp)) break;
                    if (ScStuck.Unreachable(id, bp)) continue;      // round 29b: not reached lately
                    string why;
                    if (w != null && !ScCommunity.GoodPost(w, bp, false, out why)) continue;
                    target = new Vector3(bp.x + 0.5f, bp.y, bp.z + 0.5f);
                    break;
                }
            }
            if (target == Vector3.zero && zone)
            {
                // nowhere to go just now: a pause where they stand (the task keeps running, so Guard does not take over)
                ScRoamState.Active.Add(id);
                Pause(self, 1f);   // at least a second: a 0 s pause config would search for a spot every few frames
                return;
            }
            if (target == Vector3.zero)
            {
                ScRoamState.NextAllowed[id] = Time.time + RetryBackoff;   // hand the entity to Guard instead of retrying every tick
                Stop(_context);
                return;
            }

            targets[id] = target;
            deadlines[id] = Time.time + (zone ? Mathf.Clamp(Vector3.Distance(self.position, target) * 1.2f + 8f, MaxSeconds, MaxZoneSeconds) : MaxSeconds);
            walks[id] = new ScStuck.Progress();
            ScRoamState.Active.Add(id);
            self.moveHelper.CanBreakBlocks = false;
            self.FindPath(target, self.GetMoveSpeed(), false, null);
        }

        public override void Update(Context _context)
        {
            EntityAlive self = _context.Self;
            float until;
            if (pauseUntil.TryGetValue(self.entityId, out until))
            {
                if (Time.time >= until) Stop(_context);   // a pause where the walk ended: standing, nothing to push at
                return;
            }
            Vector3 target;
            if (!targets.TryGetValue(self.entityId, out target)) { Stop(_context); return; }
            float deadline;
            deadlines.TryGetValue(self.entityId, out deadline);
            // SCore clears a stalled human's path (a door leaf still swinging open in front of them): try once more
            if (Time.time <= deadline && self.getNavigator().noPathAndNotPlanningOne() && Vector3.Distance(self.position, target) >= 1.5f
                && retried.Add(self.entityId))
            {
                self.FindPath(target, self.GetMoveSpeed(), false, null);
                return;
            }
            if (Time.time > deadline || self.getNavigator().noPathAndNotPlanningOne() || Vector3.Distance(self.position, target) < 1.5f
                || (self.moveHelper != null && self.moveHelper.BlockedTime > 2f))     // furniture / doorway: stop pushing
            {
                if (ZoneRoam(self)) { Pause(self); return; }
                Stop(_context);
                return;
            }
            // round 29b: no 1 m closer in RoamNoProgressSeconds - pushing at a wreck's hull, or sidestepping against it: the
            // walk ends as a blocked one does, and the spot is not picked again for a while
            if (NoProgress(self, target))
            {
                if (ZoneRoam(self)) { Pause(self); return; }
                Stop(_context);
                return;
            }
            self.SetLookPosition(target);
        }

        /// <summary>
        /// Round 29b: true (the spot marked unreachable) after RoamNoProgressSeconds of walking without a 1 m gain - 1 m closer
        /// to the spot, or 1 m less of the path left to walk (a detour round a wall gains on its path while it leads away).
        /// </summary>
        private bool NoProgress(EntityAlive self, Vector3 target)
        {
            float limit = ScConfig.RoamNoProgressSeconds;
            if (limit <= 0f) return false;
            int id = self.entityId;
            ScStuck.Progress p;
            if (!walks.TryGetValue(id, out p)) { p = new ScStuck.Progress(); walks[id] = p; }
            float now = Time.time;
            float d = Vector3.Distance(self.position, target);
            if (p.Gained(self, d)) { p.GainAt = now; return false; }
            if (now - p.GainAt < limit) return false;
            ScStuck.MarkUnreachable(self, target, d);
            return true;
        }

        /// <summary>Roams the whole ground with pauses: residents with ResidentRoamZone, an enclave's people always (E2).</summary>
        private static bool ZoneRoam(EntityAlive self)
        {
            return ScConfig.ResidentRoamZone || ScEnclavePeople.PersonOf(self) != null;
        }

        /// <summary>Zone roaming: the walk is over (there, blocked or out of time) - stand where it ended for a while (atLeast seconds or more).</summary>
        private void Pause(EntityAlive self, float atLeast = 0f)
        {
            float lo = Mathf.Max(0f, ScConfig.ResidentRoamPauseMin), hi = Mathf.Max(lo, ScConfig.ResidentRoamPauseMax);
            pauseUntil[self.entityId] = Time.time + Mathf.Max(atLeast, Random.Range(lo, hi));
            targets.Remove(self.entityId);
            walks.Remove(self.entityId);
            ScCommunity.DropPath(self);   // the path, a request still in the path finder (F5) and the move helper
        }

        /// <summary>
        /// Zone roaming: a random standable floor anywhere in the ground (the home zone; an enclave's footprint, E2), at least
        /// MinZoneStep from the body. The floor is
        /// looked for around the body's own level and the post's (a base has storeys), each up to two cells either way, with
        /// the post's own test (GoodPost, relaxed); then around the column's top (World.GetHeight is the highest non-air
        /// block, not the terrain), only when that is within two cells of one of those levels - a yard a step or two down or
        /// up, never a roof (the review's F3). Zero when none was found in ten tries.
        /// </summary>
        private static Vector3 ZoneTarget(EntityAlive self, Vector3 post, World w, ScArea area)
        {
            if (w == null) return Vector3.zero;
            for (int attempt = 0; attempt < 10; attempt++)
            {
                Vector3 p;
                if (!area.RandomPoint(out p)) return Vector3.zero;
                float dx = p.x - self.position.x, dz = p.z - self.position.z;
                if (dx * dx + dz * dz < MinZoneStep * MinZoneStep) continue;
                int x = Mathf.FloorToInt(p.x), z = Mathf.FloorToInt(p.z);
                int own = Mathf.FloorToInt(self.position.y + 0.05f), atPost = Mathf.FloorToInt(post.y + 0.05f);
                int top = w.GetHeight(x, z) + 1;
                int[] levels = { own, atPost, top };
                int count = top <= Mathf.Max(own, atPost) + 2 ? 3 : 2;
                for (int l = 0; l < count; l++)
                {
                    for (int step = 0; step <= 4; step++)
                    {
                        Vector3i bp = new Vector3i(x, levels[l] + (step + 1) / 2 * (step % 2 == 0 ? 1 : -1), z);
                        if (!area.Contains(bp)) break;
                        if (ScStuck.Unreachable(self.entityId, bp)) continue;      // round 29b: not reached lately
                        string why;
                        if (!ScCommunity.GoodPost(w, bp, false, out why)) continue;
                        return new Vector3(bp.x + 0.5f, bp.y, bp.z + 0.5f);
                    }
                }
            }
            return Vector3.zero;
        }

        public override void Stop(Context _context)
        {
            EntityAlive self = _context.Self;
            int id = self.entityId;
            targets.Remove(id);
            deadlines.Remove(id);
            pauseUntil.Remove(id);
            retried.Remove(id);
            walks.Remove(id);
            if (ScRoamState.Active.Remove(id)) ScRoamState.NextAllowed[id] = Time.time + (ZoneRoam(self) ? ZoneCooldown : cooldown);
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

    /// <summary>
    /// 1 when the entity is a member stationed at home (status Resident). The roam, the leash and the quiet night all work
    /// from the member's HOME post, and a follower can hold the very same Guard order (9): 0-XNPCCore's "guard and
    /// return" voice order is buffOrderGuard. Gated on the member alone, a follower told to guard a spot 500 m out would
    /// be leashed back toward home (ScReturn 5.5 outranks melee) and roam toward it by day; this keeps those actions to
    /// residents, and the follower guards where the player said (NPCModCore's Guard).
    /// </summary>
    public class UAIConsiderationScIsResident : UAIConsiderationBase
    {
        public override float GetScore(Context _context, object target)
        {
            Member m = ScRegistry.GetMember(_context.Self.entityId);
            return m != null && m.Status == MemberStatus.Resident ? 1f : 0f;
        }
    }

    /// <summary>
    /// SCEnclave's ScIsResident (E2): 1 when the entity is the current body of one of an enclave's people, at home and with
    /// a home spot; role="leader" or role="member" narrows it (the leader never roams: the player finds them at their spot).
    /// One dictionary hit (ScEnclavePeople.PersonOf). E3: 0 for one enlisted with the player (their follow order rules) and
    /// for one walking home after it (Leaving: only the Guard order toward their spot, until they are back on it).
    /// </summary>
    public class UAIConsiderationScEncHome : UAIConsiderationBase
    {
        private string role;

        public override void Init(Dictionary<string, string> parameters)
        {
            base.Init(parameters);
            if (parameters.ContainsKey("role")) role = parameters["role"].Trim().ToLowerInvariant();
        }

        public override float GetScore(Context _context, object target)
        {
            EncPerson p = ScEnclavePeople.PersonOf(_context.Self);
            if (p == null || !p.HasHome || p.State != EncPerson.StateHome || p.Leaving) return 0f;
            return string.IsNullOrEmpty(role) || p.Role == role ? 1f : 0f;
        }
    }

    /// <summary>
    /// SCEnclave's hostile-only actions (E2): 1 for a body of an enclave turned hostile whose grace is over, unless it is a
    /// raider (a raider enclave's people carry SCRaider's own approach and investigation already).
    /// </summary>
    public class UAIConsiderationScEncHostile : UAIConsiderationBase
    {
        public override float GetScore(Context _context, object target)
        {
            EncPerson p = ScEnclavePeople.PersonOf(_context.Self);
            // not while walking home after being released by the flip (the E3 review's M1: they leave you first)
            return p != null && !p.Leaving && ScEnclavePeople.HostileActive(p.EnclaveId) && !ScXnpc.IsRaider(_context.Self) ? 1f : 0f;
        }
    }

    /// <summary>
    /// 1 unless the entity is a guard on watch (base defence D3, ScWatch.Guarding) - for everyone else, raiders and
    /// strangers included, it is always 1. Gates every action that would walk a guard off a Watch post: SCResident's roam,
    /// defence walk and hold, quiet night and Cowardly flee, and 0-XNPCCore's BackupKeepDistance, BackupRanged and
    /// MeleeApproach (Config/utilityai.xml).
    /// </summary>
    public class UAIConsiderationScNotOnWatch : UAIConsiderationBase
    {
        public override float GetScore(Context _context, object target)
        {
            return ScWatch.Guarding(_context.Self.entityId) ? 0f : 1f;
        }
    }

    /// <summary>
    /// Round X2 (0-XNPCCore 3.4.03): 0 for a body whose $Timid SC wrote (ScTraits.ScTimidCvar: a member or stand-in, a
    /// runner or garrison body, an enclave's person, a band body), 1 for every other NPC. Appended to 0-XNPCCore's
    /// SdcsNpcCore Flee (weight 9: $Timid, 15 m, sight - no Stay, watch or home gate, and above every SC action), which
    /// 3.4.03 turned back on: SC's cowards keep SC's own ScFlee / ScEncFlee (weight 6, gated on Stay, the watch and the
    /// enclave's home) as their only flight, as before 3.4.03; 0-XNPCCore's own timid survivors keep its Flee.
    /// </summary>
    public class UAIConsiderationScNotScTimid : UAIConsiderationBase
    {
        public override float GetScore(Context _context, object target)
        {
            EntityAlive self = _context.Self;
            return self != null && self.Buffs != null && self.Buffs.GetCustomVar(ScTraits.ScTimidCvar) > 0f ? 0f : 1f;
        }
    }

    /// <summary>
    /// 1 when none of the entity's AI packages has 0-XNPCCore's DynamicFollow (ScXnpc.HasDynamicFollow). SC's own two
    /// follow actions are the fallback for such a member only: wherever DynamicFollow exists - every 0-XNPCCore NPC,
    /// through NPCModNPCHired - its follow and formations replace SC's (decision D6), and SC's actions, at 4 and 4.2,
    /// would otherwise outrank it (2) and fight it.
    /// </summary>
    public class UAIConsiderationScNoDynamicFollow : UAIConsiderationBase
    {
        public override float GetScore(Context _context, object target)
        {
            return ScXnpc.HasDynamicFollow(_context.Self) ? 0f : 1f;
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
    /// <summary>1 while the member is further from their post than "distance" (the leash); an enclave person once outside their POI + EnclaveLeashMargin (E2).</summary>
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
            ScHome h;
            if (!ScHome.Of(_context.Self, out h)) return 0f;
            // an enclave's people (E2): their whole POI and EnclaveLeashMargin round it is home - from anywhere on it
            if (h.IsEnclave) return h.Area.DistanceOutside(_context.Self.position) > ScConfig.EnclaveLeashMargin ? 1f : 0f;
            Member m = h.Member;
            // a live home attack (base defence D2): the leash is the defended area + LeashExtra, not the distance from the post -
            // except for a guard on watch (D3), who is leashed to the Watch post as always
            if (ScBaseAttack.WideDefence && !ScWatch.OnWatch(m)) return ScBaseAttack.InDefenceArea(_context.Self.position, ScBaseAttack.LeashExtra) ? 0f : 1f;
            // zone roaming (round hz): the home zone, and ResidentRoamZoneMargin round it, is home - a walk there is the
            // roam, not a chase; outside it the post leash applies as before. Not for a guard on watch.
            if (ScConfig.ResidentRoamZone && !ScWatch.OnWatch(m)
                && ScZone.DistanceOutside(_context.Self.position) <= ScConfig.ResidentRoamZoneMargin) return 0f;
            return Vector3.Distance(_context.Self.position, h.Post) > distance ? 1f : 0f;
        }
    }

    /// <summary>1 while the body stands within distance="4" of its post (a resident's) or home spot (an enclave person's, E2).</summary>
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
            ScHome h;
            if (!ScHome.Of(_context.Self, out h)) return 0f;
            return Vector3.Distance(_context.Self.position, h.Post) <= distance ? 1f : 0f;
        }
    }

    /// <summary>
    /// 1 when the resident stands at its post within a tolerance: within xz="0.8" metres of the post's centre horizontally
    /// and dy="1.2" metres of its floor (the M11 fix 2, STUCK_DIAG fix A2). SCore's Guard counts a guard as arrived only when
    /// the floor of its height is exactly the post's cell, so on a surface below the cell top - terrain whose density puts
    /// it under the block top, a plate, a slab - it plans a path to where it already stands for ever, and the watchdog saw
    /// a body that wants to move and does not. With this, SCResident's ScAtPost (HoldPostSC, 2.2) takes over from Guard (2)
    /// once the resident is there.
    /// </summary>
    public class UAIConsiderationScAtPost : UAIConsiderationBase
    {
        private float xz = 0.8f;
        private float dy = 1.2f;

        public override void Init(Dictionary<string, string> parameters)
        {
            base.Init(parameters);
            if (parameters.ContainsKey("xz")) xz = StringParsers.ParseFloat(parameters["xz"]);
            if (parameters.ContainsKey("dy")) dy = StringParsers.ParseFloat(parameters["dy"]);
        }

        public override float GetScore(Context _context, object target)
        {
            ScHome h;
            return ScHome.Of(_context.Self, out h) && AtPost(_context.Self, h.Post, xz, dy) ? 1f : 0f;
        }

        public static bool AtPost(EntityAlive self, Member m, float xz, float dy)
        {
            return AtPost(self, ScCommunity.PostCenter(m), xz, dy);
        }

        /// <summary>Within xz metres of the post's (or home spot's, E2) centre horizontally and dy of its floor.</summary>
        public static bool AtPost(EntityAlive self, Vector3 post, float xz, float dy)
        {
            Vector3 d = self.position - post;
            return d.x * d.x + d.z * d.z <= xz * xz && Mathf.Abs(d.y) <= dy;
        }
    }

    /// <summary>
    /// SCResident's ScAtPost task (the M11 fix 2): what SCore's Guard does once it has arrived - face the post's look
    /// direction (GuardLookPosition, as Guard turns, V4 bodies without the pitch) - without Guard's arrival test, which a
    /// post over a surface below its cell top never passes. Start drops any path and pending path request (DropPath).
    /// Stops when the resident has left the tolerance (a shove, a fight); Guard walks it back. Shared singleton, no
    /// per-entity state.
    /// </summary>
    public class UAITaskHoldPostSC : UAITaskBase
    {
        public override void Start(Context _context)
        {
            base.Start(_context);
            ScCommunity.DropPath(_context.Self);
        }

        public override void Update(Context _context)
        {
            EntityAlive self = _context.Self;
            ScHome h;
            if (!ScHome.Of(self, out h) || !UAIConsiderationScAtPost.AtPost(self, h.Post, 1f, 1.5f)) { Stop(_context); return; }
            IEntityOrderReceiverSDX orders = ScNpc.Orders(self);
            if (orders == null) return;
            Vector3 look = orders.GuardLookPosition;
            self.SetLookPosition(look);
            self.RotateTo(look.x, look.y, look.z, 30f, self is EntityAliveSDXV4 ? 0f : 30f);
        }
    }

    /// <summary>
    /// Per-resident pause of the base defence (ScDefendBase / ScDefendHold), real seconds. Set when ScCommunity.Unstick
    /// moves a FIGHTING resident back to its post (60 s): an intruder it cannot reach (on a roof, behind bars, in a pit)
    /// would otherwise pull it straight back to the same wall after every teleport. And set by ScIntruder when the defence
    /// walk itself took the resident past StrayLimit (20 s, the review's M1). For the pause it only fights what comes into
    /// sight, as before the M11 fix. Keyed by entityId like ScRoamState; main thread.
    /// </summary>
    public static class ScDefendState
    {
        public static readonly Dictionary<int, float> RestUntil = new Dictionary<int, float>();

        public static void Rest(int id, float seconds) { RestUntil[id] = Time.time + seconds; }

        /// <summary>Seconds of pause left, 0 when none.</summary>
        public static float RestLeft(int id)
        {
            float t;
            return RestUntil.TryGetValue(id, out t) && Time.time < t ? t - Time.time : 0f;
        }
    }

    /// <summary>
    /// Defend the base (M11 fix, the user's decision): 1 when the target is an intruder this RESIDENT should go after - an
    /// enemy by SCore's rules (EntityTargetingUtilities.IsEnemy, what the action's IsEnemy filter asks too), alive, awake
    /// (a POI base's sleepers are left asleep), not one of SCore's inert props, inside the home zone and within
    /// ScConfig.DefendBaseRadius (22 m) of the resident's post. Seen or not: SCore's UAI (UAIBase_considerAction) scores
    /// every living entity in the NPC's bounds grown by its see distance - 60 m on XNPC's bodies, which Bounds.Expand
    /// splits between the two sides, so about 30 m each way - with no sight test, and asks sight only through
    /// CanSeeTarget, which SCResident's ScDefendBase leaves out: that is the whole fix for "a zombie in the base and he
    /// stands in his room". Near the post that box holds the whole radius; a resident roaming 12 m off can miss an
    /// intruder at the far edge until one of them moves.
    /// Without zone roaming (see the last paragraph) the radius is measured from the post, not from the resident, and stays
    /// inside the 25 m leash (ScReturn). The
    /// resident's own walk is bounded too (StrayLimit, the review's M1): the target is within 22 m, but the path to it is
    /// whatever A* finds, and a door round the back of a wall can swing it past the leash - ScReturn (5.5) then walks it
    /// home, ScDefendBase (4) walks it out again the moment it is back inside 25 m, and so on every 0.2 s while the target
    /// stays put. So when the resident itself is beyond StrayLimit while an intruder scores, the defence pauses for
    /// StrayRestSeconds (GetScore, not Why: the console's verdict stays free of side effects) and Guard walks it home; a
    /// 0.2 s flip-flop becomes a 20 s retry, and a straying resident is stopped before or as the leash fires.
    /// A Cowardly member is kept out in the XML (it flees from 15 m instead), and a member moved back to its post by the
    /// watchdog during a fight rests a while (ScDefendState). Cheap tests first: it runs for every enemy in the candidate
    /// list of every resident each 0.2 s.
    /// While a home attack is live (base defence D2, ScBaseAttack.WideDefence) the radius and the zone test give way to
    /// the whole zone + BaseAttackDefendMargin, the stray limit to that area + StrayExtra, and the leash (ScFarFromPost)
    /// and the watchdog's far rule to that area + LeashExtra: every resident defends the whole base.
    /// With zone roaming (ResidentRoamZone, round hz; the review's F2) a resident lives all over the zone, 20-60 m from its
    /// post, so the radius is measured from the resident itself (the intruder still inside the zone), and the stray limit
    /// is the zone + ResidentRoamZoneMargin - StrayMarginInside, just inside the leash that zone roaming draws there.
    /// </summary>
    public class UAIConsiderationScIntruder : UAIConsiderationBase
    {
        /// <summary>
        /// How far from its post the defence may take a resident: 23 m, fixed - the default DefendBaseRadius plus 1 m for
        /// the sidesteps a path makes round furniture near the edge. A straight approach from inside the limit never passes
        /// it (along a straight line the distance from the post is largest at an end, and the target is at most
        /// DefendBaseRadius, 23 m, out), so the limit only cuts detours. Not DefendBaseRadius + 1: with a small radius that
        /// would stop a resident roaming 12 m from its post from defending at all, and what must be prevented is reaching
        /// the leash, which does not move with the radius. 2 m inside the 25 m leash: more than a running resident covers
        /// between two AI decisions (about 1.1 m at 5.5 m/s in 0.2 s), so the stray is seen on the way out or, if one
        /// decision jumps the band and the leash fires, on the way back in.
        /// </summary>
        public const float StrayLimit = 23f;
        /// <summary>The pause after a stray: long enough for Guard to walk the resident well inside the limit.</summary>
        public const float StrayRestSeconds = 20f;
        /// <summary>Zone roaming: the stray limit sits this far inside the leash (zone + ResidentRoamZoneMargin), as 23 m sits inside 25 m.</summary>
        public const float StrayMarginInside = 2f;

        /// <summary>Zone roaming applies to this resident (a guard on watch never roams, and never walks to an intruder).</summary>
        public static bool Roams(Member m)
        {
            return ScConfig.ResidentRoamZone && m != null && !ScWatch.OnWatch(m);
        }

        public override float GetScore(Context _context, object target)
        {
            EntityAlive self = _context.Self;
            Member m;
            EncPerson ep;
            if (Why(self, target as EntityAlive, out m, out ep) != null) return 0f;
            // an enclave's people (E2) stray once past their POI + EnclaveLeashMargin - StrayMarginInside, just inside their
            // leash, as zone-roaming residents do. During a home attack (base defence D2) the defence covers the whole zone +
            // BaseAttackDefendMargin and the stray rule moves with it: StrayExtra past that area, LeashExtra - StrayExtra inside
            // the widened leash
            bool strayed = ep != null ? ScEnclavePeople.AreaOf(ep.EnclaveId).DistanceOutside(self.position) > Mathf.Max(0f, ScConfig.EnclaveLeashMargin - StrayMarginInside)
                         : ScBaseAttack.WideDefence ? !ScBaseAttack.InDefenceArea(self.position, ScBaseAttack.StrayExtra)
                         : Roams(m) ? ScZone.DistanceOutside(self.position) > Mathf.Max(0f, ScConfig.ResidentRoamZoneMargin - StrayMarginInside)
                                    : FromPost(m, self) > StrayLimit;     // m: the member Why already looked up (the re-check's R4)
            if (strayed)
            {
                ScDefendState.Rest(self.entityId, StrayRestSeconds);
                return 0f;
            }
            return 1f;
        }

        /// <summary>The resident's own distance from its post (3D, as the leash measures it), or -1 without one.</summary>
        public static float FromPost(EntityAlive self)
        {
            return FromPost(self != null ? ScRegistry.GetMember(self.entityId) : null, self);
        }

        private static float FromPost(Member m, EntityAlive self)
        {
            return m == null || m.Post == Vector3i.zero ? -1f : Vector3.Distance(self.position, ScCommunity.PostCenter(m));
        }

        /// <summary>Null for an intruder, else why not (constant strings: no allocation on the AI's path; sc xnpc shows it).</summary>
        public static string Why(EntityAlive self, EntityAlive t)
        {
            Member m;
            EncPerson ep;
            return Why(self, t, out m, out ep);
        }

        /// <summary>
        /// Why, handing back the resident's member record (null until it has been looked up) or the enclave person (E2: their
        /// own rule, WhyEnclave) for GetScore.
        /// </summary>
        private static string Why(EntityAlive self, EntityAlive t, out Member m, out EncPerson ep)
        {
            ep = ScEnclavePeople.PersonOf(self);
            if (ep != null) { m = null; return WhyEnclave(self, t, ep); }
            return Why(self, t, out m);
        }

        /// <summary>
        /// An enclave person's defence (E2, SCEnclave's ScEncDefend / ScEncDefendHold): an awake enemy (SCore's rules, as for
        /// residents) inside the POI's footprint and within EnclaveDefendRadius of the person - they live all over it, as
        /// zone-roaming residents do - unless the defence rests (ScDefendState: moved back in a fight, or strayed).
        /// </summary>
        private static string WhyEnclave(EntityAlive self, EntityAlive t, EncPerson ep)
        {
            int radius = ScConfig.EnclaveDefendRadius;
            if (radius <= 0) return "off (EnclaveDefendRadius 0)";
            if (t == null || t == self) return "no target";
            if (t.IsDead()) return "dead";
            if (t.IsSleeping) return "asleep";
            if (!ep.HasHome) return "no home spot";
            if (ScDefendState.RestLeft(self.entityId) > 0f) return "resting (moved back to the spot in a fight, or strayed)";
            if ((t.position - self.position).sqrMagnitude > (float)radius * radius) return "beyond EnclaveDefendRadius of the person";
            ScArea a = ScEnclavePeople.AreaOf(ep.EnclaveId);
            if (a == null || !a.Contains(t.GetBlockPosition())) return "outside the enclave's POI";
            if (!EntityTargetingUtilities.IsEnemy(self, t)) return "not an enemy";
            if (global::Harmony.ZombieFeatures.InertEntity.IsInert(t)) return "inert";
            return null;
        }

#if SC_V34
        /// <summary>
        /// H1 (round 24, UAIConsiderationScHoldEngagedHome): is t an intruder for this home body - the defence's own rule, read
        /// only (GetScore's stray rest is never written from here). An enclave person: WhyEnclave unchanged. A resident: the
        /// residents' rule with one difference, hold: a guard on watch is not excluded, since 0-XNPCCore's HoldEngaged only turns
        /// him to face the intruder and never walks him off the Watch post (the exclusion is about the defence's walk). Null
        /// for an intruder, else why not.
        /// </summary>
        public static string HoldWhy(EntityAlive self, EntityAlive t, EncPerson ep)
        {
            if (ep != null) return WhyEnclave(self, t, ep);
            Member m;
            return Why(self, t, out m, true);
        }

        /// <summary>The residents' rule as the defence asks it (a guard on watch never walks to an intruder).</summary>
        private static string Why(EntityAlive self, EntityAlive t, out Member m)
        {
            return Why(self, t, out m, false);
        }

        /// <summary>The residents' rule; hold (H1's HoldWhy only) skips the watch exclusion and nothing else.</summary>
        private static string Why(EntityAlive self, EntityAlive t, out Member m, bool hold)
#else
        /// <summary>The residents' rule.</summary>
        private static string Why(EntityAlive self, EntityAlive t, out Member m)
#endif
        {
            m = null;
            int radius = ScConfig.DefendBaseRadius;
            if (radius <= 0) return "off (DefendBaseRadius 0)";
            if (self == null || t == null || t == self) return "no target";
            if (t.IsDead()) return "dead";
            if (t.IsSleeping) return "asleep";
            m = ScRegistry.GetMember(self.entityId);
            if (m == null || m.Status != MemberStatus.Resident || m.Post == Vector3i.zero) return "not a resident at a post";
            // a guard on watch (base defence D3) shoots from the post and never walks to an intruder, a live attack or not
#if SC_V34
            if (!hold && ScWatch.OnWatch(m)) return "on watch (stays at the Watch post)";
#else
            if (ScWatch.OnWatch(m)) return "on watch (stays at the Watch post)";
#endif
            if (ScDefendState.RestLeft(self.entityId) > 0f) return "resting (moved back to the post in a fight, or strayed)";
            if (ScBaseAttack.WideDefence)
            {
                // a home attack is live: anything in the zone + BaseAttackDefendMargin, wherever the post is
                if (!ScBaseAttack.InDefenceArea(t.position)) return "outside the zone + BaseAttackDefendMargin (a home attack is live)";
            }
            else
            {
                // zone roaming: from where the resident is (it lives all over the zone); otherwise from its post
                if (Roams(m))
                {
                    if ((t.position - self.position).sqrMagnitude > (float)radius * radius) return "beyond DefendBaseRadius of the resident";
                }
                else if ((t.position - ScCommunity.PostCenter(m)).sqrMagnitude > (float)radius * radius) return "beyond DefendBaseRadius of the post";
                if (!ScZone.Contains(t.GetBlockPosition())) return "outside the home zone";
            }
            if (!EntityTargetingUtilities.IsEnemy(self, t)) return "not an enemy";
            if (global::Harmony.ZombieFeatures.InertEntity.IsInert(t)) return "inert";   // CanSeeTarget's own exclusion
            return null;
        }
    }

    /// <summary>
    /// What a defending resident does about an intruder from where it stands (the review's M2): walk on (Approach), leave
    /// it to the pack's attack (Engage), or stand and wait for the line to clear (Hold).
    /// Engage is exactly the pair 0-XNPCCore's attacks ask for, so the hand-over is immediate: with a gun SCore's
    /// InWeaponRange (in GetRange and SCoreUtils.CanShoot's ray clear of blocks) AND CanSeeTarget (SCoreUtils.CanSee: the
    /// first thing its ray meets is the target) - RangedMeleeAttack's own considerations; with a melee weapon, within reach,
    /// NotInWeaponRange's rule unchanged (MeleeAttack takes over once MoveToAttackTargetSDX.Stop has set the attack target).
    /// (Since the M11 fix 2 that reach is the weapon's own, ScMelee.Reach, as SCore's InWeaponRange now judges it.)
    /// The gap the review found: CanShoot passes on ANY entity in the line, CanSee only on the target, so with another
    /// resident (or the player) in front, in range, neither the attack nor NotInWeaponRange scored, Guard walked the
    /// resident home, and out of range the defence walked it back - pacing. Now, in range with no block in the way but not
    /// seen, the first entity on CanSee's own ray decides (Protected: who is never shot across, the re-check's R2):
    ///  - the player (any player): Hold - never close in on a fight across a player; it stands, facing the intruder;
    ///  - another protected one (a living non-enemy NPC: a member, a stranger survivor, a follower, a trader): Approach
    ///    while farther than CloseInFloor, then Hold. Closing in usually opens the line round the one in front;
    ///  - anything else - an enemy (the pack's attack takes the one it sees first anyway), a passive animal, a body that
    ///    is still in the way after death, a block on the sight line (CanSee's ray stops on blocks CanShoot's passes),
    ///    nothing, or the target itself: Approach.
    /// SCore's attack-target shortcut (the re-check's R1): CanSee answers true WITHOUT a ray for the attack or revenge
    /// target within sqrt(20) = 4.47 m. So inside that distance a "seen" target is checked with the ray here (Engage
    /// only when no protected one is first), DefendHoldSC clears the attack target that MoveToAttackTargetSDX.Stop sets
    /// on the switch, the floor keeps the approach well outside the shortcut, and - for the shot itself, whichever action
    /// got there - Patches_Npc.Patch_HoldFireAcross holds a member's fire at that range while a protected one is first in
    /// the line. Nothing here shoots: every shot is still the pack's RangedMeleeAttack. Main thread (the AI).
    /// </summary>
    public static class ScEngage
    {
        public enum Verdict { Approach, Engage, Hold }

        /// <summary>
        /// Closest the defence walks a resident to an intruder whose line a protected NPC blocks (the re-check's R1): SCore's
        /// attack-target shortcut (4.47 m) plus more than one AI decision's travel at a run (about 1.1 m, 1.65 m at 10 fps)
        /// plus a margin, so the switch to Hold always happens outside the shortcut.
        /// </summary>
        public const float CloseInFloor = 6.5f;

        /// <summary>SCoreUtils.CanSee's shortcut: distanceSq &lt; 20 to the attack or revenge target answers true with no ray.</summary>
        public const float ShortcutDistanceSq = 20f;

        /// <summary>
        /// Never shot across (the re-check's R2): any living player, and any living NPC (EntityNPC: SC's and 0-XNPCCore's
        /// survivor bodies - members, strangers, followers, stand-ins - SCore's other friendly NPCs and the traders) that is
        /// not this resident's enemy. Not a dead body still in the line (IsEnemy says no to the dead), not a passive animal,
        /// not an enemy, not a vehicle or a drone.
        /// </summary>
        public static bool Protected(EntityAlive self, EntityAlive e)
        {
            if (e == null || e == self || !e.IsAlive()) return false;
            if (e is EntityPlayer) return true;
            return e is EntityNPC && !EntityTargetingUtilities.IsEnemy(self, e);
        }

        private struct Memo
        {
            public Verdict Verdict;
            public string Why;
        }

        /// <summary>
        /// This frame's verdicts by (resident, intruder): ScDefendBase and ScDefendHold ask the same question for every
        /// intruder in one chooseAction pass, and an answer can cost three rays. Emptied when the frame changes.
        /// </summary>
        private static readonly Dictionary<long, Memo> memo = new Dictionary<long, Memo>();
        private static int memoFrame = -1;

        public static Verdict Classify(EntityAlive self, EntityAlive t, out string why)
        {
            int frame = Time.frameCount;
            if (frame != memoFrame) { memo.Clear(); memoFrame = frame; }
            long key = ((long)self.entityId << 32) | (uint)t.entityId;
            Memo mm;
            if (memo.TryGetValue(key, out mm)) { why = mm.Why; return mm.Verdict; }
            Verdict v = Decide(self, t, out why);
            memo[key] = new Memo { Verdict = v, Why = why };
            return v;
        }

        private static Verdict Decide(EntityAlive self, EntityAlive t, out string why)
        {
            Inventory inv = self.inventory;
            ItemClass held = inv != null ? inv.holdingItem : null;
            if (held == null || held.Actions == null || held.Actions.Length == 0 || held.Actions[0] == null)
            {
                why = "nothing in hand";   // SCore's InWeaponRange answers 0 then, NotInWeaponRange 1
                return Verdict.Approach;
            }
            ItemAction a0 = held.Actions[0];
            float dist = self.GetDistance(t);
            ItemActionRanged ranged = a0 as ItemActionRanged;
            ItemActionRanged.ItemActionDataRanged rd = ranged != null && inv.holdingItemData != null && inv.holdingItemData.actionData != null
                                                     && inv.holdingItemData.actionData.Count > 0
                ? inv.holdingItemData.actionData[0] as ItemActionRanged.ItemActionDataRanged : null;
            if (ranged == null || rd == null)
            {
                // the reach the pack's MeleeAttack now judges by (ScMelee, the M11 fix 2), else SCore's own reading
                float weapon;
                float reach = ScMelee.Reach(self, 0, out weapon);
                if (reach < 0f) reach = a0.Range != 0f ? a0.Range : ScMelee.SCoreFallbackReach;
                why = dist <= reach ? "in reach" : "beyond reach";
                return dist <= reach ? Verdict.Engage : Verdict.Approach;
            }
            float gunRange = ranged.GetRange(rd);
            float range = ScShooting.CapRange(self, gunRange, t);   // no farther than its Shooting stars engage, unless t shoots at it (round sh)
            if (dist > range) { why = range < gunRange ? "beyond its shooting range (" + range.ToString("0") + " m)" : "out of range"; return Verdict.Approach; }
            if (!SCoreUtils.CanShoot(self, t, range)) { why = "no clear shot (a block in the line)"; return Verdict.Approach; }
            bool block;
            EntityAlive first;
            if (SCoreUtils.CanSee(self, t))
            {
                // inside the shortcut CanSee cast no ray for its attack or revenge target: cast it (the re-check's R1, route A)
                if (self.GetDistanceSq(t) < ShortcutDistanceSq)
                {
                    first = FirstOnSeeRay(self, t, out block);
                    if (Protected(self, first))
                    {
                        why = first is EntityPlayer ? "the player is in the line (close)" : "an ally is in the line (close)";
                        return Verdict.Hold;
                    }
                }
                why = "in range and seen";
                return Verdict.Engage;
            }
            first = FirstOnSeeRay(self, t, out block);
            if (first == null || first == t) { why = block ? "not seen (a block on the sight line)" : "not seen"; return Verdict.Approach; }
            if (!Protected(self, first)) { why = "an enemy, an animal or a body is in the line"; return Verdict.Approach; }
            if (first is EntityPlayer) { why = "the player is in the line"; return Verdict.Hold; }
            if (dist > CloseInFloor) { why = "an ally is in the line - closing in"; return Verdict.Approach; }
            why = "an ally is in the line, within 6.5 m";
            return Verdict.Hold;
        }

        /// <summary>
        /// The first entity on SCoreUtils.CanSee's own ray (head to head, 0.2 m out, the see distance, its weapon-buff hit
        /// mask), or null with block = true when a block or terrain is first.
        /// </summary>
        public static EntityAlive FirstOnSeeRay(EntityAlive self, EntityAlive t, out bool block)
        {
            block = false;
            Vector3 from = self.getHeadPosition();
            Vector3 dir = t.getHeadPosition() - from;
            Ray ray = new Ray(from, dir);
            ray.origin += dir.normalized * 0.2f;
            if (!Voxel.Raycast(self.world, ray, self.GetSeeDistance(), SCoreUtils.GetHitMaskByWeaponBuff(self), 0f)) return null;
            WorldRayHitInfo info = Voxel.voxelRayHitInfo;
            if (info == null) return null;
            if (GameUtils.IsBlockOrTerrain(info.tag)) { block = true; return null; }
            Transform root = info.transform != null ? GameUtils.GetHitRootTransform(info.tag, info.transform) : null;
            return root != null ? root.GetComponent<EntityAlive>() : null;
        }
    }

    /// <summary>
    /// ScDefendBase's last gate (the review's M2, in place of SCore's NotInWeaponRange): 1 while the resident should walk on
    /// towards the intruder (ScEngage.Verdict.Approach) - out of range or reach, no clear shot, a protected NPC blocking the
    /// line from farther than 6.5 m, or an unprotected one in the way. 0 the moment the pack's attack can act (Engage) or
    /// the resident should stand (Hold).
    /// </summary>
    public class UAIConsiderationScCannotEngage : UAIConsiderationBase
    {
        public override float GetScore(Context _context, object target)
        {
            EntityAlive t = target as EntityAlive;
            if (t == null) return 0f;
            string why;
            return ScEngage.Classify(_context.Self, t, out why) == ScEngage.Verdict.Approach ? 1f : 0f;
        }
    }

    /// <summary>
    /// ScDefendHold's gate (the review's M2): 1 while the resident's gun is in range of the intruder with no block in the
    /// way but a player, or an ally within 6.5 m, stands in the line - also when SCore's shortcut calls the intruder seen
    /// within 4.47 m but the ray meets one of them first (ScEngage.Verdict.Hold). It stands and waits instead of Guard
    /// walking it home and the defence walking it back.
    /// </summary>
    public class UAIConsiderationScHoldFire : UAIConsiderationBase
    {
        public override float GetScore(Context _context, object target)
        {
            EntityAlive t = target as EntityAlive;
            if (t == null) return 0f;
            string why;
            return ScEngage.Classify(_context.Self, t, out why) == ScEngage.Verdict.Hold ? 1f : 0f;
        }
    }

    /// <summary>
    /// ScDefendHold's task (the review's M2): stand still, facing the intruder, while the line of fire is blocked by a
    /// player or an ally. No shot of its own: the moment the line clears, ScHoldFire drops to 0 and RangedMeleeAttack (6)
    /// fires; if the intruder moves out of range, ScDefendBase walks on. It also lets go of the intruder as ATTACK target
    /// (the re-check's R1): MoveToAttackTargetSDX.Stop sets it on the very switch to this action, and within 4.47 m SCore's
    /// CanSee would then call the intruder seen with no ray, so RangedMeleeAttack would win the next decision and fire
    /// across the blocker. UAIBase.Update runs updateAction right after chooseAction, so the clear in Start lands before
    /// the next decision. Only the attack target: a revenge target (the intruder hit this resident) stays, and with it
    /// SCore's self-defence - Patches_Npc.Patch_HoldFireAcross still holds that shot while a protected one is in the line.
    /// Every update stamps the aim pose (ScAim.StampHeld, the M11 fix 2), so the gun is up while it waits rather than
    /// down 2 s in. Shared singleton, no per-entity state.
    /// </summary>
    public class UAITaskDefendHoldSC : UAITaskBase
    {
        public override void Start(Context _context)
        {
            base.Start(_context);
            EntityAlive self = _context.Self;
            if (self.getNavigator() != null) self.getNavigator().clearPath();
            if (self.moveHelper != null) self.moveHelper.Stop();
            ReleaseAttackTarget(self, _context.ActionData.Target as EntityAlive);
        }

        public override void Update(Context _context)
        {
            EntityAlive self = _context.Self;
            EntityAlive t = _context.ActionData.Target as EntityAlive;
            if (t == null || t.IsDead()) { Stop(_context); return; }
            ReleaseAttackTarget(self, t);
            if (self.moveHelper != null && self.moveHelper.IsActive) self.moveHelper.Stop();
            self.SetLookPosition(t.getHeadPosition());
            self.RotateTo(t, 30f, 30f);
            // "aims and waits" (the M11 fix 2, AIM_DIAG fix 2): no attack task stamps the aim pose while this holds
            ScAim.StampHeld(self);
        }

        /// <summary>
        /// Clears the attack target when it is the held intruder. SetAttackTarget(null, 0) is vanilla's normal clear, and
        /// both SCore postfixes on it take null (one only restores an SDX NPC's speed, the other writes 0 to _attackTargetId).
        /// It sends a packet, so only when it is set.
        /// </summary>
        private static void ReleaseAttackTarget(EntityAlive self, EntityAlive t)
        {
            if (t != null && self.GetAttackTarget() == t) self.SetAttackTarget(null, 0);
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
#if SC_V34

    /// <summary>
    /// 3.4 (Stage 2, D1): 0 exactly when ScTravel would score - a member holding the Task order who is a physical runner in
    /// the world (ScIsMember, HasOrder Task, ScTravelling) - else 1, for everyone else, raiders and strangers included.
    /// 0-XNPCCore 3.4 raised SdcsNpcRangedAdvanced's MeleeApproach and BackupRanged from 4 and 3 to 5, past ScTravel (4.5),
    /// while its MeleeAttack stays at 5: no weight puts the walk above the chase and the back-off but under the swing, so
    /// this gate (Config/utilityai.xml, 3.4 only) takes those two out for a runner on the move, as 3.2's weights did. The
    /// swing in reach, the shot in range and the 4 m back-off (BackupKeepDistance) still win. SCore's GetScore drops an
    /// action on a raw 0 before the response curve, so flip_x on ScTravelling could not do this.
    /// </summary>
    public class UAIConsiderationScNotTravelling : UAIConsiderationBase
    {
        public override float GetScore(Context _context, object target)
        {
            Member m = ScRegistry.GetMember(_context.Self.entityId);
            if (m == null || !ScRunners.IsPhysical(m) || m.Offline) return 1f;
            return EntityUtilities.GetCurrentOrder(_context.Self.entityId) == EntityUtilities.Orders.Task ? 0f : 1f;
        }
    }

    /// <summary>
    /// 3.4 (the user, 2026-10-29: "they should hold their ground inside their home if they get attacked"): 0 for one of an
    /// enclave's people who is at home (ScEnclavePeople.PersonOf, StateHome), else 1. 0-XNPCCore 3.4's sleeper detection
    /// sends any unhired body an enemy targets inside a sleeper volume out of it (LeaveAreaSdcs, weight 9, over every
    /// fight); an enclave's building is one, so its people left home in every zombie fight. This gate on LeaveAreaSdcs
    /// (Config/utilityai.xml, 3.4 only) keeps them in and fighting; the flags XNPC sets meanwhile (SdcsLeavingArea,
    /// SdcsSleeperDetected) have no effects and run out, and its building-avoid list only steers looting.
    /// </summary>
    public class UAIConsiderationScNotEncHome : UAIConsiderationBase
    {
        public override float GetScore(Context _context, object target)
        {
            EncPerson p = ScEnclavePeople.PersonOf(_context.Self);
            return p != null && p.State == EncPerson.StateHome ? 0f : 1f;
        }
    }

    /// <summary>
    /// 3.4, round R1 (Q10): 0 for one of a band's bodies (ScBands.PersonOf) while its band has not engaged the player
    /// (Band.Engaged), else 1 - everyone else, a band that is attacking, raiders in a den or the wild, 0-XNPCCore's own
    /// bandits. On 0-XNPCCore 3.4.02's SdcsBanditCombat HoldEngaged (Config/utilityai.xml, only where that action exists):
    /// it stands, faces and targets any enemy within 40 m, seen or not, at 3.5, over the Guard order (3) a band walks by
    /// (ScBands), so a band of raiders, SC's raiders being bandits' children there, stopped on the road for a zombie or an
    /// unseen player. Their shot, swing and approach of what they see are not gated.
    /// </summary>
    public class UAIConsiderationScNotBandWalking : UAIConsiderationBase
    {
        public override float GetScore(Context _context, object target)
        {
            BandPerson p = ScBands.PersonOf(_context.Self);
            if (p == null) return 1f;
            Band b = ScBands.ById(p.BandId);
            return b != null && !b.Engaged ? 0f : 1f;
        }
    }

    /// <summary>
    /// 3.4, round 24 (H1): 0-XNPCCore 3.4.02's HoldEngaged (SdcsNpcRangedAdvanced at 4, SdcsBanditCombat at 3.5; its task
    /// HoldEngagedSdcs) stands the NPC facing any enemy within 40 m - seen or not: no CanSeeTarget, and SCore's UAI scores
    /// every entity in the see box - makes it the attack target for 1200 ticks, and never ends by itself (only when the
    /// target dies or another action wins). Every home action of SC's people scores less (ScAtPost / ScEncAtHome 3.2, ScRoam /
    /// ScEncRoam 3.4, ScNightIdle / ScEncNight 3.6, 0-XNPCCore's Guard 3), and nothing that would replace it scores for an
    /// enemy outside the base (the shot wants CanSeeTarget, the defence ScIntruder), so a resident froze facing zombies
    /// behind the walls 20-40 m out. On SC's home bodies - a resident (Status Resident: at the post, on watch or on a work
    /// shift) and an enclave's person at home (ScEncHome's test) - this is 1 only for an INTRUDER by the defence's own rule
    /// (UAIConsiderationScIntruder.HoldWhy, read only: the rule ScDefendBase / ScEncDefend ask, with the watch exclusion
    /// skipped for a guard on watch, who is only turned, never walked), else 0. Everyone else - followers, runners, outpost
    /// stand-ins, prospects, strangers, plain 0-XNPCCore hires and bandits, SC's bands (R1's ScNotBandWalking stays) and
    /// den raiders - 1: 0-XNPCCore's rule unchanged. Appended LAST to both actions (Config/utilityai.xml, only where they
    /// exist), so MayEngage (it stamps 0-XNPCCore's combat linger) and TargetDistance run first, as before, and this only
    /// runs for an enemy within 40 m. For any other NPC it costs two dictionary reads (the enclave body lookup, then the
    /// registry: the order ScIntruder resolves a body in). Counters and the last refusal: sc xnpc (Report); per NPC and
    /// foe: sc xnpc &lt;id&gt; (Describe); a verbose line the first time a body is held off.
    /// </summary>
    public class UAIConsiderationScHoldEngagedHome : UAIConsiderationBase
    {
        private static long heldOff, letThrough;
        private static int lastSelf = -1, lastTarget = -1;
        private static string lastWhy;
        private static float lastDist, lastTime;
        /// <summary>Bodies the verbose line was written for (once per body per world).</summary>
        private static readonly HashSet<int> logged = new HashSet<int>();

        public static void Reset()
        {
            heldOff = letThrough = 0;
            lastSelf = lastTarget = -1;
            lastWhy = null;
            lastDist = lastTime = 0f;
            logged.Clear();
        }

        public override float GetScore(Context _context, object target)
        {
            EntityAlive self = _context.Self;
            EntityAlive t = target as EntityAlive;
            string why;
            if (!HomeBody(self, t, out why)) return 1f;
            if (why == null) { letThrough++; return 1f; }
            // Fable review H1 LOW 2: a sleeper's or a corpse's refusal is not spent on the diagnostics (the once-per-body line)
            if (t == null || t.IsDead() || t.IsSleeping) return 0f;
            heldOff++;
            lastSelf = self.entityId;
            lastTarget = t != null ? t.entityId : -1;
            lastWhy = why;
            lastDist = t != null ? self.GetDistance(t) : -1f;
            lastTime = Time.time;
            if (ScConfig.Verbose && logged.Add(self.entityId))
                ScLog.Verbose("H1: " + ScCommunity.DisplayName(self) + " (" + self.entityId + ") does not stand and face "
                              + (t != null ? t.EntityName + " (" + t.entityId + ") at " + lastDist.ToString("0.#") + " m" : "its target")
                              + " - 0-XNPCCore's HoldEngaged held off on SC's home bodies for all but intruders: " + why
                              + " (once per body; sc xnpc counts them)");
            return 0f;
        }

        /// <summary>
        /// False when self is not one of SC's home bodies (0-XNPCCore's HoldEngaged unchanged); else true, with why null for
        /// an intruder (the hold may score) or why it is not one. Read only.
        /// </summary>
        public static bool HomeBody(EntityAlive self, EntityAlive t, out string why)
        {
            why = null;
            if (self == null) return false;
            EncPerson ep = ScEnclavePeople.PersonOf(self);
            if (ep != null)
            {
                // ScEncHome's own test (no role): the current body of an enclave's person, at home, with a spot, not walking home
                if (!ep.HasHome || ep.State != EncPerson.StateHome || ep.Leaving) return false;
                why = UAIConsiderationScIntruder.HoldWhy(self, t, ep);
                return true;
            }
            Member m = ScRegistry.GetMember(self.entityId);
            // Fable review H1 LOW 1: a Resident with no post yet (a runner flipped to Resident before the relink's Station)
            // keeps 0-XNPCCore's rule, as SC's own home actions (ScHome.Of) ignore such a member too
            if (m == null || m.Status != MemberStatus.Resident || m.Post == Vector3i.zero) return false;
            why = UAIConsiderationScIntruder.HoldWhy(self, t, null);
            return true;
        }

        /// <summary>sc xnpc's line: the counters since the world loaded and the last refusal.</summary>
        public static string Report()
        {
            string s = "home bodies held off " + heldOff + " time(s), let through for an intruder " + letThrough + " time(s)";
            if (lastSelf < 0) return s + "; none held off yet";
            World w = GameManager.Instance != null ? GameManager.Instance.World : null;
            EntityAlive a = w != null ? w.GetEntity(lastSelf) as EntityAlive : null;
            EntityAlive b = w != null && lastTarget >= 0 ? w.GetEntity(lastTarget) as EntityAlive : null;
            return s + "; last: " + (a != null ? ScCommunity.DisplayName(a) : "?") + " (" + lastSelf + ") on "
                   + (b != null ? b.EntityName : "?") + " (" + lastTarget + ") at " + lastDist.ToString("0.#") + " m - " + lastWhy
                   + ", " + (Time.time - lastTime).ToString("0") + " s ago";
        }

        /// <summary>sc xnpc &lt;id&gt;'s verdict for one NPC and foe (read only).</summary>
        public static string Describe(EntityAlive self, EntityAlive foe)
        {
            string why;
            if (!HomeBody(self, foe, out why)) return "not an SC home body - 0-XNPCCore's rule (any enemy within 40 m)";
            return why == null ? "an intruder - may hold on it" : "held off - " + why;
        }
    }
#endif

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
            // Run the travel legs; walk while searching the site, holding a pinned spot and on the last stroll to the post.
            self.FindPath(s.Waypoint, PathSpeed(self, m), false, null);
        }

        /// <summary>
        /// The speed handed to FindPath. SCore's legacy class: GetMoveSpeedPanic MUST bind vanilla's non-virtual
        /// EntityAlive.GetMoveSpeedPanic (self is typed EntityAlive): it applies the NPC's own RunSpeed passives
        /// (XNPCCore's base_set 1.0, Cardio, hungry, injured, boots) - the speed SCore's FollowSDX runs at. Through an
        /// EntityAliveSDX reference it would bind SCore's hiding method, which ignores passives and returns the raw
        /// MoveSpeedPanic (0.45: still a walk on the player body). 0-XNPCCore's V4 survivors move in literal metres per
        /// second, scaled by their own passives inside XNPC's controller: RunnerRunMps / RunnerWalkMps are the target
        /// paces, converted by ScXnpc.PathSpeed (migration M4.1).
        /// </summary>
        private static float PathSpeed(EntityAlive self, Member m)
        {
            bool run = Running(m);
            if (ScNpc.IsV4(self)) return ScXnpc.PathSpeed(run ? ScConfig.RunnerRunMps : ScConfig.RunnerWalkMps, run);
            return run ? self.GetMoveSpeedPanic() : self.GetMoveSpeed();
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
            // the lead never reaches half the waypoint spacing, so it cannot fire on a waypoint that was just issued
            float lead = Mathf.Min(3f, Mathf.Max(0f, Mathf.Max(8f, ScConfig.RunWaypointSpacing) * 0.5f - ScConfig.RunArriveRadius));
            if (Running(m) && lead > 0f && d.magnitude <= ScConfig.RunArriveRadius + lead && ScRunners.XZ(s.Waypoint, ScRunners.LegGoal(m)) > 1f)
            {
                // at running pace the next path is asked for a few metres early, so they do not stop at every waypoint
                repaths[self.entityId] = 0;
                PathTo(self, m, true);
                return;
            }
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

        private static bool Running(Member m) { return m.RunLeg == RunLeg.Outbound || m.RunLeg == RunLeg.Inbound; }

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
