using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using NPCSdcsCharacters.Pure;
using UAI;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round 29a (J29, 3.4 only): 0-XNPCCore's jobs on the community's members - the user's decisions of 2026-11-11 and the
    /// job audit's fixes (session scratch x3\jobs\audit_jobs.md). ScWork keeps the shift side (the ledger, the full pack, the
    /// repair duty); ScLoadout the work kit itself. Here, each with its own counter (sc craft, the home jobs part):
    ///
    ///  - Home jobs only for residents (decision 2). Chop wood (3), Pick (6), Butcher (7), Base repair (8), Combat repair (9)
    ///    and Craft (10) are a resident's; Follow + loot (1), Loot here (2), Mine (4) and Dig clay (5) are refused with a
    ///    friendly line that says to take her along: Voice Control's job order (VCCommandRouter.MutateLoot: the Jobs page,
    ///    the order wheel, a routed voice order) and its spoken "loot" phrase (PhraseTriggerHandler.Loot, which sets
    ///    Follow + loot straight away). Followers keep every job. ScWork.Tick takes back a field job that slips through.
    ///  - Output into the crates (decision 3): a postfix on 0-XNPCCore's SdcsStores.WindowAdd notes what a member on a work
    ///    shift puts in her inventory window on a home job (modes 2-7); ScWork.Deposit moves it to the crates with what she
    ///    makes.
    ///  - Replanting (decision 4): when a member picks a ripe crop of the player's farm (a planted*3HarvestPlayer block, Pick
    ///    mode 6) and the plot is left empty, she replants it with that crop's seed - from her inventory window (the harvest)
    ///    or the home crates; with no seed the plot stays empty. ScScan counts a seed as a living crop, so the farm's count
    ///    only drops when no seed was found.
    ///  - The work kit is never a fighting weapon (decision 1): 0-XNPCCore's thrust pick (a stab through bars), its
    ///    block-breaker pick in a fight and the Carried weapons listing leave a kit-only tool out (ScLoadout.IsKitOnly);
    ///    ScArms.OnSwap puts the pair's melee weapon back when a swap put one in her hand (KitToolRefused); ScLoadout's
    ///    AfterPostInit when a save did; and (round 29 review M2) KitFightTick when a job kept its tool in her hand between
    ///    targets and a fight comes.
    ///  - Repair duty facts: 0-XNPCCore's own test of what she can repair (UAITaskBaseKeepSdcs.Repairable, PickRepairTool)
    ///    for ScWork.DutyTick, and what she says is missing at a duty's start.
    ///  - Squad orders never reach a resident: Voice Control's roster (XNPCVoiceControlMod.GetHiredRoster: the order wheel's
    ///    targets, its default target, Shift+click) and its spoken squad orders (VCCommandRouter.ExecuteForSquad) leave the
    ///    community's residents out - but the one a wheel is being opened on from her own ring or "Jobs and gear".
    ///  - A runner's inventory window: carried to her new entity id (ScRunners.Rekey) and, when she dies on the road with no
    ///    body, lost with her, said and logged (ScRunners' DieOffline).
    ///
    /// Every 0-XNPCCore and Voice Control target is bound by name and exact signature (internal and private ones through
    /// their assembly), each Prepare turns its patch off with one warning when it is gone, and ModApi's ExpectedTargets lists
    /// them. Positional arguments (__0, __1): their parameter names are not part of their contract.
    /// </summary>
    public static class ScJobs
    {
        private static readonly Dictionary<string, int> hits = new Dictionary<string, int>();

        internal static void Count(string what)
        {
            int n;
            hits.TryGetValue(what, out n);
            hits[what] = n + 1;
        }

        public static void Reset()
        {
            hits.Clear();
            replants.Clear();
            seedOf.Clear();
            kitChecked.Clear();
            kitDeferred.Clear();
            nextKitCheck = 0f;
            kitFightNext.Clear();
            gateRead = gateScanMissing = gateSightBlock = false;     // round 33a: the job gate is read again in the next world
            gateV4 = true;
            gateDistance = DefaultGateDistance;
            rosterLastLeftOut = -1;
        }

        /// <summary>The counters for sc craft (the work shifts' line).</summary>
        public static string Report()
        {
            var keys = new List<string>(hits.Keys);
            keys.Sort(StringComparer.Ordinal);
            var parts = new List<string>();
            foreach (string k in keys) parts.Add(k + "=" + hits[k]);
            return "jobs: " + (parts.Count > 0 ? string.Join(", ", parts.ToArray()) : "nothing yet") + (replants.Count > 0 ? ", replants waiting " + replants.Count : "")
                   + (kitDeferred.Count > 0 ? ", kit checks deferred now " + kitDeferred.Count : "");
        }

        /// <summary>The 0-XNPCCore assembly (NPCSdcsCharacters.dll): its internal types are looked up here.</summary>
        private static Assembly Xnpc { get { return typeof(AvatarSdcsNpcController).Assembly; } }

        private static bool Bound(Func<MethodBase> target, string label)
        {
            try
            {
                if (target() != null) return true;
                ScLog.Warn("0-XNPCCore method " + label + " not found - that SurvivorCommunity job patch (round 29a) is off (0-XNPCCore update?)");
            }
            catch (Exception e) { ScLog.Once("jobs-bind-" + label, e); }
            return false;
        }

        /// <summary>The same for a Voice Control target: quiet without Voice Control, a warning with it.</summary>
        private static bool BoundVc(Func<MethodBase> target, string label)
        {
            try
            {
                if (target() != null) return true;
                if (Patches_VoiceControl.ChatType != null) ScLog.Warn("Voice Control method " + label + " not found - that SurvivorCommunity job patch (round 29a) is off (Voice Control update?)");
            }
            catch (Exception e) { ScLog.Once("jobs-bind-" + label, e); }
            return false;
        }

        private static MethodInfo Returning(MethodInfo m, Type ret, bool isStatic)
        {
            return m != null && m.ReturnType == ret && m.IsStatic == isStatic ? m : null;
        }

        private static FastTags<TagGroup.Global> meleeTag;
        private static bool meleeTagParsed;

        private static FastTags<TagGroup.Global> MeleeTag
        {
            get
            {
                if (!meleeTagParsed) { meleeTag = FastTags<TagGroup.Global>.Parse("melee"); meleeTagParsed = true; }
                return meleeTag;
            }
        }

        private static string ItemLabel(string item)
        {
            ItemClass ic = ItemClass.GetItemClass(item, true);
            return ic != null ? ic.GetLocalizedItemName() : item;
        }

        // ================================================================== home jobs only for residents (decision 2) ====

        /// <summary>A follower's job, refused for residents: Follow + loot (1), Loot here (2), Mine (4), Dig clay (5).</summary>
        public static bool IsFieldJob(int mode) { return mode == 1 || mode == 2 || mode == 4 || mode == 5; }

        /// <summary>
        /// The job mode Voice Control's MutateLoot sets for this argument, by its own tests in its own order: craft:..., harvest,
        /// mine..., plants / crops, clay, butcher, the repairs (baserepair, combatrepair, repair), here - and anything else
        /// (the empty argument of the Jobs page's "Follow + loot" among it) is Follow + loot.
        /// </summary>
        private static int ModeOfArg(string arg)
        {
            if (arg != null && arg.StartsWith("craft:", StringComparison.Ordinal)) return 10;
            if (string.Equals(arg, "harvest", StringComparison.OrdinalIgnoreCase)) return 3;
            if (LootJobRules.MineFilterFromArg(arg) >= 0) return 4;
            if (LootJobRules.PickFilterFromArg(arg) >= 0) return 6;
            if (string.Equals(arg, "clay", StringComparison.OrdinalIgnoreCase)) return 5;
            if (string.Equals(arg, "butcher", StringComparison.OrdinalIgnoreCase)) return 7;
            if (string.Equals(arg, "combatrepair", StringComparison.OrdinalIgnoreCase)) return 9;
            if (string.Equals(arg, "baserepair", StringComparison.OrdinalIgnoreCase) || string.Equals(arg, "repair", StringComparison.OrdinalIgnoreCase)) return 8;
            if (string.Equals(arg, "here", StringComparison.OrdinalIgnoreCase)) return 2;
            return 1;
        }

        private static string ModeName(int mode)
        {
            switch (mode)
            {
                case 1: return "Follow + loot";
                case 2: return "Loot here";
                case 4: return "Mine";
                case 5: return "Dig clay";
                default: return "job mode " + mode;
            }
        }

        /// <summary>One of the community's members who is a resident (at home, on a talk hold, a work shift or a watch) - null for anyone else.</summary>
        private static Member Resident(EntityAlive npc)
        {
            if (npc == null || !ScRegistry.Loaded) return null;
            Member m = ScRegistry.GetMember(npc.entityId);
            return m != null && m.Status == MemberStatus.Resident ? m : null;
        }

        /// <summary>Her line for a field job: Follow + loot has its own; on 3.4 with mining runs offered, Mine and Dig clay
        /// point to the run (round 33c) instead of "take me along".</summary>
        private static string FieldJobLine(int mode)
        {
            if (mode == 1) return Localization.Get("scJobResidentFollowLoot");
#if SC_V34
            if ((mode == 4 || mode == 5) && ScConfig.MiningRuns) return Localization.Get("scJobResidentMine");
#endif
            return Localization.Get("scJobResidentField");
        }

        /// <summary>The refusal as a tooltip in her words (the order wheel and the Jobs page; ScWork's take-back).</summary>
        public static void TellFieldJob(Member m, EntityAlive npc, int mode)
        {
            EntityPlayerLocal p = ScCommunity.LocalPlayer;
            if (p == null || m == null) return;
            ScCommunity.Tell(p, string.Format(Localization.Get("scEncCallSays"), m.Name, FieldJobLine(mode)), "ui_denied");
        }

        public static Type RouterType { get { return ScTypes.Find("XNPCVoiceControl.Net.VCCommandRouter"); } }

        /// <summary>Voice Control's job order: VCCommandRouter.MutateLoot(EntityAlive npc, string arg), internal static void.</summary>
        public static MethodBase MutateLootTarget()
        {
            Type t = RouterType;
            return t != null ? Returning(AccessTools.DeclaredMethod(t, "MutateLoot", new[] { typeof(EntityAlive), typeof(string) }), typeof(void), true) : null;
        }

        /// <summary>Voice Control's spoken "loot": PhraseTriggerHandler.Loot(EntityAlive, EntityPlayer, string npcName, string dialogText), private, returning her answer.</summary>
        public static MethodBase VoiceLootTarget()
        {
            Type t = Patches_VoiceControl.TriggerType;
            return t != null ? Returning(AccessTools.DeclaredMethod(t, "Loot", new[] { typeof(EntityAlive), typeof(EntityPlayer), typeof(string), typeof(string) }), typeof(string), false) : null;
        }

        /// <summary>
        /// J29 (decision 2): a field job given to a resident - from the Jobs page of her ring or talk, the order wheel or a routed
        /// voice order - is refused before Voice Control clears her current job, with her line as a tooltip. Followers, stand-ins
        /// and everyone else: Voice Control's own.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_FieldJobOrder
        {
            [HarmonyPrepare]
            public static bool Prepare() { return BoundVc(MutateLootTarget, "VCCommandRouter.MutateLoot(EntityAlive, string)"); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return MutateLootTarget(); }

            /// <summary>__0 the NPC, __1 the job's argument ("" = Follow + loot).</summary>
            [HarmonyPrefix]
            public static bool Prefix(EntityAlive __0, string __1)
            {
                try
                {
                    int mode = ModeOfArg(__1);
                    if (!IsFieldJob(mode)) return true;
                    Member m = Resident(__0);
                    if (m == null) return true;
                    Count("field-job-refused-" + mode);
                    TellFieldJob(m, __0, mode);
                    ScLog.Out(m.Name + " was given " + ModeName(mode) + " - a follower's job, refused for a resident (take her along for it)");
                    return false;
                }
                catch (Exception e)
                {
                    ScLog.Once("jobs-field-order", e);
                    return true;
                }
            }
        }

        /// <summary>J29 (decision 2): the spoken "loot" (Follow + loot) on a resident - her refusal is the phrase's answer.</summary>
        [HarmonyPatch]
        public static class Patch_FieldJobVoice
        {
            [HarmonyPrepare]
            public static bool Prepare() { return BoundVc(VoiceLootTarget, "PhraseTriggerHandler.Loot(EntityAlive, EntityPlayer, string, string)"); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return VoiceLootTarget(); }

            [HarmonyPrefix]
            public static bool Prefix(EntityAlive __0, ref string __result)
            {
                try
                {
                    Member m = Resident(__0);
                    if (m == null) return true;
                    Count("field-job-refused-voice");
                    __result = FieldJobLine(1);
                    ScLog.Out(m.Name + " was told to loot by voice - Follow + loot is a follower's job, refused for a resident");
                    return false;
                }
                catch (Exception e)
                {
                    ScLog.Once("jobs-field-voice", e);
                    return true;
                }
            }
        }

        // ================================================================== output into the crates (decision 3) ====

        /// <summary>0-XNPCCore's SdcsStores.WindowAdd(int entityId, ItemStack stack) on its internal SdcsStores: returns what did not fit.</summary>
        public static MethodBase WindowAddTarget()
        {
            Type t = Xnpc.GetType("SdcsStores", false);
            return t != null ? Returning(AccessTools.DeclaredMethod(t, "WindowAdd", new[] { typeof(int), typeof(ItemStack) }), typeof(int), true) : null;
        }

        /// <summary>
        /// J29 (decision 3): whatever 0-XNPCCore puts in a member's inventory window on a home job (harvest and butcher payouts,
        /// HarvestPayout; loot) while she is on a work shift in job mode 2-7 is noted on the shift's ledger (ScWork.NoteGathered),
        /// so ScWork.Deposit moves it to the crates. A talk hold counts too: the job's first payouts can come before ScWork's
        /// once-a-second tick has made the hold a shift. Craft output (mode 10) has its own ledger hooks. Round 33a (round 29
        /// glance LOW 2): it is also where a station's output reaches her window (0-XNPCCore's station MoveOut adds through
        /// WindowAdd): an item the shift expects from a station (ScWork.Expects) becomes owed as it arrives (ScWork.NoteArrived),
        /// in any job mode; what a home job adds beyond that is gathered as before.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_JobOutput
        {
            [HarmonyPrepare]
            public static bool Prepare() { return Bound(WindowAddTarget, "SdcsStores.WindowAdd(int, ItemStack)"); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return WindowAddTarget(); }

            /// <summary>High bit of __state's low word: the add came in job mode 2-7 (a home job's harvest).</summary>
            private const uint GatherBit = 0x80000000u;

            /// <summary>
            /// The item and count before the call (WindowAdd lowers a stack's count to what did not fit): type in the high word,
            /// count in the low word's 31 bits, GatherBit for job modes 2-7; 0 = not ours.
            /// </summary>
            [HarmonyPrefix]
            public static void Prefix(int __0, ItemStack __1, out long __state)
            {
                __state = 0;
                try
                {
                    if (__1 == null || __1.IsEmpty() || __1.itemValue == null || __1.count <= 0 || !ScWork.OnShift(__0)) return;
                    World w = ScCommunity.World;
                    EntityAlive npc = w != null ? w.GetEntity(__0) as EntityAlive : null;
                    int mode = SdcsLootMode.Mode(npc);
                    bool gathering = mode >= 2 && mode <= 7;
                    if (!gathering && !ScWork.Expects(__0, __1.itemValue.type)) return;
                    uint low = (uint)(__1.count & 0x7FFFFFFF) | (gathering ? GatherBit : 0u);
                    __state = ((long)__1.itemValue.type << 32) | low;
                }
                catch (Exception e) { ScLog.Once("jobs-output-pre", e); }
            }

            [HarmonyPostfix]
            public static void Postfix(int __0, int __result, long __state)
            {
                if (__state == 0) return;
                try
                {
                    uint low = (uint)(__state & 0xFFFFFFFFL);
                    int type = (int)(__state >> 32), count = (int)(low & 0x7FFFFFFFu), added = count - Math.Max(0, __result);
                    if (added <= 0) return;
                    bool gathering = (low & GatherBit) != 0;
                    int fromStation = ScWork.NoteArrived(__0, type, added, gathering);
                    if (fromStation > 0) Count("station-output-arrived");
                    if (gathering && added > fromStation) Count("output-noted");
                }
                catch (Exception e) { ScLog.Once("jobs-output", e); }
            }
        }

        /// <summary>Free slots of her inventory window (int.MaxValue without one, as 0-XNPCCore's own pause counts).</summary>
        public static int WindowFreeSlots(EntityAlive npc)
        {
            if (npc == null || !HarvestManager.Has(npc.entityId)) return int.MaxValue;
            ItemStack[] items = HarvestManager.GetOrCreate(npc.entityId).items;
            if (items == null) return int.MaxValue;
            int n = 0;
            foreach (ItemStack s in items) if (s == null || s.IsEmpty()) n++;
            return n;
        }

        private static PropertyInfo resumeSlots, chestReach;
        private static bool settingsBound;

        private static void BindSettings()
        {
            if (settingsBound) return;
            settingsBound = true;
            try
            {
                resumeSlots = AccessTools.Property(typeof(AvatarSdcsNpcController), "LootResumeFreeSlots");
                if (resumeSlots != null && (resumeSlots.PropertyType != typeof(int) || resumeSlots.GetGetMethod(true) == null || !resumeSlots.GetGetMethod(true).IsStatic)) resumeSlots = null;
                chestReach = AccessTools.Property(typeof(AvatarSdcsNpcController), "BaseKeeperChestReachMetres");
                if (chestReach != null && (chestReach.PropertyType != typeof(float) || chestReach.GetGetMethod(true) == null || !chestReach.GetGetMethod(true).IsStatic)) chestReach = null;
            }
            catch (Exception e) { ScLog.Once("jobs-bind-settings", e); }
        }

        /// <summary>0-XNPCCore's own resume rule: free window slots a paused job waits for (Settings.xml, 5 by default).</summary>
        public static int ResumeFreeSlots()
        {
            BindSettings();
            try { if (resumeSlots != null) return Math.Max(1, (int)resumeSlots.GetValue(null, null)); }
            catch (Exception e) { ScLog.Once("jobs-resume-slots", e); }
            return 5;
        }

        /// <summary>How far from a repair duty's pin 0-XNPCCore takes materials from chests (Settings.xml, 15 m by default).</summary>
        private static float ChestReach()
        {
            BindSettings();
            try { if (chestReach != null) return (float)chestReach.GetValue(null, null); }
            catch (Exception e) { ScLog.Once("jobs-chest-reach", e); }
            return 15f;
        }

        // ================================================================== replanting (decision 4) ====

        private sealed class Picked
        {
            public Vector3i Pos;
            public BlockValue Crop;
            public int MemberId;
            public float Due;
            public int Tries;
        }

        private static readonly List<Picked> replants = new List<Picked>();
        private static readonly Dictionary<int, string> seedOf = new Dictionary<int, string>();
        private const string PlayerCropSuffix = "3HarvestPlayer";

        private static Type harvestRunType;
        private static FieldInfo runTarget;

        /// <summary>0-XNPCCore's UAITaskHarvestSdcs.PickPlant(Context, Run): one pick of a plant (mode 6), private; Run is its private nested class.</summary>
        public static MethodBase PickPlantTarget()
        {
            if (harvestRunType == null) harvestRunType = typeof(UAITaskHarvestSdcs).GetNestedType("Run", BindingFlags.NonPublic);
            if (harvestRunType == null) return null;
            if (runTarget == null) runTarget = AccessTools.Field(harvestRunType, "Target");
            if (runTarget == null || runTarget.FieldType != typeof(Vector3i)) return null;
            return Returning(AccessTools.DeclaredMethod(typeof(UAITaskHarvestSdcs), "PickPlant", new[] { typeof(UAI.Context), harvestRunType }), typeof(bool), false);
        }

        /// <summary>A crop of the player's farm (vanilla's planted*3HarvestPlayer: what 0-XNPCCore's "Pick crops" picks).</summary>
        private static bool IsPlayerCrop(Block b)
        {
            string n = b != null ? b.GetBlockName() : null;
            return n != null && n.StartsWith("planted", StringComparison.OrdinalIgnoreCase) && n.EndsWith(PlayerCropSuffix, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// J29 (decision 4): a pick by a member on a ripe crop of the player's farm that leaves the plot empty queues the plot to
        /// be replanted (Tick, a second later: the block change has landed by then).
        /// </summary>
        [HarmonyPatch]
        public static class Patch_PickReplant
        {
            [HarmonyPrepare]
            public static bool Prepare() { return Bound(PickPlantTarget, "UAITaskHarvestSdcs.PickPlant(Context, Run)"); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return PickPlantTarget(); }

            /// <summary>__0 the UAI context, __1 the run (its private type: as object).</summary>
            [HarmonyPrefix]
            public static void Prefix(UAI.Context __0, object __1, out object __state)
            {
                __state = null;
                try
                {
                    if (!ScConfig.ReplantCrops || __0 == null || __1 == null || runTarget == null) return;
                    EntityAlive self = __0.Self;
                    if (self == null || self.world == null || !ScXnpc.IsBody(self) || !ScRegistry.Loaded) return;
                    Member m = ScRegistry.GetMember(self.entityId);
                    if (m == null) return;
                    Vector3i pos = (Vector3i)runTarget.GetValue(__1);
                    BlockValue bv = self.world.GetBlock(pos);
                    if (bv.isair || bv.ischild || !IsPlayerCrop(bv.Block)) return;
                    __state = new Picked { Pos = pos, Crop = bv, MemberId = m.Id };
                }
                catch (Exception e) { ScLog.Once("jobs-pick-pre", e); }
            }

            [HarmonyPostfix]
            public static void Postfix(UAI.Context __0, object __state)
            {
                Picked p = __state as Picked;
                if (p == null) return;
                try
                {
                    EntityAlive self = __0 != null ? __0.Self : null;
                    if (self == null || self.world == null) return;
                    BlockValue now = self.world.GetBlock(p.Pos);
                    if (!now.isair && now.type == p.Crop.type) return;                  // still standing: more picks to come
                    foreach (Picked q in replants) if (q.Pos == p.Pos) return;
                    p.Due = Time.time + 1f;
                    replants.Add(p);
                    Count("crop-picked");
                }
                catch (Exception e) { ScLog.Once("jobs-pick", e); }
            }
        }

        /// <summary>
        /// The seed a crop of the player's farm grows from: vanilla's plantedX1 for plantedX3HarvestPlayer, else the first item
        /// its Harvest or Destroy drops name that is a growing plant (BlockPlantGrowing). Null when there is none. Cached.
        /// </summary>
        private static string SeedFor(Block crop)
        {
            if (crop == null) return null;
            string seed;
            if (seedOf.TryGetValue(crop.blockID, out seed)) return seed;
            seed = null;
            string name = crop.GetBlockName();
            if (name.EndsWith(PlayerCropSuffix, StringComparison.OrdinalIgnoreCase))
            {
                string guess = name.Substring(0, name.Length - PlayerCropSuffix.Length) + "1";
                if (Block.GetBlockByName(guess, true) is BlockPlantGrowing && ItemClass.GetItemClass(guess, true) != null) seed = Block.GetBlockByName(guess, true).GetBlockName();
            }
            if (seed == null && crop.itemsToDrop != null)
            {
                foreach (EnumDropEvent ev in new[] { EnumDropEvent.Harvest, EnumDropEvent.Destroy })
                {
                    List<Block.SItemDropProb> drops;
                    if (seed != null || !crop.itemsToDrop.TryGetValue(ev, out drops) || drops == null) continue;
                    foreach (Block.SItemDropProb d in drops)
                    {
                        if (string.IsNullOrEmpty(d.name) || !(Block.GetBlockByName(d.name, true) is BlockPlantGrowing) || ItemClass.GetItemClass(d.name, true) == null) continue;
                        seed = d.name;
                        break;
                    }
                }
            }
            seedOf[crop.blockID] = seed;
            if (seed == null) ScLog.Once("jobs-noseed-" + name, "replanting: no seed found for the crop " + name + " - a picked plot of it stays empty");
            return seed;
        }

        /// <summary>
        /// One seed of this type for the member's replanting: from her inventory window first (the harvest - and off the
        /// shift's ledger), unless someone has it open, else from the home crates. "window", "crates" or null (none).
        /// </summary>
        private static string TakeSeed(int memberId, int type)
        {
            if (HarvestManager.Has(memberId))
            {
                SCoreLootContainer window = HarvestManager.GetOrCreate(memberId);
                ItemStack[] items = window != null ? window.items : null;
                if (items != null && !window.IsUserAccessing() && !ScAmmo.IsRemoteOpen(memberId) && !ScAmmo.XnpcTradeOpen(memberId))
                {
                    for (int i = 0; i < items.Length; i++)
                    {
                        ItemStack s = items[i];
                        if (s == null || s.IsEmpty() || s.itemValue == null || s.itemValue.type != type) continue;
                        // 3.4: the window's slots are bound to its grid - written in place (HarvestManager.SetSlot clones)
                        HarvestManager.SetSlot(items, i, s.count > 1 ? new ItemStack(s.itemValue.Clone(), s.count - 1) : null);
                        window.Parent?.NotifyListeners();
                        ScWork.Unnote(memberId, type, 1);
                        return "window";
                    }
                }
            }
            Vector3i box;
            return ScStorage.TakeOneInZone(type, out box) ? "crates" : null;
        }

        /// <summary>Tick: the plots due to be replanted.</summary>
        private static void ReplantTick(World w)
        {
            if (replants.Count == 0) return;
            float now = Time.time;
            for (int i = replants.Count - 1; i >= 0; i--)
            {
                Picked p = replants[i];
                if (now < p.Due) continue;
                try
                {
                    if (w == null || w.GetChunkFromWorldPos(p.Pos) == null) { replants.RemoveAt(i); Count("replant-plot-unloaded"); continue; }
                    BlockValue cur = w.GetBlock(p.Pos);
                    if (!cur.isair)
                    {
                        // the change not seen yet (a second try), or something stands there now (the player planted it, a crop grew back)
                        if (cur.type == p.Crop.type && ++p.Tries < 3) { p.Due = now + 1f; continue; }
                        replants.RemoveAt(i);
                        Count("replant-plot-taken");
                        continue;
                    }
                    replants.RemoveAt(i);
                    Replant(w, p);
                }
                catch (Exception e)
                {
                    replants.RemoveAt(i);
                    ScLog.Once("jobs-replant", e);
                }
            }
        }

        private static void Replant(World w, Picked p)
        {
            Member m = ScRegistry.GetMember(p.MemberId);
            string who = m != null ? m.Name : "a member (" + p.MemberId + ")";
            string cropName = p.Crop.Block != null ? p.Crop.Block.GetBlockName() : "?";
            string seedName = SeedFor(p.Crop.Block);
            if (seedName == null) { Count("replant-no-seed-known"); return; }
            BlockValue seed = Block.GetBlockValue(seedName, true);
            ItemClass seedItem = ItemClass.GetItemClass(seedName, true);
            if (seed.isair || seed.Block == null || seedItem == null) { Count("replant-no-seed-known"); return; }
            seed.rotation = p.Crop.rotation;
            if (!seed.Block.CanPlaceBlockAt(w, p.Pos, seed))
            {
                Count("replant-cannot-place");
                ScLog.Verbose("replanting: " + who + " cannot plant " + seedName + " at " + p.Pos + " (the game refuses the spot: light, soil or room) - the plot stays empty");
                return;
            }
            string from = TakeSeed(p.MemberId, seedItem.Id);
            if (from == null)
            {
                Count("replant-no-seed");
                ScLog.Verbose("replanting: " + who + " picked " + cropName + " at " + p.Pos + " but has no " + seedName + " (her inventory, the home crates) - the plot stays empty");
                return;
            }
            w.SetBlockRPC(p.Pos, seed);
            Count("replanted-from-" + from);
            ScLog.Verbose("replanting: " + who + " replanted " + seedName + " at " + p.Pos + " (the seed from " + (from == "window" ? "her inventory" : "the home crates") + ")");
        }

        // ================================================================== the work kit is never a fighting weapon (decision 1) ====

        /// <summary>
        /// ScArms.OnSwap: a member was told to fight with a work-kit tool she carries only as her kit (the weapon menu, a spoken
        /// "use ...", or 0-XNPCCore's AnyWeapon falling back on the kit): the pair's melee weapon goes back in her hand, and
        /// (round 29 review L1) 0-XNPCCore is told so, so it does not remember the tool and put it back on a restore
        /// (ArmPairMelee). Said to the player for a swap they asked for (dialog, voice), not for AnyWeapon's own fallback.
        /// </summary>
        public static void KitToolRefused(EntityAlive npc, Member m, string tool, EntityPlayer player, string route)
        {
            try
            {
                string why;
                bool armed = ArmPairMelee(npc, out why);
                Loadout l = ScLoadout.Current(npc);
                Count("kit-tool-not-a-weapon-" + route);
                ScLog.Out(m.Name + " was told to fight with the work kit's " + tool + " (" + route + ") - kit tools are for jobs: "
                          + (armed ? "the " + (l != null ? l.Melee : "melee weapon") + " back in hand" : "could not re-arm (" + why + ")"));
                if (player != null && route != "any-weapon")
                    ScCommunity.Tell(player, string.Format(Localization.Get("scEncCallSays"), m.Name, string.Format(Localization.Get("scKitToolNotWeapon"), ItemLabel(tool))), "ui_denied");
            }
            catch (Exception e) { ScLog.Once("jobs-kit-refused", e); }
        }

        /// <summary>0-XNPCCore's private static SdcsCarriedThrustWeapon(EntityAlive): the thrust weapon it stabs with when a swing is blocked.</summary>
        public static MethodBase ThrustTarget()
        {
            return Returning(AccessTools.DeclaredMethod(typeof(AvatarSdcsNpcController), "SdcsCarriedThrustWeapon", new[] { typeof(EntityAlive) }), typeof(string), true);
        }

        /// <summary>0-XNPCCore's SdcsMeleeReach.BestBreaker(EntityAlive, BlockValue), private static: the item it breaks a block in its way with.</summary>
        public static MethodBase BreakerTarget()
        {
            return Returning(AccessTools.DeclaredMethod(typeof(SdcsMeleeReach), "BestBreaker", new[] { typeof(EntityAlive), typeof(BlockValue) }), typeof(string), true);
        }

        private static Func<ItemClass, MeleeStyleRules.MeleeStyle> meleeStyle;
        private static bool meleeStyleBound;

        /// <summary>0-XNPCCore's internal SdcsMeleeStyle.For(ItemClass): swing or thrust (its own cached rule). Null when not there.</summary>
        private static Func<ItemClass, MeleeStyleRules.MeleeStyle> MeleeStyle()
        {
            if (meleeStyleBound) return meleeStyle;
            meleeStyleBound = true;
            try
            {
                Type t = Xnpc.GetType("SdcsMeleeStyle", false);
                MethodInfo mi = t != null ? AccessTools.DeclaredMethod(t, "For", new[] { typeof(ItemClass) }) : null;
                if (mi != null && mi.IsStatic && mi.ReturnType == typeof(MeleeStyleRules.MeleeStyle))
                    meleeStyle = (Func<ItemClass, MeleeStyleRules.MeleeStyle>)Delegate.CreateDelegate(typeof(Func<ItemClass, MeleeStyleRules.MeleeStyle>), mi);
                else ScLog.Warn("0-XNPCCore's SdcsMeleeStyle.For(ItemClass) not found - a member blocked by bars stabs with no handed thrust weapon in place of a work-kit knife (0-XNPCCore update?)");
            }
            catch (Exception e) { ScLog.Once("jobs-bind-meleestyle", e); }
            return meleeStyle;
        }

        /// <summary>A melee item 0-XNPCCore would fight with: tagged melee, not a job-only tool, its first action no repair.</summary>
        private static bool FightingMelee(ItemClass ic)
        {
            return ic != null && ic.HasAnyTags(MeleeTag) && !AnyWeaponRules.IsJobOnlyTool(ic.GetItemName())
                   && !(ic.Actions != null && ic.Actions.Length > 0 && ic.Actions[0] is ItemActionRepair) && ic.Actions != null && ic.Actions.Length > 0;
        }

        /// <summary>Her bag's and inventory window's stacks, in 0-XNPCCore's store order (bag before window).</summary>
        private static IEnumerable<ItemStack> BagThenWindow(EntityAlive e)
        {
            ItemStack[] b = e.bag != null ? e.bag.ItemGrid.items : null;
            if (b != null) foreach (ItemStack s in b) if (s != null && !s.IsEmpty() && s.itemValue != null) yield return s;
            if (HarvestManager.Has(e.entityId))
            {
                ItemStack[] w = HarvestManager.GetOrCreate(e.entityId).items;
                if (w != null) foreach (ItemStack s in w) if (s != null && !s.IsEmpty() && s.itemValue != null) yield return s;
            }
        }

        /// <summary>
        /// J29: 0-XNPCCore's thrust pick walks the kit first, so a member with the work kit would stab through bars with its
        /// bone knife. A kit-only answer is replaced by the next thrust weapon 0-XNPCCore's order gives - one handed over into
        /// her bag or window - or none (X6 then answers the pair's melee weapon; the direct swap does not happen).
        /// </summary>
        [HarmonyPatch]
        public static class Patch_KitThrust
        {
            [HarmonyPrepare]
            public static bool Prepare() { return Bound(ThrustTarget, "AvatarSdcsNpcController.SdcsCarriedThrustWeapon(EntityAlive)"); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return ThrustTarget(); }

            [HarmonyPostfix]
            public static void Postfix(EntityAlive __0, ref string __result)
            {
                try
                {
                    if (string.IsNullOrEmpty(__result) || !ScXnpc.IsBody(__0) || !ScLoadout.IsKitOnly(__0, __result)) return;
                    string kit = __result;
                    __result = null;
                    Func<ItemClass, MeleeStyleRules.MeleeStyle> style = MeleeStyle();
                    if (style != null)
                    {
                        foreach (ItemStack s in BagThenWindow(__0))
                        {
                            ItemClass ic = s.itemValue.ItemClass;
                            if (!FightingMelee(ic) || ScLoadout.IsKitOnly(__0, ic.GetItemName()) || style(ic) != MeleeStyleRules.MeleeStyle.Thrust) continue;
                            __result = ic.GetItemName();
                            break;
                        }
                    }
                    Count("kit-thrust-replaced");
                    ScLog.Verbose(ScCommunity.DisplayName(__0) + ": no stab with the work kit's " + kit + " - " + (__result ?? "none (the pair's melee weapon)"));
                }
                catch (Exception e) { ScLog.Once("jobs-kit-thrust", e); }
            }
        }

        /// <summary>
        /// J29: in a fight 0-XNPCCore breaks a block in her way with her best block-breaking melee item, toolbelt included - with
        /// the work kit there, the stone shovel. A kit-only answer is replaced by the best of the rest (0-XNPCCore's own rule:
        /// the highest block damage above 0), or none.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_KitBreaker
        {
            [HarmonyPrepare]
            public static bool Prepare() { return Bound(BreakerTarget, "SdcsMeleeReach.BestBreaker(EntityAlive, BlockValue)"); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return BreakerTarget(); }

            [HarmonyPostfix]
            public static void Postfix(EntityAlive __0, BlockValue __1, ref string __result)
            {
                try
                {
                    if (string.IsNullOrEmpty(__result) || !ScXnpc.IsBody(__0) || !ScLoadout.IsKitOnly(__0, __result)) return;
                    string kit = __result, best = null;
                    float bestDamage = 0f;
                    var stores = new List<ItemStack>();
                    ItemStack[] belt = __0.inventory != null ? __0.inventory.ItemGrid.items : null;
                    if (belt != null) foreach (ItemStack s in belt) if (s != null && !s.IsEmpty() && s.itemValue != null) stores.Add(s);
                    stores.AddRange(BagThenWindow(__0));
                    foreach (ItemStack s in stores)
                    {
                        ItemClass ic = s.itemValue.ItemClass;
                        if (ic == null || !ic.HasAnyTags(MeleeTag) || ic.Actions == null || ic.Actions.Length == 0 || ic.Actions[0] is ItemActionRepair) continue;
                        if (ScLoadout.IsKitOnly(__0, ic.GetItemName())) continue;
                        float dmg = 0f;
                        ItemActionDynamic dyn = ic.Actions[0] as ItemActionDynamic;
                        ItemActionAttack atk = ic.Actions[0] as ItemActionAttack;
                        if (dyn != null) dmg = dyn.GetDamageBlock(s.itemValue, __1, __0);
                        else if (atk != null) dmg = atk.GetDamageBlock(s.itemValue, __1, __0);
                        if (dmg > 0f && dmg > bestDamage) { bestDamage = dmg; best = ic.GetItemName(); }
                    }
                    __result = best;
                    Count("kit-breaker-replaced");
                    ScLog.Verbose(ScCommunity.DisplayName(__0) + ": no block breaking with the work kit's " + kit + " - " + (best ?? "none"));
                }
                catch (Exception e) { ScLog.Once("jobs-kit-breaker", e); }
            }
        }

        /// <summary>
        /// J29: the "Carried weapons" listing (Voice Control's weapon menu, 0-XNPCCore's SdcsAnyWeaponMenuListing) walks the kit
        /// too: a kit-only tool is not offered as a weapon. A copy handed over into her window stays (it is hers to use).
        /// C1g's prefix on the same method empties the listing for a resident on a shift first.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_KitListing
        {
            [HarmonyPrepare]
            public static bool Prepare() { return Bound(Patches_Craft.AnyWeaponListingTarget, "AvatarSdcsNpcController.SdcsAnyWeaponMenuListing(EntityAlive)"); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return Patches_Craft.AnyWeaponListingTarget(); }

            /// <summary>__0 is the entity argument (bound by position: the controller also has an "entity" field).</summary>
            [HarmonyPostfix]
            public static void Postfix(EntityAlive __0, List<string> __result)
            {
                try
                {
                    if (__result == null || __result.Count == 0 || !ScXnpc.IsBody(__0)) return;
                    int n = __result.RemoveAll(x => ScLoadout.IsKitOnly(__0, x));
                    if (n > 0) Count("kit-listing-hidden");
                }
                catch (Exception e) { ScLog.Once("jobs-kit-listing", e); }
            }
        }

        // ================================================================== the work kit on every member's body ====

        /// <summary>Member ids whose body has had its work kit checked this session (EnsureWorkKit).</summary>
        private static readonly HashSet<int> kitChecked = new HashSet<int>();
        private static float nextKitCheck;
        private const float KitCheckSeconds = 5f;

        /// <summary>Round 33a (round 29 glance LOW 3): a member whose kit check was deferred - since when, the wait now, the next ask (Time.time).</summary>
        private sealed class KitDefer
        {
            public float Since, Wait, Next;
        }

        /// <summary>Member id -> her deferred kit check (KitTick's back-off); gone once checked, or once she is no member.</summary>
        private static readonly Dictionary<int, KitDefer> kitDeferred = new Dictionary<int, KitDefer>();
        private const float KitDeferCapSeconds = 120f;
        private static readonly List<int> deferScratch = new List<int>();

        /// <summary>A deferred check of someone who is no member any more is dropped (she would never be asked again).</summary>
        private static void PruneDeferred()
        {
            if (kitDeferred.Count == 0) return;
            deferScratch.Clear();
            foreach (int id in kitDeferred.Keys) if (ScRegistry.GetMember(id) == null) deferScratch.Add(id);
            foreach (int id in deferScratch) kitDeferred.Remove(id);
        }

        /// <summary>
        /// Every KitCheckSeconds: each member's loaded body not checked yet gets the pair + work kit item list
        /// (ScLoadout.EnsureWorkKit) - a recruit who joined after their PostInit, a body re-created under a new id. A body's
        /// own PostInit (a load, a re-create under the member's id) writes it anyway. Round 29 review L2: a body counts as
        /// checked only once EnsureWorkKit got past its guards (done) - one bandaging or with its pair not resolved yet at
        /// that tick is asked again at the next. Round 33a (round 29 glance LOW 3): one that stays deferred (a pair that never
        /// resolves - a weapon item gone from the game) is asked again after a wait that doubles from KitCheckSeconds up to
        /// KitDeferCapSeconds, and kit-check-deferred counts each member once (kit-check-resolved-late when it resolves later).
        /// </summary>
        private static void KitTick()
        {
            float now = Time.time;
            if (now < nextKitCheck || !ScRegistry.Loaded) return;
            nextKitCheck = now + KitCheckSeconds;
            PruneDeferred();
            foreach (Member m in ScRegistry.Members.Values)
            {
                if (m == null || kitChecked.Contains(m.Id)) continue;
                KitDefer d;
                bool deferred = kitDeferred.TryGetValue(m.Id, out d);
                if (deferred && now < d.Next) continue;
                EntityAlive npc = ScCommunity.FindNpc(m.Id);
                if (npc == null || npc.IsDead()) continue;
                try
                {
                    bool done;
                    if (ScLoadout.EnsureWorkKit(npc, out done)) Count("kit-given-late");
                    if (done)
                    {
                        kitChecked.Add(m.Id);
                        if (deferred)
                        {
                            kitDeferred.Remove(m.Id);
                            Count("kit-check-resolved-late");
                            ScLog.Verbose(m.Name + "'s work kit check went through after " + (now - d.Since).ToString("0") + " s deferred");
                        }
                    }
                    else if (!deferred)
                    {
                        kitDeferred[m.Id] = new KitDefer { Since = now, Wait = KitCheckSeconds, Next = now + KitCheckSeconds };
                        Count("kit-check-deferred");
                        ScLog.Verbose(m.Name + "'s work kit check is deferred (her body not set up yet, bandaging, or her weapon pair not resolved) - asked again in "
                                      + KitCheckSeconds.ToString("0") + " s, then less often (at most every " + KitDeferCapSeconds.ToString("0") + " s)");
                    }
                    else
                    {
                        d.Wait = Math.Min(d.Wait * 2f, KitDeferCapSeconds);
                        d.Next = now + d.Wait;
                    }
                }
                catch (Exception e)
                {
                    kitChecked.Add(m.Id);                 // a fault is not retried every KitCheckSeconds
                    ScLog.Once("jobs-kit-ensure", e);
                }
            }
        }

        // ================================================================== a kit tool in hand when a fight comes (review M2) ====

        /// <summary>Per member id: the Time.time before which KitFightTick does not try her again.</summary>
        private static readonly Dictionary<int, float> kitFightNext = new Dictionary<int, float>();
        private const float KitFightRetrySeconds = 3f;
        private static MethodInfo restoreWeapon;
        private static bool restoreWeaponBound;

        /// <summary>
        /// 0-XNPCCore's internal static UAITaskHarvestSdcs.RestoreWeapon(EntityAlive) (3.4.04): the weapon a job set aside
        /// (its _prevWeapon, shared by the harvest, butcher and base-keeper tasks) back in her hand. Null when gone - warned once,
        /// and KitFightTick re-arms through SC's own Arm instead.
        /// </summary>
        private static MethodInfo RestoreWeaponMethod()
        {
            if (restoreWeaponBound) return restoreWeapon;
            restoreWeaponBound = true;
            try
            {
                MethodInfo mi = AccessTools.DeclaredMethod(typeof(UAITaskHarvestSdcs), "RestoreWeapon", new[] { typeof(EntityAlive) });
                restoreWeapon = mi != null && mi.IsStatic && mi.ReturnType == typeof(void) ? mi : null;
                if (restoreWeapon == null)
                    ScLog.Warn("0-XNPCCore's UAITaskHarvestSdcs.RestoreWeapon(EntityAlive) not found - a member holding a work-kit tool when a fight comes gets the pair's melee weapon instead of the weapon her job set aside (0-XNPCCore update?)");
            }
            catch (Exception e) { ScLog.Once("jobs-bind-restore", e); }
            return restoreWeapon;
        }

        /// <summary>
        /// Round 29 review L1: the pair's melee weapon in her hand (ScLoadout.Arm), and 0-XNPCCore told so through its public
        /// AnyWeaponBridge.AnyWeaponNoteEquipped - its remembered weapon (sdcsHeldWeaponRemembered), which a close-range exit or
        /// a failover puts back, would otherwise still be the kit tool. False with why when she could not be armed.
        /// </summary>
        private static bool ArmPairMelee(EntityAlive npc, out string why)
        {
            if (!ScLoadout.Arm(npc, false, out why)) return false;
            string melee = ScLoadout.XnpcMelee(npc);
            if (!string.IsNullOrEmpty(melee) && !ScLoadout.IsKitOnly(npc, melee)) AnyWeaponBridge.AnyWeaponNoteEquipped(npc, melee);
            return true;
        }

        private static string HeldName(EntityAlive npc)
        {
            ItemValue held = npc != null && npc.inventory != null ? npc.inventory.holdingItemItemValue : null;
            return held != null && !held.IsEmpty() && held.ItemClass != null ? held.ItemClass.GetItemName() : null;
        }

        /// <summary>
        /// One of 0-XNPCCore's job tasks (harvest, butcher, the base keeper) runs as her current UAI task: she is working, and
        /// the task owns her hand (each puts her weapon back itself when it stops).
        /// </summary>
        private static bool JobTaskRunning(EntityAlive npc)
        {
            UAI.Context ctx = npc.utilityAIContext;
            if (ctx == null || !ctx.ActionData.Executing) return false;
            UAITaskBase t = ctx.ActionData.CurrentTask;
            return t is UAITaskHarvestSdcs || t is UAITaskButcherSdcs || t is UAITaskBaseKeepSdcs;
        }

        // ---- round 33a (round 29 glance LOW 1): a foe the job gate sees, not any foe within 30 m ----

        /// <summary>0-XNPCCore's job gate when its config cannot be read: SdcsEnemyNotNear distance="15" scan="v4" (3.4.04).</summary>
        private const float DefaultGateDistance = 15f;
        private static float gateDistance = DefaultGateDistance;
        private static bool gateV4 = true, gateSightBlock, gateRead, gateScanMissing;

        /// <summary>
        /// Round 33a: the job gate 0-XNPCCore's harvest and butcher actions carry (the actions whose task keeps a tool in her
        /// hand: UAITaskHarvestSdcs, UAITaskButcherSdcs), read once per world from the parsed UAI packages (UAIBase.AIPackages):
        /// its SdcsEnemyNotNear's distance, scan and sight (the consideration's private _distance / _v4 / _sightBlock, set by
        /// its Init from the XML). The smallest distance found wins: below it every such job is shut. None found (0-XNPCCore
        /// update?) leaves DefaultGateDistance, logged; packages not loaded yet are asked again on the next use.
        /// </summary>
        private static void ReadJobGate()
        {
            if (gateRead) return;
            if (UAIBase.AIPackages == null || UAIBase.AIPackages.Count == 0) return;
            gateRead = true;
            int found = 0;
            try
            {
                Type ct = typeof(UAIConsiderationSdcsEnemyNotNear);
                FieldInfo fd = AccessTools.Field(ct, "_distance"), fv = AccessTools.Field(ct, "_v4"), fs = AccessTools.Field(ct, "_sightBlock");
                if (fd != null && fd.FieldType == typeof(float))
                {
                    if (fv != null && fv.FieldType != typeof(bool)) fv = null;
                    if (fs != null && fs.FieldType != typeof(bool)) fs = null;
                    float best = float.MaxValue;
                    bool v4 = true, sight = false;
                    foreach (UAIPackage p in UAIBase.AIPackages.Values)
                    {
                        List<UAIAction> actions = p != null ? p.GetActions() : null;
                        if (actions == null) continue;
                        foreach (UAIAction a in actions)
                        {
                            if (a == null || !KeepsTool(a) || a.GetConsiderations() == null) continue;
                            foreach (UAIConsiderationBase c in a.GetConsiderations())
                            {
                                if (!(c is UAIConsiderationSdcsEnemyNotNear)) continue;
                                float d = (float)fd.GetValue(c);
                                if (d <= 0f) continue;
                                found++;
                                if (d < best)
                                {
                                    best = d;
                                    v4 = fv == null || (bool)fv.GetValue(c);
                                    sight = fs != null && (bool)fs.GetValue(c);
                                }
                            }
                        }
                    }
                    if (found > 0) { gateDistance = best; gateV4 = v4; gateSightBlock = sight; }
                }
            }
            catch (Exception e)
            {
                found = 0;                          // part read: the defaults stay
                gateDistance = DefaultGateDistance; gateV4 = true; gateSightBlock = false;
                ScLog.Once("jobs-gate-read", e);
            }
            if (found > 0)
                ScLog.Out("jobs: a work-kit tool goes back for a fight when 0-XNPCCore's job gate shuts - SdcsEnemyNotNear " + gateDistance.ToString("0.#")
                          + " m (scan " + (gateV4 ? "v4" : "v3") + (gateSightBlock ? ", sight block" : "") + ") on " + found + " harvest/butcher action(s), or a foe her action targets");
            else
                ScLog.Warn("jobs: no SdcsEnemyNotNear found on 0-XNPCCore's harvest or butcher actions (0-XNPCCore update?) - a work-kit tool goes back for a fight with a foe within "
                           + DefaultGateDistance.ToString("0") + " m (scan v4) or one her action targets");
        }

        private static bool KeepsTool(UAIAction a)
        {
            List<UAITaskBase> tasks = a.GetTasks();
            if (tasks == null) return false;
            foreach (UAITaskBase t in tasks) if (t is UAITaskHarvestSdcs || t is UAITaskButcherSdcs) return true;
            return false;
        }

        /// <summary>
        /// Round 33a: 0-XNPCCore's job gate is shut for her now - its own test (SdcsEnemyNearScan.HasNearEnemy, the one its
        /// SdcsEnemyNotNear scores) at the gate's distance, scan and sight: an awake enemy she can see within it, a revenge
        /// target it counts, her leader's target on Follow + loot. While it is shut no harvest or butcher job starts, so the
        /// weapon put back stays in her hand.
        /// </summary>
        private static bool GateShut(EntityAlive npc)
        {
            return SdcsEnemyNearScan.HasNearEnemy(npc, gateDistance, gateV4, gateSightBlock);
        }

        /// <summary>
        /// Round 29 review M2, narrowed in round 33a (round 29 glance LOW 1): a fight she is in that keeps a job from taking
        /// its tool again - the live enemy her current UAI action targets (at any range: she is acting on it; not the one a
        /// Cowardly flee runs from), or 0-XNPCCore's job gate shut (GateShut). It used to be any live foe within FightRange (30
        /// m) or any live attack or revenge target, so with a foe lingering beyond the gate (a zombie stuck behind a wall 20 m
        /// off as her target) the job went on, and her weapon came back after each tree and the axe again at the next. foe:
        /// the foe to name in the log (null when the gate is shut by one that is not hers: "a foe near"). Without 0-XNPCCore's
        /// scan (an update) the foe test is LiveFoe within the gate's distance, warned once.
        /// </summary>
        private static bool FightKeepsJobOff(EntityAlive npc, out EntityAlive foe, out string why)
        {
            foe = ScCommunity.ActionFoe(npc);
            if (foe != null) { why = "her action's target"; return true; }
            why = null;
            try { ReadJobGate(); }
            catch (Exception e) { gateRead = true; ScLog.Once("jobs-gate-bind", e); }     // the defaults stay
            bool shut;
            if (!gateScanMissing)
            {
                try { shut = GateShut(npc); }
                catch (Exception e) when (e is MissingMethodException || e is TypeLoadException)
                {
                    gateScanMissing = true;
                    ScLog.Warn("0-XNPCCore's SdcsEnemyNearScan.HasNearEnemy(EntityAlive, float, bool, bool) not found (0-XNPCCore update?) - a work-kit tool goes back"
                               + " for a fight with a foe within " + gateDistance.ToString("0.#") + " m instead");
                    shut = false;
                }
            }
            else shut = false;
            if (gateScanMissing)
            {
                foe = ScCommunity.LiveFoe(npc, gateDistance);
                if (foe == null) return false;
                why = "within " + gateDistance.ToString("0.#") + " m";
                return true;
            }
            if (!shut) return false;
            foe = ScCommunity.LiveFoe(npc, ScCommunity.FightRange);
            why = "0-XNPCCore's job gate (" + gateDistance.ToString("0.#") + " m) shut";
            return true;
        }

        /// <summary>
        /// Round 29 review M2: 0-XNPCCore keeps a job's tool in her hand between its targets (the harvest and butcher tasks'
        /// KeepTool) and its close-range swap only starts from a ranged weapon, so a zombie that reached her between two trees
        /// was fought with the stone axe. Once a second (Tick): a member whose held item is a kit-only tool
        /// (ScLoadout.IsKitOnly), with no run of 0-XNPCCore's harvest or butcher task going on and none of its job tasks as her
        /// current UAI task (then she is working, not fighting), while a fight keeps the jobs off (FightKeepsJobOff, round 33a:
        /// a foe her action targets, or 0-XNPCCore's job gate shut), gets her weapon back - 0-XNPCCore's own RestoreWeapon when a
        /// job set it aside (the weapon she held before the tool), else the pair's melee weapon (ArmPairMelee). The job takes
        /// its tool again when its next run starts (UAITaskHarvestSdcs.EquipTool), which the gate allows only once the fight is
        /// over. At most every KitFightRetrySeconds per member; counted, and logged (round 33a: Verbose when it worked).
        /// </summary>
        private static void KitFightTick()
        {
            if (!ScRegistry.Loaded || ScConfig.MemberWorkKit.Count == 0) return;
            float now = Time.time;
            foreach (Member m in ScRegistry.Members.Values)
            {
                if (m == null) continue;
                try { KitFightOne(m, now); }
                catch (Exception e) { ScLog.Once("jobs-kit-fight-one", e); }
            }
        }

        private static void KitFightOne(Member m, float now)
        {
            EntityAlive npc = ScCommunity.FindNpc(m.Id);
            if (npc == null || npc.IsDead() || npc.isEntityRemote || npc.inventory == null) return;
            string tool = HeldName(npc);
            if (tool == null || !ScLoadout.IsWorkKitItem(tool)) return;
            float next;
            if (kitFightNext.TryGetValue(m.Id, out next) && now < next) return;
            int id = npc.entityId;
            if (UAITaskHarvestSdcs.HasActiveRun(id) || UAITaskButcherSdcs.HasActiveRun(id) || JobTaskRunning(npc)) return;
            if (!ScLoadout.IsKitOnly(npc, tool)) return;
            EntityAlive foe;
            string foeWhy;
            if (!FightKeepsJobOff(npc, out foe, out foeWhy)) return;
            kitFightNext[m.Id] = now + KitFightRetrySeconds;
            string how, why = null;
            MethodInfo restore = UAITaskHarvestSdcs.HoldsJobTool(id) ? RestoreWeaponMethod() : null;
            if (restore != null)
            {
                restore.Invoke(null, new object[] { npc });
                how = "0-XNPCCore's job put her weapon back";
            }
            else how = ArmPairMelee(npc, out why) ? "the pair's melee weapon" : null;
            string inHand = HeldName(npc);
            bool ok = how != null && (inHand == null || !ScLoadout.IsKitOnly(npc, inHand));
            Count(ok ? "kit-tool-put-away-for-a-fight" : "kit-tool-in-a-fight-not-re-armed");
            string foeText = (foe != null ? ScCommunity.DisplayName(foe) + " at " + Vector3.Distance(npc.position, foe.position).ToString("0") + " m, " : "") + foeWhy;
            // round 33a (round 29 glance LOW 1): Verbose - one line per fight, not per tree, but a fight can still come and go
            if (ok) ScLog.Verbose(m.Name + " held the work kit's " + tool + " with a foe near (" + foeText + ") - " + how + ": " + (inHand ?? "empty hands") + " in hand");
            else ScLog.Out(m.Name + " holds the work kit's " + tool + " with a foe near (" + foeText + ") and could not be re-armed ("
                           + (why ?? "still holds " + (inHand ?? "nothing")) + ") - tried again in " + KitFightRetrySeconds.ToString("0") + " s");
        }

        // ================================================================== Base repair / Combat repair: what she can repair ====

        /// <summary>What 0-XNPCCore's base keeper can do for her now (RepairCheck), for ScWork's duty.</summary>
        public struct RepairFacts
        {
            /// <summary>0-XNPCCore's own test was read (false: the damage tracker alone, its tool and materials unknown).</summary>
            public bool Known;
            public bool Tool;
            public bool CanRepair;
            public bool Registered;
            public int Claims, Damaged, MaterialFails, SkillFails, OtherFails;
            public string SampleBlock, SampleReason;
            public string State;
        }

        private static MethodInfo repairable, pickRepairTool;
        private static FieldInfo fAny, fDamaged, fSkill, fOther, fMaterial, fSampleBlock, fSampleReason;
        private static bool repairBound, repairBindFailed;

        /// <summary>UAITaskBaseKeepSdcs' internal static Repairable(EntityAlive, float, float) and PickRepairTool(EntityAlive, out float, out float, out string).</summary>
        private static bool BindRepair()
        {
            if (repairBound) return true;
            if (repairBindFailed) return false;
            try
            {
                const BindingFlags S = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                Type t = typeof(UAITaskBaseKeepSdcs);
                repairable = t.GetMethod("Repairable", S, null, new[] { typeof(EntityAlive), typeof(float), typeof(float) }, null);
                pickRepairTool = t.GetMethod("PickRepairTool", S, null, new[] { typeof(EntityAlive), typeof(float).MakeByRefType(), typeof(float).MakeByRefType(), typeof(string).MakeByRefType() }, null);
                Type facts = repairable != null ? repairable.ReturnType : null;
                if (facts == null || !facts.IsValueType || pickRepairTool == null || pickRepairTool.ReturnType != typeof(string)) throw new MissingMethodException("UAITaskBaseKeepSdcs", "Repairable / PickRepairTool");
                fAny = Field(facts, "Any", typeof(bool));
                fDamaged = Field(facts, "Damaged", typeof(int));
                fSkill = Field(facts, "SkillFails", typeof(int));
                fOther = Field(facts, "OtherFails", typeof(int));
                fMaterial = Field(facts, "MaterialFails", typeof(int));
                fSampleBlock = Field(facts, "SampleBlock", typeof(string));
                fSampleReason = Field(facts, "SampleReason", typeof(string));
                repairBound = true;
                return true;
            }
            catch (Exception e)
            {
                repairBindFailed = true;
                ScLog.Warn("0-XNPCCore's repair test (UAITaskBaseKeepSdcs.Repairable / PickRepairTool) could not be bound (" + e.Message
                           + ") - a repair duty goes by the damage it tracks alone and says nothing about a missing tool or materials (0-XNPCCore update?)");
                return false;
            }
        }

        private static FieldInfo Field(Type t, string name, Type type)
        {
            FieldInfo f = AccessTools.Field(t, name);
            if (f == null || f.FieldType != type) throw new MissingFieldException(t.Name, name);
            return f;
        }

        /// <summary>
        /// What she can repair now, by 0-XNPCCore's own test: Repairable (damaged blocks of the duty's claims, and whether one of
        /// them is affordable with her tool and the materials in reach) and PickRepairTool (her bag, loot container, window,
        /// then the kit). On an error the duty is kept (CanRepair true): it never ends for a fault of ours.
        /// </summary>
        public static RepairFacts RepairCheck(EntityAlive npc)
        {
            var f = new RepairFacts();
            try
            {
                f.Registered = SdcsBaseDamageTracker.IsRegistered(npc.entityId);
                f.Claims = f.Registered ? SdcsBaseDamageTracker.RegisteredClaimCount(npc.entityId) : 0;
                if (BindRepair())
                {
                    object[] args = { npc, 0f, 0f, null };
                    f.Tool = pickRepairTool.Invoke(null, args) is string;
                    object facts = repairable.Invoke(null, new object[] { npc, 4f, Time.time });
                    f.CanRepair = (bool)fAny.GetValue(facts);
                    f.Damaged = (int)fDamaged.GetValue(facts);
                    f.SkillFails = (int)fSkill.GetValue(facts);
                    f.OtherFails = (int)fOther.GetValue(facts);
                    f.MaterialFails = (int)fMaterial.GetValue(facts);
                    f.SampleBlock = fSampleBlock.GetValue(facts) as string;
                    f.SampleReason = fSampleReason.GetValue(facts) as string;
                    f.Known = true;
                }
                else
                {
                    var list = new List<RepairTargetRules.RepairTarget>();
                    f.Damaged = SdcsBaseDamageTracker.Targets(npc.entityId, SdcsLootMode.Mode(npc), npc.position, list, 1);
                    f.CanRepair = f.Damaged > 0;
                    f.Tool = true;
                }
            }
            catch (Exception e)
            {
                ScLog.Once("jobs-repair-check", e);
                f.CanRepair = true;
                f.State = "check failed";
                return f;
            }
            f.State = !f.Known ? (f.Damaged > 0 ? "damage tracked" : "nothing damaged") + " (tool and materials unknown)"
                      : !f.Tool ? "no repair tool"
                      : f.Registered && f.Claims == 0 ? "no land claim near"
                      : f.CanRepair ? "can repair (" + f.Damaged + " damaged)"
                      : f.Damaged == 0 ? "nothing damaged"
                      : f.MaterialFails > 0 ? "no materials in reach (" + (f.SampleBlock ?? "?") + ")"
                      : f.SkillFails > 0 ? "not skilled enough (" + (f.SampleBlock ?? "?") + ")"
                      : "cannot repair the damage (" + (f.SampleReason ?? "?") + ")";
            return f;
        }

        private static string BlockLabel(string block)
        {
            if (string.IsNullOrEmpty(block)) return Localization.Get("scDutyBlockSomething");
            if (block == "hole") return Localization.Get("scDutyBlockHole");
            Block b = Block.GetBlockByName(block, true);
            return b != null ? b.GetLocalizedBlockName() : block;
        }

        /// <summary>
        /// The repair duty's first look (ScWork.DutyTick): what is missing, said once - no repair tool, the materials or the
        /// skill for a damaged block - or that nothing is damaged yet (by day, or on a Combat repair duty at any hour - review
        /// M1: night is false for it - she keeps watch until the duty's idle end). Nothing when she can repair, without a land
        /// claim (0-XNPCCore says so itself and ends the job) or with the test unread.
        /// </summary>
        public static void TellDutyStart(Member m, RepairFacts f, bool night)
        {
            EntityPlayerLocal p = ScCommunity.LocalPlayer;
            if (p == null || m == null || !f.Known || (f.Registered && f.Claims == 0) || f.CanRepair) return;
            string text;
            if (!f.Tool) text = string.Format(Localization.Get("scDutyNoTool"), m.Name);
            else if (f.Damaged > 0 && f.MaterialFails > 0) text = string.Format(Localization.Get("scDutyNoMaterials"), m.Name, BlockLabel(f.SampleBlock), ChestReach().ToString("0"));
            else if (f.Damaged > 0 && f.SkillFails > 0) text = string.Format(Localization.Get("scDutyNoSkill"), m.Name, BlockLabel(f.SampleBlock));
            else if (f.Damaged > 0) text = string.Format(Localization.Get("scDutyCantRepair"), m.Name, BlockLabel(f.SampleBlock));
            else if (night) return;                                  // she goes home at once: ScWork's end says so
            else text = string.Format(Localization.Get("scDutyNothing"), m.Name, ScConfig.RepairDutyIdleMinutes);
            Count("duty-told-" + (f.Tool ? (f.Damaged > 0 ? "cannot" : "nothing") : "notool"));
            ScCommunity.Tell(p, text, "");
            ScLog.Out(m.Name + "'s repair duty: " + f.State);
        }

        // ================================================================== squad orders never reach a resident ====

        public static Type VcModType { get { return ScTypes.Find("XNPCVoiceControl.XNPCVoiceControlMod"); } }

        /// <summary>XNPCVoiceControlMod.GetHiredRoster(EntityPlayer), internal static: the order wheel's roster (its targets, the squad).</summary>
        public static MethodBase RosterTarget()
        {
            Type t = VcModType;
            return t != null ? Returning(AccessTools.DeclaredMethod(t, "GetHiredRoster", new[] { typeof(EntityPlayer) }), typeof(List<EntityAlive>), true) : null;
        }

        /// <summary>VCCommandRouter.ExecuteForSquad(int, int, List&lt;EntityAlive&gt; roster, VCCommand, string), internal static: the spoken squad order.</summary>
        public static MethodBase SquadTarget()
        {
            Type t = RouterType;
            if (t == null) return null;
            foreach (MethodInfo mi in AccessTools.GetDeclaredMethods(t))
            {
                if (mi.Name != "ExecuteForSquad" || !mi.IsStatic || mi.ReturnType != typeof(void)) continue;
                ParameterInfo[] ps = mi.GetParameters();
                if (ps.Length == 5 && ps[0].ParameterType == typeof(int) && ps[1].ParameterType == typeof(int) && ps[2].ParameterType == typeof(List<EntityAlive>)
                    && ps[3].ParameterType.IsEnum && ps[4].ParameterType == typeof(string)) return mi;
            }
            return null;
        }

        private static FieldInfo preferTarget;
        private static bool preferTargetBound;

        /// <summary>The entity the order wheel is being opened on (XUiC_VCFormationRadial._preferTargetOnce: her ring, "Jobs and gear"), else -1.</summary>
        private static int WheelOpeningOn()
        {
            if (!preferTargetBound)
            {
                preferTargetBound = true;
                try
                {
                    Type t = ScTypes.Find("XUiC_VCFormationRadial");
                    FieldInfo f = t != null ? AccessTools.Field(t, "_preferTargetOnce") : null;
                    preferTarget = f != null && f.IsStatic && f.FieldType == typeof(int) ? f : null;
                    if (preferTarget == null && Patches_VoiceControl.ChatType != null)
                        ScLog.Warn("Voice Control's XUiC_VCFormationRadial._preferTargetOnce not found - the order wheel opened on a resident from her ring may close at once (Voice Control update?)");
                }
                catch (Exception e) { ScLog.Once("jobs-bind-prefer", e); }
            }
            try { return preferTarget != null ? (int)preferTarget.GetValue(null) : -1; }
            catch (Exception) { return -1; }
        }

        /// <summary>
        /// J29 (the audit's #5): the order wheel's roster leaves the community's residents out - linked by a talk or a work shift
        /// they count as hired for Voice Control, so the wheel's default target, its target cycling and a Shift+click squad
        /// order reached them, and any order but Guard ends a resident's shift. The one resident the wheel is being opened on
        /// (her hold-E ring, "Jobs and gear": Voice Control closes a wheel locked on someone not in the roster) stays.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_RosterNoResidents
        {
            [HarmonyPrepare]
            public static bool Prepare() { return BoundVc(RosterTarget, "XNPCVoiceControlMod.GetHiredRoster(EntityPlayer)"); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return RosterTarget(); }

            [HarmonyPostfix]
            public static void Postfix(List<EntityAlive> __result)
            {
                try
                {
                    if (__result == null || __result.Count == 0 || !ScRegistry.Loaded) return;
                    int keep = WheelOpeningOn();
                    int n = __result.RemoveAll(e => e != null && e.entityId != keep && Resident(e) != null);
                    // review L9: Voice Control asks for the roster on every game update (SquadStanceDisplay.OnGameUpdate), so
                    // the counter goes up once per change of how many were left out, not once per call
                    if (n != rosterLastLeftOut)
                    {
                        rosterLastLeftOut = n;
                        if (n > 0) Count("roster-residents-left-out");
                    }
                }
                catch (Exception e) { ScLog.Once("jobs-roster", e); }
            }
        }

        /// <summary>Review L9: how many residents the roster's last call left out (-1: none yet), so the counter moves once per change.</summary>
        private static int rosterLastLeftOut = -1;

        /// <summary>J29: a spoken squad order ("everyone, follow me") leaves the community's residents out too.</summary>
        [HarmonyPatch]
        public static class Patch_SquadNoResidents
        {
            [HarmonyPrepare]
            public static bool Prepare() { return BoundVc(SquadTarget, "VCCommandRouter.ExecuteForSquad(int, int, List<EntityAlive>, VCCommand, string)"); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return SquadTarget(); }

            /// <summary>__2 the roster (a list Voice Control builds for this order): residents taken out; none left - nothing to do.</summary>
            [HarmonyPrefix]
            public static bool Prefix(List<EntityAlive> __2)
            {
                try
                {
                    if (__2 == null || __2.Count == 0 || !ScRegistry.Loaded) return true;
                    int n = __2.RemoveAll(e => e != null && Resident(e) != null);
                    if (n == 0) return true;
                    Count("squad-residents-left-out");
                    if (__2.Count > 0) return true;
                    ScLog.Verbose("squad order: only residents in the roster - nobody given it");
                    return false;
                }
                catch (Exception e)
                {
                    ScLog.Once("jobs-squad", e);
                    return true;
                }
            }
        }

        // ================================================================== a runner's inventory window ====

        /// <summary>
        /// ScRunners.Rekey (a runner re-created under a new id, a missing resident called back): her SCore inventory window -
        /// HarvestManager's container, keyed by entity id - comes with her: its stacks into the new body's window, anything
        /// that does not fit into the home crates, the old container dropped. Only while no entity holds the old id (as
        /// before: an id another entity now uses keeps its window). A fresh body (ScSpawn) was given the rounds of her pair
        /// gun her ammo record counts - every store, the window included (ScAmmo.RestoreRecorded) - so those stacks stay
        /// behind rather than come twice (SC moves ammo out of the window into the bag whenever it is closed anyway). Logged;
        /// a loss (no room anywhere) is a warning.
        /// </summary>
        public static void CarryWindow(World w, Member m, int oldId, EntityAlive e, bool fresh)
        {
            try
            {
                if (w == null || e == null || w.GetEntity(oldId) != null || !HarvestManager.Has(oldId)) return;
                ItemStack[] items = HarvestManager.GetOrCreate(oldId).items;
                int moved = 0, toCrates = 0, recorded = 0;
                var lost = new List<string>();
                var ammo = new HashSet<int>();
                ItemActionRanged gun = fresh ? ScAmmo.RangedAction(ScAmmo.PairGun(e)) : null;
                if (gun != null && gun.MagazineItemNames != null)
                    foreach (string a in gun.MagazineItemNames) { ItemClass ac = ItemClass.GetItemClass(a, true); if (ac != null) ammo.Add(ac.Id); }
                if (items != null)
                {
                    foreach (ItemStack s in items)
                    {
                        if (s == null || s.IsEmpty() || s.itemValue == null) continue;
                        if (ammo.Contains(s.itemValue.type)) { recorded += s.count; continue; }
                        ItemStack c = s.Clone();
                        if (HarvestManager.AddItem(e.entityId, c)) { moved += s.count; continue; }
                        moved += s.count - c.count;                                    // AddItem left what did not fit in c
                        Vector3i box;
                        if (ScStorage.DepositInZone(c, out box)) toCrates += c.count;
                        else lost.Add(c.count + "x " + ItemLabel(c.itemValue.ItemClass != null ? c.itemValue.ItemClass.GetItemName() : "?"));
                    }
                }
                HarvestManager.Remove(oldId);
                Count("runner-window-carried");
                if (moved + toCrates + recorded > 0 || lost.Count > 0)
                    ScLog.Out(m.Name + "'s inventory window came with them to entity " + e.entityId + ": " + moved + " item(s)"
                              + (toCrates > 0 ? ", " + toCrates + " that did not fit into the home crates" : "")
                              + (recorded > 0 ? ", " + recorded + " round(s) of their gun left behind (the fresh body got their ammo record)" : ""));
                if (lost.Count > 0) ScLog.Warn(m.Name + "'s inventory window: no room for " + string.Join(", ", lost.ToArray()) + " in the new body or the home crates - lost");
            }
            catch (Exception ex) { ScLog.Once("runner-harvest", ex); }
        }

        /// <summary>
        /// ScRunners' DieOffline: a runner who dies on the road, out of the player's sight, has no body to drop a backpack from
        /// and her last place is not loaded, so what her inventory window held is lost with her - as her bag and toolbelt are.
        /// Not silently: logged, and told to the player beside the death. Only while no entity holds the id (as before).
        /// </summary>
        public static void LoseWindow(World w, Member m, int id, EntityPlayer player)
        {
            try
            {
                if (w != null && w.GetEntity(id) != null) return;
                if (!HarvestManager.Has(id)) return;
                ItemStack[] items = HarvestManager.GetOrCreate(id).items;
                var lost = new List<string>();
                if (items != null)
                    foreach (ItemStack s in items)
                        if (s != null && !s.IsEmpty() && s.itemValue != null && s.itemValue.ItemClass != null) lost.Add(s.count + "x " + ItemLabel(s.itemValue.ItemClass.GetItemName()));
                HarvestManager.Remove(id);
                if (lost.Count == 0) return;
                Count("runner-window-lost");
                string list = string.Join(", ", lost.ToArray());
                ScLog.Out(m.Name + " died on the road - what their inventory window held is lost with them: " + list);
                if (player != null) ScCommunity.Tell(player, string.Format(Localization.Get("scRunWindowLost"), m.Name, list), "");
            }
            catch (Exception ex) { ScLog.Once("runner-harvest", ex); }
        }

        // ================================================================== the second ====

        /// <summary>
        /// ScWork.Sweep, once a second (ScTicker): the plots to replant, the work kit's once-per-body check, and (review M2) a
        /// kit tool put away when a fight comes (KitFightTick, on its own: a fault in the others does not skip it).
        /// </summary>
        public static void Tick(EntityPlayerLocal player)
        {
            if (!ScCommunity.IsHost) return;
            try
            {
                ReplantTick(ScCommunity.World);
                KitTick();
            }
            catch (Exception e) { ScLog.Once("jobs-tick", e); }
            try { KitFightTick(); }
            catch (Exception e) { ScLog.Once("jobs-kit-fight", e); }
        }
    }
}
