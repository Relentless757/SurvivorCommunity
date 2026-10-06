using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Base defence D2: live horde attacks on the home base (BaseDefence_Plan.md in the game root; its section 6 decides).
    /// Host only, all behind BaseAttacks.
    ///   Call      attention (ScNoise) at 100 calls a horde while the player is home. Until D4 a call waits for the
    ///             player: away, attention just stays full (HoldsFullAttention: full stays full until a call, whatever
    ///             holds it back). Never within BaseAttackGapDays of the last attack's end,
    ///             never from 10:00 on blood-moon day until the blood moon ends at dawn (vanilla's blood moon comes for
    ///             the player at home already), never while an outpost attack is being fought (one live attack at a
    ///             time). Attention resets at the call and gains nothing while an attack is open.
    ///   Arrival   BaseAttackLeadMin..MaxHours later, from one side (the bearing kept with the attack). A player away
    ///             from home at that time, or a blood-moon window, puts it off: it comes the lead time after the player
    ///             is back and the window is over.
    ///   Size      (BaseAttackBase + gamestage x BaseAttackPerGamestage) x (1 + BaseAttackPerMember x residents),
    ///             BaseAttackMin..BaseAttackMax, fixed at the call.
    ///   Mix       vanilla's gamestage list BaseAttackGameStages (ScoutGSList: the screamer hordes, the one vanilla horde
    ///             that comes to a place): the stage for the host's gamestage names the entity group, so ferals and
    ///             radiated come when they would in vanilla. Never a WanderingHorde (its stages stop at 50).
    ///   Spawning  one a second, BaseAttackSpawnMin..Max m beyond the zone's edge within BaseAttackSpread degrees of the
    ///             bearing, on vanilla's own spawn-point rules (a loaded chunk, terrain mobs may spawn on, two free
    ///             blocks, BaseAttackMinPlayerRange from every player, clear of bedrolls), and only while vanilla's
    ///             live-zombie cap (AIDirector.CanSpawn) has room: they wait, never drop. After SideFailSeconds without a
    ///             spot on that side they may come from any side; after twice that with none at all (a base ringed by POI
    ///             floors or paving, which vanilla's terrain-only rule rejects), from any standable block in the ring.
    ///   Steering  marching zombies are kept on a point by the home block (an investigate position, re-asserted, as
    ///             vanilla's wandering hordes are). In the base they are let go to vanilla's horde behaviour - an
    ///             investigate position hides every non-player from them (EAISetNearestEntityAsTarget), so a steered
    ///             zombie would walk past the residents - and only nudged to another spot in the zone after IdleNudge
    ///             seconds with nothing to do. A zombie that wanders far off is steered back.
    ///   Defence   while it is live the residents defend the whole zone + BaseAttackDefendMargin (WideDefence: ScIntruder,
    ///             the leash ScFarFromPost, the watchdog), not just DefendBaseRadius around their posts.
    ///   Held      every attacker dead while the player is home: the Defence morale part, sc_ev_defend, a tooltip and a
    ///             ledger line with what the fight cost the stockpile (ScAmmo's resupplies, NoteResupply).
    ///   Left      the player more than BaseAttackLeaveRange m outside the zone: the attackers still standing go, and
    ///             only they come back, the lead time after the player returns - nobody fights while the player is away.
    ///             A save and reload mid-fight is the same: the attackers are Dynamic and never saved; the persisted
    ///             counts bring back only the ones still standing, BaseAttackResumeSeconds after the load.
    ///   Timeout   BaseAttackMaxHours after it went live with no kill in the last game hour, the stragglers are let go
    ///             as ordinary zombies and it counts as held (none killed at all: they lost the trail).
    /// </summary>
    public static class ScBaseAttack
    {
        public const int StateNone = 0, StateCalled = 1, StateLive = 2;
        public const string DefendEvent = ScLeadership.DefendEvent;
        /// <summary>Metres past the defended area a defender may go before its defence pauses (ScIntruder's stray rule)...</summary>
        public const float StrayExtra = 3f;
        /// <summary>...and before the leash and the watchdog's far rule walk it home: 2 m past the stray band, as at a post.</summary>
        public const float LeashExtra = 5f;
        /// <summary>Seconds without a spawn spot on the attack's side before the spawns may come from any side.</summary>
        private const int SideFailSeconds = 20;
        /// <summary>Seconds a zombie in the base may stand with no target and nowhere to go before it is sent elsewhere in the zone.</summary>
        private const float IdleNudge = 45f;
        /// <summary>A zombie this far outside the defended area is steered back to the base.</summary>
        private const float StrayBack = 20f;
        private const ulong TicksPerHour = 1000UL;
        private const ulong TicksPerDay = 24000UL;

        private sealed class Attacker
        {
            public Vector3 Steer;
            public bool Arrived;
            public float IdleSince;
        }

        // session state: the attackers in the world, and the zone as last seen (refreshed every tick)
        private static readonly Dictionary<int, Attacker> attackers = new Dictionary<int, Attacker>();
        private static readonly List<int> scratch = new List<int>();
        private static readonly List<int[]> rects = new List<int[]>();
        private static bool sessionReady;
        private static float nextTick, resumeAt, firstSpawnAt, nextSave;
        private static bool arrivalTold, anySide, looseGround;
        private static int sideFails, spawnFails;
        private static ulong lastProgress;
        private static int lastClass = -1;
        private static Vector3 homeCentre;

        public static void Reset()
        {
            attackers.Clear();
            rects.Clear();
            sessionReady = false;
            nextTick = resumeAt = firstSpawnAt = nextSave = 0f;
            arrivalTold = anySide = looseGround = false;
            sideFails = spawnFails = 0;
            lastProgress = 0UL;
        }

        /// <summary>The registry was (re)loaded: whatever this session spawned is squared with it on the next tick.</summary>
        public static void OnRegistryChanged()
        {
            attackers.Clear();
            sessionReady = false;
        }

        public static bool Open { get { return ScRegistry.Community.AttackState != StateNone; } }
        public static bool Live { get { return ScRegistry.Community.AttackState == StateLive; } }

        /// <summary>
        /// A full attention meter stays full until a horde is called (ScNoise): whatever holds the call back - the player
        /// away (the user's D2 answer), the gap, the blood moon, an outpost fight - the dead do not forget the base.
        /// </summary>
        public static bool HoldsFullAttention { get { return ScConfig.BaseAttacks && !Open; } }

        /// <summary>The residents defend the whole zone + BaseAttackDefendMargin (a live attack, the zone known).</summary>
        public static bool WideDefence { get { return Live && rects.Count > 0; } }

        // ------------------------------------------------------------------ geometry ----

        /// <summary>Metres outside the home zone, XZ (0 inside), from the zone as last refreshed; MaxValue with none.</summary>
        public static float OutsideZone(Vector3 p)
        {
            if (rects.Count == 0) return float.MaxValue;
            float best = float.MaxValue;
            for (int i = 0; i < rects.Count; i++)
            {
                int[] r = rects[i];
                float dx = Math.Max(Math.Max(r[0] - p.x, 0f), p.x - (r[1] + 1));
                float dz = Math.Max(Math.Max(r[2] - p.z, 0f), p.z - (r[3] + 1));
                float d = dx * dx + dz * dz;
                if (d < best) best = d;
            }
            return Mathf.Sqrt(best);
        }

        /// <summary>Inside the zone + BaseAttackDefendMargin (+ extra) - the area defended during an attack.</summary>
        public static bool InDefenceArea(Vector3 p, float extra = 0f)
        {
            return OutsideZone(p) <= ScConfig.BaseAttackDefendMargin + extra;
        }

        public static bool PlayerHome(EntityPlayer player)
        {
            return player != null && OutsideZone(player.position) <= ScConfig.BaseAttackHomeMargin;
        }

        /// <summary>How far from 'from' the zone ends along 'dir' (its last block on that line, for an L or a T too).</summary>
        private static float EdgeAlong(Vector3 from, Vector3 dir)
        {
            float last = 0f;
            for (float t = 0f; t <= 400f; t += 2f)
                if (OutsideZone(from + dir * t) <= 0f) last = t;
            return last;
        }

        private static Vector3 Dir(float bearingDeg)
        {
            float a = bearingDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));     // 0 = north (+z), 90 = east (+x)
        }

        public static string Side(int bearing)
        {
            string[] names = { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };
            int i = Mathf.RoundToInt(((bearing % 360) + 360) % 360 / 45f) % 8;
            return names[i];
        }

        /// <summary>A random spot inside the zone (inset 2 m), at the home block's height.</summary>
        private static Vector3 ZonePoint(GameRandom rnd)
        {
            if (rects.Count == 0) return homeCentre;
            int[] r = rects[rnd.RandomRange(0, rects.Count)];
            float x0 = r[0] + 2f, x1 = r[1] - 1f, z0 = r[2] + 2f, z1 = r[3] - 1f;
            if (x1 < x0) { x0 = r[0]; x1 = r[1] + 1f; }
            if (z1 < z0) { z0 = r[2]; z1 = r[3] + 1f; }
            return new Vector3(x0 + rnd.RandomFloat * (x1 - x0), homeCentre.y, z0 + rnd.RandomFloat * (z1 - z0));
        }

        // ------------------------------------------------------------------ rules ----

        /// <summary>
        /// From 10:00 on blood-moon day (a 1-2 hour lead never lands an arrival in the afternoon) until the blood moon ends
        /// at the next dawn. (Vanilla moves BloodMoonDay on to the next blood moon at that dawn - EndBloodMoon ->
        /// CalcNextDay - so the day after is never "blood-moon day + 1" here.)
        /// </summary>
        public static bool BloodMoonWindow(World w)
        {
            try
            {
                if (w == null) return false;
                if (w.aiDirector != null && w.aiDirector.BloodMoonComponent != null && w.aiDirector.BloodMoonComponent.BloodMoonActive) return true;
                int bm = GameStats.GetInt(EnumGameStats.BloodMoonDay);
                if (bm <= 0) return false;
                int day = GameUtils.WorldTimeToDays(w.worldTime), hour = GameUtils.WorldTimeToHours(w.worldTime);
                if (day == bm && hour >= 10) return true;
            }
            catch (Exception e)
            {
                ScLog.Once("baseattack-bloodmoon", e);
            }
            return false;
        }

        /// <summary>
        /// Why no horde could be called right now (null: it could). 'forced' (the console) skips the gap, the blood moon and
        /// the player being away (such a call waits for them like any other).
        /// </summary>
        public static string WhyNoCall(World w, EntityPlayerLocal player, bool forced)
        {
            Community c = ScRegistry.Community;
            if (!ScConfig.BaseAttacks) return "base attacks are off (BaseAttacks=false)";
            if (!forced && !ScConfig.BaseNoise) return "the noise meter is off (BaseNoise=false): only the console calls one";
            if (!c.HasHome) return "no community home";
            if (Open) return "an attack is already open";
            if (!EntityFactory.EnemySpawnMode) return "enemy spawning is off";
            if (!forced && w.worldTime < c.AttackNotBefore) return "too soon after the last attack (not before " + Clock(c.AttackNotBefore) + ")";
            if (!forced && BloodMoonWindow(w)) return "the blood moon (no calls from 10:00 on its day until it ends at dawn)";
            if (ScOutpostRisk.AnyLive()) return "an outpost attack is being fought";
            if (!forced && !PlayerHome(player)) return "the player is away from home (a call waits for them)";
            return null;
        }

        /// <summary>How many come: the decided sizes, about 8 early, 14 at gamestage 50, 22 at 100, +5% per resident.</summary>
        public static int Size(EntityPlayerLocal player)
        {
            int gs = 0;
            try { gs = player != null ? player.gameStage : 0; } catch (Exception) { }
            int residents = ScRegistry.CountStatus(MemberStatus.Resident);
            float n = (ScConfig.BaseAttackBase + gs * ScConfig.BaseAttackPerGamestage) * (1f + ScConfig.BaseAttackPerMember * residents);
            int hi = Math.Max(1, ScConfig.BaseAttackMax);
            return Mathf.Clamp(Mathf.RoundToInt(n), Math.Min(hi, Math.Max(1, ScConfig.BaseAttackMin)), hi);
        }

        private static ulong LeadTicks(GameRandom rnd)
        {
            float lo = Math.Max(0f, ScConfig.BaseAttackLeadMinHours), hi = Math.Max(lo, ScConfig.BaseAttackLeadMaxHours);
            return (ulong)((lo + rnd.RandomFloat * (hi - lo)) * TicksPerHour);
        }

        // ------------------------------------------------------------------ the ticker ----

        /// <summary>ScTicker, every second: the call, the arrival, the fight.</summary>
        public static void Tick(World w, EntityPlayerLocal player)
        {
            try
            {
                if (w == null || player == null) return;
                float now = Time.unscaledTime;
                if (now < nextTick) return;
                nextTick = now + 1f;
                // the pause menu stops the world but not this ticker: nothing spawns into a frozen world
                if (GameManager.Instance != null && GameManager.Instance.IsPaused()) return;
                Community c = ScRegistry.Community;
                ScZone.Rects(rects);
                if (!c.HasHome || rects.Count == 0)
                {
                    if (Open) Abort(w, "the community has no home any more");
                    return;
                }
                homeCentre = new Vector3(c.Home.x + 0.5f, c.Home.y, c.Home.z + 0.5f);
                if (Open && !ScConfig.BaseAttacks) { Abort(w, "BaseAttacks is off"); return; }
                SaneTimes(w, c);
                switch (c.AttackState)
                {
                    case StateNone:
                        if (c.Attention >= 100f && WhyNoCall(w, player, false) == null) Call(w, player, 0, false, "attention 100%");
                        break;
                    case StateCalled:
                        TickCalled(w, player, c);
                        break;
                    case StateLive:
                        TickLive(w, player, c, now);
                        break;
                    default:
                        ScLog.Warn("unknown base attack state " + c.AttackState + " - cleared");
                        Clear(c);
                        ScRegistry.SaveAtomic();
                        break;
                }
            }
            catch (Exception e)
            {
                ScLog.Once("baseattack-tick", e);
            }
        }

        /// <summary>
        /// The world clock went back (an older save, a time command) or the registry was edited: no wait longer than the
        /// gap, no arrival further off than the longest lead (then it comes the lead time after the player is home), no
        /// fight that started in the future (the timeout would never come).
        /// </summary>
        private static void SaneTimes(World w, Community c)
        {
            ulong now = w.worldTime;
            ulong gapEnd = now + (ulong)(Math.Max(0f, ScConfig.BaseAttackGapDays) * TicksPerDay);
            if (c.AttackNotBefore > gapEnd) { c.AttackNotBefore = gapEnd; ScLog.Out("base attack: the next call's wait was beyond the gap (clock went back?) - now " + Clock(gapEnd)); }
            if (c.AttackState == StateCalled && c.AttackArrive > now + (ulong)((Math.Max(0f, ScConfig.BaseAttackLeadMaxHours) + 1f) * TicksPerHour))
            {
                c.AttackArrive = 0UL;
                ScLog.Out("base attack: the arrival was further off than any lead (clock went back?) - it comes the lead time after the player is home");
            }
            if (c.AttackState == StateLive && c.AttackStarted > now) c.AttackStarted = now;
            if (lastProgress > now) lastProgress = now;
        }

        /// <summary>A horde is called: size, side and arrival are fixed now. 'size' 0 = the usual; 'now' = it arrives at once.</summary>
        public static void Call(World w, EntityPlayerLocal player, int size, bool now, string why)
        {
            Community c = ScRegistry.Community;
            GameRandom rnd = w.GetGameRandom();
            Release(w, true);
            c.AttackState = StateCalled;
            c.AttackBearing = rnd.RandomRange(0, 360);
            c.AttackSize = size > 0 ? Math.Min(size, 200) : Size(player);
            c.AttackSpawned = c.AttackKilled = 0;
            c.AttackAmmoRounds = c.AttackAmmoPoints = 0;
            c.AttackStarted = 0UL;
            c.AttackArrive = w.worldTime + (now ? 0UL : LeadTicks(rnd));
            if (c.AttackArrive == 0UL) c.AttackArrive = 1UL;
            c.Attention = 0f;                     // the dead have found the base: attention starts over
            sessionReady = true;
            // no word to the player: only a guard on watch sees a horde coming (D3, decision Q6)
            ScLog.Out("base attack called (" + why + "): " + c.AttackSize + " zombie(s) from the " + Side(c.AttackBearing)
                      + " (bearing " + c.AttackBearing + "), arriving " + Clock(c.AttackArrive) + "; mix " + Mix(player));
            ScRegistry.SaveAtomic();
        }

        private static void TickCalled(World w, EntityPlayerLocal player, Community c)
        {
            bool home = PlayerHome(player);
            bool held = BloodMoonWindow(w) || ScOutpostRisk.AnyLive();
            if (c.AttackArrive == 0UL)
            {
                // put off (the player was away, or the blood moon): it comes the lead time after the player is back
                if (home && !held)
                {
                    c.AttackArrive = w.worldTime + LeadTicks(w.GetGameRandom());
                    ScLog.Out("base attack: the player is home - " + (c.AttackSize - c.AttackKilled) + " zombie(s) arrive " + Clock(c.AttackArrive));
                    ScRegistry.SaveAtomic();
                }
                return;
            }
            if (w.worldTime < c.AttackArrive) return;
            if (!home || held)
            {
                c.AttackArrive = 0UL;
                ScLog.Out("base attack due but " + (!home ? "the player is away from home" : "it is the blood moon or an outpost fight") + " - it waits");
                ScRegistry.SaveAtomic();
                return;
            }
            GoLive(w, c);
        }

        private static void GoLive(World w, Community c)
        {
            c.AttackState = StateLive;
            c.AttackStarted = w.worldTime;
            c.AttackSpawned = c.AttackKilled;      // everyone not yet killed comes
            Release(w, true);
            StartSession(w, 0f);
            ScLog.Out("base attack arrives: " + (c.AttackSize - c.AttackKilled) + " zombie(s) from the " + Side(c.AttackBearing));
            ScRegistry.SaveAtomic();
        }

        private static void StartSession(World w, float graceUntil)
        {
            sessionReady = true;
            resumeAt = graceUntil;
            firstSpawnAt = 0f;
            arrivalTold = anySide = looseGround = false;
            sideFails = spawnFails = 0;
            lastProgress = w.worldTime;
        }

        private static void TickLive(World w, EntityPlayerLocal player, Community c, float now)
        {
            if (!sessionReady)
            {
                // loaded mid-fight: the attackers were never saved (Dynamic) - only the ones still standing come back
                attackers.Clear();
                c.AttackSpawned = c.AttackKilled;
                StartSession(w, now + Math.Max(0f, ScConfig.BaseAttackResumeSeconds));
                ScLog.Out("base attack resumes after a load: " + (c.AttackSize - c.AttackKilled) + " zombie(s) still standing come back from the " + Side(c.AttackBearing));
                ScRegistry.SaveAtomic();
            }
            if (OutsideZone(player.position) > ScConfig.BaseAttackLeaveRange) { Suspend(w, player, c); return; }

            bool changed = Watch(w, c, now);
            if (c.AttackSpawned < c.AttackSize && now >= resumeAt && TrySpawn(w, player, c, now)) changed = true;

            if (c.AttackSpawned >= c.AttackSize && attackers.Count == 0)
            {
                // nothing killed (nothing could be spawned, or a size of 0) is no win
                if (c.AttackKilled > 0) Held(w, player, c, false);
                else LostTrail(w, c);
                return;
            }
            if (w.worldTime >= c.AttackStarted + (ulong)(Math.Max(0.5f, ScConfig.BaseAttackMaxHours) * TicksPerHour)
                && w.worldTime >= lastProgress + TicksPerHour)
            {
                if (c.AttackKilled > 0) Held(w, player, c, true);
                else LostTrail(w, c);
                return;
            }
            if (!arrivalTold && attackers.Count > 0 && (AnyAtTheWalls(w) || now - firstSpawnAt >= 60f))
            {
                arrivalTold = true;
                ScCommunity.Tell(player, string.Format(Localization.Get("scBaseArrived"), c.AttackSize - c.AttackKilled), "ui_denied");
            }
            KeepGrid(w, c);
            if (changed && now >= nextSave) { nextSave = now + 10f; ScRegistry.SaveAtomic(); }
        }

        /// <summary>Counts the dead and steers the rest. True when a count changed.</summary>
        private static bool Watch(World w, Community c, float now)
        {
            bool changed = false;
            scratch.Clear();
            scratch.AddRange(attackers.Keys);
            GameRandom rnd = w.GetGameRandom();
            foreach (int id in scratch)
            {
                Attacker r = attackers[id];
                EntityAlive z = w.GetEntity(id) as EntityAlive;
                if (z == null || z.IsDead())
                {
                    // dead, or gone from the world (nothing despawns a horde zombie while a player is about): one fewer
                    attackers.Remove(id);
                    c.AttackKilled = Math.Min(c.AttackSize, c.AttackKilled + 1);
                    lastProgress = w.worldTime;
                    if (z != null) Unflag(z);           // the body lingers a while: it keeps no chunks loaded
                    changed = true;
                    continue;
                }
                try
                {
                    Steer(z, r, rnd, now);
                }
                catch (Exception e)
                {
                    ScLog.Once("baseattack-steer", e);   // one odd body never stops the count, the spawning or the timeout
                }
            }
            return changed;
        }

        private static void Steer(EntityAlive z, Attacker r, GameRandom rnd, float now)
        {
            EntityAlive t = z.GetAttackTarget();
            if (t == null || t.IsDead()) t = z.GetRevengeTarget();
            if (t != null && !t.IsDead()) { r.IdleSince = 0f; return; }      // its own fight: vanilla behaviour
            if (!r.Arrived)
            {
                // at the walls: vanilla's horde behaviour from here (it keeps the way in it has, which runs out by itself)
                if (InDefenceArea(z.position)) { r.Arrived = true; r.IdleSince = 0f; return; }
                if (z.HasInvestigatePosition)
                {
                    Vector3 ip = z.InvestigatePosition;
                    if ((ip - r.Steer).sqrMagnitude >= 0.25f && InDefenceArea(ip)) return;   // heard something in the base: it goes there anyway
                }
                z.SetInvestigatePosition(r.Steer, 6000, true);                  // on its way (re-asserted), lost it, or drawn off
                return;
            }
            if (OutsideZone(z.position) > ScConfig.BaseAttackDefendMargin + StrayBack)
            {
                // wandered off: back to the base
                r.Arrived = false;
                r.Steer = AIWanderingHordeSpawner.RandomPos(ScCommunity.World.aiDirector, homeCentre, 6f);
                z.SetInvestigatePosition(r.Steer, 6000, true);
                return;
            }
            if (z.HasInvestigatePosition) { r.IdleSince = 0f; return; }
            if (r.IdleSince <= 0f) { r.IdleSince = now; return; }
            if (now - r.IdleSince < IdleNudge) return;
            // nothing to fight where it stands: another part of the base (an investigate position hides the residents from
            // it only on the way; it meets them, or they it)
            r.IdleSince = 0f;
            z.SetInvestigatePosition(ZonePoint(rnd), 6000, true);
        }

        private static bool AnyAtTheWalls(World w)
        {
            foreach (int id in attackers.Keys)
            {
                EntityAlive z = w.GetEntity(id) as EntityAlive;
                if (z != null && !z.IsDead() && InDefenceArea(z.position, 5f)) return true;
            }
            return false;
        }

        /// <summary>Vanilla's A* grid along the way in while they spawn, then around the horde (AIWanderingHordeSpawner's pattern).</summary>
        private static void KeepGrid(World w, Community c)
        {
            try
            {
                AstarManager a = AstarManager.Instance;
                if (a == null) return;
                if (c.AttackSpawned < c.AttackSize)
                {
                    Vector3 dir = Dir(c.AttackBearing);
                    float mid = EdgeAlong(homeCentre, dir) + 0.5f * (ScConfig.BaseAttackSpawnMin + ScConfig.BaseAttackSpawnMax);
                    a.AddLocationLine(homeCentre + dir * mid, homeCentre, 64);
                    return;
                }
                Vector3 sum = Vector3.zero;
                int n = 0;
                foreach (int id in attackers.Keys)
                {
                    EntityAlive z = w.GetEntity(id) as EntityAlive;
                    if (z == null || z.IsDead()) continue;
                    sum += z.position;
                    n++;
                }
                if (n > 0) a.AddLocation(sum / n, 64);
            }
            catch (Exception e)
            {
                ScLog.Once("baseattack-grid", e);
            }
        }

        // ------------------------------------------------------------------ spawning ----

        private static bool TrySpawn(World w, EntityPlayerLocal player, Community c, float now)
        {
            if (!AIDirector.CanSpawn() || !EntityFactory.EnemySpawnMode) return false;   // the live-zombie cap: wait for room
            GameRandom rnd = w.GetGameRandom();
            Vector3 pos;
            if (!FindSpawn(w, c, rnd, out pos))
            {
                if (++sideFails >= SideFailSeconds && !anySide)
                {
                    anySide = true;
                    ScLog.Out("base attack: no spawn spot on the " + Side(c.AttackBearing) + " side for " + SideFailSeconds + " s - they may come from any side");
                }
                else if (anySide && !looseGround && sideFails >= SideFailSeconds * 2)
                    looseGround = true;                                        // from now on (this fight) any standable block will do
                else if (looseGround && sideFails == SideFailSeconds * 6)
                    ScLog.Warn("base attack: still no spawn spot anywhere around the base (unloaded ground, or the player out there)");
                return false;
            }
            sideFails = 0;
            int gs = 0;
            try { gs = player.gameStage; } catch (Exception) { }
            string group = GroupFor(gs, rnd);
            int cls = PickClass(group, rnd);
            EntityAlive z = ScCommunity.ClassExists(cls) ? EntityFactory.CreateEntity(cls, pos) as EntityAlive : null;
            if (z == null)
            {
                if (++spawnFails >= 10)
                {
                    ScLog.Warn("base attack: nothing could be spawned from " + group + " (10 tries) - the " + (c.AttackSize - c.AttackSpawned) + " not yet come are dropped");
                    c.AttackSize = c.AttackSpawned;
                    return true;
                }
                return false;
            }
            spawnFails = 0;
            z.SetSpawnerSource(EnumSpawnerSource.Dynamic);                     // never written with the chunk
            w.SpawnEntityInWorld(z);
            EntityEnemy e = z as EntityEnemy;
            if (e != null) e.IsHordeZombie = true;                             // no despawn while a player is about
            z.bIsChunkObserver = true;                                         // its chunks stay loaded (as vanilla's hordes)
            z.IsScoutZombie = true;                                            // its approach adds an A* line (EAIApproachSpot)
            var r = new Attacker { Steer = AIWanderingHordeSpawner.RandomPos(w.aiDirector, homeCentre, 6f) };
            z.SetInvestigatePosition(r.Steer, 6000, true);
            attackers[z.entityId] = r;
            c.AttackSpawned++;
            lastProgress = w.worldTime;
            if (firstSpawnAt <= 0f) firstSpawnAt = now;
            ScLog.Verbose("base attack: spawned " + z.EntityName + " (" + c.AttackSpawned + "/" + c.AttackSize + ") from " + group + " at " + pos);
            return true;
        }

        /// <summary>A spawn spot on the attack's side (any side after SideFailSeconds), vanilla's rules in a small ring.</summary>
        private static bool FindSpawn(World w, Community c, GameRandom rnd, out Vector3 pos)
        {
            pos = Vector3.zero;
            float lo = Math.Max(1f, ScConfig.BaseAttackSpawnMin), hi = Math.Max(lo + 1f, ScConfig.BaseAttackSpawnMax);
            float spread = anySide ? 180f : Mathf.Clamp(ScConfig.BaseAttackSpread, 0f, 180f);
            for (int i = 0; i < 4; i++)
            {
                Vector3 dir = Dir(c.AttackBearing + (rnd.RandomFloat * 2f - 1f) * spread);
                Vector3 at = homeCentre + dir * (EdgeAlong(homeCentre, dir) + lo + rnd.RandomFloat * (hi - lo));
                if (w.GetMobRandomSpawnPosWithWater(at, 1, 5, Math.Max(0, ScConfig.BaseAttackMinPlayerRange), true, out pos)) return true;
                // no vanilla spot for SideFailSeconds on any side either: a base ringed by POI floors, paving or water,
                // where vanilla's terrain-only rule (CanMobsSpawnOn) finds nothing - any standable block will do
                if (looseGround && LooseSpot(w, at, out pos))
                {
                    ScLog.OnceOut("baseattack-loose", "base attack: no terrain spawn spot around the base - spawning on any standable block in the ring");
                    return true;
                }
            }
            return false;
        }

        /// <summary>A standable block near 'at' (the home's level first, then the column's top), loaded, away from every player.</summary>
        private static bool LooseSpot(World w, Vector3 at, out Vector3 pos)
        {
            pos = Vector3.zero;
            Vector3i b = World.worldToBlockPos(at);
            Chunk chunk = w.GetChunkFromWorldPos(b) as Chunk;
            if (chunk == null || w.IsWithinTraderArea(b)) return false;      // vanilla's own spots never come from a trader
            int top = chunk.GetHeight(World.toBlockXZ(b.x), World.toBlockXZ(b.z)) + 1;
            int y;
            if (!ScCommunity.FindStandable(w, b.x, b.z, Mathf.RoundToInt(homeCentre.y), out y) && !ScCommunity.FindStandable(w, b.x, b.z, top, out y)) return false;
            Vector3 p = new Vector3(b.x + 0.5f, y, b.z + 0.5f);
            if (w.isPositionInRangeOfBedrolls(p)) return false;                // ...nor from a bedroll's dead zone
            float min = Math.Max(0, ScConfig.BaseAttackMinPlayerRange);
            for (int i = 0; i < w.Players.list.Count; i++)
                if (w.Players.list[i].GetDistanceSq(p) < min * min) return false;
            pos = p;
            return true;
        }

        /// <summary>The entity group the gamestage list's stage names (weighted by each group's count), else DistressZombieGroup.</summary>
        private static string GroupFor(int gs, GameRandom rnd)
        {
            try
            {
                GameStageDefinition def;
                if (!string.IsNullOrEmpty(ScConfig.BaseAttackGameStages) && GameStageDefinition.TryGetGameStage(ScConfig.BaseAttackGameStages, out def) && def != null)
                {
                    GameStageDefinition.Stage st = def.GetStage(Math.Max(1, gs));
                    if (st != null)
                    {
                        int total = 0;
                        for (int i = 0; i < st.Count; i++)
                        {
                            GameStageDefinition.SpawnGroup g = st.GetSpawnGroup(i);
                            if (g != null && EntityGroups.list.ContainsKey(g.groupName)) total += Math.Max(1, (int)g.spawnCount);
                        }
                        if (total > 0)
                        {
                            int pick = rnd.RandomRange(0, total);
                            for (int i = 0; i < st.Count; i++)
                            {
                                GameStageDefinition.SpawnGroup g = st.GetSpawnGroup(i);
                                if (g == null || !EntityGroups.list.ContainsKey(g.groupName)) continue;
                                pick -= Math.Max(1, (int)g.spawnCount);
                                if (pick < 0) return g.groupName;
                            }
                        }
                    }
                }
                ScLog.Once("baseattack-gs", "base attack: no usable stage in the gamestage list '" + ScConfig.BaseAttackGameStages + "' for gamestage " + gs + " - using " + ScConfig.DistressZombieGroup);
            }
            catch (Exception e)
            {
                ScLog.Once("baseattack-gsx", e);
            }
            return ScConfig.DistressZombieGroup;
        }

        /// <summary>An enemy within the sandbox's tier limit from the group (vanilla's own pick), else the distress group's.</summary>
        private static int PickClass(string group, GameRandom rnd)
        {
            try
            {
                int cls = EntityGroups.GetRandomEntityFromGroupMaxTier(group, EntityFactory.MaxEntityTier, ref lastClass, true, false, rnd);
                if (ScCommunity.ClassExists(cls)) return cls;
            }
            catch (Exception e)
            {
                ScLog.Once("baseattack-pick", e);
            }
            if (group != ScConfig.DistressZombieGroup && EntityGroups.list.ContainsKey(ScConfig.DistressZombieGroup))
                return EntityGroups.GetRandomFromGroup(ScConfig.DistressZombieGroup, ref lastClass, rnd);
            return 0;
        }

        /// <summary>"list stage S: group" for the log and the console.</summary>
        public static string Mix(EntityPlayerLocal player)
        {
            int gs = 0;
            try { gs = player != null ? player.gameStage : 0; } catch (Exception) { }
            try
            {
                GameStageDefinition def;
                if (GameStageDefinition.TryGetGameStage(ScConfig.BaseAttackGameStages ?? "", out def) && def != null)
                {
                    GameStageDefinition.Stage st = def.GetStage(Math.Max(1, gs));
                    if (st != null)
                    {
                        var sb = new StringBuilder();
                        for (int i = 0; i < st.Count; i++)
                        {
                            GameStageDefinition.SpawnGroup g = st.GetSpawnGroup(i);
                            if (g == null) continue;
                            if (sb.Length > 0) sb.Append(", ");
                            sb.Append(g.groupName);
                        }
                        return ScConfig.BaseAttackGameStages + " stage " + st.stageNum + " (gamestage " + gs + "): " + sb;
                    }
                }
            }
            catch (Exception) { }
            return "gamestage " + gs + ": " + ScConfig.DistressZombieGroup + " (no stage in '" + ScConfig.BaseAttackGameStages + "')";
        }

        // ------------------------------------------------------------------ endings ----

        private static void Held(World w, EntityPlayerLocal player, Community c, bool dispersed)
        {
            int killed = c.AttackKilled;
            string ammo = AmmoText(c);
            int left = attackers.Count;
            Release(w, false);                     // a timeout's stragglers carry on as ordinary zombies
            c.DefenceTriumph = Math.Min(10f, c.DefenceTriumph + ScConfig.BaseAttackWinMorale);
            c.Attention = 0f;
            c.AttackNotBefore = w.worldTime + (ulong)(Math.Max(0f, ScConfig.BaseAttackGapDays) * TicksPerDay);
            ScLeadership.Count(DefendEvent);
            if (player != null) ScCommunity.Tell(player, string.Format(Localization.Get("scBaseHeld"), killed, ammo), "");
            ScOutposts.NoteLedger(string.Format(Localization.Get("scBaseLedgerHeld"), killed, ammo));
            ScLog.Out("base attack HELD: " + killed + " killed" + (dispersed ? ", " + left + " straggler(s) let go after " + ScConfig.BaseAttackMaxHours + " h" : "")
                      + "; " + c.AttackAmmoRounds + " round(s) resupplied from the stockpile (" + c.AttackAmmoPoints + " point(s)); next call not before " + Clock(c.AttackNotBefore));
            Clear(c);
            ScMorale.Recompute();
            ScRegistry.SaveAtomic();
        }

        private static void LostTrail(World w, Community c)
        {
            Release(w, false);
            c.AttackNotBefore = w.worldTime + (ulong)(Math.Max(0f, ScConfig.BaseAttackGapDays) * TicksPerDay);
            ScLog.Warn("base attack ended without a fight: none of " + c.AttackSize + " was killed (spawned " + c.AttackSpawned
                       + "; stalled " + ScConfig.BaseAttackMaxHours + " h, or nothing could be spawned) - no reward");
            Clear(c);
            ScRegistry.SaveAtomic();
        }

        /// <summary>The player left mid-fight: the attackers still standing go, and only they come back once the player does.</summary>
        private static void Suspend(World w, EntityPlayerLocal player, Community c)
        {
            int gone = Release(w, true);
            c.AttackSpawned = c.AttackKilled;
            c.AttackState = StateCalled;
            c.AttackArrive = 0UL;
            int left = c.AttackSize - c.AttackKilled;
            if (player != null && arrivalTold) ScCommunity.Tell(player, string.Format(Localization.Get("scBaseLeft"), left), "");
            ScLog.Out("base attack: the player left the fight (" + (int)OutsideZone(player.position) + " m out) - " + gone + " attacker(s) despawned; "
                      + left + " come back the lead time after the player returns");
            ScRegistry.SaveAtomic();
        }

        /// <summary>The console's end, a lost home, or the switch turned off: everything goes, nothing is won.</summary>
        public static void Abort(World w, string why)
        {
            Community c = ScRegistry.Community;
            int gone = Release(w, true);
            ScLog.Out("base attack ended (" + why + "): " + gone + " attacker(s) despawned");
            Clear(c);
            ScRegistry.SaveAtomic();
        }

        /// <summary>Lets go of every attacker this session tracks: despawned, or left as an ordinary zombie. Returns how many were alive.</summary>
        private static int Release(World w, bool despawn)
        {
            int alive = 0;
            if (w != null)
            {
                foreach (int id in attackers.Keys)
                {
                    EntityAlive z = w.GetEntity(id) as EntityAlive;
                    if (z == null) continue;
                    if (!z.IsDead()) alive++;
                    if (despawn && !z.IsDead()) w.RemoveEntity(id, EnumRemoveEntityReason.Despawned);
                    else Unflag(z);
                }
            }
            attackers.Clear();
            return alive;
        }

        private static void Unflag(EntityAlive z)
        {
            EntityEnemy e = z as EntityEnemy;
            if (e != null) e.IsHordeZombie = false;
            z.bIsChunkObserver = false;
            z.IsScoutZombie = false;
        }

        private static void Clear(Community c)
        {
            c.AttackState = StateNone;
            c.AttackArrive = c.AttackStarted = 0UL;
            c.AttackBearing = c.AttackSize = c.AttackSpawned = c.AttackKilled = 0;
            c.AttackAmmoRounds = c.AttackAmmoPoints = 0;
            attackers.Clear();
            arrivalTold = false;
        }

        // ------------------------------------------------------------------ ammo, the day, text ----

        /// <summary>ScAmmo: a resupply at home paid from the stockpile while an attack is live goes on the fight's bill.</summary>
        public static void NoteResupply(int rounds, int points)
        {
            if (!Live || rounds <= 0) return;
            Community c = ScRegistry.Community;
            c.AttackAmmoRounds += rounds;
            c.AttackAmmoPoints += Math.Max(0, points);
        }

        private static string AmmoText(Community c)
        {
            if (c.AttackAmmoRounds <= 0) return Localization.Get("scBaseAmmoNone");
            float bundles = c.AttackAmmoPoints / (float)Math.Max(1, ScConfig.PointsPerBundle[(int)Resource.Ammo]);
            return string.Format(Localization.Get("scBaseAmmo"), c.AttackAmmoRounds, bundles.ToString("0.#", CultureInfo.InvariantCulture));
        }

        /// <summary>Day rollover: the Defence morale part fades a point a day.</summary>
        public static void Daily(int day)
        {
            Community c = ScRegistry.Community;
            if (c.DefenceTriumph > 0f) c.DefenceTriumph = Math.Max(0f, c.DefenceTriumph - 1f);
        }

        public static string Clock(ulong t)
        {
            return "day " + GameUtils.WorldTimeToDays(t) + " " + GameUtils.WorldTimeToHours(t).ToString("00") + ":" + GameUtils.WorldTimeToMinutes(t).ToString("00");
        }

        /// <summary>sc attack / sc noise: the open attack and what the next call waits for.</summary>
        public static string StatusText(World w, EntityPlayerLocal player)
        {
            Community c = ScRegistry.Community;
            var sb = new StringBuilder();
            switch (c.AttackState)
            {
                case StateCalled:
                    sb.Append("attack: called, ").Append(c.AttackSize - c.AttackKilled).Append(" of ").Append(c.AttackSize).Append(" zombie(s) from the ")
                      .Append(Side(c.AttackBearing)).Append(", ").Append(c.AttackArrive == 0UL ? "waiting for the player to be home" : "arriving " + Clock(c.AttackArrive));
                    break;
                case StateLive:
                    sb.Append("attack: LIVE from the ").Append(Side(c.AttackBearing)).Append(" since ").Append(Clock(c.AttackStarted)).Append(" - spawned ")
                      .Append(c.AttackSpawned).Append('/').Append(c.AttackSize).Append(", killed ").Append(c.AttackKilled).Append(", in the world ").Append(attackers.Count)
                      .Append(anySide ? " (from any side)" : "").Append("; resupplied ").Append(c.AttackAmmoRounds).Append(" round(s)");
                    break;
                default:
                    sb.Append("attack: none; ");
                    string why = w != null ? WhyNoCall(w, player, false) : "no world";
                    sb.Append(why == null ? (c.Attention >= 100f ? "a call is due" : "a call comes at attention 100%") : "a call waits: " + why);
                    break;
            }
            sb.Append('\n').Append("next horde: ").Append(Size(player)).Append(" zombie(s), ").Append(Mix(player));
            if (w != null && c.AttackNotBefore > w.worldTime) sb.Append("; no call before ").Append(Clock(c.AttackNotBefore));
            return sb.ToString();
        }
    }
}
