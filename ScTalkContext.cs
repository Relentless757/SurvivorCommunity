using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round MP2 (part M): what the server told a client about one NPC when a talk with them opened (NetPackageScTalkContext,
    /// ScTalkContext). Plain data, copied for each package.
    /// </summary>
    public sealed class ScTalkFacts
    {
        public int Npc;
        /// <summary>Client: when it arrived (realtimeSinceStartup).</summary>
        public float At;
        /// <summary>ScCommunity.CanRecruit for the talking player, and its reason key when not.</summary>
        public bool Recruit;
        public string RecruitWhy = "";
        /// <summary>A home whose land claim stands (ScZone.EnsureHomeFor the talking player: a read for a remote one).</summary>
        public bool HasHome;
        /// <summary>The prospect (FavourKind, 0: none) and what the favour rows show and need.</summary>
        public int Favour;
        public bool Started, Done;
        public string Name = "";
        public Vector3i PoiPos;
        public int PoiWidth, PoiDepth;
        public string PoiName = "";
        /// <summary>A POI favour's building reads clear on the server (ScFavours.PoiReadsCleared: its sleeper volumes), and what is left.</summary>
        public bool PoiCleared;
        public string PoiWhy = "";
        public int ItemCategory, ItemCount;
        public int EncPerson;
        /// <summary>One of a drifter band (E13a): 0 no (or raiders, or against you), 1 wary, 2 trusted (ScBand's phases).</summary>
        public int Band;
        /// <summary>"Come to our place.": 0 not offered, 1 they say yes, 2 they say no (ScBandCome).</summary>
        public int BandCome;
        /// <summary>The band line's tokens (ScBands.Fill): {bandwhere}, {bandjoinfacts}, {name}, {leader}, {count}.</summary>
        public string BandWhere = "", BandJoinFacts = "", BandName = "", BandLeader = "";
        public int BandCount;
        /// <summary>
        /// Round mt1 (part H): the server linked this member to the talking player for this talk (ScWork.HoldForRemote: a new talk
        /// hold, or their own hold or shift already) - her force-sent Owner cvar names them. The client re-evaluates the rows once
        /// that cvar lands and keeps the hold alive with its heartbeat (ScTalkContext.HeldTick).
        /// </summary>
        public bool Linked;

        // ---- round MP5 (part E): one of an enclave's people, or a missing survivor's body (ScEncFacts.Fill on the server) ----

        /// <summary>Round MP5 (part E): EncFlags' bits - what the enclave rows ask, as the server's requirement code answers for this player.</summary>
        public const int EncBody = 1, EncAlive = 2, EncLeader = 4, EncMember = 8, EncEnlisted = 16, EncLeaderTalking = 32, EncCanTrade = 64,
                         EncEnlistShow = 128, EncRecruitShow = 256, EncNeedsFavour = 512, EncGiftShow = 1024, EncJoinShow = 2048,
                         EncCanGiveUp = 4096, EncThanks = 8192, EncInfestNew = 16384;
        /// <summary>
        /// Round MP5 (part E): EncBody - an enclave body (its line's tokens are in EncTokens); EncAlive - alive (ScEncDialog.PersonTalking);
        /// EncLeader / EncMember / EncEnlisted - ScEncRole; EncLeaderTalking - ScEncRequests.LeaderTalking; EncCanTrade; the show
        /// halves of the enlist, recruit, gift and join rows; EncNeedsFavour (the server's half: Neutral and their favour not done);
        /// EncCanGiveUp; EncThanks - {reqnone} is a thanks the line spends; EncInfestNew - {reqask} names an infestation the line
        /// makes known. 0 for anyone else.
        /// </summary>
        public int EncFlags;
        /// <summary>Round MP5 (part E): their enclave, its standing key (ScEnclaves.StandingKey) and kind (the gift pick's order).</summary>
        public int EncId;
        public string EncStanding = "", EncKind = "";
        /// <summary>
        /// Round MP5 (part E): why a row greys, the server's part ("" when that part passes; text in the server's language): the
        /// enlist row before the dukes (the client counts its own), the recruit row, the gift row before the bundles carried (the
        /// client's own bag), the join row. EncEnlistCost: the fee at the standing now.
        /// </summary>
        public string EncEnlistWhy = "", EncRecruitWhy = "", EncGiftWhy = "", EncJoinWhy = "";
        public int EncEnlistCost;
        /// <summary>Round MP5 (part E): the leader's open request - its id, state word ("none" when there is none, "" when not the leader), kind, stage word, resource and units.</summary>
        public int EncReqId;
        public string EncReqState = "", EncReqKind = "", EncReqStage = "";
        public int EncReqRes, EncReqUnits;
        /// <summary>Round MP5 (part E): "I found Mara. I'm sorry." (the row's words, the server's language), or "".</summary>
        public string EncFoundRow = "";
        /// <summary>Round MP5 (part E): a missing survivor's body: 0 not one, 1 alive and waiting, 2 following the player (ScEncSearch's phases).</summary>
        public int EncSearch;
        /// <summary>Round MP5 (part E): where their leader is ({where}: worked out on the client from its own player's position).</summary>
        public bool EncHasWhere;
        public Vector3 EncWhere;
        /// <summary>Round MP5 (part E): the line's tokens in ScEncDialog.Fill's order (shared by Copy: never changed once made).</summary>
        public List<ScEncToken> EncTokens = new List<ScEncToken>();

        public bool Enc(int bit) { return (EncFlags & bit) != 0; }       // round MP5 (part E)

        public ScTalkFacts Copy() { return (ScTalkFacts)MemberwiseClone(); }
    }

    /// <summary>
    /// Round MP5 (part E): one token of an enclave line (ScTalkFacts.EncTokens): the text in its place is Key localized on the
    /// showing machine (a line a voice says: its clip's name too), then Text (the server's words), either may be empty.
    /// </summary>
    public sealed class ScEncToken
    {
        public string Token = "", Key = "", Text = "";

        public string Render()
        {
            string k = Key.Length > 0 ? Localization.Get(Key) : "";
            if (k.Length == 0) return Text;
            return Text.Length == 0 ? k : k + " " + Text;
        }
    }

    /// <summary>
    /// Round MP2 (multiplayer, part M): the talk context. In 3.4 a dialog's requirements and actions run only on the talking
    /// client (XUiC_DialogResponseEntry, Dialog.SelectResponse), where SC's registry is a read-only copy (ScMirror) - and some
    /// requirements had side effects the server must do (E4: ScFavour rolled the favour; ScFavourDone marked it done) or read
    /// what only the server knows (the POI's sleeper volumes, the home's land claim, a band's people).
    /// - Client: when a talk with an SCore NPC opens (a postfix on XUiC_DialogWindowGroup.OnOpen) it sends Open(npc) with its
    ///   own journal's verdict on that NPC's favour quest. The answer (ScTalkFacts) is kept per NPC for the session; the open
    ///   response list is evaluated again when it lands, and the first statement picked again if the talk still shows the one
    ///   it opened on (a band's greeting is a phase: picked before any answer could come). Until it lands the client's rows
    ///   read as before (no prospect: the recruit rows hidden).
    /// - Server: rolls the profile (ScProfile.Ensure) and the favour (ScFavours.Ensure - never on a client), marks a POI favour
    ///   done when the client's journal says its quest is past the clearing (what ScFavourDone's requirement does on the host),
    ///   and answers with the facts. The host's own player never sends one: its requirements read the registry as always.
    /// - Every requirement in DialogOps / DialogOpsBands keeps its code on the server and has a client path that reads these
    ///   facts, the copy and the client's own journal and inventory. No requirement changes anything on a client.
    /// - Favour quests close on the journal's own machine: an action's reply carries it (ScActionsMembers' fclose hint), and
    ///   the quest op (CloseFavourForOthers; round MP4: NetPackageScQuestOp, ScQuestNet) reaches the other players who took the
    ///   same favour. The package's own Close is still read (OnFavourClose), but no longer sent.
    /// - Round mt1 (part H): the talk hold for a remote talker. A member's Open makes the server hold her for that player
    ///   (ScWork.HoldForRemote, before the facts: her force-sent Owner cvar goes first) and say so (ScTalkFacts.Linked). The
    ///   client re-evaluates its rows once that cvar lands, sends Held about once a second while its own ScWork.TalkOpen holds
    ///   (and through ScWork.TalkGraceSeconds after), then TalkClosed (HeldTick). Once her copy no longer names this player,
    ///   this machine's leftover hire mark, leader cache and companion marker for her go (SweepHireMarks).
    /// - Round MP5 (part E): one of an enclave's people. The facts carry their rows' verdicts and their line's tokens
    ///   (ScEncFacts.Fill, ScTalkFacts.Enc*); the enclave operators' client paths read them (ScEncTalk), and every enclave action
    ///   is an ScActions op (ScActionsEnclave). After an op the server sends that player the NPC's facts again (SendFactsAfter),
    ///   ahead of the answer; an answer that is a reply line ({reqreply}, {giftreply}) is filled into the statement already on
    ///   screen (RefillLine). A row's action that waits on the same row's supplies hand-over goes after it (EncAfterHandOver).
    /// Main thread only.
    /// </summary>
    public static class ScTalkContext
    {
        /// <summary>Server: an Open for an NPC farther than this from the sender is ignored (a talk is a few meters).</summary>
        public const float TalkRange = 20f;

        // ------------------------------------------------------------------ the client ----

        private static readonly Dictionary<int, ScTalkFacts> facts = new Dictionary<int, ScTalkFacts>();
        /// <summary>The talk open now: its NPC and the statement it opened on (the phase pick), for the re-pick when facts land.</summary>
        private static int openNpc = -1;
        private static DialogStatement openedOn;
        /// <summary>
        /// A client's supplies hand-over in flight (DialogActionScTakeFavourItems), by NPC (when it began), and the join its row
        /// asked for after it. One older than HandOverSeconds is forgotten: an answer that never came (ScActions drops it too).
        /// </summary>
        private static readonly Dictionary<int, float> handOver = new Dictionary<int, float>();
        private const float HandOverSeconds = 30f;
        private static readonly Dictionary<int, PendingJoin> joinAfter = new Dictionary<int, PendingJoin>();
        /// <summary>Round MP5 (part E): an enclave row's action waiting on that row's supplies hand-over (scEncGiveSupplies: ScEncRecruit after ScTakeFavourItems).</summary>
        private static readonly Dictionary<int, Action> encAfter = new Dictionary<int, Action>();
        private static int sent, answered, closes;

        private struct PendingJoin
        {
            public EntityPlayer Player;
            public bool JournalDone;
        }

        /// <summary>Round mt1 (part H), client: a member the server linked to this player for a talk (facts with Linked).</summary>
        private sealed class HeldLink
        {
            /// <summary>Real time (realtimeSinceStartup): when the facts said so, when this machine's talk with her was last open, the next beat.</summary>
            public float Since, LastOpen, NextBeat;
            /// <summary>Her copy's Owner cvar has shown this player (after that, a different value means the server ended the link).</summary>
            public bool OwnerSeen;
        }
        private static readonly Dictionary<int, HeldLink> held = new Dictionary<int, HeldLink>();
        /// <summary>Round mt1 (part H), client: members whose talk link ended here - this machine's leftovers on them go once she names us no more (SweepHireMarks).</summary>
        private static readonly HashSet<int> hireSweeps = new HashSet<int>();
        private static readonly List<int> heldScratch = new List<int>();
        private static float nextHeldTick;
        private static bool heldHooked;
        /// <summary>Round mt1 (part H): the client's heartbeat interval, how long the rows wait for the Owner cvar, the tick's pace.</summary>
        private const float BeatSeconds = 1f, OwnerWaitSeconds = 3f, HeldTickSeconds = 0.2f;
        private static int beatsSent, closedSent, ownerRefreshes;
        /// <summary>
        /// Round mt1 (review fixes H-m1, H-m3), client: one re-ask per member at most this often (Relink), and the owner-link scan's
        /// pace (ScanOwnerLinks); member id -> when she was last re-asked for; the next scan.
        /// </summary>
        private const float RelinkSeconds = 10f, MarkScanSeconds = 2f;
        private static readonly Dictionary<int, float> relinkAt = new Dictionary<int, float>();
        private static float nextMarkScan;
        private static int relinks;

        /// <summary>Every machine, at world start and shutdown.</summary>
        public static void Reset()
        {
            facts.Clear();
            openNpc = -1;
            openedOn = null;
            handOver.Clear();
            joinAfter.Clear();
            encAfter.Clear();                 // round MP5 (part E)
            ScEncTalk.Reset();                // round MP5 (part E): the client's enclave reply and picks (ModApi.cs is not this part's file)
            sent = answered = closes = 0;
            // round mt1 (part H): the client's held talks, and its tick (hooked into ModEvents.GameUpdate here, once per process,
            // as ScQuestNet's below: ModApi's GameUpdate handler is not this part's to edit)
            held.Clear();
            hireSweeps.Clear();
            nextHeldTick = 0f;
            beatsSent = closedSent = ownerRefreshes = 0;
            relinkAt.Clear();                 // round mt1 (review fixes H-m1, H-m3)
            nextMarkScan = 0f;
            relinks = 0;
            if (!heldHooked)
            {
                heldHooked = true;
                ModEvents.GameUpdate.RegisterHandler((ref ModEvents.SGameUpdateData _d) => HeldTick());
            }
            // round MP4 (part Q): the quest ops' and journal reports' session state too (and their client tick, hooked once) -
            // ModApi calls this at world start and shutdown, and ModApi.cs is not this part's file
            ScQuestNet.Reset();
        }

        /// <summary>Client: the server's last answer for this NPC this session, or null.</summary>
        public static ScTalkFacts Facts(int npcId)
        {
            ScTalkFacts f;
            return facts.TryGetValue(npcId, out f) ? f : null;
        }

        /// <summary>Client: the facts for the NPC a requirement asks about (the dialog's respondent, else the CurrentNPC cvar).</summary>
        public static ScTalkFacts FactsFor(EntityPlayer player, EntityNPC talkingTo)
        {
            if (talkingTo != null) return Facts(talkingTo.entityId);
            EntityAlive npc = ScFavours.CurrentNpc(player);
            return npc != null ? Facts(npc.entityId) : null;
        }

        /// <summary>
        /// Client: an action's answer changed the favour (started, done) - the facts follow at once (the copy within a second),
        /// and the talk still open with them is evaluated again ("Actually, I've already cleared that." leads to a statement whose
        /// join row needs the favour done: its rows were evaluated before the answer came).
        /// </summary>
        public static void Note(int npcId, bool started, bool done)
        {
            ScTalkFacts f = Facts(npcId);
            if (f == null) return;
            if (started) f.Started = true;
            if (done) f.Done = true;
            try { Refresh(npcId); }
            catch (Exception e) { ScLog.Once("talk-note", e); }
        }

        /// <summary>
        /// Client: this NPC's prospect as the dialog sees it - the copy's (ScRegistry.GetProspect) with the facts' Started/Done,
        /// or the facts' alone while the copy has not caught up with a roll. Always a NEW object (the copy is never changed);
        /// null when there is none, or they are a member now.
        /// </summary>
        public static Prospect View(int npcId)
        {
            if (ScRegistry.GetMember(npcId) != null) return null;
            Prospect mp = ScRegistry.GetProspect(npcId);
            ScTalkFacts f = Facts(npcId);
            if (mp == null && (f == null || f.Favour == (int)FavourKind.None)) return null;
            var v = new Prospect { Id = npcId };
            if (mp != null)
            {
                v.Id = mp.Id;
                v.Class = mp.Class;
                v.Name = mp.Name;
                v.Favour = mp.Favour;
                v.PoiPos = mp.PoiPos;
                v.PoiName = mp.PoiName;
                v.PoiWidth = mp.PoiWidth;
                v.PoiDepth = mp.PoiDepth;
                v.ItemCategory = mp.ItemCategory;
                v.ItemCount = mp.ItemCount;
                v.Started = mp.Started;
                v.Done = mp.Done;
                v.NpcPos = mp.NpcPos;
                v.EncPersonId = mp.EncPersonId;
            }
            else
            {
                v.Name = f.Name ?? "";
                v.Favour = (FavourKind)f.Favour;
                v.PoiPos = f.PoiPos;
                v.PoiName = f.PoiName ?? "";
                v.PoiWidth = f.PoiWidth;
                v.PoiDepth = f.PoiDepth;
                v.ItemCategory = (Resource)Math.Max(0, Math.Min(ScNames.ResourceCount - 1, f.ItemCategory));
                v.ItemCount = f.ItemCount;
                v.EncPersonId = f.EncPerson;
            }
            if (f != null && f.Favour == (int)v.Favour)
            {
                v.Started |= f.Started;
                v.Done |= f.Done;
            }
            return v;
        }

        /// <summary>
        /// Client: ScCanRecruit - the server's verdict at the talk's open, unless the copy says they joined or the roster filled
        /// since. No facts yet: false (the rows stay hidden, as before round MP2). The server checks it all again on the join.
        /// </summary>
        public static bool CanRecruit(EntityAlive npc)
        {
            if (npc == null) return false;
            ScTalkFacts f = Facts(npc.entityId);
            if (f == null || !f.Recruit) return false;
            if (ScRegistry.GetMember(npc.entityId) != null) return false;
            return ScRegistry.Members.Count < ScConfig.MaxMembers;
        }

        /// <summary>
        /// Client: ScHasHome - the server's EnsureHome for this player at the talk's open; without facts, the copy's home and
        /// whether its land claim is in this machine's player list (a read: EnsureHome itself may adopt a claim).
        /// </summary>
        public static bool HasHome(EntityPlayer player)
        {
            EntityAlive npc = ScFavours.CurrentNpc(player);
            ScTalkFacts f = npc != null ? Facts(npc.entityId) : null;
            if (f != null) return f.HasHome;
            Community c = ScRegistry.Community;
            if (c == null || !c.HasHome) return false;
            PersistentPlayerList pp = GameManager.Instance != null ? GameManager.Instance.persistentPlayers : null;
            return pp == null || pp.m_lpBlockMap == null || pp.m_lpBlockMap.ContainsKey(c.Home);
        }

        /// <summary>Client (the postfix below): a talk opened. Sends Open for an SCore NPC that is no trader; notes the statement.</summary>
        internal static void OnTalkOpened(XUiC_DialogWindowGroup g)
        {
            try
            {
                if (ScCommunity.IsHost || g == null) return;          // the server's requirements read the registry
                XUi xui = g.xui;
                EntityNPC npc = xui != null && xui.Dialog != null ? xui.Dialog.Respondent : null;
                EntityPlayerLocal lp = xui != null && xui.playerUI != null ? xui.playerUI.entityPlayer : null;
                if (npc == null || lp == null) return;
                openNpc = npc.entityId;
                // the statement it opened on, only when that is the first one the phases pick (a fresh talk): a talk reopened at a
                // return statement (after the trade window, the hold-E ring's jump into a page) is never re-picked
                Dialog d = g.CurrentDialog;
                DialogStatement cur = d != null ? d.CurrentStatement : null;
                openedOn = cur != null && d.ChildDialog == null && ReferenceEquals(cur, d.GetFirstStatment(lp)) ? cur : null;
                if (!ScNpc.IsFramework(npc) || ScTrust.IsTrader(npc)) return;
                if (!ScNet.IsClient || ScNet.Cm == null || !ScNet.ServerMatches || !ScNet.ServerKnows<NetPackageScTalkContext>()) return;
                bool journalDone = ScQuests.FavourQuestDone(lp, npc.entityId);
                // round MP2 (review members MINOR 8): an earlier talk's facts never drive this one's rows (a favour done and rolled
                // again, a recruit verdict that changed) - the rows wait for this talk's answer, as on a first talk
                facts.Remove(npc.entityId);
                ScNet.Cm.SendToServer(NetPackageManager.GetPackage<NetPackageScTalkContext>().SetupOpen(npc.entityId, journalDone));
                sent++;
            }
            catch (Exception e)
            {
                ScLog.Once("talk-open", e);
            }
        }

        /// <summary>
        /// Client (NetPackageScTalkContext.ProcessPackage, main thread): the server's facts. Kept; when the talk with that NPC is
        /// still open, its first statement is picked again if it still shows the one it opened on, and its rows evaluated again.
        /// </summary>
        internal static void OnFacts(ScTalkFacts f)
        {
            try
            {
                if (f == null || ScCommunity.IsHost || ScCommunity.World == null) return;
                f.At = Time.realtimeSinceStartup;
                facts[f.Npc] = f;
                answered++;
                if (ScConfig.Verbose)
                    ScLog.Verbose("talk context for " + f.Npc + ": recruit " + f.Recruit + (f.RecruitWhy.Length > 0 ? " (" + f.RecruitWhy + ")" : "") + ", home " + f.HasHome
                                  + ", favour " + (FavourKind)f.Favour + (f.Started ? " started" : "") + (f.Done ? " done" : "") + (f.PoiCleared ? ", POI clear" : "")
                                  + (f.Band > 0 ? ", band " + f.Band + " come " + f.BandCome : "") + (f.Linked ? ", linked to you for the talk" : "")
                                  + (f.EncFlags != 0 ? ", enclave " + f.EncId + " (flags " + f.EncFlags + ", " + f.EncStanding + (f.EncReqState.Length > 0 ? ", request " + f.EncReqState : "")
                                     + (f.EncSearch > 0 ? ", missing survivor " + f.EncSearch : "") + ")" : ""));      // round MP5 (part E)
                NoteLink(f);            // round mt1 (part H): before the rows are evaluated (it drops this machine's leader cache for her)
                Refresh(f.Npc);
                // round MP5 (part E): an enclave line filled before these facts came (a talk reopened at a statement) is filled again
                if (f.EncFlags != 0 && ScEncTalk.TakeUnfilled(f.Npc)) RefillLine(f.Npc, "{");
            }
            catch (Exception e)
            {
                ScLog.Once("talk-facts", e);
            }
        }

        /// <summary>The open talk with this NPC, re-evaluated (the phase pick only while it still shows its first statement).</summary>
        private static void Refresh(int npcId)
        {
            EntityPlayerLocal lp = ScCommunity.LocalPlayer;
            LocalPlayerUI ui = lp != null ? lp.PlayerUI : null;
            XUi xui = ui != null ? ui.xui : null;
            if (xui == null || xui.Dialog == null || xui.Dialog.Respondent == null || xui.Dialog.Respondent.entityId != npcId) return;
            if (ui.windowManager == null || !ui.windowManager.IsWindowOpen("dialog")) return;
            XUiC_DialogWindowGroup g = xui.Dialog.DialogWindowGroup;
            Dialog d = g != null ? g.CurrentDialog : null;
            if (d == null || d.CurrentStatement == null) return;          // RefreshDialog would close a talk with no statement
            if (npcId == openNpc && openedOn != null && d.ChildDialog == null && ReferenceEquals(d.CurrentStatement, openedOn))
            {
                DialogStatement first = d.GetFirstStatment(lp);
                if (first != null && !ReferenceEquals(first, openedOn))
                {
                    d.CurrentStatement = first;
                    openedOn = first;
                    ScLog.Verbose("talk context: the talk with " + npcId + " starts at " + first.ID + " now");
                }
            }
            g.RefreshDialog();
        }

        /// <summary>
        /// Round MP5 (part E), client: the server's answer to an enclave action came after its reply statement was shown (the line
        /// panel fills a statement's tokens once, when it is new: XUiC_ScDialogLine) - when the talk with this NPC still shows a
        /// statement whose line holds one of the given tokens ({reqreply}, {giftreply}), it is swapped for a twin (the same id,
        /// text, owner and rows: vanilla makes such statements itself for a quest's offer, Dialog.SelectResponse), so the line
        /// panel fills it again with the answer and its voice says it. The dialog's own statement is never written.
        /// </summary>
        internal static void RefillLine(int npcId, params string[] tokens)
        {
            try
            {
                EntityPlayerLocal lp = ScCommunity.LocalPlayer;
                LocalPlayerUI ui = lp != null ? lp.PlayerUI : null;
                XUi xui = ui != null ? ui.xui : null;
                if (xui == null || xui.Dialog == null || xui.Dialog.Respondent == null || xui.Dialog.Respondent.entityId != npcId) return;
                if (ui.windowManager == null || !ui.windowManager.IsWindowOpen("dialog")) return;
                XUiC_DialogWindowGroup g = xui.Dialog.DialogWindowGroup;
                Dialog d = g != null ? g.CurrentDialog : null;
                DialogStatement st = d != null ? d.CurrentStatement : null;
                if (st == null || string.IsNullOrEmpty(st.Text)) return;
                bool holds = false;
                foreach (string t in tokens) if (!string.IsNullOrEmpty(t) && st.Text.IndexOf(t, StringComparison.Ordinal) >= 0) holds = true;
                if (!holds) return;
                var twin = new DialogStatement(st.ID) { Text = st.Text, OwnerDialog = st.OwnerDialog };
                twin.NextStatementID = st.NextStatementID;
                twin.ResponseEntries = st.ResponseEntries;
                twin.Actions = st.Actions;
                if (ReferenceEquals(openedOn, st)) openedOn = twin;
                d.CurrentStatement = twin;
                g.RefreshDialog();
            }
            catch (Exception e)
            {
                ScLog.Once("talk-refill", e);
            }
        }

        /// <summary>Client (ProcessPackage): the server says this favour is over - its quest closes here, if this journal has it.</summary>
        internal static void OnFavourClose(int prospectId, bool completed)
        {
            try
            {
                EntityPlayerLocal lp = ScCommunity.LocalPlayer;
                if (lp == null || ScCommunity.IsHost) return;
                if (ScQuests.FindFavour(lp, prospectId) == null) return;
                ScQuests.CloseFavour(lp, prospectId, completed);
                closes++;
                ScLog.Out("favour quest of " + prospectId + " closed (" + (completed ? "completed" : "removed") + ") - the server ended that favour");
            }
            catch (Exception e)
            {
                ScLog.Once("talk-favourclose", e);
            }
        }

        // ---- round mt1 (part H): the client's side of a talk hold the server keeps for it ----

        /// <summary>
        /// Client (OnFacts): the facts say whether the server linked this member to us for the talk. Linked: her heartbeat starts
        /// (HeldTick), and this machine's SCore leader cache for her is dropped so the rows' Leader requirement reads her cvars
        /// afresh. Not linked (another player holds her, or she cannot be held): nothing to keep.
        /// </summary>
        private static void NoteLink(ScTalkFacts f)
        {
            if (!f.Linked)
            {
                // round mt1 (review fix H-m3): a link this machine kept for her is over - its leftovers are swept, as at a close
                if (held.Remove(f.Npc)) hireSweeps.Add(f.Npc);
                return;
            }
            float now = Time.realtimeSinceStartup;
            HeldLink k;
            if (!held.TryGetValue(f.Npc, out k))
            {
                k = new HeldLink();
                held[f.Npc] = k;
            }
            k.Since = now;
            k.LastOpen = now;
            k.NextBeat = now;                         // the first beat at the next tick
            k.OwnerSeen = OwnerIs(f.Npc, ScCommunity.LocalPlayer);
            hireSweeps.Remove(f.Npc);
            SphereCache.LeaderCache.Remove(f.Npc);    // this machine's cache only (a client's copy is never written)
        }

        /// <summary>Client: her copy's Owner cvar names this player (the server's force-sent link has landed here).</summary>
        private static bool OwnerIs(int npcId, EntityPlayerLocal lp)
        {
            World w = ScCommunity.World;
            EntityAlive npc = w != null && lp != null ? w.GetEntity(npcId) as EntityAlive : null;
            return npc != null && npc.Buffs != null && (int)npc.Buffs.GetCustomVar("Owner") == lp.entityId;
        }

        /// <summary>
        /// Round mt1 (part H), client, every frame (GameUpdate), at most every HeldTickSeconds: for each member the server linked
        /// to us for a talk -
        /// - her Owner cvar lands after the facts (their arrival order is not guaranteed): the open talk's rows are evaluated again,
        ///   within OwnerWaitSeconds of the facts;
        /// - the server ended the link (her Owner no longer names us after it did: its cap, us out of range, her shift's end): no
        ///   more word from here;
        /// - while ScWork.TalkOpen holds on this machine's windows, and for ScWork.TalkGraceSeconds after (the hold-E ring's
        ///   close-and-reopen hop), a Held beat every BeatSeconds; then TalkClosed, once.
        /// Nothing on the server or in single player (no facts there).
        /// </summary>
        private static void HeldTick()
        {
            try
            {
                // round mt1 (review fix H-m3): the owner-link scan (ScanOwnerLinks) runs on its own clock, so its time is a third
                // reason to go on; on the host (single player too) that clock only brings it back here every few seconds
                if (held.Count == 0 && hireSweeps.Count == 0 && Time.realtimeSinceStartup < nextMarkScan) return;
                if (ScCommunity.IsHost) { held.Clear(); hireSweeps.Clear(); nextMarkScan = Time.realtimeSinceStartup + 5f; return; }
                float now = Time.realtimeSinceStartup;
                if (now < nextHeldTick) return;
                nextHeldTick = now + HeldTickSeconds;
                World w = ScCommunity.World;
                EntityPlayerLocal lp = ScCommunity.LocalPlayer;
                if (w == null || lp == null)
                {
                    held.Clear();
                    hireSweeps.Clear();
                    return;
                }
#if SC_V34
                if (now >= nextMarkScan)
                {
                    nextMarkScan = now + MarkScanSeconds;
                    ScanOwnerLinks(w, lp);
                }
                bool send = ScNet.IsClient && ScNet.Cm != null && ScNet.ServerMatches && ScNet.ServerKnows<NetPackageScTalkContext>();
                heldScratch.Clear();
                heldScratch.AddRange(held.Keys);
                foreach (int id in heldScratch)
                {
                    HeldLink k = held[id];
                    EntityAlive npc = w.GetEntity(id) as EntityAlive;
                    bool mine = npc != null && npc.Buffs != null && (int)npc.Buffs.GetCustomVar("Owner") == lp.entityId;
                    if (mine && !k.OwnerSeen)
                    {
                        k.OwnerSeen = true;
                        if (now - k.Since <= OwnerWaitSeconds)
                        {
                            SphereCache.LeaderCache.Remove(id);
                            ownerRefreshes++;
                            ScLog.Verbose("talk context: " + id + "'s link to you landed after the facts - the talk's rows are evaluated again");
                            Refresh(id);
                        }
                    }
                    else if (!mine && k.OwnerSeen)
                    {
                        held.Remove(id);                   // the server ended it: its Detach sent the 0
                        hireSweeps.Add(id);
                        ScLog.Verbose("talk context: the server ended " + id + "'s talk link to you");
                        SphereCache.LeaderCache.Remove(id);
                        if (npc != null)
                        {
                            // round mt1 (review fix H-m1): her windows here close, as the host's do at its own cap (with no link every
                            // request of theirs is refused); with the talk itself still open, the server is asked once to link her again
                            ScWork.CloseTalkWindows(npc);
                            Relink(npc, lp, now, send);
                        }
                        Refresh(id);                       // a talk still open with her shows her rows as they now are
                        continue;
                    }
                    bool dialog = false;
                    if (npc != null && ScWork.TalkOpen(npc, out dialog)) k.LastOpen = now;
                    if (npc == null || now - k.LastOpen >= ScWork.TalkGraceSeconds)
                    {
                        if (send)
                        {
                            ScNet.Cm.SendToServer(NetPackageManager.GetPackage<NetPackageScTalkContext>().SetupTalkClosed(id));
                            closedSent++;
                        }
                        held.Remove(id);
                        hireSweeps.Add(id);
                        continue;
                    }
                    if (send && now >= k.NextBeat)
                    {
                        k.NextBeat = now + BeatSeconds;
                        ScNet.Cm.SendToServer(NetPackageManager.GetPackage<NetPackageScTalkContext>().SetupHeld(id, dialog));
                        beatsSent++;
                    }
                }
#else
                held.Clear();
#endif
                SweepHireMarks(w, lp);
            }
            catch (Exception e)
            {
                ScLog.Once("talk-held-tick", e);
            }
        }

        /// <summary>
        /// Round mt1 (part H), client: SCore's leader component runs on this machine's copy too, and while her Owner cvar named
        /// this player it stamped hired_&lt;id&gt; on the local player and drew her companion marker. The server's Detach clears
        /// only the server's side, so once her copy names this player neither as owner nor as leader (the 0 has landed), this
        /// machine's leader cache, hire mark and marker for her go. While she still names us (the 0 on its way, a work shift her
        /// first order made, a follow) she is looked at again each tick; a copy this machine no longer has is dropped. Local only:
        /// nothing is sent. Round mt1 (review fix H-m3): a copy this machine no longer has takes its mark with it at once (but a
        /// follower's), and members linked to us by Call to work join the sweep too (ScanOwnerLinks).
        /// </summary>
        private static void SweepHireMarks(World w, EntityPlayerLocal lp)
        {
            if (hireSweeps.Count == 0) return;
            heldScratch.Clear();
            heldScratch.AddRange(hireSweeps);
            foreach (int id in heldScratch)
            {
                EntityAlive npc = w.GetEntity(id) as EntityAlive;
                if (npc == null || npc.Buffs == null)
                {
                    // round mt1 (review fix H-m3): her copy is gone from this machine (out of view), so nothing here will ever show her
                    // link end: the mark goes now (SCore's leader component stamps it again if she loads still linked to us). A
                    // follower keeps hers - her follow's own end takes it
                    Member fm = ScRegistry.GetMember(id);
                    if (fm == null || (fm.Status != MemberStatus.Following && fm.Status != MemberStatus.PendingStation))
                    {
                        SphereCache.LeaderCache.Remove(id);
                        RemoveHireMark(lp, id);
                    }
                    hireSweeps.Remove(id);
                    continue;
                }
                if ((int)npc.Buffs.GetCustomVar("Owner") == lp.entityId || (int)npc.Buffs.GetCustomVar("Leader") == lp.entityId) continue;
                SphereCache.LeaderCache.Remove(id);
                RemoveHireMark(lp, id);
#if SC_V34
                ScWork.DropMarker(npc);
#endif
                hireSweeps.Remove(id);
            }
        }

        /// <summary>Round mt1 (part H): this machine's hire mark for her on the local player (SCore's hired_&lt;id&gt;), if there is one.</summary>
        private static void RemoveHireMark(EntityPlayerLocal lp, int id)
        {
            string mark = "hired_" + id;
            if (lp != null && lp.Buffs != null && lp.Buffs.HasCustomVar(mark)) lp.Buffs.RemoveCustomVar(mark);
        }

        /// <summary>
        /// Round mt1 (review fix H-m3), client, every MarkScanSeconds: a resident whose copy names this player as her owner (not
        /// as her leader: a follower's mark is her follow's) is linked to us by some path - a talk (also kept in held), Call to
        /// work on the Community tab (ScWork.Start), a shift her talk became - so she joins the sweep, which takes this machine's
        /// hire mark off once she names us no more. Before, only members linked through a talk were swept, and a Call to work's
        /// mark stayed (and went back to the server's copy of this player with its own cvar sync).
        /// </summary>
        private static void ScanOwnerLinks(World w, EntityPlayerLocal lp)
        {
            foreach (Member mm in ScRegistry.Members.Values)
            {
                if (mm == null || mm.Status != MemberStatus.Resident || held.ContainsKey(mm.Id) || hireSweeps.Contains(mm.Id)) continue;
                EntityAlive npc = w.GetEntity(mm.Id) as EntityAlive;
                if (npc == null || npc.Buffs == null) continue;
                if ((int)npc.Buffs.GetCustomVar("Owner") == lp.entityId && (int)npc.Buffs.GetCustomVar("Leader") != lp.entityId) hireSweeps.Add(mm.Id);
            }
        }

#if SC_V34
        /// <summary>
        /// Round mt1 (review fix H-m1), client: the server ended our link to her while the talk with her is still open here (its
        /// cap, a lag stall past its grace, another player took her over, a verb) - one Open, at most every RelinkSeconds per
        /// member, so the server can link her again if she is free (OnTalkOpen -> ScWork.HoldForRemote: CanHold's rules; held by
        /// someone else: no link, and no "busy" tell right after a hand-over). The rows follow the facts it answers with.
        /// </summary>
        private static void Relink(EntityAlive npc, EntityPlayerLocal lp, float now, bool send)
        {
            bool dialog;
            if (!send || npc == null || lp == null || !ScWork.TalkOpen(npc, out dialog) || !dialog) return;
            float last;
            if (relinkAt.TryGetValue(npc.entityId, out last) && now - last < RelinkSeconds) return;
            relinkAt[npc.entityId] = now;
            bool journalDone = ScQuests.FavourQuestDone(lp, npc.entityId);
            ScNet.Cm.SendToServer(NetPackageManager.GetPackage<NetPackageScTalkContext>().SetupOpen(npc.entityId, journalDone));
            sent++;
            relinks++;
            ScLog.Verbose("talk context: the talk with " + npc.entityId + " is still open - the server is asked to link her again");
        }
#endif

        // ---- the dialog actions' answers (every machine: on the host the callbacks run inline) ----

        /// <summary>A talk action's tooltip: a refusal's text always, a success's only where the old row said one.</summary>
        internal static void Tell(EntityPlayer player, ScActionReply r, bool onSuccess)
        {
            if (player == null || r == null || (r.Ok && !onSuccess)) return;
            string text = r.Text;
            if (text.Length > 0) ScCommunity.Tell(player, text, r.Sound);
        }

        /// <summary>A reply's favour-quest close (ScActionsMembers.HintCloseFavour): on this machine's own journal.</summary>
        internal static void ApplyQuestHint(EntityPlayer player, ScActionReply r)
        {
            if (player == null || r == null || !r.HasHint(ScActionsMembers.HintCloseFavour)) return;
            ScQuests.CloseFavour(player, r.HintInt(ScActionsMembers.HintCloseFavour), r.HintInt(ScActionsMembers.HintCloseDone) != 0);
        }

        /// <summary>Client: a supplies hand-over starts for this NPC (its row's ScJoin waits for it: JoinAfterHandOver).</summary>
        internal static void BeginHandOver(int npcId) { handOver[npcId] = Time.realtimeSinceStartup; }

        /// <summary>Client: a hand-over for this NPC is in flight (begun less than HandOverSeconds ago).</summary>
        internal static bool HandOverPending(int npcId)
        {
            float at;
            if (!handOver.TryGetValue(npcId, out at)) return false;
            if (Time.realtimeSinceStartup - at <= HandOverSeconds) return true;
            handOver.Remove(npcId);
            joinAfter.Remove(npcId);
            encAfter.Remove(npcId);           // round MP5 (part E)
            return false;
        }

        /// <summary>Round MP5 (part E), client: an enclave row's action after that row's hand-over still in flight - run once it is through.</summary>
        internal static void EncAfterHandOver(int npcId, Action then)
        {
            if (then != null) encAfter[npcId] = then;
        }

        /// <summary>A client's ScJoin in the same row as a hand-over still in flight: sent once the hand-over is through.</summary>
        internal static void JoinAfterHandOver(int npcId, EntityPlayer player, bool journalDone)
        {
            joinAfter[npcId] = new PendingJoin { Player = player, JournalDone = journalDone };
        }

        /// <summary>The hand-over ended: done (ok) sends the join its row asked for; refused drops it (the refusal was told).</summary>
        internal static void EndHandOver(int npcId, bool ok)
        {
            handOver.Remove(npcId);
            // round MP5 (part E): an enclave row's action of the same row (its recruit) goes now, or is dropped with a refusal
            Action then;
            if (encAfter.TryGetValue(npcId, out then))
            {
                encAfter.Remove(npcId);
                if (ok)
                {
                    try { then(); }
                    catch (Exception e) { ScLog.Once("talk-enc-after", e); }
                }
            }
            PendingJoin j;
            if (!joinAfter.TryGetValue(npcId, out j)) return;
            joinAfter.Remove(npcId);
            if (ok) RequestJoin(npcId, j.Player, j.JournalDone);
        }

        /// <summary>member.join for the talk (ScJoin): the answer's tooltip on a refusal, the favour quest's close from its hint.</summary>
        internal static void RequestJoin(int npcId, EntityPlayer player, bool journalDone)
        {
            ScActions.Request("member.join", r =>
            {
                ApplyQuestHint(player, r);
                Tell(player, r, false);
            }, npcId, journalDone ? 1 : 0);
        }

        /// <summary>
        /// Client: takes count items of a category from the player (backpack first, as ScCategories.RemoveFromPlayer) and records
        /// exactly what left (vanilla's removed-items list), so a hand-over the server refuses at its confirm gives them back.
        /// Returns how many left.
        /// </summary>
        internal static int TakeItems(EntityPlayerLocal player, Resource r, int count, List<ItemStack> removed)
        {
            int n = 0;
            try
            {
                if (player == null || count <= 0) return 0;
                XUiM_PlayerInventory inv = player.PlayerUI != null && player.PlayerUI.xui != null ? player.PlayerUI.xui.PlayerInventory : null;
                if (inv == null) return 0;
                var take = new List<ItemStack>();
                ItemStack[][] sources = { player.bag != null ? (ItemStack[])player.bag.ItemGrid.items.Clone() : null,
                                          player.inventory != null ? (ItemStack[])player.inventory.ItemGrid.items.Clone() : null };
                for (int src = 0; src < sources.Length && n < count; src++)
                {
                    ItemStack[] slots = sources[src];
                    if (slots == null) continue;
                    for (int i = 0; i < slots.Length && n < count; i++)
                    {
                        ItemStack s = slots[i];
                        if (s == null || s.IsEmpty() || s.itemValue == null || !ScCategories.Is(s.itemValue.ItemClass, r)) continue;
                        int k = Math.Min(s.count, count - n);
                        take.Add(new ItemStack(s.itemValue.Clone(), k));
                        n += k;
                    }
                }
                if (take.Count == 0) return 0;
                int before = ScCategories.CountInPlayer(player, r);
                ScCraftFromBase.SuppressContainers(() => inv.RemoveItems(take, 1, removed));   // round MP4 (review cfs1 m5): the bag and toolbelt alone
                return Math.Max(0, before - ScCategories.CountInPlayer(player, r));
            }
            catch (Exception e)
            {
                ScLog.Once("talk-take-items", e);
                return 0;
            }
        }

        /// <summary>Client: the items back into the player's bag (dropped at their feet when it is full).</summary>
        internal static void Refund(EntityPlayerLocal player, List<ItemStack> stacks)
        {
            if (player == null || stacks == null) return;
            XUiM_PlayerInventory inv = player.PlayerUI != null && player.PlayerUI.xui != null ? player.PlayerUI.xui.PlayerInventory : null;
            int back = 0;
            foreach (ItemStack s in stacks)
            {
                try
                {
                    if (s == null || s.IsEmpty()) continue;
                    ItemStack c = s.Clone();
                    back += c.count;
                    if (inv != null && inv.AddItem(c)) continue;
                    if (c.count > 0 && GameManager.Instance != null) GameManager.Instance.ItemDropServer(c, player.GetPosition(), Vector3.zero, player.entityId, 60f, false);
                }
                catch (Exception e)
                {
                    ScLog.Once("talk-refund", e);
                }
            }
            if (back > 0) ScLog.Out("supplies favour: the server refused the hand-over - " + back + " item(s) given back");
        }

        // ------------------------------------------------------------------ the server ----

        /// <summary>
        /// Server (ProcessPackage): a client opened a talk with this NPC. A matched player within TalkRange of a loaded SCore NPC
        /// gets the facts; anything else is ignored (the client's rows stay hidden).
        /// </summary>
        internal static void OnTalkOpen(ClientInfo sender, int npcId, int flags)
        {
            try
            {
                if (sender == null || !ScCommunity.IsHost || !ScRegistry.Loaded) return;
                EntityPlayer p = ScPlayers.ByEntity(sender.entityId);
                if (p == null || !ScNet.ClientMatches(p)) return;
                World w = ScCommunity.World;
                EntityAlive npc = w != null ? ScNpc.As(w.GetEntity(npcId)) : null;
                if (npc == null || npc.IsDead()) return;
                if ((npc.position - p.position).sqrMagnitude > TalkRange * TalkRange) return;
                // round mt1 (part H): the talk hold for this remote talker (Patches_Craft's postfix does it for the host's own), started
                // BEFORE the facts are built and sent - its force-sent Owner cvar is queued to the client first. The first holder keeps
                // her: another player's hold or shift means no link for this one (and a tell)
                bool linked = false;
#if SC_V34
                Member m = ScRegistry.GetMember(npc.entityId);
                if (m != null) linked = ScWork.HoldForRemote(m, npc, p);
#endif
                ScTalkFacts f = Build(p, npc, (flags & 1) != 0);
                f.Linked = linked;
                sender.SendPackage(NetPackageManager.GetPackage<NetPackageScTalkContext>().SetupFacts(f));
            }
            catch (Exception e)
            {
                ScLog.Once("talk-open-server", e);
            }
        }

        /// <summary>
        /// Round mt1 (part H), server (ProcessPackage): a remote talker's client says its talk with this member is still open
        /// (dialog: the talk window with her). Only the player whose hold it is keeps it (ScWork.RemoteBeat checks); range and the
        /// rest are TalkTick's.
        /// </summary>
        internal static void OnTalkHeld(ClientInfo sender, int npcId, int flags)
        {
            try
            {
                EntityPlayer p;
                Member m;
                if (!HoldSender(sender, npcId, out p, out m)) return;
#if SC_V34
                ScWork.RemoteBeat(m, p, (flags & 1) != 0);
#endif
            }
            catch (Exception e)
            {
                ScLog.Once("talk-held-server", e);
            }
        }

        /// <summary>Round mt1 (part H), server (ProcessPackage): a remote talker's client says its talk with this member is over.</summary>
        internal static void OnTalkClosed(ClientInfo sender, int npcId)
        {
            try
            {
                EntityPlayer p;
                Member m;
                if (!HoldSender(sender, npcId, out p, out m)) return;
#if SC_V34
                ScWork.RemoteClosed(m, p);
#endif
            }
            catch (Exception e)
            {
                ScLog.Once("talk-closed-server", e);
            }
        }

        /// <summary>Round mt1 (part H), server: a matched remote player and the member a Held / TalkClosed names.</summary>
        private static bool HoldSender(ClientInfo sender, int npcId, out EntityPlayer p, out Member m)
        {
            p = null;
            m = null;
            if (sender == null || !ScCommunity.IsHost || !ScRegistry.Loaded) return false;
            p = ScPlayers.ByEntity(sender.entityId);
            if (p == null || p is EntityPlayerLocal || !ScNet.ClientMatches(p)) return false;
            m = ScRegistry.GetMember(npcId);
            return m != null;
        }

        /// <summary>
        /// Server: the facts for player p talking to npc - and the side effects the host's requirements have when they are
        /// evaluated (E4): the profile, the favour's roll (only once ScCanRecruit and ScHasHome would pass, as the XML's
        /// requirement order has it), and a POI favour marked done on the client's journal verdict (ScFavourDone's CheckDone).
        /// </summary>
        internal static ScTalkFacts Build(EntityPlayer p, EntityAlive npc, bool journalDone)
        {
            var f = new ScTalkFacts { Npc = npc.entityId };
            ScProfile.Ensure(npc);                                        // the talk card's profile (%-cvars: they reach the client)
            bool hadHome = ScRegistry.Community.HasHome;
            string why;
            f.Recruit = ScCommunity.CanRecruit(npc, p, out why);
            f.RecruitWhy = why ?? "";
            // round MP2 (review members MINOR 3): read, never adopted - a remote player's talk must not make their claim the home and
            // them the owner (with SharedOwnerOnly that would bypass home.set's Owner access). The host's own player never sends one
            f.HasHome = ScZone.EnsureHomeFor(p);
            bool changed = hadHome != ScRegistry.Community.HasHome;
            // round MP5 (part E): one of an enclave's people - their rows' verdicts and their line's tokens (ScEncFacts.Fill), before
            // the favour below: the favour row's roll (ScEnclaveDeals.EnsureFavour) is theirs, so the prospect read next sees it
            ScEncFacts.Fill(f, p, npc);
            // round MP6 (fix R-M2): the talker's community's own favour (the package's scope is the sender's) - GetProspect fell back
            // to another community's, so a second community never rolled its own and could recruit on the first one's finished favour
            Prospect pr = ScRegistry.OwnProspect(npc.entityId);
            if (pr == null && f.Recruit && f.HasHome) pr = ScFavours.Ensure(npc, p);     // E4: the roll is the server's (it saves)
            if (pr != null && !pr.Done && journalDone && pr.Favour == FavourKind.Poi)
            {
                ScFavours.MarkDone(npc, pr);
                changed = true;
                ScLog.Out("favour done: " + pr.PoiName + " for " + pr.Name + " - " + p.PlayerDisplayName + "'s quest is past the clearing");
            }
            if (pr != null)
            {
                f.Favour = (int)pr.Favour;
                f.Started = pr.Started;
                f.Done = pr.Done;
                f.Name = pr.Name ?? "";
                f.PoiPos = pr.PoiPos;
                f.PoiWidth = pr.PoiWidth;
                f.PoiDepth = pr.PoiDepth;
                f.PoiName = pr.PoiName ?? "";
                f.ItemCategory = (int)pr.ItemCategory;
                f.ItemCount = pr.ItemCount;
                f.EncPerson = pr.EncPersonId;
                if (pr.Favour == FavourKind.Poi && !pr.Done)
                {
                    string poiWhy;
                    f.PoiCleared = ScFavours.PoiReadsCleared(pr, out poiWhy);
                    f.PoiWhy = poiWhy ?? "";
                }
            }
            ScBands.TalkFacts(npc, p, f);
            if (changed) ScRegistry.SaveAtomic();
            return f;
        }

        /// <summary>
        /// Round MP5 (part E), server: after an enclave action of a remote player, that player's facts for the NPC again (Build;
        /// no link - she is no member), sent BEFORE the action's answer: the reliable channel keeps order, so the client's rows
        /// read the new state (the request accepted, the standing moved) by the time its callback shows the reply. Nothing for the
        /// host's own player, a client without SC's protocol, or one who is a member now (a recruit: the copy follows).
        /// </summary>
        internal static void SendFactsAfter(EntityPlayer p, EntityAlive npc)
        {
            try
            {
                if (p == null || p is EntityPlayerLocal || npc == null || npc.IsDead() || !ScCommunity.IsHost || !ScRegistry.Loaded) return;
                if (!ScNet.ClientMatches(p) || ScRegistry.GetMember(npc.entityId) != null) return;
                ScTalkFacts f = Build(p, npc, false);
                ScNet.ToPlayer(p, () => NetPackageManager.GetPackage<NetPackageScTalkContext>().SetupFacts(f), null);
            }
            catch (Exception e)
            {
                ScLog.Once("talk-facts-after", e);
            }
        }

        /// <summary>
        /// Server: the favour of this prospect is over - every player who holds its quest but 'except' (the one whose own close
        /// already happened, or comes with their reply) has it closed: the host's own player here, a matched client through
        /// the quest op (round MP4: NetPackageScQuestOp, which replaced the talk context's Close here). Each client checks its own
        /// journal; one away closes it with its next journal report (ScQuestNet). Single player: nobody.
        /// </summary>
        public static void CloseFavourForOthers(int prospectId, bool completed, EntityPlayer except)
        {
            try
            {
                if (!ScCommunity.IsHost || prospectId == 0) return;
                List<EntityPlayer> all = ScPlayers.All;
                if (all.Count == 0) return;
                EntityPlayer[] to = all.ToArray();          // ToPlayer may re-enter code that walks the live list
                for (int i = 0; i < to.Length; i++)
                {
                    EntityPlayer pl = to[i];
                    if (pl == null || ReferenceEquals(pl, except)) continue;
                    if (pl is EntityPlayerLocal)
                    {
                        if (ScQuests.FindFavour(pl, prospectId) != null) ScQuests.CloseFavour(pl, prospectId, completed);
                        continue;
                    }
                    if (!ScNet.ClientMatches(pl)) continue;                       // a client without SC's protocol knows no such package
                    ScQuestNet.CloseFavourFor(pl, prospectId, completed);         // round MP4: the quest op (every journal close goes through it)
                }
            }
            catch (Exception e)
            {
                ScLog.Once("talk-close-others", e);
            }
        }

        /// <summary>sc state / the log: the session's counts.</summary>
        public static string Report()
        {
            return "talk context: " + sent + " sent, " + answered + " answered, " + facts.Count + " NPC(s) known, " + closes + " favour quest(s) closed by the server"
                   + "; talk links held here " + held.Count + ", beats sent " + beatsSent + ", closes sent " + closedSent + ", rows re-evaluated for a late link " + ownerRefreshes   // round mt1 (part H)
                   + ", re-asked after the server ended a link " + relinks + ", hire marks to sweep " + hireSweeps.Count      // round mt1 (review fixes H-m1, H-m3)
                   + "; " + ScEncTalk.Report();                                                                                // round MP5 (part E)
        }

        // ------------------------------------------------------------------ the hook ----

        /// <summary>A talk opened on this machine (vanilla's dialog window group; Patches_Craft's talk hold rides the same method).</summary>
        [HarmonyPatch(typeof(XUiC_DialogWindowGroup), nameof(XUiC_DialogWindowGroup.OnOpen))]
        public static class Patch_TalkOpen
        {
            [HarmonyPostfix]
            public static void Postfix(XUiC_DialogWindowGroup __instance)
            {
                OnTalkOpened(__instance);
            }
        }
    }
}
