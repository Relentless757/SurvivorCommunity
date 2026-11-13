using System;
using System.Collections.Generic;
using System.Globalization;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round 33b (B33): the config keys of opening bundles (ScBundleOpen). Kept apart from ScConfig.cs's long lists, as
    /// ScConfigNames. Config/blocks.xml, block ConfigSurvivorCommunity, after FuelItems (the packing keys); each key is
    /// commented there. ResetBundles also clears ScBundleOpen's per-world pools (ScConfig.Reset runs at GameStarting). Both
    /// games.
    /// </summary>
    public static partial class ScConfig
    {
        // The default pools: everyday things a scavenger would carry for each resource, item[:weight] (weight 1 when left
        // out). Only items the packing station puts in that resource count (ScBundleOpen warns once and skips any other).
        // Nails are not in the materials pool: they are tagged ammo (the nail gun's), so they pack as ammo.
        public const string DefaultBundleOpenFood =
            "foodCanChili:3,foodCanBeef:2,foodCanChicken:2,foodCanPasta:2,foodCanSoup:2,foodCanPeas:2,foodCanPears:2,foodCanTuna:2,"
            + "foodCanSham:2,foodCanSalmon,foodCanLamb,foodCanMiso,foodCanStock,foodCanDogfood,foodCanCatfood,foodGrilledMeat:2,"
            + "foodBoiledMeat:2,foodBakedPotato,foodCornBread,foodEggBoiled,foodBaconAndEggs,foodVegetableStew,foodMeatStew,foodHoboStew";
        public const string DefaultBundleOpenWater =
            "drinkJarBoiledWater:6,drinkJarRedTea:2,drinkJarGoldenRodTea:2,drinkJarCoffee:2,drinkJarYuccaJuice:2,drinkJarPureMineralWater,"
            + "drinkCanMegaCrush";
        public const string DefaultBundleOpenMeds =
            "medicalBandage:4,medicalFirstAidBandage:3,medicalAloeCream:2,medicalSplint,drugPainkillers:2,drugVitamins,"
            + "drugHerbalAntibiotics:2,drugAntibiotics,medicalFirstAidKit";
        public const string DefaultBundleOpenAmmo =
            "ammo9mmBulletBall:6,ammo762mmBulletBall:4,ammoShotgunShell:4,ammo44MagnumBulletBall:2,ammoArrowStone:2,ammoArrowIron,"
            + "ammoCrossbowBoltStone,ammoCrossbowBoltIron";
        public const string DefaultBundleOpenMaterials =
            "resourceWood:3,resourceForgedIron:3,resourceScrapIron:2,resourceCloth:2,resourceDuctTape:2,resourceMechanicalParts:2,"
            + "resourceGlue,resourceElectricParts,resourceSpring,resourceLeather,resourceMetalPipe";
        public const string DefaultBundleOpenFuel = "ammoGasCan";
        public const string DefaultBundleOpenLuxury =
            "drinkJarBeer:4,drinkJarGrandpasAwesomeSauce,drinkJarGrandpasMoonshine,drinkJarGrandpasLearningElixir";

        /// <summary>The highest share of a bundle's packing value an opened bundle may hold (BundleOpenValueShare above it is cut to this).</summary>
        public const float BundleOpenShareMax = 0.85f;

        /// <summary>
        /// Every SC bundle can be opened (hold it and click, or Open in the backpack): one bundle becomes a random assortment of
        /// its resource's items. False: no Open action at all, the bundles as before round 33b.
        /// </summary>
        public static bool BundleOpen = true;
        /// <summary>
        /// What an opened bundle holds, as a share of its packing value (units x PointsPerBundle&lt;Resource&gt;, the trade value
        /// the packing station counts): below 1, so packing goods and opening the bundle again always loses some. At most
        /// BundleOpenShareMax, and lower still when the community's craftsmen pack cheaply enough to beat it (ScBundleOpen.ShareCap).
        /// </summary>
        public static float BundleOpenValueShare = 0.75f;
        /// <summary>Per resource (index = (int)Resource): the pool as the config spells it, an item[:weight] comma list ("" = that resource's bundles stay closed).</summary>
        public static readonly string[] BundleOpenPools =
        {
            DefaultBundleOpenFood, DefaultBundleOpenMeds, DefaultBundleOpenAmmo, DefaultBundleOpenMaterials, DefaultBundleOpenFuel,
            DefaultBundleOpenWater, DefaultBundleOpenLuxury
        };

        /// <summary>A resource's default pool text.</summary>
        public static string DefaultBundleOpenPool(Resource r)
        {
            switch (r)
            {
                case Resource.Food: return DefaultBundleOpenFood;
                case Resource.Meds: return DefaultBundleOpenMeds;
                case Resource.Ammo: return DefaultBundleOpenAmmo;
                case Resource.Materials: return DefaultBundleOpenMaterials;
                case Resource.Fuel: return DefaultBundleOpenFuel;
                case Resource.Water: return DefaultBundleOpenWater;
                case Resource.Luxury: return DefaultBundleOpenLuxury;
                default: return "";
            }
        }

        /// <summary>The config key of a resource's pool: BundleOpenFood, BundleOpenMeds ... BundleOpenLuxury.</summary>
        public static string BundleOpenPoolKey(Resource r) { return "BundleOpen" + r; }

        private static void ResetBundles()
        {
            BundleOpen = true;
            BundleOpenValueShare = 0.75f;
            for (int i = 0; i < ScNames.ResourceCount; i++) BundleOpenPools[i] = DefaultBundleOpenPool((Resource)i);
            // the per-world state of the feature: this world's pools are resolved again
            ScBundleOpen.Reset();
        }

        private static void ParseBundles(DynamicProperties p)
        {
            ParseBoolKey(p, "BundleOpen", ref BundleOpen);
            ParseFloat(p, "BundleOpenValueShare", ref BundleOpenValueShare, 0.05f, 1f);
            for (int i = 0; i < ScNames.ResourceCount; i++)
            {
                // present = replaces the default (an empty value empties it: that resource's bundles stay closed)
                string s;
                if (p.Values.TryGetValue(BundleOpenPoolKey((Resource)i), out s)) BundleOpenPools[i] = s ?? "";
            }
        }

        /// <summary>
        /// One pool's text, "item[:weight],item[:weight]..." (a weight left out is 1; 0 leaves the item out), into names and
        /// weights; an entry that cannot be read is skipped with a line in problems. Returns the entries read. Pure: no game
        /// state (the offline harness runs it).
        /// </summary>
        public static int ParseBundlePool(string text, List<string> names, List<float> weights, List<string> problems)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            int n = 0;
            foreach (string part in text.Split(','))
            {
                string t = part.Trim();
                if (t.Length == 0) continue;
                string name = t;
                float w = 1f;
                int c = t.IndexOf(':');
                if (c >= 0)
                {
                    name = t.Substring(0, c).Trim();
                    if (!float.TryParse(t.Substring(c + 1).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out w) || w < 0f || float.IsNaN(w) || float.IsInfinity(w))
                    {
                        if (problems != null) problems.Add("'" + t + "': the weight is not a number of 0 or more");
                        continue;
                    }
                }
                if (name.Length == 0)
                {
                    if (problems != null) problems.Add("'" + t + "': no item name");
                    continue;
                }
                if (w <= 0f) continue;          // weight 0: left out on purpose
                names.Add(name);
                weights.Add(w);
                n++;
            }
            return n;
        }
    }
}
