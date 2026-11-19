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

        /// <summary>
        /// Rolls N distinct traits by weight (or fewer when the table is short). Round m1t (T1): a pick takes every trait it
        /// excludes (excludes=, two-way since LinkExclusions) out of the pool, so no two conflicting traits are rolled together.
        /// </summary>
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
                TraitDef picked = pool[idx];
                result.Add(picked.Name);
                pool.RemoveAt(idx);
                pool.RemoveAll(t => Excludes(picked, t.Name));     // round m1t
            }
            return result;
        }

        // ------------------------------------------------------------------ round m1t (T1): exclusions ----

        /// <summary>Does trait a rule out trait b (excludes=, made two-way by LinkExclusions)?</summary>
        public static bool Excludes(TraitDef a, string b)
        {
            if (a == null || string.IsNullOrEmpty(b)) return false;
            for (int i = 0; i < a.Excludes.Count; i++)
                if (string.Equals(a.Excludes[i], b, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Do these two traits (by name) rule each other out? Unknown names never do.</summary>
        public static bool Conflict(string a, string b)
        {
            return Excludes(Find(a), b) || Excludes(Find(b), a);
        }

        /// <summary>The first pair in this list that rules each other out ("A and B"), or null.</summary>
        public static string FirstConflict(IList<string> traits)
        {
            for (int i = 0; i < traits.Count; i++)
                for (int j = i + 1; j < traits.Count; j++)
                    if (Conflict(traits[i], traits[j])) return traits[i] + " and " + traits[j];
            return null;
        }

        /// <summary>
        /// After the config's Traits class is read: every excludes= pair made two-way (B excludes A whenever A excludes B), so
        /// the table may name a pair on one row or both. A name no trait has is warned once and dropped.
        /// </summary>
        public static void LinkExclusions()
        {
            List<TraitDef> all = ScConfig.Traits;
            for (int i = 0; i < all.Count; i++)
            {
                TraitDef a = all[i];
                for (int k = a.Excludes.Count - 1; k >= 0; k--)
                {
                    TraitDef b = Find(a.Excludes[k]);
                    if (b == null || b == a)
                    {
                        ScLog.Warn("config trait " + a.Name + ": excludes '" + a.Excludes[k] + "', " + (b == null ? "which is no trait" : "itself") + " - dropped");
                        a.Excludes.RemoveAt(k);
                        continue;
                    }
                    a.Excludes[k] = b.Name;
                    if (!Excludes(b, a.Name)) b.Excludes.Add(a.Name);
                }
            }
        }

        /// <summary>The excluded pairs, each once ("Optimist/Pessimist, ..."), for sc traits and the load log.</summary>
        public static string ExclusionPairs()
        {
            var sb = new System.Text.StringBuilder();
            List<TraitDef> all = ScConfig.Traits;
            for (int i = 0; i < all.Count; i++)
                for (int j = i + 1; j < all.Count; j++)
                    if (Excludes(all[i], all[j].Name))
                    {
                        if (sb.Length > 0) sb.Append(", ");
                        sb.Append(all[i].Name).Append('/').Append(all[j].Name);
                    }
            return sb.Length > 0 ? sb.ToString() : "none";
        }

        /// <summary>
        /// Round ie1: the food she eats a day - MemberFoodPerDay, or the largest eats= of her traits (Bottomless Stomach 2).
        /// ScEconomy.FoodFor is the one caller (it also leaves out a member still on the way).
        /// </summary>
        public static int FoodUnits(Member m)
        {
            int eats = 0;
            if (m == null) return ScConfig.MemberFoodPerDay;
            for (int i = 0; i < m.Traits.Count; i++) { TraitDef t = Find(m.Traits[i]); if (t != null && t.Eats > eats) eats = t.Eats; }
            return eats > 0 ? eats : ScConfig.MemberFoodPerDay;
        }

        /// <summary>Round ie1: does the community's hunger leave her mood alone (nohungermood=1, Eats Like a Bird)?</summary>
        public static bool NoHungerMood(Member m)
        {
            if (m == null) return false;
            for (int i = 0; i < m.Traits.Count; i++) { TraitDef t = Find(m.Traits[i]); if (t != null && t.NoHungerMood) return true; }
            return false;
        }

        /// <summary>
        /// Round ie1: the bundles her traits add to a supply run of this kind (runbundles=, ScRuns.Yield), haul = the haul before
        /// them. A trait limited to some kinds (runkinds=, Tinkerer: materials and fuel) counts only on those runs; one with
        /// runfrom=N (Scavenger: 4) only when the haul with her other trait bonuses is at least N.
        /// </summary>
        public static int RunBundles(Member m, Resource kind, int haul)
        {
            if (m == null) return 0;
            int plain = 0, from = 0;
            for (int i = 0; i < m.Traits.Count; i++)
            {
                TraitDef t = Find(m.Traits[i]);
                if (t == null || t.RunBundles <= 0 || t.RunFrom > 0 || (t.RunKinds.Count > 0 && !t.RunKinds.Contains(kind))) continue;
                plain += t.RunBundles;
            }
            for (int i = 0; i < m.Traits.Count; i++)
            {
                TraitDef t = Find(m.Traits[i]);
                if (t == null || t.RunBundles <= 0 || t.RunFrom <= 0 || (t.RunKinds.Count > 0 && !t.RunKinds.Contains(kind))) continue;
                if (haul + plain >= t.RunFrom) from += t.RunBundles;
            }
            return plain + from;
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

        /// <summary>yield= of her traits: a mining run's items x this (round ie1: supply runs read RunBundles).</summary>
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
            if (IsTimid(m))
            {
                if (npc.Buffs.GetCustomVar("$Timid") < 1f) npc.Buffs.SetCustomVar("$Timid", 1f);
                if (npc.Buffs.GetCustomVar(ScTimidCvar) < 1f) npc.Buffs.SetCustomVar(ScTimidCvar, 1f);   // X2
            }
#if SC_V34
            ScTraitEffects.ApplyBody(m, npc);   // round m1t (T1): Clumsy's tool wear cvar; Squeamish is never 0-XNPCCore's party medic
#endif
        }

        /// <summary>
        /// X2 (0-XNPCCore 3.4.03): marks a body whose $Timid SC wrote. 0-XNPCCore's own Flee (SdcsNpcCore, weight 9) is live
        /// again and reads $Timid with no Stay, watch or home gate; UAIConsiderationScNotScTimid keeps it off these bodies,
        /// whose flight is SC's ScFlee / ScEncFlee.
        /// </summary>
        public const string ScTimidCvar = "sc_timid";

        public static void RemoveBuffs(Member m, EntityAlive npc)
        {
            if (npc == null || npc.Buffs == null) return;
            for (int i = 0; i < m.Traits.Count; i++)
            {
                string buff = BuffName(m.Traits[i]);
                if (npc.Buffs.HasBuff(buff)) npc.Buffs.RemoveBuff(buff);
            }
            if (npc.Buffs.HasCustomVar("$Timid")) npc.Buffs.RemoveCustomVar("$Timid");
            if (npc.Buffs.HasCustomVar(ScTimidCvar)) npc.Buffs.RemoveCustomVar(ScTimidCvar);
#if SC_V34
            if (npc.Buffs.HasCustomVar(ScTraitEffects.WearCvar)) npc.Buffs.RemoveCustomVar(ScTraitEffects.WearCvar);   // round m1t
#endif
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
            if (npc.Buffs.HasCustomVar(ScTimidCvar)) npc.Buffs.RemoveCustomVar(ScTimidCvar);
#if SC_V34
            if (npc.Buffs.HasCustomVar(ScTraitEffects.WearCvar)) npc.Buffs.RemoveCustomVar(ScTraitEffects.WearCvar);   // round m1t
#endif
        }

        /// <summary>Localized trait name ("scTrait&lt;Name&gt;", falling back to the raw name).</summary>
        public static string Display(string trait)
        {
            string key = "scTrait" + trait;
            string s = Localization.Get(key);
            return (string.IsNullOrEmpty(s) || s == key) ? trait : s;
        }

        /// <summary>
        /// The trait's one-line effect ("scTrait&lt;Name&gt;Desc", the trait buff's description), or "". Round m1t: a trait whose
        /// effect waits for a feature not built yet (TraitDef.Needs, ScFeatures) reads "... (coming later)".
        /// </summary>
        public static string Describe(string trait)
        {
            if (string.IsNullOrEmpty(trait)) return "";
            string key = "scTrait" + trait + "Desc";
            string s = Localization.Get(key);
            s = (string.IsNullOrEmpty(s) || s == key) ? "" : s;
#if SC_V34
            if (s.Length > 0 && ScFeatures.Pending(Find(trait))) s += " " + ScFeatures.ComingLater();   // round m1t
#endif
            return s;
        }
    }
}
