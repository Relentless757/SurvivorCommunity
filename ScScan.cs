using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Counts what the base contains: beds, facilities and living crops inside the whole home zone, the
    /// water its wells and dew collectors give a day (ScFacilities, round wl), and notes radios. Every one of
    /// those is a model block, and every loaded chunk already keeps a list of its
    /// model blocks (Chunk.blockEntityStubs, filled on the main thread by BlockShapeModelEntity
    /// OnBlockLoaded / OnBlockAdded, parents only, keyed by world position). Walking those lists instead of
    /// the block volume makes a scan cheap enough to run whenever the community window refreshes and every
    /// few seconds from the ticker, at any claim size and with no height window.
    ///
    /// Counts are kept PER CHUNK (Community.ScanChunks, persisted in the registry): a loaded chunk is
    /// recounted, a chunk that is not loaded keeps its last count, so leaving the base never zeroes the
    /// beds. A zone chunk that has never been counted is "missing"; while any are missing a total can rise
    /// but not fall (the first scans after an update, or after the claim grew, only see part of it).
    ///
    /// Classification is by block TYPE, built once per world from Block.list: beds = any BlockSleepingBag
    /// (bedrolls, king beds, bunk beds, modded beds of that class) plus the BedBlocks name prefixes, minus
    /// BedBlocksExclude; facilities = FacilityBlocks names; crops = planted* minus dead/helper/deco/tree.
    /// Crops are counted from the same lists, so only model-shaped plants count (the flat flower crops -
    /// chrysanthemum, goldenrod, snowberry - are not food and are not model blocks).
    /// </summary>
    public static class ScScan
    {
        private const byte KNone = 0, KBed = 1, KFacility = 2, KCrop = 3, KRadio = 4, KDefense = 5;

        private static Block[] tableFor;
        private static byte[] kinds;
        private static float lastRun = -1000f;
        private static readonly HashSet<long> pending = new HashSet<long>();
        private static readonly HashSet<long> visited = new HashSet<long>();
        private static readonly List<int[]> rects = new List<int[]>();
        /// <summary>
        /// When each loaded zone chunk (this exact Chunk instance) was first seen. A chunk is only counted once it
        /// has been in the cache for a second: the generator thread adds a chunk to the cache (GetChunkSync finds
        /// it) before World.OnChunkAdded queues it for OnLoad, and until OnLoad runs its model list is empty.
        /// </summary>
        private static Dictionary<long, KeyValuePair<Chunk, float>> seen = new Dictionary<long, KeyValuePair<Chunk, float>>();
        private static Dictionary<long, KeyValuePair<Chunk, float>> seenNext = new Dictionary<long, KeyValuePair<Chunk, float>>();
        private const float SettleSeconds = 1f;

        /// <summary>Last run: zone chunks in range, loaded now, never counted, milliseconds.</summary>
        public static int LastChunks, LastLoaded, LastMissing;
        public static double LastMs;
        public static int BedTypes, FacilityTypes, CropTypes;

        /// <summary>Per-world reset (ModEvents.GameStarting): the block table belongs to the old Block.list.</summary>
        public static void Reset()
        {
            tableFor = null;
            kinds = null;
            lastRun = -1000f;
            seen.Clear();
            seenNext.Clear();
            LastChunks = LastLoaded = LastMissing = 0;
            LastMs = 0;
            BedTypes = FacilityTypes = CropTypes = 0;
        }

        /// <summary>Runs unless the last run (by anyone) was under 'seconds' ago: the ticker asks every 15 s, the open window every second.</summary>
        public static bool RunIfDue(float seconds)
        {
            if (Time.unscaledTime - lastRun < seconds) return false;
            return Run(false);
        }

        /// <summary>Counts now ('force' also logs an unchanged result). Safe away from the base: only loaded chunks are recounted.</summary>
        public static bool Run(bool force)
        {
            lastRun = Time.unscaledTime;
            try
            {
                World w = ScCommunity.World;
                Community c = ScRegistry.Community;
                if (w == null || !ScRegistry.Loaded || !c.HasHome) return false;
                if (!EnsureTable()) return false;
                var sw = Stopwatch.StartNew();

                // the counts belong to one home; a new home starts over
                if (!c.HasScanHome || c.ScanHome != c.Home)
                {
                    c.ScanChunks.Clear();
                    c.ScanDefences.Clear();
                    c.HomeDefences = 0;
                    c.ScanWater.Clear();
                    c.HomeWater = 0;
                    c.ScanHome = c.Home;
                    c.HasScanHome = true;
                }

                // the zone as rectangles (an upgraded claim can be an L or a plus: only the chunks it reaches count,
                // or a bounding-box corner that never loads would read as "never counted" for good)
                ScZone.Rects(rects);
                if (rects.Count == 0) return false;
                int r = ScConfig.ScanRadius;
                Vector3i home = c.Home;
                if (r > 0)
                {
                    for (int i = rects.Count - 1; i >= 0; i--)
                    {
                        int[] q = rects[i];
                        q[0] = Math.Max(q[0], home.x - r); q[1] = Math.Min(q[1], home.x + r);
                        q[2] = Math.Max(q[2], home.z - r); q[3] = Math.Min(q[3], home.z + r);
                        if (q[0] > q[1] || q[2] > q[3]) rects.RemoveAt(i);
                    }
                    if (rects.Count == 0) return false;
                }
                int xMin = int.MaxValue, xMax = int.MinValue, zMin = int.MaxValue, zMax = int.MinValue;
                for (int i = 0; i < rects.Count; i++)
                {
                    int[] q = rects[i];
                    xMin = Math.Min(xMin, q[0]); xMax = Math.Max(xMax, q[1]);
                    zMin = Math.Min(zMin, q[2]); zMax = Math.Max(zMax, q[3]);
                }
                int cxMin = World.toChunkXZ(xMin), cxMax = World.toChunkXZ(xMax);
                int czMin = World.toChunkXZ(zMin), czMax = World.toChunkXZ(zMax);
                float now0 = Time.unscaledTime;
                seenNext.Clear();
                visited.Clear();

                pending.Clear();
                List<long> fresh = w.newlyLoadedChunksThisUpdate;
                if (fresh != null) lock (fresh) { for (int i = 0; i < fresh.Count; i++) pending.Add(fresh[i]); }

                int beds = 0, facilities = 0, crops = 0, defences = 0, water = 0, chunks = 0, loaded = 0, missing = 0;
                for (int cx = cxMin; cx <= cxMax; cx++)
                {
                    for (int cz = czMin; cz <= czMax; cz++)
                    {
                        if (!Touches(cx, cz)) continue;
                        chunks++;
                        long key = WorldChunkCache.MakeChunkKey(cx, cz);
                        visited.Add(key);
                        int[] known;
                        int defs = 0, wet = 0;
                        c.ScanChunks.TryGetValue(key, out known);
                        Chunk ch = w.GetChunkSync(cx, cz) as Chunk;
                        int[] now = null;
                        // a chunk that joined the cache but has not run OnLoad yet (World.updateChunkAddedRemovedCallbacks)
                        // has no model list: it keeps what it had until it is ready - pending OnLoad, still decorating,
                        // unloading, or first seen under a second ago
                        if (ch != null && !ch.NeedsDecoration && !ch.InProgressUnloading && !pending.Contains(key))
                        {
                            KeyValuePair<Chunk, float> was;
                            float first = (seen.TryGetValue(key, out was) && ReferenceEquals(was.Key, ch)) ? was.Value : now0;
                            seenNext[key] = new KeyValuePair<Chunk, float>(ch, first);
                            if (now0 - first >= SettleSeconds) now = Count(ch, cx, cz, home, r, out defs, out wet);
                        }
                        if (now != null)
                        {
                            loaded++;
                            c.ScanChunks[key] = now;
                            known = now;
                            if (defs > 0) c.ScanDefences[key] = defs; else c.ScanDefences.Remove(key);
                            if (wet > 0) c.ScanWater[key] = wet; else c.ScanWater.Remove(key);
                        }
                        if (known == null) { missing++; continue; }
                        beds += known[2];
                        facilities += known[3];
                        crops += known[4];
                        int kd;
                        if (c.ScanDefences.TryGetValue(key, out kd)) defences += kd;
                        int kw;
                        if (c.ScanWater.TryGetValue(key, out kw)) water += kw;
                    }
                }
                var swap = seen; seen = seenNext; seenNext = swap;
                // chunks that left the zone (it shrank or changed shape, or ScanRadius changed)
                if (c.ScanChunks.Count > chunks)
                {
                    var drop = new List<long>();
                    foreach (long k in c.ScanChunks.Keys) if (!visited.Contains(k)) drop.Add(k);
                    for (int i = 0; i < drop.Count; i++) c.ScanChunks.Remove(drop[i]);
                }
                if (c.ScanDefences.Count > 0)
                {
                    var dropD = new List<long>();
                    foreach (long k in c.ScanDefences.Keys) if (!c.ScanChunks.ContainsKey(k)) dropD.Add(k);
                    for (int i = 0; i < dropD.Count; i++) c.ScanDefences.Remove(dropD[i]);
                }
                if (c.ScanWater.Count > 0)
                {
                    var dropW = new List<long>();
                    foreach (long k in c.ScanWater.Keys) if (!c.ScanChunks.ContainsKey(k)) dropW.Add(k);
                    for (int i = 0; i < dropW.Count; i++) c.ScanWater.Remove(dropW[i]);
                }

                // part of the zone has never been seen: a total may rise, not fall
                if (missing > 0)
                {
                    beds = Math.Max(beds, c.Beds);
                    facilities = Math.Max(facilities, c.Facilities);
                    crops = Math.Max(crops, c.Crops);
                    defences = Math.Max(defences, c.HomeDefences);
                    water = Math.Max(water, c.HomeWater);
                }
                bool changed = beds != c.Beds || facilities != c.Facilities || crops != c.Crops || defences != c.HomeDefences || water != c.HomeWater;
                c.Beds = beds;
                c.Facilities = facilities;
                c.Crops = crops;
                c.HomeDefences = defences;
                c.HomeWater = water;
                sw.Stop();
                LastChunks = chunks; LastLoaded = loaded; LastMissing = missing; LastMs = sw.Elapsed.TotalMilliseconds;
                if (changed || force)
                    ScLog.Verbose("scan: beds " + beds + ", facilities " + facilities + ", crops " + crops + ", traps " + defences + ", water " + water + "/day (" + chunks + " zone chunks, "
                                  + loaded + " loaded, " + missing + " never counted, " + LastMs.ToString("0.00") + " ms)");
                if (changed) ScRegistry.SaveAtomic();
                return true;
            }
            catch (Exception e)
            {
                ScLog.Once("scan", e);
                return false;
            }
        }

        /// <summary>Does any zone rectangle reach into this 16x16 chunk column?</summary>
        private static bool Touches(int cx, int cz)
        {
            int x0 = cx << 4, x1 = x0 + 15, z0 = cz << 4, z1 = z0 + 15;
            for (int i = 0; i < rects.Count; i++)
            {
                int[] q = rects[i];
                if (q[0] <= x1 && q[1] >= x0 && q[2] <= z1 && q[3] >= z0) return true;
            }
            return false;
        }

        /// <summary>
        /// One loaded chunk: {cx, cz, beds, facilities, crops} from its model-block list, zone positions only; 'defences' the
        /// traps and turrets (OutpostDefenseBlocks) among them - the defence rating's count (D4) - and 'water' what its
        /// wells and dew collectors give a day (round wl), both kept apart from the five-field scanmap entry that older
        /// builds read. A dew collector's level that no longer has its collector here is forgotten.
        /// </summary>
        private static int[] Count(Chunk ch, int cx, int cz, Vector3i home, int r, out int defences, out int water)
        {
            defences = 0;
            water = 0;
            var n = new int[] { cx, cz, 0, 0, 0 };
            var stubs = ch.blockEntityStubs;
            if (stubs == null) return n;
            List<BlockEntityData> list = stubs.list;
            byte[] k = kinds;
            List<Vector3i> dews = null;
            for (int i = 0; i < list.Count; i++)
            {
                BlockEntityData d = list[i];
                if (d == null) continue;
                BlockValue bv = d.blockValue;
                if (bv.ischild) continue;
                int t = bv.type;
                if (t <= 0 || t >= k.Length) continue;
                byte kind = k[t];
                bool fac = ScFacilities.IsFacility(t);
                if (kind == KNone && !fac) continue;
                Vector3i p = d.pos;
                if (fac && ScFacilities.IsDew(t)) { if (dews == null) dews = new List<Vector3i>(); dews.Add(p); }
                if (r > 0 && (Math.Abs(p.x - home.x) > r || Math.Abs(p.z - home.z) > r)) continue;
                if (!ScZone.Contains(p)) continue;
                if (fac) water += ScFacilities.WaterAt(p, bv);
                switch (kind)
                {
                    case KBed: n[2]++; break;
                    case KFacility: n[3]++; break;
                    case KCrop: n[4]++; break;
                    case KRadio: ScRadio.NoteRadio(p); break;
                    case KDefense: defences++; break;
                }
            }
            // a dew collector picked up or broken: its level goes with it (this chunk's entries only - it is loaded and settled)
            Dictionary<Vector3i, int> levels = ScRegistry.Community.DewLevels;
            if (levels.Count > 0)
            {
                List<Vector3i> gone = null;
                foreach (Vector3i at in levels.Keys)
                {
                    if (World.toChunkXZ(at.x) != cx || World.toChunkXZ(at.z) != cz) continue;
                    if (dews != null && dews.Contains(at)) continue;
                    if (gone == null) gone = new List<Vector3i>();
                    gone.Add(at);
                }
                if (gone != null)
                {
                    for (int i = 0; i < gone.Count; i++) levels.Remove(gone[i]);
                    ScLog.Out("facilities: " + gone.Count + " upgraded dew collector(s) no longer there - level forgotten (" + string.Join(", ", gone.ConvertAll(v => v.ToString()).ToArray()) + ")");
                }
            }
            return n;
        }

        /// <summary>Classifies every block type once per world (Block.list is a new array on every load).</summary>
        private static bool EnsureTable()
        {
            Block[] list = Block.list;
            if (list == null) return false;
            if (kinds != null && ReferenceEquals(list, tableFor)) return true;
            ScConfig.EnsureParsed();
            var k = new byte[list.Length];
            int bedT = 0, facT = 0, cropT = 0;
            for (int t = 1; t < list.Length; t++)
            {
                Block b = list[t];
                if (b == null) continue;
                string name;
                try { name = b.GetBlockName(); } catch (Exception) { continue; }
                if (string.IsNullOrEmpty(name)) continue;
                if (string.Equals(name, ScRadio.BlockName, StringComparison.OrdinalIgnoreCase)) k[t] = KRadio;
                else if (IsCrop(name)) { k[t] = KCrop; cropT++; }
                else if (IsBed(b, name)) { k[t] = KBed; bedT++; }
                else if (IsFacility(name)) { k[t] = KFacility; facT++; }
                else if (IsDefense(name)) k[t] = KDefense;       // outposts' investment and the home's defence rating (D4)
            }
            kinds = k;
            tableFor = list;
            BedTypes = bedT; FacilityTypes = facT; CropTypes = cropT;
            ScLog.Out("scan: " + bedT + " bed, " + facT + " facility and " + cropT + " crop block types recognised");
            return true;
        }

        /// <summary>
        /// A bed: any sleeping-bag class block (what vanilla uses for bedrolls, king beds and bunk beds, and
        /// what bed mods copy) when BedsBySleepingBag is on, or a BedBlocks name prefix; never a placement
        /// helper (it never exists in the world) nor a BedBlocksExclude prefix (a bare bunk frame, a rolled-up
        /// mattress).
        /// </summary>
        public static bool IsBed(Block b, string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (name.IndexOf("Helper", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            for (int i = 0; i < ScConfig.BedBlocksExclude.Count; i++)
                if (name.StartsWith(ScConfig.BedBlocksExclude[i], StringComparison.OrdinalIgnoreCase)) return false;
            if (ScConfig.BedsBySleepingBag && b is BlockSleepingBag) return true;
            for (int i = 0; i < ScConfig.BedBlocks.Count; i++)
                if (name.StartsWith(ScConfig.BedBlocks[i], StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static bool IsFacility(string name)
        {
            for (int i = 0; i < ScConfig.FacilityBlocks.Count; i++)
            {
                if (string.Equals(name, ScConfig.FacilityBlocks[i], StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        public static bool IsDefense(string name)
        {
            for (int i = 0; i < ScConfig.OutpostDefenseBlocks.Count; i++)
                if (name.StartsWith(ScConfig.OutpostDefenseBlocks[i], StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static readonly HashSet<long> areaPending = new HashSet<long>();

        /// <summary>
        /// An outpost's investment: beds, facilities, crops and defenses whose model block stands inside the XZ box
        /// (inclusive) - into[0..3]. False (into untouched) while any chunk of the box is not loaded and settled.
        /// </summary>
        public static bool CountArea(int xMin, int xMax, int zMin, int zMax, int[] into)
        {
            World w = ScCommunity.World;
            if (w == null || !EnsureTable()) return false;
            areaPending.Clear();
            List<long> fresh = w.newlyLoadedChunksThisUpdate;
            if (fresh != null) lock (fresh) { for (int i = 0; i < fresh.Count; i++) areaPending.Add(fresh[i]); }
            int beds = 0, facilities = 0, crops = 0, defenses = 0;
            byte[] k = kinds;
            for (int cx = World.toChunkXZ(xMin); cx <= World.toChunkXZ(xMax); cx++)
            {
                for (int cz = World.toChunkXZ(zMin); cz <= World.toChunkXZ(zMax); cz++)
                {
                    Chunk ch = w.GetChunkSync(cx, cz) as Chunk;
                    if (ch == null || ch.NeedsDecoration || ch.InProgressUnloading || areaPending.Contains(WorldChunkCache.MakeChunkKey(cx, cz))) return false;
                    var stubs = ch.blockEntityStubs;
                    if (stubs == null) continue;
                    List<BlockEntityData> list = stubs.list;
                    for (int i = 0; i < list.Count; i++)
                    {
                        BlockEntityData d = list[i];
                        if (d == null) continue;
                        BlockValue bv = d.blockValue;
                        if (bv.ischild) continue;
                        int t = bv.type;
                        if (t <= 0 || t >= k.Length || k[t] == KNone) continue;
                        Vector3i p = d.pos;
                        if (p.x < xMin || p.x > xMax || p.z < zMin || p.z > zMax) continue;
                        switch (k[t])
                        {
                            case KBed: beds++; break;
                            case KFacility: facilities++; break;
                            case KCrop: crops++; break;
                            case KDefense: defenses++; break;
                        }
                    }
                }
            }
            into[0] = beds; into[1] = facilities; into[2] = crops; into[3] = defenses;
            return true;
        }

        public static bool IsCrop(string name)
        {
            if (!name.StartsWith("planted", StringComparison.OrdinalIgnoreCase)) return false;
            if (name.IndexOf("Dead", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (name.IndexOf("Helper", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (name.IndexOf("Deco", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (name.IndexOf("tree", StringComparison.OrdinalIgnoreCase) >= 0) return false;   // plantedtreeGrassSeed etc.
            return true;
        }

        // ---- the community window's bed line ----

        /// <summary>"Beds 12 for 2 survivors - 10 spare (+10 morale)", never null. E6: an ally's Overflow Barracks counts (and is named).</summary>
        public static string BedLine()
        {
            if (!ScRegistry.Loaded) return "";
            int m = ScRegistry.Members.Count, b = ScEncAllies.TotalBeds();
            int term = ScMorale.Compute().Beds;
            string s;
            if (m == 0) s = string.Format(Localization.Get("scBedsNoMembers"), b);
            else if (b < m) s = string.Format(Localization.Get("scBedsShort"), b, m, m - b, term);
            else if (b > m) s = string.Format(Localization.Get("scBedsSpare"), b, m, b - m, term);
            else s = string.Format(Localization.Get("scBedsExact"), b, m, term);
            int allies = ScEncAllies.ExtraBeds();
            if (allies > 0) s += " " + string.Format(Localization.Get("scMoraleBedsAllies"), allies);
            if (LastMissing > 0) s += " - " + Localization.Get("scBedsPartial");
            return s;
        }

        public static string BedColor()
        {
            if (!ScRegistry.Loaded) return "200,200,200,255";
            int m = ScRegistry.Members.Count;
            if (m == 0) return "200,200,200,255";
            return ScEncAllies.TotalBeds() < m ? "255,140,120,255" : "170,220,170,255";
        }
    }
}
