using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using SCore.Features.RemoteCrafting.Scripts;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round cfs1 (the user, 2026-11-25): "Every crafting resource in linked storage should be detected while inside the base
    /// (Craft from Storage)." 0-SCore's Craft From Containers (RemoteCraftingUtils) finds ingredients in containers near the
    /// player - within its Distance (10 m as 0-SCore ships its blocks.xml) and, while its broadcast manager runs, only the boxes
    /// switched to broadcast. Inside the community's home zone SC adds every box linked storage links there (ScStorage.Linkable:
    /// player-placed storage, never the packing station or a land claim block's own storage), wherever it stands in the base.
    /// - One hook: RemoteCraftingUtils.GetTileEntities(EntityAlive), the one-argument overload - 0-SCore's crafting paths and only
    ///   they ask it: the ingredient counts (SearchNearbyContainers(player) and (player, item)) and the taking (ConsumeItem) alike,
    ///   so what is counted is what can be taken. Block upgrades and repairs (they count AND take through the three-argument
    ///   overload) and the drop box keep 0-SCore's own range - hooking the three-argument one would have let a repair count base
    ///   boxes it then does not take from (free materials). The one-argument method is ~50 bytes of IL with a branch: above Mono's
    ///   inlining limit, so the hook cannot be bypassed by an inlined copy.
    /// - 0-SCore's own refusals stand: with an enemy near (BlockOnNearbyEnemies) it returned none, and none are added.
    /// - A box someone has open: on the server (the host's own crafting) 0-SCore skips it itself when it counts and takes
    ///   (IsLootContainerOpenByAnotherPlayer, from the server's LockManager). On a client that test is always false - a client
    ///   cannot see another player's open - so (round MP4 review cfs1 M1) the server sends its clients the home zone's open boxes
    ///   (NetPackageScOpenBoxes, at most once a second) and a client leaves those out, 0-SCore's own near ones included. Without
    ///   it the client's craft would overwrite the box on the server and the player with it open would write their old copy
    ///   back on close: the materials came back. A one-second window remains (an open the client has not heard of yet).
    /// - A client adds only boxes within ClientRange (its copy of a box farther off is not kept current by the game).
    /// - A box locked against the player (a friend's password-locked crate) is left out, as linked storage leaves it out.
    /// - 0-SCore's server options are kept for the base's boxes too - LandClaimPlayerOnly (the player must stand in their own
    ///   claim), LandClaimContainersOnly (only boxes in their own claim) and disablesender (excluded box names). The workstation
    ///   options (nottoWorkstation, bindtoWorkstation) depend on the open workstation and are not re-applied: the base's boxes
    ///   count for every station (DEVELOPER.md). Its broadcast switch is not either: every linked box in the base counts.
    /// - SC's own takes from the player's bag (ScItemTx, ScCategories, ScTalkContext) run vanilla's RemoveItems, which 0-SCore
    ///   turns into ConsumeItem: inside SuppressContainers they see no container at all (review cfs1 m5) - SC plans them from the
    ///   bag and toolbelt alone.
    /// - Loaded chunks only (the base around the player is loaded). On a client the zone comes from the community's copy.
    /// - The list is worked out once a frame per player (0-SCore asks many times in one crafting window refresh).
    /// - CraftFromBaseStorage=false in the config turns it off.
    /// </summary>
    public static class ScCraftFromBase
    {
        /// <summary>Round MP4 review cfs1 m1: a client adds no base box farther than this (meters, XZ) - the game keeps a client's copy of a box current within 192 m.</summary>
        private const float ClientRange = 150f;

        /// <summary>Round MP4 review cfs1 m5: above 0, 0-SCore's container search returns none (SC's own takes from the bag).</summary>
        internal static int SuppressDepth;

        // review cfs1 n8: the base's boxes for one player, once a frame
        private static readonly List<TileEntity> cache = new List<TileEntity>();
        private static int cacheFrame = -1, cachePlayer = -1;

        // review cfs1 M1, client: the home zone's boxes open on the server (NetPackageScOpenBoxes)
        private static readonly HashSet<Vector3i> openOnServer = new HashSet<Vector3i>();

        // review cfs1 M1, server: the set sent last, and the clients that have it
        private static readonly HashSet<Vector3i> openNow = new HashSet<Vector3i>();
        private static readonly HashSet<Vector3i> openSent = new HashSet<Vector3i>();
        private static readonly HashSet<int> haveSet = new HashSet<int>();
        private static readonly List<ClientInfo> targets = new List<ClientInfo>();

        /// <summary>A new world: nothing carries over.</summary>
        public static void Reset()
        {
            cache.Clear();
            cacheFrame = -1;
            cachePlayer = -1;
            openOnServer.Clear();
            openNow.Clear();
            openSent.Clear();
            haveSet.Clear();
            targets.Clear();
            SuppressDepth = 0;
        }

        /// <summary>Round MP4 review cfs1 m5: runs take with 0-SCore's containers out of reach (SC's own takes from the bag and toolbelt).</summary>
        internal static void SuppressContainers(Action take)
        {
            SuppressDepth++;
            try { take(); }
            finally { SuppressDepth--; }
        }

        /// <summary>Round MP4 review cfs1 M1, client: whether the server says this box is open (by anyone).</summary>
        internal static bool OpenOnServer(Vector3i pos)
        {
            return openOnServer.Count > 0 && openOnServer.Contains(pos);
        }

        /// <summary>Round MP4 review cfs1 M1, client: the server's open boxes (NetPackageScOpenBoxes: the whole set). Main thread.</summary>
        internal static void ReceiveOpen(int community, Vector3i[] boxes)
        {
            if (ScCommunity.IsHost) return;
            openOnServer.Clear();
            if (boxes != null) for (int i = 0; i < boxes.Length; i++) openOnServer.Add(boxes[i]);
            cacheFrame = -1;
            if (ScConfig.Verbose) ScLog.Verbose("craft from storage: the server says " + openOnServer.Count + " box(es) of the base are open");
        }

        /// <summary>
        /// Round MP4 review cfs1 M1, server (ScTicker, once a second): the home zone's linked storage boxes someone has open, to every
        /// remote client with SC's protocol - the whole set when it changed, and to a client that has not had it. Nothing is
        /// built with no such client (single player and a host alone are untouched).
        /// </summary>
        public static void ServerTick()
        {
            try
            {
                if (!ScCommunity.IsHost) return;
                targets.Clear();
                ConnectionManager cm = ScNet.Cm;
                if (cm != null && cm.Clients != null)
                    foreach (ClientInfo ci in cm.Clients.List)
                        if (ci != null && ci.entityId != -1 && ScNet.ClientMatches(ci.entityId)) targets.Add(ci);
                if (targets.Count == 0) { haveSet.Clear(); openSent.Clear(); return; }
                openNow.Clear();
                if (ScZone.HasHome)
                {
                    World w = ScCommunity.World;
                    ForEachBaseBox(w, (tc, st, pos) => { if (openNow.Count < NetPackageScOpenBoxes.MaxBoxes && ScStorageServer.OpenBySomeone(st)) openNow.Add(pos); });
                }
                if (!openNow.SetEquals(openSent))
                {
                    openSent.Clear();
                    openSent.UnionWith(openNow);
                    haveSet.Clear();
                }
                // forget clients gone (a returning one is sent the set again)
                if (haveSet.Count > 0)
                {
                    var still = new HashSet<int>();
                    for (int i = 0; i < targets.Count; i++) still.Add(targets[i].entityId);
                    haveSet.IntersectWith(still);
                }
                for (int i = 0; i < targets.Count; i++)
                {
                    ClientInfo ci = targets[i];
                    if (haveSet.Contains(ci.entityId)) continue;
                    ci.SendPackage(NetPackageManager.GetPackage<NetPackageScOpenBoxes>().Setup(ScPlayers.SharedCommunity, openSent));
                    haveSet.Add(ci.entityId);
                }
            }
            catch (Exception e)
            {
                ScLog.Once("craft-from-base-open", e);
            }
        }

        /// <summary>Every linked storage box inside the home zone in loaded chunks. Main thread.</summary>
        private static void ForEachBaseBox(World w, Action<TileEntityComposite, TEFeatureStorage, Vector3i> each)
        {
            if (w == null || !ScZone.HasHome) return;
            int xMin, xMax, zMin, zMax;
            ScZone.Bounds(out xMin, out xMax, out zMin, out zMax);
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
                        if (tc == null) continue;
                        TEFeatureStorage st = tc.GetFeature<TEFeatureStorage>();
                        if (!ScStorage.Linkable(tc, st)) continue;
                        Vector3i pos = tc.ToWorldPos();
                        if (!ScZone.Contains(pos)) continue;
                        each(tc, st, pos);
                    }
                }
            }
        }

        /// <summary>Adds the home zone's linked storage boxes to the list (no duplicates). Main thread.</summary>
        internal static void AddBaseBoxes(EntityPlayer player, List<TileEntity> into)
        {
            World w = ScCommunity.World;
            if (w == null || into == null || player == null || !ScZone.HasHome) return;
            if (cacheFrame != Time.frameCount || cachePlayer != player.entityId)
            {
                cache.Clear();
                cacheFrame = Time.frameCount;
                cachePlayer = player.entityId;
                Fill(w, player, cache);
            }
            if (cache.Count == 0) return;
            var have = new HashSet<TileEntity>(into);
            int added = 0;
            for (int i = 0; i < cache.Count; i++)
            {
                TileEntity te = cache[i];
                if (te == null || te.IsRemoving || !have.Add(te)) continue;
                into.Add(te);
                added++;
            }
            if (added > 0 && ScConfig.Verbose) ScLog.OnceOut("craft-from-base-added", "craft from storage: " + added + " linked storage box(es) of the base added for " + player.PlayerDisplayName + " (logged once)");
        }

        /// <summary>The base's boxes this player may craft from now (AddBaseBoxes' list for the frame).</summary>
        private static void Fill(World w, EntityPlayer player, List<TileEntity> into)
        {
            PlatformUserIdentifierAbs me = ScStorageServer.UserIdOf(player);
            // round cfs1 (review MINOR): 0-SCore's own server options, as its GetTileEntities applies them
            bool claimContainersOnly = RemoteCraftingUtils.CheckForLandClaimContainers();
            PersistentPlayerData ppd = claimContainersOnly && w.GetGameManager() != null ? w.GetGameManager().GetPersistentLocalPlayer() : null;
            if (claimContainersOnly && ppd == null) return;
            string senders = Configuration.GetPropertyValue("AdvancedRecipes", "disablesender");
            string[] disabled = string.IsNullOrEmpty(senders) ? null : senders.Split(',');
            bool client = !ScCommunity.IsHost;
            Vector2 at = new Vector2(player.position.x, player.position.z);
            float range2 = ClientRange * ClientRange;
            ForEachBaseBox(w, (tc, st, pos) =>
            {
                if (client)
                {
                    // review cfs1 M1 and m1: not a box someone has open (the server's word), not one beyond the client's current copy
                    if (OpenOnServer(pos)) return;
                    if ((new Vector2(pos.x + 0.5f, pos.z + 0.5f) - at).sqrMagnitude > range2) return;
                }
                if (ScStorageServer.LockedAgainst(st, me)) return;
                if (claimContainersOnly && !w.IsMyLandProtectedBlock(pos, ppd)) return;
                if (disabled != null && RemoteCraftingUtils.DisableSender(disabled, tc)) return;
                into.Add(tc);
            });
        }

        /// <summary>Round MP4 review cfs1 M1, client: 0-SCore's own near boxes that are open on the server leave its list too.</summary>
        internal static void DropOpenOnServer(List<TileEntity> list)
        {
            if (list == null || openOnServer.Count == 0 || ScCommunity.IsHost) return;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                TileEntity te = list[i];
                if (te != null && OpenOnServer(te.ToWorldPos())) list.RemoveAt(i);
            }
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
                if (__result == null) return;
                // review cfs1 m5: SC's own take from the bag - no container at all
                if (ScCraftFromBase.SuppressDepth > 0) { __result.Clear(); return; }
                // review cfs1 M1: on a client, never a box the server says is open (0-SCore's own near ones too)
                ScCraftFromBase.DropOpenOnServer(__result);
                if (!ScConfig.CraftFromBaseStorage) return;
                EntityPlayer p = player as EntityPlayer;
                if (p == null || !ScRegistry.Ready || !ScZone.HasHome) return;
                if (!ScZone.Contains(p.GetBlockPosition())) return;
                // 0-SCore's own rules: with an enemy near it returned none - none are added either; LandClaimPlayerOnly: only in the
                // player's own claim (the review's MINOR)
                if (RemoteCraftingUtils.CheckEnemyForCrafting() && RemoteCraftingUtils.IsEnemyNearby(player)) return;
                if (RemoteCraftingUtils.CheckForLandClaimPlayer())
                {
                    World w = ScCommunity.World;
                    PersistentPlayerData ppd = w != null && w.GetGameManager() != null ? w.GetGameManager().GetPersistentLocalPlayer() : null;
                    if (ppd == null || !w.IsMyLandProtectedBlock(new Vector3i(p.GetPosition()), ppd)) return;
                }
                ScCraftFromBase.AddBaseBoxes(p, __result);
            }
            catch (Exception e)
            {
                ScLog.Once("craft-from-base", e);
            }
        }
    }
}
