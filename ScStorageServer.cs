using System;
using System.Collections.Generic;
using System.Text;
using Platform;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round MP3 (part L): one linked storage view as the server built it for one player - what NetPackageScStorage carries to a
    /// client, and what the host's own player gets handed directly (ScStorage.OnView).
    /// </summary>
    public sealed class ScStorageView
    {
        /// <summary>The server's session id: every change the view sends names it, so a change from an older view is refused.</summary>
        public int Session;
        /// <summary>ScStorage.KindHome / KindOutpost / KindHomeLink.</summary>
        public int Kind;
        /// <summary>The claim the view stands on (the home claim, an outpost's claim): the loot window closes 32 m from it.</summary>
        public Vector3i Anchor;
        /// <summary>The outpost's name (KindOutpost), for the window's title; "" otherwise.</summary>
        public string Name = "";
        public int Boxes;
        public int FreeSlots;
        /// <summary>The boxes' free cells by the grid bridge; -1 = the server has no bridge (a free slot is a free cell).</summary>
        public int FreeCells = -1;
        public int SkippedChunks;
        /// <summary>Boxes left out: open by someone (vanilla), or locked against this player.</summary>
        public int SkippedBoxes;
        public readonly List<ScStorage.Slot> Slots = new List<ScStorage.Slot>();
    }

    /// <summary>
    /// Round MP3 (part L, multiplayer): linked storage, the server's side. The server is the only machine that writes a real box.
    ///
    /// Sessions and claims. storage.open scans the zone (or an outpost's building) for the player who asked and CLAIMS the
    /// boxes it shows: the session (one per player) holds them until the view closes (storage.close), the player walks away
    /// (MaxDistance), stops answering (storage.keep, KeepTimeout; never the host's own player), leaves it idle (IdleTimeout), dies,
    /// disconnects, or opens another view. Sweep (once a second, ScTicker) and the world's start and end (Reset) make sure no
    /// claim outlives its player. Round MP3 (review storage M2): a claim is only the list of boxes a view may write; it keeps
    /// nobody out, because every write is judged against the box itself at that moment:
    ///  - a vanilla open goes ahead, and the box leaves every view that showed it (OnVanillaOpen, a postfix on
    ///    TEFeatureStorage.OnLockRequestServer, Patches_LinkedStorage): an open box belongs to the player who has it open (their
    ///    copy overwrites any server-side write, game:TEFeatureStorage.cs:486-488, game:NetPackageTileEntity.cs), so a view's
    ///    later change to it is refused (not in the session, and open by someone: UsableFor);
    ///  - SC's own NPC writers (ScStorage.DepositInZone, TopUpInZone, TakeOneInZone) write a claimed box like any other and push
    ///    the slots they wrote to the views that show them (PushWritten); a view's change to such a slot is refused by the
    ///    compare-and-set and resynced. They still skip a box a player has open by hand, or one locked against the community.
    /// Two players' views may claim the same box (several sessions): their changes are compare-and-set against the box itself,
    /// so the second change to a slot is refused and resynced, and every committed change is pushed to the other views that
    /// show the slot (NetPackageScStorage, slot updates).
    ///
    /// Scan rules (F3-F5, for every view, the host's included): a box is left out (counted in SkippedBoxes) when somebody has it
    /// open (LockManager.IsLockedServer, or the server's own open flag) or it is locked (TEFeatureLockable) against the viewer -
    /// a friend's password-locked crate is never linked.
    ///
    /// Changes. storage.cells carries one BATCH: every cell the view changed since its last batch, each an entry of
    ///  - CAS: box x y z, slot index, the slot's code as the view last had it (expect), the code it wants there (new);
    ///  - deposit: the stack for a cell with no box slot behind it; the server picks the first claimed box with room.
    /// A batch is all or nothing for its compare-and-sets: every entry is checked first (the box is in the session, loaded, not
    /// open by anyone, not locked against the sender, the slot still holds exactly `expect`, the new stack is valid), and only
    /// then written (UpdateSlot, SetModified). A refused batch writes nothing and answers every entry's slot as it is now, so the
    /// view resyncs and puts its own cursor and inventory back (ScLinkedContainer). Round MP3 (review storage M1, m10): the
    /// deposits are judged after every compare-and-set, each against the boxes as the batch's earlier entries leave them (the
    /// grid bridge's footprint test included, RoomAsPlanned), and a deposit no box has room for is answered "-" on its own -
    /// the rest of the batch is written and only that item goes back to its player. A batch whose deposits all failed and that
    /// has nothing else is refused as before. A batch longer than PartEntries goes in parts (the request's int list holds 255
    /// numbers); the server buffers them and judges the whole batch at the last part. The view keeps one batch in flight.
    ///
    /// Stacks are compared by their encoding (ScItemTx.Encode: the game's own ItemStack.Write, base64) - ItemValue.Equals
    /// compares its mod arrays by reference, so two copies of the same modded gun never "equal". The view echoes the code the
    /// server gave it, so the compare is always the server's own Encode against itself. Round MP3 (review storage m9): the
    /// host's own view never leaves this machine, so its codes are not capped (CodeFor, StackOf); only a remote player's are.
    ///
    /// Ops (ScActions requests, Community access):
    ///   storage.open   kind, x, y, z           kind = ScStorage.Kind*; x y z = the claim pressed (KindOutpost, KindHomeLink);
    ///                                          refused farther than MaxDistance from the view's anchor (review storage m4)
    ///   storage.cells  session, batch, part, parts, then per entry (flags, x, y, z, index); s: per entry (expect, new) for a
    ///                  CAS, (stack, "") for a deposit (flags bit 0)
    ///                  answer (the last part): Done (with scStorageFull when a deposit found no room) or Fail(reason), hints
    ///                  res (EncodeResults: every entry's slot after the batch, or as it is now when refused; "-" = a deposit
    ///                  not placed), fs (free slots), fc (free cells, -1 = no bridge)
    ///   storage.close  session
    ///   storage.keep   session, active         the view is still open (every few seconds); active 1 = the player used the
    ///                                          window since the last keep (review storage M2's idle limit)
    /// </summary>
    public static class ScStorageServer
    {
        public const string OpOpen = "storage.open";
        public const string OpCells = "storage.cells";
        public const string OpClose = "storage.close";
        public const string OpKeep = "storage.keep";

        /// <summary>storage.cells entry flag: a deposit (the server picks the slot).</summary>
        public const int FlagDeposit = 1;
        /// <summary>storage.cells: ints before the entries (session, batch, part, parts) and per entry (flags, x, y, z, index).</summary>
        public const int HeadInts = 4, EntryInts = 5;
        /// <summary>Entries per request part: NetPackageScAction carries at most 255 ints and 255 strings.</summary>
        public const int PartEntries = (255 - HeadInts) / EntryInts;
        /// <summary>A batch longer than this is refused outright (no view has this many cells).</summary>
        public const int MaxBatchEntries = 12000;
        /// <summary>A view whose player sent nothing (storage.keep, a change) for this long is let go (never the host's own: review
        /// storage m8, its requests run inline and a long frame or a pause is no silence).</summary>
        public const float KeepTimeout = 20f;
        /// <summary>
        /// Round MP3 (review storage M2): a view with no cell change and no use of its window (storage.keep's active flag) for
        /// this long is closed, and its player told why - a view left open by a player gone AFK holds its claims no longer. Not in
        /// single player (nobody else to wait for it), so the host's own view only while another player is in the world.
        /// </summary>
        public const float IdleTimeout = 300f;
        /// <summary>A view whose player is farther than this from its anchor is let go (the loot window closes at 32 m).</summary>
        public const float MaxDistance = 40f;
        /// <summary>
        /// A dead player's view is held this long: ScActions refuses a dead player's requests, so a batch that reached the server
        /// just after the death comes back after the respawn (ScLinkedContainer holds it) - by then its items may be in the
        /// death backpack, and only the commit keeps them from being doubled.
        /// </summary>
        public const float DeadHold = 120f;
        /// <summary>After a respawn the view is not measured for this long: the player stands at their spawn point, and their
        /// held batch is on its way.</summary>
        public const float RespawnGrace = 15f;

        /// <summary>res: a refused deposit's code (nothing placed), and a slot whose box is gone or out of reach now.</summary>
        public const string ResNotPlaced = "-", ResGone = "!";

        private sealed class Session
        {
            public int Id;
            public int EntityId;
            public int Kind;
            public Vector3i Anchor;
            /// <summary>The boxes' area {xMin, xMax, zMin, zMax}: an outpost's building; null = the home zone.</summary>
            public int[] Area;
            /// <summary>The claimed boxes, in scan order (deposits fill them in this order).</summary>
            public readonly List<Vector3i> Boxes = new List<Vector3i>();
            public readonly HashSet<Vector3i> BoxSet = new HashSet<Vector3i>();
            public int SkippedChunks;
            public float LastSeen;
            /// <summary>Round MP3 (review storage M2): the last cell change, or the last keep that said the window was used.</summary>
            public float LastActive;
            /// <summary>Round MP3 (review storage m8, m9): the host's own player's view - its codes are not capped (CodeFor), and it
            /// never ends on silence.</summary>
            public bool Local;
            /// <summary>When the sweep first saw the player dead (-1: alive), and when they were alive again (-1: never died).</summary>
            public float DeadSince = -1f;
            public float RespawnedAt = -1f;
            // a batch arriving in parts
            public int PartBatch = -1;
            public int PartNext;
            public bool PartBroken;
            public readonly List<Entry> PartEntries = new List<Entry>();
        }

        private struct Entry
        {
            public bool Deposit;
            public Vector3i Pos;
            public int Index;
            public string A;        // CAS: expect; deposit: the stack
            public string B;        // CAS: new
        }

        private struct SlotKey : IEquatable<SlotKey>
        {
            public Vector3i Pos;
            public int Index;
            public SlotKey(Vector3i _pos, int _index) { Pos = _pos; Index = _index; }
            public bool Equals(SlotKey o) { return Index == o.Index && Pos == o.Pos; }
            public override bool Equals(object o) { return o is SlotKey && Equals((SlotKey)o); }
            public override int GetHashCode() { return Pos.GetHashCode() * 397 ^ Index; }
        }

        private sealed class Planned
        {
            public TEFeatureStorage St;
            public ItemStack Stack;
            public string Code;
        }

        /// <summary>
        /// Round MP3 (review storage M1): one batch's planned writes, by slot and by box - a deposit's room is judged against the
        /// box as these leave it (RoomAsPlanned).
        /// </summary>
        private sealed class BatchPlan
        {
            public readonly Dictionary<SlotKey, Planned> Slots = new Dictionary<SlotKey, Planned>();
            public readonly Dictionary<Vector3i, List<int>> ByBox = new Dictionary<Vector3i, List<int>>();

            public bool TryGet(SlotKey k, out Planned p) { return Slots.TryGetValue(k, out p); }
            public bool Has(SlotKey k) { return Slots.ContainsKey(k); }
            public bool Touches(Vector3i pos) { return ByBox.ContainsKey(pos); }

            public void Set(SlotKey k, Planned p)
            {
                if (!Slots.ContainsKey(k))
                {
                    List<int> l;
                    if (!ByBox.TryGetValue(k.Pos, out l)) ByBox[k.Pos] = l = new List<int>();
                    l.Add(k.Index);
                }
                Slots[k] = p;
            }
        }

        private static readonly Dictionary<int, Session> sessions = new Dictionary<int, Session>();
        /// <summary>Free cells per box by the grid bridge, dropped for a box any writer changes: the bridge packs a box's grid per question.</summary>
        private static readonly Dictionary<Vector3i, int> freeCellsByBox = new Dictionary<Vector3i, int>();
        private static int nextSession;
        private static readonly List<int> scratchIds = new List<int>();

        /// <summary>Every machine, at the world's start and end: no session or claim carries over.</summary>
        public static void Reset()
        {
            sessions.Clear();
            freeCellsByBox.Clear();
        }

        // ------------------------------------------------------------------ claims ----

        /// <summary>Server: the player's open view's session id, -1 when none.</summary>
        public static int SessionIdOf(int entityId)
        {
            Session s;
            return sessions.TryGetValue(entityId, out s) ? s.Id : -1;
        }

        /// <summary>Server: the player's view with this session id is still open.</summary>
        public static bool HasSession(int entityId, int sessionId)
        {
            Session s;
            return sessions.TryGetValue(entityId, out s) && s.Id == sessionId;
        }

        /// <summary>
        /// Server (Patches_LinkedStorage, a postfix on TEFeatureStorage.OnLockRequestServer that answered yes): a player opened
        /// this box by hand. Round MP3 (review storage M2): no longer refused - a view left open (an AFK player) blocked every box
        /// of the base. The box leaves every view that showed it instead: from now on it belongs to the opener's machine (an open
        /// box's copy overwrites server-side writes), so a view's next change to it is refused (it is out of the session, and
        /// UsableFor sees it open) and its cells are pushed as gone. The opener's own view is closing anyway (they are opening a
        /// box): it only loses the box, and its last changes to other boxes still count.
        /// </summary>
        internal static void OnVanillaOpen(TEFeatureStorage st, int lockingPlayerId)
        {
            if (sessions.Count == 0 || st == null) return;
            Vector3i pos = st.ToWorldPos();
            var holders = new List<int>();
            foreach (Session s in sessions.Values) if (s.BoxSet.Contains(pos)) holders.Add(s.EntityId);
            for (int i = 0; i < holders.Count; i++)
            {
                Session s;
                if (!sessions.TryGetValue(holders[i], out s)) continue;
                s.BoxSet.Remove(pos);
                s.Boxes.Remove(pos);
                if (ScConfig.Verbose) ScLog.Verbose("linked storage: the box at " + pos + " was opened by hand - it leaves session " + s.Id);
                if (s.EntityId == lockingPlayerId) continue;
                try
                {
                    int n = st.ItemGrid != null ? st.ItemGrid.Length : 0;
                    var gone = new List<ScStorage.Slot>(n);
                    for (int k = 0; k < n; k++) gone.Add(new ScStorage.Slot { Pos = pos, Index = k, Code = ResGone });
                    SendSlots(s, gone);
                }
                catch (Exception e)
                {
                    ScLog.Once("storage-vanilla-open-push", e);
                }
            }
        }

        // ------------------------------------------------------------------ the rules (F3-F5) ----

        /// <summary>The player's platform id, the one TEFeatureLockable knows them by (null = unknown).</summary>
        public static PlatformUserIdentifierAbs UserIdOf(EntityPlayer p)
        {
            try
            {
                if (p == null) return null;
                if (p is EntityPlayerLocal) return PlatformManager.InternalLocalUserIdentifier;
                ConnectionManager cm = ScNet.Cm;
                ClientInfo ci = cm != null && cm.Clients != null ? cm.Clients.ForEntityId(p.entityId) : null;
                return ci != null ? ci.InternalId : null;
            }
            catch (Exception e)
            {
                ScLog.Once("storage-userid", e);
                return null;
            }
        }

        /// <summary>
        /// F3: somebody has this box open by hand - a remote player's lock (LockManager, the server's record of every open,
        /// the host's own included), or the host's own open flag. Such a box belongs to that player's machine: a server write
        /// into it would be overwritten by their copy. Server only (LockManager answers nothing on a client).
        /// </summary>
        public static bool OpenBySomeone(TEFeatureStorage st)
        {
            if (st == null) return false;
            try { if (st.IsUserAccessing()) return true; }
            catch (Exception e) { ScLog.Once("storage-accessing", e); }
            try { return ScCommunity.IsHost && LockManager.Instance.IsLockedServer(st, 0); }
            catch (Exception e) { ScLog.Once("storage-locked-server", e); return false; }
        }

        /// <summary>F5: the box is locked (TEFeatureLockable) and this player may not open it (null player id: any lock counts).</summary>
        public static bool LockedAgainst(TEFeatureStorage st, PlatformUserIdentifierAbs id)
        {
            try
            {
                TEFeatureLockable lk = st != null && st.Parent != null ? st.Parent.GetFeature<TEFeatureLockable>() : null;
                if (lk == null || !lk.IsLocked()) return false;
                return id == null || !lk.IsUserAllowed(id);
            }
            catch (Exception e)
            {
                ScLog.Once("storage-lockable", e);
                return true;
            }
        }

        /// <summary>A box a linked storage view may show and write for this player: not open by anyone, not locked against them.</summary>
        private static bool UsableFor(TEFeatureStorage st, PlatformUserIdentifierAbs viewer)
        {
            return st != null && st.ItemGrid != null && !OpenBySomeone(st) && !LockedAgainst(st, viewer);
        }

        // ------------------------------------------------------------------ codes (round MP3, review storage m9) ----

        /// <summary>
        /// A stack's code for one view: ScItemTx.Encode, capped at ScItemTx.MaxEncoded because a remote player's codes travel in
        /// requests and packages (null = too large: hidden from that view, refused from it). The host's own view never leaves
        /// this machine, so for it (local) a stack past the cap is coded in full the same way - the game's ItemStack.Write as
        /// base64, byte for byte what Encode would give without its cap - and stays visible and usable, as before round MP3.
        /// A helper here, not a second encoder: below the cap it IS ScItemTx.Encode.
        /// </summary>
        internal static string CodeFor(ItemStack s, bool local)
        {
            string c = ScItemTx.Encode(s);
            if (c != null || !local) return c;
            try
            {
                using (var ms = new System.IO.MemoryStream())
                {
                    using (PooledBinaryWriter w = MemoryPools.poolBinaryWriter.AllocSync(false))
                    {
                        w.SetBaseStream(ms);
                        s.Write(w);
                    }
                    return Convert.ToBase64String(ms.ToArray());
                }
            }
            catch (Exception e)
            {
                ScLog.Once("storage-code-full", e);
                return null;
            }
        }

        /// <summary>The stack a CodeFor string holds (never null; empty for "" or anything unreadable). Past the cap only for the
        /// host's own view; below it, ScItemTx.Decode.</summary>
        internal static ItemStack StackOf(string code, bool local)
        {
            if (!local || string.IsNullOrEmpty(code) || code.Length <= ScItemTx.MaxEncoded) return ScItemTx.Decode(code);
            try
            {
                byte[] bytes = Convert.FromBase64String(code);
                using (var ms = new System.IO.MemoryStream(bytes))
                using (PooledBinaryReader r = MemoryPools.poolBinaryReader.AllocSync(false))
                {
                    r.SetBaseStream(ms);
                    ItemStack s = new ItemStack();
                    s.Read(r);
                    if (s.itemValue == null || s.itemValue.ItemClass == null || s.count <= 0) return ItemStack.Empty.Clone();
                    return s;
                }
            }
            catch (Exception e)
            {
                ScLog.Once("storage-stack-full", e);
                return ItemStack.Empty.Clone();
            }
        }

        /// <summary>A box changed (any writer): its cached free cells go.</summary>
        public static void Invalidate(Vector3i pos)
        {
            freeCellsByBox.Remove(pos);
        }

        private static int CellsFree(Vector3i pos, TEFeatureStorage st)
        {
            if (ScStorage.BoxFreeCells == null || st == null) return -1;
            int n;
            if (freeCellsByBox.TryGetValue(pos, out n)) return n;
            n = 0;
            try { n = Math.Max(0, ScStorage.BoxFreeCells(st)); }
            catch (Exception e) { ScLog.Once("storage-freecells", e); }
            freeCellsByBox[pos] = n;
            return n;
        }

        /// <summary>The session's boxes' free slots, and free cells by the grid bridge (-1 without it).</summary>
        private static void Totals(Session s, out int freeSlots, out int freeCells)
        {
            freeSlots = 0;
            freeCells = ScStorage.BoxFreeCells != null ? 0 : -1;
            for (int b = 0; b < s.Boxes.Count; b++)
            {
                TEFeatureStorage st = ScStorage.Resolve(s.Boxes[b]);
                if (st == null || st.ItemGrid == null) continue;
                ItemStack[] items = st.ItemGrid.items;
                for (int i = 0; i < items.Length; i++) if (items[i] == null || items[i].IsEmpty()) freeSlots++;
                if (freeCells >= 0) freeCells += CellsFree(s.Boxes[b], st);
            }
        }

        // ------------------------------------------------------------------ open ----

        /// <summary>
        /// Server: opens a view for the player - the old view (if any) goes, the zone (area null) or the outpost's building is
        /// scanned, the boxes it shows are claimed, and the view goes to the player (NetPackageScStorage; the host's own player
        /// gets it at once). Returns a refusal (a localization key) or null.
        /// </summary>
        public static string Open(EntityPlayer p, int kind, Vector3i anchor, int[] area, string name)
        {
            if (p == null) return "scActionNoPlayer";
            World w = ScCommunity.World;
            if (w == null || !ScZone.HasHome) return "scNeedHome";
            End(p.entityId, null);
            ScConfig.EnsureParsed();
            // round MP3 (review storage M2, m8, m9): the idle clock starts now; the host's own view is Local
            var s = new Session { Id = ++nextSession, EntityId = p.entityId, Kind = kind, Anchor = anchor, Area = area, LastSeen = Time.unscaledTime,
                                  LastActive = Time.unscaledTime, Local = p is EntityPlayerLocal };
            var v = new ScStorageView { Session = s.Id, Kind = kind, Anchor = anchor, Name = name ?? "" };
            PlatformUserIdentifierAbs viewer = UserIdOf(p);
            int xMin, xMax, zMin, zMax;
            if (area != null) { xMin = area[0]; xMax = area[1]; zMin = area[2]; zMax = area[3]; }
            else ScZone.Bounds(out xMin, out xMax, out zMin, out zMax);
            try
            {
                for (int cx = World.toChunkXZ(xMin); cx <= World.toChunkXZ(xMax); cx++)
                {
                    for (int cz = World.toChunkXZ(zMin); cz <= World.toChunkXZ(zMax); cz++)
                    {
                        Chunk chunk = w.GetChunkSync(cx, cz) as Chunk;
                        if (chunk == null) { s.SkippedChunks++; continue; }
                        var tes = chunk.tileEntities;
                        if (tes == null || tes.list == null) continue;
                        for (int i = 0; i < tes.list.Count; i++)
                        {
                            TileEntityComposite tc = tes.list[i] as TileEntityComposite;
                            TEFeatureStorage st = tc != null ? tc.GetFeature<TEFeatureStorage>() : null;
                            if (!ScStorage.Linkable(tc, st)) continue;
                            Vector3i pos = tc.ToWorldPos();
                            if (area != null ? (pos.x < xMin || pos.x > xMax || pos.z < zMin || pos.z > zMax) : !ScZone.Contains(pos)) continue;
                            if (!UsableFor(st, viewer)) { v.SkippedBoxes++; continue; }     // F3, F5: open by someone, or locked against them
                            s.Boxes.Add(pos);
                            s.BoxSet.Add(pos);
                            ItemStack[] items = st.ItemGrid.items;          // read only; written through UpdateSlot
                            for (int k = 0; k < items.Length; k++)
                            {
                                ItemStack it = items[k];
                                if (it == null || it.IsEmpty()) continue;
                                // round MP3 (review storage m9): too large to send stays in its box, out of a REMOTE view only
                                string code = CodeFor(it, s.Local);
                                if (code == null) continue;
                                v.Slots.Add(new ScStorage.Slot { Pos = pos, Index = k, Stack = it.Clone(), Code = code });
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                ScLog.Once("storage-scan", e);
            }
            sessions[p.entityId] = s;
            v.Boxes = s.Boxes.Count;
            v.SkippedChunks = s.SkippedChunks;
            Totals(s, out v.FreeSlots, out v.FreeCells);
            if (ScConfig.Verbose)
                ScLog.Verbose("linked storage: session " + s.Id + " for " + (p.PlayerDisplayName ?? "?") + " claims " + s.Boxes.Count + " box(es), "
                              + v.Slots.Count + " stack(s), " + v.SkippedBoxes + " box(es) left out (open or locked), " + s.SkippedChunks + " chunk(s) not loaded");
            ScNet.ToPlayer(p, () => NetPackageManager.GetPackage<NetPackageScStorage>().SetupView(v), () => ScStorage.OnView(v));
            return null;
        }

        /// <summary>Server: the player's view ends and its claims go. reason (a localization key) is sent to the player when not null.</summary>
        public static void End(int entityId, string reason)
        {
            Session s;
            if (!sessions.TryGetValue(entityId, out s)) return;
            sessions.Remove(entityId);
            ScHomeLink.OnSessionEnded(entityId);
            if (ScConfig.Verbose) ScLog.Verbose("linked storage: session " + s.Id + " ended (" + (reason ?? "closed") + "), " + s.Boxes.Count + " claim(s) released");
            if (reason == null) return;
            EntityPlayer p = ScPlayers.ByEntity(entityId);
            if (p == null || !(p is EntityPlayerLocal || ScNet.ClientMatches(p))) return;
            int id = s.Id;
            ScNet.ToPlayer(p, () => NetPackageManager.GetPackage<NetPackageScStorage>().SetupClosed(id, reason), () => ScStorage.OnClosed(id, reason));
        }

        /// <summary>
        /// Server, once a second (ScTicker, in its own try: review storage m7): a view whose player is gone (disconnected,
        /// forgotten), dead for DeadHold, far from the view's anchor, silent for KeepTimeout (a remote player only: review storage
        /// m8) or idle for IdleTimeout (review storage M2) is let go, so no claim is ever stuck. A dead player's view is held (no
        /// distance, silence or idle test) until DeadHold, and for RespawnGrace after the respawn: their last batch comes back then.
        /// </summary>
        public static void Sweep()
        {
            if (sessions.Count == 0) return;
            try
            {
                float now = Time.unscaledTime;
                scratchIds.Clear();
                scratchIds.AddRange(sessions.Keys);
                for (int i = 0; i < scratchIds.Count; i++)
                {
                    Session s;
                    if (!sessions.TryGetValue(scratchIds[i], out s)) continue;
                    EntityPlayer p = ScPlayers.ByEntity(s.EntityId);
                    if (p == null || !(p is EntityPlayerLocal || ScNet.ClientMatches(p))) { End(s.EntityId, null); continue; }
                    if (p.IsDead())
                    {
                        if (s.DeadSince < 0f) s.DeadSince = now;
                        if (now - s.DeadSince > DeadHold) End(s.EntityId, null);
                        continue;
                    }
                    if (s.DeadSince >= 0f)
                    {
                        s.DeadSince = -1f;
                        s.RespawnedAt = now;
                        s.LastSeen = now;
                        s.LastActive = now;          // round MP3 (review storage M2): the idle clock starts again too
                    }
                    if (s.RespawnedAt >= 0f && now - s.RespawnedAt < RespawnGrace) continue;
                    if (TooFar(p, s.Anchor)) { End(s.EntityId, "scStorageClosedFar"); continue; }
                    // round MP3 (review storage m8): the host's own requests run inline - a long frame, a loading hitch or a
                    // suspended PC is no silence, so its view never ends on silence (it ends on close, distance, idle or death)
                    if (!s.Local && now - s.LastSeen > KeepTimeout) { End(s.EntityId, "scStorageSessionEnded"); continue; }
                    // round MP3 (review storage M2): nothing moved and the window unused for IdleTimeout - the player is told
                    // why it closed. Never in single player: there is nobody it could keep waiting.
                    if ((!s.Local || ScPlayers.All.Count > 1) && now - s.LastActive > IdleTimeout) End(s.EntityId, "scStorageClosedIdle");
                }
            }
            catch (Exception e)
            {
                ScLog.Once("storage-sweep", e);
            }
        }

        private static Session SessionOf(EntityPlayer p, int sessionId)
        {
            Session s;
            if (p == null || !sessions.TryGetValue(p.entityId, out s) || s.Id != sessionId) return null;
            s.LastSeen = Time.unscaledTime;
            return s;
        }

        /// <summary>The player stands farther than MaxDistance (flat) from the view's anchor (the loot window closes at 32 m).</summary>
        private static bool TooFar(EntityPlayer p, Vector3i anchor)
        {
            if (p == null) return true;
            Vector3 pp = p.position;
            float dx = pp.x - (anchor.x + 0.5f), dz = pp.z - (anchor.z + 0.5f);
            return dx * dx + dz * dz > MaxDistance * MaxDistance;
        }

        // ------------------------------------------------------------------ handlers ----

        /// <summary>storage.open kind x y z: the window's Open storage button and the land claim radial's Linked storage entries.</summary>
        [ScAction(OpOpen, ScActionAccess.Community)]
        private static void OpenAction(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            int kind = args.Int(0, ScStorage.KindHome);
            Vector3i claim = args.Pos(1);
            if (!ScZone.HasHome) { reply.Fail("scNeedHome"); return; }
            World w = ScCommunity.World;
            string why;
            switch (kind)
            {
                case ScStorage.KindOutpost:
                {
                    Outpost o = ScOutposts.AtClaim(claim);
                    if (o == null) { reply.Fail("scOpNotHere"); return; }
                    why = ScActionsClaim.WhyNotOwnClaim(claim, sender);
                    if (why != null) { reply.Fail(why); return; }
                    if (TooFar(sender, claim)) { reply.Fail("scStorageTooFar"); return; }      // round MP3 (review storage m4)
                    why = Open(sender, kind, claim, o.Box, o.Name);
                    break;
                }
                case ScStorage.KindHomeLink:
                {
                    if (ScOutposts.AtClaim(claim) == null) { reply.Fail("scOpNotHere"); return; }
                    why = ScActionsClaim.WhyNotOwnClaim(claim, sender);
                    if (why != null) { reply.Fail(why); return; }
                    if (TooFar(sender, claim)) { reply.Fail("scStorageTooFar"); return; }      // round MP3 (review storage m4)
                    ScHomeLink.Request(sender, claim, reply);       // answers itself: opens now, or radios home first
                    return;
                }
                default:
                {
                    // the home zone's boxes, the view standing on the home claim (as before: its chunk must be loaded here)
                    Vector3i home = ScZone.Home;
                    if (w == null || w.GetChunkSync(World.toChunkXZ(home.x), World.toChunkXZ(home.z)) == null) { reply.Fail("scNotLoaded"); return; }
                    // round MP3 (review storage m4): the view stands on the home claim and its window closes 32 m from it, so an
                    // open from farther away only flashed the window; now it is refused with a line, and a modified client can
                    // no longer read the boxes from across the map
                    if (TooFar(sender, home)) { reply.Fail("scStorageTooFar"); return; }
                    why = Open(sender, ScStorage.KindHome, home, null, null);
                    break;
                }
            }
            if (why != null) reply.Fail(why);
            else reply.Done();
        }

        /// <summary>storage.close session: the view closed and its last batch is answered - its claims go.</summary>
        [ScAction(OpClose, ScActionAccess.Community)]
        private static void CloseAction(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            if (SessionOf(sender, args.Int(0, -1)) != null) End(sender.entityId, null);      // an older view's close never ends a newer one
            reply.Done();
        }

        /// <summary>storage.keep session active: the view is still open (active 1: its window was used since the last keep).</summary>
        [ScAction(OpKeep, ScActionAccess.Community, Quiet = true)]      // round cfs1: the view's keep-alive (every few seconds) stays out of the DebugLogging log
        private static void KeepAction(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            Session s = SessionOf(sender, args.Int(0, -1));
            if (s == null) { reply.Fail("scStorageSessionEnded", ""); return; }
            if (args.Int(1, 0) != 0) s.LastActive = Time.unscaledTime;      // round MP3 (review storage M2): the idle clock
            reply.Done();
        }

        /// <summary>storage.cells session batch part parts, entries...: one part of a batch of cell changes (see the class summary).</summary>
        [ScAction(OpCells, ScActionAccess.Community)]
        private static void CellsAction(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            int sid = args.Int(0, -1), batch = args.Int(1, -1), part = args.Int(2, -1), parts = args.Int(3, -1);
            Session s = SessionOf(sender, sid);
            if (s == null) { reply.Fail("scStorageSessionEnded"); return; }    // no res: nothing was written
            float now = Time.unscaledTime;
            s.LastActive = now;                                                 // round MP3 (review storage M2): a change is activity
            // round MP3 (review storage m4): a change is judged only near the view's anchor (Sweep ends a far view once a second;
            // this closes the gap for a modified client). Not while the player is dead or just respawned: their held batch comes
            // back from the spawn point (DeadHold, RespawnGrace). Refused without res: nothing was written.
            bool justBack = s.DeadSince >= 0f || (s.RespawnedAt >= 0f && now - s.RespawnedAt < RespawnGrace);
            if (!justBack && TooFar(sender, s.Anchor))
            {
                s.PartBroken = true;
                reply.Fail("scStorageClosedFar");
                return;
            }
            int n = Math.Max(0, (args.Ints.Length - HeadInts) / EntryInts);
            if (part < 0 || parts < 1 || part >= parts || n > PartEntries || args.Strs.Length < 2 * n)
            {
                s.PartBroken = true;
                reply.Fail("scActionFailed");
                return;
            }
            if (part == 0)
            {
                s.PartBatch = batch;
                s.PartNext = 0;
                s.PartBroken = false;
                s.PartEntries.Clear();
            }
            if (s.PartBatch != batch || s.PartNext != part) s.PartBroken = true;
            s.PartNext = part + 1;
            if (!s.PartBroken)
            {
                for (int k = 0; k < n; k++)
                {
                    int o = HeadInts + k * EntryInts;
                    s.PartEntries.Add(new Entry
                    {
                        Deposit = (args.Int(o) & FlagDeposit) != 0,
                        Pos = new Vector3i(args.Int(o + 1), args.Int(o + 2), args.Int(o + 3)),
                        Index = args.Int(o + 4),
                        A = args.Str(2 * k),
                        B = args.Str(2 * k + 1)
                    });
                }
                if (s.PartEntries.Count > MaxBatchEntries) s.PartBroken = true;
            }
            if (part < parts - 1) { reply.Done(); return; }     // more parts follow: the batch is judged at the last one
            var entries = new List<Entry>(s.PartEntries);
            bool broken = s.PartBroken;
            s.PartEntries.Clear();
            s.PartBatch = -1;
            s.PartBroken = false;
            if (broken || entries.Count == 0) { reply.Fail("scActionFailed"); return; }     // nothing written (no res)
            ApplyBatch(s, sender, entries, reply);
        }

        /// <summary>
        /// Judges and applies one whole batch. Round MP3 (review storage M1, m10): every compare-and-set first, against the boxes as
        /// they are now with the batch's earlier entries laid over them - any failure refuses the whole batch and writes nothing;
        /// then every deposit, against the boxes as all of that leaves them (FindRoom, RoomAsPlanned: the grid bridge's footprint
        /// test on the planned contents, as the box's own grid would judge it), and a deposit no box has room for is answered "-"
        /// on its own while the rest is written. A batch of deposits only, none of them placed, is refused as before. Answers res
        /// for every entry.
        /// </summary>
        private static void ApplyBatch(Session s, EntityPlayer sender, List<Entry> entries, ScActionReply reply)
        {
            PlatformUserIdentifierAbs viewer = UserIdOf(sender);
            var plan = new BatchPlan();
            var placed = new SlotKey[entries.Count];
            var gone = new bool[entries.Count];
            var notPlaced = new bool[entries.Count];
            string why = null, full = null;
            int cas = 0, deposits = 0, unplaced = 0;
            // the compare-and-sets (all or nothing)
            for (int k = 0; k < entries.Count && why == null; k++)
            {
                Entry e = entries[k];
                if (e.Deposit) continue;
                cas++;
                var key = new SlotKey(e.Pos, e.Index);
                placed[k] = key;
                TEFeatureStorage st = s.BoxSet.Contains(e.Pos) ? ScStorage.Resolve(e.Pos) : null;
                if (st == null || st.ItemGrid == null || e.Index < 0 || e.Index >= st.ItemGrid.Length) { gone[k] = true; why = "scStorageBoxGone"; break; }
                if (!UsableFor(st, viewer)) { gone[k] = true; why = "scStorageInUse"; break; }
                Planned pl;
                bool wasPlanned = plan.TryGet(key, out pl);
                string cur = wasPlanned ? pl.Code : CodeFor(st.ItemGrid[e.Index], s.Local);       // round MP3 (review storage m9)
                if (cur == null || !string.Equals(cur, e.A ?? "", StringComparison.Ordinal)) { why = "scStorageChanged"; break; }
                ItemStack next = string.IsNullOrEmpty(e.B) ? ItemStack.Empty.Clone() : StackOf(e.B, s.Local);
                if (!string.IsNullOrEmpty(e.B) && (next.IsEmpty() || !Placeable(next))) { why = "scActionFailed"; break; }
                // round MP3 (review storage M1): a DIFFERENT item into a slot passes the grid bridge's footprint test on the box as
                // the batch leaves it, with this slot emptied - a box never goes past its grid. The view sends a swap as a take and,
                // once that is answered, a deposit (ScLinkedContainer.SendDirty), so this only guards another sender.
                ItemStack was = wasPlanned ? pl.Stack : st.ItemGrid[e.Index];
                bool newItem = !next.IsEmpty() && (was == null || was.IsEmpty() || was.itemValue == null || was.itemValue.type != next.itemValue.type);
                if (newItem && ScStorage.BoxHasRoomFor != null && !RoomAsPlanned(plan, e.Pos, st, next, e.Index)) { why = FullKey(s); break; }
                plan.Set(key, new Planned { St = st, Stack = next, Code = string.IsNullOrEmpty(e.B) ? "" : e.B });
            }
            // the deposits, each against the boxes as every compare-and-set and the deposits before it leave them
            for (int k = 0; k < entries.Count && why == null; k++)
            {
                Entry e = entries[k];
                if (!e.Deposit) continue;
                ItemStack stack = StackOf(e.A, s.Local);
                if (stack.IsEmpty() || !Placeable(stack)) { why = "scActionFailed"; break; }
                SlotKey at;
                TEFeatureStorage into;
                if (!FindRoom(s, viewer, stack, plan, out at, out into))
                {
                    // round MP3 (review storage m10): only this deposit is not placed ("-"): the view gives its item back
                    notPlaced[k] = true;
                    unplaced++;
                    full = FullKey(s);
                    continue;
                }
                placed[k] = at;
                plan.Set(at, new Planned { St = into, Stack = stack, Code = e.A });
                deposits++;
            }
            // nothing at all to write (deposits only, none placed): refused as a whole, as before
            if (why == null && cas == 0 && deposits == 0) why = full ?? "scActionFailed";
            if (why != null)
            {
                // nothing written: every entry's slot as it is now, so the view resyncs what it showed
                var now = new string[entries.Count];
                for (int k = 0; k < entries.Count; k++)
                {
                    Entry e = entries[k];
                    if (e.Deposit) { now[k] = Res(Vector3i.zero, -1, ResNotPlaced); continue; }
                    now[k] = Res(e.Pos, e.Index, gone[k] ? ResGone : LiveCode(s, viewer, e.Pos, e.Index));
                }
                SetResults(s, reply, now);
                reply.Fail(why);
                if (ScConfig.Verbose) ScLog.Verbose("linked storage: batch of " + entries.Count + " change(s) refused (" + why + ") for " + (sender.PlayerDisplayName ?? "?"));
                return;
            }
            Commit(s, sender, entries, plan, placed, notPlaced, unplaced, full, reply);
        }

        /// <summary>
        /// Writes a judged batch and answers it. Round MP3 (review storage m1): from the first write on, the batch IS in the boxes,
        /// so the answer is Done whatever happens after it - the answer is set right after the writes, every later step (res, the
        /// free room, the log, the other views' updates) has its own try, and res is built entry by entry with the planned code as
        /// an entry's fallback. A refusal now would make the view put back (and settle with the player) what the boxes hold.
        /// </summary>
        private static void Commit(Session s, EntityPlayer sender, List<Entry> entries, BatchPlan plan, SlotKey[] placed, bool[] notPlaced,
                                   int unplaced, string full, ScActionReply reply)
        {
            var touched = new List<SlotKey>(plan.Slots.Count);
            foreach (KeyValuePair<SlotKey, Planned> kv in plan.Slots)
            {
                try
                {
                    kv.Value.St.UpdateSlot(kv.Key.Index, kv.Value.Stack.Clone());
                }
                catch (Exception ex)
                {
                    ScLog.Once("storage-commit", ex);
                }
                // round MP3 (re-check F2): marked modified even when a listener threw - the slot was set before it ran
                try { kv.Value.St.SetModified(); }
                catch (Exception ex) { ScLog.Once("storage-commit-modified", ex); }
                Invalidate(kv.Key.Pos);
                touched.Add(kv.Key);
            }
            // done - with "no box has room for that" when a deposit is handed back (review storage m10)
            if (unplaced > 0) reply.Done(full ?? "scStorageFull", "ui_denied");
            else reply.Done();
            var res = new string[entries.Count];
            for (int k = 0; k < entries.Count; k++)
            {
                try
                {
                    if (notPlaced[k]) { res[k] = Res(Vector3i.zero, -1, ResNotPlaced); continue; }
                    TEFeatureStorage st = ScStorage.Resolve(placed[k].Pos);
                    res[k] = Res(placed[k].Pos, placed[k].Index, st != null ? LiveCodeRaw(st, placed[k].Index, s.Local) : ResGone);
                }
                catch (Exception ex)
                {
                    ScLog.Once("storage-commit-res", ex);
                    Planned pl;
                    res[k] = notPlaced[k] ? Res(Vector3i.zero, -1, ResNotPlaced)
                                          : Res(placed[k].Pos, placed[k].Index, plan.TryGet(placed[k], out pl) ? pl.Code : ResGone);
                }
            }
            SetResults(s, reply, res);
            try
            {
                if (ScConfig.Verbose)
                    ScLog.Verbose("linked storage: batch of " + entries.Count + " change(s) written for " + (sender.PlayerDisplayName ?? "?")
                                  + (unplaced > 0 ? " (" + unplaced + " deposit(s) found no room and go back)" : ""));
            }
            catch (Exception ex) { ScLog.Once("storage-commit-log", ex); }
            try { PushSlots(s, touched); }
            catch (Exception ex) { ScLog.Once("storage-commit-push", ex); }
        }

        /// <summary>The reply's hints: res first (the view settles by it), then the free room in its own try (round MP3, review
        /// storage m1: a bridge fault there never costs res).</summary>
        private static void SetResults(Session s, ScActionReply reply, string[] res)
        {
            reply.SetHint("res", string.Join("|", res));
            try
            {
                int fs, fc;
                Totals(s, out fs, out fc);
                reply.SetHint("fs", fs);
                reply.SetHint("fc", fc);
            }
            catch (Exception e)
            {
                ScLog.Once("storage-reply-totals", e);
            }
        }

        /// <summary>A stack a box may take: a known item, at most a full stack, and not one the game keeps out of containers (quest items).</summary>
        private static bool Placeable(ItemStack s)
        {
            try
            {
                ItemClass ic = s != null && s.itemValue != null ? s.itemValue.ItemClass : null;
                if (ic == null || s.count <= 0) return false;
                if (s.count > Math.Max(ic.MaxCount, ic.Stacknumber.Value)) return false;
                return ic.CanPlaceInContainer();
            }
            catch (Exception e)
            {
                ScLog.Once("storage-placeable", e);
                return false;
            }
        }

        /// <summary>A slot's code now: ResGone when its box is gone, out of the session, or out of reach for the viewer.</summary>
        private static string LiveCode(Session s, PlatformUserIdentifierAbs viewer, Vector3i pos, int index)
        {
            TEFeatureStorage st = s.BoxSet.Contains(pos) ? ScStorage.Resolve(pos) : null;
            if (st == null || st.ItemGrid == null || index < 0 || index >= st.ItemGrid.Length || !UsableFor(st, viewer)) return ResGone;
            return LiveCodeRaw(st, index, s.Local);
        }

        /// <summary>A slot's code for one view (local: the host's own, uncapped - round MP3, review storage m9); ResGone when the
        /// box is gone or the stack is too large for that view.</summary>
        private static string LiveCodeRaw(TEFeatureStorage st, int index, bool local)
        {
            if (st == null || st.ItemGrid == null || index < 0 || index >= st.ItemGrid.Length) return ResGone;
            return CodeFor(st.ItemGrid[index], local) ?? ResGone;
        }

        /// <summary>
        /// A deposit's slot: the first claimed box (scan order) the player may use with a free slot not planned in this batch, and
        /// with the grid bridge, room for the stack's footprint (the bridge runs here, where the box lives). Cached "full" answers
        /// are hints: one more look with every box asked afresh, as before. Round MP3 (review storage M1): a box the batch already
        /// writes is judged as the batch leaves it (RoomAsPlanned), never by its live figures, which do not know those writes.
        /// </summary>
        private static bool FindRoom(Session s, PlatformUserIdentifierAbs viewer, ItemStack stack, BatchPlan plan, out SlotKey at, out TEFeatureStorage into)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                bool cached = false;
                for (int b = 0; b < s.Boxes.Count; b++)
                {
                    Vector3i pos = s.Boxes[b];
                    TEFeatureStorage st = ScStorage.Resolve(pos);
                    if (!UsableFor(st, viewer)) continue;
                    bool touched = plan.Touches(pos);
                    if (!touched && ScStorage.BoxFreeCells != null)
                    {
                        cached |= freeCellsByBox.ContainsKey(pos);
                        if (CellsFree(pos, st) <= 0) continue;
                    }
                    if (ScStorage.BoxHasRoomFor != null)
                    {
                        bool room;
                        if (touched) room = RoomAsPlanned(plan, pos, st, stack, -1);
                        else
                        {
                            try { room = ScStorage.BoxHasRoomFor(st, stack); }
                            catch (Exception e) { ScLog.Once("storage-room", e); room = true; }
                        }
                        if (!room) continue;
                    }
                    ItemStack[] items = st.ItemGrid.items;
                    for (int i = 0; i < items.Length; i++)
                    {
                        if (items[i] != null && !items[i].IsEmpty()) continue;
                        var key = new SlotKey(pos, i);
                        if (plan.Has(key)) continue;           // a slot the batch writes is never reused within it (res names one slot)
                        at = key;
                        into = st;
                        return true;
                    }
                }
                if (pass > 0 || !cached) break;
                for (int b = 0; b < s.Boxes.Count; b++) freeCellsByBox.Remove(s.Boxes[b]);
            }
            at = default(SlotKey);
            into = null;
            return false;
        }

        /// <summary>
        /// Round MP3 (review storage M1): the grid bridge's footprint test (ScStorage.BoxHasRoomFor) on the box as this batch leaves
        /// it - its planned slots laid over the live ones, and emptyIndex (a slot about to take a different item, -1 for none)
        /// emptied. The bridge reads the box's own slot array (packed afresh per question), so the planned stacks are put into
        /// that array for the one call and the box's own slot objects are put back in the finally: plain array entries, so no
        /// SlotChanged, no SetModified, nothing is sent or saved, and nothing else runs in between (the main thread; the game
        /// serializes a loaded chunk's boxes on the main thread too: NetPackageTileEntity.Setup, ChunkProviderGenerateWorld's
        /// SaveRandomChunks). A fault answers "no room" (refused, never past the grid).
        /// </summary>
        private static bool RoomAsPlanned(BatchPlan plan, Vector3i pos, TEFeatureStorage st, ItemStack stack, int emptyIndex)
        {
            if (ScStorage.BoxHasRoomFor == null) return true;
            ItemStack[] items = st != null && st.ItemGrid != null ? st.ItemGrid.items : null;
            if (items == null) return false;
            var saved = new List<KeyValuePair<int, ItemStack>>();
            try
            {
                List<int> idx;
                if (plan.ByBox.TryGetValue(pos, out idx))
                {
                    for (int i = 0; i < idx.Count; i++)
                    {
                        int n = idx[i];
                        Planned p;
                        if (n < 0 || n >= items.Length || !plan.TryGet(new SlotKey(pos, n), out p)) continue;
                        saved.Add(new KeyValuePair<int, ItemStack>(n, items[n]));
                        items[n] = p.Stack != null && !p.Stack.IsEmpty() ? p.Stack.Clone() : ItemStack.Empty.Clone();
                    }
                }
                if (emptyIndex >= 0 && emptyIndex < items.Length)
                {
                    saved.Add(new KeyValuePair<int, ItemStack>(emptyIndex, items[emptyIndex]));
                    items[emptyIndex] = ItemStack.Empty.Clone();
                }
                return ScStorage.BoxHasRoomFor(st, stack);
            }
            catch (Exception e)
            {
                ScLog.Once("storage-room-planned", e);
                return false;
            }
            finally
            {
                // newest first: a slot saved twice gets its own (first-saved) object back last
                for (int i = saved.Count - 1; i >= 0; i--) items[saved[i].Key] = saved[i].Value;
            }
        }

        /// <summary>Why a deposit found no room (round m1p's keys): no box has room, or the view links none at all.</summary>
        private static string FullKey(Session s)
        {
            if (s.Boxes.Count > 0) return "scStorageFull";
            if (s.SkippedChunks > 0) return "scStorageNoBoxesUnloaded";
            return s.Kind == ScStorage.KindOutpost ? "scStorageNoBoxesOutpost" : "scStorageNoBoxes";
        }

        /// <summary>
        /// Every open view except `except` (null: every view) that shows a written slot gets the slot's new code and its fresh free
        /// room. Round MP3 (review storage m9): each view's code is its own - capped for a remote player, where a stack too large
        /// to send goes as "!" (the cell leaves that view; the stack stays in its box), uncapped for the host's own.
        /// </summary>
        private static void PushSlots(Session except, List<SlotKey> keys)
        {
            if (sessions.Count == 0 || keys == null || keys.Count == 0) return;
            var targets = new List<Session>(sessions.Count);
            foreach (Session o in sessions.Values) if (!ReferenceEquals(o, except)) targets.Add(o);
            for (int t = 0; t < targets.Count; t++)
            {
                Session o = targets[t];
                List<ScStorage.Slot> list = null;
                for (int i = 0; i < keys.Count; i++)
                {
                    if (!o.BoxSet.Contains(keys[i].Pos)) continue;
                    if (list == null) list = new List<ScStorage.Slot>();
                    list.Add(new ScStorage.Slot { Pos = keys[i].Pos, Index = keys[i].Index, Code = LiveCodeRaw(ScStorage.Resolve(keys[i].Pos), keys[i].Index, o.Local) });
                }
                if (list != null) SendSlots(o, list);
            }
        }

        /// <summary>One view's slot updates (NetPackageScStorage; the host's own view gets them at once).</summary>
        private static void SendSlots(Session o, List<ScStorage.Slot> updates)
        {
            EntityPlayer p = ScPlayers.ByEntity(o.EntityId);
            if (p == null || !(p is EntityPlayerLocal || ScNet.ClientMatches(p))) return;
            int fs, fc, id = o.Id;
            Totals(o, out fs, out fc);
            ScNet.ToPlayer(p, () => NetPackageManager.GetPackage<NetPackageScStorage>().SetupSlots(id, updates, fs, fc),
                           () => ScStorage.OnSlots(id, CopySlots(updates), fs, fc));
        }

        /// <summary>
        /// Round MP3 (review storage M2): an NPC writer (ScStorage.DepositInZone, TopUpInZone, TakeOneInZone) wrote these box
        /// slots, claimed or not - every view that shows one gets its new content, as after a commit. A view's own change to such
        /// a slot was made against the old content, so the compare-and-set refuses it and the view resyncs: nothing is lost or
        /// doubled either way. Server only; never throws.
        /// </summary>
        public static void PushWritten(List<KeyValuePair<Vector3i, int>> slots)
        {
            if (sessions.Count == 0 || slots == null || slots.Count == 0) return;
            try
            {
                var keys = new List<SlotKey>(slots.Count);
                for (int i = 0; i < slots.Count; i++) keys.Add(new SlotKey(slots[i].Key, slots[i].Value));
                PushSlots(null, keys);
            }
            catch (Exception e)
            {
                ScLog.Once("storage-push-npc", e);
            }
        }

        private static List<ScStorage.Slot> CopySlots(List<ScStorage.Slot> list)
        {
            var copy = new List<ScStorage.Slot>(list.Count);
            for (int i = 0; i < list.Count; i++) copy.Add(new ScStorage.Slot { Pos = list[i].Pos, Index = list[i].Index, Code = list[i].Code });
            return copy;
        }

        // ------------------------------------------------------------------ res (a reply hint) ----

        /// <summary>
        /// One entry of the res hint: "x,y,z,index,code" with base64's '=' as '~' (a hint value holds no ';' or '='). This is the
        /// positional stack list the view needs (ScItemTx.StacksToHint skips empty stacks, and an empty slot is an answer here).
        /// </summary>
        private static string Res(Vector3i pos, int index, string code)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var sb = new StringBuilder(32 + (code != null ? code.Length : 0));
            sb.Append(pos.x.ToString(inv)).Append(',').Append(pos.y.ToString(inv)).Append(',').Append(pos.z.ToString(inv)).Append(',')
              .Append(index.ToString(inv)).Append(',');
            if (!string.IsNullOrEmpty(code)) sb.Append(code.Replace('=', '~'));
            return sb.ToString();
        }

        /// <summary>The view's side of Res: one parsed entry per batch entry (false when the hint does not have them all).</summary>
        public static bool ParseResults(string hint, int count, out ScStorage.Slot[] results)
        {
            results = null;
            if (string.IsNullOrEmpty(hint) || count <= 0) return false;
            string[] parts = hint.Split('|');
            if (parts.Length != count) return false;
            var r = new ScStorage.Slot[count];
            for (int i = 0; i < count; i++)
            {
                string[] f = parts[i].Split(new[] { ',' }, 5);
                int x, y, z, idx;
                if (f.Length != 5 || !Int(f[0], out x) || !Int(f[1], out y) || !Int(f[2], out z) || !Int(f[3], out idx)) return false;
                r[i] = new ScStorage.Slot { Pos = new Vector3i(x, y, z), Index = idx, Code = f[4].Replace('~', '=') };
            }
            results = r;
            return true;
        }

        private static bool Int(string s, out int v)
        {
            return int.TryParse(s, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out v);
        }
    }
}
