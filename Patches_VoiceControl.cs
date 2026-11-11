using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace SurvivorCommunity
{
    /// <summary>
    /// Members are never billed (the user's call, 2026-10-01) but keep their voice.
    ///
    /// 0-XNPCCore 3.2 bundles Voice Control (1-XNPCVoiceControl.dll). It gives a chat component to every NPC whose
    /// class or type reads as one (ChatTargetRules.IsChatTarget: "survivor"/"npc" in the class name, the npc tag, an
    /// NPC/Bandit/Trader type), which is every class this mod has. Once such an NPC has a leader, the component seeds a
    /// weekly hire contract (NPCChatComponent.MaybeStampHireDay writes the Retainer* cvars), asks the leader for money
    /// when it falls due (CheckBillingReactor, every 2 s) and, unpaid after the grace days, runs SCore's Dismiss - which
    /// this mod reads as the member leaving. Its modconfig has rates but no off switch, and the no_voice tag that
    /// exempts an NPC also silences it. So, for members and their outpost stand-ins:
    ///  - no contract is ever seeded (MaybeStampHireDay skipped; 3.4 X1: Voice Control 3.4.02's SeedContract too, which
    ///    today only MaybeStampHireDay calls - a guard in case another path ever does);
    ///  - (3.4 X1) a player's reply to a billing prompt ("pay", "yes", "no") never reaches HandleBillingResponse for a
    ///    member, and (X2) TryDeductPayment takes nothing from the leader for one - Voice Control 3.4.03 deducts before
    ///    HandleBillingResponse runs;
    ///  - the reactor is skipped and an old contract stripped, which covers one seeded before they joined;
    ///  - the contract cvars are stripped when someone joins (ScCommunity.StampCvars) and at every relink (ScTicker),
    ///    so nothing lingers in the save;
    ///  - the spoken "dismiss" sends a member home (ScCommunity.ReturnHome) instead of out of the community, and
    ///    "pick up" and "hire" are refused - the dialog's Dismiss, Pickup and Hire are hidden from members for the same
    ///    reason. Voice Control never reads dialog requirements: its "hire" opens SCore's hire window for any NPC
    ///    without a leader, which would charge the fee for a resident and flip them to Following, or turn an outpost
    ///    stand-in into a saved, untracked copy of the member.
    /// Other NPCs, including companions hired the vanilla way, keep Voice Control's billing.
    /// E2: one of an enclave's people (the %sc_enc cvar) is refused the spoken hire, pick-up and dismiss as well - they stay
    /// with their group, and SCore's Dismiss would end the Guard that holds them at their spot.
    /// E3: one of them enlisted with the player (the sc_enlisted cvar; SC handles the fee, Q8 a) never gets a contract or a
    /// bill either - the narrowest hook: the two billing prefixes ask for it, IsCommunityNpc stays the members' test (it also
    /// drives the drop window, hold-fire, XNPC's levels and trust) - and the spoken dismiss sends them home (ScEnclaveDeals.GoHome).
    /// E8: Voice Control's LLM chat with one of an enclave's people carries what they know of their group (ScVoiceContext), and
    /// the spoken "trade" is refused by a hostile enclave's people, raiders and survivors with a grudge (the section at the end);
    /// round 28 (3.4): by every member and stand-in too, with their own friendly line (MemberTrade).
    /// Round 28 (B1, 3.4): no "Pick me up" from Voice Control 3.4.04's hold-E ring on members, stand-ins, enclave or band people
    /// (SCore's Collect, Voice Control's TryPickup, the ring's entry and "Jobs and gear"'s page).
    /// E13a: one of a band on the road (the %sc_band cvar) is refused the spoken hire, pick-up and dismiss as an enclave's people
    /// are, and fights at will (Patch_MayEngage).
    ///
    /// Bound by name, with no reference to the DLL: without Voice Control every patch here skips itself (Prepare), and
    /// ModApi only expects them when NPCChatComponent exists, so a changed signature shows up as a MISSING PATCH line.
    /// </summary>
    public static class Patches_VoiceControl
    {
        /// <summary>
        /// Voice Control's contract cvars (BillingContractRules.ContractCvars). RetainerLeader is new in 3.4.02 (the leader
        /// the contract was made with, which carries it across sessions); an older Voice Control never writes it.
        /// </summary>
        public static readonly string[] ContractCvars =
            { "RetainerWeek", "RetainerNextDueDay", "RetainerGraceDay", "RetainerAwaitingApproval", "RetainerPromptDay", "RetainerEarnings",
              "RetainerLeader" };

        /// <summary>Counters for sc xnpc. Main thread only.</summary>
        private static readonly Dictionary<string, int> counts = new Dictionary<string, int>();

        public static Type ChatType { get { return ScTypes.Find("XNPCVoiceControl.NPCChatComponent"); } }
        public static Type TriggerType { get { return ScTypes.Find("XNPCVoiceControl.PhraseTriggerHandler"); } }

        public static MethodBase StampTarget()
        {
            Type t = ChatType;
            return t != null ? AccessTools.DeclaredMethod(t, "MaybeStampHireDay", new[] { typeof(Entity) }) : null;
        }

        public static MethodBase ReactorTarget()
        {
            Type t = ChatType;
            return t != null ? AccessTools.DeclaredMethod(t, "CheckBillingReactor", new Type[0]) : null;
        }

#if SC_V34
        /// <summary>X1: NPCChatComponent.SeedContract(), private (Voice Control 3.4.02; not in 3.4.01).</summary>
        public static MethodBase SeedTarget()
        {
            Type t = ChatType;
            return t != null ? Returning(AccessTools.DeclaredMethod(t, "SeedContract", new Type[0]), typeof(void), false) : null;
        }

        /// <summary>
        /// X1: NPCChatComponent.HandleBillingResponse, private: the player's answer to a bill. (bool accepted, bool paid) since
        /// Voice Control 3.4.03 (X2), (bool accepted) before it. The new shape first.
        /// </summary>
        public static MethodBase BillingReplyTarget()
        {
            Type t = ChatType;
            if (t == null) return null;
            return FirstReturning(t, "HandleBillingResponse", typeof(void), false,
                new[] { typeof(bool), typeof(bool) }, new[] { typeof(bool) });
        }

        /// <summary>
        /// X2: NPCChatComponent.TryDeductPayment(), private bool: the one place Voice Control takes the leader's money. Since
        /// 3.4.03 it runs BEFORE HandleBillingResponse (SettleBillingReply, and a client's routed answer), so the reply
        /// prefix alone no longer keeps a member from being charged.
        /// </summary>
        public static MethodBase DeductTarget()
        {
            Type t = ChatType;
            return t != null ? Returning(AccessTools.DeclaredMethod(t, "TryDeductPayment", new Type[0]), typeof(bool), false) : null;
        }
#endif

        private static readonly Type[] OrderArgs = { typeof(EntityAlive), typeof(EntityPlayer), typeof(string), typeof(string) };

        public static MethodBase DismissTarget()
        {
            Type t = TriggerType;
            MethodInfo m = t != null ? AccessTools.DeclaredMethod(t, "Dismiss", OrderArgs) : null;
            return m != null && m.ReturnType == typeof(string) ? m : null;
        }

        public static MethodBase PickupTarget()
        {
            Type t = TriggerType;
            MethodInfo m = t != null ? AccessTools.DeclaredMethod(t, "Pickup", OrderArgs) : null;
            return m != null && m.ReturnType == typeof(string) ? m : null;
        }

        public static MethodBase HireTarget()
        {
            Type t = TriggerType;
            MethodInfo m = t != null ? AccessTools.DeclaredMethod(t, "Hire", OrderArgs) : null;
            return m != null && m.ReturnType == typeof(string) ? m : null;
        }

        /// <summary>The component's NPC field, which the two billing prefixes take by injection (____npcEntity).</summary>
        private static bool HasNpcField()
        {
            Type t = ChatType;
            FieldInfo f = t != null ? AccessTools.Field(t, "_npcEntity") : null;
            return f != null && f.FieldType == typeof(EntityAlive);
        }

        public static void Reset()
        {
            counts.Clear();
#if SC_V34
            ringEntryHidden = 0;
            inventoryAsked = 0;
#endif
        }

        private static void Count(string what)
        {
            int n;
            counts.TryGetValue(what, out n);
            counts[what] = n + 1;
        }

        public static string Report()
        {
            if (ChatType == null) return "Voice Control not loaded";
#if SC_V34
            if (counts.Count == 0 && ringEntryHidden == 0) return "nothing intercepted yet";
#else
            if (counts.Count == 0) return "nothing intercepted yet";
#endif
            var keys = new List<string>(counts.Keys);
            keys.Sort(StringComparer.Ordinal);
            var parts = new List<string>();
            foreach (string k in keys) parts.Add(k + "=" + counts[k]);
#if SC_V34
            if (ringEntryHidden > 0) parts.Add("ring-pickup-entry-hidden-calls=" + ringEntryHidden);   // B1: per UpdateActivationCommands call
#endif
            return string.Join(", ", parts.ToArray());
        }

        /// <summary>A community member or an outpost stand-in (both carry our cvar from the moment they exist).</summary>
        public static bool IsCommunityNpc(EntityAlive e)
        {
            return e != null && e.Buffs != null && (e.Buffs.HasCustomVar("sc_member") || e.Buffs.HasCustomVar(ScRadio.StandInCvar));
        }

        /// <summary>Removes a Voice Control hire contract. True when there was one.</summary>
        public static bool StripContract(EntityAlive e)
        {
            if (e == null || e.Buffs == null) return false;
            bool had = false;
            for (int i = 0; i < ContractCvars.Length; i++)
            {
                if (!e.Buffs.HasCustomVar(ContractCvars[i])) continue;
                e.Buffs.RemoveCustomVar(ContractCvars[i]);
                had = true;
            }
            if (had) Count("contract-stripped");
            return had;
        }

        [HarmonyPatch]
        public static class Patch_NoHireContract
        {
            [HarmonyPrepare]
            public static bool Prepare() { return StampTarget() != null && HasNpcField(); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return StampTarget(); }

            [HarmonyPrefix]
            public static bool Prefix(EntityAlive ____npcEntity)
            {
                try
                {
                    // E3: one of an enclave's people enlisted with the player is not billed either
                    if (!IsCommunityNpc(____npcEntity) && !ScEnclaveDeals.IsEnlistedBody(____npcEntity)) return true;
                    Count("contract-refused");
                    return false;
                }
                catch (Exception e)
                {
                    ScLog.Once("vc-stamp", e);
                    return true;
                }
            }
        }

        [HarmonyPatch]
        public static class Patch_NoBilling
        {
            [HarmonyPrepare]
            public static bool Prepare() { return ReactorTarget() != null && HasNpcField(); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return ReactorTarget(); }

            [HarmonyPrefix]
            public static bool Prefix(EntityAlive ____npcEntity)
            {
                try
                {
                    if (!IsCommunityNpc(____npcEntity) && !ScEnclaveDeals.IsEnlistedBody(____npcEntity)) return true;
                    StripContract(____npcEntity);
                    return false;
                }
                catch (Exception e)
                {
                    ScLog.Once("vc-billing", e);
                    return true;
                }
            }
        }

#if SC_V34
        /// <summary>
        /// X1 (Voice Control 3.4.02). 3.4.02 split the contract's writes out of MaybeStampHireDay into SeedContract (which also
        /// writes the new RetainerLeader). Its only caller today is MaybeStampHireDay, which Patch_NoHireContract already
        /// skips for these NPCs (TryReadLeaderFromSCore, at chat start and on every player message, calls MaybeStampHireDay,
        /// as in 3.4.01), so this is a guard: should another path ever seed a contract, members, outpost stand-ins and E3's
        /// enlisted people still get none, and any they carry is stripped. Other NPCs keep their contracts.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_NoContractSeed
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                if (SeedTarget() != null && HasNpcField()) return true;
                return false;   // Voice Control before 3.4.02 has no SeedContract: MaybeStampHireDay (skipped) seeds there
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return SeedTarget(); }

            [HarmonyPrefix]
            public static bool Prefix(EntityAlive ____npcEntity)
            {
                try
                {
                    if (!IsCommunityNpc(____npcEntity) && !ScEnclaveDeals.IsEnlistedBody(____npcEntity)) return true;
                    StripContract(____npcEntity);
                    Count("contract-seed-refused");
                    return false;
                }
                catch (Exception e)
                {
                    ScLog.Once("vc-seed", e);
                    return true;
                }
            }
        }

        /// <summary>
        /// X1. Voice Control's HandleBillingResponse - the player's "pay" / "yes" / "no" to a bill (ProcessPlayerMessage takes a
        /// message as one only while the NPC carries RetainerNextDueDay; 3.4.02 adds "pay" in the grace days) and the only
        /// caller of TryDeductPayment - never runs for a member, a stand-in or an enlisted person, so no path can take money
        /// for one, even in the 2 s before Patch_NoBilling strips a contract left from before they joined; it strips it here.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_NoBillingReply
        {
            [HarmonyPrepare]
            public static bool Prepare() { return BillingReplyTarget() != null && HasNpcField(); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return BillingReplyTarget(); }

            [HarmonyPrefix]
            public static bool Prefix(EntityAlive ____npcEntity)
            {
                try
                {
                    if (!IsCommunityNpc(____npcEntity) && !ScEnclaveDeals.IsEnlistedBody(____npcEntity)) return true;
                    StripContract(____npcEntity);
                    Count("billing-reply-refused");
                    return false;
                }
                catch (Exception e)
                {
                    ScLog.Once("vc-billing-reply", e);
                    return true;
                }
            }
        }

        /// <summary>
        /// X2. Voice Control 3.4.03 takes the money (TryDeductPayment) before HandleBillingResponse, in SettleBillingReply and
        /// in a client's routed answer, so the guard sits on the deduction itself: for a member, a stand-in or an enlisted
        /// person it takes nothing, answers "not paid" and strips the contract. On 3.4.01/02 the reply prefix above already
        /// keeps this from running for them.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_NoBillingDeduct
        {
            [HarmonyPrepare]
            public static bool Prepare() { return DeductTarget() != null && HasNpcField(); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return DeductTarget(); }

            [HarmonyPrefix]
            public static bool Prefix(EntityAlive ____npcEntity, ref bool __result)
            {
                try
                {
                    if (!IsCommunityNpc(____npcEntity) && !ScEnclaveDeals.IsEnlistedBody(____npcEntity)) return true;
                    StripContract(____npcEntity);
                    Count("billing-deduct-refused");
                    __result = false;
                    return false;
                }
                catch (Exception e)
                {
                    ScLog.Once("vc-billing-deduct", e);
                    return true;
                }
            }
        }
#endif

        /// <summary>The spoken "dismiss": a member goes home, never out of the community.</summary>
        [HarmonyPatch]
        public static class Patch_VoiceDismiss
        {
            [HarmonyPrepare]
            public static bool Prepare() { return DismissTarget() != null; }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return DismissTarget(); }

            [HarmonyPrefix]
            public static bool Prefix(object[] __args, ref string __result)
            {
                try
                {
                    EntityAlive npc = __args[0] as EntityAlive;
                    // one of an enclave's people (E2): nobody's to dismiss - SCore's Dismiss would end their Guard at their spot;
                    // E3: one enlisted with the player goes home, never left as a stranger
                    if (ScEnclavePeople.IsEnclaveNpc(npc))
                    {
                        if (ScEnclaveDeals.IsEnlistedBody(npc) && ScEnclaveDeals.GoHome(ScNpc.As(npc), "Voice Control's spoken dismiss"))
                        {
                            Count("dismiss-enlisted-home");
                            __result = Localization.Get("scEncVoiceGoingHome");
                            return false;
                        }
                        Count("dismiss-refused-enclave");
                        __result = Localization.Get("scEncVoiceStays");
                        return false;
                    }
                    // E13a: one of a band on the road is nobody's to dismiss either (SCore's Dismiss would end the Guard that walks them)
                    if (ScBands.IsBandNpc(npc))
                    {
                        Count("dismiss-refused-enclave");
                        __result = Localization.Get("scEncVoiceStays");
                        return false;
                    }
                    if (!IsCommunityNpc(npc)) return true;
                    Count("dismiss-to-home");
                    StripContract(npc);
                    Member m = ScRegistry.Loaded ? ScRegistry.GetMember(npc.entityId) : null;
                    EntityAlive sdx = ScNpc.As(npc);
                    EntityPlayerLocal lp = __args[1] as EntityPlayerLocal;
                    string error = "scVoiceStays";
                    if (m != null && sdx != null && lp != null && ScCommunity.ReturnHome(m, sdx, lp, out error))
                    {
                        ScCommunity.Tell(lp, string.Format(Localization.Get("scStationed"), m.Name), "");
                        __result = Localization.Get("scVoiceGoingHome");
                    }
                    else
                    {
                        if (lp != null && error != null && error != "scVoiceStays") ScCommunity.Tell(lp, Localization.Get(error), "ui_denied");
                        __result = Localization.Get("scVoiceStays");
                    }
                    return false;
                }
                catch (Exception e)
                {
                    ScLog.Once("vc-dismiss", e);
                    __result = Localization.Get("scVoiceStays");
                    return false;       // never fall through to SCore's Dismiss for a member
                }
            }
        }
#if SC_V34

        /// <summary>3.4 (D5): SCore 3.4's companion window (ESC, its NPC list) Dismiss button (private, on XUiC_SCoreCompanion).</summary>
        public static MethodBase CompanionDismissTarget()
        {
            return AccessTools.DeclaredMethod(typeof(XUiC_SCoreCompanion), "BtnDismiss_Controller_OnPress", new[] { typeof(XUiController), typeof(int) });
        }

        /// <summary>The companion the window row shows (XUiC_SCoreCompanion.Companion, a private auto-property).</summary>
        private static MethodInfo companionGetter;
        private static bool companionGetterBound;

        private static EntityAlive CompanionOf(XUiC_SCoreCompanion row)
        {
            if (!companionGetterBound)
            {
                companionGetterBound = true;
                companionGetter = AccessTools.PropertyGetter(typeof(XUiC_SCoreCompanion), "Companion");
                if (companionGetter == null) ScLog.Warn("SCore's XUiC_SCoreCompanion.Companion not found - its Dismiss button makes a member leave again (SCore update?)");
            }
            return companionGetter != null ? companionGetter.Invoke(row, null) as EntityAlive : null;
        }

        /// <summary>
        /// 3.4 (decision D5). SCore 3.4's companion window lists V4 hires - our members who follow - with Follow, Stay,
        /// Teleport and Dismiss. Dismiss runs SCore's own (ExecuteCMD "Dismiss" and buffOrderDismiss: Persist 0, Leader and
        /// Owner zeroed), which ScTicker reads as a member leaving the community. For a member it is SC's Go Home instead,
        /// the same as the spoken "dismiss" (Patch_VoiceDismiss) and the dialog's and community window's: ScCommunity
        /// .ReturnHome, which walks or sends them to their post, or says why not (on a run, no home yet, too far - bring them
        /// home first; the host's alone on a client). An outpost stand-in is refused. Every other companion keeps SCore's
        /// Dismiss; an enlisted enclave person's already sends them home (ScEnclaveDeals reads the side door).
        /// </summary>
        [HarmonyPatch]
        public static class Patch_CompanionDismiss
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                try
                {
                    if (CompanionDismissTarget() != null) return true;
                    ScLog.Err("XUiC_SCoreCompanion.BtnDismiss_Controller_OnPress not found (SCore update?) - its Dismiss button makes a member leave the community");
                }
                catch (Exception e) { ScLog.Once("vc-bind-companion-dismiss", e); }
                return false;
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return CompanionDismissTarget(); }

            [HarmonyPrefix]
            public static bool Prefix(XUiC_SCoreCompanion __instance)
            {
                bool ours = false;
                try
                {
                    EntityAlive npc = __instance != null ? CompanionOf(__instance) : null;
                    if (!IsCommunityNpc(npc)) return true;
                    ours = true;
                    EntityPlayerLocal lp = __instance.xui != null && __instance.xui.playerUI != null ? __instance.xui.playerUI.entityPlayer : null;
                    Member m = ScRegistry.Loaded ? ScRegistry.GetMember(npc.entityId) : null;
                    if (m == null)
                    {
                        Count("companion-dismiss-refused");
                        if (lp != null) ScCommunity.Tell(lp, ScCommunity.IsHost ? Localization.Get("scVoiceStays") : Localization.Get("scHostOnly"), "ui_denied");
                        return false;
                    }
                    StripContract(npc);
                    string error = null;
                    if (lp != null && ScCommunity.ReturnHome(m, ScNpc.As(npc) ?? npc, lp, out error))
                    {
                        Count("companion-dismiss-to-home");
                        ScCommunity.Tell(lp, string.Format(Localization.Get("scStationed"), m.Name), "");
                        if (__instance.ViewComponent != null) __instance.ViewComponent.IsVisible = false;    // as SCore's own button hides the row
                    }
                    else
                    {
                        Count("companion-dismiss-refused");
                        if (lp != null) ScCommunity.Tell(lp, Localization.Get(error ?? "scInternal"), "ui_denied");
                    }
                    return false;
                }
                catch (Exception e)
                {
                    ScLog.Once("vc-companion-dismiss", e);
                    return !ours;       // never fall through to SCore's Dismiss for a member
                }
            }
        }
#endif

        /// <summary>The spoken "pick up": refused for members (an NPC carried as an item would vanish from the roster).</summary>
        [HarmonyPatch]
        public static class Patch_VoicePickup
        {
            [HarmonyPrepare]
            public static bool Prepare() { return PickupTarget() != null; }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return PickupTarget(); }

            [HarmonyPrefix]
            public static bool Prefix(object[] __args, ref string __result)
            {
                try
                {
                    EntityAlive npc = __args[0] as EntityAlive;
                    // one of an enclave's people (E2): they stay with their group; E13a: so does one of a band on the road
                    if (ScEnclavePeople.IsEnclaveNpc(npc) || ScBands.IsBandNpc(npc))
                    {
                        Count("pickup-refused-enclave");
                        __result = Localization.Get("scEncVoiceStays");
                        return false;
                    }
                    if (!IsCommunityNpc(npc)) return true;
                    Count("pickup-refused");
                    __result = Localization.Get("scVoiceWalk");
                    return false;
                }
                catch (Exception e)
                {
                    ScLog.Once("vc-pickup", e);
                    __result = Localization.Get("scVoiceWalk");
                    return false;
                }
            }
        }

        /// <summary>The spoken "hire": refused for members and stand-ins (no fee, no SCore hire behind the community's back),
        /// and by a survivor hostile to the player (ScTrust: the talk prompt is refused, this would be a side door).</summary>
        [HarmonyPatch]
        public static class Patch_VoiceHire
        {
            [HarmonyPrepare]
            public static bool Prepare() { return HireTarget() != null; }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return HireTarget(); }

            [HarmonyPrefix]
            public static bool Prefix(object[] __args, ref string __result)
            {
                try
                {
                    EntityAlive npc = __args[0] as EntityAlive;
                    if (ScTrust.IsHostileTo(npc, __args[1] as EntityPlayer))
                    {
                        Count("hire-refused-hostile");
                        __result = Localization.Get("scTrustNoHire");
                        return false;
                    }
                    // one of an enclave's people (E2): not for hire - E3's enlisting goes through their dialog, SC taking the fee;
                    // E13a: nor one of a band on the road (a body never saved: they come as a band, "Come to our place.")
                    if (ScEnclavePeople.IsEnclaveNpc(npc) || ScBands.IsBandNpc(npc))
                    {
                        Count("hire-refused-enclave");
                        __result = Localization.Get("scEncVoiceNoHire");
                        return false;
                    }
                    if (!IsCommunityNpc(npc)) return true;
                    Count("hire-refused");
                    __result = Localization.Get("scVoiceNoHire");
                    return false;
                }
                catch (Exception e)
                {
                    ScLog.Once("vc-hire", e);
                    __result = Localization.Get("scVoiceNoHire");
                    return false;
                }
            }
        }
#if SC_V34

        // ------------------------------------------------------------------ B1 (round 28): no "Pick me up" on SC's people ----
        //
        // 0-XNPCCore / Voice Control 3.4.04 put "Pick me up" (vcpickup) on the hold-E ring and on her "Jobs and gear" page, both
        // calling NpcPickup.TryPickup directly - past Patch_VoicePickup above (the spoken phrase only) and past the dialog's
        // hidden Pickup row. TryPickup ends in SCore's EntitySyncUtils.Collect, which turns the body into an item and removes
        // the entity: a member, a stand-in or an enlisted enclave person would vanish while SC's registry still lists them.
        // Three layers: SCore's Collect refuses them first of all (any caller, any machine, Priority.First); Voice Control's
        // TryPickup answers Refused for them with SC's line (its ring then says nothing of its own); the ring and the page never
        // offer the entry for them (UpdateActivationCommands after Voice Control's own postfix; HerRingPage.Open's list).

        /// <summary>
        /// Round 28 (B1): who is never picked up as an item - a member or a stand-in (IsCommunityNpc), one of an enclave's people
        /// (enlisted with the player too) or of a band on the road. The Localization key of what they say; null for anyone else.
        /// </summary>
        internal static string PickupRefusal(EntityAlive e)
        {
            if (e == null || e.Buffs == null) return null;
            if (IsCommunityNpc(e)) return "scVoiceWalk";
            if (ScEnclavePeople.IsEnclaveNpc(e) || ScBands.IsBandNpc(e)) return "scEncVoiceStays";
            return null;
        }

        private static void TellPickupRefused(EntityPlayer player, EntityAlive npc, string key)
        {
            if (player == null || npc == null) return;
            ScCommunity.Tell(player, string.Format(Localization.Get("scEncCallSays"), ScCommunity.DisplayName(npc), Localization.Get(key)), "ui_denied");
        }

        /// <summary>The hold-E ring's pick-up entry (HoldERingTable.Entries: "vcpickup", gate Pack).</summary>
        private const string RingPickupId = "vcpickup";
        private static int ringEntryHidden;

        public static Type NpcPickupType { get { return ScTypes.Find("XNPCVoiceControl.Core.NpcPickup"); } }
        public static Type HerRingType { get { return ScTypes.Find("XNPCVoiceControl.Harmony.HerRingPage"); } }
        /// <summary>Voice Control 3.4.04's hold-E ring (its table): the ring patches are expected only with it.</summary>
        public static bool HasHoldERing { get { return ScTypes.Find("XNPCVoiceControl.Core.Pure.HoldERingTable") != null; } }

        /// <summary>SCore's EntitySyncUtils.Collect(int entityId, int playerId), public static void.</summary>
        public static MethodBase CollectTarget()
        {
            return Returning(AccessTools.DeclaredMethod(typeof(EntitySyncUtils), "Collect", new[] { typeof(int), typeof(int) }), typeof(void), true);
        }

        /// <summary>NpcPickup.TryPickup(EntityAlive npc, EntityPlayer player), internal static, returning its internal enum PickupResult.</summary>
        public static MethodBase TryPickupTarget()
        {
            Type t = NpcPickupType;
            MethodInfo m = t != null ? AccessTools.DeclaredMethod(t, "TryPickup", new[] { typeof(EntityAlive), typeof(EntityPlayer) }) : null;
            return m != null && m.IsStatic && m.ReturnType.IsEnum ? m : null;
        }

        /// <summary>vanilla's Entity.UpdateActivationCommands(EntityPlayerLocal), public bool (Voice Control's ring entries are enabled in its postfix).</summary>
        public static MethodBase ActivationTarget()
        {
            return Returning(AccessTools.DeclaredMethod(typeof(Entity), "UpdateActivationCommands", new[] { typeof(EntityPlayerLocal) }), typeof(bool), false);
        }

        /// <summary>HerRingPage.Open(int playerEntityId, int npcEntityId), public void on an internal class ("Jobs and gear"'s page).</summary>
        public static MethodBase HerRingOpenTarget()
        {
            Type t = HerRingType;
            return t != null ? Returning(AccessTools.DeclaredMethod(t, "Open", new[] { typeof(int), typeof(int) }), typeof(void), false) : null;
        }

        /// <summary>B1, first layer: SCore's Collect - whoever calls it, on any machine (a client's request reaches the server's too).</summary>
        [HarmonyPatch]
        public static class Patch_NoCollect
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                try
                {
                    if (CollectTarget() != null) return true;
                    ScLog.Err("pick-up guard: SCore's EntitySyncUtils.Collect(int, int) not found (SCore update?) - a member could be picked up as an item from the hold-E ring");
                }
                catch (Exception e) { ScLog.Once("vc-bind-collect", e); }
                return false;
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return CollectTarget(); }

            /// <summary>__0 the NPC's entity id, __1 the player's.</summary>
            [HarmonyPrefix]
            [HarmonyPriority(Priority.First)]
            public static bool Prefix(int __0, int __1)
            {
                try
                {
                    World w = ScCommunity.World;
                    EntityAlive npc = w != null ? w.GetEntity(__0) as EntityAlive : null;
                    string key = PickupRefusal(npc);
                    if (key == null) return true;
                    Count("collect-refused");
                    TellPickupRefused(w.GetEntity(__1) as EntityPlayer, npc, key);
                    ScLog.Out("pick-up refused: " + ScCommunity.DisplayName(npc) + " (" + npc.entityId + ") is " + (key == "scVoiceWalk" ? "one of the community" : "one of an enclave or a band")
                              + " - SCore's Collect skipped");
                    return false;
                }
                catch (Exception e)
                {
                    ScLog.Once("vc-collect", e);
                    return true;
                }
            }
        }

        /// <summary>B1, second layer: Voice Control's pick-up (the ring, the page) answers Refused for SC's people, with SC's line.</summary>
        [HarmonyPatch]
        public static class Patch_RingPickup
        {
            /// <summary>PickupResult.Refused, boxed (the enum is internal: read by name in Prepare).</summary>
            private static object refused;

            [HarmonyPrepare]
            public static bool Prepare()
            {
                try
                {
                    MethodInfo m = TryPickupTarget() as MethodInfo;
                    if (m == null)
                    {
                        if (ChatType != null && HasHoldERing) ScLog.Err("pick-up guard: Voice Control's NpcPickup.TryPickup(EntityAlive, EntityPlayer) not found (Voice Control update?) - SCore's Collect still refuses SC's people, silently");
                        return false;
                    }
                    refused = Enum.Parse(m.ReturnType, "Refused");
                    return true;
                }
                catch (Exception e)
                {
                    ScLog.Once("vc-bind-trypickup", e);
                    return false;
                }
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return TryPickupTarget(); }

            /// <summary>__0 the NPC, __1 the player; the result by ref as object (a value-type return: Harmony boxes and unboxes it).</summary>
            [HarmonyPrefix]
            [HarmonyPriority(Priority.First)]
            public static bool Prefix(EntityAlive __0, EntityPlayer __1, ref object __result)
            {
                try
                {
                    string key = PickupRefusal(__0);
                    if (key == null) return true;
                    __result = refused;
                    Count("ring-pickup-refused");
                    TellPickupRefused(__1, __0, key);
                    ScLog.Out("pick-up refused: " + ScCommunity.DisplayName(__0) + " (" + __0.entityId + ") - Voice Control's Pick me up answered Refused");
                    return false;
                }
                catch (Exception e)
                {
                    ScLog.Once("vc-trypickup", e);
                    return true;
                }
            }
        }

        /// <summary>
        /// B1, third layer: the hold-E ring (vanilla's activation radial; Voice Control enables its entries in its own postfix on
        /// UpdateActivationCommands) shows no "Pick me up" on SC's people - this postfix runs after Voice Control's (Priority.Last)
        /// and disables that one entry, the call's answer (any entry enabled) recomputed.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_RingNoPickupEntry
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                try
                {
                    if (!HasHoldERing) return false;            // Voice Control before 3.4.04: no ring entry to hide
                    if (ActivationTarget() != null) return true;
                    ScLog.Err("pick-up guard: Entity.UpdateActivationCommands(EntityPlayerLocal) not found (game update?) - the hold-E ring lists Pick me up on SC's people (picking it is refused)");
                }
                catch (Exception e) { ScLog.Once("vc-bind-activation", e); }
                return false;
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return ActivationTarget(); }

            [HarmonyPostfix]
            [HarmonyPriority(Priority.Last)]
            public static void Postfix(Entity __instance, ref bool __result)
            {
                try
                {
                    EntityTrader npc = __instance as EntityTrader;
                    if (npc == null || PickupRefusal(npc) == null) return;
                    EntityActivationCommand[] cmds = __instance.GetActivationCommands();
                    if (cmds == null) return;
                    bool changed = false, any = false;
                    for (int i = 0; i < cmds.Length; i++)
                    {
                        if (cmds[i].enabled && string.Equals(cmds[i].commandId, RingPickupId, StringComparison.Ordinal))
                        {
                            EntityActivationCommand c = cmds[i];
                            c.enabled = false;
                            cmds[i] = c;
                            changed = true;
                        }
                        any |= cmds[i].enabled;
                    }
                    if (!changed) return;
                    __result = any;
                    ringEntryHidden++;
                }
                catch (Exception e)
                {
                    ScLog.Once("vc-ring-entry", e);
                }
            }
        }

        /// <summary>B1: "Jobs and gear"'s page (HerRingPage, the order wheel locked on her) lists no "Pick me up" for SC's people.</summary>
        [HarmonyPatch]
        public static class Patch_HerRingNoPickup
        {
            private static FieldInfo fSockets, fEntries, fCommandId;

            [HarmonyPrepare]
            public static bool Prepare()
            {
                try
                {
                    Type t = HerRingType;
                    if (HerRingOpenTarget() == null)
                    {
                        if (ChatType != null && HasHoldERing) ScLog.Err("pick-up guard: Voice Control's HerRingPage.Open(int, int) not found (Voice Control update?) - Jobs and gear lists Pick me up on SC's people (picking it is refused)");
                        return false;
                    }
                    fSockets = AccessTools.Field(t, "_sockets");
                    fEntries = AccessTools.Field(t, "_entries");
                    Type entry = fEntries != null && fEntries.FieldType.IsArray ? fEntries.FieldType.GetElementType() : null;
                    fCommandId = entry != null ? AccessTools.Field(entry, "CommandId") : null;
                    if (fSockets != null && fSockets.FieldType.IsArray && fCommandId != null && fCommandId.FieldType == typeof(string)) return true;
                    ScLog.Err("pick-up guard: HerRingPage's _sockets / _entries / HoldERingEntry.CommandId not found (Voice Control update?) - Jobs and gear lists Pick me up on SC's people (picking it is refused)");
                }
                catch (Exception e) { ScLog.Once("vc-bind-herring", e); }
                return false;
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return HerRingOpenTarget(); }

            /// <summary>__1 the NPC's entity id; the two parallel arrays lose the entry together (Commit indexes both alike).</summary>
            [HarmonyPostfix]
            public static void Postfix(object __instance, int __1)
            {
                try
                {
                    World w = ScCommunity.World;
                    EntityAlive npc = w != null ? w.GetEntity(__1) as EntityAlive : null;
                    if (__instance == null || PickupRefusal(npc) == null) return;
                    Array entries = fEntries.GetValue(__instance) as Array, sockets = fSockets.GetValue(__instance) as Array;
                    if (entries == null || sockets == null || entries.Length != sockets.Length) return;
                    var keep = new List<int>(entries.Length);
                    for (int i = 0; i < entries.Length; i++)
                        if (!string.Equals(fCommandId.GetValue(entries.GetValue(i)) as string, RingPickupId, StringComparison.Ordinal)) keep.Add(i);
                    if (keep.Count == entries.Length) return;
                    Array e2 = Array.CreateInstance(entries.GetType().GetElementType(), keep.Count);
                    Array s2 = Array.CreateInstance(sockets.GetType().GetElementType(), keep.Count);
                    for (int k = 0; k < keep.Count; k++)
                    {
                        e2.SetValue(entries.GetValue(keep[k]), k);
                        s2.SetValue(sockets.GetValue(keep[k]), k);
                    }
                    fEntries.SetValue(__instance, e2);
                    fSockets.SetValue(__instance, s2);
                    Count("ring-page-pickup-hidden");
                }
                catch (Exception e)
                {
                    ScLog.Once("vc-herring", e);
                }
            }
        }

        // ------------------------------------------------------------------ MayEngage (3.4) ----

        /// <summary>3.4: Voice Control's engage consideration (UAI.UAIConsiderationMayEngage.GetScore(Context, object)), or null.</summary>
        public static MethodBase MayEngageTarget()
        {
            Type t = ScTypes.Find("UAI.UAIConsiderationMayEngage");
            MethodInfo m = t != null ? AccessTools.DeclaredMethod(t, "GetScore", new[] { typeof(UAI.Context), typeof(object) }) : null;
            return m != null && m.ReturnType == typeof(float) ? m : null;
        }

        /// <summary>
        /// SC's people who fight at will, as every leaderless NPC did on 3.2: a raider; a community member or stand-in with no
        /// leader or owner (a resident at their post, a garrison stand-in); one of an enclave's people with none; E13a: one of a
        /// band on the road (drifters fight off what comes at them on the way, the trouble's zombies first of all). The leader
        /// test is 3.2 MayEngage's own (SCore's GetLeaderOrOwner).
        /// </summary>
        internal static bool EngagesAtWill(EntityAlive e)
        {
            if (e == null || e.Buffs == null) return false;
            if (ScXnpc.IsRaider(e)) return true;
            if (!IsCommunityNpc(e) && !ScEnclavePeople.IsEnclaveNpc(e) && !ScBands.IsBandNpc(e)) return false;
            // C1: a resident on a work shift has an owner (the player) only for 0-XNPCCore's crafting - still a resident in a fight
            return EntityUtilities.GetLeaderOrOwner(e.entityId) == null || ScWork.OnShift(e.entityId);
        }

        /// <summary>
        /// 3.4: SC's leaderless people fire at range again. Every attack and approach action of 0-XNPCCore's
        /// SdcsNpcRangedAdvanced carries MayEngage. On 3.2 it passed any NPC without a leader (return 1); 3.4 sends a
        /// leaderless one through its "wild" rule (EngageRules.ShouldEngageWild: its revenge target, an enemy within 5 m, an
        /// enemy targeting it within 15 m, or a 10 s commitment within 20 m) - so SC's raiders stopped shooting at players at
        /// range and residents and enclave people held fire until an intruder was 5 m away or hit them, which SC's base
        /// defence and enclave sieges need. For those three (EngagesAtWill) a refusal (0) becomes 3.2's 1; a pass keeps
        /// Voice Control's own score (StickScore, 0..1: a little under 1 for an enemy that is not the current target, so the
        /// NPC sticks to the one it is fighting - turning that into 1 made it switch targets). Everyone else - a follower, a
        /// stranger, 0-XNPCCore's own NPCs - keeps 3.4's rules, the original still runs first (its commitment and log
        /// bookkeeping), and nothing else of XNPC's package changes.
        ///
        /// A postfix rather than an SC copy of SdcsNpcRangedAdvanced without MayEngage: residents and enclave people carry
        /// XNPC's packages through npcSdcsBase (decision D1), so a copy would mean a second package list per body and an
        /// XML fork of XNPC's actions to keep up with each release; the postfix touches the one gate. Bound by name (no
        /// reference to Voice Control's DLL), off without it (Prepare), listed in ModApi's ExpectedTargets.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_MayEngage
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                try
                {
                    if (MayEngageTarget() != null) return true;
                    if (ChatType != null)
                        ScLog.Err("UAIConsiderationMayEngage.GetScore(Context, object) not found (Voice Control update?) - raiders, residents and enclave people hold fire at range");
                }
                catch (Exception e) { ScLog.Once("vc-bind-mayengage", e); }
                return false;
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return MayEngageTarget(); }

            [HarmonyPostfix]
            public static void Postfix(UAI.Context _context, ref float __result)
            {
                try
                {
                    if (__result > 0f) return;                   // Voice Control let it fight: its stick score stands
                    EntityAlive self = _context != null ? _context.Self : null;
                    if (!EngagesAtWill(self)) return;
                    __result = 1f;
                    Count("mayengage-pass");
                }
                catch (Exception e)
                {
                    ScLog.Once("vc-mayengage", e);
                }
            }
        }
#endif

        // ------------------------------------------------------------------ V1: the voice gate ----
        //
        // While SC's talk is open on one of the community's survivors, or SC's line still plays on them, Voice Control keeps
        // quiet on that NPC (ScVoice.HoldsVoice, MuteVcInTalk; Port33\research\voices\REPORT.md 1.5 B). Every one of its
        // utterances and its queued sentences is refused at the sinks it reaches - with the call's own callback invoked, so
        // its speech queue drains, onAllDone fires, s_proactiveGreetingBusy and _isWaitingForResponse clear as after a played
        // line. SC never calls Voice Control's StopSpeaking (it drops the queue without onAllDone and can wedge the greeting
        // flag for every NPC). The prefixes, all bound by name with the exact 3.4 / 3.2 signatures (identical but for
        // AcknowledgeCommand; X2: Voice Control 3.4.03 appended a parameter to four of them, and each target tries the 3.4.03
        // shape first, then the one before - FirstReturning), off by Prepare without Voice Control or on a changed signature
        // (ModApi then logs MISSING PATCH):
        //  - NPCAudioPlayer.OnWavBytesReady(byte[], VcTrace, Action, bool), private: every TTS sentence ends here, queued or
        //    prefetched before the talk opened included;
        //  - NPCAudioPlayer.PlayClipFile(ClipEntry, Action): clip personalities - SC's own call (ScVoice.SelfCall) passes;
        //  - NPCAudioPlayer.SpeakStreaming(string, bool, Action, VcTrace, bool): Voice Control's every new TTS line; it starts
        //    with StopSpeaking whenever the player is playing, which would cut SC's clip before the sentence even reached
        //    OnWavBytesReady (NPCAudioPlayer.cs:933-936), so it is refused at the door;
        //  - NetPackageVCVoiceRelay.PlayVoiceLocally(int, byte[], float), private static (NetPackageNPCVoice before Voice Control
        //    3.4.02; both names are tried): another machine's relayed TTS (MP);
        //  - NPCChatComponent.PlayProactiveGreeting(string, string, bool) and SubtitleManager.AcknowledgeCommand: no VC HUD
        //    subtitle over SC's line panel. Skipping either leaves Voice Control's state exactly as before the call: the
        //    greeting sets s_proactiveGreetingBusy only inside, together with the callback that clears it, never touches
        //    _isWaitingForResponse, and has no subscribers to its speech events; AcknowledgeCommand is static and stateless.
        // The answer pass (the player speaking to that NPC through Voice Control during the talk): a prefix on
        // NPCChatComponent.ProcessPlayerMessage (push-to-talk, typed chat) and, 3.4 only, DialogActionSayCarrying.PerformAction
        // (the dialog's "What are you carrying?" row, answered with a say-line). Not IsWaitingForResponse: Voice Control
        // clears it before the answer reaches the sinks (on the first streamed chunk, and in HandleLLMResponse before its
        // SpeakStreaming), so it would let the first words through and swallow the rest.

        public static Type AudioPlayerType { get { return ScTypes.Find("XNPCVoiceControl.TTS.NPCAudioPlayer"); } }
        private static Type TraceType { get { return ScTypes.Find("XNPCVoiceControl.Core.Pure.VcTrace"); } }
        private static Type ClipEntryType { get { return ScTypes.Find("XNPCVoiceControl.Core.ClipEntry"); } }
        private static Type PhraseTriggerType { get { return ScTypes.Find("XNPCVoiceControl.PhraseTrigger"); } }

        private static MethodBase Returning(MethodInfo m, Type ret, bool isStatic)
        {
            return m != null && m.ReturnType == ret && m.IsStatic == isStatic ? m : null;
        }

        /// <summary>
        /// X2: the first of several exact signatures that exists. Voice Control 3.4.03 appended a parameter to five of the
        /// methods SC hooks here (isReply, cacheEligible, lineId, paid); the newest shape goes first, the one before it after.
        /// Every prefix takes its parameters by name, and each of those names is in both shapes.
        /// </summary>
        private static MethodBase FirstReturning(Type t, string name, Type ret, bool isStatic, params Type[][] shapes)
        {
            foreach (Type[] shape in shapes)
            {
                MethodBase m = Returning(AccessTools.DeclaredMethod(t, name, shape), ret, isStatic);
                if (m != null) return m;
            }
            return null;
        }

        /// <summary>
        /// NPCAudioPlayer.OnWavBytesReady(byte[] wavBytes, VcTrace e2e, Action onComplete, bool isAck, bool isReply), private;
        /// no isReply before Voice Control 3.4.03.
        /// </summary>
        public static MethodBase WavReadyTarget()
        {
            Type t = AudioPlayerType, tr = TraceType;
            if (t == null || tr == null) return null;
            return FirstReturning(t, "OnWavBytesReady", typeof(void), false,
                new[] { typeof(byte[]), tr, typeof(Action), typeof(bool), typeof(bool) },
                new[] { typeof(byte[]), tr, typeof(Action), typeof(bool) });
        }

        /// <summary>NPCAudioPlayer.PlayClipFile(ClipEntry entry, Action onComplete).</summary>
        public static MethodBase PlayClipFileTarget()
        {
            Type t = AudioPlayerType, ce = ClipEntryType;
            if (t == null || ce == null) return null;
            return Returning(AccessTools.DeclaredMethod(t, "PlayClipFile", new[] { ce, typeof(Action) }), typeof(void), false);
        }

        /// <summary>
        /// NPCAudioPlayer.SpeakStreaming(string text, bool cacheEligible, Action onComplete, VcTrace e2e, bool isAck,
        /// bool isReply); no isReply before Voice Control 3.4.03.
        /// </summary>
        public static MethodBase SpeakStreamingTarget()
        {
            Type t = AudioPlayerType, tr = TraceType;
            if (t == null || tr == null) return null;
            return FirstReturning(t, "SpeakStreaming", typeof(void), false,
                new[] { typeof(string), typeof(bool), typeof(Action), tr, typeof(bool), typeof(bool) },
                new[] { typeof(string), typeof(bool), typeof(Action), tr, typeof(bool) });
        }

        /// <summary>
        /// The relay's PlayVoiceLocally(int npcEntityId, byte[] wavData, float volume), private static: on
        /// NetPackageVCVoiceRelay since Voice Control 3.4.02 (the class was renamed; method and parameters the same), on
        /// NetPackageNPCVoice before it. The new name first.
        /// </summary>
        public static MethodBase RelayTarget()
        {
            foreach (string name in RelayTypes)
            {
                Type t = ScTypes.Find(name);
                if (t == null) continue;
                MethodBase m = Returning(AccessTools.DeclaredMethod(t, "PlayVoiceLocally", new[] { typeof(int), typeof(byte[]), typeof(float) }), typeof(void), true);
                if (m != null) return m;
            }
            return null;
        }

        /// <summary>The relay package's class: Voice Control 3.4.02's name, then the one before it.</summary>
        private static readonly string[] RelayTypes = { "XNPCVoiceControl.NetPackageVCVoiceRelay", "XNPCVoiceControl.NetPackageNPCVoice" };

        /// <summary>
        /// NPCChatComponent.PlayProactiveGreeting(string greeting, string localizedSubtitle, bool showSubtitle, bool
        /// cacheEligible); no cacheEligible before Voice Control 3.4.03.
        /// </summary>
        public static MethodBase GreetingTarget()
        {
            Type t = ChatType;
            if (t == null) return null;
            return FirstReturning(t, "PlayProactiveGreeting", typeof(void), false,
                new[] { typeof(string), typeof(string), typeof(bool), typeof(bool) },
                new[] { typeof(string), typeof(string), typeof(bool) });
        }

        /// <summary>
        /// SubtitleManager.AcknowledgeCommand, static: Voice Control 3.4.03 (EntityAlive npc, PhraseTrigger trigger, string
        /// fallbackText, VcTrace e2e, string localizedSubtitle, string lineId); 3.4.01/02 have no lineId; 3.2 (Voice Control
        /// 3.2.06) has no localizedSubtitle either. Patch_ScAck reads localizedSubtitle at __args[4], the same in both 3.4 shapes.
        /// </summary>
        public static MethodBase AckTarget()
        {
            Type t = ScTypes.Find("XNPCVoiceControl.UI.SubtitleManager"), pt = PhraseTriggerType, tr = TraceType;
            if (t == null || pt == null || tr == null) return null;
#if SC_V34
            return FirstReturning(t, "AcknowledgeCommand", typeof(void), true,
                new[] { typeof(EntityAlive), pt, typeof(string), tr, typeof(string), typeof(string) },
                new[] { typeof(EntityAlive), pt, typeof(string), tr, typeof(string) });
#else
            Type[] args = { typeof(EntityAlive), pt, typeof(string), tr };
            return Returning(AccessTools.DeclaredMethod(t, "AcknowledgeCommand", args), typeof(void), true);
#endif
        }

        /// <summary>NPCChatComponent.ProcessPlayerMessage(string playerMessage, EntityPlayer player, bool usedName, Action&lt;string&gt; onComplete, VcTrace e2e).</summary>
        public static MethodBase PlayerMessageTarget()
        {
            Type t = ChatType, tr = TraceType;
            if (t == null || tr == null) return null;
            return Returning(AccessTools.DeclaredMethod(t, "ProcessPlayerMessage", new[] { typeof(string), typeof(EntityPlayer), typeof(bool), typeof(Action<string>), tr }), typeof(void), false);
        }

        /// <summary>The gate can bind: the target, every parameter the prefix takes by name, and the _npcEntity field it reads.</summary>
        private static bool Bindable(MethodBase m, string label, Type npcFieldOn, params string[] paramNames)
        {
            try
            {
                if (m == null)
                {
                    if (ChatType != null) ScLog.Err("voice gate: " + label + " not found (Voice Control update?) - Voice Control can talk over SC's line there");
                    return false;
                }
                ParameterInfo[] ps = m.GetParameters();
                foreach (string name in paramNames)
                {
                    bool found = false;
                    foreach (ParameterInfo p in ps) if (p.Name == name) { found = true; break; }
                    if (!found)
                    {
                        ScLog.Err("voice gate: " + label + " has no parameter '" + name + "' (Voice Control update?) - not patched");
                        return false;
                    }
                }
                if (npcFieldOn != null)
                {
                    FieldInfo f = AccessTools.Field(npcFieldOn, "_npcEntity");
                    if (f == null || f.FieldType != typeof(EntityAlive))
                    {
                        ScLog.Err("voice gate: " + npcFieldOn.Name + "._npcEntity not found (Voice Control update?) - " + label + " not patched");
                        return false;
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                ScLog.Once("vc-gate-bind-" + label, e);
                return false;
            }
        }

        /// <summary>The gate's common body: true runs Voice Control's call; false swallows it, its callback invoked first.</summary>
        private static bool Pass(EntityAlive npc, string sink, string what, Action callback)
        {
            bool swallow;
            try { swallow = ScVoice.Swallow(npc, sink, what); }
            catch (Exception e)
            {
                ScLog.Once("vc-gate-" + sink, e);
                return true;
            }
            if (!swallow) return true;
            // the call still completes: Voice Control's queue moves on, its greeting-busy flag and waiting state clear
            try { if (callback != null) callback(); }
            catch (Exception e) { ScLog.Once("vc-gate-callback-" + sink, e); }
            return false;
        }

        [HarmonyPatch]
        public static class Patch_GateWavReady
        {
            [HarmonyPrepare]
            public static bool Prepare() { return Bindable(WavReadyTarget(), "NPCAudioPlayer.OnWavBytesReady", AudioPlayerType, "onComplete"); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return WavReadyTarget(); }

            [HarmonyPrefix]
            public static bool Prefix(EntityAlive ____npcEntity, Action onComplete)
            {
                return Pass(____npcEntity, "OnWavBytesReady", "a TTS sentence", onComplete);
            }
        }

        [HarmonyPatch]
        public static class Patch_GatePlayClipFile
        {
            [HarmonyPrepare]
            public static bool Prepare() { return Bindable(PlayClipFileTarget(), "NPCAudioPlayer.PlayClipFile", AudioPlayerType, "onComplete"); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return PlayClipFileTarget(); }

            [HarmonyPrefix]
            public static bool Prefix(EntityAlive ____npcEntity, Action onComplete)
            {
                if (ScVoice.SelfCall) return true;      // SC's own line
                return Pass(____npcEntity, "PlayClipFile", "a voice clip", onComplete);
            }
        }

        [HarmonyPatch]
        public static class Patch_GateSpeakStreaming
        {
            [HarmonyPrepare]
            public static bool Prepare() { return Bindable(SpeakStreamingTarget(), "NPCAudioPlayer.SpeakStreaming", AudioPlayerType, "onComplete", "isAck"); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return SpeakStreamingTarget(); }

            [HarmonyPrefix]
            public static bool Prefix(EntityAlive ____npcEntity, Action onComplete, bool isAck)
            {
                return Pass(____npcEntity, "SpeakStreaming", isAck ? "a spoken ack" : "a TTS line", onComplete);
            }
        }

        [HarmonyPatch]
        public static class Patch_GateRelay
        {
            [HarmonyPrepare]
            public static bool Prepare() { return Bindable(RelayTarget(), "NetPackageVCVoiceRelay.PlayVoiceLocally (NetPackageNPCVoice before 3.4.02)", null, "npcEntityId"); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return RelayTarget(); }

            [HarmonyPrefix]
            public static bool Prefix(int npcEntityId)
            {
                try
                {
                    if (!ScVoice.MayHold(npcEntityId)) return true;
                    World w = GameManager.Instance != null ? GameManager.Instance.World : null;
                    EntityAlive npc = w != null ? w.GetEntity(npcEntityId) as EntityAlive : null;
                    return !ScVoice.Swallow(npc, "PlayVoiceLocally", "a relayed TTS sentence");
                }
                catch (Exception e)
                {
                    ScLog.Once("vc-gate-relay", e);
                    return true;
                }
            }
        }

        [HarmonyPatch]
        public static class Patch_GateGreeting
        {
            [HarmonyPrepare]
            public static bool Prepare() { return HasNpcField() && Bindable(GreetingTarget(), "NPCChatComponent.PlayProactiveGreeting", null, "showSubtitle"); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return GreetingTarget(); }

            /// <summary>Skipped whole: no HUD subtitle, no synthesis. State as before the call (see the section's note).</summary>
            [HarmonyPrefix]
            public static bool Prefix(EntityAlive ____npcEntity, bool showSubtitle)
            {
                return Pass(____npcEntity, "PlayProactiveGreeting", showSubtitle ? "a greeting" : "an NPC-to-NPC line", null);
            }
        }

        [HarmonyPatch]
        public static class Patch_GateAck
        {
            [HarmonyPrepare]
            public static bool Prepare() { return Bindable(AckTarget(), "SubtitleManager.AcknowledgeCommand", null, "npc"); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return AckTarget(); }

            /// <summary>Say-lines (ammo.dry and the rest), order acks and refusals: skipped whole, HUD subtitle and speech.</summary>
            [HarmonyPrefix]
            public static bool Prefix(EntityAlive npc)
            {
                return Pass(npc, "AcknowledgeCommand", "an ack or say-line", null);
            }
        }

        /// <summary>The answer pass: the player spoke to this NPC through Voice Control (push-to-talk, typed chat).</summary>
        [HarmonyPatch]
        public static class Patch_GatePlayerMessage
        {
            [HarmonyPrepare]
            public static bool Prepare() { return HasNpcField() && Bindable(PlayerMessageTarget(), "NPCChatComponent.ProcessPlayerMessage", null, "playerMessage", "player"); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return PlayerMessageTarget(); }

            [HarmonyPrefix]
            public static void Prefix(EntityAlive ____npcEntity, string playerMessage, EntityPlayer player)
            {
                try
                {
                    if (player == null || string.IsNullOrEmpty(playerMessage) || playerMessage.Trim().Length == 0) return;
                    ScVoice.PlayerSpoke(____npcEntity, "the player spoke to them");
                }
                catch (Exception e)
                {
                    ScLog.Once("vc-gate-message", e);
                }
            }
        }
#if SC_V34

        /// <summary>
        /// 3.4: DialogActionSayCarrying.PerformAction(EntityPlayer player) - 0-XNPCCore's "What are you carrying?" row (action
        /// type SayCarrying, 1-XNPCVoiceControl; Voice Control 3.4 only), answered with a say-line through AcknowledgeCommand.
        /// Global namespace; only Voice Control's own type counts.
        /// </summary>
        public static MethodBase SayCarryingTarget()
        {
            Type chat = ChatType, t = ScTypes.Find("DialogActionSayCarrying");
            if (chat == null || t == null || t.Assembly != chat.Assembly) return null;
            return Returning(AccessTools.DeclaredMethod(t, "PerformAction", new[] { typeof(EntityPlayer) }), typeof(void), false);
        }

        /// <summary>The answer pass, 3.4: the player picked "What are you carrying?" in the open talk.</summary>
        [HarmonyPatch]
        public static class Patch_GateSayCarrying
        {
            [HarmonyPrepare]
            public static bool Prepare() { return Bindable(SayCarryingTarget(), "DialogActionSayCarrying.PerformAction", null); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return SayCarryingTarget(); }

            [HarmonyPrefix]
            public static void Prefix()
            {
                try { ScVoice.PlayerAskedInTalk("the player asked what they carry"); }
                catch (Exception e) { ScLog.Once("vc-gate-saycarrying", e); }
            }
        }
#endif

        // ------------------------------------------------------------------ V2: SC's greetings, XNPCCore's lines in SC's voices ----
        //
        // research\voices\REPORT.md 2.2 and 4 (b). Three prefixes, bound by name with the exact 3.4 signatures (Prepare false
        // without Voice Control or on a changed signature; ModApi then logs MISSING PATCH), each try/catch + ScLog.Once
        // letting Voice Control run on any error:
        //  - NPCChatComponent.TriggerProactiveGreeting(EntityPlayer player), public instance void (VC34 NPCChatComponent.cs
        //    :1296): skipped for one of the community's survivors SC greets itself (ScGreetings.OwnsGreeting: their voice has
        //    a clip of their greeting). Skipping it whole leaves Voice Control's state as if its chance roll had failed (its
        //    cooldown, contact and LLM call all live inside), so it greets them again once SC's clip is gone.
        //  - SubtitleManager.AcknowledgeCommand (V1's AckTarget): say-lines, wheel and voice orders, refusals in SC's voice
        //    (ScBarks.SubstituteAck); skipped whole when SC plays its clip, as V1's gate does - it is static and stateless.
        //  - NPCAudioPlayer.SpeakStreaming (V1's SpeakStreamingTarget): Voice Control's fixed lines and the chat path's acks
        //    by their words (ScBarks.SubstituteSpeech); SC's clip gets Voice Control's onComplete, so its queue and waiting
        //    state end as after its own sentence.
        // The two shared with V1's gate run after it (Priority.Low; every prefix runs, Harmony ANDs their answers) and leave a
        // call the gate swallowed alone (__runOriginal false). Neither substitutes while Voice Control is saying something on
        // that NPC: SpeakStreaming would stop its own queue first, which SC never does (REPORT 1.5). EnqueueSpeech is not
        // hooked: in Voice Control 3.4 it carries only the LLM's streamed chunks (NPCChatComponent.cs:131, :606), never a
        // fixed line.

        /// <summary>NPCChatComponent.TriggerProactiveGreeting(EntityPlayer player), public instance void.</summary>
        public static MethodBase TriggerGreetingTarget()
        {
            Type t = ChatType;
            if (t == null) return null;
            return Returning(AccessTools.DeclaredMethod(t, "TriggerProactiveGreeting", new[] { typeof(EntityPlayer) }), typeof(void), false);
        }

        /// <summary>V2: Voice Control's walk-up greeting is skipped where SC greets the survivor itself.</summary>
        [HarmonyPatch]
        public static class Patch_ScGreetsInstead
        {
            [HarmonyPrepare]
            public static bool Prepare() { return HasNpcField() && Bindable(TriggerGreetingTarget(), "NPCChatComponent.TriggerProactiveGreeting", null, "player"); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return TriggerGreetingTarget(); }

            [HarmonyPrefix]
            public static bool Prefix(EntityAlive ____npcEntity, EntityPlayer player)
            {
                try
                {
                    if (!ScGreetings.OwnsGreeting(____npcEntity, player)) return true;
                    Count("greeting-sc-instead");
                    return false;
                }
                catch (Exception e)
                {
                    ScLog.Once("vc-sc-greets", e);
                    return true;
                }
            }
        }

        /// <summary>V2: a say-line, order ack or refusal SC has recorded in that survivor's voice plays SC's clip instead.</summary>
        [HarmonyPatch]
        public static class Patch_ScAck
        {
            [HarmonyPrepare]
            public static bool Prepare() { return Bindable(AckTarget(), "SubtitleManager.AcknowledgeCommand", null, "npc", "trigger", "fallbackText"); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return AckTarget(); }

            /// <summary>__args: npc, trigger, fallbackText, e2e, localizedSubtitle (3.4).</summary>
            [HarmonyPrefix]
            [HarmonyPriority(Priority.Low)]
            public static bool Prefix(object[] __args, bool __runOriginal)
            {
                if (!__runOriginal) return true;           // V1's gate swallowed it
                try
                {
                    string sub = __args.Length > 4 ? __args[4] as string : null;
                    return !ScBarks.SubstituteAck(__args[0] as EntityAlive, __args[1], __args[2] as string, sub);
                }
                catch (Exception e)
                {
                    ScLog.Once("vc-sc-ack", e);
                    return true;
                }
            }
        }

        /// <summary>V2: a fixed line or chat-path ack SC has recorded in that survivor's voice plays SC's clip instead, with Voice Control's onComplete.</summary>
        [HarmonyPatch]
        public static class Patch_ScSpeech
        {
            [HarmonyPrepare]
            public static bool Prepare() { return Bindable(SpeakStreamingTarget(), "NPCAudioPlayer.SpeakStreaming", AudioPlayerType, "text", "onComplete", "isAck"); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return SpeakStreamingTarget(); }

            [HarmonyPrefix]
            [HarmonyPriority(Priority.Low)]
            public static bool Prefix(EntityAlive ____npcEntity, string text, Action onComplete, bool isAck, bool __runOriginal)
            {
                if (!__runOriginal) return true;           // V1's gate swallowed it (and called onComplete)
                try
                {
                    return !ScBarks.SubstituteSpeech(____npcEntity, text, onComplete, isAck);
                }
                catch (Exception e)
                {
                    ScLog.Once("vc-sc-speech", e);
                    return true;
                }
            }
        }

        // ------------------------------------------------------------------ E8: voice chat context ----
        //
        // Voice Control's LLM chat knows the enclave an NPC belongs to (research\ideas\DESIGN.md section 11; ScVoiceContext).
        // Its system prompt is VC's cached BuildSystemPrompt (built at Initialize, so it could not carry a standing that
        // changes), its action-tag list, then per message the RAG memories, the sense block and the player's name, and goes
        // out in LLMService.SendStreamingChatRequest - called only from NPCChatComponent's chat coroutine on the main thread
        // (3.4 NPCChatComponent.cs:588), which builds the request body synchronously before its Task.Run (LLMService.cs:517).
        // So a prefix there, with the NPC's entity id (argument 0) and the prompt (argument 1, by ref), adds SC's paragraph to
        // that one request. Injected by index, the parameters' names don't matter. VoiceEnclaveContext switches it off.
        // The spoken trade: PhraseTriggerHandler.OpenTraderInventory (private), Voice Control's "trade" phrase - also reached
        // from its OpenInventory phrase on a trader - is let through its leaderless gate (PhraseTriggerHandler.cs:1051) and
        // on 3.4 opens 0-XNPCCore's stock trade for any unhired body with trader id 64 (:432), which every survivor body has
        // on 3.4, without asking AllowActivationCommand - even in a fight: it is one of the commands Voice Control lets
        // through while a leaderless NPC is in combat (NPCChatComponent.IsCombatPassableCommand). So the talk prompt's
        // refusal (X9) does not reach it. A prefix refuses it for the NPCs whose talk prompt is refused
        // (ScVoiceContext.TradeRefusal) with a spoken line, set as the subtitle too (ActionSubtitleOverride: ExecuteMatch
        // shows the trigger's own reply - "Take a look at my wares." - otherwise).
        // Both bound by name with the exact signatures (3.4 and 3.2 alike), off by Prepare without Voice Control or on a
        // changed signature (ModApi then logs MISSING PATCH).

        public static Type LlmServiceType { get { return ScTypes.Find("XNPCVoiceControl.LLMService"); } }
        private static Type ChatMessageType { get { return ScTypes.Find("XNPCVoiceControl.ChatMessage"); } }

        /// <summary>
        /// LLMService.SendStreamingChatRequest(int npcId, string systemPrompt, List&lt;ChatMessage&gt; conversationHistory, string
        /// playerMessage, CancellationToken interruptToken, Action&lt;string&gt; onChunk, Action&lt;string&gt; onComplete,
        /// Action&lt;string&gt; onError, VcTrace e2e = null), public instance void.
        /// </summary>
        public static MethodBase ChatRequestTarget()
        {
            Type t = LlmServiceType, cm = ChatMessageType, tr = TraceType;
            if (t == null || cm == null || tr == null) return null;
            Type[] args = { typeof(int), typeof(string), typeof(List<>).MakeGenericType(cm), typeof(string), typeof(System.Threading.CancellationToken),
                            typeof(Action<string>), typeof(Action<string>), typeof(Action<string>), tr };
            return Returning(AccessTools.DeclaredMethod(t, "SendStreamingChatRequest", args), typeof(void), false);
        }

        /// <summary>PhraseTriggerHandler.OpenTraderInventory(EntityAlive npc, EntityPlayer player, string npcName, string dialogText), private, string.</summary>
        public static MethodBase TradeTarget()
        {
            Type t = TriggerType;
            return t != null ? Returning(AccessTools.DeclaredMethod(t, "OpenTraderInventory", OrderArgs), typeof(string), false) : null;
        }
#if SC_V34

        /// <summary>PhraseTriggerHandler.OpenInventoryRouted(EntityAlive, EntityPlayer, string, string), private string: the inventory phrase.</summary>
        public static MethodBase InventoryRoutedTarget()
        {
            Type t = TriggerType;
            return t != null ? Returning(AccessTools.DeclaredMethod(t, "OpenInventoryRouted", OrderArgs), typeof(string), false) : null;
        }

        /// <summary>Above 0 while Voice Control's inventory phrase runs: on a trader body it redirects to OpenTraderInventory (round 28).</summary>
        private static int inventoryAsked;

        /// <summary>
        /// Round 28 (the Trade bug): Voice Control's "show me your inventory" phrase on a trader body - every survivor body -
        /// goes through OpenTraderInventory too (OpenInventoryRouted's redirect), so the trade prefix below must know which phrase
        /// it is: this marks the inventory phrase while it runs (a finalizer: the mark goes on any exit).
        /// </summary>
        [HarmonyPatch]
        public static class Patch_VoiceInventoryAsked
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                try
                {
                    if (InventoryRoutedTarget() != null) return true;
                    if (ChatType != null) ScLog.Warn("voice trade: PhraseTriggerHandler.OpenInventoryRouted not found (Voice Control update?) - \"show me your inventory\" by voice is refused on members like the trade");
                }
                catch (Exception e) { ScLog.Once("vc-bind-inventory", e); }
                return false;
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return InventoryRoutedTarget(); }

            [HarmonyPrefix]
            public static void Prefix() { inventoryAsked++; }

            [HarmonyFinalizer]
            public static void Finalizer() { if (inventoryAsked > 0) inventoryAsked--; }
        }

        /// <summary>
        /// Round 28 (the Trade bug, findings_x3): a member or a stand-in never trades by voice, in any state. Voice Control opens
        /// 0-XNPCCore's stock trade (selling her pack) for a trader-64 body with no player leader - a resident at home, a runner,
        /// a stand-in - and her inventory for one with a leader. The trade phrase is refused with a friendly line
        /// (scVoiceNoTradeMember); the inventory phrase (redirected here on her trader body) opens her SCore inventory - the
        /// talk's "Show me your inventory" - for a member who is not out on a run, and "not now" (scVoiceNoPackNow) for a runner
        /// or a stand-in, as the talk offers it to neither. False: the original is skipped.
        /// </summary>
        private static bool MemberTrade(object handler, EntityAlive npc, EntityPlayer player, string dialogText, ref string result)
        {
            string who = ScCommunity.DisplayName(npc) + " (" + npc.entityId + ")";
            if (inventoryAsked > 0)
            {
                bool away = npc.Buffs.HasCustomVar(ScRunners.OnRunCvar) || !npc.Buffs.HasCustomVar("sc_member");
                if (!away && player != null && EntityUtilities.ExecuteCMD(npc.entityId, "OpenInventory", player))
                {
                    result = dialogText ?? "*opens inventory*";
                    Count("voice-inventory-member");
                    return false;
                }
                string later = Localization.Get("scVoiceNoPackNow");
                result = later;
                SetActionSubtitle(handler, later);
                Count("voice-inventory-member-refused");
                ScLog.Out("voice inventory refused: " + who + " - " + (away ? "a runner or a stand-in" : "SCore's OpenInventory failed"));
                return false;
            }
            string line = Localization.Get("scVoiceNoTradeMember");
            result = line;
            SetActionSubtitle(handler, line);
            Count("voice-trade-refused-member");
            ScLog.Out("voice trade refused: " + who + " is one of the community - members and stand-ins never trade");
            return false;
        }
#endif

        /// <summary>The handler's subtitle for the action just run (ExecuteMatch shows it instead of the trigger's own reply).</summary>
        private static FieldInfo subtitleField;
        private static bool subtitleFieldBound;

        private static void SetActionSubtitle(object handler, string text)
        {
            if (!subtitleFieldBound)
            {
                subtitleFieldBound = true;
                Type t = TriggerType;
                FieldInfo f = t != null ? AccessTools.Field(t, "ActionSubtitleOverride") : null;
                subtitleField = f != null && f.FieldType == typeof(string) && !f.IsStatic ? f : null;
                if (subtitleField == null) ScLog.Warn("voice trade: PhraseTriggerHandler.ActionSubtitleOverride not found (Voice Control update?) - a refused trade shows Voice Control's own reply as its subtitle");
            }
            if (subtitleField != null && handler != null) subtitleField.SetValue(handler, text);
        }

        /// <summary>E8: every LLM chat request with one of an enclave's people carries what they know of their group.</summary>
        [HarmonyPatch]
        public static class Patch_VoiceEnclaveContext
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                try
                {
                    MethodBase m = ChatRequestTarget();
                    if (m == null)
                    {
                        if (ChatType != null) ScLog.Err("voice context: LLMService.SendStreamingChatRequest not found (Voice Control update?) - Voice Control's chat does not know the enclaves");
                        return false;
                    }
                    ParameterInfo[] ps = m.GetParameters();
                    if (ps.Length < 2 || ps[0].ParameterType != typeof(int) || ps[1].ParameterType != typeof(string)) return false;
                    return true;
                }
                catch (Exception e)
                {
                    ScLog.Once("vc-bind-context", e);
                    return false;
                }
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return ChatRequestTarget(); }

            /// <summary>__0 the NPC's entity id, __1 the system prompt (by index: the names don't matter).</summary>
            [HarmonyPrefix]
            public static void Prefix(int __0, ref string __1)
            {
                try
                {
                    if (!ScConfig.VoiceEnclaveContext) return;
                    World w = ScCommunity.World;
                    EntityAlive npc = w != null ? w.GetEntity(__0) as EntityAlive : null;
                    string what;
                    string add = ScVoiceContext.For(npc, out what);
                    if (string.IsNullOrEmpty(add)) return;
                    __1 = (__1 ?? "") + "\n\n" + add;
                    Count(what != null && what.EndsWith(", hostile", StringComparison.Ordinal) ? "voice-context-hostile" : "voice-context-added");
                    ScLog.Verbose("voice context: " + ScCommunity.DisplayName(npc) + " (" + __0 + "), " + what + " - " + add.Length
                                  + " characters added to Voice Control's chat prompt");
                }
                catch (Exception e)
                {
                    ScLog.Once("vc-context", e);
                }
            }
        }

        /// <summary>E8: the spoken "trade" is refused by the NPCs whose talk prompt is refused (a hostile enclave's people above all).</summary>
        [HarmonyPatch]
        public static class Patch_VoiceTradeHostile
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                try
                {
                    if (TradeTarget() != null) return true;
                    if (ChatType != null) ScLog.Err("voice trade: PhraseTriggerHandler.OpenTraderInventory not found (Voice Control update?) - a hostile enclave's people can be traded with by voice");
                }
                catch (Exception e) { ScLog.Once("vc-bind-trade", e); }
                return false;
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return TradeTarget(); }

            [HarmonyPrefix]
            public static bool Prefix(object __instance, object[] __args, ref string __result)
            {
                try
                {
                    EntityAlive npc = __args[0] as EntityAlive;
#if SC_V34
                    // round 28 (the Trade bug): members and stand-ins, in any state - their own line, not the hostile one
                    if (IsCommunityNpc(npc)) return MemberTrade(__instance, npc, __args[1] as EntityPlayer, __args[3] as string, ref __result);
#endif
                    string why = ScVoiceContext.TradeRefusal(npc, __args[1] as EntityPlayer);
                    if (why == null) return true;
                    string line = Localization.Get("scVoiceNoTradeHostile");
                    __result = line;
                    SetActionSubtitle(__instance, line);
                    Count("voice-trade-refused");
                    ScLog.Out("voice trade refused: " + ScCommunity.DisplayName(npc) + " (" + npc.entityId + ") won't trade by voice - " + why);
                    return false;
                }
                catch (Exception e)
                {
                    ScLog.Once("vc-trade", e);
                    return true;
                }
            }
        }
    }
}
