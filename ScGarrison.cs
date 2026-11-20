using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Outposts v1.1, people (Outposts_Plan.md section 3): members posted to an outpost.
    ///   Journey   - round op1 (2026-11-19; the user: "members shouldn't have to be close to an outpost to be assigned to them. They
    ///               should travel to it from base similar to how they did supply runs"): posting works from anywhere. The member
    ///               sets off from home on the runner legs (ScRunners) as MemberStatus.Transit - Outbound to a spot beside the claim,
    ///               where they become staff; a recall, or the game sending staff home, walks them back (Inbound to the gate, Back
    ///               to their post) and they are Resident only on arrival. A safe journey: seen and fighting when you are near, but
    ///               no off-screen dice, no distress, no radio talk, never given up hurt. On the way out they hold one of the
    ///               outpost's posts (Assigned: the cap counts them) but add no yield, no security and no stand-in.
    ///               Embodied (Snap set): their body walked out like a runner's - the walker near you, the snapshot further out
    ///               (IsPhysical). Parked (no Snap): posted while their body was not loaded, it waits hidden at home, saved with its
    ///               chunk, and the legs run on the world clock here (TickJourneys); the first time it is loaded it is taken into a
    ///               snapshot (ScRunners.Embody) and they are embodied from then on. See research/outposts/POSTING_TRAVEL.md.
    ///   Garrison  - a member who reached the outpost (MemberStatus.Garrison). Their body is the snapshot when they walked there
    ///               (ScRunners.HeldOffline: no world body), or waits hidden and parked at home (posted by an older build, or
    ///               parked on the way and not loaded since - taken into a snapshot the first time it loads). Staff still eat.
    ///               Round ie1 (ScEconomy): each staffer adds OutpostStaffBundles whole units a day, and one whose specialty
    ///               fits the outpost's resource OutpostMatchBonus more, once (cooks and gardeners for food, medics and
    ///               chemists for meds, shooters for ammo, crafters for materials, mechanics for fuel, gardeners for water).
    ///   Stand-in  - within OutpostStandInRange of a loaded, staffed outpost, a copy of each staffer (their class and
    ///               look, skills, traits) stands guard at the claim; it goes when the player leaves. It IS the
    ///               survivor: if it dies there, they died (ScRadio.OnStandInDied routes here). Never saved.
    ///   Moods     - posted away from home: -3 (a Loner: +5), +3 while the player visited in the last 3 days.
    ///   Walk-ins  - a staffed outpost sometimes becomes the spawn point of a radio contact.
    /// Host only.
    /// </summary>
    public static class ScGarrison
    {
        /// <summary>On a stand-in: which outpost it guards (with ScRadio.StandInCvar = the member id).</summary>
        public const string StandInOutpostCvar = "sc_standin_op";
        /// <summary>Round op1: a parked body whose capture failed is not tried again for this long (real seconds).</summary>
        private const float EmbodyRetrySeconds = 30f;

        private static float nextTick;
        private static readonly Dictionary<int, float> nextCount = new Dictionary<int, float>();
        private static readonly int[] countScratch = new int[4];
        /// <summary>Round op1 (session only): member id -> when a failed capture of their parked body may be tried again.</summary>
        private static readonly Dictionary<int, float> embodyRetry = new Dictionary<int, float>();
        /// <summary>Round op1 (session only): a parked traveler waiting at the gate with you home and no body of theirs there, since when.</summary>
        private static readonly Dictionary<int, float> absentSince = new Dictionary<int, float>();
        private static readonly List<Member> journeyScratch = new List<Member>();

        public static void Reset()
        {
            nextTick = 0f;
            nextCount.Clear();
            Orphans.Clear();
            embodyRetry.Clear();
            absentSince.Clear();
            journeyScratch.Clear();
        }

        /// <summary>Stand-ins that loaded from a save (the framework PostInit postfix): removed on the next tick.</summary>
        public static readonly HashSet<int> Orphans = new HashSet<int>();

        private static void SweepOrphans(World w)
        {
            if (Orphans.Count == 0) return;
            foreach (int id in Orphans)
            {
                EntityAlive s = ScNpc.As(ScNpc.Loaded(w, id));     // still being built: looked at again next sweep
                if (s == null) continue;
                bool current = false;
                foreach (var m in ScRegistry.Members.Values) if (m.StandInId == id) { current = true; break; }
                if (!current) { ScRuns.RemoveEntity(s); ScLog.Out("removed a stale outpost stand-in (entity " + id + ")"); }
            }
            Orphans.Clear();
        }

        // ------------------------------------------------------------------ queries ----

        public static int StaffCap(Outpost o) { return o != null && o.Level >= 2 ? 2 : 1; }

        /// <summary>The members who hold the outpost now (arrived): yield, security, stand-ins, workshops, walk-ins.</summary>
        public static List<Member> Staff(Outpost o)
        {
            var list = new List<Member>();
            if (o == null) return list;
            foreach (var m in ScRegistry.Members.Values)
                if (m.Status == MemberStatus.Garrison && m.OutpostId == o.Id) list.Add(m);
            list.Sort((a, b) => a.Id.CompareTo(b.Id));
            return list;
        }

        public static int StaffCount(Outpost o)
        {
            int n = 0;
            if (o == null) return 0;
            foreach (var m in ScRegistry.Members.Values) if (m.Status == MemberStatus.Garrison && m.OutpostId == o.Id) n++;
            return n;
        }

        /// <summary>Round op1: on the way to an outpost (Transit, Outbound) - the one in OutpostId.</summary>
        public static bool HeadingOut(Member m) { return m != null && m.Status == MemberStatus.Transit && m.RunLeg == RunLeg.Outbound; }

        /// <summary>Round op1: the members on the way to this outpost, by id.</summary>
        public static List<Member> OnTheWay(Outpost o)
        {
            var list = new List<Member>();
            if (o == null) return list;
            foreach (var m in ScRegistry.Members.Values) if (HeadingOut(m) && m.OutpostId == o.Id) list.Add(m);
            list.Sort((a, b) => a.Id.CompareTo(b.Id));
            return list;
        }

        /// <summary>Round op1: the posts the outpost has given out - its staff and those on the way to it. The cap counts this.</summary>
        public static int Assigned(Outpost o)
        {
            int n = 0;
            if (o == null) return 0;
            foreach (var m in ScRegistry.Members.Values)
                if (m.OutpostId == o.Id && (m.Status == MemberStatus.Garrison || HeadingOut(m))) n++;
            return n;
        }

        /// <summary>Round op1: the garrison's slots as the Outposts tab lists them - the staff, then those on the way.</summary>
        public static List<Member> Slots(Outpost o)
        {
            List<Member> list = Staff(o);
            list.AddRange(OnTheWay(o));
            return list;
        }

        public static Outpost PostOf(Member m)
        {
            return m != null && m.Status == MemberStatus.Garrison ? ScOutposts.ById(m.OutpostId) : null;
        }

        /// <summary>Round op1: posted or on the road with no snapshot - their body waits hidden and parked at home.</summary>
        public static bool Parked(Member m)
        {
            return m != null && (m.Status == MemberStatus.Garrison || m.Status == MemberStatus.Transit) && string.IsNullOrEmpty(m.Snap);
        }

        /// <summary>
        /// How many stars a member brings to an outpost of this resource (0 = no match). Round ie1: gardeners match water too
        /// (water supply runs already matched Gardening), so a water outpost has a specialty like every other kind.
        /// </summary>
        public static int MatchStars(Member m, Resource r)
        {
            if (m == null) return 0;
            switch (r)
            {
                case Resource.Food: return m.Specialty == Specialty.Cooking || m.Specialty == Specialty.Gardening ? m.SpecLevel : 0;
                case Resource.Meds: return m.Specialty == Specialty.Medicine || m.Specialty == Specialty.Chemistry ? m.SpecLevel : 0;
                case Resource.Ammo: return m.Stars(Skill.Shooting) >= 2 ? m.Stars(Skill.Shooting) - 1 : 0;
                case Resource.Materials: return m.Specialty == Specialty.Craftsmanship ? m.SpecLevel : 0;
                case Resource.Fuel: return m.Specialty == Specialty.Mechanics ? m.SpecLevel : 0;
                case Resource.Water: return m.Specialty == Specialty.Gardening ? m.SpecLevel : 0;
                default: return 0;
            }
        }

        // round ie1: a staffer's share of the yield is whole units now - ScEconomy.StaffUnits, MatchUnits and StaffShare

        // ------------------------------------------------------------------ posting ----

        /// <summary>
        /// error = a localization key. Round op1: no longer "too far away" - a member whose body is not loaded sets off from home
        /// parked (TickJourneys). npc is kept for the callers; the journey's start reads it in Post.
        /// </summary>
        public static bool CanPost(Member m, EntityAlive npc, Outpost o, out string error)
        {
            error = null;
            if (!ScCommunity.IsHost) { error = "scHostOnly"; return false; }
            if (m == null || o == null) { error = "scInternal"; return false; }
            if (!o.Holding) { error = "scOpPostLost"; return false; }
            if (m.Status == MemberStatus.Garrison) { error = m.OutpostId == o.Id ? "scOpPostAlready" : "scOpPostElsewhere"; return false; }
            if (m.Status == MemberStatus.Transit) { error = "scOpOnTheWay"; return false; }     // round op1: on the road to or from an outpost
            if (m.Status == MemberStatus.OnRun) { error = "scOnRun"; return false; }
            // round op1: a missing resident has no body to send (Call back first)
            if (m.Status != MemberStatus.Resident || ScMissing.IsMissing(m)) { error = "scOpPostNotHome"; return false; }
            if (ScWatch.OnWatch(m)) { error = "scWatchBusy"; return false; }     // relieve them at the Watch post first (D3)
            if (m.Injured) { error = "scOpPostInjured"; return false; }
#if SC_V34
            if (ScDowned.RefuseVerb(m)) { error = "scDownedBusy"; return false; }   // round U2: no posting for a downed member
#endif
            if (ScConfig.RefusesForMood(m)) { error = "scRefuseMood"; return false; }   // P2: never by default
            if (Assigned(o) >= StaffCap(o)) { error = "scOpPostFull"; return false; }   // round op1: those on the way hold their post
            return true;
        }

        /// <summary>
        /// Round op1: the member sets off for the outpost. With their body loaded it walks out of the base like a runner (captured
        /// first: the crash fallback, and what the registry walks once they are out of range); without, they start offline and
        /// parked. They become staff on arrival (TakeUpPost).
        /// </summary>
        public static bool Post(Member m, EntityAlive npc, Outpost o, EntityPlayerLocal player, out string error)
        {
            if (!CanPost(m, npc, o, out error)) return false;
            World w = ScCommunity.World;
            if (w == null) { error = "scInternal"; return false; }
            if (npc != null && (npc.IsDead() || npc.entityId != m.Id)) { error = "scInternal"; return false; }
            int slot = Assigned(o);                                                      // the stand-in spot they will take
#if SC_V34
            ScWork.EndFor(m, "posted to an outpost");   // C1: off the work shift first
#endif
            return npc != null ? SetOut(w, m, npc, o, slot, player, out error) : SetOutParked(w, m, o, slot, player, out error);
        }

        /// <summary>Round op1: the run fields a journey starts from (no site, no haul, no dice, no distress, no radio).</summary>
        private static void ResetRunFields(Member m)
        {
            m.RunSite = Vector3i.zero;
            m.RunCarry = 0;
            m.RollPending = false;
            m.RunHurt = false;
            m.RunDistress = false;
            m.RunSafe = false;
            m.EventSpawned = false;
            m.StandInId = -1;
            m.EventZombies.Clear();
#if SC_V34
            m.RunTalk = 0;
            m.RunRadio = false;
            ScMining.ClearJob(m);
#endif
        }

        private static void BeginJourney(Member m, Outpost o)
        {
            m.Status = MemberStatus.Transit;
            ScStuck.ResetGain(m);   // round 29 review L5: no held move back outlives the status
            m.OutpostId = o.Id;
            m.RunDest = o.Poi;
            m.RunDestName = o.Name ?? "";
            ResetRunFields(m);
        }

        /// <summary>Round op1: the body walks out now (ScRuns.SendPhysicalTo's start, to the outpost).</summary>
        private static bool SetOut(World w, Member m, EntityAlive npc, Outpost o, int slot, EntityPlayerLocal player, out string error)
        {
            error = null;
            // the snapshot first: without one the registry could not carry them once they are out of sight, and a failed capture
            // must leave the NPC and the record exactly as they were
            string snap;
            if (!ScRunners.TryCapture(npc, out snap)) { error = "scInternal"; return false; }
            try
            {
                BeginJourney(m, o);
                m.Offline = false;
                m.Snap = snap;                                       // before BeginLeg: the snapshot is what makes them physical (the chunk-save veto)
                if (npc.Buffs.HasCustomVar("onMission") || !npc.enabled) ScNpc.SendOnMission(npc, false);
                ScRunners.ApplyTravelOrder(npc);
                ScNpc.RestoreSpeed(npc);
                if (npc.getNavigator() != null) npc.getNavigator().clearPath();
                if (npc.moveHelper != null) npc.moveHelper.Stop();
                ScRunners.BeginLeg(m, RunLeg.Outbound, npc.position, ArrivalPoint(w, o, slot), w.worldTime);
                ScRunners.OnSent(m, npc);
                ScRegistry.SaveAtomic();
                Told(player, "scOpSetsOff", m, o.Name);
                ScLog.Out(m.Name + " set off on foot from home for outpost " + o.Id + " at " + o.Name + " (" + Far(m) + " m, about " + EtaHours(m) + " h)");
                return true;
            }
            catch (Exception e)
            {
                ScLog.Once("garrison-post", e);
                // back to how they were: a resident at their post
                try
                {
                    npc.Buffs.RemoveCustomVar(ScRunners.OnRunCvar);
                    ScRunners.ClearRun(m);
                    m.OutpostId = -1;
                    ScCommunity.Station(m, npc);
                    ScRunners.RebuildRunnerSet();
                }
                catch (Exception e2) { ScLog.Once("garrison-post-undo", e2); }
                error = "scInternal";
                return false;
            }
        }

        /// <summary>Round op1: their body is not loaded - it stays parked at home and the legs run on the clock (TickJourneys).</summary>
        private static bool SetOutParked(World w, Member m, Outpost o, int slot, EntityPlayerLocal player, out string error)
        {
            error = null;
            try
            {
                Vector3 home = m.Post != Vector3i.zero ? ScCommunity.PostCenter(m) : ScRunners.C(ScZone.Home);
                BeginJourney(m, o);
                m.Snap = null;
                m.Offline = true;
                ScRunners.BeginLeg(m, RunLeg.Outbound, home, ArrivalPoint(w, o, slot), w.worldTime);
                ScRegistry.SaveAtomic();
                Told(player, "scOpSetsOff", m, o.Name);
                ScLog.Out(m.Name + " set off from home for outpost " + o.Id + " at " + o.Name + " (" + Far(m) + " m, about " + EtaHours(m)
                          + " h) - their body is not loaded: it waits parked at home and walks the road once it is");
                return true;
            }
            catch (Exception e)
            {
                ScLog.Once("garrison-post-parked", e);
                try
                {
                    ScRunners.ClearRun(m);
                    m.OutpostId = -1;
                    m.Status = MemberStatus.Resident;
                    ScRunners.RebuildRunnerSet();
                }
                catch (Exception e2) { ScLog.Once("garrison-post-parked-undo", e2); }
                error = "scInternal";
                return false;
            }
        }

        /// <summary>
        /// Round op1: where a traveler walks to - beside the claim, on the spot a stand-in of that slot stands (SpawnStandIn's), a
        /// standable one when the claim's chunks are loaded.
        /// </summary>
        private static Vector3 ArrivalPoint(World w, Outpost o, int slot)
        {
            Vector3i near = new Vector3i(o.Flag.x + (slot == 0 ? 2 : -2), o.Flag.y, o.Flag.z + 1);
            if (w != null && ScZone.IsLoaded(o.Flag)) return ScRadio.SpotNear(w, near, 4);
            return new Vector3(near.x + 0.5f, near.y, near.z + 0.5f);
        }

        // ------------------------------------------------------------------ arriving ----

        /// <summary>
        /// Round op1, ScRunners.OnlineTick: the walker reached the claim. Their body goes into the snapshot (it is them while they
        /// are posted) and, with you near enough to keep a stand-in, the stand-in steps in on the same spot. A capture that fails
        /// is tried again on the next second.
        /// </summary>
        internal static void ArriveOnline(World w, EntityPlayerLocal player, Member m, EntityAlive npc)
        {
            Outpost o = ScOutposts.ById(m.OutpostId);
            if (o == null || !o.Holding) { string err; TurnHome(m, player, "scOpTurnedBackGone", out err); return; }
            Vector3 at = npc.position;
            bool swap = player != null && ScOutposts.DistanceToBox(o, player.position) <= ScConfig.OutpostStandInRange * 1.8f && ScZone.IsLoaded(o.Flag);
            if (!ScRunners.GoOffline(w, m, npc, "reached " + o.Name, true)) return;
            TakeUpPost(m, o, player);
            if (swap && ScRegistry.GetMember(m.Id) == m && m.Status == MemberStatus.Garrison) SpawnStandIn(w, m, o, at);
        }

        /// <summary>Round op1, ScRunners.AdvanceOffline: the walk reached the claim out of your sight.</summary>
        internal static void ArriveOffline(EntityPlayerLocal player, Member m)
        {
            Outpost o = ScOutposts.ById(m.OutpostId);
            if (o == null || !o.Holding) { string err; TurnHome(m, player, "scOpTurnedBackGone", out err); return; }
            TakeUpPost(m, o, player);
        }

        /// <summary>Round op1: at the outpost - staff from now on (yield, security, stand-in); the snapshot, if any, is their body.</summary>
        private static void TakeUpPost(Member m, Outpost o, EntityPlayerLocal player)
        {
            bool embodied = !string.IsNullOrEmpty(m.Snap);
            ScRunners.ClearRun(m, true);                                                   // the legs end; the snapshot stays
            m.Status = MemberStatus.Garrison;
            m.OutpostId = o.Id;
            m.Offline = embodied;
            m.StandInId = -1;
            ScRunners.RebuildRunnerSet();
            ScRegistry.SaveAtomic();
            Told(player, "scOpArrived", m, o.Name);
            ScLog.Out(m.Name + " reached outpost " + o.Id + " at " + o.Name + " and holds it now" + (embodied ? "" : " (their body waits parked at home)"));
        }

        // ------------------------------------------------------------------ going home ----

        /// <summary>
        /// Home again, on foot (round op1): posted staff leave the outpost - their stand-in goes and the walk home starts beside the
        /// claim (on the stand-in's spot while it stands there); one on the way out turns round (TurnHome); one already walking
        /// home goes on. Resident only when they arrive. tellKey: scOpRecalled / scOpRecalledGone, or null for no message.
        /// </summary>
        public static void Recall(Member m, EntityPlayerLocal player, string tellKey)
        {
            if (m == null) return;
            if (m.Status == MemberStatus.Transit)
            {
                if (!HeadingOut(m)) return;
                string err;
                TurnHome(m, player, tellKey == null ? null : tellKey == "scOpRecalledGone" ? "scOpTurnedBackGone" : "scOpTurnedBack", out err);
                return;
            }
            if (m.Status != MemberStatus.Garrison) return;
            try
            {
                StartHome(m, player, tellKey);
            }
            catch (Exception e)
            {
                ScLog.Once("garrison-recall-" + m.Id, e);
                if (m.Status == MemberStatus.Garrison) HomeAtOnce(m, player, tellKey != null ? "scOpHomeFrom" : null);   // as before round op1
            }
        }

        private static void StartHome(Member m, EntityPlayerLocal player, string tellKey)
        {
            Outpost o = ScOutposts.ById(m.OutpostId);
            World w = ScCommunity.World;
            bool embodied = !string.IsNullOrEmpty(m.Snap);
            string name = o != null ? o.Name : "?";
            // where the walk starts: the stand-in's spot while it stands there (the real body steps in on it), else beside the claim
            EntityAlive standIn = LiveStandIn(w, m);
            Vector3 from;
            if (standIn != null) from = standIn.position;
            else if (o != null) from = ArrivalPoint(w, o, Math.Max(0, Staff(o).IndexOf(m)));
            else if (!embodied || !SnapPos(m, out from))
            {
                // no outpost record left and no snapshot to say where they were: their body waits parked at home - home at once
                HomeAtOnce(m, player, tellKey != null ? "scOpHomeFrom" : null);
                return;
            }
            if (w == null) { HomeAtOnce(m, player, tellKey != null ? "scOpHomeFrom" : null); return; }
            RemoveStandIn(m);
            m.Status = MemberStatus.Transit;
            ScStuck.ResetGain(m);
            m.RunDest = o != null ? o.Poi : Vector3i.zero;
            m.RunDestName = name;
            ResetRunFields(m);
            m.Offline = true;
            ScRunners.BeginLeg(m, RunLeg.Inbound, from, ScRunners.GatePoint(m, from, player), w.worldTime);
            if (embodied) ScRunners.OfflineNow(m, standIn != null);
            else TryEmbody(w, m, Time.unscaledTime, "called home from " + name);       // their body at home, loaded: they walk in for real
            ScRegistry.SaveAtomic();
            Told(player, tellKey, m, name);
            ScLog.Out(m.Name + " leaves outpost " + (o != null ? o.Id.ToString() : "?") + " at " + name + " and walks home (" + Far(m) + " m, about " + EtaHours(m) + " h"
                      + (m.Injured ? ", hurt" : "") + (Parked(m) ? ", parked: their body waits at home" : "") + ")");
        }

        /// <summary>Before round op1, and still when there is nowhere to walk from: home at once, un-hidden and stationed when loaded.</summary>
        private static void HomeAtOnce(Member m, EntityPlayerLocal player, string tellKey)
        {
            Outpost o = ScOutposts.ById(m.OutpostId);
            RemoveStandIn(m);
            string name = o != null ? o.Name : !string.IsNullOrEmpty(m.RunDestName) ? m.RunDestName : "?";
            ScRunners.ClearRun(m);
            m.Status = MemberStatus.Resident;
            m.OutpostId = -1;
            EntityAlive npc = ScCommunity.FindNpc(m.Id);
            if (npc != null)
            {
                ScNpc.SendOnMission(npc, false);
                ScCommunity.Station(m, npc);
            }
            ScRunners.RebuildRunnerSet();
            Told(player, tellKey, m, name);
            ScLog.Out(m.Name + " is home at once from " + name);
        }

        /// <summary>
        /// Round op1: one on the way to an outpost turns round from where they are (the walker, or the virtual walk) and heads for
        /// the gate. error = a localization key (scRunHeadingHome: already on the way home).
        /// </summary>
        public static bool TurnHome(Member m, EntityPlayerLocal player, string tellKey, out string error)
        {
            error = null;
            if (!ScCommunity.IsHost) { error = "scHostOnly"; return false; }
            if (m == null || m.Status != MemberStatus.Transit) { error = "scInternal"; return false; }
            if (m.RunLeg != RunLeg.Outbound) { error = "scRunHeadingHome"; return false; }
            World w = ScCommunity.World;
            if (w == null) { error = "scInternal"; return false; }
            Vector3 at = ScRunners.Where(m);
            ScRunners.BeginLeg(m, RunLeg.Inbound, at, ScRunners.GatePoint(m, at, player), w.worldTime);
            ScRunners.S(m.Id).HasWaypoint = false;
            EntityAlive npc = m.Offline ? null : ScCommunity.FindNpc(m.Id);
            if (npc != null && npc.getNavigator() != null) npc.getNavigator().clearPath();
            ScRegistry.SaveAtomic();
            Told(player, tellKey, m, TransitName(m));
            ScLog.Out(m.Name + " turned back for home before reaching " + TransitName(m) + " (" + Far(m) + " m from home)");
            return true;
        }

        /// <summary>Round op1, ScRunners.Arrive (and a parked walk's end): home from the outpost - said and logged.</summary>
        internal static void HomeAgain(Member m, EntityPlayerLocal player, string from)
        {
            string name = string.IsNullOrEmpty(from) ? "?" : from;
            Told(player, "scOpHomeFrom", m, name);
            ScLog.Out(m.Name + " is home from " + name + (m.Injured ? " (injured)" : ""));
        }

        /// <summary>Anyone posted to (or on the way to) an outpost that no longer exists or is lost heads home (rollover).</summary>
        public static void Validate(EntityPlayerLocal player)
        {
            var bad = new List<Member>();
            var turn = new List<Member>();
            foreach (var m in ScRegistry.Members.Values)
            {
                if (m.Status != MemberStatus.Garrison && !HeadingOut(m)) continue;
                Outpost o = ScOutposts.ById(m.OutpostId);
                if (o != null && o.Holding) continue;
                if (m.Status == MemberStatus.Garrison) bad.Add(m); else turn.Add(m);
            }
            foreach (Member m in bad) Recall(m, player, "scOpRecalledGone");
            foreach (Member m in turn) { string err; TurnHome(m, player, "scOpTurnedBackGone", out err); }
        }

        /// <summary>The outpost was disbanded or lost: everyone posted there heads home, and anyone on the way turns round.</summary>
        public static void OnOutpostGone(Outpost o, EntityPlayerLocal player)
        {
            foreach (Member m in Staff(o)) Recall(m, player, "scOpRecalledGone");
            TurnBack(o, player, "scOpTurnedBackGone");
        }

        /// <summary>Round op1: everyone on the way to this outpost turns round (overrun: its garrison falls back). tellKey may be null.</summary>
        public static void TurnBack(Outpost o, EntityPlayerLocal player, string tellKey)
        {
            foreach (Member m in OnTheWay(o)) { string err; TurnHome(m, player, tellKey, out err); }
        }

        // ------------------------------------------------------------------ the journey's clock (round op1) ----

        /// <summary>
        /// Round op1, every second: anyone heading for an outpost that is gone or lost turns round; a parked journey moves on along
        /// its legs on the world clock (an embodied one is ScRunners'); a parked body that is loaded is taken into a snapshot -
        /// a traveler's, and a posted member's - so from then on the member is where the registry says.
        /// </summary>
        private static void TickJourneys(World w, EntityPlayerLocal player, float t)
        {
            journeyScratch.Clear();
            foreach (var m in ScRegistry.Members.Values)
                if (m.Status == MemberStatus.Transit || (m.Status == MemberStatus.Garrison && string.IsNullOrEmpty(m.Snap))) journeyScratch.Add(m);
            if (journeyScratch.Count == 0) return;
            ulong now = w.worldTime;
            foreach (Member m in journeyScratch)
            {
                try
                {
                    if (ScRegistry.GetMember(m.Id) != m) continue;                       // left, died or re-keyed during this loop
                    if (m.Status == MemberStatus.Garrison)
                    {
                        // posted, body parked at home (an older build's posting, or parked on the way): into a snapshot once it loads
                        Outpost po = ScOutposts.ById(m.OutpostId);
                        TryEmbody(w, m, t, "posted at " + (po != null ? po.Name : "?"));
                        continue;
                    }
                    if (m.Status != MemberStatus.Transit) continue;
                    if (HeadingOut(m))
                    {
                        Outpost o = ScOutposts.ById(m.OutpostId);
                        if (o == null || !o.Holding) { string err; TurnHome(m, player, "scOpTurnedBackGone", out err); continue; }
                    }
                    if (ScRunners.IsPhysical(m)) continue;                               // embodied: ScRunners walks them
                    if (m.RunLeg == RunLeg.None) { HomeParked(m, player); continue; }    // never left on no leg
                    ScRunners.ClockBack(m, now);
                    if (TryEmbody(w, m, t, "on the road, " + m.RunLeg)) continue;        // a runner on the road from now on
                    switch (m.RunLeg)
                    {
                        case RunLeg.Outbound:
                            if (now >= m.LegEnd) ArriveOffline(player, m);
                            break;
                        case RunLeg.Inbound:
                            if (now >= m.LegEnd) ScRunners.EnterBack(player, m, now);     // the edge of the base: walks in once you are home
                            break;
                        case RunLeg.Back:
                            ParkedAtGate(w, player, m, t);
                            break;
                        default:
                            HomeParked(m, player);                                       // no other leg is theirs
                            break;
                    }
                }
                catch (Exception e)
                {
                    ScLog.Once("garrison-journey-" + m.Id, e);
                }
            }
            journeyScratch.Clear();
        }

        /// <summary>
        /// Round op1: a parked member's body, loaded and theirs (class and birth time, as the relink checks), into a snapshot. A
        /// capture that fails is tried again after EmbodyRetrySeconds. True when they are embodied now.
        /// </summary>
        private static bool TryEmbody(World w, Member m, float t, string why)
        {
            if (w == null || !Parked(m)) return false;
            float next;
            if (embodyRetry.TryGetValue(m.Id, out next) && t < next) return false;
            EntityAlive body = TheirBody(m);
            if (body == null) return false;
            if (ScRunners.Embody(w, m, body, why)) { embodyRetry.Remove(m.Id); return true; }
            embodyRetry[m.Id] = t + EmbodyRetrySeconds;
            return false;
        }

        /// <summary>
        /// Round op1: a parked member's own body, loaded (not one still being built) and alive - the class and birth time match, as
        /// the relink checks (a stranger who inherited the id is never theirs). Null otherwise.
        /// </summary>
        private static EntityAlive TheirBody(Member m)
        {
            EntityAlive body = ScCommunity.FindNpc(m.Id);
            if (body == null || body.IsDead()) return null;
            if (!string.IsNullOrEmpty(m.Class) && !string.Equals(ScCommunity.ClassName(body), m.Class, StringComparison.OrdinalIgnoreCase)) return null;
            if (m.Born != 0 && body.WorldTimeBorn != 0 && body.WorldTimeBorn != m.Born) return null;
            return body;
        }

        /// <summary>
        /// Round op1: a parked traveler at the edge of the base. Their body is at home: the moment it loads it is taken into a
        /// snapshot and they walk in (TickJourneys); a body that is loaded but could not be taken means they are simply home. With
        /// you home, their post loaded and no body of theirs anywhere for ScMissing's grace, they are home too - a resident with no
        /// body, whom ScMissing then lists for Call back.
        /// </summary>
        private static void ParkedAtGate(World w, EntityPlayerLocal player, Member m, float t)
        {
            if (TheirBody(m) != null) { absentSince.Remove(m.Id); HomeParked(m, player); return; }
            if (!ScRunners.PlayerNearPost(m, player) || !ScZone.IsLoaded(m.Post) || ScNpc.HasBody(w, m.Id)) { absentSince.Remove(m.Id); return; }
            float since;
            if (!absentSince.TryGetValue(m.Id, out since)) { absentSince[m.Id] = t; return; }
            if (t - since < ScMissing.GraceSeconds) return;
            absentSince.Remove(m.Id);
            ScLog.Warn(m.Name + " walked home parked, but no body of theirs is at home - a resident again (Call back, if they are missing)");
            HomeParked(m, player);
        }

        /// <summary>Round op1: a parked member's walk home ends - their body never left: a resident again, un-hidden and stationed when loaded.</summary>
        private static void HomeParked(Member m, EntityPlayerLocal player)
        {
            string from = m.RunDestName;
            ScRunners.ClearRun(m);
            m.Status = MemberStatus.Resident;
            m.OutpostId = -1;
            m.StandInId = -1;
            EntityAlive npc = TheirBody(m);
            if (npc != null)
            {
                ScNpc.SendOnMission(npc, false);
                ScCommunity.Station(m, npc);
            }
            ScRunners.RebuildRunnerSet();
            ScRegistry.SaveAtomic();
            HomeAgain(m, player, from);
        }

        // ------------------------------------------------------------------ text ----

        /// <summary>Round op1: the outpost a traveler walks to or comes from, by name.</summary>
        public static string TransitName(Member m)
        {
            if (m == null) return "?";
            if (!string.IsNullOrEmpty(m.RunDestName)) return m.RunDestName;
            Outpost o = ScOutposts.ById(m.OutpostId);
            return o != null ? o.Name : "?";
        }

        /// <summary>Round op1: whole game hours left on the leg, at least 1 while there is any way to go.</summary>
        public static int EtaHours(Member m)
        {
            return Math.Max(1, Mathf.CeilToInt(ScRunners.HoursLeft(m) - 0.001f));
        }

        /// <summary>Round op1: meters from home, where they are now.</summary>
        private static int Far(Member m)
        {
            return Mathf.RoundToInt(ScRunners.XZ(ScRunners.Where(m), ScCommunity.PostCenter(m)));
        }

        /// <summary>Round op1: the window's line for a traveler (the member detail's run line).</summary>
        public static string TransitLine(Member m)
        {
            if (m == null || m.Status != MemberStatus.Transit) return "";
            string name = TransitName(m);
            switch (m.RunLeg)
            {
                case RunLeg.Outbound: return string.Format(Localization.Get("scWinTransitOut"), name, EtaHours(m), Far(m));
                case RunLeg.Inbound: return string.Format(Localization.Get(m.Injured ? "scWinTransitHomeHurt" : "scWinTransitHome"), name, EtaHours(m), Far(m));
                default: return string.Format(Localization.Get("scWinTransitBack"), name, EtaHours(m), Far(m));
            }
        }

        /// <summary>Round op1: the Outposts tab's garrison slot for one on the way: "on the way to X, about N h".</summary>
        public static string SlotText(Member m)
        {
            return string.Format(Localization.Get("scUiStaffOnTheWay"), TransitName(m), EtaHours(m));
        }

        private static void Told(EntityPlayerLocal player, string key, Member m, string outpost)
        {
            if (player == null || string.IsNullOrEmpty(key) || m == null) return;
            ScCommunity.Tell(player, string.Format(Localization.Get(key), m.Name, outpost ?? "?", EtaHours(m)), "");
        }

        /// <summary>Round op1: where the snapshot was taken (an arrived member's last spot).</summary>
        private static bool SnapPos(Member m, out Vector3 pos)
        {
            pos = Vector3.zero;
            EntityCreationData ecd = ScRunners.Deserialize(m.Snap);
            if (ecd == null) return false;
            pos = ecd.pos;
            return true;
        }

        // ------------------------------------------------------------------ mood ----

        /// <summary>ScMorale.Recompute: a posted member's mood term.</summary>
        public static int MoodOffset(Member m)
        {
            if (m == null || m.Status != MemberStatus.Garrison) return 0;
            bool loner = false;
            foreach (string t in m.Traits) if (string.Equals(t, "Loner", StringComparison.OrdinalIgnoreCase)) { loner = true; break; }
            int v = loner ? 5 : -3;
            Outpost o = ScOutposts.ById(m.OutpostId);
            if (o != null && o.LastVisitDay >= 0 && ScCommunity.Today - o.LastVisitDay <= 3) v += 3;
            return v;
        }

        // ------------------------------------------------------------------ stand-ins ----

        /// <summary>
        /// ScTicker, every second: stand-ins at the outpost the player is at, visits stamped for moods. Round op1: first the
        /// journeys (TickJourneys), with or without outposts left.
        /// </summary>
        public static void Tick(World w, EntityPlayerLocal player)
        {
            try
            {
                if (w == null || player == null) return;
                SweepOrphans(w);
                float now = Time.unscaledTime;
                if (now < nextTick) return;
                nextTick = now + 1f;
                TickJourneys(w, player, now);
                if (ScRegistry.Outposts.Count == 0) return;
                int today = ScCommunity.Today;
                Vector3 pp = player.position;
                foreach (Outpost o in ScRegistry.Outposts)
                {
                    if (!o.Holding) continue;
                    float d = ScOutposts.DistanceToBox(o, pp);
                    bool near = d <= ScConfig.OutpostStandInRange;
                    if (near && o.LastVisitDay != today) { o.LastVisitDay = today; }
                    if (near && ScZone.IsLoaded(o.Flag)) Visit(w, player, o, now);
                    List<Member> staff = Staff(o);
                    if (staff.Count == 0) continue;
                    bool keep = d <= ScConfig.OutpostStandInRange * 1.8f && ScZone.IsLoaded(o.Flag);
                    foreach (Member m in staff)
                    {
                        if (m.StandInId >= 0)
                        {
                            // a stand-in still being built is neither forced nor taken for gone (that would spawn a second one)
                            if (ScNpc.IsPending(w, m.StandInId)) { ScNpc.PendingSkips++; continue; }
                            EntityAlive s = ScNpc.As(w.GetEntity(m.StandInId));
                            if (s == null || s.IsDead()) { if (s == null) m.StandInId = -1; continue; }
                            if (!keep) RemoveStandIn(m);
                            continue;
                        }
                        if (near && ScZone.IsLoaded(o.Flag)) SpawnStandIn(w, m, o, null);
                    }
                }
            }
            catch (Exception e)
            {
                ScLog.Once("garrison-tick", e);
            }
        }

        /// <summary>The player is at the outpost: recount what they built (every 10 s) and hand over any stash.</summary>
        private static void Visit(World w, EntityPlayerLocal player, Outpost o, float now)
        {
            float due;
            if (!nextCount.TryGetValue(o.Id, out due) || now >= due)
            {
                nextCount[o.Id] = now + 10f;
                int[] box = o.Box;
                if (ScScan.CountArea(box[0], box[1], box[2], box[3], countScratch))
                {
                    bool changed = false;
                    if (!o.HasInvestBase)
                    {
                        // established while unloaded (the console) or by an older build: this first count is the baseline
                        for (int i = 0; i < 4; i++) o.InvestBase[i] = countScratch[i];
                        o.HasInvestBase = true;
                        changed = true;
                    }
                    for (int i = 0; i < 4; i++)
                    {
                        int above = Math.Max(0, countScratch[i] - o.InvestBase[i]);
                        if (o.Invest[i] != above) { o.Invest[i] = above; changed = true; }
                    }
                    if (changed)
                    {
                        ScLog.Verbose("outpost " + o.Id + " at " + o.Name + ": " + o.Invest[0] + " bed(s), " + o.Invest[1] + " facility(ies), "
                                      + o.Invest[2] + " crop(s), " + o.Invest[3] + " defense(s)");
                        ScRegistry.SaveAtomic();
                    }
                }
            }
            if (o.StashTotal > 0 && ScOutpostPerks.Unstash(o, w, player) > 0) ScRegistry.SaveAtomic();
        }

        /// <summary>A staffer's stand-in beside the claim. spot (round op1): exactly there - where the walker who just arrived stood.</summary>
        private static void SpawnStandIn(World w, Member m, Outpost o, Vector3? spot)
        {
            try
            {
                if (!ScSpawn.ClassOf(m, out _)) return;
                Vector3 at;
                if (spot.HasValue) at = spot.Value;
                else
                {
                    int k = Staff(o).IndexOf(m);
                    Vector3i near = new Vector3i(o.Flag.x + (k == 0 ? 2 : -2), o.Flag.y, o.Flag.z + 1);
                    at = ScRadio.SpotNear(w, near, 4);
                }
                // the stand-in IS the survivor: their class, look and name (ScSpawn)
                EntityAlive s = ScSpawn.Body(w, m, at, Vector3.zero, true);
                if (s == null) return;
                // Dynamic + no Persist: SCore's IsSavedToFile (both classes) skips Dynamic (and Biome) sources, so a stand-in is
                // never written with the chunk - it goes when the outpost unloads (a StaticSpawner one would come back)
                s.SetSpawnerSource(EnumSpawnerSource.Dynamic);
                s.Buffs.SetCustomVar(ScRadio.StandInCvar, m.Id);
                s.Buffs.SetCustomVar(StandInOutpostCvar, o.Id);
                ScSkills.StampCvars(m, s);
                ScTraits.ApplyBuffs(m, s);
                s.Buffs.AddBuff("buffOrderGuard");
                m.StandInId = s.entityId;
                ScLog.Verbose("garrison stand-in for " + m.Name + " at " + o.Name + " (entity " + s.entityId + ")");
            }
            catch (Exception e)
            {
                ScLog.Once("garrison-standin", e);
            }
        }

        /// <summary>Round op1: the member's stand-in while it stands (not one still being built), or null.</summary>
        private static EntityAlive LiveStandIn(World w, Member m)
        {
            if (w == null || m == null || m.StandInId < 0 || ScNpc.IsPending(w, m.StandInId)) return null;
            EntityAlive s = ScNpc.As(w.GetEntity(m.StandInId));
            return s != null && !s.IsDead() ? s : null;
        }

        public static void RemoveStandIn(Member m)
        {
            try
            {
                if (m == null || m.StandInId < 0) return;
                World w = ScCommunity.World;
                EntityAlive s = w != null ? ScNpc.As(w.GetEntity(m.StandInId)) : null;
                if (s != null && !s.IsDead())
                {
                    ScAmmo.Record(m, s);          // the next stand-in starts with what this one had left (never a fresh stack)
                    ScRuns.RemoveEntity(s);
                }
                m.StandInId = -1;
            }
            catch (Exception e)
            {
                ScLog.Once("garrison-standin-remove", e);
            }
        }

        /// <summary>
        /// ScRadio.OnStandInDied for a posted member: the survivor fell defending the outpost. Round op1: a member whose body is
        /// the snapshot has none in the world (FindNpc says so): an old copy that loads later goes (Doomed).
        /// </summary>
        public static void OnStandInDied(Member m, EntityPlayerLocal player)
        {
            Outpost o = ScOutposts.ById(m.OutpostId);
            m.StandInId = -1;
            EntityAlive real = ScCommunity.FindNpc(m.Id);
            if (real != null) ScRuns.RemoveEntity(real); else ScRegistry.Doomed.Add(m.Id);
            m.Snap = null;
            ScCommunity.RecordDeath(m, player, "scOpStaffDied");
            ScLog.Out(m.Name + " died defending " + (o != null ? o.Name : "an outpost"));
        }

        // ------------------------------------------------------------------ workshops ----

        /// <summary>
        /// ScOutposts.Daily: an outpost with a crafter posted turns 2 materials into 1 ammo, one with a chemist
        /// 1 fuel + 1 materials into 1 meds - only out of surplus (the stockpile keeps OutpostWorkshopKeep).
        /// </summary>
        public static void Workshops(int day, EntityPlayerLocal player, StringBuilder summary)
        {
            if (!ScConfig.OutpostWorkshop) return;
            try
            {
                Community c = ScRegistry.Community;
                int keep = Math.Max(0, ScConfig.OutpostWorkshopKeep);
                foreach (Outpost o in ScRegistry.Outposts)
                {
                    if (o.State != OutpostState.Active) continue;
                    bool crafter = false, chemist = false;
                    foreach (Member m in Staff(o))
                    {
                        if (m.Specialty == Specialty.Craftsmanship) crafter = true;
                        if (m.Specialty == Specialty.Chemistry) chemist = true;
                    }
                    string made = null;
                    if (crafter && c.Get(Resource.Materials) >= keep + 2)
                    {
                        c.Add(Resource.Materials, -2);
                        c.Add(Resource.Ammo, 1);
                        made = string.Format(Localization.Get("scOpWorkshopAmmo"), o.Name);
                    }
                    else if (chemist && c.Get(Resource.Materials) >= keep + 1 && c.Get(Resource.Fuel) >= Math.Max(1, keep / 2) + 1)
                    {
                        c.Add(Resource.Materials, -1);
                        c.Add(Resource.Fuel, -1);
                        c.Add(Resource.Meds, 1);
                        made = string.Format(Localization.Get("scOpWorkshopMeds"), o.Name);
                    }
                    if (made == null) continue;
                    if (summary.Length > 0) summary.Append(' ');
                    summary.Append(made);
                    ScLog.Out("workshop: " + made);
                }
            }
            catch (Exception e)
            {
                ScLog.Once("garrison-workshop", e);
            }
        }

        // ------------------------------------------------------------------ walk-ins ----

        /// <summary>
        /// ScOutposts.Daily: each staffed outpost may draw a survivor in - a radio contact whose spawn is the
        /// outpost, so they are there the next time the player visits (the usual favour then applies).
        /// </summary>
        public static void WalkIns(int day, EntityPlayerLocal player, StringBuilder summary)
        {
            try
            {
                if (ScConfig.OutpostWalkInChance <= 0f || ScRegistry.Contacts.Count >= ScConfig.RadioMaxContacts) return;
                World w = ScCommunity.World;
                GameRandom rnd = w != null ? w.GetGameRandom() : null;
                foreach (Outpost o in ScRegistry.Outposts)
                {
                    if (o.State != OutpostState.Active || StaffCount(o) == 0) continue;
                    if (ScRegistry.Contacts.Count >= ScConfig.RadioMaxContacts) break;
                    bool taken = false;
                    foreach (var ct in ScRegistry.Contacts) if (ct.PoiKey == o.Poi) { taken = true; break; }
                    if (taken) continue;
                    float roll = rnd != null ? rnd.RandomFloat : UnityEngine.Random.value;
                    if (roll >= ScConfig.OutpostWalkInChance) continue;
                    Contact ct2 = ScRadio.AddContactAt(o.Poi, o.Name, new Vector3i(o.Flag.x + 3, o.Flag.y, o.Flag.z + 3), player, rnd);
                    if (ct2 == null) continue;
                    if (summary.Length > 0) summary.Append(' ');
                    summary.Append(string.Format(Localization.Get("scOpWalkIn"), ct2.Label, o.Name));
                    if (player != null) ScCommunity.Tell(player, string.Format(Localization.Get("scOpWalkInTip"), ct2.Label, o.Name), "");
                    ScLog.Out("walk-in: " + ct2.Class + " turned up at outpost " + o.Id + " (" + o.Name + ")");
                }
            }
            catch (Exception e)
            {
                ScLog.Once("garrison-walkin", e);
            }
        }

        /// <summary>The roster's status: "Holding X" / "At X"; round op1: "On the way to X" / "Walking home from X" while Transit.</summary>
        public static string StatusText(Member m, bool brief)
        {
            if (m != null && m.Status == MemberStatus.Transit)
            {
                string key = m.RunLeg == RunLeg.Outbound ? (brief ? "scStTransitOutShort" : "scStTransitOut") : (brief ? "scStTransitHomeShort" : "scStTransitHome");
                return string.Format(Localization.Get(key), TransitName(m));
            }
            Outpost o = PostOf(m);
            return string.Format(Localization.Get(brief ? "scStPostedShort" : "scStPosted"), o != null ? o.Name : "?");
        }
    }
}
