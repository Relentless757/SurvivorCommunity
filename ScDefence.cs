using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Base defence D4: the defence rating, and how a fight at home without the player ends (BaseDefence_Plan.md, section 6
    /// with the D4 answers of 2026-10-06). Host only.
    ///   Rating   0-100 from saved state, so it is known with the base unloaded: BaseDefenceBase; + per fit resident (living
    ///            at home, not injured); + per combat star (Fighting + Shooting above 2); + per manned watch post (capped, and
    ///            only with ammo in the stockpile - an empty stockpile's guards still watch but add nothing); + per trap or
    ///            turret in the zone (capped; the home scan's count of OutpostDefenseBlocks); - with no ammo; + or - for
    ///            morale 75 and up or under 25; - a horde bigger than BaseDefenceBigFrom.
    ///   Result   a roll of 0-99 under the rating holds; under it + BaseAwayDamageBand the base is damaged; else overrun.
    ///   Costs    only ammo is consumed (1 bundle per BaseAwayAmmoPer attackers, x BaseAwayAmmoOverrun when overrun) and meds:
    ///            the residents hurt (none when held, BaseAwayHurtMin..Max damaged, BaseAwayHurtOverrun of them overrun) each
    ///            use a meds bundle at once and are fine; with none left they stay injured until a rollover heals them.
    ///   Deaths   any resident at home may die, in any result (the user's answer): a hurt one at DeathChance - about 8% for
    ///            Fighting + Shooting 6 against 15 zombies, scaled by the horde and the stars, at most BaseAwayDeathCap - an
    ///            unhurt one at BaseAwayDeathUnhurt of that. SC's death penalty applies; the body goes (or when it loads).
    ///   Morale   held +BaseAwayHeldMorale (DefenceTriumph), damaged / overrun -BaseAwayDamaged/OverrunMorale (DefenceLoss);
    ///            both fade a point a day, the Defence part is the difference.
    ///   Word     a powered radio tells the player at once; without one the news waits until they are home (one tooltip
    ///            each; the ledger line is written at the time). No Leadership event: only fights the player is at count.
    /// </summary>
    public static class ScDefence
    {
        public const int Held = 0, Damaged = 1, Overrun = 2;
        private static readonly string[] ResultNames = { "held", "damaged", "OVERRUN" };
        private const float NewsSeconds = 7f;
        private static float nextNews;

        public static void Reset()
        {
            nextNews = 0f;
        }

        public static string ResultName(int result) { return ResultNames[Mathf.Clamp(result, 0, 2)]; }

        public static int ParseResult(string s)
        {
            switch ((s ?? "").ToLowerInvariant())
            {
                case "held": return Held;
                case "damaged": return Damaged;
                case "overrun": return Overrun;
                default: return -1;
            }
        }

        // ------------------------------------------------------------------ the rating ----

        /// <summary>The defence rating against a horde of 'horde' (0-100); 'why' (optional) gets one line per term.</summary>
        public static int Rating(int horde, StringBuilder why)
        {
            Community c = ScRegistry.Community;
            float s = ScConfig.BaseDefenceBase;
            Line(why, "base", ScConfig.BaseDefenceBase);
            int fit = 0, stars = 0, injured = 0;
            foreach (Member m in ScRegistry.Members.Values)
            {
                if (m.Status != MemberStatus.Resident) continue;
                if (m.Injured) { injured++; continue; }
                fit++;
                stars += Math.Max(0, m.Stars(Skill.Fighting) + m.Stars(Skill.Shooting) - 2);
            }
            s += ScConfig.BaseDefencePerResident * fit;
            Line(why, fit + " fit resident(s)" + (injured > 0 ? " (" + injured + " injured: none)" : ""), ScConfig.BaseDefencePerResident * fit);
            s += ScConfig.BaseDefencePerCombatStar * stars;
            Line(why, stars + " combat star(s) above 2 (Fighting + Shooting)", ScConfig.BaseDefencePerCombatStar * stars);
            bool ammo = c.Get(Resource.Ammo) > 0;
            int manned = ScWatch.Manned();
            float posts = ammo ? Math.Min(ScConfig.BaseDefencePostCap, ScConfig.BaseDefencePerPost * manned) : 0f;
            s += posts;
            Line(why, manned + " manned watch post(s)" + (manned > 0 && !ammo ? " (no ammo in the stockpile: they watch, add nothing)" : ""), posts);
            float traps = Math.Min(ScConfig.BaseDefenceBlockCap, ScConfig.BaseDefencePerBlock * c.HomeDefences);
            s += traps;
            Line(why, c.HomeDefences + " trap(s) and turret(s) in the zone", traps);
            if (!ammo)
            {
                s -= ScConfig.BaseDefenceNoAmmo;
                Line(why, "no ammo in the stockpile", -ScConfig.BaseDefenceNoAmmo);
            }
            if (c.Morale >= 75) { s += ScConfig.BaseDefenceMorale; Line(why, "morale " + c.Morale + " (75+)", ScConfig.BaseDefenceMorale); }
            else if (c.Morale < 25) { s -= ScConfig.BaseDefenceMorale; Line(why, "morale " + c.Morale + " (under 25)", -ScConfig.BaseDefenceMorale); }
            if (horde > ScConfig.BaseDefenceBigFrom)
            {
                float big = Math.Min(ScConfig.BaseDefenceBigCap, (horde - ScConfig.BaseDefenceBigFrom) * ScConfig.BaseDefenceBigPer);
                s -= big;
                Line(why, "a horde of " + horde + " (over " + ScConfig.BaseDefenceBigFrom.ToString("0", CultureInfo.InvariantCulture) + ")", -big);
            }
            int r = Mathf.Clamp(Mathf.RoundToInt(s), 0, 100);
            if (why != null) why.Append("= ").Append(r).Append(" (0-100)");
            return r;
        }

        private static void Line(StringBuilder why, string what, float v)
        {
            if (why == null) return;
            int n = Mathf.RoundToInt(v);
            why.Append(n >= 0 ? "+" : "").Append(n).Append("  ").Append(what).Append('\n');
        }

        /// <summary>sc defence: the rating against a horde of 'horde', term by term, and what a fight without the player would mean.</summary>
        public static string Breakdown(int horde)
        {
            var sb = new StringBuilder();
            sb.Append("defence rating against ").Append(horde).Append(" zombie(s):\n");
            int r = Rating(horde, sb);
            int band = Math.Max(0, ScConfig.BaseAwayDamageBand);
            int held = r, damaged = Math.Min(100, r + band) - r, overrun = 100 - Math.Min(100, r + band);
            sb.Append("\nwithout you: held ").Append(held).Append("%, damaged ").Append(damaged).Append("%, overrun ").Append(overrun).Append('%')
              .Append("; ammo ").Append(Bundles(horde, 1f)).Append(" bundle(s) (").Append(Bundles(horde, ScConfig.BaseAwayAmmoOverrun)).Append(" overrun)");
            float worst = 0f;
            string who = null;
            foreach (Member m in ScRegistry.Members.Values)
            {
                if (m.Status != MemberStatus.Resident) continue;
                float p = DeathChance(m, horde, true);
                if (p > worst) { worst = p; who = m.Name; }
            }
            if (who != null)
                sb.Append("\nhighest risk if hurt: ").Append(who).Append(' ').Append((worst * 100f).ToString("0.#", CultureInfo.InvariantCulture))
                  .Append("% (unhurt x").Append(ScConfig.BaseAwayDeathUnhurt.ToString("0.##", CultureInfo.InvariantCulture)).Append(')');
            sb.Append("\nhome traps/turrets counted: ").Append(ScRegistry.Community.HomeDefences);
            return sb.ToString();
        }

        /// <summary>
        /// A resident's chance to die in a fight without the player against 'horde': BaseAwayDeathChance x horde /
        /// BaseAwayDeathHorde x the skill factor (1 + step x (ref - Fighting - Shooting), 0.5..1.5), at most BaseAwayDeathCap;
        /// BaseAwayDeathUnhurt of it for one who was not hurt.
        /// </summary>
        public static float DeathChance(Member m, int horde, bool hurt)
        {
            if (m == null || horde <= 0) return 0f;
            int combat = m.Stars(Skill.Fighting) + m.Stars(Skill.Shooting);
            float skill = Mathf.Clamp(1f + ScConfig.BaseAwayDeathSkillStep * (ScConfig.BaseAwayDeathSkillRef - combat), 0.5f, 1.5f);
            float p = ScConfig.BaseAwayDeathChance * (horde / Math.Max(1f, ScConfig.BaseAwayDeathHorde)) * skill;
            p = Math.Min(p, ScConfig.BaseAwayDeathCap);
            if (!hurt) p *= ScConfig.BaseAwayDeathUnhurt;
            return Mathf.Clamp01(p);
        }

        /// <summary>Ammo bundles for 'attackers' dealt with without the player: one per BaseAwayAmmoPer (x mult), at least 1.</summary>
        public static int Bundles(int attackers, float mult)
        {
            if (attackers <= 0) return 0;
            return Math.Max(1, Mathf.RoundToInt(attackers * mult / Math.Max(1f, ScConfig.BaseAwayAmmoPer)));
        }

        /// <summary>Takes up to 'want' ammo bundles from the stockpile for a fight (the day's "ammo used"); returns how many it got.</summary>
        public static int TakeAmmo(int want)
        {
            if (want <= 0) return 0;
            Community c = ScRegistry.Community;
            int took = Math.Min(want, c.Get(Resource.Ammo));
            c.Add(Resource.Ammo, -took);
            c.AmmoOpened += took;
            if (took < want) c.AmmoShort = true;
            return took;
        }

        // ------------------------------------------------------------------ the result ----

        /// <summary>
        /// A fight at home without the player is over (an attack the defenders fought alone, or a siege): rolled against
        /// the rating less 'penalty' ('forced' 0-2 from the console instead), and everything it costs applied. 'horde' is
        /// how many the defenders faced. The caller ends the attack and saves. Returns the result.
        /// </summary>
        public static int Settle(World w, EntityPlayerLocal player, int horde, int penalty, bool siege, int forced, string why)
        {
            Community c = ScRegistry.Community;
            GameRandom rnd = w != null ? w.GetGameRandom() : null;
            int rating = Rating(horde, null);
            int sec = Mathf.Clamp(rating - Math.Max(0, penalty), 0, 100);
            int roll = Rnd(rnd, 100);
            int result = forced >= Held && forced <= Overrun ? forced
                       : roll < sec ? Held : roll < sec + Math.Max(0, ScConfig.BaseAwayDamageBand) ? Damaged : Overrun;

            // ammo: the only stock a fight consumes
            int want = Bundles(horde, result == Overrun ? ScConfig.BaseAwayAmmoOverrun : 1f);
            int took = TakeAmmo(want);

            // who is at home, in a random order: the first 'hurt' of them are hurt
            var home = new List<Member>();
            foreach (Member m in ScRegistry.Members.Values) if (m.Status == MemberStatus.Resident) home.Add(m);
            for (int i = home.Count - 1; i > 0; i--)
            {
                int j = Rnd(rnd, i + 1);
                Member t = home[i]; home[i] = home[j]; home[j] = t;
            }
            int hurt = 0;
            if (result == Damaged) hurt = ScConfig.BaseAwayHurtMin + Rnd(rnd, Math.Max(0, ScConfig.BaseAwayHurtMax - ScConfig.BaseAwayHurtMin) + 1);
            else if (result == Overrun) hurt = Mathf.CeilToInt(ScConfig.BaseAwayHurtOverrun * home.Count);
            hurt = Mathf.Clamp(hurt, 0, home.Count);

            // anyone at home may die (the user's D4 answer); the hurt carry the full chance
            var dead = new List<Member>();
            var risk = new StringBuilder();
            for (int i = 0; i < home.Count; i++)
            {
                float p = DeathChance(home[i], horde, i < hurt);
                float r = rnd != null ? rnd.RandomFloat : UnityEngine.Random.value;
                if (risk.Length > 0) risk.Append(", ");
                risk.Append(home[i].Name).Append(i < hurt ? " (hurt) " : " ").Append((p * 100f).ToString("0.#", CultureInfo.InvariantCulture)).Append('%');
                if (r < p) { dead.Add(home[i]); risk.Append(" DIED"); }
            }

            // the hurt who live: a meds bundle each at once, else they stay injured
            int treated = 0, untreated = 0;
            for (int i = 0; i < hurt; i++)
            {
                Member m = home[i];
                if (dead.Contains(m)) continue;
                if (c.Get(Resource.Meds) > 0) { c.Add(Resource.Meds, -1); treated++; }
                else { m.Injured = true; untreated++; }
            }

            var names = new StringBuilder();
            foreach (Member m in dead)
            {
                if (names.Length > 0) names.Append(", ");
                names.Append(m.Name);
                Kill(m, player);
            }

            switch (result)
            {
                case Held: c.DefenceTriumph = Math.Min(10f, c.DefenceTriumph + ScConfig.BaseAwayHeldMorale); break;
                case Damaged: c.DefenceLoss = Math.Min(30f, c.DefenceLoss + ScConfig.BaseAwayDamagedMorale); break;
                default: c.DefenceLoss = Math.Min(30f, c.DefenceLoss + ScConfig.BaseAwayOverrunMorale); break;
            }
            ScMorale.Recompute();

            string news = Encode(siege, result, horde, took, want, treated + untreated, treated, untreated, names.ToString());
            ScOutposts.NoteLedger(Text(news));
            bool radio = Radio();
            if (player != null && (radio || ScBaseAttack.PlayerHome(player)))
                ScCommunity.Tell(player, string.Format(Localization.Get(ScBaseAttack.PlayerHome(player) ? "scDefNow" : "scDefRadio"), Text(news)), result == Held ? "" : "ui_denied");
            else
            {
                while (c.AwayNews.Count >= 4) c.AwayNews.RemoveAt(0);
                c.AwayNews.Add(news);
            }
            ScLog.Out((siege ? "base SIEGE" : "base attack without the player") + " " + ResultName(result) + " (" + why + "): " + horde + " zombie(s), rating "
                      + rating + (penalty > 0 ? " - " + penalty : "") + ", roll " + (forced >= 0 ? "forced" : roll.ToString()) + "; ammo " + took + "/" + want
                      + " bundle(s); hurt " + hurt + " (" + treated + " treated, " + untreated + " untreated); deaths " + dead.Count
                      + (dead.Count > 0 ? " (" + names + ")" : "") + "; risk: " + risk + "; " + (radio ? "told over the radio" : player != null && ScBaseAttack.PlayerHome(player) ? "told at home" : "news kept for the return"));
            return result;
        }

        /// <summary>A resident dies in a fight without the player: SC's death (penalty, deaths list), the body removed now or when it loads.</summary>
        private static void Kill(Member m, EntityPlayerLocal player)
        {
            try
            {
                if (ScWatch.OnWatch(m)) ScWatch.Relieve(m, null, "died in a fight at home");
                EntityAlive npc = ScCommunity.FindNpc(m.Id);
                ScCommunity.RecordDeath(m, null, null);          // no tooltip of its own: the news names them
                if (player != null) player.Buffs.RemoveCustomVar("hired_" + m.Id);
                if (npc != null) ScRuns.RemoveEntity(npc);
                else ScRegistry.Doomed.Add(m.Id);                // the body is removed when its chunk next loads
            }
            catch (Exception e)
            {
                ScLog.Once("defence-kill", e);
            }
        }

        private static int Rnd(GameRandom rnd, int n)
        {
            if (n <= 1) return 0;
            return rnd != null ? rnd.RandomRange(0, n) : UnityEngine.Random.Range(0, n);
        }

        public static bool Radio()
        {
            try { return ScOutposts.RadioOnlineAnywhere(); }
            catch (Exception e) { ScLog.Once("defence-radio", e); return false; }
        }

        // ------------------------------------------------------------------ the news ----

        // siege|result|horde|took|want|hurt|treated|untreated|names (names: ", "-joined, no separators of the file in them)
        private static string Encode(bool siege, int result, int horde, int took, int want, int hurt, int treated, int untreated, string names)
        {
            string n = (names ?? "").Replace("|", " ").Replace(";", " ").Replace("=", " ").Replace("\n", " ");
            return (siege ? 1 : 0) + "|" + result + "|" + horde + "|" + took + "|" + want + "|" + Math.Max(0, hurt) + "|" + treated + "|" + untreated + "|" + n;
        }

        private static int Num(string s)
        {
            int v;
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : 0;
        }

        /// <summary>The words for an encoded result: "the base held against 14 of the dead; 1 ammo bundle used; ..."</summary>
        public static string Text(string news)
        {
            string[] f = (news ?? "").Split('|');
            if (f.Length < 9) return news ?? "";
            bool siege = Num(f[0]) != 0;
            int result = Mathf.Clamp(Num(f[1]), 0, 2), horde = Num(f[2]), took = Num(f[3]), want = Num(f[4]), hurt = Num(f[5]), treated = Num(f[6]), untreated = Num(f[7]);
            string[] keys = siege ? new[] { "scDefSiegeHeld", "scDefSiegeDamaged", "scDefSiegeOverrun" } : new[] { "scDefHeld", "scDefDamaged", "scDefOverrun" };
            var sb = new StringBuilder(string.Format(Localization.Get(keys[result]), horde));
            sb.Append("; ").Append(string.Format(Localization.Get(took < want ? "scDefAmmoShort" : "scDefAmmo"), took));
            if (hurt > 0) sb.Append("; ").Append(string.Format(Localization.Get("scDefHurt"), hurt, treated, untreated));
            if (f[8].Trim().Length > 0) sb.Append("; ").Append(string.Format(Localization.Get("scDefDied"), f[8].Trim()));
            return sb.ToString();
        }

        /// <summary>ScBaseAttack's tick: the news kept while the player was away with no radio, one tooltip at a time once they are home.</summary>
        public static void TickNews(EntityPlayerLocal player, float now)
        {
            Community c = ScRegistry.Community;
            if (c.AwayNews.Count == 0 || player == null || now < nextNews || !ScBaseAttack.PlayerHome(player)) return;
            nextNews = now + NewsSeconds;
            string news = c.AwayNews[0];
            c.AwayNews.RemoveAt(0);
            ScCommunity.Tell(player, string.Format(Localization.Get("scDefAway"), Text(news)), "ui_denied");
            ScLog.Out("told on the return: " + Text(news));
            ScRegistry.SaveAtomic();
        }
    }
}
