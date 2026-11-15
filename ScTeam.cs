using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
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
    /// from SC's X5 prefix on SdcsCloseRangeTick (Recount) and only READ everywhere else - the UAI considerations
    /// (ScNotTeamMelee, ScNotTeamMatched) and ScArms' MeleeFirst, GunFirst, CloseRange and IsOverwhelmed.
    ///
    ///   F (friendly strength) within TeamRadius (15 m; during a live base attack the whole defended area): a member
    ///     (TeamStarBase + TeamStarStep x Fighting stars) x HpF x her weapon factor (a melee weapon in her pair 1, ranged
    ///     only 0.8, nothing 0.5) - she counts herself; a member on watch TeamGuardWeight x HpF; any member in the middle of
    ///     a medical use x TeamMedicalMult; one asleep in bed 0; the player TeamPlayerWeight x HpF (decision 1); another
    ///     ally (a hire, a friendly NPC) TeamAllyWeight x HpF. HpF = 0.5 + 0.5 x health share.
    ///   T (threat): every awake enemy in the same area, whether or not it is after the team (decision 14):
    ///     max(tag weight, health weight) x the final multipliers (crawler 0.6). The tag weight is the highest entry of the
    ///     threat table it matches (walker 1, feral 2, radiated 3, charged 3.5, infernal 4.5, the animals) times the tag
    ///     multipliers (special 1.5, strong 1.3); the health weight covers tough and modded zombies.
    ///   Matched: F >= TeamMargin x T (1.0, decision 3); it stops being matched after TeamLeaveRecounts (2) recounts in a
    ///     row with F &lt; TeamLeaveMargin x T (0.8); no enemies = matched. A new fight (the first count with enemies) is
    ///     judged by F >= T alone - the leave margin is hysteresis inside a fight (the report's worked examples).
    ///   RaiderFight: an enemy NPC (a raider, a bandit, a hostile player) or an armed enemy in the area - today's rules.
    ///   Melee-led: TeamMelee on; a member's body, not a raider; not a runner on the move, not on watch, not Cowardly
    ///     (decision 8), no gun order from the player (sc_order_ranged, decision 9); a melee weapon in her pair; health at
    ///     least TeamLowHealth (50%; back from TeamLowHealthBack, 60%: decision 5); matched; no RaiderFight. Followers away
    ///     from home too (decision 11); enclave people and strangers have no verdict (decision 12).
    ///   NeedsGun(target): the target is melee-unsafe (a zombie cop or the demolition zombie: EntityZombieCop, or a
    ///     TeamGunTags tag - decision 10), armed, or out of melee reach (ScArms.ReachWhy). The team rule stands aside for
    ///     such a target: today's rules.
    /// </summary>
    public static class ScTeam
    {
        /// <summary>Slack on the F against T tests: 0.85 + 0.15 is 1.0000000298 in floats, and decision 2 is F 1.0 >= T 1.</summary>
        private const float Eps = 0.001f;
        /// <summary>A live base attack's sweep reaches this far above and below the member (the zone is flat on the map).</summary>
        private const float WideHalfHeight = 40f;

        private sealed class Verdict
        {
            /// <summary>When it was last counted (0 = not yet), when it is next due, the last X5 tick that asked.</summary>
            public float At, Next, Seen;
            public float F, T;
            /// <summary>No enemies = matched, so a fight starts matched and leaves it on the leave margin.</summary>
            public bool Matched = true;
            /// <summary>Recounts in a row with F below TeamLeaveMargin x T.</summary>
            public int Below;
            public bool LowHp, RaiderFight, Wide, MeleeLed;
            public int Enemies, Friends, Unsafe;
            /// <summary>The nearest counted enemy (MeleeFirst's target while she has none of her own), -1 none.</summary>
            public int Nearest = -1;
            /// <summary>Why she is not melee-led ("" when she is).</summary>
            public string Why = "";
        }

        /// <summary>One count over the area.</summary>
        private struct Tally
        {
            public float F, T;
            public bool RaiderFight, Wide;
            public int Enemies, Friends, Unsafe;
            public EntityAlive Nearest;
            public float NearestD2;
        }

        private static readonly Dictionary<int, Verdict> verdicts = new Dictionary<int, Verdict>();
        private static readonly List<Entity> near = new List<Entity>();
        private static float nextPrune;
        private static int recounts, outmatched, matchedAgain;
        private static readonly FastTags<TagGroup.Global> TraderTag = FastTags<TagGroup.Global>.Parse("trader");

        public static void Reset()
        {
            verdicts.Clear();
            near.Clear();
            nextPrune = 0f;
            recounts = outmatched = matchedAgain = 0;
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
                v.Nearest = t.Nearest != null ? t.Nearest.entityId : -1;
                if (t.T <= 0f || t.F + Eps >= ScConfig.TeamMargin * t.T) { v.Matched = true; v.Below = 0; }
                else if (newFight) { v.Matched = false; v.Below = 0; }                      // starts below F >= T: outmatched at once
                else if (t.F + Eps < ScConfig.TeamLeaveMargin * t.T)
                {
                    v.Below++;
                    if (v.Below >= ScConfig.TeamLeaveRecounts) v.Matched = false;
                }
                else v.Below = 0;                                                           // between the margins: as it was
                float hp = HealthShare(e);
                bool wasLow = v.LowHp;
                if (hp < ScConfig.TeamLowHealth) v.LowHp = true;
                else if (hp >= ScConfig.TeamLowHealthBack) v.LowHp = false;
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
                    matchedAgain++;
                    Patches_Xnpc.Count("x5-team-matched-again");
                    ScLog.Verbose(ScCommunity.DisplayName(e) + " (id " + e.entityId + "): the fight is matched again (F " + F2(t.F) + " >= T " + F2(t.T) + ")");
                }
                if (!wasLow && v.LowHp) Patches_Xnpc.Count("x5-team-low-health");
            }
            catch (Exception ex)
            {
                ScLog.Once("team-recount", ex);
            }
        }

        /// <summary>Why this member is not melee-led on this verdict, or null when she is (the order: what she is, then the fight).</summary>
        private static string WhyNotLed(EntityAlive e, Member m, Verdict v)
        {
            if (ScRunners.IsPhysical(m) && !m.Offline) return "on a run (her walk decides)";
            if (ScWatch.OnWatch(m)) return "on watch (a guard keeps her gun)";
            if (ScTraits.IsTimid(m)) return "Cowardly (hangs back with her gun; runs only when outmatched)";
            if (ScArms.OrderedRanged(e)) return "you ordered her gun (order her melee weapon, or sc team " + e.entityId + " auto, to undo)";
            Loadout l = ScLoadout.Current(e);
            if (l == null || string.IsNullOrEmpty(l.Melee)) return "no melee weapon in her pair";
            if (v.LowHp) return "hurt (" + Pct(HealthShare(e)) + " - melee again from " + Pct(ScConfig.TeamLowHealthBack) + ")";
            if (v.RaiderFight) return "armed enemies in the fight (raiders, bandits: today's rules)";
            if (!v.Matched) return "outmatched (F " + F2(v.F) + " vs T " + F2(v.T) + ": matched again at F >= " + F2(ScConfig.TeamMargin) + " T)";
            return null;
        }

        private static void Prune(float now)
        {
            nextPrune = now + 30f;
            var gone = new List<int>();
            foreach (var kv in verdicts) if (now - kv.Value.Seen > 60f) gone.Add(kv.Key);
            foreach (int id in gone) verdicts.Remove(id);
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
            try
            {
                // she always counts herself, wherever the sweep's edge falls
                float own = FriendWeight(self, self, lines != null, out string ownHow);
                t.F += own;
                t.Friends++;
                if (lines != null) lines.Add("  + " + Who(self) + " (herself) " + ownHow + " = " + F2(own));
                for (int i = 0; i < near.Count; i++)
                {
                    EntityAlive a = near[i] as EntityAlive;
                    // a sleeper (a POI's sleeping zombie) has not joined the fight: neither friend nor foe
                    if (a == null || a == self || a.IsDead() || a.IsSleeping) continue;
                    Vector3 d = a.position - at;
                    bool inside = wide ? Mathf.Abs(d.y) <= WideHalfHeight && ScBaseAttack.InDefenceArea(a.position, ScBaseAttack.LeashExtra)
                                       : d.sqrMagnitude <= r2;
                    if (!inside) continue;
                    if (EntityTargetingUtilities.IsEnemy(self, a))
                    {
                        float tw = Threat(a, lines != null, out string how);
                        t.T += tw;
                        t.Enemies++;
                        bool armed = Armed(a), unsafeMelee = MeleeUnsafe(a);
                        if (armed) t.RaiderFight = true;
                        if (unsafeMelee) t.Unsafe++;
                        if (d.sqrMagnitude < t.NearestD2) { t.NearestD2 = d.sqrMagnitude; t.Nearest = a; }
                        if (lines != null)
                            lines.Add("  - " + Who(a) + " at " + d.magnitude.ToString("0.#", CultureInfo.InvariantCulture) + " m: " + how + " = " + F2(tw)
                                      + (armed ? " [ARMED: raider fight]" : "") + (unsafeMelee ? " [melee-unsafe: gun]" : ""));
                        continue;
                    }
                    float fw = FriendWeight(self, a, lines != null, out string fhow);
                    if (fhow == null) continue;                             // neither friend nor foe (a trader, a stranger's pet)
                    t.F += fw;
                    t.Friends++;
                    if (lines != null) lines.Add("  + " + Who(a) + " at " + d.magnitude.ToString("0.#", CultureInfo.InvariantCulture) + " m: " + fhow + " = " + F2(fw));
                }
            }
            finally { near.Clear(); }
        }

        /// <summary>
        /// One friend's strength, with how it was reached (detail) - how is null for an entity that is no friend at all
        /// (not counted). A member's body: her stars, health and weapon (or the guard's weight); the player; another ally.
        /// </summary>
        private static float FriendWeight(EntityAlive self, EntityAlive a, bool detail, out string how)
        {
            how = null;
            float hp = HealthShare(a);
            float hpF = ScConfig.TeamHealthFloor + (1f - ScConfig.TeamHealthFloor) * hp;
            if (a is EntityPlayer)
            {
                how = detail ? "the player " + F2(ScConfig.TeamPlayerWeight) + " x HpF " + F2(hpF) + " (hp " + Pct(hp) + ")" : "";
                return ScConfig.TeamPlayerWeight * hpF;
            }
            Member m = ScXnpc.IsBody(a) && !ScXnpc.IsRaider(a) ? MemberOf(a) : null;
            if (m != null)
            {
#if SC_V34
                if (ScSleep.IsAsleep(a)) { how = detail ? "member asleep in bed" : ""; return 0f; }
#endif
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
                    wgt = (ScConfig.TeamStarBase + ScConfig.TeamStarStep * stars) * hpF * wf;
                    if (detail) how = "member " + stars + "* (" + F2(ScConfig.TeamStarBase + ScConfig.TeamStarStep * stars) + ") x HpF " + F2(hpF) + " (hp " + Pct(hp) + ") x "
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
        /// Why this target needs the gun, or null: melee-unsafe, armed, or out of melee reach (ScArms.ReachWhy; onMelee: she
        /// holds her melee weapon, so a missing path means something). remember (X5 only) keeps an out-of-reach finding for
        /// TeamUnreachableSeconds; the console passes false.
        /// </summary>
        public static string NeedsGun(EntityAlive self, EntityAlive t, bool onMelee, bool remember)
        {
            if (self == null || t == null) return null;
            if (MeleeUnsafe(t)) return "melee-unsafe (" + t.EntityName + ")";
            if (Armed(t)) return "armed (" + t.EntityName + ")";
            return ScArms.ReachWhy(self, t, onMelee, remember);
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

        /// <summary>No recount below the leave margin is pending: a fight about to stop being matched is not the moment to take the melee weapon.</summary>
        public static bool Steady(EntityAlive e)
        {
            Verdict v = Fresh(e);
            return v != null && v.Below == 0;
        }

        /// <summary>
        /// A matched fight with no raiders in it (a fresh verdict): a Cowardly member hangs back with her gun instead of running
        /// (decision 8; ScNotTeamMatched on ScFlee / ScEncFlee). False with the rule off, no verdict (enclave people, strangers).
        /// </summary>
        public static bool MatchedFight(EntityAlive e)
        {
            Verdict v = Fresh(e);
            return v != null && v.Matched && !v.RaiderFight;
        }

        /// <summary>
        /// I5 (ScNotTeamMelee on 0-XNPCCore's BackupKeepDistance and BackupRanged): she holds her ground against this enemy -
        /// melee-led, the enemy safe to melee and unarmed, not one she found out of reach, and the overwhelmed rule not holding
        /// her on the gun (then its back-off is today's). A pure read.
        /// </summary>
        public static bool HoldsGround(EntityAlive self, EntityAlive t)
        {
            if (self == null || t == null || !MeleeLed(self)) return false;
            if (MeleeUnsafe(t) || Armed(t)) return false;
            if (ScArms.RememberedOutOfReach(self, t)) return false;
            return !ScArms.HoldingGunUpClose(self);
        }

        /// <summary>
        /// MeleeFirst's target: her attack target, else her revenge target, else the nearest enemy of the last count while
        /// it is alive, awake, still in the area and in her sight (an enemy she could shoot - decision 13: no shooting while
        /// it comes in). A read: the sight ray is SCore's own.
        /// </summary>
        public static EntityAlive Target(EntityAlive e)
        {
            if (e == null) return null;
            EntityAlive t = e.GetAttackTarget();
            if (t != null && !t.IsDead()) return t;
            t = e.GetRevengeTarget();
            if (t != null && !t.IsDead()) return t;
            Verdict v = Fresh(e);
            if (v == null || v.Nearest < 0 || e.world == null) return null;
            t = e.world.GetEntity(v.Nearest) as EntityAlive;
            if (t == null || t.IsDead() || t.IsSleeping) return null;
            if (!v.Wide && (t.position - e.position).sqrMagnitude > ScConfig.TeamRadius * ScConfig.TeamRadius) return null;
            return UAI.SCoreUtils.CanSee(e, t) ? t : null;
        }

        /// <summary>
        /// Inside the area her verdict counts (TeamRadius, or the defended area during a live base attack): an enemy farther
        /// out was not weighed, so MeleeFirst does not take the melee weapon for it (and GunFirst, melee-led, does not draw
        /// for it either: she keeps what she holds until it comes in). A read.
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

        /// <summary>sc team: one line per loaded member body.</summary>
        public static void ListLines(List<string> lines)
        {
            lines.Add("[SC] team strength (round tm1): " + (ScConfig.TeamMelee ? "on" : "OFF (TeamMelee=false)") + "; " + Report());
            if (!ScRegistry.Loaded) return;
            float now = Time.time;
            foreach (Member m in ScRegistry.Members.Values)
            {
                EntityAlive e = ScCommunity.FindNpc(m.Id);
                if (e == null) continue;
                Verdict v;
                if (!verdicts.TryGetValue(e.entityId, out v) || v.At <= 0f) { lines.Add("[SC]   " + m.Name + " (" + e.entityId + "): no verdict yet"); continue; }
                lines.Add("[SC]   " + m.Name + " (" + e.entityId + "): F " + F2(v.F) + " / T " + F2(v.T) + " " + (v.Matched ? "matched" : "OUTMATCHED")
                          + (v.RaiderFight ? " (raider fight)" : "") + " - " + (v.MeleeLed ? "MELEE-LED" : "not melee-led: " + v.Why)
                          + ", " + (now - v.At).ToString("0.0", CultureInfo.InvariantCulture) + " s ago" + (now - v.At > ScConfig.TeamFreshSeconds ? " (STALE)" : ""));
            }
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
                          + "; " + v.Friends + " friend(s), " + v.Enemies + " enemy(ies)" + (v.Unsafe > 0 ? ", " + v.Unsafe + " melee-unsafe" : "")
                          + (v.RaiderFight ? ", RAIDER FIGHT" : "") + (v.Wide ? "; counted over the defended area (base attack)" : "; within " + F2(ScConfig.TeamRadius) + " m")
                          + (v.LowHp ? "; LOW HEALTH" : ""));
                lines.Add("[SC]   " + (v.MeleeLed ? "MELEE-LED: her melee weapon unless the target needs the gun" : "not melee-led: " + v.Why + " - today's weapon rules"));
            }
            else lines.Add("[SC]   no verdict yet" + (m != null && ScConfig.TeamMelee ? " (the first count comes within a second of her close-range tick)" : ""));
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
            lines.Add("[SC]   hand: " + ScArms.TeamState(e));
        }

        private static bool IsHoldingMelee(EntityAlive e)
        {
            ItemValue held = e.inventory != null ? e.inventory.holdingItemItemValue : null;
            ItemClass ic = held != null && !held.IsEmpty() ? held.ItemClass : null;
            return ic != null && ScAmmo.RangedAction(ic) == null;
        }

        /// <summary>sc team auto: the player's weapon orders off (sc_order_melee, sc_order_ranged) - she chooses by the rules again.</summary>
        public static string ClearOrders(EntityAlive e)
        {
            if (e == null || e.Buffs == null) return "no body";
            bool any = false;
            if (e.Buffs.HasCustomVar(ScArms.OrderedMeleeCvar)) { e.Buffs.RemoveCustomVar(ScArms.OrderedMeleeCvar); any = true; }
            if (e.Buffs.HasCustomVar(ScArms.OrderedRangedCvar)) { e.Buffs.RemoveCustomVar(ScArms.OrderedRangedCvar); any = true; }
            return ScCommunity.DisplayName(e) + (any ? ": your weapon order is cleared - she picks her weapon by the rules again" : " had no weapon order from you");
        }

        /// <summary>One line for sc xnpc &lt;id&gt;.</summary>
        public static string Line(EntityAlive e)
        {
            if (!ScConfig.TeamMelee) return "team strength off (TeamMelee=false)";
            Verdict v;
            if (e == null || !verdicts.TryGetValue(e.entityId, out v) || v.At <= 0f) return "team strength: no verdict (not a member, or not counted yet)";
            float age = Time.time - v.At;
            return "team strength: F " + F2(v.F) + " / T " + F2(v.T) + " " + (v.Matched ? "matched" : "OUTMATCHED") + ", "
                   + (v.MeleeLed ? "MELEE-LED" : "not melee-led (" + v.Why + ")") + ", " + age.ToString("0.0", CultureInfo.InvariantCulture) + " s ago"
                   + (age > ScConfig.TeamFreshSeconds ? " (STALE)" : "") + "; " + ScArms.TeamState(e) + " (sc team " + e.entityId + " for the count)";
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
                   + ScArms.TeamReport() + " (counters x5-team-* in sc xnpc)";
        }
    }
}
