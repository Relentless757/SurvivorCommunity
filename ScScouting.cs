using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>A site the radio heard chatter about: marked on the map until it fades.</summary>
    public sealed class Scout
    {
        public Vector3i Poi;
        public Vector3i Size;
        public string Name = "";
        public Resource Res;
        public int ExpireDay;

        public Vector3 Centre { get { return new Vector3(Poi.x + Size.x * 0.5f, Poi.y, Poi.z + Size.z * 0.5f); } }
    }

    /// <summary>
    /// Radio barter (Outposts_Plan.md section 3): once a day, RadioBarterRate bundles of one resource for one of
    /// another, over the radio - a pressure valve for a world that lacks a family. Pure registry.
    /// </summary>
    public static class ScBarter
    {
        /// <summary>What the radio trades: food, meds, ammo, materials, fuel and (round wl) water - Resource 0..Kinds-1. Not luxuries.</summary>
        public const int Kinds = (int)Resource.Water + 1;

        public static bool CanTrade(Resource give, Resource get, out string why)
        {
            why = null;
            if (!ScCommunity.IsHost) { why = Localization.Get("scHostOnly"); return false; }
            if ((int)give >= Kinds || (int)get >= Kinds) { why = Localization.Get("scBarterNoLuxury"); return false; }
            if (!ScRadio.Usable()) { why = Localization.Get("scRadioNoPower"); return false; }
            if (give == get) { why = Localization.Get("scBarterSame"); return false; }
            Community c = ScRegistry.Community;
            int wait = c.RadioNextBarterDay - ScCommunity.Today;
            if (wait > 0) { why = string.Format(Localization.Get("scBarterWait"), wait); return false; }
            int rate = Math.Max(1, ScConfig.RadioBarterRate);
            if (c.Get(give) < rate) { why = string.Format(Localization.Get("scBarterShort"), rate, Localization.Get(ScNames.LocKey(give)), c.Get(give)); return false; }
            return true;
        }

        public static bool Trade(Resource give, Resource get, EntityPlayerLocal player, out string message)
        {
            if (!CanTrade(give, get, out message)) return false;
            Community c = ScRegistry.Community;
            int rate = Math.Max(1, ScConfig.RadioBarterRate);
            c.Add(give, -rate);
            c.Add(get, 1);
            c.RadioNextBarterDay = ScCommunity.Today + 1;
            ScOutposts.NoteLedger(string.Format(Localization.Get("scBarterLedger"), rate, Localization.Get(ScNames.LocKey(give)), Localization.Get(ScNames.LocKey(get))));
            ScRegistry.SaveAtomic();
            message = string.Format(Localization.Get("scBarterDone"), rate, Localization.Get(ScNames.LocKey(give)), Localization.Get(ScNames.LocKey(get)));
            ScLog.Out("radio barter: " + rate + " " + give + " for 1 " + get);
            return true;
        }
    }

    /// <summary>
    /// Outposts v1.1, radio scouting: one button per resource on the radio; the chatter marks the nearest
    /// RadioScoutCount eligible sites for it (visited or not) within RadioScoutRange of home, for RadioScoutDays.
    /// "Eligible" = what Establish would accept on type and distance: classified as producing that resource
    /// (the row's weight, primary rows first), at least OutpostMinDistance from home, not already an outpost.
    /// Once a day. Records persist in the registry (community "scouts="); markers are restored on load.
    /// </summary>
    public static class ScScouting
    {
        public const string NavPrefix = "sc_scout_";

        private static readonly Dictionary<Vector3i, Vector3> markers = new Dictionary<Vector3i, Vector3>();
        private static bool restored;
        private static float nextTick;

        public static void Reset()
        {
            markers.Clear();
            restored = false;
            nextTick = 0f;
        }

        public static void OnRegistryChanged()
        {
            markers.Clear();
            restored = false;
        }

        public static bool CanScout(out string why)
        {
            why = null;
            if (!ScCommunity.IsHost) { why = Localization.Get("scHostOnly"); return false; }
            if (!ScZone.HasHome) { why = Localization.Get("scNeedHome"); return false; }
            if (!ScRadio.Usable()) { why = Localization.Get("scRadioNoPower"); return false; }
            int wait = ScRegistry.Community.RadioNextScoutDay - ScCommunity.Today;
            if (wait > 0) { why = string.Format(Localization.Get("scScoutWait"), wait); return false; }
            return true;
        }

        /// <summary>The radio window's scout button for one resource.</summary>
        public static bool Scout(Resource r, EntityPlayerLocal player, out string message)
        {
            message = null;
            if ((int)r >= ScNames.ClassicCount) { message = Localization.Get("scScoutClassicOnly"); return false; }
            if (!CanScout(out message)) return false;
            try
            {
                ScConfig.EnsureParsed();
                DynamicPrefabDecorator dec = GameManager.Instance != null ? GameManager.Instance.GetDynamicPrefabDecorator() : null;
                if (dec == null) { message = Localization.Get("scInternal"); return false; }
                Vector3i home = ScZone.Home;
                int range = Math.Max(100, ScConfig.RadioScoutRange);
                var list = new List<PrefabInstance>();
                dec.GetPrefabsAtXZ(home.x - range, home.x + range, home.z - range, home.z + range, list);
                var found = new List<KeyValuePair<float, PrefabInstance>>();
                var seen = new HashSet<Vector3i>();
                foreach (PrefabInstance pi in list)
                {
                    if (!ScSecurity.IsRealPoi(pi) || !seen.Add(pi.boundingBoxPosition)) continue;
                    if (ScSecurity.OverlapsHome(pi) || ScOutposts.IsOutpostPoiKey(pi.boundingBoxPosition)) continue;
                    if (ScEnclaves.IsEnclavePoi(pi)) continue;      // an enclave's building can't be an outpost
                    PoiClass cls = ScPoiTypes.Classify(pi);
                    if (!cls.Producing || cls.Kind != "row" || cls.Row == null || cls.Row.Weight[(int)r] <= 0f) continue;
                    float d = ScOutposts.DistanceToHome(pi.boundingBoxPosition, pi.boundingBoxSize);
                    if (d < ScConfig.OutpostMinDistance || d > range) continue;
                    // a primary match sorts ahead of a POI that only makes a little of it on the side
                    float key = d + (cls.Row.Primary == r ? 0f : 100000f);
                    found.Add(new KeyValuePair<float, PrefabInstance>(key, pi));
                }
                found.Sort((a, b) => a.Key.CompareTo(b.Key));
                Community c = ScRegistry.Community;
                c.RadioNextScoutDay = ScCommunity.Today + 1;
                if (found.Count == 0)
                {
                    ScRegistry.SaveAtomic();
                    message = string.Format(Localization.Get("scScoutNone"), Localization.Get(ScNames.LocKey(r)), range);
                    return true;
                }
                var sb = new StringBuilder();
                int n = 0;
                foreach (var kv in found)
                {
                    if (n >= Math.Max(1, ScConfig.RadioScoutCount)) break;
                    PrefabInstance pi = kv.Value;
                    foreach (Scout old in c.Scouts) if (old.Poi == pi.boundingBoxPosition) RemoveMarker(old);
                    c.Scouts.RemoveAll(s => s.Poi == pi.boundingBoxPosition);
                    var sc = new Scout
                    {
                        Poi = pi.boundingBoxPosition,
                        Size = pi.boundingBoxSize,
                        Name = ScSecurity.DisplayName(pi),
                        Res = r,
                        ExpireDay = ScCommunity.Today + Math.Max(1, ScConfig.RadioScoutDays)
                    };
                    c.Scouts.Add(sc);
                    AddMarker(sc, player);
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append(string.Format(Localization.Get("scScoutSite"), sc.Name, Where(sc)));
                    n++;
                }
                ScRegistry.SaveAtomic();
                message = string.Format(Localization.Get("scScoutFound"), Localization.Get(ScNames.LocKey(r)), sb);
                ScLog.Out("radio scouting for " + r + ": " + sb);
                return true;
            }
            catch (Exception e)
            {
                ScLog.Once("scout", e);
                message = Localization.Get("scInternal");
                return false;
            }
        }

        /// <summary>"900 m north" from home.</summary>
        public static string Where(Scout s)
        {
            Vector3 c = s.Centre;
            Vector3i h = ScZone.Home;
            float dx = c.x - h.x, dz = c.z - h.z;
            int sector = Mathf.RoundToInt(Mathf.Repeat(Mathf.Atan2(dx, dz) * Mathf.Rad2Deg, 360f) / 45f) % 8;
            string[] keys = { "scDirN", "scDirNE", "scDirE", "scDirSE", "scDirS", "scDirSW", "scDirW", "scDirNW" };
            return string.Format(Localization.Get("scScoutWhere"), Mathf.RoundToInt(Mathf.Sqrt(dx * dx + dz * dz)), Localization.Get(keys[sector]));
        }

        /// <summary>ScTicker every second (works every 10): fade expired chatter, restore markers after a load.</summary>
        public static void Tick(EntityPlayerLocal player)
        {
            try
            {
                if (player == null) return;
                float now = Time.unscaledTime;
                if (now < nextTick) return;
                nextTick = now + 10f;
                Community c = ScRegistry.Community;
                if (c.Scouts.Count == 0 && markers.Count == 0) return;
                int today = ScCommunity.Today;
                bool changed = false;
                for (int i = c.Scouts.Count - 1; i >= 0; i--)
                {
                    Scout s = c.Scouts[i];
                    // faded, or since made an outpost
                    if (today < s.ExpireDay && !ScOutposts.IsOutpostPoiKey(s.Poi)) continue;
                    RemoveMarker(s);
                    c.Scouts.RemoveAt(i);
                    changed = true;
                }
                if (!restored)
                {
                    restored = true;
                    foreach (Scout s in c.Scouts) AddMarker(s, player);
                }
                if (changed) ScRegistry.SaveAtomic();
            }
            catch (Exception e)
            {
                ScLog.Once("scout-tick", e);
            }
        }

        private static void AddMarker(Scout s, EntityPlayer owner)
        {
            try
            {
                NavObjectManager nav = NavObjectManager.Instance;
                string cls = NavPrefix + ScNames.Key(s.Res);
                if (nav == null || owner == null || NavObjectClass.GetNavObjectClass(cls) == null) return;
                RemoveMarker(s);
                Vector3 at = s.Centre;
                NavObject obj = nav.RegisterNavObject(cls, at, "", false, -1, owner);
                if (obj != null) obj.name = string.Format(Localization.Get("scScoutMarker"), s.Name, Localization.Get(ScNames.LocKey(s.Res)));
                markers[s.Poi] = at;
            }
            catch (Exception e)
            {
                ScLog.Once("scout-marker", e);
            }
        }

        private static void RemoveMarker(Scout s)
        {
            try
            {
                Vector3 at;
                if (!markers.TryGetValue(s.Poi, out at)) return;
                markers.Remove(s.Poi);
                NavObjectManager nav = NavObjectManager.Instance;
                if (nav != null) foreach (string k in ScNames.ResourceKeys) nav.UnRegisterNavObjectByPosition(at, NavPrefix + k);
            }
            catch (Exception e)
            {
                ScLog.Once("scout-unmarker", e);
            }
        }

        // ---- registry text: x,y,z|w,h,d|res|expire|name;... ----

        public static string Encode(List<Scout> scouts)
        {
            var sb = new StringBuilder();
            foreach (Scout s in scouts)
            {
                if (sb.Length > 0) sb.Append(';');
                sb.Append(s.Poi.x).Append(',').Append(s.Poi.y).Append(',').Append(s.Poi.z).Append('|')
                  .Append(s.Size.x).Append(',').Append(s.Size.y).Append(',').Append(s.Size.z).Append('|')
                  .Append(ScNames.Key(s.Res)).Append('|').Append(s.ExpireDay).Append('|')
                  .Append((s.Name ?? "").Replace("\r", "").Replace("\n", " ").Replace(";", ",").Replace("|", "/").Replace("=", "-"));
            }
            return sb.ToString();
        }

        public static void Decode(string val, List<Scout> into)
        {
            into.Clear();
            if (string.IsNullOrEmpty(val)) return;
            foreach (string part in val.Split(';'))
            {
                string[] f = part.Split('|');
                if (f.Length < 5) continue;
                int[] a = Ints(f[0]), b = Ints(f[1]);
                Resource r;
                int exp;
                if (a == null || b == null || !ScNames.TryResource(f[2], out r) || !int.TryParse(f[3], out exp)) continue;
                into.Add(new Scout { Poi = new Vector3i(a[0], a[1], a[2]), Size = new Vector3i(b[0], b[1], b[2]), Res = r, ExpireDay = exp, Name = f[4] });
            }
        }

        private static int[] Ints(string s)
        {
            string[] p = s.Split(',');
            if (p.Length != 3) return null;
            var v = new int[3];
            for (int i = 0; i < 3; i++) if (!int.TryParse(p[i].Trim(), out v[i])) return null;
            return v;
        }
    }
}
