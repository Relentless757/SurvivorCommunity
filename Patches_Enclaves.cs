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
    /// E3 adds the leader's trade: prices by standing (Patch_TradeBuyPrice, Patch_TradeSellPrice) and the stock recorded when
    /// a trade window with them closes (Patch_TradeClosed).
    /// E4 adds the gun pick's row texts in the leader's talk (Patch_DialogRowText).
    /// </summary>
    public static class Patches_Enclaves
    {
        /// <summary>
        /// E3: XUiM_Trader.GetBuyPrice - what a trader charges, for the window's labels and the purchase alike
        /// (ItemActionEntryPurchase). Scaled by an enclave leader's standing at the outermost call only: the price of an item
        /// with mods adds its mods' prices through the same method (ScEnclaveDeals.ScalePrice; the prefix and finalizer keep
        /// the depth). Every other trader passes after one cvar read. Main thread (the UI).
        /// </summary>
        [HarmonyPatch]
        public static class Patch_TradeBuyPrice
        {
            public static MethodBase Target()
            {
                return AccessTools.DeclaredMethod(typeof(XUiM_Trader), "GetBuyPrice", new[] { typeof(XUi), typeof(ItemValue), typeof(int), typeof(ItemClass), typeof(int) });
            }

            [HarmonyPrepare]
            public static bool Prepare()
            {
                try
                {
                    if (Target() != null) return true;
                    ScLog.Err("XUiM_Trader.GetBuyPrice(XUi, ItemValue, int, ItemClass, int) not found (game update?) - enclave leaders charge vanilla prices at every standing");
                }
                catch (Exception e) { ScLog.Once("enclave-bind-buyprice", e); }
                return false;
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return Target(); }

            [HarmonyPrefix]
            public static void Prefix() { ScEnclaveDeals.PriceEnter(); }

            [HarmonyPostfix]
            public static void Postfix(XUi _xui, ref int __result)
            {
                try { ScEnclaveDeals.ScalePrice(_xui, true, ref __result); }
                catch (Exception e) { ScLog.Once("enclave-buyprice", e); }
            }

            [HarmonyFinalizer]
            public static Exception Finalizer(Exception __exception)
            {
                ScEnclaveDeals.PriceLeave();
                return __exception;
            }
        }

        /// <summary>E3: XUiM_Trader.GetSellPrice - what a trader pays (ItemActionEntrySell); as Patch_TradeBuyPrice.</summary>
        [HarmonyPatch]
        public static class Patch_TradeSellPrice
        {
            public static MethodBase Target()
            {
                return AccessTools.DeclaredMethod(typeof(XUiM_Trader), "GetSellPrice", new[] { typeof(XUi), typeof(ItemValue), typeof(int), typeof(ItemClass) });
            }

            [HarmonyPrepare]
            public static bool Prepare()
            {
                try
                {
                    if (Target() != null) return true;
                    ScLog.Err("XUiM_Trader.GetSellPrice(XUi, ItemValue, int, ItemClass) not found (game update?) - enclave leaders pay vanilla prices at every standing");
                }
                catch (Exception e) { ScLog.Once("enclave-bind-sellprice", e); }
                return false;
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return Target(); }

            [HarmonyPrefix]
            public static void Prefix() { ScEnclaveDeals.PriceEnter(); }

            [HarmonyPostfix]
            public static void Postfix(XUi _xui, ref int __result)
            {
                try { ScEnclaveDeals.ScalePrice(_xui, false, ref __result); }
                catch (Exception e) { ScLog.Once("enclave-sellprice", e); }
            }

            [HarmonyFinalizer]
            public static Exception Finalizer(Exception __exception)
            {
                ScEnclaveDeals.PriceLeave();
                return __exception;
            }
        }

        /// <summary>
        /// E3: EntityTrader.OnUnlockedServer - the lock on channel 1 (the trade window's) comes off when the window closes, after
        /// the trade wrote into the entity's TraderData (vanilla's own SetModified there): an enclave leader's stock goes into
        /// their enclave's record. No SCore or XNPC class overrides it. Host, main thread; anyone else passes after a channel test
        /// and one cvar read.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_TradeClosed
        {
            public static MethodBase Target()
            {
                return AccessTools.DeclaredMethod(typeof(EntityTrader), "OnUnlockedServer", new[] { typeof(int), typeof(ushort) });
            }

            [HarmonyPrepare]
            public static bool Prepare()
            {
                try
                {
                    if (Target() != null) return true;
                    ScLog.Err("EntityTrader.OnUnlockedServer(int, ushort) not found (game update?) - an enclave leader's stock is recorded only when their body goes");
                }
                catch (Exception e) { ScLog.Once("enclave-bind-tradeclosed", e); }
                return false;
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return Target(); }

            [HarmonyPostfix]
            public static void Postfix(EntityTrader __instance, ushort _channel)
            {
                try { ScEnclaveDeals.OnTradeClosed(__instance, _channel); }
                catch (Exception e) { ScLog.Once("enclave-tradeclosed", e); }
            }
        }

        /// <summary>
        /// E4, the gun pick's rows (scEncReqGun0..3): a response's Text is the shared, parsed-once "{gun}", so each row's words
        /// ("Pistol (T1, quality 3): small thanks") come through the entry's "response" binding instead - never written into the
        /// dialog. XUiC_DialogResponseEntry declares its own override; no installed mod patches it (E4_REPORT.md). It runs every
        /// frame for every visible row: a first-character test, then the binding name, then (ours only) a list read.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_DialogRowText
        {
            public static MethodBase Target()
            {
                return AccessTools.DeclaredMethod(typeof(XUiC_DialogResponseEntry), "GetBindingValueInternal", new[] { typeof(string).MakeByRefType(), typeof(string) });
            }

            [HarmonyPrepare]
            public static bool Prepare()
            {
                try
                {
                    if (Target() != null) return true;
                    ScLog.Err("XUiC_DialogResponseEntry.GetBindingValueInternal(ref string, string) not found (game update?) - an enclave's gun pick shows no names");
                }
                catch (Exception e) { ScLog.Once("enclave-bind-rowtext", e); }
                return false;
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return Target(); }

            [HarmonyPostfix]
            public static void Postfix(XUiC_DialogResponseEntry __instance, ref string value, string bindingName)
            {
                if (value == null || value.Length < 3 || value[0] != '{' || bindingName != "response") return;
                try { ScEncRequests.FillRow(__instance, ref value); }
                catch (Exception e) { ScLog.Once("enclave-rowtext", e); }
            }
        }
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
#if SC_V34

        /// <summary>
        /// 3.4 (decision D4): SleeperVolume.TouchGroup(World, EntityPlayer, bool), the group touch every touch of a room goes
        /// through - the player's (CheckTouching) and, since 0-XNPCCore 3.4, any of its NPC bodies' (its 1 Hz sleeper trigger,
        /// AvatarSdcsNpcController.SdcsSleeperVolumeTouch, calls it directly with a nearby player for the spawn and with
        /// none to wake the passive sleepers).
        /// </summary>
        public static MethodBase TouchGroupTarget()
        {
            return AccessTools.DeclaredMethod(typeof(SleeperVolume), "TouchGroup", new[] { typeof(World), typeof(EntityPlayer), typeof(bool) });
        }

        /// <summary>
        /// 3.4 (D4): the community's and 0-XNPCCore's NPC bodies may trigger sleeper rooms everywhere but in an enclave's POI,
        /// which stays sleeper-free (E1). A spawn touch only marks the room for the next tick, whose UpdatePlayerTouched E1
        /// refuses above - but the wake touch rouses whatever the room's spawn map holds at once, and an NPC standing in a
        /// room of the POI touches it every second. So a touch of an enclave POI's room keeps it cleared here, as E1 does,
        /// and goes no further, whoever touched it (E1 kept the player's touch from doing anything else already).
        /// Infested POIs are not enclaves: their rooms spawn through the same UpdatePlayerTouched, where Patches_Infestations
        /// shapes them whoever touched them. Counted (sc xnpc) only when it mattered, not for every repeat touch of a player
        /// standing in a room: enclave-sleeper-touch-cleared (the room was not clear yet), enclave-sleeper-wake-refused (a
        /// touch with no player - 0-XNPCCore's wake).
        /// </summary>
        [HarmonyPatch]
        public static class Patch_TouchGroup
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                try
                {
                    if (TouchGroupTarget() != null) return true;
                    ScLog.Err("SleeperVolume.TouchGroup(World, EntityPlayer, bool) not found (game update?) - an NPC can wake sleepers in an enclave's POI");
                }
                catch (Exception e) { ScLog.Once("enclave-bind-touchgroup", e); }
                return false;
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return TouchGroupTarget(); }

            [HarmonyPrefix]
            public static bool Prefix(SleeperVolume __instance, World _world, EntityPlayer _player)
            {
                try
                {
                    if (__instance == null || !ScEnclaves.IsEnclavePoi(__instance.PrefabInstance)) return true;
                    bool open = !__instance.wasCleared || __instance.respawnTime != ulong.MaxValue || __instance.respawnMap.Count > 0 || __instance.isSpawning;
                    ScEnclaves.KeepCleared(__instance, _world);
                    if (open) Patches_Xnpc.Count("enclave-sleeper-touch-cleared");
                    if (_player == null) Patches_Xnpc.Count("enclave-sleeper-wake-refused");
                    return false;
                }
                catch (Exception e)
                {
                    ScLog.Once("enclave-sleeper-touchgroup", e);
                    return true;
                }
            }
        }
#endif

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
#if SC_V34
                    if (ScStorage.IsPlayerStorage(__instance) || (__instance.Parent != null && __instance.Parent.PlayerPlaced)) return true;
#else
                    if (__instance.bPlayerStorage || (__instance.Parent != null && __instance.Parent.PlayerPlaced)) return true;
#endif
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
