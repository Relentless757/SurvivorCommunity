using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// The player-body shim. EModelSDCS (ModelType SDCS) is written for EntityPlayer: Awake looks the player up
    /// on its own game object and Init/GenerateMeshes/SwitchModelAndView and the equipment-refresh handler all
    /// dereference it. On one of our survivors that field stays null, so every one of those paths is patched
    /// to use the entity itself (SDCSUtils only ever needed an EntityAlive). SCore's weapon swap then calls
    /// the avatar controller with the class's Mesh name, which the player rig does not have - fixed there too.
    /// Nothing here touches a real player: every patch first checks playerEntity.
    /// </summary>
    public static class Patches_Sdcs
    {
        /// <summary>Models whose Init just built the body: the Init-time SwitchModelAndView must not build it twice.</summary>
        private static readonly HashSet<int> fresh = new HashSet<int>();

        public static void Reset()
        {
            fresh.Clear();
        }

        /// <summary>The entity that owns a player-body model: the player when there is one, else the NPC.</summary>
        public static EntityAlive Owner(EModelSDCS m)
        {
            if (m == null) return null;
            if (m.playerEntity != null) return m.playerEntity;
            return m.entity as EntityAlive;
        }

        /// <summary>
        /// Makes a survivor shootable. EntityFactory puts every capsule and box collider of a freshly built
        /// entity on layer 14 (Constants.cLayerNoCollision) and EModelBase.SwitchModelAndView is what normally
        /// puts the model's own colliders back on layer 0 - but for these bodies that happens during Init, i.e.
        /// BEFORE the factory's pass, so the hit capsules stayed on the no-collision layer and every shot and
        /// swing went straight through. Re-applied after the factory has finished (PostInit) and after any
        /// rebuild. Measured: ours were on layer 14 while a pack NPC's were on 0.
        /// </summary>
        public static void FixColliderLayers(EntityAlive e)
        {
            try
            {
                EModelSDCS m = e != null ? e.emodel as EModelSDCS : null;
                if (m == null || m.playerEntity != null) return;
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
        /// rebuild from an equipment change makes new renderers without a SwitchModelAndView after it.
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
                fresh.Remove(m.GetInstanceID());
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

        [HarmonyPatch(typeof(EModelSDCS), nameof(EModelSDCS.Init), new[] { typeof(World), typeof(Entity), typeof(EModelInstanceAssets) })]
        public static class Patch_Init
        {
            [HarmonyPrefix]
            public static bool Prefix(EModelSDCS __instance, World _world, Entity _entity, EModelInstanceAssets _assets)
            {
                if (__instance.playerEntity != null || _entity is EntityPlayer) return true;
                try
                {
                    __instance.entity = _entity;
                    __instance.assets = _assets;
                    EntityClass ec = EntityClass.list[_entity.entityClass];
                    __instance.entityClass = ec;
                    Look look = ScLooks.ForEntity(_entity);
                    __instance.archetype = look.ToArchetype();
                    __instance.ragdollChance = ec.RagdollOnDeathChance;
                    __instance.bHasRagdoll = ec.HasRagdoll;
                    __instance.modelTransformParent = EModelBase.FindModel(__instance.transform);
                    __instance.IsFPV = false;
                    EntityAlive alive = _entity as EntityAlive;
                    if (alive != null) alive.IsMale = look.Male;
                    ScLooks.StageGear(alive, look);                  // the first build already wears the clothes
                    __instance.createModel(_world, ec);             // -> GenerateMeshes (below)
                    FixEyeRenderers(__instance);                    // before the base SwitchModelAndView re-roots them
                    fresh.Add(__instance.GetInstanceID());          // EntityAlive.Init's SwitchModelAndView comes next
                    __instance.createAvatarController(ec);
                    // no XUiM_PlayerEquipment.HandleRefreshEquipment subscription: that event is the local player's
                    ScLog.Verbose("player body for " + ec.entityClassName + " id " + _entity.entityId + ": " + ScLooks.Describe(look));
                }
                catch (Exception e)
                {
                    ScLog.Once("sdcs-init", e);
                    throw;      // a half-built model would crash later in vanilla code with a less useful trace
                }
                return false;
            }
        }

        [HarmonyPatch(typeof(EModelSDCS), nameof(EModelSDCS.GenerateMeshes), new Type[0])]
        public static class Patch_GenerateMeshes
        {
            [HarmonyPrefix]
            public static bool Prefix(EModelSDCS __instance, ref Transform __result)
            {
                if (__instance.playerEntity != null) return true;
                try
                {
                    if (fresh.Remove(__instance.GetInstanceID()) && __instance.baseRig != null)
                    {
                        __result = __instance.baseRig.transform;    // built a moment ago by Init
                        return false;
                    }
                    EntityAlive alive = __instance.entity as EntityAlive;
                    SDCSUtils.CreateVizTP(__instance.archetype, ref __instance.baseRig, ref __instance.boneCatalog, alive, false);
                    __instance.ClothSimInit();
                    FixEyeRenderers(__instance);                    // a rebuild makes new renderers: re-root them too
                    FixColliderLayers(__instance.entity as EntityAlive);
                    __result = __instance.baseRig != null ? __instance.baseRig.transform : null;
                }
                catch (Exception e)
                {
                    ScLog.Once("sdcs-generate", e);
                    throw;
                }
                return false;
            }
        }

        /// <summary>
        /// SwitchModelAndView writes playerEntity.IsMale before the (non-virtual) base call; the one load of that
        /// field is redirected to Owner(), which is the entity for a survivor. Anchored on the field itself.
        /// </summary>
        [HarmonyPatch(typeof(EModelSDCS), nameof(EModelSDCS.SwitchModelAndView), new[] { typeof(bool), typeof(bool) })]
        public static class Patch_SwitchModelAndView
        {
            [HarmonyTranspiler]
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                FieldInfo field = AccessTools.Field(typeof(EModelSDCS), "playerEntity");
                MethodInfo owner = AccessTools.Method(typeof(Patches_Sdcs), nameof(Owner));
                int hits = 0;
                foreach (CodeInstruction ci in instructions)
                {
                    if (ci.opcode == OpCodes.Ldfld && field != null && ci.operand is FieldInfo && (FieldInfo)ci.operand == field)
                    {
                        hits++;
                        var call = new CodeInstruction(OpCodes.Call, owner);
                        call.labels.AddRange(ci.labels);
                        call.blocks.AddRange(ci.blocks);
                        yield return call;
                        continue;
                    }
                    yield return ci;
                }
                if (hits == 0) ScLog.Err("EModelSDCS.SwitchModelAndView: playerEntity load not found (game update?) - survivors with the player body will fail to spawn");
            }
        }

        [HarmonyPatch(typeof(EModelSDCS), "XUiM_PlayerEquipment_HandleRefreshEquipment")]
        public static class Patch_HandleRefreshEquipment
        {
            [HarmonyPrefix]
            public static bool Prefix(EModelSDCS __instance)
            {
                return __instance.playerEntity != null;     // a survivor's model never subscribes, this is belt and braces
            }
        }

        /// <summary>
        /// EntityFactory calls SetSkinTexture(class SkinTexture) on every spawned entity; EModelPlayer's version
        /// looks the "body" part up and then loads the texture by name without a null check. Our classes have no
        /// skin texture, so skip it for them (a real player goes through untouched).
        /// </summary>
        [HarmonyPatch(typeof(EModelPlayer), nameof(EModelPlayer.SetSkinTexture), new[] { typeof(string) })]
        public static class Patch_SetSkinTexture
        {
            [HarmonyPrefix]
            public static bool Prefix(EModelPlayer __instance, string _textureName)
            {
                EModelSDCS sdcs = __instance as EModelSDCS;
                if (sdcs == null || sdcs.playerEntity != null) return true;
                return !string.IsNullOrEmpty(_textureName);
            }
        }

        private static readonly Dictionary<int, KeyValuePair<float, Vector3>> speedSamples = new Dictionary<int, KeyValuePair<float, Vector3>>();
        private static readonly Dictionary<int, Vector2> speedSmoothed = new Dictionary<int, Vector2>();

        /// <summary>
        /// The player animator blends locomotion from Forward and Strafe given in METRES PER SECOND - that is
        /// what EntityPlayerLocal feeds it, straight from its controller's velocity. LegacyAvatarController.Update
        /// instead writes entity.speedForward, and on an EntityAliveSDX that number comes from SCore's own
        /// per-tick maths, roughly a tenth of the real speed: a survivor sprinting at 5 m/s handed the animator
        /// 0.3 and played a slow walk while sliding along. Measure the real velocity and write it after the
        /// controller has run. (SCore's EntityAliveSDX overrides updateSpeedForwardAndStrafe, so correcting the
        /// entity's own fields upstream is not reliable - the animator parameters are.)
        /// </summary>
        private static void WriteAnimatorSpeed(AvatarSDCSController c, EntityAlive e)
        {
            int id = e.entityId;
            float now = Time.time;
            Vector3 pos = e.position;
            // A body in ragdoll or getting up is not walking. Without this the ragdoll's own position changes read
            // as movement and the Locomotion layer replaced the get-up clip with run-strafe clips 0.2 s in
            // (measured). The sample is re-based on every such frame, so the snap back to the ground at the end
            // of the recovery is not taken for a burst of speed either.
            if (e.emodel != null && e.emodel.IsRagdollActive)
            {
                speedSamples[id] = new KeyValuePair<float, Vector3>(now, pos);
                speedSmoothed[id] = Vector2.zero;
                c.anim.SetFloat(AvatarController.forwardHash, 0f);
                c.anim.SetFloat(AvatarController.strafeHash, 0f);
                c.anim.SetBool(AvatarController.isMovingHash, false);
                return;
            }
            KeyValuePair<float, Vector3> last;
            if (!speedSamples.TryGetValue(id, out last))
            {
                speedSamples[id] = new KeyValuePair<float, Vector3>(now, pos);
                return;
            }
            float elapsed = now - last.Key;
            if (elapsed >= 0.05f)      // this runs every frame; measure over a window wide enough to be meaningful
            {
                speedSamples[id] = new KeyValuePair<float, Vector3>(now, pos);
                if (elapsed <= 1f)     // a bigger gap means a load or a teleport, not movement
                {
                    Vector3 vel = (pos - last.Value) / elapsed;
                    Vector3 local = e.transform.InverseTransformDirection(new Vector3(vel.x, 0f, vel.z));
                    Vector2 prev;
                    if (!speedSmoothed.TryGetValue(id, out prev)) prev = Vector2.zero;
                    speedSmoothed[id] = Vector2.Lerp(prev, new Vector2(local.x, local.z), 0.4f);
                }
            }
            Vector2 v;
            if (!speedSmoothed.TryGetValue(id, out v)) return;
            c.anim.SetFloat(AvatarController.forwardHash, v.y);
            c.anim.SetFloat(AvatarController.strafeHash, v.x);
            // ...and whether she is moving at all, which is what the Locomotion layer waits for.
            // LegacyAvatarController.Update has just written IsMoving from that same tenth-scale speedForward.
            // MovementState is written too, in the same shape the player's own controller uses, but measured
            // live the player animator does not declare that parameter at all (32 of them, no MovementState),
            // and a write to a parameter an animator does not have is dropped in silence - so it is the pair
            // above that does the work. Verified in game: at Forward 5.33 the Locomotion layer plays
            // 3P_2HRifle_runForward at weight 0.98 and blends back down to walkForward as she slows.
            float sq = v.x * v.x + v.y * v.y;
            c.anim.SetBool(AvatarController.isMovingHash, sq > 0.0001f);
            if (!e.IsDead()) c.anim.SetInteger(AvatarController.movementStateHash, (sq > 1f) ? 3 : ((sq > 0.001f) ? 1 : 0));
        }

        /// <summary>
        /// The NPC pack's weapons carry hold types for the pack's own animators (32 two-handed guns, 36 melee,
        /// 13 hands) that the player animator has no states for - every hold-pose and locomotion layer then sits in
        /// its empty state and the survivor stands in the unarmed idle with the gun hanging. Each pack weapon names
        /// the vanilla weapon it mirrors (CompatibleWeapon); a player-body NPC animates with THAT hold type, and
        /// the pack's empty hand as bare hands. Pack NPCs with their own rigs are untouched.
        /// </summary>
        public static int MapHoldType(EntityAlive e, int raw)
        {
            try
            {
                if (e == null || e.inventory == null || e.inventory.holdingItem == null) return raw;
                ItemClass ic = e.inventory.holdingItem;
                int mapped;
                if (holdTypeCache.TryGetValue(ic.Id, out mapped)) return mapped;
                mapped = raw;
                string compat;
                if (ic.Properties != null && ic.Properties.Values.TryGetValue("CompatibleWeapon", out compat) && !string.IsNullOrEmpty(compat))
                {
                    ItemClass v = ItemClass.GetItemClass(compat.Trim(), true);
                    if (v != null) mapped = v.HoldType.Value;
                }
                else
                {
                    string n = ic.GetItemName() ?? "";
                    if (n.IndexOf("EmptyHand", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("HandNPC", StringComparison.OrdinalIgnoreCase) >= 0) mapped = 0;
                }
                holdTypeCache[ic.Id] = mapped;
                return mapped;
            }
            catch (Exception ex)
            {
                ScLog.Once("sdcs-holdtype", ex);
                return raw;
            }
        }
        private static readonly Dictionary<int, int> holdTypeCache = new Dictionary<int, int>();

        [HarmonyPatch(typeof(AvatarController), "_setInt", new[] { typeof(int), typeof(int), typeof(bool) })]
        public static class Patch_SetInt
        {
            [HarmonyPrefix]
            public static void Prefix(AvatarController __instance, int _propertyHash, ref int _value)
            {
                if (_propertyHash != AvatarController.weaponHoldTypeHash) return;
                if (!(__instance is AvatarSDCSController)) return;
                EntityAlive e = __instance.entity;
                if (e == null || e is EntityPlayer) return;
                _value = MapHoldType(e, _value);
            }
        }

        /// <summary>The weapon's position in the hand is looked up by hold type too: re-apply it with the mapped one.</summary>
        [HarmonyPatch(typeof(AvatarSDCSController), nameof(AvatarSDCSController.SwitchModelAndView), new[] { typeof(string), typeof(bool), typeof(bool) })]
        public static class Patch_AvatarSwitchOffset
        {
            [HarmonyPostfix]
            public static void Postfix(AvatarSDCSController __instance, bool _bFPV)
            {
                try
                {
                    if (_bFPV || __instance == null || __instance.rightHandItemTransform == null) return;
                    EntityAlive e = __instance.entity;
                    if (e == null || e is EntityPlayer || !(e.emodel is EModelSDCS) || e.inventory == null || e.inventory.holdingItem == null) return;
                    int raw = e.inventory.holdingItem.HoldType.Value;
                    int mapped = MapHoldType(e, raw);
                    if (mapped == raw || mapped < 0 || mapped >= AnimationGunjointOffsetData.AnimationGunjointOffset.Length) return;
                    AnimationGunjointOffsetData.AnimationGunjointOffsets o = AnimationGunjointOffsetData.AnimationGunjointOffset[mapped];
                    __instance.rightHandItemTransform.SetLocalPositionAndRotation(o.position, Quaternion.Euler(o.rotation));
                }
                catch (Exception ex)
                {
                    ScLog.Once("sdcs-gunjoint", ex);
                }
            }
        }

        /// <summary>
        /// The player's own third-person controller (AvatarMultiBodyController.Update) also writes WeaponCarry every
        /// frame from AnimationDelayData; remote players receive it over the network. The SDCS controller a
        /// survivor gets writes neither, and the arm hold-pose layers key on it - so do it here, with the mapped
        /// hold type, exactly as the player controller does (not while a use animation plays).
        /// </summary>
        [HarmonyPatch(typeof(AvatarSDCSController), "Update", new Type[0])]
        public static class Patch_UpdateCarry
        {
            [HarmonyPostfix]
            public static void Postfix(AvatarSDCSController __instance)
            {
                try
                {
                    if (__instance == null || __instance.anim == null) return;
                    EntityAlive e = __instance.entity;
                    if (e == null || e is EntityPlayer || !(e.emodel is EModelSDCS)) return;
                    WriteAnimatorSpeed(__instance, e);
                    if (e.inventory == null || e.inventory.holdingItem == null) return;
                    if (__instance.IsAnimationUsePlaying()) return;
                    int hold = MapHoldType(e, e.inventory.holdingItem.HoldType.Value);
                    if (hold < 0 || hold >= AnimationDelayData.AnimationDelay.Length) return;
                    __instance._setInt(AvatarController.weaponCarryHash, AnimationDelayData.AnimationDelay[hold].Carry);
                }
                catch (Exception ex)
                {
                    ScLog.Once("sdcs-carry", ex);
                }
            }
        }

        /// <summary>
        /// Safety net for the eyes. FixEyeRenderers above puts the eye renderer back on the head bone, which is
        /// what the shader's rotation pivot assumes; if that ever fails (a game update renaming the tag or the
        /// shader), the gaze rotation would slide the eyeballs out of the face, so neutralise it instead.
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
                    if (e == null || e is EntityPlayer || !(e.emodel is EModelSDCS)) return;
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
        /// Lets a player-bodied NPC actually walk. EntityMoveHelper.Update refuses to feed movement unless the
        /// avatar controller reports a motion animation (LegacyAvatarController returns true only for a base
        /// state tagged jump or move), and it skips that check for animators that force root motion - which the
        /// zombie and NPC animators do and the player animator does not. A survivor standing in idle was
        /// therefore told SetMoveForward(0) on every tick and could never start walking, so the AI's path never
        /// became movement and the stuck watchdog kept teleporting them home. Movement here is driven by the AI
        /// through the move helper (these classes are RootMotion false), and the animator's locomotion blend
        /// follows speedForward, so reporting "in motion" is what closes the loop. Players and every other
        /// controller are untouched.
        /// </summary>
        [HarmonyPatch(typeof(LegacyAvatarController), nameof(LegacyAvatarController.IsAnimationWithMotionRunning))]
        public static class Patch_MotionGate
        {
            [HarmonyPrefix]
            public static bool Prefix(LegacyAvatarController __instance, ref bool __result)
            {
                try
                {
                    if (!(__instance is AvatarSDCSController)) return true;
                    EntityAlive e = __instance.entity;
                    if (e == null || e is EntityPlayer) return true;
                    __result = true;
                    return false;
                }
                catch (Exception ex)
                {
                    ScLog.Once("sdcs-motiongate", ex);
                    return true;
                }
            }
        }

        /// <summary>
        /// Makes a survivor walk the path instead of orbiting it. An entity that does not use root motion is
        /// moved by EntityMoveHelper in two writes per tick: MakeMotionMoveToward puts the path's absolute
        /// world direction into moveDirection (EntityAlive.cs:4147-4169) and SetMoveForwardWithModifiers then
        /// overwrites it with a direction relative to the body's facing, which Entity.Move rotates by
        /// transform.forward. The AI's bearing therefore never reached her feet: she could only travel along
        /// her nose, so her velocity was chained to her turn rate, and SeekYaw's floor of 20 deg/s near
        /// alignment (Entity.cs:1602-1607) sets a minimum turn radius of roughly 2.87 * speed metres - a 16 m
        /// circle at the 5.7 m/s that was measured, and still 4 m after the speed was halved, which is exactly
        /// why halving it did not stop the circling. Every zombie and pack NPC escapes this because their
        /// motion comes from root motion, which takes the other branch. The direction is put back after the
        /// original has run; the facing, the speed scale and the climb flag it works out are all left alone, so
        /// the body still turns at its own rate while the feet go where the path says.
        /// </summary>
        [HarmonyPatch(typeof(EntityAlive), nameof(EntityAlive.SetMoveForwardWithModifiers))]
        public static class Patch_AbsoluteMove
        {
            [HarmonyPrefix]
            public static void Prefix(EntityAlive __instance, out Vector3 __state)
            {
                __state = Vector3.zero;
                try
                {
                    if (__instance == null || !__instance.isMoveDirAbsolute || __instance.RootMotion) return;
                    if (__instance is EntityPlayer || !(__instance.emodel is EModelSDCS)) return;
                    __state = __instance.moveDirection;
                }
                catch (Exception ex)
                {
                    ScLog.Once("sdcs-absmove", ex);
                }
            }

            [HarmonyPostfix]
            public static void Postfix(EntityAlive __instance, Vector3 __state)
            {
                if (__state.x == 0f && __state.z == 0f) return;   // nothing was captured: leave vanilla alone
                __instance.moveDirection.x = __state.x;
                __instance.moveDirection.z = __state.z;
                __instance.isMoveDirAbsolute = true;
            }
        }

        /// <summary>
        /// The jump animation. LegacyAvatarController.StartAnimationJump fires the trigger named "JumpStart",
        /// which is what the zombie and NPC animators listen for; the player animator this rig runs on has no
        /// such parameter - AvatarMultiBodyController jumps the same body with "JumpTrigger"
        /// (AvatarController.cs:667 vs :693) - so the trigger went nowhere and a survivor hopped blocks in her
        /// idle pose. Landing already matches: both animators use "JumpLand". Fired here as well as the
        /// original, so nothing is taken away from any other controller.
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
                    if (e == null || e is EntityPlayer) return;
                    __instance._setTrigger(AvatarController.jumpTriggerHash);
                }
                catch (Exception ex)
                {
                    ScLog.Once("sdcs-jumpanim", ex);
                }
            }
        }

        /// <summary>
        /// Stops a knocked-down survivor throwing once per tick. Entity.PhysicsSetHeight writes straight
        /// through physicsCapsuleCollider with no null check; that capsule is only set up when the model has a
        /// child tagged LargeEntityBlocker (Entity.PhysicsInit), which comes from the pack mesh these classes
        /// drop in favour of a player body, so ours have none. SCore's workaround for NPCs stuck in a crouch
        /// calls SetHeight on every tick an NPC is in a Getup or Prone stun (EntityAliveSDX.OnUpdateLive), so
        /// knocking a survivor down filled the log with caught null references - 80 of them in one playtest -
        /// and buried everything else. Skipped rather than faked: with no blocker capsule there is nothing to
        /// resize, and the character-controller half of SetHeight has already run. The one field the original
        /// writes before it throws is kept. Any entity without a blocker is covered, ours or not, because for
        /// all of them the original can only throw.
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
        /// Lets a knocked-down survivor get up like a player instead of floating. EModelBase.StartRagdoll decides
        /// once whether the body is a player's (entity is EntityPlayer) and every later step keys on that: the
        /// knockdown pose and the get-up clip are cross-faded on layer 5 for a player and layer 0 otherwise.
        /// Our survivors run the player animator without being EntityPlayers, so they asked for "Knockdown -
        /// Chest" and "GetUpChest" on the player rig's layer 0 (StandingIdleTurn), which has neither; a
        /// cross-fade to a missing state is dropped in silence, so the body skipped the knockdown and the
        /// get-up and blended straight into a standing idle. That pose lifts the pelvis a metre above the model
        /// root at once, while FrameUpdateRagdoll is still pinning the entity to the pelvis rigidbody until the
        /// StandCollide step - which dragged them up into the air and let them drop only when it let go.
        /// The same flag also switches a non-player's Physics object off for the ragdoll and back on at Stand,
        /// and a player's never, so correcting it after StartRagdoll has run means undoing what that method
        /// already did on the non-player branch: the Physics object goes back on and the stun is halved, as
        /// it is for a player. Only a fresh start is touched, and never a corpse, which does not get up.
        /// </summary>
        [HarmonyPatch(typeof(EModelBase), nameof(EModelBase.StartRagdoll), new[] { typeof(float) })]
        public static class Patch_RagdollAsPlayer
        {
            /// <summary>Runtime switch, so the dev core can compare the two recoveries in one session.</summary>
            public static bool Enabled = true;

            [HarmonyPostfix]
            public static void Postfix(EModelBase __instance, bool __result)
            {
                try
                {
                    if (!Enabled || !__result || __instance == null) return;
                    if (__instance.ragdollIsPlayer) return;                       // a player, or already corrected
                    if (__instance.ragdollState != EModelBase.ERagdollState.On) return;
                    if (!(__instance is EModelSDCS)) return;
                    EntityAlive e = __instance.entity as EntityAlive;
                    if (e == null || e is EntityPlayer || e.IsDead()) return;
                    __instance.ragdollIsPlayer = true;
                    if (e.PhysicsTransform != null) e.PhysicsTransform.gameObject.SetActive(true);
                    __instance.ragdollDuration *= 0.5f;
                }
                catch (Exception ex)
                {
                    ScLog.Once("sdcs-ragdoll", ex);
                }
            }
        }

        /// <summary>
        /// Keeps a survivor on the ground while they get up. EModelBase.FrameUpdateRagdoll pins a non-player's
        /// position to the pelvis rigidbody in every ragdoll state except the last two. That is right while the
        /// body is a physics ragdoll, but once the ragdoll ends the rigidbody rides the animated pelvis, and the
        /// get-up clip lifts that pelvis above the model root: the entity follows the pelvis up, which lifts the
        /// root, which lifts the pelvis again. Measured on a survivor: 40.3 to 47.5 m in 1.1 s, then straight back
        /// to 40.0 the instant the pinning stopped at StandCollide - the "floats high and comes back down".
        /// By then BlendRagdoll has already put the entity on the ground under the pelvis (PhysicsResume), so the
        /// pin write is undone from BlendOutGround on. Nothing else in those states moves the entity. Verified
        /// live: the position held at the floor through the whole get-up.
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
                    if (!Enabled || !(__instance is EModelSDCS)) return;
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
        /// Stops a survivor from spawning invisible. EntityAlive.InitPostCommon hides and fades to 0 every entity
        /// whose flags include AIHearing (any of Zombie, Animal or Bandit) so that it can fade in, and the fade in
        /// is EntityAlive.VisiblityCheck raising renderFadeTarget within 90 m. These classes inherit
        /// EntityFlags "bandit,edible" from the NPC framework's template, so they are faded out - but a survivor is
        /// an EntityNPC (EntityAliveSDX derives from EntityTrader), and EntityNPC.VisiblityCheck only toggles
        /// SetVisible by distance and never touches the fade. The framework's own NPC models are not affected
        /// because EModelBase.SetFade only writes renderers on a Game/Character shader with a _Fade property,
        /// which theirs are not and the player body's are. Measured on a fresh survivor standing 1.6 m away:
        /// renderFade 0, target 0, every body renderer carrying _Fade 0 - fully transparent, while culling
        /// reported all of them visible. A body rebuilt later (gear sync) got fresh renderers and showed up,
        /// which is why it looked intermittent. Raiders are EntityNPCBandit, not EntityNPC, and fade in normally.
        /// </summary>
        [HarmonyPatch(typeof(EntityAlive), nameof(EntityAlive.InitPostCommon))]
        public static class Patch_NoFadeOut
        {
            [HarmonyPostfix]
            public static void Postfix(EntityAlive __instance)
            {
                try
                {
                    if (!(__instance is EntityNPC) || !(__instance.emodel is EModelSDCS)) return;
                    __instance.renderFade = __instance.renderFadeMax;
                    __instance.renderFadeTarget = __instance.renderFadeMax;
                    __instance.emodel.SetFade(__instance.renderFadeMax);
                }
                catch (Exception ex)
                {
                    ScLog.Once("sdcs-fade", ex);
                }
            }
        }

        /// <summary>
        /// Stops the upper body vibrating at certain distances. EntityAlive.RotateTo(x, y, z, dYaw, dPitch) writes
        /// rotation.x = 0f - UpdateRotation(rotation.x, -atan2(y - position.y, dist), dPitch): it steps the current
        /// pitch toward the target and then negates the result, so every call starts from the flipped angle and the
        /// pitch ping-pongs. SCore's UAI turns an NPC to face you with it every frame (TurnToFaceEntity, aimed at
        /// your feet; the head looks at your head separately), so any height difference between your feet and
        /// theirs - a step, a slope - sets it off, and how visible it is depends on the angle, hence "an exact
        /// distance". Measured: rotation.x swinging 0.1 to 28.9 degrees with 40 reversals in 4 s. The player
        /// animator turns rotation.x into YLook for its additive torso-look layer, so only these bodies bob;
        /// pack NPC animators have no such layer. Same yaw as vanilla, and the pitch vanilla means (feet to the
        /// target, positive when it is above) without the flip. Verified live: pitch settles at about 0 on flat
        /// ground and holds, 0 reversals.
        ///
        /// In a fight the point is not the feet: SCore's attack task turns the NPC toward the target's BELLY every
        /// tick (UAITaskAttackTargetEntitySDX.Update, RotateTo(getBellyPosition(), 45, 30)). Measured from the
        /// NPC's feet a belly 0.9 m up at 1 m reads as ~40 degrees above, and the torso-look layer (YLook =
        /// -rotation.x / 90) arched the whole upper body back to look over what it was stabbing. So when the point
        /// belongs to the NPC's attack / revenge target the pitch is eye to eye, as vanilla's RotateTo(Entity) and
        /// the player's own camera measure it (level with a person, down at a crawler, up at something on a
        /// ledge); every other call keeps the feet-to-point pitch that settles at 0 while facing someone. The torso
        /// never bends more than MaxPitch. SCore's attack Start also passes (head.x, head.y, head.y) - the height in
        /// the z slot - which is put right before the yaw uses it.
        ///
        /// "Eye" is EyeY: these bodies stand in a 1 m collision box (height 1.0), so GetEyeHeight (height x 0.8)
        /// put their eyes at their belly and eye-to-eye still read ~35 degrees up at a player's 1.44 m eyes (measured
        /// live 2026-09-27: rotation.x +31.7, YLook -0.36, TorsoLook_Up2 layer at full weight while clubbing).
        /// </summary>
        [HarmonyPatch(typeof(EntityAlive), nameof(EntityAlive.RotateTo),
            new[] { typeof(float), typeof(float), typeof(float), typeof(float), typeof(float) })]
        public static class Patch_RotateToPitch
        {
            private const float MaxPitch = 45f;
            private const float TargetReach = 1.5f;   // how close (horizontally) the point must be to the target to be "at" it

            [HarmonyPrefix]
            public static bool Prefix(EntityAlive __instance, float _x, float _y, float _z, float _dYaw, float _dPitch)
            {
                try
                {
                    if (__instance == null || __instance is EntityPlayer || !(__instance.emodel is EModelSDCS)) return true;
                    EntityAlive target = CombatTarget(__instance);
                    if (target != null && Mathf.Abs(_z - _y) < 0.001f && Mathf.Abs(_x - target.position.x) < TargetReach
                        && Mathf.Abs(_z - target.position.z) > TargetReach)
                        _z = target.position.z;                // SCore's (head.x, head.y, head.y)
                    float dx = _x - __instance.position.x, dz = _z - __instance.position.z;
                    float dist = Mathf.Sqrt(dx * dx + dz * dz);
                    float yaw = 0f - (float)(Math.Atan2(dz, dx) * 180.0 / Math.PI) + 90f;
                    __instance.rotation.y = EntityAlive.UpdateRotation(__instance.rotation.y, yaw, _dYaw);
                    if (_dPitch > 0f)
                    {
                        float pitch;
                        float tx = target != null ? _x - target.position.x : 0f, tz = target != null ? _z - target.position.z : 0f;
                        if (target != null && tx * tx + tz * tz <= TargetReach * TargetReach)
                        {
                            // eye to eye, over the horizontal distance between the two bodies
                            float bx = target.position.x - __instance.position.x, bz = target.position.z - __instance.position.z;
                            float rise = EyeY(target) - EyeY(__instance);
                            pitch = (float)(Math.Atan2(rise, Math.Max(0.3f, Mathf.Sqrt(bx * bx + bz * bz))) * 180.0 / Math.PI);
                        }
                        else
                        {
                            pitch = (float)(Math.Atan2(_y - __instance.position.y, dist) * 180.0 / Math.PI);
                        }
                        pitch = Mathf.Clamp(pitch, -MaxPitch, MaxPitch);
                        __instance.rotation.x = EntityAlive.UpdateRotation(__instance.rotation.x, pitch, _dPitch);
                    }
                    return false;
                }
                catch (Exception ex)
                {
                    ScLog.Once("sdcs-rotateto", ex);
                    return true;
                }
            }

            /// <summary>The live attack target, else the revenge target (not itself, not dead, within 40 m).</summary>
            private static EntityAlive CombatTarget(EntityAlive self)
            {
                EntityAlive t = self.GetAttackTarget();
                if (!Usable(self, t)) t = self.GetRevengeTarget();
                return Usable(self, t) ? t : null;
            }

            private static bool Usable(EntityAlive self, EntityAlive t)
            {
                return t != null && t != self && !t.IsDead() && (t.position - self.position).sqrMagnitude < 40f * 40f;
            }
        }

        /// <summary>A player-body NPC (never a player).</summary>
        private static bool IsBodyNpc(Entity e)
        {
            return e != null && !(e is EntityPlayer) && e.emodel is EModelSDCS;
        }

        /// <summary>
        /// World eye height. A player-body NPC gets a player's (height 1.8 x 0.8 standing, x 0.5 crouching, as
        /// EntityAlive.GetEyeHeight gives the player) instead of 0.8 x its 1 m collision box. Fixed numbers, not the
        /// head bone: the head moves with the very torso lean this feeds.
        /// </summary>
        private static float EyeY(EntityAlive e)
        {
            if (IsBodyNpc(e)) return e.position.y + (e.IsCrouching ? 0.9f : 1.44f);
            return e.position.y + e.GetEyeHeight();
        }

        /// <summary>
        /// The other RotateTo, toward an entity (SCore's move-to-target and back-off tasks): vanilla measures it eye
        /// to eye too, with the same 0.8 m eyes on these bodies. Same yaw as vanilla, EyeY for both ends, and the
        /// torso lean clamped like the point overload.
        /// </summary>
        [HarmonyPatch(typeof(EntityAlive), nameof(EntityAlive.RotateTo), new[] { typeof(Entity), typeof(float), typeof(float) })]
        public static class Patch_RotateToEntity
        {
            [HarmonyPrefix]
            public static bool Prefix(EntityAlive __instance, Entity _otherEntity, float _dYaw, float _dPitch)
            {
                try
                {
                    if (!IsBodyNpc(__instance) || _otherEntity == null) return true;
                    float dx = _otherEntity.position.x - __instance.position.x, dz = _otherEntity.position.z - __instance.position.z;
                    EntityAlive other = _otherEntity as EntityAlive;
                    float rise = other != null ? EyeY(other) - EyeY(__instance)
                                               : (_otherEntity.boundingBox.min.y + _otherEntity.boundingBox.max.y) / 2f - EyeY(__instance);
                    float dist = Mathf.Sqrt(dx * dx + dz * dz);
                    float pitch = Mathf.Clamp((float)(Math.Atan2(rise, Math.Max(0.3f, dist)) * 180.0 / Math.PI), -45f, 45f);
                    float yaw = 0f - (float)(Math.Atan2(dz, dx) * 180.0 / Math.PI) + 90f;
                    __instance.rotation.x = EntityAlive.UpdateRotation(__instance.rotation.x, pitch, _dPitch);
                    __instance.rotation.y = EntityAlive.UpdateRotation(__instance.rotation.y, yaw, _dYaw);
                    return false;
                }
                catch (Exception ex)
                {
                    ScLog.Once("sdcs-rotateto-entity", ex);
                    return true;
                }
            }
        }

        /// <summary>
        /// One swing per hit. The player's melee state (AnimatorMeleeAttackState) times the swing from the held
        /// item's AttacksPerMinute - 55 for the NPC clubs, sledges and spears, a 1.1 s swing - but an NPC's melee
        /// action (SCore MeleeSDX, vanilla ItemActionMelee) hits every Delay seconds, 0.8 for all of them. Hits
        /// outran swings (the player saw three blood splashes in two swings: the attack trigger set mid-swing merges
        /// into one). For a player-body NPC the swing is sped up to fit its real hit interval, never slowed: a knife
        /// (120 per minute, 0.5 s) already fits. The pack's NPC damage stays as tuned.
        /// </summary>
        [HarmonyPatch(typeof(AnimatorMeleeAttackState), nameof(AnimatorMeleeAttackState.OnStateEnter),
            new[] { typeof(Animator), typeof(AnimatorStateInfo), typeof(int) })]
        public static class Patch_MeleeSwingSpeed
        {
            private const float Headroom = 0.92f;       // finish just before the next hit's trigger

            [HarmonyPostfix]
            public static void Postfix(AnimatorMeleeAttackState __instance, Animator animator, int layerIndex)
            {
                try
                {
                    if (__instance == null || animator == null || __instance.playingImpact) return;
                    EntityAlive e = __instance.entity;
                    if (!IsBodyNpc(e) || e.inventory == null || e.inventory.holdingItem == null) return;
                    int i = __instance.actionIndex;
                    ItemAction[] actions = e.inventory.holdingItem.Actions;
                    if (actions == null || i < 0 || i >= actions.Length) return;
                    ItemAction act = actions[i];
                    if (!(act is ItemActionMelee) || act is ItemActionDynamicMelee || act.Delay <= 0.05f) return;
                    AnimatorClipInfo[] clips = animator.GetNextAnimatorClipInfo(layerIndex);
                    if (clips.Length == 0) clips = animator.GetCurrentAnimatorClipInfo(layerIndex);
                    if (clips.Length == 0 || clips[0].clip == null) return;
                    float speed = clips[0].clip.length / (act.Delay * Headroom);
                    if (speed <= __instance.originalMeleeAttackSpeed) return;
                    __instance.originalMeleeAttackSpeed = speed;   // what the state restores after an impact / on exit
                    animator.SetFloat("MeleeAttackSpeed", speed);
                }
                catch (Exception ex)
                {
                    ScLog.Once("sdcs-swing-speed", ex);
                }
            }
        }

        [HarmonyPatch(typeof(EModelSDCS), "OnDestroy")]
        public static class Patch_OnDestroy
        {
            [HarmonyPostfix]
            public static void Postfix(EModelSDCS __instance)
            {
                if (__instance == null || __instance.playerEntity != null) return;
                Release(__instance);
            }
        }

        /// <summary>
        /// SCore's EntityAliveSDX.UpdateWeapon calls avatarController.SwitchModelAndView(meshName) with the class's
        /// Mesh property (or the class name); the player rig's only child is the generated body, so that lookup
        /// is null and the controller then deactivates the body and throws. Substitute the model's real name.
        /// </summary>
        [HarmonyPatch(typeof(AvatarSDCSController), nameof(AvatarSDCSController.SwitchModelAndView), new[] { typeof(string), typeof(bool), typeof(bool) })]
        public static class Patch_AvatarSwitch
        {
            [HarmonyPrefix]
            public static void Prefix(AvatarSDCSController __instance, ref string _modelName, bool _bFPV)
            {
                try
                {
                    if (_bFPV || __instance == null || __instance.modelTransform == null) return;
                    EModelSDCS sdcs = __instance.entity != null ? __instance.entity.emodel as EModelSDCS : null;
                    if (sdcs == null || sdcs.playerEntity != null || string.IsNullOrEmpty(sdcs.modelName)) return;
                    if (string.IsNullOrEmpty(_modelName) || __instance.modelTransform.Find(_modelName) == null) _modelName = sdcs.modelName;
                    // the controller grabs the first Animator it finds below the entity; make sure it is the rig's
                    Transform rig = __instance.modelTransform.Find(_modelName);
                    Animator rigAnim = rig != null ? (rig.GetComponent<Animator>() ?? rig.GetComponentInChildren<Animator>(true)) : null;
                    if (rigAnim != null && __instance.anim != rigAnim) __instance.SetAnimator(rigAnim);
                }
                catch (Exception e)
                {
                    ScLog.Once("sdcs-avatar-switch", e);
                }
            }
        }
    }
}
