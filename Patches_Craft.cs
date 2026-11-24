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
    ///                                                                              Follow order, no teleport to the player); round
    ///                                                                              29a: a full pack on a home job goes to the crates
    ///                                                                              and the job carries on (ScWork.ResumeAfterFullPack)
    ///   C1g AvatarSdcsNpcController.SdcsAnyWeaponTick / SdcsAnyWeaponMenuListing(EntityAlive) / SdcsTryWorldEntryMelee(EntityAlive,
    ///       ItemValue)                                                    prefixes 0-XNPCCore's owner-gated weapon picks (its Owner cvar
    ///                                                                              test) never re-arm a resident on a shift from her
    ///                                                                              stores: she is a resident, SC drives her weapons
    ///                                                                              (review #3; its melee fallback is X6's already)
    ///
    /// X2c (round 25b, 0-XNPCCore 3.4.03): its books, its craft window and its Progress page on members (ScBooks, ScWork, ScCraft).
    ///   C1h SdcsBooks.List(EntityAlive)                                   postfix  a member's magazine rows: useful when it trains her
    ///                                                                              specialty, else "won't help her" (NoUse)
    ///   C1i SdcsBooks.Teaches(EntityAlive, Info) (private)                postfix  the same rule at 0-XNPCCore's own gate: a single
    ///                                                                              read refused, ReadAll stops (never used up)
    ///   C1j SdcsBooks.Order(EntityAlive, string)                          prefix + finalizer  a member's read order: the context
    ///                                                                              C1k/C1l act in, one tooltip and a save at its end
    ///   C1k MinEventActionAddProgressionLevel.Execute(MinEventParams)     prefix + postfix  (vanilla) the book's crafting level is
    ///                                                                              put back, specialty XP paid instead
    ///   C1l SdcsBooks.Learned(EntityAlive, Info, List, List[, List])      prefix   that read's line: SC's training line, not a
    ///       (private; 3.4.06 added the 5th, perkBefore)                            level SC resets
    ///   C1m SdcsSayLine.Say(EntityAlive, string)                          prefix   SC's training line said through Voice Control
    ///   C1n XUiC_NpcCraftWindow.CancelJob() (Voice Control, private)      prefix   Cancel on a resident's work shift drops the job
    ///                                                                              only, the shift goes on (followers: its Stay)
    ///   C1o SdcsProgress.List(EntityAlive)                                postfix  "Your progress": a member's role rows show her
    ///                                                                              star level, no craft ladder
    ///
    /// X3 (round 28, 0-XNPCCore 3.4.04; vanilla's dialog window).
    ///   X3a XUiC_DialogWindowGroup.OnOpen()                               postfix  a talk with a resident at home links her to the
    ///                                                                              player while it lasts (ScWork.StartTalk: the talk
    ///                                                                              hold, the user's decision 1)
    ///
    /// C2 (round c2, 2026-11-14; vanilla's station window and list): a member's station crafts never become the player's (ScCraftStation).
    ///   C2a XUiC_RecipeStack.outputStack()                                prefix (last) her queue item made in the player's open
    ///                                                                              window: into the output only, listed under her id once
    ///                                                                              there, no CraftedItem (Survivor Origins' XP) for him;
    ///                                                                              no room: its item kept until it fits
    ///   C2b XUiC_RecipeStack.Update(float)                                prefix   her craft reaches outputStack once, with room; a kept
    ///                                                                              item placed when it fits; vanilla's stale "no room"
    ///                                                                              retry (the player's inventory) cleared on her item
    ///   C2c TileEntityWorkstation.AddCraftComplete(int, ItemValue, string, string, int, int)  prefix  a station's finished crafts
    ///                                                                              listed crafter by crafter when a member is on either
    ///                                                                              side (vanilla merges by item: one paid for the other's)
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

        // X2c (round 25b): 0-XNPCCore 3.4.03's books (SdcsBooks, public; Teaches and Learned are private and take its private
        // nested Info class), its say-line seam, its Progress page, vanilla's magazine action, Voice Control's craft window

        /// <summary>A target whose return type is not the one the patch reads (__result) is not bound.</summary>
        private static MethodBase Returning(MethodInfo m, Type ret)
        {
            return m != null && m.ReturnType == ret ? m : null;
        }

        /// <summary>SdcsBooks.Info, the private class Teaches and Learned take (its public field Item: the book's ItemClass).</summary>
        public static Type BookInfoType()
        {
            return typeof(SdcsBooks).GetNestedType("Info", BindingFlags.NonPublic);
        }

        public static MethodBase BookListTarget()
        {
            return Returning(AccessTools.DeclaredMethod(typeof(SdcsBooks), "List", new[] { typeof(EntityAlive) }), typeof(List<string>));
        }

        public static MethodBase BookTeachesTarget()
        {
            Type info = BookInfoType();
            return info != null ? Returning(AccessTools.DeclaredMethod(typeof(SdcsBooks), "Teaches", new[] { typeof(EntityAlive), info }), typeof(bool)) : null;
        }

        public static MethodBase BookOrderTarget()
        {
            return Returning(AccessTools.DeclaredMethod(typeof(SdcsBooks), "Order", new[] { typeof(EntityAlive), typeof(string) }), typeof(string));
        }

        /// <summary>
        /// SdcsBooks.Learned (private static, the line id said after a read). 0-XNPCCore 3.4.06 added a 5th parameter, List&lt;int&gt;
        /// perkBefore: its perk books' own book-perk levels before the read (ApplyPerkBook now sets them itself), only for its own
        /// "... level N now" line. 3.4.03-3.4.05 take four. The 3.4.06 shape first, then the older one (the U2+U4 integration
        /// round). C1l reads only __0 and sets __result, and steps aside for every read SC did not pay (a perk book among them), so
        /// perkBefore needs nothing from SC: ScPerks' class perks are other progression entries than a book's perks.
        /// </summary>
        public static MethodBase BookLearnedTarget()
        {
            Type info = BookInfoType();
            if (info == null) return null;
            MethodBase m = Returning(AccessTools.DeclaredMethod(typeof(SdcsBooks), "Learned", new[] { typeof(EntityAlive), info, typeof(List<string>), typeof(List<int>), typeof(List<int>) }), typeof(string));
            return m ?? Returning(AccessTools.DeclaredMethod(typeof(SdcsBooks), "Learned", new[] { typeof(EntityAlive), info, typeof(List<string>), typeof(List<int>) }), typeof(string));
        }

        public static MethodBase SayLineTarget()
        {
            return AccessTools.DeclaredMethod(typeof(SdcsSayLine), "Say", new[] { typeof(EntityAlive), typeof(string) });
        }

        public static MethodBase MagazineLevelTarget()
        {
            return AccessTools.DeclaredMethod(typeof(MinEventActionAddProgressionLevel), "Execute", new[] { typeof(MinEventParams) });
        }

        public static MethodBase ProgressListTarget()
        {
            return Returning(AccessTools.DeclaredMethod(typeof(SdcsProgress), "List", new[] { typeof(EntityAlive) }), typeof(List<string>));
        }

        /// <summary>Voice Control's craft window (no namespace), by name: SC has no compile-time reference to Voice Control.</summary>
        public static Type CraftWindowType { get { return ScTypes.Find("XUiC_NpcCraftWindow"); } }

        public static MethodBase CraftCancelTarget()
        {
            Type t = CraftWindowType;
            return t != null ? AccessTools.DeclaredMethod(t, "CancelJob", new Type[0]) : null;
        }

        /// <summary>X3a: vanilla's XUiC_DialogWindowGroup.OnOpen() (public override, declared there).</summary>
        public static MethodBase TalkOpenTarget()
        {
            return AccessTools.DeclaredMethod(typeof(XUiC_DialogWindowGroup), "OnOpen", new Type[0]);
        }

        /// <summary>C2a: vanilla's XUiC_RecipeStack.outputStack() (private, publicized in the shipped assembly), returning bool.</summary>
        public static MethodBase RecipeOutputTarget()
        {
            return Returning(AccessTools.DeclaredMethod(typeof(XUiC_RecipeStack), "outputStack", new Type[0]), typeof(bool));
        }

        /// <summary>C2b: vanilla's XUiC_RecipeStack.Update(float) (its override of XUiController.Update).</summary>
        public static MethodBase RecipeUpdateTarget()
        {
            return AccessTools.DeclaredMethod(typeof(XUiC_RecipeStack), "Update", new[] { typeof(float) });
        }

        /// <summary>C2c: vanilla's TileEntityWorkstation.AddCraftComplete(crafter id, item, recipe name, scrapped item, craft XP, count).</summary>
        public static MethodBase CraftCompleteAddTarget()
        {
            return AccessTools.DeclaredMethod(typeof(TileEntityWorkstation), "AddCraftComplete",
                                              new[] { typeof(int), typeof(ItemValue), typeof(string), typeof(string), typeof(int), typeof(int) });
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
                        // round 33a (round 29 glance LOW 2): still in the station's output here - expected, owed once MoveOut puts them in her window
                        if (d.CraftedItemStack != null && d.CraftedItemStack.itemValue != null) ScWork.NoteMadeAtStation(m.Id, d.CraftedItemStack.itemValue.type, items);
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
                    // round MP3: a client's window has its own copy of the list (written back to the server): the server sweeps it
                    // when the client lets the station go (Patch_StationSweepRemote), so a client's open does nothing here
                    SweepMembers(__instance, "c1e-station-list-swept", "");
                }
                catch (Exception e)
                {
                    ScLog.Once("craft-sweep", e);
                }
            }
        }

        /// <summary>
        /// C1e's sweep (round MP3: shared with the remote sweep): every entry of the station's list under a member's id is paid
        /// to her (ScCraft.OnCrafted, "sweep") and taken off. The server only (the registry's side).
        /// </summary>
        internal static void SweepMembers(TileEntityWorkstation te, string counter, string why)
        {
            List<CraftCompleteData> list = te != null ? te.CraftCompleteList : null;
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
                // on a shift her order still moves the station's output into her window (once the player closes it):
                // owed to the crates then (round 33a: expected until it arrives, ScWork.NoteMadeAtStation)
                if (d.CraftedItemStack != null && d.CraftedItemStack.itemValue != null) ScWork.NoteMadeAtStation(m.Id, d.CraftedItemStack.itemValue.type, items);
                list.RemoveAt(i);
                changed = true;
                Count(counter);
                ScLog.Out("a station's list held " + items + " x " + d.RecipeName + " made by " + m.Name + " - paid to her now" + why);
            }
            if (changed) te.SetModified();
        }

#if SC_V34
        /// <summary>
        /// Round MP3 (part G): vanilla's TileEntity.OnUnlockedServer(int unlocking player, ushort channel) - TileEntityWorkstation
        /// declares none, so this is the base's.
        /// </summary>
        public static MethodBase UnlockedTarget()
        {
            return AccessTools.DeclaredMethod(typeof(TileEntity), "OnUnlockedServer", new[] { typeof(int), typeof(ushort) });
        }

        /// <summary>
        /// C1e for a remote player (round MP3, part G; B section 22). A client's station window makes the queue on its own copy
        /// of the station and lists her crafts there (ScCraftStation), written back to the server on every change and on close;
        /// C1e never sweeps on a client (no registry). So when a REMOTE player lets a station go (LockManager.UnlockRequestServer:
        /// their close - its last write-back is already in, the same ordered channel - or their disconnect), the server sweeps
        /// ITS copy: her entries paid and taken off, the change sent to the players near it. Nobody holds the station then, so
        /// nothing overwrites it; 0-XNPCCore's own pay (C1d) waits while a station is locked (SdcsStations.IsFree), so it never
        /// pays the same entry. The host's own lock is left to the host's C1e at its next open, as before (single player
        /// unchanged). Any other tile entity: one type test.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_StationSweepRemote
        {
            [HarmonyPrepare]
            public static bool Prepare() { return Bound(UnlockedTarget, "TileEntity.OnUnlockedServer(int, ushort)"); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return UnlockedTarget(); }

            /// <summary>__0 the unlocking player's entity id (by position: vanilla's parameter names are not part of the contract).</summary>
            [HarmonyPostfix]
            public static void Postfix(TileEntity __instance, int __0)
            {
                try
                {
                    TileEntityWorkstation ws = __instance as TileEntityWorkstation;
                    if (ws == null || !ScCommunity.IsHost || !ScRegistry.Loaded) return;
                    if (ScPlayers.ByEntity(__0) is EntityPlayerLocal) return;      // the host's own: its C1e swept at the open
                    World w = ScCommunity.World;
                    if (w != null && w.GetPrimaryPlayerId() == __0) return;
                    SweepMembers(ws, "c1e-station-list-swept-remote", " (player " + __0 + " closed the station)");
                }
                catch (Exception e)
                {
                    ScLog.Once("craft-sweep-remote", e);
                }
            }
        }
#endif

        // ------------------------------------------------------------------ C2a-C2c (round c2): her station crafts stay hers ----

        /// <summary>
        /// C2a. The player's open station window makes the queue (the station's own loop stops while he has it open), and
        /// vanilla's outputStack gives him a member's item when the output is full and his quest signal (Survivor Origins' craft
        /// XP) for every one of them. ScCraftStation.OutputStack makes her item instead: into the output or, with no room, kept
        /// until it fits (C2b places it; outputStack is not called again for that craft). Runs last among the prefixes, so
        /// another mod's swap (Survivor Origins' burnt meat, a quality roll) is already on the recipe and SCore's extras are
        /// placed. A call another prefix has skipped keeps that answer (Harmony ANDs them); only SCore's "an extra found no
        /// room" skip (isInventoryFull, false) on her craft is turned into her kept item (ScCraftStation.AfterSkipped).
        /// </summary>
        [HarmonyPatch]
        public static class Patch_MemberCraftOutput
        {
            [HarmonyPrepare]
            public static bool Prepare() { return Bound(RecipeOutputTarget, "XUiC_RecipeStack.outputStack()"); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return RecipeOutputTarget(); }

            [HarmonyPrefix]
            [HarmonyPriority(Priority.Last)]
            public static bool Prefix(XUiC_RecipeStack __instance, ref bool __result, bool __runOriginal)
            {
                if (!__runOriginal)
                {
                    // an earlier prefix answered: SCore's additional output with no room for an extra becomes her kept item
                    try { ScCraftStation.AfterSkipped(__instance, __result); }
                    catch (Exception e) { ScLog.Once("craft-c2-skipped", e); }
                    return true;
                }
                try
                {
                    bool made;
                    if (!ScCraftStation.OutputStack(__instance, out made)) return true;
                    __result = made;
                    return false;
                }
                catch (Exception e)
                {
                    ScLog.Once("craft-c2-output", e);      // only the member test can get here: not known to be hers, vanilla's
                    return true;
                }
            }
        }

        /// <summary>
        /// C2b. Before the window's Update (ScCraftStation.BeforeUpdate): vanilla's Update retries a "no room" item
        /// (isInventoryFull) into the player's inventory and SetRecipe / CopyTo never clear that flag within an open window, so
        /// a member's queue item in such a slot is cleared first; her craft reaches outputStack only with room for its item; a
        /// made item kept for room is placed here when it fits. A few field reads per frame for everyone else (the flag, any
        /// hold on record, whether the slot's craft is due).
        /// </summary>
        [HarmonyPatch]
        public static class Patch_MemberCraftStaleFull
        {
            [HarmonyPrepare]
            public static bool Prepare() { return Bound(RecipeUpdateTarget, "XUiC_RecipeStack.Update(float)"); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return RecipeUpdateTarget(); }

            [HarmonyPrefix]
            public static void Prefix(XUiC_RecipeStack __instance)
            {
                if (__instance == null) return;
                if (!__instance.isInventoryFull && !ScCraftStation.Holding
                    && !(__instance.isCrafting && __instance.craftingTimeLeft <= 0f && __instance.recipe != null)) return;
                try
                {
                    ScCraftStation.BeforeUpdate(__instance);
                }
                catch (Exception e)
                {
                    ScLog.Once("craft-c2-stale", e);
                }
            }
        }

        /// <summary>
        /// C2c. A station's finished crafts (CraftCompleteList, paid to the player by vanilla when he opens it, to a member by
        /// C1d / C1e) are merged by item alone: listed crafter by crafter when a member is on either side
        /// (ScCraftStation.KeepApart). Positional arguments: vanilla's parameter names are not part of the contract.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_CraftCompleteApart
        {
            [HarmonyPrepare]
            public static bool Prepare() { return Bound(CraftCompleteAddTarget, "TileEntityWorkstation.AddCraftComplete(int, ItemValue, string, string, int, int)"); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return CraftCompleteAddTarget(); }

            [HarmonyPrefix]
            public static bool Prefix(TileEntityWorkstation __instance, int __0, ItemValue __1, string __2, string __3, int __4, int __5)
            {
                try
                {
                    return !ScCraftStation.KeepApart(__instance, __0, __1, __2, __3, __4, __5);
                }
                catch (Exception e)
                {
                    ScLog.Once("craft-c2-apart", e);
                    return true;
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
                    // round 29a (J29, the user's decision 3): a full pack on a home job - what she gathered goes to the home
                    // crates and she carries on (ScWork.ResumeAfterFullPack); the job ends as before when that frees too little
                    if (lineId == "loot.full" && ScWork.ResumeAfterFullPack(m, npc))
                    {
                        Count("c1f-pack-full-resumed");
                        return false;
                    }
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

        // ------------------------------------------------------------------ C1h-C1m (X2c): magazines become training ----
        // Positional arguments (__0, __1): 0-XNPCCore's parameter names are not part of its contract; the targets are bound by
        // exact signature. The rule and its bookkeeping are ScBooks'.

        /// <summary>C1h. The rows of the "Read..." page, the router's single-read refusal and ReadAll's pick all come from List.</summary>
        [HarmonyPatch]
        public static class Patch_BookRows
        {
            [HarmonyPrepare]
            public static bool Prepare() { return Bound(BookListTarget, "SdcsBooks.List(EntityAlive)"); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return BookListTarget(); }

            [HarmonyPostfix]
            public static void Postfix(EntityAlive __0, List<string> __result)
            {
                try
                {
                    ScBooks.RewriteRows(__0, __result);
                }
                catch (Exception e)
                {
                    ScLog.Once("craft-book-rows", e);
                }
            }
        }

        /// <summary>
        /// C1i. 0-XNPCCore's own gate, under every read: a single read refuses (its "book.nothing" line) and ReadAll's loop over
        /// one book's copies stops when a magazine no longer trains her - at her seventh star mid-loop too. Never makes a book
        /// readable that 0-XNPCCore refuses.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_BookTeaches
        {
            private static FieldInfo fItem;

            [HarmonyPrepare]
            public static bool Prepare()
            {
                if (!Bound(BookTeachesTarget, "SdcsBooks.Teaches(EntityAlive, Info)")) return false;
                fItem = AccessTools.Field(BookInfoType(), "Item");
                if (fItem != null && fItem.FieldType == typeof(ItemClass)) return true;
                ScLog.Warn("crafting: SdcsBooks.Info.Item (ItemClass) not found - that SurvivorCommunity patch is off (0-XNPCCore update?)");
                return false;
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return BookTeachesTarget(); }

            /// <summary>__1 is SdcsBooks.Info (private): taken as object, its Item read through the bound field.</summary>
            [HarmonyPostfix]
            public static void Postfix(EntityAlive __0, object __1, ref bool __result)
            {
                if (!__result || __1 == null) return;
                try
                {
                    if (!ScBooks.Teaches(__0, fItem.GetValue(__1) as ItemClass)) __result = false;
                }
                catch (Exception e)
                {
                    ScLog.Once("craft-book-teaches", e);
                }
            }
        }

        /// <summary>
        /// C1j. Every read - the "Read..." page, the voice's "read your books" (read:*), Voice Control's router - is one Order call
        /// on the host: the context C1k and C1l act in opens here for a member and always closes (a finalizer, so a throw in
        /// 0-XNPCCore cannot leave it open), with the tooltip and the registry save.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_BookOrder
        {
            [HarmonyPrepare]
            public static bool Prepare() { return Bound(BookOrderTarget, "SdcsBooks.Order(EntityAlive, string)"); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return BookOrderTarget(); }

            [HarmonyPrefix]
            public static void Prefix(EntityAlive __0, string __1)
            {
                try
                {
                    ScBooks.Begin(__0, __1);
                }
                catch (Exception e)
                {
                    ScLog.Once("craft-book-order", e);
                }
            }

            [HarmonyFinalizer]
            public static void Finalizer()
            {
                try
                {
                    ScBooks.End();
                }
                catch (Exception e)
                {
                    ScLog.Once("craft-book-order-end", e);
                }
            }
        }

        /// <summary>
        /// C1k (vanilla). The magazine's AddProgressionLevel, fired by 0-XNPCCore's SdcsBooks.Fire: within a member's read order,
        /// for the book's own crafting action with her as a target, her level is read before and put back after (her stars set
        /// it), and her specialty XP is paid. Everyone else - the player's own magazines included - never gets past the context's
        /// null check.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_MagazineLevel
        {
            [HarmonyPrepare]
            public static bool Prepare() { return Bound(MagazineLevelTarget, "MinEventActionAddProgressionLevel.Execute(MinEventParams)"); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return MagazineLevelTarget(); }

            [HarmonyPrefix]
            public static void Prefix(MinEventActionAddProgressionLevel __instance, MinEventParams __0, out ScBooks.LevelState __state)
            {
                __state = null;
                try
                {
                    __state = ScBooks.BeforeLevel(__instance, __0);
                }
                catch (Exception e)
                {
                    ScLog.Once("craft-book-level", e);
                }
            }

            [HarmonyPostfix]
            public static void Postfix(ScBooks.LevelState __state)
            {
                if (__state == null) return;
                try
                {
                    ScBooks.AfterLevel(__state);
                }
                catch (Exception e)
                {
                    ScLog.Once("craft-book-pay", e);
                }
            }
        }

        /// <summary>C1l. The line for a read that trained her (the level SC put back would read "it changed nothing", with a warning).</summary>
        [HarmonyPatch]
        public static class Patch_BookLine
        {
            [HarmonyPrepare]
            public static bool Prepare() { return Bound(BookLearnedTarget, "SdcsBooks.Learned(EntityAlive, Info, List<string>, List<int>[, List<int>])"); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return BookLearnedTarget(); }

            /// <summary>__0 the reader; the lists (four parameters on 3.4.03-3.4.05, five on 3.4.06) are 0-XNPCCore's own.</summary>
            [HarmonyPrefix]
            public static bool Prefix(EntityAlive __0, ref string __result)
            {
                try
                {
                    string line;
                    if (!ScBooks.TrainingLine(__0, out line)) return true;
                    __result = line;
                    return false;
                }
                catch (Exception e)
                {
                    ScLog.Once("craft-book-line", e);
                    return true;
                }
            }
        }

        /// <summary>C1m. 0-XNPCCore's one say-line seam (Order's single read, ReadAll's "only line"): SC's training line id is SC's to say.</summary>
        [HarmonyPatch]
        public static class Patch_BookSay
        {
            [HarmonyPrepare]
            public static bool Prepare() { return Bound(SayLineTarget, "SdcsSayLine.Say(EntityAlive, string)"); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return SayLineTarget(); }

            [HarmonyPrefix]
            public static bool Prefix(EntityAlive __0, string __1)
            {
                try
                {
                    return !ScBooks.SayTraining(__0, __1);
                }
                catch (Exception e)
                {
                    ScLog.Once("craft-book-say", e);
                    return !string.Equals(__1, ScBooks.TrainLineId, StringComparison.Ordinal);   // never Voice Control's "unknown line id"
                }
            }
        }

        // ------------------------------------------------------------------ C1n (X2c): Cancel keeps the work shift ----

        /// <summary>
        /// C1n (finding 5). The craft window's Cancel (Voice Control's XUiC_NpcCraftWindow.CancelJob, a private instance method of
        /// its UI) sends VCCommand.Stay, and ScWork.Tick ends a shift on any order but Guard. Chosen over a Stay rule in Tick:
        ///  - it catches exactly the button whose meaning is "drop this job", and nothing else: a voice "stay here" or the order
        ///    wheel's Stay on a resident at work still ends the shift as documented (C1), and a follower's Cancel is Voice
        ///    Control's own Stay;
        ///  - her Guard order at the craft spot (buffSdcsCraftHere wrote it) stays, so the shift's own rule needs no exception and
        ///    the next craft order starts from the same state as the first; a Stay rule would leave her on buffOrderStay instead;
        ///  - the refund happens at once (ScWork.DropJob, as Tick's review #1 path would a second later) and the window relists.
        /// The window is UI on the machine that opened it; shifts exist on the host only (ScWork.Start needs the host's local
        /// player), which is where a shift's window is. Anyone not on a shift: the original runs.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_CraftWindowCancel
        {
            private static FieldInfo fNpcId;
            private static MethodInfo mRelist;

            [HarmonyPrepare]
            public static bool Prepare()
            {
                if (CraftWindowType == null) return false;                // no Voice Control: nothing to patch, nothing to warn about
                if (!Bound(CraftCancelTarget, "XUiC_NpcCraftWindow.CancelJob()")) return false;
                fNpcId = AccessTools.Field(CraftWindowType, "s_npcId");
                mRelist = AccessTools.Method(CraftWindowType, "Relist", new[] { typeof(EntityPlayerLocal), typeof(EntityAlive) });
                if (fNpcId != null && fNpcId.FieldType == typeof(int) && fNpcId.IsStatic) return true;
                ScLog.Warn("crafting: XUiC_NpcCraftWindow.s_npcId not found - that SurvivorCommunity patch is off (Voice Control update?)");
                return false;
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return CraftCancelTarget(); }

            [HarmonyPrefix]
            public static bool Prefix(XUiController __instance)
            {
                try
                {
                    int id = (int)fNpcId.GetValue(null);
                    World w = GameManager.Instance != null ? GameManager.Instance.World : null;
                    EntityAlive npc = w != null && id > 0 ? w.GetEntity(id) as EntityAlive : null;
                    Member m = MemberOf(npc);
                    bool dropped;
                    if (m == null || !ScWork.CancelJobOnShift(m, npc, out dropped)) return true;
                    if (!dropped) return false;                // on a shift with no job: nothing to cancel, nothing counted
                    Count("c1n-window-cancel-kept-shift");
                    EntityPlayerLocal player = __instance != null && __instance.xui != null && __instance.xui.playerUI != null ? __instance.xui.playerUI.entityPlayer : null;
                    if (mRelist != null && player != null) mRelist.Invoke(__instance, new object[] { player, npc });
                    return false;
                }
                catch (Exception e)
                {
                    ScLog.Once("craft-window-cancel", e);
                    return true;
                }
            }
        }

        // ------------------------------------------------------------------ X3a (round 28): the talk hold ----

        /// <summary>
        /// X3a (the user's decision 1, 2026-11-10). As a talk opens on a resident at home, ScWork.StartTalk links her to the
        /// player (the work shift's Owner link in its Talk phase), so 0-XNPCCore 3.4.04's hired rows - each gated on SCore's
        /// Leader requirement, which accepts the leader or the owner - show and work for her: Craft... (decision 2: the members'
        /// only craft row), Jobs and gear, Show me your inventory. A postfix: OnOpen has set the dialog and its statement and
        /// marked the response list dirty, which fills on its next Update - after this, so it sees the link (SCore's leader cache
        /// is dropped with it). Also the hold-E ring's jump into her dialog (Weapons, Read, Progress) opens here. The hold's end
        /// (2 s after the last talk window closed) and its turn into a shift are ScWork.Tick's. Host only (CanHold); a member who
        /// is not a resident at home, or is on a shift already, is left as she is.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_TalkHold
        {
            [HarmonyPrepare]
            public static bool Prepare() { return Bound(TalkOpenTarget, "XUiC_DialogWindowGroup.OnOpen()"); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return TalkOpenTarget(); }

            [HarmonyPostfix]
            public static void Postfix(XUiC_DialogWindowGroup __instance)
            {
                try
                {
                    XUi xui = __instance != null ? __instance.xui : null;
                    EntityNPC npc = xui != null && xui.Dialog != null ? xui.Dialog.Respondent : null;
                    EntityPlayerLocal player = xui != null && xui.playerUI != null ? xui.playerUI.entityPlayer : null;
                    Member m = MemberOf(npc);
                    if (m == null || player == null) return;
                    if (ScWork.StartTalk(m, npc, player)) Count("x3a-talk-hold-started");
                }
                catch (Exception e)
                {
                    ScLog.Once("craft-talk-hold", e);
                }
            }
        }

        // ------------------------------------------------------------------ C1o (X2c): the Progress page ----

        /// <summary>C1o (finding 3). "Your progress" rows (host side; a client gets them through Voice Control's net package): ScCraft.ProgressRows.</summary>
        [HarmonyPatch]
        public static class Patch_ProgressRows
        {
            [HarmonyPrepare]
            public static bool Prepare() { return Bound(ProgressListTarget, "SdcsProgress.List(EntityAlive)"); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return ProgressListTarget(); }

            [HarmonyPostfix]
            public static void Postfix(EntityAlive __0, List<string> __result)
            {
                try
                {
                    ScCraft.ProgressRows(__0, __result);
                }
                catch (Exception e)
                {
                    ScLog.Once("craft-progress-rows", e);
                }
            }
        }
    }
}
