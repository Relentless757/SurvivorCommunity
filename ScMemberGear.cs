using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round n10 (3.4): what the member inventory panel's equipment column shows (XUiC_ScMemberInv) - her pair's two weapons as
    /// she carries them, the four armor pieces SC dresses her in, and the best tool she carries for each of five jobs. Read
    /// only: nothing here moves an item (round n12's changes are ScMemberEquip's; its tool slots use this same classification,
    /// Rank, and take the tool Tools picks).
    ///
    /// The job tools (the user's decision: the BEST tool she carries for the job, from all her stores; one tool that serves
    /// several jobs shows in each, marked shared). Which tools serve which job is 0-XNPCCore 3.4.05's own rule, read from its
    /// job tasks, plus SC's mining-run tags (Config/blocks.xml MiningToolTags / DigToolTags):
    ///   Chop    (UAITaskHarvestSdcs.PickTool, wood): tag axe ranks 2, tag chainsaw 1.
    ///   Mine    (PickTool, stone): tag miningTool ranks 2, 1 when it is also a motorTool (the auger); any other item SC's
    ///           mining runs take (MiningToolTags) ranks 1.
    ///   Dig     (PickTool, clay): tag shovel ranks 3, miningTool 2 (1 for a motorTool); any other DigToolTags item 1.
    ///   Butcher (UAITaskButcherSdcs.PickKnife): tag corpseRemoval, ranked by its butcherHarvest HarvestCount, as 0-XNPCCore
    ///           ranks knives.
    ///   Repair  (UAITaskBaseKeepSdcs.PickRepairTool): tag repairTool with an ItemActionRepair, ranked by its repair amount.
    /// "Best" (decided here, see the report of round n10): the tool 0-XNPCCore's job reaches for first - its rank above, so a
    /// steel axe and a chainsaw show the axe, as she would use it - then the higher tier (ScMining.TierOf: the T0..T6 tag or
    /// the T&lt;n&gt; in the name, which is also what SC's mining runs pay by), then the higher quality, then the less worn, then
    /// one handed over (window, bag) before her toolbelt's work kit, as 0-XNPCCore looks at the kit last. A worn-out tool
    /// (UseTimes at MaxUseTimes: 0-XNPCCore will not use it) shows only when she has nothing else for the job, marked so.
    /// </summary>
    public static class ScMemberGear
    {
        public const int JobCount = 5;
        public const int JobChop = 0, JobMine = 1, JobDig = 2, JobButcher = 3, JobRepair = 4;

        /// <summary>The stores a tool is looked for in, in the order a tie goes: what was handed over first, the toolbelt last.</summary>
        public const int StoreWindow = 0, StoreBag = 1, StoreToolbelt = 2;

        private static readonly FastTags<TagGroup.Global> AxeTag = FastTags<TagGroup.Global>.Parse("axe");
        private static readonly FastTags<TagGroup.Global> ChainsawTag = FastTags<TagGroup.Global>.Parse("chainsaw");
        private static readonly FastTags<TagGroup.Global> ShovelTag = FastTags<TagGroup.Global>.Parse("shovel");
        private static readonly FastTags<TagGroup.Global> MiningTag = FastTags<TagGroup.Global>.Parse("miningTool");
        private static readonly FastTags<TagGroup.Global> MotorTag = FastTags<TagGroup.Global>.Parse("motorTool");
        private static readonly FastTags<TagGroup.Global> KnifeTag = FastTags<TagGroup.Global>.Parse("corpseRemoval");
        private static readonly FastTags<TagGroup.Global> ButcherHarvestTag = FastTags<TagGroup.Global>.Parse("butcherHarvest");
        private static readonly FastTags<TagGroup.Global> RepairTag = FastTags<TagGroup.Global>.Parse("repairTool");

        // ------------------------------------------------------------------ weapons ----

        /// <summary>One weapon slot: the item as she carries it (empty for none), its name and its info line.</summary>
        public sealed class WeaponView
        {
            public ItemStack Stack = ItemStack.Empty.Clone();
            public string Name = "";
            public string Info = "";
        }

        /// <summary>
        /// The ranged or melee weapon of her pair (ScLoadout.Current): the live copy in her hand when she holds it (its loaded
        /// rounds), else the toolbelt copy SCore made from her item list (its quality and mods). A body SC does not manage
        /// (no pair) shows what it holds, in the slot of its kind.
        /// </summary>
        public static WeaponView Weapon(EntityAlive npc, bool ranged)
        {
            var v = new WeaponView();
            if (npc == null) return v;
            Loadout l = ScLoadout.Current(npc);
            ItemClass ic;
            if (l == null)
            {
                ItemValue held = npc.inventory != null ? npc.inventory.holdingItemItemValue : null;
                ic = held != null && !held.IsEmpty() ? held.ItemClass : null;
                if (ic == null || IsRangedItem(ic) != ranged || ScXnpc.IsLightTool(ic) || ic.GetItemName() == "meleeHandPlayer")
                {
                    v.Name = Localization.Get("scInvNone");
                    return v;
                }
            }
            else
            {
                string token = l.Of(ranged);
                if (ranged && Loadout.IsNoRanged(token))
                {
                    v.Name = Localization.Get("scInvNoRanged");
                    return v;
                }
                ic = !string.IsNullOrEmpty(token) ? ItemClass.GetItemClass(token, true) : null;
                if (ic == null)
                {
                    v.Name = string.IsNullOrEmpty(token) ? Localization.Get("scInvNone") : token;
                    return v;
                }
            }
            ItemValue iv = CarriedValue(npc, ic);
            v.Stack = new ItemStack(iv, 1);
            v.Name = ic.GetLocalizedItemName();
            v.Info = Info(npc, ic, iv);
            return v;
        }

        private static bool IsRangedItem(ItemClass ic)
        {
            return ic != null && ic.Actions != null && ic.Actions.Length > 0 && ic.Actions[0] is ItemActionRanged;
        }

        /// <summary>
        /// Her copy of this weapon: the one in her hand, else (round n12) the exact item she was handed for her pair (ScHanded:
        /// what every copy carries, shown even while the toolbelt has no copy - the melee weapon while she holds the gun), else
        /// the toolbelt's, else a plain quality 1 one (not carried; a multiplayer client, which has no record, shows this way).
        /// </summary>
        private static ItemValue CarriedValue(EntityAlive npc, ItemClass ic)
        {
            ItemValue held = npc.inventory != null ? npc.inventory.holdingItemItemValue : null;
            if (held != null && !held.IsEmpty() && held.type == ic.Id) return held.Clone();
            Loadout pair = ScLoadout.Current(npc);
            ItemValue handed = pair != null ? ScHanded.ForCopy(npc, pair, ic.Id) : null;
            if (handed != null) return handed.Clone();
            ItemStack[] belt = npc.inventory != null && npc.inventory.ItemGrid != null ? npc.inventory.ItemGrid.items : null;
            if (belt != null)
                foreach (ItemStack s in belt)
                    if (s != null && !s.IsEmpty() && s.itemValue != null && s.itemValue.type == ic.Id) return s.itemValue.Clone();
            return ic.HasQuality ? new ItemValue(ic.Id, 1, 1, false, null, 1f) : new ItemValue(ic.Id, false);
        }

        /// <summary>"Quality 4 · 8/8 loaded · 45 spare" for a gun or bow (with Locked &amp; Loaded: "· 3 magazines"), "Quality 3 · 87%" for a melee weapon.</summary>
        private static string Info(EntityAlive npc, ItemClass ic, ItemValue iv)
        {
            var parts = new List<string>();
            if (iv.HasQuality && iv.Quality > 0) parts.Add(string.Format(Localization.Get("scInvQuality"), iv.Quality));
            ItemActionRanged r = ScAmmo.RangedAction(ic);
            if (r != null)
            {
                try
                {
                    ItemValue held = npc.inventory != null ? npc.inventory.holdingItemItemValue : null;
                    bool inHand = held != null && !held.IsEmpty() && held.type == ic.Id;
                    int capacity = Math.Max(1, (int)EffectManager.GetValue(PassiveEffects.MagazineSize, iv, r.BulletsPerMagazine, npc));
                    int loaded = inHand ? Math.Max(0, held.Meta) : Math.Max(0, (int)ScAmmo.StoredMagazine(npc, ic));
                    parts.Add(string.Format(Localization.Get("scInvLoaded"), loaded, capacity));
                    if (ScMags.Active && ScMags.Carries(npc, ScMags.GunValue(npc, ic)))
                    {
                        int mags = ScMags.Entries(ScMags.Record(npc, ic.GetItemName())).Count;
                        if (mags > 0) parts.Add(string.Format(Localization.Get("scInvMags"), mags));
                    }
                    int spare = Math.Max(0, ScAmmo.Rounds(npc, ic) - (inHand ? loaded : 0));
                    parts.Add(string.Format(Localization.Get("scInvSpare"), spare));
                }
                catch (Exception e)
                {
                    ScLog.Once("memberinv-rounds", e);
                }
            }
            else if (iv.MaxUseTimes > 0)
            {
                float left = Mathf.Clamp01(1f - iv.UseTimes / iv.MaxUseTimes);
                parts.Add(Mathf.RoundToInt(left * 100f).ToString(CultureInfo.InvariantCulture) + "%");
            }
            return string.Join(" · ", parts.ToArray());
        }

        // ------------------------------------------------------------------ armor ----

        /// <summary>
        /// The four pieces SC dresses her in (ScLooks.Get: Look.Gear by slot Head, Chest, Hands, Feet, at Look.Quality), as
        /// items of that quality; an empty stack for a bare slot or an item that no longer exists. Null without a look.
        /// Round n12: a piece she was handed (ScHanded) shows as that exact item - its wear and armor mods - as she wears it.
        /// </summary>
        public static ItemStack[] Armor(EntityAlive npc)
        {
            Look look = npc != null ? ScLooks.Get(npc) : null;
            if (look == null) return null;
            Member m = ScHanded.MemberOf(npc);
            var a = new ItemStack[4];
            for (int i = 0; i < 4; i++)
            {
                ItemValue handed = ScHanded.Armor(m, look, i);
                if (handed != null) { a[i] = new ItemStack(handed.Clone(), 1); continue; }
                string name = look.Gear[i];
                ItemClass ic = !string.IsNullOrEmpty(name) ? ItemClass.GetItemClass(name, true) : null;
                if (ic == null) { a[i] = ItemStack.Empty.Clone(); continue; }
                int q = Math.Max(1, Math.Min(6, look.Quality[i]));
                a[i] = new ItemStack(ic.HasQuality ? new ItemValue(ic.Id, q, q, false, null, 1f) : new ItemValue(ic.Id, false), 1);
            }
            return a;
        }

        /// <summary>
        /// The look's gear as one string ("name:q|..."), to tell whether the armor slots need building again - with each
        /// handed piece's build (round n12: the same name and quality handed again with other mods changes the slot too).
        /// </summary>
        public static string ArmorKey(EntityAlive npc)
        {
            Look look = npc != null ? ScLooks.Get(npc) : null;
            if (look == null) return "";
            Member m = ScHanded.MemberOf(npc);
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < 4; i++)
                sb.Append(look.Gear[i]).Append(':').Append(look.Quality[i]).Append(':').Append(ScHanded.Fingerprint(ScHanded.Armor(m, look, i))).Append('|');
            return sb.ToString();
        }

        // ------------------------------------------------------------------ job tools ----

        /// <summary>The tool shown for one job: a copy of the stack, where it is, and whether another job shows the same one.</summary>
        public sealed class ToolPick
        {
            public ItemStack Stack;
            public int Store = -1;
            public int Slot = -1;
            public bool Shared;
            public bool Broken;
            internal float Rank;
            internal int Tier;
            internal int Quality;
            internal float Wear;
        }

        /// <summary>
        /// The best tool for each job (JobChop..JobRepair; null where she has none) from her inventory window (window: the
        /// stacks the panel shows - the HarvestManager container on the host, the client's copy on a client), her bag and her
        /// toolbelt (where SCore keeps the copies of her item list: the work kit and the pair).
        /// </summary>
        public static ToolPick[] Tools(EntityAlive npc, ItemStack[] window)
        {
            var best = new ToolPick[JobCount];
            if (npc == null) return best;
            Scan(npc, window, StoreWindow, best);
            Scan(npc, npc.bag != null && npc.bag.ItemGrid != null ? npc.bag.ItemGrid.items : null, StoreBag, best);
            Scan(npc, npc.inventory != null && npc.inventory.ItemGrid != null ? npc.inventory.ItemGrid.items : null, StoreToolbelt, best);
            for (int j = 0; j < JobCount; j++)
            {
                if (best[j] == null) continue;
                for (int k = 0; k < JobCount; k++)
                    if (k != j && best[k] != null && best[k].Store == best[j].Store && best[k].Slot == best[j].Slot) best[j].Shared = true;
            }
            return best;
        }

        private static void Scan(EntityAlive npc, ItemStack[] items, int store, ToolPick[] best)
        {
            if (items == null) return;
            for (int i = 0; i < items.Length; i++)
            {
                ItemStack s = items[i];
                if (s == null || s.IsEmpty() || s.itemValue == null) continue;
                ItemClass ic = s.itemValue.ItemClass;
                if (ic == null) continue;
                for (int j = 0; j < JobCount; j++)
                {
                    float rank = Rank(j, ic, s.itemValue, npc);
                    if (rank <= 0f) continue;
                    var c = new ToolPick
                    {
                        Stack = s.Clone(),
                        Store = store,
                        Slot = i,
                        Rank = rank,
                        Tier = ScMining.TierOf(ic),
                        Quality = s.itemValue.HasQuality ? s.itemValue.Quality : 0,
                        Wear = s.itemValue.MaxUseTimes > 0 ? s.itemValue.UseTimes / s.itemValue.MaxUseTimes : 0f,
                        Broken = s.itemValue.MaxUseTimes > 0 && s.itemValue.UseTimes >= s.itemValue.MaxUseTimes
                    };
                    if (Beats(c, best[j])) best[j] = c;
                }
            }
        }

        /// <summary>Is c a better pick than cur (the order in the class summary; stores are scanned in tie order, so an equal one loses)?</summary>
        private static bool Beats(ToolPick c, ToolPick cur)
        {
            if (cur == null) return true;
            if (c.Broken != cur.Broken) return !c.Broken;
            if (c.Rank != cur.Rank) return c.Rank > cur.Rank;
            if (c.Tier != cur.Tier) return c.Tier > cur.Tier;
            if (c.Quality != cur.Quality) return c.Quality > cur.Quality;
            if (c.Wear != cur.Wear) return c.Wear < cur.Wear;
            return false;
        }

        /// <summary>How well this item serves the job by 0-XNPCCore's own ranking (0: not at all).</summary>
        public static float Rank(int job, ItemClass ic, ItemValue iv, EntityAlive npc)
        {
            switch (job)
            {
                case JobChop:
                    if (ic.HasAnyTags(AxeTag)) return 2f;
                    return ic.HasAnyTags(ChainsawTag) ? 1f : 0f;
                case JobMine:
                    if (ic.HasAnyTags(MiningTag)) return ic.HasAnyTags(MotorTag) ? 1f : 2f;
                    return ScMining.IsTool(ic, RunJob.Mine) ? 1f : 0f;
                case JobDig:
                    if (ic.HasAnyTags(ShovelTag)) return 3f;
                    if (ic.HasAnyTags(MiningTag)) return ic.HasAnyTags(MotorTag) ? 1f : 2f;
                    return ScMining.IsTool(ic, RunJob.Clay) ? 1f : 0f;
                case JobButcher:
                    if (!ic.HasAnyTags(KnifeTag)) return 0f;
                    // 0-XNPCCore's ToolRate: the knife's butcherHarvest HarvestCount on her; every knife counts, so never below 1
                    return 1f + Math.Max(0f, EffectManager.GetValue(PassiveEffects.HarvestCount, iv, 0f, npc, null, ButcherHarvestTag));
                case JobRepair:
                    if (!ic.HasAnyTags(RepairTag) || ic.Actions == null) return 0f;
                    foreach (ItemAction a in ic.Actions)
                    {
                        ItemActionRepair r = a as ItemActionRepair;
                        if (r != null && r.repairAmount > 0f) return r.repairAmount;
                    }
                    return 0f;
            }
            return 0f;
        }
    }
}
