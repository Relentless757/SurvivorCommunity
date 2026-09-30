using System;

namespace SurvivorCommunity
{
    /// <summary>
    /// Which of the five resources an item counts as, and what it is worth in packing points.
    /// Food = tags food/drinks; Meds = medical; Ammo = ammo; Fuel = the gasoline tag or a FuelItems entry
    /// (NOT FuelValue: wood, coal, cloth and leather all burn but are building materials); Materials =
    /// Group "Resources" or a resource* name. Bundles themselves never count. Points = EconomicValue x count
    /// unless ValueOverrides names the item.
    /// </summary>
    public static class ScCategories
    {
        private static readonly FastTags<TagGroup.Global> FoodTags = FastTags<TagGroup.Global>.Parse("food,drinks");
        private static readonly FastTags<TagGroup.Global> MedTags = FastTags<TagGroup.Global>.Parse("medical");
        private static readonly FastTags<TagGroup.Global> AmmoTags = FastTags<TagGroup.Global>.Parse("ammo");
        private static readonly FastTags<TagGroup.Global> FuelTags = FastTags<TagGroup.Global>.Parse("gasoline");
        private static readonly FastTags<TagGroup.Global> BundleTags = FastTags<TagGroup.Global>.Parse("scbundle");

        /// <summary>The resource an item belongs to, or false for anything else.</summary>
        public static bool Classify(ItemClass ic, out Resource r)
        {
            r = Resource.Food;
            if (ic == null) return false;
            try
            {
                if (ic.HasAnyTags(BundleTags)) return false;
                if (ic.HasAnyTags(FoodTags)) { r = Resource.Food; return true; }
                if (ic.HasAnyTags(MedTags)) { r = Resource.Meds; return true; }
                if (ic.HasAnyTags(AmmoTags)) { r = Resource.Ammo; return true; }
                string name = ic.Name ?? "";
                if (ic.HasAnyTags(FuelTags)) { r = Resource.Fuel; return true; }
                ScConfig.EnsureParsed();
                for (int i = 0; i < ScConfig.FuelItems.Count; i++)
                {
                    if (string.Equals(ScConfig.FuelItems[i], name, StringComparison.OrdinalIgnoreCase)) { r = Resource.Fuel; return true; }
                }
                if (ic.Groups != null)
                {
                    for (int i = 0; i < ic.Groups.Length; i++)
                    {
                        if (ic.Groups[i] != null && ic.Groups[i].IndexOf("Resources", StringComparison.OrdinalIgnoreCase) >= 0) { r = Resource.Materials; return true; }
                    }
                }
                if (name.StartsWith("resource", StringComparison.OrdinalIgnoreCase)) { r = Resource.Materials; return true; }
            }
            catch (Exception e)
            {
                ScLog.Once("classify", e);
            }
            return false;
        }

        public static bool Is(ItemClass ic, Resource want)
        {
            Resource r;
            return Classify(ic, out r) && r == want;
        }

        /// <summary>Packing points for ONE item of this class.</summary>
        public static int PointsEach(ItemClass ic)
        {
            if (ic == null) return 0;
            int v;
            if (ScConfig.ValueOverrides.TryGetValue(ic.Name ?? "", out v)) return Math.Max(0, v);
            try { return Math.Max(1, (int)ic.EconomicValue); }
            catch (Exception) { return 1; }
        }

        /// <summary>Counts items of a category across the player's backpack and toolbelt.</summary>
        public static int CountInPlayer(EntityPlayer player, Resource r)
        {
            int n = 0;
            try
            {
                if (player == null) return 0;
                ItemStack[] bag = player.bag != null ? player.bag.GetSlots() : null;
                if (bag != null) for (int i = 0; i < bag.Length; i++) n += Count(bag[i], r);
                ItemStack[] belt = player.inventory != null ? player.inventory.GetSlots() : null;
                if (belt != null) for (int i = 0; i < belt.Length; i++) n += Count(belt[i], r);
            }
            catch (Exception e)
            {
                ScLog.Once("count-player", e);
            }
            return n;
        }

        private static int Count(ItemStack s, Resource r)
        {
            if (s == null || s.IsEmpty() || s.itemValue == null) return 0;
            return Is(s.itemValue.ItemClass, r) ? s.count : 0;
        }

        /// <summary>Removes up to 'count' items of a category from the player (backpack first). Returns what was removed.</summary>
        public static int RemoveFromPlayer(EntityPlayerLocal player, Resource r, int count)
        {
            int removed = 0;
            try
            {
                if (player == null || count <= 0) return 0;
                XUiM_PlayerInventory inv = (player.PlayerUI != null && player.PlayerUI.xui != null) ? player.PlayerUI.xui.PlayerInventory : null;
                if (inv == null) return 0;
                var take = new System.Collections.Generic.List<ItemStack>();
                ItemStack[][] sources = { player.bag != null ? player.bag.GetSlots() : null, player.inventory != null ? player.inventory.GetSlots() : null };
                for (int src = 0; src < sources.Length && removed < count; src++)
                {
                    ItemStack[] slots = sources[src];
                    if (slots == null) continue;
                    for (int i = 0; i < slots.Length && removed < count; i++)
                    {
                        ItemStack s = slots[i];
                        if (s == null || s.IsEmpty() || s.itemValue == null || !Is(s.itemValue.ItemClass, r)) continue;
                        int n = Math.Min(s.count, count - removed);
                        take.Add(new ItemStack(s.itemValue.Clone(), n));
                        removed += n;
                    }
                }
                if (take.Count == 0) return 0;
                // report what actually left the inventory, not what was asked for (vanilla skips the last
                // toolbelt slot when removing and silently no-ops when a stack is missing)
                int before = CountInPlayer(player, r);
                inv.RemoveItems(take);
                int after = CountInPlayer(player, r);
                removed = Math.Max(0, before - after);
            }
            catch (Exception e)
            {
                ScLog.Once("remove-player", e);
            }
            return removed;
        }
    }
}
