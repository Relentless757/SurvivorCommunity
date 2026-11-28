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
    // Shared mode: nothing here runs. Single player: no ally event fires (no other player) and the reconciles find no ally; with
    // the game's AutoParty setting it does reach ServerHandleAutoJoinParty, in a party of one, where nothing joins (round MP7
    // (review AP/F): n3). Server, main thread.

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

        /// <summary>
        /// Round MP7 (review AP/F): m2 - the player owns their community now (ScMembership.SetOwner's hand-over): an owner has no join
        /// record (as Noted keeps it), so a stale "ally" or "party" one goes - else it would take a later owner's player out (the
        /// next hand-over) or vanish only across a restart (Write drops an owner's line). A former owner keeps no record either: kept,
        /// like a founder.
        /// </summary>
        internal static void OwnerSet(string pid)
        {
            if (string.IsNullOrEmpty(pid)) return;
            Ws.JoinedHow.Remove(pid);
            Ws.JoinedVia.Remove(pid);
        }

        /// <summary>
        /// Round MP7 (review AP/F): m3 - a community that has its owner now (sc community owner's hand-over, or an admin-made community
        /// started from the radial: ScMembership.Start's first branch), as at founding: PerPlayer - its owner's land-claim allies in
        /// no community join it (ScAllies.Founded); PerParty - the party of its owner online (else of actor, when actor is of the
        /// community: a party leader acting for an absent owner) brings its members in no community in (ScParties.OnFounded).
        /// Nothing in Shared mode or for a personal record.
        /// </summary>
        internal static void OwnerHooks(CommunityState cs, EntityPlayer actor)
        {
            try
            {
                if (cs == null || cs.Personal || !ScMembership.Separate || ScCommunities.ById(cs.Id) == null) return;
                string owner = cs.Community.OwnerId ?? "";
                if (ScParties.On)
                {
                    EntityPlayer lead = owner.Length > 0 ? ScPlayers.ById(owner) : null;
                    if (lead == null && actor != null && ScPlayers.CommunityOfId(ScPlayers.IdOf(actor)) == cs.Id) lead = actor;
                    if (lead != null) ScParties.OnFounded(lead, cs);
                }
                if (ScAllies.On && owner.Length > 0)
                {
                    ScAllies.AllyView v = ScAllies.LiveView();
                    if (v != null) ScAllies.Founded(owner, cs, v);
                }
            }
            catch (Exception e)
            {
                ScLog.Once("mp7-joins-ownerhooks", e);
            }
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

        /// <summary>
        /// Round MP7 (review AP/F): where the entry points get the ally store - the server's (AllyView.Live); an offline harness puts
        /// its own here to drive the hooks (founding, the hand-over, the switch).
        /// </summary>
        internal static Func<AllyView> LiveView = AllyView.Live;

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
                AllyView v = LiveView();
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
            AllyView v = LiveView();
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
                AllyView v = LiveView();
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
                AllyView v = LiveView();
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
                AllyView v = LiveView();
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
                        AllyView v = LiveView();
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
        /// Round MP7 (review AP/F): m4 - invitePending: the server knew of the invite. Without it a modified client could join any
        /// community's party and so the community: vanilla's party join stands, SC's community join does not happen (logged).
        /// Round MP7 (final check): M2 - invitePending is SC's own invite ledger's verdict (AcceptGenuine: inviter to invited, and
        /// the invited player's own act); partyId: the party they joined (Party.PartyID). A genuine accept verifies the invited
        /// player's party join, and the inviter's when the invite started the party; only verified members are swept in, and the
        /// inviter's or leader's community is the party's only when that player is verified - so a player who got into a party
        /// with no genuine invite never joins, nor steers, a community through it until they leave it.
        /// </summary>
        internal static int Accepted(string invitedPid, string inviterPid, bool inviterNew, List<string> members, string leaderPid, bool invitePending, int partyId)
        {
            if (!On || members == null || string.IsNullOrEmpty(invitedPid)) return 0;
            if (!invitePending)
            {
                Unverify(invitedPid);
                WarnOnce(invitedPid, "accept", "community (party): " + ScJoins.NameOf(invitedPid) + " joined " + ScJoins.NameOf(inviterPid)
                         + "'s party with no invite SC saw them get (a modified client?) - the party join stands, no community join (said once per player)");
                return 0;
            }
            Verify(invitedPid, partyId);
            if (inviterNew) Verify(inviterPid, partyId);
            ScJoins.ClearOptOuts(invitedPid);
            if (inviterNew) ScJoins.ClearOptOuts(inviterPid);
            return Joined(invitedPid, IsVerified(inviterPid, partyId) ? inviterPid : "", VerifiedOnly(members, partyId), IsVerified(leaderPid, partyId) ? leaderPid : "");
        }

        /// <summary>
        /// After vanilla's AutoParty put the player into the server's one auto party (Party.ServerHandleAutoJoinParty, at their
        /// spawn): handled as an accepted invite with no inviter (the party's community: the only one among the members, else the
        /// leader's, else the lowest id; the same joins, opt-outs and tells). It is no act of their own - AutoParty does it at every
        /// login - so their Leave opt-outs hold (a player who left a community is not pulled back in at their next login). Returns
        /// how many joined.
        /// Round MP7 (review AP/F): m4 - autoPartyOn: the game's AutoParty setting (vanilla's JoinAutoParty package never checks it, and
        /// party 1 is then just the first party made this session): off, the party join stands and nothing else happens (logged).
        /// Round MP7 (final check): M2 - genuine: the AutoParty setting on and the joiner's own package (or the host's own spawn);
        /// partyId: the auto party. A genuine auto join verifies the joiner; only verified members are swept in (as Accepted).
        /// </summary>
        internal static int AutoJoined(string joinerPid, List<string> members, string leaderPid, bool genuine, int partyId)
        {
            if (!On || members == null || string.IsNullOrEmpty(joinerPid)) return 0;
            if (!genuine)
            {
                Unverify(joinerPid);
                WarnOnce(joinerPid, "autojoin", "community (party): " + ScJoins.NameOf(joinerPid) + " was put into the auto party with the AutoParty setting off, or by "
                         + "a package not their own (a modified client?) - the party join stands, no community join (said once per player)");
                return 0;
            }
            Verify(joinerPid, partyId);
            return Joined(joinerPid, "", VerifiedOnly(members, partyId), IsVerified(leaderPid, partyId) ? leaderPid : "");
        }

        // ---- round MP7 (final check): M1 + M2 - SC's own invite ledger and the verified party joins ----

        /// <summary>
        /// Round MP7 (final check): M1 + M2 - the party invites SC saw sent, (inviter, invited) by entity id, for the session: a client's
        /// SendInvite counts only when the package came from the inviter (Patch_PartyPackage: vanilla checks no sender), the host's own
        /// invites from where it builds the package (Patch_PartyPackageSetup: its players list, the spawn window, the auto party with
        /// land claim allies). An entry ends at the accept, when either player disconnects, or with the session - never through
        /// vanilla's invite wipe (GameManager.RemovePartyInvitesFromAllPlayers clears the host's other invites at every accept).
        /// </summary>
        private static readonly HashSet<long> ledger = new HashSet<long>();
        /// <summary>Round MP7 (final check): M2 - platform id -> the party (PartyID) whose join SC verified for them, this session.</summary>
        private static readonly Dictionary<string, int> verified = new Dictionary<string, int>(StringComparer.Ordinal);
        /// <summary>Round MP7 (final check): n2 - the forged joins already warned about (player id and kind), once per player per session.</summary>
        private static readonly HashSet<string> warned = new HashSet<string>(StringComparer.Ordinal);
        /// <summary>
        /// Round MP7 (final check): M2 - the entity id of the client whose NetPackagePartyActions the server is processing (0: none - the
        /// host's own UI or spawn; -1: a client with no body), set by Patch_PartyPackage around ProcessPackage. Main thread.
        /// </summary>
        private static int packageSender;

        private static long Key(int by, int to) { return ((long)by << 32) | (uint)to; }

        /// <summary>
        /// An invite: inviter byEid to toEid, made by senderEid (a client's package's sender, or the host's own entity for its UI).
        /// Recorded only when the sender is the inviter. True when recorded.
        /// </summary>
        internal static bool NoteInvite(int senderEid, int byEid, int toEid)
        {
            if (byEid <= 0 || toEid <= 0 || byEid == toEid) return false;
            if (senderEid != byEid)
            {
                WarnOnce("eid" + senderEid, "invite", "community (party): a party invite in another player's name (entity " + senderEid + " sent it as " + byEid + " to " + toEid
                         + ") - not counted for a community (a modified client?)");
                return false;
            }
            ledger.Add(Key(byEid, toEid));
            return true;
        }

        /// <summary>
        /// The accept of byEid's invite by toEid counts for SC: the ledger holds byEid to toEid, and the invited player acted
        /// (consentEid, the accepting package's sender or the host's own entity, is toEid) - or both invited each other (vanilla's
        /// mutual invite, which accepts either way round).
        /// </summary>
        internal static bool AcceptGenuine(int byEid, int toEid, int consentEid)
        {
            if (!ledger.Contains(Key(byEid, toEid))) return false;
            return consentEid == toEid || ledger.Contains(Key(toEid, byEid));
        }

        /// <summary>An accept used the invite: both directions end.</summary>
        internal static void Consumed(int byEid, int toEid)
        {
            ledger.Remove(Key(byEid, toEid));
            ledger.Remove(Key(toEid, byEid));
        }

        /// <summary>ModApi's disconnect handler (server): the player's invites (either way) and party verification end.</summary>
        internal static void ForgetPlayer(int eid, string pid)
        {
            if (eid > 0) ledger.RemoveWhere(k => (int)(k >> 32) == eid || (int)(k & 0xffffffffL) == eid);
            Unverify(pid);
        }

        internal static void Verify(string pid, int partyId) { if (!string.IsNullOrEmpty(pid)) verified[pid] = partyId; }
        internal static void Unverify(string pid) { if (!string.IsNullOrEmpty(pid)) verified.Remove(pid); }

        internal static bool IsVerified(string pid, int partyId)
        {
            int at;
            return !string.IsNullOrEmpty(pid) && verified.TryGetValue(pid, out at) && at == partyId;
        }

        /// <summary>The members whose join to party partyId SC verified (a sweep's only candidates).</summary>
        private static List<string> VerifiedOnly(List<string> members, int partyId)
        {
            var list = new List<string>();
            if (members == null) return list;
            foreach (string m in members) if (IsVerified(m, partyId)) list.Add(m);
            return list;
        }

        private static void WarnOnce(string who, string kind, string line)
        {
            if (warned.Add(kind + "|" + (who ?? ""))) ScLog.Warn(line);
        }

        /// <summary>Who acts for an accept or an auto join now: the package's sender, else the host's own player (its UI or spawn), else -1.</summary>
        private static int ActorNow()
        {
            if (packageSender != 0) return packageSender;
            EntityPlayerLocal lp = ScCommunity.LocalPlayer;
            return lp != null ? lp.entityId : -1;
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

        /// <summary>Round MP7 (review AP/F): m5 - the player pairs already told they are in different communities this session.</summary>
        private static readonly HashSet<string> toldPairs = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Round MP7 (final check): n1 - a pair and their two communities, so a pair is told again after one of them changes community.</summary>
        private static string PairKey(string a, int ca, string b, int cb)
        {
            string x = a + "@" + ca, y = b + "@" + cb;
            return string.CompareOrdinal(x, y) <= 0 ? x + "\n" + y : y + "\n" + x;
        }

        /// <summary>
        /// The invited player keeps their founded community (mine): they are told for each other community in the party, and those
        /// communities' players in the party are told about them (decision 7: a mixed party is allowed and changes nothing).
        /// Round MP7 (review AP/F): m5 - once per player pair per session (AutoParty joins at every login, and a client may send its
        /// auto join again): a pair told already is skipped, and the player is told about a community only for a pair new to them.
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
                if (!toldPairs.Add(PairKey(pid, mine.Id, m, other.Id))) continue;
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
            Unverify(pid);                              // round MP7 (final check): M2 - out of the party: their verified join ends with it
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
                return Founded(ScPlayers.IdOf(founder), cs, IdsOf(party.MemberList), party.PartyID);
            }
            catch (Exception e)
            {
                ScLog.Once("mp7-party-founded", e);
                return 0;
            }
        }

        /// <summary>
        /// OnFounded's body: each of the party's member ids but the founder's, in no founded community and not opted out of cs, joins
        /// cs. Round MP7 (final check): M2 - only members whose join to this party (partyId) SC verified (OwnerHooks comes here too).
        /// </summary>
        internal static int Founded(string founderPid, CommunityState cs, List<string> members, int partyId)
        {
            if (!On || cs == null || cs.Personal || members == null) return 0;
            int n = 0;
            foreach (string m in VerifiedOnly(members, partyId))
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

        /// <summary>Round MP7 (review AP/F): m5 - ScMembership.OnRegistryLoaded: a new session (the mixed-party tells may come again).</summary>
        internal static void OnRegistryLoaded()
        {
            toldPairs.Clear();
            ledger.Clear();                             // round MP7 (final check): M1 + M2 - the session's invites, verified joins and warnings
            verified.Clear();
            warned.Clear();
            packageSender = 0;
        }

        /// <summary>
        /// Round MP7 (review AP/F): m5 - ModApi's game-start steps (server): PerParty with the game's AutoParty setting on puts every
        /// player into one party at login, so every player with no community joins the first founder's community - and that merge
        /// cannot be undone (Leave gives an empty record). Said once per load, so an admin knows.
        /// </summary>
        public static void OnGameStartDone()
        {
            try
            {
                if (ScCommunity.IsHost && On && GameStats.GetBool(EnumGameStats.AutoParty))
                    ScLog.Warn("community mode PerParty with the game's AutoParty setting on: every player is put into one party at login, so everyone with no community joins "
                               + "the first founder's community (their own relations merged for good; Leave gives them a fresh, empty record)");
            }
            catch (Exception e)
            {
                ScLog.Once("mp7-party-start", e);
            }
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
        // calls the same statics). Single player reaches only the auto join (AutoParty on: a party of one, nothing joins - round MP7
        // (review AP/F): n3); the others need another player. Each acts only on the method's own player argument: a two-player
        // party dissolving also removes the other player, who keeps their community.

        // static void ServerHandleAcceptInvite(EntityPlayer invitedBy, EntityPlayer invitedEntity) - Party.cs:428
        [HarmonyPatch(typeof(Party), nameof(Party.ServerHandleAcceptInvite), new[] { typeof(EntityPlayer), typeof(EntityPlayer) })]
        public static class Patch_AcceptInvite
        {
            /// <summary>__state bits: the invite starts a party (the inviter's own act too), and the server knew of the invite.</summary>
            private const int InviterNew = 1, InvitePending = 2;

            [HarmonyPrefix]
            public static void Prefix(EntityPlayer invitedBy, EntityPlayer invitedEntity, out int __state)
            {
                __state = 0;
                try
                {
                    if (!On || invitedBy == null || invitedEntity == null) return;
                    if (invitedBy.Party == null) __state |= InviterNew;
                    // round MP7 (final check): M1 + M2 - SC's own ledger, not vanilla's partyInvites (the host's are wiped at every
                    // accept, its auto party invite is never listed, and a forged SendInvite lists one): the inviter invited the invited
                    // player, and the invited player acts now (their package, or the host's own UI) - or both invited each other.
                    // Only SC's community join hangs on it; vanilla's party join is left as it is
                    if (AcceptGenuine(invitedBy.entityId, invitedEntity.entityId, ActorNow())) __state |= InvitePending;
                }
                catch (Exception e) { ScLog.Once("mp7-party-accept-pre", e); }
            }

            [HarmonyPostfix]
            public static void Postfix(EntityPlayer invitedBy, EntityPlayer invitedEntity, int __state)
            {
                try
                {
                    if (!ScCommunity.IsHost || !On || invitedEntity == null) return;
                    Party party = invitedEntity.Party;
                    if ((__state & InvitePending) != 0 && invitedBy != null) Consumed(invitedBy.entityId, invitedEntity.entityId);   // round MP7 (final check)
                    if (party == null || !party.ContainsMember(invitedEntity)) return;     // a full party: nobody joined
                    Accepted(ScPlayers.IdOf(invitedEntity), ScPlayers.IdOf(invitedBy), (__state & InviterNew) != 0, IdsOf(party.MemberList), ScPlayers.IdOf(party.Leader),
                             (__state & InvitePending) != 0, party.PartyID);
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
                    // round MP7 (review AP/F): m4 - only with the game's AutoParty setting on (vanilla never checks it for the package);
                    // round MP7 (final check): M2 - and only for the joiner's own package, or the host's own spawn
                    bool own = packageSender != 0 ? packageSender == joiningEntity.entityId : joiningEntity is EntityPlayerLocal;
                    AutoJoined(ScPlayers.IdOf(joiningEntity), IdsOf(party.MemberList), ScPlayers.IdOf(party.Leader), own && GameStats.GetBool(EnumGameStats.AutoParty), party.PartyID);
                }
                catch (Exception e)
                {
                    ScLog.Once("mp7-party-autojoin", e);
                }
            }
        }

        // round MP7 (final check): M2 - void NetPackagePartyActions.ProcessPackage(World _world, GameManager _callbacks) -
        // NetPackagePartyActions.cs:60. On the server, for a client's package (Sender set): who sent it is kept for the party
        // handlers it calls (ActorNow), and a SendInvite counts in SC's ledger only when its sender is the inviter. Vanilla runs
        // as it always does.
        [HarmonyPatch(typeof(NetPackagePartyActions), nameof(NetPackagePartyActions.ProcessPackage), new[] { typeof(World), typeof(GameManager) })]
        public static class Patch_PartyPackage
        {
            [HarmonyPrefix]
            public static void Prefix(NetPackagePartyActions __instance, NetPackagePartyActions.PartyActions ___currentOperation, int ___invitedByEntityID, int ___invitedEntityID)
            {
                packageSender = 0;
                try
                {
                    ClientInfo ci = __instance != null ? __instance.Sender : null;
                    if (ci == null || !ScCommunity.IsHost) return;       // a client's machine, or the host's own (never a client's package)
                    packageSender = ci.entityId > 0 ? ci.entityId : -1;
                    if (On && ___currentOperation == NetPackagePartyActions.PartyActions.SendInvite) NoteInvite(packageSender, ___invitedByEntityID, ___invitedEntityID);
                }
                catch (Exception e) { ScLog.Once("mp7-party-package-pre", e); }
            }

            [HarmonyFinalizer]
            public static Exception Finalizer(Exception __exception)
            {
                packageSender = 0;                      // whatever happened inside: the host's own actions are the host's again
                return __exception;
            }
        }

        // round MP7 (final check): M1 - NetPackagePartyActions Setup(PartyActions _operation, int _invitedByEntityID, int
        // _invitedEntityID, int[] _partyMembers = null, string _voiceLobbyId = null) - NetPackagePartyActions.cs:33. The listen host's
        // own invites, from where it builds their package (XUiC_PlayersListEntry, XUiC_SpawnSelectionWindow and the auto party with
        // land claim allies, GameManager.HandleFirstSpawnInteractions, which never lists one on the host): a SendInvite whose inviter
        // is the host's own player, built outside a client's package (inside one it is the server relaying that client's invite,
        // judged by Patch_PartyPackage). Its IL (five field stores) is over Mono's inline limit, so every caller reaches the patch.
        [HarmonyPatch(typeof(NetPackagePartyActions), nameof(NetPackagePartyActions.Setup),
                      new[] { typeof(NetPackagePartyActions.PartyActions), typeof(int), typeof(int), typeof(int[]), typeof(string) })]
        public static class Patch_PartyPackageSetup
        {
            [HarmonyPostfix]
            public static void Postfix(NetPackagePartyActions.PartyActions _operation, int _invitedByEntityID, int _invitedEntityID)
            {
                try
                {
                    if (_operation != NetPackagePartyActions.PartyActions.SendInvite || packageSender != 0 || !ScCommunity.IsHost || !On) return;
                    EntityPlayerLocal lp = ScCommunity.LocalPlayer;
                    if (lp == null || lp.entityId != _invitedByEntityID) return;
                    NoteInvite(lp.entityId, _invitedByEntityID, _invitedEntityID);
                }
                catch (Exception e) { ScLog.Once("mp7-party-package-setup", e); }
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
