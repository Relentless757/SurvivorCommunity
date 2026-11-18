using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>Round m1v: the moments an enclave or a drifter band contacts the player (ScEncRadio's lines).</summary>
    public enum EncCall
    {
        /// <summary>A request offered, announced on the radio (ScEncRequests.Offer). Recorded in round V-b: text only until then.</summary>
        Offer,
        /// <summary>A siege called (ScEncSiege.Call). Recorded in round V-b: text only until then.</summary>
        Siege,
        /// <summary>The m1v review's fix 8: half the siege's time to answer gone with nobody there, they call once more (ScEncSiege.TickHurry). Recorded in round V-b.</summary>
        SiegeHurry,
        /// <summary>An ally answers "Send a fighter" (ScEncCalls.Call).</summary>
        Fighter,
        /// <summary>An ally answers "Supply drop" (ScEncCalls.Call).</summary>
        Drop,
        /// <summary>An ally's call found no way through (ScEncCalls.Fail).</summary>
        Fail,
        /// <summary>The ally's fighter comes up to the player (ScEncCalls' greeting): said by the fighter, in person.</summary>
        Hello,
        /// <summary>A raider enclave's demand on an outpost (ScEncRaids.Demand).</summary>
        Demand,
        /// <summary>The demand paid (ScEncRaids.OnPaid).</summary>
        Paid,
        /// <summary>The demand refused (ScOutpostRisk.RefuseDemand).</summary>
        Refused,
        /// <summary>A Friendly or Allied group moves: they lost too many (ScEncMoves.Move).</summary>
        MoveLost,
        /// <summary>... the dead next door (an infestation).</summary>
        MoveNest,
        /// <summary>... any other reason (the console's).</summary>
        Move,
        /// <summary>A whole group joins the player and makes its own way (ScEncJoin.JoinNow): the leader, as the talk closes.</summary>
        Join,
        /// <summary>A whole group joins the player and follows them home.</summary>
        JoinFollow,
        /// <summary>Drifters thank the player for their help against the dead (ScBands.EndTrouble).</summary>
        BandThanks,
        /// <summary>A drifter band joins the player (ScBands.JoinNow), as the talk closes.</summary>
        BandJoin
    }

    /// <summary>
    /// Round m1v, voiced enclave radio calls (ROADMAP item 2, round V-a; the user's decisions 2026-11-17). When an enclave or a
    /// drifter band contacts the player, the toast they get today shows as before, and the speaker's own recorded voice says
    /// their words (RadioSpeaker, ScRadioSpeaker.Say): in person when their body stands near, else on the radio - heard aloud
    /// only while the player carries a handheld radio or stands within 30 m of a powered SC radio station (at home, an
    /// outpost's relay, or with no home any: ScRadioSpeaker.Hears); otherwise the toast alone. Wiring only: the lines recorded
    /// in round vo2 (Tools/voice_lines.py's RUNTIME_FIXED) are said; a key with no recording stays text only - the siege call
    /// (scEncSiegeCallSaid_1..3), its half-time repeat (scEncSiegeHurrySaid) and the request offer (scEncReqHeardSaid_&lt;kind&gt;)
    /// go through the same path, so round V-b only has to add their clips.
    ///
    /// Who speaks for an enclave: its leader at home; with the leader dead or away, the next living one of its people in
    /// LeaderOf's order - the succession's (ScEnclavePeople.Succeed: at home first, the highest class tier, the one made
    /// first), then one out raiding; never one enlisted with the player (they are beside them). Nobody (or a group whose
    /// people were never made): the toast alone. A fighter's greeting is the fighter's own; a band's lines its leader's.
    /// The queue's priority (ScRadioTalk): a siege call above the horde warnings and a runner's moments, every other line
    /// below them. EnclaveVoiceLines false: the toasts alone, as before. Host only, main thread.
    /// </summary>
    public static class ScEncRadio
    {
        /// <summary>The counters' word for these lines (sc radio).</summary>
        public const string Tag = "enc";

        /// <summary>The siege call's lines, recorded in round V-b ("They're coming over the walls!" and two more).</summary>
        public static readonly string[] SiegeKeys = { "scEncSiegeCallSaid_1", "scEncSiegeCallSaid_2", "scEncSiegeCallSaid_3" };
        /// <summary>The request offer's on-air line, recorded in round V-b: this stem plus the request's kind.</summary>
        public const string OfferStem = "scEncReqHeardSaid_";
        /// <summary>The m1v review's fix 8: the siege's half-time call (its words are this Localization row), recorded in round V-b.</summary>
        public const string HurryKey = "scEncSiegeHurrySaid";

        /// <summary>The line(s) of a moment (kind: the request's, for an offer); one is picked among those the speaker's voice has.</summary>
        public static string[] KeysFor(EncCall call, string kind)
        {
            switch (call)
            {
                case EncCall.Offer: return new[] { OfferStem + (string.IsNullOrEmpty(kind) ? EnclaveRequest.KindResource : kind) };
                case EncCall.Siege: return SiegeKeys;
                case EncCall.SiegeHurry: return new[] { HurryKey };
                case EncCall.Fighter: return new[] { "scEncCallFighterSaid" };
                case EncCall.Drop: return new[] { "scEncCallDropSaid" };
                case EncCall.Fail: return new[] { "scEncCallFailSaid" };
                case EncCall.Hello: return new[] { "scEncCallHello" };
                case EncCall.Demand: return new[] { "scEncRaidDemandSaid" };
                case EncCall.Paid: return new[] { "scEncRaidPaidSaid" };
                case EncCall.Refused: return new[] { "scEncRaidRefusedSaid" };
                case EncCall.MoveLost: return new[] { "scEncMoveLostSaid" };
                case EncCall.MoveNest: return new[] { "scEncMoveNestSaid" };
                case EncCall.Move: return new[] { "scEncMoveSaid" };
                case EncCall.Join: return new[] { "scEncJoinSaid" };
                case EncCall.JoinFollow: return new[] { "scEncJoinSaidFollow" };
                case EncCall.BandThanks: return new[] { "scBandThanksSaid" };
                case EncCall.BandJoin: return new[] { "scBandJoinSaid" };
            }
            return new string[0];
        }

        public static RadioPriority PriorityOf(EncCall call) { return call == EncCall.Siege || call == EncCall.SiegeHurry ? RadioPriority.Siege : RadioPriority.Enclave; }

        /// <summary>A move's moment by the words ScEncMoves shows (its SaidKey).</summary>
        public static EncCall MoveCall(string saidKey)
        {
            if (saidKey == "scEncMoveLostSaid") return EncCall.MoveLost;
            if (saidKey == "scEncMoveNestSaid") return EncCall.MoveNest;
            return EncCall.Move;
        }

        // ------------------------------------------------------------------ who speaks ----

        /// <summary>
        /// Who speaks for this group: the leader at home (LeaderOf), else the next living one in the succession's order (at home
        /// first, then out raiding; the highest class tier, then the one made first); never one enlisted with the player, never
        /// one missing. Null: nobody (or their people were never made).
        /// </summary>
        public static EncPerson SpeakerOf(Enclave e)
        {
            if (e == null) return null;
            EncPerson lead = ScEnclavePeople.LeaderOf(e);
            if (lead != null) return lead;
            EncPerson next = null;
            foreach (EncPerson q in ScRegistry.EncPeople)
            {
                if (q.EnclaveId != e.Id || !q.Alive || q.Enlisted) continue;
                if (next == null || Before(q, next)) next = q;
            }
            return next;
        }

        /// <summary>The succession's order (ScEnclavePeople.Succeed), at home before out raiding.</summary>
        private static bool Before(EncPerson a, EncPerson b)
        {
            bool ha = a.State == EncPerson.StateHome, hb = b.State == EncPerson.StateHome;
            if (ha != hb) return ha;
            int ta = ScXnpc.TierOf(a.Who.Class), tb = ScXnpc.TierOf(b.Who.Class);
            if (ta != tb) return ta > tb;
            return a.Id < b.Id;
        }

        /// <summary>The speaker for this group now (SpeakerOf as a RadioSpeaker), or null. Never throws (the m1v review's fix 6).</summary>
        public static RadioSpeaker Speaker(Enclave e)
        {
            try
            {
                return RadioSpeaker.Of(SpeakerOf(e), e);
            }
            catch (Exception ex)
            {
                ScLog.Once("enc-radio-speaker", ex);
                return null;
            }
        }

        /// <summary>
        /// The speaker for a drifter band: by when given (the m1v review's fix 4: the one the player talked to, for a join through
        /// the dialog), else its leader, else the first of the living. Null: nobody. Never throws.
        /// </summary>
        public static RadioSpeaker BandSpeaker(Band b, BandPerson by)
        {
            try
            {
                if (b == null) return null;
                BandPerson p = by ?? ScBands.LeaderOf(b);
                if (p == null)
                {
                    List<BandPerson> living = ScBands.Living(b);
                    p = living.Count > 0 ? living[0] : null;
                }
                return RadioSpeaker.Of(p, b);
            }
            catch (Exception ex)
            {
                ScLog.Once("enc-radio-band-speaker", ex);
                return null;
            }
        }

        // ------------------------------------------------------------------ saying ----

        /// <summary>The group's speaker says the moment (kind: the request's, for an offer). The caller has shown its toast.</summary>
        public static string Say(Enclave e, EncCall call, EntityPlayer player, string kind = null, bool force = false)
        {
            try
            {
                // the m1v review's fix 6: the speaker is found in here, so nothing throws into the caller mid-transaction (a demand
                // before its save, a siege call before its ledger line)
                if (e == null) return "no group";
                return Say(Speaker(e), call, player, false, kind, force, e.Name);
            }
            catch (Exception ex)
            {
                ScLog.Once("enc-radio-say-group", ex);
                return "error";
            }
        }

        /// <summary>One of an enclave's people says the moment (a fighter's hello), found in here. Never throws.</summary>
        public static string Say(EncPerson p, Enclave e, EncCall call, EntityPlayer player)
        {
            try
            {
                return Say(RadioSpeaker.Of(p, e), call, player, false, null, false, e != null ? e.Name : null);
            }
            catch (Exception ex)
            {
                ScLog.Once("enc-radio-say-person", ex);
                return "error";
            }
        }

        /// <summary>One of a drifter band says the moment (their thanks), found in here (by null: BandSpeaker's pick). Never throws.</summary>
        public static string Say(BandPerson by, Band b, EncCall call, EntityPlayer player)
        {
            try
            {
                return Say(BandSpeaker(b, by), call, player, false, null, false, b != null ? ScBands.DisplayName(b) : null);
            }
            catch (Exception ex)
            {
                ScLog.Once("enc-radio-say-band", ex);
                return "error";
            }
        }

        /// <summary>
        /// The speaker s says the moment: in person or on the radio by the rule (ScRadioSpeaker.Say). afterTalk: a line said as
        /// the talk with them closes (a join). force (the console): EnclaveVoiceLines and the hearing rule ignored. group: for
        /// the log. Returns what happened; logged verbose (said lines log a line of their own).
        /// </summary>
        public static string Say(RadioSpeaker s, EncCall call, EntityPlayer player, bool afterTalk, string kind = null, bool force = false, string group = null)
        {
            string result;
            try
            {
                if (!force && !ScConfig.EnclaveVoiceLines) { ScHandheld.Count(Tag + " off"); result = "EnclaveVoiceLines is off: the text alone"; }
                else result = ScRadioSpeaker.Say(s, KeysFor(call, kind), PriorityOf(call), player, afterTalk, force, Tag);
            }
            catch (Exception ex)
            {
                ScLog.Once("enc-radio-say", ex);
                result = "error";
            }
            ScLog.Verbose("enclave voice " + call + (group != null ? " (" + group + ")" : "") + (s != null ? " by " + s.Name : "") + ": " + result);
            return result;
        }

        // ------------------------------------------------------------------ the console ----

        private const string Usage = "[SC] sc radio enc <offer|siege|hurry|fighter|drop|fail|hello|demand|paid|refused|move|movelost|movenest|join|joinfollow|thanks|bandjoin> [enclave or band id] [force]";

        /// <summary>sc radio enc's moment names.</summary>
        public static bool TryCall(string s, out EncCall call)
        {
            call = EncCall.Offer;
            switch ((s ?? "").ToLowerInvariant())
            {
                case "offer": case "request": call = EncCall.Offer; return true;
                case "siege": call = EncCall.Siege; return true;
                case "hurry": case "siegehurry": call = EncCall.SiegeHurry; return true;     // the m1v review's fix 8
                case "fighter": call = EncCall.Fighter; return true;
                case "drop": call = EncCall.Drop; return true;
                case "fail": case "failed": call = EncCall.Fail; return true;
                case "hello": case "greet": call = EncCall.Hello; return true;
                case "demand": call = EncCall.Demand; return true;
                case "paid": case "pay": call = EncCall.Paid; return true;
                case "refused": case "refuse": call = EncCall.Refused; return true;
                case "move": call = EncCall.Move; return true;
                case "movelost": case "lost": call = EncCall.MoveLost; return true;
                case "movenest": case "nest": call = EncCall.MoveNest; return true;
                case "join": call = EncCall.Join; return true;
                case "joinfollow": case "follow": call = EncCall.JoinFollow; return true;
                case "thanks": case "bandthanks": call = EncCall.BandThanks; return true;
                case "bandjoin": call = EncCall.BandJoin; return true;
            }
            return false;
        }

        private static bool IsBand(EncCall c) { return c == EncCall.BandThanks || c == EncCall.BandJoin; }
        private static bool IsRaid(EncCall c) { return c == EncCall.Demand || c == EncCall.Paid || c == EncCall.Refused; }

        /// <summary>
        /// sc radio enc &lt;moment&gt; [id] [force]: a sample of the moment through the whole path with the rule - the toast the
        /// player would get (its words, with the group's name; a moment whose toast carries numbers shows only the said part),
        /// then the voice as the rule decides, and why when it stays silent. Nothing else happens: no request, no siege, no
        /// move. id: the enclave (or, for thanks and bandjoin, the band); default the nearest one with somebody to speak - a
        /// hostile group for the raid moments, any other for the rest. hello: the group's enlisted person if one is with you,
        /// else its speaker. force: EnclaveVoiceLines and the hearing rule ignored (in person still in person).
        /// </summary>
        public static void Command(List<string> p, Action<string> output)
        {
            EncCall call;
            if (p.Count < 3 || !TryCall(p[2], out call)) { output(Usage); return; }
            if (!ScRegistry.Loaded) { output("[SC] the registry is not loaded"); return; }
            bool force = false;
            int id = -1;
            for (int i = 3; i < p.Count; i++)
            {
                int n;
                if (p[i].Equals("force", StringComparison.OrdinalIgnoreCase)) force = true;
                else if (int.TryParse(p[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) id = n;
                else { output("[SC] '" + p[i] + "' is neither an id nor force"); output(Usage); return; }
            }
            EntityPlayerLocal pl = ScCommunity.LocalPlayer;
            if (pl == null) { output("[SC] no local player"); return; }
            RadioSpeaker s;
            string group, toast, key0 = KeysFor(call, null)[0];
            if (IsBand(call))
            {
                Band b = id >= 0 ? ScBands.ById(id) : NearestBand(pl);
                if (b == null) { output("[SC] " + (id >= 0 ? "no band " + id : "no band with anyone alive (sc band list; sc band spawn)")); return; }
                s = BandSpeaker(b, null);                       // a join started any other way than the dialog: the leader
                group = ScBands.DisplayName(b) + " (band " + b.Id + ")";
                toast = string.Format(Localization.Get("scEncCallSays"), s != null ? s.Name : ScBands.DisplayName(b), Localization.Get(key0));
            }
            else
            {
                Enclave e = id >= 0 ? ScEnclaves.ById(id) : NearestEnclave(pl, IsRaid(call));
                if (e == null) { output("[SC] " + (id >= 0 ? "no enclave " + id : "no " + (IsRaid(call) ? "hostile " : "") + "enclave with somebody to speak (sc enclave list)")); return; }
                group = e.Name + " (enclave " + e.Id + ")";
                EncPerson by = call == EncCall.Hello ? (ScEnclaveDeals.EnlistedOf(e) ?? SpeakerOf(e)) : SpeakerOf(e);
                s = RadioSpeaker.Of(by, e);
                string name = by != null && call == EncCall.Hello ? by.Name : e.Name;
                toast = call == EncCall.Siege ? string.Format(Localization.Get("scEncSiegeCall"), e.Name)
                      : call == EncCall.Offer ? string.Format(Localization.Get("scEncReqHeard"), e.Name)
                      : string.Format(Localization.Get(call == EncCall.Hello || call == EncCall.Join || call == EncCall.JoinFollow ? "scEncCallSays" : "scEncCallRadio"),
                                      call == EncCall.Join || call == EncCall.JoinFollow ? (by != null ? by.Name : e.Name) : name, Localization.Get(key0));
            }
            ScCommunity.Tell(pl, toast, call == EncCall.Siege || call == EncCall.SiegeHurry || call == EncCall.Demand || call == EncCall.Fail ? "ui_denied" : "");
            string speaker = s == null ? "nobody" : s.Name + " (" + s.Role + (s.Body() != null ? ", body in the world " + Far(s.Body(), pl) + " away" : ", no body in the world") + ")";
            output("[SC] " + call + " from " + group + ", speaker " + speaker + (force ? ", forced" : "") + ": " + Say(s, call, pl, false, null, force, group));
        }

        private static string Far(EntityAlive body, EntityPlayer pl)
        {
            return body == null || pl == null ? "?" : Vector3.Distance(body.position, pl.position).ToString("0", CultureInfo.InvariantCulture) + " m";   // fix 1: 3D
        }

        /// <summary>The nearest group with somebody to speak: a hostile one (raid moments) or one that is not.</summary>
        private static Enclave NearestEnclave(EntityPlayer pl, bool hostile)
        {
            Enclave best = null;
            float bd = float.MaxValue;
            foreach (Enclave e in ScEnclaves.All)
            {
                if (e.Lost || e.IsHostile != hostile || SpeakerOf(e) == null) continue;
                float d = e.DistanceXZ(pl.position.x, pl.position.z);
                if (d < bd) { bd = d; best = e; }
            }
            return best;
        }

        private static Band NearestBand(EntityPlayer pl)
        {
            Band best = null;
            float bd = float.MaxValue;
            foreach (Band b in ScRegistry.Bands)
            {
                if (b.IsRaiders || ScBands.Living(b).Count == 0) continue;
                float d = ScRunners.XZ(ScBands.Where(b), pl.position);
                if (d < bd) { bd = d; best = b; }
            }
            return best;
        }

        /// <summary>sc radio: the switch, whether a transmission is heard now and how, and the nearest group's speaker and voice.</summary>
        public static string Describe(EntityPlayer pl)
        {
            var sb = new StringBuilder("enclave voice: EnclaveVoiceLines=").Append(ScConfig.EnclaveVoiceLines).Append(" VoiceLines=").Append(ScConfig.VoiceLines);
            string how;
            bool heard = ScRadioSpeaker.Hears(pl, out how);
            sb.Append("; on the radio now: ").Append(heard ? "heard (" + how + ")" : "the toast alone (" + how + ")");
            if (pl != null && ScRegistry.Loaded)
            {
                Enclave e = NearestEnclave(pl, false) ?? NearestEnclave(pl, true);
                EncPerson p = SpeakerOf(e);
                if (p != null)
                {
                    string voice;
                    string clip = ScVoice.MemberClip(p, "scEncCallFighterSaid", out voice);
                    sb.Append("; nearest group ").Append(e.Name).Append(" speaks through ").Append(p.Name).Append(p.IsLeader ? " (leader)" : " (next in line)")
                      .Append(clip != null ? ", voice " + voice : ", no recorded voice");
                }
                else sb.Append("; no group with somebody to speak");
            }
            return sb.ToString();
        }
    }
}
