using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round U6 (2026-11-16, decision D12; research/npc306/FEATURES_306.md section 5): SC's own Follow - the talk row "Come with
    /// me.", the spoken "follow me" and the community window's Follow me, which all run ScCommunity.Follow - honors 0-XNPCCore
    /// 3.4.06's party cap (Settings.xml PartyMaxMembers, 8). Its party is every NPC whose Leader (or Owner) cvar names you and
    /// whose order is Follow or follow-and-loot: paid hires and SC's followers share it (SdcsParty.Members). Before this, SC's
    /// Follow never asked, so SC's followers plus paid hires could pass 8.
    ///
    /// SC's own MaxFollowers (2 by default) is checked first and stays SC's own, lower cap. A member already in the party (one who
    /// follows you) is never refused: ScCommunity.Follow returns before asking, and SdcsParty.WouldOverflow passes a candidate who
    /// is a party member. The refusal is 0-XNPCCore's own text, "Your party is full (8/8).", shown where SC shows its other
    /// refusals (the talk, the window's message line, the voice's tooltip).
    ///
    /// SdcsParty is new in 0-XNPCCore 3.4.06 (NPCSdcsCharacters): every use sits behind Available and in a method the JIT compiles
    /// only when it is there. SharePartyCap false: SC's Follow ignores the party cap, as before.
    /// </summary>
    public static class ScPartyCap
    {
        private static bool? available;
        private static int refusals;
        /// <summary>
        /// The Fable review (finding 9, traced): a spoken "follow me" runs ScCommunity.Follow twice in one call of Voice Control's
        /// ProcessPlayerMessage - Patches_VoiceControl.Patch_VoiceFollowEarly's prefix first (a refusal there is left unanswered),
        /// then Patch_VoiceFollowMember's prefix on ExecuteMatch, which answers it. The same body refused again within
        /// RepeatSeconds is the same refusal: not logged or counted twice.
        /// </summary>
        private static int lastRefusedId = -1;
        private static float lastRefusedAt;
        private const float RepeatSeconds = 2f;
        /// <summary>Round m1w (P7): the text of the last party-cap refusal (Full), so a caller can tell it from SC's other Follow refusals.</summary>
        internal static string LastRefusal;

        public static void Reset()
        {
            refusals = 0;
            lastRefusedId = -1;
            lastRefusedAt = 0f;
            LastRefusal = null;
        }

        /// <summary>0-XNPCCore 3.4.06's SdcsParty is loaded.</summary>
        public static bool Available
        {
            get
            {
                if (available == null) available = ScTypes.Find("SdcsParty") != null;
                return available.Value;
            }
        }

        /// <summary>
        /// ScCommunity.Follow, after SC's own MaxFollowers: true when 0-XNPCCore's party is full for this member. error is then the
        /// text to show - 0-XNPCCore's own, with the counts - which the callers pass through Localization.Get (an unknown key comes
        /// back unchanged). A read.
        /// </summary>
        public static bool Refuses(Member m, EntityAlive npc, EntityPlayer player, out string error)     // round MP2: the follower's own player
        {
            error = null;
            if (!ScConfig.SharePartyCap || npc == null || player == null || !Available) return false;
            try
            {
                if (!Full(npc, player, out error)) return false;
                float now = Time.unscaledTime;
                bool repeat = npc.entityId == lastRefusedId && now - lastRefusedAt < RepeatSeconds;
                lastRefusedId = npc.entityId;
                lastRefusedAt = now;
                if (repeat) return true;                // the voice path's second Follow of the same order (finding 9)
                refusals++;
                ScLog.Out((m != null ? m.Name : ScCommunity.DisplayName(npc)) + " was not made a follower: " + error + " (0-XNPCCore's party cap, shared with paid hires; round U6)");
                return true;
            }
            catch (Exception e)
            {
                ScLog.Once("party-cap", e);
                error = null;
                return false;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool Full(EntityAlive npc, EntityPlayer player, out string error)
        {
            error = null;
            if (!SdcsParty.WouldOverflow(player, npc)) return false;
            string text = Localization.Get("vcPartyFull");
            if (string.IsNullOrEmpty(text) || text == "vcPartyFull") text = "Your party is full ({0}/{1}).";
            error = string.Format(text, SdcsParty.Members(player), SdcsParty.Cap);
            LastRefusal = error;                    // round m1w (P7)
            return true;
        }

        /// <summary>sc party: 0-XNPCCore's party for you against its cap, SC's followers against MaxFollowers. A read.</summary>
        public static string Line(EntityPlayer player)
        {
            string sc = ScFollowers.Line(player);     // round MP7 (part F): your own followers and enlisted people (per player)
            if (!Available) return sc + "; 0-XNPCCore's party cap: not in this 0-XNPCCore (older than 3.4.06)";
            try { return sc + "; " + PartyText(player) + "; SharePartyCap " + ScConfig.SharePartyCap + ", refusals this session " + refusals.ToString(CultureInfo.InvariantCulture); }
            catch (Exception e) { return sc + "; 0-XNPCCore's party: " + e.Message; }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string PartyText(EntityPlayer player)
        {
            return "0-XNPCCore's party " + SdcsParty.Members(player) + " of PartyMaxMembers " + SdcsParty.Cap + " (your NPCs following you, paid hires and SC's followers alike)";
        }
    }
}
