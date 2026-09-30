using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// What an ordinary land claim does not already do for an outpost (Outposts_Plan.md 2.7):
    ///   - CheckForAnyPlayerHome: a big POI pokes out of a vanilla 41x41 claim; every sleeper volume in the POI's
    ///     footprint counts as claimed, so none respawns. A postfix, so it also runs after LandClaimUpgrade's
    ///     zone-aware prefix (which skips the original) and only ever adds protection.
    ///   - RemoveExtraLandClaims: placing a claim over LandClaimCount silently deactivates the OLDEST claim, which
    ///     is usually the home. The home and the outposts are moved to the end of the owner's list first, so the
    ///     oldest other claim goes instead.
    ///   - CanPlaceLandProtectionBlockAt: when only the home and outposts are left, a new claim would have to take
    ///     one of their places - it is refused, with a tooltip on a real placement. A keystone is placed by
    ///     BlockToolSelection.ExecuteUseAction on the PRESS (ItemActionPlaceAsBlock is torches and candles), so
    ///     that call stamps the frame; the preview cube calls the gate every frame and never reports.
    /// The quiet ClearSleepers re-arm is Patches_Quest; radio and favour picks skip outposts in ScRadio / ScSecurity.
    /// </summary>
    public static class Patches_Outposts
    {
        private static int placingFrame = -1;
        private static EntityPlayerLocal placingPlayer;
        private static float nextRefusalTip;

        /// <summary>The home claim, or a held outpost's claim: never the one the claim limit drops.</summary>
        public static bool IsProtectedClaim(Vector3i p)
        {
            return (ScZone.HasHome && ScZone.Home == p) || ScOutposts.IsOutpostClaim(p);
        }

        [HarmonyPatch(typeof(GameUtils), nameof(GameUtils.CheckForAnyPlayerHome), new Type[] { typeof(World), typeof(Vector3i), typeof(Vector3i) })]
        public static class Patch_CheckForAnyPlayerHome
        {
            [HarmonyPostfix]
            public static void Postfix(Vector3i BoxMin, Vector3i BoxMax, ref GameUtils.EPlayerHomeType __result)
            {
                try
                {
                    if (__result != GameUtils.EPlayerHomeType.None) return;
                    if (ScOutposts.BoxHitsOutpost(BoxMin, BoxMax)) __result = GameUtils.EPlayerHomeType.Landclaim;
                }
                catch (Exception e)
                {
                    ScLog.Once("outpost-home-postfix", e);
                }
            }
        }

        [HarmonyPatch(typeof(PersistentPlayerList), nameof(PersistentPlayerList.RemoveExtraLandClaims), new Type[] { typeof(PersistentPlayerData) })]
        public static class Patch_RemoveExtraLandClaims
        {
            [HarmonyPrefix]
            public static bool Prefix(PersistentPlayerData owner)
            {
                try
                {
                    if (owner == null || owner.LPBlocks == null || owner.LPBlocks.Count == 0 || !ScRegistry.Loaded) return true;
                    int limit = GameStats.GetInt(EnumGameStats.LandClaimCount);
                    if (owner.LPBlocks.Count <= limit) return true;
                    // PlaceLandProtectionBlock appended the new claim just before calling this (its only caller)
                    Vector3i newest = owner.LPBlocks[owner.LPBlocks.Count - 1];
                    var outposts = new List<Vector3i>();
                    var older = new List<Vector3i>();
                    bool home = false;
                    for (int i = 0; i < owner.LPBlocks.Count - 1; i++)
                    {
                        Vector3i p = owner.LPBlocks[i];
                        if (ScZone.HasHome && ScZone.Home == p) home = true;
                        else if (ScOutposts.IsOutpostClaim(p)) outposts.Add(p);
                        else older.Add(p);
                    }
                    if (!home && outposts.Count == 0) return true;      // nothing of ours at stake: vanilla as is
                    int dropped = owner.LPBlocks.Count - limit;
                    if (older.Count < dropped)
                    {
                        // only reached when the placement gate was bypassed (edit mode, scripted placement): vanilla
                        // would deactivate the home or an outpost - or the new claim, which PlaceLandProtectionBlock
                        // then re-adds to the claim map half-dead. Keep everything, one over the limit.
                        ScLog.Warn("claim limit (" + limit + "): only the home / outposts could make room for the claim at " + newest
                                   + " - keeping every claim this once (" + owner.LPBlocks.Count + " held)");
                        return false;
                    }
                    // vanilla drops LPBlocks[0] first: the older plain claims, then (never reached) the new one, the
                    // outposts, the home last
                    owner.LPBlocks.Clear();
                    owner.LPBlocks.AddRange(older);
                    owner.LPBlocks.Add(newest);
                    owner.LPBlocks.AddRange(outposts);
                    if (home) owner.LPBlocks.Add(ScZone.Home);
                    ScLog.Out("claim limit (" + limit + "): the home and " + outposts.Count + " outpost claim(s) kept; the " + dropped + " oldest other claim(s) go");
                    return true;
                }
                catch (Exception e)
                {
                    ScLog.Once("outpost-claim-limit", e);
                    return true;
                }
            }
        }

        [HarmonyPatch(typeof(World), nameof(World.CanPlaceLandProtectionBlockAt), new Type[] { typeof(Vector3i), typeof(PersistentPlayerData) })]
        public static class Patch_CanPlaceLandProtectionBlockAt
        {
            [HarmonyPostfix]
            public static void Postfix(Vector3i blockPos, PersistentPlayerData lpRelative, ref bool __result)
            {
                try
                {
                    if (!__result || lpRelative == null || lpRelative.LPBlocks == null || !ScRegistry.Loaded) return;
                    if (!ScCommunity.IsHost) return;
                    int limit = GameStats.GetInt(EnumGameStats.LandClaimCount);
                    int needed = lpRelative.LPBlocks.Count + 1 - limit;     // claims the new one would push out
                    if (needed <= 0) return;
                    int others = 0, held = 0;
                    foreach (Vector3i p in lpRelative.LPBlocks) { if (IsProtectedClaim(p)) held++; else others++; }
                    if (held == 0 || others >= needed) return;
                    __result = false;
                    if (placingFrame == Time.frameCount && placingPlayer != null && Time.unscaledTime >= nextRefusalTip)
                    {
                        nextRefusalTip = Time.unscaledTime + 1f;
                        ScCommunity.Tell(placingPlayer, string.Format(Localization.Get("scOpClaimLimit"), limit, ScOutposts.HeldCount()), "ui_denied");
                        placingFrame = -1;
                    }
                }
                catch (Exception e)
                {
                    ScLog.Once("outpost-can-place", e);
                }
            }
        }

        [HarmonyPatch(typeof(BlockToolSelection), nameof(BlockToolSelection.ExecuteUseAction), new Type[] { typeof(ItemInventoryData), typeof(bool), typeof(PlayerActionsLocal) })]
        public static class Patch_BlockToolExecuteUseAction
        {
            [HarmonyPrefix]
            public static void Prefix(ItemInventoryData _data, bool _bReleased)
            {
                placingFrame = -1;
                placingPlayer = null;
                try
                {
                    if (_bReleased || _data == null) return;          // blocks go down on the press
                    EntityPlayerLocal player = _data.holdingEntity as EntityPlayerLocal;
                    if (player == null) return;
                    placingFrame = Time.frameCount;
                    placingPlayer = player;
                }
                catch (Exception e)
                {
                    ScLog.Once("outpost-place-stamp", e);
                }
            }
        }
    }
}
