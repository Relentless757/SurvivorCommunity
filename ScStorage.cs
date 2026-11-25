using System;
using System.Collections.Generic;
using Platform;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Linked storage: every player-placed storage box inside the community zone, read as one list of
    /// stacks. Each entry remembers the box (block position) and the slot it came from, so the window's
    /// cells write straight back into the real box slot (take, swap, merge), and a drop on an empty cell
    /// goes into the first box with a free slot. Boxes in unloaded chunks are out of reach - the scan walks
    /// the loaded chunks' tile entity lists, which is cheap. The packing station is left out on purpose.
    ///
    /// Round MP3 (part L, multiplayer): the server owns the boxes, every player (the host's own included) has a view.
    ///  - ScStorageServer (the server) scans and claims the boxes for one player's view, and writes them: every cell change
    ///    is a compare-and-set against the box slot, judged in batches (all or nothing).
    ///  - This class holds THIS machine's view (Slots, the counts, Current) and opens it in the loot window when the server's
    ///    view arrives (OnView); RequestOpen asks for one. ScLinkedContainer turns the window's changes into batches and puts the
    ///    player's cursor and inventory back when the server refuses one. Pump (every frame, ScStoragePump) drives it.
    ///  - The NPC writers below (DepositInZone, TopUpInZone, TakeOneInZone) run on the server and skip a box a player has open
    ///    and a box locked against the community (F3-F5, single player too). Round MP3 (review storage M2): a box a linked storage
    ///    view shows is written like any other, and the slots written are pushed to the views that show them
    ///    (ScStorageServer.PushWritten); a view's change made against the old content is refused by the compare-and-set.
    /// The grid bridge's room delegates (BoxHasRoomFor, BoxFreeCells) run on the server only, where the boxes live; the view
    /// gets the server's free room in numbers (FreeSlots, FreeCellsServer).
    /// </summary>
    public static class ScStorage
    {

        public sealed class Slot
        {
            public Vector3i Pos;
            /// <summary>The slot in its box; round MP3: -1 = a deposit the server has not placed yet.</summary>
            public int Index;
            /// <summary>The box slot's content as the server last confirmed it (the cell can differ while a change is on its way).</summary>
            public ItemStack Stack;
            /// <summary>Round MP3: Stack as the server encoded it (ScItemTx.Encode, "" = empty) - what a change sends as "what I
            /// saw" for the server's compare-and-set.</summary>
            public string Code = "";
        }

        /// <summary>Round MP3: what a view shows - the home zone's boxes at home, an outpost's own boxes, or the home's boxes
        /// from an outpost (ScHomeLink).</summary>
        public const int KindHome = 0, KindOutpost = 1, KindHomeLink = 2;

        /// <summary>This machine's open view: the stacks the server sent, sorted by name here (in this machine's language).</summary>
        public static readonly List<Slot> Slots = new List<Slot>();
        private static int viewBoxes;
        public static int Boxes { get { return viewBoxes; } }
        public static int FreeSlots;
        /// <summary>Round MP3: the boxes' free cells as the server's grid bridge counts them; -1 = no bridge there (a slot is a cell).</summary>
        public static int FreeCellsServer = -1;
        public static int SkippedChunks;
        /// <summary>Round MP3: boxes left out of the view - open by someone, or locked against this player.</summary>
        public static int SkippedBoxes;

        // ---- grid bridge --------------------------------------------------------------------------
        // With GridInventory a box holds an AREA, not a stack count: a free slot is worth nothing when no
        // run of free cells fits the item's footprint. The zzzz_GridInv_SurvivorCommunity patch registers
        // these three; without it every stack is one cell and a box with a free slot takes anything, as before.
        // Round MP3: BoxHasRoomFor and BoxFreeCells are asked on the server only (ScStorageServer), where the box lives.

        /// <summary>Cells a stack covers on a grid; null = one.</summary>
        public static Func<ItemStack, int> StackArea;
        /// <summary>Whether a box's grid can seat this stack somewhere; null = yes. Its free slot is checked here regardless.</summary>
        public static Func<TEFeatureStorage, ItemStack, bool> BoxHasRoomFor;
        /// <summary>Free cells of a box's grid; null = its free slots.</summary>
        public static Func<TEFeatureStorage, int> BoxFreeCells;

        // Two more, both optional (an older bridge leaves them null and the fallbacks below apply):
        /// <summary>Does Grid Inventory draw this container (container grids on, not excluded)? null = yes whenever Grid Inventory is loaded.</summary>
#if SC_V34
        // 3.4: the loot window's container is a TEFeatureStorage (ITileEntityLootable is gone): the bridge's delegate is retyped
        public static Func<TEFeatureStorage, bool> GridDraws;
#else
        public static Func<ITileEntityLootable, bool> GridDraws;
#endif
        /// <summary>Item widgets of the loot window's grid. Grid Inventory draws a widget only for a slot that holds
        /// something - empty cells are its scrolling cell rects - so this caps STACKS on show, not cells. null/0 = 260.</summary>
        public static Func<int> GridWidgetPool;

        /// <summary>Rows when Grid Inventory scrolls the view: a sanity ceiling, 9,000 cells.</summary>
        public const int GridRowsMax = 1000;
        /// <summary>Rows with Grid Inventory when the bridge does not say whether it draws the view (an older
        /// bridge, or none): the previous fixed size, safe on either grid Grid Inventory ships.</summary>
        public const int LegacyGridRows = 28;
        /// <summary>Rows on vanilla's loot grid, which neither scrolls nor has a widget past its 90th slot.</summary>
        public const int VanillaRows = 10;
        public const int DefaultWidgetPool = 260;
        /// <summary>
        /// Round s1: SC's own linked storage window group (Config/XUi_InGame/xui.xml, windowScLinkedStorage from
        /// Tools/generate_linked_storage_ui.py): vanilla's looting window with 360 cells in a scrolling view, opened instead
        /// of "looting" when RE Inventory is not loaded (OwnWindowFor).
        /// </summary>
        public const string OwnWindowGroup = "sclinkedstorage";

        public static int AreaOf(ItemStack stack)
        {
            if (StackArea == null || stack == null || stack.IsEmpty()) return 1;
            try { int a = StackArea(stack); return a < 1 ? 1 : a; }
            catch (Exception e) { ScLog.Once("storage-area", e); return 1; }
        }

        /// <summary>The real room to deposit into, in cells: the boxes' free cells by the server's bridge, else FreeSlots.</summary>
        public static int FreeCells()
        {
            return FreeCellsServer >= 0 ? FreeCellsServer : Math.Max(0, FreeSlots);
        }

        /// <summary>Round MP3: the view's free room is counted in cells (the server has the grid bridge).</summary>
        public static bool RoomInCells { get { return FreeCellsServer >= 0; } }

        /// <summary>Room in the unit the view is drawn in: cells on Grid Inventory's grid, slots on vanilla's.</summary>
        public static int FreeFor(bool gridDrawn)
        {
            return gridDrawn ? FreeCells() : Math.Max(0, FreeSlots);
        }

        internal static int Compare(Slot a, Slot b)
        {
            string na = Name(a.Stack), nb = Name(b.Stack);
            int c = string.Compare(na, nb, StringComparison.OrdinalIgnoreCase);
            if (c != 0) return c;
            c = b.Stack.count.CompareTo(a.Stack.count);
            if (c != 0) return c;
            c = a.Pos.x.CompareTo(b.Pos.x); if (c != 0) return c;
            c = a.Pos.z.CompareTo(b.Pos.z); if (c != 0) return c;
            return a.Index.CompareTo(b.Index);
        }

        private static string Name(ItemStack s)
        {
            try
            {
                ItemClass ic = s != null && s.itemValue != null ? s.itemValue.ItemClass : null;
                return ic != null ? ic.GetLocalizedItemName() : "";
            }
            catch (Exception) { return ""; }
        }

        /// <summary>The live storage feature at a box position, or null when the chunk is gone.</summary>
        public static TEFeatureStorage Resolve(Vector3i pos)
        {
            try
            {
                World w = ScCommunity.World;
                if (w == null) return null;
                TileEntityComposite tc = w.GetTileEntity(pos) as TileEntityComposite;
                return tc != null ? tc.GetFeature<TEFeatureStorage>() : null;
            }
            catch (Exception) { return null; }
        }

        /// <summary>
        /// Round MP3: the boxes linked storage links (and NPC jobs fill) - player-placed storage, never the packing station (a
        /// workbench, not a store) nor a land claim block's own storage. The zone or area test is the caller's.
        /// </summary>
        internal static bool Linkable(TileEntityComposite tc, TEFeatureStorage st)
        {
            if (tc == null || st == null || st.ItemGrid == null) return false;
            if (tc.GetFeature<TEFeatureSCPacking>() != null) return false;      // the packing station is a workbench, not a store
            if (!(tc.PlayerPlaced || IsPlayerStorage(st))) return false;        // world loot is not the community's
            // a land claim block's own storage (keystoneBlock's 7x4) is not linked: in one grid with the crates
            // nothing shows which cells are its own (user, 2026-10-31). It stays on the block, opened there:
            // an outpost's packing strip rides it (ScStockpile.StationOf), and a saved block's items would be lost
            // with the feature (TileEntityComposite.read skips a payload the block no longer defines).
            if (tc.GetFeature<TEFeatureLandClaim>() != null) return false;
            return true;
        }

        // ------------------------------------------------------------------ NPC writers (the server) ----

        /// <summary>
        /// Round MP3 (F3-F5): may an NPC job write this box? Not while a player has it open (their copy would overwrite the job's
        /// write, and the job's take would come back), and not when it is locked against the community (a friend's
        /// password-locked crate). Round MP3 (review storage M2): a box a linked storage view shows is no longer skipped - an open
        /// view kept every job out of the whole base, and ScJobs.CarryWindow then had nowhere to put a member's overflow. The
        /// view's changes are compare-and-set against the box, and the job's writes are pushed to it (PushWritten).
        /// </summary>
        private static bool NpcMayUse(Vector3i pos, TEFeatureStorage st)
        {
            if (ScStorageServer.OpenBySomeone(st)) return false;
            return !ScStorageServer.LockedAgainst(st, CommunityUserId());
        }

        /// <summary>Round MP3 (review storage M2): the slots an NPC writer wrote, for the views that show them (never throws).</summary>
        private static void Pushed(List<KeyValuePair<Vector3i, int>> written)
        {
            if (written != null && written.Count > 0) ScStorageServer.PushWritten(written);
        }

        /// <summary>
        /// Who the NPCs work for, for a box's lock: the community's owner (platform id); a save whose owner id is not known yet
        /// (from before round MP0, until the owner is online once) counts the host's own player. Null (a dedicated server with no
        /// owner id): any lock keeps the jobs out.
        /// </summary>
        private static PlatformUserIdentifierAbs CommunityUserId()
        {
            try
            {
                string id = ScRegistry.Community != null ? ScRegistry.Community.OwnerId : null;
                PlatformUserIdentifierAbs u;
                if (!string.IsNullOrEmpty(id) && PlatformUserIdentifierAbs.TryFromCombinedString(id, out u)) return u;
                return ScCommunity.LocalPlayer != null ? PlatformManager.InternalLocalUserIdentifier : null;
            }
            catch (Exception e)
            {
                ScLog.Once("storage-community-id", e);
                return null;
            }
        }

        /// <summary>
        /// Puts one stack into the first box of the home zone's linked storage with a free slot (and, with the grid bridge,
        /// room for its footprint): what a member hands back when they keep a weapon you gave them (ScArms). The boxes are
        /// walked here rather than through a view's scan. Loaded chunks only. False when no box has room. Round MP3: a box a
        /// player has open, or a box locked against the community, is skipped (NpcMayUse); the slot written goes to the linked
        /// storage views that show its box (review storage M2).
        /// </summary>
        public static bool DepositInZone(ItemStack stack, out Vector3i box)
        {
            box = Vector3i.zero;
            try
            {
                if (stack == null || stack.IsEmpty()) return false;
                foreach (KeyValuePair<Vector3i, TEFeatureStorage> b in ZoneBoxes())
                {
                    TEFeatureStorage st = b.Value;
                    if (BoxHasRoomFor != null)
                    {
                        bool room;
                        try { room = BoxHasRoomFor(st, stack); }
                        catch (Exception e) { ScLog.Once("storage-room", e); room = true; }
                        if (!room) continue;
                    }
                    ItemStack[] items = st.ItemGrid.items;
                    for (int k = 0; k < items.Length; k++)
                    {
                        if (items[k] != null && !items[k].IsEmpty()) continue;
                        st.UpdateSlot(k, stack.Clone());
                        st.SetModified();
                        ScStorageServer.Invalidate(b.Key);
                        box = b.Key;
                        Pushed(new List<KeyValuePair<Vector3i, int>> { new KeyValuePair<Vector3i, int>(b.Key, k) });   // round MP3 (review storage M2)
                        return true;
                    }
                }
                return false;
            }
            catch (Exception e)
            {
                ScLog.Once("storage-deposit-zone", e);
                return false;
            }
        }

        /// <summary>
        /// Round 29a (J29): the boxes DepositInZone fills - player-placed storage inside the home zone, loaded, never the packing
        /// station or a land claim block's own storage - with their positions. Round MP3: only boxes an NPC may use (NpcMayUse).
        /// </summary>
        private static IEnumerable<KeyValuePair<Vector3i, TEFeatureStorage>> ZoneBoxes()
        {
            World w = ScCommunity.World;
            if (w == null || !ScZone.HasHome) yield break;
            int xMin, xMax, zMin, zMax;
            ScZone.Bounds(out xMin, out xMax, out zMin, out zMax);
            for (int cx = World.toChunkXZ(xMin); cx <= World.toChunkXZ(xMax); cx++)
            {
                for (int cz = World.toChunkXZ(zMin); cz <= World.toChunkXZ(zMax); cz++)
                {
                    Chunk chunk = w.GetChunkSync(cx, cz) as Chunk;
                    var tes = chunk != null ? chunk.tileEntities : null;
                    if (tes == null || tes.list == null) continue;
                    for (int i = 0; i < tes.list.Count; i++)
                    {
                        TileEntityComposite tc = tes.list[i] as TileEntityComposite;
                        TEFeatureStorage st = tc != null ? tc.GetFeature<TEFeatureStorage>() : null;
                        if (!Linkable(tc, st)) continue;
                        Vector3i pos = tc.ToWorldPos();
                        if (!ScZone.Contains(pos)) continue;
                        if (!NpcMayUse(pos, st)) continue;           // round MP3 (F3-F5)
                        yield return new KeyValuePair<Vector3i, TEFeatureStorage>(pos, st);
                    }
                }
            }
        }

        /// <summary>
        /// Round 29a (J29): adds what it can of a plain stackable item (no quality) onto stacks of the same item already in the
        /// home zone's linked storage, up to its stack size, so a member's home job - a few logs or a piece of meat every few
        /// seconds - fills one crate stack instead of a new slot each time. Returns how many went in; the rest is the caller's
        /// (DepositInZone, into an empty slot). Takes nothing from the stack passed. Round 29 review L3: a fault part way still
        /// returns what already went in (each top-up counted the moment its slot is written), so the caller never puts it in twice.
        /// </summary>
        public static int TopUpInZone(ItemStack stack)
        {
            int count = 0, left = 0;
            List<KeyValuePair<Vector3i, int>> written = null;       // round MP3 (review storage M2): pushed to the views (finally)
            try
            {
                if (stack == null || stack.IsEmpty() || stack.itemValue == null) return 0;
                ItemClass ic = ItemClass.GetForId(stack.itemValue.type);
                if (ic == null || !ic.CanStack() || ic.HasQuality) return 0;
                int max = Math.Max(1, ic.Stacknumber.Value);
                count = left = stack.count;
                foreach (KeyValuePair<Vector3i, TEFeatureStorage> b in ZoneBoxes())
                {
                    ItemStack[] items = b.Value.ItemGrid.items;
                    for (int k = 0; k < items.Length && left > 0; k++)
                    {
                        ItemStack s = items[k];
                        if (s == null || s.IsEmpty() || s.itemValue == null || s.itemValue.type != stack.itemValue.type || s.count >= max) continue;
                        int add = Math.Min(left, max - s.count);
                        b.Value.UpdateSlot(k, new ItemStack(s.itemValue.Clone(), s.count + add));
                        left -= add;
                        if (written == null) written = new List<KeyValuePair<Vector3i, int>>();
                        written.Add(new KeyValuePair<Vector3i, int>(b.Key, k));
                        b.Value.SetModified();
                        ScStorageServer.Invalidate(b.Key);
                    }
                    if (left <= 0) break;
                }
                return count - left;
            }
            catch (Exception e)
            {
                ScLog.Once("storage-topup-zone", e);
                return count - left;
            }
            finally
            {
                Pushed(written);
            }
        }

        /// <summary>Round 29a (J29): takes one item of this type out of the home zone's linked storage (a member's seed to replant a crop). True when one was taken.</summary>
        public static bool TakeOneInZone(int type, out Vector3i box)
        {
            box = Vector3i.zero;
            try
            {
                if (type <= 0) return false;
                foreach (KeyValuePair<Vector3i, TEFeatureStorage> b in ZoneBoxes())
                {
                    ItemStack[] items = b.Value.ItemGrid.items;
                    for (int k = 0; k < items.Length; k++)
                    {
                        ItemStack s = items[k];
                        if (s == null || s.IsEmpty() || s.itemValue == null || s.itemValue.type != type) continue;
                        b.Value.UpdateSlot(k, s.count > 1 ? new ItemStack(s.itemValue.Clone(), s.count - 1) : ItemStack.Empty.Clone());
                        b.Value.SetModified();
                        ScStorageServer.Invalidate(b.Key);
                        box = b.Key;
                        Pushed(new List<KeyValuePair<Vector3i, int>> { new KeyValuePair<Vector3i, int>(b.Key, k) });   // round MP3 (review storage M2)
                        return true;
                    }
                }
                return false;
            }
            catch (Exception e)
            {
                ScLog.Once("storage-take-zone", e);
                return false;
            }
        }

        /// <summary>Stacks that still hold something (takes leave empty entries until the next view).</summary>
        public static int LiveStacks()
        {
            int n = 0;
            for (int i = 0; i < Slots.Count; i++) if (Slots[i].Stack != null && !Slots[i].Stack.IsEmpty()) n++;
            return n;
        }

        /// <summary>
        /// 3.2's TEFeatureStorage.bPlayerStorage on 3.4: a box with an owner (Parent.Owner, as 3.2's getter asked first), or one
        /// whose stored flag says so - 3.4 keeps that flag on the grid (ItemGrid.PlayerOwned, set when a player places it).
        /// </summary>
        public static bool IsPlayerStorage(TEFeatureStorage st)
        {
            return st != null && ((st.Parent != null && st.Parent.Owner != null) || (st.ItemGrid != null && st.ItemGrid.PlayerOwned));
        }

        /// <summary>Whether Grid Inventory will draw this container: the bridge's answer, else "it is loaded".</summary>
        public static bool GridDrawsIt(TEFeatureStorage te)
        {
            if (GridDraws != null)
            {
                try { return GridDraws(te); }
                catch (Exception e) { ScLog.Once("storage-griddraws", e); }
            }
            return GridInventoryLoaded();
        }

        /// <summary>The loot window grid's item widgets (the most stacks it can show).</summary>
        public static int WidgetPool()
        {
            if (GridWidgetPool != null)
            {
                try { int n = GridWidgetPool(); if (n > 0) return n; }
                catch (Exception e) { ScLog.Once("storage-widgetpool", e); }
            }
            return DefaultWidgetPool;
        }

        /// <summary>
        /// Sizes the view for whoever draws it. Grid Inventory scrolls, and draws empty cells without widgets,
        /// so every free cell can be shown (up to GridRowsMax rows) and only the STACKS are capped - by its
        /// widget pool, less two rows kept free for what gets dropped in while the window is open (slot i needs
        /// widget i, so nothing may enter the view at or past the pool: ShowLimit). Vanilla's grid has a widget
        /// per slot and no scrolling: 10 rows, as before - and on 3.4, without Grid Inventory, SC's own window
        /// (OwnWindowGroup) scrolls over its 360 widgets: 40 rows of 9 (45 of 8 in its AshfallUI look, round s1b: the view
        /// takes the columns the window's grid declares, ViewCols), every stack and free slot up to 360. Without
        /// the bridge's GridDraws (an older bridge) the previous 28 rows. LinkedStorageRows only lowers these.
        /// </summary>
        public static void SizeFor(ScLinkedContainer c)
        {
            ScConfig.EnsureParsed();
            bool gi = GridDrawsIt(c);
            bool known = GridDraws != null;                // the bridge can tell; without it keep the old fixed size
            bool own = c.OwnWindow;                        // round s1: SC's own window, set by OpenView (never with Grid Inventory)
            int rows = !gi ? (own ? GridRowsMax : VanillaRows) : (known ? GridRowsMax : LegacyGridRows);
            // round s1b: in SC's own window the view takes the columns its grid declares (9; 8 in AshfallUI's look, as wide as
            // AshfallUI's loot window for the widest vanilla crate, so INSPECT and the inventory fit beside it), read ONCE by
            // OwnWindowFor and handed over in c.ViewCols by OpenView - a second read that failed would fall back to 9 in a
            // window 8 wide, and SetSlots would then write 9 into its grid for every later open. Vanilla's looting window and
            // RE Inventory's grid keep Cols: the looting grid's cols change with every crate opened there.
            int cols = own ? Math.Max(1, c.ViewCols) : ScLinkedContainer.Cols;
            c.ViewCols = cols;
            // 3.4: vanilla's loot refresh (XUiC_LootContainer.OnTileEntityChanged) walks the CONTAINER's grid and indexes the
            // window's widgets with the same counter, unguarded (3.2 walked the widgets) - a view with more cells than the
            // window has widgets throws on every write, after the write reached a box but before the cell took the drop. And
            // RE Inventory 3.3+ draws one cell per container slot with an item widget per slot (its pool of 260; 3.2's Grid
            // Inventory drew widgets for stacks only and scrolled over empty cells), so its grid cannot draw a cell past its
            // pool either. So on 3.4 the view never has more cells than the window's widgets, with or without the bridge
            // (90 on vanilla's 10 x 9 grid, 360 in SC's own window) - nor, with the bridge, than the grid's own pool. The
            // widgets are counted in the window the view opens in (round s1: before, the first loot window found).
            {
                int widgets = LootWidgets(own ? OwnWindowGroup : XUiC_LootWindowGroup.ID);
                if (gi && known)
                {
                    int pool34 = WidgetPool();
                    widgets = widgets > 0 ? Math.Min(widgets, pool34) : pool34;
                }
                if (widgets > 0) rows = Math.Min(rows, Math.Max(1, widgets / cols));
                else if (gi || own) rows = Math.Min(rows, VanillaRows);    // unknown: never past vanilla's grid
            }
            if (ScConfig.LinkedStorageRows > 0) rows = Math.Min(rows, ScConfig.LinkedStorageRows);
            c.RowsCap = Math.Max(1, rows);
            int pool = WidgetPool();
            c.StackCap = gi && known ? Math.Max(ScLinkedContainer.Cols, pool - 2 * ScLinkedContainer.Cols) : 0;
            c.ShowLimit = gi && known ? pool : int.MaxValue;
            c.GridDrawn = gi;
        }

        /// <summary>3.4: the item widgets of a loot window group's grid - "looting" (vanilla's looting window: 90, or RE
        /// Inventory's) or OwnWindowGroup (360) - or 0 when the group is not found.</summary>
        private static int LootWidgets(string _groupId)
        {
            try
            {
                LocalPlayerUI ui = LocalPlayerUI.GetUIForPrimaryPlayer();
                return LootWidgets(ui != null && ui.xui != null ? ui.xui.FindWindowGroupByName(_groupId) as XUiC_LootWindowGroup : null);
            }
            catch (Exception e)
            {
                ScLog.Once("storage-lootwidgets", e);
                return 0;
            }
        }

        /// <summary>3.4: the item widgets of the group's loot window grid (the XUiC_LootContainer its XUiC_LootWindow binds), 0
        /// without one.</summary>
        private static int LootWidgets(XUiC_LootWindowGroup _group)
        {
            XUiC_LootWindow lw = _group != null ? _group.lootWindow : null;
            XUiC_ItemStack[] w = lw != null && lw.lootContainer != null ? lw.lootContainer.GetItemStackControllers() : null;
            return w != null ? w.Length : 0;
        }

        /// <summary>Round s1b: the columns of a loot window group's grid, 0 without one. Asked of SC's own window only, once per
        /// open (OwnWindowFor): vanilla's XUiC_LootContainer.SetSlots gives the grid each opened container's width, and only
        /// SC's view opens there, laid out in these columns - so they stay the ones the window's XML declares.</summary>
        private static int LootCols(XUiC_LootWindowGroup _group)
        {
            XUiC_LootWindow lw = _group != null ? _group.lootWindow : null;
            XUiV_Grid g = lw != null && lw.lootContainer != null ? lw.lootContainer.grid : null;
            return g != null ? g.Columns : 0;
        }

        /// <summary>
        /// Round s1: SC's own linked storage window (OwnWindowGroup) for a view drawn on vanilla's loot grid, i.e. without RE
        /// Inventory. Vanilla's looting window has 90 item widgets, and SizeFor never gives the view more slots than its window
        /// has widgets, so there the view stopped at 90 slots: with 90 stacks the boxes' free room had no cell at all (the
        /// user's report, 2026-11-14: "90 stack(s), 102 free cell(s) ... 102 free cell(s) not shown"). This window is
        /// vanilla's looting window - its controllers, so every move is the game's own - with 360 cells in a scrolling view
        /// (Tools/generate_linked_storage_ui.py). Null with RE Inventory loaded (its grid draws the view in the looting window,
        /// as before), and when the window is missing or smaller than one row (the looting window and its 90 slots, as before,
        /// with a warning once). Round s1b: _cols is the window grid's columns (> 0 whenever a group is returned), read here
        /// once and handed to SizeFor through ScLinkedContainer.ViewCols; 0 with null.
        /// </summary>
        private static XUiC_LootWindowGroup OwnWindowFor(EntityPlayerLocal player, out int _cols)
        {
            _cols = 0;
            if (GridInventoryLoaded()) return null;
            try
            {
                XUi xui = player != null && player.PlayerUI != null ? player.PlayerUI.xui : null;
                XUiC_LootWindowGroup g = xui != null ? xui.FindWindowGroupByName(OwnWindowGroup) as XUiC_LootWindowGroup : null;
                int cols = LootCols(g);
                if (g != null && cols > 0 && LootWidgets(g) >= cols)
                {
                    _cols = cols;
                    return g;
                }
                ScLog.Once("storage-ownwindow-missing", "linked storage: window group '" + OwnWindowGroup + "' or its grid not found (xui.xml, windows.xml)"
                           + " - linked storage opens in the looting window, 90 slots at most");
            }
            catch (Exception e)
            {
                ScLog.Once("storage-ownwindow", e);
            }
            return null;
        }

        /// <summary>
        /// Is GridInventory driving the loot window? Its mod folder / ModInfo name is not fixed (it ships as
        /// "RE Inventory"), so the assembly's grid class is the test - and the bridge's hooks, when installed.
        /// </summary>
        public static bool GridInventoryLoaded()
        {
            if (BoxFreeCells != null || StackArea != null) return true;
            try { if (Type.GetType("GridInv.UI.XUiC_ContainerGrid, GridInventory") != null) return true; } catch (Exception) { }
            try { return ModManager.GetMod("GridInventory") != null || ModManager.GetMod("REInventory") != null || ModManager.GetMod("oREInventory") != null; } catch (Exception) { return false; }
        }

        /// <summary>The open container (null when the loot window shows something else or is closed).</summary>
        public static ScLinkedContainer Current;

        /// <summary>3.4: the cells the open view should draw (ScLinkedContainer.CellsNow), 0 = no limit. For the bridge, which
        /// references no SCore and so cannot touch ScLinkedContainer, a subclass of SCore's container.</summary>
        public static int CurrentCells()
        {
            return Current != null ? Current.CellsNow() : 0;
        }

        /// <summary>
        /// SCore's EntityUtilities.OpenContainer with the title passed on its own: that helper hands the loot window
        /// te.lootListName as the title, so a view with a title of its own would need an unknown loot list key.
        /// </summary>
        /// <param name="own">SC's own window (round s1, OwnWindowFor; the view was sized for it); null = vanilla's looting window.</param>
        private static void OpenLootWindow(EntityPlayerLocal player, ScLinkedContainer te, string title, XUiC_LootWindowGroup own)
        {
            LocalPlayerUI ui = player.PlayerUI;
            ui.windowManager.CloseAllOpenModalWindows(null, false);
            XUiC_LootWindowGroup group = own;
            if (group == null)
            {
                GUIWindow window = ui.windowManager.GetWindow("looting");
                group = window is XUiWindowGroup ? ((XUiWindowGroup)window).Controller as XUiC_LootWindowGroup : null;
            }
            te.bTouched = true;                         // 3.4: the grid's touched stamp
            // 3.4: straight in - the open timer now runs only for _firstTimeTouched (SCore's OpenContainer passes false too)
            if (group == null) { EntityUtilities.OpenContainer(player, te); return; }
            group.OpenLooting(title, te, false);
        }

        // ------------------------------------------------------------------ the view on this machine (round MP3) ----

        /// <summary>The open view's session (the server's id), what it shows (Kind*), the claim it stands on, and its title.</summary>
        public static int CurrentSession = -1;
        public static int CurrentKind = KindHome;
        public static Vector3i CurrentAnchorPos;
        public static string CurrentTitle;

        /// <summary>Seconds between storage.keep requests while a view is open (the server lets a view go after its KeepTimeout).</summary>
        private const float KeepSeconds = 5f;
        /// <summary>A home link's view waits this long for the player's other window to close (as the old host-side wait).</summary>
        private const float PendingViewSeconds = 25f;
        /// <summary>An open asked while the last view still finishes its changes waits at most this long.</summary>
        private const float PendingOpenSeconds = 10f;
        /// <summary>A dead client's closed view waits at most this long for the respawn before its close is sent anyway.</summary>
        private const float DeadCloseSeconds = 150f;

        /// <summary>Views the window let go whose last changes are still on their way (each sends storage.close once settled).</summary>
        private static readonly List<ScLinkedContainer> closing = new List<ScLinkedContainer>();
        private static ScStorageView pendingView;
        private static float pendingViewAt;
        private static bool openWanted;
        private static int openKind;
        private static Vector3i openClaim;
        private static float openAt;
        private static float nextKeep;
        /// <summary>Round MP3 (review storage M2): the last use of the open view's window (the cursor moved), and when the last keep
        /// went - a keep says "active" when the window was used in between (the server's idle limit).</summary>
        private static float lastUse, lastKeepAt;
        private static Vector3 lastCursor;
        private static GameObject pumpHost;

        /// <summary>Every machine, at the world's start and end (ScHomeLink.Reset): no view carries over.</summary>
        public static void ResetView()
        {
            Current = null;
            closing.Clear();
            pendingView = null;
            openWanted = false;
            CurrentSession = -1;
            Slots.Clear();
        }

        /// <summary>
        /// Round MP3: opens linked storage for this machine's player - the community window's Open storage button and the land
        /// claim radial's Linked storage / Outpost storage, on every machine. The server scans, claims and answers with the view
        /// (OnView); on the host and in single player all of it runs at once. kind = KindHome (claim unused), KindOutpost or
        /// KindHomeLink (claim = the outpost's claim the player stands at). An open asked while the last view still has
        /// changes on their way waits for their answer: the server takes requests in order, and a new view ends the old one.
        /// </summary>
        public static void RequestOpen(EntityPlayerLocal player, int kind, Vector3i claim)
        {
            try
            {
                if (player == null) return;
                string why = ScActionsWindow.RequestRefusal();
                // round MP4 (part: tells): this file's tells go by localization key (ScCommunity.TellKey / ScPlayers.TellCommunityKey), so each player reads them in their own language
                if (why != null) { ScCommunity.TellKey(player, why, "ui_denied"); return; }
                EnsurePump();
                if (Busy())
                {
                    openWanted = true;
                    openKind = kind;
                    openClaim = claim;
                    openAt = Time.unscaledTime;
                    return;
                }
                SendOpen(kind, claim);
            }
            catch (Exception e)
            {
                ScLog.Once("storage-request-open", e);
            }
        }

        /// <summary>The strip's Refresh: the same view again, laid out afresh (once this view's changes are answered).</summary>
        public static void Refresh()
        {
            EntityPlayerLocal p = ScCommunity.LocalPlayer;
            if (p == null || Current == null) return;
            RequestOpen(p, CurrentKind, CurrentAnchorPos);
        }

        private static void SendOpen(int kind, Vector3i claim)
        {
            ScActions.Request(ScStorageServer.OpOpen, new[] { kind, claim.x, claim.y, claim.z }, null, r =>
            {
                if (r.Ok && string.IsNullOrEmpty(r.Message)) return;
                EntityPlayerLocal p = ScCommunity.LocalPlayer;
                if (p != null) ScCommunity.Tell(p, r.Text, r.Sound);
            });
        }

        private static void SendClose(int session)
        {
            if (session < 0) return;
            ScActions.Request(ScStorageServer.OpClose, null, session);
        }

        /// <summary>
        /// Round MP3: the server's view for this machine's player (NetPackageScStorage on a client; at once on the host). Opens it
        /// in the loot window - a home link's view waits while another window is up, as before, so the player is never yanked out
        /// of something else.
        /// </summary>
        public static void OnView(ScStorageView v)
        {
            try
            {
                if (v == null) return;
                EntityPlayerLocal p = ScCommunity.LocalPlayer;
                if (p == null || p.PlayerUI == null || p.PlayerUI.windowManager == null) { SendClose(v.Session); return; }
                EnsurePump();
                if (pendingView != null && pendingView.Session != v.Session) SendClose(pendingView.Session);
                pendingView = null;
                if (v.Kind == KindHomeLink && p.PlayerUI.windowManager.IsModalWindowOpen())
                {
                    pendingView = v;
                    pendingViewAt = Time.unscaledTime;
                    return;
                }
                OpenView(p, v);
            }
            catch (Exception e)
            {
                ScLog.Once("storage-onview", e);
            }
        }

        /// <summary>
        /// Opens linked storage in vanilla's loot window: a virtual container that borrows the anchor block's chunk and
        /// position (vanilla's close path reads ToWorldPos / blockValue, and the window closes itself 32 m from it) and lays the
        /// merged stacks out in one scrolling grid. SCore's OpenContainer is the same call its NPC inventories use.
        /// </summary>
        private static void OpenView(EntityPlayerLocal player, ScStorageView v)
        {
            World w = ScCommunity.World;
            Vector3i at = v.Anchor;
            Chunk chunk = w != null ? w.GetChunkSync(World.toChunkXZ(at.x), World.toChunkXZ(at.z)) as Chunk : null;
            if (chunk == null)
            {
                ScCommunity.TellKey(player, "scNotLoaded", "ui_denied");
                SendClose(v.Session);
                return;
            }
            if (Current != null) Retire(Current);            // Refresh, or a second open: the old view's last changes finish first
            Slots.Clear();
            for (int i = 0; i < v.Slots.Count; i++)
            {
                Slot s = v.Slots[i];
                if (s.Stack == null) s.Stack = ScItemTx.Decode(s.Code);
                if (!s.Stack.IsEmpty()) Slots.Add(s);
            }
            Slots.Sort(Compare);
            viewBoxes = v.Boxes;
            FreeSlots = v.FreeSlots;
            FreeCellsServer = v.FreeCells;
            SkippedChunks = v.SkippedChunks;
            SkippedBoxes = v.SkippedBoxes;
            CurrentSession = v.Session;
            CurrentKind = v.Kind;
            CurrentAnchorPos = v.Anchor;
            CurrentTitle = v.Kind == KindOutpost ? string.Format(Localization.Get("scOpStorageTitle"), v.Name) : null;
            var c = new ScLinkedContainer(chunk, new Vector3i(World.toBlockXZ(at.x), at.y, World.toBlockXZ(at.z))) { Session = v.Session };
            c.lootListName = ScLinkedContainer.LootListKey;       // a real key: the title goes to the window below
            // round s1: without RE Inventory, SC's own window (360 cells, scrolling); the view is sized for the window
            // it opens in
            XUiC_LootWindowGroup own = OwnWindowFor(player, out int ownCols);
            c.OwnWindow = own != null;
            if (own != null) c.ViewCols = ownCols;                // round s1b: its grid's columns, read once (SizeFor)
            SizeFor(c);
            c.Rebuild();
            Current = c;
            nextKeep = Time.unscaledTime + KeepSeconds;
            lastUse = Time.unscaledTime;                // round MP3 (review storage M2): opening it is a use
            OpenLootWindow(player, c, CurrentTitle ?? Localization.Get("scStorageTitle"), own);
            string drawnBy = c.GridDrawn ? ", " + c.StackCap + " stacks, grid inventory" : (c.OwnWindow ? ", SC's window" : ", vanilla grid");
            ScLog.Out("linked storage opened: " + Boxes + " box(es), " + LiveStacks() + " stack(s), " + FreeCells() + " free cell(s), grid " + c.GetContainerSize().x + "x" + c.GetContainerSize().y
                      + " (cap " + c.RowsCap + " rows" + drawnBy + "), "
                      + c.Hidden + " stack(s) and " + c.HiddenCells + " free cell(s) not shown"
                      + (SkippedBoxes > 0 ? ", " + SkippedBoxes + " box(es) in use or locked left out" : ""));
            if (ScConfig.Verbose)
                ScLog.Verbose("linked storage: session " + v.Session + ", view " + c.ItemGrid.Length + " cells as shown");
        }

        /// <summary>A view the window let go: its last changes are sent and answered, then its claims go (Pump).</summary>
        private static void Retire(ScLinkedContainer c)
        {
            if (c == null) return;
            if (ReferenceEquals(Current, c)) Current = null;
            if (!c.Closed) c.ClosedAt = Time.unscaledTime;
            c.Closed = true;
            if (!closing.Contains(c)) closing.Add(c);
        }

        /// <summary>A view of this machine's has changes on their way (an open waits for them; a thrown item waits too).</summary>
        public static bool Busy()
        {
            if (Current != null && Current.Busy) return true;
            for (int i = 0; i < closing.Count; i++) if (closing[i].Busy) return true;
            return false;
        }

        /// <summary>
        /// Every frame (ScStoragePump.LateUpdate, after the window has handled its clicks): a view the window closed is retired,
        /// each view sends its changed cells as one batch (one in flight), closed views release their claims once settled, the
        /// open view tells the server it is still open, and a waiting open or home link view goes ahead.
        /// </summary>
        internal static void Pump()
        {
            try
            {
                float now = Time.unscaledTime;
                if (Current != null && !Current.IsShown()) Retire(Current);
                if (Current != null) Current.Pump(now);
                EntityPlayerLocal me = ScCommunity.LocalPlayer;
                bool deadClient = !ScCommunity.IsHost && me != null && me.IsDead();
                for (int i = closing.Count - 1; i >= 0; i--)
                {
                    ScLinkedContainer c = closing[i];
                    c.Pump(now);
                    if (c.Busy) continue;
                    // a client's requests are refused while it is dead: the close waits for the respawn (the server holds the
                    // view ScStorageServer.DeadHold at most)
                    if (deadClient && now - c.ClosedAt < DeadCloseSeconds) continue;
                    closing.RemoveAt(i);
                    if (Current == null || Current.Session != c.Session) SendClose(c.Session);
                }
                if (Current != null) NoteUse(me, now);
                if ((Current != null || pendingView != null) && now >= nextKeep)
                {
                    nextKeep = now + KeepSeconds;
                    // round MP3 (review storage M2): active 1 when the window was used since the last keep (the server's idle limit)
                    int active = lastUse > lastKeepAt ? 1 : 0;
                    lastKeepAt = now;
                    if (Current != null)
                    {
                        int sid = Current.Session;
                        ScActions.Request(ScStorageServer.OpKeep, r =>
                        {
                            // the server let the view go (it ended the session): the window follows
                            if (!r.Ok && Current != null && Current.Session == sid) CloseWindow();
                        }, sid, active);
                    }
                    if (pendingView != null && (Current == null || Current.Session != pendingView.Session))
                    {
                        // round MP3 (review storage m11): a home link's view waiting for another window is kept too (the server
                        // lets a silent view go after 20 s; the wait is 25 s), and told when it is let go after all
                        int psid = pendingView.Session;
                        ScActions.Request(ScStorageServer.OpKeep, r =>
                        {
                            if (r.Ok || pendingView == null || pendingView.Session != psid) return;
                            pendingView = null;
                            EntityPlayerLocal pl = ScCommunity.LocalPlayer;
                            if (pl != null) ScCommunity.TellKey(pl, "scStorageSessionEnded", "ui_denied");
                        }, psid, 1);
                    }
                }
                if (openWanted && (!Busy() || now - openAt > PendingOpenSeconds))
                {
                    openWanted = false;
                    SendOpen(openKind, openClaim);
                }
                if (pendingView != null) PumpPendingView(now);
            }
            catch (Exception e)
            {
                ScLog.Once("storage-pump", e);
            }
        }

        /// <summary>
        /// Round MP3 (review storage M2): the open view's window is in use - the cursor moved over the screen (a mouse or a
        /// controller's cursor). A cell change counts on the server by itself (storage.cells).
        /// </summary>
        private static void NoteUse(EntityPlayerLocal me, float now)
        {
            try
            {
                if (me == null || me.PlayerUI == null || me.PlayerUI.CursorController == null) return;
                Vector3 cur = me.PlayerUI.CursorController.GetLocalScreenPosition();
                if ((cur - lastCursor).sqrMagnitude > 4f)
                {
                    lastCursor = cur;
                    lastUse = now;
                }
            }
            catch (Exception e)
            {
                ScLog.Once("storage-note-use", e);
            }
        }

        /// <summary>A home link's view waiting for the player's other window: opens once it is closed; dropped (and its claims
        /// let go) when the player walked away from the claim or got in a vehicle, or after PendingViewSeconds.</summary>
        private static void PumpPendingView(float now)
        {
            ScStorageView v = pendingView;
            EntityPlayerLocal p = ScCommunity.LocalPlayer;
            if (p == null || p.PlayerUI == null || p.PlayerUI.windowManager == null) { pendingView = null; SendClose(v.Session); return; }
            Vector3 pos = p.position;
            float dx = pos.x - v.Anchor.x - 0.5f, dz = pos.z - v.Anchor.z - 0.5f;
            if (p.AttachedToEntity != null || dx * dx + dz * dz > 28f * 28f || now - pendingViewAt > PendingViewSeconds)
            {
                pendingView = null;
                SendClose(v.Session);
                ScCommunity.TellKey(p, "scOpDoorCancelled", "ui_denied");
                return;
            }
            if (p.PlayerUI.windowManager.IsModalWindowOpen()) return;
            pendingView = null;
            OpenView(p, v);
        }

        /// <summary>Closes the window showing the open view (a clean close through the window manager: vanilla's OnClose runs).</summary>
        private static void CloseWindow()
        {
            try
            {
                EntityPlayerLocal p = ScCommunity.LocalPlayer;
                if (p == null || p.PlayerUI == null || p.PlayerUI.windowManager == null) return;
                GUIWindowManager wm = p.PlayerUI.windowManager;
                if (wm.IsWindowOpen(XUiC_LootWindowGroup.ID)) wm.Close(XUiC_LootWindowGroup.ID);
                if (wm.IsWindowOpen(OwnWindowGroup)) wm.Close(OwnWindowGroup);
            }
            catch (Exception e)
            {
                ScLog.Once("storage-closewindow", e);
            }
        }

        /// <summary>Round MP3: closes the window when it shows this view (a view that cannot go on: no answer, a broken answer).</summary>
        internal static void CloseWindowOf(ScLinkedContainer c)
        {
            if (c != null && ReferenceEquals(Current, c)) CloseWindow();
        }

        /// <summary>Round MP3: another view wrote boxes this view shows - the slots' new codes and the boxes' free room.</summary>
        public static void OnSlots(int session, List<Slot> updates, int freeSlots, int freeCells)
        {
            try
            {
                ScLinkedContainer c = ViewOf(session);
                if (c == null) return;
                if (ReferenceEquals(c, Current)) SetFree(freeSlots, freeCells);
                c.ApplyPush(updates);
            }
            catch (Exception e)
            {
                ScLog.Once("storage-onslots", e);
            }
        }

        /// <summary>Round MP3: the server ended this view (walked away, silent too long): the window closes, with the reason.</summary>
        public static void OnClosed(int session, string reason)
        {
            try
            {
                // round MP3 (review storage m11): a waiting home link view the server let go is told too (it closed silently)
                bool wasPending = pendingView != null && pendingView.Session == session;
                if (wasPending) pendingView = null;
                if (!wasPending && (Current == null || Current.Session != session)) return;
                if (!wasPending) CloseWindow();
                EntityPlayerLocal p = ScCommunity.LocalPlayer;
                if (p != null && !string.IsNullOrEmpty(reason)) ScCommunity.TellKey(p, reason, "ui_denied");
            }
            catch (Exception e)
            {
                ScLog.Once("storage-onclosed", e);
            }
        }

        /// <summary>The boxes' free room as the server last counted it (a batch's answer, a push).</summary>
        internal static void SetFree(int freeSlots, int freeCells)
        {
            FreeSlots = Math.Max(0, freeSlots);
            FreeCellsServer = freeCells;
        }

        private static ScLinkedContainer ViewOf(int session)
        {
            if (Current != null && Current.Session == session) return Current;
            for (int i = 0; i < closing.Count; i++) if (closing[i].Session == session) return closing[i];
            return null;
        }

        /// <summary>
        /// Round m1p (memory sc-port-3-4, round 6's "Left"): why nothing took the item, from this view. "No box has room for
        /// that." (scStorageFull) - unless the view linked no box at all, when the message says so: the base has none
        /// (scStorageNoBoxes), the outpost's own storage has none (scStorageNoBoxesOutpost), or none was found in the part of the
        /// land that is loaded (scStorageNoBoxesUnloaded: boxes in unloaded chunks are out of reach, so "none" would not be the
        /// real cause).
        /// </summary>
        internal static string ViewFullKey()
        {
            if (Boxes > 0) return "scStorageFull";
            if (SkippedChunks > 0) return "scStorageNoBoxesUnloaded";
            return CurrentKind == KindOutpost ? "scStorageNoBoxesOutpost" : "scStorageNoBoxes";
        }

        private static void EnsurePump()
        {
            if (pumpHost != null) return;
            pumpHost = new GameObject("ScLinkedStoragePump");
            UnityEngine.Object.DontDestroyOnLoad(pumpHost);
            pumpHost.AddComponent<ScStoragePump>();
        }
    }

    /// <summary>Round MP3 (part L): drives linked storage on a machine with a view, after the window has handled the frame's clicks.</summary>
    public class ScStoragePump : MonoBehaviour
    {
        private void LateUpdate()
        {
            ScStorage.Pump();
        }
    }
}
