using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// E5, the siege (E5_SPEC.md; Enclaves_Plan.md 2.10 kind 6, Q13 a, Q16 a, Q17; research missions.md 1.6, 1.7, 2.5, 2.6,
    /// 3.2): an enclave calls for help over the radio, and the player has EnclaveSiegeMinutes real minutes to get there. Kept as
    /// an EnclaveRequest of kind "siege" (accepted at once: a call, never an offer) so E4's record, Close, ledger and journal
    /// carry it. ScOutpostRisk's call / deadline / arrival / settle shape; ScBaseAttack's spawn loop, flags, resume and
    /// blood-moon window.
    ///   Pace     its own roll (Daily): EnclaveSiegeChance a day, the call at a random hour EnclaveSiegeHourMin..
    ///            EnclaveSiegeHourMax (Community.EncSiegeAt), fired from the ticker - from a known enclave that is not hostile,
    ///            within EnclaveSiegeRange m of the player, with no open request of its own. Held back (the rules below) it
    ///            waits until the window's last hour that day, then lapses.
    ///   Rules    a powered radio at home or an outpost (ScRadio.Online, as distress calls); one siege at a time; never in
    ///            ScBaseAttack's blood-moon window, nor so close before it that the call's clock would still run when it
    ///            opens (WindowAhead: the arrival never falls inside it); never while a base attack, or an outpost attack where
    ///            the player is (ScOutpostRisk.AnyLiveNear), is fought - one fought where they arrive holds the arrival with the
    ///            clock stopped - and those wait for a live siege (ScBaseAttack.WhyNoCall/TickCalled/TickAway,
    ///            ScOutpostRisk.Tick: Live).
    ///   Call     a tell, a ledger line, a journal quest whose objective counts down live (ObjectiveScEncSiege: "mm:ss to
    ///            reach &lt;enclave&gt;", then "&lt;n&gt; left") on the enclave's building, tracked. The deadline is world time:
    ///            now + minutes x 60 x TimeOfDayIncPerSec - the pause stops it, sleeping uses it up, a load keeps it; at a rate
    ///            of 0 (time frozen) it counts down a real second per unpaused second instead.
    ///   Arrival  a player within EnclaveSiegeArriveRange m of the footprint with its chunks settled for a full tick: all of
    ///            its people out at once (ScEnclavePeople.SpawnAll), then the horde, one a second while AIDirector.CanSpawn,
    ///            EnclaveSiegeSpawnMin..Max m beyond the footprint on its far side from that player (any side after
    ///            SideFailSeconds without a spot, any standable block after twice that), vanilla's spot rules, never within
    ///            EnclaveSiegeNoPopRange m in a player's view; flagged as a base attack's (ScBaseAttack.SpawnHordeZombie). Each
    ///            goes for the nearest of their people (SetAttackTarget), the building's centre when none is out. Size, fixed at
    ///            the call: (EnclaveSiegeBase + gamestage x EnclaveSiegePerGS) x people / 4, EnclaveSiegeMin..Max.
    ///   Won      every attacker dead: EnclaveGainSiege, SurvivorOrigins' rescue event (Q16 a), the quest completed, the
    ///            leader's thanks owed. A fight stalled EnclaveSiegeMaxHours with no kill in the last game hour is won too (the
    ///            stragglers let go) - held, with nothing, when none was ever killed.
    ///   Lost     (Q13 a) the deadline before anyone arrived, or every one of their people dead: the enclave falls
    ///            (ScEnclaves.Fall, cause "siege": OnFalling closes the request, AfterFall infests the building - I1, known).
    ///   Left     the player beyond twice the arrive range mid-fight: the attackers go, then a roll - the share already killed
    ///            plus EnclaveSiegeLeaveRoll a living person - held ("held": the quest completed, no standing, no event: the
    ///            player did not see it through) or lost.
    ///   Reload   attackers are never saved: EnclaveSiegeResumeSeconds after a load, with the POI settled, their people are
    ///            all brought out again (SpawnAll, as at the arrival - not E2's pace), then the ones still standing come back.
    /// Host only, main thread.
    /// </summary>
    public static class ScEncSiege
    {
        public const int StageCalled = 0, StageLive = 1;
        private const string Salt = "sc.encsiege.v1";
        private const ulong TicksPerHour = 1000UL, TicksPerDay = 24000UL;
        /// <summary>Seconds without a spawn spot on the far side before the horde may come from any side (ScBaseAttack's).</summary>
        private const int SideFailSeconds = 20;

        private sealed class Attacker
        {
            public Vector3 Steer;
        }

        private static readonly Dictionary<int, Attacker> attackers = new Dictionary<int, Attacker>();
        private static readonly List<int> scratch = new List<int>();
        private static readonly List<EntityAlive> defenders = new List<EntityAlive>();
        private static bool sessionReady, anySide, looseGround;
        /// <summary>Their people were all brought out for this session's fight (the arrival does it; a load's resume does it again).</summary>
        private static bool peopleOut;
        private static float resumeAt, settledSince = -1f, nextSave, nextTry, waitSaid;
        private static int sideFails, spawnFails;
        /// <summary>The side the horde comes from (degrees, 0 = north): the enclave's far side from the player who arrived.</summary>
        private static float bearing;
        private static ulong lastProgress;
        /// <summary>
        /// The E5 review's M1: another attack fought where the player is holds the arrival and the clock stands still - the
        /// time to answer left when the hold began, and when the hold was last seen (unscaled seconds; after a gap a new hold
        /// starts from the clock as it is then).
        /// </summary>
        private static ulong holdLeft;
        private static float holdSeen;
        /// <summary>Lose's reason, read by OnFalling inside the Fall it starts.</summary>
        private static string pendingWhy;
        /// <summary>The objective's last words and what they were made from (it asks every frame).</summary>
        private static string statusText = "";
        private static long statusKey = long.MinValue;

        private static string Loc(string key) { return Localization.Get(key); }

        public static void Reset()
        {
            attackers.Clear();
            defenders.Clear();
            sessionReady = anySide = looseGround = peopleOut = false;
            resumeAt = nextSave = nextTry = waitSaid = 0f;
            settledSince = -1f;
            sideFails = spawnFails = 0;
            bearing = 0f;
            lastProgress = holdLeft = 0UL;
            holdSeen = 0f;
            pendingWhy = null;
            statusText = "";
            statusKey = long.MinValue;
        }

        /// <summary>The registry was (re)loaded: whatever this session spawned is squared with it on the next tick (a live siege resumes).</summary>
        public static void OnRegistryChanged()
        {
            Reset();
        }

        /// <summary>The open siege (called or live), or null - at most one.</summary>
        public static EnclaveRequest Open
        {
            get
            {
                List<EnclaveRequest> all = ScRegistry.EncRequests;
                for (int i = 0; i < all.Count; i++) if (all[i].Kind == EnclaveRequest.KindSiege) return all[i];
                return null;
            }
        }

        /// <summary>A siege is being fought (live: its horde out or coming) - base and outpost attacks wait for it. A short list scan.</summary>
        public static bool Live
        {
            get
            {
                EnclaveRequest r = Open;
                return r != null && r.Stage == StageLive;
            }
        }

        private static int Rate()
        {
            try { return Math.Max(0, GameStats.GetInt(EnumGameStats.TimeOfDayIncPerSec)); }
            catch (Exception) { return 0; }
        }

        /// <summary>The time to answer a call in real seconds (EnclaveSiegeMinutes, a minute at the least).</summary>
        private static ulong CountdownSeconds()
        {
            return (ulong)Math.Max(1, Mathf.RoundToInt(Mathf.Max(1f, ScConfig.EnclaveSiegeMinutes) * 60f));
        }

        private static string Pct(float f) { return Mathf.RoundToInt(f * 100f) + "%"; }

        // ------------------------------------------------------------------ pace (the day's pass) ----

        /// <summary>
        /// ScEncRequests.Daily, on the newest day of a catch-up: the day's roll (EnclaveSiegeChance, from the world seed and the
        /// day) and, when it calls, the hour (EnclaveSiegeHourMin..Max; a window begun already: within the next game hour, while
        /// it lasts). The call is on the world's clock: a request clock ahead of the world ("sc day") rolls again for the
        /// world's today each time. Nothing while a siege is open or a call is scheduled for this day or later. ScDaily saves.
        /// </summary>
        public static void Daily(int day, EntityPlayerLocal player)
        {
            try
            {
                World w = ScCommunity.World;
                Community c = ScRegistry.Community;
                if (w == null || !ScConfig.Enclaves || ScConfig.EnclaveSiegeChance <= 0f) return;
                if (Open != null) return;
                if (c.EncSiegeAt > 0UL && GameUtils.WorldTimeToDays(c.EncSiegeAt) >= day) return;
                c.EncSiegeAt = 0UL;
                int callDay = Math.Min(day, GameUtils.WorldTimeToDays(w.worldTime));
                GameRandom rnd = GameRandomManager.Instance.CreateGameRandom(w.Seed ^ Salt.GetStableHashCode() ^ unchecked(day * 7919));
                try
                {
                    if (rnd.RandomFloat >= ScConfig.EnclaveSiegeChance) return;
                    int hMin = Mathf.Clamp(ScConfig.EnclaveSiegeHourMin, 0, 23), hMax = Mathf.Clamp(Math.Max(hMin, ScConfig.EnclaveSiegeHourMax), 0, 23);
                    int minute = hMin * 60 + rnd.RandomRange(0, Math.Max(1, (hMax - hMin) * 60));
                    ulong at = GameUtils.DayTimeToWorldTime(callDay, minute / 60, minute % 60);
                    ulong last = WindowEnd(at);
                    ulong now = w.worldTime;
                    if (at <= now)
                    {
                        if (now >= last)
                        {
                            ScLog.Out("siege: day " + day + "'s roll called one, but its hours (" + hMin + "-" + hMax + ") are over - none today");
                            return;
                        }
                        at = now + 1UL + (ulong)rnd.RandomRange(0, (int)Math.Max(1UL, Math.Min(TicksPerHour, last - now)));
                    }
                    c.EncSiegeAt = at;
                    ScLog.Out("siege: day " + day + "'s roll (" + Pct(ScConfig.EnclaveSiegeChance) + ") - a call is due " + ScBaseAttack.Clock(at));
                }
                finally
                {
                    GameRandomManager.Instance.FreeGameRandom(rnd);
                }
            }
            catch (Exception e)
            {
                ScLog.Once("encsiege-daily", e);
            }
        }

        /// <summary>The end of the window a scheduled call belongs to: that day's EnclaveSiegeHourMax (an hour after the call at the least).</summary>
        private static ulong WindowEnd(ulong at)
        {
            int day = GameUtils.WorldTimeToDays(at);
            int hMax = Mathf.Clamp(Math.Max(ScConfig.EnclaveSiegeHourMin, ScConfig.EnclaveSiegeHourMax), 0, 23);
            ulong end = GameUtils.DayTimeToWorldTime(day, hMax, 0);
            return end > at ? end : at + TicksPerHour;
        }

        /// <summary>
        /// No siege open: a scheduled call fires once its time comes and an enclave can make it (PickSource; tried every 10 s);
        /// held back past its window's end, or a clock gone back or far ahead, it lapses.
        /// </summary>
        private static void TickSchedule(World w, EntityPlayerLocal player, float now)
        {
            Community c = ScRegistry.Community;
            if (c.EncSiegeAt == 0UL) return;
            ulong t = w.worldTime, last = WindowEnd(c.EncSiegeAt);
            if (c.EncSiegeAt > t + 2 * TicksPerDay)
            {
                ScLog.Out("siege: the scheduled call (" + ScBaseAttack.Clock(c.EncSiegeAt) + ") is days ahead of the world's clock (it went back?) - dropped");
                c.EncSiegeAt = 0UL;
                ScRegistry.SaveAtomic();
                return;
            }
            if (t < c.EncSiegeAt || now < nextTry) return;
            nextTry = now + 10f;
            if (t >= last)
            {
                ScLog.Out("siege: the call due " + ScBaseAttack.Clock(c.EncSiegeAt) + " never came (" + (WhyNot(null, player, false) ?? "no enclave could make it") + ") - none today");
                c.EncSiegeAt = 0UL;
                ScRegistry.SaveAtomic();
                return;
            }
            string why;
            Enclave e = PickSource(w, player, out why);
            if (e == null)
            {
                if (now >= waitSaid) { waitSaid = now + 300f; ScLog.Out("siege: a call is due but held back (" + why + ") - it waits until " + ScBaseAttack.Clock(last)); }
                return;
            }
            c.EncSiegeAt = 0UL;
            Call(w, e, player, "the day's roll");
        }

        /// <summary>The enclave that calls: one the rules allow (WhyNot), at random. Null (and why) when there is none.</summary>
        private static Enclave PickSource(World w, EntityPlayerLocal player, out string why)
        {
            why = WhyNot(null, player, false);
            if (why != null) return null;
            var ok = new List<Enclave>();
            string last = null;
            foreach (Enclave e in ScEnclaves.All)
            {
                string no = WhyNot(e, player, false);
                if (no == null) ok.Add(e);
                else last = no;
            }
            if (ok.Count == 0) { why = "no enclave can call (" + (last ?? "none") + ")"; return null; }
            return ok[w.GetGameRandom().RandomRange(0, ok.Count)];
        }

        /// <summary>
        /// Why no siege can be called (from this enclave; any of them with null) now - null when it can. forced (the console's
        /// "force") keeps only what a siege cannot be without: one at a time, enemies on, no other attack being fought, an
        /// enclave on the map that is not hostile and has no open request.
        /// </summary>
        public static string WhyNot(Enclave e, EntityPlayerLocal player, bool forced)
        {
            World w = ScCommunity.World;
            if (w == null || player == null) return "no world or player";
            if (!ScConfig.Enclaves) return "Enclaves is off";
            if (Open != null) return "a siege is open already (one at a time)";
            if (!EntityFactory.EnemySpawnMode) return "enemy spawning is off";
            if (ScBaseAttack.Live) return "a base attack is being fought";
            // an outpost fight the player walked away from stays live in name for hours: only one where they are holds a call
            // (the E5 review's M1)
            if (ScOutpostRisk.AnyLiveNear(player.position)) return "an outpost attack is being fought where the player is";
            if (!forced)
            {
                if (ScConfig.EnclaveSiegeChance <= 0f && e == null) return "sieges are off (EnclaveSiegeChance 0)";
                if (ScBaseAttack.BloodMoonWindow(w)) return "the blood moon (no call from 10:00 on its day until it ends)";
                string bm = WindowAhead(w);
                if (bm != null) return bm;
                if (!ScRadio.Online()) return "no powered radio at home or at an outpost to hear the call";
            }
            if (e == null) return null;
            if (e.Lost) return "its building is not in this world";
            if (e.IsHostile) return "they are hostile";
            if (ScEncRequests.Of(e) != null) return "they have an open request";
            if (forced) return null;
            if (!e.Known) return "not found yet";
            float d = e.DistanceXZ(player.position.x, player.position.z);
            if (d > ScConfig.EnclaveSiegeRange) return Mathf.RoundToInt(d) + " m away (more than " + ScConfig.EnclaveSiegeRange + ")";
            return null;
        }

        /// <summary>
        /// A call now would still be counting down when ScBaseAttack's blood-moon window opens (10:00 on the blood-moon day):
        /// why (null when it would not). The window holds every arrival, and a siege cannot wait out its own clock, so the call
        /// is not made at all - its arrival never falls inside the window. Time frozen (a rate of 0): the window never comes.
        /// </summary>
        private static string WindowAhead(World w)
        {
            try
            {
                int rate = Rate();
                int bm = GameStats.GetInt(EnumGameStats.BloodMoonDay);
                if (rate <= 0 || bm <= 0) return null;
                ulong opens = GameUtils.DayTimeToWorldTime(bm, 10, 0), now = w.worldTime;
                if (now >= opens || now + CountdownSeconds() * (ulong)rate < opens) return null;
                return "the blood moon's window opens " + ScBaseAttack.Clock(opens) + ", before the " + ScConfig.EnclaveSiegeMinutes.ToString("0.#", CultureInfo.InvariantCulture)
                       + " min to answer a call would run out";
            }
            catch (Exception ex)
            {
                ScLog.Once("encsiege-bmahead", ex);
                return null;
            }
        }

        /// <summary>The horde's size: (EnclaveSiegeBase + gamestage x EnclaveSiegePerGS) x their people / 4 (the group's size before it is made), EnclaveSiegeMin..Max.</summary>
        public static int Size(Enclave e, EntityPlayer player)
        {
            int gs = 0;
            try { gs = player != null ? player.gameStage : 0; } catch (Exception) { }
            int people = ScEnclavePeople.Living(e).Count;
            if (!e.PeopleRolled) people = Math.Max(people, ScEnclavePeople.GroupSize(e));
            float n = (ScConfig.EnclaveSiegeBase + gs * ScConfig.EnclaveSiegePerGS) * Math.Max(1, people) / 4f;
            int hi = Math.Max(1, ScConfig.EnclaveSiegeMax);
            return Mathf.Clamp(Mathf.RoundToInt(n), Math.Min(hi, Math.Max(1, ScConfig.EnclaveSiegeMin)), hi);
        }

        // ------------------------------------------------------------------ the call ----

        /// <summary>
        /// The call: the record (accepted, its deadline and size), a tell, a ledger line, the journal quest (tracked: the
        /// enclave's marker on screen), the log, a save.
        /// </summary>
        private static EnclaveRequest Call(World w, Enclave e, EntityPlayerLocal player, string why)
        {
            Community c = ScRegistry.Community;
            int rate = Rate();
            ulong secs = CountdownSeconds();
            int day = ScEncRequests.Now;
            var r = new EnclaveRequest
            {
                Id = ++c.NextEncRequestId,
                EnclaveId = e.Id,
                Kind = EnclaveRequest.KindSiege,
                State = EnclaveRequest.StateAccepted,
                OfferDay = day,
                LapseDay = day,
                AcceptDay = day,
                DueDay = day,
                Stage = StageCalled,
                Deadline = w.worldTime + secs * (ulong)Math.Max(1, rate),
                SiegeSize = Size(e, player)
            };
            ScRegistry.EncRequests.Add(r);
            e.Thanks = "";
            attackers.Clear();
            sessionReady = false;
            settledSince = -1f;
            string error = null;
            Quest q = player != null ? ScQuests.StartRequest(player, e, r, ScEncRequests.WhatText(r), out error) : null;
            r.QuestCode = q != null ? q.QuestCode : 0;
            if (player != null) ScCommunity.Tell(player, string.Format(Loc("scEncSiegeCall"), e.Name), "ui_denied");
            ScOutposts.NoteLedger(string.Format(Loc("scEncSiegeLedgerCall"), e.Name));
            ScLog.Out("siege called (" + why + "): " + e.Name + " (enclave " + e.Id + ") - " + r.SiegeSize + " zombie(s), " + (secs / 60UL) + " min to come within "
                      + ScConfig.EnclaveSiegeArriveRange + " m (deadline " + ScBaseAttack.Clock(r.Deadline) + (rate <= 0 ? ", real seconds: time is frozen" : "") + "); mix "
                      + ScBaseAttack.Mix(player) + (q != null ? ", journal quest " + q.QuestCode : ", no journal quest (" + (error ?? "no player") + ")"));
            ScRegistry.SaveAtomic();
            return r;
        }

        // ------------------------------------------------------------------ the ticker ----

        /// <summary>
        /// ScEncRequests.Tick, every second (never while the game is paused: the clock stops, nothing spawns into a frozen world):
        /// a scheduled call, the countdown and the arrival, the fight.
        /// </summary>
        public static void Tick(World w, EntityPlayerLocal player)
        {
            try
            {
                if (!ScEnclaves.Ready || w == null || player == null || !ScRegistry.Loaded) return;
                if (GameManager.Instance != null && GameManager.Instance.IsPaused()) return;
                float now = Time.unscaledTime;
                EnclaveRequest r = Open;
                if (r == null)
                {
                    if (ScConfig.Enclaves) TickSchedule(w, player, now);
                    return;
                }
                Enclave e = ScEnclaves.ById(r.EnclaveId);
                if (e == null || e.Lost || !ScConfig.Enclaves)
                {
                    ScEncRequests.Close(r, ScEncRequests.Withdrawn, e == null ? "the enclave is gone" : e.Lost ? "its building is not in this world" : "Enclaves is off");
                    return;
                }
                if (r.Stage == StageLive) TickLive(w, e, r, player, now);
                else TickCalled(w, e, r, player, now);
            }
            catch (Exception ex)
            {
                ScLog.Once("encsiege-tick", ex);
            }
        }

        /// <summary>
        /// Called, not yet there: the clock (a frozen world's counts down here), lost at the deadline; the arrival - a player
        /// within EnclaveSiegeArriveRange of the footprint, its chunks settled for a full tick. Another attack being fought where
        /// they are (a base attack, an outpost fight in its range) holds the arrival, and the clock stands still while it does
        /// (the E5 review's M1).
        /// </summary>
        private static void TickCalled(World w, Enclave e, EnclaveRequest r, EntityPlayerLocal player, float now)
        {
            ulong t = w.worldTime;
            int rate = Rate();
            // the world's clock set back (settime, an older save): never more than the whole time to answer still to go
            ulong full = CountdownSeconds() * (ulong)Math.Max(1, rate);
            if (r.Deadline > t + full)
            {
                ScLog.Out("siege at " + e.Name + ": its deadline (" + ScBaseAttack.Clock(r.Deadline) + ") is further off than the whole time to answer - the world's clock went back? - now " + ScBaseAttack.Clock(t + full));
                r.Deadline = t + full;
                ScRegistry.SaveAtomic();
            }
            if (rate <= 0 && r.Deadline > t)
            {
                r.Deadline--;
                if (now >= nextSave) { nextSave = now + 10f; ScRegistry.SaveAtomic(); }
            }
            if (t >= r.Deadline)
            {
                Lose(r, e, "nobody came in time");
                return;
            }
            float d;
            EntityPlayer by = NearestPlayer(w, e, out d);
            if (by == null || d > ScConfig.EnclaveSiegeArriveRange || !ScEnclavePeople.ChunksSettled(w, e)) { settledSince = -1f; return; }
            if (settledSince < 0f) { settledSince = now; return; }
            // another attack being fought where the player is (a base attack next door, an outpost fight in its range): the
            // siege waits for it, and its clock stands still meanwhile - they came in time (the E5 review's M1). Every second
            // the deadline is put back to the time left when the hold began (the frozen clock's decrement above undone too).
            if (ScBaseAttack.Live || ScOutpostRisk.AnyLiveNear(by.position))
            {
                if (holdLeft == 0UL || now - holdSeen > 2.5f) holdLeft = r.Deadline > t ? r.Deadline - t : 1UL;
                holdSeen = now;
                r.Deadline = t + holdLeft;
                if (now >= nextSave) { nextSave = now + 10f; ScRegistry.SaveAtomic(); }
                if (now >= waitSaid)
                {
                    waitSaid = now + 60f;
                    ScLog.Out("siege at " + e.Name + ": the player is there, but another attack is being fought where they are - it waits, its clock stopped at "
                              + Mmss(SecondsLeft(r, w)) + " left");
                }
                return;
            }
            GoLive(w, e, r, by);
        }

        /// <summary>The arrival: everyone of theirs out at once, the horde's session started, the objective turned to the count left.</summary>
        private static void GoLive(World w, Enclave e, EnclaveRequest r, EntityPlayer by)
        {
            int made = ScEnclavePeople.SpawnAll(e);
            if (!ScEncRequests.All.Contains(r)) return;      // making the group found it all dead: the enclave fell
            r.Stage = StageLive;
            r.SiegeStarted = w.worldTime;
            r.SiegeSpawned = r.SiegeKilled;
            if (r.SiegeSize <= 0) r.SiegeSize = Size(e, by);
            StartSession(w, e, by, 0f);
            peopleOut = true;
            if (by != null) ScCommunity.Tell(by, string.Format(Loc("scEncSiegeArrived"), e.Name, Math.Max(0, r.SiegeSize - r.SiegeKilled)), "ui_denied");
            ScEncRequests.Reshape(r);
            ScLog.Out("siege at " + e.Name + " is live: " + (r.SiegeSize - r.SiegeKilled) + " zombie(s) from the " + ScBaseAttack.Side(Mathf.RoundToInt(bearing))
                      + "; " + Math.Max(0, made) + " of their people built for it, " + ScEnclavePeople.Living(e).Count + " alive");
            ScRegistry.SaveAtomic();
        }

        private static void StartSession(World w, Enclave e, EntityPlayer by, float graceUntil)
        {
            sessionReady = true;
            resumeAt = graceUntil;
            anySide = looseGround = peopleOut = false;
            sideFails = spawnFails = 0;
            lastProgress = w.worldTime;
            // the far side of the building from the player who came: the horde is in front of them, over the walls
            Vector3 c = e.Centre;
            if (by != null)
            {
                float dx = c.x - by.position.x, dz = c.z - by.position.z;
                bearing = dx * dx + dz * dz > 1f ? Mathf.Atan2(dx, dz) * Mathf.Rad2Deg : w.GetGameRandom().RandomRange(0, 360);
            }
            else bearing = w.GetGameRandom().RandomRange(0, 360);
        }

        /// <summary>
        /// The fight, every second: a load's resume; the player leaving (beyond twice the arrive range: Leave); the dead counted
        /// and the rest steered; one more spawned; every attacker dead (Win); a stalled fight over.
        /// </summary>
        private static void TickLive(World w, Enclave e, EnclaveRequest r, EntityPlayerLocal player, float now)
        {
            float d;
            EntityPlayer by = NearestPlayer(w, e, out d);
            if (!sessionReady)
            {
                // loaded mid-fight: the attackers were never saved (Dynamic) - only the ones still standing come back
                attackers.Clear();
                r.SiegeSpawned = r.SiegeKilled;
                StartSession(w, e, by ?? player, now + Math.Max(0f, ScConfig.EnclaveSiegeResumeSeconds));
                ScLog.Out("siege at " + e.Name + " resumes after a load: " + (r.SiegeSize - r.SiegeKilled) + " zombie(s) still standing come back");
                ScRegistry.SaveAtomic();
            }
            if (by == null || d > ScConfig.EnclaveSiegeArriveRange * 2f)
            {
                Leave(w, e, r, player, d);
                return;
            }
            // after a load: their people all out again (at once, as at the arrival) before the horde comes back
            if (!peopleOut && now >= resumeAt && ScEnclavePeople.ChunksSettled(w, e))
            {
                peopleOut = true;
                int made = ScEnclavePeople.SpawnAll(e);
                if (!ScEncRequests.All.Contains(r)) return;      // making the group found it all dead: the enclave fell
                if (made > 0) ScLog.Out("siege at " + e.Name + ": " + made + " of their people back out for the fight");
            }
            bool changed = Watch(w, e, r);
            if (r.SiegeSpawned < r.SiegeSize && peopleOut && now >= resumeAt && TrySpawn(w, e, r, by, now)) changed = true;
            if (r.SiegeSpawned >= r.SiegeSize && attackers.Count == 0)
            {
                if (r.SiegeKilled > 0) Win(w, e, r, player, false);
                else HeldOut(w, e, r, "the horde never came (nothing could be spawned)");
                return;
            }
            ulong t = w.worldTime;
            if (r.SiegeStarted > t) r.SiegeStarted = t;            // the clock went back
            if (lastProgress > t) lastProgress = t;
            if (t >= r.SiegeStarted + (ulong)(Math.Max(0.5f, ScConfig.EnclaveSiegeMaxHours) * TicksPerHour) && t >= lastProgress + TicksPerHour)
            {
                if (r.SiegeKilled > 0) Win(w, e, r, player, true);
                else HeldOut(w, e, r, "the fight stalled with none killed");
                return;
            }
            KeepGrid(w, e, r);
            if (changed && now >= nextSave) { nextSave = now + 10f; ScRegistry.SaveAtomic(); }
        }

        /// <summary>Counts the dead and keeps the rest on their people. True when a count changed.</summary>
        private static bool Watch(World w, Enclave e, EnclaveRequest r)
        {
            bool changed = false;
            Defenders(e);
            scratch.Clear();
            scratch.AddRange(attackers.Keys);
            foreach (int id in scratch)
            {
                Attacker a = attackers[id];
                EntityAlive z = w.GetEntity(id) as EntityAlive;
                if (z == null || z.IsDead())
                {
                    // dead, or gone from the world (nothing despawns a horde zombie while a player is about): one fewer
                    attackers.Remove(id);
                    r.SiegeKilled = Math.Min(r.SiegeSize, r.SiegeKilled + 1);
                    lastProgress = w.worldTime;
                    if (z != null) ScBaseAttack.Unflag(z);
                    changed = true;
                    continue;
                }
                try
                {
                    Steer(z, a);
                }
                catch (Exception ex)
                {
                    ScLog.Once("encsiege-steer", ex);
                }
            }
            defenders.Clear();
            return changed;
        }

        /// <summary>Their people's bodies in the world now (the attackers' targets), into 'defenders'.</summary>
        private static void Defenders(Enclave e)
        {
            defenders.Clear();
            foreach (EncPerson p in ScRegistry.EncPeople)
            {
                if (p.EnclaveId != e.Id || p.State != EncPerson.StateHome || p.EntityId < 0) continue;
                EntityAlive npc = ScEnclavePeople.OwnBody(p);
                if (npc != null && !npc.IsDead()) defenders.Add(npc);
            }
        }

        private static EntityAlive NearestDefender(Vector3 from)
        {
            EntityAlive best = null;
            float bestD = float.MaxValue;
            for (int i = 0; i < defenders.Count; i++)
            {
                float d = (defenders[i].position - from).sqrMagnitude;
                if (d < bestD) { bestD = d; best = defenders[i]; }
            }
            return best;
        }

        /// <summary>
        /// An attacker in a fight of its own is left to vanilla's behaviour; one with none goes for the nearest of their people
        /// (an investigate position would hide every non-player from it), the building's centre when none of them is out.
        /// </summary>
        private static void Steer(EntityAlive z, Attacker a)
        {
            EntityAlive t = z.GetAttackTarget();
            if (t == null || t.IsDead()) t = z.GetRevengeTarget();
            if (t != null && !t.IsDead()) return;
            EntityAlive body = NearestDefender(z.position);
            if (body != null) { z.SetAttackTarget(body, 1200); return; }
            if (!z.HasInvestigatePosition) z.SetInvestigatePosition(a.Steer, 6000, true);
        }

        /// <summary>
        /// One attacker, ScBaseAttack's way: only while vanilla's live-zombie cap has room (they wait, never drop), a spot on the
        /// far side (FindSpawn), the base attack's gamestage list and flags (GroupFor, PickClass, SpawnHordeZombie), straight at
        /// the nearest of their people.
        /// </summary>
        private static bool TrySpawn(World w, Enclave e, EnclaveRequest r, EntityPlayer by, float now)
        {
            if (!AIDirector.CanSpawn() || !EntityFactory.EnemySpawnMode) return false;
            GameRandom rnd = w.GetGameRandom();
            Vector3 pos;
            if (!FindSpawn(w, e, rnd, out pos))
            {
                if (++sideFails >= SideFailSeconds && !anySide)
                {
                    anySide = true;
                    ScLog.Out("siege at " + e.Name + ": no spawn spot on the " + ScBaseAttack.Side(Mathf.RoundToInt(bearing)) + " side for " + SideFailSeconds + " s - they may come from any side");
                }
                else if (anySide && !looseGround && sideFails >= SideFailSeconds * 2) looseGround = true;
                else if (looseGround && sideFails == SideFailSeconds * 6) ScLog.Warn("siege at " + e.Name + ": still no spawn spot anywhere around it (unloaded ground, or players out there)");
                return false;
            }
            sideFails = 0;
            int gs = 0;
            try { gs = by != null ? by.gameStage : 0; } catch (Exception) { }
            string group = ScBaseAttack.GroupFor(gs, rnd);
            EntityAlive z = ScBaseAttack.SpawnHordeZombie(w, ScBaseAttack.PickClass(group, rnd), pos);
            if (z == null)
            {
                if (++spawnFails >= 10)
                {
                    ScLog.Warn("siege at " + e.Name + ": nothing could be spawned from " + group + " (10 tries) - the " + (r.SiegeSize - r.SiegeSpawned) + " not yet come are dropped");
                    r.SiegeSize = r.SiegeSpawned;
                    return true;
                }
                return false;
            }
            spawnFails = 0;
            Vector3 c = e.Centre;
            c.y = w.GetHeightAt(c.x, c.z) + 1f;
            var a = new Attacker { Steer = AIWanderingHordeSpawner.RandomPos(w.aiDirector, c, 6f) };
            Defenders(e);
            EntityAlive body = NearestDefender(z.position);
            defenders.Clear();
            if (body != null) z.SetAttackTarget(body, 1200);
            else z.SetInvestigatePosition(a.Steer, 6000, true);
            attackers[z.entityId] = a;
            r.SiegeSpawned++;
            lastProgress = w.worldTime;
            ScLog.Verbose("siege at " + e.Name + ": spawned " + z.EntityName + " (" + r.SiegeSpawned + "/" + r.SiegeSize + ") from " + group + " at " + pos);
            return true;
        }

        /// <summary>
        /// A spot EnclaveSiegeSpawnMin..Max m beyond the footprint within EnclaveSiegeSpread degrees of the far side (any side,
        /// then any standable block, after SideFailSeconds and twice that without one), on vanilla's spot rules
        /// (GetMobRandomSpawnPosWithWater: a loaded chunk, terrain mobs may spawn on, EnclaveSiegeMinPlayerRange from every
        /// player, clear of bedrolls), never within EnclaveSiegeNoPopRange m in a player's view.
        /// </summary>
        private static bool FindSpawn(World w, Enclave e, GameRandom rnd, out Vector3 pos)
        {
            pos = Vector3.zero;
            float lo = Math.Max(1f, ScConfig.EnclaveSiegeSpawnMin), hi = Math.Max(lo + 1f, ScConfig.EnclaveSiegeSpawnMax);
            float spread = anySide ? 180f : Mathf.Clamp(ScConfig.EnclaveSiegeSpread, 0f, 180f);
            Vector3 c = e.Centre;
            for (int i = 0; i < 4; i++)
            {
                Vector3 dir = Dir(bearing + (rnd.RandomFloat * 2f - 1f) * spread);
                Vector3 at = c + dir * (EdgeAlong(e, dir) + lo + rnd.RandomFloat * (hi - lo));
                Vector3 p;
                if (w.GetMobRandomSpawnPosWithWater(at, 1, 5, Math.Max(0, ScConfig.EnclaveSiegeMinPlayerRange), true, out p) && !InView(w, p)) { pos = p; return true; }
                if (looseGround && ScBaseAttack.LooseSpot(w, at, w.GetHeightAt(at.x, at.z) + 1f, ScConfig.EnclaveSiegeMinPlayerRange, out p) && !InView(w, p))
                {
                    ScLog.OnceOut("encsiege-loose", "siege: no terrain spawn spot round an enclave - spawning on any standable block in the ring");
                    pos = p;
                    return true;
                }
            }
            return false;
        }

        private static Vector3 Dir(float bearingDeg)
        {
            float a = bearingDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));     // 0 = north (+z), 90 = east (+x)
        }

        /// <summary>How far from the footprint's centre its edge is along 'dir' (a rectangle).</summary>
        private static float EdgeAlong(Enclave e, Vector3 dir)
        {
            float hx = Math.Max(1, e.Size.x) * 0.5f, hz = Math.Max(1, e.Size.z) * 0.5f;
            float tx = Mathf.Abs(dir.x) > 1e-4f ? hx / Mathf.Abs(dir.x) : float.MaxValue;
            float tz = Mathf.Abs(dir.z) > 1e-4f ? hz / Mathf.Abs(dir.z) : float.MaxValue;
            return Mathf.Min(tx, tz);
        }

        /// <summary>Within EnclaveSiegeNoPopRange m of a player and inside their view cone (vanilla's pop-in rule, the siege's range).</summary>
        private static bool InView(World w, Vector3 at)
        {
            float r = ScConfig.EnclaveSiegeNoPopRange;
            List<EntityPlayer> list = w.Players != null ? w.Players.list : null;
            if (r <= 0f || list == null) return false;
            for (int i = 0; i < list.Count; i++)
            {
                EntityPlayer p = list[i];
                if (p != null && !p.IsDead() && ScRunners.XZ(at, p.position) < r && p.IsInViewCone(at)) return true;
            }
            return false;
        }

        /// <summary>The nearest player to the footprint and their XZ distance (0 inside it); null with none.</summary>
        private static EntityPlayer NearestPlayer(World w, Enclave e, out float dist)
        {
            dist = float.MaxValue;
            EntityPlayer best = null;
            List<EntityPlayer> list = w.Players != null ? w.Players.list : null;
            if (list == null) return null;
            for (int i = 0; i < list.Count; i++)
            {
                EntityPlayer p = list[i];
                if (p == null) continue;
                float d = e.DistanceXZ(p.position.x, p.position.z);
                if (d < dist) { dist = d; best = p; }
            }
            return best;
        }

        /// <summary>Vanilla's A* grid round the fight (AIWanderingHordeSpawner's pattern, as the base attack keeps it).</summary>
        private static void KeepGrid(World w, Enclave e, EnclaveRequest r)
        {
            try
            {
                AstarManager a = AstarManager.Instance;
                if (a == null) return;
                Vector3 c = e.Centre;
                c.y = w.GetHeightAt(c.x, c.z);
                if (r.SiegeSpawned < r.SiegeSize)
                {
                    Vector3 dir = Dir(bearing);
                    float mid = EdgeAlong(e, dir) + 0.5f * (ScConfig.EnclaveSiegeSpawnMin + ScConfig.EnclaveSiegeSpawnMax);
                    a.AddLocationLine(c + dir * mid, c, 64);
                    return;
                }
                a.AddLocation(c, 64);
            }
            catch (Exception ex)
            {
                ScLog.Once("encsiege-grid", ex);
            }
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
                    else ScBaseAttack.Unflag(z);
                }
            }
            attackers.Clear();
            return alive;
        }

        // ------------------------------------------------------------------ endings ----

        /// <summary>
        /// Won: every attacker dead (dispersed: a stalled fight's stragglers let go as ordinary zombies) - EnclaveGainSiege, the
        /// rescue event (Q16 a), the quest completed, the leader's thanks owed for the next talk, a tell.
        /// </summary>
        private static void Win(World w, Enclave e, EnclaveRequest r, EntityPlayer player, bool dispersed)
        {
            int left = Release(w, false);
            int killed = r.SiegeKilled;
            ScLog.Out("siege at " + e.Name + " WON: " + killed + " killed" + (dispersed ? ", " + left + " straggler(s) let go after " + ScConfig.EnclaveSiegeMaxHours + " h" : ""));
            ScEncRequests.Close(r, ScEncRequests.Done, dispersed ? "the rest of the horde scattered" : "every attacker is dead", ScEncRequests.GainFor(r), null, ScEncRequests.EventFor(r));
            e.Thanks = EnclaveRequest.KindSiege;
            if (player != null) ScCommunity.Tell(player, string.Format(Loc("scEncSiegeWonTell"), e.Name, killed), "");
            ScRegistry.SaveAtomic();
        }

        /// <summary>Held without the player seeing it through (left and the roll held; nothing came; a stall with none killed): the quest completed, no standing, no event.</summary>
        private static void HeldOut(World w, Enclave e, EnclaveRequest r, string why)
        {
            int left = Release(w, false);
            ScLog.Out("siege at " + e.Name + " held: " + why + (left > 0 ? " (" + left + " let go)" : ""));
            ScEncRequests.Close(r, ScEncRequests.HeldOut, why);
        }

        /// <summary>
        /// The player left a live siege (beyond twice the arrive range): the attackers still standing go, then a roll - the share
        /// of the horde killed plus EnclaveSiegeLeaveRoll for each of their people alive - held (HeldOut) or lost (Lose).
        /// </summary>
        private static void Leave(World w, Enclave e, EnclaveRequest r, EntityPlayerLocal player, float d)
        {
            int gone = Release(w, true);
            int living = ScEnclavePeople.Living(e).Count;
            float share = r.SiegeSize > 0 ? Mathf.Clamp01(r.SiegeKilled / (float)r.SiegeSize) : 1f;
            float chance = Mathf.Clamp01(share + Math.Max(0f, ScConfig.EnclaveSiegeLeaveRoll) * living);
            bool held = w.GetGameRandom().RandomFloat < chance;
            ScLog.Out("siege at " + e.Name + ": the player left the fight (" + (d < float.MaxValue ? Mathf.RoundToInt(d) + " m out" : "no player near") + ") - " + gone
                      + " attacker(s) despawned; " + r.SiegeKilled + " of " + r.SiegeSize + " killed, " + living + " of theirs alive: " + Pct(chance) + " to hold - "
                      + (held ? "they held" : "they fell"));
            if (held)
            {
                if (player != null) ScCommunity.Tell(player, string.Format(Loc("scEncSiegeHeldTell"), e.Name), "");
                HeldOut(w, e, r, "the player left; they held out (" + Pct(chance) + ")");
                return;
            }
            Lose(r, e, "the player left the fight and they could not hold (" + Pct(chance) + ")");
        }

        /// <summary>
        /// Lost (Q13 a): the enclave falls - ScEnclaves.Fall(e, "siege"), whose OnFalling closes the siege as lost (with this
        /// reason) and whose AfterFall infests the building.
        /// </summary>
        private static void Lose(EnclaveRequest r, Enclave e, string why)
        {
            ScLog.Out("siege at " + e.Name + " LOST: " + why);
            pendingWhy = why;
            try { ScEnclaves.Fall(e, "siege"); }
            finally { pendingWhy = null; }
            if (ScEncRequests.All.Contains(r)) ScEncRequests.Close(r, ScEncRequests.Lost, why);     // a fall that did not happen
        }

        /// <summary>
        /// ScEnclaves.Fall, before anything goes: a siege still open there is lost with them (closed as "lost": no standing, the
        /// quest removed as failed, a ledger line; its attackers let go as ordinary zombies). True when it was - the building is
        /// infested after the fall (AfterFall).
        /// </summary>
        internal static bool OnFalling(Enclave e, string cause)
        {
            try
            {
                EnclaveRequest r = ScEncRequests.Of(e);
                if (r == null || r.Kind != EnclaveRequest.KindSiege) return false;
                ScEncRequests.Close(r, ScEncRequests.Lost, pendingWhy ?? "the enclave fell (" + cause + ")");
                return true;
            }
            catch (Exception ex)
            {
                ScLog.Once("encsiege-falling", ex);
                return false;
            }
        }

        /// <summary>ScEnclaves.Fall, after its record left the lookups: a building lost to a siege is infested (I1, known, its marker).</summary>
        internal static void AfterFall(Enclave e)
        {
            try
            {
                string why;
                Infestation inf = ScInfestations.InfestFallen(e, ScCommunity.LocalPlayer, out why);
                if (inf == null) ScLog.Out("siege: " + e.Name + "'s building (" + e.PoiName + ") is not infested - " + why);
            }
            catch (Exception ex)
            {
                ScLog.Once("encsiege-afterfall", ex);
            }
        }

        /// <summary>A siege ended (ScEncRequests.Close, after the record left the list): its attackers go (withdrawn) or are let go (any other end).</summary>
        internal static void OnClosed(EnclaveRequest r, string outcome)
        {
            int n = Release(ScCommunity.World, outcome == ScEncRequests.Withdrawn);
            sessionReady = false;
            holdLeft = 0UL;
            settledSince = -1f;
            if (n > 0) ScLog.Out("siege #" + r.Id + " " + outcome + ": " + n + " attacker(s) " + (outcome == ScEncRequests.Withdrawn ? "despawned" : "let go as ordinary zombies"));
        }

        // ------------------------------------------------------------------ texts ----

        private static int SecondsLeft(EnclaveRequest r, World w)
        {
            if (w == null) return 0;
            ulong now = w.worldTime;
            if (r.Deadline <= now) return 0;
            return (int)Math.Min(int.MaxValue, (r.Deadline - now) / (ulong)Math.Max(1, Rate()));
        }

        private static string Mmss(int secs)
        {
            secs = Math.Max(0, secs);
            return (secs / 60).ToString("00", CultureInfo.InvariantCulture) + ":" + (secs % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// The live countdown's words (ObjectiveScEncSiege asks every frame): "12:34 to reach Harlan's Rest" while called, "8 left"
        /// once it is live; "" when that siege is not open. Made again only when a number changes. Host only.
        /// </summary>
        public static string StatusFor(int requestId)
        {
            EnclaveRequest r = Open;
            if (r == null || r.Id != requestId || !ScCommunity.IsHost) return "";
            long key;
            if (r.Stage == StageLive)
            {
                int left = Math.Max(0, r.SiegeSize - r.SiegeKilled);
                key = ((long)r.Id << 32) | (1L << 31) | (uint)left;
                if (key != statusKey) { statusKey = key; statusText = string.Format(Loc("scObjectiveEncSiegeLeft"), left); }
                return statusText;
            }
            int secs = SecondsLeft(r, ScCommunity.World);
            key = ((long)r.Id << 32) | (uint)secs;
            if (key != statusKey)
            {
                statusKey = key;
                Enclave e = ScEnclaves.ById(r.EnclaveId);
                statusText = string.Format(Loc("scObjectiveEncSiegeClock"), Mmss(secs), e != null ? e.Name : "");
            }
            return statusText;
        }

        /// <summary>"siege: 12 zombie(s), called" - the log's and the console's form.</summary>
        public static string Describe(EnclaveRequest r)
        {
            return "siege: " + r.SiegeSize + " zombie(s), " + (r.Stage == StageLive ? "live (" + r.SiegeSpawned + " came, " + r.SiegeKilled + " killed, " + attackers.Count + " in the world)" : "called");
        }

        /// <summary>The console's clock: the time left to get there, or the fight's count.</summary>
        public static string ClockText(EnclaveRequest r)
        {
            World w = ScCommunity.World;
            if (r.Stage == StageLive) return "live since " + ScBaseAttack.Clock(r.SiegeStarted) + ", " + Math.Max(0, r.SiegeSize - r.SiegeKilled) + " left";
            int rate = Rate();
            return Mmss(SecondsLeft(r, w)) + " left to get there (deadline " + ScBaseAttack.Clock(r.Deadline) + (rate <= 0 ? ", real seconds: time is frozen" : "") + ")";
        }

        /// <summary>sc enclave requests: the siege's pace and what is scheduled or open.</summary>
        public static string ScheduleLine()
        {
            var sb = new StringBuilder();
            sb.Append("sieges: ").Append(Pct(ScConfig.EnclaveSiegeChance)).Append(" a day, called ").Append(ScConfig.EnclaveSiegeHourMin).Append("-").Append(ScConfig.EnclaveSiegeHourMax)
              .Append("h within ").Append(ScConfig.EnclaveSiegeRange).Append(" m, ").Append(ScConfig.EnclaveSiegeMinutes.ToString("0.#", CultureInfo.InvariantCulture)).Append(" min to arrive; ");
            EnclaveRequest r = Open;
            ulong at = ScRegistry.Community.EncSiegeAt;
            if (r != null)
            {
                Enclave e = ScEnclaves.ById(r.EnclaveId);
                sb.Append("open: #").Append(r.Id).Append(" at ").Append(e != null ? e.Name : "enclave " + r.EnclaveId).Append(" - ").Append(ClockText(r));
            }
            else if (at > 0UL) sb.Append("a call is due ").Append(ScBaseAttack.Clock(at)).Append(" (its window ends ").Append(ScBaseAttack.Clock(WindowEnd(at))).Append(")");
            else sb.Append("none scheduled");
            string why = WhyNot(null, ScCommunity.LocalPlayer, false);
            if (r == null && why != null) sb.Append("; a call now would wait: ").Append(why);
            return sb.ToString();
        }

        /// <summary>The leader's words while their siege is open (the wait statement).</summary>
        internal static string WaitInfo(EnclaveRequest r)
        {
            if (r.Stage == StageLive) return string.Format(Loc("scEncReqWaitInfoSiege"), Math.Max(0, r.SiegeSize - r.SiegeKilled));
            return Loc("scEncReqWaitInfoSiegeCalled");
        }

        // ------------------------------------------------------------------ console ----

        /// <summary>
        /// sc enclave siege &lt;id&gt; [now [force] | win | lose | clear]: the siege's state; a call now (the pace ignored, the rules
        /// kept unless "force": the radio, the range, discovery and the blood moon); the open one won, lost (the enclave falls,
        /// its building infested) or withdrawn (its attackers despawned, no standing).
        /// </summary>
        public static void Command(Enclave e, List<string> p, List<string> into)
        {
            string verb = p.Count > 3 ? p[3].ToLowerInvariant() : "";
            bool force = false;
            for (int i = 4; i < p.Count; i++) if (p[i].Equals("force", StringComparison.OrdinalIgnoreCase)) force = true;
            EnclaveRequest r = Open;
            World w = ScCommunity.World;
            EntityPlayerLocal pl = ScCommunity.LocalPlayer;
            if (w == null || pl == null || !ScCommunity.IsHost) { into.Add("host only, with a world and a player"); return; }
            switch (verb)
            {
                case "":
                    into.Add(r != null ? "#" + r.Id + " " + Describe(r) + " - " + ClockText(r) : "no siege open");
                    into.Add("  " + ScheduleLine());
                    into.Add("  " + e.Name + ": " + (WhyNot(e, pl, false) ?? "can call one") + "; its horde would be " + Size(e, pl) + " (mix " + ScBaseAttack.Mix(pl) + ")");
                    return;
                case "now":
                {
                    if (r != null) { into.Add("a siege is open already (#" + r.Id + "): sc enclave siege " + r.EnclaveId + " clear"); return; }
                    string why = WhyNot(e, pl, force);
                    if (why != null) { into.Add(e.Name + " calls no siege: " + why + (force ? "" : " (force skips the radio, the range, discovery and the blood moon)")); return; }
                    ScRegistry.Community.EncSiegeAt = 0UL;
                    r = Call(w, e, pl, "the console");
                    into.Add("called: #" + r.Id + " " + Describe(r) + " - " + ClockText(r));
                    return;
                }
            }
            if (r == null || r.EnclaveId != e.Id) { into.Add(e.Name + " has no siege open" + (r != null ? " (#" + r.Id + " is enclave " + r.EnclaveId + "'s)" : "")); return; }
            switch (verb)
            {
                case "win":
                    if (r.SiegeKilled <= 0) r.SiegeKilled = Math.Max(1, r.SiegeSize - attackers.Count);
                    Release(w, true);
                    Win(w, e, r, pl, false);
                    into.Add("#" + r.Id + " won - " + e.Name + " " + e.Standing + " (" + ScEnclaves.StandingWord(e) + ")");
                    return;
                case "lose":
                    Lose(r, e, "the console");
                    into.Add("#" + r.Id + " lost - " + e.Name + (ScEnclaves.All.Contains(e) ? " still stands (?)" : " fell"));
                    return;
                case "clear":
                    ScEncRequests.Close(r, ScEncRequests.Withdrawn, "the console");
                    into.Add("#" + r.Id + " withdrawn: its attackers despawned, no standing change");
                    return;
                default:
                    into.Add("sc enclave siege <id> [now [force] | win | lose | clear]");
                    return;
            }
        }
    }
}
