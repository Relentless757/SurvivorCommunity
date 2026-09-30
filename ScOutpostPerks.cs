using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Outposts v1.1, growth and identity (Outposts_Plan.md section 3):
    ///   Levels  - bought from the stockpile. L2: OutpostL2Mats + OutpostL2Fuel after OutpostL2Days held, yield
    ///             x OutpostL2Mult and a second post. L3: OutpostL3Mats + OutpostL3Fuel after OutpostL3Days more, needs
    ///             someone posted there, yield x OutpostL3Mult. Each level above 1 adds OutpostUpkeepPerLevel upkeep.
    ///   Perks   - while a producing (Active, non-scavenge) outpost of a type exists:
    ///             food  Canteen        the hunger morale hit is floored at OutpostCanteenFloor (ScMorale)
    ///             meds  Field clinic   OutpostClinicHeals injuries healed free each day (ScStockpile)
    ///             ammo  Armed escort   run risk -OutpostEscortRisk per such outpost, at most -OutpostEscortMax (ScRuns)
    ///             mats  Salvage yard   establish fees and upgrades x OutpostSalvageMult (here)
    ///             fuel  Motor pool     run yield x OutpostMotorPoolMult (ScRuns)
    /// Host only.
    /// </summary>
    public static class ScOutpostPerks
    {
        // ------------------------------------------------------------------ perks ----

        /// <summary>A producing outpost whose type makes this resource first.</summary>
        public static bool PerkActive(Resource r)
        {
            foreach (Outpost o in ScRegistry.Outposts)
                if (o.State == OutpostState.Active && !o.Scavenge && o.Primary == r) return true;
            return false;
        }

        public static int CountActive(Resource r)
        {
            int n = 0;
            foreach (Outpost o in ScRegistry.Outposts)
                if (o.State == OutpostState.Active && !o.Scavenge && o.Primary == r) n++;
            return n;
        }

        /// <summary>How much an ammo outpost's armed escort takes off a run's risk.</summary>
        public static float EscortRisk()
        {
            return Math.Min(ScConfig.OutpostEscortMax, ScConfig.OutpostEscortRisk * CountActive(Resource.Ammo));
        }

        /// <summary>A cost after the salvage yard (rounded up, never below 0).</summary>
        public static int Salvaged(int cost)
        {
            if (cost <= 0) return 0;
            return PerkActive(Resource.Materials) ? Mathf.CeilToInt(cost * ScConfig.OutpostSalvageMult) : cost;
        }

        public static int FeeMats() { return Salvaged(ScConfig.OutpostFeeMats); }
        public static int FeeFuel() { return Salvaged(ScConfig.OutpostFeeFuel); }

        /// <summary>The perks in force now, one per line (the window).</summary>
        public static string PerkLines()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < 5; i++)
            {
                if (!PerkActive((Resource)i)) continue;
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(Localization.Get("scOpPerk" + (Resource)i));
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ mode, check-in, depletion ----

        public static readonly string[] ModeKeys = { "scOpModeNormal", "scOpModeQuiet", "scOpModePush" };

        public static float ModeMult(Outpost o)
        {
            if (o == null) return 1f;
            return o.Mode == 1 ? ScConfig.OutpostQuietMult : (o.Mode == 2 ? ScConfig.OutpostPushMult : 1f);
        }

        /// <summary>The mode's effect on an outpost's risk (v2 attacks).</summary>
        public static float ModeRisk(Outpost o)
        {
            if (o == null) return 1f;
            return o.Mode == 1 ? ScConfig.OutpostQuietRisk : (o.Mode == 2 ? ScConfig.OutpostPushRisk : 1f);
        }

        public static string ModeLabel(Outpost o) { return Localization.Get(ModeKeys[o != null ? Mathf.Clamp(o.Mode, 0, 2) : 0]); }

        /// <summary>Normal -> quiet -> push -> normal.</summary>
        public static void CycleMode(Outpost o)
        {
            if (o == null) return;
            o.Mode = (Mathf.Clamp(o.Mode, 0, 2) + 1) % 3;
        }

        /// <summary>Supply-line check-in (off unless OutpostCheckInDays &gt; 0): unvisited too long, the yield slips.</summary>
        public static float CheckInMult(Outpost o)
        {
            if (o == null || ScConfig.OutpostCheckInDays <= 0) return 1f;
            int last = Math.Max(o.Day, o.LastVisitDay);
            int over = ScCommunity.Today - last - ScConfig.OutpostCheckInDays;
            if (over <= 0) return 1f;
            return Math.Max(ScConfig.OutpostCheckInFloor, 1f - ScConfig.OutpostCheckInDecay * over);
        }

        /// <summary>Depletion (off unless OutpostDepletion &gt; 0): past that many bundles the site is picked clean.</summary>
        public static float DepletionMult(Outpost o)
        {
            if (o == null || ScConfig.OutpostDepletion <= 0f) return 1f;
            return o.Produced >= ScConfig.OutpostDepletion ? ScConfig.OutpostDepletedMult : 1f;
        }

        // ------------------------------------------------------------------ investment, stash and haul ----

        /// <summary>What the player built there: beds, facilities, and (food outposts) crops, capped at OutpostInvestCap.</summary>
        public static float InvestBonus(Outpost o)
        {
            if (o == null) return 0f;
            float b = ScConfig.OutpostBedBonus * Math.Min(o.Invest[0], ScConfig.OutpostBedMax)
                    + ScConfig.OutpostFacilityBonus * Math.Min(o.Invest[1], ScConfig.OutpostFacilityMax);
            if (!o.Scavenge && o.Primary == Resource.Food) b += ScConfig.OutpostCropBonus * Math.Min(o.Invest[2], ScConfig.OutpostCropMax);
            return Math.Min(ScConfig.OutpostInvestCap, b);
        }

        public static float InvestMult(Outpost o) { return 1f + InvestBonus(o); }

        /// <summary>Bundles hauled home a day (int.MaxValue when stash and haul is off).</summary>
        public static int HaulCap(Outpost o)
        {
            if (!ScConfig.OutpostStashAndHaul || o == null) return int.MaxValue;
            return Math.Max(0, ScConfig.OutpostHaulBase + ScConfig.OutpostHaulPerLevel * Math.Max(0, o.Level - 1));
        }

        /// <summary>
        /// Stash and haul: the bundles past the day's haul go into the claim's own box while the player is there
        /// (ScGarrison.Tick). What does not fit stays stashed for the next visit.
        /// </summary>
        public static int Unstash(Outpost o, World w, EntityPlayerLocal player)
        {
            if (o == null || w == null || o.StashTotal == 0) return 0;
            TileEntity te = w.GetTileEntity(o.Flag);
            TEFeatureStorage st = te != null ? te.GetSelfOrFeature<TEFeatureStorage>() : null;
            if (st == null) return 0;
            int moved = 0;
            for (int r = 0; r < 5; r++)
            {
                if (o.Stash[r] <= 0) continue;
                ItemValue iv = ItemClass.GetItem(ScNames.BundleItem((Resource)r), true);
                if (iv == null || iv.IsEmpty()) continue;
                int stackMax = Math.Max(1, iv.ItemClass.Stacknumber.Value);
                while (o.Stash[r] > 0)
                {
                    int n = Math.Min(stackMax, o.Stash[r]);
                    if (!st.AddItem(new ItemStack(iv.Clone(), n))) break;
                    o.Stash[r] -= n;
                    moved += n;
                }
            }
            if (moved > 0)
            {
                if (player != null) ScCommunity.Tell(player, string.Format(Localization.Get("scOpUnstashed"), moved, o.Name), "");
                ScLog.Out("stash and haul: " + moved + " bundle(s) put in the claim box at " + o.Name + (o.StashTotal > 0 ? ", " + o.StashTotal + " still stashed (box full)" : ""));
            }
            return moved;
        }

        public static string InvestText(Outpost o)
        {
            if (o == null) return "";
            return string.Format(Localization.Get("scUiOpInvest"), o.Invest[0], o.Invest[1], o.Invest[2], o.Invest[3],
                                 Mathf.RoundToInt(InvestBonus(o) * 100f));
        }

        // ------------------------------------------------------------------ levels ----

        public static float LevelMult(Outpost o)
        {
            if (o == null) return 1f;
            return o.Level >= 3 ? ScConfig.OutpostL3Mult : (o.Level == 2 ? ScConfig.OutpostL2Mult : 1f);
        }

        /// <summary>Materials a day for one working outpost at its level.</summary>
        public static float UpkeepFor(Outpost o)
        {
            return ScConfig.OutpostUpkeep + ScConfig.OutpostUpkeepPerLevel * Math.Max(0, (o != null ? o.Level : 1) - 1);
        }

        /// <summary>The next level's cost (after the salvage yard); false at the top level.</summary>
        public static bool NextCost(Outpost o, out int mats, out int fuel)
        {
            mats = fuel = 0;
            if (o == null || o.Level >= 3) return false;
            mats = Salvaged(o.Level == 1 ? ScConfig.OutpostL2Mats : ScConfig.OutpostL3Mats);
            fuel = Salvaged(o.Level == 1 ? ScConfig.OutpostL2Fuel : ScConfig.OutpostL3Fuel);
            return true;
        }

        /// <summary>Days until the next level may be bought (0 = now).</summary>
        public static int DaysToNext(Outpost o)
        {
            if (o == null || o.Level >= 3) return 0;
            int since = o.Level == 1 ? o.Day : Math.Max(o.Day, o.LevelDay);
            int need = o.Level == 1 ? ScConfig.OutpostL2Days : ScConfig.OutpostL3Days;
            return Math.Max(0, since + need - ScCommunity.Today);
        }

        /// <summary>Why "Upgrade" is refused, as a finished tooltip; null = it may go ahead.</summary>
        public static string CanUpgrade(Outpost o)
        {
            if (!ScCommunity.IsHost) return Localization.Get("scHostOnly");
            if (o == null || !o.Holding) return Localization.Get("scOpPostLost");
            if (o.Level >= 3) return string.Format(Localization.Get("scOpUpgradeMax"), o.Name);
            if (o.State != OutpostState.Active) return string.Format(Localization.Get("scOpUpgradeNotActive"), o.Name);
            int wait = DaysToNext(o);
            if (wait > 0) return string.Format(Localization.Get("scOpUpgradeWait"), o.Name, wait);
            if (o.Level == 2 && ScGarrison.StaffCount(o) == 0) return string.Format(Localization.Get("scOpUpgradeNoStaff"), o.Name);
            int mats, fuel;
            NextCost(o, out mats, out fuel);
            Community c = ScRegistry.Community;
            if (c.Get(Resource.Materials) < mats || c.Get(Resource.Fuel) < fuel)
                return string.Format(Localization.Get("scOpUpgradeNoFee"), o.Level + 1, mats, fuel, c.Get(Resource.Materials), c.Get(Resource.Fuel));
            return null;
        }

        public static bool Upgrade(Outpost o, EntityPlayerLocal player, out string message)
        {
            message = CanUpgrade(o);
            if (message != null) return false;
            int mats, fuel;
            NextCost(o, out mats, out fuel);
            Community c = ScRegistry.Community;
            c.Add(Resource.Materials, -mats);
            c.Add(Resource.Fuel, -fuel);
            o.Level++;
            o.LevelDay = ScCommunity.Today;
            ScOutposts.NoteLedger(string.Format(Localization.Get("scOpLedgerLevel"), o.Name, o.Level));
            ScOutposts.RefreshMarkers(player);
            ScRegistry.SaveAtomic();
            message = string.Format(Localization.Get("scOpUpgraded"), o.Name, o.Level, ScOutposts.YieldText(ScOutposts.Estimate(o)));
            ScLog.Out("outpost " + o.Id + " at " + o.Name + " upgraded to level " + o.Level + " for " + mats + " mats + " + fuel + " fuel");
            return true;
        }

        /// <summary>The level line for the window: "Level 2 - yield x1.3" and what the next costs or waits for.</summary>
        public static string LevelText(Outpost o)
        {
            if (o == null) return "";
            string head = o.Level > 1 ? string.Format(Localization.Get("scUiOpLevelMult"), o.Level, LevelMult(o).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture))
                                      : string.Format(Localization.Get("scUiOpLevel"), o.Level);
            return head;
        }

        public static string NextText(Outpost o)
        {
            if (o == null) return "";
            if (o.Level >= 3) return Localization.Get("scUiOpMaxLevel");
            int wait = DaysToNext(o);
            if (wait > 0) return string.Format(Localization.Get("scUiOpNextWait"), wait);
            if (o.Level == 2 && ScGarrison.StaffCount(o) == 0) return Localization.Get("scUiOpNextStaff");
            int mats, fuel;
            NextCost(o, out mats, out fuel);
            return string.Format(Localization.Get("scUiOpNextLevel"), mats, fuel);
        }
    }
}
