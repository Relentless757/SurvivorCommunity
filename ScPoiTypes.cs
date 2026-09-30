using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SurvivorCommunity
{
    /// <summary>One outpost type: what it yields per day (weights on the Base rate) and the words that find it.</summary>
    public sealed class PoiRow
    {
        public string Id = "";
        public readonly float[] Weight = new float[5];
        public readonly List<string> NameWords = new List<string>();
        public readonly List<string> LocWords = new List<string>();
        public readonly List<string> Themes = new List<string>();
        /// <summary>The resource with the largest weight: decides ties and diminishing returns.</summary>
        public Resource Primary;
    }

    /// <summary>What a POI would be as an outpost, and which rule decided it (shown by "sc poi").</summary>
    public sealed class PoiClass
    {
        public string Prefab = "";
        public string Display = "";
        public int Tier;
        public int SleeperVolumes;
        public bool Eligible;
        /// <summary>The matched row; null for scavenge / none / ineligible.</summary>
        public PoiRow Row;
        /// <summary>"row", "scavenge", "none" or "ineligible".</summary>
        public string Kind = "none";
        /// <summary>The layer that decided: xml, exact, exclude, theme, name, loc, tag, fallback, gate.</summary>
        public string Rule = "";
        public string Why = "";

        public bool Producing { get { return Eligible && (Kind == "row" || Kind == "scavenge"); } }
        public string Label { get { return Kind == "row" && Row != null ? Row.Id : Kind; } }
    }

    /// <summary>
    /// Outposts, milestone 1: what a POI would produce. Config (ConfigSurvivorCommunity, Config/blocks.xml) with
    /// built-in defaults below, so a missing table still classifies:
    ///   OutpostRows class      row id = "yields | prefab-name words | localized-name words | theme tags"
    ///   OutpostPrefabs class   exact prefab name = row id, or "none"
    ///   OutpostExclude, OutpostNoise, OutpostTagFallback, OutpostBase, OutpostTierMult, OutpostMinTier ...
    /// A POI author (or a compat patch) can also set a property SCOutpost = row id / none in the prefab's own .xml.
    ///
    /// Resolution, first layer that decides wins: prefab SCOutpost > OutpostPrefabs > exclude words > ThemeTags >
    /// prefab-name tokens (author tokens dropped) > localized-name tokens > district-tag fallback (off by default)
    /// > scavenge. When one layer matches several rows the rarest primary resource wins: meds > ammo > fuel > food >
    /// materials ("Medical Warehouse" is meds). Words: "abc*" is a prefix; otherwise the token equals the word or the
    /// word + "s"; a word with a space is a phrase matched against the joined tokens ("gas station"). Tokens split
    /// on non-letters, digits and CamelCase. Results are cached per prefab name; host only (ThemeTags, zoning and
    /// custom prefab properties live on the server).
    /// </summary>
    public static class ScPoiTypes
    {
        // ---- tunables (ScConfig parses them from ConfigSurvivorCommunity) ----
        public static readonly float[] Base = { 1.0f, 0.35f, 0.3f, 0.8f, 0.6f };      // food, meds, ammo, mats, fuel: bundles/day at T2, weight 1
        public static float[] TierMult = { 0.6f, 0.8f, 1.0f, 1.15f, 1.3f, 1.5f };
        public static int MinTier = 1;
        public static float DailyCap = 5f;
        public static float ScavengeMult = 0.5f;

        public static readonly List<PoiRow> Rows = new List<PoiRow>();
        private static readonly Dictionary<string, PoiRow> byId = new Dictionary<string, PoiRow>(StringComparer.OrdinalIgnoreCase);
        public static readonly Dictionary<string, string> Exact = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public static readonly HashSet<string> Exclude = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public static readonly HashSet<string> Noise = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>District tag -> row id (e.g. industrial -> junkyard). Empty by default: it caught parking lots.</summary>
        public static readonly Dictionary<string, string> TagFallback = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, PoiClass> cache = new Dictionary<string, PoiClass>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Rarest first: decides between rows matched in the same layer.</summary>
        private static readonly Resource[] Rarity = { Resource.Meds, Resource.Ammo, Resource.Fuel, Resource.Food, Resource.Materials };

        // ---- the built-in table (Config/blocks.xml carries the same, and wins when present) ----
        private static readonly string[][] DefaultRows =
        {
            new[] { "grocery", "food:1.2 | grocery,grocer*,supermarket,market,butcher*,deli,cannery,dairy,shamway,foods,food | grocery,supermarket,market,butcher,foods,shamway | storegrocery" },
            new[] { "diner", "food:1.0 | diner,restaurant,fastfood,bakery,cafe,coffee,pizza*,burger*,taco*,donut*,bar,pub,brewery,smokehouse,saloon,tavern,winery,vineyard,bbq,steakhouse,buffet,kitchen,snack*,icecream | diner,cafe,bakery,pizza,burger*,restaurant,buffet,bar,pub,brewery,wine,smokehouse,saloon,tavern,grill,kitchen,biskitz,swines | " },
            new[] { "farm", "food:1.0 | farm,farmers,barn,orchard | farm,ranch,barn | " },
            new[] { "clinic", "meds:1.0 | hospital*,pharmac*,clinic,medical,medic,pills,dentist,vet,veterinary,asylum,nursing,urgent,healthcare,health,ambulance,vax,ultrascans,morgue,lab,laboratory | hospital,clinic,pharmacy,pills,medical,urgent,pharmaceutical,serum,healing,health,doctor,dental | storepharmacy" },
            new[] { "military", "ammo:1.0,meds:0.3 | army,military,bunker,armory,armoury,barracks | army,military,armory,bunker,army post | sitemilitary" },
            new[] { "gunstore", "ammo:1.0 | gun*,ammo,weapon*,range | gun,guns,bullets,rifle,shotgun,ammo,ammunition,range | storegun" },
            new[] { "police", "ammo:0.7 | police,swat,prison,sheriff,jail,checkpoint | police,sheriff,prison,jail,npd,checkpoint | cpvlaw,cpvjail" },
            new[] { "firestation", "meds:0.6,fuel:0.4 | fire station,firestation | fire district,fire station | stationfire" },
            new[] { "hardware", "mats:1.2 | hardware,tools,tool | hardware,tools | storehardware" },
            new[] { "industry", "mats:1.0 | lumber*,factory,warehouse*,construction,sawmill,mill,workshop,steel,concrete,quarry,mine,mining,depot,autoparts,mechanic,bodyshop,welding,foundry,selfstorage,storage,utility,substation,plant,coal,brick* | lumber,mill,welding,works,metal,construction,warehouse,factory,quarry,mine,supply | " },
            new[] { "junkyard", "mats:0.8 | junkyard,scrap*,salvage,industrial | junkyard,salvage,scrap | " },
            new[] { "gasstation", "fuel:1.0,food:0.2 | gas,gasstation,gas station,fuel,petrol,quicklube,oil,passngas,pump station | gas,fill,oil,fuel,petrol,lube | stationgas,cpvgas" },
            new[] { "refinery", "fuel:1.3 | refinery,truckstop,truck stop | refinery,truck stop,truck plaza | " },
        };

        /// <summary>Vanilla POIs whose names say nothing ("countrytown_business_05" is Butcher Pete's) and known false friends.</summary>
        private static readonly string[] DefaultExact =
        {
            "countrytown_business_05:grocery", "countrytown_business_07:diner", "countrytown_business_10:diner",
            "countrytown_business_14:diner", "countrytown_business_15:diner", "countrytown_business_11:clinic",
            "countrytown_business_12:gunstore", "remnant_business_01:diner", "remnant_business_02:clinic",
            "remnant_business_04:industry", "remnant_business_05:gunstore", "oldwest_business_01:diner",
            "oldwest_business_03:diner", "oldwest_business_09:diner", "oldwest_business_11:diner",
            "oldwest_business_13:clinic", "oldwest_business_10:gunstore", "oldwest_business_02:industry",
            "oldwest_business_14:industry", "oldwest_business_05:gasstation", "oldwest_strip_03:diner",
            "oldwest_strip_02:industry", "oldwest_strip_04:industry", "industrial_strip_09:diner",
            "industrial_strip_10:diner", "survivor_site_02:farm", "factory_02:gunstore", "factory_03:clinic",
            "skyscraper_03:clinic", "docks_01:gasstation", "rest_area_05:gasstation", "lot_industrial_03:gasstation",
            "lot_industrial_09:gasstation", "remnant_mill_01:farm", "store_gun_01:gunstore", "store_grocery_03:grocery",
            "army_camp_09:military", "oldwest_jail:police", "farm_04:clinic", "farm_17:none",
        };

        private const string DefaultExclude = "foliage,foilage,plants,wind,solar,library,libary,pubic,museum,deco";
        private const string DefaultNoise = "xcpv,zztong,by,mplogue,fluffy,panda,voltralux";

        static ScPoiTypes()
        {
            LoadDefaults();
        }

        /// <summary>Per-world reset (ScConfig.Reset): back to the built-in table; EnsureParsed then applies the config.</summary>
        public static void LoadDefaults()
        {
            Rows.Clear(); byId.Clear(); Exact.Clear(); Exclude.Clear(); Noise.Clear(); TagFallback.Clear(); cache.Clear();
            Base[0] = 1.0f; Base[1] = 0.35f; Base[2] = 0.3f; Base[3] = 0.8f; Base[4] = 0.6f;
            TierMult = new[] { 0.6f, 0.8f, 1.0f, 1.15f, 1.3f, 1.5f };
            MinTier = 1; DailyCap = 5f; ScavengeMult = 0.5f;
            foreach (string[] r in DefaultRows) AddRow(r[0], r[1]);
            foreach (string e in DefaultExact) { int c = e.IndexOf(':'); Exact[e.Substring(0, c)] = e.Substring(c + 1); }
            foreach (string w in DefaultExclude.Split(',')) Exclude.Add(w.Trim());
            foreach (string w in DefaultNoise.Split(',')) Noise.Add(w.Trim());
        }

        /// <summary>Reads the outpost properties and tables of the config block (called from ScConfig.EnsureParsed).</summary>
        public static void Parse(DynamicProperties p)
        {
            if (p == null) return;
            try
            {
                string s;
                if (p.Values.TryGetValue("OutpostBase", out s) && !string.IsNullOrEmpty(s))
                {
                    float[] w = ParseYields(s);
                    for (int i = 0; i < 5; i++) Base[i] = w[i];
                }
                if (p.Values.TryGetValue("OutpostTierMult", out s) && !string.IsNullOrEmpty(s))
                {
                    var list = new List<float>();
                    foreach (string part in s.Split(',')) { float f; if (float.TryParse(part.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out f)) list.Add(f); }
                    if (list.Count > 0) TierMult = list.ToArray();
                }
                if (p.Values.TryGetValue("OutpostMinTier", out s)) { int v; if (int.TryParse(s.Trim(), out v)) MinTier = Math.Max(0, v); }
                if (p.Values.TryGetValue("OutpostDailyCap", out s)) { float v; if (float.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) DailyCap = Math.Max(0f, v); }
                if (p.Values.TryGetValue("OutpostScavengeMult", out s)) { float v; if (float.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) ScavengeMult = Math.Max(0f, v); }
                if (p.Values.TryGetValue("OutpostExclude", out s)) { Exclude.Clear(); foreach (string w in Split(s, ',')) Exclude.Add(w); }
                if (p.Values.TryGetValue("OutpostNoise", out s)) { Noise.Clear(); foreach (string w in Split(s, ',')) Noise.Add(w); }
                if (p.Values.TryGetValue("OutpostTagFallback", out s))
                {
                    TagFallback.Clear();
                    foreach (string pair in Split(s, ','))
                    {
                        int c = pair.IndexOf(':');
                        if (c > 0) TagFallback[pair.Substring(0, c).Trim()] = pair.Substring(c + 1).Trim();
                    }
                }
                DynamicProperties rows;
                if (p.Classes != null && p.Classes.TryGetValue("OutpostRows", out rows) && rows != null && rows.Values.Count > 0)
                {
                    Rows.Clear(); byId.Clear();
                    foreach (var kv in rows.Values) AddRow(kv.Key, kv.Value);
                }
                DynamicProperties exact;
                if (p.Classes != null && p.Classes.TryGetValue("OutpostPrefabs", out exact) && exact != null && exact.Values.Count > 0)
                {
                    Exact.Clear();
                    foreach (var kv in exact.Values) Exact[kv.Key.Trim()] = (kv.Value ?? "").Trim();
                }
                foreach (var kv in Exact)
                    if (!kv.Value.Equals("none", StringComparison.OrdinalIgnoreCase) && FindRow(kv.Value) == null)
                        ScLog.Warn("OutpostPrefabs: " + kv.Key + " names an unknown row '" + kv.Value + "'");
                cache.Clear();
            }
            catch (Exception e)
            {
                ScLog.Err("outpost table parse failed, built-in table in use: " + e.Message);
                LoadDefaults();
            }
        }

        private static IEnumerable<string> Split(string s, char sep)
        {
            if (string.IsNullOrEmpty(s)) yield break;
            foreach (string part in s.Split(sep))
            {
                string t = part.Trim();
                if (t.Length > 0) yield return t;
            }
        }

        /// <summary>"food:1.2,meds:0.3" -> five weights (Resource order).</summary>
        public static float[] ParseYields(string s)
        {
            var w = new float[5];
            foreach (string part in Split(s, ','))
            {
                int c = part.IndexOf(':');
                if (c <= 0) continue;
                Resource r;
                float f;
                if (ScNames.TryResource(part.Substring(0, c), out r)
                    && float.TryParse(part.Substring(c + 1).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out f))
                    w[(int)r] = Math.Max(0f, f);
            }
            return w;
        }

        private static void AddRow(string id, string spec)
        {
            if (string.IsNullOrEmpty(id) || spec == null) return;
            string[] parts = spec.Split('|');
            var row = new PoiRow { Id = id.Trim() };
            float[] w = ParseYields(parts.Length > 0 ? parts[0] : "");
            float best = 0f;
            for (int i = 0; i < 5; i++)
            {
                row.Weight[i] = w[i];
                if (w[i] > best) { best = w[i]; row.Primary = (Resource)i; }
            }
            if (best <= 0f) { ScLog.Warn("OutpostRows: row '" + id + "' yields nothing - skipped"); return; }
            if (parts.Length > 1) foreach (string x in Split(parts[1], ',')) row.NameWords.Add(x.ToLowerInvariant());
            if (parts.Length > 2) foreach (string x in Split(parts[2], ',')) row.LocWords.Add(x.ToLowerInvariant());
            if (parts.Length > 3) foreach (string x in Split(parts[3], ',')) row.Themes.Add(x.ToLowerInvariant());
            Rows.Add(row);
            byId[row.Id] = row;
        }

        public static PoiRow FindRow(string id)
        {
            PoiRow r;
            return !string.IsNullOrEmpty(id) && byId.TryGetValue(id.Trim(), out r) ? r : null;
        }

        // ---- classification ----

        /// <summary>Classifies a placed POI (cached per prefab name: every copy of a prefab is the same kind).</summary>
        public static PoiClass Classify(PrefabInstance pi)
        {
            if (pi == null || pi.prefab == null) return new PoiClass { Kind = "none", Rule = "gate", Why = "no POI" };
            return Classify(pi.prefab, ScSecurity.DisplayName(pi));
        }

        public static PoiClass Classify(Prefab p, string display)
        {
            string name = p.PrefabName ?? "";
            PoiClass c;
            if (cache.TryGetValue(name, out c)) return c;
            c = Decide(name, display ?? "", p.DifficultyTier, SleeperVolumeCount(p), p.Tags.ToString(), p.ThemeTags.ToString(), Custom(p));
            cache[name] = c;
            return c;
        }

        /// <summary>Classifies by name alone ("sc poi classify" for a prefab not placed in this world).</summary>
        public static PoiClass ClassifyName(string prefabName)
        {
            string display = "";
            try { display = Localization.Get(prefabName); if (display == prefabName) display = ""; } catch (Exception) { }
            return Decide(prefabName ?? "", display, -1, -1, "", "", null);
        }

        private static int SleeperVolumeCount(Prefab p)
        {
            try { return p.SleeperVolumeList != null && p.SleeperVolumeList.List != null ? p.SleeperVolumeList.List.Count : 0; }
            catch (Exception) { return 0; }
        }

        private static string Custom(Prefab p)
        {
            try
            {
                string v;
                return p.properties != null && p.properties.Values != null && p.properties.Values.TryGetValue("SCOutpost", out v) ? v : null;
            }
            catch (Exception) { return null; }
        }

        /// <summary>tier / sleepers -1 = unknown (name-only classification: the gate is skipped).</summary>
        private static PoiClass Decide(string name, string display, int tier, int sleepers, string tags, string themes, string custom)
        {
            var c = new PoiClass { Prefab = name, Display = display, Tier = tier, SleeperVolumes = sleepers };
            bool known = tier >= 0;
            c.Eligible = !known || tier >= MinTier || sleepers > 0;

            // 1. the prefab's own SCOutpost property, 2. the exact table (both may name "none")
            string pick = !string.IsNullOrEmpty(custom) ? custom : null;
            string rule = pick != null ? "xml" : null;
            if (pick == null) { string e; if (Exact.TryGetValue(name, out e)) { pick = e; rule = "exact"; } }
            if (pick != null)
            {
                if (pick.Equals("none", StringComparison.OrdinalIgnoreCase)) return Set(c, "none", null, rule, name);
                PoiRow r = FindRow(pick);
                if (r != null) return Gate(Set(c, "row", r, rule, name));
                ScLog.Once("outpost-row-" + pick, "outpost row '" + pick + "' (for " + name + ") does not exist - classifying by words");
            }

            List<string> ntoks = Tokens(name);
            ntoks.RemoveAll(t => Noise.Contains(t));
            // 3. exclude words
            foreach (string t in ntoks) if (Exclude.Contains(t)) return Set(c, "none", null, "exclude", t);
            // 4. theme tags
            PoiRow themeHit = null;
            string themeWhy = null;
            foreach (string t in SplitTags(themes))
            {
                foreach (PoiRow r in Rows)
                {
                    if (!r.Themes.Contains(t)) continue;
                    if (themeHit == null || Rank(r.Primary) < Rank(themeHit.Primary)) { themeHit = r; themeWhy = t; }
                }
            }
            if (themeHit != null) return Gate(Set(c, "row", themeHit, "theme", themeWhy));
            // 5. prefab-name tokens, 6. localized-name tokens
            string why;
            PoiRow hit = Layer(ntoks, true, out why);
            if (hit != null) return Gate(Set(c, "row", hit, "name", why));
            hit = Layer(Tokens(display), false, out why);
            if (hit != null) return Gate(Set(c, "row", hit, "loc", why));
            // 7. district tag fallback (off by default)
            foreach (string t in SplitTags(tags))
            {
                string id;
                if (TagFallback.TryGetValue(t, out id))
                {
                    PoiRow r = FindRow(id);
                    if (r != null) return Gate(Set(c, "row", r, "tag", t));
                }
            }
            // 8. scavenge
            return Gate(Set(c, "scavenge", null, "fallback", "no row matched"));
        }

        private static PoiClass Set(PoiClass c, string kind, PoiRow row, string rule, string why)
        {
            c.Kind = kind; c.Row = row; c.Rule = rule; c.Why = why ?? "";
            return c;
        }

        private static PoiClass Gate(PoiClass c)
        {
            if (c.Eligible) return c;
            c.Why = "tier " + c.Tier + ", " + c.SleeperVolumes + " sleeper volume(s) - would be " + c.Label + " (" + c.Rule + ")";
            c.Kind = "ineligible"; c.Row = null; c.Rule = "gate";
            return c;
        }

        private static int Rank(Resource r) { return Array.IndexOf(Rarity, r); }

        private static IEnumerable<string> SplitTags(string s)
        {
            if (string.IsNullOrEmpty(s)) yield break;
            foreach (string part in s.Replace(" ", "").ToLowerInvariant().Split(','))
                if (part.Length > 0) yield return part;
        }

        /// <summary>Words of one layer: every row that matches, then the rarest primary resource (table order within one).</summary>
        private static PoiRow Layer(List<string> toks, bool nameLayer, out string why)
        {
            why = null;
            if (toks.Count == 0) return null;
            string joined = " " + string.Join(" ", toks.ToArray()) + " ";
            PoiRow best = null;
            foreach (PoiRow r in Rows)
            {
                List<string> words = nameLayer ? r.NameWords : r.LocWords;
                string w = Match(toks, joined, words);
                if (w == null) continue;
                if (best == null || Rank(r.Primary) < Rank(best.Primary)) { best = r; why = r.Id + " by '" + w + "'"; }
            }
            return best;
        }

        private static string Match(List<string> toks, string joined, List<string> words)
        {
            foreach (string w in words)
            {
                if (w.IndexOf(' ') >= 0)
                {
                    if (joined.Contains(" " + w + " ")) return w;
                    continue;
                }
                bool prefix = w.EndsWith("*");
                string stem = prefix ? w.Substring(0, w.Length - 1) : w;
                foreach (string t in toks)
                {
                    if (prefix ? t.StartsWith(stem, StringComparison.Ordinal) : (t == stem || t == stem + "s")) return w;
                }
            }
            return null;
        }

        /// <summary>Lower-case letter runs; CamelCase and digits split words ("Andys_GunWorks2" -> andys gun works).</summary>
        public static List<string> Tokens(string s)
        {
            var toks = new List<string>();
            if (string.IsNullOrEmpty(s)) return toks;
            var sb = new StringBuilder();
            char prev = '\0';
            foreach (char ch in s)
            {
                bool letter = char.IsLetter(ch);
                if (!letter || (char.IsUpper(ch) && char.IsLower(prev)))
                {
                    if (sb.Length > 0) { toks.Add(sb.ToString()); sb.Length = 0; }
                }
                if (letter) sb.Append(char.ToLowerInvariant(ch));
                prev = ch;
            }
            if (sb.Length > 0) toks.Add(sb.ToString());
            return toks;
        }

        // ---- yield (milestone 1 shows it; the daily tick in M2 adds duplicates, upkeep and the cap) ----

        public static float TierMultiplier(int tier)
        {
            if (TierMult == null || TierMult.Length == 0) return 1f;
            if (tier < 0) tier = 2;
            return TierMult[Math.Min(tier, TierMult.Length - 1)];
        }

        /// <summary>Bundles per day for a row at a tier (no duplicates, upkeep or staff), capped at DailyCap in total.</summary>
        public static float[] RowYield(PoiRow row, int tier)
        {
            var y = new float[5];
            if (row == null) return y;
            float m = TierMultiplier(tier), total = 0f;
            for (int i = 0; i < 5; i++) { y[i] = Base[i] * row.Weight[i] * m; total += y[i]; }
            if (DailyCap > 0f && total > DailyCap) for (int i = 0; i < 5; i++) y[i] *= DailyCap / total;
            return y;
        }

        /// <summary>A scavenge outpost's rate for one resource (it yields whatever the community is shortest of).</summary>
        public static float ScavengeYield(Resource r, int tier)
        {
            return Math.Min(DailyCap > 0f ? DailyCap : float.MaxValue, ScavengeMult * Base[(int)r] * TierMultiplier(tier));
        }

        /// <summary>"+1.56 food, +0.16 meds" (only what is non-zero; "scavenge" for the fallback).</summary>
        public static string YieldText(PoiClass c)
        {
            if (c == null || !c.Producing) return "-";
            if (c.Kind == "scavenge") return "scavenge: +" + ScavengeYield(Resource.Food, c.Tier).ToString("0.00", CultureInfo.InvariantCulture) + " food or equivalent of what you lack";
            float[] y = RowYield(c.Row, c.Tier);
            var sb = new StringBuilder();
            for (int i = 0; i < 5; i++)
            {
                if (y[i] <= 0f) continue;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append('+').Append(y[i].ToString("0.00", CultureInfo.InvariantCulture)).Append(' ').Append(ScNames.ResourceKeys[i]);
            }
            return sb.Append(" a day").ToString();
        }
    }
}
