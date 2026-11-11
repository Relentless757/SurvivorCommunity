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
#if SC_V34
                            if (!(tc.PlayerPlaced || IsPlayerStorage(st))) continue;         // world loot is not the community's
#else
                            if (!(tc.PlayerPlaced || st.bPlayerStorage)) continue;           // world loot is not the community's
#endif
                            Vector3i pos = tc.ToWorldPos();
                            if (area != null ? (pos.x < xMin || pos.x > xMax || pos.z < zMin || pos.z > zMax) : !ScZone.Contains(pos)) continue;
#if SC_V34
                            // a land claim block's own storage (keystoneBlock's 7x4) is not linked: in one grid with the crates
                            // nothing shows which cells are its own (user, 2026-10-31). It stays on the block, opened there:
                            // an outpost's packing strip rides it (ScStockpile.StationOf), and a saved block's items would be lost
                            // with the feature (TileEntityComposite.read skips a payload the block no longer defines).
                            if (tc.GetFeature<TEFeatureLandClaim>() != null) continue;
#endif
                            boxes.Add(pos);
#if SC_V34
                            ItemStack[] items = st.ItemGrid != null ? st.ItemGrid.items : null;   // 3.4: the grid's slots (read only)
#else
                            ItemStack[] items = st.items;
#endif
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
#if SC_V34
                if (st == null || st.ItemGrid == null || s.Index < 0 || s.Index >= st.ItemGrid.Length) return false;
#else
                if (st == null || st.items == null || s.Index < 0 || s.Index >= st.items.Length) return false;
#endif
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
#if SC_V34
            if (DepositOnce(stack, out placed)) return true;
            // a box can gain room without the view writing to it (another player on a server): the cached
            // "full" is a hint, so look once more with every box asked afresh
            if (freeByBox.Count > 0)
            {
                freeByBox.Clear();
                if (DepositOnce(stack, out placed)) return true;
            }
            // 3.4 (DebugLogging): why nothing took it - each box the view links, its block, slots and room
            if (ScConfig.Verbose)
                ScLog.Verbose("linked storage: no box takes " + Name(stack) + " x" + (stack != null ? stack.count : 0) + " - " + BoxesSummary(stack));
            return false;
#else
            if (DepositOnce(stack, out placed)) return true;
            // a box can gain room without the view writing to it (another player on a server): the cached
            // "full" is a hint, so look once more with every box asked afresh
            if (freeByBox.Count == 0) return false;
            freeByBox.Clear();
            return DepositOnce(stack, out placed);
#endif
        }
#if SC_V34

        /// <summary>
        /// 3.4 (DebugLogging): the boxes the view links, for the deposit log - "block at pos: N slots, M empty", plus the
        /// bridge's free cells and its answer for this stack when the bridge sets them; "no boxes linked" without boxes.
        /// </summary>
        private static string BoxesSummary(ItemStack stack)
        {
            if (boxes.Count == 0) return "no boxes linked";
            var sb = new System.Text.StringBuilder();
            for (int b = 0; b < boxes.Count; b++)
            {
                if (b > 0) sb.Append("; ");
                TEFeatureStorage st = Resolve(boxes[b]);
                sb.Append(BlockNameAt(boxes[b])).Append(" at ").Append(boxes[b]);
                if (st == null || st.ItemGrid == null) { sb.Append(": not loaded"); continue; }
                ItemStack[] items = st.ItemGrid.items;
                int empty = 0;
                for (int i = 0; i < items.Length; i++) if (items[i] == null || items[i].IsEmpty()) empty++;
                sb.Append(": ").Append(items.Length).Append(" slots, ").Append(empty).Append(" empty");
                try
                {
                    if (BoxFreeCells != null) sb.Append(", ").Append(BoxFreeCells(st)).Append(" free cells");
                    if (BoxHasRoomFor != null && stack != null) sb.Append(BoxHasRoomFor(st, stack) ? ", room for it" : ", NO room for it");
                }
                catch (Exception e) { sb.Append(" (bridge: ").Append(e.Message).Append(")"); }
            }
            return sb.ToString();
        }

        /// <summary>3.4 (DebugLogging): the block's name at a box position, for the deposit log.</summary>
        private static string BlockNameAt(Vector3i pos)
        {
            try
            {
                World w = ScCommunity.World;
                BlockValue bv = w != null ? w.GetBlock(pos) : BlockValue.Air;
                return bv.Block != null ? bv.Block.GetBlockName() : "?";
            }
            catch (Exception) { return "?"; }
        }
#endif

        private static bool DepositOnce(ItemStack stack, out Slot placed)
        {
            placed = null;
            try
            {
                if (stack == null || stack.IsEmpty()) return false;
                for (int b = 0; b < boxes.Count; b++)
                {
                    TEFeatureStorage st = Resolve(boxes[b]);
#if SC_V34
                    if (st == null || st.ItemGrid == null) continue;
#else
                    if (st == null || st.items == null) continue;
#endif
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
#if SC_V34
                    ItemStack[] items = st.ItemGrid.items;          // 3.4: the grid's slots - read here, written through UpdateSlot
                    for (int i = 0; i < items.Length; i++)
                    {
                        if (items[i] != null && !items[i].IsEmpty()) continue;
#else
                    for (int i = 0; i < st.items.Length; i++)
                    {
                        if (st.items[i] != null && !st.items[i].IsEmpty()) continue;
#endif
                        st.UpdateSlot(i, stack.Clone());
                        st.SetModified();
                        freeByBox.Remove(boxes[b]);
                        placed = new Slot { Pos = boxes[b], Index = i, Stack = stack.Clone() };
                        Slots.Add(placed);
                        if (FreeSlots > 0) FreeSlots--;
#if SC_V34
                        if (ScConfig.Verbose)
                            ScLog.Verbose("linked storage: " + Name(stack) + " x" + stack.count + " into " + BlockNameAt(boxes[b]) + " at " + boxes[b] + ", slot " + i);
#endif
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

        /// <summary>
        /// Puts one stack into the first box of the home zone's linked storage with a free slot (and, with the grid bridge,
        /// room for its footprint): what a member hands back when they keep a weapon you gave them (ScArms). The boxes are
        /// walked here rather than through Scan, which would replace the view's list and its area (an outpost's, when that
        /// was the last view opened). Loaded chunks only. False when no box has room.
        /// </summary>
        public static bool DepositInZone(ItemStack stack, out Vector3i box)
        {
            box = Vector3i.zero;
            try
            {
                World w = ScCommunity.World;
                if (w == null || !ScZone.HasHome || stack == null || stack.IsEmpty()) return false;
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
                            // the same boxes Scan links: player-placed storage inside the zone, never the packing station
                            TileEntityComposite tc = tes.list[i] as TileEntityComposite;
                            TEFeatureStorage st = tc != null ? tc.GetFeature<TEFeatureStorage>() : null;
#if SC_V34
                            if (st == null || st.ItemGrid == null || tc.GetFeature<TEFeatureSCPacking>() != null) continue;
                            if (!(tc.PlayerPlaced || IsPlayerStorage(st))) continue;
                            if (tc.GetFeature<TEFeatureLandClaim>() != null) continue;     // not linked (Scan)
#else
                            if (st == null || st.items == null || tc.GetFeature<TEFeatureSCPacking>() != null) continue;
                            if (!(tc.PlayerPlaced || st.bPlayerStorage)) continue;
#endif
                            Vector3i pos = tc.ToWorldPos();
                            if (!ScZone.Contains(pos)) continue;
                            if (BoxHasRoomFor != null)
                            {
                                bool room;
                                try { room = BoxHasRoomFor(st, stack); }
                                catch (Exception e) { ScLog.Once("storage-room", e); room = true; }
                                if (!room) continue;
                            }
#if SC_V34
                            ItemStack[] items = st.ItemGrid.items;
                            for (int k = 0; k < items.Length; k++)
                            {
                                if (items[k] != null && !items[k].IsEmpty()) continue;
#else
                            for (int k = 0; k < st.items.Length; k++)
                            {
                                if (st.items[k] != null && !st.items[k].IsEmpty()) continue;
#endif
                                st.UpdateSlot(k, stack.Clone());
                                st.SetModified();
                                freeByBox.Remove(pos);
                                box = pos;
                                return true;
                            }
                        }
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
#if SC_V34

        /// <summary>
        /// Round 29a (J29): the boxes DepositInZone fills - player-placed storage inside the home zone, loaded, never the packing
        /// station or a land claim block's own storage - with their positions.
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
                        if (st == null || st.ItemGrid == null || tc.GetFeature<TEFeatureSCPacking>() != null) continue;
                        if (!(tc.PlayerPlaced || IsPlayerStorage(st))) continue;
                        if (tc.GetFeature<TEFeatureLandClaim>() != null) continue;
                        Vector3i pos = tc.ToWorldPos();
                        if (!ScZone.Contains(pos)) continue;
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
                        b.Value.SetModified();
                        freeByBox.Remove(b.Key);
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
                        freeByBox.Remove(b.Key);
                        box = b.Key;
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
#endif

        /// <summary>Stacks that still hold something (takes leave empty entries until the next scan).</summary>
        public static int LiveStacks()
        {
            int n = 0;
            for (int i = 0; i < Slots.Count; i++) if (!Slots[i].Stack.IsEmpty()) n++;
            return n;
        }

#if SC_V34
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
#else
        /// <summary>Whether Grid Inventory will draw this container: the bridge's answer, else "it is loaded".</summary>
        public static bool GridDrawsIt(ITileEntityLootable te)
#endif
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
#if SC_V34
            // 3.4: vanilla's loot refresh (XUiC_LootContainer.OnTileEntityChanged) walks the CONTAINER's grid and indexes the
            // window's widgets with the same counter, unguarded (3.2 walked the widgets) - a view with more cells than the
            // window has widgets throws on every write, after the write reached a box but before the cell took the drop. And
            // RE Inventory 3.3+ draws one cell per container slot with an item widget per slot (its pool of 260; 3.2's Grid
            // Inventory drew widgets for stacks only and scrolled over empty cells), so its grid cannot draw a cell past its
            // pool either. So on 3.4 the view never has more cells than the window's widgets, with or without the bridge
            // (90 on vanilla's 10 x 9 grid) - nor, with the bridge, than the grid's own pool.
            {
                int widgets = LootWidgets();
                if (gi && known)
                {
                    int pool34 = WidgetPool();
                    widgets = widgets > 0 ? Math.Min(widgets, pool34) : pool34;
                }
                if (widgets > 0) rows = Math.Min(rows, Math.Max(1, widgets / ScLinkedContainer.Cols));
                else if (gi) rows = Math.Min(rows, VanillaRows);           // unknown: never past vanilla's grid
            }
#endif
            if (ScConfig.LinkedStorageRows > 0) rows = Math.Min(rows, ScConfig.LinkedStorageRows);
            c.RowsCap = Math.Max(1, rows);
            int pool = WidgetPool();
            c.StackCap = gi && known ? Math.Max(ScLinkedContainer.Cols, pool - 2 * ScLinkedContainer.Cols) : 0;
            c.ShowLimit = gi && known ? pool : int.MaxValue;
            c.GridDrawn = gi;
        }

#if SC_V34
        /// <summary>3.4: the looting window's item widgets (its XUiC_LootContainer's), or 0 when the window is not found.</summary>
        private static int LootWidgets()
        {
            try
            {
                LocalPlayerUI ui = LocalPlayerUI.GetUIForPrimaryPlayer();
                XUiC_LootWindow lw = ui != null && ui.xui != null ? ui.xui.GetChildByType<XUiC_LootWindow>() : null;
                XUiC_ItemStack[] w = lw != null && lw.lootContainer != null ? lw.lootContainer.GetItemStackControllers() : null;
                return w != null ? w.Length : 0;
            }
            catch (Exception e)
            {
                ScLog.Once("storage-lootwidgets", e);
                return 0;
            }
        }

#endif
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
#if SC_V34

        /// <summary>3.4: the cells the open view should draw (ScLinkedContainer.CellsNow), 0 = no limit. For the bridge, which
        /// references no SCore and so cannot touch ScLinkedContainer, a subclass of SCore's container.</summary>
        public static int CurrentCells()
        {
            return Current != null ? Current.CellsNow() : 0;
        }
#endif

        /// <summary>
        /// SCore's EntityUtilities.OpenContainer with the title passed on its own: that helper hands the loot window
        /// te.lootListName as the title, so a view with a title of its own would need an unknown loot list key.
        /// </summary>
#if SC_V34
        private static void OpenLootWindow(EntityPlayerLocal player, ScLinkedContainer te, string title)
        {
            LocalPlayerUI ui = player.PlayerUI;
            ui.windowManager.CloseAllOpenModalWindows(null, false);
            GUIWindow window = ui.windowManager.GetWindow("looting");
            XUiC_LootWindowGroup group = window is XUiWindowGroup ? ((XUiWindowGroup)window).Controller as XUiC_LootWindowGroup : null;
            te.bTouched = true;                         // 3.4: the grid's touched stamp
            // 3.4: straight in - the open timer now runs only for _firstTimeTouched (SCore's OpenContainer passes false too)
            if (group == null) { EntityUtilities.OpenContainer(player, te); return; }
            group.OpenLooting(title, te, false);
        }
#else
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
#endif
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
#if SC_V34
                // 3.4 (DebugLogging): what the view links, and the size the window really shows (RE Inventory 3.3+ resizes
                // a gridded container to its own rule unless the bridge exempts the view)
                if (ScConfig.Verbose)
                    ScLog.Verbose("linked storage boxes: " + BoxesSummary(null) + " | view " + c.ItemGrid.Length + " cells as shown");
#endif
            }
            catch (Exception e)
            {
                ScLog.Once("storage-open", e);
            }
        }
    }
}
