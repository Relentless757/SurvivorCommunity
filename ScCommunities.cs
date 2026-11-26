using System;
using System.Collections.Generic;

namespace SurvivorCommunity
{
    // round MP6 (core), 2026-11-26: several communities on one server. This file holds the per-community state
    // (CommunityState), the list of communities (ScCommunities), the one world record (ScWorldState), the record containers
    // that stamp a record's community as it goes in, and the scope handle ScRegistry.Use returns (ScScope).
    //
    // How it fits together:
    // - ScRegistry.Community, .Members, .Prospects, .Contacts, .Outposts and .EncRequests are read-only properties of
    //   ScRegistry.Current: the existing code keeps reading "the community" and gets the one in scope.
    // - ScRegistry.Use(cid) (also Use(Member), Use(Outpost), Use(EntityPlayer)) pushes a scope; its Dispose pops it, exceptions
    //   included (always "using (ScRegistry.Use(...)) { ... }").
    // - World state (enclaves, infestations, bands and their people, every id counter, the request clock, the seeding, refills,
    //   remains, streamer names, the membership map) is ScRegistry.WorldState plus the world lists on ScRegistry.
    // - With one community (everyone in community 0) every path answers community 0: nothing changes.
    // Main thread only, as the registry always was.

    /// <summary>Round MP6 (core): a registry record that belongs to one community (stamped by the container it is put in).</summary>
    public interface IScCommunityRecord
    {
        /// <summary>The community whose container holds the record (ScCommunities). 0 until it is put in one.</summary>
        int CommunityId { get; set; }
    }

    /// <summary>
    /// Round MP6 (core): a community's records by id (Members, Prospects). Putting a record in (the indexer's set, Add) stamps its
    /// CommunityId with this table's community, so a record's CommunityId always names the table that holds it. Reached through
    /// the Dictionary base type (a cast, an interface) a put does not stamp: SC never does that.
    /// </summary>
    public sealed class ScRecordTable<T> : Dictionary<int, T> where T : class, IScCommunityRecord
    {
        private readonly int cid;

        internal ScRecordTable(int communityId) { cid = communityId; }

        public new T this[int key]
        {
            get { return base[key]; }
            set
            {
                if (value != null) value.CommunityId = cid;
                base[key] = value;
            }
        }

        public new void Add(int key, T value)
        {
            if (value != null) value.CommunityId = cid;
            base.Add(key, value);
        }
    }

    /// <summary>Round MP6 (core): a community's records in order (Contacts, Outposts, EncRequests); Add and Insert stamp, as ScRecordTable.</summary>
    public sealed class ScRecordList<T> : List<T> where T : class, IScCommunityRecord
    {
        private readonly int cid;

        internal ScRecordList(int communityId) { cid = communityId; }

        public new void Add(T item)
        {
            if (item != null) item.CommunityId = cid;
            base.Add(item);
        }

        public new void Insert(int index, T item)
        {
            if (item != null) item.CommunityId = cid;
            base.Insert(index, item);
        }

        public new void AddRange(IEnumerable<T> items)
        {
            if (items == null) return;
            foreach (T item in items) Add(item);
        }
    }

    /// <summary>
    /// Round MP6 (core): one community's state - its Community record (the community fields only: the world's moved to
    /// ScWorldState) and its members, prospects, radio contacts, outposts and enclave requests. One per community, community 0
    /// always there (ScCommunities).
    /// </summary>
    public sealed class CommunityState
    {
        public readonly int Id;
        public readonly Community Community;
        public readonly ScRecordTable<Member> Members;
        public readonly ScRecordTable<Prospect> Prospects;
        /// <summary>Survivors heard over this community's radio, not yet met (ScRadio).</summary>
        public readonly ScRecordList<Contact> Contacts;
        /// <summary>Cleared POIs this community holds as outposts (ScOutposts), in establishment order.</summary>
        public readonly ScRecordList<Outpost> Outposts;
        /// <summary>The enclaves' open requests to this community (ScEncRequests), in the order they were offered.</summary>
        public readonly ScRecordList<EnclaveRequest> EncRequests;

        internal CommunityState(int id)
        {
            Id = id;
            Community = new Community { Id = id };
            Members = new ScRecordTable<Member>(id);
            Prospects = new ScRecordTable<Prospect>(id);
            Contacts = new ScRecordList<Contact>(id);
            Outposts = new ScRecordList<Outpost>(id);
            EncRequests = new ScRecordList<EnclaveRequest>(id);
        }

        /// <summary>For the console and the log: "community 1 (Riverside)", "community 0".</summary>
        public string Label
        {
            get { return "community " + Id + (string.IsNullOrEmpty(Community.Name) ? "" : " (" + Community.Name + ")"); }
        }

        // ---- round MP6 (part relations) ----
        /// <summary>
        /// Round MP6 (part relations): the players (platform ids) this community's people hold a grudge against - a player of
        /// another community who hit one of its members or stand-ins (the user's decision 4; ScRelations.OnMemberHit). Saved as
        /// [community] grudge=; never sent to a client.
        /// </summary>
        public readonly HashSet<string> Grudges = new HashSet<string>(StringComparer.Ordinal);
    }

    /// <summary>
    /// Round MP6 (core): the world record - one per save, whatever the number of communities: every id counter (so no id ever
    /// collides between communities and an old save's ids, journal quests included, stay valid), the enclaves' seeding, the
    /// request clock, the refills and remains, the streamer names given out, the world daily's own day guard and the
    /// membership map. Saved as [world] and [membership] (ScRegistry). Named so it cannot be mistaken for the engine's World.
    /// </summary>
    public sealed class ScWorldState
    {
        /// <summary>The world daily's day guard (ScDaily.Run's world half); each community keeps its own Community.LastDay.</summary>
        public int LastDay = -1;
        /// <summary>The community id counter: ScCommunities.Create hands out ++NextCommunityId (community 0 is never handed out).</summary>
        public int NextCommunityId;
        /// <summary>Radio contact and outpost id counters (contactid=, outpostid=): world-wide, so ids never collide.</summary>
        public int NextContactId;
        public int NextOutpostId;
        /// <summary>
        /// Enclaves (ScEnclaves, E1): the id counter; the seeding stamp enclaveseed=1:day:mode:seed:count (absent = never
        /// seeded in this save: a new save gets its world-start seed, an existing one the late seed; count = the target that
        /// run aimed at, which a raised config tops up).
        /// </summary>
        public int NextEnclaveId;
        public bool EnclaveSeeded;
        public int EnclaveSeedDay;
        public string EnclaveSeedMode = "";
        public int EnclaveSeed;
        public int EnclaveSeedCount;
        /// <summary>Infestations (ScInfestations, I1): the id counter (infestid=).</summary>
        public int NextInfestationId;
        /// <summary>Enclave people (ScEnclavePeople, E2): the person id counter (encpersonid=), one for every enclave.</summary>
        public int NextEncPersonId;
        /// <summary>Bands on the road (ScBands, E13a): the band and band person id counters (bandid=, bandpersonid=).</summary>
        public int NextBandId;
        public int NextBandPersonId;
        /// <summary>
        /// Enclave requests (ScEncRequests, E4): the id counter (encrequestid=), and the request clock - the last day the
        /// request pass ran (encreqday=; ahead of the world's day only after "sc day"). It dates enclave, band and move days.
        /// </summary>
        public int NextEncRequestId;
        public int EncRequestDay;
        /// <summary>E5 (ScEncSearch): a missing survivor's remains still in the world after their request closed (encremains=).</summary>
        public readonly List<Vector3i> EncRemains = new List<Vector3i>();
        /// <summary>E6 (ScEncAllies): the refills owed for lost groups, each due on a request-clock day (encrefill=).</summary>
        public readonly List<EncRefill> EncRefills = new List<EncRefill>();
        /// <summary>
        /// Streamer names (ScStreamerNames, round 30): every list name given out in this save, in the order given (streamname=,
        /// one line each). Never given again here, even after that NPC died; sc names reset clears it.
        /// </summary>
        public readonly List<string> StreamerNamesUsed = new List<string>();
        /// <summary>
        /// Who belongs to which community: a player's platform id (ScPlayers.IdOf) -> community id. Anyone not in it is in
        /// community 0 - exactly the one shared community of Stage 1. Server only; saved as [membership] player=id:cid.
        /// </summary>
        public readonly Dictionary<string, int> Membership = new Dictionary<string, int>(StringComparer.Ordinal);
        /// <summary>
        /// Session only (never saved): the community a console session acts in after "sc community use &lt;id&gt;", by the typing
        /// player's platform id ("" = the playerless consoles: telnet, the web console, a dedicated server's own).
        /// </summary>
        public readonly Dictionary<string, int> ConsoleUse = new Dictionary<string, int>(StringComparer.Ordinal);
    }

    /// <summary>
    /// Round MP6 (core): the communities of this save, lowest id first; community 0 always exists (the one of Stage 1, and
    /// every old save's). On a client it holds the mirrored copy (community 0 until the network part sends another).
    /// </summary>
    public static class ScCommunities
    {
        private static readonly List<CommunityState> list = new List<CommunityState>();
        /// <summary>A copy of list, replaced on every change, so a reader never sees a list being changed.</summary>
        private static CommunityState[] snapshot = new CommunityState[0];
        private static CommunityState zero;

        static ScCommunities()
        {
            Reset();
        }

        /// <summary>Every community, lowest id first. Never null; do not keep it across a load (Reset replaces it).</summary>
        public static IReadOnlyList<CommunityState> All { get { return snapshot; } }

        public static int Count { get { return snapshot.Length; } }

        /// <summary>All, as the array itself (the hot global lookups walk it without an interface call). Never change it.</summary>
        internal static CommunityState[] Array { get { return snapshot; } }

        /// <summary>Community 0 (always there).</summary>
        public static CommunityState Zero { get { return zero; } }

        /// <summary>The community with this id, or null.</summary>
        public static CommunityState ById(int cid)
        {
            if (cid == 0) return zero;
            CommunityState[] all = snapshot;
            for (int i = 0; i < all.Length; i++) if (all[i].Id == cid) return all[i];
            return null;
        }

        /// <summary>
        /// A new community (server, main thread): its id from the world counter (ScWorldState.NextCommunityId), its name, its
        /// owner's platform id and display name ("" for none). The caller saves.
        /// </summary>
        public static CommunityState Create(string name, string ownerId, string ownerName = "")
        {
            ScWorldState ws = ScRegistry.WorldState;
            int id = ++ws.NextCommunityId;
            while (ById(id) != null) id = ++ws.NextCommunityId;
            var cs = new CommunityState(id);
            cs.Community.Name = name ?? "";
            cs.Community.OwnerId = ownerId ?? "";
            cs.Community.Owner = ownerName ?? "";
            Insert(cs);
            return cs;
        }

        /// <summary>The load's lookup: the community with this id, made (empty) when the text has none yet. Never a negative id.</summary>
        internal static CommunityState Ensure(int cid)
        {
            CommunityState cs = ById(cid);
            if (cs != null) return cs;
            cs = new CommunityState(cid);
            Insert(cs);
            return cs;
        }

        private static void Insert(CommunityState cs)
        {
            int at = 0;
            while (at < list.Count && list[at].Id < cs.Id) at++;
            list.Insert(at, cs);
            snapshot = list.ToArray();
        }

        /// <summary>Back to community 0 alone, empty (a load, a client's new copy, the world closing).</summary>
        internal static void Reset()
        {
            list.Clear();
            zero = new CommunityState(0);
            list.Add(zero);
            snapshot = list.ToArray();
        }

        /// <summary>The community whose roster holds this member id (entity ids are world-unique), or -1.</summary>
        public static int CommunityOfMember(int memberId)
        {
            CommunityState[] all = snapshot;
            for (int i = 0; i < all.Length; i++) if (all[i].Members.ContainsKey(memberId)) return all[i].Id;
            return -1;
        }

        /// <summary>Every community's members, community by community - for the world loops that mean "anyone's member".</summary>
        public static IEnumerable<Member> AllMembers()
        {
            CommunityState[] all = snapshot;
            for (int i = 0; i < all.Length; i++)
                foreach (Member m in all[i].Members.Values) yield return m;
        }

        /// <summary>Every community's outposts (the claim and seeding exclusions need the union).</summary>
        public static IEnumerable<Outpost> AllOutposts()
        {
            CommunityState[] all = snapshot;
            for (int i = 0; i < all.Length; i++)
                for (int j = 0; j < all[i].Outposts.Count; j++) yield return all[i].Outposts[j];
        }

        /// <summary>
        /// Runs act once per community, lowest id first, each inside that community's scope (ScRegistry.Use) - the world loops'
        /// enumerator. A copy of the list is walked, so act may create a community (it is not visited).
        /// </summary>
        public static void ForEach(Action<CommunityState> act)
        {
            if (act == null) return;
            CommunityState[] all = snapshot;
            for (int i = 0; i < all.Length; i++)
                using (ScRegistry.Use(all[i].Id)) act(all[i]);
        }
    }

    /// <summary>
    /// Round MP6 (core): the handle ScRegistry.Use returns. Dispose restores the scope that was current before it (pops this
    /// scope and any inner one left open), so "using (ScRegistry.Use(cid)) { ... }" restores it on an exception too. Dispose a
    /// handle once; default(ScScope) does nothing.
    /// </summary>
    public struct ScScope : IDisposable
    {
        /// <summary>The scope stack's depth to go back to, plus one (0: nothing to do).</summary>
        private readonly int restore;

        internal ScScope(int depth) { restore = depth + 1; }

        public void Dispose()
        {
            if (restore > 0) ScRegistry.PopScope(restore - 1);
        }
    }
}
