using System;
using System.Collections.Generic;
using System.Globalization;
using NPCSdcsCharacters.Pure;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round U4 (2026-11-16, decision D9; research/npc306/part_relationship.md section 3.2): a follower's drinks for putting out
    /// fire. 0-XNPCCore 3.4.06 lets a burning survivor douse herself; one with a Leader (a hired companion, and so SC's follower)
    /// spends a drink from her stores to do it (SdcsHealSeqApplyDouse; SdcsHasWater says she has one), anyone else douses for
    /// free. SC never stocked drinks on members, so a follower could burn once 0-XNPCCore's two seeded jars were gone. Drinks
    /// handed to her as gifts are used the same way.
    ///
    /// Here, at home (inside the community's zone), a follower (Following or PendingStation, with a Leader cvar) who carries fewer
    /// than MemberDrinksBelow (1) of 0-XNPCCore's douse drinks (DouseRules.DrinkNames, window and bag) is topped up to
    /// MemberDrinks (2) of MemberDrinkItem (boiled water), paid from the Water stockpile at packing value (a jar's points against
    /// PointsPerBundleWater; whole units are opened, what is left over pays the next jars this session), never from the water
    /// tonight's rollover needs. At most once per MemberDrinksCooldownHours per member (session only). Into her inventory window
    /// first (0-XNPCCore's gift room for drinks is her bag), then her bag; never dropped; not while her window is open (the
    /// window copy would undo it). Called from ScMedic.HomeTick (ScAmmo.Tick's 5 s member loop: a ticker path). MemberDrinks 0:
    /// never.
    /// </summary>
    public static class ScDrinks
    {
        private static readonly Dictionary<int, ulong> lastAt = new Dictionary<int, ulong>();
        private static readonly Dictionary<int, string> lastWhy = new Dictionary<int, string>();
        /// <summary>Session only: Water points opened from the stockpile and not yet given as drinks.</summary>
        private static int openPoints;
        private static int topUps, given, refused;
        private const ulong TicksPerHour = 1000UL;

        public static void Reset()
        {
            lastAt.Clear();
            lastWhy.Clear();
            openPoints = 0;
            topUps = 0;
            given = 0;
            refused = 0;
        }

        internal static void HomeTick(Member m, EntityAlive e)
        {
            try
            {
                if (ScConfig.MemberDrinks <= 0 || m == null || e == null || e.Buffs == null || e.IsDead()) return;
                if (m.Status != MemberStatus.Following && m.Status != MemberStatus.PendingStation) return;
                if (e.Buffs.GetCustomVar("Leader") <= 0f) return;                   // 0-XNPCCore charges a drink only with a Leader
                World w = e.world;
                if (w == null || w.IsRemote() || !ScCommunity.IsHost) return;
                if (!ScZone.HasHome || !ScZone.Contains(e.GetBlockPosition())) return;
                if (!ScNpc.UsesWindow(e) || ScAmmo.IsWindowOpen(e.entityId)) return;
                int have = Count(e);
                if (have >= ScConfig.MemberDrinksBelow) return;
                ulong now = w.worldTime, last;
                ulong cd = (ulong)(Math.Max(0f, ScConfig.MemberDrinksCooldownHours) * TicksPerHour);
                if (lastAt.TryGetValue(m.Id, out last) && now >= last && now - last < cd) return;
                lastAt[m.Id] = now;
                ItemValue drink = ItemClass.GetItem(ScConfig.MemberDrinkItem, true);
                if (drink == null || drink.IsEmpty() || drink.ItemClass == null || !DouseRules.IsDrink(drink.ItemClass.GetItemName()))
                {
                    Refuse(m, "MemberDrinkItem '" + ScConfig.MemberDrinkItem + "' is no drink 0-XNPCCore douses with");
                    return;
                }
                int want = ScConfig.MemberDrinks - have;
                if (want <= 0) return;
                Community c = ScRegistry.Community;
                int each = Math.Max(1, ScCategories.PointsEach(drink.ItemClass));
                int perUnit = Math.Max(1, ScConfig.PointsPerBundle[(int)Resource.Water]);
                int reserve = Mathf.CeilToInt(ScMorale.DailyWaterShortfall());      // tonight's rollover drinks first
                int spare = Math.Max(0, c.Get(Resource.Water) - reserve);
                int give = (int)Math.Min((long)want, ((long)openPoints + (long)spare * perUnit) / each);
                if (give <= 0)
                {
                    Refuse(m, c.Get(Resource.Water) <= 0 ? "the stockpile has no Water" : "the stockpile has no Water to spare (" + reserve + " kept for tonight)");
                    return;
                }
                int placed = Place(e, drink, give);
                if (placed <= 0) { Refuse(m, "there is no room in " + ScMedic.Her(m) + " pack"); return; }
                int cost = placed * each, opened = 0;
                while (openPoints < cost && c.Get(Resource.Water) > 0) { c.Add(Resource.Water, -1); openPoints += perUnit; opened++; }
                openPoints = Math.Max(0, openPoints - cost);
                topUps++;
                given += placed;
                lastWhy.Remove(m.Id);
                ScLog.Out(m.Name + " (id " + e.entityId + ") was given " + placed + " " + drink.ItemClass.GetItemName() + " for putting out fire (had " + have + "): "
                          + opened + " Water opened, " + openPoints + " point(s) left over, " + c.Get(Resource.Water) + " Water left (round U4)");
                if (opened > 0) ScRegistry.SaveAtomic();
            }
            catch (Exception ex)
            {
                ScLog.Once("drinks-home", ex);
            }
        }

        private static void Refuse(Member m, string why)
        {
            refused++;
            string last;
            if (lastWhy.TryGetValue(m.Id, out last) && last == why) return;
            lastWhy[m.Id] = why;
            ScLog.Out(m.Name + " needs a drink for putting out fire but " + why + " (round U4)");
        }

        /// <summary>0-XNPCCore's douse drinks she carries: inventory window and bag.</summary>
        public static int Count(EntityAlive e)
        {
            int n = CountIn(ScNpc.UsesWindow(e) ? ScNpc.Items(e) : null);
            if (e.bag != null) n += CountIn(e.bag.ItemGrid.items);
            return n;
        }

        private static int CountIn(ItemStack[] slots)
        {
            if (slots == null) return 0;
            int n = 0;
            foreach (ItemStack s in slots)
                if (s != null && !s.IsEmpty() && s.itemValue.ItemClass != null && DouseRules.IsDrink(s.itemValue.ItemClass.GetItemName())) n += s.count;
            return n;
        }

        /// <summary>One at a time into her inventory window (SCore's HarvestManager), else her bag; never dropped. Returns how many went in.</summary>
        private static int Place(EntityAlive e, ItemValue item, int count)
        {
            int placed = 0;
            for (int k = 0; k < count; k++)
            {
                if (HarvestManager.AddItem(e.entityId, new ItemStack(item.Clone(), 1))) { placed++; continue; }
                if (e.bag != null && e.bag.AddItem(new ItemStack(item.Clone(), 1))) { placed++; continue; }
                break;
            }
            return placed;
        }

        /// <summary>sc bond's line for the followers' drink top-up.</summary>
        public static string Report()
        {
            return "followers' drinks (round U4): " + (ScConfig.MemberDrinks > 0 ? "to " + ScConfig.MemberDrinks + " below " + ScConfig.MemberDrinksBelow + " of "
                   + ScConfig.MemberDrinkItem : "off") + " - " + topUps + " top-up(s), " + given + " drink(s) given, " + refused + " refusal(s), "
                   + openPoints.ToString(CultureInfo.InvariantCulture) + " Water point(s) open this session";
        }
    }
}
