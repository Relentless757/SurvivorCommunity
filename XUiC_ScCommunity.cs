using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Scripting;

namespace SurvivorCommunity
{
    /// <summary>
    /// The community window (Config/XUi_InGame/windows.xml, group "sccommunity"): the stockpile, the member list
    /// (search, filter, pages, portraits), the selected survivor (health, mood, skills, specialty, traits, orders)
    /// and community morale with what helps and hurts it. Opened by ScCommunity.OpenWindow from the ESC menu
    /// entry, the land claim radial and a member's dialog.
    ///
    /// The bindings serve two layouts: the original window and the AshfallUI layout (spec
    /// sc_community_window_spec.md). Controls the XML may not have (searchInput, btnFilter, btnRun, the portrait
    /// textures) are optional. Rules: resolved as controller="SurvivorCommunity.ScCommunity, SurvivorCommunity"
    /// (XUiC_ inserted after the last '.'); rows are hand-authored rects with their own controller; EVERY binding
    /// returns a non-null string and true (bindings are evaluated while the XML is parsed, before Init and before
    /// a world exists; a null there hangs "loading UI"). Numbers go out as plain strings, fills as 0..1 with the
    /// invariant culture, colours as numeric RGBA, icons as vanilla sprite names.
    ///
    /// The Outposts tab (btnTabOutposts) swaps the member list and the survivor detail for the outposts
    /// (XUiC_ScOutpostRow), the selected outpost and the network summary. ScCommunity.OpenOutposts opens the
    /// window straight onto it (the outpost claim's radial).
    /// </summary>
    [Preserve]
    public class XUiC_ScCommunity : XUiController
    {
        /// <summary>Rows per page when the XML has none yet (the page size is the number of row controllers).</summary>
        public const int RowsPerPage = 8;
        private const float RefreshSeconds = 2f;
        private const int MoralePartSlots = 4;
        private const int TraitSlots = 3;

        public enum Filter { All, AtBase, Following, OnRun, Injured, Posted }
        private static readonly string[] FilterKeys = { "scFilterAll", "scFilterAtBase", "scFilterFollowing", "scFilterOnRun", "scFilterInjured", "scFilterPosted" };

        private XUiC_ScMemberRow[] rows;
        private readonly List<Member> all = new List<Member>();      // every member, sorted
        private readonly List<Member> list = new List<Member>();     // after filter + search: what the rows page through
        private int page;
        private int selectedId = -1;
        private string message = "";
        private float nextRefresh;
        private float confirmLeaveUntil;
        private bool runPicking;
        private Filter filter = Filter.All;
        private string search = "";
        private bool filterDirty;
        private XUiC_TextInput searchInput;
        private XUiV_Texture selPortrait;
        private bool selHasPortrait;
        private int portraitVersion = -1;
        private string communityName = "";

        // ---- the Outposts tab ----
        /// <summary>Set before the window opens: 0 = members tab, -1 = the Outposts tab, n = that outpost selected.</summary>
        public static int PendingOutpost;
        private bool outpostTab;
        private XUiC_ScOutpostRow[] opRows;
        private readonly List<Outpost> opList = new List<Outpost>();
        private int opPage;
        private int opSelectedId = -1;
        private string opMessage = "";
        private float confirmDisbandUntil;
        private readonly float[] netYield = new float[5];
        private bool radioOnline;
        /// <summary>Assign mode: the Members tab is picking someone to post at this outpost (-1 = off).</summary>
        private int assignOutpostId = -1;

        private struct Part
        {
            public int Value;
            public string Label;
            public string Icon;
        }
        private readonly List<Part> helping = new List<Part>();
        private readonly List<Part> hurting = new List<Part>();

        public EntityPlayerLocal Player
        {
            get { return (xui != null && xui.playerUI != null) ? xui.playerUI.entityPlayer : null; }
        }

        private int PageSize { get { return rows != null && rows.Length > 0 ? rows.Length : RowsPerPage; } }

        public override void Init()
        {
            base.Init();
            try
            {
                rows = GetChildrenByType<XUiC_ScMemberRow>();
                if (rows == null || rows.Length == 0)
                    ScLog.Err("community window: no member rows (controller ScMemberRow) in windows.xml");
                if (rows != null) for (int i = 0; i < rows.Length; i++) rows[i].Bind(this);
                Hook("btnClose", () => Close());
                Hook("btnStorage", () => { Close(); ScStorage.OpenWindow(Player); });
                Hook("btnPrev", () => Turn(-1));
                Hook("btnNext", () => Turn(1));
                Hook("btnFollow", OnFollow);
                Hook("btnHome", OnHome);
                Hook("btnLeave", OnLeave);
                Hook("btnRunFood", () => OnRun(Resource.Food));
                Hook("btnRunMeds", () => OnRun(Resource.Meds));
                Hook("btnRunAmmo", () => OnRun(Resource.Ammo));
                Hook("btnRunMaterials", () => OnRun(Resource.Materials));
                Hook("btnRunFuel", () => OnRun(Resource.Fuel));
                // the newer layout's controls: optional, the original window has none of them
                HookOptional("btnRun", OnRunToggle);
                HookOptional("btnFilter", OnFilterCycle);
                // the Outposts tab (optional: an older windows.xml has none of it)
                opRows = GetChildrenByType<XUiC_ScOutpostRow>();
                if (opRows != null) for (int i = 0; i < opRows.Length; i++) opRows[i].Bind(this);
                HookOptional("btnTabMembers", () => SetTab(false));
                HookOptional("btnTabOutposts", () => SetTab(true));
                HookOptional("btnOpPrev", () => TurnOutposts(-1));
                HookOptional("btnOpNext", () => TurnOutposts(1));
                HookOptional("btnOpTrack", OnOutpostTrack);
                HookOptional("btnOpDisband", OnOutpostDisband);
                HookOptional("btnOpAssign", OnOutpostAssign);
                HookOptional("btnOpUpgrade", OnOutpostUpgrade);
                HookOptional("btnOpMode", () =>
                {
                    Outpost o = OutpostSelected;
                    if (o == null || !o.Holding) return;
                    ScOutpostPerks.CycleMode(o);
                    ScRegistry.SaveAtomic();
                    opMessage = string.Format(Localization.Get("scOpModeSet"), o.Name, ScOutpostPerks.ModeLabel(o));
                    Refresh();
                });
                HookOptional("btnStaff1Recall", () => OnStaffRecall(0));
                HookOptional("btnStaff2Recall", () => OnStaffRecall(1));
                searchInput = GetChildById("searchInput") as XUiC_TextInput;
                if (searchInput != null) searchInput.OnChangeHandler += OnSearchChanged;
                XUiController sp = GetChildById("selPortrait");
                selPortrait = sp != null ? sp.ViewComponent as XUiV_Texture : null;
                bool anyPortrait = selPortrait != null;
                if (rows != null) for (int i = 0; i < rows.Length; i++) anyPortrait |= rows[i].HasPortraitView;
                if (anyPortrait) ScPortraits.Wanted = true;   // the original layout has none: no pictures are shot for it
            }
            catch (Exception e)
            {
                ScLog.Once("community-window-init", e);
            }
        }

        private void Hook(string id, Action action)
        {
            XUiController c = GetChildById(id);
            if (c == null) { ScLog.Err("community window: no control named '" + id + "' in windows.xml"); return; }
            c.OnPress += (s, m) => { if (m == -1) action(); };   // -1 = left mouse button (NGUI touch id)
        }

        private void HookOptional(string id, Action action)
        {
            XUiController c = GetChildById(id);
            if (c != null) c.OnPress += (s, m) => { if (m == -1) action(); };
        }

        public override void OnOpen()
        {
            base.OnOpen();
            try
            {
                page = 0;
                message = "";
                confirmLeaveUntil = 0f;
                runPicking = false;
                opPage = 0;
                opMessage = "";
                confirmDisbandUntil = 0f;
                assignOutpostId = -1;
                outpostTab = PendingOutpost != 0;
                if (PendingOutpost > 0) opSelectedId = PendingOutpost;
                PendingOutpost = 0;
                search = "";
                if (searchInput != null && !string.IsNullOrEmpty(searchInput.Text)) searchInput.Text = "";
                if (xui != null && xui.playerUI != null && xui.playerUI.CursorController != null)
                {
                    xui.playerUI.CursorController.Locked = false;
                    xui.playerUI.CursorController.SetCursorHidden(false);
                }
                communityName = CommunityName();
                if (ScCommunity.IsHost) ScPortraits.Prune();
                Refresh();
            }
            catch (Exception e)
            {
                ScLog.Once("community-window-open", e);
            }
        }

        public override void OnClose()
        {
            base.OnClose();
            try
            {
                list.Clear();
                all.Clear();
                runPicking = false;
                if (selPortrait != null) selPortrait.Texture = null;
                if (rows != null) for (int i = 0; i < rows.Length; i++) rows[i].ClearPortrait();
                selHasPortrait = false;
                portraitVersion = -1;
            }
            catch (Exception e)
            {
                ScLog.Once("community-window-close", e);
            }
        }

        public override void Update(float _dt)
        {
            // before base.Update: the views update in there, so textures set now are drawn this frame together with
            // the bindings (after it, a portrait or a filter change would show a frame late, blanking for a frame)
            try
            {
                if (IsOpen())
                {
                    if (Time.unscaledTime >= nextRefresh) Refresh();
                    else if (filterDirty) ApplyFilter();
                    if (ScPortraits.Version != portraitVersion)
                    {
                        portraitVersion = ScPortraits.Version;
                        ApplyPortraits();
                        RefreshBindingsSelfAndChildren();
                    }
                }
            }
            catch (Exception e)
            {
                ScLog.Once("community-window-update", e);
            }
            base.Update(_dt);
        }

        private bool IsOpen()
        {
            return xui != null && xui.playerUI != null && xui.playerUI.windowManager != null
                   && xui.playerUI.windowManager.IsWindowOpen(ScCommunity.WindowGroup);
        }

        private void Close()
        {
            try
            {
                if (xui != null && xui.playerUI != null && xui.playerUI.windowManager != null)
                    xui.playerUI.windowManager.Close(ScCommunity.WindowGroup);
            }
            catch (Exception e)
            {
                ScLog.Once("community-window-close", e);
            }
        }

        // ---- data ----

        private void Refresh()
        {
            nextRefresh = Time.unscaledTime + RefreshSeconds;
            // beds / facilities / crops straight from the loaded chunks, so a bed just built counts now
            if (ScCommunity.IsHost) ScScan.RunIfDue(RefreshSeconds * 0.5f);
            ScMorale.Recompute();
            BuildMoraleParts();
            BuildOutposts();
            all.Clear();
            all.AddRange(ScRegistry.Members.Values);
            all.Sort((a, b) =>
            {
                int s = ((int)a.Status).CompareTo((int)b.Status);
                return s != 0 ? s : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            ApplyFilter();
        }

        /// <summary>Filter + search over the sorted members, then the page. The selection survives a filter that hides it.</summary>
        private void ApplyFilter()
        {
            filterDirty = false;
            list.Clear();
            string q = (search ?? "").Trim();
            for (int i = 0; i < all.Count; i++)
            {
                Member m = all[i];
                if (!Matches(m, filter)) continue;
                if (q.Length > 0 && (m.Name ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
                list.Add(m);
            }
            int pages = PageCount();
            if (page >= pages) page = Math.Max(0, pages - 1);
            if (selectedId >= 0 && Selected == null) { selectedId = -1; runPicking = false; }
            if (runPicking && !CanSendSelected(Selected)) runPicking = false;   // followed / sent / hurt since: no stale "Cancel"
            if (rows != null)
            {
                int size = PageSize;
                for (int i = 0; i < rows.Length; i++)
                {
                    int idx = page * size + i;
                    rows[i].SetData(idx < list.Count ? list[idx] : null);
                }
            }
            ApplyPortraits();
            RefreshBindingsSelfAndChildren();
        }

        private static bool Matches(Member m, Filter f)
        {
            switch (f)
            {
                case Filter.AtBase: return m.Status == MemberStatus.Resident;
                case Filter.Following: return m.Status == MemberStatus.Following || m.Status == MemberStatus.PendingStation;
                case Filter.OnRun: return m.Status == MemberStatus.OnRun;
                case Filter.Injured: return m.Injured;
                case Filter.Posted: return m.Status == MemberStatus.Garrison;
                default: return true;
            }
        }

        private int PageCount() { int size = PageSize; return Math.Max(1, (list.Count + size - 1) / size); }

        private int ShownOnPage() { return Math.Max(0, Math.Min(PageSize, list.Count - page * PageSize)); }

        private Member Selected { get { return selectedId >= 0 ? ScRegistry.GetMember(selectedId) : null; } }

        private void Turn(int delta)
        {
            page = Math.Max(0, Math.Min(PageCount() - 1, page + delta));
            ApplyFilter();
        }

        public void Select(Member m)
        {
            if (m == null) return;
            if (assignOutpostId >= 0 && TryAssign(m)) return;
            selectedId = m.Id;
            message = "";
            confirmLeaveUntil = 0f;
            runPicking = false;
            ScPortraits.Request(m, true);
            ApplyPortraits();
            RefreshBindingsSelfAndChildren();
        }

        public bool IsSelected(Member m) { return m != null && m.Id == selectedId; }

        private void OnSearchChanged(XUiController _sender, string _text, bool _changeFromCode)
        {
            search = _text ?? "";
            page = 0;
            filterDirty = true;
        }

        private void OnFilterCycle()
        {
            filter = (Filter)(((int)filter + 1) % FilterKeys.Length);
            page = 0;
            filterDirty = true;
        }

        private void OnRunToggle()
        {
            Member m = Selected;
            if (m == null) return;
            if (!runPicking)
            {
                string why;
                if (!ScRuns.CanSend(m, ScCommunity.FindNpc(m.Id), out why))
                {
                    message = Localization.Get(why ?? "scInternal");
                    EntityPlayerLocal p = Player;
                    if (p != null) p.PlayOneShot("ui_denied");
                    RefreshBindingsSelfAndChildren();
                    return;
                }
            }
            runPicking = !runPicking;
            RefreshBindingsSelfAndChildren();
        }

        // ---- the Outposts tab ----

        private void SetTab(bool outposts)
        {
            if (assignOutpostId >= 0) { assignOutpostId = -1; message = ""; filter = Filter.All; filterDirty = true; }   // any tab click ends assign mode
            if (outpostTab == outposts) { RefreshBindingsSelfAndChildren(); return; }
            outpostTab = outposts;
            opMessage = "";
            confirmDisbandUntil = 0f;
            if (outposts) BuildOutposts();
            RefreshBindingsSelfAndChildren();
        }

        /// <summary>Held outposts in the order they were established, then the lost ones; the network's yield.</summary>
        private void BuildOutposts()
        {
            opList.Clear();
            for (int i = 0; i < 5; i++) netYield[i] = 0f;
            radioOnline = false;
            if (!ScRegistry.Loaded) { ApplyOutpostRows(); return; }
            foreach (Outpost o in ScRegistry.Outposts) if (o.Holding) opList.Add(o);
            opList.Sort((a, b) => a.Id.CompareTo(b.Id));
            foreach (Outpost o in ScRegistry.Outposts) if (!o.Holding) opList.Add(o);
            foreach (Outpost o in opList)
            {
                if (!o.Holding) continue;
                float[] y = ScOutposts.Estimate(o);
                for (int i = 0; i < 5; i++) netYield[i] += y[i];
            }
            radioOnline = ScOutposts.RadioOnlineAnywhere();
            if (opSelectedId >= 0 && OutpostSelected == null) opSelectedId = -1;
            ApplyOutpostRows();
        }

        private int OpPageSize { get { return opRows != null && opRows.Length > 0 ? opRows.Length : 6; } }

        private int OpPageCount() { int size = OpPageSize; return Math.Max(1, (opList.Count + size - 1) / size); }

        private void ApplyOutpostRows()
        {
            int pages = OpPageCount();
            if (opPage >= pages) opPage = Math.Max(0, pages - 1);
            if (opRows == null) return;
            int size = OpPageSize;
            for (int i = 0; i < opRows.Length; i++)
            {
                int idx = opPage * size + i;
                opRows[i].SetData(idx < opList.Count ? opList[idx] : null);
            }
        }

        private void TurnOutposts(int delta)
        {
            opPage = Math.Max(0, Math.Min(OpPageCount() - 1, opPage + delta));
            ApplyOutpostRows();
            RefreshBindingsSelfAndChildren();
        }

        private Outpost OutpostSelected
        {
            get
            {
                if (opSelectedId < 0) return null;
                foreach (Outpost o in ScRegistry.Outposts) if (o.Id == opSelectedId) return o;
                return null;
            }
        }

        public void SelectOutpost(Outpost o)
        {
            if (o == null) return;
            opSelectedId = o.Id;
            opMessage = "";
            confirmDisbandUntil = 0f;
            RefreshBindingsSelfAndChildren();
        }

        public bool IsOutpostSelected(Outpost o) { return o != null && o.Id == opSelectedId; }

        /// <summary>The spot "Track on map" marks: the POI's centre at the claim's height.</summary>
        private static Vector3i TrackPoint(Outpost o)
        {
            Vector3 c = o.Centre;
            return new Vector3i(Mathf.FloorToInt(c.x), o.Flag.y, Mathf.FloorToInt(c.z));
        }

        private void OnOutpostTrack()
        {
            Outpost o = OutpostSelected;
            EntityPlayerLocal p = Player;
            if (o == null || p == null) return;
            Vector3i at = TrackPoint(o);
            if (p.markerPosition == at)
            {
                p.markerPosition = Vector3i.zero;
                opMessage = Localization.Get("scUiOpUntracked");
            }
            else
            {
                p.navMarkerHidden = false;
                p.markerPosition = at;       // vanilla's own waypoint: compass + map, cleared from the map as usual
                opMessage = string.Format(Localization.Get("scUiOpTracked"), o.Name);
            }
            RefreshBindingsSelfAndChildren();
        }

        private void OnOutpostDisband()
        {
            Outpost o = OutpostSelected;
            EntityPlayerLocal p = Player;
            if (o == null || p == null) return;
            if (!ScCommunity.IsHost) { opMessage = Localization.Get("scHostOnly"); RefreshBindingsSelfAndChildren(); return; }
            if (!o.Holding)
            {
                // a lost outpost's record: forget it now instead of waiting for it to drop off
                ScRegistry.Outposts.Remove(o);
                ScRegistry.SaveAtomic();
                opSelectedId = -1;
                Refresh();
                return;
            }
            float now = Time.unscaledTime;
            if (now > confirmDisbandUntil)
            {
                confirmDisbandUntil = now + 5f;
                opMessage = string.Format(Localization.Get("scOpDisbandConfirm"), o.Name);
                RefreshBindingsSelfAndChildren();
                return;
            }
            confirmDisbandUntil = 0f;
            ScOutposts.Disband(o, p);
            opSelectedId = -1;
            opMessage = string.Format(Localization.Get("scOpDisbanded"), o.Name);
            Refresh();
        }

        /// <summary>Assign: switch to the members with a "pick someone" hint; pressed again (or any tab) cancels.</summary>
        private void OnOutpostAssign()
        {
            Outpost o = OutpostSelected;
            if (o == null) return;
            if (assignOutpostId >= 0) { assignOutpostId = -1; message = ""; filter = Filter.All; filterDirty = true; RefreshBindingsSelfAndChildren(); return; }
            if (!o.Holding || ScGarrison.StaffCount(o) >= ScGarrison.StaffCap(o))
            {
                opMessage = Localization.Get(!o.Holding ? "scOpPostLost" : "scOpPostFull");
                RefreshBindingsSelfAndChildren();
                return;
            }
            outpostTab = false;
            assignOutpostId = o.Id;
            filter = Filter.AtBase;
            page = 0;
            message = string.Format(Localization.Get("scUiOpPickStaff"), o.Name);
            ApplyFilter();
        }

        /// <summary>Assign mode: the member picked is posted (and the window returns to the outpost), or the reason shows.</summary>
        private bool TryAssign(Member m)
        {
            Outpost o = ScOutposts.ById(assignOutpostId);
            EntityPlayerLocal p = Player;
            if (o == null) { assignOutpostId = -1; return false; }
            string err;
            if (!ScGarrison.Post(m, ScCommunity.FindNpc(m.Id), o, p, out err))
            {
                message = Localization.Get(err ?? "scInternal");
                if (p != null) p.PlayOneShot("ui_denied");
                RefreshBindingsSelfAndChildren();
                return true;
            }
            assignOutpostId = -1;
            filter = Filter.All;
            message = "";
            outpostTab = true;
            opSelectedId = o.Id;
            opMessage = string.Format(Localization.Get("scOpPosted"), m.Name, o.Name);
            Refresh();
            return true;
        }

        private void OnOutpostUpgrade()
        {
            Outpost o = OutpostSelected;
            EntityPlayerLocal p = Player;
            if (o == null || p == null) return;
            string msg;
            if (ScOutpostRisk.DamagedNow(o))        // the same button repairs a damaged outpost
            {
                if (!ScOutpostRisk.Repair(o, p, out msg)) p.PlayOneShot("ui_denied");
                opMessage = msg ?? "";
                Refresh();
                return;
            }
            if (!ScOutpostPerks.Upgrade(o, p, out msg)) p.PlayOneShot("ui_denied");
            opMessage = msg ?? "";
            Refresh();
        }

        private void OnStaffRecall(int slot)
        {
            Outpost o = OutpostSelected;
            EntityPlayerLocal p = Player;
            if (o == null) return;
            List<Member> staff = ScGarrison.Staff(o);
            if (slot >= staff.Count) return;
            Member m = staff[slot];
            ScGarrison.Recall(m, p, null);
            ScRegistry.SaveAtomic();
            opMessage = string.Format(Localization.Get("scOpRecalled"), m.Name, o.Name);
            Refresh();
        }

        /// <summary>st1/st2: a posted member, an empty post, or the post level 2 unlocks.</summary>
        private static bool StaffBinding(string name, Outpost o, ref string value)
        {
            if (name.Length < 4 || !name.StartsWith("st", StringComparison.Ordinal) || (name[2] != '1' && name[2] != '2')) return false;
            int slot = name[2] - '1';
            string field = name.Substring(3);
            if (field != "name" && field != "icon" && field != "bonus" && field != "namecolor" && field != "recallvisible") return false;
            List<Member> staff = o != null ? ScGarrison.Staff(o) : new List<Member>();
            Member m = slot < staff.Count ? staff[slot] : null;
            bool open = o != null && slot < ScGarrison.StaffCap(o);
            switch (field)
            {
                case "name":
                    value = m != null ? m.Name : o == null ? "" : Localization.Get(open ? "scUiStaffEmpty" : "scUiStaffLocked");
                    return true;
                case "icon": value = m != null ? RoleIcon(m) : "ui_game_symbol_player"; return true;
                case "namecolor": value = m != null ? "226,220,208,255" : "140,134,124,255"; return true;
                case "recallvisible": value = (m != null).ToString(); return true;
                case "bonus":
                {
                    if (m == null || o == null) { value = ""; return true; }
                    float pct = ScConfig.OutpostStaffBonus + ScGarrison.MatchBonus(m, ScOutposts.PrimaryToday(o));
                    value = string.Format(Localization.Get("scUiStaffBonus"), Mathf.RoundToInt(pct * 100f));
                    return true;
                }
            }
            return false;
        }

        /// <summary>A resource's menu symbol (the same set as the resources strip).</summary>
        public static string ResourceIcon(Resource r)
        {
            switch (r)
            {
                case Resource.Meds: return "ui_game_symbol_medical";
                case Resource.Ammo: return "ui_game_symbol_shape_ammo";
                case Resource.Materials: return "ui_game_symbol_brick";
                case Resource.Fuel: return "ui_game_symbol_gas";
                default: return "ui_game_symbol_fork";
            }
        }

        public static string OutpostIcon(Outpost o)
        {
            if (o == null) return "";
            return o.Scavenge ? "ui_game_symbol_backpack" : ResourceIcon(o.Primary);
        }

        /// <summary>A row's right-hand text: the biggest resource a day, or the state when it makes nothing.</summary>
        public static string OutpostRowStatus(Outpost o)
        {
            if (o == null) return "";
            if (ScOutpostRisk.UnderAttack(o) && o.AttackHeard) return Localization.Get("scUiOpRowAttack");
            if (ScOutpostRisk.HasDemand(o)) return Localization.Get("scUiOpRowDemand");
            if (ScOutpostRisk.DamagedNow(o)) return Localization.Get("scUiOpRowDamaged");
            if (o.State != OutpostState.Active) return ScOutposts.StateLabel(o);
            float[] y = ScOutposts.ShownYield(o);
            int best = -1;
            for (int i = 0; i < 5; i++) if (y[i] > 0.005f && (best < 0 || y[i] > y[best])) best = i;
            if (best < 0) return ScOutposts.StateLabel(o);
            return string.Format(Localization.Get("scUiOpRowYield"), y[best].ToString("0.#", CultureInfo.InvariantCulture), Localization.Get("scResShort" + (Resource)best));
        }

        public static string OutpostStateColor(Outpost o)
        {
            if (o == null) return "226,220,208,255";
            if ((ScOutpostRisk.UnderAttack(o) && o.AttackHeard) || ScOutpostRisk.HasDemand(o) || ScOutpostRisk.DamagedNow(o)) return "226,108,86,255";
            switch (o.State)
            {
                case OutpostState.Active: return ScRegistry.Community.OutpostRunDown ? "220,170,70,255" : "142,208,108,255";
                case OutpostState.Idle: return "190,182,166,255";
                default: return "226,108,86,255";
            }
        }

        /// <summary>"640 m north-east of home".</summary>
        public static string OutpostDistance(Outpost o)
        {
            if (o == null || !ScZone.HasHome) return "";
            Vector3 c = o.Centre;
            Vector3i h = ScZone.Home;
            float dx = c.x - h.x, dz = c.z - h.z;
            int sector = Mathf.RoundToInt(Mathf.Repeat(Mathf.Atan2(dx, dz) * Mathf.Rad2Deg, 360f) / 45f) % 8;   // 0 = north (+z), 2 = east (+x)
            string[] keys = { "scDirN", "scDirNE", "scDirE", "scDirSE", "scDirS", "scDirSW", "scDirW", "scDirNW" };
            return string.Format(Localization.Get("scUiOpDistFmt"), Mathf.RoundToInt(Mathf.Sqrt(dx * dx + dz * dz)), Localization.Get(keys[sector]));
        }

        private string NetResource(Resource r)
        {
            float v = netYield[(int)r];
            return v > 0.005f ? string.Format(Localization.Get("scUiOpPerDayShort"), v.ToString("0.#", CultureInfo.InvariantCulture)) : "";
        }

        private bool OutpostBinding(string name, ref string value)
        {
            if (!name.StartsWith("op", StringComparison.Ordinal) && !name.StartsWith("net", StringComparison.Ordinal)
                && name != "tabmembers" && name != "taboutposts" && name != "outpostcount") return false;
            Community c = ScRegistry.Community;
            Outpost o = OutpostSelected;
            bool loaded = ScRegistry.Loaded;
            switch (name)
            {
                case "tabmembers": value = (!outpostTab).ToString(); return true;
                case "taboutposts": value = outpostTab.ToString(); return true;
                case "outpostcount": value = loaded ? ScOutposts.HeldCount() + "/" + ScOutposts.Slots() : ""; return true;
                case "opfood": value = NetResource(Resource.Food); return true;
                case "opmeds": value = NetResource(Resource.Meds); return true;
                case "opmats": value = NetResource(Resource.Materials); return true;
                case "opammo": value = NetResource(Resource.Ammo); return true;
                case "opfuel": value = NetResource(Resource.Fuel); return true;
                case "opsummary":
                    value = loaded ? string.Format(Localization.Get("scUiOpSummary"), ScOutposts.HeldCount(), ScOutposts.Slots(),
                                                   Localization.Get(radioOnline ? "scUiNetRadioOn" : "scUiNetRadioOff")) : "";
                    return true;
                case "opempty": value = (loaded && opList.Count == 0).ToString(); return true;
                case "opshowingtext":
                    value = opList.Count == 0 ? "" : string.Format(Localization.Get("scWinShowing"),
                            Math.Max(0, Math.Min(OpPageSize, opList.Count - opPage * OpPageSize)), opList.Count);
                    return true;
                case "oppagevisible": value = (OpPageCount() > 1).ToString(); return true;
                case "opselvisible": value = (o != null).ToString(); return true;
                case "opname": value = o != null ? o.Name : ""; return true;
                case "opicon": value = OutpostIcon(o); return true;
                case "opsub":
                    value = o != null ? string.Format(Localization.Get("scUiOpSub"), ScOutposts.TypeLabel(o), o.Tier, o.Day) : "";
                    return true;
                case "opstate":
                {
                    if (o == null) { value = ""; return true; }
                    value = ScOutposts.StateLabel(o);
                    string alert = ScOutpostRisk.Alert(o);
                    if (alert.Length > 0) { value = alert; return true; }
                    if (ScConfig.OutpostAttacks && o.Holding) value += " - " + string.Format(Localization.Get("scUiOpSecurity"), ScOutpostRisk.Security(o));
                    if (o.State == OutpostState.Contested) value += " - " + Localization.Get("scUiOpContestedHint");
                    else if (o.State == OutpostState.Idle) value += " - " + Localization.Get("scUiOpIdleHint");
                    else if (o.State == OutpostState.Lost) value += " - " + Localization.Get("scUiOpLostHint");
                    return true;
                }
                case "opstatecolor":
                    value = o != null && ((ScOutpostRisk.UnderAttack(o) && o.AttackHeard) || ScOutpostRisk.HasDemand(o) || ScOutpostRisk.DamagedNow(o)) ? "226,108,86,255" : OutpostStateColor(o);
                    return true;
                case "opyield":
                {
                    if (o == null) { value = ""; return true; }
                    value = ScOutposts.YieldText(o.Holding ? ScOutposts.Estimate(o) : new float[5]);
                    int built = Mathf.RoundToInt(ScOutpostPerks.InvestBonus(o) * 100f);
                    if (built > 0) value += " " + string.Format(Localization.Get("scUiOpBuiltBonus"), built);
                    if (o.StashTotal > 0) value += " " + string.Format(Localization.Get("scUiOpStashed"), o.StashTotal);
                    return true;
                }
                case "opdist": value = OutpostDistance(o); return true;
                case "opwaiting":
                    value = o == null ? "" : o.PendingTotal > 0 ? string.Format(Localization.Get("scUiOpWaitingFmt"), o.PendingTotal) : Localization.Get("scUiOpWaitingNone");
                    return true;
                case "optracklabel":
                {
                    EntityPlayerLocal p = Player;
                    value = Localization.Get(o != null && p != null && p.markerPosition == TrackPoint(o) ? "scUiOpUntrack" : "scUiOpTrack");
                    return true;
                }
                case "opdisbandlabel":
                    value = Localization.Get(o != null && !o.Holding ? "scUiOpForget" : Time.unscaledTime <= confirmDisbandUntil ? "scUiOpDisbandConfirm" : "scUiOpDisband");
                    return true;
                case "opmessage": value = opMessage ?? ""; return true;
                case "opassignlabel": value = Localization.Get(assignOutpostId >= 0 ? "scUiOpAssignCancel" : "scUiOpAssign"); return true;
                case "opupgradelabel":
                    value = o != null && ScOutpostRisk.DamagedNow(o)
                        ? string.Format(Localization.Get("scUiOpRepair"), ScOutpostPerks.Salvaged(ScConfig.OutpostRepairMats))
                        : Localization.Get("scUiOpUpgrade");
                    return true;
                case "opmodelabel": value = o != null ? string.Format(Localization.Get("scUiOpMode"), ScOutpostPerks.ModeLabel(o)) : ""; return true;
                case "opupgradecolor":
                    value = o != null && (ScOutpostRisk.DamagedNow(o) || ScOutpostPerks.CanUpgrade(o) == null) ? "142,208,108,255" : "140,134,124,255";
                    return true;
                case "oplevel": value = o != null ? ScOutpostPerks.LevelText(o) : ""; return true;
                case "opnext": value = o != null ? ScOutpostPerks.NextText(o) : ""; return true;
                case "opperks":
                {
                    string perks = loaded ? ScOutpostPerks.PerkLines() : "";
                    value = perks.Length > 0 ? perks : Localization.Get("scUiPerksNone");
                    return true;
                }
                case "netslots":
                    value = loaded ? string.Format(Localization.Get("scUiNetSlotsFmt"), ScOutposts.HeldCount(), ScOutposts.Slots(), ScConfig.OutpostMax) : "";
                    return true;
                case "netradio": value = Localization.Get(radioOnline ? "scUiNetRadioOn" : "scUiNetRadioOff"); return true;
                case "netradiocolor": value = radioOnline ? "142,208,108,255" : "226,108,86,255"; return true;
                case "netyield": value = loaded ? ScOutposts.YieldText(netYield) : ""; return true;
                case "netcosts":
                {
                    float upkeep = 0f;
                    foreach (Outpost x in opList) if (x.State == OutpostState.Active || x.State == OutpostState.Contested) upkeep += ScOutpostPerks.UpkeepFor(x);
                    float bundles = 0f;
                    for (int i = 0; i < 5; i++) bundles += netYield[i];
                    value = loaded ? string.Format(Localization.Get("scUiNetCostsFmt"),
                                                   upkeep.ToString("0.#", CultureInfo.InvariantCulture),
                                                   (ScConfig.OutpostHaulage * bundles).ToString("0.##", CultureInfo.InvariantCulture)) : "";
                    return true;
                }
                case "netstate":
                    value = !loaded || opList.Count == 0 ? Localization.Get("scUiNetNone")
                          : c.OutpostRunDown ? Localization.Get("scUiNetRunDown")
                          : c.OutpostOnFoot ? Localization.Get("scUiNetOnFoot")
                          : Localization.Get("scUiNetOk");
                    return true;
                case "netstatecolor":
                    value = loaded && (c.OutpostRunDown || c.OutpostOnFoot) ? "220,170,70,255" : "142,208,108,255";
                    return true;
            }
            return false;
        }

        // ---- portraits ----

        private void ApplyPortraits()
        {
            if (rows != null) for (int i = 0; i < rows.Length; i++) rows[i].ApplyPortrait();
            Member sel = Selected;
            Texture2D t = sel != null && selPortrait != null ? ScPortraits.Get(sel) : null;
            // by reference: Unity's == calls a destroyed texture null, and the view would keep drawing it
            if (selPortrait != null && !ReferenceEquals(selPortrait.Texture, t)) selPortrait.Texture = t;
            selHasPortrait = t != null;
        }

        // ---- orders ----

        private void OnFollow()
        {
            Member m = Selected;
            EntityPlayerLocal p = Player;
            if (m == null || p == null) return;
            EntityAliveSDX npc = ScCommunity.FindNpc(m.Id);
            string err;
            if (ScMissing.IsMissing(m))
            {
                // the body is gone from the world: respawn them at their post from their record
                string cerr;
                int before = m.Id;
                if (ScMissing.Restore(m, p, false, out cerr))
                {
                    if (selectedId == before) selectedId = m.Id;      // they come back under a new entity id
                    message = string.Format(Localization.Get("scMissingRestored"), m.Name);
                }
                else { message = Localization.Get(cerr ?? "scInternal"); p.PlayOneShot("ui_denied"); }
                Refresh();
                return;
            }
            if (ScRunners.IsPhysical(m))
            {
                // out on a physical run: turn round now
                string rerr;
                if (ScRunners.Recall(m, p, out rerr)) message = string.Format(Localization.Get("scRunRecalled"), m.Name);
                else { message = Localization.Get(rerr ?? "scInternal"); p.PlayOneShot("ui_denied"); }
                Refresh();
                return;
            }
            if (m.Status == MemberStatus.Garrison)
            {
                Outpost po = ScGarrison.PostOf(m);
                ScGarrison.Recall(m, p, "scOpRecalled");
                ScRegistry.SaveAtomic();
                message = string.Format(Localization.Get("scOpRecalled"), m.Name, po != null ? po.Name : "?");
                Refresh();
                return;
            }
            if (npc == null) err = "scNotLoaded";
            else if (m.Status == MemberStatus.Following || m.Status == MemberStatus.PendingStation) { OnHome(); return; }
            else if (ScCommunity.Follow(m, npc, p, out err)) message = string.Format(Localization.Get("scWinNowFollowing"), m.Name);
            if (err != null) { message = Localization.Get(err); p.PlayOneShot("ui_denied"); }
            Refresh();
        }

        private void OnHome()
        {
            Member m = Selected;
            EntityPlayerLocal p = Player;
            if (m == null || p == null) return;
            EntityAliveSDX npc = ScCommunity.FindNpc(m.Id);
            string err;
            if (npc == null) err = "scNotLoaded";
            else if (ScCommunity.ReturnHome(m, npc, p, out err)) message = string.Format(Localization.Get("scStationed"), m.Name);
            if (err != null) { message = Localization.Get(err); p.PlayOneShot("ui_denied"); }
            Refresh();
        }

        private void OnLeave()
        {
            Member m = Selected;
            if (m == null) return;
            float now = Time.unscaledTime;
            if (now > confirmLeaveUntil)
            {
                confirmLeaveUntil = now + 5f;
                message = Localization.Get("scWinLeaveConfirmHint");
                RefreshBindingsSelfAndChildren();
                return;
            }
            confirmLeaveUntil = 0f;
            ScCommunity.Leave(m, "scLeft");
            selectedId = -1;
            runPicking = false;
            message = string.Format(Localization.Get("scLeft"), m.Name);
            Refresh();
        }

        private void OnRun(Resource kind)
        {
            Member m = Selected;
            EntityPlayerLocal p = Player;
            if (m == null || p == null) return;
            EntityAliveSDX npc = ScCommunity.FindNpc(m.Id);
            string err;
            runPicking = false;
            if (ScRuns.Send(m, npc, kind, p, out err)) message = string.Format(Localization.Get("scRunSent"), m.Name, Localization.Get(ScNames.LocKey(kind)), m.RunEndDay);
            else { message = Localization.Get(err ?? "scInternal"); p.PlayOneShot("ui_denied"); }
            Refresh();
        }

        // ---- morale parts ----

        /// <summary>The non-zero ScMorale.Compute() terms, split into what helps (largest first) and what hurts (worst first).</summary>
        private void BuildMoraleParts()
        {
            helping.Clear();
            hurting.Clear();
            if (!ScRegistry.Loaded) return;
            ScMorale.Parts p = ScMorale.Compute();
            Community c = ScRegistry.Community;
            int members = ScRegistry.Members.Count;
            AddPart(p.Food, Localization.Get("scMoraleFood"), "ui_game_symbol_fork");
            AddPart(p.Meds, Localization.Get("scMoraleMeds"), "ui_game_symbol_medical");
            AddPart(p.Beds, string.Format(Localization.Get("scMoraleBedsCount"), c.Beds, members), "ui_game_symbol_map_bed");
            AddPart(p.Facilities, Localization.Get("scMoraleFacilities"), "ui_game_symbol_workbench");
            AddPart(p.Supplies, Localization.Get("scMoraleSupplies"), "ui_game_symbol_loot_sack");
            AddPart(p.Traits, Localization.Get("scMoraleTraits"), "ui_game_symbol_players");
            AddPart(p.Deaths, Localization.Get("scMoraleDeaths"), "ui_game_symbol_skull");
            AddPart(p.Leadership, Localization.Get("scMoraleLeadership"), "ui_game_symbol_influence");
            AddPart(p.Territory, Localization.Get("scMoraleTerritory"), "ui_game_symbol_map_town");
            helping.Sort((a, b) => b.Value.CompareTo(a.Value));
            hurting.Sort((a, b) => a.Value.CompareTo(b.Value));
        }

        private void AddPart(int value, string label, string icon)
        {
            if (value == 0) return;
            var part = new Part { Value = value, Label = label, Icon = icon };
            if (value > 0) helping.Add(part); else hurting.Add(part);
        }

        private static bool PartBinding(string name, string prefix, List<Part> parts, ref string value)
        {
            // <prefix><1..4><value|label|icon|visible>
            if (!name.StartsWith(prefix, StringComparison.Ordinal) || name.Length < prefix.Length + 2) return false;
            char d = name[prefix.Length];
            if (d < '1' || d > '0' + MoralePartSlots) return false;
            int i = d - '1';
            string field = name.Substring(prefix.Length + 1);
            bool has = i < parts.Count;
            switch (field)
            {
                case "value": value = has ? (parts[i].Value > 0 ? "+" : "") + parts[i].Value.ToString(CultureInfo.InvariantCulture) : ""; return true;
                case "label": value = has ? parts[i].Label ?? "" : ""; return true;
                case "icon": value = has ? parts[i].Icon : ""; return true;
                case "visible": value = has ? "True" : "False"; return true;
            }
            return false;
        }

        // ---- bindings ----

        public override bool GetBindingValueInternal(ref string _value, string _bindingName)
        {
            Community c = ScRegistry.Community;
            Member sel = Selected;
            if (PartBinding(_bindingName, "help", helping, ref _value)) return true;
            if (PartBinding(_bindingName, "hurt", hurting, ref _value)) return true;
            if (TraitBinding(_bindingName, sel, ref _value)) return true;
            if (OutpostBinding(_bindingName, ref _value)) return true;
            if (StaffBinding(_bindingName, OutpostSelected, ref _value)) return true;
            switch (_bindingName)
            {
                // ---- the original layout ----
                case "title":
                    _value = string.Format(Localization.Get("scWinTitle"), c.Morale, ScMorale.MoodWord(c.Morale));
                    return true;
                case "subtitle":
                    _value = string.Format(Localization.Get("scWinSubtitle"), ScRegistry.Members.Count, ScConfig.MaxMembers, c.Beds, c.Facilities, c.Crops)
                             + (c.Hungry ? " - " + Localization.Get("scWinHungry") : "");
                    return true;
                case "bedsline":
                    _value = ScScan.BedLine();
                    return true;
                case "bedscolor":
                    _value = ScScan.BedColor();
                    return true;
                case "stock":
                    _value = string.Format(Localization.Get("scWinStock"), c.Get(Resource.Food), c.Get(Resource.Meds), c.Get(Resource.Ammo), c.Get(Resource.Materials), c.Get(Resource.Fuel));
                    return true;
                case "breakdown":
                    _value = ScRegistry.Loaded ? ScMorale.Breakdown() : "";
                    return true;
                case "listheader":
                    _value = string.Format(Localization.Get("scWinListHeader"), ScRegistry.Members.Count);
                    return true;
                case "pagetext":
                    _value = ScRegistry.Members.Count == 0 ? Localization.Get("scWinNoMembers")
                           : list.Count == 0 ? Localization.Get("scWinNoMatches")
                           : string.Format(Localization.Get("scWinPage"), page + 1, PageCount());
                    return true;
                case "pagevisible":
                    _value = (PageCount() > 1).ToString();
                    return true;
                case "prevlabel": _value = Localization.Get("scWinPrev"); return true;
                case "storagelabel": _value = Localization.Get("scWinStorage"); return true;
                case "nextlabel": _value = Localization.Get("scWinNext"); return true;
                case "message": _value = message ?? ""; return true;
                case "ledgerheader": _value = Localization.Get("scWinLedger"); return true;
                case "ledger":
                {
                    var sb = new StringBuilder();
                    for (int i = 0; i < c.Ledger.Count && i < 4; i++) { if (i > 0) sb.Append('\n'); sb.Append(c.Ledger[i]); }
                    _value = sb.ToString();
                    return true;
                }

                // ---- header and resources ----
                case "communityname": _value = communityName ?? ""; return true;
                case "resfood": _value = Num(c.Get(Resource.Food)); return true;
                case "resmeds": _value = Num(c.Get(Resource.Meds)); return true;
                case "resmats": _value = Num(c.Get(Resource.Materials)); return true;
                case "resammo": _value = Num(c.Get(Resource.Ammo)); return true;
                case "resfuel": _value = Num(c.Get(Resource.Fuel)); return true;
                case "fooddays": _value = FoodDays(c); return true;

                // ---- member list ----
                case "membercount": _value = Num(ScRegistry.Members.Count); return true;
                case "showingtext":
                    _value = assignOutpostId >= 0 && !string.IsNullOrEmpty(message) ? message
                           : string.Format(Localization.Get("scWinShowing"), ShownOnPage(), list.Count);
                    return true;
                case "filterlabel": _value = Localization.Get(FilterKeys[(int)filter]); return true;

                // ---- selected survivor ----
                case "selvisible": _value = (sel != null).ToString(); return true;
                case "selname":
                    _value = sel != null ? sel.Name : Localization.Get("scWinNoSelection");
                    return true;
                case "selrole": _value = sel != null ? RoleText(sel) : ""; return true;
                case "selroleicon": _value = sel != null ? RoleIcon(sel) : ""; return true;
                case "selstatus": _value = sel != null ? StatusText(sel, false) : ""; return true;
                case "selhealth":
                case "selhealthmax":
                case "selhealthfill":
                    _value = Health(sel, _bindingName);
                    return true;
                // NPCs have no stamina stat (only PlayerEntityStats creates one): the row stays hidden
                case "selstamina": _value = ""; return true;
                case "selstaminamax": _value = ""; return true;
                case "selstaminafill": _value = "0"; return true;
                case "selstaminavisible": _value = "False"; return true;
                case "selmoodvalue": _value = sel != null ? Num(sel.Mood) : ""; return true;
                case "selmoodfill": _value = sel != null ? Fill(sel.Mood / 100f) : "0"; return true;
                case "selmoodword": _value = sel != null ? BandWord(sel.Mood) : ""; return true;
                case "skcardio": _value = SkillText(sel, Skill.Cardio); return true;
                case "skwits": _value = SkillText(sel, Skill.Wits); return true;
                case "skfighting": _value = SkillText(sel, Skill.Fighting); return true;
                case "skshooting": _value = SkillText(sel, Skill.Shooting); return true;
                case "skcardiofill": _value = SkillFillText(sel, Skill.Cardio); return true;
                case "skwitsfill": _value = SkillFillText(sel, Skill.Wits); return true;
                case "skfightingfill": _value = SkillFillText(sel, Skill.Fighting); return true;
                case "skshootingfill": _value = SkillFillText(sel, Skill.Shooting); return true;
                case "skspecname": _value = sel != null ? Localization.Get(ScNames.LocKey(sel.Specialty)) : ""; return true;
                case "skspecicon": _value = sel != null ? SpecIcon(sel.Specialty) : ""; return true;
                case "skspec": _value = sel != null && sel.Specialty != Specialty.None ? StarsText(sel.SpecLevel) : ""; return true;
                case "skspecfill": _value = sel != null && sel.Specialty != Specialty.None ? Fill(SkillFill(sel.SpecLevel, sel.SpecXp)) : "0"; return true;
                case "skspecvisible": _value = (sel != null && sel.Specialty != Specialty.None).ToString(); return true;
                case "selhasportrait": _value = selHasPortrait ? "True" : "False"; return true;
                case "selnoportrait": _value = selHasPortrait ? "False" : "True"; return true;

                // the original layout's detail panel
                case "selclass":
                    _value = sel != null ? sel.Class + " - " + ScMorale.StatusText(sel) : "";
                    return true;
                case "selmood":
                    _value = sel != null ? string.Format(Localization.Get("scWinMood"), sel.Mood, ScMorale.MoodWord(sel.Mood)) : "";
                    return true;
                case "selskills":
                    _value = sel != null ? SkillLines(sel) : Localization.Get("scWinSelectHint");
                    return true;
                case "selspec":
                    _value = sel != null ? string.Format(Localization.Get("scWinSpec"), Localization.Get(ScNames.LocKey(sel.Specialty)), sel.SpecLevel) : "";
                    return true;
                case "seltraits":
                    _value = sel != null ? string.Format(Localization.Get("scWinTraits"), TraitText(sel)) : "";
                    return true;
                case "selflags":
                    _value = sel != null ? Flags(sel, c) : "";
                    return true;
                case "selflagscolor":
                    _value = "255,140,120,255";
                    return true;
                case "selrun":
                    _value = sel != null ? ScRuns.StatusLine(sel) : "";
                    return true;

                // ---- orders ----
                case "runheader":
                    _value = string.Format(Localization.Get("scWinRunHeader"), ScRuns.Runners(), ScConfig.MaxRunners);
                    return true;
                case "runvisible":
                    _value = CanSendSelected(sel).ToString();
                    return true;
                case "runpicking":
                    _value = (runPicking && CanSendSelected(sel)).ToString();
                    return true;
                case "runlabel":
                    _value = Localization.Get(runPicking ? "scWinRunCancel" : "scWinRunSend");
                    return true;
                case "buttonsvisible":
                    _value = (sel != null).ToString();
                    return true;
                case "followlabel":
                    _value = ScMissing.IsMissing(sel) ? Localization.Get("scWinCallBack")
                           : ScRunners.IsPhysical(sel) ? Localization.Get("scWinRecall")
                           : sel != null && sel.Status == MemberStatus.Garrison ? Localization.Get("scWinRecall")
                           : sel != null && (sel.Status == MemberStatus.Following || sel.Status == MemberStatus.PendingStation)
                        ? Localization.Get("scWinHome") : Localization.Get("scWinFollow");
                    return true;
                case "followcolor":
                    _value = sel != null && sel.Status == MemberStatus.OnRun
                             && !(ScRunners.IsPhysical(sel) && (sel.RunLeg == RunLeg.Outbound || (sel.RunLeg == RunLeg.AtSite && !ScRunners.Pinned(sel))))
                        ? "70,70,70,255" : "50,80,120,255";
                    return true;
                case "homevisible":
                    _value = (sel != null && (sel.Status == MemberStatus.Following || sel.Status == MemberStatus.PendingStation)).ToString();
                    return true;
                case "homelabel": _value = Localization.Get("scWinHome"); return true;
                case "leavelabel":
                    _value = Time.unscaledTime <= confirmLeaveUntil ? Localization.Get("scWinLeaveConfirm") : Localization.Get("scWinLeave");
                    return true;
                case "leavecolor":
                    _value = Time.unscaledTime <= confirmLeaveUntil ? "150,50,50,255" : "90,40,40,255";
                    return true;

                // ---- community morale ----
                case "morale": _value = Num(c.Morale); return true;
                case "moralefill": _value = Fill(c.Morale / 100f); return true;
                case "moraleword": _value = BandWord(c.Morale).ToUpper(CultureInfo.CurrentCulture); return true;
                case "moralewordcolor": _value = BandColor(c.Morale); return true;
                case "bedscallvisible":
                    _value = (BedsShort() > 0).ToString();
                    return true;
                case "bedscalltitle":
                {
                    int n = BedsShort();
                    _value = n <= 0 ? "" : n == 1 ? Localization.Get("scBedsCallOne") : string.Format(Localization.Get("scBedsCallMany"), n);
                    return true;
                }
                case "bedscallhint":
                    _value = BedsShort() > 0 ? Localization.Get("scBedsCallHint") : "";
                    return true;

                default:
                    return base.GetBindingValueInternal(ref _value, _bindingName);
            }
        }

        private static bool CanSendSelected(Member sel)
        {
            if (sel == null) return false;
            string why;
            return ScRuns.CanSend(sel, ScCommunity.FindNpc(sel.Id), out why);
        }

        private static bool TraitBinding(string name, Member sel, ref string value)
        {
            // trait<1..3><name|desc|icon|visible>
            if (!name.StartsWith("trait", StringComparison.Ordinal) || name.Length < 7) return false;
            char d = name[5];
            if (d < '1' || d > '0' + TraitSlots) return false;
            int i = d - '1';
            string field = name.Substring(6);
            string trait = sel != null && i < sel.Traits.Count ? sel.Traits[i] : null;
            switch (field)
            {
                case "name": value = trait != null ? ScTraits.Display(trait) : ""; return true;
                case "desc": value = trait != null ? ScTraits.Describe(trait) : ""; return true;
                case "icon": value = trait != null ? TraitIcon(trait) : ""; return true;
                case "visible": value = trait != null ? "True" : "False"; return true;
            }
            return false;
        }

        private static int BedsShort()
        {
            if (!ScRegistry.Loaded) return 0;
            int m = ScRegistry.Members.Count;
            return m == 0 ? 0 : Math.Max(0, m - ScRegistry.Community.Beds);
        }

        /// <summary>"4 days remaining" from what the community eats now; "" with nobody to feed.</summary>
        private static string FoodDays(Community c)
        {
            if (!ScRegistry.Loaded || ScRegistry.Members.Count == 0) return "";
            if (c.Get(Resource.Food) <= 0) return Localization.Get("scWinFoodNone");
            float need = ScMorale.DailyFoodNeed();
            if (need <= 0.01f) return "";
            int days = Mathf.FloorToInt(c.Get(Resource.Food) / need);
            if (days <= 0) return Localization.Get("scWinFoodUnderDay");
            if (days == 1) return Localization.Get("scWinFoodOneDay");
            return string.Format(Localization.Get("scWinFoodDays"), days);
        }

        private static string Health(Member sel, string which)
        {
            EntityAliveSDX npc = sel != null ? ScCommunity.FindNpc(sel.Id) : null;
            bool live = npc != null && !npc.IsDead() && npc.Stats != null && npc.Stats.Health != null;
            switch (which)
            {
                case "selhealth": return live ? Num(npc.Health) : "-";
                case "selhealthmax": return live ? Num(npc.GetMaxHealth()) : "-";
                default: return live ? Fill(npc.Stats.Health.ValuePercentUI) : "0";
            }
        }

        private string CommunityName()
        {
            try
            {
                Community c = ScRegistry.Community;
                if (!c.HasHome) return "";
                PrefabInstance pi = ScSecurity.PoiAt(c.Home);
                return pi != null ? ScSecurity.DisplayName(pi) ?? "" : "";
            }
            catch (Exception e)
            {
                ScLog.Once("community-name", e);
                return "";
            }
        }

        // ---- shared text (rows use these too) ----

        public static string Num(int v) { return v.ToString(CultureInfo.InvariantCulture); }

        public static string Fill(float f) { return Mathf.Clamp01(f).ToString("0.###", CultureInfo.InvariantCulture); }

        private static string StarsText(int level) { return Math.Max(1, Math.Min(Member.MaxStars, level)) + " / " + Member.MaxStars; }

        private static string SkillText(Member m, Skill s) { return m != null ? StarsText(m.Stars(s)) : ""; }

        private static string SkillFillText(Member m, Skill s)
        {
            if (m == null) return "0";
            SkillState st = m.Skill(s);
            return Fill(SkillFill(st.Level, st.Xp));
        }

        /// <summary>(Level - 1 + progress to the next star) / (MaxStars - 1): XP is cumulative against ScSkills' thresholds.</summary>
        public static float SkillFill(int level, int xp)
        {
            level = Math.Max(1, Math.Min(Member.MaxStars, level));
            if (level >= Member.MaxStars) return 1f;
            int lo = ScSkills.NeededFor(level), hi = ScSkills.NeededFor(level + 1);
            float frac = hi > lo ? Mathf.Clamp01((xp - lo) / (float)(hi - lo)) : 0f;
            return Mathf.Clamp01((level - 1 + frac) / (Member.MaxStars - 1));
        }

        /// <summary>A role noun from the specialty: Medic, Cook ... ("Survivor" without one).</summary>
        public static string RoleText(Member m) { return m != null ? Localization.Get("scRole" + m.Specialty) : ""; }

        public static string RoleIcon(Member m) { return m != null ? SpecIcon(m.Specialty) : ""; }

        public static string SpecIcon(Specialty s)
        {
            switch (s)
            {
                case Specialty.Medicine: return "ui_game_symbol_medical";
                case Specialty.Cooking: return "ui_game_symbol_cookware";
                case Specialty.Mechanics: return "ui_game_symbol_wrench";
                case Specialty.Gardening: return "ui_game_symbol_crops";
                case Specialty.Chemistry: return "ui_game_symbol_chemistry";
                case Specialty.Craftsmanship: return "ui_game_symbol_hammer";
                default: return "ui_game_symbol_player";
            }
        }

        private static readonly Dictionary<string, string> traitIcons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "EatsLikeABird", "ui_game_symbol_feather" }, { "BottomlessStomach", "ui_game_symbol_hunger" }, { "Optimist", "ui_game_symbol_star" },
            { "Pessimist", "ui_game_symbol_critical" }, { "Loner", "ui_game_symbol_player" }, { "PeoplePerson", "ui_game_symbol_players" },
            { "Veteran", "ui_game_symbol_hard_target" }, { "Sharpshooter", "ui_game_symbol_long_shot" }, { "MarathonRunner", "ui_game_symbol_cardio" },
            { "QuickStudy", "ui_game_symbol_bookworm" }, { "Cowardly", "ui_game_symbol_run" }, { "Hardy", "ui_game_symbol_fortitude_mastery" },
            { "MedicTraining", "ui_game_symbol_medical" }, { "Homesteader", "ui_game_symbol_map_cabin" }, { "Tinkerer", "ui_game_symbol_tool" },
            { "Hoarder", "ui_game_symbol_backpack" }, { "Cheerful", "ui_game_symbol_trophy" }, { "Insomniac", "ui_game_symbol_clock" },
        };

        public static string TraitIcon(string trait)
        {
            string s;
            return !string.IsNullOrEmpty(trait) && traitIcons.TryGetValue(trait, out s) ? s : "ui_game_symbol_perk";   // the trait buffs' own icon
        }

        /// <summary>"At base", "Following", "Heading home", "Supply run - back day 14" (row: "Supply run - day 14"), "+ Injured".</summary>
        public static string StatusText(Member m, bool brief)
        {
            if (m == null) return "";
            string s;
            switch (m.Status)
            {
                case MemberStatus.Following: s = Localization.Get("scStFollowing"); break;
                case MemberStatus.PendingStation: s = Localization.Get("scStHeadingHome"); break;
                case MemberStatus.OnRun: s = ScRuns.ShortStatus(m, brief); break;
                case MemberStatus.Garrison: s = ScGarrison.StatusText(m, brief); break;
                case MemberStatus.Resident when ScMissing.IsMissing(m): s = Localization.Get("scStMissing"); break;
                default: s = Localization.Get("scStAtBase"); break;
            }
            if (m.Injured) s += " + " + Localization.Get("scStInjured");
            return s;
        }

        /// <summary>Community morale / a survivor's mood as a word: Thriving, Steady, Uneasy, Breaking.</summary>
        public static string BandWord(int v)
        {
            if (v >= ScConfig.HighMoraleThreshold) return Localization.Get("scBandThriving");
            if (v >= 50) return Localization.Get("scBandSteady");
            if (v >= ScConfig.LowMoraleThreshold) return Localization.Get("scBandUneasy");
            return Localization.Get("scBandBreaking");
        }

        public static string BandColor(int v)
        {
            if (v >= 50) return "120,180,90,255";
            if (v >= ScConfig.LowMoraleThreshold) return "220,170,70,255";
            return "210,80,70,255";
        }

        private static string SkillLines(Member m)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < Member.SkillCount; i++)
            {
                if (i > 0) sb.Append('\n');
                Skill s = (Skill)i;
                sb.Append(Localization.Get(ScNames.LocKey(s))).Append(' ').Append(m.Stars(s)).Append(" / ").Append(Member.MaxStars);
            }
            return sb.ToString();
        }

        private static string TraitText(Member m)
        {
            if (m.Traits.Count == 0) return "-";
            var sb = new StringBuilder();
            for (int i = 0; i < m.Traits.Count; i++) { if (i > 0) sb.Append(", "); sb.Append(ScTraits.Display(m.Traits[i])); }
            return sb.ToString();
        }

        private static string Flags(Member m, Community c)
        {
            var sb = new StringBuilder();
            if (m.Injured) sb.Append(Localization.Get("scWinInjured"));
            if (c.Hungry) { if (sb.Length > 0) sb.Append(", "); sb.Append(Localization.Get("scWinHungry")); }
            if (c.ThreatMemberId == m.Id) { if (sb.Length > 0) sb.Append(", "); sb.Append(Localization.Get("scWinThreat")); }
            return sb.ToString();
        }

        /// <summary>One row's short text and background (the original layout).</summary>
        public static string RowLine(Member m)
        {
            if (m == null) return "";
            return ScMorale.StatusText(m) + " - " + string.Format(Localization.Get("scWinMood"), m.Mood, ScMorale.MoodWord(m.Mood))
                   + (m.Injured ? " - " + Localization.Get("scWinInjured") : "")
                   + (m.Status == MemberStatus.OnRun ? " - " + ScRuns.StatusLine(m) : "");
        }

        public static string RowSkills(Member m)
        {
            if (m == null) return "";
            return "C" + m.Stars(Skill.Cardio) + " W" + m.Stars(Skill.Wits) + " F" + m.Stars(Skill.Fighting) + " S" + m.Stars(Skill.Shooting)
                   + " - " + Localization.Get(ScNames.LocKey(m.Specialty)) + " " + m.SpecLevel;
        }

        public static string RowColor(Member m, bool selected)
        {
            if (m == null) return "0,0,0,0";
            if (m.Injured) return selected ? "190,110,110,255" : "140,80,80,255";
            switch (m.Status)
            {
                case MemberStatus.Following:
                case MemberStatus.PendingStation: return selected ? "110,150,200,255" : "80,110,150,255";
                case MemberStatus.OnRun: return selected ? "150,150,150,255" : "100,100,100,255";
                case MemberStatus.Garrison: return selected ? "180,150,100,255" : "130,105,70,255";
                default: return selected ? "120,180,120,255" : "85,130,85,255";
            }
        }
    }

    /// <summary>
    /// One survivor row. Click selects; the detail panel and its buttons do the work. Every row binding is
    /// answered here even with no member (so it binds to the row, not the window), and hover is tracked for the
    /// newer layout's highlight. An optional texture named rowPortrait shows the member's portrait.
    /// </summary>
    [Preserve]
    public class XUiC_ScMemberRow : XUiController
    {
        private XUiC_ScCommunity owner;
        private Member data;
        private bool hovered;
        private XUiV_Texture portrait;
        private bool hasPortrait;

        public void Bind(XUiC_ScCommunity _owner) { owner = _owner; }

        public override void Init()
        {
            base.Init();
            if (owner == null) owner = GetParentByType<XUiC_ScCommunity>();
            XUiController pc = GetChildById("rowPortrait");
            portrait = pc != null ? pc.ViewComponent as XUiV_Texture : null;
        }

        public void SetData(Member m)
        {
            data = m;
            ApplyPortrait();
            IsDirty = true;
            RefreshBindings();
        }

        public bool HasPortraitView { get { return portrait != null; } }

        public void ApplyPortrait()
        {
            Texture2D t = data != null && portrait != null ? ScPortraits.Get(data) : null;
            // by reference: Unity's == calls a destroyed texture null, and the view would keep drawing it
            if (portrait != null && !ReferenceEquals(portrait.Texture, t)) portrait.Texture = t;
            hasPortrait = t != null;
        }

        public void ClearPortrait()
        {
            if (portrait != null) portrait.Texture = null;
            hasPortrait = false;
        }

        public override void Pressed(int _mouseButton)
        {
            base.Pressed(_mouseButton);
            if (_mouseButton != -1 || data == null) return;   // NGUI: -1 = left button
            if (owner == null) owner = GetParentByType<XUiC_ScCommunity>();
            if (owner != null) owner.Select(data);
        }

        public override void OnHovered(bool _isOver)
        {
            base.OnHovered(_isOver);
            if (hovered == _isOver) return;
            hovered = _isOver;
            RefreshBindings();
        }

        public override bool GetBindingValueInternal(ref string _value, string _bindingName)
        {
            switch (_bindingName)
            {
                case "rowvisible": _value = (data != null).ToString(); return true;
                case "rowname": _value = data != null ? data.Name : ""; return true;
                case "rowline": _value = XUiC_ScCommunity.RowLine(data); return true;
                case "rowskills": _value = XUiC_ScCommunity.RowSkills(data); return true;
                case "rowcolor": _value = XUiC_ScCommunity.RowColor(data, owner != null && owner.IsSelected(data)); return true;
                case "rowrole": _value = XUiC_ScCommunity.RoleText(data); return true;
                case "rowroleicon": _value = XUiC_ScCommunity.RoleIcon(data); return true;
                case "rowstatus": _value = XUiC_ScCommunity.StatusText(data, true); return true;
                case "rowinjured": _value = (data != null && data.Injured).ToString(); return true;
                case "rowselected": _value = (data != null && owner != null && owner.IsSelected(data)).ToString(); return true;
                case "rowhovered": _value = (data != null && hovered).ToString(); return true;
                case "rowhasportrait": _value = hasPortrait ? "True" : "False"; return true;
                case "rownoportrait": _value = hasPortrait ? "False" : "True"; return true;
                default: return base.GetBindingValueInternal(ref _value, _bindingName);
            }
        }
    }

    /// <summary>
    /// One outpost row on the Outposts tab: click selects it for the detail panel. Every binding is answered
    /// here even without an outpost, so it binds to the row and not the window.
    /// </summary>
    [Preserve]
    public class XUiC_ScOutpostRow : XUiController
    {
        private XUiC_ScCommunity owner;
        private Outpost data;
        private bool hovered;

        public void Bind(XUiC_ScCommunity _owner) { owner = _owner; }

        public override void Init()
        {
            base.Init();
            if (owner == null) owner = GetParentByType<XUiC_ScCommunity>();
        }

        public void SetData(Outpost o)
        {
            data = o;
            IsDirty = true;
            RefreshBindings();
        }

        public override void Pressed(int _mouseButton)
        {
            base.Pressed(_mouseButton);
            if (_mouseButton != -1 || data == null) return;   // NGUI: -1 = left button
            if (owner == null) owner = GetParentByType<XUiC_ScCommunity>();
            if (owner != null) owner.SelectOutpost(data);
        }

        public override void OnHovered(bool _isOver)
        {
            base.OnHovered(_isOver);
            if (hovered == _isOver) return;
            hovered = _isOver;
            RefreshBindings();
        }

        public override bool GetBindingValueInternal(ref string _value, string _bindingName)
        {
            switch (_bindingName)
            {
                case "oprowvisible": _value = (data != null).ToString(); return true;
                case "oprowname": _value = data != null ? data.Name : ""; return true;
                case "oprowicon": _value = XUiC_ScCommunity.OutpostIcon(data); return true;
                case "oprowtype":
                    _value = data != null ? string.Format(Localization.Get("scUiOpRowType"), ScOutposts.TypeLabel(data), data.Tier) : "";
                    return true;
                case "oprowstatus": _value = XUiC_ScCommunity.OutpostRowStatus(data); return true;
                case "oprowstatuscolor": _value = XUiC_ScCommunity.OutpostStateColor(data); return true;
                case "oprowselected": _value = (data != null && owner != null && owner.IsOutpostSelected(data)).ToString(); return true;
                case "oprowhovered": _value = (data != null && hovered).ToString(); return true;
                default: return base.GetBindingValueInternal(ref _value, _bindingName);
            }
        }
    }
}
