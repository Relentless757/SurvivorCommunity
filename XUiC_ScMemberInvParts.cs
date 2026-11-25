using System;
using System.Globalization;
using Audio;
using UnityEngine.Scripting;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round n10 (3.4): the member inventory panel's BAG grid - the NPC's hidden bag (npc.bag, 45 slots on SCore's NPCs: where SC
    /// keeps her ammo and magazines, where vanilla's reload and Locked &amp; Loaded take them from), fully movable like any
    /// container. The cells are vanilla item_stacks at the loot-container location, so shift-click and the item's Take action
    /// send a stack to the player's inventory, and a stack dropped anywhere on the grid goes into the bag (as vanilla's
    /// backpack grid does).
    ///
    /// Written back slot by slot as each cell changes (Bag.SetSlot), never the whole bag at once: a background change to the
    /// bag while the panel is open - a reload drawing rounds, Locked &amp; Loaded filling a magazine, a resupply - is never
    /// overwritten by an older copy, and the grid shows it on the next frame (the bag's change event). SC's own move of rounds
    /// from the window into the bag (ScAmmo.MoveWindowAmmoToBag, LoadLoose) waits while the panel is open: the panel holds the
    /// container's user-accessing flag, as the looting screen does.
    ///
    /// The host's only: a multiplayer client has no copy of an NPC's bag (vanilla never sends it), so there the grid stays
    /// unbound and the panel shows a note instead (XUiC_ScMemberInv).
    /// </summary>
    [Preserve]
    public class XUiC_ScBagGrid : XUiC_ItemStackGrid
    {
        private Bag bag;
        private bool dirty, writing, warned;
        private int shownCount = -1;

        public bool Bound { get { return bag != null; } }

        /// <summary>The bag's slots the grid shows: all of them, unless the bag has more than the grid has cells.</summary>
        public int SlotCount { get { return bag != null && itemControllers != null ? Math.Min(bag.SlotCount, itemControllers.Length) : 0; } }

        public override XUiC_ItemStack.StackLocationTypes StackLocation { get { return XUiC_ItemStack.StackLocationTypes.LootContainer; } }

        internal void Bind(EntityAlive npc)
        {
            Unbind();
            if (npc == null || npc.bag == null || itemControllers == null) return;
            bag = npc.bag;
            bag.OnBackpackItemsChangedInternal += OnBagChanged;
            if (bag.SlotCount > itemControllers.Length && !warned)
            {
                warned = true;
                ScLog.Warn("member inventory: " + ScCommunity.DisplayName(npc) + "'s bag has " + bag.SlotCount + " slots and the panel's bag grid "
                           + itemControllers.Length + " - the last " + (bag.SlotCount - itemControllers.Length) + " are not shown");
            }
            Refresh();
        }

        internal void Unbind()
        {
            if (bag != null) bag.OnBackpackItemsChangedInternal -= OnBagChanged;
            bag = null;
            dirty = false;
            if (itemControllers != null) Refresh();
        }

        private void OnBagChanged()
        {
            if (!writing) dirty = true;
        }

        /// <summary>Every cell from the bag (an empty cell past its end, hidden by the item_stack template's container_slots).</summary>
        private void Refresh()
        {
            dirty = false;
            XUiC_ItemInfoWindow info = xui.GetChildByType<XUiC_ItemInfoWindow>();
            int n = SlotCount;
            // a new count shows or hides cells (their container_slots binding): every cell re-reads its bindings then, not
            // only the ones whose stack changed
            bool recount = n != shownCount;
            shownCount = n;
            for (int i = 0; i < itemControllers.Length; i++)
            {
                XUiC_ItemStack c = itemControllers[i];
                c.SlotChangedEvent -= handleSlotChangedDelegate;
                ItemStack want = i < n ? bag.ItemGrid[i] : ItemStack.Empty;
                if (!c.ItemStack.Equals(want)) c.ItemStack = want.Clone();
                c.SlotNumber = i;
                c.InfoWindow = info;
                c.StackLocation = StackLocation;
                if (recount) c.IsDirty = true;
                if (i < n) c.SlotChangedEvent += handleSlotChangedDelegate;
            }
            IsDirty = true;
            updateSearch();
        }

        /// <summary>A cell changed (a drag, a drop, shift-click, Take): that one slot of the bag, nothing else.</summary>
        public override void HandleSlotChangedEvent(int slotNumber, ItemStack stack)
        {
            if (bag == null || slotNumber < 0 || slotNumber >= SlotCount) return;
            writing = true;
            try { bag.SetSlot(slotNumber, stack != null ? stack.Clone() : ItemStack.Empty.Clone()); }
            catch (Exception e) { ScLog.Once("memberinv-bag-write", e); dirty = true; }
            finally { writing = false; }
        }

        public override void UpdateBackend(ItemStack[] stackList) { }

        public override ItemStack[] getUISlots()
        {
            int n = SlotCount;
            var a = new ItemStack[n];
            for (int i = 0; i < n; i++) a[i] = itemControllers[i].ItemStack.Clone();
            return a;
        }

        /// <summary>A stack dropped on the grid between the cells: onto her matching stacks first, then the first empty slot.</summary>
        public override void dropItemToGrid(ItemStack _dropStack)
        {
            base.dropItemToGrid(_dropStack);
            if (bag == null || _dropStack == null || _dropStack.IsEmpty()) return;
            bag.TryStackItem(0, _dropStack);
            if (_dropStack.count > 0 && bag.AddItem(_dropStack)) _dropStack.count = 0;
        }

        public override void Update(float _dt)
        {
            // the bag's own changes first, then the cells' clicks (base.Update): a stack picked up this frame is the bag's
            // current one, never the copy from before a reload or a magazine fill changed it
            if (dirty && bag != null)
            {
                try { Refresh(); }
                catch (Exception e) { ScLog.Once("memberinv-bag-refresh", e); }
            }
            base.Update(_dt);
        }

        public override bool GetBindingValueInternal(ref string _value, string _bindingName)
        {
            // the bag's item_stack cells show up to its length (vanilla's item_stack template reads container_slots)
            if (_bindingName == "container_slots") { _value = SlotCount.ToString(CultureInfo.InvariantCulture); return true; }
            return base.GetBindingValueInternal(ref _value, _bindingName);
        }
    }

    /// <summary>
    /// Round n10 (3.4): one of the member inventory panel's equipment slots (a weapon, an armor piece, a job's tool) - a vanilla
    /// item_stack that shows an item with its quality bar, its tooltip and, on a click, the INSPECT panel. As a required-item
    /// stack its INSPECT lists no actions (no Drop or Scrap acting on the slot behind the panel's back).
    ///
    /// Round n12 (Phase 2): the slot is usable, through its own input handling - never XUiC_ItemStack's, which would write the
    /// slot's ItemStack and call that a move. A hidden stack lock keeps every vanilla pick-up, swap, half-stack and shift-click
    /// path shut (as in Phase 1); vanilla's click with an empty cursor still selects the slot for INSPECT. On top of it, the
    /// vanilla gestures, each handed to the panel (XUiC_ScMemberInv -&gt; ScMemberEquip), which changes her gear and says what
    /// the cursor holds afterwards:
    ///  - a stack on the cursor released over the slot (a click on it, or a drag let go on it): Drop - the cursor gets back what
    ///    the slot held (a swap, like two vanilla slots), or empties;
    ///  - pressing on a filled slot with an empty cursor and moving past vanilla's snap distance: Take onto the cursor;
    ///  - shift-click on a filled slot with an empty cursor: Take into the player's backpack;
    ///  - the right mouse button (half stacks, drop one): refused - an equipment slot never splits a stack;
    ///  - a gamepad: Submit takes or drops, the half-stack button is refused, the right stick sends to the backpack, Inspect
    ///    inspects (the lock shut vanilla's gamepad inspect too).
    /// A refusal plays the denied sound and shows the reason as a tooltip; the item stays on the cursor. The slot names its
    /// kind and index (Kind, Index: set by XUiC_ScMemberInv from its grid - weaponSlots, armorSlots, toolSlots).
    /// </summary>
    [Preserve]
    public class XUiC_ScGearSlot : XUiC_RequiredItemStack
    {
        /// <summary>What the slot is: a weapon (Index 0 RANGED, 1 MELEE), an armor piece (0..3 Head..Feet), a job's tool (ScMemberGear.Job*).</summary>
        internal ScMemberEquip.GearKind Kind;
        internal int Index;
        /// <summary>The panel window it belongs to (XUiC_ScMemberInv.Init).</summary>
        internal XUiC_ScMemberInv Panel;

        private float nextDenied;
        private bool pressed, handledThisPress;
        private UnityEngine.Vector2 pressPos;

        public override void Init()
        {
            base.Init();
            StackLocation = StackLocationTypes.None;
            stackLockType = StackLockTypes.Hidden;
        }

        /// <summary>Shows this item (a copy), or nothing for an empty stack.</summary>
        internal void Show(ItemStack s)
        {
            if (s == null) s = ItemStack.Empty;
            if (InfoWindow == null) InfoWindow = xui.GetChildByType<XUiC_ItemInfoWindow>();
            if (!ItemStack.Equals(s)) ItemStack = s.Clone();
            stackLockType = StackLockTypes.Hidden;      // the ItemStack setter drops every lock when it is handed an empty stack
        }

        public override void OnClose()
        {
            base.OnClose();
            pressed = handledThisPress = false;
        }

        public override void Update(float _dt)
        {
            stackLockType = StackLockTypes.Hidden;
            base.Update(_dt);
            try
            {
                if (Panel != null && WindowGroup != null && WindowGroup.isShowing) HandleInput();
            }
            catch (Exception e)
            {
                ScLog.Once("memberinv-gear-input", e);
            }
        }

        /// <summary>The gestures in the class summary, after vanilla's own handling of this frame (base.Update).</summary>
        private void HandleInput()
        {
            if (IsLocked || IsDragAndDrop) return;
            CursorControllerAbs cc = xui.playerUI.CursorController;
            bool over = isOver && ViewComponent.UiTransformIsHovered && ViewComponent.EventOnPress;
            if (!over)
            {
                pressed = handledThisPress = false;
                return;
            }
            ItemStack cursor = xui.DragAndDropWindow.CurrentStack;
            PlayerActionsGUI gui = xui.playerUI.playerInput.GUIActions;
            if (gui.LastInputType == InControl.BindingSourceType.DeviceBindingSource)
            {
                bool submit = gui.Submit.WasReleased, half = gui.HalfStack.WasReleased;
                if (cursor.IsEmpty())
                {
                    if (!HasItem) return;                   // round mt1 (review fix G-m4): was ItemStack.IsEmpty()
                    if (submit) Panel.SlotTake(this, false);
                    else if (half) Denied(Localization.Get("scInvRefuseHalf"));
                    else if (gui.RightStick.WasReleased) Panel.SlotTake(this, true);
                    else if (gui.Inspect.WasPressed) HandleItemInspect();
                }
                else if (submit) Panel.SlotDrop(this, cursor);
                else if (half) Denied(Localization.Get("scInvRefuseHalf"));
                return;
            }
            bool down = cc.GetMouseButtonDown(UICamera.MouseButton.LeftButton);
            bool held = cc.GetMouseButton(UICamera.MouseButton.LeftButton);
            bool up = cc.GetMouseButtonUp(UICamera.MouseButton.LeftButton);
            if (cc.GetMouseButtonUp(UICamera.MouseButton.RightButton))
            {
                if (!cursor.IsEmpty() || !ItemStack.IsEmpty()) Denied(Localization.Get("scInvRefuseHalf"));
                return;
            }
            if (down)
            {
                pressed = true;
                handledThisPress = false;
                pressPos = cc.GetScreenPosition();
            }
            if (InputUtils.ShiftKeyPressed)
            {
                if (up)
                {
                    bool once = !handledThisPress;
                    pressed = handledThisPress = false;
                    if (once && cursor.IsEmpty() && HasItem) Panel.SlotTake(this, true);       // round mt1 (review fix G-m4): HasItem
                }
                return;
            }
            if (held && pressed && !handledThisPress && cursor.IsEmpty() && HasItem     // round mt1 (review fix G-m4): HasItem
                && (cc.GetScreenPosition() - pressPos).magnitude > PickupSnapDistance)
            {
                handledThisPress = true;                // a refused take is not asked again until the next press
                Panel.SlotTake(this, false);
                return;
            }
            if (up)
            {
                bool took = handledThisPress;
                pressed = handledThisPress = false;
                if (!took && !cursor.IsEmpty()) Panel.SlotDrop(this, cursor);   // a click on the slot, or a drag let go on it
            }
        }

        /// <summary>
        /// Round mt1 (review fix G-m4): something to take - the item shown, or (a multiplayer client) a weapon the server's view
        /// names whose stack was too large to send (XUiC_ScMemberInv.ViewNamesItem): the take is asked, the server acts on its own.
        /// </summary>
        private bool HasItem { get { return !ItemStack.IsEmpty() || (Panel != null && Panel.ViewNamesItem(this)); } }

        /// <summary>A refusal: the denied sound and the reason as a tooltip (at most every 0.4 s, so one gesture shows one).</summary>
        internal void Denied(string why)
        {
            if (string.IsNullOrEmpty(why)) return;
            float now = UnityEngine.Time.unscaledTime;
            if (now < nextDenied) return;
            nextDenied = now + 0.4f;
            Manager.PlayInsidePlayerHead("ui_denied");
            // the tooltip formats its text with its arguments: braces in an item name must not break it
            GameManager.ShowTooltip(xui.playerUI.entityPlayer, why.Replace("{", "{{").Replace("}", "}}"), string.Empty, "ui_denied");
        }

        public override bool GetBindingValueInternal(ref string _value, string _bindingName)
        {
            // a lone slot, always shown (vanilla's item_stack template hides a cell at or past container_slots)
            if (_bindingName == "container_slots") { _value = "99"; return true; }
            return base.GetBindingValueInternal(ref _value, _bindingName);
        }
    }

    /// <summary>
    /// Round n10 (3.4): the talk snapshot's card (XUiC_ScSnapshot: name, role, portrait, health, stamina, mood, traits, the five
    /// skill rows) bound to the member whose inventory the panel shows instead of to the dialog's respondent - the same
    /// controller and bindings, laid out narrower by Tools/generate_member_inventory_ui.py. It shows for whoever it is handed
    /// (the panel opens for members only); without a record or a profile (a client before the host rolled one) it stays empty.
    /// </summary>
    [Preserve]
    public class XUiC_ScMemberCard : XUiC_ScSnapshot
    {
        /// <summary>The member on show; XUiC_ScMemberInv sets it before the group opens and clears it on close.</summary>
        public EntityAlive Target;

        protected override EntityAlive Subject() { return Target; }

        protected override bool Covers(EntityAlive npc, Member member) { return true; }
    }
}
