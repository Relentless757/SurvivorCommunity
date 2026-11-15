using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Audio;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round c2 (2026-11-14, the user's report: a member cooked 500+ grilled corn, the queued rest went into the player's
    /// inventory when the campfire's output was full, and both the player and the member got the crafting XP): a member's
    /// station crafts while the player has that station's window open, and the station's list of finished crafts.
    ///
    /// 0-XNPCCore queues a member's station craft in the station's own queue under her entity id (SdcsStationCraft.QueueIn:
    /// RecipeQueueItem.StartingEntityId). The ingredients are taken when the order is given. Then:
    ///  - Window closed: the station's HandleRecipeQueue makes it (vanilla's, or SCore's replacement of it). An item goes into
    ///    the output, and when the output is full the queue waits. Each item made is listed under her id (AddCraftComplete),
    ///    and 0-XNPCCore's PayXp (C1d) or SC's sweep (C1e) pays her for it once. This path was always right.
    ///  - Window open: the station's queue stops (HandleRecipeQueue returns while bUserAccessing) and vanilla's window makes it
    ///    instead, through XUiC_RecipeStack.outputStack, whose side effects belong to the player whatever StartingEntityId says:
    ///    a. the output full: AddItemToInventory puts the item in the PLAYER's inventory. Update's "no room" retry
    ///       (isInventoryFull) does the same once the player has room. This is where the 500 corn went.
    ///    b. every item: QuestEventManager.CraftedItem, the player's quest and challenge signal. Survivor Origins pays the
    ///       primary player's craft XP from a postfix on it (Patch_QuestEventManager_CraftedItem: $q_item, $xp_skill). So the
    ///       player got cooking XP for her corn, in the output and in his inventory alike.
    ///    c. her listing (AddCraftComplete) is written before the output is tried, so items that went to the player were
    ///       listed for her too and paid to her at the next opening (C1e): the "double" XP.
    ///    OutputStack replaces outputStack's body for a member's queue item: into the station's output only; listed under her
    ///    id once it is there (never before); no CraftedItem, achievement or XP for the player. The player's own queue items,
    ///    repairs and anyone else's are vanilla's.
    ///  - One outputStack call per craft. Other mods' prefixes on outputStack act on every call: SCore's additional output
    ///    (Harmony.RecipesPatches) puts the craft's extras in the output first, Survivor Origins rolls burnt meat (a log line per
    ///    call), REMS rolls a quality. So BeforeUpdate (the Update prefix) lets her craft reach outputStack only with room in
    ///    the output for her next item, and when outputStack has run but her item still found no room (the extras took it, the
    ///    roll made a variant that does not fit), that exact item is kept (Hold.Pending) and BeforeUpdate places it itself when
    ///    room frees, counting the craft off as vanilla's own "no room" retry does. Nothing is rolled or added twice. While
    ///    there is no room her craft waits in the queue (a tooltip and a log line per wait), with no craft time banked.
    ///  - Vanilla's stale "no room" retry: isInventoryFull survives SetRecipe and CopyTo within one open window (OnOpen resets
    ///    it), so her craft can move into a slot whose earlier craft found no room (the player's front craft, cancelled), with
    ///    its old outputItemValue (case a again): cleared on her item. SCore's extras prefix also sets it when an extra finds no
    ///    room and skips the body: on her craft that becomes a kept item (AfterSkipped), the flag cleared in the same call.
    ///  - KeepApart: AddCraftComplete merges by item alone, whoever made it. A member's corn joined the player's own entry
    ///    (vanilla pays him at his next opening: vanilla XP and, through EntityPlayerLocal.GiveExp's CraftedItem, Survivor
    ///    Origins'), or his joined hers (paid to her, nothing to him). When a member is on either side the entries stay apart.
    ///
    /// The member's remaining crafts WAIT when the output is full (the user's decision allowed waiting or a clean end; waiting
    /// is what the station itself does, and 0-XNPCCore's MoveOut and SC's work-shift deposit keep freeing it while her order
    /// lives). When her order ends with the station closed, 0-XNPCCore's Cancel takes her unmade crafts out of the queue and
    /// gives their ingredients back, as before. A kept item whose window closes before it fits is dropped: its craft is still
    /// in the queue (never counted off, never listed), so the station makes it again - SCore's extras for that one craft
    /// included, as the game does with its own "no room" item. Host or single player (the registry's side), members only: a
    /// multiplayer client's own window still makes a member's queue as vanilla does.
    /// </summary>
    public static class ScCraftStation
    {
        /// <summary>
        /// A hold not refreshed for this long (real seconds) is over. BeforeUpdate refreshes every hold on each frame its slot
        /// updates, and the window's slots update only while it is open: a stale hold means the window was closed. 3 s, not
        /// less, so a long frame (a save, a hitch) is not taken for a closed window; a close and reopen within it keeps the
        /// hold, which is safe either way (a kept craft is still counted in the queue until its item is placed).
        /// </summary>
        private const float HoldGapSeconds = 3f;
        /// <summary>
        /// While her craft waits, its craft time is set to this (just above 0) before Update looks at it, so Update does not
        /// call outputStack that frame. Vanilla never calls outputStack again for an item that found no room either (its own
        /// retry is isInventoryFull).
        /// </summary>
        private const float HoldTick = 0.0001f;
        /// <summary>The stale "no room" line is written at most this often (real seconds); the counter counts every one.</summary>
        private const float StaleLogSeconds = 60f;

        private sealed class Hold
        {
            public int MemberId, Left;
            public string Member, Recipe;
            public float Since, Seen;
            /// <summary>The window's output grid (null: none, the hold stays).</summary>
            public XUiC_WorkstationOutputGrid Grid;
            /// <summary>Nothing made yet: her next item as Update sees the recipe (the room test before outputStack).</summary>
            public Recipe NextFor;
            public ItemStack Next;
            /// <summary>
            /// Made already (outputStack ran: other mods' extras placed, their rolls done) and its exact item waiting for room,
            /// with what its listing needs. PendingFor: the queue slot's recipe as Update saw it when the craft was let through.
            /// </summary>
            public ItemStack Pending;
            public Recipe PendingFor;
            public string PendingName, PendingScrapped, PendingArea;
            public int PendingExp;
        }

        private static readonly Dictionary<XUiC_RecipeStack, Hold> holds = new Dictionary<XUiC_RecipeStack, Hold>();
        private static readonly List<XUiC_RecipeStack> staleHolds = new List<XUiC_RecipeStack>();
        /// <summary>The queue slot BeforeUpdate let through to outputStack this frame, and its recipe before other mods swap it.</summary>
        private static XUiC_RecipeStack dueStack;
        private static Recipe dueRecipe;
        private static float lastPurge, lastStaleLog;
        private static int crafts, items, holdsStarted, holdsEnded, keptItems, keptPlaced, requeued, staleFull, extrasNoRoom, keptApart;

        public static void Reset()
        {
            holds.Clear();
            dueStack = null;
            dueRecipe = null;
            lastPurge = lastStaleLog = 0f;
            crafts = items = holdsStarted = holdsEnded = keptItems = keptPlaced = requeued = staleFull = extrasNoRoom = keptApart = 0;
        }

        /// <summary>Any hold on record (C2b's per-frame test for everyone: one field read when there is none).</summary>
        public static bool Holding { get { return holds.Count > 0; } }

        /// <summary>
        /// Is this window queue item a member's? Her recipe in a workstation window, not a repair (a repair is always the
        /// player's: SetRepairRecipe), not the player's own, and its StartingEntityId one of the registry's members.
        /// </summary>
        public static bool MemberStack(XUiC_RecipeStack rs, out Member m, out XUiC_WorkstationWindowGroup wg)
        {
            m = null;
            wg = null;
            if (rs == null || rs.recipe == null) return false;
            if (rs.originalItem != null && !rs.originalItem.IsEmpty()) return false;
            int id = rs.StartingEntityId;
            EntityPlayerLocal p = rs.xui != null && rs.xui.playerUI != null ? rs.xui.playerUI.entityPlayer : null;
            if (p == null || id == p.entityId || id == -1) return false;
            wg = rs.windowGroup != null ? rs.windowGroup.Controller as XUiC_WorkstationWindowGroup : null;
            if (wg == null) return false;
            m = ScRegistry.GetMember(id);
            return m != null;
        }

        /// <summary>The recipe Update saw for this slot when BeforeUpdate let it through (inside outputStack another mod may have swapped it).</summary>
        private static Recipe Unswapped(XUiC_RecipeStack rs)
        {
            Recipe r = ReferenceEquals(dueStack, rs) && dueRecipe != null ? dueRecipe : rs.recipe;
            dueStack = null;
            dueRecipe = null;
            return r;
        }

        // ------------------------------------------------------------------ C2a: her craft in the open window ----

        /// <summary>
        /// C2a (the outputStack prefix, the body). True: a member's queue item, handled here and vanilla's body skipped, with
        /// result as outputStack's answer (true = one craft made, the caller counts it off; false = nothing counted off).
        /// False: not hers, vanilla runs.
        /// </summary>
        public static bool OutputStack(XUiC_RecipeStack rs, out bool result)
        {
            result = false;
            Member m;
            XUiC_WorkstationWindowGroup wg;
            if (!MemberStack(rs, out m, out wg)) return false;
            // hers from here on: a fault never hands her craft to vanilla (the player's inventory); before the item is placed it
            // waits (logged), after it is placed it counts as made (never made twice)
            Recipe orig = Unswapped(rs);
            Recipe r = rs.recipe;
            ItemValue iv;
            TileEntityWorkstation te;
            Hold kept;
            try
            {
                XUiC_WorkstationOutputGrid grid = wg.GetChildByType<XUiC_WorkstationOutputGrid>();
                te = wg.WorkstationData != null ? wg.WorkstationData.TileEntity : null;
                if (holds.TryGetValue(rs, out kept) && kept.Pending != null && kept.MemberId == m.Id)
                {
                    // made already (BeforeUpdate places it; something called outputStack anyway): the kept item, nothing new
                    if (grid == null || te == null || !TryPut(grid, kept.Pending.itemValue, kept.Pending.count))
                    {
                        rs.craftingTimeLeft = 0f;
                        kept.Seen = Time.unscaledTime;
                        return true;
                    }
                    result = true;
                    Listed(te, m, kept.Pending.itemValue, kept.PendingName, kept.PendingScrapped, kept.PendingExp, kept.Pending.count);
                    Made(rs, m, kept.Pending.count, kept.PendingArea, true);
                    return true;
                }
                iv = new ItemValue(r.itemValueType, rs.outputQuality, rs.outputQuality);     // vanilla's own item for the window
                if (iv.ItemClass == null) return true;            // vanilla's answer too: nothing made
                rs.outputItemValue = iv;                          // as vanilla sets it (other mods' postfixes read it)
                if (grid == null || te == null || !TryPut(grid, iv, r.count))
                {
                    // outputStack has run for this craft (other mods' extras placed, their rolls done): its item is kept as it
                    // is, and BeforeUpdate places it - outputStack is not called again for this craft
                    rs.craftingTimeLeft = 0f;                     // no craft time banked while it waits in the window
                    HoldOn(rs, m, grid, new ItemStack(iv.Clone(), r.count), orig, r, "its item has no room in the output");
                    return true;
                }
            }
            catch (Exception e)
            {
                rs.craftingTimeLeft = 0f;
                ScLog.Once("c2-output-put", e);
                return true;
            }
            result = true;
            Listed(te, m, iv, r.GetName(), Scrapped(r), r.craftExpGain, r.count);
            Made(rs, m, r.count, r.craftingArea, false);
            return true;
        }

        /// <summary>
        /// C2a, when an earlier prefix skipped outputStack's body. SCore's additional output does so when one of the craft's
        /// extras finds no room in the output: it sets isInventoryFull and answers false, and vanilla's Update would retry the
        /// slot's outputItemValue into the player's inventory. On her craft: the flag is cleared and her item kept, as when it
        /// finds no room itself (the extras that fitted are in the output; SCore drops the rest, its own behavior for anyone).
        /// Any other skip (no flag, or a craft answered as made) is left to whoever skipped it.
        /// </summary>
        public static void AfterSkipped(XUiC_RecipeStack rs, bool result)
        {
            if (rs == null || result || !rs.isInventoryFull) return;
            Member m;
            XUiC_WorkstationWindowGroup wg;
            if (!MemberStack(rs, out m, out wg)) return;
            rs.isInventoryFull = false;
            rs.craftingTimeLeft = 0f;
            Recipe orig = Unswapped(rs);
            Hold kept;
            if (holds.TryGetValue(rs, out kept) && kept.Pending != null) return;      // its item is kept already
            Recipe r = rs.recipe;
            ItemValue iv = new ItemValue(r.itemValueType, rs.outputQuality, rs.outputQuality);
            if (iv.ItemClass == null) return;
            extrasNoRoom++;
            Patches_Craft.Count("c2a-member-craft-extra-no-room");
            HoldOn(rs, m, wg.GetChildByType<XUiC_WorkstationOutputGrid>(), new ItemStack(iv, r.count), orig, r,
                   "another mod's extra output for it found no room in the output");
        }

        /// <summary>Her listing for one craft now in the output: C1d / C1e pay her once for it, the player never.</summary>
        private static void Listed(TileEntityWorkstation te, Member m, ItemValue iv, string name, string scrapped, int exp, int count)
        {
            try
            {
                te.AddCraftComplete(m.Id, iv, name, scrapped, exp, count);
            }
            catch (Exception e)
            {
                ScLog.Once("c2-output-list", e);
            }
        }

        /// <summary>The bookkeeping after one of her crafts reached the output (never throws: the craft is made either way).</summary>
        private static void Made(XUiC_RecipeStack rs, Member m, int count, string area, bool wasKept)
        {
            try
            {
                rs.originalItem = ItemValue.None;                 // as vanilla ends a successful output
                if (rs.playSound) PlayDone(area);
                Hold h;
                if (holds.Count > 0 && holds.TryGetValue(rs, out h))
                {
                    holds.Remove(rs);
                    holdsEnded++;
                    ScLog.Verbose(m.Name + "'s " + h.Recipe + " carries on: the station's output has room again");
                }
                if (wasKept)
                {
                    keptPlaced++;
                    Patches_Craft.Count("c2a-member-kept-item-placed");
                }
                crafts++;
                items += count;
                Patches_Craft.Count("c2a-member-craft-to-output");
            }
            catch (Exception e)
            {
                ScLog.Once("c2-output-after", e);
            }
        }

        /// <summary>Vanilla's output placement (the first stack it fits on, else the first empty slot), written back as vanilla does.</summary>
        private static bool TryPut(XUiC_WorkstationOutputGrid grid, ItemValue iv, int count)
        {
            ItemStack made = new ItemStack(iv.Clone(), count);
            ItemStack[] slots = grid.GetSlots();
            if (slots == null) return false;
            int at = -1;
            for (int i = 0; i < slots.Length && at < 0; i++)
            {
                if (slots[i] == null || !slots[i].CanStackWith(made)) continue;
                slots[i].count += count;
                at = i;
            }
            for (int j = 0; j < slots.Length && at < 0; j++)
            {
                if (slots[j] != null && !slots[j].IsEmpty()) continue;
                slots[j] = made;
                at = j;
            }
            if (at < 0) return false;
            grid.SetSlots(slots);
            grid.UpdateData(slots);                               // also the station's own output (XUiM_Workstation.SetOutputStacks)
            grid.IsDirty = true;
            return true;
        }

        private static string Scrapped(Recipe r)
        {
            if (!r.IsScrap || r.ingredients == null || r.ingredients.Count == 0) return "";
            ItemClass ic = r.ingredients[0].itemValue != null ? r.ingredients[0].itemValue.ItemClass : null;
            return ic != null ? ic.GetItemName() : "";
        }

        /// <summary>The station's "done" sound, as vanilla plays it for any item the window puts in the output.</summary>
        private static void PlayDone(string area)
        {
            try
            {
                if (area != null)
                {
                    WorkstationData wd = CraftingManager.GetWorkstationData(area);
                    if (wd != null) Manager.PlayInsidePlayerHead(wd.CraftCompleteSound);
                }
                else Manager.PlayInsidePlayerHead("craft_complete_item");
            }
            catch (Exception e)
            {
                ScLog.Once("c2-sound", e);
            }
        }

        /// <summary>
        /// No room: one log line and one tooltip when a hold starts. A hold of the same member on this queue slot seen within
        /// HoldGapSeconds goes on quietly. With pending (outputStack has run for this craft) its exact item is kept on the hold.
        /// </summary>
        private static void HoldOn(XUiC_RecipeStack rs, Member m, XUiC_WorkstationOutputGrid grid, ItemStack pending, Recipe pendingFor, Recipe made, string why)
        {
            float now = Time.unscaledTime;
            Hold h;
            bool had = holds.TryGetValue(rs, out h);
            bool fresh = had && h.MemberId == m.Id && now - h.Seen <= HoldGapSeconds;
            if (!fresh)
            {
                if (had) Drop(rs, h, "its window was closed");
                h = new Hold { MemberId = m.Id, Member = m.Name, Recipe = rs.recipe.GetName(), Since = now };
                holds[rs] = h;
            }
            h.Seen = now;
            h.Left = rs.recipeCount;
            if (grid != null) h.Grid = grid;
            if (pending != null && made != null)
            {
                h.Pending = pending;
                h.PendingFor = pendingFor;
                h.PendingName = made.GetName();
                h.PendingScrapped = Scrapped(made);
                h.PendingExp = made.craftExpGain;
                h.PendingArea = made.craftingArea;
                keptItems++;
                Patches_Craft.Count("c2a-member-craft-item-kept");
                ScLog.Verbose(m.Name + "'s " + h.PendingName + " is made but " + why + " - it is kept and goes in when there is room");
            }
            if (fresh) return;
            holdsStarted++;
            Patches_Craft.Count("c2a-member-craft-held");
            ScLog.Out("a station's output is full: " + m.Name + "'s " + rs.recipeCount + " x " + h.Recipe + " wait in its queue while your window is open"
                      + " - they carry on when there is room (nothing goes to your inventory)");
            EntityPlayerLocal p = rs.xui != null && rs.xui.playerUI != null ? rs.xui.playerUI.entityPlayer : null;
            ScCommunity.Tell(p, string.Format(Localization.Get("scCraftOutputHeld"), m.Name), "ui_denied");
        }

        /// <summary>A hold is over without its item placed: a kept item's craft is still in the queue (never counted off or listed).</summary>
        private static void Drop(XUiC_RecipeStack rs, Hold h, string why)
        {
            holds.Remove(rs);
            if (h == null || h.Pending == null) return;
            // the slot no longer holds her craft: it was cancelled in the window (vanilla returned the ingredients of every
            // craft left, the kept one included) - the kept item goes with it, and nothing is made again
            bool stillHers = rs != null && rs.recipe != null && rs.recipeCount > 0 && h.Recipe != null
                             && string.Equals(rs.recipe.GetName(), h.Recipe, StringComparison.Ordinal);
            if (!stillHers)
            {
                Patches_Craft.Count("c2a-member-kept-item-cancelled");
                ScLog.Out(h.Member + "'s " + h.PendingName + " was waiting for room when the craft was cancelled - the kept item is dropped with it"
                          + " (the window returned its ingredients); nothing is made again");
                return;
            }
            requeued++;
            Patches_Craft.Count("c2a-member-kept-item-requeued");
            ScLog.Out(h.Member + "'s " + h.PendingName + " was made in your window but never found room before " + why
                      + " - its craft is still in the queue and is made again");
        }

        /// <summary>Holds not refreshed for HoldGapSeconds: their windows were closed (at most once per HoldGapSeconds).</summary>
        private static void Purge(float now)
        {
            lastPurge = now;
            staleHolds.Clear();
            foreach (var kv in holds) if (now - kv.Value.Seen > HoldGapSeconds) staleHolds.Add(kv.Key);
            foreach (XUiC_RecipeStack k in staleHolds)
            {
                Hold h;
                if (holds.TryGetValue(k, out h)) Drop(k, h, "the window was closed");
            }
        }

        // ------------------------------------------------------------------ C2b: before the window's Update ----

        /// <summary>
        /// C2b (the Update prefix: a slot with isInventoryFull, any slot while a hold is on record, a slot whose craft is due):
        ///  - Vanilla's stale "no room" retry on her craft is cleared (the class summary), logged at most every StaleLogSeconds.
        ///  - Each hold on record is refreshed while its slot updates (its window is open); stale ones are purged.
        ///  - Her craft, when due: a kept item is placed when it fits, and the craft counted off as vanilla's "no room" retry
        ///    counts it off (count - 1, the next craft's time, or the slot cleared); with nothing made yet, outputStack is let
        ///    through only with room for her next item. Otherwise her craft time is set just above 0 (HoldTick), so Update does
        ///    not call outputStack this frame, and a hold starts (one tooltip and log line).
        /// </summary>
        public static void BeforeUpdate(XUiC_RecipeStack rs)
        {
            if (rs == null) return;
            float now = Time.unscaledTime;
            if (holds.Count > 0 && now - lastPurge > HoldGapSeconds) Purge(now);
            Member m;
            XUiC_WorkstationWindowGroup wg;
            if (rs.isInventoryFull && MemberStack(rs, out m, out wg))
            {
                rs.isInventoryFull = false;
                staleFull++;
                Patches_Craft.Count("c2b-stale-full-cleared");
                if (now - lastStaleLog >= StaleLogSeconds || lastStaleLog <= 0f)
                {
                    lastStaleLog = now;
                    ScLog.Out(m.Name + "'s " + rs.recipe.GetName() + " in a station window had vanilla's \"no room\" retry left over from an earlier queue item"
                              + " - cleared, so it never goes to your inventory (" + staleFull + " this session; logged at most once a minute)");
                }
            }
            Hold h = null;
            if (holds.Count > 0 && holds.TryGetValue(rs, out h)) h.Seen = now;        // its window is open
            if (!rs.isCrafting || rs.recipe == null || rs.recipeCount <= 0 || rs.craftingTimeLeft > 0f) return;    // not due this frame
            if (!MemberStack(rs, out m, out wg))
            {
                if (h != null) Drop(rs, h, "its queue slot moved on to someone else's craft");
                return;
            }
            if (h != null && h.MemberId != m.Id)
            {
                Drop(rs, h, "its queue slot moved on to another member's craft");
                h = null;
            }
            XUiC_WorkstationOutputGrid grid = h != null && h.Grid != null ? h.Grid : wg.GetChildByType<XUiC_WorkstationOutputGrid>();
            if (h != null) h.Grid = grid;
            if (h != null && h.Pending != null)
            {
                if (!ReferenceEquals(h.PendingFor, rs.recipe))
                {
                    Drop(rs, h, "its queue slot moved on to another of her crafts");
                    return;                                       // the slot's own craft goes through the room test next frame
                }
                PlaceKept(rs, m, wg, h, grid);
                return;
            }
            ItemStack next;
            if (h != null && h.Next != null && ReferenceEquals(h.NextFor, rs.recipe)) next = h.Next;
            else
            {
                next = new ItemStack(new ItemValue(rs.recipe.itemValueType, rs.outputQuality, rs.outputQuality), rs.recipe.count);
                if (h != null)
                {
                    h.Next = next;
                    h.NextFor = rs.recipe;
                    h.Recipe = rs.recipe.GetName();
                }
            }
            if (HasRoom(grid, next))
            {
                dueStack = rs;                                    // outputStack makes it this frame (C2a)
                dueRecipe = rs.recipe;
                return;
            }
            rs.craftingTimeLeft = HoldTick;
            if (h == null)
            {
                HoldOn(rs, m, grid, null, null, null, null);
                if (holds.TryGetValue(rs, out h))
                {
                    h.Next = next;
                    h.NextFor = rs.recipe;
                }
            }
            else h.Left = rs.recipeCount;
        }

        /// <summary>Her kept item into the output when it fits; the craft counted off and listed for her.</summary>
        private static void PlaceKept(XUiC_RecipeStack rs, Member m, XUiC_WorkstationWindowGroup wg, Hold h, XUiC_WorkstationOutputGrid grid)
        {
            TileEntityWorkstation te = wg.WorkstationData != null ? wg.WorkstationData.TileEntity : null;
            if (grid == null || te == null || !TryPut(grid, h.Pending.itemValue, h.Pending.count))
            {
                rs.craftingTimeLeft = HoldTick;
                h.Left = rs.recipeCount;
                return;
            }
            Listed(te, m, h.Pending.itemValue, h.PendingName, h.PendingScrapped, h.PendingExp, h.Pending.count);
            try
            {
                // vanilla's own "no room" retry, once its item is out: count - 1, then the next craft's time or the slot cleared
                rs.recipeCount--;
                if (rs.recipeCount <= 0)
                {
                    rs.isCrafting = false;
                    if (rs.recipe != null || rs.craftingTimeLeft != 0f) rs.ClearRecipe();
                }
                else
                {
                    rs.craftingTimeLeft += rs.oneItemCraftTime;
                    if (rs.craftingTimeLeft <= 0f) rs.craftingTimeLeft = HoldTick;    // the next craft takes the room test next frame
                }
            }
            catch (Exception e)
            {
                ScLog.Once("c2-kept-count", e);
            }
            Made(rs, m, h.Pending.count, h.PendingArea, true);
        }

        /// <summary>Would the next item fit in the window's output (a stack it joins, or an empty slot)? No grid: no.</summary>
        private static bool HasRoom(XUiC_WorkstationOutputGrid grid, ItemStack next)
        {
            ItemStack[] slots = grid != null ? grid.GetSlots() : null;
            if (slots == null) return false;
            for (int i = 0; i < slots.Length; i++)
                if (slots[i] == null || slots[i].IsEmpty() || slots[i].CanStackWith(next)) return true;
            return false;
        }

        // ------------------------------------------------------------------ C2c: a station's list, crafter by crafter ----

        /// <summary>
        /// C2c (the AddCraftComplete prefix). True: listed here and vanilla's body skipped. Vanilla merges into the first entry of
        /// the same item (and scrapped item), whoever made it; when that entry is another crafter's and a member is on either
        /// side, the craft goes onto the crafter's own entry instead, or a new one. Everything else (no entry of the item yet,
        /// the crafter's own entry first, two players) is vanilla's.
        /// </summary>
        public static bool KeepApart(TileEntityWorkstation te, int crafter, ItemValue item, string recipeName, string scrapped, int exp, int count)
        {
            List<CraftCompleteData> list = te != null ? te.CraftCompleteList : null;
            if (list == null || list.Count == 0 || item == null) return false;
            int id = item.GetItemId();
            int first = -1;
            for (int i = 0; i < list.Count; i++)
                if (Same(list[i], id, scrapped)) { first = i; break; }
            if (first < 0 || list[first].CrafterEntityID == crafter) return false;
            int other = list[first].CrafterEntityID;
            if (ScRegistry.GetMember(crafter) == null && ScRegistry.GetMember(other) == null) return false;
            int own = -1;
            for (int i = first + 1; i < list.Count; i++)
                if (Same(list[i], id, scrapped) && list[i].CrafterEntityID == crafter) { own = i; break; }
            if (own >= 0) list[own].CraftedItemStack.count += count;
            else list.Add(new CraftCompleteData(crafter, new ItemStack(item, count), recipeName, scrapped, exp, 1));
            // listed: from here on true whatever happens (false would let vanilla list the same craft a second time)
            try
            {
                te.SetModified();
                keptApart++;
                Patches_Craft.Count("c2c-craft-complete-kept-apart");
                if (ScConfig.Verbose)
                    ScLog.Verbose(count + " x " + recipeName + " made by " + Who(crafter, null) + " listed apart from " + Who(other, null)
                                  + "'s at a station (vanilla would have merged them, paying one for the other's)");
            }
            catch (Exception e)
            {
                ScLog.Once("c2-apart-after", e);
            }
            return true;
        }

        private static bool Same(CraftCompleteData d, int itemId, string scrapped)
        {
            return d != null && d.CraftedItemStack != null && d.CraftedItemStack.itemValue != null
                   && d.CraftedItemStack.itemValue.GetItemId() == itemId && d.ItemScrapped == scrapped;
        }

        // ------------------------------------------------------------------ console ----

        /// <summary>sc craft's one-line summary of round c2's hooks.</summary>
        public static string Summary()
        {
            return "member crafts made in your open station window " + crafts + " (" + items + " item(s)), holds for a full output " + holdsStarted
                   + " (" + holdsEnded + " carried on in the window), made items kept for room " + keptItems + " (" + keptPlaced + " placed, " + requeued
                   + " left in the queue when the window closed), another mod's extra output with no room " + extrasNoRoom
                   + ", stale \"no room\" retries cleared " + staleFull + ", station list entries kept apart " + keptApart;
        }

        private static string Who(int id, EntityPlayerLocal p)
        {
            if (p == null) p = ScCommunity.LocalPlayer;
            if (p != null && id == p.entityId) return "you";
            Member m = ScRegistry.GetMember(id);
            if (m != null) return m.Name + " [" + id + "]";
            World w = ScCommunity.World;
            Entity e = w != null ? w.GetEntity(id) : null;
            if (e is EntityPlayer) return "player " + ((EntityPlayer)e).EntityName + " [" + id + "]";
            return e is EntityAlive ? ((EntityAlive)e).EntityName + " [" + id + "] (not a member)" : "#" + id + " (not loaded, not a member)";
        }

        /// <summary>
        /// sc craft station [radius] (read-only): the nearest workstation within radius m (8) of the player - its queue (whose
        /// craft each is, and whether the next one has room in the output), its output, its list of finished crafts not yet
        /// paid (whose, and who pays them), the window holds now, and the player's own craft counters (vanilla's and Survivor
        /// Origins'), which a member's crafts never move.
        /// </summary>
        public static void ConsoleStation(List<string> p, Action<string> output)
        {
            float radius = 8f;
            if (p.Count >= 3 && !float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out radius)) radius = 8f;
            radius = Mathf.Clamp(radius, 1f, 64f);
            World w = ScCommunity.World;
            EntityPlayerLocal pl = ScCommunity.LocalPlayer;
            if (w == null || pl == null) { output("[SC] no world or local player"); return; }
            TileEntityWorkstation te;
            float dist;
            if (!Nearest(w, pl.position, radius, out te, out dist))
            {
                output("[SC] no workstation within " + radius.ToString("0", CultureInfo.InvariantCulture) + " m of you (sc craft station <radius>)");
                return;
            }
            Vector3i pos = te.ToWorldPos();
            BlockValue bv = w.GetBlock(pos);
            output("[SC] " + (bv.Block != null ? bv.Block.GetBlockName() : "workstation") + " at " + pos + ", " + dist.ToString("0.0", CultureInfo.InvariantCulture)
                   + " m: " + (te.IsBurning ? "burning" : "not burning") + ", "
                   + (te.IsUserAccessing() ? "a window is OPEN on it (the window's queue and output are the live ones; below is the station's copy, written back when it closes)" : "no window open"));
            var recipes = new List<string>();
            var itemNames = new List<string>();
            RecipeQueueItem[] q = te.Queue;
            int shown = 0;
            for (int i = q != null ? q.Length - 1 : -1; i >= 0; i--)
            {
                RecipeQueueItem it = q[i];
                if (it == null || it.Recipe == null || it.Multiplier <= 0) continue;
                string rn = it.Recipe.GetName();
                if (!recipes.Contains(rn)) recipes.Add(rn);
                AddItemName(itemNames, it.Recipe.itemValueType);
                output("[SC]   queue " + (q.Length - i) + ": " + rn + " x" + it.Multiplier + " (" + it.Recipe.count + " each) for " + Who(it.StartingEntityId, pl) + ", "
                       + it.OneItemCraftTime.ToString("0.0", CultureInfo.InvariantCulture) + " s a craft"
                       + (i == q.Length - 1 ? ", " + Mathf.Max(0f, it.CraftingTimeLeft).ToString("0.0", CultureInfo.InvariantCulture) + " s left on this one - " + RoomFor(te, it) : ""));
                shown++;
            }
            if (shown == 0) output("[SC]   queue: empty");
            ItemStack[] o = te.Output;
            int used = 0;
            var outParts = new List<string>();
            for (int i = 0; o != null && i < o.Length; i++)
            {
                if (o[i] == null || o[i].IsEmpty()) continue;
                used++;
                ItemClass ic = o[i].itemValue.ItemClass;
                outParts.Add((ic != null ? ic.GetItemName() : "#" + o[i].itemValue.type) + " x" + o[i].count);
            }
            output("[SC]   output: " + used + " of " + (o != null ? o.Length : 0) + " slot(s) used" + (outParts.Count > 0 ? " - " + string.Join(", ", outParts.ToArray()) : ""));
            List<CraftCompleteData> list = te.CraftCompleteList;
            if (list == null || list.Count == 0) output("[SC]   finished crafts not yet paid: none");
            else
            {
                foreach (CraftCompleteData d in list)
                {
                    if (d == null || d.CraftedItemStack == null) continue;
                    if (!recipes.Contains(d.RecipeName)) recipes.Add(d.RecipeName);
                    if (d.CraftedItemStack.itemValue != null) AddItemName(itemNames, d.CraftedItemStack.itemValue.type);
                    Member m = ScRegistry.GetMember(d.CrafterEntityID);
                    string payer = d.CrafterEntityID == pl.entityId ? "paid to you when you next open the station (vanilla)"
                                 : m != null ? "paid to " + m.Name + " by 0-XNPCCore's next station tick (C1d) or when you next open the station (C1e)"
                                 : "vanilla's (paid to that crafter when they open the station)";
                    output("[SC]   finished, not yet paid: " + d.CraftedItemStack.count + " x " + d.RecipeName + " by " + Who(d.CrafterEntityID, pl) + " - " + payer);
                }
            }
            float now = Time.unscaledTime;
            int live = 0;
            foreach (var kv in holds)
            {
                Hold h = kv.Value;
                live++;
                output("[SC]   holding in your window: " + h.Member + "'s " + h.Left + " x " + h.Recipe + " for "
                       + (now - h.Since).ToString("0", CultureInfo.InvariantCulture) + " s, the output full - "
                       + (h.Pending != null ? "one craft made, its " + h.PendingName + " x" + h.Pending.count + " kept until it fits" : "the next craft not made yet")
                       + (now - h.Seen > HoldGapSeconds ? " (not seen for " + (now - h.Seen).ToString("0", CultureInfo.InvariantCulture) + " s: its window is closed, dropped at the next update)" : ""));
            }
            if (live == 0) output("[SC]   holding in your window: nothing");
            output("[SC]   your own craft counters (a member's crafts never move them): " + PlayerCounters(pl, recipes, itemNames));
            output("[SC]   round c2 this session: " + Summary());
        }

        private static void AddItemName(List<string> names, int type)
        {
            ItemClass ic = ItemClass.GetForId(type);
            string n = ic != null ? ic.GetItemName() : null;
            if (!string.IsNullOrEmpty(n) && !names.Contains(n)) names.Add(n);
        }

        /// <summary>Is there room in the output for the next craft of the station's top item (as HandleRecipeQueue builds it)?</summary>
        private static string RoomFor(TileEntityWorkstation te, RecipeQueueItem it)
        {
            ItemStack[] o = te.Output;
            if (o == null) return "no output";
            ItemClass ic = ItemClass.GetForId(it.Recipe.itemValueType);
            ItemValue iv = ic != null && ic.HasQuality ? new ItemValue(it.Recipe.itemValueType, it.Quality, it.Quality) : new ItemValue(it.Recipe.itemValueType);
            ItemStack next = new ItemStack(iv, it.Recipe.count);
            for (int i = 0; i < o.Length; i++)
                if (o[i] == null || o[i].IsEmpty() || o[i].CanStackWith(next)) return "the output has room for the next one";
            return "NO room in the output for the next one: the queue waits until some is taken out";
        }

        /// <summary>Vanilla's totalItemsCrafted and _craftCount_ per recipe, Survivor Origins' $q_ per item and its waiting $xp_ pools.</summary>
        private static string PlayerCounters(EntityPlayerLocal pl, List<string> recipes, List<string> itemNames)
        {
            var sb = new StringBuilder();
            sb.Append("items crafted ").Append(pl.totalItemsCrafted);
            if (pl.Buffs == null) return sb.ToString();
            foreach (string r in recipes)
                sb.Append("; _craftCount_").Append(r).Append('=').Append(pl.Buffs.GetCustomVar("_craftCount_" + r).ToString("0", CultureInfo.InvariantCulture));
            foreach (string n in itemNames)
                sb.Append("; $q_").Append(n).Append('=').Append(pl.Buffs.GetCustomVar("$q_" + n).ToString("0", CultureInfo.InvariantCulture));
            var pools = new List<string>();
            if (pl.Buffs.CVars != null)
                foreach (var kv in pl.Buffs.CVars)
                    if (kv.Key != null && kv.Key.StartsWith("$xp_", StringComparison.OrdinalIgnoreCase) && Math.Abs(kv.Value) > 0.0001f)
                        pools.Add(kv.Key + "=" + kv.Value.ToString("0.##", CultureInfo.InvariantCulture));
            sb.Append("; Survivor Origins XP waiting for its tick: ").Append(pools.Count > 0 ? string.Join(" ", pools.ToArray()) : "none");
            return sb.ToString();
        }

        private static bool Nearest(World w, Vector3 from, float radius, out TileEntityWorkstation best, out float bestD)
        {
            best = null;
            bestD = float.MaxValue;
            int r = Mathf.CeilToInt(radius);
            int bx = Mathf.FloorToInt(from.x), bz = Mathf.FloorToInt(from.z);
            for (int cx = World.toChunkXZ(bx - r); cx <= World.toChunkXZ(bx + r); cx++)
            {
                for (int cz = World.toChunkXZ(bz - r); cz <= World.toChunkXZ(bz + r); cz++)
                {
                    Chunk ch = w.GetChunkSync(cx, cz) as Chunk;
                    var tes = ch != null ? ch.tileEntities : null;
                    if (tes == null || tes.list == null) continue;
                    for (int i = 0; i < tes.list.Count; i++)
                    {
                        TileEntityWorkstation ws = tes.list[i] as TileEntityWorkstation;
                        if (ws == null) continue;
                        float d = Vector3.Distance(from, ws.ToWorldPos().ToVector3() + new Vector3(0.5f, 0.5f, 0.5f));
                        if (d > radius || d >= bestD) continue;
                        best = ws;
                        bestD = d;
                    }
                }
            }
            return best != null;
        }
    }
}
