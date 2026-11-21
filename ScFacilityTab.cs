using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>One fact on the right-hand panel: a caption and a value (its color numeric RGBA).</summary>
    public sealed class FacilityFact
    {
        public string Label = "", Value = "", Color = ScFacilityTab.Body;
    }

    /// <summary>
    /// One action button of a facility. Either it acts at once (Run), or it first picks a resident: the window shows the
    /// residents with PickHint and runs Pick on the one picked (Call to work, Assign guard). message: the outcome line.
    /// </summary>
    public sealed class FacilityAction
    {
        public delegate bool RunFn(EntityPlayerLocal player, out string message);
        public delegate bool PickFn(Member m, EntityPlayerLocal player, out string message);

        public string Label = "";
        /// <summary>Lit (it can act now); a dim button still says why not when pressed.</summary>
        public bool Ready;
        public bool Danger;
        public RunFn Run;
        public PickFn Pick;
        public string PickHint = "";
        /// <summary>Why it can't act now, as a finished line (null: it can) - asked before a pick opens (the Fable review's finding 6).</summary>
        public Func<string> WhyNot;
    }

    /// <summary>One row of the Facilities tab and what its right-hand panel shows.</summary>
    public sealed class FacilityEntry
    {
        /// <summary>Stable id for the selection: the facility's id, or "site@x,y,z" for one well or dew collector.</summary>
        public string Key = "";
        public string Name = "", Icon = "ui_game_symbol_workbench", Status = "", StatusColor = ScFacilityTab.Body, Sub = "", About = "";
        public readonly List<FacilityFact> Facts = new List<FacilityFact>();
        public readonly List<FacilityAction> Actions = new List<FacilityAction>();

        public FacilityEntry Fact(string labelKey, string value, string color = null)
        {
            Facts.Add(new FacilityFact { Label = Localization.Get(labelKey), Value = value ?? "", Color = color ?? ScFacilityTab.Body });
            return this;
        }
    }

    /// <summary>Adds a facility's entries (none, one, or one per block) to the tab's list.</summary>
    public delegate void FacilityProvider(List<FacilityEntry> into);

    /// <summary>
    /// The community window's Facilities tab (round m1f, 2026-11-17; ROADMAP.md section 3 item 4, the user's decisions of the
    /// day): it replaces the Activities tab. The list on the left, the selected facility's detail and actions on the right, the
    /// pattern of Members and Outposts. Activities are actions of the facility that hosts them (ScActivities.All's fifth
    /// column): the round of drinks is the campfire's.
    ///
    /// All is the registry, in list order - like ScActivities.All, a table: each row a facility id and the provider that adds
    /// its entries. Later features add theirs here, or with Register from their own file (Kitchen, Infirmary, Latrine, Lounge,
    /// Gym, Range, Wall). The entries are built from saved state only (the scan's per-chunk sites, the registry), so the tab
    /// reads the same from anywhere; an action that needs the block (a well's upgrade) says so when its part of the base is not
    /// loaded.
    ///
    /// Defense (FT2, the D5 window section): the defense rating, the noise and the attention, the last attack, and Lights out
    /// (ScLightsOut). Host only, like the rest of the window's actions; on a client every action says so.
    /// </summary>
    public static class ScFacilityTab
    {
        public const string Defense = "defense", Beds = "beds", Water = "water", Workshop = "workshop", Campfire = "campfire",
                            Radio = "radio", Watch = "watch", Tower = "tower", Sites = "sites";
        public const string Body = "226,220,208,255", Dim = "190,182,166,255", Off = "140,134,124,255", Good = "142,208,108,255",
                            Warn = "220,170,70,255", Bad = "226,108,86,255";

        /// <summary>The registry: the tab's rows in order (the water sources one by one at the end, as there can be many).</summary>
        public static readonly List<KeyValuePair<string, FacilityProvider>> All = new List<KeyValuePair<string, FacilityProvider>>
        {
            new KeyValuePair<string, FacilityProvider>(Defense, AddDefense),
            new KeyValuePair<string, FacilityProvider>(Beds, AddBeds),
            new KeyValuePair<string, FacilityProvider>(Water, AddWater),
            new KeyValuePair<string, FacilityProvider>(Workshop, AddWorkshop),
            new KeyValuePair<string, FacilityProvider>(Campfire, AddCampfire),
            new KeyValuePair<string, FacilityProvider>(Radio, AddRadio),
            new KeyValuePair<string, FacilityProvider>(Watch, AddWatch),
            new KeyValuePair<string, FacilityProvider>(Tower, AddTower),
            new KeyValuePair<string, FacilityProvider>(Sites, AddWaterSites),
        };

        /// <summary>A later feature's facility, before the given id (null or unknown: before the water sources). Replaces one with the same id.</summary>
        public static void Register(string id, FacilityProvider provider, string before = null)
        {
            if (string.IsNullOrEmpty(id) || provider == null) return;
            All.RemoveAll(kv => kv.Key == id);
            int at = All.FindIndex(kv => kv.Key == (before ?? Sites));
            if (at < 0) at = All.FindIndex(kv => kv.Key == Sites);
            if (at < 0) at = All.Count;
            All.Insert(at, new KeyValuePair<string, FacilityProvider>(id, provider));
        }

        public static void Reset() { }

        /// <summary>Every entry, in the registry's order. Never throws (a provider that does is logged once and skipped).</summary>
        public static List<FacilityEntry> Build()
        {
            var list = new List<FacilityEntry>();
            if (!ScRegistry.Loaded) return list;
            for (int i = 0; i < All.Count; i++)
            {
                try { All[i].Value(list); }
                catch (Exception e) { ScLog.Once("facility-tab-" + All[i].Key, e); }
            }
            return list;
        }

        // ------------------------------------------------------------------ helpers ----

        private static string L(string key) { return Localization.Get(key); }

        private static string Fmt(string key, params object[] args)
        {
            try { return string.Format(Localization.Get(key), args); }
            catch (FormatException) { return Localization.Get(key); }
        }

        private static string N(int v) { return v.ToString(CultureInfo.InvariantCulture); }

        private static string Signed(int v) { return (v > 0 ? "+" : "") + v.ToString(CultureInfo.InvariantCulture); }

        private static string F1(float v) { return v.ToString("0.#", CultureInfo.InvariantCulture); }

        /// <summary>The refusal every action gives on a client (the community is the host's).</summary>
        private static bool HostOnly(out string message)
        {
            message = ScCommunity.IsHost ? null : L("scHostOnly");
            return message != null;
        }

        /// <summary>"12 m northeast of home" (the outposts' words).</summary>
        public static string Where(Vector3i p)
        {
            if (!ScZone.HasHome) return "";
            Vector3i h = ScZone.Home;
            float dx = p.x - h.x, dz = p.z - h.z;
            int dist = Mathf.RoundToInt(Mathf.Sqrt(dx * dx + dz * dz));
            if (dist < 2) return L("scUiFacAtHome");
            int sector = Mathf.RoundToInt(Mathf.Repeat(Mathf.Atan2(dx, dz) * Mathf.Rad2Deg, 360f) / 45f) % 8;
            string[] keys = { "scDirN", "scDirNE", "scDirE", "scDirSE", "scDirS", "scDirSW", "scDirW", "scDirNW" };
            return Fmt("scUiOpDistFmt", dist, L(keys[sector]));
        }

        /// <summary>"04:00" for a world time.</summary>
        public static string TimeOfDay(ulong t)
        {
            return GameUtils.WorldTimeToHours(t).ToString("00", CultureInfo.InvariantCulture) + ":" + GameUtils.WorldTimeToMinutes(t).ToString("00", CultureInfo.InvariantCulture);
        }

        private static string BlockLabel(string name)
        {
            try
            {
                Block b = Block.GetBlockByName(name, true);
                return b != null ? b.GetLocalizedBlockName() : name;
            }
            catch (Exception) { return name; }
        }

        /// <summary>A CampfireBlocks name: exact, or a prefix ending in * (ScSocial's rule).</summary>
        public static bool IsCampfireName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            foreach (string raw in (ScConfig.CampfireBlocks ?? "").Split(','))
            {
                string e = raw.Trim();
                if (e.Length == 0) continue;
                if (e.EndsWith("*", StringComparison.Ordinal))
                {
                    if (name.StartsWith(e.Substring(0, e.Length - 1), StringComparison.OrdinalIgnoreCase)) return true;
                }
                else if (string.Equals(name, e, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>The scan's sites inside the zone (the zone's chunks, loaded or remembered), sorted by name then position.</summary>
        private static List<KeyValuePair<Vector3i, string>> AllSites()
        {
            var all = new List<KeyValuePair<Vector3i, string>>();
            foreach (List<KeyValuePair<Vector3i, string>> l in ScRegistry.Community.ScanSites.Values) all.AddRange(l);
            all.Sort((a, b) =>
            {
                int s = string.Compare(a.Value, b.Value, StringComparison.OrdinalIgnoreCase);
                if (s != 0) return s;
                s = a.Key.x.CompareTo(b.Key.x);
                return s != 0 ? s : (a.Key.z != b.Key.z ? a.Key.z.CompareTo(b.Key.z) : a.Key.y.CompareTo(b.Key.y));
            });
            return all;
        }

        /// <summary>Round fd1: the campfires (CampfireBlocks) in the base, from the scan's sites - a feast needs one.</summary>
        public static int CampfireCount() { return CountSites(IsCampfireName); }

        private static int CountSites(Func<string, bool> match)
        {
            int n = 0;
            foreach (List<KeyValuePair<Vector3i, string>> l in ScRegistry.Community.ScanSites.Values)
                foreach (KeyValuePair<Vector3i, string> s in l) if (match(s.Value)) n++;
            return n;
        }

        private static int TypeOf(string name)
        {
            Block b = string.IsNullOrEmpty(name) ? null : Block.GetBlockByName(name, true);
            return b != null ? b.blockID : 0;
        }

        /// <summary>
        /// Zone chunks the home scan has a count for but no site list yet (last counted by a build before the sites, and not loaded
        /// since; the Fable review's finding 8): their stations / wells and dew collectors are in the totals, not in the lists.
        /// </summary>
        private static void MissingSites(out bool stations, out bool water)
        {
            Community c = ScRegistry.Community;
            stations = water = false;
            foreach (KeyValuePair<long, int[]> kv in c.ScanChunks)
                if (kv.Value != null && kv.Value.Length > 3 && kv.Value[3] > 0 && !c.ScanSites.ContainsKey(kv.Key)) { stations = true; break; }
            foreach (KeyValuePair<long, int> kv in c.ScanWater)
                if (kv.Value > 0 && !c.ScanSites.ContainsKey(kv.Key)) { water = true; break; }
        }

        /// <summary>A member is on a work shift (not a talk hold).</summary>
        private static bool AnyOnWorkShift()
        {
            foreach (Member m in ScRegistry.Members.Values) if (ScWork.OnWorkShift(m.Id)) return true;
            return false;
        }

        /// <summary>The player stands inside the home zone.</summary>
        private static bool PlayerAtHome()
        {
            EntityPlayerLocal p = ScCommunity.LocalPlayer;
            return p != null && ScZone.HasHome && ScZone.Contains(p.GetBlockPosition());
        }

        // ------------------------------------------------------------------ Defense (FT2) ----

        private static void AddDefense(List<FacilityEntry> into)
        {
            Community c = ScRegistry.Community;
            EntityPlayerLocal player = ScCommunity.LocalPlayer;
            World w = ScCommunity.World;
            int horde = ScBaseAttack.Open ? Math.Max(1, c.AttackSize - c.AttackKilled) : (player != null ? ScBaseAttack.Size(player) : ScConfig.BaseAttackMin);
            int rating = ScDefence.Rating(horde, null);
            string ratingColor = rating >= 60 ? Good : rating >= 35 ? Warn : Bad;
            float noise = ScNoise.Current();
            ScNoise.Level lv = ScNoise.LevelOf(noise);
            string noiseColor = lv == ScNoise.Level.VeryLoud ? Bad : lv == ScNoise.Level.Loud ? Warn : lv == ScNoise.Level.Quiet ? Good : Body;
            var e = new FacilityEntry
            {
                Key = Defense,
                Name = L("scUiFacDefense"),
                Icon = "ui_game_symbol_defense",
                Status = ScBaseAttack.Open ? L(ScBaseAttack.Live ? "scUiFacDefLive" : ScBaseAttack.Away ? "scUiFacDefAway" : "scUiFacDefCalled")
                                           : Fmt("scUiFacDefRow", rating, Mathf.RoundToInt(c.Attention)),
                StatusColor = ScBaseAttack.Open ? Bad : ratingColor,
                Sub = !ScConfig.BaseNoise ? L("scUiFacNoiseOff")
                    : ScLightsOut.Active ? Fmt("scUiFacLightsOutSub", TimeOfDay(c.LightsOutUntil))
                    : Fmt("scUiFacDefSub", ScNoise.Word(lv)),
            };
            e.Fact("scUiFacDefRating", Fmt("scUiFacDefRatingVal", rating, horde), ratingColor);
            e.Fact("scUiFacDefNoise", ScConfig.BaseNoise ? Fmt("scUiFacDefNoiseVal", F1(noise), ScNoise.Word(lv)) : L("scUiFacOff"), ScConfig.BaseNoise ? noiseColor : Off);
            e.Fact("scUiFacDefAttention", AttentionText(c), c.Attention >= 75f ? Bad : c.Attention >= 40f ? Warn : Body);
            e.Fact("scUiFacDefLast", ScSalvage.LastText(), ScSalvage.LastColor());

            var about = new StringBuilder();
            if (ScBaseAttack.Open)
            {
                int left = Math.Max(0, c.AttackSize - c.AttackKilled);
                string side = ScBaseAttack.Side(c.AttackBearing);
                about.Append(Fmt(ScBaseAttack.Live ? "scUiFacDefStateLive" : ScBaseAttack.Away ? "scUiFacDefStateAway" : "scUiFacDefStateCalled", left, side)).Append('\n');
            }
            about.Append(ScLightsOut.AboutText(w)).Append('\n');
            about.Append(L("scUiFacDefAbout"));
            if (ScConfig.SalvageKillsPerMaterial > 0) about.Append('\n').Append(Fmt("scUiFacSalvageAbout", ScConfig.SalvageKillsPerMaterial));
            e.About = about.ToString();

            if (ScLightsOut.Active)
                e.Actions.Add(new FacilityAction { Label = L("scUiFacLightsOn"), Ready = ScCommunity.IsHost, Run = ScLightsOut.CancelByOrder });
            else
                e.Actions.Add(new FacilityAction { Label = L("scUiFacLightsOut"), Ready = ScLightsOut.WhyNot(w) == null, Run = ScLightsOut.Order });
            into.Add(e);
        }

        /// <summary>"42% (+1.3 an hour)", or why it is not moving.</summary>
        private static string AttentionText(Community c)
        {
            int pct = Mathf.RoundToInt(c.Attention);
            if (!ScConfig.BaseNoise) return Fmt("scUiFacPct", pct);
            if (ScNoise.BloodMoonPause) return Fmt("scUiFacAttBloodMoon", pct);
            if (ScBaseAttack.Open) return Fmt("scUiFacAttAttack", pct);
            float rate = ScNoise.RatePerHour(ScNoise.Current()) + (ScConfig.Infestations ? ScInfestations.NestsAttention() : 0f);
            if (rate > 0f) rate *= ScWatch.AttentionFactor();
            if (c.Attention >= 100f && ScBaseAttack.HoldsFullAttention && rate <= 0f) return Fmt("scUiFacAttFull", pct);
            return Fmt("scUiFacAttRate", pct, (rate >= 0f ? "+" : "") + rate.ToString("0.#", CultureInfo.InvariantCulture));
        }

        // ------------------------------------------------------------------ Beds ----

        private static void AddBeds(List<FacilityEntry> into)
        {
            int members = ScRegistry.Members.Count, beds = ScEncAllies.TotalBeds(), allies = ScEncAllies.ExtraBeds();
            int term = ScMorale.Compute().Beds;
            bool shortOf = members > beds;
            var e = new FacilityEntry
            {
                Key = Beds,
                Name = L("scUiFacBeds"),
                Icon = "ui_game_symbol_map_bed",
                Status = Fmt("scUiFacBedsRow", beds, members),
                StatusColor = members == 0 ? Body : shortOf ? Bad : Good,
                Sub = members == 0 ? L("scUiFacBedsNobody") : shortOf ? Fmt("scUiFacBedsShort", members - beds) : Fmt("scUiFacBedsSpare", beds - members),
            };
            e.Fact("scUiFacBedsCount", allies > 0 ? Fmt("scUiFacBedsWithAllies", beds, allies) : N(beds));
            e.Fact("scUiFacBedsNeeded", N(members));
            e.Fact("scUiFacMorale", Signed(term), term > 0 ? Good : term < 0 ? Bad : Body);
            e.Fact("scUiFacCounted", L(ScScan.LastMissing > 0 ? "scUiFacCountedPart" : "scUiFacCountedAll"), ScScan.LastMissing > 0 ? Warn : Body);
            e.About = L(ScConfig.BedsBySleepingBag ? "scUiFacBedsRule" : "scUiFacBedsRuleNames") + "\n" + Fmt("scUiFacBedsNight", ScConfig.SleepZoneHeight);
            into.Add(e);
        }

        // ------------------------------------------------------------------ Water (the summary; the sources come at the end) ----

        private static void AddWater(List<FacilityEntry> into)
        {
            Community c = ScRegistry.Community;
            int wells = CountSites(n => ScFacilities.WellLevel(TypeOf(n)) > 0), dews = CountSites(n => ScFacilities.IsDew(TypeOf(n)));
            bool partStations, partWater;
            MissingSites(out partStations, out partWater);
            int drunk = ScEconomy.WaterDrunk();          // round ie1: the rollover's figure, whole (members still on the way drink nothing)
            bool covered = c.HomeWater >= drunk;
            var e = new FacilityEntry
            {
                Key = Water,
                Name = L("scUiFacWater"),
                Icon = "ui_game_symbol_water",
                Status = Fmt("scUiFacWaterRow", c.HomeWater, N(drunk)),
                StatusColor = ScRegistry.Members.Count == 0 ? Body : covered ? Good : Warn,
                Sub = covered ? L("scUiFacWaterCovered") : Fmt("scUiFacWaterShort", N(ScEconomy.WaterShortfall())),
            };
            e.Fact("scUiFacWaterMade", N(c.HomeWater));
            e.Fact("scUiFacWaterDrunk", N(drunk));
            if (partWater)
            {
                // the water made is the scan's total; the list lacks the sources of chunks not counted with their sites yet
                e.Fact("scUiFacCounted", L("scUiFacCountedPart"), Warn);
                e.Fact("scUiFacSourcesListed", Fmt("scUiFacWellsDews", wells, dews), Warn);
            }
            else
            {
                e.Fact("scUiFacWells", N(wells));
                e.Fact("scUiFacDews", N(dews));
            }
            e.About = Fmt("scUiFacWaterAbout", ScConfig.WellWaterPerLevel, ScConfig.DewWaterPerLevel,
                          string.Join(", ", Array.ConvertAll(ScConfig.FacilityUpgradeCost, v => v.ToString(CultureInfo.InvariantCulture))))
                      + (partWater ? "\n" + L("scUiFacSitesPartAbout") : "");
            into.Add(e);
        }

        /// <summary>Each well and dew collector the scan saw in the zone, with its level, its water and Upgrade.</summary>
        private static void AddWaterSites(List<FacilityEntry> into)
        {
            Community c = ScRegistry.Community;
            foreach (KeyValuePair<Vector3i, string> s in AllSites())
            {
                int type = TypeOf(s.Value);
                if (type <= 0 || !ScFacilities.IsFacility(type)) continue;
                Vector3i pos = s.Key;
                BlockValue bv = Block.list[type].ToBlockValue();
                int level = ScFacilities.LevelAt(pos, bv), top = ScFacilities.MaxLevel(type), water = ScFacilities.WaterAt(pos, bv);
                bool maxed = level >= top;
                int cost = ScFacilities.NextCost(level);
                bool well = ScFacilities.WellLevel(type) > 0;
                var e = new FacilityEntry
                {
                    Key = "site@" + pos.x + "," + pos.y + "," + pos.z,
                    Name = BlockLabel(s.Value),
                    Icon = "ui_game_symbol_water",
                    Status = Fmt("scUiFacSiteRow", level, water),
                    StatusColor = Body,
                    Sub = Where(pos),
                };
                e.Fact("scUiFacLevel", Fmt("scUiFacLevelOf", level, top));
                e.Fact("scUiFacWaterADay", N(water));
                e.Fact("scUiFacNextLevel", maxed ? L("scUiFacTopLevel") : Fmt("scUiFacNextCost", cost, c.Get(Resource.Materials)),
                       maxed || c.Get(Resource.Materials) >= cost ? Body : Bad);
                e.Fact("scUiFacKind", L(well ? "scUiFacKindWell" : "scUiFacKindDew"));
                e.About = L(well ? "scUiFacWellAbout" : "scUiFacDewAbout");
                Vector3i at = pos;
                e.Actions.Add(new FacilityAction
                {
                    Label = L("scUiFacUpgrade"),
                    Ready = ScCommunity.IsHost && !maxed && c.Get(Resource.Materials) >= cost,
                    Run = (EntityPlayerLocal p, out string msg) => UpgradeSite(at, p, out msg),
                });
                into.Add(e);
            }
        }

        /// <summary>ScFacilities' own upgrade for the block at pos - only while its part of the base is loaded (it swaps the block).</summary>
        private static bool UpgradeSite(Vector3i pos, EntityPlayerLocal player, out string message)
        {
            if (HostOnly(out message)) return false;
            World w = ScCommunity.World;
            if (w == null || !ScZone.IsLoaded(pos)) { message = L("scUiFacUpgradeFar"); return false; }
            BlockValue bv = w.GetBlock(pos);
            if (bv.ischild || !ScFacilities.IsFacility(bv.type)) { message = L("scUiFacSiteGone"); ScScan.RunIfDue(0f); return false; }
            return ScFacilities.TryUpgrade(w, pos, bv, player, out message);
        }

        // ------------------------------------------------------------------ the workshop ----

        private static void AddWorkshop(List<FacilityEntry> into)
        {
            Community c = ScRegistry.Community;
            var kinds = new List<string>();
            foreach (string n in ScConfig.FacilityBlocks)
            {
                bool seen = false;
                foreach (string k in kinds) if (string.Equals(k, n, StringComparison.OrdinalIgnoreCase)) seen = true;
                if (!seen && !IsCampfireName(n)) kinds.Add(n);
            }
            int total = 0;
            var counts = new int[kinds.Count];
            foreach (List<KeyValuePair<Vector3i, string>> l in c.ScanSites.Values)
                foreach (KeyValuePair<Vector3i, string> s in l)
                    for (int i = 0; i < kinds.Count; i++)
                        if (string.Equals(s.Value, kinds[i], StringComparison.OrdinalIgnoreCase)) { counts[i]++; total++; break; }
            int morale = ScMorale.Compute().Facilities;
            bool partStations, partWater;
            MissingSites(out partStations, out partWater);
            var e = new FacilityEntry
            {
                Key = Workshop,
                Name = L("scUiFacWorkshop"),
                Icon = "ui_game_symbol_workbench",
                // part of the base not listed yet: the scan's total (campfires too) instead of a list that reads short
                Status = partStations ? Fmt("scUiFacFacilitiesCounted", c.Facilities) : total == 1 ? L("scUiFacStationsOne") : Fmt("scUiFacStations", total),
                StatusColor = partStations ? Warn : total > 0 ? Body : Off,
                Sub = Fmt("scUiFacWorkshopSub", Signed(morale), c.Facilities),
            };
            if (partStations) e.Fact("scUiFacCounted", L("scUiFacCountedPart"), Warn);
            int slots = partStations ? 3 : 4;
            var more = new StringBuilder();
            for (int i = 0; i < kinds.Count; i++)
            {
                if (i < slots) e.Facts.Add(new FacilityFact { Label = BlockLabel(kinds[i]), Value = N(counts[i]), Color = counts[i] > 0 ? Body : Off });
                else more.Append(more.Length > 0 ? ", " : "").Append(BlockLabel(kinds[i])).Append(' ').Append(counts[i]);
            }
            e.About = L("scUiFacWorkshopAbout") + (more.Length > 0 ? "\n" + more : "") + (partStations ? "\n" + L("scUiFacSitesPartAbout") : "");
            e.Actions.Add(new FacilityAction
            {
                Label = L("scUiWorkCall"),
                Ready = WorkWhyNot() == null,
                WhyNot = WorkWhyNot,
                PickHint = L("scUiFacPickWork"),
                Pick = CallToWork,
            });
            into.Add(e);
        }

        /// <summary>
        /// Call to work can't start now: a client, shifts off, or the player outside the base (every pick would say so) - unless
        /// someone is on a work shift: picking her ends it from anywhere, as the Members tab does (ScWork.CallOrEnd; the
        /// re-check's N2), and ScWork.CanStart still refuses a new shift away from the base, member by member.
        /// </summary>
        private static string WorkWhyNot()
        {
            if (!ScCommunity.IsHost) return L("scHostOnly");
            if (!ScConfig.WorkShiftsEnabled) return L("scWorkOff");
            if (!PlayerAtHome() && !AnyOnWorkShift()) return L("scWorkNotHome");
            return null;
        }

        /// <summary>The Members tab's Call to work for the resident picked (it ends her shift when she is already on one, as there).</summary>
        private static bool CallToWork(Member m, EntityPlayerLocal player, out string message)
        {
            if (HostOnly(out message)) return false;
            bool ok;
            ScWork.CallOrEnd(m, player, out message, out ok);
            if (string.IsNullOrEmpty(message)) message = L("scInternal");
            return ok;
        }

        // ------------------------------------------------------------------ the campfire, host of the round of drinks ----

        private static void AddCampfire(List<FacilityEntry> into)
        {
            Community c = ScRegistry.Community;
            int fires = CountSites(IsCampfireName);
            bool partStations, partWater;
            MissingSites(out partStations, out partWater);
            var e = new FacilityEntry
            {
                Key = Campfire,
                Name = L("scUiFacCampfire"),
                Icon = "ui_game_symbol_campfire",
                Status = DrinksStatus(),           // the round of drinks: what can be done here (the count is a fact)
                StatusColor = DrinksStatusColor(),
                Sub = L("scUiFacCampfireSub"),
            };
            // round fd1 (review): the window has four fact rows (FacilityFactSlots) - the count and each activity's state;
            // what each costs and gives is in the About text below, with this base's numbers
            e.Fact("scUiFacCampfiresCount", partStations ? Fmt("scUiFacCountPart", fires) : N(fires), partStations ? Warn : fires > 0 ? Body : Off);
            e.Fact("scUiFacDrinks", DrinksStatus(), DrinksStatusColor());
            e.Fact("scUiFacFeast", FeastStatus(), FeastStatusColor());
            e.Fact("scUiFacRation", RationStatus(), RationStatusColor());
            Member cook;
            int cookLevel = Math.Max(1, ScSkills.BestSpecialist(Specialty.Cooking, out cook));   // the best cook's, or a level-1 cook's
            var about = new StringBuilder();
            // the activities this facility hosts (ScActivities.All's fifth column): one line each and their actions
            foreach (string[] a in ScActivities.All)
            {
                if (a.Length < 5 || a[4] != Campfire) continue;
                if (a[0] == ScActivities.Drinks)
                    about.Append(Fmt("scUiFacAboutDrinks", ScConfig.DrinksLuxury, ScConfig.DrinksFood, ScConfig.DrinksMorale + ScLifeSkills.DrinksBonus(),
                                     ScActivities.FormatMinutes(ScConfig.DrinksMinutes)));          // ms/m1all: with Entertainment
                else if (a[0] == ScActivities.Feast)
                    about.Append(Fmt("scUiFacAboutFeast", ScConfig.FeastFood, ScActivities.FeastMoraleFor(cookLevel), ScActivities.FeastStaminaFor(cookLevel),
                                     ScActivities.FormatMinutes(ScConfig.FeastMinutes)));
                else if (a[0] == ScActivities.Ration)
                    about.Append(Fmt("scUiFacAboutRation", Mathf.RoundToInt(ScConfig.RationShare * 100f), ScConfig.RationMorale, ScConfig.RationStamina,
                                     ScActivities.FormatMinutes(ScConfig.RationMinutes)));
                else
                    about.Append(L(a[2]));
                about.Append('\n');
                if (a[0] == ScActivities.Drinks)
                    e.Actions.Add(new FacilityAction { Label = L("scUiActDrinksDo"), Ready = ScActivities.WhyNotDrinks() == null, Run = PourDrinks });
                else if (a[0] == ScActivities.Feast)          // round fd1
                    e.Actions.Add(new FacilityAction { Label = L("scUiActFeastDo"), Ready = ScActivities.WhyNotFeast() == null, Run = HoldFeast });
                else if (a[0] == ScActivities.Ration)
                    e.Actions.Add(new FacilityAction { Label = L("scUiActRationDo"), Ready = ScActivities.WhyNotRation() == null, Run = Ration });
            }
            about.Append(L(ScConfig.CampfireGathering ? "scUiFacCampfireAbout" : "scUiFacCampfireAboutOff"));
            e.About = about.ToString();
            into.Add(e);
        }

        private static bool PourDrinks(EntityPlayerLocal player, out string message)
        {
            bool ok = ScActivities.PourDrinks(player, out message);
            if (ok && player != null) ScCommunity.Tell(player, message, "");
            return ok;
        }

        private static bool HoldFeast(EntityPlayerLocal player, out string message)
        {
            bool ok = ScActivities.PrepareFeast(player, out message);
            if (ok && player != null) ScCommunity.Tell(player, message, "");
            return ok;
        }

        private static bool Ration(EntityPlayerLocal player, out string message)
        {
            bool ok = ScActivities.EnactRationing(player, out message);
            if (ok && player != null) ScCommunity.Tell(player, message, "");
            return ok;
        }

        /// <summary>Round fd1: the feast's line - running, held today, ready, or the first thing it lacks.</summary>
        private static string FeastStatus()
        {
            if (ScActivities.FeastActive) return Fmt("scUiActRunning", ScActivities.FeastTimeLeft());
            if (ScRegistry.Community.FeastDay == ScCommunity.Today) return L("scUiActFeastDoneToday");
            if (ScActivities.WhyNotFeast() == null) return L("scUiActReady");
            // name what is missing only when the rest is in place (host, a home, members): else it would blame the cook
            if (ScCommunity.IsHost && ScRegistry.Community.HasHome && ScRegistry.Members.Count > 0)
            {
                Member cook;
                if (ScSkills.BestSpecialist(Specialty.Cooking, out cook) <= 0) return L("scUiActFeastNeedsCook");
                if (CampfireCount() <= 0) return L("scUiActFeastNeedsStation");
                if (ScRegistry.Community.Get(Resource.Food) < ScConfig.FeastFood) return L("scUiActFeastNeedsFood");
            }
            return L("scUiActNotReadyPlain");
        }

        private static string FeastStatusColor()
        {
            if (ScActivities.FeastActive) return Good;
            if (ScRegistry.Community.FeastDay == ScCommunity.Today) return Dim;
            return ScActivities.WhyNotFeast() == null ? Good : Bad;
        }

        /// <summary>Round fd1: rationing's line - its penalty running, enacted today, or ready.</summary>
        private static string RationStatus()
        {
            if (ScRegistry.Community.RationDay == ScCommunity.Today)
                return ScActivities.RationActive ? Fmt("scUiActRationRunning", ScActivities.RationTimeLeft()) : L("scUiActRationDoneToday");
            if (ScActivities.WhyNotRation() == null) return L("scUiActReady");
            if (ScCommunity.IsHost && ScRegistry.Community.HasHome && ScRegistry.Members.Count > 0 && ScActivities.RationSavesNothing())
                return L("scUiActRationNothing");
            return L("scUiActNotReadyPlain");
        }

        private static string RationStatusColor()
        {
            if (ScRegistry.Community.RationDay == ScCommunity.Today) return ScActivities.RationActive ? Warn : Dim;
            return ScActivities.WhyNotRation() == null ? Good : Bad;
        }

        /// <summary>"Running - 12:34 left" / "Poured today - again tomorrow" / "Ready" / "Not enough luxuries or food".</summary>
        private static string DrinksStatus()
        {
            if (ScActivities.DrinksActive) return Fmt("scUiActRunning", ScActivities.TimeLeft());
            if (ScRegistry.Community.DrinksDay == ScCommunity.Today) return L("scUiActDoneToday");
            return L(ScActivities.WhyNotDrinks() == null ? "scUiActReady" : "scUiActNotReady");
        }

        private static string DrinksStatusColor()
        {
            if (ScActivities.DrinksActive) return Good;
            if (ScRegistry.Community.DrinksDay == ScCommunity.Today) return Dim;
            return ScActivities.WhyNotDrinks() == null ? Good : Bad;
        }

        // ------------------------------------------------------------------ the radio station ----

        private static void AddRadio(List<FacilityEntry> into)
        {
            Community c = ScRegistry.Community;
            int radios = 0;
            foreach (Vector3i p in c.Radios) if (ScZone.Contains(p)) radios++;
            bool on = ScOutposts.RadioOnlineAnywhere();
            World w = ScCommunity.World;
            var e = new FacilityEntry
            {
                Key = Radio,
                Name = L("scUiFacRadio"),
                Icon = "ui_game_symbol_talk",
                Status = L(radios == 0 ? "scUiFacRadioNone" : on ? "scUiFacRadioOn" : "scUiFacRadioOff"),
                StatusColor = radios == 0 ? Off : on ? Good : Bad,
                Sub = L(radios == 0 ? "scUiFacRadioSubNone" : on ? "scUiFacRadioSubOn" : "scUiFacRadioSubOff"),
            };
            e.Fact("scUiFacRadioCount", N(radios), radios > 0 ? Body : Off);
            e.Fact("scUiFacRadioPower", L(on ? "scUiFacYes" : "scUiFacNo"), on ? Good : radios > 0 ? Bad : Off);
            e.Fact("scUiFacRadioRelays", N(c.Relays.Count));
            if (w != null)
                e.Fact("scUiFacRadioDrop", c.RadioNextDrop <= w.worldTime ? L("scUiActReady")
                       : Fmt("scUiFacHours", Mathf.CeilToInt((c.RadioNextDrop - w.worldTime) / 1000f)), c.RadioNextDrop <= w.worldTime ? Good : Body);
            e.About = L("scUiFacRadioAbout");
            into.Add(e);
        }

        // ------------------------------------------------------------------ watch posts ----

        private static void AddWatch(List<FacilityEntry> into)
        {
            Community c = ScRegistry.Community;
            int posts = c.WatchPosts.Count, manned = 0;
            Vector3i firstEmpty = Vector3i.zero;
            var lines = new StringBuilder();
            foreach (Vector3i p in c.WatchPosts)
            {
                Member g = ScWatch.GuardOf(p);
                if (g != null) manned++;
                else if (firstEmpty == Vector3i.zero) firstEmpty = p;
                if (lines.Length > 0) lines.Append('\n');
                lines.Append(g == null ? Fmt("scUiFacPostEmpty", ScWatch.Label(p))
                                       : Fmt("scUiFacPostManned", ScWatch.Label(p), g.Name, ScWatch.HoursLeft(g)) + ScWatchFatigue.Suffix(g));
            }
            var resting = new List<string>();
            World w = ScCommunity.World;
            foreach (Member m in ScRegistry.Members.Values)
                if (w != null && m.WatchRestUntil > w.worldTime)
                    resting.Add(m.Name + " " + Fmt("scUiFacHours", Mathf.CeilToInt((m.WatchRestUntil - w.worldTime) / 1000f)));
            float cut = 1f - ScWatch.AttentionFactor();
            var e = new FacilityEntry
            {
                Key = Watch,
                Name = L("scUiFacWatch"),
                Icon = "ui_game_symbol_perception_mastery",
                Status = posts == 0 ? L("scUiFacWatchNone") : Fmt("scUiFacWatchRow", manned, posts),
                StatusColor = posts == 0 ? Off : manned > 0 ? Good : Warn,
                Sub = ScConfig.WatchPosts ? Fmt("scUiFacWatchSub", ScConfig.WatchShiftHours, ScConfig.WatchShiftAmmo) : L("scUiFacOff"),
            };
            e.Fact("scUiFacWatchPosts", Fmt("scUiFacOfCap", posts, ScConfig.WatchMaxPosts));
            e.Fact("scUiFacWatchManned", N(manned), manned > 0 ? Good : Body);
            e.Fact("scUiFacWatchCut", cut > 0.005f ? "-" + Mathf.RoundToInt(cut * 100f) + "%" : "0%");
            e.Fact("scUiFacWatchResting", resting.Count > 0 ? string.Join(", ", resting.ToArray()) : L("scUiFacNobody"));
            var about = new StringBuilder();
            if (lines.Length > 0) about.Append(lines).Append('\n');
            about.Append(L("scUiFacWatchAbout"));
            if (ScConfig.WatchFatigue) about.Append('\n').Append(ScWatchFatigue.AboutText());
            e.About = about.ToString();
            Vector3i post = firstEmpty;
            // why the pick can't open (the Fable review's finding 6): every resident picked would be refused the same way
            Func<string> whyNot = () =>
            {
                if (!ScCommunity.IsHost) return L("scHostOnly");
                if (!ScConfig.WatchPosts) return L("scWatchOff");
                if (post == Vector3i.zero) return L(posts == 0 ? "scUiFacWatchNoPost" : "scUiFacWatchAllManned");
                if (ScRegistry.Community.Get(Resource.Ammo) < Math.Max(0, ScConfig.WatchShiftAmmo)) return ScWatch.ErrorText("scWatchNoAmmo", null, post);
                return null;
            };
            var assign = new FacilityAction
            {
                Label = L("scUiFacAssignGuard"),
                Ready = whyNot() == null,
                WhyNot = whyNot,
                PickHint = post != Vector3i.zero ? Fmt("scUiWatchPick", ScWatch.Label(post)) : "",
                Pick = (Member m, EntityPlayerLocal p, out string msg) => AssignGuard(post, m, p, out msg),
            };
            e.Actions.Add(assign);
            into.Add(e);
        }

        /// <summary>The post's own Assign guard for the resident picked (ScWatch tells the player too).</summary>
        private static bool AssignGuard(Vector3i post, Member m, EntityPlayerLocal player, out string message)
        {
            if (HostOnly(out message)) return false;
            string err;
            if (!ScWatch.Assign(post, m, player, out err)) { message = ScWatch.ErrorText(err, m, post); return false; }
            message = Fmt("scWatchAssigned", m.Name, ScConfig.WatchShiftHours, ScConfig.WatchShiftAmmo, ScWatch.Label(post));
            return true;
        }

        // ------------------------------------------------------------------ the Guard Tower ----

        /// <summary>ScTower.ExpandNow: a tower went up with its Watch post at post (host).</summary>
        public static void NoteTower(Vector3i post)
        {
            try
            {
                if (!ScCommunity.IsHost || !ScRegistry.Loaded) return;
                List<Vector3i> towers = ScRegistry.Community.Towers;
                if (towers.Contains(post)) return;
                if (towers.Count >= 32) towers.RemoveAt(0);
                towers.Add(post);
                ScRegistry.SaveAtomic();
            }
            catch (Exception e) { ScLog.Once("facility-tower-note", e); }
        }

        private static void AddTower(List<FacilityEntry> into)
        {
            Community c = ScRegistry.Community;
            World w = ScCommunity.World;
            // a tower whose post is loaded and is no Watch post any more was taken down (host: the list is saved with the registry)
            if (ScCommunity.IsHost && w != null && c.Towers.Count > 0)
            {
                int before = c.Towers.Count;
                c.Towers.RemoveAll(p => ScZone.IsLoaded(p) && !(w.GetBlock(p).Block is BlockScWatchPost));
                if (c.Towers.Count != before) ScRegistry.SaveAtomic();
            }
            int built = 0, manned = 0;
            foreach (Vector3i p in c.Towers)
            {
                if (!ScZone.Contains(p)) continue;
                built++;
                if (ScWatch.GuardOf(p) != null) manned++;
            }
            bool room = c.WatchPosts.Count < Math.Max(0, ScConfig.WatchMaxPosts);
            var e = new FacilityEntry
            {
                Key = Tower,
                Name = L("scUiFacTower"),
                Icon = "ui_game_symbol_map_fortress",
                Status = built == 0 ? L("scUiFacTowerNone") : built == 1 ? L("scUiFacTowerOne") : Fmt("scUiFacTowers", built),
                StatusColor = built > 0 ? Body : Off,
                Sub = L(room ? "scUiFacTowerRoom" : "scUiFacTowerNoRoom"),
            };
            e.Fact("scUiFacTowerBuilt", N(built), built > 0 ? Body : Off);
            e.Fact("scUiFacWatchManned", N(manned), manned > 0 ? Good : Body);
            e.Fact("scUiFacTowerAnother", Fmt(room ? "scUiFacYesOfCap" : "scUiFacNoOfCap", c.WatchPosts.Count, ScConfig.WatchMaxPosts), room ? Good : Warn);
            e.About = L("scUiFacTowerAbout");
            into.Add(e);
        }

        // ------------------------------------------------------------------ save keys (ScRegistry delegates them here) ----

        /// <summary>ScRegistry.ParseCommunity: scansites=, lightsout=, lastattack=, towers= (an older build ignores all four).</summary>
        public static void ParseKey(Community c, string key, string val)
        {
            switch (key)
            {
                case "scansites":
                    // name@x,y,z;... grouped again by chunk
                    c.ScanSites.Clear();
                    foreach (string part in (val ?? "").Split(';'))
                    {
                        int at = part.LastIndexOf('@');
                        Vector3i v;
                        if (at <= 0 || !TryVec(part.Substring(at + 1), out v)) continue;
                        long k = WorldChunkCache.MakeChunkKey(World.toChunkXZ(v.x), World.toChunkXZ(v.z));
                        List<KeyValuePair<Vector3i, string>> l;
                        if (!c.ScanSites.TryGetValue(k, out l)) c.ScanSites[k] = l = new List<KeyValuePair<Vector3i, string>>();
                        l.Add(new KeyValuePair<Vector3i, string>(v, part.Substring(0, at)));
                    }
                    break;
                case "lightsout":
                {
                    // until:x,y,z@id|x,y,z@id:hx,hy,hz - the generators it switched off with their block ids, and the home it was
                    // ordered for; the first form (until:x,y,z|x,y,z, no ids, no home) still reads: id 0 skips the id test, and
                    // the ticker adopts the home it finds
                    string[] f = (val ?? "").Split(':');
                    ulong u;
                    if (!ulong.TryParse(f[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out u)) break;
                    c.LightsOutUntil = u;
                    c.LightsOutGens.Clear();
                    if (f.Length > 1)
                    {
                        foreach (string part in f[1].Split('|'))
                        {
                            string[] g = part.Split('@');
                            Vector3i v;
                            if (!TryVec(g[0], out v) || c.LightsOutGens.FindIndex(kv => kv.Key == v) >= 0) continue;
                            c.LightsOutGens.Add(new KeyValuePair<Vector3i, int>(v, g.Length > 1 ? Math.Max(0, Int(g[1])) : 0));
                        }
                    }
                    Vector3i home;
                    c.LightsOutHome = f.Length > 2 && TryVec(f[2], out home) ? home : Vector3i.zero;
                    break;
                }
                case "lastattack":
                {
                    // day:kind:count:salvage
                    string[] f = (val ?? "").Split(':');
                    if (f.Length < 4) break;
                    c.LastAttackDay = Int(f[0]);
                    c.LastAttackKind = Math.Max(0, Int(f[1]));
                    c.LastAttackCount = Math.Max(0, Int(f[2]));
                    c.LastAttackSalvage = Math.Max(0, Int(f[3]));
                    break;
                }
                case "towers":
                    c.Towers.Clear();
                    foreach (string part in (val ?? "").Split(';')) { Vector3i v; if (TryVec(part, out v) && !c.Towers.Contains(v)) c.Towers.Add(v); }
                    break;
            }
        }

        private static readonly char[] SiteNameBreakers = { '@', ';', '=', '\n', '\r' };

        /// <summary>ScRegistry's writer: the four keys, each only when it has something.</summary>
        public static void WriteKeys(Community c, StringBuilder sb)
        {
            if (c.ScanSites.Count > 0)
            {
                var ss = new StringBuilder();
                foreach (List<KeyValuePair<Vector3i, string>> l in c.ScanSites.Values)
                    foreach (KeyValuePair<Vector3i, string> s in l)
                    {
                        string n = s.Value ?? "";
                        if (n.Length == 0 || n.IndexOfAny(SiteNameBreakers) >= 0) continue;
                        if (ss.Length > 0) ss.Append(';');
                        ss.Append(n).Append('@').Append(Vec(s.Key));
                    }
                if (ss.Length > 0) sb.AppendLine("scansites=" + ss);
            }
            if (c.LightsOutUntil > 0UL)
            {
                var lo = new StringBuilder(c.LightsOutUntil.ToString(CultureInfo.InvariantCulture)).Append(':');
                for (int i = 0; i < c.LightsOutGens.Count; i++)
                {
                    if (i > 0) lo.Append('|');
                    lo.Append(Vec(c.LightsOutGens[i].Key)).Append('@').Append(c.LightsOutGens[i].Value.ToString(CultureInfo.InvariantCulture));
                }
                lo.Append(':').Append(Vec(c.LightsOutHome));
                sb.AppendLine("lightsout=" + lo);
            }
            if (c.LastAttackKind > 0)
                sb.AppendLine("lastattack=" + c.LastAttackDay.ToString(CultureInfo.InvariantCulture) + ":" + c.LastAttackKind + ":" + c.LastAttackCount + ":" + c.LastAttackSalvage);
            if (c.Towers.Count > 0)
            {
                var tw = new StringBuilder();
                foreach (Vector3i v in c.Towers) { if (tw.Length > 0) tw.Append(';'); tw.Append(Vec(v)); }
                sb.AppendLine("towers=" + tw);
            }
        }

        private static string Vec(Vector3i v)
        {
            return v.x.ToString(CultureInfo.InvariantCulture) + "," + v.y.ToString(CultureInfo.InvariantCulture) + "," + v.z.ToString(CultureInfo.InvariantCulture);
        }

        private static bool TryVec(string s, out Vector3i v)
        {
            v = Vector3i.zero;
            string[] p = (s ?? "").Split(',');
            int x, y, z;
            if (p.Length != 3) return false;
            if (!int.TryParse(p[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out x)) return false;
            if (!int.TryParse(p[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out y)) return false;
            if (!int.TryParse(p[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out z)) return false;
            v = new Vector3i(x, y, z);
            return true;
        }

        private static int Int(string s)
        {
            int v;
            return int.TryParse((s ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : 0;
        }

        // ------------------------------------------------------------------ console ----

        /// <summary>sc facility tab: every entry as the tab would show it, with its facts and actions.</summary>
        public static string ConsoleText()
        {
            var sb = new StringBuilder();
            List<FacilityEntry> list = Build();
            sb.Append("Facilities tab: ").Append(list.Count).Append(list.Count == 1 ? " entry" : " entries");
            foreach (FacilityEntry e in list)
            {
                sb.Append('\n').Append(e.Key).Append(": ").Append(e.Name).Append(" - ").Append(e.Status).Append(e.Sub.Length > 0 ? " (" + e.Sub + ")" : "");
                foreach (FacilityFact f in e.Facts) sb.Append("\n    ").Append(f.Label).Append(": ").Append(f.Value);
                foreach (FacilityAction a in e.Actions)
                    sb.Append("\n    [").Append(a.Label).Append(a.Ready ? "" : " (dim)").Append(a.Pick != null ? ", picks a resident" : "").Append(']');
            }
            return sb.ToString();
        }
    }
}
