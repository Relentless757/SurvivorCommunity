using System;
using HarmonyLib;

namespace SurvivorCommunity
{
    /// <summary>
    /// The spawn changes of an infested POI (I1, infestations.md 2.2 and 4b), on the rare SleeperVolume methods a room spawns
    /// through - never Tick or CheckTouching (every volume, every tick). Each hook asks ScInfestations whether the room's POI
    /// is infested (a lookup over a volatile array, empty with none) and does nothing otherwise. An enclave's POI is never
    /// touched here: E1's prefix on the same UpdatePlayerTouched owns those rooms (Patches_Enclaves). The feral share lives in
    /// ScLoadout's GetRandomFromGroupList postfix, scoped by the UpdateSpawn hook below. SCore's postfix on ClearedUpdate (an
    /// event) runs alongside ours.
    /// I2 (ScSleeperCap, the zombie-cap quirk) rides the same two hooks for every POI: UpdateSpawn's postfix gives a room the
    /// cap closed empty a retry (layer A) and its prefix holds an infested room's step at the cap (layer B, the only skip of
    /// an original here); UpdatePlayerTouched's prefix repairs a room saved that way before (layer C). No other installed mod
    /// patches UpdateSpawn (SCore patches ClearedUpdate and AddEnemyToWorld, ZombieAIOverhaul Touch, OnTriggered, CheckNoise
    /// and AddEnemyToWorld; checked 2026-11-03 for I2).
    /// </summary>
    public static class Patches_Infestations
    {
        /// <summary>
        /// Where a room starts spawning (from Tick after a player's touch, and from OnTriggered). The prefix first repairs a
        /// room the enemy cap closed empty in an earlier session (I2 layer C: due now, so vanilla resets and rolls it in this
        /// call), then gives the POI its infested state before vanilla rolls the count and opens the spawn points, and tells
        /// the postfix whether the roll is a fresh one; the postfix caps the roll and may make a fresh room the one that
        /// brings the special.
        /// </summary>
        [HarmonyPatch(typeof(SleeperVolume), nameof(SleeperVolume.UpdatePlayerTouched), new Type[] { typeof(World), typeof(EntityPlayer) })]
        public static class Patch_UpdatePlayerTouched
        {
            [HarmonyPrefix]
            public static void Prefix(SleeperVolume __instance, World _world, out int __state)
            {
                __state = 0;
                if (__instance == null) return;
                try
                {
                    ScSleeperCap.OnTouch(__instance, _world);
                }
                catch (Exception e)
                {
                    ScLog.Once("sleepercap-touch", e);
                }
                try
                {
                    __state = ScInfestations.BeforeRoll(__instance, _world);
                }
                catch (Exception e)
                {
                    ScLog.Once("infest-touch-prefix", e);
                }
            }

            [HarmonyPostfix]
            public static void Postfix(SleeperVolume __instance, World _world, int __state)
            {
                if (__state == 0) return;
                try
                {
                    ScInfestations.AfterRoll(__instance, _world, __state);
                }
                catch (Exception e)
                {
                    ScLog.Once("infest-touch-postfix", e);
                }
            }
        }

        /// <summary>
        /// One spawn step of a room (each tick while it spawns; vanilla runs a step every second call): scopes the feral share
        /// and the special's check to an infested room. I2: the prefix may hold an infested room's step while the enemy cap is
        /// full and a player is near (layer B - false skips the original, the room stays spawning); the postfix gives a room
        /// that closed empty at the cap in this call a retry (layer A). __state: the room was spawning as the call began.
        /// </summary>
        [HarmonyPatch(typeof(SleeperVolume), nameof(SleeperVolume.UpdateSpawn), new Type[] { typeof(World) })]
        public static class Patch_UpdateSpawn
        {
            [HarmonyPrefix]
            public static bool Prefix(SleeperVolume __instance, World _world, out bool __state)
            {
                __state = __instance != null && __instance.isSpawning;
                if (__instance == null) return true;
                try
                {
                    if (ScSleeperCap.Hold(__instance, _world)) return false;
                }
                catch (Exception e)
                {
                    ScLog.Once("sleepercap-hold", e);
                }
                try
                {
                    ScInfestations.BeginSpawn(__instance);
                }
                catch (Exception e)
                {
                    ScLog.Once("infest-spawn-prefix", e);
                }
                return true;
            }

            /// <summary>I2 layer A: the room stopped spawning in this call - closed empty at the cap, it is due again later.</summary>
            [HarmonyPostfix]
            public static void Postfix(SleeperVolume __instance, World _world, bool __state)
            {
                if (!__state || __instance == null || __instance.isSpawning) return;
                try
                {
                    ScSleeperCap.AfterClose(__instance, _world);
                }
                catch (Exception e)
                {
                    ScLog.Once("sleepercap-close", e);
                }
            }

            /// <summary>A finalizer, so the scope is cleared even when the step throws (the exception goes on as it was).</summary>
            [HarmonyFinalizer]
            public static void Finalizer(SleeperVolume __instance)
            {
                try
                {
                    ScInfestations.EndSpawn(__instance);
                }
                catch (Exception e)
                {
                    ScLog.Once("infest-spawn-finalizer", e);
                }
            }
        }

        /// <summary>A spawn started (restores too): in the room bringing the special, a radiated, charged or infernal class meets the guarantee.</summary>
        [HarmonyPatch(typeof(SleeperVolume), nameof(SleeperVolume.Spawn), new Type[] { typeof(World), typeof(int), typeof(int), typeof(BlockSleeper) })]
        public static class Patch_Spawn
        {
            [HarmonyPostfix]
            public static void Postfix(SleeperVolume __instance, int entityClassId, bool __result)
            {
                if (!__result || ScInfestations.CurrentVolume == null) return;
                try
                {
                    ScInfestations.OnSpawned(__instance, entityClassId);
                }
                catch (Exception e)
                {
                    ScLog.Once("infest-spawned", e);
                }
            }
        }

        /// <summary>A room's last sleeper died (or it finished spawning): when it reads cleared, is the whole infested POI?</summary>
        [HarmonyPatch(typeof(SleeperVolume), nameof(SleeperVolume.ClearedUpdate), new Type[] { typeof(World) })]
        public static class Patch_ClearedUpdate
        {
            [HarmonyPostfix]
            public static void Postfix(SleeperVolume __instance, World _world)
            {
                if (__instance == null || !__instance.wasCleared) return;
                try
                {
                    ScInfestations.OnRoomCleared(__instance, _world);
                }
                catch (Exception e)
                {
                    ScLog.Once("infest-room-cleared", e);
                }
            }
        }
    }
}
