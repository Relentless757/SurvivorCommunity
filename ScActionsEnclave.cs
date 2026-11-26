using System;
using System.Collections.Generic;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round MP5 (part E): the server's half of a client's talk with one of an enclave's people - what their rows and their line
    /// need, filled into the talk's facts (ScTalkContext.Build, for the client whose talk opened, and again after each of its
    /// enclave actions: ScTalkContext.SendFactsAfter). Each verdict is the host's own requirement code answered for that player
    /// (ScEncDialog.PersonTalking, ScEnclaveDeals' Can*/Blocked, ScEncRequests.LeaderAt, ScEncGifts.BlockedServer,
    /// ScEncJoin.Shown/Blocked, ScEncSearch.TalkingAt), with the halves that read the player's own bag or journal left to the
    /// client (ScEncTalk). The line's tokens are ScEncDialog.Fill's, made with no side effect (the offer's infestation made
    /// known and the leader's thanks spent are the client's line's to report: enc.seen). Server, main thread.
    /// </summary>
    internal static class ScEncFacts
    {
        internal static void Fill(ScTalkFacts f, EntityPlayer player, EntityAlive npc)
        {
            try
            {
                if (f == null || npc == null || !ScCommunity.IsHost || !ScRegistry.Loaded) return;
                EncPerson body = ScEnclavePeople.PersonOf(npc);
                Enclave e = ScEnclavePeople.EnclaveOf(body);
                if (body == null || e == null) return;
                int flags = ScTalkFacts.EncBody;
                f.EncId = e.Id;
                f.EncKind = e.Kind ?? "";
                f.EncStanding = ScEnclaves.StandingKey(e);
                flags |= Tokens(f, e, body);

                EncPerson p = body.Alive ? body : null;          // ScEncDialog.PersonTalking: alive, at home or enlisted
                if (p != null)
                {
                    flags |= ScTalkFacts.EncAlive;
                    if (p.Enlisted) flags |= ScTalkFacts.EncEnlisted;
                    else if (p.Role == EncPerson.Leader) flags |= ScTalkFacts.EncLeader;
                    else if (p.Role == EncPerson.MemberRole) flags |= ScTalkFacts.EncMember;
                    if (ScEnclaveDeals.CanTrade(p, npc)) flags |= ScTalkFacts.EncCanTrade;
                    // E3: enlist - the dukes are the client's own bag's (it counts them); the fee to count them against
                    if (ScEnclaveDeals.CanAskEnlist(p)) flags |= ScTalkFacts.EncEnlistShow;
                    f.EncEnlistWhy = ScEnclaveDeals.EnlistBlocked(p, player, player is EntityPlayerLocal) ?? "";
                    f.EncEnlistCost = ScEnclaveDeals.EnlistCost(e);
                    // E3: recruit - and the favour row's roll, which the host's ScEncFavour makes once show, open and favour pass
                    bool ask = ScEnclaveDeals.CanAskRecruit(p);
                    if (ask) flags |= ScTalkFacts.EncRecruitShow;
                    string why = ScEnclaveDeals.RecruitBlocked(p, npc, player);
                    f.EncRecruitWhy = why ?? "";
                    bool needs = ScEnclaveDeals.NeedsFavour(p, npc, player);
                    if (needs) flags |= ScTalkFacts.EncNeedsFavour;
                    if (ask && why == null && needs) ScEnclaveDeals.EnsureFavour(p, npc);
                    // E4, E7, E12: the leader at home of an enclave that is not hostile
                    Enclave le;
                    EnclaveRequest r;
                    if (ScEncRequests.LeaderAt(npc, out le, out r))
                    {
                        flags |= ScTalkFacts.EncLeaderTalking;
                        f.EncReqState = ScEncRequests.StateWord(r);
                        if (r != null)
                        {
                            f.EncReqId = r.Id;
                            f.EncReqKind = r.Kind ?? "";
                            f.EncReqStage = ScEncRequests.StageWord(r);
                            f.EncReqRes = (int)r.Res;
                            f.EncReqUnits = r.Units;
                            if (ScEncRequests.CanGiveUp(r)) flags |= ScTalkFacts.EncCanGiveUp;
                            if (r.Kind == EnclaveRequest.KindMissing) f.EncFoundRow = string.Format(Localization.Get("scEncReqFoundRow"), ScEncSearch.PersonName(r));
                        }
                        if (ScEncGifts.Shown(le)) flags |= ScTalkFacts.EncGiftShow;
                        f.EncGiftWhy = ScEncGifts.BlockedServer(le) ?? "";
                        if (ScEncJoin.Shown(le)) flags |= ScTalkFacts.EncJoinShow;
                        f.EncJoinWhy = ScEncJoin.Blocked(le, player) ?? "";
                    }
                }
                // E5: a missing survivor's own body (their phases)
                EncPerson mp;
                EnclaveRequest mr;
                if (ScEncSearch.TalkingAt(npc, out mp, out mr))
                    f.EncSearch = mr.Stage == ScEncSearch.StageAlive ? 1 : mr.Stage == ScEncSearch.StageFollowing ? 2 : 0;
                f.EncFlags = flags;
            }
            catch (Exception ex)
            {
                ScLog.Once("encfacts-fill", ex);
            }
        }

        /// <summary>
        /// The line's tokens in ScEncDialog.Fill's order (the request lines first: their keys are what a voice says), and where
        /// their leader is. Returns the flags the line's own effects need (EncThanks, EncInfestNew).
        /// </summary>
        private static int Tokens(ScTalkFacts f, Enclave e, EncPerson p)
        {
            int flags = 0;
            var list = new List<ScEncToken>();
            string key, info;
            EnclaveRequest known;
            ScEncRequests.AskParts(e, out key, out info, out known);
            Add(list, "{reqask}", key, info);
            Infestation inf = known != null ? ScInfestations.ById(known.InfestationId) : null;
            if (inf != null && !inf.Known) flags |= ScTalkFacts.EncInfestNew;
            ScEncRequests.WaitParts(e, out key, out info);
            Add(list, "{reqwait}", key, info);
            Add(list, "{reqnone}", ScEncRequests.NoneKey(e), null);
            if (!string.IsNullOrEmpty(e.Thanks)) flags |= ScTalkFacts.EncThanks;
            Add(list, "{terms}", null, ScEnclaveDeals.TermsText(e));
            Add(list, "{until}", null, ScEnclaveDeals.UntilText(p));
            Add(list, "{recruitcost}", null, ScEnclaveDeals.RecruitCostText(e));
            Add(list, "{benefit}", null, ScEncAllies.AboutText(e));
            Add(list, "{joinfacts}", null, ScEncJoin.FactsText(e));
            EncPerson leader = ScEnclavePeople.LeaderOf(e);
            Add(list, "{enc}", null, e.Name);
            Add(list, "{leader}", null, leader != null ? leader.Name : "");
            Add(list, "{count}", null, ScEnclavePeople.Living(e).Count.ToString());
            Add(list, "{poi}", null, e.PoiName);
            Add(list, "{standing}", ScEnclaves.StandingWordKey(e), null);
            Add(list, "{kind}", KindKey(e.Kind), null);
            Add(list, "{name}", null, p.Name);
            f.EncTokens = list;
            // {where}: the leader's body, else their spot (ScEncDialog.Where) - the client measures from its own player
            World w = ScCommunity.World;
            EntityAlive lb = leader != null && leader.EntityId >= 0 && w != null ? ScNpc.As(ScNpc.Loaded(w, leader.EntityId)) : null;
            if (lb != null) { f.EncHasWhere = true; f.EncWhere = lb.position; }
            else if (leader != null && leader.HasHome) { f.EncHasWhere = true; f.EncWhere = leader.HomeCentre; }
            return flags;
        }

        private static void Add(List<ScEncToken> list, string token, string key, string text)
        {
            list.Add(new ScEncToken { Token = token, Key = key ?? "", Text = text ?? "" });
        }

        /// <summary>ScEnclavePeople.KindName's localization key, so the client says the kind in its own language.</summary>
        private static string KindKey(string kind)
        {
            switch (kind)
            {
                case "growers": return "scEncKindGrowers";
                case "medics": return "scEncKindMedics";
                case "militia": return "scEncKindMilitia";
                case "builders": return "scEncKindBuilders";
                case "roadcrew": return "scEncKindRoadcrew";
                default: return "scEncKindGeneral";
            }
        }
    }

    /// <summary>
    /// Round MP5 (part E): a client's half of a talk with one of an enclave's people. The enclave operators' client paths
    /// (DialogOpsEnclaves.cs, DialogOpsEncRequests.cs) read the talk's facts (ScTalkContext; ScEncFacts on the server) and this
    /// machine's own bag and journal - never the registry's copy for a person (it has none) - and change nothing. Each action is
    /// an ScActionsEnclave op; one that hands items over takes them from this machine's own bag first and sends them (ScItemTx),
    /// and gets them back on any refusal; one that gives items (a threat's bundles) gets them in the answer's hint. The reply line
    /// ({reqreply}, {giftreply}) shows "..." until the answer comes, then the answer (ScTalkContext.RefillLine). Client, main thread.
    /// </summary>
    public static class ScEncTalk
    {
        /// <summary>An answer's hints: the reply line's key and specifics (escaped), and stacks for the asking player's bag.</summary>
        public const string HintKey = "rk", HintInfo = "ri", HintGive = "give";
        /// <summary>Ask's line: none, the request reply ({reqreply}), the gift reply ({giftreply}).</summary>
        public const int LineNone = 0, LineRequest = 1, LineGift = 2;

        /// <summary>The reply the next statement shows, for one NPC: pending until the answer comes.</summary>
        private sealed class Reply
        {
            public int Npc;
            public bool Gift, Pending;
            public string Key = "", Info = "";
        }

        private static Reply reply;
        /// <summary>Enclave bodies whose line was filled before their facts came (filled again when they land).</summary>
        private static readonly HashSet<int> unfilled = new HashSet<int>();
        private static int asked, answered, refunds, effects, refills;

        private static string Loc(string key) { return Localization.Get(key); }

        /// <summary>Every machine, at world start and shutdown (ScTalkContext.Reset).</summary>
        public static void Reset()
        {
            reply = null;
            unfilled.Clear();
            asked = answered = refunds = effects = refills = 0;
        }

        public static string Report()
        {
            return "enclave talk: " + asked + " action(s) asked, " + answered + " answered, " + refunds + " hand-over(s) given back, " + effects + " line effect(s) reported, "
                   + refills + " line(s) filled again when the facts came";
        }

        /// <summary>One of an enclave's people had their line filled with no facts yet (ScEncDialog.Fill).</summary>
        internal static void NoteUnfilled(int npcId) { unfilled.Add(npcId); }

        /// <summary>Their facts came: true once when their line waits to be filled again (ScTalkContext.OnFacts).</summary>
        internal static bool TakeUnfilled(int npcId)
        {
            if (!unfilled.Remove(npcId)) return false;
            refills++;
            return true;
        }

        // ------------------------------------------------------------------ the facts ----

        /// <summary>The facts for the NPC a requirement or action asks about (the respondent, else the CurrentNPC cvar), or null.</summary>
        internal static ScTalkFacts Of(EntityPlayer player, EntityNPC talkingTo) { return ScTalkContext.FactsFor(player, talkingTo); }

        internal static ScTalkFacts Of(EntityAlive npc) { return npc != null ? ScTalkContext.Facts(npc.entityId) : null; }

        internal static bool Has(ScTalkFacts f, int bit) { return f != null && f.Enc(bit); }

        /// <summary>ScEncRole: "" any of them, "enlisted", or "leader" / "member" (not enlisted).</summary>
        internal static bool Role(ScTalkFacts f, string v)
        {
            if (!Has(f, ScTalkFacts.EncAlive)) return false;
            switch (v)
            {
                case "": return true;
                case "enlisted": return Has(f, ScTalkFacts.EncEnlisted);
                case EncPerson.Leader: return Has(f, ScTalkFacts.EncLeader);
                case EncPerson.MemberRole: return Has(f, ScTalkFacts.EncMember);
                default: return false;
            }
        }

        /// <summary>ScEnclaveDeals.EnlistBlocked: the server's part, then the fee against this machine's own dukes. Null: they can come.</summary>
        internal static string EnlistWhy(ScTalkFacts f, EntityPlayer player)
        {
            if (f == null) return Loc("scEncStays");
            if (f.EncEnlistWhy.Length > 0) return f.EncEnlistWhy;
            int cost = f.EncEnlistCost;
            if (cost > 0 && ScEnclaveDeals.DukesCarried(player as EntityPlayerLocal) < cost) return string.Format(Loc("scEncNeedDukes"), cost);
            return null;
        }

        /// <summary>ScEnclaveDeals.NeedsFavour: the server's (Neutral, the record not done), unless this machine's journal says the favour quest is past its clearing.</summary>
        internal static bool NeedsFavour(ScTalkFacts f, EntityPlayer player)
        {
            if (!Has(f, ScTalkFacts.EncNeedsFavour)) return false;
            Prospect v = ScTalkContext.View(f.Npc);
            return !(v != null && (v.Done || ScQuests.FavourQuestDone(player, v.Id)));
        }

        /// <summary>ScEncGifts.Blocked: the record's part, then a bundle in this machine's own bag. Null: a gift can be given.</summary>
        internal static string GiftWhy(ScTalkFacts f, EntityPlayer player)
        {
            if (f == null) return Loc("scEncGiftWhyOff");
            if (f.EncGiftWhy.Length > 0) return f.EncGiftWhy;
            if (ScEncGifts.RowsFor(player as EntityPlayerLocal, f.EncKind).Count == 0) return Loc("scEncGiftWhyNone");
            return null;
        }

        /// <summary>ScEncGifts.WhyText: "I brought you something. (Come back tomorrow.)", "" when it is open.</summary>
        internal static string GiftWhyText(ScTalkFacts f, EntityPlayer player)
        {
            string why = GiftWhy(f, player);
            return why == null ? "" : string.Format(Loc("scEncGiftWhy"), Loc("scEncGift"), why);
        }

        /// <summary>ScEncJoin.WhyText from the server's reason, "" when it is open.</summary>
        internal static string JoinWhyText(ScTalkFacts f)
        {
            return f == null || f.EncJoinWhy.Length == 0 ? "" : string.Format(Loc("scEncJoinWhy"), Loc("scEncJoinAsk"), f.EncJoinWhy);
        }

        /// <summary>ScEncRequests.FillRow's found row ("I found Mara. I'm sorry.") for the leader being talked to.</summary>
        internal static string FoundRow()
        {
            EntityPlayerLocal lp = ScCommunity.LocalPlayer;
            ScTalkFacts f = lp != null ? Of(lp, null) : null;
            return Has(f, ScTalkFacts.EncLeaderTalking) && f.EncReqKind == EnclaveRequest.KindMissing ? f.EncFoundRow : "";
        }

        // ------------------------------------------------------------------ the line ----

        /// <summary>
        /// ScEncDialog.Fill for a client: the facts' tokens in the host's order (the request lines and the reply first), {where}
        /// from this machine's own player. voiceKey: the key of the request or reply line put in (its clip's name), else null.
        /// Null when the facts know no enclave body (the caller tries a band's line).
        /// </summary>
        internal static string Fill(string text, EntityAlive npc, EntityPlayer player, out string voiceKey)
        {
            voiceKey = null;
            ScTalkFacts f = Of(npc);
            if (!Has(f, ScTalkFacts.EncBody) || string.IsNullOrEmpty(text)) return null;
            string s = text;
            ScEncToken t;
            if (s.IndexOf("{req", StringComparison.Ordinal) >= 0)
            {
                if (s.IndexOf("{reqask}", StringComparison.Ordinal) >= 0 && (t = Token(f, "{reqask}")) != null)
                {
                    s = s.Replace("{reqask}", t.Render());
                    voiceKey = t.Key;
                    if (f.Enc(ScTalkFacts.EncInfestNew)) Effect(f, ScActionsEnclave.SeenInfestation);      // AskText's MakeKnown, on the server
                }
                if (s.IndexOf("{reqwait}", StringComparison.Ordinal) >= 0 && (t = Token(f, "{reqwait}")) != null)
                {
                    s = s.Replace("{reqwait}", t.Render());
                    voiceKey = t.Key;
                }
                if (s.IndexOf("{reqreply}", StringComparison.Ordinal) >= 0)
                {
                    string k;
                    s = s.Replace("{reqreply}", ReplyLine(f.Npc, false, out k));
                    voiceKey = k;
                }
                if (s.IndexOf("{reqnone}", StringComparison.Ordinal) >= 0 && (t = Token(f, "{reqnone}")) != null)
                {
                    s = s.Replace("{reqnone}", t.Render());
                    voiceKey = t.Key;
                    if (f.Enc(ScTalkFacts.EncThanks)) Effect(f, ScActionsEnclave.SeenThanks);                // NoneText's spent thanks, on the server
                }
            }
            if (s.IndexOf("{giftreply}", StringComparison.Ordinal) >= 0)
            {
                string k;
                s = s.Replace("{giftreply}", ReplyLine(f.Npc, true, out k));
                voiceKey = k;
            }
            List<ScEncToken> list = f.EncTokens;
            if (list != null)
            {
                foreach (ScEncToken x in list)
                {
                    if (x == null || x.Token.Length == 0 || x.Token.StartsWith("{req", StringComparison.Ordinal)) continue;
                    if (s.IndexOf(x.Token, StringComparison.Ordinal) >= 0) s = s.Replace(x.Token, x.Render());
                }
            }
            if (s.IndexOf("{where}", StringComparison.Ordinal) >= 0) s = s.Replace("{where}", f.EncHasWhere && player != null ? ScEnclaves.Where(player.position, f.EncWhere) : "");
            return s;
        }

        private static ScEncToken Token(ScTalkFacts f, string token)
        {
            if (f.EncTokens == null) return null;
            foreach (ScEncToken t in f.EncTokens) if (t != null && t.Token == token) return t;
            return null;
        }

        /// <summary>The reply line for this NPC: "..." while the answer is on its way, the answer, else the host's "Never mind that now. It's settled.".</summary>
        private static string ReplyLine(int npcId, bool gift, out string key)
        {
            Reply r = reply;
            if (r != null && r.Npc == npcId && r.Gift == gift)
            {
                if (r.Pending) { key = null; return Loc("scEncReplyWait"); }
                key = r.Key;
                return (Loc(r.Key) + " " + r.Info).Trim();
            }
            key = "scEncReqGone";
            return Loc("scEncReqGone");
        }

        /// <summary>
        /// A line effect the host's fill has as it shows (the offer's infestation made known, the leader's thanks spent): reported to
        /// the server once (enc.seen), and this machine's facts follow at once - a thanks is said once, as on the host.
        /// </summary>
        private static void Effect(ScTalkFacts f, int what)
        {
            if (what == ScActionsEnclave.SeenInfestation) f.EncFlags &= ~ScTalkFacts.EncInfestNew;
            else
            {
                f.EncFlags &= ~ScTalkFacts.EncThanks;
                var list = new List<ScEncToken>();
                foreach (ScEncToken t in f.EncTokens) list.Add(t != null && t.Token == "{reqnone}" ? new ScEncToken { Token = t.Token, Key = "scEncReqNothingLine" } : t);
                f.EncTokens = list;
            }
            effects++;
            ScActions.Request("enc.seen", (Action<ScActionReply>)null, f.Npc, what);
        }

        // ------------------------------------------------------------------ the actions ----

        /// <summary>
        /// One enclave action from this machine: op with npc first, then more; strs (stacks this machine took, ScItemTx.Encode);
        /// line: the reply statement it leads to (shown "..." until the answer); refund: what this machine took for it, given back
        /// on any refusal (the server kept nothing). after: the answer, once handled.
        /// </summary>
        internal static void Ask(EntityPlayer player, string op, int npcId, int[] more, string[] strs, int line, List<ItemStack> refund, Action<ScActionReply> after = null)
        {
            EntityPlayerLocal lp = player as EntityPlayerLocal;
            var ints = new List<int> { npcId };
            if (more != null) ints.AddRange(more);
            if (line != LineNone) reply = new Reply { Npc = npcId, Gift = line == LineGift, Pending = true };
            asked++;
            ScActions.Request(op, ints.ToArray(), strs, r => Answer(lp, npcId, line, refund, r, after));
        }

        private static void Answer(EntityPlayerLocal lp, int npcId, int line, List<ItemStack> refund, ScActionReply r, Action<ScActionReply> after)
        {
            answered++;
            try
            {
                if (!r.Ok && refund != null && refund.Count > 0 && lp != null)
                {
                    int dropped = ScItemTx.GiveToLocal(lp, refund);
                    refunds++;
                    ScLog.Out("enclave talk: the server refused the hand-over - " + refund.Count + " stack(s) given back" + (dropped > 0 ? " (" + dropped + " item(s) at your feet: a full bag)" : ""));
                }
                string give = r.HintStr(HintGive, null);
                if (!string.IsNullOrEmpty(give) && lp != null) ScItemTx.GiveToLocal(lp, ScItemTx.HintToStacks(give));
                ScTalkContext.ApplyQuestHint(lp, r);
                bool lined = line != LineNone && r.HasHint(HintKey);
                if (line != LineNone)
                {
                    reply = new Reply { Npc = npcId, Gift = line == LineGift, Key = lined ? r.HintStr(HintKey) : "scEncReqGone", Info = lined ? Unescape(r.HintStr(HintInfo)) : "" };
                    ScTalkContext.RefillLine(npcId, line == LineGift ? "{giftreply}" : "{reqreply}");
                }
                if (!lined) ScTalkContext.Tell(lp, r, false);          // a refusal no reply line carries: its tooltip
                if (after != null) after(r);
            }
            catch (Exception e)
            {
                ScLog.Once("enctalk-answer", e);
            }
        }

        /// <summary>The reply line set here, with no request (this machine could not take what the row hands over: the host's own refusal line).</summary>
        private static void LocalReply(int npcId, bool gift, string key)
        {
            reply = new Reply { Npc = npcId, Gift = gift, Key = key ?? "scEncReqGone" };
        }

        /// <summary>"I'm not asking.": the leader's bundles come back in the answer (enc.threat).</summary>
        internal static void Threat(EntityPlayer player, EntityAlive npc)
        {
            if (npc != null) Ask(player, "enc.threat", npc.entityId, null, null, LineNone, null);
        }

        /// <summary>"Deal.": the fee out of this machine's own bag first (checked again: it may have left since the offer), then enc.enlist.</summary>
        internal static void Enlist(EntityPlayer player, EntityAlive npc)
        {
            EntityPlayerLocal lp = player as EntityPlayerLocal;
            ScTalkFacts f = Of(npc);
            if (lp == null || f == null) return;
            string why = EnlistWhy(f, lp);
            if (why != null) { ScCommunity.Tell(player, why, "ui_denied"); return; }
            int cost = Math.Max(0, f.EncEnlistCost);
            var taken = new List<ItemStack>();
            if (cost > 0)
            {
                ItemValue cur = ScEnclaveDeals.CurrencyValue();
                int type = cur != null ? cur.type : -1;
                int got = type >= 0 ? ScItemTx.TakeFromLocal(lp, iv => iv != null && iv.type == type, cost, taken) : 0;
                if (got < cost)
                {
                    if (taken.Count > 0) ScItemTx.GiveToLocal(lp, taken);
                    ScCommunity.Tell(player, string.Format(Loc("scEncNeedDukes"), cost), "ui_denied");
                    return;
                }
            }
            string[] strs = ScItemTx.EncodeAll(taken);
            if (strs == null) { ScItemTx.GiveToLocal(lp, taken); ScCommunity.TellKey(player, "scInternal", "ui_denied"); return; }
            Ask(player, "enc.enlist", npc.entityId, new[] { cost }, strs, LineNone, taken);
        }

        internal static void GoHome(EntityPlayer player, EntityAlive npc)
        {
            if (npc != null) Ask(player, "enc.gohome", npc.entityId, null, null, LineNone, null);
        }

        /// <summary>
        /// "Welcome to the community." for one of them, with this machine's journal verdict on their POI favour (member.join's
        /// rule); after the same row's supplies hand-over when one is in flight (scEncGiveSupplies).
        /// </summary>
        internal static void Recruit(EntityPlayer player, EntityAlive npc)
        {
            if (npc == null) return;
            int id = npc.entityId;
            Action send = () =>
            {
                Prospect v = ScTalkContext.View(id);
                bool journal = v != null && ScQuests.FavourQuestDone(player, v.Id);
                Ask(player, "enc.recruit", id, new[] { journal ? 1 : 0 }, null, LineNone, null);
            };
            if (ScTalkContext.HandOverPending(id)) { ScTalkContext.EncAfterHandOver(id, send); return; }
            send();
        }

        internal static void Join(EntityPlayer player, EntityAlive npc)
        {
            if (npc != null) Ask(player, "enc.join", npc.entityId, null, null, LineNone, null);
        }

        /// <summary>"I brought you something.": the pick of this machine's own bundles.</summary>
        internal static void GiftList(EntityPlayer player, EntityAlive npc)
        {
            ScTalkFacts f = Of(npc);
            if (Has(f, ScTalkFacts.EncLeaderTalking)) ScEncGifts.BuildPickClient(player as EntityPlayerLocal, npc.entityId, f.EncId, f.EncKind);
            else ScEncGifts.ClearPick();
        }

        /// <summary>A gift row: that bundle out of this machine's own bag first, then enc.gift; the leader's answer is the reply line.</summary>
        internal static void Gift(EntityPlayer player, EntityAlive npc, int row)
        {
            EntityPlayerLocal lp = player as EntityPlayerLocal;
            ScTalkFacts f = Of(npc);
            if (lp == null || f == null) { reply = null; return; }       // the host's Acting: no reply left (the line says "Never mind ...")
            if (!Has(f, ScTalkFacts.EncLeaderTalking)) { LocalReply(npc.entityId, true, "scEncReqGone"); return; }
            ScEncGifts.GiftRow g = ScEncGifts.PickRowClient(row, lp, npc.entityId, f.EncId, f.EncKind);
            var taken = new List<ItemStack>();
            if (g == null || !ScEncGifts.TakeRow(lp, g, taken)) { LocalReply(npc.entityId, true, "scEncReqNotCarried"); return; }
            ScEncGifts.ClearPick();
            string[] strs = ScItemTx.EncodeAll(taken);
            if (strs == null) { ScItemTx.GiveToLocal(lp, taken); LocalReply(npc.entityId, true, "scEncReqNotCarried"); return; }
            Ask(player, "enc.gift", npc.entityId, null, strs, LineGift, taken);
        }

        /// <summary>The request rows that need no item: accept, no, think, I can't do it, I found them, I took care of it (ScActionsEnclave.Req*).</summary>
        internal static void Req(EntityPlayer player, int what)
        {
            EntityAlive npc = ScFavours.CurrentNpc(player);
            if (npc == null) { reply = null; return; }
            Ask(player, "enc.req", npc.entityId, new[] { what }, null, what == ScActionsEnclave.ReqThink ? LineNone : LineRequest, null);
        }

        /// <summary>"I have it right here.": what the request asks out of this machine's own bag first, then enc.reqgive.</summary>
        internal static void ReqGive(EntityPlayer player)
        {
            EntityPlayerLocal lp = player as EntityPlayerLocal;
            EntityAlive npc = ScFavours.CurrentNpc(player);
            ScTalkFacts f = Of(npc);
            if (lp == null || f == null) { reply = null; return; }
            if (!Has(f, ScTalkFacts.EncLeaderTalking) || (f.EncReqKind != EnclaveRequest.KindResource && f.EncReqKind != EnclaveRequest.KindGenerator))
            {
                LocalReply(npc.entityId, false, "scEncReqGone");
                return;
            }
            var taken = new List<ItemStack>();
            if (!ScEncRequests.TakeForRequest(lp, f.EncReqKind, f.EncReqRes, f.EncReqUnits, taken)) { LocalReply(npc.entityId, false, "scEncReqNotCarried"); return; }
            string[] strs = ScItemTx.EncodeAll(taken);
            if (strs == null) { ScItemTx.GiveToLocal(lp, taken); LocalReply(npc.entityId, false, "scEncReqNotCarried"); return; }
            Ask(player, "enc.reqgive", npc.entityId, null, strs, LineRequest, taken);
        }

        /// <summary>"Take one of these.": the pick of this machine's own guns (none for a request that is no gun request).</summary>
        internal static void ReqGunList(EntityPlayer player)
        {
            EntityPlayerLocal lp = player as EntityPlayerLocal;
            EntityAlive npc = ScFavours.CurrentNpc(player);
            ScTalkFacts f = Of(npc);
            if (lp != null && Has(f, ScTalkFacts.EncLeaderTalking) && f.EncReqKind == EnclaveRequest.KindGun) ScEncRequests.BuildPickClient(lp, npc.entityId, f.EncReqId);
            else ScEncRequests.BuildPickClient(lp, 0, 0);
        }

        /// <summary>A gun pick row: that gun out of its exact slot first, then enc.reqgun.</summary>
        internal static void ReqGiveGun(EntityPlayer player, int row)
        {
            EntityPlayerLocal lp = player as EntityPlayerLocal;
            EntityAlive npc = ScFavours.CurrentNpc(player);
            ScTalkFacts f = Of(npc);
            if (lp == null || f == null) { reply = null; return; }
            if (!Has(f, ScTalkFacts.EncLeaderTalking) || f.EncReqKind != EnclaveRequest.KindGun) { LocalReply(npc.entityId, false, "scEncReqGone"); return; }
            ScEncRequests.Gun g = ScEncRequests.PickRowClient(row, lp, npc.entityId, f.EncReqId);
            ItemStack taken;
            if (g == null || !ScEncRequests.TakeGun(lp, g, out taken)) { LocalReply(npc.entityId, false, "scEncReqNotCarried"); return; }
            ScEncRequests.BuildPickClient(lp, 0, 0);          // spent, as the host's ActGiveGun clears it
            var back = new List<ItemStack> { taken };
            string enc = ScItemTx.Encode(taken);
            if (enc == null) { ScItemTx.GiveToLocal(lp, back); LocalReply(npc.entityId, false, "scEncReqNotCarried"); return; }
            Ask(player, "enc.reqgun", npc.entityId, null, new[] { enc }, LineRequest, back);
        }

        /// <summary>A missing survivor's "Let's get you home." / "Wait here for me." (enc.follow / enc.wait).</summary>
        internal static void Search(EntityPlayer player, bool follow)
        {
            EntityAlive npc = ScFavours.CurrentNpc(player);
            if (npc != null) Ask(player, follow ? "enc.follow" : "enc.wait", npc.entityId, null, null, LineNone, null);
        }

        // ------------------------------------------------------------------ hint text ----

        /// <summary>A reply's specifics for a hint (no ';' or '=' may stand in one).</summary>
        internal static string Escape(string s)
        {
            try { return Uri.EscapeDataString(s ?? ""); }
            catch (Exception) { return ""; }
        }

        internal static string Unescape(string s)
        {
            try { return string.IsNullOrEmpty(s) ? "" : Uri.UnescapeDataString(s); }
            catch (Exception) { return ""; }
        }
    }

    /// <summary>
    /// Round MP5 (part E): the server's handlers for a client's enclave talk (ScActions' [ScAction] ops). The host's own talk
    /// keeps its inline actions (DialogOpsEnclaves.cs, DialogOpsEncRequests.cs: exactly as before); a client's actions come here
    /// and run the same cores with the sender. Every handler finds the NPC by the id the request names - loaded, alive and, for a
    /// remote sender, within ScTalkContext.TalkRange - checks the person's record as it is NOW with the host's own tests, acts,
    /// sends the sender the NPC's facts again (ScTalkContext.SendFactsAfter, ahead of this answer) and answers. Items: a hand-over
    /// left the client's bag before the request (the stacks in its strings); a refusal keeps nothing and the client gives them
    /// back; a give (the threat's bundles) is the answer's give hint. Every op is Community access: the community's standing,
    /// known flags and requests are shared (Stage 1); MP6: per community.
    ///
    /// Ops (ints unless marked s:; npc = the NPC's entity id):
    ///   enc.threat   npc                      "I'm not asking." - their bundles (hint give), the enclave hostile after its grace.
    ///   enc.enlist   npc, paid; s: the dukes  "Deal." - the fee the client took (the fee now, or refused: scEncEnlistFeeChanged).
    ///   enc.gohome   npc                      "Go home now." to one enlisted.
    ///   enc.recruit  npc, journal             "Welcome to the community." for one of them (journal 1: the client's POI favour
    ///                                         quest is past its clearing). Hint fclose/fdone: their favour quest closes there.
    ///   enc.join     npc                      E12: the whole group joins.
    ///   enc.gift     npc; s: the bundle       E7: a gift. Hints rk/ri: the reply line ({giftreply}).
    ///   enc.req      npc, what                E4/E5: what 1 accept, 2 decline, 3 think, 4 can't do, 5 found, 6 cleared. Hints rk/ri.
    ///   enc.reqgive  npc; s: the stacks       E4: the bundles or the generator handed over. Hints rk/ri.
    ///   enc.reqgun   npc; s: the gun          E4: a gun from the pick. Hints rk/ri.
    ///   enc.follow   npc                      E5: a missing survivor follows the sender home.
    ///   enc.wait     npc                      E5: they wait where they stand.
    ///   enc.search   x, y, z                  E5: "Search the body" on their remains (within SearchRange of a remote sender).
    ///   enc.seen     npc, what                the client's line showed: what 1 the offer's infestation (known now), 2 the
    ///                                         leader's thanks (spent). Quiet.
    /// </summary>
    public static class ScActionsEnclave
    {
        public const int ReqAccept = 1, ReqDecline = 2, ReqThink = 3, ReqCantDo = 4, ReqFound = 5, ReqCleared = 6;
        public const int SeenInfestation = 1, SeenThanks = 2;
        /// <summary>A remote sender's "Search the body" is within this many meters of the remains (the block's reach, and some slack).</summary>
        private const float SearchRange = 8f;
        /// <summary>The share of a threat's bundles that are the resource their kind lives on (ScEnclavePeople.Threaten's ThreatKindShare).</summary>
        private const float ThreatKindShare = 0.65f;

        /// <summary>The NPC the request names: loaded and alive, and for a remote sender within talking reach. Null otherwise.</summary>
        private static EntityAlive Npc(EntityPlayer sender, ScActionArgs args)
        {
            World w = ScCommunity.World;
            int id = args.Int(0, -1);
            EntityAlive npc = w != null && id > 0 ? ScNpc.As(ScNpc.Loaded(w, id)) : null;
            if (npc == null || npc.IsDead() || sender == null) return null;
            if (sender is EntityPlayerLocal) return npc;
            return (npc.position - sender.position).sqrMagnitude <= ScTalkContext.TalkRange * ScTalkContext.TalkRange ? npc : null;
        }

        /// <summary>A reply line's answer: done or refused, no tooltip (the line says it), the key and the specifics as hints.</summary>
        private static void Line(ScActionReply reply, bool ok, string key, string info)
        {
            if (ok) reply.Done();
            else reply.Fail("", "");
            reply.SetHint(ScEncTalk.HintKey, string.IsNullOrEmpty(key) ? "scEncReqGone" : key);
            if (!string.IsNullOrEmpty(info)) reply.SetHint(ScEncTalk.HintInfo, ScEncTalk.Escape(info));
        }

        /// <summary>
        /// A hand-over's core threw: when the record already took the items (took: the request closed, the gift day moved) they are
        /// kept - the answer is done, for a refusal would give them back as well (a copy); else the exception goes on (ScActions
        /// answers scActionFailed, and the client gives the items back).
        /// </summary>
        private static bool Kept(Exception ex, string tag, bool took)
        {
            if (!took) throw new InvalidOperationException(tag, ex);
            ScLog.Once(tag, ex);
            return true;
        }

        /// <summary>The leader's open request before a hand-over (null: none, or no leader).</summary>
        private static EnclaveRequest OpenRequest(EntityAlive npc)
        {
            Enclave e;
            EnclaveRequest r;
            return npc != null && ScEncRequests.LeaderAt(npc, out e, out r) ? r : null;
        }

        /// <summary>The leader's enclave's next gift day now (int.MinValue: no leader).</summary>
        private static int LeaderGiftDay(EntityAlive npc)
        {
            Enclave e;
            EnclaveRequest r;
            return npc != null && ScEncRequests.LeaderAt(npc, out e, out r) ? e.GiftDay : int.MinValue;
        }

        // ------------------------------------------------------------------ E2, E3 ----

        [ScAction("enc.threat", ScActionAccess.Community)]
        private static void Threat(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            EntityAlive npc = Npc(sender, args);
            EncPerson p = ScEnclavePeople.PersonOf(npc);
            Enclave e = ScEnclavePeople.EnclaveOf(p);
            if (npc == null || e == null || !p.IsLeader || e.IsHostile) { reply.Fail(""); return; }
            EntityPlayerLocal local = sender as EntityPlayerLocal;
            if (local != null)
            {
                ScEnclavePeople.Threaten(local, npc);        // the host's own (its own bag)
                reply.Done();
                return;
            }
            var give = new List<ItemStack>();
            // an exception after the bundles were made (the ledger, the hostility) still hands them over: never lost
            try { ThreatenRemote(sender, npc, p, e, give); }
            catch (Exception ex) { ScLog.Once("enc-threat-remote", ex); }
            string hint = ScItemTx.StacksToHint(give);
            if (hint != null) reply.SetHint(ScEncTalk.HintGive, hint);
            else
            {
                // too large for a hint (never for bundles): at the sender's feet, never lost
                foreach (ItemStack s in give) GameManager.Instance.ItemDropServer(s, sender.GetDropPosition(), Vector3.zero, -1, 60f);
            }
            reply.Done();
        }

        /// <summary>
        /// ScEnclavePeople.Threaten for a remote player - MP5 merge: Threaten takes EntityPlayerLocal (part W's file), so this twin
        /// gives the bundles into give (the answer's hint: the sender's machine puts them in its bag) instead of the local bag. The
        /// same roll, ledger line, log and hostility after EnclaveThreatGrace.
        /// </summary>
        private static void ThreatenRemote(EntityPlayer player, EntityAlive npc, EncPerson p, Enclave e, List<ItemStack> give)
        {
            World w = ScCommunity.World;
            GameRandom r = w != null ? w.GetGameRandom() : null;
            int lo = Math.Max(0, ScConfig.EnclaveThreatBundlesMin), hi = Math.Max(lo, ScConfig.EnclaveThreatBundlesMax);
            int n = r != null ? r.RandomRange(lo, hi + 1) : lo;
            Resource main = ScEnclavePeople.KindResource(e.Kind, r);
            var counts = new int[ScNames.ClassicCount];
            for (int i = 0; i < n; i++)
            {
                Resource res = r == null || r.RandomFloat < ThreatKindShare ? main : (Resource)r.RandomRange(0, ScNames.ClassicCount);
                counts[(int)res]++;
            }
            var parts = new List<string>();
            int given = 0;
            for (int i = 0; i < counts.Length; i++)
            {
                if (counts[i] <= 0) continue;
                int g = ScStockpile.GiveBundles(null, (Resource)i, BundleSize.Medium, counts[i], player, give);
                given += g;
                if (g > 0) parts.Add(g + " " + Localization.Get(ScNames.LocKey((Resource)i)));
            }
            string what = parts.Count > 0 ? string.Join(", ", parts.ToArray()) : "0";
            ScOutposts.NoteLedger(string.Format(Localization.Get("scEncLedgerThreat"), e.Name, what));
            ScLog.Out("enclave " + e.Id + " '" + e.Name + "': " + player.EntityName + " threatened " + p.Name + " and took " + given + " medium bundle(s) (" + what + ")");
            ScEnclaves.TurnHostile(e, "threatened by " + player.EntityName, ScConfig.EnclaveThreatGrace);
        }

        [ScAction("enc.enlist", ScActionAccess.Community)]
        private static void Enlist(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            EntityAlive npc = Npc(sender, args);
            if (npc == null) { reply.Fail("scEncStays"); return; }       // the dukes go back
            string reason;
            bool ok;
            if (sender is EntityPlayerLocal) ok = ScEnclaveDeals.Enlist(sender, npc, out reason);     // the host's own pays from its bag
            else
            {
                // the dukes the client took: the currency only, and as many as it says (co-op trust in the stacks, as every MP3 take)
                int paid = Math.Max(0, args.Int(1, 0)), n = 0;
                ItemValue cur = ScEnclaveDeals.CurrencyValue();
                foreach (ItemStack s in ScItemTx.DecodeAll(args.Strs))
                {
                    if (cur == null || s.itemValue.type != cur.type) { reply.Fail("scInternal"); return; }
                    n += s.count;
                }
                if (n != paid) { reply.Fail("scInternal"); return; }
                EncPerson who = ScEnclavePeople.PersonOf(npc);
                bool was = who != null && who.Enlisted;
                try { ok = ScEnclaveDeals.EnlistPaid(sender, npc, paid, out reason); }
                catch (Exception ex)
                {
                    // Kept: once they are enlisted the fee is theirs - a refusal now would give it back as well (a copy)
                    if (who == null || was || !who.Enlisted) throw;
                    ScLog.Once("enc-enlist-after", ex);
                    ok = true;
                    reason = null;
                }
            }
            if (ok) reply.Done();                                         // EnlistPaid told them
            else reply.Fail(reason ?? "scEncStays", "ui_denied");
            ScTalkContext.SendFactsAfter(sender, npc);
        }

        [ScAction("enc.gohome", ScActionAccess.Community)]
        private static void GoHome(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            EntityAlive npc = Npc(sender, args);
            EncPerson p = ScEnclavePeople.PersonOf(npc);
            // MP6: any community player may send them home (one shared community; their enlistment counts the community's followers)
            if (npc == null || p == null || !p.Enlisted || !ScEnclaveDeals.GoHome(npc, "the player sent them home")) { reply.Fail(""); return; }
            reply.Done();
            ScTalkContext.SendFactsAfter(sender, npc);
        }

        [ScAction("enc.recruit", ScActionAccess.Community)]
        private static void Recruit(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            EntityAlive npc = Npc(sender, args);
            if (npc == null) { reply.Fail("scEncStays", "ui_denied"); return; }
            EncPerson p = ScEnclavePeople.PersonOf(npc);
            Prospect pr = p != null ? ScEnclaveDeals.ProspectByPerson(p.Id) : null;
            // a client's journal verdict: its POI favour quest is past the clearing - marked done first, as member.join does
            if (pr != null && !pr.Done && pr.Favour == FavourKind.Poi && args.Int(1, 0) != 0 && !(sender is EntityPlayerLocal))
            {
                ScFavours.MarkDone(npc, pr);
                ScRegistry.SaveAtomic();
                ScLog.Out("favour done: " + pr.PoiName + " for " + pr.Name + " - " + sender.PlayerDisplayName + "'s quest is past the clearing");
            }
            string reason;
            if (!ScEnclaveDeals.Recruit(sender, npc, out reason))
            {
                reply.Fail(reason ?? "scEncStays", "ui_denied");
                ScTalkContext.SendFactsAfter(sender, npc);
                return;
            }
            reply.Done();
            // their favour quest closes on the sender's own journal (ScCommunity.Admit closed everyone else's)
            if (pr != null && !(sender is EntityPlayerLocal)) reply.SetHint(ScActionsMembers.HintCloseFavour, pr.Id).SetHint(ScActionsMembers.HintCloseDone, pr.Done ? 1 : 0);
        }

        [ScAction("enc.join", ScActionAccess.Community)]
        private static void Join(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            EntityAlive npc = Npc(sender, args);
            if (npc == null) { reply.Fail("scEncStays", "ui_denied"); return; }
            string reason;
            if (ScEncJoin.Join(sender, npc, out reason)) { reply.Done(); return; }     // JoinNow told them
            reply.Fail(reason ?? "scEncStays", "ui_denied");
            ScTalkContext.SendFactsAfter(sender, npc);
        }

        // ------------------------------------------------------------------ E7 ----

        [ScAction("enc.gift", ScActionAccess.Community)]
        private static void Gift(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            EntityAlive npc = Npc(sender, args);
            List<ItemStack> stacks = ScItemTx.DecodeAll(args.Strs);
            string key = "scEncReqGone", info = "";
            Enclave e0;
            EnclaveRequest r0;
            int giftDay = npc != null && ScEncRequests.LeaderAt(npc, out e0, out r0) ? e0.GiftDay : int.MinValue;
            bool ok;
            try { ok = npc != null && stacks.Count == 1 && ScEncGifts.GiveFor(sender, npc, stacks[0], out key, out info); }
            catch (Exception ex)
            {
                ok = Kept(ex, "enc-gift-after", giftDay != int.MinValue && LeaderGiftDay(npc) != giftDay);
                key = "scEncReqGone";
                info = "";
            }
            if (npc != null && stacks.Count != 1) key = "scEncReqNotCarried";
            Line(reply, ok, key, info);
            ScTalkContext.SendFactsAfter(sender, npc);
        }

        // ------------------------------------------------------------------ E4, E5 ----

        [ScAction("enc.req", ScActionAccess.Community)]
        private static void Req(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            EntityAlive npc = Npc(sender, args);
            int what = args.Int(1, 0);
            string key = "scEncReqGone", info = "";
            bool ok = false;
            Enclave e;
            EnclaveRequest r;
            if (npc != null && ScEncRequests.LeaderAt(npc, out e, out r))
            {
                switch (what)
                {
                    case ReqAccept: ok = ScEncRequests.AcceptAs(sender, e, r, out key, out info); break;
                    case ReqDecline: ok = ScEncRequests.DeclineAs(e, r, out key, out info); break;
                    case ReqThink: ok = ScEncRequests.ThinkAs(sender, npc, e, r); break;
                    case ReqCantDo: ok = ScEncRequests.CantDoAs(e, r, out key, out info); break;
                    case ReqFound: ok = ScEncRequests.FoundAs(e, r, out key, out info); break;
                    case ReqCleared: ok = ScEncRequests.ClearedAs(e, r, out key, out info); break;
                }
            }
            if (what == ReqThink)
            {
                // no reply line: the talk closes, the leader's word is a tell (ThinkAs)
                if (ok) reply.Done();
                else reply.Fail("");
                return;
            }
            Line(reply, ok, key, info);
            ScTalkContext.SendFactsAfter(sender, npc);
        }

        [ScAction("enc.reqgive", ScActionAccess.Community)]
        private static void ReqGive(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            EntityAlive npc = Npc(sender, args);
            string key = "scEncReqGone", info = "";
            EnclaveRequest open = OpenRequest(npc);
            bool ok;
            try { ok = npc != null && ScEncRequests.GiveFor(sender, npc, ScItemTx.DecodeAll(args.Strs), out key, out info); }
            catch (Exception ex)
            {
                ok = Kept(ex, "enc-reqgive-after", open != null && !ScRegistry.EncRequests.Contains(open));
                key = "scEncReqGone";
                info = "";
            }
            Line(reply, ok, key, info);
            ScTalkContext.SendFactsAfter(sender, npc);
        }

        [ScAction("enc.reqgun", ScActionAccess.Community)]
        private static void ReqGun(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            EntityAlive npc = Npc(sender, args);
            List<ItemStack> stacks = ScItemTx.DecodeAll(args.Strs);
            string key = "scEncReqGone", info = "";
            EnclaveRequest open = OpenRequest(npc);
            bool ok;
            try { ok = npc != null && stacks.Count == 1 && ScEncRequests.GunFor(sender, npc, stacks[0], out key, out info); }
            catch (Exception ex)
            {
                ok = Kept(ex, "enc-reqgun-after", open != null && !ScRegistry.EncRequests.Contains(open));
                key = "scEncReqGone";
                info = "";
            }
            if (npc != null && stacks.Count != 1) key = "scEncReqNotCarried";
            Line(reply, ok, key, info);
            ScTalkContext.SendFactsAfter(sender, npc);
        }

        [ScAction("enc.follow", ScActionAccess.Community)]
        private static void Follow(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            EntityAlive npc = Npc(sender, args);
            if (npc != null && ScEncSearch.FollowAt(sender, npc)) reply.Done();
            else reply.Fail("");
            ScTalkContext.SendFactsAfter(sender, npc);
        }

        [ScAction("enc.wait", ScActionAccess.Community)]
        private static void Wait(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            EntityAlive npc = Npc(sender, args);
            if (npc != null && ScEncSearch.WaitAt(sender, npc)) reply.Done();
            else reply.Fail("");
            ScTalkContext.SendFactsAfter(sender, npc);
        }

        [ScAction("enc.search", ScActionAccess.Community)]
        private static void Search(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            Vector3i pos = args.Pos(0);
            if (!(sender is EntityPlayerLocal))
            {
                Vector3 c = new Vector3(pos.x + 0.5f, pos.y + 0.5f, pos.z + 0.5f);
                if ((c - sender.position).sqrMagnitude > SearchRange * SearchRange) { reply.Fail(""); return; }
            }
            ScEncSearch.SearchAtFor(pos, sender);         // its tells answer the sender
            reply.Done();
        }

        [ScAction("enc.seen", ScActionAccess.Community, Quiet = true)]
        private static void Seen(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            EntityAlive npc = Npc(sender, args);
            if (npc != null && ScEncRequests.SeenFor(sender, npc, args.Int(1, 0))) reply.Done();
            else reply.Fail("", "");
        }
    }
}
