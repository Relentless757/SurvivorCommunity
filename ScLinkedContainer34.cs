using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Linked storage as a loot container - the game 3.4 edition (compiled only by the ScGame=34 build, in place of
    /// ScLinkedContainer.cs; SurvivorCommunity.csproj). One virtual container whose slots are the merged stacks of every player
    /// box in the zone (ScStorage.Slots), opened in vanilla's own loot window.
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
    ///  - every writer - the window's drag and drop, take and half, swap, take all, Grid Inventory's grid - changes the view's
    ///    grid, whose SlotChanged marks the cell (OnViewSlotChanged); SC's own writes to the view are made with it off (quiet);
    ///  - TEFeatureStorage.AddItem and TryStackItem (shift-click from the backpack, the backpack's move-all) and ItemStackGrid.Sort
    ///    are prefixed for this class alone (Patch_LinkedAddItem, Patch_LinkedTryStackItem, Patch_LinkedSort);
    ///  - no slot locks (3.2's HasSlotLocksSupport false): a lock on a cell of a view rebuilt on every open means nothing.
    ///
    /// Round MP3 (part L, multiplayer): the server writes the boxes (ScStorageServer). The view only proposes:
    ///  - Each cell is backed by a box slot (map: ScStorage.Slot, whose Stack and Code are what the server last confirmed) or by
    ///    none (an empty cell: what lands there is a deposit, into the first box with room).
    ///  - The window's changes mark cells; once a frame, after the window's clicks (Pump, from ScStoragePump.LateUpdate), every
    ///    changed cell goes to the server as ONE batch (storage.cells): a compare-and-set per backed cell ("the slot holds what I
    ///    saw; put this there"), a deposit per empty one. One batch is in flight per view; what changes meanwhile waits for it.
    ///  - The server judges the batch as a whole. Accepted: the cells' slots take the new codes. Refused (another view or the
    ///    world changed a slot, a box is gone, no room): nothing was written, and Refuse puts EVERY cell this batch touched,
    ///    and every cell changed since, back to the box as it is now - and settles the difference with the player: what those
    ///    cells held here goes back into the player's inventory, what they showed before goes back out of the player's cursor
    ///    and inventory (Settle: netted per item first, so an item that only moved between cells costs nothing). The settle
    ///    exists because vanilla's grid moves an item onto the cursor before anyone can ask the server.
    ///  - On the host and in single player the batch is answered at once, inline, in the same frame (one code path); AddItem,
    ///    TryStackItem and Sort ask the server first there, as 3.2's logic did, and nothing moves on a refusal. On a client they
    ///    move at once and a refusal gives the items back.
    /// Layout as on 3.2 (Rebuild): ONE grid, live stacks first, then a band of empty cells for the boxes' free room; capped
    /// by Grid Inventory's widget pool (StackCap, ShowLimit) when it draws the view (ScStorage.SizeFor). Without Grid
    /// Inventory it opens in SC's own window (round s1, OwnWindow): vanilla's looting window with 360 cells in a scrolling
    /// view, where vanilla's has 90.
    /// </summary>
    public class ScLinkedContainer : SCoreLootContainer
    {
        /// <summary>The view's columns in vanilla's looting window and on RE Inventory's grid (and SC's own window's vanilla look).</summary>
        public const int Cols = 9;
        /// <summary>Round s1b: the columns this view is laid out in - Cols, or in SC's own window the columns its grid declares
        /// (read once by ScStorage.OwnWindowFor, set by OpenView, kept by SizeFor; Tools/generate_linked_storage_ui.py: 9,
        /// and 8 in AshfallUI's look, the width of AshfallUI's loot
        /// window for the widest vanilla crate). The container is built this wide, and XUiC_LootContainer.SetSlots gives the
        /// grid the container's width, so this is what the window draws.</summary>
        public int ViewCols = Cols;
        /// <summary>A loot container defined in Config/loot.xml: vanilla looks lootListName up on open and close (and SCore
        /// closes a loot window whose container has no loot list name).</summary>
        public const string LootListKey = "scLinkedStorage";

        public int RowsCap = 10;
        /// <summary>Most stacks laid out (0 = no cap): Grid Inventory's item widgets, less two rows for drops.</summary>
        public int StackCap;
        /// <summary>Whether Grid Inventory draws this view (set by ScStorage.SizeFor).</summary>
        public bool GridDrawn;
        /// <summary>Round s1: the view opens in SC's own linked storage window (ScStorage.OwnWindowGroup: vanilla's looting
        /// window with 360 cells, scrolling) rather than vanilla's looting window - without RE Inventory. Set by
        /// ScStorage.OpenView before SizeFor, which then sizes the view to that window's widgets.</summary>
        public bool OwnWindow;
        /// <summary>Slots that can be drawn: Grid Inventory's item widgets (slot i needs widget i). A stack may
        /// only enter the view below it; past it a drop is refused rather than stored out of sight.</summary>
        public int ShowLimit = int.MaxValue;
        /// <summary>Empty rows laid out under the stacks when Grid Inventory draws the view.</summary>
        public const int HeadroomRows = 12;

        /// <summary>Round MP3: the server's session this view belongs to (every batch names it).</summary>
        public int Session = -1;
        /// <summary>Round MP3: the window let the view go; its last changes are still sent and answered (ScStorage.Pump).</summary>
        internal bool Closed;
        internal float ClosedAt;

        /// <summary>A batch with no answer after this many real seconds is given up (only a dropped connection does that).</summary>
        private const float FlightTimeout = 40f;
        /// <summary>Times in a row the view may make good a shortfall from a box (TakeFromView) before it only logs it.</summary>
        private const int MakeGoodLimit = 3;
        /// <summary>A batch refused because the player was dead is held this long for the respawn (the server holds the view
        /// ScStorageServer.DeadHold); past it, it is put back as any refusal.</summary>
        private const float DeathHoldSeconds = 150f;

        /// <summary>The box slot behind each cell (null: none - an empty cell; Index -1: a deposit not placed yet).</summary>
        private ScStorage.Slot[] map = new ScStorage.Slot[0];

        /// <summary>The mirror is off: SC itself is writing the view (Rebuild, a cell put back).</summary>
        private bool quiet;

        /// <summary>Round MP3: cells the window changed since the last batch, in the order they changed.</summary>
        private readonly List<int> dirty = new List<int>();
        private readonly HashSet<int> dirtySet = new HashSet<int>();
        private Flight flight;
        /// <summary>Round MP3: a batch the server refused because the player was dead (scActionDead) - nothing was written, and
        /// it is sent again once they are alive; reverting it would take back items the death may have dropped in a backpack.</summary>
        private Flight held;
        private float heldSince;
        private bool sortWanted;
        private int makeGood;
        private static int nextBatch;

        /// <summary>One cell's change in a batch: a compare-and-set on Slot (Expect -> New), or a deposit of New.</summary>
        private sealed class Change
        {
            public ScStorage.Slot Slot;
            public bool Deposit;
            public string Expect = "";
            public string New = "";
        }

        /// <summary>The batch in flight.</summary>
        private sealed class Flight
        {
            public int Id;
            public float At;
            public List<Change> Changes;
        }

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

        /// <summary>Round MP3: changes on their way - unsent cells or a batch in flight.</summary>
        internal bool Busy { get { return flight != null || held != null || dirty.Count > 0; } }

        // ---- layout ----

        /// <summary>Cells the grid may hold: ViewCols x RowsCap (up to 1000 rows under Grid Inventory, 10 on vanilla's looting grid,
        /// 40 of 9 in SC's own window, 45 of 8 in its AshfallUI look - on 3.4 never more than the window has widgets,
        /// ScStorage.SizeFor).</summary>
        private int MaxCells { get { return ViewCols * Math.Max(1, RowsCap); } }

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
            var live = new List<ScStorage.Slot>();
            int usedArea = 0;
            Hidden = 0;
            for (int i = 0; i < ScStorage.Slots.Count; i++)
            {
                ScStorage.Slot s = ScStorage.Slots[i];
                if (s.Stack == null || s.Stack.IsEmpty()) continue;
                int a = GridDrawn ? Math.Min(ScStorage.AreaOf(s.Stack), cap) : 1;   // vanilla: one stack, one slot
                // 3.4: hidden by STACKS only. RE Inventory 3.3+ draws a cell per slot and grows the view's rows for
                // footprints past its cells, so a stack costs the view one slot; the area only sizes it (below), so a
                // rifle (ten cells) no longer hides stacks past the view's slots (3.2's rule did, at 9,000 cells)
                if ((StackCap > 0 && live.Count >= StackCap) || live.Count >= cap) { Hidden++; continue; }
                live.Add(s);
                usedArea += a;
            }
            int free = ScStorage.FreeFor(GridDrawn);
            int shown = ShownFree(usedArea, free);
            HiddenCells = free - shown;
            int cells = Mathf.Clamp(usedArea + shown, 1, cap);
            int cols = Math.Max(1, ViewCols);
            int rows = Mathf.Clamp((cells + cols - 1) / cols, 1, Math.Max(1, RowsCap));
            int total = cols * rows;
            var stacks = new ItemStack[total];
            map = new ScStorage.Slot[total];
            for (int i = 0; i < total; i++)
            {
                if (i < live.Count) { map[i] = live[i]; stacks[i] = live[i].Stack.Clone(); }
                else { map[i] = null; stacks[i] = ItemStack.Empty.Clone(); }
            }
            dirty.Clear();
            dirtySet.Clear();
            quiet = true;
            try
            {
                SetContainerSize(new Vector2i(cols, rows), false);
                ItemGrid.SetItems(stacks, false);
                ItemGrid.SetSlotLocks(null);
            }
            finally
            {
                quiet = false;
            }
        }

        /// <summary>The empty cells the view lays out for the boxes' free room: past MaxCells, or past HeadroomRows on Grid Inventory's grid, the footer still counts that room but no cell shows it.</summary>
        private int ShownFree(int _usedArea, int _free)
        {
            int shown = Math.Min(_free, MaxCells - _usedArea);
            if (GridDrawn) shown = Math.Min(shown, HeadroomRows * ViewCols);
            return Math.Max(0, shown);
        }

        /// <summary>
        /// The cells the view should have right now: its stacks' area plus the empty cells for the boxes' free room, at least
        /// 1 because 0 = no limit (vanilla's grid). Rebuild has to give the view a whole number of rows, so its last row is
        /// padded with cells no box stands behind: 74 cells of stacks and 2 free made 81 cells, 7 of them empty on screen, with
        /// the footer rightly saying 2 (user, 2026-10-30). RE Inventory 3.3+ asks this on every layout (the bridge's hook) and
        /// does not draw the cells past it. It is asked again because it moves: a stack leaving a box filled past its grid
        /// gives back fewer cells than it held, and a deposit can tip a box over. A change with no slot change (a crate
        /// placed while the view is open) waits for the next layout, as the view itself waits for Refresh.
        /// </summary>
        public int CellsNow()
        {
            if (!GridDrawn) return 0;
            int cap = MaxCells;
            int used = 0;
            for (int i = 0; i < map.Length; i++)
            {
                if (map[i] != null && map[i].Stack != null && !map[i].Stack.IsEmpty()) used += Math.Min(ScStorage.AreaOf(map[i].Stack), cap);
            }
            return Math.Max(1, used + ShownFree(used, ScStorage.FreeFor(GridDrawn)));
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

        /// <summary>The cell a box slot is shown in, -1 when none.</summary>
        private int CellOf(ScStorage.Slot _s)
        {
            if (_s == null) return -1;
            for (int i = 0; i < map.Length; i++) if (ReferenceEquals(map[i], _s)) return i;
            return -1;
        }

        /// <summary>A box slot whose content is unknown until the batch in flight is answered.</summary>
        private bool InFlight(ScStorage.Slot _s)
        {
            if (flight == null || _s == null) return false;
            for (int i = 0; i < flight.Changes.Count; i++) if (ReferenceEquals(flight.Changes[i].Slot, _s)) return true;
            return false;
        }

        /// <summary>A backed cell whose box slot is known (not changed here, not in flight).</summary>
        private bool Settled(int _idx)
        {
            ScStorage.Slot s = MapAt(_idx);
            return s != null && s.Index >= 0 && s.Stack != null && !dirtySet.Contains(_idx) && !InFlight(s);
        }

        /// <summary>Writes a cell with the mirror off (nothing is sent for it).</summary>
        private void SetQuiet(int _idx, ItemStack _stack)
        {
            quiet = true;
            try { ItemGrid.SetItem(_idx, _stack != null && !_stack.IsEmpty() ? _stack.Clone() : null); }
            finally { quiet = false; }
        }

        // ---- the window's changes ----

        /// <summary>
        /// A cell changed (the grid's SlotChanged): round MP3, it is only marked. The pump sends the frame's changed cells as one
        /// batch after the window has finished the click - vanilla moves the cursor and the cell in either order, in one call,
        /// so only then do the cell and the cursor agree. Any exception is caught: this runs inside the window's own write.
        /// </summary>
        private void OnViewSlotChanged(int _idx)
        {
            if (quiet) return;
            try
            {
                if (_idx < 0 || _idx >= ItemGrid.Length) return;
                MarkDirty(_idx);
            }
            catch (Exception e)
            {
                ScLog.Once("linked-slotchanged", e);
            }
        }

        private void MarkDirty(int _idx)
        {
            if (dirtySet.Add(_idx)) dirty.Add(_idx);
        }

        private void Notify()
        {
            try { if (Parent != null) Parent.NotifyListeners(); }
            catch (Exception e) { ScLog.Once("linked-notify", e); }
        }

        private static void Tell(string key, string sound = "ui_denied")
        {
            EntityPlayerLocal p = ScCommunity.LocalPlayer;
            if (p != null && !string.IsNullOrEmpty(key)) ScCommunity.Tell(p, ScActions.Localize(key), sound);
        }

        /// <summary>The view shows this container in the loot window (vanilla's looting window or SC's own).</summary>
        internal bool IsShown()
        {
            try
            {
                EntityPlayerLocal p = ScCommunity.LocalPlayer;
                if (p == null || p.PlayerUI == null || p.PlayerUI.windowManager == null || p.PlayerUI.xui == null) return false;
                return ShownIn(p, XUiC_LootWindowGroup.ID) || ShownIn(p, ScStorage.OwnWindowGroup);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private bool ShownIn(EntityPlayerLocal p, string group)
        {
            if (!p.PlayerUI.windowManager.IsWindowOpen(group)) return false;
            XUiC_LootWindowGroup lg = p.PlayerUI.xui.FindWindowGroupByName(group) as XUiC_LootWindowGroup;
            return lg != null && ReferenceEquals(lg.te, this);
        }

        // ---- batches ----

        /// <summary>
        /// Once a frame (ScStorage.Pump): with no batch in flight, every changed cell goes as one batch. The host is answered
        /// at once, so it sends again while cells are left (a settle can mark some). A batch never answered is given up.
        /// </summary>
        internal void Pump(float now)
        {
            try
            {
                for (int guard = 0; guard < 8; guard++)
                {
                    if (flight != null)
                    {
                        if (now - flight.At > FlightTimeout) GiveUp();
                        return;
                    }
                    if (held != null)
                    {
                        // the batch refused at the player's death: again once they are alive (the server held the view)
                        EntityPlayerLocal p = ScCommunity.LocalPlayer;
                        Flight h = held;
                        if (p != null && !p.IsDead())
                        {
                            held = null;
                            Transmit(h.Changes, null);
                            continue;
                        }
                        if (now - heldSince > DeathHoldSeconds)
                        {
                            held = null;
                            Refuse(h, null);
                        }
                        return;
                    }
                    if (dirty.Count == 0)
                    {
                        if (sortWanted)
                        {
                            sortWanted = false;
                            if (!GridDrawn && !Closed) { SortCells(); Notify(); }
                        }
                        return;
                    }
                    if (!SendDirty()) return;
                }
            }
            catch (Exception e)
            {
                ScLog.Once("linked-pump", e);
            }
        }

        /// <summary>Builds the batch from the changed cells and sends it. False when none of them changed after all.</summary>
        private bool SendDirty()
        {
            var cells = dirty.ToArray();
            dirty.Clear();
            dirtySet.Clear();
            var changes = new List<Change>();
            bool notify = false;
            for (int n = 0; n < cells.Length; n++)
            {
                int i = cells[n];
                if (i < 0 || i >= ItemGrid.Length) continue;
                ItemStack now = ItemGrid[i];
                ScStorage.Slot s = MapAt(i);
                bool backed = s != null && s.Index >= 0 && s.Stack != null && !s.Stack.IsEmpty();
                if (!backed)
                {
                    // no box slot behind it (or one a take emptied): what lands here is a deposit - the first box with room, as
                    // before - never a write into the emptied slot past the grid bridge's footprint test
                    if (s != null) SetMap(i, null);
                    if (now.IsEmpty()) continue;
                    if (i >= ShowLimit)
                    {
                        // Grid Inventory has no widget for this slot: stored here the item would vanish from the view
                        RefuseLocal(i, "scStorageViewFull");
                        notify = true;
                        continue;
                    }
                    string dep = ScItemTx.Encode(now);
                    if (dep == null) { RefuseLocal(i, "scStorageTooLarge"); notify = true; continue; }
                    var placeholder = new ScStorage.Slot { Pos = Vector3i.zero, Index = -1, Stack = ItemStack.Empty.Clone(), Code = "" };
                    SetMap(i, placeholder);
                    changes.Add(new Change { Slot = placeholder, Deposit = true, New = dep });
                    continue;
                }
                string code = ScItemTx.Encode(now);
                if (code == null)
                {
                    // too large to send (an item with a huge payload): the cell goes back to its box's content, the player's side too
                    var back = new List<ItemStack> { now.Clone() };
                    var owed = new List<ItemStack> { s.Stack.Clone() };
                    SetQuiet(i, s.Stack);
                    Settle(back, owed);
                    Tell("scStorageTooLarge");
                    notify = true;
                    continue;
                }
                if (string.Equals(code, s.Code, StringComparison.Ordinal)) continue;     // back to what the box holds: nothing to send
                changes.Add(new Change { Slot = s, Expect = s.Code ?? "", New = code });
            }
            if (notify) Notify();
            if (changes.Count == 0) return false;
            Transmit(changes, null);
            return true;
        }

        /// <summary>
        /// Sends a batch (in parts of ScStorageServer.PartEntries; the server judges it at the last part) and keeps it in flight.
        /// onAnswer replaces the usual answer (OnAnswer) - the host's ask-first paths; on the host it runs before this returns.
        /// </summary>
        private void Transmit(List<Change> changes, Action<Flight, ScActionReply> onAnswer)
        {
            var f = new Flight { Id = ++nextBatch, At = Time.unscaledTime, Changes = changes };
            flight = f;
            int per = ScStorageServer.PartEntries;
            int parts = Math.Max(1, (changes.Count + per - 1) / per);
            for (int part = 0; part < parts; part++)
            {
                int from = part * per, n = Math.Min(per, changes.Count - from);
                var ints = new int[ScStorageServer.HeadInts + n * ScStorageServer.EntryInts];
                var strs = new string[2 * n];
                ints[0] = Session; ints[1] = f.Id; ints[2] = part; ints[3] = parts;
                for (int k = 0; k < n; k++)
                {
                    Change c = changes[from + k];
                    int o = ScStorageServer.HeadInts + k * ScStorageServer.EntryInts;
                    ints[o] = c.Deposit ? ScStorageServer.FlagDeposit : 0;
                    ints[o + 1] = c.Slot.Pos.x; ints[o + 2] = c.Slot.Pos.y; ints[o + 3] = c.Slot.Pos.z;
                    ints[o + 4] = c.Slot.Index;
                    strs[2 * k] = c.Deposit ? c.New : c.Expect;
                    strs[2 * k + 1] = c.Deposit ? "" : c.New;
                }
                Action<ScActionReply> cb = null;
                if (part == parts - 1)
                {
                    if (onAnswer != null) cb = r => { if (ReferenceEquals(flight, f)) flight = null; onAnswer(f, r); };
                    else cb = r => OnAnswer(f, r);
                }
                ScActions.Request(ScStorageServer.OpCells, ints, strs, cb);
            }
        }

        /// <summary>The server's answer to a batch from the pump: accepted, or refused and put back.</summary>
        private void OnAnswer(Flight f, ScActionReply r)
        {
            try
            {
                if (!ReferenceEquals(flight, f)) return;         // given up meanwhile
                flight = null;
                ScStorage.Slot[] res;
                bool hasRes = ScStorageServer.ParseResults(r.HintStr("res", null), f.Changes.Count, out res);
                if (r.HasHint("fs") && ReferenceEquals(ScStorage.Current, this)) ScStorage.SetFree(r.HintInt("fs"), r.HintInt("fc", -1));
                if (r.Ok)
                {
                    if (!hasRes)
                    {
                        // a server of this protocol always answers res: without it the view cannot know where a deposit went
                        ScLog.Err("linked storage: the server took batch " + f.Id + " without saying where - the view closes");
                        dirty.Clear();
                        dirtySet.Clear();
                        ScStorage.CloseWindowOf(this);
                        return;
                    }
                    Accept(f, res);
                    makeGood = 0;
                    return;
                }
                if (r.Message == "scActionDead")
                {
                    // ScActions refuses a dead player before the handler runs: nothing was written. Sent again after the respawn.
                    held = f;
                    heldSince = Time.unscaledTime;
                    if (ScConfig.Verbose) ScLog.Verbose("linked storage: batch " + f.Id + " held until the respawn (the server refused it: dead)");
                    return;
                }
                Refuse(f, hasRes ? res : null);
                if (!string.IsNullOrEmpty(r.Message)) Tell(r.Message, r.Sound);
            }
            catch (Exception e)
            {
                ScLog.Once("linked-answer", e);
            }
        }

        /// <summary>Accepted: each slot takes the server's code; a deposit learns its box and slot; an emptied slot leaves its cell.</summary>
        private void Accept(Flight f, ScStorage.Slot[] res)
        {
            bool current = ReferenceEquals(ScStorage.Current, this);
            for (int k = 0; k < f.Changes.Count; k++)
            {
                Change c = f.Changes[k];
                ScStorage.Slot r = res[k];
                ScStorage.Slot s = c.Slot;
                if (c.Deposit)
                {
                    s.Pos = r.Pos;
                    s.Index = r.Index;
                    if (current && !ScStorage.Slots.Contains(s))
                    {
                        // an older entry for that box slot (emptied since, no longer in a cell) would count twice in the strip
                        ScStorage.Slots.RemoveAll(o => !ReferenceEquals(o, s) && o.Pos == s.Pos && o.Index == s.Index && CellOf(o) < 0);
                        ScStorage.Slots.Add(s);
                    }
                }
                bool gone = r.Code == ScStorageServer.ResGone;
                s.Code = gone ? "" : r.Code;
                s.Stack = gone ? ItemStack.Empty.Clone() : ScItemTx.Decode(r.Code);
                int cell = CellOf(s);
                if (cell >= 0 && s.Stack.IsEmpty() && ItemGrid[cell].IsEmpty() && !dirtySet.Contains(cell)) SetMap(cell, null);
            }
        }

        /// <summary>
        /// Refused: the server wrote nothing. Every cell this batch touched and every cell changed since goes back to its box slot
        /// as it is now (res; without it, as the view last knew it), and the player gets back what those cells' changes moved:
        /// what the cells hold here returns to them, what the cells showed before is taken back from their cursor and inventory.
        /// </summary>
        private void Refuse(Flight f, ScStorage.Slot[] res)
        {
            var cells = new List<int>();
            var now = new Dictionary<ScStorage.Slot, string>();
            for (int k = 0; k < f.Changes.Count; k++)
            {
                Change c = f.Changes[k];
                int cell = CellOf(c.Slot);
                if (cell >= 0 && !cells.Contains(cell)) cells.Add(cell);
                if (!c.Deposit && res != null) now[c.Slot] = res[k].Code;
            }
            for (int i = 0; i < dirty.Count; i++) if (!cells.Contains(dirty[i])) cells.Add(dirty[i]);
            dirty.Clear();
            dirtySet.Clear();
            var back = new List<ItemStack>();
            var owed = new List<ItemStack>();
            for (int n = 0; n < cells.Count; n++)
            {
                int i = cells[n];
                if (i < 0 || i >= ItemGrid.Length) continue;
                ScStorage.Slot s = MapAt(i);
                ItemStack here = ItemGrid[i].Clone();
                bool real = s != null && s.Index >= 0;
                ItemStack before = real && s.Stack != null ? s.Stack.Clone() : ItemStack.Empty.Clone();
                if (!here.IsEmpty()) back.Add(here);
                if (!before.IsEmpty()) owed.Add(before);
                ItemStack show = ItemStack.Empty.Clone();
                if (real)
                {
                    string code;
                    if (now.TryGetValue(s, out code))
                    {
                        // its box as it is now; a box gone or out of reach (locked, opened) leaves the view
                        bool gone = code == ScStorageServer.ResGone;
                        s.Code = gone ? "" : code;
                        s.Stack = gone ? ItemStack.Empty.Clone() : ScItemTx.Decode(code);
                    }
                    show = s.Stack != null ? s.Stack.Clone() : ItemStack.Empty.Clone();
                    if (show.IsEmpty()) SetMap(i, null);
                }
                else SetMap(i, null);
                SetQuiet(i, show);
            }
            if (ScConfig.Verbose) ScLog.Verbose("linked storage: batch " + f.Id + " refused - " + cells.Count + " cell(s) put back");
            Settle(back, owed);
            Notify();
        }

        /// <summary>A change the view refuses by itself (nothing sent): the cell is emptied and its item goes back to the player.</summary>
        private void RefuseLocal(int _idx, string why)
        {
            ItemStack v = ItemGrid[_idx].Clone();
            if (ScConfig.Verbose)
                ScLog.Verbose("linked storage: cell " + _idx + " of " + ItemGrid.Length + " refused (" + why + ") for "
                              + (v.itemValue != null && v.itemValue.ItemClass != null ? v.itemValue.ItemClass.GetItemName() : "?") + " x" + v.count);
            SetMap(_idx, null);
            SetQuiet(_idx, null);
            Settle(new List<ItemStack> { v }, new List<ItemStack>());
            Tell(why);
        }

        /// <summary>A batch never answered (only a dropped connection does that): the view closes; what was in flight is unknown.</summary>
        private void GiveUp()
        {
            Flight f = flight;
            flight = null;
            dirty.Clear();
            dirtySet.Clear();
            ScLog.Warn("linked storage: the server never answered batch " + (f != null ? f.Id : 0) + " (" + (f != null ? f.Changes.Count : 0)
                       + " change(s)) - the view closes");
            ScStorage.CloseWindowOf(this);
        }

        /// <summary>
        /// Another view wrote boxes this view shows: a cell that is settled here (not changed, not in flight) takes the slot's new
        /// content. A cell with a change of its own keeps it - the server judges that change against the box.
        /// </summary>
        internal void ApplyPush(List<ScStorage.Slot> updates)
        {
            if (updates == null) return;
            bool changed = false;
            for (int u = 0; u < updates.Count; u++)
            {
                ScStorage.Slot up = updates[u];
                for (int i = 0; i < map.Length; i++)
                {
                    ScStorage.Slot s = map[i];
                    if (s == null || s.Index != up.Index || s.Pos != up.Pos) continue;
                    if (!Settled(i)) break;
                    s.Code = up.Code ?? "";
                    s.Stack = ScItemTx.Decode(s.Code);
                    SetQuiet(i, s.Stack);
                    if (s.Stack.IsEmpty()) SetMap(i, null);
                    changed = true;
                    break;
                }
                if (!ReferenceEquals(ScStorage.Current, this)) continue;
                // a stack the grid could not show (Hidden) is counted in the strip all the same
                for (int k = 0; k < ScStorage.Slots.Count; k++)
                {
                    ScStorage.Slot s = ScStorage.Slots[k];
                    if (s.Index != up.Index || s.Pos != up.Pos || CellOf(s) >= 0) continue;
                    s.Code = up.Code ?? "";
                    s.Stack = ScItemTx.Decode(s.Code);
                }
            }
            if (changed) Notify();
        }

        // ---- settling with the player (round MP3) ----

        /// <summary>
        /// The player's side of a put-back: `back` is what the put-back cells held here (it returns to the player), `owed` what
        /// they showed before (the player took it, so it comes back from them). Netted per item first, so an item that moved
        /// between the view's own cells, or was put back where it was, costs nothing. Given back: into the bag and toolbelt, the
        /// rest dropped at their feet (ScItemTx.GiveToLocal - never lost). Taken back: from the cursor, then the bag and toolbelt
        /// (the exact item). What the player no longer has (used or dropped within the round trip) is taken from a box of the view
        /// that holds it instead - a take the next batch asks for - else logged.
        /// </summary>
        private void Settle(List<ItemStack> back, List<ItemStack> owed)
        {
            var give = new List<ItemStack>();
            var take = new List<ItemStack>();
            Net(back, owed, give, take);
            if (give.Count == 0 && take.Count == 0) return;
            EntityPlayerLocal p = ScCommunity.LocalPlayer;
            if (p == null)
            {
                ScLog.Warn("linked storage: no player to settle a refused change with (" + give.Count + " stack(s) to give, " + take.Count + " to take)");
                return;
            }
            if (give.Count > 0)
            {
                int dropped = ScItemTx.GiveToLocal(p, give);
                if (dropped > 0 && ScConfig.Verbose) ScLog.Verbose("linked storage: " + dropped + " item(s) given back did not fit - dropped at the player's feet");
            }
            for (int i = 0; i < take.Count; i++)
            {
                ItemStack t = take[i];
                int left = t.count - TakeBackLocal(p, t);
                if (left > 0 && makeGood < MakeGoodLimit) left = TakeFromView(t, left);
                if (left > 0)
                    ScLog.Warn("linked storage: " + left + " x " + ItemName(t) + " could not be taken back after a refused change (the item left the player's inventory) - the player keeps it");
            }
        }

        private static string ItemName(ItemStack s)
        {
            ItemClass ic = s != null && s.itemValue != null ? s.itemValue.ItemClass : null;
            return ic != null ? ic.GetItemName() : "?";
        }

        /// <summary>
        /// The item's identity for settling: a plain stackable item (no quality) by its type - stacks of it are one and the same,
        /// as vanilla's merge treats them - anything else by its whole encoding (that gun, with its quality, mods and wear).
        /// </summary>
        private static string Key(ItemStack s)
        {
            if (s == null || s.IsEmpty() || s.itemValue == null) return null;
            ItemClass ic = s.itemValue.ItemClass;
            if (ic != null && ic.CanStack() && !s.itemValue.HasQuality) return "t" + s.itemValue.type;
            string c = ScItemTx.Encode(new ItemStack(s.itemValue.Clone(), 1));
            return "x" + (c ?? s.itemValue.type.ToString());
        }

        private static void Net(List<ItemStack> back, List<ItemStack> owed, List<ItemStack> give, List<ItemStack> take)
        {
            var net = new Dictionary<string, int>();
            var sample = new Dictionary<string, ItemStack>();
            var order = new List<string>();
            for (int pass = 0; pass < 2; pass++)
            {
                List<ItemStack> list = pass == 0 ? back : owed;
                for (int i = 0; i < list.Count; i++)
                {
                    string k = Key(list[i]);
                    if (k == null) continue;
                    int n;
                    if (!net.TryGetValue(k, out n)) { n = 0; order.Add(k); sample[k] = list[i]; }
                    net[k] = n + (pass == 0 ? list[i].count : -list[i].count);
                }
            }
            for (int i = 0; i < order.Count; i++)
            {
                int n = net[order[i]];
                ItemStack smp = sample[order[i]];
                if (n < 0) { take.Add(new ItemStack(smp.itemValue.Clone(), -n)); continue; }
                ItemClass ic = smp.itemValue.ItemClass;
                int max = Math.Max(1, ic != null ? ic.MaxCount : 1);
                while (n > 0)
                {
                    int k = Math.Min(n, max);
                    give.Add(new ItemStack(smp.itemValue.Clone(), k));
                    n -= k;
                }
            }
        }

        /// <summary>The player's own machine: removes up to want.count of the exact item - the cursor first, then the bag and the toolbelt. Returns how many.</summary>
        private static int TakeBackLocal(EntityPlayerLocal p, ItemStack want)
        {
            int got = 0;
            try
            {
                string key = Key(want);
                int need = want.count;
                XUi xui = p.PlayerUI != null ? p.PlayerUI.xui : null;
                XUiC_DragAndDropWindow dd = xui != null ? xui.DragAndDropWindow : null;
                if (dd != null && need > 0)
                {
                    ItemStack cur = dd.CurrentStack;
                    if (Key(cur) == key)
                    {
                        int k = Math.Min(need, cur.count);
                        cur.count -= k;
                        dd.SetCurrentStack(cur.count > 0 ? cur : ItemStack.Empty.Clone(), false);
                        got += k;
                        need -= k;
                    }
                }
                if (need > 0 && p.bag != null)
                {
                    ItemStackGrid g = p.bag.ItemGrid;
                    for (int i = 0; i < g.Length && need > 0; i++)
                    {
                        ItemStack s = g[i];
                        if (Key(s) != key) continue;
                        int k = Math.Min(need, s.count);
                        p.bag.SetSlot(i, s.count > k ? new ItemStack(s.itemValue.Clone(), s.count - k) : ItemStack.Empty.Clone());
                        got += k;
                        need -= k;
                    }
                }
                if (need > 0 && p.inventory != null)
                {
                    ItemStackGrid g = p.inventory.ItemGrid;
                    for (int i = 0; i < g.Length && need > 0; i++)
                    {
                        ItemStack s = g[i];
                        if (Key(s) != key) continue;
                        int k = Math.Min(need, s.count);
                        p.inventory.SetItem(i, s.count > k ? new ItemStack(s.itemValue.Clone(), s.count - k) : ItemStack.Empty.Clone());
                        got += k;
                        need -= k;
                    }
                }
                if (xui != null && xui.PlayerInventory != null)
                {
                    xui.PlayerInventory.dispatchBackpackItemsChanged();
                    xui.PlayerInventory.dispatchToolbeltItemsChanged();
                }
            }
            catch (Exception e)
            {
                ScLog.Once("linked-takeback", e);
            }
            return got;
        }

        /// <summary>
        /// The player kept `left` of this item that the server never gave them: a settled cell of the view that holds it gives it
        /// up instead - a take the next batch asks the server for (judged like any other). Returns what is still left.
        /// </summary>
        private int TakeFromView(ItemStack want, int left)
        {
            string key = Key(want);
            bool any = false;
            for (int i = 0; i < map.Length && i < ItemGrid.Length && left > 0; i++)
            {
                if (!Settled(i)) continue;
                ItemStack here = ItemGrid[i];
                if (Key(here) != key) continue;
                int k = Math.Min(left, here.count);
                SetQuiet(i, here.count > k ? new ItemStack(here.itemValue.Clone(), here.count - k) : null);
                MarkDirty(i);
                left -= k;
                any = true;
            }
            if (any)
            {
                makeGood++;
                Notify();
            }
            return left;
        }

        // ---- AddItem / TryStackItem (3.2's IInventory logic, reached through the prefixes below) ----

        /// <summary>An empty cell the view can draw (below ShowLimit) with no change of its own; -1 when none.</summary>
        private int FreeCell()
        {
            int limit = Math.Min(ItemGrid.Length, ShowLimit);
            for (int i = 0; i < limit; i++)
            {
                if (!ItemGrid[i].IsEmpty() || dirtySet.Contains(i)) continue;
                ScStorage.Slot s = MapAt(i);
                if (s == null || (s.Index >= 0 && !InFlight(s) && (s.Stack == null || s.Stack.IsEmpty()))) return i;
            }
            return -1;
        }

        /// <summary>Round MP3: deposits not answered yet - their slots and their cells (a client's own estimate of the room left).</summary>
        private void PendingDeposits(out int slots, out int cells)
        {
            slots = 0;
            cells = 0;
            for (int i = 0; i < dirty.Count; i++)
            {
                int c = dirty[i];
                if (c < 0 || c >= ItemGrid.Length || ItemGrid[c].IsEmpty()) continue;
                ScStorage.Slot s = MapAt(c);
                if (s != null && s.Index >= 0 && s.Stack != null && !s.Stack.IsEmpty()) continue;
                slots++;
                cells += ScStorage.AreaOf(ItemGrid[c]);
            }
            if (flight == null) return;
            for (int i = 0; i < flight.Changes.Count; i++)
            {
                if (!flight.Changes[i].Deposit) continue;
                slots++;
                int cell = CellOf(flight.Changes[i].Slot);
                cells += cell >= 0 ? ScStorage.AreaOf(ItemGrid[cell]) : 1;
            }
        }

        /// <summary>
        /// Shift-click and move-all from the backpack: into a box first, then into the lowest free cell the view can draw.
        /// False - with the reason told - when no cell is free below ShowLimit or no box has room: nothing moves. Round MP3: on
        /// the host the server is asked first (answered at once), as before; a client puts it in the cell at once and the batch
        /// follows - a refusal gives it back (Refuse) - after a check against the room the server last reported.
        /// </summary>
        internal bool AddItemLinked(ItemStack _itemStack)
        {
            try
            {
                if (_itemStack == null || _itemStack.IsEmpty()) return false;
                // a cell the view can draw first: past ShowLimit the item would be stored out of sight
                int at = FreeCell();
                if (at < 0)
                {
                    Tell(ScStorage.FreeFor(GridDrawn) > 0 ? "scStorageViewFull" : ScStorage.ViewFullKey());   // round m1p
                    return false;
                }
                if (!ScCommunity.IsHost)
                {
                    int pendSlots, pendCells;
                    PendingDeposits(out pendSlots, out pendCells);
                    if (ScStorage.FreeSlots - pendSlots <= 0
                        || (ScStorage.RoomInCells && ScStorage.FreeCellsServer - pendCells < ScStorage.AreaOf(_itemStack)))
                    {
                        Tell(ScStorage.ViewFullKey());
                        return false;
                    }
                    SetMap(at, null);
                    SetQuiet(at, _itemStack);
                    MarkDirty(at);
                    Notify();
                    return true;
                }
                // the host: the boxes first, as before (the frame's own changes go ahead of it)
                if (flight == null && dirty.Count > 0) SendDirty();
                if (flight != null) { Tell("scStorageBusy"); return false; }
                string code = ScItemTx.Encode(_itemStack);
                if (code == null) { Tell("scStorageTooLarge"); return false; }
                var placeholder = new ScStorage.Slot { Pos = Vector3i.zero, Index = -1, Stack = ItemStack.Empty.Clone(), Code = "" };
                SetMap(at, placeholder);
                SetQuiet(at, _itemStack);
                bool ok = false;
                Transmit(new List<Change> { new Change { Slot = placeholder, Deposit = true, New = code } }, (f, r) =>
                {
                    ScStorage.Slot[] res;
                    if (r.HasHint("fs") && ReferenceEquals(ScStorage.Current, this)) ScStorage.SetFree(r.HintInt("fs"), r.HintInt("fc", -1));
                    if (r.Ok && ScStorageServer.ParseResults(r.HintStr("res", null), 1, out res)) { Accept(f, res); ok = true; return; }
                    // 3.4: say so, as a drop on a cell does. With RE Inventory each box takes an item only where its own grid fits
                    // the footprint, so a free cell in the view no longer means a box will take a rifle, and a shift-click that
                    // silently did nothing read as broken. Nothing moved: the backpack keeps it.
                    SetMap(at, null);
                    SetQuiet(at, null);
                    Tell(string.IsNullOrEmpty(r.Message) ? ScStorage.ViewFullKey() : r.Message, r.Sound);
                });
                if (flight != null) flight = null;           // a host answers inline; never leave this one in flight
                // the item is in a box now: a failing redraw must not report the add as failed (the caller would keep its
                // copy too)
                Notify();
                return ok;
            }
            catch (Exception e)
            {
                ScLog.Once("linked-additem", e);
                return false;
            }
        }

        /// <summary>
        /// Merges into the view's stacks of the same item. Round MP3: on the host each merge is asked first (answered at once),
        /// as before; on a client the stack is merged at once and the batch follows (a refusal gives it back).
        /// </summary>
        internal (bool anyMoved, bool allMoved) TryStackItemLinked(int _startIndex, ItemStack _itemStack)
        {
            bool any = false;
            try
            {
                if (_itemStack == null || _itemStack.IsEmpty()) return (false, false);
                bool host = ScCommunity.IsHost;
                for (int i = Math.Max(0, _startIndex); i < ItemGrid.Length && _itemStack.count > 0; i++)
                {
                    if (!Settled(i)) continue;
                    ItemStack s = ItemGrid[i];
                    ScStorage.Slot box = MapAt(i);
                    if (s.IsEmpty() || s.itemValue.type != _itemStack.itemValue.type) continue;
                    int n = _itemStack.count;
                    if (!s.CanStackPartly(ref n) || n <= 0) continue;
                    ItemStack merged = s.Clone();
                    merged.count += n;
                    if (!host)
                    {
                        SetQuiet(i, merged);
                        MarkDirty(i);
                        _itemStack.count -= n;
                        any = true;
                        continue;
                    }
                    if (!AskNow(new List<Change> { Cas(box, merged) })) continue;
                    SetQuiet(i, merged);
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

        /// <summary>A compare-and-set for the host's ask-first paths; New is null when the stack is too large to send (AskNow
        /// refuses it - never "" for it, which would empty the slot).</summary>
        private static Change Cas(ScStorage.Slot s, ItemStack next)
        {
            return new Change { Slot = s, Expect = s.Code ?? "", New = ScItemTx.Encode(next) };
        }

        /// <summary>
        /// The host's ask-first path (TryStackItem, the sort's merges): sends these changes as one batch, answered at once. True
        /// when written (the slots take the server's codes). Refused: nothing moved here either, so the cells only take their
        /// boxes' content as it is now - no settle.
        /// </summary>
        private bool AskNow(List<Change> changes)
        {
            for (int i = 0; i < changes.Count; i++) if (changes[i].New == null) return false;
            bool ok = false;
            Transmit(changes, (f, r) =>
            {
                ScStorage.Slot[] res;
                bool hasRes = ScStorageServer.ParseResults(r.HintStr("res", null), f.Changes.Count, out res);
                if (r.HasHint("fs") && ReferenceEquals(ScStorage.Current, this)) ScStorage.SetFree(r.HintInt("fs"), r.HintInt("fc", -1));
                if (r.Ok && hasRes) { Accept(f, res); ok = true; return; }
                if (!hasRes) return;
                for (int k = 0; k < f.Changes.Count; k++)
                {
                    ScStorage.Slot s = f.Changes[k].Slot;
                    bool gone = res[k].Code == ScStorageServer.ResGone;
                    s.Code = gone ? "" : res[k].Code;
                    s.Stack = gone ? ItemStack.Empty.Clone() : ScItemTx.Decode(res[k].Code);
                    int cell = CellOf(s);
                    if (cell < 0) continue;
                    SetQuiet(cell, s.Stack);
                    if (s.Stack.IsEmpty()) SetMap(cell, null);
                }
                Notify();
            });
            if (flight != null) flight = null;           // a host answers inline; never leave this one in flight
            return ok;
        }

        // ---- Sort (reached through Patch_LinkedSort) ----

        /// <summary>
        /// The Sort button: partial stacks of an item are merged, each write going through to its box, and no other stack
        /// moves in any box. Vanilla's ItemGrid.Sort reorders the view's cells, then raises SlotChanged for every one, so the
        /// mirror wrote each cell's new stack into whichever box slot stood behind that cell, with no room check: stacks were
        /// dealt across the boxes by position, a box's free cells changed with nothing added, and a box could go past its grid
        /// (user's log, 2026-10-30: a 28-cell land claim block holding 31 or 32, reached past Deposit's footprint test).
        /// On Grid Inventory's grid the order vanilla would have made never showed anyway: its repack after the button (area,
        /// then item type) decides what the player sees, and it runs on these slots as they are. Vanilla's grid shows slot
        /// order, so there the view's own cells are sorted (SortCells) - round MP3: once no change is on its way (the cells move).
        /// </summary>
        internal void SortView()
        {
            MergeStacks();
            if (GridDrawn) return;
            if (Busy) sortWanted = true;
            else SortCells();
        }

        /// <summary>Vanilla's order on the view alone: the cells and the map behind them move together with the mirror off, so
        /// each stack keeps its box slot and no box is written. The window redraws from the button's NotifyListeners.</summary>
        private void SortCells()
        {
            var order = new List<int>();
            for (int i = 0; i < ItemGrid.Length; i++)
            {
                if (!ItemGrid[i].IsEmpty()) order.Add(i);
            }
            order.Sort((a, b) => ItemStackGrid.CompareForSort(ItemGrid[a], ItemGrid[b]));
            var stacks = new ItemStack[ItemGrid.Length];
            var slots = new ScStorage.Slot[ItemGrid.Length];
            for (int i = 0; i < order.Count; i++)
            {
                stacks[i] = ItemGrid[order[i]].Clone();
                slots[i] = MapAt(order[i]);
            }
            quiet = true;
            try
            {
                ItemGrid.SetItems(stacks, false);
            }
            finally
            {
                quiet = false;
            }
            map = slots;
        }

        /// <summary>Same stacking rule as vanilla's CompactSlots (never an item with quality), plus CanStackPartlyWith's own
        /// tests. A merge that would make the target stack's footprint bigger is left alone (RE Inventory's stack footprints
        /// option, off by default). Round MP3: only between settled cells (no change of their own on its way).</summary>
        private void MergeStacks()
        {
            try
            {
                int merged = 0;
                for (int i = 0; i < ItemGrid.Length; i++)
                {
                    if (!Settled(i)) continue;
                    ScStorage.Slot to = MapAt(i);
                    ItemStack a = ItemGrid[i];
                    ItemClass ic = a.IsEmpty() ? null : a.itemValue.ItemClass;
                    if (ic == null || ic.HasQuality) continue;
                    for (int k = i + 1; k < ItemGrid.Length; k++)
                    {
                        if (!Settled(k)) continue;
                        ScStorage.Slot from = MapAt(k);
                        ItemStack b = ItemGrid[k];
                        int n;
                        if (b.IsEmpty() || !a.CanStackPartlyWith(b, out n) || n <= 0) continue;
                        if (MergeInto(i, to, k, from, n)) merged++;
                        a = ItemGrid[i];
                    }
                }
                if (merged > 0 && ScConfig.Verbose) ScLog.Verbose("linked storage: sort merged " + merged + " partial stack(s)");
            }
            catch (Exception e)
            {
                ScLog.Once("linked-sort", e);
            }
        }

        /// <summary>Moves _n from cell _k's stack to cell _i's. On the host both boxes are written as one batch first (answered at
        /// once; a refusal changes nothing). On a client both cells change at once and go in the same batch (all or nothing).</summary>
        private bool MergeInto(int _i, ScStorage.Slot _to, int _k, ScStorage.Slot _from, int _n)
        {
            ItemStack more = ItemGrid[_i].Clone();
            more.count += _n;
            if (ScStorage.AreaOf(more) > ScStorage.AreaOf(ItemGrid[_i])) return false;
            ItemStack less = ItemGrid[_k].Clone();
            less.count -= _n;
            if (less.count <= 0) less = ItemStack.Empty.Clone();
            if (ScCommunity.IsHost)
            {
                if (!AskNow(new List<Change> { Cas(_from, less), Cas(_to, more) })) return false;
            }
            else
            {
                MarkDirty(_k);
                MarkDirty(_i);
            }
            SetQuiet(_i, more);
            SetQuiet(_k, less);
            if (less.IsEmpty() && ScCommunity.IsHost) SetMap(_k, null);
            return true;
        }

        // ---- the prefixes ----

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

        /// <summary>ItemStackGrid.Sort on the view's own grid: SortView instead (the boxes keep their stacks). Every other
        /// grid runs vanilla's. XUiC_LootWindow's button calls this and then NotifyListeners, and RE Inventory's repack
        /// (a postfix on the button) still tidies what the player sees.</summary>
        [HarmonyPatch]
        public static class Patch_LinkedSort
        {
            public static MethodBase Target()
            {
                return AccessTools.DeclaredMethod(typeof(ItemStackGrid), "Sort", Type.EmptyTypes);
            }

            [HarmonyPrepare]
            public static bool Prepare()
            {
                try
                {
                    if (Target() != null) return true;
                    ScLog.Err("ItemStackGrid.Sort() not found (game update?) - Sort in linked storage moves stacks between boxes with no room check");
                }
                catch (Exception e) { ScLog.Once("linked-bind-sort", e); }
                return false;
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return Target(); }

            [HarmonyPrefix]
            public static bool Prefix(ItemStackGrid __instance)
            {
                ScLinkedContainer c = ScStorage.Current;
                if (c == null || !ReferenceEquals(c.ItemGrid, __instance)) return true;
                c.SortView();
                return false;
            }
        }
    }
}
