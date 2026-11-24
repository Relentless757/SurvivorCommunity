using System;
using SurvivorCommunity;

// The band dialog's operators (E13a, Config/dialogs.xml; ScBands). Like DialogOpsEnclaves.cs: type="ScBand, SurvivorCommunity"
// resolves to the GLOBAL class DialogRequirementScBand, so the operators live outside the namespace. They find the person
// through the NPC the dialog passes (talkingTo: phases get the dialog's CurrentOwner) or else the player's CurrentNPC cvar,
// then ScBands' body lookup - one dictionary hit. Host only: a client keeps no band records - round MP2 (part M): a client's
// path reads what the server said at the talk's open (ScTalkContext: ScTalkFacts.Band, BandCome), and the join is a request
// (band.join, ScActionsMembers) on every machine.

/// <summary>
/// value="drifter": one of a drifter band, alive, not against the player (the phase picks their greeting); "trusted": ...
/// and trusting the player (they were helped in trouble); "wary": ... and not yet (the rows back to the greeting). Host only;
/// round MP2: a client reads the talk context (the phase is picked again when it lands: ScTalkContext).
/// </summary>
public class DialogRequirementScBand : BaseDialogRequirement
{
    public override bool CheckRequirement(EntityPlayer player, EntityNPC talkingTo)
    {
        try
        {
            string v = (Value ?? "").Trim().ToLowerInvariant();
            if (!ScCommunity.IsHost)
            {
                // round MP2: the client's path - the server's answer at the talk's open (0: not one of a drifter band with you)
                ScTalkFacts f = ScTalkContext.FactsFor(player, talkingTo);
                if (f == null || f.Band <= 0) return false;
                if (v == "trusted") return f.Band == 2;
                if (v == "wary") return f.Band == 1;
                return true;
            }
            Band b;
            BandPerson p;
            if (!ScBands.Talking(player, talkingTo, out b, out p) || b.IsRaiders || b.Trust < 0) return false;
            if (v == "trusted") return b.Trust > 0;
            if (v == "wary") return b.Trust == 0;
            return true;
        }
        catch (Exception e) { ScLog.Once("req-band", e); return false; }
    }
}

/// <summary>
/// "Come to our place.": value="yes" - offered (ScBands.HiddenWhy: drifters, not against you, not in their trouble's fight,
/// a home with room for all of them) and they say yes (they trust you, or their own answer: ScBands.Accepts); "no" - offered
/// and they say no (the same each time asked). Never offered otherwise: neither row shows. Host only; round MP2: a client
/// reads the talk context.
/// </summary>
public class DialogRequirementScBandCome : BaseDialogRequirement
{
    public override bool CheckRequirement(EntityPlayer player, EntityNPC talkingTo)
    {
        try
        {
            string v = (Value ?? "").Trim().ToLowerInvariant();
            if (!ScCommunity.IsHost)
            {
                // round MP2: the client's path - offered and their answer, as the server worked them out at the talk's open
                ScTalkFacts f = ScTalkContext.FactsFor(player, talkingTo);
                if (f == null || f.BandCome <= 0) return false;
                if (v == "yes") return f.BandCome == 1;
                if (v == "no") return f.BandCome == 2;
                return false;
            }
            Band b;
            BandPerson p;
            if (!ScBands.Talking(player, talkingTo, out b, out p) || ScBands.HiddenWhy(b, player) != null) return false;
            if (v == "yes") return ScBands.Accepts(b);
            if (v == "no") return !ScBands.Accepts(b);
            return false;
        }
        catch (Exception e) { ScLog.Once("req-bandcome", e); return false; }
    }
}

/// <summary>
/// "Welcome to the community.": the whole band joins, everything checked again (ScBands.Join); the reason when not. Round MP2:
/// band.join on every machine (inline on the server); JoinNow tells the player, a refusal is the answer's tooltip.
/// </summary>
public class DialogActionScBandJoin : BaseDialogAction
{
    public override void PerformAction(EntityPlayer player)
    {
        try
        {
            if (!(player is EntityPlayerLocal)) return;
            EntityAlive npc = ScFavours.CurrentNpc(player);
            ScActions.Request("band.join", r => ScTalkContext.Tell(player, r, false), npc != null ? npc.entityId : -1);
        }
        catch (Exception e) { ScLog.Once("act-bandjoin", e); }
    }
}
