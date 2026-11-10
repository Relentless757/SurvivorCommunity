using System;
using System.Globalization;

namespace SurvivorCommunity
{
    /// <summary>
    /// R2 (round 27, 3.4 build only): the config keys of the handheld radio - the Radio tab (XUiC_ScRadioTab), runners who
    /// carry a radio talking to you on it (ScRadioTalk), the distress rule (ScHandheld.DistressAllowed) and the raiders' drop
    /// (ScHandheld.OnRaiderDied). Kept apart from ScConfig.cs's long lists, as ScConfigVoices and ScConfigCraft. Config/blocks.xml,
    /// block ConfigSurvivorCommunity, after RescueBonus (the radio station's keys); each key is commented there. ResetRadio also
    /// clears ScHandheld's and ScRadioTalk's per-world state (ScConfig.Reset runs at GameStarting).
    /// </summary>
    public static partial class ScConfig
    {
        /// <summary>The Radio tab left of the Community tab while the player carries a handheld radio (false: no tab; the station opens only at its block).</summary>
        public static bool HandheldRadioTab = true;
        /// <summary>A runner who carries a handheld radio talks to you on it during a supply run (heard while you carry one too).</summary>
        public static bool RunnerRadioTalk = true;
        /// <summary>The runner's voice on the radio: its volume (0-1).</summary>
        public static float RunnerRadioVolume = 0.8f;
        /// <summary>The radio sound: band-limited, a little grit, a squelch in and out (false: the plain recording, flat, wherever you are).</summary>
        public static bool RunnerRadioEffect = true;
        /// <summary>A runner radios for help only while carrying a radio (false: any runner, while a radio station at home has power - the rule before R2).</summary>
        public static bool DistressNeedsRadio = true;
        /// <summary>The chance one of SC's raiders drops a handheld radio where it dies.</summary>
        public static float RaiderRadioChance = 0.04f;

        private static void ResetRadio()
        {
            HandheldRadioTab = true;
            RunnerRadioTalk = true;
            RunnerRadioVolume = 0.8f;
            RunnerRadioEffect = true;
            DistressNeedsRadio = true;
            RaiderRadioChance = 0.04f;
            // the per-world state of the feature: a new world starts with none of it
            ScHandheld.Reset();
            ScRadioTalk.Reset();
        }

        private static void ParseRadio(DynamicProperties p)
        {
            ParseBoolKey(p, "HandheldRadioTab", ref HandheldRadioTab);
            ParseBoolKey(p, "RunnerRadioTalk", ref RunnerRadioTalk);
            ParseFloat(p, "RunnerRadioVolume", ref RunnerRadioVolume, 0f, 1f);
            ParseBoolKey(p, "RunnerRadioEffect", ref RunnerRadioEffect);
            ParseBoolKey(p, "DistressNeedsRadio", ref DistressNeedsRadio);
            ParseFloat(p, "RaiderRadioChance", ref RaiderRadioChance, 0f, 1f);
        }

        /// <summary>The config line's handheld radio part.</summary>
        private static string RadioLine()
        {
            Func<float, string> f = x => x.ToString("0.##", CultureInfo.InvariantCulture);
            return "handheldTab=" + HandheldRadioTab
                   + " runnerTalk=" + (RunnerRadioTalk ? "volume " + f(RunnerRadioVolume) + (RunnerRadioEffect ? ", radio effect" : ", plain") : "off")
                   + " distress=" + (DistressNeedsRadio ? "runner's radio" : "any runner") + " raiderDrop=" + f(RaiderRadioChance);
        }
    }
}
