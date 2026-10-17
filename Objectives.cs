using UnityEngine.Scripting;

// Quest objective classes resolve as "Objective" + type, so a type="ScTalkToSurvivor, SurvivorCommunity" in
// quests.xml is this class in the global namespace (ReflectionHelpers.GetTypeWithPrefix).

/// <summary>
/// Phase 2 of the favour quest: go back to the survivor who asked. Vanilla's InteractWithNPC does the
/// work - it completes on QuestEventManager.NPCInteract, which EntityTrader fires when the dialog opens
/// (SCore's EntityAliveSDX goes through the same lock request) - and, because the quest class leaves
/// return_to_quest_giver at its default (true) and Quest.QuestGiverID is the prospect's entity id, only
/// that survivor counts. This subclass only changes the journal line to "Return to &lt;name&gt;".
/// </summary>
/// <summary>
/// The rescue objective: "Defend &lt;name&gt; at &lt;POI&gt;". It completes through the mod (ScRadio closes the quest
/// when the spawned zombies are down), never on its own, so the quest stays open until then.
/// </summary>
[Preserve]
public class ObjectiveScRescue : BaseObjective
{
    public override void SetupDisplay()
    {
        string name = null, poi = null;
        try
        {
            if (base.OwnerQuest != null && base.OwnerQuest.DataVariables != null)
            {
                base.OwnerQuest.DataVariables.TryGetValue(ObjectiveScTalkToSurvivor.NameVar, out name);
                base.OwnerQuest.DataVariables.TryGetValue(SurvivorCommunity.ScQuests.PoiNameVar, out poi);
            }
        }
        catch (System.Exception) { }
        base.Description = string.Format(Localization.Get("scObjectiveRescue"), name ?? Localization.Get("scObjectiveReturnWho"), poi ?? "");
        StatusText = "";
    }

    public override BaseObjective Clone()
    {
        ObjectiveScRescue o = new ObjectiveScRescue();
        CopyValues(o);
        return o;
    }
}

/// <summary>
/// The supplies favour's objective: "Bring 15 food items to &lt;name&gt;". It never completes on its own - the hand-over
/// in the survivor's dialog (DialogActionScTakeFavourItems) closes the quest.
/// </summary>
[Preserve]
public class ObjectiveScBringSupplies : BaseObjective
{
    public override void SetupDisplay()
    {
        string name = null, what = null;
        try
        {
            if (base.OwnerQuest != null && base.OwnerQuest.DataVariables != null)
            {
                base.OwnerQuest.DataVariables.TryGetValue(ObjectiveScTalkToSurvivor.NameVar, out name);
                base.OwnerQuest.DataVariables.TryGetValue(SurvivorCommunity.ScQuests.SuppliesVar, out what);
            }
        }
        catch (System.Exception) { }
        base.Description = string.Format(Localization.Get("scObjectiveSupplies"), what ?? "", name ?? Localization.Get("scObjectiveReturnWho"));
        StatusText = "";
    }

    public override BaseObjective Clone()
    {
        ObjectiveScBringSupplies o = new ObjectiveScBringSupplies();
        CopyValues(o);
        return o;
    }
}

/// <summary>
/// E4, an enclave's request taken on: "Bring a medium bundle of medicine to &lt;enclave&gt; by the end of day 14". Display
/// only: it never completes on its own - the hand-over in the leader's talk, or the request's end (ScEncRequests.Close),
/// closes the quest. No InteractWithNPC: the giver is the enclave, and its people's bodies are rebuilt with new ids.
/// </summary>
[Preserve]
public class ObjectiveScEncRequest : BaseObjective
{
    public override void SetupDisplay()
    {
        string what = null, name = null, due = null;
        try
        {
            if (base.OwnerQuest != null && base.OwnerQuest.DataVariables != null)
            {
                base.OwnerQuest.DataVariables.TryGetValue(SurvivorCommunity.ScQuests.RequestWhatVar, out what);
                base.OwnerQuest.DataVariables.TryGetValue(SurvivorCommunity.ScQuests.RequestNameVar, out name);
                base.OwnerQuest.DataVariables.TryGetValue(SurvivorCommunity.ScQuests.RequestDueVar, out due);
            }
        }
        catch (System.Exception) { }
        base.Description = string.Format(Localization.Get("scObjectiveEncRequest"), what ?? "", name ?? "", due ?? "?");
        StatusText = "";
    }

    public override BaseObjective Clone()
    {
        ObjectiveScEncRequest o = new ObjectiveScEncRequest();
        CopyValues(o);
        return o;
    }
}

/// <summary>The E5 objectives' shared reads: a quest variable, "" when it is not there.</summary>
internal static class ScEncObjectiveVars
{
    public static string Get(BaseObjective o, string key)
    {
        try
        {
            string v;
            if (o != null && o.OwnerQuest != null && o.OwnerQuest.DataVariables != null && o.OwnerQuest.DataVariables.TryGetValue(key, out v)) return v ?? "";
        }
        catch (System.Exception) { }
        return "";
    }
}

/// <summary>
/// E5, a missing survivor's search (quest_scEncSearch, phase 1): "Find Mara, last seen at the Shotgun Messiah factory, by the
/// end of day 14" - once they follow the player, "Bring Mara home to Harlan's Rest" (the stage word ScEncStage). Display only:
/// the search (ScEncSearch, ScEncRequests.Close) closes the quest.
/// </summary>
[Preserve]
public class ObjectiveScEncSearch : BaseObjective
{
    public override void SetupDisplay()
    {
        string who = ScEncObjectiveVars.Get(this, SurvivorCommunity.ScQuests.RequestWhoVar);
        string stage = ScEncObjectiveVars.Get(this, SurvivorCommunity.ScQuests.RequestStageVar);
        if (stage == "follow")
            base.Description = string.Format(Localization.Get("scObjectiveEncSearchFollow"), who, ScEncObjectiveVars.Get(this, SurvivorCommunity.ScQuests.RequestNameVar));
        else
            base.Description = string.Format(Localization.Get("scObjectiveEncSearch"), who, ScEncObjectiveVars.Get(this, SurvivorCommunity.ScQuests.PoiNameVar),
                                             ScEncObjectiveVars.Get(this, SurvivorCommunity.ScQuests.RequestDueVar));
        StatusText = "";
    }

    public override BaseObjective Clone()
    {
        ObjectiveScEncSearch o = new ObjectiveScEncSearch();
        CopyValues(o);
        return o;
    }
}

/// <summary>
/// E5, the search's phase 2 once their remains were searched: "Tell Harlan's Rest about Mara". Display only: the leader's "I
/// found &lt;name&gt;. I'm sorry." closes the quest.
/// </summary>
[Preserve]
public class ObjectiveScEncReturn : BaseObjective
{
    public override void SetupDisplay()
    {
        base.Description = string.Format(Localization.Get("scObjectiveEncReturn"), ScEncObjectiveVars.Get(this, SurvivorCommunity.ScQuests.RequestNameVar),
                                         ScEncObjectiveVars.Get(this, SurvivorCommunity.ScQuests.RequestWhoVar));
        StatusText = "";
    }

    public override BaseObjective Clone()
    {
        ObjectiveScEncReturn o = new ObjectiveScEncReturn();
        CopyValues(o);
        return o;
    }
}

/// <summary>
/// E5, an infestation to clear (quest_scEncInfest): "Clear the infestation at the Crack-a-Book by the end of day 14". Display
/// only - never ClearSleepers (it would re-arm the rooms): I1's own "cleared" path closes the quest (ScEncInfest.OnEnded).
/// </summary>
[Preserve]
public class ObjectiveScEncInfest : BaseObjective
{
    public override void SetupDisplay()
    {
        base.Description = string.Format(Localization.Get("scObjectiveEncInfest"), ScEncObjectiveVars.Get(this, SurvivorCommunity.ScQuests.PoiNameVar),
                                         ScEncObjectiveVars.Get(this, SurvivorCommunity.ScQuests.RequestDueVar));
        StatusText = "";
    }

    public override BaseObjective Clone()
    {
        ObjectiveScEncInfest o = new ObjectiveScEncInfest();
        CopyValues(o);
        return o;
    }
}

/// <summary>
/// E5, a siege (quest_scEncSiege): "Siege:" and a live count - "12:34 to reach Harlan's Rest" while it is called, "8 left" once
/// the horde is there (ScEncSiege.StatusFor, from the registry: the deadline is world time, so the pause stops it and a load
/// keeps it). A Time value with UpdateUI: the tracker draws the row every frame. Display only: the siege closes the quest.
/// </summary>
[Preserve]
public class ObjectiveScEncSiege : BaseObjective
{
    public override ObjectiveValueTypes ObjectiveValueType => ObjectiveValueTypes.Time;

    public override bool UpdateUI => true;

    public override string StatusText
    {
        get
        {
            try { return SurvivorCommunity.ScEncSiege.StatusFor(SurvivorCommunity.ScQuests.RequestIdOf(base.OwnerQuest)); }
            catch (System.Exception) { return ""; }
        }
    }

    public override void SetupDisplay()
    {
        base.Description = Localization.Get("scObjectiveEncSiege");
    }

    public override BaseObjective Clone()
    {
        ObjectiveScEncSiege o = new ObjectiveScEncSiege();
        CopyValues(o);
        return o;
    }
}

[Preserve]
public class ObjectiveScTalkToSurvivor : ObjectiveInteractWithNPC
{
    public const string NameVar = "SurvivorName";

    public override void SetupDisplay()
    {
        string name = null;
        try
        {
            if (base.OwnerQuest != null && base.OwnerQuest.DataVariables != null) base.OwnerQuest.DataVariables.TryGetValue(NameVar, out name);
        }
        catch (System.Exception) { name = null; }
        if (string.IsNullOrEmpty(name)) name = Localization.Get("scObjectiveReturnWho");
        base.Description = string.Format(Localization.Get("scObjectiveReturn"), name);
        StatusText = "";
    }

    public override BaseObjective Clone()
    {
        ObjectiveScTalkToSurvivor o = new ObjectiveScTalkToSurvivor();
        CopyValues(o);
        return o;
    }
}
