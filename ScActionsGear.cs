using System;
using System.Collections.Generic;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round MP3 (multiplayer, part G: member gear): the server's handlers for the member inventory panel's gear slots and
    /// ammo line (ScActions' [ScAction] ops). The panel asks through ScActions.Request on every machine; on the server (single
    /// player, the host) the handler runs inline with the local player, on the host's own cursor and container in place - the
    /// old SlotDrop / SlotTake / Ammo bodies, unchanged - so the host's play takes this same path.
    ///
    /// A remote player's panel shows the copy of her pack SCore sent it (SCore's remote session, merged back on close by what
    /// the player changed - HarvestManager.EndRemoteSession). For them:
    ///  - The cursor's stack goes in the request (ScItemTx), taken off their cursor first; the server runs ScMemberEquip
    ///    (Remote) against her pair, her look, her bag and its own pack, and answers. A refusal gives their stack back; done,
    ///    the reply carries what comes back (hint cur: the slot's old item for the cursor; give: other items for their bag;
    ///    pt: her Wardrobe pick's real piece, which their own machine takes out of its copy of her pack - ScMemberEquip.Armor).
    ///  - The server never takes from its own pack while their copy is out (their grid would still show the item); it only
    ///    adds to it (a stale stored piece), which SCore's merge keeps.
    ///  - The job tool slots move items in their own copy of her pack with no request (ScMemberEquip.CopyDropTool /
    ///    CopyTakeTool); a remote tool request is refused (silently).
    ///  - Every request needs their own session (ScMemberEquip.Gate): only one player has a copy out at a time (SCore refuses a
    ///    second), and the host cannot open her panel meanwhile.
    ///  - The bag stays the host's (no copy of an NPC's bag on a client; the panel says so).
    ///
    /// Ops (ints unless marked s:; "npc" is the member's entity id; kind = ScMemberEquip.GearKind: 1 weapon, 2 armor, 3 tool):
    ///   member.geardrop   npc, kind, index; s: the stack     the cursor's stack dropped on a slot. Hints cur, give, pt.
    ///   member.geartake   npc, kind, index, toBackpack (0/1) a slot's item taken. Hints cur, give, pt; placed=1 (the host's:
    ///                                                      it all went into the backpack - the place sound).
    ///   member.ammo       npc, step (1 / -1)               ScAmmoChoice.Cycle; the reply's text is the refusal, or a done note.
    ///   member.ammoline   npc                              a read: the ammo line's parts (hints show, key, ammo, n).
    ///   member.gearview   npc                              round mt1 (part G, F2): a quiet read - the equipment column as the
    ///                                                      server sees it (ScMemberGear.GearView: RANGED, MELEE, the armor, the
    ///                                                      ammo line's parts, the job tools of her bag and toolbelt), hints v*
    ///                                                      (ScMemberGear.PutView). A remote geardrop / geartake reply carries
    ///                                                      the same view after its own hints, when it fits (MaxReplyChars).
    ///                                                      Round mt1 (review fix G-m3): only for a player with her inventory
    ///                                                      open (ScAmmo's mark) or within ViewReach; else a quiet refusal.
    /// </summary>
    public static class ScActionsGear
    {
        public const string OpDrop = "member.geardrop", OpTake = "member.geartake", OpAmmo = "member.ammo", OpAmmoLine = "member.ammoline";
        /// <summary>Round mt1 (part G, F2): the server's view of her equipment column (a quiet read; ScMemberGear.GearView).</summary>
        public const string OpGearView = "member.gearview";

        /// <summary>
        /// Round mt1 (part G): the most characters a reply's hint gets from the gear view - a view alone (MaxViewChars), and a
        /// whole remote gear reply with its own stacks (MaxReplyChars: the view is left out when the stacks leave no room for
        /// it, and the client asks for it). NetPackageScResult writes the hint as one string with no cap of its own, and the
        /// game's connection buffers take a package of up to about 2 MB; a typical view is 2-4 thousand characters.
        /// </summary>
        private const int MaxViewChars = 16000, MaxReplyChars = 24000;

        /// <summary>Round mt1 (review fix G-m3): member.gearview answers a player within this many meters of her without an open session (the panel closes at about 6 m).</summary>
        private const float ViewReach = 10f;

        /// <summary>The stack for the player's cursor (ScItemTx.StacksToHint, one stack), items for their bag, her real piece to take out of their copy.</summary>
        public const string HintCursor = "cur", HintGive = "give", HintPackTake = "pt";
        /// <summary>member.geartake on the host: the item all went into the backpack (the panel plays the place sound).</summary>
        public const string HintPlaced = "placed";
        /// <summary>member.ammoline: 1 when there is a line; its localization key, ammo item name and count.</summary>
        public const string HintAmmoShow = "show", HintAmmoKey = "key", HintAmmoItem = "ammo", HintAmmoCount = "n";

        private static ScMemberEquip.GearKind KindOf(int k)
        {
            return k == (int)ScMemberEquip.GearKind.Weapon || k == (int)ScMemberEquip.GearKind.Armor || k == (int)ScMemberEquip.GearKind.Tool
                ? (ScMemberEquip.GearKind)k : ScMemberEquip.GearKind.None;
        }

        /// <summary>The host's own open panel when it shows this member (its request came from it, inline), else null.</summary>
        private static XUiC_ScMemberInv HostPanel(int npcId)
        {
            XUiC_ScMemberInv p = XUiC_ScMemberInvGroup.OpenPanel();
            return p != null && p.Shown != null && p.Container != null && p.Shown.entityId == npcId ? p : null;
        }

        private static XUiC_DragAndDropWindow Cursor(EntityPlayerLocal local)
        {
            return local != null && local.PlayerUI != null && local.PlayerUI.xui != null ? local.PlayerUI.xui.DragAndDropWindow : null;
        }

        /// <summary>A remote request's member and the server's own container of her pack (null pack: none there).</summary>
        private static EntityAlive RemoteNpc(int npcId, out TEFeatureStorage pack)
        {
            World w = ScCommunity.World;
            EntityAlive npc = w != null && npcId > 0 ? w.GetEntity(npcId) as EntityAlive : null;
            pack = npc != null && HarvestManager.Has(npcId) ? HarvestManager.GetOrCreate(npcId) : null;
            return npc;
        }

        // ------------------------------------------------------------------ the gear slots ----

        [ScAction(OpDrop, ScActionAccess.Community)]
        private static void GearDrop(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            int npcId = args.Int(0, -1), index = args.Int(2, -1);
            ScMemberEquip.GearKind kind = KindOf(args.Int(1, 0));
            EntityPlayerLocal local = sender as EntityPlayerLocal;
            if (local != null)
            {
                // the host's own panel: its cursor and its container in place, exactly as before round MP3
                XUiC_ScMemberInv panel = HostPanel(npcId);
                XUiC_DragAndDropWindow dnd = Cursor(local);
                if (panel == null || dnd == null) { reply.Fail("", ""); return; }
                ItemStack back;
                string why = ScMemberEquip.Drop(local, panel.Shown, panel.Container, kind, index, dnd.CurrentStack, out back);
                if (why != null) { reply.Fail(why, ""); return; }
                // the cursor first (the review's fix 2): the dropped stack is spent - she has it now - whatever the sound does
                dnd.SetCurrentStack(back ?? ItemStack.Empty.Clone());
                dnd.PickUpType = XUiC_ItemStack.StackLocationTypes.LootContainer;
                reply.Done("", "");
                return;
            }
            if (kind != ScMemberEquip.GearKind.Weapon && kind != ScMemberEquip.GearKind.Armor) { reply.Fail("", ""); return; }
            TEFeatureStorage pack;
            EntityAlive npc = RemoteNpc(npcId, out pack);
            ItemStack cursor = ScItemTx.Decode(args.Str(0));
            if (cursor.IsEmpty()) { reply.Fail("", ""); return; }       // nothing, or an item this server does not know: theirs again
            ScMemberEquip.RemoteGear rg = ScMemberEquip.BeginRemote(sender);
            try
            {
                ItemStack back;
                string why = ScMemberEquip.Drop(sender, npc, pack, kind, index, cursor, out back);
                if (why != null) { reply.Fail(why, ""); return; }
                Answer(sender, reply, back, rg, npc);
            }
            finally { ScMemberEquip.EndRemote(); }
        }

        [ScAction(OpTake, ScActionAccess.Community)]
        private static void GearTake(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            int npcId = args.Int(0, -1), index = args.Int(2, -1);
            ScMemberEquip.GearKind kind = KindOf(args.Int(1, 0));
            bool toBackpack = args.Int(3, 0) != 0;
            EntityPlayerLocal local = sender as EntityPlayerLocal;
            if (local != null)
            {
                // the host's own panel, as before round MP3
                XUiC_ScMemberInv panel = HostPanel(npcId);
                XUiC_DragAndDropWindow dnd = Cursor(local);
                if (panel == null || dnd == null || !dnd.CurrentStack.IsEmpty()) { reply.Fail("", ""); return; }
                ItemStack taken;
                string why = ScMemberEquip.Take(local, panel.Shown, panel.Container, kind, index, toBackpack, out taken);
                if (why != null) { reply.Fail(why, ""); return; }
                if (taken != null && !taken.IsEmpty())
                {
                    dnd.SetCurrentStack(taken);
                    dnd.PickUpType = XUiC_ItemStack.StackLocationTypes.LootContainer;
                }
                else if (toBackpack) reply.SetHint(HintPlaced, 1);
                reply.Done("", "");
                return;
            }
            if (kind != ScMemberEquip.GearKind.Weapon && kind != ScMemberEquip.GearKind.Armor) { reply.Fail("", ""); return; }
            TEFeatureStorage pack;
            EntityAlive npc = RemoteNpc(npcId, out pack);
            ScMemberEquip.RemoteGear rg = ScMemberEquip.BeginRemote(sender);
            try
            {
                ItemStack taken;
                string why = ScMemberEquip.Take(sender, npc, pack, kind, index, toBackpack, out taken);
                if (why != null) { reply.Fail(why, ""); return; }
                Answer(sender, reply, taken, rg, npc);
            }
            finally { ScMemberEquip.EndRemote(); }
        }

        /// <summary>
        /// A remote player's change is made: the reply carries what their machine adds. Never throws, and never loses an item:
        /// a stack too large for a reply (ScItemTx.MaxEncoded) drops at their feet instead.
        /// Round mt1 (part G, F2): then her equipment column as it is now (ScMemberGear.GearView), so the client's slots show the
        /// change with the server's own items at once - left out (the client asks) when the stacks above leave it no room. The
        /// items are settled first: a view that throws or does not fit costs only a poll.
        /// </summary>
        private static void Answer(EntityPlayer sender, ScActionReply reply, ItemStack cursor, ScMemberEquip.RemoteGear rg, EntityAlive npc)
        {
            reply.Done("", "");
            if (cursor != null && !cursor.IsEmpty()) Put(reply, HintCursor, new List<ItemStack> { cursor }, sender);
            if (rg.Give.Count > 0) Put(reply, HintGive, rg.Give, sender);
            // still in her pack if it cannot be named: nothing to drop, nothing lost
            if (rg.PackTake != null) Put(reply, HintPackTake, new List<ItemStack> { rg.PackTake }, null);
            if (npc == null) return;
            try { ScMemberGear.PutView(reply, ScMemberGear.BuildView(npc), Math.Min(MaxViewChars, MaxReplyChars - (reply.Hint ?? "").Length)); }
            catch (Exception e) { ScLog.Once("gear-reply-view", e); }
        }

        private static void Put(ScActionReply reply, string key, List<ItemStack> stacks, EntityPlayer dropFor)
        {
            string hint = null;
            try { hint = ScItemTx.StacksToHint(stacks); }
            catch (Exception e) { ScLog.Once("gear-reply-encode", e); }
            if (!string.IsNullOrEmpty(hint)) { reply.SetHint(key, hint); return; }
            if (dropFor == null) return;
            foreach (ItemStack s in stacks)
            {
                if (s == null || s.IsEmpty()) continue;
                try
                {
                    GameManager.Instance.ItemDropServer(s.Clone(), dropFor.GetPosition(), Vector3.zero, dropFor.entityId, 60f, false);
                    ScLog.Warn("member panel: " + ScHanded.ItemText(s.itemValue) + " for " + dropFor.PlayerDisplayName + " is too large to send - dropped at their feet");
                }
                catch (Exception e) { ScLog.Once("gear-reply-drop", e); }
            }
        }

        // ------------------------------------------------------------------ the ammo line ----

        [ScAction(OpAmmo, ScActionAccess.Community)]
        private static void Ammo(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            int npcId = args.Int(0, -1), step = args.Int(1, 1) < 0 ? -1 : 1;
            World w = ScCommunity.World;
            EntityAlive npc = w != null && npcId > 0 ? w.GetEntity(npcId) as EntityAlive : null;
            if (npc == null) { reply.Fail("", ""); return; }
            string note;
            string why = ScAmmoChoice.Cycle(sender, npc, step, out note);
            if (why != null) reply.Fail(why, "");
            else reply.Done(note ?? "", "");
        }

        /// <summary>A read: the panel's ammo line for her, as the server's ScAmmoChoice sees it (a client has neither her bag nor her choice).</summary>
        [ScAction(OpAmmoLine, ScActionAccess.Community, Quiet = true)]      // round MP3 merge: polled every 2 s - not in the log
        private static void AmmoLine(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            int npcId = args.Int(0, -1);
            World w = ScCommunity.World;
            EntityAlive npc = w != null && npcId > 0 ? w.GetEntity(npcId) as EntityAlive : null;
            string key, ammo;
            int n;
            reply.Done("", "");
            if (npc == null || !ScAmmoChoice.LineParts(npc, out key, out ammo, out n) || ammo.IndexOf(';') >= 0 || ammo.IndexOf('=') >= 0)
            {
                reply.SetHint(HintAmmoShow, 0);
                return;
            }
            reply.SetHint(HintAmmoShow, 1).SetHint(HintAmmoKey, key).SetHint(HintAmmoItem, ammo).SetHint(HintAmmoCount, n);
        }

        // ------------------------------------------------------------------ round mt1 (part G): the gear view ----

        /// <summary>
        /// Round mt1 (part G, F2; bug_gear_equip_window.md 1.2): a read - her equipment column as the server sees it, for a
        /// multiplayer client's panel (polled every 1.5 s while it is open, one request at a time; every remote gear reply
        /// carries it too). A member only; anything else is refused silently. Nothing changes here. The host's own panel never
        /// asks: it calls the same builders inline.
        /// </summary>
        [ScAction(OpGearView, ScActionAccess.Community, Quiet = true)]      // polled: not in the log
        private static void ViewGear(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            int npcId = args.Int(0, -1);
            World w = ScCommunity.World;
            EntityAlive npc = w != null && npcId > 0 ? w.GetEntity(npcId) as EntityAlive : null;
            if (npc == null || npc.IsDead() || ScRegistry.GetMember(npcId) == null) { reply.Fail("", ""); return; }
            // round mt1 (review fix G-m3): only for a player at her panel - one who has her inventory open (the server's mark of
            // SCore's session: every open panel has one) or stands within ViewReach of her; anyone else is refused quietly, before
            // anything is built (a distance, then a table lookup)
            if (sender == null || (!(sender is EntityPlayerLocal) && (npc.position - sender.position).sqrMagnitude > ViewReach * ViewReach
                                   && !ScAmmo.IsRemoteOpenBy(npcId, sender.entityId)))
            {
                reply.Fail("", "");
                return;
            }
            reply.Done("", "");
            if (!ScMemberGear.PutView(reply, ScMemberGear.BuildView(npc), MaxViewChars)) reply.Fail("", "");
        }
    }
}
