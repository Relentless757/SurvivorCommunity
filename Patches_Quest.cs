using System;
using HarmonyLib;

namespace SurvivorCommunity
{
    /// <summary>
    /// Vanilla re-arms a POI for a clear quest: the first SleeperEventData.Update after a quest subscribes
    /// calls DespawnAndReset on every non-quest-exclude sleeper volume, with no player-home guard. That is
    /// right for a trader quest and wrong for a favour quest next to the player's base, so hasRefreshed is
    /// set before the vanilla body runs - only for POIs one of OUR quests targets. LandClaimUpgrade carries
    /// the same prefix for its own quests; both check their own journals, so they coexist. A POI held as an
    /// outpost is never re-armed either: a trader quest accepted there before it was established would otherwise
    /// quietly refill it with sleepers the first time the player walks in. Nor is an enclave's POI (E1): its rooms
    /// stay clear (Patches_Enclaves would only clear them again).
    /// </summary>
    public static class Patches_Quest
    {
        [HarmonyPatch(typeof(SleeperEventData), nameof(SleeperEventData.Update))]
        public static class Patch_SleeperEventDataUpdate
        {
            [HarmonyPrefix]
            public static void Prefix(SleeperEventData __instance)
            {
                try
                {
                    if (__instance == null || __instance.hasRefreshed) return;
                    bool outpost = ScOutposts.IsOutpostPoi(__instance.position);
                    bool enclave = !outpost && ScEnclaves.IsEnclavePoi(__instance.position);
                    if (!outpost && !enclave && !ScQuests.IsOurPoi(__instance.position)) return;
                    __instance.hasRefreshed = true;
                    ScLog.Verbose("suppressed the quest POI re-arm at " + __instance.position + (outpost ? " (our outpost)" : enclave ? " (an enclave's POI)" : " (favour quest)"));
                }
                catch (Exception e)
                {
                    ScLog.Once("sleeper-event-prefix", e);
                }
            }
        }
    }
}
