using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round tl1 (2026-11-18, the user's report: "I talked to them before, but now I can't open the dialogue window anymore",
    /// with "[LockManager] Failed to lock EntityAliveSDXV4 on channel 0. Target refused to be locked on server.").
    ///
    /// SCore's NPCs are EntityTraders, and EntityAliveSDXV4.OnUpdateLive runs vanilla's EntityTrader.OnUpdateLive. The first time
    /// such an NPC stands inside a trader's compound, vanilla stores that TraderArea on it for good (traderArea is only looked up
    /// while it is null), and from then on the NPC behaves as that trader:
    ///   - EntityTrader.OnLockRequestServer refuses the talk lock while the trader is closed (22:00 to 06:00 by default) - the
    ///     reported bug: no dialog, a "Failed to lock" warning on every E;
    ///   - every second it despawns itself when another NPC of the same entity class with a lower id stands within 10 m (vanilla's
    ///     "one trader per compound" rule) - SC's people share a few survivor classes, so a stranger or member could vanish;
    ///   - it greets players with trader voice lines;
    ///   - and, since 0-XNPCCore gives every body trader id 64 (its stock) and its legacy NPCs id 1, it runs the trader's
    ///     open/close tail with THAT trader's hours: id 64 has none (always open), so a body standing in a compound re-opened
    ///     it at night (doors, lights) once a second, fighting the real trader's own tick (the Fable review of tl1).
    /// "Has a TraderInfo" therefore says nothing about being a compound's trader. No SCore NPC is one: in this install only
    /// 0-XNPCCore and SC define SCore classes, and the compound traders are vanilla EntityTraders. So for every SCore NPC
    /// (EntityAliveSDXV4 / EntityAliveSDX) SC keeps the stored area empty (a postfix clears it after every update), stops the
    /// once-a-second trader block from running (the prefix pushes vanilla's private updateTime out of reach), and lets the
    /// talk lock ignore a closed trader area. Vanilla traders are untouched. Host / single player, main thread.
    /// </summary>
    public static class Patches_TraderArea
    {
        private static AccessTools.FieldRef<EntityTrader, float> updateTimeRef;
        private static bool refTried;
        private static readonly HashSet<int> told = new HashSet<int>();

        /// <summary>
        /// An SCore NPC - never a trader compound's own trader, whatever trader id it carries (0-XNPCCore's 64 stock id, its
        /// legacy NPCs' 1, an enclave leader's 200+). A type test, so it never changes for a live body.
        /// </summary>
        public static bool NotATrader(EntityTrader t)
        {
            return t is EntityAliveSDXV4 || t is EntityAliveSDX;
        }

        public static MethodBase UpdateTarget()
        {
            return AccessTools.DeclaredMethod(typeof(EntityTrader), "OnUpdateLive", Type.EmptyTypes);
        }

        public static MethodBase LockTarget()
        {
            return AccessTools.DeclaredMethod(typeof(EntityTrader), "OnLockRequestServer", new[] { typeof(int), typeof(PooledBinaryReader), typeof(ushort) });
        }

        private static AccessTools.FieldRef<EntityTrader, float> UpdateTimeRef()
        {
            if (refTried) return updateTimeRef;
            refTried = true;
            try
            {
                updateTimeRef = AccessTools.FieldRefAccess<EntityTrader, float>("updateTime");
            }
            catch (Exception e)
            {
                ScLog.Err("EntityTrader.updateTime not found (game update?) - an SCore NPC standing in a trader compound may still "
                          + "greet as its trader, re-open it at night and despawn next to another NPC of its class (" + e.GetType().Name + ")");
            }
            return updateTimeRef;
        }

        /// <summary>World change: forget which NPCs were logged.</summary>
        public static void Reset()
        {
            told.Clear();
        }

        [HarmonyPatch]
        public static class Patch_NpcTraderUpdate
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                if (UpdateTarget() != null) return true;
                ScLog.Err("EntityTrader.OnUpdateLive() not found (game update?) - SCore NPCs that walk through a trader compound act as its "
                          + "trader again (no talk while it is closed)");
                return false;
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return UpdateTarget(); }

            [HarmonyPrefix]
            public static void Prefix(EntityTrader __instance)
            {
                try
                {
                    if (!NotATrader(__instance)) return;
                    var r = UpdateTimeRef();
                    if (r != null) r(__instance) = float.MaxValue;     // the once-a-second trader block (greet, despawn) never runs
                }
                catch (Exception e)
                {
                    ScLog.Once("trader-area-prefix", e);
                }
            }

            [HarmonyPostfix]
            public static void Postfix(EntityTrader __instance)
            {
                try
                {
                    if (__instance.traderArea == null || !NotATrader(__instance)) return;
                    __instance.traderArea = null;
                    if (told.Count > 512) told.Clear();
                    if (told.Add(__instance.entityId))
                        ScLog.Verbose(__instance.EntityName + " (" + __instance.entityId + ") walked into a trader compound - SC keeps the NPC "
                                      + "from taking it on as its trader (round tl1)");
                }
                catch (Exception e)
                {
                    ScLog.Once("trader-area-postfix", e);
                }
            }
        }

        /// <summary>
        /// Belt and braces: the talk lock ignores a closed trader area on a non-trader NPC (one stored before this round's update
        /// postfix could clear it). Runs before 0-XNPCCore's SdcsSleepTalkWakePatch postfix, so a talk still wakes a sleeper.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_NpcTalkLock
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                if (LockTarget() != null) return true;
                ScLog.Err("EntityTrader.OnLockRequestServer(int, PooledBinaryReader, ushort) not found (game update?) - an SCore NPC "
                          + "that stood in a trader compound may refuse to talk while that trader is closed");
                return false;
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return LockTarget(); }

            [HarmonyPostfix]
            [HarmonyPriority(Priority.First)]
            public static void Postfix(EntityTrader __instance, ref bool __result)
            {
                try
                {
                    if (__result || __instance == null || __instance.IsDead()) return;
                    TraderArea a = __instance.traderArea;
                    if (a == null || !a.IsClosed || !NotATrader(__instance)) return;
                    __result = true;
                    ScLog.Verbose(__instance.EntityName + " (" + __instance.entityId + "): talk allowed past a closed trader area (round tl1)");
                }
                catch (Exception e)
                {
                    ScLog.Once("trader-area-lock", e);
                }
            }
        }
    }
}
