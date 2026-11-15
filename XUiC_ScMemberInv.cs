using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Audio;
using UnityEngine;
using UnityEngine.Scripting;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round n10 (3.4): the member inventory panel's window group, "scmemberinv" (Config/XUi_InGame/xui.xml), resolved as
    /// controller="SurvivorCommunity.ScMemberInvGroup, SurvivorCommunity". "Show me your inventory" on one of the community's
    /// members opens it instead of the game's looting screen (Patches_MemberInv: a prefix on SCore's
    /// EntityUtilities.OpenContainer, where both of SCore's paths end - the host's, and a multiplayer client's once the server
    /// has sent it a copy of the container). Strangers, prospects, raiders, enclave and band people keep the looting screen.
    ///
    /// Modeled on vanilla's XUiC_LootWindowGroup.openContainer / OnClose, with SCore's two loot-window hooks folded in (they
    /// are patches on XUiC_LootWindowGroup and never see this group): on close, an NPC the player talked to last (the
    /// CurrentNPC cvar) re-reads its weapons (SCore's Harmony.Dialog.XUiC_LootWindowGroupOnClose), and a multiplayer client
    /// sends its copy of the container back to the server (SCore's XUiC_LootWindowGroup_OnClose). Vanilla's SetModified on
    /// close is left out: it only runs for a container that is the world's tile entity at its position, which SCore's never
    /// is (its parent is a composite with no block), so vanilla never ran it for this container either.
    ///
    /// Windows: windowScMemberInv (XUiC_ScMemberInv, everything of hers, anchored and draggable), windowScMemberInvSpace (an
    /// empty window of the same size in the Left stack panel: it reserves the looting window's place, so the backpack's
    /// columns - vanilla's backpack and item info, or AshfallUI's INSPECT and inventory - lay out exactly as they do for
    /// looting, and gives the panel its default position) and the non-paging header. The backpack group opens beside it as
    /// vanilla's looting does.
    /// </summary>
    [Preserve]
    public class XUiC_ScMemberInvGroup : XUiController
    {
        public const string ID = "scmemberinv";
        public const string SpaceWindow = "windowScMemberInvSpace";

        private XUiC_ScMemberInv panel;
        private XUiC_WindowNonPagingHeader header;
        private XUiV_Window space;

        /// <summary>The container on show (SCore's SCoreLootContainer: HarvestManager's, or a client's copy), null while closed.</summary>
        public TEFeatureStorage Te { get; private set; }
        /// <summary>The member whose inventory it is, -1 while closed.</summary>
        public int NpcId { get; private set; } = -1;

        public override void Init()
        {
            base.Init();
            panel = GetChildByType<XUiC_ScMemberInv>();
            header = GetChildByType<XUiC_WindowNonPagingHeader>();
            foreach (XUiController c in Children)
                if (c != null && c.ViewComponent is XUiV_Window w && w.ID == SpaceWindow) space = w;
            if (panel == null) ScLog.Err("member inventory: the group '" + ID + "' has no windowScMemberInv (xui.xml / windows.xml) - members get the game's looting screen");
            else panel.Space = space;
        }

        // ------------------------------------------------------------------ opening ----

        /// <summary>Is this NPC one of the community's members (the host's registry; a client has none and reads the synced member flag)?</summary>
        public static bool IsMember(EntityAlive npc)
        {
            if (npc == null || npc.IsDead()) return false;
            if (ScRegistry.GetMember(npc.entityId) != null) return true;
            // a multiplayer client: the registry lives on the host, which stamps sc_member on every member's body (a synced
            // cvar); a radio stand-in carries it too and is no member
            return !ScCommunity.IsHost && npc.Buffs != null && npc.Buffs.HasCustomVar("sc_member") && !npc.Buffs.HasCustomVar(ScRadio.StandInCvar);
        }

        /// <summary>
        /// Opens the panel on this container for its member, as SCore's EntityUtilities.OpenContainer opens the looting screen
        /// (every other modal window closed first, the talk window too). False when the group is missing (windows.xml or
        /// xui.xml did not load): the caller then lets SCore open the game's screen. Everything is checked here, before
        /// the group opens: a window group must never close itself from its OnOpen (xui-gotchas).
        /// </summary>
        public static bool Open(EntityPlayerLocal player, SCoreLootContainer te, EntityAlive npc)
        {
            LocalPlayerUI ui = player != null ? player.PlayerUI : null;
            if (ui == null) ui = LocalPlayerUI.GetUIForPrimaryPlayer();
            XUiC_ScMemberInvGroup g = ui != null && ui.xui != null ? ui.xui.FindWindowGroupByName(ID) as XUiC_ScMemberInvGroup : null;
            if (g == null || g.panel == null || g.panel.Pack == null)
            {
                ScLog.Once("memberinv-missing", "member inventory: window group '" + ID + "' or its pack grid not found - members get the game's looting screen");
                return false;
            }
            // vanilla's grid refresh (XUiC_LootContainer.OnTileEntityChanged) indexes one cell per container slot: a container
            // bigger than the panel's cells (SCore's is 8 x 8 = 64; the panel has 100) would throw on every change
            XUiC_ItemStack[] cells = g.panel.Pack.GetItemStackControllers();
            if (te.ItemGrid == null || cells == null || te.ItemGrid.Length > cells.Length)
            {
                ScLog.Once("memberinv-too-big", "member inventory: SCore's container has " + (te.ItemGrid != null ? te.ItemGrid.Length : 0)
                           + " slots and the panel's pack " + (cells != null ? cells.Length : 0) + " cells - members get the game's looting screen"
                           + " (Tools/generate_member_inventory_ui.py PACK_ROWS)");
                return false;
            }
            ui.windowManager.CloseAllOpenModalWindows();
            // vanilla's OpenLooting: an effect on the player can forbid looting (a twitch action)
            if (EffectManager.GetValue(PassiveEffects.DisableLoot, null, 0f, player, null, BlockTags(te)) > 0f)
            {
                Manager.PlayInsidePlayerHead("twitch_no_attack");
                LockManager.Instance.UnlockRequestLocal();
                // a multiplayer client: the server opened a session for this copy - hand it back unchanged, or her
                // inventory stays "in use" for everyone until this client leaves (SCore's own screen has the same hole)
                try { SendClientCopy(te); }
                catch (Exception e) { ScLog.Once("memberinv-disableloot-copy", e); }
                return true;
            }
            return g.Show(te, npc);
        }

        /// <summary>The block tags vanilla's DisableLoot test reads (the container's block); none for a container without a chunk.</summary>
        private static FastTags<TagGroup.Global> BlockTags(TEFeatureStorage te)
        {
            try
            {
                Block b = te != null ? te.blockValue.Block : null;
                if (b != null) return b.Tags;
            }
            catch (Exception) { }   // SCore's container may have no chunk: no block, no tags
            return FastTags<TagGroup.Global>.none;
        }

        /// <summary>
        /// Vanilla's openContainer: the container held open, the panel filled, the group and the backpack opened, laid out.
        /// False when it failed part way: the container is let go again (a user-accessing flag left set would hold SC's ammo
        /// moves for her for the rest of the session) and the caller lets SCore open the looting screen instead.
        /// </summary>
        private bool Show(SCoreLootContainer te, EntityAlive npc)
        {
            GUIWindowManager wm = xui.playerUI.windowManager;
            try
            {
                Te = te;
                NpcId = npc.entityId;
                te.SetUserAccessing(true);
                panel.SetTarget(te, npc);
                wm.Open(windowGroup, _bModal: true);
                QuestEventManager.Current.OpenedContainer(te.ToWorldPos(), te, false);
                wm.Open("backpack", _bModal: false);
                if (header != null) header.SetHeader(Localization.Get("scInvHeader"));
                xui.RecenterWindowGroup(windowGroup);
                panel.PlaceNow();
                ScLog.Verbose("member inventory: opened for " + ScCommunity.DisplayName(npc) + " (id " + npc.entityId + ")");
                return true;
            }
            catch (Exception e)
            {
                ScLog.Once("memberinv-show", e);
                // SCore's looting screen opens on the same container next: a client's pending copy stays for it (its close
                // sends it back), so the panel's OnClose must not send it and end the server's session now
                abandoning = true;
                try
                {
                    if (wm.IsWindowOpen(ID)) wm.Close(ID);       // its OnClose lets the container go
                    else te.SetUserAccessing(false);
                }
                catch (Exception) { te.SetUserAccessing(false); }
                finally { abandoning = false; }
                Te = null;
                NpcId = -1;
                return false;
            }
        }

        /// <summary>Set while a failed Show closes the group again (SCore's screen takes over the client's copy).</summary>
        private bool abandoning;

        public override void OnClose()
        {
            TEFeatureStorage te = Te;
            int id = NpcId;
            try { ReArm(); }                 // SCore's Harmony.Dialog prefix ran before vanilla's close; so here
            catch (Exception e) { ScLog.Once("memberinv-close-weapon", e); }
            base.OnClose();
            try
            {
                xui.playerUI.windowManager.Close("backpack");
                if (te != null)
                {
                    te.SetUserAccessing(false);
                    if (!abandoning) SendClientCopy(te);
                }
                LockManager.Instance.UnlockRequestLocal();
            }
            catch (Exception e)
            {
                ScLog.Once("memberinv-close", e);
            }
            finally
            {
                Te = null;
                NpcId = -1;
            }
            if (id >= 0) ScLog.Verbose("member inventory: closed for id " + id);
        }

        /// <summary>SCore's close hook for its talk NPC: the NPC re-reads its weapons from what it now carries (a client sends it).</summary>
        private void ReArm()
        {
            EntityPlayerLocal p = xui.playerUI.entityPlayer;
            if (p == null || p.Buffs == null || !p.Buffs.HasCustomVar("CurrentNPC")) return;
            IEntityAliveSDX sdx = p.world.GetEntity((int)p.Buffs.GetCustomVar("CurrentNPC")) as IEntityAliveSDX;
            if (sdx == null) return;
            sdx.UpdateWeapon();
            if (!ScCommunity.IsHost) sdx.SendSyncData(1);
        }

        /// <summary>
        /// SCore's client close hook: a multiplayer client's grid showed its own copy of the container (NetPackageHarvestInventoryData);
        /// what it holds now goes back to the server, which merges it (HarvestManager.EndRemoteSession).
        /// </summary>
        private static void SendClientCopy(TEFeatureStorage te)
        {
            if (HarvestManager.ClientPendingEntityId == -1 || HarvestManager.ClientPendingContainer == null
                || !ReferenceEquals(te, HarvestManager.ClientPendingContainer)) return;
            string items = EntitySyncUtils.SerializeItemStackArray(HarvestManager.ClientPendingContainer.items);
            SingletonMonoBehaviour<ConnectionManager>.Instance.SendToServer(
                NetPackageManager.GetPackage<NetPackageHarvestInventoryUpdate>().Setup(HarvestManager.ClientPendingEntityId, items));
            HarvestManager.ClientPendingEntityId = -1;
            HarvestManager.ClientPendingContainer = null;
        }

        // ------------------------------------------------------------------ queries ----

        /// <summary>The open group on the local player's screen, or null (none open, a dedicated server).</summary>
        private static XUiC_ScMemberInvGroup OpenGroup()
        {
            if (GameManager.IsDedicatedServer) return null;
            LocalPlayerUI ui = LocalPlayerUI.GetUIForPrimaryPlayer();
            if (ui == null || ui.windowManager == null || ui.xui == null || !ui.windowManager.IsWindowOpen(ID)) return null;
            return ui.xui.FindWindowGroupByName(ID) as XUiC_ScMemberInvGroup;
        }

        /// <summary>Does the panel show this container right now (SCore's StorageLockUtils asks this about the looting screen)?</summary>
        public static bool IsShowing(TEFeatureStorage storage)
        {
            if (storage == null) return false;
            XUiC_ScMemberInvGroup g = OpenGroup();
            return g != null && ReferenceEquals(g.Te, storage);
        }

        /// <summary>Does the panel show this NPC's inventory right now?</summary>
        public static bool ShowsNpc(int entityId)
        {
            XUiC_ScMemberInvGroup g = OpenGroup();
            return g != null && g.NpcId == entityId && entityId >= 0;
        }

        /// <summary>Closes the panel when it shows this NPC's inventory (the end of a talk hold). True when it closed.</summary>
        public static bool CloseFor(int entityId)
        {
            if (!ShowsNpc(entityId)) return false;
            LocalPlayerUI.GetUIForPrimaryPlayer().windowManager.Close(ID);
            return true;
        }

        /// <summary>Closes the panel when it shows this container (its NPC's death: Patches_Npc's CloseHostWindow). True when it closed.</summary>
        public static bool CloseIfShowing(TEFeatureStorage storage)
        {
            if (!IsShowing(storage)) return false;
            LocalPlayerUI.GetUIForPrimaryPlayer().windowManager.Close(ID);
            return true;
        }

        /// <summary>The open panel's window controller (for sc ui), or null.</summary>
        public static XUiC_ScMemberInv OpenPanel()
        {
            XUiC_ScMemberInvGroup g = OpenGroup();
            return g != null ? g.panel : null;
        }
    }

    /// <summary>
    /// Round n10 (3.4): the member inventory panel's one window, windowScMemberInv (generated by
    /// Tools/generate_member_inventory_ui.py). Top to bottom: the title bar (her name, "Member · status · T2", the close
    /// button; the drag handle of the whole window, task t40), her card (XUiC_ScMemberCard: the talk snapshot's card bound to
    /// her) beside her equipment (ScMemberGear: the pair, the armor, the best tool per job - display only in Phase 1,
    /// XUiC_ScGearSlot), then her storage: the PACK (her inventory window, SCore's container, on vanilla's own
    /// XUiC_LootContainer so drag and drop, shift-click and take-all are the game's) and the BAG (npc.bag, XUiC_ScBagGrid;
    /// shown on the host only: a multiplayer client has no copy of an NPC's bag).
    ///
    /// The window is anchored at the screen's top-left (not a stack panel), so the layout never moves it: by default it sits
    /// where windowScMemberInvSpace was laid out (the looting window's place, left of the backpack), and once dragged where
    /// the player left it - remembered in ScUiLayout (%APPDATA%\7DaysToDie\SurvivorCommunity\ui_layout.txt) across closing,
    /// reopening and worlds. Right-click on the title bar or `sc ui reset` puts it back.
    /// </summary>
    [Preserve]
    public class XUiC_ScMemberInv : XUiController
    {
        private const float RefreshSeconds = 0.5f;
        private const string Dot = " · ";

        internal XUiV_Window Space;
        internal XUiC_LootContainer Pack { get; private set; }
        private XUiC_ScBagGrid bag;
        private XUiC_ScMemberCard card;
        private XUiV_Grid packGrid;
        private int packCols = 10;
        private XUiC_ScGearSlot[] weaponSlots = new XUiC_ScGearSlot[0];
        private XUiC_ScGearSlot[] armorSlots = new XUiC_ScGearSlot[0];
        private XUiC_ScGearSlot[] toolSlots = new XUiC_ScGearSlot[0];

        private TEFeatureStorage te;
        private EntityAlive npc;
        private float nextRefresh;
        private string armorKey;
        private bool dragging;
        private Vector2 dragPos;
        private bool wasReleased, activeKeyDown;

        // bound values: every one set from the constructor on (bindings are read while the XML is parsed)
        private string name = "", sub = "", packCount = "", bagCount = "", rName = "", rInfo = "", mName = "", mInfo = "";
        private int packSlots;
        private readonly string[] toolNotes = { "", "", "", "", "" };

        public override void Init()
        {
            base.Init();
            Pack = GetChildByType<XUiC_LootContainer>();
            bag = GetChildByType<XUiC_ScBagGrid>();
            card = GetChildByType<XUiC_ScMemberCard>();
            if (Pack != null)
            {
                packGrid = Pack.grid;
                if (packGrid != null && packGrid.Columns > 0) packCols = packGrid.Columns;
            }
            weaponSlots = Slots("weaponSlots");
            armorSlots = Slots("armorSlots");
            toolSlots = Slots("toolSlots");
            Hook("btnClose", (s, b) => { if (b == -1) xui.playerUI.windowManager.Close(windowGroup); });
            Hook("btnPackTakeAll", (s, b) => { if (b == -1) TakeAll(Pack); });
            Hook("btnBagTakeAll", (s, b) => { if (b == -1) TakeAll(bag); });
            XUiController bar = GetChildById("dragBar");
            if (bar != null)
            {
                bar.OnDrag += OnBarDrag;
                bar.OnRightPress += (s, b) => ResetPosition();
            }
            else ScLog.Err("member inventory: no dragBar in windowScMemberInv - the window cannot be moved");
            if (Pack == null) ScLog.Err("member inventory: no LootContainer grid in windowScMemberInv");
            if (bag == null) ScLog.Err("member inventory: no ScBagGrid in windowScMemberInv");
            if (weaponSlots.Length < 2 || armorSlots.Length < 4 || toolSlots.Length < ScMemberGear.JobCount)
                ScLog.Err("member inventory: equipment slots missing (weapons " + weaponSlots.Length + "/2, armor " + armorSlots.Length
                          + "/4, tools " + toolSlots.Length + "/" + ScMemberGear.JobCount + ")");
        }

        private XUiC_ScGearSlot[] Slots(string gridId)
        {
            XUiController g = GetChildById(gridId);
            return g != null ? g.GetChildrenByType<XUiC_ScGearSlot>() : new XUiC_ScGearSlot[0];
        }

        private void Hook(string id, XUiEvent_OnPressEventHandler h)
        {
            XUiController c = GetChildById(id);
            if (c == null) { ScLog.Err("member inventory: no control named '" + id + "' in windowScMemberInv"); return; }
            c.OnPress += h;   // OnPress: the left button only (3.4's Pressed sends a right click to OnRightPress)
        }

        // ------------------------------------------------------------------ target ----

        /// <summary>Fills the panel for this member (before the group opens: the pack grid's OnOpen needs its container).</summary>
        internal void SetTarget(TEFeatureStorage storage, EntityAlive member)
        {
            te = storage;
            npc = member;
            armorKey = null;
            Pack.SetSlots(storage);
            // SetSlots lays the grid out in the container's own columns (8); the panel's are wider
            if (packGrid != null) { packGrid.Columns = packCols; packGrid.Rows = (storage.ItemGrid.Length + packCols - 1) / packCols; }
            packSlots = storage.ItemGrid.Length;
            if (bag != null)
            {
                if (ScCommunity.IsHost) bag.Bind(member);
                else bag.Unbind();
            }
            if (card != null) card.Target = member;
            Refresh(true);
        }

        public override void OnOpen()
        {
            base.OnOpen();
            wasReleased = false;
            activeKeyDown = false;
            dragging = false;
            nextRefresh = 0f;
        }

        public override void OnClose()
        {
            base.OnClose();
            if (bag != null) bag.Unbind();
            if (card != null) card.Target = null;
            dragging = false;
            te = null;
            npc = null;
        }

        public override void Update(float _dt)
        {
            base.Update(_dt);
            if (!windowGroup.isShowing) return;
            try
            {
                // vanilla's loot window: E (Activate) pressed and released again closes it; R (Reload) takes all from the pack
                PlayerActionsLocal input = xui.playerUI.playerInput;
                if (!input.PermanentActions.Activate.IsPressed) wasReleased = true;
                if (wasReleased && !xui.playerUI.windowManager.IsInputActive())
                {
                    if (input.PermanentActions.Activate.IsPressed) activeKeyDown = true;
                    if (input.PermanentActions.Activate.WasReleased && activeKeyDown)
                    {
                        activeKeyDown = false;
                        xui.playerUI.windowManager.Close(windowGroup);
                        return;
                    }
                    if (input.PermanentActions.Reload.WasPressed || input.GUIActions.LeftStick.WasPressed) TakeAll(Pack);
                }
                if (!dragging) Place();
                float now = Time.unscaledTime;
                if (now < nextRefresh) return;
                nextRefresh = now + RefreshSeconds;
                // she died, was unloaded or walked off: the panel goes, as vanilla's loot window goes at a distance
                if (npc == null || npc.IsDead() || xui.playerUI.entityPlayer == null
                    || (npc.position - xui.playerUI.entityPlayer.position).sqrMagnitude > (Constants.cCollectItemDistance + 30f) * (Constants.cCollectItemDistance + 30f))
                {
                    xui.playerUI.windowManager.Close(windowGroup);
                    return;
                }
                Refresh(false);
            }
            catch (Exception e)
            {
                ScLog.Once("memberinv-update", e);
            }
        }

        /// <summary>The title, the counts and the equipment, read again; bindings refreshed when anything changed.</summary>
        private void Refresh(bool force)
        {
            if (npc == null || te == null) return;
            bool changed = force;
            Member m = ScRegistry.GetMember(npc.entityId);
            changed |= Set(ref name, ScCommunity.DisplayName(npc));
            changed |= Set(ref sub, SubTitle(m));
            changed |= Set(ref packCount, CountText(te.ItemGrid.items, te.ItemGrid.Length));
            changed |= Set(ref bagCount, bag != null && bag.Bound ? CountText(npc.bag.ItemGrid.items, bag.SlotCount) : "");

            ScMemberGear.WeaponView r = ScMemberGear.Weapon(npc, true), w = ScMemberGear.Weapon(npc, false);
            changed |= Set(ref rName, r.Name) | Set(ref rInfo, r.Info) | Set(ref mName, w.Name) | Set(ref mInfo, w.Info);
            if (weaponSlots.Length > 0) weaponSlots[0].Show(r.Stack);
            if (weaponSlots.Length > 1) weaponSlots[1].Show(w.Stack);

            string key = ScMemberGear.ArmorKey(npc);
            if (key != armorKey)
            {
                armorKey = key;
                ItemStack[] a = ScMemberGear.Armor(npc);
                for (int i = 0; i < armorSlots.Length; i++) armorSlots[i].Show(a != null && i < a.Length ? a[i] : ItemStack.Empty);
            }

            ScMemberGear.ToolPick[] tools = ScMemberGear.Tools(npc, te.ItemGrid.items);
            for (int i = 0; i < toolSlots.Length && i < tools.Length; i++)
            {
                ScMemberGear.ToolPick t = tools[i];
                toolSlots[i].Show(t != null ? t.Stack : ItemStack.Empty);
                string note = t == null ? "" : t.Broken ? Localization.Get("scInvBroken") : t.Shared ? Localization.Get("scInvShared") : "";
                changed |= Set(ref toolNotes[i], note);
            }
            if (changed) RefreshBindings();
        }

        /// <summary>"Member · At base · T2" (a client, without her record: "Member · T2").</summary>
        private string SubTitle(Member m)
        {
            var parts = new List<string> { Localization.Get("scInvMember") };
            if (m != null) parts.Add(XUiC_ScCommunity.StatusText(m, true));
            parts.Add(string.Format(Localization.Get("scInvTier"), ScXnpc.TierOf(ScCommunity.ClassName(npc)) + 1));
            return string.Join(Dot, parts.ToArray());
        }

        private static string CountText(ItemStack[] items, int length)
        {
            int n = 0;
            if (items != null) for (int i = 0; i < items.Length && i < length; i++) if (items[i] != null && !items[i].IsEmpty()) n++;
            return string.Format(Localization.Get("scInvOf"), n, length);
        }

        /// <summary>Everything in a grid into the player's inventory: vanilla's take-all (XUiC_ContainerStandardControls.MoveAll).</summary>
        private void TakeAll(XUiC_ItemStackGrid grid)
        {
            if (grid == null || te == null) return;
            var (_, any) = XUiM_LootContainer.StashItems(this, grid, xui.PlayerInventory, 0, null, XUiM_LootContainer.EItemMoveKind.All, false);
            if (any && npc != null) Manager.BroadcastPlayByLocalPlayer(npc.position, "UseActions/takeall1");
        }

        // ------------------------------------------------------------------ the window's place (t40) ----

        private XUiV_Window Win { get { return ViewComponent as XUiV_Window; } }

        /// <summary>Right after the group's layout: the saved place, else the place the layout gave the spacer.</summary>
        internal void PlaceNow() { Place(); }

        /// <summary>Puts the window where it belongs: the player's saved place (kept on screen), else over the spacer window.</summary>
        private void Place()
        {
            XUiV_Window w = Win;
            if (w == null || w.UiTransform == null) return;
            Vector2i p;
            if (ScUiLayout.TryGet(ScUiLayout.MemberInventory, out p)) p = Clamp(p);
            else if (!DefaultPlace(w, out p)) return;
            if (!p.Equals(w.Position)) w.Position = p;
        }

        /// <summary>Where the layout put the spacer (the looting window's place), in the panel's anchor space.</summary>
        private bool DefaultPlace(XUiV_Window w, out Vector2i p)
        {
            p = default(Vector2i);
            if (Space == null || Space.UiTransform == null || w.UiTransform.parent == null) return false;
            Vector3 local = w.UiTransform.parent.InverseTransformPoint(Space.UiTransform.position);
            p = Clamp(new Vector2i(Mathf.RoundToInt(local.x), Mathf.RoundToInt(local.y)));
            return true;
        }

        /// <summary>
        /// The window kept on screen: its top-left corner (its position, in its anchor's space) limited so the whole window
        /// shows - or its top, when the screen is shorter than the window. The screen's corners come from the UI camera, as
        /// vanilla's sign editor maps the mouse (ScreenToWorldPoint), so the anchor's place, the screen bounds option and
        /// the UI scale need no arithmetic here.
        /// </summary>
        private Vector2i Clamp(Vector2i p)
        {
            XUiV_Window w = Win;
            Camera cam = xui.playerUI != null ? xui.playerUI.camera : null;
            Transform parent = w != null && w.UiTransform != null ? w.UiTransform.parent : null;
            if (cam == null || parent == null) return p;
            Vector3 a = parent.InverseTransformPoint(cam.ScreenToWorldPoint(Vector3.zero));
            Vector3 b = parent.InverseTransformPoint(cam.ScreenToWorldPoint(new Vector3(cam.pixelWidth, cam.pixelHeight, 0f)));
            float minX = Mathf.Min(a.x, b.x), maxX = Mathf.Max(a.x, b.x), minY = Mathf.Min(a.y, b.y), maxY = Mathf.Max(a.y, b.y);
            float x = Mathf.Clamp(p.x, minX, Mathf.Max(minX, maxX - w.Size.x));
            float y = Mathf.Clamp(p.y, Mathf.Min(maxY, minY + w.Size.y), maxY);
            return new Vector2i(Mathf.RoundToInt(x), Mathf.RoundToInt(y));
        }

        private void OnBarDrag(XUiController _sender, EDragType _type, Vector2 _delta)
        {
            try
            {
                XUiV_Window w = Win;
                if (w == null) return;
                if (_type == EDragType.DragEnd)
                {
                    if (!dragging) return;
                    dragging = false;
                    ScUiLayout.Set(ScUiLayout.MemberInventory, w.Position);
                    return;
                }
                if (!dragging)
                {
                    dragging = true;
                    dragPos = new Vector2(w.Position.x, w.Position.y);
                }
                // NGUI hands over screen pixels: XUi units are pixels times the pixel ratio factor; screen y grows upward, as XUi's
                dragPos += _delta * xui.GetPixelRatioFactor();
                Vector2i p = Clamp(new Vector2i(Mathf.RoundToInt(dragPos.x), Mathf.RoundToInt(dragPos.y)));
                dragPos = new Vector2(Mathf.Clamp(dragPos.x, p.x - 0.5f, p.x + 0.5f), Mathf.Clamp(dragPos.y, p.y - 0.5f, p.y + 0.5f));
                if (!p.Equals(w.Position)) w.Position = p;
            }
            catch (Exception e)
            {
                ScLog.Once("memberinv-drag", e);
            }
        }

        /// <summary>Right-click on the title bar, or sc ui reset: back to the default place (and forgotten).</summary>
        internal void ResetPosition()
        {
            dragging = false;
            ScUiLayout.Clear(ScUiLayout.MemberInventory);
            Place();
        }

        // ------------------------------------------------------------------ bindings ----

        private static bool Set(ref string field, string value)
        {
            if (value == null) value = "";
            if (field == value) return false;
            field = value;
            return true;
        }

        public override bool GetBindingValueInternal(ref string _value, string _bindingName)
        {
            switch (_bindingName)
            {
                case "invname": _value = name; return true;
                case "invsub": _value = sub; return true;
                case "invpackcount": _value = packCount; return true;
                case "invbagcount": _value = bagCount; return true;
                case "invbagvisible": _value = bag != null && bag.Bound ? "true" : "false"; return true;
                case "invbagnote": _value = bag != null && bag.Bound ? "false" : "true"; return true;
                case "invrname": _value = rName; return true;
                case "invrinfo": _value = rInfo; return true;
                case "invmname": _value = mName; return true;
                case "invminfo": _value = mInfo; return true;
                // the pack's item_stack cells show up to the container's length (vanilla's item_stack template reads it)
                case "container_slots": _value = packSlots.ToString(CultureInfo.InvariantCulture); return true;
            }
            if (_bindingName.StartsWith("invtoolnote", StringComparison.Ordinal) && _bindingName.Length == 12)
            {
                int i = _bindingName[11] - '0';
                if (i >= 0 && i < toolNotes.Length) { _value = toolNotes[i]; return true; }
            }
            return base.GetBindingValueInternal(ref _value, _bindingName);
        }

        /// <summary>For sc ui: where the open panel sits and what it shows.</summary>
        internal string Describe()
        {
            XUiV_Window w = Win;
            return (npc != null ? ScCommunity.DisplayName(npc) + " (id " + npc.entityId + ")" : "-") + " at " + (w != null ? w.Position.ToString() : "?")
                   + ", pack " + packCount + (bag != null && bag.Bound ? ", bag " + bagCount : ", bag hidden (not the host)");
        }
    }

    /// <summary>
    /// Round n10: where the player put SC's movable windows, by key - for now only the member inventory panel's top-left corner
    /// (XUi units from its anchor, the screen's top-left). One small text file in SC's user data folder
    /// (%APPDATA%\7DaysToDie\SurvivorCommunity\ui_layout.txt, beside the streamer names), so it is the player's own and holds
    /// across worlds and saves; read once, written on every drag's end and reset. A missing or unreadable file is no place.
    /// </summary>
    public static class ScUiLayout
    {
        public const string MemberInventory = "memberinv";
        private const string FileName = "ui_layout.txt";

        private static Dictionary<string, Vector2i> places;

        private static string FilePath()
        {
            return Path.Combine(Path.Combine(GameIO.GetUserGameDataDir(), ScStreamerNames.FolderName), FileName);
        }

        private static void Load()
        {
            if (places != null) return;
            places = new Dictionary<string, Vector2i>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string path = FilePath();
                if (!File.Exists(path)) return;
                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string[] xy = line.Substring(eq + 1).Split(',');
                    int x, y;
                    if (xy.Length != 2 || !int.TryParse(xy[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out x)
                        || !int.TryParse(xy[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out y)) continue;
                    places[line.Substring(0, eq).Trim()] = new Vector2i(x, y);
                }
            }
            catch (Exception e)
            {
                ScLog.Once("ui-layout-read", "ui layout: could not read " + FileName + " (" + e.Message + ") - windows open in their default places");
            }
        }

        private static void Save()
        {
            try
            {
                string path = FilePath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var lines = new List<string> { "# Survivor Community: where you moved its windows (XUi units from the screen's top-left). Delete a line,",
                                               "# or type sc ui reset in the F1 console, to put a window back." };
                foreach (var kv in places)
                    lines.Add(kv.Key + "=" + kv.Value.x.ToString(CultureInfo.InvariantCulture) + "," + kv.Value.y.ToString(CultureInfo.InvariantCulture));
                File.WriteAllLines(path, lines.ToArray());
            }
            catch (Exception e)
            {
                ScLog.Once("ui-layout-write", "ui layout: could not write " + FileName + " (" + e.Message + ") - the window's place is kept for this session only");
            }
        }

        public static bool TryGet(string key, out Vector2i p)
        {
            Load();
            return places.TryGetValue(key, out p);
        }

        public static void Set(string key, Vector2i p)
        {
            Load();
            Vector2i old;
            if (places.TryGetValue(key, out old) && old.Equals(p)) return;
            places[key] = p;
            Save();
        }

        /// <summary>Forgets a window's place; true when it had one.</summary>
        public static bool Clear(string key)
        {
            Load();
            if (!places.Remove(key)) return false;
            Save();
            return true;
        }

        /// <summary>For sc ui: the file and its places.</summary>
        public static string Describe()
        {
            Load();
            var parts = new List<string>();
            foreach (var kv in places) parts.Add(kv.Key + "=" + kv.Value.x + "," + kv.Value.y);
            string path;
            try { path = FilePath(); } catch (Exception) { path = "?"; }
            return (parts.Count > 0 ? string.Join(" ", parts.ToArray()) : "no window moved (default places)") + " - " + path;
        }
    }
}
