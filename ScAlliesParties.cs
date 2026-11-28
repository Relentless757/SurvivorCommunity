using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HarmonyLib;

namespace SurvivorCommunity
{
    // round MP7 (part AP), 2026-11-27: land-claim allies (PerPlayer) and vanilla parties (PerParty) bring players into a founder's
    // community and take them out again (MP7_DESIGN.md, the user's decisions 5-7). Every change goes through ScMembership.Move.
    //
    // - ScJoins: how each player joined the founded community they are in (an admin; a founder's land-claim ally, with the link
    //   that brought them in; or a party), and Leave's opt-outs, saved in [membership] (joined=, optout=). A player an admin
    //   placed (or one placed before part AP or by the mode switch: no record) is never taken out by an ally or party change. A
    //   player who left a community (Leave, or an admin moved them out) is not pulled back into it by the allies reconcile or by
    //   another player's party accept until the link is made again (the ally link removed and made again; their own party join).
    // - ScAllies (PerPlayer): the vanilla ally event (fires twice per change, once per side, never at load: idempotent) and the
    //   reconcile at load, at each spawn, at founding, after the mode switch and after a player leaves a community.
    // - ScParties (PerParty): postfixes on Party.ServerHandleAcceptInvite, ServerHandleAutoJoinParty (AutoParty, as an accept),
    //   ServerHandleLeaveParty, ServerHandleKickParty and ServerHandleChangeLead (never the disconnect handler: a disconnect
    //   changes nothing), and the founder's party at founding.
    //   ScOwnerRule.PartyLeaderMayAct (ScMembership.cs) is the helper rule.
    // Shared mode: nothing here runs. Single player: no ally or party event ever fires (no other player), and the reconciles find
    // no ally. Server, main thread.

    /// <summary>
    /// Round MP7 (part AP): how each player joined their founded community, and the communities they left (Leave sticks). Kept in
    /// the world record (ScWorldState.JoinedHow, JoinedVia, OptOuts), saved in [membership] (Write / Parse). PerPlayer/PerParty only.
    /// </summary>
    public static class ScJoins
    {
        /// <summary>An admin's console (sc community join / owner / new), or any reason that is not an ally's or a party's.</summary>
        public const string HowAdmin = "admin";
        /// <summary>A founder's land-claim ally (PerPlayer); JoinedVia names the link.</summary>
        public const string HowAlly = "ally";
        /// <summary>A party holding one of the community's players (PerParty).</summary>
        public const string HowParty = "party";

        /// <summary>The ally link the next ByAlly Move records (ScAllies.Join sets it around its Move; Noted takes it).</summary>
        internal static string PendingVia;

        private static ScWorldState Ws { get { return ScRegistry.WorldState; } }

        /// <summary>How the player joined (HowAdmin / HowAlly / HowParty), or "" - no record: placed before part AP, by the mode switch, or an owner.</summary>
        internal static string How(string pid)
        {
            string h;
            return !string.IsNullOrEmpty(pid) && Ws.JoinedHow.TryGetValue(pid, out h) ? h ?? "" : "";
        }

        /// <summary>An ally-joined player's link: the founder whose land-claim ally brought them in ("" when none recorded).</summary>
        internal static string Via(string pid)
        {
            string v;
            return !string.IsNullOrEmpty(pid) && Ws.JoinedVia.TryGetValue(pid, out v) ? v ?? "" : "";
        }

        /// <summary>The player joined by an ally link: an ally change may take them out (an admin's or an older join never).</summary>
        internal static bool ByAlly(string pid) { return How(pid) == HowAlly; }

        /// <summary>The player joined by a party: leaving or being kicked from the community's party takes them out.</summary>
        internal static bool ByParty(string pid) { return How(pid) == HowParty; }

        /// <summary>The player left community cid (Leave, or an admin moved them out): the reconcile and other players' party accepts leave them out.</summary>
        internal static bool OptedOut(string pid, int cid)
        {
            SortedSet<int> s;
            return !string.IsNullOrEmpty(pid) && Ws.OptOuts.TryGetValue(pid, out s) && s.Contains(cid);
        }

        internal static void OptOut(string pid, int cid)
        {
            if (string.IsNullOrEmpty(pid) || cid < 0) return;
            SortedSet<int> s;
            if (!Ws.OptOuts.TryGetValue(pid, out s)) Ws.OptOuts[pid] = s = new SortedSet<int>();
            s.Add(cid);
        }

        /// <summary>The opt-out of community cid ends (the link made again, or they are in it again). True when there was one.</summary>
        internal static bool ClearOptOut(string pid, int cid)
        {
            SortedSet<int> s;
            if (string.IsNullOrEmpty(pid) || !Ws.OptOuts.TryGetValue(pid, out s) || !s.Remove(cid)) return false;
            if (s.Count == 0) Ws.OptOuts.Remove(pid);
            return true;
        }

        /// <summary>Every opt-out of the player ends (their own party join: a fresh consent). How many there were.</summary>
        internal static int ClearOptOuts(string pid)
        {
            SortedSet<int> s;
            if (string.IsNullOrEmpty(pid) || !Ws.OptOuts.TryGetValue(pid, out s)) return 0;
            Ws.OptOuts.Remove(pid);
            return s.Count;
        }

        /// <summary>
        /// ScMembership.Move's step, after the map changed (before its tells and save): PerPlayer/PerParty only.
        /// - Into a founded community: how they joined is recorded (ByAlly: ally, with PendingVia as the link; ByParty: party;
        ///   anything else: admin) - none for its owner; their opt-out of it ends; an admin's move out of another founded
        ///   community opts them out of that one.
        /// - Out to a personal record (or none): the join record goes; Leave or an admin's move out of a founded community opts
        ///   them out of it (Leave sticks); in PerPlayer the allies reconcile runs for them right after (ScAllies.QueueReconcile:
        ///   another owner's ally joins that community) - not for ByAllyRemoved, whose caller reconciles itself.
        /// </summary>
        internal static void Noted(string pid, CommunityState old, CommunityState target, string reason)
        {
            string via = PendingVia;
            PendingVia = null;
            try
            {
                if (!ScMembership.Separate || string.IsNullOrEmpty(pid)) return;
                ScWorldState ws = Ws;
                bool oldFounded = old != null && !old.Personal && ScCommunities.ById(old.Id) != null;
                if (target != null && !target.Personal)
                {
                    ClearOptOut(pid, target.Id);
                    if (ScMembership.IsOwner(target, pid, null))
                    {
                        ws.JoinedHow.Remove(pid);
                        ws.JoinedVia.Remove(pid);
                    }
                    else
                    {
                        string how = reason == ScMembership.ByAlly ? HowAlly : reason == ScMembership.ByParty ? HowParty : HowAdmin;
                        ws.JoinedHow[pid] = how;
                        if (how == HowAlly && !string.IsNullOrEmpty(via)) ws.JoinedVia[pid] = via;
                        else ws.JoinedVia.Remove(pid);
                    }
                    if (oldFounded && old.Id != target.Id && reason == ScMembership.ByAdmin) OptOut(pid, old.Id);
                    return;
                }
                ws.JoinedHow.Remove(pid);
                ws.JoinedVia.Remove(pid);
                if (!oldFounded) return;
                if (reason == ScMembership.ByLeave || reason == ScMembership.ByAdmin) OptOut(pid, old.Id);
                if (ScMembership.Mode == ScCommunityMode.PerPlayer && reason != ScMembership.ByAllyRemoved) ScAllies.QueueReconcile(pid);
            }
            catch (Exception e)
            {
                ScLog.Once("mp7-joins-noted", e);
            }
        }

        // ------------------------------------------------------------------ the save ----

        /// <summary>
        /// [membership]'s part AP lines (ScRegistry.BuildSaveText, after the player= lines), sorted: joined=pid:how[:via] for each
        /// listed player with a record who is in a founded community they do not own, optout=pid:cid for each community that exists. Ids are escaped (% : = ; and line breaks as
        /// %XX), so a line splits at its colons. A save with none of them writes nothing (an old save's text stays as it was).
        /// </summary>
        internal static void Write(StringBuilder sb, ScWorldState ws)
        {
            if (ws.JoinedHow.Count > 0)
            {
                var ids = new List<string>(ws.JoinedHow.Keys);
                ids.Sort(StringComparer.Ordinal);
                foreach (string id in ids)
                {
                    string how = ws.JoinedHow[id];
                    int cid;
                    if (string.IsNullOrEmpty(how) || !ws.Membership.TryGetValue(id, out cid)) continue;
                    CommunityState cs = ScCommunities.ById(cid);
                    if (cs == null || cs.Personal || ScMembership.IsOwner(cs, id, null)) continue;     // a record only means something for a founded community's non-owner
                    string via;
                    ws.JoinedVia.TryGetValue(id, out via);
                    sb.AppendLine("joined=" + Esc(id) + ":" + how + (how == HowAlly && !string.IsNullOrEmpty(via) ? ":" + Esc(via) : ""));
                }
            }
            if (ws.OptOuts.Count > 0)
            {
                var ids = new List<string>(ws.OptOuts.Keys);
                ids.Sort(StringComparer.Ordinal);
                foreach (string id in ids)
                    foreach (int cid in ws.OptOuts[id])
                        if (ScCommunities.ById(cid) != null) sb.AppendLine("optout=" + Esc(id) + ":" + cid.ToString(CultureInfo.InvariantCulture));
            }
        }

        /// <summary>
        /// ScRegistry.ParseMembership: a joined= or optout= line (true: handled here). A line that does not read is skipped with a
        /// warning (never a reason to call the file unreadable: the join records only steer later ally and party changes).
        /// </summary>
        internal static bool Parse(string key, string val)
        {
            if (key != "joined" && key != "optout") return false;
            try
            {
                ScWorldState ws = Ws;
                if (key == "joined")
                {
                    string[] f = (val ?? "").Split(':');
                    string id = f.Length >= 2 ? Unesc(f[0]).Trim() : "";
                    string how = f.Length >= 2 ? f[1].Trim() : "";
                    if (id.Length == 0 || f.Length > 3 || (how != HowAdmin && how != HowAlly && how != HowParty)) { ScLog.Warn("registry: skipped a membership line it cannot read: joined=" + val); return true; }
                    ws.JoinedHow[id] = how;
                    string via = f.Length == 3 ? Unesc(f[2]).Trim() : "";
                    if (how == HowAlly && via.Length > 0) ws.JoinedVia[id] = via;
                    return true;
                }
                int colon = (val ?? "").LastIndexOf(':');
                int cid;
                string pid = colon > 0 ? Unesc(val.Substring(0, colon)).Trim() : "";
                if (pid.Length == 0 || !int.TryParse(val.Substring(colon + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out cid) || cid < 0)
                {
                    ScLog.Warn("registry: skipped a membership line it cannot read: optout=" + val);
                    return true;
                }
                OptOut(pid, cid);
            }
            catch (Exception e)
            {
                ScLog.Once("mp7-joins-parse", e);
            }
            return true;
        }

        private static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            StringBuilder sb = null;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                bool bad = c == '%' || c == ':' || c == '=' || c == ';' || c == '\r' || c == '\n';
                if (bad && sb == null) sb = new StringBuilder(s.Substring(0, i));
                if (sb == null) continue;
                if (bad) sb.Append('%').Append(((int)c).ToString("X2", CultureInfo.InvariantCulture));
                else sb.Append(c);
            }
            return sb != null ? sb.ToString() : s;
        }

        private static string Unesc(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('%') < 0) return s ?? "";
            return Uri.UnescapeDataString(s);
        }

        // ------------------------------------------------------------------ shared helpers ----

        /// <summary>A player's name for tells and the log: their display name online, else players.xml's, else the id.</summary>
        internal static string NameOf(string pid)
        {
            if (string.IsNullOrEmpty(pid)) return "?";
            EntityPlayer p = ScPlayers.ById(pid);
            if (p != null && !string.IsNullOrEmpty(p.PlayerDisplayName)) return p.PlayerDisplayName;
            try
            {
                GameManager gm = GameManager.Instance;
                PersistentPlayerList pp = gm != null ? gm.persistentPlayers : null;
                PlatformUserIdentifierAbs u = pp != null ? PlatformUserIdentifierAbs.FromCombinedString(pid, false) : null;
                PersistentPlayerData d = u != null ? pp.GetPlayerData(u) : null;
                string n = d != null && d.PlayerName != null && d.PlayerName.AuthoredName != null ? d.PlayerName.AuthoredName.Text : null;
                if (!string.IsNullOrEmpty(n)) return n;
            }
            catch (Exception e)
            {
                ScLog.Once("mp7-joins-name", e);
            }
            return pid;
        }

        /// <summary>The founded community the player is in (server's map), or null - none, or a personal record.</summary>
        internal static CommunityState FoundedOf(string pid)
        {
            if (string.IsNullOrEmpty(pid)) return null;
            CommunityState cs = ScCommunities.ById(ScPlayers.CommunityOfId(pid));
            return cs != null && !cs.Personal ? cs : null;
        }
    }

    /// <summary>
    /// Round MP7 (part AP), PerPlayer (the user's decision 6): a founder's land-claim allies who have no community join it; removing
    /// the ally takes an ally-joined player out. The ally store (persistentPlayers.Allies: saved in players.xml, offline players
    /// included, keyed by the platform id SC uses) is read through AllyView.
    /// </summary>
    public static class ScAllies
    {
        /// <summary>The ally store as platform id strings: the server's persistentPlayers.Allies (Live), or an offline harness's.</summary>
        internal sealed class AllyView
        {
            private readonly Func<string, string, bool> isAlly;
            private readonly Func<string, IEnumerable<string>> alliesOf;

            internal AllyView(Func<string, string, bool> isAlly, Func<string, IEnumerable<string>> alliesOf)
            {
                this.isAlly = isAlly;
                this.alliesOf = alliesOf;
            }

            internal bool IsAlly(string a, string b)
            {
                return !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b) && !string.Equals(a, b, StringComparison.Ordinal) && isAlly(a, b);
            }

            /// <summary>The player's allies (copied: a move during the walk never trips the store's enumerator).</summary>
            internal List<string> AlliesOf(string a)
            {
                var list = new List<string>();
                if (string.IsNullOrEmpty(a)) return list;
                foreach (string s in alliesOf(a))
                    if (!string.IsNullOrEmpty(s) && !string.Equals(s, a, StringComparison.Ordinal) && !list.Contains(s)) list.Add(s);
                return list;
            }

            /// <summary>The server's ally store, or null when there is none (no game, a client before its world).</summary>
            internal static AllyView Live()
            {
                GameManager gm = GameManager.Instance;
                PersistentPlayerList pp = gm != null ? gm.persistentPlayers : null;
                AllyStore store = pp != null ? pp.Allies : null;
                if (store == null) return null;
                var ids = new Dictionary<string, PlatformUserIdentifierAbs>(StringComparer.Ordinal);
                Func<string, PlatformUserIdentifierAbs> id = s =>
                {
                    PlatformUserIdentifierAbs u;
                    if (!ids.TryGetValue(s, out u)) ids[s] = u = PlatformUserIdentifierAbs.FromCombinedString(s, false);
                    return u;
                };
                return new AllyView(
                    (a, b) =>
                    {
                        PlatformUserIdentifierAbs ua = id(a), ub = id(b);
                        return ua != null && ub != null && store.IsAlly(ua, ub);
                    },
                    a =>
                    {
                        var r = new List<string>();
                        PlatformUserIdentifierAbs ua = id(a);
                        if (ua != null)
                            foreach (PlatformUserIdentifierAbs u in store.EnumerateAllies(ua))
                                if (u != null && !string.IsNullOrEmpty(u.CombinedString)) r.Add(u.CombinedString);
                        return r;
                    });
            }
        }

        /// <summary>PerPlayer, the registry loaded (ScMembership.Separate: the server's).</summary>
        internal static bool On { get { return ScMembership.Separate && ScMembership.Mode == ScCommunityMode.PerPlayer; } }

        // ------------------------------------------------------------------ the ally event ----

        private static bool subscribed;

        /// <summary>ModApi.InitMod, once per game launch: the vanilla ally event (static; it fires on every machine, acted on only on the server).</summary>
        public static void Subscribe()
        {
            if (subscribed) return;
            subscribed = true;
            PersistentPlayerList.OnPersistentAllyChangeEvent += OnAllyChange;
        }

        /// <summary>
        /// PersistentPlayerList.OnPersistentAllyChangeEvent: raised by AllyStore.AllyUpdateResponse after the store changed, twice per
        /// change (source and target swapped, each with its own event), never at load. Now allies (IncomingAccepted /
        /// OutgoingAccepted): Made(source, target) - this call's source founded a community, its target joins; the other call
        /// asks the other way round. No longer allies (AllyRemoved / RemovedByAlly): Gone(source, target) - this call's target may
        /// lose access. Both are idempotent (the second call finds nothing left to do).
        /// </summary>
        private static void OnAllyChange(PlatformUserIdentifierAbs source, PlatformUserIdentifierAbs target, AllyStore.AllyEvent ev)
        {
            try
            {
                if (!ScCommunity.IsHost || !On) return;
                bool made = ev == AllyStore.AllyEvent.IncomingAccepted || ev == AllyStore.AllyEvent.OutgoingAccepted;
                bool gone = ev == AllyStore.AllyEvent.AllyRemoved || ev == AllyStore.AllyEvent.RemovedByAlly;
                if (!made && !gone) return;
                string a = source != null ? source.CombinedString : null;
                string b = target != null ? target.CombinedString : null;
                if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return;
                if (!ThreadManager.IsMainThread())
                {
                    ThreadManager.AddSingleTaskMainThread("sc-ally-event", () => Handle(a, b, made));
                    return;
                }
                Handle(a, b, made);
            }
            catch (Exception e)
            {
                ScLog.Once("mp7-ally-event", e);
            }
        }

        private static void Handle(string a, string b, bool made)
        {
            try
            {
                if (!ScCommunity.IsHost || !On) return;
                AllyView v = AllyView.Live();
                if (v == null) return;
                if (made) Made(a, b, v);
                else Gone(a, b, v);
            }
            catch (Exception e)
            {
                ScLog.Once("mp7-ally-handle", e);
            }
        }

        /// <summary>
        /// a and b are allies now: when a founded a community and b is in none (a personal record, or no record offline), b joins it
        /// (merged; Join). A b in a founded community keeps it (an ally of two owners joins the first by event order). The link
        /// made again ends b's opt-out of a's community. True when b joined.
        /// </summary>
        internal static bool Made(string a, string b, AllyView v)
        {
            if (!On || v == null || !v.IsAlly(a, b)) return false;           // not allies (any more: a queued event)
            CommunityState d = ScCommunities.ById(ScMembership.FoundedBy(a));
            if (d == null) return false;                                     // a founded nothing: the pair's other call asks the other way
            if (ScJoins.FoundedOf(b) != null) return false;                  // b keeps the founded community they are in
            ScJoins.ClearOptOut(b, d.Id);
            return Join(b, d, a);
        }

        /// <summary>
        /// a and b are allies no more: a's community's opt-out for b ends (the link was removed: made again, b joins); and b, when
        /// ally-joined in a founded community whose link this was (its owner, or b's recorded link), loses access unless still
        /// allied with its owner or their link (LoseAccess: a fresh personal record, then the reconcile - another owner's ally joins
        /// that community). An owner and an admin-joined player are never taken out. True when b lost access.
        /// </summary>
        internal static bool Gone(string a, string b, AllyView v)
        {
            if (!On || v == null || string.Equals(a, b, StringComparison.Ordinal) || v.IsAlly(a, b)) return false;
            int ad = ScMembership.FoundedBy(a);
            if (ad != ScPlayers.NoCommunity) ScJoins.ClearOptOut(b, ad);
            CommunityState bc = ScJoins.FoundedOf(b);
            if (bc == null || ScMembership.IsOwner(bc, b, null) || !ScJoins.ByAlly(b)) return false;
            string owner = bc.Community.OwnerId ?? "", via = ScJoins.Via(b);
            if (!string.Equals(a, owner, StringComparison.Ordinal) && !string.Equals(a, via, StringComparison.Ordinal)) return false;
            if (Anchored(b, bc, v)) return false;
            return LoseAccess(b, bc, v);
        }

        /// <summary>
        /// An ally-joined player is still tied to the community: allied with its owner, or with their recorded link (the founder
        /// whose ally they became - so an admin's hand-over, sc community owner, does not drop the players who came in as the old
        /// owner's allies).
        /// </summary>
        private static bool Anchored(string pid, CommunityState cs, AllyView v)
        {
            string owner = cs.Community.OwnerId ?? "", via = ScJoins.Via(pid);
            return (owner.Length > 0 && v.IsAlly(pid, owner)) || (via.Length > 0 && v.IsAlly(pid, via));
        }

        // ------------------------------------------------------------------ the reconcile ----

        /// <summary>
        /// One player against the ally store (PerPlayer). In a founded community: an ally-joined player no longer tied to it
        /// (Anchored) loses access (then joins another owner's, as below). In none (a personal record, or no record offline): the
        /// lowest-id founded community whose owner is their ally and that they did not leave (ScJoins.OptedOut) takes them in.
        /// Returns 1 joined, -1 lost access, 0 nothing.
        /// </summary>
        internal static int Reconcile(string pid, AllyView v)
        {
            if (!On || v == null || string.IsNullOrEmpty(pid)) return 0;
            CommunityState cs = ScJoins.FoundedOf(pid);
            if (cs != null)
            {
                if (ScMembership.IsOwner(cs, pid, null) || !ScJoins.ByAlly(pid) || Anchored(pid, cs, v)) return 0;
                return LoseAccess(pid, cs, v) ? -1 : 0;
            }
            CommunityState best = null;
            string bestOwner = null;
            CommunityState[] all = ScCommunities.Array;
            for (int i = 0; i < all.Length; i++)
            {
                CommunityState d = all[i];
                if (!ScMembership.IsFounded(d) || (best != null && d.Id >= best.Id)) continue;
                string owner = d.Community.OwnerId;
                if (string.IsNullOrEmpty(owner) || string.Equals(owner, pid, StringComparison.Ordinal) || ScJoins.OptedOut(pid, d.Id)) continue;
                if (!v.IsAlly(pid, owner)) continue;
                best = d;
                bestOwner = owner;
            }
            if (best == null) return 0;
            return Join(pid, best, bestOwner) ? 1 : 0;
        }

        /// <summary>
        /// Every player the save lists and every ally of a founded community's owner (offline players included), each reconciled
        /// once (Reconcile), in platform id order. why: for the log. Returns the log's summary.
        /// </summary>
        internal static string ReconcileAll(AllyView v, string why)
        {
            if (!On || v == null) return "";
            var ids = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string pid in ScRegistry.WorldState.Membership.Keys) ids.Add(pid);
            CommunityState[] all = ScCommunities.Array;
            for (int i = 0; i < all.Length; i++)
                if (ScMembership.IsFounded(all[i]) && !string.IsNullOrEmpty(all[i].Community.OwnerId))
                    foreach (string y in v.AlliesOf(all[i].Community.OwnerId)) ids.Add(y);
            int joined = 0, lost = 0;
            foreach (string pid in ids)
            {
                int r = Reconcile(pid, v);
                if (r > 0) joined++;
                else if (r < 0) lost++;
            }
            string line = "community (allies): the reconcile " + why + " - " + ids.Count + " player(s) checked, " + joined + " joined a founder's community as their land claim ally, "
                          + lost + " lost access (no longer the ally)";
            if (joined + lost > 0) ScLog.Out(line);
            else if (ScConfig.Verbose) ScLog.Out(line);
            return line;
        }

        /// <summary>
        /// The player joins founded community d as the land-claim ally of linkPid (its founder): ScMembership.Move with ByAlly (a
        /// personal record merges; the tells; the save), the link recorded by ScJoins.Noted. True when they joined.
        /// </summary>
        private static bool Join(string pid, CommunityState d, string linkPid)
        {
            string who = ScJoins.NameOf(pid);
            ScMembership.MoveResult r;
            ScJoins.PendingVia = linkPid;
            try { r = ScMembership.Move(pid, d.Id, ScMembership.ByAlly, who); }
            finally { ScJoins.PendingVia = null; }
            if (!r.Ok)
            {
                ScLog.Warn("community (allies): " + who + " could not join " + d.Label + " as " + ScJoins.NameOf(linkPid) + "'s land claim ally: " + r.Error);
                return false;
            }
            ScLog.Out("community (allies): " + who + " joined " + d.Label + " as the land claim ally of " + ScJoins.NameOf(linkPid) + (r.Merged ? " (their own relations merged)" : ""));
            return true;
        }

        /// <summary>
        /// An ally-joined player loses access (ScMembership.Move to none, ByAllyRemoved: decision 8's hand-over, a fresh personal
        /// record, the tells), then the reconcile runs for them at once (another founder's ally joins that community). True when
        /// they lost access.
        /// </summary>
        private static bool LoseAccess(string pid, CommunityState cs, AllyView v)
        {
            string who = ScJoins.NameOf(pid);
            ScMembership.MoveResult r = ScMembership.Move(pid, ScPlayers.NoCommunity, ScMembership.ByAllyRemoved, who);
            if (!r.Ok)
            {
                ScLog.Warn("community (allies): " + who + " is no longer a land claim ally of " + ScMembership.Whose(cs) + ", but could not be taken out of " + cs.Label + ": " + r.Error);
                return false;
            }
            ScLog.Out("community (allies): " + who + " is no longer a land claim ally of " + ScMembership.Whose(cs) + " - out of " + cs.Label);
            Reconcile(pid, v);
            return true;
        }

        // ------------------------------------------------------------------ when it runs ----

        /// <summary>The reconcile at load is owed (OnRegistryLoaded): run at GameStartDone or the first spawn, whichever comes first.</summary>
        private static bool loadDue;
        /// <summary>Players whose reconcile is queued (QueueReconcile), so one change queues one.</summary>
        private static readonly HashSet<string> queued = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// ScMembership.OnRegistryLoaded (from the players.xml read, before the game holds the ally store it returns): the reconcile
        /// at load is owed.
        /// </summary>
        internal static void OnRegistryLoaded()
        {
            loadDue = true;
            queued.Clear();
        }

        /// <summary>ModApi's game-start steps (server): the reconcile at load (no ally event fires at load; allies may have changed while SC was not watching).</summary>
        public static void OnGameStartDone()
        {
            try { if (ScCommunity.IsHost) RunLoadReconcile(); }
            catch (Exception e) { ScLog.Once("mp7-allies-start", e); }
        }

        private static void RunLoadReconcile()
        {
            if (!loadDue || !ScRegistry.Loaded) return;
            if (!On) { loadDue = false; return; }
            AllyView v = AllyView.Live();
            if (v == null) return;                     // owed still: the first spawn tries again
            loadDue = false;
            ReconcileAll(v, "at load");
        }

        /// <summary>
        /// ScMembership.OnPlayerSpawned (server, PerPlayer/PerParty): the reconcile for the spawning player, at every spawn (allies
        /// can change while they are offline) - and the one at load first, when GameStartDone has not run it.
        /// </summary>
        internal static void OnPlayerSpawned(string pid)
        {
            try
            {
                if (!ScCommunity.IsHost || !On) return;
                RunLoadReconcile();
                AllyView v = AllyView.Live();
                if (v != null && !string.IsNullOrEmpty(pid)) Reconcile(pid, v);
            }
            catch (Exception e)
            {
                ScLog.Once("mp7-allies-spawn", e);
            }
        }

        /// <summary>The spawning player's platform id (OnPlayerSpawned's arguments: a client's info, the local player, or the body).</summary>
        internal static string SpawnedId(int entityId, ClientInfo ci, bool local)
        {
            try
            {
                if (ci != null) return ci.InternalId != null ? ci.InternalId.CombinedString ?? "" : "";
                if (local && ScCommunity.LocalPlayer != null) return ScPlayers.IdOf(ScCommunity.LocalPlayer);
                return ScPlayers.IdOf(ScPlayers.ByEntity(entityId));
            }
            catch (Exception e)
            {
                ScLog.Once("mp7-allies-spawnid", e);
                return "";
            }
        }

        /// <summary>
        /// ScMembership.Start (PerPlayer): the founder's land-claim allies in no community (personal records, or no record offline)
        /// join the new community. Returns how many.
        /// </summary>
        internal static int OnFounded(EntityPlayer founder, CommunityState cs)
        {
            try
            {
                if (!On || founder == null || cs == null || cs.Personal) return 0;
                AllyView v = AllyView.Live();
                return v != null ? Founded(ScPlayers.IdOf(founder), cs, v) : 0;
            }
            catch (Exception e)
            {
                ScLog.Once("mp7-allies-founded", e);
                return 0;
            }
        }

        /// <summary>OnFounded's body: founderPid's allies in no founded community, not opted out of cs, join cs. Returns how many.</summary>
        internal static int Founded(string founderPid, CommunityState cs, AllyView v)
        {
            if (!On || v == null || string.IsNullOrEmpty(founderPid) || cs == null || cs.Personal) return 0;
            int n = 0;
            foreach (string y in v.AlliesOf(founderPid))
            {
                if (ScJoins.FoundedOf(y) != null || ScJoins.OptedOut(y, cs.Id)) continue;
                if (Join(y, cs, founderPid)) n++;
            }
            return n;
        }

        /// <summary>ScMembership.SwitchMode (an old save switched to PerPlayer): the reconcile at once.</summary>
        internal static void OnModeSwitched()
        {
            try
            {
                if (!On) return;
                AllyView v = AllyView.Live();
                if (v != null) ReconcileAll(v, "after the mode switch");
            }
            catch (Exception e)
            {
                ScLog.Once("mp7-allies-switch", e);
            }
        }

        /// <summary>
        /// ScJoins.Noted (PerPlayer): the player left a founded community (Leave, an admin's move) - their reconcile runs on the main
        /// thread's next pass, after Move has finished (another founder's ally joins that community; the one they left keeps them out).
        /// </summary>
        internal static void QueueReconcile(string pid)
        {
            if (string.IsNullOrEmpty(pid) || !queued.Add(pid)) return;
            try
            {
                ThreadManager.AddSingleTaskMainThread("sc-allies-reconcile", () =>
                {
                    queued.Remove(pid);
                    try
                    {
                        if (!ScCommunity.IsHost || !On) return;
                        AllyView v = AllyView.Live();
                        if (v != null) Reconcile(pid, v);
                    }
                    catch (Exception e) { ScLog.Once("mp7-allies-queued", e); }
                });
            }
            catch (Exception e)
            {
                queued.Remove(pid);
                ScLog.Once("mp7-allies-queue", e);
            }
        }
    }

    /// <summary>
    /// Round MP7 (part AP), PerParty (the user's decision 7): a player in no community who is in a party holding one of a
    /// community's players joins it at the accept; one who leaves (or is kicked from) the community's party loses access, never its
    /// owner; the founder's party comes in at founding. Vanilla parties are session-only: the saved membership map stays the truth,
    /// party events are only its join and leave triggers (a disconnect changes nothing).
    /// </summary>
    public static class ScParties
    {
        /// <summary>PerParty, the registry loaded (the server's).</summary>
        internal static bool On { get { return ScMembership.Separate && ScMembership.Mode == ScCommunityMode.PerParty; } }

        /// <summary>
        /// After an accepted invite (the party's member ids, the invited player now among them): the invited player's own Leave
        /// opt-outs end (joining a party is their own consent), and the inviter's when the invite started the party (inviterNew).
        /// The party's community: the inviter's founded community; else the only one among the members; else the leader's; else
        /// the lowest id. Every member in no community (a personal record, or no record) who did not leave that community joins it
        /// (merged). A member in a founded community keeps it; when it is the invited player, both sides are told they are in
        /// different communities. Returns how many joined.
        /// </summary>
        internal static int Accepted(string invitedPid, string inviterPid, bool inviterNew, List<string> members, string leaderPid)
        {
            if (!On || members == null || string.IsNullOrEmpty(invitedPid)) return 0;
            ScJoins.ClearOptOuts(invitedPid);
            if (inviterNew) ScJoins.ClearOptOuts(inviterPid);
            return Joined(invitedPid, inviterPid, members, leaderPid);
        }

        /// <summary>
        /// After vanilla's AutoParty put the player into the server's one auto party (Party.ServerHandleAutoJoinParty, at their
        /// spawn): handled as an accepted invite with no inviter (the party's community: the only one among the members, else the
        /// leader's, else the lowest id; the same joins, opt-outs and tells). It is no act of their own - AutoParty does it at every
        /// login - so their Leave opt-outs hold (a player who left a community is not pulled back in at their next login). Returns
        /// how many joined.
        /// </summary>
        internal static int AutoJoined(string joinerPid, List<string> members, string leaderPid)
        {
            if (!On || members == null || string.IsNullOrEmpty(joinerPid)) return 0;
            return Joined(joinerPid, "", members, leaderPid);
        }

        /// <summary>Accepted's and AutoJoined's joins and tells (the opt-outs already handled for the player's own act).</summary>
        private static int Joined(string invitedPid, string inviterPid, List<string> members, string leaderPid)
        {
            CommunityState target = ScJoins.FoundedOf(inviterPid);
            if (target == null)
            {
                var found = new SortedDictionary<int, CommunityState>();
                foreach (string m in members)
                {
                    CommunityState c = ScJoins.FoundedOf(m);
                    if (c != null) found[c.Id] = c;
                }
                if (found.Count > 1) target = ScJoins.FoundedOf(leaderPid);
                if (target == null) foreach (CommunityState c in found.Values) { target = c; break; }
            }
            int n = 0;
            if (target != null)
            {
                foreach (string m in members)
                {
                    if (string.IsNullOrEmpty(m) || ScJoins.FoundedOf(m) != null) continue;
                    // round MP7 (part AP, decided at the merge - kept as built): the design's "Leave sticks" is written for PerPlayer; it is
                    // applied to parties too, or a player who pressed Leave would be pulled straight back in by the next invite anyone in
                    // their party accepts. Their own accept (or the invite that starts their party) ends it: joining a party is their own
                    // consent (an AutoParty join is not)
                    if (ScJoins.OptedOut(m, target.Id))
                    {
                        ScLog.Out("community (party): " + ScJoins.NameOf(m) + " left " + target.Label + " earlier - not pulled back in by another player's party invite (their own next party join is)");
                        continue;
                    }
                    if (Join(m, target, "their party holds " + ScMembership.Whose(target) + "'s community's players")) n++;
                }
            }
            CommunityState mine = ScJoins.FoundedOf(invitedPid);
            if (mine != null) TellDifferent(invitedPid, mine, members);
            return n;
        }

        /// <summary>
        /// The invited player keeps their founded community (mine): they are told for each other community in the party, and those
        /// communities' players in the party are told about them (decision 7: a mixed party is allowed and changes nothing).
        /// </summary>
        private static void TellDifferent(string pid, CommunityState mine, List<string> members)
        {
            EntityPlayer me = ScPlayers.ById(pid);
            string myName = ScJoins.NameOf(pid);
            var told = new HashSet<int>();
            foreach (string m in members)
            {
                if (string.IsNullOrEmpty(m) || string.Equals(m, pid, StringComparison.Ordinal)) continue;
                CommunityState other = ScJoins.FoundedOf(m);
                if (other == null || other.Id == mine.Id) continue;
                if (told.Add(other.Id))
                {
                    if (me != null) ScCommunity.TellKey(me, "scMp7PartyOtherYou", "", ScMembership.Whose(other), ScMembership.Whose(mine));
                    ScLog.Out("community (party): " + myName + " joined a party with players of " + other.Label + " - stays in " + mine.Label + " (a mixed party changes nothing)");
                }
                EntityPlayer them = ScPlayers.ById(m);
                if (them != null) ScCommunity.TellKey(them, "scMp7PartyOtherTell", "", myName, ScMembership.Whose(mine));
            }
        }

        /// <summary>
        /// After a leave or a kick (how: "left" / "was kicked from", for the log; before: the party's member ids before it): the
        /// player loses access to their founded community when they joined it by party and the party they left held another of
        /// its players (the community's party) - never its owner, never an admin-joined player (or one placed before part AP).
        /// The remaining members keep it (decision 7). True when they lost access.
        /// </summary>
        internal static bool Left(string pid, List<string> before, string how)
        {
            if (!On || string.IsNullOrEmpty(pid)) return false;
            CommunityState cs = ScJoins.FoundedOf(pid);
            if (cs == null) return false;
            string who = ScJoins.NameOf(pid);
            EntityPlayer online = ScPlayers.ById(pid);
            if (ScMembership.IsOwner(cs, pid, online != null ? online.PlayerDisplayName : null))
            {
                ScLog.Out("community (party): " + who + " " + how + " the party - they own " + cs.Label + " and keep it (the others keep it too)");
                return false;
            }
            // round MP7 (part AP, decided at the merge - kept as built): the design says a leave or kick loses access "unless they are
            // the owner". Two exceptions are built: (1) only when the party they left held another of their community's players - leaving a party of other
            // communities' players only (a mixed party) has nothing to do with theirs; (2) only a party-joined player - one an admin
            // placed (or placed before part AP, or by the mode switch) is kept, as the ally rule keeps admin-joined players (the
            // design's risk 3).
            bool held = false;
            if (before != null)
                foreach (string m in before)
                    if (!string.IsNullOrEmpty(m) && !string.Equals(m, pid, StringComparison.Ordinal) && ScPlayers.CommunityOfId(m) == cs.Id) { held = true; break; }
            if (!held) return false;                    // not the community's party (only other communities' players in it)
            if (!ScJoins.ByParty(pid))
            {
                ScLog.Out("community (party): " + who + " " + how + " " + cs.Label + "'s party but stays in it (" + (ScJoins.How(pid).Length > 0 ? "joined by an admin" : "in it from before parties counted") + ")");
                return false;
            }
            ScMembership.MoveResult r = ScMembership.Move(pid, ScPlayers.NoCommunity, ScMembership.ByPartyLeft, who);
            if (!r.Ok)
            {
                ScLog.Warn("community (party): " + who + " " + how + " " + cs.Label + "'s party, but could not be taken out: " + r.Error);
                return false;
            }
            ScLog.Out("community (party): " + who + " " + how + " " + cs.Label + "'s party - out of it (the others keep it)");
            return true;
        }

        /// <summary>
        /// ScMembership.Start (PerParty): the founder's party members in no community (and not opted out of it) join the new
        /// community. Returns how many.
        /// </summary>
        internal static int OnFounded(EntityPlayer founder, CommunityState cs)
        {
            try
            {
                if (!On || founder == null || cs == null || cs.Personal) return 0;
                Party party = founder.Party;
                if (party == null || party.MemberList == null) return 0;
                return Founded(ScPlayers.IdOf(founder), cs, IdsOf(party.MemberList));
            }
            catch (Exception e)
            {
                ScLog.Once("mp7-party-founded", e);
                return 0;
            }
        }

        /// <summary>OnFounded's body: each of the party's member ids but the founder's, in no founded community and not opted out of cs, joins cs.</summary>
        internal static int Founded(string founderPid, CommunityState cs, List<string> members)
        {
            if (!On || cs == null || cs.Personal || members == null) return 0;
            int n = 0;
            foreach (string m in members)
            {
                if (string.IsNullOrEmpty(m) || string.Equals(m, founderPid, StringComparison.Ordinal)) continue;
                if (ScJoins.FoundedOf(m) != null || ScJoins.OptedOut(m, cs.Id)) continue;
                if (Join(m, cs, "their party's " + ScJoins.NameOf(founderPid) + " started it")) n++;
            }
            return n;
        }

        /// <summary>The player joins community d by party (ScMembership.Move, ByParty: a personal record merges; the tells; the save). why: for the log.</summary>
        private static bool Join(string pid, CommunityState d, string why)
        {
            string who = ScJoins.NameOf(pid);
            ScMembership.MoveResult r = ScMembership.Move(pid, d.Id, ScMembership.ByParty, who);
            if (!r.Ok)
            {
                ScLog.Warn("community (party): " + who + " could not join " + d.Label + ": " + r.Error);
                return false;
            }
            ScLog.Out("community (party): " + who + " joined " + d.Label + " - " + why + (r.Merged ? " (their own relations merged)" : ""));
            return true;
        }

        /// <summary>
        /// A new party leader: no membership change (decision 7). When they now act for the owner (ScOwnerRule.PartyLeaderMayAct:
        /// they are of the community and its owner is offline), they are told.
        /// </summary>
        internal static void LeadChanged(EntityPlayer lead)
        {
            if (!On || lead == null) return;
            CommunityState cs = ScJoins.FoundedOf(ScPlayers.IdOf(lead));
            if (cs == null || !ScOwnerRule.PartyLeaderMayAct(lead, cs.Id)) return;
            ScCommunity.TellKey(lead, "scMp7PartyHelper", "", ScMembership.Whose(cs));
            ScLog.Out("community (party): " + lead.PlayerDisplayName + " leads the party now and acts for " + ScMembership.Whose(cs) + " (the owner is away) in " + cs.Label);
        }

        /// <summary>The community's owner is online (by platform id; a save from before MP0 knows only the display name).</summary>
        internal static bool OwnerOnline(CommunityState cs)
        {
            if (cs == null) return false;
            Community c = cs.Community;
            if (!string.IsNullOrEmpty(c.OwnerId)) return ScPlayers.ById(c.OwnerId) != null;
            if (string.IsNullOrEmpty(c.Owner)) return false;
            List<EntityPlayer> all = ScPlayers.All;
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && string.Equals(all[i].PlayerDisplayName, c.Owner, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>The party members' platform ids (copied; no record is made here).</summary>
        private static List<string> IdsOf(List<EntityPlayer> list)
        {
            var ids = new List<string>();
            if (list == null) return ids;
            foreach (EntityPlayer p in list.ToArray())
            {
                string id = ScPlayers.IdOf(p);
                if (id.Length > 0 && !ids.Contains(id)) ids.Add(id);
            }
            return ids;
        }

        // ------------------------------------------------------------------ the vanilla party's server handlers ----
        // Each runs on the server (a client's NetPackagePartyActions, or the host's own players list, XUiC_PlayersListEntry, which
        // calls the same statics). Single player never reaches them (no other player to invite). Each acts only on the method's
        // own player argument: a two-player party dissolving also removes the other player, who keeps their community.

        // static void ServerHandleAcceptInvite(EntityPlayer invitedBy, EntityPlayer invitedEntity) - Party.cs:428
        [HarmonyPatch(typeof(Party), nameof(Party.ServerHandleAcceptInvite), new[] { typeof(EntityPlayer), typeof(EntityPlayer) })]
        public static class Patch_AcceptInvite
        {
            [HarmonyPrefix]
            public static void Prefix(EntityPlayer invitedBy, out bool __state)
            {
                __state = false;
                try { __state = On && invitedBy != null && invitedBy.Party == null; }       // the invite starts a party: the inviter's own act too
                catch (Exception e) { ScLog.Once("mp7-party-accept-pre", e); }
            }

            [HarmonyPostfix]
            public static void Postfix(EntityPlayer invitedBy, EntityPlayer invitedEntity, bool __state)
            {
                try
                {
                    if (!ScCommunity.IsHost || !On || invitedEntity == null) return;
                    Party party = invitedEntity.Party;
                    if (party == null || !party.ContainsMember(invitedEntity)) return;     // a full party: nobody joined
                    Accepted(ScPlayers.IdOf(invitedEntity), ScPlayers.IdOf(invitedBy), __state, IdsOf(party.MemberList), ScPlayers.IdOf(party.Leader));
                }
                catch (Exception e)
                {
                    ScLog.Once("mp7-party-accept", e);
                }
            }
        }

        // static void ServerHandleLeaveParty(EntityPlayer player, int entityID) - Party.cs:514
        [HarmonyPatch(typeof(Party), nameof(Party.ServerHandleLeaveParty), new[] { typeof(EntityPlayer), typeof(int) })]
        public static class Patch_LeaveParty
        {
            [HarmonyPrefix]
            public static void Prefix(EntityPlayer player, out List<string> __state)
            {
                __state = null;
                try { if (On && ScCommunity.IsHost && player != null && player.Party != null) __state = IdsOf(player.Party.MemberList); }
                catch (Exception e) { ScLog.Once("mp7-party-leave-pre", e); }
            }

            [HarmonyPostfix]
            public static void Postfix(EntityPlayer player, List<string> __state)
            {
                try
                {
                    if (__state == null || player == null || !On) return;
                    if (player.Party != null && player.Party.ContainsMember(player)) return;   // still in it: nothing happened
                    Left(ScPlayers.IdOf(player), __state, "left");
                }
                catch (Exception e)
                {
                    ScLog.Once("mp7-party-leave", e);
                }
            }
        }

        // static void ServerHandleKickParty(int entityID) - Party.cs:477 (the kicked player's entity id; vanilla never checks the
        // kicker leads the party: MP7_DESIGN.md risk 5)
        [HarmonyPatch(typeof(Party), nameof(Party.ServerHandleKickParty), new[] { typeof(int) })]
        public static class Patch_KickParty
        {
            [HarmonyPrefix]
            public static void Prefix(int entityID, out List<string> __state)
            {
                __state = null;
                try
                {
                    if (!On || !ScCommunity.IsHost) return;
                    EntityPlayer p = ScPlayers.ByEntity(entityID);
                    if (p != null && p.Party != null) __state = IdsOf(p.Party.MemberList);
                }
                catch (Exception e) { ScLog.Once("mp7-party-kick-pre", e); }
            }

            [HarmonyPostfix]
            public static void Postfix(int entityID, List<string> __state)
            {
                try
                {
                    if (__state == null || !On) return;
                    EntityPlayer p = ScPlayers.ByEntity(entityID);
                    if (p == null || (p.Party != null && p.Party.ContainsMember(p))) return;
                    Left(ScPlayers.IdOf(p), __state, "was kicked from");
                }
                catch (Exception e)
                {
                    ScLog.Once("mp7-party-kick", e);
                }
            }
        }

        // static void ServerHandleAutoJoinParty(EntityPlayer joiningEntity) - Party.cs:596: the AutoParty game setting puts every
        // player into the server's one auto party at their spawn (a client's JoinAutoParty package, or the host's own spawn; also in
        // single player, where the party holds only that player and nothing joins)
        [HarmonyPatch(typeof(Party), nameof(Party.ServerHandleAutoJoinParty), new[] { typeof(EntityPlayer) })]
        public static class Patch_AutoJoinParty
        {
            [HarmonyPostfix]
            public static void Postfix(EntityPlayer joiningEntity)
            {
                try
                {
                    if (!ScCommunity.IsHost || !On || joiningEntity == null) return;
                    Party party = joiningEntity.Party;
                    if (party == null || !party.ContainsMember(joiningEntity)) return;     // a full party: nobody joined
                    AutoJoined(ScPlayers.IdOf(joiningEntity), IdsOf(party.MemberList), ScPlayers.IdOf(party.Leader));
                }
                catch (Exception e)
                {
                    ScLog.Once("mp7-party-autojoin", e);
                }
            }
        }

        // static void ServerHandleChangeLead(EntityPlayer newHost) - Party.cs:457
        [HarmonyPatch(typeof(Party), nameof(Party.ServerHandleChangeLead), new[] { typeof(EntityPlayer) })]
        public static class Patch_ChangeLead
        {
            [HarmonyPostfix]
            public static void Postfix(EntityPlayer newHost)
            {
                try
                {
                    if (!ScCommunity.IsHost || !On || newHost == null || newHost.Party == null) return;
                    if (!ReferenceEquals(newHost.Party.Leader, newHost)) return;
                    LeadChanged(newHost);
                }
                catch (Exception e)
                {
                    ScLog.Once("mp7-party-lead", e);
                }
            }
        }
    }
}
