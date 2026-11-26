using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Platform;
using UnityEngine;

namespace SurvivorCommunity
{
    // round MP6 (part relations), 2026-11-26: what each community has with the world's groups - its standing with an enclave,
    // what it knows of an enclave, an infestation or a band, its band trust, its requests' and calls' clocks - kept as one row
    // per (community, world record), and the hostility that goes with it (the user's decisions 1, 4 and 5).
    //
    // How it fits together:
    // - Enclave, Infestation and Band keep their relation fields as properties (Enclave.Standing, .Known, ...); each reads and
    //   writes the row of ScRelations.Cid - the community in scope (ScRegistry.Current) on the server, row 0 on a client (its
    //   copy holds one community's view: the server projects that community's values into the fields a client reads). So code
    //   that runs in a community's scope (its ticker pass, an action from its player, a dialog on its player's machine) reads
    //   and writes that community's relation unchanged. World code that touches a relation (a finder, a kill, the world daily,
    //   a world event) opens the scope of the community concerned (ScRegistry.Use) or walks every row (Rels.Count / IdAt / At).
    // - Community 0's row always exists (Stage 1's values, and the fast path: with one community nothing else is ever made);
    //   another community's row is made by its first write, so a read of one that never touched the record gives the defaults
    //   (neutral, unknown, no clock running).
    // - The save: community 0's row stays in the record's own keys ([enclave] standing=, known=, ...: a one-community save is
    //   byte for byte what it was), each other community's is one packed line in the same section (rel=cid:...). A client's
    //   snapshot (BuildText(true)) writes the scope's row into the record's keys and no rel= line.
    // - Hostility: an enclave turned hostile to community c stamps HostileCvarFor(c) on its bodies (community 0's is the old
    //   %sc_enchostile) and puts (c, enclave) in ScEnclavePeople's faction lookup; the faction postfix finds the other side's
    //   community in O(1) (SideCid: a player through a snapshot of the membership map, a member through sc_member = community id
    //   + 1, a stand-in through its member, a hired follower through its leader). A drifter band against community c holds
    //   ScTrust's per-player grudge against c's players. A community's people hold a grudge against a player of another
    //   community who hit one of them (Grudges: saved, by platform id).
    // Main thread, except the faction lookups (SideCid, PlayerCid, MemberCid, GrudgePair), which read volatile snapshots.

    /// <summary>Round MP6 (part relations): one community's relation to one enclave (Enclave's relation properties read it).</summary>
    public sealed class EncRel
    {
        public int Standing;
        public bool Known;
        public int KnownDay = -1;
        public bool TurnedHostile;
        public int HostileDay = -1;
        public int NextRequestDay;
        public string Thanks = "";
        public int AllyRevealDay;
        public int GiftDay;
        public int BarterDay;
        public int CallDay;
        public string CallKind = "";
        public ulong CallAt;
        public int CallPid;
        public ulong CallUntil;
        // ---- session only (never saved): the attack rule's clocks against this community's players, a threat's grace ----
        public float LastHitAt = -1f, WarnedAt = -1f;
        public float GraceUntil;

        /// <summary>Anything a save keeps differs from a new row's (a row that is all defaults is not written).</summary>
        internal bool HasSaved
        {
            get
            {
                return Standing != 0 || Known || KnownDay != -1 || TurnedHostile || HostileDay != -1 || NextRequestDay != 0 || Thanks.Length > 0
                       || AllyRevealDay != 0 || GiftDay != 0 || BarterDay != 0 || CallDay != 0 || CallKind.Length > 0;
            }
        }
    }

    /// <summary>Round MP6 (part relations): one community's knowledge of one infestation.</summary>
    public sealed class InfRel
    {
        public bool Known;
        public int KnownDay = -1;

        internal bool HasSaved { get { return Known || KnownDay != -1; } }
    }

    /// <summary>Round MP6 (part relations): one community's relation to one band on the road.</summary>
    public sealed class BandRel
    {
        public bool Known;
        public int KnownDay = -1;
        /// <summary>Drifters: 0 wary, 1 trusting this community (helped in trouble), -1 against it (one of them hit or killed).</summary>
        public int Trust;
        // ---- session only ----
        /// <summary>The trouble's zombies this community's players killed.</summary>
        public int Helped;
        /// <summary>Raiders, or drifters against this community: they have one of its players in their sights (EngagedId).</summary>
        public bool Engaged;
        public int EngagedId = -1;

        internal bool HasSaved { get { return Known || KnownDay != -1 || Trust != 0; } }
    }

    /// <summary>
    /// Round MP6 (part relations): a world record's relation rows by community id - community 0's always there, another's made by
    /// its first write (Get). Copy on write: a reader on another thread never sees a list being changed.
    /// </summary>
    public sealed class RelRows<T> where T : class, new()
    {
        private readonly T zero = new T();
        private int[] ids = new int[0];
        private T[] rows = new T[0];

        /// <summary>Community 0's row (always there).</summary>
        public T Zero { get { return zero; } }

        /// <summary>The community's row, or null when it never had one (community 0: always its row).</summary>
        public T Peek(int cid)
        {
            if (cid == 0) return zero;
            int[] i = ids;
            for (int k = 0; k < i.Length; k++) if (i[k] == cid) return rows[k];
            return null;
        }

        /// <summary>The community's row, made when it has none (a negative id: community 0's).</summary>
        public T Get(int cid)
        {
            if (cid <= 0) return zero;
            T r = Peek(cid);
            if (r != null) return r;
            r = new T();
            int n = ids.Length;
            var ni = new int[n + 1];
            var nr = new T[n + 1];
            Array.Copy(ids, ni, n);
            Array.Copy(rows, nr, n);
            ni[n] = cid;
            nr[n] = r;
            rows = nr;
            ids = ni;
            return r;
        }

        /// <summary>How many rows there are (community 0's counts): walk them with IdAt / At, 0 first.</summary>
        public int Count { get { return ids.Length + 1; } }

        public int IdAt(int i) { return i == 0 ? 0 : ids[i - 1]; }

        public T At(int i) { return i == 0 ? zero : rows[i - 1]; }
    }

    /// <summary>Round MP6 (part relations): the relation rows' helpers, hostility between communities, and their save lines.</summary>
    public static class ScRelations
    {
        /// <summary>The defaults a never-touched row reads as (never written: the properties only read it).</summary>
        internal static readonly EncRel NoEnc = new EncRel();
        internal static readonly InfRel NoInf = new InfRel();
        internal static readonly BandRel NoBand = new BandRel();

        /// <summary>
        /// The community whose row the relation properties read and write: the one in scope on the server (ScRegistry.CurrentId);
        /// community 0's row on a client and while a save is being read (Loaded is false then) - a client's copy holds one
        /// community's view, and a save's record keys are community 0's.
        /// </summary>
        public static int Cid { get { return ScRegistry.Loaded ? ScRegistry.CurrentId : ScPlayers.SharedCommunity; } }

        /// <summary>Two or more communities exist (the server): only then does anything here differ from Stage 1.</summary>
        public static bool Several { get { return ScRegistry.Loaded && ScCommunities.Count > 1; } }

        // ------------------------------------------------------------------ a community's side in a fight ----

        /// <summary>Player entity id -> community, member id -> community: rebuilt every second while several communities exist.</summary>
        private static volatile Dictionary<int, int> playerCids = new Dictionary<int, int>();
        private static volatile Dictionary<int, int> memberCids = new Dictionary<int, int>();

        /// <summary>
        /// The player's community, for the faction lookups (any thread): community 0 while nobody is in another; a client's own
        /// (MirroredCommunity) on a client, for every player (a client knows no other's); else the snapshot, computed on the main
        /// thread when it misses (a player who just came), community 0 off it.
        /// </summary>
        public static int PlayerCid(EntityPlayer p)
        {
            if (p == null) return -1;
            if (!ScRegistry.Loaded) return ScRegistry.MirroredCommunity;
            if (ScRegistry.WorldState.Membership.Count == 0) return ScPlayers.SharedCommunity;
            int c;
            if (playerCids.TryGetValue(p.entityId, out c)) return c;
            if (ThreadManager.IsMainThread()) return ScPlayers.CommunityOf(p);
            return ScPlayers.SharedCommunity;
        }

        /// <summary>
        /// The community that holds this member id (any thread: the snapshot off the main thread); -1 for none. A client knows its
        /// own community's members only (its copy): theirs, else -1.
        /// </summary>
        public static int MemberCid(int memberId)
        {
            if (!ScRegistry.Loaded)
                return ScRegistry.MirroredCommunity == ScPlayers.SharedCommunity || ScRegistry.GetMember(memberId) != null ? ScRegistry.MirroredCommunity : -1;
            if (ScCommunities.Count <= 1) return ScPlayers.SharedCommunity;
            int c;
            if (memberCids.TryGetValue(memberId, out c)) return c;
            if (ThreadManager.IsMainThread()) return ScCommunities.CommunityOfMember(memberId);
            return -1;
        }

        /// <summary>
        /// The community a member or a stand-in belongs to, by the body (any thread): sc_member holds community id + 1 (round
        /// MP6 (part relations)), a stand-in carries its member's id. -1 for any other body.
        /// </summary>
        public static int MemberSideCid(EntityAlive e)
        {
            EntityBuffs bf = e != null ? e.Buffs : null;
            if (bf == null || e is EntityPlayer) return -1;
            float m = bf.GetCustomVar("sc_member");
            if (m > 0f) return (int)m - 1;
            if (bf.HasCustomVar(ScRadio.StandInCvar)) return MemberCid((int)bf.GetCustomVar(ScRadio.StandInCvar));
            return -1;
        }

        /// <summary>
        /// The community an entity stands for in a fight (the faction postfix, any thread): a player's, a member's or stand-in's, a
        /// player's hired follower's (their leader's); -1 for anyone else. With one community: 0 exactly for the bodies the old
        /// ScEnclavePeople.Opposes counted (a player, one of the community, a companion following a player).
        /// </summary>
        public static int SideCid(EntityAlive e)
        {
            if (e == null) return -1;
            EntityPlayer p = e as EntityPlayer;
            if (p != null) return PlayerCid(p);
            int c = MemberSideCid(e);
            if (c >= 0) return c;
            int leader = ScCommunity.HiredBy(e);
            if (leader <= 0) return -1;
            World w = ScCommunity.World;
            EntityPlayer lp;
            // the player list, never World.GetEntity (it would finish a pending build inside the AI's frame: the E2 review's L3)
            if (w == null || w.Players == null || !w.Players.dict.TryGetValue(leader, out lp) || lp == null) return -1;
            return PlayerCid(lp);
        }

        /// <summary>The community of the player with this entity id, online or not (persistent player data); -1 when unknown.</summary>
        public static int CommunityOfPlayerEntity(int entityId)
        {
            if (!ScRegistry.Loaded || ScRegistry.WorldState.Membership.Count == 0) return ScPlayers.SharedCommunity;
            EntityPlayer p = ScPlayers.ByEntity(entityId);
            if (p != null) return ScPlayers.CommunityOf(p);
            try
            {
                PersistentPlayerList pp = GameManager.Instance != null ? GameManager.Instance.persistentPlayers : null;
                PersistentPlayerData ppd = pp != null ? pp.GetPlayerDataFromEntityID(entityId) : null;
                PlatformUserIdentifierAbs id = ppd != null ? ppd.PrimaryId : null;
                return id != null ? ScPlayers.CommunityOfId(id.CombinedString) : -1;
            }
            catch (Exception e)
            {
                ScLog.Once("rel-player-entity", e);
                return -1;
            }
        }

        /// <summary>
        /// The community of a turret's or sensor's owner (the X10 filters): the membership map on the server; a client's own
        /// community on a client (it knows no other player's). Community 0 with no owner.
        /// </summary>
        public static int OwnerCid(PlatformUserIdentifierAbs owner)
        {
            if (!ScRegistry.Loaded) return ScRegistry.MirroredCommunity;
            if (owner == null || ScRegistry.WorldState.Membership.Count == 0) return ScPlayers.SharedCommunity;
            return ScPlayers.CommunityOfId(owner.CombinedString);
        }

        // ------------------------------------------------------------------ a community's grudge against a player (decision 4) ----

        /// <summary>(community id, player entity id) of every grudge whose player is online - what the faction postfix reads.</summary>
        private static volatile HashSet<long> grudgeKeys = new HashSet<long>();
        private static volatile bool anyGrudge;
        private static bool grudgesDirty = true;

        /// <summary>Any community holds a grudge against a player who is online: the faction postfix's first, cheapest test.</summary>
        public static bool AnyGrudge { get { return anyGrudge; } }

        private static long Key(int cid, int entityId) { return ((long)cid << 32) | (uint)entityId; }

        /// <summary>
        /// The faction postfix's widening (any thread, O(1)): one side is a player and the other a member or stand-in of a
        /// community that holds a grudge against that player (they hit one of its people).
        /// </summary>
        public static bool GrudgePair(EntityAlive a, EntityAlive b)
        {
            if (!anyGrudge || a == null || b == null) return false;
            EntityPlayer p = a as EntityPlayer;
            EntityAlive o = b;
            if (p == null)
            {
                p = b as EntityPlayer;
                o = a;
            }
            if (p == null || o == null || o is EntityPlayer) return false;
            int c = MemberSideCid(o);
            return c >= 0 && grudgeKeys.Contains(Key(c, p.entityId));
        }

        /// <summary>
        /// Whether community cid's people hold a grudge against this player (the console). Main thread. MP6 (part network): a
        /// client does not know a grudge against its player - its talk prompt still shows on that community's members.
        /// </summary>
        public static bool HoldsGrudge(int cid, EntityPlayer p)
        {
            CommunityState cs = ScCommunities.ById(cid);
            if (cs == null || p == null) return false;
            string id = ScPlayers.IdOf(p);
            return id.Length > 0 && cs.Grudges.Contains(id);
        }

        /// <summary>
        /// Patches_Trust's hit hook, before ScTrust's wild-survivor rule (host): a player's landed hit on a member or stand-in of
        /// ANOTHER community (the user's decision 4) - her community's people hold a grudge against that player from now on (the
        /// faction postfix: they fight back on sight, and that player's companions fight them). One tell to the hitter and one to
        /// the hit community, the log, a save. True for any member or stand-in body (handled here: never a wild survivor's grudge).
        /// With one community every member is the hitter's own: true, nothing changes (friendly fire decides those hits).
        /// </summary>
        public static bool OnMemberHit(EntityAlive npc, EntityPlayer player)
        {
            // one community: every member is the hitter's own - nothing here (ScTrust's rule runs exactly as before)
            if (npc == null || player == null || !Several) return false;
            int cid;
            Member m = ScRegistry.GetMember(npc.entityId);
            if (m != null) cid = m.CommunityId;
            else
            {
                cid = MemberSideCid(npc);
                if (cid < 0) return false;
            }
            int pc;
            if (!ScPlayers.TryCommunityOf(player, out pc) || pc == cid) return true;      // her own community's player (or unknown): no grudge
            CommunityState cs = ScCommunities.ById(cid);
            string id = ScPlayers.IdOf(player);
            if (cs == null || id.Length == 0 || cs.Grudges.Contains(id)) return true;
            cs.Grudges.Add(id);
            grudgesDirty = true;
            RebuildGrudges();
            string name = ScCommunity.DisplayName(npc);
            ScCommunity.TellKey(player, "scRelGrudgeHitter", "ui_denied", name);
            using (ScRegistry.Use(cid)) ScPlayers.TellCommunityKey("scRelGrudgeHit", "ui_denied", player.PlayerDisplayName, name);
            ScLog.Out(player.PlayerDisplayName + " (community " + pc + ") hit " + name + " of " + cs.Label + " - its people hold a grudge against them for good");
            ScRegistry.SaveAtomic();
            return true;
        }

        /// <summary>"sc trust community": forgives a community's grudge against a player (by platform id). True when there was one.</summary>
        public static bool Forgive(int cid, string platformId)
        {
            CommunityState cs = ScCommunities.ById(cid);
            if (cs == null || string.IsNullOrEmpty(platformId) || !cs.Grudges.Remove(platformId)) return false;
            grudgesDirty = true;
            RebuildGrudges();
            ScRegistry.SaveAtomic();
            return true;
        }

        /// <summary>The grudge lookup rebuilt from the communities' lists and the players online (main thread).</summary>
        private static void RebuildGrudges()
        {
            grudgesDirty = false;
            var s = new HashSet<long>();
            if (ScRegistry.Loaded)
            {
                List<EntityPlayer> all = ScPlayers.All;
                foreach (CommunityState cs in ScCommunities.All)
                {
                    if (cs.Grudges.Count == 0) continue;
                    for (int i = 0; i < all.Count; i++)
                    {
                        EntityPlayer p = all[i];
                        if (p == null) continue;
                        string id = ScPlayers.IdOf(p);
                        if (id.Length > 0 && cs.Grudges.Contains(id)) s.Add(Key(cs.Id, p.entityId));
                    }
                }
            }
            grudgeKeys = s;
            anyGrudge = s.Count > 0;
        }

        // ------------------------------------------------------------------ the tick ----

        private static int lastPlayers = -1, lastMembership = -1, lastCommunities = -1;

        /// <summary>
        /// ScTicker, once a second in the world steps: the faction snapshots (players' and members' communities, the grudges of
        /// the players online) rebuilt while several communities exist, cleared otherwise. Cheap with one community.
        /// </summary>
        public static void Tick()
        {
            try
            {
                if (!ScRegistry.Loaded) return;
                bool several = ScCommunities.Count > 1;
                int players = ScPlayers.All.Count, membership = ScRegistry.WorldState.Membership.Count;
                if (!several)
                {
                    if (playerCids.Count > 0) playerCids = new Dictionary<int, int>();
                    if (memberCids.Count > 0) memberCids = new Dictionary<int, int>();
                }
                else
                {
                    var pc = new Dictionary<int, int>();
                    List<EntityPlayer> all = ScPlayers.All;
                    int c;
                    for (int i = 0; i < all.Count; i++)
                        if (all[i] != null && ScPlayers.TryCommunityOf(all[i], out c)) pc[all[i].entityId] = c;
                    playerCids = pc;
                    var mc = new Dictionary<int, int>();
                    foreach (CommunityState cs in ScCommunities.All)
                        foreach (int id in cs.Members.Keys) mc[id] = cs.Id;
                    memberCids = mc;
                }
                // the grudges follow who is online (a player's entity id is only known while they are)
                if (grudgesDirty || players != lastPlayers || membership != lastMembership || ScCommunities.Count != lastCommunities) RebuildGrudges();
                lastPlayers = players;
                lastMembership = membership;
                lastCommunities = ScCommunities.Count;
            }
            catch (Exception e)
            {
                ScLog.Once("rel-tick", e);
            }
        }

        /// <summary>A load or a reset: the snapshots start empty, the grudges are read again on the next tick.</summary>
        public static void Reset()
        {
            playerCids = new Dictionary<int, int>();
            memberCids = new Dictionary<int, int>();
            grudgeKeys = new HashSet<long>();
            anyGrudge = false;
            grudgesDirty = true;
            lastPlayers = lastMembership = lastCommunities = -1;
        }

        // ------------------------------------------------------------------ wild survivors (decision 5) ----

        /// <summary>
        /// CanRecruit (the user's decision 5): this wild survivor holds a grudge against a player of community cid (ScTrust's
        /// per-player cvars). With one community: any grudge at all, as before. A grudge whose player cannot be placed blocks.
        /// </summary>
        public static bool GrudgeBlocks(EntityAlive npc, int cid)
        {
            if (!ScTrust.IsHostileToAny(npc)) return false;
            if (!Several) return true;
            try
            {
                foreach (KeyValuePair<string, float> kv in npc.Buffs.CVars)
                {
                    int pid;
                    if (kv.Value <= 0f || !ScTrust.TryGrudgePlayer(kv.Key, out pid)) continue;
                    int pc = CommunityOfPlayerEntity(pid);
                    if (pc < 0 || pc == cid) return true;
                }
            }
            catch (Exception e)
            {
                ScLog.Once("rel-grudge-blocks", e);
                return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ finders ----

        /// <summary>
        /// The world ticks' finders (ScEnclaves.Tick, ScInfestations.Tick, ScBands): act once per community with a player online,
        /// lowest id first, inside its scope, with its anchor (the first community's: the ticker's own player). With one
        /// community: once, community 0, the ticker's player - exactly the old call.
        /// </summary>
        public static void ForEachOnline(EntityPlayer first, Action<EntityPlayer> act)
        {
            if (act == null) return;
            if (!Several)
            {
                act(first);
                return;
            }
            CommunityState[] all = ScCommunities.Array;
            int firstCid = ScPlayers.CommunityOf(first);
            for (int i = 0; i < all.Length; i++)
            {
                using (ScRegistry.Use(all[i].Id))
                {
                    EntityPlayer anchor = all[i].Id == firstCid && first != null ? first : ScPlayers.Anchor(all[i].Id);
                    if (anchor == null) continue;          // none of its players online: it finds nothing (and it is paused)
                    act(anchor);
                }
            }
        }

        // ------------------------------------------------------------------ the daily: each community's half ----

        /// <summary>
        /// ScDaily.Run, for a community whose rollover did not run the world daily (another community's did, earlier): the
        /// community halves of the world daily's relation steps, in their old order - the radio's enclave reveal, the day's
        /// requests (lapses, expiries, the roll, the siege roll), allies' bundles and words. With one community never called
        /// (its rollover always runs the world daily, community halves included).
        /// </summary>
        public static void DailyCommunity(int day, EntityPlayer player, StringBuilder summary)
        {
            ScEnclaves.Daily(day, player);                                  // E1: this community's radio reveal
            ScEncRequests.DailyCommunity(day, player);                      // E4: this community's requests (the clock is the world's)
            ScEncAllies.Daily(day, player, summary, false);                 // E6: allies' bundles and words (refills are the world's)
        }

        // ------------------------------------------------------------------ the save ----

        private static string Clean(string s)
        {
            if (s == null) return "";
            return s.Replace("\r", "").Replace("\n", " ").Replace("=", "-").Replace(";", ",").Replace(":", "").Trim();
        }

        private static int I(string s)
        {
            int v;
            return int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : 0;
        }

        private static ulong U(string s)
        {
            ulong v;
            return ulong.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : 0UL;
        }

        private static string S(int v) { return v.ToString(CultureInfo.InvariantCulture); }

        /// <summary>
        /// [enclave] rel= lines, one per community but 0 with anything to keep:
        /// rel=cid:standing:known:knownday:turned:nextreq:allyreveal:gift:barter:callday:thanks:callkind:callat:callpid:calluntil
        /// (turned: the hostile day, -1 never). Community 0's row is the section's own keys.
        /// </summary>
        internal static void WriteEnclaveRows(StringBuilder sb, Enclave e)
        {
            RelRows<EncRel> rows = e.Rels;
            for (int i = 1; i < rows.Count; i++)
            {
                EncRel r = rows.At(i);
                if (!r.HasSaved) continue;
                sb.Append("rel=").Append(S(rows.IdAt(i))).Append(':').Append(S(r.Standing)).Append(':').Append(r.Known ? '1' : '0').Append(':')
                  .Append(S(r.KnownDay)).Append(':').Append(S(r.TurnedHostile ? Math.Max(0, r.HostileDay) : -1)).Append(':').Append(S(r.NextRequestDay)).Append(':')
                  .Append(S(r.AllyRevealDay)).Append(':').Append(S(r.GiftDay)).Append(':').Append(S(r.BarterDay)).Append(':').Append(S(r.CallDay)).Append(':')
                  .Append(Clean(r.Thanks)).Append(':').Append(Clean(r.CallKind)).Append(':').Append(r.CallAt.ToString(CultureInfo.InvariantCulture)).Append(':')
                  .Append(S(r.CallPid)).Append(':').Append(r.CallUntil.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine();
            }
        }

        /// <summary>One [enclave] rel= line into its community's row (a missing field keeps the default; an unknown call kind is dropped).</summary>
        internal static void ParseEnclaveRow(Enclave e, string val)
        {
            string[] f = val.Split(':');
            int cid = I(f[0]);
            if (cid <= 0) return;                      // community 0's row is the section's own keys
            EncRel r = e.Rels.Get(cid);
            if (f.Length > 1) r.Standing = Mathf.Clamp(I(f[1]), -100, 100);
            if (f.Length > 2) r.Known = f[2].Trim() == "1";
            if (f.Length > 3) r.KnownDay = I(f[3]);
            if (f.Length > 4) { int d = I(f[4]); r.TurnedHostile = d >= 0; r.HostileDay = d; }
            if (f.Length > 5) r.NextRequestDay = I(f[5]);
            if (f.Length > 6) r.AllyRevealDay = Math.Max(0, I(f[6]));
            if (f.Length > 7) r.GiftDay = Math.Max(0, I(f[7]));
            if (f.Length > 8) r.BarterDay = Math.Max(0, I(f[8]));
            if (f.Length > 9) r.CallDay = Math.Max(0, I(f[9]));
            if (f.Length > 10) r.Thanks = f[10].Trim().ToLowerInvariant();
            if (f.Length > 14)
            {
                string kind = f[11].Trim().ToLowerInvariant();
                if (kind == ScEncCalls.Fighter || kind == ScEncCalls.Drop)
                {
                    r.CallKind = kind;
                    r.CallAt = U(f[12]);
                    r.CallPid = I(f[13]);
                    r.CallUntil = kind == ScEncCalls.Fighter ? U(f[14]) : 0UL;
                }
            }
        }

        /// <summary>[infestation] rel=cid:known:knownday, one per community but 0 that knows of it.</summary>
        internal static void WriteInfestationRows(StringBuilder sb, Infestation inf)
        {
            RelRows<InfRel> rows = inf.Rels;
            for (int i = 1; i < rows.Count; i++)
            {
                InfRel r = rows.At(i);
                if (r.HasSaved) sb.AppendLine("rel=" + S(rows.IdAt(i)) + ":" + (r.Known ? "1" : "0") + ":" + S(r.KnownDay));
            }
        }

        internal static void ParseInfestationRow(Infestation inf, string val)
        {
            string[] f = val.Split(':');
            int cid = I(f[0]);
            if (cid <= 0) return;
            InfRel r = inf.Rels.Get(cid);
            if (f.Length > 1) r.Known = f[1].Trim() == "1";
            if (f.Length > 2) r.KnownDay = I(f[2]);
        }

        /// <summary>[band] rel=cid:known:knownday:trust, one per community but 0 with anything to keep.</summary>
        internal static void WriteBandRows(StringBuilder sb, Band b)
        {
            RelRows<BandRel> rows = b.Rels;
            for (int i = 1; i < rows.Count; i++)
            {
                BandRel r = rows.At(i);
                if (r.HasSaved) sb.AppendLine("rel=" + S(rows.IdAt(i)) + ":" + (r.Known ? "1" : "0") + ":" + S(r.KnownDay) + ":" + S(r.Trust));
            }
        }

        internal static void ParseBandRow(Band b, string val)
        {
            string[] f = val.Split(':');
            int cid = I(f[0]);
            if (cid <= 0) return;
            BandRel r = b.Rels.Get(cid);
            if (f.Length > 1) r.Known = f[1].Trim() == "1";
            if (f.Length > 2) r.KnownDay = I(f[2]);
            if (f.Length > 3) r.Trust = Mathf.Clamp(I(f[3]), -1, 1);
        }

        /// <summary>[community] grudge=&lt;platform id&gt;, one line per player its people hold a grudge against (never to a client).</summary>
        internal static void WriteGrudges(StringBuilder sb, Community c, bool forClients)
        {
            if (forClients || c == null) return;
            CommunityState cs = ScCommunities.ById(c.Id);
            if (cs == null || cs.Grudges.Count == 0) return;
            var ids = new List<string>(cs.Grudges);
            ids.Sort(StringComparer.Ordinal);
            foreach (string id in ids) sb.AppendLine("grudge=" + id.Replace("\r", "").Replace("\n", "").Replace("=", "-").Replace(";", ",").Trim());
        }

        internal static void ParseGrudge(Community c, string val)
        {
            CommunityState cs = c != null ? ScCommunities.ById(c.Id) : null;
            if (cs != null && !string.IsNullOrEmpty(val)) cs.Grudges.Add(val.Trim());
            grudgesDirty = true;
        }

        // ------------------------------------------------------------------ the console ----

        /// <summary>sc enclave info / sc infest info: every community's row of a record that has one ("community 1: ...").</summary>
        internal static void RowLines(Enclave e, List<string> into)
        {
            if (e == null || into == null || ScCommunities.Count <= 1) return;
            RelRows<EncRel> rows = e.Rels;
            for (int i = 0; i < rows.Count; i++)
            {
                EncRel r = rows.At(i);
                into.Add("  community " + rows.IdAt(i) + ": standing " + r.Standing + (r.TurnedHostile ? ", hostile since day " + r.HostileDay : "")
                         + (r.Known ? ", known since day " + r.KnownDay : ", not found") + (r.CallKind.Length > 0 ? ", call " + r.CallKind : "")
                         + (r.Thanks.Length > 0 ? ", thanks owed (" + r.Thanks + ")" : ""));
            }
        }
    }
}
