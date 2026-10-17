using System;
using HarmonyLib;

namespace SurvivorCommunity
{
    /// <summary>
    /// The spawn changes of an infested POI (I1, infestations.md 2.2 and 4b), on the rare SleeperVolume methods a room spawns
    /// through - never Tick or CheckTouching (every volume, every tick). Each hook asks ScInfestations whether the room's POI
    /// is infested (a lookup over a volatile array, empty with none) and does nothing otherwise. None skips the original, and
    /// an enclave's POI is never touched here: E1's prefix on the same UpdatePlayerTouched owns those rooms
    /// (Patches_Enclaves). The feral share lives in ScLoadout's GetRandomFromGroupList postfix, scoped by the UpdateSpawn hook
    /// below. SCore's postfix on ClearedUpdate (an event) runs alongside ours.
    /// </summary>
    public static class Patches_Infestations
    {
        /// <summary>
        /// Where a room starts spawning (from Tick after a player's touch, and from OnTriggered). The prefix gives the POI its
        /// infested state before vanilla rolls the count and opens the spawn points, and tells the postfix whether the roll is
        /// a fresh one; the postfix caps the roll and may make a fresh room the one that brings the special.
        /// </summary>
        [HarmonyPatch(typeof(SleeperVolume), nameof(SleeperVolume.UpdatePlayerTouched), new Type[] { typeof(World), typeof(EntityPlayer) })]
        public static class Patch_UpdatePlayerTouched
        {
            [HarmonyPrefix]
            public static void Prefix(SleeperVolume __instance, World _world, out int __state)
            {
                __state = 0;
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

        /// <summary>One spawn step of a room (each tick while it spawns): scopes the feral share and the special's check to an infested room.</summary>
        [HarmonyPatch(typeof(SleeperVolume), nameof(SleeperVolume.UpdateSpawn), new Type[] { typeof(World) })]
        public static class Patch_UpdateSpawn
        {
            [HarmonyPrefix]
            public static void Prefix(SleeperVolume __instance)
            {
                try
                {
                    ScInfestations.BeginSpawn(__instance);
                }
                catch (Exception e)
                {
                    ScLog.Once("infest-spawn-prefix", e);
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
