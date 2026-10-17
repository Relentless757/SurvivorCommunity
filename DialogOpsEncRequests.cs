using System;
using SurvivorCommunity;

// The enclave requests' dialog operators (E4, Config/dialogs.xml; ScEncRequests). Like DialogOps.cs and
// DialogOpsEnclaves.cs: type="ScEncReq, SurvivorCommunity" resolves to the GLOBAL class DialogRequirementScEncReq, so the
// operators live outside the namespace. Requirements are the leader's only (at home, of an enclave that is not hostile),
// answer from the records and roll nothing; they run when a statement's rows are built, not every frame. Actions check
// everything again (the rows were built a moment earlier) and leave the leader's reply for the next statement ({reqreply}).
// Host only: a client keeps no enclave records, so on a client every requirement fails and no request row shows.

// ---------------------------------------------------------------- requirements ----

/// <summary>
/// value="none" | "offered" | "accepted", or a comma list of them: the leader's enclave has no open request, or one in that
/// state (the three "What do you need?" rows pick the statement by it; the answers show by it). Host only.
/// </summary>
public class DialogRequirementScEncReq : BaseDialogRequirement
{
    public override bool CheckRequirement(EntityPlayer player, EntityNPC talkingTo)
    {
        try
        {
            Enclave e;
            EnclaveRequest r;
            if (!ScEncRequests.LeaderTalking(player, talkingTo, out e, out r)) return false;
            return ScEncReqOps.Listed(Value, ScEncRequests.StateWord(r));
        }
        catch (Exception e) { ScLog.Once("req-encreq", e); return false; }
    }
}

/// <summary>value="resource" | "generator" | "gun", or a comma list: the leader's open request is of that kind. Host only.</summary>
public class DialogRequirementScEncReqKind : BaseDialogRequirement
{
    public override bool CheckRequirement(EntityPlayer player, EntityNPC talkingTo)
    {
        try
        {
            Enclave e;
            EnclaveRequest r;
            return ScEncRequests.LeaderTalking(player, talkingTo, out e, out r) && r != null && ScEncReqOps.Listed(Value, r.Kind);
        }
        catch (Exception e) { ScLog.Once("req-encreqkind", e); return false; }
    }
}

/// <summary>The player carries what the open request asks: its units in bundles of that resource, a generator, a gun that counts. Host only.</summary>
public class DialogRequirementScEncReqHas : BaseDialogRequirement
{
    public override bool CheckRequirement(EntityPlayer player, EntityNPC talkingTo)
    {
        try
        {
            Enclave e;
            EnclaveRequest r;
            return ScEncRequests.LeaderTalking(player, talkingTo, out e, out r) && ScEncRequests.PlayerHas(r, player);
        }
        catch (Exception e) { ScLog.Once("req-encreqhas", e); return false; }
    }
}

/// <summary>value="0".."3": the gun pick on screen has that row (ScEncReqGunList made it just before). Host only.</summary>
public class DialogRequirementScEncReqGun : BaseDialogRequirement
{
    public override bool CheckRequirement(EntityPlayer player, EntityNPC talkingTo)
    {
        try
        {
            Enclave e;
            EnclaveRequest r;
            int i;
            if (!int.TryParse((Value ?? "").Trim(), out i) || !ScEncRequests.LeaderTalking(player, talkingTo, out e, out r) || r == null) return false;
            EntityAlive npc = talkingTo != null ? (EntityAlive)talkingTo : ScFavours.CurrentNpc(player);
            return ScEncRequests.PickRow(i, player, npc, r) != null;
        }
        catch (Exception e) { ScLog.Once("req-encreqgun", e); return false; }
    }
}

// ---------------------------------------------------------------- actions ----

/// <summary>"Leave it with me.": the offer accepted - due in EnclaveDueDaysItems days, the journal quest and its marker (ScEncRequests.ActAccept). Host only.</summary>
public class DialogActionScEncReqAccept : BaseDialogAction
{
    public override void PerformAction(EntityPlayer player)
    {
        try { ScEncRequests.ActAccept(player); }
        catch (Exception e) { ScLog.Once("act-encreqaccept", e); }
    }
}

/// <summary>"I have it right here.": the bundles or the generator handed over, the request done (ScEncRequests.ActGive). Host only.</summary>
public class DialogActionScEncReqGive : BaseDialogAction
{
    public override void PerformAction(EntityPlayer player)
    {
        try { ScEncRequests.ActGive(player); }
        catch (Exception e) { ScLog.Once("act-encreqgive", e); }
    }
}

/// <summary>"Take one of these.": the gun pick for this talk - the 2 least and the 2 most valuable guns carried (ScEncRequests.ActGunList). Host only.</summary>
public class DialogActionScEncReqGunList : BaseDialogAction
{
    public override void PerformAction(EntityPlayer player)
    {
        try { ScEncRequests.ActGunList(player); }
        catch (Exception e) { ScLog.Once("act-encreqgunlist", e); }
    }
}

/// <summary>value="0".."3": that row's gun taken from its exact slot, the request done for its points (ScEncRequests.ActGiveGun). Host only.</summary>
public class DialogActionScEncReqGiveGun : BaseDialogAction
{
    public override void PerformAction(EntityPlayer player)
    {
        try
        {
            int i;
            if (int.TryParse((Value ?? "").Trim(), out i)) ScEncRequests.ActGiveGun(player, i);
        }
        catch (Exception e) { ScLog.Once("act-encreqgivegun", e); }
    }
}

/// <summary>"No, I can't help with that.": the offer declined - a failure now (ScEncRequests.ActDecline). Host only.</summary>
public class DialogActionScEncReqDecline : BaseDialogAction
{
    public override void PerformAction(EntityPlayer player)
    {
        try { ScEncRequests.ActDecline(player); }
        catch (Exception e) { ScLog.Once("act-encreqdecline", e); }
    }
}

/// <summary>"Let me think about it.": nothing now - the talk closes, the offer stands (ScEncRequests.ActThink). Host only.</summary>
public class DialogActionScEncReqThink : BaseDialogAction
{
    public override void PerformAction(EntityPlayer player)
    {
        try { ScEncRequests.ActThink(player); }
        catch (Exception e) { ScLog.Once("act-encreqthink", e); }
    }
}

/// <summary>"I can't do it.": the accepted request failed now (ScEncRequests.ActCantDo). Host only.</summary>
public class DialogActionScEncReqCantDo : BaseDialogAction
{
    public override void PerformAction(EntityPlayer player)
    {
        try { ScEncRequests.ActCantDo(player); }
        catch (Exception e) { ScLog.Once("act-encreqcantdo", e); }
    }
}

namespace SurvivorCommunity
{
    /// <summary>The request operators' shared helper.</summary>
    public static class ScEncReqOps
    {
        /// <summary>Is 'word' one of the comma list 'value' (trimmed, any case)?</summary>
        public static bool Listed(string value, string word)
        {
            if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(word)) return false;
            foreach (string part in value.Split(','))
                if (string.Equals(part.Trim(), word, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
