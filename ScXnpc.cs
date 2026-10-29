using System;
using System.Collections.Generic;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// The single owner of "is this one of OUR 0-XNPCCore survivors" (the XNPCCore migration, design section 1.1).
    ///
    /// 0-XNPCCore 3.2 ships player-body survivors of its own, npcSdcsSurvivor{Male,Female}T{1,2,3}: an SCore
    /// EntityAliveSDXV4 wearing an EModelSdcsNpc (NPCSdcsCharacters.dll, a subclass of the vanilla EModelSDCS) and driven by
    /// an AvatarSdcsNpcController. The community takes those six classes over - SC's own looks, names, half armor
    /// protection, item-only healing with the bandage animation and the visible aim pose instead of XNPC's presets (the
    /// user's decisions D2, D7 and D12) - so every hook that should act on such a body asks IsBody here, and nowhere
    /// else is the list of classes spelled out. XNPC's other player-body classes (npcSdcsTest, npcSdcsFemaleAK47 and the
    /// like: menu-only classes the community never spawns) stay XNPC's own, and the body hooks keep counting them as
    /// "foreign" when they step aside (sc xnpc).
    ///
    /// The classes are configurable (XnpcSurvivorClasses in Config/blocks.xml) in case a later XNPCCore renames or adds
    /// tiers; the XML that strips XNPC's look applier from them (Config/entityclasses.xml, emitted by
    /// Tools/gen_arsenal.py) names the same six and has to follow such a change by hand.
    ///
    /// SC's raiders ride the same body since M10 (DESIGN_M10_RAIDERS.md): scRaider{Male,Female}T{1,2,3}, hostile
    /// EntityAliveSDXV4 classes SC defines itself on XNPC's npcSdcsBase (Tools/gen_arsenal.py, XnpcRaiderClasses). They
    /// are a second managed set: every body hook that asks IsBody / IsManagedClass (the union) covers them with no further
    /// code - looks, eyes, colliders, the pair's melee weapon, aim, healing, the self-hit guard, the feet offset - and the
    /// few places where a raider must differ from a survivor ask IsRaider / IsRaiderClass or IsSurvivorClass.
    /// </summary>
    public static class ScXnpc
    {
        /// <summary>The classes the community owns when the config does not say otherwise.</summary>
        public static readonly string[] DefaultClasses =
        {
            "npcSdcsSurvivorMaleT1", "npcSdcsSurvivorMaleT2", "npcSdcsSurvivorMaleT3",
            "npcSdcsSurvivorFemaleT1", "npcSdcsSurvivorFemaleT2", "npcSdcsSurvivorFemaleT3"
        };

        /// <summary>SC's hostile classes on the same body (Config/entityclasses.xml, generated) when the config does not say otherwise.</summary>
        public static readonly string[] DefaultRaiderClasses =
        {
            "scRaiderMaleT1", "scRaiderMaleT2", "scRaiderMaleT3",
            "scRaiderFemaleT1", "scRaiderFemaleT2", "scRaiderFemaleT3"
        };

        /// <summary>
        /// What XNPC's kill levels leave on a body (AvatarSdcsNpcController.SdcsLevelTick, the only writer of all of
        /// them): the level bonus buff (health, damage, spread and a 0.2 HP/s regeneration) and the no-wear buff its wear
        /// tick adds first, and the cvars the bonus buff reads live.
        /// </summary>
        private static readonly string[] LevelBuffs = { "SdcsNpcLevelBonus", "SdcsNoWear" };
#if SC_V34
        // 3.4: XNPC's level tick also writes SdcsLevelHarvest (its harvest bonus, AvatarSdcsNpcController.SdcsLevelTick)
        private static readonly string[] LevelCvars = { "SdcsNpcLevel", "SdcsLevelHealth", "SdcsLevelDamage", "SdcsLevelSpread", "SdcsLevelRegen", "SdcsLevelHarvest" };
#else
        private static readonly string[] LevelCvars = { "SdcsNpcLevel", "SdcsLevelHealth", "SdcsLevelDamage", "SdcsLevelSpread", "SdcsLevelRegen" };
#endif
        /// <summary>The regeneration cvar XNPC's level bonus buff reads live (0.2 HP/s per level): zeroed on raiders that keep levels.</summary>
        public const string LevelRegenCvar = "SdcsLevelRegen";

        /// <summary>
        /// Class ids (EntityClass.FromString, the name's hash): the survivor classes, the raider classes and their union
        /// (the managed set every body hook asks). Built together on first use per world.
        /// </summary>
        private static HashSet<int> managed, survivors, raiders;

        /// <summary>AI package name -> has 0-XNPCCore's DynamicFollow (packages are fixed once the XML is loaded).</summary>
        private static readonly Dictionary<string, bool> followPackages = new Dictionary<string, bool>();

        /// <summary>The task class of 0-XNPCCore's follow action (UAI.UAITaskDynamicFollow, in 1-XNPCVoiceControl.dll).</summary>
        private const string DynamicFollowTask = "UAITaskDynamicFollow";

        /// <summary>
        /// The move speed above which 0-XNPCCore's controller runs an NPC: AvatarSdcsNpcController.Update sets
        /// MovementRunning = moveHelper.moveSpeed &gt; 2.75 (a private literal), and MovementRunning picks the run blend
        /// and the RunSpeed passives in EntityAlive.GetPassiveEffectSpeedModifier.
        /// </summary>
        private const float XnpcRunAbove = 2.75f;

        /// <summary>Per-world reset (ModEvents.GameStarting): the config the set is built from is re-read for each world.</summary>
        public static void Reset()
        {
            managed = null;
            survivors = null;
            raiders = null;
            followPackages.Clear();
        }

        /// <summary>Never filled: what Managed answers off the main thread before the set exists.</summary>
        private static readonly HashSet<int> NoClasses = new HashSet<int>();

        /// <summary>
        /// The union. The three sets are built together, so a caller of one never sees another half-built. Built on the main
        /// thread only (the M11 fix review's M5): ScAmmo.WriteHeldGun asks IsBody from the chunk-save thread, and building
        /// would parse the config there. Every body asks IsBody on the main thread when it is created, long before its chunk
        /// can unload, so this answers "none" off the main thread only in theory - and then that one record is skipped.
        /// </summary>
        private static HashSet<int> Managed()
        {
            HashSet<int> set = managed;
            if (set != null) return set;
            if (!ThreadManager.IsMainThread()) return NoClasses;
            var s = new HashSet<int>();
            var r = new HashSet<int>();
            try
            {
                // Read lazily because a body can initialise before anything else in the mod has asked for the config.
                if (!ScConfig.Parsed) ScConfig.EnsureParsed();
                foreach (string c in ScConfig.XnpcSurvivorClasses)
                    if (!string.IsNullOrEmpty(c)) s.Add(EntityClass.FromString(c.Trim()));
                foreach (string c in ScConfig.XnpcRaiderClasses)
                    if (!string.IsNullOrEmpty(c)) r.Add(EntityClass.FromString(c.Trim()));
                // a class listed on both sides would be a survivor with a raider's rules in some places and not in others
                foreach (int id in r)
                    if (s.Contains(id)) ScLog.Once("xnpc-classes-both", "a class is in both XnpcSurvivorClasses and XnpcRaiderClasses - it is treated as a raider");
                s.ExceptWith(r);
            }
            catch (Exception e)
            {
                ScLog.Once("xnpc-classes", e);
            }
            set = new HashSet<int>(s);
            set.UnionWith(r);
            survivors = s;
            raiders = r;
            managed = set;
            return set;
        }

        private static HashSet<int> Survivors() { Managed(); return survivors ?? new HashSet<int>(); }
        private static HashSet<int> Raiders() { Managed(); return raiders ?? new HashSet<int>(); }

        /// <summary>Is this entity class one of the classes SC drives on XNPC's body - a survivor class the community owns, or a raider class?</summary>
        public static bool IsManagedClass(int entityClass)
        {
            return Managed().Contains(entityClass);
        }

        /// <summary>The same by class name (registry records keep the name).</summary>
        public static bool IsManagedClass(string className)
        {
            return !string.IsNullOrEmpty(className) && Managed().Contains(EntityClass.FromString(className));
        }

        /// <summary>One of 0-XNPCCore's survivor classes the community owns (never a raider class): recruits, radio contacts, members.</summary>
        public static bool IsSurvivorClass(int entityClass)
        {
            return Survivors().Contains(entityClass);
        }

        public static bool IsSurvivorClass(string className)
        {
            return !string.IsNullOrEmpty(className) && Survivors().Contains(EntityClass.FromString(className));
        }

        /// <summary>One of SC's hostile raider classes (scRaider{Male,Female}T{1,2,3}).</summary>
        public static bool IsRaiderClass(int entityClass)
        {
            return Raiders().Contains(entityClass);
        }

        public static bool IsRaiderClass(string className)
        {
            return !string.IsNullOrEmpty(className) && Raiders().Contains(EntityClass.FromString(className));
        }

        /// <summary>A raider on XNPC's body (IsBody and a raider class). Type tests only, like IsBody.</summary>
        public static bool IsRaider(Entity e)
        {
            return IsBody(e) && IsRaiderClass(e.entityClass);
        }

        /// <summary>One of the community's 0-XNPCCore survivors on XNPC's body (IsBody and a survivor class).</summary>
        public static bool IsSurvivorBody(Entity e)
        {
            return IsBody(e) && IsSurvivorClass(e.entityClass);
        }

        /// <summary>
        /// One of our XNPC bodies, survivor or raider: an EntityAliveSDXV4 of a managed class wearing XNPC's EModelSdcsNpc. Type tests only
        /// (no Unity null comparison), so it answers correctly for a body that is being destroyed, which is when the
        /// asset release in Patches_Sdcs asks.
        /// </summary>
        public static bool IsBody(Entity e)
        {
            return e is EntityAliveSDXV4 && e.emodel is EModelSdcsNpc && IsManagedClass(e.entityClass);
        }

        /// <summary>
        /// A model that belongs to one of our XNPC bodies. Usable from inside the model's own Init (createModel, the first
        /// GenerateMeshes): Entity.InitEModel assigns emodel before it calls emodel.Init, so the entity already points at
        /// this model there. Plain reference tests for the same reason as IsBody.
        /// </summary>
        public static bool IsBodyModel(EModelBase m)
        {
            if ((object)m == null) return false;
            Entity e = m.entity;
            return (object)e != null && ReferenceEquals(e.emodel, m) && IsBody(e);
        }

        /// <summary>
        /// A body 0-XNPCCore's kill levels run on: any of its player-body NPCs (EModelSdcsNpc, driven by its
        /// AvatarSdcsNpcController, whose level tick is the only writer), the six managed classes or not - D8 keeps the
        /// levels off every member, and a companion of another 0-XNPCCore class may have joined (D9).
        /// </summary>
        public static bool HasXnpcLevels(Entity e)
        {
            return e is EntityAliveSDXV4 && e.emodel is EModelSdcsNpc;
        }

        /// <summary>A community member or an outpost or rescue stand-in (Patches_VoiceControl's test: the sc_member or stand-in cvar).</summary>
        public static bool IsCommunity(EntityAlive e)
        {
            return Patches_VoiceControl.IsCommunityNpc(e);
        }

        /// <summary>
        /// The managed class for a sex and a weapon tier (0..2 as ScLooks.MaxTierFor gives it, T1..T3), or null. Falls back
        /// to the first configured class of that sex when the configured list has no exact T&lt;n&gt; name.
        /// </summary>
        public static string ClassFor(bool male, int tier)
        {
            string want = "npcSdcsSurvivor" + (male ? "Male" : "Female") + "T" + (Mathf.Clamp(tier, 0, 2) + 1);
            if (IsSurvivorClass(want) && ScCommunity.ClassExists(EntityClass.FromString(want))) return want;
            foreach (string c in ScConfig.XnpcSurvivorClasses)
                if (ScLooks.ClassIsMale(c) == male && IsSurvivorClass(c) && ScCommunity.ClassExists(EntityClass.FromString(c))) return c;
            return null;
        }

        /// <summary>
        /// The raider class for a sex and a band tier (0..2: T1..T3; outpost raids and sc spawn hostile pick it directly
        /// instead of rolling a spawn group, whose "none" entry would win 96% of the time). Falls back to any configured
        /// raider class of that sex, then null.
        /// </summary>
        public static string RaiderClassFor(bool male, int tier)
        {
            string want = "scRaider" + (male ? "Male" : "Female") + "T" + (Mathf.Clamp(tier, 0, 2) + 1);
            if (IsRaiderClass(want) && ScCommunity.ClassExists(EntityClass.FromString(want))) return want;
            foreach (string c in ScConfig.XnpcRaiderClasses)
                if (ScLooks.ClassIsMale(c) == male && IsRaiderClass(c) && ScCommunity.ClassExists(EntityClass.FromString(c))) return c;
            return null;
        }

        /// <summary>
        /// The weapon tier a managed class stands for, 0..2 from its T1..T3 name: the band its spawn group, the radio or the
        /// console picked it for (ClassFor, RaiderClassFor). 2 when the name carries no tier, so such a class is capped by
        /// the gamestage alone (ScLoadout).
        /// </summary>
        public static int TierOf(string className)
        {
            if (string.IsNullOrEmpty(className)) return 2;
            int i = className.LastIndexOf('T');
            int t;
            if (i < 0 || i == className.Length - 1 || !int.TryParse(className.Substring(i + 1), out t)) return 2;
            return Mathf.Clamp(t - 1, 0, 2);
        }

        /// <summary>
        /// Does one of this NPC's AI packages carry 0-XNPCCore's DynamicFollow? Every 0-XNPCCore NPC does (NPCModNPCHired,
        /// NPCModMechHired); SC's own follow actions are only the fallback for a member whose packages lack it
        /// (UAIConsiderationScNoDynamicFollow). Looked up by task class name, so nothing here needs Voice Control's DLL.
        /// </summary>
        public static bool HasDynamicFollow(EntityAlive e)
        {
            if (e == null || e.AIPackages == null) return false;
            for (int i = 0; i < e.AIPackages.Count; i++)
                if (PackageFollows(e.AIPackages[i])) return true;
            return false;
        }

        private static bool PackageFollows(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            bool has;
            if (followPackages.TryGetValue(name, out has)) return has;
            has = false;
            try
            {
                UAI.UAIPackage pkg;
                // a package that is not registered (yet) is not cached: it is looked up again next time
                if (UAI.UAIBase.AIPackages == null || !UAI.UAIBase.AIPackages.TryGetValue(name, out pkg) || pkg == null) return false;
                if (pkg.GetActions() != null)
                {
                    foreach (UAI.UAIAction a in pkg.GetActions())
                    {
                        if (a == null || a.GetTasks() == null) continue;
                        foreach (UAI.UAITaskBase t in a.GetTasks())
                            if (t != null && t.GetType().Name == DynamicFollowTask) { has = true; break; }
                        if (has) break;
                    }
                }
                followPackages[name] = has;
            }
            catch (Exception ex)
            {
                ScLog.Once("xnpc-dynamicfollow", ex);
            }
            return has;
        }

        /// <summary>
        /// The speed to hand FindPath so that one of 0-XNPCCore's V4 survivors moves at groundMps (migration M4.1, the
        /// runners). XNPC drives the legs at moveHelper.moveSpeed x GetPassiveEffectSpeedModifier() (AvatarSdcsNpcController
        /// .Update, clamped to 6 m/s): the NPC's own RunSpeed passives on vanilla's 1.6 while it runs (moveSpeed above 2.75),
        /// its WalkSpeed passives on 0.8 while it walks. So the target pace is divided by that base factor - the NPC's own
        /// passives (Cardio, hungry, injured) then apply exactly once, inside XNPC - and kept on its side of the run
        /// threshold, else a slow run would drop into a walk (or a fast walk into a run) and halve (or double) the pace.
        /// A run therefore never goes below about 4.5 m/s, a walk never above about 2.1.
        /// </summary>
        public static float PathSpeed(float groundMps, bool run)
        {
            float factor = run ? Constants.cPlayerSpeedModifierRunning : Constants.cPlayerSpeedModifierWalking;
            float s = Mathf.Max(0.1f, groundMps) / Mathf.Max(0.1f, factor);
            return run ? Mathf.Max(s, XnpcRunAbove + 0.05f) : Mathf.Min(s, XnpcRunAbove - 0.05f);
        }

        /// <summary>
        /// Takes XNPC's kill levels off a body (decision D8: members and stand-ins grow through SC's skills only). Idempotent.
        /// Progression goes too: XNPC seeds one for any NPC with a leader, and V4's Write and XNPC's kill-XP handler both
        /// treat a null Progression as "none" (Persistence.cs writes the flag; SdcsNpcKillXpHandler skips it). Called on
        /// join and every stamp (ScCommunity.StampCvars), for respawned members and stand-ins (ScSpawn.Identify), and once
        /// a second in place of XNPC's level tick for every member or stand-in body (Patches_Xnpc X3), which is what keeps
        /// it off. A member who leaves loses the community cvars, and XNPC's tick then seeds a day-based level again.
        /// X3 does the same for every raider while RaiderXnpcLevels is off (M10-D3: raiders get no kill levels).
        /// </summary>
        public static void StripLevels(EntityAlive e)
        {
            if (e == null || e.Buffs == null) return;
            try
            {
                for (int i = 0; i < LevelBuffs.Length; i++)
                    if (e.Buffs.HasBuff(LevelBuffs[i])) e.Buffs.RemoveBuff(LevelBuffs[i]);
                for (int i = 0; i < LevelCvars.Length; i++)
                    if (e.Buffs.HasCustomVar(LevelCvars[i])) e.Buffs.RemoveCustomVar(LevelCvars[i]);
                e.Progression = null;
            }
            catch (Exception ex)
            {
                ScLog.Once("xnpc-strip-levels", ex);
            }
        }
    }
}
