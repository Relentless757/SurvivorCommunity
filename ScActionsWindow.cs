using System;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round MP2 (part W, "window and facilities"): the server's side of the community window - its Members, Outposts and
    /// Facilities tabs -, the Watch post's and the well / dew collector's radials, and the "That's all for now." dialog row.
    /// Every one of those buttons calls ScActions.Request on every machine, the host included (single player and the host run
    /// the handler inline, so this is their only path too). The handlers run on the server with the player who asked: they
    /// re-check the target's CURRENT state with the verbs' own tests (a client's copy can be a push behind: a stale button only
    /// costs a refusal), act with that player, and answer with the message and sound the old inline code showed. A verb's
    /// error key goes back as the key (each machine localizes it); a line the verb or the old window formatted goes back as
    /// text (the server's language; MP4 moves those to keys).
    ///
    /// Ops (whole numbers unless marked s:; member = Member.Id, the body's entity id):
    ///   member.run           member, kind (Resource; -1 for a mining run), job (RunJob; -1 for a supply run)
    ///   member.work          member, intent                 Call to work / End work (ScWork.CallOrEnd)
    ///   member.workend       member                         "That's all for now." (silent when she is not on a shift)
    ///   member.role          member, role (0..3), want      a crafting role on or off (ScCraft.ToggleRole)
    ///   member.post          member, outpost id             posted (ScGarrison.Post); answers the hint outpost=&lt;id&gt;
    ///   member.watch         member, x, y, z                takes the watch at that post (ScWatch.Assign)
    ///   member.recallposted  member                         a staff row's Recall (ScGarrison.Recall)
    ///   outpost.disband      outpost id, forget (0/1)       Owner. Disband a held outpost, or forget a lost one's record
    ///   outpost.upgrade      outpost id, seen               repairs a damaged outpost, else buys its next level
    ///   outpost.mode         outpost id, seen               normal -> quiet -> push
    ///   facility.do          slot, member (-1: none), pick state; s: entry key, s: action id   the Facilities tab's action (see FacilityDo)
    ///   facility.upgrade     x, y, z, seen                  the well / dew collector radial's Upgrade (the parent block)
    ///   watch.relieve        x, y, z                        the Watch post radial's Relieve guard
    /// Round MP2 (review core/window M1): the toggles and the dual-purpose buttons carry what the button meant as their LAST
    /// int (absent or -1: no check, the old behavior), and a state that no longer matches answers scActionChanged - two
    /// players (or one double click on a client a push behind) pressing the same button get one action and a refusal, never
    /// the opposite action. member.work: 1 Call to work, 0 End work. member.role: 1 turn it on, 0 off. outpost.upgrade: 0
    /// Repair, else the level the Upgrade was pressed at. outpost.mode: the mode shown (0..2). facility.upgrade: the level
    /// shown. facility.do: a pick's state (FacilityAction.PickState: Call to work's 1 call / 0 end); its Upgrade and Assign
    /// guard carry their level and post in the action's id (ScFacilityTab), which the slot-and-id match checks. The host
    /// computes the intent from the state the handler reads in the same frame, so the check always passes there.
    /// </summary>
    public static class ScActionsWindow
    {
        // ------------------------------------------------------------------ asking (every machine) ----

        /// <summary>
        /// Why this machine cannot ask the server (a localization key), or null when it can: the server itself (single player,
        /// the host), or a client holding the server's copy from a server with SC's protocol. The radials read it to show their
        /// SC commands; a refusal says it.
        /// </summary>
        public static string RequestRefusal()
        {
            if (ScCommunity.IsHost) return null;
            if (!ScNet.IsClient) return "scActionNotReady";
            if (!ScNet.ServerMatches) return ScNet.ServerProtocol > 0 ? "scNetMismatch" : "scActionNoServer";
            if (!ScRegistry.Mirrored) return "scActionNotReady";
            return null;
        }

        public static bool CanRequest { get { return RequestRefusal() == null; } }

        // ------------------------------------------------------------------ helpers (the server) ----

        private static Member MemberArg(ScActionArgs args, int i, ScActionReply reply)
        {
            Member m = ScRegistry.GetMember(args.Int(i, -1));
            if (m == null) reply.Fail("scActionMemberGone");
            return m;
        }

        private static Outpost OutpostArg(ScActionArgs args, int i, ScActionReply reply)
        {
            Outpost o = ScOutposts.ById(args.Int(i, -1));
            if (o == null) reply.Fail("scActionOutpostGone");
            return o;
        }

        /// <summary>A verb's (ok, message): done with its line, or refused with it and the denied sound - as the window did.</summary>
        private static void Answer(ScActionReply reply, bool ok, string message)
        {
            if (ok) reply.Done(message ?? ""); else reply.Fail(message ?? "");
        }

        // ------------------------------------------------------------------ the Members tab ----

        /// <summary>A supply run (OnRun) or a mining run (OnRunJob): the line the window showed, the reason key otherwise.</summary>
        [ScAction("member.run", ScActionAccess.Community)]
        private static void MemberRun(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            Member m = MemberArg(args, 0, reply);
            if (m == null) return;
            int kind = args.Int(1, -1), job = args.Int(2, -1);
            EntityAlive npc = ScCommunity.FindNpc(m.Id);
            string err;
            if (job >= 0)
            {
                if (job == (int)RunJob.Supply || !Enum.IsDefined(typeof(RunJob), job)) { reply.Fail("scInternal"); return; }
                if (ScMining.Send(m, npc, (RunJob)job, sender, out err))
                    reply.Done(string.Format(Localization.Get("scRunSent"), m.Name, ScRuns.What(m), m.RunEndDay));
                else reply.Fail(err ?? "scInternal");
                return;
            }
            if (kind < 0 || kind >= ScNames.ResourceCount) { reply.Fail("scInternal"); return; }
            Resource r = (Resource)kind;
            if (ScRuns.Send(m, npc, r, sender, out err))
                reply.Done(string.Format(Localization.Get("scRunSent"), m.Name, Localization.Get(ScNames.LocKey(r)), m.RunEndDay));
            else reply.Fail(err ?? "scInternal");
        }

        /// <summary>The work row's Call to work / End work (OnWork; the Facilities tab's pick goes through facility.do).</summary>
        [ScAction("member.work", ScActionAccess.Community)]
        private static void MemberWork(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            Member m = MemberArg(args, 0, reply);
            if (m == null) return;
            // round MP2 (review core/window M1): 1 Call to work, 0 End work - a press on a shift that started or ended since is
            // refused, not turned into the opposite (two players calling the same resident must not end her shift)
            int intent = args.Int(1, -1);
            if (intent >= 0 && (intent == 0) != ScWork.OnWorkShift(m.Id)) { reply.Fail("scActionChanged"); return; }
            string msg;
            bool ok;
            ScWork.CallOrEnd(m, sender, out msg, out ok);
            Answer(reply, ok, msg);
        }

        /// <summary>
        /// "That's all for now." (DialogActionScWorkEnd): her shift ends and she walks back to her post. As the row did on the
        /// host: nothing at all when she is no member or on no shift; else the talker is told she said so (the reply's line,
        /// which the row shows as a tooltip) and she says it as the talk closes.
        /// </summary>
        [ScAction("member.workend", ScActionAccess.Community)]
        private static void MemberWorkEnd(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            Member m = ScRegistry.GetMember(args.Int(0, -1));
            if (m == null || !ScWork.OnShift(m.Id)) { reply.Fail("", ""); return; }
            EntityAlive npc = ScCommunity.FindNpc(m.Id);
            ScWork.End(m, npc, "That's all for now.", true, false);
            // her voice plays on the talker's machine: the host's own player's here (MP4: SC's voices reach clients)
            if (sender is EntityPlayerLocal) ScVoice.SayAfterTalk(npc, "scWorkEndSaid");
            reply.Done(string.Format(Localization.Get("scEncCallSays"), m.Name, Localization.Get("scWorkEndSaid")));
        }

        /// <summary>A crafting role button (OnRole).</summary>
        [ScAction("member.role", ScActionAccess.Community)]
        private static void MemberRole(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            Member m = MemberArg(args, 0, reply);
            if (m == null) return;
            int role = args.Int(1, -1), want = args.Int(2, -1);
            // round MP2 (review core/window M1): the state wanted (1 on, 0 off) - a role already so is refused, not toggled back.
            // Only on a loaded body (ToggleRole's own test): without one ToggleRole answers scNotLoaded as before
            if (want >= 0 && role >= 0 && role < ScCraft.RoleCvars.Length)
            {
                EntityAlive npc = ScCommunity.FindNpc(m.Id);
                if (npc != null && !npc.IsDead() && npc.Buffs != null && ((ScCraft.ReadMask(npc) & (1 << role)) != 0) == (want != 0))
                {
                    reply.Fail("scActionChanged");
                    return;
                }
            }
            string msg;
            Answer(reply, ScCraft.ToggleRole(m, role, out msg), msg);
        }

        /// <summary>The Outposts tab's Assign, the member picked (TryAssign): she sets off. Hint outpost=&lt;id&gt;: the window goes there.</summary>
        [ScAction("member.post", ScActionAccess.Community)]
        private static void MemberPost(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            Member m = MemberArg(args, 0, reply);
            if (m == null) return;
            Outpost o = OutpostArg(args, 1, reply);
            if (o == null) return;
            string err;
            if (!ScGarrison.Post(m, ScCommunity.FindNpc(m.Id), o, sender, out err)) { reply.Fail(err ?? "scInternal"); return; }
            reply.Done(string.Format(Localization.Get("scOpSetsOff"), m.Name, o.Name, ScGarrison.EtaHours(m))).SetHint("outpost", o.Id);   // round op1: on foot, about N h
        }

        /// <summary>The Watch post's Assign guard, the member picked (TryAssignWatch). ScWatch.Assign tells the player.</summary>
        [ScAction("member.watch", ScActionAccess.Community)]
        private static void MemberWatch(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            Member m = MemberArg(args, 0, reply);
            if (m == null) return;
            Vector3i post = args.Pos(1);
            string err;
            if (!ScWatch.Assign(post, m, sender, out err)) { reply.Fail(ScWatch.ErrorText(err, m, post)); return; }
            reply.Done();
        }

        /// <summary>A staff row's Recall on the Outposts tab (OnStaffRecall): posted staff walk home, one on the way turns round.</summary>
        [ScAction("member.recallposted", ScActionAccess.Community)]
        private static void MemberRecallPosted(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            Member m = MemberArg(args, 0, reply);
            if (m == null) return;
            bool onTheWay = ScGarrison.HeadingOut(m);
            if (m.Status != MemberStatus.Garrison && !onTheWay) { reply.Fail("scActionChanged"); return; }      // home or turned round since
            Outpost o = ScOutposts.ById(m.OutpostId);
            ScGarrison.Recall(m, sender, null);
            ScRegistry.SaveAtomic();
            reply.Done(string.Format(Localization.Get(onTheWay ? "scOpTurnedBack" : "scOpRecalled"), m.Name, o != null ? o.Name : "?"));
        }

        // ------------------------------------------------------------------ the Outposts tab ----

        /// <summary>
        /// Disband (the window's second press; the confirm stays on the client) or, forget = 1, a lost outpost's record dropped
        /// now. Owner: SharedOwnerOnly keeps it to the owner and admins. Hint gone=&lt;id&gt;.
        /// </summary>
        [ScAction("outpost.disband", ScActionAccess.Owner)]
        private static void OutpostDisband(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            Outpost o = OutpostArg(args, 0, reply);
            if (o == null) return;
            bool forget = args.Int(1, 0) != 0;
            // the button the player pressed (Disband or Forget) no longer fits the outpost: look again
            if (forget == o.Holding) { reply.Fail("scActionChanged"); return; }
            if (forget)
            {
                ScRegistry.Outposts.Remove(o);
                ScRegistry.SaveAtomic();
                reply.Done().SetHint("gone", o.Id);
                return;
            }
            ScOutposts.Disband(o, sender);
            reply.Done(string.Format(Localization.Get("scOpDisbanded"), o.Name)).SetHint("gone", o.Id);
        }

        /// <summary>Upgrade, which repairs a damaged outpost instead (OnOutpostUpgrade).</summary>
        [ScAction("outpost.upgrade", ScActionAccess.Community)]
        private static void OutpostUpgrade(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            Outpost o = OutpostArg(args, 0, reply);
            if (o == null) return;
            // round MP2 (review core/window M1): what the button was - 0 Repair, else Upgrade at the level shown. A second Repair
            // on an outpost repaired since must not buy the next level, a second Upgrade not the level after
            int seen = args.Int(1, -1);
            if (seen >= 0)
            {
                bool damaged = ScOutpostRisk.DamagedNow(o);
                if (seen == 0 ? !damaged : (damaged || SeenLevel(o) != seen)) { reply.Fail("scActionChanged"); return; }
            }
            string msg;
            bool ok = ScOutpostRisk.DamagedNow(o) ? ScOutpostRisk.Repair(o, sender, out msg) : ScOutpostPerks.Upgrade(o, sender, out msg);
            Answer(reply, ok, msg);
        }

        /// <summary>Round MP2 (review core/window M1): outpost.upgrade's intent as the window sends it - 0 Repair, else the level (1..3).</summary>
        public static int UpgradeIntent(Outpost o)
        {
            if (o == null) return -1;
            return ScOutpostRisk.DamagedNow(o) ? 0 : SeenLevel(o);
        }

        private static int SeenLevel(Outpost o) { return Math.Max(1, o.Level); }

        /// <summary>Round MP2 (review core/window M1): outpost.mode's intent - the mode the button shows (0..2).</summary>
        public static int ModeIntent(Outpost o)
        {
            return o != null ? Math.Max(0, Math.Min(2, o.Mode)) : -1;
        }

        /// <summary>Mode: normal -> quiet -> push (a held outpost; the window shows the button for those only).</summary>
        [ScAction("outpost.mode", ScActionAccess.Community)]
        private static void OutpostMode(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            Outpost o = OutpostArg(args, 0, reply);
            if (o == null) return;
            if (!o.Holding) { reply.Fail("scOpPostLost"); return; }
            // round MP2 (review core/window M1): the mode the button showed - two presses must not skip a mode
            int seen = args.Int(1, -1);
            if (seen >= 0 && ModeIntent(o) != seen) { reply.Fail("scActionChanged"); return; }
            ScOutpostPerks.CycleMode(o);
            ScRegistry.SaveAtomic();
            reply.Done(string.Format(Localization.Get("scOpModeSet"), o.Name, ScOutpostPerks.ModeLabel(o)));
        }

        // ------------------------------------------------------------------ the Facilities tab ----

        /// <summary>
        /// One action of the Facilities tab. The client sends the entry's key, the action's slot and its id (FacilityAction.MatchKey:
        /// the label's localization key, the same in every language), and the member picked for an action that picks one. The
        /// server rebuilds the tab's entries for the SENDER (ScFacilityTab.Build(sender): their position, their gamestage),
        /// finds the entry by key and the action by slot AND id - a list that changed in between cannot fire the wrong button -,
        /// checks the action's own access (rationing: Owner) and its WhyNot, then runs Run, or Pick on the member, with the sender.
        /// </summary>
        [ScAction("facility.do", ScActionAccess.Community)]
        private static void FacilityDo(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            int slot = args.Int(0, -1), memberId = args.Int(1, -1);
            string key = args.Str(0), id = args.Str(1);
            FacilityEntry e = null;
            foreach (FacilityEntry x in ScFacilityTab.Build(sender)) if (x.Key == key) { e = x; break; }
            FacilityAction a = e != null && slot >= 0 && slot < e.Actions.Count ? e.Actions[slot] : null;
            // round MP2 (re-check m1): an Assign guard pick names its post - another first empty post here is no reason to refuse
            Vector3i guardPost = Vector3i.zero;
            bool guardPick = memberId >= 0 && a != null && ScFacilityTab.TryGuardPost(id, out guardPost)
                             && a.MatchKey.StartsWith(ScFacilityTab.AssignGuardIdPrefix, StringComparison.Ordinal);
            if (a == null || (a.MatchKey != id && !guardPick)) { reply.Fail("scActionChanged"); return; }
            if (!ScActions.Allowed(sender, a.Access)) { reply.Fail("scActionDenied"); return; }
            string why;
            try { why = a.WhyNot != null ? a.WhyNot() : null; }
            catch (Exception ex) { ScLog.Once("community-facility-whynot", ex); why = Localization.Get("scInternal"); }
            if (why != null) { reply.Fail(why); return; }
            string msg = null;
            bool ok = false;
            if (memberId >= 0)
            {
                if (a.Pick == null) { reply.Fail("scActionChanged"); return; }
                Member m = ScRegistry.GetMember(memberId);
                if (m == null) { reply.Fail("scActionMemberGone"); return; }
                // round MP2 (review core/window M1): the pick's state the client saw (Call to work: 1 call, 0 end) - one that
                // changed since is refused, not turned into the opposite
                int seen = args.Int(2, -1);
                if (seen >= 0 && a.PickState != null && a.PickState(m) != seen) { reply.Fail("scActionChanged"); return; }
                try { ok = guardPick ? ScFacilityTab.AssignGuardAt(guardPost, m, sender, out msg) : a.Pick(m, sender, out msg); }   // re-check m1
                catch (Exception ex) { ScLog.Once("community-facility-pick", ex); msg = Localization.Get("scInternal"); ok = false; }
            }
            else
            {
                if (a.Run == null) { reply.Fail("scActionChanged"); return; }
                try { ok = a.Run(sender, out msg); }
                catch (Exception ex) { ScLog.Once("community-facility-action", ex); msg = Localization.Get("scInternal"); ok = false; }
            }
            Answer(reply, ok, msg);
        }

        // ------------------------------------------------------------------ the radials ----

        /// <summary>The well / dew collector radial's Upgrade (ScFacilities.Handle): the block at x,y,z (its parent) one level up.</summary>
        [ScAction("facility.upgrade", ScActionAccess.Community)]
        private static void FacilityUpgrade(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            World w = ScCommunity.World;
            if (w == null) { reply.Fail("scInternal"); return; }
            Vector3i pos = args.Pos(0);
            BlockValue bv = w.GetBlock(pos);
            pos = ScFacilities.ParentOf(w, pos, ref bv);
            if (!ScFacilities.IsFacility(bv.type)) { reply.Fail("scUiFacSiteGone"); return; }
            // round MP2 (review core/window M1): the level the radial showed - two presses must not buy two levels
            int seen = args.Int(3, -1);
            if (seen >= 0 && ScFacilities.LevelAt(pos, bv) != seen) { reply.Fail("scActionChanged"); return; }
            string msg;
            Answer(reply, ScFacilities.TryUpgrade(w, pos, bv, sender, out msg), msg);
        }

        /// <summary>The Watch post radial's Relieve guard (ScWatch.RelieveAt): the shift ends early - told to the players at the base.</summary>
        [ScAction("watch.relieve", ScActionAccess.Community)]
        private static void WatchRelieve(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            Vector3i post = args.Pos(0);
            Member g = ScWatch.GuardOf(post);
            if (g == null) { reply.Fail(ScWatch.ErrorText("scWatchNoGuard", null, post)); return; }
            ScWatch.Relieve(g, sender, "relieved at the post");
            reply.Done();
        }
    }
}
