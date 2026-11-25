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
    /// CurrentNPC cvar) re-reads its weapons (SCore's Harmony.Dialog.XUiC_LootWindowGroupOnClose; round mt1: never a member,
    /// see ReArm), and a multiplayer client
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
            return !ScCommunity.IsHost && npc.Buffs != null && npc.Buffs.GetCustomVar("sc_member") > 0f && !npc.Buffs.HasCustomVar(ScRadio.StandInCvar);   // round MP2 (E9): the value (0 once cleared)
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
                ShownNpcId = npc.entityId;       // round mt1 (part G, H1): Voice Control's hold asks every frame (HoldsNpc)
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
                ShownNpcId = -1;                 // round mt1 (part G, H1)
                return false;
            }
        }

        /// <summary>
        /// Round mt1 (part G, H1): the member the panel shows on this machine, -1 while closed - set with NpcId, cleared on close,
        /// so Voice Control's hold (Patches_VoiceControl.Patch_PanelHold asks for every NPC every frame) costs one compare.
        /// </summary>
        internal static int ShownNpcId { get; private set; } = -1;

        /// <summary>
        /// Round mt1 (part G, H1): does the panel show this NPC right now? The cheap test first (ShownNpcId), then the window
        /// itself (ShowsNpc), so a group that went away without its close never holds her.
        /// </summary>
        internal static bool HoldsNpc(int entityId)
        {
            return entityId >= 0 && entityId == ShownNpcId && ShowsNpc(entityId);
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
                ShownNpcId = -1;             // round mt1 (part G, H1): Voice Control's hold on her ends with the panel
            }
            if (id >= 0) ScLog.Verbose("member inventory: closed for id " + id);
            ScEquipRedirect.PanelClosed();   // round U1 (B4): Voice Control's equipment window reopen, armed only by its Show backpack
        }

        /// <summary>
        /// SCore's close hook for its talk NPC: the NPC re-reads its weapons from what it now carries (a client sends it).
        /// Round mt1 (part G, F3; bug_gear_equip_window.md 1.4): never for a member. That re-read re-equips SCore's remembered
        /// weapon BY NAME - on the host UpdateWeapon(), on a client NetPackageWeaponSwap and SendSyncData, which the server answers
        /// with ItemClass.GetItem(name) - so the gun she was handed came back at a random quality with no mods and no stats on
        /// every close (always within Voice Control's 20 s chat stow, which round mt1's hold keeps up for the whole panel). Every
        /// change the panel makes already armed her with the exact, imprinted copy (ScLoadout.Set / Arm, ScArms.PanelWeapon), and
        /// her window is not where a member's pair comes from; a client sends nothing - the server's state is current. Another
        /// NPC the player talked to last (CurrentNPC is sticky) keeps SCore's re-read, as before.
        /// </summary>
        private void ReArm()
        {
            EntityPlayerLocal p = xui.playerUI.entityPlayer;
            if (p == null || p.Buffs == null || !p.Buffs.HasCustomVar("CurrentNPC")) return;
            int cur = (int)p.Buffs.GetCustomVar("CurrentNPC");
            Entity e = p.world.GetEntity(cur);
            IEntityAliveSDX sdx = e as IEntityAliveSDX;
            if (sdx == null) return;
            if (cur == NpcId || IsMember(e as EntityAlive)) return;      // round mt1 (part G, F3): no by-name re-arm for a member
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
    /// her) beside her equipment (ScMemberGear: the pair, the armor, the best tool per job - display only in Phase 1, usable
    /// since round n12 on the host: XUiC_ScGearSlot, SlotDrop / SlotTake, ScMemberEquip; since round MP3 on a multiplayer
    /// client too: requests to the server, ScActionsGear), then her storage: the PACK (her
    /// inventory window, SCore's container, on vanilla's own
    /// XUiC_LootContainer so drag and drop, shift-click and take-all are the game's) and the BAG (npc.bag, XUiC_ScBagGrid;
    /// shown on the host only: a multiplayer client has no copy of an NPC's bag).
    ///
    /// Round mt1 (part G): on a multiplayer client the equipment column is the server's view of it (member.gearview, polled
    /// while open and carried by every gear reply: AskView, TakeView), never items of the client's own making; on every machine
    /// the panel holds her in place through Voice Control's interaction hold (Patches_VoiceControl.Patch_PanelHold) and closes
    /// when she walks out of a person's reach (WalkedOff, the tell scInvClosedAway).
    ///
    /// The window is anchored at the screen's top-left (not a stack panel), so the layout never moves it: by default it sits
    /// where windowScMemberInvSpace was laid out (the looting window's place, left of the backpack), and once dragged where
    /// the player left it - remembered in ScUiLayout (%APPDATA%\7DaysToDie\SurvivorCommunity\ui_layout.txt) across closing,
    /// reopening and worlds (ScWindowMover, shared with the talk card since round n11). Right-click on the title bar or
    /// `sc ui reset` puts it back.
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

        /// <summary>Round MP3: the member on show and her container (the host's in place: ScActionsGear's handlers for the host's own panel).</summary>
        internal EntityAlive Shown { get { return npc; } }
        internal TEFeatureStorage Container { get { return te; } }

        /// <summary>
        /// Round MP3, a client: its gear request on the way (one at a time), as a token; given up after GearWaitSeconds (a
        /// reply that comes later still settles its own items).
        /// </summary>
        private int gearRequest, gearSeq;
        private float gearWaitUntil;
        private const float GearWaitSeconds = 10f;
        private string armorKey;

        /// <summary>
        /// Round mt1 (part G, F2), a client: the server's view of her equipment column (member.gearview; null until its first
        /// answer for this opening - the weapon slots show names only until then, never a placeholder), asked every ViewSeconds
        /// while the panel is open, one request at a time (ViewWaitSeconds: given up on an answer that never comes), and taken
        /// from every gear reply that carries it. viewToken changes with every opening and close, so an answer of an earlier one
        /// is dropped; viewAgain asks once more after an answer already on its way when something changed meanwhile.
        /// </summary>
        private ScMemberGear.GearView view;
        private int viewToken;
        private bool viewAsking, viewAgain;
        private float viewAskedAt = -100f;
        private const float ViewSeconds = 1.5f, ViewWaitSeconds = 10f;
        /// <summary>Round mt1 (part G): the job tool slots' picks as last shown (CopyToolTake: a pick of the server's stores is not this machine's to take).</summary>
        private ScMemberGear.ToolPick[] shownTools;

        /// <summary>
        /// Round mt1 (part G, H3; bug_gear_equip_window.md 2.3): the panel closes when she is more than AwayMeters away
        /// horizontally, or AwayHeight vertically, for AwaySeconds - a person's reach, not vanilla's 32 m for a crate. The limits
        /// grow to where she stood at the open (plus a step) when that was farther (a voice order from across the yard), and
        /// shrink with her as she comes closer. awaySince: when she was first seen beyond them (-1: she is not).
        /// </summary>
        private const float AwayMeters = 6f, AwayHeight = 3f, AwaySeconds = 1f;
        private float nearH = float.MaxValue, nearV = float.MaxValue, awaySince = -1f;
        private ScWindowMover mover;     // the title bar drags the window (ScWindowMover.cs)
        private bool wasReleased, activeKeyDown;

        // bound values: every one set from the constructor on (bindings are read while the XML is parsed)
        private string name = "", sub = "", packCount = "", bagCount = "", rName = "", rInfo = "", mName = "", mInfo = "";
        /// <summary>Round U1: the ammo line under RANGED (ScAmmoChoice.Line) and whether it shows.</summary>
        private string ammoLine = "";
        private bool ammoVisible;
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
            weaponSlots = Slots("weaponSlots", ScMemberEquip.GearKind.Weapon);
            armorSlots = Slots("armorSlots", ScMemberEquip.GearKind.Armor);
            toolSlots = Slots("toolSlots", ScMemberEquip.GearKind.Tool);
            Hook("btnClose", (s, b) => { if (b == -1) xui.playerUI.windowManager.Close(windowGroup); });
            Hook("btnPackTakeAll", (s, b) => { if (b == -1) TakeAll(Pack); });
            Hook("btnBagTakeAll", (s, b) => { if (b == -1) TakeAll(bag); });
            Hook("btnAmmo", (s, b) => { if (b == -1) Ammo(1); });                // round U1: the gun's next ammo type
            XUiController ammoButton = GetChildById("btnAmmo");
            if (ammoButton != null) ammoButton.OnRightPress += (s, b) => Ammo(-1);   // and the one before
            mover = new ScWindowMover(this, ScUiLayout.MemberInventory, DefaultPlace);
            if (!mover.Attach("dragBar")) ScLog.Err("member inventory: no dragBar in windowScMemberInv - the window cannot be moved");
            if (Pack == null) ScLog.Err("member inventory: no LootContainer grid in windowScMemberInv");
            if (bag == null) ScLog.Err("member inventory: no ScBagGrid in windowScMemberInv");
            if (weaponSlots.Length < 2 || armorSlots.Length < 4 || toolSlots.Length < ScMemberGear.JobCount)
                ScLog.Err("member inventory: equipment slots missing (weapons " + weaponSlots.Length + "/2, armor " + armorSlots.Length
                          + "/4, tools " + toolSlots.Length + "/" + ScMemberGear.JobCount + ")");
        }

        /// <summary>A grid's slots in grid order (round n12: each told its kind, its index and this window).</summary>
        private XUiC_ScGearSlot[] Slots(string gridId, ScMemberEquip.GearKind kind)
        {
            XUiController g = GetChildById(gridId);
            XUiC_ScGearSlot[] slots = g != null ? g.GetChildrenByType<XUiC_ScGearSlot>() : new XUiC_ScGearSlot[0];
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i].Kind = kind;
                slots[i].Index = i;
                slots[i].Panel = this;
            }
            return slots;
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
            gearRequest = 0;                 // round MP3: a request of an earlier opening settles its own items when it answers
            ResetView();                     // round mt1 (part G, F2): a new opening waits for its own view
            nearH = nearV = float.MaxValue;  // round mt1 (part G, H3)
            awaySince = -1f;
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
            ScPanelHurt.Watch(member);       // round m1p: a hit on her closes the panel
            Refresh(true);
        }

        public override void OnOpen()
        {
            base.OnOpen();
            wasReleased = false;
            activeKeyDown = false;
            if (mover != null) mover.EndDrag();
            nextRefresh = 0f;
        }

        public override void OnClose()
        {
            base.OnClose();
            if (bag != null) bag.Unbind();
            if (card != null) card.Target = null;
            if (mover != null) mover.EndDrag();
            ScPanelHurt.Watch(null);         // round m1p
            ResetView();                     // round mt1 (part G, F2): a late answer finds no panel to fill
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
                if (mover != null && !mover.Dragging) mover.Place();
                // round m1p (the user's decision): a hit on her closes the panel at once, and says why - only a hit on her, never
                // the player's own fight (ScPanelHurt)
                if (npc != null && ScPanelHurt.TakeHit(npc))
                {
                    EntityAlive hurt = npc;                  // OnClose clears npc
                    xui.playerUI.windowManager.Close(windowGroup);
                    ScPanelHurt.Tell(xui.playerUI.entityPlayer, hurt);
                    return;
                }
                float now = Time.unscaledTime;
                // round mt1 (part G, H3; the user: "the equipment menu stays up when the NPC walks away"): she walked off - a fight,
                // an investigation or a flight, the only things Voice Control's hold (round mt1's H1) still lets her leave for -
                // so the panel goes once she is out of a person's reach for AwaySeconds, and says why
                if (npc != null && WalkedOff(now))
                {
                    EntityAlive gone = npc;                  // OnClose clears npc
                    xui.playerUI.windowManager.Close(windowGroup);
                    ScCommunity.TellKey(xui.playerUI.entityPlayer, "scInvClosedAway", "", ScCommunity.DisplayName(gone));
                    ScLog.Verbose("member inventory: closed - " + ScCommunity.DisplayName(gone) + " (id " + gone.entityId + ") walked off (round mt1)");
                    return;
                }
                if (now < nextRefresh) return;
                nextRefresh = now + RefreshSeconds;
                // ms/m1all: knocked down (round U2) - 0-XNPCCore catches the hit that would have killed her, so ScPanelHurt never
                // counts it as a hit; the panel closes with the same message (and cannot be opened on her until she is revived)
                if (npc != null && ScDowned.IsDown(npc))
                {
                    EntityAlive downed = npc;                // OnClose clears npc
                    xui.playerUI.windowManager.Close(windowGroup);
                    ScPanelHurt.Tell(xui.playerUI.entityPlayer, downed);
                    return;
                }
                // she died, was unloaded or walked off: the panel goes, as vanilla's loot window goes at a distance (round mt1: the
                // walk-off itself is WalkedOff's, above; 32 m stays as the last word)
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
            if (force) armorKey = null;              // round n12: a change from a slot shows at once, the armor too
            Member m = ScRegistry.GetMember(npc.entityId);
            changed |= Set(ref name, ScCommunity.DisplayName(npc));
            changed |= Set(ref sub, SubTitle(m));
            changed |= Set(ref packCount, CountText(te.ItemGrid.items, te.ItemGrid.Length));
            changed |= Set(ref bagCount, bag != null && bag.Bound ? CountText(npc.bag.ItemGrid.items, bag.SlotCount) : "");

            // round mt1 (part G, F2): the host (single player, a listen host) builds the column inline, as before; a client shows
            // the server's view (her exact items, its numbers in this machine's language) and, until its first answer, the
            // weapons' names alone - never an item of its own making
            bool host = ScCommunity.IsHost;
            if (!host) AskView(false);
            ScMemberGear.GearView v = host ? null : view;
            ScMemberGear.WeaponView r, w;
            if (host) { r = ScMemberGear.Weapon(npc, true); w = ScMemberGear.Weapon(npc, false); }
            else if (v != null) { r = v.Weapons[0]; w = v.Weapons[1]; }
            else { r = ScMemberGear.WeaponName(npc, true); w = ScMemberGear.WeaponName(npc, false); }
            changed |= Set(ref rName, r.Name) | Set(ref rInfo, r.Info) | Set(ref mName, w.Name) | Set(ref mInfo, w.Info);
            string line = "";
            bool show;
            if (host) show = ScAmmoChoice.Line(npc, out line);
            else
            {
                show = v != null && v.AmmoShow;
                if (show) line = ScAmmoChoice.FormatLine(v.AmmoKey, v.AmmoItem, v.AmmoCount);
            }
            changed |= Set(ref ammoLine, line) | show != ammoVisible;
            ammoVisible = show;
            if (weaponSlots.Length > 0) weaponSlots[0].Show(r.Stack);
            if (weaponSlots.Length > 1) weaponSlots[1].Show(w.Stack);

            // round mt1 (part G, F2): a client's armor is the server's (her handed pieces exactly); a view whose armor did not fit
            // its reply leaves the client's own copy of her look, as before; no view yet: bare slots
            bool ownArmor = host || (v != null && v.ArmorState == ScMemberGear.GearView.ArmorLeftOut);
            string key = ownArmor ? ScMemberGear.ArmorKey(npc) : v != null ? v.ArmorKey() : "-";
            if (key != armorKey)
            {
                armorKey = key;
                ItemStack[] a = ownArmor ? ScMemberGear.Armor(npc) : v != null && v.ArmorState == ScMemberGear.GearView.ArmorSent ? v.Armor : null;
                for (int i = 0; i < armorSlots.Length; i++) armorSlots[i].Show(a != null && i < a.Length ? a[i] : ItemStack.Empty);
            }

            // round mt1 (part G; bug_toolset_wood_job.md section 2): a client's job slots are its copy of her pack plus the
            // server's picks from her bag and toolbelt - the work kit shows, as on the host
            ScMemberGear.ToolPick[] tools = ScMemberGear.Tools(npc, te.ItemGrid.items, v != null ? v.Tools : null);
            shownTools = tools;
            for (int i = 0; i < toolSlots.Length && i < tools.Length; i++)
            {
                ScMemberGear.ToolPick t = tools[i];
                toolSlots[i].Show(t != null ? t.Stack : ItemStack.Empty);
                string note = t == null ? "" : t.Broken ? Localization.Get("scInvBroken") : t.Shared ? Localization.Get("scInvShared") : "";
                changed |= Set(ref toolNotes[i], note);
            }
            if (changed) RefreshBindings();
        }

        // ------------------------------------------------------------------ round mt1 (part G): a client's gear view ----

        /// <summary>Round mt1 (part G, F2): forgets the view (a new opening, the close); an answer still on its way is dropped (viewToken).</summary>
        private void ResetView()
        {
            view = null;
            viewToken++;
            viewAsking = viewAgain = false;
            viewAskedAt = -100f;
            shownTools = null;
        }

        /// <summary>
        /// Round mt1 (part G, F2), a client: asks the server for her equipment column (member.gearview) - every ViewSeconds, or
        /// now (a choice was made, a gear reply came without it); one request at a time (a "now" while one is on its way asks
        /// again after it). The host never asks (it builds the column inline).
        /// </summary>
        private void AskView(bool now)
        {
            if (ScCommunity.IsHost || npc == null) return;
            float t = Time.unscaledTime;
            if (viewAsking && t - viewAskedAt > ViewWaitSeconds) viewAsking = false;     // never answered: ask again
            if (viewAsking)
            {
                if (now) viewAgain = true;
                return;
            }
            if (!now && t - viewAskedAt < ViewSeconds) return;
            viewAsking = true;
            viewAgain = false;
            viewAskedAt = t;
            int token = viewToken, id = npc.entityId;
            ScActions.Request(ScActionsGear.OpGearView, reply =>
            {
                try
                {
                    if (token != viewToken) return;              // an earlier opening's answer
                    viewAsking = false;
                    if (npc == null || npc.entityId != id || windowGroup == null || !windowGroup.isShowing) return;
                    if (TakeView(reply)) Refresh(false);          // what changed shows (the bindings and armor key compare)
                    if (viewAgain) AskView(true);
                }
                catch (Exception e) { ScLog.Once("memberinv-view-reply", e); }
            }, id);
        }

        /// <summary>
        /// Round mt1 (part G, F2), a client: the view a reply carries (member.gearview, or a gear reply's), for the member on
        /// show; true when there was one. The next poll waits a full ViewSeconds from here.
        /// </summary>
        private bool TakeView(ScActionReply reply)
        {
            if (npc == null) return false;
            ScMemberGear.GearView v = ScMemberGear.ReadView(reply, npc.entityId);
            if (v == null) return false;
            view = v;
            if (!viewAsking) viewAskedAt = Time.unscaledTime;
            return true;
        }

        /// <summary>
        /// Round mt1 (part G, H3): has she been out of a person's reach of the player for AwaySeconds? Horizontal and vertical
        /// apart (a step up or a floor below is not a walk-off); the limits follow the nearest she has been since the open.
        /// </summary>
        private bool WalkedOff(float now)
        {
            EntityPlayerLocal p = xui.playerUI.entityPlayer;
            if (p == null || npc == null) return false;
            Vector3 d = npc.position - p.position;
            float h = Mathf.Sqrt(d.x * d.x + d.z * d.z), vert = Mathf.Abs(d.y);
            if (h < nearH) nearH = h;
            if (vert < nearV) nearV = vert;
            bool away = h > Mathf.Max(AwayMeters, nearH + 2f) || vert > Mathf.Max(AwayHeight, nearV + 1.5f);
            if (!away) { awaySince = -1f; return false; }
            if (awaySince < 0f) { awaySince = now; return false; }
            return now - awaySince >= AwaySeconds;
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

        // ------------------------------------------------------------------ the equipment slots (round n12) ----

        /// <summary>
        /// A stack released over an equipment slot (XUiC_ScGearSlot): ScMemberEquip makes the change, or says why not. Done,
        /// the cursor holds what the slot held (or nothing) in the same frame, and the slots show the change at once.
        /// Round MP3: a request (ScActionsGear) on every machine - inline on the host, on its own cursor as before; a client's
        /// weapon and armor slots ask the server (ClientDrop), its tool slots move the tool into its own copy of her pack.
        /// </summary>
        internal void SlotDrop(XUiC_ScGearSlot slot, ItemStack cursor)
        {
            if (slot == null || npc == null || te == null) return;
            if (!ScCommunity.IsHost)
            {
                if (slot.Kind == ScMemberEquip.GearKind.Tool) CopyToolDrop(slot, cursor);
                else ClientDrop(slot, cursor);
                return;
            }
            ItemStack before = cursor;
            ScActions.Request(ScActionsGear.OpDrop, new[] { npc.entityId, (int)slot.Kind, slot.Index }, null, reply =>
            {
                // the handler set the cursor already (the review's fix 2: the dropped stack is spent - she has it now)
                if (!reply.Ok) { slot.Denied(reply.Text); return; }
                try { slot.PlayPlaceSound(before); }
                catch (Exception e) { ScLog.Once("memberinv-place-sound", e); }
                Refresh(true);
            });
        }

        /// <summary>
        /// An equipment slot's item taken with an empty cursor: onto the cursor, or (toBackpack, shift-click) into the player's
        /// backpack - what the backpack had no room for lands on the cursor instead. Round MP3: a request, as SlotDrop.
        /// </summary>
        internal void SlotTake(XUiC_ScGearSlot slot, bool toBackpack)
        {
            if (slot == null || npc == null || te == null || !xui.DragAndDropWindow.CurrentStack.IsEmpty()) return;
            if (!ScCommunity.IsHost)
            {
                if (slot.Kind == ScMemberEquip.GearKind.Tool) CopyToolTake(slot, toBackpack);
                else ClientTake(slot, toBackpack);
                return;
            }
            ScActions.Request(ScActionsGear.OpTake, new[] { npc.entityId, (int)slot.Kind, slot.Index, toBackpack ? 1 : 0 }, null, reply =>
            {
                if (!reply.Ok) { slot.Denied(reply.Text); return; }
                if (reply.HintInt(ScActionsGear.HintPlaced, 0) > 0) slot.PlayPlaceSound();      // all of it went into the backpack
                Refresh(true);
            });
        }

        /// <summary>
        /// Round U1: the ammo line clicked - step 1 the next type she has rounds of, -1 the one before (ScAmmoChoice.Cycle); a
        /// refusal shows as a slot's does. Round MP3: a request (member.ammo) on every machine; a client then asks for the line
        /// again.
        /// </summary>
        private void Ammo(int step)
        {
            if (npc == null || te == null) return;
            try
            {
                EntityPlayerLocal player = xui.playerUI.entityPlayer;
                ScActions.Request(ScActionsGear.OpAmmo, reply =>
                {
                    try
                    {
                        string text = reply.Text;
                        if (!reply.Ok)
                        {
                            if (!string.IsNullOrEmpty(text))
                                GameManager.ShowTooltip(player, text.Replace("{", "{{").Replace("}", "}}"), string.Empty, "ui_denied");
                        }
                        else if (!string.IsNullOrEmpty(text))     // done, but she can load it only once a magazine is empty (L&L)
                            GameManager.ShowTooltip(player, text.Replace("{", "{{").Replace("}", "}}"), string.Empty, "");
                        if (!ScCommunity.IsHost) AskView(true);     // round mt1 (part G): the line comes with the gear view now
                        if (npc != null) Refresh(true);
                    }
                    catch (Exception e) { ScLog.Once("memberinv-ammo-reply", e); }
                }, npc.entityId, step < 0 ? -1 : 1);
            }
            catch (Exception e)
            {
                ScLog.Once("memberinv-ammo", e);
            }
        }

        // ------------------------------------------------------------------ round MP3: a client's gear slots ----

        /// <summary>Is the panel still open on the container this callback's request was made from?</summary>
        private bool StillShows(TEFeatureStorage copy)
        {
            return windowGroup != null && windowGroup.isShowing && copy != null && ReferenceEquals(te, copy);
        }

        private bool GearBusy { get { return gearRequest != 0 && Time.unscaledTime < gearWaitUntil; } }

        private int BeginGear()
        {
            gearRequest = ++gearSeq;
            if (gearRequest == 0) gearRequest = ++gearSeq;
            gearWaitUntil = Time.unscaledTime + GearWaitSeconds;
            return gearRequest;
        }

        private void EndGear(int token)
        {
            if (gearRequest == token) gearRequest = 0;
        }

        /// <summary>
        /// A client's weapon or armor drop: the take half first (ScItemTx) - the stack leaves the cursor now and travels in the
        /// request; a refusal (or a server that cannot answer) gives it back, a done gives the cursor what the slot held.
        /// </summary>
        private void ClientDrop(XUiC_ScGearSlot slot, ItemStack cursor)
        {
            if (cursor == null || cursor.IsEmpty() || GearBusy) return;
            if (cursor.count != 1) { slot.Denied(ScMemberEquip.Text("scInvRefuseStack")); return; }
            string enc = ScItemTx.Encode(cursor);
            if (string.IsNullOrEmpty(enc)) { slot.Denied(ScMemberEquip.Text("scInvRefuseTooBig", ScMemberEquip.Label(cursor.itemValue))); return; }
            EntityPlayerLocal player = xui.playerUI.entityPlayer;
            TEFeatureStorage copy = te;
            ItemStack sent = cursor.Clone();
            xui.DragAndDropWindow.SetCurrentStack(ItemStack.Empty.Clone());
            int token = BeginGear();
            ScActions.Request(ScActionsGear.OpDrop, new[] { npc.entityId, (int)slot.Kind, slot.Index }, new[] { enc }, reply =>
            {
                EndGear(token);
                try
                {
                    if (!reply.Ok)
                    {
                        ToPlayer(player, copy, sent, false);         // theirs again: the cursor, else the bag
                        Deny(slot, copy, player, reply.Text);
                        return;
                    }
                    Settle(player, copy, reply, false);
                    if (StillShows(copy))
                    {
                        try { slot.PlayPlaceSound(sent); }
                        catch (Exception e) { ScLog.Once("memberinv-place-sound", e); }
                        if (!TakeView(reply)) AskView(true);         // round mt1 (part G, F2): the server's column after the change
                        Refresh(true);
                    }
                }
                catch (Exception e) { ScLog.Once("memberinv-client-drop", e); }
            });
        }

        /// <summary>
        /// A client's weapon or armor take: asked of the server; what comes back goes on the cursor, or (toBackpack) into the
        /// backpack - room checked here first, as the host checks it; what still does not fit lands on the cursor.
        /// </summary>
        private void ClientTake(XUiC_ScGearSlot slot, bool toBackpack)
        {
            if (GearBusy || slot.ItemStack == null || slot.ItemStack.IsEmpty()) return;
            EntityPlayerLocal player = xui.playerUI.entityPlayer;
            if (toBackpack && !ScMemberEquip.BackpackRoom(player, slot.ItemStack)) { slot.Denied(ScMemberEquip.Text("scInvRefuseBackpackFull")); return; }
            TEFeatureStorage copy = te;
            int token = BeginGear();
            ScActions.Request(ScActionsGear.OpTake, new[] { npc.entityId, (int)slot.Kind, slot.Index, toBackpack ? 1 : 0 }, null, reply =>
            {
                EndGear(token);
                try
                {
                    if (!reply.Ok) { Deny(slot, copy, player, reply.Text); return; }
                    bool onCursor = Settle(player, copy, reply, toBackpack);
                    if (StillShows(copy))
                    {
                        if (toBackpack && !onCursor)
                        {
                            try { slot.PlayPlaceSound(); }
                            catch (Exception e) { ScLog.Once("memberinv-place-sound", e); }
                        }
                        if (!TakeView(reply)) AskView(true);         // round mt1 (part G, F2): the server's column after the change
                        Refresh(true);
                    }
                }
                catch (Exception e) { ScLog.Once("memberinv-client-take", e); }
            });
        }

        /// <summary>
        /// A done answer's items onto this machine: her Wardrobe pick's real piece out of this machine's copy of her pack (pt -
        /// only while that copy is still the session's: once the panel closed it went back with the piece in it, and the
        /// piece stays hers), else the slot's old item (cur), on the cursor or (toBackpack) into the backpack; other items
        /// (give) into the bag. True when something landed on the cursor.
        /// </summary>
        private bool Settle(EntityPlayerLocal player, TEFeatureStorage copy, ScActionReply reply, bool toBackpack)
        {
            List<ItemStack> cur = ScItemTx.HintToStacks(reply.HintStr(ScActionsGear.HintCursor));
            List<ItemStack> give = ScItemTx.HintToStacks(reply.HintStr(ScActionsGear.HintGive));
            List<ItemStack> pt = ScItemTx.HintToStacks(reply.HintStr(ScActionsGear.HintPackTake));
            ItemStack main = cur.Count > 0 ? cur[0] : ItemStack.Empty.Clone();
            for (int i = 1; i < cur.Count; i++) give.Add(cur[i]);
            if (pt.Count > 0 && npc != null && StillShows(copy) && ScMemberEquip.IsSessionCopy(npc.entityId, copy))
            {
                ItemStack got = ScMemberEquip.TakeFromCopy(copy, pt[0]);
                if (!got.IsEmpty())
                {
                    if (!main.IsEmpty()) give.Add(main);
                    main = got;
                }
            }
            bool onCursor = ToPlayer(player, copy, main, toBackpack);
            if (give.Count > 0) ScItemTx.GiveToLocal(player, give);
            return onCursor;
        }

        /// <summary>
        /// One stack to this player: (toBackpack) the backpack first; the rest on the cursor while the panel still shows this
        /// copy and the cursor is empty, else the bag and toolbelt, else at their feet (ScItemTx.GiveToLocal). True: on the cursor.
        /// </summary>
        private bool ToPlayer(EntityPlayerLocal player, TEFeatureStorage copy, ItemStack st, bool toBackpack)
        {
            if (st == null || st.IsEmpty()) return false;
            if (toBackpack) ScMemberEquip.ToBackpack(player, ref st);
            if (st == null || st.IsEmpty()) return false;
            if (StillShows(copy) && xui.DragAndDropWindow.CurrentStack.IsEmpty())
            {
                xui.DragAndDropWindow.SetCurrentStack(st);
                xui.DragAndDropWindow.PickUpType = XUiC_ItemStack.StackLocationTypes.LootContainer;
                return true;
            }
            ScItemTx.GiveToLocal(player, new List<ItemStack> { st });
            return false;
        }

        /// <summary>A refusal: as a slot shows one while the panel is open, else a tooltip.</summary>
        private void Deny(XUiC_ScGearSlot slot, TEFeatureStorage copy, EntityPlayerLocal player, string why)
        {
            if (string.IsNullOrEmpty(why)) return;
            if (StillShows(copy)) slot.Denied(why);
            else ScCommunity.Tell(player, why, "ui_denied");
        }

        /// <summary>A client's tool drop: into its own copy of her pack, as a drag into the pack grid (ScMemberEquip.CopyDropTool).</summary>
        private void CopyToolDrop(XUiC_ScGearSlot slot, ItemStack cursor)
        {
            ItemStack back;
            string why = ScMemberEquip.CopyDropTool(xui.playerUI.entityPlayer, npc, te, slot.Index, cursor, out back);
            if (why != null) { slot.Denied(why); return; }
            xui.DragAndDropWindow.SetCurrentStack(back ?? ItemStack.Empty.Clone());
            xui.DragAndDropWindow.PickUpType = XUiC_ItemStack.StackLocationTypes.LootContainer;
            try { slot.PlayPlaceSound(cursor); }
            catch (Exception e) { ScLog.Once("memberinv-place-sound", e); }
            Refresh(true);
        }

        /// <summary>
        /// A client's tool take: out of its own copy of her pack (ScMemberEquip.CopyTakeTool). Round mt1 (part G): a slot showing
        /// one of the server's picks (her toolbelt's work kit or pair weapon, or her bag: ScMemberGear.Tools) is refused here as
        /// the host refuses the kit - CopyTakeTool would otherwise take the best tool of the client's copy of her pack, which is
        /// not the one shown.
        /// </summary>
        private void CopyToolTake(XUiC_ScGearSlot slot, bool toBackpack)
        {
            ScMemberGear.ToolPick shown = shownTools != null && slot.Index >= 0 && slot.Index < shownTools.Length ? shownTools[slot.Index] : null;
            if (shown != null && shown.Store != ScMemberGear.StoreWindow)
            {
                string who = ScCommunity.DisplayName(npc);
                if (shown.Store == ScMemberGear.StoreBag) { slot.Denied(ScMemberEquip.Text("scInvRefuseBagClient", who)); return; }
                string item = ScHanded.NameOf(shown.Stack != null ? shown.Stack.itemValue : null);
                Loadout l = ScLoadout.Current(npc);
                bool pair = l != null && item != null && (string.Equals(l.Melee, item, StringComparison.OrdinalIgnoreCase) || string.Equals(l.Ranged, item, StringComparison.OrdinalIgnoreCase));
                slot.Denied(ScMemberEquip.Text(pair ? "scInvRefuseKitPair" : "scInvRefuseKit", who));
                return;
            }
            ItemStack taken;
            string why = ScMemberEquip.CopyTakeTool(xui.playerUI.entityPlayer, npc, te, slot.Index, toBackpack, out taken);
            if (why != null) { slot.Denied(why); return; }
            if (taken != null && !taken.IsEmpty())
            {
                xui.DragAndDropWindow.SetCurrentStack(taken);
                xui.DragAndDropWindow.PickUpType = XUiC_ItemStack.StackLocationTypes.LootContainer;
            }
            else if (toBackpack) slot.PlayPlaceSound();
            Refresh(true);
        }

        /// <summary>Everything in a grid into the player's inventory: vanilla's take-all (XUiC_ContainerStandardControls.MoveAll).</summary>
        private void TakeAll(XUiC_ItemStackGrid grid)
        {
            if (grid == null || te == null) return;
            var (_, any) = XUiM_LootContainer.StashItems(this, grid, xui.PlayerInventory, 0, null, XUiM_LootContainer.EItemMoveKind.All, false);
            if (any && npc != null) Manager.BroadcastPlayByLocalPlayer(npc.position, "UseActions/takeall1");
        }

        // ------------------------------------------------------------------ the window's place ----

        private XUiV_Window Win { get { return ViewComponent as XUiV_Window; } }

        /// <summary>Right after the group's layout: the saved place, else the place the layout gave the spacer.</summary>
        internal void PlaceNow() { if (mover != null) mover.Place(); }

        /// <summary>Where the layout put the spacer (the looting window's place), in the panel's anchor space.</summary>
        private bool DefaultPlace(XUiV_Window w, out Vector2i p)
        {
            p = default(Vector2i);
            if (Space == null || Space.UiTransform == null || w.UiTransform.parent == null) return false;
            Vector3 local = w.UiTransform.parent.InverseTransformPoint(Space.UiTransform.position);
            p = new Vector2i(Mathf.RoundToInt(local.x), Mathf.RoundToInt(local.y));
            return true;
        }

        /// <summary>Right-click on the title bar, or sc ui reset: back to the default place (and forgotten).</summary>
        internal void ResetPosition()
        {
            if (mover != null) mover.Reset();
            else ScUiLayout.Clear(ScUiLayout.MemberInventory);
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
                case "invammo": _value = ammoLine; return true;
                case "invammovisible": _value = ammoVisible ? "true" : "false"; return true;
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
}
