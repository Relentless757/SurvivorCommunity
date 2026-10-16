using System;
using HarmonyLib;

namespace SurvivorCommunity
{
    /// <summary>
    /// An enclave's POI never spawns sleeper zombies (E1, infestations.md section 5). ScEnclaves marks every generated sleeper
    /// area of the POI cleared for good (wasCleared, respawnTime = never; both are saved with the world) when the enclave is
    /// seeded, moved or loaded. This prefix does the same, lazily, for whatever undoes that or comes later: rooms whose chunks
    /// are generated afterwards, vanilla's re-arms (a quest's SleeperEventData, World.ResetPOIS, chunk resets, "sleeper r")
    /// and trigger rooms fired by doors. UpdatePlayerTouched is where a volume starts spawning - from Tick after a player's
    /// touch, and from OnTriggered - and it is rare; Tick and CheckTouching run every tick and are never patched. The rest of
    /// the protection sits with the outposts' (Patches_Outposts: CheckForAnyPlayerHome, the claim gate) and Patches_Quest.
    /// </summary>
    public static class Patches_Enclaves
    {
        [HarmonyPatch(typeof(SleeperVolume), nameof(SleeperVolume.UpdatePlayerTouched), new Type[] { typeof(World), typeof(EntityPlayer) })]
        public static class Patch_UpdatePlayerTouched
        {
            /// <summary>
            /// Returns false on purpose for a volume of an enclave POI: the original would roll the room's sleepers, run its
            /// script and start spawning them. The room is marked cleared instead, as if the enclave had cleared it.
            /// </summary>
            [HarmonyPrefix]
            public static bool Prefix(SleeperVolume __instance, World _world)
            {
                try
                {
                    if (__instance == null || !ScEnclaves.IsEnclavePoi(__instance.PrefabInstance)) return true;
                    ScEnclaves.KeepCleared(__instance, _world);
                    return false;
                }
                catch (Exception e)
                {
                    ScLog.Once("enclave-sleeper-touch", e);
                    return true;
                }
            }
        }
    }
}
