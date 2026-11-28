using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>Round k4: a member's one posture (ScPosture). None: no fight - 0-XNPCCore's own rules, the hand as it is.</summary>
    public enum Posture { None = 0, Melee = 1, Kite = 2, Hold = 3, Shelter = 4, Investigate = 5 }

    /// <summary>
    /// Round k4 (2026-11-15, research/combat/SC_VS_NPCCORE.md 4.1 and 4.2, the plan the user approved): ONE posture per member,
    /// computed once per X5 tick (Patches_Xnpc.Patch_NoSwapWhileUsing, on SdcsCloseRangeTick, 5 Hz) right after ScTeam.Recount
    /// and ScRetreat.Update, and only READ everywhere else - by the one hand rule (ScArms.Hand) and by the one SC consideration
    /// on each of 0-XNPCCore's moving actions (UAIConsiderationScPosture: BackupKeepDistance, BackupRanged, MeleeApproach). It
    /// replaces the stacked gates of rounds n07, tm1, tm2 and rt1 on those actions and the three owners of the hand
    /// (GunFirst, MeleeFirst / AdoptBout, tm2's reach-gun hold). 0-XNPCCore keeps every mechanic: the shot, the swing and its
    /// swing check, its back-off task (kept inside the base by ScBackOff), its close-range bout and its exits.
    ///
    ///   HOLD     on watch, or a Stay order. The gun stays up (a guard's 3 m swap is held); no back-off, no approach.
    ///   SHELTER  rt1's shelter (hurt with no usable gun, armed enemies, Cowardly). The hand as it is; no back-off, no approach.
    ///   MELEE    a matched fight with margin (ScTeam: F >= TeamMargin x T, health from TeamLowHealth, ...), and her target one she
    ///            can swing at (not melee-unsafe, armed, past her leash, above her, marked unreachable or refused by 0-XNPCCore's
    ///            swing check), and not overwhelmed (one attacker more in a matched fight). Her melee weapon; no back-off; the
    ///            approach. A gun holder stays KITE until her target is within PostureMeleeDist (4 m), then takes her melee
    ///            weapon once; so does a member whose best skill is Shooting (round fd2: she kept a loaded gun before), and she
    ///            also for a lone enemy the count calls outmatched. A player's melee order is MELEE (round nc1: 0-XNPCCore's
    ///            weapon toggle is such an order too, ScArms.OrderedMelee / OrderedRanged).
    ///   KITE     any other fight with a gun that has rounds: outmatched, overwhelmed, hurt, a target she cannot swing at, a gun
    ///            order, hurt with nothing to heal with (ScRetreat's Kite mode: then the WIDE band - SCResident's ScKiteWide backs
    ///            her off from what comes within StandOffMin; round k5, was rt1's stand-off). Her gun, 0-XNPCCore's 3 m swap held
    ///            (R2: reloading or no shot for 3 s with no room, melee until room); the back-offs (inside the base at home); no
    ///            approach with the gun (with the melee weapon still in hand - the
    ///            draw held, R2, a runner's bout - the approach, as the back-offs need a gun). With no gun that has rounds she
    ///            can only swing: MELEE.
    ///   INVESTIGATE  a resident sent to an intruder that is after nobody (ScInvestigate) while she has no other fight: the
    ///            defense walk takes her to it, and she fights it with the stance MELEE / KITE would give her (Kiting).
    ///   None     no fight (no live target, none in her counted area that she may engage), or asleep in bed.
    ///
    /// Hysteresis (the user: ten swaps in 2 min 45 s were the problem): a switch between the MELEE and KITE stances waits
    /// PostureDwellSeconds after the last switch AND TeamSwapHoldSeconds after the last change of her hand (anyone's) - except
    /// when her target needs the gun (a cop, an armed enemy, past her leash, out of reach - and KITE hurt: the switch and the
    /// draw at once), when the gun runs dry (at once) and for the gun holder's one swap at PostureMeleeDist. So the hand
    /// changes at most once per dwell, a target that needs the gun excepted.
    /// Runners on a run (KITE) keep 0-XNPCCore's 3 m swap and round n08's 5 m draw (NativeClose): their walk decides.
    /// Strangers, enclave people and raiders have no posture (the overwhelmed rule as before, ScArms.CloseRange).
    /// </summary>
    public static class ScPosture
    {
        /// <summary>A posture older than this reads as none (the X5 tick skips a medical use, 1.4-2.5 s; a dead hook must not keep one).</summary>
        private const float FreshSeconds = 3f;
        /// <summary>"Hand changes in the last 3 minutes" (the report: Gina's 10 swaps in 2:45).</summary>
        private const float RecentWindow = 180f;
        /// <summary>A refused swing's memory doubles per time for the same target, up to this.</summary>
        private const float RefusedMax = 180f;
        /// <summary>A refusal record is forgotten this long after its memory ran out (its doubling count with it).</summary>
        private const float RefusedForget = 600f;
        private const int OrderStay = 2;
        /// <summary>
        /// Round nc1 (the audit, item 11): ScTeam.Target answers null for a moment in a fight (SCore's 1 s attack-target lease, a
        /// sight ray that misses for a tick) - a fight posture is held this long on its last target while that target lives.
        /// </summary>
        private const float TargetLapseSeconds = 1f;
        /// <summary>Want's why for "no fight" (the only None the lapse hold may bridge).</summary>
        private const string NoFightWhy = "no fight";
        /// <summary>
        /// Round wp1 (decision 4): a fight posture whose target just died (or left the world) is held up to one team recount
        /// (TeamRecountSeconds) and this much more - the verdict's nearest enemy is the dead one until the next count, so every
        /// kill dropped her to "no fight" for up to a second and back (TargetLapseSeconds bridged only a target still alive).
        /// </summary>
        private const float DeadLapseSlack = 0.3f;
        /// <summary>Round wp1 (review WP1-n3): that hold never runs longer than this, whatever TeamRecountSeconds says.</summary>
        private const float DeadLapseMax = 2f;
        /// <summary>Round wp1 (decision 4): a fight posture back within this long of a "no fight" gap is the same fight - its stance and dwell carry over.</summary>
        private const float ReentrySeconds = 2f;
        /// <summary>Round wp1 (decision 2): this long after her verdict turned matched again, the switch to MELEE is urgent (no dwell, no hand clock).</summary>
        private const float MatchedAgainUrgentSeconds = 3f;

        private sealed class Refusal
        {
            public float Until, At;
            public int Fails;
            public string Why;
        }

        private sealed class State
        {
            public Posture P;
            /// <summary>INVESTIGATE's stance (true: the gun); for KITE true, MELEE false.</summary>
            public bool Kiting;
            /// <summary>KITE only for the gun holder's distance (PostureMeleeDist): her one swap to melee does not wait for the dwell.</summary>
            public bool Soft;
            /// <summary>A runner on a run: 0-XNPCCore's 3 m swap kept, the gun only for a target beyond 5 m.</summary>
            public bool NativeClose;
            /// <summary>Her target is out of her melee reach: the draw does not wait for the hand clock.</summary>
            public bool UrgentGun;
            public string Why = "";
            /// <summary>When the posture last changed; the last Update. StanceSince (the Fable review of k3+k4, F8): when the
            /// stance last switched (MELEE / KITE) or a fight posture was entered from a non-fight one - the dwell's clock, so a
            /// change that keeps the stance (KITE to INVESTIGATE with the gun, say) does not restart it.</summary>
            public float Since, At, LastTick, StanceSince;
            public int TargetId = -1;
            public float TargetDist = -1f;
            /// <summary>Round nc1 (item 11): when Want last had a target (the lapse hold's clock); LapseHeld: counted once per lapse.</summary>
            public float TargetSeenAt;
            public bool LapseHeld;
            /// <summary>A switch the dwell holds (Pending), why, and since when.</summary>
            public Posture Pending;
            public bool PendingKiting, Waiting;
            public string PendingWhy;
            public float PendingSince;
            /// <summary>The item in her hand at the last tick, and when it last changed (the hand clock).</summary>
            public string Hand;
            public bool HandSeen;
            public float HandAt = -1000f;
            public int HandChanges, Swaps, UrgentSwaps, Flips, Refusals;
            public readonly List<float> HandTimes = new List<float>();
            /// <summary>A swap the hand clock held was counted (once per hold).</summary>
            public bool HeldNoted;
            /// <summary>Last tick SC let 0-XNPCCore run her bout in MELEE (its refusal exits), against this target.</summary>
            public bool BoutRan;
            public int BoutTarget = -1;
            /// <summary>Last tick SC let 0-XNPCCore decide its 3 m swap for her gun in MELEE (its refusal text), against this target.</summary>
            public int SwapWatch = -1;
            public bool BoutHeldNoted;
            public readonly Dictionary<int, Refusal> Refused = new Dictionary<int, Refusal>();
            /// <summary>Seconds spent in each posture this session (sc xnpc).</summary>
            public readonly float[] Seconds = new float[6];
            /// <summary>
            /// Round hr1: the last stand - on since StandSince, its weapon class (ScArms.StandClose / StandSlow / StandNoGun) and why;
            /// since when its conditions have not held (StandOffSince, the leave's hysteresis); the enemies after her within
            /// StandRadius at the last tick (StandReach: one at least - the commit clock's "an enemy in reach"); its swaps and the
            /// stands this session.
            /// </summary>
            public bool Stand, StandReach;
            public float StandSince, StandOffSince;
            public int StandClass, StandSwaps, StandHeld, Stands;
            public string StandWhy = "", StandGun = "";
            /// <summary>
            /// Round cb1 (item 3): she holds her spot until HoldUntil (since HoldSince) after HoldWhy (a back-off, a heal, a reload);
            /// the reload cvar at the last tick; the start of her last medical use already seen; how many holds this session.
            /// </summary>
            public float HoldUntil, HoldSince, UseSeen = -1f;
            /// <summary>Round cb1 (review CB1-M1, part d): when she last had a line of fire to her target while holding.</summary>
            public float HoldSightAt;
            /// <summary>
            /// Round cb1 (re-check CB1R-m3): her last hold ran out (its time or the blind end) - a reload or a heal starts no new one
            /// until she backs off again or takes the melee stance (or the fight ends).
            /// </summary>
            public bool HoldSpent;
            /// <summary>Round cb1 (re-check CB1R-m4): no melee walk to this target until StepOffUntil (she stepped off a model by it).</summary>
            public int StepOffTarget = -1;
            public float StepOffUntil;
            public string HoldWhy = "";
            public bool WasReloading;
            public int Holds;
            /// <summary>Round cb1: where she stood still since when (a step off a model waits for StepStillSeconds).</summary>
            public Vector3 StillPos;
            public float StillAt;
            /// <summary>Round cb1 (point 1): the pair gun she has no rounds for in a fight (null: none or rounds); told once per episode.</summary>
            public string NoAmmoGun;
            public bool NoAmmoTold;
            public float NoAmmoToldAt = -1000f;
            /// <summary>Round wp1 (decision 4): the fight posture and stance before the last "no fight" gap and when it began (GapFrom None: no gap);
            /// (review WP1-n2) its soft, runner and urgent-gun flags too, restored with it.</summary>
            public Posture GapFrom;
            public bool GapKiting, GapSoft, GapNative, GapUrgentGun;
            public float GapAt;
            /// <summary>Round wp1 (review WP1-m2): Want's last answer was MELEE by the team rule itself (MeleeWhy null), and when - cb1's hold reads it.</summary>
            public bool MeleeByWhy;
            public float MeleeByWhyAt;
            /// <summary>Round wp1 (decision 2): her verdict just turned matched again - the switch to MELEE and its swap are prompt.</summary>
            public bool UrgentMelee;
        }

        /// <summary>One member's posture as the hand rule reads it.</summary>
        public struct View
        {
            public Posture P;
            public bool Kiting, NativeClose, UrgentGun;
            /// <summary>Round wp1 (decision 2): matched again moments ago - MELEE's swap does not wait for the hand clock.</summary>
            public bool UrgentMelee;
            /// <summary>A switch of stance waits for the dwell: the hand rule makes no swap meanwhile (it would only be undone).</summary>
            public bool Waiting;
            public int TargetId;
            /// <summary>Round hr1: the last stand is on (since StandSince; StandClass: ScArms' weapon class) - ScArms.StandHand owns her hand.</summary>
            public bool Stand;
            public float StandSince;
            public int StandClass;
        }

        /// <summary>Round hr1: the last stand needs this many enemies after her within StandRadius (or overwhelmed).</summary>
        private const int StandMin = 2;
        private const float StandRadius = 3f;
        /// <summary>Round hr1: a stand whose conditions have not held for this long ends (hysteresis); not at home or no fight ends it at once.</summary>
        private const float StandLeaveSeconds = 1.5f;
        /// <summary>Round hr1: in a stand both of 0-XNPCCore's back-offs are off member-wide this long at a time (one probe every 4 s).</summary>
        internal const float StandBackOffSeconds = 4f;
        /// <summary>Round hr1: in a stand no hand swap for this long while an enemy is in reach (the hand clock's own hold, longer).</summary>
        internal const float StandCommitSeconds = 6f;
        /// <summary>Round hr1 (review HR1-m2): a stand holds this long after the last "no room" verdict while she is still hurt (the item's 12 s cooldown + 3 s).</summary>
        private const float StandKeepSeconds = 15f;
        /// <summary>Round cb1 (item 3): the hold ends when an enemy after her comes this close (meters).</summary>
        private const float HoldBreakDist = 3f;
        /// <summary>
        /// Round cb1: a medical use that began more than this long ago starts no hold (the first tick of a body, a long gap). Round cb1
        /// (review CB1-m3): 12 s - md2's longest use (its 8 s deadline) plus the skipped X5 ticks, so a long use that did heal still holds.
        /// </summary>
        private const float HoldUseFresh = 12f;
        /// <summary>Round cb1 (review CB1-m1): renewals extend a hold to at most this share of BackOffHoldSeconds from its start (12 s at 8).</summary>
        private const float HoldCapShare = 1.5f;
        /// <summary>Round cb1 (review CB1-M1, part d): a hold with no line of fire to her target this long ends (the defense walk takes over).</summary>
        private const float HoldBlindSeconds = 2f;
        /// <summary>Round cb1 (re-check CB1R-m4): after a step off a model, no melee walk to the target by it for this long.</summary>
        private const float StepOffMarkSeconds = 6f;
        /// <summary>Round cb1 (point 4): on a model or in a wreck's hull this long without moving 1 m, in a fight at home: a step down.</summary>
        private const float StepStillSeconds = 1.5f;
        /// <summary>Round cb1 (point 1): the out-of-ammo tell comes again at most this often for one member.</summary>
        private const float NoAmmoTellSeconds = 120f;
        private static int stands, holds, noAmmoTells;
        private static readonly List<Entity> standNear = new List<Entity>();

        private static readonly Dictionary<int, State> states = new Dictionary<int, State>();
        private static readonly int[] entered = new int[6];
        private static int swaps, urgentSwaps, flips, held, refusals;
        /// <summary>Round nc1 (item 11): lapses of ScTeam.Target the posture was held through.</summary>
        private static int lapses;
        private static float nextPrune;

        public static void Reset()
        {
            states.Clear();
            Array.Clear(entered, 0, entered.Length);
            swaps = urgentSwaps = flips = held = refusals = lapses = 0;
            stands = 0;                                // round hr1
            holds = noAmmoTells = 0;                   // round cb1
            standNear.Clear();
            nextPrune = 0f;
        }

        /// <summary>A despawned body's posture (entity ids are recycled).</summary>
        public static void Forget(int entityId)
        {
            states.Remove(entityId);
            ScBackOff.ForgetStep(entityId);                                    // round cb1 (review CB1-m4)
        }

        // ------------------------------------------------------------------ the writer (X5 only) ----

        /// <summary>
        /// The X5 prefix's posture part (after ScTeam.Recount and ScRetreat.Update, before ScArms.Hand): the hand clock (any
        /// change of what she holds), what 0-XNPCCore's last tick said about her swing, then the posture with its dwell. The
        /// only writer of the postures (ScArms.Hand stamps its own swaps through NoteSwap / NoteBout). Never throws.
        /// </summary>
        public static void Update(AvatarSDCSController ctrl, EntityAlive e)
        {
            try
            {
                if (e == null || e.isEntityRemote || !ScRegistry.Loaded || !ScXnpc.IsBody(e) || ScXnpc.IsRaider(e)) return;
                Member m = ScTeam.MemberOf(e);
                if (m == null) return;
                float now = Time.time;
                if (now >= nextPrune) Prune(now);
                State st;
                if (!states.TryGetValue(e.entityId, out st)) states[e.entityId] = st = new State { Since = now, StanceSince = now, UseSeen = ScMedic.LastUseStart(e) };
                if (st.LastTick > 0f && now - st.LastTick < 1f) st.Seconds[(int)st.P] += now - st.LastTick;
                st.LastTick = now;
                // 1. the hand clock: a change of her hand since the last tick - 0-XNPCCore's own swaps included (SC's are
                // stamped by NoteSwap as they happen, so they are not counted twice)
                string hand = ScArms.HeldName(e);
                if (!st.HandSeen) { st.HandSeen = true; st.Hand = hand; }
                else if (!string.Equals(hand, st.Hand, StringComparison.OrdinalIgnoreCase)) { st.Hand = hand; HandChanged(st, now); }
                // 2. what 0-XNPCCore's last tick said about her swing - a refusal turns her KITE against that target. A bout SC let
                // run (its target alive within 5 m) ends only on a refusal (a wall, bars, unreachable), with the gun back - or, when
                // 0-XNPCCore holds the gun dry, with the melee weapon kept: a refusal all the same (rejoining would loop)
                if (st.BoutRan)
                {
                    st.BoutRan = false;
                    EntityAlive bt = Entity(e, st.BoutTarget);
                    if (bt != null && !ScArms.InBout(ctrl)) Refuse(e, st, bt, ScArms.BoutExitWhy(ctrl, e, bt), now, true);
                }
                if (st.SwapWatch >= 0)
                {
                    EntityAlive sw = Entity(e, st.SwapWatch);
                    st.SwapWatch = -1;
                    string r = sw != null && ScArms.HoldsRanged(e) && !ScArms.InBout(ctrl) ? ScArms.SwapRefusal(ctrl) : null;
                    if (r != null) Refuse(e, st, sw, "0-XNPCCore refused her swap to melee (" + r + ")", now, true);
                }
                // 2b. round cb1 (item 3): a reload or a heal in a fight just ended - she holds her spot (a back-off's own end is
                // ScBackOff's); then whatever ends a hold (an enemy within 3 m, a dry gun, its time, the stand, away from home)
                // round cb1 (review CB1-M1, part c): her bounded run at home that another UAI action cut short holds too when it gained
                if (ScBackOff.RunCutWithGain(e)) StartHold(e, st, "her back-off (cut short)", now, true);
                bool reloading = ScBackOff.Reloading(e);
                if (st.WasReloading && !reloading && IsFight(st.P)) StartHold(e, st, "her reload", now);
                st.WasReloading = reloading;
                float use = ScMedic.LastUseStart(e);
                if (use > st.UseSeen + 0.01f && !ScMedic.IsUsing(e.entityId))
                {
                    st.UseSeen = use;
                    if (now - use <= HoldUseFresh && IsFight(st.P)) StartHold(e, st, "her heal", now);
                }
                CheckHold(e, st, now);
                // 3. the posture
                string why;
                bool kiting, soft, native, urgent, urgentGun;
                EntityAlive t;
                Posture want = Want(e, m, st, out why, out kiting, out soft, out native, out urgent, out urgentGun, out t);
                // round hr1: the last stand (at home, two or more on her, no room to heal) - before the holds below, which may return
                UpdateStand(e, st, want, t, now);
                // round cb1: the out-of-ammo tell (point 1) and a step down off a model or out of a wreck's hull (point 4)
                TellNoAmmo(e, m, st, want, now);
                StepOffCheck(e, st, want, t, now);
                // round nc1 (the audit, item 11: the posture flickered to none and back in a fight): a fight posture whose target
                // vanished from ScTeam.Target for a moment is held TargetLapseSeconds on its last target while that one lives - the
                // posture, its stance and its target stay as they are (and fresh); only then does "no fight" take over
                if (t != null) { st.TargetSeenAt = now; st.LapseHeld = false; }
                else if (want == Posture.None && why == NoFightWhy && IsFight(st.P) && st.TargetSeenAt > 0f
                         && now - st.TargetSeenAt < Mathf.Min(DeadLapseMax, Mathf.Max(TargetLapseSeconds, ScConfig.TeamRecountSeconds + DeadLapseSlack)))
                {
                    EntityAlive last = Entity(e, st.TargetId);
                    // round wp1 (decision 4): a target that just died (or left the world) is bridged too, up to the next recount
                    bool gone = last == null || last.IsDead();
                    if (gone || now - st.TargetSeenAt < TargetLapseSeconds)
                    {
                        if (!st.LapseHeld) { st.LapseHeld = true; lapses++; Patches_Xnpc.Count("posture-lapse-held"); }
                        st.At = now;
                        return;
                    }
                }
                // round cb1 (re-check CB1R-m3, m4): the melee stance or the fight's end re-arms the hold; the fight's end drops the step-off mark.
                // Final check CB1F-m1: read after the target-lapse hold above, so a one-tick target flicker mid-fight (held there) clears neither
                if (want == Posture.None || (IsFight(want) && !kiting)) st.HoldSpent = false;
                if (want == Posture.None) st.StepOffUntil = 0f;
                // round wp1 (decision 4): back in a fight within ReentrySeconds of a "no fight" gap - the same fight. Another stance
                // waits for the dwell as it would have without the gap (the old posture back meanwhile); the same one keeps its
                // dwell clock (Change). Before, the gap let her stance flip with no dwell at all
                if (st.P == Posture.None && IsFight(want) && IsFight(st.GapFrom) && now - st.GapAt < ReentrySeconds
                    && GunStance(st.GapFrom, st.GapKiting) != GunStance(want, kiting) && !urgent && !DwellOver(st, now, st.Soft && !kiting))
                {
                    // round wp1 (review WP1-n2): its flags back too, and INVESTIGATE only while she still investigates (else its stance)
                    Posture back = st.GapFrom == Posture.Investigate && want != Posture.Investigate ? (st.GapKiting ? Posture.Kite : Posture.Melee) : st.GapFrom;
                    ScLog.Verbose(ScRetreat.Name(e) + ": posture " + Name(back, st.GapKiting) + " again after " + F1(now - st.GapAt) + " s with no target - "
                                  + Name(want, kiting) + " waits for the dwell (round wp1)");
                    st.P = back;
                    st.Kiting = st.GapKiting;
                    st.Soft = st.GapSoft;
                    st.NativeClose = st.GapNative;
                    st.UrgentGun = st.GapUrgentGun;
                    st.Since = now;
                    st.Why = "the same fight after " + F1(now - st.GapAt) + " s with no target (round wp1)";
                    st.GapFrom = Posture.None;
                    st.PendingSince = now;
                    st.Waiting = true;
                    st.Pending = want;
                    st.PendingKiting = kiting;
                    st.PendingWhy = why;
                    st.TargetId = t != null ? t.entityId : -1;
                    st.TargetDist = t != null ? (t.position - e.position).magnitude : -1f;
                    st.At = now;
                    Patches_Xnpc.Count("posture-gap-bridged");
                    return;
                }
                bool stanceFlip = IsFight(st.P) && IsFight(want) && GunStance(st.P, st.Kiting) != GunStance(want, kiting);
                if (want == st.P && !stanceFlip)
                {
                    st.Waiting = false;
                    st.PendingSince = 0f;
                }
                else if (stanceFlip && !urgent && !DwellOver(st, now, st.Soft && !kiting))
                {
                    // the dwell holds the switch: she stays as she is, and the console says what waits
                    if (!st.Waiting || st.Pending != want || st.PendingKiting != kiting) st.PendingSince = now;
                    st.Waiting = true;
                    st.Pending = want;
                    st.PendingKiting = kiting;
                    st.PendingWhy = why;
                    st.TargetId = t != null ? t.entityId : -1;
                    st.TargetDist = t != null ? (t.position - e.position).magnitude : -1f;
                    st.At = now;
                    return;
                }
                else Change(e, st, want, kiting, why, stanceFlip, urgent, now);
                st.Why = why;
                st.Kiting = kiting;
                st.Soft = soft;
                st.NativeClose = native;
                st.UrgentGun = urgentGun;
                st.TargetId = t != null ? t.entityId : -1;
                st.TargetDist = t != null ? (t.position - e.position).magnitude : -1f;
                st.At = now;
            }
            catch (Exception ex)
            {
                ScLog.Once("posture-update", ex);
            }
        }

        /// <summary>
        /// The posture this tick wants (see the class summary), with why, the stance (kiting: the gun), soft (KITE only for the
        /// gun holder's distance), native (a runner), urgent (no dwell: her target needs the gun, the gun is dry, KITE hurt),
        /// urgentGun (her target needs the gun - melee-unsafe, armed, past her leash, out of reach - or KITE hurt: the draw
        /// does not wait for the hand clock, since with her melee weapon in KITE she could not back off) and her target.
        /// </summary>
        private static Posture Want(EntityAlive e, Member m, State st, out string why, out bool kiting, out bool soft, out bool native, out bool urgent,
                                    out bool urgentGun, out EntityAlive t)
        {
            kiting = soft = native = urgent = urgentGun = false;
            t = null;
            st.UrgentMelee = false;                                                  // round wp1: set below while matched again
            st.MeleeByWhy = false;                                                   // round wp1 (review WP1-m2): set below with a target
            if (ScWatch.OnWatch(m)) { why = "on watch: " + ScRetreat.She(e) + " holds the post and keeps " + ScRetreat.Her(e) + " gun up"; return Posture.Hold; }
            if (e.Buffs != null && Mathf.RoundToInt(e.Buffs.GetCustomVar("CurrentOrder")) == OrderStay)
            {
                why = "a Stay order: supporting fire from where " + ScRetreat.She(e) + " stands";
                return Posture.Hold;
            }
#if SC_V34
            if (ScSleep.IsAsleep(e)) { why = "asleep in bed"; return Posture.None; }
#endif
            if (ScRetreat.Shelters(e)) { why = "sheltering (round rt1: hurt with no usable gun, armed enemies, or Cowardly)"; return Posture.Shelter; }
            // round cb1 (item 3): holding her spot after a back-off, a heal or a reload - no INVESTIGATE walk to an intruder
            bool holding = st.HoldSince > 0f && Time.time < st.HoldUntil;
            EntityAlive inv = holding ? null : ScInvestigate.IntruderOf(e);
            t = ScTeam.Target(e);
            bool investigating = false;
            if (inv != null && (t == null || t == inv)) { t = inv; investigating = true; }
            if (t == null) { why = NoFightWhy; return Posture.None; }
            ItemClass gun = ScAmmo.PairGun(e);
            bool gunRounds = gun != null && ScAmmo.Rounds(e, gun) > 0;
            if (gunRounds || gun == null) { st.NoAmmoGun = null; if (gunRounds) st.NoAmmoTold = false; }   // round cb1 (point 1)
            bool holdsGun = ScArms.HoldsRanged(e);
            int count = 0, need = 0;
            bool over = ScConfig.Overwhelmed && ScArms.IsOverwhelmed(e, out count, out need);   // X5 only: it recounts
            bool melee;
            // round wp1 (review WP1-m2): the team rule's own answer, read once for every branch - MELEE by MeleeWhy (a matched fight,
            // not swarmed, no target that needs the gun, not the gun holder's distance) is what "she prefers melee" means for cb1's
            // hold (PrefersMelee); the old proxy (melee-led and no clear shooter) missed swarmed and a target that needs the gun.
            // Round wp1 (decision m4): and her personal choice is melee (MeleeFirst: her own Fighting against her own Shooting) - a
            // member who shoots first keeps the hold to its own ends (an enemy within 3 m, its time), as before wp1
            ScTeam.GunNeed kind;
            bool mwSoft;
            // round wp1 (re-check WP1R-n5): a refusal (ReachWhy) is remembered only on the branches that use this answer - not under
            // a weapon order, KITE hurt or a hold, where MeleeWhy was never asked before wp1
            bool remember = !ScRetreat.Kites(e) && !ScArms.OrderedMelee(e) && !(holding && gunRounds) && !(ScArms.OrderedRanged(e) && gunRounds);
            string mw = MeleeWhy(e, m, st, t, holdsGun, gunRounds, over, count, need, remember, out kind, out mwSoft);
            st.MeleeByWhy = mw == null && MeleeFirst(m);
            st.MeleeByWhyAt = Time.time;
            if (ScRetreat.Kites(e)) { melee = false; urgent = urgentGun = true; why = "hurt with nothing to heal with, a gun with rounds: KITE's wide band (round k5, was rt1's stand-off)"; }
            else if (ScArms.OrderedMelee(e)) { melee = true; why = "you ordered " + ScRetreat.Her(e) + " melee weapon"; }
            // round cb1 (item 3): she holds her spot - KITE with the gun (no MELEE once "matched" again), at once
            else if (holding && gunRounds && !st.MeleeByWhy)                       // round wp1 (review WP1-m2, decision m4): not once she prefers melee
            {
                melee = false;
                urgent = urgentGun = true;
                why = "holds " + ScRetreat.Her(e) + " spot after " + st.HoldWhy + ": " + ScRetreat.She(e) + " shoots from here and lets them come (round cb1)";
            }
            else if (ScArms.OrderedRanged(e) && gunRounds) { melee = false; why = "you ordered " + ScRetreat.Her(e) + " gun"; }
            else
            {
                soft = mwSoft;
                if (mw == null)
                {
                    melee = true;
                    why = ScTeam.MatchedText(e) + ", " + Pct(Share(e)) + " health, " + Who(t) + " in " + ScRetreat.Her(e) + " reach";
                    // round wp1 (decision 2): her verdict turned matched again moments ago (after TeamLeaveRecounts recounts in a row) -
                    // the switch to melee is prompt: no dwell, and MELEE's swap does not wait for the hand clock
                    if (ScTeam.MatchedAgainWithin(e, MatchedAgainUrgentSeconds))
                    {
                        urgent = true;
                        st.UrgentMelee = true;
                        if (IsFight(st.P) && GunStance(st.P, st.Kiting)) why += " - matched again: melee at once (round wp1)";
                    }
                }
                else if (gunRounds)
                {
                    melee = false;
                    why = mw;
                    urgent = urgentGun = kind != ScTeam.GunNeed.None;
                    // F1: a target she cannot swing at (a refusal R2's melee ended with, too) or should not (melee-unsafe, armed)
                    // draws the gun at once even when the posture is KITE for another reason (outmatched, hurt): with the melee
                    // weapon in KITE she could neither back off nor land a swing
                    if (!urgentGun && (RefusedWhy(e, t) != null || ScTeam.MeleeUnsafe(t) || ScTeam.Armed(t))) urgentGun = true;
                }
                else
                {
                    melee = true;
                    soft = false;
                    urgent = true;
                    why = "nothing to shoot with (" + mw + ") - melee is all " + ScRetreat.She(e) + " has";
                    if (gun != null) st.NoAmmoGun = gun.GetItemName();               // round cb1 (point 1): her gun has no rounds left
                }
            }
            if (!melee && ScRunners.IsPhysical(m) && !m.Offline && !over)
            {
                native = true;
                why = "on a run (" + why + "): 0-XNPCCore's 3 m swap kept, the gun beyond 5 m - round r2's walk decides";
            }
            kiting = !melee;
            if (investigating)
            {
                why = "investigating " + Who(t) + " (inside the base, after nobody) - " + (melee ? "melee" : "the gun") + ": " + why;
                return Posture.Investigate;
            }
            return melee ? Posture.Melee : Posture.Kite;
        }

        /// <summary>
        /// Why she is not MELEE against t (null: MELEE). kind: the target needs the gun (ScTeam.NeedsGun: melee-unsafe, armed,
        /// past her leash, out of reach). soft: only the gun holder's distance (PostureMeleeDist; 1 m more while already MELEE).
        /// </summary>
        private static string MeleeWhy(EntityAlive e, Member m, State st, EntityAlive t, bool holdsGun, bool gunRounds, bool over, int count, int need,
                                       bool remember, out ScTeam.GunNeed kind, out bool soft)
        {
            kind = ScTeam.GunNeed.None;
            soft = false;
            if (!ScConfig.TeamMelee) return "team strength off (TeamMelee false: no MELEE posture)";
            // round cb1 (re-check CB1R-m4): she just stepped off a model by this target - not into melee with it for a few seconds
            if (t != null && t.entityId == st.StepOffTarget && Time.time < st.StepOffUntil)
                return "she just stepped off where " + Who(t) + " stands - no melee walk to it for a few seconds (round cb1 re-check)";
            string led = ScTeam.NotLedWhy(e);
            // round fd2 (the user: "a lone enemy, or a fight she isn't outmatched in"): a marksman takes her melee weapon for
            // a lone enemy in reach even when the count calls one walker too many for her (Fighting 1 is parity at full
            // health); hurt, armed enemies, orders and the rest still keep her gun
            bool loneMarksman = led != null && gunRounds && Marksman(m) && ScTeam.OutmatchedByLoneEnemy(e);
            if (led != null && !loneMarksman) return led;
            string gunNeed = ScTeam.NeedsGun(e, t, !holdsGun, remember, out kind);
            if (gunNeed != null) return gunNeed;
            if (over) return "overwhelmed (" + count + " after " + ScRetreat.HerObj(e) + ", " + need + " in a matched fight)";
            // the Fable review of k3+k4 (F6): from her pair's gun, not her hand - the same verdict with either weapon in it.
            // Round fd2 (the user: "never uses a melee weapon against even a single zombie nearby"): only at range - a target
            // within PostureMeleeDist (1 m more once she is on her melee weapon) gets everyone's one swap to melee; the fights
            // the checks above give the gun (hurt, outmatched, swarmed, a target that needs it) keep it
            // round wp1 (decision 2; the user: "Mia's fighting skill is 4, she still shoots 1 lone zombie inside the base") and
            // round wp1 (decision m4, the user: her own skill decides): in a matched fight a member whose own Fighting stars are at
            // least her own Shooting stars (MeleeFirst, TeamShooterMargin 0) takes her melee weapon at any distance and walks to it;
            // everyone else keeps her gun until her target is within PostureMeleeDist, then one swap - as before wp1. Her own stars
            // only: a teammate's Fighting bonus can make the fight matched (ScTeam's F), never send a weaker fighter in
            if (gunRounds && !MeleeFirst(m))
            {
                float mlim = ScConfig.PostureMeleeDist + (GunStance(st.P, st.Kiting) ? 0f : 1f);
                float md = (t.position - e.position).magnitude;
                if (md > mlim)
                {
                    soft = true;
                    return "Shooting is " + ScRetreat.Her(e) + " better skill (Shooting " + m.Stars(Skill.Shooting) + "*, Fighting " + m.Stars(Skill.Fighting) + "*) - "
                           + ScRetreat.Her(e) + " gun until " + Who(t) + " is within " + F1(mlim) + " m (" + F1(md) + " m now), then one swap";
                }
            }
            return null;
        }

        /// <summary>
        /// Round hr1 (research/combat/HR1_DESIGN.md, "Last stand"; Nora died at home with a crossbow and a sledge, seven zombies on
        /// her, without one swing): the last stand's state, from the X5 tick (in her community's scope). On: at home (round hl1's
        /// AtHome), in a fight posture or sheltering, StandMin or more enemies after her within StandRadius (or overwhelmed), and
        /// no room - steps 2-5 of the heal order found nothing for her current wait (ScHealBackOff.NoRoomNow), or she has no heal
        /// item and the shelter walk found nothing (holds where she stands). Off once that has not held for StandLeaveSeconds, at
        /// once away from home or out of the fight. Its start puts both of 0-XNPCCore's back-offs off member-wide for
        /// StandBackOffSeconds (ScBackOff keeps it so: one probe every 4 s); the hand is ScArms.StandHand's. Logged at its start
        /// (with the weapon class: a modded gun in the wrong class shows) and its end.
        /// </summary>
        private static void UpdateStand(EntityAlive e, State st, Posture want, EntityAlive t, float now)
        {
            // a stand rides through a moment of "no fight" (ScTeam.Target's lease lapsing, as the lapse hold below) on its hysteresis
            bool fight = IsFight(want) || want == Posture.Shelter || (want == Posture.None && st.Stand);
            bool atHome = UAI.UAIConsiderationScNotHomeMember.AtHome(e.position);
            bool home = fight && atHome;
            // the cheap reads first: no room (steps 2-5) or the shelter's "nothing" - only then are the enemies on her counted
            string room = null, noRoom = null;
            if (home)
            {
                if (ScHealBackOff.NoRoomNow(e, out room)) noRoom = "no room to heal: " + room;
                else if (want == Posture.Shelter && ScRetreat.HoldsWithNoSpot(e) && !ScMedic.CanTreatSelf(e))
                    noRoom = "nothing to heal with and the shelter walk found no spot (holds where " + ScRetreat.She(e) + " stands)";
                // round hr1 (review HR1-m2): a stand holds through the item's cooldown after a heal in a gap (no wait then) while she is
                // still at or under the heal line and the last "no room" verdict is recent
                else if (st.Stand && e.GetMaxHealth() > 0 && e.Health <= e.GetMaxHealth() * ScMedic.HealBelow)
                {
                    // round hr1 (re-check HR1R-m3): counted from the later of the last verdict and her last use's start, and held while an
                    // item still heals her (the next wait starts only after the item's cooldown)
                    if (ScHealBackOff.NoRoomRecently(e, StandKeepSeconds, out room))
                        noRoom = "still hurt, no room to heal within the last " + StandKeepSeconds.ToString("0", CultureInfo.InvariantCulture) + " s (" + room + ")";
                    else if (ScMedic.UsedOrWorking(e, StandKeepSeconds))
                        noRoom = "still hurt after a heal in a gap (its item's cooldown or effect still running)";
                }
            }
            int close = home && (noRoom != null || st.Stand) ? AfterHerWithin(e, StandRadius) : 0;
            st.StandReach = close > 0;
            string why = null;
            bool wanted = false;
            if (noRoom != null)
            {
                int count = 0, need;
                float ago;
                bool over = ScConfig.Overwhelmed && ScArms.PeekOverwhelmed(e, out count, out need, out ago);   // the recount Want just made
                if (close >= StandMin || over)
                {
                    wanted = true;
                    why = (close >= StandMin ? close + " after " + ScRetreat.HerObj(e) + " within " + F1(StandRadius) + " m" : "overwhelmed (" + count + " after " + ScRetreat.HerObj(e) + ")")
                          + ", " + noRoom;
                }
            }
            if (wanted)
            {
                st.StandOffSince = 0f;
                st.StandWhy = why;
                if (!st.Stand) StartStand(e, st, t, now);
                return;
            }
            if (!st.Stand) return;
            if (!home) { EndStand(e, st, !atHome ? "away from home" : want == Posture.Hold ? "on watch or told to stay" : "no fight", now); return; }
            if (st.StandOffSince <= 0f) { st.StandOffSince = now; return; }
            if (now - st.StandOffSince >= StandLeaveSeconds)
                EndStand(e, st, close < StandMin ? "fewer than " + StandMin + " after " + ScRetreat.HerObj(e) + " within " + F1(StandRadius) + " m" : "room to heal again (or healed)", now);
        }

        /// <summary>Round hr1: the stand starts - its weapon class and window (logged once), the back-offs off member-wide.</summary>
        private static void StartStand(EntityAlive e, State st, EntityAlive t, float now)
        {
            st.Stand = true;
            st.StandSince = now;
            st.StandOffSince = 0f;
            st.StandSwaps = 0;
            st.StandHeld = 0;
            st.Stands++;
            stands++;
            Patches_Xnpc.Count("stand-start");
            Loadout l = ScLoadout.Current(e);
            ItemClass gun = l != null && l.HasRanged ? ItemClass.GetItemClass(l.Ranged, true) : null;
            int mag;
            float reload;
            st.StandClass = ScArms.GunClass(e, gun, out mag, out reload);
            st.StandGun = gun != null ? gun.GetItemName() : "";
            string cls = st.StandClass == ScArms.StandClose
                ? "a CLOSE gun (magazine " + mag + (reload > 0f ? ", reload " + F1(reload) + " s" : "") + "): it keeps shooting and reloads only when empty with no enemy within 2 m (or no melee weapon)"
                : st.StandClass == ScArms.StandSlow
                ? "a SLOW gun (magazine " + mag + (reload > 0f ? ", reload " + F1(reload) + " s" : "") + "): its loaded round, then melee for the rest of the stand, no reload with an enemy within 3 m"
                : "no gun with rounds";
            string melee = l != null && !string.IsNullOrEmpty(l.Melee) && ItemClass.GetItemClass(l.Melee, true) != null ? l.Melee : null;
            float apm;
            string mw = melee != null ? "the " + melee + " (no-swing window " + F1(ScArms.NoSwingWindow(e, melee, out apm)) + " s from " + apm.ToString("0", CultureInfo.InvariantCulture) + " attacks/min)"
                                      : "no melee weapon (the gun anyway)";
            if (t != null) ScBackOff.HandToShot(e, t, StandBackOffSeconds);
            ScLog.Out(ScRetreat.Name(e) + " makes a LAST STAND at home: " + st.StandWhy + ". " + (gun != null ? "The " + st.StandGun + " is " + cls : "No gun") + "; " + mw
                      + "; " + F1(StandCommitSeconds) + " s on a weapon while an enemy is in reach; back-offs off for " + F1(StandBackOffSeconds) + " s at a time; a heal only in a gap (round hr1)");
        }

        /// <summary>Round hr1: the stand ends (logged with its length and swaps).</summary>
        private static void EndStand(EntityAlive e, State st, string why, float now)
        {
            st.Stand = false;
            st.StandOffSince = 0f;
            Patches_Xnpc.Count("stand-end");
            ScLog.Out(ScRetreat.Name(e) + "'s last stand ends after " + (now - st.StandSince).ToString("0.0", CultureInfo.InvariantCulture) + " s: " + why + " (" + st.StandSwaps
                      + " weapon change(s), " + st.StandHeld + " held by the " + F1(StandCommitSeconds) + " s commitment; round hr1)");
        }

        // ------------------------------------------------------------------ round cb1: the hold, the tell, the step off ----

        /// <summary>
        /// Round cb1 (item 3, the user: "they shouldn't run right back to the zombies"): ScBackOff's back-off at home ended on its own
        /// with a gain - she holds that spot (StartHold). Main thread.
        /// </summary>
        internal static void StartHold(EntityAlive e, string why)
        {
            State st;
            if (e == null || !states.TryGetValue(e.entityId, out st)) return;
            StartHold(e, st, why, Time.time, true);
        }

        /// <summary>
        /// Round cb1: the hold starts (or, already on, runs BackOffHoldSeconds from now) - at home, her pair's gun with rounds, not in a
        /// last stand (its own rules), BackOffHoldSeconds above 0. Logged once per hold, at its start.
        /// </summary>
        private static void StartHold(EntityAlive e, State st, string why, float now, bool backOff = false)
        {
            if (ScConfig.BackOffHoldSeconds <= 0f || st.Stand) return;
            // round cb1 (re-check CB1R-m3): the cap is per back-off - a reload or a heal after a hold that ran out starts none (a
            // back-off re-arms it, so does the melee stance)
            if (backOff) st.HoldSpent = false;
            else if (st.HoldSpent) return;
            if (!UAI.UAIConsiderationScNotHomeMember.AtHome(e.position) || !ScRetreat.GunReady(e)) return;
            if (PrefersMelee(st, now)) return;                                   // round wp1 (decision 2): no hold in a matched fight she melees
            bool fresh = !(st.HoldSince > 0f && now < st.HoldUntil);
            // round cb1 (review CB1-m1): a renewal (each reload of a bow) extends the hold, but never past HoldCapShare x its time
            // from its start - "8 s" stays a limit; (review CB1-n3) a new hold with an enemy already within HoldBreakDist never starts
            if (!fresh)
            {
                st.HoldUntil = Mathf.Max(st.HoldUntil, Mathf.Min(now + ScConfig.BackOffHoldSeconds, st.HoldSince + HoldCapShare * ScConfig.BackOffHoldSeconds));
                return;
            }
            if (AfterHerWithin(e, HoldBreakDist) > 0) return;
            st.HoldUntil = now + ScConfig.BackOffHoldSeconds;
            st.HoldWhy = why;
            st.HoldSightAt = now;
            st.HoldSince = now;
            st.Holds++;
            holds++;
            Patches_Xnpc.Count("posture-hold");
            ScLog.Verbose(ScRetreat.Name(e) + " holds " + ScRetreat.Her(e) + " spot after " + why + ": " + ScRetreat.She(e) + " shoots from here and lets them come - no melee, no walk to"
                          + " the fight until one comes within " + F1(HoldBreakDist) + " m, " + ScRetreat.Her(e) + " gun runs dry, " + ScRetreat.She(e) + " has no line of fire for "
                          + F1(HoldBlindSeconds) + " s or " + F1(ScConfig.BackOffHoldSeconds) + " s pass (at most " + F1(HoldCapShare * ScConfig.BackOffHoldSeconds) + " s; round cb1)");
        }

        /// <summary>
        /// Round cb1: the hold's ends - its time, away from home, a last stand, no gun with rounds, an enemy after her within
        /// HoldBreakDist, and (review) no line of fire to her target for HoldBlindSeconds. X5 only.
        /// </summary>
        private static void CheckHold(EntityAlive e, State st, float now)
        {
            if (st.HoldSince <= 0f) return;
            string end = null;
            bool spent = false;                                                // round cb1 (re-check CB1R-m3): it ran out
            // round cb1 (re-check CB1R-n2): the hold's real length (a renewed one runs up to 1.5 x BackOffHoldSeconds)
            if (now >= st.HoldUntil) { end = (now - st.HoldSince).ToString("0.0", CultureInfo.InvariantCulture) + " s without anything reaching " + ScRetreat.HerObj(e); spent = true; }
            else if (!UAI.UAIConsiderationScNotHomeMember.AtHome(e.position)) end = "away from home";
            else if (st.Stand) end = "her last stand";
            else if (!ScRetreat.GunReady(e)) end = ScRetreat.Her(e) + " gun has no rounds";
            else if (PrefersMelee(st, now)) end = "the fight is matched and " + ScRetreat.She(e) + " prefers melee (round wp1)";
            else if (AfterHerWithin(e, HoldBreakDist) > 0) end = "an enemy reached " + ScRetreat.HerObj(e) + " (within " + F1(HoldBreakDist) + " m)";
            else
            {
                // round cb1 (review CB1-M1, part d): she holds to shoot - no line of fire to her target (SCore's own sight test) for
                // HoldBlindSeconds ends it, so the defense walk can take her to what she cannot hit
                EntityAlive tgt = ScTeam.Target(e);
                if (tgt == null) tgt = e.GetAttackTarget();
                // round cb1 (re-check CB1R-m2): geometry, as the far points - terrain and solid blocks (glass and bars too) from her eye
                // to its chest; not SCore's CanSee, whose view cone (past 10 m) and "a body in the way" ended holds she could shoot from
                if (tgt != null && !tgt.IsDead() && ScShelter.ClearLine(e.world, e.getHeadPosition(), tgt.getChestPosition(), true)) st.HoldSightAt = now;
                else if (now - st.HoldSightAt >= HoldBlindSeconds) { end = "no line of fire to an enemy for " + F1(HoldBlindSeconds) + " s"; spent = true; }
            }
            if (end == null) return;
            ScLog.Verbose(ScRetreat.Name(e) + "'s hold ends after " + (now - st.HoldSince).ToString("0.0", CultureInfo.InvariantCulture) + " s: " + end + " - the normal rules again (round cb1)");
            // round cb1 (re-check CB1R-m3): a hold that ran out (its time, or blind) is spent until her next back-off or melee
            if (spent) st.HoldSpent = true;
            st.HoldSince = 0f;
            st.HoldUntil = 0f;
        }

        /// <summary>
        /// Round cb1: she holds her spot (a fresh posture). UAIConsiderationScNotRetreating (her defense walk, the investigate walk,
        /// the leash and Guard), MeleeApproach (ScPosture action="approach") and ScInvestigate's pick read it. A pure read.
        /// </summary>
        internal static bool Holds(EntityAlive e)
        {
            State st;
            if (e == null || !states.TryGetValue(e.entityId, out st)) return false;
            float now = Time.time;
            return st.HoldSince > 0f && now < st.HoldUntil && st.At > 0f && now - st.At <= FreshSeconds;
        }

        /// <summary>
        /// Round cb1 (point 1): a member at home fighting with melee only because her pair's gun has no rounds left - one tell to the
        /// community per episode (until she has rounds again), at most once per NoAmmoTellSeconds, logged.
        /// </summary>
        private static void TellNoAmmo(EntityAlive e, Member m, State st, Posture want, float now)
        {
            if (st.NoAmmoGun == null || st.NoAmmoTold || !IsFight(want)) return;
            if (!UAI.UAIConsiderationScNotHomeMember.AtHome(e.position)) return;
            st.NoAmmoTold = true;
            if (now - st.NoAmmoToldAt < NoAmmoTellSeconds) return;
            st.NoAmmoToldAt = now;
            noAmmoTells++;
            Patches_Xnpc.Count("posture-no-ammo-tell");
            ItemClass ic = ItemClass.GetItemClass(st.NoAmmoGun, true);
            string label = ic != null ? ic.GetLocalizedItemName() : st.NoAmmoGun;
            ScPlayers.TellCommunityKey("scCb1NoAmmoFight", "ui_denied", m.Name, label);
            ScLog.Out(ScRetreat.Name(e) + " has no rounds left for " + ScRetreat.Her(e) + " " + st.NoAmmoGun + " in a fight at home - melee only; the community is told once (round cb1)");
        }

        /// <summary>
        /// Round cb1 (point 4): in a fight at home (or sheltering, or a last stand), standing on a model or inside a wreck's hull (a
        /// truck's bed) without moving 1 m for StepStillSeconds - a step down to open ground (ScBackOff.RequestStep; it keeps its own
        /// cooldown and logs). X5 only.
        /// </summary>
        private static void StepOffCheck(EntityAlive e, State st, Posture want, EntityAlive t, float now)
        {
            if (!(IsFight(want) || want == Posture.Shelter || st.Stand)) { st.StillAt = 0f; return; }
            if (st.StillAt <= 0f || (e.position - st.StillPos).sqrMagnitude > 1f) { st.StillPos = e.position; st.StillAt = now; return; }
            if (now - st.StillAt < StepStillSeconds) return;
            st.StillAt = now;                                                  // round cb1 (review CB1-n2): the next look in StepStillSeconds
            string what;
            if (!ScBackOff.OnObstacle(e.world, e.position, out what)) return;
            if (!ScBackOff.RequestStep(e, t, false, ScRetreat.She(e) + " stands " + what + " in a fight at home")) return;
            // round cb1 (review CB1-m5, re-check CB1R-m4): her target stands by what she stepped off - no melee walk to it (any weapon,
            // rounds or not: AllowsApproach) and, with a gun with rounds, KITE against it (MeleeWhy), for StepOffMarkSeconds, or until
            // the fight ends - not a 45-180 s refusal, which also did nothing for a member who walks in with her melee weapon
            if (t != null && !t.IsDead() && (t.position - e.position).sqrMagnitude <= 25f)
            {
                bool fresh = st.StepOffTarget != t.entityId || now >= st.StepOffUntil;
                st.StepOffTarget = t.entityId;
                st.StepOffUntil = now + StepOffMarkSeconds;
                Patches_Xnpc.Count("posture-step-off-mark");
                if (fresh)
                    ScLog.Verbose(ScRetreat.Name(e) + ": no melee walk to " + Who(t) + " for " + F1(StepOffMarkSeconds) + " s - " + ScRetreat.She(e) + " stood " + what
                                  + " by it and stepped off (round cb1 re-check)");
            }
        }

        /// <summary>Round hr1: awake, living enemies within r whose attack or revenge target she is (SCore's IsEnemy, not inert). X5 only (a shared list).</summary>
        private static int AfterHerWithin(EntityAlive e, float r)
        {
            World w = e.world;
            if (w == null) return 0;
            int n = 0;
            standNear.Clear();
            try
            {
                w.GetEntitiesInBounds(typeof(EntityAlive), new Bounds(e.position, new Vector3(2f * r, 2f * r, 2f * r)), standNear);
                for (int i = 0; i < standNear.Count; i++)
                {
                    EntityAlive a = standNear[i] as EntityAlive;
                    if (a == null || a == e || a.IsDead() || a.IsSleeping || (a.position - e.position).sqrMagnitude > r * r) continue;
                    if (a.GetAttackTarget() != e && a.GetRevengeTarget() != e) continue;
                    if (!EntityTargetingUtilities.IsEnemy(e, a) || global::Harmony.ZombieFeatures.InertEntity.IsInert(a)) continue;
                    n++;
                }
            }
            finally { standNear.Clear(); }
            return n;
        }

        /// <summary>Round hr1: she makes a last stand now (fresh). ScBackOff, ScMedic and ScArms read it. A pure read.</summary>
        internal static bool InStand(EntityAlive e)
        {
            State st;
            return e != null && states.TryGetValue(e.entityId, out st) && st.Stand && st.At > 0f && Time.time - st.At <= FreshSeconds;
        }

        /// <summary>Round hr1: ScArms.StandHand changed her weapon (why) and commits to it - logged, counted.</summary>
        internal static void NoteStandSwap(EntityAlive e, string item, bool toGun, string why)
        {
            State st;
            if (e == null || !states.TryGetValue(e.entityId, out st)) return;
            float now = Time.time;
            st.Hand = item;
            st.HandSeen = true;
            HandChanged(st, now);
            st.Swaps++;
            st.StandSwaps++;
            swaps++;
            Patches_Xnpc.Count(toGun ? "stand-swap-to-gun" : "stand-swap-to-melee");
            ScLog.Out(ScRetreat.Name(e) + " commits to the " + item + " for " + F1(StandCommitSeconds) + " s in " + ScRetreat.Her(e) + " last stand: " + why + " (round hr1)");
        }

        /// <summary>Round hr1: the stand's commitment held a swap the weapon class wants (counted once per hold).</summary>
        internal static void NoteStandHeld(EntityAlive e)
        {
            State st;
            if (e == null || !states.TryGetValue(e.entityId, out st) || st.HeldNoted) return;
            st.HeldNoted = true;
            st.StandHeld++;
            held++;
            Patches_Xnpc.Count("stand-swap-held");
        }

        /// <summary>
        /// Round wp1 (decision m4, the user: her own skill decides; was decision 2's ClearShooter): her personal weapon choice is
        /// melee - her own Fighting stars at least her own Shooting stars minus TeamShooterMargin (0). Then a matched fight sends
        /// her in with her melee weapon at any distance and ends cb1's hold; otherwise she shoots until PostureMeleeDist. A
        /// member's own stars only - never the team's F, where a teammate's Fighting bonus counts. A read.
        /// </summary>
        internal static bool MeleeFirst(Member m)
        {
            return m != null && m.Stars(Skill.Fighting) >= m.Stars(Skill.Shooting) - ScConfig.TeamShooterMargin;
        }

        /// <summary>
        /// Round wp1 (decision 2): her preference is melee, so round cb1's hold neither starts nor goes on. Round wp1 (review
        /// WP1-m2): Want's last answer was MELEE by the team rule itself (MeleeWhy null: matched, not swarmed, no target that
        /// needs the gun), fresh - so a back-off while swarmed in a matched fight still holds; (decision m4) and her personal
        /// choice is melee (MeleeFirst). A read.
        /// </summary>
        private static bool PrefersMelee(State st, float now)
        {
            return st.MeleeByWhy && now - st.MeleeByWhyAt <= FreshSeconds;
        }

        /// <summary>Shooting is her best skill: more stars than in Fighting, and at least as many as in every other skill.</summary>
        private static bool Marksman(Member m)
        {
            if (m == null) return false;
            int s = m.Stars(Skill.Shooting);
            return s > m.Stars(Skill.Fighting) && s >= m.Stars(Skill.Cardio) && s >= m.Stars(Skill.Wits);
        }

        private static void Change(EntityAlive e, State st, Posture want, bool kiting, string why, bool stanceFlip, bool urgent, float now)
        {
            Posture was = st.P;
            bool wasKiting = st.Kiting;
            float had = now - st.Since;
            st.P = want;
            st.Since = now;
            // round wp1 (decision 4): a fight back within ReentrySeconds of a "no fight" gap, in the stance it had, keeps its dwell clock
            bool sameFight = was == Posture.None && IsFight(st.GapFrom) && now - st.GapAt < ReentrySeconds && GunStance(st.GapFrom, st.GapKiting) == GunStance(want, kiting);
            if (stanceFlip || (!IsFight(was) && IsFight(want) && !sameFight)) st.StanceSince = now;   // F8: only a new stance restarts the dwell
            if (IsFight(was) && want == Posture.None)
            {
                st.GapFrom = was;
                st.GapKiting = wasKiting;
                st.GapSoft = st.Soft;                  // round wp1 (review WP1-n2): still the fight's (Update sets the new ones after Change)
                st.GapNative = st.NativeClose;
                st.GapUrgentGun = st.UrgentGun;
                st.GapAt = now;
            }
            else if (IsFight(want)) st.GapFrom = Posture.None;
            st.Waiting = false;
            st.PendingSince = 0f;
            entered[(int)want]++;
            Patches_Xnpc.Count("posture-" + CounterName(want, kiting));
            if (stanceFlip)
            {
                st.Flips++;
                flips++;
                Patches_Xnpc.Count("posture-flip");
            }
            if (want == Posture.None) return;                                      // a fight that ends is not worth a line
            ScLog.Verbose(ScRetreat.Name(e) + ": posture " + Name(want, kiting) + " (was " + Name(was, wasKiting) + " for " + had.ToString("0.0", CultureInfo.InvariantCulture)
                          + " s" + (urgent && stanceFlip ? ", at once" : "") + ") - " + why);
        }

        private static void HandChanged(State st, float now)
        {
            st.HandAt = now;
            st.HandChanges++;
            st.HeldNoted = false;
            st.HandTimes.Add(now);
            while (st.HandTimes.Count > 0 && now - st.HandTimes[0] > RecentWindow) st.HandTimes.RemoveAt(0);
        }

        /// <summary>
        /// A switch of stance may happen: PostureDwellSeconds in the stance (StanceSince; not for the gun holder's one swap at
        /// PostureMeleeDist: soft) and, always, TeamSwapHoldSeconds since her hand last changed - so the posture never turns to a
        /// hand she may not take yet (the hand rule would leave her with the wrong weapon for the stance's gates).
        /// </summary>
        private static bool DwellOver(State st, float now, bool soft)
        {
            return (soft || now - st.StanceSince >= ScConfig.PostureDwellSeconds) && now - st.HandAt >= ScConfig.TeamSwapHoldSeconds;
        }

        private static void Refuse(EntityAlive e, State st, EntityAlive t, string why, float now, bool log)
        {
            Refusal r;
            if (!st.Refused.TryGetValue(t.entityId, out r)) st.Refused[t.entityId] = r = new Refusal();
            if (now < r.Until) { r.Why = why; return; }                            // still held: nothing new
            float span = Mathf.Min(ScConfig.TeamUnreachableSeconds * (1 << Math.Min(r.Fails, 8)), RefusedMax);
            r.Fails++;
            r.At = now;
            r.Until = now + span;
            r.Why = why;
            st.Refusals++;
            refusals++;
            Patches_Xnpc.Count("posture-refused");
            if (log)
                ScLog.Verbose(ScRetreat.Name(e) + " cannot swing at " + Who(t) + ": " + why + " - KITE against it for " + span.ToString("0", CultureInfo.InvariantCulture)
                              + " s (time " + r.Fails + " for it; round k4)");
        }

        private static void Prune(float now)
        {
            nextPrune = now + 30f;
            var gone = new List<int>();
            foreach (var kv in states)
            {
                if (now - kv.Value.At > 60f && now - kv.Value.LastTick > 60f) { gone.Add(kv.Key); continue; }
                var old = new List<int>();
                foreach (var r in kv.Value.Refused) if (now - r.Value.Until > RefusedForget) old.Add(r.Key);
                foreach (int id in old) kv.Value.Refused.Remove(id);
            }
            foreach (int id in gone) states.Remove(id);
        }

        // ------------------------------------------------------------------ the hand rule's side (X5, ScArms.Hand) ----

        /// <summary>SC's hand rule swapped her hand now (to the gun, or to the melee weapon); urgent: the clock was skipped.</summary>
        internal static void NoteSwap(EntityAlive e, string item, bool toGun, bool urgent)
        {
            State st;
            if (e == null || !states.TryGetValue(e.entityId, out st)) return;
            float now = Time.time;
            st.Hand = item;
            st.HandSeen = true;
            HandChanged(st, now);
            st.Swaps++;
            swaps++;
            if (urgent) { st.UrgentSwaps++; urgentSwaps++; }
            Patches_Xnpc.Count(toGun ? (urgent ? "posture-swap-to-gun-at-once" : "posture-swap-to-gun") : "posture-swap-to-melee");
            ScLog.Verbose(ScRetreat.Name(e) + " takes the " + item + " (posture " + Name(st.P, st.Kiting) + (urgent ? ", at once: " + ScRetreat.Her(e) + " target needs the gun" : "") + "; " + st.Why + ")");
        }

        /// <summary>The hand clock held a swap the posture wants (counted once per hold).</summary>
        internal static void NoteHeld(EntityAlive e)
        {
            State st;
            if (e == null || !states.TryGetValue(e.entityId, out st) || st.HeldNoted) return;
            st.HeldNoted = true;
            held++;
            Patches_Xnpc.Count("posture-swap-held");
        }

        /// <summary>SC let 0-XNPCCore run her bout in MELEE against this target: an exit with the gun back is its refusal (next Update).</summary>
        internal static void NoteBout(EntityAlive e, int targetId)
        {
            State st;
            if (e == null || !states.TryGetValue(e.entityId, out st)) return;
            st.BoutRan = true;
            st.BoutTarget = targetId;
            st.BoutHeldNoted = false;
        }

        /// <summary>SC held 0-XNPCCore's bout exit (MELEE keeps the melee weapon for a target gone or beyond 5 m) - counted once per bout.</summary>
        internal static void NoteBoutHeld(EntityAlive e)
        {
            State st;
            if (e == null || !states.TryGetValue(e.entityId, out st) || st.BoutHeldNoted) return;
            st.BoutHeldNoted = true;
            Patches_Xnpc.Count("posture-bout-exit-held");
        }

        /// <summary>SC let 0-XNPCCore decide its 3 m swap for her gun in MELEE: a refusal it records is read next Update.</summary>
        internal static void NoteSwapWatch(EntityAlive e, int targetId)
        {
            State st;
            if (e != null && states.TryGetValue(e.entityId, out st)) st.SwapWatch = targetId;
        }

        /// <summary>X5 only (ScArms.ReachWhy's path findings): this target is out of her melee reach - remembered as a refusal.</summary>
        internal static void NoteRefused(EntityAlive e, EntityAlive t, string why)
        {
            State st;
            if (e == null || t == null || !states.TryGetValue(e.entityId, out st)) return;
            Refuse(e, st, t, why, Time.time, false);
        }

        // ------------------------------------------------------------------ pure reads ----

        /// <summary>Her posture while it is fresh (else false: 0-XNPCCore's own rules and the live gates). A pure read.</summary>
        public static bool Of(EntityAlive e, out View v)
        {
            v = new View { P = Posture.None, TargetId = -1 };
            State st;
            if (e == null || !states.TryGetValue(e.entityId, out st) || st.At <= 0f || Time.time - st.At > FreshSeconds) return false;
            v.P = st.P;
            v.Kiting = st.Kiting;
            v.NativeClose = st.NativeClose;
            v.UrgentGun = st.UrgentGun;
            v.UrgentMelee = st.UrgentMelee;            // round wp1
            v.Waiting = st.Waiting;
            v.TargetId = st.TargetId;
            v.Stand = st.Stand;                        // round hr1: the last stand
            v.StandSince = st.StandSince;
            v.StandClass = st.StandClass;
            return true;
        }

        /// <summary>
        /// Round k5: KITE's wide band is hers - ScRetreat's Kite mode (hurt with nothing to heal with, a gun with rounds, no armed
        /// enemy), unless a fresh posture holds the melee stance (MELEE, INVESTIGATE with the melee weapon - not possible while
        /// she kites hurt, but read so) - KITE, INVESTIGATE with the gun, none (no counted target yet: a zombie that merely sees
        /// her) and no fresh posture all keep it. UAIConsiderationScPosture action="wide" on SCResident's ScKiteWide. A pure read.
        /// </summary>
        internal static bool KitesWide(EntityAlive e)
        {
            if (!ScRetreat.Kites(e)) return false;
            View v;
            return !Of(e, out v) || v.P == Posture.None || GunStance(v.P, v.Kiting);
        }

        /// <summary>F1: when her hand last changed (SC's swap or 0-XNPCCore's), -1000 with no posture. A pure read.</summary>
        internal static float HandChangedAt(EntityAlive e)
        {
            State st;
            return e != null && states.TryGetValue(e.entityId, out st) ? st.HandAt : -1000f;
        }

        /// <summary>The hand clock allows a swap: TeamSwapHoldSeconds since her hand last changed (true with no posture). A pure read.</summary>
        internal static bool HandClockAllows(EntityAlive e, float now)
        {
            State st;
            if (e == null || !states.TryGetValue(e.entityId, out st)) return true;
            // round hr1: in a last stand with an enemy in reach the one hand clock holds StandCommitSeconds (the commitment), so no
            // rule's swap can fight the stand's
            float hold = st.Stand && st.StandReach ? Mathf.Max(ScConfig.TeamSwapHoldSeconds, StandCommitSeconds) : ScConfig.TeamSwapHoldSeconds;
            return now - st.HandAt >= hold;
        }

        /// <summary>A swing 0-XNPCCore refused at this target (or a path finding) is still remembered: why, else null. A pure read.</summary>
        internal static string RefusedWhy(EntityAlive e, EntityAlive t)
        {
            State st;
            Refusal r;
            if (e == null || t == null || !states.TryGetValue(e.entityId, out st) || !st.Refused.TryGetValue(t.entityId, out r)) return null;
            float now = Time.time;
            return now < r.Until ? r.Why + " (kept " + (r.Until - now).ToString("0", CultureInfo.InvariantCulture) + " s more)" : null;
        }

        /// <summary>
        /// UAIConsiderationScPosture, BackupKeepDistance and BackupRanged: may she back off from t? KITE, INVESTIGATE with the
        /// gun and no posture (0-XNPCCore's own) yes; MELEE only from a melee-unsafe or armed enemy (tm1's per-target rule);
        /// HOLD and SHELTER no. A pure read.
        /// </summary>
        public static bool AllowsBackOff(EntityAlive self, EntityAlive t)
        {
            View v;
            if (!Of(self, out v)) return true;
            switch (v.P)
            {
                case Posture.Kite: return true;
                case Posture.Investigate: return v.Kiting;
                case Posture.Melee: return t != null && (ScTeam.MeleeUnsafe(t) || ScTeam.Armed(t));
                case Posture.Hold:
                case Posture.Shelter: return false;
                default: return true;
            }
        }

        /// <summary>
        /// UAIConsiderationScPosture, MeleeApproach: may she walk to t with her melee weapon? MELEE, INVESTIGATE with the melee
        /// stance and no posture yes; KITE (and INVESTIGATE with the gun) only while the melee weapon is still in her hand - the
        /// hand clock holding her draw, R2's cornered melee, or a runner's own bout - since 0-XNPCCore's back-offs need a gun in
        /// hand and she would otherwise stand still; never to a melee-unsafe or armed enemy while she has a gun with rounds
        /// (its draw is at once). HOLD and SHELTER no. A pure read.
        /// </summary>
        public static bool AllowsApproach(EntityAlive self, EntityAlive t)
        {
            // round cb1 (re-check CB1R-m4): no melee walk to the target she just stepped off a model by, whatever she holds
            State sm;
            if (t != null && self != null && states.TryGetValue(self.entityId, out sm) && sm.StepOffTarget == t.entityId && Time.time < sm.StepOffUntil) return false;
            View v;
            if (!Of(self, out v)) return true;
            bool unsafeTarget = t != null && (ScTeam.MeleeUnsafe(t) || ScTeam.Armed(t)) && ScArms.HasPairGun(self);
            switch (v.P)
            {
                case Posture.Melee:
                case Posture.Investigate:
                    if (v.P == Posture.Investigate && v.Kiting) return !ScArms.HoldsRanged(self) && !unsafeTarget;
                    return !unsafeTarget;
                case Posture.Kite: return !ScArms.HoldsRanged(self) && !unsafeTarget;
                case Posture.Hold:
                case Posture.Shelter: return false;
                default: return true;
            }
        }

        // ------------------------------------------------------------------ helpers ----

        private static bool IsFight(Posture p) { return p == Posture.Melee || p == Posture.Kite || p == Posture.Investigate; }

        /// <summary>The stance's hand: the gun (KITE, INVESTIGATE kiting) or the melee weapon.</summary>
        private static bool GunStance(Posture p, bool kiting) { return p == Posture.Kite || (p == Posture.Investigate && kiting); }

        public static string Name(Posture p, bool kiting)
        {
            switch (p)
            {
                case Posture.Melee: return "MELEE";
                case Posture.Kite: return "KITE";
                case Posture.Hold: return "HOLD";
                case Posture.Shelter: return "SHELTER";
                case Posture.Investigate: return kiting ? "INVESTIGATE (gun)" : "INVESTIGATE (melee)";
                default: return "none";
            }
        }

        private static string CounterName(Posture p, bool kiting)
        {
            switch (p)
            {
                case Posture.Melee: return "melee";
                case Posture.Kite: return "kite";
                case Posture.Hold: return "hold";
                case Posture.Shelter: return "shelter";
                case Posture.Investigate: return kiting ? "investigate-gun" : "investigate-melee";
                default: return "none";
            }
        }

        private static EntityAlive Entity(EntityAlive e, int id)
        {
            EntityAlive a = id >= 0 && e.world != null ? e.world.GetEntity(id) as EntityAlive : null;
            return a != null && !a.IsDead() ? a : null;
        }

        private static float Share(EntityAlive a)
        {
            int max = a.GetMaxHealth();
            return max > 0 ? Mathf.Clamp01((float)a.Health / max) : 1f;
        }

        private static string Who(EntityAlive t) { return t != null ? t.EntityName + " (" + t.entityId + ")" : "-"; }
        private static string F1(float f) { return f.ToString("0.#", CultureInfo.InvariantCulture); }
        private static string Pct(float f) { return (f * 100f).ToString("0", CultureInfo.InvariantCulture) + "%"; }

        private static int Recent(State st, float now)
        {
            int n = 0;
            foreach (float at in st.HandTimes) if (now - at <= RecentWindow) n++;
            return n;
        }

        // ------------------------------------------------------------------ console ----

        /// <summary>sc team &lt;name&gt; and sc xnpc &lt;id&gt;: her posture, why, what waits, her hand's counts and the refused targets.</summary>
        public static void DescribeLines(EntityAlive e, List<string> lines)
        {
            State st;
            if (e == null || !states.TryGetValue(e.entityId, out st) || st.At <= 0f)
            {
                lines.Add("[SC]   posture (round k4): none yet" + (ScTeam.MemberOf(e) == null ? " (not a member: strangers keep the overwhelmed rule)" : " (the first comes with her next close-range tick)"));
                return;
            }
            float now = Time.time;
            bool stale = now - st.At > FreshSeconds;
            lines.Add("[SC]   posture (round k4): " + Name(st.P, st.Kiting) + " for " + (now - st.Since).ToString("0.0", CultureInfo.InvariantCulture) + " s" + (stale ? " (STALE - reads as none)" : "")
                      + " - " + st.Why + (st.TargetId >= 0 ? "; target #" + st.TargetId + " at " + F1(st.TargetDist) + " m" : "")
                      + (st.NativeClose ? "; 0-XNPCCore's 3 m swap kept (a runner)" : "") + (st.UrgentGun ? "; the gun at once (" + ScRetreat.Her(e) + " target needs it)" : "")
                      + (KitesWide(e) ? "; the WIDE band: ScKiteWide backs " + ScRetreat.HerObj(e) + " off from what comes within " + F1(ScConfig.StandOffMin) + " m (round k5)" : ""));
            if (st.Stand)                                                   // round hr1
                lines.Add("[SC]   LAST STAND for " + (now - st.StandSince).ToString("0.0", CultureInfo.InvariantCulture) + " s - " + st.StandWhy + "; weapon class "
                          + (st.StandClass == ScArms.StandClose ? "CLOSE" : st.StandClass == ScArms.StandSlow ? "SLOW" : "no gun") + (st.StandGun.Length > 0 ? " (" + st.StandGun + ")" : "")
                          + "; " + st.StandSwaps + " weapon change(s), " + st.StandHeld + " held by the commitment (round hr1)");
            else if (st.Stands > 0) lines.Add("[SC]   last stands this session: " + st.Stands + " (round hr1)");
            if (st.HoldSince > 0f && now < st.HoldUntil)                    // round cb1
                lines.Add("[SC]   HOLDS " + ScRetreat.Her(e) + " spot after " + st.HoldWhy + " for " + (now - st.HoldSince).ToString("0.0", CultureInfo.InvariantCulture) + " s ("
                          + (st.HoldUntil - now).ToString("0.0", CultureInfo.InvariantCulture) + " s left; ends when one comes within " + F1(HoldBreakDist) + " m; round cb1)");
            else if (st.Holds > 0) lines.Add("[SC]   holds after a back-off, a heal or a reload this session: " + st.Holds + " (round cb1)");
            if (st.Waiting)
                lines.Add("[SC]   waits to switch to " + Name(st.Pending, st.PendingKiting) + " (" + st.PendingWhy + ") for " + (now - st.PendingSince).ToString("0.0", CultureInfo.InvariantCulture)
                          + " s: a switch needs " + F1(ScConfig.PostureDwellSeconds) + " s in the stance (" + F1(now - st.StanceSince) + " s so far) and "
                          + F1(ScConfig.TeamSwapHoldSeconds) + " s since her hand last changed (" + F1(Math.Max(0f, now - st.HandAt)) + " s)");
            lines.Add("[SC]   hand: " + ScArms.HandState(e) + "; hand changes " + st.HandChanges + " (" + Recent(st, now) + " in the last 3 min), SC's swaps " + st.Swaps
                      + " (" + st.UrgentSwaps + " at once), MELEE/KITE switches " + st.Flips + "; next swap "
                      + (now - st.HandAt >= ScConfig.TeamSwapHoldSeconds ? "allowed now" : "in " + (ScConfig.TeamSwapHoldSeconds - (now - st.HandAt)).ToString("0.0", CultureInfo.InvariantCulture) + " s"));
            foreach (var kv in st.Refused)
                if (now < kv.Value.Until)
                    lines.Add("[SC]   cannot swing at #" + kv.Key + ": " + kv.Value.Why + " - KITE against it " + (kv.Value.Until - now).ToString("0", CultureInfo.InvariantCulture) + " s more (time " + kv.Value.Fails + ")");
            EntityAlive inv = ScInvestigate.IntruderOf(e);
            if (inv != null) lines.Add("[SC]   responder: sent to " + Who(inv) + " at " + F1((inv.position - e.position).magnitude) + " m (an intruder after nobody; sc team lists them)");
        }

        /// <summary>One line for sc team's list and sc xnpc &lt;id&gt;.</summary>
        public static string Line(EntityAlive e)
        {
            State st;
            if (e == null || !states.TryGetValue(e.entityId, out st) || st.At <= 0f) return "posture (round k4): none yet";
            float now = Time.time;
            return "posture (round k4): " + Name(st.P, st.Kiting) + " " + (now - st.Since).ToString("0", CultureInfo.InvariantCulture) + " s" + (now - st.At > FreshSeconds ? " (STALE)" : "")
                   + (st.Stand ? " LAST STAND " + (now - st.StandSince).ToString("0", CultureInfo.InvariantCulture) + " s" : "")        // round hr1
                   + (st.Waiting ? " (waits for " + Name(st.Pending, st.PendingKiting) + ")" : "") + " - " + st.Why
                   + "; hand changes " + st.HandChanges + " (" + Recent(st, now) + " in 3 min), SC swaps " + st.Swaps + ", switches " + st.Flips;
        }

        /// <summary>sc xnpc: one line per loaded member body - her posture, the time in each, the hand counts.</summary>
        public static void ListLines(List<string> lines)
        {
            if (!ScRegistry.Loaded) return;
            float now = Time.time;
            foreach (Member m in ScRegistry.Members.Values)
            {
                EntityAlive e = ScCommunity.FindNpc(m.Id);
                State st;
                if (e == null || !states.TryGetValue(e.entityId, out st) || st.At <= 0f) continue;
                var sb = new StringBuilder("[SC]   ").Append(m.Name).Append(" (").Append(e.entityId).Append("): ").Append(Name(st.P, st.Kiting))
                    .Append(now - st.At > FreshSeconds ? " (stale)" : "").Append("; hand changes ").Append(st.HandChanges).Append(" (").Append(Recent(st, now))
                    .Append(" in the last 3 min), SC swaps ").Append(st.Swaps).Append(" (").Append(st.UrgentSwaps).Append(" at once), switches ").Append(st.Flips)
                    .Append(", refused swings ").Append(st.Refusals).Append("; time in MELEE / KITE / HOLD / SHELTER / INVESTIGATE: ");
                for (int i = 1; i < 6; i++) sb.Append(i > 1 ? " / " : "").Append(st.Seconds[i].ToString("0", CultureInfo.InvariantCulture));
                lines.Add(sb.Append(" s").ToString());
            }
        }

        /// <summary>The totals for sc xnpc and sc team.</summary>
        public static string Report()
        {
            float now = Time.time;
            var n = new int[6];
            foreach (var kv in states) if (kv.Value.At > 0f && now - kv.Value.At <= FreshSeconds) n[(int)kv.Value.P]++;
            return "now " + n[1] + " MELEE, " + n[2] + " KITE, " + n[3] + " HOLD, " + n[4] + " SHELTER, " + n[5] + " INVESTIGATE, " + n[0] + " none; this session "
                   + swaps + " swap(s) by SC (" + urgentSwaps + " at once: a target that needs the gun), " + held + " held by the hand clock, " + flips
                   + " MELEE/KITE switch(es), " + refusals + " refused swing(s) (0-XNPCCore's bout exits and swap refusals), " + lapses
                   + " target lapse(s) held (round nc1), " + stands + " last stand(s) (round hr1), " + holds + " hold(s) after a back-off, a heal or a reload and "
                   + noAmmoTells + " out-of-ammo tell(s) (round cb1) - counters posture-* and stand-* in the hook list";
        }
    }
}

namespace UAI
{
    using SurvivorCommunity;

    /// <summary>
    /// Round k4: the ONE SC consideration on each of 0-XNPCCore's moving actions (Config/utilityai.xml, each append behind its
    /// own xpath() guard) - action="keep" on BackupKeepDistance, "ranged" on BackupRanged, "approach" on MeleeApproach. A pure
    /// read of the member's posture (ScPosture.AllowsBackOff / AllowsApproach) and of the few live facts that must never wait for
    /// a tick: a guard on watch (was ScNotOnWatch), rt1's shelter on the back-offs and any retreat or "hurt with nothing to heal
    /// with" on the approach (were ScNotRetreating and ScNotHurtNoMeds), and on BackupKeepDistance k1's band
    /// (BackupKeepDistanceMax, 2.5 m, + 0.5 m for the back-off already running against that enemy: was ScBackupKeepBand). It
    /// replaces those and round tm1's ScNotTeamMelee. k1's ScBackOffRoom (no point / the shot's beat) stays beside it as a
    /// mechanical gate. 1 for everyone who is not one of the community's members on 0-XNPCCore's body (0-XNPCCore's rule), and
    /// for a member with no fresh posture (the live facts still apply). A throw is logged once and means 1.
    /// Round k5: action="wide" on SCResident's ScKiteWide (8.2, 0-XNPCCore's own back-off task): 1 against an enemy within
    /// StandOffMin (3D) while KITE's wide band is hers (ScPosture.KitesWide: hurt with nothing to heal with, a gun with
    /// rounds), up to ScRetreat.WideGap for the run already going against it; 0 for everyone else and in every other
    /// posture. It replaced rt1's stand-off (ScStandOffHurt and its own walk). A throw is logged once and means 0.
    /// </summary>
    public class UAIConsiderationScPosture : UAIConsiderationBase
    {
        private const int Approach = 0, Keep = 1, Ranged = 2, Wide = 3;
        private int what = Approach;

        public override void Init(System.Collections.Generic.Dictionary<string, string> parameters)
        {
            base.Init(parameters);
            string v;
            if (parameters == null || !parameters.TryGetValue("action", out v) || v == null) return;
            v = v.Trim().ToLowerInvariant();
            what = v == "keep" ? Keep : v == "ranged" ? Ranged : v == "wide" ? Wide : Approach;
        }

        public override float GetScore(Context _context, object target)
        {
            try
            {
                EntityAlive self = _context.Self;
                if (self == null || !ScXnpc.IsBody(self) || ScXnpc.IsRaider(self)) return what == Wide ? 0f : 1f;
                // the live facts are the community's members' own (each is false for anyone else); the posture is read for
                // whoever has one (a member, or a stand-in fighting as one); everyone else scores 1
                if (ScWatch.Guarding(self.entityId)) return 0f;                    // a guard holds her post (any posture, any tick)
                EntityAlive t = UAIUtils.ConvertToEntityAlive(target);
                if (what == Approach)
                {
                    if (ScRetreat.Owns(self) || ScRetreat.HurtNoMedsMember(self)) return 0f;   // rt1: no chase out of her room; decision 10
                    if (ScPosture.Holds(self)) return 0f;                                       // round cb1: she holds her spot
                    return ScPosture.AllowsApproach(self, t) ? 1f : 0f;
                }
                if (ScRetreat.Shelters(self)) return 0f;                            // rt1: sheltering (k1: only="shelter")
                // round wp1 (decision 1): an empty gun is no gun - no BackupRanged, BackupKeepDistance or ScKiteWide for a member
                // whose gun has no rounds in it or her stores (ScBackOff's start ends one that slips through); (review WP1-m5) only
                // with a melee weapon in her pair to fight with instead - with none, MELEE's AllowsBackOff still lets her back away
                // from a melee-unsafe or armed enemy only (re-check WP1R-m3)
                if (ScBackOff.IsMember(self) && ScArms.DryWithMelee(self)) return 0f;
                if (what == Wide)
                {
                    if (t == null) return 0f;
                    ActionData wd = _context.ActionData;
                    bool runningOnT = wd.Executing && wd.Action != null && wd.Action.Name == ScBackOff.WideBackOffAction && ReferenceEquals(wd.Target, target);
                    // round cb1: a run at home against t goes on to its end (its own end, the gain test, the path check or its deadline) -
                    // round cb1 (review CB1-M1): sight or not, a path round a wall or a truck no longer cuts it
                    if (runningOnT && ScBackOff.WideRunning(self, t)) return 1f;
                    bool band;
                    // round k5: KITE's wide band - within StandOffMin, kept up to WideGap for the run already going against t
                    if (ScPosture.KitesWide(self))
                    {
                        float wide = runningOnT ? ScRetreat.WideGap(self) : ScConfig.StandOffMin;
                        band = UAIUtils.DistanceSqr(self.position, t.position) <= wide * wide;
                    }
                    // round cb1 (item 2): the shooting back-off at home - from an enemy within BackupKeepDistanceMax, far, at a run
                    else band = ScBackOff.FarBand(self, t, runningOnT);
                    // round cb1 (review CB1-M1): SCore's CanSeeTarget, moved off ScKiteWide into here (the same test: not inert, and
                    // SCoreUtils.CanSee) - needed to start a run, not to keep the one above going
                    return band && !global::Harmony.ZombieFeatures.InertEntity.IsInert(t) && SCoreUtils.CanSee(self, t) ? 1f : 0f;
                }
                if (what == Keep && t != null && ScBackOff.IsMember(self))
                {
                    // k1's band: within BackupKeepDistanceMax (3D, as TargetDistance), 0.5 m more for the back-off already running on t
                    float max = ScConfig.BackupKeepDistanceMax;
                    ActionData ad = _context.ActionData;
                    if (ad.Executing && ad.Action != null && ad.Action.Name == "BackupKeepDistance" && ReferenceEquals(ad.Target, target)) max += ScBackOff.BandHysteresis;
                    if (UAIUtils.DistanceSqr(self.position, t.position) > max * max) return 0f;
                }
                return ScPosture.AllowsBackOff(self, t) ? 1f : 0f;
            }
            catch (System.Exception e)
            {
                ScLog.Once("uai-posture", e);
                return what == Wide ? 0f : 1f;
            }
        }
    }
}
