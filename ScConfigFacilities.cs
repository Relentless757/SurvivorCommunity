using System;
using System.Collections.Generic;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round m1f (2026-11-17, ROADMAP.md section 3 items 4 and 5): the config keys of base defense BD1 - Lights out
    /// (ScLightsOut), Watch fatigue (ScWatchFatigue) and Salvage (ScSalvage). Kept apart from ScConfig.cs's long lists, as
    /// ScConfigTeam. Config/blocks.xml, block ConfigSurvivorCommunity, right after the watch post keys; each key is commented
    /// there. The Facilities tab itself has no keys.
    /// </summary>
    public static partial class ScConfig
    {
        /// <summary>The Defense entry offers Lights out (false: not offered; one already ordered still ends at dawn).</summary>
        public static bool LightsOut = true;
        /// <summary>During Lights out the people's part of the base's noise is multiplied by this (0.5: halved).</summary>
        public static float LightsOutPeopleNoise = 0.5f;
        /// <summary>Lights out switches the running generators inside the home zone off until dawn (false: the people's part only).</summary>
        public static bool LightsOutGenerators = true;
        /// <summary>Lights out can be ordered from this many game hours before dusk until dawn (24: at any time).</summary>
        public static float LightsOutBeforeDuskHours = 2f;

        /// <summary>A long watch costs a little mood (false: no watch term in the mood).</summary>
        public static bool WatchFatigue = true;
        /// <summary>A guard is tired once on watch this many game hours, and stays tired through the rest after a full shift.</summary>
        public static float WatchFatigueHours = 8f;
        /// <summary>The mood a tired guard loses.</summary>
        public static int WatchFatigueMood = 5;
        /// <summary>Traits that like a night watch: on watch while it is dark they gain WatchNightMood and are never tired (Insomniac).</summary>
        public static readonly List<string> WatchNightTraits = new List<string>();
        public const string DefaultWatchNightTraits = "Insomniac";
        /// <summary>The mood a night person gains on a night watch.</summary>
        public static int WatchNightMood = 3;

        /// <summary>After a held attack the corpse pile yields 1 material per this many kills (0: no salvage).</summary>
        public static int SalvageKillsPerMaterial = 5;

        private static void ResetFacilities()
        {
            LightsOut = true;
            LightsOutPeopleNoise = 0.5f;
            LightsOutGenerators = true;
            LightsOutBeforeDuskHours = 2f;
            WatchFatigue = true;
            WatchFatigueHours = 8f;
            WatchFatigueMood = 5;
            WatchNightTraits.Clear();
            WatchNightTraits.AddRange(SplitList(DefaultWatchNightTraits));
            WatchNightMood = 3;
            SalvageKillsPerMaterial = 5;
        }

        private static void ParseFacilities(DynamicProperties p)
        {
            ParseBoolKey(p, "LightsOut", ref LightsOut);
            ParseFloat(p, "LightsOutPeopleNoise", ref LightsOutPeopleNoise, 0f, 1f);
            ParseBoolKey(p, "LightsOutGenerators", ref LightsOutGenerators);
            ParseFloat(p, "LightsOutBeforeDuskHours", ref LightsOutBeforeDuskHours, 0f, 24f);
            ParseBoolKey(p, "WatchFatigue", ref WatchFatigue);
            ParseFloat(p, "WatchFatigueHours", ref WatchFatigueHours, 0.5f, 48f);
            ParseInt(p, "WatchFatigueMood", ref WatchFatigueMood, 0, 50);
            ParseList(p, "WatchNightTraits", WatchNightTraits);   // no NightOwl: its night watch is round m1t's nightwatch=6, its fatigue exemption ScTraitEffects.LikesNightWatch at the merge
            ParseInt(p, "WatchNightMood", ref WatchNightMood, 0, 50);
            ParseInt(p, "SalvageKillsPerMaterial", ref SalvageKillsPerMaterial, 0, 1000);
        }

        /// <summary>The config log line.</summary>
        private static string FacilitiesLine()
        {
            return "lights out " + (LightsOut ? "offered from " + F2(LightsOutBeforeDuskHours) + " h before dusk, people's noise x" + F2(LightsOutPeopleNoise)
                                               + (LightsOutGenerators ? ", the zone's generators off until dawn" : ", generators left on") : "off")
                   + "; watch fatigue " + (WatchFatigue ? "-" + WatchFatigueMood + " mood after " + F2(WatchFatigueHours) + " h on watch and through the rest, +"
                                                         + WatchNightMood + " on a night watch for " + (WatchNightTraits.Count > 0 ? string.Join("/", WatchNightTraits.ToArray()) : "nobody") : "off")
                   + "; salvage " + (SalvageKillsPerMaterial > 0 ? "1 material per " + SalvageKillsPerMaterial + " kills of a held attack" : "off");
        }
    }
}
