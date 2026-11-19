using System;
using System.Collections.Generic;
using System.Text;

namespace SurvivorCommunity
{
    /// <summary>
    /// The stockpile (one number per resource in the registry, counted in bundle UNITS) and the packing station's
    /// verbs on its container:
    ///   Pack     - turns the loose items of one category in the box into whole units (points = EconomicValue
    ///              x count, PointsPerBundle&lt;Resource&gt; per unit; the highest-value stacks go first and
    ///              anything not needed for a whole unit stays in the box; a small overshoot on the last
    ///              stack is accepted), handed out as Large bundles (3 units) first, then one Medium (2) or Small (1)
    ///   Deposit  - moves every bundle in the box into the stockpile at its size (bundles only exist in transit;
    ///              the old one-size bundle counts 1)
    ///   Withdraw - one Small bundle from the stockpile back into the box
    /// and the daily consumption the ticker runs. Host only.
    /// </summary>
    public static class ScStockpile
    {
        private static readonly FastTags<TagGroup.Global> BundleTags = FastTags<TagGroup.Global>.Parse("scbundle");

        /// <summary>
        /// The storage feature of an open packing station - or of an outpost's land claim, whose box is the
        /// stockpile's door out there - or null for any other container.
        /// </summary>
#if SC_V34
        public static TEFeatureStorage StationOf(TEFeatureStorage te)      // 3.4: the loot window holds a TEFeatureStorage (no ITileEntityLootable)
#else
        public static TEFeatureStorage StationOf(ITileEntityLootable te)
#endif
        {
            try
            {
                TEFeatureStorage st = te as TEFeatureStorage;
                if (st == null || st.Parent == null) return null;
                if (st.Parent.GetFeature<TEFeatureSCPacking>() != null) return st;
                if (st.Parent.GetFeature<TEFeatureLandClaim>() != null && ScOutposts.IsOutpostClaim(st.Parent.ToWorldPos())) return st;
                return null;
            }
            catch (Exception) { return null; }
        }

        /// <summary>A bundle stack: its resource and the units ONE of its items holds (Small 1, Medium 2, Large 3, old 1).</summary>
        public static bool IsBundle(ItemStack s, out Resource r, out int units)
        {
            r = Resource.Food;
            units = 0;
            if (s == null || s.IsEmpty() || s.itemValue == null || s.itemValue.ItemClass == null) return false;
            ItemClass ic = s.itemValue.ItemClass;
            if (!ic.HasAnyTags(BundleTags)) return false;
            return ScNames.TryBundle(ic.Name, out r, out units);
        }

        /// <summary>Units as bundles, the biggest first: as many Large as fit, then one Medium or one Small for the rest.</summary>
        public static void SplitUnits(int units, out int large, out int medium, out int small)
        {
            units = Math.Max(0, units);
            large = units / 3;
            int rest = units - large * 3;
            medium = rest == 2 ? 1 : 0;
            small = rest == 1 ? 1 : 0;
        }

        /// <summary>The item of a sized bundle, or null (an error is logged once) when Config/items.xml did not define it.</summary>
        public static ItemValue BundleValue(Resource r, BundleSize size)
        {
            string name = ScNames.BundleItem(r, size);
            ItemValue iv = ItemClass.GetItem(name, true);
            if (iv == null || iv.IsEmpty() || iv.ItemClass == null) { ScLog.Once("bundle-item-" + name, "bundle item " + name + " is not defined - Config/items.xml did not apply"); return null; }
            return iv;
        }

        /// <summary>"2 large and 1 small" (the sizes Pack made).</summary>
        public static string SizesText(int large, int medium, int small)
        {
            var parts = new List<string>(3);
            if (large > 0) parts.Add(large + " " + Localization.Get("scBundleSizeLarge"));
            if (medium > 0) parts.Add(medium + " " + Localization.Get("scBundleSizeMedium"));
            if (small > 0) parts.Add(small + " " + Localization.Get("scBundleSizeSmall"));
            if (parts.Count == 0) return "0";
            if (parts.Count == 1) return parts[0];
            return string.Join(", ", parts.GetRange(0, parts.Count - 1).ToArray()) + " " + Localization.Get("scAnd") + " " + parts[parts.Count - 1];
        }

        private struct Slot
        {
            public int Index;
            public int Count;
            public int PointsEach;
        }

        /// <summary>Packs one category. Returns the number of bundles made (0 with a message key + args).</summary>
        public static int Pack(TEFeatureStorage st, Resource r, EntityPlayerLocal player, out string message)
        {
            message = null;
            try
            {
                if (!ScCommunity.IsHost) { message = Localization.Get("scHostOnly"); return 0; }
                if (st == null) { message = Localization.Get("scInternal"); return 0; }
                ScConfig.EnsureParsed();
#if SC_V34
                ItemStack[] items = st.ItemGrid != null ? st.ItemGrid.items : null;     // 3.4: the grid's slots, written through UpdateSlot
#else
                ItemStack[] items = st.items;
#endif
                if (items == null) { message = Localization.Get("scInternal"); return 0; }

                var slots = new List<Slot>();
                long total = 0;
                for (int i = 0; i < items.Length; i++)
                {
                    ItemStack s = items[i];
                    if (s == null || s.IsEmpty() || s.itemValue == null) continue;
                    if (!ScCategories.Is(s.itemValue.ItemClass, r)) continue;
                    int each = ScCategories.PointsEach(s.itemValue.ItemClass);
                    if (each <= 0) continue;
                    slots.Add(new Slot { Index = i, Count = s.count, PointsEach = each });
                    total += (long)each * s.count;
                }
                // Craftsmanship: stationed craftsmen pack tighter (threshold x (1 - PackBonusPerStar x stars), floor 0.6)
                int craftStars = ScSkills.SpecialistStars(Specialty.Craftsmanship);
                float packMult = Math.Max(0.6f, 1f - ScConfig.PackBonusPerStar * craftStars);
                int threshold = Math.Max(1, (int)Math.Round(ScConfig.PointsPerBundle[(int)r] * packMult));
                int bundles = (int)(total / threshold);
                if (bundles <= 0)
                {
                    message = string.Format(Localization.Get("scPackShort"), Localization.Get(ScNames.LocKey(r)), total, threshold);
                    return 0;
                }

                // take the most valuable items first until the whole bundles are covered
                slots.Sort((a, b) => b.PointsEach.CompareTo(a.PointsEach));
                long need = (long)bundles * threshold;
                for (int k = 0; k < slots.Count && need > 0; k++)
                {
                    Slot sl = slots[k];
                    int take = (int)Math.Min(sl.Count, (need + sl.PointsEach - 1) / sl.PointsEach);
                    if (take <= 0) continue;
                    ItemStack s = items[sl.Index];
                    if (take >= s.count) st.UpdateSlot(sl.Index, ItemStack.Empty.Clone());
                    else { s.count -= take; st.UpdateSlot(sl.Index, s); }
                    need -= (long)take * sl.PointsEach;
                }

                int large, medium, small;
                SplitUnits(bundles, out large, out medium, out small);
                int made = GiveBundles(st, r, BundleSize.Large, large, player) * 3
                         + GiveBundles(st, r, BundleSize.Medium, medium, player) * 2
                         + GiveBundles(st, r, BundleSize.Small, small, player);
                st.SetModified();
                if (made > 0 && craftStars > 0)
                {
                    foreach (var cm in ScRegistry.Members.Values)
                        if (cm.Specialty == Specialty.Craftsmanship && cm.IsHome && cm.Status != MemberStatus.OnRun) ScSkills.AddSpecialtyXp(cm, 1f);
                }
                message = string.Format(Localization.Get("scPackedInto"), made, Localization.Get(ScNames.LocKey(r)), SizesText(large, medium, small));
                ScLog.Out("packed " + made + " " + r + " unit(s) (" + large + " large, " + medium + " medium, " + small + " small) from " + total + " points at the station");
                return made;
            }
            catch (Exception e)
            {
                ScLog.Once("pack", e);
                message = Localization.Get("scInternal");
                return 0;
            }
        }

        /// <summary>
        /// Bundles of one size into the box; what does not fit goes to the player, then the ground. No box (st null: an
        /// enclave's threat payout, E2): the player, then the ground. Returns the bundles given.
        /// </summary>
        public static int GiveBundles(TEFeatureStorage st, Resource r, BundleSize size, int count, EntityPlayerLocal player)
        {
            if (count <= 0) return 0;
            ItemValue iv = BundleValue(r, size);
            if (iv == null) return 0;
            int stackMax = Math.Max(1, iv.ItemClass.Stacknumber.Value);
            int given = 0;
            while (given < count)
            {
                int n = Math.Min(stackMax, count - given);
                var stack = new ItemStack(iv.Clone(), n);
                if (st != null && st.AddItem(stack)) { given += n; continue; }
                if (player != null && player.PlayerUI != null && player.PlayerUI.xui != null && player.PlayerUI.xui.PlayerInventory != null
                    && player.PlayerUI.xui.PlayerInventory.AddItem(stack)) { given += n; continue; }
                if (player != null)
                {
                    GameManager.Instance.ItemDropServer(stack, player.GetDropPosition(), UnityEngine.Vector3.zero, -1, 60f);
                    given += n;
                    continue;
                }
                break;
            }
            return given;
        }

        /// <summary>Moves every bundle in the box into the stockpile, each at its size. Returns the number of bundles moved.</summary>
        public static int Deposit(TEFeatureStorage st, out string message)
        {
            message = null;
            try
            {
                if (!ScCommunity.IsHost) { message = Localization.Get("scHostOnly"); return 0; }
#if SC_V34
                if (st == null || st.ItemGrid == null) { message = Localization.Get("scInternal"); return 0; }
                ItemStack[] items = st.ItemGrid.items;
#else
                if (st == null || st.items == null) { message = Localization.Get("scInternal"); return 0; }
                ItemStack[] items = st.items;
#endif
                int moved = 0;
                var counts = new int[ScNames.ResourceCount];
                for (int i = 0; i < items.Length; i++)
                {
                    Resource r;
                    int units;
                    if (!IsBundle(items[i], out r, out units)) continue;
                    counts[(int)r] += items[i].count * units;
                    moved += items[i].count;
                    st.UpdateSlot(i, ItemStack.Empty.Clone());
                }
                if (moved == 0) { message = Localization.Get("scNoBundles"); return 0; }
                for (int i = 0; i < ScNames.ResourceCount; i++) ScRegistry.Community.Add((Resource)i, counts[i]);
                st.SetModified();
                ScRegistry.SaveAtomic();
                message = string.Format(Localization.Get("scDepositedUnits"), moved, ScOutposts.Amounts(counts));
                ScLog.Out("deposited " + moved + " bundle(s) (" + ScOutposts.Amounts(counts) + "): " + StockText());
                return moved;
            }
            catch (Exception e)
            {
                ScLog.Once("deposit", e);
                message = Localization.Get("scInternal");
                return 0;
            }
        }

        /// <summary>One Small bundle (one unit) from the stockpile into the box.</summary>
        public static bool Withdraw(TEFeatureStorage st, Resource r, out string message)
        {
            message = null;
            try
            {
                if (!ScCommunity.IsHost) { message = Localization.Get("scHostOnly"); return false; }
                if (st == null) { message = Localization.Get("scInternal"); return false; }
                if (ScRegistry.Community.Get(r) <= 0) { message = string.Format(Localization.Get("scStockEmpty"), Localization.Get(ScNames.LocKey(r))); return false; }
                ItemValue iv = BundleValue(r, BundleSize.Small);
                if (iv == null) { message = Localization.Get("scInternal"); return false; }
                if (!st.AddItem(new ItemStack(iv.Clone(), 1))) { message = Localization.Get("scBoxFull"); return false; }
                ScRegistry.Community.Add(r, -1);
                st.SetModified();
                ScRegistry.SaveAtomic();
                message = string.Format(Localization.Get("scWithdrawn"), Localization.Get(ScNames.LocKey(r)));
                return true;
            }
            catch (Exception e)
            {
                ScLog.Once("withdraw", e);
                message = Localization.Get("scInternal");
                return false;
            }
        }

        public static string StockText()
        {
            Community c = ScRegistry.Community;
            return string.Format(Localization.Get("scStockText"), c.Get(Resource.Food), c.Get(Resource.Meds), c.Get(Resource.Ammo), c.Get(Resource.Materials), c.Get(Resource.Fuel),
                                 c.Get(Resource.Water), c.Get(Resource.Luxury));
        }

        // ---- daily use ----

        /// <summary>
        /// One day of the community: the wells and dew collectors fill the water store, the garden adds its food, then food and
        /// water for every member who has arrived (round ie1: whole units from ScEconomy, nothing carried), meds for injured
        /// members (one per injury, healing it), and the day's ammo resupplies reported (no ammo upkeep), and the Meds the
        /// members' own medical restocks took (round md1). Appends a summary line.
        /// </summary>
        public static void DailyConsume(int day, StringBuilder summary)
        {
            Community c = ScRegistry.Community;
            int members = ScRegistry.Members.Count;
            // water facilities in the home zone (the last scan's count): they run with or without anyone to drink
            int drawn = Math.Max(0, c.HomeWater);
            if (drawn > 0) c.Add(Resource.Water, drawn);
            if (members == 0)
            {
                c.Hungry = false; c.Thirsty = false; c.AmmoOpened = 0; c.AmmoShort = false; c.MedsRestocked = 0;
                if (drawn > 0) { if (summary.Length > 0) summary.Append(' '); summary.Append(string.Format(Localization.Get("scDailyWells"), drawn)); }
                return;
            }

            // Gardening: living crops in the zone (last scan) feed the stockpile through the gardeners - round ie1: 1 food per
            // CropsPerFood crops, at most 1 per gardener star (ScEconomy.Harvest), the remainder dropped, never carried
            int harvested = ScEconomy.Harvest();
            if (ScSkills.SpecialistStars(Specialty.Gardening) > 0 && c.Crops > 0)
            {
                if (harvested > 0) c.Add(Resource.Food, harvested);
                foreach (var g in ScRegistry.Members.Values)
                    if (g.Specialty == Specialty.Gardening && g.IsHome && g.Status != MemberStatus.OnRun) ScSkills.AddSpecialtyXp(g, 2f);
            }

            // food (round ie1): ScEconomy.FoodEaten - MemberFoodPerDay a member who has arrived (a trait's eats= instead), less
            // the best cook's ceil(stars / CookStarsPerFood), never more than half. Whole units: a short day is a hungry day,
            // nothing is owed or carried to the next
            int foodRaw, cookSaved;
            Member cook;
            int foodWhole = ScEconomy.FoodEaten(out foodRaw, out cookSaved, out cook);
            int foodUsed = Math.Min(foodWhole, c.Get(Resource.Food));
            c.Add(Resource.Food, -foodUsed);
            bool hungry = foodUsed < foodWhole;
            c.Hungry = hungry;

            // water (round wl): MemberWaterPerDay a member who has arrived (ScEconomy.WaterDrunk); short = thirsty, the hunger rules again
            int waterWhole = ScEconomy.WaterDrunk();
            int waterUsed = Math.Min(waterWhole, c.Get(Resource.Water));
            c.Add(Resource.Water, -waterUsed);
            bool thirsty = waterUsed < waterWhole;
            c.Thirsty = thirsty;

            // meds: one bundle heals 1 + floor(medic stars x MedicPerStar) injuries
            if (cook != null && foodUsed > 0) ScSkills.AddSpecialtyXp(cook, 2f);
            Member medic;
            int medicStars = ScSkills.BestSpecialist(Specialty.Medicine, out medic);
            int healsPerBundle = 1 + (int)Math.Floor(medicStars * ScConfig.MedicPerStar);
            int medsUsed = 0, stillInjured = 0, healsLeft = 0, healed = 0;
            int clinic = ScOutpostPerks.PerkActive(Resource.Meds) ? ScConfig.OutpostClinicHeals : 0;   // a medical outpost's field clinic
#if SC_V34
            int selfHealed = 0;
            GameRandom healRnd = ScCommunity.World != null ? ScCommunity.World.GetGameRandom() : null;
#endif
            foreach (var m in ScRegistry.Members.Values)
            {
                if (!m.Injured) continue;
#if SC_V34
                // round m1t (T1): Fast Healer - her injury heals with no Meds bundle (or clinic place) half of the time
                if (ScTraitEffects.HealsUnaided(m, healRnd)) { m.Injured = false; selfHealed++; continue; }   // no medic XP for it
#endif
                if (clinic > 0) { clinic--; m.Injured = false; healed++; continue; }
                if (healsLeft == 0 && c.Get(Resource.Meds) > 0) { c.Add(Resource.Meds, -1); medsUsed++; healsLeft = healsPerBundle; }
                if (healsLeft > 0) { healsLeft--; m.Injured = false; healed++; }
                else stillInjured++;
            }
            if (healed > 0 && medic != null && medicStars > 0) ScSkills.AddSpecialtyXp(medic, 2f);

            // Wits: every day lived
            foreach (var m in ScRegistry.Members.Values) ScSkills.AddXp(m, Skill.Wits, ScConfig.DailyWitsXp);

            // ammo: no daily upkeep (decision D3, the weapons task's W3 call: ammo is spent only by shooting). The
            // survivors' guns are finite and are refilled from the stockpile only when they run low in the base or at an
            // outpost (ScAmmo), which opens a bundle when it needs one: today's "ammo used" is the bundles opened since
            // the last rollover, and "out of ammo" means a resupply found the stockpile empty
            int ammoUsed = c.AmmoOpened;
            bool dryAmmo = c.AmmoShort;
            c.AmmoOpened = 0;
            c.AmmoShort = false;
            // round md1 (decision 23): the Meds members' own restocks took since the last rollover (ScMedic.Restock), shown
            // apart from the injury Meds above
            int restocked = c.MedsRestocked;
            c.MedsRestocked = 0;

            if (summary.Length > 0) summary.Append(' ');     // runs and outposts report first
            summary.Append(string.Format(Localization.Get("scDailyUse"), foodUsed, medsUsed, ammoUsed, waterUsed));
            if (restocked > 0) summary.Append(' ').Append(string.Format(Localization.Get("scDailyMedsRestock"), restocked));
            if (harvested > 0) summary.Append(' ').Append(string.Format(Localization.Get("scDailyHarvest"), harvested));
            if (drawn > 0) summary.Append(' ').Append(string.Format(Localization.Get("scDailyWells"), drawn));
            if (hungry) summary.Append(' ').Append(Localization.Get("scDailyHungry"));
            if (thirsty) summary.Append(' ').Append(Localization.Get("scDailyThirsty"));
            if (stillInjured > 0) summary.Append(' ').Append(string.Format(Localization.Get("scDailyInjured"), stillInjured));
#if SC_V34
            if (selfHealed > 0) summary.Append(' ').Append(string.Format(Localization.Get("scDailySelfHealed"), selfHealed));   // round m1t
#endif
            if (dryAmmo) summary.Append(' ').Append(Localization.Get("scDailyNoAmmo"));
        }

        /// <summary>
        /// Whole bundles of each resource the next rollover will want (the Home Status panel shows a stock below it in red).
        /// Round ie1: every figure is the rollover's own function (ScEconomy), so the panel and the rollover agree:
        ///   food      - ScEconomy.FoodEaten (the members who have arrived, their traits, the cook), less ScEconomy.Harvest;
        ///   meds      - one bundle per healsPerBundle injured members the outpost clinic does not cover;
        ///   ammo      - no upkeep (D3): 1 while any member at home fights with a gun, so an empty stockpile (no resupply)
        ///               shows red;
        ///   materials - ScEconomy.Upkeep, the producing outposts' upkeep by level;
        ///   fuel      - ScEconomy.Haulage of what the rollover will deliver (ScOutposts.ProjectedDelivery);
        ///   water     - ScEconomy.WaterShortfall, what is drunk less what the wells and dew collectors add first;
        ///   luxury    - nothing (only the Activities spend it).
        /// </summary>
        public static int[] NeedForRollover()
        {
            var need = new int[ScNames.ResourceCount];
            if (ScRegistry.Members.Count == 0) return need;
            need[(int)Resource.Water] = ScEconomy.WaterShortfall();
            need[(int)Resource.Food] = Math.Max(0, ScEconomy.FoodEaten() - ScEconomy.Harvest());

            bool gunner = false;
            foreach (var m in ScRegistry.Members.Values)
                if (m.Ranged && m.Status == MemberStatus.Resident) gunner = true;
            need[(int)Resource.Meds] = MedsForRollover();
            need[(int)Resource.Ammo] = gunner ? 1 : 0;

            if (ScRegistry.Outposts.Count > 0)
            {
                int day = ScCommunity.Today + 1;
                need[(int)Resource.Materials] = ScEconomy.Upkeep(day);
                need[(int)Resource.Fuel] = ScEconomy.Haulage(ScOutposts.ProjectedDelivery(day));
            }
            return need;
        }

        /// <summary>
        /// NeedForRollover's meds term: whole Meds bundles the next rollover will want for injured members - one per
        /// healsPerBundle (1 + floor(medic stars x MedicPerStar)) injured members the outpost clinic does not cover. Also the
        /// reserve a member's own medical restock never takes (round md1, ScMedic.Restock, MedsResupplyKeepForInjured). A
        /// pure read.
        /// </summary>
        public static int MedsForRollover()
        {
            if (ScRegistry.Members.Count == 0) return 0;
            Member medic;
            int medicStars = ScSkills.BestSpecialist(Specialty.Medicine, out medic);
            int healsPerBundle = 1 + (int)Math.Floor(medicStars * ScConfig.MedicPerStar);
            int injured = 0;
            foreach (var m in ScRegistry.Members.Values)
                if (m.Injured) injured++;
            if (ScOutpostPerks.PerkActive(Resource.Meds)) injured = Math.Max(0, injured - ScConfig.OutpostClinicHeals);
            return (injured + healsPerBundle - 1) / healsPerBundle;
        }
    }
}
