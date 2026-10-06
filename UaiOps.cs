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
        private readonly HashSet<int> retried = new HashSet<int>();
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
            for (int attempt = 0; attempt < 6 && target == Vector3.zero; attempt++)
            {
                // CalcNear validates nothing - check the cell ourselves. A real floor (the M11 fix 2, STUCK_DIAG: the old
                // "two air cells over anything" aimed roams at furniture tops), at the post's level or up to 2 off it
                Vector3 p = RandomPositionGenerator.CalcNear(self, post, radius, 0);
                for (int step = 0; step <= 4; step++)
                {
                    Vector3i bp = World.worldToBlockPos(p) + new Vector3i(0, (step + 1) / 2 * (step % 2 == 0 ? 1 : -1), 0);
                    if (!ScZone.Contains(bp)) break;
                    string why;
                    if (w != null && !ScCommunity.GoodPost(w, bp, false, out why)) continue;
                    target = new Vector3(bp.x + 0.5f, bp.y, bp.z + 0.5f);
                    break;
                }
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
            retried.Remove(id);
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
            // a live home attack (base defence D2): the leash is the defended area + LeashExtra, not the distance from the post
            if (ScBaseAttack.WideDefence) return ScBaseAttack.InDefenceArea(_context.Self.position, ScBaseAttack.LeashExtra) ? 0f : 1f;
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
            Member m = ScRegistry.GetMember(_context.Self.entityId);
            return m != null && m.Post != Vector3i.zero && AtPost(_context.Self, m, xz, dy) ? 1f : 0f;
        }

        public static bool AtPost(EntityAlive self, Member m, float xz, float dy)
        {
            Vector3 d = self.position - ScCommunity.PostCenter(m);
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
            Member m = ScRegistry.GetMember(self.entityId);
            if (m == null || m.Post == Vector3i.zero || !UAIConsiderationScAtPost.AtPost(self, m, 1f, 1.5f)) { Stop(_context); return; }
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
    /// The radius is measured from the post, not from the resident, and stays inside the 25 m leash (ScReturn). The
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

        public override float GetScore(Context _context, object target)
        {
            EntityAlive self = _context.Self;
            Member m;
            if (Why(self, target as EntityAlive, out m) != null) return 0f;
            // during a home attack (base defence D2) the defence covers the whole zone + BaseAttackDefendMargin and the
            // stray rule moves with it: StrayExtra past that area, LeashExtra - StrayExtra inside the widened leash
            bool strayed = ScBaseAttack.WideDefence ? !ScBaseAttack.InDefenceArea(self.position, ScBaseAttack.StrayExtra)
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
            return Why(self, t, out m);
        }

        /// <summary>Why, handing back the resident's member record (null until it has been looked up) for GetScore.</summary>
        private static string Why(EntityAlive self, EntityAlive t, out Member m)
        {
            m = null;
            int radius = ScConfig.DefendBaseRadius;
            if (radius <= 0) return "off (DefendBaseRadius 0)";
            if (self == null || t == null || t == self) return "no target";
            if (t.IsDead()) return "dead";
            if (t.IsSleeping) return "asleep";
            m = ScRegistry.GetMember(self.entityId);
            if (m == null || m.Status != MemberStatus.Resident || m.Post == Vector3i.zero) return "not a resident at a post";
            if (ScDefendState.RestLeft(self.entityId) > 0f) return "resting (moved back to the post in a fight, or strayed)";
            if (ScBaseAttack.WideDefence)
            {
                // a home attack is live: anything in the zone + BaseAttackDefendMargin, wherever the post is
                if (!ScBaseAttack.InDefenceArea(t.position)) return "outside the zone + BaseAttackDefendMargin (a home attack is live)";
            }
            else
            {
                if ((t.position - ScCommunity.PostCenter(m)).sqrMagnitude > (float)radius * radius) return "beyond DefendBaseRadius of the post";
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
            float range = ranged.GetRange(rd);
            if (dist > range) { why = "out of range"; return Verdict.Approach; }
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
