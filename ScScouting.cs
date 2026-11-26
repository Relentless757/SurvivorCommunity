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

        /// <summary>Round MP5 (part R): who - the player at the radio (ScRadio.Usable(who): their station); null this machine's own.</summary>
        public static bool CanTrade(Resource give, Resource get, out string why, EntityPlayer who = null)
        {
            why = null;
            if (!ScCommunity.IsHost) { why = Localization.Get("scHostOnly"); return false; }
            if ((int)give >= Kinds || (int)get >= Kinds) { why = Localization.Get("scBarterNoLuxury"); return false; }
            if (!ScRadio.Usable(who)) { why = Localization.Get("scRadioNoPower"); return false; }
            if (give == get) { why = Localization.Get("scBarterSame"); return false; }
            Community c = ScRegistry.Community;
            int wait = c.RadioNextBarterDay - ScCommunity.Today;
            if (wait > 0) { why = string.Format(Localization.Get("scBarterWait"), wait); return false; }
#if SC_V34
            int rate = ScLifeSkills.RadioBarter().Give;     // round m1t: Bartering gives fewer
#else
            int rate = Math.Max(1, ScConfig.RadioBarterRate);
#endif
            if (c.Get(give) < rate) { why = string.Format(Localization.Get("scBarterShort"), rate, Localization.Get(ScNames.LocKey(give)), c.Get(give)); return false; }
            return true;
        }

        public static bool Trade(Resource give, Resource get, EntityPlayer player, out string message)     // round MP5 (part R): any player (stockpile-only)
        {
            if (!CanTrade(give, get, out message, player)) return false;
            Community c = ScRegistry.Community;
#if SC_V34
            // round m1t: the terms with Bartering's cut and a Haggler's extra (ScLifeSkills.RadioBarter)
            ScLifeSkills.Terms t = ScLifeSkills.RadioBarter();
            int rate = t.Give, n = t.Get;
            c.Add(give, -rate);
            c.Add(get, n);
            c.RadioNextBarterDay = ScCommunity.Today + 1;
            ScOutposts.NoteLedger(string.Format(Localization.Get("scBarterLedgerN"), rate, Localization.Get(ScNames.LocKey(give)), n, Localization.Get(ScNames.LocKey(get))));
            ScLifeSkills.OnBarter(t);
            ScRegistry.SaveAtomic();
            message = string.Format(Localization.Get("scBarterDoneN"), rate, Localization.Get(ScNames.LocKey(give)), n, Localization.Get(ScNames.LocKey(get)))
                      + ScLifeSkills.HelpText(t);
            ScLog.Out("radio barter: " + rate + " " + give + " for " + n + " " + get
                      + (t.Barterer != null ? " (Bartering: " + t.Barterer.Name + ")" : "") + (t.Haggler != null ? " (Haggler: " + t.Haggler.Name + ")" : ""));
#else
            int rate = Math.Max(1, ScConfig.RadioBarterRate);
            c.Add(give, -rate);
            c.Add(get, 1);
            c.RadioNextBarterDay = ScCommunity.Today + 1;
            ScOutposts.NoteLedger(string.Format(Localization.Get("scBarterLedger"), rate, Localization.Get(ScNames.LocKey(give)), Localization.Get(ScNames.LocKey(get))));
            ScRegistry.SaveAtomic();
            message = string.Format(Localization.Get("scBarterDone"), rate, Localization.Get(ScNames.LocKey(give)), Localization.Get(ScNames.LocKey(get)));
            ScLog.Out("radio barter: " + rate + " " + give + " for 1 " + get);
#endif
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
        /// <summary>Round MP6 (fix N-m1): the community whose scout marks the host's map shows (-1: none restored yet).</summary>
        private static int markersFor = -1;
        private static readonly ScHomeState<ScHomeTimer> homeTicks = new ScHomeState<ScHomeTimer>();     // round MP6 (part H): the ticker step's throttle, one per community
        private static float nextTick { get { return homeTicks.Current.Next; } set { homeTicks.Current.Next = value; } }

        public static void Reset()
        {
            markers.Clear();
            markersFor = -1;
            homeTicks.Reset();                    // round MP6 (fix H-N3): every community's throttle (was the scope's row only)
        }

        public static void OnRegistryChanged()
        {
            markers.Clear();
            markersFor = -1;
        }

        /// <summary>Round MP5 (part R): who - the player at the radio (ScRadio.Usable(who): their station); null this machine's own.</summary>
        public static bool CanScout(out string why, EntityPlayer who = null)
        {
            why = null;
            if (!ScCommunity.IsHost) { why = Localization.Get("scHostOnly"); return false; }
            if (!ScZone.HasHome) { why = Localization.Get("scNeedHome"); return false; }
            if (!ScRadio.Usable(who)) { why = Localization.Get("scRadioNoPower"); return false; }
            int wait = ScRegistry.Community.RadioNextScoutDay - ScCommunity.Today;
            if (wait > 0) { why = string.Format(Localization.Get("scScoutWait"), wait); return false; }
            return true;
        }

        /// <summary>The radio window's scout button for one resource.</summary>
        public static bool Scout(Resource r, EntityPlayer player, out string message)     // round MP5 (part R): any player (the marker: this machine's own; ScMarkers for the rest)
        {
            message = null;
            if ((int)r >= ScNames.ClassicCount) { message = Localization.Get("scScoutClassicOnly"); return false; }
            if (!CanScout(out message, player)) return false;
            // round MP5 (part R): the markers are this machine's own player's - a listen host's own map too when a client scouted
            EntityPlayer markFor = player is EntityPlayerLocal ? player : ScCommunity.LocalPlayer;
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
                    if (ScEncRequests.IsTarget(pi.boundingBoxPosition)) continue;     // E5: nor one an enclave's request sends the player to
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
#if SC_V34
                Member recon;
                int count = ScLifeSkills.ScoutCount(out recon);     // round m1t (T2): Recon marks 5
#else
                int count = Math.Max(1, ScConfig.RadioScoutCount);
#endif
                foreach (var kv in found)
                {
                    if (n >= count) break;
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
                    AddMarker(sc, markFor);
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append(string.Format(Localization.Get("scScoutSite"), sc.Name, Where(sc)));
                    n++;
                }
#if SC_V34
                if (recon != null && n > Math.Max(1, ScConfig.RadioScoutCount)) ScLifeSkills.OnScouted(recon);   // round m1t: she found the extra ones
#endif
                ScRegistry.SaveAtomic();
                message = string.Format(Localization.Get("scScoutFound"), Localization.Get(ScNames.LocKey(r)), sb);
#if SC_V34
                if (recon != null && n > Math.Max(1, ScConfig.RadioScoutCount)) message += " " + string.Format(Localization.Get("scScoutRecon"), recon.Name);
#endif
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
        public static void Tick(EntityPlayer player)
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
                // round MP6 (fix N-m1): restored for the host's own player in its own community's pass (HostOwner) - once per community the host is in: the flag was one for the world, set in whichever community's pass came first (a remote anchor drew nothing), and a host moved by sc community join kept the old community's
                EntityPlayerLocal host = ScMarkers.HostOwner();
                if (host != null && markersFor != ScRegistry.CurrentId)
                {
                    markersFor = ScRegistry.CurrentId;
                    ClearHostMarkers();
                    foreach (Scout s in c.Scouts) AddMarker(s, host);
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
                // round MP0: the marker is the host's own player's (a local map object) - none on a dedicated server.
                // round MP4: remote players' come from ScMarkers (CollectMarkers)
                if (!(owner is EntityPlayerLocal)) return;
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

        /// <summary>Round MP6 (fix N-m1): every scout mark on the host's map goes (another community's, after the host moved).</summary>
        private static void ClearHostMarkers()
        {
            try
            {
                NavObjectManager nav = NavObjectManager.Instance;
                if (nav != null)
                    foreach (Vector3 at in markers.Values)
                        foreach (string k in ScNames.ResourceKeys) nav.UnRegisterNavObjectByPosition(at, NavPrefix + k);
            }
            catch (Exception e)
            {
                ScLog.Once("scout-unmarker-all", e);
            }
            markers.Clear();
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

        /// <summary>
        /// Round MP4 (part K): every scouted site's marker as AddMarker draws it, for ScMarkers - the set each remote community
        /// player registers for themselves (keyed by the site's POI corner). Server side; registers nothing.
        /// </summary>
        internal static void CollectMarkers(List<ScMarkers.Entry> into)
        {
            foreach (Scout s in ScRegistry.Community.Scouts)
            {
                string key = s.Poi.x.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," + s.Poi.y.ToString(System.Globalization.CultureInfo.InvariantCulture)
                             + "," + s.Poi.z.ToString(System.Globalization.CultureInfo.InvariantCulture);
                into.Add(new ScMarkers.Entry(ScMarkers.KindScout, key, NavPrefix + ScNames.Key(s.Res), s.Centre,
                                             string.Format(Localization.Get("scScoutMarker"), s.Name, Localization.Get(ScNames.LocKey(s.Res)))));
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
