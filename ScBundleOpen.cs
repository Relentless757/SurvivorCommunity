using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Scripting;

// Item action classes resolve as "ItemAction" + the Class property, so Config/items.xml's Action0 Class="ScOpenBundle,
// SurvivorCommunity" is this class in the global namespace (ReflectionHelpers.GetTypeWithPrefix in ItemClassesFromXml).

/// <summary>
/// Round 33b (B33): the Open action of every SC bundle (Config/items.xml, scBundleMaster's Action0). It IS vanilla's
/// ItemActionOpenBundle - read the same way (Delay, Sound_start), so the game treats it as a bundle: the backpack's and
/// toolbelt's Open entry (XUiC_ItemActionList.AddActionActions, ItemActionEntryUse ConsumeType.Open, which takes ONE bundle
/// off the stack and hands it here) and a click while holding it (ItemActionOpenBundle.ExecuteAction, on release) - and only
/// what one bundle turns into is SC's: ScBundleOpen.Open.
/// </summary>
[Preserve]
public class ItemActionScOpenBundle : ItemActionOpenBundle
{
    public override bool ExecuteInstantAction(EntityAlive ent, ItemStack stack, bool isHeldItem, XUiC_ItemStack stackController)
    {
        return SurvivorCommunity.ScBundleOpen.Open(this, ent, stack, isHeldItem, stackController);
    }
}

namespace SurvivorCommunity
{
    /// <summary>
    /// Round 33b (B33): opening a bundle. One bundle becomes a random assortment of its resource's items from that resource's
    /// pool (BundleOpen&lt;Resource&gt;), worth about BundleOpenValueShare x the bundle's packing value (units x
    /// PointsPerBundle&lt;Resource&gt;) by the measure the packing station counts (ScCategories.PointsEach), never more than
    /// OverTarget above that and at least one item. Items reach the player as vanilla's bundles give them
    /// (ItemActionOpenBundle.ExecuteInstantAction): the player's inventory (XUiM_PlayerInventory.AddItem, which shows the
    /// collected-items list and plays the pick-up sound), and on the ground at their feet what does not fit - on the client
    /// that opens it, as vanilla. BundleOpen false (or a pool with no usable item): Apply takes the Open action off those
    /// bundles at world start, so they are as before round 33b.
    /// </summary>
    public static class ScBundleOpen
    {
        /// <summary>An assortment may come out this share above its target, never more (the item values are coarse).</summary>
        public const float OverTarget = 0.10f;
        /// <summary>Each pick asks for this share of the target (a random point between the two), in one item's stack.</summary>
        public const float ChunkMin = 0.15f, ChunkMax = 0.45f;
        /// <summary>Assortments tried before the closest one under the target is taken; picks per assortment at most.</summary>
        public const int Attempts = 24, MaxPicks = 40;

        /// <summary>One resource's pool, resolved against this world's items.</summary>
        private sealed class Pool
        {
            public readonly List<string> Names = new List<string>();
            public readonly List<ItemValue> Items = new List<ItemValue>();
            public int[] Points;
            public float[] Weights;
            public int[] StackMax;
            public int Count { get { return Names.Count; } }
        }

        /// <summary>This world's pools, per resource (null until first asked).</summary>
        private static Pool[] pools;
        /// <summary>The Open actions Apply took off (BundleOpen false), by item name, so a later world with it on can put them back.</summary>
        private static readonly Dictionary<string, ItemAction> stripped = new Dictionary<string, ItemAction>(StringComparer.Ordinal);
        /// <summary>The description keys Apply swapped for the closed text, by item name.</summary>
        private static readonly Dictionary<string, string> openKeys = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Per-world reset (from ScConfig.ResetBundles at GameStarting): the pools are resolved again.</summary>
        public static void Reset() { pools = null; }

        // ---- the assortment (pure: the offline harness runs it on the built DLL) ----

        /// <summary>
        /// The share of a bundle's packing value an opened one holds: BundleOpenValueShare, cut to BundleOpenShareMax x the
        /// packing multiplier of EVERY craftsman in the community (home or not): the packing station's threshold falls by
        /// PackBonusPerStar per star of the craftsmen at home (floor 0.6), and an opened bundle may hold at most OverTarget
        /// above its share, so share x (1 + OverTarget) stays below what packing it cost - no loop gains.
        /// </summary>
        public static float ShareCap(float share, int craftStarsAll, float bonusPerStar)
        {
            float packFloor = Math.Max(0.6f, 1f - bonusPerStar * Math.Max(0, craftStarsAll));
            return Math.Max(0f, Math.Min(share, ScConfig.BundleOpenShareMax * packFloor));
        }

        /// <summary>The target points of one bundle: units x points per unit x the share, at least 1.</summary>
        public static int TargetPoints(int units, int pointsPerUnit, float share)
        {
            return Math.Max(1, (int)Math.Round((double)Math.Max(1, units) * Math.Max(1, pointsPerUnit) * share));
        }

        /// <summary>
        /// Picks counts of the pool's entries worth target..target x (1 + OverTarget) points: weighted picks among the entries
        /// that still fit, each a chunk of ChunkMin..ChunkMax of the target (rounded to fives / tens from 10 / 20 up), the last
        /// one only what finishes it (rounded up the same way when that still fits), every entry at most one stack
        /// (stackMax). Attempts assortments; the first in range wins, else the closest under the target. Nothing fits at all
        /// (every entry above the ceiling): one of the cheapest. Returns the count per entry; value = their points.
        /// </summary>
        public static int[] Assemble(int[] points, float[] weights, int[] stackMax, int target, Func<float> next01, out int value)
        {
            int n = points.Length;
            var best = new int[n];
            int bestValue = 0;
            bool have = false;
            target = Math.Max(1, target);
            int cap = target + (int)Math.Floor(target * OverTarget);
            for (int attempt = 0; attempt < Attempts; attempt++)
            {
                var counts = new int[n];
                int got = 0;
                for (int pick = 0; pick < MaxPicks && got < target; pick++)
                {
                    int room = cap - got;
                    float sum = 0f;
                    for (int i = 0; i < n; i++)
                        if (Fits(points, weights, stackMax, counts, i, room)) sum += weights[i];
                    if (sum <= 0f) break;
                    float roll = next01() * sum;
                    int k = -1;
                    for (int i = 0; i < n; i++)
                    {
                        if (!Fits(points, weights, stackMax, counts, i, room)) continue;
                        k = i;                       // the last fitting entry catches a roll at the very top
                        roll -= weights[i];
                        if (roll < 0f) break;
                    }
                    if (k < 0) break;
                    int ev = points[k];
                    int most = Math.Min(room / ev, Math.Max(1, stackMax[k]) - counts[k]);
                    int need = target - got;
                    int c = (int)Math.Round(target * (ChunkMin + (ChunkMax - ChunkMin) * next01()) / ev);
                    if ((long)c * ev >= need)
                    {
                        c = (need + ev - 1) / ev;    // this pick finishes it: no more than that ...
                        int up = NiceUp(c);
                        if (up <= most) c = up;      // ... but a round number when that still fits
                    }
                    else c = NiceDown(c);
                    c = Math.Max(1, Math.Min(c, most));
                    counts[k] += c;
                    got += c * ev;
                }
                if (got >= target && got <= cap) { value = got; return counts; }
                if (!have || Math.Abs(target - got) < Math.Abs(target - bestValue))
                {
                    Array.Copy(counts, best, n);
                    bestValue = got;
                    have = true;
                }
            }
            if (bestValue <= 0 && n > 0)
            {
                // every entry is above the ceiling: at least one item, the cheapest
                int cheap = -1;
                for (int i = 0; i < n; i++)
                    if (points[i] > 0 && weights[i] > 0f && (cheap < 0 || points[i] < points[cheap])) cheap = i;
                if (cheap >= 0)
                {
                    Array.Clear(best, 0, n);
                    best[cheap] = 1;
                    bestValue = points[cheap];
                }
            }
            value = bestValue;
            return best;
        }

        private static bool Fits(int[] points, float[] weights, int[] stackMax, int[] counts, int i, int room)
        {
            return points[i] > 0 && points[i] <= room && weights[i] > 0f && counts[i] < Math.Max(1, stackMax[i]);
        }

        /// <summary>A count rounded down to a multiple of 10 from 20, of 5 from 10 (smaller ones as they are).</summary>
        public static int NiceDown(int c)
        {
            if (c >= 20) return c - c % 10;
            if (c >= 10) return c - c % 5;
            return c;
        }

        /// <summary>A count rounded up to a multiple of 10 from 20, of 5 from 10.</summary>
        public static int NiceUp(int c)
        {
            if (c >= 20) return c % 10 == 0 ? c : c + 10 - c % 10;
            if (c >= 10) return c % 5 == 0 ? c : c + 5 - c % 5;
            return c;
        }

        // ---- the pools ----

        /// <summary>
        /// A resource's pool for this world: the config's entries that exist, pack as that resource (ScCategories.Classify,
        /// the packing station's rule), are worth points, are no dev, test, hidden or quest item and have no quality (a tool or
        /// weapon). Each entry that fails is warned about once and skipped (modded installs differ); one named twice adds its
        /// weights.
        /// </summary>
        private static Pool PoolFor(Resource r)
        {
            if (pools == null) pools = new Pool[ScNames.ResourceCount];
            Pool pool = pools[(int)r];
            if (pool != null) return pool;
            ScConfig.EnsureParsed();
            pool = new Pool();
            var names = new List<string>();
            var weights = new List<float>();
            var problems = new List<string>();
            string key = ScConfig.BundleOpenPoolKey(r);
            ScConfig.ParseBundlePool(ScConfig.BundleOpenPools[(int)r], names, weights, problems);
            foreach (string p in problems) ScLog.Once("bundleopen-parse-" + key + p, "config " + key + ": " + p + " - skipped");
            var points = new List<int>();
            var wts = new List<float>();
            var max = new List<int>();
            for (int i = 0; i < names.Count; i++)
            {
                string why = null;
                ItemClass ic = null;
                try { ic = ItemClass.GetItemClass(names[i], true); }
                catch (Exception) { ic = null; }
                Resource got;
                if (ic == null) why = "no such item";
                else if (!ScCategories.Classify(ic, out got)) why = "it packs as no resource";
                else if (got != r) why = "it packs as " + got + ", not " + r;
                else if (ic.CreativeMode == EnumCreativeMode.Dev || ic.CreativeMode == EnumCreativeMode.Test || ic.CreativeMode == EnumCreativeMode.None) why = "it is a dev, test or hidden item (CreativeMode " + ic.CreativeMode + ")";
                else if (IsQuestItem(ic)) why = "it is a quest item";
                else if (ic.HasQuality) why = "it has a quality (a tool, weapon or armor, not a supply)";
                else if (ScCategories.PointsEach(ic) <= 0) why = "it is worth 0 points (ValueOverrides)";
                if (why != null)
                {
                    ScLog.Once("bundleopen-skip-" + key + names[i], "config " + key + ": '" + names[i] + "' is skipped - " + why);
                    continue;
                }
                int at = pool.Names.IndexOf(ic.Name);
                if (at >= 0) { wts[at] += weights[i]; continue; }
                pool.Names.Add(ic.Name);
                pool.Items.Add(new ItemValue(ic.Id));
                points.Add(ScCategories.PointsEach(ic));
                wts.Add(weights[i]);
                max.Add(Math.Max(1, ic.Stacknumber != null ? ic.Stacknumber.Value : 1));
            }
            pool.Points = points.ToArray();
            pool.Weights = wts.ToArray();
            pool.StackMax = max.ToArray();
            pools[(int)r] = pool;
            return pool;
        }

        /// <summary>The quest tag, in a holder of its own so ScBundleOpen's static constructor touches no game type (the offline harness runs Assemble).</summary>
        private static class Tags
        {
            internal static readonly FastTags<TagGroup.Global> Quest = FastTags<TagGroup.Global>.Parse("quest");
        }

        private static bool IsQuestItem(ItemClass ic)
        {
            if (ic.HasAnyTags(Tags.Quest)) return true;
            string n = ic.Name ?? "";
            return n.StartsWith("quest", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The share for an open now: the setting, cut by ShareCap with every craftsman member's stars.</summary>
        public static float ShareNow()
        {
            int stars = 0;
            try
            {
                foreach (Member m in ScRegistry.Members.Values)
                    if (m != null && m.Specialty == Specialty.Craftsmanship) stars += Math.Max(0, m.SpecLevel);
            }
            catch (Exception e) { ScLog.Once("bundleopen-stars", e); }
            return ShareCap(ScConfig.BundleOpenValueShare, stars, ScConfig.PackBonusPerStar);
        }

        // ---- the world-start pass ----

        /// <summary>
        /// At GameStartDone: every bundle item of a resource with a usable pool keeps its Open action and the "can be opened"
        /// description; with BundleOpen false, or a resource whose pool names no usable item, the action comes off (Actions[0]
        /// = null, as before round 33b: no Open entry, nothing on a click) and the description goes back to the old one
        /// (Name + "Desc"). One log line.
        /// </summary>
        public static void Apply()
        {
            try
            {
                ScConfig.EnsureParsed();
                var line = new StringBuilder();
                int shut = 0, missing = 0;
                for (int i = 0; i < ScNames.ResourceCount; i++)
                {
                    var r = (Resource)i;
                    Pool pool = ScConfig.BundleOpen ? PoolFor(r) : null;
                    bool open = pool != null && pool.Count > 0;
                    if (ScConfig.BundleOpen && !open)
                        ScLog.Warn("bundle opening: " + ScConfig.BundleOpenPoolKey(r) + " names no item that packs as " + r + " - " + r + " bundles cannot be opened");
                    for (int s = 0; s <= 3; s++)
                    {
                        ItemClass ic = ItemClass.GetItemClass(ScNames.BundleItem(r, (BundleSize)s));
                        if (ic == null) continue;                       // the old one-size water and luxury bundles never existed
                        if (!SetOpenable(ic, open)) missing++;
                        if (!open) shut++;
                    }
                    if (ScConfig.BundleOpen)
                    {
                        if (line.Length > 0) line.Append(", ");
                        line.Append(ScNames.Key(r)).Append(' ').Append(open ? pool.Count.ToString() : "closed");
                    }
                }
                if (!ScConfig.BundleOpen) ScLog.Out("bundle opening: off (BundleOpen false) - " + shut + " bundle item(s) without the Open action");
                else ScLog.Out("bundle opening: on, " + ScConfig.BundleOpenValueShare.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
                               + " of the packing value (now " + ShareNow().ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
                               + "), pool items: " + line + (shut > 0 ? " (" + shut + " bundle item(s) closed)" : "")
                               + (missing > 0 ? " - " + missing + " bundle item(s) have no Open action" : ""));
            }
            catch (Exception e)
            {
                ScLog.Once("bundle-open-apply", e);
            }
        }

        /// <summary>One bundle item open or closed (see Apply). False when it should open but has no SC Open action.</summary>
        private static bool SetOpenable(ItemClass ic, bool open)
        {
            if (ic.Actions == null || ic.Actions.Length == 0) return !open;
            string name = ic.Name ?? "";
            string closedKey = name + "Desc";
            if (open)
            {
                ItemAction keep;
                if (ic.Actions[0] == null && stripped.TryGetValue(name, out keep) && keep != null && ReferenceEquals(keep.item, ic)) ic.Actions[0] = keep;
                string was;
                if (ic.DescriptionKey == closedKey && openKeys.TryGetValue(name, out was) && !string.IsNullOrEmpty(was)) ic.DescriptionKey = was;
                if (ic.Actions[0] is ItemActionScOpenBundle) return true;
                ScLog.Once("bundle-open-noaction-" + name, "bundle opening: " + name + " has no Open action (Action0 ScOpenBundle) - Config/items.xml did not apply, or another mod changed the item");
                return false;
            }
            if (ic.Actions[0] is ItemActionScOpenBundle)
            {
                stripped[name] = ic.Actions[0];
                ic.Actions[0] = null;
            }
            if (ic.DescriptionKey != closedKey)
            {
                openKeys[name] = ic.DescriptionKey;
                ic.DescriptionKey = closedKey;
            }
            return true;
        }

        // ---- opening one ----

        /// <summary>
        /// The Open action (ItemActionScOpenBundle.ExecuteInstantAction): vanilla's order - the start event and the sound, ONE
        /// bundle used (the held stack loses one; from the backpack's Open entry the stack handed here is the one bundle that
        /// entry took off), the end event - then the assortment into the inventory, the rest on the ground. A refusal (opening
        /// off, nothing to open into) uses nothing: the backpack entry's bundle goes back on its stack, and a tooltip says why.
        /// Always true: false would make the backpack entry play the item through the held-item path.
        /// </summary>
        public static bool Open(ItemAction action, EntityAlive ent, ItemStack stack, bool isHeldItem, XUiC_ItemStack stackController)
        {
            EntityPlayerLocal player = ent as EntityPlayerLocal;
            bool used = false;
            try
            {
                // as vanilla's: the local player's own bundle (its inventory window takes the items)
                if (player == null || stack == null || stack.IsEmpty() || stack.itemValue == null || stack.itemValue.ItemClass == null) return true;
                Resource r;
                int units;
                if (!ScStockpile.IsBundle(stack, out r, out units)) { Refuse(player, stack, isHeldItem, stackController, Localization.Get("scInternal")); return true; }
                ScConfig.EnsureParsed();
                if (!ScConfig.BundleOpen) { Refuse(player, stack, isHeldItem, stackController, Localization.Get("scBundleOpenOff")); return true; }
                Pool pool = PoolFor(r);
                if (pool == null || pool.Count == 0)
                {
                    Refuse(player, stack, isHeldItem, stackController, string.Format(Localization.Get("scBundleOpenEmpty"), Localization.Get(ScNames.LocKey(r))));
                    return true;
                }
                string bundleName = stack.itemValue.ItemClass.Name;
                int pack = Math.Max(1, units) * ScConfig.PointsPerBundle[(int)r];
                float share = ShareNow();
                int target = TargetPoints(units, ScConfig.PointsPerBundle[(int)r], share);
                GameRandom rand = ent.rand;
                int value;
                int[] counts = Assemble(pool.Points, pool.Weights, pool.StackMax, target, () => rand.RandomFloat, out value);

                ent.MinEventContext.ItemValue = stack.itemValue;
                ent.MinEventContext.ItemValue.FireEvent(MinEventTypes.onSelfPrimaryActionStart, ent.MinEventContext);
                ent.FireEvent(MinEventTypes.onSelfPrimaryActionStart, false);
                if (action != null && action.soundStart != null) ent.PlayOneShot(action.soundStart);
                if (isHeldItem) ent.inventory.DecHoldingItem(1);
                else stack.count--;
                used = true;
                ent.MinEventContext.ItemValue = stack.itemValue;
                ent.MinEventContext.ItemValue.FireEvent(MinEventTypes.onSelfPrimaryActionEnd, ent.MinEventContext);
                ent.FireEvent(MinEventTypes.onSelfPrimaryActionEnd, false);

                LocalPlayerUI ui = LocalPlayerUI.GetUIForPlayer(player);
                XUiM_PlayerInventory inv = ui != null && ui.xui != null ? ui.xui.PlayerInventory : null;
                var got = new StringBuilder();
                int dropped = 0;
                for (int i = 0; i < counts.Length; i++)
                {
                    if (counts[i] <= 0) continue;
                    if (got.Length > 0) got.Append(", ");
                    got.Append(counts[i]).Append(' ').Append(pool.Names[i]);
                    int left = counts[i];
                    int max = Math.Max(1, pool.StackMax[i]);
                    while (left > 0)
                    {
                        int n = Math.Min(left, max);
                        left -= n;
                        var s = new ItemStack(pool.Items[i].Clone(), n);
                        if (inv != null && inv.AddItem(s)) continue;
                        if (s.count > 0)
                        {
                            dropped += s.count;
                            ent.world.gameManager.ItemDropServer(s, ent.GetPosition(), Vector3.zero);
                        }
                    }
                }
                ScLog.Out("opened " + bundleName + ": " + got + " = " + value + " points (target " + target + " = "
                          + share.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " of " + pack + ")"
                          + (dropped > 0 ? ", " + dropped + " item(s) on the ground (inventory full)" : ""));
                return true;
            }
            catch (Exception e)
            {
                ScLog.Once("bundle-open", e);
                if (!used && player != null)
                {
                    try { Refuse(player, stack, isHeldItem, stackController, Localization.Get("scInternal")); }
                    catch (Exception) { }
                }
                return true;
            }
        }

        /// <summary>
        /// Nothing opened: the tooltip, and the backpack entry's bundle back on its stack (ItemActionEntryUse.OnActivated took it
        /// off before asking; a held bundle was never touched).
        /// </summary>
        private static void Refuse(EntityPlayerLocal player, ItemStack stack, bool isHeldItem, XUiC_ItemStack stackController, string text)
        {
            if (!isHeldItem && stack != null && stack.itemValue != null && !stack.itemValue.IsEmpty())
            {
                ItemStack back = new ItemStack(stack.itemValue.Clone(), 1);
                ItemStack now = stackController != null ? stackController.ItemStack : null;
                if (stackController != null && (now == null || now.IsEmpty()))
                    stackController.ItemStack = back;
                else if (stackController != null && now.itemValue != null && now.itemValue.type == back.itemValue.type
                         && now.count < Math.Max(1, back.itemValue.ItemClass.Stacknumber.Value))
                    stackController.ItemStack = new ItemStack(now.itemValue.Clone(), now.count + 1);
                else
                {
                    LocalPlayerUI ui = LocalPlayerUI.GetUIForPlayer(player);
                    XUiM_PlayerInventory inv = ui != null && ui.xui != null ? ui.xui.PlayerInventory : null;
                    if (inv == null || !inv.AddItem(back)) GameManager.Instance.ItemDropServer(back, player.GetPosition(), Vector3.zero);
                }
            }
            ScCommunity.Tell(player, text, "ui_denied");
        }
    }
}
