using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;

namespace SurvivorCommunity
{
    /// <summary>
    /// "Secure NPD 3rd Division" instead of "Secure the area", and the POI marker for the favour quest.
    ///
    /// A quest's title is not per instance in vanilla: every renderer reads QuestClass.Name / SubTitle. Each
    /// vanilla method that reads them runs with the shared class's Name and SubTitle temporarily swapped to
    /// the instance title - a prefix swaps, a finalizer restores (on an exception too) and the swap record
    /// travels in __state. LandClaimUpgrade carries the same patches for ITS quest; the state locals are
    /// keyed per patch class and each swap only fires for its own quest id, so both coexist.
    /// </summary>
    [HarmonyPatch]
    public static class Patches_ScQuestTitle
    {
        private const ArgumentType Ref = ArgumentType.Ref, Normal = ArgumentType.Normal;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(XUiC_QuestEntry), "GetBindingValueInternal", new Type[] { typeof(string), typeof(string) }, new ArgumentType[] { Ref, Normal })]
        public static void Prefix_QuestEntry(Quest ___quest, out ScQuests.TitleSwap __state) { __state = ScQuests.TitleSwap.Begin(___quest); }

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(XUiC_QuestEntry), "GetBindingValueInternal", new Type[] { typeof(string), typeof(string) }, new ArgumentType[] { Ref, Normal })]
        public static void Finalizer_QuestEntry(ScQuests.TitleSwap __state) { ScQuests.TitleSwap.End(__state); }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(XUiC_QuestTrackerWindow), "GetBindingValueInternal", new Type[] { typeof(string), typeof(string) }, new ArgumentType[] { Ref, Normal })]
        public static void Prefix_Tracker(Quest ___currentQuest, out ScQuests.TitleSwap __state) { __state = ScQuests.TitleSwap.Begin(___currentQuest); }

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(XUiC_QuestTrackerWindow), "GetBindingValueInternal", new Type[] { typeof(string), typeof(string) }, new ArgumentType[] { Ref, Normal })]
        public static void Finalizer_Tracker(ScQuests.TitleSwap __state) { ScQuests.TitleSwap.End(__state); }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(XUiC_QuestDescriptionWindow), "GetBindingValueInternal", new Type[] { typeof(string), typeof(string) }, new ArgumentType[] { Ref, Normal })]
        public static void Prefix_Description(Quest ___currentQuest, out ScQuests.TitleSwap __state) { __state = ScQuests.TitleSwap.Begin(___currentQuest); }

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(XUiC_QuestDescriptionWindow), "GetBindingValueInternal", new Type[] { typeof(string), typeof(string) }, new ArgumentType[] { Ref, Normal })]
        public static void Finalizer_Description(ScQuests.TitleSwap __state) { ScQuests.TitleSwap.End(__state); }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(XUiC_QuestObjectivesWindow), "GetBindingValueInternal", new Type[] { typeof(string), typeof(string) }, new ArgumentType[] { Ref, Normal })]
        public static void Prefix_Objectives(Quest ___currentQuest, out ScQuests.TitleSwap __state) { __state = ScQuests.TitleSwap.Begin(___currentQuest); }

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(XUiC_QuestObjectivesWindow), "GetBindingValueInternal", new Type[] { typeof(string), typeof(string) }, new ArgumentType[] { Ref, Normal })]
        public static void Finalizer_Objectives(ScQuests.TitleSwap __state) { ScQuests.TitleSwap.End(__state); }

        // "Quest started: ..." toast, and the POI marker once the quest is fully started (add + load path)
        // (3.4: StartQuest(Quest.QuestSource, bool, bool) - the source is new; these bind __instance / __state only. Optional
        // there: a later signature change skips these three, not the whole class - ModApi's MISSING PATCH line still names it)
        [HarmonyPrefix]
#if SC_V34
        [HarmonyOptional]
        [HarmonyPatch(typeof(Quest), nameof(Quest.StartQuest), new Type[] { typeof(Quest.QuestSource), typeof(bool), typeof(bool) })]
#else
        [HarmonyPatch(typeof(Quest), nameof(Quest.StartQuest), new Type[] { typeof(bool), typeof(bool) })]
#endif
        public static void Prefix_StartQuest(Quest __instance, out ScQuests.TitleSwap __state) { __state = ScQuests.TitleSwap.Begin(__instance); }

        [HarmonyFinalizer]
#if SC_V34
        [HarmonyOptional]
        [HarmonyPatch(typeof(Quest), nameof(Quest.StartQuest), new Type[] { typeof(Quest.QuestSource), typeof(bool), typeof(bool) })]
#else
        [HarmonyPatch(typeof(Quest), nameof(Quest.StartQuest), new Type[] { typeof(bool), typeof(bool) })]
#endif
        public static void Finalizer_StartQuest(ScQuests.TitleSwap __state) { ScQuests.TitleSwap.End(__state); }

        [HarmonyPostfix]
#if SC_V34
        [HarmonyOptional]
        [HarmonyPatch(typeof(Quest), nameof(Quest.StartQuest), new Type[] { typeof(Quest.QuestSource), typeof(bool), typeof(bool) })]
#else
        [HarmonyPatch(typeof(Quest), nameof(Quest.StartQuest), new Type[] { typeof(bool), typeof(bool) })]
#endif
        public static void Postfix_StartQuest(Quest __instance) { ScQuests.EnsureMapObject(__instance); }

        // "Quest completed / failed: ..." toast
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Quest), nameof(Quest.CloseQuest), new Type[] { typeof(Quest.QuestState), typeof(List<BaseReward>) })]
        public static void Prefix_CloseQuest(Quest __instance, out ScQuests.TitleSwap __state) { __state = ScQuests.TitleSwap.Begin(__instance); }

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(Quest), nameof(Quest.CloseQuest), new Type[] { typeof(Quest.QuestState), typeof(List<BaseReward>) })]
        public static void Finalizer_CloseQuest(ScQuests.TitleSwap __state) { ScQuests.TitleSwap.End(__state); }

        // journal search box + sort: vanilla reads the class name inside a LINQ lambda; redo that rebuild
        // with the instance title on the frames vanilla rebuilt it
        [HarmonyPrefix]
        [HarmonyPatch(typeof(XUiC_QuestListWindow), nameof(XUiC_QuestListWindow.Update), new Type[] { typeof(float) })]
        public static void Prefix_QuestListUpdate(XUiC_QuestListWindow __instance, out bool __state) { __state = __instance != null && __instance.IsDirty; }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(XUiC_QuestListWindow), nameof(XUiC_QuestListWindow.Update), new Type[] { typeof(float) })]
        public static void Postfix_QuestListUpdate(XUiC_QuestListWindow __instance, bool __state)
        {
            try
            {
                if (!__state || __instance == null) return;
                EntityPlayerLocal player = __instance.player;
                if (player == null || player.QuestJournal == null || player.QuestJournal.quests == null) return;
                List<Quest> quests = player.QuestJournal.quests;
                bool anyTitled = false;
                for (int i = 0; i < quests.Count && !anyTitled; i++) anyTitled = ScQuests.TitleFor(quests[i]) != null;
                if (!anyTitled) return;
                string filter = __instance.filterText ?? "";
                var items = quests
                    .Where(q => q != null && (filter.Length == 0 || ListTitle(q).ToLower().Contains(filter)))
                    .OrderBy(q => !q.Active)
                    .ThenByDescending(q => q.FinishTime)
                    .ThenBy(q => ListTitle(q), StringComparer.CurrentCulture)
                    .ToList();
                __instance.currentItems = items;
                if (__instance.questList != null) __instance.questList.SetQuestList(items);
            }
            catch (Exception e)
            {
                ScLog.Once("Postfix_QuestListUpdate", e);
            }
        }

        private static string ListTitle(Quest q)
        {
            string t = ScQuests.TitleFor(q);
            if (t != null) return t;
            QuestClass c = QuestClass.GetQuest(q.ID);
            return (c != null && c.Name != null) ? c.Name : "";
        }
    }

#if SC_V34
    /// <summary>
    /// Round n11b: the player file must always save. 3.4's Quest.Write writes QuestUniqueId for every quest in progress and
    /// each data variable's value; a null one throws in PooledBinaryWriter.Write(string), PlayerDataFile.Save then fails as a
    /// whole ("ERR Save PlayerData file: Value cannot be null"), at every autosave, for as long as that quest is in the
    /// journal - nothing of the player is saved. Vanilla fills the id only in SetupQuestCode, and only for a quest whose
    /// QuestCode is still 0: SC's enclave request quests set their own code first (fixed in ScQuests.StartRequest), and any
    /// other mod could do the same. So right before a quest is written (PlayerDataFile writes a clone of the journal), a missing id gets the value vanilla itself gives an
    /// older save's quest when it reads one (Quest.Read: QuestCode.ToString()), and a null data value an empty string.
    /// Its own class: a failure here never takes the title patches above with it.
    /// </summary>
    [HarmonyPatch]
    public static class Patches_ScQuestSave
    {
        public static System.Reflection.MethodBase WriteTarget()
        {
            return AccessTools.Method(typeof(Quest), nameof(Quest.Write), new Type[] { typeof(PooledBinaryWriter) });
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Quest), nameof(Quest.Write), new Type[] { typeof(PooledBinaryWriter) })]
        public static void Prefix_Write(Quest __instance)
        {
            try
            {
                if (__instance == null) return;
                // only a quest in progress writes the id (Quest.Write); Quest.Read leaves a finished one's null - nothing to fix there
                if (__instance.CurrentState == Quest.QuestState.InProgress && __instance.QuestUniqueId == null)
                {
                    __instance.QuestUniqueId = __instance.QuestCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    ScLog.Once("quest-uid-" + __instance.ID, "quest " + __instance.ID + " had no unique id (code " + __instance.QuestCode
                               + ") - given one, so the player file can save");
                }
                Dictionary<string, string> vars = __instance.DataVariables;
                if (vars != null && vars.ContainsValue(null))
                {
                    foreach (string k in vars.Where(kv => kv.Value == null).Select(kv => kv.Key).ToList()) vars[k] = "";
                    ScLog.Once("quest-var-" + __instance.ID, "quest " + __instance.ID + " had an empty data value - saved as \"\"");
                }
            }
            catch (Exception e)
            {
                ScLog.Once("quest-write-guard", e);
            }
        }
    }
#endif
}
