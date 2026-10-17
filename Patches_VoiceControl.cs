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
                    if (!IsCommunityNpc(____npcEntity)) return true;
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
                    if (!IsCommunityNpc(____npcEntity)) return true;
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
                    // one of an enclave's people (E2): nobody's to dismiss - SCore's Dismiss would end their Guard at their spot
                    if (ScEnclavePeople.IsEnclaveNpc(npc))
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
                    // one of an enclave's people (E2): not for hire (E3 brings enlisting)
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
    }
}
