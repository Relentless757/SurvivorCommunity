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
    /// quietly refill it with sleepers the first time the player walks in.
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
                    if (!outpost && !ScQuests.IsOurPoi(__instance.position)) return;
                    __instance.hasRefreshed = true;
                    ScLog.Verbose("suppressed the quest POI re-arm at " + __instance.position + (outpost ? " (our outpost)" : " (favour quest)"));
                }
                catch (Exception e)
                {
                    ScLog.Once("sleeper-event-prefix", e);
                }
            }
        }
    }
}
