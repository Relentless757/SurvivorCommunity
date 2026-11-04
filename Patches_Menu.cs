using System;
using HarmonyLib;

namespace SurvivorCommunity
{
    /// <summary>
    /// The ESC menu entry. Config/XUi_InGame/windows.xml inserts a simplebutton named btnScCommunity into
    /// the vanilla menu column; XUiC_InGameMenuWindow.Init wires only its own ten buttons by name, so an
    /// XML-inserted one renders but does nothing until this postfix attaches a handler (SCore's own two
    /// entries work the same way). Press: close the menu, open the community window modally.
    /// </summary>
    public static class Patches_Menu
    {
        public const string ButtonName = "btnScCommunity";

        [HarmonyPatch(typeof(XUiC_InGameMenuWindow), nameof(XUiC_InGameMenuWindow.Init), new Type[0])]
        public static class Patch_InGameMenuInit
        {
            [HarmonyPostfix]
            public static void Postfix(XUiC_InGameMenuWindow __instance)
            {
                try
                {
                    XUiController holder = __instance.GetChildById(ButtonName);
                    if (holder == null) { ScLog.Warn("ESC menu: '" + ButtonName + "' is not in the menu - Config/XUi_InGame/windows.xml did not apply (SCore's btnNPCView missing?)"); return; }
                    XUiC_SimpleButton button = holder.GetChildByType<XUiC_SimpleButton>();
                    if (button == null) { ScLog.Warn("ESC menu: '" + ButtonName + "' has no simple button child"); return; }
                    button.OnPressed += (sender, mouseButton) =>
                    {
                        try
                        {
                            EntityPlayerLocal player = __instance.xui.playerUI.entityPlayer;
                            if (player == null || player.IsDead()) return;     // W1 review LOW-1: OpenWindow opens nothing for the dead, so the menu stays
                            GUIWindowManager wm = __instance.xui.playerUI.windowManager;
                            wm.Close(__instance.windowGroup);
                            ScCommunity.OpenWindow(player);
                        }
                        catch (Exception e) { ScLog.Once("menu-press", e); }
                    };
                }
                catch (Exception e)
                {
                    ScLog.Once("XUiC_InGameMenuWindow.Init", e);
                }
            }
        }
    }
}
