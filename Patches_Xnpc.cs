using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace SurvivorCommunity
{
    /// <summary>
    /// Every patch on a 0-XNPCCore type (NPCSdcsCharacters.dll), for the survivor classes the community owns (ScXnpc).
    /// The numbering is the migration design's (section 1.2); the rows this file does not have yet belong to later tasks
    /// (X3 kill levels and X6 the melee choice).
    ///
    ///   X1  EModelSdcsNpc.createModel        prefix   the body is built from SC's look, not XNPC's preset archetype
    ///   X1b MinEventActionSdcsApplyLook      prefix   XNPC's look applier never reaches one of ours (belt and braces)
    ///   X2  EModelSdcsNpc.GenerateMeshes     postfix  eyes re-rooted on the head after every build (and hit colliders)
    ///   X4  AvatarSdcsNpcController.SdcsWearTick                     prefix   no no-wear buff: armor wears like SC's
    ///   X5  AvatarSdcsNpcController.SdcsFailoverTick / SdcsCloseRangeTick / SdcsTryWorldEntryRearm
    ///                                                                prefix   XNPC never swaps the hand during a medical use
    ///   X7  AvatarSdcsNpcController.Update   postfix  SC's aim pose, written after XNPC's whole frame
    ///
    /// Every target is DECLARED on XNPC's type (CLAUDE.md pattern #4: a base-class match would patch every player body),
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

        public static void Reset()
        {
            hits.Clear();
            updateHits = 0;
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

        public static MethodBase UpdateTarget()
        {
            return AccessTools.DeclaredMethod(typeof(AvatarSdcsNpcController), "Update", NoArgs);
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
        /// The look comes from ScLooks.ForEntity, the same resolution SC's own bodies use: a pending spawn look, else the
        /// registry's (member or prospect), else this session's, else a fresh roll. Its sex follows the class: the rig, the
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
        /// renderer onto the pelvis - the same eyes-out-of-the-face fault Patches_Sdcs.FixEyeRenderers fixes on SC's own
        /// bodies, fixed the same way (tag and head root bone). The hit colliders of the new build are put back on the
        /// hittable layer too, as SC's own rebuild does, while XnpcBodyColliderLayers is on.
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
                    if (e == null || !ScMedic.IsUsing(e.entityId) || !ScXnpc.IsBody(e)) return true;
                    Count("x5-" + (__originalMethod != null ? __originalMethod.Name : "?"));
                    return false;
                }
                catch (Exception ex)
                {
                    ScLog.Once("xnpc-noswap", ex);
                    return true;
                }
            }
        }

        // ------------------------------------------------------------------ X7: aim pose ----

        /// <summary>
        /// X7. SC's visible aiming (decision D12) on our bodies. XNPC's controller overrides Update, drives locomotion,
        /// carry and the look pitch itself and never sets IsAiming; ScAim.Drive runs after the whole of it, so the aim pose
        /// is the last thing written in the frame. SC's own per-frame body hook (Patches_Sdcs.Patch_UpdateCarry) also
        /// fires inside XNPC's base.Update call, but stays on SC's own bodies: its locomotion writes would fight XNPC's.
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
    }
}
