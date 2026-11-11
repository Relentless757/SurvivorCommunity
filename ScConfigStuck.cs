using System;
using System.Globalization;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round 29b (M29): the config keys of the residents' stuck rescue (ScStuck) - the roam's no-progress rule and its
    /// unreachable spots (UAI.UAITaskWanderHomeSC), the watchdog's no-gain signal and the wait while the player watches
    /// (ScCommunity.Watchdog). Kept apart from ScConfig.cs's long lists, as ScConfigVoices. Config/blocks.xml, block
    /// ConfigSurvivorCommunity, after StuckSeconds; each key is commented there. ResetStuck also clears ScStuck's per-world
    /// state (ScConfig.Reset runs at GameStarting). Both games.
    /// </summary>
    public static partial class ScConfig
    {
        /// <summary>A roam walk that gets no 1 m closer to its spot in this many seconds ends, and the spot is remembered as unreachable (0: off).</summary>
        public static float RoamNoProgressSeconds = 8f;
        /// <summary>How long a body is not sent to a spot it could not reach, nor to one beside it (minutes; 0: not remembered).</summary>
        public static float RoamUnreachableMinutes = 5f;
        /// <summary>The watchdog also counts a body that wants to move but gains no meter toward its path's end in 2 x StuckSeconds of trying.</summary>
        public static bool StuckNoGain = true;
        /// <summary>A watchdog move back waits while the player looks toward the body or its post, at most this many seconds (0: at once, in sight, as before).</summary>
        public static float StuckSeenWaitSeconds = 60f;

        private static void ResetStuck()
        {
            RoamNoProgressSeconds = 8f;
            RoamUnreachableMinutes = 5f;
            StuckNoGain = true;
            StuckSeenWaitSeconds = 60f;
            // the per-world state of the feature: a new world starts with none of it
            ScStuck.Reset();
        }

        private static void ParseStuck(DynamicProperties p)
        {
            ParseFloat(p, "RoamNoProgressSeconds", ref RoamNoProgressSeconds, 0f, 120f);
            ParseFloat(p, "RoamUnreachableMinutes", ref RoamUnreachableMinutes, 0f, 60f);
            ParseBoolKey(p, "StuckNoGain", ref StuckNoGain);
            ParseFloat(p, "StuckSeenWaitSeconds", ref StuckSeenWaitSeconds, 0f, 600f);
        }

        /// <summary>The config line's stuck rescue part.</summary>
        private static string StuckLine()
        {
            Func<float, string> f = x => x.ToString("0.##", CultureInfo.InvariantCulture);
            return "roamNoProgress=" + (RoamNoProgressSeconds > 0f ? f(RoamNoProgressSeconds) + " s, unreachable " + f(RoamUnreachableMinutes) + " min" : "off")
                   + " noGain=" + (StuckNoGain ? (2 * StuckSeconds) + " s" : "off")
                   + " seenWait=" + (StuckSeenWaitSeconds > 0f ? f(StuckSeenWaitSeconds) + " s" : "off (in sight at once)");
        }
    }
}
