using System;
using System.Collections.Generic;

namespace SurvivorCommunity
{
    /// <summary>
    /// The community window's Facilities tab (round m1f, 2026-11-17), the part of XUiC_ScCommunity that serves it: the rows
    /// btnFac1..8 (a page; btnFacPrev / btnFacNext), the selected facility's detail and its action buttons btnFacDo1..3, and
    /// the member pick an action can start (Call to work, Assign guard: the Members tab shows the residents with the action's
    /// hint, and the one picked is handed to the action; any tab click cancels it). The entries themselves are ScFacilityTab's.
    /// Every binding answers with a string, before a world exists too (bindings run while the XML is parsed). Round MP2: the
    /// entries are built for this machine's player, and an action (or the pick's member) goes to the server as facility.do.
    /// </summary>
    public partial class XUiC_ScCommunity
    {
        private const int FacilitySlots = 8, FacilityFactSlots = 4, FacilityActionSlots = 3;
        private readonly List<FacilityEntry> facList = new List<FacilityEntry>();
        private string facSelected = "";
        /// <summary>Round fd2: the first row shown (the list scrolls a row per wheel notch; Prev / Next turn a page).</summary>
        private int facTop;
        private string facMessage = "";
        /// <summary>A facility action picking a resident on the Members tab (null: none).</summary>
        private FacilityAction facPick;
        /// <summary>Round MP2: the pick's entry key and button slot - what facility.do names it by (the list may be rebuilt meanwhile).</summary>
        private string facPickKey = "";
        private int facPickSlot = -1;

        /// <summary>From Init: the tab button, the rows, the pager and the action buttons (all optional in the XML).</summary>
        private void InitFacilities()
        {
            HookOptional("btnTabFacilities", () => SetTab(Tab.Facilities));
            HookOptional("btnTabActivities", () => SetTab(Tab.Facilities));     // the tab it replaced, in an older windows.xml
            for (int i = 0; i < FacilitySlots; i++)
            {
                int slot = i;
                HookOptional("btnFac" + (i + 1), () => SelectFacility(slot));
                XUiController row = GetChildById("btnFac" + (i + 1));          // round fd2: the wheel over a row (on_scroll in the XML)
                if (row != null) row.OnScroll += (_s, _delta) => ScrollFacilities(_delta);
            }
            HookOptional("btnFacPrev", () => TurnFacilities(-1));
            HookOptional("btnFacNext", () => TurnFacilities(1));
            for (int k = 0; k < FacilityActionSlots; k++) { int slot = k; HookOptional("btnFacDo" + (k + 1), () => OnFacilityDo(slot)); }
        }

        /// <summary>OnOpen: no message, no pick, the first page (the selection is kept).</summary>
        private void ResetFacilities()
        {
            facMessage = "";
            facPick = null;
            facTop = 0;
        }

        /// <summary>A facility pick still open is dropped (a tab click, the window closing). True when there was one.</summary>
        private bool EndFacilityPick()
        {
            if (facPick == null) return false;
            facPick = null;
            message = "";
            filter = Filter.All;
            filterDirty = true;
            return true;
        }

        /// <summary>
        /// Refresh: the entries again from saved state; the page and the selection kept where they still exist. Round MP2: built
        /// for this machine's own player (a client: from its copy, its position) - Build(viewer), no longer the host's.
        /// </summary>
        private void BuildFacilities()
        {
            facList.Clear();
            try { facList.AddRange(ScFacilityTab.Build(Player)); }
            catch (Exception e) { ScLog.Once("community-facilities", e); }
            facTop = Math.Max(0, Math.Min(facTop, FacMaxTop()));
            if (FacilitySelected == null && facList.Count > 0) facSelected = facList[0].Key;
        }

        private int FacPageCount() { return Math.Max(1, (facList.Count + FacilitySlots - 1) / FacilitySlots); }

        /// <summary>The last first row: the list's end fills the last row slot.</summary>
        private int FacMaxTop() { return Math.Max(0, facList.Count - FacilitySlots); }

        private FacilityEntry FacilitySelected
        {
            get
            {
                for (int i = 0; i < facList.Count; i++) if (facList[i].Key == facSelected) return facList[i];
                return null;
            }
        }

        private FacilityEntry FacilityAt(int slot)
        {
            int i = facTop + slot;
            return slot >= 0 && slot < FacilitySlots && i < facList.Count ? facList[i] : null;
        }

        private void SelectFacility(int slot)
        {
            FacilityEntry e = FacilityAt(slot);
            if (e == null) return;
            facSelected = e.Key;
            facMessage = "";
            RefreshBindingsSelfAndChildren();
        }

        private void TurnFacilities(int delta)
        {
            facTop = Math.Max(0, Math.Min(FacMaxTop(), facTop + delta * FacilitySlots));
            RefreshBindingsSelfAndChildren();
        }

        /// <summary>Round fd2: the wheel - rolled away from you (a positive delta) goes up the list, a row per notch.</summary>
        private void ScrollFacilities(float delta)
        {
            if (delta == 0f) return;
            int to = Math.Max(0, Math.Min(FacMaxTop(), facTop + (delta > 0f ? -1 : 1)));
            if (to == facTop) return;
            facTop = to;
            RefreshBindingsSelfAndChildren();
        }

        /// <summary>
        /// An action button: it acts (the outcome under the buttons), or it starts a pick on the Members tab. Round MP2: the server
        /// acts (facility.do: the entry's key, the slot and the action's id; ScActionsWindow.FacilityDo rebuilds the entry for this
        /// player there and checks it again). The WhyNot asked here is this machine's look - a client's is a hint from its copy.
        /// </summary>
        private void OnFacilityDo(int slot)
        {
            FacilityEntry e = FacilitySelected;
            EntityPlayerLocal p = Player;
            if (e == null || p == null || slot >= e.Actions.Count) return;
            FacilityAction a = e.Actions[slot];
            string why = null;
            try { why = a.WhyNot != null ? a.WhyNot() : null; }
            catch (Exception ex) { ScLog.Once("community-facility-whynot", ex); why = Localization.Get("scInternal"); }
            if (why != null)
            {
                // a dim action says why, and a pick does not open (the Fable review's finding 6)
                p.PlayOneShot("ui_denied");
                facMessage = why;
                Refresh();
                return;
            }
            if (a.Pick != null)
            {
                // the residents, picking one (Select -> TryFacilityPick)
                facPick = a;
                facPickKey = e.Key;
                facPickSlot = slot;
                facMessage = "";
                tab = Tab.Members;
                filter = Filter.AtBase;
                page = 0;
                message = a.PickHint ?? "";
                ApplyFilter();
                return;
            }
            if (a.Run == null) return;
            ScActions.Request("facility.do", new[] { slot, -1 }, new[] { e.Key, a.MatchKey }, r =>
            {
                facMessage = r.Text;
                PlaySound(r.Sound);
                AfterReply();
            });
        }

        /// <summary>
        /// Select's first look: a pick is open - the action runs on her; done, the window goes back to the facility. Round MP2: on
        /// the server (facility.do with the member); a refusal shows in the pick's line and the pick stays open.
        /// </summary>
        private bool TryFacilityPick(Member m)
        {
            FacilityAction a = facPick;
            if (a == null || a.Pick == null) { facPick = null; return false; }
            ScActions.Request("facility.do", new[] { facPickSlot, m.Id }, new[] { facPickKey, a.MatchKey }, r =>
            {
                if (!r.Ok)
                {
                    message = r.Text;                 // the reason in the pick's own line; the pick stays open
                    PlaySound(r.Sound);
                    RefreshBindingsSelfAndChildren();
                    return;
                }
                if (facPick == a)                     // a client: the pick may have been left meanwhile (another tab, the window closed)
                {
                    EndFacilityPick();
                    tab = Tab.Facilities;
                }
                facMessage = r.Text;
                AfterReply();
            });
            return true;
        }

        /// <summary>
        /// tabfacilities, facilitycount, facsummary, the rows fac&lt;1..8&gt;&lt;visible|selected|name|icon|status|statuscolor&gt;,
        /// the pager, and the detail: facname, facicon, facsub, facabout, facfact&lt;1..4&gt;[label|color|visible],
        /// facdo&lt;1..3&gt;&lt;label|color|visible&gt;, facactionsvisible, facmessage.
        /// </summary>
        private bool FacilityBinding(string name, ref string value)
        {
            if (!name.StartsWith("fac", StringComparison.Ordinal) && name != "tabfacilities") return false;
            FacilityEntry sel = FacilitySelected;
            switch (name)
            {
                case "tabfacilities": value = (tab == Tab.Facilities).ToString(); return true;
                case "facilitycount": value = ScRegistry.Ready ? Num(facList.Count) : ""; return true;     // round MP1: Ready (a client's copy)
                case "facsummary":
                {
                    if (!ScRegistry.Ready) { value = ""; return true; }       // round MP1: Ready (a client's copy)
                    Community c = ScRegistry.Community;
                    int morale = ScMorale.Compute().Facilities;
                    value = string.Format(Localization.Get("scUiFacSummary"), c.Facilities, (morale > 0 ? "+" : "") + Num(morale), c.HomeWater);
                    return true;
                }
                case "facshowingtext":
                    value = facList.Count == 0 ? "" : string.Format(Localization.Get("scWinShowing"),
                            Math.Max(0, Math.Min(FacilitySlots, facList.Count - facTop)), facList.Count);
                    return true;
                case "facpagevisible": value = (FacPageCount() > 1).ToString(); return true;
                case "facselvisible": value = (sel != null).ToString(); return true;
                case "facname": value = sel != null ? sel.Name : Localization.Get("scUiFacilities"); return true;
                case "facicon": value = sel != null ? sel.Icon : "ui_game_symbol_workbench"; return true;
                case "facsub": value = sel != null ? sel.Sub : ""; return true;
                case "facabout": value = sel != null ? sel.About : Localization.Get("scUiFacSelectHint"); return true;
                case "facactionsvisible": value = (sel != null && sel.Actions.Count > 0).ToString(); return true;
                case "facmessage": value = facMessage ?? ""; return true;
            }
            // the rows: fac<1..8><field>
            if (name.Length > 4 && name[3] >= '1' && name[3] <= '0' + FacilitySlots)
            {
                FacilityEntry e = FacilityAt(name[3] - '1');
                switch (name.Substring(4))
                {
                    case "visible": value = (e != null).ToString(); return true;
                    case "selected": value = (e != null && e.Key == facSelected).ToString(); return true;
                    case "name": value = e != null ? e.Name : ""; return true;
                    case "icon": value = e != null ? e.Icon : ""; return true;
                    case "status": value = e != null ? e.Status : ""; return true;
                    case "statuscolor": value = e != null ? e.StatusColor : ScFacilityTab.Body; return true;
                }
                return false;
            }
            // the facts: facfact<1..4>[label|color|visible]
            if (name.Length >= 8 && name.StartsWith("facfact", StringComparison.Ordinal) && name[7] >= '1' && name[7] <= '0' + FacilityFactSlots)
            {
                int k = name[7] - '1';
                FacilityFact f = sel != null && k < sel.Facts.Count ? sel.Facts[k] : null;
                switch (name.Substring(8))
                {
                    case "": value = f != null ? f.Value : ""; return true;
                    case "label": value = f != null ? f.Label : ""; return true;
                    case "color": value = f != null ? f.Color : ScFacilityTab.Body; return true;
                    case "visible": value = (f != null).ToString(); return true;
                }
                return false;
            }
            // the actions: facdo<1..3><label|color|visible>
            if (name.Length >= 6 && name.StartsWith("facdo", StringComparison.Ordinal) && name[5] >= '1' && name[5] <= '0' + FacilityActionSlots)
            {
                int k = name[5] - '1';
                FacilityAction a = sel != null && k < sel.Actions.Count ? sel.Actions[k] : null;
                switch (name.Substring(6))
                {
                    case "label": value = a != null ? a.Label : ""; return true;
                    case "color":
                        value = a == null ? ScFacilityTab.Off : a == facPick ? ScFacilityTab.Body : a.Danger ? "240,150,130,255"
                              : a.Ready ? ScFacilityTab.Good : ScFacilityTab.Off;
                        return true;
                    case "visible": value = (a != null).ToString(); return true;
                }
                return false;
            }
            return false;
        }

        /// <summary>The pick's hint (or the last refusal) in the member list's "Showing" line while a facility action picks.</summary>
        private bool FacilityPicking { get { return facPick != null; } }
    }
}
