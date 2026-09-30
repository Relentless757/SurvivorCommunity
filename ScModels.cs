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
        Garrison
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

    public enum Resource
    {
        Food = 0,
        Meds = 1,
        Ammo = 2,
        Materials = 3,
        Fuel = 4
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
        /// <summary>Face, hair and worn armor when the survivor wears the player's body (null for pack NPCs).</summary>
        public Look Look;
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
        public bool Done;
        /// <summary>Where the survivor was last seen (they hold a guard order there); the return marker when unloaded.</summary>
        public Vector3i NpcPos;
        /// <summary>Face, hair and worn armor when the survivor wears the player's body (null for pack NPCs).</summary>
        public Look Look;
    }

    public sealed class Community
    {
        public bool HasHome;
        public Vector3i Home;
        public string Owner = "";
        public int Morale = 50;
        public int LastDay = -1;
        public readonly int[] Stock = new int[5];
        /// <summary>day:name of the last few deaths, newest first.</summary>
        public readonly List<KeyValuePair<int, string>> Deaths = new List<KeyValuePair<int, string>>();
        /// <summary>One summary line per day, newest first, last 7.</summary>
        public readonly List<string> Ledger = new List<string>();
        public int LowMoraleDays;
        /// <summary>Member who was warned they will leave; -1 = nobody.</summary>
        public int ThreatMemberId = -1;
        public bool Hungry;
        /// <summary>Morale penalty from recent deaths (DeathPenalty per death, fades DeathDecayPerDay per day).</summary>
        public float DeathPenalty;
        /// <summary>Fractional daily needs carried to the next day (0.5 bundles/member/day etc.).</summary>
        public float FoodCarry;
        public float AmmoCarry;
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

        public int Get(Resource r) { return Stock[(int)r]; }
        public void Set(Resource r, int v) { Stock[(int)r] = Math.Max(0, v); }
        public void Add(Resource r, int delta) { Set(r, Get(r) + delta); }
    }

    public static class ScNames
    {
        public static readonly string[] ResourceKeys = { "food", "meds", "ammo", "mats", "fuel" };
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

        /// <summary>The bundle item for a resource.</summary>
        public static string BundleItem(Resource r) { return "scBundle" + r; }
    }
}
