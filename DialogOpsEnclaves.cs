using System;
using SurvivorCommunity;

// The enclave people's dialog operators (E2, Config/dialogs.xml). Like DialogOps.cs: type="ScEncRole, SurvivorCommunity"
// resolves to the GLOBAL class DialogRequirementScEncRole, so the operators live outside the namespace. They find the
// person through the NPC the dialog passes (talkingTo: phases get the dialog's CurrentOwner) or else the player's
// CurrentNPC cvar, then ScEnclavePeople's body lookup - one dictionary hit. Host only: a client keeps no enclave records.
// E3 adds trade, enlisting and recruiting (ScEnclaveDeals); E7 the leader's gift rows (ScEncGifts) and E12 the whole group's
// join (ScEncJoin), both at the end of this file.
// A row's greyed text is its FIRST requirement's description
// (DialogResponse.GetRequiredDescription), and a row's requirements stop at the first that fails (XUiC_DialogResponseEntry):
// so a row that can grey with a reason lists value="show" (Hide) first, whose description is the reason, then value="open".

namespace SurvivorCommunity
{
    /// <summary>The enclave dialog's helpers (E2): who is being talked to, and the tokens the line panel fills.</summary>
    public static class ScEncDialog
    {
        /// <summary>The enclave person behind the NPC being talked to, alive - at home, or (E3) enlisted with the player - or null. Host only.</summary>
        public static EncPerson PersonTalking(EntityPlayer player, EntityNPC talkingTo)
        {
            if (!ScCommunity.IsHost) return null;
            EntityAlive npc = talkingTo != null ? (EntityAlive)talkingTo : ScFavours.CurrentNpc(player);
            EncPerson p = ScEnclavePeople.PersonOf(npc);
            return p != null && p.Alive ? p : null;
        }

        /// <summary>The NPC being talked to and their enclave person (alive), or false. Host only.</summary>
        public static bool Talking(EntityPlayer player, EntityNPC talkingTo, out EntityAlive npc, out EncPerson p)
        {
            npc = null;
            p = null;
            if (!ScCommunity.IsHost) return false;
            npc = talkingTo != null ? (EntityAlive)talkingTo : ScFavours.CurrentNpc(player);
            p = ScEnclavePeople.PersonOf(npc);
            return p != null && p.Alive;
        }

        /// <summary>
        /// The line panel's tokens (XUiC_ScDialogLine) for an enclave respondent: {enc} the enclave's name, {leader} its
        /// leader, {count} how many living, {poi} the POI, {standing} the standing's word, {where} where the leader is from
        /// the player (E1's Where), {kind} the kind of group, {name} the respondent; E3's {terms} (the enlist fee and days),
        /// {until} (when one enlisted goes home) and {recruitcost} (what a recruit costs with the enclave); E4's {reqask},
        /// {reqwait} and {reqreply} (their request: the offer, the wait, the reply to the last answer - ScEncRequests.FillTokens);
        /// E6's {benefit} (what they do for an ally, doing it now or once allied: ScEncAllies.AboutText); E7's {giftreply} (the
        /// leader's answer to a gift: ScEncGifts.FillReply); E12's {joinfacts} (how many would join, what they bring and how
        /// they come: ScEncJoin.FactsText).
        /// A token with no value becomes "". The statement's shared Text is never written; a line of anybody else is returned
        /// as it is. vo2: voiceKey is the key of the request line put in (FillTokens), the name of its voice clip; else null.
        /// </summary>
        public static string Fill(string text, EntityAlive npc, EntityPlayer player, out string voiceKey)
        {
            voiceKey = null;
            if (string.IsNullOrEmpty(text) || text.IndexOf('{') < 0 || !ScCommunity.IsHost) return text;
            EncPerson p = ScEnclavePeople.PersonOf(npc);
            Enclave e = ScEnclavePeople.EnclaveOf(p);
            if (e == null) return text;
            EncPerson leader = ScEnclavePeople.LeaderOf(e);
            string s = text;
            if (s.IndexOf("{req", StringComparison.Ordinal) >= 0) s = ScEncRequests.FillTokens(s, e, out voiceKey);    // E4: first - its lines carry {enc} and the like
            if (s.IndexOf("{giftreply}", StringComparison.Ordinal) >= 0) s = ScEncGifts.FillReply(s, e, out voiceKey);  // E7: a gift's reply
            if (s.IndexOf("{terms}", StringComparison.Ordinal) >= 0) s = Token(s, "{terms}", ScEnclaveDeals.TermsText(e));
            if (s.IndexOf("{until}", StringComparison.Ordinal) >= 0) s = Token(s, "{until}", ScEnclaveDeals.UntilText(p));
            if (s.IndexOf("{recruitcost}", StringComparison.Ordinal) >= 0) s = Token(s, "{recruitcost}", ScEnclaveDeals.RecruitCostText(e));
            if (s.IndexOf("{benefit}", StringComparison.Ordinal) >= 0) s = Token(s, "{benefit}", ScEncAllies.AboutText(e));     // E6: what they do for an ally
            if (s.IndexOf("{joinfacts}", StringComparison.Ordinal) >= 0) s = Token(s, "{joinfacts}", ScEncJoin.FactsText(e));   // E12: who comes, what they bring
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
/// first statement by it); E3's value="enlisted": one enlisted with the player (their own statement). No value: any of them,
/// alive. Host only, one dictionary hit.
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
            if (v.Length == 0) return true;
            if (v == "enlisted") return p.Enlisted;
            return !p.Enlisted && p.Role == v;
        }
        catch (Exception e) { ScLog.Once("req-encrole", e); return false; }
    }
}

/// <summary>E3: the leader trades - an enclave that is not hostile, and their body names a trader that exists (ScEnclaveDeals.CanTrade). Host only.</summary>
public class DialogRequirementScEncCanTrade : BaseDialogRequirement
{
    public override bool CheckRequirement(EntityPlayer player, EntityNPC talkingTo)
    {
        try
        {
            EntityAlive npc;
            EncPerson p;
            return ScEncDialog.Talking(player, talkingTo, out npc, out p) && ScEnclaveDeals.CanTrade(p, npc);
        }
        catch (Exception e) { ScLog.Once("req-enccantrade", e); return false; }
    }
}

/// <summary>
/// E3, "Can you come with me?": value="show" - a member (never the leader) at home, of an enclave that is not hostile (put it
/// first: its description is the greyed row's reason); value="open" - they can come now (one per enclave, the follower
/// limit, the dukes: ScEnclaveDeals.EnlistBlocked). Host only.
/// </summary>
public class DialogRequirementScEncEnlist : BaseDialogRequirement
{
    public override bool CheckRequirement(EntityPlayer player, EntityNPC talkingTo)
    {
        try
        {
            EntityAlive npc;
            EncPerson p;
            if (!ScEncDialog.Talking(player, talkingTo, out npc, out p)) return false;
            string v = (Value ?? "").Trim().ToLowerInvariant();
            if (v == "show") return ScEnclaveDeals.CanAskEnlist(p);
            if (v == "open") return ScEnclaveDeals.EnlistBlocked(p, player) == null;
            return false;
        }
        catch (Exception e) { ScLog.Once("req-encenlist", e); return false; }
    }

    private string why = "";
    private int whyNpc = -1;
    private float whyAt = -10f;

    public override string GetRequiredDescription(EntityPlayer player)
    {
        try
        {
            EntityAlive npc;
            EncPerson p;
            if (!ScEncDialog.Talking(player, null, out npc, out p)) return "";
            // the response binding asks every frame while the row shows greyed: the reason is worked out again at most
            // twice a second, or for another NPC (the E3 review's L1)
            int id = npc != null ? npc.entityId : -1;
            float t = UnityEngine.Time.unscaledTime;
            if (id == whyNpc && t - whyAt < 0.5f) return why;
            whyNpc = id;
            whyAt = t;
            why = ScEnclaveDeals.EnlistBlocked(p, player) ?? "";
            return why;
        }
        catch (Exception e) { ScLog.Once("req-encenlist-why", e); return ""; }
    }
}

/// <summary>
/// E3, "Would you join us?": value="show" - a member (never the leader), at home or enlisted, of an enclave that is not
/// hostile (first: its description is the greyed row's reason); "open" - every gate passes (ScEnclaveDeals.RecruitBlocked);
/// "favour" - their favour comes first (Neutral, not done yet: NeedsFavour); "ask" - the row that leads to the confirm or
/// shows the reason: no favour needed, or a gate fails. Host only.
/// </summary>
public class DialogRequirementScEncRecruit : BaseDialogRequirement
{
    public override bool CheckRequirement(EntityPlayer player, EntityNPC talkingTo)
    {
        try
        {
            EntityAlive npc;
            EncPerson p;
            if (!ScEncDialog.Talking(player, talkingTo, out npc, out p)) return false;
            switch ((Value ?? "").Trim().ToLowerInvariant())
            {
                case "show": return ScEnclaveDeals.CanAskRecruit(p);
                case "open": return ScEnclaveDeals.RecruitBlocked(p, npc, player) == null;
                case "favour": return ScEnclaveDeals.NeedsFavour(p, npc, player);
                case "ask": return ScEnclaveDeals.RecruitBlocked(p, npc, player) != null || !ScEnclaveDeals.NeedsFavour(p, npc, player);
                default: return false;
            }
        }
        catch (Exception e) { ScLog.Once("req-encrecruit", e); return false; }
    }

    private string why = "";
    private int whyNpc = -1;
    private float whyAt = -10f;

    public override string GetRequiredDescription(EntityPlayer player)
    {
        try
        {
            EntityAlive npc;
            EncPerson p;
            if (!ScEncDialog.Talking(player, null, out npc, out p)) return "";
            // as the enlist row's reason: worked out at most twice a second, or for another NPC (the E3 review's L1)
            int id = npc != null ? npc.entityId : -1;
            float t = UnityEngine.Time.unscaledTime;
            if (id == whyNpc && t - whyAt < 0.5f) return why;
            whyNpc = id;
            whyAt = t;
            why = ScEnclaveDeals.RecruitBlocked(p, npc, player) ?? "";
            return why;
        }
        catch (Exception e) { ScLog.Once("req-encrecruit-why", e); return ""; }
    }
}

/// <summary>
/// E3, value="poi" | "supplies": the favour one of an enclave's people asks first (rolled the first time: ScEnclaveDeals.
/// EnsureFavour, unpinned, kept per person) is that kind. Host only.
/// </summary>
public class DialogRequirementScEncFavour : BaseDialogRequirement
{
    public override bool CheckRequirement(EntityPlayer player, EntityNPC talkingTo)
    {
        try
        {
            EntityAlive npc;
            EncPerson p;
            if (!ScEncDialog.Talking(player, talkingTo, out npc, out p)) return false;
            Prospect pr = ScEnclaveDeals.EnsureFavour(p, npc);
            if (pr == null) return false;
            string v = (Value ?? "").Trim().ToLowerInvariant();
            return v == "poi" ? pr.Favour == FavourKind.Poi : v == "supplies" && pr.Favour == FavourKind.Supplies;
        }
        catch (Exception e) { ScLog.Once("req-encfavour", e); return false; }
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

/// <summary>E3, "Deal.": the fee paid, they follow for EnclaveEnlistDays days (ScEnclaveDeals.Enlist); the reason when not. Host only.</summary>
public class DialogActionScEncEnlist : BaseDialogAction
{
    public override void PerformAction(EntityPlayer player)
    {
        try
        {
            if (!ScCommunity.IsHost) return;
            string reason;
            if (!ScEnclaveDeals.Enlist(player as EntityPlayerLocal, ScFavours.CurrentNpc(player), out reason)) ScCommunity.Tell(player, reason, "ui_denied");
        }
        catch (Exception e) { ScLog.Once("act-encenlist", e); }
    }
}

/// <summary>E3, "Go home now.": one enlisted with the player walks home (ScEnclaveDeals.GoHome). Host only.</summary>
public class DialogActionScEncGoHome : BaseDialogAction
{
    public override void PerformAction(EntityPlayer player)
    {
        try
        {
            if (!ScCommunity.IsHost) return;
            ScEnclaveDeals.GoHome(ScFavours.CurrentNpc(player), "the player sent them home");
        }
        catch (Exception e) { ScLog.Once("act-encgohome", e); }
    }
}

/// <summary>E3, "Welcome to the community.": they leave their enclave and join (ScEnclaveDeals.Recruit); the reason when not. Host only.</summary>
public class DialogActionScEncRecruit : BaseDialogAction
{
    public override void PerformAction(EntityPlayer player)
    {
        try
        {
            if (!ScCommunity.IsHost) return;
            string reason;
            if (!ScEnclaveDeals.Recruit(player as EntityPlayerLocal, ScFavours.CurrentNpc(player), out reason)) ScCommunity.Tell(player, reason, "ui_denied");
        }
        catch (Exception e) { ScLog.Once("act-encrecruit", e); }
    }
}

// ---------------------------------------------------------------- E7: gifts (ScEncGifts) ----

/// <summary>
/// E7, "I brought you something.": value="show" - the leader at home, of an enclave that is not hostile, with gifts on (put it
/// first: its description is the greyed row's words, "I brought you something. (Come back tomorrow.)"); value="open" - a gift
/// can be given now (under EnclaveGiftCap, past the day's wait, a bundle carried: ScEncGifts.Blocked). Host only.
/// </summary>
public class DialogRequirementScEncGift : BaseDialogRequirement
{
    public override bool CheckRequirement(EntityPlayer player, EntityNPC talkingTo)
    {
        try
        {
            Enclave e;
            EnclaveRequest r;
            if (!ScEncRequests.LeaderTalking(player, talkingTo, out e, out r)) return false;
            string v = (Value ?? "").Trim().ToLowerInvariant();
            if (v == "show") return ScEncGifts.Shown(e);
            if (v == "open") return ScEncGifts.Blocked(e, player) == null;
            return false;
        }
        catch (Exception e) { ScLog.Once("req-encgift", e); return false; }
    }

    private string why = "";
    private int whyNpc = -1;
    private float whyAt = -10f;

    public override string GetRequiredDescription(EntityPlayer player)
    {
        try
        {
            Enclave e;
            EnclaveRequest r;
            if (!ScEncRequests.LeaderTalking(player, null, out e, out r)) return "";
            // the response binding asks every frame while the row shows greyed: worked out again at most twice a second, or
            // for another enclave (a bag scan per resource)
            int id = e.Id;
            float t = UnityEngine.Time.unscaledTime;
            if (id == whyNpc && t - whyAt < 0.5f) return why;
            whyNpc = id;
            whyAt = t;
            why = ScEncGifts.WhyText(e, player);
            return why;
        }
        catch (Exception e) { ScLog.Once("req-encgift-why", e); return ""; }
    }
}

/// <summary>E7: value="0".."3": the gift pick on screen has that row (ScEncGiftList made it just before). Host only.</summary>
public class DialogRequirementScEncGiftRow : BaseDialogRequirement
{
    public override bool CheckRequirement(EntityPlayer player, EntityNPC talkingTo)
    {
        try
        {
            Enclave e;
            EnclaveRequest r;
            int i;
            if (!int.TryParse((Value ?? "").Trim(), out i) || !ScEncRequests.LeaderTalking(player, talkingTo, out e, out r)) return false;
            EntityAlive npc = talkingTo != null ? (EntityAlive)talkingTo : ScFavours.CurrentNpc(player);
            return ScEncGifts.PickRow(i, player, npc, e) != null;
        }
        catch (Exception e) { ScLog.Once("req-encgiftrow", e); return false; }
    }
}

/// <summary>E7, "I brought you something.": the pick of the bundles carried, at most four rows (ScEncGifts.ActList). Host only.</summary>
public class DialogActionScEncGiftList : BaseDialogAction
{
    public override void PerformAction(EntityPlayer player)
    {
        try { ScEncGifts.ActList(player); }
        catch (Exception e) { ScLog.Once("act-encgiftlist", e); }
    }
}

/// <summary>E7, value="0".."3": that row's bundle handed over as a gift, checked again (ScEncGifts.ActGive). Host only.</summary>
public class DialogActionScEncGift : BaseDialogAction
{
    public override void PerformAction(EntityPlayer player)
    {
        try
        {
            int i;
            if (int.TryParse((Value ?? "").Trim(), out i)) ScEncGifts.ActGive(player, i);
        }
        catch (Exception e) { ScLog.Once("act-encgift", e); }
    }
}

// ---------------------------------------------------------------- E12: the whole group joins (ScEncJoin) ----

/// <summary>
/// E12, "Would your people join ours?": value="show" - the leader at home of an Allied group at EnclaveJoinStanding or more,
/// with room in the community for all of them (ScEncJoin.Shown: never offered otherwise; put it first: its description is
/// the greyed row's words, "Would your people join ours? (Not while Mara is out with you)"); value="open" - they can come now
/// (nobody of theirs away, no request or radio call of theirs open, their group made, a home: ScEncJoin.Blocked). Host only.
/// </summary>
public class DialogRequirementScEncJoin : BaseDialogRequirement
{
    public override bool CheckRequirement(EntityPlayer player, EntityNPC talkingTo)
    {
        try
        {
            Enclave e;
            EnclaveRequest r;
            if (!ScEncRequests.LeaderTalking(player, talkingTo, out e, out r)) return false;
            string v = (Value ?? "").Trim().ToLowerInvariant();
            if (v == "show") return ScEncJoin.Shown(e);
            if (v == "open") return ScEncJoin.Blocked(e, player) == null;
            return false;
        }
        catch (Exception e) { ScLog.Once("req-encjoin", e); return false; }
    }

    private string why = "";
    private int whyEnc = -1;
    private float whyAt = -10f;

    public override string GetRequiredDescription(EntityPlayer player)
    {
        try
        {
            Enclave e;
            EnclaveRequest r;
            if (!ScEncRequests.LeaderTalking(player, null, out e, out r)) return "";
            // the response binding asks every frame while the row shows greyed: worked out again at most twice a second, or for
            // another enclave (a walk over the people and the members)
            float t = UnityEngine.Time.unscaledTime;
            if (e.Id == whyEnc && t - whyAt < 0.5f) return why;
            whyEnc = e.Id;
            whyAt = t;
            why = ScEncJoin.WhyText(e, player);
            return why;
        }
        catch (Exception e) { ScLog.Once("req-encjoin-why", e); return ""; }
    }
}

/// <summary>E12, "Welcome to the community.": the whole group joins, everything checked again (ScEncJoin.Join); the reason when not. Host only.</summary>
public class DialogActionScEncJoin : BaseDialogAction
{
    public override void PerformAction(EntityPlayer player)
    {
        try
        {
            if (!ScCommunity.IsHost) return;
            string reason;
            if (!ScEncJoin.Join(player as EntityPlayerLocal, ScFavours.CurrentNpc(player), out reason)) ScCommunity.Tell(player, reason, "ui_denied");
        }
        catch (Exception e) { ScLog.Once("act-encjoin", e); }
    }
}
