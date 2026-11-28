using System;
using System.Collections.Generic;
using System.Globalization;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round tm1 (2026-11-15, research/combat/TEAM_MELEE_REPORT.md, every recommendation approved): the config keys of the
    /// team strength rule (ScTeam) and, since round k4, of the one posture per member (ScPosture, ScArms.Hand) and the
    /// investigation of idle intruders (ScInvestigate). Kept apart from ScConfig.cs's long lists, as ScConfigStuck.
    /// Config/blocks.xml, block ConfigSurvivorCommunity, after OverwhelmedGunSeconds; each key is commented there. ResetTeam
    /// also clears the per-world verdicts, postures and intruders (ScConfig.Reset runs at GameStarting). TeamMelee=false: no
    /// team verdict, so never the MELEE posture (round k4: every fight with a gun is KITE).
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
        /// <summary>
        /// Matched when F >= TeamMargin x T. Round k3 tried 1.25 (research/combat/SC_VS_NPCCORE.md 4.2); the user's decision
        /// after the Fable review of k3+k4: "Exact parity, lone member melees" - 1.0, so a healthy member alone matches one walker.
        /// </summary>
        public static float TeamMargin = 1f;
        /// <summary>
        /// A matched fight stops being matched after TeamLeaveRecounts recounts in a row with F &lt; TeamLeaveMargin x T, and an
        /// outmatched one is matched again after as many recounts in a row with F >= TeamMargin x T (the Fable review of k3+k4,
        /// F3: the player's 5 s hit window no longer flips the verdict). 0.8 (F2; k3's 1.0 under a margin of 1 had a lone 1-star
        /// member leave "matched" at about 89% health): with TeamHealthFullAt that leaves TeamLowHealth (60%) as her only exit.
        /// </summary>
        public static float TeamLeaveMargin = 0.8f;
        public static int TeamLeaveRecounts = 2;
        /// <summary>Seconds between two recounts of one member (staggered by entity id).</summary>
        public static float TeamRecountSeconds = 1f;
        /// <summary>A verdict older than this counts as none (she is not melee-led).</summary>
        public static float TeamFreshSeconds = 3f;
        /// <summary>F: the player, another ally (a hire, a friendly NPC) and a member on watch, each times HpF.</summary>
        public static float TeamPlayerWeight = 2f;
        /// <summary>
        /// Round k1 (the user's decisions; the Fable review's F2a, "Only my hits count"): the player counts TeamPlayerWeight only
        /// while he is fighting - he landed a hit on an enemy in the counted area in the last TeamPlayerFightingSeconds (a kill
        /// counts too) - else 0. An enemy that merely has him as its target does not count.
        /// </summary>
        public static float TeamPlayerFightingSeconds = 5f;
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
        /// <summary>
        /// HpF = TeamHealthFloor + (1 - TeamHealthFloor) x min(1, health share / TeamHealthFullAt): 0.5 at no health, 1 from 90%
        /// of it. TeamHealthFullAt (the Fable review of round tm1): a scratch does not count, so a healthy 1-star member alone
        /// meleed one walker (decision 2: at 99% her F was 0.995 against T 1). Round k3's margin of 1.25 made that fight
        /// outmatched for a member below 3 stars; the user's decision after the Fable review of k3+k4 put it back to 1.0.
        /// </summary>
        public static float TeamHealthFloor = 0.5f;
        public static float TeamHealthFullAt = 0.9f;
        /// <summary>
        /// Below this health share a member is no longer melee-led; she is again from TeamLowHealthBack. Round k3: 0.6 / 0.7 (were
        /// 0.5 / 0.6) - the MELEE posture needs 60% (research/combat/SC_VS_NPCCORE.md 4.1).
        /// </summary>
        public static float TeamLowHealth = 0.6f;
        public static float TeamLowHealthBack = 0.7f;
        /// <summary>
        /// Round k4: the one hand clock (ScPosture). After ANY change of what she holds - SC's swap or 0-XNPCCore's own - SC
        /// makes no swap for this long, and her posture does not switch between MELEE and KITE either (each swap can cost a
        /// reload). Only a swing 0-XNPCCore refuses (she cannot fight with the melee weapon there) draws the gun at once.
        /// </summary>
        public static float TeamSwapHoldSeconds = 5f;
        /// <summary>A melee-led member is overwhelmed by this many attackers more than her usual threshold (decision 7).</summary>
        public static int TeamOverwhelmedExtra = 1;
        /// <summary>
        /// A target found out of melee reach stays "needs the gun" this long. 45 (the Fable review; 20 at first, as 0-XNPCCore's
        /// own mark): a gun holder cannot find out whether it has come in reach, so each lapse is a probe - a swap to melee, the
        /// finding again, and a swap back - which costs two reloads. Round k4: also a swing 0-XNPCCore refused (its bout ended
        /// for a wall, bars or an unreachable target, or it refused the swap) - doubling for the same target, at most 3 min.
        /// </summary>
        public static float TeamUnreachableSeconds = 45f;
        /// <summary>
        /// Round k4 (ScPosture): a MELEE / KITE posture lasts at least this long before it switches to the other (hysteresis on
        /// the verdict, the health line and the overwhelmed count), and never while the hand clock (TeamSwapHoldSeconds) runs.
        /// A target that needs the gun (a cop, an armed enemy, past her leash, out of reach) switches at once.
        /// </summary>
        public static float PostureDwellSeconds = 5f;
        /// <summary>
        /// Round k4: a gun holder in a matched fight stays KITE until her target is within this many meters, then takes her
        /// melee weapon once (and keeps MELEE with the gun still in hand until it is 1 m farther).
        /// </summary>
        public static float PostureMeleeDist = 4f;
        /// <summary>
        /// Round wp1 (the user: "Mia's fighting skill is 4, she still shoots 1 lone zombie"): a clear shooter - Shooting stars at
        /// least this many above her Fighting stars - keeps her gun in a matched fight until her target is within PostureMeleeDist;
        /// everyone else takes her melee weapon at once in a matched fight.
        /// </summary>
        public static int TeamShooterMargin = 2;
        /// <summary>
        /// Round wp1: F for a member with a melee weapon in her pair is also multiplied by 1 + TeamStarBonus per Fighting star above
        /// TeamStarBonusFrom (Fighting 4: 1.45 x 1.5 = 2.18, two walkers matched); members at or below it count as before.
        /// </summary>
        public static float TeamStarBonus = 0.25f;
        public static int TeamStarBonusFrom = 2;
        /// <summary>
        /// Round wp1 (the user: "she approached zombies as if she was going to [swing], until I started attacking"): at home a
        /// resident fights a zombie inside the home zone, on her floor, that is within this many meters of her even when it is
        /// after nobody - round n08 left those alone, even one at her elbow. A possible part of that report, not its proven cause
        /// (round wp1 review, WP1-M1; ScArms' no-swing watchdog logs the facts). 0: n08's rule as it was.
        /// </summary>
        public static float EngageWanderersWithin = 5f;
        /// <summary>
        /// Round k4 (ScInvestigate; the user, 2026-11-15: "If a zombie not engaged with any member or the player is inside the
        /// base, members should investigate"): an awake enemy inside the home zone that is after nobody gets
        /// InvestigateResponders of the nearest free residents within InvestigateRadius (0: anywhere in the zone). The Fable review
        /// of k3+k4: InvestigateEnterMargin (F4) - meters outside the zone an intruder may stand (0: inside only, the user's
        /// "inside the base"); InvestigateHeight (F5) - meters below the lowest and above the highest of the home block and the
        /// residents' posts that count as the base (6: one story either way; a cellar, a mine or a roof beyond them does not).
        /// </summary>
        public static bool InvestigateIntruders = true;
        public static int InvestigateResponders = 2;
        public static float InvestigateRadius = 0f;
        public static float InvestigateEnterMargin = 0f;
        public static float InvestigateHeight = 6f;
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
        /// longer melee one (decision 2). At 230 that holds for the healthSlim and healthNormal zombies only: at the top of
        /// the roll a utility worker weighs 1.09, a fit zombie 1.17 and a strong one 1.76.
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

        /// <summary>
        /// Round k4: keys the posture replaced - MeleeFirst's wait, tm2's no-swing clock and n08's gun-first switch (the one hand
        /// rule draws the gun in KITE and takes the melee weapon in MELEE). Read only to say they are ignored.
        /// </summary>
        private static readonly string[] RetiredTeamKeys = { "TeamMeleeFirstSeconds", "TeamNoSwingConfirmSeconds", "SurvivorGunFirst" };

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
            TeamPlayerFightingSeconds = 5f;
            TeamAllyWeight = 1f;
            TeamGuardWeight = 0.8f;
            TeamMedicalMult = 0.5f;
            TeamStarBase = 0.85f;
            TeamStarStep = 0.15f;
            TeamWeaponMelee = 1f;
            TeamWeaponRanged = 0.8f;
            TeamWeaponBare = 0.5f;
            TeamHealthFloor = 0.5f;
            TeamHealthFullAt = 0.9f;
            TeamLowHealth = 0.6f;
            TeamLowHealthBack = 0.7f;
            TeamSwapHoldSeconds = 5f;
            TeamOverwhelmedExtra = 1;
            TeamUnreachableSeconds = 45f;
            PostureDwellSeconds = 5f;
            PostureMeleeDist = 4f;
            TeamShooterMargin = 2;                 // round wp1
            TeamStarBonus = 0.25f;
            TeamStarBonusFrom = 2;
            EngageWanderersWithin = 5f;
            InvestigateIntruders = true;
            InvestigateResponders = 2;
            InvestigateRadius = 0f;
            InvestigateEnterMargin = 0f;
            InvestigateHeight = 6f;
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
            // the per-world state of the feature: a new world starts with no verdicts, postures or intruders
            ScTeam.Reset();
            ScPosture.Reset();
            ScInvestigate.Reset();
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
            ParseFloat(p, "TeamPlayerFightingSeconds", ref TeamPlayerFightingSeconds, 0f, 60f);
            ParseFloat(p, "TeamAllyWeight", ref TeamAllyWeight, 0f, 20f);
            ParseFloat(p, "TeamGuardWeight", ref TeamGuardWeight, 0f, 20f);
            ParseFloat(p, "TeamMedicalMult", ref TeamMedicalMult, 0f, 1f);
            ParseFloat(p, "TeamStarBase", ref TeamStarBase, 0f, 10f);
            ParseFloat(p, "TeamStarStep", ref TeamStarStep, 0f, 5f);
            ParseFloat(p, "TeamWeaponMelee", ref TeamWeaponMelee, 0f, 5f);
            ParseFloat(p, "TeamWeaponRanged", ref TeamWeaponRanged, 0f, 5f);
            ParseFloat(p, "TeamWeaponBare", ref TeamWeaponBare, 0f, 5f);
            ParseFloat(p, "TeamHealthFloor", ref TeamHealthFloor, 0f, 1f);
            ParseFloat(p, "TeamHealthFullAt", ref TeamHealthFullAt, 0.1f, 1f);
            ParseFloat(p, "TeamLowHealth", ref TeamLowHealth, 0f, 1f);
            ParseFloat(p, "TeamLowHealthBack", ref TeamLowHealthBack, 0f, 1f);
            if (TeamLowHealthBack < TeamLowHealth)
            {
                ScLog.Warn("config TeamLowHealthBack=" + TeamLowHealthBack + " is below TeamLowHealth=" + TeamLowHealth + " - set to TeamLowHealth");
                TeamLowHealthBack = TeamLowHealth;
            }
            ParseFloat(p, "TeamSwapHoldSeconds", ref TeamSwapHoldSeconds, 0f, 60f);
            ParseInt(p, "TeamOverwhelmedExtra", ref TeamOverwhelmedExtra, 0, 10);
            ParseFloat(p, "TeamUnreachableSeconds", ref TeamUnreachableSeconds, 0f, 300f);
            // round k4: the posture's hysteresis, and the investigation of idle intruders
            ParseFloat(p, "PostureDwellSeconds", ref PostureDwellSeconds, 0f, 60f);
            ParseFloat(p, "PostureMeleeDist", ref PostureMeleeDist, 1f, 15f);
            // round wp1: who keeps the gun in a matched fight, Fighting stars in F, and wanderers within reach
            ParseInt(p, "TeamShooterMargin", ref TeamShooterMargin, 0, 7);
            ParseFloat(p, "TeamStarBonus", ref TeamStarBonus, 0f, 2f);
            ParseInt(p, "TeamStarBonusFrom", ref TeamStarBonusFrom, 0, 7);
            ParseFloat(p, "EngageWanderersWithin", ref EngageWanderersWithin, 0f, 15f);
            ParseBoolKey(p, "InvestigateIntruders", ref InvestigateIntruders);
            ParseInt(p, "InvestigateResponders", ref InvestigateResponders, 1, 10);
            ParseFloat(p, "InvestigateRadius", ref InvestigateRadius, 0f, 500f);
            ParseFloat(p, "InvestigateEnterMargin", ref InvestigateEnterMargin, 0f, 20f);
            ParseFloat(p, "InvestigateHeight", ref InvestigateHeight, 1f, 64f);
            // round k4 retired three keys (the posture replaced them); a config that still sets one is told so once
            foreach (string gone in RetiredTeamKeys)
                if (p.Values.ContainsKey(gone)) ScLog.Warn("config " + gone + " is no longer used (round k4: one posture per member replaced it) - ignored");
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
            if (!TeamMelee) return "off (TeamMelee=false: no team verdict - never the MELEE posture, every fight with a gun is KITE)";
            return "on, within " + f(TeamRadius) + " m (a live base attack: the defended area), matched at F >= " + f(TeamMargin) + " T, left after "
                   + TeamLeaveRecounts + " recounts below " + f(TeamLeaveMargin) + " T (matched again after as many at " + f(TeamMargin) + " T), recount " + f(TeamRecountSeconds) + " s (fresh " + f(TeamFreshSeconds) + " s)"
                   + "; F: member (" + f(TeamStarBase) + " + " + f(TeamStarStep) + "/star) x HpF x weapon " + f(TeamWeaponMelee) + "/" + f(TeamWeaponRanged) + "/" + f(TeamWeaponBare)
                   + ", player " + f(TeamPlayerWeight) + " while fighting (his hit on an enemy in the area in the last " + f(TeamPlayerFightingSeconds) + " s, a kill too; else 0), ally " + f(TeamAllyWeight) + ", guard " + f(TeamGuardWeight) + ", medical x" + f(TeamMedicalMult)
                   + ", HpF " + f(TeamHealthFloor) + "+" + f(1f - TeamHealthFloor) + "xmin(1,hp/" + f(TeamHealthFullAt) + ")"
                   + "; T: tags " + TeamTableText(TeamThreats()) + " (else " + f(TeamThreatDefault) + ") x " + TeamTableText(TeamTagMults())
                   + ", hp (max/" + f(TeamThreatHpBase) + ")^" + f(TeamThreatHpPower) + " in " + f(TeamThreatHpMin) + ".." + f(TeamThreatHpMax)
                   + ", then x " + TeamTableText(TeamMults()) + "; gun on " + (string.IsNullOrEmpty(TeamGunTags) ? "-" : TeamGunTags) + " + EntityZombieCop"
                   + "; low health " + f(TeamLowHealth) + " (back " + f(TeamLowHealthBack) + "), overwhelmed +" + TeamOverwhelmedExtra
                   + ", unreachable or refused swing kept " + f(TeamUnreachableSeconds) + " s (doubling, at most 180)";
        }

        /// <summary>Round k4: the posture keys in one line (the load log).</summary>
        private static string PostureLine()
        {
            Func<float, string> f = x => x.ToString("0.##", CultureInfo.InvariantCulture);
            return "one per member (MELEE, KITE, HOLD, SHELTER, INVESTIGATE): MELEE needs a matched fight (F >= " + f(TeamMargin) + " T), " + f(TeamLowHealth)
                   + " health and a target she can swing at" + (TeamMelee ? "" : " - never with TeamMelee false (every fight with a gun is KITE)")
                   + "; one hand change per " + f(TeamSwapHoldSeconds) + " s (TeamSwapHoldSeconds), a MELEE / KITE switch lasts " + f(PostureDwellSeconds)
                   + " s (PostureDwellSeconds); a gun holder in a matched fight keeps her gun until her target is within " + f(PostureMeleeDist)
                   + " m; investigate idle intruders: " + (InvestigateIntruders ? InvestigateResponders + " responder(s) each, "
                   + (InvestigateRadius > 0f ? "within " + f(InvestigateRadius) + " m" : "anywhere in the zone") + ", entered "
                   + (InvestigateEnterMargin > 0f ? "within " + f(InvestigateEnterMargin) + " m of the zone" : "inside the zone") + ", " + f(InvestigateHeight)
                   + " m above and below the base's levels" : "off (InvestigateIntruders false: n08's rule, wanderers left alone)");
        }
    }
}
