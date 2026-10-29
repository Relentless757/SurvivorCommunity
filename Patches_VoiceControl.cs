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
    ///  - no contract is ever seeded (MaybeStampHireDay skipped);
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
    ///
    /// Bound by name, with no reference to the DLL: without Voice Control every patch here skips itself (Prepare), and
    /// ModApi only expects them when NPCChatComponent exists, so a changed signature shows up as a MISSING PATCH line.
    /// </summary>
    public static class Patches_VoiceControl
    {
        public static readonly string[] ContractCvars =
            { "RetainerWeek", "RetainerNextDueDay", "RetainerGraceDay", "RetainerAwaitingApproval", "RetainerPromptDay", "RetainerEarnings" };

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
            if (counts.Count == 0) return "nothing intercepted yet";
            var keys = new List<string>(counts.Keys);
            keys.Sort(StringComparer.Ordinal);
            var parts = new List<string>();
            foreach (string k in keys) parts.Add(k + "=" + counts[k]);
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
                    // one of an enclave's people (E2): they stay with their group
                    if (ScEnclavePeople.IsEnclaveNpc(npc))
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
                    // one of an enclave's people (E2): not for hire - E3's enlisting goes through their dialog, SC taking the fee
                    if (ScEnclavePeople.IsEnclaveNpc(npc))
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
        /// leader or owner (a resident at their post, a garrison stand-in); one of an enclave's people with none. The leader
        /// test is 3.2 MayEngage's own (SCore's GetLeaderOrOwner).
        /// </summary>
        internal static bool EngagesAtWill(EntityAlive e)
        {
            if (e == null || e.Buffs == null) return false;
            if (ScXnpc.IsRaider(e)) return true;
            if (!IsCommunityNpc(e) && !ScEnclavePeople.IsEnclaveNpc(e)) return false;
            return EntityUtilities.GetLeaderOrOwner(e.entityId) == null;
        }

        /// <summary>
        /// 3.4: SC's leaderless people fire at range again. Every attack and approach action of 0-XNPCCore's
        /// SdcsNpcRangedAdvanced carries MayEngage. On 3.2 it passed any NPC without a leader (return 1); 3.4 sends a
        /// leaderless one through its "wild" rule (EngageRules.ShouldEngageWild: its revenge target, an enemy within 5 m, an
        /// enemy targeting it within 15 m, or a 10 s commitment within 20 m) - so SC's raiders stopped shooting at players at
        /// range and residents and enclave people held fire until an intruder was 5 m away or hit them, which SC's base
        /// defence and enclave sieges need. For those three (EngagesAtWill) the score is 3.2's 1 again; everyone else - a
        /// follower, a stranger, 0-XNPCCore's own NPCs - keeps 3.4's rules, the original still runs first (its commitment
        /// and log bookkeeping), and nothing else of XNPC's package changes.
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
                    if (__result == 1f) return;
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
    }
}
