using System.Globalization;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round rt1 (2026-11-15, research/flee_meds/DESIGN.md Part A, every recommendation approved): the config keys of the
    /// retreat (ScRetreat, ScShelter, the UAI classes in UaiRetreat.cs). Kept apart from ScConfig.cs's long lists, as
    /// ScConfigTeam. Config/blocks.xml, block ConfigSurvivorCommunity, right after FleeBelowHealth; each key is commented
    /// there. ResetRetreat also clears the per-world state (ScRetreat, ScShelter): ScConfig.Reset runs at GameStarting.
    /// RetreatHome=false is exactly the behavior before this round (every new gate answers 1, nothing new runs).
    /// </summary>
    public static partial class ScConfig
    {
        /// <summary>Decision 13: hurt members (and cowards) at home retreat inside or stand off with a gun; false: as before round rt1.</summary>
        public static bool RetreatHome = true;
        /// <summary>Decision 6: a Cowardly member at home who is not hanging back (tm1) shelters instead of running from the base.</summary>
        public static bool RetreatCowards = true;
        /// <summary>Decision 8: a hurt member's retreat ends once her health is back to this share of her max.</summary>
        public static float RetreatHealedAt = 0.45f;
        /// <summary>Decision 8: a retreat ends once no threat has been about for this many seconds.</summary>
        public static float RetreatClearSeconds = 20f;
        /// <summary>Decision 8: a sheltering member fights back at an enemy within this many meters that is after her.</summary>
        public static float RetreatFightBackRange = 2.5f;
        /// <summary>A shelter spot has a wall (or a door) within 6 cells in at least this many of the 8 directions at head height.</summary>
        public static int ShelterWalls = 6;
        /// <summary>A shelter spot is at least this far (meters) from the threat.</summary>
        public static float ShelterMinThreatDist = 12f;
        /// <summary>Real seconds between two scans of the home zone for shelter spots (also rescanned when the zone changes).</summary>
        public static float ShelterRescanSeconds = 300f;
        /// <summary>Decision 2: a hurt member with a gun backs off from an enemy inside this many meters...</summary>
        public static float StandOffMin = 8f;
        /// <summary>...to about this far from it, never past her engage range minus 1 m.</summary>
        public static float StandOffMax = 15f;
        /// <summary>Decision 4: away from home a follower with a gun stands off within this many meters of you (0: she flees as before).</summary>
        public static float StandOffFollowerLeash = 20f;

        private static void ResetRetreat()
        {
            RetreatHome = true;
            RetreatCowards = true;
            RetreatHealedAt = 0.45f;
            RetreatClearSeconds = 20f;
            RetreatFightBackRange = 2.5f;
            ShelterWalls = 6;
            ShelterMinThreatDist = 12f;
            ShelterRescanSeconds = 300f;
            StandOffMin = 8f;
            StandOffMax = 15f;
            StandOffFollowerLeash = 20f;
            // the per-world state of the feature: a new world starts with no retreats and no shelter cache
            ScRetreat.Reset();
            ScShelter.Reset();
        }

        private static void ParseRetreat(DynamicProperties p)
        {
            ParseBoolKey(p, "RetreatHome", ref RetreatHome);
            ParseBoolKey(p, "RetreatCowards", ref RetreatCowards);
            ParseFloat(p, "RetreatHealedAt", ref RetreatHealedAt, 0.05f, 1f);
            // FleeBelowHealth is parsed before this (ScConfig.Parse): a retreat must end above where it starts
            if (FleeBelowHealth > 0f && RetreatHealedAt <= FleeBelowHealth)
            {
                float to = UnityEngine.Mathf.Min(1f, FleeBelowHealth + 0.1f);
                ScLog.Warn("config RetreatHealedAt=" + F2(RetreatHealedAt) + " is not above FleeBelowHealth=" + F2(FleeBelowHealth) + " - set to " + F2(to));
                RetreatHealedAt = to;
            }
            ParseFloat(p, "RetreatClearSeconds", ref RetreatClearSeconds, 0f, 600f);
            ParseFloat(p, "RetreatFightBackRange", ref RetreatFightBackRange, 0.5f, 10f);
            ParseInt(p, "ShelterWalls", ref ShelterWalls, 1, 8);
            ParseFloat(p, "ShelterMinThreatDist", ref ShelterMinThreatDist, 0f, 60f);
            ParseFloat(p, "ShelterRescanSeconds", ref ShelterRescanSeconds, 10f, 3600f);
            ParseFloat(p, "StandOffMin", ref StandOffMin, 2f, 30f);
            ParseFloat(p, "StandOffMax", ref StandOffMax, 3f, 60f);
            if (StandOffMax < StandOffMin + 2f)
            {
                ScLog.Warn("config StandOffMax=" + F2(StandOffMax) + " is less than StandOffMin + 2 (" + F2(StandOffMin + 2f) + ") - set to it");
                StandOffMax = StandOffMin + 2f;
            }
            ParseFloat(p, "StandOffFollowerLeash", ref StandOffFollowerLeash, 0f, 60f);
        }

        /// <summary>Round rt1: the retreat keys in one line (the load log, sc retreat).</summary>
        public static string RetreatLine()
        {
            if (!RetreatHome) return "off (RetreatHome false: hurt members flee as before round rt1)";
            return "on: at home a member hurt below " + F2(FleeBelowHealth) + " with nothing to heal with"
                   + (RetreatCowards ? ", or Cowardly and not hanging back," : " (cowards flee as before: RetreatCowards false)")
                   + " shelters inside (a roof, " + ShelterWalls + " of 8 walls, " + F2(ShelterMinThreatDist) + " m from the threat) or, hurt with a gun, stands off "
                   + F2(StandOffMin) + "-" + F2(StandOffMax) + " m; ends at " + F2(RetreatHealedAt) + " health or " + F2(RetreatClearSeconds)
                   + " s without a threat; fights back within " + F2(RetreatFightBackRange) + " m; "
                   + (StandOffFollowerLeash > 0f ? "followers away stand off within " + F2(StandOffFollowerLeash) + " m of you" : "followers away flee as before (StandOffFollowerLeash 0)")
                   + "; shelter scan every " + ShelterRescanSeconds.ToString("0", CultureInfo.InvariantCulture) + " s";
        }
    }
}
