using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Outposts v1.1, growth and identity (Outposts_Plan.md section 3):
    ///   Levels  - bought from the stockpile. L2: OutpostL2Mats + OutpostL2Fuel after OutpostL2Days held, and a second
    ///             post. L3: OutpostL3Mats + OutpostL3Fuel after OutpostL3Days more, needs someone posted there. Round ie1:
    ///             a level makes OutpostLevelYield (1 / 2 / 3) a day and costs OutpostUpkeepByLevel (1 / 1 / 2) materials
    ///             a day (ScEconomy).
    ///   Perks   - while a producing (Active, non-scavenge) outpost of a type exists:
    ///             food  Canteen        the hunger morale hit is floored at OutpostCanteenFloor (ScMorale)
    ///             meds  Field clinic   OutpostClinicHeals injuries healed free each day (ScStockpile)
    ///             ammo  Armed escort   run risk -OutpostEscortRisk per such outpost, at most -OutpostEscortMax (ScRuns)
    ///             mats  Salvage yard   OutpostSalvageCut materials off each fee, upgrade and repair, never below 1 (here)
    ///             fuel  Motor pool     OutpostMotorPoolBundles more from every supply run (ScRuns)
    ///             water Cistern        the thirst morale hit is floored at OutpostCisternFloor (ScMorale, round wl)
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

        /// <summary>
        /// A materials cost after the salvage yard - an establish fee, an upgrade, a repair. Round ie1: OutpostSalvageCut less,
        /// never below 1, and a free cost stays free (it was x0.75 rounded up, which left the fee of 2 and the repair of 2 as they
        /// were). Fuel costs are not cut.
        /// </summary>
        public static int Salvaged(int cost)
        {
            if (cost <= 0) return 0;
            return PerkActive(Resource.Materials) ? Math.Max(1, cost - ScConfig.OutpostSalvageCut) : cost;
        }

        public static int FeeMats() { return Salvaged(ScConfig.OutpostFeeMats); }
        public static int FeeFuel() { return Math.Max(0, ScConfig.OutpostFeeFuel); }

        /// <summary>The perks in force now, one per line (the window).</summary>
        public static string PerkLines()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < ScNames.ResourceCount; i++)
            {
                if ((Resource)i == Resource.Luxury || !PerkActive((Resource)i)) continue;    // no luxury outposts, no luxury perk
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(Localization.Get("scOpPerk" + (Resource)i));
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ mode, check-in, depletion ----

        public static readonly string[] ModeKeys = { "scOpModeNormal", "scOpModeQuiet", "scOpModePush" };

        // round ie1: the mode's yield step, the check-in and depletion are whole units in ScEconomy (ModeUnits, CheckInLate, Depleted)

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
            if (o == null || !ScRegistry.Loaded) return;      // round MP1: never a client's copy (the window refuses there first)
            o.Mode = (Mathf.Clamp(o.Mode, 0, 2) + 1) % 3;
        }

        // ------------------------------------------------------------------ investment, stash and haul ----

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
        public static int Unstash(Outpost o, World w, EntityPlayer player)
        {
            // round MP0: 'player' is the community player visiting the outpost (ScGarrison.Tick: any of them), who is told
            if (o == null || w == null || o.StashTotal == 0) return 0;
            TileEntity te = w.GetTileEntity(o.Flag);
            TEFeatureStorage st = te != null ? te.GetSelfOrFeature<TEFeatureStorage>() : null;
            if (st == null) return 0;
            // round MP0 (the review of part 2, S6): not while a player has the box open - a client's open box sends its own copy
            // back over the server's write (the bundles would be lost); the stash waits for the next visit tick
            if (LockManager.Instance != null && LockManager.Instance.IsLockedServer(st, 0)) return 0;
            int moved = 0;
            for (int r = 0; r < ScNames.ResourceCount; r++)
            {
                if (o.Stash[r] <= 0) continue;
                // the stash counts units: Large bundles first, then one Medium or Small for the rest (round wl)
                int large, medium, small;
                ScStockpile.SplitUnits(o.Stash[r], out large, out medium, out small);
                int[] counts = { large, medium, small };
                BundleSize[] sizes = { BundleSize.Large, BundleSize.Medium, BundleSize.Small };
                for (int k = 0; k < 3; k++)
                {
                    if (counts[k] <= 0) continue;
                    ItemValue iv = ScStockpile.BundleValue((Resource)r, sizes[k]);
                    if (iv == null) continue;
                    int units = (int)sizes[k];
                    int stackMax = Math.Max(1, iv.ItemClass.Stacknumber.Value);
                    while (counts[k] > 0)
                    {
                        int n = Math.Min(stackMax, counts[k]);
                        if (!st.AddItem(new ItemStack(iv.Clone(), n))) break;
                        counts[k] -= n;
                        o.Stash[r] -= n * units;
                        moved += n;
                    }
                }
            }
            if (moved > 0)
            {
                if (player != null) ScCommunity.Tell(player, string.Format(Localization.Get("scOpUnstashed"), moved, o.Name), "");
                ScLog.Out("stash and haul: " + moved + " bundle(s) put in the claim box at " + o.Name + (o.StashTotal > 0 ? ", " + o.StashTotal + " still stashed (box full)" : ""));
            }
            return moved;
        }

        /// <summary>"2 beds, 1 facilities, 0 crops, 3 defenses: +1" - what was built there and its whole-unit step (ScEconomy.BuiltUnits).</summary>
        public static string InvestText(Outpost o)
        {
            if (o == null) return "";
            return string.Format(Localization.Get("scUiOpInvest"), o.Invest[0], o.Invest[1], o.Invest[2], o.Invest[3], ScEconomy.BuiltUnits(o));
        }

        // ------------------------------------------------------------------ levels ----

        /// <summary>The next level's cost (materials after the salvage yard; fuel is not cut); false at the top level.</summary>
        public static bool NextCost(Outpost o, out int mats, out int fuel)
        {
            mats = fuel = 0;
            if (o == null || o.Level >= 3) return false;
            mats = Salvaged(o.Level == 1 ? ScConfig.OutpostL2Mats : ScConfig.OutpostL3Mats);
            fuel = Math.Max(0, o.Level == 1 ? ScConfig.OutpostL2Fuel : ScConfig.OutpostL3Fuel);
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

        /// <summary>
        /// Why "Upgrade" is refused, as a finished tooltip; null = it may go ahead. Round MP2: a read - a client's window colors the
        /// button from its copy (Ready; it said "host only" there); Upgrade keeps the server-only gate.
        /// </summary>
        public static string CanUpgrade(Outpost o)
        {
            if (!ScRegistry.Ready) return Localization.Get("scHostOnly");
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

        /// <summary>The next level, bought (outpost.upgrade on the server, sc outpost upgrade). Round MP2: any player; never a client's copy (E10).</summary>
        public static bool Upgrade(Outpost o, EntityPlayer player, out string message)
        {
            if (!ScCommunity.IsHost || !ScRegistry.Loaded) { message = Localization.Get("scHostOnly"); return false; }
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
            ScOutposts.RefreshMarkers(player);      // round MP2: the host's own player's map only (round MP4: remote players' come from ScMarkers)
            ScOutposts.HostMarkersFollow(player);   // round MP2 (review core/window m7): a remote player's upgrade - the listen host's map follows (its name carries the yield)
            ScRegistry.SaveAtomic();
            message = string.Format(Localization.Get("scOpUpgraded"), o.Name, o.Level, ScOutposts.YieldText(ScOutposts.Estimate(o)));
            ScLog.Out("outpost " + o.Id + " at " + o.Name + " upgraded to level " + o.Level + " for " + mats + " mats + " + fuel + " fuel");
            return true;
        }

        /// <summary>
        /// The level line for the window: "Level 2 - 2 a day" (round ie1: the level's whole units, ScEconomy.BaseUnits; it was
        /// "yield x1.3"). Its upkeep is in the network's costs line.
        /// </summary>
        public static string LevelText(Outpost o)
        {
            if (o == null) return "";
            // the review of ie1 (L2): a tier 4-5 building's +1 is part of what the level makes there
            return string.Format(Localization.Get("scUiOpLevelYield"), o.Level, ScEconomy.BaseUnits(o) + ScEconomy.TierBonus(o.Tier));
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
