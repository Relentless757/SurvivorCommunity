using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round MP3 (part L): the Harmony patches linked storage's claims need.
    ///  - TEFeatureStorage.OnLockRequestServer: round MP3 (review storage M2), a postfix - a box opened by hand leaves every
    ///    linked storage view that showed it (ScStorageServer.OnVanillaOpen). The open itself is no longer refused: a view left
    ///    open blocked every box of the base. An open box belongs to the machine that has it open (F3), so a view's later change
    ///    to it is refused on the server, never written.
    ///  - XUiC_DragAndDropWindow.DropCurrentItem: while this machine's view has a change on its way, the item on the cursor is
    ///    not thrown on the ground (a click outside the windows, XUi_FallThrough). If the server refuses a take, that item must
    ///    still be where the view can take it back from (the cursor, the bag) - a thrown one would be doubled.
    ///  - XUiC_DragAndDropWindow.PlaceItemBackInInventory: round MP3 (review storage m3), the same hold for the window's own
    ///    put-back (closing the window, or the cursor outside a menu), which throws the stack on the ground when the bag is full.
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
                    ScLog.Err("TEFeatureStorage.OnLockRequestServer not found (game update?) - a box opened by hand stays in linked storage views (their changes to it are still refused)");
                }
                catch (Exception e) { ScLog.Once("linked-bind-lock", e); }
                return false;
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return Target(); }

            /// <summary>Round MP3 (review storage M2): a postfix, after the lock was granted - the box leaves the views.</summary>
            [HarmonyPostfix]
            public static void Postfix(TEFeatureStorage __instance, int _lockingPlayerID, bool __result)
            {
                try
                {
                    if (!__result || __instance is ScLinkedContainer) return;     // refused, or a view (never a vanilla lock)
                    ScStorageServer.OnVanillaOpen(__instance, _lockingPlayerID);
                }
                catch (Exception e)
                {
                    ScLog.Once("linked-vanilla-open", e);
                }
            }
        }

        /// <summary>
        /// Round MP3 (review storage m3): XUiC_DragAndDropWindow.PlaceItemBackInInventory - run by the window's own close and by its
        /// Update outside a menu - puts the cursor's stack into the bag, or throws it on the ground when the bag is full, past the
        /// DropCurrentItem hold below. While this machine's view has a change on its way the stack stays on the cursor instead
        /// (the player's DragAndDropItem, saved with the player): a refused take finds it there. Once the answer is in, the next
        /// call (Update, every frame outside a menu, or the next open) does vanilla's put-back.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_LinkedHoldPlaceBack
        {
            public static MethodBase Target()
            {
                return AccessTools.DeclaredMethod(typeof(XUiC_DragAndDropWindow), "PlaceItemBackInInventory", Type.EmptyTypes);
            }

            [HarmonyPrepare]
            public static bool Prepare()
            {
                try
                {
                    if (Target() != null) return true;
                    ScLog.Warn("XUiC_DragAndDropWindow.PlaceItemBackInInventory not found (game update?) - closing the window with a full bag can throw an item taken from linked storage before the server answers");
                }
                catch (Exception e) { ScLog.Once("linked-bind-placeback", e); }
                return false;
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return Target(); }

            [HarmonyPrefix]
            public static bool Prefix(XUiC_DragAndDropWindow __instance)
            {
                try
                {
                    if (__instance == null || __instance.IsEmpty() || !ScStorage.Busy()) return true;
                    return false;                                               // kept on the cursor until the answer is in
                }
                catch (Exception e)
                {
                    ScLog.Once("linked-hold-placeback", e);
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
                        // round MP4 (part: tells): this file's tells go by localization key (ScCommunity.TellKey / ScPlayers.TellCommunityKey), so each player reads them in their own language
                        if (p != null) ScCommunity.TellKey(p, "scStorageBusy", "ui_denied");
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
