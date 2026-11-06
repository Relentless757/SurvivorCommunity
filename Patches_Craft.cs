using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace SurvivorCommunity
{
    /// <summary>
    /// C1 (round 22, 3.4): the patches community crafting needs on 0-XNPCCore 3.4.02 (NPCSdcsCharacters.dll) and one on vanilla.
    ///
    ///   C1a SdcsNpcXp.Pay(EntityAlive, int, string)                       prefix   no 0-XNPCCore XP into a member's Progression
    ///                                                                              (kill, craft, harvest, repair: D8 now that she has one)
    ///   C1b AvatarSdcsNpcController.SdcsCountCrafts(EntityAlive, string, int)  prefix   no learn-by-crafting on members: their
    ///                                                                              levels are their stars (ScCraft)
    ///   C1c SdcsCraft.PayXp(EntityAlive, Recipe)                          postfix  a backpack item made: SC's specialty XP, the shift ledger
    ///   C1d SdcsStationCraft.PayXp(EntityAlive, TileEntityWorkstation)    prefix   station items made under her id: the same (the
    ///                                                                              original then removes them; its XP is refused by C1a)
    ///   C1e TileEntityWorkstation.CheckForCraftComplete(EntityPlayerLocal) postfix the player opens a station: items a member made
    ///                                                                              there while unloaded or after a reload are paid and
    ///                                                                              taken off its list
    ///   C1f SdcsLootHere.Finish(EntityAlive, string, string)              prefix   a resident member (on a work shift, or with a link a
    ///                                                                              reload left) stays where she is when a job ends (no
    ///                                                                              Follow order, no teleport to the player)
    ///   C1g AvatarSdcsNpcController.SdcsAnyWeaponTick / SdcsAnyWeaponMenuListing(EntityAlive) / SdcsTryWorldEntryMelee(EntityAlive,
    ///       ItemValue)                                                    prefixes 0-XNPCCore's owner-gated weapon picks (its Owner cvar
    ///                                                                              test) never re-arm a resident on a shift from her
    ///                                                                              stores: she is a resident, SC drives her weapons
    ///                                                                              (review #3; its melee fallback is X6's already)
    ///
    /// As Patches_Xnpc: every target DECLARED on the named type and bound by name (SdcsStationCraft is internal: found through
    /// SdcsCraft's assembly), a Prepare that turns the patch off with one warning when it is gone, ModApi's ExpectedTargets lists
    /// them, and each counts its hits (sc craft) - a renamed PayXp would otherwise stop station XP silently. Non-members are never
    /// touched: each patch asks the registry first.
    /// </summary>
    public static class Patches_Craft
    {
        private static readonly Dictionary<string, int> hits = new Dictionary<string, int>();

        public static void Reset()
        {
            hits.Clear();
            anyWeaponSkips = listingSkips = worldEntrySkips = 0;
        }

        internal static void Count(string what)
        {
            int n;
            hits.TryGetValue(what, out n);
            hits[what] = n + 1;
        }

        public static string Report()
        {
            var keys = new List<string>(hits.Keys);
            keys.Sort(StringComparer.Ordinal);
            var parts = new List<string>();
            foreach (string k in keys) parts.Add(k + "=" + hits[k]);
            parts.Add("c1g-anyweapon-tick-skipped-frames=" + anyWeaponSkips);
            parts.Add("c1g-anyweapon-listing-empty=" + listingSkips);
            parts.Add("c1g-worldentry-melee-skipped=" + worldEntrySkips);
            return string.Join(", ", parts.ToArray());
        }

        // ------------------------------------------------------------------ targets ----

        public static MethodBase PayTarget()
        {
            return AccessTools.DeclaredMethod(typeof(SdcsNpcXp), "Pay", new[] { typeof(EntityAlive), typeof(int), typeof(string) });
        }

        public static MethodBase CountCraftsTarget()
        {
            return AccessTools.DeclaredMethod(typeof(AvatarSdcsNpcController), "SdcsCountCrafts", new[] { typeof(EntityAlive), typeof(string), typeof(int) });
        }

        public static MethodBase BackpackPayTarget()
        {
            return AccessTools.DeclaredMethod(typeof(SdcsCraft), "PayXp", new[] { typeof(EntityAlive), typeof(Recipe) });
        }

        /// <summary>SdcsStationCraft is internal to NPCSdcsCharacters.dll: looked up in SdcsCraft's assembly by its full name (no namespace).</summary>
        public static MethodBase StationPayTarget()
        {
            Type t = typeof(SdcsCraft).Assembly.GetType("SdcsStationCraft", false);
            return t != null ? AccessTools.DeclaredMethod(t, "PayXp", new[] { typeof(EntityAlive), typeof(TileEntityWorkstation) }) : null;
        }

        public static MethodBase CraftCompleteTarget()
        {
            return AccessTools.DeclaredMethod(typeof(TileEntityWorkstation), "CheckForCraftComplete", new[] { typeof(EntityPlayerLocal) });
        }

        public static MethodBase LootFinishTarget()
        {
            return AccessTools.DeclaredMethod(typeof(SdcsLootHere), "Finish", new[] { typeof(EntityAlive), typeof(string), typeof(string) });
        }

        public static MethodBase AnyWeaponTickTarget()
        {
            return AccessTools.DeclaredMethod(typeof(AvatarSdcsNpcController), "SdcsAnyWeaponTick", new Type[0]);
        }

        public static MethodBase AnyWeaponListingTarget()
        {
            return AccessTools.DeclaredMethod(typeof(AvatarSdcsNpcController), "SdcsAnyWeaponMenuListing", new[] { typeof(EntityAlive) });
        }

        public static MethodBase WorldEntryMeleeTarget()
        {
            return AccessTools.DeclaredMethod(typeof(AvatarSdcsNpcController), "SdcsTryWorldEntryMelee", new[] { typeof(EntityAlive), typeof(ItemValue) });
        }

        /// <summary>C1g runs every frame per body (the any-weapon tick): plain counters, read by Report.</summary>
        private static int anyWeaponSkips, listingSkips, worldEntrySkips;

        /// <summary>A Prepare's lookup, in its own method so a missing DLL is a caught exception and an inert patch (CLAUDE.md #5).</summary>
        private static bool Bound(Func<MethodBase> target, string label)
        {
            try
            {
                if (target() != null) return true;
                ScLog.Warn("crafting: " + label + " not found - that SurvivorCommunity patch is off (0-XNPCCore or game update?)");
            }
            catch (Exception e)
            {
                ScLog.Once("craft-bind-" + label, e);
            }
            return false;
        }

        private static Member MemberOf(EntityAlive npc)
        {
            return npc != null ? ScRegistry.GetMember(npc.entityId) : null;
        }

        /// <summary>The recipe behind a station's entry (its output count; any recipe of that name - they make the same item).</summary>
        private static Recipe RecipeFor(string name)
        {
            List<Recipe> rs = string.IsNullOrEmpty(name) ? null : CraftingManager.GetRecipes(name);
            if (rs == null || rs.Count == 0) return null;
            return rs[0];
        }

        /// <summary>Items in a station entry -> crafts (vanilla merges a crafter's entries by item and keeps RecipeUsedCount at 1).</summary>
        private static int CraftsOf(CraftCompleteData d, out int items)
        {
            items = d.CraftedItemStack != null ? Math.Max(0, d.CraftedItemStack.count) : 0;
            Recipe r = RecipeFor(d.RecipeName);
            int per = r != null ? Math.Max(1, r.count) : 1;
            return Math.Max(1, items / per);
        }

        // ------------------------------------------------------------------ C1a, C1b: no 0-XNPCCore learning on members ----

        /// <summary>C1a. 0-XNPCCore's one XP payer: a member's Progression is SC's, so nothing of 0-XNPCCore's is paid into it.</summary>
        [HarmonyPatch]
        public static class Patch_NoNpcXp
        {
            [HarmonyPrepare]
            public static bool Prepare() { return Bound(PayTarget, "SdcsNpcXp.Pay(EntityAlive, int, string)"); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return PayTarget(); }

            [HarmonyPrefix]
            public static bool Prefix(EntityAlive npc, ref bool __result)
            {
                try
                {
                    if (MemberOf(npc) == null) return true;
                    __result = false;
                    Count("c1a-npccore-xp-refused");
                    return false;
                }
                catch (Exception e)
                {
                    ScLog.Once("craft-npcxp", e);
                    return true;
                }
            }
        }

        /// <summary>C1b. 0-XNPCCore's learn-by-crafting (its SdcsCrafts* counters raising role skills): never on a member.</summary>
        [HarmonyPatch]
        public static class Patch_NoRoleLearning
        {
            [HarmonyPrepare]
            public static bool Prepare() { return Bound(CountCraftsTarget, "AvatarSdcsNpcController.SdcsCountCrafts(EntityAlive, string, int)"); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return CountCraftsTarget(); }

            [HarmonyPrefix]
            public static bool Prefix(EntityAlive npc)
            {
                try
                {
                    if (MemberOf(npc) == null) return true;
                    Count("c1b-role-learning-refused");
                    return false;
                }
                catch (Exception e)
                {
                    ScLog.Once("craft-countcrafts", e);
                    return true;
                }
            }
        }

        // ------------------------------------------------------------------ C1c-C1e: SC's XP per craft ----

        /// <summary>C1c. A backpack item made (SdcsCraft.Finish put it into her window and calls this once per item).</summary>
        [HarmonyPatch]
        public static class Patch_BackpackCraft
        {
            [HarmonyPrepare]
            public static bool Prepare() { return Bound(BackpackPayTarget, "SdcsCraft.PayXp(EntityAlive, Recipe)"); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return BackpackPayTarget(); }

            [HarmonyPostfix]
            public static void Postfix(EntityAlive npc, Recipe r)
            {
                try
                {
                    Member m = MemberOf(npc);
                    if (m == null || r == null) return;
                    Count("c1c-backpack-craft");
                    ScCraft.OnCrafted(m, npc, r.GetName(), 1, "backpack");
                    ScWork.NoteMade(m.Id, r.itemValueType, SdcsCraft.OutputCount(r));
                }
                catch (Exception e)
                {
                    ScLog.Once("craft-backpack", e);
                }
            }
        }

        /// <summary>
        /// C1d. Station items made under her id (0-XNPCCore tags her queue items; vanilla's queue lists each finished item in the
        /// station's CraftCompleteList): counted here, before 0-XNPCCore's own pass takes them off the list (its XP refused by C1a).
        /// </summary>
        [HarmonyPatch]
        public static class Patch_StationCraft
        {
            [HarmonyPrepare]
            public static bool Prepare() { return Bound(StationPayTarget, "SdcsStationCraft.PayXp(EntityAlive, TileEntityWorkstation)"); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return StationPayTarget(); }

            [HarmonyPrefix]
            public static void Prefix(EntityAlive npc, TileEntityWorkstation te)
            {
                try
                {
                    Member m = MemberOf(npc);
                    List<CraftCompleteData> list = te != null ? te.CraftCompleteList : null;
                    if (m == null || list == null) return;
                    for (int i = 0; i < list.Count; i++)
                    {
                        CraftCompleteData d = list[i];
                        if (d == null || d.CrafterEntityID != npc.entityId) continue;
                        int items;
                        int crafts = CraftsOf(d, out items);
                        Count("c1d-station-entry");
                        ScCraft.OnCrafted(m, npc, d.RecipeName, crafts, "station");
                        if (d.CraftedItemStack != null && d.CraftedItemStack.itemValue != null) ScWork.NoteMade(m.Id, d.CraftedItemStack.itemValue.type, items);
                    }
                }
                catch (Exception e)
                {
                    ScLog.Once("craft-station", e);
                }
            }
        }

        /// <summary>
        /// C1e. The player opens a station (XUiC_WorkstationWindowGroup.OnOpen): vanilla takes his own entries; any entry under a
        /// member's id is paid to her (loaded or not) and taken off, so items she made while unloaded or after a reload (her order
        /// gone, the queue still running) count once, and the list does not keep them for ever. Host only (the registry's side).
        /// </summary>
        [HarmonyPatch]
        public static class Patch_StationSweep
        {
            [HarmonyPrepare]
            public static bool Prepare() { return Bound(CraftCompleteTarget, "TileEntityWorkstation.CheckForCraftComplete(EntityPlayerLocal)"); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return CraftCompleteTarget(); }

            [HarmonyPostfix]
            public static void Postfix(TileEntityWorkstation __instance)
            {
                try
                {
                    List<CraftCompleteData> list = __instance != null ? __instance.CraftCompleteList : null;
                    if (list == null || list.Count == 0 || !ScCommunity.IsHost || !ScRegistry.Loaded) return;
                    bool changed = false;
                    for (int i = list.Count - 1; i >= 0; i--)
                    {
                        CraftCompleteData d = list[i];
                        Member m = d != null ? ScRegistry.GetMember(d.CrafterEntityID) : null;
                        if (m == null) continue;
                        int items;
                        int crafts = CraftsOf(d, out items);
                        ScCraft.OnCrafted(m, ScCommunity.FindNpc(m.Id), d.RecipeName, crafts, "sweep");
                        // on a shift her order still moves the station's output into her window: owed to the crates then
                        if (d.CraftedItemStack != null && d.CraftedItemStack.itemValue != null) ScWork.NoteMade(m.Id, d.CraftedItemStack.itemValue.type, items);
                        list.RemoveAt(i);
                        changed = true;
                        Count("c1e-station-list-swept");
                        ScLog.Out("a station's list held " + items + " x " + d.RecipeName + " made by " + m.Name + " - paid to her now");
                    }
                    if (changed) __instance.SetModified();
                }
                catch (Exception e)
                {
                    ScLog.Once("craft-sweep", e);
                }
            }
        }

        // ------------------------------------------------------------------ C1f: a shift's job ends in place ----

        /// <summary>
        /// C1f. 0-XNPCCore ends a job away from a station (a backpack craft, a loot or harvest job) with the Follow order,
        /// buffOrderFollow and a teleport to her leader beyond 40 m. A resident member has no leader to follow - on a work shift
        /// (ScWork), or with the Owner cvar and craft-here state a reload left (review #2: 0-XNPCCore's craft task may end that
        /// order before SC's ticker stations her): she stays where she is, exactly as after a station job (its AtStation branch
        /// is the same Clear + Say); the shift's idle timer, or the resident rule, sends her home.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_ShiftJobEnd
        {
            [HarmonyPrepare]
            public static bool Prepare() { return Bound(LootFinishTarget, "SdcsLootHere.Finish(EntityAlive, string, string)"); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return LootFinishTarget(); }

            [HarmonyPrefix]
            public static bool Prefix(EntityAlive npc, string lineId, string reason)
            {
                try
                {
                    Member m = MemberOf(npc);
                    if (m == null || npc.Buffs == null || m.Status != MemberStatus.Resident) return true;
                    SdcsLootMode.Clear(npc);
                    SdcsSayLine.Say(npc, lineId);
                    Count("c1f-shift-job-ended-in-place");
                    ScLog.Verbose(m.Name + ": job done as a resident (" + reason + ") - stays at her spot");
                    return false;
                }
                catch (Exception e)
                {
                    ScLog.Once("craft-lootfinish", e);
                    return true;
                }
            }
        }

        // ------------------------------------------------------------------ C1g: no owner-gated re-arm of a resident (review #3) ----

        /// <summary>
        /// C1g. 0-XNPCCore's any-weapon tick (every frame from Update, self-throttled) equips a weapon newly found in a hire's stores;
        /// its gate is the Owner cvar (SdcsNpcRelation.OwnedByCvar), which a work shift writes. A resident on a shift (ScWork.ShiftOwned:
        /// a resident member with the Owner cvar - a reload's leftover too) is skipped, as she is without the link. Followers keep it
        /// (X16 joins what it equips to the pair).
        /// </summary>
        [HarmonyPatch]
        public static class Patch_ShiftNoAnyWeapon
        {
            [HarmonyPrepare]
            public static bool Prepare() { return Bound(AnyWeaponTickTarget, "AvatarSdcsNpcController.SdcsAnyWeaponTick()"); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return AnyWeaponTickTarget(); }

            [HarmonyPrefix]
            public static bool Prefix(AvatarSDCSController __instance)
            {
                try
                {
                    EntityAlive e = __instance != null ? __instance.entity : null;
                    if (!ScWork.ShiftOwned(e)) return true;
                    anyWeaponSkips++;
                    return false;
                }
                catch (Exception ex)
                {
                    ScLog.Once("craft-anyweapon", ex);
                    return true;
                }
            }
        }

        /// <summary>C1g. The "Carried Weapons" listing (Voice Control's AnyWeaponBridge): empty for a resident on a shift, as for an NPC with no owner.</summary>
        [HarmonyPatch]
        public static class Patch_ShiftNoAnyWeaponListing
        {
            [HarmonyPrepare]
            public static bool Prepare() { return Bound(AnyWeaponListingTarget, "AvatarSdcsNpcController.SdcsAnyWeaponMenuListing(EntityAlive)"); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return AnyWeaponListingTarget(); }

            /// <summary>__0 is the entity argument (bound by position: the controller also has an "entity" field).</summary>
            [HarmonyPrefix]
            public static bool Prefix(EntityAlive __0, ref List<string> __result)
            {
                try
                {
                    if (!ScWork.ShiftOwned(__0)) return true;
                    listingSkips++;
                    __result = new List<string>();
                    return false;
                }
                catch (Exception ex)
                {
                    ScLog.Once("craft-anyweapon-listing", ex);
                    return true;
                }
            }
        }

        /// <summary>
        /// C1g. The world-entry re-arm's melee step (from SdcsTryWorldEntryRearm, X5's gate) is owner-gated too: a resident whose
        /// body comes into the world with a reload's leftover link keeps the hand SC put together (X13, ScLoadout), as without it.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_ShiftNoWorldEntryMelee
        {
            [HarmonyPrepare]
            public static bool Prepare() { return Bound(WorldEntryMeleeTarget, "AvatarSdcsNpcController.SdcsTryWorldEntryMelee(EntityAlive, ItemValue)"); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return WorldEntryMeleeTarget(); }

            [HarmonyPrefix]
            public static bool Prefix(EntityAlive __0, ref bool __result)
            {
                try
                {
                    if (!ScWork.ShiftOwned(__0)) return true;
                    worldEntrySkips++;
                    __result = false;
                    return false;
                }
                catch (Exception ex)
                {
                    ScLog.Once("craft-worldentry", ex);
                    return true;
                }
            }
        }
    }
}
