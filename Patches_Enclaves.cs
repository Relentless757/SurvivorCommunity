using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

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
    ///
    /// E2 adds: their containers are refused while they live (Patch_ContainerLock), and biome-spawned NPCs are kept off their
    /// grounds (Patch_BiomeSpawn and Patch_BiomeSpawnSpot, with the check in ScLoadout's group-pick postfix).
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

        /// <summary>
        /// E2 (Q7 a): a POI's loot container inside a living enclave's footprint - raider or not - is refused while the enclave
        /// exists, with "That belongs to &lt;name&gt;." (EnclaveContainerLock). TEFeatureStorage.OnBlockActivated with the
        /// "Search" command is the one way the loot window opens: E and the radial both run it, it takes the container's lock,
        /// and the lock's answer opens the window (ShowUI); no class overrides it. A container the player placed (player
        /// storage, or a placed block) is never refused. Breaking one open is not covered (README). Host / single player: a
        /// client keeps no enclave records.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_ContainerLock
        {
            public static MethodBase Target()
            {
                return AccessTools.DeclaredMethod(typeof(TEFeatureStorage), "OnBlockActivated",
                    new[] { typeof(ReadOnlySpan<char>), typeof(WorldBase), typeof(Vector3i), typeof(BlockValue), typeof(EntityPlayerLocal) });
            }

            [HarmonyPrepare]
            public static bool Prepare()
            {
                try
                {
                    if (Target() != null) return true;
                    ScLog.Err("TEFeatureStorage.OnBlockActivated(ReadOnlySpan<char>, WorldBase, Vector3i, BlockValue, EntityPlayerLocal) not found (game update?) - enclaves' containers open for anyone");
                }
                catch (Exception e) { ScLog.Once("enclave-bind-container", e); }
                return false;
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return Target(); }

            /// <summary>Returns false on purpose for a refused container: the original would take the lock and open the window.</summary>
            [HarmonyPrefix]
            public static bool Prefix(TEFeatureStorage __instance, ReadOnlySpan<char> _commandName, Vector3i _blockPos, EntityPlayerLocal _player, ref bool __result)
            {
                try
                {
                    if (__instance == null || !ScCommunity.IsHost || !__instance.CommandIs(_commandName, "Search")) return true;
                    if (__instance.bPlayerStorage || (__instance.Parent != null && __instance.Parent.PlayerPlaced)) return true;
                    Enclave e = ScEnclavePeople.ContainerOwner(_blockPos);
                    if (e == null) return true;
                    ScCommunity.Tell(_player, string.Format(Localization.Get("scEncContainer"), e.Name), "ui_denied");
                    __result = false;
                    return false;
                }
                catch (Exception ex)
                {
                    ScLog.Once("enclave-container", ex);
                    return true;
                }
            }
        }

        /// <summary>
        /// E2, the user's decision of 2026-10-16: SC's (and XNPC's) wild survivors and raiders never spawn within
        /// EnclaveWildSpawnMargin of a living enclave - they looked like its people. SpawnManagerBiomes.SpawnUpdate picks a
        /// spot (World.GetRandomSpawnPositionInAreaMinMaxToPlayers, its only caller) and then the class
        /// (EntityGroups.GetRandomFromGroup, ending in GetRandomFromGroupList, which ScLoadout already postfixes); a class of 0
        /// makes it skip the spawn (DecMaxCount, no body built). So this prefix and finalizer raise a flag around the update,
        /// the spot postfix notes the spot while it is up, and the group-pick postfix turns an npc-tagged class at a noted spot
        /// on enclave grounds into 0 (ScEnclavePeople.FilterWild). Main thread (the world's tick); the update itself is
        /// untouched.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_BiomeSpawn
        {
            public static MethodBase Target()
            {
                return AccessTools.DeclaredMethod(typeof(SpawnManagerBiomes), "SpawnUpdate", new[] { typeof(string), typeof(bool), typeof(ChunkAreaBiomeSpawnData) });
            }

            [HarmonyPrepare]
            public static bool Prepare()
            {
                try
                {
                    if (Target() != null && Patch_BiomeSpawnSpot.Target() != null) return true;
                    ScLog.Err("SpawnManagerBiomes.SpawnUpdate(string, bool, ChunkAreaBiomeSpawnData) or World.GetRandomSpawnPositionInAreaMinMaxToPlayers not found (game update?) - wild NPCs may spawn on enclave grounds");
                }
                catch (Exception e) { ScLog.Once("enclave-bind-biomespawn", e); }
                return false;
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return Target(); }

            [HarmonyPrefix]
            public static void Prefix() { ScEnclavePeople.BeginBiomeSpawn(); }

            [HarmonyFinalizer]
            public static Exception Finalizer(Exception __exception)
            {
                ScEnclavePeople.EndBiomeSpawn();
                return __exception;
            }
        }

        /// <summary>The spot a biome spawn chose (see Patch_BiomeSpawn): noted only while a SpawnUpdate runs.</summary>
        [HarmonyPatch]
        public static class Patch_BiomeSpawnSpot
        {
            public static MethodBase Target()
            {
                return AccessTools.DeclaredMethod(typeof(World), "GetRandomSpawnPositionInAreaMinMaxToPlayers",
                    new[] { typeof(Rect), typeof(int), typeof(int), typeof(bool), typeof(Vector3).MakeByRefType(), typeof(Chunk).MakeByRefType() });
            }

            [HarmonyPrepare]
            public static bool Prepare() { return Target() != null && Patch_BiomeSpawn.Target() != null; }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return Target(); }

            [HarmonyPostfix]
            public static void Postfix(bool __result, ref Vector3 _position)
            {
                if (__result) ScEnclavePeople.NoteSpawnPosition(_position);
            }
        }
    }
}
