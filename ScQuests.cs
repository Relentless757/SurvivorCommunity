using System;
using System.Collections.Generic;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// The "secure this POI for me" favour quest (Config/quests.xml, quest_scSecurePoi). A guide, never a
    /// gate: whether the favour is done is decided by ScSecurity.IsCleared alone, so a player who clears the
    /// POI without ever taking the quest is not blocked. No RallyPoint / Goto objective on purpose - the
    /// rally point is the only vanilla path to World.ResetPOIS (which wipes anything built there), and the
    /// quieter reset ObjectiveClearSleepers performs on subscribe is suppressed by Patches_Quest for the POIs
    /// of these quests only. Position data must be set BEFORE QuestJournal.AddQuest (the objectives read it
    /// in AddHooks).
    /// </summary>
    public static class ScQuests
    {
        public const string QuestId = "quest_scSecurePoi";
        public const string RescueQuestId = "quest_scRescue";
        public const string TitleVar = "ScTitle";
        public const string MemberVar = "ScMember";

        public static bool IsOurs(Quest q)
        {
            return q != null && (string.Equals(q.ID, QuestId, StringComparison.OrdinalIgnoreCase) || string.Equals(q.ID, RescueQuestId, StringComparison.OrdinalIgnoreCase));
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
                q.SetPositionData(Quest.PositionDataTypes.QuestGiver, GiverPosition(p));

                QuestJournal j = Journal(player);
                GameManager gm = GameManager.Instance;
                if (j == null || gm == null || gm.persistentPlayers == null || gm.persistentPlayers.GetPlayerDataFromEntityID(player.entityId) == null)
                {
                    error = "scQuestFailed";
                    return false;
                }
                j.AddQuest(q);
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

        /// <summary>Where the survivor is: the loaded entity, else the last position the ticker recorded.</summary>
        public static Vector3 GiverPosition(Prospect p)
        {
            EntityAlive npc = ScCommunity.FindNpc(p.Id);
            if (npc != null) return npc.position;
            return new Vector3(p.NpcPos.x + 0.5f, p.NpcPos.y, p.NpcPos.z + 0.5f);
        }

        private static Prospect ProspectFor(Quest q)
        {
            Vector3 pos;
            if (q == null || !q.GetPositionData(out pos, Quest.PositionDataTypes.POIPosition)) return null;
            Vector3i key = World.worldToBlockPos(pos);
            foreach (var p in ScRegistry.Prospects.Values) if (p.Favour == FavourKind.Poi && p.PoiPos == key) return p;
            return null;
        }

        /// <summary>
        /// Ticker: in phase 2 the quest marker leaves the building and follows the survivor (their entity while
        /// loaded, the last recorded position otherwise), so they can be found again. The map object is the
        /// one EnsureMapObject registered by position; moving it is a TrackedPosition write.
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
                    if (!string.Equals(q.ID, QuestId, StringComparison.OrdinalIgnoreCase)) continue;
                    if (q.NavObject == null) { EnsureMapObject(q); continue; }
                    if (q.CurrentPhase < 2) continue;
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
                j.AddQuest(q);
                return true;
            }
            catch (Exception e)
            {
                ScLog.Once("quest-startrescue", e);
                error = "scQuestFailed";
                return false;
            }
        }

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

        /// <summary>Closes the quest through Quest.CloseQuest (unhooks objectives) and restores the compass.</summary>
        public static void Close(EntityPlayer player, Vector3i poiKey, bool completed)
        {
            try
            {
                Quest q = Find(player, poiKey);
                if (q == null) return;
                QuestJournal j = Journal(player);
                if (j == null) return;
                Vector3 pos, size;
                Rect ours = default(Rect);
                if (q.GetPositionData(out pos, Quest.PositionDataTypes.POIPosition) && q.GetPositionData(out size, Quest.PositionDataTypes.POISize))
                    ours = new Rect(pos.x, pos.z, size.x, size.z);
                EntityPlayerLocal lp = player as EntityPlayerLocal;
                bool restoreCompass = lp != null && lp.ZombieCompassBounds == ours;
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
                Vector3 pos, size;
                if (!q.GetPositionData(out pos, Quest.PositionDataTypes.POIPosition)
                    || !q.GetPositionData(out size, Quest.PositionDataTypes.POISize)) return;
                World w = ScCommunity.World;
                NavObjectManager nav = NavObjectManager.Instance;
                EntityPlayer owner = (w != null) ? w.GetPrimaryPlayer() : null;
                if (w == null || nav == null || owner == null) return;

                float cx = pos.x + size.x / 2f, cz = pos.z + size.z / 2f;
                Vector3 at = new Vector3(cx + 0.5f, (int)w.GetHeightAt(cx, cz) + 0.25f, cz + 0.5f);
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

        /// <summary>Ticker: marks POI favours done once their POI reads cleared (quest or no quest).</summary>
        public static void SyncCompleted(EntityPlayerLocal player)
        {
            try
            {
                World w = ScCommunity.World;
                if (w == null || player == null || ScRegistry.Prospects.Count == 0) return;
                doneScratch.Clear();
                foreach (var p in ScRegistry.Prospects.Values)
                {
                    if (p.Favour != FavourKind.Poi || p.Done) continue;
                    if (FavourQuestDone(player, p.Id)) { doneScratch.Add(p); continue; }   // the quest cleared it
                    // only judge a POI while it is loaded - an unloaded one has no volumes to read
                    if (!ScZone.IsLoaded(p.PoiPos)) continue;
                    PrefabInstance pi = ScSecurity.PoiByKey(p.PoiPos);
                    if (pi == null) continue;
                    string why;
                    if (ScSecurity.IsCleared(w, pi, out why)) doneScratch.Add(p);
                }
                for (int i = 0; i < doneScratch.Count; i++)
                {
                    Prospect p = doneScratch[i];
                    p.Done = true;
                    ScLeadership.Count(ScLeadership.FavourEvent);
                    // the quest stays open: its phase 2 ("return to <name>") completes when the player talks to
                    // them, and ScJoin closes whatever is still open as a fallback
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
