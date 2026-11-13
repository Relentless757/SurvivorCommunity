using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>Round 33c (M33): what a run is for - the stockpile's resources (a supply run) or real items dug out of the ground.</summary>
    public enum RunJob
    {
        /// <summary>A supply run for one of the stockpile's resources (Member.RunKind).</summary>
        Supply = 0,
        /// <summary>Mine: stone, and the ores of the spot's biome (its vein first).</summary>
        Mine = 1,
        /// <summary>Dig clay: what the spot's ground layers give a shovel (clay, sand in the desert).</summary>
        Clay = 2
    }

    /// <summary>
    /// Round 33c (M33, the user's decision 2026-11-13; 3.4 only): mining runs. 0-XNPCCore's Mine and Dig clay are field jobs a
    /// resident is never given at home (round 29a); instead they are run kinds beside the supply runs. A resident who carries a
    /// mining tool (any item tagged MiningToolTags: the pickaxes, the work kit's stone axe, the auger) or a digging tool
    /// (DigToolTags: any shovel, the kit's stone shovel) - in the toolbelt, the bag or their inventory window - walks to a real
    /// natural spot RunMinDist..RunMaxDist from home (no POI, no water, no claim, bedroll, outpost or enclave near it), works it
    /// for the site hours and walks back, with the supply runs' legs, dice, escort, distress call and radio (ScRunners).
    ///
    /// The spot is read from the world's own biome data with no chunk loaded: the biome map and the sub-biome noise
    /// (IBiomeProvider.GetSubBiomeIdxAt) say whether it lies in one of biomes.xml's ore veins. Mine prefers a vein, nearest first;
    /// Dig clay the ground whose layers give the most clay. The haul is real items, worked out as they leave the site: stone
    /// (MiningStoneItem) plus ore items in the mix biomes.xml gives that biome - each vein's resource blocks and what blocks.xml
    /// says they harvest - with the spot's own vein weighed MiningVeinWeight times; for clay the spot's ground layers' harvest,
    /// limited to DigClayItems. Amount: per site hour, x the best tool's tier factor, (1 + RunYieldPerWitsStar per Wits star
    /// above 1), the traits' run yield, x1.25 at high morale; the dice halve it as they halve a supply run's.
    ///
    /// Delivery: at the gate (where a supply run counts its haul) the items go into the home crates - the stacks there first,
    /// then empty slots, as round 29a's home jobs do - and what does not fit goes into the runner's inventory window, said. With
    /// the home's chunks unloaded (you are away) the haul waits on the record (Member.RunHaul, saved as haul=) and is put away
    /// by Tick once they are. A hidden run (no spot in reach, PhysicalRuns off) brings the home biome's mix, off a vein, at the
    /// day rollover, put away the same way. Host only, main thread.
    /// </summary>
    public static class ScMining
    {
        private const int SpotSamples = 72;
        /// <summary>No prefab (POI, street tile, wilderness POI) within this many blocks of a spot.</summary>
        private const int PoiMargin = 16;
        /// <summary>No claim, bedroll, outpost or enclave footprint within this many blocks (GameUtils.CheckForAnyPlayerHome).</summary>
        private const int HomeMargin = 12;
        private const float EnclaveMargin = 60f;
        /// <summary>Another runner's destination this close (m) takes the spot.</summary>
        private const float TakenMargin = 48f;
        /// <summary>The radio's good-haul line from this share of the haul a tier 1 tool brings at 1 star on a vein.</summary>
        private const float GoodHaulShare = 0.6f;

        private static readonly Dictionary<string, Mix> mixes = new Dictionary<string, Mix>();
        private static readonly Dictionary<int, float> nextTry = new Dictionary<int, float>();
        private static readonly Dictionary<string, int> counters = new Dictionary<string, int>();
        private static readonly List<Member> scratch = new List<Member>();
        private static FastTags<TagGroup.Global> mineTags, digTags;
        private static bool tagsParsed;
        private static FastTags<TagGroup.Global>[] tierTags;
        private static float nextTick;

        /// <summary>Per world (ScConfig.ResetMining): the biome mixes, the delivery retries, the counters.</summary>
        public static void Reset()
        {
            ClearCaches();
            nextTry.Clear();
            counters.Clear();
            nextTick = 0f;
        }

        /// <summary>A config parse: the mixes and the tool tags are worked out again from the new keys.</summary>
        public static void ClearCaches()
        {
            mixes.Clear();
            tagsParsed = false;
        }

        private static void Count(string what) { Count(what, 1); }

        private static void Count(string what, int by)
        {
            if (by <= 0) return;
            int n;
            counters.TryGetValue(what, out n);
            counters[what] = n + by;
        }

        public static string Counters()
        {
            if (counters.Count == 0) return "none";
            var parts = new List<string>();
            foreach (var kv in counters) parts.Add(kv.Key + " " + kv.Value);
            parts.Sort(StringComparer.Ordinal);
            return string.Join(", ", parts.ToArray());
        }

        // ------------------------------------------------------------------ names ----

        public static bool IsDig(Member m) { return m != null && m.RunJob != RunJob.Supply; }

        /// <summary>The console's and the dialog's names: mine | clay (and a few aliases).</summary>
        public static bool TryJob(string s, out RunJob job)
        {
            job = RunJob.Supply;
            if (string.IsNullOrEmpty(s)) return false;
            switch (s.Trim().ToLowerInvariant())
            {
                case "mine": case "mining": case "ore": case "stone": job = RunJob.Mine; return true;
                case "clay": case "dig": case "digclay": job = RunJob.Clay; return true;
                default: return false;
            }
        }

        public static string Key(RunJob j) { return j == RunJob.Mine ? "mine" : j == RunJob.Clay ? "clay" : "supply"; }

        /// <summary>What they went out for, in the run's lines ("stone and ore", "clay").</summary>
        public static string What(RunJob j) { return Localization.Get(j == RunJob.Clay ? "scRunWhatClay" : "scRunWhatMine"); }

        /// <summary>The site hours of a mining run: MiningRunSiteHours, 0 = the supply runs' RunSiteHours.</summary>
        public static float SiteHours() { return ScConfig.MiningRunSiteHours > 0f ? ScConfig.MiningRunSiteHours : ScConfig.RunSiteHours; }

        // ------------------------------------------------------------------ tools ----

        private static void ParseTags()
        {
            if (tagsParsed) return;
            mineTags = FastTags<TagGroup.Global>.Parse(string.Join(",", ScConfig.MiningToolTags.ToArray()));
            digTags = FastTags<TagGroup.Global>.Parse(string.Join(",", ScConfig.DigToolTags.ToArray()));
            if (tierTags == null)
            {
                tierTags = new FastTags<TagGroup.Global>[7];
                for (int t = 0; t < tierTags.Length; t++) tierTags[t] = FastTags<TagGroup.Global>.Parse("T" + t);
            }
            tagsParsed = true;
        }

        /// <summary>Is this item a tool for the job: a mining tool for Mine, a digging tool for Dig clay?</summary>
        public static bool IsTool(ItemClass ic, RunJob job)
        {
            if (ic == null || job == RunJob.Supply) return false;
            ParseTags();
            return ic.HasAnyTags(job == RunJob.Clay ? digTags : mineTags);
        }

        /// <summary>The item's tier: its T0..T6 tag, else the T&lt;n&gt; in its name (meleeToolPickT2SteelPickaxe), else 1.</summary>
        public static int TierOf(ItemClass ic)
        {
            if (ic == null) return 0;
            ParseTags();
            for (int t = tierTags.Length - 1; t >= 0; t--) if (ic.ItemTags.Test_AnySet(tierTags[t])) return t;
            string n = ic.GetItemName() ?? "";
            for (int i = 0; i + 1 < n.Length; i++)
                if (n[i] == 'T' && char.IsDigit(n[i + 1]) && (i + 2 >= n.Length || !char.IsDigit(n[i + 2]))) return n[i + 1] - '0';
            return 1;
        }

        /// <summary>The yield factor of a tool tier (MiningToolTierMults; a tier past the list takes its last; no tool: 0).</summary>
        public static float TierMult(int tier)
        {
            var l = ScConfig.MiningToolTierMults;
            if (tier < 0 || l.Count == 0) return tier < 0 ? 0f : 1f;
            return l[Math.Min(tier, l.Count - 1)];
        }

        /// <summary>
        /// The best tool for the job they carry - toolbelt (where the work kit lives), bag, inventory window: the highest tier, then
        /// the highest quality. where: "work kit" for a kit item in the toolbelt, else the store.
        /// </summary>
        public static bool BestTool(EntityAlive npc, RunJob job, out string item, out int tier, out string where)
        {
            item = null;
            tier = -1;
            where = null;
            if (npc == null) return false;
            int quality = -1;
            string bi = null, bw = null;
            int bt = -1;
            try
            {
                Look(npc.inventory != null && npc.inventory.ItemGrid != null ? npc.inventory.ItemGrid.items : null, "toolbelt", job, ref bi, ref bt, ref quality, ref bw);
                Look(npc.bag != null && npc.bag.ItemGrid != null ? npc.bag.ItemGrid.items : null, "bag", job, ref bi, ref bt, ref quality, ref bw);
                if (HarvestManager.Has(npc.entityId)) Look(HarvestManager.GetOrCreate(npc.entityId).items, "inventory", job, ref bi, ref bt, ref quality, ref bw);
            }
            catch (Exception e)
            {
                ScLog.Once("mining-tool", e);
            }
            if (bi == null) return false;
            item = bi;
            tier = bt;
            where = bw == "toolbelt" && ScConfig.MemberWorkKit.Contains(bi) ? "work kit" : bw;
            return true;
        }

        private static void Look(ItemStack[] items, string store, RunJob job, ref string best, ref int bestTier, ref int bestQuality, ref string bestWhere)
        {
            if (items == null) return;
            for (int i = 0; i < items.Length; i++)
            {
                ItemStack s = items[i];
                if (s == null || s.IsEmpty() || s.itemValue == null) continue;
                ItemClass ic = s.itemValue.ItemClass;
                if (!IsTool(ic, job)) continue;
                int t = TierOf(ic), q = s.itemValue.Quality;
                if (t < bestTier || (t == bestTier && q <= bestQuality)) continue;
                best = ic.GetItemName();
                bestTier = t;
                bestQuality = q;
                bestWhere = store;
            }
        }

        // ------------------------------------------------------------------ the biome mixes (biomes.xml + blocks.xml) ----

        /// <summary>One biome's mixes, normalized weights by item name: every ore, each sub-biome's vein, the ground's clay.</summary>
        private sealed class Mix
        {
            public string Label = "";
            public readonly Dictionary<string, float> Ore = new Dictionary<string, float>();
            public readonly List<Dictionary<string, float>> SubOre = new List<Dictionary<string, float>>();
            /// <summary>Per sub-biome: the vein's main ore block, its display name (null: no vein).</summary>
            public readonly List<string> SubVein = new List<string>();
            public Dictionary<string, float> Clay = new Dictionary<string, float>();
            public readonly List<Dictionary<string, float>> SubClay = new List<Dictionary<string, float>>();
        }

        private static Mix MixFor(BiomeDefinition bd)
        {
            if (bd == null) return null;
            Mix x;
            string key = bd.m_sBiomeName ?? "";
            if (mixes.TryGetValue(key, out x)) return x;
            x = new Mix();
            try
            {
                x.Label = !string.IsNullOrEmpty(bd.LocalizedName) ? bd.LocalizedName : key;
                var all = new Dictionary<string, float>();
                float cover = 0f;
                for (int i = 0; i < bd.subbiomes.Count; i++)
                {
                    BiomeDefinition sb = bd.subbiomes[i];
                    // the share of the biome a sub-biome covers: its noise window (the first match wins in the game - near enough)
                    float c = sb != null ? Mathf.Clamp01(sb.noiseMax - sb.noiseMin) : 0f;
                    string vein = null;
                    Dictionary<string, float> ore = sb != null ? LayersOre(sb.m_Layers, out vein) : new Dictionary<string, float>();
                    if (ore.Count == 0) vein = null;
                    cover += c;
                    foreach (var kv in ore) Add(all, kv.Key, kv.Value * c);
                    x.SubOre.Add(Normalized(ore));
                    x.SubVein.Add(vein);
                    x.SubClay.Add(sb != null && sb.m_Layers != null && sb.m_Layers.Count > 0 ? Normalized(LayersClay(sb.m_Layers)) : null);
                }
                string mainVein;
                Dictionary<string, float> main = LayersOre(bd.m_Layers, out mainVein);
                foreach (var kv in main) Add(all, kv.Key, kv.Value * Mathf.Max(0.1f, 1f - cover));
                foreach (var kv in Normalized(all)) x.Ore[kv.Key] = kv.Value;
                x.Clay = Normalized(LayersClay(bd.m_Layers));
                for (int i = 0; i < x.SubClay.Count; i++) if (x.SubClay[i] == null || x.SubClay[i].Count == 0) x.SubClay[i] = x.Clay;
            }
            catch (Exception e)
            {
                ScLog.Once("mining-mix-" + key, e);
            }
            mixes[key] = x;
            ScLog.Verbose("mining mix for " + key + ": ore [" + Weights(x.Ore) + "], clay [" + Weights(x.Clay) + "], " + x.SubVein.FindAll(v => v != null).Count + " vein sub-biome(s) of " + x.SubVein.Count);
            return x;
        }

        private static void Add(Dictionary<string, float> d, string k, float v)
        {
            if (v <= 0f || string.IsNullOrEmpty(k)) return;
            float o;
            d.TryGetValue(k, out o);
            d[k] = o + v;
        }

        private static Dictionary<string, float> Normalized(Dictionary<string, float> d)
        {
            var r = new Dictionary<string, float>();
            float sum = 0f;
            foreach (var kv in d) sum += kv.Value;
            if (sum <= 0f) return r;
            foreach (var kv in d) r[kv.Key] = kv.Value / sum;
            return r;
        }

        private static string Weights(Dictionary<string, float> d)
        {
            var parts = new List<string>();
            foreach (var kv in d) parts.Add(kv.Key + " " + kv.Value.ToString("0.00", CultureInfo.InvariantCulture));
            return string.Join(", ", parts.ToArray());
        }

        private static bool Rock(Block b)
        {
            string c = b != null && b.blockMaterial != null ? b.blockMaterial.DamageCategory : null;
            return c == "stone" || c == "metal";
        }

        /// <summary>
        /// What a block gives when harvested with a tool: its Harvest drops that always fall (prob 0.5 or more) and carry no perk
        /// tag (none, or oreWoodHarvest - not the brass, lucky strike or coal perks' extras), as (item, mean count). Falls back
        /// to every likely drop when none matches.
        /// </summary>
        private static List<KeyValuePair<string, float>> Harvest(Block b, Func<string, bool> keep)
        {
            var r = new List<KeyValuePair<string, float>>();
            List<Block.SItemDropProb> drops;
            if (b == null || b.itemsToDrop == null || !b.itemsToDrop.TryGetValue(EnumDropEvent.Harvest, out drops) || drops == null) return r;
            for (int pass = 0; pass < 2 && r.Count == 0; pass++)
            {
                foreach (Block.SItemDropProb d in drops)
                {
                    if (string.IsNullOrEmpty(d.name) || d.prob < 0.5f || !keep(d.name)) continue;
                    if (pass == 0 && !string.IsNullOrEmpty(d.tag) && d.tag.IndexOf("oreWoodHarvest", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    r.Add(new KeyValuePair<string, float>(d.name, 0.5f * (d.minCount + d.maxCount) * Mathf.Min(1f, d.prob)));
                }
            }
            return r;
        }

        /// <summary>The ore of a layer list: each rock (stone or metal) resource block, by its prob x what it harvests, the stone left out.</summary>
        private static Dictionary<string, float> LayersOre(List<BiomeLayer> layers, out string vein)
        {
            vein = null;
            var d = new Dictionary<string, float>();
            if (layers == null) return d;
            var byBlock = new Dictionary<string, float>();
            string stone = ScConfig.MiningStoneItem;
            foreach (BiomeLayer l in layers)
            {
                if (l == null || l.m_Resources == null) continue;
                foreach (BiomeBlockDecoration res in l.m_Resources)
                {
                    if (res == null || res.blockValues == null || res.blockValues.Length == 0) continue;
                    foreach (BlockValue bv in res.blockValues)
                    {
                        Block b = bv.Block;
                        if (bv.isair || !Rock(b)) continue;
                        float share = res.prob / res.blockValues.Length;
                        float got = 0f;
                        foreach (var kv in Harvest(b, n => n != stone)) { Add(d, kv.Key, kv.Value * share); got += kv.Value * share; }
                        if (got > 0f) Add(byBlock, b.GetBlockName(), got);
                    }
                }
            }
            float best = 0f;
            foreach (var kv in byBlock) if (kv.Value > best) { best = kv.Value; vein = kv.Key; }
            return d;
        }

        /// <summary>
        /// The clay of a layer list: the earth layers from the top down, by depth (at most 4) x their DigClayItems harvest. A thin
        /// rock cover on top (the wasteland's rubble) is dug through; the first rock layer under earth, or a bottomless one, ends it.
        /// </summary>
        private static Dictionary<string, float> LayersClay(List<BiomeLayer> layers)
        {
            var d = new Dictionary<string, float>();
            if (layers == null) return d;
            var items = ScConfig.DigClayItems;
            bool earthSeen = false;
            foreach (BiomeLayer l in layers)
            {
                if (l == null || l.m_Block == null || l.m_Block.blockValues == null || l.m_Block.blockValues.Length == 0) continue;
                bool rock = false, earth = false;
                int depth = l.m_Depth <= 0 ? 4 : Math.Min(4, l.m_Depth);
                foreach (BlockValue bv in l.m_Block.blockValues)
                {
                    Block b = bv.Block;
                    if (bv.isair || b == null) continue;
                    if (Rock(b)) { rock = true; continue; }
                    earth = true;
                    foreach (var kv in Harvest(b, n => items.Contains(n))) Add(d, kv.Key, kv.Value * depth / l.m_Block.blockValues.Length);
                }
                if (earth) earthSeen = true;
                else if (rock && (earthSeen || l.m_Depth <= 0)) break;
            }
            return d;
        }

        /// <summary>The fixed mix of MiningOreMix (item:weight,...), or null when the key is empty.</summary>
        private static Dictionary<string, float> FixedOre()
        {
            if (string.IsNullOrEmpty(ScConfig.MiningOreMix)) return null;
            var d = new Dictionary<string, float>();
            foreach (string part in ScConfig.MiningOreMix.Split(','))
            {
                string[] kv = part.Split(':');
                float wgt = 1f;
                if (kv.Length >= 1 && kv[0].Trim().Length > 0 && (kv.Length < 2 || float.TryParse(kv[1], NumberStyles.Float, CultureInfo.InvariantCulture, out wgt)))
                    Add(d, kv[0].Trim(), wgt);
            }
            return Normalized(d);
        }

        /// <summary>No ore data for the biome (a modded one without veins, the biome gone): the four common ores, iron twice.</summary>
        private static Dictionary<string, float> FallbackOre()
        {
            var d = new Dictionary<string, float> { { "resourceScrapIron", 2f }, { "resourceCoal", 1f }, { "resourceScrapLead", 1f }, { "resourcePotassiumNitratePowder", 1f } };
            return Normalized(d);
        }

        /// <summary>The ore mix of a spot: the vein's own ores x MiningVeinWeight plus the biome's mix. vein: the spot lies on one.</summary>
        private static Dictionary<string, float> OreFor(Mix x, int sub, out bool vein)
        {
            Dictionary<string, float> sv = x != null && sub >= 0 && sub < x.SubOre.Count ? x.SubOre[sub] : null;
            vein = sv != null && sv.Count > 0;
            Dictionary<string, float> fixd = FixedOre();
            if (fixd != null && fixd.Count > 0) return fixd;
            Dictionary<string, float> biome = x != null && x.Ore.Count > 0 ? x.Ore : FallbackOre();
            if (!vein) return biome;
            var d = new Dictionary<string, float>();
            foreach (var kv in sv) Add(d, kv.Key, kv.Value * Mathf.Max(0f, ScConfig.MiningVeinWeight));
            foreach (var kv in biome) Add(d, kv.Key, kv.Value);
            return Normalized(d);
        }

        private static Dictionary<string, float> ClayFor(Mix x, int sub)
        {
            Dictionary<string, float> c = x == null ? null : sub >= 0 && sub < x.SubClay.Count ? x.SubClay[sub] : x.Clay;
            if (c != null && c.Count > 0) return c;
            var d = new Dictionary<string, float>();
            if (ScConfig.DigClayItems.Count > 0) d[ScConfig.DigClayItems[0]] = 1f;
            return d;
        }

        /// <summary>The first DigClayItems item's share of a spot's dig (the Dig clay spot score; 0.5 for ground of unknown make).</summary>
        private static float ClayShare(Mix x, int sub)
        {
            if (ScConfig.DigClayItems.Count == 0) return 0f;
            Dictionary<string, float> c = x == null ? null : sub >= 0 && sub < x.SubClay.Count ? x.SubClay[sub] : x.Clay;
            if (c == null || c.Count == 0) return 0.5f;
            float s;
            return c.TryGetValue(ScConfig.DigClayItems[0], out s) ? s : 0f;
        }

        // ------------------------------------------------------------------ the spot ----

        /// <summary>Where a mining run goes: a natural spot (its block position), its biome and sub-biome, and its name for the run's lines.</summary>
        public sealed class Spot
        {
            public Vector3i Pos;
            public string Biome = "";
            public int Sub = -1;
            public bool Vein;
            public string Name = "";
            public float Dist;
            public float Score;
        }

        /// <summary>
        /// A natural spot RunMinDist..RunMaxDist from home, from SpotSamples random points around it: inside the world, not
        /// water, a real biome; no prefab within PoiMargin, no claim, bedroll, outpost or enclave within HomeMargin, no enclave
        /// within EnclaveMargin, no other runner's destination within TakenMargin. Mine: a spot on an ore vein first; Dig clay: the
        /// ground with the most clay (within 0.15 of the best). Of those the nearest three, one at random. Null when none.
        /// </summary>
        public static Spot PickSpot(RunJob job, GameRandom rnd)
        {
            try
            {
                World w = ScCommunity.World;
                if (w == null || !ScZone.HasHome || job == RunJob.Supply) return null;
                IBiomeProvider bp = w.ChunkCache != null && w.ChunkCache.ChunkProvider != null ? w.ChunkCache.ChunkProvider.GetBiomeProvider() : null;
                DynamicPrefabDecorator dec = GameManager.Instance != null ? GameManager.Instance.GetDynamicPrefabDecorator() : null;
                Vector3i home = ScZone.Home;
                int min = ScConfig.RunMinDist, max = Math.Max(ScConfig.RunMinDist + 50, ScConfig.RunMaxDist);
                Vector3i wmin, wmax;
                bool bounded = w.GetWorldExtent(out wmin, out wmax);
                var taken = new List<Vector3>();
                foreach (Member om in ScRegistry.Members.Values)
                    if (ScRunners.IsPhysical(om)) { taken.Add(ScRunners.C(om.RunDest)); if (om.RunSite != Vector3i.zero) taken.Add(ScRunners.C(om.RunSite)); }
                var ok = new List<Spot>();
                int rejected = 0;
                for (int i = 0; i < SpotSamples; i++)
                {
                    float ang = (rnd != null ? rnd.RandomFloat : UnityEngine.Random.value) * Mathf.PI * 2f;
                    float r = min + (rnd != null ? rnd.RandomFloat : UnityEngine.Random.value) * (max - min);
                    int x = home.x + Mathf.RoundToInt(Mathf.Cos(ang) * r), z = home.z + Mathf.RoundToInt(Mathf.Sin(ang) * r);
                    if (bounded && (x < wmin.x + 64 || x > wmax.x - 64 || z < wmin.z + 64 || z > wmax.z - 64)) { rejected++; continue; }
                    if (w.GetWaterAt(x, z)) { rejected++; continue; }
                    BiomeDefinition bd = w.GetBiomeInWorld(x, z);
                    if (bd == null || bd.m_BiomeType == BiomeDefinition.BiomeType.Water || bd.m_BiomeType == BiomeDefinition.BiomeType.caveFloor
                        || bd.m_BiomeType == BiomeDefinition.BiomeType.caveCeiling || string.Equals(bd.m_sBiomeName, "underwater", StringComparison.OrdinalIgnoreCase)) { rejected++; continue; }
                    if (dec != null && dec.HasPrefabsAtXZ(x - PoiMargin, x + PoiMargin, z - PoiMargin, z + PoiMargin)) { rejected++; continue; }
                    if (GameUtils.CheckForAnyPlayerHome(w, new Vector3i(x - HomeMargin, 0, z - HomeMargin), new Vector3i(x + HomeMargin, 255, z + HomeMargin)) != GameUtils.EPlayerHomeType.None) { rejected++; continue; }
                    Vector3 p = new Vector3(x + 0.5f, 0f, z + 0.5f);
                    if (ScEnclaves.NearEnclave(p, EnclaveMargin)) { rejected++; continue; }
                    bool near = false;
                    foreach (Vector3 tp in taken) if (ScRunners.XZ(tp, p) < TakenMargin) { near = true; break; }
                    if (near) { rejected++; continue; }
                    float h = w.GetHeightAt(x, z);
                    if (h < 2f) { rejected++; continue; }
                    int y = Mathf.FloorToInt(h) + 1;
                    int sub = -1;
                    try { if (bp != null) sub = bp.GetSubBiomeIdxAt(bd, x, y, z); }
                    catch (Exception e) { ScLog.Once("mining-subbiome", e); }
                    Mix mx = MixFor(bd);
                    var s = new Spot { Pos = new Vector3i(x, y, z), Biome = bd.m_sBiomeName ?? "", Sub = sub };
                    s.Dist = ScOutposts.DistanceToHome(s.Pos, Vector3i.one);
                    if (job == RunJob.Mine)
                    {
                        bool vein;
                        OreFor(mx, sub, out vein);
                        s.Vein = vein;
                        s.Score = vein ? 2f : 1f;
                    }
                    else s.Score = ClayShare(mx, sub);
                    ok.Add(s);
                }
                if (ok.Count == 0)
                {
                    ScLog.Out("mining: no natural spot " + min + "-" + max + " m from home for " + Key(job) + " (" + rejected + " of " + SpotSamples + " points refused)");
                    return null;
                }
                float top = 0f;
                foreach (Spot s in ok) top = Mathf.Max(top, s.Score);
                float floor = job == RunJob.Mine ? top : top - 0.15f;
                var pool = ok.FindAll(s => s.Score >= floor);
                pool.Sort((a, b) => a.Dist.CompareTo(b.Dist));
                Spot pick = pool[rnd != null ? rnd.RandomRange(0, Math.Min(3, pool.Count)) : 0];
                pick.Name = SpotName(job, pick);
                ScLog.Out("mining: " + Key(job) + " spot " + pick.Pos + " (" + Mathf.RoundToInt(pick.Dist) + " m, " + pick.Biome + " sub-biome " + pick.Sub
                          + (pick.Vein ? ", on a vein" : "") + ", score " + pick.Score.ToString("0.00", CultureInfo.InvariantCulture) + ") of " + ok.Count + " usable point(s), " + rejected + " refused");
                return pick;
            }
            catch (Exception e)
            {
                ScLog.Once("mining-pickspot", e);
                return null;
            }
        }

        /// <summary>"an outcrop of Iron Ore (Pine Forest)", "rocky ground (Desert)", "a clay bank (Forest)", "sandy ground (Desert)".</summary>
        private static string SpotName(RunJob job, Spot s)
        {
            World w = ScCommunity.World;
            BiomeDefinition bd = w != null && w.Biomes != null ? w.Biomes.GetBiome(s.Biome) : null;
            Mix x = MixFor(bd);
            string biome = x != null && x.Label.Length > 0 ? x.Label : s.Biome;
            if (job == RunJob.Clay)
                return string.Format(Localization.Get(ClayShare(x, s.Sub) >= 0.5f ? "scClaySiteBank" : "scClaySiteSand"), biome);
            string vein = x != null && s.Sub >= 0 && s.Sub < x.SubVein.Count ? x.SubVein[s.Sub] : null;
            if (s.Vein && vein != null)
            {
                Block b = Block.GetBlockByName(vein, true);
                string label = b != null ? b.GetLocalizedBlockName() : vein;
                return string.Format(Localization.Get("scMineSiteVein"), label, biome);
            }
            return string.Format(Localization.Get("scMineSiteRock"), biome);
        }

        // ------------------------------------------------------------------ the haul ----

        /// <summary>The run's multiplier: the tool's tier x (1 + RunYieldPerWitsStar per Wits star above 1) x traits x 1.25 at high morale.</summary>
        public static float Mult(Member m, int tier)
        {
            float y = TierMult(tier);
            y *= 1f + ScConfig.RunYieldPerWitsStar * (m.Stars(Skill.Wits) - 1);
            y *= ScTraits.RunYieldMult(m);
            if (ScRegistry.Community.Morale > ScConfig.HighMoraleThreshold) y *= 1.25f;
            return Mathf.Max(0f, y);
        }

        /// <summary>The haul (item, count; the stone first) of share of the site hours, at the spot the record names.</summary>
        public static List<KeyValuePair<string, int>> Haul(Member m, float share)
        {
            var haul = new List<KeyValuePair<string, int>>();
            if (!IsDig(m)) return haul;
            World w = ScCommunity.World;
            BiomeDefinition bd = w != null && w.Biomes != null && !string.IsNullOrEmpty(m.RunBiome) ? w.Biomes.GetBiome(m.RunBiome) : null;
            Mix x = MixFor(bd);
            float k = Mult(m, m.RunToolTier) * SiteHours() * Mathf.Clamp01(share);
            if (m.RunJob == RunJob.Mine)
            {
                Put(haul, ScConfig.MiningStoneItem, ScConfig.MiningStonePerHour * k);
                bool vein;
                Dictionary<string, float> ore = OreFor(x, m.RunSub, out vein);
                float units = ScConfig.MiningOrePerHour * k * (vein ? 1f : ScConfig.MiningNoVeinOreShare);
                var keys = new List<string>(ore.Keys);
                keys.Sort((a, b) => ore[b].CompareTo(ore[a]));
                foreach (string item in keys) Put(haul, item, units * ore[item]);
            }
            else
            {
                Dictionary<string, float> clay = ClayFor(x, m.RunSub);
                float units = ScConfig.DigClayPerHour * k;
                var keys = new List<string>(clay.Keys);
                keys.Sort((a, b) => clay[b].CompareTo(clay[a]));
                foreach (string item in keys) Put(haul, item, units * clay[item]);
            }
            return haul;
        }

        private static void Put(List<KeyValuePair<string, int>> haul, string item, float n)
        {
            int c = Mathf.RoundToInt(n);
            if (c <= 0 || string.IsNullOrEmpty(item)) return;
            if (ItemClass.GetItemClass(item, true) == null) { ScLog.Once("mining-noitem-" + item, "mining: the item " + item + " does not exist - left out of the haul"); return; }
            for (int i = 0; i < haul.Count; i++) if (haul[i].Key == item) { haul[i] = new KeyValuePair<string, int>(item, haul[i].Value + c); return; }
            haul.Add(new KeyValuePair<string, int>(item, c));
        }

        /// <summary>ScRunners.LeaveSite: the haul of this share of the site hours (a rescue's bonus on it) onto the record; the total.</summary>
        public static int TakeHaul(Member m, float share, bool rescued)
        {
            List<KeyValuePair<string, int>> h = Haul(m, share);
            if (rescued) h = Scaled(h, ScConfig.RescueBonus);
            m.RunHaul = Encode(h);
            return Total(h);
        }

        /// <summary>The run's dice: half the haul (a hurt runner, a distress call nobody answered). Mining runs only: a supply run's carry stays its own.</summary>
        public static void Halve(Member m)
        {
            if (!IsDig(m) || string.IsNullOrEmpty(m.RunHaul)) return;
            List<KeyValuePair<string, int>> h = Scaled(Decode(m.RunHaul), 0.5f);
            m.RunHaul = Encode(h);
            m.RunCarry = Total(h);
        }

        private static List<KeyValuePair<string, int>> Scaled(List<KeyValuePair<string, int>> h, float f)
        {
            var r = new List<KeyValuePair<string, int>>();
            foreach (var kv in h)
            {
                int c = f < 1f ? (int)(kv.Value * f) : Mathf.RoundToInt(kv.Value * f);
                if (c > 0) r.Add(new KeyValuePair<string, int>(kv.Key, c));
            }
            return r;
        }

        /// <summary>The expected total for a job at 1 Wits star with a tier 1 tool on a vein, the whole site time (the radio's yardstick, the console).</summary>
        public static int BaseTotal(RunJob job)
        {
            float h = SiteHours();
            return Mathf.RoundToInt(job == RunJob.Clay ? ScConfig.DigClayPerHour * h : (ScConfig.MiningStonePerHour + ScConfig.MiningOrePerHour) * h);
        }

        /// <summary>The radio: a haul worth the good-haul line?</summary>
        public static bool GoodHaul(Member m) { return m != null && m.RunCarry >= Mathf.Max(1f, GoodHaulShare * BaseTotal(m.RunJob)); }

        public static string Encode(List<KeyValuePair<string, int>> h)
        {
            var sb = new StringBuilder();
            foreach (var kv in h)
            {
                if (kv.Value <= 0 || string.IsNullOrEmpty(kv.Key)) continue;
                if (sb.Length > 0) sb.Append(',');
                sb.Append(kv.Key.Replace(",", "").Replace(":", "")).Append(':').Append(kv.Value.ToString(CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        public static List<KeyValuePair<string, int>> Decode(string s)
        {
            var r = new List<KeyValuePair<string, int>>();
            if (string.IsNullOrEmpty(s)) return r;
            foreach (string part in s.Split(','))
            {
                string[] kv = part.Split(':');
                int n;
                if (kv.Length == 2 && kv[0].Trim().Length > 0 && int.TryParse(kv[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out n) && n > 0)
                    r.Add(new KeyValuePair<string, int>(kv[0].Trim(), n));
            }
            return r;
        }

        public static int Total(List<KeyValuePair<string, int>> h)
        {
            int n = 0;
            foreach (var kv in h) n += kv.Value;
            return n;
        }

        public static int Total(string haul) { return Total(Decode(haul)); }

        /// <summary>"180 Small Stone, 43 Scrap Iron and 20 Coal" (localized item names).</summary>
        public static string Describe(List<KeyValuePair<string, int>> h)
        {
            var parts = new List<string>();
            foreach (var kv in h)
            {
                ItemClass ic = ItemClass.GetItemClass(kv.Key, true);
                parts.Add(kv.Value + " " + (ic != null ? ic.GetLocalizedItemName() : kv.Key));
            }
            if (parts.Count == 0) return "";
            if (parts.Count == 1) return parts[0];
            return string.Join(", ", parts.GetRange(0, parts.Count - 1).ToArray()) + " " + Localization.Get("scMineAnd") + " " + parts[parts.Count - 1];
        }

        public static string Describe(string haul) { return Describe(Decode(haul)); }

        // ------------------------------------------------------------------ sending ----

        /// <summary>
        /// Mine or Dig clay, from the talk, the community window or `sc run`: the supply run's checks (home, healthy, willing, not
        /// on watch, MaxRunners), a tool for the job, no earlier haul still to put away; then a physical run to a natural spot, or
        /// - none in reach, PhysicalRuns off - a hidden run with the home biome's mix. error: a localization key.
        /// </summary>
        public static bool Send(Member m, EntityAlive npc, RunJob job, EntityPlayerLocal player, out string error)
        {
            error = null;
            if (job == RunJob.Supply) { error = "scInternal"; return false; }
            if (!ScConfig.MiningRuns) { error = "scMineOff"; return false; }
            if (!ScRuns.CanSend(m, npc, out error)) return false;
            if (!string.IsNullOrEmpty(m.RunHaul)) { error = "scMineHaulPending"; return false; }
            string tool, where;
            int tier;
            if (!BestTool(npc, job, out tool, out tier, out where))
            {
                error = job == RunJob.Clay ? "scClayNoTool" : "scMineNoTool";
                Count(Key(job) + "-refused-no-tool");
                ScLog.Out(m.Name + " was asked to " + (job == RunJob.Clay ? "dig clay" : "mine") + " but carries no " + (job == RunJob.Clay ? "digging" : "mining") + " tool (tags "
                          + string.Join("+", (job == RunJob.Clay ? ScConfig.DigToolTags : ScConfig.MiningToolTags).ToArray()) + ") - refused");
                return false;
            }
            ScWork.EndFor(m, job == RunJob.Clay ? "sent to dig clay" : "sent on a mining run");   // C1: off the work shift first
            m.RunJob = job;
            m.RunTool = tool;
            m.RunToolTier = tier;
            m.RunBiome = "";
            m.RunSub = -1;
            m.RunHaul = "";
            World w = ScCommunity.World;
            GameRandom rnd = w != null ? w.GetGameRandom() : null;
            string how = tool + " (T" + tier + ", " + where + ")";
            if (ScConfig.PhysicalRuns && player != null && player.ChunkObserver != null)
            {
                Spot s = PickSpot(job, rnd);
                if (s != null)
                {
                    m.RunBiome = s.Biome;
                    m.RunSub = s.Sub;
                    if (ScRuns.SendPhysicalTo(m, npc, Resource.Materials, player, s.Pos, s.Name, ScRunners.C(s.Pos), Mathf.RoundToInt(s.Dist), out error))
                    {
                        Count(Key(job) + "-sent-physical");
                        ScLog.Out(m.Name + " takes their " + how + " to " + s.Name + " " + s.Pos + " (" + s.Biome + ", sub-biome " + s.Sub + (s.Vein ? ", a vein" : "") + ")");
                        return true;
                    }
                    ClearJob(m);
                    return false;
                }
                ScLog.Out("no natural spot " + ScConfig.RunMinDist + "-" + ScConfig.RunMaxDist + " m from home to " + Key(job) + " - " + m.Name + " goes on a hidden run instead");
            }
            // hidden: the home's own biome, off a vein
            if (w != null && ScZone.HasHome)
            {
                BiomeDefinition hb = w.GetBiomeInWorld(ScZone.Home.x, ScZone.Home.z);
                if (hb != null) m.RunBiome = hb.m_sBiomeName ?? "";
            }
            if (ScRuns.SendHidden(m, npc, Resource.Materials, player, out error))
            {
                Count(Key(job) + "-sent-hidden");
                ScLog.Out(m.Name + " takes their " + how + " out on a hidden run (" + m.RunBiome + ", off a vein)");
                return true;
            }
            ClearJob(m);
            return false;
        }

        /// <summary>The run's job fields back to a supply run's (ScRunners.ClearRun, a hidden run's end). The haul is not touched.</summary>
        public static void ClearJob(Member m)
        {
            if (m == null) return;
            m.RunJob = RunJob.Supply;
            m.RunTool = "";
            m.RunToolTier = 0;
            m.RunBiome = "";
            m.RunSub = -1;
        }

        /// <summary>ScRuns.Resolve, a hidden mining run's end at the rollover: the haul onto the record (Tick puts it away). The summary text.</summary>
        public static string ResolveHidden(Member m, bool bad, bool rescued)
        {
            List<KeyValuePair<string, int>> h = Haul(m, 1f);
            if (rescued) h = Scaled(h, ScConfig.RescueBonus);
            if (bad) h = Scaled(h, 0.5f);
            m.RunHaul = Encode(h);
            Count(Key(m.RunJob) + "-hidden-back");
            ScSkills.AddXp(m, Skill.Wits, ScConfig.RunWitsXp);
            string text = Describe(h);
            ScLog.Out(m.Name + " is back from a hidden " + Key(m.RunJob) + " run with " + (text.Length > 0 ? Encode(h) : "nothing") + (bad ? " (injured)" : ""));
            return text.Length > 0 ? text : Localization.Get("scMineNothing");
        }

        // ------------------------------------------------------------------ delivery ----

        /// <summary>Wait: the home's crates are not loaded; WaitWindow (round 33 review L5): their inventory window is open.</summary>
        private enum Landed { Nothing, All, Some, NoRoom, Wait, WaitWindow }

        /// <summary>
        /// ScRunners.EnterBack, a mining runner at the gate: Wits XP, the haul into the home crates now (their chunks loaded) or
        /// once they are (Tick), and one line that says both.
        /// </summary>
        public static void AtGate(EntityPlayerLocal player, Member m)
        {
            ScSkills.AddXp(m, Skill.Wits, ScConfig.RunWitsXp);
            string haul = Describe(m.RunHaul);
            string head = string.Format(Localization.Get(m.Injured ? "scMineHurtFrom" : "scMineBackFrom"), m.Name, m.RunDestName,
                                        haul.Length > 0 ? haul : Localization.Get("scMineNothing"));
            Count(Key(m.RunJob) + "-at-gate");
            if (haul.Length == 0)
            {
                if (player != null) ScCommunity.Tell(player, head, m.Injured ? "ui_denied" : "");
                ScLog.Out(m.Name + " is back from " + m.RunDestName + " with nothing to put away" + (m.Injured ? " (injured)" : ""));
                return;
            }
            string put, kept, left;
            Landed r = Deliver(m, out put, out kept, out left);
            string tail = Tail(r, kept, left);
            if (player != null) ScCommunity.Tell(player, tail.Length > 0 ? head + " " + tail : head, m.Injured ? "ui_denied" : "");
            ScLog.Out(m.Name + " is back from " + m.RunDestName + " with " + haul + (m.Injured ? " (injured)" : "") + " - " + LogResult(r, put, kept, left));
        }

        private static string Tail(Landed r, string kept, string left)
        {
            switch (r)
            {
                case Landed.All: return Localization.Get("scMineTailCrates");
                case Landed.Some:
                case Landed.NoRoom: return Rest(kept, left);
                case Landed.Wait: return Localization.Get("scMineTailWait");
                case Landed.WaitWindow: return Localization.Get("scMineTailWaitWindow");
                default: return "";
            }
        }

        /// <summary>What did not go into the crates: kept in their inventory, and what fits nowhere and waits.</summary>
        private static string Rest(string kept, string left)
        {
            string a = kept.Length > 0 ? string.Format(Localization.Get("scMineTailSome"), kept) : "";
            string b = left.Length > 0 ? string.Format(Localization.Get("scMineTailWaits"), left) : "";
            return a.Length > 0 && b.Length > 0 ? a + " " + b : a + b;
        }

        private static string LogResult(Landed r, string put, string kept, string left)
        {
            if (r == Landed.WaitWindow) return "their inventory window is open: it waits on the record until it is closed";
            return r == Landed.Wait ? "the home crates are not loaded: it waits on the record until they are"
                 : "into the home crates: " + (put.Length > 0 ? put : "nothing") + (kept.Length > 0 ? "; into their inventory window: " + kept : "")
                   + (left.Length > 0 ? "; no room anywhere for " + left + " (it waits, tried again in 2 min)" : "");
        }

        /// <summary>
        /// Puts the record's haul away: onto the home crates' stacks of the same item, then into empty slots (round 29a's home-job
        /// deposit: ScStorage.TopUpInZone, DepositInZone), the rest into their inventory window (SCore's, by entity id); what fits
        /// nowhere stays on the record. Waits while the home zone's chunks are not all loaded or their window is open.
        /// </summary>
        private static Landed Deliver(Member m, out string put, out string kept, out string left)
        {
            put = kept = left = "";
            List<KeyValuePair<string, int>> haul = Decode(m.RunHaul);
            if (haul.Count == 0) { m.RunHaul = ""; return Landed.Nothing; }
            if (!HomeLoaded()) return Landed.Wait;
            if (WindowOpen(m.Id)) return Landed.WaitWindow;
            var putL = new List<KeyValuePair<string, int>>();
            var keptL = new List<KeyValuePair<string, int>>();
            var leftL = new List<KeyValuePair<string, int>>();
            foreach (var kv in haul)
            {
                ItemClass ic = ItemClass.GetItemClass(kv.Key, true);
                if (ic == null) { ScLog.Warn("mining: " + m.Name + "'s haul names " + kv.Key + ", which no longer exists - " + kv.Value + " dropped"); continue; }
                int rem = kv.Value;
                try
                {
                    rem -= Math.Max(0, ScStorage.TopUpInZone(new ItemStack(new ItemValue(ic.Id), rem)));
                    int max = Math.Max(1, ic.Stacknumber.Value);
                    while (rem > 0)
                    {
                        int k = Math.Min(rem, max);
                        Vector3i box;
                        if (!ScStorage.DepositInZone(new ItemStack(new ItemValue(ic.Id), k), out box)) break;
                        rem -= k;
                    }
                }
                catch (Exception e) { ScLog.Once("mining-deposit", e); }
                if (kv.Value - rem > 0) putL.Add(new KeyValuePair<string, int>(kv.Key, kv.Value - rem));
                if (rem <= 0) continue;
                var st = new ItemStack(new ItemValue(ic.Id), rem);
                bool fit = false;
                try { fit = HarvestManager.AddItem(m.Id, st); }
                catch (Exception e) { ScLog.Once("mining-window", e); st.count = rem; }
                int inWindow = fit ? rem : rem - st.count;
                if (inWindow > 0) keptL.Add(new KeyValuePair<string, int>(kv.Key, inWindow));
                if (!fit && st.count > 0) leftL.Add(new KeyValuePair<string, int>(kv.Key, st.count));
            }
            m.RunHaul = Encode(leftL);
            if (leftL.Count > 0) nextTry[m.Id] = Time.unscaledTime + 120f;
            else nextTry.Remove(m.Id);
            if (putL.Count == 0 && keptL.Count == 0 && leftL.Count == 0) { ScRegistry.SaveAtomic(); return Landed.Nothing; }   // only items that no longer exist
            put = Describe(putL);
            kept = Describe(keptL);
            left = Describe(leftL);
            Count("items-to-crates", Total(putL));
            Count("items-to-window", Total(keptL));
            if (leftL.Count > 0) Count("haul-no-room-anywhere");
            ScRegistry.SaveAtomic();
            if (putL.Count == 0) return Landed.NoRoom;
            return keptL.Count > 0 || leftL.Count > 0 ? Landed.Some : Landed.All;
        }

        /// <summary>
        /// Every chunk of the home zone is loaded (the crates DepositInZone walks are all there to see) - or the player stands
        /// inside the zone. Round 33 review L2: a zone wider than the player's loaded area (a LandClaimUpgrade claim near its
        /// largest, an L-shaped zone's bounding box) never has every chunk loaded, so the haul waited forever and every later
        /// Mine or Dig clay was refused; with the player at home, the loaded crates are the ones there are.
        /// </summary>
        private static bool HomeLoaded()
        {
            World w = ScCommunity.World;
            if (w == null || !ScZone.HasHome) return false;
            int xMin, xMax, zMin, zMax;
            ScZone.Bounds(out xMin, out xMax, out zMin, out zMax);
            bool all = true;
            for (int cx = World.toChunkXZ(xMin); all && cx <= World.toChunkXZ(xMax); cx++)
                for (int cz = World.toChunkXZ(zMin); cz <= World.toChunkXZ(zMax); cz++)
                    if (w.GetChunkSync(cx, cz) == null) { all = false; break; }
            if (all) return true;
            EntityPlayerLocal p = ScCommunity.LocalPlayer;
            return p != null && !p.IsDead() && ScZone.Contains(p.position);
        }

        private static bool WindowOpen(int id)
        {
            try
            {
                if (HarvestManager.Has(id) && HarvestManager.GetOrCreate(id).IsUserAccessing()) return true;
                return ScAmmo.IsRemoteOpen(id) || ScAmmo.XnpcTradeOpen(id);
            }
            catch (Exception) { return false; }
        }

        /// <summary>A haul on the record can be put away: a resident's (a hidden run, a run that ended at the gate), or a runner at the gate.</summary>
        private static bool Deliverable(Member m)
        {
            return m.Status == MemberStatus.Resident || (m.Status == MemberStatus.OnRun && m.RunLeg == RunLeg.Back);
        }

        /// <summary>ScTicker, once a second (every 2 s here): a haul that waited for the home's chunks, or for room, is put away and said.</summary>
        public static void Tick(World w, EntityPlayerLocal player)
        {
            float t = Time.unscaledTime;
            if (t < nextTick) return;
            nextTick = t + 2f;
            try
            {
                if (w == null || !ScRegistry.Loaded || !ScCommunity.IsHost) return;
                scratch.Clear();
                foreach (Member m in ScRegistry.Members.Values) if (!string.IsNullOrEmpty(m.RunHaul) && Deliverable(m)) scratch.Add(m);
                foreach (Member m in scratch)
                {
                    float at;
                    if (nextTry.TryGetValue(m.Id, out at) && t < at) continue;
                    string before = Describe(m.RunHaul), put, kept, left;
                    Landed r = Deliver(m, out put, out kept, out left);
                    if (r == Landed.Wait || r == Landed.WaitWindow || r == Landed.Nothing) continue;
                    if (r == Landed.NoRoom && kept.Length == 0) continue;   // still no room anywhere: said when it first waited
                    string rest = left.Length > 0 ? " " + string.Format(Localization.Get("scMineTailWaits"), left) : "";
                    string text = r == Landed.NoRoom ? string.Format(Localization.Get("scMineWindowOnly"), m.Name, kept) + rest
                                                     : string.Format(Localization.Get("scMineDelivered"), m.Name, put) + (r == Landed.Some ? " " + Rest(kept, left) : "");
                    if (player != null) ScCommunity.Tell(player, text, "");
                    ScLog.Out(m.Name + " puts away the haul that waited (" + before + ") - " + LogResult(r, put, kept, left));
                }
                scratch.Clear();
            }
            catch (Exception e)
            {
                ScLog.Once("mining-tick", e);
            }
        }

        // ------------------------------------------------------------------ the registry ----

        /// <summary>runjob=mine:tier:tool:biome:sub (written while OnRun on a mining run).</summary>
        public static string EncodeJob(Member m)
        {
            return Key(m.RunJob) + ":" + m.RunToolTier.ToString(CultureInfo.InvariantCulture) + ":" + Safe(m.RunTool) + ":" + Safe(m.RunBiome) + ":" + m.RunSub.ToString(CultureInfo.InvariantCulture);
        }

        public static void DecodeJob(Member m, string val)
        {
            if (m == null || string.IsNullOrEmpty(val)) return;
            string[] f = val.Split(':');
            RunJob j;
            if (f.Length < 1 || !TryJob(f[0], out j)) return;
            m.RunJob = j;
            int n;
            if (f.Length > 1 && int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) m.RunToolTier = Math.Max(0, Math.Min(20, n));
            m.RunTool = f.Length > 2 ? f[2] : "";
            m.RunBiome = f.Length > 3 ? f[3] : "";
            m.RunSub = f.Length > 4 && int.TryParse(f[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : -1;
        }

        private static string Safe(string s) { return (s ?? "").Replace(":", "").Replace(",", "").Replace("=", "").Replace(";", "").Trim(); }

        // ------------------------------------------------------------------ console ----

        /// <summary>`sc runners`: the mining part of a runner's line.</summary>
        public static string DescribeRun(Member m)
        {
            if (!IsDig(m)) return "";
            return ", " + Key(m.RunJob) + " with " + m.RunTool + " (T" + m.RunToolTier + ", x" + TierMult(m.RunToolTier).ToString("0.##", CultureInfo.InvariantCulture) + ") in "
                   + (m.RunBiome.Length > 0 ? m.RunBiome : "?") + " sub-biome " + m.RunSub
                   + (string.IsNullOrEmpty(m.RunHaul) ? "" : ", haul [" + m.RunHaul + "]");
        }

        /// <summary>`sc run &lt;member&gt; mine|clay`: what a whole run would bring them now (the console's line after a send).</summary>
        public static string Expected(Member m)
        {
            return IsDig(m) ? Encode(Haul(m, 1f)) : "";
        }
    }
}
