using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Hurt members at home retreat inside, and with a gun keep their distance (rounds rt1 and k5; research/flee_meds/DESIGN.md
    /// Part A, research/combat/SC_VS_NPCCORE.md 4.4). The user's request (rt1): "They shouldn't flee away from home, they should
    /// retreat inside, closing doors etc. If they have a ranged weapon, they should create distance and continue engaging so they
    /// aren't just leaving others to fend for themselves."
    ///
    /// One state per body, keyed by entity id, host only. The ONLY writer is Update, called from SC's X5 prefix on
    /// SdcsCloseRangeTick (5 Hz, Patches_Xnpc) right after ScTeam.Recount; the threat scan runs at most every ScanSeconds per
    /// body, staggered by id. Everything else only READS it: the UAI considerations (UaiRetreat.cs), the posture (ScPosture),
    /// ScTeam and ScArms. The shelter task (UAITaskRetreatSC) writes its spot and its failures back through NoteSpot, SpotFailed
    /// and NoteDoors.
    ///
    /// Who (decision 1, live in Eligible): one of the community's members on her own body (never a stand-in, a raider, an
    /// enclave person or a stranger: decision 12), not on watch, not on a Stay order, and either at home (the home zone and
    /// ResidentRoamZoneMargin round it, or the defended area in a base attack: round hl1's AtHome) as a resident, a follower,
    /// one on the way to her post or a runner on the Back leg - region Home - or (decision 4) a follower away from home with you
    /// within StandOffFollowerLeash - region Leader, KITE only. Runners on the way out, at the site or on the way in keep round
    /// r2's flight. RetreatHome false: nobody, ever.
    ///
    /// Hurt: ScMedic.HurtNoMeds at FleeBelowHealth (ScFleeHurt's own rule) - and she stays hurt until RetreatHealedAt. Cowardly:
    /// $Timid at home and not hanging back (ScTeam.HangsBack; RetreatCowards, decision 6). Either, with a threat (an awake enemy
    /// within FleeRadius, 15 m, that is after her or can see her), starts a mode:
    ///   Kite (round k5, was rt1's stand-off): hurt with a gun that has rounds in her pair and no armed enemy within FleeClear
    ///     (decision 3). Not a walk of its own: the posture is KITE with the wide band (ScPosture, View.Wide) and 0-XNPCCore's
    ///     back-off task does the moving - BackupKeepDistance within BackupKeepDistanceMax, and SCResident's ScKiteWide (the
    ///     same task, 8.2) from any visible enemy within StandOffMin, kept up until WideGap while it runs - with round k1's
    ///     bounded points (10, 6, 3 m steps inside the base; within StandOffFollowerLeash of you as a follower away from home),
    ///     its gain test and its shooting beats (ScBackOff). Away from home, no point CorneredAwayMax times in a row: she flees
    ///     as before for CorneredAwayFleeSeconds.
    ///   Shelter (at home only): hurt with no gun that has rounds, or armed enemies about, or Cowardly. The shelter walk
    ///     (ScRetreat 8.5, UAITaskRetreatSC): a roofed, walled spot away from the threat, doors shut behind her (ScDoors).
    /// A mode switch waits ModeDwellSeconds, except to shelter on a dry gun.
    /// Exits (decision 8 and round k5): healed to RetreatHealedAt; a coward who now hangs back; no threat for
    /// RetreatClearSeconds; no longer eligible. Shelter only (round k5, "no member stays in shelter forever"): she can heal
    /// herself again (md1's or k2's emergency restock from the stockpile while she shelters, or an item you hand her) and
    /// nothing has been after her for RestockExitSeconds - she leaves to heal and goes back to her routine; or she has sheltered
    /// RetreatMaxSeconds, nothing has been after her for RestockExitSeconds, no armed enemy is about and the scan's threat is
    /// gone or at least ShelterMinThreatDist off (the Fable review of k5, finding 1: LongExitClear). After those two exits only an enemy after her
    /// (not one that merely sees her) starts a shelter again for RetreatClearSeconds. Every exit clears the state at once,
    /// never kept to act on later (the round hl1 lesson).
    /// FightBack (Shelter): an enemy within RetreatFightBackRange that is after her, or one that hit her in the last 3 s within
    /// 4 m - each with no block between their heads (a zombie pounding the door she shut, or a bleed tick, is not a reason).
    /// It lingers 3 s; while it holds the shelter action scores 0 and the pack's attacks take over.
    /// </summary>
    public static class ScRetreat
    {
        public enum Mode { None = 0, Shelter = 1, Kite = 2 }
        /// <summary>
        /// Where a retreat may act: at home, or away with her leader. Free (round k2b) is never a retreat's region: it only tells
        /// ScBackOff.Pick that a back-off to heal away from home and her leader has no bound but its own short steps.
        /// </summary>
        public enum Region { None = 0, Home = 1, Leader = 2, Free = 3 }

        /// <summary>The threat scan, per body (staggered by id).</summary>
        private const float ScanSeconds = 0.5f;
        /// <summary>
        /// A state not updated for this long counts as none (trap 9: the X5 prefix skips its tick during a medical use, 1.4
        /// to 2.5 s; a body that stops ticking for good must not keep acting on an old state).
        /// </summary>
        private const float StaleSeconds = 4f;
        /// <summary>FightBack: a hit this recent from her revenge target within FightBackRevengeRange.</summary>
        private const float FightBackHitSeconds = 3f, FightBackRevengeRange = 4f;
        /// <summary>FightBack stays this long after neither reason holds.</summary>
        private const float FightBackLinger = 3f;
        /// <summary>"Threat gone": no awake enemy in the home zone within this of her either.</summary>
        private const float ZoneClearRadius = 15f;
        /// <summary>CurrentOrder 2 is SCore's Stay (the cvar 0-XNPCCore's actions are gated on; ScTeam reads it the same way).</summary>
        private const int OrderStay = 2;
        /// <summary>
        /// The Fable review of rt1 (finding 5): armed enemies count from FleeClear (30 m) and stop counting only past this,
        /// so a raider pacing at 30 m does not flip shelter and KITE at every scan.
        /// </summary>
        private const float ArmedLeave = 35f;
        /// <summary>
        /// The Fable review (finding 5): a mode lasts at least this long before it switches (shelter to KITE and back) - except
        /// to shelter when her gun is dry (KITE with nothing to shoot is worse than any flip).
        /// </summary>
        private const float ModeDwellSeconds = 5f;
        /// <summary>
        /// The Fable review (finding 6): a body whose X5 tick (Update) has not run for this long is not one the retreat can
        /// own - ScNotRetreatHome lets today's flight run, rather than leaving her with neither.
        /// </summary>
        private const float TickedSeconds = 5f;
        /// <summary>The Fable review (finding 8): away from home, this many "no point" verdicts in a row (ScBackOff) end KITE...</summary>
        private const int CorneredAwayMax = 2;
        /// <summary>...and she flees as before (ScFleeHurt) for this long before she may kite there again.</summary>
        private const float CorneredAwayFleeSeconds = 15f;
        /// <summary>Round k5: a shelter's restock and long exits need nothing after her for this long.</summary>
        private const float RestockExitSeconds = 5f;

        internal sealed class State
        {
            public Mode Mode;
            public Region Region;
            public string Why = "";
            /// <summary>When the retreat began, the last Update, the last X5 tick that asked, the next threat scan.</summary>
            public float Since, At, Seen, NextScan;
            /// <summary>What keeps her in it: hurt (until RetreatHealedAt), Cowardly (until she hangs back).</summary>
            public bool Hurt, Coward;
            /// <summary>The scan: the nearest threat within FleeRadius (after her or seeing her), -1 none; its last known place.</summary>
            public int ThreatId = -1;
            public Vector3 ThreatPos;
            public float ThreatDist = -1f, ThreatSeenAt;
            public bool ThreatAfter;
            /// <summary>The Fable review (finding 5): when the mode last changed (its dwell).</summary>
            public float ModeSince;
            /// <summary>The Fable review (finding 8): away from home KITE is off until then after CorneredAwayMax "no point" verdicts in a row.</summary>
            public float NoKiteUntil;
            /// <summary>The scan: an armed enemy within FleeClear; her pair's gun has rounds; a threat present (the clear clock).</summary>
            public bool Armed, Gun, Present;
            /// <summary>When "no threat" began (0: a threat is present).</summary>
            public float ClearSince;
            /// <summary>Round k5, the scan: an enemy within FleeClear is after her (its attack or revenge target); since when none is (0: one is).</summary>
            public bool Targeted;
            public float NotTargetSince;
            /// <summary>Round k5: after a restock or long exit, until then only an enemy after her starts a shelter again.</summary>
            public float AfterOnlyUntil;
            public bool FightBack;
            public float FightBackUntil, HitAt;
            public int LastHealth = -1;
            /// <summary>The shelter spot the task picked (kept across the task's stops: FightBack, then back to it).</summary>
            public bool HasSpot, SpotHold;
            public Vector3 Spot;
            public string SpotSource = "";
            public int SpotWalls;
            public bool SpotRoofed;
            public ScShelter.PickKind SpotKind;
            /// <summary>The fallback this retreat took (logged once), spots she could not reach this retreat, how many.</summary>
            public string Fallback;
            public readonly List<Vector3i> Bad = new List<Vector3i>();
            public int Fails;
            public int Doors;
        }

        private static readonly Dictionary<int, State> states = new Dictionary<int, State>();
        /// <summary>The Fable review (finding 6): when Update last ran for each member body (states are only made for the hurt and the cowards).</summary>
        private static readonly Dictionary<int, float> ticked = new Dictionary<int, float>();
        private static readonly List<Entity> near = new List<Entity>();
        private static float nextPrune;
        private static int entered, ended;

        public static void Reset()
        {
            states.Clear();
            ticked.Clear();
            near.Clear();
            nextPrune = 0f;
            entered = ended = 0;
        }

        /// <summary>A despawned body's state (entity ids are recycled).</summary>
        public static void Forget(int entityId) { states.Remove(entityId); ticked.Remove(entityId); }

        // ------------------------------------------------------------------ the writer (X5 only) ----

        /// <summary>
        /// The X5 prefix's retreat part (Patches_Xnpc.Patch_NoSwapWhileUsing, on SdcsCloseRangeTick, 5 Hz, after the team
        /// recount): eligibility, the threat scan when due, entry, the mode, the exits and FightBack. The only writer of the
        /// states (the shelter task writes its spot through NoteSpot, SpotFailed and NoteDoors). Never throws.
        /// </summary>
        public static void Update(EntityAlive e)
        {
            try
            {
                if (e == null || e.isEntityRemote) return;
                State st;
                states.TryGetValue(e.entityId, out st);
                if (!ScConfig.RetreatHome || !ScRegistry.Loaded)
                {
                    if (st != null && st.Mode != Mode.None) End(e, st, "off", "RetreatHome is off");
                    return;
                }
                if (!ScXnpc.IsBody(e) || ScXnpc.IsRaider(e)) return;
                Member m = ScRegistry.GetMember(e.entityId);
                float now = Time.time;
                if (m != null) ticked[e.entityId] = now;                          // the X5 hook is alive for her (FleeAllowed)
                if (now >= nextPrune) Prune(now);
                string notWhy = "not a member";
                Region region = m != null ? Eligible(e, m, out notWhy) : Region.None;
                bool active = st != null && st.Mode != Mode.None;
                if (active && region != st.Region && !(st.Mode == Mode.Kite && region != Region.None))
                {
                    End(e, st, "ineligible", "no longer " + (st.Region == Region.Home ? "at home" : "with you away from home") + " ("
                        + (region == Region.None ? notWhy : region == Region.Home ? "now at home" : "now away with you") + ")");
                    active = false;
                }
                if (active && st.Mode == Mode.Kite) st.Region = region;               // KITE goes with her: home, or away with you
                bool hurtEntry = ScMedic.HurtNoMeds(e, ScConfig.FleeBelowHealth);
                bool cowardNow = region == Region.Home && ScConfig.RetreatCowards && Timid(e) && !ScTeam.HangsBack(e);
                if (!active && (region == Region.None || (!hurtEntry && !cowardNow)))
                {
                    if (st != null) { st.Seen = now; st.At = now; }
                    return;
                }
                if (st == null)
                {
                    states[e.entityId] = st = new State();
                    st.NextScan = now + (e.entityId % 5) * 0.1f;                  // staggered: not every body in one frame
                    st.LastHealth = e.Health;
                }
                st.Seen = now;
                st.At = now;
                if (e.Health < st.LastHealth) st.HitAt = now;
                st.LastHealth = e.Health;
                if (now >= st.NextScan)
                {
                    st.NextScan = now + ScanSeconds;
                    Scan(e, st, now);
                }
                if (!active)
                {
                    if (st.ThreatId < 0) return;                                      // nothing within 15 m after her or seeing her
                    st.Hurt = hurtEntry;
                    st.Coward = cowardNow;
                    Mode first = Want(e, st, region, now);
                    if (first == Mode.None) return;                                   // away with no gun: today's flight (decision 5)
                    // round k5: right after a restock or long exit only an enemy AFTER her shelters her again
                    if (first == Mode.Shelter && now < st.AfterOnlyUntil && !st.Targeted) return;
                    Begin(e, st, region, first, now);
                    return;
                }
                // the exits (decision 8)
                float share = Share(e);
                if (hurtEntry) st.Hurt = true;                                        // a coward hurt meanwhile
                bool stillHurt = st.Hurt && share < ScConfig.RetreatHealedAt;
                bool wasHurt = st.Hurt;
                st.Hurt = stillHurt;
                st.Coward = cowardNow;
                if (!stillHurt && !cowardNow)
                {
                    if (wasHurt) End(e, st, "healed", "healed to " + Pct(share) + " (RetreatHealedAt " + Pct(ScConfig.RetreatHealedAt) + ")");
                    else End(e, st, "hangs-back", Timid(e) ? "a matched fight: " + She(e) + " hangs back with " + Her(e) + " gun (tm1)" : "no longer Cowardly");
                    return;
                }
                if (st.ClearSince > 0f && now - st.ClearSince >= ScConfig.RetreatClearSeconds)
                {
                    End(e, st, "threat-gone", "no threat for " + ScConfig.RetreatClearSeconds.ToString("0", CultureInfo.InvariantCulture) + " s");
                    return;
                }
                bool calm = !st.Targeted && st.NotTargetSince > 0f && now - st.NotTargetSince >= RestockExitSeconds;
                if (st.Mode == Mode.Shelter && calm)
                {
                    // round k5 (research/combat/SC_VS_NPCCORE.md 4.2, "an exit for cannot heal"): md1's restock (k2's emergency one
                    // inside the cooldown) reaches her while she shelters at home; with a heal item again and nothing after her she
                    // leaves to heal (ScMedic, k2b's back-off first if one comes) and goes back to her routine
                    if (st.Hurt && ScMedic.CanTreatSelf(e))
                    {
                        st.AfterOnlyUntil = now + ScConfig.RetreatClearSeconds;
                        // round U4 (the Fable review's note): her painkillers count in this line, as CanTreatSelf counts them
                        End(e, st, "restocked", She(e) + " can heal " + (Male(e) ? "himself" : "herself") + " again (" + (ScMedic.HealItemCount(e) + ScMedic.PainkillerCount(e))
                            + " heal item(s), painkillers included: the stockpile's restock, or one you gave " + HerObj(e) + ") and nothing has been after " + HerObj(e) + " for "
                            + RestockExitSeconds.ToString("0", CultureInfo.InvariantCulture) + " s - " + She(e) + " leaves the shelter to heal (round k5)");
                        return;
                    }
                    // round k5: no member shelters forever - after RetreatMaxSeconds with nothing after her she goes back to her
                    // routine; the Fable review of k5 (finding 1): not out into what kept her in - no armed enemy about and the
                    // scan's threat gone or at least ShelterMinThreatDist off (her regen is held by any enemy within 12 m, and the
                    // restock reaches her in the shelter anyway, so leaving sooner gains her nothing)
                    if (ScConfig.RetreatMaxSeconds > 0f && now - st.Since >= ScConfig.RetreatMaxSeconds && LongExitClear(e, st))
                    {
                        st.AfterOnlyUntil = now + ScConfig.RetreatClearSeconds;
                        End(e, st, "long", "sheltered " + (now - st.Since).ToString("0", CultureInfo.InvariantCulture) + " s (RetreatMaxSeconds "
                            + ScConfig.RetreatMaxSeconds.ToString("0", CultureInfo.InvariantCulture) + "), nothing has been after " + HerObj(e) + " for "
                            + RestockExitSeconds.ToString("0", CultureInfo.InvariantCulture) + " s, " + LongExitText(e, st) + "; restock: " + ScMedic.RestockText(e)
                            + " (round k5, the Fable review's finding 1)");
                        return;
                    }
                }
                Mode want = Want(e, st, st.Region, now);
                if (want == Mode.None)
                {
                    End(e, st, st.Region == Region.Leader && now < st.NoKiteUntil ? "cornered-away" : "no-gun-away",
                        st.Region == Region.Leader && now < st.NoKiteUntil
                        ? "no point to back off to near you " + CorneredAwayMax + " times in a row away from home - " + She(e) + " flees as before for "
                          + CorneredAwayFleeSeconds.ToString("0", CultureInfo.InvariantCulture) + " s"
                        : "away from home with no gun to kite with" + (st.Armed ? " (or armed enemies)" : "") + " - " + She(e) + " flees as before");
                    return;
                }
                // the Fable review (finding 5): a mode lasts ModeDwellSeconds before it switches, except to shelter on a dry gun
                bool dry = want == Mode.Shelter && st.Hurt && !st.Gun;
                if (want != st.Mode && (dry || now - st.ModeSince >= ModeDwellSeconds))
                {
                    Patches_Xnpc.Count(want == Mode.Shelter ? "retreat-to-shelter" : "retreat-to-kite");
                    ScLog.Out(Name(e) + " switches " + Her(e) + " retreat from " + ModeName(st.Mode) + " to " + ModeName(want) + ": "
                              + (want == Mode.Shelter ? (!st.Gun ? Her(e) + " gun is dry" : st.Armed ? "armed enemies" : "no longer hurt (a Cowardly member shelters)") : "a gun with rounds again, no armed enemies"));
                    st.Mode = want;
                    st.ModeSince = now;
                    st.FightBack = false;
                    if (want == Mode.Shelter) ScShelter.Want();
                }
                st.Why = WhyText(e, st, share);
                if (st.Mode == Mode.Shelter) FightBackTick(e, st, now);
            }
            catch (Exception ex)
            {
                ScLog.Once("retreat-update", ex);
            }
        }

        /// <summary>
        /// The mode for this scan (decision 3: armed enemies mean shelter): KITE hurt with a gun that has rounds and no armed
        /// enemy - away from home not while CorneredAwayMax "no point" verdicts in a row (ScBackOff) keep it off (the Fable
        /// review, finding 8: then she flees as before for CorneredAwayFleeSeconds); else shelter at home, None away from home.
        /// </summary>
        private static Mode Want(EntityAlive e, State st, Region region, float now)
        {
            if (st.Hurt && st.Gun && !st.Armed)
            {
                if (region == Region.Leader && now >= st.NoKiteUntil && ScBackOff.NoPointStreak(e) >= CorneredAwayMax)
                {
                    st.NoKiteUntil = now + CorneredAwayFleeSeconds;
                    Patches_Xnpc.Count("retreat-cornered-away");
                }
                if (region != Region.Leader || now >= st.NoKiteUntil) return Mode.Kite;
            }
            return region == Region.Home ? Mode.Shelter : Mode.None;
        }

        /// <summary>
        /// The Fable review of k5 (finding 1): the long exit's room to leave - no armed enemy about (the scan's Armed: ScTeam.Armed
        /// within FleeClear, kept until ArmedLeave) and the scan's threat (after her or seeing her) gone or at least
        /// ShelterMinThreatDist off (XZ). A read of the scan's state.
        /// </summary>
        private static bool LongExitClear(EntityAlive e, State st)
        {
            return !st.Armed && (st.ThreatId < 0 || ScRunners.XZ(e.position, st.ThreatPos) >= ScConfig.ShelterMinThreatDist);
        }

        /// <summary>The long exit's room to leave in words (LongExitClear): the exit's log line and sc retreat. A read.</summary>
        private static string LongExitText(EntityAlive e, State st)
        {
            if (st.Armed) return "ARMED enemies about";
            if (st.ThreatId < 0) return "no armed enemy and no threat after " + HerObj(e) + " or seeing " + HerObj(e);
            float d = ScRunners.XZ(e.position, st.ThreatPos);
            return "no armed enemy, the threat " + d.ToString("0.#", CultureInfo.InvariantCulture) + " m off ("
                   + (d >= ScConfig.ShelterMinThreatDist ? "at least" : "UNDER") + " ShelterMinThreatDist "
                   + ScConfig.ShelterMinThreatDist.ToString("0.#", CultureInfo.InvariantCulture) + ")";
        }

        private static void Begin(EntityAlive e, State st, Region region, Mode mode, float now)
        {
            st.Mode = mode;
            st.Region = region;
            st.Since = now;
            st.ModeSince = now;
            st.ClearSince = 0f;
            st.FightBack = false;
            st.FightBackUntil = 0f;
            st.HasSpot = false;
            st.SpotSource = "";
            st.Fallback = null;
            st.Bad.Clear();
            st.Fails = 0;
            st.Doors = 0;
            float share = Share(e);
            st.Why = WhyText(e, st, share);
            entered++;
            Patches_Xnpc.Count(mode == Mode.Shelter ? "retreat-shelter" : "retreat-kite");
            if (region == Region.Leader) Patches_Xnpc.Count("retreat-kite-away");
            ScLog.Out(Name(e) + " retreats - " + (mode == Mode.Shelter ? "SHELTER inside" : "KITE with " + Her(e) + " gun, backing off from what comes within "
                      + ScConfig.StandOffMin.ToString("0.#", CultureInfo.InvariantCulture) + " m (round k5)")
                      + ": " + st.Why + "; threat " + ThreatText(e, st) + (region == Region.Leader ? " (away from home: within " + ScConfig.StandOffFollowerLeash.ToString("0", CultureInfo.InvariantCulture) + " m of you)" : ""));
            if (mode == Mode.Shelter) ScShelter.Want();                       // the cache scans now if it has nothing yet
        }

        private static void End(EntityAlive e, State st, string key, string why)
        {
            float dur = Time.time - st.Since;
            ended++;
            Patches_Xnpc.Count("retreat-ended-" + key);
            ScLog.Out(Name(e) + " ends " + Her(e) + " retreat (" + ModeName(st.Mode) + ", " + dur.ToString("0", CultureInfo.InvariantCulture) + " s"
                      + (st.Doors > 0 ? ", " + st.Doors + " open door(s) handed to ScDoors to shut behind " + HerObj(e) : "") + "): " + why);
            st.Mode = Mode.None;
            st.Region = Region.None;
            st.Hurt = st.Coward = false;
            st.FightBack = false;
            st.HasSpot = false;
            st.Fallback = null;
            st.Bad.Clear();
            st.Fails = 0;
            st.ClearSince = 0f;
        }

        /// <summary>
        /// The threat scan (X5, every ScanSeconds): the nearest awake enemy within FleeRadius that is after her or can see her
        /// (the sight ray only for one nearer than the best so far, as ScRunners.NearestThreat), whether any is present for the
        /// clear clock (after her or seeing her within FleeClear, or awake in the zone within 15 m), whether any within FleeClear
        /// is after her (round k5: the shelter's restock and long exits), an armed enemy (within FleeClear to start counting,
        /// ArmedLeave to stop: the Fable review, finding 5), and her pair's gun.
        /// </summary>
        private static void Scan(EntityAlive e, State st, float now)
        {
            World w = e.world;
            st.Gun = GunReady(e);
            if (w == null) return;
            float clear = ScRunners.FleeClear, flee = ScRunners.FleeRadius;
            near.Clear();
            w.GetEntitiesInBounds(typeof(EntityAlive), new Bounds(e.position, new Vector3(2f * ArmedLeave, 2f * ArmedLeave, 2f * ArmedLeave)), near);
            EntityAlive best = null;
            float bestD2 = flee * flee;
            bool present = false, armedNear = false, armedFar = false, bestAfter = false, targeted = false;
            try
            {
                for (int i = 0; i < near.Count; i++)
                {
                    EntityAlive a = near[i] as EntityAlive;
                    if (a == null || a == e || a.IsDead() || a.IsSleeping) continue;
                    float d2 = (a.position - e.position).sqrMagnitude;
                    if (d2 > ArmedLeave * ArmedLeave || !EntityTargetingUtilities.IsEnemy(e, a)) continue;
                    if (global::Harmony.ZombieFeatures.InertEntity.IsInert(a)) continue;
                    if (ScTeam.Armed(a)) { armedFar = true; if (d2 <= clear * clear) armedNear = true; }
                    if (d2 > clear * clear) continue;
                    bool after = a.GetAttackTarget() == e || a.GetRevengeTarget() == e;
                    if (after) targeted = true;
                    if (d2 <= bestD2 && (after || a.CanEntityBeSeen(e)))
                    {
                        best = a;
                        bestD2 = d2;
                        bestAfter = after;
                        present = true;
                        continue;
                    }
                    if (present) continue;
                    if (after || (d2 <= ZoneClearRadius * ZoneClearRadius && ScShelter.InZone(a.GetBlockPosition())) || a.CanEntityBeSeen(e)) present = true;
                }
            }
            finally { near.Clear(); }
            st.Armed = armedNear || (st.Armed && armedFar);
            st.Present = present;
            st.Targeted = targeted;
            if (targeted) st.NotTargetSince = 0f;
            else if (st.NotTargetSince <= 0f) st.NotTargetSince = now;
            if (best != null)
            {
                st.ThreatId = best.entityId;
                st.ThreatPos = best.position;
                st.ThreatDist = Mathf.Sqrt(bestD2);
                st.ThreatSeenAt = now;
                st.ThreatAfter = bestAfter;
            }
            else
            {
                st.ThreatId = -1;
                st.ThreatDist = -1f;
            }
            if (present) st.ClearSince = 0f;
            else if (st.ClearSince <= 0f) st.ClearSince = now;
        }

        /// <summary>
        /// FightBack (Shelter, X5): an enemy within RetreatFightBackRange after her with no block between their heads (the Fable
        /// review, finding 1: one pounding the door she shut, or the wall, must not end her hold - then MeleeAttack could not
        /// score and Guard walked her out through that door), or a hit from her revenge target within 4 m in the last 3 s - with
        /// no block between their heads either (round k5, rt1 follow-up B: a bleed tick lowers her health too, and the revenge
        /// target behind her shut door then ended the hold).
        /// </summary>
        private static void FightBackTick(EntityAlive e, State st, float now)
        {
            bool close = AfterHerWithin(e, ScConfig.RetreatFightBackRange);
            EntityAlive rv = e.GetRevengeTarget();
            bool hit = rv != null && !rv.IsDead() && st.HitAt > 0f && now - st.HitAt <= FightBackHitSeconds
                       && (rv.position - e.position).sqrMagnitude <= FightBackRevengeRange * FightBackRevengeRange
                       && ScShelter.ClearLine(e.world, rv.getHeadPosition(), e.getHeadPosition(), true);
            if (close || hit)
            {
                if (!st.FightBack)
                {
                    st.FightBack = true;
                    Patches_Xnpc.Count("retreat-fightback");
                    ScLog.Verbose(Name(e) + " fights back from " + Her(e) + " shelter: " + (close ? "an enemy within " + ScConfig.RetreatFightBackRange.ToString("0.#", CultureInfo.InvariantCulture) + " m is after " + HerObj(e) : "hit by " + rv.EntityName + " (" + rv.entityId + ") within 4 m"));
                }
                st.FightBackUntil = now + FightBackLinger;
            }
            else if (st.FightBack && now >= st.FightBackUntil)
            {
                st.FightBack = false;
                ScLog.Verbose(Name(e) + " stops fighting back - back to " + Her(e) + " shelter spot");
            }
        }

        /// <summary>
        /// An awake, living enemy within r that has her as its attack target and no block between its head and hers (a shut
        /// door, a wall, glass or bars: ScShelter.ClearLine with transparent blocks).
        /// </summary>
        private static bool AfterHerWithin(EntityAlive e, float r)
        {
            World w = e.world;
            if (w == null) return false;
            near.Clear();
            w.GetEntitiesInBounds(typeof(EntityAlive), new Bounds(e.position, new Vector3(2f * r, 2f * r, 2f * r)), near);
            bool found = false;
            for (int i = 0; i < near.Count && !found; i++)
            {
                EntityAlive a = near[i] as EntityAlive;
                if (a == null || a == e || a.IsDead() || a.IsSleeping || (a.position - e.position).sqrMagnitude > r * r) continue;
                found = a.GetAttackTarget() == e && EntityTargetingUtilities.IsEnemy(e, a)
                        && ScShelter.ClearLine(w, a.getHeadPosition(), e.getHeadPosition(), true);
            }
            near.Clear();
            return found;
        }

        private static void Prune(float now)
        {
            nextPrune = now + 30f;
            var gone = new List<int>();
            foreach (var kv in states) if (now - kv.Value.Seen > 60f) gone.Add(kv.Key);
            foreach (int id in gone) states.Remove(id);
            gone.Clear();
            foreach (var kv in ticked) if (now - kv.Value > 60f) gone.Add(kv.Key);
            foreach (int id in gone) ticked.Remove(id);
        }

        // ------------------------------------------------------------------ eligibility (pure) ----

        /// <summary>
        /// Where the retreat may act for this member's body right now (decisions 1 and 4), or None with why. A pure read
        /// (position, status, cvars), so the considerations check it live (trap 9) and a stale state never acts away from home.
        /// </summary>
        internal static Region Eligible(EntityAlive e, Member m, out string why)
        {
            why = null;
            if (e == null || m == null) { why = "not a member"; return Region.None; }
            if (ScWatch.OnWatch(m)) { why = "on watch (holds the post)"; return Region.None; }
            if (e.Buffs != null && Mathf.RoundToInt(e.Buffs.GetCustomVar("CurrentOrder")) == OrderStay) { why = "on a Stay order"; return Region.None; }
            bool home = UAI.UAIConsiderationScNotHomeMember.AtHome(e.position);
            switch (m.Status)
            {
                case MemberStatus.Resident:
                    if (home) return Region.Home;
                    why = "a resident outside the home zone + " + ScConfig.ResidentRoamZoneMargin.ToString("0", CultureInfo.InvariantCulture) + " m";
                    return Region.None;
                case MemberStatus.Following:
                case MemberStatus.PendingStation:
                    if (home) return Region.Home;
                    if (ScConfig.StandOffFollowerLeash <= 0f) { why = "a follower away from home (StandOffFollowerLeash 0: flees as before)"; return Region.None; }
                    EntityPlayer leader = LeaderOf(e);
                    if (leader == null) { why = "a follower away from home with no leader in the world"; return Region.None; }
                    // the Fable review (finding 8): one radius - the back-off points must be within it of you too
                    if (ScRunners.XZ(leader.position, e.position) > ScConfig.StandOffFollowerLeash) { why = "a follower away from home, farther than " + ScConfig.StandOffFollowerLeash.ToString("0", CultureInfo.InvariantCulture) + " m from you"; return Region.None; }
                    return Region.Leader;
                case MemberStatus.OnRun:
                case MemberStatus.Transit:                                                  // round op1: a posting journey, as a run
                    if (ScRunners.IsPhysical(m) && !m.Offline && m.RunLeg == RunLeg.Back && home) return Region.Home;
                    why = m.Status == MemberStatus.Transit ? "on the road to or from an outpost" : "on a run (round r2's flight and turn home)";
                    return Region.None;
                default:
                    why = m.Status.ToString();
                    return Region.None;
            }
        }

        /// <summary>Her leader when it is a living player (SCore's leader-or-owner), else null.</summary>
        internal static EntityPlayer LeaderOf(EntityAlive e)
        {
            EntityPlayer p = e != null ? EntityUtilities.GetLeaderOrOwner(e.entityId) as EntityPlayer : null;
            return p != null && !p.IsDead() ? p : null;
        }

        /// <summary>The state while it is live: a mode, fresh, and the body still where it was made for. A pure read.</summary>
        private static bool Active(EntityAlive e, out State st)
        {
            st = null;
            if (e == null || !ScConfig.RetreatHome) return false;
            if (!states.TryGetValue(e.entityId, out st) || st.Mode == Mode.None) return false;
            if (Time.time - st.At > StaleSeconds) return false;
            Member m = ScRegistry.GetMember(e.entityId);
            string why;
            return m != null && Eligible(e, m, out why) == st.Region;
        }

        // ------------------------------------------------------------------ pure reads (the considerations, ScTeam, ScArms) ----

        /// <summary>The retreat owns her (shelter or KITE hurt, live): ScNotRetreating, the posture's approach gate, ScInvestigate. A pure read.</summary>
        public static bool Owns(EntityAlive e)
        {
            State st;
            return Active(e, out st);
        }

        /// <summary>
        /// The retreat owns her in Shelter mode (fighting back or not): the posture is SHELTER (ScPosture: no back-off, no
        /// approach) and ScNotRetreating only="shelter" reads it. A pure read.
        /// </summary>
        public static bool Shelters(EntityAlive e)
        {
            State st;
            return Active(e, out st) && st.Mode == Mode.Shelter;
        }

        /// <summary>ScRetreat (8.5): Shelter, not fighting back. A pure read.</summary>
        public static bool ShelterScores(EntityAlive e)
        {
            State st;
            return Active(e, out st) && st.Mode == Mode.Shelter && !st.FightBack;
        }

        /// <summary>I11 (ScTeam.FriendWeight): sheltering and not fighting back - she is not in the fight. A pure read.</summary>
        public static bool Hiding(EntityAlive e) { return ShelterScores(e); }

        /// <summary>
        /// Round k5: KITE with the wide band is on for her (hurt with nothing to heal with, a gun with rounds, no armed enemy):
        /// the posture is KITE (ScPosture: View.Wide) and SCResident's ScKiteWide backs her off from what comes within
        /// StandOffMin. It replaced rt1's stand-off and its own walk. A pure read.
        /// </summary>
        public static bool Kites(EntityAlive e)
        {
            State st;
            return Active(e, out st) && st.Mode == Mode.Kite;
        }

        /// <summary>Round k5: KITE away from home with you - ScBackOff bounds her back-off to within StandOffFollowerLeash of you. A pure read.</summary>
        internal static bool KitesAway(EntityAlive e, out EntityPlayer leader)
        {
            State st;
            leader = null;
            if (!Active(e, out st) || st.Mode != Mode.Kite || st.Region != Region.Leader) return false;
            leader = LeaderOf(e);
            return leader != null;
        }

        /// <summary>
        /// ScNotRetreatHome (on ScFleeHurt: coward false; on ScFlee: coward true): false where the retreat owns her or would at
        /// home - a member at home (live, whatever the state: trap 9) - and away from home a follower whose last scan found
        /// a gun and no armed enemy (she kites instead of fleeing). True everywhere else, and always with RetreatHome off
        /// (or, for the cowards' flee, RetreatCowards off). A pure read.
        /// </summary>
        public static bool FleeAllowed(EntityAlive e, bool coward)
        {
            if (e == null || !ScConfig.RetreatHome) return true;
            // the Fable review (finding 6): a body the retreat can never own - not one of the community's 0-XNPCCore survivors,
            // or a raider - and one whose X5 tick has not run lately (a dead hook) keep today's flight
            if (!ScXnpc.IsBody(e) || ScXnpc.IsRaider(e)) return true;
            float now = Time.time, tick;
            if (!ticked.TryGetValue(e.entityId, out tick) || now - tick > TickedSeconds) return true;
            State st;
            if (Active(e, out st)) return false;
            if (coward && !ScConfig.RetreatCowards) return true;
            Member m = ScRegistry.GetMember(e.entityId);
            if (m == null) return true;
            string why;
            Region r = Eligible(e, m, out why);
            if (r == Region.Home) return false;
            if (r == Region.Leader && !coward && states.TryGetValue(e.entityId, out st) && now - st.At <= StaleSeconds && st.Gun && !st.Armed
                && now >= st.NoKiteUntil) return false;
            return true;
        }

        /// <summary>ScNotHurtNoMeds (decision 10): one of the community's members hurt with nothing to heal with (RetreatHome on). A pure read.</summary>
        public static bool HurtNoMedsMember(EntityAlive e)
        {
            return e != null && ScConfig.RetreatHome && ScRegistry.GetMember(e.entityId) != null && ScMedic.HurtNoMeds(e, ScConfig.FleeBelowHealth);
        }

        // ------------------------------------------------------------------ the tasks' side (C#, not considerations) ----

        /// <summary>The shelter spot the task picked this retreat, if any.</summary>
        internal static bool SpotOf(EntityAlive e, out Vector3 spot, out bool hold)
        {
            State st;
            spot = Vector3.zero;
            hold = false;
            if (!Active(e, out st) || !st.HasSpot) return false;
            spot = st.Spot;
            hold = st.SpotHold;
            return true;
        }

        /// <summary>The threat's last known place (zero with none this retreat), for the pick and her facing.</summary>
        internal static bool ThreatPlace(EntityAlive e, out Vector3 pos, out EntityAlive threat)
        {
            State st;
            pos = Vector3.zero;
            threat = null;
            if (e == null || !states.TryGetValue(e.entityId, out st) || st.ThreatSeenAt <= 0f) return false;
            pos = st.ThreatPos;
            threat = st.ThreatId >= 0 && e.world != null ? e.world.GetEntity(st.ThreatId) as EntityAlive : null;
            if (threat != null && (threat.IsDead() || threat.IsSleeping)) threat = null;
            if (threat != null) pos = threat.position;
            return true;
        }

        internal static Region RegionOf(EntityAlive e)
        {
            State st;
            return Active(e, out st) ? st.Region : Region.None;
        }

        /// <summary>The spots she failed to reach this retreat and how many, for the pick.</summary>
        internal static List<Vector3i> BadSpots(EntityAlive e, out int fails)
        {
            State st;
            fails = 0;
            if (e == null || !states.TryGetValue(e.entityId, out st)) return null;
            fails = st.Fails;
            return st.Bad;
        }

        /// <summary>The task picked a spot (logs the fallback once per retreat, decision 9).</summary>
        internal static void NoteSpot(EntityAlive e, ScShelter.PickResult r)
        {
            State st;
            if (!Active(e, out st)) return;
            st.HasSpot = true;
            st.Spot = r.Spot;
            st.SpotHold = r.Kind == ScShelter.PickKind.Hold;           // fallback (c): wherever she stands when she comes back to it
            st.SpotSource = r.Source ?? "";
            st.SpotWalls = r.Walls;
            st.SpotRoofed = r.Roofed;
            st.SpotKind = r.Kind;
            bool fallback = r.Kind == ScShelter.PickKind.Walled || r.Kind == ScShelter.PickKind.Yard || r.Kind == ScShelter.PickKind.Hold;
            if (fallback && st.Fallback == null)
            {
                st.Fallback = r.Note ?? r.Kind.ToString();
                Patches_Xnpc.Count("retreat-no-shelter");
                ScLog.Out(Name(e) + ": no roofed room in the base to shelter in" + (st.Fails >= ScShelter.MaxFails ? " " + She(e) + " can reach" : "") + " - " + st.Fallback
                          + " (" + ScShelter.Summary() + ")");
            }
            else ScLog.Verbose(Name(e) + " shelters at " + Cell(r.Spot) + ": " + (r.Note ?? r.Kind.ToString()) + ", " + ScRunners.XZ(e.position, r.Spot).ToString("0.#", CultureInfo.InvariantCulture) + " m off");
        }

        /// <summary>The held spot still holds (the shelter task, every 2 s while she holds it): ScShelter.StillGood on what was picked.</summary>
        internal static bool SpotHolds(EntityAlive e, out string why)
        {
            why = null;
            State st;
            if (!Active(e, out st) || !st.HasSpot) return true;
            return ScShelter.StillGood(e, st.Spot, st.SpotKind, st.SpotWalls, st.SpotRoofed, out why);
        }

        /// <summary>Her spot is a fallback (a walled spot, a yard floor, holding where she stood): the task looks for a room again now and then.</summary>
        internal static bool OnFallback(EntityAlive e)
        {
            State st;
            return Active(e, out st) && st.HasSpot && (st.SpotKind == ScShelter.PickKind.Walled || st.SpotKind == ScShelter.PickKind.Yard || st.SpotKind == ScShelter.PickKind.Hold);
        }

        /// <summary>The task could not reach the spot (no 1 m gain in 8 s, no path, out of time): marked, the next one is picked.</summary>
        internal static void SpotFailed(EntityAlive e, Vector3 spot, string why, bool unreachable)
        {
            State st;
            if (e == null || !states.TryGetValue(e.entityId, out st)) return;
            st.HasSpot = false;
            // not picked again this retreat either way (the cache's roof and walls are as old as its scan); only a walk that
            // failed counts toward MaxFails
            if (st.SpotKind != ScShelter.PickKind.Hold && st.SpotKind != ScShelter.PickKind.Here) st.Bad.Add(World.worldToBlockPos(spot));
            if (unreachable)
            {
                st.Fails++;
                Patches_Xnpc.Count("retreat-spot-unreachable");
            }
            ScLog.Verbose(Name(e) + ": " + Her(e) + " shelter spot " + Cell(spot) + " " + why + (unreachable ? " - marked, " + st.Fails + " so far this retreat" : "") + "; picking another");
        }

        /// <summary>The shelter task handed doors she passed to ScDoors (counted per retreat for the end line).</summary>
        internal static void NoteDoors(EntityAlive e, int n)
        {
            State st;
            if (n <= 0 || e == null || !states.TryGetValue(e.entityId, out st)) return;
            st.Doors += n;
        }

        /// <summary>The other bodies' shelter spots (live retreats): the reservation and the company bonus.</summary>
        internal static void OtherSpots(int selfId, List<Vector3> into)
        {
            float now = Time.time;
            foreach (var kv in states)
            {
                State st = kv.Value;
                if (kv.Key == selfId || st.Mode != Mode.Shelter || !st.HasSpot || now - st.At > StaleSeconds) continue;
                into.Add(st.Spot);
            }
        }

        /// <summary>
        /// The speed a retreat runs at (trap 11): 0-XNPCCore's V4 survivors move in literal meters per second, so their run is
        /// RunnerRunMps through ScXnpc.PathSpeed (migration M4.1, as TravelSC runs); any other body gets vanilla's
        /// GetMoveSpeedPanic, called through an EntityAlive reference - SCore's hiding method on an EntityAliveSDX reference
        /// returns the raw value, a walk on the player body.
        /// </summary>
        internal static float RunSpeed(EntityAlive self)
        {
            if (ScNpc.IsV4(self)) return ScXnpc.PathSpeed(ScConfig.RunnerRunMps, true);
            return self.GetMoveSpeedPanic();
        }

        // ------------------------------------------------------------------ helpers ----

        private static bool Timid(EntityAlive e)
        {
            return e != null && e.Buffs != null && e.Buffs.GetCustomVar("$Timid") >= 1f;
        }

        /// <summary>Her pair's gun with rounds in her stores, unless you ordered her melee weapon (then she would back off with it).</summary>
        private static bool GunReady(EntityAlive e)
        {
            if (ScArms.OrderedMelee(e)) return false;
            Loadout l = ScLoadout.Current(e);
            if (l == null || !l.HasRanged) return false;
            ItemClass gun = ItemClass.GetItemClass(l.Ranged, true);
            return ScAmmo.RangedAction(gun) != null && ScAmmo.Rounds(e, gun) > 0;
        }

        /// <summary>
        /// KITE's wide band once a back-off is going (decision 2's gap G; round k5: UAIConsiderationScPosture action="wide" keeps
        /// ScKiteWide's run up to it): StandOffMax, but never past her engage range minus 1 m (her Shooting stars' range,
        /// ScShooting.EngageRange, and the gun's own), and at least StandOffMin + 2 m. A pure read.
        /// </summary>
        internal static float WideGap(EntityAlive e)
        {
            float cap = float.MaxValue;
            if (ScShooting.Applies(e))
            {
                string src;
                cap = ScShooting.EngageRange(ScShooting.Stars(e, out src));
            }
            Loadout l = ScLoadout.Current(e);
            ItemClass gun = l != null && l.HasRanged ? ItemClass.GetItemClass(l.Ranged, true) : null;
            ItemActionRanged ra = ScAmmo.RangedAction(gun);
            if (ra != null && ra.Range > 1f) cap = Mathf.Min(cap, ra.Range);
            float g = Mathf.Min(ScConfig.StandOffMax, cap - 1f);
            return Mathf.Max(g, ScConfig.StandOffMin + 2f);
        }

        private static float Share(EntityAlive e)
        {
            int max = e.GetMaxHealth();
            return max > 0 ? Mathf.Clamp01((float)e.Health / max) : 1f;
        }

        private static string WhyText(EntityAlive e, State st, float share)
        {
            var sb = new StringBuilder();
            if (st.Hurt) sb.Append("hurt (").Append(Pct(share)).Append("), nothing to heal with");
            if (st.Coward) sb.Append(sb.Length > 0 ? ", " : "").Append("Cowardly (not hanging back)");
            if (sb.Length == 0) sb.Append("winding down");
            if (st.Mode == Mode.Kite) sb.Append(", a gun with rounds: KITE");
            if (st.Mode == Mode.Shelter || st.Mode == Mode.None)
                sb.Append(st.Hurt && st.Gun && st.Armed ? ", armed enemies" : st.Hurt && !st.Gun ? ", no gun with rounds" : "");
            return sb.ToString();
        }

        private static string ThreatText(EntityAlive e, State st)
        {
            if (st.ThreatSeenAt <= 0f) return "none seen";
            EntityAlive t = st.ThreatId >= 0 && e.world != null ? e.world.GetEntity(st.ThreatId) as EntityAlive : null;
            string who = t != null ? t.EntityName + " (" + t.entityId + ")" : "#" + st.ThreatId;
            if (st.ThreatId < 0) return "last seen at " + Cell(st.ThreatPos) + " " + (Time.time - st.ThreatSeenAt).ToString("0", CultureInfo.InvariantCulture) + " s ago";
            return who + " at " + st.ThreatDist.ToString("0.#", CultureInfo.InvariantCulture) + " m" + (st.ThreatAfter ? ", after " + HerObj(e) : ", sees " + HerObj(e)) + (st.Armed ? ", ARMED enemies about" : "");
        }

        internal static string Name(EntityAlive e) { return e != null ? ScCommunity.DisplayName(e) + " (id " + e.entityId + ")" : "?"; }
        /// <summary>The member's pronouns for the log lines (ScMedic's rule: her look, else her class).</summary>
        internal static bool Male(EntityAlive e)
        {
            Member m = e != null ? ScRegistry.GetMember(e.entityId) : null;
            return m != null && (m.Look != null ? m.Look.Male : ScLooks.ClassIsMale(m.Class));
        }
        internal static string Her(EntityAlive e) { return Male(e) ? "his" : "her"; }
        internal static string HerObj(EntityAlive e) { return Male(e) ? "him" : "her"; }
        internal static string She(EntityAlive e) { return Male(e) ? "he" : "she"; }
        private static string ModeName(Mode m) { return m == Mode.Shelter ? "shelter" : m == Mode.Kite ? "KITE (hurt)" : "none"; }
        private static string Pct(float f) { return (f * 100f).ToString("0", CultureInfo.InvariantCulture) + "%"; }
        internal static string Cell(Vector3 p) { Vector3i c = World.worldToBlockPos(p); return c.x + " " + c.y + " " + c.z; }

        // ------------------------------------------------------------------ console ----

        /// <summary>One line for sc xnpc and the sc retreat list (a read).</summary>
        public static string Line(EntityAlive e)
        {
            if (!ScConfig.RetreatHome) return "retreat off (RetreatHome=false)";
            State st;
            Member m = e != null ? ScRegistry.GetMember(e.entityId) : null;
            if (m == null) return "retreat: not a member (the retreat is for the community's members)";
            string why;
            Region r = Eligible(e, m, out why);
            if (!Active(e, out st))
            {
                bool hurt = ScMedic.HurtNoMeds(e, ScConfig.FleeBelowHealth);
                float now0 = Time.time;
                return "retreat: none - " + (r == Region.None ? "not eligible (" + why + ")" : (r == Region.Home ? "at home" : "away with you"))
                       + (hurt ? ", HURT with nothing to heal with" : "") + (Timid(e) ? ", Cowardly" + (ScTeam.HangsBack(e) ? " (hangs back)" : "") : "")
                       + (states.TryGetValue(e.entityId, out st) && now0 < st.AfterOnlyUntil ? "; for " + (st.AfterOnlyUntil - now0).ToString("0", CultureInfo.InvariantCulture)
                          + " s more only an enemy after " + HerObj(e) + " shelters " + HerObj(e) + " again" : "");
            }
            float now = Time.time;
            var sb = new StringBuilder("retreat: ").Append(st.Mode == Mode.Shelter ? "SHELTER" : "KITE").Append(" for ").Append((now - st.Since).ToString("0", CultureInfo.InvariantCulture)).Append(" s - ")
                .Append(st.Why).Append("; threat ").Append(ThreatText(e, st));
            if (st.ClearSince > 0f) sb.Append("; clear ").Append((now - st.ClearSince).ToString("0", CultureInfo.InvariantCulture)).Append(" of ").Append(ScConfig.RetreatClearSeconds.ToString("0", CultureInfo.InvariantCulture)).Append(" s");
            if (st.Mode == Mode.Shelter)
            {
                if (st.FightBack) sb.Append("; FIGHTING BACK");
                if (st.HasSpot) sb.Append("; spot ").Append(Cell(st.Spot)).Append(" (").Append(st.SpotSource).Append(", ").Append(ScRunners.XZ(e.position, st.Spot).ToString("0.#", CultureInfo.InvariantCulture)).Append(" m off)");
                else sb.Append("; no spot yet");
                if (st.Fallback != null) sb.Append("; fallback: ").Append(st.Fallback);
                sb.Append(st.Targeted ? "; an enemy is after " + HerObj(e) : "; nothing after " + HerObj(e) + " for " + (st.NotTargetSince > 0f ? (now - st.NotTargetSince).ToString("0", CultureInfo.InvariantCulture) : "0") + " s");
            }
            else sb.Append("; backs off from what comes within ").Append(ScConfig.StandOffMin.ToString("0.#", CultureInfo.InvariantCulture)).Append(" m, up to ")
                   .Append(WideGap(e).ToString("0.#", CultureInfo.InvariantCulture)).Append(" m once going (ScKiteWide, round k5)");
            return sb.ToString();
        }

        /// <summary>The totals for sc retreat.</summary>
        public static string Report()
        {
            int live = 0, shelter = 0, kite = 0;
            float now = Time.time;
            foreach (var kv in states)
            {
                if (kv.Value.Mode == Mode.None || now - kv.Value.At > StaleSeconds) continue;
                live++;
                if (kv.Value.Mode == Mode.Shelter) shelter++; else kite++;
            }
            return live + " retreating now (" + shelter + " sheltering, " + kite + " kiting hurt); " + entered + " retreat(s) begun, " + ended + " ended this session";
        }
        /// <summary>
        /// sc retreat - the setting and its keys, the counters, the shelter cache and one line per loaded member;
        /// sc retreat &lt;id|name&gt; - one member in full: eligibility, health and meds, the state, the gates as the UAI reads
        /// them now, and the shelter she would pick now (a read: nothing is written); sc retreat shelters [n] - the shelter
        /// cache's best candidates (rescan: a new scan now).
        /// </summary>
        public static void ConsoleCmd(List<string> p, Action<string> output)
        {
            ScConfig.EnsureParsed();
            if (!ScRegistry.Loaded) { output("[SC] the registry is not loaded"); return; }
            World w = ScCommunity.World;
            if (p.Count < 2)
            {
                output("[SC] retreat (rounds rt1, k5): " + ScConfig.RetreatLine());
                output("[SC] " + Report());
                output("[SC] counters (since the world loaded): " + Patches_Xnpc.ReportPrefix("retreat-") + "; weapons: " + Patches_Xnpc.ReportPrefix("x5-retreat-"));
                output("[SC] shelter cache: " + ScShelter.Summary());
                int shown = 0;
                foreach (Member m in ScRegistry.Members.Values)
                {
                    EntityAlive e = ScCommunity.FindNpc(m.Id);
                    if (e == null || e.IsDead()) continue;
                    shown++;
                    output("[SC]   " + m.Name + " (" + e.entityId + ", " + m.Status + "): " + Line(e));
                }
                output("[SC] " + shown + " member(s) loaded - sc retreat <id|name> for one in full, sc retreat shelters [n] for the cache");
                return;
            }
            if (p[1].Equals("shelters", StringComparison.OrdinalIgnoreCase) || p[1].Equals("shelter", StringComparison.OrdinalIgnoreCase))
            {
                if (p.Count >= 3 && p[2].Equals("rescan", StringComparison.OrdinalIgnoreCase)) { ScShelter.Rescan(); output("[SC] shelter cache: a new scan starts on the next frame"); return; }
                int n = 20;
                if (p.Count >= 3 && !int.TryParse(p[2], out n)) { output("[SC] sc retreat shelters [n | rescan]"); return; }
                var lines = new List<string>();
                ScShelter.ListLines(lines, Mathf.Clamp(n, 1, 500));
                foreach (string s in lines) output(s);
                return;
            }
            EntityAlive body = FindBody(p[1]);
            if (body == null) { output("[SC] no loaded member '" + p[1] + "' - sc retreat [id|name | shelters [n | rescan]]"); return; }
            var d = new List<string>();
            Describe(body, d);
            foreach (string s in d) output(s);
        }

        private static EntityAlive FindBody(string key)
        {
            int id;
            if (int.TryParse(key, out id)) return ScCommunity.FindNpc(id);
            foreach (Member m in ScRegistry.Members.Values)
                if (m.Name != null && m.Name.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0) return ScCommunity.FindNpc(m.Id);
            return null;
        }

        /// <summary>sc retreat &lt;id|name&gt;: everything about one member (a read).</summary>
        public static void Describe(EntityAlive e, List<string> lines)
        {
            Member m = ScRegistry.GetMember(e.entityId);
            float now = Time.time;
            lines.Add("[SC] " + Name(e) + (m != null ? ", " + m.Status + (m.Status == MemberStatus.OnRun ? " (" + m.RunLeg + ")" : "") : ", not a member")
                      + " - retreat " + (ScConfig.RetreatHome ? "on" : "OFF (RetreatHome=false: nothing below acts)"));
            if (m == null) return;
            string why;
            Region r = Eligible(e, m, out why);
            lines.Add("[SC]   eligible: " + (r == Region.Home ? "AT HOME (shelter, or KITE hurt with a gun)" : r == Region.Leader ? "AWAY WITH YOU (KITE only, within " + ScConfig.StandOffFollowerLeash.ToString("0", CultureInfo.InvariantCulture) + " m of you)" : "no - " + why));
            int max = e.GetMaxHealth();
            lines.Add("[SC]   health " + e.Health + "/" + max + " (" + Pct(Share(e)) + "); heal items " + ScMedic.HealItemCount(e) + ", can treat " + (Male(e) ? "himself " : "herself ") + (ScMedic.CanTreatSelf(e) ? "yes" : "NO")
                      + "; hurt with nothing to heal with (at or below FleeBelowHealth " + Pct(ScConfig.FleeBelowHealth) + "): " + (ScMedic.HurtNoMeds(e, ScConfig.FleeBelowHealth) ? "YES" : "no")
                      + "; Cowardly: " + (Timid(e) ? "yes, " + (ScTeam.HangsBack(e) ? "HANGS BACK with " + Her(e) + " gun (tm1)" : "not hanging back") + (ScConfig.RetreatCowards ? "" : " (RetreatCowards false)") : "no")
                      + "; " + Her(e) + " pair's gun with rounds: " + (GunReady(e) ? "yes" : "no"));
            State st;
            bool active = Active(e, out st);
            if (!states.TryGetValue(e.entityId, out st)) lines.Add("[SC]   state: none yet (made the first time " + She(e) + " is hurt with nothing, or a coward, where " + She(e) + " may retreat)");
            else
            {
                lines.Add("[SC]   state: " + (st.Mode == Mode.None ? "NONE" : (active ? "" : "STALE ") + ModeName(st.Mode).ToUpperInvariant() + " (" + st.Region + ") for " + (now - st.Since).ToString("0", CultureInfo.InvariantCulture) + " s - " + st.Why)
                          + "; updated " + (now - st.At).ToString("0.0", CultureInfo.InvariantCulture) + " s ago");
                lines.Add("[SC]   last scan: threat " + ThreatText(e, st) + "; armed enemies within " + ScRunners.FleeClear.ToString("0", CultureInfo.InvariantCulture) + " m " + (st.Armed ? "YES" : "no")
                          + "; a threat present " + (st.Present ? "yes" : "no") + (st.ClearSince > 0f ? " (clear for " + (now - st.ClearSince).ToString("0", CultureInfo.InvariantCulture) + " of " + ScConfig.RetreatClearSeconds.ToString("0", CultureInfo.InvariantCulture) + " s)" : ""));
                if (st.Mode == Mode.Shelter)
                    lines.Add("[SC]   shelter: " + (st.FightBack ? "FIGHTING BACK (" + Mathf.Max(0f, st.FightBackUntil - now).ToString("0.0", CultureInfo.InvariantCulture) + " s linger)" : "not fighting back")
                              + "; spot " + (st.HasSpot ? Cell(st.Spot) + " - " + st.SpotSource + " (" + st.SpotKind + ", " + st.SpotWalls + " of 8 walls, " + (st.SpotRoofed ? "roofed" : "NO ROOF") + "), "
                                 + ScRunners.XZ(e.position, st.Spot).ToString("0.#", CultureInfo.InvariantCulture) + " m off" : "none yet")
                              + (st.Fallback != null ? "; fallback: " + st.Fallback : "") + "; unreachable this retreat " + st.Fails + "; doors handed to ScDoors " + st.Doors);
                if (st.Mode == Mode.Kite)
                    lines.Add("[SC]   KITE (round k5): the posture's wide band - ScKiteWide backs " + HerObj(e) + " off from what comes within "
                              + ScConfig.StandOffMin.ToString("0.#", CultureInfo.InvariantCulture) + " m, up to " + WideGap(e).ToString("0.#", CultureInfo.InvariantCulture)
                              + " m once going; " + ScBackOff.Line(e) + (st.Region == Region.Leader ? "; away from home: " + ScBackOff.NoPointStreak(e) + " no-point verdict(s) in a row ("
                              + CorneredAwayMax + " end it)" : ""));
                lines.Add("[SC]   exits (round k5): " + (st.Targeted ? "an enemy is after " + HerObj(e) : "nothing after " + HerObj(e) + " for "
                          + (st.NotTargetSince > 0f ? (now - st.NotTargetSince).ToString("0", CultureInfo.InvariantCulture) : "0") + " s")
                          + "; heal items " + ScMedic.HealItemCount(e) + "; restock: " + ScMedic.RestockText(e)
                          + (ScConfig.RetreatMaxSeconds > 0f ? "; a shelter ends after " + ScConfig.RetreatMaxSeconds.ToString("0", CultureInfo.InvariantCulture) + " s once nothing is after " + HerObj(e)
                             + ", no armed enemy is about and the threat is gone or " + ScConfig.ShelterMinThreatDist.ToString("0.#", CultureInfo.InvariantCulture) + "+ m off (now: "
                             + LongExitText(e, st) + ")" : "")
                          + (now < st.AfterOnlyUntil ? "; for " + (st.AfterOnlyUntil - now).ToString("0", CultureInfo.InvariantCulture) + " s more only an enemy after " + HerObj(e) + " shelters " + HerObj(e) + " again" : ""));
            }
            EntityAlive t;
            Vector3 tp;
            ThreatPlace(e, out tp, out t);
            lines.Add("[SC]   gates now: ScRetreating " + (ShelterScores(e) ? 1 : 0) + ", KITE's wide band " + (Kites(e) ? "ON" : "off")
                      + (t != null ? " (threat " + t.EntityName + " " + ScRunners.XZ(t.position, e.position).ToString("0.#", CultureInfo.InvariantCulture) + " m)" : " (no live threat)")
                      + ", ScNotRetreatHome hurt " + (FleeAllowed(e, false) ? 1 : 0) + " / coward " + (FleeAllowed(e, true) ? 1 : 0)
                      + ", ScNotRetreating " + (Owns(e) ? 0 : 1) + " (shelter only: " + (Shelters(e) ? 0 : 1) + ")"
                      + ", ScNotHurtNoMeds " + (HurtNoMedsMember(e) ? 0 : 1));
            if (r == Region.Home)
            {
                int fails;
                List<Vector3i> bad = BadSpots(e, out fails);
                ScShelter.PickResult pr = ScShelter.Pick(e, bad, fails);
                lines.Add("[SC]   " + She(e) + " would shelter now at: " + (pr.Kind == ScShelter.PickKind.None ? "-" : Cell(pr.Spot) + " - " + pr.Kind + ", " + (pr.Note ?? pr.Source)
                          + ", " + ScRunners.XZ(e.position, pr.Spot).ToString("0.#", CultureInfo.InvariantCulture) + " m off") + " (a read; " + ScShelter.Summary() + ")");
            }
            lines.Add("[SC]   doors: ScDoors shuts the ordinary doors " + She(e) + " passed that stood open (never a drawbridge or hatch " + She(e) + " did not open, never within 2.5 m of you)");
        }
    }
}
