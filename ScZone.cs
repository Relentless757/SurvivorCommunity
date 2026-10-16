using System;
using System.Collections.Generic;
using System.Reflection;

namespace SurvivorCommunity
{
    /// <summary>
    /// The home zone: the land claim the community lives in. When LandClaimUpgrade is loaded its zone
    /// (LcbuRegistry.Covers / GetZone) is used through reflection; otherwise the vanilla square of half-extent
    /// (LandClaimSize-1)/2 around the home block. The home block is the registry's, else the owner's first
    /// land claim that still exists.
    /// </summary>
    public static class ScZone
    {
        private static bool lcbuProbed;
        private static MethodInfo lcbuCovers;      // static bool Covers(Vector3i claim, int vanillaRadius, Vector3i target)
        private static MethodInfo lcbuGetZone;     // static LcbuZone GetZone(Vector3i claim)
        private static PropertyInfo lcbuBoundsProp; // LcbuZone.Bounds -> LcbuRect (a public readonly FIELD in LandClaimUpgrade; the
        private static FieldInfo lcbuBoundsField;   //   property form is probed too in case that ever changes)
        private static FieldInfo rxMin, rxMax, rzMin, rzMax;
        private static FieldInfo lcbuPiecesField;   // LcbuZone.Pieces (LcbuRect[]): the rectangles an upgraded claim is made of

        public static bool LcbuAvailable
        {
            get { Probe(); return lcbuCovers != null; }
        }

        /// <summary>Per-world reset (the LCBU assembly does not change, but be safe about statics).</summary>
        public static void Reset()
        {
            // the probe is process-wide; the rectangle cache is per world
            rectCache.Clear();
            rectCacheAt = -100f;
        }

        // ---- distance and random spots (round hz: residents roam the zone, ScHomeHold keeps it loaded) ----

        private static readonly List<int[]> rectCache = new List<int[]>();
        private static float rectCacheAt = -100f;
        private const float RectCacheSeconds = 2f;

        /// <summary>The zone's rectangles (Rects), read again at most every RectCacheSeconds: AI scoring asks five times a second per resident.</summary>
        private static List<int[]> CachedRects()
        {
            float now = UnityEngine.Time.unscaledTime;
            if (rectCache.Count == 0 || now - rectCacheAt > RectCacheSeconds)
            {
                Rects(rectCache);
                rectCacheAt = now;
            }
            return rectCache;
        }

        /// <summary>Horizontal metres from the position to the nearest part of the home zone: 0 inside it, float.MaxValue with no home.</summary>
        public static float DistanceOutside(UnityEngine.Vector3 p)
        {
            if (!HasHome) return float.MaxValue;
            List<int[]> rs = CachedRects();
            float best = float.MaxValue;
            for (int i = 0; i < rs.Count; i++)
            {
                int[] r = rs[i];
                // inclusive block bounds: block xMax covers world x up to xMax + 1
                float dx = Math.Max(Math.Max(r[0] - p.x, p.x - (r[1] + 1)), 0f);
                float dz = Math.Max(Math.Max(r[2] - p.z, p.z - (r[3] + 1)), 0f);
                float d = (float)Math.Sqrt(dx * dx + dz * dz);
                if (d < best) best = d;
            }
            return best;
        }

        /// <summary>A random point in the zone (y 0), each rectangle weighted by its area. False with no home.</summary>
        public static bool RandomPoint(out UnityEngine.Vector3 p)
        {
            p = UnityEngine.Vector3.zero;
            if (!HasHome) return false;
            List<int[]> rs = CachedRects();
            if (rs.Count == 0) return false;
            long total = 0;
            for (int i = 0; i < rs.Count; i++) total += (long)(rs[i][1] - rs[i][0] + 1) * (rs[i][3] - rs[i][2] + 1);
            if (total <= 0) return false;
            long pick = (long)(UnityEngine.Random.value * total);
            int[] r = rs[rs.Count - 1];
            for (int i = 0; i < rs.Count; i++)
            {
                long a = (long)(rs[i][1] - rs[i][0] + 1) * (rs[i][3] - rs[i][2] + 1);
                if (pick < a) { r = rs[i]; break; }
                pick -= a;
            }
            p = new UnityEngine.Vector3(UnityEngine.Random.Range(r[0], r[1] + 1) + 0.5f, 0f, UnityEngine.Random.Range(r[2], r[3] + 1) + 0.5f);
            return true;
        }

        private static void Probe()
        {
            if (lcbuProbed) return;
            lcbuProbed = true;
            try
            {
                Type reg = null;
                foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (!a.GetName().Name.Equals("LandClaimUpgrade", StringComparison.OrdinalIgnoreCase)) continue;
                    reg = a.GetType("LandClaimUpgrade.LcbuRegistry", false);
                    break;
                }
                if (reg == null) { ScLog.Out("LandClaimUpgrade not loaded - home zone = vanilla land claim square"); return; }
                lcbuCovers = reg.GetMethod("Covers", BindingFlags.Public | BindingFlags.Static, null,
                    new[] { typeof(Vector3i), typeof(int), typeof(Vector3i) }, null);
                lcbuGetZone = reg.GetMethod("GetZone", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Vector3i) }, null);
                if (lcbuGetZone != null)
                {
                    Type zoneType = lcbuGetZone.ReturnType;
                    lcbuBoundsField = zoneType.GetField("Bounds", BindingFlags.Public | BindingFlags.Instance);
                    lcbuBoundsProp = lcbuBoundsField == null ? zoneType.GetProperty("Bounds", BindingFlags.Public | BindingFlags.Instance) : null;
                    lcbuPiecesField = zoneType.GetField("Pieces", BindingFlags.Public | BindingFlags.Instance);
                    Type rect = lcbuBoundsField != null ? lcbuBoundsField.FieldType : (lcbuBoundsProp != null ? lcbuBoundsProp.PropertyType : null);
                    if (rect != null)
                    {
                        rxMin = rect.GetField("xMin"); rxMax = rect.GetField("xMax");
                        rzMin = rect.GetField("zMin"); rzMax = rect.GetField("zMax");
                    }
                }
                if (lcbuCovers == null) ScLog.Warn("LandClaimUpgrade is loaded but LcbuRegistry.Covers was not found - falling back to the vanilla square");
                else ScLog.Out("LandClaimUpgrade detected - home zone follows the upgraded claim" + (rxMin == null ? " (bounds unreadable: POI favours may not exclude the base)" : ""));
            }
            catch (Exception e)
            {
                ScLog.Err("LandClaimUpgrade probe failed: " + e.Message);
                lcbuCovers = null;
            }
        }

        public static int VanillaRadius
        {
            get
            {
                try { return Math.Max(1, (GameStats.GetInt(EnumGameStats.LandClaimSize) - 1) / 2); }
                catch (Exception) { return 20; }
            }
        }

        /// <summary>Resolves (and, if needed, adopts) the home block. False when the owner has no land claim at all.</summary>
        public static bool EnsureHome(EntityPlayer owner)
        {
            try
            {
                Community c = ScRegistry.Community;
                var pp = GameManager.Instance != null ? GameManager.Instance.persistentPlayers : null;
                if (pp == null) return c.HasHome;
                if (c.HasHome && pp.m_lpBlockMap != null && pp.m_lpBlockMap.ContainsKey(c.Home)) return true;
                if (owner == null) return false;
                PersistentPlayerData ppd = pp.GetPlayerDataFromEntityID(owner.entityId);
                if (ppd == null || ppd.LPBlocks == null) return false;
                for (int i = 0; i < ppd.LPBlocks.Count; i++)
                {
                    Vector3i lp = ppd.LPBlocks[i];
                    if (pp.m_lpBlockMap != null && !pp.m_lpBlockMap.ContainsKey(lp)) continue;
                    if (ScOutposts.IsOutpostClaim(lp)) continue;       // an outpost is never the home
                    c.Home = lp;
                    c.HasHome = true;
                    c.Owner = owner.PlayerDisplayName ?? "";
                    ScLog.Out("community home set to the land claim at " + lp);
                    return true;
                }
                return false;
            }
            catch (Exception e)
            {
                ScLog.Once("zone-ensurehome", e);
                return ScRegistry.Community.HasHome;
            }
        }

        /// <summary>Makes a specific claim the home (the land claim radial "sethome").</summary>
        public static void SetHome(Vector3i claim, EntityPlayer owner)
        {
            Community c = ScRegistry.Community;
            c.Home = claim;
            c.HasHome = true;
            if (owner != null) c.Owner = owner.PlayerDisplayName ?? c.Owner;
        }

        public static bool HasHome { get { return ScRegistry.Community.HasHome; } }
        public static Vector3i Home { get { return ScRegistry.Community.Home; } }

        /// <summary>Is the block position inside the home zone?</summary>
        public static bool Contains(Vector3i p)
        {
            Community c = ScRegistry.Community;
            if (!c.HasHome) return false;
            try
            {
                Probe();
                if (lcbuCovers != null)
                {
                    object r = lcbuCovers.Invoke(null, new object[] { c.Home, VanillaRadius, p });
                    if (r is bool) return (bool)r;
                }
            }
            catch (Exception e)
            {
                ScLog.Once("zone-covers", e);
                lcbuCovers = null;
            }
            int vr = VanillaRadius;
            return Math.Abs(p.x - c.Home.x) <= vr && Math.Abs(p.z - c.Home.z) <= vr;
        }

        public static bool Contains(UnityEngine.Vector3 world)
        {
            return Contains(World.worldToBlockPos(world));
        }

        /// <summary>Inclusive XZ bounds of the zone.</summary>
        public static void Bounds(out int xMin, out int xMax, out int zMin, out int zMax)
        {
            Community c = ScRegistry.Community;
            int vr = VanillaRadius;
            xMin = c.Home.x - vr; xMax = c.Home.x + vr; zMin = c.Home.z - vr; zMax = c.Home.z + vr;
            if (!c.HasHome) return;
            try
            {
                Probe();
                if (lcbuGetZone != null && (lcbuBoundsField != null || lcbuBoundsProp != null) && rxMin != null)
                {
                    object zone = lcbuGetZone.Invoke(null, new object[] { c.Home });
                    if (zone != null)
                    {
                        object rect = lcbuBoundsField != null ? lcbuBoundsField.GetValue(zone) : lcbuBoundsProp.GetValue(zone, null);
                        if (rect != null)
                        {
                            xMin = (int)rxMin.GetValue(rect); xMax = (int)rxMax.GetValue(rect);
                            zMin = (int)rzMin.GetValue(rect); zMax = (int)rzMax.GetValue(rect);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                ScLog.Once("zone-bounds", e);
            }
        }

        /// <summary>
        /// The zone as inclusive rectangles {xMin, xMax, zMin, zMax}: an upgraded claim's pieces (a plus, an L or
        /// a T is several), else one rectangle, the Bounds. One reflection call per use - callers test many
        /// chunks against the result.
        /// </summary>
        public static void Rects(List<int[]> into)
        {
            into.Clear();
            if (!HasHome) return;
            try
            {
                Probe();
                if (lcbuGetZone != null && lcbuPiecesField != null && rxMin != null && rxMax != null && rzMin != null && rzMax != null)
                {
                    object zone = lcbuGetZone.Invoke(null, new object[] { Home });
                    Array pieces = zone != null ? lcbuPiecesField.GetValue(zone) as Array : null;
                    if (pieces != null && pieces.Length > 0)
                    {
                        foreach (object r in pieces)
                            into.Add(new[] { (int)rxMin.GetValue(r), (int)rxMax.GetValue(r), (int)rzMin.GetValue(r), (int)rzMax.GetValue(r) });
                        return;
                    }
                }
            }
            catch (Exception e)
            {
                ScLog.Once("zone-rects", e);
                into.Clear();
            }
            int xMin, xMax, zMin, zMax;
            Bounds(out xMin, out xMax, out zMin, out zMax);
            into.Add(new[] { xMin, xMax, zMin, zMax });
        }

        /// <summary>
        /// Any claim's rectangles {xMin, xMax, zMin, zMax}, ADDED to the list (Rects is the home's): its LandClaimUpgrade
        /// pieces when LCBU is loaded (a claim it stores nothing for comes back as the vanilla square), else the vanilla
        /// square round the block. The enclave late seed keeps its distance from every player's claims with it.
        /// </summary>
        public static void ClaimRects(Vector3i claim, List<int[]> into)
        {
            try
            {
                Probe();
                if (lcbuGetZone != null && lcbuPiecesField != null && rxMin != null && rxMax != null && rzMin != null && rzMax != null)
                {
                    object zone = lcbuGetZone.Invoke(null, new object[] { claim });
                    Array pieces = zone != null ? lcbuPiecesField.GetValue(zone) as Array : null;
                    if (pieces != null && pieces.Length > 0)
                    {
                        foreach (object r in pieces)
                            into.Add(new[] { (int)rxMin.GetValue(r), (int)rxMax.GetValue(r), (int)rzMin.GetValue(r), (int)rzMax.GetValue(r) });
                        return;
                    }
                }
            }
            catch (Exception e)
            {
                ScLog.Once("zone-claimrects", e);
            }
            int vr = VanillaRadius;
            into.Add(new[] { claim.x - vr, claim.x + vr, claim.z - vr, claim.z + vr });
        }

        /// <summary>Is the chunk holding this block position loaded?</summary>
        public static bool IsLoaded(Vector3i p)
        {
            World w = GameManager.Instance != null ? GameManager.Instance.World : null;
            return w != null && w.GetChunkSync(World.toChunkXZ(p.x), World.toChunkXZ(p.z)) != null;
        }

        public static bool HomeLoaded { get { return HasHome && IsLoaded(Home); } }
    }
}
