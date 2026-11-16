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
    ///            weapon once; a member whose best skill is Shooting keeps a loaded gun (KITE). A player's melee order is MELEE.
    ///   KITE     any other fight with a gun that has rounds: outmatched, overwhelmed, hurt, a target she cannot swing at, a gun
    ///            order, rt1's stand-off. Her gun, 0-XNPCCore's 3 m swap held (R2: reloading or no shot for 3 s with no room, melee until room);
    ///            the back-offs (inside the base at home); no approach with the gun (with the melee weapon still in hand - the
    ///            draw held, R2, a runner's bout - the approach, as the back-offs need a gun). With no gun that has rounds she
    ///            can only swing: MELEE.
    ///   INVESTIGATE  a resident sent to an intruder that is after nobody (ScInvestigate) while she has no other fight: the
    ///            defense walk takes her to it, and she fights it with the stance MELEE / KITE would give her (Kiting).
    ///   None     no fight (no live target, none in her counted area that she may engage), or asleep in bed.
    ///
    /// Hysteresis (the user: ten swaps in 2 min 45 s were the problem): a switch between the MELEE and KITE stances waits
    /// PostureDwellSeconds after the last switch AND TeamSwapHoldSeconds after the last change of her hand (anyone's) - except
    /// when her target needs the gun (a cop, an armed enemy, past her leash, out of reach - and rt1's stand-off: the switch and
    /// the draw at once), when the gun runs dry (at once) and for the gun holder's one swap at PostureMeleeDist. So the hand
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
        }

        /// <summary>One member's posture as the hand rule reads it.</summary>
        public struct View
        {
            public Posture P;
            public bool Kiting, NativeClose, UrgentGun;
            /// <summary>A switch of stance waits for the dwell: the hand rule makes no swap meanwhile (it would only be undone).</summary>
            public bool Waiting;
            public int TargetId;
        }

        private static readonly Dictionary<int, State> states = new Dictionary<int, State>();
        private static readonly int[] entered = new int[6];
        private static int swaps, urgentSwaps, flips, held, refusals;
        private static float nextPrune;

        public static void Reset()
        {
            states.Clear();
            Array.Clear(entered, 0, entered.Length);
            swaps = urgentSwaps = flips = held = refusals = 0;
            nextPrune = 0f;
        }

        /// <summary>A despawned body's posture (entity ids are recycled).</summary>
        public static void Forget(int entityId) { states.Remove(entityId); }

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
                if (!states.TryGetValue(e.entityId, out st)) states[e.entityId] = st = new State { Since = now, StanceSince = now };
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
                // 3. the posture
                string why;
                bool kiting, soft, native, urgent, urgentGun;
                EntityAlive t;
                Posture want = Want(e, m, st, out why, out kiting, out soft, out native, out urgent, out urgentGun, out t);
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
        /// gun holder's distance), native (a runner), urgent (no dwell: her target needs the gun, the gun is dry, a stand-off),
        /// urgentGun (her target needs the gun - melee-unsafe, armed, past her leash, out of reach - or rt1's stand-off: the draw
        /// does not wait for the hand clock, since with her melee weapon in KITE she could not back off) and her target.
        /// </summary>
        private static Posture Want(EntityAlive e, Member m, State st, out string why, out bool kiting, out bool soft, out bool native, out bool urgent,
                                    out bool urgentGun, out EntityAlive t)
        {
            kiting = soft = native = urgent = urgentGun = false;
            t = null;
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
            EntityAlive inv = ScInvestigate.IntruderOf(e);
            t = ScTeam.Target(e);
            bool investigating = false;
            if (inv != null && (t == null || t == inv)) { t = inv; investigating = true; }
            if (t == null) { why = "no fight"; return Posture.None; }
            ItemClass gun = ScAmmo.PairGun(e);
            bool gunRounds = gun != null && ScAmmo.Rounds(e, gun) > 0;
            bool holdsGun = ScArms.HoldsRanged(e);
            int count = 0, need = 0;
            bool over = ScConfig.Overwhelmed && ScArms.IsOverwhelmed(e, out count, out need);   // X5 only: it recounts
            bool melee;
            if (ScRetreat.StandsOff(e)) { melee = false; urgent = urgentGun = true; why = "standing off (round rt1: hurt, nothing to heal with, a gun with rounds)"; }
            else if (ScArms.OrderedMelee(e)) { melee = true; why = "you ordered " + ScRetreat.Her(e) + " melee weapon"; }
            else if (ScArms.OrderedRanged(e) && gunRounds) { melee = false; why = "you ordered " + ScRetreat.Her(e) + " gun"; }
            else
            {
                ScTeam.GunNeed kind;
                string mw = MeleeWhy(e, m, st, t, holdsGun, gunRounds, over, count, need, out kind, out soft);
                if (mw == null) { melee = true; why = ScTeam.MatchedText(e) + ", " + Pct(Share(e)) + " health, " + Who(t) + " in " + ScRetreat.Her(e) + " reach"; }
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
                                       out ScTeam.GunNeed kind, out bool soft)
        {
            kind = ScTeam.GunNeed.None;
            soft = false;
            if (!ScConfig.TeamMelee) return "team strength off (TeamMelee false: no MELEE posture)";
            string led = ScTeam.NotLedWhy(e);
            if (led != null) return led;
            string gunNeed = ScTeam.NeedsGun(e, t, !holdsGun, true, out kind);
            if (gunNeed != null) return gunNeed;
            if (over) return "overwhelmed (" + count + " after " + ScRetreat.HerObj(e) + ", " + need + " in a matched fight)";
            // the Fable review of k3+k4 (F6): from her pair's gun, not her hand - the same verdict with either weapon in it
            if (gunRounds && Marksman(m)) return "a marksman: Shooting is " + ScRetreat.Her(e) + " best skill and " + ScRetreat.Her(e) + " gun has rounds";
            if (holdsGun)
            {
                float lim = ScConfig.PostureMeleeDist + (GunStance(st.P, st.Kiting) ? 0f : 1f);
                float d = (t.position - e.position).magnitude;
                if (d > lim)
                {
                    soft = true;
                    return "a matched fight, but " + ScRetreat.She(e) + " keeps " + ScRetreat.Her(e) + " gun until " + Who(t) + " is within "
                           + F1(ScConfig.PostureMeleeDist) + " m (" + F1(d) + " m now), then one swap";
                }
            }
            return null;
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
            if (stanceFlip || (!IsFight(was) && IsFight(want))) st.StanceSince = now;   // F8: only a new stance restarts the dwell
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
            v.Waiting = st.Waiting;
            v.TargetId = st.TargetId;
            return true;
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
            return e == null || !states.TryGetValue(e.entityId, out st) || now - st.HandAt >= ScConfig.TeamSwapHoldSeconds;
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
                      + (st.NativeClose ? "; 0-XNPCCore's 3 m swap kept (a runner)" : "") + (st.UrgentGun ? "; the gun at once (" + ScRetreat.Her(e) + " target needs it)" : ""));
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
                   + " MELEE/KITE switch(es), " + refusals + " refused swing(s) (0-XNPCCore's bout exits and swap refusals) - counters posture-* in the hook list";
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
    /// </summary>
    public class UAIConsiderationScPosture : UAIConsiderationBase
    {
        private const int Approach = 0, Keep = 1, Ranged = 2;
        private int what = Approach;

        public override void Init(System.Collections.Generic.Dictionary<string, string> parameters)
        {
            base.Init(parameters);
            string v;
            if (parameters == null || !parameters.TryGetValue("action", out v) || v == null) return;
            v = v.Trim().ToLowerInvariant();
            what = v == "keep" ? Keep : v == "ranged" ? Ranged : Approach;
        }

        public override float GetScore(Context _context, object target)
        {
            try
            {
                EntityAlive self = _context.Self;
                if (self == null || !ScXnpc.IsBody(self) || ScXnpc.IsRaider(self)) return 1f;
                // the live facts are the community's members' own (each is false for anyone else); the posture is read for
                // whoever has one (a member, or a stand-in fighting as one); everyone else scores 1
                if (ScWatch.Guarding(self.entityId)) return 0f;                    // a guard holds her post (any posture, any tick)
                EntityAlive t = UAIUtils.ConvertToEntityAlive(target);
                if (what == Approach)
                {
                    if (ScRetreat.Owns(self) || ScRetreat.HurtNoMedsMember(self)) return 0f;   // rt1: no chase out of her room; decision 10
                    return ScPosture.AllowsApproach(self, t) ? 1f : 0f;
                }
                if (ScRetreat.Shelters(self)) return 0f;                            // rt1: sheltering (k1: only="shelter")
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
                return 1f;
            }
        }
    }
}
