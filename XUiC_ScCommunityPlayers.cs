using System;
using System.Collections.Generic;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round MP7 (part U), 2026-11-27: the community window's parts for the community modes - the other part of XUiC_ScCommunity
    /// (Tools/generate_community_ui.py draws them in both looks):
    /// - the no-community page: a player with only their own relations (PerPlayer/PerParty, a personal record) sees it in place of
    ///   the three panels - how to start or join a community in this mode, what they can do now, the enclaves they know with their
    ///   standing, and their favors and enclave requests (what carries over when they found or join). Open storage hides for them;
    /// - the players list: a PLAYERS button in the header swaps the morale panel for the community's players (name, role, online),
    ///   in PerPlayer/PerParty or with two or more communities (never in single player);
    /// - Leave community under the list for a player who is not the owner (two presses; community.leave). Never in Shared mode's
    ///   community 0, which has nowhere to leave to;
    /// - the owner's orders (Ask to leave, Disband, rationing's facility action) grayed for a player they are not for in
    ///   PerPlayer/PerParty. The server still refuses them (ScActions.WhyNot); a grayed button still asks, so a role that changed a
    ///   moment ago is the server's to judge.
    /// What it reads is ScCommunityView.Take: the registry on the host and in single player, the derived block's mode=, role=, ownops=
    /// and cpl= on a client. Shared mode with one community: the view has no role, so the window is exactly as before.
    /// Round MP7 (review U): m1, m2 - the players list and the no-community page's three lists page (Showing X of Y and a pager, as
    /// the member list does), so every player, enclave, favor and request can be seen; n2 - the Leave hint goes with its confirm.
    /// Round db1: Disband community for the owner of a founded community, in Leave's place (two presses; community.disband; the first
    /// press's hint says what it does). The panel (PanelOn) shows for them in single player and in Shared with one community too.
    /// Every field is set here, not in Init: the bindings are read while the XML is parsed (xui-gotchas).
    /// </summary>
    public partial class XUiC_ScCommunity
    {
        /// <summary>The generator's row counts (PL_ROWS, NC_FAV_ROWS, NC_REQ_ROWS). Round MP7 (review U): n3 - 9 players (was 10): the
        /// player's own role line got a third line and the list a pager. Round en1: the no-community page's enclave rows are the ENCLAVES
        /// tab's (XUiC_ScCommunityEnclaves.cs, EnclaveSlots, the generator's ENC_ROWS): NcEnclaveSlots is gone.</summary>
        public const int PlayerSlots = 9, NcFavourSlots = 5, NcRequestSlots = 3;
        private const string TextBody = "226,220,208,255", TextOff = "130,124,112,255", TextGood = "142,208,108,255", TextGold = "206,178,120,255",
                             TextDim = "190,182,166,255", TextDanger = "240,150,130,255", TextAlly = "90,170,255,255", TextFriend = "140,220,120,255";

        private ScCommunityView.View view = ScCommunityView.Empty;
        /// <summary>The morale panel shows the players list (the header's PLAYERS button).</summary>
        private bool showPlayers;
        /// <summary>Leave community asks to be pressed twice: until when the second press counts.</summary>
        private float confirmLeaveCommunityUntil;
        private string playersMessage = "";
        /// <summary>Round MP7 (review U): n2 - playersMessage is Leave's hint (it goes when the confirm lapses or the panel turns).</summary>
        private bool leaveHint;
        /// <summary>Round MP7 (review U): m1, m2 - the page of the players list and of the no-community page's three lists.</summary>
        private int plPage, ncEncPage, ncFavPage, ncReqPage;
        // round db1: Disband community's two presses
        /// <summary>Round db1: Disband community asks to be pressed twice - until when the second press counts; playersMessage is its hint.</summary>
        private float confirmDisbandCommunityUntil;
        private bool disbandCommunityHint;

        /// <summary>A row of the no-community page: two texts on the first line (A, B in Color), one under them (C).</summary>
        private sealed class NcRow
        {
            public string A = "", B = "", C = "", Color = TextBody;
        }
        private readonly List<NcRow> ncFavours = new List<NcRow>();
        private readonly List<NcRow> ncRequests = new List<NcRow>();

        /// <summary>Init: the PLAYERS button and Leave community (optional: an older windows.xml has neither).</summary>
        private void InitPlayers()
        {
            HookOptional("btnPlayers", OnPlayersToggle);
            HookOptional("btnLeaveCommunity", OnLeaveCommunity);
            HookOptional("btnDisbandCommunity", OnDisbandCommunity);        // round db1
            // round MP7 (review U): m1, m2 - the pagers
            HookOptional("btnPlPrev", () => TurnPage(ref plPage, -1, view.Players.Count, PlayerSlots));
            HookOptional("btnPlNext", () => TurnPage(ref plPage, 1, view.Players.Count, PlayerSlots));
            // round en1: the enclave rows are the ENCLAVES tab's list (encList), six to a page
            HookOptional("btnNcEncPrev", () => TurnPage(ref ncEncPage, -1, encList.Count, EnclaveSlots));
            HookOptional("btnNcEncNext", () => TurnPage(ref ncEncPage, 1, encList.Count, EnclaveSlots));
            HookOptional("btnNcFavPrev", () => TurnPage(ref ncFavPage, -1, ncFavours.Count, NcFavourSlots));
            HookOptional("btnNcFavNext", () => TurnPage(ref ncFavPage, 1, ncFavours.Count, NcFavourSlots));
            HookOptional("btnNcReqPrev", () => TurnPage(ref ncReqPage, -1, ncRequests.Count, NcRequestSlots));
            HookOptional("btnNcReqNext", () => TurnPage(ref ncReqPage, 1, ncRequests.Count, NcRequestSlots));
        }

        /// <summary>Round MP7 (review U): m1, m2 - a list's page count (one at least) and its page clamped to it.</summary>
        private static int Pages(int count, int size) { return Math.Max(1, (count + size - 1) / size); }

        private static int ClampPage(int page, int count, int size) { return Math.Max(0, Math.Min(Pages(count, size) - 1, page)); }

        private void TurnPage(ref int page, int delta, int count, int size)
        {
            page = ClampPage(page + delta, count, size);
            RefreshBindingsSelfAndChildren();
        }

        /// <summary>Round MP7 (review U): m1, m2 - "Showing 9 of 23" for a page of a list ("" for an empty one), as the member list says it.</summary>
        private static string Showing(int page, int count, int size)
        {
            if (count <= 0) return "";
            int shown = Math.Max(0, Math.Min(size, count - page * size));
            return string.Format(Localization.Get("scWinShowing"), shown, count);
        }

        /// <summary>OnOpen: the morale panel again, no confirm pending, no old answer.</summary>
        private void ResetPlayers()
        {
            showPlayers = false;
            confirmLeaveCommunityUntil = 0f;
            playersMessage = "";
            leaveHint = false;
            plPage = ncEncPage = ncFavPage = ncReqPage = 0;       // round MP7 (review U)
            confirmDisbandCommunityUntil = 0f;                     // round db1
            disbandCommunityHint = false;
        }

        /// <summary>Refresh: the view (mode, role, players) and the no-community page's rows.</summary>
        private void RefreshView()
        {
            view = ScCommunityView.Take(Player, true);
            if (!view.PanelOn) showPlayers = false;                // round db1: the panel is also the owner's Disband community
            // round MP7 (review U): n2 - Leave's hint goes once its five seconds are over
            if (confirmLeaveCommunityUntil > 0f && Time.unscaledTime > confirmLeaveCommunityUntil)
            {
                confirmLeaveCommunityUntil = 0f;
                if (leaveHint) { playersMessage = ""; leaveHint = false; }
            }
            // round db1: and Disband community's
            if (confirmDisbandCommunityUntil > 0f && Time.unscaledTime > confirmDisbandCommunityUntil)
            {
                confirmDisbandCommunityUntil = 0f;
                if (disbandCommunityHint) { playersMessage = ""; disbandCommunityHint = false; }
            }
            plPage = ClampPage(plPage, view.Players.Count, PlayerSlots);        // round MP7 (review U): m1 - a shorter list since
            BuildNoCommunity();
        }

        private void OnPlayersToggle()
        {
            if (!view.PanelOn) return;              // round db1: the list, or the owner's Disband community
            showPlayers = !showPlayers;
            confirmLeaveCommunityUntil = 0f;
            playersMessage = "";                    // round MP7 (review U): n2 - no hint or old answer left behind
            leaveHint = false;
            confirmDisbandCommunityUntil = 0f;      // round db1
            disbandCommunityHint = false;
            RefreshBindingsSelfAndChildren();
        }

        // round db1: the owner's Disband community
        /// <summary>
        /// Round db1: Disband community (the owner's) - the first press says what it does and asks for a second within five seconds, the
        /// second sends community.disband. The server decides (ScMembership.DisbandAction: the owner or an admin, no siege or live
        /// attack, no member down) and tells every player of the community; the window turns with the new copy. Round db1 (review): the
        /// first press asks the server (community.disband 0) whether it may be done now - its hint then, or the refusal naming the
        /// members away (the user's decision 1: everyone home with her body loaded) or why not (a blood moon, a siege, an attack ...).
        /// </summary>
        private void OnDisbandCommunity()
        {
            if (!view.Disband || view.Personal) return;
            float now = Time.unscaledTime;
            if (now > confirmDisbandCommunityUntil)
            {
                // round db1 (review): the first press asks; the second counts only after a yes
                confirmDisbandCommunityUntil = 0f;
                ScActions.Request(ScMembership.OpDisband, r =>
                {
                    if (r.Ok)
                    {
                        confirmDisbandCommunityUntil = Time.unscaledTime + 5f;
                        playersMessage = Localization.Get("scUiDb1Hint");
                        disbandCommunityHint = true;
                    }
                    else
                    {
                        playersMessage = DisbandRefusal(r);
                        disbandCommunityHint = false;
                        PlaySound(r.Sound);
                    }
                    AfterReply();
                }, 0);
                return;
            }
            confirmDisbandCommunityUntil = 0f;
            disbandCommunityHint = false;
            ScActions.Request(ScMembership.OpDisband, r =>
            {
                playersMessage = r.Ok ? r.Text : DisbandRefusal(r);      // round db1 (review): a done one's text is a partial warning, if any
                PlaySound(r.Sound);
                if (r.Ok) showPlayers = false;
                AfterReply();
            }, 1);
        }

        // round db1 (review): the refusal's words in this machine's language - for scDb1Away, the members away named with why
        /// <summary>
        /// Round db1 (review): a community.disband refusal as the panel shows it: scDb1Away names the members away from the reply's hint
        /// (ScMembership.AwayHint: "why:name" entries and the count), each why in this machine's language; any other refusal as it is.
        /// </summary>
        private static string DisbandRefusal(ScActionReply r)
        {
            string list = r.HintStr(ScMembership.AwayHintKey, null);
            if (list == null) return r.Text;
            var parts = new List<string>();
            foreach (string e in list.Split(','))
            {
                int c = e.IndexOf(':');
                if (c <= 0) continue;
                string why = e.Substring(0, c);
                string key = why == ScMembership.AwayRun ? "scDb1AwayRun" : why == ScMembership.AwayPosted ? "scDb1AwayPosted"
                           : why == ScMembership.AwayTravel ? "scDb1AwayTravel" : "scDb1AwayUnloaded";
                parts.Add(string.Format(Localization.Get("scDb1AwayOne"), ScCommunityView.Unescape(e.Substring(c + 1)), Localization.Get(key)));
            }
            int total = r.HintInt(ScMembership.AwayCountKey, parts.Count);
            string names = string.Join(", ", parts.ToArray());
            if (total > parts.Count) names += " " + string.Format(Localization.Get("scDb1AwayMore"), total - parts.Count);
            return string.Format(Localization.Get("scDb1Away"), names);
        }

        /// <summary>Leave community: the first press asks for a second within five seconds, the second sends community.leave.</summary>
        private void OnLeaveCommunity()
        {
            if (!view.CanLeave) return;
            float now = Time.unscaledTime;
            if (now > confirmLeaveCommunityUntil)
            {
                confirmLeaveCommunityUntil = now + 5f;
                playersMessage = Localization.Get("scUiPlLeaveHint");
                leaveHint = true;
                RefreshBindingsSelfAndChildren();
                return;
            }
            confirmLeaveCommunityUntil = 0f;
            leaveHint = false;
            // the server decides (ScMembership.LeaveAction: never the owner, a personal record or Shared mode's community 0) and tells
            // the player (scMp7Left); their new community's copy follows, and the window turns to it
            ScActions.Request(ScMembership.OpLeave, r =>
            {
                playersMessage = r.Ok ? "" : r.Text;
                PlaySound(r.Sound);
                if (r.Ok) showPlayers = false;
                AfterReply();
            });
        }

        /// <summary>
        /// The no-community page's rows (only for a personal record): the favors, the requests. Round MP7 (review U): m2 - every one
        /// (the pages show them a page at a time); requests taken on before offered ones. Round en1: the enclaves known are the
        /// ENCLAVES tab's list (BuildEnclaves, XUiC_ScCommunityEnclaves.cs), the same rows on this page.
        /// </summary>
        private void BuildNoCommunity()
        {
            ncFavours.Clear();
            ncRequests.Clear();
            if (!view.Personal || !ScRegistry.Ready) { ncFavPage = ncReqPage = 0; return; }
            try
            {
                if (ScConfig.Enclaves)
                {
                    var open = new List<EnclaveRequest>();
                    foreach (EnclaveRequest r in ScEncRequests.All) if (r.Kind != EnclaveRequest.KindSiege) open.Add(r);
                    // round MP7 (review U): m2 - those taken on first (their delivery is what can be missed), then by the day that comes first
                    open.Sort((a, b) => a.IsAccepted != b.IsAccepted ? (a.IsAccepted ? -1 : 1) : Due(a).CompareTo(Due(b)));
                    int now = ScEncRequests.Now;
                    for (int i = 0; i < open.Count; i++)
                    {
                        EnclaveRequest r = open[i];
                        Enclave e = ScEnclaves.ById(r.EnclaveId);
                        ncRequests.Add(new NcRow
                        {
                            A = e != null ? e.Name ?? "" : "?",
                            B = r.IsAccepted ? string.Format(Localization.Get("scRadioReqDue"), r.DueDay) : string.Format(Localization.Get("scRadioReqAnswer"), r.LapseDay),
                            C = ScEncRequests.WhatText(r),
                            Color = r.IsAccepted ? (r.DueDay <= now ? TextDanger : TextBody) : TextGold
                        });
                    }
                }
                var favs = new List<Prospect>(ScRegistry.Prospects.Values);
                // done first (they join once there is a home), then taken on, then offered; by name
                favs.Sort((a, b) =>
                {
                    int ra = a.Done ? 0 : a.Started ? 1 : 2, rb = b.Done ? 0 : b.Started ? 1 : 2;
                    return ra != rb ? ra.CompareTo(rb) : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                });
                for (int i = 0; i < favs.Count; i++)
                {
                    Prospect p = favs[i];
                    ncFavours.Add(new NcRow
                    {
                        A = p.Name ?? "",
                        B = Localization.Get(p.Done ? "scUiNcFavDone" : p.Started ? "scUiNcFavTaken" : "scUiNcFavOffered"),
                        C = p.Favour == FavourKind.Poi ? string.Format(Localization.Get("scUiNcFavPoi"), p.PoiName)
                          : p.Favour == FavourKind.Supplies ? string.Format(Localization.Get("scUiNcFavSupplies"), p.ItemCount, Localization.Get(ScNames.LocKey(p.ItemCategory)))
                          : "",
                        Color = p.Done ? TextGood : p.Started ? TextGold : TextDim
                    });
                }
            }
            catch (Exception e)
            {
                ScLog.Once("community-window-nocommunity", e);
            }
            ncFavPage = ClampPage(ncFavPage, ncFavours.Count, NcFavourSlots);
            ncReqPage = ClampPage(ncReqPage, ncRequests.Count, NcRequestSlots);
        }

        /// <summary>A request's day that comes first: the delivery's for one taken on, the answer's for one offered.</summary>
        private static int Due(EnclaveRequest r) { return r.IsAccepted ? r.DueDay : r.LapseDay; }

        /// <summary>"You own this community." and the like, for the players panel.</summary>
        private string YouLine()
        {
            switch (view.Role)
            {
                case ScCommunityView.RoleOwner: return Localization.Get("scUiPlYouOwner");
                case ScCommunityView.RoleHelper: return Localization.Get("scUiPlYouHelper");
                case ScCommunityView.RoleMember:
                    if (view.Mode == ScCommunityMode.Shared) return Localization.Get("scUiPlYouMember");
                    if (view.OwnOps) return Localization.Get("scUiPlYouAdmin");
                    return Localization.Get(view.Mode == ScCommunityMode.PerParty ? "scUiPlYouMemberParty" : "scUiPlYouMemberPlayer");
                // round db1: no role (single player in Shared, Shared with one community) - its owner's panel says so
                default: return view.Disband ? Localization.Get("scUiPlYouOwner") : "";
            }
        }

        private static string RoleWord(string role)
        {
            return Localization.Get(role == ScCommunityView.RoleOwner ? "scUiPlRoleOwner" : role == ScCommunityView.RoleHelper ? "scUiPlRoleHelper" : "scUiPlRoleMember");
        }

        /// <summary>
        /// Round MP7 (review U): m1 - the players list's line under its rows: Showing X of Y, and how many the server did not list
        /// (cplt=, past ScCommunityView.MaxListed).
        /// </summary>
        private string PlayersShowing()
        {
            string s = Showing(plPage, view.Players.Count, PlayerSlots);
            int unlisted = view.PlayerTotal - view.Players.Count;
            return unlisted > 0 ? s + " - " + string.Format(Localization.Get("scUiPlNotListed"), unlisted) : s;
        }

        /// <summary>
        /// The bindings of this part (always a non-null string and true when the name is one of them): hascommunity, nocommunity,
        /// storagevisible, the PLAYERS button (playersbtnvisible, playersbtnlabel), showmorale / showplayers, playercount, playersyou,
        /// pl&lt;1..9&gt;&lt;visible|name|namecolor|role|dotcolor&gt;, plshowingtext / plpagevisible, leavecommunityvisible / label,
        /// playersmessage, the gray owner's orders (leavetextcolor, opdisbandcolor), and the no-community page (ncjoin, ncjoinicon,
        /// ncfav&lt;1..5&gt;, ncfavempty, ncreq&lt;1..3&gt;, ncreqempty - each row's &lt;visible|a|b|c|color&gt; - and each list's
        /// &lt;ncfav|ncreq&gt;&lt;showing|pagevisible&gt;). Round MP7 (review U): the pagers replaced plmore, ncencmore, ncfavmore and ncreqmore.
        /// Round db1: disbandcommunityvisible / disbandcommunitylabel. Round en1: the enclave list's (ncenc...) are EnclaveBinding's.
        /// </summary>
        private bool PlayersBinding(string name, ref string value)
        {
            switch (name)
            {
                case "hascommunity": value = (!view.Personal).ToString(); return true;
                case "nocommunity": value = view.Personal.ToString(); return true;
                case "storagevisible": value = (!view.Personal).ToString(); return true;
                // round db1: the panel (PanelOn) is the list, or the owner's Disband community where the list is off
                case "playersbtnvisible": value = view.PanelOn.ToString(); return true;
                case "playersbtnlabel":
                    value = showPlayers ? Localization.Get("scUiPlMorale") : string.Format(Localization.Get("scUiPlButton"), view.PlayerTotal);
                    return true;
                case "showplayers": value = (showPlayers && view.PanelOn).ToString(); return true;
                case "showmorale": value = (!(showPlayers && view.PanelOn)).ToString(); return true;
                case "playercount": value = Num(view.PlayerTotal); return true;      // round MP7 (review U): m1 - the true count
                case "playersyou": value = YouLine(); return true;
                // round MP7 (review U): m1 - the pager (it replaced plmore)
                case "plshowingtext": value = PlayersShowing(); return true;
                case "plpagevisible": value = (Pages(view.Players.Count, PlayerSlots) > 1).ToString(); return true;
                case "leavecommunityvisible": value = view.CanLeave.ToString(); return true;
                case "leavecommunitylabel":
                    value = Localization.Get(Time.unscaledTime <= confirmLeaveCommunityUntil ? "scUiPlLeaveConfirm" : "scUiPlLeave");
                    return true;
                // round db1: Disband community, the owner's (where Leave would be: an owner never has Leave)
                case "disbandcommunityvisible": value = (view.Disband && !view.Personal).ToString(); return true;
                case "disbandcommunitylabel":
                    value = Localization.Get(Time.unscaledTime <= confirmDisbandCommunityUntil ? "scUiDb1Confirm" : "scUiDb1Disband");
                    return true;
                case "playersmessage": value = playersMessage ?? ""; return true;
                // the owner's orders, grayed when they are not this player's (PerPlayer/PerParty)
                case "leavetextcolor": value = view.Gated ? TextOff : TextDanger; return true;
                case "opdisbandcolor": value = view.Gated ? TextOff : TextDanger; return true;
                // the no-community page
                case "ncjoin": value = Localization.Get(view.Mode == ScCommunityMode.PerParty ? "scUiNcJoinParty" : "scUiNcJoinAlly"); return true;
                case "ncjoinicon": value = view.Mode == ScCommunityMode.PerParty ? "ui_game_symbol_party" : "ui_game_symbol_allies"; return true;
                case "ncfavempty": value = (ncFavours.Count == 0).ToString(); return true;
                case "ncreqempty": value = (ncRequests.Count == 0).ToString(); return true;
                // round MP7 (review U): m2 - each list's pager (they replaced ncencmore, ncfavmore and ncreqmore)
                case "ncfavshowing": value = Showing(ncFavPage, ncFavours.Count, NcFavourSlots); return true;
                case "ncfavpagevisible": value = (Pages(ncFavours.Count, NcFavourSlots) > 1).ToString(); return true;
                case "ncreqshowing": value = Showing(ncReqPage, ncRequests.Count, NcRequestSlots); return true;
                case "ncreqpagevisible": value = (Pages(ncRequests.Count, NcRequestSlots) > 1).ToString(); return true;
            }
            int k;
            string field;
            if (Slot(name, "pl", PlayerSlots, out k, out field))
            {
                List<ScCommunityPlayer> ps = view.Players;
                int at = plPage * PlayerSlots + k;                   // round MP7 (review U): m1 - this page's
                ScCommunityPlayer r = at < ps.Count ? ps[at] : null;
                EntityPlayerLocal me = Player;
                switch (field)
                {
                    case "visible": value = (r != null).ToString(); return true;
                    case "name":
                        value = r == null ? "" : me != null && r.Online && r.EntityId == me.entityId ? string.Format(Localization.Get("scUiPlYou"), r.Name) : r.Name;
                        return true;
                    case "namecolor": value = r != null && r.Online ? TextBody : TextOff; return true;
                    case "role": value = r == null ? "" : RoleWord(r.Role) + (r.Online ? "" : " - " + Localization.Get("scUiPlOffline")); return true;
                    case "dotcolor": value = r != null && r.Online ? TextGood : TextOff; return true;
                }
                return false;
            }
            if (NcRowBinding(name, "ncfav", NcFavourSlots, ncFavours, ncFavPage, ref value)) return true;
            if (NcRowBinding(name, "ncreq", NcRequestSlots, ncRequests, ncReqPage, ref value)) return true;
            return false;
        }

        private static bool NcRowBinding(string name, string prefix, int slots, List<NcRow> rows, int page, ref string value)
        {
            int k;
            string field;
            if (!Slot(name, prefix, slots, out k, out field)) return false;
            int at = page * slots + k;                               // round MP7 (review U): m2 - this page's
            NcRow r = at < rows.Count ? rows[at] : null;
            switch (field)
            {
                case "visible": value = (r != null).ToString(); return true;
                case "a": value = r != null ? r.A : ""; return true;
                case "b": value = r != null ? r.B : ""; return true;
                case "c": value = r != null ? r.C : ""; return true;
                case "color": value = r != null ? r.Color : TextBody; return true;
            }
            return false;
        }

        /// <summary>name = prefix + a slot number 1..slots + a field: the slot's index and the field. False for anything else.</summary>
        private static bool Slot(string name, string prefix, int slots, out int index, out string field)
        {
            index = -1;
            field = null;
            if (name == null || !name.StartsWith(prefix, StringComparison.Ordinal)) return false;
            int i = prefix.Length, n = 0;
            while (i < name.Length && name[i] >= '0' && name[i] <= '9' && i - prefix.Length < 3) { n = n * 10 + (name[i] - '0'); i++; }
            if (i == prefix.Length || n < 1 || n > slots || i >= name.Length) return false;
            index = n - 1;
            field = name.Substring(i);
            return true;
        }
    }
}
