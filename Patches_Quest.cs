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
    /// stay clear (Patches_Enclaves would only clear them again). Round MP2 (E6): a quest in a REMOTE player's journal is out
    /// of the server's sight, so with a remote subscriber the server answers from the registry as well (ScQuests.IsOurPoi).
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
                    // round MP2 (E6): with the subscribers - a remote player's favour POI is held from the server's records
                    if (!outpost && !enclave && !ScQuests.IsOurPoi(__instance.position, __instance.EntityList)) return;
                    __instance.hasRefreshed = true;
                    ScLog.Verbose("suppressed the quest POI re-arm at " + __instance.position + (outpost ? " (our outpost)" : enclave ? " (an enclave's POI)" : " (favour quest)"));
                }
                catch (Exception e)
                {
                    ScLog.Once("sleeper-event-prefix", e);
                }
            }
        }

        /// <summary>
        /// Round r2: SC's rescue quest never locks a building, so closing it never unlocks one. Quest.CloseQuest unlocks the
        /// quest's POIPosition whatever the quest (HandleUnlockPOI -> QuestEventManager.QuestUnlockPOI, which dereferences the
        /// prefab at that spot unchecked): a mining run's rescue marks a natural spot with no prefab, so the close threw half-way
        /// (the 2026-11-15 log), and at a real POI it would take the player off a vanilla quest's lock there. ScQuests.CloseRescue
        /// drops the place before closing; this covers the calls SC does not make - the player removing the quest in the journal
        /// (quest_scRescue has allow_remove), whose RemoveQuest threw the same way and left the quest in the journal, and
        /// QuestEventManager.HandlePlayerDisconnect, which unlocks every quest in progress as the player leaves and would throw
        /// on an open mining rescue, skipping the unlocks and treasure clean-up of every quest after it. Every other quest
        /// unlocks as before.
        /// </summary>
        [HarmonyPatch(typeof(Quest), nameof(Quest.HandleUnlockPOI))]
        public static class Patch_RescueNoUnlock
        {
            [HarmonyPrefix]
            public static bool Prefix(Quest __instance)
            {
                try
                {
                    return !ScQuests.IsRescueQuest(__instance);
                }
                catch (Exception e)
                {
                    ScLog.Once("rescue-no-unlock", e);
                    return true;
                }
            }
        }
    }
}
