#if SC_V34
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round m1t (T1 + T2, 2026-11-17, ROADMAP.md section 3 item 3): the config keys of the life skills round m1t added
    /// (ScLifeSkills) - how often a new recruit rolls each one, and the numbers behind their effects. The trait keys
    /// (nightwatch, wake, defense, haul, barter, heal, sickness, books, rangexp, gymxp, nomedic, wear, aim, excludes) are
    /// per trait, in the Traits class (ScConfig.ParseTrait). Kept apart from ScConfig.cs's long lists, as ScConfigSocial.
    /// Config/blocks.xml, block ConfigSurvivorCommunity, after the Traits class; each key is commented there. ResetTraits runs
    /// at GameStarting with the rest of ScConfig.Reset.
    /// </summary>
    public static partial class ScConfig
    {
        /// <summary>The default roll weights of a new recruit's life skill (Specialty name: weight). A trait's spec= still wins.</summary>
        public const string DefaultSpecialtyWeights =
            "Medicine:3,Cooking:3,Mechanics:3,Gardening:3,Chemistry:3,Craftsmanship:3,"
            + "Construction:2,Logistics:2,Bartering:2,Recon:2,Entertainment:2,Fitness:1,FirearmsInstructor:1";

        /// <summary>A new recruit's life skill is rolled by these weights, index = (int)Specialty (0 = None, never rolled).</summary>
        public static readonly float[] SpecialtyWeights = new float[Enum.GetValues(typeof(Specialty)).Length];

        /// <summary>
        /// Logistics: the share of a bundle's packing value an opened bundle holds rises from BundleOpenValueShare (0.75) toward
        /// this at 7 stars (a seventh of the way per star). Never above BundleOpenShareMax (0.85), and the craftsmen's cap
        /// (ScBundleOpen.ShareCap) still applies, so opening a bundle never gives back more than packing it cost.
        /// </summary>
        public static float LogisticsShareTop = 0.85f;
        /// <summary>Logistics: the best Logistics member gets this much specialty XP per bundle opened.</summary>
        public static float LogisticsXpPerBundle = 1f;

        /// <summary>
        /// Bartering, by the best Bartering member's stars (1..7): bundles fewer you give on a radio or ally barter (never below
        /// one, and a trade is never better than one for one).
        /// </summary>
        public static readonly int[] BarteringCutByStars = { 1, 1, 1, 1, 2, 2, 2 };

        /// <summary>Recon: with a Recon member at home, the radio's scouting marks this many sites (RadioScoutCount without one).</summary>
        public static int ReconScoutCount = 5;

        /// <summary>
        /// Construction, by her stars (1..7): the strongest blocks she may repair on a Base repair duty - 0-XNPCCore's repair
        /// tiers, 0 wood, 1 cobblestone, 2 concrete, 3 steel (its RepairSkill settings). SC raises her repair XP to that tier's
        /// threshold; what she learns by repairing on top still counts, and nothing is ever taken away.
        /// </summary>
        public static readonly int[] ConstructionRepairTiers = { 1, 1, 2, 2, 3, 3, 3 };
        /// <summary>Construction: specialty XP per point of 0-XNPCCore repair XP she earns (a full wood repair pays about 4).</summary>
        public static float ConstructionXpPerRepairXp = 0.25f;

        /// <summary>Entertainment: a round of drinks lifts morale this much more per star of the best entertainer at home (rounded up).</summary>
        public static float EntertainmentDrinksPerStar = 0.5f;

        /// <summary>Specialty XP for using a life skill once: a barter (Bartering), a scouting (Recon), a round of drinks (Entertainment).</summary>
        public static float LifeSkillUseXp = 3f;

        private static void ResetTraits()
        {
            ParseSpecialtyWeights(DefaultSpecialtyWeights, false);
            LogisticsShareTop = 0.85f;
            LogisticsXpPerBundle = 1f;
            int[] cut = { 1, 1, 1, 1, 2, 2, 2 };
            Array.Copy(cut, BarteringCutByStars, cut.Length);
            ReconScoutCount = 5;
            int[] tiers = { 1, 1, 2, 2, 3, 3, 3 };
            Array.Copy(tiers, ConstructionRepairTiers, tiers.Length);
            ConstructionXpPerRepairXp = 0.25f;
            EntertainmentDrinksPerStar = 0.5f;
            LifeSkillUseXp = 3f;
            ScTraitEffects.Reset();          // their session state (counters, the toasts said) goes with the world, as ScBond's
            ScLifeSkills.Reset();
        }

        private static void ParseTraits(DynamicProperties p)
        {
            string s;
            if (p.Values.TryGetValue("SpecialtyWeights", out s) && !string.IsNullOrEmpty(s)) ParseSpecialtyWeights(s, true);
            ParseFloat(p, "LogisticsShareTop", ref LogisticsShareTop, 0f, 1f);
            ParseFloat(p, "LogisticsXpPerBundle", ref LogisticsXpPerBundle, 0f, 100f);
            if (p.Values.TryGetValue("BarteringCutByStars", out s) && !string.IsNullOrEmpty(s)) ParseTierInts("BarteringCutByStars", s, BarteringCutByStars, 0, 20);
            ParseInt(p, "ReconScoutCount", ref ReconScoutCount, 1, 10);
            if (p.Values.TryGetValue("ConstructionRepairTiers", out s) && !string.IsNullOrEmpty(s)) ParseTierInts("ConstructionRepairTiers", s, ConstructionRepairTiers, 0, 3);
            ParseFloat(p, "ConstructionXpPerRepairXp", ref ConstructionXpPerRepairXp, 0f, 100f);
            ParseFloat(p, "EntertainmentDrinksPerStar", ref EntertainmentDrinksPerStar, 0f, 20f);
            ParseFloat(p, "LifeSkillUseXp", ref LifeSkillUseXp, 0f, 100f);
        }

        /// <summary>"Medicine:3,Recon:2,..." into SpecialtyWeights. A specialty the list leaves out weighs 0 (never rolled).</summary>
        private static void ParseSpecialtyWeights(string s, bool warn)
        {
            var w = new float[SpecialtyWeights.Length];
            float total = 0f;
            foreach (string part in s.Split(','))
            {
                string[] kv = part.Split(':');
                if (kv.Length != 2) { if (warn && part.Trim().Length > 0) ScLog.Warn("config SpecialtyWeights: '" + part + "' is not Name:weight - skipped"); continue; }
                Specialty sp;
                float x;
                if (!ScNames.TrySpecialty(kv[0], out sp) || sp == Specialty.None)
                {
                    if (warn) ScLog.Warn("config SpecialtyWeights: no specialty '" + kv[0].Trim() + "' - skipped");
                    continue;
                }
                if (!float.TryParse(kv[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out x) || x < 0f || float.IsNaN(x) || float.IsInfinity(x))
                {
                    if (warn) ScLog.Warn("config SpecialtyWeights: " + sp + " weight '" + kv[1].Trim() + "' is not a number of 0 or more - skipped");
                    continue;
                }
                w[(int)sp] = x;
                total += x;
            }
            if (total <= 0f)
            {
                if (warn) ScLog.Warn("config SpecialtyWeights '" + s + "': no specialty with a weight above 0 - kept the previous weights");
                return;
            }
            Array.Copy(w, SpecialtyWeights, w.Length);
        }

        /// <summary>For sc spec and the load log: the life-skill keys in one line.</summary>
        public static string LifeSkillsLine()
        {
            var sb = new StringBuilder("weights ");
            for (int i = 1; i < SpecialtyWeights.Length; i++)
            {
                if (i > 1) sb.Append(',');
                sb.Append((Specialty)i).Append(':').Append(SpecialtyWeights[i].ToString("0.##", CultureInfo.InvariantCulture));
            }
            sb.Append("; Logistics share to ").Append(LogisticsShareTop.ToString("0.###", CultureInfo.InvariantCulture))
              .Append(" (+").Append(LogisticsXpPerBundle.ToString("0.##", CultureInfo.InvariantCulture)).Append(" XP a bundle)")
              .Append("; Bartering cut ").Append(Join(BarteringCutByStars))
              .Append("; Recon marks ").Append(ReconScoutCount)
              .Append("; Construction repair tiers ").Append(Join(ConstructionRepairTiers))
              .Append(" (").Append(ConstructionXpPerRepairXp.ToString("0.##", CultureInfo.InvariantCulture)).Append(" XP per repair XP)")
              .Append("; Entertainment +").Append(EntertainmentDrinksPerStar.ToString("0.##", CultureInfo.InvariantCulture)).Append(" drinks morale a star")
              .Append("; use XP ").Append(LifeSkillUseXp.ToString("0.##", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        private static string Join(int[] a)
        {
            var parts = new string[a.Length];
            for (int i = 0; i < a.Length; i++) parts[i] = a[i].ToString(CultureInfo.InvariantCulture);
            return string.Join("/", parts);
        }
    }
}
#endif
