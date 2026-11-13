using System;
using System.Collections.Generic;
using System.Globalization;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round 33c (M33, 3.4 build only): the config keys of mining runs - Mine and Dig clay sent like a supply run (ScMining).
    /// Kept apart from ScConfig.cs's long lists, as ScConfigCraft and ScConfigRadio. Config/blocks.xml, block
    /// ConfigSurvivorCommunity, after RunWitsXp; each key is commented there. ResetMining also clears ScMining's per-world
    /// state (ScConfig.Reset runs at GameStarting).
    /// </summary>
    public static partial class ScConfig
    {
        /// <summary>Mine and Dig clay are offered beside the resource runs (the talk's run list, the community window, `sc run`).</summary>
        public static bool MiningRuns = true;
        /// <summary>Game hours a miner works the spot; 0 = RunSiteHours (the supply runs' site hours, 6 by default).</summary>
        public static float MiningRunSiteHours;
        /// <summary>Stone (MiningStoneItem) a Mine run brings per site hour at 1 Wits star with a tier 1 tool (an iron pickaxe).</summary>
        public static float MiningStonePerHour = 30f;
        /// <summary>Ore items a Mine run brings per site hour on a spot in an ore vein, at 1 Wits star with a tier 1 tool.</summary>
        public static float MiningOrePerHour = 15f;
        /// <summary>The share of MiningOrePerHour a spot outside every ore vein gives (a hidden run, no vein in reach).</summary>
        public static float MiningNoVeinOreShare = 0.4f;
        /// <summary>The ore mix on a vein: the vein's own ores weigh this many times the biome's mix of every ore.</summary>
        public static float MiningVeinWeight = 3f;
        /// <summary>Clay (DigClayItems) a Dig clay run brings per site hour at 1 Wits star with a tier 1 tool (an iron shovel).</summary>
        public static float DigClayPerHour = 40f;
        /// <summary>An item with any of these tags is a mining tool (vanilla: the pickaxes, the stone axe, the auger).</summary>
        public const string DefaultMiningToolTags = "miningTool";
        public static readonly List<string> MiningToolTags = new List<string>(SplitMiningList(DefaultMiningToolTags));
        /// <summary>An item with any of these tags is a digging tool (vanilla: the shovels).</summary>
        public const string DefaultDigToolTags = "shovel";
        public static readonly List<string> DigToolTags = new List<string>(SplitMiningList(DefaultDigToolTags));
        /// <summary>The yield factor of the best tool they carry, by its tier tag (T0, T1, ...); tiers past the list take its last.</summary>
        public const string DefaultMiningToolTierMults = "0.6,1,1.4,1.8";
        public static readonly List<float> MiningToolTierMults = new List<float>(ParseMults(DefaultMiningToolTierMults));
        /// <summary>The stone a Mine run brings (the stone layers' harvest).</summary>
        public static string MiningStoneItem = "resourceRockSmall";
        /// <summary>Empty: the ore mix is read from biomes.xml (each biome's ore veins and what their blocks harvest). Else item:weight,... everywhere.</summary>
        public static string MiningOreMix = "";
        /// <summary>The items a Dig clay run brings, as the ground layers of the spot's biome harvest them; the first is the fallback.</summary>
        public const string DefaultDigClayItems = "resourceClayLump,resourceCrushedSand";
        public static readonly List<string> DigClayItems = new List<string>(SplitMiningList(DefaultDigClayItems));

        private static IEnumerable<string> SplitMiningList(string s)
        {
            if (string.IsNullOrEmpty(s)) yield break;
            foreach (string part in s.Split(','))
            {
                string t = part.Trim();
                if (t.Length > 0) yield return t;
            }
        }

        private static IEnumerable<float> ParseMults(string s)
        {
            foreach (string part in SplitMiningList(s))
            {
                float f;
                if (float.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out f)) yield return Math.Max(0f, Math.Min(20f, f));
            }
        }

        private static void ResetMining()
        {
            MiningRuns = true;
            MiningRunSiteHours = 0f;
            MiningStonePerHour = 30f;
            MiningOrePerHour = 15f;
            MiningNoVeinOreShare = 0.4f;
            MiningVeinWeight = 3f;
            DigClayPerHour = 40f;
            MiningToolTags.Clear();
            MiningToolTags.AddRange(SplitMiningList(DefaultMiningToolTags));
            DigToolTags.Clear();
            DigToolTags.AddRange(SplitMiningList(DefaultDigToolTags));
            MiningToolTierMults.Clear();
            MiningToolTierMults.AddRange(ParseMults(DefaultMiningToolTierMults));
            MiningStoneItem = "resourceRockSmall";
            MiningOreMix = "";
            DigClayItems.Clear();
            DigClayItems.AddRange(SplitMiningList(DefaultDigClayItems));
            // the per-world state of the feature: a new world starts with none of it
            ScMining.Reset();
        }

        private static void ParseMining(DynamicProperties p)
        {
            string v;
            ParseBoolKey(p, "MiningRuns", ref MiningRuns);
            ParseFloat(p, "MiningRunSiteHours", ref MiningRunSiteHours, 0f, 48f);
            ParseFloat(p, "MiningStonePerHour", ref MiningStonePerHour, 0f, 1000f);
            ParseFloat(p, "MiningOrePerHour", ref MiningOrePerHour, 0f, 1000f);
            ParseFloat(p, "MiningNoVeinOreShare", ref MiningNoVeinOreShare, 0f, 1f);
            ParseFloat(p, "MiningVeinWeight", ref MiningVeinWeight, 0f, 100f);
            ParseFloat(p, "DigClayPerHour", ref DigClayPerHour, 0f, 1000f);
            if (p.Values.TryGetValue("MiningToolTags", out v) && !string.IsNullOrEmpty(v)) { MiningToolTags.Clear(); MiningToolTags.AddRange(SplitMiningList(v)); }
            if (p.Values.TryGetValue("DigToolTags", out v) && !string.IsNullOrEmpty(v)) { DigToolTags.Clear(); DigToolTags.AddRange(SplitMiningList(v)); }
            if (p.Values.TryGetValue("MiningToolTierMults", out v) && !string.IsNullOrEmpty(v))
            {
                var m = new List<float>(ParseMults(v));
                if (m.Count > 0) { MiningToolTierMults.Clear(); MiningToolTierMults.AddRange(m); }
            }
            if (p.Values.TryGetValue("MiningStoneItem", out v) && !string.IsNullOrEmpty(v)) MiningStoneItem = v.Trim();
            if (p.Values.TryGetValue("MiningOreMix", out v)) MiningOreMix = (v ?? "").Trim();
            if (p.Values.TryGetValue("DigClayItems", out v) && !string.IsNullOrEmpty(v)) { DigClayItems.Clear(); DigClayItems.AddRange(SplitMiningList(v)); }
            ScMining.ClearCaches();              // the mixes and the tool tags are worked out again from the new keys
        }

        /// <summary>The config line's mining runs part.</summary>
        private static string MiningLine()
        {
            Func<float, string> f = x => x.ToString("0.##", CultureInfo.InvariantCulture);
            var mults = new List<string>();
            foreach (float x in MiningToolTierMults) mults.Add(f(x));
            return MiningRuns
                ? "on siteHours=" + f(ScMining.SiteHours()) + " stone=" + f(MiningStonePerHour) + "/h ore=" + f(MiningOrePerHour) + "/h (off a vein x" + f(MiningNoVeinOreShare)
                  + ", vein weight " + f(MiningVeinWeight) + ") clay=" + f(DigClayPerHour) + "/h tools=" + string.Join("+", MiningToolTags.ToArray()) + "/" + string.Join("+", DigToolTags.ToArray())
                  + " tiers=" + string.Join(",", mults.ToArray()) + " stoneItem=" + MiningStoneItem + " oreMix=" + (MiningOreMix.Length > 0 ? MiningOreMix : "biomes.xml")
                  + " clayItems=" + string.Join("+", DigClayItems.ToArray())
                : "off";
        }
    }
}
