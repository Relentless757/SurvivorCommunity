using System;
using System.Collections.Generic;
using System.Text;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round ie1 (2026-11-19, research/balance/INTEGER_ECONOMY.md section 7, the user's decisions): the whole-number economy.
    /// Every daily change to the stockpile is a whole number worked out from whole numbers, with no hidden fraction carried to
    /// the next day and no debt: a day the stockpile cannot pay is a short day (hungry, thirsty, run-down, on foot), as hunger
    /// always was. Modifiers are steps (+1 / -1), thresholds or "per N" with the remainder dropped.
    ///
    /// One function per flow, and every place that shows or settles a flow calls it - the day rollover (ScStockpile,
    /// ScOutposts.Daily), the windows (XUiC_ScCommunity, ScFacilityTab), the Home Status panel (ScStockpile.NeedForRollover),
    /// the console (sc status, sc outpost yield, sc poi) and the morale estimate (ScMorale) - so a display can no longer
    /// disagree with the rollover. Before this round the same formula was written out five or six times.
    ///   Needs     FoodFor / FoodEaten (the cook's saving inside it) / Harvest / WaterDrunk / WaterShortfall
    ///   Outposts  OwnUnits (one outpost's steps) / NetworkYields (duplicates, run-down, on foot, the split) / Upkeep / Haulage
    /// Supply runs keep their one function, ScRuns.Yield (whole steps since this round too). Pure reads: nothing here changes
    /// the registry. Host only.
    /// </summary>
    public static class ScEconomy
    {
        // ------------------------------------------------------------------ food and water ----

        /// <summary>
        /// Does this member eat and drink from the stockpile? Everyone but a member still on the way to the base (E12, a whole
        /// enclave's people, MemberStatus.Arriving): they start once they arrive. Posted staff and runners eat as residents do.
        /// </summary>
        public static bool Eats(Member m) { return m != null && m.Status != MemberStatus.Arriving; }

        /// <summary>The members who eat and drink today (Eats).</summary>
        public static int Eaters()
        {
            int n = 0;
            foreach (Member m in ScRegistry.Members.Values) if (Eats(m)) n++;
            return n;
        }

        /// <summary>Food one member eats a day: MemberFoodPerDay, or a trait's eats= (Bottomless Stomach 2); 0 for one still on the way.</summary>
        public static int FoodFor(Member m) { return Eats(m) ? ScTraits.FoodUnits(m) : 0; }

        /// <summary>
        /// The food the next rollover takes from the stockpile: what every eater eats (FoodFor), less the best cook's saving.
        /// The cook at home saves ceil(stars / CookStarsPerFood) a day (1, 1, 2, 2, 3, 3, 4 for 1-7 stars at 2), never more
        /// than half of the day's food (rounded down). raw = before the cook; saved = the cook's part; cook = who (null: none).
        /// Round fd1 (review): an ordinary day, never rationed - the projections (days of food and the Food morale term, the
        /// window's days left, the outposts' shortest supply, the stockpile need) must not count a one-day ration, or
        /// enacting rationing would double the days of food and win back most of its own morale penalty.
        /// </summary>
        public static int FoodEaten(out int raw, out int saved, out Member cook)
        {
            int cut;
            return FoodEaten(0, out raw, out saved, out cook, out cut);
        }

        /// <summary>
        /// The food the rollover into rolloverDay takes (0: an ordinary day, the overload above). Round fd1: when rationing
        /// was enacted on the day that rollover closes (Rationed), it takes RationShare of it, rounded up; cut = what that
        /// spared. Only the rollover itself and the lines about tonight (sc status, sc ration, Enact rationing) pass a day.
        /// </summary>
        public static int FoodEaten(int rolloverDay, out int raw, out int saved, out Member cook, out int cut)
        {
            raw = 0;
            foreach (Member m in ScRegistry.Members.Values) raw += FoodFor(m);
            int stars = ScSkills.BestSpecialist(Specialty.Cooking, out cook);
            saved = 0;
            if (stars > 0 && ScConfig.CookStarsPerFood > 0) saved = Math.Min((stars + ScConfig.CookStarsPerFood - 1) / ScConfig.CookStarsPerFood, raw / 2);
            int eaten = raw - saved;
            cut = 0;
            if (eaten > 0 && Rationed(rolloverDay))
            {
                // rounded up (7 -> 4); the share goes through decimal so 0.3f is 0.3, not 0.30000001 (100 -> 30, not 31)
                int kept = Math.Max(0, Math.Min(eaten, (int)Math.Ceiling(eaten * (double)(decimal)ScConfig.RationShare - 1e-9)));
                cut = eaten - kept;
                eaten = kept;
            }
            return eaten;
        }

        /// <summary>Round fd1: rationing was enacted on the day the rollover into rolloverDay closes (Community.RationDay).</summary>
        public static bool Rationed(int rolloverDay)
        {
            Community c = ScRegistry.Loaded ? ScRegistry.Community : null;
            return rolloverDay > 0 && c != null && c.RationDay >= 0 && c.RationDay == rolloverDay - 1;
        }

        public static int FoodEaten()
        {
            int raw, saved;
            Member cook;
            return FoodEaten(out raw, out saved, out cook);
        }

        /// <summary>
        /// The garden (round fd1): 1 food per CropsPerFood(out gardener) living crops in the zone (the last scan), the remainder dropped. No
        /// gardener needed; 0 without crops.
        /// </summary>
        public static int Harvest()
        {
            Community c = ScRegistry.Community;
            if (ScRegistry.Members.Count == 0) return 0;     // fd2 review: DailyConsume returns before the garden with nobody home
            Member g;
            int per = CropsPerFood(out g);
            if (per <= 0 || c.Crops <= 0) return 0;
            return c.Crops / per;
        }

        /// <summary>
        /// Round fd1: the crops one food takes today - CropsPerFood, lowered by the best gardener at home (BestSpecialist) to
        /// GardenerCropsPerFood's figure for its level; only that gardener counts. gardener = that one when it lowers it.
        /// 0 = the garden makes nothing.
        /// </summary>
        public static int CropsPerFood(out Member gardener)
        {
            gardener = null;
            int per = ScConfig.CropsPerFood;
            if (per <= 0) return 0;
            Member g;
            int level = ScSkills.BestSpecialist(Specialty.Gardening, out g);
            int[] lv = ScConfig.GardenerCropLevels, cr = ScConfig.GardenerCrops;
            for (int i = 0; g != null && lv != null && cr != null && i < lv.Length && i < cr.Length; i++)
                if (level >= lv[i] && cr[i] > 0 && cr[i] < per) { per = cr[i]; gardener = g; }
            return per;
        }

        /// <summary>Water every eater drinks a day (MemberWaterPerDay each).</summary>
        public static int WaterDrunk() { return Eaters() * ScConfig.MemberWaterPerDay; }

        /// <summary>The day's water beyond what the home's wells and dew collectors make (0 when they cover it).</summary>
        public static int WaterShortfall() { return Math.Max(0, WaterDrunk() - Math.Max(0, ScRegistry.Community.HomeWater)); }

        /// <summary>Whole days a stock lasts at a day's use (99 with no use): what the morale terms and the windows show.</summary>
        public static int DaysOf(int stock, int perDay) { return perDay > 0 ? Math.Max(0, stock) / perDay : 99; }

        // ------------------------------------------------------------------ outposts: one outpost's steps ----

        /// <summary>A day of an outpost's level: OutpostLevelYield (1, 2, 3); a level past the list takes its last.</summary>
        public static int LevelYield(int level)
        {
            int[] t = ScConfig.OutpostLevelYield;
            if (t == null || t.Length == 0) return 0;
            return t[Math.Max(0, Math.Min(t.Length - 1, level - 1))];
        }

        /// <summary>The level's yield; a scavenge outpost (no row matched) makes one less, at least 1 (1, 1, 2).</summary>
        public static int BaseUnits(Outpost o)
        {
            if (o == null) return 0;
            int v = LevelYield(o.Level);
            return o.Scavenge ? Math.Max(1, v - 1) : v;
        }

        /// <summary>A big site makes more: +OutpostTierBonus from tier OutpostTierBonusFrom (4; 0 = off). Tier still sets eligibility and security.</summary>
        public static int TierBonus(int tier)
        {
            return ScConfig.OutpostTierBonusFrom > 0 && tier >= ScConfig.OutpostTierBonusFrom ? ScConfig.OutpostTierBonus : 0;
        }

        /// <summary>+OutpostStaffBundles for every member posted there (one post, two from level 2).</summary>
        public static int StaffUnits(Outpost o) { return o != null ? ScGarrison.StaffCount(o) * ScConfig.OutpostStaffBundles : 0; }

        /// <summary>
        /// The staffer whose specialty fits the outpost's resource (any stars; the most stars, then the first posted), or null.
        /// Only one match counts: the window gives it to this one.
        /// </summary>
        public static Member BestMatch(Outpost o, Resource primary)
        {
            Member best = null;
            int bestStars = 0;
            if (o == null) return null;
            foreach (Member m in ScGarrison.Staff(o))
            {
                int s = ScGarrison.MatchStars(m, primary);
                if (s > bestStars) { bestStars = s; best = m; }
            }
            return best;
        }

        /// <summary>+OutpostMatchBonus while a staffer's specialty fits what the outpost makes today.</summary>
        public static int MatchUnits(Outpost o)
        {
            return o != null && BestMatch(o, ScOutposts.PrimaryToday(o)) != null ? ScConfig.OutpostMatchBonus : 0;
        }

        /// <summary>One staffer's part of the yield, as the window shows it: the staff step, and the match step for the best match.</summary>
        public static int StaffShare(Outpost o, Member m)
        {
            if (o == null || m == null) return 0;
            return ScConfig.OutpostStaffBundles + (ReferenceEquals(BestMatch(o, ScOutposts.PrimaryToday(o)), m) ? ScConfig.OutpostMatchBonus : 0);
        }

        /// <summary>
        /// Built up: +OutpostBuiltBonus once the player built OutpostBuiltBeds beds and OutpostBuiltFacilities facilities inside it
        /// (ScScan's count above what the building had); a food outpost may count OutpostBuiltCrops crops in place of the facilities.
        /// </summary>
        public static int BuiltUnits(Outpost o)
        {
            if (o == null) return 0;
            bool beds = o.Invest[0] >= ScConfig.OutpostBuiltBeds;
            bool facilities = o.Invest[1] >= ScConfig.OutpostBuiltFacilities;
            bool crops = !o.Scavenge && o.Primary == Resource.Food && o.Invest[2] >= ScConfig.OutpostBuiltCrops;
            return beds && (facilities || crops) ? ScConfig.OutpostBuiltBonus : 0;
        }

        /// <summary>The output mode: quiet -OutpostQuietLess (the day can reach 0), push +OutpostPushMore; the risk is ScOutpostPerks.ModeRisk.</summary>
        public static int ModeUnits(Outpost o)
        {
            if (o == null) return 0;
            return o.Mode == 1 ? -ScConfig.OutpostQuietLess : (o.Mode == 2 ? ScConfig.OutpostPushMore : 0);
        }

        /// <summary>Supply-line check-in (off unless OutpostCheckInDays &gt; 0): unvisited past that many days.</summary>
        public static bool CheckInLate(Outpost o, int day)
        {
            if (o == null || ScConfig.OutpostCheckInDays <= 0) return false;
            // the review of ie1 (L1): judged on the rollover day asked about, so a projection agrees with that rollover
            return day - Math.Max(o.Day, o.LastVisitDay) - ScConfig.OutpostCheckInDays > 0;
        }

        /// <summary>Depletion (off unless OutpostDepletesAfter &gt; 0): picked clean once it has made that many units in all.</summary>
        public static bool Depleted(Outpost o)
        {
            return o != null && ScConfig.OutpostDepletesAfter > 0 && o.Produced >= ScConfig.OutpostDepletesAfter;
        }

        /// <summary>n less, but never below 1 (a day already at 0 stays 0): the duplicate and check-in steps.</summary>
        private static int LessNotBelowOne(int units, int less) { return Math.Max(Math.Min(units, 1), units - Math.Max(0, less)); }

        /// <summary>
        /// One outpost's own day, before the network steps: the level's yield, +tier, +staff, +match, +built, the mode (never
        /// below 0), then a late check-in (-OutpostCheckInLess, never below 1) and depletion (at most OutpostDepletedYield).
        /// </summary>
        public static int OwnUnits(Outpost o, int day)
        {
            if (o == null) return 0;
            int t = Math.Max(0, BaseUnits(o) + TierBonus(o.Tier) + StaffUnits(o) + MatchUnits(o) + BuiltUnits(o) + ModeUnits(o));
            if (CheckInLate(o, day)) t = LessNotBelowOne(t, ScConfig.OutpostCheckInLess);
            if (Depleted(o)) t = Math.Min(t, ScConfig.OutpostDepletedYield);
            return t;
        }

        /// <summary>"level 2: 2 + tier 1 + staff 1 + match 1 + push 1 = 6" (the console's sc outpost yield).</summary>
        public static string StepsText(Outpost o)
        {
            if (o == null) return "";
            var sb = new StringBuilder();
            sb.Append(o.Scavenge ? "scavenge level " : "level ").Append(o.Level).Append(": ").Append(BaseUnits(o));
            Step(sb, "tier", TierBonus(o.Tier));
            Step(sb, "staff", StaffUnits(o));
            Step(sb, "match", MatchUnits(o));
            Step(sb, "built", BuiltUnits(o));
            Step(sb, o.Mode == 1 ? "quiet" : "push", ModeUnits(o));
            if (CheckInLate(o, ScCommunity.Today + 1)) sb.Append(", check-in late -").Append(ScConfig.OutpostCheckInLess).Append(" (not below 1)");
            if (Depleted(o)) sb.Append(", depleted: at most ").Append(ScConfig.OutpostDepletedYield);
            return sb.Append(" = ").Append(OwnUnits(o, ScCommunity.Today + 1)).ToString();
        }

        private static void Step(StringBuilder sb, string name, int v)
        {
            if (v != 0) sb.Append(v > 0 ? " + " : " - ").Append(name).Append(' ').Append(Math.Abs(v));
        }

        // ------------------------------------------------------------------ outposts: the network ----

        /// <summary>Does it work on that rollover day: Active (not idle, contested or lost) and not damaged.</summary>
        public static bool Produces(Outpost o, int day)
        {
            return o != null && o.State == OutpostState.Active && !ScOutpostRisk.IsDamaged(o, day);
        }

        /// <summary>
        /// What every producing outpost makes on that rollover day, in whole units per resource (outpost id -> units). After its
        /// own day (OwnUnits): every outpost of the same primary resource after the best makes OutpostDupLess less, never below 1
        /// (the best: the most units, then the oldest); while the upkeep went unpaid each makes OutpostRunDownLess less (it can
        /// reach 0); while the haulage went unpaid each makes at most OutpostOnFootMax; then Split. There is no daily cap.
        /// The rollover, ScOutposts.Estimate / ProjectedDelivery, the windows and the console all read it.
        /// </summary>
        public static Dictionary<int, int[]> NetworkYields(int day)
        {
            var producing = new List<Outpost>();
            var own = new Dictionary<int, int>();
            var primary = new Dictionary<int, Resource>();       // read once: a scavenge outpost's is the stock's shortest
            foreach (Outpost o in ScRegistry.Outposts)
            {
                if (!Produces(o, day)) continue;
                producing.Add(o);
                own[o.Id] = OwnUnits(o, day);
                primary[o.Id] = ScOutposts.PrimaryToday(o);
            }
            var units = new Dictionary<int, int>(own);
            for (int r = 0; r < ScNames.ResourceCount; r++)
            {
                var same = new List<Outpost>();
                foreach (Outpost o in producing) if ((int)primary[o.Id] == r) same.Add(o);
                if (same.Count < 2) continue;
                same.Sort((a, b) => { int k = own[b.Id].CompareTo(own[a.Id]); return k != 0 ? k : a.Id.CompareTo(b.Id); });
                for (int i = 1; i < same.Count; i++) units[same[i].Id] = LessNotBelowOne(units[same[i].Id], ScConfig.OutpostDupLess);
            }
            Community c = ScRegistry.Community;
            var result = new Dictionary<int, int[]>();
            foreach (Outpost o in producing)
            {
                int t = units[o.Id];
                if (c.OutpostRunDown) t = Math.Max(0, t - ScConfig.OutpostRunDownLess);
                if (c.OutpostOnFoot) t = Math.Min(t, ScConfig.OutpostOnFootMax);
                result[o.Id] = Split(o, t);
            }
            return result;
        }

        /// <summary>
        /// A day's units by resource: what the outpost makes today (a scavenge one: whatever is shortest), and once it makes 2 or
        /// more, 1 of them is its row's second resource (a fire station at level 2: 1 meds + 1 fuel; at level 3: 2 + 1).
        /// </summary>
        public static int[] Split(Outpost o, int total)
        {
            var y = new int[ScNames.ResourceCount];
            if (o == null || total <= 0) return y;
            int p = (int)ScOutposts.PrimaryToday(o);
            int s = o.Scavenge ? -1 : SecondOf(o.Weight, (Resource)p);
            if (total >= 2 && s >= 0) { y[s] = 1; y[p] = total - 1; }
            else y[p] = total;
            return y;
        }

        /// <summary>The row's second resource: the largest weight after the primary's (weights only rank them now), or -1.</summary>
        public static int SecondOf(float[] weight, Resource primary)
        {
            int best = -1;
            float w = 0f;
            for (int i = 0; i < ScNames.ResourceCount && i < weight.Length; i++)
            {
                if (i == (int)primary || weight[i] <= w) continue;
                w = weight[i];
                best = i;
            }
            return best;
        }

        /// <summary>
        /// "sc poi": what a POI would make as an outpost, unstaffed and in normal mode, at each level (its tier's bonus included;
        /// tier &lt; 0 = unknown, no bonus).
        /// </summary>
        public static string PoiText(PoiClass c)
        {
            if (c == null || !c.Producing) return "-";
            bool scavenge = c.Kind == "scavenge";
            var sb = new StringBuilder("unstaffed:");
            for (int level = 1; level <= 3; level++)
            {
                int v = LevelYield(level);
                if (scavenge) v = Math.Max(1, v - 1);
                v += TierBonus(c.Tier);
                sb.Append(level > 1 ? ";" : "").Append(" L").Append(level).Append(' ');
                if (scavenge) { sb.Append('+').Append(v).Append(" of what you lack"); continue; }
                if (c.Row == null) { sb.Append('-'); continue; }
                int s = SecondOf(c.Row.Weight, c.Row.Primary);
                int p = v >= 2 && s >= 0 ? v - 1 : v;
                sb.Append('+').Append(p).Append(' ').Append(ScNames.ResourceKeys[(int)c.Row.Primary]);
                if (p < v) sb.Append(", +1 ").Append(ScNames.ResourceKeys[s]);
            }
            return sb.Append(" a day").ToString();
        }

        // ------------------------------------------------------------------ outposts: costs ----

        /// <summary>Materials a day for one outpost by its level: OutpostUpkeepByLevel (1, 1, 2); a level past the list takes its last.</summary>
        public static int UpkeepFor(Outpost o)
        {
            int[] t = ScConfig.OutpostUpkeepByLevel;
            if (o == null || t == null || t.Length == 0) return 0;
            return t[Math.Max(0, Math.Min(t.Length - 1, o.Level - 1))];
        }

        /// <summary>
        /// The network's materials upkeep on that rollover day: every producing outpost's (Produces). Idle, contested and damaged
        /// outposts pay nothing that day, as they make nothing.
        /// </summary>
        public static int Upkeep(int day)
        {
            int n = 0;
            foreach (Outpost o in ScRegistry.Outposts) if (Produces(o, day)) n += UpkeepFor(o);
            return n;
        }

        /// <summary>Fuel for the day's haul: 1 per OutpostHaulPerFuel units delivered, the remainder free (0 = no haulage).</summary>
        public static int Haulage(int delivered)
        {
            return ScConfig.OutpostHaulPerFuel > 0 ? Math.Max(0, delivered) / ScConfig.OutpostHaulPerFuel : 0;
        }

        // ------------------------------------------------------------------ the daily flow (round fd2) ----

        /// <summary>
        /// Round fd2: what the rollover into `day` adds to (gain) and takes from (use) each resource, from the functions the
        /// rollover itself runs, in its order, before any shortage: the outposts' deliveries, upkeep (materials), haulage (fuel)
        /// and workshops; the allies' bundles; the garden and the wells and dew collectors; what the members eat (the cook and
        /// tonight's rationing included) and drink; the Meds the injured need. Not daily, so not counted: runs, trades, crafting,
        /// the activities, ammo (spent only by shooting, D3, and by watch shifts as they start). The window's resource strip
        /// shows gain - use. A pure read.
        /// </summary>
        public static void DailyFlow(int day, int[] gain, int[] use)
        {
            Array.Clear(gain, 0, gain.Length);
            Array.Clear(use, 0, use.Length);
            if (!ScRegistry.Loaded) return;
            Community c = ScRegistry.Community;
            int n = ScNames.ResourceCount;
            // ScOutposts.Daily: deliveries, then upkeep and haulage, then the workshops out of what is left
            var delivered = new int[n];
            int total = ScOutposts.ProjectedDelivery(day, delivered);
            for (int r = 0; r < n; r++) gain[r] += delivered[r];
            use[(int)Resource.Materials] += Upkeep(day);
            use[(int)Resource.Fuel] += Haulage(total);
            var stock = new int[n];
            for (int r = 0; r < n; r++) stock[r] = Math.Max(0, c.Get((Resource)r) + gain[r] - use[r]);
            ScGarrison.ProjectedWorkshops(stock, gain, use);
            // ScEncAllies.Daily: each ally's bundles
            ScEncAllies.ProjectedBundles(gain);
            // ScStockpile.DailyConsume
            gain[(int)Resource.Water] += Math.Max(0, c.HomeWater);
            gain[(int)Resource.Food] += Harvest();
            int raw, saved, cut;
            Member cook;
            use[(int)Resource.Food] += FoodEaten(day, out raw, out saved, out cook, out cut);
            use[(int)Resource.Water] += WaterDrunk();
            use[(int)Resource.Meds] += ScStockpile.MedsForRollover();
        }

        // ------------------------------------------------------------------ the console ----

        /// <summary>sc status: the next rollover's needs and costs, step by step.</summary>
        public static string NeedsLine()
        {
            int raw, saved;
            Member cook;
            int cut;
            int day = ScCommunity.Today + 1;
            int food = FoodEaten(day, out raw, out saved, out cook, out cut);
            Member gardener;
            int per = CropsPerFood(out gardener);
            int delivered = ScOutposts.ProjectedDelivery(day);
            int arriving = ScRegistry.Members.Count - Eaters();
            return "next rollover: food " + food + " (" + raw + " eaten" + (saved > 0 ? ", cook " + (cook != null ? cook.Name : "?") + " -" + saved : "")
                   + (cut > 0 ? ", rationed -" + cut : "") + ")"
                   + ", garden +" + Harvest() + " (" + ScRegistry.Community.Crops + " crops, "
                   + (per > 0 ? "1 food per " + per + (gardener != null ? " with " + gardener.Name : "") : "garden off (CropsPerFood 0)") + ")"
                   + ", water " + WaterDrunk() + " (wells +" + Math.Max(0, ScRegistry.Community.HomeWater) + ")"
                   + (arriving > 0 ? ", " + arriving + " on the way eat nothing yet" : "")
                   + "; outposts: upkeep " + Upkeep(day) + " mats, haulage " + Haulage(delivered) + " fuel for " + delivered + " delivered";
        }
    }
}
