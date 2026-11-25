using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using SCore.Features.RemoteCrafting.Scripts;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round cfs1 (the user, 2026-11-25): "Every crafting resource in linked storage should be detected while inside the base
    /// (Craft from Storage)." 0-SCore's Craft From Containers (RemoteCraftingUtils) finds ingredients in containers near the
    /// player - within its Distance (30 m by default) and, while its broadcast manager runs, only the boxes switched to
    /// broadcast. Inside the community's home zone SC adds every box linked storage links there (ScStorage.Linkable:
    /// player-placed storage, never the packing station or a land claim block's own storage), wherever it stands in the base.
    /// - One hook: RemoteCraftingUtils.GetTileEntities(EntityAlive), the one-argument overload - 0-SCore's crafting paths and only
    ///   they ask it: the ingredient counts (SearchNearbyContainers(player) and (player, item)) and the taking (ConsumeItem) alike,
    ///   so what is counted is what can be taken. Block upgrades and repairs (they count AND take through the three-argument
    ///   overload) and the drop box keep 0-SCore's own range - hooking the three-argument one would have let a repair count base
    ///   boxes it then does not take from (free materials). The one-argument method is ~50 bytes of IL with a branch: above Mono's
    ///   inlining limit, so the hook cannot be bypassed by an inlined copy.
    /// - 0-SCore's own refusals stand: with an enemy near (BlockOnNearbyEnemies) it returned none, and none are added; a box open by
    ///   another player is skipped by 0-SCore itself (IsLootContainerOpenByAnotherPlayer) when it counts and takes.
    /// - A box locked against the player (a friend's password-locked crate) is left out, as linked storage leaves it out.
    /// - Loaded chunks only (the base around the player is loaded). On a client the zone comes from the community's copy.
    /// - CraftFromBaseStorage=false in the config turns it off.
    /// </summary>
    public static class ScCraftFromBase
    {
        /// <summary>Adds the home zone's linked storage boxes to the list (no duplicates). Main thread.</summary>
        internal static void AddBaseBoxes(EntityPlayer player, List<TileEntity> into)
        {
            World w = ScCommunity.World;
            if (w == null || into == null || !ScZone.HasHome) return;
            var have = new HashSet<TileEntity>(into);
            PlatformUserIdentifierAbs me = ScStorageServer.UserIdOf(player);
            int xMin, xMax, zMin, zMax;
            ScZone.Bounds(out xMin, out xMax, out zMin, out zMax);
            int added = 0;
            for (int cx = World.toChunkXZ(xMin); cx <= World.toChunkXZ(xMax); cx++)
            {
                for (int cz = World.toChunkXZ(zMin); cz <= World.toChunkXZ(zMax); cz++)
                {
                    Chunk chunk = w.GetChunkSync(cx, cz) as Chunk;
                    var tes = chunk != null ? chunk.tileEntities : null;
                    if (tes == null || tes.list == null) continue;
                    for (int i = 0; i < tes.list.Count; i++)
                    {
                        TileEntityComposite tc = tes.list[i] as TileEntityComposite;
                        if (tc == null || have.Contains(tc)) continue;
                        TEFeatureStorage st = tc.GetFeature<TEFeatureStorage>();
                        if (!ScStorage.Linkable(tc, st)) continue;
                        if (!ScZone.Contains(tc.ToWorldPos())) continue;
                        if (ScStorageServer.LockedAgainst(st, me)) continue;
                        into.Add(tc);
                        have.Add(tc);
                        added++;
                    }
                }
            }
            if (added > 0 && ScConfig.Verbose) ScLog.OnceOut("craft-from-base-added", "craft from storage: " + added + " linked storage box(es) of the base added for " + player.PlayerDisplayName + " (logged once)");
        }
    }

    /// <summary>Round cfs1: the hook on 0-SCore's container search (see ScCraftFromBase).</summary>
    [HarmonyPatch]
    public static class Patch_CraftFromBase
    {
        public static MethodBase Target()
        {
            return AccessTools.DeclaredMethod(typeof(RemoteCraftingUtils), "GetTileEntities", new[] { typeof(EntityAlive) });
        }

        [HarmonyPrepare]
        public static bool Prepare()
        {
            if (Target() != null) return true;
            ScLog.Warn("craft from storage: 0-SCore's RemoteCraftingUtils.GetTileEntities(EntityAlive) was not found - the base's linked storage is not added to Craft from Storage");
            return false;
        }

        [HarmonyTargetMethod]
        public static MethodBase TargetMethod() { return Target(); }

        [HarmonyPostfix]
        public static void Postfix(EntityAlive player, List<TileEntity> __result)
        {
            try
            {
                if (__result == null || !ScConfig.CraftFromBaseStorage) return;
                EntityPlayer p = player as EntityPlayer;
                if (p == null || !ScRegistry.Ready || !ScZone.HasHome) return;
                if (!ScZone.Contains(p.GetBlockPosition())) return;
                // 0-SCore's own rule: with an enemy near it returned none - none are added either
                if (RemoteCraftingUtils.CheckEnemyForCrafting() && RemoteCraftingUtils.IsEnemyNearby(player)) return;
                ScCraftFromBase.AddBaseBoxes(p, __result);
            }
            catch (Exception e)
            {
                ScLog.Once("craft-from-base", e);
            }
        }
    }
}
