using System;
using SurvivorCommunity;

// The band dialog's operators (E13a, Config/dialogs.xml; ScBands). Like DialogOpsEnclaves.cs: type="ScBand, SurvivorCommunity"
// resolves to the GLOBAL class DialogRequirementScBand, so the operators live outside the namespace. They find the person
// through the NPC the dialog passes (talkingTo: phases get the dialog's CurrentOwner) or else the player's CurrentNPC cvar,
// then ScBands' body lookup - one dictionary hit. Host only: a client keeps no band records.

/// <summary>
/// value="drifter": one of a drifter band, alive, not against the player (the phase picks their greeting); "trusted": ...
/// and trusting the player (they were helped in trouble); "wary": ... and not yet (the rows back to the greeting). Host only.
/// </summary>
public class DialogRequirementScBand : BaseDialogRequirement
{
    public override bool CheckRequirement(EntityPlayer player, EntityNPC talkingTo)
    {
        try
        {
            Band b;
            BandPerson p;
            if (!ScBands.Talking(player, talkingTo, out b, out p) || b.IsRaiders || b.Trust < 0) return false;
            string v = (Value ?? "").Trim().ToLowerInvariant();
            if (v == "trusted") return b.Trust > 0;
            if (v == "wary") return b.Trust == 0;
            return true;
        }
        catch (Exception e) { ScLog.Once("req-band", e); return false; }
    }
}

/// <summary>
/// "Come to our place.": value="yes" - offered (ScBands.HiddenWhy: drifters, not against you, a home with room for all of
/// them) and they say yes (they trust you, or their own answer: ScBands.Accepts); "no" - offered and they say no (the same
/// each time asked). Never offered otherwise: neither row shows. Host only.
/// </summary>
public class DialogRequirementScBandCome : BaseDialogRequirement
{
    public override bool CheckRequirement(EntityPlayer player, EntityNPC talkingTo)
    {
        try
        {
            Band b;
            BandPerson p;
            if (!ScBands.Talking(player, talkingTo, out b, out p) || ScBands.HiddenWhy(b, player) != null) return false;
            string v = (Value ?? "").Trim().ToLowerInvariant();
            if (v == "yes") return ScBands.Accepts(b);
            if (v == "no") return !ScBands.Accepts(b);
            return false;
        }
        catch (Exception e) { ScLog.Once("req-bandcome", e); return false; }
    }
}

/// <summary>"Welcome to the community.": the whole band joins, everything checked again (ScBands.Join); the reason when not. Host only.</summary>
public class DialogActionScBandJoin : BaseDialogAction
{
    public override void PerformAction(EntityPlayer player)
    {
        try
        {
            if (!ScCommunity.IsHost) return;
            string reason;
            if (!ScBands.Join(player as EntityPlayerLocal, ScFavours.CurrentNpc(player), out reason)) ScCommunity.Tell(player, reason, "ui_denied");
        }
        catch (Exception e) { ScLog.Once("act-bandjoin", e); }
    }
}
