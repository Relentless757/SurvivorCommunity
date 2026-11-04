using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// A band on the road, milestone E13a (research\ideas\DESIGN.md section 10, idea 9, the first cut of Q12: drifters and
    /// raider bands): a registry record - who they are (BandPerson), the building they set out from and the one they head
    /// for, and where the current stretch of their walk began and when. Their place is worked out from the world clock
    /// (ScBands.VirtualPos), never stored, and their people have bodies only near a player (ScBands). Saved as [band]
    /// (ScRegistry; forward-only: an older build skips the section's lines and drops it at its next save).
    /// </summary>
    public sealed class Band
    {
        public const string Drifters = "drifters", Raiders = "raiders";

        public int Id;
        /// <summary>Drifters (survivors looking for somewhere to live: they talk, and may come to your place) or raiders (they attack on sight).</summary>
        public string Kind = Drifters;
        /// <summary>"Mara's band" (raiders: "Mara's crew"), named for their first leader once their people are made; "" before.</summary>
        public string Name = "";
        /// <summary>How many they are (BandSizeMin..Max), and whether their people were made (the first time a player came near them).</summary>
        public int Size;
        public bool PeopleMade;
        /// <summary>The building they set out from and the one they head for (POI keys and names); ToEnclave: the enclave holding it, else 0.</summary>
        public Vector3i From, To;
        public string FromName = "", ToName = "";
        public int ToEnclave;
        /// <summary>Where they walk to: just outside the destination's footprint, on the side they come from (y: the heightmap).</summary>
        public Vector3i ToAt;
        /// <summary>
        /// The walk: the current stretch began at At (y: the heightmap) at world time Depart (later than now: they set out then)
        /// and goes by Via (the bend round the player's home zone, while HasVia: still ahead) to ToAt, at BandMetresPerHour a
        /// game hour outside the camp hours. When their bodies go, a new stretch starts from where they really were.
        /// </summary>
        public Vector3i At;
        public ulong Depart;
        public Vector3i Via;
        public bool HasVia;
        /// <summary>The request-clock day the trip began (a trip still going after MaxTripDays ends once nobody sees them).</summary>
        public int StartDay;
        /// <summary>Seen by the player: their marker shows while they travel. KnownDay: the day it happened (-1: never).</summary>
        public bool Known;
        public int KnownDay = -1;
        /// <summary>Their own random stream (their people's classes, their answer to "Come to our place.").</summary>
        public int Seed;
        /// <summary>Drifters: 0 wary, 1 trusting the player (helped in trouble), -1 against the player (one of them hit or killed).</summary>
        public int Trust;
        /// <summary>Drifters: 0 none, 1 zombies on them when they are met (owed), 2 the fight is on (this session only), 3 over.</summary>
        public int Trouble;
        /// <summary>At their destination: no marker; their bodies go once nobody sees them, then the record.</summary>
        public bool Arrived;

        // ---- session only (never saved) ----
        /// <summary>When the ground under their place first read settled (bodies wait one full tick, as runners do); -1 when it does not.</summary>
        public float SettledSince = -1f;
        /// <summary>No bodies before this (real time): after a stuck walk went off the map, or the console.</summary>
        public float NoSpawnUntil;
        /// <summary>The leader's walk point and when it was given (real time; -1: not yet).</summary>
        public Vector3 Target;
        public float SteeredAt = -1f;
        /// <summary>The stuck watch: where the leader last made progress, and since when (real time; -1: not watching).</summary>
        public Vector3 ProgressPos;
        public float ProgressSince = -1f;
        /// <summary>The arrival watch near the end: the leader's nearest to ToAt so far (metres), and since when (real time; -1: not watching).</summary>
        public float GoalBest;
        public float GoalBestAt = -1f;
        /// <summary>Camping now (their camp spots given round CampAt).</summary>
        public bool Camped;
        public Vector3 CampAt;
        /// <summary>Where the body leading them last stood (a walk that loses every body without the unload capture starts again there).</summary>
        public Vector3 LastPos;
        public bool HasLastPos;
        /// <summary>A talk with one of them holds them all where they stand (ScBands.InTalk).</summary>
        public bool Halted;
        /// <summary>Raiders, or drifters against the player: they have the player in their sights (the whole band attacks).</summary>
        public bool Engaged;
        public float NextAssert;
        /// <summary>Their map marker (a moving one: its tracked position follows them).</summary>
        public NavObject Marker;
        public Vector3 MarkerAt;
        /// <summary>The trouble's zombies (entity ids) while it is on, how many of them a player killed, the next target check.</summary>
        public readonly List<int> TroubleIds = new List<int>();
        public int Helped;
        public float TroubleAssert;
        /// <summary>Their roll for "Come to our place." (ScBands.JoinRoll; -1: not drawn yet this session).</summary>
        public float JoinRoll = -1f;

        public bool IsRaiders { get { return Kind == Raiders; } }
        /// <summary>They attack the player: raiders always, drifters once against them.</summary>
        public bool Hostile { get { return IsRaiders || Trust < 0; } }
    }

    /// <summary>
    /// One of a band's people (E13a): the person themself is a Member (Who) - class, name, look, weapons, rounds, skills,
    /// traits - so ScSpawn.Body rebuilds them like any known survivor and "Come to our place." hands the same record to the
    /// community (a member on the way, E12's path). Who.Id and Who.Born stay 0: a body is never saved, and the one in the
    /// world this session is EntityId. Saved as [bandperson] (forward-only, as [band]).
    /// </summary>
    public sealed class BandPerson
    {
        public const string Leader = "leader", MemberRole = "member", StateAlive = "alive", StateDead = "dead";

        public int Id;
        public int BandId;
        public string Role = MemberRole;
        public string State = StateAlive;
        public int DiedDay = -1;
        /// <summary>Health as a share of the body's maximum (0..1), recorded when the body goes.</summary>
        public float Health = 1f;
        public Member Who = new Member();

        // ---- session only (never saved) ----
        /// <summary>The body in the world (-1: none) and its birth time (a recycled id is not theirs); the one that went last.</summary>
        public int EntityId = -1;
        public ulong EntityBorn;
        public int LastEntityId = -1;
        public int SpawnFailures;

        public bool IsLeader { get { return Role == Leader; } }
        public bool Alive { get { return State == StateAlive; } }
        public string Name { get { return Who.Name; } }
    }

    /// <summary>
    /// Bands on the road, milestone E13a (research\ideas\DESIGN.md section 10, idea 9; the user's Q12: drifters and raider
    /// bands first, errands, traders and E10/E11 bands later; single player).
    ///
    /// Trips (Daily, from ScDaily.Run, once per request-clock day). With Bands on and fewer than BandMax on the road, a rollover
    /// rolls BandDailyChance for a new band: raiders with BandRaiderShare, else drifters (BandTroubleChance of them owe zombies
    /// when met), BandSizeMin..Max people, between two real buildings BandTripMin..BandTripMax m apart on a line that passes
    /// within BandPassRange m of the player's home (of the player without one). Neither end is infested, an outpost, claimed or
    /// the home; a drifter band may head for a friendly enclave's building, raiders for a raider den, nobody sets out from one,
    /// or from a building a player is near. A line through the home zone bends round its edge (one extra point, Via). They set
    /// out up to three hours after the rollover.
    ///
    /// The walk. Their place is derived on the world clock (VirtualPos: BandMetresPerHour a game hour along At -> Via -> ToAt,
    /// halted from BandCampFrom to BandCampTo); nothing walks the map unloaded. Within BandSpawnRange m of a player, on settled
    /// ground, bodies come one a second, in file (or round their camp), never within BandNoPopRange m in a player's view; the
    /// first time their people are made from strangers of SC's wild classes for the gamestage tier (as E2 makes an enclave's)
    /// and kept. A band's bodies hold the Guard order at the leader's next walk point (ScRunners.NextWaypoint, re-given when
    /// reached or every ResteerSeconds), the others in file behind, on 0-XNPCCore's own packages (E12's husks walk the same
    /// way); at night they camp. They go - health and rounds recorded, the walk re-planned from where the leader really was -
    /// once no player is within BandDespawnRange m and none sees them, when a chunk unloads under them, and when the world shuts
    /// down; stuck and unseen, they go and walk on unseen. Never saved (the IsSavedToFile veto); a copy a crash brings back is
    /// swept (Orphans). No band comes near a live base attack, outpost fight, enclave siege or blood moon.
    ///
    /// Meeting them. Seen (a body in a player's view, or one within KnowRange m), a band gets a marker that follows it while it
    /// travels (drifters grey, raiders red), a tell and a ledger line; the marker goes when they arrive or are destroyed.
    /// Raiders attack on sight: once one of them sees the player within AggroRange m the whole band takes the player as its
    /// target. Drifters talk (Config/dialogs.xml, DialogOpsBands.cs): "Where are you headed?", "Stay safe." and, with a home
    /// with room for all of them, "Come to our place." - they say yes when they trust the player, else by their own roll
    /// (BandJoinChance, the same each time asked), and join through E12's path: each a member on the way (ScEncJoin.OnTheWay),
    /// their bodies walking off toward home (husks), turning up at home after the time it takes to walk there. Drifters met in
    /// trouble have BandTroubleZombies zombies on them; a player who kills one of those earns their trust and a thanks; however
    /// the trouble ends, its zombies are let go. A hit by the player (OnHitBy, Patches_Trust's hook, ahead of ScTrust's
    /// wild-survivor rule, which leaves band bodies out) or a kill turns the whole drifter band against the player.
    ///
    /// Deaths are real. A band whose people are all dead is destroyed (a tell near it, a ledger line once met) and gone; one
    /// that arrives is gone too. Host only, main thread, single player; the save thread's veto reads a volatile snapshot.
    /// </summary>
    public static class ScBands
    {
        /// <summary>On every band body: its band's id ('%': synced; the dialog's hides and the orphan sweep read it).</summary>
        public const string BandCvar = "%sc_band";
        private const string Salt = "sc.bands.v1";
        /// <summary>Bodies made a second across all bands (E2's EnclaveSpawnPerTick idea).</summary>
        private const int SpawnPerTick = 1;
        /// <summary>A bend round the home zone keeps this far outside its corners.</summary>
        private const float ZoneMargin = 30f;
        /// <summary>The leader counts as at a point (the bend, the destination) within these many metres.</summary>
        private const float ViaReached = 20f, ArriveReach = 10f;
        /// <summary>
        /// The end the ground keeps them from (ToAt is a heightmap point, never checked: a fenced yard, a wreck, water): within
        /// ArriveNear of it (ArriveReach plus NextWaypoint's 24 m sidestep) and no nearer by ArriveGainMetres in
        /// ArriveStallSeconds while walking, they are there.
        /// </summary>
        private const float ArriveNear = 34f, ArriveGainMetres = 2f, ArriveStallSeconds = 30f;
        /// <summary>The leader's next walk point when within this of the last, or this many real seconds after it.</summary>
        private const float StepReached = 4f, ResteerSeconds = 15f;
        /// <summary>The stuck watch: less than StuckMetres in StuckSeconds while walking; then no bodies for StuckRespawnSeconds.</summary>
        private const float StuckMetres = 3f, StuckSeconds = 60f, StuckRespawnSeconds = 90f;
        /// <summary>A body this far from its leader, seen by nobody, is put back in file.</summary>
        private const float StraggleRange = 50f;
        /// <summary>The camp's circle, the file's spacing and its side step.</summary>
        private const float CampRadius = 3f, FileSpacing = 2.5f, SideSpacing = 1.5f;
        /// <summary>Raiders see the player within this, and look at their targets this often (real seconds).</summary>
        private const float AggroRange = 60f, AssertSeconds = 5f;
        private const int AttackTargetTicks = 1200;
        /// <summary>A player this close to one of them has met them, seen or not.</summary>
        private const float KnowRange = 30f;
        /// <summary>The trouble's zombies come this far from the band, on the side away from the player.</summary>
        private const float TroubleMin = 14f, TroubleMax = 24f;
        /// <summary>A trip still going after this many request-clock days ends once nobody sees them (they left the area).</summary>
        private const int MaxTripDays = 3;
        /// <summary>The walk clock never counts more than this many days (a world clock moved far ahead).</summary>
        private const ulong MaxWalkDays = 30UL;
        /// <summary>Markers are moved this often (real seconds), when they moved this far.</summary>
        private const float MarkerSeconds = 5f, MarkerMove = 10f;
        private const int RouteTries = 1500;
        /// <summary>The console's band starts this many metres before the player, heading through them.</summary>
        private const float ConsoleLead = 140f;
        private const string NavDrifters = "sc_band_drifters", NavRaiders = "sc_band_raiders";
        private const int DefaultMood = 50;
        private static readonly Vector3 Nudge = new Vector3(0f, 0.002f, 0f);

        /// <summary>Entity id -> the person whose current body it is. Main thread.</summary>
        private static readonly Dictionary<int, BandPerson> bodies = new Dictionary<int, BandPerson>();
        /// <summary>Every band body built this session: id -> (class id, birth time), swapped whole; read by the save thread's veto.</summary>
        private static volatile Dictionary<int, KeyValuePair<int, ulong>> vetoIds = new Dictionary<int, KeyValuePair<int, ulong>>();
        /// <summary>Bodies with %sc_band that a chunk or a save brought back (the PostInit postfix): removed on the next tick.</summary>
        public static readonly HashSet<int> Orphans = new HashSet<int>();
        /// <summary>The route's points (Route), reused: main thread only.</summary>
        private static readonly Vector3[] pts = new Vector3[3];
        private static int capturingId = -1;
        private static World hooked;
        private static bool markersRestored;
        private static float nextMarkers;
        /// <summary>The request-clock day the daily pass last ran (a catch-up's later passes share the day and do nothing).</summary>
        private static int passDay = -1;

        public static void Reset()
        {
            bodies.Clear();
            vetoIds = new Dictionary<int, KeyValuePair<int, ulong>>();
            Orphans.Clear();
            capturingId = -1;
            markersRestored = false;
            nextMarkers = 0f;
            passDay = -1;
            Unhook();
        }

        /// <summary>
        /// The registry was (re)loaded or reset (ScOutposts.OnRegistryChanged): ids squared and the counters ahead of them, no
        /// body known (bodies are session-only), markers restored on the next tick.
        /// </summary>
        public static void OnRegistryChanged()
        {
            bodies.Clear();
            markersRestored = false;
            passDay = -1;
            Community c = ScRegistry.Community;
            var ids = new HashSet<int>();
            int max = 0;
            foreach (Band b in ScRegistry.Bands) if (b.Id > max) max = b.Id;
            foreach (Band b in ScRegistry.Bands)
            {
                if (b.Id <= 0 || !ids.Add(b.Id))
                {
                    int old = b.Id;
                    b.Id = Math.Max(c.NextBandId, max) + 1;
                    max = b.Id;
                    ids.Add(b.Id);
                    ScLog.Warn("band '" + b.Name + "' had a duplicate id " + old + " - now " + b.Id);
                }
                if (b.Id > c.NextBandId) c.NextBandId = b.Id;
                if (b.Trouble == 2) b.Trouble = 3;          // its zombies never outlive a load
            }
            var pids = new HashSet<int>();
            int pmax = 0;
            foreach (BandPerson p in ScRegistry.BandPeople) if (p.Id > pmax) pmax = p.Id;
            foreach (BandPerson p in ScRegistry.BandPeople)
            {
                p.EntityId = p.LastEntityId = -1;
                p.SpawnFailures = 0;
                if (p.Id <= 0 || !pids.Add(p.Id))
                {
                    p.Id = Math.Max(c.NextBandPersonId, pmax) + 1;
                    pmax = p.Id;
                    pids.Add(p.Id);
                }
                if (p.Id > c.NextBandPersonId) c.NextBandPersonId = p.Id;
            }
        }

        private static string Loc(string key) { return Localization.Get(key); }

        // ------------------------------------------------------------------ the switch, lookups ----

        public static bool Configured { get { return ScConfig.Bands; } }

        /// <summary>One of a band's bodies, by its cvar (any machine).</summary>
        public static bool IsBandNpc(EntityAlive e)
        {
            return e != null && e.Buffs != null && e.Buffs.HasCustomVar(BandCvar);
        }

        /// <summary>
        /// Thread-safe (the IsSavedToFile veto, through ScRunners.VetoChunkSave): a band body built this session - same id, class
        /// and birth time. Reference tests only (an unloaded body is written after its GameObject was destroyed).
        /// </summary>
        public static bool IsBandBody(Entity e)
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

        /// <summary>The person whose current body this is, or null (one dictionary hit).</summary>
        public static BandPerson PersonOf(EntityAlive npc)
        {
            BandPerson p;
            return npc != null && bodies.TryGetValue(npc.entityId, out p) && p.EntityBorn == npc.WorldTimeBorn ? p : null;
        }

        public static Band ById(int id)
        {
            foreach (Band b in ScRegistry.Bands) if (b.Id == id) return b;
            return null;
        }

        public static List<BandPerson> PeopleOf(Band b)
        {
            var list = new List<BandPerson>();
            foreach (BandPerson p in ScRegistry.BandPeople) if (p.BandId == b.Id) list.Add(p);
            return list;
        }

        public static List<BandPerson> Living(Band b)
        {
            var list = new List<BandPerson>();
            foreach (BandPerson p in ScRegistry.BandPeople) if (p.BandId == b.Id && p.Alive) list.Add(p);
            return list;
        }

        public static BandPerson LeaderOf(Band b)
        {
            foreach (BandPerson p in ScRegistry.BandPeople) if (p.BandId == b.Id && p.Alive && p.IsLeader) return p;
            return null;
        }

        private static EntityAlive BodyOf(BandPerson p)
        {
            World w = ScCommunity.World;
            EntityAlive npc = p != null && p.EntityId >= 0 && w != null ? ScNpc.As(ScNpc.Loaded(w, p.EntityId)) : null;
            return npc != null && npc.WorldTimeBorn == p.EntityBorn ? npc : null;
        }

        /// <summary>Their name for a line: "Mara's band", or "drifters" / "raiders" before their people are made.</summary>
        public static string DisplayName(Band b)
        {
            return b.Name.Length > 0 ? b.Name : KindWord(b);
        }

        public static string KindWord(Band b) { return Loc(b.IsRaiders ? "scBandKindRaiders" : "scBandKindDrifters"); }

        /// <summary>Where they head: the building's name, an enclave's ("The Holdouts at Miller Farmhouse") when one holds it.</summary>
        public static string DestText(Band b)
        {
            Enclave e = b.ToEnclave > 0 ? ScEnclaves.ById(b.ToEnclave) : null;
            return e != null && !e.Lost ? string.Format(Loc("scBandWhereEnclave"), e.Name, b.ToName) : b.ToName;
        }

        // ------------------------------------------------------------------ the walk ----

        private static float Speed { get { return Mathf.Max(1f, ScConfig.BandMetresPerHour); } }

        private static Vector3 V(Vector3i v) { return new Vector3(v.x + 0.5f, v.y, v.z + 0.5f); }

        private static Vector3i B(Vector3 v) { return new Vector3i(Mathf.FloorToInt(v.x), Mathf.FloorToInt(v.y), Mathf.FloorToInt(v.z)); }

        /// <summary>A point on the ground by the static heightmap (it works with the chunk unloaded).</summary>
        private static Vector3 OnHeightmap(Vector3 p)
        {
            World w = ScCommunity.World;
            float y = w != null ? w.GetHeightAt(p.x, p.z) : 0f;
            p.y = y > 0f ? y : p.y;
            return p;
        }

        /// <summary>Is this world time in the camp hours (BandCampFrom to BandCampTo, across midnight when From is the later)?</summary>
        public static bool IsCamp(ulong t)
        {
            int f = ScConfig.BandCampFrom, to = ScConfig.BandCampTo;
            if (f == to) return false;
            int h = (int)(t % 24000UL / 1000UL);
            return f > to ? h >= f || h < to : h >= f && h < to;
        }

        private static ulong Overlap(ulong a, ulong b, ulong x, ulong y)
        {
            ulong lo = Math.Max(a, x), hi = Math.Min(b, y);
            return hi > lo ? hi - lo : 0UL;
        }

        /// <summary>World ticks in [a, b) outside the camp hours (at most MaxWalkDays days are counted).</summary>
        private static ulong WalkTicks(ulong a, ulong b)
        {
            if (b <= a) return 0UL;
            if (b - a > MaxWalkDays * 24000UL) b = a + MaxWalkDays * 24000UL;
            int f = ScConfig.BandCampFrom, to = ScConfig.BandCampTo;
            ulong sum = 0UL;
            for (ulong d = a / 24000UL; d * 24000UL < b; d++)
            {
                ulong s = d * 24000UL;
                if (f == to) sum += Overlap(a, b, s, s + 24000UL);
                else if (f > to) sum += Overlap(a, b, s + (ulong)to * 1000UL, s + (ulong)f * 1000UL);
                else sum += Overlap(a, b, s, s + (ulong)f * 1000UL) + Overlap(a, b, s + (ulong)to * 1000UL, s + 24000UL);
            }
            return sum;
        }

        /// <summary>The route still to walk into pts: At, the bend while ahead, ToAt. Returns how many points.</summary>
        private static int Route(Band b)
        {
            pts[0] = V(b.At);
            int n = 1;
            if (b.HasVia) pts[n++] = V(b.Via);
            pts[n++] = V(b.ToAt);
            return n;
        }

        private static float RouteLength(Band b)
        {
            int n = Route(b);
            float len = 0f;
            for (int i = 1; i < n; i++) len += ScRunners.XZ(pts[i - 1], pts[i]);
            return len;
        }

        /// <summary>
        /// The point s metres along the route (the end when past it), the way they face there, whether that is the end, and the
        /// stretch it is on (0: the first; with the bend still ahead, 1 means they are past it).
        /// </summary>
        private static Vector3 Along(Band b, float s, out Vector3 dir, out bool end, out int seg)
        {
            int n = Route(b);
            dir = Vector3.forward;
            end = false;
            seg = 0;
            for (int i = 1; i < n; i++)
            {
                seg = i - 1;
                Vector3 d = pts[i] - pts[i - 1];
                d.y = 0f;
                float len = d.magnitude;
                if (len > 0.01f) dir = d / len;
                if (s <= len) return Vector3.Lerp(pts[i - 1], pts[i], len > 0.01f ? s / len : 1f);
                s -= len;
            }
            end = true;
            return pts[n - 1];
        }

        /// <summary>Metres walked on the current stretch by now (camp hours halt it).</summary>
        private static float Walked(Band b, ulong now) { return WalkTicks(b.Depart, now) / 1000f * Speed; }

        /// <summary>Where a band with no bodies is now: derived from its stretch, never stored. arrived: they are at the end of it.</summary>
        public static Vector3 VirtualPos(Band b, ulong now, out Vector3 dir, out bool arrived)
        {
            int seg;
            Vector3 p = Along(b, Walked(b, now), out dir, out arrived, out seg);
            return OnHeightmap(p);
        }

        /// <summary>
        /// Their first body comes: the stretch starts again where the virtual walk has them now (the bend dropped once they are
        /// past it), so the walk with bodies carries on from there - and not back toward a bend already behind them.
        /// </summary>
        private static void GoOnline(Band b, ulong now)
        {
            Vector3 dir;
            bool end;
            int seg;
            Vector3 v = Along(b, Walked(b, now), out dir, out end, out seg);
            if (b.HasVia && seg >= 1) b.HasVia = false;
            b.At = B(OnHeightmap(v));
            if (b.Depart < now) b.Depart = now;
        }

        /// <summary>The way they walk from a point: toward the bend while it is ahead, else toward where they head.</summary>
        private static Vector3 TravelDir(Band b, Vector3 from)
        {
            Vector3 d = (b.HasVia ? V(b.Via) : V(b.ToAt)) - from;
            d.y = 0f;
            return d.sqrMagnitude > 0.01f ? d.normalized : Vector3.forward;
        }

        /// <summary>Resting: camp hours, or not yet set out (they stand round where they are, not in file).</summary>
        private static bool Resting(Band b, ulong now) { return !b.Arrived && (IsCamp(now) || now < b.Depart); }

        /// <summary>Where the band is now: the leader's body (any body of theirs) while they are in the world, else the virtual place.</summary>
        public static Vector3 Where(Band b)
        {
            EntityAlive lead = LeadBody(b);
            if (lead != null) return lead.position;
            World w = ScCommunity.World;
            Vector3 dir;
            bool arrived;
            return VirtualPos(b, w != null ? w.worldTime : 0UL, out dir, out arrived);
        }

        /// <summary>The body that leads in the world: the living leader's, else the first living person's with one; null with none out.</summary>
        private static EntityAlive LeadBody(Band b)
        {
            EntityAlive first = null;
            foreach (BandPerson p in ScRegistry.BandPeople)
            {
                if (p.BandId != b.Id || !p.Alive || p.EntityId < 0) continue;
                EntityAlive npc = BodyOf(p);
                if (npc == null || npc.IsDead()) continue;
                if (p.IsLeader) return npc;
                if (first == null) first = npc;
            }
            return first;
        }

        /// <summary>A new stretch from 'from' at 'now' (their bodies went, the console moved them): the bend kept while still ahead.</summary>
        private static void Replan(Band b, Vector3 from, ulong now)
        {
            if (b.HasVia && ScRunners.XZ(from, V(b.Via)) <= ViaReached) b.HasVia = false;
            b.At = B(OnHeightmap(from));
            if (b.Depart < now) b.Depart = now;     // a band yet to set out keeps its hour
        }

        /// <summary>Metres still to walk (the console): from the leader's body while they are in the world, else from the virtual place.</summary>
        private static float ToGo(Band b, ulong now)
        {
            EntityAlive lead = LeadBody(b);
            if (lead == null) return Mathf.Max(0f, RouteLength(b) - Walked(b, now));
            Vector3 p = lead.position;
            return b.HasVia ? ScRunners.XZ(p, V(b.Via)) + ScRunners.XZ(V(b.Via), V(b.ToAt)) : ScRunners.XZ(p, V(b.ToAt));
        }

        /// <summary>The world time they reach the end at this pace (for the console): when the walk clock has covered the route.</summary>
        private static ulong EndTime(Band b, ulong now)
        {
            float need = RouteLength(b) / Speed * 1000f;
            ulong lo = Math.Max(now, b.Depart), hi = b.Depart + MaxWalkDays * 24000UL;
            if (WalkTicks(b.Depart, lo) >= need) return lo;
            if (WalkTicks(b.Depart, hi) < need) return hi;
            for (int i = 0; i < 40 && hi - lo > 10UL; i++)
            {
                ulong mid = lo + (hi - lo) / 2UL;
                if (WalkTicks(b.Depart, mid) >= need) hi = mid; else lo = mid;
            }
            return hi;
        }

        // ------------------------------------------------------------------ geometry ----

        private static float SegDist(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 c = Closest(p, a, b);
            return ScRunners.XZ(p, c);
        }

        private static Vector3 Closest(Vector3 p, Vector3 a, Vector3 b)
        {
            float dx = b.x - a.x, dz = b.z - a.z, l2 = dx * dx + dz * dz;
            if (l2 < 0.0001f) return a;
            float t = Mathf.Clamp01(((p.x - a.x) * dx + (p.z - a.z) * dz) / l2);
            return new Vector3(a.x + dx * t, a.y + (b.y - a.y) * t, a.z + dz * t);
        }

        private static bool HomePoint(out Vector3 home)
        {
            home = Vector3.zero;
            if (!ScZone.HasHome) return false;
            Vector3i h = ScZone.Home;
            home = new Vector3(h.x + 0.5f, h.y, h.z + 0.5f);
            return true;
        }

        /// <summary>The home zone as a circle round the claim: its farthest corner, plus ZoneMargin.</summary>
        private static float ZoneRadius()
        {
            int x0, x1, z0, z1;
            ScZone.Bounds(out x0, out x1, out z0, out z1);
            Vector3i h = ScZone.Home;
            float dx = Math.Max(Math.Abs(x0 - h.x), Math.Abs(x1 + 1 - h.x)), dz = Math.Max(Math.Abs(z0 - h.z), Math.Abs(z1 + 1 - h.z));
            return Mathf.Sqrt(dx * dx + dz * dz) + ZoneMargin;
        }

        /// <summary>
        /// A line from a to b that would cross the home zone bends round its edge: one point (via) on the side the line passes,
        /// pushed out until neither half comes inside the circle. False when the line keeps clear (or there is no home).
        /// </summary>
        private static bool Bend(Vector3 a, Vector3 b, out Vector3 via)
        {
            via = Vector3.zero;
            Vector3 h;
            if (!HomePoint(out h)) return false;
            float r = ZoneRadius();
            if (SegDist(h, a, b) >= r) return false;
            Vector3 n = Closest(h, a, b) - h;
            n.y = 0f;
            if (n.sqrMagnitude < 1f) { Vector3 d = b - a; n = new Vector3(-d.z, 0f, d.x); }
            n.Normalize();
            for (float k = 1.2f; k <= 3.01f; k += 0.3f)
            {
                via = h + n * (r * k);
                if (SegDist(h, a, via) >= r && SegDist(h, via, b) >= r) return true;
            }
            return true;
        }

        /// <summary>Just outside a building's footprint, toward a point: its centre, out past the farthest corner and 3 m more.</summary>
        private static Vector3 Edge(PoiEntry pe, Vector3 toward)
        {
            Vector3 c = new Vector3(pe.X, 0f, pe.Z);
            Vector3 d = toward - c;
            d.y = 0f;
            if (d.sqrMagnitude < 0.01f) d = Vector3.forward;
            float half = Mathf.Sqrt(pe.Size.x * pe.Size.x + pe.Size.z * pe.Size.z) * 0.5f;
            return OnHeightmap(c + d.normalized * (half + 3f));
        }

        // ------------------------------------------------------------------ starting a trip ----

        /// <summary>Why this building can't be one end of a trip of this kind (a short reason), or null. start: where they set out from.</summary>
        private static string Refuse(World w, PoiEntry pe, string kind, bool start, out Enclave enc)
        {
            enc = null;
            if (ScInfestations.IsInfestedPoiKey(pe.Key)) return "infested";
            if (ScOutposts.IsOutpostPoiKey(pe.Key)) return "outpost";
            if (ScSecurity.OverlapsHome(pe.Pi)) return "home";
            enc = ScEnclaves.Holding(pe.Pi);
            if (enc != null)
            {
                // E13a: nobody sets out from an enclave (an errand is E13b's); drifters may head for a friendly one, raiders for a den
                if (start) return "enclave";
                if (kind == Band.Raiders ? !enc.IsHostile : enc.IsHostile) return "enclave";
                return null;
            }
            if (pe.Pi.CheckForAnyPlayerHome(w) != GameUtils.EPlayerHomeType.None) return "claimed";
            if (start && PlayerWithin(w, new Vector3(pe.X, 0f, pe.Z), ScConfig.BandDespawnRange)) return "a player is near";
            return null;
        }

        private static bool PlayerWithin(World w, Vector3 at, float range)
        {
            List<EntityPlayer> list = w != null && w.Players != null ? w.Players.list : null;
            if (list == null) return false;
            for (int i = 0; i < list.Count; i++) if (list[i] != null && ScRunners.XZ(list[i].position, at) <= range) return true;
            return false;
        }

        private sealed class Trip
        {
            public PoiEntry A, B;
            public Enclave ToEnc;
            public Vector3 At, ToAt, Via;
            public bool HasVia;
            public float Pass;
        }

        /// <summary>
        /// A trip of this kind past 'anchor': two buildings BandTripMin..BandTripMax m apart (centre to centre) on a line within
        /// 'pass' m of it, both passing Refuse, from up to RouteTries random pairs of the stream; the ends just outside each, the
        /// bend round the home zone. Null with the reason.
        /// </summary>
        private static Trip PickTrip(World w, string kind, Vector3 anchor, float pass, GameRandom r, out string why)
        {
            why = null;
            var near = new List<PoiEntry>();
            ScPoiIndex.Near(anchor.x, anchor.z, pass + ScConfig.BandTripMax, near);
            var starts = new List<PoiEntry>();
            var ends = new List<PoiEntry>();
            var encs = new Dictionary<PoiEntry, Enclave>();
            foreach (PoiEntry pe in near)
            {
                Enclave e;
                if (Refuse(w, pe, kind, true, out e) == null) starts.Add(pe);
                if (Refuse(w, pe, kind, false, out e) == null) { ends.Add(pe); if (e != null) encs[pe] = e; }
            }
            if (starts.Count == 0 || ends.Count == 0)
            {
                why = "no building to set out from or head for within " + Mathf.RoundToInt(pass + ScConfig.BandTripMax) + " m (" + near.Count + " in reach, " + starts.Count
                      + " to set out from, " + ends.Count + " to head for)";
                return null;
            }
            for (int i = 0; i < RouteTries; i++)
            {
                PoiEntry a = starts[r.RandomRange(0, starts.Count)], b = ends[r.RandomRange(0, ends.Count)];
                if (a == b) continue;
                Vector3 pa = new Vector3(a.X, 0f, a.Z), pb = new Vector3(b.X, 0f, b.Z);
                float d = ScRunners.XZ(pa, pb);
                if (d < ScConfig.BandTripMin || d > ScConfig.BandTripMax) continue;
                float passes = SegDist(anchor, pa, pb);
                if (passes > pass) continue;
                var t = new Trip { A = a, B = b, Pass = passes };
                encs.TryGetValue(b, out t.ToEnc);
                Vector3 via;
                t.HasVia = Bend(pa, pb, out via);
                t.Via = OnHeightmap(via);
                t.At = Edge(a, t.HasVia ? via : pb);
                t.ToAt = Edge(b, t.HasVia ? via : pa);
                return t;
            }
            why = "no pair of buildings " + ScConfig.BandTripMin + "-" + ScConfig.BandTripMax + " m apart passes within " + Mathf.RoundToInt(pass) + " m ("
                  + starts.Count + " to set out from, " + ends.Count + " to head for)";
            return null;
        }

        /// <summary>A new band on that trip: its record (no people yet), the log line. Returns it.</summary>
        private static Band NewBand(Trip t, string kind, int size, bool trouble, ulong depart, int seed, string cause)
        {
            Community c = ScRegistry.Community;
            var b = new Band
            {
                Id = ++c.NextBandId,
                Kind = kind,
                Size = size,
                From = t.A.Key,
                FromName = t.A.Display,
                To = t.B.Key,
                ToName = t.B.Display,
                ToEnclave = t.ToEnc != null ? t.ToEnc.Id : 0,
                ToAt = B(t.ToAt),
                At = B(t.At),
                Depart = depart,
                Via = B(t.Via),
                HasVia = t.HasVia,
                StartDay = ScEncRequests.Now,
                Seed = seed,
                Trouble = trouble && kind == Band.Drifters ? 1 : 0
            };
            ScRegistry.Bands.Add(b);
            World w = ScCommunity.World;
            ScLog.Out("band " + b.Id + " (" + kind + ", " + size + " people" + (b.Trouble == 1 ? ", zombies on them when met" : "") + ") sets out (" + cause + ") from " + b.FromName + " "
                      + b.From + " for " + DestText(b) + " " + b.To + ", " + Mathf.RoundToInt(RouteLength(b)) + " m"
                      + (b.HasVia ? " bending round the home zone at " + b.Via : "") + ", passing " + Mathf.RoundToInt(t.Pass) + " m from " + (ScZone.HasHome ? "home" : "you")
                      + "; on the road from " + Clock(depart) + " at " + Mathf.RoundToInt(Speed) + " m/h" + (w != null ? ", there by " + Clock(EndTime(b, w.worldTime)) : ""));
            ScRegistry.SaveAtomic();
            return b;
        }

        /// <summary>The trip's random stream: the world seed, the salt, the request-clock day and a part ("trip", "console").</summary>
        private static int SeedFor(World w, int day, string part)
        {
            return (w != null ? w.Seed : 0) ^ Salt.GetStableHashCode() ^ (part + ":" + day.ToString(CultureInfo.InvariantCulture)).GetStableHashCode();
        }

        /// <summary>
        /// ScDaily.Run, once per request-clock day (after E11's moves): trips that went on too long end once nobody sees them,
        /// then - Bands on, fewer than BandMax on the road - the day's roll (BandDailyChance) for a new one past home (the player
        /// without one). force: the console's "roll" (the chance and the once-a-day ignored). Returns the console's line.
        /// </summary>
        public static string Daily(int day, EntityPlayerLocal player, bool force = false)
        {
            try
            {
                if (!ScRegistry.Loaded) return "the registry is not loaded";
                World w = ScCommunity.World;
                int now = ScEncRequests.Now;
                if (!force)
                {
                    if (now == passDay) return "";
                    passDay = now;
                }
                foreach (Band b in ScRegistry.Bands.ToArray())
                {
                    if (now - b.StartDay <= MaxTripDays || LeadBody(b) != null) continue;
                    End(b, "on the road since day " + b.StartDay + " - gone from the area");
                }
                if (!Configured) return "bands are off (Bands=false)";
                if (w == null || !ScPoiIndex.EnsureBuilt()) return "the POI index is not ready";
                if (ScRegistry.Bands.Count >= Math.Max(0, ScConfig.BandMax)) return "BandMax " + ScConfig.BandMax + " bands are on the road already";
                GameRandom r = GameRandomManager.Instance.CreateGameRandom(SeedFor(w, now, force ? "console:" + Time.frameCount : "trip"));
                try
                {
                    if (!force && r.RandomFloat >= ScConfig.BandDailyChance) { ScLog.Verbose("bands: no new band today (day " + now + ", " + Pct(ScConfig.BandDailyChance) + " a day)"); return "no new band today"; }
                    Vector3 anchor;
                    if (!HomePoint(out anchor))
                    {
                        if (player == null) return "no home and no player to pass";
                        anchor = player.position;
                    }
                    string kind = r.RandomFloat < ScConfig.BandRaiderShare ? Band.Raiders : Band.Drifters;
                    int size = r.RandomRange(Math.Max(1, ScConfig.BandSizeMin), Math.Max(ScConfig.BandSizeMin, ScConfig.BandSizeMax) + 1);
                    bool trouble = r.RandomFloat < ScConfig.BandTroubleChance;
                    string why;
                    Trip t = PickTrip(w, kind, anchor, ScConfig.BandPassRange, r, out why);
                    if (t == null)
                    {
                        ScLog.Out("bands: a band of " + kind + " would set out today (day " + now + "), but " + why);
                        return "no trip: " + why;
                    }
                    ulong depart = w.worldTime + (ulong)r.RandomRange(0, 4) * 1000UL;      // up to three hours after the rollover
                    Band b = NewBand(t, kind, size, trouble, depart, r.RandomInt, force ? "the console's roll" : "the day's roll, day " + now);
                    return "band " + b.Id + " (" + kind + ", " + size + " people) sets out from " + b.FromName + " for " + DestText(b);
                }
                finally
                {
                    GameRandomManager.Instance.FreeGameRandom(r);
                }
            }
            catch (Exception e)
            {
                ScLog.Once("bands-daily", e);
                return "failed: " + e.Message;
            }
        }

        private static string Pct(float f) { return Mathf.RoundToInt(f * 100f) + "%"; }

        // ------------------------------------------------------------------ the 1 Hz driver ----

        /// <summary>
        /// ScTicker, every second, after ScEnclavePeople's: orphans swept; with Bands off every body goes (the records stay);
        /// else each band's second (TickBand), at most SpawnPerTick new bodies across all of them, the markers.
        /// </summary>
        public static void Tick(World w, EntityPlayerLocal player)
        {
            try
            {
                if (w == null || player == null || !ScRegistry.Loaded) return;
                Hook(w);
                SweepOrphans(w);
                if (!Configured)
                {
                    if (bodies.Count > 0) DespawnAll(w, "Bands is off");
                    if (!markersRestored) { markersRestored = true; ClearMarkers(); }
                    return;
                }
                if (ScRegistry.Bands.Count == 0) return;
                if (!markersRestored)
                {
                    markersRestored = true;
                    ClearMarkers();
                    nextMarkers = 0f;
                }
                float t = Time.unscaledTime;
                ulong now = w.worldTime;
                int budget = SpawnPerTick;
                bool changed = false;
                foreach (Band b in ScRegistry.Bands.ToArray())       // a copy: an end takes one out of the list
                {
                    try
                    {
                        if (TickBand(w, player, b, t, now, ref budget)) changed = true;
                    }
                    catch (Exception ex)
                    {
                        ScLog.Once("bands-tick-" + b.Id, ex);
                    }
                }
                if (t >= nextMarkers)
                {
                    nextMarkers = t + MarkerSeconds;
                    UpdateMarkers(player);
                }
                if (changed) ScRegistry.SaveAtomic();
            }
            catch (Exception e)
            {
                ScLog.Once("bands-tick", e);
            }
        }

        /// <summary>One band's second. True when a record changed and the registry wants saving.</summary>
        private static bool TickBand(World w, EntityPlayerLocal player, Band b, float t, ulong now, ref int budget)
        {
            bool changed = false;
            var live = new List<KeyValuePair<BandPerson, EntityAlive>>();
            int pending = 0, out_ = 0;
            foreach (BandPerson p in PeopleOf(b))
            {
                if (p.EntityId < 0) continue;
                out_++;
                if (ScNpc.IsPending(w, p.EntityId)) { pending++; continue; }      // never forced (ours are built in place anyway)
                EntityAlive npc = ScNpc.As(w.GetEntity(p.EntityId));
                if (npc == null || npc.WorldTimeBorn != p.EntityBorn)
                {
                    // gone without the unload handler or the death hook (another mod removed it, a fall out of the world)
                    ScLog.Warn(p.Name + " of band " + b.Id + ": the body (entity " + p.EntityId + ") is gone - they come back from their record");
                    Unbody(p, npc == null);
                    changed = true;
                    continue;
                }
                if (npc.IsDead()) continue;                 // the SetDead postfix took it
                live.Add(new KeyValuePair<BandPerson, EntityAlive>(p, npc));
            }
            if (live.Count > 0) return TickOnline(w, player, b, live, t, now, ref budget) || changed;
            if (pending > 0) return changed;
            // every body went without the unload capture (the loop above): the walk starts again where the last one led them
            if (out_ > 0 && b.HasLastPos && !b.Arrived) { Replan(b, b.LastPos, now); changed = true; }
            b.HasLastPos = false;
            // nobody of theirs in the world: an arrival ends them; else the virtual walk, and bodies near a player
            Vector3 dir;
            bool arrived;
            Vector3 v = VirtualPos(b, now, out dir, out arrived);
            if (b.Arrived || arrived)
            {
                End(b, "arrived at " + b.ToName);
                return false;
            }
            if (b.Trouble == 2) EndTrouble(b, player, "they went off the map");
            if (!Configured || t < b.NoSpawnUntil || budget <= 0) return changed;
            if (NearestPlayer(w, v) > ScConfig.BandSpawnRange) { b.SettledSince = -1f; return changed; }
            if (FightNear(w, player)) return changed;
            if (!ScRunners.Settled(w, v)) { b.SettledSince = -1f; return changed; }
            if (b.SettledSince < 0f) { b.SettledSince = t; return changed; }       // settled for one full tick first
            if (SpawnNext(w, b, v, dir, Resting(b, now)))
            {
                GoOnline(b, now);       // their first body is out: the stretch starts again there (a failed spawn leaves the walk going)
                budget--;
                changed = true;
            }
            return changed;
        }

        /// <summary>
        /// The second of a band with bodies out: the despawn rule, being met, the rest of their bodies, trouble, the aim of
        /// raiders (and drifters against the player), the walk. A hit turns drifters at once (OnHitBy).
        /// </summary>
        private static bool TickOnline(World w, EntityPlayerLocal player, Band b, List<KeyValuePair<BandPerson, EntityAlive>> live, float t, ulong now, ref int budget)
        {
            bool changed = false;
            // the leader first (the walk's point is theirs, the others fall in behind)
            for (int i = 1; i < live.Count; i++)
            {
                if (!live[i].Key.IsLeader) continue;
                var kv0 = live[i];
                live.RemoveAt(i);
                live.Insert(0, kv0);
                break;
            }
            EntityAlive lead = live[0].Value;
            b.LastPos = lead.position;
            b.HasLastPos = true;
            bool seen = false, near = false;
            foreach (var kv in live)
            {
                Vector3 pos = kv.Value.position;
                if (!seen && ScEnclavePeople.SeenByAny(w, pos)) seen = true;
                if (!near && PlayerWithin(w, pos, ScConfig.BandDespawnRange)) near = true;
            }
            // they go: arrived and out of sight; nobody within the despawn range and out of sight (Bands switched off: the tick's)
            if (!seen && (b.Arrived || !near))
            {
                Despawn(b, lead.position, now, b.Arrived ? "arrived, out of sight" : "no player within " + ScConfig.BandDespawnRange + " m");
                if (b.Arrived) End(b, "arrived at " + b.ToName);
                return true;
            }
            if (!b.Known)
            {
                bool met = seen;
                if (!met) foreach (var kv in live) if (ScRunners.XZ(kv.Value.position, player.position) <= KnowRange) { met = true; break; }
                if (met) { MarkKnown(b, player); changed = true; }
            }
            // the rest of their people come while a player is near (one a second across all bands), in file behind the leader
            int missing = 0;
            foreach (BandPerson p in Living(b)) if (p.EntityId < 0 && p.SpawnFailures < ScEnclavePeople.MaxSpawnFailures) missing++;
            bool whole = b.PeopleMade && missing == 0;
            if (!whole && budget > 0 && near && !FightNear(w, player) && SpawnNext(w, b, lead.position, TravelDir(b, lead.position), Resting(b, now)))
            {
                budget--;
                changed = true;
            }
            if (whole && b.Trouble == 1 && !b.IsRaiders && ScRunners.XZ(lead.position, player.position) <= ScConfig.BandSpawnRange + 30f && !FightNear(w, player))
            {
                StartTrouble(w, player, b, live);
                changed = true;
            }
            if (b.Trouble == 2 && TickTrouble(w, player, b, live, t)) changed = true;
            if (b.Hostile) Engage(b, player, live, t);
            // a talk with one of them holds the whole band where it stands; they walk on as it closes
            if (InTalk(b))
            {
                if (!b.Halted)
                {
                    b.Halted = true;
                    foreach (var kv in live) ScEnclavePeople.GuardAt(kv.Value, kv.Value.position, kv.Value.position + kv.Value.GetLookVector() * 4f);
                }
                b.ProgressSince = b.GoalBestAt = -1f;
                return changed;
            }
            if (b.Halted) { b.Halted = false; b.SteeredAt = -1f; b.Camped = false; }
            if (Steer(w, b, lead, live, t, now)) changed = true;
            return changed;
        }

        /// <summary>The local player's talk is open on one of this band's bodies (the dialog's respondent; cleared as it closes).</summary>
        private static bool InTalk(Band b)
        {
            LocalPlayerUI ui = LocalPlayerUI.GetUIForPrimaryPlayer();
            EntityAlive npc = ui != null && ui.xui != null && ui.xui.Dialog != null ? ui.xui.Dialog.Respondent as EntityAlive : null;
            BandPerson p = PersonOf(npc);
            return p != null && p.BandId == b.Id;
        }

        /// <summary>Horizontal metres from the nearest player to a point.</summary>
        private static float NearestPlayer(World w, Vector3 at)
        {
            float best = float.MaxValue;
            List<EntityPlayer> list = w.Players != null ? w.Players.list : null;
            if (list == null) return best;
            for (int i = 0; i < list.Count; i++) if (list[i] != null) best = Mathf.Min(best, ScRunners.XZ(list[i].position, at));
            return best;
        }

        /// <summary>Vanilla's pop-in rule with BandNoPopRange: within it and inside some player's view cone.</summary>
        private static bool PopsInView(World w, Vector3 at)
        {
            float r = ScConfig.BandNoPopRange;
            List<EntityPlayer> list = w.Players != null ? w.Players.list : null;
            if (r <= 0f || list == null) return false;
            for (int i = 0; i < list.Count; i++)
            {
                EntityPlayer p = list[i];
                if (p != null && !p.IsDead() && ScRunners.XZ(at, p.position) < r && p.IsInViewCone(at)) return true;
            }
            return false;
        }

        /// <summary>
        /// A live fight near the player (the one-live-attack spirit): the home's horde with the player near it, an outpost fight
        /// near them (ScOutpostRisk.AnyLiveNear), an enclave's siege, a blood moon. No band body comes while one is on.
        /// </summary>
        private static bool FightNear(World w, EntityPlayerLocal player)
        {
            if (player == null) return false;
            return (ScBaseAttack.Live && ScBaseAttack.Near(player)) || ScOutpostRisk.AnyLiveNear(player.position) || ScEncSiege.Live || ScSiege.BloodMoonActive(w);
        }

        // ------------------------------------------------------------------ bodies coming ----

        /// <summary>The place of the k-th of n in file behind 'at' (facing dir), or round their camp.</summary>
        private static Vector3 Slot(int k, int n, Vector3 at, Vector3 dir, bool camp)
        {
            if (camp)
            {
                if (n <= 1) return at;
                float a = k * Mathf.PI * 2f / n;
                return at + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * CampRadius;
            }
            if (k == 0) return at;
            Vector3 perp = new Vector3(dir.z, 0f, -dir.x);
            int row = (k + 1) / 2;
            return at - dir * (FileSpacing * row) + perp * (k % 2 == 1 ? SideSpacing : -SideSpacing);
        }

        /// <summary>A standable ground spot at a point, else on rings 2-6 m round it.</summary>
        private static bool GroundNear(World w, Vector3 p, out Vector3 spot)
        {
            spot = p;
            int y;
            int x = Mathf.FloorToInt(p.x), z = Mathf.FloorToInt(p.z);
            if (ScRunners.GroundSpot(w, x, z, out y)) { spot = new Vector3(x + 0.5f, y, z + 0.5f); return true; }
            for (int r = 2; r <= 6; r += 2)
            {
                for (int i = 0; i < 8; i++)
                {
                    float a = i * Mathf.PI / 4f;
                    int sx = Mathf.FloorToInt(p.x + Mathf.Sin(a) * r), sz = Mathf.FloorToInt(p.z + Mathf.Cos(a) * r);
                    if (!ScRunners.GroundSpot(w, sx, sz, out y)) continue;
                    spot = new Vector3(sx + 0.5f, y, sz + 0.5f);
                    return true;
                }
            }
            return false;
        }

        private static float Yaw(Vector3 dir) { return dir.sqrMagnitude < 0.0001f ? 0f : Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg; }

        /// <summary>
        /// The next body of theirs at 'at' (their place; the leader's body once one is out): a person made the first time (a
        /// stranger of SC's wild classes for the gamestage tier, kept as their record), else a living person rebuilt from their
        /// record (ScSpawn.Body) - in file facing dir, or round their camp. Never within BandNoPopRange in a player's view. True
        /// when a body was made.
        /// </summary>
        private static bool SpawnNext(World w, Band b, Vector3 at, Vector3 dir, bool camp)
        {
            List<BandPerson> people = PeopleOf(b);
            int n = Math.Max(b.Size, people.Count);
            if (!b.PeopleMade)
            {
                int k = people.Count;
                if (k >= b.Size) { b.PeopleMade = true; return false; }
                Vector3 spot;
                if (!GroundNear(w, Slot(k, n, at, dir, camp), out spot) || PopsInView(w, spot)) return false;
                return Generate(w, b, k, spot, camp ? Yaw(at - spot) : Yaw(dir));
            }
            for (int k = 0; k < people.Count; k++)
            {
                BandPerson p = people[k];
                if (!p.Alive || p.EntityId >= 0 || p.SpawnFailures >= ScEnclavePeople.MaxSpawnFailures) continue;
                if (p.LastEntityId >= 0 && ScNpc.IsPending(w, p.LastEntityId)) continue;      // a copy of their last body still being built
                Vector3 spot;
                if (!GroundNear(w, Slot(k, n, at, dir, camp), out spot) || PopsInView(w, spot)) continue;
                EntityAlive npc = null;
                try { npc = ScSpawn.Body(w, p.Who, spot + Nudge, new Vector3(0f, camp ? Yaw(at - spot) : Yaw(dir), 0f), false); }
                catch (Exception ex) { ScLog.Once("bands-spawn-" + p.Id, ex); }
                if (npc == null)
                {
                    p.SpawnFailures++;
                    ScLog.Once("bands-nobody-" + p.Id, p.Name + " of band " + b.Id + ": no body for the class " + p.Who.Class + " (gone from the game?) - they stay off the map");
                    continue;
                }
                Dress(b, p, npc, spot);
                ScLog.Verbose("band " + b.Id + ": " + p.Name + " (bp" + p.Id + ") is in the world at " + World.worldToBlockPos(spot) + " (entity " + npc.entityId + ")");
                return true;
            }
            return false;
        }

        /// <summary>
        /// The class of their k-th person: a weighted pick from SC's own wild group for the gamestage tier now (E2's RollClass:
        /// scSurvivorGroupGS01/50/100, scRaiderGroupGS... for raiders), from the band's own stream; SC's own class of the tier
        /// when another mod took the group away.
        /// </summary>
        private static int RollClass(Band b, int k)
        {
            int tier = Mathf.Clamp(ScLoadout.GamestageTier(), 0, 2);
            string group = (b.IsRaiders ? "scRaiderGroupGS" : "scSurvivorGroupGS") + (tier == 0 ? "01" : tier == 1 ? "50" : "100");
            GameRandom r = GameRandomManager.Instance.CreateGameRandom(b.Seed ^ unchecked((k + 1) * 19349663));
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
                string cls = b.IsRaiders ? ScXnpc.RaiderClassFor(male, tier) : ScXnpc.ClassFor(male, tier);
                return string.IsNullOrEmpty(cls) ? 0 : EntityClass.FromString(cls);
            }
            finally
            {
                GameRandomManager.Instance.FreeGameRandom(r);
            }
        }

        private static bool Pickable(SEntityClassAndProb c)
        {
            return c.entityClassId != 0 && c.prob > 0f && ScCommunity.ClassExists(c.entityClassId);
        }

        /// <summary>
        /// Their k-th person, the first time they are met: a stranger spawned at the spot, their record captured from what they
        /// rolled (E2's Capture: class, name, look, weapons, rounds, profile; a raider's stars as their Shooting), then dressed.
        /// The first is the leader, and names the band. True when a body was made.
        /// </summary>
        private static bool Generate(World w, Band b, int k, Vector3 spot, float yaw)
        {
            int cid = RollClass(b, k);
            if (!ScCommunity.ClassExists(cid))
            {
                ScLog.Once("bands-noclass-" + b.Id, "band " + b.Id + ": no " + (b.IsRaiders ? "raider" : "survivor") + " class to make its people from - none made");
                return false;
            }
            EntityAlive npc;
            try
            {
                npc = ScSpawn.Create(cid, spot + Nudge, new Vector3(0f, yaw, 0f), null);
                if (npc == null) { ScLog.Once("bands-create-" + b.Id, "band " + b.Id + ": the factory made no body of class " + cid); return false; }
                w.SpawnEntityInWorld(npc);
            }
            catch (Exception ex)
            {
                ScLog.Once("bands-generate-" + b.Id, ex);
                return false;
            }
            var p = new BandPerson { BandId = b.Id, Role = k == 0 || LeaderOf(b) == null ? BandPerson.Leader : BandPerson.MemberRole };
            Capture(p, npc);
            if (p.IsLeader && b.Name.Length == 0) b.Name = string.Format(Loc(b.IsRaiders ? "scBandNameRaiders" : "scBandName"), p.Name);
            Dress(b, p, npc, spot);
            if (PeopleOf(b).Count >= b.Size) b.PeopleMade = true;
            ScRegistry.SaveAtomic();
            ScLog.Out("band " + b.Id + " (" + b.Kind + ", " + b.Name + "): " + (p.IsLeader ? "the leader " : "") + p.Name + " (" + p.Who.Class + ", bp" + p.Id + ") is one of them, met at "
                      + World.worldToBlockPos(spot) + " - " + PeopleOf(b).Count + " of " + b.Size + (b.PeopleMade ? ", the band is made" : ""));
            return true;
        }

        /// <summary>A new person's record from the stranger's body spawned for it (E2's Capture), with a new id, into the records.</summary>
        private static void Capture(BandPerson p, EntityAlive npc)
        {
            Member m = ScCommunity.MemberFromBody(npc, DefaultMood);
            m.Id = 0;
            m.Born = 0;
            if (m.Look != null) m.Look = m.Look.Clone();
            if (ScXnpc.IsRaider(npc))
            {
                // a raider has no profile: the stars it rolled from its tier at spawn are its Shooting from now on
                string src;
                SkillState sh = m.Skill(Skill.Shooting);
                sh.Level = ScShooting.Stars(npc, out src);
                sh.Xp = ScSkills.NeededFor(sh.Level);
            }
            p.Who = m;
            p.Id = ++ScRegistry.Community.NextBandPersonId;
            ScRegistry.BandPeople.Add(p);
        }

        /// <summary>
        /// Everything a band body carries: never saved (Dynamic, the veto), the band's cvar, the record's person - a raider's
        /// stars, a survivor's skills, traits and profile - the record's health, doors; drifters against the player hold the
        /// grudge (ScTrust's cvars); the Guard order where it stands (the walk moves it). No SC package: 0-XNPCCore's own.
        /// </summary>
        private static void Dress(Band b, BandPerson p, EntityAlive npc, Vector3 spot)
        {
            npc.SetSpawnerSource(EnumSpawnerSource.Dynamic);
            npc.Buffs.SetCustomVar(BandCvar, b.Id);
            if (ScXnpc.IsRaider(npc)) npc.Buffs.SetCustomVar(ScShooting.RaiderStarsVar, p.Who.Stars(Skill.Shooting));
            else
            {
                ScSkills.StampCvars(p.Who, npc);
                ScTraits.ApplyBuffs(p.Who, npc);
                ScProfile.Stamp(p.Who, npc);
            }
            if (!b.IsRaiders && b.Trust < 0) Grudge(npc, ScCommunity.LocalPlayer);
            ScDoors.Enable(npc, true);
            int max = npc.GetMaxHealth();
            if (max > 0) npc.Health = Mathf.Clamp(Mathf.RoundToInt(p.Health * max), 1, max);
            p.EntityId = npc.entityId;
            p.EntityBorn = npc.WorldTimeBorn;
            p.SpawnFailures = 0;
            bodies[npc.entityId] = p;
            AddVeto(npc);
            ScCommunity.ResetStill(p.Who);
            p.Who.AwaySeconds = 0;
            p.Who.LastPos = npc.position;
            ScEnclavePeople.GuardAt(npc, spot, spot + Vector3.forward);
            b.SteeredAt = -1f;          // the walk is given again once they are all out
            b.Camped = false;
            b.ProgressSince = b.GoalBestAt = -1f;
        }

        /// <summary>ScTrust's grudge on a body (both cvars): it attacks that player on sight, and has no talk prompt for them.</summary>
        private static void Grudge(EntityAlive npc, EntityPlayer player)
        {
            if (npc == null || npc.Buffs == null || player == null) return;
            npc.Buffs.SetCustomVar(ScTrust.HostileVar(player.entityId), 1f);
            npc.Buffs.SetCustomVar(ScTrust.AnyHostileCvar, 1f);
        }

        // ------------------------------------------------------------------ bodies going ----

        /// <summary>What a body leaves in the record when it goes: its health and its rounds.</summary>
        private static void Record(BandPerson p, EntityAlive npc)
        {
            int max = npc.GetMaxHealth();
            if (max > 0) p.Health = Mathf.Clamp01((float)npc.Health / max);
            ScAmmo.Record(p.Who, npc);
        }

        /// <summary>Takes a body out of the world for good (ScRuns.RemoveEntity: Persist off, no corpse, no chunk record).</summary>
        private static void Remove(BandPerson p, EntityAlive npc)
        {
            int id = npc.entityId;
            Unbody(p, true);
            capturingId = id;
            try { ScRuns.RemoveEntity(npc); }
            finally { capturingId = -1; }
        }

        /// <summary>The person has no body any more: the lookups forget it (caches: it left the world too), LastEntityId keeps its id.</summary>
        private static void Unbody(BandPerson p, bool caches)
        {
            if (p.EntityId < 0) return;
            Forget(p.EntityId, caches);
            p.LastEntityId = p.EntityId;
            p.EntityId = -1;
        }

        private static void Forget(int id, bool caches)
        {
            bodies.Remove(id);
            if (!caches) return;
            ScRunners.ClearScoreCaches(id);
            ScLooks.Forget(id);
            try { if (HarvestManager.Has(id)) HarvestManager.Remove(id); }
            catch (Exception e) { ScLog.Once("bands-harvest", e); }
        }

        /// <summary>
        /// Every body of the band out of the world (state recorded), the walk re-planned from 'from'; a trouble on ends (its
        /// zombies let go). A body the game is still building (ScNpc.IsPending) keeps its EntityId: TickBand's loop takes it
        /// once it is built and the rule that sent the others runs again - never left untracked, never built twice. The
        /// caller saves.
        /// </summary>
        private static int Despawn(Band b, Vector3 from, ulong now, string why)
        {
            int n = 0;
            World w = ScCommunity.World;
            foreach (BandPerson p in PeopleOf(b))
            {
                if (p.EntityId < 0 || (w != null && ScNpc.IsPending(w, p.EntityId))) continue;
                EntityAlive npc = w != null ? ScNpc.As(ScNpc.Loaded(w, p.EntityId)) : null;
                if (npc != null && npc.WorldTimeBorn == p.EntityBorn && !npc.IsDead()) { Record(p, npc); Remove(p, npc); n++; }
                else Unbody(p, false);
            }
            if (!b.Arrived) Replan(b, from, now);
            if (b.Trouble == 2) EndTrouble(b, ScCommunity.LocalPlayer, "they went off the map");
            b.SettledSince = -1f;
            b.Engaged = false;
            b.Camped = false;
            if (n > 0) ScLog.Out("band " + b.Id + " (" + b.Kind + ", " + DisplayName(b) + "): " + n + " body(ies) went (" + why + ") at " + World.worldToBlockPos(from)
                                 + (b.Arrived ? "" : " - they walk on unseen"));
            return n;
        }

        private static void DespawnAll(World w, string why)
        {
            int n = 0;
            ulong now = w != null ? w.worldTime : 0UL;
            foreach (Band b in ScRegistry.Bands)
            {
                EntityAlive lead = LeadBody(b);
                if (lead != null || HasBodyIds(b)) n += Despawn(b, lead != null ? lead.position : Where(b), now, why);
            }
            // what Despawn left is a body still being built (taken once it is: its band's tick, or with Bands off the next
            // DespawnAll); any other entry is stale
            var stale = new List<int>();
            foreach (KeyValuePair<int, BandPerson> kv in bodies)
                if (kv.Value.EntityId != kv.Key || w == null || !ScNpc.IsPending(w, kv.Key)) stale.Add(kv.Key);
            foreach (int id in stale) bodies.Remove(id);
            if (n > 0)
            {
                ScRegistry.SaveAtomic();
                ScLog.Out("bands: " + n + " body(ies) taken out of the world (" + why + ")");
            }
        }

        private static bool HasBodyIds(Band b)
        {
            foreach (BandPerson p in ScRegistry.BandPeople) if (p.BandId == b.Id && p.EntityId >= 0) return true;
            return false;
        }

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

        /// <summary>A chunk unloads under a body (a teleport, a bed respawn): its state into the record first, then it goes (E2's capture).</summary>
        private static void OnEntityUnloaded(Entity e, EnumRemoveEntityReason reason)
        {
            try
            {
                if (e == null || reason != EnumRemoveEntityReason.Unloaded || e.entityId == capturingId || !ScCommunity.IsHost || !ScRegistry.Loaded) return;
                BandPerson p;
                if (!bodies.TryGetValue(e.entityId, out p)) return;
                EntityAlive npc = e as EntityAlive;
                if (npc == null || npc.IsDead() || npc.WorldTimeBorn != p.EntityBorn) return;
                Record(p, npc);
                Unbody(p, true);
                npc.SetSpawnerSource(EnumSpawnerSource.Delete);
                if (!npc.IsMarkedForUnload()) npc.MarkToUnload();
                Band b = ById(p.BandId);
                if (b != null && !HasBodyIds(b))
                {
                    World w = ScCommunity.World;
                    Replan(b, npc.position, w != null ? w.worldTime : 0UL);
                }
                ScRegistry.SaveAtomic();
                ScLog.Out(p.Name + " of band " + p.BandId + " was captured as their chunk unloaded");
            }
            catch (Exception ex)
            {
                ScLog.Once("bands-unload-capture", ex);
            }
        }

        /// <summary>WorldShuttingDown, BEFORE ScRegistry.FlushAndReset and the world's save: every body recorded and taken out.</summary>
        public static void OnWorldShuttingDown()
        {
            try
            {
                World w = ScCommunity.World;
                if (!ScCommunity.IsHost || !ScRegistry.Loaded || w == null) return;
                DespawnAll(w, "the world is shutting down");
            }
            catch (Exception e)
            {
                ScLog.Once("bands-shutdown", e);
            }
            finally
            {
                ClearMarkers();
                Unhook();
            }
        }

        /// <summary>
        /// A body of ours never comes back from a save (it is never written), but after a crash one can: it goes. So does a
        /// body End, JoinNow or the console's kill let go of while it was still being built (SweepWhenBuilt). An id the game
        /// is still building stays in Orphans for a later tick.
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
                BandPerson p;
                if (bodies.TryGetValue(id, out p) && p.EntityBorn == s.WorldTimeBorn) continue;      // a current body: never ours to sweep
                ScRunners.ClearScoreCaches(id);
                ScRuns.RemoveEntity(s);
                ScLog.Out("removed a stale band body (entity " + id + ", band " + (int)s.Buffs.GetCustomVar(BandCvar) + ") that a chunk or a save brought back");
            }
            Orphans.Clear();
            if (keep != null) foreach (int id in keep) Orphans.Add(id);
        }

        /// <summary>
        /// A body a person is done with for good (End, JoinNow, the console's kill) that the game is still building
        /// (ScNpc.IsPending): the person forgets it and the orphan sweep removes it once it is built. True when it was.
        /// </summary>
        private static bool SweepWhenBuilt(World w, BandPerson p)
        {
            if (w == null || p.EntityId < 0 || !ScNpc.IsPending(w, p.EntityId)) return false;
            Orphans.Add(p.EntityId);
            Unbody(p, false);
            return true;
        }

        // ------------------------------------------------------------------ the walk with bodies ----

        /// <summary>
        /// Their walk, once a second: at night the camp (each at a spot round where the leader stopped); at the destination
        /// (within ArriveReach of it, or as near as the ground lets them: ArriveStalled) they hold; else the leader's next point
        /// (ScRunners.NextWaypoint, else E3's StepToward) when the last is reached or after ResteerSeconds, the others in file
        /// behind it. The stuck watch (no progress in StuckSeconds while walking and not fighting: unseen, they go and walk on
        /// unseen) and the stragglers (put back in file, unseen). True when a record changed.
        /// </summary>
        private static bool Steer(World w, Band b, EntityAlive lead, List<KeyValuePair<BandPerson, EntityAlive>> live, float t, ulong now)
        {
            Vector3 lp = lead.position;
            int n = live.Count;
            if (Resting(b, now))
            {
                // the camp (or the wait to set out): each at a spot round where the leader stopped, given again now and then
                if (b.Camped && t - b.SteeredAt < ResteerSeconds) return false;
                if (!b.Camped)
                {
                    b.CampAt = lp;
                    ScLog.Verbose("band " + b.Id + " " + (IsCamp(now) ? "camps for the night" : "waits to set out") + " at " + World.worldToBlockPos(lp));
                }
                b.Camped = true;
                b.SteeredAt = t;
                b.ProgressSince = b.GoalBestAt = -1f;
                for (int k = 0; k < n; k++)
                {
                    Vector3 spot;
                    if (!GroundNear(w, Slot(k, n, b.CampAt, Vector3.forward, true), out spot)) spot = b.CampAt;
                    ScEnclavePeople.GuardAt(live[k].Value, spot, b.CampAt);
                }
                return false;
            }
            if (b.Camped) { b.Camped = false; b.SteeredAt = -1f; }       // morning: on the road at once
            bool changed = false;
            if (b.HasVia && ScRunners.XZ(lp, V(b.Via)) <= ViaReached) { b.HasVia = false; changed = true; }
            Vector3 goal = b.HasVia ? V(b.Via) : V(b.ToAt);
            bool fighting = Fighting(live);
            float toGoal = ScRunners.XZ(lp, goal);
            if (!b.Arrived && !b.HasVia && (toGoal <= ArriveReach || ArriveStalled(b, toGoal, fighting, t)))
            {
                bool there = toGoal <= ArriveReach;
                if (!there) b.ToAt = B(lp);         // short of an end they can't reach: their end is where the leader stands (they hold there)
                b.Arrived = true;
                b.SteeredAt = -1f;
                RemoveMarker(b);
                ScLog.Out("band " + b.Id + " (" + b.Kind + ", " + DisplayName(b) + ") arrived at " + b.ToName
                          + (there ? "" : " (as near as they can get, " + Mathf.RoundToInt(toGoal) + " m off)") + " - they go once nobody sees them");
                return true;
            }
            if (!b.Arrived && Stuck(w, b, lead, live, fighting, t)) return true;
            if (b.SteeredAt >= 0f && t - b.SteeredAt < ResteerSeconds && (b.Arrived || ScRunners.XZ(lp, b.Target) > StepReached)) return changed;
            if (b.SteeredAt < 0f) foreach (var kv in live) ScCommunity.DropPath(kv.Value);
            Vector3 next;
            if (b.Arrived) next = goal;
            else if (!ScRunners.NextWaypoint(w, lp, 0, goal, out next) && !ScEnclaveDeals.StepToward(w, lp, goal, out next)) return changed;
            b.Target = next;
            b.SteeredAt = t;
            Vector3 dir = next - lp;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) { dir = goal - lp; dir.y = 0f; }
            dir = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.forward;
            for (int k = 0; k < n; k++)
            {
                EntityAlive npc = live[k].Value;
                Vector3 at = Slot(k, n, next, dir, false), spot;
                if (k > 0 && GroundNear(w, at, out spot)) at = spot;
                ScEnclavePeople.GuardAt(npc, at, at + dir * 4f);
                // a straggler far behind, seen by nobody, is put back in file
                if (k > 0 && ScRunners.XZ(npc.position, lp) > StraggleRange && !ScEnclavePeople.SeenByAny(w, npc.position))
                {
                    Vector3 back;
                    if (GroundNear(w, Slot(k, n, lp, dir, false), out back) && !ScEnclavePeople.SeenByAny(w, back))
                    {
                        ScCommunity.DropPath(npc);
                        npc.SetPosition(back + Nudge, true);
                        ScLog.Verbose(live[k].Key.Name + " of band " + b.Id + " fell behind - put back in file");
                    }
                }
            }
            return changed;
        }

        /// <summary>Any of them has a live attack target (a fight holds the stuck and arrival watches).</summary>
        private static bool Fighting(List<KeyValuePair<BandPerson, EntityAlive>> live)
        {
            foreach (var kv in live)
            {
                EntityAlive at = kv.Value.GetAttackTarget();
                if (at != null && !at.IsDead()) return true;
            }
            return false;
        }

        /// <summary>
        /// The arrival watch on the leader near the end, walking and not fighting: within ArriveNear of it and no nearer by
        /// ArriveGainMetres in ArriveStallSeconds - the end is somewhere the ground keeps them from, and they are as near as
        /// they get. Watched or not: the stuck watch waits for nobody looking, and a leader circling the end moves enough to
        /// never count as stuck.
        /// </summary>
        private static bool ArriveStalled(Band b, float toGoal, bool fighting, float t)
        {
            if (toGoal > ArriveNear || fighting) { b.GoalBestAt = -1f; return false; }
            if (b.GoalBestAt < 0f || toGoal < b.GoalBest - ArriveGainMetres)
            {
                b.GoalBest = toGoal;
                b.GoalBestAt = t;
                return false;
            }
            return t - b.GoalBestAt >= ArriveStallSeconds;
        }

        /// <summary>
        /// The stuck watch on the leader while walking (not fighting): less than StuckMetres in StuckSeconds. Unseen, the band goes
        /// and walks on unseen (no bodies for StuckRespawnSeconds); seen, it waits. True when the bodies went.
        /// </summary>
        private static bool Stuck(World w, Band b, EntityAlive lead, List<KeyValuePair<BandPerson, EntityAlive>> live, bool fighting, float t)
        {
            Vector3 lp = lead.position;
            if (b.ProgressSince < 0f || fighting || ScRunners.XZ(lp, b.ProgressPos) >= StuckMetres)
            {
                b.ProgressPos = lp;
                b.ProgressSince = t;
                return false;
            }
            if (t - b.ProgressSince < StuckSeconds) return false;
            foreach (var kv in live) if (ScEnclavePeople.SeenByAny(w, kv.Value.position)) return false;
            World ww = ScCommunity.World;
            Despawn(b, lp, ww != null ? ww.worldTime : 0UL, "stuck for " + Mathf.RoundToInt(t - b.ProgressSince) + " s, out of sight");
            b.NoSpawnUntil = t + StuckRespawnSeconds;
            return true;
        }

        /// <summary>
        /// Raiders (and drifters against the player) attack on sight: once one of them sees the player within AggroRange, the
        /// whole band takes the player as its target, given again every AssertSeconds to any of them with no live target, until
        /// the player is beyond BandDespawnRange of them all.
        /// </summary>
        private static void Engage(Band b, EntityPlayerLocal player, List<KeyValuePair<BandPerson, EntityAlive>> live, float t)
        {
            if (player == null || player.IsDead()) { b.Engaged = false; return; }
            if (!b.Engaged)
            {
                foreach (var kv in live)
                {
                    EntityAlive npc = kv.Value;
                    if (ScRunners.XZ(npc.position, player.position) > AggroRange || !npc.CanSee(player)) continue;
                    b.Engaged = true;
                    b.NextAssert = 0f;
                    ScLog.Out("band " + b.Id + " (" + b.Kind + ", " + DisplayName(b) + "): " + kv.Key.Name + " saw " + player.EntityName + " - they attack");
                    break;
                }
                if (!b.Engaged) return;
            }
            bool inRange = false;
            foreach (var kv in live) if (ScRunners.XZ(kv.Value.position, player.position) <= ScConfig.BandDespawnRange) { inRange = true; break; }
            if (!inRange) { b.Engaged = false; return; }
            if (t < b.NextAssert) return;
            b.NextAssert = t + AssertSeconds;
            foreach (var kv in live)
            {
                EntityAlive cur = kv.Value.GetAttackTarget();
                if (cur != null && !cur.IsDead()) continue;
                kv.Value.SetAttackTarget(player, AttackTargetTicks);
            }
        }

        // ------------------------------------------------------------------ meeting them ----

        /// <summary>Seen for the first time: known, the marker, a tell, a ledger line, the log.</summary>
        private static void MarkKnown(Band b, EntityPlayerLocal player)
        {
            b.Known = true;
            b.KnownDay = ScCommunity.Today;
            AddMarker(b, player);
            ScCommunity.Tell(player, string.Format(Loc(b.IsRaiders ? "scBandSeenRaiders" : "scBandSeenDrifters"), DestText(b)), b.IsRaiders ? "ui_denied" : "");
            ScOutposts.NoteLedger(string.Format(Loc(b.IsRaiders ? "scBandLedgerRaiders" : "scBandLedgerDrifters"), DestText(b)));
            ScLog.Out("band " + b.Id + " (" + b.Kind + ", " + DisplayName(b) + ") met on the road at " + World.worldToBlockPos(Where(b)) + ", heading for " + DestText(b));
        }

        /// <summary>
        /// Patches_Trust's hit hook, for a player's landed hit (host; never the killing blow - the death has its own rule), ahead
        /// of ScTrust's: a band body's hit is the band's (ScTrust.Subject leaves band bodies out, so no wild survivor's grudge,
        /// Persist or Wander order on it, and one tell). A drifter band's first one turns the whole band (TurnOn); raiders, a
        /// band already against the player, a husk walking off to the player's home or a stale copy: nothing. True for any
        /// band body (handled here, whatever the outcome).
        /// </summary>
        public static bool OnHitBy(EntityAlive npc, EntityPlayer player)
        {
            if (!IsBandNpc(npc)) return false;
            BandPerson p = PersonOf(npc);
            Band b = p != null ? ById(p.BandId) : null;
            if (b == null || player == null || b.IsRaiders || b.Trust < 0) return true;
            TurnOn(b, player, "one of them was hit");
            ScRegistry.SaveAtomic();
            return true;
        }

        /// <summary>
        /// Drifters turn on the player (a hit on one of them, a kill): against the player for good, every body of theirs holding
        /// ScTrust's grudge (it attacks on sight; no talk), "Come to our place." never offered. A tell, the log.
        /// </summary>
        private static void TurnOn(Band b, EntityPlayer player, string cause)
        {
            if (b.IsRaiders || b.Trust < 0) return;
            b.Trust = -1;
            int n = 0;
            foreach (BandPerson p in PeopleOf(b))
            {
                EntityAlive npc = BodyOf(p);
                if (npc == null || npc.IsDead()) continue;
                Grudge(npc, player);
                n++;
            }
            if (n > 0 && player != null) ScCommunity.Tell(player, Loc("scBandTurned"), "ui_denied");
            ScLog.Out("band " + b.Id + " (drifters, " + DisplayName(b) + ") turned on " + (player != null ? player.EntityName : "the player") + " (" + cause + ")");
        }

        // ------------------------------------------------------------------ trouble ----

        /// <summary>
        /// Drifters met in trouble: BandTroubleZombies zombies of the gamestage's mix (the base attack's pick) come TroubleMin..Max m
        /// from them, on the side away from the player, each on one of them. Once. They carry the base attack's flags
        /// (SpawnHordeZombie), which every end of the trouble takes back (TickTrouble's dead, ReleaseTrouble's rest).
        /// </summary>
        private static void StartTrouble(World w, EntityPlayerLocal player, Band b, List<KeyValuePair<BandPerson, EntityAlive>> live)
        {
            b.Trouble = 2;
            b.TroubleIds.Clear();
            b.Helped = 0;
            Vector3 c = Vector3.zero;
            foreach (var kv in live) c += kv.Value.position;
            c /= live.Count;
            Vector3 away = c - player.position;
            away.y = 0f;
            float baseAngle = away.sqrMagnitude > 0.01f ? Mathf.Atan2(away.x, away.z) : 0f;
            GameRandom rnd = w.GetGameRandom();
            int gs = 0;
            try { gs = player.gameStage; } catch (Exception) { gs = 1; }
            string group = ScBaseAttack.GroupFor(gs, rnd);
            int want = Math.Max(0, ScConfig.BandTroubleZombies);
            for (int i = 0, tries = 0; b.TroubleIds.Count < want && tries < want * 4; tries++)
            {
                float a = baseAngle + (rnd.RandomFloat - 0.5f) * 140f * Mathf.Deg2Rad;
                float r = TroubleMin + rnd.RandomFloat * (TroubleMax - TroubleMin);
                Vector3 spot;
                if (!GroundNear(w, c + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * r, out spot) || PopsInView(w, spot)) continue;
                EntityAlive z = ScBaseAttack.SpawnHordeZombie(w, ScBaseAttack.PickClass(group, rnd), spot + Nudge);
                if (z == null) continue;
                z.SetAttackTarget(live[i % live.Count].Value, AttackTargetTicks);
                b.TroubleIds.Add(z.entityId);
                i++;
            }
            b.TroubleAssert = Time.unscaledTime + AssertSeconds;
            ScLog.Out("band " + b.Id + " (drifters, " + DisplayName(b) + ") is in trouble: " + b.TroubleIds.Count + " zombie(s) of '" + group + "' on them at " + World.worldToBlockPos(c));
            if (b.TroubleIds.Count == 0) b.Trouble = 3;
        }

        /// <summary>
        /// The trouble, once a second: a zombie of it that is dead counts for the player when a player (or their companion) killed
        /// it; one gone from the world is let go; one with no live target is put on one of them again. When none is left: over.
        /// True when it ended.
        /// </summary>
        private static bool TickTrouble(World w, EntityPlayerLocal player, Band b, List<KeyValuePair<BandPerson, EntityAlive>> live, float t)
        {
            bool assert = t >= b.TroubleAssert;
            if (assert) b.TroubleAssert = t + AssertSeconds;
            for (int i = b.TroubleIds.Count - 1; i >= 0; i--)
            {
                EntityAlive z = w.GetEntity(b.TroubleIds[i]) as EntityAlive;
                if (z == null) { b.TroubleIds.RemoveAt(i); continue; }
                if (z.IsDead())
                {
                    if (KillerPlayer(z.entityThatKilledMe) != null) b.Helped++;
                    ScBaseAttack.Unflag(z);         // the corpse lingers a while: it keeps no chunks loaded (as the base attack's)
                    b.TroubleIds.RemoveAt(i);
                    continue;
                }
                if (!assert) continue;
                EntityAlive cur = z.GetAttackTarget();
                if (cur == null || cur.IsDead()) z.SetAttackTarget(live[i % live.Count].Value, AttackTargetTicks);
            }
            if (b.TroubleIds.Count > 0) return false;
            EndTrouble(b, player, null);
            return true;
        }

        /// <summary>
        /// The trouble is over: with the player's help (a kill), drifters still alive trust the player - their thanks, then the
        /// facts, as a tell when near; a ledger line. Without it, or cut short (why: they went off the map, the band is gone),
        /// only the log; a cut-short fight's zombies still standing are let go first (ReleaseTrouble; despawn: the unseen go).
        /// </summary>
        private static void EndTrouble(Band b, EntityPlayerLocal player, string why, bool despawn = true)
        {
            b.Trouble = 3;
            int gone, left;
            ReleaseTrouble(b, despawn, out gone, out left);
            List<BandPerson> living = Living(b);
            if (why != null || living.Count == 0)
            {
                ScLog.Out("band " + b.Id + " (drifters, " + DisplayName(b) + "): their trouble is over (" + (why ?? "nobody of them is left") + ")"
                          + (gone + left > 0 ? " - its zombies: " + gone + " despawned unseen, " + left + " let go" : ""));
                return;
            }
            if (b.Helped <= 0 || b.Trust < 0)
            {
                ScLog.Out("band " + b.Id + " (drifters, " + DisplayName(b) + "): they fought off the dead" + (b.Trust < 0 ? " (they are against you)" : " without your help"));
                return;
            }
            b.Trust = 1;
            BandPerson lead = LeaderOf(b) ?? living[0];
            if (player != null && ScRunners.XZ(Where(b), player.position) <= ScConfig.BandDespawnRange)
                ScCommunity.Tell(player, string.Format(Loc("scEncCallSays"), lead.Name, Loc("scBandThanksSaid")) + " " + Loc("scBandTrustNow"), "");
            ScOutposts.NoteLedger(string.Format(Loc("scBandLedgerHelped"), DisplayName(b)));
            ScLog.Out("band " + b.Id + " (drifters, " + DisplayName(b) + "): you helped them fight off the dead (" + b.Helped + " killed by you) - they trust you now");
        }

        /// <summary>
        /// The trouble's zombies, let go however the fight ends (SpawnHordeZombie gave them the base attack's flags: never
        /// despawned while a player is about, their chunks held, a scout's A* line): with despawn, one alive that nobody sees
        /// goes (Despawned, as the base attack's abandoned fight); the rest - in view, a corpse, or every one without despawn -
        /// stay as ordinary zombies (ScBaseAttack.Unflag: vanilla's despawn and the chunk take them). gone / left: how many
        /// went, how many alive were let go.
        /// </summary>
        private static void ReleaseTrouble(Band b, bool despawn, out int gone, out int left)
        {
            gone = left = 0;
            World w = ScCommunity.World;
            if (w != null)
            {
                foreach (int id in b.TroubleIds)
                {
                    EntityAlive z = w.GetEntity(id) as EntityAlive;
                    if (z == null) continue;
                    if (despawn && !z.IsDead() && !ScEnclavePeople.SeenByAny(w, z.position))
                    {
                        w.RemoveEntity(id, EnumRemoveEntityReason.Despawned);
                        gone++;
                        continue;
                    }
                    ScBaseAttack.Unflag(z);
                    if (!z.IsDead()) left++;
                }
            }
            b.TroubleIds.Clear();
        }

        /// <summary>The player behind a kill: the player, or the player a companion follows (its Leader / Owner cvar).</summary>
        private static EntityPlayer KillerPlayer(EntityAlive killer)
        {
            if (killer == null) return null;
            EntityPlayer p = killer as EntityPlayer;
            if (p != null) return p;
            int leader = ScCommunity.HiredBy(killer);
            World w = ScCommunity.World;
            return leader > 0 && w != null ? w.GetEntity(leader) as EntityPlayer : null;
        }

        // ------------------------------------------------------------------ deaths, the end ----

        /// <summary>The SetDead postfix (Patches_Npc) for a body carrying %sc_band: the person dies with it.</summary>
        public static void OnBodyDied(EntityAlive npc)
        {
            try
            {
                BandPerson p = PersonOf(npc);
                if (p == null) return;          // an orphan, a husk walking off to your home: nobody's
                Unbody(p, false);               // the corpse is left to vanilla (its loot) and stays out of every save (the veto)
                Band b = ById(p.BandId);
                if (b != null) Died(b, p, npc.entityThatKilledMe, null);
            }
            catch (Exception ex)
            {
                ScLog.Once("bands-died", ex);
            }
        }

        /// <summary>
        /// A person died (a body, or the console: how): marked dead, the next best of the living leads after a leader. With nobody
        /// left of a band whose people were made it is destroyed. A kill by the player turns drifters against them; a drifter's
        /// death near the player is told.
        /// </summary>
        private static void Died(Band b, BandPerson p, EntityAlive killer, string how)
        {
            bool wasLeader = p.IsLeader;
            p.State = BandPerson.StateDead;
            p.DiedDay = ScCommunity.Today;
            p.Health = 0f;
            p.Role = BandPerson.MemberRole;
            EntityPlayer by = KillerPlayer(killer);
            EntityPlayerLocal pl = ScCommunity.LocalPlayer;
            ScLog.Out("band " + b.Id + " (" + b.Kind + ", " + DisplayName(b) + "): " + (wasLeader ? "the leader " : "") + p.Name + " (bp" + p.Id + ") died"
                      + (killer != null ? " - killed by " + ScCommunity.DisplayName(killer) + (by != null && !ReferenceEquals(by, killer) ? " (" + by.EntityName + "'s)" : "")
                         : how != null ? " (" + how + ")" : ""));
            List<BandPerson> living = Living(b);
            if (living.Count == 0 && (b.PeopleMade || PeopleOf(b).Count >= b.Size))
            {
                Destroyed(b, by);
                return;
            }
            if (wasLeader && living.Count > 0)
            {
                BandPerson next = living[0];
                foreach (BandPerson q in living)
                {
                    int tq = ScXnpc.TierOf(q.Who.Class), tn = ScXnpc.TierOf(next.Who.Class);
                    if (tq > tn || (tq == tn && q.Id < next.Id)) next = q;
                }
                next.Role = BandPerson.Leader;
                ScLog.Out("band " + b.Id + ": " + next.Name + " (bp" + next.Id + ") leads them now");
            }
            if (!b.IsRaiders)
            {
                if (pl != null && ScRunners.XZ(Where(b), pl.position) <= ScConfig.BandDespawnRange) ScCommunity.Tell(pl, string.Format(Loc("scBandDied"), p.Name), "");
                if (by != null && b.Trust >= 0) TurnOn(b, by, "a kill");
            }
            ScRegistry.SaveAtomic();
        }

        /// <summary>
        /// Everybody of theirs is dead: a tell when the player is near them, a ledger line once met, the end. Called from a
        /// death (the SetDead postfix), where the zombie that killed the last of them may be mid-attack - removing it would
        /// null its move helper and navigator under it (EntityAlive.OnEntityUnload) - so a trouble on is only let go, not
        /// despawned.
        /// </summary>
        private static void Destroyed(Band b, EntityPlayer by)
        {
            EntityPlayerLocal pl = ScCommunity.LocalPlayer;
            bool near = pl != null && ScRunners.XZ(Where(b), pl.position) <= ScConfig.BandDespawnRange;
            if (near) ScCommunity.Tell(pl, string.Format(Loc("scBandGone"), KindWord(b), DestText(b)), "");
            if (b.Known || near) ScOutposts.NoteLedger(string.Format(Loc("scBandLedgerGone"), KindWord(b), DestText(b)));
            End(b, "destroyed" + (by != null ? " (the last killed by " + by.EntityName + ")" : ""), false);
        }

        /// <summary>
        /// The band ends (arrived, destroyed, joined the player, gone from the area, the console): bodies still out go (not the
        /// corpses; one still being built is swept once it is), the marker, the trouble (its zombies let go: EndTrouble -
        /// despawnTrouble: the unseen go), its records; the log, a save.
        /// </summary>
        private static void End(Band b, string cause, bool despawnTrouble = true)
        {
            World w = ScCommunity.World;
            foreach (BandPerson p in PeopleOf(b))
            {
                if (p.EntityId < 0 || SweepWhenBuilt(w, p)) continue;
                EntityAlive npc = BodyOf(p);
                if (npc != null && !npc.IsDead()) Remove(p, npc);
                else Unbody(p, false);
            }
            RemoveMarker(b);
            if (b.Trouble == 2) EndTrouble(b, ScCommunity.LocalPlayer, "the band is gone", despawnTrouble);
            ScRegistry.BandPeople.RemoveAll(p => p.BandId == b.Id);
            ScRegistry.Bands.Remove(b);
            ScLog.Out("band " + b.Id + " (" + b.Kind + ", " + DisplayName(b) + ", from " + b.FromName + " for " + DestText(b) + ") is gone: " + cause);
            ScRegistry.SaveAtomic();
        }

        // ------------------------------------------------------------------ markers ----

        private static string NavClassFor(Band b) { return b.IsRaiders ? NavRaiders : NavDrifters; }

        /// <summary>A known band's marker where they are (owner-only, map; drifters grey, raiders red); its label says who and where to.</summary>
        private static void AddMarker(Band b, EntityPlayer owner)
        {
            try
            {
                if (!b.Known || b.Arrived || owner == null) return;
                NavObjectManager nav = NavObjectManager.Instance;
                string cls = NavClassFor(b);
                if (nav == null || NavObjectClass.GetNavObjectClass(cls) == null) return;
                RemoveMarker(b);
                Vector3 at = Where(b);
                NavObject obj = nav.RegisterNavObject(cls, at, "", false, -1, owner);
                if (obj != null) obj.name = string.Format(Loc(b.IsRaiders ? "scBandMarkerRaiders" : "scBandMarkerDrifters"), DestText(b));
                b.Marker = obj;
                b.MarkerAt = at;
            }
            catch (Exception ex)
            {
                ScLog.Once("bands-marker", ex);
            }
        }

        private static void RemoveMarker(Band b)
        {
            try
            {
                NavObject obj = b.Marker;
                b.Marker = null;
                if (obj == null) return;
                NavObjectManager nav = NavObjectManager.Instance;
                if (nav != null) nav.UnRegisterNavObject(obj);
            }
            catch (Exception ex)
            {
                ScLog.Once("bands-unmarker", ex);
            }
        }

        /// <summary>Every band marker off (both classes, whatever registered them: a load, Bands switched off, the shutdown).</summary>
        private static void ClearMarkers()
        {
            try
            {
                foreach (Band b in ScRegistry.Bands) b.Marker = null;
                NavObjectManager nav = NavObjectManager.Instance;
                if (nav == null) return;
                nav.UnRegisterNavObjectByClass(NavDrifters);
                nav.UnRegisterNavObjectByClass(NavRaiders);
            }
            catch (Exception ex)
            {
                ScLog.Once("bands-markers-clear", ex);
            }
        }

        /// <summary>Every MarkerSeconds: a known band's marker follows them (registered when missing, moved when they moved MarkerMove m).</summary>
        private static void UpdateMarkers(EntityPlayer owner)
        {
            foreach (Band b in ScRegistry.Bands)
            {
                if (!b.Known || b.Arrived) { if (b.Marker != null) RemoveMarker(b); continue; }
                if (b.Marker == null) { AddMarker(b, owner); continue; }
                Vector3 at = Where(b);
                if (ScRunners.XZ(at, b.MarkerAt) < MarkerMove) continue;
                b.Marker.TrackedPosition = at;
                b.MarkerAt = at;
            }
        }

        // ------------------------------------------------------------------ the dialog: "Come to our place." ----

        /// <summary>The band person behind the NPC being talked to (alive) and their band, or false. Host only.</summary>
        public static bool Talking(EntityPlayer player, EntityNPC talkingTo, out Band b, out BandPerson p)
        {
            b = null;
            p = null;
            if (!ScCommunity.IsHost) return false;
            EntityAlive npc = talkingTo != null ? (EntityAlive)talkingTo : ScFavours.CurrentNpc(player);
            p = PersonOf(npc);
            if (p == null || !p.Alive) return false;
            b = ById(p.BandId);
            return b != null;
        }

        /// <summary>How many would come: the living.</summary>
        public static int Coming(Band b) { return b == null ? 0 : Living(b).Count; }

        /// <summary>
        /// Why "Come to our place." is not offered (console words), or null when it is: bands off, not drifters, against the
        /// player, in the middle of their trouble's fight, nobody alive, no home, no room for all of them under MaxMembers
        /// (members on the way count: ScEncJoin.RoomLeft).
        /// </summary>
        public static string HiddenWhy(Band b, EntityPlayer player)
        {
            if (!Configured) return "bands are off (Bands=false)";
            if (b == null) return "no such band";
            if (b.IsRaiders) return "they are raiders";
            if (b.Trust < 0) return "they are against you";
            if (b.Trouble == 2) return "they are fighting";
            int coming = Coming(b), room = ScEncJoin.RoomLeft();
            if (coming <= 0) return "nobody of theirs is alive";
            if (player != null ? !ScZone.EnsureHome(player) : !ScZone.HasHome) return "you have no home";
            if (coming > room) return "your community has room for " + room + " more, they are " + coming + " (MaxMembers " + ScConfig.MaxMembers + ")";
            return null;
        }

        /// <summary>Their answer to "Come to our place.": yes when they trust the player, else their own roll (BandJoinChance), the same each time.</summary>
        public static bool Accepts(Band b)
        {
            return b != null && (b.Trust > 0 || JoinRoll(b) < ScConfig.BandJoinChance);
        }

        /// <summary>Their own answer's roll (0..1, from their seed: the same every time; kept, the dialog asks it every refresh).</summary>
        private static float JoinRoll(Band b)
        {
            if (b.JoinRoll >= 0f) return b.JoinRoll;
            GameRandom r = GameRandomManager.Instance.CreateGameRandom(b.Seed ^ "join".GetStableHashCode());
            try { b.JoinRoll = r.RandomFloat; }
            finally { GameRandomManager.Instance.FreeGameRandom(r); }
            return b.JoinRoll;
        }

        /// <summary>{bandjoinfacts} in the confirm: "3 of them. They make their own way to your home."</summary>
        public static string JoinFacts(Band b) { return string.Format(Loc("scBandJoinFacts"), Coming(b)); }

        /// <summary>{bandwhere} in "Where are you headed?"'s answer: "They are heading for Miller Farmhouse, 900 m north-east." (from the player).</summary>
        public static string WhereFacts(Band b, EntityPlayer player)
        {
            Vector3 from = player != null ? player.position : Where(b);
            return string.Format(Loc("scBandWhereFacts"), DestText(b), ScEnclaves.Where(from, V(b.ToAt)));
        }

        /// <summary>
        /// The line panel's tokens for a band respondent (ScEncDialog.Fill hands them here): {bandwhere}, {bandjoinfacts}, {name}
        /// (the respondent), {leader}, {count}. A line of anybody else is returned as it is.
        /// </summary>
        public static string Fill(string text, EntityAlive npc, EntityPlayer player)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('{') < 0 || !ScCommunity.IsHost) return text;
            BandPerson p = PersonOf(npc);
            Band b = p != null ? ById(p.BandId) : null;
            if (b == null) return text;
            BandPerson lead = LeaderOf(b);
            string s = text;
            if (s.IndexOf("{bandwhere}", StringComparison.Ordinal) >= 0) s = s.Replace("{bandwhere}", WhereFacts(b, player));
            if (s.IndexOf("{bandjoinfacts}", StringComparison.Ordinal) >= 0) s = s.Replace("{bandjoinfacts}", JoinFacts(b));
            s = s.Replace("{name}", p.Name).Replace("{leader}", lead != null ? lead.Name : "");
            if (s.IndexOf("{count}", StringComparison.Ordinal) >= 0) s = s.Replace("{count}", Coming(b).ToString(CultureInfo.InvariantCulture));
            return s;
        }

        /// <summary>
        /// "Welcome to the community." (DialogActionScBandJoin): one of a drifter band being talked to, every gate again
        /// (HiddenWhy, Accepts), then the join (JoinNow). False with the reason (a tooltip).
        /// </summary>
        public static bool Join(EntityPlayerLocal player, EntityAlive npc, out string reason)
        {
            reason = null;
            BandPerson p = PersonOf(npc);
            Band b = p != null ? ById(p.BandId) : null;
            if (player == null || b == null || npc.IsDead() || !p.Alive)
            {
                reason = Loc("scEncStays");
                return false;
            }
            string hidden = HiddenWhy(b, player);
            if (hidden != null || !Accepts(b))
            {
                ScLog.Out("band " + b.Id + " (" + DisplayName(b) + "): the join was refused - " + (hidden ?? "they said no"));
                reason = Loc("scEncStays");
                return false;
            }
            JoinNow(b, player, "the dialog");
            return true;
        }

        /// <summary>A band person's key while on the way: no entity id, enclave person's key (ScEncJoin.Key) or prospect's ever reaches it.</summary>
        public static int Key(BandPerson p) { return int.MaxValue - 0x1000000 - p.Id; }

        /// <summary>The world ticks they take to walk from 'from' to the player's home at BandMetresPerHour (one game hour at the least).</summary>
        private static ulong ArriveTicks(Vector3 from)
        {
            Vector3 home;
            float metres = HomePoint(out home) ? ScRunners.XZ(from, home) : 0f;
            return (ulong)Mathf.Max(1000f, metres / Speed * 1000f);
        }

        /// <summary>
        /// The join, the gates passed: each living person becomes a member on the way (E12's path, ScEncJoin.OnTheWay: keyed by
        /// Key, Arriving, due when they could have walked to the home, coming in on the side of where they are now) - a body out
        /// gives its pair and rounds first and walks off toward home as a husk; then the band ends. One tell (their word, then the
        /// facts), a ledger line, the log, a save. Returns the console's line.
        /// </summary>
        internal static string JoinNow(Band b, EntityPlayerLocal player, string cause)
        {
            World w = ScCommunity.World;
            BandPerson leader = LeaderOf(b);
            string leaderName = leader != null ? leader.Name : "";
            string name = DisplayName(b);
            Vector3 from = Where(b);
            ulong at = (w != null ? w.worldTime : 0UL) + ArriveTicks(from);
            var names = new List<string>();
            int joined = 0, walking = 0, failed = 0;
            foreach (BandPerson p in Living(b))
            {
                try
                {
                    Member m = p.Who;
                    EntityAlive body = SweepWhenBuilt(w, p) ? null : BodyOf(p);      // one still being built: swept once it is, no husk
                    if (body != null && !body.IsDead())
                    {
                        Loadout lo = ScLoadout.Current(body);
                        if (lo != null) m.Loadout = lo;
                        m.Ammo = ScAmmo.Carried(body, out m.Mags);
                        m.Ranged = ScSkills.HoldsRanged(body);
                        Unbody(p, false);                           // no person's body any more; its cvar and the veto stay
                        ScEncJoin.AddHusk(body, p.Name);
                        walking++;
                    }
                    ScEncJoin.OnTheWay(w, m, Key(p), at, -b.Id, from, name);
                    ScRegistry.BandPeople.Remove(p);
                    names.Add(p.Name + (p.IsLeader ? " (leader)" : ""));
                    joined++;
                }
                catch (Exception ex)
                {
                    failed++;
                    ScLog.Once("bands-join-person-" + p.Id, ex);
                }
            }
            int left = Living(b).Count;
            if (player != null && joined > 0)
                ScCommunity.Tell(player, string.Format(Loc("scEncCallSays"), leaderName.Length > 0 ? leaderName : name, Loc("scBandJoinSaid")) + " "
                                         + string.Format(Loc("scBandJoinTold"), name, joined), "");
            if (joined > 0) ScOutposts.NoteLedger(string.Format(Loc("scEncJoinLedger"), name, joined));
            string line = name + " (band " + b.Id + ") joined your community (" + cause + "): " + joined + " people - " + string.Join(", ", names.ToArray())
                          + "; on their own way, at home from " + Clock(at) + " (" + ScEncCalls.TimeText(at - (w != null ? w.worldTime : 0UL)) + "; " + walking + " walk off now)"
                          + (left > 0 ? "; " + left + " stay (" + failed + " failed)" : "");
            ScLog.Out(line);
            if (left == 0) End(b, "joined you");
            else ScRegistry.SaveAtomic();
            return line;
        }

        // ------------------------------------------------------------------ the clock, the console ----

        /// <summary>
        /// ScEnclaves.OnWorldDayBack: the world's day went back worldBack days (the request clock reqBack): each band's walk keeps
        /// its span (Depart, the world's), its trip its age (StartDay, the request clock's). Returns how many stamps moved.
        /// </summary>
        internal static int ShiftDays(int reqBack, int worldBack)
        {
            int n = 0;
            ulong ticks = (ulong)Math.Max(0, worldBack) * 24000UL;
            foreach (Band b in ScRegistry.Bands)
            {
                if (ticks > 0UL && b.Depart > 0UL) { b.Depart = b.Depart > ticks ? b.Depart - ticks : 0UL; n++; }
                if (reqBack > 0 && b.StartDay > 0) { b.StartDay = Math.Max(0, b.StartDay - reqBack); n++; }
            }
            return n;
        }

        /// <summary>"day 12, 18:00": a world time (E9's scEncCallClock).</summary>
        private static string Clock(ulong t)
        {
            return string.Format(Loc("scEncCallClock"), GameUtils.WorldTimeToDays(t), GameUtils.WorldTimeToHours(t).ToString("00") + ":" + GameUtils.WorldTimeToMinutes(t).ToString("00"));
        }

        /// <summary>What they are doing now, for the console.</summary>
        private static string StateText(Band b, ulong now)
        {
            if (b.Arrived) return "arrived";
            if (now < b.Depart) return "setting out at " + Clock(b.Depart);
            return IsCamp(now) ? "camped" : "walking";
        }

        private static int BodiesOut(Band b)
        {
            int n = 0;
            foreach (BandPerson p in ScRegistry.BandPeople) if (p.BandId == b.Id && p.EntityId >= 0) n++;
            return n;
        }

        /// <summary>One band in a line (sc band list).</summary>
        private static string Line(Band b, EntityPlayer player, ulong now)
        {
            int living = Living(b).Count, all = PeopleOf(b).Count;
            Vector3 at = Where(b);
            return "#" + b.Id + " " + b.Kind + " - " + (b.PeopleMade || all > 0 ? DisplayName(b) + ", " + living + " of " + all + " alive" : b.Size + " people not met yet")
                   + "; " + b.FromName + " -> " + DestText(b) + "; " + (player != null ? ScEnclaves.Where(player.position, at) + " from you" : World.worldToBlockPos(at).ToString())
                   + ", " + StateText(b, now) + "; " + (b.Known ? "met" : "not met") + (BodiesOut(b) > 0 ? ", " + BodiesOut(b) + " in the world" : "");
        }

        /// <summary>
        /// sc band [list | info &lt;id&gt; | spawn [drifters|raiders] [trouble] | here &lt;id&gt; | advance &lt;id&gt; [hours] | kill &lt;id&gt;
        /// [&lt;person id&gt;|all] | clear &lt;id|all&gt; | trust &lt;id&gt; | roll]
        /// </summary>
        public static void Command(List<string> p, List<string> into)
        {
            string sub = p.Count > 1 ? p[1].ToLowerInvariant() : "list";
            World w = ScCommunity.World;
            EntityPlayerLocal pl = ScCommunity.LocalPlayer;
            if (w == null || pl == null) { into.Add("host only, with a player"); return; }
            ulong now = w.worldTime;
            switch (sub)
            {
                case "list":
                {
                    into.Add(ScRegistry.Bands.Count + " band(s) on the road (BandMax " + ScConfig.BandMax + "; " + (Configured ? Pct(ScConfig.BandDailyChance) + " a day, raiders "
                             + Pct(ScConfig.BandRaiderShare) : "Bands=false") + ")");
                    foreach (Band b in ScRegistry.Bands) into.Add("  " + Line(b, pl, now));
                    return;
                }
                case "info":
                {
                    Band b = Arg(p, into);
                    if (b != null) Info(b, pl, now, into);
                    return;
                }
                case "spawn":
                {
                    string kind = Band.Drifters;
                    bool trouble = false;
                    for (int i = 2; i < p.Count; i++)
                    {
                        string a = p[i].ToLowerInvariant();
                        if (a == "raiders" || a == "raider") kind = Band.Raiders;
                        else if (a == "drifters" || a == "drifter") kind = Band.Drifters;
                        else if (a == "trouble") trouble = true;
                    }
                    into.Add(ConsoleSpawn(w, pl, kind, trouble));
                    return;
                }
                case "here":
                {
                    Band b = Arg(p, into);
                    if (b != null) into.Add(Here(w, pl, b, "the console"));
                    return;
                }
                case "advance":
                {
                    Band b = Arg(p, into);
                    if (b == null) return;
                    float hours = 1f, h;
                    if (p.Count > 3 && float.TryParse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture, out h)) hours = h;
                    into.Add(Advance(w, b, Mathf.Clamp(hours, 0f, 240f)));
                    return;
                }
                case "trust":
                {
                    Band b = Arg(p, into);
                    if (b == null) return;
                    if (b.IsRaiders) { into.Add("refused: band " + b.Id + " are raiders"); return; }
                    if (b.Trust < 0) { into.Add("refused: band " + b.Id + " are against you (for good)"); return; }
                    b.Trust = 1;
                    ScRegistry.SaveAtomic();
                    ScLog.Out("band " + b.Id + " (drifters, " + DisplayName(b) + ") trust you now (the console)");
                    into.Add("band " + b.Id + " trust you now - \"Come to our place.\" gets a yes while it is offered");
                    return;
                }
                case "kill":
                {
                    Band b = Arg(p, into);
                    if (b != null) into.Add(Kill(b, p.Count > 3 ? p[3] : null));
                    return;
                }
                case "clear":
                {
                    if (p.Count > 2 && p[2].Equals("all", StringComparison.OrdinalIgnoreCase))
                    {
                        int n = ScRegistry.Bands.Count;
                        foreach (Band b in ScRegistry.Bands.ToArray()) End(b, "cleared (the console)");
                        into.Add(n + " band(s) cleared");
                        return;
                    }
                    Band one = Arg(p, into);
                    if (one == null) return;
                    End(one, "cleared (the console)");
                    into.Add("band " + one.Id + " cleared - gone from the map");
                    return;
                }
                case "roll":
                {
                    string r = Daily(ScCommunity.Today, pl, true);
                    into.Add(r.Length > 0 ? r : "nothing");
                    return;
                }
                default:
                    into.Add("sc band [list | info <id> | spawn [drifters|raiders] [trouble] | here <id> | advance <id> [hours] | kill <id> [<person id>|all] | clear <id|all> | trust <id> | roll]");
                    return;
            }
        }

        private static Band Arg(List<string> p, List<string> into)
        {
            int id;
            if (p.Count < 3 || !int.TryParse(p[2], out id)) { into.Add("give a band id (sc band list)"); return null; }
            Band b = ById(id);
            if (b == null) into.Add("no band " + id);
            return b;
        }

        /// <summary>sc band info &lt;id&gt;: the route, where they are and when they get there, met or not, trust, trouble, the join, the people.</summary>
        private static void Info(Band b, EntityPlayerLocal pl, ulong now, List<string> into)
        {
            Vector3 at = Where(b);
            into.Add("#" + b.Id + " " + b.Kind + " - " + DisplayName(b) + " (on the road since day " + b.StartDay + " on the request clock)");
            into.Add("  route: " + b.FromName + " " + b.From + " -> " + DestText(b) + " " + b.To + (b.HasVia ? " by the bend round your home at " + b.Via : "")
                     + "; now at " + World.worldToBlockPos(at) + " (" + ScEnclaves.Where(pl.position, at) + " from you), " + Mathf.RoundToInt(ToGo(b, now)) + " m to go (the walk counted from "
                     + b.At + "); " + StateText(b, now) + " (camp " + ScConfig.BandCampFrom + "-" + ScConfig.BandCampTo + "h) at " + Mathf.RoundToInt(Speed) + " m/h"
                     + (b.Arrived ? "" : ", there by " + Clock(EndTime(b, now))));
            into.Add("  met: " + (b.Known ? "yes (day " + b.KnownDay + "), marked" : "not yet") + "; " + BodiesOut(b) + " in the world");
            if (!b.IsRaiders)
            {
                string trouble = b.Trouble == 1 ? "owed (zombies on them when you meet)" : b.Trouble == 2 ? "on (" + b.TroubleIds.Count + " zombie(s) left, " + b.Helped + " killed by you)"
                                 : b.Trouble == 3 ? "over" : "none";
                string hidden = HiddenWhy(b, pl);
                into.Add("  trust: " + (b.Trust > 0 ? "they trust you" : b.Trust < 0 ? "against you" : "wary") + "; trouble: " + trouble + "; come to our place: "
                         + (hidden != null ? "not offered (" + hidden + ")" : "offered, they'd say " + (Accepts(b) ? "yes" : "no")) + " (" + (b.Trust > 0 ? "they trust you"
                         : "their roll " + JoinRoll(b).ToString("0.00", CultureInfo.InvariantCulture) + " against " + ScConfig.BandJoinChance.ToString("0.00", CultureInfo.InvariantCulture)) + ")");
            }
            else into.Add("  raiders: they attack on sight" + (b.Engaged ? " (attacking you now)" : ""));
            if (PeopleOf(b).Count == 0) into.Add("  " + b.Size + " people, not met yet (made the first time you come within " + ScConfig.BandSpawnRange + " m)");
            foreach (BandPerson q in PeopleOf(b))
            {
                EntityAlive npc = BodyOf(q);
                into.Add("  bp" + q.Id + " " + q.Role + " " + q.Name + " (" + q.Who.Class + ") - " + q.State + (q.Alive ? "" : " since day " + q.DiedDay) + ", health "
                         + Mathf.RoundToInt(q.Health * 100f) + "%, " + (q.EntityId >= 0 ? "entity " + q.EntityId + (npc != null ? " at " + World.worldToBlockPos(npc.position) + ", "
                         + npc.Health + "/" + npc.GetMaxHealth() + " hp" : "") : "no body"));
            }
        }

        /// <summary>
        /// sc band spawn [drifters|raiders] [trouble]: a new band on a real trip past the player (the route rules, BandPassRange
        /// from the player), set on the road ConsoleLead m from them heading through them (Here). BandMax and the day's roll ignored.
        /// </summary>
        private static string ConsoleSpawn(World w, EntityPlayerLocal pl, string kind, bool trouble)
        {
            if (!Configured) return "refused: bands are off (Bands=false)";
            if (!ScPoiIndex.EnsureBuilt()) return "refused: the POI index is not ready";
            int seed = SeedFor(w, ScEncRequests.Now, "console:" + ScRegistry.Community.NextBandId + ":" + Time.frameCount);
            GameRandom r = GameRandomManager.Instance.CreateGameRandom(seed);
            try
            {
                string why;
                Trip t = PickTrip(w, kind, pl.position, ScConfig.BandPassRange, r, out why);
                if (t == null) return "refused: " + why;
                int size = r.RandomRange(Math.Max(1, ScConfig.BandSizeMin), Math.Max(ScConfig.BandSizeMin, ScConfig.BandSizeMax) + 1);
                Band b = NewBand(t, kind, size, trouble, w.worldTime, r.RandomInt, "the console");
                return "band " + b.Id + " (" + kind + ", " + size + " people" + (b.Trouble == 1 ? ", zombies on them when met" : "") + "), from " + b.FromName + " for " + DestText(b) + ": "
                       + Here(w, pl, b, "the console");
            }
            finally
            {
                GameRandomManager.Instance.FreeGameRandom(r);
            }
        }

        /// <summary>
        /// sc band here &lt;id&gt; (and the console's spawn): bodies out go first; the band is put ConsoleLead m from the player on the
        /// side away from where they head, walking from there straight through the player to their destination (no bend), setting
        /// out now. A test of the walk past you, not the trip rules.
        /// </summary>
        private static string Here(World w, EntityPlayerLocal pl, Band b, string cause)
        {
            ulong now = w.worldTime;
            if (LeadBody(b) != null || HasBodyIds(b)) Despawn(b, Where(b), now, cause);
            b.Arrived = false;
            Vector3 to = V(b.ToAt), me = pl.position;
            Vector3 d = me - to;
            d.y = 0f;
            if (d.sqrMagnitude < 1f) d = Vector3.forward;
            b.At = B(OnHeightmap(me + d.normalized * ConsoleLead));
            b.HasVia = false;
            b.Depart = now;
            b.NoSpawnUntil = 0f;
            b.SettledSince = -1f;
            ScRegistry.SaveAtomic();
            ScLog.Out("band " + b.Id + " (" + b.Kind + ", " + DisplayName(b) + ") put on the road at " + b.At + ", " + Mathf.RoundToInt(ConsoleLead) + " m from " + pl.EntityName + " (" + cause + ")");
            return "on the road at " + b.At + ", " + Mathf.RoundToInt(ConsoleLead) + " m " + DirWord(me, V(b.At)) + " of you, walking through you to " + DestText(b)
                   + (IsCamp(now) ? " - it is camp time (" + ScConfig.BandCampFrom + "-" + ScConfig.BandCampTo + "h): they wait there for the morning" : "");
        }

        private static string DirWord(Vector3 from, Vector3 to)
        {
            string w = ScEnclaves.Where(from, to);       // "140 m north-east"
            int sp = w.IndexOf(' ');
            sp = sp >= 0 ? w.IndexOf(' ', sp + 1) : -1;
            return sp >= 0 ? w.Substring(sp + 1) : w;
        }

        /// <summary>sc band advance &lt;id&gt; [hours]: bodies out go, then the band walks on that many walking hours (camps ignored) - to the end, they arrive.</summary>
        private static string Advance(World w, Band b, float hours)
        {
            ulong now = w.worldTime;
            if (LeadBody(b) != null || HasBodyIds(b)) Despawn(b, Where(b), now, "the console");
            GoOnline(b, now);           // the stretch from where the virtual walk has them now (a bend behind them dropped)
            Vector3 dir;
            bool end;
            int seg;
            Vector3 to = Along(b, hours * Speed, out dir, out end, out seg);
            if (end || b.Arrived)
            {
                End(b, "arrived at " + b.ToName + " (the console moved them on)");
                return "band " + b.Id + " moved on " + hours.ToString("0.#", CultureInfo.InvariantCulture) + " h - they arrived at " + b.ToName + " and are gone";
            }
            if (b.HasVia && seg >= 1) b.HasVia = false;
            Replan(b, OnHeightmap(to), now);
            b.Depart = now;             // on the road from here now, even a band that had not set out yet
            b.NoSpawnUntil = 0f;
            ScRegistry.SaveAtomic();
            return "band " + b.Id + " moved on " + hours.ToString("0.#", CultureInfo.InvariantCulture) + " h (" + Mathf.RoundToInt(hours * Speed) + " m) to " + b.At + ", "
                   + Mathf.RoundToInt(RouteLength(b)) + " m to go";
        }

        /// <summary>sc band kill &lt;id&gt; [&lt;person id&gt;|all] (no argument: the leader): marked dead, the body removed (no corpse); all: destroyed.</summary>
        private static string Kill(Band b, string arg)
        {
            List<BandPerson> living = Living(b);
            if (PeopleOf(b).Count == 0) return "their people are not made yet (come within " + ScConfig.BandSpawnRange + " m of them, or sc band here " + b.Id + ")";
            if (living.Count == 0) return "nobody of theirs is alive";
            var targets = new List<BandPerson>();
            bool all = arg != null && arg.Equals("all", StringComparison.OrdinalIgnoreCase);
            if (all) { targets.AddRange(living); b.PeopleMade = true; }
            else if (string.IsNullOrEmpty(arg)) targets.Add(LeaderOf(b) ?? living[0]);
            else
            {
                int pid;
                if (!int.TryParse(arg.TrimStart('b', 'B', 'p', 'P'), out pid)) return "give a person id (sc band info " + b.Id + ") or all";
                foreach (BandPerson q in living) if (q.Id == pid) targets.Add(q);
                if (targets.Count == 0) return "no living person bp" + pid + " in band " + b.Id;
            }
            int n = 0;
            World w = ScCommunity.World;
            foreach (BandPerson q in targets)
            {
                if (!ScRegistry.Bands.Contains(b)) break;
                EntityAlive npc = SweepWhenBuilt(w, q) ? null : BodyOf(q);
                if (npc != null && !npc.IsDead()) Remove(q, npc);
                else Unbody(q, false);
                Died(b, q, null, "the console");
                n++;
            }
            return n + " marked dead - " + (ScRegistry.Bands.Contains(b) ? Living(b).Count + " of band " + b.Id + " alive" : "band " + b.Id + " is gone");
        }
    }
}
