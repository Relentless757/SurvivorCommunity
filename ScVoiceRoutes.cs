using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round m1w (2026-11-17, research/voices_v9/VOICE_INVENTORY.md section 3.1, the user's decision "Build all of them"):
    /// Voice Control's speech routed into SC's own recorded voices - the two parts that are not ScBarks' own matching.
    ///
    /// P2, the order wheel. For almost every slot Voice Control's wheel speaks the slot's English label as her reply
    /// (XUiC_VCFormationRadial.CommitToNpc / TryCommitSquad, EmoteWheelPage.Commit: AcknowledgeCommand(npc, null, label)) -
    /// "Close", "Jump down", "Loot here", "Wave" - right after VCCommandRouter.Execute ran the order. Patch_OrderStamp, a
    /// prefix on that public Execute (bound by name and shape), notes each NPC's order and frame; when ScBarks.SubstituteAck
    /// then gets a trigger-less, id-less line, WheelKeys decides it first - before SC's word matching, which would take the
    /// label "Loser" for ack.emote.loser.1's words (the review of adee167, fix 7). It maps the order to the reply SC has already
    /// recorded for the same spoken order (FamilyFor: Follow is ack.followplayer, SetFollowDistance 1 ack.setfollowdistance.1,
    /// Loot here ack.loothere, RoleOn cook the new ack.roleon.cook ...), and the caption becomes SC's words. Three slots have
    /// no Execute or no fixed label and are known by their words instead: a weapon category (RadialOrderWheel.Weapons'
    /// labels) is ack.swapweapon, "Taking position: X" ack.setformation, "Heading to the X." the go-there replies. Come here's
    /// and the whistle's label is swallowed when that NPC said 0-XNPCCore's comehere.ack ("On my way.") since the order was
    /// noted (NoteLine), and "Whatever" (SilentEmotes) always; a gesture with no reply of SC's is Voice Control's to say, and
    /// every other emote on the page is a dance. A label SC has no recording for yet is Voice Control's to speak, as before;
    /// so is any label of a downed survivor, any with VoiceWheelAcks off, and any right after SC refused that order
    /// (SayRefusal marks it, Refused): a refused job never says "I'll clear this place out." (ScBarks.SubstituteSpeech
    /// swallows the spoken order's reply after a refusal too).
    /// Re-check fix 2: SC's reply is a yes, so only an order Voice Control carried out gets one. Patch_OrderRan notes the
    /// orders that reached VCCommandRouter.Dispatch (past Execute's gates: her leader, not downed, a job she has the AI
    /// package for, a fight that allows it) and Patch_OrderDropped the ones 0-XNPCCore's party cap refused there
    /// (SdcsParty.Refuse: Follow, Come here, the whistle, a formation ... with the party full); any other order's label is
    /// Voice Control's, as before. A label with no order noted (a weapon category) needs you as her leader.
    /// Re-check fix 6: ScBarks swallows a SpeakStreaming only when it is the reply to a phrase Voice Control just matched on
    /// that NPC (Patch_ReplyStamp, ExecuteMatch's own reply text, this frame) - never a billing line or any other line it
    /// says on its own.
    ///
    /// P7, SC's own refusals that were tooltips only (ScJobs.TellFieldJob and KitToolRefused, the talk's and the community
    /// window's "Come with me." with no room in the party): SayRefusal says them in her recorded voice with a caption when
    /// she is within 30 m of you - through ScBarks.SayOwn, which also starts the one-line rule (P6), so the order's own
    /// Voice Control reply right after is swallowed. The tooltip alone, as before, without a recording, and (review fix 3)
    /// while she is asleep or down, while Voice Control is saying something on her (its next sentence would cut SC's clip),
    /// or with ScBarks off.
    ///
    /// P1 (say-line ids), P3 (SC's own lines indexed), P5 (punctuation-blind words) and P6 (one line per NPC) are ScBarks';
    /// P4 (greetings) is ScGreetings'. VoiceWheelAcks and VoiceSpokenRefusals switch P2 and P7 off. Main thread only.
    /// </summary>
    public static class ScVoiceRoutes
    {
        /// <summary>P7: a refusal is said aloud only this close to you (as a runner talks in person: ScRadioTalk.InPersonRange).</summary>
        private const float SpokenRange = 30f;
        /// <summary>"Taking position: {0}" (Voice Control's formation_ack): the Point slot, which finds no trigger.</summary>
        private const string FormationPrefix = "Taking position: ";
        /// <summary>"Heading to the {0}." (Voice Control's go-there ack for a block it can name).</summary>
        private const string GoTherePrefix = "Heading to the ";

        private struct Order
        {
            public string Cmd, Arg;
            public int Frame;
            /// <summary>Review fix 5: 0-XNPCCore's comehere.ack ("On my way.") was said on this NPC since the order was noted.</summary>
            public bool ComeHere;
            /// <summary>Re-check fix 2: the order reached VCCommandRouter.Dispatch (Patch_OrderRan) - Execute's gates let it through.</summary>
            public bool Ran;
            /// <summary>Re-check fix 2: 0-XNPCCore's party cap refused it in Dispatch (Patch_OrderDropped) - it did not run.</summary>
            public bool Dropped;
        }

        /// <summary>The verdict on a trigger-less, id-less line (WheelKeys).</summary>
        internal enum Wheel
        {
            /// <summary>No order behind it, or a sentence: SC's word matching decides.</summary>
            NotLabel,
            /// <summary>A wheel label Voice Control says, as before.</summary>
            Leave,
            /// <summary>A wheel label that is no reply: swallowed.</summary>
            Silence,
            /// <summary>A wheel label SC answers with the order's reply (keys).</summary>
            Reply
        }

        /// <summary>Review fix 6: the gestures SC keeps silent on purpose (a shrug needs no word); any other gesture without a reply of SC's is Voice Control's.</summary>
        private static readonly HashSet<string> SilentEmotes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "whatever" };

        /// <summary>P2: each NPC's last order through VCCommandRouter.Execute (its VCCommand's name, its argument, the frame).</summary>
        private static readonly Dictionary<int, Order> orders = new Dictionary<int, Order>();
        /// <summary>P2/P7: the frame SC last refused an order of this NPC's, by entity id.</summary>
        private static readonly Dictionary<int, int> refusedFrame = new Dictionary<int, int>();
        private static readonly Regex NonAlnum = new Regex("[^A-Za-z0-9]+");
        private static int stamped, mapped, silenced, afterRefusal, left, notRun, refusalsSaid, refusalsUnrecorded, refusalsFar, refusalsInTalk, refusalsQuiet;
        /// <summary>Re-check fix 6: the NPC, frame and reply text of the phrase Voice Control last matched (Patch_ReplyStamp).</summary>
        private static int replyNpc = -1, replyFrame = -1;
        private static string replyText;
        private static readonly List<int> stale = new List<int>();
        /// <summary>The gestures already logged as having no reply of SC's (once each).</summary>
        private static readonly HashSet<string> loggedGestures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Voice Control, by name (no compile reference): the Weapons page's labels, the emote page's gestures
        private static bool vcBound;
        private static readonly HashSet<string> weaponLabels = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> gestureClips = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>0-XNPCCore 3.4.06's, used only when Voice Control's own tables cannot be read.</summary>
        private static readonly string[] FallbackWeaponLabels = { "Guns", "Rifles", "Shotguns", "Machine guns", "Melee", "Blades", "Empty hands" };
        private static readonly string[] FallbackGestures = { "wave", "salute", "fistbump", "stretch", "warmup", "whatever", "flipoff", "insult", "loser", "threaten" };

        /// <summary>Per world (ScBarks.Reset): no stamps, no refusals, counters at zero.</summary>
        public static void Reset()
        {
            orders.Clear();
            refusedFrame.Clear();
            stamped = mapped = silenced = afterRefusal = left = notRun = 0;
            replyNpc = replyFrame = -1;
            replyText = null;
            refusalsSaid = refusalsUnrecorded = refusalsFar = refusalsInTalk = refusalsQuiet = 0;
        }

        // ------------------------------------------------------------------ P2: the order stamp ----

        public static Type RouterType { get { return ScTypes.Find("XNPCVoiceControl.Net.VCCommandRouter"); } }

        /// <summary>
        /// VCCommandRouter.Execute(int playerEntityId, int npcEntityId, VCCommand cmd, string arg = ""), public static void -
        /// found by name and shape (two ints, Voice Control's command enum, a string), so the enum's own type name or values may
        /// change; any other shape (a parameter added, even an optional one) finds none, and that fails loud: Prepare warns and
        /// ModApi logs MISSING PATCH.
        /// </summary>
        public static MethodBase ExecuteTarget()
        {
            Type t = RouterType;
            if (t == null) return null;
            foreach (MethodInfo m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (m.Name != "Execute" || m.ReturnType != typeof(void)) continue;
                ParameterInfo[] ps = m.GetParameters();
                if (ps.Length == 4 && ps[0].ParameterType == typeof(int) && ps[1].ParameterType == typeof(int) && ps[2].ParameterType.IsEnum
                    && ps[3].ParameterType == typeof(string)) return m;
            }
            return null;
        }

        /// <summary>P2: every order Voice Control runs (the wheel, a squad, a voice order) notes its NPC, command and argument.</summary>
        [HarmonyPatch]
        public static class Patch_OrderStamp
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                try
                {
                    if (ExecuteTarget() != null) return true;
                    if (Patches_VoiceControl.ChatType != null)
                        ScLog.Warn("voice routes: Voice Control's VCCommandRouter.Execute(int, int, VCCommand, string) not found (Voice Control update?) - "
                                   + "the order wheel's replies stay Voice Control's spoken slot labels");
                }
                catch (Exception e) { ScLog.Once("voice-routes-bind-execute", e); }
                return false;
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return ExecuteTarget(); }

            /// <summary>__args: player id, NPC id, command (boxed enum), argument. Read only.</summary>
            [HarmonyPrefix]
            public static void Prefix(object[] __args)
            {
                try
                {
                    if (__args == null || __args.Length < 4 || !(__args[1] is int) || __args[2] == null) return;
                    if (orders.Count > 64) PruneOrders();       // review fix 8: never this frame's (a squad's notes)
                    orders[(int)__args[1]] = new Order { Cmd = __args[2].ToString(), Arg = __args[3] as string ?? "", Frame = Time.frameCount };
                    stamped++;
                }
                catch (Exception e)
                {
                    ScLog.Once("voice-routes-stamp", e);
                }
            }
        }

        /// <summary>
        /// Re-check fix 2: VCCommandRouter.Dispatch(EntityAlive npc, EntityPlayer player, string npcName, VCCommand cmd, string
        /// arg), private static void - where ExecuteAuthoritative hands an order on once every gate passed. Found by name and
        /// shape; any other shape finds none, and that fails loud (Prepare warns, ModApi logs MISSING PATCH) - every wheel label
        /// is then Voice Control's, as before.
        /// </summary>
        public static MethodBase DispatchTarget()
        {
            Type t = RouterType;
            if (t == null) return null;
            foreach (MethodInfo m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (m.Name != "Dispatch" || m.ReturnType != typeof(void)) continue;
                ParameterInfo[] ps = m.GetParameters();
                if (ps.Length == 5 && ps[0].ParameterType == typeof(EntityAlive) && ps[1].ParameterType == typeof(EntityPlayer)
                    && ps[2].ParameterType == typeof(string) && ps[3].ParameterType.IsEnum && ps[4].ParameterType == typeof(string)) return m;
            }
            return null;
        }

        /// <summary>
        /// Re-check fix 2: 0-XNPCCore 3.4.06's SdcsParty.Refuse(EntityAlive candidate, EntityPlayer leader, string path), public
        /// static - its "Your party is full" tooltip, after which Voice Control drops the order. By name and shape; null without
        /// SdcsParty.
        /// </summary>
        public static MethodBase PartyRefuseTarget()
        {
            Type t = ScTypes.Find("SdcsParty");
            if (t == null) return null;
            foreach (MethodInfo m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (m.Name != "Refuse") continue;
                ParameterInfo[] ps = m.GetParameters();
                if (ps.Length == 3 && ps[0].ParameterType == typeof(EntityAlive) && ps[1].ParameterType == typeof(EntityPlayer) && ps[2].ParameterType == typeof(string))
                    return m;
            }
            return null;
        }

        /// <summary>Re-check fix 6: PhraseTriggerHandler.ExecuteMatch (Patches_VoiceControl's binding), where a phrase's reply is made.</summary>
        public static MethodBase MatchTarget() { return Patches_VoiceControl.ExecuteMatchTarget(); }

        /// <summary>Re-check fix 2: this NPC's order noted this frame or the last reached Dispatch (ran) or was refused there (dropped).</summary>
        private static void MarkOrder(EntityAlive npc, bool ran, bool dropped)
        {
            Order o;
            if ((object)npc == null || !orders.TryGetValue(npc.entityId, out o) || Time.frameCount - o.Frame > 1) return;
            if (ran) o.Ran = true;
            if (dropped) o.Dropped = true;
            orders[npc.entityId] = o;
        }

        /// <summary>Re-check fix 2: an order that got past Execute's gates (Dispatch) is noted as run.</summary>
        [HarmonyPatch]
        public static class Patch_OrderRan
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                try
                {
                    if (DispatchTarget() != null) return true;
                    if (Patches_VoiceControl.ChatType != null)
                        ScLog.Warn("voice routes: Voice Control's VCCommandRouter.Dispatch(EntityAlive, EntityPlayer, string, VCCommand, string) not found (Voice Control "
                                   + "update?) - the order wheel's labels stay Voice Control's");
                }
                catch (Exception e) { ScLog.Once("voice-routes-bind-dispatch", e); }
                return false;
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return DispatchTarget(); }

            /// <summary>__0: the NPC. Read only.</summary>
            [HarmonyPrefix]
            public static void Prefix(EntityAlive __0)
            {
                try { MarkOrder(__0, true, false); }
                catch (Exception e) { ScLog.Once("voice-routes-ran", e); }
            }
        }

        /// <summary>Re-check fix 2: an order 0-XNPCCore's party cap refused (the party is full) is noted as dropped.</summary>
        [HarmonyPatch]
        public static class Patch_OrderDropped
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                try
                {
                    // the wheel is Voice Control's: without it ModApi does not list this target either
                    if (Patches_VoiceControl.ChatType == null) return false;
                    if (PartyRefuseTarget() != null) return true;
                    if (ScPartyCap.Available)
                        ScLog.Warn("voice routes: 0-XNPCCore's SdcsParty.Refuse(EntityAlive, EntityPlayer, string) not found (0-XNPCCore update?) - a wheel order "
                                   + "refused for a full party can still get SC's reply");
                }
                catch (Exception e) { ScLog.Once("voice-routes-bind-refuse", e); }
                return false;
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return PartyRefuseTarget(); }

            /// <summary>__0: the NPC refused. Read only.</summary>
            [HarmonyPostfix]
            public static void Postfix(EntityAlive __0)
            {
                try { MarkOrder(__0, false, true); }
                catch (Exception e) { ScLog.Once("voice-routes-dropped", e); }
            }
        }

        /// <summary>
        /// Re-check fix 6: the reply Voice Control makes for a phrase it matched on an NPC (ExecuteMatch's audioResponse, final:
        /// TryHandlePhrase returns it and ProcessPlayerMessage speaks it in the same call). A second patch on the target
        /// Patches_VoiceControl.Patch_VoiceFollowMember already lists, so no new ModApi entry; a postfix also sees SC's own
        /// answer when that prefix skipped the original.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_ReplyStamp
        {
            [HarmonyPrepare]
            public static bool Prepare() { return MatchTarget() != null; }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return MatchTarget(); }

            /// <summary>npc and audioResponse by name, as Patch_VoiceFollowMember binds them. Read only.</summary>
            [HarmonyPostfix]
            public static void Postfix(EntityAlive npc, ref string audioResponse)
            {
                try
                {
                    if ((object)npc == null) return;
                    replyNpc = npc.entityId;
                    replyFrame = Time.frameCount;
                    replyText = audioResponse;
                }
                catch (Exception e) { ScLog.Once("voice-routes-reply", e); }
            }
        }

        /// <summary>
        /// Re-check fix 6 (ScBarks.SubstituteSpeech): this speech on this NPC is the reply to the phrase Voice Control matched on
        /// her this frame - its very words. Never one of its billing lines or anything else it says on its own.
        /// </summary>
        internal static bool IsTriggerReply(EntityAlive npc, string text)
        {
            return (object)npc != null && replyNpc == npc.entityId && replyFrame == Time.frameCount && !string.IsNullOrEmpty(replyText)
                   && string.Equals(replyText, text, StringComparison.Ordinal);
        }

        // ------------------------------------------------------------------ P2: the wheel's label ----

        /// <summary>
        /// ScBarks.SubstituteAck, for a trigger-less, id-less line, before any word matching (review fix 7): is it a wheel label,
        /// and what becomes of it? NotLabel - no order noted on this NPC this frame or the last, or a sentence (a refusal or any
        /// other line Voice Control says after an order ends in one), and none of the three label rules: SC's words decide.
        /// Otherwise it is decided here: Leave (Voice Control says it, as before) with VoiceWheelAcks off, for a downed survivor
        /// (review fix 5: ExecuteAuthoritative may have dropped the order), right after SC refused that order, or with no reply
        /// SC has keys for; Silence; Reply with keys. family: what it was taken for (the log).
        /// </summary>
        internal static Wheel WheelKeys(EntityAlive npc, string text, out List<string> keys, out string family)
        {
            keys = null;
            family = null;
            if (npc == null || string.IsNullOrWhiteSpace(text)) return Wheel.NotLabel;
            BindVc();
            string t = text.Trim();
            Order o;
            bool noted = orders.TryGetValue(npc.entityId, out o) && Time.frameCount - o.Frame <= 1;
            // the slots with no Execute, or with words of their own, known by those words; any other label needs its order noted
            if (weaponLabels.Contains(t)) family = "ack.swapweapon";
            else if (t.StartsWith(FormationPrefix, StringComparison.Ordinal)) family = "ack.setformation";
            else if (t.StartsWith(GoTherePrefix, StringComparison.Ordinal) && t.EndsWith(".", StringComparison.Ordinal)) family = "vc.gothere";
            else if (!noted || t.Length > 48 || t.EndsWith(".", StringComparison.Ordinal) || t.EndsWith("!", StringComparison.Ordinal) || t.EndsWith("?", StringComparison.Ordinal))
                return Wheel.NotLabel;
            if (!ScConfig.VoiceWheelAcks) return Leave();
#if SC_V34
            if (ScDowned.IsDown(npc)) return Leave();
#endif
            if (Refused(npc))
            {
                // SC refused this order a moment ago (a field job for a resident, the work kit as a weapon): her label is no
                // yes - Voice Control says it as before, unless her refusal played (the one-line rule swallowed it already)
                afterRefusal++;
                return Wheel.Leave;
            }
            // re-check fix 2: SC's reply is a yes - only for an order Voice Control carried out. One noted must have reached its
            // Dispatch and not been refused there for a full party; a label with no order noted (a weapon category) needs you as
            // her leader, as Voice Control's own order does
            if (noted ? !o.Ran || o.Dropped : !LedByYou(npc))
            {
                notRun++;
                return Wheel.Leave;
            }
            if (family == null)
            {
                bool silence;
                family = FamilyFor(o, out silence);
                if (silence)
                {
                    silenced++;
                    return Wheel.Silence;
                }
                if (family == null) return Leave();
            }
            // ms/m1all (final review): a Squeamish member never keeps the medic role (round m1t takes it back within a second),
            // so her own recorded "I'll look after everyone." would be a yes she doesn't mean - Voice Control says the label
            if (family == "ack.medicon" && !ScTraitEffects.CanBeMedic(ScRegistry.GetMember(npc.entityId))) return Leave();
            keys = family == "vc.gothere" ? GoThereKeys() : ScBarks.KeysOf(family);
            if (keys.Count == 0) return Leave();
            mapped++;
            return Wheel.Reply;
        }

        /// <summary>Re-check fix 2: you are her leader (or owner) - Voice Control's own test (NpcRelation.IsLeaderOfEntity).</summary>
        private static bool LedByYou(EntityAlive npc)
        {
#if SC_V34
            EntityPlayerLocal lp = ScCommunity.LocalPlayer;
            if (lp == null) return false;
            Entity leader = SdcsNpcRelation.LeaderEntity(npc.entityId);
            return leader != null && leader.entityId == lp.entityId;
#else
            return true;
#endif
        }

        private static Wheel Leave()
        {
            left++;
            return Wheel.Leave;
        }

        /// <summary>
        /// Review fix 5 (ScBarks.SubstituteAck, every line it sees): 0-XNPCCore's comehere.ack on this NPC after its order was
        /// noted - only then is Come here's or the whistle's label swallowed (the recall ran; otherwise Voice Control says it).
        /// </summary>
        internal static void NoteLine(EntityAlive npc, string lineId)
        {
            if (npc == null || lineId != "comehere.ack") return;
            Order o;
            if (!orders.TryGetValue(npc.entityId, out o) || Time.frameCount - o.Frame > 1) return;
            o.ComeHere = true;
            orders[npc.entityId] = o;
        }

        /// <summary>Review fix 8: the notes older than last frame go; this frame's (a squad order's, one per NPC) stay.</summary>
        private static void PruneOrders()
        {
            int f = Time.frameCount;
            stale.Clear();
            foreach (KeyValuePair<int, Order> kv in orders) if (f - kv.Value.Frame > 1) stale.Add(kv.Key);
            foreach (int id in stale) orders.Remove(id);
        }

        private static void PruneRefused()
        {
            int f = Time.frameCount;
            stale.Clear();
            foreach (KeyValuePair<int, int> kv in refusedFrame) if (f - kv.Value > 1) stale.Add(kv.Key);
            foreach (int id in stale) refusedFrame.Remove(id);
        }

        /// <summary>
        /// The reply family of a wheel order (VCCommand's name, its argument) - the same family a spoken order of that kind
        /// says (ScBarks' ack.&lt;ActionType&gt;[.&lt;ActionParam&gt;]); null for none (Voice Control says the label); silence for
        /// a label that is no reply.
        /// </summary>
        private static string FamilyFor(Order o, out bool silence)
        {
            silence = false;
            string cmd = o.Cmd, a = (o.Arg ?? "").Trim().ToLowerInvariant();
            switch (cmd)
            {
                case "Follow": return "ack.followplayer";
                case "Stay": return "ack.stopfollow";
                case "Guard": return a == "return" ? "ack.guardreturn" : "ack.guardhere";
                case "ComeHere":
                case "Whistle":
                    // review fix 5: her comehere.ack ("On my way.") is the answer, and the label would cut it - only when she said
                    // it; a recall that did not run (a full party, nothing to recall) leaves the label to Voice Control
                    silence = o.ComeHere;
                    return null;
                case "SetEngage": return a == "4" ? null : "ack.setengage." + Param(a);   // round nc1: 4, hunting - see below
                case "SetCrouch": return "ack.setcrouch." + Param(a);
                case "Wander": return "ack.wander";
                case "SetPatrolPoint": return "ack.setpatrolpoint";
                case "Patrol": return "ack.patrol";
                case "CancelPatrolRecord": return "ack.cancelpatrolrecord";
                case "SetFollowDistance": return "ack.setfollowdistance." + Param(a);      // 2.5 is ack.setfollowdistance.2_5
                case "SetFormation": return "ack.setformation";                             // Point ("point,1") too
                case "MoveToPosition": return "vc.gothere";
                case "JumpDown": return "ack.jumpdown";
                case "Vehicle": return "ack.vehicle." + Param(a);
                case "Loot": return LootFamily(a);
                case "RememberBase": return "ack.rememberbase";
                case "MedicOn": return "ack.medicon";
                case "MedicOff": return "ack.medicoff";
                case "RoleOn": return "ack.roleon." + Param(a);
                case "RoleOff": return "ack.roleoff." + Param(a);
                // round nc1 (Voice Control 3.4.06 final's new wheel orders): the weapon toggle's label ("Melee -> Ranged", "Ranged ->
                // Melee") is a change of weapon - SC's recorded weapon-swap replies ("Switching." ...); its usual answer is a sentence
                // ("Going ranged."), which SC's word matching decides like any other. The quiet kill and the hunting stance have no
                // recording of SC's yet: Voice Control says its own line ("On it.", "Going quiet.", "Hunting"), as for any order SC
                // has not recorded
                case "SetWeaponPref": return "ack.swapweapon";
                case "StealthKill": return null;
                case "Emote": return EmoteFamily(a, out silence);
            }
            return null;
        }

        /// <summary>The Jobs, Harvest and Mine pages' Loot arguments (RadialOrderWheel.Actions / Harvest / Mine).</summary>
        private static string LootFamily(string a)
        {
            switch (a)
            {
                case "": return "ack.loot";
                case "here": return "ack.loothere";
                case "harvest": return "ack.harvesthere";
                case "plants":
                case "crops": return "ack.pickhere." + a;
                case "butcher": return "ack.butcherhere";
                case "mine": return "ack.minehere";
                case "clay": return "ack.digclayhere";
                case "baserepair":
                case "combatrepair":
                case "repair": return "ack." + a + "here";
            }
            if (a.StartsWith("mine-", StringComparison.Ordinal)) return "ack.minehere." + Param(a.Substring(5));
            return null;
        }

        /// <summary>
        /// An emote's reply: ack.emote.&lt;clip&gt;; SilentEmotes ("Whatever") are silence; review fix 6: any other gesture of
        /// Voice Control's (EmoteWheelSteps) with no reply of SC's is Voice Control's to say (logged once), and any other emote
        /// on the page is a dance.
        /// </summary>
        private static string EmoteFamily(string clip, out bool silence)
        {
            silence = false;
            if (clip.Length == 0) return null;
            string fam = "ack.emote." + Param(clip);
            if (ScBarks.KeysOf(fam).Count > 0) return fam;
            if (SilentEmotes.Contains(clip))
            {
                silence = true;
                return null;
            }
            BindVc();
            if (gestureClips.Contains(clip))
            {
                if (loggedGestures.Add(clip))
                    ScLog.Out("voice routes: the gesture '" + clip + "' has no reply of SC's (ack.emote." + clip + ") - Voice Control says its label");
                return null;
            }
            return "ack.emote.dance";
        }

        private static string Param(string a)
        {
            return NonAlnum.Replace(a ?? "", "_");
        }

        /// <summary>The go-there replies: "Heading over there." and "On my way." (vc.gothere.floor, vc.gothere.go).</summary>
        private static List<string> GoThereKeys()
        {
            var l = new List<string>(ScBarks.KeysOf("vc.gothere.floor"));
            l.AddRange(ScBarks.KeysOf("vc.gothere.go"));
            return l;
        }

        private static void BindVc()
        {
            if (vcBound) return;
            vcBound = true;
            try
            {
                // the Weapons page (RadialOrderWheel.Weapons: RadialSocket's Command and Label): its category slots never call Execute
                Type wheel = ScTypes.Find("XNPCVoiceControl.Core.Pure.RadialOrderWheel");
                FieldInfo fw = wheel != null ? wheel.GetField("Weapons", BindingFlags.Public | BindingFlags.Static) : null;
                Array sockets = fw != null ? fw.GetValue(null) as Array : null;
                Type st = sockets != null ? sockets.GetType().GetElementType() : null;
                FieldInfo fc = st != null ? st.GetField("Command", BindingFlags.Public | BindingFlags.Instance) : null;
                FieldInfo fl = st != null ? st.GetField("Label", BindingFlags.Public | BindingFlags.Instance) : null;
                if (sockets != null && fc != null && fl != null)
                {
                    foreach (object s in sockets)
                    {
                        object c = fc.GetValue(s);
                        string l = fl.GetValue(s) as string;
                        if (c != null && c.ToString() == "ResolveSwapWeapon" && !string.IsNullOrEmpty(l)) weaponLabels.Add(l);
                    }
                }
                // the emote page's gestures (EmoteWheelSteps.Gestures and MoreEmotes: { clip id, English label } rows)
                Type steps = ScTypes.Find("XNPCVoiceControl.Core.Pure.EmoteWheelSteps");
                foreach (string name in new[] { "Gestures", "MoreEmotes" })
                {
                    FieldInfo f = steps != null ? steps.GetField(name, BindingFlags.Public | BindingFlags.Static) : null;
                    string[][] rows = f != null ? f.GetValue(null) as string[][] : null;
                    if (rows == null) continue;
                    foreach (string[] r in rows) if (r != null && r.Length > 0 && !string.IsNullOrEmpty(r[0])) gestureClips.Add(r[0]);
                }
            }
            catch (Exception e)
            {
                ScLog.Once("voice-routes-bind", e);
            }
            if (weaponLabels.Count > 0 && gestureClips.Count > 0) return;
            if (Patches_VoiceControl.ChatType != null)
                ScLog.Warn("voice routes: Voice Control's RadialOrderWheel.Weapons / EmoteWheelSteps.Gestures not found (Voice Control update?) - "
                           + "the wheel's weapon and gesture slots are told apart by 0-XNPCCore 3.4.06's lists");
            if (weaponLabels.Count == 0) foreach (string s in FallbackWeaponLabels) weaponLabels.Add(s);
            if (gestureClips.Count == 0) foreach (string s in FallbackGestures) gestureClips.Add(s);
        }

        // ------------------------------------------------------------------ P7: SC's spoken refusals ----

        /// <summary>SC refused an order of this NPC's this frame or the last (SayRefusal): P2's labels and the spoken order's reply (ScBarks) are no yes.</summary>
        internal static bool Refused(EntityAlive npc)
        {
            if ((object)npc == null) return false;
            int f;
            return refusedFrame.TryGetValue(npc.entityId, out f) && Time.frameCount - f <= 1;
        }

        /// <summary>
        /// P7: SC refused an order of this NPC's with one of its own lines - key, its Localization key, which an own line's clip
        /// is named after (ScVoiceLines.Own). Marks the refusal for P2 always; then, VoiceSpokenRefusals on, one of SC's
        /// survivors within 30 m of you says it in her recorded voice (ScBarks.SayOwn), or as a talk line while the talk is
        /// open on her (ScVoice.SayAfterTalk). Without a recording nothing is said. Review fix 3, the gates SC's other barks
        /// keep: the tooltip alone with ScBarks off, while she is asleep or down, and (outside a talk) while Voice Control is
        /// saying something on her - its queued next sentence would cut SC's clip. why: for the log. markOrder (re-check fix 5):
        /// false for a refusal with no order behind it (a work shift taking a field job back), which must not make the next
        /// reply on her look refused.
        /// </summary>
        internal static void SayRefusal(EntityAlive npc, string key, string why, bool markOrder = true)
        {
            try
            {
                if (npc == null || string.IsNullOrEmpty(key)) return;
                if (markOrder)
                {
                    if (refusedFrame.Count > 64) PruneRefused();   // review fix 8
                    refusedFrame[npc.entityId] = Time.frameCount;
                }
                if (!ScConfig.VoiceSpokenRefusals || npc.IsDead() || !ScXnpc.IsSurvivorBody(npc)) return;
                if (!ScConfig.ScBarks
#if SC_V34
                    || ScSleep.IsAsleep(npc) || ScDowned.IsDown(npc)
#endif
                   )
                {
                    refusalsQuiet++;                                // review fix 3: as SC's other barks, not a word
                    return;
                }
                EntityPlayerLocal lp = ScCommunity.LocalPlayer;
                if (lp == null || (npc.position - lp.position).sqrMagnitude > SpokenRange * SpokenRange)
                {
                    refusalsFar++;
                    return;
                }
                if (ScVoice.Holds(npc))
                {
                    // the talk is open on her (the dialog's "Come with me.", its weapon pick): a talk line, which its close leaves be
                    ScVoice.SayAfterTalk(npc, key);
                    refusalsInTalk++;
                    return;
                }
                if (ScVoice.VcBusy(npc))
                {
                    refusalsQuiet++;                                // review fix 3: Voice Control's next sentence would cut it
                    return;
                }
                if (ScBarks.SayOwn(npc, key, why, lp)) refusalsSaid++;
                else refusalsUnrecorded++;
            }
            catch (Exception e)
            {
                ScLog.Once("voice-routes-refusal", e);
            }
        }

        /// <summary>P7: a Follow refusal for want of room - SC's MaxFollowers (scFollowersFull) or 0-XNPCCore's party cap (ScPartyCap's text).</summary>
        internal static bool IsFollowFull(string error)
        {
            if (string.IsNullOrEmpty(error)) return false;
            if (error == "scFollowersFull") return true;
#if SC_V34
            return error == ScPartyCap.LastRefusal;
#else
            return false;
#endif
        }

        // ------------------------------------------------------------------ console ----

        /// <summary>sc voice barks: the routes' switches and this session's counts.</summary>
        public static string Status()
        {
            return "routes (round m1w): wheel acks " + (ScConfig.VoiceWheelAcks ? "on" : "off") + " (" + stamped + " orders noted, " + mapped + " labels taken for a reply, "
                   + silenced + " left silent, " + left + " left to Voice Control, " + afterRefusal + " after a refusal, " + notRun + " not run by Voice Control), one line per NPC "
                   + (ScConfig.VoiceOneLineSeconds > 0f ? ScConfig.VoiceOneLineSeconds.ToString("0.##", CultureInfo.InvariantCulture) + " s" : "off")
                   + ", spoken refusals " + (ScConfig.VoiceSpokenRefusals ? "on" : "off") + " (" + refusalsSaid + " said, " + refusalsUnrecorded + " not recorded yet, "
                   + refusalsInTalk + " in a talk, " + refusalsQuiet + " kept quiet (asleep, down, Voice Control speaking, ScBarks off), " + refusalsFar + " too far), greetings "
                   + (ScConfig.ScGreetingsOwnAll ? "SC's for all of SC's people (Voice Control's where SC has no recording)" : "Voice Control's where SC has none");
        }
    }
}
