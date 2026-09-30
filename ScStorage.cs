using System;
using System.Collections.Generic;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Linked storage: every player-placed storage box inside the community zone, read as one list of
    /// stacks. Each entry remembers the box (block position) and the slot it came from, so the window's
    /// cells write straight back into the real box slot (take, swap, merge), and a drop on an empty cell
    /// goes into the first box with a free slot. Boxes in unloaded chunks are out of reach - the scan walks
    /// the loaded chunks' tile entity lists, which is cheap. The packing station is left out on purpose.
    /// Host only (the window refuses to open elsewhere).
    /// </summary>
    public static class ScStorage
    {

        public sealed class Slot
        {
            public Vector3i Pos;
            public int Index;
            public ItemStack Stack;
        }

        public static readonly List<Slot> Slots = new List<Slot>();
        private static readonly List<Vector3i> boxes = new List<Vector3i>();
        public static int Boxes { get { return boxes.Count; } }
        public static int FreeSlots;
        public static int SkippedChunks;

        // ---- grid bridge --------------------------------------------------------------------------
        // With GridInventory a box holds an AREA, not a stack count: a free slot is worth nothing when no
        // run of free cells fits the item's footprint. The zzzz_GridInv_SurvivorCommunity patch registers
        // these three; without it every stack is one cell and a box with a free slot takes anything, as before.

        /// <summary>Cells a stack covers on a grid; null = one.</summary>
        public static Func<ItemStack, int> StackArea;
        /// <summary>Whether a box's grid can seat this stack somewhere; null = yes. Its free slot is checked here regardless.</summary>
        public static Func<TEFeatureStorage, ItemStack, bool> BoxHasRoomFor;
        /// <summary>Free cells of a box's grid; null = its free slots.</summary>
        public static Func<TEFeatureStorage, int> BoxFreeCells;

        // Two more, both optional (an older bridge leaves them null and the fallbacks below apply):
        /// <summary>Does Grid Inventory draw this container (container grids on, not excluded)? null = yes whenever Grid Inventory is loaded.</summary>
        public static Func<ITileEntityLootable, bool> GridDraws;
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

        /// <summary>Free cells per box, filled lazily and dropped for a box the view writes to: re-packing every
        /// box on every question (the strip asks twice a second) costs a grid pack per box.</summary>
        private static readonly Dictionary<Vector3i, int> freeByBox = new Dictionary<Vector3i, int>();

        private static int BoxFree(Vector3i pos)
        {
            int n;
            if (freeByBox.TryGetValue(pos, out n)) return n;
            n = 0;
            TEFeatureStorage st = Resolve(pos);
            if (st != null)
            {
                try { n = Math.Max(0, BoxFreeCells(st)); }
                catch (Exception e) { ScLog.Once("storage-freecells", e); }
            }
            freeByBox[pos] = n;
            return n;
        }

        public static int AreaOf(ItemStack stack)
        {
            if (StackArea == null || stack == null || stack.IsEmpty()) return 1;
            try { int a = StackArea(stack); return a < 1 ? 1 : a; }
            catch (Exception e) { ScLog.Once("storage-area", e); return 1; }
        }

        /// <summary>The real room to deposit into, in cells: the boxes' free cells with the bridge, else FreeSlots.</summary>
        public static int FreeCells()
        {
            if (BoxFreeCells == null) return Math.Max(0, FreeSlots);
            int n = 0;
            for (int b = 0; b < boxes.Count; b++) n += BoxFree(boxes[b]);
            return n;
        }

        /// <summary>Room in the unit the view is drawn in: cells on Grid Inventory's grid, slots on vanilla's.</summary>
        public static int FreeFor(bool gridDrawn)
        {
            return gridDrawn ? FreeCells() : Math.Max(0, FreeSlots);
        }

        /// <summary>Rebuilds the list from the loaded boxes inside the zone. Returns false without a home.</summary>
        public static bool Scan()
        {
            Slots.Clear();
            boxes.Clear();
            freeByBox.Clear();
            FreeSlots = 0;
            SkippedChunks = 0;
            try
            {
                World w = ScCommunity.World;
                if (w == null || !ScZone.HasHome) return false;
                ScConfig.EnsureParsed();
                int xMin, xMax, zMin, zMax;
                int[] area = CurrentArea;
                if (area != null) { xMin = area[0]; xMax = area[1]; zMin = area[2]; zMax = area[3]; }
                else ScZone.Bounds(out xMin, out xMax, out zMin, out zMax);
                int cxMin = World.toChunkXZ(xMin), cxMax = World.toChunkXZ(xMax);
                int czMin = World.toChunkXZ(zMin), czMax = World.toChunkXZ(zMax);
                for (int cx = cxMin; cx <= cxMax; cx++)
                {
                    for (int cz = czMin; cz <= czMax; cz++)
                    {
                        Chunk chunk = w.GetChunkSync(cx, cz) as Chunk;
                        if (chunk == null) { SkippedChunks++; continue; }
                        var tes = chunk.tileEntities;
                        if (tes == null || tes.list == null) continue;
                        for (int i = 0; i < tes.list.Count; i++)
                        {
                            TileEntityComposite tc = tes.list[i] as TileEntityComposite;
                            if (tc == null) continue;
                            TEFeatureStorage st = tc.GetFeature<TEFeatureStorage>();
                            if (st == null) continue;
                            if (tc.GetFeature<TEFeatureSCPacking>() != null) continue;      // the packing station is a workbench, not a store
                            if (!(tc.PlayerPlaced || st.bPlayerStorage)) continue;           // world loot is not the community's
                            Vector3i pos = tc.ToWorldPos();
                            if (area != null ? (pos.x < xMin || pos.x > xMax || pos.z < zMin || pos.z > zMax) : !ScZone.Contains(pos)) continue;
                            boxes.Add(pos);
                            ItemStack[] items = st.items;
                            if (items == null) continue;
                            for (int k = 0; k < items.Length; k++)
                            {
                                ItemStack s = items[k];
                                if (s == null || s.IsEmpty()) { FreeSlots++; continue; }
                                Slots.Add(new Slot { Pos = pos, Index = k, Stack = s.Clone() });
                            }
                        }
                    }
                }
                Slots.Sort(Compare);
                return true;
            }
            catch (Exception e)
            {
                ScLog.Once("storage-scan", e);
                return false;
            }
        }

        private static int Compare(Slot a, Slot b)
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

        /// <summary>Writes a cell's new content into the real box slot it came from.</summary>
        public static bool WriteThrough(Slot s, ItemStack stack)
        {
            try
            {
                if (s == null) return false;
                TEFeatureStorage st = Resolve(s.Pos);
                if (st == null || st.items == null || s.Index < 0 || s.Index >= st.items.Length) return false;
                ItemStack v = (stack == null) ? ItemStack.Empty.Clone() : stack.Clone();
                bool wasEmpty = s.Stack == null || s.Stack.IsEmpty();
                st.UpdateSlot(s.Index, v);
                st.SetModified();
                freeByBox.Remove(s.Pos);
                s.Stack = v.Clone();
                if (wasEmpty && !v.IsEmpty()) FreeSlots = Math.Max(0, FreeSlots - 1);
                else if (!wasEmpty && v.IsEmpty()) FreeSlots++;
                return true;
            }
            catch (Exception e)
            {
                ScLog.Once("storage-write", e);
                return false;
            }
        }

        /// <summary>
        /// A drop on an empty cell: the first box (from the last scan) with a free slot takes it - and, with
        /// the grid bridge, only a box whose grid has room for the item's footprint (BoxHasRoomFor).
        /// </summary>
        public static bool Deposit(ItemStack stack, out Slot placed)
        {
            if (DepositOnce(stack, out placed)) return true;
            // a box can gain room without the view writing to it (another player on a server): the cached
            // "full" is a hint, so look once more with every box asked afresh
            if (freeByBox.Count == 0) return false;
            freeByBox.Clear();
            return DepositOnce(stack, out placed);
        }

        private static bool DepositOnce(ItemStack stack, out Slot placed)
        {
            placed = null;
            try
            {
                if (stack == null || stack.IsEmpty()) return false;
                for (int b = 0; b < boxes.Count; b++)
                {
                    TEFeatureStorage st = Resolve(boxes[b]);
                    if (st == null || st.items == null) continue;
                    // a full box is skipped before the footprint test packs its grid (Move-All into a base of full
                    // crates would pack every box per stack). Only "no room at all": for a box Grid Inventory leaves
                    // on vanilla slots the figure is free SLOTS, which a footprint must not be compared against.
                    if (BoxFreeCells != null && BoxFree(boxes[b]) <= 0) continue;
                    if (BoxHasRoomFor != null)
                    {
                        bool room;
                        try { room = BoxHasRoomFor(st, stack); }
                        catch (Exception e) { ScLog.Once("storage-room", e); room = true; }
                        if (!room) continue;
                    }
                    for (int i = 0; i < st.items.Length; i++)
                    {
                        if (st.items[i] != null && !st.items[i].IsEmpty()) continue;
                        st.UpdateSlot(i, stack.Clone());
                        st.SetModified();
                        freeByBox.Remove(boxes[b]);
                        placed = new Slot { Pos = boxes[b], Index = i, Stack = stack.Clone() };
                        Slots.Add(placed);
                        if (FreeSlots > 0) FreeSlots--;
                        return true;
                    }
                }
                return false;
            }
            catch (Exception e)
            {
                ScLog.Once("storage-deposit", e);
                return false;
            }
        }

        /// <summary>Stacks that still hold something (takes leave empty entries until the next scan).</summary>
        public static int LiveStacks()
        {
            int n = 0;
            for (int i = 0; i < Slots.Count; i++) if (!Slots[i].Stack.IsEmpty()) n++;
            return n;
        }

        /// <summary>Whether Grid Inventory will draw this container: the bridge's answer, else "it is loaded".</summary>
        public static bool GridDrawsIt(ITileEntityLootable te)
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
        /// per slot and no scrolling: 10 rows, as before. Without the bridge's GridDraws (an older bridge) the
        /// previous 28 rows. LinkedStorageRows only lowers these.
        /// </summary>
        public static void SizeFor(ScLinkedContainer c)
        {
            ScConfig.EnsureParsed();
            bool gi = GridDrawsIt(c);
            bool known = GridDraws != null;                // the bridge can tell; without it keep the old fixed size
            int rows = !gi ? VanillaRows : (known ? GridRowsMax : LegacyGridRows);
            if (ScConfig.LinkedStorageRows > 0) rows = Math.Min(rows, ScConfig.LinkedStorageRows);
            c.RowsCap = Math.Max(1, rows);
            int pool = WidgetPool();
            c.StackCap = gi && known ? Math.Max(ScLinkedContainer.Cols, pool - 2 * ScLinkedContainer.Cols) : 0;
            c.ShowLimit = gi && known ? pool : int.MaxValue;
            c.GridDrawn = gi;
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

        /// <summary>
        /// SCore's EntityUtilities.OpenContainer with the title passed on its own: that helper hands the loot window
        /// te.lootListName as the title, so a view with a title of its own would need an unknown loot list key.
        /// </summary>
        private static void OpenLootWindow(EntityPlayerLocal player, ITileEntityLootable te, string title)
        {
            LocalPlayerUI ui = player.PlayerUI;
            ui.windowManager.CloseAllOpenModalWindows(null, false);
            GUIWindow window = ui.windowManager.GetWindow("looting");
            XUiC_LootWindowGroup group = window is XUiWindowGroup ? ((XUiWindowGroup)window).Controller as XUiC_LootWindowGroup : null;
            te.bWasTouched = true;                      // straight in: no open timer (which reads openTime)
            if (group == null) { EntityUtilities.OpenContainer(player, te); return; }
            group.OpenLooting(title, te);
        }
        /// <summary>
        /// The block the open view is anchored on: the home claim, or (ScHomeLink) the outpost claim the player
        /// radioed from. Vanilla's loot window closes itself when the player is more than 32 m from its container's
        /// ToWorldCenterPos, so a view opened from afar must stand where the player stands. Null = the home claim.
        /// </summary>
        public static Vector3i? CurrentAnchor;
        /// <summary>The boxes' area of the open view {xMin, xMax, zMin, zMax}: an outpost's POI; null = the home zone.</summary>
        public static int[] CurrentArea;
        public static string CurrentTitle;

        /// <summary>
        /// Opens linked storage in vanilla's loot window: a virtual container that borrows the home block's
        /// chunk and position (vanilla's close path reads ToWorldPos / blockValue) and lays the merged
        /// stacks out in one scrolling grid. SCore's OpenContainer is the same call its NPC inventories use.
        /// </summary>
        public static void OpenWindow(EntityPlayerLocal player)
        {
            OpenWindow(player, null);
        }

        /// <summary>anchor = where the view stands (see CurrentAnchor); null = the home claim.</summary>
        public static void OpenWindow(EntityPlayerLocal player, Vector3i? anchor)
        {
            OpenWindow(player, anchor, null, null);
        }

        /// <summary>
        /// area = the boxes to link {xMin, xMax, zMin, zMax} (an outpost's own storage) - null = the home zone;
        /// anchor = where the view stands (the player's claim); title = the window's title (null = linked storage).
        /// </summary>
        public static void OpenWindow(EntityPlayerLocal player, Vector3i? anchor, int[] area, string title)
        {
            try
            {
                if (player == null || player.PlayerUI == null || player.PlayerUI.windowManager == null) return;
                if (!ScCommunity.IsHost) { ScCommunity.Tell(player, Localization.Get("scHostOnly"), "ui_denied"); return; }
                if (!ScZone.HasHome) { ScCommunity.Tell(player, Localization.Get("scNeedHome"), "ui_denied"); return; }
                World w = ScCommunity.World;
                Vector3i home = ScZone.Home;
                Chunk homeChunk = (w != null) ? w.GetChunkSync(World.toChunkXZ(home.x), World.toChunkXZ(home.z)) as Chunk : null;
                if (homeChunk == null && area == null) { ScCommunity.Tell(player, Localization.Get("scNotLoaded"), "ui_denied"); return; }
                Vector3i at = anchor ?? home;
                Chunk chunk = anchor.HasValue && w != null ? w.GetChunkSync(World.toChunkXZ(at.x), World.toChunkXZ(at.z)) as Chunk : homeChunk;
                if (chunk == null) { chunk = homeChunk; at = home; }
                if (chunk == null) { ScCommunity.Tell(player, Localization.Get("scNotLoaded"), "ui_denied"); return; }
                CurrentArea = area;
                CurrentTitle = title;
                Scan();
                var c = new ScLinkedContainer(chunk, new Vector3i(World.toBlockXZ(at.x), at.y, World.toBlockXZ(at.z)));
                CurrentAnchor = at == home ? (Vector3i?)null : at;
                c.lootListName = ScLinkedContainer.LootListKey;       // a real key: the title goes to the window below
                SizeFor(c);
                c.Rebuild();
                Current = c;
                OpenLootWindow(player, c, title ?? Localization.Get("scStorageTitle"));
                ScLog.Out("linked storage opened: " + Boxes + " box(es), " + LiveStacks() + " stack(s), " + FreeCells() + " free cell(s), grid " + c.GetContainerSize().x + "x" + c.GetContainerSize().y
                          + " (cap " + c.RowsCap + " rows" + (c.GridDrawn ? ", " + c.StackCap + " stacks, grid inventory" : ", vanilla grid") + "), "
                          + c.Hidden + " stack(s) and " + c.HiddenCells + " free cell(s) not shown");
            }
            catch (Exception e)
            {
                ScLog.Once("storage-open", e);
            }
        }
    }
}
