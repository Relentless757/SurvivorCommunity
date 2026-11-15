using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round rt1 (2026-11-15, research/flee_meds/DESIGN.md Part A; the user approved decisions 1-13): hurt members retreat
    /// inside at home, and stand off with a gun. The user's request: "They shouldn't flee away from home, they should retreat
    /// inside, closing doors etc. If they have a ranged weapon, they should create distance and continue engaging so they
    /// aren't just leaving others to fend for themselves."
    ///
    /// One state per body, keyed by entity id, host only. The ONLY writer is Update, called from SC's X5 prefix on
    /// SdcsCloseRangeTick (5 Hz, Patches_Xnpc) right after ScTeam.Recount; the threat scan runs at most every ScanSeconds per
    /// body, staggered by id. The UAI considerations (UaiRetreat.cs) only READ it (the n07 rule: UAI runs every consideration
    /// for every candidate several times a second). The two tasks (UAITaskRetreatSC, UAITaskStandOffSC) are C#: they pick the
    /// spot and the back-off point, and write the spot, its failures and "cornered" back through the internal calls below.
    ///
    /// Who (decision 1, live in Eligible): one of the community's members on her own body (never a stand-in, a raider, an
    /// enclave person or a stranger: decision 12), not on watch, not on a Stay order, and either at home (the home zone and
    /// ResidentRoamZoneMargin round it, or the defended area in a base attack: round hl1's AtHome) as a resident, a follower,
    /// one on the way to her post or a runner on the Back leg - region Home - or (decision 4) a follower away from home with
    /// you within twice StandOffFollowerLeash - region Leader, stand-off only. Runners on the way out, at the site or on the
    /// way in keep round r2's flight. RetreatHome false: nobody, ever.
    ///
    /// Entry: hurt with nothing to heal with (ScMedic.HurtNoMeds at FleeBelowHealth - ScFleeHurt's own rule), or at home
    /// Cowardly ($Timid) and not hanging back (ScTeam.HangsBack; RetreatCowards, decision 6) - and a threat: an awake enemy
    /// within FleeRadius (15 m) that is after her or can see her (ScFleeHurt's TargetDistance + SelfVisible).
    /// Mode: StandOff when hurt with a gun with rounds in her pair and no armed enemy within FleeClear (decision 3); else
    /// Shelter at home; away from home with no gun (or armed enemies) nothing - today's FleeFromTarget (decision 5).
    /// Exit (decision 8): healed to RetreatHealedAt; a coward who now hangs back; no threat (after her or seeing her within
    /// FleeClear, or an awake enemy in the zone within 15 m) for RetreatClearSeconds; or no longer eligible - cleared at
    /// once, never kept to act on later (the round hl1 lesson).
    /// FightBack (Shelter): an enemy within RetreatFightBackRange that is after her, or one that hit her in the last 3 s
    /// within 4 m; it lingers 3 s. While it holds the shelter action scores 0 and the pack's attacks take over.
    /// </summary>
    public static class ScRetreat
    {
        public enum Mode { None = 0, Shelter = 1, StandOff = 2 }
        public enum Region { None = 0, Home = 1, Leader = 2 }

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
        /// <summary>Cornered: the stand-off scores 0 this long (the shot fires point-blank).</summary>
        public const float CorneredSeconds = 3f;
        /// <summary>CurrentOrder 2 is SCore's Stay (the cvar 0-XNPCCore's actions are gated on; ScTeam reads it the same way).</summary>
        private const int OrderStay = 2;

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
            /// <summary>The scan: an armed enemy within FleeClear; her pair's gun has rounds; a threat present (the clear clock).</summary>
            public bool Armed, Gun, Present;
            /// <summary>When "no threat" began (0: a threat is present).</summary>
            public float ClearSince;
            public bool FightBack;
            public float FightBackUntil, HitAt;
            public int LastHealth = -1;
            /// <summary>Written by the stand-off task: the stand-off scores 0 until then.</summary>
            public float CorneredUntil;
            public string CorneredWhy;
            /// <summary>The stand-off's target gap (StandOffMin + 2 .. min(StandOffMax, engage range - 1)).</summary>
            public float Gap;
            /// <summary>The stand-off task is walking her back (the consideration's hysteresis), and to where.</summary>
            public bool BackingOff;
            public Vector3 BackOffPoint;
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
        private static readonly List<Entity> near = new List<Entity>();
        private static float nextPrune;
        private static int entered, ended;

        public static void Reset()
        {
            states.Clear();
            near.Clear();
            nextPrune = 0f;
            entered = ended = 0;
        }

        /// <summary>A despawned body's state (entity ids are recycled).</summary>
        public static void Forget(int entityId) { states.Remove(entityId); }

        // ------------------------------------------------------------------ the writer (X5 only) ----

        /// <summary>
        /// The X5 prefix's retreat part (Patches_Xnpc.Patch_NoSwapWhileUsing, on SdcsCloseRangeTick, 5 Hz, after the team
        /// recount): eligibility, the threat scan when due, entry, the mode, the exits and FightBack. The only writer of the
        /// states (the tasks write the spot and "cornered" through NoteSpot, SpotFailed, Cornered). Never throws.
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
                if (now >= nextPrune) Prune(now);
                string notWhy = "not a member";
                Region region = m != null ? Eligible(e, m, out notWhy) : Region.None;
                bool active = st != null && st.Mode != Mode.None;
                if (active && region != st.Region)
                {
                    End(e, st, "ineligible", "no longer " + (st.Region == Region.Home ? "at home" : "with you away from home") + " ("
                        + (region == Region.None ? notWhy : region == Region.Home ? "now at home" : "now away with you") + ")");
                    active = false;
                }
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
                    Mode first = Want(st, region);
                    if (first == Mode.None) return;                                   // away with no gun: today's flight (decision 5)
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
                Mode want = Want(st, region);
                if (want == Mode.None)
                {
                    End(e, st, "no-gun-away", "away from home with no gun to stand off with" + (st.Armed ? " (or armed enemies)" : "") + " - " + She(e) + " flees as before");
                    return;
                }
                if (want != st.Mode)
                {
                    Patches_Xnpc.Count(want == Mode.Shelter ? "retreat-to-shelter" : "retreat-to-standoff");
                    ScLog.Out(Name(e) + " switches " + Her(e) + " retreat from " + ModeName(st.Mode) + " to " + ModeName(want) + ": "
                              + (want == Mode.Shelter ? (!st.Gun ? Her(e) + " gun is dry" : st.Armed ? "armed enemies" : "no longer hurt (a Cowardly member shelters)") : "a gun with rounds again, no armed enemies"));
                    st.Mode = want;
                    st.BackingOff = false;
                    st.CorneredUntil = 0f;
                    st.FightBack = false;
                }
                st.Why = WhyText(e, st, share);
                if (st.Mode == Mode.StandOff) st.Gap = Gap(e);
                else FightBackTick(e, st, now);
            }
            catch (Exception ex)
            {
                ScLog.Once("retreat-update", ex);
            }
        }

        /// <summary>The mode for this scan (decision 3: armed enemies mean shelter); None away from home without a gun.</summary>
        private static Mode Want(State st, Region region)
        {
            if (st.Hurt && st.Gun && !st.Armed) return Mode.StandOff;
            return region == Region.Home ? Mode.Shelter : Mode.None;
        }

        private static void Begin(EntityAlive e, State st, Region region, Mode mode, float now)
        {
            st.Mode = mode;
            st.Region = region;
            st.Since = now;
            st.ClearSince = 0f;
            st.FightBack = false;
            st.FightBackUntil = 0f;
            st.CorneredUntil = 0f;
            st.CorneredWhy = null;
            st.BackingOff = false;
            st.HasSpot = false;
            st.SpotSource = "";
            st.Fallback = null;
            st.Bad.Clear();
            st.Fails = 0;
            st.Doors = 0;
            float share = Share(e);
            st.Why = WhyText(e, st, share);
            if (mode == Mode.StandOff) st.Gap = Gap(e);
            entered++;
            Patches_Xnpc.Count(mode == Mode.Shelter ? "retreat-shelter" : "retreat-standoff");
            if (region == Region.Leader) Patches_Xnpc.Count("retreat-standoff-away");
            ScLog.Out(Name(e) + " retreats - " + (mode == Mode.Shelter ? "SHELTER inside" : "STAND-OFF with " + Her(e) + " gun (gap " + st.Gap.ToString("0.#", CultureInfo.InvariantCulture) + " m)")
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
            st.CorneredUntil = 0f;
            st.BackingOff = false;
            st.HasSpot = false;
            st.Fallback = null;
            st.Bad.Clear();
            st.Fails = 0;
            st.ClearSince = 0f;
        }

        /// <summary>
        /// The threat scan (X5, every ScanSeconds): the nearest awake enemy within FleeRadius that is after her or can see her
        /// (the sight ray only for one nearer than the best so far, as ScRunners.NearestThreat), whether any is present for the
        /// clear clock (after her or seeing her within FleeClear, or awake in the zone within 15 m), an armed enemy within
        /// FleeClear, and her pair's gun.
        /// </summary>
        private static void Scan(EntityAlive e, State st, float now)
        {
            World w = e.world;
            st.Gun = GunReady(e);
            if (w == null) return;
            float clear = ScRunners.FleeClear, flee = ScRunners.FleeRadius;
            near.Clear();
            w.GetEntitiesInBounds(typeof(EntityAlive), new Bounds(e.position, new Vector3(2f * clear, 2f * clear, 2f * clear)), near);
            EntityAlive best = null;
            float bestD2 = flee * flee;
            bool present = false, armed = false, bestAfter = false;
            try
            {
                for (int i = 0; i < near.Count; i++)
                {
                    EntityAlive a = near[i] as EntityAlive;
                    if (a == null || a == e || a.IsDead() || a.IsSleeping) continue;
                    float d2 = (a.position - e.position).sqrMagnitude;
                    if (d2 > clear * clear || !EntityTargetingUtilities.IsEnemy(e, a)) continue;
                    if (global::Harmony.ZombieFeatures.InertEntity.IsInert(a)) continue;
                    if (ScTeam.Armed(a)) armed = true;
                    bool after = a.GetAttackTarget() == e || a.GetRevengeTarget() == e;
                    if (d2 <= bestD2 && (after || a.CanEntityBeSeen(e)))
                    {
                        best = a;
                        bestD2 = d2;
                        bestAfter = after;
                        present = true;
                        continue;
                    }
                    if (present) continue;
                    if (after || (d2 <= ZoneClearRadius * ZoneClearRadius && ScZone.Contains(a.GetBlockPosition())) || a.CanEntityBeSeen(e)) present = true;
                }
            }
            finally { near.Clear(); }
            st.Armed = armed;
            st.Present = present;
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

        /// <summary>FightBack (Shelter, X5): an enemy within RetreatFightBackRange after her, or a hit from her revenge target within 4 m in the last 3 s.</summary>
        private static void FightBackTick(EntityAlive e, State st, float now)
        {
            bool close = AfterHerWithin(e, ScConfig.RetreatFightBackRange);
            EntityAlive rv = e.GetRevengeTarget();
            bool hit = rv != null && !rv.IsDead() && st.HitAt > 0f && now - st.HitAt <= FightBackHitSeconds
                       && (rv.position - e.position).sqrMagnitude <= FightBackRevengeRange * FightBackRevengeRange;
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

        /// <summary>An awake, living enemy within r that has her as its attack target.</summary>
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
                found = a.GetAttackTarget() == e && EntityTargetingUtilities.IsEnemy(e, a);
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
            if (ScWatch.OnWatch(m)) { why = "on watch (she holds her post)"; return Region.None; }
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
                    if (ScConfig.StandOffFollowerLeash <= 0f) { why = "a follower away from home (StandOffFollowerLeash 0: she flees as before)"; return Region.None; }
                    EntityPlayer leader = LeaderOf(e);
                    if (leader == null) { why = "a follower away from home with no leader in the world"; return Region.None; }
                    if (ScRunners.XZ(leader.position, e.position) > 2f * ScConfig.StandOffFollowerLeash) { why = "a follower away from home, farther than " + (2f * ScConfig.StandOffFollowerLeash).ToString("0", CultureInfo.InvariantCulture) + " m from you"; return Region.None; }
                    return Region.Leader;
                case MemberStatus.OnRun:
                    if (ScRunners.IsPhysical(m) && !m.Offline && m.RunLeg == RunLeg.Back && home) return Region.Home;
                    why = "on a run (round r2's flight and turn home)";
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

        /// <summary>The retreat owns her (any mode, live). A pure read.</summary>
        public static bool Owns(EntityAlive e)
        {
            State st;
            return Active(e, out st);
        }

        /// <summary>ScRetreat (8.5): Shelter, not fighting back. A pure read.</summary>
        public static bool ShelterScores(EntityAlive e)
        {
            State st;
            return Active(e, out st) && st.Mode == Mode.Shelter && !st.FightBack;
        }

        /// <summary>I11 (ScTeam.FriendWeight): sheltering and not fighting back - she is not in the fight. A pure read.</summary>
        public static bool Hiding(EntityAlive e) { return ShelterScores(e); }

        /// <summary>A stand-off is on for her (CloseRange's keep-gun, GunFirst's draw). A pure read.</summary>
        public static bool StandsOff(EntityAlive e)
        {
            State st;
            return Active(e, out st) && st.Mode == Mode.StandOff;
        }

        /// <summary>The stand-off task may run (StandOff, not cornered). A pure read.</summary>
        public static bool StandOffMayRun(EntityAlive e)
        {
            State st;
            return Active(e, out st) && st.Mode == Mode.StandOff && Time.time >= st.CorneredUntil;
        }

        /// <summary>
        /// ScStandOff (8.2, per enemy): StandOff, not cornered, and the enemy (alive, awake) inside StandOffMin - or the
        /// back-off is already running and it is still nearer than her target gap (hysteresis). A pure read.
        /// </summary>
        public static bool StandOffScores(EntityAlive e, EntityAlive t)
        {
            State st;
            if (t == null || t.IsDead() || t.IsSleeping || !Active(e, out st) || st.Mode != Mode.StandOff || Time.time < st.CorneredUntil) return false;
            float d2 = (t.position - e.position).sqrMagnitude;
            if (d2 < ScConfig.StandOffMin * ScConfig.StandOffMin) return true;
            return st.BackingOff && d2 < st.Gap * st.Gap;
        }

        /// <summary>
        /// ScNotRetreatHome (on ScFleeHurt: coward false; on ScFlee: coward true): false where the retreat owns her or would at
        /// home - a member at home (live, whatever the state: trap 9) - and away from home a follower whose last scan found
        /// a gun and no armed enemy (she stands off instead of fleeing). True everywhere else, and always with RetreatHome
        /// off (or, for the cowards' flee, RetreatCowards off). A pure read.
        /// </summary>
        public static bool FleeAllowed(EntityAlive e, bool coward)
        {
            if (e == null || !ScConfig.RetreatHome) return true;
            State st;
            if (Active(e, out st)) return false;
            if (coward && !ScConfig.RetreatCowards) return true;
            Member m = ScRegistry.GetMember(e.entityId);
            if (m == null) return true;
            string why;
            Region r = Eligible(e, m, out why);
            if (r == Region.Home) return false;
            if (r == Region.Leader && !coward && states.TryGetValue(e.entityId, out st) && Time.time - st.At <= StaleSeconds && st.Gun && !st.Armed) return false;
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

        internal static float GapOf(EntityAlive e)
        {
            State st;
            return Active(e, out st) && st.Gap > 0f ? st.Gap : ScConfig.StandOffMax;
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

        /// <summary>The stand-off task: no way to back off, or the threat gains on her - she stands and shoots for CorneredSeconds.</summary>
        internal static void Cornered(EntityAlive e, string why)
        {
            State st;
            if (!Active(e, out st)) return;
            st.CorneredUntil = Time.time + CorneredSeconds;
            st.CorneredWhy = why;
            st.BackingOff = false;
            Patches_Xnpc.Count("retreat-cornered");
            ScLog.Verbose(Name(e) + " is cornered (" + why + ") - stands and shoots for " + CorneredSeconds.ToString("0", CultureInfo.InvariantCulture) + " s");
        }

        /// <summary>The stand-off task started (point) or stopped walking her back.</summary>
        internal static void BackingOff(EntityAlive e, bool on, Vector3 point)
        {
            State st;
            if (e == null || !states.TryGetValue(e.entityId, out st)) return;
            st.BackingOff = on && st.Mode == Mode.StandOff;
            if (on) { st.BackOffPoint = point; Patches_Xnpc.Count("retreat-backoff"); }
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
        /// The stand-off's target gap G (decision 2): StandOffMax, but never past her engage range minus 1 m (her Shooting
        /// stars' range, ScShooting.EngageRange, and the gun's own), and at least StandOffMin + 2 m.
        /// </summary>
        private static float Gap(EntityAlive e)
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
        private static string ModeName(Mode m) { return m == Mode.Shelter ? "shelter" : m == Mode.StandOff ? "stand-off" : "none"; }
        private static string Pct(float f) { return (f * 100f).ToString("0", CultureInfo.InvariantCulture) + "%"; }
        internal static string Cell(Vector3 p) { Vector3i c = World.worldToBlockPos(p); return c.x + " " + c.y + " " + c.z; }

        // ------------------------------------------------------------------ console ----

        /// <summary>One line for sc xnpc and the sc retreat list (a read).</summary>
        public static string Line(EntityAlive e)
        {
            if (!ScConfig.RetreatHome) return "retreat off (RetreatHome=false)";
            State st;
            Member m = e != null ? ScRegistry.GetMember(e.entityId) : null;
            if (m == null) return "retreat: not a member (round rt1 is for the community's members)";
            string why;
            Region r = Eligible(e, m, out why);
            if (!Active(e, out st))
            {
                bool hurt = ScMedic.HurtNoMeds(e, ScConfig.FleeBelowHealth);
                return "retreat: none - " + (r == Region.None ? "not eligible (" + why + ")" : (r == Region.Home ? "at home" : "away with you"))
                       + (hurt ? ", HURT with nothing to heal with" : "") + (Timid(e) ? ", Cowardly" + (ScTeam.HangsBack(e) ? " (hangs back)" : "") : "");
            }
            float now = Time.time;
            var sb = new StringBuilder("retreat: ").Append(st.Mode == Mode.Shelter ? "SHELTER" : "STAND-OFF").Append(" for ").Append((now - st.Since).ToString("0", CultureInfo.InvariantCulture)).Append(" s - ")
                .Append(st.Why).Append("; threat ").Append(ThreatText(e, st));
            if (st.ClearSince > 0f) sb.Append("; clear ").Append((now - st.ClearSince).ToString("0", CultureInfo.InvariantCulture)).Append(" of ").Append(ScConfig.RetreatClearSeconds.ToString("0", CultureInfo.InvariantCulture)).Append(" s");
            if (st.Mode == Mode.Shelter)
            {
                if (st.FightBack) sb.Append("; FIGHTING BACK");
                if (st.HasSpot) sb.Append("; spot ").Append(Cell(st.Spot)).Append(" (").Append(st.SpotSource).Append(", ").Append(ScRunners.XZ(e.position, st.Spot).ToString("0.#", CultureInfo.InvariantCulture)).Append(" m off)");
                else sb.Append("; no spot yet");
                if (st.Fallback != null) sb.Append("; fallback: ").Append(st.Fallback);
            }
            else
            {
                sb.Append("; gap ").Append(st.Gap.ToString("0.#", CultureInfo.InvariantCulture)).Append(" m");
                if (st.BackingOff) sb.Append(", BACKING OFF to ").Append(Cell(st.BackOffPoint));
                if (now < st.CorneredUntil) sb.Append(", CORNERED ").Append((st.CorneredUntil - now).ToString("0.0", CultureInfo.InvariantCulture)).Append(" s more (").Append(st.CorneredWhy).Append(')');
            }
            return sb.ToString();
        }

        /// <summary>The totals for sc retreat.</summary>
        public static string Report()
        {
            int live = 0, shelter = 0, standoff = 0;
            float now = Time.time;
            foreach (var kv in states)
            {
                if (kv.Value.Mode == Mode.None || now - kv.Value.At > StaleSeconds) continue;
                live++;
                if (kv.Value.Mode == Mode.Shelter) shelter++; else standoff++;
            }
            return live + " retreating now (" + shelter + " sheltering, " + standoff + " standing off); " + entered + " retreat(s) begun, " + ended + " ended this session";
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
                output("[SC] retreat (round rt1): " + ScConfig.RetreatLine());
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
            lines.Add("[SC]   eligible: " + (r == Region.Home ? "AT HOME (shelter or stand-off)" : r == Region.Leader ? "AWAY WITH YOU (stand-off only, within " + ScConfig.StandOffFollowerLeash.ToString("0", CultureInfo.InvariantCulture) + " m of you)" : "no - " + why));
            int max = e.GetMaxHealth();
            lines.Add("[SC]   health " + e.Health + "/" + max + " (" + Pct(Share(e)) + "); heal items " + ScMedic.HealItemCount(e) + ", can treat herself " + (ScMedic.CanTreatSelf(e) ? "yes" : "NO")
                      + "; hurt with nothing to heal with (at or below FleeBelowHealth " + Pct(ScConfig.FleeBelowHealth) + "): " + (ScMedic.HurtNoMeds(e, ScConfig.FleeBelowHealth) ? "YES" : "no")
                      + "; Cowardly: " + (Timid(e) ? "yes, " + (ScTeam.HangsBack(e) ? "HANGS BACK with her gun (tm1)" : "not hanging back") + (ScConfig.RetreatCowards ? "" : " (RetreatCowards false)") : "no")
                      + "; her pair's gun with rounds: " + (GunReady(e) ? "yes" : "no"));
            State st;
            bool active = Active(e, out st);
            if (!states.TryGetValue(e.entityId, out st)) lines.Add("[SC]   state: none yet (made the first time she is hurt with nothing, or a coward, where she may retreat)");
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
                if (st.Mode == Mode.StandOff)
                    lines.Add("[SC]   stand-off: gap " + st.Gap.ToString("0.#", CultureInfo.InvariantCulture) + " m" + (st.BackingOff ? ", BACKING OFF to " + Cell(st.BackOffPoint) : ", not backing off")
                              + (now < st.CorneredUntil ? ", CORNERED " + (st.CorneredUntil - now).ToString("0.0", CultureInfo.InvariantCulture) + " s more (" + st.CorneredWhy + ")" : ""));
            }
            EntityAlive t;
            Vector3 tp;
            ThreatPlace(e, out tp, out t);
            lines.Add("[SC]   gates now: ScRetreating " + (ShelterScores(e) ? 1 : 0) + ", ScStandOff on " + (t != null ? t.EntityName + " " + (StandOffScores(e, t) ? 1 : 0) : "- (no live threat)")
                      + ", ScNotRetreatHome hurt " + (FleeAllowed(e, false) ? 1 : 0) + " / coward " + (FleeAllowed(e, true) ? 1 : 0)
                      + ", ScNotRetreating " + (Owns(e) ? 0 : 1) + ", ScNotHurtNoMeds " + (HurtNoMedsMember(e) ? 0 : 1));
            if (r == Region.Home)
            {
                int fails;
                List<Vector3i> bad = BadSpots(e, out fails);
                ScShelter.PickResult pr = ScShelter.Pick(e, bad, fails, true);
                lines.Add("[SC]   she would shelter now at: " + (pr.Kind == ScShelter.PickKind.None ? "-" : Cell(pr.Spot) + " - " + pr.Kind + ", " + (pr.Note ?? pr.Source)
                          + ", " + ScRunners.XZ(e.position, pr.Spot).ToString("0.#", CultureInfo.InvariantCulture) + " m off") + " (a read; " + ScShelter.Summary() + ")");
            }
            lines.Add("[SC]   doors: ScDoors shuts the ordinary doors she passed that stood open (never a drawbridge or hatch she did not open, never within 2.5 m of you)");
        }
    }
}
