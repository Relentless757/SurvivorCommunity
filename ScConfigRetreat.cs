using System;
using System.Collections.Generic;
using System.Globalization;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round rt1 (2026-11-15, research/flee_meds/DESIGN.md Part A, every recommendation approved): the config keys of the
    /// retreat (ScRetreat, ScShelter, the UAI classes in UaiRetreat.cs). Kept apart from ScConfig.cs's long lists, as
    /// ScConfigTeam. Config/blocks.xml, block ConfigSurvivorCommunity, right after FleeBelowHealth; each key is commented
    /// there. ResetRetreat also clears the per-world state (ScRetreat, ScShelter): ScConfig.Reset runs at GameStarting.
    /// RetreatHome=false is exactly the behavior before this round (every new gate answers 1, nothing new runs).
    /// Round k1 adds the back-off keys (ScBackOff: BackOffHome, BackOffSteps, BackupKeepDistanceMax, BackOffNoPointSeconds,
    /// StandOffShootSeconds) here, after StandOffFollowerLeash; they are not behind RetreatHome. ResetRetreat clears ScBackOff too.
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

        // ---- round k1 (2026-11-15, ScBackOff): 0-XNPCCore's back-offs work again for members, kept inside the base. Not behind
        // RetreatHome or TeamMelee: the back-off fixes apply either way. Keys right after StandOffFollowerLeash in blocks.xml.

        /// <summary>The default BackOffSteps.</summary>
        public const string DefaultBackOffSteps = "10,6,3";
        /// <summary>Round k1: at home 0-XNPCCore's back-off goes to a point SC picks inside the base (ScBackOff); false: its own, unbounded.</summary>
        public static bool BackOffHome = true;
        /// <summary>Round k1: the steps (meters) tried for a back-off at home and for a stand-off's shorter steps, longest first.</summary>
        public static string BackOffSteps = DefaultBackOffSteps;
        /// <summary>
        /// Round k1: a member backs off (0-XNPCCore's BackupKeepDistance, weight 8) only from an enemy this close - its own band is
        /// 0-4 m and its back-off holds no shot, so from here to 4 m her shot (6) wins instead. Also how far from every enemy a
        /// bounded step must end (out of its reach).
        /// </summary>
        public static float BackupKeepDistanceMax = 2.5f;
        /// <summary>
        /// Round k1: when no step fits, the back-off waits this long against that enemy - one decision: the shot takes it - and a
        /// cornered stand-off stands and shoots this long (it was 3 s).
        /// </summary>
        public static float BackOffNoPointSeconds = 1f;
        /// <summary>Round k1: a stand-off step that leaves the enemy inside StandOffMin is followed by this long of shooting before the next step.</summary>
        public static float StandOffShootSeconds = 1.5f;
        private static List<float> backOffStepList;

        /// <summary>BackOffSteps, parsed (lazily): 1 to 30 m each, longest first, no repeats; nothing readable keeps the default.</summary>
        public static List<float> BackOffStepList()
        {
            if (backOffStepList != null) return backOffStepList;
            var list = new List<float>();
            foreach (string part in SplitList(BackOffSteps ?? ""))
            {
                float v;
                if (!float.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out v) || v < 1f || v > 30f)
                {
                    ScLog.Warn("config BackOffSteps entry '" + part + "' is not a number of meters from 1 to 30 - skipped");
                    continue;
                }
                bool dup = false;
                for (int i = 0; i < list.Count; i++) if (Math.Abs(list[i] - v) < 0.01f) dup = true;
                if (!dup) list.Add(v);
            }
            if (list.Count == 0)
            {
                if (!string.Equals(BackOffSteps, DefaultBackOffSteps, StringComparison.Ordinal))
                    ScLog.Warn("config BackOffSteps '" + BackOffSteps + "' has no readable step - the default " + DefaultBackOffSteps + " is used");
                list.Add(10f);
                list.Add(6f);
                list.Add(3f);
            }
            list.Sort((a, b) => b.CompareTo(a));
            backOffStepList = list;
            return list;
        }

        /// <summary>The steps for the log and the console ("10, 6, 3").</summary>
        public static string BackOffStepsText()
        {
            var parts = new List<string>();
            foreach (float f in BackOffStepList()) parts.Add(F2(f));
            return string.Join(", ", parts.ToArray());
        }

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
            // round k1
            BackOffHome = true;
            BackOffSteps = DefaultBackOffSteps;
            backOffStepList = null;
            BackupKeepDistanceMax = 2.5f;
            BackOffNoPointSeconds = 1f;
            StandOffShootSeconds = 1.5f;
            // the per-world state of the feature: a new world starts with no retreats and no shelter cache
            ScRetreat.Reset();
            ScShelter.Reset();
            ScBackOff.Reset();
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
            // round k1
            ParseBoolKey(p, "BackOffHome", ref BackOffHome);
            string s;
            if (p.Values.TryGetValue("BackOffSteps", out s)) { BackOffSteps = (s ?? "").Trim(); backOffStepList = null; }
            BackOffStepList();                                   // parsed now: a bad list shows in the log at load
            ParseFloat(p, "BackupKeepDistanceMax", ref BackupKeepDistanceMax, 0.5f, 4f);
            ParseFloat(p, "BackOffNoPointSeconds", ref BackOffNoPointSeconds, 0.2f, 5f);
            ParseFloat(p, "StandOffShootSeconds", ref StandOffShootSeconds, 0f, 10f);
        }

        /// <summary>Round k1: the back-off keys in one line (the load log).</summary>
        public static string BackOffLine()
        {
            return (BackOffHome ? "at home kept inside the base (steps " + BackOffStepsText() + " m on 7 directions, out of reach of every enemy)"
                                : "NOT bounded at home (BackOffHome false: 0-XNPCCore's own back-off)")
                   + "; members back off within " + F2(BackupKeepDistanceMax) + " m (0-XNPCCore's band is 4: the shot wins beyond)"
                   + "; no point: the shot takes " + F2(BackOffNoPointSeconds) + " s (a cornered stand-off too)"
                   + "; a back-off that gains nothing (1.5 s in, or at its end: still within " + F2(BackupKeepDistanceMax) + " m or the gap not 1 m larger)"
                   + " and a short stand-off step are followed by " + F2(StandOffShootSeconds) + " s of shooting";
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
