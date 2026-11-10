using System;
using System.Collections.Generic;

namespace SurvivorCommunity
{
    public enum MemberStatus
    {
        /// <summary>Stationed at their post inside the home zone (Guard order, no leader).</summary>
        Resident,
        /// <summary>Hired to the player through SCore (Leader set, Follow order).</summary>
        Following,
        /// <summary>Following until the player brings them inside the home zone, then stationed.</summary>
        PendingStation,
        /// <summary>On a supply run. RunLeg None: hidden at home, resolved at the day rollover (legacy). Otherwise a
        /// physical run: walking to a POI and back (ScRunners), in the world near the player, a snapshot further out.</summary>
        OnRun,
        /// <summary>Posted to an outpost (ScGarrison): hidden and parked at home like a runner, OutpostId says where.</summary>
        Garrison,
        /// <summary>
        /// E12 (ScEncJoin): one of a whole enclave that joined, making their own way to the base - no body until they turn
        /// up at home (ArriveAt), keyed by ScEncJoin.Key meanwhile. A member all the same (they eat, need a bed, count for
        /// morale and the roster), but no orders, runs, postings or watches, and never missing. Saved as Resident with
        /// arrive= (an older build has a resident with no body: missing, and "Call back" brings them).
        /// </summary>
        Arriving
    }

    /// <summary>Where a physical runner is in the trip (None = the legacy hidden-at-home run).</summary>
    public enum RunLeg
    {
        None = 0,
        Outbound = 1,
        AtSite = 2,
        Inbound = 3,
        Back = 4
    }

    public enum Skill
    {
        Cardio = 0,
        Wits = 1,
        Fighting = 2,
        Shooting = 3
    }

    public enum Specialty
    {
        None = 0,
        Medicine,
        Cooking,
        Mechanics,
        Gardening,
        Chemistry,
        Craftsmanship
    }

    /// <summary>
    /// The stockpile's resources. APPEND ONLY: the registry keeps per-outpost arrays by position (carry, pending, last,
    /// stash), so a value is never renumbered. Water and Luxury came in round wl (2026-10-10); a save from before simply
    /// has none of them.
    /// </summary>
    public enum Resource
    {
        Food = 0,
        Meds = 1,
        Ammo = 2,
        Materials = 3,
        Fuel = 4,
        /// <summary>Drinks that hydrate (not alcohol). Members drink WaterPerMemberDay a day; wells, dew collectors and water outposts make it.</summary>
        Water = 5,
        /// <summary>Alcohol (LuxuryItems). Never used up by the day: spent on the community window's Activities.</summary>
        Luxury = 6
    }

    /// <summary>A bundle item's size: the stockpile units it holds (round wl). Legacy = the old one-size bundle, worth 1.</summary>
    public enum BundleSize
    {
        Legacy = 0,
        Small = 1,
        Medium = 2,
        Large = 3
    }

    public enum FavourKind
    {
        None = 0,
        Poi = 1,
        Supplies = 2
    }

    public sealed class SkillState
    {
        public int Level = 1;
        public int Xp;
    }

    /// <summary>One community member. The registry file is the source of truth; NPC cvars mirror it.</summary>
    public sealed class Member
    {
        public const int SkillCount = 4;
        public const int MaxStars = 7;

        public int Id;
        public string Class = "";
        public string Name = "";
        /// <summary>Entity.WorldTimeBorn - a second identity key, so a reused entity id is never mistaken for the member.</summary>
        public ulong Born;
        public MemberStatus Status = MemberStatus.Resident;
        public Vector3i Post;
        public int JoinDay;
        public readonly SkillState[] Skills = { new SkillState(), new SkillState(), new SkillState(), new SkillState() };
        public Specialty Specialty = Specialty.None;
        public int SpecLevel = 1;
        public int SpecXp;
        public readonly List<string> Traits = new List<string>();
        public int Mood = 50;
        public bool Injured;
        public bool Ranged;
        /// <summary>Resource being fetched while OnRun; meaningless otherwise.</summary>
        public Resource RunKind = Resource.Food;
        public int RunEndDay = -1;
        /// <summary>Days since the entity was last seen by the ticker (0 = seen this session).</summary>
        public int MissingDays;
        /// <summary>Set by the ticker when the entity must be re-hidden or un-hidden the next time it is loaded.</summary>
        public bool PendingUnhide;
        /// <summary>Seconds spent following since the last Cardio XP tick (session only).</summary>
        public int FollowSeconds;
        /// <summary>
        /// Session only: the SCore order a Following / PendingStation member holds, as the ticker last read it. A voice
        /// or order-wheel order (Stay, Guard, Wander, a route, Loot) is a member STATE (ScOrders, decision D6): the
        /// member is still following, holding where they were told. For the window, the console and the log only.
        /// </summary>
        public EntityUtilities.Orders HeldOrder = EntityUtilities.Orders.Follow;
        /// <summary>
        /// Session only: Time.unscaledTime when the community itself last gave this member the Follow order (join, the
        /// Follow verb, a re-link), 0 once the NPC has been seen on Follow. Until then a leftover Guard - the favour wait,
        /// the resident's station - is SC's own and repaired; after it, any held order is the player's (ScTicker).
        /// </summary>
        public float FollowIssued;
        /// <summary>Radio distress on this run: pinned at a POI, rescued by the player or not (see ScRadio).</summary>
        public bool RunDistress;
        public Vector3i RunPoi;
        public string RunPoiName = "";
        public Vector3i RunSpawn;
        public bool RunSafe;
        /// <summary>Session only: the rescue event on the ground.</summary>
        public bool EventSpawned;
        public int StandInId = -1;
        public readonly List<int> EventZombies = new List<int>();
        /// <summary>Stuck watchdog (session only): last position, seconds without progress, seconds far from the post.</summary>
        public UnityEngine.Vector3 LastPos;
        public int StillSeconds;
        public int AwaySeconds;
        /// <summary>
        /// The M11 fix 2's stuck bookkeeping (session only, STUCK_DIAG): the body's ticksExisted at the last sample (a
        /// paused or frozen world does not tick, and is not watched), ticksExisted when the current count began, and per
        /// signal the seconds it held while StillSeconds counted - a path request pending in the path finder, a path, the
        /// move helper aiming more than 1.5 m off, its BlockedTime over 1.5 s, SCore's IsStuck - for the log line.
        /// </summary>
        public int LastTicks, StuckTicks0;
        public int SigPlan, SigPath, SigMover, SigBlocked, SigIsStuck;
        /// <summary>Real times of the recent moves back to the post (three within three minutes get a new post instead).</summary>
        public readonly List<float> UnstickTimes = new List<float>();
        /// <summary>Posts this member was moved off this session: ChoosePost never gives them back. Warned: posts already warned about.</summary>
        public readonly HashSet<Vector3i> BadPosts = new HashSet<Vector3i>();
        public readonly HashSet<Vector3i> WarnedPosts = new HashSet<Vector3i>();
        /// <summary>With no better post to give, the stuck limit is StuckSeconds shifted left by this (doubling, at most 32x); 0 after ten quiet minutes.</summary>
        public int StuckShift;
        /// <summary>Real time of the last stuck verdict (the back-off's ten quiet minutes count from it).</summary>
        public float LastStuckAt;
        /// <summary>The post was checked against GoodPost once its chunk was loaded this session (ScCommunity.CheckPostOnce).</summary>
        public bool PostChecked;
        /// <summary>Face, hair and worn armor when the survivor wears the player's body (null for pack NPCs).</summary>
        public Look Look;
        /// <summary>
        /// The two weapons a 0-XNPCCore survivor carries (ScLoadout: vanilla item names), so that every body the community
        /// re-creates for them (ScSpawn) carries the same pair. Null for a member who joined without one.
        /// </summary>
        public Loadout Loadout;
        /// <summary>
        /// The rounds for their gun they carried when last seen (ScAmmo: ammo item name -> count, the inventory window plus
        /// a loaded magazine), so a body the community re-creates for them (a call-back, a stand-in, a runner's fresh body)
        /// starts with what they had, not with a new survivor's starting stack. Null = never recorded (the body then starts
        /// empty, and a resupply in the base or at an outpost arms it).
        /// </summary>
        public Dictionary<string, int> Ammo;
        /// <summary>
        /// Their Locked &amp; Loaded magazines for the gun when last seen (M11: NpcMagazines.Record's text, the one in the gun
        /// included; ScAmmo), recorded with Ammo and saved in the same ammo= value; a re-created body gets them back as
        /// magazines. Null = none (a gun without magazines, no L&amp;L, or a save from before M11).
        /// </summary>
        public string Mags;
        /// <summary>The outpost a Garrison member holds (-1 otherwise).</summary>
        public int OutpostId = -1;
        /// <summary>Physical supply run (ScRunners). Saved only while OnRun with a leg other than None.</summary>
        public RunLeg RunLeg = RunLeg.None;
        /// <summary>The destination POI (its boundingBoxPosition key) and its name.</summary>
        public Vector3i RunDest;
        public string RunDestName = "";
        /// <summary>Where they reached the site: the virtual position while there, the distress spot.</summary>
        public Vector3i RunSite;
        /// <summary>The current leg: a straight line from/to, timed in world ticks at RunWalkMps.</summary>
        public Vector3i LegFrom, LegTo;
        public ulong LegStart, LegEnd;
        /// <summary>No entity may represent the runner: the snapshot is the runner.</summary>
        public bool Offline;
        /// <summary>"1:&lt;bytes&gt;:&lt;base64url&gt;" - the runner's EntityCreationData.</summary>
        public string Snap;
        /// <summary>What they found at the site, deposited at the gate.</summary>
        public int RunCarry;
        /// <summary>The run's dice are still to be rolled (only ever while offline).</summary>
        public bool RollPending;
        /// <summary>
        /// Base defence D3 (ScWatch): the Watch post block this resident keeps watch on (Vector3i.zero = not on watch).
        /// While on watch, Post is the cell above it; WatchHome is the post to go back to. WatchStart: world time the
        /// shift began (it lasts WatchShiftHours). WatchPrepaid: packing points left of the ammo the shift paid for at its
        /// start (ScAmmo spends them before the stockpile). Saved as watch=, a key older builds ignore.
        /// </summary>
        public Vector3i WatchPost;
        public Vector3i WatchHome;
        public ulong WatchStart;
        public int WatchPrepaid;
        /// <summary>No new shift before this world time: the rest after a full shift (saved as watchrest=).</summary>
        public ulong WatchRestUntil;
        /// <summary>
        /// E12, while Arriving (ScEncJoin): the world time they may turn up at home from, the enclave they came from (its id
        /// and the centre of its building - the side of the base they come in on) and its name. Saved as
        /// arrive=at:enclave:x,y,z:name, a key older builds ignore.
        /// </summary>
        public ulong ArriveAt;
        public int ArriveEnclave;
        public Vector3i ArriveFrom;
        public string ArriveName = "";
#if SC_V34
        /// <summary>
        /// C1 (ScCraft): her 0-XNPCCore crafting roles as a mask (1 cook, 2 blacksmith, 4 chemist, 8 crafter; -1 = never stamped),
        /// the specialty they were set for (a change re-applies its defaults), and the specialty XP her crafts earned that is not a
        /// whole point yet. Saved as craftroles=mask:Specialty and craftxp=, keys older builds ignore.
        /// </summary>
        public int CraftRoles = -1;
        public Specialty CraftRolesFor = Specialty.None;
        public float CraftXpCarry;
        /// <summary>
        /// R2 (ScHandheld, ScRadioTalk), while OnRun: the runner carries a handheld radio - snapshot when the run starts and
        /// whenever their body is seen, so it is known while the body is unloaded - and the moments of this run already said on
        /// it (RadioTalk bits, each said at most once). Saved as runradio=1 and runtalk=N, each written when set (keys older
        /// builds ignore); cleared when the run ends.
        /// </summary>
        public bool RunRadio;
        public int RunTalk;
#endif

        public SkillState Skill(Skill s) { return Skills[(int)s]; }

        public int Stars(Skill s) { return Math.Max(1, Math.Min(MaxStars, Skills[(int)s].Level)); }

        public bool IsHome { get { return Status == MemberStatus.Resident || Status == MemberStatus.OnRun; } }
    }

    /// <summary>A wild NPC the player has talked to about joining; holds the favour they asked for.</summary>
    public sealed class Prospect
    {
        public int Id;
        public string Class = "";
        public string Name = "";
        public FavourKind Favour = FavourKind.None;
        /// <summary>POI favour: footprint min corner (the quest's POIPosition), name and size.</summary>
        public Vector3i PoiPos;
        public string PoiName = "";
        public int PoiWidth;
        public int PoiDepth;
        /// <summary>Supplies favour: the category and count asked for.</summary>
        public Resource ItemCategory = Resource.Food;
        public int ItemCount;
        /// <summary>The player has taken the favour on ("Mark it" / "Sure, I'll see what I can find."); registry started=1.</summary>
        public bool Started;
        public bool Done;
        /// <summary>Where the survivor was last seen (they hold a guard order there); the return marker when unloaded.</summary>
        public Vector3i NpcPos;
        /// <summary>Face, hair and worn armor when the survivor wears the player's body (null for pack NPCs).</summary>
        public Look Look;
        /// <summary>The two weapons they carry (ScLoadout), handed on to the member record when they join.</summary>
        public Loadout Loadout;
        /// <summary>
        /// E3: the enclave person this favour belongs to (0: a wild survivor's). Their body is rebuilt under a new entity id at
        /// each spawn, so the prospect is the person's: Id (its registry key and the favour quest's giver) follows each new
        /// body, and is parked on ScEnclaveDeals.ParkedKey while they have none (ScEnclaveDeals.RekeyFavour). Saved as encp=.
        /// </summary>
        public int EncPersonId;
    }

    public sealed class Community
    {
        public bool HasHome;
        public Vector3i Home;
        public string Owner = "";
        public int Morale = 50;
        public int LastDay = -1;
        public readonly int[] Stock = new int[ScNames.ResourceCount];
        /// <summary>day:name of the last few deaths, newest first.</summary>
        public readonly List<KeyValuePair<int, string>> Deaths = new List<KeyValuePair<int, string>>();
        /// <summary>One summary line per day, newest first, last 7.</summary>
        public readonly List<string> Ledger = new List<string>();
        public int LowMoraleDays;
        /// <summary>Member who was warned they will leave; -1 = nobody.</summary>
        public int ThreatMemberId = -1;
        public bool Hungry;
        /// <summary>The last rollover found too little water (round wl): thirst, the hunger rules over again.</summary>
        public bool Thirsty;
        /// <summary>Morale penalty from recent deaths (DeathPenalty per death, fades DeathDecayPerDay per day).</summary>
        public float DeathPenalty;
        /// <summary>Fractional daily needs carried to the next day (0.5 bundles/member/day etc.).</summary>
        public float FoodCarry;
        public float WaterCarry;
        /// <summary>
        /// Water a day from the wells and dew collectors inside the home zone (ScScan, ScFacilities), and per counted
        /// chunk (only those with any; key WorldChunkCache.MakeChunkKey). Like the beds, a chunk that is not loaded keeps
        /// its last count.
        /// </summary>
        public int HomeWater;
        public readonly Dictionary<long, int> ScanWater = new Dictionary<long, int>();
        /// <summary>Upgraded dew collectors: parent block position -> level 2..5 (a collector not listed is level 1).</summary>
        public readonly Dictionary<Vector3i, int> DewLevels = new Dictionary<Vector3i, int>();
        /// <summary>
        /// Activities (ScActivities): real seconds the round of drinks still lifts morale (counted only while the game
        /// runs unpaused), and the world day it was last poured (once a day).
        /// </summary>
        public float DrinksLeft;
        public int DrinksDay = -1;
        /// <summary>
        /// Ammo (decision D3: no daily upkeep, only resupplies spend it). AmmoOpenPoints: packing points left in the ammo
        /// bundle ScAmmo last opened to hand out rounds, so a bundle is used up round by round instead of being lost to
        /// one resupply. AmmoOpened / AmmoShort: bundles opened, and whether a resupply found the stockpile empty, since
        /// the last day rollover (the daily ledger's "ammo used" and "out of ammo").
        /// </summary>
        public int AmmoOpenPoints;
        public int AmmoOpened;
        public bool AmmoShort;
        /// <summary>Fractional garden harvest carried to the next day.</summary>
        public float HarvestCarry;
        /// <summary>Beds / facilities / crops inside the zone (ScScan: recounted every few seconds from the loaded chunks).</summary>
        public int Beds;
        public int Facilities;
        public int Crops;
        /// <summary>
        /// ScScan's last count per zone chunk, key WorldChunkCache.MakeChunkKey(cx, cz), value {cx, cz, beds,
        /// facilities, crops}: a chunk that is not loaded keeps its last count. Valid for ScanHome only.
        /// </summary>
        public readonly Dictionary<long, int[]> ScanChunks = new Dictionary<long, int[]>();
        public bool HasScanHome;
        public Vector3i ScanHome;
        /// <summary>Radio: world time the next air drop may be requested; day of the next survivor call; contact id counter.</summary>
        public ulong RadioNextDrop;
        public int RadioNextCallDay;
        public int NextContactId;
        /// <summary>Radio blocks inside the home zone, remembered so outposts can check their power from anywhere.</summary>
        public readonly List<Vector3i> Radios = new List<Vector3i>();
        /// <summary>The last day the home radio had power: that day's outpost deliveries report in.</summary>
        public int RadioOnlineDay = -1;
        public int NextOutpostId;
        /// <summary>Outpost economy: materials upkeep and fuel haulage owed (fractions carry), and what went unpaid.</summary>
        public float UpkeepDebt;
        public float HaulDebt;
        public bool OutpostRunDown;
        public bool OutpostOnFoot;
        /// <summary>Morale penalty from lost outposts (OutpostLostPenalty per loss, fades OutpostLostDecay a day).</summary>
        public float OutpostLoss;
        /// <summary>Radio scouting: sites marked by the chatter, and the day the next scout may go out.</summary>
        public readonly List<Scout> Scouts = new List<Scout>();
        public int RadioNextScoutDay;
        /// <summary>Radio blocks OUTSIDE the home zone: a powered one inside an outpost lets it report in on its own.</summary>
        public readonly List<Vector3i> Relays = new List<Vector3i>();
        /// <summary>Radio barter: the day the next trade may go through.</summary>
        public int RadioNextBarterDay;
        /// <summary>Morale from outposts held in a fight (OutpostWinMorale each, fades 1 a day, at most 10).</summary>
        public float OutpostTriumph;
        /// <summary>Base defence (ScNoise): the fading part of the base's noise, and the dead's attention (0-100).</summary>
        public float NoiseEvents;
        public float Attention;
        /// <summary>World time the noise and attention were last advanced to (0 = not yet).</summary>
        public ulong NoiseStamp;
        /// <summary>
        /// Base defence D2 (ScBaseAttack): the open home attack. State 0 none, 1 called (AttackArrive = world time it
        /// arrives, 0 = when the player is next home), 2 live (since AttackStarted), 3 fought without the player (D4). The
        /// side it comes from, how many come,
        /// how many have been spawned and killed (a reload brings back only the ones still standing), and the stockpile's
        /// resupplies during the fight. AttackNotBefore: the next call waits until this world time.
        /// </summary>
        public int AttackState;
        public ulong AttackArrive, AttackStarted, AttackNotBefore;
        public int AttackBearing, AttackSize, AttackSpawned, AttackKilled, AttackAmmoRounds, AttackAmmoPoints;
        /// <summary>Morale from home attacks held (BaseAttackWinMorale each, fades 1 a day, at most 10): the Defence part.</summary>
        public float DefenceTriumph;
        /// <summary>
        /// Base defence D3 (ScWatch): the Watch post blocks in the home zone (at most WatchMaxPosts), saved as watchposts=;
        /// and what a guard on watch has told the player about the open attack: 0 nothing, 1 "gathering" (at the call),
        /// 2 "spotted" (saved as attackwarn=; cleared with the attack).
        /// </summary>
        public readonly List<Vector3i> WatchPosts = new List<Vector3i>();
        public int AttackWarned;
        /// <summary>
        /// Base defence D4 (ScBaseAttack state 3, ScDefence): the open attack fought without the player - since when, when it
        /// is settled, and how many were already killed when it began (a live fight's kills are credited); saved as
        /// attackaway=. AttackHeard: the radio's notices given for it (1 the call, 2 the arrival). AttackAwayUsed: this
        /// absence's attack has come (at most one per absence; cleared once the player is home with none open, awayused=).
        /// </summary>
        public ulong AttackAwaySince, AttackAwayEnd;
        public int AttackAwayBase, AttackHeard;
        public bool AttackAwayUsed;
        /// <summary>Morale lost in fights at home without the player (fades 1 a day): the Defence part is DefenceTriumph - DefenceLoss.</summary>
        public float DefenceLoss;
        /// <summary>Traps and turrets in the home zone (OutpostDefenseBlocks, the home scan) and per counted chunk (only those with any).</summary>
        public int HomeDefences;
        public readonly Dictionary<long, int> ScanDefences = new Dictionary<long, int>();
        /// <summary>The day the player was last seen at home during a blood moon (0: not this one) - no siege at its end.</summary>
        public int BloodMoonHomeDay;
        /// <summary>What happened at home while the player was away with no working radio, told when they are back (ScDefence.News).</summary>
        public readonly List<string> AwayNews = new List<string>();
        /// <summary>
        /// Enclaves (ScEnclaves, E1): the id counter; the seeding stamp enclaveseed=1:day:mode:seed:count (absent = never
        /// seeded in this save: a new save gets its world-start seed, an existing one the late seed; count = the target that
        /// run aimed at, which a raised config tops up); and the day the radio may next reveal one (enclaveradio=).
        /// </summary>
        public int NextEnclaveId;
        public bool EnclaveSeeded;
        public int EnclaveSeedDay;
        public string EnclaveSeedMode = "";
        public int EnclaveSeed;
        public int EnclaveSeedCount;
        public int EnclaveRadioDay;
        /// <summary>Infestations (ScInfestations, I1): the id counter (infestid=).</summary>
        public int NextInfestationId;
        /// <summary>Enclave people (ScEnclavePeople, E2): the person id counter (encpersonid=), one for every enclave.</summary>
        public int NextEncPersonId;
        /// <summary>Bands on the road (ScBands, E13a): the band and band person id counters (bandid=, bandpersonid=).</summary>
        public int NextBandId;
        public int NextBandPersonId;
        /// <summary>
        /// Enclave requests (ScEncRequests, E4): the id counter (encrequestid=), and the request clock - the last day the
        /// request pass ran (encreqday=; ahead of the world's day only after "sc day").
        /// </summary>
        public int NextEncRequestId;
        public int EncRequestDay;
        /// <summary>
        /// P2 (user, 2026-11-09: "Maximum requests for help should be limited to 3 per day"): the world's day the calls for help
        /// were last counted on and how many came that day - offers and siege calls (encoffers=day:n; EnclaveRequestMaxPerDay).
        /// </summary>
        public int EncOfferDay;
        public int EncOffersToday;
        /// <summary>
        /// E5 (ScEncSiege): the world time today's siege call is due (encsiegeat=; 0: none scheduled), set by the day's roll and
        /// fired by the ticker.
        /// </summary>
        public ulong EncSiegeAt;
        /// <summary>
        /// E5 (ScEncSearch): a missing survivor's remains still in the world after their request closed (encremains=), taken
        /// out once their chunk is loaded.
        /// </summary>
        public readonly List<Vector3i> EncRemains = new List<Vector3i>();
        /// <summary>
        /// E6 (ScEncAllies): the Enclave lost morale part - EnclaveLostMorale for each Friendly or Allied group lost, fading a
        /// point a day (encloss=); and the refills owed for lost groups, each due on a request-clock day (encrefill=).
        /// </summary>
        public float EnclaveLoss;
        public readonly List<EncRefill> EncRefills = new List<EncRefill>();

        public int Get(Resource r) { return Stock[(int)r]; }
        public void Set(Resource r, int v) { Stock[(int)r] = Math.Max(0, v); }
        public void Add(Resource r, int delta) { Set(r, Get(r) + delta); }
    }

    public static class ScNames
    {
        /// <summary>Every resource (the stockpile, the registry's arrays). Loops over resources run to this, never to a literal.</summary>
        public const int ResourceCount = 7;
        /// <summary>The five resources the game began with: the radio's barter and scouting and the supply runs stay on these.</summary>
        public const int ClassicCount = 5;
        public static readonly string[] ResourceKeys = { "food", "meds", "ammo", "mats", "fuel", "water", "luxury" };
        public static readonly string[] SkillKeys = { "cardio", "wits", "fighting", "shooting" };

        public static string Key(Resource r) { return ResourceKeys[(int)r]; }
        public static string Key(Skill s) { return SkillKeys[(int)s]; }

        public static bool TryResource(string s, out Resource r)
        {
            r = Resource.Food;
            if (string.IsNullOrEmpty(s)) return false;
            s = s.Trim().ToLowerInvariant();
            for (int i = 0; i < ResourceKeys.Length; i++)
            {
                if (ResourceKeys[i] == s) { r = (Resource)i; return true; }
            }
            // friendly aliases used by the config / console
            switch (s)
            {
                case "medical": case "medicine": r = Resource.Meds; return true;
                case "materials": case "material": r = Resource.Materials; return true;
                case "luxuries": case "lux": case "alcohol": r = Resource.Luxury; return true;
                default: return false;
            }
        }

        public static bool TrySkill(string s, out Skill k)
        {
            k = Skill.Cardio;
            if (string.IsNullOrEmpty(s)) return false;
            s = s.Trim().ToLowerInvariant();
            for (int i = 0; i < SkillKeys.Length; i++)
            {
                if (SkillKeys[i] == s) { k = (Skill)i; return true; }
            }
            return false;
        }

        public static bool TrySpecialty(string s, out Specialty sp)
        {
            sp = Specialty.None;
            if (string.IsNullOrEmpty(s)) return false;
            try
            {
                sp = (Specialty)Enum.Parse(typeof(Specialty), s.Trim(), true);
                return true;
            }
            catch (Exception) { return false; }
        }

        /// <summary>Localization key for a resource ("scResFood"), skill ("scSkillCardio") or specialty ("scSpecMedicine").</summary>
        public static string LocKey(Resource r) { return "scRes" + r; }
        public static string LocKey(Skill s) { return "scSkill" + s; }
        public static string LocKey(Specialty s) { return "scSpec" + s; }

        /// <summary>The old one-size bundle item for a resource (round wl keeps it working, worth 1; nothing makes it any more).</summary>
        public static string BundleItem(Resource r) { return "scBundle" + r; }

        /// <summary>A sized bundle: "scBundleFoodSmall", "scBundleWaterLarge" ...</summary>
        public static string BundleItem(Resource r, BundleSize size)
        {
            return size == BundleSize.Legacy ? BundleItem(r) : "scBundle" + r + size;
        }

        private static Dictionary<string, KeyValuePair<Resource, int>> bundleNames;

        /// <summary>A bundle item's resource and the stockpile units it holds (Small 1, Medium 2, Large 3, the old bundle 1).</summary>
        public static bool TryBundle(string itemName, out Resource r, out int units)
        {
            r = Resource.Food;
            units = 0;
            if (string.IsNullOrEmpty(itemName)) return false;
            if (bundleNames == null)
            {
                var map = new Dictionary<string, KeyValuePair<Resource, int>>(StringComparer.Ordinal);
                for (int i = 0; i < ResourceCount; i++)
                {
                    var res = (Resource)i;
                    map[BundleItem(res)] = new KeyValuePair<Resource, int>(res, 1);
                    for (int s = 1; s <= 3; s++) map[BundleItem(res, (BundleSize)s)] = new KeyValuePair<Resource, int>(res, s);
                }
                bundleNames = map;
            }
            KeyValuePair<Resource, int> hit;
            if (!bundleNames.TryGetValue(itemName, out hit)) return false;
            r = hit.Key;
            units = hit.Value;
            return true;
        }
    }
}
