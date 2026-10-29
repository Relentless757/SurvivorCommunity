using System;
using UnityEngine;

namespace SurvivorCommunity
{
    // Game 3.2 only: the ScGame=34 build compiles ScLinkedContainer34.cs in place of this file (SurvivorCommunity.csproj).
    /// <summary>
    /// Linked storage as a loot container: one virtual ITileEntityLootable whose slots are the merged
    /// stacks of every player box in the zone (ScStorage.Slots), opened in vanilla's own loot window
    /// (SCore's EntityUtilities.OpenContainer, the path NPC inventories use). Every change the window
    /// reports - take, half, swap, merge, shift-move, move-all - arrives as UpdateSlot / AddItem /
    /// TryStackItem and is written straight into the real box slot behind the cell; a change on a cell with
    /// no box behind it deposits into the first box with room. Because it lives in vanilla's window, drag
    /// and drop, the backpack's transfer buttons, the left panel and GridInventory's takeover all work.
    ///
    /// Shape mirrors SCore's SCoreLootContainer (TileEntity subclass, no world registration). It borrows
    /// the home land claim's chunk and local position so ToWorldPos / blockValue resolve (vanilla's close
    /// path reads both); setModified is a no-op so the phantom never flags the chunk or sends packets.
    /// One grid (ScStorage.SizeFor): vanilla's grid shows 10 rows. Grid Inventory scrolls and draws a widget
    /// only for a slot that holds something, but slot i can only be drawn by widget i, so the stacks are
    /// capped by its widget pool (StackCap, ShowLimit) and below them sits a band of empty rows to drop into
    /// (HeadroomRows) rather than every free cell of every box: its layout cost grows with the square of the
    /// slot count. The strip's header gives the boxes' whole free room.
    /// </summary>
    public class ScLinkedContainer : TileEntity, ITileEntityLootable, IInventory
    {
        public const int Cols = 9;
        /// <summary>A loot container defined in Config/loot.xml: vanilla looks lootListName up on open and close.</summary>
        public const string LootListKey = "scLinkedStorage";

        public int EntityId { get; set; }
        public string lootListName { get; set; }
        public float LootStageMod { get { return 0f; } }
        public float LootStageBonus { get { return 0f; } }
        public bool bPlayerStorage { get; set; }
        public PreferenceTracker preferences { get; set; }
        public bool bTouched { get; set; }
        public ulong worldTimeTouched { get; set; }
        public bool bWasTouched { get; set; }
        public ItemStack[] items { get; set; }
        public bool HasSlotLocksSupport { get { return false; } }
        public PackedBoolArray SlotLocks { get; set; }

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
        private Vector2i size = new Vector2i(Cols, 1);
        private ScStorage.Slot[] map = new ScStorage.Slot[0];

        public ScLinkedContainer(Chunk _chunk, Vector3i _localPos) : base(_chunk)
        {
            localChunkPos = _localPos;
            EntityId = -1;
            bPlayerStorage = true;
            bTouched = true;
            bWasTouched = true;
            lootListName = LootListKey;
            items = new ItemStack[0];
        }

        public override TileEntityType GetTileEntityType() { return TileEntityType.None; }

        /// <summary>A phantom: never marks the chunk modified nor sends tile-entity packets.</summary>
        public override void setModified() { }

        // ---- layout ----

        /// <summary>Cells the grid may hold: Cols x RowsCap (up to 1000 rows under Grid Inventory, 10 on vanilla's grid).</summary>
        private int MaxCells { get { return Cols * Math.Max(1, RowsCap); } }

        /// <summary>Stacks that did not fit the grid on the last Rebuild (shown in the strip).</summary>
        public int Hidden { get; private set; }

        /// <summary>Free room in the boxes not laid out as empty cells on the last Rebuild (logged).</summary>
        public int HiddenCells { get; private set; }

        /// <summary>
        /// Lays the view out as ONE grid: the live stacks in order, then empty cells for the boxes' free room
        /// (the real room to deposit into), capped at MaxCells. Under Grid Inventory that is cells, not stacks
        /// (a rifle is eight of them, ScStorage.AreaOf) and the empty part is a band of HeadroomRows rows;
        /// vanilla's grid is one stack per slot and shows every free slot that fits. Stacks past StackCap or
        /// the cap are counted in Hidden. The live stacks come first, so they hold the lowest slot indices -
        /// the ones Grid Inventory has widgets for - and a drop lands in the lowest free index after them. No
        /// paging - re-binding the slots under GridInventory's takeover broke its scrolling.
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
                if ((StackCap > 0 && live.Count >= StackCap) || usedArea + a > cap) { Hidden++; continue; }
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
            size = new Vector2i(Cols, rows);
            int total = Cols * rows;
            items = new ItemStack[total];
            map = new ScStorage.Slot[total];
            for (int i = 0; i < total; i++)
            {
                if (i < live.Count) { map[i] = live[i]; items[i] = live[i].Stack.Clone(); }
                else { map[i] = null; items[i] = ItemStack.Empty.Clone(); }
            }
        }

        // ---- ITileEntityLootable ----

        public Vector2i GetContainerSize() { return size; }

        /// <summary>The window sizes itself from the boxes; a resize request (GridInventory scaling) is ignored.</summary>
        public void SetContainerSize(Vector2i _containerSize, bool _clearItems = true)
        {
            if (_containerSize != size) ScLog.Verbose("linked storage: resize to " + _containerSize + " ignored (the grid is laid out from the boxes)");
        }

        public void UpdateSlot(int _idx, ItemStack _item)
        {
            try
            {
                if (items == null || _idx < 0 || _idx >= items.Length) return;
                ItemStack v = (_item == null) ? ItemStack.Empty.Clone() : _item.Clone();
                // No claiming a box slot by content for an unmapped cell: vanilla's Sort compacts this array in
                // place before calling here, and a content match could pick a stack the view is NOT showing and
                // overwrite it (one stack lost per hole). An unmapped cell with something in it is a deposit.
                ScStorage.Slot s = map[_idx];
                if (s != null)
                {
                    if (!ScStorage.WriteThrough(s, v))
                    {
                        // the box vanished under us: the item stays with the player rather than in limbo
                        ReturnToPlayer(v);
                        items[_idx] = ItemStack.Empty.Clone();
                        map[_idx] = null;
                        NotifyListeners();
                        return;
                    }
                    items[_idx] = v;
                    if (v.IsEmpty()) map[_idx] = null;
                    return;
                }
                // no box behind this cell: a deposit
                if (v.IsEmpty()) { items[_idx] = v; return; }
                ScStorage.Slot placed;
                if (_idx >= ShowLimit)
                {
                    // Grid Inventory has no widget for this slot: stored here the item would vanish from the view
                    Refuse(_idx, v, "scStorageViewFull");
                    return;
                }
                if (ScStorage.Deposit(v, out placed))
                {
                    items[_idx] = v;
                    map[_idx] = placed;
                    return;
                }
                Refuse(_idx, v, "scStorageFull");
            }
            catch (Exception e)
            {
                ScLog.Once("linked-updateslot", e);
            }
        }

        /// <summary>
        /// A drop nothing takes. Vanilla's drop has cleared the cursor by now, so the item comes back to the
        /// player; GridInventory's drop verifies the write first and still holds the item on the cursor, and
        /// returning it as well would double it.
        /// </summary>
        private void Refuse(int _idx, ItemStack v, string why)
        {
            if (!StillOnCursor(v)) ReturnToPlayer(v);
            items[_idx] = ItemStack.Empty.Clone();
            map[_idx] = null;
            try { NotifyListeners(); } catch (Exception e) { ScLog.Once("linked-notify", e); }
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
                       && cur.itemValue.type == v.itemValue.type && cur.count == v.count;
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

        public void RemoveItem(ItemValue _itemValue)
        {
            if (items == null || _itemValue == null) return;
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i] == null || items[i].IsEmpty() || items[i].itemValue == null || items[i].itemValue.type != _itemValue.type) continue;
                UpdateSlot(i, ItemStack.Empty.Clone());
                return;
            }
        }

        public int RemoveItems(ItemValue _itemValue, int _count)
        {
            int removed = 0;
            if (items == null || _itemValue == null) return 0;
            for (int i = 0; i < items.Length && removed < _count; i++)
            {
                ItemStack s = items[i];
                if (s == null || s.IsEmpty() || s.itemValue == null || s.itemValue.type != _itemValue.type) continue;
                int n = Math.Min(s.count, _count - removed);
                ItemStack left = s.Clone();
                left.count -= n;
                if (left.count <= 0) left = ItemStack.Empty.Clone();
                UpdateSlot(i, left);
                removed += n;
            }
            if (removed > 0) { try { NotifyListeners(); } catch (Exception e) { ScLog.Once("linked-notify", e); } }
            return removed;
        }

        public bool IsEmpty()
        {
            if (items == null) return true;
            for (int i = 0; i < items.Length; i++) if (items[i] != null && !items[i].IsEmpty()) return false;
            return true;
        }

        /// <summary>Never: "set empty" on the view must not wipe the boxes.</summary>
        public void SetEmpty() { }

        public bool ShouldDestroyOnClose() { return false; }

        // ---- IInventory (shift-click from the backpack, move-all, drop-merge) ----

        public bool AddItem(ItemStack _itemStack)
        {
            try
            {
                if (_itemStack == null || _itemStack.IsEmpty()) return false;
                // a cell the view can draw first: past ShowLimit the item would be stored out of sight
                int at = -1;
                int limit = Math.Min(items.Length, ShowLimit);
                for (int i = 0; i < limit; i++)
                {
                    if (map[i] == null && (items[i] == null || items[i].IsEmpty())) { at = i; break; }
                }
                if (at < 0)
                {
                    EntityPlayerLocal p = ScCommunity.LocalPlayer;
                    if (p != null) ScCommunity.Tell(p, Localization.Get(ScStorage.FreeFor(GridDrawn) > 0 ? "scStorageViewFull" : "scStorageFull"), "ui_denied");
                    return false;
                }
                ScStorage.Slot placed;
                if (!ScStorage.Deposit(_itemStack, out placed)) return false;
                items[at] = _itemStack.Clone();
                map[at] = placed;
                // the item is in a box now: a failing redraw must not report the add as failed (the caller would
                // keep its copy too)
                try { NotifyListeners(); } catch (Exception e) { ScLog.Once("linked-notify", e); }
                return true;
            }
            catch (Exception e)
            {
                ScLog.Once("linked-additem", e);
                return false;
            }
        }

        public (bool anyMoved, bool allMoved) TryStackItem(int _startIndex, ItemStack _itemStack)
        {
            bool any = false;
            try
            {
                if (_itemStack == null || _itemStack.IsEmpty() || items == null) return (false, false);
                for (int i = Math.Max(0, _startIndex); i < items.Length && _itemStack.count > 0; i++)
                {
                    ItemStack s = items[i];
                    if (s == null || s.IsEmpty() || map[i] == null) continue;
                    if (s.itemValue.type != _itemStack.itemValue.type) continue;
                    int n = _itemStack.count;
                    if (!s.CanStackPartly(ref n) || n <= 0) continue;
                    ItemStack merged = s.Clone();
                    merged.count += n;
                    if (!ScStorage.WriteThrough(map[i], merged)) continue;
                    items[i] = merged;
                    _itemStack.count -= n;
                    any = true;
                }
                if (any) NotifyListeners();
            }
            catch (Exception e)
            {
                ScLog.Once("linked-trystack", e);
            }
            return (any, _itemStack != null && _itemStack.count == 0);
        }

        public bool HasItem(ItemValue _item)
        {
            if (items == null || _item == null) return false;
            for (int i = 0; i < items.Length; i++)
            {
                ItemStack s = items[i];
                if (s != null && !s.IsEmpty() && s.itemValue != null && s.itemValue.type == _item.type) return true;
            }
            return false;
        }
    }
}
