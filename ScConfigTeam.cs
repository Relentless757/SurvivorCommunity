using System;
using System.Collections.Generic;
using System.Globalization;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round tm1 (2026-11-15, research/combat/TEAM_MELEE_REPORT.md, every recommendation approved): the config keys of the
    /// team strength rule (ScTeam, ScArms' MeleeFirst / GunFirst / CloseRange). Kept apart from ScConfig.cs's long lists, as
    /// ScConfigStuck. Config/blocks.xml, block ConfigSurvivorCommunity, after OverwhelmedGunSeconds; each key is commented
    /// there. ResetTeam also clears ScTeam's per-world verdicts (ScConfig.Reset runs at GameStarting). TeamMelee=false is
    /// exactly the behavior before this round.
    /// </summary>
    public static partial class ScConfig
    {
        /// <summary>The threat table's default: the tag weight of an enemy (highest matching entry wins; "a+b" needs both tags).</summary>
        public const string DefaultTeamThreatTags =
            "walker:1,feral:2,radiated:3,charged:3.5,infernal:4.5,dog:1.2,wolf:1.2,coyote:1.2,mountainlion:2,bear:5,bear+zombie:6,vulture:0.6,spider:1.5";
        /// <summary>Multipliers on the tag weight (every matching entry multiplies).</summary>
        public const string DefaultTeamThreatTagMult = "special:1.5,strong:1.3";
        /// <summary>Multipliers on an enemy's final weight, after the health term (every matching entry multiplies).</summary>
        public const string DefaultTeamThreatMult = "crawler:0.6";
        /// <summary>Tags that make an enemy unsafe to melee (MeleeUnsafe); EntityZombieCop - the cops and the demolition zombie - always is.</summary>
        public const string DefaultTeamGunTags = "zombieCop";

        /// <summary>Team strength decides melee for the community's members (false: everything as before round tm1).</summary>
        public static bool TeamMelee = true;
        /// <summary>Friends and enemies within this many meters of a member are counted (a live base attack: the whole defended area).</summary>
        public static float TeamRadius = 15f;
        /// <summary>Matched when F >= TeamMargin x T.</summary>
        public static float TeamMargin = 1f;
        /// <summary>A matched fight stops being matched after TeamLeaveRecounts recounts in a row with F &lt; TeamLeaveMargin x T.</summary>
        public static float TeamLeaveMargin = 0.8f;
        public static int TeamLeaveRecounts = 2;
        /// <summary>Seconds between two recounts of one member (staggered by entity id).</summary>
        public static float TeamRecountSeconds = 1f;
        /// <summary>A verdict older than this counts as none (she is not melee-led).</summary>
        public static float TeamFreshSeconds = 3f;
        /// <summary>F: the player, another ally (a hire, a friendly NPC) and a member on watch, each times HpF.</summary>
        public static float TeamPlayerWeight = 2f;
        public static float TeamAllyWeight = 1f;
        public static float TeamGuardWeight = 0.8f;
        /// <summary>F: a member in the middle of a medical use counts this share.</summary>
        public static float TeamMedicalMult = 0.5f;
        /// <summary>F: a member counts (TeamStarBase + TeamStarStep x Fighting stars) x HpF x her weapon factor.</summary>
        public static float TeamStarBase = 0.85f;
        public static float TeamStarStep = 0.15f;
        /// <summary>The weapon factor: a melee weapon in her pair, a ranged weapon only, nothing.</summary>
        public static float TeamWeaponMelee = 1f;
        public static float TeamWeaponRanged = 0.8f;
        public static float TeamWeaponBare = 0.5f;
        /// <summary>HpF = TeamHealthFloor + (1 - TeamHealthFloor) x the health share (0.5 + 0.5 x share by default).</summary>
        public static float TeamHealthFloor = 0.5f;
        /// <summary>Below this health share a member is no longer melee-led; she is again from TeamLowHealthBack.</summary>
        public static float TeamLowHealth = 0.5f;
        public static float TeamLowHealthBack = 0.6f;
        /// <summary>A swap made by SC's rules (MeleeFirst, GunFirst) holds the opposite one this long (one clock for both).</summary>
        public static float TeamSwapHoldSeconds = 5f;
        /// <summary>How long a melee-led member holding her gun must have a target in reach before she takes her melee weapon.</summary>
        public static float TeamMeleeFirstSeconds = 1f;
        /// <summary>A melee-led member is overwhelmed by this many attackers more than her usual threshold (decision 7).</summary>
        public static int TeamOverwhelmedExtra = 1;
        /// <summary>A target found out of melee reach stays "needs the gun" this long (0-XNPCCore forgets its own unreachable mark after 20 s).</summary>
        public static float TeamUnreachableSeconds = 20f;
        /// <summary>T: the threat table as the config spells it (parsed into TeamThreats on first use).</summary>
        public static string TeamThreatTags = DefaultTeamThreatTags;
        public static string TeamThreatTagMult = DefaultTeamThreatTagMult;
        public static string TeamThreatMult = DefaultTeamThreatMult;
        /// <summary>The tag weight of an enemy no entry of TeamThreatTags matches.</summary>
        public static float TeamThreatDefault = 1f;
        /// <summary>
        /// The health term: clamp((MaxHealth / TeamThreatHpBase) ^ TeamThreatHpPower, TeamThreatHpMin, TeamThreatHpMax). The
        /// report's base was 200 (a normal zombie); 230 because vanilla rolls every zombie's HealthMax up to 15% higher
        /// (RANDOM_ZOMBIE_HEALTH), and at 200 half the plain walkers would weigh more than 1 - a 1-star member alone would no
        /// longer melee one (decision 2).
        /// </summary>
        public static float TeamThreatHpBase = 230f;
        public static float TeamThreatHpPower = 0.7f;
        public static float TeamThreatHpMin = 0.5f;
        public static float TeamThreatHpMax = 8f;
        /// <summary>Tags of an enemy that is never meleed on the team rule (decision 10).</summary>
        public static string TeamGunTags = DefaultTeamGunTags;

        /// <summary>One parsed threat entry: the tags it needs (all of them) and its weight or multiplier.</summary>
        public sealed class TeamTagEntry
        {
            public string Text = "";
            public FastTags<TagGroup.Global> Tags;
            public float Value;
        }

        private static List<TeamTagEntry> teamThreats, teamTagMults, teamMults;
        private static FastTags<TagGroup.Global> teamGunTags;
        private static bool teamGunTagsParsed;

        /// <summary>TeamThreatTags, parsed (lazily: a table that cannot be read falls back to the default with a warning).</summary>
        public static List<TeamTagEntry> TeamThreats()
        {
            if (teamThreats == null) teamThreats = ParseTeamTable("TeamThreatTags", TeamThreatTags, DefaultTeamThreatTags);
            return teamThreats;
        }

        public static List<TeamTagEntry> TeamTagMults()
        {
            if (teamTagMults == null) teamTagMults = ParseTeamTable("TeamThreatTagMult", TeamThreatTagMult, DefaultTeamThreatTagMult);
            return teamTagMults;
        }

        public static List<TeamTagEntry> TeamMults()
        {
            if (teamMults == null) teamMults = ParseTeamTable("TeamThreatMult", TeamThreatMult, DefaultTeamThreatMult);
            return teamMults;
        }

        /// <summary>TeamGunTags, parsed (an empty value: only the EntityZombieCop class is melee-unsafe).</summary>
        public static FastTags<TagGroup.Global> TeamGunTagSet()
        {
            if (!teamGunTagsParsed)
            {
                teamGunTags = string.IsNullOrEmpty(TeamGunTags) ? FastTags<TagGroup.Global>.none : FastTags<TagGroup.Global>.Parse(TeamGunTags);
                teamGunTagsParsed = true;
            }
            return teamGunTags;
        }

        private static void ResetTeam()
        {
            TeamMelee = true;
            TeamRadius = 15f;
            TeamMargin = 1f;
            TeamLeaveMargin = 0.8f;
            TeamLeaveRecounts = 2;
            TeamRecountSeconds = 1f;
            TeamFreshSeconds = 3f;
            TeamPlayerWeight = 2f;
            TeamAllyWeight = 1f;
            TeamGuardWeight = 0.8f;
            TeamMedicalMult = 0.5f;
            TeamStarBase = 0.85f;
            TeamStarStep = 0.15f;
            TeamWeaponMelee = 1f;
            TeamWeaponRanged = 0.8f;
            TeamWeaponBare = 0.5f;
            TeamHealthFloor = 0.5f;
            TeamLowHealth = 0.5f;
            TeamLowHealthBack = 0.6f;
            TeamSwapHoldSeconds = 5f;
            TeamMeleeFirstSeconds = 1f;
            TeamOverwhelmedExtra = 1;
            TeamUnreachableSeconds = 20f;
            TeamThreatTags = DefaultTeamThreatTags;
            TeamThreatTagMult = DefaultTeamThreatTagMult;
            TeamThreatMult = DefaultTeamThreatMult;
            TeamThreatDefault = 1f;
            TeamThreatHpBase = 230f;
            TeamThreatHpPower = 0.7f;
            TeamThreatHpMin = 0.5f;
            TeamThreatHpMax = 8f;
            TeamGunTags = DefaultTeamGunTags;
            teamThreats = teamTagMults = teamMults = null;
            teamGunTagsParsed = false;
            // the per-world state of the feature: a new world starts with no verdicts
            ScTeam.Reset();
        }

        private static void ParseTeam(DynamicProperties p)
        {
            ParseBoolKey(p, "TeamMelee", ref TeamMelee);
            ParseFloat(p, "TeamRadius", ref TeamRadius, 3f, 60f);
            ParseFloat(p, "TeamMargin", ref TeamMargin, 0.1f, 10f);
            ParseFloat(p, "TeamLeaveMargin", ref TeamLeaveMargin, 0.1f, 10f);
            if (TeamLeaveMargin > TeamMargin)
            {
                ScLog.Warn("config TeamLeaveMargin=" + TeamLeaveMargin + " is above TeamMargin=" + TeamMargin + " - set to TeamMargin (no hysteresis)");
                TeamLeaveMargin = TeamMargin;
            }
            ParseInt(p, "TeamLeaveRecounts", ref TeamLeaveRecounts, 1, 20);
            ParseFloat(p, "TeamRecountSeconds", ref TeamRecountSeconds, 0.2f, 10f);
            ParseFloat(p, "TeamFreshSeconds", ref TeamFreshSeconds, 0.5f, 60f);
            if (TeamFreshSeconds < 2f * TeamRecountSeconds)
            {
                ScLog.Warn("config TeamFreshSeconds=" + TeamFreshSeconds + " is shorter than two recounts (TeamRecountSeconds=" + TeamRecountSeconds + ") - set to " + (2f * TeamRecountSeconds));
                TeamFreshSeconds = 2f * TeamRecountSeconds;
            }
            ParseFloat(p, "TeamPlayerWeight", ref TeamPlayerWeight, 0f, 20f);
            ParseFloat(p, "TeamAllyWeight", ref TeamAllyWeight, 0f, 20f);
            ParseFloat(p, "TeamGuardWeight", ref TeamGuardWeight, 0f, 20f);
            ParseFloat(p, "TeamMedicalMult", ref TeamMedicalMult, 0f, 1f);
            ParseFloat(p, "TeamStarBase", ref TeamStarBase, 0f, 10f);
            ParseFloat(p, "TeamStarStep", ref TeamStarStep, 0f, 5f);
            ParseFloat(p, "TeamWeaponMelee", ref TeamWeaponMelee, 0f, 5f);
            ParseFloat(p, "TeamWeaponRanged", ref TeamWeaponRanged, 0f, 5f);
            ParseFloat(p, "TeamWeaponBare", ref TeamWeaponBare, 0f, 5f);
            ParseFloat(p, "TeamHealthFloor", ref TeamHealthFloor, 0f, 1f);
            ParseFloat(p, "TeamLowHealth", ref TeamLowHealth, 0f, 1f);
            ParseFloat(p, "TeamLowHealthBack", ref TeamLowHealthBack, 0f, 1f);
            if (TeamLowHealthBack < TeamLowHealth)
            {
                ScLog.Warn("config TeamLowHealthBack=" + TeamLowHealthBack + " is below TeamLowHealth=" + TeamLowHealth + " - set to TeamLowHealth");
                TeamLowHealthBack = TeamLowHealth;
            }
            ParseFloat(p, "TeamSwapHoldSeconds", ref TeamSwapHoldSeconds, 0f, 60f);
            ParseFloat(p, "TeamMeleeFirstSeconds", ref TeamMeleeFirstSeconds, 0f, 10f);
            ParseInt(p, "TeamOverwhelmedExtra", ref TeamOverwhelmedExtra, 0, 10);
            ParseFloat(p, "TeamUnreachableSeconds", ref TeamUnreachableSeconds, 0f, 120f);
            string s;
            if (p.Values.TryGetValue("TeamThreatTags", out s)) { TeamThreatTags = s ?? ""; teamThreats = null; }
            if (p.Values.TryGetValue("TeamThreatTagMult", out s)) { TeamThreatTagMult = s ?? ""; teamTagMults = null; }
            if (p.Values.TryGetValue("TeamThreatMult", out s)) { TeamThreatMult = s ?? ""; teamMults = null; }
            ParseFloat(p, "TeamThreatDefault", ref TeamThreatDefault, 0f, 50f);
            ParseFloat(p, "TeamThreatHpBase", ref TeamThreatHpBase, 10f, 100000f);
            ParseFloat(p, "TeamThreatHpPower", ref TeamThreatHpPower, 0f, 3f);
            ParseFloat(p, "TeamThreatHpMin", ref TeamThreatHpMin, 0f, 50f);
            ParseFloat(p, "TeamThreatHpMax", ref TeamThreatHpMax, 0f, 100f);
            if (TeamThreatHpMax < TeamThreatHpMin) TeamThreatHpMax = TeamThreatHpMin;
            if (p.Values.TryGetValue("TeamGunTags", out s)) { TeamGunTags = (s ?? "").Trim(); teamGunTagsParsed = false; }
            // parsed now, so a bad table shows in the log at load rather than in the first fight
            TeamThreats();
            TeamTagMults();
            TeamMults();
            TeamGunTagSet();
        }

        /// <summary>
        /// "tag[+tag]:value,..." into entries (every tag of an entry must be on the enemy). An entry that cannot be read is
        /// skipped with a warning; an empty value is an empty table; a value with no readable entry keeps the default.
        /// </summary>
        private static List<TeamTagEntry> ParseTeamTable(string key, string text, string fallback)
        {
            var list = new List<TeamTagEntry>();
            if (string.IsNullOrEmpty(text) || text.Trim().Length == 0) return list;
            int bad = 0;
            foreach (string part in SplitList(text))
            {
                int c = part.LastIndexOf(':');
                float v;
                string tags = c > 0 ? part.Substring(0, c).Trim() : "";
                if (c <= 0 || tags.Length == 0
                    || !float.TryParse(part.Substring(c + 1).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v) || v < 0f || float.IsNaN(v) || float.IsInfinity(v))
                {
                    ScLog.Warn("config " + key + " entry '" + part + "' is not tag:number (tag+tag:number for several tags; a number of 0 or more) - skipped");
                    bad++;
                    continue;
                }
                FastTags<TagGroup.Global> ft = FastTags<TagGroup.Global>.Parse(tags.Replace('+', ','));
                if (ft.IsEmpty)
                {
                    ScLog.Warn("config " + key + " entry '" + part + "' names no tag - skipped");
                    bad++;
                    continue;
                }
                list.Add(new TeamTagEntry { Text = tags, Tags = ft, Value = v });
            }
            if (list.Count == 0 && bad > 0 && fallback != null && !string.Equals(text, fallback, StringComparison.Ordinal))
            {
                ScLog.Warn("config " + key + " '" + text + "' has no readable entry - the default is used");
                return ParseTeamTable(key, fallback, null);
            }
            return list;
        }

        private static string TeamTableText(List<TeamTagEntry> list)
        {
            var parts = new List<string>();
            foreach (TeamTagEntry t in list) parts.Add(t.Text + ":" + t.Value.ToString("0.##", CultureInfo.InvariantCulture));
            return parts.Count > 0 ? string.Join(",", parts.ToArray()) : "-";
        }

        /// <summary>The config line's team strength part.</summary>
        private static string TeamLine()
        {
            Func<float, string> f = x => x.ToString("0.##", CultureInfo.InvariantCulture);
            if (!TeamMelee) return "off (TeamMelee=false: 0-XNPCCore's and the overwhelmed rules as before)";
            return "on, within " + f(TeamRadius) + " m (a live base attack: the defended area), matched at F >= " + f(TeamMargin) + " T, left after "
                   + TeamLeaveRecounts + " recounts below " + f(TeamLeaveMargin) + " T, recount " + f(TeamRecountSeconds) + " s (fresh " + f(TeamFreshSeconds) + " s)"
                   + "; F: member (" + f(TeamStarBase) + " + " + f(TeamStarStep) + "/star) x HpF x weapon " + f(TeamWeaponMelee) + "/" + f(TeamWeaponRanged) + "/" + f(TeamWeaponBare)
                   + ", player " + f(TeamPlayerWeight) + ", ally " + f(TeamAllyWeight) + ", guard " + f(TeamGuardWeight) + ", medical x" + f(TeamMedicalMult)
                   + ", HpF " + f(TeamHealthFloor) + "+" + f(1f - TeamHealthFloor) + "xhp"
                   + "; T: tags " + TeamTableText(TeamThreats()) + " (else " + f(TeamThreatDefault) + ") x " + TeamTableText(TeamTagMults())
                   + ", hp (max/" + f(TeamThreatHpBase) + ")^" + f(TeamThreatHpPower) + " in " + f(TeamThreatHpMin) + ".." + f(TeamThreatHpMax)
                   + ", then x " + TeamTableText(TeamMults()) + "; gun on " + (string.IsNullOrEmpty(TeamGunTags) ? "-" : TeamGunTags) + " + EntityZombieCop"
                   + "; low health " + f(TeamLowHealth) + " (back " + f(TeamLowHealthBack) + "), swap hold " + f(TeamSwapHoldSeconds) + " s, melee first after "
                   + f(TeamMeleeFirstSeconds) + " s, overwhelmed +" + TeamOverwhelmedExtra + ", unreachable kept " + f(TeamUnreachableSeconds) + " s";
        }
    }
}
