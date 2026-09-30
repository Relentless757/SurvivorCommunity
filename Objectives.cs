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
