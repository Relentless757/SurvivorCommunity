using System;
using System.Collections.Generic;
using HarmonyLib;

namespace SurvivorCommunity
{
    /// <summary>
    /// Facilities (round wl): water sources inside the home zone that give the stockpile water every day, and that the
    /// player upgrades with stockpile materials by holding E on them (the radial: Info first, so a tap only reads it;
    /// Upgrade second).
    ///   Wells          V3-OakravenWaterFillers' five models (WellBlocks, in level order): the block IS the level, and an
    ///                  upgrade swaps it for the next model (a multi-block swap: the old children go with the old parent).
    ///                  Level x WellWaterPerLevel water a day (2/4/6/8/10). The well kit places Level 1 only
    ///                  (WellsPlaceLevelOne): its model picker is cut to the first model at world start.
    ///   Dew collectors vanilla's (DewCollectorBlocks): one block, the level kept in the registry by position
    ///                  (Community.DewLevels, 1 when not listed). Level x DewWaterPerLevel a day (1..5); it still fills
    ///                  its own jars as before.
    /// Upgrades cost FacilityUpgradeCost[level - 1] materials (4/8/12/16 for levels 2..5). ScScan counts the water with
    /// the zone (only parents in the zone count, at most once each); ScStockpile adds it at the rollover. The hooks are
    /// Harmony patches by block type, not XML: this mod loads before V3-OakravenWaterFillers, so its blocks.xml cannot
    /// reach that mod's blocks. Host only, like the stockpile.
    /// </summary>
    public static class ScFacilities
    {
        public const string CmdInfo = "scfacinfo";
        public const string CmdUpgrade = "scfacupgrade";

        private static Block[] tableFor;
        /// <summary>Per block type: 0 = not a well, else its level (1 = WellBlocks[0]).</summary>
        private static byte[] wellLevel;
        private static bool[] dew;
        private static int wellTypes, dewTypes;
        /// <summary>The well kits RestrictWellKit cut to Level 1 (block ids of this world): placed, they keep the ghost's rotation.</summary>
        private static readonly HashSet<int> cutKits = new HashSet<int>();

        /// <summary>Per-world reset (ModEvents.GameStarting): the table belongs to the old Block.list.</summary>
        public static void Reset()
        {
            tableFor = null;
            wellLevel = null;
            dew = null;
            wellTypes = dewTypes = 0;
            cutKits.Clear();
        }

        public static bool IsCutKit(int blockId) { return cutKits.Count > 0 && cutKits.Contains(blockId); }

        /// <summary>Classifies every block type once per world (Block.list is a new array on every load).</summary>
        public static bool EnsureTable()
        {
            Block[] list = Block.list;
            if (list == null) return false;
            if (wellLevel != null && ReferenceEquals(list, tableFor)) return true;
            ScConfig.EnsureParsed();
            var w = new byte[list.Length];
            var d = new bool[list.Length];
            int wt = 0, dt = 0;
            for (int i = 0; i < ScConfig.WellBlocks.Count && i < 250; i++)
            {
                Block b = Block.GetBlockByName(ScConfig.WellBlocks[i], true);
                if (b == null || b.blockID <= 0 || b.blockID >= w.Length) continue;
                w[b.blockID] = (byte)(i + 1);
                wt++;
            }
            for (int i = 0; i < ScConfig.DewCollectorBlocks.Count; i++)
            {
                Block b = Block.GetBlockByName(ScConfig.DewCollectorBlocks[i], true);
                if (b == null || b.blockID <= 0 || b.blockID >= d.Length) continue;
                if (w[b.blockID] != 0) continue;          // listed as both: the well wins
                d[b.blockID] = true;
                dt++;
            }
            wellLevel = w;
            dew = d;
            tableFor = list;
            wellTypes = wt;
            dewTypes = dt;
            ScLog.Out("facilities: " + wt + " well level(s) (of " + ScConfig.WellBlocks.Count + " listed), " + dt + " dew collector block(s) recognised");
            return true;
        }

        public static int WellTypes { get { EnsureTable(); return wellTypes; } }
        public static int DewTypes { get { EnsureTable(); return dewTypes; } }

        /// <summary>0 = not a well; else the well's level (its model).</summary>
        public static int WellLevel(int type)
        {
            byte[] w = wellLevel;
            if (w == null || !ReferenceEquals(Block.list, tableFor)) { if (!EnsureTable()) return 0; w = wellLevel; }
            return type > 0 && type < w.Length ? w[type] : 0;
        }

        public static bool IsDew(int type)
        {
            bool[] d = dew;
            if (d == null || !ReferenceEquals(Block.list, tableFor)) { if (!EnsureTable()) return false; d = dew; }
            return type > 0 && type < d.Length && d[type];
        }

        public static bool IsFacility(int type) { return WellLevel(type) > 0 || IsDew(type); }

        /// <summary>The top level: the cost list's length + 1, and for wells no more than the models there are.</summary>
        public static int MaxLevel(int type)
        {
            int top = ScConfig.FacilityUpgradeCost.Length + 1;
            if (WellLevel(type) > 0) top = Math.Min(top, wellTypes);
            return Math.Max(1, top);
        }

        /// <summary>A facility's level at its parent position (a dew collector not in the registry is level 1).</summary>
        public static int LevelAt(Vector3i parent, BlockValue bv)
        {
            int wl = WellLevel(bv.type);
            if (wl > 0) return wl;
            if (!IsDew(bv.type)) return 0;
            int lv;
            return ScRegistry.Loaded && ScRegistry.Community.DewLevels.TryGetValue(parent, out lv) ? Math.Max(1, Math.Min(lv, MaxLevel(bv.type))) : 1;
        }

        /// <summary>Water a day from one facility (parent block) at its level.</summary>
        public static int WaterAt(Vector3i parent, BlockValue bv)
        {
            int wl = WellLevel(bv.type);
            if (wl > 0) return wl * ScConfig.WellWaterPerLevel;
            if (IsDew(bv.type)) return LevelAt(parent, bv) * ScConfig.DewWaterPerLevel;
            return 0;
        }

        /// <summary>Materials the next level costs (0 at the top).</summary>
        public static int NextCost(int level)
        {
            int[] cost = ScConfig.FacilityUpgradeCost;
            int i = level - 1;
            return i >= 0 && i < cost.Length ? Math.Max(0, cost[i]) : 0;
        }

        private static Vector3i Parent(WorldBase world, Vector3i pos, ref BlockValue bv)
        {
            if (bv.ischild && bv.Block != null && bv.Block.multiBlockPos != null)
            {
                Vector3i p = bv.Block.multiBlockPos.GetParentPos(pos, bv);
                bv = world.GetBlock(p);
                return p;
            }
            return pos;
        }

        private static bool InHome(Vector3i pos)
        {
            return ScRegistry.Loaded && ScRegistry.Community.HasHome && ScZone.Contains(pos);
        }

        // ------------------------------------------------------------------ the radial ----

        private static readonly BlockActivationCommand[] InfoOnly = { new BlockActivationCommand(CmdInfo, "water", true) };
        private static readonly BlockActivationCommand[] InfoAndUpgrade =
        {
            new BlockActivationCommand(CmdInfo, "water", true),
            new BlockActivationCommand(CmdUpgrade, "block_upgrade", true)
        };

        /// <summary>
        /// The facility's commands: Info, and Upgrade while it is inside the home zone and below its top level. The game
        /// shows an entry for every enabled command in order, and a tap runs the first one.
        /// </summary>
        public static BlockActivationCommand[] Commands(WorldBase world, Vector3i pos, BlockValue bv)
        {
            Vector3i parent = Parent(world, pos, ref bv);
            bool canUp = ScCommunity.IsHost && InHome(parent) && LevelAt(parent, bv) < MaxLevel(bv.type);
            return canUp ? InfoAndUpgrade : InfoOnly;
        }

        /// <summary>Appends the facility commands to a block's own (a new array: the game reuses its own between calls).</summary>
        public static BlockActivationCommand[] Append(BlockActivationCommand[] own, BlockActivationCommand[] ours)
        {
            int n = own != null ? own.Length : 0;
            var all = new BlockActivationCommand[n + ours.Length];
            for (int i = 0; i < n; i++) all[i] = own[i];
            for (int i = 0; i < ours.Length; i++) all[n + i] = ours[i];
            return all;
        }

        /// <summary>A facility command was picked. False for anybody else's command.</summary>
        public static bool Handle(string cmd, WorldBase world, Vector3i pos, BlockValue bv, EntityPlayerLocal player)
        {
            if (cmd != CmdInfo && cmd != CmdUpgrade) return false;
            try
            {
                Vector3i parent = Parent(world, pos, ref bv);
                if (!IsFacility(bv.type)) return true;
                if (cmd == CmdInfo)
                {
                    ScCommunity.Tell(player, InfoText(parent, bv), "");
                    return true;
                }
                string msg;
                bool ok = TryUpgrade(world as World, parent, bv, player, out msg);
                ScCommunity.Tell(player, msg ?? "", ok ? "" : "ui_denied");
            }
            catch (Exception e)
            {
                ScLog.Once("facility-activate", e);
            }
            return true;
        }

        /// <summary>
        /// A well's focus prompt, "Press [E] to check Oak's Stone Well (level 2) - hold [E] to upgrade". The game formats
        /// it with the key's markup once more: {0} stays a placeholder (the name has no braces).
        /// </summary>
        public static string PromptText(WorldBase world, Vector3i pos, BlockValue bv)
        {
            Vector3i parent = Parent(world, pos, ref bv);
            int level = LevelAt(parent, bv);
            bool canUp = ScCommunity.IsHost && InHome(parent) && level < MaxLevel(bv.type);
            return string.Format(Localization.Get(canUp ? "scFacPromptUp" : "scFacPrompt"), "{0}", NameOf(bv), level);
        }

        public static string NameOf(BlockValue bv)
        {
            try { return bv.Block != null ? bv.Block.GetLocalizedBlockName() : "?"; }
            catch (Exception) { return "?"; }
        }

        /// <summary>"Oak's Stone Well - level 2 of 5: 4 water a day for the community. Level 3: 8 materials (12 in the stockpile)."</summary>
        public static string InfoText(Vector3i parent, BlockValue bv)
        {
            int level = LevelAt(parent, bv), top = MaxLevel(bv.type);
            string name = NameOf(bv);
            if (!ScCommunity.IsHost) return string.Format(Localization.Get("scFacInfoClient"), name, level, top);
            if (!InHome(parent)) return string.Format(Localization.Get("scFacInfoOutside"), name, level, top);
            string s = string.Format(Localization.Get("scFacInfo"), name, level, top, WaterAt(parent, bv));
            if (level >= top) return s + " " + Localization.Get("scFacInfoTop");
            return s + " " + string.Format(Localization.Get("scFacInfoNext"), level + 1, NextCost(level), ScRegistry.Community.Get(Resource.Materials));
        }

        /// <summary>One level up for stockpile materials: a well becomes the next model, a dew collector's level goes up.</summary>
        public static bool TryUpgrade(World world, Vector3i parent, BlockValue bv, EntityPlayerLocal player, out string message)
        {
            message = null;
            if (!ScCommunity.IsHost) { message = Localization.Get("scHostOnly"); return false; }
            if (world == null || !IsFacility(bv.type)) { message = Localization.Get("scInternal"); return false; }
            string name = NameOf(bv);
            if (!InHome(parent)) { message = string.Format(Localization.Get("scFacOutside"), name); return false; }
            int level = LevelAt(parent, bv), top = MaxLevel(bv.type);
            if (level >= top) { message = string.Format(Localization.Get("scFacMaxed"), name, level); return false; }
            int cost = NextCost(level);
            Community c = ScRegistry.Community;
            if (c.Get(Resource.Materials) < cost) { message = string.Format(Localization.Get("scFacNoMats"), cost, c.Get(Resource.Materials), level + 1); return false; }

            int wl = WellLevel(bv.type);
            if (wl > 0)
            {
                Block next = Block.GetBlockByName(ScConfig.WellBlocks[wl], true);    // WellBlocks[level] is the next model
                if (next == null) { message = Localization.Get("scInternal"); return false; }
                BlockValue nv = next.ToBlockValue();
                nv.rotation = bv.rotation;
                world.SetBlockRPC(parent, nv);
                ScLog.Out("facility: well at " + parent + " upgraded " + bv.Block.GetBlockName() + " -> " + next.GetBlockName() + " for " + cost + " materials");
                name = NameOf(nv);
            }
            else
            {
                c.DewLevels[parent] = level + 1;
                ScLog.Out("facility: dew collector at " + parent + " upgraded to level " + (level + 1) + " for " + cost + " materials");
            }
            c.Add(Resource.Materials, -cost);
            ScRegistry.SaveAtomic();
            ScScan.RunIfDue(0f);
            int water = level + 1;
            water *= wl > 0 ? ScConfig.WellWaterPerLevel : ScConfig.DewWaterPerLevel;
            message = string.Format(Localization.Get("scFacUpgraded"), name, level + 1, water, cost);
            return true;
        }

        // ------------------------------------------------------------------ the well kit ----

        /// <summary>
        /// The well kit (a SelectAlternates helper listing the well models) places Level 1 only: its model list is cut to
        /// WellBlocks[0] and the picker turned off. Turning SelectAlternates off matters: a kit already in a pack can carry
        /// a picked model index above 0, and every vanilla path that indexes the model list by it (placement, the icon,
        /// the name) is guarded by SelectAlternates. Placed with the picker off, the kit uses its one-entry list
        /// (Block.GetPlaceAltBlockValue) - and that branch of Block.OnBlockPlaceBefore takes a fresh BlockValue at rotation
        /// 0, so Patch_KitKeepsRotation puts the ghost's rotation back (the Fable review's M1). Every machine, the clients
        /// too: placement runs on the one placing.
        /// </summary>
        public static void RestrictWellKit()
        {
            try
            {
                ScConfig.EnsureParsed();
                if (!ScConfig.WellsPlaceLevelOne || ScConfig.WellBlocks.Count < 2) return;
                Block[] list = Block.list;
                if (list == null) return;
                var wells = new HashSet<string>(ScConfig.WellBlocks, StringComparer.OrdinalIgnoreCase);
                string first = ScConfig.WellBlocks[0];
                if (Block.GetBlockByName(first, true) == null) return;          // the wells mod is not installed
                for (int t = 1; t < list.Length; t++)
                {
                    Block b = list[t];
                    if (b == null || b.placeAltBlockNames == null || b.placeAltBlockNames.Length < 2) continue;
                    int hits = 0;
                    foreach (string n in b.placeAltBlockNames) if (n != null && wells.Contains(n.Trim())) hits++;
                    if (hits < 2) continue;
                    b.placeAltBlockNames = new[] { first };
                    b.placeAltBlockClasses = null;
                    b.SelectAlternates = false;
                    cutKits.Add(b.blockID);
                    ScLog.Out("facilities: the well kit " + b.GetBlockName() + " now places " + first + " (Level 1) only - upgrade it by holding E inside the community");
                }
            }
            catch (Exception e)
            {
                ScLog.Once("well-kit", e);
            }
        }
    }

    /// <summary>
    /// The facility radial. Wells are plain Block class blocks: Block's own HasBlockActivationCommands /
    /// GetBlockActivationCommands / OnBlockActivated(string) / GetActivationText run for them, and these patches answer
    /// for well types only. Dew collectors are BlockCollector, which overrides the first three (it always has commands):
    /// its two patches append and handle ours behind Search and Take.
    /// </summary>
    public static class Patches_Facilities
    {
        [HarmonyPatch(typeof(Block), nameof(Block.HasBlockActivationCommands))]
        public static class Patch_BlockHasCommands
        {
            [HarmonyPostfix]
            public static void Postfix(Block __instance, BlockValue _blockValue, ref bool __result)
            {
                try
                {
                    if (!__result && ScFacilities.WellLevel(_blockValue.type) > 0) __result = true;
                }
                catch (Exception e) { ScLog.Once("Block.HasBlockActivationCommands", e); }
            }
        }

        [HarmonyPatch(typeof(Block), nameof(Block.GetBlockActivationCommands))]
        public static class Patch_BlockGetCommands
        {
            [HarmonyPostfix]
            public static void Postfix(Block __instance, WorldBase _world, BlockValue _blockValue, Vector3i _blockPos, ref BlockActivationCommand[] __result)
            {
                try
                {
                    if (ScFacilities.WellLevel(_blockValue.type) <= 0) return;
                    BlockActivationCommand[] ours = ScFacilities.Commands(_world, _blockPos, _blockValue);
                    // ours first: a tap runs the first entry, and Info is the harmless one
                    __result = ScFacilities.Append(ours, __result);
                }
                catch (Exception e) { ScLog.Once("Block.GetBlockActivationCommands", e); }
            }
        }

        [HarmonyPatch(typeof(Block), nameof(Block.OnBlockActivated), new[] { typeof(string), typeof(WorldBase), typeof(Vector3i), typeof(BlockValue), typeof(EntityPlayerLocal) })]
        public static class Patch_BlockActivated
        {
            [HarmonyPrefix]
            public static bool Prefix(string _commandName, WorldBase _world, Vector3i _blockPos, BlockValue _blockValue, EntityPlayerLocal _player, ref bool __result)
            {
                try
                {
                    if (ScFacilities.WellLevel(_blockValue.type) <= 0) return true;
                    if (!ScFacilities.Handle(_commandName, _world, _blockPos, _blockValue, _player)) return true;
                    __result = true;
                    return false;
                }
                catch (Exception e) { ScLog.Once("Block.OnBlockActivated", e); return true; }
            }
        }

        [HarmonyPatch(typeof(Block), nameof(Block.GetActivationText))]
        public static class Patch_BlockActivationText
        {
            [HarmonyPostfix]
            public static void Postfix(WorldBase _world, BlockValue _blockValue, Vector3i _blockPos, ref string __result)
            {
                try
                {
                    if (__result != null || ScFacilities.WellLevel(_blockValue.type) <= 0) return;
                    __result = ScFacilities.PromptText(_world, _blockPos, _blockValue);
                }
                catch (Exception e) { ScLog.Once("Block.GetActivationText", e); }
            }
        }

        /// <summary>
        /// The cut well kit: Block.OnBlockPlaceBefore's non-picker branch swaps in GetBlockValue(first model) at rotation
        /// 0; the rotation the ghost showed (set before this call) is put back on it.
        /// </summary>
        [HarmonyPatch(typeof(Block), nameof(Block.OnBlockPlaceBefore))]
        public static class Patch_KitKeepsRotation
        {
            [HarmonyPrefix]
            public static void Prefix(ref BlockPlacement.Result _bpResult, out byte __state)
            {
                __state = _bpResult.blockValue.rotation;
            }

            [HarmonyPostfix]
            public static void Postfix(Block __instance, ref BlockPlacement.Result _bpResult, byte __state)
            {
                try
                {
                    if (__instance != null && ScFacilities.IsCutKit(__instance.blockID)) _bpResult.blockValue.rotation = __state;
                }
                catch (Exception e) { ScLog.Once("Block.OnBlockPlaceBefore", e); }
            }
        }

        [HarmonyPatch(typeof(BlockCollector), nameof(BlockCollector.GetBlockActivationCommands))]
        public static class Patch_CollectorGetCommands
        {
            [HarmonyPostfix]
            public static void Postfix(WorldBase _world, BlockValue _blockValue, Vector3i _blockPos, ref BlockActivationCommand[] __result)
            {
                try
                {
                    if (!ScFacilities.IsDew(_blockValue.type)) return;
                    // behind Search and Take: a tap still searches
                    __result = ScFacilities.Append(__result, ScFacilities.Commands(_world, _blockPos, _blockValue));
                }
                catch (Exception e) { ScLog.Once("BlockCollector.GetBlockActivationCommands", e); }
            }
        }

        [HarmonyPatch(typeof(BlockCollector), nameof(BlockCollector.OnBlockActivated), new[] { typeof(string), typeof(WorldBase), typeof(Vector3i), typeof(BlockValue), typeof(EntityPlayerLocal) })]
        public static class Patch_CollectorActivated
        {
            [HarmonyPrefix]
            public static bool Prefix(string _commandName, WorldBase _world, Vector3i _blockPos, BlockValue _blockValue, EntityPlayerLocal _player, ref bool __result)
            {
                try
                {
                    if (!ScFacilities.IsDew(_blockValue.type)) return true;
                    if (!ScFacilities.Handle(_commandName, _world, _blockPos, _blockValue, _player)) return true;
                    __result = true;
                    return false;
                }
                catch (Exception e) { ScLog.Once("BlockCollector.OnBlockActivated", e); return true; }
            }
        }
    }
}
