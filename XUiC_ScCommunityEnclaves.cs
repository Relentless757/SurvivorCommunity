using System;
using System.Collections.Generic;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round en1 (2026-11-28): the community window's ENCLAVES tab, the part of XUiC_ScCommunity that serves it - and the no-community
    /// page's enclave list, which shows the same rows (ScEnclaveList's entries: the record in scope's, a personal record's too).
    /// The tab: a page of rows btnEnc1..6 (btnEncPrev / btnEncNext), the selected enclave's detail (four facts, the standing bar, what
    /// the level means) and btnEncMark. The no-community page: rows btnNcEnc1..6 on its pager (btnNcEncPrev / btnNcEncNext). A row
    /// clicked marks its enclave with a vanilla waypoint, or takes the mark away when it has one (ScEnclaveList.ToggleMark: this
    /// machine only, nothing goes to the server); on the tab it is selected too. On the no-community page a marked row is lit.
    /// Every binding answers with a string, before a world exists too (bindings run while the XML is parsed).
    /// </summary>
    public partial class XUiC_ScCommunity
    {
        /// <summary>The generator's ENC_ROWS: the rows of a page, on the tab and on the no-community page.</summary>
        public const int EnclaveSlots = 6, EnclaveFactSlots = 4;
        private readonly List<EnclaveEntry> encList = new List<EnclaveEntry>();
        private int encPage;
        /// <summary>The tab's selected enclave (its id; -1 none): kept while it is still listed.</summary>
        private int encSelected = -1;
        private string encMessage = "";

        /// <summary>From Init: the tab button, both lists' rows, the tab's pager and the detail's button (all optional in the XML).</summary>
        private void InitEnclaves()
        {
            HookOptional("btnTabEnclaves", () => SetTab(Tab.Enclaves));
            for (int i = 0; i < EnclaveSlots; i++)
            {
                int slot = i;
                HookOptional("btnEnc" + (i + 1), () => OnEnclaveRow(slot, false));
                HookOptional("btnNcEnc" + (i + 1), () => OnEnclaveRow(slot, true));
            }
            HookOptional("btnEncPrev", () => TurnPage(ref encPage, -1, encList.Count, EnclaveSlots));
            HookOptional("btnEncNext", () => TurnPage(ref encPage, 1, encList.Count, EnclaveSlots));
            HookOptional("btnEncMark", OnEnclaveMark);
        }

        /// <summary>OnOpen: the first page, no message (the selection is kept).</summary>
        private void ResetEnclaves()
        {
            encPage = 0;
            encMessage = "";
        }

        /// <summary>
        /// Refresh: the entries again from the record in scope (a client: its copy and the derived block), measured from this machine's
        /// player; the pages and the selection kept where they still exist; the marks read from the player's waypoints.
        /// </summary>
        private void BuildEnclaves()
        {
            encList.Clear();
            if (ScRegistry.Ready && ScConfig.Enclaves)
            {
                try
                {
                    EntityPlayerLocal pl = Player;
                    encList.AddRange(ScEnclaveList.Build(pl != null ? pl.position : Vector3.zero));
                    HashSet<int> marked = ScEnclaveList.MarkedIds(pl, encList);
                    foreach (EnclaveEntry x in encList) x.Marked = marked.Contains(x.Id);
                }
                catch (Exception e)
                {
                    ScLog.Once("community-enclaves", e);
                }
            }
            encPage = ClampPage(encPage, encList.Count, EnclaveSlots);
            ncEncPage = ClampPage(ncEncPage, encList.Count, EnclaveSlots);
            if (EnclaveSelected == null) encSelected = encList.Count > 0 ? encList[0].Id : -1;
        }

        private EnclaveEntry EnclaveSelected
        {
            get
            {
                for (int i = 0; i < encList.Count; i++) if (encList[i].Id == encSelected) return encList[i];
                return null;
            }
        }

        private EnclaveEntry EnclaveAt(int page, int slot)
        {
            int i = page * EnclaveSlots + slot;
            return slot >= 0 && slot < EnclaveSlots && i >= 0 && i < encList.Count ? encList[i] : null;
        }

        /// <summary>A row clicked: the tab selects it; both mark it, or take its mark away.</summary>
        private void OnEnclaveRow(int slot, bool noCommunityPage)
        {
            EnclaveEntry x = EnclaveAt(noCommunityPage ? ncEncPage : encPage, slot);
            if (x == null) return;
            if (!noCommunityPage) encSelected = x.Id;
            ToggleEnclaveMark(x);
        }

        /// <summary>The detail's button: the selected enclave's mark, on or off.</summary>
        private void OnEnclaveMark()
        {
            EnclaveEntry x = EnclaveSelected;
            if (x != null) ToggleEnclaveMark(x);
        }

        private void ToggleEnclaveMark(EnclaveEntry x)
        {
            try
            {
                Enclave e = ScEnclaves.ById(x.Id);
                EntityPlayerLocal pl = Player;
                if (e == null || pl == null) return;
                x.Marked = ScEnclaveList.ToggleMark(pl, e);
                encMessage = string.Format(Localization.Get(x.Marked ? "scUiEncMarked" : "scUiEncUnmarked"), x.Name);
            }
            catch (Exception ex)
            {
                ScLog.Once("community-enclave-mark", ex);
                encMessage = Localization.Get("scInternal");
            }
            RefreshBindingsSelfAndChildren();
        }

        /// <summary>
        /// tabenclaves, enclavecount, encsummary, encempty, encshowingtext, encpagevisible, the tab's rows encrow&lt;1..6&gt;&lt;field&gt;,
        /// the detail (encselvisible, encselname, encselicon, encselsub, encselbarvisible, encselfill, encselpoints, encselnobarvisible,
        /// encselnobar, encselabout, encselfact&lt;1..4&gt;[label|color], encmarklabel, encmessage), and the no-community page's list
        /// (ncencsummary, ncencempty, ncencshowing, ncencpagevisible, rows ncenc&lt;1..6&gt;&lt;field&gt;). A row's fields: visible, selected,
        /// name, level, levelcolor, fill, barvisible, points, nobarvisible, nobar, info, badge, badgecolor.
        /// </summary>
        private bool EnclaveBinding(string name, ref string value)
        {
            if (!name.StartsWith("enc", StringComparison.Ordinal) && !name.StartsWith("ncenc", StringComparison.Ordinal)
                && name != "tabenclaves" && name != "enclavecount") return false;
            EnclaveEntry sel = EnclaveSelected;
            switch (name)
            {
                case "tabenclaves": value = (tab == Tab.Enclaves).ToString(); return true;
                case "enclavecount": value = ScRegistry.Ready ? Num(encList.Count) : ""; return true;
                case "encsummary": value = encList.Count > 0 ? string.Format(Localization.Get("scUiEncSummary"), encList.Count) : ""; return true;
                case "encempty": value = (ScRegistry.Ready && encList.Count == 0).ToString(); return true;
                case "encshowingtext": value = Showing(encPage, encList.Count, EnclaveSlots); return true;
                case "encpagevisible": value = (Pages(encList.Count, EnclaveSlots) > 1).ToString(); return true;
                case "ncencsummary": value = encList.Count > 0 ? string.Format(Localization.Get("scUiEncSummary"), encList.Count) : ""; return true;
                case "ncencempty": value = (encList.Count == 0).ToString(); return true;
                case "ncencshowing": value = Showing(ncEncPage, encList.Count, EnclaveSlots); return true;
                case "ncencpagevisible": value = (Pages(encList.Count, EnclaveSlots) > 1).ToString(); return true;
                case "encselvisible": value = (sel != null).ToString(); return true;
                case "encselname": value = sel != null ? sel.Name : Localization.Get("scUiEnclaves"); return true;
                case "encselicon": value = sel != null && sel.Hostile ? ScEnclaveList.IconHostile : ScEnclaveList.IconEnclave; return true;
                case "encselsub": value = sel != null ? sel.Sub : ""; return true;
                case "encselbarvisible": value = (sel != null && !sel.Hostile).ToString(); return true;
                case "encselfill": value = sel != null ? Fill(sel.Fill) : "0"; return true;
                case "encselpoints": value = sel != null ? sel.Points : ""; return true;
                case "encselnobarvisible": value = (sel != null && sel.Hostile).ToString(); return true;
                case "encselnobar": value = sel != null ? sel.NoBar : ""; return true;
                case "encselabout": value = sel != null ? sel.About : Localization.Get("scUiEncSelectHint"); return true;
                case "encmarklabel": value = Localization.Get(sel != null && sel.Marked ? "scUiEncUnmark" : "scUiEncMark"); return true;
                case "encmessage": value = encMessage ?? ""; return true;
            }
            // the facts: encselfact<1..4>[label|color]
            if (name.Length >= 11 && name.StartsWith("encselfact", StringComparison.Ordinal) && name[10] >= '1' && name[10] <= '0' + EnclaveFactSlots)
                return EnclaveFact(sel, name[10] - '1', name.Substring(11), ref value);
            int k;
            string field;
            if (Slot(name, "encrow", EnclaveSlots, out k, out field))
            {
                EnclaveEntry x = EnclaveAt(encPage, k);
                return EnclaveRowField(x, x != null && x.Id == encSelected, field, ref value);
            }
            if (Slot(name, "ncenc", EnclaveSlots, out k, out field))
            {
                EnclaveEntry x = EnclaveAt(ncEncPage, k);
                return EnclaveRowField(x, x != null && x.Marked, field, ref value);
            }
            return false;
        }

        /// <summary>The detail's facts: standing, the next level, where, a request or favor.</summary>
        private static bool EnclaveFact(EnclaveEntry sel, int k, string field, ref string value)
        {
            string[] labels = { "scUiEncFactStanding", "scUiEncFactNext", "scUiEncFactWhere", "scUiEncFactRequest" };
            if (k < 0 || k >= EnclaveFactSlots) return false;
            switch (field)
            {
                case "label": value = Localization.Get(labels[k]); return true;
                case "color":
                    value = sel == null ? ScEnclaveList.ColorBody : k == 0 ? sel.LevelColor : k == 3 && sel.Badge != ScEnclaveList.BadgeNone ? sel.BadgeColor
                          : ScEnclaveList.ColorBody;
                    return true;
                case "":
                    value = sel == null ? "" : k == 0 ? sel.StandingText : k == 1 ? sel.NextText : k == 2 ? sel.Where : sel.RequestText;
                    return true;
            }
            return false;
        }

        private static bool EnclaveRowField(EnclaveEntry x, bool lit, string field, ref string value)
        {
            switch (field)
            {
                case "visible": value = (x != null).ToString(); return true;
                case "selected": value = lit.ToString(); return true;
                case "name": value = x != null ? x.Name : ""; return true;
                case "level": value = x != null ? x.Level : ""; return true;
                case "levelcolor": value = x != null ? x.LevelColor : ScEnclaveList.ColorDim; return true;
                case "fill": value = x != null ? Fill(x.Fill) : "0"; return true;
                case "barvisible": value = (x != null && !x.Hostile).ToString(); return true;
                case "points": value = x != null ? x.Points : ""; return true;
                case "nobarvisible": value = (x != null && x.Hostile).ToString(); return true;
                case "nobar": value = x != null ? x.NoBar : ""; return true;
                case "info": value = x != null ? x.Info : ""; return true;
                case "badge": value = x != null ? x.BadgeText : ""; return true;
                case "badgecolor": value = x != null ? x.BadgeColor : ScEnclaveList.ColorDim; return true;
            }
            return false;
        }
    }
}
