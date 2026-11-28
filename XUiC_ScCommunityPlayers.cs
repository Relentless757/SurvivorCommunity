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
    /// Every field is set here, not in Init: the bindings are read while the XML is parsed (xui-gotchas).
    /// </summary>
    public partial class XUiC_ScCommunity
    {
        /// <summary>The generator's row counts (PL_ROWS, NC_ENC_ROWS, NC_FAV_ROWS, NC_REQ_ROWS).</summary>
        public const int PlayerSlots = 10, NcEnclaveSlots = 8, NcFavourSlots = 5, NcRequestSlots = 3;
        private const string TextBody = "226,220,208,255", TextOff = "130,124,112,255", TextGood = "142,208,108,255", TextGold = "206,178,120,255",
                             TextDim = "190,182,166,255", TextDanger = "240,150,130,255", TextAlly = "90,170,255,255", TextFriend = "140,220,120,255";

        private ScCommunityView.View view = ScCommunityView.Empty;
        /// <summary>The morale panel shows the players list (the header's PLAYERS button).</summary>
        private bool showPlayers;
        /// <summary>Leave community asks to be pressed twice: until when the second press counts.</summary>
        private float confirmLeaveCommunityUntil;
        private string playersMessage = "";

        /// <summary>A row of the no-community page: two texts on the first line (A, B in Color), one under them (C).</summary>
        private sealed class NcRow
        {
            public string A = "", B = "", C = "", Color = TextBody;
        }
        private readonly List<NcRow> ncEnclaves = new List<NcRow>();
        private readonly List<NcRow> ncFavours = new List<NcRow>();
        private readonly List<NcRow> ncRequests = new List<NcRow>();
        private int ncEncKnown, ncEncMore, ncFavMore, ncReqMore;

        /// <summary>Init: the PLAYERS button and Leave community (optional: an older windows.xml has neither).</summary>
        private void InitPlayers()
        {
            HookOptional("btnPlayers", OnPlayersToggle);
            HookOptional("btnLeaveCommunity", OnLeaveCommunity);
        }

        /// <summary>OnOpen: the morale panel again, no confirm pending, no old answer.</summary>
        private void ResetPlayers()
        {
            showPlayers = false;
            confirmLeaveCommunityUntil = 0f;
            playersMessage = "";
        }

        /// <summary>Refresh: the view (mode, role, players) and the no-community page's rows.</summary>
        private void RefreshView()
        {
            view = ScCommunityView.Take(Player, true);
            if (!view.ShowPlayers) showPlayers = false;
            BuildNoCommunity();
        }

        private void OnPlayersToggle()
        {
            if (!view.ShowPlayers) return;
            showPlayers = !showPlayers;
            confirmLeaveCommunityUntil = 0f;
            RefreshBindingsSelfAndChildren();
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
                RefreshBindingsSelfAndChildren();
                return;
            }
            confirmLeaveCommunityUntil = 0f;
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

        /// <summary>The no-community page's rows (only for a personal record): the enclaves known, the favors, the requests.</summary>
        private void BuildNoCommunity()
        {
            ncEnclaves.Clear();
            ncFavours.Clear();
            ncRequests.Clear();
            ncEncKnown = ncEncMore = ncFavMore = ncReqMore = 0;
            if (!view.Personal || !ScRegistry.Ready) return;
            try
            {
                EntityPlayerLocal pl = Player;
                Vector3 from = pl != null ? pl.position : Vector3.zero;
                if (ScConfig.Enclaves)
                {
                    var known = new List<Enclave>();
                    foreach (Enclave e in ScEnclaves.All) if (e.Known && !e.Lost) known.Add(e);
                    known.Sort((a, b) => a.DistanceXZ(from.x, from.z).CompareTo(b.DistanceXZ(from.x, from.z)));
                    ncEncKnown = known.Count;
                    for (int i = 0; i < known.Count; i++)
                    {
                        if (i >= NcEnclaveSlots) { ncEncMore = known.Count - NcEnclaveSlots; break; }
                        Enclave e = known[i];
                        string band = ScEnclaves.StandingKey(e);
                        ncEnclaves.Add(new NcRow
                        {
                            A = e.Name ?? "",
                            B = ScEnclaves.StandingWord(e),
                            C = ScEnclaves.Where(from, e.Centre),
                            Color = band == "allied" ? TextAlly : band == "friendly" ? TextFriend : band == "hostile" ? TextDanger : TextDim
                        });
                    }
                    var open = new List<EnclaveRequest>();
                    foreach (EnclaveRequest r in ScEncRequests.All) if (r.Kind != EnclaveRequest.KindSiege) open.Add(r);
                    open.Sort((a, b) => Due(a).CompareTo(Due(b)));
                    int now = ScEncRequests.Now;
                    for (int i = 0; i < open.Count; i++)
                    {
                        if (i >= NcRequestSlots) { ncReqMore = open.Count - NcRequestSlots; break; }
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
                    if (i >= NcFavourSlots) { ncFavMore = favs.Count - NcFavourSlots; break; }
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
                default: return "";
            }
        }

        private static string RoleWord(string role)
        {
            return Localization.Get(role == ScCommunityView.RoleOwner ? "scUiPlRoleOwner" : role == ScCommunityView.RoleHelper ? "scUiPlRoleHelper" : "scUiPlRoleMember");
        }

        private static string More(int n) { return n > 0 ? string.Format(Localization.Get("scUiNcMore"), n) : ""; }

        /// <summary>
        /// The bindings of this part (always a non-null string and true when the name is one of them): hascommunity, nocommunity,
        /// storagevisible, the PLAYERS button (playersbtnvisible, playersbtnlabel), showmorale / showplayers, playercount, playersyou,
        /// pl&lt;1..10&gt;&lt;visible|name|namecolor|role|dotcolor&gt;, plmore, leavecommunityvisible / label, playersmessage, the gray
        /// owner's orders (leavetextcolor, opdisbandcolor), and the no-community page (ncjoin, ncjoinicon, ncencsummary, ncencempty, ncencmore,
        /// ncenc&lt;1..8&gt;, ncfav&lt;1..5&gt;, ncfavempty, ncfavmore, ncreq&lt;1..3&gt;, ncreqempty, ncreqmore; each row's
        /// &lt;visible|a|b|c|color&gt;).
        /// </summary>
        private bool PlayersBinding(string name, ref string value)
        {
            switch (name)
            {
                case "hascommunity": value = (!view.Personal).ToString(); return true;
                case "nocommunity": value = view.Personal.ToString(); return true;
                case "storagevisible": value = (!view.Personal).ToString(); return true;
                case "playersbtnvisible": value = view.ShowPlayers.ToString(); return true;
                case "playersbtnlabel":
                    value = showPlayers ? Localization.Get("scUiPlMorale") : string.Format(Localization.Get("scUiPlButton"), view.Players.Count);
                    return true;
                case "showplayers": value = (showPlayers && view.ShowPlayers).ToString(); return true;
                case "showmorale": value = (!(showPlayers && view.ShowPlayers)).ToString(); return true;
                case "playercount": value = Num(view.Players.Count); return true;
                case "playersyou": value = YouLine(); return true;
                case "plmore": value = More(view.Players.Count - PlayerSlots); return true;
                case "leavecommunityvisible": value = view.CanLeave.ToString(); return true;
                case "leavecommunitylabel":
                    value = Localization.Get(Time.unscaledTime <= confirmLeaveCommunityUntil ? "scUiPlLeaveConfirm" : "scUiPlLeave");
                    return true;
                case "playersmessage": value = playersMessage ?? ""; return true;
                // the owner's orders, grayed when they are not this player's (PerPlayer/PerParty)
                case "leavetextcolor": value = view.Gated ? TextOff : TextDanger; return true;
                case "opdisbandcolor": value = view.Gated ? TextOff : TextDanger; return true;
                // the no-community page
                case "ncjoin": value = Localization.Get(view.Mode == ScCommunityMode.PerParty ? "scUiNcJoinParty" : "scUiNcJoinAlly"); return true;
                case "ncjoinicon": value = view.Mode == ScCommunityMode.PerParty ? "ui_game_symbol_party" : "ui_game_symbol_allies"; return true;
                case "ncencsummary": value = ncEncKnown > 0 ? string.Format(Localization.Get("scUiNcEncSummary"), ncEncKnown) : ""; return true;
                case "ncencempty": value = (ncEnclaves.Count == 0).ToString(); return true;
                case "ncencmore": value = More(ncEncMore); return true;
                case "ncfavempty": value = (ncFavours.Count == 0).ToString(); return true;
                case "ncfavmore": value = More(ncFavMore); return true;
                case "ncreqempty": value = (ncRequests.Count == 0).ToString(); return true;
                case "ncreqmore": value = More(ncReqMore); return true;
            }
            int k;
            string field;
            if (Slot(name, "pl", PlayerSlots, out k, out field))
            {
                List<ScCommunityPlayer> ps = view.Players;
                ScCommunityPlayer r = k < ps.Count ? ps[k] : null;
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
            if (NcRowBinding(name, "ncenc", NcEnclaveSlots, ncEnclaves, ref value)) return true;
            if (NcRowBinding(name, "ncfav", NcFavourSlots, ncFavours, ref value)) return true;
            if (NcRowBinding(name, "ncreq", NcRequestSlots, ncRequests, ref value)) return true;
            return false;
        }

        private static bool NcRowBinding(string name, string prefix, int slots, List<NcRow> rows, ref string value)
        {
            int k;
            string field;
            if (!Slot(name, prefix, slots, out k, out field)) return false;
            NcRow r = k < rows.Count ? rows[k] : null;
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
