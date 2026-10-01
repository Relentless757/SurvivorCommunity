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
    /// </summary>
    public static class ScXnpc
    {
        /// <summary>The classes the community owns when the config does not say otherwise.</summary>
        public static readonly string[] DefaultClasses =
        {
            "npcSdcsSurvivorMaleT1", "npcSdcsSurvivorMaleT2", "npcSdcsSurvivorMaleT3",
            "npcSdcsSurvivorFemaleT1", "npcSdcsSurvivorFemaleT2", "npcSdcsSurvivorFemaleT3"
        };

        /// <summary>
        /// What XNPC's kill levels leave on a body (AvatarSdcsNpcController.SdcsLevelTick, the only writer of all of
        /// them): the level bonus buff (health, damage, spread and a 0.2 HP/s regeneration) and the no-wear buff its wear
        /// tick adds first, and the cvars the bonus buff reads live.
        /// </summary>
        private static readonly string[] LevelBuffs = { "SdcsNpcLevelBonus", "SdcsNoWear" };
        private static readonly string[] LevelCvars = { "SdcsNpcLevel", "SdcsLevelHealth", "SdcsLevelDamage", "SdcsLevelSpread", "SdcsLevelRegen" };

        /// <summary>Class ids (EntityClass.FromString, the name's hash) of the managed classes; built on first use per world.</summary>
        private static HashSet<int> managed;

        /// <summary>Per-world reset (ModEvents.GameStarting): the config the set is built from is re-read for each world.</summary>
        public static void Reset()
        {
            managed = null;
        }

        private static HashSet<int> Managed()
        {
            HashSet<int> set = managed;
            if (set != null) return set;
            set = new HashSet<int>();
            try
            {
                // Read lazily because a body can initialise before anything else in the mod has asked for the config.
                if (!ScConfig.Parsed) ScConfig.EnsureParsed();
                foreach (string c in ScConfig.XnpcSurvivorClasses)
                    if (!string.IsNullOrEmpty(c)) set.Add(EntityClass.FromString(c.Trim()));
            }
            catch (Exception e)
            {
                ScLog.Once("xnpc-classes", e);
            }
            managed = set;
            return set;
        }

        /// <summary>Is this entity class one of the survivor classes the community owns?</summary>
        public static bool IsManagedClass(int entityClass)
        {
            return Managed().Contains(entityClass);
        }

        /// <summary>The same by class name (registry records keep the name).</summary>
        public static bool IsManagedClass(string className)
        {
            return !string.IsNullOrEmpty(className) && Managed().Contains(EntityClass.FromString(className));
        }

        /// <summary>
        /// One of our XNPC bodies: an EntityAliveSDXV4 of a managed class wearing XNPC's EModelSdcsNpc. Type tests only
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
            if (IsManagedClass(want) && ScCommunity.ClassExists(EntityClass.FromString(want))) return want;
            foreach (string c in ScConfig.XnpcSurvivorClasses)
                if (ScLooks.ClassIsMale(c) == male && ScCommunity.ClassExists(EntityClass.FromString(c))) return c;
            return null;
        }

        /// <summary>
        /// The weapon tier a managed class stands for, 0..2 from its T1..T3 name: the band its spawn group, the radio or the
        /// console picked it for (ClassFor). 2 when the name carries no tier, so such a class is capped by the gamestage
        /// alone (ScLoadout).
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
        /// Takes XNPC's kill levels off a body (decision D8: members and stand-ins grow through SC's skills only). Idempotent.
        /// Progression goes too: XNPC seeds one for any NPC with a leader, and V4's Write and XNPC's kill-XP handler both
        /// treat a null Progression as "none" (Persistence.cs writes the flag; SdcsNpcKillXpHandler skips it).
        /// XNPC's level tick re-seeds all of this a second later until migration task M2's SdcsLevelTick prefix keeps it
        /// off; until then a call here is harmless.
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
