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
    ///   C1f SdcsLootHere.Finish(EntityAlive, string, string)              prefix   a member on a work shift stays where she is when a
    ///                                                                              job ends (no Follow order, no teleport to the player)
    ///
    /// As Patches_Xnpc: every target DECLARED on the named type and bound by name (SdcsStationCraft is internal: found through
    /// SdcsCraft's assembly), a Prepare that turns the patch off with one warning when it is gone, ModApi's ExpectedTargets lists
    /// them, and each counts its hits (sc craft) - a renamed PayXp would otherwise stop station XP silently. Non-members are never
    /// touched: each patch asks the registry first.
    /// </summary>
    public static class Patches_Craft
    {
        private static readonly Dictionary<string, int> hits = new Dictionary<string, int>();

        public static void Reset() { hits.Clear(); }

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
            return parts.Count > 0 ? string.Join(", ", parts.ToArray()) : "none yet";
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
        /// buffOrderFollow and a teleport to her leader beyond 40 m. A member on a work shift (ScWork) has no leader to follow: she
        /// stays where she is, exactly as after a station job (its AtStation branch is the same Clear + Say), and the shift's idle
        /// timer sends her home.
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
                    if (m == null || npc.Buffs == null || !ScWork.OnShift(m.Id)) return true;
                    SdcsLootMode.Clear(npc);
                    SdcsSayLine.Say(npc, lineId);
                    Count("c1f-shift-job-ended-in-place");
                    ScLog.Verbose(m.Name + ": job done on her shift (" + reason + ") - stays at her spot");
                    return false;
                }
                catch (Exception e)
                {
                    ScLog.Once("craft-lootfinish", e);
                    return true;
                }
            }
        }
    }
}
