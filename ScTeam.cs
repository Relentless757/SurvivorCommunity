using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round tm1 (2026-11-15, research/combat/TEAM_MELEE_REPORT.md; the user approved every recommendation): team strength
    /// decides melee. The play test that asked for it: Gina (Fighting 2, full health) swung at one zombie and then went for
    /// her bow, Rob used his crossbow on a lone walker, and Paul drew his .44 on a walker 8 m off and backed away from it -
    /// "5 members vs 5 zombies should not use ranged unless HP is low".
    ///
    /// Each of the community's members on 0-XNPCCore's body keeps a verdict, recounted once a second (staggered by entity id)
    /// from SC's X5 prefix on SdcsCloseRangeTick (Recount) and only READ everywhere else - round k4's posture (ScPosture: the
    /// verdict is its input, MELEE needs "melee-led"), the Cowardly flee's ScNotTeamMatched and ScArms.IsOverwhelmed.
    ///
    ///   F (friendly strength) within TeamRadius (15 m; during a live base attack the whole defended area): a member
    ///     (TeamStarBase + TeamStarStep x Fighting stars) x HpF x her weapon factor (a melee weapon in her pair 1, ranged
    ///     only 0.8, nothing 0.5) - she counts herself; a member on watch TeamGuardWeight x HpF; any member in the middle of
    ///     a medical use x TeamMedicalMult; one asleep in bed 0; the player TeamPlayerWeight x HpF (decision 1) - round k1
    ///     (the user's decisions, k1 and its Fable review: "Only my hits count"): only while he is fighting, i.e. he landed a
    ///     hit on an enemy inside the counted area in the last TeamPlayerFightingSeconds (Patch_PlayerHit, kept per player,
    ///     so a kill counts as well) - a zombie that merely has him as its target no longer does - else 0; another
    ///     ally (a hire, a friendly NPC) TeamAllyWeight x HpF. HpF = 0.5 + 0.5 x min(1, health share / 0.9): a scratch
    ///     does not count (TeamHealthFullAt, the Fable review).
    ///   T (threat): every awake enemy in the same area, whether or not it is after the team (decision 14):
    ///     max(tag weight, health weight) x the final multipliers (crawler 0.6). The tag weight is the highest entry of the
    ///     threat table it matches (walker 1, feral 2, radiated 3, charged 3.5, infernal 4.5, the animals) times the tag
    ///     multipliers (special 1.5, strong 1.3); the health weight covers tough and modded zombies.
    ///   Matched: F >= TeamMargin x T (1.0, decision 3 - round k3 tried 1.25; the user's decision after the Fable review of
    ///     k3+k4: "Exact parity, lone member melees"); it stops being matched after TeamLeaveRecounts (2) recounts in a row with
    ///     F &lt; TeamLeaveMargin x T (0.8), and inside a fight it is matched again only after as many recounts in a row with
    ///     F >= TeamMargin x T (F3); no enemies = matched. A new fight (the first count with enemies) is judged by the entry
    ///     margin alone, at once - the leave margin and the recounts are hysteresis inside a fight (the report's worked examples).
    ///   RaiderFight: an enemy NPC (a raider, a bandit, a hostile player) or an armed enemy in the area - today's rules.
    ///   Melee-led: TeamMelee on; a member's body, not a raider; not a runner on the move, not on a Stay order (she gives
    ///     supporting fire), not asleep in bed, not on watch, not Cowardly (decision 8), no gun order from the player
    ///     (sc_order_ranged, decision 9); a melee weapon in her pair; health at
    ///     least her own hurt line (ScTeam.HurtLines, round wp2: TeamLowHealth 60%, back from TeamLowHealthBack 65%, both lower per
    ///     Fighting star above TeamStarBonusFrom; round k3 had 60% / 70%, before it 50% / 60%, decision 5); matched; no
    ///     RaiderFight. Followers away from home too (decision 11); enclave people and strangers have no verdict (decision 12).
    ///   NeedsGun(target): the target is melee-unsafe (a zombie cop or the demolition zombie: EntityZombieCop, or a
    ///     TeamGunTags tag - decision 10), armed, past a resident's leash (ScReturn's own rule), or out of melee reach
    ///     (ScArms.ReachWhy: a swing 0-XNPCCore refused, above her, its unreachable mark, a path that ends short). Round k4:
    ///     such a target makes the posture KITE at once (ScPosture), and its draw of the gun is at once too.
    /// </summary>
    public static class ScTeam
    {
        /// <summary>Slack on the F against T tests: 0.85 + 0.15 is 1.0000000298 in floats, and decision 2 is F 1.0 >= T 1.</summary>
        private const float Eps = 0.001f;
        /// <summary>A live base attack's sweep reaches this far above and below the member (the zone is flat on the map).</summary>
        private const float WideHalfHeight = 40f;
        /// <summary>
        /// Round wp1 (decision 3): a member's F / T breakdown is logged at a fight's start (the first count with enemies in it), at
        /// most once in this long - a zombie stepping in and out of TeamRadius starts a "fight" each time.
        /// </summary>
        private const float FightLogSeconds = 30f;
        /// <summary>Round wp1 (decision 3): at most this many friends and enemies in that line (then "and N more").</summary>
        private const int FightLogItems = 8;

        private sealed class Verdict
        {
            /// <summary>When it was last counted (0 = not yet), when it is next due, the last X5 tick that asked.</summary>
            public float At, Next, Seen;
            public float F, T;
            /// <summary>No enemies = matched, so a fight starts matched and leaves it on the leave margin.</summary>
            public bool Matched = true;
            /// <summary>Recounts in a row with F below TeamLeaveMargin x T (matched), or at least TeamMargin x T (outmatched, F3).</summary>
            public int Below, Above;
            public bool LowHp, RaiderFight, Wide, MeleeLed;
            /// <summary>The Fable review: her pair has a ranged weapon with rounds - a Cowardly member can hang back with it.</summary>
            public bool Gun;
            public int Enemies, Friends, Unsafe;
            /// <summary>Round k1: players inside the counted area, and how many of them were fighting (counted TeamPlayerWeight).</summary>
            public int PlayersIn, PlayersFighting;
            /// <summary>The nearest counted enemy (her fight's target while she has none of her own: Target), -1 none.</summary>
            public int Nearest = -1;
            /// <summary>Round wp2 (1): her own hurt lines at the last count - off below HurtOff, melee again from HurtOn (HurtLines).</summary>
            public float HurtOff, HurtOn;
            /// <summary>Why she is not melee-led ("" when she is).</summary>
            public string Why = "";
            /// <summary>Round wp1 (decision 2): when the fight last turned matched again (0 never) - ScPosture's switch to melee is prompt then.</summary>
            public float MatchedAgainAt;
            /// <summary>Round wp1 (decision 3): when her F breakdown was last logged at a fight's start (FightLogSeconds apart at most).</summary>
            public float FightLoggedAt = -1000f;
        }

        /// <summary>One count over the area.</summary>
        private struct Tally
        {
            public float F, T;
            public bool RaiderFight, Wide;
            public int Enemies, Friends, Unsafe;
            public int PlayersIn, PlayersFighting;
            public EntityAlive Nearest;
            public float NearestD2;
        }

        /// <summary>
        /// Round k1, per player since the Fable review (F2b): one landed hit by that player - on whom (name, for the console),
        /// where the victim stood and when (Patch_PlayerHit, host only).
        /// </summary>
        private struct PlayerHit
        {
            public string Victim;
            public Vector3 Where;
            public float At;
        }

        /// <summary>F2b: a player keeps his last this many hits (the oldest drops out first), pruned by age.</summary>
        private const int HitsPerPlayer = 8;

        /// <summary>
        /// Why a target needs the gun (NeedsGun). Round k4: each makes the posture KITE at once (no dwell), and the draw does not
        /// wait for the hand clock either - with her melee weapon in KITE she could neither back off nor walk to it.
        /// </summary>
        public enum GunNeed { None, Unsafe, Armed, Leash, Reach }

        private static readonly Dictionary<int, Verdict> verdicts = new Dictionary<int, Verdict>();
        private static readonly List<Entity> near = new List<Entity>();
        private static float nextPrune;
        private static int recounts, outmatched, matchedAgain;
        private static readonly FastTags<TagGroup.Global> TraderTag = FastTags<TagGroup.Global>.Parse("trader");
        /// <summary>
        /// Round k1: player entity id -> his last HitsPerPlayer landed hits (Patch_PlayerHit); pruned by age. The Fable review (F2b):
        /// keyed per player, not per victim - per victim, a zombie he killed took its hit with it (the clock was only read from
        /// the living enemies counted), so a player who one-shot zombies counted 0.
        /// </summary>
        private static readonly Dictionary<int, List<PlayerHit>> playerHits = new Dictionary<int, List<PlayerHit>>();
        private static int playerHitsNoted;
        /// <summary>Round k1, Census scratch: the players fighting in the area being counted (id -> why, "" without detail), and the non-enemies in it.</summary>
        private static readonly Dictionary<int, string> fighting = new Dictionary<int, string>();
        private static readonly List<EntityAlive> others = new List<EntityAlive>();

        public static void Reset()
        {
            verdicts.Clear();
            near.Clear();
            nextPrune = 0f;
            recounts = outmatched = matchedAgain = 0;
            playerHits.Clear();
            playerHitsNoted = 0;
            fighting.Clear();
            others.Clear();
        }

        /// <summary>A despawned body's verdict (entity ids are recycled).</summary>
        public static void Forget(int entityId) { verdicts.Remove(entityId); }

        // ------------------------------------------------------------------ the recount (X5 only) ----

        /// <summary>
        /// The X5 prefix's team part (Patches_Xnpc.Patch_NoSwapWhileUsing, on SdcsCloseRangeTick, 5 Hz): recounts this
        /// member's verdict when it is due (TeamRecountSeconds, staggered). The only writer of the verdicts. Never throws.
        /// </summary>
        public static void Recount(EntityAlive e)
        {
            try
            {
                if (!ScConfig.TeamMelee || e == null || e.isEntityRemote || !ScRegistry.Loaded || !ScXnpc.IsBody(e) || ScXnpc.IsRaider(e)) return;
                Member m = MemberOf(e);
                if (m == null) return;
                float now = Time.time;
                Verdict v;
                if (!verdicts.TryGetValue(e.entityId, out v))
                {
                    verdicts[e.entityId] = v = new Verdict();
                    v.Next = now + (e.entityId % 10) * 0.1f * ScConfig.TeamRecountSeconds;   // staggered: not every member in one frame
                }
                v.Seen = now;
                if (now >= nextPrune) Prune(now);
                if (now < v.Next) return;
                v.Next = now + ScConfig.TeamRecountSeconds;
                var t = new Tally();
                Census(e, ref t, null);
                recounts++;
                Patches_Xnpc.Count("x5-team-recount");
                bool wasMatched = v.Matched;
                // a new fight (her first count, or the first with enemies in it) is judged by the entry rule alone: the
                // report's worked examples "5 members + you vs 5 ferals 8.5 / 10 -> today's rules" and "Gina at 58% alone
                // 0.91 / 1 -> her bow" both sit between the margins, so a fight that began "matched" (no enemies) would
                // otherwise stay matched there; the leave margin is hysteresis inside a fight, not at its start
                bool newFight = v.At <= 0f || v.T <= 0f;
                v.F = t.F;
                v.T = t.T;
                v.RaiderFight = t.RaiderFight;
                v.Wide = t.Wide;
                v.Enemies = t.Enemies;
                v.Friends = t.Friends;
                v.Unsafe = t.Unsafe;
                v.PlayersIn = t.PlayersIn;
                v.PlayersFighting = t.PlayersFighting;
                v.Nearest = t.Nearest != null ? t.Nearest.entityId : -1;
                bool atMargin = t.F + Eps >= ScConfig.TeamMargin * t.T;
                if (t.T <= 0f) { v.Matched = true; v.Below = v.Above = 0; }                   // no enemies: matched
                else if (newFight) { v.Matched = atMargin; v.Below = v.Above = 0; }           // a new fight: the entry rule alone, at once
                else if (v.Matched)
                {
                    v.Above = 0;
                    if (t.F + Eps < ScConfig.TeamLeaveMargin * t.T)
                    {
                        v.Below++;
                        if (v.Below >= ScConfig.TeamLeaveRecounts) v.Matched = false;
                    }
                    else v.Below = 0;                                                       // between the margins: as it was
                }
                else
                {
                    // the Fable review of k3+k4 (F3): matched again inside a fight only after TeamLeaveRecounts recounts in a row at
                    // the entry margin, as leaving takes - your 5 s hit window (TeamPlayerFightingSeconds) cannot flip it by itself
                    v.Below = 0;
                    if (atMargin)
                    {
                        v.Above++;
                        if (v.Above >= ScConfig.TeamLeaveRecounts) { v.Matched = true; v.Above = 0; }
                    }
                    else v.Above = 0;
                }
                float hp = HealthShare(e);
                bool wasLow = v.LowHp;
                // round wp2 (1): her own lines - lower for a strong fighter, and the way back always under the regeneration cap
                HurtLines(m, out v.HurtOff, out v.HurtOn);
                if (hp < v.HurtOff) v.LowHp = true;
                else if (hp >= v.HurtOn) v.LowHp = false;
                Loadout pair = ScLoadout.Current(e);
                ItemClass gun = pair != null && pair.HasRanged ? ItemClass.GetItemClass(pair.Ranged, true) : null;
                v.Gun = ScAmmo.RangedAction(gun) != null && ScAmmo.Rounds(e, gun) > 0;
                string why = WhyNotLed(e, m, v);
                v.MeleeLed = why == null;
                v.Why = why ?? "";
                v.At = now;
                if (wasMatched && !v.Matched)
                {
                    outmatched++;
                    Patches_Xnpc.Count("x5-team-outmatched");
                    ScLog.Verbose(ScCommunity.DisplayName(e) + " (id " + e.entityId + "): the fight is outmatched now (F " + F2(t.F) + " < "
                                  + F2(ScConfig.TeamLeaveMargin) + " x T " + F2(t.T) + ", " + t.Enemies + " enemies) - today's weapon rules");
                }
                else if (!wasMatched && v.Matched)
                {
                    // round wp1 (decision 2); (review WP1-m3) only with enemies counted - "matched again" at T 0 is a fight's end, and
                    // the next fight's first seconds must not skip the dwell and the hand clock
                    if (t.T > 0f) v.MatchedAgainAt = now;
                    matchedAgain++;
                    Patches_Xnpc.Count("x5-team-matched-again");
                    ScLog.Verbose(ScCommunity.DisplayName(e) + " (id " + e.entityId + "): the fight is matched again (F " + F2(t.F) + " >= " + F2(ScConfig.TeamMargin) + " x T " + F2(t.T) + ", "
                                  + ScConfig.TeamLeaveRecounts + " recounts in a row)");
                }
                if (!wasLow && v.LowHp) Patches_Xnpc.Count("x5-team-low-health");
                // round wp1 (decision 3): a fight's start (the first count with enemies) logs her F / T breakdown once - the Fighting
                // stars' share in it shows; a second census only then, with its detail lines
                if (newFight && t.T > 0f && now - v.FightLoggedAt >= FightLogSeconds)
                {
                    v.FightLoggedAt = now;
                    LogFight(e, v);
                }
            }
            catch (Exception ex)
            {
                ScLog.Once("team-recount", ex);
            }
        }

        /// <summary>Round wp1 (decision 3): her verdict at a fight's start with its friends and enemies, one line (FightLogItems of them at most).</summary>
        private static void LogFight(EntityAlive e, Verdict v)
        {
            var lines = new List<string>();
            var t = new Tally();
            Census(e, ref t, lines);
            var sb = new System.Text.StringBuilder();
            sb.Append(ScCommunity.DisplayName(e)).Append(" (id ").Append(e.entityId).Append("): a fight starts - F ").Append(F2(v.F)).Append(" / T ").Append(F2(v.T))
              .Append(v.Matched ? " (matched" : " (outmatched").Append(v.MeleeLed ? ", melee-led)" : ": " + v.Why + ")").Append(':');
            int n = Math.Min(lines.Count, FightLogItems);
            for (int i = 0; i < n; i++) sb.Append(i == 0 ? " " : "; ").Append(lines[i].Trim());
            if (lines.Count > n) sb.Append("; and ").Append(lines.Count - n).Append(" more");
            sb.Append(" (round wp1)");
            ScLog.Out(sb.ToString());
        }

        /// <summary>Why this member is not melee-led on this verdict, or null when she is (the order: what she is, then the fight).</summary>
        private static string WhyNotLed(EntityAlive e, Member m, Verdict v)
        {
            if (ScRunners.IsPhysical(m) && !m.Offline) return "on a run (" + ScRetreat.Her(e) + " walk decides)";     // round wp2 (review): the pronouns
            // the Fable review: a Stay order (CurrentOrder 2, the cvar 0-XNPCCore's MeleeApproach is gated on) - she does not
            // walk to a fight, so she gives supporting fire
            if (e.Buffs != null && Mathf.RoundToInt(e.Buffs.GetCustomVar("CurrentOrder")) == 2) return "ordered to stay (" + ScRetreat.She(e) + " gives supporting fire)";
#if SC_V34
            if (ScSleep.IsAsleep(e)) return "asleep in bed";
#endif
            string her = ScRetreat.Her(e);
            if (ScWatch.OnWatch(m)) return "on watch (a guard keeps " + her + " gun)";
            if (ScTraits.IsTimid(m)) return "Cowardly (hangs back with " + her + " gun; runs only when outmatched)";
            if (ScArms.OrderedRanged(e)) return "you ordered " + her + " gun (by talking or the weapon toggle; order " + her + " melee weapon, or sc team " + e.entityId + " auto, to undo)";
            Loadout l = ScLoadout.Current(e);
            if (l == null || string.IsNullOrEmpty(l.Melee)) return "no melee weapon in " + her + " pair";
            if (v.LowHp) return "hurt (" + Pct(HealthShare(e)) + " - melee again from " + Pct(v.HurtOn) + "; off below " + Pct(v.HurtOff)
                                + (m.Stars(Skill.Fighting) > ScConfig.TeamStarBonusFrom && ScConfig.MeleeHurtPerStar > 0f ? " for Fighting " + m.Stars(Skill.Fighting) + "*" : "") + ")";     // round wp2 (1; review: the stars, not the cap)
            if (v.RaiderFight) return "armed enemies in the fight (raiders, bandits: today's rules)";
            if (!v.Matched) return "outmatched (F " + F2(v.F) + " vs T " + F2(v.T) + ": matched again after " + ScConfig.TeamLeaveRecounts + " recounts at F >= "
                                   + F2(ScConfig.TeamMargin) + " T)";
            return null;
        }

        private static void Prune(float now)
        {
            nextPrune = now + 30f;
            var gone = new List<int>();
            foreach (var kv in verdicts) if (now - kv.Value.Seen > 60f) gone.Add(kv.Key);
            foreach (int id in gone) verdicts.Remove(id);
            PruneHits(now);
        }

        /// <summary>Round k1: player hits too old to count (never fewer than 30 s kept); a player with none left goes.</summary>
        private static void PruneHits(float now)
        {
            float keep = Mathf.Max(30f, ScConfig.TeamPlayerFightingSeconds);
            var gone = new List<int>();
            foreach (var kv in playerHits)
            {
                kv.Value.RemoveAll(h => now - h.At > keep);
                if (kv.Value.Count == 0) gone.Add(kv.Key);
            }
            foreach (int id in gone) playerHits.Remove(id);
        }

        /// <summary>
        /// Round k1 (Patch_PlayerHit, host only): a player landed a hit on this entity now. F2b: kept per player, with where the
        /// victim stood - a kill keeps it, and each member's count reads it against her own counted area.
        /// </summary>
        internal static void NotePlayerHit(EntityAlive victim, int playerId)
        {
            float now = Time.time;
            List<PlayerHit> hits;
            if (!playerHits.TryGetValue(playerId, out hits)) playerHits[playerId] = hits = new List<PlayerHit>(HitsPerPlayer);
            if (hits.Count >= HitsPerPlayer) hits.RemoveAt(0);
            hits.Add(new PlayerHit { Victim = Who(victim), Where = victim.position, At = now });
            playerHitsNoted++;
            if (playerHitsNoted % 64 == 0) PruneHits(now);
        }

        /// <summary>
        /// The players fighting in the area being counted (into the fighting scratch; the Fable review, F2a, the user: "Only my
        /// hits count"): each player whose hit landed on an enemy inside the area (inside: the census's own test, on where the
        /// victim stood) at most TeamPlayerFightingSeconds ago - dead or alive now. An enemy that merely has him as its target
        /// does not make him fighting any more. detail: the reason, for the console.
        /// </summary>
        private static void NoteFighting(Vector3 at, bool wide, float r2, float now, bool detail)
        {
            float win = ScConfig.TeamPlayerFightingSeconds;
            foreach (var kv in playerHits)
            {
                List<PlayerHit> hits = kv.Value;
                for (int i = hits.Count - 1; i >= 0; i--)
                {
                    PlayerHit h = hits[i];
                    if (now - h.At > win) break;                        // newest last: the rest are older still
                    if (!Inside(at, wide, r2, h.Where)) continue;
                    fighting[kv.Key] = detail ? "you hit " + h.Victim + " " + (now - h.At).ToString("0.0", CultureInfo.InvariantCulture) + " s ago" : "";
                    break;
                }
            }
        }

        /// <summary>The census's area test: within the radius (r2, squared) of at, or - wide - in the defended area of a base attack.</summary>
        private static bool Inside(Vector3 at, bool wide, float r2, Vector3 p)
        {
            Vector3 d = p - at;
            return wide ? Mathf.Abs(d.y) <= WideHalfHeight && ScBaseAttack.InDefenceArea(p, ScBaseAttack.LeashExtra) : d.sqrMagnitude <= r2;
        }

        /// <summary>
        /// Round k1: a light postfix on vanilla's EntityAlive.ProcessDamageResponseLocal - every landed hit on the host (its own, and
        /// a client's through NetPackageDamageEntity; the overrides in EntityPlayerLocal, EntityHuman, EntityZombieCop and SCore's
        /// NPCs call it): a hit whose source is a player is kept for that player (NotePlayerHit) for NoteFighting. Nothing else is touched;
        /// nothing at all with TeamMelee off. Declared on EntityAlive (the method's own declaring type). The Fable review (F2b): only
        /// a hit on an enemy (EntityEnemy, or SCore's IsEnemy from the player's side), kept per player with where the victim stood.
        /// </summary>
        [HarmonyPatch(typeof(EntityAlive), nameof(EntityAlive.ProcessDamageResponseLocal))]
        public static class Patch_PlayerHit
        {
            [HarmonyPostfix]
            public static void Postfix(EntityAlive __instance, DamageResponse _dmResponse)
            {
                try
                {
                    if (!ScConfig.TeamMelee || __instance == null || __instance.isEntityRemote || __instance is EntityPlayer || __instance.world == null) return;
                    DamageSource src = _dmResponse.Source;
                    int id = src != null ? src.getEntityId() : -1;
                    EntityPlayer pl = id >= 0 && id != __instance.entityId ? __instance.world.GetEntity(id) as EntityPlayer : null;
                    if (pl == null) return;
                    // F2b: only a hit on an enemy (a zombie, a hostile animal, a raider) - the per-victim key used to be read only
                    // from the counted enemies; per player, the filter moves here
                    if (!(__instance is EntityEnemy) && !EntityTargetingUtilities.IsEnemy(pl, __instance)) return;
                    NotePlayerHit(__instance, id);
                }
                catch (Exception e)
                {
                    ScLog.Once("team-player-hit", e);
                }
            }
        }

        // ------------------------------------------------------------------ the count ----

        /// <summary>
        /// Counts the area round this member: F and T, RaiderFight, the nearest enemy. lines (the console) gets one line per
        /// counted entity, else null. No state of its own is written (the console runs it fresh).
        /// </summary>
        private static void Census(EntityAlive self, ref Tally t, List<string> lines)
        {
            World w = self.world;
            if (w == null) return;
            Vector3 at = self.position;
            float r = ScConfig.TeamRadius;
            // decision 4: during a live base attack, with her in the defended area, the whole area instead of the radius
            Bounds box;
            bool wide = false;
            if (ScBaseAttack.WideDefence && ScBaseAttack.InDefenceArea(at, ScBaseAttack.LeashExtra)
                && ScBaseAttack.DefenceBounds(at.y, WideHalfHeight, ScBaseAttack.LeashExtra, out box)) wide = true;
            else box = new Bounds(at, new Vector3(2f * r, 2f * r, 2f * r));
            t.Wide = wide;
            t.NearestD2 = float.MaxValue;
            near.Clear();
            w.GetEntitiesInBounds(typeof(EntityAlive), box, near);
            float r2 = r * r;
            bool detail = lines != null;
            float now = Time.time;
            fighting.Clear();
            others.Clear();
            try
            {
                // she always counts herself, wherever the sweep's edge falls
                float own = FriendWeight(self, self, detail, out string ownHow);
                t.F += own;
                t.Friends++;
                if (detail) lines.Add("  + " + Who(self) + " (" + (ScRetreat.Male(self) ? "himself" : "herself") + ") " + ownHow + " = " + F2(own));   // round wp2: the pronoun
                // round k1, the Fable review (F2a): the players who hit an enemy in this area lately - the only way a player counts
                NoteFighting(at, wide, r2, now, detail);
                for (int i = 0; i < near.Count; i++)
                {
                    EntityAlive a = near[i] as EntityAlive;
                    // a sleeper (a POI's sleeping zombie) has not joined the fight: neither friend nor foe
                    if (a == null || a == self || a.IsDead() || a.IsSleeping) continue;
#if SC_V34
                    if (ScDowned.IsDown(a)) continue;   // round U2 (risk 3): a downed body is out of the fight - neither a friend's strength nor a foe
#endif
                    Vector3 d = a.position - at;
                    if (!Inside(at, wide, r2, a.position)) continue;
                    if (!EntityTargetingUtilities.IsEnemy(self, a)) { others.Add(a); continue; }
                    float tw = Threat(a, detail, out string how);
                    t.T += tw;
                    t.Enemies++;
                    bool armed = Armed(a), unsafeMelee = MeleeUnsafe(a);
                    if (armed) t.RaiderFight = true;
                    if (unsafeMelee) t.Unsafe++;
                    if (d.sqrMagnitude < t.NearestD2) { t.NearestD2 = d.sqrMagnitude; t.Nearest = a; }
                    if (detail)
                        lines.Add("  - " + Who(a) + " at " + d.magnitude.ToString("0.#", CultureInfo.InvariantCulture) + " m: " + how + " = " + F2(tw)
                                  + (armed ? " [ARMED: raider fight]" : "") + (unsafeMelee ? " [melee-unsafe: gun]" : ""));
                }
                // then everyone else inside the area
                for (int i = 0; i < others.Count; i++)
                {
                    EntityAlive a = others[i];
                    float fw = FriendWeight(self, a, detail, out string fhow);
                    if (fhow == null) continue;                             // neither friend nor foe (a trader, a stranger's pet)
                    if (a is EntityPlayer)
                    {
                        t.PlayersIn++;
                        if (fighting.ContainsKey(a.entityId)) t.PlayersFighting++;
                    }
                    t.F += fw;
                    t.Friends++;
                    if (detail) lines.Add("  + " + Who(a) + " at " + (a.position - at).magnitude.ToString("0.#", CultureInfo.InvariantCulture) + " m: " + fhow + " = " + F2(fw));
                }
            }
            finally
            {
                near.Clear();
                others.Clear();
                fighting.Clear();
            }
        }

        /// <summary>
        /// One friend's strength, with how it was reached (detail) - how is null for an entity that is no friend at all
        /// (not counted). A member's body: her stars, health and weapon (or the guard's weight); the player (round k1: only while
        /// he is fighting - Census's fighting scratch, filled from the area's enemies first); another ally.
        /// </summary>
        private static float FriendWeight(EntityAlive self, EntityAlive a, bool detail, out string how)
        {
            how = null;
            float hp = HealthShare(a);
            float hpF = HpF(hp);
            if (a is EntityPlayer)
            {
                // round k1 (the user's decision): a player standing by while the members fight counts 0
                string fight;
                if (!fighting.TryGetValue(a.entityId, out fight))
                {
                    how = detail ? "the player, NOT fighting (no hit of yours on an enemy in the area in the last "
                                   + F2(ScConfig.TeamPlayerFightingSeconds) + " s)" : "";
                    return 0f;
                }
                how = detail ? "the player, fighting (" + fight + "): " + F2(ScConfig.TeamPlayerWeight) + " x HpF " + F2(hpF) + " (hp " + Pct(hp) + ")" : "";
                return ScConfig.TeamPlayerWeight * hpF;
            }
            Member m = ScXnpc.IsBody(a) && !ScXnpc.IsRaider(a) ? MemberOf(a) : null;
            if (m != null)
            {
#if SC_V34
                if (ScSleep.IsAsleep(a)) { how = detail ? "member asleep in bed" : ""; return 0f; }
#endif
                // round rt1 (I11, decision 11): another member sheltering (and not fighting back) is not in the fight - the ones
                // still fighting are not told it is matched on her strength. She counts herself as before in her own verdict:
                // a Cowardly member's "hangs back" (her way out of the shelter) is not made harder by her hiding.
                if (a != self && ScRetreat.Hiding(a)) { how = detail ? "member sheltering (round rt1)" : ""; return 0f; }
                float wgt;
                if (ScWatch.OnWatch(m))
                {
                    wgt = ScConfig.TeamGuardWeight * hpF;
                    if (detail) how = "member on watch " + F2(ScConfig.TeamGuardWeight) + " x HpF " + F2(hpF) + " (hp " + Pct(hp) + ")";
                }
                else
                {
                    int stars = m.Stars(Skill.Fighting);
                    Loadout l = ScLoadout.Current(a);
                    bool melee = l != null && !string.IsNullOrEmpty(l.Melee), ranged = l != null && l.HasRanged;
                    float wf = melee ? ScConfig.TeamWeaponMelee : ranged ? ScConfig.TeamWeaponRanged : ScConfig.TeamWeaponBare;
                    // round wp1 (decision 3; the user: "Mia's fighting skill is 4, she still shoots 1 lone zombie"): with a melee weapon
                    // in her pair each Fighting star above TeamStarBonusFrom adds TeamStarBonus to her weight (Fighting 4: x 1.5, so
                    // 1.45 becomes 2.18 and two walkers are matched); at or below it, and without a melee weapon, as before
                    float sb = melee ? 1f + ScConfig.TeamStarBonus * Math.Max(0, stars - ScConfig.TeamStarBonusFrom) : 1f;
                    wgt = (ScConfig.TeamStarBase + ScConfig.TeamStarStep * stars) * sb * hpF * wf;
                    if (detail) how = "member " + stars + "* (" + F2(ScConfig.TeamStarBase + ScConfig.TeamStarStep * stars) + ")"
                                      + (sb > 1f ? " x Fighting above " + ScConfig.TeamStarBonusFrom + "* " + F2(sb) : "") + " x HpF " + F2(hpF) + " (hp " + Pct(hp) + ") x "
                                      + (melee ? "melee in pair " : ranged ? "ranged only " : "no weapon ") + F2(wf);
                }
                if (ScMedic.IsUsing(a.entityId))
                {
                    wgt *= ScConfig.TeamMedicalMult;
                    if (detail) how += " x medical use " + F2(ScConfig.TeamMedicalMult);
                }
                if (how == null) how = "";
                return wgt;
            }
            // another ally: an NPC (never a vanilla trader, nor a junk drone) that is no enemy of hers - a hire of a player,
            // or a friend by SCore's rule (her faction's)
            if (!(a is EntityNPC) || a is EntityDrone || a.HasAnyTags(TraderTag) || ScXnpc.IsRaider(a) || a.IsSleeping) return 0f;
            if (!(EntityUtilities.GetLeaderOrOwner(a.entityId) is EntityPlayer) && !EntityTargetingUtilities.IsFriend(self, a)) return 0f;
            how = detail ? "ally " + F2(ScConfig.TeamAllyWeight) + " x HpF " + F2(hpF) + " (hp " + Pct(hp) + ")" : "";
            return ScConfig.TeamAllyWeight * hpF;
        }

        /// <summary>
        /// One enemy's threat: max(tag weight, health weight) x the final multipliers. The tag weight is the highest threat
        /// table entry whose tags it all carries (TeamThreatDefault when none does) times every tag multiplier it matches;
        /// the health weight is clamp((MaxHealth / TeamThreatHpBase) ^ TeamThreatHpPower, min, max).
        /// </summary>
        public static float Threat(EntityAlive a, bool detail, out string how)
        {
            how = null;
            float tag = -1f;
            string which = null;
            foreach (ScConfig.TeamTagEntry en in ScConfig.TeamThreats())
                if (en.Value > tag && a.HasAllTags(en.Tags)) { tag = en.Value; which = en.Text; }
            if (tag < 0f) { tag = ScConfig.TeamThreatDefault; which = "no table entry"; }
            string mults = "";
            foreach (ScConfig.TeamTagEntry en in ScConfig.TeamTagMults())
                if (a.HasAllTags(en.Tags)) { tag *= en.Value; if (detail) mults += " x" + en.Text + " " + F2(en.Value); }
            int max = Math.Max(1, a.GetMaxHealth());
            float hpW = Mathf.Clamp(Mathf.Pow(max / Math.Max(1f, ScConfig.TeamThreatHpBase), ScConfig.TeamThreatHpPower), ScConfig.TeamThreatHpMin, ScConfig.TeamThreatHpMax);
            float wgt = Math.Max(tag, hpW);
            string fin = "";
            foreach (ScConfig.TeamTagEntry en in ScConfig.TeamMults())
                if (a.HasAllTags(en.Tags)) { wgt *= en.Value; if (detail) fin += " x" + en.Text + " " + F2(en.Value); }
            if (detail) how = "tags " + which + mults + " = " + F2(tag) + ", hp " + max + " = " + F2(hpW) + fin;
            return wgt;
        }

        // ------------------------------------------------------------------ the target rules ----

        /// <summary>
        /// Never meleed on the team rule (decision 10): EntityZombieCop - vanilla's cops and its demolition zombie, both blow
        /// up or spit - or a TeamGunTags tag (zombieCop by default).
        /// </summary>
        public static bool MeleeUnsafe(EntityAlive t)
        {
            if (t == null) return false;
            if (t is EntityZombieCop) return true;
            FastTags<TagGroup.Global> tags = ScConfig.TeamGunTagSet();
            return !tags.IsEmpty && t.HasAnyTags(tags);
        }

        /// <summary>
        /// An armed enemy: a player, any NPC (a raider, 0-XNPCCore's bandits, a hostile survivor), vanilla's bandit (an
        /// EntityEnemy), or anything else but a zombie or an animal holding a ranged weapon (a cop's spit is not a gun).
        /// </summary>
        public static bool Armed(EntityAlive t)
        {
            if (t == null) return false;
            if (t is EntityPlayer || t is EntityNPC || t is EntityBandit) return true;
            if (t is EntityEnemy || t is EntityAnimal) return false;
            ItemValue held = t.inventory != null ? t.inventory.holdingItemItemValue : null;
            ItemClass ic = held != null && !held.IsEmpty() ? held.ItemClass : null;
            return ic != null && ic.Actions != null && ic.Actions.Length > 0 && ic.Actions[0] is ItemActionRanged;
        }

        /// <summary>
        /// Why this target needs the gun, or null: melee-unsafe, armed, past her leash (a resident: the Fable review, finding
        /// 3), or out of melee reach (ScArms.ReachWhy; onMelee: she holds her melee weapon, so a missing path means
        /// something). remember (X5 only) keeps an out-of-reach finding for TeamUnreachableSeconds; the console passes false.
        /// </summary>
        public static string NeedsGun(EntityAlive self, EntityAlive t, bool onMelee, bool remember)
        {
            GunNeed kind;
            return NeedsGun(self, t, onMelee, remember, out kind);
        }

        public static string NeedsGun(EntityAlive self, EntityAlive t, bool onMelee, bool remember, out GunNeed kind)
        {
            kind = GunNeed.None;
            if (self == null || t == null) return null;
            if (MeleeUnsafe(t)) { kind = GunNeed.Unsafe; return "melee-unsafe (" + t.EntityName + ")"; }
            if (Armed(t)) { kind = GunNeed.Armed; return "armed (" + t.EntityName + ")"; }
            // the Fable review (finding 3): a resident melee-led against a zombie she could only reach by crossing ScReturn's
            // leash walked out, was pulled back (5.5 over MeleeApproach's 5), and walked out again - the gun for it instead
            if (UAI.UAIConsiderationScFarFromPost.PastLeash(self, t.position)) { kind = GunNeed.Leash; return "past her leash (ScReturn would walk her back)"; }
            string why = ScArms.ReachWhy(self, t, onMelee, remember);
            if (why != null) kind = GunNeed.Reach;
            return why;
        }

        // ------------------------------------------------------------------ pure reads ----

        /// <summary>The verdict while it is fresh (TeamFreshSeconds) and the rule is on, else null.</summary>
        private static Verdict Fresh(EntityAlive e)
        {
            if (e == null || !ScConfig.TeamMelee) return null;
            Verdict v;
            if (!verdicts.TryGetValue(e.entityId, out v) || v.At <= 0f || Time.time - v.At > ScConfig.TeamFreshSeconds) return null;
            return v;
        }

        /// <summary>Is this member melee-led right now (see the class summary)? A pure read.</summary>
        public static bool MeleeLed(EntityAlive e)
        {
            Verdict v = Fresh(e);
            return v != null && v.MeleeLed;
        }

        /// <summary>Round wp2 (1a): the way back to melee sits at least this far under RegenCapShare, so regeneration alone reaches it.</summary>
        private const float RegenLineMargin = 0.05f;
        /// <summary>Round wp2 (review WP2-m1): the least gap between the two hurt lines once the regeneration cap moved the second.</summary>
        private const float MinHurtGap = 0.05f;

        /// <summary>
        /// Round wp2 (1; the user: "Mia is still using a shotgun against few zombies. her Fighting skill is greater than her shooting
        /// skill"): a member's own hurt lines. Off (not melee-led) below off, melee again from on. (b) Each Fighting star above
        /// TeamStarBonusFrom lowers both by MeleeHurtPerStar: off = max(MeleeHurtFloor, TeamLowHealth - per x stars), on = off +
        /// (TeamLowHealthBack - TeamLowHealth) - 60% / 65% at Fighting 1-2, 45% / 50% at 5, 35% / 40% at most. (a) The bug fix: on
        /// is at most RegenCapShare - RegenLineMargin while regeneration runs (it stops at exactly the cap, 70% since hr1, which was
        /// the old line: after any hit she stayed on the gun until a medical item), and off never above on. Her own stars only. A read.
        /// </summary>
        internal static void HurtLines(Member m, out float off, out float on)
        {
            int above = m != null ? Math.Max(0, m.Stars(Skill.Fighting) - ScConfig.TeamStarBonusFrom) : 0;
            float gap = Mathf.Max(0f, ScConfig.TeamLowHealthBack - ScConfig.TeamLowHealth);
            off = Mathf.Max(Mathf.Min(ScConfig.MeleeHurtFloor, ScConfig.TeamLowHealth), ScConfig.TeamLowHealth - ScConfig.MeleeHurtPerStar * above);
            on = off + gap;
            if (ScConfig.RegenOutOfCombat && ScConfig.RegenRatePerSecond > 0f && ScConfig.RegenCapShare > 0f)
            {
                float cap = Mathf.Max(0f, ScConfig.RegenCapShare - RegenLineMargin);
                if (on > cap)
                {
                    on = cap;
                    // round wp2 (review WP2-m1): the cap moved the way back - keep a gap of at least MinHurtGap under it (the off line
                    // comes down), so the two lines never collapse into one; warned once per world for such a config
                    float keep = Mathf.Max(gap, MinHurtGap);
                    if (on - off < keep)
                    {
                        off = Mathf.Max(0f, on - keep);
                        ScLog.Once("hurt-lines-cap", "config: RegenCapShare " + ScConfig.RegenCapShare.ToString("0.##", CultureInfo.InvariantCulture) + " holds the melee-again line at "
                                   + on.ToString("0.##", CultureInfo.InvariantCulture) + ", under TeamLowHealthBack; the off line is lowered to "
                                   + off.ToString("0.##", CultureInfo.InvariantCulture) + " to keep a " + keep.ToString("0.##", CultureInfo.InvariantCulture)
                                   + " gap (round wp2) - raise RegenCapShare or lower TeamLowHealth / TeamLowHealthBack");
                    }
                }
            }
            if (off > on) off = on;
        }

        /// <summary>Round wp1 (decision 2): her fight turned matched again within the last seconds and still is. A pure read.</summary>
        internal static bool MatchedAgainWithin(EntityAlive e, float seconds)
        {
            Verdict v = Fresh(e);
            return v != null && v.Matched && v.MatchedAgainAt > 0f && Time.time - v.MatchedAgainAt <= seconds;
        }

        /// <summary>No recount below the leave margin is pending: a fight about to stop being matched is not the moment to take the melee weapon.</summary>
        public static bool Steady(EntityAlive e)
        {
            Verdict v = Fresh(e);
            return v != null && v.Below == 0;
        }

        /// <summary>
        /// A matched fight with no raiders in it (a fresh verdict) and a gun with rounds in her pair: a Cowardly member hangs
        /// back with her gun instead of running (decision 8; ScNotTeamMatched on ScFlee / ScEncFlee). False with the rule off,
        /// no verdict (enclave people, strangers), or no gun to hang back with (the Fable review: then she runs, as before).
        /// </summary>
        public static bool HangsBack(EntityAlive e)
        {
            Verdict v = Fresh(e);
            return v != null && v.Matched && !v.RaiderFight && v.Gun;
        }

        /// <summary>
        /// Round k4 (ScPosture): why this member is not melee-led on her fresh verdict, or null when she is - "no team verdict
        /// yet" with none (a new body, a stale one, TeamMelee off). A pure read.
        /// </summary>
        internal static string NotLedWhy(EntityAlive e)
        {
            Verdict v = Fresh(e);
            if (v == null) return ScConfig.TeamMelee ? "no team verdict yet" : "team strength off (TeamMelee false)";
            return v.MeleeLed ? null : v.Why;
        }

        /// <summary>
        /// Round fd2 (the user's marksman rule: "a lone enemy, or a fight she isn't outmatched in"): her fresh verdict leaves
        /// her not melee-led on the count alone ("outmatched" is WhyNotLed's last reason, so nothing about her, her orders,
        /// her health or armed enemies came first) and there is a single enemy in it. A pure read.
        /// </summary>
        internal static bool OutmatchedByLoneEnemy(EntityAlive e)
        {
            Verdict v = Fresh(e);
            return v != null && !v.MeleeLed && !v.Matched && !v.LowHp && !v.RaiderFight && v.Enemies == 1
                   && v.Why != null && v.Why.StartsWith("outmatched", StringComparison.Ordinal);
        }

        /// <summary>Round k4: the fresh verdict in a few words, for the posture's why ("a matched fight (F 3.2 / T 2)"). A pure read.</summary>
        internal static string MatchedText(EntityAlive e)
        {
            Verdict v = Fresh(e);
            if (v == null) return "no verdict";
            return v.T <= 0f ? "no enemy counted (matched)" : (v.Matched ? "a matched fight" : "an outmatched fight") + " (F " + F2(v.F) + " / T " + F2(v.T) + ")";
        }

        /// <summary>
        /// Her fight's target (round tm1's MeleeFirst, round k4's posture): her attack target, else her revenge target, else the
        /// nearest enemy of the last count while it is alive, awake, still in the area, in her sight and one she may engage at
        /// all (round k4: not one round n08's home rule leaves alone - a wanderer that is after nobody). A read: the sight ray is
        /// SCore's own.
        /// </summary>
        public static EntityAlive Target(EntityAlive e)
        {
            if (e == null) return null;
            // round wp1 (review WP1-M2): her own target is no fight target while round n08's rule leaves it alone - MayEngage is 0
            // for its approach, its swing and its shot, so a fight posture on it only left her standing with her weapon out (and
            // ScInvestigate read her as "in another fight"). Within EngageWanderersWithin inside the zone LeavesAlone is false.
            // Round wp1 (re-check WP1R-n4): the attack target and the revenge target each on its own - a left-alone attack target
            // no longer hides a zombie that just hit her
            EntityAlive own = OwnFight(e, e.GetAttackTarget());
            if (own == null) own = OwnFight(e, e.GetRevengeTarget());
            // the Fable review: her own target outside the counted area (one she shoots from afar) gives way to the nearest
            // counted enemy she sees - else she would wait on the far one while a walker comes in next to her
            if (own != null && InArea(e, own)) return own;
            Verdict v = Fresh(e);
            if (v == null || v.Nearest < 0 || e.world == null) return own;
            EntityAlive t = e.world.GetEntity(v.Nearest) as EntityAlive;
            if (t == null || t.IsDead() || t.IsSleeping || t == own || !InArea(e, t)) return own;
            if (UAI.UAIConsiderationScIntruder.LeavesAlone(e, t)) return own;
            return UAI.SCoreUtils.CanSee(e, t) ? t : own;
        }

        /// <summary>Round wp1 (re-check WP1R-n4): a living target of hers that round n08 does not leave her alone with, else null. A read.</summary>
        internal static EntityAlive OwnFight(EntityAlive e, EntityAlive t)
        {
            return t != null && !t.IsDead() && !UAI.UAIConsiderationScIntruder.LeavesAlone(e, t) ? t : null;
        }

        /// <summary>
        /// Inside the area her verdict counts (TeamRadius, or the defended area during a live base attack): an enemy farther
        /// out was not weighed, so it is not her fight's target while a counted one is in her sight (Target). A read.
        /// </summary>
        public static bool InArea(EntityAlive e, EntityAlive t)
        {
            if (e == null || t == null) return false;
            Verdict v = Fresh(e);
            if (v != null && v.Wide) return ScBaseAttack.InDefenceArea(t.position, ScBaseAttack.LeashExtra);
            return (t.position - e.position).sqrMagnitude <= ScConfig.TeamRadius * ScConfig.TeamRadius;
        }

        // ------------------------------------------------------------------ helpers ----

        /// <summary>The member a body is (a stand-in fights as the member it stands in for), or null.</summary>
        internal static Member MemberOf(EntityAlive e)
        {
            if (e == null || !ScRegistry.Loaded) return null;
            Member m = ScRegistry.GetMember(e.entityId);
            if (m == null && e.Buffs != null && e.Buffs.HasCustomVar(ScRadio.StandInCvar)) m = ScRegistry.GetMember((int)e.Buffs.GetCustomVar(ScRadio.StandInCvar));
            return m;
        }

        /// <summary>
        /// HpF = TeamHealthFloor + (1 - TeamHealthFloor) x min(1, share / TeamHealthFullAt): 0.5 at no health, 1 from 90% of
        /// it (the Fable review: a scratch does not count - decision 2 for a healthy 1-star member).
        /// </summary>
        private static float HpF(float share)
        {
            float full = Mathf.Max(0.01f, ScConfig.TeamHealthFullAt);
            return ScConfig.TeamHealthFloor + (1f - ScConfig.TeamHealthFloor) * Mathf.Min(1f, share / full);
        }

        private static float HealthShare(EntityAlive a)
        {
            int max = a.GetMaxHealth();
            return max > 0 ? Mathf.Clamp01((float)a.Health / max) : 1f;
        }

        private static string Who(EntityAlive a)
        {
            if (a is EntityPlayer) return "you";
            return (ScXnpc.IsBody(a) ? ScCommunity.DisplayName(a) : a.EntityName) + " (" + a.entityId + ")";
        }

        private static string F2(float f) { return f.ToString("0.##", CultureInfo.InvariantCulture); }
        private static string Pct(float f) { return (f * 100f).ToString("0", CultureInfo.InvariantCulture) + "%"; }

        // ------------------------------------------------------------------ console ----

        /// <summary>sc team: one line per loaded member body (her verdict, then her posture), and the intruders (round k4).</summary>
        public static void ListLines(List<string> lines)
        {
            lines.Add("[SC] team strength (round tm1, margins round k3): " + (ScConfig.TeamMelee ? "on" : "OFF (TeamMelee=false: no MELEE posture)") + "; matched at F >= "
                      + F2(ScConfig.TeamMargin) + " T (left below " + F2(ScConfig.TeamLeaveMargin) + " T), melee-led from " + Pct(ScConfig.TeamLowHealth) + " health (again from "
                      + Pct(ScConfig.TeamLowHealthBack) + "; round wp2: both " + Pct(ScConfig.MeleeHurtPerStar) + " lower per Fighting star above "
                      + ScConfig.TeamStarBonusFrom + ", the first down to " + Pct(ScConfig.MeleeHurtFloor) + ", the second under the regeneration cap); " + Report());
            lines.Add("[SC] you count " + F2(ScConfig.TeamPlayerWeight) + " only while you are fighting (round k1; only your hits count): you hit an enemy in a member's counted area "
                      + "in the last " + F2(ScConfig.TeamPlayerFightingSeconds) + " s (TeamPlayerFightingSeconds; a kill counts too) - an enemy merely after you does not; standing by, you count 0");
            lines.Add("[SC] postures (round k4): " + ScPosture.Report());
            if (!ScRegistry.Loaded) return;
            float now = Time.time;
            foreach (Member m in ScRegistry.Members.Values)
            {
                EntityAlive e = ScCommunity.FindNpc(m.Id);
                if (e == null) continue;
                Verdict v;
                string verdict = !verdicts.TryGetValue(e.entityId, out v) || v.At <= 0f ? "no verdict yet"
                    : "F " + F2(v.F) + " / T " + F2(v.T) + " " + (v.Matched ? "matched" : "OUTMATCHED") + (v.RaiderFight ? " (raider fight)" : "") + PlayerNote(v)
                      + " - " + (v.MeleeLed ? "MELEE-LED" : "not melee-led: " + v.Why) + ", " + (now - v.At).ToString("0.0", CultureInfo.InvariantCulture) + " s ago"
                      + (now - v.At > ScConfig.TeamFreshSeconds ? " (STALE)" : "");
                lines.Add("[SC]   " + m.Name + " (" + e.entityId + "): " + verdict);
                lines.Add("[SC]     " + ScPosture.Line(e));
            }
            ScInvestigate.ListLines(lines);
        }

        /// <summary>Round k1: what the players in her area counted in this verdict ("" with none in it).</summary>
        private static string PlayerNote(Verdict v)
        {
            if (v == null || v.PlayersIn <= 0) return "";
            if (v.PlayersFighting <= 0) return "; you count 0 (not fighting)";
            return "; you count " + F2(ScConfig.TeamPlayerWeight) + " x HpF (fighting)" + (v.PlayersIn > v.PlayersFighting ? ", " + (v.PlayersIn - v.PlayersFighting) + " other player(s) 0" : "");
        }

        /// <summary>sc team &lt;id|name&gt;: the verdict, its reasons, a fresh count with every counted entity, her target and the swap state.</summary>
        public static void Describe(EntityAlive e, List<string> lines)
        {
            Member m = MemberOf(e);
            lines.Add("[SC] " + ScCommunity.DisplayName(e) + " (id " + e.entityId + ") - team strength " + (ScConfig.TeamMelee ? "on" : "OFF (TeamMelee=false: nothing below acts)")
                      + (m == null ? "; not a member: no verdict (enclave people and strangers are not in this round)" : ""));
            if (!ScXnpc.IsBody(e) || ScXnpc.IsRaider(e)) { lines.Add("[SC]   not one of the community's 0-XNPCCore survivors"); return; }
            float now = Time.time;
            Verdict v;
            if (verdicts.TryGetValue(e.entityId, out v) && v.At > 0f)
            {
                lines.Add("[SC]   verdict (" + (now - v.At).ToString("0.0", CultureInfo.InvariantCulture) + " s ago" + (now - v.At > ScConfig.TeamFreshSeconds ? ", STALE - counts as none" : "")
                          + "): F " + F2(v.F) + " / T " + F2(v.T) + " - " + (v.Matched ? "MATCHED" : "OUTMATCHED")
                          + (v.Below > 0 ? " (" + v.Below + " of " + ScConfig.TeamLeaveRecounts + " recounts below " + F2(ScConfig.TeamLeaveMargin) + " T)" : "")
                          + (v.Above > 0 ? " (" + v.Above + " of " + ScConfig.TeamLeaveRecounts + " recounts at " + F2(ScConfig.TeamMargin) + " T to match again)" : "")
                          + "; " + v.Friends + " friend(s), " + v.Enemies + " enemy(ies)" + (v.Unsafe > 0 ? ", " + v.Unsafe + " melee-unsafe" : "")
                          + (v.RaiderFight ? ", RAIDER FIGHT" : "") + (v.Wide ? "; counted over the defended area (base attack)" : "; within " + F2(ScConfig.TeamRadius) + " m")
                          + (v.LowHp ? "; LOW HEALTH" : "") + PlayerNote(v));
                lines.Add("[SC]   " + (v.MeleeLed ? "MELEE-LED: the MELEE posture unless her target needs the gun, she is overwhelmed, or her gun stays (below)"
                                                    : "not melee-led: " + v.Why + " - KITE with a gun that has rounds (round k4)"));
            }
            else lines.Add("[SC]   no verdict yet" + (m != null && ScConfig.TeamMelee ? " (the first count comes within a second of her close-range tick)" : ""));
            if (m != null) ScPosture.DescribeLines(e, lines);                        // round k4: her posture, why, her hand and its clock
            var t = new Tally();
            var counted = new List<string>();
            Census(e, ref t, counted);
            lines.Add("[SC]   counted now: F " + F2(t.F) + " / T " + F2(t.T) + " -> " + (t.T <= 0f ? "no enemies (matched)"
                      : t.F + Eps >= ScConfig.TeamMargin * t.T ? "matched (F >= " + F2(ScConfig.TeamMargin) + " T)"
                      : t.F + Eps < ScConfig.TeamLeaveMargin * t.T ? "outmatched (F < " + F2(ScConfig.TeamLeaveMargin) + " T)"
                      : "between the margins (a matched fight stays matched, a new one is outmatched)")
                      + (t.RaiderFight ? ", raider fight" : ""));
            foreach (string s in counted) lines.Add("[SC]" + s);
            EntityAlive target = Target(e);
            lines.Add("[SC]   target: " + (target == null ? "none" : Who(target) + " at " + (target.position - e.position).magnitude.ToString("0.#", CultureInfo.InvariantCulture) + " m - "
                      + (NeedsGun(e, target, IsHoldingMelee(e), false) ?? "safe to melee and in reach")));
            if (m == null) lines.Add("[SC]   hand: " + ScArms.HandState(e));
        }

        private static bool IsHoldingMelee(EntityAlive e)
        {
            ItemValue held = e.inventory != null ? e.inventory.holdingItemItemValue : null;
            ItemClass ic = held != null && !held.IsEmpty() ? held.ItemClass : null;
            return ic != null && ScAmmo.RangedAction(ic) == null;
        }

        /// <summary>
        /// sc team auto: the player's weapon orders off (sc_order_melee, sc_order_ranged) - she chooses by the rules again. Round nc1
        /// (D4): 0-XNPCCore's weapon toggle with them (SdcsWeaponPref to none), since the toggle is an order of SC's now.
        /// </summary>
        public static string ClearOrders(EntityAlive e)
        {
            if (e == null || e.Buffs == null) return "no body";
            bool any = false;
            if (e.Buffs.HasCustomVar(ScArms.OrderedMeleeCvar)) { e.Buffs.RemoveCustomVar(ScArms.OrderedMeleeCvar); any = true; }
            if (e.Buffs.HasCustomVar(ScArms.OrderedRangedCvar)) { e.Buffs.RemoveCustomVar(ScArms.OrderedRangedCvar); any = true; }
            if (ScArms.PrefKind(e) != 0) { ScArms.SetPref(e, 0); any = true; }
            return ScCommunity.DisplayName(e) + (any ? ": your weapon order is cleared - she picks her weapon by the rules again" : " had no weapon order from you");
        }

        /// <summary>One line for sc xnpc &lt;id&gt;.</summary>
        public static string Line(EntityAlive e)
        {
            if (!ScConfig.TeamMelee) return "team strength off (TeamMelee=false)";
            Verdict v;
            if (e == null || !verdicts.TryGetValue(e.entityId, out v) || v.At <= 0f) return "team strength: no verdict (not a member, or not counted yet)";
            float age = Time.time - v.At;
            return "team strength: F " + F2(v.F) + " / T " + F2(v.T) + " " + (v.Matched ? "matched" : "OUTMATCHED") + PlayerNote(v) + ", "
                   + (v.MeleeLed ? "MELEE-LED" : "not melee-led (" + v.Why + ")") + ", " + age.ToString("0.0", CultureInfo.InvariantCulture) + " s ago"
                   + (age > ScConfig.TeamFreshSeconds ? " (STALE)" : "") + " (sc team " + e.entityId + " for the count and her posture)";
        }

        /// <summary>The totals for sc team and sc xnpc.</summary>
        public static string Report()
        {
            float now = Time.time;
            int fresh = 0, matched = 0, led = 0;
            foreach (var kv in verdicts)
            {
                Verdict v = kv.Value;
                if (v.At <= 0f || now - v.At > ScConfig.TeamFreshSeconds) continue;
                fresh++;
                if (v.Matched) matched++;
                if (v.MeleeLed) led++;
            }
            return fresh + " fresh verdict(s): " + matched + " matched, " + (fresh - matched) + " outmatched, " + led + " melee-led; "
                   + recounts + " recount(s), " + outmatched + " fight(s) turned outmatched, " + matchedAgain + " matched again this session; "
                   + playerHitsNoted + " player hit(s) noted (round k1; kept for " + playerHits.Count + " player(s)) (counters x5-team-* in sc xnpc)";
        }
    }
}
