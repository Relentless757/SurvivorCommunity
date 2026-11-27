using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round k2b (2026-11-15; the user, on the gap the Fable review of round k2 found: "They should back off and heal if they
    /// can for any situation"): back off, then heal.
    ///
    /// Round k2's hold (ScMedic.HeldByReach) starts no medical use while an enemy that is after her is within MedicSafeDistance
    /// (3 m) - right, a use costs 14-27 health in hits up close - but nothing moved her out of reach: a melee member between
    /// 60% and 25% never healed at all. Now, for one of the community's members (not a stand-in, a raider or a stranger):
    ///   - While ScMedic holds her from a use (WaitingToHeal: a heal at or below HealBelow, or a bleed), SCResident's
    ///     ScHealBackOff action (8.3, its consideration a pure read: Wanted) walks her away from the nearest enemy after her
    ///     (UAITaskHealBackOffSC): one of k1's points (ScBackOff.Pick: the BackOffSteps 10, 6, 3 m on seven directions, out of
    ///     reach of every enemy, a clear walk) at a run. At home the point is inside the base (round hl1's AtHome); a follower
    ///     away from home stays within StandOffFollowerLeash of her leader; anyone else away from home (a runner, a follower
    ///     with no leader near) takes the same steps (10, 6, 3 m) with no other bound, and her travel resumes after.
    ///   - Once nothing after her is within MedicSafeDistance + ClearMargin (4 m), the task stops and asks ScMedic for its check
    ///     at once (CheckSoon): the hold finds nothing in reach and the item goes into her hand. The action rests ClearHoldSeconds
    ///     meanwhile so the decision moves on.
    ///   - No room to back off at all (no step fits): the task marks this wait "heal now" (heal-now-cornered) and ScMedic uses
    ///     the item at the next frame with the enemy still there. The Fable review (F4): an enemy that keeps up (the nearest one
    ///     after her not GainNeed, 1 m, farther than GainSeconds, 2 s, before - the clock runs across the steps of one wait) is
    ///     the same: heal now (heal-now-gained), not 6 s of running first. Held MedicMaxWait without getting clear (the
    ///     back-off slowed, or kept from running by another action): ScMedic uses it as well (heal-now-timeout). Sheltering in
    ///     her room (rt1), on watch or on a Stay order she never backs off: ScMedic uses it at once (MayBackOff;
    ///     heal-now-shelter, heal-now-post). These replace round k2's MedicMaxWait / 25% exception for members.
    ///   - While the task walks her, SC's X5 tick holds the weapon swaps (round k4's hand rule, 0-XNPCCore's 3 m swap): the item
    ///     takes her hand next.
    ///   - Round nc1 (the user's decision D2; Tom healed 12 times in one fight, each heal-now zeroing 0-XNPCCore's attacks for about
    ///     2.5 s): no room to back off with her health above CorneredHealBelow (35%) is no heal-now any more - she keeps fighting
    ///     (heal-cornered-fights-on), the action stays out of her way and looks for room again every CorneredRecheckSeconds (4 s),
    ///     and MedicMaxWait's "anyway" waits too (CorneredFightsOn); at or below the line the next look heals her at once
    ///     (heal-now-cornered). The enemy keeping up, sheltering, on watch and a Stay order are not "cornered": as before.
    /// No flip faster than the item: a use starts ScMedic's 12 s cooldown, during which no hold (and so no back-off) can start;
    /// an episode that never gets her clear ends in a use after MedicMaxWait at the latest. HealBackOff false: none of this -
    /// round k2's hold as it was.
    /// State is written only by the task (and read by the consideration, ScMedic and the X5 tick). Main thread only.
    /// </summary>
    public static class ScHealBackOff
    {
        /// <summary>She is clear once nothing after her is within MedicSafeDistance + this (the hold's own test is MedicSafeDistance).</summary>
        public const float ClearMargin = 1f;
        /// <summary>After the task found her clear, the action rests this long: the next medical check (asked for at once) uses the item.</summary>
        private const float ClearHoldSeconds = 1f;
        /// <summary>A heal-now verdict holds this long for its wait (ScMedic's check runs the next frame).</summary>
        private const float HealNowSeconds = 3f;
        /// <summary>Walking (the X5 swap hold) needs the task's Update to have run this recently.</summary>
        private const float WalkFresh = 1f;
        /// <summary>The longest step, at home and away alike: k1's own back-off reaches 10 m (the steps are BackOffSteps, 10, 6 and 3 m).</summary>
        private const float MaxStep = 10f;
        private const float ArriveXZ = 1f;
        /// <summary>Two waits are the same one when their starts are this close (seconds).</summary>
        private const float SameWait = 0.05f;
        /// <summary>CurrentOrder 2 is SCore's Stay (the cvar 0-XNPCCore's actions are gated on).</summary>
        private const int OrderStay = 2;
        /// <summary>
        /// The Fable review (F4): every this many seconds of one wait's back-off the nearest enemy after her must be GainNeed
        /// farther than at the last check - else it keeps up with her and she heals now (heal-now-gained).
        /// </summary>
        private const float GainSeconds = 2f, GainNeed = 1f;

        internal sealed class State
        {
            /// <summary>The wait (ScMedic's heldSince) this episode belongs to, -1 none.</summary>
            public float WaitSince = -1f;
            /// <summary>Round t2 (review T2-m3): the wait whose "keeps shooting" line was logged, -1 none.</summary>
            public float ShootLoggedWait = -1f;
            /// <summary>This episode was counted (heal-backoff) / got her clear (heal-backoff-clear).</summary>
            public bool Counted, ClearCounted;
            /// <summary>The heal-now verdict: for the wait that began at HealNowWait, until HealNowUntil, and why.</summary>
            public float HealNowWait = -1f, HealNowUntil;
            public string HealNowWhy;
            /// <summary>The task found her clear: the action rests until then.</summary>
            public float ClearUntil;
            /// <summary>The task is walking her (stamped every Update), to where, how far and which way, within what bound.</summary>
            public bool Walking;
            public float WalkAt, Step, Angle;
            public Vector3 Point;
            public string Bound = "";
            public bool Retried;
            /// <summary>The last outcome (for the console) and when.</summary>
            public string Outcome = "";
            public float OutcomeAt;
            /// <summary>Points she could not reach (no path), set aside for k1's Pick.</summary>
            public readonly ScBackOff.Last Memory = new ScBackOff.Last();
            /// <summary>F4: the gain clock of this wait's back-off - the next check, and the nearest pursuer's gap at the last (0: not started).</summary>
            public float GainAt, GainGap;
            /// <summary>
            /// Round nc1 (D2): cornered above CorneredHealBelow in the wait that began at FightOnWait (-1 none) - she fights on; the
            /// back-off is looked for again from FightOnUntil (CorneredRecheckSeconds), at once once her health drops to the line.
            /// </summary>
            public float FightOnWait = -1f, FightOnUntil;
        }

        /// <summary>Round nc1 (D2): a cornered member who fights on looks for room to back off again this often.</summary>
        private const float CorneredRecheckSeconds = 4f;

        private static readonly Dictionary<int, State> states = new Dictionary<int, State>();
        private static int episodes, clears, cornered, keptUp, foughtOn;
        private static float nextPrune;

        public static void Reset()
        {
            states.Clear();
            episodes = clears = cornered = keptUp = foughtOn = 0;
            nextPrune = 0f;
        }

        // ------------------------------------------------------------------ pure reads ----

        /// <summary>
        /// May this member back off to heal, or must she use the item where she is? No while she shelters in her room (rt1's
        /// Shelter mode: the room is where she should be - shelter true), on watch (a guard holds her post) or on a Stay order.
        /// A pure read (ScMedic.HeldByReach and the consideration).
        /// </summary>
        internal static bool MayBackOff(EntityAlive e, Member m, out string why, out bool shelter)
        {
            why = null;
            shelter = false;
            if (e == null || m == null) { why = "not a member"; return false; }
            if (ScRetreat.Shelters(e))
            {
                shelter = true;
                why = "sheltering in " + ScMedic.Her(e) + " room (round rt1: that is where " + ScMedic.She(e) + " should be)";
                return false;
            }
            if (ScWatch.OnWatch(m)) { why = "on watch (a guard holds " + ScMedic.Her(e) + " post)"; return false; }
            if (e.Buffs != null && Mathf.RoundToInt(e.Buffs.GetCustomVar("CurrentOrder")) == OrderStay) { why = "told to stay"; return false; }
            return true;
        }

        /// <summary>
        /// The task found no room to back off during the wait that began at waitSince (ScMedic's heldSince), less than
        /// HealNowSeconds ago: ScMedic uses the item now. A pure read.
        /// </summary>
        internal static bool HealNowFor(EntityAlive e, float waitSince, out string why)
        {
            why = null;
            State st;
            if (e == null || !states.TryGetValue(e.entityId, out st)) return false;
            if (st.HealNowWait < 0f || Mathf.Abs(st.HealNowWait - waitSince) > SameWait || Time.time >= st.HealNowUntil) return false;
            why = st.HealNowWhy;
            return true;
        }

        /// <summary>
        /// The ScHealBackOff consideration (and the task's own re-check): HealBackOff on; one of the community's members on
        /// 0-XNPCCore's body, not a raider; no use running; ScMedic holding her from one (WaitingToHeal); she may back off
        /// (MayBackOff); not resting after she got clear; and no heal-now verdict for this wait. A pure read.
        /// </summary>
        public static bool Wanted(EntityAlive e)
        {
            if (!ScConfig.HealBackOff || ScConfig.MedicSafeDistance <= 0f || e == null || e.isEntityRemote) return false;
            if (!ScRegistry.Loaded || !ScXnpc.IsBody(e) || ScXnpc.IsRaider(e)) return false;
            Member m = ScRegistry.GetMember(e.entityId);
            if (m == null || ScMedic.IsUsing(e.entityId)) return false;
            float waited;
            if (!ScMedic.WaitingToHeal(e, out waited)) return false;
            string why;
            bool shelter;
            if (!MayBackOff(e, m, out why, out shelter)) return false;
            State st;
            if (!states.TryGetValue(e.entityId, out st)) return true;
            float now = Time.time;
            if (now < st.ClearUntil) return false;
            // round nc1 (D2): cornered above CorneredHealBelow she fights on - the back-off is looked for again only after
            // CorneredRecheckSeconds, or at once when her health drops to the line
            if (now < st.FightOnUntil && FightsOn(e, st, now - waited)) return false;
            return !HealNowFor(e, now - waited, out why);
        }

        /// <summary>
        /// Round nc1 (the user's decision D2): a member CORNERED - no room to back off, the task's heal-now-cornered - keeps fighting
        /// while her health is above CorneredHealBelow (35%); at or below it she heals at once, as before. Not cornered: unchanged
        /// (she backs off to heal). Heal-now's other verdicts (the enemy keeps up, sheltering, on watch, told to stay) are not
        /// "cornered" and stay as they were. True while this wait (waitSince, ScMedic's heldSince) found no room above the line;
        /// ScMedic's MedicMaxWait timeout waits on it too (CorneredFightsOn). A pure read.
        /// </summary>
        private static bool FightsOn(EntityAlive e, State st, float waitSince)
        {
            return st.FightOnWait >= 0f && Mathf.Abs(st.FightOnWait - waitSince) <= SameWait && (AboveCorneredLine(e) || ShootsInstead(e, st));
        }

        /// <summary>Round t2 (review T2-M3): at most this long from the start of the wait she shoots instead of healing.</summary>
        private const float ShootInsteadMaxSeconds = 10f;

        /// <summary>Round t2 (Ned, 2026-11-27): an enemy this close cuts a heal short - a bandage takes 2.5 s and a hit stops it.</summary>
        private const float HealCutRange = 2.5f;

        /// <summary>
        /// Round t2 (Ned, 2026-11-27: at 33% with an auto shotgun with rounds, four zombies on him and no room to back off, he stood
        /// bandaging with one at 1.1 m, was hit out of it and went down): cornered at or below CorneredHealBelow, with her gun ready
        /// (rounds in her stores: ScRetreat.GunReady) and her pursuer within HealCutRange, she keeps shooting - the heal would be cut
        /// short - and looks again in CorneredRecheckSeconds (healing at once when the enemy is farther or the gun is dry). A pure read.
        /// </summary>
        private static bool ShootsInstead(EntityAlive e, State st)
        {
            if (e == null || st == null || !ScRetreat.GunReady(e)) return false;
            // review T2-M3: never past ShootInsteadMaxSeconds of this wait (then the heal comes, as before), and only at a pursuer she
            // can see - one behind a door or a wall cannot cut the heal short
            if (st.WaitSince < 0f || Time.time - st.WaitSince > ShootInsteadMaxSeconds) return false;
            float dist;
            EntityAlive t = Pursuer(e, out dist);
            return t != null && dist <= HealCutRange && UAI.SCoreUtils.CanSee(e, t);
        }

        /// <summary>Round nc1 (D2): her health is above CorneredHealBelow (a cornered member fights on). A pure read.</summary>
        private static bool AboveCorneredLine(EntityAlive e)
        {
            int max = e != null ? e.GetMaxHealth() : 0;
            return max > 0 && ScConfig.CorneredHealBelow < 1f && e.Health > max * ScConfig.CorneredHealBelow;
        }

        /// <summary>
        /// Round nc1 (D2, ScMedic.HeldByReach): the wait that began at waitSince found no room to back off with her health above
        /// CorneredHealBelow - she fights on, and MedicMaxWait's "use it anyway" waits too (it would heal her with the zombie on
        /// her, which D2 rules out above the line). A pure read.
        /// </summary>
        internal static bool CorneredFightsOn(EntityAlive e, float waitSince)
        {
            State st;
            return e != null && states.TryGetValue(e.entityId, out st) && FightsOn(e, st, waitSince);
        }

        /// <summary>The task is walking her to a point right now (SC's X5 tick holds the weapon swaps meanwhile). A pure read.</summary>
        public static bool Walking(EntityAlive e)
        {
            State st;
            return e != null && states.TryGetValue(e.entityId, out st) && st.Walking && Time.time - st.WalkAt <= WalkFresh;
        }

        /// <summary>
        /// The Fable review (F8): did a back-off walk start during the wait that began at waitSince (ScMedic's timeout line says
        /// "backing off" only then)? A pure read.
        /// </summary>
        internal static bool BackedOffFor(EntityAlive e, float waitSince)
        {
            State st;
            return e != null && states.TryGetValue(e.entityId, out st) && st.Counted && Mathf.Abs(st.WaitSince - waitSince) <= SameWait;
        }

        /// <summary>F8: why the back-off never ran - the action that has her now (her UAI context), for ScMedic's timeout line. A pure read.</summary>
        internal static string NoRunWhy(EntityAlive e)
        {
            UAI.Context ctx = e != null ? e.utilityAIContext : null;
            UAI.UAIAction a = ctx != null ? ctx.ActionData.Action : null;
            if (a == null) return "no action had " + ScMedic.HerObj(e);
            return a.Name == "ScHealBackOff" ? "it had no step to take yet" : a.Name + " had " + ScMedic.HerObj(e);
        }

        // ------------------------------------------------------------------ the task's side (the only writer) ----

        private static State StateOf(int id)
        {
            State st;
            if (!states.TryGetValue(id, out st)) states[id] = st = new State();
            return st;
        }

        private static void Prune(float now)
        {
            nextPrune = now + 30f;
            var gone = new List<int>();
            foreach (var kv in states) if (!kv.Value.Walking && now - kv.Value.OutcomeAt > 120f && now >= kv.Value.HealNowUntil) gone.Add(kv.Key);
            foreach (int id in gone) states.Remove(id);
        }

        /// <summary>
        /// Her bound: at home inside the base (Home); a follower away from home with her leader within StandOffFollowerLeash,
        /// within that of him (Leader); anyone else away from home - a runner, a follower with no leader near - Free (the same
        /// steps, with no other bound).
        /// </summary>
        private static ScRetreat.Region BoundOf(EntityAlive e, Member m, out EntityPlayer leader, out string text)
        {
            leader = null;
            if (UAI.UAIConsiderationScNotHomeMember.AtHome(e.position)) { text = "inside the base"; return ScRetreat.Region.Home; }
            if (m.Status == MemberStatus.Following || m.Status == MemberStatus.PendingStation)
            {
                EntityPlayer p = ScRetreat.LeaderOf(e);
                if (p != null && ScConfig.StandOffFollowerLeash > 0f && ScRunners.XZ(p.position, e.position) <= ScConfig.StandOffFollowerLeash)
                {
                    leader = p;
                    text = "within " + ScConfig.StandOffFollowerLeash.ToString("0", CultureInfo.InvariantCulture) + " m of you";
                    return ScRetreat.Region.Leader;
                }
            }
            text = ScRunners.IsPhysical(m) ? "on " + ScMedic.Her(e) + " run, no bound" : "away from home, no bound";
            return ScRetreat.Region.Free;
        }

        /// <summary>The nearest enemy after her within MedicSafeDistance + ClearMargin (null: she is clear).</summary>
        private static EntityAlive Pursuer(EntityAlive e, out float dist)
        {
            return ScMedic.NearestEnemy(e, ScConfig.MedicSafeDistance + ClearMargin, true, out dist);
        }

        private static void Note(State st, string outcome, float now)
        {
            st.Outcome = outcome;
            st.OutcomeAt = now;
        }

        /// <summary>
        /// The task's Start: the episode (one per ScMedic wait), then - already clear: rest and heal; no room: heal now (this
        /// wait); else a path to the point at a run. True while she walks.
        /// </summary>
        internal static bool StartWalk(EntityAlive self)
        {
            float now = Time.time;
            if (now >= nextPrune) Prune(now);
            Member m = ScRegistry.GetMember(self.entityId);
            float waited;
            if (m == null || !ScMedic.WaitingToHeal(self, out waited)) return false;
            State st = StateOf(self.entityId);
            float waitSince = now - waited;
            if (Mathf.Abs(st.WaitSince - waitSince) > SameWait)
            {
                st.WaitSince = waitSince;                           // a new wait: a new episode
                st.Counted = st.ClearCounted = false;
                st.GainAt = 0f;
            }
            st.Walking = false;
            st.Retried = false;
            float dist;
            EntityAlive t = Pursuer(self, out dist);
            if (t == null) { Clear(self, st, now); return false; }
            // F4: the gain clock runs across the steps of one wait (a step that ends in under GainSeconds does not reset it)
            if (st.GainAt <= 0f) { st.GainAt = now + GainSeconds; st.GainGap = dist; }
            else if (now >= st.GainAt && !Gained(self, st, t, dist, now)) return false;
            EntityPlayer leader;
            string bound;
            ScRetreat.Region region = BoundOf(self, m, out leader, out bound);
            Vector3 p;
            float step, angle;
            string why;
            if (!ScBackOff.Pick(self, t, MaxStep, region, leader, st.Memory, out p, out step, out angle, out why))
            {
                // round nc1 (D2): cornered above CorneredHealBelow she fights on (no heal-now); the back-off is looked for again in
                // CorneredRecheckSeconds, and at or below the line the next look heals her at once (below)
                bool above = AboveCorneredLine(self);
                if (above || ShootsInstead(self, st))  // round t2: or a heal an enemy at arm's length would cut short, with a gun ready
                {
                    bool first = st.FightOnWait < 0f || Mathf.Abs(st.FightOnWait - st.WaitSince) > SameWait;
                    st.FightOnWait = st.WaitSince;
                    st.FightOnUntil = now + CorneredRecheckSeconds;
                    // round t2 (review T2-m3): the "keeps shooting" line once per wait, also when the wait was first cornered above the line
                    if (!above && Mathf.Abs(st.ShootLoggedWait - st.WaitSince) > SameWait)
                    {
                        st.ShootLoggedWait = st.WaitSince;
                        ScLog.Out(ScMedic.BodyName(self) + " has no room to back off from " + Who(t) + " at " + dist.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
                                  + " m " + bound + " - " + ScMedic.She(self) + " keeps shooting at " + self.Health + "/" + self.GetMaxHealth()
                                  + ": a heal would be cut short (round t2)");
                    }
                    st.OutcomeAt = now;                              // the review: Prune keeps a live fights-on state (120 s from the last look)
                    st.GainAt = 0f;                                  // standing and fighting is no back-off: F4's clock starts again at the next look
                    if (first)
                    {
                        foughtOn++;
                        Patches_Xnpc.Count("heal-cornered-fights-on");
                        Note(st, above ? "CORNERED " + bound + " - fights on above " + Pct(ScConfig.CorneredHealBelow) : "CORNERED " + bound + " - keeps shooting, a heal would be cut short (t2)", now);
                        if (above)
                            ScLog.Out(ScMedic.BodyName(self) + " has no room to back off from " + Who(t) + " " + bound + " - " + ScMedic.She(self) + " keeps fighting at "
                                      + self.Health + "/" + self.GetMaxHealth() + " and heals at once only at or below " + Pct(ScConfig.CorneredHealBelow) + " (round nc1, D2)");
                    }
                    return false;
                }
                st.HealNowWait = st.WaitSince;
                st.HealNowUntil = now + HealNowSeconds;
                st.HealNowWhy = "no room to back off " + bound + " (" + why + ")";
                cornered++;
                Patches_Xnpc.Count("heal-now-cornered");
                Note(st, "CORNERED " + bound + " - healed at once", now);
                ScLog.Out(ScMedic.BodyName(self) + " has no room to back off from " + Who(t) + " " + bound + " - " + ScMedic.She(self)
                          + " heals now (round k2b)");
                ScMedic.CheckSoon(self.entityId);
                return false;
            }
            st.Point = p;
            st.Step = step;
            st.Angle = angle;
            st.Bound = bound;
            st.FightOnWait = -1f;                                    // round nc1 (D2): room again - the back-off's own rules (MedicMaxWait)
            st.FightOnUntil = 0f;
            st.Walking = true;
            st.WalkAt = now;
            Note(st, "backed off " + step.ToString("0", CultureInfo.InvariantCulture) + " m " + bound, now);
            if (!st.Counted)
            {
                st.Counted = true;
                episodes++;
                Patches_Xnpc.Count("heal-backoff");
                ScLog.Verbose(ScMedic.BodyName(self) + " backs off from " + Who(t) + " at " + dist.ToString("0.#", CultureInfo.InvariantCulture) + " m to heal: "
                              + step.ToString("0", CultureInfo.InvariantCulture) + " m " + bound
                              + (Mathf.Abs(angle) > 0.5f ? ", " + angle.ToString("0", CultureInfo.InvariantCulture) + " degrees off straight back" : "")
                              + " (round k2b)");
            }
            if (self.moveHelper != null) self.moveHelper.CanBreakBlocks = false;
            self.FindPath(p, ScRetreat.RunSpeed(self), false, null);
            return true;
        }

        /// <summary>
        /// The task's Update: false ends the walk - she is clear (rest, and ScMedic's check at once), she reached the point
        /// still not clear (SCore starts the task again: a new step from where she stands), or the point has no path after one
        /// retry (set aside 10 s).
        /// </summary>
        internal static bool TickWalk(EntityAlive self)
        {
            State st;
            if (!states.TryGetValue(self.entityId, out st) || !st.Walking) return false;
            float now = Time.time;
            st.WalkAt = now;
            float dist;
            EntityAlive t = Pursuer(self, out dist);
            if (t == null) { Clear(self, st, now); return false; }
            if (now >= st.GainAt && st.GainAt > 0f && !Gained(self, st, t, dist, now)) return false;
            if (ScRunners.XZ(self.position, st.Point) <= ArriveXZ) return false;
            if (self.getNavigator() != null && self.getNavigator().noPathAndNotPlanningOne())
            {
                if (!st.Retried)
                {
                    st.Retried = true;
                    self.FindPath(st.Point, ScRetreat.RunSpeed(self), false, null);
                    return true;
                }
                st.Memory.Bad.Add(st.Point);
                st.Memory.BadUntil.Add(now + 10f);
                Patches_Xnpc.Count("heal-backoff-unreached");
                return false;
            }
            return true;
        }

        /// <summary>
        /// F4: the gain check, due now - the nearest enemy after her (t, dist) GainNeed farther than at the last check: the clock
        /// runs on (true). Else it keeps up with her: this wait is marked heal now (heal-now-gained) and ScMedic is asked at once
        /// (false: the walk ends).
        /// </summary>
        private static bool Gained(EntityAlive self, State st, EntityAlive t, float dist, float now)
        {
            if (dist >= st.GainGap + GainNeed)
            {
                st.GainGap = dist;
                st.GainAt = now + GainSeconds;
                return true;
            }
            st.Walking = false;
            st.HealNowWait = st.WaitSince;
            st.HealNowUntil = now + HealNowSeconds;
            st.HealNowWhy = Who(t) + " keeps up with " + ScMedic.HerObj(self) + " (" + st.GainGap.ToString("0.#", CultureInfo.InvariantCulture) + " m to "
                            + dist.ToString("0.#", CultureInfo.InvariantCulture) + " m in " + GainSeconds.ToString("0", CultureInfo.InvariantCulture) + " s)";
            keptUp++;
            Patches_Xnpc.Count("heal-now-gained");
            Note(st, "KEPT UP (" + Who(t) + ") - healed at once", now);
            ScLog.Out(ScMedic.BodyName(self) + " cannot shake off " + Who(t) + " (" + st.GainGap.ToString("0.#", CultureInfo.InvariantCulture) + " m to "
                      + dist.ToString("0.#", CultureInfo.InvariantCulture) + " m in " + GainSeconds.ToString("0", CultureInfo.InvariantCulture) + " s) - "
                      + ScMedic.She(self) + " heals now (the Fable review, F4)");
            ScMedic.CheckSoon(self.entityId);
            return false;
        }

        /// <summary>She is clear of everything after her within MedicSafeDistance + ClearMargin: rest, and the item now.</summary>
        private static void Clear(EntityAlive self, State st, float now)
        {
            st.Walking = false;
            st.ClearUntil = now + ClearHoldSeconds;
            if (st.Counted && !st.ClearCounted)
            {
                st.ClearCounted = true;
                clears++;
                Patches_Xnpc.Count("heal-backoff-clear");
                Note(st, "got clear, the item next", now);
                ScLog.Verbose(ScMedic.BodyName(self) + " is clear: nothing after " + ScMedic.HerObj(self) + " within "
                              + (ScConfig.MedicSafeDistance + ClearMargin).ToString("0.#", CultureInfo.InvariantCulture) + " m - " + ScMedic.She(self)
                              + " uses " + ScMedic.Her(self) + " item now (round k2b)");
            }
            ScMedic.CheckSoon(self.entityId);
        }

        /// <summary>The task stopped (done, or another action took over): the path goes, the swap hold ends.</summary>
        internal static void Stopped(EntityAlive self)
        {
            State st;
            if (states.TryGetValue(self.entityId, out st)) st.Walking = false;
            ScCommunity.DropPath(self);
            if (self.moveHelper != null) self.moveHelper.CanBreakBlocks = true;
        }

        private static string Who(EntityAlive t) { return t.EntityName + " (" + t.entityId + ")"; }

        private static string Pct(float share) { return (share * 100f).ToString("0", CultureInfo.InvariantCulture) + "%"; }

        // ------------------------------------------------------------------ console ----

        /// <summary>Her back-off to heal during a wait (sc xnpc's medic line, sc meds).</summary>
        public static string Text(EntityAlive e)
        {
            if (!ScConfig.HealBackOff) return "HealBackOff false: round k2's hold (MedicMaxWait under 25%)";
            Member m = e != null && ScRegistry.Loaded ? ScRegistry.GetMember(e.entityId) : null;
            if (m == null) return "a stand-in: round k2's hold (MedicMaxWait under 25%)";
            string why;
            bool shelter;
            if (!MayBackOff(e, m, out why, out shelter)) return "no back-off (" + why + "): heals at once";
            State st;
            float now = Time.time;
            if (!states.TryGetValue(e.entityId, out st)) return "backs off to heal (round k2b)";
            if (st.Walking && now - st.WalkAt <= WalkFresh)
                return "BACKING OFF TO HEAL to " + ScRetreat.Cell(st.Point) + " (" + st.Step.ToString("0", CultureInfo.InvariantCulture) + " m, "
                       + st.Angle.ToString("0", CultureInfo.InvariantCulture) + " degrees, " + st.Bound + ")";
            if (now < st.HealNowUntil) return "HEALS NOW: " + st.HealNowWhy;
            float held;
            if (ScMedic.WaitingToHeal(e, out held) && FightsOn(e, st, now - held))
                return "CORNERED - fights on above " + Pct(ScConfig.CorneredHealBelow) + " (round nc1, D2)";
            if (now < st.ClearUntil) return "CLEAR - heals now";
            return "backs off to heal (round k2b)";
        }

        /// <summary>Her last back-off to heal, when no wait is on ("" with none).</summary>
        public static string LastText(EntityAlive e)
        {
            State st;
            if (e == null || !states.TryGetValue(e.entityId, out st) || st.OutcomeAt <= 0f) return "";
            return "; last back-off to heal " + (Time.time - st.OutcomeAt).ToString("0", CultureInfo.InvariantCulture) + " s ago: " + st.Outcome;
        }

        /// <summary>sc meds &lt;id|name&gt;: whether she may back off to heal and how it stands.</summary>
        public static string Describe(EntityAlive e, Member m)
        {
            if (!ScConfig.HealBackOff) return "OFF (HealBackOff false: round k2's hold, MedicMaxWait under 25%)";
            if (ScConfig.MedicSafeDistance <= 0f) return "nothing to back off from (MedicSafeDistance 0: " + ScMedic.She(e) + " uses items wherever " + ScMedic.She(e) + " is)";
            string why;
            bool shelter;
            string may = MayBackOff(e, m, out why, out shelter) ? "may back off now" : "may NOT back off now (" + why + "): " + ScMedic.She(e) + " heals at once";
            float waited;
            string now = ScMedic.WaitingToHeal(e, out waited) ? "held " + waited.ToString("0.#", CultureInfo.InvariantCulture) + " s - " + Text(e) : "not held";
            return may + "; " + now + LastText(e);
        }

        /// <summary>The totals for sc meds.</summary>
        public static string Report()
        {
            if (!ScConfig.HealBackOff) return "OFF (HealBackOff false: round k2's hold with the MedicMaxWait / 25% exception)";
            return "on - " + episodes + " back-off(s) to heal, " + clears + " got clear, " + cornered + " cornered and " + keptUp + " kept up with (healed at once), "
                   + foughtOn + " cornered above " + Pct(ScConfig.CorneredHealBelow) + " (fought on: round nc1, D2) this session";
        }
    }
}

namespace UAI
{
    using SurvivorCommunity;

    /// <summary>
    /// Round k2b: SCResident's ScHealBackOff (8.3, IsSelf) - 1 while ScMedic holds one of the community's members from a medical
    /// use with an enemy after her in reach and she may back off (ScHealBackOff.Wanted), else 0. A pure read; a throw is logged
    /// once and means 0 (round k2's hold as it was).
    /// </summary>
    public class UAIConsiderationScHealBackOff : UAIConsiderationBase
    {
        public override float GetScore(Context _context, object target)
        {
            try
            {
                return ScHealBackOff.Wanted(_context.Self) ? 1f : 0f;
            }
            catch (System.Exception e)
            {
                ScLog.Once("uai-heal-backoff", e);
                return 0f;
            }
        }
    }

    /// <summary>
    /// Round k2b: ScHealBackOff's task - walk her away from the enemy after her (ScHealBackOff.StartWalk / TickWalk) until she
    /// is clear, then stop so the item goes into her hand. A shared singleton: its state is per entity, in ScHealBackOff.
    /// SCore starts it again when it ends while the action still wins: Start re-checks (Wanted) and picks from where she stands.
    /// </summary>
    public class UAITaskHealBackOffSC : UAITaskBase
    {
        public override void Start(Context _context)
        {
            base.Start(_context);
            EntityAlive self = _context.Self;
            try
            {
                if (!ScHealBackOff.Wanted(self) || !ScHealBackOff.StartWalk(self)) Stop(_context);
            }
            catch (System.Exception e)
            {
                ScLog.Once("heal-backoff-start", e);
                Stop(_context);
            }
        }

        public override void Update(Context _context)
        {
            EntityAlive self = _context.Self;
            try
            {
                if (!ScHealBackOff.Wanted(self) || !ScHealBackOff.TickWalk(self)) Stop(_context);
            }
            catch (System.Exception e)
            {
                ScLog.Once("heal-backoff-update", e);
                Stop(_context);
            }
        }

        public override void Stop(Context _context)
        {
            try { ScHealBackOff.Stopped(_context.Self); }
            catch (System.Exception e) { ScLog.Once("heal-backoff-stop", e); }
            base.Stop(_context);
        }
    }
}
