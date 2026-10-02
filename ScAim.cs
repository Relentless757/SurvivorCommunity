using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UAI;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Visible aiming for the NPCs that wear the player's body (survivors and raiders). The player's third-person
    /// controller already has the whole pose: layer 16 "AdditiveAimPoses" shoulders every gun whose hold type it lists
    /// (a standing and a crouched clip each) while the Bool IsAiming is set, and layer 18 "BowDrawAndFire" draws a bow
    /// on WeaponPreFire and looses it on WeaponFire. Vanilla only sets those through the local player's zoom action and
    /// bow action, so an NPC fired every shot from the low carry pose and a bow never drew (WeaponFire alone does
    /// nothing on layer 18). Verified live on 2026-10-01 on a rifle survivor: IsAiming put 3P_2HRifle_Additive_Aim on
    /// layer 16 at full weight (crouched: 3P_2HRifle_Additive_crouchAim), the rifle went to the shoulder and nothing
    /// cleared the flag again. On a primitive bow, WeaponPreFire held 3P_2HBow_Draw until WeaponFire played
    /// 3P_2HBow_Fire, which then settled in 2HBowHold.
    ///
    /// These are our XNPC bodies (ScXnpc.IsBody: the survivor classes the community owns and SC's raiders; decision D12 -
    /// XNPC itself never sets IsAiming).
    ///
    /// When: SCore's attack task (UAITaskAttackTargetEntitySDX, which 0-XNPCCore's packages use, and its V4 sibling
    /// UAITaskAttackTargetEntitySDXV4 should a package use it) is stamped on every Start and Update with its
    /// action_index. Our bodies carry vanilla player weapons, which shoot on Action0 - XNPC's attack actions all use
    /// action_index 0 - so a stamp is ranged when the held item's action at that index is an ItemActionRanged, and melee
    /// otherwise. The gun stays up for SurvivorAimHoldSeconds after the last ranged tick, so the AI re-scoring every
    /// 0.2 s does not make it bob. It comes down at once for a melee tick, a reload, a use animation, a stun or a
    /// ragdoll. The driver runs from Patches_Xnpc (X7) after XNPC's own Update, on the machine that owns the NPC, and
    /// writes through the controller's own setters, so the parameters reach other players the way a remote player's aim
    /// does. Their reload is the vanilla one, which XNPC publishes as the SdcsReloading cvar.
    ///
    /// Visual only (the user's decision): EntityAlive.AimingGun reads the animator's IsAiming, and every gameplay
    /// reader goes through it: the spread (ItemActionRanged.updateAccuracy via OnHoldingUpdate), the forced critical
    /// in fireShot and the kickback. So its getter answers false for these bodies while SurvivorAimVisualOnly is on.
    ///
    /// The M11 fix 2 (AIM_DIAG): the arms under the aim. (1) KeepCarry re-asserts the base carry pose (WeaponCarry) every
    /// frame: 0-XNPCCore writes it only when its own cache changes, so a parameter reset or a write dropped while the
    /// animator was off left it at 0 - unarmed, arms hanging - under a correct IsAiming until the next weapon swap.
    /// (2) A resident that holds fire (SCResident's ScDefendHold, "aims and waits") stamps as an attack does
    /// (StampHeld), so its gun is up while it waits. (3) Whether it faces its foe is judged by the target the task works
    /// on first, then the attack and revenge targets (FacingTarget). (4) The user's decision: the gun stays up while
    /// SCore's back-off task (UAITaskBackupFromTargetSDX: 0-XNPCCore's BackupKeepDistance inside 4 m and BackupRanged)
    /// walks it away from a target its gun reaches - one new target, Patch_BackoffAim.
    /// </summary>
    public static class ScAim
    {
        /// <summary>Hold types with a clip on layer 16 (each has a standing and a crouched state), from the controller dump.</summary>
        private static readonly HashSet<int> AimHoldTypes = new HashSet<int> { 1, 5, 6, 11, 26, 30, 37, 39, 44, 48, 56, 60, 69, 71, 75, 76, 77, 81, 82, 83, 84, 85 };

        /// <summary>Hold types layer 18 draws: the primitive, compound and wooden bows. The crossbows (26, 69) aim like guns.</summary>
        private static bool IsBow(int hold) { return hold == 27 || hold == 53 || hold == 68; }

        // degrees off the target: a raised weapon comes down past FacingDrop, a lowered one only rises inside FacingRaise,
        // so a target circling at the edge does not flick it up and down (SCore itself only shoots within 35)
        private const float FacingDrop = 70f;
        private const float FacingRaise = 50f;
        private const float RedrawGap = 0.3f;       // seconds between two draw triggers
        private const float ForgetAfter = 10f;      // an entry is dropped this long after its hold ran out
        private const float SweepEvery = 30f;       // seconds between sweeps for NPCs that left the world mid-fight

        // Animator.StringToHash of the state names in the player controller (checked against the live shortNameHash values)
        private static readonly int EmptyState = Animator.StringToHash("Empty");
        private static readonly int BowHold = Animator.StringToHash("2HBowHold (27) (53) (68)");
        private static readonly int BowHold53 = Animator.StringToHash("2HBowHold (53)");
        private static readonly int BowDraw = Animator.StringToHash("2HBowDraw (27) (53) (68)");
        private static readonly int BowDraw53 = Animator.StringToHash("2HBowDraw (53)");
        private static readonly int BowRelease = Animator.StringToHash("2HBow_Fire (27) (53) (68)");
        private static readonly int BowRelease53 = Animator.StringToHash("2HBow_Fire (53)");
        private static readonly int BowReload = Animator.StringToHash("3P_2HBow_Reload (27) (53) (68)");
        private static readonly int BowReload53 = Animator.StringToHash("3P_2HBow_Reload (53)");

        private sealed class State
        {
            public float LastRanged = -1000f;    // Time.time of the last attack-task tick whose action shoots
            public float LastMelee = -1000f;     // ... whose action does not (a melee weapon)
            public float DrawAt = -1000f;        // bow: when the last draw was triggered
            public bool Aiming;                  // what the driver last wrote to IsAiming
            public bool Up;                      // the last decision was weapon up (aim or bow), for the facing hysteresis
            public bool Drawn;                   // bow: layer 18 is drawing or holding the draw
            public string Why = "";              // the reason for the last decision, for `sc aim`
            public int AnimId;                   // the animator the layer indices below belong to
            public int AimLayer = -1, BowLayer = -1, ReloadLayer = -1;
        }

        /// <summary>Only NPCs that have been in a ranged fight in the last ForgetAfter seconds.</summary>
        private static readonly Dictionary<int, State> states = new Dictionary<int, State>();

        /// <summary>Hook counters: a patch is proven by a count, not assumed from Harmony's report.</summary>
        private static int stampCalls, driveCalls, getterCalls;
        /// <summary>The M11 fix 2's counters: hold stamps (ScDefendHold), back-off stamps, and WeaponCarry repairs (KeepCarry).</summary>
        private static int holdStamps, backoffStamps, carryFixes;

        private static float lastSweep;
        private static readonly List<int> gone = new List<int>();

        public static void Reset()
        {
            states.Clear();
            stampCalls = driveCalls = getterCalls = 0;
            holdStamps = backoffStamps = carryFixes = 0;
            carryAnimators.Clear();
            lastSweep = 0f;
        }

        /// <summary>
        /// Drops the entries of NPCs that left the world mid-fight (killed and despawned, or unloaded with their
        /// chunk): Drive's own clean-up never runs for them. Only entities that are gone are dropped; a loaded one
        /// always reaches Drive, which lowers the weapon before it forgets the entry.
        /// </summary>
        private static void Sweep(float now)
        {
            if (now - lastSweep < SweepEvery) return;
            lastSweep = now;
            World w = GameManager.Instance != null ? GameManager.Instance.World : null;
            if (w == null) return;
            gone.Clear();
            foreach (KeyValuePair<int, State> kv in states)
                if (w.GetEntity(kv.Key) == null) gone.Add(kv.Key);
            for (int i = 0; i < gone.Count; i++) states.Remove(gone[i]);
        }

        // ---- when: SCore's attack tasks ----

        /// <summary>
        /// SCore's V4 attack task (UAI.UAITaskAttackTargetEntitySDXV4, SCore 3.2.36): a SIBLING of the SDX one - both
        /// derive from vanilla's UAITaskAttackTargetEntity, each declaring its own Start, Update and private _actionIndex -
        /// that 0-XNPCCore's packages do not use today (they name the SDX task). Bound by name too, so the aim pose stays
        /// live if a package switches over, and an SCore without it only loses this half.
        /// </summary>
        private const string V4AttackTask = "UAI.UAITaskAttackTargetEntitySDXV4";

        /// <summary>The attack tasks' own declared method of this name (Start / Update with a Context), each class that has one.</summary>
        public static List<MethodBase> AttackTargets(string name)
        {
            var list = new List<MethodBase>();
            try
            {
                foreach (Type t in new[] { typeof(UAITaskAttackTargetEntitySDX), AccessTools.TypeByName(V4AttackTask) })
                {
                    MethodInfo m = t != null ? AccessTools.DeclaredMethod(t, name, new[] { typeof(Context) }) : null;
                    if (m != null) list.Add(m);
                }
            }
            catch (Exception e)
            {
                ScLog.Once("aim-targets-" + name, e);
            }
            return list;
        }

        /// <summary>The V4 task's method for ModApi's ExpectedTargets (null when this SCore has no V4 task).</summary>
        public static MethodBase V4AttackTarget(string name)
        {
            Type t = AccessTools.TypeByName(V4AttackTask);
            return t != null ? AccessTools.DeclaredMethod(t, name, new[] { typeof(Context) }) : null;
        }

        /// <summary>Per task class: its private _actionIndex (null when the class has none - logged once, then -1).</summary>
        private static readonly Dictionary<Type, AccessTools.FieldRef<object, int>> actionIndex = new Dictionary<Type, AccessTools.FieldRef<object, int>>();

        /// <summary>The task's private action_index, or -1. Resolved per class on first use, so a renamed field only logs.</summary>
        internal static int ActionIndex(UAITaskBase task)
        {
            if (task == null) return -1;
            Type t = task.GetType();
            AccessTools.FieldRef<object, int> get;
            if (!actionIndex.TryGetValue(t, out get))
            {
                string why = "no such field";
                try { get = AccessTools.FieldRefAccess<int>(t, "_actionIndex"); }
                catch (Exception e)
                {
                    get = null;
                    why = e.Message;
                }
                if (get == null) ScLog.Err(t.Name + "._actionIndex not found (SCore update?) - NPCs will not raise their guns on that task: " + why);
                actionIndex[t] = get;
            }
            return get != null ? get(task) : -1;
        }

        private static void Stamp(UAITaskBase task, Context ctx)
        {
            try
            {
                stampCalls++;
                Sweep(Time.time);
                EntityAlive e = ctx != null ? ctx.Self : null;
                if (e == null || e.isEntityRemote || !ScXnpc.IsBody(e)) return;
                StampIndex(e, ActionIndex(task));
            }
            catch (Exception ex)
            {
                ScLog.Once("aim-stamp", ex);
            }
        }

        /// <summary>The stamp itself, for one of our bodies on the machine that owns it: the action at this index says ranged or melee.</summary>
        private static void StampIndex(EntityAlive e, int index)
        {
            if (index != 0 && index != 1) return;
            // player items shoot on whichever action the attack names, so the item itself says what this attack is
            bool ranged = IsRangedAction(e, index);
            State s;
            if (!states.TryGetValue(e.entityId, out s))
            {
                if (!ranged) return;            // a melee fight alone never raises anything
                s = new State();
                states[e.entityId] = s;
            }
            if (ranged) s.LastRanged = Time.time;
            else s.LastMelee = Time.time;
        }

        /// <summary>
        /// The M11 fix 2 (AIM_DIAG fix 2): a stamp from a task that is not an attack - SCResident's DefendHoldSC, a resident
        /// that holds fire while someone stands in its line and "aims and waits" - with the held weapon's action 0, the
        /// action every one of XNPC's attacks names, deciding ranged or melee as Stamp does. Without it the gun came down
        /// 2 s into the wait (no attack task ran, so "calm"). Visual only: AimingGun stays false; a bow holding fire stays
        /// drawn while it waits.
        /// </summary>
        public static void StampHeld(EntityAlive e)
        {
            try
            {
                if (e == null || e.isEntityRemote || !ScXnpc.IsBody(e)) return;
                holdStamps++;
                StampIndex(e, 0);
            }
            catch (Exception ex)
            {
                ScLog.Once("aim-stamp-held", ex);
            }
        }

        /// <summary>The held item's action at this index shoots (ItemActionRanged, which the bows and launchers derive from too).</summary>
        internal static bool IsRangedAction(EntityAlive e, int index)
        {
            ItemClass held = e.inventory != null ? e.inventory.holdingItem : null;
            ItemAction[] acts = held != null ? held.Actions : null;
            return acts != null && index >= 0 && index < acts.Length && acts[index] is ItemActionRanged;
        }

        /// <summary>Start: the gun comes up as soon as the ranged attack is chosen, before its first shot. Both attack tasks.</summary>
        [HarmonyPatch]
        public static class Patch_AttackStart
        {
            [HarmonyPrepare]
            public static bool Prepare() { return AttackTargets("Start").Count > 0; }

            [HarmonyTargetMethods]
            public static IEnumerable<MethodBase> TargetMethods() { return AttackTargets("Start"); }

            [HarmonyPostfix]
            public static void Postfix(UAITaskBase __instance, Context _context)
            {
                Stamp(__instance, _context);
            }
        }

        /// <summary>Update runs on every AI tick while the attack is the chosen action, shooting or not (turning, throttled). Both attack tasks.</summary>
        [HarmonyPatch]
        public static class Patch_AttackUpdate
        {
            [HarmonyPrepare]
            public static bool Prepare() { return AttackTargets("Update").Count > 0; }

            [HarmonyTargetMethods]
            public static IEnumerable<MethodBase> TargetMethods() { return AttackTargets("Update"); }

            [HarmonyPostfix]
            public static void Postfix(UAITaskBase __instance, Context _context)
            {
                Stamp(__instance, _context);
            }
        }

        /// <summary>SCore's back-off task's Update (UAI.UAITaskBackupFromTargetSDX.Update(Context), SCore 3.2.36), or null.</summary>
        public static MethodBase BackoffTarget()
        {
            return AccessTools.DeclaredMethod(typeof(UAITaskBackupFromTargetSDX), "Update", new[] { typeof(Context) });
        }

        /// <summary>
        /// The M11 fix 2, the user's decision "aim while backing off" (AIM_DIAG's optional fix): 0-XNPCCore's
        /// BackupKeepDistance (weight 7 inside 4 m, beating its own attack's 6) and BackupRanged both run SCore's
        /// UAITaskBackupFromTargetSDX, and no attack task stamps while it walks the NPC away, so the gun came down 2 s into
        /// every back-off ("calm") - in close fights most of the time. This postfix stamps ranged while the task's target
        /// is alive and within the held gun's range; FacingAway still lowers it while the NPC walks with its back to the
        /// target. Visual only, as every stamp. One new target (ModApi's ExpectedTargets): declared on the SCore class,
        /// exact parameter types, a Prepare that logs an error and turns the patch off when it is gone and never throws.
        /// The V4 sibling (UAITaskBackupFromTargetSDXV4) is not patched: no package our bodies carry names it.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_BackoffAim
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                try
                {
                    if (BackoffTarget() != null) return true;
                    ScLog.Err("UAITaskBackupFromTargetSDX.Update(Context) not found (SCore update?) - NPCs lower their gun while they back off");
                }
                catch (Exception e)
                {
                    ScLog.Once("aim-backoff-bind", e);
                }
                return false;
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return BackoffTarget(); }

            [HarmonyPostfix]
            public static void Postfix(Context _context)
            {
                try
                {
                    EntityAlive e = _context != null ? _context.Self : null;
                    if (e == null || e.isEntityRemote || !ScXnpc.IsBody(e)) return;
                    EntityAlive t = UAIUtils.ConvertToEntityAlive(_context.ActionData.Target);
                    if (t == null || t == e || t.IsDead()) return;
                    // the gun in hand and the target within its range: what the pack's own attack asks before it shoots
                    Inventory inv = e.inventory;
                    ItemClass held = inv != null ? inv.holdingItem : null;
                    ItemActionRanged ranged = held != null && held.Actions != null && held.Actions.Length > 0 ? held.Actions[0] as ItemActionRanged : null;
                    ItemActionRanged.ItemActionDataRanged rd = ranged != null && inv.holdingItemData != null && inv.holdingItemData.actionData != null
                                                             && inv.holdingItemData.actionData.Count > 0
                        ? inv.holdingItemData.actionData[0] as ItemActionRanged.ItemActionDataRanged : null;
                    if (rd == null || e.GetDistance(t) > ranged.GetRange(rd)) return;
                    backoffStamps++;
                    Sweep(Time.time);
                    StampIndex(e, 0);
                }
                catch (Exception ex)
                {
                    ScLog.Once("aim-backoff", ex);
                }
            }
        }

        // ---- what: the animator ----

        /// <summary>
        /// Called every frame for each of our bodies from Patches_Xnpc's AvatarSdcsNpcController.Update postfix (X7), after
        /// XNPC's whole frame; a use animation lowers the gun (Blocker). IsAiming is re-asserted every frame because a
        /// controller swap resets every animator parameter; the setter only writes (and syncs) a change.
        /// </summary>
        public static void Drive(AvatarSDCSController c, EntityAlive e)
        {
            try
            {
                if (e.isEntityRemote) return;                       // the owner decides; the parameters reach everyone else
                State s;
                if (!states.TryGetValue(e.entityId, out s)) return; // no recent ranged fight: nothing of ours is set
                driveCalls++;
                Animator a = c.anim;
                CacheLayers(s, a);
                float now = Time.time;
                int hold;
                string why = Blocker(c, e, s, now, out hold);
                bool aim = why == null && AimHoldTypes.Contains(hold);
                c._setBool(AvatarController.isAimingHash, aim);
                s.Aiming = aim;
                s.Up = why == null;
                DriveBow(c, a, s, why == null && IsBow(hold), now);
                s.Why = why ?? (aim ? "aiming" : (s.Drawn ? "bow drawn" : "bow between shots"));
                if (!aim && !s.Drawn && now - Math.Max(s.LastRanged, s.LastMelee) > ScConfig.SurvivorAimHoldSeconds + ForgetAfter)
                    states.Remove(e.entityId);
            }
            catch (Exception ex)
            {
                ScLog.Once("aim-drive", ex);
            }
        }

        /// <summary>The player controller's carry pose parameter (0 = unarmed, 5 pistol, 6 rifle, 3 club ...), hashed as 0-XNPCCore hashes it.</summary>
        private static readonly int WeaponCarryHash = Animator.StringToHash("WeaponCarry");
        /// <summary>Animators (instance ids) known to have WeaponCarry: GetInteger on a missing parameter logs a warning every call.</summary>
        private static readonly Dictionary<int, bool> carryAnimators = new Dictionary<int, bool>();

        /// <summary>
        /// The M11 fix 2 (AIM_DIAG fix 1, mechanism A). The arms of a pistol or rifle come from the carry pose layers, which
        /// WeaponCarry selects; IsAiming's additive aim only adds to them. 0-XNPCCore writes WeaponCarry in its Update only
        /// when its own cache (sdcsCarryLast) changes, so a parameter reset (the Animator's GameObject deactivated, a
        /// controller reassigned, a Rebind) or a write dropped while the Animator was disabled or inactive (a ragdoll, a
        /// rebuild) left it at its default 0, unarmed - arms hanging under a correct IsAiming - until the next weapon swap.
        /// Re-asserted here every frame with 0-XNPCCore's own formula (AnimationDelayData.AnimationDelay[hold type].Carry),
        /// so the two never disagree; written only on a mismatch, straight to the Animator as 0-XNPCCore does (a local value,
        /// not sent: it runs on every machine, like 0-XNPCCore's write), and not while a use animation plays (0-XNPCCore's
        /// own gate: the bandage clip owns the arms). A future 0-XNPCCore holster or alternate carry would be overridden
        /// here. Called from X7 before Drive; carryFixes in sc aim counts the repairs - it shows whether the hole is ever hit.
        /// </summary>
        public static void KeepCarry(AvatarSDCSController c, EntityAlive e)
        {
            try
            {
                Animator a = c.anim;
                if (a == null || !a.isActiveAndEnabled || a.runtimeAnimatorController == null || e.IsDead() || c.IsAnimationUsePlaying()) return;
                ItemClass held = e.inventory != null ? e.inventory.holdingItem : null;
                if (held == null || held.HoldType == null) return;
                int hold = held.HoldType.Value;
                AnimationDelayData.AnimationDelays[] d = AnimationDelayData.AnimationDelay;
                if (d == null || hold < 0 || hold >= d.Length) return;
                int id = a.GetInstanceID();
                bool has;
                if (!carryAnimators.TryGetValue(id, out has))
                {
                    has = false;
                    foreach (AnimatorControllerParameter p in a.parameters)
                        if (p.nameHash == WeaponCarryHash && p.type == AnimatorControllerParameterType.Int) { has = true; break; }
                    if (carryAnimators.Count > 512) carryAnimators.Clear();
                    carryAnimators[id] = has;
                }
                if (!has) return;
                int carry = d[hold].Carry;
                if (a.GetInteger(WeaponCarryHash) == carry) return;
                a.SetInteger(WeaponCarryHash, carry);
                carryFixes++;
            }
            catch (Exception ex)
            {
                ScLog.Once("aim-carry", ex);
            }
        }

        /// <summary>Why the weapon must stay down, or null when it goes up. Also returns the held item's hold type.</summary>
        private static string Blocker(AvatarSDCSController c, EntityAlive e, State s, float now, out int hold)
        {
            hold = -1;
            if (!ScConfig.SurvivorAimPose) return "off";
            if (e.IsDead()) return "dead";
            ItemClass held = e.inventory != null ? e.inventory.holdingItem : null;
            if (held == null) return "empty hands";
            ItemAction[] acts = held.Actions;
            // a player item shoots on Action0 (its Action1 is the zoom); either one shooting makes it a ranged weapon
            bool shoots = acts != null && ((acts.Length > 0 && acts[0] is ItemActionRanged) || (acts.Length > 1 && acts[1] is ItemActionRanged));
            if (!shoots) return "not a ranged weapon";
            hold = held.HoldType.Value;
            if (!AimHoldTypes.Contains(hold) && !IsBow(hold)) return "no aim pose for this hold type";
            if (s.LastMelee > s.LastRanged) return "melee";
            if (now - s.LastRanged > ScConfig.SurvivorAimHoldSeconds) return "calm";
            if (e.emodel == null || e.emodel.IsRagdollActive) return "ragdoll";
            if (e.bodyDamage.CurrentStun != EnumEntityStunType.None) return "stunned";
            if (c.IsAnimationUsePlaying()) return "using an item";
            if (Reloading(c.anim, s) || (e.Buffs != null && e.Buffs.GetCustomVar("SdcsReloading") > 0f)) return "reloading";
            if (FacingAway(e, s.Up ? FacingDrop : FacingRaise)) return "facing away";
            return null;
        }

        private static void CacheLayers(State s, Animator a)
        {
            int id = a.GetInstanceID();
            if (s.AnimId == id) return;
            s.AnimId = id;
            s.AimLayer = a.GetLayerIndex("AdditiveAimPoses");
            s.BowLayer = a.GetLayerIndex("BowDrawAndFire");
            s.ReloadLayer = a.GetLayerIndex("UpperBodyUseAndReload");
        }

        /// <summary>The state a layer is in, or blending into.</summary>
        private static int Destination(Animator a, int layer)
        {
            return (a.IsInTransition(layer) ? a.GetNextAnimatorStateInfo(layer) : a.GetCurrentAnimatorStateInfo(layer)).shortNameHash;
        }

        /// <summary>
        /// A reload clip is playing (or the Reload bool is set): a gun reloads on layer 19, which rests in Empty; a bow
        /// reloads on its own layer 18. 0-XNPCCore reloads our bodies the vanilla way and says so in the SdcsReloading
        /// cvar, which Blocker reads as well. The clip test dates from SC's former NPC-item bodies, whose framework only
        /// pulsed the Reload bool; it stays as a second witness.
        /// </summary>
        private static bool Reloading(Animator a, State s)
        {
            if (a.GetBool(AvatarController.reloadHash)) return true;
            if (s.ReloadLayer >= 0 && Destination(a, s.ReloadLayer) != EmptyState) return true;
            if (s.BowLayer >= 0)
            {
                int st = Destination(a, s.BowLayer);
                if (st == BowReload || st == BowReload53) return true;
            }
            return false;
        }

        /// <summary>
        /// Who the NPC's weapon is meant for (the M11 fix 2, AIM_DIAG fix 3): while it runs an attack task, SCResident's
        /// hold-fire task or SCore's back-off task (the three that stamp), the target that task works on - SCore's attack task
        /// keeps its own action target when the attack target is cleared, which hold fire (Patch_HoldFireAcross,
        /// DefendHoldSC) now does often, and a revenge target can be another entity; else the live attack target, else the
        /// revenge target. Null when none is alive.
        /// </summary>
        private static EntityAlive FacingTarget(EntityAlive e)
        {
            Context ctx = e.utilityAIContext;
            UAITaskBase task = ctx != null ? ctx.ActionData.CurrentTask : null;
            if (task is UAITaskAttackTargetEntity || task is UAITaskDefendHoldSC || task is UAITaskBackupFromTargetSDX)
            {
                EntityAlive at = ctx.ActionData.Target as EntityAlive;
                if (at != null && at != e && !at.IsDead()) return at;     // a self-target attack falls through to its attack/revenge target
            }
            EntityAlive t = e.GetAttackTarget();
            if (t == null || t == e || t.IsDead()) t = e.GetRevengeTarget();
            return t == null || t == e || t.IsDead() ? null : t;
        }

        /// <summary>The target the NPC fights (FacingTarget) is more than <paramref name="limit"/> degrees off its facing.</summary>
        private static bool FacingAway(EntityAlive e, float limit)
        {
            EntityAlive t = FacingTarget(e);
            if (t == null) return false;
            float dx = t.position.x - e.position.x, dz = t.position.z - e.position.z;
            if (dx * dx + dz * dz < 0.01f) return false;
            float yaw = 0f - (float)(Math.Atan2(dz, dx) * 180.0 / Math.PI) + 90f;
            return Mathf.Abs(Mathf.DeltaAngle(e.rotation.y, yaw)) > limit;
        }

        /// <summary>
        /// Bows draw on layer 18 instead of aiming. The draw is an AnyState transition on WeaponPreFire, so it is only
        /// triggered from rest (2HBowHold or Empty, not mid-blend): earlier, it would cut the release clip short. From
        /// rest the cycle is draw, hold the draw, WeaponFire from SCore's shot, release, rest, draw again. At 75 shots a
        /// minute a shot can land mid-draw; the draw state releases at once then. Lowering is vanilla's cancel
        /// (SpecialAttack2): WeaponPreFireCancel takes the draw back to rest. _setTrigger queues a network message on
        /// every call, so a trigger is only set when it is not already pending.
        /// </summary>
        private static void DriveBow(AvatarSDCSController c, Animator a, State s, bool want, float now)
        {
            int layer = s.BowLayer;
            if (layer < 0) { s.Drawn = false; return; }
            bool blending = a.IsInTransition(layer);
            int st = Destination(a, layer);
            if (want)
            {
                // a cancel from a one-frame blocker (a tick of melee, the facing edge) is still pending in the draw,
                // which only takes it at its exit time: withdraw it, or the bow drops and is drawn again
                if ((st == BowDraw || st == BowDraw53) && a.GetBool(AvatarController.weaponPreFireCancelHash))
                    c._resetTrigger(AvatarController.weaponPreFireCancelHash);
                bool rest = !blending && (st == BowHold || st == BowHold53 || st == EmptyState);
                if (rest && now - s.DrawAt > RedrawGap && !a.GetBool(AvatarController.weaponPreFireHash))
                {
                    c._resetTrigger(AvatarController.weaponPreFireCancelHash);
                    c._setTrigger(AvatarController.weaponPreFireHash);
                    s.DrawAt = now;
                }
            }
            else if (st == BowDraw || st == BowDraw53 || a.GetBool(AvatarController.weaponPreFireHash))
            {
                c._resetTrigger(AvatarController.weaponPreFireHash);
                if (!a.GetBool(AvatarController.weaponPreFireCancelHash)) c._setTrigger(AvatarController.weaponPreFireCancelHash);
            }
            s.Drawn = st == BowDraw || st == BowDraw53;
        }

        // ---- visual only ----

        /// <summary>
        /// Keeps the aim cosmetic. AimingGun's getter reads the animator's IsAiming, which the driver now sets, and the
        /// game would then give these NPCs aimed spread (SpreadMultiplierAiming), a critical on every hit (fireShot) and
        /// the smaller aimed kickback. All three read this getter, so answering false here leaves them as they were
        /// before the pose existed - XNPC's tuned spread stays exactly as XNPC set it; the animator flag, and the pose,
        /// stay. The getter is virtual (EntityZombie overrides it, nothing on the survivor or raider chain does), so Mono
        /// cannot inline it into its callers; the counter in `sc aim` proves it runs.
        /// </summary>
        [HarmonyPatch(typeof(EntityAlive), nameof(EntityAlive.AimingGun), MethodType.Getter)]
        public static class Patch_AimingGunVisualOnly
        {
            [HarmonyPrefix]
            public static bool Prefix(EntityAlive __instance, ref bool __result)
            {
                if (!ScConfig.SurvivorAimVisualOnly || !ScXnpc.IsBody(__instance)) return true;
                getterCalls++;
                __result = false;
                return false;
            }
        }

        // ---- diagnostics: sc aim [id|name] ----

        public static string Describe(EntityAlive e)
        {
            var sb = new StringBuilder();
            sb.Append("[SC] aim: pose=").Append(ScConfig.SurvivorAimPose).Append(" visualOnly=").Append(ScConfig.SurvivorAimVisualOnly)
              .Append(" hold=").Append(ScConfig.SurvivorAimHoldSeconds.ToString("0.0")).Append("s; hook calls: attack task=").Append(stampCalls)
              .Append(" hold fire=").Append(holdStamps).Append(" back off=").Append(backoffStamps)
              .Append(" driver=").Append(driveCalls).Append(" AimingGun getter=").Append(getterCalls).Append(" carryFixes=").Append(carryFixes)
              .Append("; in a ranged fight: ").Append(states.Count);
            float now = Time.time;
            if (e == null)
            {
                foreach (KeyValuePair<int, State> kv in states)
                    sb.Append("\n  ").Append(kv.Key).Append(' ').Append(kv.Value.Why).Append(" ranged ").Append(Age(now, kv.Value.LastRanged))
                      .Append(" melee ").Append(Age(now, kv.Value.LastMelee));
                return sb.ToString();
            }
            sb.Append("\n  ").Append(ScCommunity.DisplayName(e)).Append(" id ").Append(e.entityId).Append(' ').Append(e.GetType().Name)
              .Append(" xnpcBody=").Append(ScXnpc.IsBody(e)).Append(" remote=").Append(e.isEntityRemote);
            ItemClass held = e.inventory != null ? e.inventory.holdingItem : null;
            if (held != null)
            {
                ItemAction[] acts = held.Actions;
                sb.Append("\n  holding ").Append(held.GetItemName()).Append(" holdType ").Append(held.HoldType.Value).Append(" action0=")
                  .Append(acts != null && acts.Length > 0 && acts[0] != null ? acts[0].GetType().Name : "none").Append(" action1=")
                  .Append(acts != null && acts.Length > 1 && acts[1] != null ? acts[1].GetType().Name : "none");
            }
            State s;
            if (states.TryGetValue(e.entityId, out s))
                sb.Append("\n  state: ").Append(s.Why).Append(" ranged ").Append(Age(now, s.LastRanged)).Append(" melee ").Append(Age(now, s.LastMelee))
                  .Append(" aiming=").Append(s.Aiming).Append(" drawn=").Append(s.Drawn).Append(" lastDraw ").Append(Age(now, s.DrawAt));
            else sb.Append("\n  state: none (no ranged attack in the last ").Append(ForgetAfter.ToString("0")).Append(" s)");
            EntityAlive ft = FacingTarget(e);
            UAITaskBase task = e.utilityAIContext != null ? e.utilityAIContext.ActionData.CurrentTask : null;
            sb.Append("\n  game reads AimingGun=").Append(e.AimingGun).Append(" target=")
              .Append(e.GetAttackTarget() != null ? e.GetAttackTarget().entityId.ToString() : "-").Append(" facing judged on=").Append(ft != null ? ft.entityId.ToString() : "-")
              .Append(" (task ").Append(task != null ? task.GetType().Name : "-").Append(')')
              .Append(" facingAway=").Append(FacingAway(e, s != null && s.Up ? FacingDrop : FacingRaise))
              .Append(" stun=").Append(e.bodyDamage.CurrentStun);
            Animator a = e.emodel != null && e.emodel.avatarController != null ? e.emodel.avatarController.anim : null;
            if (a == null) { sb.Append("\n  no animator"); return sb.ToString(); }
            int want = -1;
            if (held != null && held.HoldType != null && AnimationDelayData.AnimationDelay != null && held.HoldType.Value >= 0 && held.HoldType.Value < AnimationDelayData.AnimationDelay.Length)
                want = AnimationDelayData.AnimationDelay[held.HoldType.Value].Carry;
            sb.Append("\n  animator IsAiming=").Append(a.GetBool(AvatarController.isAimingHash)).Append(" IsCrouching=").Append(a.GetBool(AvatarController.isCrouchingHash))
              .Append(" Reload=").Append(a.GetBool(AvatarController.reloadHash)).Append(" WeaponHoldType=").Append(a.GetInteger(AvatarController.weaponHoldTypeHash))
              .Append(" WeaponCarry=").Append(a.GetInteger(WeaponCarryHash)).Append(" (the hold type's ").Append(want).Append(')');
            AppendLayer(sb, a, "AdditiveAimPoses");
            AppendLayer(sb, a, "BowDrawAndFire");
            AppendLayer(sb, a, "UpperBodyUseAndReload");
            return sb.ToString();
        }

        private static string Age(float now, float t)
        {
            return t < -999f ? "never" : (now - t).ToString("0.0") + "s ago";
        }

        private static void AppendLayer(StringBuilder sb, Animator a, string name)
        {
            int i = a.GetLayerIndex(name);
            sb.Append("\n  layer ").Append(i).Append(' ').Append(name);
            if (i < 0) return;
            AnimatorStateInfo st = a.GetCurrentAnimatorStateInfo(i);
            sb.Append(" w=").Append(a.GetLayerWeight(i).ToString("0.00")).Append(" state=").Append(StateName(st.shortNameHash))
              .Append(a.IsInTransition(i) ? " -> " + StateName(a.GetNextAnimatorStateInfo(i).shortNameHash) : "");
            foreach (AnimatorClipInfo ci in a.GetCurrentAnimatorClipInfo(i))
                sb.Append(' ').Append(ci.clip != null ? ci.clip.name : "?").Append('(').Append(ci.weight.ToString("0.00")).Append(')');
        }

        private static string StateName(int h)
        {
            if (h == EmptyState) return "Empty";
            if (h == BowHold || h == BowHold53) return "BowHold";
            if (h == BowDraw || h == BowDraw53) return "BowDraw";
            if (h == BowRelease || h == BowRelease53) return "BowRelease";
            if (h == BowReload || h == BowReload53) return "BowReload";
            return h.ToString();
        }
    }
}
