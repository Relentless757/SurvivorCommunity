using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace SurvivorCommunity
{
    /// <summary>
    /// Every patch on a 0-XNPCCore type (NPCSdcsCharacters.dll), for the classes SC drives on XNPC's body (ScXnpc): the
    /// survivor classes the community owns and, since M10, SC's raider classes - plus the two on the body's SCore and
    /// vanilla classes that only these bodies need (X8, X9). The numbering is the migration design's (section 1.2).
    ///
    ///   X1  EModelSdcsNpc.createModel        prefix   the body is built from SC's look, not XNPC's preset archetype
    ///   X1b MinEventActionSdcsApplyLook      prefix   XNPC's look applier never reaches one of ours (belt and braces)
    ///   X2  EModelSdcsNpc.GenerateMeshes     postfix  eyes re-rooted on the head after every build (and hit colliders)
    ///   X3  AvatarSdcsNpcController.SdcsLevelTick                    prefix   no XNPC kill levels on members and stand-ins (D8)
    ///                                                                         nor on raiders (M10-D3); postfix: no regen
    ///   X4  AvatarSdcsNpcController.SdcsWearTick                     prefix   no no-wear buff: armor wears like SC's
    ///   X5  AvatarSdcsNpcController.SdcsFailoverTick / SdcsCloseRangeTick / SdcsTryWorldEntryRearm
    ///                                                                prefix   XNPC never swaps the hand during a medical use;
    ///                                                                         overwhelmed keeps the gun up close (ScArms);
    ///                                                                         melee-lead raiders keep melee (M10-D1)
    ///   X6  AvatarSdcsNpcController.SdcsChooseMeleeFallback          prefix   XNPC's melee choice is the pair's melee weapon
    ///   X7  AvatarSdcsNpcController.Update   postfix  SC's aim pose, written after XNPC's whole frame
    ///   X8  EntityAliveSDXV4.SetModelLayer   prefix   our bodies' own colliders are lifted out of their own attack ray
    ///   X9  EntityTrader.AllowActivationCommand      postfix  no talk prompt (and nothing on E) on a raider
    ///
    /// Every target is DECLARED on the type named (CLAUDE.md pattern #4: a base-class match would patch every player body),
    /// bound by name with a Prepare that turns the patch off when the method is gone, and listed in ModApi's
    /// ExpectedTargets, so a renamed private method shows up as a MISSING PATCH line instead of silence. The patch
    /// methods take XNPC's objects as their vanilla base types (EModelSDCS, AvatarSDCSController, MinEventActionBase), so
    /// nothing here but the target lookups names a type from XNPC's DLL. Every patch counts its hits (sc xnpc): a private
    /// method Mono inlined into its caller would never run its prefix, and only a counter shows that.
    /// </summary>
    public static class Patches_Xnpc
    {
        /// <summary>Hits per patch, for sc xnpc. Main thread only.</summary>
        private static readonly Dictionary<string, int> hits = new Dictionary<string, int>();
        /// <summary>X7 runs every frame for every body: a plain counter instead of a dictionary write.</summary>
        private static int updateHits;
        /// <summary>X9 runs every frame while a raider is looked at (once per command): a plain counter too.</summary>
        private static int talkRefused;

        public static void Reset()
        {
            hits.Clear();
            updateHits = 0;
            talkRefused = 0;
            Patch_ModelLayer.Reset();
        }

        internal static void Count(string what)
        {
            int n;
            hits.TryGetValue(what, out n);
            hits[what] = n + 1;
        }

        public static string Report()
        {
            var keys = new List<string>(hits.Keys);
            keys.Sort(StringComparer.Ordinal);
            var parts = new List<string>();
            foreach (string k in keys) parts.Add(k + "=" + hits[k]);
            parts.Add("x7-update=" + updateHits);
            parts.Add("x9-raider-talk-refused=" + talkRefused);
            return string.Join(", ", parts.ToArray());
        }

        // ------------------------------------------------------------------ targets ----

        private static readonly Type[] NoArgs = new Type[0];

        public static MethodBase CreateModelTarget()
        {
            return AccessTools.DeclaredMethod(typeof(EModelSdcsNpc), "createModel", new[] { typeof(World), typeof(EntityClass) });
        }

        public static MethodBase GenerateMeshesTarget()
        {
            return AccessTools.DeclaredMethod(typeof(EModelSdcsNpc), "GenerateMeshes", NoArgs);
        }

        public static MethodBase ApplyLookTarget()
        {
            return AccessTools.DeclaredMethod(typeof(MinEventActionSdcsApplyLook), "Execute", new[] { typeof(MinEventParams) });
        }

        public static MethodBase LevelTickTarget()
        {
            return AccessTools.DeclaredMethod(typeof(AvatarSdcsNpcController), "SdcsLevelTick", NoArgs);
        }

        public static MethodBase WearTickTarget()
        {
            return AccessTools.DeclaredMethod(typeof(AvatarSdcsNpcController), "SdcsWearTick", new[] { typeof(EntityBuffs) });
        }

        public static MethodBase FailoverTarget()
        {
            return AccessTools.DeclaredMethod(typeof(AvatarSdcsNpcController), "SdcsFailoverTick", NoArgs);
        }

        public static MethodBase CloseRangeTarget()
        {
            return AccessTools.DeclaredMethod(typeof(AvatarSdcsNpcController), "SdcsCloseRangeTick", NoArgs);
        }

        public static MethodBase RearmTarget()
        {
            return AccessTools.DeclaredMethod(typeof(AvatarSdcsNpcController), "SdcsTryWorldEntryRearm", new[] { typeof(ItemValue) });
        }

        public static MethodBase MeleeFallbackTarget()
        {
            return AccessTools.DeclaredMethod(typeof(AvatarSdcsNpcController), "SdcsChooseMeleeFallback", new[] { typeof(EntityAlive) });
        }

        public static MethodBase UpdateTarget()
        {
            return AccessTools.DeclaredMethod(typeof(AvatarSdcsNpcController), "Update", NoArgs);
        }

        /// <summary>X9: vanilla's virtual on EntityTrader; EntityAliveSDXV4 does not override it (SCore 3.2.36).</summary>
        public static MethodBase TalkPromptTarget()
        {
            return AccessTools.DeclaredMethod(typeof(EntityTrader), "AllowActivationCommand", new[] { typeof(ReadOnlySpan<char>), typeof(EntityPlayerLocal) });
        }

        /// <summary>
        /// Resolves a target for a Prepare. The lookup runs in its own method so that a missing NPCSdcsCharacters.dll (the
        /// type cannot load) surfaces here as a caught exception and an inert patch, never as a throw inside PatchAll,
        /// which would silently drop every patch class after this one (CLAUDE.md pattern #5).
        /// </summary>
        private static bool Bound(Func<MethodBase> target, string label)
        {
            try
            {
                if (target() != null) return true;
                ScLog.Warn("0-XNPCCore method " + label + " not found - that SurvivorCommunity patch is off (0-XNPCCore update?)");
                return false;
            }
            catch (Exception e)
            {
                ScLog.Once("xnpc-bind-" + label, e);
                return false;
            }
        }

        // ------------------------------------------------------------------ X1: looks ----

        /// <summary>
        /// X1. Builds one of our bodies from SC's look. EModelSdcsNpc.Init loads the class's preset archetype ("Clark",
        /// "Anne") and then calls createModel, the single place the model is built (its first GenerateMeshes); after it,
        /// Init subscribes the equipment's OnChanged and creates the avatar controller. So swapping the archetype and
        /// staging the armor here, in front of createModel, gives a first build that already wears SC's face and clothes,
        /// and the raw slot writes trigger no rebuild. Every later rebuild (XNPC's SwitchModelAndView prefix, its
        /// equipment handler, ScLooks) builds from the same archetype field, which is ours from here on.
        ///
        /// The look comes from ScLooks.ForEntity: a pending spawn look, else the registry's (member or prospect), else
        /// this session's, else a fresh roll (a raider's clothes from RaiderGearSets). Its sex follows the class: the rig, the
        /// animator, the voice and the ragdoll ("player_" + Sex + "Ragdoll") are the class's, so a record that disagrees is
        /// corrected the way ScLooks.Apply corrects one on a live body.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_CreateModel
        {
            [HarmonyPrepare]
            public static bool Prepare() { return Bound(CreateModelTarget, "EModelSdcsNpc.createModel"); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return CreateModelTarget(); }

            [HarmonyPrefix]
            public static void Prefix(EModelSDCS __instance, EntityClass _ec)
            {
                try
                {
                    Entity e = __instance != null ? __instance.entity : null;
                    if (e == null || e is EntityPlayer || !(e is EntityAliveSDXV4) || !ScXnpc.IsManagedClass(e.entityClass)) return;
                    Count("x1-look");
                    Look look = ScLooks.ForEntity(e);
                    // the class's IsMale, read the way EntityAlive.CopyPropertiesFromEntityClass reads it (male when absent)
                    bool male = look.Male;
                    if (_ec != null && _ec.Properties != null) { male = true; _ec.Properties.ParseBool(EntityClass.PropIsMale, ref male); }
                    if (look.Male != male)
                    {
                        look.Male = male;
                        look.Validated = false;
                        ScLooks.Validate(look);
                    }
                    __instance.archetype = look.ToArchetype();
                    ScLooks.StageGear(e as EntityAlive, look);
                    // Feet on the ground. These classes use the player's entity prefab (Prefab "Player"), whose Model
                    // transform sits at y -0.08 to cancel the player's CharacterController skin; an NPC moves with block
                    // collision and stands right on the block top, so the body sank 8 cm (measured 2026-10-01: a V4
                    // body's lowest foot bone 34.92 on ground 35.00; the player's 35.01; SC's raider, on an NPC prefab,
                    // 35.00). Nothing else ever moves this transform.
                    if (__instance.modelTransformParent != null)
                    {
                        UnityEngine.Vector3 lp = __instance.modelTransformParent.localPosition;
                        if (UnityEngine.Mathf.Abs(lp.y - ScConfig.XnpcModelY) > 0.0005f)
                            __instance.modelTransformParent.localPosition = new UnityEngine.Vector3(lp.x, ScConfig.XnpcModelY, lp.z);
                    }
                    ScLog.Verbose("xnpc body for " + (_ec != null ? _ec.entityClassName : "?") + " id " + e.entityId + ": " + ScLooks.Describe(look));
                }
                catch (Exception ex)
                {
                    // XNPC's preset archetype is still in place: the body builds with XNPC's look rather than not at all
                    ScLog.Once("xnpc-createmodel", ex);
                }
            }
        }

        /// <summary>
        /// X1b. Keeps XNPC's look applier off our bodies. Its only trigger is the class effect group "SDCS Look"
        /// (onSelfFirstSpawn, which fires on every world entry), and Config/entityclasses.xml removes that group from the
        /// managed classes; if anything ever puts it back, the applier would swap the archetype back to a preset, rewrite
        /// FirstName and dress the body in a full-protection set. The action is dropped for our bodies here and counted:
        /// "x1b-applier-blocked" should stay 0, and a count means the XML op stopped matching.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_ApplyLook
        {
            [HarmonyPrepare]
            public static bool Prepare() { return Bound(ApplyLookTarget, "MinEventActionSdcsApplyLook.Execute"); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return ApplyLookTarget(); }

            [HarmonyPrefix]
            public static void Prefix(MinEventActionTargetedBase __instance)
            {
                try
                {
                    if (__instance == null || __instance.targets == null) return;
                    // the list is rebuilt by CanExecute before every Execute, so trimming it here touches nothing else
                    int removed = __instance.targets.RemoveAll(t => ScXnpc.IsBody(t));
                    for (int i = 0; i < removed; i++) Count("x1b-applier-blocked");
                }
                catch (Exception ex)
                {
                    ScLog.Once("xnpc-applylook", ex);
                }
            }
        }

        /// <summary>
        /// X2. The eyes after every build of one of our bodies. XNPC's mesh build makes fresh, untagged eye renderers, and
        /// the base EModelBase.SwitchModelAndView its own SwitchModelAndView prefix then runs re-roots every untagged
        /// renderer onto the pelvis, which slides the eyes out of the face; Patches_Sdcs.FixEyeRenderers tags them and
        /// roots them at the head. The hit colliders of the new build are put back on the hittable layer too
        /// (Patches_Sdcs.FixColliderLayers) while XnpcBodyColliderLayers is on.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_GenerateMeshes
        {
            [HarmonyPrepare]
            public static bool Prepare() { return Bound(GenerateMeshesTarget, "EModelSdcsNpc.GenerateMeshes"); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return GenerateMeshesTarget(); }

            [HarmonyPostfix]
            public static void Postfix(EModelSDCS __instance)
            {
                try
                {
                    if (!ScXnpc.IsBodyModel(__instance)) return;
                    Count("x2-eyes");
                    Patches_Sdcs.FixEyeRenderers(__instance);
                    if (ScConfig.XnpcBodyColliderLayers) Patches_Sdcs.FixColliderLayers(__instance.entity as EntityAlive);
                }
                catch (Exception ex)
                {
                    ScLog.Once("xnpc-generatemeshes", ex);
                }
            }
        }

        // ------------------------------------------------------------------ X3: kill levels ----

        /// <summary>
        /// X3. No XNPC kill levels on a community member or stand-in (decision D8: they grow through SC's skills only;
        /// strangers keep XNPC's levels). XNPC's level tick runs at 1 Hz for every body and is the only writer of the level
        /// cvars, the level bonus buff (health, damage, spread and a 0.2 HP/s regeneration) and - once the NPC has a leader
        /// - a Progression that XNPC's kill-XP handler then feeds. For ours it is replaced by StripLevels, so whatever an
        /// earlier tick or the time before joining left is taken off too. Skipping it also skips the wear tick it starts
        /// with (X4's job is then StripLevels' no-wear removal). A member who leaves loses the community cvars, and the tick
        /// resumes with a day-based level. The method is ~90 lines: Mono does not inline it, and the counter proves it.
        ///
        /// Raiders (M10-D3, "raiders won't persist"): no kill levels either while RaiderXnpcLevels is off (the default) -
        /// the same strip. With it on, XNPC's tick runs and the postfix zeroes the levels' regeneration cvar (the bonus buff
        /// reads it live) unless RaiderLevelRegen, since a raider heals by its items only (ScMedic).
        /// </summary>
        [HarmonyPatch]
        public static class Patch_LevelTick
        {
            [HarmonyPrepare]
            public static bool Prepare() { return Bound(LevelTickTarget, "AvatarSdcsNpcController.SdcsLevelTick"); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return LevelTickTarget(); }

            [HarmonyPrefix]
            public static bool Prefix(AvatarSDCSController __instance)
            {
                try
                {
                    // any community body this controller drives, not only the six managed classes: D8 is about members,
                    // and a hired XNPC companion of another player-body class may have joined (D9)
                    EntityAlive e = __instance != null ? __instance.entity : null;
                    if (e == null || !ScXnpc.HasXnpcLevels(e)) return true;
                    bool community = ScXnpc.IsCommunity(e);
                    if (!community && !(ScXnpc.IsRaider(e) && !ScConfig.RaiderXnpcLevels)) return true;
                    ScXnpc.StripLevels(e);
                    Count(community ? "x3-levels-off" : "x3-raider-levels-off");
                    return false;
                }
                catch (Exception ex)
                {
                    ScLog.Once("xnpc-leveltick", ex);
                    return true;
                }
            }

            /// <summary>A raider that keeps XNPC's levels (RaiderXnpcLevels) loses their regeneration (RaiderLevelRegen off).</summary>
            [HarmonyPostfix]
            public static void Postfix(AvatarSDCSController __instance)
            {
                try
                {
                    if (!ScConfig.RaiderXnpcLevels || ScConfig.RaiderLevelRegen) return;
                    EntityAlive e = __instance != null ? __instance.entity : null;
                    if (e == null || e.Buffs == null || !ScXnpc.IsRaider(e) || ScXnpc.IsCommunity(e)) return;
                    if (e.Buffs.GetCustomVar(ScXnpc.LevelRegenCvar) == 0f) return;
                    e.Buffs.SetCustomVar(ScXnpc.LevelRegenCvar, 0f);
                    Count("x3-raider-regen-off");
                }
                catch (Exception ex)
                {
                    ScLog.Once("xnpc-leveltick-post", ex);
                }
            }
        }

        // ------------------------------------------------------------------ X4: wear ----

        /// <summary>
        /// X4. No no-wear buff on our bodies (decision D2: SC's half protection on worn vanilla armor that wears down).
        /// XNPC's wear tick runs at 1 Hz from its level tick and adds SdcsNoWear to every body while its NoWear setting is
        /// on; for ours the buff is taken off instead and the tick skipped. The method is ~15 lines with branches, too big
        /// for Mono to inline, and the counter proves the hook runs.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_WearTick
        {
            [HarmonyPrepare]
            public static bool Prepare() { return Bound(WearTickTarget, "AvatarSdcsNpcController.SdcsWearTick"); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return WearTickTarget(); }

            [HarmonyPrefix]
            public static bool Prefix(AvatarSDCSController __instance, EntityBuffs buffs)
            {
                try
                {
                    if (__instance == null || !ScXnpc.IsBody(__instance.entity)) return true;
                    if (buffs != null && buffs.HasBuff("SdcsNoWear")) buffs.RemoveBuff("SdcsNoWear");
                    Count("x4-wear");
                    return false;
                }
                catch (Exception ex)
                {
                    ScLog.Once("xnpc-weartick", ex);
                    return true;
                }
            }
        }

        // ------------------------------------------------------------------ X5: hands off during a use ----

        /// <summary>
        /// X5. XNPC never swaps what one of our bodies holds while it is using a medical item (ScMedic). XNPC's weapon state
        /// machine would otherwise take the bandage out of the hand mid-use: its dry-gun failover restores the remembered
        /// gun as soon as there is reserve ammo and the held item is not dry-capable (a bandage is not), its close-range
        /// swap's exit restores the remembered weapon, and the world-entry re-arm swaps to the best loaded gun. All three
        /// are skipped for the 1-3 s a use lasts; ScMedic puts the weapon back itself when the use ends.
        /// The close-range tick also carries the overwhelmed rule (migration M7.5, ScArms.CloseRange) and, before it, the
        /// melee-lead raiders' switch to their gun for a target out of reach (M10-D1, ScArms.RaiderLead); the re-arm is held
        /// off for a melee-lead raider (ScArms.WorldEntryRearm). One prefix, so the medical-use skip always comes first and
        /// the order of the rules never depends on how Harmony chains prefixes.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_NoSwapWhileUsing
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                return Bound(FailoverTarget, "AvatarSdcsNpcController.SdcsFailoverTick")
                       | Bound(CloseRangeTarget, "AvatarSdcsNpcController.SdcsCloseRangeTick")
                       | Bound(RearmTarget, "AvatarSdcsNpcController.SdcsTryWorldEntryRearm");
            }

            [HarmonyTargetMethods]
            public static IEnumerable<MethodBase> TargetMethods()
            {
                var list = new List<MethodBase>();
                foreach (MethodBase m in new[] { FailoverTarget(), CloseRangeTarget(), RearmTarget() })
                    if (m != null) list.Add(m);
                return list;
            }

            [HarmonyPrefix]
            public static bool Prefix(AvatarSDCSController __instance, MethodBase __originalMethod)
            {
                try
                {
                    EntityAlive e = __instance != null ? __instance.entity : null;
                    if (e == null) return true;
                    if (ScMedic.IsUsing(e.entityId) && ScXnpc.IsBody(e))
                    {
                        Count("x5-" + (__originalMethod != null ? __originalMethod.Name : "?"));
                        return false;
                    }
                    string name = __originalMethod != null ? __originalMethod.Name : null;
                    // a melee-lead raider keeps the melee weapon it spawns with (M10-D1)
                    if (name == "SdcsTryWorldEntryRearm") return ScArms.WorldEntryRearm(e);
                    if (name == "SdcsCloseRangeTick")
                    {
                        // a melee-lead raider takes its gun for a target out of reach (then this round is done)
                        if (ScArms.RaiderLead(__instance, e)) return false;
                        // overwhelmed: keep (or go back to) the gun instead of the melee swap up close
                        return ScArms.CloseRange(__instance, e);
                    }
                    return true;
                }
                catch (Exception ex)
                {
                    ScLog.Once("xnpc-noswap", ex);
                    return true;
                }
            }
        }

        // ------------------------------------------------------------------ X6: the melee weapon ----

        /// <summary>
        /// X6. The melee weapon XNPC reaches for is the pair's (migration M7, decision D4). XNPC's close-range swap and its
        /// dry-gun failover both ask SdcsChooseMeleeFallback: with an Owner (a follower) it returns the last melee weapon
        /// the NPC held (the SdcsLastMelee cvar, which a voice "use X" can leave on anything), otherwise the first melee
        /// item on itemsOnEnterGame - the pair's melee weapon, since ScLoadout writes that list. For one of our bodies the
        /// answer is always the pair's melee weapon while SCore's FindWeapon still accepts it; when it does not (or the
        /// body has no pair yet) XNPC's own choice runs. The method has loops and is far too big to be inlined, and the
        /// counters show it runs.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_MeleeFallback
        {
            [HarmonyPrepare]
            public static bool Prepare() { return Bound(MeleeFallbackTarget, "AvatarSdcsNpcController.SdcsChooseMeleeFallback"); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return MeleeFallbackTarget(); }

            /// <summary>__0 is the entity argument (bound by position: the controller also has an "entity" field).</summary>
            [HarmonyPrefix]
            public static bool Prefix(EntityAlive __0, ref string __result)
            {
                try
                {
                    if (__0 == null || !ScXnpc.IsBody(__0)) return true;
                    string melee = ScLoadout.XnpcMelee(__0);
                    if (melee == null) { Count("x6-xnpc-choice"); return true; }
                    Count("x6-pair-melee");
                    __result = melee;
                    return false;
                }
                catch (Exception ex)
                {
                    ScLog.Once("xnpc-meleefallback", ex);
                    return true;
                }
            }
        }

        // ------------------------------------------------------------------ X7: aim pose ----

        /// <summary>
        /// X7. SC's visible aiming (decision D12) on our bodies. XNPC's controller overrides Update, drives locomotion,
        /// carry and the look pitch itself and never sets IsAiming; ScAim.Drive runs after the whole of it, so the aim pose
        /// is the last thing written in the frame.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_Update
        {
            [HarmonyPrepare]
            public static bool Prepare() { return Bound(UpdateTarget, "AvatarSdcsNpcController.Update"); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return UpdateTarget(); }

            [HarmonyPostfix]
            public static void Postfix(AvatarSDCSController __instance)
            {
                try
                {
                    if (__instance == null || __instance.anim == null) return;
                    EntityAlive e = __instance.entity;
                    if (!ScXnpc.IsBody(e)) return;
                    updateHits++;
                    ScAim.Drive(__instance, e);
                }
                catch (Exception ex)
                {
                    ScLog.Once("xnpc-update", ex);
                }
            }
        }

        // ------------------------------------------------------------------ X8: no self-hits ----

        /// <summary>
        /// X8. Our bodies must not hit themselves. A melee swing (ItemActionDynamic.GetExecuteActionTarget) and a gun's shot
        /// hide the attacker from their own ray with SetModelLayer(2) and put it back afterwards, but EntityAliveSDXV4
        /// overrides SetModelLayer with an empty body. 0-XNPCCore's own survivors get away with it because their hit
        /// colliders stay on the no-collision layer; ours are on layer 0 so they can be hit at all (FixColliderLayers),
        /// so the ray met the attacker's own arm or hand first: a bat seen landing on a zombie did no damage (reported
        /// 2026-10-01). On SetModelLayer(2) the body's layer-0 colliders - and only those, never the renderers vanilla's
        /// version would flatten too - go to layer 2; the next SetModelLayer call (the restore) puts exactly those back.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_ModelLayer
        {
            private static readonly Dictionary<int, List<UnityEngine.Collider>> lifted = new Dictionary<int, List<UnityEngine.Collider>>();

            public static void Reset() { lifted.Clear(); }

            public static MethodBase Target()
            {
                return AccessTools.DeclaredMethod(typeof(EntityAliveSDXV4), "SetModelLayer", new[] { typeof(int), typeof(bool), typeof(string[]) });
            }

            [HarmonyPrepare]
            public static bool Prepare() { return Target() != null; }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return Target(); }

            [HarmonyPrefix]
            public static void Prefix(EntityAliveSDXV4 __instance, int _layerId)
            {
                try
                {
                    if (__instance == null || !ScXnpc.IsBody(__instance) || __instance.emodel == null) return;
                    int id = __instance.entityId;
                    List<UnityEngine.Collider> list;
                    if (_layerId == 2)
                    {
                        if (lifted.ContainsKey(id)) return;
                        UnityEngine.Transform mt = __instance.emodel.GetModelTransform();
                        if (mt == null) return;
                        list = new List<UnityEngine.Collider>();
                        foreach (UnityEngine.Collider c in mt.GetComponentsInChildren<UnityEngine.Collider>(true))
                        {
                            if (c == null || c.gameObject.layer != 0) continue;
                            c.gameObject.layer = 2;
                            list.Add(c);
                        }
                        lifted[id] = list;
                        Count("x8-self-hit-guard");
                    }
                    else if (lifted.TryGetValue(id, out list))
                    {
                        foreach (UnityEngine.Collider c in list) if (c != null && c.gameObject.layer == 2) c.gameObject.layer = 0;
                        lifted.Remove(id);
                    }
                }
                catch (Exception ex)
                {
                    ScLog.Once("xnpc-modellayer", ex);
                }
            }
        }

        // ------------------------------------------------------------------ X9: no talk prompt on raiders ----

        /// <summary>
        /// X9. A raider offers nothing to talk about (M10; the user's call, brought forward from round 3): looking at one
        /// showed "Press E to talk to Dregs the Raider", and E opened the radial to SCore's "sworn enemy" tooltip.
        /// Vanilla shows an entity's prompt and opens its radial whenever one of its activation commands is allowed
        /// (PlayerMoveController's focus check calls Entity.HasEnabledActivationCommands, which asks AllowActivationCommand
        /// for every command InitLocalActivationCommands registered; XUiC_Radial asks the same method). A raider is an
        /// EntityTrader - EntityAliveSDXV4 derives from it - and EntityTrader registers talk, trade and remove and allows
        /// talk and trade for anything alive with an NPCInfo (remove with the debug menu on). SCore's V4 only hides
        /// GetActivationCommands behind a "new" method that returns nothing for an enemy, and vanilla never calls that one
        /// (the vanilla method is not virtual), nor does V4 override AllowActivationCommand, InitLocalActivationCommands or
        /// GetActivationText (SCore 3.2.36, decompiled). So every command is refused here for a raider body: no prompt, no
        /// radial, nothing on E. Survivors, members, traders and 0-XNPCCore's own NPCs are untouched (IsRaider: a raider
        /// class on our body). A dead raider was already refused by vanilla (its loot is a dropped backpack).
        /// </summary>
        [HarmonyPatch]
        public static class Patch_NoTalkToRaiders
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                try
                {
                    if (TalkPromptTarget() != null) return true;
                    ScLog.Err("EntityTrader.AllowActivationCommand(ReadOnlySpan<char>, EntityPlayerLocal) not found (game update?) - raiders show the talk prompt again");
                }
                catch (Exception e)
                {
                    ScLog.Once("xnpc-bind-talkprompt", e);
                }
                return false;
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return TalkPromptTarget(); }

            [HarmonyPostfix]
            public static void Postfix(EntityTrader __instance, ref bool __result)
            {
                try
                {
                    if (!__result || !(__instance is EntityAliveSDXV4) || !ScXnpc.IsRaider(__instance)) return;
                    __result = false;
                    talkRefused++;
                }
                catch (Exception ex)
                {
                    ScLog.Once("xnpc-talkprompt", ex);
                }
            }
        }
    }
}
