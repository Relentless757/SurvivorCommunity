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
    /// and the daily consumption the ticker runs.
    /// Round MP3 (multiplayer, part T): an open box belongs to the player who has it open - the server never writes it (a
    /// server write into a box a client has open is thrown away and then overwritten by the client's copy). So the verbs are
    /// split: the ITEM side runs on the machine that has the station open, in its own open grid (Pack, TakeBundles, PutBack,
    /// AddSmall - the host's own box, or a client's), and the STOCKPILE side on the server (CreditDeposit, DebitWithdraw, the
    /// Craftsmanship XP), asked through ScActionsStock's ops. On the host and in single player both halves run back to back,
    /// inline, with the same checks, lines and order as before.
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
                if (st.Parent.GetFeature<TEFeatureLandClaim>() != null && IsOutpostDoor(st.Parent.ToWorldPos())) return st;
                return null;
            }
            catch (Exception) { return null; }
        }

        /// <summary>
        /// Round MP3: whether a land claim is a held outpost's (its box is the stockpile's door out there). The server asks
        /// ScOutposts' flags as before; a client's flags are never built (its copy of the registry has no load hook, MP1), so it
        /// reads the copy's outposts instead - the same test ScOutposts.Rebuild makes (held, this flag).
        /// </summary>
        private static bool IsOutpostDoor(Vector3i claim)
        {
            if (ScOutposts.IsOutpostClaim(claim)) return true;
            if (!ScMirror.Active) return false;
            foreach (Outpost o in ScRegistry.Outposts)
                if (o != null && o.Holding && o.Flag == claim) return true;
            return false;
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

        /// <summary>The Craftsmanship pack threshold for a resource: PointsPerBundle x (1 - PackBonusPerStar x stars), floor 0.6, at least 1.</summary>
        public static int PackThreshold(Resource r, int craftStars)
        {
            float packMult = Math.Max(0.6f, 1f - ScConfig.PackBonusPerStar * craftStars);
            return Math.Max(1, (int)Math.Round(ScConfig.PointsPerBundle[(int)r] * packMult));
        }

        /// <summary>
        /// Packs one category. Returns the number of bundles made (0 with a message key + args).
        /// Round MP3: runs on the machine that has the station open (the host, or a client in its own open grid - the box is
        /// its holder's), with the Craftsmanship stars of the registry or, on a client, of the server's copy; the strip asks
        /// only where ScActionsWindow.RequestRefusal() is null. The craftsmen's XP is the server's: stock.packed
        /// (ScActionsStock.RequestPacked), inline on the host. The overflow goes to the player's own bag (GiveBundles): on a
        /// client that is its own local player.
        /// </summary>
        public static int Pack(TEFeatureStorage st, Resource r, EntityPlayer player, out string message)
        {
            message = null;
            try
            {
                // round MP3: no host gate any more - the item side is whoever has the box open (see above)
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
                // round MP3: on a client the members are the server's copy (status, specialty and stars: a pure read of it)
                int craftStars = ScSkills.SpecialistStars(Specialty.Craftsmanship);
                int threshold = PackThreshold(r, craftStars);
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
                // round MP3: the sizes really given are kept apart (stock.packed checks them against the units)
                int gotLarge = GiveBundles(st, r, BundleSize.Large, large, player);
                int gotMedium = GiveBundles(st, r, BundleSize.Medium, medium, player);
                int gotSmall = GiveBundles(st, r, BundleSize.Small, small, player);
                int made = gotLarge * 3 + gotMedium * 2 + gotSmall;
                st.SetModified();
                // round MP3: the craftsmen's XP (made > 0 and stars > 0, as before) is paid by the server - inline on the host,
                // where it runs right here as it always did
                if (made > 0) ScActionsStock.RequestPacked(st, r, made, gotLarge, gotMedium, gotSmall, threshold);
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
        /// Round MP3: the box must be one this machine has open (the server never writes a box a client has open). The player
        /// may be any player:
        /// - this machine's own (EntityPlayerLocal: the host's, or a client packing in its own grid): their bag, then their
        ///   feet, as before;
        /// - a remote player on the server: their bag is their own machine's. With toRemote given, the stacks go into it, and
        ///   the caller sends them in its reply (ScItemTx.StacksToHint) for that player's machine to add (ScItemTx.GiveToLocal:
        ///   the bag, the rest at their feet); without it they are dropped at the player's feet from here (never lost).
        /// </summary>
        public static int GiveBundles(TEFeatureStorage st, Resource r, BundleSize size, int count, EntityPlayer player, List<ItemStack> toRemote = null)
        {
            if (count <= 0) return 0;
            ItemValue iv = BundleValue(r, size);
            if (iv == null) return 0;
            int stackMax = Math.Max(1, iv.ItemClass.Stacknumber.Value);
            int given = 0;
            EntityPlayerLocal local = player as EntityPlayerLocal;
            while (given < count)
            {
                int n = Math.Min(stackMax, count - given);
                var stack = new ItemStack(iv.Clone(), n);
                if (st != null && st.AddItem(stack)) { given += n; continue; }
                if (local != null && local.PlayerUI != null && local.PlayerUI.xui != null && local.PlayerUI.xui.PlayerInventory != null
                    && local.PlayerUI.xui.PlayerInventory.AddItem(stack)) { given += n; continue; }
                // round MP3: a remote player's bag is reached through the caller's reply
                if (player != null && local == null && toRemote != null) { toRemote.Add(stack); given += n; continue; }
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

        // ---- deposit and withdraw (round MP3: the item side on the box's machine, the stockpile side on the server) ----

        /// <summary>
        /// Deposit, the item side (the machine that has the station open): takes every bundle out of the box, recording each
        /// slot and stack in taken (to put back on a refusal) and the units per resource in counts. Returns the bundles taken
        /// (0 with the message: none in the box, or no grid). The stockpile is credited by stock.deposit (CreditDeposit).
        /// </summary>
        public static int TakeBundles(TEFeatureStorage st, List<KeyValuePair<int, ItemStack>> taken, int[] counts, out string message)
        {
            message = null;
            try
            {
#if SC_V34
                if (st == null || st.ItemGrid == null) { message = Localization.Get("scInternal"); return 0; }
                ItemStack[] items = st.ItemGrid.items;
#else
                if (st == null || st.items == null) { message = Localization.Get("scInternal"); return 0; }
                ItemStack[] items = st.items;
#endif
                int moved = 0;
                for (int i = 0; i < items.Length; i++)
                {
                    Resource r;
                    int units;
                    if (!IsBundle(items[i], out r, out units)) continue;
                    counts[(int)r] += items[i].count * units;
                    moved += items[i].count;
                    if (taken != null) taken.Add(new KeyValuePair<int, ItemStack>(i, items[i].Clone()));
                    st.UpdateSlot(i, ItemStack.Empty.Clone());
                }
                if (moved == 0) { message = Localization.Get("scNoBundles"); return 0; }
                st.SetModified();
                return moved;
            }
            catch (Exception e)
            {
                ScLog.Once("deposit", e);
                message = Localization.Get("scInternal");
                return 0;
            }
        }

        /// <summary>
        /// Deposit refused (the machine that took them): the bundles go back - into their own slots, else any empty slot,
        /// while boxHeld (this machine still has that box open); the rest (all of them when it does not) into the player's
        /// bag, then at their feet (ScItemTx.GiveToLocal). Returns how many bundles did not go back into the box.
        /// </summary>
        public static int PutBack(TEFeatureStorage st, List<KeyValuePair<int, ItemStack>> taken, EntityPlayerLocal player, bool boxHeld)
        {
            if (taken == null || taken.Count == 0) return 0;
            var rest = new List<ItemStack>();
            try
            {
                ItemStack[] items = boxHeld && st != null && st.ItemGrid != null ? st.ItemGrid.items : null;
                bool changed = false;
                foreach (var kv in taken)
                {
                    ItemStack s = kv.Value;
                    if (s == null || s.IsEmpty()) continue;
                    try        // round MP3 (review m3): a try per stack - one slot that throws goes to the player, the rest still go back
                    {
                        if (items != null && kv.Key >= 0 && kv.Key < items.Length && (items[kv.Key] == null || items[kv.Key].IsEmpty()))
                        {
                            st.UpdateSlot(kv.Key, s.Clone());
                            changed = true;
                            continue;
                        }
                        if (items != null && st.AddItem(s.Clone())) { changed = true; continue; }
                    }
                    catch (Exception e)
                    {
                        ScLog.Once("deposit-putback-slot", e);
                    }
                    rest.Add(s);
                }
                if (changed) st.SetModified();
            }
            catch (Exception e)
            {
                ScLog.Once("deposit-putback", e);
            }
            int n = 0;
            foreach (ItemStack s in rest) n += s.count;
            if (rest.Count > 0) ScItemTx.GiveToLocal(player, rest);
            return n;
        }

        /// <summary>
        /// Deposit, the stockpile side (the server, stock.deposit): the units in, saved; the old log line (a remote player's
        /// deposit names them: by).
        /// </summary>
        public static void CreditDeposit(int[] counts, int moved, string by = null)
        {
            for (int i = 0; i < ScNames.ResourceCount; i++) ScRegistry.Community.Add((Resource)i, counts[i]);
            ScRegistry.SaveAtomic();
            ScLog.Out("deposited " + moved + " bundle(s) (" + ScOutposts.Amounts(counts) + ")" + (by != null ? " by " + by : "") + ": " + StockText());
        }

        /// <summary>Withdraw, before asking: the box has an empty slot (st.AddItem's test).</summary>
        public static bool HasRoom(TEFeatureStorage st)
        {
            ItemStack[] items = st != null && st.ItemGrid != null ? st.ItemGrid.items : null;
            if (items == null) return false;
            for (int i = 0; i < items.Length; i++) if (items[i] == null || items[i].IsEmpty()) return true;
            return false;
        }

        /// <summary>
        /// Withdraw, the stockpile side (the server, stock.withdraw): one unit out, saved - or false with the reason as a
        /// localization key (scStockEmpty takes the resource's name; the strip formats it), checked in the old order: the
        /// stock, the bundle item, the box's room (as the asking machine saw it).
        /// </summary>
        public static bool DebitWithdraw(Resource r, bool room, out string why)
        {
            why = null;
            if (ScRegistry.Community.Get(r) <= 0) { why = "scStockEmpty"; return false; }
            if (BundleValue(r, BundleSize.Small) == null) { why = "scInternal"; return false; }
            if (!room) { why = "scBoxFull"; return false; }
            ScRegistry.Community.Add(r, -1);
            ScRegistry.SaveAtomic();
            return true;
        }

        /// <summary>Withdraw, the item side (the machine that has the box open, after the server's debit): one Small bundle in.</summary>
        public static bool AddSmall(TEFeatureStorage st, Resource r)
        {
            ItemValue iv = st != null ? BundleValue(r, BundleSize.Small) : null;
            if (iv == null || !st.AddItem(new ItemStack(iv.Clone(), 1))) return false;
            st.SetModified();
            return true;
        }

        /// <summary>A Small bundle of r (or null), for a withdrawal that ends in the player's bag.</summary>
        public static ItemStack SmallStack(Resource r)
        {
            ItemValue iv = BundleValue(r, BundleSize.Small);
            return iv != null ? new ItemStack(iv.Clone(), 1) : null;
        }

        public static string StockText()
        {
            Community c = ScRegistry.Community;
            return string.Format(Localization.Get("scStockText"), c.Get(Resource.Food), c.Get(Resource.Meds), c.Get(Resource.Ammo), c.Get(Resource.Materials), c.Get(Resource.Fuel),
                                 c.Get(Resource.Water), c.Get(Resource.Luxury));
        }

        /// <summary>Round MP3: the same line from given figures (one per Resource): a client shows the server's answer at once.</summary>
        public static string StockText(int[] n)
        {
            if (n == null || n.Length < ScNames.ResourceCount) return StockText();
            return string.Format(Localization.Get("scStockText"), n[(int)Resource.Food], n[(int)Resource.Meds], n[(int)Resource.Ammo], n[(int)Resource.Materials],
                                 n[(int)Resource.Fuel], n[(int)Resource.Water], n[(int)Resource.Luxury]);
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

            // Gardening (round fd1): living crops in the zone (last scan) feed the stockpile - 1 food per CropsPerFood crops,
            // lowered by the best gardener at home (ScEconomy.Harvest), the remainder dropped, never carried; no gardener needed.
            // Every gardener at home still learns from tending them.
            int harvested = ScEconomy.Harvest();
            if (c.Crops > 0)
            {
                if (harvested > 0) c.Add(Resource.Food, harvested);
                foreach (var g in ScRegistry.Members.Values)
                    if (g.Specialty == Specialty.Gardening && g.IsHome && g.Status != MemberStatus.OnRun) ScSkills.AddSpecialtyXp(g, 2f);
            }

            // food (round ie1): ScEconomy.FoodEaten - MemberFoodPerDay a member who has arrived (a trait's eats= instead), less
            // the best cook's ceil(stars / CookStarsPerFood), never more than half. Whole units: a short day is a hungry day,
            // nothing is owed or carried to the next
            int foodRaw, cookSaved, rationCut;
            Member cook;
            // round fd1: the rollover into `day` closes day - 1; rationing enacted that day eats RationShare of it
            int foodWhole = ScEconomy.FoodEaten(day, out foodRaw, out cookSaved, out cook, out rationCut);
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
            if (rationCut > 0) summary.Append(' ').Append(string.Format(Localization.Get("scDailyRationed"), rationCut));   // round fd1
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
            // round MP1: a client shows the server's figures (the outposts' delivery reads the server's power and runtime state)
            if (ScMirror.Active)
            {
                int[] mn = ScMirror.Ints("need", ScNames.ResourceCount);
                if (mn != null) return mn;
            }
            var need = new int[ScNames.ResourceCount];
            // the review of ie1 (L3): the outposts' upkeep and hauling are due with or without members
            if (ScRegistry.Outposts.Count > 0)
            {
                int day = ScCommunity.Today + 1;
                need[(int)Resource.Materials] = ScEconomy.Upkeep(day);
                need[(int)Resource.Fuel] = ScEconomy.Haulage(ScOutposts.ProjectedDelivery(day));
            }
            if (ScRegistry.Members.Count == 0) return need;
            need[(int)Resource.Water] = ScEconomy.WaterShortfall();
            need[(int)Resource.Food] = Math.Max(0, ScEconomy.FoodEaten() - ScEconomy.Harvest());

            bool gunner = false;
            foreach (var m in ScRegistry.Members.Values)
                if (m.Ranged && m.Status == MemberStatus.Resident) gunner = true;
            need[(int)Resource.Meds] = MedsForRollover();
            need[(int)Resource.Ammo] = gunner ? 1 : 0;
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
