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
    ///   - Round hr1 (2026-11-27, research/combat/HR1_DESIGN.md; the user: "members inside the base ... should try to run away first
    ///     to heal. If they are in an enclosed space with doors closed and no zombies inside they should be considered safe to
    ///     heal."): at home (region Home) the order is (1) in a safe room (ScSafeRoom.SafeToHeal) she heals where she stands, (2)
    ///     k1's straight point as above, (3) a walk into an enclosable room within RoomWalkMax (ScShelter.PickRoom; doors shut behind
    ///     her; heal on arrival if it is a safe room), (4) a path-found flight point 8-20 m away inside the base, (5) 0-XNPCCore's
    ///     own roll with an enemy within 2 m (XnpcRoll; SC's item after it), then the cornered rules and the last stand's verdict
    ///     (NoRoom, read by ScPosture). The room and flight walks are checked on the navigator's path (CheckPath). MedicMaxWait
    ///     counts from the first walk start (WalkClock, TimeoutAt), and at home the gap rule (FightsInstead: no heal with an enemy
    ///     within 2.5 m that sees her while she has a usable weapon, gun or melee, no 10 s cap) replaces t2's ShootsInstead.
    ///     Away from home nothing of this applies.
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
            /// <summary>Round t2 (re-check T2R-M1): when that wait's first shoots-instead verdict came (the 10 s cap runs from it).</summary>
            public float ShootSince = -1f;
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
            /// <summary>Round hr1: the walk's kind (WalkK1, WalkRoom, WalkFlight) and what it is going to (the log's words).</summary>
            public int Kind;
            public string WalkNote = "";
            /// <summary>Round hr1: the walk's path was checked (it reaches the spot and passes no enemy) - once, when the navigator has it.</summary>
            public bool PathChecked;
            /// <summary>
            /// Round hr1: MedicMaxWait from the first walk start - the wait this clock belongs to (WalkWait), its first walk's start, this
            /// walk's start, the deadline before this walk, and the deadline now (path length over her run speed + WalkSlackSeconds,
            /// at most WalkCapSeconds after the first walk's start).
            /// </summary>
            public float WalkWait = -1f, FirstWalkAt = -1f, WalkStartedAt, DeadlineBefore, WalkDeadline;
            /// <summary>Round hr1: the doors a room or flight walk passed (ScDoors.TrackPassed shuts them behind her), and the next look.</summary>
            public GamePath.PathEntity DoorPath;
            public int DoorIndex;
            public Vector3 LastDoor;
            public bool HasDoor;
            public float NextDoors, NextSafe;
            /// <summary>
            /// Round hr1: steps 2-5 found nothing at home in the wait that began at NoRoomWait (-1 none), last at NoRoomAt, and why (the
            /// last stand reads it: ScPosture); the wait whose "no room" line was logged.
            /// </summary>
            public float NoRoomWait = -1f, NoRoomAt, NoRoomLoggedWait = -1f;
            public string NoRoomWhy = "";
            /// <summary>Round hr1: her last roll (0-XNPCCore's own, step 5) and the wait it was for; the wait whose gap-rule line was logged.</summary>
            public float RollAt, RollWait = -1f, GapLoggedWait = -1f;
            /// <summary>
            /// Round hr1 (review HR1-M1): room and flight walks started in the wait that began at WalksWait (at most MaxWalksPerWait), and
            /// the wait whose MedicMaxWait ran out while the gap rule held the heal (TimedOutWait: no more walks in it - she fights).
            /// </summary>
            public int Walks, FailedWalks;                       // round hr1 (re-check HR1R-n1): walks whose path passed / failed
            public float WalksWait = -1f, TimedOutWait = -1f;
            /// <summary>
            /// Round hr1 (review HR1-M2): at a room walk's spot, not safe yet with a door passed on the way - she waits there until
            /// ArriveUntil (ScDoors needs about 2 s to shut it), testing afresh from NextArriveTest every ArriveTestSeconds.
            /// </summary>
            public float ArriveUntil, NextArriveTest;
            /// <summary>Round hr1 (review HR1-m1): the navigator's path when the walk began (an older task's): CheckPath never judges it.</summary>
            public GamePath.PathEntity StalePath;
        }

        /// <summary>Round hr1 (review HR1-M1): at most this many room and flight walks in one wait (a wait can pick spot after spot).</summary>
        private const int MaxWalksPerWait = 4;
        /// <summary>Round hr1 (review HR1-M2): at a room spot she waits up to this long for the door behind her to shut, testing this often.</summary>
        private const float ArriveWaitSeconds = 3f, ArriveTestSeconds = 0.5f;
        /// <summary>Round hr1 (re-check HR1R-M1): a pursuer this close ends the wait at a room spot at once (she never stands still with it on her).</summary>
        private const float ArriveLeaveDist = 3f;
        /// <summary>Round hr1 (re-check HR1R-n1): room and flight walks whose path failed CheckPath, at most this many per wait (they do not count toward MaxWalksPerWait).</summary>
        private const int MaxFailedWalksPerWait = 8;
        /// <summary>Round hr1 (review HR1-m7): the gap rule's verdict is kept this long per body (the consideration asks on every decision).</summary>
        private const float GapCacheSeconds = 0.25f;

        private sealed class GapVerdict
        {
            public float At;
            public bool Fights;
            public string Why;
        }
        private static readonly Dictionary<int, GapVerdict> gapCache = new Dictionary<int, GapVerdict>();

        /// <summary>Round nc1 (D2): a cornered member who fights on looks for room to back off again this often.</summary>
        private const float CorneredRecheckSeconds = 4f;

        /// <summary>Round hr1: a walk's kind - k1's straight step back (step 2), a walk into an enclosable room (3), a path-found flight point (4).</summary>
        private const int WalkK1 = 0, WalkRoom = 1, WalkFlight = 2;
        /// <summary>Round hr1 (step 3): an enclosable room within this walk (XZ).</summary>
        private const float RoomWalkMax = 30f;
        /// <summary>Round hr1 (step 4): the flight point's distances, longest first (inside 8-20 m), and its 12 directions round straight away.</summary>
        private static readonly float[] FlightSteps = { 20f, 14f, 8f };
        private static readonly float[] FlightAngles = { 0f, 30f, -30f, 60f, -60f, 90f, -90f, 120f, -120f, 150f, -150f, 180f };
        /// <summary>Round hr1: a flight point lies at least this much farther (XZ) from the threat than she is, and no enemy within FlightClear of it.</summary>
        private const float FlightGain = 4f, FlightClear = 5f;
        /// <summary>Round hr1: a path that ends this far (XZ) from its spot does not reach it; one that passes an enemy within PathPassClear (beyond PathPassFrom of her) is no flight.</summary>
        private const float PathReach = 1.5f, PathPassClear = 1.5f, PathPassFrom = 2f;
        /// <summary>Round hr1: MedicMaxWait from the first walk start - path length over her run speed plus this, at most WalkCapSeconds.</summary>
        private const float WalkSlackSeconds = 2f, WalkCapSeconds = 12f;
        /// <summary>Round hr1 (step 5): 0-XNPCCore rolls away from an enemy this close (its heal sequence's own 2 m), about RollLand meters (SdcsAnimPlayback.RollDistanceMetres).</summary>
        private const float RollNear = 2f, RollLand = 5f;
        /// <summary>Round hr1: at most one roll this often, and its "heal now" verdict lasts this long (the roll takes about 1.5 s plus its turn).</summary>
        private const float RollEvery = 8f, RollHealSeconds = 6f;
        /// <summary>Round hr1: the last stand reads "no room" while its last look is this recent (the looks come every CorneredRecheckSeconds).</summary>
        private const float NoRoomFresh = 8f;

        private static readonly Dictionary<int, State> states = new Dictionary<int, State>();
        private static int episodes, clears, cornered, keptUp, foughtOn;
        /// <summary>Round hr1: room walks, flight walks, rolls, safe-room heals (where she stood or on arrival), "no room" at home, gap-rule holds.</summary>
        private static int roomWalks, flightWalks, rolls, safeHeals, noRoom, gapHolds;
        private static float nextPrune;
        private static readonly List<Entity> scratchNear = new List<Entity>();
        private static readonly List<Vector3> scratchFoes = new List<Vector3>();

        public static void Reset()
        {
            states.Clear();
            episodes = clears = cornered = keptUp = foughtOn = 0;
            roomWalks = flightWalks = rolls = safeHeals = noRoom = gapHolds = 0;      // round hr1
            gapCache.Clear();                                                           // round hr1 (review HR1-m7)
            nextPrune = 0f;
            scratchNear.Clear();
            scratchFoes.Clear();
        }

        /// <summary>Round hr1 (re-check HR1R-n4): a body SC lets go of (ScLooks.Forget; entity ids are recycled) - its state and gap verdict.</summary>
        public static void Forget(int entityId)
        {
            states.Remove(entityId);
            gapCache.Remove(entityId);
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
            if (st.FightOnWait < 0f || Mathf.Abs(st.FightOnWait - waitSince) > SameWait) return false;
            // round hr1 (review HR1-M1): after a MedicMaxWait the gap rule held, only the gap rule holds her (no D2 line): a gap heals her
            if (AboveCorneredLine(e) && Mathf.Abs(st.TimedOutWait - waitSince) > SameWait) return true;
            // round hr1 (decision 6): at home the gap rule (a gun or a melee weapon, no 10 s cap); away from home t2's gun rule as it was
            string why;
            return AtHome(e) ? FightsInstead(e, out why) : ShootsInstead(e, st);
        }

        /// <summary>Round hr1: at home (round hl1's AtHome: the home zone + its margin, or the defended area in a base attack). A pure read.</summary>
        internal static bool AtHome(EntityAlive e)
        {
            return e != null && UAI.UAIConsiderationScNotHomeMember.AtHome(e.position);
        }

        /// <summary>
        /// Round hr1 (the user's decision 6, "heal only in a gap"; t2's ShootsInstead widened at home): never a heal with an enemy
        /// within HealCutRange (2.5 m) that can see her while she has a usable weapon - her pair's gun with rounds (ScRetreat.GunReady)
        /// or her melee weapon. Asked on every heal path at home (ScMedic.HeldByReach: the back-off's verdicts, the MedicMaxWait
        /// timeout, sheltering, on watch, told to stay; the cornered rule here). No 10 s cap: in a gap, or with no usable weapon, she
        /// heals. why: who and with what. A pure read (vanilla's sight cache).
        /// </summary>
        internal static bool FightsInstead(EntityAlive e, out string why)
        {
            why = null;
            if (e == null) return false;
            // round hr1 (review HR1-m7): kept GapCacheSeconds per body - Wanted asks on every decision of a cornered member
            float now = Time.time;
            GapVerdict gv;
            if (gapCache.TryGetValue(e.entityId, out gv) && now - gv.At < GapCacheSeconds) { why = gv.Why; return gv.Fights; }
            if (gv == null) gapCache[e.entityId] = gv = new GapVerdict();
            gv.At = now;
            gv.Fights = false;
            gv.Why = null;
            string weapon = UsableWeapon(e);
            if (weapon == null) return false;
            float d;
            EntityAlive t = SeeingEnemy(e, HealCutRange, out d);
            if (t == null) return false;
            why = Who(t) + " at " + d.ToString("0.0", CultureInfo.InvariantCulture) + " m sees " + ScMedic.HerObj(e) + " and " + ScMedic.She(e) + " has " + weapon;
            gv.Fights = true;
            gv.Why = why;
            return true;
        }

        /// <summary>Round hr1 (review HR1-m5): the gap rule's reach - ScSafeRoom's near test uses it too for a member with a usable weapon.</summary>
        internal const float GapRange = HealCutRange;

        /// <summary>Round hr1: her usable weapon in a few words (her pair's gun with rounds, else her pair's melee weapon), null with none.</summary>
        internal static string UsableWeapon(EntityAlive e)
        {
            Loadout l = ScLoadout.Current(e);
            if (l == null) return null;
            if (ScRetreat.GunReady(e)) return ScMedic.Her(e) + " " + l.Ranged + " with rounds";
            if (!string.IsNullOrEmpty(l.Melee) && ItemClass.GetItemClass(l.Melee, true) != null) return ScMedic.Her(e) + " " + l.Melee;
            return null;
        }

        /// <summary>Round hr1: the nearest awake, living enemy within r that can see her (or she it: a clear line), null when none.</summary>
        private static EntityAlive SeeingEnemy(EntityAlive e, float r, out float dist)
        {
            dist = -1f;
            World w = e.world;
            if (w == null) return null;
            EntityAlive best = null;
            float bestD2 = r * r;
            scratchNear.Clear();
            try
            {
                w.GetEntitiesInBounds(typeof(EntityAlive), new Bounds(e.position, new Vector3(2f * r, 2f * r, 2f * r)), scratchNear);
                for (int i = 0; i < scratchNear.Count; i++)
                {
                    EntityAlive a = scratchNear[i] as EntityAlive;
                    if (a == null || a == e || a.IsDead() || a.IsSleeping) continue;
                    float d2 = (a.position - e.position).sqrMagnitude;
                    if (d2 > bestD2) continue;
                    if (!EntityTargetingUtilities.IsEnemy(e, a) || global::Harmony.ZombieFeatures.InertEntity.IsInert(a)) continue;
                    if (!a.CanSee(e) && !e.CanSee(a)) continue;
                    best = a;
                    bestD2 = d2;
                }
            }
            finally { scratchNear.Clear(); }
            if (best != null) dist = Mathf.Sqrt(bestD2);
            return best;
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
            // review T2-M3: never past ShootInsteadMaxSeconds (then the heal comes, as before) - re-check T2R-M1: counted from this wait's
            // first shoots-instead verdict, not the wait's start (a wait cornered above the line can last longer than that) - and only
            // at a pursuer she can see (vanilla's own sight test: one behind a door or a wall cannot cut the heal short)
            if (st.WaitSince < 0f) return false;
            if (Mathf.Abs(st.ShootLoggedWait - st.WaitSince) <= SameWait && Time.time - st.ShootSince > ShootInsteadMaxSeconds) return false;
            float dist;
            EntityAlive t = Pursuer(e, out dist);
            return t != null && dist <= HealCutRange && e.CanSee(t);
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

        /// <summary>
        /// Round hr1: when ScMedic's MedicMaxWait runs out for the wait that began at waitSince - from the first walk start of that
        /// wait (step 2, 3 or 4: the walk's path length over her run speed plus WalkSlackSeconds, at most WalkCapSeconds after the
        /// first walk began; each later walk may push it out to that cap), else MedicMaxWait from the wait's start as before
        /// (MedicMaxWait 0: at once). Only ScMedic's home path asks. A pure read.
        /// </summary>
        internal static float TimeoutAt(EntityAlive e, float waitSince, out bool walked)
        {
            walked = false;
            float plain = waitSince + ScConfig.MedicMaxWait;
            State st;
            if (ScConfig.MedicMaxWait <= 0f || e == null || !states.TryGetValue(e.entityId, out st)) return plain;
            if (st.FirstWalkAt < 0f || Mathf.Abs(st.WalkWait - waitSince) > SameWait) return plain;
            walked = true;
            return st.WalkDeadline;
        }

        /// <summary>
        /// Round hr1 (the last stand, ScPosture): steps 2-5 of the heal order found nothing at home for her current wait, at a look
        /// under NoRoomFresh ago (a walk that finds room clears it). why: what each step said. A pure read.
        /// </summary>
        internal static bool NoRoomNow(EntityAlive e, out string why)
        {
            why = null;
            State st;
            float waited;
            if (e == null || !states.TryGetValue(e.entityId, out st) || st.NoRoomWait < 0f) return false;
            float now = Time.time;
            if (now - st.NoRoomAt > NoRoomFresh || !ScMedic.WaitingToHeal(e, out waited) || Mathf.Abs(st.NoRoomWait - (now - waited)) > SameWait) return false;
            why = st.NoRoomWhy;
            return true;
        }

        /// <summary>Round hr1: her roll (step 5) is still playing - 0-XNPCCore's busy flag up within RollHealSeconds of it (ScMedic keeps her wait fresh meanwhile). A pure read.</summary>
        internal static bool Rolling(EntityAlive e)
        {
            State st;
            return e != null && states.TryGetValue(e.entityId, out st) && st.RollAt > 0f && Time.time - st.RollAt < RollHealSeconds && e.GetCVar("SdcsAnimBusy") >= 0.5f;
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
            gone.Clear();                                            // round hr1 (review HR1-m7): the gap verdicts too
            foreach (var kv in gapCache) if (now - kv.Value.At > 60f) gone.Add(kv.Key);
            foreach (int id in gone) gapCache.Remove(id);
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
            bool home = region == ScRetreat.Region.Home;                     // round hr1: the heal order is for home only
            // round hr1 (step 1): in a safe room she heals where she stands - never a walk out of it
            string room;
            if (home && ScSafeRoom.SafeToHeal(self, out room))
            {
                st.ClearUntil = now + ClearHoldSeconds;
                Note(st, "in a safe room - heals where " + ScMedic.She(self) + " stands (hr1)", now);
                ScMedic.CheckSoon(self.entityId);
                return false;
            }
            Vector3 p = Vector3.zero;
            float step = 0f, angle = 0f;
            string why = null;
            // round hr1 (review HR1-M1): once MedicMaxWait ran out in this wait with the gap rule holding her heal, no more walks - she
            // fights (the cornered rules below, the last stand), and heals the moment a gap opens
            bool walksOff = home && Mathf.Abs(st.TimedOutWait - st.WaitSince) <= SameWait;
            if (walksOff) why = "MedicMaxWait ran out while an enemy within " + HealCutRange.ToString("0.#", CultureInfo.InvariantCulture) + " m saw " + ScMedic.HerObj(self) + ": no more walks this wait";
            if (walksOff || !ScBackOff.Pick(self, t, MaxStep, region, leader, st.Memory, out p, out step, out angle, out why))
            {
                // round hr1 (steps 3-5): at home, a walk into an enclosable room, a path-found flight point, 0-XNPCCore's roll - and
                // only when none works the cornered rules below (and, with two or more on her, the last stand: ScPosture)
                if (home)
                {
                    string roomWhy = why, flightWhy = why, rollWhy;
                    if (!walksOff && StartRoomWalk(self, st, t, bound, now, out roomWhy)) return true;
                    if (!walksOff && StartFlightWalk(self, st, t, dist, bound, now, out flightWhy)) return true;
                    if (TryRoll(self, st, t, dist, now, out rollWhy)) return false;
                    NoRoom(self, st, t, dist, why, roomWhy, flightWhy, rollWhy, now);
                }
                // round nc1 (D2): cornered above CorneredHealBelow she fights on (no heal-now); the back-off is looked for again in
                // CorneredRecheckSeconds, and at or below the line the next look heals her at once (below)
                bool above = AboveCorneredLine(self) && !walksOff;   // round hr1 (review HR1-M1): after the timeout only the gap rule
                // round hr1 (decision 6): at home the gap rule - a gun OR a melee weapon, no 10 s cap; away from home t2's gun rule
                string fightWhy = null;
                bool fights = home ? FightsInstead(self, out fightWhy) : ShootsInstead(self, st);
                if (above || fights)  // round t2: or a heal an enemy at arm's length would cut short, with a gun ready
                {
                    bool first = st.FightOnWait < 0f || Mathf.Abs(st.FightOnWait - st.WaitSince) > SameWait;
                    st.FightOnWait = st.WaitSince;
                    st.FightOnUntil = now + CorneredRecheckSeconds;
                    // round hr1: at home the gap rule's line once per wait (it replaces t2's "keeps shooting" there)
                    if (!above && home && Mathf.Abs(st.GapLoggedWait - st.WaitSince) > SameWait)
                    {
                        st.GapLoggedWait = st.WaitSince;
                        gapHolds++;
                        Patches_Xnpc.Count("heal-gap-fights-instead");
                        Note(st, "CORNERED " + bound + " - fights instead, no gap to heal in (hr1)", now);
                        ScLog.Out(ScMedic.BodyName(self) + " has no room to heal " + bound + " - " + ScMedic.She(self) + " fights instead at " + self.Health + "/" + self.GetMaxHealth()
                                  + ": " + fightWhy + " (round hr1, heal only in a gap)");
                    }
                    // round t2 (review T2-m3): the "keeps shooting" line once per wait, also when the wait was first cornered above the line
                    if (!above && !home && Mathf.Abs(st.ShootLoggedWait - st.WaitSince) > SameWait)
                    {
                        st.ShootLoggedWait = st.WaitSince;
                        st.ShootSince = now;                         // re-check T2R-M1: the cap's start
                        Note(st, "CORNERED " + bound + " - keeps shooting, a heal would be cut short (t2)", now);     // re-check of T2-m3
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
                        if (above) Note(st, "CORNERED " + bound + " - fights on above " + Pct(ScConfig.CorneredHealBelow), now);
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
            st.Kind = WalkK1;                                        // round hr1: step 2, k1's straight point
            st.WalkNote = step.ToString("0", CultureInfo.InvariantCulture) + " m straight back";
            st.PathChecked = true;                                   // round hr1: a straight walk k1 already proved clear
            st.NoRoomWait = -1f;                                     // round hr1: room again - no last stand for this wait's verdict
            st.NoRoomAt = 0f;                                        // round hr1 (review HR1-m2): nor for the stand's hold
            st.ArriveUntil = 0f;                                     // round hr1 (review HR1-M2)
            WalkClock(self, st, step, now);                          // round hr1: MedicMaxWait from the first walk start
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

        // ------------------------------------------------------------------ round hr1: steps 3-5 of the heal order at home ----

        /// <summary>Round hr1: her run in meters per second (0-XNPCCore's V4 bodies move in literal meters: RunnerRunMps, as RunSpeed paths).</summary>
        private static float RunMps(EntityAlive self)
        {
            return Mathf.Max(1f, ScConfig.RunnerRunMps);
        }

        /// <summary>
        /// Round hr1: MedicMaxWait from the first walk start (the user's plan: "the walk's path length over her run speed plus 2 s,
        /// capped at 12 s"). The first walk of a wait starts the clock; each later walk may push the deadline out to its own length's
        /// worth, never past WalkCapSeconds after the first walk began. len: the walk's length so far known (a straight line; a
        /// room or flight walk's path replaces it once the navigator has it, PathLength).
        /// </summary>
        private static void WalkClock(EntityAlive self, State st, float len, float now)
        {
            float budget = len / RunMps(self) + WalkSlackSeconds;
            st.WalkStartedAt = now;
            if (st.FirstWalkAt < 0f || Mathf.Abs(st.WalkWait - st.WaitSince) > SameWait)
            {
                st.WalkWait = st.WaitSince;
                st.FirstWalkAt = now;
                st.DeadlineBefore = now;
                st.WalkDeadline = now + Mathf.Min(budget, WalkCapSeconds);
                return;
            }
            st.DeadlineBefore = st.WalkDeadline;
            st.WalkDeadline = Mathf.Min(st.FirstWalkAt + WalkCapSeconds, Mathf.Max(st.WalkDeadline, now + budget));
        }

        /// <summary>Round hr1: the points she could not reach this wait (k1's memory, its expired ones dropped), for the room and flight picks.</summary>
        private static List<Vector3> LiveBad(State st, float now)
        {
            ScBackOff.Last l = st.Memory;
            for (int i = l.Bad.Count - 1; i >= 0; i--)
                if (now >= l.BadUntil[i]) { l.Bad.RemoveAt(i); l.BadUntil.RemoveAt(i); }
            return l.Bad;
        }

        /// <summary>Round hr1: a room or flight walk's spot she could not use (no path, a path past an enemy, not safe on arrival): set aside 10 s.</summary>
        private static void SetAside(State st, float now)
        {
            st.Memory.Bad.Add(st.Point);
            st.Memory.BadUntil.Add(now + 10f);
        }

        /// <summary>Round hr1: a room or flight walk starts - the episode, its clock, the path (doors shut behind her by TickWalk).</summary>
        private static void BeginWalk(EntityAlive self, State st, int kind, Vector3 p, string note, string bound, float now)
        {
            st.Kind = kind;
            st.Point = p;
            st.Step = ScRunners.XZ(self.position, p);
            st.Angle = 0f;
            st.Bound = bound;
            st.WalkNote = note;
            st.PathChecked = false;
            st.DoorPath = null;
            st.DoorIndex = 0;
            st.HasDoor = false;
            st.NextDoors = 0f;
            st.NextSafe = 0f;
            st.FightOnWait = -1f;                                    // room again: the cornered rule's verdict goes
            st.FightOnUntil = 0f;
            st.NoRoomWait = -1f;                                     // and the last stand's
            st.NoRoomAt = 0f;                                        // round hr1 (review HR1-m2): and the stand's hold
            st.ArriveUntil = 0f;                                     // round hr1 (review HR1-M2): no wait at a spot yet
            st.NextArriveTest = 0f;
            st.GainAt = 0f;                                          // F4's straight-line gain test is k1's (a path may first go sideways)
            st.Walking = true;
            st.WalkAt = now;
            // round hr1 (review HR1-M1): counted per wait - MaxWalksPerWait at most
            if (Mathf.Abs(st.WalksWait - st.WaitSince) > SameWait) { st.WalksWait = st.WaitSince; st.Walks = 0; st.FailedWalks = 0; }
            // round hr1 (re-check HR1R-n1): counted once its path passes CheckPath (a failed one counts toward MaxFailedWalksPerWait)
            // a path is longer than the straight line: 1.4 x until the navigator has it (CheckPath)
            WalkClock(self, st, st.Step * 1.4f, now);
            if (!st.Counted)
            {
                st.Counted = true;
                episodes++;
                Patches_Xnpc.Count("heal-backoff");
            }
            if (self.moveHelper != null) self.moveHelper.CanBreakBlocks = false;
            // round hr1 (review HR1-m1): the last action's path must not be judged as this walk's - the navigator is cleared first and
            // CheckPath skips the path that was there (StalePath) and waits while the path thread still works on hers
            GamePath.PathNavigate nav = self.getNavigator();
            st.StalePath = nav != null ? nav.getPath() : null;
            ScCommunity.DropPath(self);
            self.FindPath(p, ScRetreat.RunSpeed(self), false, null);
        }

        /// <summary>Round hr1 (review HR1-M1): she may start another room or flight walk in this wait (fewer than MaxWalksPerWait so far).</summary>
        private static bool WalkAllowed(State st, out string why)
        {
            why = null;
            if (Mathf.Abs(st.WalksWait - st.WaitSince) <= SameWait && st.Walks >= MaxWalksPerWait)
            {
                why = st.Walks + " room or flight walk(s) this wait already (at most " + MaxWalksPerWait + ")";
                return false;
            }
            if (Mathf.Abs(st.WalksWait - st.WaitSince) <= SameWait && st.FailedWalks >= MaxFailedWalksPerWait)
            {
                why = st.FailedWalks + " room or flight spot(s) with no usable path this wait (at most " + MaxFailedWalksPerWait + "; round hr1 re-check)";
                return false;
            }
            return true;
        }

        /// <summary>
        /// Round hr1 (review HR1-M1): ScMedic's MedicMaxWait ran out for the wait that began at waitSince and the gap rule held the
        /// heal (an enemy within 2.5 m sees her, she has a weapon). The walk ends, she fights on (FightOnWait: Wanted lets the fight
        /// actions have her until the gap opens - ScMedic's next check then heals her), no more walks this wait (TimedOutWait), and
        /// the "no room" verdict is set so the last stand can start with two or more on her. Written by ScMedic's check (the task's
        /// state otherwise). Logged once per wait.
        /// </summary>
        internal static void TimedOutHeld(EntityAlive e, float waitSince)
        {
            if (e == null) return;
            State st = StateOf(e.entityId);
            float now = Time.time;
            bool first = Mathf.Abs(st.TimedOutWait - waitSince) > SameWait;
            st.TimedOutWait = waitSince;
            st.Walking = false;
            st.ArriveUntil = 0f;
            st.FightOnWait = waitSince;
            st.FightOnUntil = now + CorneredRecheckSeconds;
            st.NoRoomWait = waitSince;
            st.NoRoomAt = now;
            st.NoRoomWhy = "MedicMaxWait ran out with an enemy within " + HealCutRange.ToString("0.#", CultureInfo.InvariantCulture) + " m that sees " + ScMedic.HerObj(e);
            st.OutcomeAt = now;
            if (!first) return;
            Patches_Xnpc.Count("heal-timeout-fights-on");
            Note(st, "MedicMaxWait ran out in the fight - fights on, no more walks this wait (hr1)", now);
            ScLog.Out(ScMedic.BodyName(e) + "'s time to get clear ran out with an enemy at arm's length - " + ScMedic.She(e)
                      + " stops running (no more walks this wait; 0-XNPCCore's roll at most) and fights on, and heals the moment a gap opens (round hr1 review)");   // round hr1 (re-check HR1R-n2)
        }

        /// <summary>
        /// Round hr1 (review HR1-m2): her last "no room" verdict at home is under seconds old (any wait) - the last stand holds through
        /// the item's cooldown after a heal in a gap. A pure read.
        /// </summary>
        internal static bool NoRoomRecently(EntityAlive e, float seconds, out string why)
        {
            why = null;
            State st;
            if (e == null || !states.TryGetValue(e.entityId, out st) || st.NoRoomAt <= 0f || Time.time - st.NoRoomAt > seconds) return false;
            why = st.NoRoomWhy;
            return true;
        }

        /// <summary>
        /// Round hr1 (step 3): a walk into an enclosable room within RoomWalkMax (ScShelter.PickRoom: a candidate of the shelter scan in
        /// a room whose doors are walls, at least 2.5 m from every door, a walk that never takes her nearer the threat). True: walking.
        /// </summary>
        private static bool StartRoomWalk(EntityAlive self, State st, EntityAlive t, string bound, float now, out string why)
        {
            why = null;
            if (!ScConfig.RetreatHome) { why = "no shelter scan (RetreatHome false)"; return false; }
            if (!WalkAllowed(st, out why)) return false;                     // round hr1 (review HR1-M1)
            Vector3 spot;
            string note;
            if (!ScShelter.PickRoom(self, t, RoomWalkMax, LiveBad(st, now), out spot, out note)) { why = note; return false; }
            BeginWalk(self, st, WalkRoom, spot, note, bound, now);
            roomWalks++;
            Patches_Xnpc.Count("heal-room-walk");
            Note(st, "ran to a room to heal: " + note, now);
            ScLog.Out(ScMedic.BodyName(self) + " has no straight step back from " + Who(t) + " - " + ScMedic.She(self) + " runs to " + note
                      + " to heal there, shutting doors behind " + ScMedic.HerObj(self) + " (round hr1, step 3)");
            return true;
        }

        /// <summary>
        /// Round hr1 (step 4): a flight point FlightSteps (20, 14, 8 m) away on 12 directions round straight away from the threat -
        /// a standable floor within 3 cells of her level, inside the base, not one she failed to reach, at least FlightGain farther
        /// from the threat than she is and no enemy within FlightClear of it - longest step first, the straightest and least crowded
        /// on it. Not a straight walk: the navigator's path must reach it (CheckPath). True: walking.
        /// </summary>
        private static bool StartFlightWalk(EntityAlive self, State st, EntityAlive t, float dist, string bound, float now, out string why)
        {
            Vector3 p;
            string note;
            if (!WalkAllowed(st, out why)) return false;                     // round hr1 (review HR1-M1)
            if (!PickFlight(self, t, st, now, out p, out note)) { why = note; return false; }
            why = null;
            BeginWalk(self, st, WalkFlight, p, note, bound, now);
            flightWalks++;
            Patches_Xnpc.Count("heal-flight-walk");
            Note(st, "ran to a flight point to heal: " + note, now);
            ScLog.Out(ScMedic.BodyName(self) + " has no straight step back from " + Who(t) + " at " + dist.ToString("0.0", CultureInfo.InvariantCulture) + " m and no room to run to - "
                      + ScMedic.She(self) + " runs to " + note + " (a path, inside the base) to heal (round hr1, step 4)");
            return true;
        }

        private static bool PickFlight(EntityAlive self, EntityAlive t, State st, float now, out Vector3 point, out string why)
        {
            point = Vector3.zero;
            why = null;
            World w = self.world;
            if (w == null || t == null) { why = "no threat"; return false; }
            Vector3 me = self.position;
            Vector3 away = ScBackOff.AwayDir(self, t.position);
            int yRef = Mathf.FloorToInt(me.y + 0.05f);
            float meFromT = ScRunners.XZ(me, t.position);
            GatherFoes(self, FlightSteps[0] + FlightClear + 2f);
            List<Vector3> bad = LiveBad(st, now);
            string lastWhy = "no floor";
            int tried = 0;
            try
            {
                for (int si = 0; si < FlightSteps.Length; si++)
                {
                    float s = FlightSteps[si], best = float.MaxValue;
                    for (int k = 0; k < FlightAngles.Length; k++)
                    {
                        tried++;
                        Vector3 q = me + Quaternion.Euler(0f, FlightAngles[k], 0f) * away * s;
                        int x = Mathf.FloorToInt(q.x), z = Mathf.FloorToInt(q.z), y;
                        if (!ScCommunity.FindStandable(w, x, z, yRef, out y) || Math.Abs(y - yRef) > 3) { lastWhy = "no floor near her level"; continue; }
                        Vector3 pc = new Vector3(x + 0.5f, y, z + 0.5f);
                        if (!UAI.UAIConsiderationScNotHomeMember.AtHome(pc)) { lastWhy = "outside the home zone + margin"; continue; }
                        if (ScRunners.XZ(pc, t.position) < meFromT + FlightGain) { lastWhy = "not " + FlightGain.ToString("0", CultureInfo.InvariantCulture) + " m farther from the threat"; continue; }
                        bool bad1 = false;
                        for (int i = 0; i < bad.Count && !bad1; i++) bad1 = ScRunners.XZ(bad[i], pc) <= 1.2f && Mathf.Abs(bad[i].y - pc.y) < 1.5f;
                        if (bad1) { lastWhy = "a point " + ScMedic.She(self) + " just failed to reach"; continue; }
                        int crowd = 0;
                        bool foeAt = false;
                        for (int i = 0; i < scratchFoes.Count; i++)
                        {
                            float fd = ScRunners.XZ(scratchFoes[i], pc);
                            if (fd < FlightClear) { foeAt = true; break; }
                            if (fd < 10f) crowd++;
                        }
                        if (foeAt) { lastWhy = "an enemy within " + FlightClear.ToString("0", CultureInfo.InvariantCulture) + " m of it"; continue; }
                        Vector3i cell = new Vector3i(x, y, z);
                        string gw;
                        if (!ScBackOff.Standable(w, self, cell, out gw)) { lastWhy = gw; continue; }     // round cb1: the reason (a wreck beside it, a model under it)
                        float score = Mathf.Abs(FlightAngles[k]) / 30f + 2f * crowd;
                        if (score < best) { best = score; point = pc; }
                    }
                    if (best < float.MaxValue)
                    {
                        why = s.ToString("0", CultureInfo.InvariantCulture) + " m off at " + ScRetreat.Cell(point) + ", " + ScRunners.XZ(point, t.position).ToString("0", CultureInfo.InvariantCulture) + " m from the threat";
                        return true;
                    }
                }
            }
            finally { scratchFoes.Clear(); }
            why = "none of " + tried + " points 8-20 m away works (last: " + lastWhy + ")";
            return false;
        }

        /// <summary>Round hr1: the awake, living enemies within r of her (SCore's IsEnemy, not inert), their places into scratchFoes.</summary>
        private static void GatherFoes(EntityAlive self, float r)
        {
            scratchFoes.Clear();
            scratchNear.Clear();
            World w = self.world;
            if (w == null) return;
            try
            {
                w.GetEntitiesInBounds(typeof(EntityAlive), new Bounds(self.position, new Vector3(2f * r, 2f * r, 2f * r)), scratchNear);
                for (int i = 0; i < scratchNear.Count; i++)
                {
                    EntityAlive a = scratchNear[i] as EntityAlive;
                    if (a == null || a == self || a.IsDead() || a.IsSleeping) continue;
                    if (!EntityTargetingUtilities.IsEnemy(self, a) || global::Harmony.ZombieFeatures.InertEntity.IsInert(a)) continue;
                    scratchFoes.Add(a.position);
                }
            }
            finally { scratchNear.Clear(); }
        }

        /// <summary>
        /// Round hr1: a room or flight walk's path, once the navigator has it - it must reach its spot (end within PathReach) and pass
        /// no enemy within PathPassClear beyond PathPassFrom of her; its length then sets the walk's MedicMaxWait budget. False: the
        /// spot is set aside 10 s and the walk ends (the task starts again: the next step). True: still planning, or it passed.
        /// </summary>
        private static bool CheckPath(EntityAlive self, State st, float now)
        {
            GamePath.PathNavigate nav = self.getNavigator();
            GamePath.PathEntity path = nav != null ? nav.getPath() : null;
            if (path == null || !path.HasPoints() || path.getCurrentPathLength() <= 0) return true;    // still planning (no path at all: TickWalk's retry)
            // round hr1 (review HR1-m1): not the path an earlier action left, nor one while the path thread still works on hers
            if (ReferenceEquals(path, st.StalePath)) return true;
            if (GamePath.PathFinderThread.Instance != null && GamePath.PathFinderThread.Instance.IsCalculatingPath(self.entityId)) return true;
            st.PathChecked = true;
            st.StalePath = null;
            Vector3 end = path.GetEndPos();
            string kind = st.Kind == WalkRoom ? "room" : "flight point";
            if (ScRunners.XZ(end, st.Point) > PathReach || Mathf.Abs(end.y - st.Point.y) > 1.5f)
            {
                SetAside(st, now);
                Patches_Xnpc.Count("heal-walk-no-path");
                ScLog.Out(ScMedic.BodyName(self) + "'s path to the " + kind + " at " + ScRetreat.Cell(st.Point) + " ends " + ScRunners.XZ(end, st.Point).ToString("0.#", CultureInfo.InvariantCulture)
                          + " m short - set aside, the next step (round hr1)");
                return false;
            }
            // round nv1: a path through a gap that stopped a member of her base (ScStuck) - set aside, the next step
            Vector3i gap;
            if (ScStuck.PathCrossesGap(self, path, out gap))
            {
                SetAside(st, now);
                Patches_Xnpc.Count("heal-walk-through-gap");
                ScLog.Out(ScMedic.BodyName(self) + "'s path to the " + kind + " at " + ScRetreat.Cell(st.Point) + " runs through the remembered gap at " + gap
                          + " - set aside, the next step (round nv1)");
                return false;
            }
            GatherFoes(self, 40f);
            try
            {
                float len = 0f;
                Vector3 prev = self.position;
                int n = path.getCurrentPathLength();
                for (int i = Math.Max(0, path.getCurrentPathIndex()); i < n; i++)
                {
                    GamePath.PathPoint pt = path.getPathPointFromIndex(i);
                    if (pt == null) break;
                    Vector3 q = pt.projectedLocation;
                    len += Vector3.Distance(prev, q);
                    prev = q;
                    // round hr1 (review HR1-n5): every walk stays inside the base (decision 2) - the path too, not only its end
                    if (!UAI.UAIConsiderationScNotHomeMember.AtHome(q))
                    {
                        SetAside(st, now);
                        Patches_Xnpc.Count("heal-walk-leaves-base");
                        ScLog.Out(ScMedic.BodyName(self) + "'s path to the " + kind + " at " + ScRetreat.Cell(st.Point) + " leaves the base at " + ScRetreat.Cell(q)
                                  + " - set aside, the next step (round hr1 review)");
                        return false;
                    }
                    if (len < PathPassFrom) continue;
                    for (int f = 0; f < scratchFoes.Count; f++)
                    {
                        if (ScRunners.XZ(scratchFoes[f], q) >= PathPassClear || Mathf.Abs(scratchFoes[f].y - q.y) > 2f) continue;
                        SetAside(st, now);
                        Patches_Xnpc.Count("heal-walk-passes-enemy");
                        ScLog.Out(ScMedic.BodyName(self) + "'s path to the " + kind + " at " + ScRetreat.Cell(st.Point) + " passes an enemy at " + ScRetreat.Cell(scratchFoes[f])
                                  + " - set aside, the next step (round hr1)");
                        return false;
                    }
                }
                // the budget from the path's own length (WalkClock's rule, from this walk's start)
                float budget = len / RunMps(self) + WalkSlackSeconds;
                st.WalkDeadline = Mathf.Min(st.FirstWalkAt + WalkCapSeconds, Mathf.Max(st.DeadlineBefore, st.WalkStartedAt + budget));
                ScLog.Verbose(ScMedic.BodyName(self) + "'s path to the " + kind + " is " + len.ToString("0.#", CultureInfo.InvariantCulture) + " m - MedicMaxWait runs out "
                              + Mathf.Max(0f, st.WalkDeadline - now).ToString("0.#", CultureInfo.InvariantCulture) + " s from now (round hr1)");
            }
            finally { scratchFoes.Clear(); }
            st.Walks++;                                              // round hr1 (re-check HR1R-n1): the walk counts once its path passed
            return true;
        }

        /// <summary>
        /// Round hr1 (step 5): with an enemy within RollNear and nothing above found room, 0-XNPCCore's own roll - its probe
        /// (SdcsRollProbe.ProbeForHeal, as its heal sequence asks it) for a clear heading whose landing spot (RollLand along it) is
        /// inside the base, then its forced roll (AvatarSdcsNpcController.StartForcedRoll). Not its heal sequence: X15 still skips its
        /// self-heal decision and nothing adds SdcsIsHealing (its free heal stays refused); SC's medic uses HER item once she is up
        /// (the roll's "heal now" verdict for this wait, which the gap rule judges at home), else the last stand. True: she rolls.
        /// </summary>
        private static bool TryRoll(EntityAlive self, State st, EntityAlive t, float dist, float now, out string why)
        {
            why = null;
            if (dist > RollNear) { why = "no enemy within " + RollNear.ToString("0", CultureInfo.InvariantCulture) + " m (0-XNPCCore rolls only from one that close)"; return false; }
            if (st.RollAt > 0f && now - st.RollAt < RollEvery) { why = "rolled " + (now - st.RollAt).ToString("0", CultureInfo.InvariantCulture) + " s ago"; return false; }
            if (!XnpcRoll.Bind(out why)) return false;
            float yaw;
            string heading, noClear;
            if (!XnpcRoll.Probe(self, t, out yaw, out heading, out noClear)) { why = "0-XNPCCore's roll probe found no clear heading (" + (noClear ?? "no probe") + ")"; return false; }
            float rad = yaw * Mathf.Deg2Rad;
            Vector3 land = self.position + new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad)) * RollLand;
            if (!UAI.UAIConsiderationScNotHomeMember.AtHome(land)) { why = "its clear heading (" + heading + ") lands outside the base at " + ScRetreat.Cell(land); return false; }
            if (!XnpcRoll.Start(self, yaw - self.rotation.y)) { why = "0-XNPCCore refused the roll (busy, downed, or no clip)"; return false; }
            st.RollAt = now;
            st.RollWait = st.WaitSince;
            st.HealNowWait = st.WaitSince;
            st.HealNowUntil = now + RollHealSeconds;
            st.HealNowWhy = "rolled clear of " + Who(t) + " (0-XNPCCore's own roll, heading " + heading + ")";
            st.NoRoomWait = st.WaitSince;                            // steps 2-4 found nothing: the last stand's verdict if the roll does not get her clear
            st.NoRoomAt = now;
            st.NoRoomWhy = "nothing but a roll";
            rolls++;
            Patches_Xnpc.Count("heal-roll");
            Note(st, "rolled clear (0-XNPCCore's roll, " + heading + ")", now);
            ScLog.Out(ScMedic.BodyName(self) + " has no room to heal at home and " + Who(t) + " at " + dist.ToString("0.0", CultureInfo.InvariantCulture) + " m - " + ScMedic.She(self)
                      + " rolls clear (0-XNPCCore's own roll, heading " + heading + ", landing at " + ScRetreat.Cell(land) + " inside the base); " + ScMedic.Her(self)
                      + " own item next if no enemy within " + HealCutRange.ToString("0.#", CultureInfo.InvariantCulture) + " m sees " + ScMedic.HerObj(self) + " (round hr1, step 5)");
            ScMedic.CheckSoon(self.entityId);
            return true;
        }

        /// <summary>
        /// Round hr1: steps 2-5 found nothing at home for this wait - the verdict the last stand reads (ScPosture, with two or more
        /// on her) and one log line per wait with what each step said. The cornered rules (D2, the gap rule) follow in StartWalk.
        /// </summary>
        private static void NoRoom(EntityAlive self, State st, EntityAlive t, float dist, string k1Why, string roomWhy, string flightWhy, string rollWhy, float now)
        {
            st.NoRoomWait = st.WaitSince;
            st.NoRoomAt = now;
            st.NoRoomWhy = "no straight step back (" + k1Why + "); no room within " + RoomWalkMax.ToString("0", CultureInfo.InvariantCulture) + " m (" + roomWhy + "); no flight point ("
                           + flightWhy + "); no roll (" + rollWhy + ")";
            if (Mathf.Abs(st.NoRoomLoggedWait - st.WaitSince) <= SameWait) return;
            st.NoRoomLoggedWait = st.WaitSince;
            noRoom++;
            Patches_Xnpc.Count("heal-no-room-home");
            ScLog.Out(ScMedic.BodyName(self) + " finds no room to heal at home from " + Who(t) + " at " + dist.ToString("0.0", CultureInfo.InvariantCulture) + " m: " + st.NoRoomWhy
                      + " - the cornered rules, and with two or more on " + ScMedic.HerObj(self) + " the last stand (round hr1)");
        }

        /// <summary>
        /// Round hr1 (step 5): 0-XNPCCore's public roll, bound by name once (no new decompiling: SdcsRollProbe.ProbeForHeal and
        /// AvatarSdcsNpcController.StartForcedRoll, both public in 3.4.06 final). Missing: step 5 is off for the game, one warning.
        /// </summary>
        private static class XnpcRoll
        {
            private static bool tried, ok;
            private static string missing;
            private static System.Reflection.MethodInfo probe, start;

            internal static bool Bind(out string why)
            {
                why = null;
                if (!tried)
                {
                    tried = true;
                    try
                    {
                        Type rp = typeof(AvatarSdcsNpcController).Assembly.GetType("NPCSdcsCharacters.SdcsRollProbe", false);
                        probe = rp != null ? HarmonyLib.AccessTools.DeclaredMethod(rp, "ProbeForHeal", new[] { typeof(EntityAlive), typeof(EntityAlive), typeof(float).MakeByRefType(),
                                                                                                                   typeof(string).MakeByRefType(), typeof(string).MakeByRefType() }) : null;
                        start = HarmonyLib.AccessTools.DeclaredMethod(typeof(AvatarSdcsNpcController), "StartForcedRoll", new[] { typeof(float), typeof(bool) });
                        if (probe == null || !probe.IsStatic || probe.ReturnType != typeof(bool)) missing = "SdcsRollProbe.ProbeForHeal";
                        else if (start == null || start.IsStatic || start.ReturnType != typeof(bool)) missing = "AvatarSdcsNpcController.StartForcedRoll";
                        ok = missing == null;
                    }
                    catch (Exception e)
                    {
                        missing = e.GetType().Name;
                        ok = false;
                    }
                    if (!ok) ScLog.Warn("0-XNPCCore's roll (" + missing + ") not found - step 5 of the heal order at home (the roll) is off (0-XNPCCore update?)");
                }
                if (!ok) why = "0-XNPCCore's roll is not bound (" + missing + ")";
                return ok;
            }

            internal static bool Probe(EntityAlive self, EntityAlive t, out float yaw, out string heading, out string noClear)
            {
                yaw = -1f;
                heading = null;
                noClear = null;
                try
                {
                    object[] args = { self, t, 0f, null, null };
                    bool r = (bool)probe.Invoke(null, args);
                    yaw = (float)args[2];
                    heading = args[3] as string;
                    noClear = args[4] as string;
                    return r && heading != null;
                }
                catch (Exception e)
                {
                    ScLog.Once("heal-roll-probe", e);
                    noClear = "the probe threw";
                    return false;
                }
            }

            internal static bool Start(EntityAlive self, float headingOffsetDeg)
            {
                try
                {
                    AvatarSdcsNpcController c = self.emodel != null ? self.emodel.avatarController as AvatarSdcsNpcController : null;
                    if (c == null) return false;
                    object r = start.Invoke(c, new object[] { headingOffsetDeg, false });
                    return r is bool && (bool)r;
                }
                catch (Exception e)
                {
                    ScLog.Once("heal-roll-start", e);
                    return false;
                }
            }
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
            // round hr1: a room or flight walk shuts the doors she passes (ScDoors, as rt1's shelter walk), checks its path once the
            // navigator has one (it must reach the spot and pass no enemy), and heals in a safe room on the way (2.5 m from doors)
            if (st.Kind != WalkK1)
            {
                if (now >= st.NextDoors)
                {
                    st.NextDoors = now + 0.5f;
                    ScDoors.TrackPassed(self, ref st.DoorPath, ref st.DoorIndex, ref st.LastDoor, ref st.HasDoor);
                }
                if (!st.PathChecked && !CheckPath(self, st, now)) { st.Walking = false; st.FailedWalks++; return false; }   // round hr1 (re-check HR1R-n1): FailedWalks
                if (now >= st.NextSafe)
                {
                    st.NextSafe = now + 1f;
                    string room;
                    if (!ScShelter.NearDoor(self.world, World.worldToBlockPos(self.position)) && ScSafeRoom.SafeToHeal(self, out room)) { SafeArrival(self, st, room, now, false); return false; }
                }
            }
            float dist;
            EntityAlive t = Pursuer(self, out dist);
            if (t == null) { Clear(self, st, now); return false; }
            if (st.Kind == WalkK1 && now >= st.GainAt && st.GainAt > 0f && !Gained(self, st, t, dist, now)) return false;   // round hr1: F4 judges k1's straight steps
            if (st.ArriveUntil > 0f || ScRunners.XZ(self.position, st.Point) <= ArriveXZ)
            {
                // round hr1: a room walk's spot - heal there if it is a safe room now, else it is set aside and the next step comes
                if (st.Kind == WalkRoom)
                {
                    // round hr1 (re-check HR1R-M1): she never stands waiting with the pursuer on her - it within ArriveLeaveDist, or the gap
                    // rule holding (an enemy within 2.5 m sees her), ends the wait at once (the fight, the next step or the stand takes over).
                    // Asked every tick, not only at the 0.5 s tests: while the door is open the room test sees no enemy at all
                    string fw;
                    bool leave = dist <= ArriveLeaveDist || FightsInstead(self, out fw);
                    if (st.ArriveUntil > 0f && leave)
                    {
                        st.ArriveUntil = 0f;
                        // round hr1 (final check HR1F-m1): one last fresh test before she leaves - the door may have just shut with the
                        // pursuer outside it (the test's own sight rules still refuse a pursuer that can see her)
                        string lastRoom, lastKey;
                        if (ScSafeRoom.SafeToHeal(self, true, out lastRoom, out lastKey)) { SafeArrival(self, st, lastRoom, now, true); return false; }
                        SetAside(st, now);
                        Patches_Xnpc.Count("heal-room-wait-ended");
                        ScLog.Out(ScMedic.BodyName(self) + " stops waiting in the room at " + ScRetreat.Cell(st.Point) + ": " + Who(t) + " is at " + dist.ToString("0.0", CultureInfo.InvariantCulture)
                                  + " m - the next step (round hr1 re-check)");
                        return false;
                    }
                    // round hr1 (review HR1-M2): waiting at the spot for the door behind her - a fresh test every ArriveTestSeconds
                    if (st.ArriveUntil > 0f && now < st.NextArriveTest) return true;
                    if (st.ArriveUntil <= 0f) ScDoors.TrackPassed(self, ref st.DoorPath, ref st.DoorIndex, ref st.LastDoor, ref st.HasDoor);
                    string room, key;
                    // round hr1 (review HR1-m6): a fresh test here - the 1 s cache may hold a verdict taken outside the room
                    if (ScSafeRoom.SafeToHeal(self, true, out room, out key)) { st.ArriveUntil = 0f; SafeArrival(self, st, room, now, true); return false; }
                    // round hr1 (re-check HR1R-m1): "leave now" is the distance test above, not the verdict's key - with an inner door the
                    // open-door fill is enclosed and a pursuer in the corridor reads "inside", though the door would shut it out. The wait
                    // has its own ArriveWaitSeconds (the walk's deadline is pushed to it), never past WalkCapSeconds after the first walk
                    float until = Mathf.Min(now + ArriveWaitSeconds, st.FirstWalkAt + WalkCapSeconds);
                    if (!leave && st.ArriveUntil <= 0f && st.HasDoor && until > now + ArriveTestSeconds)
                    {
                        // ScDoors shuts a door she passed 2 s after it was tracked and once her path no longer runs through it: she
                        // stands at the spot (her path dropped) and the room is tested again until then
                        st.ArriveUntil = until;
                        if (st.WalkDeadline < until) st.WalkDeadline = until;    // MedicMaxWait waits for it too (bounded by the cap above)
                        st.NextArriveTest = now + ArriveTestSeconds;
                        ScCommunity.DropPath(self);
                        Patches_Xnpc.Count("heal-room-wait-door");
                        ScLog.Verbose(ScMedic.BodyName(self) + " reached the room at " + ScRetreat.Cell(st.Point) + " - waits up to "
                                      + (st.ArriveUntil - now).ToString("0.#", CultureInfo.InvariantCulture) + " s for the door behind " + ScMedic.HerObj(self) + " to shut (" + room + "; round hr1 review)");
                        return true;
                    }
                    if (!leave && st.ArriveUntil > 0f && now < st.ArriveUntil) { st.NextArriveTest = now + ArriveTestSeconds; return true; }
                    st.ArriveUntil = 0f;
                    SetAside(st, now);
                    Patches_Xnpc.Count("heal-room-not-safe");
                    ScLog.Out(ScMedic.BodyName(self) + " reached the room at " + ScRetreat.Cell(st.Point) + " but it is no safe room now (" + room + ") - the next step (round hr1)");
                }
                return false;
            }
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
                if (st.Kind != WalkK1 && !st.PathChecked) st.FailedWalks++;          // round hr1 (re-check HR1R-n1)
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

        /// <summary>
        /// Round hr1: a room or flight walk brought her into a safe room (on arrival, or on the way 2.5 m from every door): the walk
        /// ends, the action rests and ScMedic's check runs at once - HeldByReach finds the safe room and uses her item.
        /// </summary>
        private static void SafeArrival(EntityAlive self, State st, string room, float now, bool arrived)
        {
            st.Walking = false;
            st.ClearUntil = now + ClearHoldSeconds;
            safeHeals++;
            Patches_Xnpc.Count("heal-room-safe");
            Note(st, "reached a safe room - healed there (hr1)", now);
            ScLog.Out(ScMedic.BodyName(self) + (arrived ? " reached " : " is in ") + "a safe room on " + ScMedic.Her(self) + (st.Kind == WalkRoom ? " room walk" : " flight walk")
                      + " (" + room + ") - " + ScMedic.She(self) + " heals there (round hr1)");
            ScMedic.CheckSoon(self.entityId);
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
            if (states.TryGetValue(self.entityId, out st))
            {
                st.Walking = false;
                st.StalePath = null;                                 // round hr1 (re-check HR1R-n4): no destructed path kept
                st.ArriveUntil = 0f;                                 // round hr1 (re-check): a stopped walk waits at no spot
            }
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
            {
                // round hr1: the walk's kind and its MedicMaxWait deadline (from the first walk start)
                string clock = st.FirstWalkAt >= 0f ? "; MedicMaxWait in " + Mathf.Max(0f, st.WalkDeadline - now).ToString("0.#", CultureInfo.InvariantCulture) + " s" : "";
                if (st.Kind == WalkRoom) return "RUNNING TO A ROOM TO HEAL: " + st.WalkNote + (st.PathChecked ? "" : " (path not checked yet)") + clock + " (round hr1)";
                if (st.Kind == WalkFlight) return "RUNNING TO A FLIGHT POINT TO HEAL: " + st.WalkNote + (st.PathChecked ? "" : " (path not checked yet)") + clock + " (round hr1)";
                return "BACKING OFF TO HEAL to " + ScRetreat.Cell(st.Point) + " (" + st.Step.ToString("0", CultureInfo.InvariantCulture) + " m, "
                       + st.Angle.ToString("0", CultureInfo.InvariantCulture) + " degrees, " + st.Bound + ")" + clock;
            }
            string nr;
            if (NoRoomNow(e, out nr) && now >= st.HealNowUntil) return "NO ROOM TO HEAL AT HOME: " + nr + " (round hr1)";
            if (now < st.HealNowUntil) return "HEALS NOW: " + st.HealNowWhy;
            float held;
            if (ScMedic.WaitingToHeal(e, out held) && FightsOn(e, st, now - held))
            {
                // round hr1 (re-check HR1R-n3): a timed-out wait is held by the gap rule alone, at any health
                if (Mathf.Abs(st.TimedOutWait - (now - held)) <= SameWait) return "TIMED OUT - fights on until no enemy within " + HealCutRange.ToString("0.#", CultureInfo.InvariantCulture) + " m sees " + ScMedic.HerObj(e) + " (round hr1)";
                return "CORNERED - fights on above " + Pct(ScConfig.CorneredHealBelow) + " (round nc1, D2)";
            }
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
                   + foughtOn + " cornered above " + Pct(ScConfig.CorneredHealBelow) + " (fought on: round nc1, D2) this session; at home (round hr1): "
                   + roomWalks + " room walk(s), " + flightWalks + " flight walk(s), " + rolls + " roll(s), " + safeHeals + " healed in a safe room they reached, "
                   + noRoom + " wait(s) with no room at all, " + gapHolds + " held by the gap rule (no heal with an enemy within " + HealCutRange.ToString("0.#", CultureInfo.InvariantCulture)
                   + " m that sees her); safe rooms: " + ScSafeRoom.Report();
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
