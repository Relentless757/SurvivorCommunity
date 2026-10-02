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
    /// token as a short name the console accepts.
    /// </summary>
    public sealed class XnpcWeaponDef
    {
        public string Item = "";
        public string Family = "";
        public int Tier;
        public string Alias = "";
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
        /// <summary>Points (EconomicValue x count) that make one bundle, per resource.</summary>
        public static readonly int[] PointsPerBundle = { 400, 300, 2000, 1000, 600 };
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
        /// <summary>Seconds a stationed resident may make no progress before being moved back to the post.</summary>
        public static int StuckSeconds = 12;
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
        public static int SurvivorGearQualityMin = 1;
        public static int SurvivorGearQualityMax = 4;
        public static float SurvivorBaldChance = 0.15f;
        public static float SurvivorFacialHairChance = 0.4f;
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
        /// <summary>OBSOLETE since M10: how often a hostile console spawn got a ranged class weapon (the old raider classes).
        /// Still parsed so an old blocks.xml loads; nothing reads it.</summary>
        public static float SurvivorRangedMainChance = 0.4f;
        /// <summary>The vanilla weapons the community's 0-XNPCCore survivors may carry (XnpcWeapons class, generated).</summary>
        public static readonly List<XnpcWeaponDef> XnpcWeapons = new List<XnpcWeaponDef>();
        /// <summary>Rounds of its gun's basic ammo a new 0-XNPCCore survivor carries in its inventory window, once per
        /// entity (decision D3: finite ammo). 0 = none.</summary>
        public static int XnpcSpawnAmmo = 30;
        /// <summary>The same for a bow or crossbow: arrows or bolts.</summary>
        public static int XnpcSpawnArrows = 20;
        // ---- ammo resupply (ScAmmo, migration M7.3, decision D3) ----
        /// <summary>A member (or an outpost's stand-in) whose gun runs low is resupplied from the stockpile's ammo, but only
        /// inside the home zone or at one of the community's outposts. False: they only ever have what they carry.</summary>
        public static bool XnpcResupply = true;
        /// <summary>Resupplied when the gun's rounds (magazine plus every store SCore reloads from) are below this.</summary>
        public static int XnpcResupplyBelow = 10;
        /// <summary>Rounds of the gun's basic ammo one resupply hands over (XnpcResupplyArrows: arrows or bolts for a bow).</summary>
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
        /// <summary>Seconds the state outlasts the count dropping below the threshold (no weapon flapping at the edge).</summary>
        public static float OverwhelmedHoldSeconds = 3f;
        // ---- 0-XNPCCore's survivors (ScXnpc, Patches_Xnpc) ----
        /// <summary>The 0-XNPCCore survivor classes the community owns: SC's looks, names, healing and aim pose on them.</summary>
        public static readonly List<string> XnpcSurvivorClasses = new List<string>(ScXnpc.DefaultClasses);
        /// <summary>Their knock-down recovery runs as a player's (Patches_Sdcs ragdoll fixes); the raiders' too (every switch below covers both sets).</summary>
        public static bool XnpcBodyGetUp = true;
        /// <summary>Their jumps fire the player animator's jump trigger (Patches_Sdcs.Patch_JumpAnim).</summary>
        public static bool XnpcBodyJumpAnim = true;
        /// <summary>Their hit colliders are put back on the hittable layer after spawn and rebuilds (Patches_Sdcs.FixColliderLayers).</summary>
        public static bool XnpcBodyColliderLayers = true;
        /// <summary>The XNPC body's Model offset (m): 0 puts its feet on the ground (the player prefab ships -0.08).</summary>
        public static float XnpcModelY = 0f;
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
        /// <summary>Rounds of its gun's basic ammo a raider starts with, in its bag, once (M10-D2: never resupplied).</summary>
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
            ValueOverrides.Clear();
            BedBlocks.Clear();
            BedBlocksExclude.Clear();
            BedsBySleepingBag = true;
            ScanRadius = 0;
            LinkedStorageRows = 0;
            FuelItems.Clear();
            FacilityBlocks.Clear();
            PostRadius = 8;
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
            SurvivorBaldChance = 0.15f;
            SurvivorFacialHairChance = 0.4f;
            SurvivorAimPose = true;
            SurvivorAimVisualOnly = true;
            SurvivorAimHoldSeconds = 2f;
            SurvivorSecondWeapon = true;
            SurvivorFirearmChance = 0.12f;
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
            OverwhelmedHoldSeconds = 3f;
            XnpcSurvivorClasses.Clear();
            XnpcSurvivorClasses.AddRange(ScXnpc.DefaultClasses);
            XnpcBodyGetUp = true;
            XnpcBodyJumpAnim = true;
            XnpcBodyColliderLayers = true;
            XnpcModelY = 0f;
            XnpcRaiderClasses.Clear();
            XnpcRaiderClasses.AddRange(ScXnpc.DefaultRaiderClasses);
            RaiderRangedShare[0] = 0.3f; RaiderRangedShare[1] = 0.4f; RaiderRangedShare[2] = 0.5f;
            RaiderLeadRangedDist = 8f;
            RaiderLeadRangedSeconds = 3f;
            RaiderFirearmChance = 0.12f;
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
            OutpostAttacks = false; OutpostGoodNews = true; OutpostGoodNewsChance = 0.04f; OutpostSecBase = 35; OutpostSecPerStaffer = 8; OutpostSecPerCombatStar = 2; OutpostSecPerTier = 4; OutpostSecVisited = 5; OutpostSecNoAmmo = 10; OutpostSecPerDefense = 2; OutpostSecDefenseCap = 30; OutpostSecBonusFade = 1f; OutpostSecWinBonus = 10f; OutpostSecBonusMax = 30f; OutpostAttackChance = 0.08f; OutpostAttackHours = 10; OutpostAttackBase = 3f; OutpostAttackPerGamestage = 0.04f; OutpostAttackMax = 16; OutpostAttendedBonus = 20; OutpostDamageBand = 35; OutpostDamagedDays = 2; OutpostRepairMats = 2; OutpostSiegePenalty = 15; OutpostWinMorale = 4f; OutpostNeglectDays = 10; OutpostRaiderGamestage = 30; OutpostRaiderChance = 0.03f; OutpostDemandAmmo = 2; OutpostDemandFood = 2;
            ScPoiTypes.LoadDefaults();
            Defaults();
        }

        private static void Defaults()
        {
            BedBlocks.AddRange(new[] { "bedroll", "bed02", "bunkBed", "bedMade", "bedMessy", "mattressFlatThin", "hospitalBed" });   // name PREFIXES (3.2 beds are bed02Blue, bunkBedMadeWhite, bedMadeNoFrameGrey ...)
            BedBlocksExclude.AddRange(new[] { "bunkBedFrame", "mattressCurled" });
            FuelItems.Add("ammoGasCan");
            FacilityBlocks.AddRange(new[] { "campfire", "workbench", "forge", "chemistryStation", "cementMixer" });
            SuppliesFavours.Add(new KeyValuePair<Resource, int>(Resource.Food, 15));
            SuppliesFavours.Add(new KeyValuePair<Resource, int>(Resource.Meds, 5));
            SuppliesFavours.Add(new KeyValuePair<Resource, int>(Resource.Ammo, 60));
            OutpostDefenseBlocks.AddRange(new[] { "trapSpikes", "barbedWire", "barbedFence", "bladeTrap", "dartTrap", "electricfencepost",
                                                  "autoTurret", "shotgunTurret", "mineCandyTin", "mineCookingPot", "mineHubcap", "mineAirFilter" });
            SurvivorGearSets.AddRange(new[] { "Primitive", "Athletic", "Enforcer", "Lumberjack", "Preacher", "Rogue", "Assassin", "Biker", "Commando", "Farmer", "Ranger", "Scavenger", "Miner", "Nerd", "Nomad", "Raider" });
            // M10-D7: raiders dress rougher than survivors (the design's suggested list, accepted 2026-10-01)
            RaiderGearSets.AddRange(new[] { "Raider", "Biker", "Enforcer", "Assassin", "Rogue", "Commando", "Nomad", "Scavenger", "Primitive" });
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
                for (int i = 0; i < 5; i++)
                {
                    int v = PointsPerBundle[i];
                    ParseInt(p, "PointsPerBundle" + (Resource)i, ref v, 1, 1000000);
                    PointsPerBundle[i] = v;
                }
                ParseInt(p, "PostRadius", ref PostRadius, 2, 40);
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
                ParseInt(p, "StuckSeconds", ref StuckSeconds, 3, 600);
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
                ParseFloat(p, "SurvivorBaldChance", ref SurvivorBaldChance, 0f, 1f);
                ParseFloat(p, "SurvivorFacialHairChance", ref SurvivorFacialHairChance, 0f, 1f);
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
                bool xnpcGetUp = XnpcBodyGetUp;
                if (p.Values.ContainsKey("XnpcBodyGetUp")) p.ParseBool("XnpcBodyGetUp", ref xnpcGetUp);
                XnpcBodyGetUp = xnpcGetUp;
                bool xnpcJump = XnpcBodyJumpAnim;
                if (p.Values.ContainsKey("XnpcBodyJumpAnim")) p.ParseBool("XnpcBodyJumpAnim", ref xnpcJump);
                XnpcBodyJumpAnim = xnpcJump;
                bool xnpcColliders = XnpcBodyColliderLayers;
                if (p.Values.ContainsKey("XnpcBodyColliderLayers")) p.ParseBool("XnpcBodyColliderLayers", ref xnpcColliders);
                XnpcBodyColliderLayers = xnpcColliders;
                ParseFloat(p, "XnpcModelY", ref XnpcModelY, -0.3f, 0.3f);
                ParseFloat(p, "RaiderFirearmChance", ref RaiderFirearmChance, 0f, 1f);
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
                if (p.Values.TryGetValue("FacilityBlocks", out s) && !string.IsNullOrEmpty(s)) { FacilityBlocks.Clear(); FacilityBlocks.AddRange(SplitList(s)); }
                if (p.Values.TryGetValue("SuppliesFavours", out s) && !string.IsNullOrEmpty(s)) ParseSuppliesFavours(s);
                if (p.Values.TryGetValue("ValueOverrides", out s) && !string.IsNullOrEmpty(s)) ParseOverrides(s);
                if (p.Values.TryGetValue("SurvivorGearSets", out s) && !string.IsNullOrEmpty(s)) { SurvivorGearSets.Clear(); SurvivorGearSets.AddRange(SplitList(s)); }
                if (p.Values.TryGetValue("XnpcSurvivorClasses", out s) && !string.IsNullOrEmpty(s)) { XnpcSurvivorClasses.Clear(); XnpcSurvivorClasses.AddRange(SplitList(s)); }
                if (p.Values.TryGetValue("XnpcRaiderClasses", out s) && !string.IsNullOrEmpty(s)) { XnpcRaiderClasses.Clear(); XnpcRaiderClasses.AddRange(SplitList(s)); }
                // an empty value is meaningful here: raiders then dress from SurvivorGearSets
                if (p.Values.TryGetValue("RaiderGearSets", out s)) { RaiderGearSets.Clear(); if (!string.IsNullOrEmpty(s)) RaiderGearSets.AddRange(SplitList(s)); }
                if (p.Values.TryGetValue("RaiderRangedShare", out s) && !string.IsNullOrEmpty(s)) ParseRangedShare(s);

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
                parsed = true;
                ScLog.Out("config: MaxMembers=" + MaxMembers + " MaxFollowers=" + MaxFollowers + " FoodPerMemberDay=" + FoodPerMemberDay
                          + " AmmoPerRangedDay=" + AmmoPerRangedDay + " PointsPerBundle=[" + string.Join(",", Array.ConvertAll(PointsPerBundle, x => x.ToString())) + "]"
                          + " PostRadius=" + PostRadius + " LowMoraleDays=" + LowMoraleDays + " traits=" + Traits.Count
                          + " beds=" + BedBlocks.Count + " facilities=" + FacilityBlocks.Count + " favours=" + SuppliesFavours.Count
                          + " secondWeapon=" + SurvivorSecondWeapon + " firearmChance=" + SurvivorFirearmChance + " rangedMainChance=" + SurvivorRangedMainChance
                          + " xnpcWeapons=" + XnpcWeapons.Count + " xnpcSpawnAmmo=" + XnpcSpawnAmmo + "/" + XnpcSpawnArrows
                          + " resupply=" + (XnpcResupply ? "below " + XnpcResupplyBelow + " +" + XnpcResupplyAmmo + "/" + XnpcResupplyArrows
                                            + " every " + XnpcResupplyCooldown + "s x" + XnpcResupplyPointsMult : "off")
                          + " overwhelmed=" + (Overwhelmed ? OverwhelmedMin + ".." + OverwhelmedMax + "/" + OverwhelmedStrangers + " in " + OverwhelmedRadius + "m" : "off")
                          + " raiders=" + XnpcRaiderClasses.Count + " classes, ranged-lead " + string.Join("/", Array.ConvertAll(RaiderRangedShare, x => x.ToString("0.##", CultureInfo.InvariantCulture)))
                          + " (melee-lead to ranged beyond " + RaiderLeadRangedDist + "m for " + RaiderLeadRangedSeconds + "s)"
                          + " firearm " + RaiderFirearmChance + " ammo " + RaiderSpawnAmmo + "/" + RaiderSpawnArrows
                          + " levels=" + (RaiderXnpcLevels ? (RaiderLevelRegen ? "on" : "on, no regen") : "off") + " drop " + RaiderWeaponDropChance
                          + " vanillaDoors=" + RaiderVanillaDoors + " turretsAsEnemies=" + RaiderTurretsAsEnemies
                          + " gearSets=" + (RaiderGearSets.Count > 0 ? RaiderGearSets.Count.ToString() : "survivors'"));
            }
            catch (Exception e)
            {
                parsed = true;   // never retry every frame
                ScLog.Err("config parse failed, defaults in use: " + e.Message);
                Log.Exception(e);
            }
        }

        private static IEnumerable<string> SplitList(string s)
        {
            foreach (string part in s.Split(','))
            {
                string t = part.Trim();
                if (t.Length > 0) yield return t;
            }
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

        /// <summary>"family,tier[,alias]" for one XnpcWeapons entry; null (with a warning) when it cannot be read.</summary>
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
            return new XnpcWeaponDef { Item = item.Trim(), Family = family, Tier = tier, Alias = parts.Length > 2 ? parts[2].Trim() : "" };
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
