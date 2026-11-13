using System;
using System.Collections.Generic;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// The "secure this POI for me" favour quest (Config/quests.xml, quest_scSecurePoi). The favour completes
    /// only through it: its clearing phase (SyncCompleted), or - when the POI already reads clear as the player
    /// takes it - the player's "Actually, I've already cleared that." in the dialog (DialogActionScFavourPoiCleared,
    /// which closes the quest as completed). Clearing the POI without the quest completes nothing.
    /// No RallyPoint / Goto objective on purpose - the rally point is the only vanilla path to World.ResetPOIS
    /// (which wipes anything built there), and the quieter reset ObjectiveClearSleepers performs on subscribe is
    /// suppressed by Patches_Quest for the POIs of these quests only. Position data must be set BEFORE
    /// QuestJournal.AddQuest (the objectives read it in AddHooks).
    /// </summary>
    public static class ScQuests
    {
        public const string QuestId = "quest_scSecurePoi";
        public const string RescueQuestId = "quest_scRescue";
        /// <summary>The supplies favour ("Sure, I'll see what I can find."): one objective, closed by the hand-over.</summary>
        public const string SuppliesQuestId = "quest_scSupplies";
        public const string TitleVar = "ScTitle";
        public const string MemberVar = "ScMember";
        public const string SuppliesVar = "ScSupplies";
        /// <summary>E3: the enclave person a favour quest belongs to (their prospect's giver id follows each new body).</summary>
        public const string EncPersonVar = "ScEncPerson";
        /// <summary>
        /// E4: an enclave's request taken on (ScEncRequests.Accept): the request's id, what is wanted, the enclave's name and the
        /// last day (the objective's line), its marker on their building (POIPosition/POISize: EnsureMapObject).
        /// </summary>
        public const string RequestQuestId = "quest_scEncRequest";
        public const string RequestVar = "ScEncRequest", RequestWhatVar = "ScEncWhat", RequestNameVar = "ScEncName", RequestDueVar = "ScEncDue";
        /// <summary>
        /// E5's request quests, one class per kind (E5 kinds have their own objectives): a missing survivor (phase 1 the search,
        /// phase 2 "Return to the enclave" once the remains were searched), an infestation to clear, a siege (a live countdown).
        /// The person's name and the search's stage word ride in RequestWhoVar and RequestStageVar.
        /// </summary>
        public const string SearchQuestId = "quest_scEncSearch", InfestQuestId = "quest_scEncInfest", SiegeQuestId = "quest_scEncSiege";
        public const string RequestWhoVar = "ScEncWho", RequestStageVar = "ScEncStage";
        /// <summary>StartRequest's error when the player's record is not in yet (the journal pass tries again).</summary>
        public const string RequestJournalNotReady = "the player's journal is not ready";

        public static bool IsOurs(Quest q)
        {
            return q != null && (Is(q, QuestId) || Is(q, RescueQuestId) || Is(q, SuppliesQuestId) || IsRequestQuest(q));
        }

        /// <summary>E4: an enclave request's quest (E5: of any kind).</summary>
        public static bool IsRequestQuest(Quest q)
        {
            return q != null && (Is(q, RequestQuestId) || Is(q, SearchQuestId) || Is(q, InfestQuestId) || Is(q, SiegeQuestId));
        }

        /// <summary>The quest class a request's journal entry is made of (E4's kinds share quest_scEncRequest).</summary>
        public static string RequestQuestIdFor(EnclaveRequest r)
        {
            switch (r != null ? r.Kind : "")
            {
                case EnclaveRequest.KindMissing: return SearchQuestId;
                case EnclaveRequest.KindInfestation: return InfestQuestId;
                case EnclaveRequest.KindSiege: return SiegeQuestId;
                default: return RequestQuestId;
            }
        }

        /// <summary>The request id an enclave request's quest carries (0: none).</summary>
        public static int RequestIdOf(Quest q)
        {
            string v;
            int id;
            return q != null && q.DataVariables != null && q.DataVariables.TryGetValue(RequestVar, out v) && int.TryParse(v, out id) ? id : 0;
        }

        private static bool Is(Quest q, string id)
        {
            return string.Equals(q.ID, id, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>A recruit favour's quest: the POI one or the supplies one.</summary>
        public static bool IsFavourQuest(Quest q)
        {
            return q != null && (Is(q, QuestId) || Is(q, SuppliesQuestId));
        }

        /// <summary>
        /// The favour quest this prospect gave is past its clearing phase (or completed) in the player's journal.
        /// Vanilla's ClearSleepers objective is the authority: the mod's own sleeper-volume reading can disagree
        /// with it (a volume flagged "due to respawn" right after a quest clear) and then the favour never ends.
        /// </summary>
        public static bool FavourQuestDone(EntityPlayer player, int prospectId)
        {
            try
            {
                QuestJournal j = Journal(player);
                if (j == null || j.quests == null) return false;
                for (int i = 0; i < j.quests.Count; i++)
                {
                    Quest q = j.quests[i];
                    if (q == null || q.QuestGiverID != prospectId || !string.Equals(q.ID, QuestId, StringComparison.OrdinalIgnoreCase)) continue;
                    if (q.CurrentState == Quest.QuestState.Completed) return true;
                    if (q.CurrentState == Quest.QuestState.InProgress && q.CurrentPhase >= 2) return true;
                }
            }
            catch (Exception e) { ScLog.Once("favour-quest-done", e); }
            return false;
        }

        public static bool Available
        {
            get
            {
                try { return QuestClass.s_Quests != null && QuestClass.s_Quests.ContainsKey(QuestId); }
                catch (Exception) { return false; }
            }
        }

        private static QuestJournal Journal(EntityPlayer player)
        {
            return player != null ? player.QuestJournal : null;
        }

        public static Quest Find(EntityPlayer player, Vector3i poiKey)
        {
            try
            {
                QuestJournal j = Journal(player);
                if (j == null || j.quests == null) return null;
                for (int i = 0; i < j.quests.Count; i++)
                {
                    Quest q = j.quests[i];
                    if (q == null || q.CurrentState != Quest.QuestState.InProgress) continue;
                    if (!string.Equals(q.ID, QuestId, StringComparison.OrdinalIgnoreCase)) continue;
                    Vector3 pos;
                    if (!q.GetPositionData(out pos, Quest.PositionDataTypes.POIPosition)) continue;
                    if (World.worldToBlockPos(pos) == poiKey) return q;
                }
            }
            catch (Exception e)
            {
                ScLog.Once("quest-find", e);
            }
            return null;
        }

        /// <summary>Is this POI position the target of one of our live quests? (SleeperEventData prefix.)</summary>
        public static bool IsOurPoi(Vector3 poiPosition)
        {
            try
            {
                World w = ScCommunity.World;
                if (w == null) return false;
                Vector3i key = World.worldToBlockPos(poiPosition);
#if SC_V34
                if (IsBeingAdded(key)) return true;
#endif
                List<EntityPlayerLocal> locals = w.GetLocalPlayers();
                if (locals == null) return false;
                for (int i = 0; i < locals.Count; i++) if (Find(locals[i], key) != null) return true;
            }
            catch (Exception e)
            {
                ScLog.Once("quest-isourpoi", e);
            }
            return false;
        }

#if SC_V34
        /// <summary>
        /// 3.4: Quest.StartQuest calls QuestEventManager.ForceUpdate() for a new quest, so the POI re-arm (the first
        /// SleeperEventData.Update after ClearSleepers subscribes) runs INSIDE QuestJournal.AddQuest, before the journal
        /// holds the quest, where Find cannot see it. Start() holds the quest here for that call. Main thread only.
        /// </summary>
        private static Quest beingAdded;

        private static bool IsBeingAdded(Vector3i poiKey)
        {
            Quest q = beingAdded;
            if (q == null || !string.Equals(q.ID, QuestId, StringComparison.OrdinalIgnoreCase)) return false;
            Vector3 pos;
            return q.GetPositionData(out pos, Quest.PositionDataTypes.POIPosition) && World.worldToBlockPos(pos) == poiKey;
        }
#endif

        public static bool Start(EntityPlayerLocal player, Prospect p, out string error)
        {
            error = null;
            try
            {
                if (player == null || p == null || p.Favour != FavourKind.Poi) { error = "scInternal"; return false; }
                if (!Available) { error = "scQuestUnavailable"; ScLog.Err("quest class " + QuestId + " is not loaded - Config/quests.xml did not apply"); return false; }
                if (Find(player, p.PoiPos) != null) { error = "scQuestAlready"; return false; }

                Quest q = QuestClass.CreateQuest(QuestId);
                if (q == null) { error = "scQuestUnavailable"; return false; }
                q.SetPositionData(Quest.PositionDataTypes.POIPosition, new Vector3(p.PoiPos.x, p.PoiPos.y, p.PoiPos.z));
                q.SetPositionData(Quest.PositionDataTypes.POISize, new Vector3(p.PoiWidth, 1f, p.PoiDepth));
                q.DataVariables[PoiNameVar] = SafeName(p.PoiName ?? "");
                // phase 2 (return to the survivor): vanilla InteractWithNPC checks Quest.QuestGiverID
                q.DataVariables[ObjectiveScTalkToSurvivor.NameVar] = SafeName(p.Name ?? "");
                q.QuestGiverID = p.Id;
                if (p.EncPersonId > 0) q.DataVariables[EncPersonVar] = p.EncPersonId.ToString();   // E3: the giver follows the person
                q.SetPositionData(Quest.PositionDataTypes.QuestGiver, GiverPosition(p));

                QuestJournal j = Journal(player);
                GameManager gm = GameManager.Instance;
                if (j == null || gm == null || gm.persistentPlayers == null || gm.persistentPlayers.GetPlayerDataFromEntityID(player.entityId) == null)
                {
                    error = "scQuestFailed";
                    return false;
                }
#if SC_V34
                // 3.4: AddQuest names where the quest came from (StartQuest's analytics). QuestSystem, not Trader: with Trader
                // vanilla reads the giver's NPCInfo.QuestFaction and activeQuests, which an SCore NPC (an EntityTrader) can lack.
                beingAdded = q;
                try
                {
                    j.AddQuest(q, Quest.QuestSource.QuestSystem);
                }
                finally
                {
                    beingAdded = null;
                }
#else
                j.AddQuest(q);
#endif
                ScLog.Out("secure quest started for " + p.PoiName + " at " + p.PoiPos + " (favour of " + p.Name + ")");
                return true;
            }
            catch (Exception e)
            {
                ScLog.Err("starting the secure quest failed: " + e.Message);
                Log.Exception(e);
                error = "scQuestFailed";
                return false;
            }
        }

        /// <summary>
        /// Starts the supplies favour's quest: "Supplies for &lt;name&gt;", one objective ("Bring 15 food items to
        /// &lt;name&gt;") that never completes on its own - the hand-over (DialogActionScTakeFavourItems) closes it. Its
        /// marker is the survivor (EnsureMapObject / TickMarkers).
        /// </summary>
        public static bool StartSupplies(EntityPlayerLocal player, Prospect p, out string error)
        {
            error = null;
            try
            {
                if (player == null || p == null || p.Favour != FavourKind.Supplies) { error = "scInternal"; return false; }
                if (QuestClass.s_Quests == null || !QuestClass.s_Quests.ContainsKey(SuppliesQuestId))
                {
                    error = "scQuestUnavailable";
                    ScLog.Err("quest class " + SuppliesQuestId + " is not loaded - Config/quests.xml did not apply");
                    return false;
                }
                if (FindFavour(player, p.Id) != null) { error = "scQuestAlready"; return false; }
                Quest q = QuestClass.CreateQuest(SuppliesQuestId);
                if (q == null) { error = "scQuestUnavailable"; return false; }
                string name = SafeName(p.Name ?? "");
                q.DataVariables[ObjectiveScTalkToSurvivor.NameVar] = name;
                q.DataVariables[TitleVar] = string.Format(Localization.Get("scQuestSuppliesNameFmt"), name);
                q.DataVariables[SuppliesVar] = SafeName(string.Format(Localization.Get("scQuestSuppliesWhat"), p.ItemCount,
                    Localization.Get(ScNames.LocKey(p.ItemCategory))));
                q.QuestGiverID = p.Id;
                if (p.EncPersonId > 0) q.DataVariables[EncPersonVar] = p.EncPersonId.ToString();   // E3: the giver follows the person
                q.SetPositionData(Quest.PositionDataTypes.QuestGiver, GiverPosition(p));
                QuestJournal j = Journal(player);
                GameManager gm = GameManager.Instance;
                if (j == null || gm == null || gm.persistentPlayers == null || gm.persistentPlayers.GetPlayerDataFromEntityID(player.entityId) == null)
                {
                    error = "scQuestFailed";
                    return false;
                }
#if SC_V34
                j.AddQuest(q, Quest.QuestSource.QuestSystem);
#else
                j.AddQuest(q);
#endif
                ScLog.Out("supplies quest started: " + p.ItemCount + " " + p.ItemCategory + " for " + p.Name);
                return true;
            }
            catch (Exception e)
            {
                ScLog.Once("quest-startsupplies", e);
                error = "scQuestFailed";
                return false;
            }
        }

        /// <summary>Where the survivor is: the loaded entity, else the last position the ticker recorded.</summary>
        public static Vector3 GiverPosition(Prospect p)
        {
            EntityAlive npc = ScCommunity.FindNpc(p.Id);
            if (npc != null) return npc.position;
            return new Vector3(p.NpcPos.x + 0.5f, p.NpcPos.y, p.NpcPos.z + 0.5f);
        }

        /// <summary>The prospect a favour quest belongs to: its quest giver, else (an older POI quest) the POI.</summary>
        internal static Prospect ProspectFor(Quest q)
        {
            if (q == null) return null;
            Prospect byGiver = ScRegistry.GetProspect(q.QuestGiverID);
            if (byGiver != null) return byGiver;
            Vector3 pos;
            if (!q.GetPositionData(out pos, Quest.PositionDataTypes.POIPosition)) return null;
            Vector3i key = World.worldToBlockPos(pos);
            foreach (var p in ScRegistry.Prospects.Values) if (p.Favour == FavourKind.Poi && p.PoiPos == key) return p;
            return null;
        }

        /// <summary>
        /// Ticker: the favour quest's marker follows the survivor (their entity while loaded, the last recorded
        /// position otherwise) so they can be found again - the POI quest's from phase 2, when it leaves the
        /// building; the supplies quest's always. The map object is the one EnsureMapObject registered by position;
        /// moving it is a TrackedPosition write.
        /// </summary>
        public static void TickMarkers(EntityPlayerLocal player)
        {
            try
            {
                QuestJournal j = Journal(player);
                if (j == null || j.quests == null) return;
                for (int i = 0; i < j.quests.Count; i++)
                {
                    Quest q = j.quests[i];
                    if (q == null || q.CurrentState != Quest.QuestState.InProgress) continue;
                    // E4: an enclave request's marker stays on their building - only placed again when it is missing
                    if (IsRequestQuest(q)) { if (q.NavObject == null) EnsureMapObject(q); continue; }
                    if (!IsFavourQuest(q)) continue;
                    if (q.NavObject == null) { EnsureMapObject(q); continue; }
                    if (Is(q, QuestId) && q.CurrentPhase < 2) continue;
                    Prospect p = ProspectFor(q);
                    if (p == null) continue;
                    Vector3 target = GiverPosition(p) + new Vector3(0f, 0.25f, 0f);
                    if ((q.NavObject.TrackedPosition - target).sqrMagnitude < 0.25f) continue;
                    q.NavObject.TrackedPosition = target;
                    q.Position = target;
                    q.SetPositionData(Quest.PositionDataTypes.QuestGiver, target);
                }
            }
            catch (Exception e)
            {
                ScLog.Once("quest-markers", e);
            }
        }

        // ------------------------------------------------------------------ rescue ----

        public static Quest FindRescue(EntityPlayer player, int memberId)
        {
            try
            {
                QuestJournal j = Journal(player);
                if (j == null || j.quests == null) return null;
                string want = memberId.ToString();
                for (int i = 0; i < j.quests.Count; i++)
                {
                    Quest q = j.quests[i];
                    if (q == null || q.CurrentState != Quest.QuestState.InProgress) continue;
                    if (!string.Equals(q.ID, RescueQuestId, StringComparison.OrdinalIgnoreCase)) continue;
                    string id;
                    if (q.DataVariables != null && q.DataVariables.TryGetValue(MemberVar, out id) && id == want) return q;
                }
            }
            catch (Exception e)
            {
                ScLog.Once("quest-findrescue", e);
            }
            return null;
        }

        /// <summary>Starts the rescue quest for a distress run (marker at the POI, title "Help &lt;name&gt;").</summary>
        public static bool StartRescue(EntityPlayerLocal player, Member m, PrefabInstance pi, out string error)
        {
            error = null;
            try
            {
                if (player == null || m == null || pi == null) { error = "scInternal"; return false; }
                if (QuestClass.s_Quests == null || !QuestClass.s_Quests.ContainsKey(RescueQuestId)) { error = "scQuestUnavailable"; return false; }
                if (FindRescue(player, m.Id) != null) return true;
                Quest q = QuestClass.CreateQuest(RescueQuestId);
                if (q == null) { error = "scQuestUnavailable"; return false; }
                Vector3i a = pi.boundingBoxPosition, s = pi.boundingBoxSize;
                q.SetPositionData(Quest.PositionDataTypes.POIPosition, new Vector3(a.x, a.y, a.z));
                q.SetPositionData(Quest.PositionDataTypes.POISize, new Vector3(s.x, 1f, s.z));
                q.DataVariables[PoiNameVar] = SafeName(m.RunPoiName ?? "");
                q.DataVariables[ObjectiveScTalkToSurvivor.NameVar] = SafeName(m.Name ?? "");
                q.DataVariables[TitleVar] = string.Format(Localization.Get("scQuestRescueNameFmt"), SafeName(m.Name ?? ""));
                q.DataVariables[MemberVar] = m.Id.ToString();
                QuestJournal j = Journal(player);
                if (j == null) { error = "scQuestFailed"; return false; }
#if SC_V34
                j.AddQuest(q, Quest.QuestSource.QuestSystem);
#else
                j.AddQuest(q);
#endif
                return true;
            }
            catch (Exception e)
            {
                ScLog.Once("quest-startrescue", e);
                error = "scQuestFailed";
                return false;
            }
        }

#if SC_V34
        /// <summary>
        /// Round 33c (M33): the rescue quest for a runner pinned at a natural spot (a mining run: no POI) - the same quest, its
        /// marker on the box at corner a, size s (16 x 16 around the spot).
        /// </summary>
        public static bool StartRescueAt(EntityPlayerLocal player, Member m, Vector3i a, Vector3i s, out string error)
        {
            error = null;
            try
            {
                if (player == null || m == null) { error = "scInternal"; return false; }
                if (QuestClass.s_Quests == null || !QuestClass.s_Quests.ContainsKey(RescueQuestId)) { error = "scQuestUnavailable"; return false; }
                if (FindRescue(player, m.Id) != null) return true;
                Quest q = QuestClass.CreateQuest(RescueQuestId);
                if (q == null) { error = "scQuestUnavailable"; return false; }
                q.SetPositionData(Quest.PositionDataTypes.POIPosition, new Vector3(a.x, a.y, a.z));
                q.SetPositionData(Quest.PositionDataTypes.POISize, new Vector3(s.x, 1f, s.z));
                q.DataVariables[PoiNameVar] = SafeName(m.RunPoiName ?? "");
                q.DataVariables[ObjectiveScTalkToSurvivor.NameVar] = SafeName(m.Name ?? "");
                q.DataVariables[TitleVar] = string.Format(Localization.Get("scQuestRescueNameFmt"), SafeName(m.Name ?? ""));
                q.DataVariables[MemberVar] = m.Id.ToString();
                QuestJournal j = Journal(player);
                if (j == null) { error = "scQuestFailed"; return false; }
                j.AddQuest(q, Quest.QuestSource.QuestSystem);
                return true;
            }
            catch (Exception e)
            {
                ScLog.Once("quest-startrescue-at", e);
                error = "scQuestFailed";
                return false;
            }
        }
#endif

        /// <summary>A runner came back under a new entity id: their rescue quest follows them.</summary>
        public static void RekeyRescue(EntityPlayer player, int oldId, int newId)
        {
            try
            {
                Quest q = player != null ? FindRescue(player, oldId) : null;
                if (q != null) q.DataVariables[MemberVar] = newId.ToString();
            }
            catch (Exception e)
            {
                ScLog.Once("quest-rekeyrescue", e);
            }
        }

        public static void CloseRescue(EntityPlayer player, Member m, bool completed)
        {
            try
            {
                if (player == null || m == null) return;
                Quest q = FindRescue(player, m.Id);
                if (q == null) return;
                QuestJournal j = Journal(player);
                if (j == null) return;
                if (completed) q.CloseQuest(Quest.QuestState.Completed);
                else j.RemoveQuest(q);
            }
            catch (Exception e)
            {
                ScLog.Once("quest-closerescue", e);
            }
        }

        /// <summary>
        /// The favour quest THIS survivor gave (POI or supplies), still in progress - by quest giver, not by POI: two
        /// survivors can name the same building, and only the first one's quest goes in the journal (Start refuses a
        /// second on that POI).
        /// </summary>
        public static Quest FindFavour(EntityPlayer player, int prospectId)
        {
            try
            {
                QuestJournal j = Journal(player);
                if (j == null || j.quests == null) return null;
                for (int i = 0; i < j.quests.Count; i++)
                {
                    Quest q = j.quests[i];
                    if (q == null || q.CurrentState != Quest.QuestState.InProgress || q.QuestGiverID != prospectId) continue;
                    if (IsFavourQuest(q)) return q;
                }
            }
            catch (Exception e)
            {
                ScLog.Once("quest-findfavour", e);
            }
            return null;
        }

        /// <summary>Closes the favour quest this survivor gave, if one is still in progress - see Close.</summary>
        public static void CloseFavour(EntityPlayer player, int prospectId, bool completed)
        {
            Close(player, FindFavour(player, prospectId), completed);
        }

        /// <summary>
        /// E3: an enclave person's favour moved to a new key (a new body, or parked without one): the favour quests that named
        /// the old one as their giver name the new one (InteractWithNPC reads QuestGiverID at each talk; RekeyRescue's
        /// precedent). Every state: a completed quest's giver is history, but cheap to keep right.
        /// </summary>
        public static void RekeyFavour(EntityPlayer player, int oldId, int newId)
        {
            try
            {
                QuestJournal j = Journal(player);
                if (j == null || j.quests == null) return;
                for (int i = 0; i < j.quests.Count; i++)
                {
                    Quest q = j.quests[i];
                    if (q != null && q.QuestGiverID == oldId && IsFavourQuest(q)) q.QuestGiverID = newId;
                }
            }
            catch (Exception e)
            {
                ScLog.Once("quest-rekeyfavour", e);
            }
        }

        /// <summary>
        /// E3, once a session (ScEnclaveDeals.SyncOnce): every in-progress favour quest of an enclave person (EncPersonVar)
        /// names their prospect's key now - a save keeps the giver id of a body that is gone - and one whose prospect is gone
        /// (the person died, joined, their enclave fell) is closed as failed. Returns how many changed.
        /// </summary>
        public static int SyncEnclaveFavours(EntityPlayer player)
        {
            int n = 0;
            try
            {
                QuestJournal j = Journal(player);
                if (j == null || j.quests == null) return 0;
                var gone = new List<Quest>();
                for (int i = 0; i < j.quests.Count; i++)
                {
                    Quest q = j.quests[i];
                    string v;
                    int pid;
                    if (q == null || q.CurrentState != Quest.QuestState.InProgress || !IsFavourQuest(q) || q.DataVariables == null
                        || !q.DataVariables.TryGetValue(EncPersonVar, out v) || !int.TryParse(v, out pid)) continue;
                    Prospect p = ScEnclaveDeals.ProspectByPerson(pid);
                    if (p == null) { gone.Add(q); continue; }
                    if (q.QuestGiverID == p.Id) continue;
                    q.QuestGiverID = p.Id;
                    n++;
                }
                foreach (Quest q in gone) Close(player, q, false);
                n += gone.Count;
            }
            catch (Exception e)
            {
                ScLog.Once("quest-syncenc", e);
            }
            return n;
        }

        // ------------------------------------------------------------------ enclave requests (E4) ----

        /// <summary>
        /// E4: the journal entry of an accepted enclave request (ScEncRequests.Accept and the session's journal pass):
        /// quest_scEncRequest, "Help &lt;enclave&gt;", one display-only objective (what, to whom, by the end of which day), the
        /// marker on their building - position data set before AddQuest, as the favour quests do. The giver is the enclave
        /// (no QuestGiverID, no InteractWithNPC: their bodies are rebuilt with new ids). Null (and why) when it cannot start.
        /// E5: each of its kinds has its own quest class (RequestQuestIdFor) and place (ScEncRequests.RequestPlace: the building
        /// searched, the infested one, the besieged enclave), the person's name and the stage word, a code of its own (two
        /// quests added in one frame would share vanilla's); a search whose remains were found starts in its "return" phase,
        /// a siege is tracked at once.
        /// </summary>
        public static Quest StartRequest(EntityPlayerLocal player, Enclave e, EnclaveRequest r, string what, out string error)
        {
            error = null;
            try
            {
                if (player == null || e == null || r == null) { error = "no player, enclave or request"; return null; }
                string id = RequestQuestIdFor(r);
                if (QuestClass.s_Quests == null || !QuestClass.s_Quests.ContainsKey(id))
                {
                    error = "quest class " + id + " is not loaded";
                    ScLog.Once("no-request-quest-" + id, "quest class " + id + " is not loaded - Config/quests.xml did not apply (requests work without the journal)");
                    return null;
                }
                QuestJournal j = Journal(player);
                GameManager gm = GameManager.Instance;
                if (j == null || gm == null || gm.persistentPlayers == null || gm.persistentPlayers.GetPlayerDataFromEntityID(player.entityId) == null)
                {
                    error = RequestJournalNotReady;
                    return null;
                }
                Quest q = QuestClass.CreateQuest(id);
                if (q == null) { error = "quest class " + id + " made no quest"; return null; }
                Vector3i at, size;
                string place;
                ScEncRequests.RequestPlace(r, e, out at, out size, out place);
                q.SetPositionData(Quest.PositionDataTypes.POIPosition, new Vector3(at.x, at.y, at.z));
                q.SetPositionData(Quest.PositionDataTypes.POISize, new Vector3(Math.Max(1, size.x), 1f, Math.Max(1, size.z)));
                string name = SafeName(e.Name ?? "");
                q.DataVariables[PoiNameVar] = SafeName(place ?? "");
                q.DataVariables[TitleVar] = TitleOf(r, name);
                q.DataVariables[RequestVar] = r.Id.ToString();
                q.DataVariables[RequestWhatVar] = SafeName(what ?? "");
                q.DataVariables[RequestNameVar] = name;
                q.DataVariables[RequestDueVar] = r.DueDay.ToString();
                if (r.IsE5)
                {
                    q.DataVariables[RequestWhoVar] = SafeName(ScEncSearch.PersonName(r));
                    q.DataVariables[RequestStageVar] = ScEncRequests.StageWord(r);
                }
                // a code of our own for every request quest (SetupQuestCode keeps a non-zero one): vanilla's hashes the frame time,
                // the quest id and a giver of -1, so two requests added in one frame would share it (the E4 review's L1)
                int code = ("scEncRequest_" + r.Id + "_" + r.Kind + "_" + player.entityId + "_"
                            + Time.unscaledTime.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).GetHashCode();
                q.QuestCode = code != 0 ? code : 1;
#if SC_V34
                j.AddQuest(q, Quest.QuestSource.QuestSystem);
#else
                j.AddQuest(q);
#endif
                if (r.Kind == EnclaveRequest.KindMissing && r.Stage == ScEncSearch.StageSearched) AdvanceRequest(q);
                if (r.Kind == EnclaveRequest.KindSiege) j.TrackedQuest = q;          // the besieged enclave's marker on screen at once
                return q;
            }
            catch (Exception ex)
            {
                ScLog.Once("quest-startrequest", ex);
                error = "the journal threw (see the log)";
                return null;
            }
        }

        /// <summary>
        /// E4: an enclave request's quest closed - completed, or removed as failed. Quest.CloseQuest's POI unlock looks the
        /// building up by its exact key and throws when no POI has it any more (an enclave that moved or lost its building):
        /// that quest's POI data goes first (the marker is ours, NavObject, and is removed all the same). E5: a quest that
        /// points at any other building - a missing survivor's, an infestation's, a spot where one waits - drops it too: the
        /// unlock would take the player off a vanilla quest's lock there (QuestUnlockPOI -> RemoveQuester).
        /// </summary>
        public static void CloseRequest(EntityPlayer player, Quest q, bool completed)
        {
            try
            {
                QuestJournal j = Journal(player);
                if (q == null || j == null) return;
                Vector3 pos;
                if (q.GetPositionData(out pos, Quest.PositionDataTypes.POIPosition))
                {
                    DynamicPrefabDecorator dpd = GameManager.Instance != null ? GameManager.Instance.GetDynamicPrefabDecorator() : null;
                    bool foreign = !Is(q, RequestQuestId) && !ScEnclaves.IsEnclavePoiKey(World.worldToBlockPos(pos));
                    if (foreign || dpd == null || dpd.GetPrefabFromWorldPos((int)pos.x, (int)pos.z) == null)
                    {
                        q.RemovePositionData(Quest.PositionDataTypes.POIPosition);
                        q.RemovePositionData(Quest.PositionDataTypes.POISize);
                    }
                }
                if (completed) q.CloseQuest(Quest.QuestState.Completed);
                else j.RemoveQuest(q);
            }
            catch (Exception ex)
            {
                ScLog.Once("quest-closerequest", ex);
            }
        }

        /// <summary>
        /// E4, the session's journal pass: a request quest points at its enclave's building now - an enclave moves when its
        /// POI is gone (ScEnclaves.Validate), and a POI pack can move a key - and its marker follows.
        /// </summary>
        public static void FollowRequestPoi(Quest q, Enclave e)
        {
            try
            {
                if (q == null || e == null) return;
                var want = new Vector3(e.Poi.x, e.Poi.y, e.Poi.z);
                Vector3 pos;
                if (q.GetPositionData(out pos, Quest.PositionDataTypes.POIPosition) && pos == want) return;
                q.SetPositionData(Quest.PositionDataTypes.POIPosition, want);
                q.SetPositionData(Quest.PositionDataTypes.POISize, new Vector3(Math.Max(1, e.Size.x), 1f, Math.Max(1, e.Size.z)));
                q.DataVariables[PoiNameVar] = SafeName(e.PoiName ?? "");
                if (q.NavObject != null && NavObjectManager.Instance != null) NavObjectManager.Instance.UnRegisterNavObject(q.NavObject);
                q.NavObject = null;
                EnsureMapObject(q);
                ScLog.Out("enclave request quest " + q.QuestCode + ": its marker moved to " + e.Name + "'s building now (" + e.PoiName + " at " + e.Poi + ")");
            }
            catch (Exception ex)
            {
                ScLog.Once("quest-followrequest", ex);
            }
        }

        /// <summary>E4's title "Help &lt;enclave&gt;"; E5's "Find &lt;person&gt;", "Clear &lt;POI&gt;", "Siege at &lt;enclave&gt;".</summary>
        private static string TitleOf(EnclaveRequest r, string enclave)
        {
            switch (r.Kind)
            {
                case EnclaveRequest.KindMissing: return string.Format(Localization.Get("scQuestEncSearchNameFmt"), SafeName(ScEncSearch.PersonName(r)));
                case EnclaveRequest.KindInfestation: return string.Format(Localization.Get("scQuestEncInfestNameFmt"), SafeName(r.TargetName ?? ""));
                case EnclaveRequest.KindSiege: return string.Format(Localization.Get("scQuestEncSiegeNameFmt"), enclave);
                default: return string.Format(Localization.Get("scQuestEncReqNameFmt"), enclave);
            }
        }

        /// <summary>
        /// E5: an E5 request's quest made to match the record - the session's journal pass and every stage change (found,
        /// following, lost again, the remains searched): its place (ScEncRequests.RequestPlace) and marker, the person's name,
        /// the stage word and the due day its objective reads, the "return" phase once the remains were searched; the
        /// objectives drawn again.
        /// </summary>
        public static void ShapeRequest(Quest q, EnclaveRequest r, Enclave e)
        {
            try
            {
                if (q == null || r == null || e == null || q.CurrentState != Quest.QuestState.InProgress) return;
                Vector3i at, size;
                string place;
                ScEncRequests.RequestPlace(r, e, out at, out size, out place);
                MoveRequestMarker(q, at, size, place);
                q.DataVariables[RequestWhoVar] = SafeName(ScEncSearch.PersonName(r));
                q.DataVariables[RequestStageVar] = ScEncRequests.StageWord(r);
                q.DataVariables[RequestDueVar] = r.DueDay.ToString();
                if (r.Kind == EnclaveRequest.KindMissing && r.Stage == ScEncSearch.StageSearched) AdvanceRequest(q);
                Redraw(q);
            }
            catch (Exception ex)
            {
                ScLog.Once("quest-shaperequest", ex);
            }
        }

        /// <summary>The quest's place (POIPosition/POISize, the name its objective reads) and its marker moved there; nothing when it is there already.</summary>
        private static void MoveRequestMarker(Quest q, Vector3i at, Vector3i size, string place)
        {
            var want = new Vector3(at.x, at.y, at.z);
            Vector3 pos;
            q.DataVariables[PoiNameVar] = SafeName(place ?? "");
            if (q.GetPositionData(out pos, Quest.PositionDataTypes.POIPosition) && pos == want) return;
            q.SetPositionData(Quest.PositionDataTypes.POIPosition, want);
            q.SetPositionData(Quest.PositionDataTypes.POISize, new Vector3(Math.Max(1, size.x), 1f, Math.Max(1, size.z)));
            if (q.NavObject != null && NavObjectManager.Instance != null) NavObjectManager.Instance.UnRegisterNavObject(q.NavObject);
            q.NavObject = null;
            EnsureMapObject(q);
            ScLog.Verbose("enclave request quest " + q.QuestCode + ": its marker moved to " + place + " at " + at);
        }

        /// <summary>
        /// The quest on to its next phase (a search's "Return to the enclave"): the current phase's objectives - display only,
        /// they never complete on their own - marked complete, then vanilla's own completion check moves the phase
        /// (Quest.RefreshQuestCompletion -> AdvancePhase). Never past the last phase: that would complete the quest.
        /// </summary>
        private static void AdvanceRequest(Quest q)
        {
            if (q.CurrentState != Quest.QuestState.InProgress || q.QuestClass == null || q.CurrentPhase >= q.QuestClass.HighestPhase) return;
            byte phase = q.CurrentPhase;
            foreach (BaseObjective o in q.Objectives) if (o != null && o.Phase == phase) o.Complete = true;
            q.RefreshQuestCompletion(QuestClass.CompletionTypes.AutoComplete, null, true);
        }

        /// <summary>Each objective's line made again (BaseObjective.CurrentValue's setter calls SetupDisplay and tells the tracker).</summary>
        private static void Redraw(Quest q)
        {
            foreach (BaseObjective o in q.Objectives) if (o != null) o.CurrentValue = o.CurrentValue;
        }

        /// <summary>Closes the quest through Quest.CloseQuest (unhooks objectives) and restores the compass.</summary>
        private static void Close(EntityPlayer player, Quest q, bool completed)
        {
            try
            {
                if (q == null) return;
                QuestJournal j = Journal(player);
                if (j == null) return;
                Vector3 pos, size;
                Rect ours = default(Rect);
                bool hasPos = q.GetPositionData(out pos, Quest.PositionDataTypes.POIPosition);
                if (hasPos && q.GetPositionData(out size, Quest.PositionDataTypes.POISize))
                    ours = new Rect(pos.x, pos.z, size.x, size.z);
                EntityPlayerLocal lp = player as EntityPlayerLocal;
                bool restoreCompass = hasPos && lp != null && lp.ZombieCompassBounds == ours;
                if (hasPos) NoRearm(pos);
                if (completed) q.CloseQuest(Quest.QuestState.Completed);
                else j.RemoveQuest(q);
                if (restoreCompass) lp.ZombieCompassBounds = default(Rect);
                Resubscribe(player, q);
            }
            catch (Exception e)
            {
                ScLog.Once("quest-close", e);
            }
        }

        // Closing unsubscribes, but QuestEventManager runs a dropped POI's SleeperEventData once more before removing it,
        // and with our quest closed Patches_Quest no longer suppresses its re-arm: a quest closed before its first update
        // (the frame it started) would re-arm the POI. Marking it refreshed is what that first update would have done.
        private static void NoRearm(Vector3 poiPos)
        {
            try
            {
                QuestEventManager qem = QuestEventManager.Current;
                SleeperEventData sed;
                if (qem != null && qem.SleeperVolumeUpdateDictionary != null
                    && qem.SleeperVolumeUpdateDictionary.TryGetValue(poiPos, out sed) && sed != null) sed.hasRefreshed = true;
            }
            catch (Exception e)
            {
                ScLog.Once("quest-norearm", e);
            }
        }

        // QuestEventManager keys sleeper subscriptions by POI position and tracks entity ids, so closing our
        // quest unsubscribes the player from that POI even when a vanilla quest still watches the same building.
        private static void Resubscribe(EntityPlayer player, Quest closed)
        {
            try
            {
                Vector3 pos;
                if (player == null || closed == null || !closed.GetPositionData(out pos, Quest.PositionDataTypes.POIPosition)) return;
                QuestJournal j = Journal(player);
                if (j == null || j.quests == null) return;
                for (int i = 0; i < j.quests.Count; i++)
                {
                    Quest other = j.quests[i];
                    if (other == null || other == closed || other.CurrentState != Quest.QuestState.InProgress) continue;
                    Vector3 op;
                    if (!other.GetPositionData(out op, Quest.PositionDataTypes.POIPosition) || op != pos) continue;
                    QuestEventManager qem = QuestEventManager.Current;
                    if (qem != null) qem.SubscribeToUpdateEvent(player.entityId, pos);
                    return;
                }
            }
            catch (Exception e)
            {
                ScLog.Once("quest-resubscribe", e);
            }
        }

        // ------------------------------------------------------------------ title ----

        public const string PoiNameVar = "POIName";

        /// <summary>
        /// The instance title ("Secure NPD 3rd Division"). Vanilla has no per-instance quest title - every
        /// renderer reads the shared QuestClass.Name / SubTitle - so Patches_ScQuestTitle swaps those around
        /// each vanilla read. Null for any other quest.
        /// </summary>
        public static string TitleFor(Quest q)
        {
            try
            {
                if (q == null || q.DataVariables == null) return null;
                if (!IsOurs(q)) return null;
                string title;
                if (q.DataVariables.TryGetValue(TitleVar, out title) && !string.IsNullOrEmpty(title)) return SafeName(title);
                if (!string.Equals(q.ID, QuestId, StringComparison.OrdinalIgnoreCase)) return null;
                string poi;
                if (!q.DataVariables.TryGetValue(PoiNameVar, out poi) || string.IsNullOrEmpty(poi)) return null;
                return string.Format(Localization.Get("scQuestSecureNameFmt"), SafeName(poi));
            }
            catch (Exception e)
            {
                ScLog.Once("quest-title", e);
                return null;
            }
        }

        /// <summary>
        /// The "Quest completed" toast is handed to string.Format as the FORMAT string with the class name
        /// inside it, so a brace in a POI name would throw from the middle of CloseQuest. Parentheses, not
        /// "{{": the journal and tracker do not string.Format and would show the doubling.
        /// </summary>
        public static string SafeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            return (name.IndexOf('{') < 0 && name.IndexOf('}') < 0) ? name : name.Replace('{', '(').Replace('}', ')');
        }

        /// <summary>
        /// One swap of the shared QuestClass's Name and SubTitle to the instance title, undone by End. Created
        /// in a Harmony prefix, carried in __state, ended in the finalizer. Nesting is safe (LIFO restore).
        /// </summary>
        public sealed class TitleSwap
        {
            private QuestClass cls;
            private string name;
            private string sub;

            public static TitleSwap Begin(Quest q)
            {
                try
                {
                    string title = TitleFor(q);
                    if (title == null) return null;
                    QuestClass c = q.QuestClass;
                    if (c == null) return null;
                    var s = new TitleSwap { cls = c, name = c.Name, sub = c.SubTitle };
                    c.Name = title;
                    c.SubTitle = title;
                    return s;
                }
                catch (Exception e)
                {
                    ScLog.Once("quest-title-swap", e);
                    return null;
                }
            }

            public static void End(TitleSwap s)
            {
                if (s == null || s.cls == null) return;
                try { s.cls.Name = s.name; s.cls.SubTitle = s.sub; }
                catch (Exception e) { ScLog.Once("quest-title-restore", e); }
                s.cls = null;
            }
        }

        // ------------------------------------------------------------------ marker ----

        /// <summary>
        /// Puts the quest's POI on the map, the compass and the screen as vanilla's own quest map object
        /// (Quest.NavObject, class "quest": grey while another quest is tracked, orange + the 3D on-screen
        /// icon with its distance while this one is). Vanilla creates it from a Goto objective, which this
        /// quest deliberately lacks, so the StartQuest postfix creates it - add path and load path alike.
        /// Registered directly rather than through Quest.HandleMapObject: that samples the height with (x, y)
        /// instead of (x, z), and NavObjectManager.RegisterNavObject hands back an EXISTING object when
        /// class, owner and position match exactly - a vanilla clear quest for the same POI registers at that
        /// same uncorrected position on load and would be aliased. Every vanilla position is integer-valued,
        /// so a quarter block above the ground can never coincide.
        /// </summary>
        public static void EnsureMapObject(Quest q)
        {
            try
            {
                if (q == null || !IsOurs(q)) return;
                if (q.OwnerJournal == null || !q.Active || q.NavObject != null) return;
                if (GameManager.IsDedicatedServer) return;
                if (NavObjectClass.GetNavObjectClass("quest") == null) return;
                World w = ScCommunity.World;
                NavObjectManager nav = NavObjectManager.Instance;
                EntityPlayer owner = (w != null) ? w.GetPrimaryPlayer() : null;
                if (w == null || nav == null || owner == null) return;

                Vector3 at;
                if (Is(q, SuppliesQuestId))
                {
                    // no building: the survivor (TickMarkers keeps it on them; retried there until the registry is in)
                    Prospect p = ProspectFor(q);
                    if (p == null) return;
                    at = GiverPosition(p) + new Vector3(0f, 0.25f, 0f);
                }
                else
                {
                    Vector3 pos, size;
                    if (!q.GetPositionData(out pos, Quest.PositionDataTypes.POIPosition)
                        || !q.GetPositionData(out size, Quest.PositionDataTypes.POISize)) return;
                    float cx = pos.x + size.x / 2f, cz = pos.z + size.z / 2f;
                    at = new Vector3(cx + 0.5f, (int)w.GetHeightAt(cx, cz) + 0.25f, cz + 0.5f);
                }
                NavObject obj = nav.RegisterNavObject("quest", at, "", false, -1, owner);
                if (obj == null) return;
                if (IsHeldByAnotherQuest(q, obj))
                {
                    obj = nav.RegisterNavObject("quest", at + new Vector3(0.01f, 0f, 0f), "", false, -1, owner);
                    if (obj == null || IsHeldByAnotherQuest(q, obj)) return;
                }
                // fields Quest.HandleMapObject fills, plus the ones NavObject.Reset leaves behind on a recycled object
                obj.ExtraData = -1f;
                obj.hiddenOnMap = false;
                obj.HiddenDisplayName = null;
                obj.UseOverrideFontColor = false;
                obj.name = (TitleFor(q) ?? q.ID) + " (" + owner.PlayerDisplayName + ")";
                q.NavObject = obj;
                q.Position = at;
                obj.IsActive = q.Tracked;
                ScLog.Verbose("favour quest marker placed at " + at + " for " + (TitleFor(q) ?? q.ID));
            }
            catch (Exception e)
            {
                ScLog.Once("quest-marker", e);
            }
        }

        private static bool IsHeldByAnotherQuest(Quest q, NavObject obj)
        {
            QuestJournal j = Journal(q.OwnerJournal != null ? q.OwnerJournal.OwnerPlayer : null);
            if (j == null || j.quests == null) return false;
            for (int i = 0; i < j.quests.Count; i++)
            {
                Quest other = j.quests[i];
                if (other == null || other == q || other.NavObject != obj) continue;
                NavObjectManager nav = NavObjectManager.Instance;
                return nav != null && nav.NavObjectList != null && nav.NavObjectList.Contains(obj);
            }
            return false;
        }

        private static readonly List<Prospect> doneScratch = new List<Prospect>();

        /// <summary>The entity id of the NPC whose dialog the player has open (SCore's CurrentNPC), or -1.</summary>
        private static int TalkingTo(EntityPlayerLocal player)
        {
            try
            {
                LocalPlayerUI ui = player.PlayerUI;
                if (ui == null || ui.windowManager == null || !ui.windowManager.IsWindowOpen("dialog")) return -1;
                if (player.Buffs == null || !player.Buffs.HasCustomVar("CurrentNPC")) return -1;
                return (int)player.Buffs.GetCustomVar("CurrentNPC");
            }
            catch (Exception) { return -1; }
        }

        /// <summary>
        /// Ticker: marks a POI favour done once its quest is past the clearing phase. Only the quest counts - a POI
        /// cleared without it (before or after the favour was asked) never completes the favour on its own; the
        /// player says so in the dialog instead (DialogActionScFavourPoiCleared). Not while the player is talking to
        /// that survivor: on a POI that is already clear vanilla moves the quest on the frame after "Mark it" (the
        /// re-arm is suppressed, every volume reads cleared), and the dialog completes it there.
        /// </summary>
        public static void SyncCompleted(EntityPlayerLocal player)
        {
            try
            {
                if (player == null || ScRegistry.Prospects.Count == 0) return;
                int talking = TalkingTo(player);
                doneScratch.Clear();
                foreach (var p in ScRegistry.Prospects.Values)
                {
                    if (p.Favour != FavourKind.Poi || p.Done || p.Id == talking) continue;
                    if (FavourQuestDone(player, p.Id)) doneScratch.Add(p);
                }
                for (int i = 0; i < doneScratch.Count; i++)
                {
                    Prospect p = doneScratch[i];
                    p.Done = true;
                    ScLeadership.Count(ScLeadership.FavourEvent);
                    // the quest stays open: its phase 2 ("return to <name>") completes when the player talks to them
                    EntityAlive npc = ScCommunity.FindNpc(p.Id);
                    if (npc != null) npc.Buffs.SetCustomVar("sc_favourDone", 1f);
                    ScCommunity.Tell(player, string.Format(Localization.Get("scFavourPoiDone"), p.PoiName, p.Name), "");
                    ScLog.Out("favour done: " + p.PoiName + " is clear - return to " + p.Name + " and they will join");
                }
                if (doneScratch.Count > 0) ScRegistry.SaveAtomic();
                doneScratch.Clear();
            }
            catch (Exception e)
            {
                ScLog.Once("quest-sync", e);
            }
        }
    }
}
