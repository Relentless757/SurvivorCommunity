using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round MP4 (part Q): what a client's journal holds, as it tells the server (NetPackageScJournalReport). Plain data.
    /// </summary>
    public sealed class ScJournalReport
    {
        public const byte KindPoiFavour = 0, KindSupplies = 1, KindRescue = 2, KindRequest = 3;
        /// <summary>Entry flags: the quest is in progress; a POI favour's quest is past its clearing (or completed).</summary>
        public const byte FlagInProgress = 1, FlagDone = 2;
        /// <summary>At most this many entries and POIs in one report (a journal holds a handful).</summary>
        public const int MaxEntries = 200, MaxPois = 64;

        public struct Entry
        {
            public byte Kind;
            /// <summary>A favour's giver (the prospect id), a rescue's member id, a request's id.</summary>
            public int Id;
            /// <summary>A favour of one of an enclave's people: the person (their prospect's key follows each new body); else 0.</summary>
            public int EncPerson;
            public byte Flags;

            public bool InProgress { get { return (Flags & FlagInProgress) != 0; } }
            public bool Done { get { return (Flags & FlagDone) != 0; } }
        }

        /// <summary>The NPC whose dialog the player has open (-1: none) - a POI favour is not marked done mid-talk (SyncCompleted's rule).</summary>
        public int Talking = -1;
        public readonly List<Entry> Entries = new List<Entry>();
        /// <summary>The POIs (block keys) every quest in progress leads to, vanilla's too - the pickers keep away from them.</summary>
        public readonly List<Vector3i> Pois = new List<Vector3i>();
        /// <summary>Server: the sender's entity id when it arrived.</summary>
        public int EntityId;

        public void Add(byte kind, int id, int encPerson, byte flags)
        {
            if (Entries.Count >= MaxEntries) return;
            Entries.Add(new Entry { Kind = kind, Id = id, EncPerson = encPerson, Flags = flags });
        }

        /// <summary>The report as one string: a client sends again only when it changed (or ReportSeconds passed).</summary>
        public string Digest()
        {
            var sb = new StringBuilder();
            sb.Append(Talking).Append('|');
            for (int i = 0; i < Entries.Count; i++)
                sb.Append(Entries[i].Kind).Append(':').Append(Entries[i].Id).Append(':').Append(Entries[i].EncPerson).Append(':').Append(Entries[i].Flags).Append(';');
            sb.Append('|');
            for (int i = 0; i < Pois.Count; i++) sb.Append(Pois[i].x).Append(',').Append(Pois[i].y).Append(',').Append(Pois[i].z).Append(';');
            return sb.ToString();
        }
    }

    /// <summary>
    /// Round MP4 (multiplayer, part Q): quests reach the right player's journal. A journal belongs to its own machine
    /// (QuestJournal.AddQuest is local, game:QuestJournal.cs:392-405), so the server tells a client what to do with its journal
    /// (NetPackageScQuestOp: start, close or re-key a quest - the client runs the same ScQuests functions on its own journal)
    /// and the client tells the server what its journal holds (NetPackageScJournalReport). The host's own player keeps its
    /// direct path (ScQuests on its own journal, exactly as before); single player has no clients and sends nothing.
    /// Who gets what (MP4_BRIEF, "Decisions"):
    /// - A favour quest is the taker's own (the dialog starts it on their machine). A favour that ends for everyone (a join, a
    ///   death, a grudge, an enclave person's end) closes every remote copy with an op (ScTalkContext.CloseFavourForOthers).
    /// - A rescue quest goes to EVERY community player online, each their own copy (RescueStarted). The first to complete it
    ///   (the server's ScRadio.Rescued) closes it for everyone, as does its failure (CloseRescueAll). A player who comes online
    ///   while it is still open gets it with their first report - derived from the open rescue (Member.RunDistress), sent once
    ///   per player per call.
    /// - An enclave request's quest goes to the player who accepted it (EnclaveRequest.AcceptedBy). Empty is the host's own
    ///   player: a save from before MP4, and every acceptance until MP5 opens the enclave dialogs to clients.
    /// The report (on a change, and every ReportSeconds) carries the client's SC quests and the POIs its quests lead to. The
    /// server answers it (OnReport): a POI favour whose quest is past its clearing is done (what SyncCompleted does for the
    /// host); a favour quest whose prospect is gone is closed, or re-keyed to its enclave person's new key; a rescue or request
    /// quest that ended while the player was away is closed; an open rescue, or a request they accepted, that their journal
    /// lacks is started. It also closes MP2's E6 gap: a POI favour no player holds any more is no longer "taken on"
    /// (Prospect.Started), so that building's vanilla clear quests re-arm again for remote players.
    /// Main thread only (the client's GameUpdate tick, ProcessPackage, the server's verbs).
    /// </summary>
    public static class ScQuestNet
    {
        /// <summary>A client sends its report when it changed, and at least this often (real seconds) all the same.</summary>
        private const float ReportSeconds = 30f;
        /// <summary>A client's op that found its journal not ready is tried this many seconds (one try a second).</summary>
        private const int PendingTries = 30;
        /// <summary>StartRequestFor's answer when the quest went to a client's own journal (no Quest on this machine).</summary>
        public const string SentToClient = "sent to the player's own journal";

        private static bool hooked;

        // ------------------------------------------------------------------ the client ----

        private static float nextClientTick;
        private static float nextReport;
        private static string lastDigest;

        /// <summary>A client's start op whose journal was not ready: tried again each second (PendingTries times).</summary>
        private sealed class PendingStart
        {
            public byte Kind;
            public int Id;
            public int[] Ints;
            public string[] Strs;
            public int Tries;
        }

        private static readonly List<PendingStart> pendingStarts = new List<PendingStart>();

        // ------------------------------------------------------------------ the server ----

        /// <summary>Each remote player's last report this session, by platform id (kept after they leave: E6 and the pickers).</summary>
        private static readonly Dictionary<string, ScJournalReport> reports = new Dictionary<string, ScJournalReport>(StringComparer.Ordinal);
        /// <summary>
        /// POI favours (prospect ids) a remote report held this session: only those can be found "held by nobody" (E6). Round MP6
        /// (fix N-m3): by (community, prospect id) - a wild survivor's prospect has the same id in every community courting them.
        /// </summary>
        private static readonly HashSet<long> favourEverHeld = new HashSet<long>();

        /// <summary>Round MP6 (fix N-m3): favourEverHeld's key for a community's prospect.</summary>
        private static long FavourKey(int cid, int prospectId) { return ((long)cid << 32) | (uint)prospectId; }
        /// <summary>The rescues (member ids) whose start went to each player this call - sent once, a quest removed by hand stays removed.</summary>
        private static readonly Dictionary<string, HashSet<int>> rescueSent = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        /// <summary>How each rescue ended this session (member id: completed), for a copy closed later (its holder was away).</summary>
        private static readonly Dictionary<int, bool> rescueOutcome = new Dictionary<int, bool>();
        /// <summary>A runner's member id before a re-key (ScRunners.Rekey) and after: an away player's copy follows on their report.</summary>
        private static readonly Dictionary<int, int> rescueRekeys = new Dictionary<int, int>();
        /// <summary>How each request ended this session (request id: completed), for a quest closed later (its holder was away).</summary>
        private static readonly Dictionary<int, bool> requestOutcome = new Dictionary<int, bool>();
        /// <summary>The requests each player's journal was started for, or shaped to the record, this session (once each).</summary>
        private static readonly Dictionary<string, HashSet<int>> requestStarted = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        private static readonly Dictionary<string, HashSet<int>> requestShaped = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);

        /// <summary>
        /// Every machine, at world start and shutdown (ScTalkContext.Reset): the session's state goes. The client's tick is hooked
        /// into ModEvents.GameUpdate here, once per process (ModApi.cs is not this part's file; the merge may move it there).
        /// </summary>
        public static void Reset()
        {
            nextClientTick = nextReport = 0f;
            lastDigest = null;
            pendingStarts.Clear();
            reports.Clear();
            favourEverHeld.Clear();
            rescueSent.Clear();
            rescueOutcome.Clear();
            rescueRekeys.Clear();
            requestOutcome.Clear();
            requestStarted.Clear();
            requestShaped.Clear();
            if (hooked) return;
            hooked = true;
            ModEvents.GameUpdate.RegisterHandler((ref ModEvents.SGameUpdateData _d) => ClientTick());
        }

        // ================================================================== the client ====

        /// <summary>
        /// Client, every frame (GameUpdate), at most once a second: its own journal's markers (TickMarkers, from the copy's prospect
        /// positions), a start that waited for the journal, and the report when it changed. Nothing on the server or in single
        /// player: the host's journal is the ticker's (ScTicker: SyncCompleted, TickMarkers).
        /// </summary>
        private static void ClientTick()
        {
            try
            {
                if (ScCommunity.IsHost) return;
                float now = Time.realtimeSinceStartup;
                if (now < nextClientTick) return;
                nextClientTick = now + 1f;
                if (!ScNet.IsClient || ScNet.Cm == null || !ScNet.ServerMatches || !ScRegistry.Mirrored) return;
                EntityPlayerLocal lp = ScCommunity.LocalPlayer;
                if (lp == null || lp.QuestJournal == null || lp.QuestJournal.quests == null) return;
                RetryPending(lp);
                ScQuests.TickMarkers(lp);
                if (!ScNet.ServerKnows<NetPackageScJournalReport>()) return;
                ScJournalReport r = BuildReport(lp);
                string d = r.Digest();
                if (d == lastDigest && now < nextReport) return;
                lastDigest = d;
                nextReport = now + ReportSeconds;
                ScNet.Cm.SendToServer(NetPackageManager.GetPackage<NetPackageScJournalReport>().Setup(r));
            }
            catch (Exception e)
            {
                ScLog.Once("questnet-client", e);
            }
        }

        /// <summary>Client: what its journal holds (the SC quests) and where its quests in progress lead (every quest's POI).</summary>
        internal static ScJournalReport BuildReport(EntityPlayerLocal lp)
        {
            var r = new ScJournalReport { Talking = ScQuests.TalkingTo(lp) };
            List<Quest> quests = lp.QuestJournal.quests;
            var seen = new HashSet<Vector3i>();
            for (int i = 0; i < quests.Count; i++)
            {
                Quest q = quests[i];
                if (q == null) continue;
                bool inProgress = q.CurrentState == Quest.QuestState.InProgress;
                Vector3 pos;
                if (inProgress && r.Pois.Count < ScJournalReport.MaxPois && q.GetPositionData(out pos, Quest.PositionDataTypes.POIPosition))
                {
                    Vector3i key = World.worldToBlockPos(pos);
                    if (seen.Add(key)) r.Pois.Add(key);
                }
                if (ScQuests.IsFavourQuest(q))
                {
                    bool poi = string.Equals(q.ID, ScQuests.QuestId, StringComparison.OrdinalIgnoreCase);
                    bool completed = q.CurrentState == Quest.QuestState.Completed;
                    if (!inProgress)
                    {
                        // a completed POI favour quest counts while the copy says that favour is not done yet (FavourQuestDone's rule)
                        if (!poi || !completed) continue;
                        Prospect mp = ScRegistry.GetProspect(q.QuestGiverID);
                        if (mp == null || mp.Done) continue;
                    }
                    byte flags = 0;
                    if (inProgress) flags |= ScJournalReport.FlagInProgress;
                    if (poi && (completed || (inProgress && q.CurrentPhase >= 2))) flags |= ScJournalReport.FlagDone;
                    r.Add(poi ? ScJournalReport.KindPoiFavour : ScJournalReport.KindSupplies, q.QuestGiverID, IntVar(q, ScQuests.EncPersonVar), flags);
                    continue;
                }
                if (!inProgress) continue;
                if (ScQuests.IsRescueQuest(q))
                {
                    int mid = IntVar(q, ScQuests.MemberVar);
                    if (mid != 0) r.Add(ScJournalReport.KindRescue, mid, 0, ScJournalReport.FlagInProgress);
                }
                else if (ScQuests.IsRequestQuest(q))
                {
                    int rid = ScQuests.RequestIdOf(q);
                    if (rid != 0) r.Add(ScJournalReport.KindRequest, rid, 0, ScJournalReport.FlagInProgress);
                }
            }
            return r;
        }

        private static int IntVar(Quest q, string key)
        {
            string v;
            int n;
            return q != null && q.DataVariables != null && q.DataVariables.TryGetValue(key, out v) && int.TryParse(v, out n) ? n : 0;
        }

        /// <summary>
        /// Client (NetPackageScQuestOp.ProcessPackage, main thread): the server's op, on this machine's own journal with the same
        /// ScQuests functions the host uses on its own. A start whose journal is not ready yet waits (RetryPending).
        /// </summary>
        internal static void OnOp(byte kind, int id, int to, bool completed, int[] ints, string[] strs)
        {
            try
            {
                if (ScCommunity.IsHost) return;
                EntityPlayerLocal lp = ScCommunity.LocalPlayer;
                if (lp == null) return;
                lastDigest = null;                      // the journal changed: the next tick reports it
                switch (kind)
                {
                    case NetPackageScQuestOp.KindFavourClose:
                        if (ScQuests.FindFavour(lp, id) == null) return;
                        ScQuests.CloseFavour(lp, id, completed);
                        ScLog.Out("favour quest of " + id + " closed (" + (completed ? "completed" : "removed") + ") - the server ended that favour");
                        return;
                    case NetPackageScQuestOp.KindFavourRekey:
                        ScQuests.RekeyFavour(lp, id, to);
                        return;
                    case NetPackageScQuestOp.KindRescueStart:
                    case NetPackageScQuestOp.KindRequestStart:
                        if (!TryStart(lp, kind, id, ints, strs)) pendingStarts.Add(new PendingStart { Kind = kind, Id = id, Ints = ints, Strs = strs });
                        return;
                    case NetPackageScQuestOp.KindRescueClose:
                        DropPending(NetPackageScQuestOp.KindRescueStart, id);
                        if (ScQuests.FindRescue(lp, id) == null) return;
                        ScQuests.CloseRescueById(lp, id, completed);
                        ScLog.Out("rescue quest of " + id + " closed (" + (completed ? "completed" : "removed") + ") - the server ended that rescue");
                        return;
                    case NetPackageScQuestOp.KindRescueRekey:
                        ScQuests.RekeyRescueIn(lp, id, to);
                        for (int i = 0; i < pendingStarts.Count; i++)
                            if (pendingStarts[i].Kind == NetPackageScQuestOp.KindRescueStart && pendingStarts[i].Id == id) pendingStarts[i].Id = to;
                        return;
                    case NetPackageScQuestOp.KindRequestShape:
                    {
                        Quest q = ScQuests.FindRequest(lp, id);
                        if (q != null) ScQuests.ShapeRequestData(q, RequestData(id, ints, strs));
                        return;
                    }
                    case NetPackageScQuestOp.KindRequestClose:
                    {
                        DropPending(NetPackageScQuestOp.KindRequestStart, id);
                        Quest q = ScQuests.FindRequest(lp, id);
                        if (q == null) return;
                        ScQuests.CloseRequest(lp, q, completed);
                        ScLog.Out("enclave request quest " + id + " closed (" + (completed ? "completed" : "removed") + ") - the server ended that request");
                        return;
                    }
                }
            }
            catch (Exception e)
            {
                ScLog.Once("questnet-op-" + kind, e);
            }
        }

        /// <summary>Client: a start op on its own journal. False only when the journal is not ready yet (tried again).</summary>
        private static bool TryStart(EntityPlayerLocal lp, byte kind, int id, int[] ints, string[] strs)
        {
            string error;
            if (kind == NetPackageScQuestOp.KindRescueStart)
            {
                var a = new Vector3i(At(ints, 0), At(ints, 1), At(ints, 2));
                var s = new Vector3i(At(ints, 3), At(ints, 4), At(ints, 5));
                bool had = ScQuests.FindRescue(lp, id) != null;
                if (ScQuests.StartRescueData(lp, id, Str(strs, 0), Str(strs, 1), a, s, out error))
                {
                    if (!had) ScLog.Out("rescue quest for " + Str(strs, 0) + " (" + id + ") started - the server's call for help");
                    return true;
                }
                if (error == ScQuests.RescueJournalNotReady) return false;
                ScLog.Warn("rescue quest for " + Str(strs, 0) + " (" + id + ") could not start: " + error);
                return true;
            }
            if (ScQuests.FindRequest(lp, id) != null) return true;          // in the journal already (a report crossed the op)
            Quest q = ScQuests.StartRequestData(lp, RequestData(id, ints, strs), out error);
            if (q != null)
            {
                ScLog.Out("enclave request quest " + id + " started (" + q.ID + ") - the request you took on");
                return true;
            }
            if (error == ScQuests.RequestJournalNotReady) return false;
            ScLog.Warn("enclave request quest " + id + " could not start: " + error);
            return true;
        }

        private static void RetryPending(EntityPlayerLocal lp)
        {
            for (int i = pendingStarts.Count - 1; i >= 0; i--)
            {
                PendingStart p = pendingStarts[i];
                if (TryStart(lp, p.Kind, p.Id, p.Ints, p.Strs)) { pendingStarts.RemoveAt(i); continue; }
                if (++p.Tries < PendingTries) continue;
                pendingStarts.RemoveAt(i);
                ScLog.Warn("a quest op (kind " + p.Kind + ", id " + p.Id + ") gave up: this player's record never came in");
            }
        }

        private static void DropPending(byte kind, int id)
        {
            for (int i = pendingStarts.Count - 1; i >= 0; i--)
                if (pendingStarts[i].Kind == kind && pendingStarts[i].Id == id) pendingStarts.RemoveAt(i);
        }

        private static int At(int[] a, int i) { return a != null && i >= 0 && i < a.Length ? a[i] : 0; }
        private static string Str(string[] a, int i) { return a != null && i >= 0 && i < a.Length && a[i] != null ? a[i] : ""; }

        // ---- the request data's places in an op (NetPackageScQuestOp's layout) ----

        private const int RqFlagE5 = 1, RqFlagReturning = 2;

        private static void RequestPayload(ScQuests.RequestQuestData d, out int[] ints, out string[] strs)
        {
            int flags = (d.E5 ? RqFlagE5 : 0) | (d.Returning ? RqFlagReturning : 0);
            ints = new[] { d.At.x, d.At.y, d.At.z, d.Size.x, d.Size.y, d.Size.z, d.Due, flags };
            strs = new[] { d.QuestClassId ?? "", d.Kind ?? "", d.Place ?? "", d.What ?? "", d.EnclaveName ?? "", d.Who ?? "", d.Target ?? "", d.Stage ?? "" };
        }

        private static ScQuests.RequestQuestData RequestData(int id, int[] ints, string[] strs)
        {
            int flags = At(ints, 7);
            return new ScQuests.RequestQuestData
            {
                Id = id,
                At = new Vector3i(At(ints, 0), At(ints, 1), At(ints, 2)),
                Size = new Vector3i(At(ints, 3), At(ints, 4), At(ints, 5)),
                Due = At(ints, 6),
                E5 = (flags & RqFlagE5) != 0,
                Returning = (flags & RqFlagReturning) != 0,
                QuestClassId = Str(strs, 0),
                Kind = Str(strs, 1),
                Place = Str(strs, 2),
                What = Str(strs, 3),
                EnclaveName = Str(strs, 4),
                Who = Str(strs, 5),
                Target = Str(strs, 6),
                Stage = Str(strs, 7)
            };
        }

        // ================================================================== the server ====

        /// <summary>A player's key for the session's tables: their platform id, else their entity id.</summary>
        private static string Key(EntityPlayer p)
        {
            string id = ScPlayers.IdOf(p);
            return !string.IsNullOrEmpty(id) ? id : "entity:" + (p != null ? p.entityId : 0);
        }

        private static HashSet<int> SetOf(Dictionary<string, HashSet<int>> table, string key)
        {
            HashSet<int> s;
            if (!table.TryGetValue(key, out s)) table[key] = s = new HashSet<int>();
            return s;
        }

        /// <summary>Server: one op to one remote player who speaks SC's protocol (never the host's own: its path is direct).</summary>
        private static bool Send(EntityPlayer p, Func<NetPackageScQuestOp> make)
        {
            if (p == null || p is EntityPlayerLocal || !ScCommunity.IsHost || !ScNet.ClientMatches(p)) return false;
            return ScNet.ToPlayer(p, () => make(), null);
        }

        /// <summary>
        /// Server: the community's remote players online who speak SC's protocol (a copy: sending may re-enter). Round MP6 (part N):
        /// the given community's (ScPlayers.CurrentCommunity: the one in scope; a rescue's: its runner's).
        /// </summary>
        private static EntityPlayer[] Remotes(int community = ScPlayers.CurrentCommunity)
        {
            List<EntityPlayer> mine = ScPlayers.Community(community);
            var to = new List<EntityPlayer>();
            for (int i = 0; i < mine.Count; i++)
                if (mine[i] != null && !(mine[i] is EntityPlayerLocal) && ScNet.ClientMatches(mine[i])) to.Add(mine[i]);
            return to.ToArray();
        }

        /// <summary>
        /// Round MP6 (part N), server: every remote player online who speaks SC's protocol, whatever their community - for an op
        /// every copy must follow (an enclave person's favour re-keyed: each community courting them holds its own quest).
        /// </summary>
        private static EntityPlayer[] AllRemotes()
        {
            List<EntityPlayer> all = ScPlayers.All;
            var to = new List<EntityPlayer>();
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && !(all[i] is EntityPlayerLocal) && ScNet.ClientMatches(all[i])) to.Add(all[i]);
            return to.ToArray();
        }

        /// <summary>
        /// Round MP6 (part N), server: the prospect with this id in the community in scope (a report's: its sender's), or null - a
        /// favour quest is its own community's (each community courts a survivor on its own); with one community GetProspect.
        /// </summary>
        private static Prospect OwnProspect(int id)
        {
            Prospect p;
            return ScRegistry.Prospects.TryGetValue(id, out p) ? p : null;
        }

        // ---- favours ----

        /// <summary>Server: close this favour's quest in one remote player's journal (ScTalkContext.CloseFavourForOthers).</summary>
        public static void CloseFavourFor(EntityPlayer p, int prospectId, bool completed)
        {
            Send(p, () => NetPackageManager.GetPackage<NetPackageScQuestOp>().Setup(NetPackageScQuestOp.KindFavourClose, prospectId, 0, completed, null, null));
        }

        /// <summary>
        /// Server: an enclave person's favour moved to a new key (ScEnclaveDeals.Rekey: the host's own journal is re-keyed there) -
        /// every remote copy follows. A player away now follows on their next report (their quest names the person).
        /// Round MP6 (part N): every remote player's, whatever their community - the person's body is the world's, and each
        /// community courting them holds its own quest under the old key (a client holding none ignores the op).
        /// </summary>
        public static void RekeyFavourRemote(int oldId, int newId)
        {
            try
            {
                if (!ScCommunity.IsHost || oldId == newId) return;
                foreach (EntityPlayer p in AllRemotes())
                    Send(p, () => NetPackageManager.GetPackage<NetPackageScQuestOp>().Setup(NetPackageScQuestOp.KindFavourRekey, oldId, newId, false, null, null));
            }
            catch (Exception e)
            {
                ScLog.Once("questnet-rekeyfavour", e);
            }
        }

        // ---- rescues ----

        /// <summary>A runner's rescue is open: the call is out, nobody has come yet, and nothing ended it (ScRadio's distress).</summary>
        private static bool IsOpenRescue(Member m)
        {
            return m != null && m.Status == MemberStatus.OnRun && m.RunDistress && !m.RunSafe;
        }

        /// <summary>
        /// Where a rescue's marker goes (as ScRadio.ForceDistress made it): the POI the call came from (Member.RunPoi: the
        /// physical runner's destination, or the building picked), else (3.4) a mining runner's natural spot, 16 x 16 round it.
        /// False when the call has no place (no quest then, as for the host).
        /// </summary>
        private static bool RescueBox(Member m, out Vector3i a, out Vector3i s)
        {
            a = s = Vector3i.zero;
            PrefabInstance pi = ScSecurity.PoiByKey(m.RunPoi);
            if (pi != null)
            {
                a = pi.boundingBoxPosition;
                s = pi.boundingBoxSize;
                return true;
            }
#if SC_V34
            if (ScRunners.IsPhysical(m) && ScMining.IsDig(m))
            {
                a = new Vector3i(m.RunSite.x - 8, m.RunSite.y, m.RunSite.z - 8);
                s = new Vector3i(16, 1, 16);
                return true;
            }
#endif
            return false;
        }

        private static NetPackageScQuestOp RescueStartOp(Member m, Vector3i a, Vector3i s)
        {
            return NetPackageManager.GetPackage<NetPackageScQuestOp>().Setup(NetPackageScQuestOp.KindRescueStart, m.Id, 0, false,
                new[] { a.x, a.y, a.z, s.x, s.y, s.z }, new[] { m.Name ?? "", m.RunPoiName ?? "" });
        }

        /// <summary>
        /// Server (ScRadio.ForceDistress, after the host's own quest): the call is out - every remote community player online gets
        /// their own rescue quest. One who is not online (or not matched yet) gets it with their first report.
        /// Round MP6 (part N): the runner's own community's players (her record's CommunityId), whoever's scope the call came from.
        /// </summary>
        public static void RescueStarted(Member m)
        {
            try
            {
                if (m == null || !ScCommunity.IsHost) return;
                // a new call of the same runner: the last one's bookkeeping goes
                rescueOutcome.Remove(m.Id);
                foreach (HashSet<int> sent in rescueSent.Values) sent.Remove(m.Id);
                Vector3i a, s;
                if (!IsOpenRescue(m) || !RescueBox(m, out a, out s)) return;
                foreach (EntityPlayer p in Remotes(m.CommunityId))
                    if (Send(p, () => RescueStartOp(m, a, s))) SetOf(rescueSent, Key(p)).Add(m.Id);
            }
            catch (Exception e)
            {
                ScLog.Once("questnet-rescue-start", e);
            }
        }

        /// <summary>
        /// Server (ScRadio: Rescued, the stand-in's death, EndDistress): the rescue ended - every copy closes, completed or
        /// removed as failed. The host's own journal here (ScQuests.CloseRescue, as before - the host's own player, whichever
        /// player the caller reasons from), every remote one by op; one away closes with their next report (the outcome kept).
        /// A second close of the same call (EndDistress after Rescued) closes the host's again (a no-op) and sends nothing.
        /// Round MP6 (part N): the runner's own community's remote players (her record's CommunityId). The host's own journal is
        /// closed whatever their community (a copy there is closed; none there, a no-op).
        /// </summary>
        public static void CloseRescueAll(Member m, bool completed)
        {
            if (m == null) return;
            EntityPlayerLocal lp = ScCommunity.LocalPlayer;
            if (lp != null) ScQuests.CloseRescue(lp, m, completed);
            try
            {
                if (!ScCommunity.IsHost || rescueOutcome.ContainsKey(m.Id)) return;
                rescueOutcome[m.Id] = completed;
                foreach (HashSet<int> sent in rescueSent.Values) sent.Remove(m.Id);
                int id = m.Id;
                foreach (EntityPlayer p in Remotes(m.CommunityId))
                    Send(p, () => NetPackageManager.GetPackage<NetPackageScQuestOp>().Setup(NetPackageScQuestOp.KindRescueClose, id, 0, completed, null, null));
            }
            catch (Exception e)
            {
                ScLog.Once("questnet-rescue-close", e);
            }
        }

        /// <summary>
        /// Server (ScQuests.RekeyRescue): a runner's new entity id - every remote copy follows, and the session's tables. Round MP6
        /// (part N): her community's players (the record under either id; else the community in scope).
        /// </summary>
        public static void RekeyRescueRemote(int oldId, int newId)
        {
            try
            {
                if (!ScCommunity.IsHost || oldId == newId) return;
                rescueRekeys[oldId] = newId;
                bool done;
                if (rescueOutcome.TryGetValue(oldId, out done)) { rescueOutcome.Remove(oldId); rescueOutcome[newId] = done; }
                foreach (HashSet<int> sent in rescueSent.Values)
                    if (sent.Remove(oldId)) sent.Add(newId);
                Member who = ScRegistry.GetMember(newId) ?? ScRegistry.GetMember(oldId);     // round MP6 (part N)
                foreach (EntityPlayer p in Remotes(who != null ? who.CommunityId : ScPlayers.CurrentCommunity))
                    Send(p, () => NetPackageManager.GetPackage<NetPackageScQuestOp>().Setup(NetPackageScQuestOp.KindRescueRekey, oldId, newId, false, null, null));
            }
            catch (Exception e)
            {
                ScLog.Once("questnet-rescue-rekey", e);
            }
        }

        /// <summary>A member id through the session's re-keys (a runner re-created more than once follows to the last).</summary>
        private static int CurrentMemberId(int id)
        {
            int next, guard = 0;
            while (rescueRekeys.TryGetValue(id, out next) && next != id && guard++ < 16) id = next;
            return id;
        }

        // ---- enclave requests ----

        /// <summary>
        /// Server: the request's quest is the host's own player's - AcceptedBy names them, or nobody (a save from before MP4; until
        /// MP5 every acceptance is the host's). Single player: always.
        /// </summary>
        public static bool HostAccepted(EnclaveRequest r)
        {
            if (r == null) return false;
            if (string.IsNullOrEmpty(r.AcceptedBy)) return true;
            EntityPlayerLocal lp = ScCommunity.LocalPlayer;
            if (lp == null) return false;
            if (string.Equals(r.AcceptedBy, ScPlayers.IdOf(lp), StringComparison.Ordinal)) return true;
            // the host's own id is the cross-platform one, else the platform one (ScPlayers.IdOf): a session where the cross-platform
            // service was not there keeps an acceptance under the other one the host's all the same
            try
            {
                PlatformUserIdentifierAbs cross = Platform.PlatformManager.CrossplatformPlatform != null && Platform.PlatformManager.CrossplatformPlatform.User != null
                    ? Platform.PlatformManager.CrossplatformPlatform.User.PlatformUserId : null;
                PlatformUserIdentifierAbs native = Platform.PlatformManager.NativePlatform != null && Platform.PlatformManager.NativePlatform.User != null
                    ? Platform.PlatformManager.NativePlatform.User.PlatformUserId : null;
                return (cross != null && string.Equals(r.AcceptedBy, cross.CombinedString, StringComparison.Ordinal))
                       || (native != null && string.Equals(r.AcceptedBy, native.CombinedString, StringComparison.Ordinal));
            }
            catch (Exception e)
            {
                ScLog.Once("questnet-hostids", e);
                return false;
            }
        }

        private static bool AcceptedBy(EnclaveRequest r, EntityPlayer p)
        {
            return r != null && p != null && !string.IsNullOrEmpty(r.AcceptedBy) && string.Equals(r.AcceptedBy, ScPlayers.IdOf(p), StringComparison.Ordinal);
        }

        /// <summary>Server: the remote player online who accepted this request, or null (the host's own, nobody, or away).</summary>
        private static EntityPlayer RemoteAcceptor(EnclaveRequest r)
        {
            if (r == null || HostAccepted(r)) return null;
            EntityPlayer p = ScPlayers.ById(r.AcceptedBy);
            return p != null && !(p is EntityPlayerLocal) ? p : null;
        }

        /// <summary>
        /// Server (ScEncRequests.Accept, ScEncSiege's call): the request's journal quest for the player it is for - the host's own
        /// journal (ScQuests.StartRequest, as before: the quest comes back), or a client's by op (null, error SentToClient).
        /// The caller has set EnclaveRequest.AcceptedBy.
        /// </summary>
        public static Quest StartRequestFor(EntityPlayer acceptor, Enclave e, EnclaveRequest r, string what, out string error)
        {
            error = null;
            if (acceptor == null || e == null || r == null) { error = "no player"; return null; }
            EntityPlayerLocal lp = acceptor as EntityPlayerLocal;
            if (lp != null) return ScQuests.StartRequest(lp, e, r, what, out error);
            try
            {
                int[] ints;
                string[] strs;
                RequestPayload(ScQuests.RequestQuestData.Of(e, r, what), out ints, out strs);
                int id = r.Id;
                if (!Send(acceptor, () => NetPackageManager.GetPackage<NetPackageScQuestOp>().Setup(NetPackageScQuestOp.KindRequestStart, id, 0, false, ints, strs)))
                {
                    error = "the player's game does not speak SurvivorCommunity's protocol";
                    return null;
                }
                string key = Key(acceptor);
                SetOf(requestStarted, key).Add(id);
                SetOf(requestShaped, key).Add(id);
                error = SentToClient;
            }
            catch (Exception ex)
            {
                ScLog.Once("questnet-request-start", ex);
                error = "the quest op threw (see the log)";
            }
            return null;
        }

        /// <summary>
        /// Server (ScEncRequests.Close, after the host's own close): the request ended - its acceptor's quest closes by op when they
        /// are a remote player online; one away closes with their next report (the outcome kept).
        /// </summary>
        public static void CloseRequestRemote(EnclaveRequest r, bool completed)
        {
            try
            {
                if (r == null || !ScCommunity.IsHost) return;
                if (!requestOutcome.ContainsKey(r.Id)) requestOutcome[r.Id] = completed;
                EntityPlayer p = RemoteAcceptor(r);
                int id = r.Id;
                if (p != null) Send(p, () => NetPackageManager.GetPackage<NetPackageScQuestOp>().Setup(NetPackageScQuestOp.KindRequestClose, id, 0, completed, null, null));
            }
            catch (Exception e)
            {
                ScLog.Once("questnet-request-close", e);
            }
        }

        /// <summary>Server (ScEncRequests.Reshape, after the host's own): a stage changed - the remote acceptor's quest follows the record.</summary>
        public static void ShapeRequestRemote(EnclaveRequest r)
        {
            try
            {
                if (r == null || !ScCommunity.IsHost) return;
                EntityPlayer p = RemoteAcceptor(r);
                Enclave e = p != null ? ScEnclaves.ById(r.EnclaveId) : null;
                if (e == null) return;
                SendShape(p, e, r);
            }
            catch (Exception ex)
            {
                ScLog.Once("questnet-request-shape", ex);
            }
        }

        private static void SendShape(EntityPlayer p, Enclave e, EnclaveRequest r)
        {
            int[] ints;
            string[] strs;
            RequestPayload(ScQuests.RequestQuestData.Of(e, r, null), out ints, out strs);
            int id = r.Id;
            if (Send(p, () => NetPackageManager.GetPackage<NetPackageScQuestOp>().Setup(NetPackageScQuestOp.KindRequestShape, id, 0, false, ints, strs)))
                SetOf(requestShaped, Key(p)).Add(id);
        }

        // ---- what the reports tell the rest of SC ----

        /// <summary>
        /// Server (the enclave, infestation and missing-survivor pickers' "taken" sets): the POIs remote players' quests in
        /// progress lead to, from their last reports this session - as the host's own journal is read there.
        /// </summary>
        public static void AddReportedPois(HashSet<Vector3i> into)
        {
            if (into == null || reports.Count == 0) return;
            foreach (ScJournalReport r in reports.Values)
                for (int i = 0; i < r.Pois.Count; i++) into.Add(r.Pois[i]);
        }

        /// <summary>
        /// Server (ScQuests.IsOurPoi, E6): does a remote subscriber's last report hold a POI favour quest in progress whose
        /// building is this one?
        /// </summary>
        public static bool ReportedFavourPoi(List<int> subscribers, Vector3i key)
        {
            if (subscribers == null || reports.Count == 0) return false;
            for (int i = 0; i < subscribers.Count; i++)
            {
                EntityPlayer pl = ScPlayers.ByEntity(subscribers[i]);
                if (pl == null || pl is EntityPlayerLocal) continue;
                ScJournalReport r;
                if (!reports.TryGetValue(Key(pl), out r)) continue;
                // round MP6 (fix N-m4): the reporter's own community's favour (another community courting the same survivor has its own POI)
                CommunityState rcs = ScCommunities.Count > 1 ? ScCommunities.ById(ScPlayers.CommunityOf(pl)) : null;
                foreach (ScJournalReport.Entry e in r.Entries)
                {
                    if (e.Kind != ScJournalReport.KindPoiFavour || !e.InProgress) continue;
                    Prospect p;
                    if (rcs != null) rcs.Prospects.TryGetValue(e.Id, out p);
                    else p = ScRegistry.GetProspect(e.Id);
                    if (p != null && p.Favour == FavourKind.Poi && p.PoiPos == key) return true;
                }
            }
            return false;
        }

        // ---- the report ----

        /// <summary>
        /// Server (NetPackageScJournalReport.ProcessPackage, main thread): a remote player's journal. Kept for the session, then
        /// answered - see the class. Never from the host's own player (it has its direct path).
        /// </summary>
        internal static void OnReport(ClientInfo sender, ScJournalReport rep)
        {
            try
            {
                if (sender == null || rep == null || !ScCommunity.IsHost || !ScRegistry.Loaded) return;
                EntityPlayer p = ScPlayers.ByEntity(sender.entityId);
                if (p == null || p is EntityPlayerLocal || !ScNet.ClientMatches(p)) return;
                string key = Key(p);
                rep.EntityId = p.entityId;
                reports[key] = rep;
                bool save = FavoursDone(p, rep);
                if (FavoursHeld()) save = true;
                StaleFavours(p, rep);
                Rescues(p, key, rep);
                Requests(p, key, rep);
                if (save) ScRegistry.SaveAtomic();
            }
            catch (Exception e)
            {
                ScLog.Once("questnet-report", e);
            }
        }

        /// <summary>
        /// The report's POI favours past their clearing are done - SyncCompleted's test on the host's journal, for this one:
        /// Done, sc_favourDone (force-sent), the leadership count, the tell. Not the favour of the survivor they are talking to
        /// (the dialog completes it there). True when one changed.
        /// </summary>
        private static bool FavoursDone(EntityPlayer p, ScJournalReport rep)
        {
            bool changed = false;
            foreach (ScJournalReport.Entry e in rep.Entries)
            {
                if (e.Kind != ScJournalReport.KindPoiFavour || !e.Done) continue;
                Prospect pr = OwnProspect(e.Id);         // round MP6 (part N): the sender's community's favour (the report's scope)
                if (pr == null || pr.Favour != FavourKind.Poi || pr.Done || pr.Id == rep.Talking) continue;
                ScFavours.MarkDone(ScCommunity.FindNpc(pr.Id), pr);
                if (!pr.Done) continue;
                changed = true;
                // the quest stays open: its phase 2 ("return to <name>") completes when the player talks to them
                // round MP4 (part: tells): this file's tells go by localization key (ScCommunity.TellKey / ScPlayers.TellCommunityKey), so each player reads them in their own language
                ScCommunity.TellKey(p, "scFavourPoiDone", "", pr.PoiName, pr.Name);
                ScLog.Out("favour done: " + pr.PoiName + " is clear - " + p.PlayerDisplayName + " returns to " + pr.Name + " and they will join (their journal's report)");
            }
            return changed;
        }

        /// <summary>
        /// E6 (MP2's known gap): a POI favour a remote player held this session that no player holds now - no remote report, not
        /// the host's own journal - is no longer "taken on" (Prospect.Started), so its building's vanilla clear quests re-arm again
        /// for remote players (ScQuests.IsOurPoi). A favour only the host ever held keeps its old behavior. A report holding a
        /// POI favour marks it taken on again. True when one changed.
        /// </summary>
        private static bool FavoursHeld()
        {
            var held = new HashSet<int>();
            // round MP6 (part N): the reports of the community in scope's players (the sender's), whose favours these prospects are -
            // another community's quest for the same survivor is its own favour. With one community: every report, as before
            bool several = ScCommunities.Count > 1;
            int cid = ScRegistry.CurrentId;
            foreach (KeyValuePair<string, ScJournalReport> kv in reports)
            {
                if (several && ScPlayers.CommunityOfId(kv.Key) != cid) continue;
                foreach (ScJournalReport.Entry e in kv.Value.Entries)
                    if (e.Kind == ScJournalReport.KindPoiFavour && e.InProgress) held.Add(e.Id);
            }
            bool changed = false;
            EntityPlayerLocal host = ScCommunity.LocalPlayer;
            foreach (Prospect pr in ScRegistry.Prospects.Values)
            {
                if (pr.Favour != FavourKind.Poi || pr.Done) continue;
                if (held.Contains(pr.Id))
                {
                    favourEverHeld.Add(FavourKey(cid, pr.Id));
                    if (pr.Started) continue;
                    pr.Started = true;
                    changed = true;
                    ScLog.Out("favour of " + pr.Name + " (" + pr.PoiName + "): a player's journal holds its quest - taken on again (E6)");
                    continue;
                }
                if (!pr.Started || !favourEverHeld.Contains(FavourKey(cid, pr.Id))) continue;
                // round MP6 (fix N-m3): the host's journal holds this community's quest only when the host is of it
                if (host != null && (!several || ScPlayers.CommunityOf(host) == cid) && ScQuests.FindFavour(host, pr.Id) != null) continue;
                pr.Started = false;
                favourEverHeld.Remove(FavourKey(cid, pr.Id));
                changed = true;
                ScLog.Out("favour of " + pr.Name + " (" + pr.PoiName + "): no player holds its quest any more - " + pr.PoiName + " is no longer held for it (E6)");
            }
            return changed;
        }

        /// <summary>
        /// The report's favour quests whose favour ended while they were away (or the close never reached them): an enclave
        /// person's follows their prospect's new key (ScQuests.SyncEnclaveFavours' rule) or closes when the person's favour is
        /// gone; any other closes (removed) when its prospect is gone - they joined, died or turned hostile.
        /// </summary>
        private static void StaleFavours(EntityPlayer p, ScJournalReport rep)
        {
            foreach (ScJournalReport.Entry e in rep.Entries)
            {
                if ((e.Kind != ScJournalReport.KindPoiFavour && e.Kind != ScJournalReport.KindSupplies) || !e.InProgress) continue;
                int id = e.Id;
                if (e.EncPerson > 0)
                {
                    Prospect pp = ScEnclaveDeals.ProspectByPerson(e.EncPerson);
                    if (pp == null) CloseFavourFor(p, id, false);
                    else if (pp.Id != id)
                    {
                        int to = pp.Id;
                        Send(p, () => NetPackageManager.GetPackage<NetPackageScQuestOp>().Setup(NetPackageScQuestOp.KindFavourRekey, id, to, false, null, null));
                    }
                    continue;
                }
                if (OwnProspect(id) == null) CloseFavourFor(p, id, false);     // round MP6 (part N): gone from the sender's community
            }
        }

        /// <summary>
        /// The report's rescue quests and the open rescues: a copy of a call that ended closes (the session's outcome, else
        /// removed), one of a runner re-keyed since follows, and an open call their journal lacks starts - once per player per call.
        /// </summary>
        private static void Rescues(EntityPlayer p, string key, ScJournalReport rep)
        {
            HashSet<int> sent = SetOf(rescueSent, key);
            var held = new HashSet<int>();
            foreach (ScJournalReport.Entry e in rep.Entries)
            {
                if (e.Kind != ScJournalReport.KindRescue || !e.InProgress) continue;
                int id = e.Id, cur = CurrentMemberId(e.Id);
                Member m = ScRegistry.GetMember(cur);
                // round MP6 (part N): a runner of another community than the sender's (they changed community) is not theirs to
                // rescue - their copy closes as an ended call's
                if (m != null && m.CommunityId != ScRegistry.CurrentId) m = null;
                if (IsOpenRescue(m))
                {
                    // review m1: a copy this player was not sent this session is an older call's (they were away while it ended and
                    // the runner called again, or the server restarted): it is replaced - removed, then the open call's copy starts
                    // (its place and marker are this call's). After a restart every holder gets one harmless close and start
                    if (!sent.Contains(cur))
                    {
                        Vector3i ra, rs;
                        if (!RescueBox(m, out ra, out rs)) { held.Add(cur); continue; }
                        Member rm = m;
                        int oldId = id;
                        Send(p, () => NetPackageManager.GetPackage<NetPackageScQuestOp>().Setup(NetPackageScQuestOp.KindRescueClose, oldId, 0, false, null, null));
                        if (Send(p, () => RescueStartOp(rm, ra, rs)))
                        {
                            sent.Add(cur);
                            ScLog.Out("rescue quest for " + m.Name + " renewed for " + p.PlayerDisplayName + " (their copy was from an earlier call or session)");
                        }
                        held.Add(cur);
                        continue;
                    }
                    if (cur != id) Send(p, () => NetPackageManager.GetPackage<NetPackageScQuestOp>().Setup(NetPackageScQuestOp.KindRescueRekey, id, cur, false, null, null));
                    held.Add(cur);
                    continue;
                }
                bool done;
                if (!rescueOutcome.TryGetValue(cur, out done) && !rescueOutcome.TryGetValue(id, out done)) done = false;
                Send(p, () => NetPackageManager.GetPackage<NetPackageScQuestOp>().Setup(NetPackageScQuestOp.KindRescueClose, id, 0, done, null, null));
            }
            foreach (Member m in ScRegistry.Members.Values)
            {
                if (!IsOpenRescue(m) || held.Contains(m.Id) || sent.Contains(m.Id)) continue;
                Vector3i a, s;
                if (!RescueBox(m, out a, out s)) continue;
                Member mm = m;
                if (Send(p, () => RescueStartOp(mm, a, s)))
                {
                    sent.Add(m.Id);
                    ScLog.Out("rescue quest for " + m.Name + " sent to " + p.PlayerDisplayName + " (the call is still open)");
                }
            }
        }

        /// <summary>
        /// The report's request quests and the requests this player accepted: a quest whose request ended, is gone or is not
        /// theirs closes (the session's outcome, else removed); a kept one is shaped to the record once a session (the host's
        /// journal pass: ScEncRequests.SyncJournal); an accepted request their journal lacks starts, once a session.
        /// </summary>
        private static void Requests(EntityPlayer p, string key, ScJournalReport rep)
        {
            HashSet<int> started = SetOf(requestStarted, key), shaped = SetOf(requestShaped, key);
            var held = new HashSet<int>();
            foreach (ScJournalReport.Entry e in rep.Entries)
            {
                if (e.Kind != ScJournalReport.KindRequest || !e.InProgress) continue;
                int id = e.Id;
                EnclaveRequest r = ScEncRequests.ById(id);
                Enclave en = r != null ? ScEnclaves.ById(r.EnclaveId) : null;
                if (r == null || !r.IsAccepted || en == null || !AcceptedBy(r, p) || !held.Add(id))
                {
                    bool done;
                    if (!requestOutcome.TryGetValue(id, out done)) done = false;
                    Send(p, () => NetPackageManager.GetPackage<NetPackageScQuestOp>().Setup(NetPackageScQuestOp.KindRequestClose, id, 0, done, null, null));
                    continue;
                }
                if (!shaped.Contains(id)) SendShape(p, en, r);
            }
            if (!ScConfig.Enclaves) return;
            foreach (EnclaveRequest r in ScEncRequests.All)
            {
                if (r == null || !r.IsAccepted || held.Contains(r.Id) || started.Contains(r.Id) || !AcceptedBy(r, p)) continue;
                Enclave en = ScEnclaves.ById(r.EnclaveId);
                if (en == null || en.Lost || en.IsHostile) continue;     // no building this load / withdrawn at the next day's pass
                string error;
                StartRequestFor(p, en, r, ScEncRequests.WhatText(r), out error);
                if (error == SentToClient) ScLog.Out("enclave request #" + r.Id + ": its quest sent again to " + p.PlayerDisplayName + " (their journal lacked it)");
            }
        }
    }
}
