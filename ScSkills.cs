using System;
using System.Collections.Generic;

namespace SurvivorCommunity
{
    /// <summary>
    /// Skills: four core skills (1-7 stars) and one specialty per survivor. XP thresholds are cumulative per
    /// star; the NPC cvars sc_cardio / sc_wits / sc_fighting / sc_shooting / sc_spec / sc_speclvl mirror the
    /// levels and sc_dmgmelee / sc_dmgranged / sc_speed / sc_hp carry the derived bonuses that buffSCSkills
    /// (Config/buffs.xml) reads through cvar-valued passive effects (value="@sc_dmgmelee" etc.).
    /// XP sources: kills (Fighting / Shooting), time spent following the player (Cardio), each day lived
    /// (Wits), doing the specialty's job (specialty).
    /// </summary>
    public static class ScSkills
    {
        /// <summary>Cumulative XP needed to REACH star index (1-based); star 1 is free.</summary>
        private static readonly int[] Thresholds = { 0, 0, 25, 60, 120, 220, 360, 550 };

        public const string BuffName = "buffSCSkills";
        public const string HungryBuff = "buffSCHungry";
        public const string ThirstyBuff = "buffSCThirsty";
        public const string InjuredBuff = "buffSCInjured";

        public static int NeededFor(int star)
        {
            if (star <= 1) return 0;
            if (star >= Thresholds.Length) return Thresholds[Thresholds.Length - 1];
            return Thresholds[star];
        }

        /// <summary>Adds XP, returns true when a star was gained.</summary>
        public static bool AddXp(Member m, Skill s, float amount)
        {
            if (m == null || amount <= 0f) return false;
            SkillState st = m.Skill(s);
            if (st.Level >= Member.MaxStars) return false;
            float mult = ScTraits.XpMult(m) * (ScRegistry.Community.Morale > ScConfig.HighMoraleThreshold ? 1.5f : 1f)
                         * ScLeadership.XpMult();                                       // SurvivorOrigins' Leadership (1 without it)
            st.Xp += Math.Max(1, (int)Math.Round(amount * mult));
            bool gained = false;
            while (st.Level < Member.MaxStars && st.Xp >= NeededFor(st.Level + 1))
            {
                st.Level++;
                gained = true;
            }
            if (gained) ScLog.Out(m.Name + " gained a star in " + s + " (now " + st.Level + ")");
            return gained;
        }

        public static bool AddSpecialtyXp(Member m, float amount)
        {
            if (m == null || amount <= 0f || m.Specialty == Specialty.None) return false;
            if (m.SpecLevel >= Member.MaxStars) return false;
            m.SpecXp += Math.Max(1, (int)Math.Round(amount * ScTraits.XpMult(m) * ScLeadership.XpMult()));
            bool gained = false;
            while (m.SpecLevel < Member.MaxStars && m.SpecXp >= NeededFor(m.SpecLevel + 1))
            {
                m.SpecLevel++;
                gained = true;
            }
            if (gained) ScLog.Out(m.Name + " gained a star in " + m.Specialty + " (now " + m.SpecLevel + ")");
            return gained;
        }

        /// <summary>Starting skills at recruit: 1 star each, one random +1, trait bonuses; specialty random or trait-preferred.</summary>
        public static void RollStart(Member m, GameRandom rnd)
        {
            for (int i = 0; i < Member.SkillCount; i++) { m.Skills[i].Level = 1; m.Skills[i].Xp = 0; }
            m.Skills[rnd.RandomRange(0, Member.SkillCount)].Level = 2;
            Specialty preferred = Specialty.None;
            for (int i = 0; i < m.Traits.Count; i++)
            {
                TraitDef t = ScTraits.Find(m.Traits[i]);
                if (t == null) continue;
                if (t.HasSkill) m.Skill(t.SkillBonus).Level = Math.Max(1, Math.Min(Member.MaxStars, m.Skill(t.SkillBonus).Level + t.SkillBonusStars));
                if (t.PreferredSpecialty != Specialty.None) preferred = t.PreferredSpecialty;
            }
            // class-name bias: a Nurse tends to Medicine, a Baker to Cooking
            string cls = (m.Class ?? "").ToLowerInvariant();
            if (preferred == Specialty.None && cls.Contains("nurse") && rnd.RandomFloat < 0.7f) preferred = Specialty.Medicine;
            if (preferred == Specialty.None && cls.Contains("baker") && rnd.RandomFloat < 0.7f) preferred = Specialty.Cooking;
            if (preferred == Specialty.None)
            {
                Specialty[] all = { Specialty.Medicine, Specialty.Cooking, Specialty.Mechanics, Specialty.Gardening, Specialty.Chemistry, Specialty.Craftsmanship };
                preferred = all[rnd.RandomRange(0, all.Length)];
            }
            m.Specialty = preferred;
            m.SpecLevel = 1;
            m.SpecXp = 0;
            for (int i = 0; i < Member.SkillCount; i++) m.Skills[i].Xp = NeededFor(m.Skills[i].Level);
        }

        // ---- derived bonuses (fractions; star 1 = no bonus) ----

        public static float MeleeBonus(Member m) { return ScConfig.MeleePerStar * (m.Stars(Skill.Fighting) - 1); }
        public static float RangedBonus(Member m) { return ScConfig.RangedPerStar * (m.Stars(Skill.Shooting) - 1); }
        public static float SpeedBonus(Member m) { return ScConfig.SpeedPerStar * (m.Stars(Skill.Cardio) - 1); }
        public static float HealthBonus(Member m) { return ScConfig.HealthPerStar * (m.Stars(Skill.Cardio) - 1); }

        /// <summary>Best star count among stationed members with a specialty (0 when nobody has it).</summary>
        public static int BestSpecialist(Specialty s, out Member who)
        {
            who = null;
            int best = 0;
            foreach (var m in ScRegistry.Members.Values)
            {
                if (m.Specialty != s || !m.IsHome || m.Status == MemberStatus.OnRun) continue;
                if (m.SpecLevel > best) { best = m.SpecLevel; who = m; }
            }
            return best;
        }

        /// <summary>Sum of stars of every stationed member with a specialty.</summary>
        public static int SpecialistStars(Specialty s)
        {
            int sum = 0;
            foreach (var m in ScRegistry.Members.Values)
            {
                if (m.Specialty == s && m.IsHome && m.Status != MemberStatus.OnRun) sum += m.SpecLevel;
            }
            return sum;
        }

        /// <summary>Writes the skill mirror cvars and the derived bonus cvars onto the NPC (levels are never 0: a 0 cvar is not saved).</summary>
        public static void StampCvars(Member m, EntityAlive npc)
        {
            if (npc == null || npc.Buffs == null) return;
            for (int i = 0; i < Member.SkillCount; i++) npc.Buffs.SetCustomVar("sc_" + ScNames.SkillKeys[i], m.Skills[i].Level);
            npc.Buffs.SetCustomVar("sc_spec", (int)m.Specialty > 0 ? (int)m.Specialty : -1);
            npc.Buffs.SetCustomVar("sc_speclvl", m.SpecLevel);
            // bonuses: buffSCSkills reads them as passive-effect values; 0 is fine here (re-stamped every relink)
            npc.Buffs.SetCustomVar("sc_dmgmelee", MeleeBonus(m));
            npc.Buffs.SetCustomVar("sc_dmgranged", RangedBonus(m));
            npc.Buffs.SetCustomVar("sc_speed", SpeedBonus(m));
            npc.Buffs.SetCustomVar("sc_hp", HealthBonus(m));
            if (BuffManager.GetBuff(BuffName) != null && !npc.Buffs.HasBuff(BuffName)) npc.Buffs.AddBuff(BuffName);
        }

        /// <summary>Hungry / injured status buffs follow the registry (both are defined in buffs.xml with real penalties).</summary>
        public static void SyncStatusBuffs(Member m, EntityAlive npc)
        {
            if (npc == null || npc.Buffs == null) return;
            Toggle(npc, HungryBuff, ScRegistry.Community.Hungry);
            Toggle(npc, ThirstyBuff, ScRegistry.Community.Thirsty);
            Toggle(npc, InjuredBuff, m.Injured);
        }

        private static readonly Dictionary<string, bool> buffKnown = new Dictionary<string, bool>();

        private static void Toggle(EntityAlive npc, string buff, bool on)
        {
            bool known;
            if (!buffKnown.TryGetValue(buff, out known)) { known = BuffManager.GetBuff(buff) != null; buffKnown[buff] = known; }
            if (!known) return;
            bool has = npc.Buffs.HasBuff(buff);
            if (on && !has) npc.Buffs.AddBuff(buff);
            else if (!on && has) npc.Buffs.RemoveBuff(buff);
        }

        public static void Reset() { buffKnown.Clear(); }

        public static void ClearCvars(EntityAlive npc)
        {
            if (npc == null || npc.Buffs == null) return;
            for (int i = 0; i < Member.SkillCount; i++) npc.Buffs.RemoveCustomVar("sc_" + ScNames.SkillKeys[i]);
            npc.Buffs.RemoveCustomVar("sc_spec");
            npc.Buffs.RemoveCustomVar("sc_speclvl");
            npc.Buffs.RemoveCustomVar("sc_dmgmelee");
            npc.Buffs.RemoveCustomVar("sc_dmgranged");
            npc.Buffs.RemoveCustomVar("sc_speed");
            npc.Buffs.RemoveCustomVar("sc_hp");
            if (npc.Buffs.HasBuff(BuffName)) npc.Buffs.RemoveBuff(BuffName);
            if (npc.Buffs.HasBuff(HungryBuff)) npc.Buffs.RemoveBuff(HungryBuff);
            if (npc.Buffs.HasBuff(ThirstyBuff)) npc.Buffs.RemoveBuff(ThirstyBuff);
            if (npc.Buffs.HasBuff(InjuredBuff)) npc.Buffs.RemoveBuff(InjuredBuff);
        }

        /// <summary>Does the NPC hold a ranged weapon? (XNPCCore hand items carry the melee / ranged tags.)</summary>
        public static bool HoldsRanged(EntityAlive npc)
        {
            try
            {
                if (npc == null || npc.inventory == null) return false;
                ItemValue iv = npc.inventory.holdingItemItemValue;
                if (iv == null || iv.IsEmpty() || iv.ItemClass == null) return false;
                ItemClass ic = iv.ItemClass;
                if (ic.HasAnyTags(FastTags<TagGroup.Global>.Parse("ranged"))) return true;
                if (ic.HasAnyTags(FastTags<TagGroup.Global>.Parse("melee"))) return false;
                return ic.Actions != null && ic.Actions.Length > 0 && ic.Actions[0] is ItemActionRanged;
            }
            catch (Exception) { return false; }
        }

        /// <summary>ModEvents.EntityKilled: a member's kill feeds Fighting or Shooting.</summary>
        public static void OnEntityKilled(Entity killed, Entity killer)
        {
            try
            {
                if (killer == null || killed == null) return;
                Member m = ScRegistry.GetMember(killer.entityId);
                if (m == null) return;
                EntityAlive npc = killer as EntityAlive;
                if (!(killed is EntityAlive) || killed is EntityPlayer) return;
                bool ranged = HoldsRanged(npc);
                m.Ranged = ranged;
                Skill s = ranged ? Skill.Shooting : Skill.Fighting;
                if (AddXp(m, s, 5f))
                {
                    StampCvars(m, npc);
                    ScRegistry.SaveAtomic();
                }
            }
            catch (Exception e)
            {
                ScLog.Once("skills-kill", e);
            }
        }

        /// <summary>Called once a second for a following member: Cardio XP for the road (1 XP per FollowXpSeconds).</summary>
        public static void OnFollowTick(Member m, EntityAlive npc)
        {
            if (m == null) return;
            m.FollowSeconds++;
            if (m.FollowSeconds < Math.Max(5, ScConfig.FollowXpSeconds)) return;
            m.FollowSeconds = 0;
            if (AddXp(m, Skill.Cardio, 1f)) { StampCvars(m, npc); ScRegistry.SaveAtomic(); }
        }

        public static string Stars(int n)
        {
            n = Math.Max(0, Math.Min(Member.MaxStars, n));
            return new string('★', n) + new string('☆', Member.MaxStars - n);
        }
    }
}
