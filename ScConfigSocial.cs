#if SC_V34
using System;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round U3 (2026-11-16, the user's decision D11 "SC layer, SC voices"): the config keys of the community's social life
    /// (ScSocial) - small talk between residents, walking over to chat, the evening campfire - and of the one-greeting-per-group
    /// rule for strangers (ScGreetings). Kept apart from ScConfig.cs's long lists, as ScConfigTeam. Config/blocks.xml, block
    /// ConfigSurvivorCommunity, after ScChatterRange; each key is commented there. ResetSocial runs at GameStarting with the
    /// rest of ScConfig.Reset.
    /// </summary>
    public static partial class ScConfig
    {
        /// <summary>Two free residents near each other now and then exchange a line and an answer (and an enclave's people in their building).</summary>
        public static bool ResidentSmallTalk = true;
        /// <summary>At most one exchange this often (real seconds, a quarter either way), anywhere: one speaker pair at a time.</summary>
        public static float SmallTalkGapSeconds = 90f;
        /// <summary>The same two people talk at most once in this many real minutes.</summary>
        public static float SmallTalkPairMinutes = 10f;
        /// <summary>Two people talk when they stand this close (meters), as 0-XNPCCore's own residents (ChatMetres 4).</summary>
        public static float SmallTalkRange = 4f;
        /// <summary>
        /// Voice Control's player-distance rule: an exchange starts only while the nearest player is farther than SmallTalkHearMin
        /// (its ProactiveGreetRange, 10 m) and within SmallTalkHearMax (32 m, its earshot) of one of the two; a player who comes
        /// closer than SmallTalkHearMin ends it (the answer is dropped).
        /// </summary>
        public static float SmallTalkHearMin = 10f;
        public static float SmallTalkHearMax = 32f;
        /// <summary>
        /// A walk over to chat: when no two free people stand close, every SmallTalkMeetSeconds (20 s) this is the chance that a
        /// resident pausing in her day roam walks over to a free neighbor within SmallTalkMeetRange meters to talk to her.
        /// </summary>
        public static float SmallTalkMeetChance = 0.3f;
        public static float SmallTalkMeetRange = 25f;
        /// <summary>Voice Control's own 220 pairs, spoken by its text-to-speech, for an opener who has no recording of SC's line (off: captions).</summary>
        public static bool SmallTalkTtsFallback = false;
        /// <summary>An enclave's people do the same in their own building (small talk and the campfire).</summary>
        public static bool SmallTalkEnclaves = true;

        /// <summary>In the evening (and during a round of drinks) free residents gather at a campfire inside the base for a while.</summary>
        public static bool CampfireGathering = true;
        /// <summary>How long each one stays at the fire once there (real seconds, a random time between the two).</summary>
        public static float CampfireMinSeconds = 30f;
        public static float CampfireMaxSeconds = 60f;
        /// <summary>At most this many at one fire at a time (a ring of 8 spots 2 m out; more stand farther out).</summary>
        public static int CampfireMaxPeople = 8;
        /// <summary>Between two exchanges at the fire (real seconds, a quarter either way): shorter than SmallTalkGapSeconds, so a gathering talks.</summary>
        public static float CampfireTalkGapSeconds = 20f;
        /// <summary>The blocks that count as a campfire: exact names, or a prefix ending in * (0-XNPCCore's Routine/GatherBlocks form).</summary>
        public static string CampfireBlocks = "campfire";
        /// <summary>The fire shows lit while they sit at it (flames only, through 0-XNPCCore's work-flame cvars: no fuel, no block change).</summary>
        public static bool CampfireLit = true;

        /// <summary>Strangers (wild survivors) greet a group once: one greets, the rest within ScGreetGroupRange count as met (Voice Control's rule).</summary>
        public static bool ScGreetOnePerGroup = true;
        public static float ScGreetGroupRange = 30f;

        private static void ResetSocial()
        {
            ResidentSmallTalk = true;
            SmallTalkGapSeconds = 90f;
            SmallTalkPairMinutes = 10f;
            SmallTalkRange = 4f;
            SmallTalkHearMin = 10f;
            SmallTalkHearMax = 32f;
            SmallTalkMeetChance = 0.3f;
            SmallTalkMeetRange = 25f;
            SmallTalkTtsFallback = false;
            SmallTalkEnclaves = true;
            CampfireGathering = true;
            CampfireMinSeconds = 30f;
            CampfireMaxSeconds = 60f;
            CampfireMaxPeople = 8;
            CampfireTalkGapSeconds = 20f;
            CampfireBlocks = "campfire";
            CampfireLit = true;
            ScGreetOnePerGroup = true;
            ScGreetGroupRange = 30f;
            ScSocial.ResetConfig();
        }

        private static void ParseSocial(DynamicProperties p)
        {
            ParseBoolKey(p, "ResidentSmallTalk", ref ResidentSmallTalk);
            ParseFloat(p, "SmallTalkGapSeconds", ref SmallTalkGapSeconds, 10f, 3600f);
            ParseFloat(p, "SmallTalkPairMinutes", ref SmallTalkPairMinutes, 0f, 1440f);
            ParseFloat(p, "SmallTalkRange", ref SmallTalkRange, 1f, 10f);
            ParseFloat(p, "SmallTalkHearMin", ref SmallTalkHearMin, 0f, 50f);
            ParseFloat(p, "SmallTalkHearMax", ref SmallTalkHearMax, 5f, 100f);
            if (SmallTalkHearMax < SmallTalkHearMin + 1f) SmallTalkHearMax = SmallTalkHearMin + 1f;
            ParseFloat(p, "SmallTalkMeetChance", ref SmallTalkMeetChance, 0f, 1f);
            ParseFloat(p, "SmallTalkMeetRange", ref SmallTalkMeetRange, 5f, 80f);
            ParseBoolKey(p, "SmallTalkTtsFallback", ref SmallTalkTtsFallback);
            ParseBoolKey(p, "SmallTalkEnclaves", ref SmallTalkEnclaves);
            ParseBoolKey(p, "CampfireGathering", ref CampfireGathering);
            ParseFloat(p, "CampfireMinSeconds", ref CampfireMinSeconds, 5f, 600f);
            ParseFloat(p, "CampfireMaxSeconds", ref CampfireMaxSeconds, 5f, 600f);
            if (CampfireMaxSeconds < CampfireMinSeconds) CampfireMaxSeconds = CampfireMinSeconds;
            ParseInt(p, "CampfireMaxPeople", ref CampfireMaxPeople, 1, 16);
            ParseFloat(p, "CampfireTalkGapSeconds", ref CampfireTalkGapSeconds, 5f, 600f);
            string blocks;
            if (p.Values.TryGetValue("CampfireBlocks", out blocks) && blocks != null) CampfireBlocks = blocks.Trim();
            ParseBoolKey(p, "CampfireLit", ref CampfireLit);
            ParseBoolKey(p, "ScGreetOnePerGroup", ref ScGreetOnePerGroup);
            ParseFloat(p, "ScGreetGroupRange", ref ScGreetGroupRange, 3f, 100f);
            ScSocial.ResetConfig();
        }

        /// <summary>For sc chat: the social keys in one line.</summary>
        public static string SocialLine()
        {
            return "ResidentSmallTalk=" + ResidentSmallTalk + " gap " + SmallTalkGapSeconds + " s, pair " + SmallTalkPairMinutes + " min, range "
                   + SmallTalkRange + " m, hear " + SmallTalkHearMin + "-" + SmallTalkHearMax + " m, meet " + SmallTalkMeetChance + " within "
                   + SmallTalkMeetRange + " m, TTS fallback " + SmallTalkTtsFallback + ", enclaves " + SmallTalkEnclaves
                   + "; CampfireGathering=" + CampfireGathering + " " + CampfireMinSeconds + "-" + CampfireMaxSeconds + " s, max "
                   + CampfireMaxPeople + ", talk gap " + CampfireTalkGapSeconds + " s, blocks '" + CampfireBlocks + "', lit " + CampfireLit
                   + "; ScGreetOnePerGroup=" + ScGreetOnePerGroup + " within " + ScGreetGroupRange + " m";
        }
    }
}
#endif
