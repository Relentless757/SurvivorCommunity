using System;
using System.Collections.Generic;
using System.Globalization;

namespace SurvivorCommunity
{
    /// <summary>
    /// C1 (round 22, 3.4 build only): the config keys of community crafting - members craft through 0-XNPCCore 3.4.02's
    /// crafting with their specialty stars as their skills (ScCraft), residents on a work shift (ScWork). Kept apart from
    /// ScConfig.cs's long lists, as ScConfigVoices. Config/blocks.xml, block ConfigSurvivorCommunity, after
    /// VoiceEnclaveContext; each key is commented there. ResetCraft also clears ScCraft's, ScWork's and Patches_Craft's
    /// per-world state (ScConfig.Reset runs at GameStarting).
    /// </summary>
    public static partial class ScConfig
    {
        /// <summary>Members' crafting skills come from their specialty stars, kept in an SC-owned Progression (false: the old null Progression, no crafting skills, no craft XP).</summary>
        public static bool CraftSkillsEnabled = true;
        /// <summary>Per specialty (Specialty - 1): the game's crafting skills (progression.xml names) its stars stand for.</summary>
        public static readonly List<string>[] CraftSkills = NewLists(ScCraftMath.DefaultSkills);
        /// <summary>Per specialty (Specialty - 1): the 0-XNPCCore roles (cook, blacksmith, chemist, crafter) a member starts with.</summary>
        public static readonly List<string>[] CraftRoles = NewLists(ScCraftMath.DefaultRoles);
        /// <summary>level = round(max level x (stars / 7) ^ CraftLevelCurve); 1 = proportional.</summary>
        public static float CraftLevelCurve = 1f;
        /// <summary>Specialty XP per counted craft (a recipe one of her specialty's skills unlocks or tiers); no daily cap.</summary>
        public static float CraftSpecXpPerCraft = 0.5f;
        /// <summary>
        /// X2c (round 25b, 0-XNPCCore 3.4.03's books): specialty XP a member gets when she reads a crafting magazine of her
        /// specialty, per crafting level the magazine would have granted (the sandbox's Magazine Points, 1 by default; the
        /// level stays her stars'). 0: magazines never help a member (all listed as "won't help her", none used up).
        /// </summary>
        public static float CraftSpecXpPerMagazineLevel = 10f;
        /// <summary>Residents can be put on a work shift (a craft order or a job from her talk - round 28 -, the Community tab's Call to work).</summary>
        public static bool WorkShiftsEnabled = true;
        /// <summary>
        /// Round 28 (X3, the talk link): talking to a resident at home inside the base links her to you (the work shift's Owner
        /// link, ScWork's Talk phase) while the talk or a window opened from it is open, so 0-XNPCCore's rows (Craft..., Jobs and
        /// gear, Show me your inventory) show and work; it ends 2 s after the last of them closes, or becomes a work shift when
        /// she is given a craft or a job. Needs WorkShiftsEnabled. false: no link at talk (no Craft... on residents).
        /// </summary>
        public static bool TalkHold = true;
        /// <summary>After her last job of the shift, real seconds (game time running) before she goes back to her post.</summary>
        public static int WorkIdleSeconds = 90;
        /// <summary>Called to work and never given an order: real seconds before she goes back to her post.</summary>
        public static int WorkCallSeconds = 180;
        /// <summary>
        /// What she makes on a shift goes from her inventory window into the community's home crates (linked storage). Round 29a
        /// (J29, the user's decision 3): what she gathers on a home job (wood, crops, meat ...) too, and a full pack is emptied
        /// into the crates so the job carries on.
        /// </summary>
        public static bool WorkDepositOutput = true;
        /// <summary>
        /// Round 29a (J29, the user's decision 1): the work kit every member carries beside their weapon pair - 0-XNPCCore's own
        /// work tools (its survivors' spawn kit), so its jobs (Chop wood, Butcher, Base repair ...) find a tool. Item names;
        /// empty or "none" = no kit. A better tool handed over through her inventory is used first (0-XNPCCore looks in her
        /// stores before the kit); a kit tool is never her fighting weapon (ScLoadout.IsKitOnly).
        /// </summary>
        public const string DefaultMemberWorkKit = "meleeToolRepairT0StoneAxe,meleeToolShovelT0StoneShovel,meleeWpnBladeT0BoneKnife";
        public static readonly List<string> MemberWorkKit = new List<string>(SplitCraftList(DefaultMemberWorkKit));
        /// <summary>Round 29a (J29): a Base repair / Combat repair duty ends after this many minutes (game time running) with nothing she can repair.</summary>
        public static int RepairDutyIdleMinutes = 5;
        /// <summary>Round 29a (J29, the user's decision 4): a member who picks a ripe crop on the player's farm replants the plot with a seed of that crop.</summary>
        public static bool ReplantCrops = true;
        /// <summary>Bumped by every parse: ScCraft re-resolves its skill lists when it changes.</summary>
        public static int CraftStamp;

        private static List<string>[] NewLists(string[] defaults)
        {
            var a = new List<string>[defaults.Length];
            for (int i = 0; i < defaults.Length; i++) a[i] = new List<string>(SplitCraftList(defaults[i]));
            return a;
        }

        private static IEnumerable<string> SplitCraftList(string s)
        {
            if (string.IsNullOrEmpty(s)) yield break;
            foreach (string part in s.Split(','))
            {
                string t = part.Trim();
                if (t.Length > 0) yield return t;
            }
        }

        private static void ResetCraft()
        {
            CraftSkillsEnabled = true;
            for (int i = 0; i < CraftSkills.Length; i++) { CraftSkills[i].Clear(); CraftSkills[i].AddRange(SplitCraftList(ScCraftMath.DefaultSkills[i])); }
            for (int i = 0; i < CraftRoles.Length; i++) { CraftRoles[i].Clear(); CraftRoles[i].AddRange(SplitCraftList(ScCraftMath.DefaultRoles[i])); }
            CraftLevelCurve = 1f;
            CraftSpecXpPerCraft = 0.5f;
            CraftSpecXpPerMagazineLevel = 10f;
            WorkShiftsEnabled = true;
            TalkHold = true;
            WorkIdleSeconds = 90;
            WorkCallSeconds = 180;
            WorkDepositOutput = true;
            MemberWorkKit.Clear();
            MemberWorkKit.AddRange(SplitCraftList(DefaultMemberWorkKit));
            RepairDutyIdleMinutes = 5;
            ReplantCrops = true;
            CraftStamp++;
            // the per-world state of the feature: a new world starts with none of it
            ScCraft.Reset();
            ScWork.Reset();
            ScBooks.Reset();
            Patches_Craft.Reset();
        }

        private static void ParseCraft(DynamicProperties p)
        {
            string v;
            if (p.Values.TryGetValue("CraftSkillsEnabled", out v) && !string.IsNullOrEmpty(v)) CraftSkillsEnabled = StringParsers.ParseBool(v);
            for (int i = 0; i < ScCraftMath.SpecialtyNames.Length; i++)
            {
                if (p.Values.TryGetValue("CraftSkills" + ScCraftMath.SpecialtyNames[i], out v)) { CraftSkills[i].Clear(); CraftSkills[i].AddRange(SplitCraftList(v)); }
                if (p.Values.TryGetValue("CraftRoles" + ScCraftMath.SpecialtyNames[i], out v))
                {
                    CraftRoles[i].Clear();
                    if (!string.Equals((v ?? "").Trim(), "none", StringComparison.OrdinalIgnoreCase)) CraftRoles[i].AddRange(SplitCraftList(v));   // "none" = no role
                }
            }
            ParseFloat(p, "CraftLevelCurve", ref CraftLevelCurve, 0.25f, 4f);
            ParseFloat(p, "CraftSpecXpPerCraft", ref CraftSpecXpPerCraft, 0f, 10f);
            ParseFloat(p, "CraftSpecXpPerMagazineLevel", ref CraftSpecXpPerMagazineLevel, 0f, 550f);
            if (p.Values.TryGetValue("WorkShiftsEnabled", out v) && !string.IsNullOrEmpty(v)) WorkShiftsEnabled = StringParsers.ParseBool(v);
            if (p.Values.TryGetValue("TalkHold", out v) && !string.IsNullOrEmpty(v)) TalkHold = StringParsers.ParseBool(v);
            ParseInt(p, "WorkIdleSeconds", ref WorkIdleSeconds, 10, 3600);
            ParseInt(p, "WorkCallSeconds", ref WorkCallSeconds, 10, 3600);
            if (p.Values.TryGetValue("WorkDepositOutput", out v) && !string.IsNullOrEmpty(v)) WorkDepositOutput = StringParsers.ParseBool(v);
            // round 29a (J29): the members' work kit ("none" or empty = no kit), the repair duty's idle end, the replanting
            if (p.Values.TryGetValue("MemberWorkKit", out v))
            {
                MemberWorkKit.Clear();
                if (!string.Equals((v ?? "").Trim(), "none", StringComparison.OrdinalIgnoreCase)) MemberWorkKit.AddRange(SplitCraftList(v));
            }
            ParseInt(p, "RepairDutyIdleMinutes", ref RepairDutyIdleMinutes, 1, 240);
            if (p.Values.TryGetValue("ReplantCrops", out v) && !string.IsNullOrEmpty(v)) ReplantCrops = StringParsers.ParseBool(v);
            CraftStamp++;
        }

        /// <summary>The config line's crafting part.</summary>
        private static string CraftLine()
        {
            var parts = new List<string>();
            for (int i = 0; i < ScCraftMath.SpecialtyNames.Length; i++)
                parts.Add(ScCraftMath.SpecialtyNames[i] + "=" + string.Join("+", CraftSkills[i].ToArray()) + "/" + (CraftRoles[i].Count > 0 ? string.Join("+", CraftRoles[i].ToArray()) : "-"));
            return "craftSkills=" + (CraftSkillsEnabled ? "on curve " + CraftLevelCurve.ToString("0.##", CultureInfo.InvariantCulture)
                                                         + " xp " + CraftSpecXpPerCraft.ToString("0.##", CultureInfo.InvariantCulture) + "/craft "
                                                         + CraftSpecXpPerMagazineLevel.ToString("0.##", CultureInfo.InvariantCulture) + "/magazine level [" + string.Join(" ", parts.ToArray()) + "]" : "off")
                   + " workShifts=" + (WorkShiftsEnabled ? "on idle " + WorkIdleSeconds + "s call " + WorkCallSeconds + "s deposit " + WorkDepositOutput : "off")
                   + " talkHold=" + (TalkHold && WorkShiftsEnabled ? "on" : "off")
                   + " workKit=" + (MemberWorkKit.Count > 0 ? string.Join("+", MemberWorkKit.ToArray()) : "none")
                   + " repairDutyIdle=" + RepairDutyIdleMinutes + "min replantCrops=" + ReplantCrops;
        }
    }
}
