#if SC_V34
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round m1t (2026-11-17): the features later roadmap rounds add, which some trait and life-skill effects wait for. Such an
    /// effect is parsed and stored now but does nothing, and its card line ends "(coming later)" (ScTraits.Describe,
    /// ScLifeSkills.Describe) until the round that builds the feature makes Has answer true for it - the one switch to flip.
    /// </summary>
    public static class ScFeatures
    {
        /// <summary>#8 New facilities: Iron Stomach's immunity to latrine sickness.</summary>
        public const string Latrine = "latrine";
        /// <summary>#8: Gun Nut's Shooting XP and Firearms Instructor's bonus at the range.</summary>
        public const string Range = "range";
        /// <summary>#8: Brawler's Fighting XP and Fitness's bonus at the gym.</summary>
        public const string Gym = "gym";
        /// <summary>#8: Entertainment's lounge morale (its drinks boost is live now).</summary>
        public const string Lounge = "lounge";
        /// <summary>#14 Auto-built perimeter wall: Construction's build speed (its repair tiers are live now).</summary>
        public const string Wall = "wall";

        /// <summary>Does the community have this feature yet? None of them exists in round m1t.</summary>
        public static bool Has(string feature)
        {
            switch (feature)
            {
                default: return false;
            }
        }

        /// <summary>Does this trait have an effect that waits for a feature not built yet?</summary>
        public static bool Pending(TraitDef t)
        {
            if (t == null) return false;
            for (int i = 0; i < t.Needs.Count; i++) if (!Has(t.Needs[i])) return true;
            return false;
        }

        /// <summary>"(coming later)", the card's mark for an effect that waits for a later feature.</summary>
        public static string ComingLater()
        {
            string s = Localization.Get("scComingLater");
            return string.IsNullOrEmpty(s) || s == "scComingLater" ? "(coming later)" : s;
        }
    }

    /// <summary>
    /// Round m1t (T1, 2026-11-17, ROADMAP.md section 3 item 3; the user's list): what the twelve new traits do, from the trait
    /// keys ScConfig.ParseTrait reads into TraitDef. Every rule here reads the member's traits; the places that act on them are
    /// marked "round m1t" where they are:
    ///   Night Owl      nightwatch=6   ScMorale.Recompute: her mood +6 while she stands a watch at night (SituationalMood).
    ///   Light Sleeper  wake=1         ScSleep.MemberRefusal: her night lease closes (she wakes) while a horde is on its way
    ///                                 (called, with an arrival time) or fighting the base - not while one is held back.
    ///                  defense=1      ScDefence.Rating: +1 per fit resident with it.
    ///   Scavenger      runbundles=1,runfrom=4  ScRuns.Yield (round ie1): +1 bundle on a supply run's haul of 4 or more
    ///                                 (ScTraits.RunBundles; it was x1.1 with the odd part of a bundle as a chance).
    ///                  haul=1.1       ScMining.Mult: a mining run's items x1.1 (mining is not part of the stockpile).
    ///   Haggler        barter=1       ScLifeSkills.RadioBarter: the radio barter gives 3 for 2 while she is at home.
    ///   Fast Healer    heal=0.5       ScStockpile.DailyConsume and ScDefence.Settle: her injury heals with no Meds bundle half the time.
    ///   Iron Stomach   sickness=0     needs the latrine (#8): stored, inert.
    ///   Bookworm       books=1.5      ScBooks.AfterLevel: the specialty XP a magazine pays.
    ///   Gun Nut        rangexp=1.5    needs the range (#8): stored, inert.
    ///   Brawler        gymxp=1.5      needs the gym (#8): stored, inert.
    ///   Squeamish      nomedic=1      never Medicine (ScSkills.RollStart, sc spec), never a resident medic (ScDowned, BestSpecialist),
    ///                                 never 0-XNPCCore's party medic (ApplyBody clears SdcsMedicRole).
    ///   Clumsy         wear=1.5       ApplyBody stamps sc_toolwear 0.5; buffSCSkills turns it into DegradationPerUse +50% on
    ///                                 items tagged "tool" she swings (chopping, mining, digging). Not 0-XNPCCore's base repair
    ///                                 (it damages blocks directly, the tool does not wear) and not knives (tagged weapon).
    ///   Steady Hands   aim=0.8        ScShooting.Aim / AimLauncher: the aim error x0.8 (SC's own aim error, round sh).
    /// Exclusions (excludes=) live in ScTraits (Roll, LinkExclusions). Host only; reads only, except ApplyBody's two cvars.
    /// </summary>
    public static class ScTraitEffects
    {
        /// <summary>
        /// The cvar buffSCSkills reads for Clumsy: tools wear this much faster (wear - 1). Stamped on every member, 0 included,
        /// like buffSCSkills' other cvars: vanilla's PassiveEffect.ModifyValue adds a referenced cvar that is missing (as 0, sent
        /// to clients) on every tool swing, so removing it here would loop. Only RemoveBuffs / RemoveAllBuffs take it away.
        /// </summary>
        public const string WearCvar = "sc_toolwear";
        /// <summary>0-XNPCCore's party-medic role (Voice Control's order), which a squeamish member never keeps.</summary>
        public const string MedicRoleCvar = "SdcsMedicRole";
        /// <summary>ScSleep's refusal for a light sleeper while a horde is called (compared by reference there).</summary>
        public static readonly string WakeWhy = "a light sleeper: awake for the horde on the base";

        private static readonly HashSet<int> toldMedicRole = new HashSet<int>();
        private static readonly HashSet<int> toldWoken = new HashSet<int>();
        private static int healedUnaided, medicRolesCleared, woken;

        public static void Reset()
        {
            toldMedicRole.Clear();
            toldWoken.Clear();
            healedUnaided = medicRolesCleared = woken = 0;
        }

        // ------------------------------------------------------------------ per member ----

        public static int NightWatchMood(Member m)
        {
            int v = 0;
            if (m == null) return 0;
            for (int i = 0; i < m.Traits.Count; i++) { TraitDef t = ScTraits.Find(m.Traits[i]); if (t != null) v += t.NightWatchMood; }
            return v;
        }

        /// <summary>For round m1f's Watch fatigue (merge note): does she like a night watch (Night Owl)?</summary>
        public static bool LikesNightWatch(Member m) { return NightWatchMood(m) > 0; }

        /// <summary>ScMorale.Recompute: Night Owl's mood while she stands a watch at night; 0 otherwise.</summary>
        public static int SituationalMood(Member m)
        {
            if (m == null || !ScWatch.OnWatch(m)) return 0;
            World w = ScCommunity.World;
            if (w == null || w.IsDaytime()) return 0;
            return NightWatchMood(m);
        }

        public static bool WakesForAttack(Member m)
        {
            if (m == null) return false;
            for (int i = 0; i < m.Traits.Count; i++) { TraitDef t = ScTraits.Find(m.Traits[i]); if (t != null && t.WakesForAttack) return true; }
            return false;
        }

        public static int DefenseBonus(Member m)
        {
            int v = 0;
            if (m == null) return 0;
            for (int i = 0; i < m.Traits.Count; i++) { TraitDef t = ScTraits.Find(m.Traits[i]); if (t != null) v += t.DefenseBonus; }
            return v;
        }

        public static float HaulMult(Member m)
        {
            float f = 1f;
            if (m == null) return f;
            for (int i = 0; i < m.Traits.Count; i++) { TraitDef t = ScTraits.Find(m.Traits[i]); if (t != null) f *= t.HaulMult; }
            return f;
        }

        public static int RadioBarterExtra(Member m)
        {
            int v = 0;
            if (m == null) return 0;
            for (int i = 0; i < m.Traits.Count; i++) { TraitDef t = ScTraits.Find(m.Traits[i]); if (t != null) v += t.RadioBarterExtra; }
            return v;
        }

        /// <summary>The chance her injury heals with no Meds bundle (two such traits: either may do it).</summary>
        public static float HealChance(Member m)
        {
            float miss = 1f;
            if (m == null) return 0f;
            for (int i = 0; i < m.Traits.Count; i++) { TraitDef t = ScTraits.Find(m.Traits[i]); if (t != null) miss *= 1f - t.HealChance; }
            return 1f - miss;
        }

        /// <summary>Iron Stomach - for #8's latrine sickness (inert now): her chance of it x this; 0 = immune.</summary>
        public static float SicknessMult(Member m)
        {
            float f = 1f;
            if (m == null) return f;
            for (int i = 0; i < m.Traits.Count; i++) { TraitDef t = ScTraits.Find(m.Traits[i]); if (t != null) f *= t.SicknessMult; }
            return f;
        }

        public static float BooksMult(Member m)
        {
            float f = 1f;
            if (m == null) return f;
            for (int i = 0; i < m.Traits.Count; i++) { TraitDef t = ScTraits.Find(m.Traits[i]); if (t != null) f *= t.BooksMult; }
            return f;
        }

        /// <summary>Gun Nut - for #8's range (inert now): her Shooting XP there x this.</summary>
        public static float RangeXpMult(Member m)
        {
            float f = 1f;
            if (m == null) return f;
            for (int i = 0; i < m.Traits.Count; i++) { TraitDef t = ScTraits.Find(m.Traits[i]); if (t != null) f *= t.RangeXpMult; }
            return f;
        }

        /// <summary>Brawler - for #8's gym (inert now): her Fighting XP there x this.</summary>
        public static float GymXpMult(Member m)
        {
            float f = 1f;
            if (m == null) return f;
            for (int i = 0; i < m.Traits.Count; i++) { TraitDef t = ScTraits.Find(m.Traits[i]); if (t != null) f *= t.GymXpMult; }
            return f;
        }

        /// <summary>May she take the medic role (the Medicine specialty, a revive, 0-XNPCCore's party medic)? False for Squeamish.</summary>
        public static bool CanBeMedic(Member m)
        {
            if (m == null) return true;
            for (int i = 0; i < m.Traits.Count; i++) { TraitDef t = ScTraits.Find(m.Traits[i]); if (t != null && t.NoMedic) return false; }
            return true;
        }

        public static float WearMult(Member m)
        {
            float f = 1f;
            if (m == null) return f;
            for (int i = 0; i < m.Traits.Count; i++) { TraitDef t = ScTraits.Find(m.Traits[i]); if (t != null) f *= t.WearMult; }
            return f;
        }

        public static float AimMult(Member m)
        {
            float f = 1f;
            if (m == null) return f;
            for (int i = 0; i < m.Traits.Count; i++) { TraitDef t = ScTraits.Find(m.Traits[i]); if (t != null) f *= t.AimMult; }
            return f;
        }

        /// <summary>
        /// ScShooting, per shot: a body's aim-error multiplier - a member's own traits; else the traits any trait buff shows
        /// (stand-ins, enclave and band people wear their record's) or a stranger's profile (ScProfile) holds. 1 for anyone else.
        /// </summary>
        public static float AimMult(EntityAlive e)
        {
            if (e == null) return 1f;
            Member m = ScRegistry.Loaded ? ScRegistry.GetMember(e.entityId) : null;
            if (m != null) return AimMult(m);
            float f = 1f;
            List<TraitDef> all = ScConfig.Traits;
            for (int i = 0; i < all.Count; i++)
            {
                TraitDef t = all[i];
                if (t.AimMult == 1f) continue;
                if ((e.Buffs != null && e.Buffs.HasBuff(ScTraits.BuffName(t.Name))) || ScProfile.HasTrait(e, t.Name)) f *= t.AimMult;
            }
            return f;
        }

        // ------------------------------------------------------------------ community-wide ----

        /// <summary>Is she at home for a community effect (a resident, not out on a run): the same test as ScSkills.BestSpecialist.</summary>
        public static bool AtHome(Member m) { return m != null && m.IsHome && m.Status != MemberStatus.OnRun; }

        /// <summary>ScDefence.Rating: the defense= bonus of every fit resident (not injured), and a label ("Light Sleeper x2").</summary>
        public static int DefenseBonusAll(out string label)
        {
            int sum = 0;
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (Member m in ScRegistry.Members.Values)
            {
                if (m.Status != MemberStatus.Resident || m.Injured) continue;
                for (int i = 0; i < m.Traits.Count; i++)
                {
                    TraitDef t = ScTraits.Find(m.Traits[i]);
                    if (t == null || t.DefenseBonus == 0) continue;
                    sum += t.DefenseBonus;
                    int n;
                    counts.TryGetValue(t.Name, out n);
                    counts[t.Name] = n + 1;
                }
            }
            var sb = new StringBuilder();
            foreach (var kv in counts)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(ScTraits.Display(kv.Key)).Append(" x").Append(kv.Value);
            }
            label = sb.ToString();
            return sum;
        }

        /// <summary>The most extra radio-barter bundles a member at home brings (Haggler), and who.</summary>
        public static int BestRadioBarterExtra(out Member who)
        {
            who = null;
            int best = 0;
            foreach (Member m in ScRegistry.Members.Values)
            {
                if (!AtHome(m)) continue;
                int x = RadioBarterExtra(m);
                if (x > best) { best = x; who = m; }
            }
            return best;
        }

        /// <summary>
        /// Fast Healer: does her injury heal on its own this time (no Meds bundle)? A roll of HealChance. Counted for sc traits.
        /// </summary>
        public static bool HealsUnaided(Member m, GameRandom rnd)
        {
            float p = HealChance(m);
            if (p <= 0f) return false;
            if (Rand01(rnd) >= p) return false;
            healedUnaided++;
            ScLog.Out(m.Name + "'s injury heals on its own (" + (p * 100f).ToString("0", CultureInfo.InvariantCulture) + "% chance) - no Meds bundle used");
            return true;
        }

        // round ie1: Haul (Scavenger's x1.1 with the odd part of a bundle as a chance) is gone - a supply run's trait bonuses are
        // whole bundles (runbundles=, ScTraits.RunBundles); HaulMult above is a mining run's item multiplier only

        // ------------------------------------------------------------------ the body ----

        /// <summary>
        /// ScTraits.ApplyBuffs, every relink second: Clumsy's sc_toolwear cvar (wear - 1, 0 without Clumsy), and a squeamish member's
        /// 0-XNPCCore party-medic role (Voice Control's order, the SdcsMedicRole cvar) taken back, with a word to the player once.
        /// </summary>
        public static void ApplyBody(Member m, EntityAlive npc)
        {
            if (m == null || npc == null || npc.Buffs == null) return;
            float w = WearMult(m) - 1f;
            if (Mathf.Abs(w) < 0.001f) w = 0f;
            // stamped, 0 included (never removed here: vanilla would add it back on the next tool swing, and so on)
            if (!npc.Buffs.HasCustomVar(WearCvar) || npc.Buffs.GetCustomVar(WearCvar) != w) npc.Buffs.SetCustomVar(WearCvar, w);

            if (!CanBeMedic(m) && npc.Buffs.GetCustomVar(MedicRoleCvar) >= 0.5f)
            {
                npc.Buffs.RemoveCustomVar(MedicRoleCvar);
                medicRolesCleared++;
                ScLog.Out(m.Name + " (" + npc.entityId + ") is squeamish - the party-medic role is taken back");
                if (ReferenceEquals(ScRegistry.GetMember(npc.entityId), m) && toldMedicRole.Add(npc.entityId))
                {
                    EntityPlayerLocal player = ScCommunity.LocalPlayer;
                    // round MP4 (part: tells): this file's tells go by localization key (ScCommunity.TellKey / ScPlayers.TellCommunityKey), so each player reads them in their own language
                    if (player != null) ScCommunity.TellKey(player, "scSqueamishNoMedic", "ui_denied", m.Name);
                }
            }
        }

        /// <summary>
        /// ScSleep.MemberRefusal: is a horde really coming for a light sleeper to get up for? A live attack, or a called one with
        /// an arrival time. Not a called one that is held back (AttackArrive 0: the blood moon, an outpost fight, the player
        /// away): that has no horde on its way, and she would otherwise stay up all night for nothing.
        /// </summary>
        public static bool HordeComing()
        {
            Community c = ScRegistry.Community;
            return ScBaseAttack.Live || (c.AttackState == ScBaseAttack.StateCalled && c.AttackArrive != 0UL);
        }

        /// <summary>ScSleep.Tick, every second: no attack open any more - the next one may tell its light sleepers again.</summary>
        public static void SleepTick()
        {
            if (toldWoken.Count == 0) return;
            // round MP6 (fix H-m3): per community - a community with no attack open clears its own light sleepers (another
            // community's open or paused attack no longer silences them). With one community: cleared when its attack closed, as before
            foreach (CommunityState cs in ScCommunities.All)
            {
                bool open;
                using (ScRegistry.Use(cs.Id)) open = ScBaseAttack.Open;
                if (open) continue;
                if (ScCommunities.Count < 2) { toldWoken.Clear(); return; }
                foreach (Member m in cs.Members.Values) toldWoken.Remove(m.Id);
            }
        }

        /// <summary>ScSleep: a light sleeper's lease closed for a horde on the base - logged, the player told once per attack.</summary>
        public static void TellWoken(EntityAlive npc, string name)
        {
            woken++;
            ScLog.Out("sleep: " + name + " (" + (npc != null ? npc.entityId : -1) + ") is a light sleeper - up for the horde on the base");
            if (npc == null || !toldWoken.Add(npc.entityId)) return;      // once per attack: cleared only when no attack is open
            EntityPlayerLocal player = ScCommunity.LocalPlayer;
            // round MP6 (fix H-m3): the host hears of its own community's sleepers only
            if (player != null && (ScCommunities.Count < 2 || ScGates.MemberCommunityOf(npc) == ScPlayers.CommunityOf(player)))
                ScCommunity.TellKey(player, "scLightSleeperUp", "", name);
        }

        /// <summary>sc traits: the counters.</summary>
        public static string Report()
        {
            return "healed with no Meds " + healedUnaided + ", party-medic roles taken back " + medicRolesCleared + ", light sleepers woken " + woken;
        }

        /// <summary>sc traits &lt;member&gt;: one line per trait - what it does and whether that is live.</summary>
        public static List<string> Lines(Member m)
        {
            var o = new List<string>();
            if (m == null) return o;
            for (int i = 0; i < m.Traits.Count; i++)
            {
                string name = m.Traits[i];
                TraitDef t = ScTraits.Find(name);
                o.Add(name + " (" + ScTraits.Display(name) + "): " + (t == null ? "NOT in this config's trait table (kept, no effect)" : KeysText(t))
                      + " - " + ScTraits.Describe(name));
            }
            if (m.Traits.Count == 0) o.Add("no traits");
            o.Add("now: eats " + ScTraits.FoodUnits(m) + " a day" + (ScTraits.NoHungerMood(m) ? " (hunger spares her mood)" : "")
                  + ", supply runs up to +" + ScTraits.RunBundles(m, Resource.Food, 99) + " on food, +" + ScTraits.RunBundles(m, Resource.Materials, 99) + " on materials"
                  + ", mood on a night watch +" + NightWatchMood(m) + (SituationalMood(m) != 0 ? " (on it now)" : "") + ", wakes for a horde " + WakesForAttack(m)
                  + ", defense +" + DefenseBonus(m) + ", mining haul x" + F(HaulMult(m) * ScTraits.RunYieldMult(m)) + ", radio barter +" + RadioBarterExtra(m) + ", heals alone "
                  + (HealChance(m) * 100f).ToString("0", CultureInfo.InvariantCulture) + "%, books x" + F(BooksMult(m)) + ", may be a medic " + CanBeMedic(m)
                  + ", tool wear x" + F(WearMult(m)) + ", aim error x" + F(AimMult(m)) + "; waiting: sickness x" + F(SicknessMult(m)) + ", range XP x"
                  + F(RangeXpMult(m)) + ", gym XP x" + F(GymXpMult(m)));
            return o;
        }

        /// <summary>A trait's keys as the config writes them ("weight=2,haul=1.1,excludes=X").</summary>
        public static string KeysText(TraitDef t)
        {
            var parts = new List<string> { "weight=" + t.Weight };
            if (t.Eats > 0) parts.Add("eats=" + t.Eats);                                         // round ie1: whole-number keys
            if (t.NoHungerMood) parts.Add("nohungermood=1");
            if (t.RunBundles > 0) parts.Add("runbundles=" + t.RunBundles);
            if (t.RunKinds.Count > 0) parts.Add("runkinds=" + string.Join("/", t.RunKinds.ConvertAll(r => r.ToString()).ToArray()));
            if (t.RunFrom > 0) parts.Add("runfrom=" + t.RunFrom);
            if (t.MoraleOffset != 0) parts.Add("morale=" + t.MoraleOffset);
            if (t.MoodOffset != 0) parts.Add("mood=" + t.MoodOffset);
            if (t.HasSkill) parts.Add("skill=" + t.SkillBonus + ":" + t.SkillBonusStars);
            if (t.PreferredSpecialty != Specialty.None) parts.Add("spec=" + t.PreferredSpecialty);
            if (t.XpMult != 1f) parts.Add("xp=" + F(t.XpMult));
            if (t.RunYieldMult != 1f) parts.Add("yield=" + F(t.RunYieldMult));
            if (t.Timid) parts.Add("timid=1");
            if (t.NightWatchMood != 0) parts.Add("nightwatch=" + t.NightWatchMood);
            if (t.WakesForAttack) parts.Add("wake=1");
            if (t.DefenseBonus != 0) parts.Add("defense=" + t.DefenseBonus);
            if (t.HaulMult != 1f) parts.Add("haul=" + F(t.HaulMult));
            if (t.RadioBarterExtra != 0) parts.Add("barter=" + t.RadioBarterExtra);
            if (t.HealChance != 0f) parts.Add("heal=" + F(t.HealChance));
            if (t.SicknessMult != 1f) parts.Add("sickness=" + F(t.SicknessMult));
            if (t.BooksMult != 1f) parts.Add("books=" + F(t.BooksMult));
            if (t.RangeXpMult != 1f) parts.Add("rangexp=" + F(t.RangeXpMult));
            if (t.GymXpMult != 1f) parts.Add("gymxp=" + F(t.GymXpMult));
            if (t.NoMedic) parts.Add("nomedic=1");
            if (t.WearMult != 1f) parts.Add("wear=" + F(t.WearMult));
            if (t.AimMult != 1f) parts.Add("aim=" + F(t.AimMult));
            if (t.Excludes.Count > 0) parts.Add("excludes=" + string.Join("/", t.Excludes.ToArray()));
            if (t.Needs.Count > 0) parts.Add("waits for " + string.Join("+", t.Needs.ToArray()) + (ScFeatures.Pending(t) ? " (coming later)" : ""));
            return string.Join(",", parts.ToArray());
        }

        private static string F(float v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }

        /// <summary>A roll in [0,1): the game's random when there is one, else Unity's (kept apart so the rest runs offline too).</summary>
        public static float Rand01(GameRandom rnd) { return rnd != null ? rnd.RandomFloat : UnityRand(); }

        private static float UnityRand() { return UnityEngine.Random.value; }
    }
}
#endif
