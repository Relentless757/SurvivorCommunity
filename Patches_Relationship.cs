using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round U4 (2026-11-16, decisions D7, D8 and D9; research/npc306/part_relationship.md): 0-XNPCCore 3.4.06's relationships on
    /// SC's people, "SC owns it" (ScBond has the why of each event).
    ///
    ///  - Patch_Apply, RelationshipState.Apply (public static, 502 IL bytes): for a member, a stand-in or an enlisted enclave
    ///    person every 0-XNPCCore relationship write is skipped - her per-hire trust and affinity and the 25% ripple into the
    ///    player's global trust (D8). A gift (signal "gift", after its bag took the item) becomes Bond, its points 1:1. Never the
    ///    class-wide norelationship tag: it would opt out every paid hire of the survivor classes too.
    ///  - Patch_KilledRelationship + Patch_ApplyGlobal, NPCEventMemoryHooks.OnEntityKilledRelationship (private static, 335) and
    ///    RelationshipState.ApplyGlobal (public static, 164): a member's (or a stand-in's, or an enlisted person's) death costs the
    ///    player no global trust (D8: SC charges its own DeathPenalty). Only the "hire-died" write of that one call is skipped.
    ///  - Patch_Revived / Patch_Downed, NPCEventMemoryHooks.OnRevived (public static, 290) / OnDowned (public static, 95): Bond for
    ///    every revive and down of a member, whoever revives her (0-XNPCCore's downed state calls them through its AppDomain
    ///    bridge for every body; its own relationship then asks for her leader as the reviver).
    ///  - Patch_TryEffective, RelationshipState.TryEffective (public static, 174): the Progress page's "Trust: x  Feels: y" for a
    ///    member (or her stand-in) comes from SC - Feels from her mood, Trust from her Bond (ScBond.Mirror).
    ///  - Patch_SettleWeek, NPCChatComponent.TrySettleWeekFromGift (internal instance, 234): coins handed to one of SC's people never
    ///    pay a week (research 2.4: a contract left on her could take the coins past SC's billing guard). NotDue, nothing taken.
    ///  - Patch_GiftCoins, SdcsGiftBag.TryAddGift (public static, 128; NPCSdcsCharacters): coins are refused for a member, a
    ///    stand-in or an enlisted enclave person (D9: members are never paid; an enlisted one goes home with her bag, the Fable
    ///    review's finding 6) with 0-XNPCCore's own refusal, "I've no use for that."
    ///  - Patch_Standing, ConsoleCmdLLMChat.RunStandingCommand (private instance, 33): Voice Control's "vc standing" on a member
    ///    shows SC's view instead of 0-XNPCCore's numbers, which no longer apply to her.
    /// Every target is above Mono's 20-byte inline limit. Bound by name with no reference to Voice Control's DLL: without it, or
    /// with a 0-XNPCCore older than 3.4.06, each patch turns itself off in Prepare; ModApi only expects them when they bind.
    /// </summary>
    public static class Patches_Relationship
    {
        private static readonly Dictionary<string, int> counts = new Dictionary<string, int>();
        /// <summary>Set while 0-XNPCCore's kill handler runs for the death of one of SC's people (Patch_KilledRelationship). Main thread.</summary>
        private static bool killedIsOurs;
        private static MethodInfo isActive, lookedAt, roster;
        private static bool standingBound;

        public static void Reset()
        {
            lock (counts) counts.Clear();
            killedIsOurs = false;
        }

        private static void Count(string what)
        {
            lock (counts)
            {
                int n;
                counts.TryGetValue(what, out n);
                counts[what] = n + 1;
            }
        }

        public static string Report()
        {
            lock (counts)
            {
                if (counts.Count == 0) return "nothing intercepted yet";
                var keys = new List<string>(counts.Keys);
                keys.Sort(StringComparer.Ordinal);
                var parts = new List<string>();
                foreach (string k in keys) parts.Add(k + "=" + counts[k]);
                return string.Join(" ", parts.ToArray());
            }
        }

        // ------------------------------------------------------------------ targets ----

        public static Type RelType { get { return ScTypes.Find("XNPCVoiceControl.Core.RelationshipState"); } }
        public static Type HooksType { get { return ScTypes.Find("XNPCVoiceControl.NPCEventMemoryHooks"); } }
        public static Type ConsoleType { get { return ScTypes.Find("XNPCVoiceControl.ConsoleCmdLLMChat"); } }
        public static Type GiftBagType { get { return ScTypes.Find("SdcsGiftBag"); } }

        private static readonly Type FloatRef = typeof(float).MakeByRefType();

        /// <summary>RelationshipState.Apply(EntityAlive npc, EntityPlayer player, float trustDelta, float affinityDelta, string signal).</summary>
        public static MethodBase ApplyTarget()
        {
            Type t = RelType;
            return t != null ? Returning(AccessTools.DeclaredMethod(t, "Apply", new[] { typeof(EntityAlive), typeof(EntityPlayer), typeof(float), typeof(float), typeof(string) }), typeof(void), true) : null;
        }

        /// <summary>RelationshipState.ApplyGlobal(EntityPlayer player, float trustDelta, string signal).</summary>
        public static MethodBase ApplyGlobalTarget()
        {
            Type t = RelType;
            return t != null ? Returning(AccessTools.DeclaredMethod(t, "ApplyGlobal", new[] { typeof(EntityPlayer), typeof(float), typeof(string) }), typeof(void), true) : null;
        }

        /// <summary>RelationshipState.TryEffective(EntityAlive npc, out float trustEffective, out float affinityEffective).</summary>
        public static MethodBase TryEffectiveTarget()
        {
            Type t = RelType;
            return t != null ? Returning(AccessTools.DeclaredMethod(t, "TryEffective", new[] { typeof(EntityAlive), FloatRef, FloatRef }), typeof(bool), true) : null;
        }

        /// <summary>NPCEventMemoryHooks.OnRevived(EntityAlive npc, string reviverName).</summary>
        public static MethodBase RevivedTarget()
        {
            Type t = HooksType;
            return t != null ? Returning(AccessTools.DeclaredMethod(t, "OnRevived", new[] { typeof(EntityAlive), typeof(string) }), typeof(void), true) : null;
        }

        /// <summary>NPCEventMemoryHooks.OnDowned(EntityAlive npc).</summary>
        public static MethodBase DownedTarget()
        {
            Type t = HooksType;
            return t != null ? Returning(AccessTools.DeclaredMethod(t, "OnDowned", new[] { typeof(EntityAlive) }), typeof(void), true) : null;
        }

        /// <summary>NPCEventMemoryHooks.OnEntityKilledRelationship(ModEvents.SEntityKilledData data), private.</summary>
        public static MethodBase KilledTarget()
        {
            Type t = HooksType;
            return t != null ? Returning(AccessTools.DeclaredMethod(t, "OnEntityKilledRelationship", new[] { typeof(ModEvents.SEntityKilledData) }), typeof(void), true) : null;
        }

        /// <summary>NPCChatComponent.TrySettleWeekFromGift(int coinsHeld, out int coinsUsed), internal, returning its WeekSettle enum.</summary>
        public static MethodBase SettleWeekTarget()
        {
            Type t = Patches_VoiceControl.ChatType;
            MethodInfo m = t != null ? AccessTools.DeclaredMethod(t, "TrySettleWeekFromGift", new[] { typeof(int), typeof(int).MakeByRefType() }) : null;
            return m != null && !m.IsStatic && m.ReturnType.IsEnum ? m : null;
        }

        /// <summary>ConsoleCmdLLMChat.RunStandingCommand(), private: Voice Control's "vc standing".</summary>
        public static MethodBase StandingTarget()
        {
            Type t = ConsoleType;
            return t != null ? Returning(AccessTools.DeclaredMethod(t, "RunStandingCommand", Type.EmptyTypes), typeof(void), false) : null;
        }

        /// <summary>SdcsGiftBag.TryAddGift(EntityAlive npc, ItemValue item, int count, IDictionary&lt;string, int&gt; giftItems, out string refusalKey).</summary>
        public static MethodBase GiftAddTarget()
        {
            Type t = GiftBagType;
            return t != null ? Returning(AccessTools.DeclaredMethod(t, "TryAddGift", new[] { typeof(EntityAlive), typeof(ItemValue), typeof(int),
                typeof(IDictionary<string, int>), typeof(string).MakeByRefType() }), typeof(int), true) : null;
        }

        private static MethodBase Returning(MethodInfo m, Type ret, bool isStatic)
        {
            return m != null && m.ReturnType == ret && m.IsStatic == isStatic ? m : null;
        }

        /// <summary>Prepare's test: the target bound, or one warning (only when the mod it belongs to is there).</summary>
        private static bool Bound(Func<MethodBase> target, string label, bool vc = true)
        {
            try
            {
                if (target() != null) return true;
                if (!vc || Patches_VoiceControl.ChatType != null)
                    ScLog.Warn("0-XNPCCore method " + label + " not found - that part of round U4 is off (0-XNPCCore older than 3.4.06, or an update?)");
            }
            catch (Exception e) { ScLog.Once("rel-bind-" + label, e); }
            return false;
        }

        /// <summary>One of SC's people for 0-XNPCCore's relationships: a member, a stand-in, or an enclave person enlisted with the player.</summary>
        private static bool Ours(EntityAlive e)
        {
            return e != null && (Patches_VoiceControl.IsCommunityNpc(e) || ScEnclaveDeals.IsEnlistedBody(e));
        }

        // ------------------------------------------------------------------ the patches ----

        /// <summary>Every 0-XNPCCore relationship write skipped for SC's people; a gift's points become her Bond.</summary>
        [HarmonyPatch]
        public static class Patch_Apply
        {
            [HarmonyPrepare]
            public static bool Prepare() { return Bound(ApplyTarget, "RelationshipState.Apply"); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return ApplyTarget(); }

            [HarmonyPrefix]
            public static bool Prefix(EntityAlive npc, float affinityDelta, string signal)
            {
                try
                {
                    if (!Ours(npc)) return true;
                    Count("apply-skipped-" + (signal ?? "?"));
                    if (signal == "gift")
                    {
                        // the bag took the item before this (VCCommandRouter.GiftCore); 0-XNPCCore's daily cap made the points
                        Member m = ScBond.MemberOf(npc);
                        int points = Mathf.RoundToInt(affinityDelta);
                        if (m != null) ScBond.Note(m.Id, points * ScConfig.BondGiftPerPoint, ScBond.SigGift, "a gift (" + points + " point(s) by 0-XNPCCore's count)");
                    }
                    // a down and a revive come through Patch_Downed / Patch_Revived (any reviver), a blood moon and a kill on her
                    // through ScBond's own checks (every member, not only those 0-XNPCCore sees); a paid week never happens for them
                    return false;
                }
                catch (Exception e)
                {
                    ScLog.Once("rel-apply", e);
                    return true;
                }
            }
        }

        /// <summary>D8: the death of one of SC's people - only that call's global write is skipped (Patch_ApplyGlobal reads the flag).</summary>
        [HarmonyPatch]
        public static class Patch_KilledRelationship
        {
            [HarmonyPrepare]
            public static bool Prepare() { return Bound(KilledTarget, "NPCEventMemoryHooks.OnEntityKilledRelationship") && Bound(ApplyGlobalTarget, "RelationshipState.ApplyGlobal"); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return KilledTarget(); }

            [HarmonyPrefix]
            public static void Prefix(ModEvents.SEntityKilledData data)
            {
                try { killedIsOurs = Ours(data.KilledEntitiy as EntityAlive); }
                catch (Exception e) { killedIsOurs = false; ScLog.Once("rel-killed", e); }
            }

            [HarmonyPostfix]
            public static void Postfix()
            {
                killedIsOurs = false;
            }
        }

        [HarmonyPatch]
        public static class Patch_ApplyGlobal
        {
            [HarmonyPrepare]
            public static bool Prepare() { return Bound(ApplyGlobalTarget, "RelationshipState.ApplyGlobal") && KilledTarget() != null; }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return ApplyGlobalTarget(); }

            [HarmonyPrefix]
            public static bool Prefix(string signal)
            {
                if (!killedIsOurs || signal != "hire-died") return true;
                Count("global-hire-died-skipped");
                ScLog.Verbose("one of the community died - 0-XNPCCore's global trust is left alone (D8; SC's DeathPenalty applies)");
                return false;
            }
        }

        /// <summary>Bond for every revive of a member, whoever revives her.</summary>
        [HarmonyPatch]
        public static class Patch_Revived
        {
            [HarmonyPrepare]
            public static bool Prepare() { return Bound(RevivedTarget, "NPCEventMemoryHooks.OnRevived"); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return RevivedTarget(); }

            [HarmonyPrefix]
            public static void Prefix(EntityAlive npc, string reviverName)
            {
                try
                {
                    Member m = ScBond.MemberOf(npc);
                    if (m == null) return;
                    Count("revived");
                    ScBond.Note(m.Id, ScConfig.BondRevive, ScBond.SigRevive, string.IsNullOrEmpty(reviverName) ? "revived" : "revived by " + reviverName);
                }
                catch (Exception e) { ScLog.Once("rel-revived", e); }
            }
        }

        /// <summary>Bond for every down of a member.</summary>
        [HarmonyPatch]
        public static class Patch_Downed
        {
            [HarmonyPrepare]
            public static bool Prepare() { return Bound(DownedTarget, "NPCEventMemoryHooks.OnDowned"); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return DownedTarget(); }

            [HarmonyPrefix]
            public static void Prefix(EntityAlive npc)
            {
                try
                {
                    Member m = ScBond.MemberOf(npc);
                    if (m == null) return;
                    Count("downed");
                    ScBond.Note(m.Id, ScConfig.BondDowned, ScBond.SigDown, "went down");
                }
                catch (Exception e) { ScLog.Once("rel-downed", e); }
            }
        }

        /// <summary>The Progress page's line for a member from SC (ScBond.Mirror), while 0-XNPCCore's relationships are on for her.</summary>
        [HarmonyPatch]
        public static class Patch_TryEffective
        {
            [HarmonyPrepare]
            public static bool Prepare() { return Bound(TryEffectiveTarget, "RelationshipState.TryEffective"); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return TryEffectiveTarget(); }

            [HarmonyPrefix]
            public static bool Prefix(EntityAlive npc, ref float trustEffective, ref float affinityEffective, ref bool __result)
            {
                try
                {
                    Member m = ScBond.MemberOf(npc);
                    if (m == null || !RelationshipsOn(npc)) return true;
                    float t, a;
                    ScBond.Mirror(m, out t, out a);
                    trustEffective = t;
                    affinityEffective = a;
                    __result = true;
                    Count("progress-mirrored");
                    return false;
                }
                catch (Exception e)
                {
                    ScLog.Once("rel-effective", e);
                    return true;
                }
            }
        }

        /// <summary>RelationshipState.IsActive (its config's Enabled and opt-out tag): off, 0-XNPCCore shows no line, and neither does SC.</summary>
        private static bool RelationshipsOn(EntityAlive npc)
        {
            if (isActive == null)
            {
                Type t = RelType;
                isActive = t != null ? AccessTools.DeclaredMethod(t, "IsActive", new[] { typeof(EntityAlive) }) : null;
                if (isActive == null || isActive.ReturnType != typeof(bool)) { isActive = null; return true; }
            }
            object r = isActive.Invoke(null, new object[] { npc });
            return !(r is bool) || (bool)r;
        }

        /// <summary>No week is ever settled from a gift to SC's people: NotDue (the enum's 0, Harmony's default result), no coins used.</summary>
        [HarmonyPatch]
        public static class Patch_SettleWeek
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                if (!Bound(SettleWeekTarget, "NPCChatComponent.TrySettleWeekFromGift")) return false;
                FieldInfo f = AccessTools.Field(Patches_VoiceControl.ChatType, "_npcEntity");
                return f != null && f.FieldType == typeof(EntityAlive);
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return SettleWeekTarget(); }

            [HarmonyPrefix]
            public static bool Prefix(EntityAlive ____npcEntity, ref int coinsUsed)
            {
                try
                {
                    if (!Ours(____npcEntity)) return true;
                    coinsUsed = 0;                      // no write here: Patches_VoiceControl's Patch_NoBilling strips a contract within 2 s
                    Count("gift-week-refused");
                    return false;
                }
                catch (Exception e)
                {
                    ScLog.Once("rel-settle", e);
                    return true;
                }
            }
        }

        /// <summary>D9: coins refused for SC's people (Ours), with 0-XNPCCore's own refusal (vcGiftCantUse: "I've no use for that.").</summary>
        [HarmonyPatch]
        public static class Patch_GiftCoins
        {
            [HarmonyPrepare]
            public static bool Prepare() { return Bound(GiftAddTarget, "SdcsGiftBag.TryAddGift", false); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return GiftAddTarget(); }

            [HarmonyPrefix]
            public static bool Prefix(EntityAlive npc, ItemValue item, int count, ref string refusalKey, ref int __result)
            {
                try
                {
                    if (!ScConfig.MemberRefuseCoins || npc == null || item == null || item.ItemClass == null) return true;
                    if (!Ours(npc) || !IsCurrency(item.ItemClass)) return true;   // finding 6: an enlisted enclave person too
                    refusalKey = "vcGiftCantUse";
                    __result = 0;
                    Count("gift-coins-refused");
                    ScLog.Out(ScCommunity.DisplayName(npc) + " refused " + count + " " + item.ItemClass.GetItemName() + " as a gift - SC's people are never paid that way (round U4)");
                    return false;
                }
                catch (Exception e)
                {
                    ScLog.Once("rel-gift-coins", e);
                    return true;
                }
            }

            /// <summary>0-XNPCCore's currency test (SdcsGiftBag.IsCurrency): the trader's currency item.</summary>
            private static bool IsCurrency(ItemClass ic)
            {
                ItemValue coin = ItemClass.GetItem(TraderInfo.CurrencyItem);
                return coin != null && !coin.IsEmpty() && coin.ItemClass == ic;
            }
        }

        /// <summary>"vc standing" on a member: SC's view (ScBond.Lines); anyone else keeps Voice Control's.</summary>
        [HarmonyPatch]
        public static class Patch_Standing
        {
            [HarmonyPrepare]
            public static bool Prepare() { return Bound(StandingTarget, "ConsoleCmdLLMChat.RunStandingCommand"); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return StandingTarget(); }

            [HarmonyPrefix]
            public static bool Prefix()
            {
                try { return !StandingForMember(); }
                catch (Exception e)
                {
                    ScLog.Once("rel-standing", e);
                    return true;
                }
            }
        }

        /// <summary>
        /// Voice Control's own pick - the hire you look at, else your nearest hire (its roster, which J29 keeps residents out of) -
        /// and, when that is one of SC's members (or a stand-in), SC's lines instead of its numbers. True when it answered.
        /// Main thread only (the F1 console); anything else is left to Voice Control.
        /// </summary>
        private static bool StandingForMember()
        {
            if (!ThreadManager.IsMainThread() || !ScRegistry.Loaded) return false;
            World w = GameManager.Instance != null ? GameManager.Instance.World : null;
            EntityPlayerLocal p = w != null ? w.GetPrimaryPlayer() : null;
            if (p == null) return false;
            if (!standingBound)
            {
                standingBound = true;
                Type t = ScJobs.VcModType;
                lookedAt = t != null ? AccessTools.DeclaredMethod(t, "GetLookedAtHiredNPC", new[] { typeof(EntityPlayer), typeof(float) }) : null;
                roster = t != null ? AccessTools.DeclaredMethod(t, "GetHiredRoster", new[] { typeof(EntityPlayer) }) : null;
            }
            if (lookedAt == null || roster == null) return false;
            string how = "looked";
            EntityAlive target = lookedAt.Invoke(null, new object[] { p, 30f }) as EntityAlive;
            if (target == null)
            {
                how = "nearest";
                var list = roster.Invoke(null, new object[] { p }) as List<EntityAlive>;
                float best = float.MaxValue;
                if (list != null)
                    foreach (EntityAlive e in list)
                    {
                        if (e == null || e.IsDead()) continue;
                        float d = Vector3.Distance(p.position, e.position);
                        if (d < best) { best = d; target = e; }
                    }
            }
            Member m = ScBond.MemberOf(target);
            if (m == null) return false;
            SdtdConsole con = SingletonMonoBehaviour<SdtdConsole>.Instance;
            foreach (string line in ScBond.Lines(m, target, "[VC-REL] "))
            {
                if (con != null) con.Output(line);
                Log.Out(line);
            }
            Count("standing-shown-sc (" + how + ")");
            return true;
        }
    }
}
