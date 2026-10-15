using System;

namespace SurvivorCommunity
{
    /// <summary>
    /// The person behind a survivor who is not (yet) in the community: traits, skills, specialty and mood, rolled when they
    /// spawn on SC's own survivor bodies (ScShooting.OnPostInit from their PostInit, round sh: their Shooting stars aim their
    /// gun from the start), else - another mod's survivors, and an older body that has none - the first time the player
    /// talks to them (the dialog's snapshot panel, XUiC_ScSnapshot), and kept when they join (the user's choice, 2026-10-07) - ScCommunity.Join takes them instead of
    /// rolling new ones. Members keep their own record in the registry; this is only for everyone else.
    ///
    /// Stored as cvars on the survivor, so it lives and dies with the body (EntityAliveSDXV4.Write saves every non-zero
    /// cvar) and needs no registry record: %sc_pf (rolled), %sc_pf_t1..t5 (trait codes, TraitCode), %sc_pf_s0..s3 (skill
    /// stars), %sc_pf_spec / %sc_pf_specl (specialty and its stars), %sc_pf_mood (mood + 1, as a 0 is never saved),
    /// %sc_pf_known (a former member: their traits show by name). A member who leaves takes their record as it is then
    /// (Store). The body is only kept while something pins it (a favour asked about, a grudge, membership): a survivor
    /// the player only talked to goes with the chunk like any wild survivor, profile and all.
    /// The '%' makes them reach clients. Rolled on the host only.
    /// </summary>
    public static class ScProfile
    {
        private const string Rolled = "%sc_pf";
        private const string TraitPrefix = "%sc_pf_t";
        private const string SkillPrefix = "%sc_pf_s";
        private const string SpecVar = "%sc_pf_spec";
        private const string SpecLevelVar = "%sc_pf_specl";
        private const string MoodVar = "%sc_pf_mood";
        private const string KnownVar = "%sc_pf_known";
        private const int MaxTraits = 5;
        private static readonly string[] traitVars = { TraitPrefix + 1, TraitPrefix + 2, TraitPrefix + 3, TraitPrefix + 4, TraitPrefix + 5 };
        private static readonly string[] skillVars = { SkillPrefix + 0, SkillPrefix + 1, SkillPrefix + 2, SkillPrefix + 3 };

        public static bool Has(EntityAlive npc)
        {
            return npc != null && npc.Buffs != null && npc.Buffs.HasCustomVar(Rolled);
        }

        /// <summary>A skill's stars in this survivor's profile (1..7), or 0 when no profile was rolled.</summary>
        public static int SkillStars(EntityAlive npc, Skill s)
        {
            if (!Has(npc)) return 0;
            return Math.Max(1, Math.Min(Member.MaxStars, (int)npc.Buffs.GetCustomVar(skillVars[(int)s])));
        }

        /// <summary>Host: roll this survivor's profile unless they have one (or are not a survivor trust applies to).</summary>
        public static void Ensure(EntityAlive npc)
        {
            if (!ScCommunity.IsHost || Has(npc) || !ScTrust.Subject(npc)) return;
            try
            {
                ScConfig.EnsureParsed();
                World w = ScCommunity.World;
                GameRandom rnd = npc.rand ?? (w != null ? w.GetGameRandom() : null);
                if (rnd == null) return;
                var m = new Member { Id = npc.entityId, Class = ScCommunity.ClassName(npc) };
                m.Traits.AddRange(ScTraits.Roll(ScConfig.TraitsPerRecruit, rnd));
                ScSkills.RollStart(m, rnd, ScSkills.RecruitTier(npc));   // T2/T3 bodies start higher (round t3)
                // their own mood: a middling start, nudged by their traits (Optimist, Pessimist...)
                m.Mood = Math.Max(5, Math.Min(95, rnd.RandomRange(35, 71) + ScTraits.MoodOffset(m)));
                Write(npc.Buffs, m);
                ScLog.Verbose(ScCommunity.DisplayName(npc) + " (" + npc.entityId + "): profile rolled - traits [" + string.Join(",", m.Traits.ToArray())
                              + "] spec " + m.Specialty + " mood " + m.Mood);
            }
            catch (Exception e)
            {
                ScLog.Once("profile-roll", e);
            }
        }

        /// <summary>
        /// Host: a member leaving (ScCommunity.Leave) keeps their record as their profile - trained stars, current mood -
        /// so their card, and a later re-join, show who they are now rather than their first roll.
        /// </summary>
        public static void Store(Member m, EntityAlive npc)
        {
            if (!ScCommunity.IsHost || m == null || npc == null || npc.Buffs == null) return;
            try
            {
                Write(npc.Buffs, m);
                npc.Buffs.SetCustomVar(KnownVar, 1f);
            }
            catch (Exception e)
            {
                ScLog.Once("profile-store", e);
            }
        }

        /// <summary>A former member: the snapshot shows their traits by name.</summary>
        public static bool Known(EntityAlive npc)
        {
            return npc != null && npc.Buffs != null && npc.Buffs.GetCustomVar(KnownVar) > 0f;
        }

        private static void Write(EntityBuffs b, Member m)
        {
            // every slot written, an unused one as 0 (read back as no trait): a shorter trait list replaces a longer one
            for (int i = 0; i < MaxTraits; i++) b.SetCustomVar(traitVars[i], i < m.Traits.Count ? TraitCode(m.Traits[i]) : 0f);
            for (int i = 0; i < Member.SkillCount; i++) b.SetCustomVar(skillVars[i], Math.Max(1, m.Skills[i].Level));
            b.SetCustomVar(SpecVar, (int)m.Specialty);
            b.SetCustomVar(SpecLevelVar, Math.Max(1, m.SpecLevel));
            b.SetCustomVar(MoodVar, Math.Max(0, Math.Min(100, m.Mood)) + 1);
            b.SetCustomVar(Rolled, 1f);
        }

        /// <summary>
        /// The stored profile as a Member (Traits, Skills, Specialty, SpecLevel, Mood filled in; nothing else), or null.
        /// A trait code that matches no trait in the current table (a renamed or removed trait) is skipped.
        /// </summary>
        public static Member Read(EntityAlive npc)
        {
            if (!Has(npc)) return null;
            EntityBuffs b = npc.Buffs;
            var m = new Member { Id = npc.entityId };
            for (int i = 0; i < MaxTraits; i++)
            {
                if (!b.HasCustomVar(traitVars[i])) continue;
                string name = TraitFor((int)b.GetCustomVar(traitVars[i]));
                if (name != null && !m.Traits.Contains(name)) m.Traits.Add(name);
            }
            for (int i = 0; i < Member.SkillCount; i++)
            {
                int level = (int)b.GetCustomVar(skillVars[i]);
                m.Skills[i].Level = Math.Max(1, Math.Min(Member.MaxStars, level));
                m.Skills[i].Xp = ScSkills.NeededFor(m.Skills[i].Level);
            }
            int spec = (int)b.GetCustomVar(SpecVar);
            m.Specialty = Enum.IsDefined(typeof(Specialty), spec) ? (Specialty)spec : Specialty.None;
            m.SpecLevel = Math.Max(1, Math.Min(Member.MaxStars, (int)b.GetCustomVar(SpecLevelVar)));
            m.Mood = Math.Max(0, Math.Min(100, (int)b.GetCustomVar(MoodVar) - 1));
            return m;
        }

        /// <summary>Join: the new member takes the profile the player was shown. False when there is none (roll as before).</summary>
        public static bool ApplyOnJoin(Member m, EntityAlive npc)
        {
            Member p = m != null ? Read(npc) : null;
            if (p == null) return false;
            m.Traits.Clear();
            m.Traits.AddRange(p.Traits);
            for (int i = 0; i < Member.SkillCount; i++)
            {
                m.Skills[i].Level = p.Skills[i].Level;
                m.Skills[i].Xp = p.Skills[i].Xp;
            }
            m.Specialty = p.Specialty;
            m.SpecLevel = p.SpecLevel;
            m.SpecXp = 0;
            m.Mood = p.Mood;
            return true;
        }

        /// <summary>
        /// A trait's code: a stable hash of its name kept below 2^23, so the float cvar holds it exactly and a reordered
        /// trait table still finds it.
        /// </summary>
        public static int TraitCode(string name)
        {
            unchecked
            {
                int h = 5381;
                string s = (name ?? "").ToLowerInvariant();
                for (int i = 0; i < s.Length; i++) h = h * 33 + s[i];
                return (h & 0x7FFFFF) | 1;   // never 0: a cvar of 0 is not saved
            }
        }

        private static string TraitFor(int code)
        {
            for (int i = 0; i < ScConfig.Traits.Count; i++)
                if (TraitCode(ScConfig.Traits[i].Name) == code) return ScConfig.Traits[i].Name;
            return null;
        }
    }
}
