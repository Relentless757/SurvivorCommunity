using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Platform;
using UnityEngine;
using UnityEngine.Scripting;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round 28 (X3, the user's decision 4, 2026-11-10): the talk page's pager. Vanilla's response list (windowResponses: a
    /// grid of 10 response_entry slots) fills its slots in the statement's order and silently drops whatever does not fit, so
    /// a statement with more visible rows than slots lost its last ones - "Nevermind." first of all (0-XNPCCore 3.4.04's own
    /// weapon menu lists 11 rows when she carries all seven weapon kinds). Now such a page splits into pages of the window's
    /// slot count, with "Nevermind." (response id "done") pinned as the last row of EVERY page; the mouse wheel over the rows,
    /// the gamepad's right stick (vanilla's XUi.HandlePaging, as every paged list) and the two arrows of a small "1/2"
    /// indicator switch pages. Every dialog, not only the NPCs' (a trader's long page too); a page that fits is untouched.
    ///
    ///   P1 XUiC_DialogResponseList.Update(float) transpiler: right after its only DialogStatement.GetResponses() call (the
    ///      rows every mod's GetResponses postfix has already added to) ScDialogPager.Page(list, this) - stack-neutral, the
    ///      list in, a list out. Vanilla's fill, wiring, sounds and cursor run on whatever Page returns. Anchored on the call
    ///      by its MethodInfo; found other than once it logs an ERROR and leaves the method as it is (vanilla).
    ///   P2 Init postfix: every row's OnScroll pages (the rows already carry on_scroll: their template's "scroll" style).
    ///   P3 OnOpen postfix: a new talk starts on page 1.
    ///   P4 Update postfix: the right stick, through vanilla's HandlePaging (repeat timing and paging sound), while paged and
    ///      not on keyboard and mouse.
    ///   XUiC_ScDialogPager (Config/XUi_InGame/windows.xml, appended to windowResponses): the "1/2" text and the arrows.
    ///
    /// Page mirrors vanilla's own fill: a row whose response is missing, a repeated UniqueID and a row whose first failing
    /// requirement is Hide take no slot; a row grayed by a failing non-Hide requirement takes one. Only a statement with more
    /// rows than slots is evaluated here (twice per refresh then: here and in vanilla's entry setter, the same frame); rows
    /// vanilla never reached are evaluated now too. Selection never depends on a slot (vanilla selects by the entry's
    /// response object), so nothing else needs to know about pages. DialogPager=false (read at every refresh) and any
    /// exception (logged once) give vanilla's list back.
    /// </summary>
    public static class ScDialogPager
    {
        /// <summary>The response id 0-XNPCCore, vanilla's traders and SC give "Nevermind.": pinned last on every page.</summary>
        public const string NevermindId = "done";
        /// <summary>One wheel flip per this many real seconds: a fast wheel or a trackpad does not skip pages.</summary>
        private const float WheelGap = 0.12f;

        /// <summary>Per response list (one per local player UI).</summary>
        internal sealed class State
        {
            /// <summary>The statement shown (reference): another one starts on page 1.</summary>
            public object Statement;
            public int Page, Pages = 1;
            /// <summary>Bumped whenever Page or Pages changes: the indicator refreshes its bindings on a change.</summary>
            public int Version;
            public float NextWheel;
            public readonly List<BaseResponseEntry> Visible = new List<BaseResponseEntry>();
            public readonly HashSet<string> Unique = new HashSet<string>();
            /// <summary>HandlePaging's two callbacks, made once per list (no allocation per frame).</summary>
            public Func<bool> Next, Prev;
        }

        private static readonly ConditionalWeakTable<XUiC_DialogResponseList, State> states = new ConditionalWeakTable<XUiC_DialogResponseList, State>();
        /// <summary>P1's anchor: how often the transpiler found GetResponses() (-1 before it ran; 1 = the pager is on).</summary>
        private static int anchors = -1;
        private static int pagedRefreshes, flips, failures;

        internal static State StateOf(XUiC_DialogResponseList ui)
        {
            if (ui == null) return null;
            State s;
            if (states.TryGetValue(ui, out s)) return s;
            s = new State();
            s.Next = () => Flip(ui, 1, false);
            s.Prev = () => Flip(ui, -1, false);
            states.Add(ui, s);
            return s;
        }

        /// <summary>The page count of this list (1 when it is not paged, or unknown).</summary>
        public static int PagesOf(XUiC_DialogResponseList ui)
        {
            State s;
            return ui != null && states.TryGetValue(ui, out s) ? s.Pages : 1;
        }

        /// <summary>The page shown, 0-based.</summary>
        public static int PageOf(XUiC_DialogResponseList ui)
        {
            State s;
            return ui != null && states.TryGetValue(ui, out s) ? s.Page : 0;
        }

        public static int VersionOf(XUiC_DialogResponseList ui)
        {
            State s;
            return ui != null && states.TryGetValue(ui, out s) ? s.Version : 0;
        }

        /// <summary>
        /// P1: the rows vanilla is about to fill (GetResponses' list) -> the rows of the page shown. Returns 'all' itself
        /// whenever the visible rows fit (vanilla, untouched), the pager is off or anything throws.
        /// </summary>
        public static List<BaseResponseEntry> Page(List<BaseResponseEntry> all, XUiC_DialogResponseList ui)
        {
            State s = null;
            try
            {
                if (ui == null || all == null) return all;
                s = StateOf(ui);
                object st = ui.currentDialog != null ? ui.currentDialog.CurrentStatement : null;
                if (!ReferenceEquals(st, s.Statement))
                {
                    s.Statement = st;
                    s.Page = 0;                            // a new statement starts on page 1
                }
                int slots = ui.entryList != null ? ui.entryList.Count : 0;   // the window's slots (10 in vanilla's XUi), never hardcoded
                if (!ScConfig.DialogPager || slots < 2 || all.Count <= slots) return Single(s, all);

                XUi xui = ui.xui;
                EntityPlayer player = xui != null && xui.playerUI != null ? xui.playerUI.entityPlayer : null;
                EntityNPC npc = xui != null && xui.Dialog != null ? xui.Dialog.Respondent : null;
                s.Visible.Clear();
                s.Unique.Clear();
                for (int i = 0; i < all.Count; i++)
                {
                    BaseResponseEntry e = all[i];
                    DialogResponse r = e != null ? e.Response : null;
                    if (r == null) continue;                                              // GetResponse found nothing
                    string uid = e.UniqueID ?? "";
                    if (uid.Length > 0 && s.Unique.Contains(uid)) continue;               // vanilla's UniqueID rule
                    if (Hidden(r, player, npc)) continue;                                 // its first failing requirement is Hide
                    s.Visible.Add(e);
                    if (uid.Length > 0) s.Unique.Add(uid);
                }
                if (s.Visible.Count <= slots) return Single(s, all);                     // fits: vanilla fills it as always

                BaseResponseEntry last = s.Visible[s.Visible.Count - 1];
                BaseResponseEntry pin = IsNevermind(last) ? last : null;
                int per = slots - (pin != null ? 1 : 0);
                int body = s.Visible.Count - (pin != null ? 1 : 0);
                int pages = (body + per - 1) / per;
                int page = Mathf.Clamp(s.Page, 0, pages - 1);
                if (pages != s.Pages || page != s.Page) s.Version++;
                s.Pages = pages;
                s.Page = page;
                var shown = new List<BaseResponseEntry>(slots);
                int to = Math.Min(body, (page + 1) * per);
                for (int i = page * per; i < to; i++) shown.Add(s.Visible[i]);
                if (pin != null) shown.Add(pin);
                pagedRefreshes++;
                return shown;
            }
            catch (Exception e)
            {
                failures++;
                ScLog.Once("dialog-pager", e);
                if (s != null) Single(s, all);
                return all;
            }
        }

        private static List<BaseResponseEntry> Single(State s, List<BaseResponseEntry> all)
        {
            if (s.Pages != 1 || s.Page != 0)
            {
                s.Pages = 1;
                s.Page = 0;
                s.Version++;
            }
            return all;
        }

        /// <summary>XUiC_DialogResponseEntry.CurrentResponse's own rule: the first requirement that fails decides.</summary>
        private static bool Hidden(DialogResponse r, EntityPlayer player, EntityNPC npc)
        {
            List<BaseDialogRequirement> reqs = r.RequirementList;
            if (reqs == null) return false;
            for (int i = 0; i < reqs.Count; i++)
            {
                BaseDialogRequirement q = reqs[i];
                if (q == null) continue;
                if (!q.CheckRequirement(player, npc)) return q.RequirementVisibilityType == BaseDialogRequirement.RequirementVisibilityTypes.Hide;
            }
            return false;
        }

        private static bool IsNevermind(BaseResponseEntry e)
        {
            return e != null && (e.ID == NevermindId || (e.Response != null && e.Response.ID == NevermindId));
        }

        /// <summary>One page on (step 1) or back (-1), clamped, no wrap. True when the page changed (HandlePaging plays its sound then).</summary>
        internal static bool Flip(XUiC_DialogResponseList ui, int step, bool sound)
        {
            try
            {
                State s = StateOf(ui);
                if (s == null || s.Pages <= 1) return false;
                int to = Mathf.Clamp(s.Page + step, 0, s.Pages - 1);
                if (to == s.Page) return false;
                s.Page = to;
                s.Version++;
                flips++;
                ui.Refresh();
                if (sound && ui.xui != null && ui.xui.playerUI != null && ui.xui.playerUI.CursorController != null) ui.xui.playerUI.CursorController.PlayPagingSound();
                return true;
            }
            catch (Exception e)
            {
                ScLog.Once("dialog-pager-flip", e);
                return false;
            }
        }

        /// <summary>The wheel over a row or the indicator: NGUI's delta is positive for the wheel rolled away from you (back a page).</summary>
        internal static void Wheel(XUiC_DialogResponseList ui, float delta)
        {
            State s = StateOf(ui);
            if (s == null || s.Pages <= 1 || delta == 0f) return;
            float now = Time.unscaledTime;
            if (now < s.NextWheel) return;
            s.NextWheel = now + WheelGap;
            Flip(ui, delta > 0f ? -1 : 1, true);
        }

        /// <summary>For "sc xnpc": the anchor and the counters.</summary>
        public static string Report()
        {
            string anchor = anchors < 0 ? "not run" : anchors == 1 ? "anchored" : "NOT anchored (" + anchors + " hits)";
            return "dialog pager: " + (ScConfig.DialogPager ? "on" : "off (DialogPager false)") + ", " + anchor + ", " + pagedRefreshes
                   + " paged refresh(es), " + flips + " flip(s), " + failures + " failure(s)";
        }

        private static bool KeyboardInput()
        {
            try
            {
                return PlatformManager.NativePlatform != null && PlatformManager.NativePlatform.Input != null
                       && PlatformManager.NativePlatform.Input.CurrentInputStyle == PlayerInputManager.InputStyle.Keyboard;
            }
            catch (Exception)
            {
                return true;
            }
        }

        // ------------------------------------------------------------------ the patches ----

        /// <summary>P1: Page(list, this) right after Update's only GetResponses() call; fail loud and leave vanilla otherwise.</summary>
        [HarmonyPatch(typeof(XUiC_DialogResponseList), "Update", new[] { typeof(float) })]
        public static class Patch_PagerAnchor
        {
            [HarmonyTranspiler]
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var list = new List<CodeInstruction>(instructions);
                try
                {
                    MethodInfo getResponses = AccessTools.DeclaredMethod(typeof(DialogStatement), "GetResponses", Type.EmptyTypes);
                    MethodInfo page = AccessTools.DeclaredMethod(typeof(ScDialogPager), "Page", new[] { typeof(List<BaseResponseEntry>), typeof(XUiC_DialogResponseList) });
                    int hits = 0, at = -1;
                    if (getResponses != null)
                        for (int i = 0; i < list.Count; i++)
                            if (list[i].Calls(getResponses)) { hits++; at = i; }
                    anchors = hits;
                    if (hits != 1 || page == null)
                    {
                        ScLog.Err("dialog pager: DialogStatement.GetResponses() found " + hits + " time(s) in XUiC_DialogResponseList.Update (expected once"
                                  + (page == null ? "; ScDialogPager.Page missing" : "") + ") - the pager is OFF: a talk page with more rows than the window shows only the first ones (game update?)");
                        return list;
                    }
                    list.InsertRange(at + 1, new[] { new CodeInstruction(OpCodes.Ldarg_0), new CodeInstruction(OpCodes.Call, page) });
                    ScLog.Out("dialog pager: anchored on DialogStatement.GetResponses() in XUiC_DialogResponseList.Update");
                    return list;
                }
                catch (Exception e)
                {
                    anchors = 0;
                    ScLog.Err("dialog pager: the transpiler failed (" + e.Message + ") - the pager is OFF");
                    return new List<CodeInstruction>(instructions);
                }
            }
        }

        /// <summary>P2: the wheel over any row pages the list.</summary>
        [HarmonyPatch(typeof(XUiC_DialogResponseList), "Init", new Type[0])]
        public static class Patch_PagerWheel
        {
            [HarmonyPostfix]
            public static void Postfix(XUiC_DialogResponseList __instance)
            {
                try
                {
                    if (__instance == null || __instance.entryList == null) return;
                    XUiC_DialogResponseList ui = __instance;
                    StateOf(ui);
                    for (int i = 0; i < ui.entryList.Count; i++)
                        if (ui.entryList[i] != null) ui.entryList[i].OnScroll += (_sender, _delta) => Wheel(ui, _delta);
                }
                catch (Exception e)
                {
                    ScLog.Once("dialog-pager-init", e);
                }
            }
        }

        /// <summary>P3: a new talk starts on page 1.</summary>
        [HarmonyPatch(typeof(XUiC_DialogResponseList), "OnOpen", new Type[0])]
        public static class Patch_PagerOpen
        {
            [HarmonyPostfix]
            public static void Postfix(XUiC_DialogResponseList __instance)
            {
                try
                {
                    State s = StateOf(__instance);
                    if (s == null) return;
                    s.Statement = null;
                    s.Page = 0;
                    s.Pages = 1;
                    s.Version++;
                }
                catch (Exception e)
                {
                    ScLog.Once("dialog-pager-open", e);
                }
            }
        }

        /// <summary>P4: the gamepad's right stick, right = the next page, through vanilla's paging helper.</summary>
        [HarmonyPatch(typeof(XUiC_DialogResponseList), "Update", new[] { typeof(float) })]
        public static class Patch_PagerStick
        {
            [HarmonyPostfix]
            public static void Postfix(XUiC_DialogResponseList __instance)
            {
                try
                {
                    State s;
                    if (__instance == null || !states.TryGetValue(__instance, out s) || s.Pages <= 1 || __instance.xui == null) return;
                    if (KeyboardInput()) return;
                    XUi.HandlePaging(__instance.xui, s.Next, s.Prev);
                }
                catch (Exception e)
                {
                    ScLog.Once("dialog-pager-stick", e);
                }
            }
        }
    }

    /// <summary>
    /// Round 28 (X3): the pager's "1/2" and its two arrows, top right of vanilla's windowResponses (Config/XUi_InGame/windows.xml).
    /// No state of its own: it reads ScDialogPager's for the response list above it, refreshes its bindings when that changes
    /// and hides while the page fits. Bindings answer from the constructor on (they are evaluated while the XML is parsed).
    /// </summary>
    [Preserve]
    public class XUiC_ScDialogPager : XUiController
    {
        private const string On = "255,255,255,255", Off = "110,110,110,160";
        private XUiC_DialogResponseList list;
        private int seen = -1;

        public override void Init()
        {
            base.Init();
            list = GetParentByType<XUiC_DialogResponseList>();
            if (list == null)
            {
                ScLog.Warn("dialog pager: the page indicator found no response list above it - the wheel and the stick still page, without the indicator");
                return;
            }
            XUiController prev = GetChildById("scPagePrev"), next = GetChildById("scPageNext");
            if (prev != null) prev.OnPress += (_sender, _button) => { if (_button == -1) ScDialogPager.Flip(list, -1, false); };
            if (next != null) next.OnPress += (_sender, _button) => { if (_button == -1) ScDialogPager.Flip(list, 1, false); };
            OnScroll += (_sender, _delta) => ScDialogPager.Wheel(list, _delta);
        }

        public override void Update(float _dt)
        {
            base.Update(_dt);
            int v = ScDialogPager.VersionOf(list);
            if (v == seen) return;
            seen = v;
            RefreshBindings();
        }

        public override bool GetBindingValueInternal(ref string _value, string _bindingName)
        {
            switch (_bindingName)
            {
                case "scpagershow":
                    _value = ScDialogPager.PagesOf(list) > 1 ? "true" : "false";
                    return true;
                case "scpagetext":
                {
                    int pages = ScDialogPager.PagesOf(list);
                    _value = pages > 1 ? string.Format(Localization.Get("scDialogPage"), ScDialogPager.PageOf(list) + 1, pages) : "";
                    return true;
                }
                case "scpageprevcolor":
                    _value = ScDialogPager.PageOf(list) > 0 ? On : Off;
                    return true;
                case "scpagenextcolor":
                    _value = ScDialogPager.PageOf(list) < ScDialogPager.PagesOf(list) - 1 ? On : Off;
                    return true;
            }
            return base.GetBindingValueInternal(ref _value, _bindingName);
        }
    }
}
