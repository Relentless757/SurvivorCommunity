using System;
using SurvivorCommunity;

// The enclave people's dialog operators (E2, Config/dialogs.xml). Like DialogOps.cs: type="ScEncRole, SurvivorCommunity"
// resolves to the GLOBAL class DialogRequirementScEncRole, so the operators live outside the namespace. They find the
// person through the NPC the dialog passes (talkingTo: phases get the dialog's CurrentOwner) or else the player's
// CurrentNPC cvar, then ScEnclavePeople's body lookup - one dictionary hit. Host only: a client keeps no enclave records.

namespace SurvivorCommunity
{
    /// <summary>The enclave dialog's helpers (E2): who is being talked to, and the tokens the line panel fills.</summary>
    public static class ScEncDialog
    {
        /// <summary>The enclave person behind the NPC being talked to, at home (alive, with their group), or null. Host only.</summary>
        public static EncPerson PersonTalking(EntityPlayer player, EntityNPC talkingTo)
        {
            if (!ScCommunity.IsHost) return null;
            EntityAlive npc = talkingTo != null ? (EntityAlive)talkingTo : ScFavours.CurrentNpc(player);
            EncPerson p = ScEnclavePeople.PersonOf(npc);
            return p != null && p.State == EncPerson.StateHome ? p : null;
        }

        /// <summary>
        /// The line panel's tokens (XUiC_ScDialogLine) for an enclave respondent: {enc} the enclave's name, {leader} its
        /// leader, {count} how many living, {poi} the POI, {standing} the standing's word, {where} where the leader is from
        /// the player (E1's Where), {kind} the kind of group, {name} the respondent. A token with no value becomes "". The
        /// statement's shared Text is never written; a line of anybody else is returned as it is.
        /// </summary>
        public static string Fill(string text, EntityAlive npc, EntityPlayer player)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('{') < 0 || !ScCommunity.IsHost) return text;
            EncPerson p = ScEnclavePeople.PersonOf(npc);
            Enclave e = ScEnclavePeople.EnclaveOf(p);
            if (e == null) return text;
            EncPerson leader = ScEnclavePeople.LeaderOf(e);
            string s = text;
            s = Token(s, "{enc}", e.Name);
            s = Token(s, "{leader}", leader != null ? leader.Name : "");
            s = Token(s, "{count}", s.IndexOf("{count}", StringComparison.Ordinal) >= 0 ? ScEnclavePeople.Living(e).Count.ToString() : "");
            s = Token(s, "{poi}", e.PoiName);
            s = Token(s, "{standing}", ScEnclaves.StandingWord(e));
            s = Token(s, "{kind}", ScEnclavePeople.KindName(e.Kind));
            s = Token(s, "{name}", p.Name);
            if (s.IndexOf("{where}", StringComparison.Ordinal) >= 0) s = Token(s, "{where}", Where(leader, player));
            return s;
        }

        private static string Token(string s, string token, string value)
        {
            return s.IndexOf(token, StringComparison.Ordinal) >= 0 ? s.Replace(token, value ?? "") : s;
        }

        /// <summary>"12 m north-east": the leader's body, else their spot, from the player; "" without a leader or a player.</summary>
        private static string Where(EncPerson leader, EntityPlayer player)
        {
            if (leader == null || player == null) return "";
            World w = ScCommunity.World;
            EntityAlive body = leader.EntityId >= 0 && w != null ? ScNpc.As(ScNpc.Loaded(w, leader.EntityId)) : null;
            if (body != null) return ScEnclaves.Where(player.position, body.position);
            return leader.HasHome ? ScEnclaves.Where(player.position, leader.HomeCentre) : "";
        }
    }
}

// ---------------------------------------------------------------- requirements ----

/// <summary>
/// value="leader" | "member": the NPC is one of an enclave's people, at home, in that role (E2: the dialog's phases pick the
/// first statement by it). Host only, one dictionary hit.
/// </summary>
public class DialogRequirementScEncRole : BaseDialogRequirement
{
    public override bool CheckRequirement(EntityPlayer player, EntityNPC talkingTo)
    {
        try
        {
            EncPerson p = ScEncDialog.PersonTalking(player, talkingTo);
            if (p == null) return false;
            string v = (Value ?? "").Trim().ToLowerInvariant();
            return v.Length == 0 || p.Role == v;
        }
        catch (Exception e) { ScLog.Once("req-encrole", e); return false; }
    }
}

/// <summary>
/// value="neutral" | "friendly" | "allied": the NPC's enclave stands there with the player (ScEnclaves.StandingKey; a hostile
/// enclave matches none, so its people have no greeting - X9 keeps them from being talked to anyway). Host only.
/// </summary>
public class DialogRequirementScEncStanding : BaseDialogRequirement
{
    public override bool CheckRequirement(EntityPlayer player, EntityNPC talkingTo)
    {
        try
        {
            Enclave e = ScEnclavePeople.EnclaveOf(ScEncDialog.PersonTalking(player, talkingTo));
            return e != null && ScEnclaves.StandingKey(e) == (Value ?? "").Trim().ToLowerInvariant();
        }
        catch (Exception e) { ScLog.Once("req-encstanding", e); return false; }
    }
}

// ---------------------------------------------------------------- actions ----

/// <summary>
/// "I'm not asking." (E2, Q6 a): the leader hands over EnclaveThreatBundlesMin..Max bundles of what they have and the
/// enclave turns hostile for good after EnclaveThreatGrace seconds (ScEnclavePeople.Threaten). Host only.
/// </summary>
public class DialogActionScEncThreat : BaseDialogAction
{
    public override void PerformAction(EntityPlayer player)
    {
        try
        {
            ScEnclavePeople.Threaten(player as EntityPlayerLocal, ScFavours.CurrentNpc(player));
        }
        catch (Exception e) { ScLog.Once("act-encthreat", e); }
    }
}
