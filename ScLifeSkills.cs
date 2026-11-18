#if SC_V34
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round m1t (T2, 2026-11-17, ROADMAP.md section 3 item 3; the user's list): the seven life skills (Specialty values 7-13)
    /// and what they do. Still one life skill per survivor, with stars as before; nobody learns one later yet (the user's
    /// decision: that waits for the gym and range). Existing members keep theirs; new recruits roll by SpecialtyWeights.
    ///   Construction        0-XNPCCore's repair tier from her stars (ConstructionRepairTiers: cobblestone from 1 star,
    ///                       concrete from 3, steel from 5) on a Base repair duty, and specialty XP from the repair XP she earns
    ///                       (Apply, every relink second). The wall's build speed waits for #14 (WallBuildMult, inert).
    ///   Logistics           an opened bundle holds 0.75 to 0.85 of its packing value, by her stars (ScBundleOpen.ShareNow).
    ///   Bartering           a radio or ally barter costs one bundle less, two from 5 stars (never better than one for one).
    ///   Recon               the radio's scouting marks ReconScoutCount (5) sites instead of RadioScoutCount (3).
    ///   Fitness             the gym's bonus: waits for #8 (inert).
    ///   Firearms Instructor the range's bonus: waits for #8 (inert).
    ///   Entertainment       the lounge's morale waits for #8; until then a round of drinks lifts morale one more per two of
    ///                       her stars (rounded up: +1 at 1-2, +4 at 7).
    /// "Her" is the best of that life skill at home (ScSkills.BestSpecialist: a resident, not out on a run). Host only.
    /// </summary>
    public static class ScLifeSkills
    {
        /// <summary>0-XNPCCore's repair XP cvar (AvatarSdcsNpcController.SdcsRepairXpCVar).</summary>
        public const string RepairXpCvar = "SdcsRepairXp";

        /// <summary>Construction: the repair XP last seen on each member's body, and the specialty XP still to pay (fractions).</summary>
        private static readonly Dictionary<int, float> seenRepairXp = new Dictionary<int, float>();
        private static readonly Dictionary<int, float> repairXpCarry = new Dictionary<int, float>();
        private static float lastSave;
        private static bool saveDue;
        private static int floorsRaised, repairXpPaid, bundlesCounted, bartersHelped, scoutsHelped, roundsHelped;

        public static void Reset()
        {
            seenRepairXp.Clear();
            repairXpCarry.Clear();
            lastSave = 0f;
            saveDue = false;
            floorsRaised = repairXpPaid = bundlesCounted = bartersHelped = scoutsHelped = roundsHelped = 0;
            bound = false;
            mNextXp = null;
            pEnabled = null;
        }

        // ------------------------------------------------------------------ the roll ----

        /// <summary>
        /// A new recruit's life skill (ScSkills.RollStart): by SpecialtyWeights; a squeamish one never rolls Medicine. Uniform over
        /// every specialty if the weights are all 0 (the config not read yet).
        /// </summary>
        public static Specialty Roll(Member m, GameRandom rnd)
        {
            bool medic = ScTraitEffects.CanBeMedic(m);
            float[] w = ScConfig.SpecialtyWeights;
            float total = 0f;
            for (int i = 1; i < w.Length; i++) if (Allowed((Specialty)i, medic)) total += w[i];
            float r = ScTraitEffects.Rand01(rnd);
            if (total <= 0f)
            {
                var any = new List<Specialty>();
                for (int i = 1; i < w.Length; i++) if (Allowed((Specialty)i, medic)) any.Add((Specialty)i);
                return any.Count > 0 ? any[Math.Min(any.Count - 1, (int)(r * any.Count))] : Specialty.None;
            }
            float pick = r * total;
            Specialty last = Specialty.None;
            for (int i = 1; i < w.Length; i++)
            {
                if (!Allowed((Specialty)i, medic) || w[i] <= 0f) continue;
                last = (Specialty)i;
                pick -= w[i];
                if (pick < 0f) return last;
            }
            return last;
        }

        private static bool Allowed(Specialty s, bool medic) { return medic || s != Specialty.Medicine; }

        /// <summary>A specialty's roll chance in percent (sc spec).</summary>
        public static float ChancePercent(Specialty s)
        {
            float[] w = ScConfig.SpecialtyWeights;
            float total = 0f;
            for (int i = 1; i < w.Length; i++) total += w[i];
            int k = (int)s;
            return total > 0f && k > 0 && k < w.Length ? 100f * w[k] / total : 0f;
        }

        // ------------------------------------------------------------------ the card ----

        /// <summary>The later features a life skill's effects wait for (all of them, or the clause its description ends with).</summary>
        public static string Needs(Specialty s)
        {
            switch (s)
            {
                case Specialty.Construction: return ScFeatures.Wall;
                case Specialty.Fitness: return ScFeatures.Gym;
                case Specialty.FirearmsInstructor: return ScFeatures.Range;
                case Specialty.Entertainment: return ScFeatures.Lounge;
                default: return null;
            }
        }

        public static bool Pending(Specialty s)
        {
            string need = Needs(s);
            return need != null && !ScFeatures.Has(need);
        }

        /// <summary>A life skill's name ("scSpec&lt;Name&gt;"), the enum name when the text is missing; "" for None.</summary>
        public static string Name(Specialty s)
        {
            string key = ScNames.LocKey(s);
            string t = Localization.Get(key);
            return string.IsNullOrEmpty(t) || t == key ? s.ToString() : t;
        }

        /// <summary>
        /// What a life skill does ("scSpec&lt;Name&gt;Desc"), "(coming later)" at the end while its last clause waits for a later
        /// feature; "" for None or with no text. The community window's tooltip on the specialty row.
        /// </summary>
        public static string Describe(Specialty s)
        {
            if (s == Specialty.None) return "";
            string key = "scSpec" + s + "Desc";
            string t = Localization.Get(key);
            if (string.IsNullOrEmpty(t) || t == key) return "";
            return Pending(s) ? t + " " + ScFeatures.ComingLater() : t;
        }

        // ------------------------------------------------------------------ Logistics ----

        /// <summary>
        /// ScBundleOpen.ShareNow: the share before the craftsmen's cap - BundleOpenValueShare, raised toward LogisticsShareTop
        /// (never above BundleOpenShareMax) a seventh of the way per star of the best Logistics member at home.
        /// </summary>
        public static float BundleShare(float baseShare)
        {
            Member who;
            int stars = ScSkills.BestSpecialist(Specialty.Logistics, out who);
            if (stars <= 0) return baseShare;
            float top = Math.Min(ScConfig.BundleOpenShareMax, ScConfig.LogisticsShareTop);
            if (top <= baseShare) return baseShare;
            return baseShare + (top - baseShare) * Math.Min(Member.MaxStars, stars) / (float)Member.MaxStars;
        }

        /// <summary>A bundle was opened: the best Logistics member at home learns a little (LogisticsXpPerBundle).</summary>
        public static void OnBundleOpened()
        {
            if (!ScCommunity.IsHost || !ScRegistry.Loaded || ScConfig.LogisticsXpPerBundle <= 0f) return;
            Member who;
            if (ScSkills.BestSpecialist(Specialty.Logistics, out who) <= 0 || who == null) return;
            bundlesCounted++;
            Pay(who, ScConfig.LogisticsXpPerBundle);
        }

        // ------------------------------------------------------------------ Bartering (and Haggler) ----

        /// <summary>The bundles fewer a barter costs with the best Bartering member at home (BarteringCutByStars), and who.</summary>
        public static int BarterCut(out Member who)
        {
            int stars = ScSkills.BestSpecialist(Specialty.Bartering, out who);
            if (stars <= 0) { who = null; return 0; }
            return Math.Max(0, ScConfig.BarteringCutByStars[Math.Max(1, Math.Min(Member.MaxStars, stars)) - 1]);
        }

        /// <summary>
        /// One barter's terms: give and get from the base terms, less Bartering's cut, plus Haggler's bundles (the radio only);
        /// the bonuses never make it better than one for one (nor worse than the base terms).
        /// </summary>
        public struct Terms
        {
            public int Give, Get;
            public Member Barterer, Haggler;
            public bool Helped { get { return Barterer != null || Haggler != null; } }
        }

        public static Terms RadioBarter() { return Barter(ScConfig.RadioBarterRate, 1, true); }

        public static Terms AllyBarter() { return Barter(ScConfig.EnclaveBarterGive, ScConfig.EnclaveBarterGet, false); }

        private static Terms Barter(int baseGive, int baseGet, bool radio)
        {
            var t = new Terms { Give = Math.Max(1, baseGive), Get = Math.Max(1, baseGet) };
            if (!ScRegistry.Loaded) return t;
            Member who;
            int cut = BarterCut(out who);
            int give = Math.Max(1, t.Give - cut);
            Member haggler = null;
            int extra = radio ? ScTraitEffects.BestRadioBarterExtra(out haggler) : 0;
            int get = t.Get + extra;
            if (get > give) get = Math.Max(t.Get, give);       // never better than one for one
            if (give < t.Give) t.Barterer = who;
            if (get > t.Get) t.Haggler = haggler;
            t.Give = give;
            t.Get = get;
            return t;
        }

        /// <summary>A barter was made on these terms: Bartering's specialist learns (LifeSkillUseXp) when her cut counted.</summary>
        public static void OnBarter(Terms t)
        {
            if (t.Barterer == null) return;
            bartersHelped++;
            Pay(t.Barterer, ScConfig.LifeSkillUseXp);
        }

        /// <summary>" Mara drove the price down. Jo haggled." - who improved a barter, for the message; "" when nobody did.</summary>
        public static string HelpText(Terms t)
        {
            string s = "";
            if (t.Barterer != null) s += " " + string.Format(Localization.Get("scBarterHelpBarterer"), t.Barterer.Name);
            if (t.Haggler != null) s += " " + string.Format(Localization.Get("scBarterHelpHaggler"), t.Haggler.Name);
            return s;
        }

        // ------------------------------------------------------------------ Recon ----

        /// <summary>How many sites the radio's scouting marks: ReconScoutCount with a Recon member at home, else RadioScoutCount.</summary>
        public static int ScoutCount(out Member who)
        {
            int baseCount = Math.Max(1, ScConfig.RadioScoutCount);
            if (ScSkills.BestSpecialist(Specialty.Recon, out who) <= 0 || who == null) { who = null; return baseCount; }
            int n = Math.Max(baseCount, ScConfig.ReconScoutCount);
            if (n == baseCount) who = null;
            return n;
        }

        public static void OnScouted(Member who)
        {
            if (who == null) return;
            scoutsHelped++;
            Pay(who, ScConfig.LifeSkillUseXp);
        }

        // ------------------------------------------------------------------ Entertainment ----

        /// <summary>A round of drinks' extra morale: EntertainmentDrinksPerStar per star of the best entertainer at home, rounded up.</summary>
        public static int DrinksBonus(out Member who)
        {
            int stars = ScSkills.BestSpecialist(Specialty.Entertainment, out who);
            if (stars <= 0 || ScConfig.EntertainmentDrinksPerStar <= 0f) { who = null; return 0; }
            return Mathf.CeilToInt(stars * ScConfig.EntertainmentDrinksPerStar - 0.0001f);
        }

        public static int DrinksBonus() { Member who; return DrinksBonus(out who); }

        /// <summary>A round of drinks was poured: the entertainer at home learns (LifeSkillUseXp).</summary>
        public static void OnDrinksPoured()
        {
            Member who;
            if (DrinksBonus(out who) <= 0 || who == null) return;
            roundsHelped++;
            Pay(who, ScConfig.LifeSkillUseXp);
        }

        /// <summary>#8's lounge (inert now): its morale x this with an entertainer at home.</summary>
        public static float LoungeMoraleMult() { return 1f; }

        // ------------------------------------------------------------------ Fitness, Firearms Instructor, the wall (inert) ----

        /// <summary>#8's gym (inert now): the Fitness specialist's bonus to what training there gives.</summary>
        public static float GymBonus() { return 0f; }

        /// <summary>#8's range (inert now): the Firearms Instructor's bonus to what training there gives.</summary>
        public static float RangeBonus() { return 0f; }

        /// <summary>#14's wall (inert now): how much faster the builders work with Construction members on it.</summary>
        public static float WallBuildMult() { return 1f; }

        // ------------------------------------------------------------------ Construction ----

        private static bool bound;
        private static MethodInfo mNextXp;
        private static PropertyInfo pEnabled;

        private static void Bind()
        {
            if (bound) return;
            bound = true;
            try
            {
                mNextXp = AccessTools.Method(typeof(AvatarSdcsNpcController), "SdcsRepairNextXp", new[] { typeof(int) });
                pEnabled = AccessTools.Property(typeof(AvatarSdcsNpcController), "RepairSkillEnabled");
            }
            catch (Exception e) { ScLog.Once("lifeskills-bind", e); }
            if (mNextXp == null || pEnabled == null)
                ScLog.Warn("Construction: 0-XNPCCore's repair skill (SdcsRepairNextXp / RepairSkillEnabled) was not found - its repair tiers are left alone");
        }

        /// <summary>0-XNPCCore's repair XP that reaches this tier (0 wood ... 3 steel); -1 when unknown.</summary>
        public static int TierXp(int tier)
        {
            if (tier <= 0) return 0;
            Bind();
            if (mNextXp == null) return -1;
            try { return (int)mNextXp.Invoke(null, new object[] { Math.Min(3, tier) - 1 }); }
            catch (Exception e) { ScLog.Once("lifeskills-tierxp", e); return -1; }
        }

        /// <summary>Is 0-XNPCCore's repair skill on (RepairSkill/Enabled)? Off: every keeper repairs everything, nothing to raise.</summary>
        public static bool RepairSkillOn()
        {
            Bind();
            if (pEnabled == null) return false;
            try { return (bool)pEnabled.GetValue(null, null); }
            catch (Exception e) { ScLog.Once("lifeskills-enabled", e); return false; }
        }

        /// <summary>The repair tier her Construction stars give (0 for anyone else).</summary>
        public static int RepairTier(Member m)
        {
            if (m == null || m.Specialty != Specialty.Construction) return 0;
            return Math.Max(0, Math.Min(3, ScConfig.ConstructionRepairTiers[Math.Max(1, Math.Min(Member.MaxStars, m.SpecLevel)) - 1]));
        }

        /// <summary>
        /// ScSkills.StampCvars, every relink second, a member's own body: Construction's specialty XP from the repair XP
        /// 0-XNPCCore paid her since the last look (ConstructionXpPerRepairXp; the first look of a session only notes it), then
        /// her repair XP raised to her stars' tier. Never lowered; anyone else's left alone.
        /// </summary>
        public static void Apply(Member m, EntityAlive npc)
        {
            if (m == null || npc == null || npc.Buffs == null) return;
            if (!ReferenceEquals(ScRegistry.GetMember(npc.entityId), m)) return;
            try
            {
                float cur = npc.Buffs.GetCustomVar(RepairXpCvar);
                float last;
                bool seen = seenRepairXp.TryGetValue(npc.entityId, out last);
                // noted BEFORE any pay: a star's StampCvars runs this again, which must find nothing new to pay
                seenRepairXp[npc.entityId] = cur;
                int whole = 0;
                if (m.Specialty == Specialty.Construction && seen && cur > last + 0.01f && ScConfig.ConstructionXpPerRepairXp > 0f)
                {
                    float carry;
                    repairXpCarry.TryGetValue(npc.entityId, out carry);
                    carry += (cur - last) * ScConfig.ConstructionXpPerRepairXp;
                    whole = (int)Math.Floor(carry);
                    repairXpCarry[npc.entityId] = carry - whole;
                }
                int tier = RepairTier(m);
                if (tier > 0 && RepairSkillOn())
                {
                    int floor = TierXp(tier);
                    if (floor > 0 && cur < floor)
                    {
                        npc.Buffs.SetCustomVar(RepairXpCvar, floor);
                        floorsRaised++;
                        ScLog.Out(m.Name + ": Construction " + m.SpecLevel + " star(s) - repairs up to " + TierName(tier) + " (0-XNPCCore repair XP "
                                  + cur.ToString("0", CultureInfo.InvariantCulture) + " -> " + floor + ")");
                        seenRepairXp[npc.entityId] = floor;      // SC's own raise is not XP she earned
                    }
                }
                if (whole > 0)
                {
                    repairXpPaid += whole;
                    Pay(m, whole);
                }
            }
            catch (Exception e) { ScLog.Once("lifeskills-apply", e); }
        }

        public static string TierName(int tier)
        {
            switch (Math.Max(0, Math.Min(3, tier)))
            {
                case 1: return "cobblestone";
                case 2: return "concrete";
                case 3: return "steel";
                default: return "wood";
            }
        }

        // ------------------------------------------------------------------ XP ----

        /// <summary>Specialty XP for using a life skill; a star is saved at once, anything else with the next save (at most 30 s).</summary>
        private static void Pay(Member m, float xp)
        {
            if (m == null || xp <= 0f) return;
            bool star = ScSkills.AddSpecialtyXp(m, xp);
            if (star)
            {
                EntityAlive npc = ScCommunity.FindNpc(m.Id);
                if (npc != null) ScSkills.StampCvars(m, npc);
                ScRegistry.SaveAtomic();
                lastSave = Time.time;
                saveDue = false;
                return;
            }
            saveDue = true;
            if (Time.time - lastSave >= 30f)
            {
                ScRegistry.SaveAtomic();
                lastSave = Time.time;
                saveDue = false;
            }
        }

        /// <summary>sc spec: the counters.</summary>
        public static string Report()
        {
            return "Construction tiers raised " + floorsRaised + ", Construction XP from repairs " + repairXpPaid + ", bundles counted for Logistics " + bundlesCounted
                   + ", barters helped " + bartersHelped + ", scoutings helped " + scoutsHelped + ", rounds of drinks helped " + roundsHelped
                   + (saveDue ? " (XP waiting for the next save)" : "");
        }

        /// <summary>sc spec &lt;member&gt;: what her life skill does now.</summary>
        public static string Line(Member m)
        {
            if (m == null) return "";
            string now;
            Member who;
            switch (m.Specialty)
            {
                case Specialty.Construction:
                {
                    EntityAlive npc = ScCommunity.FindNpc(m.Id);
                    now = "repair tier " + TierName(RepairTier(m)) + " (XP " + TierXp(RepairTier(m)) + ")" + (RepairSkillOn() ? "" : " - 0-XNPCCore's repair skill is OFF")
                          + (npc != null ? ", her 0-XNPCCore repair XP " + npc.Buffs.GetCustomVar(RepairXpCvar).ToString("0", CultureInfo.InvariantCulture) : ", not loaded");
                    break;
                }
                case Specialty.Logistics:
                    now = "bundle share now " + ScBundleOpen.ShareNow().ToString("0.###", CultureInfo.InvariantCulture) + " (base " + ScConfig.BundleOpenValueShare.ToString("0.###", CultureInfo.InvariantCulture) + ")";
                    break;
                case Specialty.Bartering:
                {
                    Terms r = RadioBarter(), a = AllyBarter();
                    now = "radio barter " + r.Give + " for " + r.Get + ", ally barter " + a.Give + " for " + a.Get;
                    break;
                }
                case Specialty.Recon:
                    now = "scouting marks " + ScoutCount(out who);
                    break;
                case Specialty.Entertainment:
                    now = "a round of drinks +" + DrinksBonus(out who) + " morale";
                    break;
                default:
                    now = Pending(m.Specialty) ? "waits for " + Needs(m.Specialty) : "";
                    break;
            }
            Member best;
            int bestStars = ScSkills.BestSpecialist(m.Specialty, out best);
            return m.Name + ": " + m.Specialty + " " + m.SpecLevel + " star(s), XP " + m.SpecXp + (best != null && best != m ? " (" + best.Name + " is the best at home, " + bestStars + ")" : "")
                   + (now.Length > 0 ? " - " + now : "") + " - " + Describe(m.Specialty);
        }
    }
}
#endif
