using System.Globalization;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round m1w (2026-11-17, research/voices_v9/VOICE_INVENTORY.md section 3.1): the switches of Voice Control's speech routed
    /// into SC's own recorded voices (ScVoiceRoutes, ScBarks, ScGreetings). Kept apart from ScConfigVoices.cs, which calls
    /// ResetVoiceRoutes, ParseVoiceRoutes and VoiceRoutesLine. Config/blocks.xml, block ConfigSurvivorCommunity, after
    /// VoiceClipIdleMinutes; each key is commented there. The say-line ids (P1), SC's own lines indexed (P3) and the
    /// punctuation-blind matching (P5) have no switch: they only find more of the lines SC already plays.
    /// </summary>
    public static partial class ScConfig
    {
        /// <summary>
        /// P2: the order wheel's spoken slot labels ("Close", "Jump down", "Loot here", "Wave") become the order's own reply in
        /// SC's recorded voice ("I'll stay close."), "Come here" leaves the "On my way." it already said, and a gesture with no
        /// reply ("Whatever") plays without a word. false: Voice Control speaks the labels, as before.
        /// </summary>
        public static bool VoiceWheelAcks = true;
        /// <summary>
        /// P6: one line per NPC at a time - after SC put one of its lines on an NPC in Voice Control's place, the second line
        /// of the same order (the wheel's label, a spoken order's reply: the same or the next frame), which cut SC's clip, is
        /// swallowed if it comes within this many real seconds. Never a say-line (going down) or a trigger's reply. 0: off.
        /// </summary>
        public static float VoiceOneLineSeconds = 0.4f;
        /// <summary>
        /// P7: SC's refusals that were text only - a field job for a resident, the work kit as a weapon, no room in the party
        /// for "Come with me." - are said aloud in her recorded voice as well (once recorded), within 30 m of you; not while
        /// she is asleep or down, Voice Control is speaking on her, or ScBarks is off.
        /// </summary>
        public static bool VoiceSpokenRefusals = true;
        /// <summary>
        /// P4: SC owns the greetings of SC's people: a runner on the road, one posted to an outpost and one arriving greet
        /// with the member lines that fit away from home (none of the home-only ones; nothing when none is left), and an
        /// outpost stand-in or someone hostile to you says nothing - Voice Control greets none of them. Where SC has a
        /// greeting but no recording of it, Voice Control greets as before. false: Voice Control greets the ones SC has no
        /// greeting for, as before.
        /// </summary>
        public static bool ScGreetingsOwnAll = true;

        private static void ResetVoiceRoutes()
        {
            VoiceWheelAcks = true;
            VoiceOneLineSeconds = 0.4f;
            VoiceSpokenRefusals = true;
            ScGreetingsOwnAll = true;
        }

        private static void ParseVoiceRoutes(DynamicProperties p)
        {
            ParseBoolKey(p, "VoiceWheelAcks", ref VoiceWheelAcks);
            ParseFloat(p, "VoiceOneLineSeconds", ref VoiceOneLineSeconds, 0f, 2f);
            ParseBoolKey(p, "VoiceSpokenRefusals", ref VoiceSpokenRefusals);
            ParseBoolKey(p, "ScGreetingsOwnAll", ref ScGreetingsOwnAll);
        }

        /// <summary>The voice routes on the config's voices line (round m1w).</summary>
        private static string VoiceRoutesLine()
        {
            return "wheelAcks=" + VoiceWheelAcks + " oneLine=" + VoiceOneLineSeconds.ToString("0.##", CultureInfo.InvariantCulture) + "s"
                   + " spokenRefusals=" + VoiceSpokenRefusals + " greetingsOwnAll=" + ScGreetingsOwnAll;
        }
    }
}
