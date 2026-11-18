#if SC_V34
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round n06: SurvivorCommunity's people use 0-XNPCCore 3.4.05's resident routine to SLEEP AT NIGHT - and only to
    /// sleep. The day stays SC's (the user's decision, 2026-11-14): no station work, no station rotation, no strolling.
    ///
    /// How 0-XNPCCore's side works, and why this is a lease rather than a handover:
    ///  - Everything in its routine - beds, going home at dusk, waking, station work - hangs off ONE entity cvar,
    ///    SdcsResident, plus six more holding the POI box (SdcsResidentPoiMin/Size X,Y,Z). SdcsRoutine.Tick returns at
    ///    once while SdcsResident is below 0.5, so SC simply writing those seven turns the whole thing on for one body.
    ///  - Its own SdcsResident.Mark refuses anyone with Leader or Owner ("she is hired"), and SdcsResident.Tick CLEARS
    ///    residency the moment either appears. SC writes Owner for the length of every work shift and every conversation
    ///    (ScWork), so eligibility below tests those two cvars directly rather than fighting that: a member in a talk or
    ///    on a shift is simply not leased, and the lease is taken again once she is free.
    ///  - She can only lie down while RoutineWalk is the running action (SleepRules.MayBegin returns holdingAtBed), and
    ///    RoutineWalk ships at weight 1.5 - under Guard and Stay (3.0), Chat and DynamicFollow (2.0) and all three of
    ///    SC's night actions, so it never runs for one of SC's people. Config/utilityai.xml therefore raises it to
    ///    3.8: above ScNightIdle (3.6), ScRoam (3.4), ScAtPost (3.2), Guard and Stay, and below ScDefendBase/Hold (5.2),
    ///    ScTravel (4.5), ScReturn (5.5) and ScFlee (6.0). A fight, a flee and the walk home all still outrank sleeping.
    ///    That raise is safe for every other NPC because RoutineWalk is already gated on the .SdcsRoutineWalk cvar, which
    ///    only SdcsRoutine.Update writes - it scores 0 for anyone the routine is not already driving, whatever its
    ///    weight. One real consequence for 0-XNPCCore's OWN POI residents: the raise also puts RoutineWalk above its
    ///    Guard and Stay (3.0), so a standing order no longer interrupts one of ITS residents mid-routine. A fight
    ///    (SdcsEnemyNotNear 30 m) and a chat (NoActiveChat) still do.
    ///  - RoutineWalk also carries SdcsEnemyNotNear at 30 m, so 0-XNPCCore stands the routine down for an enemy further
    ///    out than SC's own base defence reaches (22 m). SC does not need its own intruder test.
    ///
    /// No bed, no lease (the user's decision): without a bed claim SleepRules falls back to bedding down wherever she is
    /// standing at dusk - in the open, mid-base, or on a post she was just relieved from. SC refuses the lease unless the
    /// zone really holds one of 0-XNPCCore's bed blocks, so that fallback can never fire.
    ///
    /// Wild SC survivors spawned into a POI by a sleeper volume are NOT handled here and need nothing: 0-XNPCCore's own
    /// SdcsResidentSpawnPatch already marks them, which the user chose to keep.
    /// </summary>
    public static class ScSleep
    {
        private const string ResidentCvar = "SdcsResident";
        private static readonly string[] BoxCvars =
        {
            "SdcsResidentPoiMinX", "SdcsResidentPoiMinY", "SdcsResidentPoiMinZ",
            "SdcsResidentPoiSizeX", "SdcsResidentPoiSizeY", "SdcsResidentPoiSizeZ"
        };

        /// <summary>Bodies SC currently holds a lease on, so it only ever clears its own.</summary>
        private static readonly HashSet<int> leased = new HashSet<int>();

        /// <summary>Counters for "sc sleep".</summary>
        public static int Opened, Closed, Refused;
        public static string LastRefusal = "";

        public static void Forget(int entityId) { leased.Remove(entityId); dawnSince.Remove(entityId); }

        /// <summary>When each leased body was first seen still in 0-XNPCCore's sleep phase after daybreak (see Apply).</summary>
        private static readonly Dictionary<int, float> dawnSince = new Dictionary<int, float>();
        /// <summary>The longest SC waits at daybreak for 0-XNPCCore to wake her itself before it closes the lease anyway (s).</summary>
        private const float DawnHoldSeconds = 30f;

        /// <summary>
        /// Is she actually lying down asleep right now? 0-XNPCCore writes these two cvars for exactly that - SdcsSleepBed
        /// while she is on a bed and SdcsSleepLoop while the sleep animation runs - and it tests them itself, so no
        /// reflection and no proxy is needed.
        /// Deliberately NOT the routine phase: the phases are 1 work, 2 home, 3 sleep, 4 WAKE, so a "&gt;= 3" style test
        /// stays true right through the post-dawn wake window and would keep her silent all morning, and it is also true
        /// while she is still walking to bed, so she would not greet the player on the way.
        /// Used to keep her quiet - without it she chatters, greets and taunts from her bed. It reads 0-XNPCCore's own
        /// cvars, not SC's lease set, so it also holds for a survivor 0-XNPCCore put to bed itself (a POI sleeper).
        /// </summary>
        public static bool IsAsleep(EntityAlive npc)
        {
            if (npc == null || npc.Buffs == null) return false;
            try { return npc.GetCVar("SdcsSleepBed") >= 0.5f || npc.GetCVar("SdcsSleepLoop") >= 0.5f; }
            catch (Exception) { return false; }
        }

        /// <summary>Records why a lease was refused, for "sc sleep". Not called from the console command itself.</summary>
        private static void Note(string why) { Refused++; LastRefusal = why; }

        /// <summary>
        /// Is it the part of the night 0-XNPCCore would actually be SLEEPING in? Not simply "dark":
        ///  - its own phase only reaches 3 (sleep) SleepAfterDuskHours after dusk; the hour before that is phase 2, the
        ///    STROLL phase, and the user asked for no strolling. Opening the lease at dusk would also displace SC's own
        ///    night behavior for that hour, since RoutineWalk at 3.8 outscores it.
        ///  - on a blood moon night its phase stays 2 all night, so she would walk to her bed, never lie down, and
        ///    abandon her post on the one night the base matters most.
        /// SleepAfterDuskHours mirrors 0-XNPCCore's Routine/SleepAfterDuskHours (its default is 1); change SC's to match
        /// if that setting is changed there.
        /// </summary>
        public const string WhyDaytime = "daytime";

        private static string SleepWhy(World w)
        {
            if (!ScConfig.SleepAtNight) return "SleepAtNight is off";
            if (!RoutineOn()) return "0-XNPCCore's own Routine is switched off in its Settings.xml";
            if (w.IsDaytime()) return WhyDaytime;
            if (ScBaseAttack.BloodMoonWindow(w)) return "a blood moon night (0-XNPCCore never sleeps through one)";
            float hour = GameUtils.WorldTimeToHours(w.worldTime) + GameUtils.WorldTimeToMinutes(w.worldTime) / 60f;
            int dusk = w.DuskHour;                     // World's own, derived from DayLightLength
            float since = hour - dusk;
            if (since < 0f) since += 24f;              // past midnight
            // round U3 (research B7): never before 0-XNPCCore's OWN phase 3. SleepAfterDuskHours used to mirror its setting by
            // hand; opened inside its phase 2 the lease would now walk every leased member to the base campfire (3.4.06's
            // GatherChance 1.0) - so the larger of SC's value and 0-XNPCCore's, read from it (ScNpcClock).
            return since >= ScNpcClock.LeaseAfterDusk() ? null : "too soon after dusk (0-XNPCCore is still in its evening)";
        }

        public static void Reset() { leased.Clear(); dawnSince.Clear(); bedCache.Clear(); grants.Clear(); Opened = Closed = Refused = 0; LastRefusal = ""; }

        /// <summary>1 Hz from ScTicker: opens the night lease for everyone eligible and takes it back from everyone who is not.</summary>
        public static void Tick(World w, EntityPlayer player)
        {
            if (w == null || !ScCommunity.IsHost || !ScRegistry.Loaded) return;
            try
            {
                // SleepAtNight off (or no wake path) still has to CLOSE what is already open - just not opening would
                // leave whoever is asleep stranded in 0-XNPCCore's routine for good.
                string nightWhy = CannotWake() ? "0-XNPCCore cannot wake her (SdcsResident.Clear missing)" : SleepWhy(w);
                List<Verdict> all = Plan(w, nightWhy, ScConfig.Enclaves);
                for (int i = 0; i < all.Count; i++) Apply(all[i].Npc, all[i].Why, all[i].Min, all[i].Size, all[i].Name);
            }
            catch (Exception ex) { ScLog.Once("sleep-tick", ex); }
        }

        /// <summary>
        /// The enclaves' own people, in their own building: the POI footprint 0-XNPCCore wants is exactly what the
        /// enclave already stores (Enclave.Poi is the PrefabInstance's boundingBoxPosition, Enclave.Size its rotated
        /// size). A hostile enclave is left alone - its people are not the community's to put to bed.
        /// One pass over every enclave person rather than Living(en) per enclave - that allocates a fresh list and
        /// rescans all of EncPeople for each enclave, once a second.
        /// </summary>
        private static void CollectEnclaves(List<Verdict> into, string nightWhy)
        {
            foreach (EncPerson p in ScRegistry.EncPeople)
            {
                if (p == null) continue;
                Enclave en = ScEnclavePeople.EnclaveOf(p);
                if (en == null) continue;
                // OwnBody, not BodyOf: it also checks the body is still the one that person was given, so a
                // recycled entity id can never be marked a resident on SC's say-so.
                EntityAlive npc = ScEnclavePeople.OwnBody(p);
                if (npc == null) continue;
                string why = en.IsHostile ? "a hostile enclave"
                           : p.State != EncPerson.StateHome ? "not at home"
                           : Common(npc, nightWhy);
                into.Add(new Verdict { Npc = npc, Name = npc.EntityName, Why = why, Min = en.Poi, Size = en.Size,
                                       Key = p.Id, Group = en, Place = "the enclave" });
            }
        }

        /// <summary>Why this member may not be leased tonight, or null when she may. Fills the home zone's box.</summary>
        private static string MemberRefusal(World w, Member m, EntityAlive npc, string nightWhy, out Vector3i min, out Vector3i size)
        {
            min = Vector3i.zero;
            size = Vector3i.zero;
            if (m.Status != MemberStatus.Resident) return "not a resident at home";
            if (ScWatch.OnWatch(m)) return "on watch";
            if (ScWork.OnShift(m.Id)) return "on a work shift";
            // round m1t (T1): a light sleeper is up while a horde is called on the base or fighting it (she sleeps again after)
            if (ScBaseAttack.Open && !ScBaseAttack.Away && ScTraitEffects.WakesForAttack(m)) return ScTraitEffects.WakeWhy;
            if (!ScZone.HasHome) return "no home zone";
            // The box comes from ScZone.Bounds, the same XZ extent ScZone.Contains tests, so a LandClaimUpgrade-enlarged
            // claim gives 0-XNPCCore the whole zone. Building it from VanillaRadius instead would hand it a smaller box
            // than the one SC counts beds in, and a bed in the enlarged part would pass SC's gate while being invisible
            // to 0-XNPCCore's own bed scan - which is the floor-sleep case this round exists to prevent.
            HomeBox(out min, out size);
            if (!ScZone.Contains(npc.GetBlockPosition())) return "not inside the home zone";
            // The bed count is not tested here: it caps the people who pass everything else, so it needs all of them
            // at once (CapByBeds).
            return Common(npc, nightWhy);
        }

        /// <summary>The tests both sides share.</summary>
        private static string Common(EntityAlive npc, string nightWhy)
        {
            if (npc.IsDead() || npc.isEntityRemote) return "not a live local body";
            // round U2: a downed member is not leased (a lease she had closes through 0-XNPCCore's own teardown), nor a medic sent to
            // one (her lease closes, which wakes her)
            if (ScDowned.IsDown(npc)) return "down";
            if (ScDowned.IsReviving(npc.entityId)) return "on the way to revive a downed member";
            if (nightWhy != null) return nightWhy;
            // Mirrors 0-XNPCCore's own gate (SdcsNpcRelation.HiredOrOwnedByCvar): it refuses to mark, and clears, anyone
            // who is hired or being talked to. SC writes Owner for a work shift and for the length of a conversation.
            if (npc.Buffs == null) return "no buffs";
            if (npc.Buffs.GetCustomVar("Leader") > 0f) return "hired (following)";
            if (npc.Buffs.GetCustomVar("Owner") > 0f) return "busy with the player";
            if (IsBandit(npc)) return "a bandit class (0-XNPCCore keeps its own sleeping bandits separate)";
            return null;
        }

        /// <summary>Opens, holds or closes one body's lease.</summary>
        private static void Apply(EntityAlive npc, string why, Vector3i min, Vector3i size, string name)
        {
            bool held = leased.Contains(npc.entityId);
            if (why != null)
            {
                // Close on the CVAR, not only on SC's own set: the cvars are saved with the entity (EntityBuffs.Write
                // keeps every name that does not start with '.'), so a body that unloaded, or a world reloaded while
                // she was leased, comes back still marked a resident with SC's set empty. Left alone she would run
                // 0-XNPCCore's whole DAY routine - station work and strolling - which is exactly what was ruled out.
                if (!held && npc.GetCVar(ResidentCvar) < 0.5f) return;
                // At daybreak, give 0-XNPCCore a moment to wake her itself: its dawn tick moves her from phase 3 (asleep)
                // to phase 4 (wake) and gets her up off the bed, and closing first would cut that short. Only phase 3,
                // though - phase 4 then holds her at her bed anchor for its WorkStartHours (an hour of standing by the
                // bed every morning), and that hour is SC's day, so the lease closes as soon as phase 3 is over. And
                // only for DawnHoldSeconds: if its idea of dawn is later than the world's, SdcsResident.Clear wakes her
                // anyway (it runs SdcsSleep.Stop itself, so closing mid-sleep does not strand her).
                if (held && why == WhyDaytime)
                {
                    float phase = npc.GetCVar("SdcsRoutinePhase");
                    if (phase >= 2.5f && phase < 3.5f)
                    {
                        float since;
                        if (!dawnSince.TryGetValue(npc.entityId, out since)) dawnSince[npc.entityId] = since = Time.time;
                        if (Time.time - since < DawnHoldSeconds) return;
                    }
                }
                if (ReferenceEquals(why, ScTraitEffects.WakeWhy)) ScTraitEffects.TellWoken(npc, name);   // round m1t: Light Sleeper
                Close(npc, name, why);
                return;
            }
            if (!held)
            {
                Open(npc, min, size, name);
                return;
            }
            // 0-XNPCCore clears residency itself whenever Leader/Owner appeared (a talk, a shift). Those are refusals
            // above, so reaching here with the cvar gone means it was cleared for some other reason: take it again.
            if (npc.GetCVar(ResidentCvar) < 0.5f) Write(npc, min, size);
        }

        private static void Open(EntityAlive npc, Vector3i min, Vector3i size, string name)
        {
            Write(npc, min, size);
            leased.Add(npc.entityId);
            Opened++;
            ScLog.Verbose("sleep: " + name + " (" + npc.entityId + ") is off duty for the night - 0-XNPCCore's routine has her until dawn");
        }

        /// <summary>
        /// 0-XNPCCore's own teardown, bound once. Zeroing the seven cvars by hand is NOT enough and strands her: both
        /// SdcsResident.Tick and SdcsRoutine.Tick return at once while SdcsResident is below 0.5, so SdcsSleep.Tick
        /// never runs again - and the sleep run on the avatar only ends on a wake cause, which that tick is the only
        /// source of. She would stay lying in the bed, with SdcsRoutinePhase still 3 and .SdcsRoutineWalk still 1, so
        /// RoutineWalk at 3.8 would go on winning every scoring pass for the rest of the session and she would stand at
        /// her bed through the following day.
        /// SdcsResident.Clear does the whole unwind: FreeAll (releases the bed and work claims) + SdcsRoutine.ClearPhase
        /// (which is the only caller of SdcsSleep.Stop, and zeroes the phase and the walk cvar) + the seven cvars.
        /// It is internal, so it is bound by reflection the way SC binds other 0-XNPCCore internals.
        /// </summary>
        private static MethodInfo mClear;
        private static PropertyInfo pRoutineEnabled;
        private static MethodInfo mIsBandit;
        private static PropertyInfo pScanCache;
        private static bool clearBound, clearFailed;

        private static void BindClear()
        {
            if (clearBound) return;
            clearBound = true;
            const BindingFlags S = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            try
            {
                Assembly a = typeof(AvatarSdcsNpcController).Assembly;
                Type t = a.GetType("SdcsResident", false);
                mClear = t != null
                    ? t.GetMethod("Clear", S, null, new[] { typeof(EntityAlive), typeof(string) }, null)
                    : null;
                // Both internal, so reflection too: the routine can be switched off entirely in 0-XNPCCore's
                // Settings.xml, in which case every lease is a no-op and "sc sleep" should say so rather than
                // report people as asleep; and its own bandit refusal is mirrored for completeness.
                pRoutineEnabled = typeof(AvatarSdcsNpcController).GetProperty("RoutineEnabled", S);
                mIsBandit = typeof(AvatarSdcsNpcController).GetMethod("IsBanditClass", S, null, new[] { typeof(int) }, null);
                // How long its POI bed scan is cached - a rise in SC's bed count waits this out (Grant).
                pScanCache = typeof(AvatarSdcsNpcController).GetProperty("RoutineScanCacheSeconds", S);
            }
            catch (Exception ex) { ScLog.Once("sleep-bind-clear", ex); mClear = null; }
            if (mClear == null)
                ScLog.Warn("sleep: 0-XNPCCore's SdcsResident.Clear was not found - the night lease is OFF, because without it "
                           + "a survivor cannot be woken again. Check the 0-XNPCCore version (3.4.05 or later).");
        }

        private static void Close(EntityAlive npc, string name, string why)
        {
            leased.Remove(npc.entityId);
            dawnSince.Remove(npc.entityId);
            Closed++;
            BindClear();
            try
            {
                if (mClear != null) mClear.Invoke(null, new object[] { npc, "SurvivorCommunity: " + why });
                else
                {
                    // Last resort only: she is woken as far as the cvars allow, so the routine cannot keep driving her.
                    npc.SetCVar("SdcsRoutinePhase", 0f);
                    npc.SetCVar(".SdcsRoutineWalk", 0f);
                    npc.SetCVar(ResidentCvar, 0f);
                    foreach (string c in BoxCvars) npc.SetCVar(c, 0f);
                }
            }
            catch (Exception ex) { ScLog.Once("sleep-close", ex); }
            ScLog.Verbose("sleep: " + name + " (" + npc.entityId + ") is back on SC's own routine - " + why);
        }

        /// <summary>Is 0-XNPCCore's resident routine switched on at all? Unknown (no bind) is treated as yes.</summary>
        private static bool RoutineOn()
        {
            BindClear();
            try { return pRoutineEnabled == null || (bool)pRoutineEnabled.GetValue(null, null); }
            catch (Exception ex) { ScLog.Once("sleep-routine-enabled", ex); return true; }
        }

        /// <summary>0-XNPCCore's own third refusal, mirrored: it never makes a bandit class a resident.</summary>
        private static bool IsBandit(EntityAlive npc)
        {
            BindClear();
            try { return mIsBandit != null && (bool)mIsBandit.Invoke(null, new object[] { npc.entityClass }); }
            catch (Exception ex) { ScLog.Once("sleep-isbandit", ex); return false; }
        }

        /// <summary>True when the lease may not be opened at all, because the wake path is missing.</summary>
        private static bool CannotWake()
        {
            BindClear();
            if (mClear != null) return false;
            clearFailed = true;
            return true;
        }

        private static void Write(EntityAlive npc, Vector3i min, Vector3i size)
        {
            npc.SetCVar(BoxCvars[0], min.x);
            npc.SetCVar(BoxCvars[1], min.y);
            npc.SetCVar(BoxCvars[2], min.z);
            npc.SetCVar(BoxCvars[3], size.x);
            npc.SetCVar(BoxCvars[4], size.y);
            npc.SetCVar(BoxCvars[5], size.z);
            npc.SetCVar(ResidentCvar, 1f);
        }

        /// <summary>
        /// "sc sleep": every MEMBER (enclave people are not listed), whether SC is holding the night lease on her, and
        /// when it is not, the exact reason - so a survivor who will not go to bed can be explained without reading the
        /// log. Reading does not change anything: the refusal counters are snapshotted and put back.
        /// </summary>
        public static void ConsoleCmd(Action<string> output)
        {
            World w = ScCommunity.World;
            if (w == null) { output("[SC] no world"); return; }
            int keepRefused = Refused; string keepLast = LastRefusal;   // reading must not move the counters
            string nightWhy = SleepWhy(w);
            output("[SC] sleep: " + (ScConfig.SleepAtNight ? "on" : "OFF (SleepAtNight)") + ", " + (nightWhy == null ? "sleeping hours" : "not sleeping hours: " + nightWhy)
                   + " - leases opened " + Opened + ", closed " + Closed + ", refused " + Refused
                   + (string.IsNullOrEmpty(LastRefusal) ? "" : " (last: " + LastRefusal + ")"));
            string bedLine = "[SC] home zone beds: " + ScRegistry.Community.Beds + " in the zone";
            if (ScZone.HasHome)
            {
                Vector3i bmin, bsize;
                HomeBox(out bmin, out bsize);
                bool whole;
                int inBox = BedsIn(HomeGroup, bmin, bsize, out whole);
                bedLine += ", " + (inBox < 0 ? "? (cannot count yet)" : inBox.ToString()) + " within " + Math.Max(2, ScConfig.SleepZoneHeight)
                           + " m of the home block's height in loaded chunks (the box 0-XNPCCore searches - only these count)"
                           + (inBox >= 0 && !whole ? " - part of the zone is not loaded" : "");
                if (inBox == 0) bedLine += "  <- no bed: nobody at home will be leased out";
            }
            output(bedLine);
            output("[SC] sleep clock: " + ScNpcClock.Describe(w));   // round U3 (B7): the lease waits for 0-XNPCCore's own phase 3
            var shown = new HashSet<int>();
            foreach (Verdict v in Plan(w, nightWhy, false))
            {
                shown.Add(v.Key);
                bool held = leased.Contains(v.Npc.entityId);
                output("  " + v.Name + " (" + v.Npc.entityId + "): " + (held ? "ASLEEP/leased" : "SC's own routine")
                       + (v.Why == null ? "" : " - " + v.Why)
                       + "  [SdcsResident=" + v.Npc.GetCVar(ResidentCvar).ToString("0.#")
                       + " .SdcsRoutineWalk=" + v.Npc.GetCVar(".SdcsRoutineWalk").ToString("0.#")
                       + " SdcsRoutinePhase=" + v.Npc.GetCVar("SdcsRoutinePhase").ToString("0.#") + "]");
            }
            foreach (Member m in ScRegistry.Members.Values)
                if (!shown.Contains(m.Id)) output("  " + m.Name + ": not loaded");
            Refused = keepRefused; LastRefusal = keepLast;
        }

        // ---- the plan, and beds ----

        /// <summary>One body's verdict for this pass. Why is null when she may be leased.</summary>
        private sealed class Verdict
        {
            public EntityAlive Npc;
            public string Name;
            public string Why;
            public Vector3i Min, Size;      // the box 0-XNPCCore is given, and the box beds are counted in
            public int Key;                 // her record's id: who keeps a bed first, after whoever is already asleep
            public object Group;            // HomeGroup, or her Enclave - beds are shared within one group
            public string Place;            // for the refusal text
        }
        private static readonly object HomeGroup = new object();

        /// <summary>
        /// Everyone's verdict for this pass, the bed cap included - the one place both Tick and "sc sleep" get it from,
        /// so the console shows exactly what is applied.
        /// </summary>
        private static List<Verdict> Plan(World w, string nightWhy, bool enclaves)
        {
            var all = new List<Verdict>();
            foreach (Member m in ScRegistry.Members.Values)
            {
                EntityAlive npc = ScCommunity.FindNpc(m.Id);
                if (npc == null) continue;
                Vector3i min, size;
                string why = MemberRefusal(w, m, npc, nightWhy, out min, out size);
                all.Add(new Verdict { Npc = npc, Name = m.Name, Why = why, Min = min, Size = size, Key = m.Id,
                                      Group = HomeGroup, Place = "the home zone" });
            }
            if (enclaves) CollectEnclaves(all, nightWhy);
            CapByBeds(all);
            return all;
        }

        /// <summary>
        /// One bed is not N beds: 0-XNPCCore claims one bed per resident (SdcsClaimTable), and anyone who gets no claim
        /// lies down wherever she is standing. So within each building the number leased is capped at its bed count.
        /// Ranked among the people who passed every OTHER test tonight - someone away, on a shift or not loaded no
        /// longer holds a bed she cannot use - with whoever is already asleep first, so a member coming off watch at
        /// 2 am never wakes one who is in bed, and then by record id, so the same people get the beds night after night.
        /// </summary>
        private static void CapByBeds(List<Verdict> all)
        {
            var groups = new Dictionary<object, List<Verdict>>();
            foreach (Verdict v in all)
            {
                if (v.Why != null) continue;
                List<Verdict> g;
                if (!groups.TryGetValue(v.Group, out g)) groups[v.Group] = g = new List<Verdict>();
                g.Add(v);
            }
            foreach (KeyValuePair<object, List<Verdict>> kv in groups)
            {
                List<Verdict> g = kv.Value;
                bool complete;
                int counted = BedsIn(kv.Key, g[0].Min, g[0].Size, out complete);
                if (counted < 0)
                {
                    // Cannot count at all (no world or no block table yet). Nobody new is leased, nobody asleep is woken.
                    foreach (Verdict v in g)
                        if (!InBed(v.Npc)) v.Why = "beds not counted yet";
                    continue;
                }
                int granted = Grant(kv.Key, counted);
                g.Sort((a, b) =>
                {
                    bool la = InBed(a.Npc), lb = InBed(b.Npc);
                    if (la != lb) return la ? -1 : 1;
                    return a.Key.CompareTo(b.Key);
                });
                string place = g[0].Place;
                string none = complete ? "no bed in " + place : "no bed found yet in " + place + " (part of it is not loaded)";
                string full = "not enough beds (" + counted + ") - someone else has them";
                string waiting = "not enough beds yet (" + granted + " of " + counted + " - a new bed is used once 0-XNPCCore rescans)";
                bool noted = false;
                for (int i = 0; i < g.Count; i++)
                {
                    // Someone already in bed holds 0-XNPCCore's claim: only a COMPLETE count with no bed left for her place
                    // takes her out (a bed was removed). A partial count - a neighboring chunk still loading after a
                    // reload - never wakes her. Anyone new is held to the granted count, which waits out a rise.
                    bool refuse = InBed(g[i].Npc) ? complete && i >= counted : i >= granted;
                    if (!refuse) continue;
                    // per person: past the real count it is a shortage; between granted and counted it is the wait
                    g[i].Why = counted == 0 ? none : i >= counted ? full : waiting;
                    if (!noted) { Note(counted == 0 ? "no bed in " + place : "not enough beds (" + granted + ")"); noted = true; }
                }
            }
        }

        /// <summary>The bed count SC lets new sleepers use, per building, and when a higher count was first seen.</summary>
        private sealed class BedGrant { public int Count; public float RiseSince = -1f; }
        private static readonly Dictionary<object, BedGrant> grants = new Dictionary<object, BedGrant>();

        /// <summary>
        /// The number of beds new sleepers may be leased against. A FALL applies at once (fewer beds is always safe). A
        /// RISE - a bed placed, or a chunk of the box finishing loading - only once it has held for longer than
        /// 0-XNPCCore's own POI scan cache: it caches its bed list per box for Routine/ScanCacheSeconds (60 s by
        /// default) whether that scan was complete or not, so a survivor leased on SC's newer count would find "no free
        /// bed" in its older list and lie down wherever she stood (SleepRules.MayBegin with no bed).
        /// sc sleep goes through here too; it can only start the clock the tick would have started anyway.
        /// </summary>
        private static int Grant(object group, int counted)
        {
            float now = Time.time;                             // 0-XNPCCore's scan clock is Time.time as well
            BedGrant g;
            if (!grants.TryGetValue(group, out g)) { grants[group] = new BedGrant { Count = counted }; return counted; }
            if (counted <= g.Count) { g.Count = counted; g.RiseSince = -1f; return counted; }
            if (g.RiseSince < 0f) g.RiseSince = now;
            if (now - g.RiseSince >= ScanCacheSeconds() + 5f) { g.Count = counted; g.RiseSince = -1f; }
            return g.Count;
        }

        /// <summary>0-XNPCCore's Routine/ScanCacheSeconds (internal, so reflection), else its default of 60.</summary>
        private static float ScanCacheSeconds()
        {
            BindClear();
            try
            {
                if (pScanCache != null)
                {
                    object v = pScanCache.GetValue(null, null);
                    if (v is float && (float)v > 0f) return (float)v;
                }
            }
            catch (Exception ex) { ScLog.Once("sleep-scan-cache", ex); }
            return 60f;
        }

        /// <summary>
        /// Is she in 0-XNPCCore's routine tonight already - SC's lease, or the saved cvar? After a world load the lease set
        /// is empty while SdcsResident (saved with the entity) is still 1, so testing the set alone would rank everyone
        /// asleep as a newcomer and could hand her bed to someone else.
        /// </summary>
        private static bool InBed(EntityAlive npc)
        {
            return leased.Contains(npc.entityId) || npc.GetCVar(ResidentCvar) >= 0.5f;
        }

        /// <summary>The home zone's box as 0-XNPCCore is given it: the zone's XZ extent (ScZone.Bounds, the same one
        /// ScZone.Contains tests, so a LandClaimUpgrade-enlarged claim is covered) within SleepZoneHeight of the home block.</summary>
        private static void HomeBox(out Vector3i min, out Vector3i size)
        {
            int xMin, xMax, zMin, zMax;
            ScZone.Bounds(out xMin, out xMax, out zMin, out zMax);
            Vector3i home = ScZone.Home;
            int band = Math.Max(2, ScConfig.SleepZoneHeight);
            min = new Vector3i(xMin, home.y - band, zMin);
            size = new Vector3i(xMax - xMin + 1, band * 2 + 1, zMax - zMin + 1);
        }

        private sealed class BedAnswer { public int Count; public bool Complete; public float Until; public Vector3i Min, Size; }
        private static readonly Dictionary<object, BedAnswer> bedCache = new Dictionary<object, BedAnswer>();
        private static readonly int[] areaCounts = new int[4];
        /// <summary>A box's answer is kept this long: beds rarely move, and the count sweeps whole chunks.</summary>
        private const float BedCacheSeconds = 60f;
        /// <summary>...and this long while part of the box is not loaded, so the count grows as the chunks come in.</summary>
        private const float BedPartialSeconds = 5f;

        /// <summary>
        /// How many beds stand inside this box, Y included. Counted in the SAME box 0-XNPCCore is given (HomeBox, or the
        /// enclave's POI), not ScRegistry's zone-wide count: that one is XZ only, so a bed in a deep basement or high up
        /// a tower passed SC's gate while lying outside the box 0-XNPCCore searches for a claim - and she slept on the
        /// floor.
        /// Through ScScan's own area sweep - the same block table and IsBed test the home zone uses, so a bed mod
        /// recognized at home is recognized here. The two sides still do not define a bed identically: ScScan also
        /// accepts any BlockSleepingBag when BedsBySleepingBag is on, while 0-XNPCCore claims against its own
        /// Routine/BedBlocks name list. For vanilla beds they agree; for a modded bed only SC recognizes, BedBlocks is
        /// the setting to check first if someone sleeps on the floor.
        /// The box is 0-XNPCCore's own test, RoutineRules.InsideBox: min to min + size INCLUSIVE on every axis (one block
        /// more than size on each), so SC's count covers exactly the cells its bed search does.
        /// Only LOADED chunks are counted. 0-XNPCCore can only claim a bed it can read, so a bed in a chunk that is not
        /// loaded is no use tonight anyway - and waiting for the whole box would lease nobody at all while a corner of
        /// a large (LandClaimUpgrade-enlarged) zone is out of range. A partial answer is kept only briefly, so the count
        /// grows as the chunks come in.
        /// Cached per box, and only asked once every other test has passed, so it never runs by day. -1 only when it
        /// cannot count at all (no world or no block table yet).
        /// </summary>
        private static int BedsIn(object group, Vector3i min, Vector3i size, out bool complete)
        {
            BedAnswer a;
            float now = Time.unscaledTime;
            if (bedCache.TryGetValue(group, out a) && a.Min == min && a.Size == size && now < a.Until) { complete = a.Complete; return a.Count; }
            if (!ScScan.CountAreaLoaded(min.x, min.x + Math.Max(0, size.x), min.y, min.y + Math.Max(0, size.y),
                                        min.z, min.z + Math.Max(0, size.z), areaCounts, out complete))
                return -1;
            bedCache[group] = new BedAnswer { Count = areaCounts[0], Complete = complete, Min = min, Size = size,
                                              Until = now + (complete ? BedCacheSeconds : BedPartialSeconds) };
            return areaCounts[0];
        }
    }
}
#endif
