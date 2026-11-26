using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round n10 (3.4): the member inventory panel's two hooks into 0-SCore.
    ///
    /// Patch_OpenContainer: "Show me your inventory" (the talk menu, Voice Control's ring and phrase - every route runs SCore's
    /// ExecuteCMD "OpenInventory") ends in EntityUtilities.OpenContainer(EntityPlayerLocal, SCoreLootContainer): on the host
    /// with HarvestManager's container, on a multiplayer client with the copy the server sent it
    /// (NetPackageHarvestInventoryData.ProcessPackage). For one of the community's members it opens SC's panel
    /// (XUiC_ScMemberInvGroup) and skips SCore's looting screen; anyone else, the panel switched off (MemberInventoryPanel)
    /// or a panel that cannot open (its XML missing) keeps SCore's screen.
    ///
    /// Patch_LocalLootWindow: SCore asks StorageLockUtils.IsOpenInLocalLootWindow whether the host's own screen shows an NPC's
    /// container before it hands a remote player a copy (HarvestManager.IsOpenByOther) - it only knows the looting screen,
    /// so the panel answers too: two copies of one container open at once would duplicate items.
    ///
    /// Patch_BagCellActions: the item info panel's Use, Read and Open (ItemActionEntryUse) taken off a BAG cell. For a
    /// loot-container cell vanilla's animated use (UseItemWithAnimationCoroutine) runs the action on
    /// xui.LootContainer's grid at the cell's slot number - the PACK here, not her bag - so it would read, open or use
    /// whatever sits in that pack slot (and Read/Open take one off the bag first). Pack cells keep it: there
    /// xui.LootContainer is right, as on vanilla's looting screen. Take, Wear, Drop and Scrap work on the cell itself and stay.
    ///
    /// Every target resolves in a Prepare that never throws (a missing one switches its patch off with one error line), as
    /// PatchClassByClass expects; ModApi's ExpectedTargets lists them all.
    /// </summary>
    public static class Patches_MemberInv
    {
        public static MethodBase OpenContainerTarget()
        {
            return AccessTools.Method(typeof(EntityUtilities), "OpenContainer", new[] { typeof(EntityPlayerLocal), typeof(SCoreLootContainer) });
        }

        public static MethodBase LocalLootWindowTarget()
        {
            return AccessTools.Method(typeof(StorageLockUtils), "IsOpenInLocalLootWindow", new[] { typeof(TEFeatureStorage) });
        }

        public static MethodBase ActionListTarget()
        {
            return AccessTools.Method(typeof(XUiC_ItemActionList), "SetCraftingActionList",
                                      new[] { typeof(XUiC_ItemActionList.ItemActionListTypes), typeof(XUiController) });
        }

        private static bool Bound(Func<MethodBase> target, string label, string consequence)
        {
            try
            {
                if (target() != null) return true;
                ScLog.Err(label + " not found (0-SCore update?) - " + consequence);
            }
            catch (Exception e)
            {
                ScLog.Once("memberinv-bind-" + label, e);
            }
            return false;
        }

        [HarmonyPatch]
        public static class Patch_OpenContainer
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                return Bound(OpenContainerTarget, "EntityUtilities.OpenContainer(EntityPlayerLocal, SCoreLootContainer)",
                             "members' inventories open on the game's looting screen, not SC's member inventory panel");
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return OpenContainerTarget(); }

            /// <summary>__0 the local player, __1 the container (by position: SCore's parameter names are not part of the contract).</summary>
            [HarmonyPrefix]
            public static bool Prefix(EntityPlayerLocal __0, SCoreLootContainer __1)
            {
                try
                {
                    if (__0 == null || __1 == null) return true;
                    World w = ScCommunity.World;
                    EntityAlive npc = w != null ? w.GetEntity(__1.EntityId) as EntityAlive : null;
                    // round MP6 (part G): another community's member - neither SC's panel nor SCore's screen (talk only: decision 4),
                    // the panel switched off or not
                    if (XUiC_ScMemberInvGroup.IsForeign(npc)) { XUiC_ScMemberInvGroup.RefuseForeign(__0, __1, npc); return false; }
                    if (!ScConfig.MemberInventoryPanel) return true;
                    if (!XUiC_ScMemberInvGroup.IsMember(npc)) return true;
                    if (XUiC_ScMemberInvGroup.Open(__0, __1, npc)) return false;
                }
                catch (Exception e)
                {
                    ScLog.Once("memberinv-open", e);
                }
                return true;
            }
        }

        [HarmonyPatch]
        public static class Patch_LocalLootWindow
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                return Bound(LocalLootWindowTarget, "StorageLockUtils.IsOpenInLocalLootWindow(TEFeatureStorage)",
                             "with the member inventory panel open on the host, a remote player could open the same member's inventory at once");
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return LocalLootWindowTarget(); }

            [HarmonyPostfix]
            public static void Postfix(TEFeatureStorage __0, ref bool __result)
            {
                try
                {
                    if (!__result && XUiC_ScMemberInvGroup.IsShowing(__0)) __result = true;
                }
                catch (Exception e)
                {
                    ScLog.Once("memberinv-local-open", e);
                }
            }
        }

        [HarmonyPatch]
        public static class Patch_BagCellActions
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                return Bound(ActionListTarget, "XUiC_ItemActionList.SetCraftingActionList(ItemActionListTypes, XUiController)",
                             "Use, Read and Open on an item in a member's BAG would act on her PACK slot of the same number");
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return ActionListTarget(); }

            /// <summary>__1 the item controller the list was built for (by position, as above).</summary>
            [HarmonyPostfix]
            public static void Postfix(XUiC_ItemActionList __instance, XUiController __1)
            {
                try
                {
                    XUiC_ItemStack cell = __1 as XUiC_ItemStack;
                    if (cell == null || cell.GetParentByType<XUiC_ScBagGrid>() == null) return;
                    List<BaseItemActionEntry> list = __instance.itemActionEntries;
                    for (int i = list.Count - 1; i >= 0; i--)
                    {
                        if (!(list[i] is ItemActionEntryUse)) continue;
                        list[i].DisableEvents();     // as SetCraftingActionList does for the entries it drops
                        list.RemoveAt(i);
                    }
                }
                catch (Exception e)
                {
                    ScLog.Once("memberinv-bag-actions", e);
                }
            }
        }
    }
}
