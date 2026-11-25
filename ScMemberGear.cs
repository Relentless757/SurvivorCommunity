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
    ///
    /// Round mt1 (part G): the host's panel calls these builders inline, as before; a multiplayer client's panel shows the
    /// server's (GearView, the op member.gearview: ScActionsGear), since its own copy of her body is not hers. The shown hand
    /// copy carries the handed item's identity, and the last-resort item is the same every refresh (Placeholder).
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
            /// <summary>Round mt1 (part G): what the slot names (KindNone .. KindToken), for the server's gear view.</summary>
            internal int Kind = KindNone;
            /// <summary>Round mt1 (part G): the item's name (KindItem) or the pair's token as it is (KindToken).</summary>
            internal string Item = "";
            /// <summary>Round mt1 (part G): the info line's numbers (KindItem), so a client formats them in its own language.</summary>
            internal InfoParts Parts = new InfoParts();
        }

        /// <summary>Round mt1 (part G): a weapon slot names nothing, "no ranged weapon", an item, or a pair token that is no item.</summary>
        internal const int KindNone = 0, KindNoRanged = 1, KindItem = 2, KindToken = 3;

        /// <summary>
        /// Round mt1 (part G): the info line's numbers (Info used to build the text on the spot): the quality (0: none shown),
        /// for a gun or bow its loaded rounds, capacity, Locked &amp; Loaded magazines (0: not shown) and spare rounds, for anything
        /// else the wear left in percent (-1: not shown).
        /// </summary>
        internal sealed class InfoParts
        {
            public int Quality;
            public bool Gun;
            public int Loaded, Capacity, Mags, Spare;
            public int WearPct = -1;

            /// <summary>"q,gun,loaded,capacity,mags,spare,wear" (whole numbers only: safe in a reply hint).</summary>
            public string Encode()
            {
                CultureInfo c = CultureInfo.InvariantCulture;
                return string.Join(",", new[] { Quality.ToString(c), Gun ? "1" : "0", Loaded.ToString(c), Capacity.ToString(c), Mags.ToString(c),
                                                Spare.ToString(c), WearPct.ToString(c) });
            }

            public static InfoParts Decode(string s)
            {
                var p = new InfoParts();
                if (string.IsNullOrEmpty(s)) return p;
                string[] a = s.Split(',');
                int[] n = new int[7];
                n[6] = -1;
                for (int i = 0; i < a.Length && i < n.Length; i++)
                {
                    int v;
                    if (int.TryParse(a[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) n[i] = v;
                }
                p.Quality = n[0];
                p.Gun = n[1] != 0;
                p.Loaded = n[2];
                p.Capacity = n[3];
                p.Mags = n[4];
                p.Spare = n[5];
                p.WearPct = n[6];
                return p;
            }
        }

        /// <summary>
        /// The ranged or melee weapon of her pair (ScLoadout.Current): the live copy in her hand when she holds it (its loaded
        /// rounds), else the toolbelt copy SCore made from her item list (its quality and mods). A body SC does not manage
        /// (no pair) shows what it holds, in the slot of its kind.
        /// </summary>
        public static WeaponView Weapon(EntityAlive npc, bool ranged)
        {
            return Weapon(npc, ranged, true);
        }

        /// <summary>
        /// Round mt1 (part G): the slot's name alone - no item, no info line. A multiplayer client's panel shows this until the
        /// server's gear view first answers (its own copies are not hers: a placeholder there was a new roll every refresh).
        /// </summary>
        public static WeaponView WeaponName(EntityAlive npc, bool ranged)
        {
            return Weapon(npc, ranged, false);
        }

        private static WeaponView Weapon(EntityAlive npc, bool ranged, bool withItem)
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
                    v.Kind = KindNoRanged;
                    v.Name = Localization.Get("scInvNoRanged");
                    return v;
                }
                ic = !string.IsNullOrEmpty(token) ? ItemClass.GetItemClass(token, true) : null;
                if (ic == null)
                {
                    v.Kind = string.IsNullOrEmpty(token) ? KindNone : KindToken;
                    v.Item = token ?? "";
                    v.Name = string.IsNullOrEmpty(token) ? Localization.Get("scInvNone") : token;
                    return v;
                }
            }
            v.Kind = KindItem;
            v.Item = ic.GetItemName();
            v.Name = ic.GetLocalizedItemName();
            if (!withItem) return v;
            ItemValue iv = CarriedValue(npc, ic);
            v.Stack = new ItemStack(iv, 1);
            v.Parts = InfoOf(npc, ic, iv);
            v.Info = FormatInfo(v.Parts);
            return v;
        }

        private static bool IsRangedItem(ItemClass ic)
        {
            return ic != null && ic.Actions != null && ic.Actions.Length > 0 && ic.Actions[0] is ItemActionRanged;
        }

        /// <summary>
        /// Her copy of this weapon: the one in her hand, else (round n12) the exact item she was handed for her pair (ScHanded:
        /// what every copy carries, shown even while the toolbelt has no copy - the melee weapon while she holds the gun), else
        /// the toolbelt's, else a plain quality 1 one (not carried).
        /// Round mt1 (part G): the hand's copy as shown carries the handed item's identity (ScHanded.Imprint on the shown clone,
        /// never on her copy): a copy just rebuilt by name (SCore's by-name equip, before the 1 s relink re-imprints it) no
        /// longer shows a strange quality, seed or stats. The last resort is the same item every refresh (seed 0): a new seed
        /// each call made the slot's item - and its seed-rolled numbers - change every half second.
        /// </summary>
        private static ItemValue CarriedValue(EntityAlive npc, ItemClass ic)
        {
            Loadout pair = ScLoadout.Current(npc);
            ItemValue handed = pair != null ? ScHanded.ForCopy(npc, pair, ic.Id) : null;
            ItemValue held = npc.inventory != null ? npc.inventory.holdingItemItemValue : null;
            if (held != null && !held.IsEmpty() && held.type == ic.Id)
            {
                ItemValue shown = held.Clone();
                if (handed != null) ScHanded.Imprint(shown, handed);       // round mt1 (part G): the exact item, with her rounds
                return shown;
            }
            if (handed != null) return handed.Clone();
            ItemStack[] belt = npc.inventory != null && npc.inventory.ItemGrid != null ? npc.inventory.ItemGrid.items : null;
            if (belt != null)
                foreach (ItemStack s in belt)
                    if (s != null && !s.IsEmpty() && s.itemValue != null && s.itemValue.type == ic.Id) return s.itemValue.Clone();
            return Placeholder(ic, 1);
        }

        /// <summary>
        /// Round mt1 (part G): an item of this class and quality made for display only - the same every call (seed 0, no mods,
        /// no stats), so a slot comparing stacks (XUiC_ScGearSlot.Show: ItemStack.Equals counts the seed) never churns.
        /// </summary>
        private static ItemValue Placeholder(ItemClass ic, int quality)
        {
            ItemValue iv = ic.HasQuality ? new ItemValue(ic.Id, quality, quality, false, null, 1f) : new ItemValue(ic.Id, false);
            iv.Seed = 0;
            return iv;
        }

        /// <summary>
        /// The info line's numbers: "Quality 4 · 8/8 loaded · 45 spare" for a gun or bow (with Locked &amp; Loaded: "· 3
        /// magazines"), "Quality 3 · 87%" for a melee weapon. Round mt1 (part G): numbers here, the text in FormatInfo, so the
        /// server's gear view sends numbers and a client formats them in its own language.
        /// </summary>
        private static InfoParts InfoOf(EntityAlive npc, ItemClass ic, ItemValue iv)
        {
            var p = new InfoParts();
            if (iv.HasQuality && iv.Quality > 0) p.Quality = iv.Quality;
            ItemActionRanged r = ScAmmo.RangedAction(ic);
            if (r != null)
            {
                try
                {
                    ItemValue held = npc.inventory != null ? npc.inventory.holdingItemItemValue : null;
                    bool inHand = held != null && !held.IsEmpty() && held.type == ic.Id;
                    int capacity = Math.Max(1, (int)EffectManager.GetValue(PassiveEffects.MagazineSize, iv, r.BulletsPerMagazine, npc));
                    int loaded = inHand ? Math.Max(0, held.Meta) : Math.Max(0, (int)ScAmmo.StoredMagazine(npc, ic));
                    int mags = 0;
                    if (ScMags.Active && ScMags.Carries(npc, ScMags.GunValue(npc, ic)))
                        mags = ScMags.Entries(ScMags.Record(npc, ic.GetItemName())).Count;
                    int spare = Math.Max(0, ScAmmo.Rounds(npc, ic) - (inHand ? loaded : 0));
                    p.Gun = true;
                    p.Loaded = loaded;
                    p.Capacity = capacity;
                    p.Mags = mags;
                    p.Spare = spare;
                }
                catch (Exception e)
                {
                    ScLog.Once("memberinv-rounds", e);
                }
            }
            else if (iv.MaxUseTimes > 0)
            {
                float left = Mathf.Clamp01(1f - iv.UseTimes / iv.MaxUseTimes);
                p.WearPct = Mathf.RoundToInt(left * 100f);
            }
            return p;
        }

        /// <summary>Round mt1 (part G): the info line from its numbers, in this machine's language (the host's and a client's alike).</summary>
        internal static string FormatInfo(InfoParts p)
        {
            var parts = new List<string>();
            if (p == null) return "";
            try
            {
                if (p.Quality > 0) parts.Add(string.Format(Localization.Get("scInvQuality"), p.Quality));
                if (p.Gun)
                {
                    parts.Add(string.Format(Localization.Get("scInvLoaded"), p.Loaded, p.Capacity));
                    if (p.Mags > 0) parts.Add(string.Format(Localization.Get("scInvMags"), p.Mags));
                    parts.Add(string.Format(Localization.Get("scInvSpare"), p.Spare));
                }
                else if (p.WearPct >= 0) parts.Add(p.WearPct.ToString(CultureInfo.InvariantCulture) + "%");
            }
            catch (FormatException e)
            {
                ScLog.Once("memberinv-info-format", e);       // a translation with a broken placeholder: what was built so far
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
                a[i] = new ItemStack(Placeholder(ic, q), 1);        // round mt1 (part G): the same item every build (seed 0)
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
            return Tools(npc, window, null);
        }

        /// <summary>
        /// Round mt1 (part G): Tools with the server's picks (server: ServerTools, from the gear view) standing in for her bag and
        /// toolbelt on a multiplayer client. The client's own copy of her body is not hers - its toolbelt is a local roll of her
        /// item list without the work kit (bug_toolset_wood_job.md section 2: the five job slots showed empty), and it has no
        /// bag - so a client scans only its copy of her pack (the window, which it edits itself) and takes the rest from the
        /// server. Picks that are the server's keep the server's store and slot (Store is never StoreWindow for those): a client
        /// cannot take them (XUiC_ScMemberInv.CopyToolTake says why). Null server: the window alone (before the first answer).
        /// The host (single player, a listen host) scans all three of its own stores, as before.
        /// </summary>
        public static ToolPick[] Tools(EntityAlive npc, ItemStack[] window, ToolPick[] server)
        {
            var best = new ToolPick[JobCount];
            if (npc == null) return best;
            Scan(npc, window, StoreWindow, best);
            if (ScCommunity.IsHost)
            {
                // round MP3: a multiplayer client has no copy of her bag (the game never sends an NPC's) - whatever its copy of the
                // body holds there is not hers to show, nor to take from (ScMemberEquip.CopyTakeTool)
                Scan(npc, npc.bag != null && npc.bag.ItemGrid != null ? npc.bag.ItemGrid.items : null, StoreBag, best);
                Scan(npc, npc.inventory != null && npc.inventory.ItemGrid != null ? npc.inventory.ItemGrid.items : null, StoreToolbelt, best);
            }
            else if (server != null) ScanServer(npc, server, best);
            for (int j = 0; j < JobCount; j++)
            {
                if (best[j] == null) continue;
                for (int k = 0; k < JobCount; k++)
                    if (k != j && best[k] != null && best[k].Store == best[j].Store && best[k].Slot == best[j].Slot) best[j].Shared = true;
            }
            return best;
        }

        /// <summary>
        /// Round mt1 (part G), the server: the best tool per job from her bag and her toolbelt only (the gear view's tool picks;
        /// her pack is the opener's own copy on a client, scanned there).
        /// </summary>
        internal static ToolPick[] ServerTools(EntityAlive npc)
        {
            var best = new ToolPick[JobCount];
            if (npc == null) return best;
            Scan(npc, npc.bag != null && npc.bag.ItemGrid != null ? npc.bag.ItemGrid.items : null, StoreBag, best);
            Scan(npc, npc.inventory != null && npc.inventory.ItemGrid != null ? npc.inventory.ItemGrid.items : null, StoreToolbelt, best);
            return best;
        }

        /// <summary>
        /// Round mt1 (part G), a client: the server's picks scanned in the server's own order (store, then slot), each one item
        /// at its store and slot - so for every job the winner is the one the server would pick among its bag and toolbelt, and
        /// a tie with the client's window still goes to the window, as on the host.
        /// </summary>
        private static void ScanServer(EntityAlive npc, ToolPick[] server, ToolPick[] best)
        {
            var seen = new List<ToolPick>();
            foreach (ToolPick t in server)
            {
                if (t == null || t.Stack == null || t.Stack.IsEmpty() || t.Store == StoreWindow) continue;
                bool dup = false;
                foreach (ToolPick s in seen) if (s.Store == t.Store && s.Slot == t.Slot) dup = true;
                if (!dup) seen.Add(t);
            }
            seen.Sort((a, b) => a.Store != b.Store ? a.Store.CompareTo(b.Store) : a.Slot.CompareTo(b.Slot));
            foreach (ToolPick t in seen) ScanOne(npc, t.Stack, t.Store, t.Slot, best);
        }

        private static void Scan(EntityAlive npc, ItemStack[] items, int store, ToolPick[] best)
        {
            if (items == null) return;
            for (int i = 0; i < items.Length; i++) ScanOne(npc, items[i], store, i, best);
        }

        /// <summary>One stack at its store and slot against the best pick of every job (round mt1: Scan's body, shared with ScanServer).</summary>
        private static void ScanOne(EntityAlive npc, ItemStack s, int store, int slot, ToolPick[] best)
        {
            if (s == null || s.IsEmpty() || s.itemValue == null) return;
            ItemClass ic = s.itemValue.ItemClass;
            if (ic == null) return;
            for (int j = 0; j < JobCount; j++)
            {
                float rank = Rank(j, ic, s.itemValue, npc);
                if (rank <= 0f) continue;
                var c = new ToolPick
                {
                    Stack = s.Clone(),
                    Store = store,
                    Slot = slot,
                    Rank = rank,
                    Tier = ScMining.TierOf(ic),
                    Quality = s.itemValue.HasQuality ? s.itemValue.Quality : 0,
                    Wear = s.itemValue.MaxUseTimes > 0 ? s.itemValue.UseTimes / s.itemValue.MaxUseTimes : 0f,
                    Broken = s.itemValue.MaxUseTimes > 0 && s.itemValue.UseTimes >= s.itemValue.MaxUseTimes
                };
                if (Beats(c, best[j])) best[j] = c;
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

        // ------------------------------------------------------------------ round mt1 (part G): the server's gear view ----

        /// <summary>
        /// Round mt1 (part G, F2; bug_gear_equip_window.md 1.2): the equipment column as the server sees it, for a multiplayer
        /// client's panel. A client's own copy of her body never has what the column shows: the exact handed items are the
        /// server's (ScHanded, never mirrored), her toolbelt there is a local roll of her item list without the work kit, she
        /// has no bag there, and her held item can be a by-name roll for a while. So the client's RANGED and MELEE showed its
        /// own roll or a new quality 1 placeholder every half second - the user's "re-rolls" - and its job slots no kit.
        /// The server builds this with the very builders the host's panel calls inline (Weapon, Armor, ServerTools,
        /// ScAmmoChoice.LineParts) and sends it as reply hints (PutView): the op member.gearview (a quiet read the panel polls)
        /// and every remote member.geardrop / geartake reply. The client reads it (ReadView) and formats every text in its own
        /// language: names from item names, the info line from numbers (InfoParts), the ammo line from its parts.
        /// </summary>
        public sealed class GearView
        {
            public const int ArmorNone = 0, ArmorSent = 1, ArmorLeftOut = 2;

            public int NpcId = -1;
            /// <summary>RANGED, MELEE - on a client already in its own language (ReadView).</summary>
            public readonly WeaponView[] Weapons = { new WeaponView(), new WeaponView() };
            /// <summary>ArmorNone: she has no look (bare slots); ArmorSent: Armor holds the four pieces; ArmorLeftOut: too large for the reply - the client builds them from its copy of her look, as before round mt1.</summary>
            public int ArmorState = ArmorNone;
            public readonly ItemStack[] Armor = { ItemStack.Empty.Clone(), ItemStack.Empty.Clone(), ItemStack.Empty.Clone(), ItemStack.Empty.Clone() };
            /// <summary>The server's best tool per job from her bag and toolbelt (ServerTools; null where none or not sent).</summary>
            public readonly ToolPick[] Tools = new ToolPick[JobCount];
            public bool AmmoShow;
            public string AmmoKey = "", AmmoItem = "";
            public int AmmoCount;

            /// <summary>The armor slots' key (XUiC_ScMemberInv rebuilds them when it changes).</summary>
            public string ArmorKey()
            {
                var sb = new System.Text.StringBuilder("view:").Append(ArmorState);
                for (int i = 0; i < Armor.Length; i++)
                    sb.Append('|').Append(ScHanded.Fingerprint(Armor[i] != null && !Armor[i].IsEmpty() ? Armor[i].itemValue : null));
                return sb.ToString();
            }
        }

        // the view's hint keys: all start with 'v', apart from the gear replies' cur / give / pt / placed
        private const string VKey = "v", VVersion = "1";
        private static readonly string[] VWeapon = { "vr", "vm" };      // + k (kind), i (item), s (stack), p (InfoParts)
        private const string VArmor = "va";                              // the ArmorState; va0..va3 the pieces
        private const string VTool = "vt";                               // vt<job> "store,slot"; vt<job>s the stack
        private const string VLine = "vl";                               // 0/1; vlk the key, vla the ammo item, vln the count

        /// <summary>Round mt1 (part G), the server: her equipment column now (main thread).</summary>
        internal static GearView BuildView(EntityAlive npc)
        {
            var v = new GearView();
            if (npc == null) return v;
            v.NpcId = npc.entityId;
            v.Weapons[0] = Weapon(npc, true);
            v.Weapons[1] = Weapon(npc, false);
            ItemStack[] a = Armor(npc);
            v.ArmorState = a == null ? GearView.ArmorNone : GearView.ArmorSent;
            for (int i = 0; a != null && i < a.Length && i < v.Armor.Length; i++) v.Armor[i] = a[i] ?? ItemStack.Empty.Clone();
            ToolPick[] t = ServerTools(npc);
            for (int j = 0; j < JobCount; j++) v.Tools[j] = t[j];
            string key, ammo;
            int n;
            if (ScAmmoChoice.LineParts(npc, out key, out ammo, out n))
            {
                v.AmmoShow = true;
                v.AmmoKey = key ?? "";
                v.AmmoItem = ammo ?? "";
                v.AmmoCount = n;
            }
            return v;
        }

        /// <summary>
        /// Round mt1 (part G), the server: the view into the reply's hint, at most budget more characters (NetPackageScResult has
        /// no cap of its own; the game's connection takes a package of up to about 2 MB). What a client needs most goes first:
        /// the weapons and the ammo line; then the armor (left out: the client builds it from its copy of her look); then the
        /// tools (left out: its window alone). False when not even the weapons fit (the client keeps its last view and asks again).
        /// </summary>
        internal static bool PutView(ScActionReply reply, GearView v, int budget)
        {
            if (reply == null || v == null) return false;
            try
            {
                var core = new ScActionReply();
                core.SetHint(VKey, VVersion);
                for (int w = 0; w < 2; w++) PutWeapon(core, VWeapon[w], v.Weapons[w]);
                if (v.AmmoShow && Safe(v.AmmoKey) && Safe(v.AmmoItem))
                    core.SetHint(VLine, 1).SetHint(VLine + "k", v.AmmoKey).SetHint(VLine + "a", v.AmmoItem).SetHint(VLine + "n", v.AmmoCount);
                else core.SetHint(VLine, 0);

                var armor = new ScActionReply();
                int state = v.ArmorState;
                if (state == GearView.ArmorSent)
                {
                    for (int i = 0; i < v.Armor.Length && state == GearView.ArmorSent; i++)
                    {
                        string h = StackHint(v.Armor[i]);
                        if (h == null) state = GearView.ArmorLeftOut;        // one piece too large: the client's own look for all four
                        else if (h.Length > 0) armor.SetHint(VArmor + i.ToString(CultureInfo.InvariantCulture), h);
                    }
                    if (state != GearView.ArmorSent) armor = new ScActionReply();
                }
                armor.SetHint(VArmor, state);

                var tools = new ScActionReply();
                for (int j = 0; j < JobCount; j++)
                {
                    ToolPick t = v.Tools[j];
                    string h = t != null && t.Store != StoreWindow ? StackHint(t.Stack) : null;
                    if (string.IsNullOrEmpty(h)) continue;
                    string k = VTool + j.ToString(CultureInfo.InvariantCulture);
                    tools.SetHint(k, t.Store.ToString(CultureInfo.InvariantCulture) + "," + t.Slot.ToString(CultureInfo.InvariantCulture)).SetHint(k + "s", h);
                }

                string all = Join(core.Hint, armor.Hint, tools.Hint);
                if (all.Length > budget)
                {
                    all = Join(core.Hint, armor.Hint);
                    if (all.Length > budget) all = Join(core.Hint, VArmor + "=" + GearView.ArmorLeftOut.ToString(CultureInfo.InvariantCulture));
                    if (all.Length > budget) return false;
                }
                reply.Hint = Join(reply.Hint, all);
                return true;
            }
            catch (Exception e)
            {
                ScLog.Once("gear-view-put", e);
                return false;
            }
        }

        private static void PutWeapon(ScActionReply r, string p, WeaponView w)
        {
            int kind = w != null ? w.Kind : KindNone;
            string item = w != null ? w.Item ?? "" : "";
            if (!Safe(item)) { kind = KindNone; item = ""; }
            r.SetHint(p + "k", kind);
            if (item.Length > 0) r.SetHint(p + "i", item);
            if (kind != KindItem) return;
            string s = StackHint(w.Stack);
            if (!string.IsNullOrEmpty(s)) r.SetHint(p + "s", s);        // too large to send: the name and line alone
            r.SetHint(p + "p", (w.Parts ?? new InfoParts()).Encode());
        }

        /// <summary>
        /// Round mt1 (part G), a client: the view a reply carries, with every text in this machine's language; null when the
        /// reply has none (a refusal, a reply whose view did not fit, a server of another version).
        /// </summary>
        internal static GearView ReadView(ScActionReply reply, int npcId)
        {
            if (reply == null || !reply.Ok || reply.HintStr(VKey, null) != VVersion) return null;
            try
            {
                var v = new GearView { NpcId = npcId };
                for (int w = 0; w < 2; w++) v.Weapons[w] = ReadWeapon(reply, VWeapon[w]);
                v.ArmorState = reply.HintInt(VArmor, GearView.ArmorLeftOut);
                if (v.ArmorState == GearView.ArmorSent)
                    for (int i = 0; i < v.Armor.Length; i++) v.Armor[i] = FirstStack(reply.HintStr(VArmor + i.ToString(CultureInfo.InvariantCulture)));
                for (int j = 0; j < JobCount; j++)
                {
                    string k = VTool + j.ToString(CultureInfo.InvariantCulture);
                    string at = reply.HintStr(k, null);
                    if (at == null) continue;
                    string[] p = at.Split(',');
                    int store, slot;
                    if (p.Length != 2 || !int.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out store)
                        || !int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out slot) || store == StoreWindow) continue;
                    ItemStack st = FirstStack(reply.HintStr(k + "s"));
                    if (!st.IsEmpty()) v.Tools[j] = new ToolPick { Stack = st, Store = store, Slot = slot };
                }
                v.AmmoShow = reply.HintInt(VLine, 0) > 0;
                if (v.AmmoShow)
                {
                    v.AmmoKey = reply.HintStr(VLine + "k", "scInvAmmoLine");
                    v.AmmoItem = reply.HintStr(VLine + "a");
                    v.AmmoCount = reply.HintInt(VLine + "n", 0);
                }
                return v;
            }
            catch (Exception e)
            {
                ScLog.Once("gear-view-read", e);
                return null;
            }
        }

        private static WeaponView ReadWeapon(ScActionReply reply, string p)
        {
            var w = new WeaponView { Kind = reply.HintInt(p + "k", KindNone), Item = reply.HintStr(p + "i") };
            switch (w.Kind)
            {
                case KindNoRanged:
                    w.Name = Localization.Get("scInvNoRanged");
                    break;
                case KindToken:
                    w.Name = w.Item;
                    break;
                case KindItem:
                    ItemStack s = FirstStack(reply.HintStr(p + "s"));
                    ItemClass ic = ItemClass.GetItemClass(w.Item, true);
                    if (ic == null && !s.IsEmpty()) ic = s.itemValue.ItemClass;
                    w.Name = ic != null ? ic.GetLocalizedItemName() : w.Item;
                    w.Stack = s;                       // empty when it was too large to send: the name and the line alone
                    w.Parts = InfoParts.Decode(reply.HintStr(p + "p"));
                    w.Info = FormatInfo(w.Parts);
                    break;
                default:
                    w.Kind = KindNone;
                    w.Name = Localization.Get("scInvNone");
                    break;
            }
            return w;
        }

        /// <summary>One stack for a hint: "" for none, null when it is too large to send (ScItemTx.MaxEncoded).</summary>
        private static string StackHint(ItemStack s)
        {
            if (s == null || s.IsEmpty()) return "";
            return ScItemTx.StacksToHint(new List<ItemStack> { s });
        }

        private static ItemStack FirstStack(string hint)
        {
            List<ItemStack> l = ScItemTx.HintToStacks(hint);
            return l.Count > 0 ? l[0] : ItemStack.Empty.Clone();
        }

        /// <summary>A hint value may hold neither ';' nor '='.</summary>
        private static bool Safe(string s)
        {
            return s != null && s.IndexOf(';') < 0 && s.IndexOf('=') < 0;
        }

        private static string Join(params string[] hints)
        {
            var parts = new List<string>();
            foreach (string h in hints) if (!string.IsNullOrEmpty(h)) parts.Add(h);
            return string.Join(";", parts.ToArray());
        }
    }
}
