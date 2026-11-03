using System;
using System.Text;

namespace SurvivorCommunity
{
    /// <summary>
    /// E8, Voice Control's chat knows the enclave (research\ideas\DESIGN.md section 11; the user's decisions: single player,
    /// the host; a hostile enclave's people refuse the spoken trade and the prompt makes them refuse to talk, chat is never
    /// blocked). Patches_VoiceControl's prefix on LLMService.SendStreamingChatRequest appends For(npc) to the system prompt
    /// of every LLM chat request with one of an enclave's people: push-to-talk, typed @ chat and Voice Control's console
    /// chat all end there (NPCChatComponent.ProcessPlayerMessage, after its phrase triggers, then its coroutine's one call).
    /// Built per message from the records, so it is never stale; English (Voice Control's spoken_text always is).
    ///
    /// What it says - only what the NPC knows, kept short (it is sent with every request):
    ///  - who they are: the leader of their group or one of it (and who leads it), how many are left, where they live,
    ///    what they live on; a leader is told to speak as one (Voice Control may have dealt them any survivor personality,
    ///    "teenager" or "nervous" included: DESIGN.md section 15, side finding 2);
    ///  - the group's standing with the player as a word (never the points), and an ally's benefit;
    ///  - the group's open request as the group knows it: an offer by what it wants (an infestation's building and who would
    ///    go missing are told only in the talk, as SC's lines do), a request the player took on by what and by when, a siege;
    ///    never a missing survivor's fate;
    ///  - one enlisted with the player: about how long until they go home; a missing survivor's own body: that they are lost
    ///    and waiting, or that the player was sent and is taking them home;
    ///  - that deals are made only in the talk menu: no trading, giving, requests or leaving their group by chat.
    /// A hostile group's people (a raider enclave, or one turned hostile) get two sentences that make them refuse to talk.
    /// Host only, main thread (Voice Control calls the target from its chat coroutine).
    /// </summary>
    public static class ScVoiceContext
    {
        public const string Header = "[YOUR GROUP]";

        /// <summary>The paragraph for this NPC, or null when they are not one of an enclave's people (then nothing is added).
        /// what: a short note for the log ("leader of 'X', friendly, request offered").</summary>
        public static string For(EntityAlive npc, out string what)
        {
            what = null;
            if (npc == null || !ScCommunity.IsHost || !ScRegistry.Loaded || !ScConfig.Enclaves || !ScEnclaves.Ready) return null;
            EncPerson p = ScEnclavePeople.PersonOf(npc);
            Enclave e = ScEnclavePeople.EnclaveOf(p);
            if (p == null || e == null || e.Lost || (!p.Alive && !p.Missing)) return null;
            string standing = ScEnclaves.StandingKey(e);
            string role = p.IsLeader ? "leader" : "member";
            if (e.IsHostile)
            {
                what = role + " of '" + e.Name + "', hostile";
                return Header + " You belong to a group that is hostile to this survivor. Refuse to talk with them: answer only with a"
                       + " short warning to leave, and never help them, trade with them or tell them anything about your group.";
            }

            var sb = new StringBuilder(Header);
            int living = ScEnclavePeople.Living(e).Count;
            string poi = string.IsNullOrEmpty(e.PoiName) || e.PoiName == "?" ? "" : " living at " + e.PoiName;
            string people = living > 0 ? ", " + living + (living == 1 ? " survivor" : " survivors") + poi : poi.Length > 0 ? "," + poi : "";
            if (p.IsLeader && !p.Missing)
            {
                sb.Append(" You are the leader of ").Append(e.Name).Append(people).Append('.');
            }
            else
            {
                EncPerson leader = ScEnclavePeople.LeaderOf(e);
                sb.Append(" You are one of ").Append(e.Name).Append(people);
                if (leader != null && leader != p) sb.Append(", led by ").Append(leader.Name);
                sb.Append('.');
            }
            string kind = KindLine(e.Kind);
            if (kind.Length > 0) sb.Append(' ').Append(kind);
            if (p.IsLeader && !p.Missing) sb.Append(" Speak with a leader's authority.");
            sb.Append(" Your group's standing with this survivor: ").Append(StandingLine(e, standing));

            string state;
            if (p.Missing) state = MissingLine(p);
            else
            {
                state = RequestLine(e);
                if (p.Enlisted) state = Join(state, EnlistedLine(p));
            }
            if (!string.IsNullOrEmpty(state)) sb.Append(' ').Append(state);

            sb.Append(" You may talk about all this, but in this conversation you cannot trade, give or take anything, accept or settle"
                      + " requests, or leave your group: for that they must talk with you in person (the talk menu). Never claim you did.");
            what = role + " of '" + e.Name + "', " + standing + (p.Enlisted ? ", enlisted" : "") + (p.Missing ? ", missing" : "")
                   + ", request " + RequestState(e);
            return sb.ToString();
        }

        private static string Join(string a, string b)
        {
            if (string.IsNullOrEmpty(a)) return b;
            return string.IsNullOrEmpty(b) ? a : a + " " + b;
        }

        /// <summary>What a kind of group lives on (ScEnclaves.Kinds), as one sentence.</summary>
        private static string KindLine(string kind)
        {
            switch (kind)
            {
                case "growers": return "You grow food.";
                case "medics": return "You are medics and make medicine.";
                case "militia": return "You are a militia and make ammunition.";
                case "builders": return "You are builders and gather building materials.";
                case "roadcrew": return "You are a road crew and gather fuel.";
                case "general": return "You live by scavenging.";
                default: return "";
            }
        }

        /// <summary>The standing as a word and what it means to them; an ally's benefit by name.</summary>
        private static string StandingLine(Enclave e, string key)
        {
            switch (key)
            {
                case "allied":
                    string benefit = ScEncAllies.Name(e.Benefit);
                    if (string.IsNullOrEmpty(benefit)) return "Allied - you trust them.";
                    string desc = ScEncAllies.Describe(e.Benefit);
                    return "Allied - you trust them, and your group gives them its " + benefit + (string.IsNullOrEmpty(desc) ? "" : " (" + desc + ")") + ".";
                case "friendly": return "Friendly - you like them.";
                default: return "Neutral - you don't know them well and stay careful.";
            }
        }

        private static string RequestState(Enclave e)
        {
            EnclaveRequest r = ScEncRequests.Of(e);
            return r == null ? "none" : r.Kind + " " + r.State;
        }

        /// <summary>The group's open request as they know it, or "" (none).</summary>
        private static string RequestLine(Enclave e)
        {
            EnclaveRequest r = ScEncRequests.Of(e);
            if (r == null) return "";
            string today = " (today is day " + ScEncRequests.Now + ")";
            switch (r.Kind)
            {
                case EnclaveRequest.KindSiege:
                    return r.Stage == ScEncSiege.StageLive
                        ? "The dead are attacking your home right now, and your group called this survivor for help."
                        : "The dead are coming for your home soon, and your group called this survivor for help.";
                case EnclaveRequest.KindMissing:
                    if (r.IsOffered) return "One of your people went out and has not come back; your group means to ask this survivor to look for them.";
                    return ScEncSearch.PersonName(r) + ", one of yours, went missing"
                           + (string.IsNullOrEmpty(r.TargetName) ? "" : " on the way to " + r.TargetName)
                           + "; this survivor agreed to look for them by the end of day " + r.DueDay + today + ".";
                case EnclaveRequest.KindInfestation:
                    if (r.IsOffered) return "The dead have taken a building near you; your group means to ask this survivor to clear it.";
                    return "This survivor agreed to clear the infestation " + (string.IsNullOrEmpty(r.TargetName) ? "near you" : "at " + r.TargetName)
                           + " by the end of day " + r.DueDay + today + ".";
                default:
                    string what = ScEncRequests.WhatText(r);
                    if (r.IsOffered) return "Your group needs " + what + " and means to ask this survivor for it.";
                    return "This survivor agreed to bring your group " + what + " by the end of day " + r.DueDay + today + ".";
            }
        }

        /// <summary>One enlisted with the player: about how long until they go home.</summary>
        private static string EnlistedLine(EncPerson p)
        {
            World w = ScCommunity.World;
            if (w == null) return "";
            ulong now = w.worldTime;
            ulong left = p.EnlistedUntil > now ? p.EnlistedUntil - now : 0UL;
            int hours = (int)((left + 999UL) / 1000UL);
            string howLong = hours >= 48 ? "about " + ((hours + 12) / 24) + " more days" : hours > 1 ? "about " + hours + " more hours" : "a little longer";
            return "You are travelling with this survivor for " + howLong + ", then you go home to your group.";
        }

        /// <summary>A missing survivor's own body: lost and waiting, or being taken home by the one their group sent.</summary>
        private static string MissingLine(EncPerson p)
        {
            EnclaveRequest r = ScEncSearch.RequestOf(p);
            if (r != null && r.Stage == ScEncSearch.StageFollowing) return "Your group sent this survivor to find you, and you are going home with them.";
            return "You got lost away from your group and are waiting here for help.";
        }

        // ------------------------------------------------------------------ the spoken trade ----

        /// <summary>
        /// Why this NPC refuses Voice Control's spoken trade (null: they don't): the side door the talk prompt's refusal (X9,
        /// Patches_Xnpc.Patch_NoTalkToRaiders) does not cover. A body of an enclave turned hostile (the synced cvar), one of a
        /// hostile enclave's people (a raider enclave), any raider, a survivor with a grudge against this player (ScTrust) -
        /// the same NPCs whose talk prompt is refused. (Raiders' classes carry Voice Control's no_voice tag, so they are no chat
        /// target today; they are refused all the same in case that tag goes.) Main thread.
        /// </summary>
        public static string TradeRefusal(EntityAlive npc, EntityPlayer player)
        {
            if (npc == null) return null;
            if (ScEnclavePeople.IsHostileBody(npc)) return "their enclave turned hostile to you";
            Enclave e = ScEnclavePeople.EnclaveOf(ScEnclavePeople.PersonOf(npc));
            if (e != null && e.IsHostile) return e.Hostile ? "one of a raider enclave" : "their enclave is hostile to you";
            if (ScXnpc.IsRaider(npc)) return "a raider";
            if (ScTrust.IsHostileTo(npc, player)) return "they hold a grudge against you";
            return null;
        }
    }
}
