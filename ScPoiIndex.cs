using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>One real POI of the world, as ScPoiIndex holds it: everything the enclave seeding filters on, read once.</summary>
    public sealed class PoiEntry
    {
        public PrefabInstance Pi;
        /// <summary>boundingBoxPosition (the min corner: SC's POI key) and the rotated footprint size.</summary>
        public Vector3i Key;
        public Vector3i Size;
        /// <summary>The footprint's centre (XZ).</summary>
        public float X, Z;
        public string Prefab = "";
        public string Lower = "";
        public string Display = "";
        public int Tier;
        /// <summary>Sleeper volumes the prefab defines (pi.sleeperVolumes only fills once its chunks are generated).</summary>
        public int Sleepers;
        /// <summary>The vanilla "infested" quest tag (the infestation milestone's candidates).</summary>
        public bool Infested;
        /// <summary>The district: the first of ScPoiIndex.Kinds its Tags carry ("other" for none), and every one it carries.</summary>
        public string Kind = "other";
        public string[] Kinds = new string[0];
        /// <summary>The ScPoiTypes row id ("" when no row matches): an enclave's archetype later.</summary>
        public string Row = "";
        public string Biome = "";

        public int Side { get { return Math.Max(Size.x, Size.z); } }
    }

    /// <summary>
    /// The world's real POIs (ScSecurity.IsRealPoi over DynamicPrefabDecorator.GetWorldPrefabs), built once per world on the
    /// host at GameStartDone: tier, sleepers, district, outpost row and biome read once, a 256 m grid for "near" queries
    /// (each POI in every cell its footprint touches, so no 200 m-radius blind spot as in vanilla's sweep), and an (x, z)
    /// lookup for finding a saved POI again (the key's y moves when a POI pack changes its YOffset). The decorator only
    /// holds the full list on the server. Read-only once built; main thread only.
    /// </summary>
    public static class ScPoiIndex
    {
        public const int Cell = 256;

        /// <summary>The districts, in the order the seeding reads them (EnclaveKindWeights names them).</summary>
        public static readonly string[] KindNames = { "rural", "countryresidential", "countrytown", "wilderness", "oldwest", "residential", "commercial", "industrial", "downtown" };
        private static readonly FastTags<TagGroup.Poi>[] kindTags = Array.ConvertAll(KindNames, k => FastTags<TagGroup.Poi>.Parse(k));
        private static readonly FastTags<TagGroup.Global> infestedTag = FastTags<TagGroup.Global>.Parse("infested");

        private static readonly List<PoiEntry> entries = new List<PoiEntry>();
        private static readonly Dictionary<long, List<int>> grid = new Dictionary<long, List<int>>();
        private static readonly Dictionary<long, List<int>> byXZ = new Dictionary<long, List<int>>();
        private static bool built;

        public static bool Built { get { return built; } }
        public static int Count { get { return entries.Count; } }
        public static List<PoiEntry> All { get { return entries; } }

        public static void Reset()
        {
            entries.Clear();
            grid.Clear();
            byXZ.Clear();
            built = false;
        }

        private static long CellKey(int cx, int cz) { return ((long)cx << 32) ^ (uint)cz; }

        /// <summary>Builds the index once per world (host). False when the world or its POI list is not there.</summary>
        public static bool EnsureBuilt()
        {
            if (built) return true;
            try
            {
                World w = ScCommunity.World;
                DynamicPrefabDecorator dec = GameManager.Instance != null ? GameManager.Instance.GetDynamicPrefabDecorator() : null;
                if (w == null || dec == null || !ScCommunity.IsHost) return false;
                var sw = Stopwatch.StartNew();
                var list = new List<PrefabInstance>();
                dec.GetWorldPrefabs(list);
                Reset();
                foreach (PrefabInstance pi in list)
                {
                    if (!ScSecurity.IsRealPoi(pi)) continue;
                    PoiEntry e = Read(w, pi);
                    if (e == null) continue;
                    int i = entries.Count;
                    entries.Add(e);
                    int x0 = Mathf.FloorToInt((float)e.Key.x / Cell), x1 = Mathf.FloorToInt((float)(e.Key.x + Math.Max(1, e.Size.x) - 1) / Cell);
                    int z0 = Mathf.FloorToInt((float)e.Key.z / Cell), z1 = Mathf.FloorToInt((float)(e.Key.z + Math.Max(1, e.Size.z) - 1) / Cell);
                    for (int cx = x0; cx <= x1; cx++)
                        for (int cz = z0; cz <= z1; cz++)
                            Add(grid, CellKey(cx, cz), i);
                    Add(byXZ, CellKey(e.Key.x, e.Key.z), i);
                }
                built = entries.Count > 0;
                ScLog.Out("POI index: " + entries.Count + " real POI(s) of " + list.Count + " world prefab(s) in " + grid.Count + " cell(s) ("
                          + sw.ElapsedMilliseconds + " ms)");
                return built;
            }
            catch (Exception e)
            {
                ScLog.Once("poi-index-build", e);
                Reset();
                return false;
            }
        }

        private static void Add(Dictionary<long, List<int>> d, long key, int i)
        {
            List<int> l;
            if (!d.TryGetValue(key, out l)) d[key] = l = new List<int>(2);
            l.Add(i);
        }

        private static PoiEntry Read(World w, PrefabInstance pi)
        {
            Prefab p = pi.prefab;
            if (p == null) return null;
            var e = new PoiEntry
            {
                Pi = pi,
                Key = pi.boundingBoxPosition,
                Size = pi.boundingBoxSize,
                X = pi.boundingBoxPosition.x + pi.boundingBoxSize.x * 0.5f,
                Z = pi.boundingBoxPosition.z + pi.boundingBoxSize.z * 0.5f,
                Prefab = p.PrefabName ?? "",
                Display = ScSecurity.DisplayName(pi),
                Tier = p.DifficultyTier,
                Sleepers = p.SleeperVolumeList != null ? p.SleeperVolumeList.Count : 0
            };
            e.Lower = e.Prefab.ToLowerInvariant();
            try { e.Infested = p.GetQuestTag(infestedTag); } catch (Exception) { }
            var kinds = new List<string>();
            for (int k = 0; k < KindNames.Length; k++) if (p.Tags.Test_AnySet(kindTags[k])) kinds.Add(KindNames[k]);
            e.Kinds = kinds.ToArray();
            if (kinds.Count > 0) e.Kind = kinds[0];
            PoiClass c = ScPoiTypes.Classify(pi);
            e.Row = c != null && c.Kind == "row" && c.Row != null ? c.Row.Id : "";
            BiomeDefinition b = w.GetBiomeInWorld((int)e.X, (int)e.Z);
            e.Biome = b != null ? b.m_sBiomeName ?? "" : "";
            return e;
        }

        /// <summary>The POI whose key is at (x, z) with this prefab: a saved POI found again even when its key's y moved.</summary>
        public static PoiEntry Find(int x, int z, string prefab)
        {
            List<int> l;
            if (!byXZ.TryGetValue(CellKey(x, z), out l)) return null;
            foreach (int i in l)
                if (string.Equals(entries[i].Prefab, prefab, StringComparison.OrdinalIgnoreCase)) return entries[i];
            return null;
        }

        /// <summary>Every POI whose centre is within r of (x, z), nearest first.</summary>
        public static void Near(float x, float z, float r, List<PoiEntry> into)
        {
            into.Clear();
            if (entries.Count == 0) return;
            var seen = new HashSet<int>();
            int x0 = Mathf.FloorToInt((x - r) / Cell), x1 = Mathf.FloorToInt((x + r) / Cell);
            int z0 = Mathf.FloorToInt((z - r) / Cell), z1 = Mathf.FloorToInt((z + r) / Cell);
            float r2 = r * r;
            for (int cx = x0; cx <= x1; cx++)
            {
                for (int cz = z0; cz <= z1; cz++)
                {
                    List<int> l;
                    if (!grid.TryGetValue(CellKey(cx, cz), out l)) continue;
                    foreach (int i in l)
                    {
                        if (!seen.Add(i)) continue;
                        PoiEntry e = entries[i];
                        float dx = e.X - x, dz = e.Z - z;
                        if (dx * dx + dz * dz <= r2) into.Add(e);
                    }
                }
            }
            into.Sort((a, b) =>
            {
                int k = ((a.X - x) * (a.X - x) + (a.Z - z) * (a.Z - z)).CompareTo((b.X - x) * (b.X - x) + (b.Z - z) * (b.Z - z));
                if (k != 0) return k;
                k = a.Key.x.CompareTo(b.Key.x);      // a tie keeps one order whatever the load order was
                return k != 0 ? k : a.Key.z.CompareTo(b.Key.z);
            });
        }
    }
}
