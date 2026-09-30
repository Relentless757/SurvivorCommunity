using System;
using System.Collections.Generic;

namespace SurvivorCommunity
{
    /// <summary>Trait rolling and the aggregation of a member's trait modifiers (definitions come from ScConfig).</summary>
    public static class ScTraits
    {
        public const string BuffPrefix = "traitSC_";

        public static TraitDef Find(string name)
        {
            for (int i = 0; i < ScConfig.Traits.Count; i++)
            {
                if (string.Equals(ScConfig.Traits[i].Name, name, StringComparison.OrdinalIgnoreCase)) return ScConfig.Traits[i];
            }
            return null;
        }

        /// <summary>Rolls N distinct traits by weight (or fewer when the table is short).</summary>
        public static List<string> Roll(int count, GameRandom rnd)
        {
            var result = new List<string>();
            var pool = new List<TraitDef>(ScConfig.Traits);
            pool.RemoveAll(t => t.Weight <= 0);
            while (result.Count < count && pool.Count > 0)
            {
                int total = 0;
                for (int i = 0; i < pool.Count; i++) total += pool[i].Weight;
                int pick = rnd.RandomRange(0, total);
                int idx = 0;
                for (int i = 0; i < pool.Count; i++)
                {
                    pick -= pool[i].Weight;
                    if (pick < 0) { idx = i; break; }
                }
                result.Add(pool[idx].Name);
                pool.RemoveAt(idx);
            }
            return result;
        }

        public static float FoodMult(Member m)
        {
            float f = 1f;
            for (int i = 0; i < m.Traits.Count; i++) { TraitDef t = Find(m.Traits[i]); if (t != null) f *= t.FoodMult; }
            return f;
        }

        public static int MoraleOffset(Member m)
        {
            int v = 0;
            for (int i = 0; i < m.Traits.Count; i++) { TraitDef t = Find(m.Traits[i]); if (t != null) v += t.MoraleOffset; }
            return v;
        }

        public static int MoodOffset(Member m)
        {
            int v = 0;
            for (int i = 0; i < m.Traits.Count; i++) { TraitDef t = Find(m.Traits[i]); if (t != null) v += t.MoodOffset; }
            return v;
        }

        public static bool IsTimid(Member m)
        {
            for (int i = 0; i < m.Traits.Count; i++) { TraitDef t = Find(m.Traits[i]); if (t != null && t.Timid) return true; }
            return false;
        }

        public static float XpMult(Member m)
        {
            float f = 1f;
            for (int i = 0; i < m.Traits.Count; i++) { TraitDef t = Find(m.Traits[i]); if (t != null) f *= t.XpMult; }
            return f;
        }

        public static float RunYieldMult(Member m)
        {
            float f = 1f;
            for (int i = 0; i < m.Traits.Count; i++) { TraitDef t = Find(m.Traits[i]); if (t != null) f *= t.RunYieldMult; }
            return f;
        }

        /// <summary>The buff carrying a trait's tooltip; only applied when buffs.xml defines it.</summary>
        public static string BuffName(string trait) { return BuffPrefix + trait; }

        // buff name and "does buffs.xml define it" resolved once per trait: this runs once a second per member
        private static readonly Dictionary<string, string> buffNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, bool> buffExists = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        private static string ResolveBuff(string trait, out bool exists)
        {
            string buff;
            if (!buffNames.TryGetValue(trait, out buff)) { buff = BuffName(trait); buffNames[trait] = buff; }
            if (!buffExists.TryGetValue(trait, out exists))
            {
                exists = BuffManager.GetBuff(buff) != null;
                buffExists[trait] = exists;
                if (!exists) ScLog.OnceOut("trait-buff-" + buff, "no buff named " + buff + " in buffs.xml - the trait shows only in the roster");
            }
            return buff;
        }

        /// <summary>Per-world reset (buff definitions may differ between worlds' mod sets).</summary>
        public static void Reset()
        {
            buffNames.Clear();
            buffExists.Clear();
        }

        /// <summary>Adds the trait buffs (tooltips) and the Timid flag to the entity; idempotent and allocation-free once warm.</summary>
        public static void ApplyBuffs(Member m, EntityAlive npc)
        {
            if (npc == null || npc.Buffs == null) return;
            for (int i = 0; i < m.Traits.Count; i++)
            {
                bool exists;
                string buff = ResolveBuff(m.Traits[i], out exists);
                if (exists && !npc.Buffs.HasBuff(buff)) npc.Buffs.AddBuff(buff);
            }
            if (IsTimid(m) && npc.Buffs.GetCustomVar("$Timid") < 1f) npc.Buffs.SetCustomVar("$Timid", 1f);
        }

        public static void RemoveBuffs(Member m, EntityAlive npc)
        {
            if (npc == null || npc.Buffs == null) return;
            for (int i = 0; i < m.Traits.Count; i++)
            {
                string buff = BuffName(m.Traits[i]);
                if (npc.Buffs.HasBuff(buff)) npc.Buffs.RemoveBuff(buff);
            }
            if (npc.Buffs.HasCustomVar("$Timid")) npc.Buffs.RemoveCustomVar("$Timid");
        }

        /// <summary>Removes every trait buff the config knows about (for an entity whose record is gone).</summary>
        public static void RemoveAllBuffs(EntityAlive npc)
        {
            if (npc == null || npc.Buffs == null) return;
            for (int i = 0; i < ScConfig.Traits.Count; i++)
            {
                string buff = BuffName(ScConfig.Traits[i].Name);
                if (npc.Buffs.HasBuff(buff)) npc.Buffs.RemoveBuff(buff);
            }
            if (npc.Buffs.HasCustomVar("$Timid")) npc.Buffs.RemoveCustomVar("$Timid");
        }

        /// <summary>Localized trait name ("scTrait&lt;Name&gt;", falling back to the raw name).</summary>
        public static string Display(string trait)
        {
            string key = "scTrait" + trait;
            string s = Localization.Get(key);
            return (string.IsNullOrEmpty(s) || s == key) ? trait : s;
        }

        /// <summary>The trait's one-line effect ("scTrait&lt;Name&gt;Desc", the trait buff's description), or "".</summary>
        public static string Describe(string trait)
        {
            if (string.IsNullOrEmpty(trait)) return "";
            string key = "scTrait" + trait + "Desc";
            string s = Localization.Get(key);
            return (string.IsNullOrEmpty(s) || s == key) ? "" : s;
        }
    }
}
