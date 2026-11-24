using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round MP3 (part L): the two Harmony patches linked storage's claims need.
    ///  - TEFeatureStorage.OnLockRequestServer: a box another player's linked storage view claims cannot be opened by hand (the
    ///    server refuses the lock, and that player is told why). An open box belongs to the machine that has it open: the
    ///    view's server-side writes would be overwritten by that copy (F3). A claim of the opener's own view is stale and goes.
    ///  - XUiC_DragAndDropWindow.DropCurrentItem: while this machine's view has a change on its way, the item on the cursor is
    ///    not thrown on the ground (a click outside the windows, XUi_FallThrough). If the server refuses a take, that item must
    ///    still be where the view can take it back from (the cursor, the bag) - a thrown one would be doubled.
    /// </summary>
    public static class Patches_LinkedStorage
    {
        [HarmonyPatch]
        public static class Patch_LinkedVanillaOpen
        {
            public static MethodBase Target()
            {
                return AccessTools.DeclaredMethod(typeof(TEFeatureStorage), "OnLockRequestServer", new[] { typeof(int), typeof(PooledBinaryReader), typeof(ushort) });
            }

            [HarmonyPrepare]
            public static bool Prepare()
            {
                try
                {
                    if (Target() != null) return true;
                    ScLog.Err("TEFeatureStorage.OnLockRequestServer not found (game update?) - a box shown in linked storage can be opened by hand meanwhile");
                }
                catch (Exception e) { ScLog.Once("linked-bind-lock", e); }
                return false;
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return Target(); }

            [HarmonyPrefix]
            [HarmonyPriority(Priority.First)]
            public static bool Prefix(TEFeatureStorage __instance, int _lockingPlayerID, ref bool __result)
            {
                try
                {
                    if (__instance is ScLinkedContainer) return true;           // a view never takes a vanilla lock
                    if (ScStorageServer.AllowVanillaOpen(__instance, _lockingPlayerID)) return true;
                    __result = false;                                           // LockManager answers the opener "refused"
                    return false;
                }
                catch (Exception e)
                {
                    ScLog.Once("linked-vanilla-open", e);
                    return true;
                }
            }
        }

        [HarmonyPatch]
        public static class Patch_LinkedHoldThrow
        {
            private static float nextTell;

            [HarmonyPrepare]
            public static bool Prepare()
            {
                try
                {
                    if (AccessTools.DeclaredMethod(typeof(XUiC_DragAndDropWindow), "DropCurrentItem", Type.EmptyTypes) != null) return true;
                    ScLog.Warn("XUiC_DragAndDropWindow.DropCurrentItem not found (game update?) - an item taken from linked storage can be thrown before the server answers");
                }
                catch (Exception e) { ScLog.Once("linked-bind-throw", e); }
                return false;
            }

            [HarmonyTargetMethods]
            public static IEnumerable<MethodBase> TargetMethods()
            {
                MethodBase a = AccessTools.DeclaredMethod(typeof(XUiC_DragAndDropWindow), "DropCurrentItem", Type.EmptyTypes);
                MethodBase b = AccessTools.DeclaredMethod(typeof(XUiC_DragAndDropWindow), "DropCurrentItem", new[] { typeof(int) });
                if (a != null) yield return a;
                if (b != null) yield return b;
            }

            [HarmonyPrefix]
            public static bool Prefix()
            {
                try
                {
                    if (!ScStorage.Busy()) return true;
                    float now = Time.unscaledTime;
                    if (now >= nextTell)
                    {
                        nextTell = now + 2f;
                        EntityPlayerLocal p = ScCommunity.LocalPlayer;
                        if (p != null) ScCommunity.Tell(p, Localization.Get("scStorageBusy"), "ui_denied");
                    }
                    return false;
                }
                catch (Exception e)
                {
                    ScLog.Once("linked-hold-throw", e);
                    return true;
                }
            }
        }
    }
}
