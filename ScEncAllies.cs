using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// A refill owed for a lost enclave (E6, Q4 a): the request-clock day a new group moves in, and the lost group's POI
    /// (never the new one's) and name (never given again). Saved as [community] encrefill=day:x,y,z:name;..
    /// </summary>
    public sealed class EncRefill
    {
        public int Day;
        public Vector3i Poi;
        public string Name = "";
    }

    /// <summary>
    /// Allies, milestone E6 (E6_SPEC.md; Enclaves_Plan.md 2.11, 2.13 and 6: Q4 a, Q5 a): what an Allied enclave does for the
    /// player, the morale the enclaves bring and cost, the refill of lost enclaves, and allies telling of more.
    ///
    /// Benefits (plan 2.11, State of Decay 2's names): each group has one, fixed and saved (Enclave.Benefit) - by kind
    /// (growers Share of the Harvest, medics Open Pharmacy, militia Munitions Manufacture, builders Construction Partners, a
    /// road crew Filtration Service: EnclaveBenefitBundles units of their resource a day), a scavengers' group one of the
    /// other eight rolled once from the world seed and its POI (Comfort in Numbers, Overflow Barracks, Fight Club, Gun Safety
    /// Club, Jogging Buddies, Scavenging Partners, Training and Advice, Neighborhood Watch); raiders none. It applies while the
    /// group is Allied (not hostile, its building in this world, Enclaves on): Q5 a's ally let down is Friendly again and its
    /// benefit stops until it is Allied once more. Gained or lost: a tell and a ledger line (OnBandChanged).
    ///   Resources   ScDaily.Run (Daily): the units into the stockpile at each rollover, before the day's needs, in the
    ///               day's summary line.
    ///   Morale      ScMorale's parts: Allies (+EnclaveAllyMorale an ally, at most EnclaveAllyMoraleCap), Comfort in Numbers
    ///               (+EnclaveBenefitMorale each, a part of its own past that cap), Enclave lost (OnFallen: -EnclaveLostMorale
    ///               for a Friendly or Allied group lost, any cause; ScMorale.Daily fades it a point a day; Leadership's
    ///               shield shrinks it as the other negative parts).
    ///   Beds        TotalBeds: the scanned beds plus EnclaveBenefitBeds for each Overflow Barracks - everywhere SC weighs beds
    ///               against members (morale, the community window); the scanned count itself is never written.
    ///   XP          XpMult / SpecXpMult (ScSkills): each club adds EnclaveBenefitClubXp - 1 to its skill's multiplier, each
    ///               Training and Advice EnclaveBenefitTrainingXp - 1 to every skill's (the specialty's too), additively, the
    ///               whole capped at EnclaveBenefitXpCap.
    ///   Watch       ScInfestations' filter (WatchZones / WatchedBy): no new infestation - the daily roll, a spread, E5's start
    ///               near an enclave - within EnclaveWatchRange m of such an ally; a spread from one within that range rolls
    ///               EnclaveWatchSpread the chance (SpreadFactor).
    /// Refill (Q4 a; OnFallen, Daily): every lost group (ScEnclaves.Fallen: wiped out, a siege lost - recruiting can never
    /// empty one: E3 keeps EnclaveRecruitMinLeft) owes a new group EnclaveRefillDays later on the request clock; then
    /// ScEnclaves.Refill picks one POI with the late seed's rules, never above the target (owed no more), and with none that
    /// fits it is tried again the next day. A powered radio hears of it ("near the building", the group still unknown).
    /// Allies tell of more (plan 2.13): on becoming Allied, then every EnclaveAllyRevealDays while Allied (request-clock days,
    /// Enclave.AllyRevealDay), an ally makes the enclave nearest to them that the player does not know known (E1's discovery:
    /// the marker, a tell, a ledger line). A standing flipping back and forth tells no more: the reveal on becoming Allied
    /// waits for the stamp.
    /// The request clock (ScEncRequests.Now) carries the refill and reveal days, so "sc day" moves them as it moves requests;
    /// a world day set back shifts them (ShiftDays, from ScEnclaves.OnWorldDayBack). Host only, main thread.
    /// </summary>
    public static class ScEncAllies
    {
        public const string Harvest = "harvest", Pharmacy = "pharmacy", Munitions = "munitions", Construction = "construction", Filtration = "filtration";
        public const string Comfort = "comfort", Barracks = "barracks", FightClub = "fightclub", GunClub = "gunclub", Jogging = "jogging",
                            Scavenging = "scavenging", Training = "training", Watch = "watch";
        /// <summary>Every benefit this build knows (a saved one it does not know is set again at the next world start).</summary>
        public static readonly string[] Benefits = { Harvest, Pharmacy, Munitions, Construction, Filtration, Comfort, Barracks, FightClub, GunClub, Jogging,
                                                     Scavenging, Training, Watch };
        /// <summary>What a scavengers' (general) group rolls from.</summary>
        public static readonly string[] GeneralBenefits = { Comfort, Barracks, FightClub, GunClub, Jogging, Scavenging, Training, Watch };
        /// <summary>The most the Enclave lost part holds (the registry clamps a saved one to it).</summary>
        public const float MaxLoss = 30f;
        private const string Salt = "sc.encbenefit.v1", RefillSalt = "sc.encrefill.v1";
        /// <summary>
        /// The frame an ally last told of another in the daily pass: a catch-up of missed days (each day its own pass, all in
        /// one frame) tells one at most - the others stay due and tell on the next real day (the E6 review's L3; E1's radio
        /// does the same with radioFrame).
        /// </summary>
        private static int revealFrame = -1;

        private static string Loc(string key) { return Localization.Get(key); }

        private static string F(float f) { return f.ToString("0.##", CultureInfo.InvariantCulture); }

        // ------------------------------------------------------------------ benefits ----

        /// <summary>The benefit a kind of group always has (null: a scavengers' group rolls one).</summary>
        public static string ForKind(string kind)
        {
            switch (kind)
            {
                case "growers": return Harvest;
                case "medics": return Pharmacy;
                case "militia": return Munitions;
                case "builders": return Construction;
                case "roadcrew": return Filtration;
                default: return null;
            }
        }

        /// <summary>The stockpile resource a benefit brings each day, or -1.</summary>
        public static int ResourceOf(string benefit)
        {
            switch (benefit)
            {
                case Harvest: return (int)Resource.Food;
                case Pharmacy: return (int)Resource.Meds;
                case Munitions: return (int)Resource.Ammo;
                case Construction: return (int)Resource.Materials;
                case Filtration: return (int)Resource.Fuel;
                default: return -1;
            }
        }

        /// <summary>
        /// The group's benefit, set once and saved: by its kind, or (scavengers) one of GeneralBenefits drawn from the world seed
        /// and its POI's (x, z) - the same save always rolls the same. Raiders have none. A saved one this build does not know is
        /// set again. True when it changed.
        /// </summary>
        public static bool EnsureBenefit(Enclave e, World w)
        {
            if (e == null) return false;
            if (e.Hostile)
            {
                if (e.Benefit.Length == 0) return false;
                e.Benefit = "";
                return true;
            }
            if (Array.IndexOf(Benefits, e.Benefit) >= 0) return false;
            string b = ForKind(e.Kind);
            if (b == null)
            {
                int seed = (w != null ? w.Seed : 0) ^ Salt.GetStableHashCode() ^ unchecked(e.Poi.x * 73856093) ^ unchecked(e.Poi.z * 19349663);
                GameRandom rnd = GameRandomManager.Instance.CreateGameRandom(seed);
                try { b = GeneralBenefits[rnd.RandomRange(0, GeneralBenefits.Length)]; }
                finally { GameRandomManager.Instance.FreeGameRandom(rnd); }
            }
            e.Benefit = b;
            return true;
        }

        /// <summary>ScEnclaves.OnGameStart (and a reseed): every group's benefit, set where it is missing. One log line when any was.</summary>
        public static void EnsureBenefits(World w)
        {
            int n = 0;
            foreach (Enclave e in ScEnclaves.All) if (EnsureBenefit(e, w)) n++;
            if (n > 0) ScLog.Out("enclave benefits: " + n + " set (" + Census() + ")");
        }

        /// <summary>"harvest 2, watch 1, raiders 3": how many groups have each benefit.</summary>
        private static string Census()
        {
            var counts = new Dictionary<string, int>();
            foreach (Enclave e in ScEnclaves.All)
            {
                string k = e.Benefit.Length > 0 ? e.Benefit : e.Hostile ? "raiders" : "none";
                int n;
                counts.TryGetValue(k, out n);
                counts[k] = n + 1;
            }
            var parts = new List<string>();
            foreach (var kv in counts) parts.Add(kv.Key + " " + kv.Value);
            parts.Sort(StringComparer.Ordinal);
            return string.Join(", ", parts.ToArray());
        }

        /// <summary>"Share of the Harvest" ("" for none).</summary>
        public static string Name(string benefit)
        {
            return string.IsNullOrEmpty(benefit) ? "" : Loc("scEncBenefit_" + benefit);
        }

        /// <summary>"+1 food a day", "your survivors' Fighting XP x2": what the benefit does at the config's numbers.</summary>
        public static string Describe(string benefit)
        {
            switch (benefit)
            {
                case Harvest:
                case Pharmacy:
                case Munitions:
                case Construction:
                case Filtration:
                    return string.Format(Loc("scEncBenefitDesc_" + benefit), ScConfig.EnclaveBenefitBundles);
                case Comfort: return string.Format(Loc("scEncBenefitDesc_comfort"), ScConfig.EnclaveBenefitMorale);
                case Barracks: return string.Format(Loc("scEncBenefitDesc_barracks"), ScConfig.EnclaveBenefitBeds);
                case FightClub:
                case GunClub:
                case Jogging:
                case Scavenging:
                    return string.Format(Loc("scEncBenefitDesc_" + benefit), F(ScConfig.EnclaveBenefitClubXp));
                case Training: return string.Format(Loc("scEncBenefitDesc_training"), F(ScConfig.EnclaveBenefitTrainingXp));
                case Watch: return string.Format(Loc("scEncBenefitDesc_watch"), ScConfig.EnclaveWatchRange, Mathf.RoundToInt(Mathf.Clamp01(ScConfig.EnclaveWatchSpread) * 100f));
                default: return "";
            }
        }

        // ------------------------------------------------------------------ who is an ally (main thread; a short list scan) ----

        /// <summary>Allied (not hostile, its building in this world, Enclaves on): its benefit applies, it counts toward the Allies part.</summary>
        public static bool IsAlly(Enclave e)
        {
            return e != null && ScConfig.Enclaves && !e.Lost && ScEnclaves.StandingKey(e) == "allied";
        }

        /// <summary>An ally with this benefit.</summary>
        public static bool Active(Enclave e, string benefit)
        {
            return IsAlly(e) && e.Benefit == benefit;
        }

        public static int AlliedCount()
        {
            int n = 0;
            List<Enclave> all = ScEnclaves.All;
            for (int i = 0; i < all.Count; i++) if (IsAlly(all[i])) n++;
            return n;
        }

        private static int CountActive(string benefit)
        {
            int n = 0;
            List<Enclave> all = ScEnclaves.All;
            for (int i = 0; i < all.Count; i++) if (Active(all[i], benefit)) n++;
            return n;
        }

        // ------------------------------------------------------------------ morale, beds, XP ----

        /// <summary>The Allies morale part: +EnclaveAllyMorale for each Allied enclave, at most EnclaveAllyMoraleCap.</summary>
        public static int AlliesMorale()
        {
            return Math.Min(Math.Max(0, ScConfig.EnclaveAllyMoraleCap), Math.Max(0, ScConfig.EnclaveAllyMorale) * AlliedCount());
        }

        /// <summary>Comfort in Numbers: +EnclaveBenefitMorale for each such ally - a morale part of its own, past the Allies cap.</summary>
        public static int ComfortMorale()
        {
            return Math.Max(0, ScConfig.EnclaveBenefitMorale) * CountActive(Comfort);
        }

        /// <summary>" (2 allied: Share of the Harvest, Fight Club)": the breakdown's words after the Allies part ("" with none).</summary>
        public static string AlliesDetail()
        {
            var names = new List<string>();
            int n = 0;
            foreach (Enclave e in ScEnclaves.All)
            {
                if (!IsAlly(e)) continue;
                n++;
                if (e.Benefit.Length > 0) names.Add(Name(e.Benefit));
            }
            return n == 0 ? "" : string.Format(Loc("scMoraleAlliesDetail"), n, string.Join(", ", names.ToArray()));
        }

        /// <summary>Overflow Barracks: EnclaveBenefitBeds for each such ally.</summary>
        public static int ExtraBeds()
        {
            return Math.Max(0, ScConfig.EnclaveBenefitBeds) * CountActive(Barracks);
        }

        /// <summary>The beds SC weighs against its members: the home scan's count (Community.Beds, never written here) plus the allies' (ExtraBeds).</summary>
        public static int TotalBeds()
        {
            return ScRegistry.Community.Beds + ExtraBeds();
        }

        private static string ClubFor(Skill s)
        {
            switch (s)
            {
                case Skill.Fighting: return FightClub;
                case Skill.Shooting: return GunClub;
                case Skill.Cardio: return Jogging;
                case Skill.Wits: return Scavenging;
                default: return null;
            }
        }

        /// <summary>
        /// The allies' XP multiplier for a skill (ScSkills.AddXp): each club for that skill adds EnclaveBenefitClubXp - 1, each
        /// Training and Advice EnclaveBenefitTrainingXp - 1 (several stack additively), the whole at most EnclaveBenefitXpCap.
        /// 1 with none.
        /// </summary>
        public static float XpMult(Skill s)
        {
            string club = ClubFor(s);
            float m = 1f;
            List<Enclave> all = ScEnclaves.All;
            for (int i = 0; i < all.Count; i++)
            {
                Enclave e = all[i];
                if (!IsAlly(e)) continue;
                if (club != null && e.Benefit == club) m += Math.Max(0f, ScConfig.EnclaveBenefitClubXp - 1f);
                else if (e.Benefit == Training) m += Math.Max(0f, ScConfig.EnclaveBenefitTrainingXp - 1f);
            }
            return Mathf.Clamp(m, 1f, Math.Max(1f, ScConfig.EnclaveBenefitXpCap));
        }

        /// <summary>The specialty's multiplier (ScSkills.AddSpecialtyXp): Training and Advice only - "every skill" - capped as XpMult.</summary>
        public static float SpecXpMult()
        {
            float m = 1f + CountActive(Training) * Math.Max(0f, ScConfig.EnclaveBenefitTrainingXp - 1f);
            return Mathf.Clamp(m, 1f, Math.Max(1f, ScConfig.EnclaveBenefitXpCap));
        }

        // ------------------------------------------------------------------ Neighborhood Watch (ScInfestations) ----

        /// <summary>The allies keeping a Neighborhood Watch (into: cleared first). ScInfestations.RulesFor reads them once per pass.</summary>
        public static void WatchZones(List<Enclave> into)
        {
            into.Clear();
            if (!ScConfig.Enclaves || ScConfig.EnclaveWatchRange <= 0) return;
            foreach (Enclave e in ScEnclaves.All) if (Active(e, Watch)) into.Add(e);
        }

        /// <summary>The first of these allies whose footprint is within EnclaveWatchRange m (XZ) of the point, or null.</summary>
        public static Enclave WatchedBy(List<Enclave> zones, float x, float z)
        {
            if (zones == null || zones.Count == 0) return null;
            float range = ScConfig.EnclaveWatchRange;
            for (int i = 0; i < zones.Count; i++) if (zones[i].DistanceXZ(x, z) < range) return zones[i];
            return null;
        }

        /// <summary>The spread chance's factor for an infestation: EnclaveWatchSpread within a watch's range of its centre, else 1.</summary>
        public static float SpreadFactor(List<Enclave> zones, Infestation src)
        {
            if (src == null || zones == null || zones.Count == 0) return 1f;
            Vector3 c = src.Centre;
            return WatchedBy(zones, c.x, c.z) != null ? Mathf.Clamp01(ScConfig.EnclaveWatchSpread) : 1f;
        }

        // ------------------------------------------------------------------ the day (ScDaily.Run) ----

        /// <summary>
        /// ScDaily.Run, after the infestations (each rollover, "sc day" too): 1. each ally with a resource benefit adds
        /// EnclaveBenefitBundles units to the stockpile - before the day's needs, in the day's summary line; 2. on the request
        /// clock (ScEncRequests.Now: a day further each "sc day"), the refills due move in; 3. each ally whose next word is due
        /// tells of the enclave nearest to them that the player does not know. ScDaily saves.
        /// </summary>
        public static void Daily(int day, EntityPlayerLocal player, StringBuilder summary)
        {
            try
            {
                if (!ScEnclaves.Ready || !ScConfig.Enclaves || !ScRegistry.Loaded) return;
                Community c = ScRegistry.Community;
                var sent = new int[ScNames.ResourceCount];
                var from = new List<string>();
                int units = Math.Max(0, ScConfig.EnclaveBenefitBundles);
                foreach (Enclave e in ScEnclaves.All)
                {
                    int res = ResourceOf(e.Benefit);
                    if (res < 0 || units <= 0 || !IsAlly(e)) continue;
                    sent[res] += units;
                    from.Add(e.Name);
                }
                if (from.Count > 0)
                {
                    for (int r = 0; r < ScNames.ResourceCount; r++) if (sent[r] > 0) c.Add((Resource)r, sent[r]);
                    if (summary != null)
                    {
                        if (summary.Length > 0) summary.Append(' ');
                        summary.Append(string.Format(Loc("scEncAllyDaily"), ScOutposts.Amounts(sent)));
                    }
                    ScLog.Out("allies, day " + day + ": " + ScOutposts.Amounts(sent) + " into the stockpile from " + string.Join(", ", from.ToArray()));
                }
                int now = ScEncRequests.Now;
                Refills(now, player, false, null);
                foreach (Enclave e in ScEnclaves.All.ToArray())
                {
                    if (Time.frameCount == revealFrame) break;      // one word a frame (L3)
                    if (IsAlly(e) && e.AllyRevealDay <= now && RevealFor(e, now, player)) revealFrame = Time.frameCount;
                }
            }
            catch (Exception ex)
            {
                ScLog.Once("encally-daily", ex);
            }
        }

        /// <summary>Where "900 m north-east" is measured from: home, or the player without one.</summary>
        private static bool From(EntityPlayerLocal player, out Vector3 at)
        {
            if (ScZone.HasHome)
            {
                Vector3i h = ScZone.Home;
                at = new Vector3(h.x, h.y, h.z);
                return true;
            }
            if (player != null) { at = player.position; return true; }
            at = Vector3.zero;
            return false;
        }

        /// <summary>
        /// An ally tells the player about the enclave nearest to them that the player does not know (raiders too, not one off the
        /// map, not one that moved off the map this request-clock day - E11's pass runs just before: the E11 review's L4): E1's
        /// discovery (known, its marker - SetKnown saves), a tell and a ledger line, and its next word EnclaveAllyRevealDays
        /// later. With none left to tell of it stays due: the next day's pass asks again. True when it told.
        /// </summary>
        private static bool RevealFor(Enclave ally, int now, EntityPlayerLocal player)
        {
            Vector3 a = ally.Centre;
            Enclave best = null;
            float bestD = float.MaxValue;
            foreach (Enclave e in ScEnclaves.All)
            {
                if (e == ally || e.Known || e.Lost || e.MovedDay == now) continue;
                Vector3 c = e.Centre;
                float dx = c.x - a.x, dz = c.z - a.z, d = dx * dx + dz * dz;
                if (d < bestD) { bestD = d; best = e; }
            }
            if (best == null) return false;
            ally.AllyRevealDay = now + Math.Max(1, ScConfig.EnclaveAllyRevealDays);
            ScEnclaves.SetKnown(best, true, player);
            Vector3 from;
            string where = From(player, out from) ? ScEnclaves.Where(from, best.Centre) : "";
            if (player != null) ScCommunity.Tell(player, string.Format(Loc("scEncAllyReveal"), ally.Name, best.Name, best.PoiName, where), best.IsHostile ? "ui_denied" : "");
            ScOutposts.NoteLedger(string.Format(Loc("scEncAllyLedgerReveal"), ally.Name, best.Name, best.PoiName));
            ScLog.Out("enclave " + best.Id + " '" + best.Name + "' told of by the ally " + ally.Id + " '" + ally.Name + "', " + Mathf.RoundToInt(Mathf.Sqrt(bestD))
                      + " m from them" + (best.IsHostile ? " (hostile)" : "") + "; their next word from day " + ally.AllyRevealDay);
            return true;
        }

        // ------------------------------------------------------------------ band changes, a group lost ----

        /// <summary>
        /// A band change (ScEnclaves.AddStanding, TurnHostile). Becoming Allied turns the benefit on (a tell, a ledger line) and
        /// the ally tells of one more at once - unless it did within the last EnclaveAllyRevealDays (AllyRevealDay ahead): a
        /// standing flipping back and forth tells no more. Leaving Allied (Friendly again - Q5 a - or hostile) turns it off (a
        /// tell, a ledger line). The caller saves.
        /// </summary>
        public static void OnBandChanged(Enclave e, string before, string after)
        {
            try
            {
                if (e == null || before == after || !ScConfig.Enclaves) return;
                bool was = before == "allied", now = after == "allied";
                if (was == now) return;
                EntityPlayerLocal pl = ScCommunity.LocalPlayer;
                bool has = e.Benefit.Length > 0;
                // a group whose building is off the map (Lost) has no benefit that applies either way: no tell or ledger line
                // for it (the E6 review's N4)
                bool shown = has && !e.Lost;
                if (now)
                {
                    if (shown)
                    {
                        if (pl != null) ScCommunity.Tell(pl, string.Format(Loc("scEncAllyGained"), e.Name, Name(e.Benefit), Describe(e.Benefit)), "");
                        ScOutposts.NoteLedger(string.Format(Loc("scEncAllyLedgerGained"), e.Name, Name(e.Benefit)));
                    }
                    int day = ScEncRequests.Now;
                    ScLog.Out("enclave " + e.Id + " '" + e.Name + "' is an ally now - " + (has ? e.Benefit + " on" : "no benefit")
                              + (e.AllyRevealDay > day ? "; their next word of another from day " + e.AllyRevealDay : ""));
                    if (e.AllyRevealDay <= day && !e.Lost) RevealFor(e, day, pl);
                }
                else
                {
                    if (shown)
                    {
                        if (pl != null) ScCommunity.Tell(pl, string.Format(Loc("scEncAllyLost"), e.Name, Name(e.Benefit)), "ui_denied");
                        ScOutposts.NoteLedger(string.Format(Loc("scEncAllyLedgerLost"), e.Name, Name(e.Benefit)));
                    }
                    ScLog.Out("enclave " + e.Id + " '" + e.Name + "' is no ally now (" + after + ")" + (has ? " - " + e.Benefit + " off" : ""));
                }
            }
            catch (Exception ex)
            {
                ScLog.Once("encally-band", ex);
            }
        }

        /// <summary>
        /// ScEnclaves.Fallen, after Fall's save (any cause: wiped out, a siege lost). A Friendly or Allied group lost costs
        /// EnclaveLostMorale (the Enclave lost part, fading a point a day) - an ally's benefit goes with them (a tell; the
        /// ledger line when it had one) - and a new group is owed EnclaveRefillDays later on the request clock (Q4 a). Saves.
        /// </summary>
        public static void OnFallen(Enclave e, string cause)
        {
            try
            {
                if (e == null || !ScRegistry.Loaded) return;
                Community c = ScRegistry.Community;
                string band = ScEnclaves.StandingKey(e);
                int loss = band == "friendly" || band == "allied" ? Math.Max(0, ScConfig.EnclaveLostMorale) : 0;
                if (loss > 0)
                {
                    c.EnclaveLoss = Math.Min(MaxLoss, c.EnclaveLoss + loss);
                    bool benefit = band == "allied" && e.Benefit.Length > 0;
                    EntityPlayerLocal pl = ScCommunity.LocalPlayer;
                    if (pl != null)
                        ScCommunity.Tell(pl, benefit ? string.Format(Loc("scEncAllyFallen"), e.Name, loss, Name(e.Benefit))
                                                     : string.Format(Loc("scEncFriendFallen"), e.Name, loss), "ui_denied");
                    if (benefit) ScOutposts.NoteLedger(string.Format(Loc("scEncAllyLedgerLost"), e.Name, Name(e.Benefit)));
                }
                int day = ScEncRequests.Now + Math.Max(0, ScConfig.EnclaveRefillDays);
                c.EncRefills.Add(new EncRefill { Day = day, Poi = e.Poi, Name = e.Name });
                ScLog.Out("enclave " + e.Id + " '" + e.Name + "' lost (" + cause + ", " + band + ")"
                          + (loss > 0 ? ": morale -" + loss + " (the Enclave lost part now -" + F(c.EnclaveLoss) + ")" : "")
                          + "; a new group moves in from day " + day + " on the request clock (" + c.EncRefills.Count + " refill(s) owed)");
                ScRegistry.SaveAtomic();
            }
            catch (Exception ex)
            {
                ScLog.Once("encally-fallen", ex);
            }
        }

        // ------------------------------------------------------------------ refills ----

        /// <summary>
        /// The refills due by the request clock's day now - every one owed with force ("sc enclave refill now") - each through
        /// MoveIn: moved in or not owed any more, it leaves the list; with no POI that fits it is due again the next day.
        /// Returns how many moved in. The caller saves.
        /// </summary>
        private static int Refills(int now, EntityPlayerLocal player, bool force, List<string> into)
        {
            Community c = ScRegistry.Community;
            if (c.EncRefills.Count == 0) return 0;
            int moved = 0;
            foreach (EncRefill r in c.EncRefills.ToArray())
            {
                if (!force && r.Day > now) continue;
                int got = MoveIn(r, now, player, into);
                if (got > 0) moved++;
                if (got != 0) c.EncRefills.Remove(r);
                else r.Day = now + 1;
            }
            return moved;
        }

        /// <summary>
        /// One refill (ScEnclaves.Refill): 1 a new group moved in - the log, and with a powered radio a tell and a ledger line
        /// ("near the building", the group still unknown); -1 not owed any more (the target count is reached); 0 no POI fits
        /// now (tried again the next day).
        /// </summary>
        private static int MoveIn(EncRefill r, int now, EntityPlayerLocal player, List<string> into)
        {
            Community c = ScRegistry.Community;
            World w = ScCommunity.World;
            int seed = (w != null ? w.Seed : 0) ^ RefillSalt.GetStableHashCode() ^ unchecked(now * 7919) ^ unchecked(c.NextEnclaveId * 486187739);
            bool full;
            string why;
            Enclave en = ScEnclaves.Refill(w, seed, r.Poi, r.Name, out full, out why);
            string lost = r.Name.Length > 0 ? "'" + r.Name + "'" : "the console";
            string line;
            if (en == null)
            {
                line = full ? "refill for " + lost + ": not owed any more - " + why : "refill for " + lost + ": no new group yet - " + why + "; tried again on day " + (now + 1);
                ScLog.Out("enclaves: " + line);
                if (into != null) into.Add(line);
                return full ? -1 : 0;
            }
            line = "refill for " + lost + ": enclave " + en.Id + " '" + en.Name + "' moved into " + en.PoiName + " (" + en.Prefab + ", T" + en.Tier + ") " + en.Poi
                   + (en.Hostile ? ", raiders" : ", " + en.Kind + ", " + en.Benefit) + " - " + why;
            ScLog.Out("enclaves: " + line);
            if (into != null) into.Add(line);
            if (ScRadio.Online())
            {
                Vector3 from;
                string where = From(player, out from) ? ScEnclaves.Where(from, en.Centre) : "";
                if (player != null) ScCommunity.Tell(player, string.Format(Loc("scEncRefillRadio"), en.PoiName, where), "");
                ScOutposts.NoteLedger(string.Format(Loc("scEncRefillLedger"), en.PoiName));
            }
            return 1;
        }

        /// <summary>
        /// E6's R2 (ScEnclaves.OnWorldDayBack): the request clock went back by back days - every refill owed and every ally's
        /// next word keeps its remaining span (never below 0). Returns how many stamps moved.
        /// </summary>
        internal static int ShiftDays(int back)
        {
            if (back <= 0) return 0;
            int n = 0;
            foreach (EncRefill r in ScRegistry.Community.EncRefills) { r.Day = Math.Max(0, r.Day - back); n++; }
            foreach (Enclave e in ScEnclaves.All)
                if (e.AllyRevealDay > 0) { e.AllyRevealDay = Math.Max(0, e.AllyRevealDay - back); n++; }
            return n;
        }

        // ------------------------------------------------------------------ texts, console ----

        /// <summary>
        /// The leader's {benefit} in "Tell me about this place" (ScEncDialog.Fill): their benefit by name - the player's now,
        /// or once allied; "" for raiders, a group turned hostile, or one with no benefit. The name only: the line panel holds
        /// four lines, and the tell on becoming allies, sc enclave info and the README say what it does.
        /// </summary>
        public static string AboutText(Enclave e)
        {
            if (e == null || e.IsHostile || e.Benefit.Length == 0) return "";
            return string.Format(Loc(IsAlly(e) ? "scEncAboutBenefitAllied" : "scEncAboutBenefit"), Name(e.Benefit));
        }

        /// <summary>sc enclave info: the benefit, whether it applies and why not, the next word of another.</summary>
        public static string InfoLine(Enclave e)
        {
            if (e.Hostile) return "benefit: none (raiders)";
            if (e.Benefit.Length == 0) return "benefit: not set yet (set at the next world start)";
            string state = IsAlly(e) ? "applies now (Allied)"
                         : e.IsHostile ? "never again (hostile)"
                         : !ScConfig.Enclaves ? "off (Enclaves=false)"
                         : e.Lost ? "not while their building is not in this world"
                         : "not until they are Allied (standing " + e.Standing + " of 70)";
            int now = ScEncRequests.Now;
            return "benefit: " + Name(e.Benefit) + " [" + e.Benefit + "] - " + Describe(e.Benefit) + "; " + state + "; as an ally they tell of another "
                   + (e.AllyRevealDay > now ? "from day " + e.AllyRevealDay : "at once") + " (request clock day " + now + ")";
        }

        /// <summary>sc enclave list: the allies and what they bring, the Enclave lost part, the refills owed.</summary>
        public static string SummaryLine()
        {
            Community c = ScRegistry.Community;
            var names = new List<string>();
            foreach (Enclave e in ScEnclaves.All) if (IsAlly(e)) names.Add(e.Name + (e.Benefit.Length > 0 ? " (" + e.Benefit + ")" : ""));
            int next = int.MaxValue;
            foreach (EncRefill r in c.EncRefills) next = Math.Min(next, r.Day);
            return "allies: " + names.Count + (names.Count > 0 ? " - " + string.Join(", ", names.ToArray()) : "") + "; morale +" + AlliesMorale()
                   + (ComfortMorale() > 0 ? " +" + ComfortMorale() + " comfort" : "") + (c.EnclaveLoss > 0f ? ", enclave lost -" + F(c.EnclaveLoss) : "")
                   + "; beds +" + ExtraBeds() + "; refills owed " + c.EncRefills.Count + (c.EncRefills.Count > 0 ? " (the first from day " + next + ")" : "");
        }

        /// <summary>
        /// sc enclave refill [now]: the refills owed, the request clock and the target; "now" moves every one owed in at once
        /// (with none owed, one new group anyway - still never above the target count). Saves after "now".
        /// </summary>
        public static void RefillCommand(List<string> p, List<string> into)
        {
            Community c = ScRegistry.Community;
            int now = ScEncRequests.Now;
            bool run = p.Count > 2 && p[2].Equals("now", StringComparison.OrdinalIgnoreCase);
            if (!run)
            {
                into.Add(c.EncRefills.Count + " refill(s) owed; request clock day " + now + "; " + ScEnclaves.All.Count + " enclave(s), target " + ScEnclaves.TargetNow()
                         + "; a new group comes " + ScConfig.EnclaveRefillDays + " day(s) after a loss");
                foreach (EncRefill r in c.EncRefills)
                    into.Add("  for '" + r.Name + "' (their building " + r.Poi + "): from day " + r.Day + (r.Day <= now ? " - due" : ""));
                return;
            }
            if (!ScConfig.Enclaves || !ScEnclaves.Ready) { into.Add("enclaves are off in the config, or the world is still starting"); return; }
            EntityPlayerLocal pl = ScCommunity.LocalPlayer;
            if (c.EncRefills.Count == 0)
            {
                // none owed: one anyway (testing), never queued - the sentinel POI excludes nothing
                if (MoveIn(new EncRefill { Day = now, Poi = new Vector3i(int.MinValue, int.MinValue, int.MinValue) }, now, pl, into) > 0) ScRegistry.SaveAtomic();
                return;
            }
            int moved = Refills(now, pl, true, into);
            into.Add(moved + " moved in; " + c.EncRefills.Count + " still owed");
            ScRegistry.SaveAtomic();
        }
    }
}
