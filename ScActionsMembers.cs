using System;
using System.Collections.Generic;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round MP2 (multiplayer, part M: members and dialogs): the server's handlers for the member orders, joining, the recruit
    /// favours and a band's join (ScActions' [ScAction] ops). The community window and the talk's rows ask through
    /// ScActions.Request on every machine; on the server (single player, the host) the handler runs inline with the local
    /// player, so the host's own play takes this same path. Each handler re-checks the record's CURRENT state (a client's copy
    /// can be a push behind) with the verbs' own tests, acts with the sender, and answers with the message and sound the old
    /// inline code showed. Journal work is the acting player's own machine's: the host's is done here, a client's comes back
    /// as a reply hint (HintCloseFavour) that the talk's callback performs (ScTalkContext.ApplyQuestHint).
    ///
    /// Ops (ints unless marked s:; "member" is Member.Id, "npc" the NPC's entity id):
    ///   member.follow  member, mode   mode 0 (or absent): the window's branch - missing: call back (ScMissing.Restore, hint
    ///                                 select = the new id); on the way to an outpost: turn round; on a physical run: recall
    ///                                 (the radio rule); posted: recall; following: go home; else follow. Mode 1: the talk's
    ///                                 "Follow me" (ScCommunity.Follow only); mode 2: the talk's "Head home" to a runner met
    ///                                 out in the world (ScRunners.Recall only, no radio needed: in person).
    ///   member.home    member, mode   ScCommunity.ReturnHome; mode 1: the talk's "Go home" (silent without a body).
    ///   member.leave   member, mode   (Owner) ScCommunity.Leave; mode 0: the window's (a downed member refused: scDownedBusy);
    ///                                 mode 1: the talk's "Ask to leave" (Leave's own refusal tells her leader).
    ///   member.join    npc, journal   ScCommunity.Join; journal 1: the asking player's POI favour quest for them is past its
    ///                                 clearing (a client's verdict: marked done first, as ScFavourDone's requirement does on
    ///                                 the host). Hint fclose/fdone: the favour quest to close.
    ///   favour.start   npc, kind      the favour taken on (Prospect.Started) - the quest itself is the client's journal's
    ///                                 (kind: FavourKind, 1 POI, 2 supplies).
    ///   favour.poicleared npc, journal "Actually, I've already cleared that.": the POI reads clear on the server, or the
    ///                                 player's quest is past the clearing - done. Hint: the quest to close (completed).
    ///   favour.items   npc, any       the supplies hand-over, step 1: may they take it now? Hint fres (Resource) and fcount.
    ///   favour.given   npc, any, res, taken   step 2: the client took the items - done, or refused with hint refund=1
    ///                                 (the items go back). Hint: the quest to close (completed).
    ///   band.join      npc            "Welcome to the community.": the whole band joins (ScBands.Join).
    /// </summary>
    public static class ScActionsMembers
    {
        /// <summary>member.follow / member.home / member.leave: the mode argument.</summary>
        public const int ModeWindow = 0, ModeTalk = 1, ModeTalkRecall = 2;

        /// <summary>member.follow: the member's id after the order (a call-back respawns them under a new entity id).</summary>
        public const string HintSelect = "select";
        /// <summary>The favour quest (its prospect id) the asking player's machine closes, and whether as completed (1) or removed (0).</summary>
        public const string HintCloseFavour = "fclose", HintCloseDone = "fdone";
        /// <summary>favour.items: what to take (Resource) and how many.</summary>
        public const string HintResource = "fres", HintCount = "fcount";
        /// <summary>favour.given refused: the items the client took go back.</summary>
        public const string HintRefund = "refund";

        private static string Fmt(string key, params object[] args)
        {
            return string.Format(Localization.Get(key), args);
        }

        /// <summary>The favour quest closes on the sender's journal: here for the host's own player (as before), else by the hint.</summary>
        private static void CloseFavourFor(EntityPlayer sender, int prospectId, bool completed, ScActionReply reply)
        {
            if (sender is EntityPlayerLocal) ScQuests.CloseFavour(sender, prospectId, completed);
            else reply.SetHint(HintCloseFavour, prospectId).SetHint(HintCloseDone, completed ? 1 : 0);
        }

        /// <summary>
        /// The NPC a talk's request names: loaded and alive, and - for a remote player - within ScTalkContext.TalkRange of them
        /// (the host's own player talks to it right now: no new test there). Null otherwise.
        /// </summary>
        private static EntityAlive TalkNpc(EntityPlayer sender, int npcId)
        {
            World w = ScCommunity.World;
            // round MP2 (review members NIT 1): ScNpc.Loaded, as ScCommunity.FindNpc - a body still being built is not forced
            EntityAlive npc = w != null && npcId > 0 ? ScNpc.As(ScNpc.Loaded(w, npcId)) : null;
            return InTalkReach(sender, npc);
        }

        /// <summary>
        /// Round MP2 (review members MINOR 5): npc when the sender can be talking to it - always for the host's own player (no
        /// new test), for a remote player its live body within ScTalkContext.TalkRange. Null otherwise.
        /// </summary>
        private static EntityAlive InTalkReach(EntityPlayer sender, EntityAlive npc)
        {
            if (npc == null) return null;
            if (sender is EntityPlayerLocal) return npc;
            if (sender == null || npc.IsDead() || (npc.position - sender.position).sqrMagnitude > ScTalkContext.TalkRange * ScTalkContext.TalkRange) return null;
            return npc;
        }

        /// <summary>Round MP2 (review members MINOR 5): a favour op's NPC is in talking reach of a remote sender (the host's own player: always).</summary>
        private static bool FavourInReach(EntityPlayer sender, int npcId)
        {
            return sender is EntityPlayerLocal || TalkNpc(sender, npcId) != null;
        }

        // ------------------------------------------------------------------ member orders ----

        [ScAction("member.follow", ScActionAccess.Community)]
        private static void MemberFollow(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            int mode = args.Int(1, ModeWindow);
            Member m = ScGates.OwnMember(args.Int(0, -1));          // round MP6 (part G): the sender's community's only (another's takes no orders)
            if (m == null) { reply.Fail(mode == ModeWindow ? "scMemberGone" : ""); return; }
#if SC_V34
            if (ScWork.RefuseHeldByOther(m, sender, reply)) return;      // round mt1 (review fix H-m4): another player's talk keeps her
#endif
            EntityAlive npc = ScCommunity.FindNpc(m.Id);
            // round MP2 (review members MINOR 5): the talk's modes are in person - a remote sender beyond talking reach is answered
            // as a body that is not there (silent); mode 2 needs no radio only because of it
            if (mode == ModeTalk) { TalkFollow(sender, m, InTalkReach(sender, npc), reply); return; }
            if (mode == ModeTalkRecall) { TalkRecall(sender, m, InTalkReach(sender, npc), reply); return; }

            // the window's branch (XUiC_ScCommunity.OnFollow until round MP2), decided here on the record as it is NOW
            if (m.Status == MemberStatus.Arriving)
            {
                // E12: still on the way (ScEncJoin) - no orders until they turn up at home
                reply.Fail("scEncJoinOnTheWay");
                return;
            }
            if (ScMissing.IsMissing(m))
            {
                // the body is gone from the world: respawn them at their post from their record (Restore tells the sender too, as
                // it always did beside the window's line)
                string cerr;
                if (ScMissing.Restore(m, sender, false, out cerr)) reply.Done(Fmt("scMissingRestored", m.Name)).SetHint(HintSelect, m.Id);
                else reply.Fail(cerr ?? "scInternal");
                return;
            }
            if (m.Status == MemberStatus.Transit)
            {
                // round op1: on the way to an outpost - turn round now (no radio needed: a posting is not a run); walking home already: coming
                string terr;
                if (ScGarrison.TurnHome(m, sender, null, out terr)) reply.Done(Fmt("scOpTurnedBack", m.Name, ScGarrison.TransitName(m)));
                else reply.Fail(terr ?? "scInternal");
                return;
            }
            if (ScRunners.IsPhysical(m))
            {
                // out on a physical run: turn round now
#if SC_V34
                // R2 (user, 2026-11-09): only over their radio, or in person - asked only of a runner the recall could turn round,
                // so pinned down / already heading home keep their own refusals (the re-check's LOW-2)
                bool turnable = m.RunLeg == RunLeg.Outbound || (m.RunLeg == RunLeg.AtSite && !ScRunners.Pinned(m));
                if (turnable && !ScHandheld.WindowRecallAllowed(m, sender))       // round MP2: in person = the sender beside her
                {
                    ScHandheld.Count("window recall refused: runner has no radio");
                    reply.Fail(Fmt("scRecallNoRadio", m.Name));
                    return;
                }
                bool byRadio = m.RunRadio && !ScRadioTalk.InPerson(m, sender);
#endif
                string rerr;
                if (ScRunners.Recall(m, sender, out rerr))
                {
                    reply.Done(Fmt("scRunRecalled", m.Name));
#if SC_V34
                    if (byRadio) { ScRadioTalk.OnRecalled(m, sender); ScRegistry.SaveAtomic(); }      // R2: "Copy that." on the radio (round MP4: to the player who recalled)
#endif
                }
                else reply.Fail(rerr ?? "scInternal");
                return;
            }
            if (m.Status == MemberStatus.Garrison)
            {
                Outpost po = ScGarrison.PostOf(m);
                ScGarrison.Recall(m, sender, null);                             // round op1: they walk home (the line says so)
                ScRegistry.SaveAtomic();
                reply.Done(Fmt("scOpRecalled", m.Name, po != null ? po.Name : "?"));
                return;
            }
            string err = null;
            if (npc == null) err = "scNotLoaded";
            else if (FollowsOther(m, sender)) { reply.Fail(Fmt("scFollowingOther", m.Name)); return; }   // round MP2: not a toggle of someone else's follower
            else if (m.Status == MemberStatus.Following || m.Status == MemberStatus.PendingStation) { WindowHome(sender, m, npc, reply); return; }
            else if (ScCommunity.Follow(m, npc, sender, out err)) reply.Done(Fmt("scWinNowFollowing", m.Name));
            if (npc != null && ScVoiceRoutes.IsFollowFull(err)) ScVoiceRoutes.SayRefusal(npc, "scFollowFullSaid", "the community window's Follow me: no room in the party");   // round m1w (P7)
            if (err != null) reply.Fail(err);
        }

        /// <summary>
        /// Round MP2: she follows ANOTHER player who is online (two players pressing Follow at once: the second must not send her
        /// home through the window's toggle, nor be told she follows them). Never in single player: her leader is that player.
        /// </summary>
        private static bool FollowsOther(Member m, EntityPlayer sender)
        {
            if (m == null || sender == null || (m.Status != MemberStatus.Following && m.Status != MemberStatus.PendingStation)) return false;
            if (string.IsNullOrEmpty(m.LeaderId)) return false;
            EntityPlayer leader = ScPlayers.ById(m.LeaderId);
            return leader != null && !ReferenceEquals(leader, sender);
        }

        /// <summary>The talk's "Follow me" (DialogActionScFollow until round MP2): Follow only.</summary>
        private static void TalkFollow(EntityPlayer sender, Member m, EntityAlive npc, ScActionReply reply)
        {
            if (npc == null) { reply.Fail(""); return; }
            if (FollowsOther(m, sender)) { reply.Fail(Fmt("scFollowingOther", m.Name)); return; }    // round MP2
            string error;
            if (ScCommunity.Follow(m, npc, sender, out error)) { reply.Done(Fmt("scWinNowFollowing", m.Name)); return; }
            reply.Fail(error ?? "scInternal");
            // round m1w (P7): no room in the party - she says so too, once that line is recorded
            if (ScVoiceRoutes.IsFollowFull(error)) ScVoiceRoutes.SayRefusal(npc, "scFollowFullSaid", "the talk's Come with me: no room in the party", true, sender);   // round MP4 review M1: the talker
        }

        /// <summary>The talk's "Head home" to a runner met out in the world (DialogActionScRecall until round MP2): ScRunners.Recall.</summary>
        private static void TalkRecall(EntityPlayer sender, Member m, EntityAlive npc, ScActionReply reply)
        {
            if (npc == null) { reply.Fail(""); return; }
            string error;
            if (ScRunners.Recall(m, sender, out error)) reply.Done(Fmt("scRunRecalled", m.Name));
            else reply.Fail(error ?? "scInternal");
        }

        [ScAction("member.home", ScActionAccess.Community)]
        private static void MemberHome(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            int mode = args.Int(1, ModeWindow);
            Member m = ScGates.OwnMember(args.Int(0, -1));          // round MP6 (part G): the sender's community's only (another's takes no orders)
            if (m == null) { reply.Fail(mode == ModeWindow ? "scMemberGone" : ""); return; }
#if SC_V34
            if (ScWork.RefuseHeldByOther(m, sender, reply)) return;      // round mt1 (review fix H-m4): another player's talk keeps her
#endif
            EntityAlive npc = ScCommunity.FindNpc(m.Id);
            if (mode != ModeWindow)
            {
                // the talk's "Go home" (DialogActionScGoHome until round MP2). Round MP2 (review members MINOR 5): in person
                npc = InTalkReach(sender, npc);
                if (npc == null) { reply.Fail(""); return; }
                string error;
                if (ScCommunity.ReturnHome(m, npc, sender, out error)) reply.Done(Fmt("scStationed", m.Name));
                else reply.Fail(error ?? "scInternal");
                return;
            }
            WindowHome(sender, m, npc, reply);
        }

        /// <summary>The window's Home (XUiC_ScCommunity.OnHome until round MP2), and Follow on one already following.</summary>
        private static void WindowHome(EntityPlayer sender, Member m, EntityAlive npc, ScActionReply reply)
        {
            string err = null;
            if (m.Status == MemberStatus.Arriving) err = "scEncJoinOnTheWay";      // E12: still on the way
            else if (npc == null) err = "scNotLoaded";
            else if (ScCommunity.ReturnHome(m, npc, sender, out err)) reply.Done(Fmt("scStationed", m.Name));
            if (err != null) reply.Fail(err);
        }

        [ScAction("member.leave", ScActionAccess.Owner)]
        private static void MemberLeave(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            int mode = args.Int(1, ModeWindow);
            Member m = ScGates.OwnMember(args.Int(0, -1));          // round MP6 (part G): the sender's community's only (another's takes no orders)
            if (m == null) { reply.Fail(mode == ModeWindow ? "scMemberGone" : ""); return; }
#if SC_V34
            if (ScWork.RefuseHeldByOther(m, sender, reply)) return;      // round mt1 (review fix H-m4): another player's talk keeps her
#endif
            if (mode == ModeWindow)
            {
#if SC_V34
                if (ScDowned.MemberDown(m)) { reply.Fail("scDownedBusy"); return; }   // round U2: not while she is down (Leave tells her too)
#endif
                ScCommunity.Leave(m, "scLeft");          // its own tell goes to the whole community, as before
                reply.Done(Fmt("scLeft", m.Name));
                return;
            }
            // the talk's "Ask to leave" (DialogActionScLeave until round MP2): Leave's tell is the whole answer
            ScCommunity.Leave(m, "scLeft");
            if (ScRegistry.GetMember(m.Id) != null) reply.Fail("");        // refused (down): Leave told her leader
            else reply.Done();
        }

        // ------------------------------------------------------------------ joining ----

        [ScAction("member.join", ScActionAccess.Community)]
        private static void MemberJoin(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            EntityAlive npc = TalkNpc(sender, args.Int(0, -1));
            if (npc == null) { reply.Fail(""); return; }                    // the old row: silent without a body
            Prospect p = ScRegistry.OwnProspect(npc.entityId);      // round MP6 (part relations): the sender's community's own favour
            // a client's journal verdict: its POI favour quest is past the clearing - what ScFavourDone's requirement marks on the
            // host when the row is evaluated (the host's own requirements did it already: nothing new for it)
            if (p != null && !p.Done && p.Favour == FavourKind.Poi && args.Int(1, 0) != 0 && !(sender is EntityPlayerLocal))
            {
                ScFavours.MarkDone(npc, p);
                ScRegistry.SaveAtomic();
                ScLog.Out("favour done: " + p.PoiName + " for " + p.Name + " - " + sender.PlayerDisplayName + "'s quest is past the clearing");
            }
            if (p != null && !p.Done) { reply.Fail("scFavourNotDone"); return; }
            // round MP6 (fix R-M2): no favour of the sender's community while another community courts them - the sender's own
            // favour comes first (the user's decision 2), never a join on the other's. Never with one community
            if (p == null && ScCommunities.Count > 1 && ScZone.HasHome && ScRegistry.GetProspect(npc.entityId) != null) { reply.Fail("scFavourNotDone"); return; }   // (fix F-n3: no home - Join's own answer first)
            int favourId = p != null ? p.Id : 0;
            bool favourDone = p != null && p.Done;
            string error;
            if (!ScCommunity.Join(npc, sender, out error)) { reply.Fail(error ?? "scInternal"); return; }
            reply.Done();                                                   // Admit told the sender ("joined", or following until home)
            // a client's favour quest closes on its own journal (Admit closed the host's own)
            if (favourId != 0 && !(sender is EntityPlayerLocal)) reply.SetHint(HintCloseFavour, favourId).SetHint(HintCloseDone, favourDone ? 1 : 0);
        }

        // ------------------------------------------------------------------ favours ----

        [ScAction("favour.start", ScActionAccess.Community)]
        private static void FavourStart(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            Prospect p = ScRegistry.OwnProspect(args.Int(0, -1));      // round MP6 (part relations): the sender's community's own
            int kind = args.Int(1, 0);
            if (p == null || (int)p.Favour != kind || (p.Favour == FavourKind.Supplies && p.Done)) { reply.Fail("scFavourGone"); return; }
            if (!FavourInReach(sender, args.Int(0, -1))) { reply.Fail(""); return; }       // round MP2 (review members MINOR 5)
            ScFavourStart.Mark(p);
            if (p.Favour == FavourKind.Poi)
            {
                string why;
                ScLog.Out(p.PoiName + " for " + p.Name + (ScFavours.PoiReadsCleared(p, out why) ? " already reads clear" : " is not clear yet: " + why));
            }
            reply.Done();
        }

        [ScAction("favour.poicleared", ScActionAccess.Community)]
        private static void FavourPoiCleared(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            int npcId = args.Int(0, -1);
            Prospect p = ScRegistry.OwnProspect(npcId);      // round MP6 (part relations): the sender's community's own
            if (p == null || p.Favour != FavourKind.Poi) { reply.Fail(""); return; }
            if (!FavourInReach(sender, npcId)) { reply.Fail(""); return; }                  // round MP2 (review members MINOR 5)
            if (!p.Done)
            {
                string why;
                // the journal is the sender's own machine's: the host's read here, a client's verdict in the request
                bool journal = sender is EntityPlayerLocal ? ScQuests.FavourQuestDone(sender, p.Id) : args.Int(1, 0) != 0;
                if (!ScFavours.PoiReadsCleared(p, out why) && !journal)
                {
                    reply.Fail(Fmt("scFavourPoiNotClear", p.PoiName, why));
                    return;
                }
                ScFavours.MarkDone(ScCommunity.FindNpc(npcId), p);
                ScRegistry.SaveAtomic();
                ScLog.Out("favour done: " + p.PoiName + " was already clear (the player said so to " + p.Name + ")");
            }
            CloseFavourFor(sender, p.Id, true, reply);
            reply.Done();
        }

        /// <summary>The supplies hand-over's gates (DialogActionScTakeFavourItems until round MP2): null when it may go ahead.</summary>
        private static string HandOverRefusal(EntityPlayer sender, Prospect p, bool any, out bool silent)
        {
            silent = false;
            if (p == null || p.Favour != FavourKind.Supplies || p.Done) { silent = true; return "scFavourGone"; }
            // value="any": handed over in the talk that named them, before taking the favour on (ScHasFavourItems "any")
            if (!p.Started && !any) { silent = true; return "scFavourGone"; }
            // round MP7 (core), the user's decision 4: a player with only their own relations (PerPlayer/PerParty) does the favor
            // all the same - it is held done on their record, and she joins once they start a community (or the community they join
            // gets the done favor: ScMembership.Merge). Her join's own tests (home, roster) are asked then
            if (ScRegistry.Current.Personal) return null;
            // never take the goods when the join that follows would be refused. Round MP2 (review members MINOR 3): a remote
            // sender's hand-over never adopts their claim as the home (EnsureHomeFor: a read for them, EnsureHome for the host's)
            if (!ScZone.EnsureHomeFor(sender)) return ScZone.NeedHomeKey();
            if (ScRegistry.Members.Count >= ScConfig.MaxMembers) return "scRosterFull";
            return null;
        }

        [ScAction("favour.items", ScActionAccess.Community)]
        private static void FavourItems(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            Prospect p = ScRegistry.OwnProspect(args.Int(0, -1));      // round MP6 (part relations): the sender's community's own
            bool silent;
            string why = HandOverRefusal(sender, p, args.Int(1, 0) != 0, out silent);
            if (why != null) { reply.Fail(silent ? "" : why); return; }      // the old action said nothing for a favour not open
            if (!FavourInReach(sender, args.Int(0, -1))) { reply.Fail(""); return; }       // round MP2 (review members MINOR 5)
            reply.Done().SetHint(HintResource, (int)p.ItemCategory).SetHint(HintCount, p.ItemCount);
        }

        [ScAction("favour.given", ScActionAccess.Community)]
        private static void FavourGiven(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            int npcId = args.Int(0, -1);
            Prospect p = ScRegistry.OwnProspect(npcId);      // round MP6 (part relations): the sender's community's own
            bool silent;
            // checked again: another player may have done this favour, or filled the roster, since step 1 - the items go back
            string why = HandOverRefusal(sender, p, args.Int(1, 0) != 0, out silent);
            if (why != null) { reply.Fail(why).SetHint(HintRefund, 1); return; }
            if (!FavourInReach(sender, npcId)) { reply.Fail("").SetHint(HintRefund, 1); return; }     // round MP2 (review members MINOR 5): the items go back
            ScFavours.MarkDone(ScCommunity.FindNpc(npcId), p);
            ScRegistry.SaveAtomic();
            CloseFavourFor(sender, p.Id, true, reply);
            ScLog.Out(p.Name + " received " + args.Int(3, p.ItemCount) + " " + p.ItemCategory + " item(s)");
            reply.Done();
        }

        // ------------------------------------------------------------------ bands ----

        [ScAction("band.join", ScActionAccess.Community)]
        private static void BandJoin(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            string reason;
            // a body out of reach is nobody's to join: Join answers it as one who stays (scEncStays)
            if (!ScBands.Join(sender, TalkNpc(sender, args.Int(0, -1)), out reason)) reply.Fail(reason ?? "");
            else reply.Done();                                              // JoinNow told the sender
        }
    }
}
