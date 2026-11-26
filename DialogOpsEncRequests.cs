using System;
using SurvivorCommunity;

// The enclave requests' dialog operators (E4, Config/dialogs.xml; ScEncRequests). Like DialogOps.cs and
// DialogOpsEnclaves.cs: type="ScEncReq, SurvivorCommunity" resolves to the GLOBAL class DialogRequirementScEncReq, so the
// operators live outside the namespace. Requirements are the leader's only (at home, of an enclave that is not hostile),
// answer from the records and roll nothing; they run when a statement's rows are built, not every frame. Actions check
// everything again (the rows were built a moment earlier) and leave the leader's reply for the next statement ({reqreply}).
// Round MP5 (part E): every community player talks to the leader. On the server (single player, the host's own player)
// every operator runs as before; on a client a requirement reads the talk's facts (ScTalkContext; ScEncTalk) and the client's
// own bag, and an action is an ScActionsEnclave op whose answer is the reply line (shown "..." until it comes).
// E5 adds the stage rows ("I found <name>. I'm sorry.", "I already took care of it.", "I can't do it." only where it can be
// given up) and the found survivor's own talk (ScEncSearch: "Your people sent me. Let's get you home.", "Wait here for me.").

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
            if (!ScCommunity.IsHost)
            {
                ScTalkFacts f = ScEncTalk.Of(player, talkingTo);      // round MP5 (part E): the request's state the server said
                return ScEncTalk.Has(f, ScTalkFacts.EncLeaderTalking) && ScEncReqOps.Listed(Value, f.EncReqState);
            }
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
            if (!ScCommunity.IsHost)
            {
                ScTalkFacts f = ScEncTalk.Of(player, talkingTo);      // round MP5 (part E)
                return ScEncReqOps.HasRequest(f) && ScEncReqOps.Listed(Value, f.EncReqKind);
            }
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
            if (!ScCommunity.IsHost)
            {
                // round MP5 (part E): the facts' request against this machine's own bag
                ScTalkFacts f = ScEncTalk.Of(player, talkingTo);
                return ScEncReqOps.HasRequest(f) && ScEncRequests.ClientHas(player as EntityPlayerLocal, f.EncReqKind, f.EncReqRes, f.EncReqUnits);
            }
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
            if (!int.TryParse((Value ?? "").Trim(), out i)) return false;
            if (!ScCommunity.IsHost)
            {
                // round MP5 (part E): this machine's own pick (ScEncTalk.ReqGunList built it from its own bag)
                ScTalkFacts f = ScEncTalk.Of(player, talkingTo);
                return ScEncReqOps.HasRequest(f) && ScEncRequests.PickRowClient(i, player as EntityPlayerLocal, f.Npc, f.EncReqId) != null;
            }
            if (!ScEncRequests.LeaderTalking(player, talkingTo, out e, out r) || r == null) return false;
            EntityAlive npc = talkingTo != null ? (EntityAlive)talkingTo : ScFavours.CurrentNpc(player);
            return ScEncRequests.PickRow(i, player, npc, r) != null;
        }
        catch (Exception e) { ScLog.Once("req-encreqgun", e); return false; }
    }
}

/// <summary>
/// E5: value = a comma list of stage words (ScEncRequests.StageWord: a missing survivor's "find", "follow", "return"; an
/// infestation's "clear", "cleared"; a siege's "called", "live"), a leading "!" for none of them: the leader's open request
/// is (or is not) at that stage. E4's kinds have no stage word - they never match a list, and always match a "!" one. Host only.
/// </summary>
public class DialogRequirementScEncReqStage : BaseDialogRequirement
{
    public override bool CheckRequirement(EntityPlayer player, EntityNPC talkingTo)
    {
        try
        {
            Enclave e;
            EnclaveRequest r;
            string stage;
            if (!ScCommunity.IsHost)
            {
                ScTalkFacts f = ScEncTalk.Of(player, talkingTo);      // round MP5 (part E): the stage word the server said
                if (!ScEncReqOps.HasRequest(f)) return false;
                stage = f.EncReqStage;
            }
            else
            {
                if (!ScEncRequests.LeaderTalking(player, talkingTo, out e, out r) || r == null) return false;
                stage = ScEncRequests.StageWord(r);
            }
            string v = (Value ?? "").Trim();
            bool not = v.StartsWith("!", StringComparison.Ordinal);
            bool hit = ScEncReqOps.Listed(not ? v.Substring(1) : v, stage);
            return not ? !hit : hit;
        }
        catch (Exception e) { ScLog.Once("req-encreqstage", e); return false; }
    }
}

/// <summary>E5: "I can't do it." shows - the open request is accepted and one the player may give up (ScEncRequests.CanGiveUp). Host only.</summary>
public class DialogRequirementScEncReqCanGiveUp : BaseDialogRequirement
{
    public override bool CheckRequirement(EntityPlayer player, EntityNPC talkingTo)
    {
        try
        {
            if (!ScCommunity.IsHost)
            {
                ScTalkFacts f = ScEncTalk.Of(player, talkingTo);      // round MP5 (part E)
                return ScEncReqOps.HasRequest(f) && f.Enc(ScTalkFacts.EncCanGiveUp);
            }
            Enclave e;
            EnclaveRequest r;
            return ScEncRequests.LeaderTalking(player, talkingTo, out e, out r) && ScEncRequests.CanGiveUp(r);
        }
        catch (Exception e) { ScLog.Once("req-encreqcangiveup", e); return false; }
    }
}

/// <summary>
/// E5: value="alive" | "following": the NPC is a missing survivor's body (ScEncSearch), found and waiting, or following the
/// player home (the dialog's phases pick their statement by it). Host only, one dictionary hit and a short list scan.
/// </summary>
public class DialogRequirementScEncSearch : BaseDialogRequirement
{
    public override bool CheckRequirement(EntityPlayer player, EntityNPC talkingTo)
    {
        try
        {
            EntityAlive npc;
            EncPerson p;
            EnclaveRequest r;
            string v = (Value ?? "").Trim().ToLowerInvariant();
            if (!ScCommunity.IsHost)
            {
                // round MP5 (part E): the stage the server said (1 alive and waiting, 2 following)
                ScTalkFacts f = ScEncTalk.Of(player, talkingTo);
                int stage = f != null ? f.EncSearch : 0;
                if (v == "following") return stage == 2;
                if (v == "alive") return stage == 1;
                return stage == 1 || stage == 2;
            }
            if (!ScEncSearch.Talking(player, talkingTo, out npc, out p, out r)) return false;
            if (v == "following") return r.Stage == ScEncSearch.StageFollowing;
            if (v == "alive") return r.Stage == ScEncSearch.StageAlive;
            return r.Stage == ScEncSearch.StageAlive || r.Stage == ScEncSearch.StageFollowing;
        }
        catch (Exception e) { ScLog.Once("req-encsearch", e); return false; }
    }
}

// ---------------------------------------------------------------- actions ----

/// <summary>"Leave it with me.": the offer accepted - due in EnclaveDueDaysItems days, the journal quest and its marker (ScEncRequests.ActAccept). Host only.</summary>
public class DialogActionScEncReqAccept : BaseDialogAction
{
    public override void PerformAction(EntityPlayer player)
    {
        try
        {
            if (!ScCommunity.IsHost) { ScEncTalk.Req(player, ScActionsEnclave.ReqAccept); return; }     // round MP5 (part E): enc.req
            ScEncRequests.ActAccept(player);
        }
        catch (Exception e) { ScLog.Once("act-encreqaccept", e); }
    }
}

/// <summary>"I have it right here.": the bundles or the generator handed over, the request done (ScEncRequests.ActGive). Host only.</summary>
public class DialogActionScEncReqGive : BaseDialogAction
{
    public override void PerformAction(EntityPlayer player)
    {
        try
        {
            if (!ScCommunity.IsHost) { ScEncTalk.ReqGive(player); return; }     // round MP5 (part E): taken from this bag, then enc.reqgive
            ScEncRequests.ActGive(player);
        }
        catch (Exception e) { ScLog.Once("act-encreqgive", e); }
    }
}

/// <summary>"Take one of these.": the gun pick for this talk - the 2 least and the 2 most valuable guns carried (ScEncRequests.ActGunList). Host only.</summary>
public class DialogActionScEncReqGunList : BaseDialogAction
{
    public override void PerformAction(EntityPlayer player)
    {
        try
        {
            if (!ScCommunity.IsHost) { ScEncTalk.ReqGunList(player); return; }  // round MP5 (part E): this machine's own guns
            ScEncRequests.ActGunList(player);
        }
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
            if (!int.TryParse((Value ?? "").Trim(), out i)) return;
            if (!ScCommunity.IsHost) { ScEncTalk.ReqGiveGun(player, i); return; }     // round MP5 (part E): from its slot, then enc.reqgun
            ScEncRequests.ActGiveGun(player, i);
        }
        catch (Exception e) { ScLog.Once("act-encreqgivegun", e); }
    }
}

/// <summary>"No, I can't help with that.": the offer declined - a failure now (ScEncRequests.ActDecline). Host only.</summary>
public class DialogActionScEncReqDecline : BaseDialogAction
{
    public override void PerformAction(EntityPlayer player)
    {
        try
        {
            if (!ScCommunity.IsHost) { ScEncTalk.Req(player, ScActionsEnclave.ReqDecline); return; }     // round MP5 (part E): enc.req
            ScEncRequests.ActDecline(player);
        }
        catch (Exception e) { ScLog.Once("act-encreqdecline", e); }
    }
}

/// <summary>"Let me think about it.": nothing now - the talk closes, the offer stands (ScEncRequests.ActThink). Host only.</summary>
public class DialogActionScEncReqThink : BaseDialogAction
{
    public override void PerformAction(EntityPlayer player)
    {
        try
        {
            if (!ScCommunity.IsHost) { ScEncTalk.Req(player, ScActionsEnclave.ReqThink); return; }     // round MP5 (part E): enc.req
            ScEncRequests.ActThink(player);
        }
        catch (Exception e) { ScLog.Once("act-encreqthink", e); }
    }
}

/// <summary>"I can't do it.": the accepted request failed now (ScEncRequests.ActCantDo). Host only.</summary>
public class DialogActionScEncReqCantDo : BaseDialogAction
{
    public override void PerformAction(EntityPlayer player)
    {
        try
        {
            if (!ScCommunity.IsHost) { ScEncTalk.Req(player, ScActionsEnclave.ReqCantDo); return; }     // round MP5 (part E): enc.req
            ScEncRequests.ActCantDo(player);
        }
        catch (Exception e) { ScLog.Once("act-encreqcantdo", e); }
    }
}

/// <summary>E5, "I found &lt;name&gt;. I'm sorry.": a missing survivor's search done once their remains were searched (ScEncRequests.ActFound). Host only.</summary>
public class DialogActionScEncReqFound : BaseDialogAction
{
    public override void PerformAction(EntityPlayer player)
    {
        try
        {
            if (!ScCommunity.IsHost) { ScEncTalk.Req(player, ScActionsEnclave.ReqFound); return; }     // round MP5 (part E): enc.req
            ScEncRequests.ActFound(player);
        }
        catch (Exception e) { ScLog.Once("act-encreqfound", e); }
    }
}

/// <summary>E5, "I already took care of it.": an infestation request whose target was cleared already, done on the spot (ScEncRequests.ActCleared). Host only.</summary>
public class DialogActionScEncReqCleared : BaseDialogAction
{
    public override void PerformAction(EntityPlayer player)
    {
        try
        {
            if (!ScCommunity.IsHost) { ScEncTalk.Req(player, ScActionsEnclave.ReqCleared); return; }     // round MP5 (part E): enc.req
            ScEncRequests.ActCleared(player);
        }
        catch (Exception e) { ScLog.Once("act-encreqcleared", e); }
    }
}

/// <summary>E5, "Your people sent me. Let's get you home.": a missing survivor follows the player home (ScEncSearch.ActFollow). Host only.</summary>
public class DialogActionScEncFollow : BaseDialogAction
{
    public override void PerformAction(EntityPlayer player)
    {
        try
        {
            if (!ScCommunity.IsHost) { ScEncTalk.Search(player, true); return; }     // round MP5 (part E): enc.follow
            ScEncSearch.ActFollow(player);
        }
        catch (Exception e) { ScLog.Once("act-encfollow", e); }
    }
}

/// <summary>E5, "Wait here for me.": a missing survivor following the player waits where they stand (ScEncSearch.ActWait). Host only.</summary>
public class DialogActionScEncWait : BaseDialogAction
{
    public override void PerformAction(EntityPlayer player)
    {
        try
        {
            if (!ScCommunity.IsHost) { ScEncTalk.Search(player, false); return; }    // round MP5 (part E): enc.wait
            ScEncSearch.ActWait(player);
        }
        catch (Exception e) { ScLog.Once("act-encwait", e); }
    }
}

namespace SurvivorCommunity
{
    /// <summary>The request operators' shared helper.</summary>
    public static class ScEncReqOps
    {
        /// <summary>Round MP5 (part E), client: the facts say the leader is talking and has an open request (the host's "r != null").</summary>
        internal static bool HasRequest(ScTalkFacts f)
        {
            return ScEncTalk.Has(f, ScTalkFacts.EncLeaderTalking) && f.EncReqState.Length > 0 && f.EncReqState != "none";
        }

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
