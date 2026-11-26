using System;
using NPCSdcsCharacters.Pure;      // WardrobeRules, WardrobeCheck (SdcsArmorSets is in the global namespace)

namespace SurvivorCommunity
{
    /// <summary>
    /// Round n12 (3.4): Phase 2 of the member inventory panel - its equipment slots (XUiC_ScGearSlot) are usable. Every
    /// action here is one synchronous step on the host: checked in full first (a refusal changes nothing and names the
    /// reason), then committed, and what comes back is handed to the caller for the player's cursor in the same frame - so
    /// closing the panel mid-drag, her death or her unload can never catch a half-done change (the cursor's item goes back to
    /// the player's inventory with vanilla's drag window, as for any screen).
    ///
    /// Host and single player only (the user's decision for this round): a multiplayer client is refused with a message; its
    /// pack works as before. A member only (the panel opens for members only; checked again here), alive and still in the
    /// world, and nobody else holding her inventory (a remote player's copy, 0-XNPCCore's stock trade: Gate).
    ///
    /// RANGED / MELEE (ScArms.PanelWeapon): a gun or bow on RANGED, a melee weapon on MELEE becomes that half of her pair, by
    /// the existing intake rule (ScLoadout.TakesHanded / WieldableFamily / IsRangedToken; ScLoadout.WieldRefusal names why
    /// not), and is kept as the exact item (ScHanded). The replaced weapon comes to the cursor. Taking RANGED off with an empty
    /// cursor (or shift-click, into the backpack) makes her melee-only; MELEE is swap only.
    ///
    /// ARMOR (Armor): a piece that fits the slot and that she can wear and render - 0-XNPCCore's own Wardrobe rule
    /// (SdcsArmorSets.PieceByName + WardrobeRules.Check: an SDCS model, the slot, the body's sex) and SC's outfit table's sex -
    /// is worn with its real quality and kept exactly; her previous piece comes back as a real item: the handed one exactly,
    /// the Wardrobe's pick as the real piece out of her pack (she only wore a copy of it), else a new item of the look's name
    /// and quality. Taking a piece off leaves the slot bare. Either way the slot's Wardrobe cvar and ScWardrobe's snapshot are
    /// cleared (ScWardrobe.PanelChanged), so the panel's choice stands.
    ///
    /// JOB TOOLS (DropTool, TakeTool): a tool for that job (ScMemberGear.Rank, the panel's own classification) goes into her
    /// pack (stack, else the first free slot; a full pack refuses); taking the tool shown takes it out of her pack or bag. The
    /// work kit (toolbelt copies rebuilt from her item list) and a pair weapon shown as a tool stay with her.
    ///
    /// Partial stacks are never split on an equipment slot: a weapon or a piece must be a single item on the cursor (a tool
    /// stack goes into the pack whole), and the right mouse button is refused (XUiC_ScGearSlot).
    ///
    /// Round MP3 (part G): a multiplayer client's panel works too. Its pack is the copy SCore sent it (SCore's remote session,
    /// merged back on close by what the player changed: HarvestManager.EndRemoteSession), so the pack is THAT player's for the
    /// session and the server never takes anything out of its own pack while the copy is out - the client's grid would still
    /// show it and the item could leave twice. Hence:
    ///  - RANGED, MELEE and the armor slots are requests (ScActionsGear): the server runs Drop / Take here for the sender
    ///    (Remote: the stack from the request, what comes back in the reply), against her pair, her look and her bag. What it
    ///    puts in her pack (a stale stored piece) is ADDED to its own pack - SCore's merge keeps a server addition - and shows
    ///    in the client's grid at its next open. The Wardrobe pick's real piece in her pack is not taken out on the server: the
    ///    reply names it (RemoteGear.PackTake) and the client takes it out of its own copy (TakeFromCopy), as a drag out of the
    ///    grid would; the merge then takes it out of the server's pack.
    ///  - The job tool slots are a view over her pack: on a client they move the tool between the cursor and its own copy
    ///    (CopyDropTool, CopyTakeTool), exactly as a drag in the pack grid would, with no request.
    ///  - Gate lets the requesting player's own open copy through; anyone else's still refuses, and a remote player needs a
    ///    session of their own (their panel open).
    /// The host's own panel and single player go through ScActionsGear too, with the host's own cursor and container in place
    /// (no encoding), so they behave exactly as before.
    /// </summary>
    internal static class ScMemberEquip
    {
        internal enum GearKind { None, Weapon, Armor, Tool }

        private static readonly string[] JobKeys = { "scInvJobChop", "scInvJobMine", "scInvJobDig", "scInvJobButcher", "scInvJobRepair" };
        private static readonly string[] SlotKeys = { "scInvHead", "scInvChest", "scInvHands", "scInvFeet" };

        // ------------------------------------------------------------------ round MP3: a remote player's request ----

        /// <summary>
        /// Round MP3: the server handling one remote player's gear request (ScActionsGear sets it around one Drop / Take, main
        /// thread). What only the player's own machine can do is collected here for the reply instead: items for their bag
        /// (Give) and her Wardrobe pick's real piece, which their own copy of her pack gives up (PackTake).
        /// </summary>
        internal sealed class RemoteGear
        {
            public EntityPlayer Sender;
            /// <summary>Items for the player that are not the cursor's (a stale weapon of the same type): into their bag.</summary>
            public readonly System.Collections.Generic.List<ItemStack> Give = new System.Collections.Generic.List<ItemStack>();
            /// <summary>One item the client takes out of its own copy of her pack onto the cursor (null: none).</summary>
            public ItemStack PackTake;
        }

        private static RemoteGear remote;

        internal static RemoteGear BeginRemote(EntityPlayer sender)
        {
            remote = new RemoteGear { Sender = sender };
            return remote;
        }

        internal static void EndRemote() { remote = null; }

        // ------------------------------------------------------------------ entry points ----

        /// <summary>
        /// The cursor's stack dropped on a slot. Null when done, with back the stack the cursor holds now (what the slot held,
        /// or empty); else the refusal for the player ("" for nothing to do), nothing changed and the cursor kept.
        /// </summary>
        internal static string Drop(EntityPlayer player, EntityAlive npc, TEFeatureStorage pack, GearKind kind, int index, ItemStack cursor, out ItemStack back)
        {
            back = ItemStack.Empty.Clone();
            if (cursor == null || cursor.IsEmpty() || cursor.itemValue == null || cursor.itemValue.ItemClass == null) return "";
            if (remote != null && kind == GearKind.Tool) return "";      // round MP3: a client's tools move in its own copy of her pack
            Member m;
            string why = Gate(player, npc, out m);
            if (why != null) return why;
            if (kind != GearKind.Tool && cursor.count != 1) return Text("scInvRefuseStack");
            ItemValue iv = cursor.itemValue.Clone();
            switch (kind)
            {
                case GearKind.Weapon:
                {
                    bool ranged = index == 0;
                    if (!ScXnpc.IsBody(npc)) return Text("scInvRefuseNoPair", m.Name);
                    why = WeaponRefusal(iv.ItemClass, ranged);
                    if (why != null) return why;
                    ItemValue b;
                    // round MP3: ScArms.PanelWeapon takes EntityPlayerLocal - null for a remote player on purpose: its one use, a stale
                    // weapon for the player, comes back through GiveToPlayer into the reply (RemoteGear)
                    why = ScArms.PanelWeapon(player as EntityPlayerLocal, npc, m, pack, ranged, iv, out b);
                    if (why != null) return why;
                    back = b != null ? new ItemStack(b, 1) : ItemStack.Empty.Clone();
                    return null;
                }
                case GearKind.Armor:
                    return Armor(player, npc, m, pack, index, iv, false, out back);
                case GearKind.Tool:
                    return DropTool(npc, m.Name, pack, index, cursor, out back);
            }
            return "";
        }

        /// <summary>
        /// The slot's item taken with an empty cursor (toBackpack: shift-click, into the player's backpack). Null when done,
        /// with taken the stack for the cursor (empty when it all went into the backpack); else the refusal ("" for nothing).
        /// </summary>
        internal static string Take(EntityPlayer player, EntityAlive npc, TEFeatureStorage pack, GearKind kind, int index, bool toBackpack, out ItemStack taken)
        {
            taken = ItemStack.Empty.Clone();
            if (remote != null && kind == GearKind.Tool) return "";      // round MP3: a client's tools move in its own copy of her pack
            Member m;
            string why = Gate(player, npc, out m);
            if (why != null) return why;
            switch (kind)
            {
                case GearKind.Weapon:
                {
                    if (index != 0) return Text("scInvMeleeKeep");
                    Loadout cur = ScXnpc.IsBody(npc) ? ScLoadout.Current(npc) : null;
                    if (cur == null || !cur.HasRanged) return "";
                    ItemClass gun = ItemClass.GetItemClass(cur.Ranged, true);
                    if (toBackpack && gun != null && !BackpackRoom(player, new ItemStack(new ItemValue { type = gun.Id }, 1))) return Text("scInvRefuseBackpackFull");
                    ItemValue b;
                    // round MP3: ScArms.PanelWeapon takes EntityPlayerLocal - null for a remote player on purpose (see Drop)
                    why = ScArms.PanelWeapon(player as EntityPlayerLocal, npc, m, pack, true, null, out b);
                    if (why != null) return why;
                    taken = b != null ? new ItemStack(b, 1) : ItemStack.Empty.Clone();
                    break;
                }
                case GearKind.Armor:
                    why = Armor(player, npc, m, pack, index, null, toBackpack, out taken);
                    if (why != null) return why;
                    break;
                case GearKind.Tool:
                    why = TakeTool(player, npc, m.Name, pack, index, toBackpack, out taken);
                    if (why != null) return why;
                    break;
                default:
                    return "";
            }
            if (toBackpack) ToBackpack(player, ref taken);
            return null;
        }

        // ------------------------------------------------------------------ checks ----

        /// <summary>
        /// Everything every action needs: the server, a member alive and in the world, nobody else in her inventory. Round MP3:
        /// a remote player (ScActionsGear) acts through the copy their own panel shows, so their own open copy is no obstacle
        /// and they need one (SCore's session of theirs).
        /// </summary>
        internal static string Gate(EntityPlayer player, EntityAlive npc, out Member m)
        {
            m = null;
            if (!ScCommunity.IsHost) return Text("scInvSlotHostOnly");
            string name = npc != null ? ScCommunity.DisplayName(npc) : "?";
            World w = ScCommunity.World;
            if (player == null || npc == null || w == null || npc.IsDead() || npc.IsMarkedForUnload() || !ReferenceEquals(w.GetEntity(npc.entityId), npc))
                return Text("scInvRefuseGone", name);
            m = ScRegistry.Loaded ? ScRegistry.GetMember(npc.entityId) : null;
            if (m == null) return Text("scInvRefuseNotMember", name);
            if (m.CommunityId != ScPlayers.CommunityOf(player)) { m = null; return Text("scInvRefuseNotMember", name); }   // round MP6 (part G): another community's member is not yours to equip
            if (ScDowned.IsDown(npc)) return Text("scDownedBusy");      // the combined review, finding 2: no gear changes on a downed body
            // the host's own panel holds the container's user-accessing flag (and makes SCore refuse remote opens), so it is no
            // obstacle; a remote player's copy written back on its close, or 0-XNPCCore's trade, would undo or double a change.
            // Round MP3: the requesting player's own copy is the one their request acts through - only someone else's refuses
            // (the host's own player is never marked: for the host this is the old IsRemoteOpen test)
            if (ScAmmo.IsRemoteOpenByOther(npc.entityId, player.entityId) || ScAmmo.XnpcTradeOpen(npc.entityId)) return Text("scInvRefuseRemote", name);
            if (!(player is EntityPlayerLocal) && !OwnsCopy(npc.entityId, player.entityId)) return Text("scInvRefuseReopen", name);
            return null;
        }

        /// <summary>
        /// Round MP3: does this remote player hold SCore's session on the NPC's inventory - the copy their panel shows, sent by
        /// the server and merged back when they close it (HarvestManager.BeginRemoteSession / EndRemoteSession)? A request
        /// without one comes from no open panel (a stale or forged one).
        /// </summary>
        internal static bool OwnsCopy(int npc, int player)
        {
            return HarvestManager.HasRemoteSession(npc) && !HarvestManager.IsOpenByOther(npc, player);
        }

        /// <summary>Why this item cannot go on RANGED (ranged) or MELEE, or null.</summary>
        private static string WeaponRefusal(ItemClass ic, bool ranged)
        {
            string key = ScLoadout.WieldRefusal(ic);
            if (key != null) return Text(key, Label(ic));
            if (ScLoadout.IsRangedToken(ic.GetItemName()) != ranged) return Text(ranged ? "scInvRefuseWantRanged" : "scInvRefuseWantMelee", Label(ic));
            return null;
        }

        /// <summary>
        /// Why she cannot wear this piece on this equipment slot, or null: not armor of the four slots, the wrong slot, and
        /// 0-XNPCCore's own Wardrobe rule (an SDCS model for it, the slot, the body's sex; its Dev pieces have no model entry),
        /// then SC's outfit table's sex (ScLooks leaves a piece of the other sex off).
        /// </summary>
        private static string ArmorRefusal(Look look, int slot, ItemClass ic)
        {
            ItemClassArmor armor = ic as ItemClassArmor;
            int es = armor != null ? (int)armor.EquipSlot : -1;
            if (es < 0 || es > 3) return Text("scInvRefuseNotArmor", Label(ic));
            if (es != slot) return Text("scInvRefuseWrongSlot", Label(ic), Localization.Get(SlotKeys[es]));
            if (ic.CreativeMode == EnumCreativeMode.Dev) return Text("scInvRefuseDev", Label(ic));
            string item = ic.GetItemName();
            WardrobeRules.Piece piece;
            bool found = SdcsArmorSets.PieceByName(item, out piece);
            switch (WardrobeRules.Check(found ? piece.Slot : es, slot, piece.Sex, look.Male, found, true))
            {
                case WardrobeCheck.Ok: break;
                case WardrobeCheck.WrongSlot: return Text("scInvRefuseWrongSlot", Label(ic), Localization.Get(SlotKeys[es]));
                case WardrobeCheck.WrongSex: return Text(look.Male ? "scInvRefuseForWomen" : "scInvRefuseForMen", Label(ic));
                default: return Text("scInvRefuseNoModel", Label(ic));
            }
            if (!ScLooks.ArmorSexFits(item, look.Male)) return Text(look.Male ? "scInvRefuseForWomen" : "scInvRefuseForMen", Label(ic));
            return null;
        }

        // ------------------------------------------------------------------ armor ----

        /// <summary>
        /// A piece dropped on (handed) or taken off (handed null) equipment slot `slot`. back: her previous piece for the
        /// cursor - the handed one exactly, the Wardrobe pick's real piece out of her pack or bag, else a new item of the
        /// look's name and quality; empty for a bare slot. A stale stored piece of the slot goes into her pack. toBackpack:
        /// the room for back is checked first (Take puts it there).
        /// </summary>
        private static string Armor(EntityPlayer player, EntityAlive npc, Member m, TEFeatureStorage pack, int slot, ItemValue handed, bool toBackpack, out ItemStack back)
        {
            back = ItemStack.Empty.Clone();
            string name = m.Name;
            Look look = ScXnpc.IsBody(npc) ? ScLooks.Get(npc) : null;
            if (look == null || slot < 0 || slot > 3) return Text("scInvRefuseNoLook", name);
            if (handed != null)
            {
                string why = ArmorRefusal(look, slot, handed.ItemClass);
                if (why != null) return why;
            }
            string worn = look.Gear[slot] ?? "";
            if (handed == null && worn.Length == 0) return "";
            // a Wardrobe copy: its cvar standing, or (the review's fix 1) her record noting it after the cvar or the snapshot went
            string wardrobe = ScWardrobe.WardrobePick(npc, m, look, slot);
            ItemValue valid = ScHanded.Armor(m, look, slot);
            // the stored piece is not what comes back when the Wardrobe dresses her (her real pick does), even of the same name
            ItemValue stale = valid == null || wardrobe != null ? ScHanded.Get(m, ScHanded.ArmorSlot(slot)) : null;
            int fromStore = -1, fromSlot = -1;
            ItemValue backIv;
            if (wardrobe != null) backIv = FindReal(npc, pack, wardrobe, look.Quality[slot], out fromStore, out fromSlot);   // null: its real piece left her pack
            else if (valid != null) backIv = valid.Clone();
            else backIv = worn.Length > 0 ? ScLooks.MakeItem(worn, look.Quality[slot]) : null;
            // round MP3: for a remote player the real piece in her PACK leaves the copy their panel shows, on their machine (the
            // reply's PackTake; SCore's merge then takes it out of this pack) - never out of this pack now, which their grid
            // would still show. Her bag is the server's alone: taken here as before
            bool copyTake = remote != null && fromStore == ScMemberGear.StoreWindow;
            // the Wardrobe's real piece leaving her pack frees its slot for the stale one when it was the last of its stack (fix 9)
            // (round MP3: not before the merge, for a remote player's copy take)
            bool freesSlot = !copyTake && fromStore == ScMemberGear.StoreWindow && pack.ItemGrid[fromSlot].count <= 1;
            if (stale != null && !freesSlot && !PackRoom(pack, new ItemStack(stale, 1))) return Text("scInvRefuseStaleRoom", Label(stale));
            if (toBackpack && backIv != null && !BackpackRoom(player, new ItemStack(backIv, 1))) return Text("scInvRefuseBackpackFull");

            // commit - the Wardrobe's real piece first: nothing else changes unless it really left her pack or bag (fix 2)
            if (fromStore >= 0 && !copyTake && !TakeOne(npc, pack, fromStore, fromSlot)) return Text("scInvRefuseFailed", Label(backIv));
            ItemValue keep = handed != null ? ScHanded.Clean(handed, false) : null;
            ScHanded.Put(m, ScHanded.ArmorSlot(slot), keep);
            Look next = look.Clone();
            next.Gear[slot] = keep != null ? ScHanded.NameOf(keep) ?? "" : "";
            next.Quality[slot] = keep != null && keep.ItemClass != null && keep.ItemClass.HasQuality ? ScHanded.LookQuality(keep) : 1;
            ScWardrobe.PanelChanged(npc, slot);
            m.Look = next;
            if (stale != null) Step("armor-stale", () => PutInPack(npc, pack, new ItemStack(stale.Clone(), 1), "a piece she no longer wore"));
            Step("armor-apply", () => ScLooks.Apply(npc, next));
            Step("armor-save", ScRegistry.SaveAtomic);
            if (copyTake)
            {
                // round MP3: the remote player's machine takes it out of its copy of her pack onto the cursor (ScActionsGear's reply)
                remote.PackTake = new ItemStack(backIv.Clone(), 1);
                back = ItemStack.Empty.Clone();
            }
            else back = backIv != null ? new ItemStack(backIv, 1) : ItemStack.Empty.Clone();
            Patches_Xnpc.Count(handed != null ? "n12-panel-armor" : "n12-panel-armor-off");
            string slotName = SlotKeys[slot].Substring(5).ToLowerInvariant();
            ScLog.Out(name + (keep != null ? " now wears " + ScHanded.ItemText(ScHanded.Get(m, ScHanded.ArmorSlot(slot))) + " on her " + slotName
                                           : " goes bare on her " + slotName)
                      + " (member panel" + (remote != null ? ", " + remote.Sender.PlayerDisplayName : "") + ")"
                      + (backIv != null ? " - " + ScHanded.ItemText(backIv) + " to the player" + (wardrobe != null ? (fromStore >= 0 ? " (the Wardrobe's pick, out of her " + (fromStore == ScMemberGear.StoreWindow ? "pack" : "bag")
                                                                                                                                       + (copyTake ? ": their copy of it gives it up" : "") + ")" : "") : "") : "")
                      + (wardrobe != null && fromStore < 0 ? " (her Wardrobe pick " + wardrobe + " is no longer in her pack: nothing comes back for it)" : ""));
            return null;
        }

        /// <summary>One piece of this name out of her pack (the window, first) or her bag - the stack of the look's quality first - as a single item; null when she has none.</summary>
        private static ItemValue FindReal(EntityAlive npc, TEFeatureStorage pack, string item, int quality, out int store, out int slot)
        {
            store = slot = -1;
            ItemStack[][] stores = { pack != null && pack.ItemGrid != null ? pack.ItemGrid.items : null, npc.bag != null ? npc.bag.ItemGrid.items : null };
            int[] storeIds = { ScMemberGear.StoreWindow, ScMemberGear.StoreBag };
            for (int pass = 0; pass < 2 && slot < 0; pass++)
                for (int k = 0; k < stores.Length && slot < 0; k++)
                {
                    ItemStack[] s = stores[k];
                    if (s == null) continue;
                    for (int i = 0; i < s.Length; i++)
                    {
                        ItemStack st = s[i];
                        if (st == null || st.IsEmpty() || !string.Equals(ScHanded.NameOf(st.itemValue), item, StringComparison.OrdinalIgnoreCase)) continue;
                        if (pass == 0 && st.itemValue.HasQuality && ScHanded.LookQuality(st.itemValue) != quality) continue;
                        store = storeIds[k];
                        slot = i;
                        break;
                    }
                }
            if (slot < 0) return null;
            ItemStack found = store == ScMemberGear.StoreWindow ? pack.ItemGrid[slot] : npc.bag.ItemGrid[slot];
            return found.itemValue.Clone();
        }

        /// <summary>
        /// One item off a stack of her pack (told to the open grid: TEFeatureStorage.UpdateSlot notifies its listeners) or her
        /// bag. True when it really left: checked after the write, so a listener that throws after the slot changed still
        /// counts, and one that throws before it does not (the review's fix 2).
        /// </summary>
        private static bool TakeOne(EntityAlive npc, TEFeatureStorage pack, int store, int slot)
        {
            ItemStack before = StoreSlot(npc, pack, store, slot);
            if (before == null || before.IsEmpty()) return false;
            before = before.Clone();
            ItemStack rest = before.Clone();
            rest.count--;
            WriteSlot(npc, pack, store, slot, rest.count > 0 ? rest : ItemStack.Empty.Clone());
            ItemStack after = StoreSlot(npc, pack, store, slot);
            if (after == null || after.IsEmpty()) return before.count <= 1;
            return after.itemValue.type == before.itemValue.type && after.count == before.count - 1;
        }

        /// <summary>A slot of her pack (StoreWindow) or her bag, or null.</summary>
        private static ItemStack StoreSlot(EntityAlive npc, TEFeatureStorage pack, int store, int slot)
        {
            ItemStack[] s = store == ScMemberGear.StoreWindow ? (pack != null && pack.ItemGrid != null ? pack.ItemGrid.items : null)
                          : (npc.bag != null ? npc.bag.ItemGrid.items : null);
            return s != null && slot >= 0 && slot < s.Length ? s[slot] : null;
        }

        /// <summary>Writes one slot of her pack or bag; a throw (a grid listener) is logged, the caller checks what was written.</summary>
        private static void WriteSlot(EntityAlive npc, TEFeatureStorage pack, int store, int slot, ItemStack value)
        {
            try
            {
                if (store == ScMemberGear.StoreWindow) pack.UpdateSlot(slot, value);
                else npc.bag.SetSlot(slot, value);
            }
            catch (Exception e) { ScLog.Once("memberinv-write-slot", e); }
        }

        // ------------------------------------------------------------------ job tools ----

        /// <summary>
        /// A tool for this job into her pack: stacked, else the first free slot; a full pack refuses. back: what stays on the
        /// cursor - nothing, or (the review's fix 2: the pack is counted before and after, so a grid listener that throws part
        /// way can neither lose the tool nor leave it on the cursor as well) the part that did not go in.
        /// </summary>
        private static string DropTool(EntityAlive npc, string name, TEFeatureStorage pack, int job, ItemStack cursor, out ItemStack back)
        {
            back = ItemStack.Empty.Clone();
            if (job < 0 || job >= ScMemberGear.JobCount) return "";
            ItemValue iv = cursor.itemValue;
            ItemClass ic = iv.ItemClass;
            if (ScMemberGear.Rank(job, ic, iv, npc) <= 0f) return Text("scInvRefuseNotTool", Label(ic), Localization.Get(JobKeys[job]));
            if (pack == null || !PackRoom(pack, cursor)) return Text("scInvRefusePackFull", name);
            int before = CountIn(pack, iv.type);
            ItemStack st = cursor.Clone();
            try
            {
                pack.TryStackItem(0, st);
                if (st.count > 0 && pack.AddItem(st.Clone())) st.count = 0;
            }
            catch (Exception e) { ScLog.Once("memberinv-tool-in", e); }
            int placed = Math.Max(0, Math.Min(cursor.count, CountIn(pack, iv.type) - before));
            if (placed <= 0) return Text("scInvRefuseFailed", Label(ic));
            if (placed < cursor.count) { back = cursor.Clone(); back.count = cursor.count - placed; }
            Patches_Xnpc.Count("n12-panel-tool");
            ScLog.Out(name + " now carries " + ScHanded.ItemText(iv) + (placed > 1 ? " x" + placed : "") + " in her pack for "
                      + JobKeys[job].Substring(8).ToLowerInvariant() + " (member panel)" + (placed < cursor.count ? " - " + (cursor.count - placed) + " stayed with the player" : ""));
            return null;
        }

        /// <summary>How many of this item her pack holds.</summary>
        private static int CountIn(TEFeatureStorage pack, int type)
        {
            return pack != null && pack.ItemGrid != null ? CountIn(pack.ItemGrid.items, type) : 0;
        }

        private static int CountIn(ItemStack[] slots, int type)
        {
            int n = 0;
            if (slots != null)
                foreach (ItemStack s in slots) if (s != null && !s.IsEmpty() && s.itemValue != null && s.itemValue.type == type) n += s.count;
            return n;
        }

        /// <summary>The tool shown for this job out of her pack or bag; the work kit and a pair weapon stay with her.</summary>
        private static string TakeTool(EntityPlayer player, EntityAlive npc, string name, TEFeatureStorage pack, int job, bool toBackpack, out ItemStack taken)
        {
            taken = ItemStack.Empty.Clone();
            if (job < 0 || job >= ScMemberGear.JobCount || pack == null) return "";
            ScMemberGear.ToolPick t = ScMemberGear.Tools(npc, pack.ItemGrid.items)[job];      // fresh: what the slot shows now
            if (t == null || t.Stack == null || t.Stack.IsEmpty()) return "";
            if (t.Store == ScMemberGear.StoreToolbelt)
            {
                // toolbelt copies are rebuilt from her item list: taking one would make a free tool (decided: the kit stays)
                string item = ScHanded.NameOf(t.Stack.itemValue);
                Loadout l = ScLoadout.Current(npc);
                bool pair = l != null && (string.Equals(l.Melee, item, StringComparison.OrdinalIgnoreCase) || string.Equals(l.Ranged, item, StringComparison.OrdinalIgnoreCase));
                return Text(pair ? "scInvRefuseKitPair" : "scInvRefuseKit", name);
            }
            // round MP3: a client has no copy of her bag (ScMemberGear.Tools shows none there) - its own copy of her pack only
            if (!ScCommunity.IsHost && t.Store != ScMemberGear.StoreWindow) return "";
            ItemStack real = t.Store == ScMemberGear.StoreWindow ? pack.ItemGrid[t.Slot] : npc.bag != null ? npc.bag.ItemGrid[t.Slot] : null;
            if (real == null || real.IsEmpty() || real.itemValue.type != t.Stack.itemValue.type) return "";
            ItemStack st = real.Clone();
            if (toBackpack && !BackpackRoom(player, st)) return Text("scInvRefuseBackpackFull");
            // the review's fix 2: checked after the write - it is the player's only when the slot really emptied
            WriteSlot(npc, pack, t.Store, t.Slot, ItemStack.Empty.Clone());
            ItemStack after = StoreSlot(npc, pack, t.Store, t.Slot);
            if (after != null && !after.IsEmpty()) return Text("scInvRefuseFailed", Label(st.itemValue));
            taken = st;
            Patches_Xnpc.Count("n12-panel-tool-taken");
            ScLog.Out(name + "'s " + ScHanded.ItemText(st.itemValue) + (st.count > 1 ? " x" + st.count : "") + " went from her " + (t.Store == ScMemberGear.StoreWindow ? "pack" : "bag")
                      + " to the player (member panel, " + JobKeys[job].Substring(8).ToLowerInvariant() + ")");
            return null;
        }

        // ------------------------------------------------------------------ round MP3: a client's own copy of her pack ----

        /// <summary>
        /// Round MP3, a multiplayer client: the job tool slots move a tool between the cursor and this machine's own copy of her
        /// pack (SCore's session copy, merged back by what changed when the panel closes) - exactly what a drag in the pack grid
        /// does, so no request: the server never takes from its own pack while the copy is out. The same rules as the host's
        /// DropTool; Gate's member and downed tests from this machine's view (CopyGate).
        /// </summary>
        internal static string CopyDropTool(EntityPlayerLocal player, EntityAlive npc, TEFeatureStorage copy, int job, ItemStack cursor, out ItemStack back)
        {
            back = ItemStack.Empty.Clone();
            if (cursor == null || cursor.IsEmpty() || cursor.itemValue == null || cursor.itemValue.ItemClass == null) return "";
            string why = CopyGate(player, npc, copy);
            if (why != null) return why;
            return DropTool(npc, ScCommunity.DisplayName(npc), copy, job, cursor, out back);
        }

        /// <summary>Round MP3, a client: the tool a job slot shows, out of this machine's copy of her pack (see CopyDropTool).</summary>
        internal static string CopyTakeTool(EntityPlayerLocal player, EntityAlive npc, TEFeatureStorage copy, int job, bool toBackpack, out ItemStack taken)
        {
            taken = ItemStack.Empty.Clone();
            string why = CopyGate(player, npc, copy);
            if (why != null) return why;
            why = TakeTool(player, npc, ScCommunity.DisplayName(npc), copy, job, toBackpack, out taken);
            if (why != null) return why;
            if (toBackpack) ToBackpack(player, ref taken);
            return null;
        }

        /// <summary>
        /// Round MP3, a client: a member (the synced sc_member flag), alive and in this machine's world, not knocked down, and
        /// copy still the one SCore's session sent this panel (HarvestManager's pending copy: gone once the panel closed).
        /// </summary>
        private static string CopyGate(EntityPlayerLocal player, EntityAlive npc, TEFeatureStorage copy)
        {
            string name = npc != null ? ScCommunity.DisplayName(npc) : "?";
            World w = ScCommunity.World;
            if (player == null || npc == null || w == null || npc.IsDead() || !ReferenceEquals(w.GetEntity(npc.entityId), npc))
                return Text("scInvRefuseGone", name);
            if (!XUiC_ScMemberInvGroup.IsMember(npc)) return Text("scInvRefuseNotMember", name);
            if (ScDowned.IsDown(npc)) return Text("scDownedBusy");
            if (!IsSessionCopy(npc.entityId, copy)) return Text("scInvRefuseReopen", name);
            return null;
        }

        /// <summary>Round MP3, a client: is copy the container SCore's open session sent this machine for this NPC?</summary>
        internal static bool IsSessionCopy(int npcId, TEFeatureStorage copy)
        {
            return copy != null && HarvestManager.ClientPendingEntityId == npcId && ReferenceEquals(HarvestManager.ClientPendingContainer, copy);
        }

        /// <summary>
        /// Round MP3, a client (ScActionsGear's PackTake): one item like want out of this machine's copy of her pack, as a drag
        /// out of the grid takes it - by SCore's item key first (what its merge counts by), else the same item and quality, else
        /// the same item. Checked after the write (a grid listener that throws). Returns what left (empty: nothing like it there
        /// any more - the player already took it, so nothing more is owed).
        /// </summary>
        internal static ItemStack TakeFromCopy(TEFeatureStorage copy, ItemStack want)
        {
            if (copy == null || copy.ItemGrid == null || want == null || want.IsEmpty() || want.itemValue == null) return ItemStack.Empty.Clone();
            ItemStack[] items = copy.ItemGrid.items;
            string key = EntitySyncUtils.ItemKey(want.itemValue);
            int slot = -1;
            for (int pass = 0; pass < 3 && slot < 0; pass++)
                for (int i = 0; i < items.Length && slot < 0; i++)
                {
                    ItemStack s = items[i];
                    if (s == null || s.IsEmpty() || s.itemValue == null || s.itemValue.type != want.itemValue.type) continue;
                    if (pass == 0 && EntitySyncUtils.ItemKey(s.itemValue) != key) continue;
                    if (pass == 1 && s.itemValue.HasQuality && s.itemValue.Quality != want.itemValue.Quality) continue;
                    slot = i;
                }
            if (slot < 0) return ItemStack.Empty.Clone();
            ItemStack before = items[slot].Clone();
            ItemStack rest = before.Clone();
            rest.count--;
            try { copy.UpdateSlot(slot, rest.count > 0 ? rest : ItemStack.Empty.Clone()); }
            catch (Exception e) { ScLog.Once("memberinv-copy-take", e); }
            ItemStack after = copy.ItemGrid.items[slot];
            bool left = after == null || after.IsEmpty() ? before.count <= 1 : after.itemValue.type == before.itemValue.type && after.count == before.count - 1;
            if (!left) return ItemStack.Empty.Clone();
            ItemStack one = before.Clone();
            one.count = 1;
            return one;
        }

        // ------------------------------------------------------------------ stores ----

        /// <summary>Room in her pack for the whole stack (empty slots and partial stacks of the same item).</summary>
        internal static bool PackRoom(TEFeatureStorage pack, ItemStack st)
        {
            return pack != null && pack.ItemGrid != null && Room(pack.ItemGrid.items, st) >= st.count;
        }

        /// <summary>
        /// The stack into her pack: stacked onto her matching stacks, else the first free slot (TEFeatureStorage tells the open
        /// grid). Checked with PackRoom first; should it not fit after all, the rest drops at her feet rather than vanish.
        /// </summary>
        internal static void PutInPack(EntityAlive npc, TEFeatureStorage pack, ItemStack st, string what)
        {
            if (st == null || st.IsEmpty() || st.itemValue == null) return;
            int want = st.count, placed = 0;
            if (pack != null && pack.ItemGrid != null)
            {
                // counted before and after (the review's fix 2): a grid listener that throws part way loses nothing
                int before = CountIn(pack, st.itemValue.type);
                ItemStack s = st.Clone();
                try
                {
                    pack.TryStackItem(0, s);
                    if (s.count > 0) pack.AddItem(s.Clone());
                }
                catch (Exception e) { ScLog.Once("memberinv-pack-put", e); }
                placed = Math.Max(0, Math.Min(want, CountIn(pack, st.itemValue.type) - before));
            }
            if (placed >= want) return;
            ItemStack rest = st.Clone();
            rest.count = want - placed;
            GameManager.Instance.ItemDropServer(rest, npc.position + UnityEngine.Vector3.up, UnityEngine.Vector3.zero, -1, 60f);
            ScLog.Warn(ScCommunity.DisplayName(npc) + ": no room in her pack for " + what + " (" + ScHanded.NameOf(st.itemValue) + (rest.count > 1 ? " x" + rest.count : "")
                       + ") - dropped at her feet");
        }

        /// <summary>
        /// Round n12 re-check (B): an item for the player that is not the cursor's - into the backpack, else the toolbelt
        /// (XUiM_PlayerInventory.AddItem; a weapon, never armor that would be worn), else dropped at the player's feet.
        /// </summary>
        internal static void GiveToPlayer(EntityPlayerLocal player, EntityAlive npc, ItemStack st, string what)
        {
            if (st == null || st.IsEmpty()) return;
            if (player == null && remote != null)
            {
                // round MP3: a remote player's request - their own machine puts it in their bag (the reply's give hint)
                remote.Give.Add(st.Clone());
                ScLog.Out(ScCommunity.DisplayName(npc) + ": " + what + " (" + ScHanded.ItemText(st.itemValue) + ") goes to " + remote.Sender.PlayerDisplayName
                          + "'s inventory (their machine adds it)");
                return;
            }
            XUiM_PlayerInventory inv = Inventory(player);
            ItemStack rest = st.Clone();
            if (inv != null && (inv.AddItemToBackpack(rest) || inv.AddItem(rest.Clone()))) rest = ItemStack.Empty.Clone();
            if (!rest.IsEmpty() && rest.count > 0)
            {
                UnityEngine.Vector3 at = player != null ? player.GetDropPosition() : npc.position + UnityEngine.Vector3.up;
                GameManager.Instance.ItemDropServer(rest, at, UnityEngine.Vector3.zero, -1, 60f);
            }
            ScLog.Out(ScCommunity.DisplayName(npc) + ": " + what + " (" + ScHanded.ItemText(st.itemValue) + ") went to the player's "
                      + (rest.IsEmpty() || rest.count <= 0 ? "inventory" : "feet (no room)"));
        }

        /// <summary>
        /// Room in the player's backpack for the whole stack. Round MP3: a remote player's backpack is their own machine's - it
        /// checked its room before asking (XUiC_ScMemberInv) and puts what does not fit on the cursor; the server does not ask.
        /// </summary>
        internal static bool BackpackRoom(EntityPlayer player, ItemStack st)
        {
            if (remote != null) return true;
            XUiM_PlayerInventory inv = Inventory(player as EntityPlayerLocal);
            return inv != null && inv.Backpack != null && Room(inv.Backpack.ItemGrid.items, st) >= st.count;
        }

        /// <summary>
        /// The stack into the player's backpack (never the toolbelt, never worn: XUiM_PlayerInventory.AddItem would put armor on);
        /// what does not fit stays in it. Round MP3: a remote player's goes back whole in the reply (their machine does this).
        /// </summary>
        internal static void ToBackpack(EntityPlayer player, ref ItemStack st)
        {
            if (st == null || st.IsEmpty() || remote != null) return;
            XUiM_PlayerInventory inv = Inventory(player as EntityPlayerLocal);
            if (inv == null) return;
            ItemStack copy = st.Clone();
            st = inv.AddItemToBackpack(copy) ? ItemStack.Empty.Clone() : copy;
        }

        private static XUiM_PlayerInventory Inventory(EntityPlayerLocal player)
        {
            return player != null && player.PlayerUI != null && player.PlayerUI.xui != null ? player.PlayerUI.xui.PlayerInventory : null;
        }

        /// <summary>How many of this item fit in these slots: an empty slot takes a full stack, one of the same item what it lacks.</summary>
        private static int Room(ItemStack[] slots, ItemStack st)
        {
            if (slots == null || st == null || st.itemValue == null || st.itemValue.ItemClass == null) return 0;
            int max = Math.Max(1, st.itemValue.ItemClass.MaxCount);
            int room = 0;
            foreach (ItemStack s in slots)
            {
                if (s == null || s.IsEmpty()) room += max;
                else if (max > 1 && s.itemValue.type == st.itemValue.type && s.itemValue.ItemClass != null && s.itemValue.ItemClass.CanStack()) room += Math.Max(0, max - s.count);
            }
            return room;
        }

        // ------------------------------------------------------------------ text ----

        /// <summary>A localized line, formatted with its arguments (the plain line should a format fail).</summary>
        internal static string Text(string key, params object[] args)
        {
            string s = Localization.Get(key);
            if (args == null || args.Length == 0) return s;
            try { return string.Format(s, args); }
            catch (FormatException) { return s; }
        }

        internal static string Label(ItemClass ic) { return ic != null ? ic.GetLocalizedItemName() : "?"; }

        internal static string Label(ItemValue iv) { return Label(iv != null && !iv.IsEmpty() ? iv.ItemClass : null); }

        private static void Step(string what, Action step)
        {
            try { step(); }
            catch (Exception e) { ScLog.Once("memberinv-" + what, e); }
        }
    }
}
