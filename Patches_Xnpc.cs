using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace SurvivorCommunity
{
    /// <summary>
    /// Every patch on a 0-XNPCCore type (NPCSdcsCharacters.dll), for the classes SC drives on XNPC's body (ScXnpc): the
    /// survivor classes the community owns and, since M10, SC's raider classes - plus the ones on SCore and vanilla
    /// classes that only these bodies need (X8-X13). The numbering is the migration design's (section 1.2).
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
    ///   X7  AvatarSdcsNpcController.Update   postfix  SC's aim pose, written after XNPC's whole frame (and its carry pose kept)
    ///   X8  EntityAliveSDXV4.SetModelLayer   prefix   our bodies' own colliders are lifted out of their own attack ray
    ///   X9  EntityTrader.AllowActivationCommand      postfix  no talk prompt (and nothing on E) on a raider; no "Trade"
    ///                                                         on an NPC body that has no trader (it locked the game)
    ///   X10 AutoTurretFireController / MiniTurretFireController / MotionSensorController.shouldIgnoreTarget
    ///                                                postfix  player turrets shoot raiders as enemies, motion sensors
    ///                                                         trip on them (RaiderTurretsAsEnemies)
    ///   X11 EntityAliveSDXV4.HandleNavObject         prefix   a raider gets its class's map markers (clear-sleeper dots)
    ///   X12 EntityAliveSDXV4.UpdateWeapon(ItemValue, bool)   prefix + finalizer   the loose rounds in a gun SCore's swap
    ///                                                         writes over go back to the bag (M11)
    ///   X13 EntityAliveSDXV4.Write / Read                     postfixes  the gun in the hand, its rounds and its seated
    ///                                                         magazine are saved and loaded with the body (M11 review F1)
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
        /// <summary>X9's trade half: the frames a body with no trader was refused its "Trade" command.</summary>
        private static int tradeRefused;
        /// <summary>X10 runs every frame a turret looks at a raider: a plain counter of the times it was made a target.</summary>
        private static int turretTargets;
        /// <summary>The same for a motion sensor's scan.</summary>
        private static int sensorTargets;

        public static void Reset()
        {
            hits.Clear();
            updateHits = 0;
            talkRefused = 0;
            tradeRefused = 0;
            turretTargets = 0;
            sensorTargets = 0;
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
            parts.Add("x9-no-trader-trade-refused=" + tradeRefused);
            parts.Add("x10-turret-raider-target=" + turretTargets);
            parts.Add("x10-sensor-raider-target=" + sensorTargets);
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

        /// <summary>X10: the powered auto turret's target filter (vanilla; public in the shipped 3.2 assembly).</summary>
        public static MethodBase AutoTurretTarget()
        {
            return AccessTools.DeclaredMethod(typeof(AutoTurretFireController), "shouldIgnoreTarget", new[] { typeof(Entity) });
        }

        /// <summary>X10: the junk turret's and junk sledge's (EntityTurret's) target filter (vanilla).</summary>
        public static MethodBase MiniTurretTarget()
        {
            return AccessTools.DeclaredMethod(typeof(MiniTurretFireController), "shouldIgnoreTarget", new[] { typeof(Entity) });
        }

        /// <summary>X10: the motion sensor's target filter (vanilla; public in the shipped 3.2 assembly; no mod patches it).</summary>
        public static MethodBase MotionSensorTarget()
        {
            return AccessTools.DeclaredMethod(typeof(MotionSensorController), "shouldIgnoreTarget", new[] { typeof(Entity) });
        }

        /// <summary>X11: SCore's override of vanilla's Entity.HandleNavObject (SCore 3.2.36).</summary>
        public static MethodBase NavObjectTarget()
        {
            return AccessTools.DeclaredMethod(typeof(EntityAliveSDXV4), "HandleNavObject", NoArgs);
        }

        /// <summary>X12: SCore's weapon swap, the overload every other one ends in (SCore 3.2.36; no other mod patches it).</summary>
        public static MethodBase UpdateWeaponTarget()
        {
            return AccessTools.DeclaredMethod(typeof(EntityAliveSDXV4), "UpdateWeapon", new[] { typeof(ItemValue), typeof(bool) });
        }

        /// <summary>X13: SCore's V4 save (Write(BinaryWriter _bw, bool bNetworkWrite); ScMedic's mid-use prefix is on it too).</summary>
        public static MethodBase WriteTarget()
        {
            return AccessTools.DeclaredMethod(typeof(EntityAliveSDXV4), "Write", new[] { typeof(System.IO.BinaryWriter), typeof(bool) });
        }

        /// <summary>X13: SCore's V4 load (Read(byte _version, BinaryReader _br)).</summary>
        public static MethodBase ReadTarget()
        {
            return AccessTools.DeclaredMethod(typeof(EntityAliveSDXV4), "Read", new[] { typeof(byte), typeof(System.IO.BinaryReader) });
        }

        /// <summary>SCore's Harmony id (SphereIICoreInit): X10 runs after its turret postfixes, which decide an EntityNPC alone.</summary>
        private const string SCoreHarmonyId = "Harmony.SphereIICoreInit";

        /// <summary>
        /// Resolves a vanilla or SCore target for a Prepare like Bound, but fail-loud (an error line, not a warning): these
        /// are not 0-XNPCCore's private methods but public game and SCore API, so a miss means a game or SCore update.
        /// </summary>
        private static bool BoundLoud(Func<MethodBase> target, string label, string effect)
        {
            try
            {
                if (target() != null) return true;
                ScLog.Err(label + " not found (game or SCore update?) - " + effect);
            }
            catch (Exception e)
            {
                ScLog.Once("xnpc-bind-" + label, e);
            }
            return false;
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
        /// is the last thing written in the frame. Before it, ScAim.KeepCarry (the M11 fix 2) repairs the carry pose XNPC
        /// writes only on a change - on every machine, as XNPC's own write; Drive itself acts only on the owner's. Before
        /// both, ScRelax lowers or raises the arms and hides or shows the weapon of a body out of danger. Then, in
        /// its own try, Patches_Sdcs.GetUp holds IsMoving off while a knocked-down body plays its get-up clip (and traces it).
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
                    try
                    {
                        ScRelax.Tick(__instance, e);
                    }
                    catch (Exception ex)
                    {
                        ScLog.Once("xnpc-relax", ex);
                    }
                    ScAim.KeepCarry(__instance, e);
                    ScAim.Drive(__instance, e);
                }
                catch (Exception ex)
                {
                    ScLog.Once("xnpc-update", ex);
                }
                try
                {
                    EntityAlive e = __instance != null && __instance.anim != null ? __instance.entity : null;
                    if (ScXnpc.IsBody(e)) Patches_Sdcs.GetUp.Tick(__instance, e);
                }
                catch (Exception ex)
                {
                    ScLog.Once("xnpc-getup", ex);
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
        ///
        /// The trade half (2026-10-03, the user's T1: "Trade" on a member's radial made the UI vanish and left the player
        /// unable to do anything until a reload). Every SCore V4 body offers vanilla's "trade" while it is alive, but
        /// 0-XNPCCore's bodies carry NPCID npcSdcsTest, whose npc_info has no trader_id: NPCInfo.TraderID stays -1 and so
        /// does TraderData.TraderID (EntityTrader.PostInit copies it only when it is above 0). Choosing Trade runs V4's
        /// OnEntityActivated -> the lock on channel 0 -> EntityTrader.OnLockedLocal, which zooms the camera in (OverrideFOV
        /// 30) and asks for the channel 1 lock; the server's OnLockedServer then calls TraderManager.TraderInventoryRequested,
        /// whose first line is TraderInfo.traderInfoList[TraderID] - [-1] on a 256-entry array throws, after the lock was
        /// already taken and before any window opened or the zoom was undone (the throw is inside LockRequestServer's
        /// finally, so the lock response never comes either). So "trade" is refused for every SCore NPC body - an
        /// EntityAliveSDXV4, or the older EntityAliveSDX that 0-XNPCCore's legacy templates still use, which inherits the
        /// same AllowActivationCommand and lock path - whose TraderData names no existing trader (HasTrader): SC's
        /// survivors and members, a 0-XNPCCore companion that joined (D9) and XNPC's own NPCs, all of which can only hit
        /// that throw. An NPC with a real trader_id keeps it. "talk" is untouched, so the prompt, the radial's Talk and
        /// SC's recruit/member dialogs stay. (Voice Control's trade orders open SCore's inventory, not this path.)
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
                    ScLog.Err("EntityTrader.AllowActivationCommand(ReadOnlySpan<char>, EntityPlayerLocal) not found (game update?) - raiders show the talk prompt again, and Trade on a member locks the game");
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
            public static void Postfix(EntityTrader __instance, ReadOnlySpan<char> _commandName, EntityPlayerLocal _playerFocusing, ref bool __result)
            {
                try
                {
                    if (!__result) return;
                    if (__instance is EntityAliveSDXV4)
                    {
                        if (ScXnpc.IsRaider(__instance))
                        {
                            __result = false;
                            talkRefused++;
                            return;
                        }
                    }
                    else if (!(__instance is EntityAliveSDX)) return;
                    // a survivor this player hit holds a grudge (ScTrust): nothing to say, nothing on the radial
                    if (ScTrust.IsHostileTo(__instance, _playerFocusing))
                    {
                        __result = false;
                        talkRefused++;
                        return;
                    }
                    if (__instance.CommandIs(_commandName, EntityTrader.CommandTrade) && !HasTrader(__instance))
                    {
                        __result = false;
                        tradeRefused++;
                    }
                }
                catch (Exception ex)
                {
                    ScLog.Once("xnpc-talkprompt", ex);
                }
            }

            /// <summary>Does this NPC's TraderData name a trader that exists, i.e. would vanilla's trade lock find one?</summary>
            private static bool HasTrader(EntityTrader npc)
            {
                TraderData data = npc.TraderData;
                TraderInfo[] list = TraderInfo.traderInfoList;
                if (data == null || list == null) return false;
                int id = data.TraderID;
                return id >= 0 && id < list.Length && list[id] != null;
            }
        }

        // ------------------------------------------------------------------ X10: turrets shoot raiders ----

        /// <summary>
        /// X10. Player turrets treat a raider as the enemy it is (RaiderTurretsAsEnemies, M10-D8). The old raiders were
        /// EntityEnemy, so the powered auto turret shot them under "target zombies" and the junk turret under "target
        /// enemies". A raider on this body is an EntityNPC - an EntityTrader even - which vanilla's auto turret shoots only
        /// with "target strangers" on and its junk turret never; SCore's postfixes on both filters then decide every
        /// EntityNPC alone: "target strangers" on and the turret's owner its enemy (Harmony\MiniTurret\shouldIgnoreTarget.cs).
        /// These postfixes run after SCore's (Priority.Last, after its Harmony id) and only ever turn "ignore" into "shoot",
        /// for a raider body (IsRaider: SC's raider class on XNPC's body - never a member, a trader or one of 0-XNPCCore's
        /// own NPCs), or a survivor hostile to a player (ScTrust.IsHostileToAny), that is alive, with the turret's zombie
        /// or enemy switch on, inside the field of view
        /// vanilla tests first, and - the junk turret's filter ends with it - within its reach and line of fire
        /// (canHitEntity). SCore's strangers rule stays, so either switch lets the turret fire; with the key off only
        /// SCore's rule is left. Pure and stateless: the same answer on the server and on a client.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_AutoTurretRaiders
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                return BoundLoud(AutoTurretTarget, "AutoTurretFireController.shouldIgnoreTarget(Entity)", "the auto turret shoots raiders only with 'target strangers' on");
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return AutoTurretTarget(); }

            [HarmonyPostfix]
            [HarmonyPriority(Priority.Last)]
            [HarmonyAfter(SCoreHarmonyId)]
            public static void Postfix(AutoTurretFireController __instance, Entity _target, ref bool __result)
            {
                try
                {
                    if (!__result || !ScConfig.RaiderTurretsAsEnemies || !(_target is EntityAliveSDXV4) || !(ScXnpc.IsRaider(_target) || ScTrust.IsHostileToAny(_target))) return;
                    TileEntityPoweredRangedTrap te = __instance != null ? __instance.TileEntity : null;
                    if (te == null || !te.TargetZombies || !_target.IsAlive() || __instance.Cone == null) return;
                    // vanilla's own first test, unchanged: a positive dot product with the cone's forward is out of view
                    if (UnityEngine.Vector3.Dot(_target.position - te.ToWorldPos().ToVector3(), __instance.Cone.forward) > 0f) return;
                    __result = false;
                    turretTargets++;
                }
                catch (Exception ex)
                {
                    ScLog.Once("xnpc-autoturret", ex);
                }
            }
        }

        /// <summary>
        /// X10 for the junk turret and the junk sledge (EntityTurret; JunkSledgeFireController inherits this filter): their
        /// "target enemies" switch, as for the old EntityEnemy raiders.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_MiniTurretRaiders
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                return BoundLoud(MiniTurretTarget, "MiniTurretFireController.shouldIgnoreTarget(Entity)", "the junk turret shoots raiders only with 'target strangers' on");
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return MiniTurretTarget(); }

            [HarmonyPostfix]
            [HarmonyPriority(Priority.Last)]
            [HarmonyAfter(SCoreHarmonyId)]
            public static void Postfix(MiniTurretFireController __instance, Entity _target, ref bool __result)
            {
                try
                {
                    if (!__result || !ScConfig.RaiderTurretsAsEnemies || !(_target is EntityAliveSDXV4) || !(ScXnpc.IsRaider(_target) || ScTrust.IsHostileToAny(_target))) return;
                    EntityTurret turret = __instance != null ? __instance.entityTurret : null;
                    if (turret == null || !turret.TargetEnemies || !_target.IsAlive() || _target.entityId == turret.entityId) return;
                    // vanilla's field of view (the cone's forward, else the controller's)...
                    UnityEngine.Vector3 forward = __instance.Cone != null ? __instance.Cone.forward : __instance.transform.forward;
                    if (UnityEngine.Vector3.Dot(_target.position - turret.position, forward) > 0f) return;
                    // ...and the reach and line-of-fire test its filter ends with
                    float yaw = 0f, pitch = 0f;
                    UnityEngine.Vector3 aim;
                    if (!__instance.canHitEntity(_target, ref yaw, ref pitch, out aim)) return;
                    __result = false;
                    turretTargets++;
                }
                catch (Exception ex)
                {
                    ScLog.Once("xnpc-miniturret", ex);
                }
            }
        }

        /// <summary>
        /// X10 for the motion sensor (M10 round 3 review, F4): a sensor with "target zombies" on trips on a live raider, as
        /// it did on the old EntityEnemy raiders; vanilla trips on an EntityNPC only with "target strangers" on. No mod
        /// patches this filter (SCore included); the same ordering attributes keep this postfix last should one appear.
        /// Vanilla's own tests are kept: alive and inside the cone's field of view (its first test); the scan that calls
        /// the filter then tracks and ray-tests the target itself (MotionSensorController.hasTarget).
        /// </summary>
        [HarmonyPatch]
        public static class Patch_MotionSensorRaiders
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                return BoundLoud(MotionSensorTarget, "MotionSensorController.shouldIgnoreTarget(Entity)", "motion sensors trip on raiders only with 'target strangers' on");
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return MotionSensorTarget(); }

            [HarmonyPostfix]
            [HarmonyPriority(Priority.Last)]
            [HarmonyAfter(SCoreHarmonyId)]
            public static void Postfix(MotionSensorController __instance, Entity _target, ref bool __result)
            {
                try
                {
                    if (!__result || !ScConfig.RaiderTurretsAsEnemies || !(_target is EntityAliveSDXV4) || !(ScXnpc.IsRaider(_target) || ScTrust.IsHostileToAny(_target))) return;
                    TileEntityPoweredTrigger te = __instance != null ? __instance.TileEntity : null;
                    if (te == null || !te.TargetZombies || !_target.IsAlive() || __instance.Cone == null) return;
                    if (UnityEngine.Vector3.Dot(_target.position - te.ToWorldPos().ToVector3(), __instance.Cone.forward) > 0f) return;
                    __result = false;
                    sensorTargets++;
                }
                catch (Exception ex)
                {
                    ScLog.Once("xnpc-motionsensor", ex);
                }
            }
        }

        // ------------------------------------------------------------------ X11: map markers on raiders ----

        /// <summary>
        /// X11. A raider gets its class's map markers like any other enemy: the red clear-sleeper dot during a clear quest
        /// (and the twitch-spawn markers), from the class's NavObject - "twitch_spawn,twitch_spawn_other,clear_sleeper", the
        /// old raiders' value (Config/entityclasses.xml, written by Tools/gen_arsenal.py). Vanilla's Entity.HandleNavObject,
        /// run from PostInit, registers the class's NavObject for every entity; SCore's EntityAliveSDXV4 overrides it to
        /// register one only while the local player is the NPC's leader (its companion marker) and to drop it otherwise, so
        /// an enemy never had one. For a raider body this prefix does vanilla's registration instead and skips SCore's;
        /// every other NPC - survivors, members, companions - keeps SCore's. The marker is a local UI object, made on
        /// every machine that builds the entity (IsRaider needs no registry); V4's SetDead and vanilla's entity removal
        /// unregister it. Each marker class decides when it shows: clear_sleeper only for a sleeper inside the bounds of
        /// the local player's active quest (NavObject.IsValidEntity), so a wild raider shows nothing, as a wild zombie.
        /// A raider that already has its marker keeps it (only PostInit calls this today; SCore's leader component would
        /// call it again for an NPC with a local leader, which a raider never has).
        /// </summary>
        [HarmonyPatch]
        public static class Patch_RaiderNavObject
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                return BoundLoud(NavObjectTarget, "EntityAliveSDXV4.HandleNavObject()", "raiders get no clear-sleeper markers");
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return NavObjectTarget(); }

            [HarmonyPrefix]
            public static bool Prefix(EntityAliveSDXV4 __instance)
            {
                try
                {
                    if (__instance == null || !ScXnpc.IsRaider(__instance)) return true;
                    // once only: vanilla's RegisterNavObject finds an existing marker by its first class name, not the
                    // whole list, so a second call would add a second marker that nothing ever unregisters
                    if (__instance.NavObject != null) return false;
                    EntityClass ec;
                    string nav = EntityClass.list.TryGetValue(__instance.entityClass, out ec) && ec != null ? ec.NavObject : null;
                    if (!string.IsNullOrEmpty(nav))
                    {
                        __instance.NavObject = NavObjectManager.Instance.RegisterNavObject(nav, __instance);
                        Count("x11-raider-navobject");
                    }
                    return false;
                }
                catch (Exception ex)
                {
                    ScLog.Once("xnpc-navobject", ex);
                    return true;
                }
            }
        }

        // ------------------------------------------------------------------ X12: a gun put away keeps its rounds ----

        /// <summary>
        /// X12 (M11, the user's decision 6). SCore's weapon swap writes the new hand item over slot 0
        /// (Inventory.SetItem(0, item, 1)), and the gun that was there went with whatever it had loaded: a magazine's worth of
        /// loose rounds per melee bout, or a nocked arrow. Every swap of SC's bodies ends here - 0-XNPCCore's close-melee
        /// swap and dry failover (UpdateWeapon(string)), its restore and world-entry re-arm, ScArms' lead and overwhelmed
        /// swaps, ScLoadout.Arm and SCore's own load-time re-equip. The prefix notes slot 0's own ItemValue when it is a
        /// ranged weapon with rounds in it; the finalizer, if the slot holds a different ItemValue afterwards (the gun was
        /// written over) that is not a loaded copy of the same gun (which carries the rounds over), gives those rounds back
        /// to the bag as loose rounds of the gun's selected ammo and empties that ItemValue in place (ScAmmo.ReturnLoose).
        ///
        /// Never twice: a gun Locked &amp; Loaded manages is handled before this sees it - L&amp;L's own Inventory.SetItem prefix
        /// runs inside this call, gives the seated magazine back and empties the very same ItemValue in place, so it holds
        /// nothing by the finalizer (and L&amp;L leaves an unflagged loose-round gun alone). Re-equipping never brings rounds
        /// back either: every re-equip builds a fresh gun with Meta 0 (UpdateWeapon(string) and ItemClass.GetItem; ScLoadout's
        /// copy from the item list, whose entries SCore made with new ItemValues and Inventory.SetItem clones). A finalizer,
        /// not a postfix, because the model refresh after the write could throw, and the gun is gone either way. SC's bodies
        /// only, on the host (the bag is the server's).
        /// </summary>
        [HarmonyPatch]
        public static class Patch_SwapKeepsRounds
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                return BoundLoud(UpdateWeaponTarget, "EntityAliveSDXV4.UpdateWeapon(ItemValue, bool)", "the rounds in a gun SCore's weapon swap puts away are lost again");
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return UpdateWeaponTarget(); }

            [HarmonyPrefix]
            public static void Prefix(EntityAlive __instance, out ItemValue __state)
            {
                __state = null;
                try
                {
                    if (__instance == null || __instance.inventory == null || !ScCommunity.IsHost || !ScXnpc.IsBody(__instance)) return;
                    if (__instance.inventory.GetItemCount() <= 0) return;
                    ItemStack slot = __instance.inventory.GetItem(0);
                    ItemValue gun = slot != null ? slot.itemValue : null;
                    if (gun == null || gun.IsEmpty() || gun.Meta <= 0 || ScAmmo.RangedAction(gun.ItemClass) == null) return;
                    __state = gun;
                }
                catch (Exception ex)
                {
                    ScLog.Once("xnpc-swap-rounds-pre", ex);
                }
            }

            [HarmonyFinalizer]
            public static void Finalizer(EntityAlive __instance, ItemValue __state)
            {
                if (__state == null) return;
                try
                {
                    ItemStack slot = __instance.inventory.GetItem(0);
                    ItemValue now = slot != null ? slot.itemValue : null;
                    if (ReferenceEquals(now, __state)) return;                                   // not written over: the gun is still there
                    // the same gun written back still loaded (a copy of it) carries its rounds over: not a swap (L&L's rule too)
                    if (now != null && now.type == __state.type && now.Meta > 0) return;
                    Count("x12-swap-loaded-gun");
                    int n = ScAmmo.ReturnLoose(__instance, __state, "weapon swap", true);
                    if (n > 0) ScAmmo.NoteSwapReturn(n);
                }
                catch (Exception ex)
                {
                    ScLog.Once("xnpc-swap-rounds", ex);
                }
            }
        }

        // ------------------------------------------------------------------ X13: the held gun survives a save ----

        /// <summary>
        /// X13 (M11, the review's F1), the save half. SCore's V4 Write saves the held item's name only, and vanilla keeps the
        /// rest of a non-player's hand nowhere (EntityCreationData writes holdingItem for players only), so every load - a
        /// chunk coming back as well as a game - re-equipped an empty gun: the loaded rounds and, with Locked &amp; Loaded,
        /// the seated magazine were gone. The postfix appends the held gun (ScAmmo.WriteHeldGun) after SCore's section, which
        /// is length-framed (SdxEntityPersistence: magic, count, each component's length), so SCore's Read stops before it
        /// and an SC without X13, or SCore alone, never reads it; vanilla stores the whole entity record behind one ushort
        /// length, so the next entity is never misaligned. Not gated on bNetworkWrite: chunk saves and unloads build their
        /// EntityCreationData with the default true (Chunk.cs), the same as a spawn sent to a client.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_SaveHeldGun
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                return BoundLoud(WriteTarget, "EntityAliveSDXV4.Write(BinaryWriter, bool)", "the gun in an NPC's hand loses its rounds and its magazine whenever it is saved and loaded");
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return WriteTarget(); }

            [HarmonyPostfix]
            public static void Postfix(EntityAlive __instance, System.IO.BinaryWriter _bw)
            {
                try { ScAmmo.WriteHeldGun(__instance, _bw); }
                catch (Exception ex) { ScLog.Once("xnpc-save-held-gun", ex); }
            }
        }

        /// <summary>
        /// X13, the load half: right where SCore's V4 Read stopped, the held gun X13 saved is read back (ScAmmo.KeepHeldGun)
        /// for this body's PostInit (ScLoadout.AfterPostInit -&gt; ScAmmo.RestoreHeldGun). A save from before X13, SCore's data
        /// alone or a network read without it: the next bytes are not the marker, nothing is read. A client keeps nothing
        /// (host only). Counted (x13-held-gun-read) so an inlined or skipped Read would show.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_LoadHeldGun
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                return BoundLoud(ReadTarget, "EntityAliveSDXV4.Read(byte, BinaryReader)", "the gun in an NPC's hand loses its rounds and its magazine whenever it is saved and loaded");
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return ReadTarget(); }

            [HarmonyPostfix]
            public static void Postfix(EntityAlive __instance, System.IO.BinaryReader _br)
            {
                try
                {
                    Count("x13-held-gun-read");
                    ScAmmo.KeepHeldGun(__instance, _br);
                }
                catch (Exception ex)
                {
                    ScLog.Once("xnpc-load-held-gun", ex);
                }
            }
        }
    }
}
