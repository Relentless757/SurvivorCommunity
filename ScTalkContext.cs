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

        public ScTalkFacts Copy() { return (ScTalkFacts)MemberwiseClone(); }
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
        private static int sent, answered, closes;

        private struct PendingJoin
        {
            public EntityPlayer Player;
            public bool JournalDone;
        }

        /// <summary>Every machine, at world start and shutdown.</summary>
        public static void Reset()
        {
            facts.Clear();
            openNpc = -1;
            openedOn = null;
            handOver.Clear();
            joinAfter.Clear();
            sent = answered = closes = 0;
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
                                  + (f.Band > 0 ? ", band " + f.Band + " come " + f.BandCome : ""));
                Refresh(f.Npc);
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
            return false;
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
                ScTalkFacts f = Build(p, npc, (flags & 1) != 0);
                sender.SendPackage(NetPackageManager.GetPackage<NetPackageScTalkContext>().SetupFacts(f));
            }
            catch (Exception e)
            {
                ScLog.Once("talk-open-server", e);
            }
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
            Prospect pr = ScRegistry.GetProspect(npc.entityId);
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
            return "talk context: " + sent + " sent, " + answered + " answered, " + facts.Count + " NPC(s) known, " + closes + " favour quest(s) closed by the server";
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
