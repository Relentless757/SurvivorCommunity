using System;
using System.Collections.Generic;
using System.Text;

namespace SurvivorCommunity
{
    /// <summary>
    /// The stockpile (five numbers in the registry) and the packing station's verbs on its container:
    ///   Pack     - turns the loose items of one category in the box into whole bundles (points = EconomicValue
    ///              x count, PointsPerBundle&lt;Resource&gt; per bundle; the highest-value stacks go first and
    ///              anything not needed for a whole bundle stays in the box; a small overshoot on the last
    ///              stack is accepted)
    ///   Deposit  - moves every bundle in the box into the stockpile (bundles only exist in transit)
    ///   Withdraw - one bundle from the stockpile back into the box
    /// and the daily consumption the ticker runs. Host only.
    /// </summary>
    public static class ScStockpile
    {
        private static readonly FastTags<TagGroup.Global> BundleTags = FastTags<TagGroup.Global>.Parse("scbundle");

        /// <summary>
        /// The storage feature of an open packing station - or of an outpost's land claim, whose box is the
        /// stockpile's door out there - or null for any other container.
        /// </summary>
        public static TEFeatureStorage StationOf(ITileEntityLootable te)
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

        public static bool IsBundle(ItemStack s, out Resource r)
        {
            r = Resource.Food;
            if (s == null || s.IsEmpty() || s.itemValue == null || s.itemValue.ItemClass == null) return false;
            ItemClass ic = s.itemValue.ItemClass;
            if (!ic.HasAnyTags(BundleTags)) return false;
            string name = ic.Name ?? "";
            for (int i = 0; i < 5; i++) if (name == ScNames.BundleItem((Resource)i)) { r = (Resource)i; return true; }
            return false;
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
                ItemStack[] items = st.items;
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

                int made = GiveBundles(st, r, bundles, player);
                st.SetModified();
                if (made > 0 && craftStars > 0)
                {
                    foreach (var cm in ScRegistry.Members.Values)
                        if (cm.Specialty == Specialty.Craftsmanship && cm.IsHome && cm.Status != MemberStatus.OnRun) ScSkills.AddSpecialtyXp(cm, 1f);
                }
                message = string.Format(Localization.Get("scPacked"), made, Localization.Get(ScNames.LocKey(r)));
                ScLog.Out("packed " + made + " " + r + " bundle(s) from " + total + " points at the station");
                return made;
            }
            catch (Exception e)
            {
                ScLog.Once("pack", e);
                message = Localization.Get("scInternal");
                return 0;
            }
        }

        /// <summary>Bundles into the box; what does not fit goes to the player, then the ground.</summary>
        private static int GiveBundles(TEFeatureStorage st, Resource r, int count, EntityPlayerLocal player)
        {
            ItemValue iv = ItemClass.GetItem(ScNames.BundleItem(r), true);
            if (iv == null || iv.IsEmpty()) { ScLog.Err("bundle item " + ScNames.BundleItem(r) + " is not defined - Config/items.xml did not apply"); return 0; }
            int stackMax = Math.Max(1, iv.ItemClass.Stacknumber.Value);
            int given = 0;
            while (given < count)
            {
                int n = Math.Min(stackMax, count - given);
                var stack = new ItemStack(iv.Clone(), n);
                if (st.AddItem(stack)) { given += n; continue; }
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

        /// <summary>Moves every bundle in the box into the stockpile. Returns the number of bundles moved.</summary>
        public static int Deposit(TEFeatureStorage st, out string message)
        {
            message = null;
            try
            {
                if (!ScCommunity.IsHost) { message = Localization.Get("scHostOnly"); return 0; }
                if (st == null || st.items == null) { message = Localization.Get("scInternal"); return 0; }
                ItemStack[] items = st.items;
                int moved = 0;
                var counts = new int[5];
                for (int i = 0; i < items.Length; i++)
                {
                    Resource r;
                    if (!IsBundle(items[i], out r)) continue;
                    counts[(int)r] += items[i].count;
                    moved += items[i].count;
                    st.UpdateSlot(i, ItemStack.Empty.Clone());
                }
                if (moved == 0) { message = Localization.Get("scNoBundles"); return 0; }
                for (int i = 0; i < 5; i++) ScRegistry.Community.Add((Resource)i, counts[i]);
                st.SetModified();
                ScRegistry.SaveAtomic();
                message = string.Format(Localization.Get("scDeposited"), moved);
                ScLog.Out("deposited " + moved + " bundle(s): " + StockText());
                return moved;
            }
            catch (Exception e)
            {
                ScLog.Once("deposit", e);
                message = Localization.Get("scInternal");
                return 0;
            }
        }

        /// <summary>One bundle from the stockpile into the box.</summary>
        public static bool Withdraw(TEFeatureStorage st, Resource r, out string message)
        {
            message = null;
            try
            {
                if (!ScCommunity.IsHost) { message = Localization.Get("scHostOnly"); return false; }
                if (st == null) { message = Localization.Get("scInternal"); return false; }
                if (ScRegistry.Community.Get(r) <= 0) { message = string.Format(Localization.Get("scStockEmpty"), Localization.Get(ScNames.LocKey(r))); return false; }
                ItemValue iv = ItemClass.GetItem(ScNames.BundleItem(r), true);
                if (iv == null || iv.IsEmpty()) { message = Localization.Get("scInternal"); return false; }
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
            return string.Format(Localization.Get("scStockText"), c.Get(Resource.Food), c.Get(Resource.Meds), c.Get(Resource.Ammo), c.Get(Resource.Materials), c.Get(Resource.Fuel));
        }

        // ---- daily use ----

        /// <summary>
        /// One day of the community: food for every member (fractions carry over), meds for injured members
        /// (one per injury, healing it), and the day's ammo resupplies reported (no ammo upkeep). Appends a summary line.
        /// </summary>
        public static void DailyConsume(int day, StringBuilder summary)
        {
            Community c = ScRegistry.Community;
            int members = ScRegistry.Members.Count;
            if (members == 0) { c.Hungry = false; c.AmmoOpened = 0; c.AmmoShort = false; return; }

            // Gardening: living crops in the zone (last scan) feed the stockpile through the gardeners
            int gardenStars = ScSkills.SpecialistStars(Specialty.Gardening);
            int harvested = 0;
            if (gardenStars > 0 && c.Crops > 0)
            {
                c.HarvestCarry += c.Crops * ScConfig.CropFoodPerStar * gardenStars;
                harvested = (int)Math.Floor(c.HarvestCarry);
                c.HarvestCarry -= harvested;
                if (harvested > 0) c.Add(Resource.Food, harvested);
                foreach (var g in ScRegistry.Members.Values)
                    if (g.Specialty == Specialty.Gardening && g.IsHome && g.Status != MemberStatus.OnRun) ScSkills.AddSpecialtyXp(g, 2f);
            }

            // food: FoodPerMemberDay bundles per member, trait multipliers, the best cook's discount, fractional carry
            Member cook;
            int cookStars = ScSkills.BestSpecialist(Specialty.Cooking, out cook);
            float cookMult = Math.Max(0.5f, 1f - ScConfig.CookFoodPerStar * cookStars);
            float foodNeed = c.FoodCarry;
            foreach (var m in ScRegistry.Members.Values) foodNeed += ScConfig.FoodPerMemberDay * ScTraits.FoodMult(m) * cookMult;
            int foodWhole = (int)Math.Floor(foodNeed);
            c.FoodCarry = foodNeed - foodWhole;
            int foodHad = c.Get(Resource.Food);
            int foodUsed = Math.Min(foodWhole, foodHad);
            c.Add(Resource.Food, -foodUsed);
            bool hungry = foodUsed < foodWhole;
            if (hungry) c.FoodCarry = 0f;           // an unpaid day is simply a hungry day, not a growing debt
            c.Hungry = hungry;

            // meds: one bundle heals 1 + floor(medic stars x MedicPerStar) injuries
            if (cookStars > 0 && foodUsed > 0 && cook != null) ScSkills.AddSpecialtyXp(cook, 2f);
            Member medic;
            int medicStars = ScSkills.BestSpecialist(Specialty.Medicine, out medic);
            int healsPerBundle = 1 + (int)Math.Floor(medicStars * ScConfig.MedicPerStar);
            int medsUsed = 0, stillInjured = 0, healsLeft = 0, healed = 0;
            int clinic = ScOutpostPerks.PerkActive(Resource.Meds) ? ScConfig.OutpostClinicHeals : 0;   // a medical outpost's field clinic
            foreach (var m in ScRegistry.Members.Values)
            {
                if (!m.Injured) continue;
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

            if (summary.Length > 0) summary.Append(' ');     // runs and outposts report first
            summary.Append(string.Format(Localization.Get("scDailyUse"), foodUsed, medsUsed, ammoUsed));
            if (harvested > 0) summary.Append(' ').Append(string.Format(Localization.Get("scDailyHarvest"), harvested));
            if (hungry) summary.Append(' ').Append(Localization.Get("scDailyHungry"));
            if (stillInjured > 0) summary.Append(' ').Append(string.Format(Localization.Get("scDailyInjured"), stillInjured));
            if (dryAmmo) summary.Append(' ').Append(Localization.Get("scDailyNoAmmo"));
        }
    }
}
