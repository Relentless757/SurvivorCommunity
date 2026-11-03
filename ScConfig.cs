using System;
using System.Collections.Generic;
using System.Globalization;

namespace SurvivorCommunity
{
    /// <summary>
    /// One rolled-at-recruit trait, parsed from the config block's Traits class:
    ///   &lt;property name="EatsLikeABird" value="weight=3,food=0.75,morale=0,mood=0,skill=Cardio:1,spec=Cooking,timid=0"/&gt;
    /// Every key is optional. food = food-use multiplier, morale = community morale offset, mood = own mood
    /// offset, skill = a starting skill bonus (Skill:+stars), spec = preferred specialty, timid = flees.
    /// </summary>
    public sealed class TraitDef
    {
        public string Name = "";
        public int Weight = 1;
        public float FoodMult = 1f;
        public int MoraleOffset;
        public int MoodOffset;
        public bool Timid;
        public bool HasSkill;
        public Skill SkillBonus;
        public int SkillBonusStars;
        public Specialty PreferredSpecialty = Specialty.None;
        /// <summary>XP multiplier for all skills (Quick Study).</summary>
        public float XpMult = 1f;
        /// <summary>Supply-run yield multiplier.</summary>
        public float RunYieldMult = 1f;
    }

    /// <summary>
    /// One vanilla player weapon the community's 0-XNPCCore survivors may carry, parsed from the config block's
    /// XnpcWeapons class (written by Tools/gen_arsenal.py from its WEAPONS table):
    ///   &lt;property name="gunBowT0PrimitiveBow" value="bow,0,PrimitiveBow"/&gt;
    /// family melee / bow / firearm, tier 0..2 (the same tiers as the raiders' weapons), and the generator's weapon
    /// token as a short name the console accepts. An optional "bands=a/b/c" field (with IZY Classic installed) weights it
    /// per band instead: a at an NPC's effective tier 0, b at 1, c at 2 (0 = never there); without it a weapon weighs 1 at
    /// its tier and above. An optional "group=T2" makes the bands the whole group's: the weapons of one family and group
    /// split them, so a tier with many guns is no likelier than one with few. Other fields past the alias are ignored.
    /// </summary>
    public sealed class XnpcWeaponDef
    {
        public string Item = "";
        public string Family = "";
        public int Tier;
        public string Alias = "";
        /// <summary>Weight at band 0, 1, 2; null = 1 from Tier up.</summary>
        public float[] Bands;
        /// <summary>The group whose weapons (of this family) share Bands; "" = Bands are this weapon's own.</summary>
        public string Pool = "";
    }

    /// <summary>
    /// One whole mod outfit survivors and raiders may wear (the config block's Outfits class, LittleRedSonja's packs):
    ///   &lt;property name="TWD_Rick" value="head=armorUnisexRickTWDHelmet,chest=...,hands=...,feet=...,sex=any,
    ///             side=survivor,tier=1,weight=1,mix=TWD"/&gt;
    /// sex any / male / female (a mesh built for one sex only), side survivor / raider / both, tier 0..2 (from which
    /// effective band it turns up), weight among the outfits that fit, and mix: pieces swap only with another outfit of
    /// the same group (empty = never). Only outfits whose body armor item exists are used, so a missing pack is no error.
    /// </summary>
    public sealed class OutfitDef
    {
        public string Id = "";
        /// <summary>Item names by slot: Head, Chest, Hands, Feet ("" = none).</summary>
        public readonly string[] Items = { "", "", "", "" };
        /// <summary>0 any, 1 male only, 2 female only.</summary>
        public int Sex;
        public bool Survivors = true, Raiders = true;
        public int Tier;
        public float Weight = 1f;
        public string Mix = "";
    }

    /// <summary>
    /// All tunables, read from the block "ConfigSurvivorCommunity" in Config/blocks.xml once per world.
    /// Defaults are valid before the first parse so nothing depends on ordering.
    /// </summary>
    public static class ScConfig
    {
        public const string BlockName = "ConfigSurvivorCommunity";

        public static int MaxMembers = 12;
        public static int MaxFollowers = 2;
        /// <summary>Food bundles per member per day (0.5 = one bundle feeds two survivors for a day).</summary>
        public static float FoodPerMemberDay = 0.5f;
        /// <summary>
        /// No longer charged (decision D3: there is no daily ammo upkeep; the stockpile's ammo is spent only when a survivor
        /// is resupplied, ScAmmo). Kept as the planning estimate of ammo bundles a ranged resident gets through in a day,
        /// which is all ScOutposts.Shortest ("the resource that runs out first") still reads it for.
        /// </summary>
        public static float AmmoPerRangedDay = 0.25f;
        /// <summary>Points (EconomicValue x count) that make one bundle unit, per resource (a Medium bundle is two units, a Large three).</summary>
        public static readonly int[] PointsPerBundle = { 400, 300, 2000, 1000, 600, 200, 300 };
        /// <summary>Water bundles per member per day (round wl: 1 = a Level 1 well, 2 a day, keeps two survivors).</summary>
        public static float WaterPerMemberDay = 1f;
        /// <summary>
        /// Alcohol: these items are Luxuries, never Food or Water. Vanilla has no alcohol tag, so it is a name list: the
        /// drinks that give a drunk buff (beer and Grandpa's three).
        /// </summary>
        public static readonly List<string> LuxuryItems = new List<string>();
        // ---- facilities (ScFacilities, round wl): hold E on one inside the home zone to upgrade it with materials ----
        /// <summary>The well blocks in level order (V3-OakravenWaterFillers' five models): the block IS the level.</summary>
        public static readonly List<string> WellBlocks = new List<string>();
        /// <summary>Dew collector blocks: one block for every level, the level is kept in the registry.</summary>
        public static readonly List<string> DewCollectorBlocks = new List<string>();
        public static int WellWaterPerLevel = 2;
        public static int DewWaterPerLevel = 1;
        /// <summary>Stockpile materials for levels 2, 3, 4, 5 (the length sets the top level, 1 + its count).</summary>
        public static int[] FacilityUpgradeCost = { 4, 8, 12, 16 };
        /// <summary>Crafting a well places Level 1 only: the well kit's model picker is cut to the first model at world start.</summary>
        public static bool WellsPlaceLevelOne = true;
        // ---- activities (ScActivities): the community window's Activities tab ----
        public static int DrinksLuxury = 4;
        public static int DrinksFood = 1;
        public static int DrinksMorale = 15;
        /// <summary>Real minutes the round of drinks lifts morale (only while the game runs unpaused).</summary>
        public static float DrinksMinutes = 15f;
        /// <summary>Per-item point overrides: item name -> points each.</summary>
        public static readonly Dictionary<string, int> ValueOverrides = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Bed name PREFIXES, on top of every sleeping-bag class block when BedsBySleepingBag is on.</summary>
        public static readonly List<string> BedBlocks = new List<string>();
        /// <summary>Name prefixes that are never beds (a bare bunk frame, a rolled-up mattress).</summary>
        public static readonly List<string> BedBlocksExclude = new List<string>();
        /// <summary>Count every BlockSleepingBag (bedrolls, king beds, bunk beds, modded beds of that class) as a bed.</summary>
        public static bool BedsBySleepingBag = true;
        /// <summary>Items that count as fuel (plus anything tagged gasoline). Wood/coal/cloth burn but are materials.</summary>
        public static readonly List<string> FuelItems = new List<string>();
        public static readonly List<string> FacilityBlocks = new List<string>();
        public static int PostRadius = 8;
        // ---- residents about the base (round hz) ----
        /// <summary>
        /// By day residents walk anywhere in the home zone and stand ResidentRoamPauseMin..Max real seconds at each stop
        /// (UAI.UAITaskWanderHomeSC); inside the zone, or within ResidentRoamZoneMargin metres of it, the 25 m leash and
        /// the watchdog's far-from-the-post rule do not count. False: the old roam within 12 m of the post, 45 s apart.
        /// </summary>
        public static bool ResidentRoamZone = true;
        public static float ResidentRoamPauseMin = 10f;
        public static float ResidentRoamPauseMax = 20f;
        public static float ResidentRoamZoneMargin = 8f;
        // ---- the home zone kept loaded (round hz, ScHomeHold) ----
        public static bool HomeKeepLoaded = true;
        /// <summary>Held while any player is within this many metres of the zone (let go 50 m further out).</summary>
        public static float HomeKeepLoadedRange = 250f;
        public static int LowMoraleDays = 2;
        public static int MissingDaysToPurge = 30;
        public static float RunRiskBase = 0.15f;
        public static int RunYieldBase = 2;
        /// <summary>Supplies favour choices "food:15|meds:5|ammo:60" - one is rolled per prospect.</summary>
        public static readonly List<KeyValuePair<Resource, int>> SuppliesFavours = new List<KeyValuePair<Resource, int>>();
        public static int PoiFavourRange = 300;
        // ---- skill effects (fraction per star above the first) and specialty yields ----
        public static float MeleePerStar = 0.10f;
        public static float RangedPerStar = 0.10f;
        public static float SpeedPerStar = 0.04f;
        public static float HealthPerStar = 0.08f;
        /// <summary>Cooking: food need multiplier is 1 - CookFoodPerStar x best cook's stars (floor 0.5).</summary>
        public static float CookFoodPerStar = 0.08f;
        /// <summary>Gardening: food bundles per day = crops x CropFoodPerStar x gardener stars.</summary>
        public static float CropFoodPerStar = 0.01f;
        /// <summary>Medicine: injuries healed per medical bundle = 1 + floor(medic stars x MedicPerStar).</summary>
        public static float MedicPerStar = 0.5f;
        /// <summary>Craftsmanship: packing threshold multiplier 1 - PackBonusPerStar x craftsman stars (floor 0.6).</summary>
        public static float PackBonusPerStar = 0.04f;
        /// <summary>0 = count the whole zone; above 0 = only within this many blocks of the land claim.</summary>
        public static int ScanRadius = 0;
        public static int FollowXpSeconds = 60;
        public static int DailyWitsXp = 3;
        /// <summary>
        /// A new survivor's lowest stars in every core skill by class tier (T1, T2, T3; round t3, the user's ask: T2 at least
        /// 2, T3 at least 3). Rolled when the profile is (ScSkills.RollStart); 0-XNPCCore's other classes and pack NPCs count as T1.
        /// </summary>
        public static readonly int[] SkillFloorByTier = { 1, 2, 3 };
        /// <summary>The odds a new survivor has one standout core skill of 5+ stars (5 60%, 6 30%, 7 10%), by class tier.</summary>
        public static readonly float[] StandoutSkillChance = { 0.04f, 0.06f, 0.08f };
        /// <summary>The most stars an ordinary start gives a skill (floor, +1 and traits): above it only a standout.</summary>
        public static int StartSkillCap = 4;
        /// <summary>Seconds a stationed resident may make no progress before being moved back to the post.</summary>
        public static int StuckSeconds = 12;
        /// <summary>
        /// Defend the base (M11 fix, the user's decision): a resident goes after any awake enemy inside the home zone within
        /// this many metres of its post, seen or not (SCResident's ScDefendBase, UAIConsiderationScIntruder), fights it and
        /// walks back. 0 = off: residents only fight what they see, as before. At most 23, kept inside the resident leash
        /// (ScReturn, 25 m from the post in Config/utilityai.xml) so the defence and the leash never take turns at the edge.
        /// </summary>
        public static int DefendBaseRadius = 22;
        // ---- morale ----
        public static float DeathPenalty = 15f;
        public static float DeathDecayPerDay = 3f;
        public static int LowMoraleThreshold = 25;
        public static int HighMoraleThreshold = 75;
        // ---- physical supply runs (ScRunners) ----
        /// <summary>Runners walk to a real POI and back; off (or on a dedicated server) runs are the hidden-at-home kind.</summary>
        public static bool PhysicalRuns = true;
        /// <summary>Members may open the community owner's own LOCKED doors (and shut them behind them). Other locks stay shut.</summary>
        public static bool MembersOpenOwnerLockedDoors = true;
        /// <summary>Members open drawbridges and hatches as well (and shut them behind them).</summary>
        public static bool MembersOpenHatches = true;
        public static float RunWalkMps = 4f;       // the runner's RUNNING pace on the Outbound/Inbound legs (name kept for config compatibility)
        /// <summary>
        /// A 0-XNPCCore survivor's ground pace in the world, in literal m/s (migration M4.1): running the Outbound/Inbound
        /// legs, and walking the site and the last stroll home. Its own passives (Cardio, hungry, injured) apply on top.
        /// XNPC runs only above its own threshold, so a run is never slower than about 4.5 and a walk never faster than
        /// about 2.1 (ScXnpc.PathSpeed). SCore's legacy class keeps its own speeds.
        /// </summary>
        public static float RunnerRunMps = 4.5f;
        public static float RunnerWalkMps = 1.6f;
        public static int RunMinDist = 150;
        public static int RunMaxDist = 700;
        public static float RunSiteHours = 6f;
        public static float RunDistressHours = 12f;
        public static int RunDeadlineGraceDays = 2;
        public static float RunWaypointSpacing = 25f;
        public static float RunArriveRadius = 2.5f;
        public static float RunPartialArrive = 6f;
        public static float RunArriveSite = 6f;
        public static int RunGridSize = 64;
        public static float RunGridFarFromPlayer = 25f;
        public static int RunOnlineRingMargin = 2;
        public static int RunOfflineRingMargin = 0;
        public static float RunOnlineDwellSeconds = 10f;
        public static float RunOfflineDwellSeconds = 5f;
        public static float RunFrozenSeconds = 3f;
        public static float RunNoPopInRange = 50f;
        public static float RunPopInMaxWait = 20f;
        public static float RunGateDistance = 56f;
        public static float RunStuckRepathSeconds = 5f;
        public static float RunStuckSkipSeconds = 15f;
        public static float RunSkipMinPlayerDist = 40f;
        public static float RunSnapRefreshSeconds = 60f;
        public static int RunSnapMaxBytes = 60000;
        public static int RunMaterializePerTick = 1;
        // ---- supply runs ----
        public static int RunDays = 1;
        public static int MaxRunners = 3;
        public static float RunYieldPerWitsStar = 0.15f;
        public static float RunSpecialistPerStar = 0.15f;
        public static float RunRiskPerCombatStar = 0.02f;
        /// <summary>Share of bad outcomes that are deaths (the rest are injuries with half the yield).</summary>
        public static float RunDeathShare = 0.25f;
        public static int RunWitsXp = 8;
        /// <summary>Most rows the linked-storage grid shows (0 = automatic: 1000 when Grid Inventory draws it, 10 on vanilla's grid, which cannot scroll).</summary>
        public static int LinkedStorageRows = 0;
        // ---- radio ----
        /// <summary>Vanilla's scheduled air drops are refused; the sandbox frequency is the radio's cooldown.</summary>
        public static bool RadioOnlyAirdrops = true;
        public static int RadioCallDays = 2;
        public static int RadioContactDays = 7;
        public static int RadioMaxContacts = 3;
        public static int RadioContactMinDist = 300;
        public static int RadioContactMaxDist = 1500;
        public static float RadioDistressChance = 0.35f;
        public static int RescueTriggerRange = 70;
        public static float DistressZombiesBase = 4f;
        public static float DistressZombiesPerGamestage = 0.06f;
        public static float DistressZombiesPerCombatStar = 1.5f;
        public static int DistressZombiesMax = 24;
        public static string DistressZombieGroup = "ZombiesAll";
        public static float RescueBonus = 1.25f;
        // ---- outposts (the POI table and yield tunables live in ScPoiTypes) ----
        /// <summary>An outpost's POI centre must be at least this many blocks from the home block.</summary>
        public static int OutpostMinDistance = 150;
        /// <summary>Most outposts at once (slots also grow with members and Leadership).</summary>
        public static int OutpostMax = 4;
        /// <summary>slots = min(OutpostMax, 1 + members / OutpostMembersPerSlot + Leadership morale / OutpostLeadershipPerSlot).</summary>
        public static int OutpostMembersPerSlot = 4;
        public static int OutpostLeadershipPerSlot = 5;
        /// <summary>Establish fee, paid from the stockpile.</summary>
        public static int OutpostFeeMats = 2;
        public static int OutpostFeeFuel = 1;
        /// <summary>Materials per working outpost per day, and fuel per bundle delivered (fractions carry over).</summary>
        public static float OutpostUpkeep = 0.2f;
        public static float OutpostHaulage = 0.05f;
        /// <summary>Yield multipliers while upkeep (materials) or haulage (fuel) goes unpaid.</summary>
        public static float OutpostRunDownMult = 0.5f;
        public static float OutpostOnFootMult = 0.75f;
        /// <summary>While the home radio is silent, bundles wait at the outpost - at most this many days of yield.</summary>
        public static int OutpostPendingDays = 3;
        /// <summary>Outposts sharing a primary resource: the best x1, the second x0.75, the rest x0.5.</summary>
        public static float[] OutpostDupMult = { 1f, 0.75f, 0.5f };
        /// <summary>Morale: +OutpostMoralePer per producing outpost up to OutpostMoraleCap; a lost outpost costs OutpostLostPenalty, fading OutpostLostDecay a day.</summary>
        public static int OutpostMoralePer = 2;
        public static int OutpostMoraleCap = 6;
        public static float OutpostLostPenalty = 5f;
        public static float OutpostLostDecay = 1f;
        // ---- outposts v1.1: garrisons, levels, perks, scouting ----
        /// <summary>Yield per staffer (+50%), and the best matching specialty's bonus: base + per star.</summary>
        public static float OutpostStaffBonus = 0.5f;
        public static float OutpostMatchBase = 0.2f;
        public static float OutpostMatchPerStar = 0.1f;
        /// <summary>Stand-ins appear within this many blocks of a staffed outpost's centre.</summary>
        public static int OutpostStandInRange = 60;
        /// <summary>Chance a day that a staffed outpost draws a survivor in (a radio contact spawning there).</summary>
        public static float OutpostWalkInChance = 0.08f;
        /// <summary>Levels: cost (materials, fuel), days held before the upgrade, and the yield multiplier.</summary>
        public static int OutpostL2Mats = 4, OutpostL2Fuel = 2, OutpostL2Days = 3;
        public static int OutpostL3Mats = 8, OutpostL3Fuel = 4, OutpostL3Days = 7;
        public static float OutpostL2Mult = 1.3f, OutpostL3Mult = 1.5f;
        /// <summary>Extra materials upkeep a day per level above 1.</summary>
        public static float OutpostUpkeepPerLevel = 0.1f;
        /// <summary>Type perks while a producing outpost of that type exists.</summary>
        public static int OutpostClinicHeals = 1;
        public static float OutpostEscortRisk = 0.03f, OutpostEscortMax = 0.06f;
        public static float OutpostMotorPoolMult = 1.15f;
        public static float OutpostSalvageMult = 0.75f;
        public static int OutpostCanteenFloor = -10;
        /// <summary>A water outpost's cistern: the thirst morale hit is floored here (the canteen's twin, round wl).</summary>
        public static int OutpostCisternFloor = -10;
        /// <summary>Radio scouting: search radius around home, sites marked per call, days the marks last.</summary>
        public static int RadioScoutRange = 1500;
        public static int RadioScoutCount = 3;
        public static int RadioScoutDays = 3;
        // ---- outposts v1.2: output mode, check-in, depletion, distress, workshops, barter ----
        public static float OutpostQuietMult = 0.5f, OutpostPushMult = 1.5f;
        public static float OutpostQuietRisk = 0.5f, OutpostPushRisk = 1.75f;
        /// <summary>Supply-line check-in: 0 = off; else after this many days unvisited the yield slips by Decay a day to Floor.</summary>
        public static int OutpostCheckInDays = 0;
        public static float OutpostCheckInDecay = 0.1f, OutpostCheckInFloor = 0.5f;
        /// <summary>Depletion: 0 = off; else bundles an outpost yields before it is picked clean (x DepletedMult after).</summary>
        public static float OutpostDepletion = 0f;
        public static float OutpostDepletedMult = 0.25f;
        /// <summary>Chance a runner's distress call pins them at one of your outposts instead of a random POI.</summary>
        public static float OutpostDistressChance = 0.3f;
        /// <summary>Workshops: posted crafters make ammo, chemists meds, out of the surplus above Keep.</summary>
        public static bool OutpostWorkshop = true;
        public static int OutpostWorkshopKeep = 10;
        /// <summary>Radio barter: this many bundles of one resource for one of another, once a day.</summary>
        public static int RadioBarterRate = 3;
        // ---- outposts v1.3: physical investment, stash and haul ----
        /// <summary>Yield per bed / facility / crop (food outposts) built inside an outpost, each capped; total capped.</summary>
        public static float OutpostBedBonus = 0.05f, OutpostFacilityBonus = 0.1f, OutpostCropBonus = 0.02f;
        public static int OutpostBedMax = 4, OutpostFacilityMax = 3, OutpostCropMax = 10;
        public static float OutpostInvestCap = 0.5f;
        /// <summary>Block-name prefixes counted as an outpost's defenses (the v2 security rating reads them).</summary>
        public static readonly List<string> OutpostDefenseBlocks = new List<string>();
        /// <summary>Stash and haul (off by default): only Base + PerLevel bundles a day come home, the rest wait in the claim box.</summary>
        public static bool OutpostStashAndHaul = false;
        public static int OutpostHaulBase = 2, OutpostHaulPerLevel = 2, OutpostStashMax = 30;
        // ---- base defence D1: the Home Status panel and the base's noise (XUiC_ScHomeStatus, ScNoise) ----
        /// <summary>The Home Status panel at the HUD's top left while the player is inside the home zone.</summary>
        public static bool HomeStatusHud = true;
        /// <summary>The noise meter and the dead's attention (false: no noise is counted and attention stands still).</summary>
        public static bool BaseNoise = true;
        /// <summary>Steady noise: per member living at home, per running generator in the zone, per workstation counted.</summary>
        public static float NoisePerMember = 2f, NoisePerGenerator = 6f, NoisePerFacility = 1f;
        /// <summary>Event noise: x vanilla's heat for heat events in the zone, x the gun's heat for NPC shots (a gun sound with none counts NoiseNpcShotDefault).</summary>
        public static float NoiseHeatMult = 1f, NoiseNpcShotMult = 1f, NoiseNpcShotDefault = 0.5f;
        /// <summary>Event noise halves every NoiseHalfLifeHours game hours; it never stacks above NoiseEventCap.</summary>
        public static float NoiseHalfLifeHours = 1f, NoiseEventCap = 200f;
        /// <summary>Level thresholds (quiet below NoiseNoticeable); attention only grows above NoiseNoticeable.</summary>
        public static float NoiseNoticeable = 15f, NoiseLoud = 30f, NoiseVeryLoud = 50f;
        /// <summary>Attention per game hour: + (noise - NoiseNoticeable) x AttentionPerNoiseHour, - AttentionDecayPerHour.</summary>
        public static float AttentionPerNoiseHour = 0.14f, AttentionDecayPerHour = 2f;
        // ---- base defence D2: live horde attacks on the home (ScBaseAttack) ----
        /// <summary>Attention at 100 calls a horde on the home base (false: attention is still measured, nothing comes).</summary>
        public static bool BaseAttacks = true;
        /// <summary>Game days after an attack ends before the next may be called.</summary>
        public static float BaseAttackGapDays = 2f;
        /// <summary>Game hours from the call to the arrival.</summary>
        public static float BaseAttackLeadMinHours = 1f, BaseAttackLeadMaxHours = 2f;
        /// <summary>Size: (Base + gamestage x PerGamestage) x (1 + PerMember x residents), Min..Max.</summary>
        public static float BaseAttackBase = 6f, BaseAttackPerGamestage = 0.16f, BaseAttackPerMember = 0.05f;
        public static int BaseAttackMin = 4, BaseAttackMax = 40;
        /// <summary>The gamestages.xml spawner whose stage for the host's gamestage names the zombies' entity group.</summary>
        public static string BaseAttackGameStages = "ScoutGSList";
        /// <summary>Spawn ring: SpawnMin..SpawnMax m beyond the zone's edge, within Spread degrees of the attack's side, MinPlayerRange m from every player.</summary>
        public static float BaseAttackSpawnMin = 25f, BaseAttackSpawnMax = 50f, BaseAttackSpread = 30f;
        public static int BaseAttackMinPlayerRange = 30;
        /// <summary>During an attack the residents defend the zone + DefendMargin m. The player is home within HomeMargin m of the zone; LeaveRange m out they have left the fight.</summary>
        public static float BaseAttackDefendMargin = 10f, BaseAttackHomeMargin = 30f, BaseAttackLeaveRange = 150f;
        /// <summary>Game hours a live attack may run (with no kill in the last hour) before the stragglers are let go.</summary>
        public static float BaseAttackMaxHours = 4f;
        /// <summary>Real seconds after a load before the attackers of a live attack come back.</summary>
        public static float BaseAttackResumeSeconds = 15f;
        /// <summary>Morale for a held attack (the Defence part; at most 10, fades 1 a day).</summary>
        public static float BaseAttackWinMorale = 4f;
        // ---- base defence D3: watch posts and guard shifts (ScWatch, BlockScWatchPost) ----
        /// <summary>Watch posts register and take guards (false: posts are inert, guards on watch come down).</summary>
        public static bool WatchPosts = true;
        /// <summary>At most this many Watch posts in the home zone.</summary>
        public static int WatchMaxPosts = 4;
        /// <summary>A shift lasts this many game hours; after a full one the guard rests WatchRestHours before another.</summary>
        public static float WatchShiftHours = 12f, WatchRestHours = 12f;
        /// <summary>Ammo bundles a shift takes from the stockpile at its start (it cannot start with fewer); they prepay the guard's resupplies.</summary>
        public static int WatchShiftAmmo = 3;
        /// <summary>A guard on watch sees this many times farther (EntityAlive.sightRangeBase).</summary>
        public static float WatchSightMult = 1.5f;
        /// <summary>Real seconds a guard may take to reach the post before being placed on it while the player is not looking.</summary>
        public static float WatchPlaceSeconds = 30f;
        /// <summary>Each manned post cuts attention gain by this share, all of them together by at most WatchAttentionCutMax.</summary>
        public static float WatchAttentionCut = 0.15f, WatchAttentionCutMax = 0.45f;
        // ---- base defence D4: attacks while the player is away, the defence rating, the siege (ScBaseAttack, ScDefence, ScSiege) ----
        /// <summary>A horde is called, and fought by the defenders alone, while the player is away (false: a call waits for them, as in D2).</summary>
        public static bool BaseAttacksAway = true;
        /// <summary>Metres outside the zone within which an arriving horde is fought live and an away fight is joined (below BaseAttackLeaveRange).</summary>
        public static float BaseAttackJoinRange = 100f;
        /// <summary>A fight without the player lasts BaseAwayHoursBase + BaseAwayHoursPer10 per 10 attackers (game hours).</summary>
        public static float BaseAwayHoursBase = 2f, BaseAwayHoursPer10 = 1f;
        /// <summary>The roll against the rating: under it held, under it + this damaged, else overrun.</summary>
        public static int BaseAwayDamageBand = 35;
        /// <summary>Ammo bundles a fight without the player takes: one per this many attackers (at least 1), x BaseAwayAmmoOverrun when overrun.</summary>
        public static float BaseAwayAmmoPer = 10f, BaseAwayAmmoOverrun = 1.5f;
        /// <summary>Residents hurt: BaseAwayHurtMin..Max when damaged, this share of them when overrun, none when held.</summary>
        public static int BaseAwayHurtMin = 1, BaseAwayHurtMax = 2;
        public static float BaseAwayHurtOverrun = 0.67f;
        /// <summary>
        /// A hurt resident's chance to die: BaseAwayDeathChance x horde / BaseAwayDeathHorde x (1 + BaseAwayDeathSkillStep x
        /// (BaseAwayDeathSkillRef - Fighting - Shooting), 0.5..1.5), at most BaseAwayDeathCap; an unhurt one BaseAwayDeathUnhurt of it.
        /// </summary>
        public static float BaseAwayDeathChance = 0.08f, BaseAwayDeathHorde = 15f, BaseAwayDeathSkillRef = 6f, BaseAwayDeathSkillStep = 0.125f;
        public static float BaseAwayDeathCap = 0.5f, BaseAwayDeathUnhurt = 0.1f;
        /// <summary>Morale for a fight without the player: held +, damaged -, overrun - (the Defence part, fading 1 a day).</summary>
        public static float BaseAwayHeldMorale = 2f, BaseAwayDamagedMorale = 5f, BaseAwayOverrunMorale = 12f;
        /// <summary>The blood moon passing while the player was never at home besieges the base: settled on the rating minus BaseSiegePenalty.</summary>
        public static bool BaseSiege = true;
        public static int BaseSiegePenalty = 15;
        /// <summary>The defence rating's terms (ScDefence.Rating; 0..100).</summary>
        public static float BaseDefenceBase = 20f, BaseDefencePerResident = 4f, BaseDefencePerCombatStar = 1f;
        public static float BaseDefencePerPost = 10f, BaseDefencePostCap = 30f, BaseDefencePerBlock = 1f, BaseDefenceBlockCap = 20f;
        public static float BaseDefenceNoAmmo = 15f, BaseDefenceMorale = 5f;
        /// <summary>A horde bigger than BaseDefenceBigFrom lowers the rating BaseDefenceBigPer per extra attacker, by at most BaseDefenceBigCap.</summary>
        public static float BaseDefenceBigFrom = 20f, BaseDefenceBigPer = 1f, BaseDefenceBigCap = 15f;
        // ---- base defence: the Guard Tower kit (ScTower, BlockScGuardTower) ----
        /// <summary>How the whole tower's ghost looks while placing: glow, solid (vanilla's block ghost), plain (a flat tint) or off.</summary>
        public static string GuardTowerGhost = "glow";
        /// <summary>A bottom-layer block over air reaches down at most this many blocks to the ground (more: too uneven).</summary>
        public static int GuardTowerFill = 3;
        /// <summary>Terrain up to this many layers above the ground inside the tower's box is cut away (higher: too uneven).</summary>
        public static int GuardTowerCut = 2;
        // ---- outposts v2: risk (ScOutpostRisk) ----
        /// <summary>Master switch for the risk features (attacks, sieges, neglect, raider demands).</summary>
        public static bool OutpostAttacks = false;
        /// <summary>Good-news caches: bundles waiting at an outpost for the player.</summary>
        public static bool OutpostGoodNews = true;
        public static float OutpostGoodNewsChance = 0.04f;
        /// <summary>Security: base, per staffer, per combat star, per POI tier (minus), visited in 3 days, no ammo (minus), per defense (capped), won-fight bonus (fades a day, max).</summary>
        public static int OutpostSecBase = 35;
        public static int OutpostSecPerStaffer = 8;
        public static int OutpostSecPerCombatStar = 2;
        public static int OutpostSecPerTier = 4;
        public static int OutpostSecVisited = 5;
        public static int OutpostSecNoAmmo = 10;
        public static int OutpostSecPerDefense = 2;
        public static int OutpostSecDefenseCap = 30;
        public static float OutpostSecBonusFade = 1f;
        public static float OutpostSecWinBonus = 10f;
        public static float OutpostSecBonusMax = 30f;
        /// <summary>Attacks: chance a day at security 35 (scales with 100 - security and the output mode), hours to get there, attackers (base + per gamestage, x tier, capped).</summary>
        public static float OutpostAttackChance = 0.08f;
        public static int OutpostAttackHours = 10;
        public static float OutpostAttackBase = 3f;
        public static float OutpostAttackPerGamestage = 0.04f;
        public static int OutpostAttackMax = 16;
        /// <summary>Settling: security bonus when the player fought there; the band above security that ends Damaged instead of Overrun; damaged days; repair cost; blood-moon penalty.</summary>
        public static int OutpostAttendedBonus = 20;
        public static int OutpostDamageBand = 35;
        public static int OutpostDamagedDays = 2;
        public static int OutpostRepairMats = 2;
        public static int OutpostSiegePenalty = 15;
        public static float OutpostWinMorale = 4f;
        /// <summary>Neglect: days an unstaffed outpost may go unvisited before a sleeper area is re-armed.</summary>
        public static int OutpostNeglectDays = 10;
        /// <summary>Raider demands: from this gamestage, chance a day, and the price in ammo and food.</summary>
        public static int OutpostRaiderGamestage = 30;
        public static float OutpostRaiderChance = 0.03f;
        public static int OutpostDemandAmmo = 2;
        public static int OutpostDemandFood = 2;
        // ---- enclaves (ScEnclaves, E1: groups of survivors in POIs, on the map; no people yet) ----
        /// <summary>The master switch: seeding, discovery, markers and the POI protections (off: the records are kept, nothing acts).</summary>
        public static bool Enclaves = true;
        /// <summary>How many: clamp(round(EnclavesPerKm2 x the world's km2), EnclaveMin, EnclaveMax) - 11 on a 6k map, 20 on an 8k one.</summary>
        public static float EnclavesPerKm2 = 0.3f;
        public static int EnclaveMin = 4, EnclaveMax = 36;
        /// <summary>Spacing D = max(EnclaveSpacingMin, EnclaveSpacingFactor x sqrt(world area / N)) m, relaxed 15% a pass down to EnclaveSpacingFloor.</summary>
        public static int EnclaveSpacingMin = 500;
        public static float EnclaveSpacingFactor = 0.55f;
        public static int EnclaveSpacingFloor = 300;
        /// <summary>The share that are raiders, never within EnclaveHostileMinStart m of the start or a spawn point (half that on maps under 4 km).</summary>
        public static float EnclaveHostileShare = 0.25f;
        public static int EnclaveHostileMinStart = 1200;
        /// <summary>The start ring: up to EnclaveStartRing non-hostile enclaves EnclaveStartRingMin..Max m from the start, EnclaveStartRingSpacing m apart.</summary>
        public static int EnclaveStartRing = 2;
        public static int EnclaveStartRingMin = 400, EnclaveStartRingMax = 1500, EnclaveStartRingSpacing = 600;
        /// <summary>Which POIs: tier, the footprint's larger side (m), and the distance from traders, spawn points and the world's edge (m).</summary>
        public static int EnclaveTierMin = 1, EnclaveTierMax = 4;
        public static int EnclaveSizeMin = 20, EnclaveSizeMax = 80;
        public static int EnclaveTraderDistance = 300, EnclaveSpawnDistance = 300, EnclaveEdgeDistance = 300;
        /// <summary>Weight by district (a POI's first Tags entry of ScPoiIndex.KindNames, else "other"); 0 = never; a district not listed weighs as "other".</summary>
        public static readonly Dictionary<string, float> EnclaveKindWeights = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Prefab-name parts (lower case) never picked: caves, mines, lots, rubble, bridges and the like.</summary>
        public static readonly List<string> EnclavePoiExcludes = new List<string>();
        /// <summary>An existing save gets enclaves the first time this build loads it: none within EnclaveClaimBuffer m of a claim, none in loaded chunks.</summary>
        public static bool EnclaveLateSeed = true;
        public static int EnclaveClaimBuffer = 150;
        /// <summary>The late seed's (and a top-up's) weight for a POI never generated in the save, so nobody has looted it.</summary>
        public static float EnclaveUnvisitedWeight = 3f;
        /// <summary>An enclave whose POI is gone moves to the nearest free POI within this many m, else it is dropped.</summary>
        public static int EnclaveRelocateRange = 1000;
        /// <summary>Found within EnclaveDiscoverRange m of its POI; a powered radio picks one up every EnclaveRadioRevealDays within EnclaveRadioRange m of home.</summary>
        public static int EnclaveDiscoverRange = 150;
        public static int EnclaveRadioRevealDays = 3;
        public static int EnclaveRadioRange = 2000;
        private const string DefaultEnclaveKindWeights = "rural:1.5,countryresidential:1.4,countrytown:1.3,wilderness:1.2,oldwest:1.2,residential:1,commercial:0.9,industrial:0.8,other:0.6,downtown:0";
        private const string DefaultEnclavePoiExcludes = "cave,mine,bunker,bombshelter,rubble,lot_,filler,street,bridge,tunnel,sewer,crater,crack,parking,remnant_waste,wreck,dock,skyscraper,water_tower,celltower,sign,billboard,deco";
        // ---- enclave people (ScEnclavePeople, E2: their records and bodies, the SCEnclave AI, standing and hostility) ----
        /// <summary>
        /// A group's size: by the footprint's larger side (EnclaveSizeMin..Max m) from EnclaveGroupMin to EnclaveGroupMax people
        /// (a raider enclave from EnclaveRaiderGroupMin to EnclaveRaiderGroupMax), +-1 from a seeded roll, clamped.
        /// </summary>
        public static int EnclaveGroupMin = 3, EnclaveGroupMax = 5, EnclaveRaiderGroupMin = 3, EnclaveRaiderGroupMax = 6;
        /// <summary>
        /// Bodies: built within EnclaveSpawnRange m of the footprint, at most EnclaveSpawnPerTick a second, never at a spot within
        /// EnclaveNoPopRange m in a player's view; removed out of sight once nobody is within EnclaveSpawnRange +
        /// EnclaveDespawnMargin m. EnclaveHoldChunks keeps the POI's chunks loaded while its bodies are out.
        /// </summary>
        public static int EnclaveSpawnRange = 150, EnclaveDespawnMargin = 50, EnclaveNoPopRange = 50, EnclaveSpawnPerTick = 1;
        public static bool EnclaveHoldChunks = true;
        /// <summary>The day rollover gives every living person their full health and their starting ammo back.</summary>
        public static bool EnclaveDailyRestock = true;
        /// <summary>
        /// The SCEnclave AI: the footprint plus EnclaveLeashMargin m is home (beyond it the leash walks them back to their spot),
        /// and they go after an enemy inside the footprint within EnclaveDefendRadius m of themselves.
        /// </summary>
        public static int EnclaveLeashMargin = 8, EnclaveDefendRadius = 22;
        /// <summary>
        /// Standing's floor (Neutral below 30 whatever the points). Attacks: the first counted hit is a warning costing
        /// EnclaveWarnPenalty; another within EnclaveWarnWindow s turns them hostile; hits within EnclaveHitDebounce s of the last
        /// counted one are the same attack.
        /// </summary>
        public static int EnclaveStandingMin = -30, EnclaveWarnPenalty = 10;
        public static float EnclaveWarnWindow = 120f, EnclaveHitDebounce = 1.5f;
        /// <summary>A threat pays EnclaveThreatBundlesMin..Max bundles; they attack EnclaveThreatGrace s later.</summary>
        public static int EnclaveThreatBundlesMin = 2, EnclaveThreatBundlesMax = 4;
        public static float EnclaveThreatGrace = 10f;
        /// <summary>Their POI's loot containers are refused while they live; no wild NPC spawns within EnclaveWildSpawnMargin m of a footprint.</summary>
        public static bool EnclaveContainerLock = true;
        public static int EnclaveWildSpawnMargin = 40;
        /// <summary>EnclaveKindRows "row:kind,...": an enclave's kind by its POI's ScPoiTypes row (a row not named: general).</summary>
        public static readonly Dictionary<string, string> EnclaveKindRows = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private const string DefaultEnclaveKindRows = "farm:growers,clinic:medics,military:militia,police:militia,gunstore:militia,industry:builders,hardware:builders,gasstation:roadcrew,refinery:roadcrew";
        // ---- enclave trade, enlisting and recruiting (ScEnclaveDeals, E3) ----
        /// <summary>
        /// The leader's trader_info ids (Config/traders.xml): EnclaveTraderIdBase + 0..5 for general, growers, medics, militia,
        /// builders and roadcrew. Must match the file's ids (a missing one: that kind's leader does not trade).
        /// </summary>
        public static int EnclaveTraderIdBase = 200;
        /// <summary>Price factors by standing: what they charge you (Buy) and what they pay you (Sell); your barter perks on top.</summary>
        public static float EnclaveBuyNeutral = 1f, EnclaveBuyFriendly = 1f, EnclaveBuyAllied = 0.8f;
        public static float EnclaveSellNeutral = 1f, EnclaveSellFriendly = 1.2f, EnclaveSellAllied = 1.35f;
        /// <summary>Enlisting one of their people: dukes by standing, for EnclaveEnlistDays days; their death meanwhile costs EnclaveEnlistDeathPenalty standing.</summary>
        public static int EnclaveEnlistCostNeutral = 500, EnclaveEnlistCostFriendly = 250, EnclaveEnlistCostAllied = 0;
        public static int EnclaveEnlistDays = 2, EnclaveEnlistDeathPenalty = 5;
        /// <summary>Recruiting: at least EnclaveRecruitMinLeft stay, one every EnclaveRecruitCooldownDays days, EnclaveRecruitStanding standing at Neutral and Friendly.</summary>
        public static int EnclaveRecruitMinLeft = 2, EnclaveRecruitCooldownDays = 7, EnclaveRecruitStanding = 10;
        // ---- enclave requests (ScEncRequests, E4: standing changes, pacing, the resource, generator and gun requests) ----
        /// <summary>The day's chance of a new request from an enclave, by its band (Q17); at most EnclaveRequestMaxOpen open across the map.</summary>
        public static float EnclaveRequestChanceNeutral = 0.35f, EnclaveRequestChanceFriendly = 0.45f, EnclaveRequestChanceAllied = 0.2f;
        public static int EnclaveRequestMaxOpen = 3;
        /// <summary>
        /// Days: an enclave rests EnclaveRequestCooldownDays whole days after the day its last request closed; an offer stands
        /// EnclaveOfferLapseDays days (the day it is made counts); an accepted item or gun request is due in EnclaveDueDaysItems
        /// days (the day it is accepted counts); generator requests from day EnclaveGeneratorFromDay.
        /// </summary>
        public static int EnclaveRequestCooldownDays = 1, EnclaveOfferLapseDays = 3, EnclaveDueDaysItems = 4, EnclaveGeneratorFromDay = 10;
        /// <summary>
        /// EnclaveRequestKindWeights "resource:6,gun:3,generator:1,missing:2,infestation:2" (E5 adds the last two; a siege has
        /// its own pace) and EnclaveRequestSizeWeights "small:5,medium:3,large:2" (0 = never).
        /// </summary>
        public static readonly Dictionary<string, float> EnclaveRequestKindWeights = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        public static readonly Dictionary<string, float> EnclaveRequestSizeWeights = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        private const string DefaultEnclaveRequestKindWeights = "resource:6,gun:3,generator:1,missing:2,infestation:2";
        private const string DefaultEnclaveRequestSizeWeights = "small:5,medium:3,large:2";
        /// <summary>Standing gained for a request done: a resource request (whatever its size), a generator.</summary>
        public static int EnclaveGainResource = 10, EnclaveGainGenerator = 15;
        /// <summary>
        /// A gun's gain (Q11): EnclaveGunTierPoints[tier] (T0..T5) x (EnclaveGunQualityBase + EnclaveGunQualityStep x quality),
        /// rounded, clamped to EnclaveGunPointsMin..Max - a T1 pistol of quality 3 is +6, a T3 rifle of quality 5 +19. A gun with
        /// no tier tag, no T# in its name and not in the NPC weapon catalogue takes its tier from its EconomicValue: T1 from
        /// EnclaveGunValueBands[0], T2 from [1] ... The gun pick's worth phrase (scEncReqWorth0..4) by points: up to
        /// EnclaveGunWorthBands[0] the first, up to [1] the second ... above the last the fifth.
        /// </summary>
        public static readonly int[] EnclaveGunTierPoints = { 3, 6, 10, 15, 18, 22 };
        public static float EnclaveGunQualityBase = 0.75f, EnclaveGunQualityStep = 0.1f;
        public static int EnclaveGunPointsMin = 3, EnclaveGunPointsMax = 30;
        public static readonly int[] EnclaveGunValueBands = { 400, 1200, 3500, 7000, 12000 };
        public static readonly int[] EnclaveGunWorthBands = { 5, 9, 14, 20 };
        /// <summary>
        /// Standing lost by band (Neutral, Friendly, Allied): saying no (EnclaveLossDecline), accepting then giving up or running
        /// out of time (EnclaveLossFail), leaving an offer unanswered until it lapses (EnclaveLossLapse).
        /// </summary>
        public static readonly int[] EnclaveLossDecline = { 5, 5, 12 };
        public static readonly int[] EnclaveLossFail = { 8, 8, 15 };
        public static readonly int[] EnclaveLossLapse = { 0, 3, 15 };
        // ---- enclave requests, part 2 (E5: the missing survivor ScEncSearch, the infestation ScEncInfest, the siege ScEncSiege) ----
        /// <summary>A missing survivor's or an infestation request's days, once accepted (the day it is accepted counts).</summary>
        public static int EnclaveDueDaysSearch = 3;
        /// <summary>
        /// A missing survivor's building: a real POI with sleeper areas EnclaveMissingMin..EnclaveMissingMax m from the enclave
        /// (centre to centre). Their fate is rolled when the player first gets there: alive with EnclaveMissingAlive on the day
        /// the request was accepted, EnclaveMissingAliveFade less each day after, never below EnclaveMissingAliveMin.
        /// </summary>
        public static int EnclaveMissingMin = 200, EnclaveMissingMax = 800;
        public static float EnclaveMissingAlive = 0.8f, EnclaveMissingAliveFade = 0.2f, EnclaveMissingAliveMin = 0.1f;
        /// <summary>An infestation request names one within EnclaveInfestRange m of the enclave's footprint, or starts one there.</summary>
        public static int EnclaveInfestRange = 300;
        /// <summary>
        /// The siege (Q17: about one a week): each day EnclaveSiegeChance that a known enclave that is not hostile, within
        /// EnclaveSiegeRange m of the player and with no open request, calls for help at a random hour EnclaveSiegeHourMin..
        /// EnclaveSiegeHourMax. The player has EnclaveSiegeMinutes real minutes (world time at the day's speed: pausing stops
        /// it, sleeping uses it up) to come within EnclaveSiegeArriveRange m of its footprint; leaving a live siege beyond
        /// twice that ends the fight there. A live siege with no kill for a game hour, EnclaveSiegeMaxHours after it began,
        /// is over (the stragglers let go).
        /// </summary>
        public static float EnclaveSiegeChance = 0.15f;
        public static int EnclaveSiegeHourMin = 8, EnclaveSiegeHourMax = 20;
        public static int EnclaveSiegeRange = 2000;
        public static float EnclaveSiegeMinutes = 15f;
        public static int EnclaveSiegeArriveRange = 70;
        public static float EnclaveSiegeMaxHours = 3f;
        /// <summary>
        /// The horde: (EnclaveSiegeBase + gamestage x EnclaveSiegePerGS) x living people / 4, EnclaveSiegeMin..EnclaveSiegeMax,
        /// fixed at the call; one a second, EnclaveSiegeSpawnMin..EnclaveSiegeSpawnMax m beyond the footprint within
        /// EnclaveSiegeSpread degrees of its far side from the player, at least EnclaveSiegeMinPlayerRange m from every player
        /// and never within EnclaveSiegeNoPopRange m in a player's view. After a load the ones still standing come back
        /// EnclaveSiegeResumeSeconds later.
        /// </summary>
        public static float EnclaveSiegeBase = 6f, EnclaveSiegePerGS = 0.12f;
        public static int EnclaveSiegeMin = 4, EnclaveSiegeMax = 30;
        public static int EnclaveSiegeSpawnMin = 25, EnclaveSiegeSpawnMax = 50;
        public static float EnclaveSiegeSpread = 90f;
        public static int EnclaveSiegeMinPlayerRange = 30, EnclaveSiegeNoPopRange = 50;
        public static float EnclaveSiegeResumeSeconds = 15f;
        /// <summary>The player leaving a live siege: the chance it holds is the share of the horde killed plus EnclaveSiegeLeaveRoll a living person.</summary>
        public static float EnclaveSiegeLeaveRoll = 0.05f;
        /// <summary>Standing for E5's requests done: a survivor brought home, one found dead and told of, an infestation cleared, a siege beaten.</summary>
        public static int EnclaveGainMissingAlive = 15, EnclaveGainMissingDead = 8, EnclaveGainInfestation = 15, EnclaveGainSiege = 25;
        // ---- allies (E6: ScEncAllies - each group's benefit while Allied, the Allies morale part, refills, allies telling of more) ----
        /// <summary>
        /// The benefits, while an enclave is Allied: the five resource ones add EnclaveBenefitBundles units a day; Comfort in
        /// Numbers +EnclaveBenefitMorale morale (a part of its own); Overflow Barracks +EnclaveBenefitBeds beds; the four clubs
        /// multiply your members' XP in their skill by EnclaveBenefitClubXp, Training and Advice every skill's by
        /// EnclaveBenefitTrainingXp - several stack additively, the whole multiplier at most EnclaveBenefitXpCap; Neighborhood
        /// Watch: no new infestation within EnclaveWatchRange m of them, those within it spread at EnclaveWatchSpread the chance.
        /// </summary>
        public static int EnclaveBenefitBundles = 1, EnclaveBenefitMorale = 5, EnclaveBenefitBeds = 3;
        public static float EnclaveBenefitClubXp = 2f, EnclaveBenefitTrainingXp = 1.5f, EnclaveBenefitXpCap = 3f;
        public static int EnclaveWatchRange = 500;
        public static float EnclaveWatchSpread = 0.5f;
        /// <summary>Morale: +EnclaveAllyMorale per Allied enclave, at most EnclaveAllyMoraleCap; -EnclaveLostMorale for a Friendly or Allied one lost, fading a point a day.</summary>
        public static int EnclaveAllyMorale = 2, EnclaveAllyMoraleCap = 6, EnclaveLostMorale = 5;
        /// <summary>A lost enclave's refill comes EnclaveRefillDays later; an ally tells of one more on becoming Allied, then every EnclaveAllyRevealDays.</summary>
        public static int EnclaveRefillDays = 5, EnclaveAllyRevealDays = 7;
        // ---- infestations (ScInfestations, I1: POIs overrun by more and worse zombies) ----
        /// <summary>The master switch: the daily roll, spread, the spawn changes and the markers (off: the records are kept, nothing acts).</summary>
        public static bool Infestations = true;
        /// <summary>The chance a day that a new one starts; at most clamp(round(InfestPerKm2 x the world's km2), InfestMin, InfestMax) at once - 8 on a 6k map, 13 on 8k.</summary>
        public static float InfestDailyChance = 0.4f;
        public static float InfestPerKm2 = 0.2f;
        public static int InfestMin = 2, InfestMax = 30;
        /// <summary>Spread: one at least InfestSpreadDays old has InfestSpreadChance a day to infest the nearest eligible POI within InfestSpreadRange m.</summary>
        public static int InfestSpreadDays = 3;
        public static float InfestSpreadChance = 0.3f;
        public static int InfestSpreadRange = 300;
        /// <summary>
        /// Which POIs: at least InfestTraderDistance m from a trader; the daily roll weighs a POI 1 within InfestMinDist..InfestMaxDist m
        /// of home (of the player without one) and InfestOutsideWeight outside, and takes the tiers of the day (InfestTierByDay).
        /// </summary>
        public static int InfestTraderDistance = 150;
        public static int InfestMinDist = 300, InfestMaxDist = 2500;
        public static float InfestOutsideWeight = 0.1f;
        /// <summary>InfestTierByDay "day:min-max,...", sorted by day: {day, min, max} - from that day on, the tiers the daily roll takes.</summary>
        public static readonly List<int[]> InfestTierByDay = new List<int[]>();
        /// <summary>
        /// Inside: the rooms' count multiplier (InfestMultiplierHighTier from prefab tier InfestHighTier up, vanilla's infested quests'
        /// numbers), the share of normal zombies that come feral, and the special (InfestSpecial): radiated below gamestage
        /// InfestSpecialRadiatedBelowGS, then charged or infernal.
        /// </summary>
        public static float InfestMultiplier = 2f, InfestMultiplierHighTier = 1.5f;
        public static int InfestHighTier = 4;
        public static float InfestFeralShare = 0.35f;
        public static bool InfestSpecial = true;
        public static int InfestSpecialRadiatedBelowGS = 50;
        /// <summary>
        /// Found within InfestDiscoverRange m of its POI; a powered radio reports a new one within InfestRadioRange m of home (of the
        /// player without one); none starts or spreads within InfestPlayerMargin m of a player; a room with sleepers left from before
        /// is reset only with no player within InfestRearmPlayerRange m.
        /// </summary>
        public static int InfestDiscoverRange = 150, InfestRadioRange = 2000, InfestPlayerMargin = 50, InfestRearmPlayerRange = 100;
        private const string DefaultInfestTierByDay = "1:1-2,8:1-3,15:2-4,29:3-5";
        // ---- infestations near home (ScInfestations.NearHome, I3) ----
        /// <summary>
        /// A nest is near home within InfestHomeRange m of the home block (XZ, to its footprint; 0 = none is). Morale: each of the
        /// nearest InfestHomeMoraleNests costs its distance band's points (InfestHomeMoraleBands), at most InfestHomeMoraleCap in
        /// all. Attention: +InfestHomeAttention a game hour for each, at most InfestHomeAttentionCap counted. With InfestHomeKnown
        /// one that starts or spreads near home is known at once (and those of a save, once a session and when the home moves).
        /// </summary>
        public static int InfestHomeRange = 500, InfestHomeMoraleCap = 15, InfestHomeMoraleNests = 5, InfestHomeAttentionCap = 5;
        public static float InfestHomeAttention = 1f;
        public static bool InfestHomeKnown = true;
        /// <summary>InfestHomeMoraleBands "metres:cost,...", sorted by distance: {metres, cost} - a nest at most that far costs that much (the first band it fits; past the last, 0).</summary>
        public static readonly List<int[]> InfestHomeMoraleBands = new List<int[]>();
        private const string DefaultInfestHomeMoraleBands = "150:5,300:3,500:2";
        // ---- the zombie-cap fix (ScSleeperCap, I2: sleeper rooms the enemy cap closed empty or cut short) ----
        /// <summary>
        /// Layers A and C, every POI: a room the enemy cap closed with nobody spawned is due again SleeperCapRetryTicks later
        /// (vanilla's own "player inside" wait is 1000, about a game hour), and one saved that way before is repaired as a
        /// player comes near it.
        /// </summary>
        public static bool SleeperCapFix = true;
        public static int SleeperCapRetryTicks = 1000;
        /// <summary>Layer B, infested rooms only: a room the cap cuts short waits while a player is near, at most SleeperCapPauseMaxTicks.</summary>
        public static bool SleeperCapPause = true;
        public static int SleeperCapPauseMaxTicks = 3000;
        // ---- survivors and raiders wearing the player's body (0-XNPCCore's survivors, ScXnpc; SC's scRaider* classes) ----
        /// <summary>No longer used: it was the chance a radio contact is a player-body survivor rather than a pack NPC,
        /// and pack NPCs no longer spawn at all (every contact is now one of ours). Still parsed so old configs load.</summary>
        public static float SurvivorLookChance = 0.5f;
        /// <summary>Whether the player's own attacks can hurt members of their community. NPCs outside the
        /// community are always damageable; the NPC framework shields both, which this undoes.</summary>
        public static bool CommunityFriendlyFire = false;
        /// <summary>Vanilla armor sets (armor&lt;Set&gt;Helmet/Outfit/Gloves/Boots) a survivor may turn up wearing.</summary>
        public static readonly List<string> SurvivorGearSets = new List<string>();
        public static float SurvivorGearPieceChance = 0.8f;
        /// <summary>Whether the body armor piece ignores SurvivorGearPieceChance and is always worn.</summary>
        public static bool SurvivorGearAlwaysChest = true;
        public static float SurvivorGearMixChance = 0.3f;
        /// <summary>Armor quality range when the band is unknown (sc gear); a rolled NPC uses GearQualityByTier.</summary>
        public static int SurvivorGearQualityMin = 1;
        public static int SurvivorGearQualityMax = 4;
        /// <summary>
        /// Each vanilla armor set's weight class (Primitive and light 0, medium 1, heavy 2); an unlisted set weighs 1
        /// everywhere. GearTierWeights[band][class] is a set's weight when an NPC of that effective band rolls (the user's
        /// pick 2026-10-08: lighter sets likelier early, heavier later, nothing ruled out).
        /// </summary>
        public static readonly Dictionary<string, int> GearSetTiers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Sets that stop at a band: "Primitive:0" - the starter armor is T1 NPCs' only (the user, 2026-10-09: no T0 gear on T2 NPCs).</summary>
        public static readonly Dictionary<string, int> GearSetLastTier = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public static float[][] GearTierWeights = DefaultGearTierWeights();
        /// <summary>Armor quality [min, max] by effective band (T1 1-2, T2 2-4, T3 3-5).</summary>
        public static int[][] GearQualityByTier = DefaultGearQuality();
        /// <summary>Mod outfits (OutfitDef): how often one is worn instead of a vanilla set, when one fits the NPC.</summary>
        public static readonly List<OutfitDef> Outfits = new List<OutfitDef>();
        public static float SurvivorOutfitShare = 0.35f;
        public static float RaiderOutfitShare = 0.35f;
        public static float SurvivorBaldChance = 0.15f;
        public static float SurvivorFacialHairChance = 0.4f;
        /// <summary>
        /// Character-creator options never rolled for survivors or raiders (exact names, case-insensitive; a trailing '*'
        /// matches a prefix). Defaults = the user's picks of 2026-10-07: Vanity Vault's tinted skins and its eyes.
        /// </summary>
        public static readonly List<string> SurvivorExcludeSkins = new List<string>();
        public static readonly List<string> SurvivorExcludeEyes = new List<string>();
        public static readonly List<string> SurvivorExcludeHairs = new List<string>();
        /// <summary>The dyed hair colours, rolled only with SurvivorDyedHairChance (female / male); the rest are natural.</summary>
        public static readonly List<string> SurvivorDyedHairColors = new List<string>();
        public static float SurvivorDyedHairChanceFemale = 0.06f;
        public static float SurvivorDyedHairChanceMale = 0.005f;
        /// <summary>
        /// Vanity Vault paints and tattoos on new survivors and raiders (ScVanity; the user's picks of 2026-10-07): the
        /// chance per new look, and the lists to pick from - body paint sets as "body:face" (the body alone when it has
        /// no face paint), face paints alone, tattoo shapes, and tattoo inks (a name twice is twice as likely).
        /// </summary>
        public static float SurvivorBodyPaintChance = 0.01f;
        public static float RaiderBodyPaintChance = 0.06f;
        public static float SurvivorFacePaintChance = 0.02f;
        public static float RaiderFacePaintChance = 0.1f;
        public static float SurvivorTattooChance = 0.15f;
        public static float RaiderTattooChance = 0.35f;
        public static readonly List<string> SurvivorBodyPaints = new List<string>();
        public static readonly List<string> RaiderBodyPaints = new List<string>();
        public static readonly List<string> SurvivorFacePaints = new List<string>();
        public static readonly List<string> RaiderFacePaints = new List<string>();
        public static readonly List<string> SurvivorTattoos = new List<string>();
        public static readonly List<string> RaiderTattoos = new List<string>();
        public static readonly List<string> SurvivorTattooInks = new List<string>();
        public static readonly List<string> RaiderTattooInks = new List<string>();
        /// <summary>Vanity Vault's tattoo mode: Blend (ink in the skin) or Vivid (painted on top).</summary>
        public static string TattooMode = "Blend";
        /// <summary>Survivors and raiders with the player's body shoulder their gun (or draw their bow) while shooting (ScAim).</summary>
        public static bool SurvivorAimPose = true;
        /// <summary>The aim pose is only a pose: their spread, critical hits and kickback stay as without it. False gives
        /// aiming NPCs the aimed spread and a critical on every hit, as aiming does for a player.</summary>
        public static bool SurvivorAimVisualOnly = true;
        /// <summary>Seconds the weapon stays up after the last ranged attack.</summary>
        public static float SurvivorAimHoldSeconds = 2f;
        /// <summary>OBSOLETE since M10 (raiders on XNPC bodies): it switched the old scRaider&lt;Sex&gt;&lt;Weapon&gt; classes'
        /// rolled second weapon. Raiders now roll their pair by RaiderRangedShare / RaiderFirearmChance, and survivors always
        /// carry theirs. Still parsed so an old blocks.xml loads; nothing reads it.</summary>
        public static bool SurvivorSecondWeapon = true;
        /// <summary>The odds a survivor's ranged weapon is a firearm rather than a bow or crossbow (raiders: RaiderFirearmChance).</summary>
        public static float SurvivorFirearmChance = 0.12f;
        /// <summary>The odds a new survivor carries only a melee weapon (Loadout.NoRanged; raiders: RaiderMeleeOnlyChance).</summary>
        public static float SurvivorMeleeOnlyChance = 0.15f;
        /// <summary>
        /// A T3 survivor's odds of carrying a firearm at all (round t3, the user's ask: 85%); the rest are melee-only or carry a
        /// bow or crossbow in the proportion the two chances above give them. T1 and T2 roll by those two chances alone.
        /// </summary>
        public static float SurvivorFirearmChanceT3 = 0.85f;
        /// <summary>
        /// Decision D4 (tiers by gamestage), kept at spawn time since 2026-10-09: a spawn group's pick of one of our tiered
        /// classes above the gamestage tier comes in as the same side and sex at that tier (ScLoadout.Patch_ClassByGamestage),
        /// and a body's weapons and clothes follow its class tier alone - so a T2 NPC never carries T0 gear.
        /// </summary>
        public static bool XnpcClassByGamestage = true;
        /// <summary>OBSOLETE since M10: how often a hostile console spawn got a ranged class weapon (the old raider classes).
        /// Still parsed so an old blocks.xml loads; nothing reads it.</summary>
        public static float SurvivorRangedMainChance = 0.4f;
        /// <summary>The vanilla weapons the community's 0-XNPCCore survivors may carry (XnpcWeapons class, generated).</summary>
        public static readonly List<XnpcWeaponDef> XnpcWeapons = new List<XnpcWeaponDef>();
        /// <summary>Rounds of its gun's basic ammo a new 0-XNPCCore survivor carries in its bag, once per entity (decision D3:
        /// finite ammo). 0 = none. With Locked &amp; Loaded (M11) only for a gun without magazines: one that takes them gets
        /// L&amp;L's tier magazines instead.</summary>
        public static int XnpcSpawnAmmo = 30;
        /// <summary>The same for a bow or crossbow: arrows or bolts.</summary>
        public static int XnpcSpawnArrows = 20;
        // ---- ammo resupply (ScAmmo, migration M7.3, decision D3) ----
        /// <summary>A member (or an outpost's stand-in) whose gun runs low is resupplied from the stockpile's ammo, but only
        /// inside the home zone or at one of the community's outposts. False: they only ever have what they carry.</summary>
        public static bool XnpcResupply = true;
        /// <summary>Resupplied when the gun's rounds (magazine plus every store SCore reloads from) are below this.</summary>
        public static int XnpcResupplyBelow = 10;
        /// <summary>Rounds of the gun's basic ammo one resupply hands over (XnpcResupplyArrows: arrows or bolts for a bow).
        /// With Locked &amp; Loaded (M11) the most one resupply loads into a member's magazines; it pays only for what they take.</summary>
        public static int XnpcResupplyAmmo = 30;
        public static int XnpcResupplyArrows = 20;
        /// <summary>Real seconds before the same NPC may be resupplied again (so a long fight in the base does not drain
        /// the stockpile in one go).</summary>
        public static float XnpcResupplyCooldown = 60f;
        /// <summary>
        /// The conversion from the stockpile's ammo to rounds: a round costs its packing points (EconomicValue, or its
        /// ValueOverrides entry - what it was worth when it was packed) times this, and an ammo bundle holds
        /// PointsPerBundleAmmo points. 1 = a bundle gives back what it took to pack (2000 points: about 220 9 mm rounds or
        /// 165 stone arrows). Above 1 ammo costs more to hand out than it was worth in the box.
        /// </summary>
        public static float XnpcResupplyPointsMult = 1f;
        // ---- overwhelmed (ScArms, migration M7.5, the weapons task's W4/W6 decision) ----
        /// <summary>An overwhelmed survivor keeps the gun instead of 0-XNPCCore's melee swap at 3 m (while it has ammo).</summary>
        public static bool Overwhelmed = true;
        /// <summary>Enemies targeting the survivor within this many metres are counted.</summary>
        public static float OverwhelmedRadius = 12f;
        /// <summary>
        /// How many of them make a member overwhelmed: OverwhelmedMin + (Fighting stars - 1) / OverwhelmedStarsPerStep,
        /// at most OverwhelmedMax (2/2/3/3/4/4/5 for 1-7 stars: a better fighter takes on more up close before falling back
        /// on the gun). Anyone else (a stranger) uses OverwhelmedStrangers.
        /// </summary>
        public static int OverwhelmedMin = 2;
        public static int OverwhelmedMax = 5;
        public static int OverwhelmedStarsPerStep = 2;
        public static int OverwhelmedStrangers = 3;
        /// <summary>
        /// Seconds the state outlasts the count dropping below the threshold (no weapon flapping at the edge). 5 since the
        /// M11 fix 2 (was 3): zombies keep switching their target between the player and the survivor, so the count sat at
        /// the threshold and dipped for 3-4 s at a time (MELEE_DIAG cause 3), and each dip let 0-XNPCCore swap to melee
        /// with the gun's next return costing a Locked &amp; Loaded reload of about 2 s - staying on a loaded gun costs nothing.
        /// </summary>
        public static float OverwhelmedHoldSeconds = 5f;
        /// <summary>
        /// The M11 fix 2 (MELEE_DIAG F3): a survivor on its melee weapon goes back to the gun only once the count has stayed
        /// at the threshold for this long (three recounts), and with its target within 1.5 m only at the threshold + 1 - a
        /// zombie switching its target for a moment is not a swarm.
        /// </summary>
        public static float OverwhelmedSustainSeconds = 1.5f;
        /// <summary>After such a return to the gun, 0-XNPCCore's swap to melee is held off this long (the reload plus some shooting).</summary>
        public static float OverwhelmedGunSeconds = 6f;
        // ---- melee (ScMelee, the M11 fix 2, MELEE_DIAG F1 and F4) ----
        /// <summary>Survivors swing their melee weapon as fast as it allows (its AttacksPerMinute), not on SCore's 1 s attack ticks.</summary>
        public static bool MeleePacing = true;
        /// <summary>The same for SC's raiders (the user's decision: on; a raider then swings about 60% faster than before).</summary>
        public static bool MeleePacingRaiders = true;
        /// <summary>SCore's in-range test uses the melee weapon's real reach (its MaxRange less MeleeReachMargin) instead of 1.5 m.</summary>
        public static bool MeleeReach = true;
        /// <summary>Metres kept off the weapon's reach so a swing started at the edge still lands on a target that steps back.</summary>
        public static float MeleeReachMargin = 0.4f;
        // ---- the Shooting skill aims the gun (ScShooting, round sh) ----
        /// <summary>Our bodies' guns hit by their Shooting stars and engage no farther than they shoot well; false: perfect aim as before.</summary>
        public static bool ShootingSkill = true;
        /// <summary>The hit range per Shooting star (5 m at 1 star, 35 m at 7): inside it nearly every shot lands.</summary>
        public static float ShootingRangePerStar = 5f;
        /// <summary>A gun engages out to this many times its shooter's hit range.</summary>
        public static float ShootingEngageMult = 2f;
        /// <summary>...but never closer than this (at least 10: 0-XNPCCore's BackupRanged walks a shooter back to 10 m).</summary>
        public static float ShootingEngageMin = 12f;
        /// <summary>The aim error's size at the hit range, in metres: about a torso's half-width.</summary>
        public static float ShootingHitRadius = 0.25f;
        /// <summary>Shots are aimed at the target's chest instead of the head SCore aims at.</summary>
        public static bool ShootingAimChest = true;
        /// <summary>Stars for one of our bodies that has none (someone's hired companion, an older body).</summary>
        public static int ShootingDefaultStars = 2;
        /// <summary>A raider's Shooting stars by tier (T1, T2, T3): low and high, rolled at spawn.</summary>
        public static readonly int[,] RaiderShootingStars = { { 1, 2 }, { 2, 4 }, { 4, 6 } };
        // ---- the dialog line and voiced lines (XUiC_ScDialogLine, ScVoice; the voice test, 2026-10-16) ----
        /// <summary>The NPC's line shows above the dialog's responses (vanilla shows only the responses).</summary>
        public static bool DialogLine = true;
        /// <summary>A survivor's line plays as a recorded clip when Resources/Voices has one in their voice.</summary>
        public static bool VoiceLines = true;
        /// <summary>V1: while SC's talk is open on one of the community's survivors, or SC's line still plays on them, Voice Control keeps quiet on them (ScVoice.HoldsVoice).</summary>
        public static bool MuteVcInTalk = true;
        // ---- 0-XNPCCore's survivors (ScXnpc, Patches_Xnpc) ----
        /// <summary>The 0-XNPCCore survivor classes the community owns: SC's looks, names, healing and aim pose on them.</summary>
        public static readonly List<string> XnpcSurvivorClasses = new List<string>(ScXnpc.DefaultClasses);
        /// <summary>Their knock-down recovery runs as a player's (Patches_Sdcs ragdoll fixes); the raiders' too (every switch below covers both sets).</summary>
        public static bool XnpcBodyGetUp = true;
        /// <summary>While one of them plays its get-up clip, IsMoving is held false so the locomotion layer cannot cut it short (Patches_Sdcs.GetUp).</summary>
        public static bool XnpcGetUpGuard = true;
        /// <summary>One log line per knockdown of one of them: the ragdoll's steps and what the animator's locomotion layer did (Patches_Sdcs.GetUp).</summary>
        public static bool XnpcGetUpTrace = true;
        /// <summary>Out of danger, they lower their arms and hide the weapon they still hold (ScRelax).</summary>
        public static bool XnpcRelax = true;
        /// <summary>Raiders relax too (the user's call: they look unarmed until they spot someone).</summary>
        public static bool XnpcRelaxRaiders = true;
        /// <summary>A relaxed body's weapon is hidden; false only lowers the arms (the weapon stays in the lowered hand).</summary>
        public static bool XnpcRelaxHideWeapon = true;
        /// <summary>Seconds with no alert reason before a body relaxes; any reason raises it at once.</summary>
        public static float XnpcRelaxCalmSeconds = 4f;
        /// <summary>The enemy range (m) that keeps a body alert; 0 = the NPC's own sight range (its class's SightRange).</summary>
        public static float XnpcRelaxRange = 0f;
        /// <summary>Their jumps fire the player animator's jump trigger (Patches_Sdcs.Patch_JumpAnim).</summary>
        public static bool XnpcBodyJumpAnim = true;
        /// <summary>Their hit colliders are put back on the hittable layer after spawn and rebuilds (Patches_Sdcs.FixColliderLayers).</summary>
        public static bool XnpcBodyColliderLayers = true;
        /// <summary>The XNPC body's Model offset (m): 0 puts its feet on the ground (the player prefab ships -0.08).</summary>
        public static float XnpcModelY = 0f;
        /// <summary>Load the held models of the weapons NPCs can carry now in the background (ScWeaponWarm, round wp).</summary>
        public static bool XnpcWarmWeapons = true;
        // ---- SC's raiders on 0-XNPCCore's body (M10, DESIGN_M10_RAIDERS.md; the user's decisions M10-D1..D9) ----
        /// <summary>SC's hostile classes on XNPC's body (Config/entityclasses.xml, written by Tools/gen_arsenal.py): ScXnpc's second managed set.</summary>
        public static readonly List<string> XnpcRaiderClasses = new List<string>(ScXnpc.DefaultRaiderClasses);
        /// <summary>
        /// Per class tier (T1, T2, T3): the chance a raider leads with its ranged weapon (M10-D1, the old bands' mix). The
        /// rest lead with the melee weapon; every raider carries both (ScLoadout's lead cvar, ScArms' lead rule).
        /// </summary>
        public static readonly float[] RaiderRangedShare = { 0.3f, 0.4f, 0.5f };
        /// <summary>A melee-lead raider takes its ranged weapon when its target stays farther than this (m), above it or
        /// beyond a path's reach for RaiderLeadRangedSeconds - and only while the gun has rounds.</summary>
        public static float RaiderLeadRangedDist = 8f;
        public static float RaiderLeadRangedSeconds = 3f;
        /// <summary>The odds a raider's ranged weapon is a firearm rather than a bow or crossbow (the old FIREARM_SHARE).</summary>
        public static float RaiderFirearmChance = 0.12f;
        /// <summary>The odds a new raider carries only a melee weapon (it then always leads with it).</summary>
        public static float RaiderMeleeOnlyChance = 0.15f;
        /// <summary>A T3 raider's odds of carrying a firearm at all (SurvivorFirearmChanceT3's twin; round t3: 85%).</summary>
        public static float RaiderFirearmChanceT3 = 0.85f;
        /// <summary>Rounds of its gun's basic ammo a raider starts with, in its bag, once (M10-D2: never resupplied). With
        /// Locked &amp; Loaded (M11) only for a gun without magazines: one that takes them gets L&amp;L's tier magazines instead.</summary>
        public static int RaiderSpawnAmmo = 24;
        /// <summary>The same for a bow or crossbow: arrows or bolts.</summary>
        public static int RaiderSpawnArrows = 15;
        /// <summary>XNPC's day-based kill levels on raiders (M10-D3: off - they are stripped as on members).</summary>
        public static bool RaiderXnpcLevels = false;
        /// <summary>With RaiderXnpcLevels on: keep the levels' 0.2 HP/s regeneration too (false zeroes it: raiders heal by items only).</summary>
        public static bool RaiderLevelRegen = false;
        /// <summary>Each of a dead raider's pair weapons goes into its backpack with this chance (M10-D6).</summary>
        public static float RaiderWeaponDropChance = 0.25f;
        /// <summary>
        /// Raiders also get vanilla's move-helper door opener (unlocked doors, opened without a stall, never shut). False
        /// (M10-D5): SCore's own opener alone, which every human NPC already has from its move tasks and which shuts the door
        /// behind them.
        /// </summary>
        public static bool RaiderVanillaDoors = false;
        /// <summary>
        /// Player turrets shoot raiders as enemies (M10 round 3): the powered auto turret with "target zombies" on, the junk
        /// turret and junk sledge with "target enemies" on, as they shot the old EntityEnemy raiders; a motion sensor with
        /// "target zombies" on trips on them. Raiders are EntityNPCs on this body, which turrets and sensors otherwise only
        /// take with "target strangers" on (vanilla's and SCore's rule, which stays either way).
        /// </summary>
        public static bool RaiderTurretsAsEnemies = true;
        /// <summary>The vanilla armor sets raiders roll their clothes from (M10-D7); empty = SurvivorGearSets.</summary>
        public static readonly List<string> RaiderGearSets = new List<string>();
        public static int TraitsPerRecruit = 2;
        public static bool Verbose = false;
        public static readonly List<TraitDef> Traits = new List<TraitDef>();

        private static bool parsed;

        public static bool Parsed { get { return parsed; } }

        /// <summary>Per-world reset (ModEvents.GameStarting).</summary>
        public static void Reset()
        {
            parsed = false;
            MaxMembers = 12;
            MaxFollowers = 2;
            FoodPerMemberDay = 0.5f;
            AmmoPerRangedDay = 0.25f;
            PointsPerBundle[0] = 400; PointsPerBundle[1] = 300; PointsPerBundle[2] = 2000; PointsPerBundle[3] = 1000; PointsPerBundle[4] = 600;
            PointsPerBundle[5] = 200; PointsPerBundle[6] = 300;
            WaterPerMemberDay = 1f;
            LuxuryItems.Clear();
            WellBlocks.Clear();
            DewCollectorBlocks.Clear();
            WellWaterPerLevel = 2;
            DewWaterPerLevel = 1;
            FacilityUpgradeCost = new[] { 4, 8, 12, 16 };
            WellsPlaceLevelOne = true;
            DrinksLuxury = 4; DrinksFood = 1; DrinksMorale = 15; DrinksMinutes = 15f;
            OutpostCisternFloor = -10;
            ValueOverrides.Clear();
            BedBlocks.Clear();
            BedBlocksExclude.Clear();
            BedsBySleepingBag = true;
            ScanRadius = 0;
            LinkedStorageRows = 0;
            FuelItems.Clear();
            FacilityBlocks.Clear();
            PostRadius = 8;
            ResidentRoamZone = true;
            ResidentRoamPauseMin = 10f; ResidentRoamPauseMax = 20f;
            ResidentRoamZoneMargin = 8f;
            HomeKeepLoaded = true;
            HomeKeepLoadedRange = 250f;
            DefendBaseRadius = 22;
            LowMoraleDays = 2;
            MissingDaysToPurge = 30;
            RunRiskBase = 0.15f;
            RunYieldBase = 2;
            MembersOpenOwnerLockedDoors = true;
            MembersOpenHatches = true;
            PhysicalRuns = true; RunWalkMps = 4f; RunnerRunMps = 4.5f; RunnerWalkMps = 1.6f; RunMinDist = 150; RunMaxDist = 700; RunSiteHours = 6f; RunDistressHours = 12f; RunDeadlineGraceDays = 2; RunWaypointSpacing = 25f; RunArriveRadius = 2.5f; RunPartialArrive = 6f; RunArriveSite = 6f; RunGridSize = 64; RunGridFarFromPlayer = 25f; RunOnlineRingMargin = 2; RunOfflineRingMargin = 0; RunOnlineDwellSeconds = 10f; RunOfflineDwellSeconds = 5f; RunFrozenSeconds = 3f; RunNoPopInRange = 50f; RunPopInMaxWait = 20f; RunGateDistance = 56f; RunStuckRepathSeconds = 5f; RunStuckSkipSeconds = 15f; RunSkipMinPlayerDist = 40f; RunSnapRefreshSeconds = 60f; RunSnapMaxBytes = 60000; RunMaterializePerTick = 1;
            SuppliesFavours.Clear();
            PoiFavourRange = 300;
            TraitsPerRecruit = 2;
            Verbose = false;
            Traits.Clear();
            SurvivorLookChance = 0.5f;
            SurvivorGearSets.Clear();
            SurvivorGearPieceChance = 0.8f;
            SurvivorGearAlwaysChest = true;
            SurvivorGearMixChance = 0.3f;
            SurvivorGearQualityMin = 1;
            SurvivorGearQualityMax = 4;
            GearSetTiers.Clear();
            GearSetLastTier.Clear();
            GearTierWeights = DefaultGearTierWeights();
            GearQualityByTier = DefaultGearQuality();
            Outfits.Clear();
            SurvivorOutfitShare = 0.35f;
            RaiderOutfitShare = 0.35f;
            SurvivorBaldChance = 0.15f;
            SurvivorFacialHairChance = 0.4f;
            SurvivorExcludeSkins.Clear();
            SurvivorExcludeEyes.Clear();
            SurvivorExcludeHairs.Clear();
            SurvivorDyedHairColors.Clear();
            SurvivorDyedHairChanceFemale = 0.06f;
            SurvivorDyedHairChanceMale = 0.005f;
            SurvivorBodyPaintChance = 0.01f; RaiderBodyPaintChance = 0.06f;
            SurvivorFacePaintChance = 0.02f; RaiderFacePaintChance = 0.1f;
            SurvivorTattooChance = 0.15f; RaiderTattooChance = 0.35f;
            SurvivorBodyPaints.Clear(); RaiderBodyPaints.Clear();
            SurvivorFacePaints.Clear(); RaiderFacePaints.Clear();
            SurvivorTattoos.Clear(); RaiderTattoos.Clear();
            SurvivorTattooInks.Clear(); RaiderTattooInks.Clear();
            TattooMode = "Blend";
            SurvivorAimPose = true;
            SurvivorAimVisualOnly = true;
            SurvivorAimHoldSeconds = 2f;
            SurvivorSecondWeapon = true;
            SurvivorFirearmChance = 0.12f;
            SurvivorMeleeOnlyChance = 0.15f;
            SurvivorFirearmChanceT3 = 0.85f;
            XnpcClassByGamestage = true;
            SurvivorRangedMainChance = 0.4f;
            XnpcWeapons.Clear();
            XnpcSpawnAmmo = 30;
            XnpcSpawnArrows = 20;
            XnpcResupply = true;
            XnpcResupplyBelow = 10;
            XnpcResupplyAmmo = 30;
            XnpcResupplyArrows = 20;
            XnpcResupplyCooldown = 60f;
            XnpcResupplyPointsMult = 1f;
            Overwhelmed = true;
            OverwhelmedRadius = 12f;
            OverwhelmedMin = 2;
            OverwhelmedMax = 5;
            OverwhelmedStarsPerStep = 2;
            OverwhelmedStrangers = 3;
            OverwhelmedHoldSeconds = 5f;
            OverwhelmedSustainSeconds = 1.5f;
            OverwhelmedGunSeconds = 6f;
            MeleePacing = true;
            MeleePacingRaiders = true;
            MeleeReach = true;
            MeleeReachMargin = 0.4f;
            ShootingSkill = true;
            ShootingRangePerStar = 5f;
            ShootingEngageMult = 2f;
            ShootingEngageMin = 12f;
            ShootingHitRadius = 0.25f;
            ShootingAimChest = true;
            ShootingDefaultStars = 2;
            ParseRaiderShooting("1-2,2-4,4-6");
            DialogLine = true;
            VoiceLines = true;
            MuteVcInTalk = true;
            XnpcSurvivorClasses.Clear();
            XnpcSurvivorClasses.AddRange(ScXnpc.DefaultClasses);
            XnpcBodyGetUp = true;
            XnpcGetUpGuard = true;
            XnpcGetUpTrace = true;
            XnpcRelax = true;
            XnpcRelaxRaiders = true;
            XnpcRelaxHideWeapon = true;
            XnpcRelaxCalmSeconds = 4f;
            XnpcRelaxRange = 0f;
            XnpcBodyJumpAnim = true;
            XnpcBodyColliderLayers = true;
            XnpcModelY = 0f;
            XnpcWarmWeapons = true;
            XnpcRaiderClasses.Clear();
            XnpcRaiderClasses.AddRange(ScXnpc.DefaultRaiderClasses);
            RaiderRangedShare[0] = 0.3f; RaiderRangedShare[1] = 0.4f; RaiderRangedShare[2] = 0.5f;
            SkillFloorByTier[0] = 1; SkillFloorByTier[1] = 2; SkillFloorByTier[2] = 3;
            StandoutSkillChance[0] = 0.04f; StandoutSkillChance[1] = 0.06f; StandoutSkillChance[2] = 0.08f;
            StartSkillCap = 4;
            RaiderLeadRangedDist = 8f;
            RaiderLeadRangedSeconds = 3f;
            RaiderFirearmChance = 0.12f;
            RaiderMeleeOnlyChance = 0.15f;
            RaiderFirearmChanceT3 = 0.85f;
            RaiderSpawnAmmo = 24;
            RaiderSpawnArrows = 15;
            RaiderXnpcLevels = false;
            RaiderLevelRegen = false;
            RaiderWeaponDropChance = 0.25f;
            RaiderVanillaDoors = false;
            RaiderTurretsAsEnemies = true;
            RaiderGearSets.Clear();
            OutpostMinDistance = 150;
            OutpostMax = 4;
            OutpostMembersPerSlot = 4;
            OutpostLeadershipPerSlot = 5;
            OutpostFeeMats = 2;
            OutpostFeeFuel = 1;
            OutpostUpkeep = 0.2f;
            OutpostHaulage = 0.05f;
            OutpostRunDownMult = 0.5f;
            OutpostOnFootMult = 0.75f;
            OutpostPendingDays = 3;
            OutpostDupMult = new[] { 1f, 0.75f, 0.5f };
            OutpostMoralePer = 2;
            OutpostMoraleCap = 6;
            OutpostLostPenalty = 5f;
            OutpostLostDecay = 1f;
            OutpostStaffBonus = 0.5f; OutpostMatchBase = 0.2f; OutpostMatchPerStar = 0.1f;
            OutpostStandInRange = 60; OutpostWalkInChance = 0.08f;
            OutpostL2Mats = 4; OutpostL2Fuel = 2; OutpostL2Days = 3;
            OutpostL3Mats = 8; OutpostL3Fuel = 4; OutpostL3Days = 7;
            OutpostL2Mult = 1.3f; OutpostL3Mult = 1.5f; OutpostUpkeepPerLevel = 0.1f;
            OutpostClinicHeals = 1; OutpostEscortRisk = 0.03f; OutpostEscortMax = 0.06f;
            OutpostMotorPoolMult = 1.15f; OutpostSalvageMult = 0.75f; OutpostCanteenFloor = -10;
            RadioScoutRange = 1500; RadioScoutCount = 3; RadioScoutDays = 3;
            OutpostQuietMult = 0.5f; OutpostPushMult = 1.5f; OutpostQuietRisk = 0.5f; OutpostPushRisk = 1.75f;
            OutpostCheckInDays = 0; OutpostCheckInDecay = 0.1f; OutpostCheckInFloor = 0.5f;
            OutpostDepletion = 0f; OutpostDepletedMult = 0.25f; OutpostDistressChance = 0.3f;
            OutpostWorkshop = true; OutpostWorkshopKeep = 10; RadioBarterRate = 3;
            OutpostBedBonus = 0.05f; OutpostFacilityBonus = 0.1f; OutpostCropBonus = 0.02f;
            OutpostBedMax = 4; OutpostFacilityMax = 3; OutpostCropMax = 10; OutpostInvestCap = 0.5f;
            OutpostDefenseBlocks.Clear();
            OutpostStashAndHaul = false; OutpostHaulBase = 2; OutpostHaulPerLevel = 2; OutpostStashMax = 30;
            HomeStatusHud = true; BaseNoise = true; NoisePerMember = 2f; NoisePerGenerator = 6f; NoisePerFacility = 1f;
            NoiseHeatMult = 1f; NoiseNpcShotMult = 1f; NoiseNpcShotDefault = 0.5f; NoiseHalfLifeHours = 1f; NoiseEventCap = 200f;
            NoiseNoticeable = 15f; NoiseLoud = 30f; NoiseVeryLoud = 50f; AttentionPerNoiseHour = 0.14f; AttentionDecayPerHour = 2f;
            BaseAttacks = true; BaseAttackGapDays = 2f; BaseAttackLeadMinHours = 1f; BaseAttackLeadMaxHours = 2f;
            BaseAttackBase = 6f; BaseAttackPerGamestage = 0.16f; BaseAttackPerMember = 0.05f; BaseAttackMin = 4; BaseAttackMax = 40;
            BaseAttackGameStages = "ScoutGSList"; BaseAttackSpawnMin = 25f; BaseAttackSpawnMax = 50f; BaseAttackSpread = 30f; BaseAttackMinPlayerRange = 30;
            BaseAttackDefendMargin = 10f; BaseAttackHomeMargin = 30f; BaseAttackLeaveRange = 150f; BaseAttackMaxHours = 4f; BaseAttackResumeSeconds = 15f; BaseAttackWinMorale = 4f;
            WatchPosts = true; WatchMaxPosts = 4; WatchShiftHours = 12f; WatchRestHours = 12f; WatchShiftAmmo = 3; WatchSightMult = 1.5f;
            WatchPlaceSeconds = 30f; WatchAttentionCut = 0.15f; WatchAttentionCutMax = 0.45f;
            BaseAttacksAway = true; BaseAttackJoinRange = 100f; BaseAwayHoursBase = 2f; BaseAwayHoursPer10 = 1f; BaseAwayDamageBand = 35;
            BaseAwayAmmoPer = 10f; BaseAwayAmmoOverrun = 1.5f; BaseAwayHurtMin = 1; BaseAwayHurtMax = 2; BaseAwayHurtOverrun = 0.67f;
            BaseAwayDeathChance = 0.08f; BaseAwayDeathHorde = 15f; BaseAwayDeathSkillRef = 6f; BaseAwayDeathSkillStep = 0.125f;
            BaseAwayDeathCap = 0.5f; BaseAwayDeathUnhurt = 0.1f;
            BaseAwayHeldMorale = 2f; BaseAwayDamagedMorale = 5f; BaseAwayOverrunMorale = 12f; BaseSiege = true; BaseSiegePenalty = 15;
            BaseDefenceBase = 20f; BaseDefencePerResident = 4f; BaseDefencePerCombatStar = 1f; BaseDefencePerPost = 10f; BaseDefencePostCap = 30f;
            BaseDefencePerBlock = 1f; BaseDefenceBlockCap = 20f; BaseDefenceNoAmmo = 15f; BaseDefenceMorale = 5f;
            BaseDefenceBigFrom = 20f; BaseDefenceBigPer = 1f; BaseDefenceBigCap = 15f;
            GuardTowerGhost = "glow"; GuardTowerFill = 3; GuardTowerCut = 2;
            OutpostAttacks = false; OutpostGoodNews = true; OutpostGoodNewsChance = 0.04f; OutpostSecBase = 35; OutpostSecPerStaffer = 8; OutpostSecPerCombatStar = 2; OutpostSecPerTier = 4; OutpostSecVisited = 5; OutpostSecNoAmmo = 10; OutpostSecPerDefense = 2; OutpostSecDefenseCap = 30; OutpostSecBonusFade = 1f; OutpostSecWinBonus = 10f; OutpostSecBonusMax = 30f; OutpostAttackChance = 0.08f; OutpostAttackHours = 10; OutpostAttackBase = 3f; OutpostAttackPerGamestage = 0.04f; OutpostAttackMax = 16; OutpostAttendedBonus = 20; OutpostDamageBand = 35; OutpostDamagedDays = 2; OutpostRepairMats = 2; OutpostSiegePenalty = 15; OutpostWinMorale = 4f; OutpostNeglectDays = 10; OutpostRaiderGamestage = 30; OutpostRaiderChance = 0.03f; OutpostDemandAmmo = 2; OutpostDemandFood = 2;
            Enclaves = true; EnclavesPerKm2 = 0.3f; EnclaveMin = 4; EnclaveMax = 36; EnclaveSpacingMin = 500; EnclaveSpacingFactor = 0.55f; EnclaveSpacingFloor = 300;
            EnclaveHostileShare = 0.25f; EnclaveHostileMinStart = 1200; EnclaveStartRing = 2; EnclaveStartRingMin = 400; EnclaveStartRingMax = 1500; EnclaveStartRingSpacing = 600;
            EnclaveTierMin = 1; EnclaveTierMax = 4; EnclaveSizeMin = 20; EnclaveSizeMax = 80; EnclaveTraderDistance = 300; EnclaveSpawnDistance = 300; EnclaveEdgeDistance = 300;
            EnclaveKindWeights.Clear(); EnclavePoiExcludes.Clear(); EnclaveLateSeed = true; EnclaveClaimBuffer = 150; EnclaveUnvisitedWeight = 3f; EnclaveRelocateRange = 1000;
            EnclaveDiscoverRange = 150; EnclaveRadioRevealDays = 3; EnclaveRadioRange = 2000;
            EnclaveGroupMin = 3; EnclaveGroupMax = 5; EnclaveRaiderGroupMin = 3; EnclaveRaiderGroupMax = 6; EnclaveSpawnRange = 150; EnclaveDespawnMargin = 50;
            EnclaveNoPopRange = 50; EnclaveSpawnPerTick = 1; EnclaveHoldChunks = true; EnclaveDailyRestock = true; EnclaveLeashMargin = 8; EnclaveDefendRadius = 22;
            EnclaveStandingMin = -30; EnclaveWarnPenalty = 10; EnclaveWarnWindow = 120f; EnclaveHitDebounce = 1.5f; EnclaveThreatBundlesMin = 2; EnclaveThreatBundlesMax = 4;
            EnclaveThreatGrace = 10f; EnclaveContainerLock = true; EnclaveWildSpawnMargin = 40; EnclaveKindRows.Clear();
            EnclaveTraderIdBase = 200; EnclaveBuyNeutral = 1f; EnclaveBuyFriendly = 1f; EnclaveBuyAllied = 0.8f; EnclaveSellNeutral = 1f; EnclaveSellFriendly = 1.2f;
            EnclaveSellAllied = 1.35f; EnclaveEnlistCostNeutral = 500; EnclaveEnlistCostFriendly = 250; EnclaveEnlistCostAllied = 0; EnclaveEnlistDays = 2;
            EnclaveEnlistDeathPenalty = 5; EnclaveRecruitMinLeft = 2; EnclaveRecruitCooldownDays = 7; EnclaveRecruitStanding = 10;
            EnclaveRequestChanceNeutral = 0.35f; EnclaveRequestChanceFriendly = 0.45f; EnclaveRequestChanceAllied = 0.2f; EnclaveRequestMaxOpen = 3;
            EnclaveRequestCooldownDays = 1; EnclaveOfferLapseDays = 3; EnclaveDueDaysItems = 4; EnclaveGeneratorFromDay = 10;
            EnclaveRequestKindWeights.Clear(); EnclaveRequestSizeWeights.Clear(); EnclaveGainResource = 10; EnclaveGainGenerator = 15;
            SetInts(EnclaveGunTierPoints, 3, 6, 10, 15, 18, 22); EnclaveGunQualityBase = 0.75f; EnclaveGunQualityStep = 0.1f; EnclaveGunPointsMin = 3;
            EnclaveGunPointsMax = 30; SetInts(EnclaveGunValueBands, 400, 1200, 3500, 7000, 12000); SetInts(EnclaveGunWorthBands, 5, 9, 14, 20);
            SetInts(EnclaveLossDecline, 5, 5, 12); SetInts(EnclaveLossFail, 8, 8, 15); SetInts(EnclaveLossLapse, 0, 3, 15);
            EnclaveDueDaysSearch = 3; EnclaveMissingMin = 200; EnclaveMissingMax = 800; EnclaveMissingAlive = 0.8f; EnclaveMissingAliveFade = 0.2f;
            EnclaveMissingAliveMin = 0.1f; EnclaveInfestRange = 300; EnclaveSiegeChance = 0.15f; EnclaveSiegeHourMin = 8; EnclaveSiegeHourMax = 20;
            EnclaveSiegeRange = 2000; EnclaveSiegeMinutes = 15f; EnclaveSiegeArriveRange = 70; EnclaveSiegeMaxHours = 3f; EnclaveSiegeBase = 6f;
            EnclaveSiegePerGS = 0.12f; EnclaveSiegeMin = 4; EnclaveSiegeMax = 30; EnclaveSiegeSpawnMin = 25; EnclaveSiegeSpawnMax = 50; EnclaveSiegeSpread = 90f;
            EnclaveSiegeMinPlayerRange = 30; EnclaveSiegeNoPopRange = 50; EnclaveSiegeResumeSeconds = 15f; EnclaveSiegeLeaveRoll = 0.05f;
            EnclaveGainMissingAlive = 15; EnclaveGainMissingDead = 8; EnclaveGainInfestation = 15; EnclaveGainSiege = 25;
            EnclaveBenefitBundles = 1; EnclaveBenefitMorale = 5; EnclaveBenefitBeds = 3; EnclaveBenefitClubXp = 2f; EnclaveBenefitTrainingXp = 1.5f;
            EnclaveBenefitXpCap = 3f; EnclaveWatchRange = 500; EnclaveWatchSpread = 0.5f; EnclaveAllyMorale = 2; EnclaveAllyMoraleCap = 6;
            EnclaveLostMorale = 5; EnclaveRefillDays = 5; EnclaveAllyRevealDays = 7;
            Infestations = true; InfestDailyChance = 0.4f; InfestPerKm2 = 0.2f; InfestMin = 2; InfestMax = 30; InfestSpreadDays = 3; InfestSpreadChance = 0.3f;
            InfestSpreadRange = 300; InfestTraderDistance = 150; InfestMinDist = 300; InfestMaxDist = 2500; InfestOutsideWeight = 0.1f; InfestTierByDay.Clear();
            InfestMultiplier = 2f; InfestMultiplierHighTier = 1.5f; InfestHighTier = 4; InfestFeralShare = 0.35f; InfestSpecial = true; InfestSpecialRadiatedBelowGS = 50;
            InfestDiscoverRange = 150; InfestRadioRange = 2000; InfestPlayerMargin = 50; InfestRearmPlayerRange = 100;
            InfestHomeRange = 500; InfestHomeMoraleCap = 15; InfestHomeMoraleNests = 5; InfestHomeAttention = 1f; InfestHomeAttentionCap = 5;
            InfestHomeKnown = true; InfestHomeMoraleBands.Clear();
            SleeperCapFix = true; SleeperCapRetryTicks = 1000; SleeperCapPause = true; SleeperCapPauseMaxTicks = 3000;
            ScPoiTypes.LoadDefaults();
            Defaults();
        }

        private static void Defaults()
        {
            BedBlocks.AddRange(new[] { "bedroll", "bed02", "bunkBed", "bedMade", "bedMessy", "mattressFlatThin", "hospitalBed" });   // name PREFIXES (3.2 beds are bed02Blue, bunkBedMadeWhite, bedMadeNoFrameGrey ...)
            BedBlocksExclude.AddRange(new[] { "bunkBedFrame", "mattressCurled" });
            FuelItems.Add("ammoGasCan");
            LuxuryItems.AddRange(new[] { "drinkJarBeer", "drinkJarGrandpasMoonshine", "drinkJarGrandpasAwesomeSauce", "drinkJarGrandpasLearningElixir" });
            WellBlocks.AddRange(new[] { "OakravenWaterFiller1", "OakravenWaterFiller2", "OakravenWaterFiller3", "OakravenWaterFiller4", "OakravenWaterFiller5" });
            DewCollectorBlocks.Add("cntDewCollector");
            FacilityBlocks.AddRange(new[] { "campfire", "workbench", "forge", "chemistryStation", "cementMixer" });
            SuppliesFavours.Add(new KeyValuePair<Resource, int>(Resource.Food, 15));
            SuppliesFavours.Add(new KeyValuePair<Resource, int>(Resource.Meds, 5));
            SuppliesFavours.Add(new KeyValuePair<Resource, int>(Resource.Ammo, 60));
            OutpostDefenseBlocks.AddRange(new[] { "trapSpikes", "barbedWire", "barbedFence", "bladeTrap", "dartTrap", "electricfencepost",
                                                  "autoTurret", "shotgunTurret", "mineCandyTin", "mineCookingPot", "mineHubcap", "mineAirFilter" });
            SurvivorGearSets.AddRange(new[] { "Primitive", "Athletic", "Enforcer", "Lumberjack", "Preacher", "Rogue", "Assassin", "Biker", "Commando", "Farmer", "Ranger", "Scavenger", "Miner", "Nerd", "Nomad", "Raider" });
            foreach (string s in new[] { "Primitive", "Athletic", "Enforcer", "Lumberjack", "Preacher", "Rogue" }) GearSetTiers[s] = 0;
            foreach (string s in new[] { "Assassin", "Biker", "Commando", "Farmer", "Ranger", "Scavenger" }) GearSetTiers[s] = 1;
            foreach (string s in new[] { "Miner", "Nerd", "Nomad", "Raider" }) GearSetTiers[s] = 2;
            GearSetLastTier["Primitive"] = 0;
            // M10-D7: raiders dress rougher than survivors (the design's suggested list, accepted 2026-10-01)
            RaiderGearSets.AddRange(new[] { "Raider", "Biker", "Enforcer", "Assassin", "Rogue", "Commando", "Nomad", "Scavenger", "Primitive" });
            // the user's picks (2026-10-07): Vanity Vault's tinted skins and all of its eyes; vanilla's dyed colours made rare
            SurvivorExcludeSkins.AddRange(new[] { "Obsidian", "Ash", "Mutant Green", "Demon Red", "Orchid", "Brimstone", "Cobalt", "Amethyst", "Ember" });
            SurvivorExcludeEyes.AddRange(new[] { "Sonja_RedEyes_01", "Sonja_Underworld", "Sonja_YellowEyes_01", "Sonja_Bullseye", "Sonja_Smiley",
                                                 "Sonja_Blackeyes", "Sonja_Eyes_AllScaryBlack", "Sonja_Eyes_Emission_Red", "Sonja_Eyes_TEST" });
            SurvivorDyedHairColors.AddRange(new[] { "11 Yellow", "12 Orange", "13 Redder", "14 Redder1", "15 Pink", "16 Pink 1", "17 Blue",
                                                    "18 Blue 1", "19 Blue 2", "20 Purple", "21 Purple1", "22 Purple 2", "23 Green", "24 Green 1" });
            // the user's paint and tattoo picks (2026-10-07, SurvivorPaints_Choices.md): Vanity Vault's names
            SurvivorBodyPaints.AddRange(new[] { "Sonja_Clean_Vanilla_Full_F", "Sonja_Broken_Full_M:Sonja_Broken" });
            RaiderBodyPaints.AddRange(new[] { "Sonja_Broken_Full_M:Sonja_Broken", "Sonja_Harley_Full_F:Sonja_Harley",
                                              "Sonja_Skeletton_Full_M:Sonja_Skeletton", "Sonja_Skeletton_Full_F:Sonja_Skeletton" });
            SurvivorFacePaints.AddRange(new[] { "Sonja_Broken", "Sonja_Furiosa" });
            RaiderFacePaints.AddRange(new[] { "Sonja_Broken", "Sonja_Furiosa", "Sonja_Tribal_01", "Sonja_Braveheart", "Sonja_Voodoo",
                                              "Sonja_Skeletton", "Sonja_Catrina", "Sonja_Clown", "Sonja_Joker", "Sonja_Harley" });
            SurvivorTattoos.AddRange(TattooShapes);
            RaiderTattoos.AddRange(TattooShapes);
            RaiderTattoos.AddRange(new[] { "Oni 01", "Kiss 01", "Harley Tattoo", "Harley Eyes Only" });
            SurvivorTattooInks.AddRange(new[] { "Black Orchid", "Black Orchid", "Black Orchid", "Dark Truffle", "Abyssal", "Royal Lapis", "Venom", "Cendre" });
            RaiderTattooInks.AddRange(new[] { "Black Orchid", "Black Orchid", "Dark Truffle", "Abyssal", "Venom", "Rouge Noir", "Oxblood Wine", "Chili Red" });
            ParseKindWeights(DefaultEnclaveKindWeights);
            EnclavePoiExcludes.AddRange(SplitList(DefaultEnclavePoiExcludes));
            ParseKindRows(DefaultEnclaveKindRows);
            ParseNamedWeights("EnclaveRequestKindWeights", DefaultEnclaveRequestKindWeights, ScEncRequests.RollKinds, EnclaveRequestKindWeights);
            ParseNamedWeights("EnclaveRequestSizeWeights", DefaultEnclaveRequestSizeWeights, ScEncRequests.Sizes, EnclaveRequestSizeWeights);
            ParseTierByDay(DefaultInfestTierByDay);
            ParseHomeBands(DefaultInfestHomeMoraleBands);
        }

        /// <summary>A fixed-size array's defaults (Reset): as many values as it holds.</summary>
        private static void SetInts(int[] into, params int[] values)
        {
            Array.Copy(values, into, Math.Min(values.Length, into.Length));
        }

        /// <summary>The tattoo shapes the user picked for survivors (raiders get these and four more).</summary>
        private static readonly string[] TattooShapes = { "Arm Dragon 01", "Arm Flowers 01", "Hand Cat 01", "Hand Cat 02", "Hand Hearts 14",
            "Snake Between Flowers", "Leg Color 09", "Leg Color 11", "Leg Color 13", "Leg Color 14", "Back Dragon 01", "Back Dragon 02",
            "Maori 01", "Maori 02", "Maori 03", "Star 01", "Tribal 01 - Face", "Tribal 01 - Body", "Tribal 01 - Full", "Yakuza" };

        /// <summary>A name is on an exclude list: exact (case-insensitive), or a prefix when the entry ends in '*' (a lone '*' is everything).</summary>
        public static bool Listed(List<string> list, string name)
        {
            if (list == null || string.IsNullOrEmpty(name)) return false;
            for (int i = 0; i < list.Count; i++)
            {
                string e = list[i];
                if (string.IsNullOrEmpty(e)) continue;
                if (e[e.Length - 1] == '*')
                {
                    if (name.StartsWith(e.Substring(0, e.Length - 1), StringComparison.OrdinalIgnoreCase)) return true;
                }
                else if (string.Equals(e, name, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>RaiderRangedShare for a class tier (0..2 = T1..T3, ScXnpc.TierOf).</summary>
        public static float RaiderRangedShareFor(int tier)
        {
            return RaiderRangedShare[Math.Max(0, Math.Min(RaiderRangedShare.Length - 1, tier))];
        }

        /// <summary>The armor sets a raider's clothes are rolled from: RaiderGearSets, or SurvivorGearSets when that is empty.</summary>
        public static List<string> GearSetsFor(bool raider)
        {
            return raider && RaiderGearSets.Count > 0 ? RaiderGearSets : SurvivorGearSets;
        }

        private static float[][] DefaultGearTierWeights()
        {
            return new[] { new[] { 1f, 0.5f, 0.25f }, new[] { 0.6f, 1f, 0.6f }, new[] { 0.35f, 0.7f, 1f } };
        }

        private static int[][] DefaultGearQuality()
        {
            return new[] { new[] { 1, 2 }, new[] { 2, 4 }, new[] { 3, 5 } };
        }

        /// <summary>A vanilla armor set's weight for an NPC of this effective band (1 when the band or the set's class is unknown).</summary>
        public static float GearSetWeight(string set, int band)
        {
            int c;
            if (band >= 0 && !string.IsNullOrEmpty(set) && GearSetLastTier.TryGetValue(set, out c) && band > c) return 0f;
            if (band < 0 || string.IsNullOrEmpty(set) || !GearSetTiers.TryGetValue(set, out c)) return 1f;
            float[] row = GearTierWeights[Math.Min(band, GearTierWeights.Length - 1)];
            return row[Math.Max(0, Math.Min(row.Length - 1, c))];
        }

        /// <summary>The armor quality range for this effective band; SurvivorGearQualityMin..Max when it is unknown (-1).</summary>
        public static void GearQuality(int band, out int min, out int max)
        {
            if (band < 0)
            {
                min = Math.Max(1, Math.Min(SurvivorGearQualityMin, SurvivorGearQualityMax));
                max = Math.Max(min, SurvivorGearQualityMax);
                return;
            }
            int[] q = GearQualityByTier[Math.Min(band, GearQualityByTier.Length - 1)];
            min = q[0];
            max = q[1];
        }

        static ScConfig()
        {
            Defaults();
        }

        /// <summary>Parses the config block. Safe to call repeatedly; only the first call per world does work.</summary>
        public static void EnsureParsed()
        {
            if (parsed) return;
            try
            {
                Block b = Block.GetBlockByName(BlockName, true);
                if (b == null || b.Properties == null)
                {
                    ScLog.Once("config-missing", "block " + BlockName + " not found - using defaults (is Config/blocks.xml applying?)");
                    parsed = true;
                    return;
                }
                DynamicProperties p = b.Properties;
                ParseInt(p, "MaxMembers", ref MaxMembers, 1, 64);
                ParseInt(p, "MaxFollowers", ref MaxFollowers, 0, 8);
                ParseFloat(p, "FoodPerMemberDay", ref FoodPerMemberDay, 0f, 10f);
                ParseFloat(p, "AmmoPerRangedDay", ref AmmoPerRangedDay, 0f, 10f);
                for (int i = 0; i < ScNames.ResourceCount; i++)
                {
                    int v = PointsPerBundle[i];
                    ParseInt(p, "PointsPerBundle" + (Resource)i, ref v, 1, 1000000);
                    PointsPerBundle[i] = v;
                }
                ParseFloat(p, "WaterPerMemberDay", ref WaterPerMemberDay, 0f, 10f);
                ParseInt(p, "WellWaterPerLevel", ref WellWaterPerLevel, 0, 100);
                ParseInt(p, "DewWaterPerLevel", ref DewWaterPerLevel, 0, 100);
                bool wellsOne = WellsPlaceLevelOne;
                if (p.Values.ContainsKey("WellsPlaceLevelOne")) p.ParseBool("WellsPlaceLevelOne", ref wellsOne);
                WellsPlaceLevelOne = wellsOne;
                ParseInt(p, "DrinksLuxury", ref DrinksLuxury, 0, 1000);
                ParseInt(p, "DrinksFood", ref DrinksFood, 0, 1000);
                ParseInt(p, "DrinksMorale", ref DrinksMorale, 0, 100);
                ParseFloat(p, "DrinksMinutes", ref DrinksMinutes, 0.1f, 1440f);
                ParseInt(p, "OutpostCisternFloor", ref OutpostCisternFloor, -100, 0);
                ParseInt(p, "PostRadius", ref PostRadius, 2, 40);
                bool roamZone = ResidentRoamZone;
                if (p.Values.ContainsKey("ResidentRoamZone")) p.ParseBool("ResidentRoamZone", ref roamZone);
                ResidentRoamZone = roamZone;
                ParseFloat(p, "ResidentRoamPauseMin", ref ResidentRoamPauseMin, 0f, 600f);
                ParseFloat(p, "ResidentRoamPauseMax", ref ResidentRoamPauseMax, 0f, 600f);
                if (ResidentRoamPauseMax < ResidentRoamPauseMin) ResidentRoamPauseMax = ResidentRoamPauseMin;
                ParseFloat(p, "ResidentRoamZoneMargin", ref ResidentRoamZoneMargin, 0f, 100f);
                bool keepLoaded = HomeKeepLoaded;
                if (p.Values.ContainsKey("HomeKeepLoaded")) p.ParseBool("HomeKeepLoaded", ref keepLoaded);
                HomeKeepLoaded = keepLoaded;
                ParseFloat(p, "HomeKeepLoadedRange", ref HomeKeepLoadedRange, 0f, 2000f);
                ParseInt(p, "LowMoraleDays", ref LowMoraleDays, 1, 30);
                ParseInt(p, "MissingDaysToPurge", ref MissingDaysToPurge, 1, 1000);
                ParseFloat(p, "RunRiskBase", ref RunRiskBase, 0f, 1f);
                ParseInt(p, "RunYieldBase", ref RunYieldBase, 0, 50);
                ParseInt(p, "PoiFavourRange", ref PoiFavourRange, 20, 2000);
                ParseInt(p, "TraitsPerRecruit", ref TraitsPerRecruit, 0, 5);
                ParseFloat(p, "MeleePerStar", ref MeleePerStar, 0f, 2f);
                ParseFloat(p, "RangedPerStar", ref RangedPerStar, 0f, 2f);
                ParseFloat(p, "SpeedPerStar", ref SpeedPerStar, 0f, 1f);
                ParseFloat(p, "HealthPerStar", ref HealthPerStar, 0f, 2f);
                ParseFloat(p, "CookFoodPerStar", ref CookFoodPerStar, 0f, 0.5f);
                ParseFloat(p, "CropFoodPerStar", ref CropFoodPerStar, 0f, 1f);
                ParseFloat(p, "MedicPerStar", ref MedicPerStar, 0f, 5f);
                ParseFloat(p, "PackBonusPerStar", ref PackBonusPerStar, 0f, 0.4f);
                ParseInt(p, "ScanRadius", ref ScanRadius, 0, 1000);
                ParseInt(p, "FollowXpSeconds", ref FollowXpSeconds, 5, 3600);
                ParseInt(p, "DailyWitsXp", ref DailyWitsXp, 0, 100);
                ParseInt(p, "StartSkillCap", ref StartSkillCap, 1, 7);
                string tierList;
                if (p.Values.TryGetValue("SkillFloorByTier", out tierList) && !string.IsNullOrEmpty(tierList)) ParseTierInts("SkillFloorByTier", tierList, SkillFloorByTier, 1, 7);
                if (p.Values.TryGetValue("StandoutSkillChance", out tierList) && !string.IsNullOrEmpty(tierList)) ParseTierFloats("StandoutSkillChance", tierList, StandoutSkillChance, 0f, 1f);
                ParseInt(p, "StuckSeconds", ref StuckSeconds, 3, 600);
                ParseInt(p, "DefendBaseRadius", ref DefendBaseRadius, 0, 23);
                ParseFloat(p, "DeathPenalty", ref DeathPenalty, 0f, 100f);
                ParseFloat(p, "DeathDecayPerDay", ref DeathDecayPerDay, 0f, 100f);
                ParseInt(p, "LowMoraleThreshold", ref LowMoraleThreshold, 0, 100);
                ParseInt(p, "HighMoraleThreshold", ref HighMoraleThreshold, 0, 100);
                ParseInt(p, "RunDays", ref RunDays, 1, 30);
                {
                    string bv;
                    if (p.Values.TryGetValue("PhysicalRuns", out bv) && !string.IsNullOrEmpty(bv)) PhysicalRuns = StringParsers.ParseBool(bv);
                    if (p.Values.TryGetValue("MembersOpenOwnerLockedDoors", out bv) && !string.IsNullOrEmpty(bv)) MembersOpenOwnerLockedDoors = StringParsers.ParseBool(bv);
                    if (p.Values.TryGetValue("MembersOpenHatches", out bv) && !string.IsNullOrEmpty(bv)) MembersOpenHatches = StringParsers.ParseBool(bv);
                }
                ParseFloat(p, "RunWalkMps", ref RunWalkMps, 0.3f, 6.0f);
                ParseFloat(p, "RunnerRunMps", ref RunnerRunMps, 1.0f, 6.0f);
                ParseFloat(p, "RunnerWalkMps", ref RunnerWalkMps, 0.5f, 4.0f);
                ParseInt(p, "RunMinDist", ref RunMinDist, 20, 5000);
                ParseInt(p, "RunMaxDist", ref RunMaxDist, 50, 8000);
                ParseFloat(p, "RunSiteHours", ref RunSiteHours, 0.25f, 48.0f);
                ParseFloat(p, "RunDistressHours", ref RunDistressHours, 1.0f, 72.0f);
                ParseInt(p, "RunDeadlineGraceDays", ref RunDeadlineGraceDays, 1, 30);
                ParseFloat(p, "RunWaypointSpacing", ref RunWaypointSpacing, 8.0f, 40.0f);
                ParseFloat(p, "RunArriveRadius", ref RunArriveRadius, 1.0f, 8.0f);
                ParseFloat(p, "RunPartialArrive", ref RunPartialArrive, 2.0f, 20.0f);
                ParseFloat(p, "RunArriveSite", ref RunArriveSite, 2.0f, 30.0f);
                ParseInt(p, "RunGridSize", ref RunGridSize, 32, 128);
                ParseFloat(p, "RunGridFarFromPlayer", ref RunGridFarFromPlayer, 0.0f, 200.0f);
                ParseInt(p, "RunOnlineRingMargin", ref RunOnlineRingMargin, 1, 4);
                ParseInt(p, "RunOfflineRingMargin", ref RunOfflineRingMargin, 0, 3);
                ParseFloat(p, "RunOnlineDwellSeconds", ref RunOnlineDwellSeconds, 0.0f, 120.0f);
                ParseFloat(p, "RunOfflineDwellSeconds", ref RunOfflineDwellSeconds, 0.0f, 120.0f);
                ParseFloat(p, "RunFrozenSeconds", ref RunFrozenSeconds, 1.0f, 60.0f);
                ParseFloat(p, "RunNoPopInRange", ref RunNoPopInRange, 0.0f, 120.0f);
                ParseFloat(p, "RunPopInMaxWait", ref RunPopInMaxWait, 0.0f, 300.0f);
                ParseFloat(p, "RunGateDistance", ref RunGateDistance, 40.0f, 120.0f);
                ParseFloat(p, "RunStuckRepathSeconds", ref RunStuckRepathSeconds, 1.0f, 60.0f);
                ParseFloat(p, "RunStuckSkipSeconds", ref RunStuckSkipSeconds, 3.0f, 300.0f);
                ParseFloat(p, "RunSkipMinPlayerDist", ref RunSkipMinPlayerDist, 0.0f, 200.0f);
                ParseFloat(p, "RunSnapRefreshSeconds", ref RunSnapRefreshSeconds, 10.0f, 600.0f);
                ParseInt(p, "RunSnapMaxBytes", ref RunSnapMaxBytes, 4000, 60000);
                ParseInt(p, "RunMaterializePerTick", ref RunMaterializePerTick, 1, 8);
                ParseInt(p, "MaxRunners", ref MaxRunners, 0, 64);
                ParseFloat(p, "RunYieldPerWitsStar", ref RunYieldPerWitsStar, 0f, 2f);
                ParseFloat(p, "RunSpecialistPerStar", ref RunSpecialistPerStar, 0f, 2f);
                ParseFloat(p, "RunRiskPerCombatStar", ref RunRiskPerCombatStar, 0f, 0.5f);
                ParseFloat(p, "RunDeathShare", ref RunDeathShare, 0f, 1f);
                ParseInt(p, "RunWitsXp", ref RunWitsXp, 0, 500);
                ParseInt(p, "LinkedStorageRows", ref LinkedStorageRows, 0, 1000);
                bool bySleepingBag = BedsBySleepingBag;
                if (p.Values.ContainsKey("BedsBySleepingBag")) p.ParseBool("BedsBySleepingBag", ref bySleepingBag);
                BedsBySleepingBag = bySleepingBag;
                bool radioOnly = RadioOnlyAirdrops;
                if (p.Values.ContainsKey("RadioOnlyAirdrops")) p.ParseBool("RadioOnlyAirdrops", ref radioOnly);
                RadioOnlyAirdrops = radioOnly;
                ParseInt(p, "RadioCallDays", ref RadioCallDays, 0, 100);
                ParseInt(p, "RadioContactDays", ref RadioContactDays, 1, 365);
                ParseInt(p, "RadioMaxContacts", ref RadioMaxContacts, 1, 20);
                ParseInt(p, "RadioContactMinDist", ref RadioContactMinDist, 50, 5000);
                ParseInt(p, "RadioContactMaxDist", ref RadioContactMaxDist, 100, 8000);
                ParseFloat(p, "RadioDistressChance", ref RadioDistressChance, 0f, 1f);
                ParseInt(p, "RescueTriggerRange", ref RescueTriggerRange, 20, 300);
                ParseFloat(p, "DistressZombiesBase", ref DistressZombiesBase, 0f, 50f);
                ParseFloat(p, "DistressZombiesPerGamestage", ref DistressZombiesPerGamestage, 0f, 2f);
                ParseFloat(p, "DistressZombiesPerCombatStar", ref DistressZombiesPerCombatStar, 0f, 10f);
                ParseInt(p, "DistressZombiesMax", ref DistressZombiesMax, 1, 100);
                string zg; if (p.Values.TryGetValue("DistressZombieGroup", out zg) && !string.IsNullOrEmpty(zg)) DistressZombieGroup = zg.Trim();
                ParseFloat(p, "RescueBonus", ref RescueBonus, 0.5f, 3f);
                ParseInt(p, "OutpostMinDistance", ref OutpostMinDistance, 0, 5000);
                ParseInt(p, "OutpostMax", ref OutpostMax, 0, 16);
                ParseInt(p, "OutpostMembersPerSlot", ref OutpostMembersPerSlot, 0, 100);
                ParseInt(p, "OutpostLeadershipPerSlot", ref OutpostLeadershipPerSlot, 0, 100);
                ParseInt(p, "OutpostFeeMats", ref OutpostFeeMats, 0, 1000);
                ParseInt(p, "OutpostFeeFuel", ref OutpostFeeFuel, 0, 1000);
                ParseFloat(p, "OutpostUpkeep", ref OutpostUpkeep, 0f, 100f);
                ParseFloat(p, "OutpostHaulage", ref OutpostHaulage, 0f, 10f);
                ParseFloat(p, "OutpostRunDownMult", ref OutpostRunDownMult, 0f, 1f);
                ParseFloat(p, "OutpostOnFootMult", ref OutpostOnFootMult, 0f, 1f);
                ParseInt(p, "OutpostPendingDays", ref OutpostPendingDays, 0, 60);
                {
                    string dup;
                    if (p.Values.TryGetValue("OutpostDupMult", out dup) && !string.IsNullOrEmpty(dup))
                    {
                        var list = new List<float>();
                        foreach (string part in dup.Split(','))
                        {
                            float f;
                            if (float.TryParse(part.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out f)) list.Add(Math.Max(0f, f));
                        }
                        if (list.Count > 0) OutpostDupMult = list.ToArray();
                    }
                }
                ParseInt(p, "OutpostMoralePer", ref OutpostMoralePer, 0, 50);
                ParseInt(p, "OutpostMoraleCap", ref OutpostMoraleCap, 0, 100);
                ParseFloat(p, "OutpostLostPenalty", ref OutpostLostPenalty, 0f, 100f);
                ParseFloat(p, "OutpostLostDecay", ref OutpostLostDecay, 0f, 100f);
                ParseFloat(p, "OutpostStaffBonus", ref OutpostStaffBonus, 0f, 10f);
                ParseFloat(p, "OutpostMatchBase", ref OutpostMatchBase, 0f, 10f);
                ParseFloat(p, "OutpostMatchPerStar", ref OutpostMatchPerStar, 0f, 10f);
                ParseInt(p, "OutpostStandInRange", ref OutpostStandInRange, 10, 500);
                ParseFloat(p, "OutpostWalkInChance", ref OutpostWalkInChance, 0f, 1f);
                ParseInt(p, "OutpostL2Mats", ref OutpostL2Mats, 0, 1000);
                ParseInt(p, "OutpostL2Fuel", ref OutpostL2Fuel, 0, 1000);
                ParseInt(p, "OutpostL2Days", ref OutpostL2Days, 0, 365);
                ParseInt(p, "OutpostL3Mats", ref OutpostL3Mats, 0, 1000);
                ParseInt(p, "OutpostL3Fuel", ref OutpostL3Fuel, 0, 1000);
                ParseInt(p, "OutpostL3Days", ref OutpostL3Days, 0, 365);
                ParseFloat(p, "OutpostL2Mult", ref OutpostL2Mult, 1f, 10f);
                ParseFloat(p, "OutpostL3Mult", ref OutpostL3Mult, 1f, 10f);
                ParseFloat(p, "OutpostUpkeepPerLevel", ref OutpostUpkeepPerLevel, 0f, 10f);
                ParseInt(p, "OutpostClinicHeals", ref OutpostClinicHeals, 0, 20);
                ParseFloat(p, "OutpostEscortRisk", ref OutpostEscortRisk, 0f, 1f);
                ParseFloat(p, "OutpostEscortMax", ref OutpostEscortMax, 0f, 1f);
                ParseFloat(p, "OutpostMotorPoolMult", ref OutpostMotorPoolMult, 1f, 10f);
                ParseFloat(p, "OutpostSalvageMult", ref OutpostSalvageMult, 0f, 1f);
                ParseInt(p, "OutpostCanteenFloor", ref OutpostCanteenFloor, -100, 0);
                ParseInt(p, "RadioScoutRange", ref RadioScoutRange, 100, 10000);
                ParseInt(p, "RadioScoutCount", ref RadioScoutCount, 1, 10);
                ParseInt(p, "RadioScoutDays", ref RadioScoutDays, 1, 60);
                ParseFloat(p, "OutpostQuietMult", ref OutpostQuietMult, 0f, 1f);
                ParseFloat(p, "OutpostPushMult", ref OutpostPushMult, 1f, 5f);
                ParseFloat(p, "OutpostQuietRisk", ref OutpostQuietRisk, 0f, 1f);
                ParseFloat(p, "OutpostPushRisk", ref OutpostPushRisk, 1f, 5f);
                ParseInt(p, "OutpostCheckInDays", ref OutpostCheckInDays, 0, 365);
                ParseFloat(p, "OutpostCheckInDecay", ref OutpostCheckInDecay, 0f, 1f);
                ParseFloat(p, "OutpostCheckInFloor", ref OutpostCheckInFloor, 0f, 1f);
                ParseFloat(p, "OutpostDepletion", ref OutpostDepletion, 0f, 100000f);
                ParseFloat(p, "OutpostDepletedMult", ref OutpostDepletedMult, 0f, 1f);
                ParseFloat(p, "OutpostDistressChance", ref OutpostDistressChance, 0f, 1f);
                {
                    string ws;
                    if (p.Values.TryGetValue("OutpostWorkshop", out ws) && !string.IsNullOrEmpty(ws)) OutpostWorkshop = StringParsers.ParseBool(ws);
                }
                ParseInt(p, "OutpostWorkshopKeep", ref OutpostWorkshopKeep, 0, 1000);
                ParseInt(p, "RadioBarterRate", ref RadioBarterRate, 1, 20);
                ParseFloat(p, "OutpostBedBonus", ref OutpostBedBonus, 0f, 1f);
                ParseFloat(p, "OutpostFacilityBonus", ref OutpostFacilityBonus, 0f, 1f);
                ParseFloat(p, "OutpostCropBonus", ref OutpostCropBonus, 0f, 1f);
                ParseInt(p, "OutpostBedMax", ref OutpostBedMax, 0, 100);
                ParseInt(p, "OutpostFacilityMax", ref OutpostFacilityMax, 0, 100);
                ParseInt(p, "OutpostCropMax", ref OutpostCropMax, 0, 1000);
                ParseFloat(p, "OutpostInvestCap", ref OutpostInvestCap, 0f, 5f);
                {
                    string def;
                    if (p.Values.TryGetValue("OutpostDefenseBlocks", out def) && def != null)
                    {
                        OutpostDefenseBlocks.Clear();
                        foreach (string part in def.Split(',')) if (part.Trim().Length > 0) OutpostDefenseBlocks.Add(part.Trim());
                    }
                    string sh;
                    if (p.Values.TryGetValue("OutpostStashAndHaul", out sh) && !string.IsNullOrEmpty(sh)) OutpostStashAndHaul = StringParsers.ParseBool(sh);
                }
                ParseInt(p, "OutpostHaulBase", ref OutpostHaulBase, 0, 100);
                ParseInt(p, "OutpostHaulPerLevel", ref OutpostHaulPerLevel, 0, 100);
                ParseInt(p, "OutpostStashMax", ref OutpostStashMax, 0, 1000);
                {
                    string bv;
                    if (p.Values.TryGetValue("HomeStatusHud", out bv) && !string.IsNullOrEmpty(bv)) HomeStatusHud = StringParsers.ParseBool(bv);
                    if (p.Values.TryGetValue("BaseNoise", out bv) && !string.IsNullOrEmpty(bv)) BaseNoise = StringParsers.ParseBool(bv);
                }
                ParseFloat(p, "NoisePerMember", ref NoisePerMember, 0f, 100f);
                ParseFloat(p, "NoisePerGenerator", ref NoisePerGenerator, 0f, 100f);
                ParseFloat(p, "NoisePerFacility", ref NoisePerFacility, 0f, 100f);
                ParseFloat(p, "NoiseHeatMult", ref NoiseHeatMult, 0f, 100f);
                ParseFloat(p, "NoiseNpcShotMult", ref NoiseNpcShotMult, 0f, 100f);
                ParseFloat(p, "NoiseNpcShotDefault", ref NoiseNpcShotDefault, 0f, 100f);
                ParseFloat(p, "NoiseHalfLifeHours", ref NoiseHalfLifeHours, 0.05f, 48f);
                ParseFloat(p, "NoiseEventCap", ref NoiseEventCap, 0f, 10000f);
                ParseFloat(p, "NoiseNoticeable", ref NoiseNoticeable, 0f, 10000f);
                ParseFloat(p, "NoiseLoud", ref NoiseLoud, 0f, 10000f);
                ParseFloat(p, "NoiseVeryLoud", ref NoiseVeryLoud, 0f, 10000f);
                ParseFloat(p, "AttentionPerNoiseHour", ref AttentionPerNoiseHour, 0f, 100f);
                ParseFloat(p, "AttentionDecayPerHour", ref AttentionDecayPerHour, 0f, 100f);
                {
                    string bv;
                    if (p.Values.TryGetValue("BaseAttacks", out bv) && !string.IsNullOrEmpty(bv)) BaseAttacks = StringParsers.ParseBool(bv);
                    if (p.Values.TryGetValue("BaseAttackGameStages", out bv) && !string.IsNullOrEmpty(bv)) BaseAttackGameStages = bv.Trim();
                }
                ParseFloat(p, "BaseAttackGapDays", ref BaseAttackGapDays, 0f, 100f);
                ParseFloat(p, "BaseAttackLeadMinHours", ref BaseAttackLeadMinHours, 0f, 24f);
                ParseFloat(p, "BaseAttackLeadMaxHours", ref BaseAttackLeadMaxHours, 0f, 24f);
                ParseFloat(p, "BaseAttackBase", ref BaseAttackBase, 0f, 200f);
                ParseFloat(p, "BaseAttackPerGamestage", ref BaseAttackPerGamestage, 0f, 10f);
                ParseFloat(p, "BaseAttackPerMember", ref BaseAttackPerMember, 0f, 10f);
                ParseInt(p, "BaseAttackMin", ref BaseAttackMin, 1, 200);
                ParseInt(p, "BaseAttackMax", ref BaseAttackMax, 1, 200);
                ParseFloat(p, "BaseAttackSpawnMin", ref BaseAttackSpawnMin, 1f, 200f);
                ParseFloat(p, "BaseAttackSpawnMax", ref BaseAttackSpawnMax, 2f, 300f);
                ParseFloat(p, "BaseAttackSpread", ref BaseAttackSpread, 0f, 180f);
                ParseInt(p, "BaseAttackMinPlayerRange", ref BaseAttackMinPlayerRange, 0, 200);
                ParseFloat(p, "BaseAttackDefendMargin", ref BaseAttackDefendMargin, 0f, 60f);
                ParseFloat(p, "BaseAttackHomeMargin", ref BaseAttackHomeMargin, 0f, 200f);
                ParseFloat(p, "BaseAttackLeaveRange", ref BaseAttackLeaveRange, 20f, 1000f);
                ParseFloat(p, "BaseAttackMaxHours", ref BaseAttackMaxHours, 0.5f, 48f);
                ParseFloat(p, "BaseAttackResumeSeconds", ref BaseAttackResumeSeconds, 0f, 300f);
                ParseFloat(p, "BaseAttackWinMorale", ref BaseAttackWinMorale, 0f, 10f);
                if (BaseAttackLeaveRange < BaseAttackHomeMargin + 20f)
                {
                    // "left the fight" inside "home" would despawn and re-arm the attack every lead time
                    ScLog.Warn("config: BaseAttackLeaveRange " + BaseAttackLeaveRange + " is not well beyond BaseAttackHomeMargin " + BaseAttackHomeMargin + " - using " + (BaseAttackHomeMargin + 20f));
                    BaseAttackLeaveRange = BaseAttackHomeMargin + 20f;
                }
                if (BaseAttackSpawnMax <= BaseAttackSpawnMin) BaseAttackSpawnMax = BaseAttackSpawnMin + 1f;
                if (BaseAttackLeadMaxHours < BaseAttackLeadMinHours) BaseAttackLeadMaxHours = BaseAttackLeadMinHours;
                {
                    string bv;
                    if (p.Values.TryGetValue("WatchPosts", out bv) && !string.IsNullOrEmpty(bv)) WatchPosts = StringParsers.ParseBool(bv);
                }
                ParseInt(p, "WatchMaxPosts", ref WatchMaxPosts, 0, 16);
                ParseFloat(p, "WatchShiftHours", ref WatchShiftHours, 0.25f, 240f);
                ParseFloat(p, "WatchRestHours", ref WatchRestHours, 0f, 240f);
                ParseInt(p, "WatchShiftAmmo", ref WatchShiftAmmo, 0, 100);
                ParseFloat(p, "WatchSightMult", ref WatchSightMult, 1f, 4f);
                ParseFloat(p, "WatchPlaceSeconds", ref WatchPlaceSeconds, 5f, 600f);
                ParseFloat(p, "WatchAttentionCut", ref WatchAttentionCut, 0f, 1f);
                ParseFloat(p, "WatchAttentionCutMax", ref WatchAttentionCutMax, 0f, 1f);
                {
                    string bv;
                    if (p.Values.TryGetValue("BaseAttacksAway", out bv) && !string.IsNullOrEmpty(bv)) BaseAttacksAway = StringParsers.ParseBool(bv);
                    if (p.Values.TryGetValue("BaseSiege", out bv) && !string.IsNullOrEmpty(bv)) BaseSiege = StringParsers.ParseBool(bv);
                }
                ParseFloat(p, "BaseAttackJoinRange", ref BaseAttackJoinRange, 10f, 1000f);
                if (BaseAttackJoinRange > BaseAttackLeaveRange - 20f)
                {
                    // joined at the edge of "left": the fight would hand over and come back every few steps
                    ScLog.Warn("config: BaseAttackJoinRange " + BaseAttackJoinRange + " is not well inside BaseAttackLeaveRange " + BaseAttackLeaveRange + " - using " + (BaseAttackLeaveRange - 20f));
                    BaseAttackJoinRange = BaseAttackLeaveRange - 20f;
                }
                ParseFloat(p, "BaseAwayHoursBase", ref BaseAwayHoursBase, 0.25f, 48f);
                ParseFloat(p, "BaseAwayHoursPer10", ref BaseAwayHoursPer10, 0f, 24f);
                ParseInt(p, "BaseAwayDamageBand", ref BaseAwayDamageBand, 0, 100);
                ParseFloat(p, "BaseAwayAmmoPer", ref BaseAwayAmmoPer, 1f, 200f);
                ParseFloat(p, "BaseAwayAmmoOverrun", ref BaseAwayAmmoOverrun, 1f, 10f);
                ParseInt(p, "BaseAwayHurtMin", ref BaseAwayHurtMin, 0, 50);
                ParseInt(p, "BaseAwayHurtMax", ref BaseAwayHurtMax, 0, 50);
                if (BaseAwayHurtMax < BaseAwayHurtMin) BaseAwayHurtMax = BaseAwayHurtMin;
                ParseFloat(p, "BaseAwayHurtOverrun", ref BaseAwayHurtOverrun, 0f, 1f);
                ParseFloat(p, "BaseAwayDeathChance", ref BaseAwayDeathChance, 0f, 1f);
                ParseFloat(p, "BaseAwayDeathHorde", ref BaseAwayDeathHorde, 1f, 200f);
                ParseFloat(p, "BaseAwayDeathSkillRef", ref BaseAwayDeathSkillRef, 2f, 14f);
                ParseFloat(p, "BaseAwayDeathSkillStep", ref BaseAwayDeathSkillStep, 0f, 0.5f);
                ParseFloat(p, "BaseAwayDeathCap", ref BaseAwayDeathCap, 0f, 1f);
                ParseFloat(p, "BaseAwayDeathUnhurt", ref BaseAwayDeathUnhurt, 0f, 1f);
                ParseFloat(p, "BaseAwayHeldMorale", ref BaseAwayHeldMorale, 0f, 10f);
                ParseFloat(p, "BaseAwayDamagedMorale", ref BaseAwayDamagedMorale, 0f, 30f);
                ParseFloat(p, "BaseAwayOverrunMorale", ref BaseAwayOverrunMorale, 0f, 30f);
                ParseInt(p, "BaseSiegePenalty", ref BaseSiegePenalty, 0, 100);
                ParseFloat(p, "BaseDefenceBase", ref BaseDefenceBase, 0f, 100f);
                ParseFloat(p, "BaseDefencePerResident", ref BaseDefencePerResident, 0f, 100f);
                ParseFloat(p, "BaseDefencePerCombatStar", ref BaseDefencePerCombatStar, 0f, 100f);
                ParseFloat(p, "BaseDefencePerPost", ref BaseDefencePerPost, 0f, 100f);
                ParseFloat(p, "BaseDefencePostCap", ref BaseDefencePostCap, 0f, 100f);
                ParseFloat(p, "BaseDefencePerBlock", ref BaseDefencePerBlock, 0f, 100f);
                ParseFloat(p, "BaseDefenceBlockCap", ref BaseDefenceBlockCap, 0f, 100f);
                ParseFloat(p, "BaseDefenceNoAmmo", ref BaseDefenceNoAmmo, 0f, 100f);
                ParseFloat(p, "BaseDefenceMorale", ref BaseDefenceMorale, 0f, 100f);
                ParseFloat(p, "BaseDefenceBigFrom", ref BaseDefenceBigFrom, 0f, 200f);
                ParseFloat(p, "BaseDefenceBigPer", ref BaseDefenceBigPer, 0f, 10f);
                ParseFloat(p, "BaseDefenceBigCap", ref BaseDefenceBigCap, 0f, 100f);
                {
                    string gv;
                    if (p.Values.TryGetValue("GuardTowerGhost", out gv) && !string.IsNullOrEmpty(gv))
                    {
                        gv = gv.Trim().ToLowerInvariant();
                        if (gv == "glow" || gv == "solid" || gv == "plain" || gv == "off") GuardTowerGhost = gv;
                        else ScLog.Warn("config: GuardTowerGhost '" + gv + "' is not glow, solid, plain or off - using " + GuardTowerGhost);
                    }
                }
                ParseInt(p, "GuardTowerFill", ref GuardTowerFill, 0, 8);
                ParseInt(p, "GuardTowerCut", ref GuardTowerCut, 0, 6);
                {
                    string bv;
                    if (p.Values.TryGetValue("OutpostAttacks", out bv) && !string.IsNullOrEmpty(bv)) OutpostAttacks = StringParsers.ParseBool(bv);
                }
                {
                    string bv;
                    if (p.Values.TryGetValue("OutpostGoodNews", out bv) && !string.IsNullOrEmpty(bv)) OutpostGoodNews = StringParsers.ParseBool(bv);
                }
                ParseFloat(p, "OutpostGoodNewsChance", ref OutpostGoodNewsChance, 0f, 100000f);
                ParseInt(p, "OutpostSecBase", ref OutpostSecBase, 0, 100000);
                ParseInt(p, "OutpostSecPerStaffer", ref OutpostSecPerStaffer, 0, 100000);
                ParseInt(p, "OutpostSecPerCombatStar", ref OutpostSecPerCombatStar, 0, 100000);
                ParseInt(p, "OutpostSecPerTier", ref OutpostSecPerTier, 0, 100000);
                ParseInt(p, "OutpostSecVisited", ref OutpostSecVisited, 0, 100000);
                ParseInt(p, "OutpostSecNoAmmo", ref OutpostSecNoAmmo, 0, 100000);
                ParseInt(p, "OutpostSecPerDefense", ref OutpostSecPerDefense, 0, 100000);
                ParseInt(p, "OutpostSecDefenseCap", ref OutpostSecDefenseCap, 0, 100000);
                ParseFloat(p, "OutpostSecBonusFade", ref OutpostSecBonusFade, 0f, 100000f);
                ParseFloat(p, "OutpostSecWinBonus", ref OutpostSecWinBonus, 0f, 100000f);
                ParseFloat(p, "OutpostSecBonusMax", ref OutpostSecBonusMax, 0f, 100000f);
                ParseFloat(p, "OutpostAttackChance", ref OutpostAttackChance, 0f, 100000f);
                ParseInt(p, "OutpostAttackHours", ref OutpostAttackHours, 1, 100000);
                ParseFloat(p, "OutpostAttackBase", ref OutpostAttackBase, 0f, 100000f);
                ParseFloat(p, "OutpostAttackPerGamestage", ref OutpostAttackPerGamestage, 0f, 100000f);
                ParseInt(p, "OutpostAttackMax", ref OutpostAttackMax, 1, 100000);
                ParseInt(p, "OutpostAttendedBonus", ref OutpostAttendedBonus, 0, 100000);
                ParseInt(p, "OutpostDamageBand", ref OutpostDamageBand, 0, 100000);
                ParseInt(p, "OutpostDamagedDays", ref OutpostDamagedDays, 1, 100000);
                ParseInt(p, "OutpostRepairMats", ref OutpostRepairMats, 0, 100000);
                ParseInt(p, "OutpostSiegePenalty", ref OutpostSiegePenalty, 0, 100000);
                ParseFloat(p, "OutpostWinMorale", ref OutpostWinMorale, 0f, 100000f);
                ParseInt(p, "OutpostNeglectDays", ref OutpostNeglectDays, 1, 100000);
                ParseInt(p, "OutpostRaiderGamestage", ref OutpostRaiderGamestage, 0, 100000);
                ParseFloat(p, "OutpostRaiderChance", ref OutpostRaiderChance, 0f, 100000f);
                ParseInt(p, "OutpostDemandAmmo", ref OutpostDemandAmmo, 0, 100000);
                ParseInt(p, "OutpostDemandFood", ref OutpostDemandFood, 0, 100000);
                bool enclaves = Enclaves;
                if (p.Values.ContainsKey("Enclaves")) p.ParseBool("Enclaves", ref enclaves);
                Enclaves = enclaves;
                ParseFloat(p, "EnclavesPerKm2", ref EnclavesPerKm2, 0f, 10f);
                ParseInt(p, "EnclaveMin", ref EnclaveMin, 0, 500);
                ParseInt(p, "EnclaveMax", ref EnclaveMax, 0, 500);
                if (EnclaveMax < EnclaveMin) EnclaveMax = EnclaveMin;
                ParseInt(p, "EnclaveSpacingMin", ref EnclaveSpacingMin, 0, 20000);
                ParseFloat(p, "EnclaveSpacingFactor", ref EnclaveSpacingFactor, 0f, 5f);
                ParseInt(p, "EnclaveSpacingFloor", ref EnclaveSpacingFloor, 0, 20000);
                ParseFloat(p, "EnclaveHostileShare", ref EnclaveHostileShare, 0f, 1f);
                ParseInt(p, "EnclaveHostileMinStart", ref EnclaveHostileMinStart, 0, 20000);
                ParseInt(p, "EnclaveStartRing", ref EnclaveStartRing, 0, 10);
                ParseInt(p, "EnclaveStartRingMin", ref EnclaveStartRingMin, 0, 20000);
                ParseInt(p, "EnclaveStartRingMax", ref EnclaveStartRingMax, 0, 20000);
                if (EnclaveStartRingMax < EnclaveStartRingMin) EnclaveStartRingMax = EnclaveStartRingMin;
                ParseInt(p, "EnclaveStartRingSpacing", ref EnclaveStartRingSpacing, 0, 20000);
                ParseInt(p, "EnclaveTierMin", ref EnclaveTierMin, 0, 6);
                ParseInt(p, "EnclaveTierMax", ref EnclaveTierMax, 0, 6);
                if (EnclaveTierMax < EnclaveTierMin) EnclaveTierMax = EnclaveTierMin;
                ParseInt(p, "EnclaveSizeMin", ref EnclaveSizeMin, 1, 1000);
                ParseInt(p, "EnclaveSizeMax", ref EnclaveSizeMax, 1, 1000);
                if (EnclaveSizeMax < EnclaveSizeMin) EnclaveSizeMax = EnclaveSizeMin;
                ParseInt(p, "EnclaveTraderDistance", ref EnclaveTraderDistance, 0, 20000);
                ParseInt(p, "EnclaveSpawnDistance", ref EnclaveSpawnDistance, 0, 20000);
                ParseInt(p, "EnclaveEdgeDistance", ref EnclaveEdgeDistance, 0, 20000);
                {
                    string ev;
                    if (p.Values.TryGetValue("EnclaveKindWeights", out ev) && !string.IsNullOrEmpty(ev)) ParseKindWeights(ev);
                    // an empty value is meaningful here: no prefab name is ruled out
                    if (p.Values.TryGetValue("EnclavePoiExcludes", out ev))
                    {
                        EnclavePoiExcludes.Clear();
                        if (!string.IsNullOrEmpty(ev)) foreach (string x in SplitList(ev)) EnclavePoiExcludes.Add(x.ToLowerInvariant());
                    }
                }
                bool lateSeed = EnclaveLateSeed;
                if (p.Values.ContainsKey("EnclaveLateSeed")) p.ParseBool("EnclaveLateSeed", ref lateSeed);
                EnclaveLateSeed = lateSeed;
                ParseInt(p, "EnclaveClaimBuffer", ref EnclaveClaimBuffer, 0, 5000);
                ParseFloat(p, "EnclaveUnvisitedWeight", ref EnclaveUnvisitedWeight, 0f, 100f);
                ParseInt(p, "EnclaveRelocateRange", ref EnclaveRelocateRange, 0, 20000);
                ParseInt(p, "EnclaveDiscoverRange", ref EnclaveDiscoverRange, 0, 5000);
                ParseInt(p, "EnclaveRadioRevealDays", ref EnclaveRadioRevealDays, 1, 365);
                ParseInt(p, "EnclaveRadioRange", ref EnclaveRadioRange, 0, 20000);
                // enclave people (E2)
                ParseInt(p, "EnclaveGroupMin", ref EnclaveGroupMin, 1, 12);
                ParseInt(p, "EnclaveGroupMax", ref EnclaveGroupMax, 1, 12);
                if (EnclaveGroupMax < EnclaveGroupMin) EnclaveGroupMax = EnclaveGroupMin;
                ParseInt(p, "EnclaveRaiderGroupMin", ref EnclaveRaiderGroupMin, 1, 12);
                ParseInt(p, "EnclaveRaiderGroupMax", ref EnclaveRaiderGroupMax, 1, 12);
                if (EnclaveRaiderGroupMax < EnclaveRaiderGroupMin) EnclaveRaiderGroupMax = EnclaveRaiderGroupMin;
                ParseInt(p, "EnclaveSpawnRange", ref EnclaveSpawnRange, 20, 1000);
                ParseInt(p, "EnclaveDespawnMargin", ref EnclaveDespawnMargin, 0, 1000);
                ParseInt(p, "EnclaveNoPopRange", ref EnclaveNoPopRange, 0, 500);
                ParseInt(p, "EnclaveSpawnPerTick", ref EnclaveSpawnPerTick, 1, 12);
                bool holdChunks = EnclaveHoldChunks;
                if (p.Values.ContainsKey("EnclaveHoldChunks")) p.ParseBool("EnclaveHoldChunks", ref holdChunks);
                EnclaveHoldChunks = holdChunks;
                bool restock = EnclaveDailyRestock;
                if (p.Values.ContainsKey("EnclaveDailyRestock")) p.ParseBool("EnclaveDailyRestock", ref restock);
                EnclaveDailyRestock = restock;
                ParseInt(p, "EnclaveLeashMargin", ref EnclaveLeashMargin, 2, 100);
                ParseInt(p, "EnclaveDefendRadius", ref EnclaveDefendRadius, 0, 100);
                ParseInt(p, "EnclaveStandingMin", ref EnclaveStandingMin, -100, 0);
                ParseInt(p, "EnclaveWarnPenalty", ref EnclaveWarnPenalty, 0, 100);
                ParseFloat(p, "EnclaveWarnWindow", ref EnclaveWarnWindow, 0f, 3600f);
                ParseFloat(p, "EnclaveHitDebounce", ref EnclaveHitDebounce, 0f, 30f);
                ParseInt(p, "EnclaveThreatBundlesMin", ref EnclaveThreatBundlesMin, 0, 20);
                ParseInt(p, "EnclaveThreatBundlesMax", ref EnclaveThreatBundlesMax, 0, 20);
                if (EnclaveThreatBundlesMax < EnclaveThreatBundlesMin) EnclaveThreatBundlesMax = EnclaveThreatBundlesMin;
                ParseFloat(p, "EnclaveThreatGrace", ref EnclaveThreatGrace, 0f, 300f);
                bool containerLock = EnclaveContainerLock;
                if (p.Values.ContainsKey("EnclaveContainerLock")) p.ParseBool("EnclaveContainerLock", ref containerLock);
                EnclaveContainerLock = containerLock;
                ParseInt(p, "EnclaveWildSpawnMargin", ref EnclaveWildSpawnMargin, 0, 1000);
                {
                    string kr;
                    if (p.Values.TryGetValue("EnclaveKindRows", out kr) && !string.IsNullOrEmpty(kr)) ParseKindRows(kr);
                }
                // enclave trade, enlisting and recruiting (E3); the id base keeps base + 5 inside the 256-slot trader list
                ParseInt(p, "EnclaveTraderIdBase", ref EnclaveTraderIdBase, 11, 250);
                ParseFloat(p, "EnclaveBuyNeutral", ref EnclaveBuyNeutral, 0.1f, 10f);
                ParseFloat(p, "EnclaveBuyFriendly", ref EnclaveBuyFriendly, 0.1f, 10f);
                ParseFloat(p, "EnclaveBuyAllied", ref EnclaveBuyAllied, 0.1f, 10f);
                ParseFloat(p, "EnclaveSellNeutral", ref EnclaveSellNeutral, 0.1f, 10f);
                ParseFloat(p, "EnclaveSellFriendly", ref EnclaveSellFriendly, 0.1f, 10f);
                ParseFloat(p, "EnclaveSellAllied", ref EnclaveSellAllied, 0.1f, 10f);
                ParseInt(p, "EnclaveEnlistCostNeutral", ref EnclaveEnlistCostNeutral, 0, 100000);
                ParseInt(p, "EnclaveEnlistCostFriendly", ref EnclaveEnlistCostFriendly, 0, 100000);
                ParseInt(p, "EnclaveEnlistCostAllied", ref EnclaveEnlistCostAllied, 0, 100000);
                ParseInt(p, "EnclaveEnlistDays", ref EnclaveEnlistDays, 1, 60);
                ParseInt(p, "EnclaveEnlistDeathPenalty", ref EnclaveEnlistDeathPenalty, 0, 100);
                ParseInt(p, "EnclaveRecruitMinLeft", ref EnclaveRecruitMinLeft, 1, 12);
                ParseInt(p, "EnclaveRecruitCooldownDays", ref EnclaveRecruitCooldownDays, 0, 365);
                ParseInt(p, "EnclaveRecruitStanding", ref EnclaveRecruitStanding, 0, 100);
                // enclave requests (E4)
                ParseFloat(p, "EnclaveRequestChanceNeutral", ref EnclaveRequestChanceNeutral, 0f, 1f);
                ParseFloat(p, "EnclaveRequestChanceFriendly", ref EnclaveRequestChanceFriendly, 0f, 1f);
                ParseFloat(p, "EnclaveRequestChanceAllied", ref EnclaveRequestChanceAllied, 0f, 1f);
                ParseInt(p, "EnclaveRequestMaxOpen", ref EnclaveRequestMaxOpen, 0, 100);
                ParseInt(p, "EnclaveRequestCooldownDays", ref EnclaveRequestCooldownDays, 0, 365);
                ParseInt(p, "EnclaveOfferLapseDays", ref EnclaveOfferLapseDays, 1, 60);
                ParseInt(p, "EnclaveDueDaysItems", ref EnclaveDueDaysItems, 1, 60);
                ParseInt(p, "EnclaveGeneratorFromDay", ref EnclaveGeneratorFromDay, 0, 100000);
                ParseInt(p, "EnclaveGainResource", ref EnclaveGainResource, 0, 100);
                ParseInt(p, "EnclaveGainGenerator", ref EnclaveGainGenerator, 0, 100);
                ParseFloat(p, "EnclaveGunQualityBase", ref EnclaveGunQualityBase, 0f, 10f);
                ParseFloat(p, "EnclaveGunQualityStep", ref EnclaveGunQualityStep, 0f, 10f);
                ParseInt(p, "EnclaveGunPointsMin", ref EnclaveGunPointsMin, 0, 100);
                ParseInt(p, "EnclaveGunPointsMax", ref EnclaveGunPointsMax, 0, 100);
                if (EnclaveGunPointsMax < EnclaveGunPointsMin) EnclaveGunPointsMax = EnclaveGunPointsMin;
                {
                    string rv;
                    if (p.Values.TryGetValue("EnclaveRequestKindWeights", out rv) && !string.IsNullOrEmpty(rv))
                        ParseNamedWeights("EnclaveRequestKindWeights", rv, ScEncRequests.RollKinds, EnclaveRequestKindWeights);
                    if (p.Values.TryGetValue("EnclaveRequestSizeWeights", out rv) && !string.IsNullOrEmpty(rv))
                        ParseNamedWeights("EnclaveRequestSizeWeights", rv, ScEncRequests.Sizes, EnclaveRequestSizeWeights);
                    if (p.Values.TryGetValue("EnclaveGunTierPoints", out rv) && !string.IsNullOrEmpty(rv)) ParseTierInts("EnclaveGunTierPoints", rv, EnclaveGunTierPoints, 0, 100);
                    if (p.Values.TryGetValue("EnclaveGunValueBands", out rv) && !string.IsNullOrEmpty(rv)) ParseTierInts("EnclaveGunValueBands", rv, EnclaveGunValueBands, 0, 10000000);
                    if (p.Values.TryGetValue("EnclaveGunWorthBands", out rv) && !string.IsNullOrEmpty(rv)) ParseTierInts("EnclaveGunWorthBands", rv, EnclaveGunWorthBands, 0, 100);
                    if (p.Values.TryGetValue("EnclaveLossDecline", out rv) && !string.IsNullOrEmpty(rv)) ParseTierInts("EnclaveLossDecline", rv, EnclaveLossDecline, 0, 100);
                    if (p.Values.TryGetValue("EnclaveLossFail", out rv) && !string.IsNullOrEmpty(rv)) ParseTierInts("EnclaveLossFail", rv, EnclaveLossFail, 0, 100);
                    if (p.Values.TryGetValue("EnclaveLossLapse", out rv) && !string.IsNullOrEmpty(rv)) ParseTierInts("EnclaveLossLapse", rv, EnclaveLossLapse, 0, 100);
                }
                // enclave requests, part 2 (E5)
                ParseInt(p, "EnclaveDueDaysSearch", ref EnclaveDueDaysSearch, 1, 60);
                ParseInt(p, "EnclaveMissingMin", ref EnclaveMissingMin, 0, 20000);
                ParseInt(p, "EnclaveMissingMax", ref EnclaveMissingMax, 0, 20000);
                if (EnclaveMissingMax < EnclaveMissingMin) EnclaveMissingMax = EnclaveMissingMin;
                ParseFloat(p, "EnclaveMissingAlive", ref EnclaveMissingAlive, 0f, 1f);
                ParseFloat(p, "EnclaveMissingAliveFade", ref EnclaveMissingAliveFade, 0f, 1f);
                ParseFloat(p, "EnclaveMissingAliveMin", ref EnclaveMissingAliveMin, 0f, 1f);
                ParseInt(p, "EnclaveInfestRange", ref EnclaveInfestRange, 0, 5000);
                ParseFloat(p, "EnclaveSiegeChance", ref EnclaveSiegeChance, 0f, 1f);
                ParseInt(p, "EnclaveSiegeHourMin", ref EnclaveSiegeHourMin, 0, 23);
                ParseInt(p, "EnclaveSiegeHourMax", ref EnclaveSiegeHourMax, 0, 23);
                if (EnclaveSiegeHourMax < EnclaveSiegeHourMin) EnclaveSiegeHourMax = EnclaveSiegeHourMin;
                ParseInt(p, "EnclaveSiegeRange", ref EnclaveSiegeRange, 0, 20000);
                ParseFloat(p, "EnclaveSiegeMinutes", ref EnclaveSiegeMinutes, 1f, 600f);
                ParseInt(p, "EnclaveSiegeArriveRange", ref EnclaveSiegeArriveRange, 10, 500);
                ParseFloat(p, "EnclaveSiegeMaxHours", ref EnclaveSiegeMaxHours, 0.5f, 48f);
                ParseFloat(p, "EnclaveSiegeBase", ref EnclaveSiegeBase, 0f, 200f);
                ParseFloat(p, "EnclaveSiegePerGS", ref EnclaveSiegePerGS, 0f, 10f);
                ParseInt(p, "EnclaveSiegeMin", ref EnclaveSiegeMin, 1, 200);
                ParseInt(p, "EnclaveSiegeMax", ref EnclaveSiegeMax, 1, 200);
                if (EnclaveSiegeMax < EnclaveSiegeMin) EnclaveSiegeMax = EnclaveSiegeMin;
                ParseInt(p, "EnclaveSiegeSpawnMin", ref EnclaveSiegeSpawnMin, 1, 500);
                ParseInt(p, "EnclaveSiegeSpawnMax", ref EnclaveSiegeSpawnMax, 2, 500);
                if (EnclaveSiegeSpawnMax <= EnclaveSiegeSpawnMin) EnclaveSiegeSpawnMax = EnclaveSiegeSpawnMin + 1;
                ParseFloat(p, "EnclaveSiegeSpread", ref EnclaveSiegeSpread, 0f, 180f);
                ParseInt(p, "EnclaveSiegeMinPlayerRange", ref EnclaveSiegeMinPlayerRange, 0, 200);
                ParseInt(p, "EnclaveSiegeNoPopRange", ref EnclaveSiegeNoPopRange, 0, 500);
                ParseFloat(p, "EnclaveSiegeResumeSeconds", ref EnclaveSiegeResumeSeconds, 0f, 600f);
                ParseFloat(p, "EnclaveSiegeLeaveRoll", ref EnclaveSiegeLeaveRoll, 0f, 1f);
                ParseInt(p, "EnclaveGainMissingAlive", ref EnclaveGainMissingAlive, 0, 100);
                ParseInt(p, "EnclaveGainMissingDead", ref EnclaveGainMissingDead, 0, 100);
                ParseInt(p, "EnclaveGainInfestation", ref EnclaveGainInfestation, 0, 100);
                ParseInt(p, "EnclaveGainSiege", ref EnclaveGainSiege, 0, 100);
                // allies (E6)
                ParseInt(p, "EnclaveBenefitBundles", ref EnclaveBenefitBundles, 0, 20);
                ParseInt(p, "EnclaveBenefitMorale", ref EnclaveBenefitMorale, 0, 50);
                ParseInt(p, "EnclaveBenefitBeds", ref EnclaveBenefitBeds, 0, 50);
                ParseFloat(p, "EnclaveBenefitClubXp", ref EnclaveBenefitClubXp, 1f, 10f);
                ParseFloat(p, "EnclaveBenefitTrainingXp", ref EnclaveBenefitTrainingXp, 1f, 10f);
                ParseFloat(p, "EnclaveBenefitXpCap", ref EnclaveBenefitXpCap, 1f, 20f);
                ParseInt(p, "EnclaveWatchRange", ref EnclaveWatchRange, 0, 5000);
                ParseFloat(p, "EnclaveWatchSpread", ref EnclaveWatchSpread, 0f, 1f);
                ParseInt(p, "EnclaveAllyMorale", ref EnclaveAllyMorale, 0, 50);
                ParseInt(p, "EnclaveAllyMoraleCap", ref EnclaveAllyMoraleCap, 0, 100);
                ParseInt(p, "EnclaveLostMorale", ref EnclaveLostMorale, 0, 30);
                ParseInt(p, "EnclaveRefillDays", ref EnclaveRefillDays, 0, 365);
                ParseInt(p, "EnclaveAllyRevealDays", ref EnclaveAllyRevealDays, 1, 365);
                bool infest = Infestations;
                if (p.Values.ContainsKey("Infestations")) p.ParseBool("Infestations", ref infest);
                Infestations = infest;
                ParseFloat(p, "InfestDailyChance", ref InfestDailyChance, 0f, 1f);
                ParseFloat(p, "InfestPerKm2", ref InfestPerKm2, 0f, 10f);
                ParseInt(p, "InfestMin", ref InfestMin, 0, 500);
                ParseInt(p, "InfestMax", ref InfestMax, 0, 500);
                if (InfestMax < InfestMin) InfestMax = InfestMin;
                ParseInt(p, "InfestSpreadDays", ref InfestSpreadDays, 0, 365);
                ParseFloat(p, "InfestSpreadChance", ref InfestSpreadChance, 0f, 1f);
                ParseInt(p, "InfestSpreadRange", ref InfestSpreadRange, 0, 5000);
                ParseInt(p, "InfestTraderDistance", ref InfestTraderDistance, 0, 20000);
                ParseInt(p, "InfestMinDist", ref InfestMinDist, 0, 20000);
                ParseInt(p, "InfestMaxDist", ref InfestMaxDist, 0, 20000);
                if (InfestMaxDist < InfestMinDist) InfestMaxDist = InfestMinDist;
                ParseFloat(p, "InfestOutsideWeight", ref InfestOutsideWeight, 0f, 100f);
                {
                    string tv;
                    if (p.Values.TryGetValue("InfestTierByDay", out tv) && !string.IsNullOrEmpty(tv)) ParseTierByDay(tv);
                }
                // 1..4: never fewer zombies than vanilla's roll, and a room's counts stay far below the byte the world saves them in
                ParseFloat(p, "InfestMultiplier", ref InfestMultiplier, 1f, 4f);
                ParseFloat(p, "InfestMultiplierHighTier", ref InfestMultiplierHighTier, 1f, 4f);
                ParseInt(p, "InfestHighTier", ref InfestHighTier, 0, 7);
                ParseFloat(p, "InfestFeralShare", ref InfestFeralShare, 0f, 1f);
                bool special = InfestSpecial;
                if (p.Values.ContainsKey("InfestSpecial")) p.ParseBool("InfestSpecial", ref special);
                InfestSpecial = special;
                ParseInt(p, "InfestSpecialRadiatedBelowGS", ref InfestSpecialRadiatedBelowGS, 0, 100000);
                ParseInt(p, "InfestDiscoverRange", ref InfestDiscoverRange, 0, 5000);
                ParseInt(p, "InfestRadioRange", ref InfestRadioRange, 0, 20000);
                ParseInt(p, "InfestPlayerMargin", ref InfestPlayerMargin, 0, 5000);
                ParseInt(p, "InfestRearmPlayerRange", ref InfestRearmPlayerRange, 0, 5000);
                // I3: infestations near home
                ParseInt(p, "InfestHomeRange", ref InfestHomeRange, 0, 5000);
                {
                    string hb;
                    if (p.Values.TryGetValue("InfestHomeMoraleBands", out hb) && !string.IsNullOrEmpty(hb)) ParseHomeBands(hb);
                }
                ParseInt(p, "InfestHomeMoraleCap", ref InfestHomeMoraleCap, 0, 100);
                ParseInt(p, "InfestHomeMoraleNests", ref InfestHomeMoraleNests, 0, 100);
                ParseFloat(p, "InfestHomeAttention", ref InfestHomeAttention, 0f, 20f);
                ParseInt(p, "InfestHomeAttentionCap", ref InfestHomeAttentionCap, 0, 100);
                bool homeKnown = InfestHomeKnown;
                if (p.Values.ContainsKey("InfestHomeKnown")) p.ParseBool("InfestHomeKnown", ref homeKnown);
                InfestHomeKnown = homeKnown;
                bool capFix = SleeperCapFix;
                if (p.Values.ContainsKey("SleeperCapFix")) p.ParseBool("SleeperCapFix", ref capFix);
                SleeperCapFix = capFix;
                // at least 100: a retry due within a tick or two would be due again before Touch pushes it, and a room at the
                // cap would roll and close every few ticks while a player stands near it
                ParseInt(p, "SleeperCapRetryTicks", ref SleeperCapRetryTicks, 100, 240000);
                bool capPause = SleeperCapPause;
                if (p.Values.ContainsKey("SleeperCapPause")) p.ParseBool("SleeperCapPause", ref capPause);
                SleeperCapPause = capPause;
                ParseInt(p, "SleeperCapPauseMaxTicks", ref SleeperCapPauseMaxTicks, 0, 240000);
                ScPoiTypes.Parse(p);
                ParseFloat(p, "SurvivorLookChance", ref SurvivorLookChance, 0f, 1f);
                bool friendlyFire = CommunityFriendlyFire;
                if (p.Values.ContainsKey("CommunityFriendlyFire")) p.ParseBool("CommunityFriendlyFire", ref friendlyFire);
                CommunityFriendlyFire = friendlyFire;
                ParseFloat(p, "SurvivorGearPieceChance", ref SurvivorGearPieceChance, 0f, 1f);
                bool alwaysChest = SurvivorGearAlwaysChest;
                if (p.Values.ContainsKey("SurvivorGearAlwaysChest")) p.ParseBool("SurvivorGearAlwaysChest", ref alwaysChest);
                SurvivorGearAlwaysChest = alwaysChest;
                ParseFloat(p, "SurvivorGearMixChance", ref SurvivorGearMixChance, 0f, 1f);
                ParseInt(p, "SurvivorGearQualityMin", ref SurvivorGearQualityMin, 1, 6);
                ParseInt(p, "SurvivorGearQualityMax", ref SurvivorGearQualityMax, 1, 6);
                ParseFloat(p, "SurvivorOutfitShare", ref SurvivorOutfitShare, 0f, 1f);
                ParseFloat(p, "RaiderOutfitShare", ref RaiderOutfitShare, 0f, 1f);
                ParseFloat(p, "SurvivorBaldChance", ref SurvivorBaldChance, 0f, 1f);
                ParseFloat(p, "SurvivorFacialHairChance", ref SurvivorFacialHairChance, 0f, 1f);
                ParseFloat(p, "SurvivorDyedHairChanceFemale", ref SurvivorDyedHairChanceFemale, 0f, 1f);
                ParseFloat(p, "SurvivorDyedHairChanceMale", ref SurvivorDyedHairChanceMale, 0f, 1f);
                ParseFloat(p, "SurvivorBodyPaintChance", ref SurvivorBodyPaintChance, 0f, 1f);
                ParseFloat(p, "RaiderBodyPaintChance", ref RaiderBodyPaintChance, 0f, 1f);
                ParseFloat(p, "SurvivorFacePaintChance", ref SurvivorFacePaintChance, 0f, 1f);
                ParseFloat(p, "RaiderFacePaintChance", ref RaiderFacePaintChance, 0f, 1f);
                ParseFloat(p, "SurvivorTattooChance", ref SurvivorTattooChance, 0f, 1f);
                ParseFloat(p, "RaiderTattooChance", ref RaiderTattooChance, 0f, 1f);
                bool aimPose = SurvivorAimPose;
                if (p.Values.ContainsKey("SurvivorAimPose")) p.ParseBool("SurvivorAimPose", ref aimPose);
                SurvivorAimPose = aimPose;
                bool aimVisualOnly = SurvivorAimVisualOnly;
                if (p.Values.ContainsKey("SurvivorAimVisualOnly")) p.ParseBool("SurvivorAimVisualOnly", ref aimVisualOnly);
                SurvivorAimVisualOnly = aimVisualOnly;
                ParseFloat(p, "SurvivorAimHoldSeconds", ref SurvivorAimHoldSeconds, 0.25f, 30f);
                bool secondWeapon = SurvivorSecondWeapon;
                if (p.Values.ContainsKey("SurvivorSecondWeapon")) p.ParseBool("SurvivorSecondWeapon", ref secondWeapon);
                SurvivorSecondWeapon = secondWeapon;
                ParseFloat(p, "SurvivorFirearmChance", ref SurvivorFirearmChance, 0f, 1f);
                ParseFloat(p, "SurvivorMeleeOnlyChance", ref SurvivorMeleeOnlyChance, 0f, 1f);
                ParseFloat(p, "SurvivorFirearmChanceT3", ref SurvivorFirearmChanceT3, 0f, 1f);
                bool byGs = XnpcClassByGamestage;
                if (p.Values.ContainsKey("XnpcClassByGamestage")) p.ParseBool("XnpcClassByGamestage", ref byGs);
                XnpcClassByGamestage = byGs;
                ParseFloat(p, "SurvivorRangedMainChance", ref SurvivorRangedMainChance, 0f, 1f);
                ParseInt(p, "XnpcSpawnAmmo", ref XnpcSpawnAmmo, 0, 1000);
                ParseInt(p, "XnpcSpawnArrows", ref XnpcSpawnArrows, 0, 1000);
                bool resupply = XnpcResupply;
                if (p.Values.ContainsKey("XnpcResupply")) p.ParseBool("XnpcResupply", ref resupply);
                XnpcResupply = resupply;
                ParseInt(p, "XnpcResupplyBelow", ref XnpcResupplyBelow, 1, 1000);
                ParseInt(p, "XnpcResupplyAmmo", ref XnpcResupplyAmmo, 1, 1000);
                ParseInt(p, "XnpcResupplyArrows", ref XnpcResupplyArrows, 1, 1000);
                ParseFloat(p, "XnpcResupplyCooldown", ref XnpcResupplyCooldown, 5f, 3600f);
                ParseFloat(p, "XnpcResupplyPointsMult", ref XnpcResupplyPointsMult, 0.1f, 100f);
                bool overwhelmed = Overwhelmed;
                if (p.Values.ContainsKey("Overwhelmed")) p.ParseBool("Overwhelmed", ref overwhelmed);
                Overwhelmed = overwhelmed;
                ParseFloat(p, "OverwhelmedRadius", ref OverwhelmedRadius, 2f, 40f);
                ParseInt(p, "OverwhelmedMin", ref OverwhelmedMin, 1, 20);
                ParseInt(p, "OverwhelmedMax", ref OverwhelmedMax, 1, 20);
                if (OverwhelmedMax < OverwhelmedMin) OverwhelmedMax = OverwhelmedMin;
                ParseInt(p, "OverwhelmedStarsPerStep", ref OverwhelmedStarsPerStep, 1, 7);
                ParseInt(p, "OverwhelmedStrangers", ref OverwhelmedStrangers, 1, 20);
                ParseFloat(p, "OverwhelmedHoldSeconds", ref OverwhelmedHoldSeconds, 0f, 30f);
                ParseFloat(p, "OverwhelmedSustainSeconds", ref OverwhelmedSustainSeconds, 0f, 10f);
                ParseFloat(p, "OverwhelmedGunSeconds", ref OverwhelmedGunSeconds, 0f, 60f);
                bool meleePacing = MeleePacing;
                if (p.Values.ContainsKey("MeleePacing")) p.ParseBool("MeleePacing", ref meleePacing);
                MeleePacing = meleePacing;
                bool meleePacingRaiders = MeleePacingRaiders;
                if (p.Values.ContainsKey("MeleePacingRaiders")) p.ParseBool("MeleePacingRaiders", ref meleePacingRaiders);
                MeleePacingRaiders = meleePacingRaiders;
                bool meleeReach = MeleeReach;
                if (p.Values.ContainsKey("MeleeReach")) p.ParseBool("MeleeReach", ref meleeReach);
                MeleeReach = meleeReach;
                ParseFloat(p, "MeleeReachMargin", ref MeleeReachMargin, 0f, 1.5f);
                bool shooting = ShootingSkill;
                if (p.Values.ContainsKey("ShootingSkill")) p.ParseBool("ShootingSkill", ref shooting);
                ShootingSkill = shooting;
                ParseFloat(p, "ShootingRangePerStar", ref ShootingRangePerStar, 1f, 30f);
                ParseFloat(p, "ShootingEngageMult", ref ShootingEngageMult, 1f, 5f);
                ParseFloat(p, "ShootingEngageMin", ref ShootingEngageMin, 10f, 60f);   // below 10 m BackupRanged walks them out of it
                ParseFloat(p, "ShootingHitRadius", ref ShootingHitRadius, 0.05f, 1f);
                bool chest = ShootingAimChest;
                if (p.Values.ContainsKey("ShootingAimChest")) p.ParseBool("ShootingAimChest", ref chest);
                ShootingAimChest = chest;
                ParseInt(p, "ShootingDefaultStars", ref ShootingDefaultStars, 1, 7);
                string raiderShooting;
                if (p.Values.TryGetValue("RaiderShootingStars", out raiderShooting) && !string.IsNullOrEmpty(raiderShooting)) ParseRaiderShooting(raiderShooting);
                bool dialogLine = DialogLine;
                if (p.Values.ContainsKey("DialogLine")) p.ParseBool("DialogLine", ref dialogLine);
                DialogLine = dialogLine;
                bool voiceLines = VoiceLines;
                if (p.Values.ContainsKey("VoiceLines")) p.ParseBool("VoiceLines", ref voiceLines);
                VoiceLines = voiceLines;
                bool muteVc = MuteVcInTalk;
                if (p.Values.ContainsKey("MuteVcInTalk")) p.ParseBool("MuteVcInTalk", ref muteVc);
                MuteVcInTalk = muteVc;
                bool xnpcGetUp = XnpcBodyGetUp;
                if (p.Values.ContainsKey("XnpcBodyGetUp")) p.ParseBool("XnpcBodyGetUp", ref xnpcGetUp);
                XnpcBodyGetUp = xnpcGetUp;
                bool getUpGuard = XnpcGetUpGuard;
                if (p.Values.ContainsKey("XnpcGetUpGuard")) p.ParseBool("XnpcGetUpGuard", ref getUpGuard);
                XnpcGetUpGuard = getUpGuard;
                bool getUpTrace = XnpcGetUpTrace;
                if (p.Values.ContainsKey("XnpcGetUpTrace")) p.ParseBool("XnpcGetUpTrace", ref getUpTrace);
                XnpcGetUpTrace = getUpTrace;
                bool relax = XnpcRelax;
                if (p.Values.ContainsKey("XnpcRelax")) p.ParseBool("XnpcRelax", ref relax);
                XnpcRelax = relax;
                bool relaxRaiders = XnpcRelaxRaiders;
                if (p.Values.ContainsKey("XnpcRelaxRaiders")) p.ParseBool("XnpcRelaxRaiders", ref relaxRaiders);
                XnpcRelaxRaiders = relaxRaiders;
                bool relaxHide = XnpcRelaxHideWeapon;
                if (p.Values.ContainsKey("XnpcRelaxHideWeapon")) p.ParseBool("XnpcRelaxHideWeapon", ref relaxHide);
                XnpcRelaxHideWeapon = relaxHide;
                ParseFloat(p, "XnpcRelaxCalmSeconds", ref XnpcRelaxCalmSeconds, 0f, 120f);
                ParseFloat(p, "XnpcRelaxRange", ref XnpcRelaxRange, 0f, 200f);
                bool xnpcJump = XnpcBodyJumpAnim;
                if (p.Values.ContainsKey("XnpcBodyJumpAnim")) p.ParseBool("XnpcBodyJumpAnim", ref xnpcJump);
                XnpcBodyJumpAnim = xnpcJump;
                bool xnpcColliders = XnpcBodyColliderLayers;
                if (p.Values.ContainsKey("XnpcBodyColliderLayers")) p.ParseBool("XnpcBodyColliderLayers", ref xnpcColliders);
                XnpcBodyColliderLayers = xnpcColliders;
                ParseFloat(p, "XnpcModelY", ref XnpcModelY, -0.3f, 0.3f);
                bool warm = XnpcWarmWeapons;
                if (p.Values.ContainsKey("XnpcWarmWeapons")) p.ParseBool("XnpcWarmWeapons", ref warm);
                XnpcWarmWeapons = warm;
                ParseFloat(p, "RaiderFirearmChance", ref RaiderFirearmChance, 0f, 1f);
                ParseFloat(p, "RaiderMeleeOnlyChance", ref RaiderMeleeOnlyChance, 0f, 1f);
                ParseFloat(p, "RaiderFirearmChanceT3", ref RaiderFirearmChanceT3, 0f, 1f);
                ParseFloat(p, "RaiderLeadRangedDist", ref RaiderLeadRangedDist, 3f, 60f);
                ParseFloat(p, "RaiderLeadRangedSeconds", ref RaiderLeadRangedSeconds, 0f, 30f);
                ParseInt(p, "RaiderSpawnAmmo", ref RaiderSpawnAmmo, 0, 1000);
                ParseInt(p, "RaiderSpawnArrows", ref RaiderSpawnArrows, 0, 1000);
                bool raiderLevels = RaiderXnpcLevels;
                if (p.Values.ContainsKey("RaiderXnpcLevels")) p.ParseBool("RaiderXnpcLevels", ref raiderLevels);
                RaiderXnpcLevels = raiderLevels;
                bool raiderRegen = RaiderLevelRegen;
                if (p.Values.ContainsKey("RaiderLevelRegen")) p.ParseBool("RaiderLevelRegen", ref raiderRegen);
                RaiderLevelRegen = raiderRegen;
                ParseFloat(p, "RaiderWeaponDropChance", ref RaiderWeaponDropChance, 0f, 1f);
                bool raiderDoors = RaiderVanillaDoors;
                if (p.Values.ContainsKey("RaiderVanillaDoors")) p.ParseBool("RaiderVanillaDoors", ref raiderDoors);
                RaiderVanillaDoors = raiderDoors;
                bool raiderTurrets = RaiderTurretsAsEnemies;
                if (p.Values.ContainsKey("RaiderTurretsAsEnemies")) p.ParseBool("RaiderTurretsAsEnemies", ref raiderTurrets);
                RaiderTurretsAsEnemies = raiderTurrets;
                bool verbose = Verbose;
                if (p.Values.ContainsKey("DebugLogging")) p.ParseBool("DebugLogging", ref verbose);
                Verbose = verbose;

                string s;
                if (p.Values.TryGetValue("BedBlocks", out s) && !string.IsNullOrEmpty(s)) { BedBlocks.Clear(); BedBlocks.AddRange(SplitList(s)); }
                if (p.Values.TryGetValue("BedBlocksExclude", out s)) { BedBlocksExclude.Clear(); if (!string.IsNullOrEmpty(s)) BedBlocksExclude.AddRange(SplitList(s)); }
                if (p.Values.TryGetValue("FuelItems", out s) && !string.IsNullOrEmpty(s)) { FuelItems.Clear(); FuelItems.AddRange(SplitList(s)); }
                // an empty value is meaningful for these three: no alcohol / no wells / no dew collectors
                ParseList(p, "LuxuryItems", LuxuryItems);
                ParseList(p, "WellBlocks", WellBlocks);
                ParseList(p, "DewCollectorBlocks", DewCollectorBlocks);
                if (p.Values.TryGetValue("FacilityUpgradeCost", out s) && !string.IsNullOrEmpty(s)) ParseUpgradeCost(s);
                if (p.Values.TryGetValue("FacilityBlocks", out s) && !string.IsNullOrEmpty(s)) { FacilityBlocks.Clear(); FacilityBlocks.AddRange(SplitList(s)); }
                if (p.Values.TryGetValue("SuppliesFavours", out s) && !string.IsNullOrEmpty(s)) ParseSuppliesFavours(s);
                if (p.Values.TryGetValue("ValueOverrides", out s) && !string.IsNullOrEmpty(s)) ParseOverrides(s);
                if (p.Values.TryGetValue("SurvivorGearSets", out s) && !string.IsNullOrEmpty(s)) { SurvivorGearSets.Clear(); SurvivorGearSets.AddRange(SplitList(s)); }
                // an empty value is meaningful here: every set weighs the same
                if (p.Values.TryGetValue("GearSetTiers", out s)) ParseSetTiers(s, GearSetTiers, "GearSetTiers");
                if (p.Values.TryGetValue("GearSetLastTier", out s)) ParseSetTiers(s, GearSetLastTier, "GearSetLastTier");
                if (p.Values.TryGetValue("GearTierWeights", out s) && !string.IsNullOrEmpty(s)) ParseGearTierWeights(s);
                if (p.Values.TryGetValue("GearQualityByTier", out s) && !string.IsNullOrEmpty(s)) ParseGearQuality(s);
                if (p.Values.TryGetValue("XnpcSurvivorClasses", out s) && !string.IsNullOrEmpty(s)) { XnpcSurvivorClasses.Clear(); XnpcSurvivorClasses.AddRange(SplitList(s)); }
                if (p.Values.TryGetValue("XnpcRaiderClasses", out s) && !string.IsNullOrEmpty(s)) { XnpcRaiderClasses.Clear(); XnpcRaiderClasses.AddRange(SplitList(s)); }
                // an empty value is meaningful here: raiders then dress from SurvivorGearSets
                if (p.Values.TryGetValue("RaiderGearSets", out s)) { RaiderGearSets.Clear(); if (!string.IsNullOrEmpty(s)) RaiderGearSets.AddRange(SplitList(s)); }
                if (p.Values.TryGetValue("RaiderRangedShare", out s) && !string.IsNullOrEmpty(s)) ParseRangedShare(s);
                // an empty value is meaningful for these: nothing excluded / no colour counted as dyed
                if (p.Values.TryGetValue("SurvivorExcludeSkins", out s)) { SurvivorExcludeSkins.Clear(); if (!string.IsNullOrEmpty(s)) SurvivorExcludeSkins.AddRange(SplitList(s)); }
                if (p.Values.TryGetValue("SurvivorExcludeEyes", out s)) { SurvivorExcludeEyes.Clear(); if (!string.IsNullOrEmpty(s)) SurvivorExcludeEyes.AddRange(SplitList(s)); }
                if (p.Values.TryGetValue("SurvivorExcludeHairs", out s)) { SurvivorExcludeHairs.Clear(); if (!string.IsNullOrEmpty(s)) SurvivorExcludeHairs.AddRange(SplitList(s)); }
                if (p.Values.TryGetValue("SurvivorDyedHairColors", out s)) { SurvivorDyedHairColors.Clear(); if (!string.IsNullOrEmpty(s)) SurvivorDyedHairColors.AddRange(SplitList(s)); }
                // paints: an empty list = none of that kind
                ParseList(p, "SurvivorBodyPaints", SurvivorBodyPaints);
                ParseList(p, "RaiderBodyPaints", RaiderBodyPaints);
                ParseList(p, "SurvivorFacePaints", SurvivorFacePaints);
                ParseList(p, "RaiderFacePaints", RaiderFacePaints);
                ParseList(p, "SurvivorTattoos", SurvivorTattoos);
                ParseList(p, "RaiderTattoos", RaiderTattoos);
                ParseList(p, "SurvivorTattooInks", SurvivorTattooInks);
                ParseList(p, "RaiderTattooInks", RaiderTattooInks);
                if (p.Values.TryGetValue("TattooMode", out s) && !string.IsNullOrEmpty(s))
                {
                    s = s.Trim();
                    if (string.Equals(s, "Blend", StringComparison.OrdinalIgnoreCase) || string.Equals(s, "Vivid", StringComparison.OrdinalIgnoreCase))
                        TattooMode = char.ToUpperInvariant(s[0]) + s.Substring(1).ToLowerInvariant();
                    else ScLog.Warn("config: TattooMode '" + s + "' is not Blend or Vivid - using " + TattooMode);
                }

                DynamicProperties traits;
                if (p.Classes != null && p.Classes.TryGetValue("Traits", out traits) && traits != null)
                {
                    foreach (var kv in traits.Values)
                    {
                        TraitDef t = ParseTrait(kv.Key, kv.Value);
                        if (t != null) Traits.Add(t);
                    }
                }
                DynamicProperties weapons;
                if (p.Classes != null && p.Classes.TryGetValue("XnpcWeapons", out weapons) && weapons != null)
                {
                    foreach (var kv in weapons.Values)
                    {
                        XnpcWeaponDef wd = ParseXnpcWeapon(kv.Key, kv.Value);
                        if (wd != null) XnpcWeapons.Add(wd);
                    }
                }
                DynamicProperties outfits;
                if (p.Classes != null && p.Classes.TryGetValue("Outfits", out outfits) && outfits != null)
                {
                    foreach (var kv in outfits.Values)
                    {
                        OutfitDef od = ParseOutfit(kv.Key, kv.Value);
                        if (od != null) Outfits.Add(od);
                    }
                }
                parsed = true;
                ScLog.Out("config: MaxMembers=" + MaxMembers + " MaxFollowers=" + MaxFollowers + " FoodPerMemberDay=" + FoodPerMemberDay
                          + " AmmoPerRangedDay=" + AmmoPerRangedDay + " PointsPerBundle=[" + string.Join(",", Array.ConvertAll(PointsPerBundle, x => x.ToString())) + "]"
                          + " PostRadius=" + PostRadius + " DefendBaseRadius=" + DefendBaseRadius + " LowMoraleDays=" + LowMoraleDays + " traits=" + Traits.Count
                          + " beds=" + BedBlocks.Count + " facilities=" + FacilityBlocks.Count + " favours=" + SuppliesFavours.Count
                          + " WaterPerMemberDay=" + WaterPerMemberDay + " luxuryItems=" + LuxuryItems.Count
                          + " wells=" + WellBlocks.Count + "x" + WellWaterPerLevel + " dew=" + DewCollectorBlocks.Count + "x" + DewWaterPerLevel
                          + " upgrades=" + string.Join("/", Array.ConvertAll(FacilityUpgradeCost, x => x.ToString())) + (WellsPlaceLevelOne ? " (wells place L1)" : "")
                          + " drinks=" + DrinksLuxury + "lux+" + DrinksFood + "food +" + DrinksMorale + " for " + DrinksMinutes + "min"
                          + " secondWeapon=" + SurvivorSecondWeapon + " firearmChance=" + SurvivorFirearmChance + " meleeOnly=" + SurvivorMeleeOnlyChance + "/" + RaiderMeleeOnlyChance
                          + " classByGamestage=" + XnpcClassByGamestage + " rangedMainChance=" + SurvivorRangedMainChance
                          + " xnpcWeapons=" + XnpcWeapons.Count + " xnpcSpawnAmmo=" + XnpcSpawnAmmo + "/" + XnpcSpawnArrows
                          + " resupply=" + (XnpcResupply ? "below " + XnpcResupplyBelow + " +" + XnpcResupplyAmmo + "/" + XnpcResupplyArrows
                                            + " every " + XnpcResupplyCooldown + "s x" + XnpcResupplyPointsMult : "off")
                          + " overwhelmed=" + (Overwhelmed ? OverwhelmedMin + ".." + OverwhelmedMax + "/" + OverwhelmedStrangers + " in " + OverwhelmedRadius + "m"
                                               + " hold " + OverwhelmedHoldSeconds + "s sustain " + OverwhelmedSustainSeconds + "s gun " + OverwhelmedGunSeconds + "s" : "off")
                          + " meleePacing=" + (MeleePacing ? (MeleePacingRaiders ? "on" : "on, not raiders") : "off")
                          + " meleeReach=" + (MeleeReach ? "weapon -" + MeleeReachMargin + "m" : "off (1.5 m)")
                          + " shooting=" + (ShootingSkill ? ShootingRangePerStar + "m/star engage x" + ShootingEngageMult + " min " + ShootingEngageMin + "m radius "
                                            + ShootingHitRadius + "m" + (ShootingAimChest ? " chest" : " head") + " default " + ShootingDefaultStars + " raiders "
                                            + RaiderShootingStars[0, 0] + "-" + RaiderShootingStars[0, 1] + "/" + RaiderShootingStars[1, 0] + "-" + RaiderShootingStars[1, 1]
                                            + "/" + RaiderShootingStars[2, 0] + "-" + RaiderShootingStars[2, 1] : "off")
                          + " dialogLine=" + DialogLine + " voiceLines=" + VoiceLines + " muteVcInTalk=" + MuteVcInTalk
                          + " getUp=" + (XnpcBodyGetUp ? (XnpcGetUpGuard ? "guarded" : "unguarded") + (XnpcGetUpTrace ? "+trace" : "") : "off")
                          + " relax=" + (XnpcRelax ? "after " + XnpcRelaxCalmSeconds + "s, " + (XnpcRelaxRange > 0f ? XnpcRelaxRange + "m" : "sight range")
                                                     + (XnpcRelaxHideWeapon ? ", weapon hidden" : ", weapon shown") + (XnpcRelaxRaiders ? ", raiders too" : ", not raiders") : "off")
                          + " raiders=" + XnpcRaiderClasses.Count + " classes, ranged-lead " + string.Join("/", Array.ConvertAll(RaiderRangedShare, x => x.ToString("0.##", CultureInfo.InvariantCulture)))
                          + " (melee-lead to ranged beyond " + RaiderLeadRangedDist + "m for " + RaiderLeadRangedSeconds + "s)"
                          + " firearm " + RaiderFirearmChance + " ammo " + RaiderSpawnAmmo + "/" + RaiderSpawnArrows
                          + " levels=" + (RaiderXnpcLevels ? (RaiderLevelRegen ? "on" : "on, no regen") : "off") + " drop " + RaiderWeaponDropChance
                          + " vanillaDoors=" + RaiderVanillaDoors + " turretsAsEnemies=" + RaiderTurretsAsEnemies
                          + " gearSets=" + (RaiderGearSets.Count > 0 ? RaiderGearSets.Count.ToString() : "survivors'")
                          + " outfits=" + Outfits.Count + " (share " + SurvivorOutfitShare.ToString("0.##", CultureInfo.InvariantCulture) + "/" + RaiderOutfitShare.ToString("0.##", CultureInfo.InvariantCulture) + ")"
                          + " gearQuality=" + string.Join(",", Array.ConvertAll(GearQualityByTier, q => q[0] + "-" + q[1]))
                          + " homeHud=" + HomeStatusHud
                          + " noise=" + (BaseNoise ? "member " + NoisePerMember + " generator " + NoisePerGenerator + " workstation " + NoisePerFacility
                                                      + ", heat x" + NoiseHeatMult + " npc shot x" + NoiseNpcShotMult + ", half-life " + NoiseHalfLifeHours + "h"
                                                      + ", levels " + NoiseNoticeable + "/" + NoiseLoud + "/" + NoiseVeryLoud
                                                      + ", attention +" + AttentionPerNoiseHour + " -" + AttentionDecayPerHour + "/h" : "off")
                          + " baseAttacks=" + (BaseAttacks ? "gap " + BaseAttackGapDays + "d, lead " + BaseAttackLeadMinHours + "-" + BaseAttackLeadMaxHours + "h, size ("
                                                             + BaseAttackBase + "+gs*" + BaseAttackPerGamestage + ")*(1+" + BaseAttackPerMember + "/resident) " + BaseAttackMin + ".." + BaseAttackMax
                                                             + ", mix " + BaseAttackGameStages + ", ring +" + BaseAttackSpawnMin + ".." + BaseAttackSpawnMax + "m +-" + BaseAttackSpread + "deg"
                                                             + ", defend zone+" + BaseAttackDefendMargin + "m, home " + BaseAttackHomeMargin + "m, leave " + BaseAttackLeaveRange + "m"
                                                             + ", max " + BaseAttackMaxHours + "h, morale +" + BaseAttackWinMorale : "off")
                          + " watchPosts=" + (WatchPosts ? WatchMaxPosts + " posts, shift " + WatchShiftHours + "h (rest " + WatchRestHours + "h, " + WatchShiftAmmo
                                                           + " ammo), sight x" + WatchSightMult + ", placed after " + WatchPlaceSeconds + "s, attention -" + WatchAttentionCut
                                                           + "/post (max -" + WatchAttentionCutMax + ")" : "off")
                          + " awayAttacks=" + (BaseAttacks && BaseAttacksAway ? BaseAwayHoursBase + "h+" + BaseAwayHoursPer10 + "h/10, join " + BaseAttackJoinRange + "m, band "
                                                           + BaseAwayDamageBand + ", ammo 1/" + BaseAwayAmmoPer + " (x" + BaseAwayAmmoOverrun + "), death " + BaseAwayDeathChance
                                                           + " (cap " + BaseAwayDeathCap + ", unhurt x" + BaseAwayDeathUnhurt + "), morale +" + BaseAwayHeldMorale + "/-"
                                                           + BaseAwayDamagedMorale + "/-" + BaseAwayOverrunMorale : "off")
                          + " siege=" + (BaseAttacks && BaseSiege ? "-" + BaseSiegePenalty : "off")
                          + " defence=" + BaseDefenceBase + "+" + BaseDefencePerResident + "/resident+" + BaseDefencePerCombatStar + "/star+" + BaseDefencePerPost + "/post(max "
                          + BaseDefencePostCap + ")+" + BaseDefencePerBlock + "/trap(max " + BaseDefenceBlockCap + ") -" + BaseDefenceNoAmmo + " no ammo +-" + BaseDefenceMorale + " morale"
                          + " guardTower=preview " + GuardTowerGhost + ", legs +" + GuardTowerFill + ", cut " + GuardTowerCut
                          + " enclaves=" + (Enclaves ? EnclavesPerKm2.ToString("0.##", CultureInfo.InvariantCulture) + "/km2 (" + EnclaveMin + ".." + EnclaveMax + "), D max("
                                            + EnclaveSpacingMin + "," + EnclaveSpacingFactor.ToString("0.##", CultureInfo.InvariantCulture) + "*sqrt(A/N)) floor " + EnclaveSpacingFloor
                                            + ", hostile " + EnclaveHostileShare.ToString("0.##", CultureInfo.InvariantCulture) + " from " + EnclaveHostileMinStart + "m, ring "
                                            + EnclaveStartRing + " at " + EnclaveStartRingMin + "-" + EnclaveStartRingMax + "m, T" + EnclaveTierMin + "-" + EnclaveTierMax + ", "
                                            + EnclaveSizeMin + "-" + EnclaveSizeMax + "m, " + EnclaveKindWeights.Count + " districts, " + EnclavePoiExcludes.Count + " excludes, late seed "
                                            + (EnclaveLateSeed ? "on" : "off") + ", discover " + EnclaveDiscoverRange + "m, radio every " + EnclaveRadioRevealDays + "d within "
                                            + EnclaveRadioRange + "m" : "off")
                          + " enclavePeople=" + (Enclaves ? EnclaveGroupMin + "-" + EnclaveGroupMax + " (raiders " + EnclaveRaiderGroupMin + "-" + EnclaveRaiderGroupMax
                                                 + "), spawn " + EnclaveSpawnRange + "m (+" + EnclaveDespawnMargin + "m out, no pop-in " + EnclaveNoPopRange + "m, "
                                                 + EnclaveSpawnPerTick + "/s, hold " + (EnclaveHoldChunks ? "on" : "off") + "), restock " + (EnclaveDailyRestock ? "on" : "off")
                                                 + ", leash +" + EnclaveLeashMargin + "m, defend " + EnclaveDefendRadius + "m, standing from " + EnclaveStandingMin
                                                 + ", warning -" + EnclaveWarnPenalty + " (" + EnclaveWarnWindow.ToString("0.#", CultureInfo.InvariantCulture) + "s, hits "
                                                 + EnclaveHitDebounce.ToString("0.##", CultureInfo.InvariantCulture) + "s apart), threat " + EnclaveThreatBundlesMin + "-"
                                                 + EnclaveThreatBundlesMax + " bundles (grace " + EnclaveThreatGrace.ToString("0.#", CultureInfo.InvariantCulture) + "s), containers "
                                                 + (EnclaveContainerLock ? "locked" : "open") + ", wild spawns off " + EnclaveWildSpawnMargin + "m, " + EnclaveKindRows.Count + " kind rows" : "off")
                          + " enclaveDeals=" + (Enclaves ? "traders " + EnclaveTraderIdBase + "-" + (EnclaveTraderIdBase + 5) + ", buy x" + F2(EnclaveBuyNeutral) + "/" + F2(EnclaveBuyFriendly) + "/"
                                                + F2(EnclaveBuyAllied) + ", sell x" + F2(EnclaveSellNeutral) + "/" + F2(EnclaveSellFriendly) + "/" + F2(EnclaveSellAllied) + ", enlist "
                                                + EnclaveEnlistCostNeutral + "/" + EnclaveEnlistCostFriendly + "/" + EnclaveEnlistCostAllied + " dukes for " + EnclaveEnlistDays + "d (death -"
                                                + EnclaveEnlistDeathPenalty + "), recruit -" + EnclaveRecruitStanding + " (" + EnclaveRecruitMinLeft + " stay, every " + EnclaveRecruitCooldownDays + "d)" : "off")
                          + " enclaveRequests=" + (Enclaves ? F2(EnclaveRequestChanceNeutral) + "/" + F2(EnclaveRequestChanceFriendly) + "/" + F2(EnclaveRequestChanceAllied)
                                                + " a day, " + EnclaveRequestMaxOpen + " open, cooldown " + EnclaveRequestCooldownDays + "d, offers stand " + EnclaveOfferLapseDays
                                                + "d, due in " + EnclaveDueDaysItems + "d, generators from day " + EnclaveGeneratorFromDay + ", kinds " + WeightsText(EnclaveRequestKindWeights)
                                                + ", sizes " + WeightsText(EnclaveRequestSizeWeights) + ", gains +" + EnclaveGainResource + "/+" + EnclaveGainGenerator + "/guns "
                                                + string.Join(",", Array.ConvertAll(EnclaveGunTierPoints, x => x.ToString(CultureInfo.InvariantCulture))) + " x ("
                                                + F2(EnclaveGunQualityBase) + "+" + F2(EnclaveGunQualityStep) + "q) in " + EnclaveGunPointsMin + ".." + EnclaveGunPointsMax
                                                + ", losses no " + IntsText(EnclaveLossDecline) + " fail " + IntsText(EnclaveLossFail) + " lapse " + IntsText(EnclaveLossLapse) : "off")
                          + " enclaveRequests2=" + (Enclaves ? "search/infestation due in " + EnclaveDueDaysSearch + "d, missing " + EnclaveMissingMin + "-" + EnclaveMissingMax
                                                 + "m (alive " + F2(EnclaveMissingAlive) + " -" + F2(EnclaveMissingAliveFade) + "/day, min " + F2(EnclaveMissingAliveMin) + "), infestation within "
                                                 + EnclaveInfestRange + "m, siege " + F2(EnclaveSiegeChance) + "/day at " + EnclaveSiegeHourMin + "-" + EnclaveSiegeHourMax + "h within "
                                                 + EnclaveSiegeRange + "m, " + F2(EnclaveSiegeMinutes) + " min to come within " + EnclaveSiegeArriveRange + "m (max " + F2(EnclaveSiegeMaxHours)
                                                 + "h live), horde (" + F2(EnclaveSiegeBase) + "+" + F2(EnclaveSiegePerGS) + "gs)xpeople/4 in " + EnclaveSiegeMin + ".." + EnclaveSiegeMax
                                                 + " at " + EnclaveSiegeSpawnMin + "-" + EnclaveSiegeSpawnMax + "m (+-" + F2(EnclaveSiegeSpread) + " deg, " + EnclaveSiegeMinPlayerRange
                                                 + "m from players, no pop-in " + EnclaveSiegeNoPopRange + "m, resume " + F2(EnclaveSiegeResumeSeconds) + "s), leave +" + F2(EnclaveSiegeLeaveRoll)
                                                 + "/person, gains +" + EnclaveGainMissingAlive + "/+" + EnclaveGainMissingDead + "/+" + EnclaveGainInfestation + "/+" + EnclaveGainSiege : "off")
                          + " enclaveAllies=" + (Enclaves ? "bundles +" + EnclaveBenefitBundles + "/day, comfort +" + EnclaveBenefitMorale + ", beds +" + EnclaveBenefitBeds
                                                + ", clubs x" + F2(EnclaveBenefitClubXp) + ", training x" + F2(EnclaveBenefitTrainingXp) + " (cap x" + F2(EnclaveBenefitXpCap)
                                                + "), watch " + EnclaveWatchRange + "m (spread x" + F2(EnclaveWatchSpread) + "), morale +" + EnclaveAllyMorale + "/ally (cap "
                                                + EnclaveAllyMoraleCap + "), lost -" + EnclaveLostMorale + ", refill after " + EnclaveRefillDays + "d, ally reveal every "
                                                + EnclaveAllyRevealDays + "d" : "off")
                          + " infestations=" + (Infestations ? InfestDailyChance.ToString("0.##", CultureInfo.InvariantCulture) + "/day, cap "
                                                + InfestPerKm2.ToString("0.##", CultureInfo.InvariantCulture) + "/km2 (" + InfestMin + ".." + InfestMax + "), spread "
                                                + InfestSpreadChance.ToString("0.##", CultureInfo.InvariantCulture) + "/day after " + InfestSpreadDays + "d within " + InfestSpreadRange
                                                + "m, trader " + InfestTraderDistance + "m, home " + InfestMinDist + "-" + InfestMaxDist + "m (else x"
                                                + InfestOutsideWeight.ToString("0.##", CultureInfo.InvariantCulture) + "), tiers " + TierByDayText() + ", x"
                                                + InfestMultiplier.ToString("0.##", CultureInfo.InvariantCulture) + " (T" + InfestHighTier + "+ x"
                                                + InfestMultiplierHighTier.ToString("0.##", CultureInfo.InvariantCulture) + "), feral "
                                                + InfestFeralShare.ToString("0.##", CultureInfo.InvariantCulture) + ", special "
                                                + (InfestSpecial ? "radiated below gs " + InfestSpecialRadiatedBelowGS : "off") + ", discover " + InfestDiscoverRange + "m, radio "
                                                + InfestRadioRange + "m, margin " + InfestPlayerMargin + "m, re-arm " + InfestRearmPlayerRange + "m, near home "
                                                + InfestHomeRange + "m (morale " + HomeBandsText() + " for the nearest " + InfestHomeMoraleNests + ", at most "
                                                + InfestHomeMoraleCap + "; attention +" + F2(InfestHomeAttention) + "/h each for " + InfestHomeAttentionCap
                                                + "; known " + (InfestHomeKnown ? "at once" : "as usual") + ")" : "off")
                          + " sleeperCap=" + (SleeperCapFix ? "retry after " + SleeperCapRetryTicks + " ticks" : "off") + ", pause "
                                           + (SleeperCapPause && SleeperCapPauseMaxTicks > 0 ? "at most " + SleeperCapPauseMaxTicks + " ticks (infested rooms)" : "off")
                          + " looks=excluded " + SurvivorExcludeSkins.Count + " skins/" + SurvivorExcludeEyes.Count + " eyes/" + SurvivorExcludeHairs.Count
                          + " hairs, dyed hair F" + SurvivorDyedHairChanceFemale + "/M" + SurvivorDyedHairChanceMale + " of " + SurvivorDyedHairColors.Count
                          + " paints=body " + SurvivorBodyPaintChance + "/" + RaiderBodyPaintChance + " of " + SurvivorBodyPaints.Count + "/" + RaiderBodyPaints.Count
                          + ", face " + SurvivorFacePaintChance + "/" + RaiderFacePaintChance + " of " + SurvivorFacePaints.Count + "/" + RaiderFacePaints.Count
                          + ", tattoo " + SurvivorTattooChance + "/" + RaiderTattooChance + " of " + SurvivorTattoos.Count + "/" + RaiderTattoos.Count + " " + TattooMode);
            }
            catch (Exception e)
            {
                parsed = true;   // never retry every frame
                ScLog.Err("config parse failed, defaults in use: " + e.Message);
                Log.Exception(e);
            }
        }

        /// <summary>A comma list key: present = replaces the list (an empty value empties it), absent = keeps the default.</summary>
        private static void ParseList(DynamicProperties p, string key, List<string> into)
        {
            string s;
            if (!p.Values.TryGetValue(key, out s)) return;
            into.Clear();
            if (!string.IsNullOrEmpty(s)) into.AddRange(SplitList(s));
        }

        private static IEnumerable<string> SplitList(string s)
        {
            foreach (string part in s.Split(','))
            {
                string t = part.Trim();
                if (t.Length > 0) yield return t;
            }
        }

        /// <summary>EnclaveKindWeights "rural:1.5,...,downtown:0": replaces the table; an unreadable entry is skipped with a warning, none readable keeps it.</summary>
        private static void ParseKindWeights(string s)
        {
            var parsed = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            foreach (string part in SplitList(s))
            {
                int c = part.IndexOf(':');
                float w;
                if (c <= 0 || !float.TryParse(part.Substring(c + 1).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out w) || w < 0f)
                {
                    ScLog.Warn("config EnclaveKindWeights entry '" + part + "' is not district:weight (a weight of 0 or more) - skipped");
                    continue;
                }
                parsed[part.Substring(0, c).Trim().ToLowerInvariant()] = w;
            }
            if (parsed.Count == 0) { ScLog.Warn("config EnclaveKindWeights '" + s + "' names no district:weight - kept"); return; }
            EnclaveKindWeights.Clear();
            foreach (var kv in parsed) EnclaveKindWeights[kv.Key] = kv.Value;
        }

        /// <summary>
        /// EnclaveKindRows "farm:growers,...,refinery:roadcrew" (E2): replaces the table; an entry whose kind is not one of
        /// ScEnclaves.Kinds is skipped with a warning, none readable keeps the table.
        /// </summary>
        private static void ParseKindRows(string s)
        {
            var parsed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string part in SplitList(s))
            {
                int c = part.IndexOf(':');
                string kind = c > 0 ? part.Substring(c + 1).Trim().ToLowerInvariant() : "";
                if (c <= 0 || Array.IndexOf(ScEnclaves.Kinds, kind) < 0)
                {
                    ScLog.Warn("config EnclaveKindRows entry '" + part + "' is not row:kind (kinds: " + string.Join(", ", ScEnclaves.Kinds) + ") - skipped");
                    continue;
                }
                parsed[part.Substring(0, c).Trim()] = kind;
            }
            if (parsed.Count == 0) { ScLog.Warn("config EnclaveKindRows '" + s + "' names no row:kind - kept"); return; }
            EnclaveKindRows.Clear();
            foreach (var kv in parsed) EnclaveKindRows[kv.Key] = kv.Value;
        }

        /// <summary>
        /// E4's "name:weight,..." tables (EnclaveRequestKindWeights, EnclaveRequestSizeWeights): replaces the table; a name not
        /// among 'names' or a weight below 0 is skipped with a warning, none readable keeps the table. A name left out weighs 0.
        /// </summary>
        private static void ParseNamedWeights(string key, string s, string[] names, Dictionary<string, float> into)
        {
            var parsed = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            foreach (string part in SplitList(s))
            {
                int c = part.IndexOf(':');
                string name = c > 0 ? part.Substring(0, c).Trim().ToLowerInvariant() : "";
                float w;
                if (c <= 0 || Array.IndexOf(names, name) < 0 || !float.TryParse(part.Substring(c + 1).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out w) || w < 0f)
                {
                    ScLog.Warn("config " + key + " entry '" + part + "' is not name:weight (names: " + string.Join(", ", names) + "; a weight of 0 or more) - skipped");
                    continue;
                }
                parsed[name] = w;
            }
            if (parsed.Count == 0) { ScLog.Warn("config " + key + " '" + s + "' names no name:weight - kept"); return; }
            into.Clear();
            foreach (var kv in parsed) into[kv.Key] = kv.Value;
        }

        /// <summary>"resource:6,gun:3,generator:1" for the config line.</summary>
        private static string WeightsText(Dictionary<string, float> weights)
        {
            var parts = new List<string>();
            foreach (var kv in weights) parts.Add(kv.Key + ":" + F2(kv.Value));
            return string.Join(",", parts.ToArray());
        }

        /// <summary>"5/5/12" for the config line.</summary>
        private static string IntsText(int[] v)
        {
            return string.Join("/", Array.ConvertAll(v, x => x.ToString(CultureInfo.InvariantCulture)));
        }

        /// <summary>InfestTierByDay "1:1-2,8:1-3,...": replaces the table (sorted by day); an unreadable entry is skipped with a warning, none readable keeps it.</summary>
        private static void ParseTierByDay(string s)
        {
            var parsed = new List<int[]>();
            foreach (string part in SplitList(s))
            {
                int c = part.IndexOf(':'), dash = part.IndexOf('-', c + 1);
                int day, lo, hi;
                if (c <= 0 || dash <= c + 1 || !int.TryParse(part.Substring(0, c).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out day)
                    || !int.TryParse(part.Substring(c + 1, dash - c - 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out lo)
                    || !int.TryParse(part.Substring(dash + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out hi) || day < 0 || lo < 0 || hi < lo)
                {
                    ScLog.Warn("config InfestTierByDay entry '" + part + "' is not day:min-max (min at most max) - skipped");
                    continue;
                }
                parsed.Add(new[] { day, lo, hi });
            }
            if (parsed.Count == 0) { ScLog.Warn("config InfestTierByDay '" + s + "' names no day:min-max - kept"); return; }
            parsed.Sort((a, b) => a[0].CompareTo(b[0]));
            InfestTierByDay.Clear();
            InfestTierByDay.AddRange(parsed);
        }

        /// <summary>The tiers the daily roll takes on this day: the last InfestTierByDay entry at or before it (before the first, the first).</summary>
        public static void InfestTierWindow(int day, out int min, out int max)
        {
            min = 1;
            max = 2;
            for (int i = 0; i < InfestTierByDay.Count; i++)
            {
                int[] e = InfestTierByDay[i];
                if (i > 0 && e[0] > day) break;
                min = e[1];
                max = e[2];
            }
        }

        private static string TierByDayText()
        {
            var parts = new List<string>();
            foreach (int[] e in InfestTierByDay) parts.Add(e[0] + ":" + e[1] + "-" + e[2]);
            return string.Join(",", parts.ToArray());
        }

        /// <summary>
        /// InfestHomeMoraleBands "150:5,300:3,500:2" (I3): replaces the table, sorted by distance. An entry that is not metres:cost
        /// (metres 1 to 20000, a cost of 0 to 100) or repeats a distance is skipped with a warning; none readable keeps the table.
        /// </summary>
        private static void ParseHomeBands(string s)
        {
            var parsed = new List<int[]>();
            foreach (string part in SplitList(s))
            {
                int c = part.IndexOf(':');
                int metres, cost;
                if (c <= 0 || !int.TryParse(part.Substring(0, c).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out metres)
                    || !int.TryParse(part.Substring(c + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out cost)
                    || metres < 1 || metres > 20000 || cost < 0 || cost > 100)
                {
                    ScLog.Warn("config InfestHomeMoraleBands entry '" + part + "' is not metres:cost (metres 1 to 20000, a cost of 0 to 100) - skipped");
                    continue;
                }
                if (parsed.Exists(b => b[0] == metres))
                {
                    ScLog.Warn("config InfestHomeMoraleBands entry '" + part + "' repeats " + metres + " m - skipped");
                    continue;
                }
                parsed.Add(new[] { metres, cost });
            }
            if (parsed.Count == 0) { ScLog.Warn("config InfestHomeMoraleBands '" + s + "' names no metres:cost - kept"); return; }
            parsed.Sort((a, b) => a[0].CompareTo(b[0]));
            InfestHomeMoraleBands.Clear();
            InfestHomeMoraleBands.AddRange(parsed);
        }

        /// <summary>I3: the morale points a nest this many metres from home costs: the first band it fits (nearest first), 0 past the last.</summary>
        public static int InfestHomeCost(int metres)
        {
            for (int i = 0; i < InfestHomeMoraleBands.Count; i++) if (metres <= InfestHomeMoraleBands[i][0]) return InfestHomeMoraleBands[i][1];
            return 0;
        }

        /// <summary>"150:5,300:3,500:2" for the config line and the console.</summary>
        public static string HomeBandsText()
        {
            var parts = new List<string>();
            foreach (int[] e in InfestHomeMoraleBands) parts.Add(e[0] + ":" + e[1]);
            return string.Join(",", parts.ToArray());
        }

        /// <summary>RaiderRangedShare "0.3,0.4,0.5" (the ranged-lead chance): one share per class tier; a shorter list repeats its last value, a bad entry keeps the default.</summary>
        private static void ParseRangedShare(string s)
        {
            string[] parts = s.Split(',');
            float last = -1f;
            for (int i = 0; i < RaiderRangedShare.Length; i++)
            {
                float v;
                if (i < parts.Length && float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                {
                    if (v < 0f || v > 1f) { ScLog.Warn("config RaiderRangedShare entry " + v + " out of range [0,1], keeping " + RaiderRangedShare[i]); continue; }
                    RaiderRangedShare[i] = last = v;
                }
                else if (i >= parts.Length && last >= 0f) RaiderRangedShare[i] = last;
                else ScLog.Warn("config RaiderRangedShare '" + s + "': entry " + (i + 1) + " unreadable, keeping " + RaiderRangedShare[i]);
            }
        }

        /// <summary>
        /// "1,2,3" into a per-tier array (T1, T2, T3): every entry within [min,max], else the whole array is kept; a short
        /// list repeats its last entry for the higher tiers.
        /// </summary>
        private static void ParseTierInts(string key, string s, int[] into, int min, int max)
        {
            string[] parts = s.Split(',');
            var v = new int[into.Length];
            for (int i = 0; i < into.Length; i++)
            {
                string part = parts[Math.Min(i, parts.Length - 1)].Trim();
                int x;
                if (!int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out x) || x < min || x > max)
                {
                    ScLog.Warn("config " + key + " '" + s + "': entry " + (i + 1) + " is not a number in [" + min + "," + max + "] - kept");
                    return;
                }
                v[i] = x;
            }
            Array.Copy(v, into, into.Length);
        }

        /// <summary>"0.04,0.06,0.08" into a per-tier array (T1, T2, T3), as ParseTierInts.</summary>
        private static void ParseTierFloats(string key, string s, float[] into, float min, float max)
        {
            string[] parts = s.Split(',');
            var v = new float[into.Length];
            for (int i = 0; i < into.Length; i++)
            {
                string part = parts[Math.Min(i, parts.Length - 1)].Trim();
                float x;
                if (!float.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out x) || x < min || x > max)
                {
                    ScLog.Warn("config " + key + " '" + s + "': entry " + (i + 1) + " is not a number in [" + min + "," + max + "] - kept");
                    return;
                }
                v[i] = x;
            }
            Array.Copy(v, into, into.Length);
        }

        private static void ParseInt(DynamicProperties p, string key, ref int v, int min, int max)
        {
            if (!p.Values.ContainsKey(key)) return;
            int tmp = v;
            p.ParseInt(key, ref tmp);
            if (tmp < min || tmp > max) { ScLog.Warn("config " + key + "=" + tmp + " out of range [" + min + "," + max + "], keeping " + v); return; }
            v = tmp;
        }

        private static void ParseFloat(DynamicProperties p, string key, ref float v, float min, float max)
        {
            if (!p.Values.ContainsKey(key)) return;
            float tmp = v;
            p.ParseFloat(key, ref tmp);
            if (tmp < min || tmp > max) { ScLog.Warn("config " + key + "=" + tmp + " out of range [" + min + "," + max + "], keeping " + v); return; }
            v = tmp;
        }

        /// <summary>A factor for the config line ("1.35"), invariant.</summary>
        private static string F2(float f) { return f.ToString("0.##", CultureInfo.InvariantCulture); }

        /// <summary>
        /// RaiderShootingStars "1-2,2-4,4-6": low-high Shooting stars for tiers T1, T2, T3 (a single number is both). Anything
        /// unreadable, outside 1..7 or low above high keeps the whole table as it was.
        /// </summary>
        private static void ParseRaiderShooting(string s)
        {
            string[] parts = s.Split(',');
            if (parts.Length != 3) { ScLog.Warn("config RaiderShootingStars '" + s + "' needs three tiers (\"1-2,2-4,4-6\") - kept"); return; }
            var t = new int[3, 2];
            for (int i = 0; i < 3; i++)
            {
                string[] lh = parts[i].Trim().Split('-');
                int lo, hi;
                if (lh.Length < 1 || lh.Length > 2 || !int.TryParse(lh[0].Trim(), out lo) || !int.TryParse(lh[lh.Length - 1].Trim(), out hi)
                    || lo < 1 || hi > 7 || lo > hi)
                {
                    ScLog.Warn("config RaiderShootingStars '" + s + "' tier " + (i + 1) + " is not low-high within 1..7 - kept");
                    return;
                }
                t[i, 0] = lo;
                t[i, 1] = hi;
            }
            for (int i = 0; i < 3; i++) { RaiderShootingStars[i, 0] = t[i, 0]; RaiderShootingStars[i, 1] = t[i, 1]; }
        }

        /// <summary>FacilityUpgradeCost "4,8,12,16": materials for each level above 1. Any unreadable or negative entry keeps the whole default.</summary>
        private static void ParseUpgradeCost(string s)
        {
            var list = new List<int>();
            foreach (string part in SplitList(s))
            {
                int n;
                if (!int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) || n < 0)
                {
                    ScLog.Warn("config FacilityUpgradeCost '" + s + "': entry '" + part + "' unreadable - keeping " + string.Join(",", Array.ConvertAll(FacilityUpgradeCost, x => x.ToString())));
                    return;
                }
                list.Add(n);
            }
            if (list.Count > 0) FacilityUpgradeCost = list.ToArray();
        }

        private static void ParseSuppliesFavours(string s)
        {
            var list = new List<KeyValuePair<Resource, int>>();
            foreach (string part in s.Split('|'))
            {
                string[] kv = part.Split(':');
                Resource r; int n;
                if (kv.Length == 2 && ScNames.TryResource(kv[0], out r) && int.TryParse(kv[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n) && n > 0)
                    list.Add(new KeyValuePair<Resource, int>(r, n));
                else ScLog.Warn("config SuppliesFavours: cannot parse '" + part + "'");
            }
            if (list.Count > 0) { SuppliesFavours.Clear(); SuppliesFavours.AddRange(list); }
        }

        private static void ParseOverrides(string s)
        {
            foreach (string part in s.Split(','))
            {
                string[] kv = part.Split(':');
                int n;
                if (kv.Length == 2 && int.TryParse(kv[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
                    ValueOverrides[kv[0].Trim()] = n;
                else ScLog.Warn("config ValueOverrides: cannot parse '" + part + "'");
            }
        }

        /// <summary>"family,tier[,alias][,bands=a/b/c][,group=g]" for one XnpcWeapons entry; null (with a warning) when it cannot be read.</summary>
        private static XnpcWeaponDef ParseXnpcWeapon(string item, string spec)
        {
            if (string.IsNullOrEmpty(item) || string.IsNullOrEmpty(spec)) return null;
            string[] parts = spec.Split(',');
            int tier;
            string family = parts[0].Trim().ToLowerInvariant();
            if (parts.Length < 2 || (family != "melee" && family != "bow" && family != "firearm")
                || !int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out tier) || tier < 0 || tier > 2)
            {
                ScLog.Warn("config XnpcWeapons: cannot parse " + item + "='" + spec + "' (want family melee|bow|firearm, tier 0-2)");
                return null;
            }
            var d = new XnpcWeaponDef { Item = item.Trim(), Family = family, Tier = tier, Alias = parts.Length > 2 ? parts[2].Trim() : "" };
            for (int i = 3; i < parts.Length; i++)
            {
                string f = parts[i].Trim();
                if (f.StartsWith("group=", StringComparison.OrdinalIgnoreCase)) { d.Pool = f.Substring(6).Trim(); continue; }
                if (!f.StartsWith("bands=", StringComparison.OrdinalIgnoreCase)) continue;
                d.Bands = ParseWeights(f.Substring(6), 3);
                if (d.Bands == null) ScLog.Warn("config XnpcWeapons: " + item + " has an unreadable '" + f + "' (want bands=a/b/c, each 0 or more, one above 0) - weighted by its tier");
            }
            return d;
        }

        /// <summary>n numbers of 0 or more separated by '/', at least one above 0; null when they are not.</summary>
        private static float[] ParseWeights(string s, int n)
        {
            string[] parts = (s ?? "").Split('/');
            if (parts.Length != n) return null;
            var w = new float[n];
            bool any = false;
            for (int i = 0; i < n; i++)
            {
                if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out w[i]) || w[i] < 0f || float.IsNaN(w[i]) || float.IsInfinity(w[i])) return null;
                if (w[i] > 0f) any = true;
            }
            return any ? w : null;
        }

        /// <summary>"Primitive:0,Assassin:1,Raider:2" - a 0-2 number per vanilla armor set (GearSetTiers, GearSetLastTier).</summary>
        private static void ParseSetTiers(string s, Dictionary<string, int> into, string key)
        {
            into.Clear();
            if (string.IsNullOrEmpty(s)) return;
            foreach (string part in s.Split(','))
            {
                string[] kv = part.Split(':');
                int c;
                if (kv.Length == 2 && kv[0].Trim().Length > 0 && int.TryParse(kv[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out c) && c >= 0 && c <= 2)
                    into[kv[0].Trim()] = c;
                else if (part.Trim().Length > 0) ScLog.Warn("config " + key + ": cannot parse '" + part + "' (want Set:0-2)");
            }
        }

        /// <summary>"1/0.5/0.25,0.6/1/0.6,0.35/0.7/1" - per band, the light/medium/heavy set weights.</summary>
        private static void ParseGearTierWeights(string s)
        {
            string[] rows = s.Split(',');
            var w = new float[3][];
            for (int i = 0; i < 3; i++)
            {
                w[i] = rows.Length == 3 ? ParseWeights(rows[i], 3) : null;
                if (w[i] == null) { ScLog.Warn("config GearTierWeights: cannot parse '" + s + "' (want three a/b/c groups) - using the defaults"); return; }
            }
            GearTierWeights = w;
        }

        /// <summary>"1-2,2-4,3-5" - the armor quality range per band.</summary>
        private static void ParseGearQuality(string s)
        {
            string[] rows = s.Split(',');
            var q = new int[3][];
            for (int i = 0; i < 3; i++)
            {
                string[] mm = rows.Length == 3 ? rows[i].Split('-') : new string[0];
                int a, b;
                if (mm.Length != 2 || !int.TryParse(mm[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out a)
                    || !int.TryParse(mm[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out b) || a < 1 || b > 6 || a > b)
                {
                    ScLog.Warn("config GearQualityByTier: cannot parse '" + s + "' (want three min-max ranges within 1-6) - using the defaults");
                    return;
                }
                q[i] = new[] { a, b };
            }
            GearQualityByTier = q;
        }

        /// <summary>One Outfits entry (OutfitDef); null (with a warning) when it has no body armor item or cannot be read.</summary>
        private static OutfitDef ParseOutfit(string id, string spec)
        {
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(spec)) return null;
            var o = new OutfitDef { Id = id.Trim() };
            foreach (string part in spec.Split(','))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                string k = part.Substring(0, eq).Trim().ToLowerInvariant(), v = part.Substring(eq + 1).Trim();
                switch (k)
                {
                    case "head": o.Items[0] = v; break;
                    case "chest": o.Items[1] = v; break;
                    case "hands": o.Items[2] = v; break;
                    case "feet": o.Items[3] = v; break;
                    case "sex":
                        string sx = v.ToLowerInvariant();
                        if (sx == "male") o.Sex = 1;
                        else if (sx == "female") o.Sex = 2;
                        else if (sx == "any" || sx == "both") o.Sex = 0;
                        else { ScLog.Warn("config Outfits: " + id + " sex '" + v + "' is not any, male or female - left out"); return null; }
                        break;
                    case "side":
                        string sd = v.ToLowerInvariant();
                        if (sd == "survivor" || sd == "survivors") { o.Survivors = true; o.Raiders = false; }
                        else if (sd == "raider" || sd == "raiders") { o.Survivors = false; o.Raiders = true; }
                        else if (sd == "both") { o.Survivors = true; o.Raiders = true; }
                        else { ScLog.Warn("config Outfits: " + id + " side '" + v + "' is not survivor, raider or both - left out"); return null; }
                        break;
                    case "tier":
                        int tier;
                        if (!int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out tier) || tier < 0 || tier > 2) { ScLog.Warn("config Outfits: " + id + " tier '" + v + "' is not 0-2 - left out"); return null; }
                        o.Tier = tier;
                        break;
                    case "weight":
                        float w;
                        if (!float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out w) || w < 0f || float.IsNaN(w) || float.IsInfinity(w)) { ScLog.Warn("config Outfits: " + id + " weight '" + v + "' is not a number of 0 or more - left out"); return null; }
                        o.Weight = w;
                        break;
                    case "mix": o.Mix = v.Equals("none", StringComparison.OrdinalIgnoreCase) ? "" : v; break;
                }
            }
            if (o.Items[1].Length == 0) { ScLog.Warn("config Outfits: " + id + " has no chest item - left out"); return null; }
            return o;
        }

        private static TraitDef ParseTrait(string name, string spec)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var t = new TraitDef { Name = name.Trim() };
            if (string.IsNullOrEmpty(spec)) return t;
            foreach (string part in spec.Split(','))
            {
                string[] kv = part.Split('=');
                if (kv.Length != 2) continue;
                string k = kv[0].Trim().ToLowerInvariant(), v = kv[1].Trim();
                try
                {
                    switch (k)
                    {
                        case "weight": t.Weight = Math.Max(0, int.Parse(v, CultureInfo.InvariantCulture)); break;
                        case "food": t.FoodMult = float.Parse(v, CultureInfo.InvariantCulture); break;
                        case "morale": t.MoraleOffset = int.Parse(v, CultureInfo.InvariantCulture); break;
                        case "mood": t.MoodOffset = int.Parse(v, CultureInfo.InvariantCulture); break;
                        case "timid": t.Timid = v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase); break;
                        case "xp": t.XpMult = float.Parse(v, CultureInfo.InvariantCulture); break;
                        case "yield": t.RunYieldMult = float.Parse(v, CultureInfo.InvariantCulture); break;
                        case "spec": { Specialty sp; if (ScNames.TrySpecialty(v, out sp)) t.PreferredSpecialty = sp; break; }
                        case "skill":
                        {
                            string[] sk = v.Split(':');
                            Skill skill;
                            if (sk.Length == 2 && ScNames.TrySkill(sk[0], out skill))
                            {
                                t.HasSkill = true;
                                t.SkillBonus = skill;
                                t.SkillBonusStars = int.Parse(sk[1], CultureInfo.InvariantCulture);
                            }
                            break;
                        }
                    }
                }
                catch (Exception)
                {
                    ScLog.Warn("config trait " + name + ": cannot parse '" + part + "'");
                }
            }
            return t;
        }
    }
}
