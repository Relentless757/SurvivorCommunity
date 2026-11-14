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

        public static void Forget(int entityId) { leased.Remove(entityId); }

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
            return since >= Math.Max(0f, ScConfig.SleepAfterDuskHours) ? null : "too soon after dusk (0-XNPCCore is still strolling)";
        }

        public static void Reset() { leased.Clear(); encBeds.Clear(); Opened = Closed = Refused = 0; LastRefusal = ""; }

        /// <summary>1 Hz from ScTicker: opens the night lease for everyone eligible and takes it back from everyone who is not.</summary>
        public static void Tick(World w, EntityPlayer player)
        {
            if (w == null || !ScCommunity.IsHost || !ScRegistry.Loaded) return;
            try
            {
                // SleepAtNight off (or no wake path) still has to CLOSE what is already open - just not opening would
                // leave whoever is asleep stranded in 0-XNPCCore's routine for good.
                string nightWhy = CannotWake() ? "0-XNPCCore cannot wake her (SdcsResident.Clear missing)" : SleepWhy(w);
                foreach (Member m in ScRegistry.Members.Values)
                {
                    EntityAlive npc = ScCommunity.FindNpc(m.Id);
                    if (npc == null) { continue; }
                    Vector3i min, size;
                    string why = MemberRefusal(w, m, npc, nightWhy, out min, out size);
                    Apply(npc, why, min, size, m.Name);
                }
                if (ScConfig.Enclaves) TickEnclaves(w, nightWhy);
            }
            catch (Exception ex) { ScLog.Once("sleep-tick", ex); }
        }

        /// <summary>
        /// The enclaves' own people, in their own building: the POI footprint 0-XNPCCore wants is exactly what the
        /// enclave already stores (Enclave.Poi is the PrefabInstance's boundingBoxPosition, Enclave.Size its rotated
        /// size). A hostile enclave is left alone - its people are not the community's to put to bed.
        /// </summary>
        private static void TickEnclaves(World w, string nightWhy)
        {
            // One pass over every enclave person rather than Living(en) per enclave - that allocates a fresh list and
            // rescans all of EncPeople for each enclave, once a second.
            foreach (EncPerson p in ScRegistry.EncPeople)
            {
                if (p == null) continue;
                Enclave en = ScEnclavePeople.EnclaveOf(p);
                if (en != null)
                {
                    // OwnBody, not BodyOf: it also checks the body is still the one that person was given, so a
                    // recycled entity id can never be marked a resident on SC's say-so.
                    EntityAlive npc = ScEnclavePeople.OwnBody(p);
                    if (npc == null) continue;
                    Vector3i min = en.Poi, size = en.Size;
                    string why = en.IsHostile ? "a hostile enclave"
                               : p.State != EncPerson.StateHome ? "not at home"
                               : Common(npc, nightWhy);
                    if (why == null && !EnclaveHasBed(en)) { why = "no bed in the enclave"; Note(why); }
                    Apply(npc, why, min, size, npc.EntityName);
                }
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
            if (!ScZone.HasHome) return "no home zone";
            // The box comes from ScZone.Bounds, the same XZ extent ScZone.Contains tests, so a LandClaimUpgrade-enlarged
            // claim gives 0-XNPCCore the whole zone. Building it from VanillaRadius instead would hand it a smaller box
            // than the one SC counts beds in, and a bed in the enlarged part would pass SC's gate while being invisible
            // to 0-XNPCCore's own bed scan - which is the floor-sleep case this round exists to prevent.
            int xMin, xMax, zMin, zMax;
            ScZone.Bounds(out xMin, out xMax, out zMin, out zMax);
            Vector3i home = ScZone.Home;
            int band = Math.Max(2, ScConfig.SleepZoneHeight);
            min = new Vector3i(xMin, home.y - band, zMin);
            size = new Vector3i(xMax - xMin + 1, band * 2 + 1, zMax - zMin + 1);
            if (!ScZone.Contains(npc.GetBlockPosition())) return "not inside the home zone";
            string why = Common(npc, nightWhy);
            if (why != null) return why;
            // One bed is not N beds: 0-XNPCCore claims one bed per resident (SdcsClaimTable), and anyone who gets no
            // claim lies down where she stands. So the number leased is capped at the number of beds, oldest member id
            // first so the same people get them each night rather than a nightly reshuffle.
            int beds = ScRegistry.Community.Beds;
            if (beds <= 0) { Note("no bed in the home zone"); return "no bed in the home zone"; }
            if (!WithinBedCount(m, beds)) { Note("not enough beds (" + beds + ")"); return "not enough beds (" + beds + ") - someone else has them"; }
            return null;
        }

        /// <summary>The tests both sides share.</summary>
        private static string Common(EntityAlive npc, string nightWhy)
        {
            if (npc.IsDead() || npc.isEntityRemote) return "not a live local body";
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
                // Let 0-XNPCCore leave the sleep phase on its own where it can: closing mid-phase-3 is the stranding
                // case, and at dawn its own tick unwinds her a moment later anyway.
                if (held && why == WhyDaytime && npc.GetCVar("SdcsRoutinePhase") >= 2.5f) return;
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
            }
            catch (Exception ex) { ScLog.Once("sleep-bind-clear", ex); mClear = null; }
            if (mClear == null)
                ScLog.Warn("sleep: 0-XNPCCore's SdcsResident.Clear was not found - the night lease is OFF, because without it "
                           + "a survivor cannot be woken again. Check the 0-XNPCCore version (3.4.05 or later).");
        }

        private static void Close(EntityAlive npc, string name, string why)
        {
            leased.Remove(npc.entityId);
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
            output("[SC] home zone beds: " + ScRegistry.Community.Beds + (ScRegistry.Community.Beds > 0 ? "" : "  <- no bed: nobody at home will be leased out"));
            foreach (Member m in ScRegistry.Members.Values)
            {
                EntityAlive npc = ScCommunity.FindNpc(m.Id);
                if (npc == null) { output("  " + m.Name + ": not loaded"); continue; }
                Vector3i min, size;
                string why = MemberRefusal(w, m, npc, nightWhy, out min, out size);
                bool held = leased.Contains(npc.entityId);
                output("  " + m.Name + " (" + npc.entityId + "): " + (held ? "ASLEEP/leased" : "SC's own routine")
                       + (why == null ? "" : " - " + why)
                       + "  [SdcsResident=" + npc.GetCVar(ResidentCvar).ToString("0.#")
                       + " .SdcsRoutineWalk=" + npc.GetCVar(".SdcsRoutineWalk").ToString("0.#") + "]");
            }
            Refused = keepRefused; LastRefusal = keepLast;
        }

        // ---- beds ----

        private sealed class BedAnswer { public bool Has; public float Until; }
        private static readonly Dictionary<int, BedAnswer> encBeds = new Dictionary<int, BedAnswer>();
        private static readonly int[] areaCounts = new int[4];
        /// <summary>An enclave's answer is kept this long: beds rarely move, and the count sweeps whole chunks.</summary>
        private const float BedCacheSeconds = 60f;

        /// <summary>
        /// Is this member within the first `beds` of those who could be leased tonight? Ordered by member id, so the
        /// same people keep the same beds night after night instead of the set changing every evening.
        /// Note the two sides do not define a bed identically - SC's count is ScScan's (which also accepts any
        /// BlockSleepingBag when BedsBySleepingBag is on), while 0-XNPCCore claims against its own Routine/BedBlocks
        /// name list. For vanilla beds the two agree; a modded bed only SC recognizes would still leave her without a
        /// claim, so BedBlocks is the setting to check first if someone sleeps on the floor.
        /// </summary>
        private static bool WithinBedCount(Member who, int beds)
        {
            int ahead = 0;
            foreach (Member m in ScRegistry.Members.Values)
            {
                if (m.Id == who.Id) continue;
                if (m.Status != MemberStatus.Resident || ScWatch.OnWatch(m) || ScWork.OnShift(m.Id)) continue;
                if (m.Id < who.Id && ++ahead >= beds) return false;
            }
            return true;
        }

        /// <summary>
        /// Is there a bed inside this enclave's POI? Counted through ScScan's own area sweep - the same block table and
        /// the same IsBed test the home zone uses, so a bed mod recognized at home is recognized here. Cached per
        /// enclave, and only ever asked once every other test has passed, so it never runs by day. The caveat under
        /// WithinBedCount applies here too: ScScan's idea of a bed is not byte-for-byte 0-XNPCCore's. CountArea answers
        /// false while any chunk of the box is still loading; that is treated as "no bed for now", and the lease simply
        /// opens a moment later once the building is in.
        /// </summary>
        private static bool EnclaveHasBed(Enclave en)
        {
            BedAnswer a;
            float now = Time.unscaledTime;
            if (encBeds.TryGetValue(en.Id, out a) && now < a.Until) return a.Has;
            bool has = false;
            if (ScScan.CountArea(en.Poi.x, en.Poi.x + Math.Max(0, en.Size.x - 1),
                                 en.Poi.z, en.Poi.z + Math.Max(0, en.Size.z - 1), areaCounts))
                has = areaCounts[0] > 0;
            else
                return false;                                  // still loading: no answer yet, and nothing cached
            encBeds[en.Id] = new BedAnswer { Has = has, Until = now + BedCacheSeconds };
            return has;
        }
    }
}
#endif
