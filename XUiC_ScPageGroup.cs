using System;
using UnityEngine.Scripting;

namespace SurvivorCommunity
{
    /// <summary>
    /// The community window as a page of the player menu (W1, round 19): the controller of the window group
    /// "sccommunity" (Config/XUi_InGame/xui.xml), resolved as controller="SurvivorCommunity.ScPageGroup, SurvivorCommunity".
    ///
    /// The tab bar over the player menu's pages (Crafting, Character, Map, ...) is a window group of its own, "windowpaging"
    /// (window windowPagingHeader, controller XUiC_WindowSelector). Every vanilla page group opens it in OnOpen, selects its
    /// own tab and closes it in OnClose (XUiC_CraftingWindowGroup, XUiC_CharacterSheetWindowGroup, ...); a page without
    /// such a controller shows with no bar. This is that controller, with two differences:
    ///   - it selects the tab BEFORE it opens the bar. The bar's OnOpen opens whatever tab is selected
    ///     (XUiC_WindowSelector.OpenSelectedWindow, modally), and a stale selection - the last page used, Crafting at the
    ///     start - would open that page, which closes this one. Vanilla's groups open the bar first because only the
    ///     selector opens them, after selecting them; this window is also opened straight by name (SCore's OpenWindowSDX,
    ///     any Open("sccommunity")).
    ///   - it does so only while the bar has a Community tab that can be opened (Tab). SetSelected with a name no tab has
    ///     keeps the old selection - that tab stays lit, the title keeps its name, and opening the bar would open its page
    ///     over this one. Without the tab (the prefab editor hides it; windows.xml did not apply) the window opens on its
    ///     own, as before W1.
    /// The tab is a ButtonSelectable named ScCommunity on its own plate left of the bar (Config/XUi_InGame/windows.xml), so
    /// the selector lights it, titles the bar "Community" and pages to it like any other tab.
    /// R2 (round 27, 3.4 build): the radio station's group "scradio" has this controller too - the page is the group's own
    /// name, its tab the Radio tab left of the Community plate (XUiC_ScRadioTab), shown only while the player carries a
    /// handheld radio. Without it (the window opened at a radio block) the radio window opens alone, as before.
    /// </summary>
    [Preserve]
    public class XUiC_ScPageGroup : XUiController
    {
        /// <summary>The player menu's tab bar: vanilla's window group "windowpaging" (XUi_InGame/xui.xml).</summary>
        public const string PagingGroup = "windowpaging";

        public override void OnOpen()
        {
            base.OnOpen();
            try
            {
#if SC_V34
                // R2: the radio station's window ("scradio") is a page too, its tab the Radio tab (XUiC_ScRadioTab), shown only
                // while a handheld radio is carried - so its look is made current first (the bar may have been closed since).
                // Without the tab (no handheld: the window opened at a radio block) it opens on its own, as before R2, unlogged.
                string page = windowGroup != null && !string.IsNullOrEmpty(windowGroup.Id) ? windowGroup.Id : ScCommunity.WindowGroup;
                bool radio = string.Equals(page, ScRadio.WindowGroup, StringComparison.OrdinalIgnoreCase);
                if (radio) XUiC_ScRadioTab.RefreshNow();
                XUiC_WindowSelector selector = Selector(xui);
                if (Tab(selector, page) == null)
                {
                    if (!radio)
                        ScLog.OnceOut("community-tab-none", "community window: the tab bar has no Community tab (windowPagingHeader has no "
                                      + "visible ScCommunity) - the window opens on its own, without the bar");
                    return;
                }
                selector.SetSelected(page);
#else
                XUiC_WindowSelector selector = Selector(xui);
                if (Tab(selector) == null)
                {
                    ScLog.OnceOut("community-tab-none", "community window: the tab bar has no Community tab (windowPagingHeader has no "
                                  + "visible ScCommunity) - the window opens on its own, without the bar");
                    return;
                }
                // select first: the bar's OnOpen opens the selected tab's page, and this page is already opening (IsWindowOpen)
                selector.SetSelected(ScCommunity.WindowGroup);
#endif
                xui.playerUI.windowManager.Open(PagingGroup, false);
            }
            catch (Exception e)
            {
                ScLog.Once("community-page-open", e);
            }
        }

        public override void OnClose()
        {
            base.OnClose();
            try
            {
                // as vanilla's pages: the bar goes with the page (a tab press reopens it with the next page)
                if (xui != null && xui.playerUI != null && xui.playerUI.windowManager != null)
                    xui.playerUI.windowManager.Close(PagingGroup);
            }
            catch (Exception e)
            {
                ScLog.Once("community-page-close", e);
            }
        }

        /// <summary>The player menu's tab bar controller, or null.</summary>
        public static XUiC_WindowSelector Selector(XUi xui)
        {
            XUiController group = xui != null ? xui.FindWindowGroupByName(PagingGroup) : null;
            return group != null ? group.GetChildByType<XUiC_WindowSelector>() : null;
        }

        /// <summary>
        /// The Community tab when the bar has one the selector would open: a button named like the window group (the
        /// selector's buttonsByName is case-insensitive), visible and enabled - OpenSelectedWindow pages on past a hidden or
        /// disabled one. Null otherwise.
        /// </summary>
        public static XUiC_ButtonSelectable Tab(XUiC_WindowSelector selector)
        {
#if SC_V34
            return Tab(selector, ScCommunity.WindowGroup);
        }

        /// <summary>R2: a page's tab by its window group's name (sccommunity, scradio), as Tab(selector) for the Community tab.</summary>
        public static XUiC_ButtonSelectable Tab(XUiC_WindowSelector selector, string page)
        {
            if (selector == null || selector.buttonsByName == null || string.IsNullOrEmpty(page)) return null;
            XUiC_ButtonSelectable tab;
            if (!selector.buttonsByName.TryGetValue(page, out tab) || tab == null || tab.ViewComponent == null) return null;
            return tab.ViewComponent.IsVisible && tab.ViewComponent.Enabled ? tab : null;
#else
            if (selector == null || selector.buttonsByName == null) return null;
            XUiC_ButtonSelectable tab;
            if (!selector.buttonsByName.TryGetValue(ScCommunity.WindowGroup, out tab) || tab == null || tab.ViewComponent == null) return null;
            return tab.ViewComponent.IsVisible && tab.ViewComponent.Enabled ? tab : null;
#endif
        }
    }
}
