using System;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round 30 (S30): the config keys of streamer names (ScStreamerNames) - a streamer's list of viewers' names that SC gives
    /// to the new survivors (and, if asked, raiders) it makes. Kept apart from ScConfig.cs's long lists, as ScConfigStuck.
    /// Config/blocks.xml, block ConfigSurvivorCommunity, after TraitsPerRecruit; each key is commented there. ResetNames also
    /// clears ScStreamerNames' per-world state (ScConfig.Reset runs at GameStarting). Both games.
    /// </summary>
    public static partial class ScConfig
    {
        /// <summary>The list's file when the config names none: relative, so it lives in the game's user data folder.</summary>
        public const string DefaultStreamerNamesFile = "StreamerNames.txt";

        /// <summary>StreamerNamesFor: which of SC's new bodies take a list name.</summary>
        public const int NamesForSurvivors = 0, NamesForRaiders = 1, NamesForAll = 2;

        /// <summary>The switch: SC gives the list's names to the new bodies it makes.</summary>
        public static bool StreamerNames = false;
        /// <summary>
        /// The list's file. A relative path is read from the SurvivorCommunity folder in the game's user data folder
        /// (GameIO.GetUserGameDataDir(): %APPDATA%\7DaysToDie\SurvivorCommunity), so it survives a mod update; an absolute one
        /// is used as it is. %VARIABLES% are expanded.
        /// </summary>
        public static string StreamerNamesFile = DefaultStreamerNamesFile;
        /// <summary>survivors (every non-hostile survivor SC makes), raiders (SC's hostile ones) or all.</summary>
        public static int StreamerNamesFor = NamesForSurvivors;

        private static void ResetNames()
        {
            StreamerNames = false;
            StreamerNamesFile = DefaultStreamerNamesFile;
            StreamerNamesFor = NamesForSurvivors;
            // the per-world state of the feature: a new world starts with none of it
            ScStreamerNames.Reset();
        }

        private static void ParseNames(DynamicProperties p)
        {
            ParseBoolKey(p, "StreamerNames", ref StreamerNames);
            string s;
            if (p.Values.TryGetValue("StreamerNamesFile", out s) && s != null && s.Trim().Length > 0) StreamerNamesFile = s.Trim();
            if (p.Values.TryGetValue("StreamerNamesFor", out s) && !string.IsNullOrEmpty(s))
            {
                int f = NamesForWord(s);
                if (f < 0) ScLog.Warn("StreamerNamesFor '" + s + "' is not survivors, raiders or all - survivors is used");
                else StreamerNamesFor = f;
            }
        }

        /// <summary>survivors / raiders / all (case-insensitive; "survivor", "raider", "both" too), or -1.</summary>
        public static int NamesForWord(string s)
        {
            switch ((s ?? "").Trim().ToLowerInvariant())
            {
                case "survivors": case "survivor": return NamesForSurvivors;
                case "raiders": case "raider": return NamesForRaiders;
                case "all": case "both": return NamesForAll;
                default: return -1;
            }
        }

        /// <summary>The StreamerNamesFor value as the config spells it.</summary>
        public static string NamesForText()
        {
            return StreamerNamesFor == NamesForRaiders ? "raiders" : StreamerNamesFor == NamesForAll ? "all" : "survivors";
        }

        /// <summary>The config line's streamer names part.</summary>
        private static string NamesLine()
        {
            return StreamerNames ? "on, for " + NamesForText() + ", file " + StreamerNamesFile : "off";
        }
    }
}
