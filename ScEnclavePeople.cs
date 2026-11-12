using System;
using System.Collections.Generic;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// One of an enclave's people (E2, Enclaves_Plan.md 2.3): a registry record, never in ScRegistry.Members (relink,
    /// consumption, morale and the roster walk that dictionary). The person themself is a Member (Who) - class, name, look,
    /// weapons, ammo, skills, traits, specialty, mood - so ScSpawn.Body rebuilds them like any known survivor and E3's
    /// recruit hands the same record to the community (ScCommunity.Admit). Who.Id and Who.Born stay 0: a body is never
    /// saved, and the one in the world this session is EntityId.
    /// </summary>
    public sealed class EncPerson
    {
        public const string Leader = "leader", MemberRole = "member";
        /// <summary>
        /// At the enclave, dead, or (E3) enlisted: following the player for a few days (ScEnclaveDeals). An enlisted person is
        /// alive - Living, the fall rule, the group size and the recruit minimum count them - but their body is the player's
        /// follower, not one of the POI's: the spawn, the despawn rule, the daily restock, the spot search and succession
        /// leave them alone. A recruited person leaves the records altogether.
        /// E5: missing - the subject of their enclave's open "missing survivor" request (ScEncSearch): not at home and not
        /// Living (the fall rule, the group's count and the recruit minimum leave them out), their spot kept for their return;
        /// a body of theirs is the search's (waiting where they were found, or following the player), never the POI's. They
        /// come home (home) or are lost (dead) when the request ends.
        /// E10: raiding - one of a hostile group's party raiding an outpost (ScEncRaids): alive (Living, the fall rule and the
        /// group count see them), not at home (no spawn at their spot, no restock, no succession); their body is the raid's,
        /// or none while it is off the map. Home again when the raid ends; saved as home (bodies never outlive a load).
        /// </summary>
        public const string StateHome = "home", StateDead = "dead", StateEnlisted = "enlisted", StateMissing = "missing", StateRaiding = "raiding";

        public int Id;
        public int EnclaveId;
        public string Role = MemberRole;
        public string State = StateHome;
        public int DiedDay = -1;
        /// <summary>
        /// E3, while enlisted: the player's entity id, the world time they go home and their body's id (a load always finds
        /// them home; after a crash the player's hired_* for that body is cleared: ScEnclaveDeals.SyncOnce).
        /// </summary>
        public int EnlistedBy = -1;
        public ulong EnlistedUntil;
        public int EnlistedBody = -1;
        public Member Who = new Member();
        /// <summary>The home spot (the cell the body stands in) and the way they face there; unset until their first spawn.</summary>
        public Vector3i Home;
        public bool HasHome;
        public float Yaw;
        /// <summary>Health as a share of the body's maximum (0..1), recorded when the body goes.</summary>
        public float Health = 1f;
        /// <summary>The rounds and magazines they were made with: what the daily restock gives back (EnclaveDailyRestock).</summary>
        public Dictionary<string, int> AmmoStart;
        public string AmmoStartMags;

        // ---- session only (never saved) ----
        /// <summary>The body in the world (-1: none) and its birth time (the Member.Born idiom: a recycled id is not theirs).</summary>
        public int EntityId = -1;
        public ulong EntityBorn;
        /// <summary>The id of the body that went last (-1: none): no new body while a chunk is still building a copy under it.</summary>
        public int LastEntityId = -1;
        /// <summary>A daily restock came while the body was out: its rounds are refilled when it goes.</summary>
        public bool RestockDue;
        /// <summary>Bodies that could not be built in a row (a class that is gone): skipped after a few, so the others still come.</summary>
        public int SpawnFailures;
        /// <summary>
        /// E3: a body walking home after the player let them go (ScEnclaveDeals.SendHome) - no watchdog until it is back on
        /// its spot. LeaveTarget: the next point on a walk toward a POI that is out of reach (zero: straight to the spot);
        /// LeavingSince: when the walk began (real time).
        /// </summary>
        public bool Leaving;
        public Vector3 LeaveTarget;
        public float LeavingSince;

        public bool IsLeader { get { return Role == Leader; } }
        /// <summary>E3: alive - at home or enlisted with the player (E10: or out raiding an outpost).</summary>
        public bool Alive { get { return State == StateHome || State == StateEnlisted || State == StateRaiding; } }
        public bool Enlisted { get { return State == StateEnlisted; } }
        /// <summary>E5: the subject of a missing survivor request (ScEncSearch).</summary>
        public bool Missing { get { return State == StateMissing; } }
        /// <summary>E10: out with their group's raid party (ScEncRaids).</summary>
        public bool Raiding { get { return State == StateRaiding; } }
        public string Name { get { return Who.Name; } }
        public Vector3 HomeCentre { get { return new Vector3(Home.x + 0.5f, Home.y, Home.z + 0.5f); } }
    }

    /// <summary>
    /// Enclave people, milestone E2 (E2_SPEC.md; Enclaves_Plan.md 2.3, 2.5, 2.6 and 6; research enclave_people.md 1-3 and 8):
    /// the records of each enclave's people and the bodies SC builds for them while a player is near.
    ///
    /// Records first, bodies on demand. The first time a player comes within EnclaveSpawnRange of an enclave its group is
    /// made by spawning strangers of SC's own wild classes for the gamestage tier and keeping what they rolled (look, weapon
    /// pair, ammo, profile, name), one body a tick, leader first. From then on each body is rebuilt from its record
    /// (ScSpawn.Body), one a tick across all enclaves, while a player is within EnclaveSpawnRange of the POI and its chunks
    /// are settled - never popping up within EnclaveNoPopRange in front of anyone. Bodies are Dynamic and never written to a
    /// chunk (the IsSavedToFile veto: talking to one or hitting it writes Persist), hold the Guard order at their home spot
    /// and run the SCEnclave AI package (Config/utilityai.xml; UaiOps' area abstraction). They go - health and rounds
    /// recorded - when no player is within EnclaveSpawnRange + EnclaveDespawnMargin and none sees them, when a chunk unloads
    /// under them, and when the world shuts down; a copy that comes back from a save anyway (a crash) is removed (Orphans).
    /// The POI's chunks are held while they are out, so a player at the edge of the view distance does not rebuild them.
    ///
    /// Deaths are real: a body that dies marks its person dead, a dead leader is replaced by the best of the rest, and an
    /// enclave with nobody left falls (ScEnclaves.Fall) - once its whole group was made: deaths while it is still being made
    /// leave the rest to come. The daily restock heals the living and refills their rounds.
    /// Also here: the hostility lookups (the faction postfix, X9, X10), the wild-spawn rule (no biome-spawned NPC within
    /// EnclaveWildSpawnMargin of a living enclave) and the container lock's owner.
    ///
    /// E3 (ScEnclaveDeals: trade, enlisting, recruiting) hooks the body lifecycle here: a leader's body is dressed as their
    /// enclave's trader and its stock recorded when it goes (Dress, Unbody); an enlisted body is the player's follower, so the
    /// tick neither despawns, watches nor counts it (ScEnclaveDeals.TickEnlisted) and a body that goes in any way ends the
    /// enlistment (Unbody); one sent home walks back unwatched (Leaving, ScEnclaveDeals.TickLeaving); a person's favour
    /// follows each new body (Dress, Unbody); a death while enlisted costs standing (Died); succession passes over one
    /// enlisted (Succeed, EnsureLeader); a recruit leaves the records with their body (LeaveEnclave).
    ///
    /// E5 (ScEncSearch, ScEncSiege) hooks in too: a missing person's body is built by the search wherever they are found
    /// (SpawnAt: dressed as every body, but with no spot of theirs and no SCEnclave package) and ticked by it (the tick
    /// hands it over: ScEncSearch.TickBody); it goes as every body does (the veto, the unload capture, the shutdown), and
    /// a body that goes or dies tells the search first (Unbody, OnBodyDied). One brought home is an enclave body again on
    /// the spot (Rehome). The spot finder works on any POI (SpotsIn), and a siege brings everyone out at once (SpawnAll).
    ///
    /// E10 (ScEncRaids) hooks in the same way: a raiding person's body is built at the outpost (SpawnAt; a group never met
    /// makes its people there, GenerateAway) and ticked by the raid (ScEncRaids.TickBody); one that goes alive leaves them
    /// raiding off the map, one that dies counts as the raid's loss (Unbody, Died); a group that falls mid-raid leaves a
    /// raider's body in the fight as a plain raider (DropPeople, Abandon); an unattended raid that was held costs them a
    /// record (KillRecord) or a place in a group not yet made (Enclave.RaidLost, GroupSize).
    ///
    /// E12 (ScEncJoin): a whole group joins the player. Every person leaves the records (LeaveEnclave); a body of theirs out
    /// now either joins with them (EnclaveJoinArrive follow: E3's recruit shape) or walks off toward the player's home as a
    /// husk (Husk: no person's any more, still vetoed) and goes once nobody sees it. The group then ends (ScEnclaves.Disband).
    ///
    /// Host only, main thread; the save thread's veto and the faction check read volatile snapshots rebuilt here.
    /// </summary>
    public static class ScEnclavePeople
    {
        /// <summary>On every body: its enclave's id and its person's id ('%': synced to clients, which X9 and X10 read).</summary>
        public const string EncCvar = "%sc_enc";
        public const string PersonCvar = "%sc_encp";
        /// <summary>On every body of an enclave turned hostile (X9 hides talk, X10 turrets shoot: both run on clients too).</summary>
        public const string HostileCvar = "%sc_enchostile";
        /// <summary>The AI package added at spawn (the class's list never has it; a body is never reloaded, so once is enough).</summary>
        public const string Package = "SCEnclave";
        private const string Salt = "sc.encpeople.v1";
        /// <summary>EntityNPC is not drawn beyond 90 m: a body further from every player is out of sight (ScRunners' rule).</summary>
        private const float NoDrawRange = 90f;
        /// <summary>A POI held loaded is held at most this many chunks round its centre (ScHomeHold's 10 is for a whole base).</summary>
        private const int MaxHoldDim = 6;
        /// <summary>Home spots stay this far apart (ScCommunity's post spacing); spread beyond SpreadCap no longer counts.</summary>
        private const float MinSpacing = 3f, SpreadCap = 8f;
        /// <summary>The ground-floor scan for spots: a column every ScanStep blocks, at most MaxScanColumns per enclave and session.</summary>
        private const int ScanStep = 3, MaxScanColumns = 600;
        /// <summary>The watchdog's far rule: further than this past the leash (EnclaveLeashMargin) for FarSeconds, a body is moved home.</summary>
        private const float FarExtra = 14f;
        private const int FarSeconds = 90;
        private const int MaxStuckShift = 5;
        internal const int MaxSpawnFailures = 3;
        /// <summary>How long "sc enclave despawn" keeps them away while the player stays near.</summary>
        private const float ConsoleDespawnSeconds = 30f;
        private const int DefaultMood = 50;
        private static readonly Vector3 Nudge = new Vector3(0f, 0.002f, 0f);
        private static readonly FastTags<TagGroup.Global> NpcTag = FastTags<TagGroup.Global>.Parse("npc");

        /// <summary>Entity id -> the person whose current body it is: the AI's and the hooks' O(1) lookup. Main thread.</summary>
        private static readonly Dictionary<int, EncPerson> bodies = new Dictionary<int, EncPerson>();
        /// <summary>
        /// Every enclave body built this session: id -> (class id, birth time). Swapped whole on the main thread; read by the
        /// IsSavedToFile veto, which runs on the chunk-save thread too. Entries stay for the session: an unloaded body is
        /// written after it is destroyed, and a corpse is never written either (its loot is the dropped backpack).
        /// </summary>
        private static volatile Dictionary<int, KeyValuePair<int, ulong>> vetoIds = new Dictionary<int, KeyValuePair<int, ulong>>();
        /// <summary>Enclave id -> its ground for the AI (UAI.ScArea), cached: it reads the record's live footprint.</summary>
        private static readonly Dictionary<int, UAI.ScArea> areas = new Dictionary<int, UAI.ScArea>();
        private static readonly Dictionary<int, Visit> visits = new Dictionary<int, Visit>();
        /// <summary>Bodies with %sc_enc that a chunk or a save brought back (the framework PostInit postfix): removed on the next tick.</summary>
        public static readonly HashSet<int> Orphans = new HashSet<int>();
        /// <summary>Enclaves turned hostile whose grace is over: what the faction postfix reads (any thread). Swapped whole.</summary>
        private static volatile HashSet<int> hostileActive = new HashSet<int>();
        private static volatile bool anyHostile;
        private static int capturingId = -1;
        private static World hooked;
        private static readonly List<EncPerson> scratch = new List<EncPerson>();

        // the wild-spawn rule: set inside SpawnManagerBiomes.SpawnUpdate on the main thread (per thread: a group roll
        // elsewhere meanwhile never sees them)
        [ThreadStatic] private static bool biomeSpawn;
        [ThreadStatic] private static bool refusedThisUpdate;
        [ThreadStatic] private static Vector3 noted;
        /// <summary>A biome spawn noted its position: ScLoadout's group-pick postfix asks FilterWild (one static read otherwise).</summary>
        [ThreadStatic] public static bool WildSpotNoted;
        private static int wildRefused;

        /// <summary>Session-only state per enclave.</summary>
        private sealed class Visit
        {
            /// <summary>The chunk observer holding the POI while its people are out (EnclaveHoldChunks).</summary>
            public ChunkManager.ChunkObserver Hold;
            /// <summary>When its chunks first read settled (spawns wait one full tick, as runners do); -1 when they do not.</summary>
            public float SettledSince = -1f;
            /// <summary>No spawn before this (real time): "sc enclave despawn".</summary>
            public float NoSpawnUntil;
            /// <summary>Home spot candidates (found once a session) and the spots that failed since.</summary>
            public List<Spot> Spots;
            public readonly HashSet<Vector3i> Bad = new HashSet<Vector3i>();
        }

        internal struct Spot
        {
            public Vector3i Pos;
            public float Yaw;
            /// <summary>0/1 a sleeper spawn point (strict / relaxed GoodPost), 2/3 a ground-floor scan's, 4 a ground spot (WanderSpot).</summary>
            public int Rank;
            public bool Roofed;
        }

        public static void Reset()
        {
            ReleaseHolds(null);
            visits.Clear();
            bodies.Clear();
            areas.Clear();
            Orphans.Clear();
            vetoIds = new Dictionary<int, KeyValuePair<int, ulong>>();
            hostileActive = new HashSet<int>();
            anyHostile = false;
            capturingId = -1;
            biomeSpawn = refusedThisUpdate = WildSpotNoted = false;
            wildRefused = 0;
            Unhook();
        }

        /// <summary>
        /// The registry was (re)loaded or reset (ScEnclaves.OnRegistryChanged): person ids squared, no body known (bodies are
        /// session-only), the hostility lookup rebuilt.
        /// </summary>
        public static void OnRegistryChanged()
        {
            ReleaseHolds(null);
            visits.Clear();
            bodies.Clear();
            areas.Clear();
            Community c = ScRegistry.Community;
            var ids = new HashSet<int>();
            int max = 0;
            foreach (EncPerson p in ScRegistry.EncPeople) if (p.Id > max) max = p.Id;
            int homeAgain = 0, raidersHome = 0;
            foreach (EncPerson p in ScRegistry.EncPeople)
            {
                p.EntityId = p.LastEntityId = -1;
                p.RestockDue = false;
                p.SpawnFailures = 0;
                p.Leaving = false;
                if (p.Id <= 0 || !ids.Add(p.Id))
                {
                    int old = p.Id;
                    p.Id = Math.Max(c.NextEncPersonId, max) + 1;
                    max = p.Id;
                    ids.Add(p.Id);
                    ScLog.Warn("enclave person '" + p.Name + "' had a duplicate id " + old + " - now " + p.Id);
                }
                if (p.Id > c.NextEncPersonId) c.NextEncPersonId = p.Id;
                // E3: no body outlives a load, so nobody is still with the player (the shutdown sent them home; a crash did not)
                if (p.Enlisted)
                {
                    p.State = EncPerson.StateHome;
                    p.EnlistedBy = -1;
                    p.EnlistedUntil = 0;
                    homeAgain++;
                }
                // E10: nor out raiding (the save writes them home; this is for a file edited by hand)
                if (p.Raiding) { p.State = EncPerson.StateHome; raidersHome++; }
            }
            if (homeAgain > 0) ScLog.Out("enclave people: " + homeAgain + " enlisted person(s) recorded as with the player are home again (bodies never outlive a load)");
            if (raidersHome > 0) ScLog.Out("enclave people: " + raidersHome + " person(s) recorded as out raiding are home again (bodies never outlive a load)");
            ScEnclaveDeals.OnRegistryChanged();     // E3: every favour parked (no body yet), the dead's dropped
            RebuildHostile();
        }

        // ------------------------------------------------------------------ lookups ----

        /// <summary>The person whose current body this is, or null (the AI asks every decision: one dictionary hit). Main thread.</summary>
        public static EncPerson PersonOf(EntityAlive self)
        {
            EncPerson p;
            return self != null && bodies.TryGetValue(self.entityId, out p) ? p : null;
        }

        /// <summary>An enclave's ground for the AI (its live footprint), cached per enclave; null for an enclave that is gone.</summary>
        public static UAI.ScArea AreaOf(int enclaveId)
        {
            UAI.ScArea a;
            if (areas.TryGetValue(enclaveId, out a)) return a;
            Enclave e = ScEnclaves.ById(enclaveId);
            if (e == null) return null;
            a = new UAI.ScArea(e);
            areas[enclaveId] = a;
            return a;
        }

        /// <summary>A person's enclave through the cached area (O(1) after the first lookup), or null when it is gone.</summary>
        public static Enclave EnclaveOf(EncPerson p)
        {
            UAI.ScArea a = p != null ? AreaOf(p.EnclaveId) : null;
            return a != null ? a.Enclave : null;
        }

        /// <summary>One of the enclaves' bodies, by its cvar (any machine: the side doors, X9).</summary>
        public static bool IsEnclaveNpc(EntityAlive e)
        {
            return e != null && e.Buffs != null && e.Buffs.HasCustomVar(EncCvar);
        }

        /// <summary>A body of an enclave turned hostile (the synced cvar: X9 asks on any machine - no talk from the moment it turns).</summary>
        public static bool IsHostileBody(Entity e)
        {
            EntityAlive a = e as EntityAlive;
            return a != null && a.Buffs != null && a.Buffs.GetCustomVar(HostileCvar) > 0f;
        }

        /// <summary>
        /// X10's turrets: a body of an enclave whose hostility is in force - on the host, once a threat's grace is over (the
        /// review's L2: the grace is for walking away from the talk, and turrets must not open fire in it); a client has no
        /// registry and goes by the synced cvar alone.
        /// </summary>
        public static bool HostileNow(Entity e)
        {
            if (!IsHostileBody(e)) return false;
            return !ScCommunity.IsHost || HostileBodyNow((EntityAlive)e);
        }

        /// <summary>
        /// The dialog card (XUiC_ScSnapshot): an enclave person's traits are known once their enclave is Friendly or better,
        /// as a stranger's are once their favour is done. Host only (the records).
        /// </summary>
        public static bool TraitsKnown(EntityAlive npc)
        {
            EncPerson p = PersonOf(npc);
            Enclave e = p != null ? EnclaveOf(p) : null;
            if (e == null || e.IsHostile) return false;
            string k = ScEnclaves.StandingKey(e);
            return k == "friendly" || k == "allied";
        }

        /// <summary>
        /// Thread-safe (the IsSavedToFile veto, through ScRunners.VetoChunkSave): an enclave body built this session - same id,
        /// class and birth time, so a stranger that inherited the id after a crash is saved as normal. Reference tests only:
        /// an unloaded body is written after its GameObject was destroyed.
        /// </summary>
        public static bool IsEnclaveBody(Entity e)
        {
            KeyValuePair<int, ulong> who;
            if ((object)e == null || !vetoIds.TryGetValue(e.entityId, out who)) return false;
            return e.entityClass == who.Key && (who.Value == 0 || e.WorldTimeBorn == 0 || e.WorldTimeBorn == who.Value);
        }

        private static void AddVeto(EntityAlive npc)
        {
            var s = new Dictionary<int, KeyValuePair<int, ulong>>(vetoIds);
            s[npc.entityId] = new KeyValuePair<int, ulong>(npc.entityClass, npc.WorldTimeBorn);
            vetoIds = s;
        }

        private static Visit VisitOf(int enclaveId)
        {
            Visit v;
            if (!visits.TryGetValue(enclaveId, out v)) visits[enclaveId] = v = new Visit();
            return v;
        }

        internal static EntityAlive BodyOf(EncPerson p)
        {
            World w = ScCommunity.World;
            return p != null && p.EntityId >= 0 && w != null ? ScNpc.As(ScNpc.Loaded(w, p.EntityId)) : null;
        }

        /// <summary>BodyOf, and only when it is still the body that person was given (its birth time): never a stranger that holds the id now.</summary>
        internal static EntityAlive OwnBody(EncPerson p)
        {
            EntityAlive npc = BodyOf(p);
            return npc != null && npc.WorldTimeBorn == p.EntityBorn ? npc : null;
        }

        public static List<EncPerson> PeopleOf(Enclave e)
        {
            var list = new List<EncPerson>();
            foreach (EncPerson p in ScRegistry.EncPeople) if (p.EnclaveId == e.Id) list.Add(p);
            return list;
        }

        /// <summary>The living: at home, or (E3) enlisted with the player, or (E10) out raiding - the fall rule, the group's count and the recruit minimum.</summary>
        public static List<EncPerson> Living(Enclave e)
        {
            var list = new List<EncPerson>();
            foreach (EncPerson p in ScRegistry.EncPeople) if (p.EnclaveId == e.Id && p.Alive) list.Add(p);
            return list;
        }

        /// <summary>The living leader, or null.</summary>
        public static EncPerson LeaderOf(Enclave e)
        {
            foreach (EncPerson p in ScRegistry.EncPeople) if (p.EnclaveId == e.Id && p.State == EncPerson.StateHome && p.IsLeader) return p;
            return null;
        }

        private static bool HasBodies(int enclaveId)
        {
            foreach (EncPerson p in bodies.Values) if (p.EnclaveId == enclaveId) return true;
            return false;
        }

        // ------------------------------------------------------------------ the 1 Hz driver ----

        /// <summary>ScTicker, every second, right after ScEnclaves.Tick: orphans, despawns, the watchdog, holds, then at most
        /// EnclaveSpawnPerTick new bodies across all enclaves (a group being made first, leader first).</summary>
        public static void Tick(World w, EntityPlayerLocal player)
        {
            try
            {
                if (!ScEnclaves.Ready || w == null || player == null || !ScRegistry.Loaded) return;
                Hook(w);
                ScEnclaveDeals.SyncOnce(player);        // E3: favour quests and a crashed session's hire squared with the records
                SweepOrphans(w);
                if (!ScConfig.Enclaves)
                {
                    // the records stay; no body is out and none comes (Enclaves=false)
                    if (bodies.Count > 0 || visits.Count > 0) DespawnAll(w, "Enclaves is off");
                    return;
                }
                if (ScEnclaves.All.Count == 0) return;
                Grace();
                float t = Time.unscaledTime;
                int budget = Math.Max(1, ScConfig.EnclaveSpawnPerTick);
                bool changed = false;
                foreach (Enclave e in ScEnclaves.All.ToArray())       // a copy: a fall takes one out of the list
                {
                    try
                    {
                        if (TickEnclave(w, e, t, ref budget)) changed = true;
                    }
                    catch (Exception ex)
                    {
                        ScLog.Once("encpeople-tick-" + e.Id, ex);
                    }
                }
                if (changed) ScRegistry.SaveAtomic();
            }
            catch (Exception e)
            {
                ScLog.Once("encpeople-tick", e);
            }
        }

        /// <summary>One enclave's second. True when a record changed and the registry wants saving.</summary>
        private static bool TickEnclave(World w, Enclave e, float t, ref int budget)
        {
            bool changed = false, went = false;
            float near = NearestPlayer(w, e);
            float keep = ScConfig.EnclaveSpawnRange + ScConfig.EnclaveDespawnMargin;
            int live = 0;
            if (HasBodies(e.Id))
            {
                scratch.Clear();
                foreach (EncPerson p in bodies.Values) if (p.EnclaveId == e.Id) scratch.Add(p);
                foreach (EncPerson p in scratch)
                {
                    if (ScNpc.IsPending(w, p.EntityId)) { live++; continue; }      // never forced (ours are built in place anyway)
                    EntityAlive npc = ScNpc.As(w.GetEntity(p.EntityId));
                    if (npc == null || npc.WorldTimeBorn != p.EntityBorn)
                    {
                        // gone without passing the unload handler or the death hook (another mod removed it, a fall out of the world);
                        // an id some other entity holds now keeps that entity's caches
                        ScLog.Warn(p.Name + " of " + e.Name + ": the body (entity " + p.EntityId + ") is gone - they come back from their record");
                        Unbody(p, npc == null);
                        changed = went = true;
                        continue;
                    }
                    if (npc.IsDead()) continue;                                     // the SetDead postfix took it
                    // E3: an enlisted body is the player's follower, not one of the POI's - never despawned, watched or
                    // counted for the hold; it goes home when its days are up or the player lets it go
                    if (p.Enlisted)
                    {
                        if (ScEnclaveDeals.TickEnlisted(w, e, p, npc)) changed = true;
                        continue;
                    }
                    // E5: one of theirs gone missing - the body is the search's (waiting where they were found, following the
                    // player home, or left over from before they went): its own rules, never the POI's
                    if (p.Missing)
                    {
                        if (ScEncSearch.TickBody(w, e, p, npc)) changed = true;
                        continue;
                    }
                    // E10: one of a raid party at an outpost - the raid's body (its target, its walk off once the raid is over),
                    // never despawned by distance, watched or counted for the hold while the raid is on
                    if (p.Raiding)
                    {
                        if (ScEncRaids.TickBody(w, e, p, npc)) changed = true;
                        continue;
                    }
                    if (e.Lost || (near > keep && !SeenByAny(w, npc.position)))
                    {
                        Despawn(p, npc);
                        changed = went = true;
                        continue;
                    }
                    live++;
                    // E3: one walking home after the player let them go is not watched until it is back on its ground
                    if (p.Leaving) { ScEnclaveDeals.TickLeaving(w, e, p, npc); continue; }
                    Watch(w, e, p, npc);
                }
                scratch.Clear();
                if (live == 0 && went) ScLog.Out("enclave " + e.Id + " '" + e.Name + "': its people went (no player within " + Mathf.RoundToInt(keep) + " m)");
            }
            UpdateHold(e, near, live > 0);
            if (budget <= 0 || e.Lost || near > ScConfig.EnclaveSpawnRange) return changed;
            Visit v = VisitOf(e.Id);
            if (t < v.NoSpawnUntil || !Settled(w, e, v, t)) return changed;
            // a group still being made: the next stranger first; when that one is held back (its spot in view, no class, no
            // spot) the people already made still come (the review's L1: a reload inside the building must not leave it empty)
            if (!e.PeopleRolled && Generate(w, e, false)) { budget--; return changed; }
            EncPerson next = NextToSpawn(w, e);
            if (next != null && Spawn(w, e, next, false)) budget--;
            return changed;
        }

        /// <summary>Horizontal metres from the nearest player to the footprint (0 inside it).</summary>
        private static float NearestPlayer(World w, Enclave e)
        {
            float best = float.MaxValue;
            List<EntityPlayer> list = w.Players != null ? w.Players.list : null;
            if (list == null) return best;
            for (int i = 0; i < list.Count; i++)
            {
                EntityPlayer p = list[i];
                if (p != null) best = Mathf.Min(best, e.DistanceXZ(p.position.x, p.position.z));
            }
            return best;
        }

        /// <summary>Vanilla's pop-in rule (RunNoPopInRange's idiom): within EnclaveNoPopRange and inside some player's view cone.</summary>
        internal static bool PopsInView(World w, Vector3 at)
        {
            float r = ScConfig.EnclaveNoPopRange;
            List<EntityPlayer> list = w.Players != null ? w.Players.list : null;
            if (r <= 0f || list == null) return false;
            for (int i = 0; i < list.Count; i++)
            {
                EntityPlayer p = list[i];
                if (p != null && !p.IsDead() && ScRunners.XZ(at, p.position) < r && p.IsInViewCone(at)) return true;
            }
            return false;
        }

        /// <summary>Some player may see it: within NoDrawRange and inside their view cone.</summary>
        internal static bool SeenByAny(World w, Vector3 at)
        {
            List<EntityPlayer> list = w.Players != null ? w.Players.list : null;
            if (list == null) return false;
            for (int i = 0; i < list.Count; i++)
            {
                EntityPlayer p = list[i];
                if (p != null && ScRunners.XZ(at, p.position) <= NoDrawRange && p.IsInViewCone(at)) return true;
            }
            return false;
        }

        /// <summary>The POI's chunks settled for one full tick (ScRunners' rule): spawns wait for it.</summary>
        private static bool Settled(World w, Enclave e, Visit v, float t)
        {
            if (!ChunksSettled(w, e)) { v.SettledSince = -1f; return false; }
            if (v.SettledSince < 0f) { v.SettledSince = t; return false; }
            return true;
        }

        /// <summary>
        /// ScRunners.Settled (3x3 chunks: loaded, decorated, lit, colliders built, not new this update) at the footprint's
        /// corners and centre, which together cover every chunk of a POI up to 80 m wide.
        /// </summary>
        internal static bool ChunksSettled(World w, Enclave e)
        {
            return BoxSettled(w, e.Poi, e.Size);
        }

        /// <summary>ChunksSettled for any footprint (E5: the building a missing survivor is searched for): its corners and centre.</summary>
        internal static bool BoxSettled(World w, Vector3i poi, Vector3i size)
        {
            int x0 = poi.x, x1 = poi.x + Math.Max(1, size.x) - 1, z0 = poi.z, z1 = poi.z + Math.Max(1, size.z) - 1;
            float cx = poi.x + size.x * 0.5f, cz = poi.z + size.z * 0.5f;
            return ScRunners.Settled(w, new Vector3(cx, 0f, cz))
                   && ScRunners.Settled(w, new Vector3(x0, 0f, z0)) && ScRunners.Settled(w, new Vector3(x1, 0f, z0))
                   && ScRunners.Settled(w, new Vector3(x0, 0f, z1)) && ScRunners.Settled(w, new Vector3(x1, 0f, z1));
        }

        /// <summary>
        /// The next person at home without a body: the leader first; one whose spot is in somebody's view waits. (E3: one
        /// enlisted with the player has their body at the player's side; their enlistment ends when it goes.)
        /// </summary>
        private static EncPerson NextToSpawn(World w, Enclave e)
        {
            EncPerson pick = null;
            foreach (EncPerson p in ScRegistry.EncPeople)
            {
                if (p.EnclaveId != e.Id || p.State != EncPerson.StateHome || p.EntityId >= 0 || p.SpawnFailures >= MaxSpawnFailures) continue;
                // a copy of their last body still being built (a save that kept it after all): never forced, never doubled -
                // the orphan sweep takes it once it is there
                if (p.LastEntityId >= 0 && ScNpc.IsPending(w, p.LastEntityId)) continue;
                if (p.HasHome && PopsInView(w, p.HomeCentre)) continue;
                if (p.IsLeader) return p;
                if (pick == null) pick = p;
            }
            return pick;
        }

        // ------------------------------------------------------------------ making the group ----

        /// <summary>
        /// The group's size: the POI's footprint side between EnclaveSizeMin and EnclaveSizeMax maps onto EnclaveGroupMin..Max
        /// (a raider enclave: EnclaveRaiderGroupMin..Max), plus or minus one from a roll seeded by the world and the enclave,
        /// clamped to the range; E10: less Enclave.RaidLost (people a held raid cost them before they were made), at least 1.
        /// </summary>
        public static int GroupSize(Enclave e)
        {
            bool raiders = e.Hostile;
            int lo = raiders ? ScConfig.EnclaveRaiderGroupMin : ScConfig.EnclaveGroupMin;
            int hi = Math.Max(lo, raiders ? ScConfig.EnclaveRaiderGroupMax : ScConfig.EnclaveGroupMax);
            int smin = ScConfig.EnclaveSizeMin, smax = Math.Max(smin + 1, ScConfig.EnclaveSizeMax);
            float t = Mathf.Clamp01((Math.Max(e.Size.x, e.Size.z) - smin) / (float)(smax - smin));
            int n = lo + Mathf.RoundToInt(t * (hi - lo));
            GameRandom r = GameRandomManager.Instance.CreateGameRandom(Seed(e, 0));
            try { n += r.RandomRange(-1, 2); }
            finally { GameRandomManager.Instance.FreeGameRandom(r); }
            // E10: people an unattended raid cost them before they were ever made (ScEncRaids) are never made; the leader stays
            return Math.Max(1, Mathf.Clamp(n, lo, hi) - Math.Max(0, e.RaidLost));
        }

        private static int Seed(Enclave e, int salt)
        {
            World w = ScCommunity.World;
            return (w != null ? w.Seed : 0) ^ Salt.GetStableHashCode() ^ unchecked(e.Id * 73856093) ^ unchecked(salt * 19349663);
        }

        /// <summary>
        /// The class of a new person: a weighted pick from SC's own wild group for the gamestage tier now (Enclaves_Plan.md
        /// 2.3: the tier wild spawns would have) - scSurvivorGroupGS01/50/100, or scRaiderGroupGS01/50/100 for a raider enclave,
        /// as spawning.xml uses them - so sex and class vary as wild spawns do. Seeded per enclave and person, so a pick that
        /// has to wait (the view rule) comes back the same. SC's own class of the tier when another mod took the group away.
        /// </summary>
        private static int RollClass(Enclave e, int index)
        {
            int tier = Mathf.Clamp(ScLoadout.GamestageTier(), 0, 2);
            string group = (e.Hostile ? "scRaiderGroupGS" : "scSurvivorGroupGS") + (tier == 0 ? "01" : tier == 1 ? "50" : "100");
            GameRandom r = GameRandomManager.Instance.CreateGameRandom(Seed(e, index + 1));
            try
            {
                List<SEntityClassAndProb> list;
                if (EntityGroups.list != null && EntityGroups.list.TryGetValue(group, out list) && list != null)
                {
                    float total = 0f;
                    foreach (SEntityClassAndProb c in list) if (Pickable(c)) total += c.prob;
                    if (total > 0f)
                    {
                        float pick = r.RandomFloat * total;
                        int last = 0;
                        foreach (SEntityClassAndProb c in list)
                        {
                            if (!Pickable(c)) continue;
                            last = c.entityClassId;
                            if (pick < c.prob) return c.entityClassId;
                            pick -= c.prob;
                        }
                        return last;
                    }
                }
                bool male = r.RandomFloat < 0.5f;
                string cls = e.Hostile ? ScXnpc.RaiderClassFor(male, tier) : ScXnpc.ClassFor(male, tier);
                return string.IsNullOrEmpty(cls) ? 0 : EntityClass.FromString(cls);
            }
            finally
            {
                GameRandomManager.Instance.FreeGameRandom(r);
            }
        }

        /// <summary>A group entry a person can be: not "none" (class 0), weighted, a class the game has.</summary>
        private static bool Pickable(SEntityClassAndProb c)
        {
            return c.entityClassId != 0 && c.prob > 0f && ScCommunity.ClassExists(c.entityClassId);
        }

        /// <summary>
        /// One more person for a group still being made: a stranger spawned at their spot, their record captured from what
        /// they rolled (ScCommunity.MemberFromBody, Join's own builder), then dressed as every later body is. The record is
        /// made with the body, so a visit cut short keeps the people already made and the rest come next time; never two
        /// records for one body. force: the console (the view rule ignored). True when a body was made.
        /// </summary>
        private static bool Generate(World w, Enclave e, bool force)
        {
            int size = GroupSize(e), have = 0;
            bool leader = false;
            foreach (EncPerson q in ScRegistry.EncPeople)
            {
                if (q.EnclaveId != e.Id) continue;
                have++;
                if (q.IsLeader && q.State == EncPerson.StateHome) leader = true;
            }
            if (have >= size)
            {
                e.PeopleRolled = true;
                ScRegistry.SaveAtomic();
                // those made died while it was still being made and it needs no more (a smaller group in the config since)
                if (Living(e).Count == 0 && !ScEncSearch.AnyAlive(e)) ScEnclaves.Fall(e, "everyone died");     // E5: not while one missing may be alive (L1)
                return false;
            }
            int cid = RollClass(e, have);
            if (!ScCommunity.ClassExists(cid))
            {
                ScLog.Once("encpeople-noclass-" + e.Id, "enclave " + e.Id + " '" + e.Name + "': no " + (e.Hostile ? "raider" : "survivor") + " class to make its people from - none made");
                return false;
            }
            var p = new EncPerson { EnclaveId = e.Id, Role = leader ? EncPerson.MemberRole : EncPerson.Leader };
            if (!AssignSpot(w, e, p)) return false;
            if (!force && PopsInView(w, p.HomeCentre)) return false;
            EntityAlive npc;
            try
            {
                // a stranger: PostInit rolls the look, the weapon pair, the ammo and (a survivor) the profile, as for a wild spawn
                npc = ScSpawn.Create(cid, p.HomeCentre + Nudge, new Vector3(0f, p.Yaw, 0f), null, kind: e.Hostile ? "raider enclave" : "enclave");
                if (npc == null) { ScLog.Once("encpeople-create-" + e.Id, "enclave " + e.Id + ": the factory made no body of class " + cid); return false; }
                w.SpawnEntityInWorld(npc);
            }
            catch (Exception ex)
            {
                ScLog.Once("encpeople-generate-" + e.Id, ex);
                return false;
            }
            Capture(p, npc);
            Dress(e, p, npc);
            if (have + 1 >= size) e.PeopleRolled = true;
            ScRegistry.SaveAtomic();
            ScLog.Out("enclave " + e.Id + " '" + e.Name + "': " + (p.IsLeader ? "the leader " : "") + p.Name + " (" + p.Who.Class + ", p" + p.Id + ") joins the group at "
                      + p.Home + " - " + (have + 1) + " of " + size + (e.PeopleRolled ? ", the group is made" : ""));
            return true;
        }

        /// <summary>
        /// A new person's record from the stranger's body that was spawned for it (Generate; E10: GenerateAway): what it rolled -
        /// class, name, look, weapons, rounds, profile (ScCommunity.MemberFromBody, Join's own builder), a raider's stars as its
        /// Shooting - with a new person id, the rounds it was made with, into the records.
        /// </summary>
        private static void Capture(EncPerson p, EntityAlive npc)
        {
            Member m = ScCommunity.MemberFromBody(npc, DefaultMood);
            m.Id = 0;                    // never a member and never saved: the body's id lives in EntityId for the session
            m.Born = 0;
            if (m.Look != null) m.Look = m.Look.Clone();     // their own copy, not the session cache's
            if (ScXnpc.IsRaider(npc))
            {
                // a raider has no profile: the stars it rolled from its tier at spawn are its Shooting from now on
                string src;
                SkillState sh = m.Skill(Skill.Shooting);
                sh.Level = ScShooting.Stars(npc, out src);
                sh.Xp = ScSkills.NeededFor(sh.Level);
            }
            p.Who = m;
            p.Id = ++ScRegistry.Community.NextEncPersonId;
            p.AmmoStart = ScAmmo.Copy(m.Ammo);
            p.AmmoStartMags = m.Mags;
            ScRegistry.EncPeople.Add(p);
        }

        /// <summary>
        /// E10: one more person for a group still being made, made away from home (ScEncRaids.SpawnParty: a den never visited
        /// raids an outpost): a stranger of the class the next one at home would have been (RollClass by the records so far, as
        /// Generate) spawned at 'at', its record captured as Generate's, a member (the leader's place stays at home) with no spot
        /// until their first spawn there, dressed as a body away from home (SpawnAt's). Null when no body could be made. The
        /// caller sets their state and saves.
        /// </summary>
        internal static EntityAlive GenerateAway(World w, Enclave e, Vector3 at, float yaw, out EncPerson made)
        {
            made = null;
            if (w == null || e == null) return null;
            int have = 0;
            foreach (EncPerson q in ScRegistry.EncPeople) if (q.EnclaveId == e.Id) have++;
            int cid = RollClass(e, have);
            if (!ScCommunity.ClassExists(cid))
            {
                ScLog.Once("encpeople-noclass-" + e.Id, "enclave " + e.Id + " '" + e.Name + "': no " + (e.Hostile ? "raider" : "survivor") + " class to make its people from - none made");
                return null;
            }
            EntityAlive npc;
            try
            {
                npc = ScSpawn.Create(cid, at + Nudge, new Vector3(0f, yaw, 0f), null, kind: e.Hostile ? "raider enclave" : "enclave");
                if (npc == null) { ScLog.Once("encpeople-create-" + e.Id, "enclave " + e.Id + ": the factory made no body of class " + cid); return null; }
                w.SpawnEntityInWorld(npc);
            }
            catch (Exception ex)
            {
                ScLog.Once("encpeople-generateaway-" + e.Id, ex);
                return null;
            }
            var p = new EncPerson { EnclaveId = e.Id, Role = EncPerson.MemberRole };
            Capture(p, npc);
            Dress(e, p, npc, false);
            ScLog.Out("enclave " + e.Id + " '" + e.Name + "': " + p.Name + " (" + p.Who.Class + ", p" + p.Id + ") is one of the group, met away from home at "
                      + World.worldToBlockPos(at) + " - " + (have + 1) + " of " + GroupSize(e));
            made = p;
            return npc;
        }

        /// <summary>
        /// A known person's body: their record rebuilt at their spot (ScSpawn.Body: the class, look, weapon pair, rounds and
        /// name). The spot is checked first and replaced when it is no longer one. force: the console (the view rule ignored).
        /// </summary>
        private static bool Spawn(World w, Enclave e, EncPerson p, bool force)
        {
            bool fresh;
            if (!EnsureSpot(w, e, p, out fresh))
            {
                p.SpawnFailures++;
                ScLog.Once("encpeople-nospot-" + p.Id, p.Name + " of " + e.Name + ": no spot to stand in the POI - they stay off the map for now");
                return false;
            }
            if (fresh) ScRegistry.SaveAtomic();
            if (!force && PopsInView(w, p.HomeCentre)) return false;
            EntityAlive npc = null;
            try { npc = ScSpawn.Body(w, p.Who, p.HomeCentre + Nudge, new Vector3(0f, p.Yaw, 0f), false); }
            catch (Exception ex) { ScLog.Once("encpeople-spawn-" + p.Id, ex); }
            if (npc == null)
            {
                p.SpawnFailures++;
                ScLog.Once("encpeople-nobody-" + p.Id, p.Name + " of " + e.Name + ": no body for the class " + p.Who.Class + " (gone from the game?) - they stay off the map");
                return false;
            }
            Dress(e, p, npc);
            ScLog.Verbose("enclave " + e.Id + " '" + e.Name + "': " + p.Name + " (p" + p.Id + ") is back at " + p.Home + " (entity " + npc.entityId + ")");
            return true;
        }

        /// <summary>
        /// E5: a missing person's body where the search found them (ScEncSearch): their record rebuilt (ScSpawn.Body) and
        /// dressed as every body is - never saved, the cvars, health, the lookups and the veto - but not as one at home: no
        /// Guard order at their spot and no SCEnclave package (the caller gives the order where they wait). Null when no body
        /// could be made (the class is gone). E9's called fighter and E10's raiders come this way too.
        /// </summary>
        internal static EntityAlive SpawnAt(World w, Enclave e, EncPerson p, Vector3 at, float yaw)
        {
            EntityAlive npc = null;
            try { npc = ScSpawn.Body(w, p.Who, at + Nudge, new Vector3(0f, yaw, 0f), false); }
            catch (Exception ex) { ScLog.Once("encpeople-spawnat-" + p.Id, ex); }
            if (npc == null) return null;
            Dress(e, p, npc, false);
            return npc;
        }

        /// <summary>
        /// E5: a body that came home with the player (ScEncSearch) becomes one of the POI's again: a spot of theirs (theirs
        /// when it is still one, else a new one), the Guard order there, the SCEnclave package, and a leader for a group that
        /// has none.
        /// </summary>
        internal static void Rehome(Enclave e, EncPerson p, EntityAlive npc)
        {
            World w = ScCommunity.World;
            bool fresh;
            if (w != null && !EnsureSpot(w, e, p, out fresh)) ScLog.Warn(p.Name + " of " + e.Name + " is home, but the POI has no spot for them - they stand where they are");
            if (p.HasHome) GuardAtSpot(p, npc);
            AddPackage(npc);
            ScCommunity.ResetStill(p.Who);
            p.Who.AwaySeconds = 0;
            EnsureLeader(e);
        }

        /// <summary>
        /// Everything a body of an enclave carries, the first one and every later one alike: never saved (Dynamic, the
        /// veto), its enclave's and person's cvars, the record's person (never a re-roll), the Guard order at the home spot,
        /// the SCEnclave package, doors, the recorded health; and (E3) a leader's trade stock, the person's favour. E5: home
        /// false - a missing person's body away from home (SpawnAt) - leaves out the spot's Guard order and the package.
        /// </summary>
        private static void Dress(Enclave e, EncPerson p, EntityAlive npc, bool home = true)
        {
            npc.SetSpawnerSource(EnumSpawnerSource.Dynamic);
            npc.Buffs.SetCustomVar(EncCvar, e.Id);
            npc.Buffs.SetCustomVar(PersonCvar, p.Id);
            if (e.TurnedHostile) npc.Buffs.SetCustomVar(HostileCvar, 1f);
            if (ScXnpc.IsRaider(npc))
            {
                // a raider keeps its stars (ScShooting reads them); no skill bonuses or trait buffs, as for any raider
                npc.Buffs.SetCustomVar(ScShooting.RaiderStarsVar, p.Who.Stars(Skill.Shooting));
            }
            else
            {
                // as for an outpost stand-in: the record's skills (and their bonuses) and traits; and the profile the dialog's
                // snapshot shows, written over the one PostInit rolled for this body
                ScSkills.StampCvars(p.Who, npc);
                ScTraits.ApplyBuffs(p.Who, npc);
                ScProfile.Stamp(p.Who, npc);
            }
            if (home)
            {
                GuardAtSpot(p, npc);
                AddPackage(npc);
            }
            ScDoors.Enable(npc, true);
            int max = npc.GetMaxHealth();
            if (max > 0) npc.Health = Mathf.Clamp(Mathf.RoundToInt(p.Health * max), 1, max);
            p.EntityId = npc.entityId;
            p.EntityBorn = npc.WorldTimeBorn;
            p.SpawnFailures = 0;
            bodies[npc.entityId] = p;
            AddVeto(npc);
            Member m = p.Who;
            ScCommunity.ResetStill(m);
            m.AwaySeconds = 0;
            m.LastPos = npc.position;
            ScEnclaveDeals.OnDressed(e, p, npc);        // E3: a leader's trade stock; the person's favour follows the body
        }

        /// <summary>
        /// The Guard order at their spot as cvars (ScCommunity.Station's shape: buffOrderGuard's GuardHere would move the
        /// guard spot to wherever the body stands a tick later). Dress, and E3's walk home.
        /// </summary>
        internal static void GuardAtSpot(EncPerson p, EntityAlive npc)
        {
            GuardAt(npc, p.HomeCentre, LookPoint(p));
        }

        /// <summary>GuardAtSpot's order at any point (E3: a step of the walk toward a POI out of reach).</summary>
        internal static void GuardAt(EntityAlive npc, Vector3 at, Vector3 look)
        {
            npc.Buffs.SetCustomVar("CurrentOrder", (float)EntityUtilities.Orders.Guard);
            npc.Buffs.SetCustomVar("Guarding", 2f);
            ScNpc.SetGuard(npc, at, look);
        }

        internal static void AddPackage(EntityAlive npc)
        {
            if (npc.AIPackages == null) return;
            if (!UAI.UAIBase.AIPackages.ContainsKey(Package))
            {
                ScLog.Once("no-enclave-package", "ai_package " + Package + " is not registered - Config/utilityai.xml did not apply (enclave people only guard their spots)");
                return;
            }
            if (!npc.AIPackages.Contains(Package)) npc.AIPackages.Add(Package);
        }

        /// <summary>The point a body at its spot faces: four metres out along the spot's yaw, on its floor (as a resident's post).</summary>
        private static Vector3 LookPoint(EncPerson p)
        {
            return p.HomeCentre + Quaternion.Euler(0f, p.Yaw, 0f) * Vector3.forward * 4f;
        }

        // ------------------------------------------------------------------ bodies going ----

        /// <summary>What a body leaves in the record when it goes: its health and its rounds (and a refill that came meanwhile).</summary>
        private static void Record(EncPerson p, EntityAlive npc)
        {
            int max = npc.GetMaxHealth();
            if (max > 0) p.Health = Mathf.Clamp01((float)npc.Health / max);
            ScAmmo.Record(p.Who, npc);
            if (p.RestockDue)
            {
                p.RestockDue = false;
                Refill(p, false);          // the body was healed when the restock came; its rounds now
            }
        }

        internal static void Despawn(EncPerson p, EntityAlive npc)
        {
            Record(p, npc);
            Remove(p, npc);
        }

        /// <summary>Takes a body out of the world for good (ScRuns.RemoveEntity: Persist off, no corpse, no chunk record).</summary>
        private static void Remove(EncPerson p, EntityAlive npc)
        {
            int id = npc.entityId;
            Unbody(p, true);
            capturingId = id;
            try { ScRuns.RemoveEntity(npc); }
            finally { capturingId = -1; }
        }

        /// <summary>
        /// The person has no body any more (it went, died or vanished): the lookups forget it, LastEntityId keeps its id. E3:
        /// a leader's stock is recorded from it, an enlistment ends (home: the next body is built at their spot), a walk home
        /// ends, the person's favour waits for the next body (ScEnclaveDeals.OnUnbodied).
        /// </summary>
        private static void Unbody(EncPerson p, bool caches)
        {
            if (p.EntityId < 0) return;
            if (p.Missing) ScEncSearch.OnUnbodied(p, OwnBody(p));      // E5: one following the player waits where the body was
            if (p.Raiding) ScEncRaids.OnUnbodied(p, OwnBody(p));       // E10: one raiding stays with the party, off the map
            ScEnclaveDeals.OnUnbodied(p, OwnBody(p));
            Forget(p.EntityId, caches);
            p.LastEntityId = p.EntityId;
            p.EntityId = -1;
        }

        /// <summary>
        /// E5: a missing person's body is no longer theirs (they died in the record, or came home with no walk) - the lookups
        /// forget it; it stays in the world (and in the veto: never saved) until the search removes it unseen (RemoveStray).
        /// </summary>
        internal static void ForgetBody(EncPerson p)
        {
            Unbody(p, false);
        }

        /// <summary>E5: a body nobody's any more (ForgetBody) out of the world for good, its caches with it (SweepOrphans' way).</summary>
        internal static void RemoveStray(EntityAlive npc)
        {
            if (npc == null) return;
            int id = npc.entityId;
            Forget(id, true);
            capturingId = id;
            try { ScRuns.RemoveEntity(npc); }
            finally { capturingId = -1; }
        }

        /// <summary>
        /// A body is no longer a person's. caches: it leaves the world too, so the per-id caches go with it - SCore's
        /// (ScRunners.ClearScoreCaches), the session look, the AI's roam and defence state and a held inventory window.
        /// A corpse keeps them until vanilla removes it (its look, its loot window).
        /// </summary>
        private static void Forget(int id, bool caches)
        {
            bodies.Remove(id);
            if (!caches) return;
            ScRunners.ClearScoreCaches(id);
            ScLooks.Forget(id);
            UAI.ScRoamState.Active.Remove(id);
            UAI.ScRoamState.NextAllowed.Remove(id);
            UAI.ScDefendState.RestUntil.Remove(id);
            try { if (HarvestManager.Has(id)) HarvestManager.Remove(id); }
            catch (Exception e) { ScLog.Once("encpeople-harvest", e); }
        }

        private static int DespawnEnclave(World w, Enclave e)
        {
            int n = 0;
            foreach (EncPerson p in new List<EncPerson>(bodies.Values))
            {
                if (e != null && p.EnclaveId != e.Id) continue;
                if (w != null && ScNpc.IsPending(w, p.EntityId)) continue;      // still being built: kept, a later tick takes it once it is
                EntityAlive npc = w != null ? ScNpc.As(ScNpc.Loaded(w, p.EntityId)) : null;
                if (npc != null && !npc.IsDead()) { Despawn(p, npc); n++; }
                else Unbody(p, false);
            }
            return n;
        }

        /// <summary>Every body out of the world (Enclaves switched off), their state recorded; the holds let go.</summary>
        private static void DespawnAll(World w, string why)
        {
            int n = DespawnEnclave(w, null);
            ReleaseHolds(why);
            visits.Clear();
            if (n > 0)
            {
                ScRegistry.SaveAtomic();
                ScLog.Out("enclave people: " + n + " body(ies) taken out of the world (" + why + ")");
            }
        }

        // ------------------------------------------------------------------ chunk hold ----

        /// <summary>
        /// The POI's chunks are held by a chunk observer (ScHomeHold's pattern) once a player is within EnclaveSpawnRange
        /// while its people are out, and let go when the last of them has gone - so a player at the edge of the view distance
        /// does not unload and rebuild them at every chunk-line crossing.
        /// </summary>
        private static void UpdateHold(Enclave e, float near, bool bodiesOut)
        {
            Visit v;
            visits.TryGetValue(e.Id, out v);
            if (!ScConfig.EnclaveHoldChunks || !bodiesOut)
            {
                if (v != null && v.Hold != null) Release(v, "enclave " + e.Id + " '" + e.Name + "': " + (bodiesOut ? "EnclaveHoldChunks is off" : "its people went"));
                return;
            }
            if (v == null) v = VisitOf(e.Id);
            if (v.Hold != null || near > ScConfig.EnclaveSpawnRange || GameManager.Instance == null) return;
            Vector3 c = e.Centre;
            int half = Math.Max(e.Size.x, e.Size.z) / 2;
            // ChunkManager holds rings 0..viewDim+1 round the centre chunk (ScHomeHold): ceil(half / 16) covers the POI
            int dim = Mathf.Clamp((half + 15) / 16, 1, MaxHoldDim);
            try
            {
                v.Hold = GameManager.Instance.AddChunkObserver(c, false, dim, -1);
                ScLog.Verbose("enclave " + e.Id + " '" + e.Name + "': holding its POI's chunks (" + dim + " chunk radius) while its people are out");
            }
            catch (Exception ex)
            {
                ScLog.Once("encpeople-hold", ex);
            }
        }

        private static void Release(Visit v, string why)
        {
            if (v == null || v.Hold == null) return;
            try
            {
                if (GameManager.Instance != null && GameManager.Instance.World != null) GameManager.Instance.RemoveChunkObserver(v.Hold);
                if (why != null) ScLog.Verbose(why + " - its POI's chunks are no longer held");
            }
            catch (Exception e)
            {
                ScLog.Once("encpeople-release", e);
            }
            finally
            {
                v.Hold = null;
            }
        }

        private static void ReleaseHolds(string why)
        {
            foreach (Visit v in visits.Values) Release(v, why);
        }

        // ------------------------------------------------------------------ unload, shutdown, orphans ----

        private static void Hook(World w)
        {
            if (hooked == w) return;
            Unhook();
            w.EntityUnloadedDelegates += OnEntityUnloaded;             // fired BEFORE OnEntityUnload: the body is intact
            hooked = w;
        }

        private static void Unhook()
        {
            if (hooked != null) { try { hooked.EntityUnloadedDelegates -= OnEntityUnloaded; } catch (Exception) { } }
            hooked = null;
        }

        /// <summary>
        /// A chunk unloads under a body (a teleport, a bed respawn, a hold that never came): its state goes into the record
        /// first, then the person has no body; the veto keeps the body out of the chunk, and a Delete-sourced copy would
        /// destroy itself on load should that ever fail (ScRunners' backstop).
        /// </summary>
        private static void OnEntityUnloaded(Entity e, EnumRemoveEntityReason reason)
        {
            try
            {
                if (e == null || reason != EnumRemoveEntityReason.Unloaded || e.entityId == capturingId || !ScCommunity.IsHost || !ScRegistry.Loaded) return;
                EncPerson p;
                if (!bodies.TryGetValue(e.entityId, out p)) return;
                EntityAlive npc = e as EntityAlive;
                if (npc == null || npc.IsDead() || npc.WorldTimeBorn != p.EntityBorn) return;
                Record(p, npc);
                Unbody(p, true);
                npc.SetSpawnerSource(EnumSpawnerSource.Delete);
                if (!npc.IsMarkedForUnload()) npc.MarkToUnload();
                ScRegistry.SaveAtomic();
                ScLog.Out(p.Name + " of enclave " + p.EnclaveId + " was captured as their chunk unloaded");
            }
            catch (Exception ex)
            {
                ScLog.Once("encpeople-unload-capture", ex);
            }
        }

        /// <summary>WorldShuttingDown, BEFORE ScRegistry.FlushAndReset and the world's save: every body recorded and taken out.</summary>
        public static void OnWorldShuttingDown()
        {
            try
            {
                World w = ScCommunity.World;
                if (!ScCommunity.IsHost || !ScRegistry.Loaded || w == null) return;
                int n = DespawnEnclave(w, null);
                if (n > 0)
                {
                    ScRegistry.SaveAtomic();
                    ScLog.Out("enclave people: " + n + " body(ies) recorded and taken out before the save");
                }
            }
            catch (Exception e)
            {
                ScLog.Once("encpeople-shutdown", e);
            }
            finally
            {
                ReleaseHolds(null);
                visits.Clear();
                Unhook();
            }
        }

        /// <summary>
        /// A body of ours never comes back from a save (it is never written), but after a crash or a save race one can: the
        /// PostInit postfix sees %sc_enc on a body we did not just build (ours get the cvar after PostInit) and it goes. An
        /// id the game is still building stays in Orphans for a later tick.
        /// </summary>
        private static void SweepOrphans(World w)
        {
            if (Orphans.Count == 0) return;
            List<int> keep = null;
            foreach (int id in Orphans)
            {
                if (ScNpc.IsPending(w, id))
                {
                    if (keep == null) keep = new List<int>();
                    keep.Add(id);           // swept once it is built
                    continue;
                }
                EntityAlive s = ScNpc.As(ScNpc.Loaded(w, id));
                if (s == null) continue;
                EncPerson p;
                if (bodies.TryGetValue(id, out p) && p.EntityBorn == s.WorldTimeBorn) continue;      // a current body: never ours to sweep
                ScRunners.ClearScoreCaches(id);
                ScRuns.RemoveEntity(s);
                ScLog.Out("removed a stale enclave body (entity " + id + ", enclave " + (int)s.Buffs.GetCustomVar(EncCvar) + ") that a chunk or a save brought back");
            }
            Orphans.Clear();
            if (keep != null) foreach (int id in keep) Orphans.Add(id);
        }

        // ------------------------------------------------------------------ the watchdog ----

        /// <summary>
        /// The residents' stuck watchdog on an enclave body, once a second: ScCommunity.StuckSample is the residents' own
        /// detection (no progress while it wants to move, the move helper blocked, SCore's in-a-block flag; frozen time never
        /// counts), with StuckSeconds (three times that in a fight) and the back-off. The cure is the spot's, not a post's:
        /// stuck at the spot itself, a new spot; anywhere else, or far outside the POI for FarSeconds, back to the spot.
        /// </summary>
        private static void Watch(World w, Enclave e, EncPerson p, EntityAlive npc)
        {
            Member m = p.Who;
            if (!p.HasHome || !ScCommunity.StuckSample(m, npc)) return;
            bool fighting = ScCommunity.LiveFoe(npc, ScCommunity.FightRange) != null;
            int limit = (fighting ? ScConfig.StuckSeconds * 3 : ScConfig.StuckSeconds) << m.StuckShift;
            Vector3 pos = npc.position;
            float outside = e.DistanceXZ(pos.x, pos.z);
            float far = ScConfig.EnclaveLeashMargin + FarExtra + (fighting ? ScConfig.EnclaveDefendRadius : 0);
            if (outside > far) m.AwaySeconds++; else m.AwaySeconds = 0;
            if (m.StillSeconds >= limit) Unstick(w, e, p, npc, "no progress for " + m.StillSeconds + " s" + (fighting ? ", fighting" : ""), fighting);
            else if (m.AwaySeconds >= FarSeconds) Unstick(w, e, p, npc, Mathf.RoundToInt(outside) + " m outside the POI for " + m.AwaySeconds + " s", fighting);
        }

        private static void Unstick(World w, Enclave e, EncPerson p, EntityAlive npc, string why, bool fighting)
        {
            Member m = p.Who;
            ScCommunity.ResetStill(m);
            m.AwaySeconds = 0;
            Vector3 spot = p.HomeCentre;
            Vector3 d = npc.position - spot;
            if (!fighting && d.x * d.x + d.z * d.z <= 1f && Mathf.Abs(d.y) <= 1.5f)
            {
                // a move back to where it already stands cannot help: the spot is the problem
                Vector3i old = p.Home;
                float oldYaw = p.Yaw;
                VisitOf(e.Id).Bad.Add(old);
                p.HasHome = false;
                if (AssignSpot(w, e, p) && p.Home != old)
                {
                    m.StuckShift = 0;
                    ScCommunity.DropPath(npc);
                    ScNpc.SetGuard(npc, p.HomeCentre, LookPoint(p));
                    ScRegistry.SaveAtomic();
                    ScLog.Out(p.Name + " of " + e.Name + " was stuck at their spot " + old + " (" + why + ") - new spot " + p.Home);
                    return;
                }
                p.Home = old;
                p.Yaw = oldYaw;
                p.HasHome = true;
                if (m.StuckShift < MaxStuckShift) m.StuckShift++;
                ScLog.Warn(p.Name + " of " + e.Name + " is stuck at their spot " + old + " (" + why + ") and the POI has no other - the stuck check now waits "
                           + (ScConfig.StuckSeconds << m.StuckShift) + " s");
                return;
            }
            ScCommunity.DropPath(npc);
            npc.SetPosition(spot, true);
            m.LastPos = spot;
            ScNpc.SetGuard(npc, spot, LookPoint(p));
            UAI.ScRoamState.NextAllowed[npc.entityId] = Time.time + 30f;
            if (fighting) UAI.ScDefendState.Rest(npc.entityId, ScCommunity.DefendRestSeconds);
            ScLog.Out(p.Name + " of " + e.Name + " was stuck (" + why + ") - moved back to their spot " + p.Home);
        }

        // ------------------------------------------------------------------ home spots ----

        /// <summary>
        /// The spot is still one (GoodPost, relaxed: two air cells over a real floor) or a new one is chosen: a quest reset
        /// rebuilds the POI, a block can be placed over it. Never a spawn into a block. fresh: a new spot was chosen.
        /// </summary>
        private static bool EnsureSpot(World w, Enclave e, EncPerson p, out bool fresh)
        {
            fresh = false;
            if (p.HasHome)
            {
                string why;
                if (ScCommunity.GoodPost(w, p.Home, false, out why)) return true;
                ScLog.Out(p.Name + " of " + e.Name + ": the spot " + p.Home + " is " + why + " - a new one");
                VisitOf(e.Id).Bad.Add(p.Home);
                p.HasHome = false;
            }
            fresh = AssignSpot(w, e, p);
            return fresh;
        }

        /// <summary>
        /// A home spot for this person from the enclave's candidates, spread from the others' (MinSpacing); the leader's is
        /// the good spot nearest the centre on the lowest floor, under a roof when there is one. With none left: a ground
        /// spot in the footprint or at its edge (ScRunners.WanderSpot, as a runner wanders a site).
        /// </summary>
        private static bool AssignSpot(World w, Enclave e, EncPerson p)
        {
            Visit v = VisitOf(e.Id);
            List<Spot> cands = Candidates(w, e);
            var taken = new List<Vector3i>();
            // the living's spots (E3: one enlisted with the player comes back to theirs; E5: so does one gone missing)
            foreach (EncPerson q in ScRegistry.EncPeople)
                if (q != p && q.EnclaveId == e.Id && (q.Alive || q.Missing) && q.HasHome) taken.Add(q.Home);
            GameRandom r = w.GetGameRandom();
            Spot s;
            if (!Pick(e, cands, p.IsLeader, taken, v.Bad, r, out s))
            {
                Vector3 g;
                if (!ScRunners.WanderSpot(w, r, e.Poi, e.Size, out g)) return false;
                Vector3i gp = World.worldToBlockPos(g);
                s = new Spot { Pos = gp, Yaw = FaceCentre(e, gp), Rank = 4 };
            }
            p.Home = s.Pos;
            p.Yaw = s.Yaw;
            p.HasHome = true;
            return true;
        }

        /// <summary>
        /// The enclave's spot candidates, found once a session with its chunks loaded: the sleeper spawn points of its POI's
        /// volumes first (positions its author put on its floors; strict GoodPost before relaxed), then - when they are too
        /// few - the ground floor column by column (FindPost round the terrain height: never World.GetHeight, the roof).
        /// </summary>
        private static List<Spot> Candidates(World w, Enclave e)
        {
            Visit v = VisitOf(e.Id);
            if (v.Spots != null) return v.Spots;
            int want = Math.Max(GroupSize(e), PeopleOf(e).Count) + 4;
            int fromSleepers;
            List<Spot> list = SpotsIn(w, ScEnclaves.PoiOf(e), e.Poi, e.Size, e.Centre, want, out fromSleepers);
            v.Spots = list;
            ScLog.Verbose("enclave " + e.Id + " '" + e.Name + "': " + list.Count + " home spot(s) to choose from, " + fromSleepers + " of them sleeper spawn points");
            return list;
        }

        /// <summary>
        /// The spot candidates of a POI's footprint (poi: its key, size: its rotated size, centre: what a scanned spot faces),
        /// its chunks loaded: Candidates' search (above), for an enclave's POI and (E5) the building a missing survivor waits in.
        /// fromSleepers: how many are sleeper spawn points.
        /// </summary>
        internal static List<Spot> SpotsIn(World w, PrefabInstance pi, Vector3i poi, Vector3i size, Vector3 centre, int want, out int fromSleepers)
        {
            var list = new List<Spot>();
            var seen = new HashSet<Vector3i>();
            string why;
            SleeperVolume[] vols = null;
            if (pi != null) { try { vols = pi.sleeperVolumes.ToArray(); } catch (Exception) { vols = null; } }
            if (vols != null)
            {
                foreach (SleeperVolume sv in vols)
                {
                    List<SleeperVolume.SpawnPoint> pts = sv != null ? sv.GetSpawnPoints() : null;
                    if (pts == null) continue;
                    for (int i = 0; i < pts.Count; i++)
                    {
                        Vector3i pos = pts[i].pos;
                        if (!InFootprint(poi, size, pos.x, pos.z) || !seen.Add(pos)) continue;
                        int rank = ScCommunity.GoodPost(w, pos, true, out why) ? 0 : ScCommunity.GoodPost(w, pos, false, out why) ? 1 : -1;
                        if (rank >= 0) list.Add(new Spot { Pos = pos, Yaw = pts[i].rot, Rank = rank, Roofed = Roofed(w, pos) });
                    }
                }
            }
            fromSleepers = list.Count;
            if (list.Count < want)
            {
                int x0 = poi.x + 1, x1 = poi.x + Math.Max(1, size.x) - 2, z0 = poi.z + 1, z1 = poi.z + Math.Max(1, size.z) - 2;
                int cols = 0;
                for (int x = x0; x <= x1 && cols < MaxScanColumns; x += ScanStep)
                {
                    for (int z = z0; z <= z1 && cols < MaxScanColumns; z += ScanStep)
                    {
                        cols++;
                        if (w.GetChunkFromWorldPos(x, 0, z) == null) continue;
                        Vector3i post;
                        int tier;
                        if (!ScCommunity.FindPost(w, x, z, w.GetTerrainHeight(x, z) + 1, null, out post, out tier) || tier > ScCommunity.TierRelaxed || !seen.Add(post)) continue;
                        list.Add(new Spot { Pos = post, Yaw = FaceCentre(centre, post), Rank = 2 + tier, Roofed = Roofed(w, post) });
                    }
                }
            }
            return list;
        }

        /// <summary>Is (x, z) in the footprint (Enclave.Contains for any POI)?</summary>
        internal static bool InFootprint(Vector3i poi, Vector3i size, int x, int z)
        {
            return x >= poi.x && x < poi.x + size.x && z >= poi.z && z < poi.z + size.z;
        }

        private static bool Pick(Enclave e, List<Spot> cands, bool leader, List<Vector3i> taken, HashSet<Vector3i> bad, GameRandom r, out Spot best)
        {
            best = default(Spot);
            bool found = false;
            if (cands.Count == 0) return false;
            if (leader)
            {
                // the lowest floor any free spot is on, then a roof over it, a strict spot, and the nearest the centre
                int low = int.MaxValue;
                foreach (Spot s in cands) if (!bad.Contains(s.Pos) && Spread(taken, s.Pos) >= MinSpacing) low = Math.Min(low, s.Pos.y);
                Vector3 c = e.Centre;
                float bestScore = float.MaxValue;
                foreach (Spot s in cands)
                {
                    if (bad.Contains(s.Pos) || Spread(taken, s.Pos) < MinSpacing) continue;
                    float dx = s.Pos.x + 0.5f - c.x, dz = s.Pos.z + 0.5f - c.z;
                    float score = (s.Pos.y <= low + 2 ? 0f : 100000f) + (s.Roofed ? 0f : 10000f) + (s.Rank % 2) * 50f + Mathf.Sqrt(dx * dx + dz * dz);
                    if (score < bestScore) { bestScore = score; best = s; found = true; }
                }
                return found;
            }
            // the others: the best kind of spot, spread from the rest up to SpreadCap, from a random start among equals
            int n = cands.Count, start = r != null ? r.RandomRange(0, n) : 0;
            float bestOther = float.MaxValue;
            for (int k = 0; k < n; k++)
            {
                Spot s = cands[(start + k) % n];
                if (bad.Contains(s.Pos)) continue;
                float spread = Spread(taken, s.Pos);
                if (spread < MinSpacing) continue;
                float score = s.Rank * 100f - Mathf.Min(spread, SpreadCap);
                if (score < bestOther) { bestOther = score; best = s; found = true; }
            }
            return found;
        }

        /// <summary>XZ metres to the nearest taken spot (1000 with none).</summary>
        private static float Spread(List<Vector3i> taken, Vector3i p)
        {
            float best = 1000f;
            for (int i = 0; i < taken.Count; i++)
            {
                float dx = taken[i].x - p.x, dz = taken[i].z - p.z;
                best = Mathf.Min(best, Mathf.Sqrt(dx * dx + dz * dz));
            }
            return best;
        }

        /// <summary>Anything solid within ten cells over the spot's head: under a roof.</summary>
        private static bool Roofed(World w, Vector3i p)
        {
            for (int k = 2; k <= 12; k++) if (!w.GetBlock(new Vector3i(p.x, p.y + k, p.z)).isair) return true;
            return false;
        }

        /// <summary>The yaw that faces the footprint's centre from a spot (a column scan's spot has no authored rotation).</summary>
        private static float FaceCentre(Enclave e, Vector3i p)
        {
            return FaceCentre(e.Centre, p);
        }

        private static float FaceCentre(Vector3 c, Vector3i p)
        {
            float dx = c.x - (p.x + 0.5f), dz = c.z - (p.z + 0.5f);
            return dx * dx + dz * dz < 0.01f ? 0f : Mathf.Atan2(dx, dz) * Mathf.Rad2Deg;
        }

        /// <summary>A moved enclave (ScEnclaves.Validate): its spots are in the old POI, chosen again when its people next spawn.</summary>
        public static void ClearSpots(Enclave e)
        {
            int n = 0;
            foreach (EncPerson p in ScRegistry.EncPeople) if (p.EnclaveId == e.Id && p.HasHome) { p.HasHome = false; n++; }
            Visit v;
            if (visits.TryGetValue(e.Id, out v)) { v.Spots = null; v.Bad.Clear(); }
            if (n > 0) ScLog.Out("enclave " + e.Id + " '" + e.Name + "': " + n + " home spot(s) cleared (its POI moved) - chosen again when they next spawn");
        }

        /// <summary>
        /// E11: a group about to move to another building (ScEncMoves, none of its bodies out): the visit's chunk hold let go and
        /// the visit dropped (its spot candidates, failed spots and settle clock were the old building's), the AI's area cache
        /// with it.
        /// </summary>
        internal static void ReleaseVisit(Enclave e)
        {
            Visit v;
            if (visits.TryGetValue(e.Id, out v))
            {
                Release(v, "enclave " + e.Id + " '" + e.Name + "': it is moving");
                visits.Remove(e.Id);
            }
            areas.Remove(e.Id);
        }

        /// <summary>
        /// E11: after a move. A group still being made is made to the new building's size (GroupSize reads the footprint); when the
        /// people made already fill it - some met away from home by E10's raids, none of them a leader - one of those at home
        /// leads now (EnsureLeader), as Generate would never make one. Returns the group's size now.
        /// </summary>
        internal static int AfterMove(Enclave e)
        {
            int size = GroupSize(e);
            if (!e.PeopleRolled && PeopleOf(e).Count >= size) EnsureLeader(e);
            return size;
        }

        // ------------------------------------------------------------------ deaths ----

        /// <summary>The SetDead postfix (Patches_Npc) for a body carrying %sc_enc: the person dies with it.</summary>
        public static void OnBodyDied(EntityAlive npc)
        {
            try
            {
                EncPerson p;
                if (npc == null || !bodies.TryGetValue(npc.entityId, out p) || p.EntityBorn != npc.WorldTimeBorn) return;   // an orphan: nobody's
                // E3: one enlisted with the player died at their side - the hire undone first, quietly (the death tells)
                bool wasEnlisted = p.Enlisted;
                if (wasEnlisted) ScEnclaveDeals.EndEnlist(p, npc, null, "died", false);
                // E5: one gone missing - found and waiting, or following the player home (the search undoes an escort's hire
                // as the body goes: ScEncSearch.OnUnbodied); their request hears of the death after it is recorded
                bool wasMissing = p.Missing;
                bool wasRaiding = p.Raiding;          // E10: killed raiding an outpost - the raid counts the loss
                Vector3 at = npc.position;
                // the corpse is left to vanilla (its loot, its despawn) and stays out of every save (the veto)
                Unbody(p, false);
                Enclave e = ScEnclaves.ById(p.EnclaveId);
                if (e != null) Died(e, p, npc.entityThatKilledMe, true, wasEnlisted, wasMissing, wasRaiding);
                if (wasMissing) ScEncSearch.OnDied(p, at);
            }
            catch (Exception ex)
            {
                ScLog.Once("encpeople-died", ex);
            }
        }

        /// <summary>
        /// A person died (a body, or "sc enclave kill"): marked dead, a ledger line, a tell while the player is near. With
        /// nobody left of a group that was all made the enclave falls; a group still being made (its first bodies killed
        /// while a spot in view held the rest back) does not - the rest come, a new leader first. A kill by the player (or a
        /// companion of theirs) turns a friendly or neutral enclave hostile (Q6 a); a dead leader's place and spot go to the next.
        /// E3: one who died enlisted with the player costs EnclaveEnlistDeathPenalty standing and says so in the ledger; a
        /// favour they asked is off. E5: one who died missing (found, or following the player home) is told wherever the
        /// player is - they were there. E10: one who died in a raid (raid: killed raiding an outpost, or lost as an unattended
        /// raid was held) has no ledger line of their own - the raid's end writes one for its losses (ScEncRaids.OnDied counts
        /// them); how: the log's words for a death no body and no console caused.
        /// </summary>
        private static void Died(Enclave e, EncPerson p, EntityAlive killer, bool real, bool wasEnlisted, bool wasMissing = false, bool raid = false, string how = null)
        {
            bool wasLeader = p.IsLeader;
            p.State = EncPerson.StateDead;
            p.DiedDay = ScCommunity.Today;
            p.Health = 0f;
            p.Role = EncPerson.MemberRole;             // one leader among the living
            ScEncMoves.NoteLoss(e);                    // E11: the day of their last death (request clock)
            if (raid) ScEncRaids.OnDied(e, p);         // E10: before a fall below can end the raid
            EntityPlayerLocal pl = ScCommunity.LocalPlayer;
            if (!raid) ScOutposts.NoteLedger(string.Format(Localization.Get(wasEnlisted ? "scEncLedgerEnlistedDied" : "scEncLedgerDied"), p.Name, e.Name));
            if (wasEnlisted || wasMissing || Near(e, pl)) ScCommunity.Tell(pl, string.Format(Localization.Get("scEncDied"), p.Name, e.Name), "");
            if (wasEnlisted && ScConfig.EnclaveEnlistDeathPenalty > 0) ScEnclaves.AddStanding(e, -ScConfig.EnclaveEnlistDeathPenalty, p.Name + " died while enlisted with the player");
            ScEnclaveDeals.DropFavour(p, "they died");
            EntityPlayer by = KillerPlayer(killer);
            ScLog.Out("enclave " + e.Id + " '" + e.Name + "': " + (wasLeader ? "the leader " : "") + p.Name + " (p" + p.Id + ") died"
                      + (killer != null ? " - killed by " + ScCommunity.DisplayName(killer) + (by != null && !ReferenceEquals(by, killer) ? " (" + by.EntityName + "'s)" : "")
                         : how != null ? " (" + how + ")" : real ? "" : " (the console)")
                      + (raid && killer != null ? " raiding an outpost" : ""));
            List<EncPerson> living = Living(e);
            // E5 (the review's L1): one of theirs missing who may be alive (not found yet, waiting, following the player home)
            // holds the fall off; it comes once they are lost or found dead with nobody else alive (ScEncSearch.CheckFalls)
            if (living.Count == 0 && ScEncSearch.AnyAlive(e))
                ScLog.Out("enclave " + e.Id + " '" + e.Name + "': nobody at home is alive, but one of theirs is missing and may be - the group stands while their search goes on");
            else if (living.Count == 0)
            {
                if (e.PeopleRolled || PeopleOf(e).Count >= GroupSize(e))
                {
                    ScEnclaves.Fall(e, by != null ? "wiped out by " + by.EntityName : "everyone died");
                    return;
                }
                ScLog.Out("enclave " + e.Id + " '" + e.Name + "': nobody made so far is alive - the rest of the group ("
                          + Math.Max(0, GroupSize(e) - PeopleOf(e).Count) + " more) is still to come, a new leader first");
            }
            if (by != null && !e.IsHostile) ScEnclaves.TurnHostile(e, "kill");
            if (wasLeader) Succeed(e, p, living);
            ScRegistry.SaveAtomic();
        }

        /// <summary>The player behind a kill: the player, or the player a companion follows (its Leader / Owner cvar). Turrets name their owner already.</summary>
        private static EntityPlayer KillerPlayer(EntityAlive killer)
        {
            if (killer == null) return null;
            EntityPlayer p = killer as EntityPlayer;
            if (p != null) return p;
            int leader = ScCommunity.HiredBy(killer);
            World w = ScCommunity.World;
            return leader > 0 && w != null ? w.GetEntity(leader) as EntityPlayer : null;
        }

        /// <summary>
        /// The leader died: the living person at home with the highest tier (then the one made first) leads now, from the
        /// leader's spot, and (E3) trades with the enclave's stock. One enlisted with the player is passed over: with nobody
        /// else at home the group has no leader until they come back (EnsureLeader). dead: null for EnsureLeader's call.
        /// </summary>
        private static void Succeed(Enclave e, EncPerson dead, List<EncPerson> living)
        {
            EncPerson next = null;
            foreach (EncPerson q in living)
            {
                if (q.State != EncPerson.StateHome) continue;
                if (next == null) { next = q; continue; }
                int tq = ScXnpc.TierOf(q.Who.Class), tn = ScXnpc.TierOf(next.Who.Class);
                if (tq > tn || (tq == tn && q.Id < next.Id)) next = q;
            }
            if (next == null) return;
            next.Role = EncPerson.Leader;
            ScEnclaveDeals.OnNewLeader(e, next);
            if (dead != null && dead.HasHome)
            {
                next.Home = dead.Home;
                next.Yaw = dead.Yaw;
                next.HasHome = true;
                EntityAlive npc = BodyOf(next);
                if (npc != null)
                {
                    ScCommunity.DropPath(npc);
                    ScNpc.SetGuard(npc, next.HomeCentre, LookPoint(next));
                }
            }
            ScOutposts.NoteLedger(string.Format(Localization.Get("scEncLedgerLeader"), next.Name, e.Name));
            EntityPlayerLocal pl = ScCommunity.LocalPlayer;
            if (Near(e, pl)) ScCommunity.Tell(pl, string.Format(Localization.Get("scEncNewLeader"), next.Name, e.Name), "");
            ScLog.Out("enclave " + e.Id + " '" + e.Name + "': " + next.Name + " (p" + next.Id + ") leads them now");
        }

        /// <summary>
        /// E3: a group nobody leads - its leader died while the only others were out with the player - gets one from those at
        /// home (ScEnclaveDeals.EndEnlist, when one comes back). Nothing while it has a leader or nobody is home.
        /// </summary>
        internal static void EnsureLeader(Enclave e)
        {
            if (e == null || !ScEnclaves.All.Contains(e) || LeaderOf(e) != null) return;
            Succeed(e, null, Living(e));
        }

        /// <summary>The player is near enough to hear of it: within the range the bodies are kept in.</summary>
        private static bool Near(Enclave e, EntityPlayer pl)
        {
            return pl != null && e.DistanceXZ(pl.position.x, pl.position.z) <= ScConfig.EnclaveSpawnRange + ScConfig.EnclaveDespawnMargin;
        }

        /// <summary>
        /// An enclave fell or was dropped (ScEnclaves.Fall, sc enclave reseed): bodies still out are taken away, its records
        /// go, its hold and lookups with them; (E3) one enlisted with the player is let go quietly and every favour its
        /// people asked is off. E10: its demand or raid on an outpost is off first, and a raider's body out raiding stays in
        /// that fight as a plain raider (ScEncRaids.OnDenFalling, Abandon).
        /// </summary>
        public static void DropPeople(Enclave e)
        {
            World w = ScCommunity.World;
            ScEncRaids.OnDenFalling(e);
            foreach (EncPerson p in new List<EncPerson>(bodies.Values))
            {
                if (p.EnclaveId != e.Id) continue;
                EntityAlive npc = w != null ? ScNpc.As(ScNpc.Loaded(w, p.EntityId)) : null;
                if (p.Enlisted) ScEnclaveDeals.EndEnlist(p, npc != null && npc.WorldTimeBorn == p.EntityBorn ? npc : null, null, "their enclave is gone", false);
                if (npc != null && !npc.IsDead()) Remove(p, npc);
                else Unbody(p, false);
            }
            foreach (EncPerson p in ScRegistry.EncPeople)
                if (p.EnclaveId == e.Id) ScEnclaveDeals.DropFavour(p, "their enclave is gone");
            ScRegistry.EncPeople.RemoveAll(p => p.EnclaveId == e.Id);
            Visit v;
            if (visits.TryGetValue(e.Id, out v)) { Release(v, null); visits.Remove(e.Id); }
            areas.Remove(e.Id);
            RebuildHostile();
        }

        /// <summary>
        /// E3's recruit (ScEnclaveDeals.Recruit): the person leaves the enclave for good - their record goes, and their body,
        /// which joins the community, loses the enclave's cvars and AI package and leaves the lookups and the chunk-save
        /// veto: from now on the chunk saves it as a member's. The '%' cvars are set to 0 first (a 0 reaches clients, a
        /// removal never does) and then removed, so the host's own tests (IsEnclaveNpc: HasCustomVar) read false at once.
        /// </summary>
        internal static void LeaveEnclave(EncPerson p, EntityAlive npc)
        {
            if (npc != null)
            {
                string[] synced = { EncCvar, PersonCvar, HostileCvar };
                foreach (string cv in synced)
                {
                    if (!npc.Buffs.HasCustomVar(cv)) continue;
                    npc.Buffs.SetCustomVar(cv, 0f);
                    npc.Buffs.RemoveCustomVar(cv);
                }
                npc.Buffs.RemoveCustomVar(ScEnclaveDeals.EnlistedCvar);
                if (npc.AIPackages != null) npc.AIPackages.Remove(Package);
                RemoveVeto(npc.entityId);
                bodies.Remove(npc.entityId);
            }
            if (p.EntityId >= 0) bodies.Remove(p.EntityId);
            p.EntityId = -1;
            p.Leaving = false;
            ScRegistry.EncPeople.Remove(p);
        }

        /// <summary>
        /// E10: a raider's body out raiding when its group fell (ScEncRaids.OnDenFalling) stays in the fight as a plain raider:
        /// the enclave's cvars off (LeaveEnclave's way: 0 first, which reaches clients, then removed), no person's body any more
        /// - but still in the chunk-save veto (never saved, as the outpost attack's own raiders are Dynamic).
        /// </summary>
        internal static void Abandon(EncPerson p, EntityAlive npc)
        {
            if (npc != null && npc.Buffs != null)
            {
                string[] synced = { EncCvar, PersonCvar, HostileCvar };
                foreach (string cv in synced)
                {
                    if (!npc.Buffs.HasCustomVar(cv)) continue;
                    npc.Buffs.SetCustomVar(cv, 0f);
                    npc.Buffs.RemoveCustomVar(cv);
                }
            }
            Unbody(p, false);
        }

        /// <summary>
        /// E12: the body of a person who joined the player's community with their whole group and makes their own way there
        /// (ScEncJoin): no person's body any more (the lookups forget it, Unbody) and the SCEnclave package off, so it walks
        /// where ScEncJoin steers it; its enclave cvars and the chunk-save veto stay - the stranger rows stay hidden on it, it is
        /// never saved, and one a crash brings back is swept as an orphan. ScEncJoin removes it once nobody sees it.
        /// </summary>
        internal static void Husk(EncPerson p, EntityAlive npc)
        {
            if (npc != null && npc.AIPackages != null) npc.AIPackages.Remove(Package);
            Unbody(p, false);
        }

        /// <summary>
        /// E10: a person dies in the records only (an unattended raid the outpost held, ScEncRaids): their body, if one is out,
        /// is taken away (no corpse), then Died's rules with the raid's ledger (one line for the raid's losses). how: the log's words.
        /// </summary>
        internal static void KillRecord(Enclave e, EncPerson p, string how)
        {
            if (e == null || p == null || !p.Alive) return;
            EntityAlive npc = OwnBody(p);
            if (npc != null && !npc.IsDead()) Remove(p, npc);
            else Unbody(p, false);
            Died(e, p, null, false, false, false, true, how);
        }

        private static void RemoveVeto(int id)
        {
            if (!vetoIds.ContainsKey(id)) return;
            var s = new Dictionary<int, KeyValuePair<int, ulong>>(vetoIds);
            s.Remove(id);
            vetoIds = s;
        }

        // ------------------------------------------------------------------ the daily restock ----

        /// <summary>
        /// ScDaily.Run (EnclaveDailyRestock): every living person at home back to full health and the rounds they were made
        /// with (E3: not one enlisted with the player - they are out with them). Idempotent, so a catch-up of several days
        /// gives the same. A body out in the world is healed now and gets its rounds when it goes (it carries what it carries).
        /// </summary>
        public static void Daily(int day)
        {
            try
            {
                if (!ScConfig.EnclaveDailyRestock || ScRegistry.EncPeople.Count == 0) return;
                int n = 0;
                foreach (EncPerson p in ScRegistry.EncPeople)
                {
                    if (p.State != EncPerson.StateHome) continue;
                    EntityAlive npc = BodyOf(p);
                    if (npc != null && !npc.IsDead())
                    {
                        npc.Health = npc.GetMaxHealth();
                        p.RestockDue = true;
                    }
                    else Refill(p, true);
                    n++;
                }
                if (n > 0) ScLog.Verbose("enclave people: " + n + " restocked for day " + day);
            }
            catch (Exception e)
            {
                ScLog.Once("encpeople-daily", e);
            }
        }

        private static void Refill(EncPerson p, bool health)
        {
            if (health) p.Health = 1f;
            if (p.AmmoStart == null) return;
            p.Who.Ammo = ScAmmo.Copy(p.AmmoStart);
            p.Who.Mags = p.AmmoStartMags;
        }

        // ------------------------------------------------------------------ hostility ----

        /// <summary>Is this enclave's hostility on now (turned hostile, its grace over)? Thread-safe.</summary>
        public static bool HostileActive(int enclaveId)
        {
            return anyHostile && hostileActive.Contains(enclaveId);
        }

        /// <summary>Any enclave's hostility on: the faction postfix's first, cheapest test.</summary>
        public static bool AnyHostile { get { return anyHostile; } }

        /// <summary>
        /// The faction postfix's widening (hot, O(1)): one side is a body of an enclave whose hostility is on and the other a
        /// player, one of the community (a member or a stand-in) or a companion following a player. Residents share the
        /// survivors' faction (800), so without this they would watch such a body raid the base.
        /// </summary>
        public static bool HostilePair(EntityAlive a, EntityAlive b)
        {
            if (!anyHostile || a == null || b == null) return false;
            return (HostileBodyNow(a) && Opposes(b)) || (HostileBodyNow(b) && Opposes(a));
        }

        private static bool HostileBodyNow(EntityAlive e)
        {
            EntityBuffs bf = e.Buffs;
            if (bf == null || e is EntityPlayer) return false;
            float id = bf.GetCustomVar(EncCvar);
            if (id <= 0f || !hostileActive.Contains((int)id)) return false;
            // a follower the flip released walks home first (plan 2.8: "they leave you first"): no enemy of anyone until
            // they are back on their spot or gone (the E3 review's M1)
            EncPerson p;
            return !(bodies.TryGetValue(e.entityId, out p) && p.Leaving);
        }

        private static bool Opposes(EntityAlive e)
        {
            if (e is EntityPlayer) return true;
            EntityBuffs bf = e.Buffs;
            if (bf == null) return false;
            if (bf.HasCustomVar("sc_member") || bf.HasCustomVar(ScRadio.StandInCvar)) return true;
            int leader = ScCommunity.HiredBy(e);
            if (leader <= 0) return false;
            World w = ScCommunity.World;
            // the player list, never World.GetEntity (it would finish a pending build inside the AI's frame: the review's L3)
            return w != null && w.Players != null && w.Players.dict.ContainsKey(leader);
        }

        /// <summary>ScEnclaves.TurnHostile: the live bodies stamped (X9, X10), the grace set, the lookup rebuilt.</summary>
        public static void OnTurnedHostile(Enclave e, float grace)
        {
            e.GraceUntil = Time.unscaledTime + Math.Max(0f, grace);
            foreach (EncPerson p in bodies.Values)
            {
                if (p.EnclaveId != e.Id) continue;
                EntityAlive npc = BodyOf(p);
                if (npc != null) npc.Buffs.SetCustomVar(HostileCvar, 1f);
            }
            RebuildHostile();
        }

        /// <summary>The tick: a threat's grace that ended switches the enclave's hostility on.</summary>
        private static void Grace()
        {
            float t = Time.unscaledTime;
            foreach (Enclave e in ScEnclaves.All)
                if (e.TurnedHostile && !e.Lost && t >= e.GraceUntil && !hostileActive.Contains(e.Id)) { RebuildHostile(); return; }
        }

        public static void RebuildHostile()
        {
            var s = new HashSet<int>();
            float t = Time.unscaledTime;
            if (ScConfig.Enclaves)
                foreach (Enclave e in ScEnclaves.All)
                    if (e.TurnedHostile && !e.Lost && t >= e.GraceUntil) s.Add(e.Id);
            hostileActive = s;
            anyHostile = s.Count > 0;
        }

        // ------------------------------------------------------------------ attacks and threats (Q6 a) ----

        /// <summary>
        /// Patches_Trust's hit hook, for a player's landed hit (host; never the killing blow - the death has its own rule):
        /// an enclave body's hits go to the enclave's rules instead of ScTrust's per-person grudge. Hits within
        /// EnclaveHitDebounce of the last counted one are the same attack (a shotgun's pellets, a blast); the first counted
        /// hit is a warning (the person's "Watch it!", -EnclaveWarnPenalty standing); another within EnclaveWarnWindow of the
        /// warning turns the whole enclave hostile; after the window a hit is a fresh warning. A hostile enclave (raiders, or
        /// turned) changes nothing. True for any enclave body (handled here, whatever the outcome).
        /// </summary>
        public static bool OnHitBy(EntityAlive npc, EntityPlayer player)
        {
            if (npc == null || npc.Buffs == null || !npc.Buffs.HasCustomVar(EncCvar)) return false;
            EncPerson p = PersonOf(npc);
            Enclave e = EnclaveOf(p);
            if (e == null || player == null || e.IsHostile) return true;
            float t = Time.unscaledTime;
            if (e.LastHitAt >= 0f && t - e.LastHitAt < ScConfig.EnclaveHitDebounce) return true;
            e.LastHitAt = t;
            if (e.WarnedAt >= 0f && t - e.WarnedAt <= ScConfig.EnclaveWarnWindow)
            {
                ScEnclaves.TurnHostile(e, player.EntityName + " hit " + p.Name + " again within " + Mathf.RoundToInt(ScConfig.EnclaveWarnWindow) + " s of the warning");
                return true;
            }
            e.WarnedAt = t;
            ScCommunity.Tell(player, string.Format(Localization.Get("scEncWarn"), p.Name), "ui_denied");
            ScEnclaves.AddStanding(e, -ScConfig.EnclaveWarnPenalty, player.EntityName + " hit " + p.Name + " (a warning)");
            return true;
        }

        /// <summary>The share of a threat's bundles that are the resource their kind lives on (the rest: any classic one).</summary>
        private const float ThreatKindShare = 0.65f;

        /// <summary>The resource a kind of group lives on; a general group's is drawn from the classic five.</summary>
        public static Resource KindResource(string kind, GameRandom r)
        {
            switch (kind)
            {
                case "growers": return Resource.Food;
                case "medics": return Resource.Meds;
                case "militia": return Resource.Ammo;
                case "builders": return Resource.Materials;
                case "roadcrew": return Resource.Fuel;
                default: return (Resource)(r != null ? r.RandomRange(0, ScNames.ClassicCount) : 0);
            }
        }

        /// <summary>A kind's name for a line ("growers", "road crew"; Localization scEncKind*).</summary>
        public static string KindName(string kind)
        {
            switch (kind)
            {
                case "growers": return Localization.Get("scEncKindGrowers");
                case "medics": return Localization.Get("scEncKindMedics");
                case "militia": return Localization.Get("scEncKindMilitia");
                case "builders": return Localization.Get("scEncKindBuilders");
                case "roadcrew": return Localization.Get("scEncKindRoadcrew");
                default: return Localization.Get("scEncKindGeneral");
            }
        }

        /// <summary>
        /// The leader's "I'm not asking." (Q6 a; DialogActionScEncThreat): EnclaveThreatBundlesMin..Max Medium bundles of what
        /// they have - most of the resource their kind lives on, the rest any classic one - into the player's bag, the
        /// overflow at their feet; then the enclave is hostile for good, after EnclaveThreatGrace seconds so the dialog
        /// closing is not a shot in the back. Host only.
        /// </summary>
        public static void Threaten(EntityPlayerLocal player, EntityAlive npc)
        {
            if (!ScCommunity.IsHost || player == null) return;
            EncPerson p = PersonOf(npc);
            Enclave e = EnclaveOf(p);
            if (e == null || !p.IsLeader || e.IsHostile) return;
            World w = ScCommunity.World;
            GameRandom r = w != null ? w.GetGameRandom() : null;
            int lo = Math.Max(0, ScConfig.EnclaveThreatBundlesMin), hi = Math.Max(lo, ScConfig.EnclaveThreatBundlesMax);
            int n = r != null ? r.RandomRange(lo, hi + 1) : lo;
            Resource main = KindResource(e.Kind, r);
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
                int g = ScStockpile.GiveBundles(null, (Resource)i, BundleSize.Medium, counts[i], player);
                given += g;
                if (g > 0) parts.Add(g + " " + Localization.Get(ScNames.LocKey((Resource)i)));
            }
            string what = parts.Count > 0 ? string.Join(", ", parts.ToArray()) : "0";
            ScOutposts.NoteLedger(string.Format(Localization.Get("scEncLedgerThreat"), e.Name, what));
            ScLog.Out("enclave " + e.Id + " '" + e.Name + "': " + player.EntityName + " threatened " + p.Name + " and took " + given + " medium bundle(s) (" + what + ")");
            ScEnclaves.TurnHostile(e, "threatened by " + player.EntityName, ScConfig.EnclaveThreatGrace);
        }

        // ------------------------------------------------------------------ wild spawns kept off ----

        /// <summary>SpawnManagerBiomes.SpawnUpdate's prefix: a biome spawn starts (main thread).</summary>
        public static void BeginBiomeSpawn()
        {
            biomeSpawn = true;
            refusedThisUpdate = false;
            WildSpotNoted = false;
        }

        /// <summary>...and its finalizer: it ended, however.</summary>
        public static void EndBiomeSpawn()
        {
            biomeSpawn = false;
            WildSpotNoted = false;
        }

        /// <summary>World.GetRandomSpawnPositionInAreaMinMaxToPlayers' postfix: the spot a biome spawn chose (its only caller).</summary>
        public static void NoteSpawnPosition(Vector3 at)
        {
            if (!biomeSpawn) return;
            noted = at;
            WildSpotNoted = true;
        }

        /// <summary>
        /// The user's decision of 2026-10-16: a biome spawn's NPC (an npc-tagged class that is no animal: SC's and XNPC's wild
        /// survivors and raiders; zombies and animals pass) at a spot within EnclaveWildSpawnMargin of a living enclave's
        /// footprint is refused - class 0, which SpawnUpdate takes for "none" (DecMaxCount, no body). Counted once per update.
        /// </summary>
        public static void FilterWild(ref int classId)
        {
            if (!WildSpotNoted || classId == 0 || !ScConfig.Enclaves) return;
            EntityClass ec;
            if (!EntityClass.list.TryGetValue(classId, out ec) || ec == null) return;
            if (!ec.Tags.Test_AnySet(NpcTag) || ec.Tags.Test_AnySet(ScCommunity.AnimalTags)) return;
            if (!ScEnclaves.NearEnclave(noted, ScConfig.EnclaveWildSpawnMargin)) return;
            classId = 0;
            if (refusedThisUpdate) return;
            refusedThisUpdate = true;
            wildRefused++;
            ScLog.Verbose("wild spawn of " + ec.entityClassName + " at " + World.worldToBlockPos(noted) + " refused: within " + ScConfig.EnclaveWildSpawnMargin + " m of an enclave");
        }

        public static int WildRefused { get { return wildRefused; } }

        // ------------------------------------------------------------------ containers ----

        /// <summary>The living enclave whose footprint holds this block (EnclaveContainerLock), or null. Main thread.</summary>
        public static Enclave ContainerOwner(Vector3i pos)
        {
            return ScConfig.EnclaveContainerLock ? ScEnclaves.FirstIn(pos, pos) : null;
        }

        // ------------------------------------------------------------------ console ----

        /// <summary>sc enclave info: one line on the people.</summary>
        public static string Summary(Enclave e)
        {
            int all = 0, dead = 0, out_ = 0, enlisted = 0, raiding = 0;
            string leader = null;
            foreach (EncPerson p in ScRegistry.EncPeople)
            {
                if (p.EnclaveId != e.Id) continue;
                all++;
                if (p.State == EncPerson.StateDead) dead++;
                else if (p.IsLeader) leader = p.Name;
                if (p.Enlisted) enlisted++;
                if (p.Raiding) raiding++;
                if (p.EntityId >= 0) out_++;
            }
            // E12: people who left them to join you and are still on the way (a join cut short: the rest stand)
            int joining = ScEncJoin.ArrivingFrom(e);
            if (all == 0) return "people not made yet (" + GroupSize(e) + " when a player first comes within " + ScConfig.EnclaveSpawnRange + " m)"
                                 + (joining > 0 ? ", " + joining + " on the way to join you" : "");
            return (all - dead) + " living" + (dead > 0 ? ", " + dead + " dead" : "") + (e.PeopleRolled ? "" : " (still being made: " + GroupSize(e) + " in all)")
                   + ", led by " + (leader ?? "nobody") + ", " + out_ + " in the world" + (enlisted > 0 ? ", " + enlisted + " with you" : "")
                   + (raiding > 0 ? ", " + raiding + " out raiding" : "") + (joining > 0 ? ", " + joining + " on the way to join you" : "");
        }

        /// <summary>sc enclave people &lt;id&gt; (E3: who is enlisted, by whom and until when; the recruit cooldown)</summary>
        public static void PeopleLines(Enclave e, List<string> into)
        {
            into.Add("#" + e.Id + " " + e.Name + ": " + Summary(e));
            int wait = ScEnclaveDeals.CooldownLeft(e);
            into.Add("  recruiting: " + (e.IsHostile ? "never (hostile)" : !e.PeopleRolled ? "not while the group is being made" : wait > 0 ? "the next one from day "
                     + (e.LastRecruitDay + ScConfig.EnclaveRecruitCooldownDays) + " (" + wait + " day(s))" : "open") + (e.LastRecruitDay >= 0 ? ", the last on day " + e.LastRecruitDay : "")
                     + ", at least " + Math.Max(1, ScConfig.EnclaveRecruitMinLeft) + " stay");
            World w = ScCommunity.World;
            foreach (EncPerson p in PeopleOf(e))
            {
                EntityAlive npc = BodyOf(p);
                EntityPlayer by = p.Enlisted && w != null ? w.GetEntity(p.EnlistedBy) as EntityPlayer : null;
                Prospect fav = ScEnclaveDeals.ProspectByPerson(p.Id);
                into.Add("  p" + p.Id + " " + (p.IsLeader ? "leader" : "member") + " " + p.Name + " (" + p.Who.Class + ") - " + p.State
                         + (p.State == EncPerson.StateDead ? " since day " + p.DiedDay : "")
                         + (p.Enlisted ? " with " + (by != null ? by.EntityName : "entity " + p.EnlistedBy) + " until day " + GameUtils.WorldTimeToDays(p.EnlistedUntil) + " "
                            + GameUtils.WorldTimeToHours(p.EnlistedUntil).ToString("00") + ":" + GameUtils.WorldTimeToMinutes(p.EnlistedUntil).ToString("00") : "")
                         + (p.Leaving ? " (walking home)" : "")
                         + (p.Raiding ? " (" + ScEncRaids.WhereText(p) + ")" : "")
                         + ", health " + Mathf.RoundToInt(p.Health * 100f) + "%, spot " + (p.HasHome ? p.Home + " yaw " + Mathf.RoundToInt(p.Yaw) : "not chosen yet")
                         + ", " + (p.EntityId >= 0 ? "entity " + p.EntityId + (npc != null ? " at " + World.worldToBlockPos(npc.position) + ", " + npc.Health + "/" + npc.GetMaxHealth() + " hp" : "") : "no body")
                         + (fav != null ? ", favour: " + ScFavours.Describe(fav) + (fav.Done ? " (done)" : "") + " (key " + fav.Id + ")" : ""));
            }
        }

        /// <summary>sc enclave spawn &lt;id&gt;: its people now (made first when needed), the view rule ignored; its chunks must be loaded.</summary>
        public static string SpawnNow(Enclave e)
        {
            World w = ScCommunity.World;
            if (w == null || !ScCommunity.IsHost) return "host only, with a world";
            if (!ScConfig.Enclaves) return "Enclaves is off in the config";
            if (e.Lost) return "its POI is not in this world";
            if (!ChunksSettled(w, e)) return "its POI's chunks are not loaded and settled - go nearer";
            int made = SpawnAll(e);
            if (!ScEnclaves.All.Contains(e)) return "nobody of the group was alive and it needs no more - the enclave is gone";
            return made + " body(ies) built - " + Summary(e);
        }

        /// <summary>
        /// Everyone at home in the world at once (sc enclave spawn; E5: a siege's arrival): the group made first while it is
        /// still being made, then a body for each person at home who has none - forced, E2's pace and the view rule ignored.
        /// The POI's chunks must be settled (-1 when they are not). Returns how many bodies were built.
        /// </summary>
        internal static int SpawnAll(Enclave e)
        {
            World w = ScCommunity.World;
            if (w == null || e == null || e.Lost || !ChunksSettled(w, e)) return -1;
            Hook(w);
            VisitOf(e.Id).NoSpawnUntil = 0f;
            int made = 0, guard = 0;
            while (!e.PeopleRolled && guard++ < 16)
            {
                if (Generate(w, e, true)) made++;
                else if (!e.PeopleRolled) break;
            }
            if (!ScEnclaves.All.Contains(e)) return made;
            foreach (EncPerson p in PeopleOf(e))
            {
                if (p.State != EncPerson.StateHome || p.EntityId >= 0) continue;
                p.SpawnFailures = 0;
                if (Spawn(w, e, p, true)) made++;
            }
            return made;
        }

        /// <summary>sc enclave despawn &lt;id&gt;: its bodies go now (state recorded) and stay away ConsoleDespawnSeconds.</summary>
        public static string DespawnNow(Enclave e)
        {
            World w = ScCommunity.World;
            if (w == null || !ScCommunity.IsHost) return "host only, with a world";
            int n = DespawnEnclave(w, e);
            Visit v = VisitOf(e.Id);
            v.NoSpawnUntil = Time.unscaledTime + ConsoleDespawnSeconds;
            Release(v, null);
            if (n > 0) ScRegistry.SaveAtomic();
            return n + " body(ies) taken out (health and rounds recorded); none comes back for " + Mathf.RoundToInt(ConsoleDespawnSeconds) + " s";
        }

        /// <summary>
        /// sc enclave kill &lt;id&gt; [&lt;person id&gt;|all] (no argument: the leader): marked dead, the body removed (no
        /// corpse). "all" is the whole group: those not made yet never will be, so it ends in the fall (E5: held off while one
        /// of theirs is missing and may be alive - ScEncSearch.AnyAlive; a second "all", with nobody alive, forces it).
        /// </summary>
        public static string Kill(Enclave e, string arg)
        {
            if (PeopleOf(e).Count == 0) return "its people are not made yet (come near, or sc enclave spawn " + e.Id + " there)";
            bool all = arg != null && arg.Equals("all", StringComparison.OrdinalIgnoreCase);
            if (all && !e.PeopleRolled) e.PeopleRolled = true;
            List<EncPerson> living = Living(e);
            if (living.Count == 0)
            {
                if (!all) return "nobody alive there";
                ScEnclaves.Fall(e, "everyone died (the console)");
                return "nobody was alive - the enclave is gone";
            }
            var targets = new List<EncPerson>();
            if (string.IsNullOrEmpty(arg))
            {
                EncPerson l = LeaderOf(e);
                targets.Add(l ?? living[0]);
            }
            else if (all) targets.AddRange(living);
            else
            {
                int pid;
                if (!int.TryParse(arg.TrimStart('p', 'P'), out pid)) return "give a person id (sc enclave people " + e.Id + ") or all";
                foreach (EncPerson p in living) if (p.Id == pid) targets.Add(p);
                if (targets.Count == 0) return "no living person p" + pid + " in " + e.Name;
            }
            int n = 0;
            foreach (EncPerson p in targets)
            {
                if (!ScEnclaves.All.Contains(e)) break;           // the last one took the enclave with them
                EntityAlive npc = BodyOf(p);
                // E3: one enlisted with the player dies as if at their side (the penalty too: the console tests it)
                bool wasEnlisted = p.Enlisted;
                bool wasRaiding = p.Raiding;          // E10: one out raiding dies as a loss of the raid
                if (wasEnlisted) ScEnclaveDeals.EndEnlist(p, OwnBody(p), null, "killed from the console", false);
                if (npc != null && !npc.IsDead()) Remove(p, npc);
                else Unbody(p, false);
                Died(e, p, null, false, wasEnlisted, false, wasRaiding);
                n++;
            }
            return n + " marked dead - " + (ScEnclaves.All.Contains(e) ? Summary(e) : "the enclave is gone");
        }
    }
}
