using System;
using System.Reflection;
using HarmonyLib;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round U1 (2026-11-16, 0-XNPCCore 3.4.06; research/npc306/FEATURES_306.md sections 1 and 4): Voice Control's new
    /// equipment window and its debug Remove, for the community's people.
    ///
    /// Patch_EquipWindowOpen: 3.4.06's "Equipment..." talk row, the hold-E ring's Equipment entry and the "Jobs and gear" page
    /// all open Voice Control's equipment window through one public static method, XUiC_NpcEquipWindow.Open(EntityPlayerLocal,
    /// EntityAlive). For one of the community's members, with MemberInventoryPanel on, that opens SC's member inventory panel
    /// instead (the user: "Opens SC's member inventory panel"), through SCore's own OpenInventory - the route every other
    /// "show her inventory" takes, so the host, a listen server and a dedicated server's client all behave as they already do
    /// (Patches_MemberInv.Patch_OpenContainer turns it into the panel). The panel has her armor slots and, since this round, the
    /// gun's ammo type (ScAmmoChoice) - the one thing the equipment window had that it lacked; there is no button to the
    /// equipment window. A hired stranger, and a member with the panel off, keep Voice Control's window. The ring's old
    /// "Inventory" entry, which would open the same panel, is hidden on those members (Patches_VoiceControl, D2). A runner met
    /// on the road and a stand-in get neither window ("not now", the voice inventory's answer): the talk offers neither of
    /// them her inventory, and what is handed to a runner on the road can be lost (ScRunners.Rekey).
    ///
    /// PanelClosed (B4): the equipment window's "Show backpack" arms a reopen (s_reopenAfterBackpack) that only a vanilla
    /// looting window's close disarms; SC's panel is its own window group. So when the panel closes, Voice Control's own
    /// OnLootWindowClosed runs - under the bypass, so that if the panel WAS opened from the equipment window (a stranger never
    /// gets the panel; a member only when the redirect did not apply at the time) the window the player came from comes back,
    /// and otherwise the call finds nothing armed and does nothing.
    ///
    /// Patch_AdminRemove: the debug Admin page's "Remove (debug)..." deletes the body through the game's game_remove_entity -
    /// no death, so SC's record of her stays with no body (a ghost member). Config/dialogs.xml hides the row on members; this
    /// refuses it for every one of SC's people (a member, an enclave's or a band's) with a line, should it show anyway.
    ///
    /// Patch_VcSetAmmo: Voice Control's own ammo pick on a member (the order wheel's ammo page, the spoken phrase) becomes her
    /// ammo choice in SC's record too (ScAmmoChoice.Noted), so SC's switch back to the chosen type never undoes it.
    ///
    /// Each target is bound by name (Voice Control is not referenced at build time) and optional: an older Voice Control
    /// without the type binds nothing and logs nothing; one that has the type but not the method logs one error.
    /// </summary>
    public static class ScEquipRedirect
    {
        private const string WindowTypeName = "XUiC_NpcEquipWindow";
        private const string AdminRemoveTypeName = "DialogActionAdminRemove";

        /// <summary>Set while SC itself calls into Voice Control's window (PanelClosed): the redirect stands aside.</summary>
        private static bool bypass;

        private static Type windowType;
        private static bool windowLooked;

        /// <summary>
        /// Voice Control's XUiC_NpcEquipWindow (3.4.06+, global namespace), or null - looked up once (the hold-E ring's D2 asks
        /// on every ring update; Voice Control's assembly is loaded before SC's mod init asks first).
        /// </summary>
        public static Type WindowType
        {
            get
            {
                if (!windowLooked) { windowType = VcType(WindowTypeName); windowLooked = true; }
                return windowType;
            }
        }

        /// <summary>Voice Control's DialogActionAdminRemove (3.4.06+, global namespace), or null.</summary>
        public static Type AdminRemoveType { get { return VcType(AdminRemoveTypeName); } }

        private static Type VcType(string name)
        {
            Type chat = Patches_VoiceControl.ChatType, t = chat != null ? ScTypes.Find(name) : null;
            return t != null && t.Assembly == chat.Assembly ? t : null;
        }

        /// <summary>XUiC_NpcEquipWindow.Open(EntityPlayerLocal player, EntityAlive npc), public static void.</summary>
        public static MethodBase OpenTarget()
        {
            Type t = WindowType;
            MethodInfo m = t != null ? AccessTools.DeclaredMethod(t, "Open", new[] { typeof(EntityPlayerLocal), typeof(EntityAlive) }) : null;
            return m != null && m.IsStatic && m.ReturnType == typeof(void) ? m : null;
        }

        /// <summary>XUiC_NpcEquipWindow.OnLootWindowClosed(), public static void.</summary>
        private static MethodInfo ClosedMethod()
        {
            Type t = WindowType;
            MethodInfo m = t != null ? AccessTools.DeclaredMethod(t, "OnLootWindowClosed", Type.EmptyTypes) : null;
            return m != null && m.IsStatic ? m : null;
        }

        /// <summary>VCCommandRouter.MutateSetAmmo(EntityAlive npc, string arg), internal static void (Voice Control 3.4.05+).</summary>
        public static MethodBase SetAmmoTarget()
        {
            Type t = ScTypes.Find("XNPCVoiceControl.Net.VCCommandRouter");
            MethodInfo m = t != null ? AccessTools.DeclaredMethod(t, "MutateSetAmmo", new[] { typeof(EntityAlive), typeof(string) }) : null;
            return m != null && m.IsStatic ? m : null;
        }

        /// <summary>DialogActionAdminRemove.PerformAction(EntityPlayer player), public void.</summary>
        public static MethodBase AdminRemoveTarget()
        {
            Type t = AdminRemoveType;
            MethodInfo m = t != null ? AccessTools.DeclaredMethod(t, "PerformAction", new[] { typeof(EntityPlayer) }) : null;
            return m != null && !m.IsStatic && m.ReturnType == typeof(void) ? m : null;
        }

        /// <summary>
        /// Does Voice Control's equipment window open SC's member panel for this NPC: the panel on (MemberInventoryPanel) and one
        /// of the community's members (XUiC_ScMemberInvGroup.IsMember: her record on the host, the sc_member cvar on a client).
        /// A read; the ring's D2 asks it too.
        /// </summary>
        public static bool Redirects(EntityAlive npc)
        {
            return ScConfig.MemberInventoryPanel && npc != null && XUiC_ScMemberInvGroup.IsMember(npc);
        }

        /// <summary>One of the community's bodies away from her inventory: a runner on the road (sc_onrun) or a stand-in (sc_standin). A read.</summary>
        private static bool Away(EntityAlive npc)
        {
            return npc.Buffs != null && ((npc.Buffs.HasCustomVar(ScRunners.OnRunCvar) && npc.Buffs.HasCustomVar("sc_member")) || npc.Buffs.HasCustomVar(ScRadio.StandInCvar));
        }

        /// <summary>
        /// B4: SC's member panel just closed. Voice Control's OnLootWindowClosed runs under the bypass: with its reopen armed
        /// (the panel came from its window's Show backpack) the equipment window comes back for that NPC; otherwise nothing.
        /// </summary>
        public static void PanelClosed()
        {
            MethodInfo m;
            try { m = ClosedMethod(); }
            catch (Exception e) { ScLog.Once("equip-closed-bind", e); return; }
            if (m == null) return;
            bypass = true;
            try
            {
                m.Invoke(null, null);
            }
            catch (Exception e) { ScLog.Once("equip-closed", e); }
            finally { bypass = false; }
        }

        private static bool Bindable(MethodBase target, Type type, string label, string consequence)
        {
            if (type == null) return false;                 // this Voice Control has no such window or row: nothing to say
            if (target != null) return true;
            ScLog.Err(label + " not found (Voice Control update?) - " + consequence);
            return false;
        }

        [HarmonyPatch]
        public static class Patch_EquipWindowOpen
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                try
                {
                    return Bindable(OpenTarget(), WindowType, "XUiC_NpcEquipWindow.Open(EntityPlayerLocal, EntityAlive)",
                                    "the equipment window opens for members instead of SC's member inventory panel");
                }
                catch (Exception e) { ScLog.Once("equip-open-bind", e); return false; }
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return OpenTarget(); }

            /// <summary>__0 the local player, __1 the NPC (by position). False: SC's panel opened instead.</summary>
            [HarmonyPrefix]
            public static bool Prefix(EntityPlayerLocal __0, EntityAlive __1)
            {
                try
                {
                    if (bypass || __0 == null || __1 == null) return true;
                    if (Away(__1))
                    {
                        Patches_Xnpc.Count("equip-window-refused-away");
                        ScCommunity.Tell(__0, string.Format(Localization.Get("scEncCallSays"), ScCommunity.DisplayName(__1), Localization.Get("scVoiceNoPackNow")), "ui_denied");
                        return false;
                    }
                    if (!Redirects(__1)) return true;
                    if (!EntityUtilities.ExecuteCMD(__1.entityId, "OpenInventory", __0)) return true;   // SCore did not take it: Voice Control's window
                    Patches_Xnpc.Count("equip-window-to-panel");
                    ScLog.Verbose("equipment window for " + ScCommunity.DisplayName(__1) + " (id " + __1.entityId + ") opened as SC's member inventory panel (round U1)");
                    return false;
                }
                catch (Exception e)
                {
                    ScLog.Once("equip-open", e);
                    return true;
                }
            }
        }

        [HarmonyPatch]
        public static class Patch_VcSetAmmo
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                try
                {
                    if (Patches_VoiceControl.ChatType == null) return false;      // no Voice Control: nothing to follow
                    if (SetAmmoTarget() != null) return true;
                    ScLog.Err("VCCommandRouter.MutateSetAmmo(EntityAlive, string) not found (Voice Control update?) - an ammo type picked through"
                              + " Voice Control on a member may be switched back to the one chosen in SC's member panel");
                }
                catch (Exception e) { ScLog.Once("vc-setammo-bind", e); }
                return false;
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return SetAmmoTarget(); }

            /// <summary>__0 the NPC (by position): her gun's ammo index before the pick.</summary>
            [HarmonyPrefix]
            public static void Prefix(EntityAlive __0, out int __state)
            {
                __state = -1;
                try { __state = ScAmmoChoice.GunIndex(__0); }
                catch (Exception e) { ScLog.Once("vc-setammo-pre", e); }
            }

            /// <summary>Only a pick that changed her gun's type is taken as her choice.</summary>
            [HarmonyPostfix]
            public static void Postfix(EntityAlive __0, int __state)
            {
                try { if (__state >= 0 && ScAmmoChoice.GunIndex(__0) != __state) ScAmmoChoice.Noted(__0); }
                catch (Exception e) { ScLog.Once("vc-setammo-post", e); }
            }
        }

        [HarmonyPatch]
        public static class Patch_AdminRemove
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                try
                {
                    return Bindable(AdminRemoveTarget(), AdminRemoveType, "DialogActionAdminRemove.PerformAction(EntityPlayer)",
                                    "the debug Admin page's Remove can delete one of SC's people without a death (Config/dialogs.xml still hides the row on members)");
                }
                catch (Exception e) { ScLog.Once("admin-remove-bind", e); return false; }
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return AdminRemoveTarget(); }

            /// <summary>The NPC as Voice Control finds it: the talk window's respondent, else the dialog's owner. False: refused.</summary>
            [HarmonyPrefix]
            public static bool Prefix(BaseDialogAction __instance, EntityPlayer __0)
            {
                try
                {
                    EntityPlayerLocal local = __0 as EntityPlayerLocal;
                    LocalPlayerUI ui = local != null ? LocalPlayerUI.GetUIForPlayer(local) : null;
                    EntityAlive npc = ui != null && ui.xui != null && ui.xui.Dialog != null ? ui.xui.Dialog.Respondent as EntityAlive : null;
                    if (npc == null && __instance != null && __instance.OwnerDialog != null) npc = __instance.OwnerDialog.CurrentOwner as EntityAlive;
                    if (npc == null || Patches_VoiceControl.PickupRefusal(npc) == null) return true;
                    Patches_Xnpc.Count("admin-remove-refused");
                    ScCommunity.Tell(__0, string.Format(Localization.Get("scAdminRemoveRefused"), ScCommunity.DisplayName(npc)), "ui_denied");
                    ScLog.Out("debug Remove refused: " + ScCommunity.DisplayName(npc) + " (id " + npc.entityId + ") is one of SC's people - removing the body"
                              + " without a death would leave SC's record with no body (round U1)");
                    return false;
                }
                catch (Exception e)
                {
                    ScLog.Once("admin-remove", e);
                    return true;
                }
            }
        }
    }
}
