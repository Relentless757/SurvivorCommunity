using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Linked storage as a loot container - the game 3.4 edition (compiled only by the ScGame=34 build, in place of
    /// ScLinkedContainer.cs; SurvivorCommunity.csproj). The same view and the same behaviour as 3.2's: one virtual container
    /// whose slots are the merged stacks of every player box in the zone (ScStorage.Slots), opened in vanilla's own loot
    /// window, where every change is written straight into the real box slot behind the cell and a change on a cell with no
    /// box behind it deposits into the first box with room.
    ///
    /// Why it is built differently on 3.4. The loot window no longer takes an interface: ITileEntityLootable is gone and
    /// XUiC_LootWindowGroup.OpenLooting, XUiC_LootWindow and XUiC_LootContainer bind a TEFeatureStorage - a feature of a
    /// TileEntityComposite - whose UpdateSlot, AddItem and TryStackItem are not virtual, and whose slots are an
    /// ItemStackGrid of ItemStacks bound to the grid (a write to one raises the grid's SlotChanged). So:
    ///  - the class derives from SCore 3.4's SCoreLootContainer: a TEFeatureStorage on a detached composite that is never
    ///    registered with the world and has its modified check disabled (so SetModified sends nothing - 3.2's no-op
    ///    setModified), with an empty feature table (the window's GetSelfOrFeature lookups answer "none"). It borrows the
    ///    anchor's chunk and local position, so ToWorldPos and blockValue resolve: the window's 32 m auto-close measures from
    ///    there, and its close path reads both;
    ///  - the write-through moved from 3.2's UpdateSlot / AddItem / TryStackItem overrides to the view grid's SlotChanged
    ///    (OnViewSlotChanged): an idempotent mirror, "the box slot behind cell i := cell i", so every writer - the window's
    ///    drag and drop, take and half, swap, its sort, move-all, Grid Inventory's grid - lands in the boxes whatever path it
    ///    takes, however many change events one write raises. SC's own writes to the view (Rebuild, a refused cell put back)
    ///    are made with the mirror off (quiet), so they are never written back;
    ///  - TEFeatureStorage.AddItem and TryStackItem (shift-click from the backpack, the backpack's move-all) are prefixed for
    ///    this class alone (Patch_LinkedAddItem / Patch_LinkedTryStackItem) to run 3.2's own logic: a deposit goes into a box
    ///    first and only then shows in a cell, and when no box has room nothing moves and the player is told - instead of
    ///    the cell taking it and the mirror handing it back;
    ///  - no slot locks (3.2's HasSlotLocksSupport false): a lock on a cell of a view rebuilt on every open means nothing.
    ///
    /// Layout as on 3.2 (Rebuild): ONE grid, live stacks first, then a band of empty cells for the boxes' free room; capped
    /// by Grid Inventory's widget pool (StackCap, ShowLimit) when it draws the view (ScStorage.SizeFor).
    /// Host only (ScStorage.OpenWindow refuses elsewhere).
    /// </summary>
    public class ScLinkedContainer : SCoreLootContainer
    {
        public const int Cols = 9;
        /// <summary>A loot container defined in Config/loot.xml: vanilla looks lootListName up on open and close (and SCore
        /// closes a loot window whose container has no loot list name).</summary>
        public const string LootListKey = "scLinkedStorage";

        public int RowsCap = 10;
        /// <summary>Most stacks laid out (0 = no cap): Grid Inventory's item widgets, less two rows for drops.</summary>
        public int StackCap;
        /// <summary>Whether Grid Inventory draws this view (set by ScStorage.SizeFor).</summary>
        public bool GridDrawn;
        /// <summary>Slots that can be drawn: Grid Inventory's item widgets (slot i needs widget i). A stack may
        /// only enter the view below it; past it a drop is refused rather than stored out of sight.</summary>
        public int ShowLimit = int.MaxValue;
        /// <summary>Empty rows laid out under the stacks when Grid Inventory draws the view.</summary>
        public const int HeadroomRows = 12;

        /// <summary>The box slot behind each cell (null: none - an empty cell, or one a deposit has not reached yet).</summary>
        private ScStorage.Slot[] map = new ScStorage.Slot[0];

        /// <summary>The mirror is off: SC itself is writing the view (Rebuild, a cell put back).</summary>
        private bool quiet;

        public ScLinkedContainer(Chunk _chunk, Vector3i _localPos) : base(_chunk)
        {
            localChunkPos = _localPos;
            EntityId = -1;
            bPlayerStorage = true;          // the grid's PlayerOwned: no looting XP, no loot respawn, no timer
            bTouched = true;
            lootListName = LootListKey;
            ItemGrid.SetSlotLocks(null);
            ItemGrid.SlotChanged += OnViewSlotChanged;
        }

        // ---- layout ----

        /// <summary>Cells the grid may hold: Cols x RowsCap (up to 1000 rows under Grid Inventory, 10 on vanilla's grid).</summary>
        private int MaxCells { get { return Cols * Math.Max(1, RowsCap); } }

        /// <summary>Stacks that did not fit the grid on the last Rebuild (shown in the strip).</summary>
        public int Hidden { get; private set; }

        /// <summary>Free room in the boxes not laid out as empty cells on the last Rebuild (logged).</summary>
        public int HiddenCells { get; private set; }

        /// <summary>
        /// Lays the view out as ONE grid: the live stacks in order, then empty cells for the boxes' free room (the real room
        /// to deposit into), capped at MaxCells - 3.2's layout, sized as there by the stacks' area (the bridge's StackArea),
        /// but on 3.4 a stack is hidden only past MaxCells STACKS: RE Inventory 3.3+ has a cell per slot, so slots = the
        /// stacks' area + the free band gives its grid that many free cells to drop a rifle into, and past MaxCells it
        /// grows the view's rows itself. The grid is resized and filled with the mirror off and no change events (SetItems
        /// without firing), so nothing is written back to the boxes.
        /// </summary>
        public void Rebuild()
        {
            int cap = MaxCells;
            var live = new System.Collections.Generic.List<ScStorage.Slot>();
            int usedArea = 0;
            Hidden = 0;
            for (int i = 0; i < ScStorage.Slots.Count; i++)
            {
                ScStorage.Slot s = ScStorage.Slots[i];
                if (s.Stack.IsEmpty()) continue;
                int a = GridDrawn ? Math.Min(ScStorage.AreaOf(s.Stack), cap) : 1;   // vanilla: one stack, one slot
                // 3.4: hidden by STACKS only. RE Inventory 3.3+ draws a cell per slot and grows the view's rows for
                // footprints past its cells, so a stack costs the view one slot; the area only sizes it (below), so a
                // rifle (ten cells) no longer hides stacks past the view's slots (3.2's rule did, at 9,000 cells)
                if ((StackCap > 0 && live.Count >= StackCap) || live.Count >= cap) { Hidden++; continue; }
                live.Add(s);
                usedArea += a;
            }
            int free = ScStorage.FreeFor(GridDrawn);
            int shown = Math.Min(free, cap - usedArea);
            if (GridDrawn) shown = Math.Min(shown, HeadroomRows * Cols);
            shown = Math.Max(0, shown);
            HiddenCells = free - shown;
            int cells = Mathf.Clamp(usedArea + shown, 1, cap);
            int rows = Mathf.Clamp((cells + Cols - 1) / Cols, 1, Math.Max(1, RowsCap));
            int total = Cols * rows;
            var stacks = new ItemStack[total];
            map = new ScStorage.Slot[total];
            for (int i = 0; i < total; i++)
            {
                if (i < live.Count) { map[i] = live[i]; stacks[i] = live[i].Stack.Clone(); }
                else { map[i] = null; stacks[i] = ItemStack.Empty.Clone(); }
            }
            quiet = true;
            try
            {
                SetContainerSize(new Vector2i(Cols, rows), false);
                ItemGrid.SetItems(stacks, false);
                ItemGrid.SetSlotLocks(null);
            }
            finally
            {
                quiet = false;
            }
        }

        private ScStorage.Slot MapAt(int _idx)
        {
            return _idx >= 0 && _idx < map.Length ? map[_idx] : null;
        }

        /// <summary>Sets the box slot behind a cell; a cell past the map (the grid grown by somebody else) grows it.</summary>
        private void SetMap(int _idx, ScStorage.Slot _s)
        {
            if (_idx < 0) return;
            if (_idx >= map.Length) Array.Resize(ref map, Math.Max(_idx + 1, ItemGrid.Length));
            map[_idx] = _s;
        }

        // ---- the mirror ----

        /// <summary>
        /// A cell changed (the grid's SlotChanged): the box slot behind it gets the cell's content - idempotent, so the two
        /// or three events one write can raise all leave the same result. A cell with no box behind it that now holds
        /// something is a deposit: into the first box with room, and the cell is mapped to that slot from then on. A cell
        /// whose box vanished, or a deposit nothing takes, is put back empty and the item goes back to the player (see
        /// Refuse). Any exception is caught: this runs inside the window's own write.
        /// </summary>
        private void OnViewSlotChanged(int _idx)
        {
            if (quiet) return;
            try
            {
                if (_idx < 0 || _idx >= ItemGrid.Length) return;
                ItemStack v = ItemGrid[_idx];
                ScStorage.Slot s = MapAt(_idx);
                if (s != null)
                {
                    if (!ScStorage.WriteThrough(s, v))
                    {
                        // the box vanished under us: the item stays with the player rather than in limbo
                        ItemStack lost = v.Clone();
                        SetMap(_idx, null);
                        PutBack(_idx);
                        if (!lost.IsEmpty()) ReturnToPlayer(lost);
                        Notify();
                        return;
                    }
                    if (v.IsEmpty()) SetMap(_idx, null);
                    return;
                }
                // no box behind this cell: a deposit
                if (v.IsEmpty()) return;
                if (_idx >= ShowLimit)
                {
                    // Grid Inventory has no widget for this slot: stored here the item would vanish from the view
                    Refuse(_idx, "scStorageViewFull");
                    return;
                }
                ScStorage.Slot placed;
                if (ScStorage.Deposit(v, out placed)) { SetMap(_idx, placed); return; }
                Refuse(_idx, "scStorageFull");
            }
            catch (Exception e)
            {
                ScLog.Once("linked-slotchanged", e);
            }
        }

        /// <summary>Empties a cell with the mirror off (nothing is written to a box).</summary>
        private void PutBack(int _idx)
        {
            quiet = true;
            try { ItemGrid.SetItem(_idx, null); }
            finally { quiet = false; }
        }

        private void Notify()
        {
            try { if (Parent != null) Parent.NotifyListeners(); }
            catch (Exception e) { ScLog.Once("linked-notify", e); }
        }

        /// <summary>
        /// A drop nothing takes: the cell is put back empty and the item comes back to the player. Vanilla's drop has moved
        /// the cell's old content (nothing) onto the cursor before it writes the cell (XUiC_ItemStack.SwapItem), so the item
        /// is no longer on the cursor; a grid that still holds it there when it writes (Grid Inventory's, on 3.2) keeps it,
        /// and returning it as well would double it (StillOnCursor). That test only runs when Grid Inventory draws the view:
        /// vanilla's drop-one takes the item off the cursor first too (HandleDropOne), and a cursor left with as many as
        /// were dropped (2 -> 1) would read as "still on the cursor" and the one dropped would be lost. RE Inventory 3.3+
        /// takes nothing off the cursor until its write is confirmed (writeSlot), drop-one included: one written out of a
        /// held stack of N leaves all N on the cursor. Its writes are the whole held stack or exactly one, so on 3.4 "still
        /// on the cursor" is the same count, or one written from a held stack of that item - not "at least as many": a
        /// sort that moves a stack into an unmapped cell with every box full would then keep, say, 5 nails off the
        /// player because the cursor holds 20.
        /// </summary>
        private void Refuse(int _idx, string why)
        {
            ItemStack v = ItemGrid[_idx].Clone();
            if (ScConfig.Verbose)
                ScLog.Verbose("linked storage: cell " + _idx + " of " + ItemGrid.Length + " refused (" + why + ") for "
                              + (v.itemValue != null && v.itemValue.ItemClass != null ? v.itemValue.ItemClass.GetItemName() : "?") + " x" + v.count);
            SetMap(_idx, null);
            PutBack(_idx);
            if (!(GridDrawn && StillOnCursor(v))) ReturnToPlayer(v);
            Notify();
            EntityPlayerLocal p = ScCommunity.LocalPlayer;
            if (p != null) ScCommunity.Tell(p, Localization.Get(why), "ui_denied");
        }

        private static bool StillOnCursor(ItemStack v)
        {
            try
            {
                EntityPlayerLocal p = ScCommunity.LocalPlayer;
                XUi xui = (p != null && p.PlayerUI != null) ? p.PlayerUI.xui : null;
                ItemStack cur = (xui != null && xui.DragAndDropWindow != null) ? xui.DragAndDropWindow.CurrentStack : null;
                return cur != null && !cur.IsEmpty() && v != null && !v.IsEmpty()
                       && cur.itemValue != null && v.itemValue != null
                       && cur.itemValue.type == v.itemValue.type && (cur.count == v.count || v.count == 1);
            }
            catch (Exception) { return false; }
        }

        private static void ReturnToPlayer(ItemStack v)
        {
            try
            {
                EntityPlayerLocal p = ScCommunity.LocalPlayer;
                if (p == null || v == null || v.IsEmpty()) return;
                XUiM_PlayerInventory inv = (p.PlayerUI != null && p.PlayerUI.xui != null) ? p.PlayerUI.xui.PlayerInventory : null;
                if (inv != null && inv.AddItem(v.Clone())) return;
                GameManager.Instance.ItemDropServer(v.Clone(), p.GetDropPosition(), Vector3.zero, -1, 60f);
            }
            catch (Exception e)
            {
                ScLog.Once("linked-return", e);
            }
        }

        // ---- AddItem / TryStackItem (3.2's IInventory logic, reached through the prefixes below) ----

        /// <summary>
        /// Shift-click and move-all from the backpack: into a box first, then into the lowest free cell the view can draw.
        /// False - with the reason told - when no cell is free below ShowLimit or no box has room: nothing moves.
        /// </summary>
        internal bool AddItemLinked(ItemStack _itemStack)
        {
            try
            {
                if (_itemStack == null || _itemStack.IsEmpty()) return false;
                // a cell the view can draw first: past ShowLimit the item would be stored out of sight
                int at = -1;
                int limit = Math.Min(ItemGrid.Length, ShowLimit);
                for (int i = 0; i < limit; i++)
                {
                    if (MapAt(i) == null && ItemGrid[i].IsEmpty()) { at = i; break; }
                }
                if (at < 0)
                {
                    EntityPlayerLocal p = ScCommunity.LocalPlayer;
                    if (p != null) ScCommunity.Tell(p, Localization.Get(ScStorage.FreeFor(GridDrawn) > 0 ? "scStorageViewFull" : "scStorageFull"), "ui_denied");
                    return false;
                }
                ScStorage.Slot placed;
                if (!ScStorage.Deposit(_itemStack, out placed))
                {
                    // 3.4: say so, as a drop on a cell does (Refuse). With RE Inventory each box takes an item only where its
                    // own grid fits the footprint, so a free cell in the view no longer means a box will take a rifle, and a
                    // shift-click that silently did nothing read as broken.
                    EntityPlayerLocal p = ScCommunity.LocalPlayer;
                    if (p != null) ScCommunity.Tell(p, Localization.Get("scStorageFull"), "ui_denied");
                    return false;
                }
                quiet = true;
                try { ItemGrid.SetItem(at, _itemStack); }
                finally { quiet = false; }
                SetMap(at, placed);
                // the item is in a box now: a failing redraw must not report the add as failed (the caller would keep its
                // copy too)
                Notify();
                return true;
            }
            catch (Exception e)
            {
                ScLog.Once("linked-additem", e);
                return false;
            }
        }

        /// <summary>Merges into the view's stacks of the same item, each written through to its box first.</summary>
        internal (bool anyMoved, bool allMoved) TryStackItemLinked(int _startIndex, ItemStack _itemStack)
        {
            bool any = false;
            try
            {
                if (_itemStack == null || _itemStack.IsEmpty()) return (false, false);
                for (int i = Math.Max(0, _startIndex); i < ItemGrid.Length && _itemStack.count > 0; i++)
                {
                    ItemStack s = ItemGrid[i];
                    ScStorage.Slot box = MapAt(i);
                    if (s == null || s.IsEmpty() || box == null) continue;
                    if (s.itemValue.type != _itemStack.itemValue.type) continue;
                    int n = _itemStack.count;
                    if (!s.CanStackPartly(ref n) || n <= 0) continue;
                    ItemStack merged = s.Clone();
                    merged.count += n;
                    if (!ScStorage.WriteThrough(box, merged)) continue;
                    quiet = true;
                    try { ItemGrid.SetItem(i, merged); }
                    finally { quiet = false; }
                    _itemStack.count -= n;
                    any = true;
                }
                if (any) Notify();
            }
            catch (Exception e)
            {
                ScLog.Once("linked-trystack", e);
            }
            return (any, _itemStack != null && _itemStack.count == 0);
        }

        // ---- the two prefixes ----

        /// <summary>
        /// TEFeatureStorage.AddItem(ItemStack) for linked storage only: AddItemLinked instead of the cell-first original.
        /// Every other container runs vanilla's. It runs LAST and decides alone (Priority.Last). The game's Harmony calls
        /// every prefix in order and ANDs their answers, and `__result` is one shared local, so the last writer wins. The
        /// other prefixes judge the VIEW as if it were a box: RE Inventory 3.3+'s room gate packs the view's footprints
        /// (GridSpace.ContainerHasRoomFor), and SCore's slot-tag prefix asks about the view's own block. Round 3 ran first
        /// (SC is patched before RE, same priority). It deposited, then RE's gate on the fuller view set `__result` false,
        /// so the backpack kept the rifle the crate now held: a duplicate per shift-click (user's test, 2026-10-30:
        /// `... into cntWoodWritableCrate ..., slot 18`, then `[GridInv] no grid space for gunRifleT3SniperRifle (5x2);
        /// refused`). The boxes are what take the item, and AddItemLinked asks them; nothing writes `__result` after it.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_LinkedAddItem
        {
            public static MethodBase Target()
            {
                return AccessTools.DeclaredMethod(typeof(TEFeatureStorage), "AddItem", new[] { typeof(ItemStack) });
            }

            [HarmonyPrepare]
            public static bool Prepare()
            {
                try
                {
                    if (Target() != null) return true;
                    ScLog.Err("TEFeatureStorage.AddItem(ItemStack) not found (game update?) - a shift-click into linked storage with no room moves the item and hands it back");
                }
                catch (Exception e) { ScLog.Once("linked-bind-additem", e); }
                return false;
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return Target(); }

            [HarmonyPrefix]
            [HarmonyPriority(Priority.Last)]
            public static bool Prefix(TEFeatureStorage __instance, ItemStack _itemStack, ref bool __result)
            {
                ScLinkedContainer c = __instance as ScLinkedContainer;
                if (c == null) return true;
                __result = c.AddItemLinked(_itemStack);
                return false;
            }
        }

        /// <summary>TEFeatureStorage.TryStackItem(int, ItemStack) for linked storage only: TryStackItemLinked - last and
        /// alone, as Patch_LinkedAddItem (a merge into a view stack is written into its box).</summary>
        [HarmonyPatch]
        public static class Patch_LinkedTryStackItem
        {
            public static MethodBase Target()
            {
                return AccessTools.DeclaredMethod(typeof(TEFeatureStorage), "TryStackItem", new[] { typeof(int), typeof(ItemStack) });
            }

            [HarmonyPrepare]
            public static bool Prepare()
            {
                try
                {
                    if (Target() != null) return true;
                    ScLog.Err("TEFeatureStorage.TryStackItem(int, ItemStack) not found (game update?) - a shift-click merge into linked storage goes through the mirror only");
                }
                catch (Exception e) { ScLog.Once("linked-bind-trystack", e); }
                return false;
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return Target(); }

            [HarmonyPrefix]
            [HarmonyPriority(Priority.Last)]
            public static bool Prefix(TEFeatureStorage __instance, int _startIndex, ItemStack _itemStack, ref (bool anyMoved, bool allMoved) __result)
            {
                ScLinkedContainer c = __instance as ScLinkedContainer;
                if (c == null) return true;
                __result = c.TryStackItemLinked(_startIndex, _itemStack);
                return false;
            }
        }
    }
}
