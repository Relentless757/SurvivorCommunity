using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round U2 (2026-11-16, research/npc306/part_downed.md sections 1, 2 and 5; the user's decision D5, "Do it now"): 0-XNPCCore
    /// 3.4.06's downed state for the community's people at home. 0-XNPCCore downs an NPC only while it has a player leader
    /// (SdcsDowned.HasPlayerLeader: the Leader or Owner cvar), counts her bleed-out only while that leader is present
    /// (LeaderPresent) and tells only that leader (TellLeader). A resident at home and a guard on watch have neither cvar, so
    /// 0-XNPCCore killed them outright. There is no hook for either rule (the author's AppDomain slots are for after the
    /// knockdown and the revive), so SC re-points those three private calls inside their callers - never the three small
    /// methods themselves, which the JIT may inline (HasPlayerLeader is 26 bytes of IL, LeaderPresent 48, TellLeader 73).
    ///
    /// One transpiler, five targets, each a call swapped for SC's own static method of the same signature (stack-neutral, no
    /// local slots), every one checked by count:
    ///   SdcsDownedCatch.ShouldCatch      (118 bytes)  HasPlayerLeader x1   the damage-response catch (the knockdown)
    ///   SdcsDownedCatch.ShouldCatchKill  (139 bytes)  HasPlayerLeader x1   the Kill catch (a kill off the damage path)
    ///   SdcsDownedCatch.QueueVerdict     ( 93 bytes)  HasPlayerLeader x1   its verdict (down, or a real death)
    ///   SdcsDowned.Enter                 (536 bytes)  HasPlayerLeader x1, TellLeader x1
    ///   SdcsDowned.Tick                  (777 bytes)  LeaderPresent x1, TellLeader x1
    /// All five are far above Mono's inlining limit (20 bytes), so the detour on each is the method every caller reaches.
    /// The five are the game's paths, not every caller (the Fable review, F6): a sixth call of HasPlayerLeader, in
    /// SdcsAnimBridgeHandlers.AnyWeaponSdcsAnimDowned (IL_0029, 0-XNPCCore's own console "downed" for a body seated in a vehicle),
    /// is left as it is - a seated home member is refused there; no gameplay path reaches it.
    ///
    /// SC's methods call 0-XNPCCore's own first (bound once as delegates): 0-XNPCCore decides exactly as before for everyone,
    /// and SC only adds its own people where 0-XNPCCore said no (ScDowned: HomeDownable, BleedsWithoutLeader, the tells).
    /// Fail-loud: a target or a callee that is not there, or a call found a different number of times than above, logs ONE
    /// error and the whole feature stays off - every swapped call then answers exactly what 0-XNPCCore's own would (Active
    /// false), so nothing half-runs. Settle (from InitMod, after the patching) decides Active once and logs it.
    /// </summary>
    public static class Patches_Downed
    {
        private const string Feature = "downed (round U2)";

        private static bool bound, bindOk;
        private static string bindWhy = "";
        private static MethodInfo mHasPlayerLeader, mLeaderPresent, mTellLeader;
        private static MethodInfo mShouldCatch, mShouldCatchKill, mQueueVerdict, mEnter, mTick;
        private static MethodInfo hEligible, hBleeds, hTell;
        private static Func<EntityAlive, bool> origHasPlayerLeader, origLeaderPresent;
        private static Action<EntityAlive, string, int> origTellLeader;

        /// <summary>Per target: what the transpiler found on its last run (Harmony runs every transpiler again when anyone adds a patch).</summary>
        private static readonly Dictionary<MethodBase, string> found = new Dictionary<MethodBase, string>();
        private static readonly Dictionary<MethodBase, bool> swapped = new Dictionary<MethodBase, bool>();
        private static bool settled;

        /// <summary>All five targets swapped as expected: SC's people at home go down. False: 0-XNPCCore's own rules, exactly.</summary>
        public static bool Active { get; private set; }

        /// <summary>For sc downed: the patch state in one line.</summary>
        public static string State { get; private set; } = "not patched yet";

        /// <summary>Counters (sc downed): a home member made eligible, a bleed kept running, a tell sent to the player.</summary>
        internal static int EligibleHome, BleedHome, TellsHome, TellsDown;

        // ------------------------------------------------------------------ binding ----

        /// <summary>Resolves every method once. False (with bindWhy) when 0-XNPCCore has no downed state or it changed shape.</summary>
        internal static bool Bind()
        {
            if (bound) return bindOk;
            bound = true;
            try { bindOk = BindCore(); }
            catch (Exception e)
            {
                // an 0-XNPCCore without SdcsDowned (before 3.4.06) fails the JIT of BindCore here: the same answer, no patch
                bindOk = false;
                bindWhy = "0-XNPCCore has no downed state (" + e.GetType().Name + ": " + e.Message + ") - 3.4.06 or later is needed";
            }
            return bindOk;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool BindCore()
        {
            const BindingFlags S = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            Type downed = typeof(SdcsDowned);
            Type catcher = downed.Assembly.GetType("NPCSdcsCharacters.SdcsDownedCatch", false);
            Type[] one = { typeof(EntityAlive) };
            mHasPlayerLeader = downed.GetMethod("HasPlayerLeader", S, null, one, null);
            mLeaderPresent = downed.GetMethod("LeaderPresent", S, null, one, null);
            mTellLeader = downed.GetMethod("TellLeader", S, null, new[] { typeof(EntityAlive), typeof(string), typeof(int) }, null);
            mEnter = downed.GetMethod("Enter", S, null, one, null);
            mTick = downed.GetMethod("Tick", S, null, one, null);
            if (catcher != null)
            {
                mShouldCatch = catcher.GetMethod("ShouldCatch", S, null, new[] { typeof(EntityAlive), typeof(DamageResponse) }, null);
                mShouldCatchKill = catcher.GetMethod("ShouldCatchKill", S, null, new[] { typeof(EntityAlive), typeof(DamageResponse) }, null);
                mQueueVerdict = catcher.GetMethod("QueueVerdict", S, null, one, null);
            }
            hEligible = typeof(Patches_Downed).GetMethod(nameof(HasPlayerLeaderOrHome), S, null, one, null);
            hBleeds = typeof(Patches_Downed).GetMethod(nameof(LeaderPresentOrHome), S, null, one, null);
            hTell = typeof(Patches_Downed).GetMethod(nameof(TellLeaderOrPlayer), S, null, new[] { typeof(EntityAlive), typeof(string), typeof(int) }, null);
            var missing = new List<string>();
            if (catcher == null) missing.Add("NPCSdcsCharacters.SdcsDownedCatch");
            if (mHasPlayerLeader == null || mHasPlayerLeader.ReturnType != typeof(bool)) missing.Add("SdcsDowned.HasPlayerLeader(EntityAlive)");
            if (mLeaderPresent == null || mLeaderPresent.ReturnType != typeof(bool)) missing.Add("SdcsDowned.LeaderPresent(EntityAlive)");
            if (mTellLeader == null || mTellLeader.ReturnType != typeof(void)) missing.Add("SdcsDowned.TellLeader(EntityAlive, string, int)");
            if (mEnter == null) missing.Add("SdcsDowned.Enter(EntityAlive)");
            if (mTick == null) missing.Add("SdcsDowned.Tick(EntityAlive)");
            if (catcher != null && mShouldCatch == null) missing.Add("SdcsDownedCatch.ShouldCatch(EntityAlive, DamageResponse)");
            if (catcher != null && mShouldCatchKill == null) missing.Add("SdcsDownedCatch.ShouldCatchKill(EntityAlive, DamageResponse)");
            if (catcher != null && mQueueVerdict == null) missing.Add("SdcsDownedCatch.QueueVerdict(EntityAlive)");
            if (hEligible == null || hBleeds == null || hTell == null) missing.Add("SC's own hook methods");
            if (missing.Count > 0)
            {
                bindWhy = "0-XNPCCore's downed code changed shape - not found: " + string.Join(", ", missing.ToArray());
                return false;
            }
            origHasPlayerLeader = (Func<EntityAlive, bool>)Delegate.CreateDelegate(typeof(Func<EntityAlive, bool>), mHasPlayerLeader);
            origLeaderPresent = (Func<EntityAlive, bool>)Delegate.CreateDelegate(typeof(Func<EntityAlive, bool>), mLeaderPresent);
            origTellLeader = (Action<EntityAlive, string, int>)Delegate.CreateDelegate(typeof(Action<EntityAlive, string, int>), mTellLeader);
            return true;
        }

        /// <summary>The five targets for ModApi's patch verification (only when they all exist: a missing one is Settle's one error).</summary>
        public static List<KeyValuePair<string, MethodBase>> Targets()
        {
            var list = new List<KeyValuePair<string, MethodBase>>();
            if (!Bind()) return list;
            list.Add(new KeyValuePair<string, MethodBase>("SdcsDownedCatch::ShouldCatch", mShouldCatch));
            list.Add(new KeyValuePair<string, MethodBase>("SdcsDownedCatch::ShouldCatchKill", mShouldCatchKill));
            list.Add(new KeyValuePair<string, MethodBase>("SdcsDownedCatch::QueueVerdict", mQueueVerdict));
            list.Add(new KeyValuePair<string, MethodBase>("SdcsDowned::Enter", mEnter));
            list.Add(new KeyValuePair<string, MethodBase>("SdcsDowned::Tick", mTick));
            return list;
        }

        /// <summary>
        /// From InitMod, once the patch classes ran: Active only when all five targets were swapped exactly as expected. One line
        /// either way - "ON" with the counts, or ONE error naming what was wrong (the feature then stays off).
        /// </summary>
        public static void Settle()
        {
            if (settled) return;
            settled = true;
            try
            {
                if (!Bind())
                {
                    Active = false;
                    State = "OFF - " + bindWhy;
                    ScLog.Err(Feature + ": " + bindWhy + " - residents at home and guards on watch die at once, as before; "
                              + "every other guard of the round stays on");
                    return;
                }
                var bad = new List<string>();
                foreach (var t in Targets())
                {
                    bool ok;
                    string what;
                    swapped.TryGetValue(t.Value, out ok);
                    found.TryGetValue(t.Value, out what);
                    if (!ok) bad.Add(t.Key + " (" + (what ?? "the transpiler never ran") + ")");
                }
                if (bad.Count > 0)
                {
                    Active = false;
                    State = "OFF - not as expected: " + string.Join("; ", bad.ToArray());
                    ScLog.Err(Feature + ": 0-XNPCCore's downed code is not what SC expects - " + string.Join("; ", bad.ToArray())
                              + " - the feature is OFF (0-XNPCCore's own rules exactly: residents at home and guards on watch die at once). "
                              + "0-XNPCCore update? Check research/npc306/part_downed.md against the new build.");
                    return;
                }
                Active = true;
                State = "ON - 5 of 5 calls swapped (ShouldCatch, ShouldCatchKill, QueueVerdict, Enter, Tick)";
                ScLog.Out(Feature + ": 0-XNPCCore's downed state patched - " + State);
            }
            catch (Exception e)
            {
                Active = false;
                State = "OFF - settle failed: " + e.Message;
                ScLog.Err(Feature + ": checking the patches failed (" + e.Message + ") - the feature is OFF");
            }
        }

        // ------------------------------------------------------------------ the transpiler ----

        [HarmonyPatch]
        public static class Patch_DownedCalls
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                // the reason is logged once, by Settle (one clear error, not one per patch class)
                try { return Bind(); }
                catch (Exception) { return false; }
            }

            [HarmonyTargetMethods]
            public static IEnumerable<MethodBase> TargetMethods()
            {
                var list = new List<MethodBase>();
                if (!Bind()) return list;
                list.Add(mShouldCatch);
                list.Add(mShouldCatchKill);
                list.Add(mQueueVerdict);
                list.Add(mEnter);
                list.Add(mTick);
                return list;
            }

            [HarmonyTranspiler]
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
            {
                var list = new List<CodeInstruction>(instructions);
                try
                {
                    int lead, present, tell;
                    Expected(original, out lead, out present, out tell);
                    int nLead = 0, nPresent = 0, nTell = 0;
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (list[i].Calls(mHasPlayerLeader)) nLead++;
                        else if (list[i].Calls(mLeaderPresent)) nPresent++;
                        else if (list[i].Calls(mTellLeader)) nTell++;
                    }
                    string what = "HasPlayerLeader x" + nLead + ", LeaderPresent x" + nPresent + ", TellLeader x" + nTell;
                    if (original != null) found[original] = what;
                    if (lead < 0 || nLead != lead || nPresent != present || nTell != tell)
                    {
                        if (original != null) swapped[original] = false;
                        return new List<CodeInstruction>(instructions);   // untouched: 0-XNPCCore's own code runs
                    }
                    for (int i = 0; i < list.Count; i++)
                    {
                        // the operand only: labels and exception blocks stay on the instruction; same signature, same stack
                        if (list[i].Calls(mHasPlayerLeader)) { list[i].opcode = OpCodes.Call; list[i].operand = hEligible; }
                        else if (list[i].Calls(mLeaderPresent)) { list[i].opcode = OpCodes.Call; list[i].operand = hBleeds; }
                        else if (list[i].Calls(mTellLeader)) { list[i].opcode = OpCodes.Call; list[i].operand = hTell; }
                    }
                    swapped[original] = true;
                    return list;
                }
                catch (Exception e)
                {
                    if (original != null) { swapped[original] = false; found[original] = "the transpiler failed: " + e.Message; }
                    return new List<CodeInstruction>(instructions);
                }
            }
        }

        /// <summary>What each target is expected to hold (read in 3.4.06's IL: research/npc306/part_downed.md and this round's IL dump).</summary>
        private static void Expected(MethodBase m, out int lead, out int present, out int tell)
        {
            lead = -1; present = 0; tell = 0;
            if (m == null) return;
            if (m == (MethodBase)mShouldCatch || m == (MethodBase)mShouldCatchKill || m == (MethodBase)mQueueVerdict) { lead = 1; return; }
            if (m == (MethodBase)mEnter) { lead = 1; tell = 1; return; }
            if (m == (MethodBase)mTick) { lead = 0; present = 1; tell = 1; }
        }

        // ------------------------------------------------------------------ the swapped-in calls ----

        /// <summary>
        /// In place of SdcsDowned.HasPlayerLeader (the catch, the kill catch, the verdict, Enter): 0-XNPCCore's own answer, and
        /// when that is no, SC's - a resident at home or a guard on watch (ScDowned.HomeDownable, DownedAtHome). Runs on every hit
        /// on one of 0-XNPCCore's survivors, before its lethal test: two cvar reads and a dictionary lookup.
        /// </summary>
        public static bool HasPlayerLeaderOrHome(EntityAlive npc)
        {
            if (origHasPlayerLeader(npc)) return true;
            if (!Active) return false;
            try
            {
                if (ScDowned.HomeDownable(npc)) { EligibleHome++; return true; }
            }
            catch (Exception e) { ScLog.Once("downed-eligible", e); }
            return false;
        }

        /// <summary>
        /// In place of SdcsDowned.LeaderPresent (Tick: the bleed-out counts only while it is true): 0-XNPCCore's own answer, and
        /// when that is no, SC's - one of the community's people whose bleed-out must run anyway (ScDowned.BleedsWithoutLeader:
        /// at home there is no leader at all, and a link SC holds could still be lost; without this she would lie invulnerable for
        /// good). Paused while you are dead, as 0-XNPCCore pauses for a dead leader.
        /// </summary>
        public static bool LeaderPresentOrHome(EntityAlive npc)
        {
            if (origLeaderPresent(npc)) return true;
            if (!Active) return false;
            try
            {
                if (ScDowned.BleedsWithoutLeader(npc)) { BleedHome++; return true; }
            }
            catch (Exception e) { ScLog.Once("downed-bleeds", e); }
            return false;
        }

        /// <summary>
        /// In place of SdcsDowned.TellLeader (Enter: "is down"; Tick: the bleed-out warnings). One of the community's people: the
        /// "is down" line is SC's own (D6), kept back here and sent by ScDowned's tick within the second, after its medic pick,
        /// with who is coming folded in (the Fable review, F2: one tooltip, not three); a warning goes to the player when she has
        /// no leader to tell (at home). Everyone else, and the follower's warnings: 0-XNPCCore's own call.
        /// </summary>
        public static void TellLeaderOrPlayer(EntityAlive npc, string key, int seconds)
        {
            if (Active)
            {
                try
                {
                    Member m = ScDowned.MemberOfBody(npc);
                    if (m != null)
                    {
                        if (key == "vcDownedIsDown" && ScConfig.DownedTell) { TellsDown++; return; }   // ScDowned.Tick says it
                        if (!(SdcsNpcRelation.LeaderEntity(npc.entityId) is EntityPlayer)) { ScDowned.TellPlayerNpcCore(npc, key, seconds); TellsHome++; return; }
                    }
                }
                catch (Exception e) { ScLog.Once("downed-tell", e); }
            }
            origTellLeader(npc, key, seconds);
        }

        /// <summary>For sc downed: 0-XNPCCore's own eligibility answer, read through the original (never the swapped call).</summary>
        internal static bool NpcCoreEligible(EntityAlive npc)
        {
            try { return Bind() && origHasPlayerLeader(npc); }
            catch (Exception) { return false; }
        }
    }
}
