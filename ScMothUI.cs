using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round ui2: the C# half of SC's MothUI support (iiMothMan_MothUI; the analysis is Port33\research\uicompat\mothui.md,
    /// the XML half is in Config/XUi_InGame/windows.xml under mod_loaded('iiMothMan_MothUI')). Nothing here runs or
    /// patches anything without MothUI, and nothing references a MothUI type at compile time.
    ///
    /// The statement: MothUI shows the NPC's line itself, inside its dialog card (MothUI.XUiC_DialogStatement, binding
    /// mui_statement), as the statement's raw Text. An enclave respondent's lines carry tokens ({enc}, {leader}, {count},
    /// ...) that SC fills only in its own line box (XUiC_ScDialogLine, ScEncDialog.Fill), which the MothUI branch hides.
    /// Patch_MothStatement hands MothUI SC's filled text for the statement SC filled; anything else (an SCore SDX NPC,
    /// DialogLine and VoiceLines both off, a statement SC has not filled yet) keeps MothUI's own text. XUiC_ScDialogLine
    /// marks MothUI's row dirty after each fill, so the two windows' update order costs at most one frame.
    ///
    /// The pages: MothUI's page frame (bar 96 units at the top, key footer 96 at the bottom) opens with every page of the
    /// player menu, SC's included, and its MenuLayout never touches an anchored window, so the Community (1500x924) and
    /// Radio (1384x940) windows overlapped the bar and the footer by 36-44 units at a 1044-unit-tall screen. FitPage
    /// scales a page down to fit between them (MothUI's own room: its MenuLayout frame of 216 and width margin of 80) and
    /// recenters it; NGUI scales the colliders with it. At full size nothing changes. The scale has to be held: every
    /// XUiV_Window.updateData (any SetDirty, the window's own OnOpen included) runs setRootNode, which sets localScale back
    /// to one - so the pages call FitPage after base.Update each frame, where the view has just updated and NGUI has not
    /// drawn yet (LateUpdate). Only with MothUI's look in charge (Layout: not with AshfallUI, whose branches win the XML)
    /// and only while MothUI's frame shows (the tab bar's group is open; a radio opened at a block without a handheld
    /// shows no bar); otherwise the page is put back at its XML place and size.
    /// </summary>
    public static class ScMothUI
    {
        public const string ModName = "iiMothMan_MothUI";
        private const string StatementTypeName = "MothUI.XUiC_DialogStatement";
        /// <summary>MothUI's page frame: the 96-unit bar and the 96-unit footer plus a margin (MothUI MenuLayout).</summary>
        private const float FrameHeight = 216f;
        private const float FrameWidth = 80f;

        private const string AshfallUI = "zzzzz_AshfallUI";

        private static int active = -1;
        private static int layout = -1;

        /// <summary>MothUI is loaded (the mod list does not change during a session, so it is read once).</summary>
        public static bool Active
        {
            get
            {
                if (active < 0)
                {
                    try { active = ModManager.ModLoaded(ModName) ? 1 : 0; }
                    catch (Exception) { active = 0; }
                }
                return active == 1;
            }
        }

        /// <summary>MothUI's look is the one in charge: MothUI loaded and AshfallUI not (with both, SC's XML takes AshfallUI's
        /// branches, as AshfallUI's windows win).</summary>
        public static bool Layout
        {
            get
            {
                if (layout < 0)
                {
                    try { layout = Active && !ModManager.ModLoaded(AshfallUI) ? 1 : 0; }
                    catch (Exception) { layout = 0; }
                }
                return layout == 1;
            }
        }

        /// <summary>MothUI's page frame shows: the player menu's tab bar group (XUiC_ScPageGroup.PagingGroup) is open.</summary>
        public static bool FrameOpen(XUi xui)
        {
            return xui != null && xui.playerUI != null && xui.playerUI.windowManager != null
                   && xui.playerUI.windowManager.IsWindowOpen(XUiC_ScPageGroup.PagingGroup);
        }

        /// <summary>MothUI.XUiC_DialogStatement.GetBindingValueInternal(ref string, string), or null without MothUI.</summary>
        public static MethodBase StatementTarget()
        {
            Type t = ScTypes.Find(StatementTypeName);
            return t != null ? AccessTools.DeclaredMethod(t, "GetBindingValueInternal", new[] { typeof(string).MakeByRefType(), typeof(string) }) : null;
        }

        [HarmonyPatch]
        public static class Patch_MothStatement
        {
            [HarmonyPrepare]
            public static bool Prepare() { return StatementTarget() != null; }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return StatementTarget(); }

            /// <summary>Positional arguments (__0, __1): MothUI's parameter names are not part of its contract.</summary>
            [HarmonyPostfix]
            public static void Postfix(XUiController __instance, ref string __0, string __1, bool __result)
            {
                try
                {
                    if (!__result || __1 != "mui_statement" || __instance == null) return;
                    XUiC_DialogWindowGroup group = __instance.windowGroup != null ? __instance.windowGroup.Controller as XUiC_DialogWindowGroup : null;
                    Dialog dialog = group != null ? group.CurrentDialog : null;
                    string filled;
                    if (XUiC_ScDialogLine.TryFilled(dialog != null ? dialog.CurrentStatement : null, out filled)) __0 = filled;
                }
                catch (Exception e)
                {
                    ScLog.Once("mothui-statement", e);
                }
            }
        }

        /// <summary>
        /// Holds a page window (anchor Center, its pos the top-left corner, XML pos -width/2,height/2) at the size that fits
        /// MothUI's frame, centered; with no frame, at its XML place and full size. Called after base.Update every frame
        /// (setRootNode resets the scale) and from OnOpen with a guess at whether the bar comes with the page, for a first
        /// frame drawn before the page's Update. Writes only what differs, so a held page costs a few comparisons; a changed
        /// screen size or UI scale applies at once. Does nothing unless MothUI's look is in charge (Layout).
        /// </summary>
        public static void FitPage(XUiController page, int width, int height, bool frame)
        {
            try
            {
                if (!Layout || page == null || page.xui == null || width <= 0 || height <= 0) return;
                XUiV_Window win = page.ViewComponent as XUiV_Window;
                if (win == null || win.UiTransform == null) return;
                float s = 1f;
                if (frame)
                {
                    Vector2i screen = page.xui.GetXUiScreenSize();
                    if (screen.x > 0 && screen.y > 0)
                    {
                        s = Mathf.Min(1f, Mathf.Min((screen.y - FrameHeight) / height, (screen.x - FrameWidth) / width));
                        s = Mathf.Max(0.5f, s);          // a page past half size is unreadable anyway; never collapse it
                    }
                }
                Vector2i pos = new Vector2i(-Mathf.RoundToInt(width * s / 2f), Mathf.RoundToInt(height * s / 2f));
                if (!win.Position.Equals(pos))
                {
                    win.Position = pos;                  // SetDirty: the next updateData resets the scale (held below)
                    // the transform at once too, as setRootNode would, so no frame is drawn at the old place before then
                    Vector2i padded = win.PaddedPosition;
                    win.UiTransform.localPosition = new Vector3(padded.x, padded.y, 0f);
                }
                if (!Mathf.Approximately(win.UiTransform.localScale.x, s)) win.UiTransform.localScale = new Vector3(s, s, 1f);
            }
            catch (Exception e)
            {
                ScLog.Once("mothui-fit", e);
            }
        }
    }
}
