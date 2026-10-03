using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Body fixes for the community's XNPC bodies: the classes SC drives on 0-XNPCCore's player body (ScXnpc.IsBody - the
    /// survivor classes the community owns and, since M10, SC's raiders). 0-XNPCCore builds and drives those bodies itself
    /// (EModelSdcsNpc and AvatarSdcsNpcController: the mesh build, locomotion, carry and the look pitch), Patches_Xnpc feeds
    /// that build SC's look (X1) and re-roots its eyes after every build (X2). What is left here is what the player body
    /// still gets wrong on an NPC and XNPC does not fix: the eye renderer's root bone (FixEyeRenderers, called from X2) and
    /// the gaze safety net behind it, the asset release on despawn, and - each behind its own config switch, because only
    /// a test in game shows whether XNPC's bodies need it - the hit-collider layers (XnpcBodyColliderLayers), the jump
    /// trigger (XnpcBodyJumpAnim) and the get-up (XnpcBodyGetUp). The physics-height guard covers any entity.
    ///
    /// Until M10 round 2 this file was also the shim that let SC's own classes wear the player body (an exact EModelSDCS:
    /// the model's Init and mesh build, SwitchModelAndView, the skin texture, hold types, carry, locomotion, pitch, swing
    /// speed and the fade-in). SC defines no such class any more - its survivors are 0-XNPCCore's (D11) and its raiders
    /// ride XNPC's body (M10) - so those hooks are gone, and with them the ExpectedTargets they had.
    ///
    /// A hook that meets a player-body NPC that is not one of ours (0-XNPCCore's own menu classes such as npcSdcsTest, or
    /// another mod's) counts it under the hook's name (sc xnpc) and steps aside, as if this mod were not installed.
    /// Nothing here touches a real player. The class and nested type names are kept: the dev core flips
    /// Patch_RagdollPin.Enabled and Patch_RagdollAsPlayer.Enabled by reflection ("SurvivorCommunity.Patches_Sdcs+...").
    /// </summary>
    public static class Patches_Sdcs
    {
        /// <summary>Times each hook met a player-body NPC that is not ours and stepped aside (sc xnpc). Main thread only.</summary>
        private static readonly Dictionary<string, int> foreignSkips = new Dictionary<string, int>();

        internal static void CountForeign(string hook)
        {
            int n;
            foreignSkips.TryGetValue(hook, out n);
            foreignSkips[hook] = n + 1;
        }

        internal static string ForeignReport()
        {
            if (foreignSkips.Count == 0) return "none yet";
            var keys = new List<string>(foreignSkips.Keys);
            keys.Sort(StringComparer.Ordinal);
            var parts = new List<string>();
            foreach (string k in keys) parts.Add(k + "=" + foreignSkips[k]);
            return string.Join(", ", parts.ToArray());
        }

        public static void Reset()
        {
            foreignSkips.Clear();
            GetUp.Reset();
        }

        /// <summary>
        /// One of our XNPC bodies (ScXnpc.IsBody). Any other NPC on a player-body model - 0-XNPCCore's own classes, or
        /// another mod's - is counted under the hook's name and left alone. Type and reference tests only, like IsBody, so
        /// it answers for a body that is being destroyed.
        /// </summary>
        private static bool Body(Entity e, string hook)
        {
            if (ScXnpc.IsBody(e)) return true;
            if ((object)e != null && !(e is EntityPlayer) && e.emodel is EModelSDCS) CountForeign(hook);
            return false;
        }

        /// <summary>The same by model (ScXnpc.IsBodyModel), for the hooks on the model itself. A player's model is never counted.</summary>
        private static bool BodyModel(EModelBase m, string hook)
        {
            if (ScXnpc.IsBodyModel(m)) return true;
            EModelSDCS s = m as EModelSDCS;
            if ((object)s != null && (object)s.playerEntity == null && !(s.entity is EntityPlayer)) CountForeign(hook);
            return false;
        }

        /// <summary>
        /// Makes one of our bodies shootable. EntityFactory puts every capsule and box collider of a freshly built
        /// entity on layer 14 (Constants.cLayerNoCollision) and EModelBase.SwitchModelAndView is what normally
        /// puts the model's own colliders back on layer 0 - but a player body is built during Init, i.e. BEFORE the
        /// factory's pass, so its hit capsules stay on the no-collision layer and every shot and swing goes straight
        /// through. Measured on SC's former player-body survivors: theirs were on layer 14 while a pack NPC's were on 0.
        /// 0-XNPCCore builds its bodies inside Init as well, so ours get the fix after the factory has finished
        /// (PostInit, Patches_Npc) and after every rebuild (Patches_Xnpc X2) while XnpcBodyColliderLayers is on. With
        /// their colliders on layer 0 they need the self-hit guard too (Patches_Xnpc X8).
        /// </summary>
        public static void FixColliderLayers(EntityAlive e)
        {
            try
            {
                EModelSDCS m = Body(e, "colliders") && ScConfig.XnpcBodyColliderLayers ? e.emodel as EModelSDCS : null;
                if (m == null) return;
                Transform model = m.modelTransform != null ? m.modelTransform : (m.baseRig != null ? m.baseRig.transform : null);
                if (model == null) return;
                m.SetColliderLayers(model, 0);
            }
            catch (Exception ex)
            {
                ScLog.Once("sdcs-colliderlayer", ex);
            }
        }

        /// <summary>
        /// Re-roots the generated eye renderer on the head bone. The eye shader rotates each eyeball about a
        /// pivot expressed in the renderer's root-bone space, and the gaze controller always supplies that pivot
        /// in HEAD space - but EModelBase.SwitchModelAndView re-roots every untagged skinned renderer of a
        /// non-player entity onto the pelvis, which leaves the pivot a whole torso away and slides the eyeballs
        /// out of the face. Vanilla exempts renderers tagged "E_BP_Eye"; the generated body has no tags, so the
        /// tag goes on here (for vanilla's own pass) and the same settings are applied directly, because a
        /// rebuild from an equipment change makes new renderers without a SwitchModelAndView after it. Called by
        /// Patches_Xnpc (X2) after every build of one of our bodies.
        /// </summary>
        public static void FixEyeRenderers(EModelSDCS m)
        {
            if (m == null || m.baseRig == null) return;
            try
            {
                Transform head = m.GetHeadTransform();
                if (head == null) return;
                SkinnedMeshRenderer[] all = m.baseRig.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    SkinnedMeshRenderer smr = all[i];
                    if (smr == null || smr.sharedMaterial == null || smr.sharedMaterial.shader == null) continue;
                    if (smr.sharedMaterial.shader.name != EyeShader) continue;
                    if (!smr.CompareTag(EyeTag)) { try { smr.gameObject.tag = EyeTag; } catch (Exception) { } }
                    if (smr.rootBone != head)
                    {
                        smr.rootBone = head;
                        Bounds b = smr.localBounds;
                        b.center = Vector3.zero;
                        b.extents = new Vector3(0.25f, 0.25f, 0.25f);
                        smr.localBounds = b;
                    }
                    smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }
            catch (Exception e)
            {
                ScLog.Once("sdcs-eye-rootbone", e);
            }
        }
        private const string EyeTag = "E_BP_Eye";
        private const string EyeShader = "Game/SDCS/Eye";

        private static void Release(EModelSDCS m)
        {
            try
            {
                if (m.baseRig == null) return;
                // the meshes and materials stitched for this rig are instances, and the asset handles are
                // refcounted per rig - players never despawn so vanilla never frees them
                SDCSUtils.AssetRefs refs = SDCSUtils.RemoveAssetOwnerForRig(m.baseRig);
                SDCSUtils.DestroyViz(m.baseRig, true);
                if (refs != null) refs.ReleaseAssets();
            }
            catch (Exception e)
            {
                ScLog.Once("sdcs-release", e);
            }
        }

        /// <summary>
        /// Safety net for the eyes. FixEyeRenderers above (from Patches_Xnpc X2) puts the eye renderer back on the head
        /// bone, which is what the shader's rotation pivot assumes; if that ever fails (a game update renaming the tag or
        /// the shader), the gaze rotation would slide the eyeballs out of the face, so neutralise it instead.
        /// </summary>
        [HarmonyPatch(typeof(CharacterGazeController), "LateUpdate")]
        public static class Patch_GazeEyes
        {
            private static readonly Vector4 NoRotation = new Vector4(0f, 0f, 0f, 1f);

            [HarmonyPostfix]
            public static void Postfix(CharacterGazeController __instance)
            {
                try
                {
                    if (__instance == null || __instance.eyeMaterial == null) return;
                    EntityAlive e = __instance.entityAlive;
                    if (!Body(e, "gaze")) return;
                    // with the eye renderer rooted at the head the shader's pivot is right and the gaze is safe
                    SkinnedMeshRenderer smr = __instance.eyeSkinnedMeshRenderer;
                    if (smr != null && smr.rootBone != null && smr.rootBone == __instance.headTransform) return;
                    __instance.eyeMaterial.SetVector("_LeftEyeRotation", NoRotation);
                    __instance.eyeMaterial.SetVector("_RightEyeRotation", NoRotation);
                }
                catch (Exception ex)
                {
                    ScLog.Once("sdcs-eyes", ex);
                }
            }
        }

        /// <summary>
        /// The jump animation. LegacyAvatarController.StartAnimationJump fires the trigger named "JumpStart",
        /// which is what the zombie and NPC animators listen for; the player animator this rig runs on has no
        /// such parameter - AvatarMultiBodyController jumps the same body with "JumpTrigger"
        /// (AvatarController.cs:667 vs :693) - so the trigger went nowhere and a body hopped blocks in its
        /// idle pose. Landing already matches: both animators use "JumpLand". Fired here as well as the
        /// original, so nothing is taken away from any other controller. 0-XNPCCore fires no JumpTrigger either, so
        /// our bodies get it while XnpcBodyJumpAnim is on.
        /// </summary>
        [HarmonyPatch(typeof(LegacyAvatarController), nameof(LegacyAvatarController.StartAnimationJump), new[] { typeof(AnimJumpMode) })]
        public static class Patch_JumpAnim
        {
            [HarmonyPostfix]
            public static void Postfix(LegacyAvatarController __instance, AnimJumpMode jumpMode)
            {
                try
                {
                    if (jumpMode != AnimJumpMode.Start) return;
                    if (!(__instance is AvatarSDCSController) || __instance.anim == null) return;
                    EntityAlive e = __instance.entity;
                    if (!Body(e, "jumpanim") || !ScConfig.XnpcBodyJumpAnim) return;
                    __instance._setTrigger(AvatarController.jumpTriggerHash);
                }
                catch (Exception ex)
                {
                    ScLog.Once("sdcs-jumpanim", ex);
                }
            }
        }

        /// <summary>
        /// Stops a knocked-down NPC throwing once per tick. Entity.PhysicsSetHeight writes straight through
        /// physicsCapsuleCollider with no null check; that capsule is only set up when the model has a child tagged
        /// LargeEntityBlocker (Entity.PhysicsInit), which SC's former player-body classes did not have. SCore's
        /// workaround for NPCs stuck in a crouch calls SetHeight on every tick an NPC is in a Getup or Prone stun
        /// (EntityAliveSDX.OnUpdateLive), so knocking one of those survivors down filled the log with caught null
        /// references - 80 of them in one playtest - and buried everything else. Skipped rather than faked: with no
        /// blocker capsule there is nothing to resize, and the character-controller half of SetHeight has already run.
        /// The one field the original writes before it throws is kept. 0-XNPCCore gives its bodies a blocker
        /// (EModelSdcsNpc.EnsureLargeEntityBlocker); the guard stays for any entity that still has none, ours or not,
        /// because for all of them the original can only throw.
        /// </summary>
        [HarmonyPatch(typeof(Entity), nameof(Entity.PhysicsSetHeight))]
        public static class Patch_PhysicsHeight
        {
            [HarmonyPrefix]
            public static bool Prefix(Entity __instance, float _height)
            {
                if (__instance == null || __instance.physicsCapsuleCollider != null) return true;
                __instance.physicsHeight = _height;
                return false;
            }
        }

        /// <summary>
        /// Lets one of our knocked-down bodies get up like a player instead of floating. EModelBase.StartRagdoll decides
        /// once whether the body is a player's (entity is EntityPlayer) and every later step keys on that: the
        /// knockdown pose and the get-up clip are cross-faded on layer 5 for a player and layer 0 otherwise.
        /// These bodies run the player animator without being EntityPlayers, so they asked for "Knockdown -
        /// Chest" and "GetUpChest" on the player rig's layer 0 (StandingIdleTurn), which has neither; a
        /// cross-fade to a missing state is dropped in silence, so the body skipped the knockdown and the
        /// get-up and blended straight into a standing idle. That pose lifts the pelvis a metre above the model
        /// root at once, while FrameUpdateRagdoll is still pinning the entity to the pelvis rigidbody until the
        /// StandCollide step - which dragged them up into the air and let them drop only when it let go.
        /// The same flag also switches a non-player's Physics object off for the ragdoll and back on at Stand,
        /// and a player's never, so correcting it after StartRagdoll has run means undoing what that method
        /// already did on the non-player branch: the Physics object goes back on and the stun is halved, as
        /// it is for a player. Only a fresh start is touched, and never a corpse, which does not get up.
        /// XNPC has no get-up handling of its own; this runs while XnpcBodyGetUp is on. A fresh start is told apart by the
        /// state before the call (the prefix): StartRagdoll on a ragdoll that is already On only lengthens it. Every fresh
        /// knockdown of a living body of ours also starts GetUp (below) - its guard on the player path, its trace on
        /// either path, so a get-up that stayed on vanilla's path still writes its line.
        /// </summary>
        [HarmonyPatch(typeof(EModelBase), nameof(EModelBase.StartRagdoll), new[] { typeof(float) })]
        public static class Patch_RagdollAsPlayer
        {
            /// <summary>Runtime switch, so the dev core can compare the two recoveries in one session.</summary>
            public static bool Enabled = true;

            [HarmonyPrefix]
            public static void Prefix(EModelBase __instance, out EModelBase.ERagdollState __state)
            {
                __state = __instance != null ? __instance.ragdollState : EModelBase.ERagdollState.Off;
            }

            [HarmonyPostfix]
            public static void Postfix(EModelBase __instance, bool __result, EModelBase.ERagdollState __state)
            {
                try
                {
                    if (!__result || __instance == null || __state == EModelBase.ERagdollState.On) return;
                    if (__instance.ragdollState != EModelBase.ERagdollState.On) return;   // a corpse
                    EntityAlive e = __instance.entity as EntityAlive;
                    if (e == null || e is EntityPlayer || e.IsDead()) return;
                    if (!BodyModel(__instance, "ragdoll"))
                    {
                        if (e is EntityAliveSDXV4 && ScXnpc.IsRaiderClass(e.entityClass))
                            ScLog.OnceOut("getup-raider-notbody", "a raider (" + ScCommunity.ClassName(e) + " #" + e.entityId
                                          + ") was knocked down but is not one of our bodies (model " + (e.emodel != null ? e.emodel.GetType().Name : "none")
                                          + ") - it gets up on vanilla's path, with no get-up clip");
                        return;
                    }
                    if (Enabled && ScConfig.XnpcBodyGetUp && !__instance.ragdollIsPlayer)
                    {
                        __instance.ragdollIsPlayer = true;
                        if (e.PhysicsTransform != null) e.PhysicsTransform.gameObject.SetActive(true);
                        __instance.ragdollDuration *= 0.5f;
                    }
                    GetUp.OnStart(__instance, e, __instance.ragdollIsPlayer);
                }
                catch (Exception ex)
                {
                    ScLog.Once("sdcs-ragdoll", ex);
                }
            }
        }

        /// <summary>
        /// Keeps one of our bodies on the ground while it gets up. EModelBase.FrameUpdateRagdoll pins a non-player's
        /// position to the pelvis rigidbody in every ragdoll state except the last two. That is right while the
        /// body is a physics ragdoll, but once the ragdoll ends the rigidbody rides the animated pelvis, and the
        /// get-up clip lifts that pelvis above the model root: the entity follows the pelvis up, which lifts the
        /// root, which lifts the pelvis again. Measured on a survivor: 40.3 to 47.5 m in 1.1 s, then straight back
        /// to 40.0 the instant the pinning stopped at StandCollide - the "floats high and comes back down".
        /// By then BlendRagdoll has already put the entity on the ground under the pelvis (PhysicsResume), so the
        /// pin write is undone from BlendOutGround on. Nothing else in those states moves the entity. Verified
        /// live: the position held at the floor through the whole get-up. Runs while XnpcBodyGetUp is on (it belongs
        /// with Patch_RagdollAsPlayer).
        /// </summary>
        [HarmonyPatch(typeof(EModelBase), "FrameUpdateRagdoll")]
        public static class Patch_RagdollPin
        {
            /// <summary>Runtime switch, so the dev core can compare both recoveries in one session.</summary>
            public static bool Enabled = true;

            [HarmonyPrefix]
            public static void Prefix(EModelBase __instance, out Vector3 __state)
            {
                __state = new Vector3(float.NaN, 0f, 0f);
                try
                {
                    if (!Enabled) return;
                    if (!BodyModel(__instance, "ragdollpin") || !ScConfig.XnpcBodyGetUp) return;
                    EModelBase.ERagdollState s = __instance.ragdollState;
                    if (s != EModelBase.ERagdollState.BlendOutGround && s != EModelBase.ERagdollState.BlendOutStand
                        && s != EModelBase.ERagdollState.Stand) return;
                    Entity e = __instance.entity;
                    if (e == null || e is EntityPlayer) return;
                    __state = e.position;
                }
                catch (Exception ex)
                {
                    ScLog.Once("sdcs-ragdollpin", ex);
                }
            }

            [HarmonyPostfix]
            public static void Postfix(EModelBase __instance, Vector3 __state)
            {
                if (float.IsNaN(__state.x) || __instance == null) return;
                Entity e = __instance.entity;
                if (e == null || (e.position - __state).sqrMagnitude <= 0.000001f) return;
                e.SetPosition(__state, false);
            }
        }

        /// <summary>
        /// Keeps the get-up clip of one of our knocked-down bodies from being cut short, and traces each get-up (the
        /// user's T2, 2026-10-03: raiders came back up without the get-up animation; survivors were not reported).
        ///
        /// On the player path (Patch_RagdollAsPlayer) the knockdown pose and the get-up ("Knockdown - Chest"/"- Back",
        /// then "GetUpChest" 2.567 s / "GetUpBack" 2.5 s) play on the player animator's layer 5, "Locomotion" - the layer
        /// that also holds the walk and run blend trees. That layer is entered from Any State on [WeaponCarry == n and
        /// IsMoving] (22 transitions, 0.25 s, one per carry and crouch; read from 3PPlayerMaleController), and the get-up
        /// states' only own exit is Empty at 90 %. So whenever IsMoving is true while the knockdown or the get-up plays,
        /// layer 5 drops into a run blend tree and the body comes up in its locomotion pose.
        ///
        /// What makes it raiders: StartRagdoll turns the animator off for the ragdoll, and LegacyAvatarController.Update
        /// writes no parameter while the animator is off, so IsMoving keeps the value it had at the knockdown until
        /// DisableRagdoll turns the animator back on and cross-fades to the knockdown pose. A raider is usually running at
        /// its target when it is knocked down (IsMoving true); a survivor standing beside the player is not. After that the
        /// controller writes IsMoving every frame from entity.speedForward/Strafe, which on these bodies come from how far
        /// the entity actually moved (SCore's EntityAliveSDXV4 overrides updateSpeedForwardAndStrafe; EntityTrader's is
        /// empty), so a slide or push during the get-up does it too. And the clip outlives the ragdoll: FrameUpdateRagdoll
        /// cross-fades to the get-up 0.25 s after DisableRagdoll and goes Off (clearing the stun, so the AI may move
        /// again) 2.4 s after it, while GetUpChest reaches its exit at 0.25 + 0.9 x 2.567 = 2.56 s - a raider that
        /// runs at its target the moment it is free cuts the clip's tail too. That is the suspected cause, not yet a
        /// measured one: the trace records it.
        ///
        /// The guard (XnpcGetUpGuard, player path only): IsMoving is cleared when the ragdoll starts (the animator is
        /// already off and keeps its state, keepAnimatorStateOnDisable), and from the knockdown cross-fade on it is held
        /// false after the controller's own write each frame (from Patches_Xnpc X7, after XNPC's whole Update) for as long
        /// as layer 5 is on, or heading for, a knockdown or get-up state - past Off, until the clip has left for Empty (at
        /// most OffGrace seconds after Off). Written with the controller's _setBool without a network sync: every machine
        /// runs the ragdoll and guards its own copy (the patches are type tests). Nothing else on the layer is touched.
        ///
        /// The trace (XnpcGetUpTrace): one line per knockdown of a living body of ours, on either path - each ragdoll step
        /// with layer 5's state (during a transition, the state it is leaving) and IsMoving at that moment, how and when
        /// layer 5 left the clip, IsMoving at the knockdown, the frames IsMoving was on (and held off), InAir frames and
        /// the top speed. Main thread only (StartRagdoll, the controller's Update, the model's OnDestroy).
        /// </summary>
        internal static class GetUp
        {
            private const string LocomotionLayer = "Locomotion";
            private static readonly int EmptyHash = Animator.StringToHash("Empty");
            private static readonly int GetUpChestHash = Animator.StringToHash("GetUpChest");
            private static readonly int GetUpBackHash = Animator.StringToHash("GetUpBack");
            private static readonly int KnockChestHash = Animator.StringToHash("Knockdown - Chest");
            private static readonly int KnockBackHash = Animator.StringToHash("Knockdown - Back");

            /// <summary>A run that saw no frame for this long (its body unloaded mid get-up) is dropped at the next start.</summary>
            private const int StaleFrames = 3000;
            /// <summary>After the ragdoll is Off, the run (and the hold) lasts until the clip has left, at most this long.</summary>
            private const float OffGrace = 2f;

            private sealed class Run
            {
                public float Start;
                public int LastFrame;
                public int Layer = -1;
                public bool PlayerPath;
                public EModelBase.ERagdollState State = EModelBase.ERagdollState.On;
                public float OffAt = -1f;
                public bool MovingAtStart;
                public readonly System.Text.StringBuilder Steps = new System.Text.StringBuilder();
                public int Frames, Moving, Held, InAir;
                public float MaxSpeed2;
                public bool SawClip;
                public string Exit;
            }

            private static readonly Dictionary<int, Run> runs = new Dictionary<int, Run>();
            private static int started, vanillaPath, clearedAtStart, heldRuns, heldFrames, cutShort;

            internal static void Reset()
            {
                runs.Clear();
                started = vanillaPath = clearedAtStart = heldRuns = heldFrames = cutShort = 0;
            }

            internal static string Report()
            {
                return "knockdowns " + started + " (" + vanillaPath + " on vanilla's path), IsMoving cleared at the knockdown "
                       + clearedAtStart + ", held off in " + heldRuns + " get-up(s) (" + heldFrames + " frames), the locomotion "
                       + "layer left a get-up clip for a locomotion state " + cutShort + " time(s), " + runs.Count + " running";
            }

            private static bool IsClip(int hash)
            {
                return hash == GetUpChestHash || hash == GetUpBackHash || hash == KnockChestHash || hash == KnockBackHash;
            }

            private static string Name(int hash)
            {
                if (hash == EmptyHash) return "Empty";
                if (hash == GetUpChestHash) return "GetUpChest";
                if (hash == GetUpBackHash) return "GetUpBack";
                if (hash == KnockChestHash) return "Knockdown-Chest";
                if (hash == KnockBackHash) return "Knockdown-Back";
                return "locomotion";
            }

            /// <summary>From Patch_RagdollAsPlayer, on every fresh knockdown of a living body of ours.</summary>
            internal static void OnStart(EModelBase m, EntityAlive e, bool playerPath)
            {
                if (!ScConfig.XnpcGetUpGuard && !ScConfig.XnpcGetUpTrace) return;
                AvatarController ac = m.avatarController;
                Animator a = ac != null ? ac.GetAnimator() : null;
                if (a == null) return;
                int now = Time.frameCount;
                Run old;
                if (runs.TryGetValue(e.entityId, out old)) Finish(old, e, "knocked down again");
                if (runs.Count > 0)
                {
                    var stale = new List<int>();
                    foreach (KeyValuePair<int, Run> kv in runs)
                        if (now - kv.Value.LastFrame > StaleFrames) stale.Add(kv.Key);
                    foreach (int id in stale) runs.Remove(id);
                }
                var run = new Run { Start = Time.time, LastFrame = now, Layer = a.GetLayerIndex(LocomotionLayer), PlayerPath = playerPath };
                run.MovingAtStart = a.GetBool(AvatarController.isMovingHash);
                runs[e.entityId] = run;
                started++;
                if (!playerPath) vanillaPath++;
                if (playerPath && run.MovingAtStart && ScConfig.XnpcGetUpGuard)
                {
                    ac._setBool(AvatarController.isMovingHash, false, false);
                    clearedAtStart++;
                }
            }

            /// <summary>From Patch_OnDestroy: a body unloaded mid get-up leaves no run behind.</summary>
            internal static void Forget(int entityId)
            {
                if (runs.Count > 0) runs.Remove(entityId);
            }

            /// <summary>Every frame for every body of ours, from Patches_Xnpc X7 (after the controller's own writes).</summary>
            internal static void Tick(AvatarSDCSController c, EntityAlive e)
            {
                Run run;
                if (runs.Count == 0 || !runs.TryGetValue(e.entityId, out run)) return;
                EModelBase m = e.emodel;
                Animator a = c.anim;
                if (m == null || a == null) return;
                run.LastFrame = Time.frameCount;
                EModelBase.ERagdollState s = m.ragdollState;
                bool moving = a.GetBool(AvatarController.isMovingHash);
                int cur = 0, next = 0;
                bool inTransition = false;
                float norm = 0f;
                bool readable = run.Layer >= 0 && a.enabled;
                if (readable)
                {
                    AnimatorStateInfo info = a.GetCurrentAnimatorStateInfo(run.Layer);
                    cur = info.shortNameHash;
                    norm = info.normalizedTime;
                    inTransition = a.IsInTransition(run.Layer);
                    if (inTransition) next = a.GetNextAnimatorStateInfo(run.Layer).shortNameHash;
                }
                if (s != run.State)
                {
                    run.State = s;
                    if (run.Steps.Length > 0) run.Steps.Append(" > ");
                    run.Steps.Append(s).Append(' ').Append((Time.time - run.Start).ToString("0.00", CultureInfo.InvariantCulture))
                       .Append('[').Append(!a.enabled ? "animator off" : run.Layer < 0 ? "no locomotion layer" : Name(cur))
                       .Append(moving ? " moving" : "").Append(']');
                }
                if (s == EModelBase.ERagdollState.Dead || e.IsDead())
                {
                    Finish(run, e, "died");
                    runs.Remove(e.entityId);
                    return;
                }
                if (s == EModelBase.ERagdollState.On || s == EModelBase.ERagdollState.SpawnWait) return;   // the animator is off

                bool clipNow = IsClip(cur), clipNext = inTransition && IsClip(next);
                if (s == EModelBase.ERagdollState.Off)
                {
                    if (run.OffAt < 0f) run.OffAt = Time.time;
                    // the clip outlives the ragdoll by about 0.4 s: follow it until it has left, within OffGrace
                    if (!run.PlayerPath || !(clipNow || clipNext) || Time.time - run.OffAt > OffGrace)
                    {
                        Finish(run, e, clipNow || clipNext ? "still on the clip " + OffGrace + " s after the ragdoll ended" : null);
                        runs.Remove(e.entityId);
                        return;
                    }
                }

                if (clipNow || clipNext) run.SawClip = true;
                if (run.Exit == null && clipNow && inTransition && !clipNext)
                {
                    run.Exit = Name(cur) + " at " + norm.ToString("0.00", CultureInfo.InvariantCulture) + " for " + Name(next)
                               + (moving ? " (IsMoving)" : "") + " in " + s;
                    if (next != EmptyHash) cutShort++;
                }
                else if (run.Exit == null && run.SawClip && readable && !clipNow && !clipNext && cur != EmptyHash)
                {
                    run.Exit = "jumped to " + Name(cur) + (moving ? " (IsMoving)" : "") + " in " + s;
                    cutShort++;
                }

                run.Frames++;
                if (moving) run.Moving++;
                if (a.GetBool(AvatarController.inAirHash)) run.InAir++;
                float sp = e.speedForward * e.speedForward + e.speedStrafe * e.speedStrafe;
                if (sp > run.MaxSpeed2) run.MaxSpeed2 = sp;
                // the hold: from the knockdown cross-fade (BlendOutGround, before it has landed) for as long as the
                // layer is on or heading for a knockdown or get-up state
                if (run.PlayerPath && moving && ScConfig.XnpcGetUpGuard && (s == EModelBase.ERagdollState.BlendOutGround || clipNow || clipNext))
                {
                    c._setBool(AvatarController.isMovingHash, false, false);
                    if (run.Held == 0) heldRuns++;
                    run.Held++;
                    heldFrames++;
                }
            }

            private static void Finish(Run run, EntityAlive e, string how)
            {
                if (!ScConfig.XnpcGetUpTrace) return;
                EntityAlive target = e.GetAttackTarget();
                string clip = run.Exit != null ? "left " + run.Exit
                            : run.SawClip ? "seen, no exit seen"
                            : run.Layer < 0 ? "no locomotion layer on this animator"
                            : "never on the locomotion layer";
                ScLog.Out("get-up " + ScCommunity.DisplayName(e) + " (" + ScCommunity.ClassName(e) + " #" + e.entityId + ","
                          + (ScXnpc.IsRaider(e) ? " raider" : " survivor") + (run.PlayerPath ? ", player path)" : ", VANILLA path - XnpcBodyGetUp or the dev switch off)")
                          + (how != null ? " (" + how + ")" : "")
                          + ": " + run.Steps
                          + " | clip " + clip
                          + " | IsMoving at the knockdown " + run.MovingAtStart + ", on " + run.Moving + "/" + run.Frames
                          + " frames, held off " + run.Held + ", InAir " + run.InAir
                          + ", top speed " + Mathf.Sqrt(run.MaxSpeed2).ToString("0.000", CultureInfo.InvariantCulture)
                          + ", target " + (target != null ? target.entityId.ToString() : "none"));
            }
        }

        /// <summary>
        /// Frees a despawned body's stitched meshes, materials and asset handles (Release). 0-XNPCCore's EModelSdcsNpc
        /// declares its own OnDestroy, which calls this one as its base, and XNPC never releases them either, so our
        /// bodies are released here: without it every one that unloads leaks its rig's assets. The test is by type only
        /// (ScXnpc.IsBody), because the entity is being destroyed along with its model. A get-up run (GetUp) still open
        /// for the body is dropped with it.
        /// </summary>
        [HarmonyPatch(typeof(EModelSDCS), "OnDestroy")]
        public static class Patch_OnDestroy
        {
            [HarmonyPostfix]
            public static void Postfix(EModelSDCS __instance)
            {
                if (__instance == null || __instance.playerEntity != null) return;
                if (!Body(__instance.entity, "release")) return;
                GetUp.Forget(__instance.entity.entityId);
                Release(__instance);
            }
        }
    }
}
