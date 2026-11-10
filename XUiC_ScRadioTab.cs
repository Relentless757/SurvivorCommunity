using System;
using UnityEngine;
using UnityEngine.Scripting;

namespace SurvivorCommunity
{
    /// <summary>
    /// R2 (round 27, decisions 1 and 2): the Radio tab's controller - the rect scRadioTab that Config/XUi_InGame/windows.xml
    /// appends to windowPagingHeader just before W1's scCommunityTab, so it stands LEFT of the Community plate (its own plate,
    /// 4 units apart) and comes before it in the selector's tab order. Resolved as controller="SurvivorCommunity.ScRadioTab,
    /// SurvivorCommunity".
    ///
    /// The tab is the selector's like any other (a ButtonSelectable named ScRadio: pressing it opens the window group
    /// "scradio", the radio station's window, whose XUiC_ScPageGroup keeps the bar with it - "remote", over the handheld).
    /// It shows only while the local player carries a handheld radio (ScHandheld.Carries: bag or toolbelt), the host plays
    /// and HandheldRadioTab is on - never in the prefab editor. The rect and the tab bind {scradiotab}, which this controller
    /// answers from its last look (both bindings register here, the nearest controller that answers), so a change shows by
    /// refreshing its own bindings - and the selector skips the hidden tab when it pages, as it skips any hidden one.
    /// Looked at as the bar opens (OnOpen), every half second while it is open (Update: two item counts, no allocation) and
    /// by the radio page group before it asks for the tab (RefreshNow: the bar may have been closed since the radio left
    /// the bag).
    ///
    /// The gamepad LB icon (vanilla's look only): W1 put it left of the Community plate; with the Radio tab shown it moves left
    /// of this plate too. The look gives both spots as the rect's lb_shown / lb_hidden attributes ("x,y"); AshfallUI's looks
    /// give none, and there the icon stays where AshfallUI puts it.
    /// </summary>
    [Preserve]
    public class XUiC_ScRadioTab : XUiController
    {
        /// <summary>The tab bar's Radio tab: a button named like the radio's window group (the selector's buttonsByName ignores case).</summary>
        public const string TabName = "ScRadio";
        private const float PollSeconds = 0.5f;

        /// <summary>The live one (single player: one header, one tab).</summary>
        private static XUiC_ScRadioTab live;

        /// <summary>Its last look - false until the first, so the parse-time binding reads hidden (bindings run before Init).</summary>
        private bool shown;
        private float nextPoll;
        private XUiController lbIcon;
        private bool lbMoves;
        private Vector2i lbShown, lbHidden;

        public override void Init()
        {
            base.Init();
            live = this;
            try
            {
                Vector2i a, b;
                if (Spot("lb_shown", out a) && Spot("lb_hidden", out b))
                {
                    XUiController root = this;
                    while (root.Parent != null) root = root.Parent;
                    lbIcon = root.GetChildById("LB_Icon");
                    lbShown = a;
                    lbHidden = b;
                    lbMoves = lbIcon != null && lbIcon.ViewComponent != null;
                }
            }
            catch (Exception e)
            {
                ScLog.Once("radio-tab-init", e);
            }
        }

        /// <summary>A custom "x,y" attribute of the rect (the view leaves unknown attributes in CustomAttributes).</summary>
        private bool Spot(string name, out Vector2i v)
        {
            v = Vector2i.zero;
            object o;
            if (!CustomAttributes.TryGetValue(name, out o) || o == null) return false;
            string[] p = o.ToString().Split(',');
            int x, y;
            if (p.Length != 2 || !int.TryParse(p[0].Trim(), out x) || !int.TryParse(p[1].Trim(), out y)) return false;
            v = new Vector2i(x, y);
            return true;
        }

        public override void OnOpen()
        {
            shown = Wanted(xui);       // before base.OnOpen, whose binding refresh shows it
            base.OnOpen();
            MoveLb();
            nextPoll = Time.unscaledTime + PollSeconds;
        }

        public override void Update(float _dt)
        {
            base.Update(_dt);
            float now = Time.unscaledTime;
            if (now < nextPoll) return;
            nextPoll = now + PollSeconds;
            Refresh(false);
        }

        /// <summary>Looks again; shows or hides the tab when that changed (force: refresh its bindings either way).</summary>
        private bool Refresh(bool force)
        {
            try
            {
                bool want = Wanted(xui);
                if (want == shown && !force) return shown;
                bool changed = want != shown;
                shown = want;
                RefreshBindings();
                MoveLb();
                if (changed) ScLog.Verbose("radio tab: " + (shown ? "shown (a handheld radio is carried)" : "hidden"));
            }
            catch (Exception e)
            {
                ScLog.Once("radio-tab-refresh", e);
            }
            return shown;
        }

        private void MoveLb()
        {
            if (!lbMoves || lbIcon == null || lbIcon.ViewComponent == null) return;
            lbIcon.ViewComponent.Position = shown ? lbShown : lbHidden;
        }

        /// <summary>The radio page group, before it asks the selector for the tab: the live tab's look, made current. Its state.</summary>
        public static bool RefreshNow()
        {
            return live != null && live.Refresh(true);
        }

        /// <summary>Should the tab show: HandheldRadioTab, the host, not the prefab editor, a living player carrying a handheld radio.</summary>
        public static bool Wanted(XUi xui)
        {
            if (!ScConfig.HandheldRadioTab || !ScCommunity.IsHost) return false;
            if (PrefabEditModeManager.Instance != null && PrefabEditModeManager.Instance.IsActive()) return false;
            EntityPlayerLocal p = xui != null && xui.playerUI != null ? xui.playerUI.entityPlayer : null;
            return p != null && !p.IsDead() && ScHandheld.Carries(p);
        }

        public override bool GetBindingValueInternal(ref string _value, string _bindingName)
        {
            if (_bindingName == "scradiotab")
            {
                _value = shown ? "true" : "false";
                return true;
            }
            return base.GetBindingValueInternal(ref _value, _bindingName);
        }
    }
}
