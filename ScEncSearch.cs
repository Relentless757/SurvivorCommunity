using System;
using System.Collections.Generic;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// E5, the missing survivor (E5_SPEC.md; Enclaves_Plan.md 2.10 kind 4, Q12 a; research missions.md 3.4). When the player
    /// takes the request on, one of the enclave's people - a member at home, never the leader - is missing, last seen at a real
    /// POI EnclaveMissingMin..EnclaveMissingMax m away (Prepare: the pickers' taken set both ways).
    ///
    /// Where it stands is the request's Stage. Unrolled: their fate is rolled the first time a player is within
    /// EnclaveSpawnRange of that building with its chunks settled - alive with EnclaveMissingAlive on the day it was accepted,
    /// EnclaveMissingAliveFade less a day after, never below EnclaveMissingAliveMin. Alive: their body (ScSpawn.Body from their
    /// record: ScEnclavePeople.SpawnAt) waits at an indoor spot of the building - a sleeper spawn point first, else E2's spot
    /// finder (ScEnclavePeople.SpotsIn) - on a Guard order, and comes and goes by E2's rules for bodies (Dynamic, the chunk-save
    /// veto, captured on unload, gone when nobody is near and nobody sees it, built again on the next approach). Following:
    /// "Your people sent me. Let's get you home." hires them to the player the way E3's enlisting does (ScEnclaveDeals.Hire: the
    /// follow order, Persist, the enlisted cvar) - no fee, no timer, and not counted against MaxFollowers (a rescue, not a
    /// hire: a full party must still be able to bring them home) - until they are inside the enclave's footprint: home again
    /// (ScEnclavePeople.Rehome), EnclaveGainMissingAlive and SurvivorOrigins' rescue event. Dead: their remains
    /// (BlockScEncRemains) lie at the spot, placed once its chunk is settled and put back if something erased them. Searched:
    /// the player searched the remains - the person is dead, the journal says "Return to &lt;enclave&gt;", and the leader's
    /// "I found &lt;name&gt;. I'm sorry." completes it (EnclaveGainMissingDead, the favour event).
    ///
    /// A body that goes while following (an unload, the shutdown, "Wait here.", a side-door dismiss) leaves them waiting where
    /// it stood; a death while following fails the request; a death before the player reached them leaves their remains where
    /// they fell. An expiry or a failure: they stay lost - marked dead, the remains (if any) taken out of the world (now, or
    /// once their chunk is next loaded: Community.EncRemains), a body still out let go as nobody's (SweepStrays). While they
    /// may be alive their group does not fall (AnyAlive: E2's fall rule waits for them; CheckFalls once they are lost).
    /// Withdrawn (the enclave turned hostile or fell, the console) or done from the console: one alive comes home, on foot when
    /// their body is out. Host only, main thread.
    /// </summary>
    public static class ScEncSearch
    {
        public const int StageUnrolled = 0, StageAlive = 1, StageFollowing = 2, StageDead = 3, StageSearched = 4;
        /// <summary>The remains block (Config/blocks.xml, BlockScEncRemains).</summary>
        public const string RemainsBlock = "scEncRemains";
        /// <summary>Spawns that fail in a row (a class that is gone) before the search stops trying this session.</summary>
        private const int MaxSpawnFailures = 3;
        /// <summary>The remains queue is looked at every few seconds, not every one (the strays every second while there are any).</summary>
        private const float SweepSeconds = 5f;
        /// <summary>A stray with no player within this many metres - talking reach - is taken out at once, seen or not (the review's L2).</summary>
        private const float StrayTalkReach = 5f;

        /// <summary>Session-only state per request.</summary>
        private sealed class Session
        {
            /// <summary>When the place first read settled (the roll and a spawn wait one full tick, as E2's do); -1 when it does not.</summary>
            public float SettledSince = -1f;
            /// <summary>"sc enclave request &lt;id&gt; fate alive|dead": the next roll's outcome.</summary>
            public bool? Forced;
            public int SpawnFailures;
            /// <summary>The building searched, looked up once.</summary>
            public PrefabInstance Pi;
            public bool PiLooked;
        }

        private struct Stray
        {
            public int Id;
            public ulong Born;
        }

        private static readonly Dictionary<int, Session> sessions = new Dictionary<int, Session>();
        /// <summary>Bodies that are nobody's any more (ForgetBody): taken out of the world by SweepStrays.</summary>
        private static readonly List<Stray> strays = new List<Stray>();
        /// <summary>The review's L1: groups whose fall rule is tested on the next tick (MarkDead, a load) - never inside a fall.</summary>
        private static readonly HashSet<int> fallChecks = new HashSet<int>();
        private static float nextSweep;
        private static int remainsType = -1;

        private static string Loc(string key) { return Localization.Get(key); }

        public static void Reset()
        {
            sessions.Clear();
            strays.Clear();
            fallChecks.Clear();
            nextSweep = 0f;
            remainsType = -1;
        }

        /// <summary>
        /// ScEncRequests.OnRegistryChanged, after the requests are squared: an accepted search whose person is gone or in a state
        /// its stage cannot have (a hand-edited save) is dropped (its journal quest closes at the first tick's pass); one still
        /// recorded as following the player (a crash - a clean quit made them wait where they stood, at the shutdown) waits at
        /// its last spot again (no body outlives a load; the hire of the body the save still names comes off the player at the
        /// first tick, as E3's do); a person recorded missing with no search for them is home again.
        /// </summary>
        public static void OnRegistryChanged()
        {
            Reset();
            // the review's L1: a group a session left with nobody alive (it ended before the next tick's test) falls now
            foreach (Enclave enc in ScEnclaves.All) fallChecks.Add(enc.Id);
            var named = new HashSet<int>();
            foreach (EnclaveRequest r in new List<EnclaveRequest>(ScRegistry.EncRequests))
            {
                if (r.Kind != EnclaveRequest.KindMissing || !r.IsAccepted) continue;
                EncPerson p = PersonById(r.PersonId);
                string bad = null;
                if (p == null || p.EnclaveId != r.EnclaveId) bad = "its person is gone";
                else if (r.Stage < StageUnrolled || r.Stage > StageSearched) bad = "an unknown stage " + r.Stage;
                else if (r.Stage == StageSearched) { if (p.State != EncPerson.StateDead) bad = "the remains were searched, but the person is not dead"; }
                else if (r.Stage == StageDead) { if (!p.Missing && p.State != EncPerson.StateDead) bad = "the person is neither missing nor dead"; }
                else if (!p.Missing) bad = "the person is not missing";
                if (bad != null)
                {
                    ScRegistry.EncRequests.Remove(r);
                    ScLog.Warn("enclave request #" + r.Id + " (missing survivor, enclave " + r.EnclaveId + "): " + bad + " - dropped");
                    continue;
                }
                named.Add(p.Id);
                if (r.Stage == StageFollowing)
                {
                    ScEnclaveDeals.AddStaleHire(r.EscortBody);
                    r.Stage = StageAlive;
                    ScLog.Out(p.Name + " (p" + p.Id + ") was following the player home when the game was saved - they wait at "
                              + (r.HasSpot ? r.Spot.ToString() : r.TargetName) + " again");
                }
                r.EscortBy = r.EscortBody = -1;
            }
            foreach (EncPerson p in ScRegistry.EncPeople)
            {
                if (!p.Missing || named.Contains(p.Id)) continue;
                p.State = EncPerson.StateHome;
                ScLog.Warn(p.Name + " (p" + p.Id + ", enclave " + p.EnclaveId + ") was recorded missing with no search for them - home again");
            }
        }

        // ------------------------------------------------------------------ lookups ----

        internal static EncPerson PersonById(int id)
        {
            if (id <= 0) return null;
            foreach (EncPerson p in ScRegistry.EncPeople) if (p.Id == id) return p;
            return null;
        }

        /// <summary>The open (accepted) search for this person, or null.</summary>
        internal static EnclaveRequest RequestOf(EncPerson p)
        {
            if (p == null) return null;
            foreach (EnclaveRequest r in ScRegistry.EncRequests)
                if (r.Kind == EnclaveRequest.KindMissing && r.IsAccepted && r.PersonId == p.Id) return r;
            return null;
        }

        /// <summary>
        /// The review's L1: one of theirs is missing and may be alive - their search open, the fate not rolled yet, waiting or
        /// following the player home, and the person still missing. E2's fall rule waits for them (ScEnclavePeople's Died and
        /// Generate): brought home they lead the group; lost or found dead they leave it with nobody (CheckFalls).
        /// </summary>
        internal static bool AnyAlive(Enclave e)
        {
            if (e == null) return false;
            foreach (EnclaveRequest r in ScRegistry.EncRequests)
            {
                if (r.Kind != EnclaveRequest.KindMissing || !r.IsAccepted || r.EnclaveId != e.Id || r.Stage > StageFollowing) continue;
                EncPerson p = PersonById(r.PersonId);
                if (p != null && p.Missing) return true;
            }
            return false;
        }

        /// <summary>
        /// The review's L1, the second half: the groups MarkDead queued (their missing survivor lost or found dead) and, after a
        /// load, every group - one with nobody alive any more and no missing survivor who may be falls (E2's rule and cause).
        /// Run from the tick, so never inside a fall already running (a search that a fall withdraws can end in MarkDead too).
        /// </summary>
        private static void CheckFalls()
        {
            if (fallChecks.Count == 0) return;
            var ids = new List<int>(fallChecks);
            fallChecks.Clear();
            foreach (int id in ids)
            {
                Enclave e = ScEnclaves.ById(id);
                if (e == null || ScEnclavePeople.Living(e).Count > 0 || AnyAlive(e)) continue;
                if (!e.PeopleRolled && ScEnclavePeople.PeopleOf(e).Count < ScEnclavePeople.GroupSize(e)) continue;
                ScLog.Out("enclave " + e.Id + " '" + e.Name + "': nobody of theirs is alive any more");
                ScEnclaves.Fall(e, "everyone died");
            }
        }

        /// <summary>The open search whose remains lie here (dead or searched), or null.</summary>
        private static EnclaveRequest RemainsAt(Vector3i pos)
        {
            foreach (EnclaveRequest r in ScRegistry.EncRequests)
                if (r.Kind == EnclaveRequest.KindMissing && r.IsAccepted && r.HasSpot && r.Spot == pos && (r.Stage == StageDead || r.Stage == StageSearched)) return r;
            return null;
        }

        /// <summary>The missing person's name for the texts ("someone" until they are picked at acceptance; "" for another kind).</summary>
        public static string PersonName(EnclaveRequest r)
        {
            if (r == null || r.Kind != EnclaveRequest.KindMissing) return "";
            EncPerson p = PersonById(r.PersonId);
            return p != null && !string.IsNullOrEmpty(p.Name) ? p.Name : Loc("scEncReqSomeone");
        }

        private static Session SessionOf(int requestId)
        {
            Session s;
            if (!sessions.TryGetValue(requestId, out s)) sessions[requestId] = s = new Session();
            return s;
        }

        /// <summary>The building searched (by its key), looked up once a session; null when it is not in this world.</summary>
        private static PrefabInstance TargetPoi(EnclaveRequest r)
        {
            Session s = SessionOf(r.Id);
            if (!s.PiLooked)
            {
                s.PiLooked = true;
                s.Pi = r.HasTarget ? ScSecurity.PoiByKey(r.Target) : null;
            }
            return s.Pi;
        }

        /// <summary>"missing: Mara (p12) last seen at Shotgun Messiah x,y,z - alive, waiting" - the log's and the console's form.</summary>
        public static string Describe(EnclaveRequest r)
        {
            EncPerson p = PersonById(r.PersonId);
            string who = p != null ? p.Name + " (p" + p.Id + ")" : "someone (picked when it is accepted)";
            if (!r.IsAccepted || !r.HasTarget) return "missing: " + who;
            return "missing: " + who + " last seen at " + r.TargetName + " " + r.Target + " - " + StageText(r) + (r.HasSpot ? ", spot " + r.Spot : "");
        }

        private static string StageText(EnclaveRequest r)
        {
            switch (r.Stage)
            {
                case StageAlive: return "alive, waiting (" + Mathf.RoundToInt(Chance(r) * 100f) + "% was today's chance)";
                case StageFollowing: return "alive, following the player home";
                case StageDead: return "dead, their remains not searched yet";
                case StageSearched: return "dead, their remains searched - tell the leader";
                default: return "not found yet (" + Mathf.RoundToInt(Chance(r) * 100f) + "% alive if found today)";
            }
        }

        // ------------------------------------------------------------------ the offer and acceptance ----

        /// <summary>Why this enclave cannot be given a missing survivor request (null when it can): someone at home besides the leader who could go missing, and a building to search.</summary>
        public static string CanOffer(Enclave e)
        {
            if (e == null) return "no enclave";
            if (Candidate(e, null) == null) return "nobody at home besides the leader could go missing";
            string no;
            return PickPoi(e, null, out no) == null ? no : null;
        }

        /// <summary>
        /// Who goes missing: a living member at home (never the leader, never one enlisted with the player) - one with no body out
        /// first, then one whose body nobody sees, then anyone; at random among them (the first without a random).
        /// </summary>
        private static EncPerson Candidate(Enclave e, GameRandom rnd)
        {
            World w = ScCommunity.World;
            var none = new List<EncPerson>();
            var unseen = new List<EncPerson>();
            var any = new List<EncPerson>();
            foreach (EncPerson p in ScRegistry.EncPeople)
            {
                if (p.EnclaveId != e.Id || p.State != EncPerson.StateHome || p.IsLeader) continue;
                EntityAlive npc = ScEnclavePeople.OwnBody(p);
                if (npc == null || npc.IsDead()) none.Add(p);
                else if (w != null && !ScEnclavePeople.SeenByAny(w, npc.position)) unseen.Add(p);
                else any.Add(p);
            }
            List<EncPerson> from = none.Count > 0 ? none : unseen.Count > 0 ? unseen : any;
            if (from.Count == 0) return null;
            return from[rnd != null ? rnd.RandomRange(0, from.Count) : 0];
        }

        /// <summary>
        /// The building they were last seen at: a real POI with sleeper areas whose centre is EnclaveMissingMin..EnclaveMissingMax m
        /// from the enclave's, never an enclave's, the home zone, an outpost, claimed, infested, quest-locked or in use (Taken). At
        /// random among them (the first without a random). Null (and why) when there is none.
        /// </summary>
        internal static PoiEntry PickPoi(Enclave e, GameRandom rnd, out string no)
        {
            no = null;
            World w = ScCommunity.World;
            if (w == null || !ScPoiIndex.EnsureBuilt()) { no = "no POI list on this world"; return null; }
            float lo = Math.Max(0, ScConfig.EnclaveMissingMin), hi = Math.Max(lo, ScConfig.EnclaveMissingMax);
            Vector3 c = e.Centre;
            var near = new List<PoiEntry>();
            ScPoiIndex.Near(c.x, c.z, hi, near);
            HashSet<Vector3i> taken = Taken();
            var ok = new List<PoiEntry>();
            foreach (PoiEntry pe in near)
            {
                float dx = pe.X - c.x, dz = pe.Z - c.z;
                if (dx * dx + dz * dz < lo * lo) continue;
                if (Refuse(w, pe, taken) == null) ok.Add(pe);
            }
            if (ok.Count == 0) { no = "no building to search " + Mathf.RoundToInt(lo) + "-" + Mathf.RoundToInt(hi) + " m from " + e.Name; return null; }
            return ok[rnd != null ? rnd.RandomRange(0, ok.Count) : 0];
        }

        /// <summary>Why this POI cannot be the building a missing survivor is searched for (null when it can).</summary>
        private static string Refuse(World w, PoiEntry pe, HashSet<Vector3i> taken)
        {
            if (pe == null || pe.Pi == null) return "no POI";
            if (pe.Sleepers <= 0) return "no sleeper areas";
            if (ScEnclaves.IsEnclavePoiKey(pe.Key)) return "an enclave's building";
            if (ScSecurity.OverlapsHome(pe.Pi)) return "the home zone";
            if (ScOutposts.IsOutpostPoiKey(pe.Key)) return "an outpost";
            if (ScInfestations.ByPoi(pe.Key) != null) return "infested";
            if (taken.Contains(pe.Key)) return "in use";
            if (pe.Pi.lockInstance != null && !pe.Pi.lockInstance.CheckQuestLock()) return "quest-locked";
            if (pe.Pi.CheckForAnyPlayerHome(w) != GameUtils.EPlayerHomeType.None) return "claimed";
            return null;
        }

        /// <summary>
        /// The POIs other tasks send someone to - the "in use" set of the enclaves' and infestations' pickers: radio contacts,
        /// open POI favours, physical runners' destinations, radio scouting marks, the player's quests in progress - and the open
        /// requests' own targets (ScEncRequests.AddTargets, which those pickers read in turn).
        /// </summary>
        private static HashSet<Vector3i> Taken()
        {
            var t = new HashSet<Vector3i>();
            foreach (Contact ct in ScRegistry.Contacts) t.Add(ct.PoiKey);
            foreach (Prospect p in ScRegistry.Prospects.Values) if (p.Favour == FavourKind.Poi && !p.Done) t.Add(p.PoiPos);
            foreach (Member m in ScRegistry.Members.Values) if (ScRunners.IsPhysical(m)) t.Add(m.RunDest);
            foreach (Scout s in ScRegistry.Community.Scouts) t.Add(s.Poi);
            ScEncRequests.AddTargets(t);
            EntityPlayerLocal qp = ScCommunity.LocalPlayer;
            if (qp != null && qp.QuestJournal != null && qp.QuestJournal.quests != null)
            {
                foreach (Quest q in qp.QuestJournal.quests)
                {
                    Vector3 qpos;
                    if (q != null && q.CurrentState == Quest.QuestState.InProgress && q.GetPositionData(out qpos, Quest.PositionDataTypes.POIPosition))
                        t.Add(World.worldToBlockPos(qpos));
                }
            }
            return t;
        }

        /// <summary>
        /// "Leave it with me." on a missing survivor's offer (ScEncRequests.Accept, before the record is accepted): who went
        /// missing (Candidate) and where they were last seen (PickPoi), named now. The person is missing from here on: a body of
        /// theirs goes now, seen or not (its health and rounds recorded); a favour they asked is off. False (and why) when there
        /// is nobody or nowhere: the offer is withdrawn.
        /// </summary>
        internal static bool Prepare(EnclaveRequest r, Enclave e, out string no)
        {
            no = null;
            World w = ScCommunity.World;
            GameRandom rnd = w != null ? w.GetGameRandom() : null;
            EncPerson p = Candidate(e, rnd);
            if (p == null) { no = "nobody at home besides the leader could go missing"; return false; }
            PoiEntry pe = PickPoi(e, rnd, out no);
            if (pe == null) return false;
            EntityAlive npc = ScEnclavePeople.OwnBody(p);
            p.State = EncPerson.StateMissing;
            // their body goes now, seen or not (the review's L2: one left standing as nobody's could be talked to, and hired
            // through another mod's dialog, while the records say they are missing)
            if (npc != null && !npc.IsDead()) ScEnclavePeople.Despawn(p, npc);
            ScEnclaveDeals.DropFavour(p, "they went missing");
            r.PersonId = p.Id;
            r.HasTarget = true;
            r.Target = pe.Key;
            r.TargetName = pe.Display ?? "";
            r.Stage = StageUnrolled;
            r.HasSpot = false;
            r.EscortBy = r.EscortBody = -1;
            sessions.Remove(r.Id);
            ScLog.Out("enclave " + e.Id + " '" + e.Name + "': " + p.Name + " (p" + p.Id + ") is missing, last seen at " + r.TargetName + " " + pe.Key + " ("
                      + Mathf.RoundToInt(Vector3.Distance(new Vector3(pe.X, 0f, pe.Z), new Vector3(e.Centre.x, 0f, e.Centre.z))) + " m away)");
            return true;
        }

        // ------------------------------------------------------------------ the 1 Hz part (ScEncRequests.Tick) ----

        /// <summary>
        /// Every second: a group whose missing survivor was its last hope falls (CheckFalls), strays go as soon as they may
        /// (SweepStrays), leftover remains every few seconds; then each open search - the fate rolled on the player's
        /// first settled arrival, the body of one waiting built when a player is near, the remains kept in place.
        /// </summary>
        public static void Tick(World w, EntityPlayerLocal player)
        {
            try
            {
                if (!ScEnclaves.Ready || w == null || player == null || !ScRegistry.Loaded || !ScConfig.Enclaves) return;
                float t = Time.unscaledTime;
                CheckFalls();                               // the review's L1
                if (strays.Count > 0) SweepStrays(w);       // the review's L2: every second while there are any
                if (t >= nextSweep)
                {
                    nextSweep = t + SweepSeconds;
                    SweepRemains(w);
                }
                bool changed = false;
                foreach (EnclaveRequest r in ScRegistry.EncRequests.ToArray())
                {
                    if (r.Kind != EnclaveRequest.KindMissing || !r.IsAccepted || !ScRegistry.EncRequests.Contains(r)) continue;
                    try
                    {
                        if (TickRequest(w, r, t)) changed = true;
                    }
                    catch (Exception ex)
                    {
                        ScLog.Once("encsearch-tick-" + r.Id, ex);
                    }
                }
                if (changed) ScRegistry.SaveAtomic();
            }
            catch (Exception e)
            {
                ScLog.Once("encsearch-tick", e);
            }
        }

        /// <summary>One search's second. True when its record changed.</summary>
        private static bool TickRequest(World w, EnclaveRequest r, float t)
        {
            Enclave e = ScEnclaves.ById(r.EnclaveId);
            EncPerson p = PersonById(r.PersonId);
            if (e == null || e.Lost || p == null) return false;       // the day's pass withdraws it
            switch (r.Stage)
            {
                case StageUnrolled: return TryRoll(w, e, r, p, t);
                case StageAlive: return TrySpawn(w, e, r, p, t);
                case StageDead: return KeepRemains(w, r);
                default: return false;          // following: the body's own tick (TickBody); searched: nothing left to keep
            }
        }

        /// <summary>
        /// The fate, rolled the first time a player is within EnclaveSpawnRange of the building with its chunks settled for a
        /// full tick (so a live body is never built where no one will see it for days): alive or dead (Chance; the console's
        /// forced fate first), and the spot they wait or lie at. The dead lie there now (KeepRemains).
        /// </summary>
        private static bool TryRoll(World w, Enclave e, EnclaveRequest r, EncPerson p, float t)
        {
            Session s = SessionOf(r.Id);
            PrefabInstance pi = TargetPoi(r);
            if (pi == null)
            {
                ScLog.Once("encsearch-nopoi-" + r.Id, "enclave request #" + r.Id + ": the building " + r.TargetName + " at " + r.Target
                           + " is not in this world - the search waits (it runs out on its due day)");
                return false;
            }
            Vector3i poi = pi.boundingBoxPosition, size = pi.boundingBoxSize;
            if (NearestToBox(w, poi, size) > ScConfig.EnclaveSpawnRange || !ScEnclavePeople.BoxSettled(w, poi, size)) { s.SettledSince = -1f; return false; }
            if (s.SettledSince < 0f) { s.SettledSince = t; return false; }
            float chance = Chance(r);
            bool forced = s.Forced.HasValue;
            bool alive = forced ? s.Forced.Value : w.GetGameRandom().RandomFloat < chance;
            s.Forced = null;
            s.SettledSince = -1f;
            Vector3i spot;
            float yaw;
            if (!FindSpot(w, pi, out spot, out yaw))
            {
                // nowhere to stand in the whole footprint: the centre's ground
                Vector3 c = new Vector3(poi.x + size.x * 0.5f, 0f, poi.z + size.z * 0.5f);
                int x = Mathf.FloorToInt(c.x), z = Mathf.FloorToInt(c.z), y;
                if (!ScCommunity.FindStandable(w, x, z, (int)w.GetHeightAt(c.x, c.z) + 1, out y)) y = (int)w.GetHeightAt(c.x, c.z) + 1;
                spot = new Vector3i(x, y, z);
                yaw = 0f;
            }
            r.HasSpot = true;
            r.Spot = spot;
            r.SpotYaw = yaw;
            r.Stage = alive ? StageAlive : StageDead;
            ScLog.Out("enclave " + e.Id + " '" + e.Name + "': " + p.Name + " (p" + p.Id + ") is found " + (alive ? "ALIVE" : "DEAD") + " at " + r.TargetName + ", spot " + spot
                      + (forced ? " (the console's fate)" : " (" + Mathf.RoundToInt(chance * 100f) + "% alive, " + Math.Max(0, ScEncRequests.Now - r.AcceptDay) + " day(s) after accepting)"));
            if (!alive) KeepRemains(w, r);
            ScEncRequests.Reshape(r);
            return true;
        }

        /// <summary>The chance they are alive: EnclaveMissingAlive on the day accepted, EnclaveMissingAliveFade less a day after, never below EnclaveMissingAliveMin.</summary>
        internal static float Chance(EnclaveRequest r)
        {
            int days = r.AcceptDay >= 0 ? Math.Max(0, ScEncRequests.Now - r.AcceptDay) : 0;
            return Mathf.Clamp01(Mathf.Max(ScConfig.EnclaveMissingAliveMin, ScConfig.EnclaveMissingAlive - ScConfig.EnclaveMissingAliveFade * days));
        }

        /// <summary>
        /// An indoor spot in the building: E2's spot finder over its footprint (ScEnclavePeople.SpotsIn: its sleeper spawn points,
        /// strict then relaxed, then the ground-floor scan) - under a roof first, then the best rank, at random among equals. With
        /// none, any ground to stand on in the footprint (ScRunners.WanderSpot). False when there is nowhere at all.
        /// </summary>
        private static bool FindSpot(World w, PrefabInstance pi, out Vector3i spot, out float yaw)
        {
            spot = Vector3i.zero;
            yaw = 0f;
            Vector3i poi = pi.boundingBoxPosition, size = pi.boundingBoxSize;
            var centre = new Vector3(poi.x + size.x * 0.5f, poi.y, poi.z + size.z * 0.5f);
            int fromSleepers;
            List<ScEnclavePeople.Spot> list = ScEnclavePeople.SpotsIn(w, pi, poi, size, centre, 6, out fromSleepers);
            var best = new List<ScEnclavePeople.Spot>();
            int bestKey = int.MaxValue;
            foreach (ScEnclavePeople.Spot s in list)
            {
                int key = (s.Roofed ? 0 : 10) + s.Rank;
                if (key < bestKey) { bestKey = key; best.Clear(); }
                if (key == bestKey) best.Add(s);
            }
            GameRandom rnd = w.GetGameRandom();
            if (best.Count > 0)
            {
                ScEnclavePeople.Spot s = best[rnd.RandomRange(0, best.Count)];
                spot = s.Pos;
                yaw = s.Yaw;
                return true;
            }
            Vector3 g;
            if (!ScRunners.WanderSpot(w, rnd, poi, size, out g)) return false;
            spot = World.worldToBlockPos(g);
            yaw = rnd.RandomFloat * 360f;
            return true;
        }

        /// <summary>
        /// The spot is no good any more (a block over it, its floor gone): the nearest place to stand in the same column, else a
        /// new spot in the building. False when neither is there.
        /// </summary>
        private static bool Respot(World w, EnclaveRequest r, string why)
        {
            Vector3i old = r.Spot;
            int y;
            if (ScCommunity.FindStandable(w, old.x, old.z, old.y, out y) && y != old.y)
            {
                r.Spot = new Vector3i(old.x, y, old.z);
            }
            else
            {
                PrefabInstance pi = TargetPoi(r);
                Vector3i s;
                float yaw;
                if (pi == null || !FindSpot(w, pi, out s, out yaw) || s == old) return false;
                r.Spot = s;
                r.SpotYaw = yaw;
            }
            ScLog.Out("enclave request #" + r.Id + ": the spot " + old + " is " + why + " - now " + r.Spot);
            return true;
        }

        /// <summary>
        /// One found alive and waiting, with no body out: built (ScEnclavePeople.SpawnAt) at their spot once a player is within
        /// EnclaveSpawnRange of it, its chunks settled for a full tick, never popping up in a player's view; a Guard order there.
        /// </summary>
        private static bool TrySpawn(World w, Enclave e, EnclaveRequest r, EncPerson p, float t)
        {
            if (p.EntityId >= 0 || !r.HasSpot) return false;          // out: TickBody watches it
            Session s = SessionOf(r.Id);
            Vector3 at = SpotCentre(r);
            if (NearestXZ(w, at) > ScConfig.EnclaveSpawnRange || !ScRunners.Settled(w, at)) { s.SettledSince = -1f; return false; }
            if (s.SettledSince < 0f) { s.SettledSince = t; return false; }
            if (s.SpawnFailures >= MaxSpawnFailures) return false;
            if (p.LastEntityId >= 0 && ScNpc.IsPending(w, p.LastEntityId)) return false;     // a copy of their last body still being built
            bool changed = false;
            string why;
            if (!ScCommunity.GoodPost(w, r.Spot, false, out why))
            {
                if (!Respot(w, r, why)) return false;
                at = SpotCentre(r);
                changed = true;
            }
            if (ScEnclavePeople.PopsInView(w, at)) return changed;
            EntityAlive npc = ScEnclavePeople.SpawnAt(w, e, p, at, r.SpotYaw);
            if (npc == null)
            {
                s.SpawnFailures++;
                ScLog.Once("encsearch-nobody-" + p.Id, p.Name + " of " + e.Name + ": no body for the class " + p.Who.Class + " (gone from the game?) - they are not seen this session");
                return changed;
            }
            ScEnclavePeople.GuardAt(npc, at, LookPoint(r));
            ScLog.Verbose(p.Name + " of " + e.Name + " waits at " + r.Spot + " in " + r.TargetName + " (entity " + npc.entityId + ")");
            return true;
        }

        /// <summary>
        /// ScEnclavePeople's tick for a missing person's body: one waiting goes by E2's rule (no player within EnclaveSpawnRange +
        /// EnclaveDespawnMargin, nobody sees it: its health and rounds recorded); one following is the escort's (TickEscort). A
        /// body whose search is over is let go as nobody's. True when a record changed.
        /// </summary>
        internal static bool TickBody(World w, Enclave e, EncPerson p, EntityAlive npc)
        {
            EnclaveRequest r = RequestOf(p);
            if (r == null || (r.Stage != StageAlive && r.Stage != StageFollowing))
            {
                ScEnclavePeople.ForgetBody(p);
                LetGo(npc);
                ScLog.Warn(p.Name + " of " + e.Name + ": a body out with no search waiting for it - let go");
                return true;
            }
            if (r.Stage == StageFollowing) return TickEscort(w, e, r, p, npc);
            if (NearestXZ(w, npc.position) > ScConfig.EnclaveSpawnRange + ScConfig.EnclaveDespawnMargin && !ScEnclavePeople.SeenByAny(w, npc.position))
            {
                ScEnclavePeople.Despawn(p, npc);
                return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ found alive: the way home ----

        /// <summary>
        /// The dialog's helper: the NPC being talked to is a missing person's body, and their search (any stage). Host only.
        /// </summary>
        public static bool Talking(EntityPlayer player, EntityNPC talkingTo, out EntityAlive npc, out EncPerson p, out EnclaveRequest r)
        {
            npc = null;
            p = null;
            r = null;
            if (!ScCommunity.IsHost) return false;
            npc = talkingTo != null ? (EntityAlive)talkingTo : ScFavours.CurrentNpc(player);
            p = ScEnclavePeople.PersonOf(npc);
            if (p == null || !p.Missing || npc.WorldTimeBorn != p.EntityBorn) return false;
            r = RequestOf(p);
            return r != null;
        }

        /// <summary>
        /// "Your people sent me. Let's get you home.": they follow the player (ScEnclaveDeals.Hire, no fee, no timer, not counted
        /// against MaxFollowers) until they are inside their enclave's footprint. Host only; checked again.
        /// </summary>
        public static void ActFollow(EntityPlayer player)
        {
            EntityAlive npc;
            EncPerson p;
            EnclaveRequest r;
            if (!Talking(player, null, out npc, out p, out r) || r.Stage != StageAlive || npc.IsDead() || player == null) return;
            Enclave e = ScEnclaves.ById(r.EnclaveId);
            if (e == null) return;
            ScEnclaveDeals.Hire(npc, player);
            r.Stage = StageFollowing;
            r.EscortBy = player.entityId;
            r.EscortBody = npc.entityId;
            ScCommunity.ResetStill(p.Who);
            ScCommunity.Tell(player, string.Format(Loc("scEncSearchFollowTell"), p.Name, e.Name), "");
            ScEncRequests.Reshape(r);
            ScRegistry.SaveAtomic();
            ScLog.Out(p.Name + " (p" + p.Id + ", entity " + npc.entityId + ") of " + e.Name + " follows " + player.EntityName + " home");
        }

        /// <summary>"Wait here for me.": they stop following and wait where they stand (StopEscort). Host only; checked again.</summary>
        public static void ActWait(EntityPlayer player)
        {
            EntityAlive npc;
            EncPerson p;
            EnclaveRequest r;
            if (!Talking(player, null, out npc, out p, out r) || r.Stage != StageFollowing) return;
            StopEscort(r, p, npc, player, "the player told them to wait", "scEncSearchWaitTell");
            ScRegistry.SaveAtomic();
        }

        /// <summary>
        /// The escort, every second: home once inside the enclave's footprint (Home); the player gone from the world, or the
        /// follow taken off them through a side door (SCore's companion window, any order that drops the leader) - they wait
        /// where they stand. True when the record changed.
        /// </summary>
        private static bool TickEscort(World w, Enclave e, EnclaveRequest r, EncPerson p, EntityAlive npc)
        {
            EntityPlayer pl = r.EscortBy > 0 ? w.GetEntity(r.EscortBy) as EntityPlayer : null;
            if (pl == null) { StopEscort(r, p, npc, null, "the player who led them is gone", null); return true; }
            if ((int)npc.Buffs.GetCustomVar("Leader") != r.EscortBy) { StopEscort(r, p, npc, pl, "the follow was dropped through a side door (no Leader)", "scEncSearchStopTell"); return true; }
            Vector3 pos = npc.position;
            if (!e.Contains(Mathf.FloorToInt(pos.x), Mathf.FloorToInt(pos.z))) return false;
            Home(e, r, p, npc, pl);
            return true;
        }

        /// <summary>
        /// Inside the footprint (Q12 a): the hire undone, home again (state home, their spot, the Guard order and the SCEnclave
        /// package: ScEnclavePeople.Rehome), and the request done - EnclaveGainMissingAlive, SurvivorOrigins' rescue event, the
        /// leader's thanks owed for the next talk.
        /// </summary>
        private static void Home(Enclave e, EnclaveRequest r, EncPerson p, EntityAlive npc, EntityPlayer pl)
        {
            ScEnclaveDeals.Unhire(npc, pl, npc.entityId);
            r.EscortBy = r.EscortBody = -1;
            p.State = EncPerson.StateHome;
            ScEnclavePeople.Rehome(e, p, npc);
            if (pl != null) ScCommunity.Tell(pl, string.Format(Loc("scEncSearchHomeTell"), p.Name, e.Name), "");
            ScLog.Out(p.Name + " (p" + p.Id + ") is home at " + e.Name + " - brought back by " + (pl != null ? pl.EntityName : "the player"));
            ScEncRequests.Close(r, ScEncRequests.Done, p.Name + " is home", ScEncRequests.GainFor(r), p.Name, ScEncRequests.EventFor(r));
            e.Thanks = EnclaveRequest.KindMissing;      // the leader thanks the player at the next talk
            ScRegistry.SaveAtomic();
        }

        /// <summary>
        /// The escort ends with them still missing: the hire undone (the body's side and the player's), and they wait where the
        /// body stands - their new spot, a Guard order there - until the player comes back for them. tellKey: the player's
        /// tooltip ({0} the name), or null for none.
        /// </summary>
        private static void StopEscort(EnclaveRequest r, EncPerson p, EntityAlive npc, EntityPlayer pl, string why, string tellKey)
        {
            ScEnclaveDeals.Unhire(npc, pl, npc != null ? npc.entityId : r.EscortBody);
            r.EscortBy = r.EscortBody = -1;
            r.Stage = StageAlive;
            if (npc != null)
            {
                Mark(r, npc);
                ScEnclavePeople.GuardAt(npc, SpotCentre(r), LookPoint(r));
            }
            if (tellKey != null && pl != null) ScCommunity.Tell(pl, string.Format(Loc(tellKey), p.Name), "");
            ScEncRequests.Reshape(r);
            ScLog.Out(p.Name + " (p" + p.Id + ") stopped following (" + why + ") - they wait at " + r.Spot);
        }

        /// <summary>Where a body stands becomes their spot.</summary>
        private static void Mark(EnclaveRequest r, EntityAlive npc)
        {
            r.HasSpot = true;
            r.Spot = World.worldToBlockPos(npc.position);
            r.SpotYaw = npc.rotation.y;
        }

        // ------------------------------------------------------------------ bodies going and dying (ScEnclavePeople) ----

        /// <summary>
        /// ScEnclavePeople.Unbody, for a missing person: a body that was following the player has its hire undone (the player's
        /// side too, the body being gone); one that went alive (an unload, the shutdown, gone from the world) leaves them waiting
        /// where it stood. A dead one leaves the rest to OnDied. own: the body while it is still there, else null.
        /// </summary>
        internal static void OnUnbodied(EncPerson p, EntityAlive own)
        {
            EnclaveRequest r = RequestOf(p);
            if (r == null || r.Stage != StageFollowing) return;
            World w = ScCommunity.World;
            EntityPlayer pl = r.EscortBy > 0 && w != null ? w.GetEntity(r.EscortBy) as EntityPlayer : null;
            ScEnclaveDeals.Unhire(own, pl, own != null ? own.entityId : r.EscortBody);
            r.EscortBy = r.EscortBody = -1;
            if (own != null && own.IsDead()) return;
            r.Stage = StageAlive;
            if (own != null) Mark(r, own);
            ScEncRequests.Reshape(r);
            ScLog.Out(p.Name + " (p" + p.Id + ") was following the player, but their body went - they wait at " + (r.HasSpot ? r.Spot.ToString() : r.TargetName));
        }

        /// <summary>
        /// ScEnclavePeople.OnBodyDied, after the death is recorded: following the player home, the request fails (Q12 a); found
        /// but not yet on the way, their remains lie where they fell (dead: search them, tell the leader). at: where the body died.
        /// </summary>
        internal static void OnDied(EncPerson p, Vector3 at)
        {
            EnclaveRequest r = RequestOf(p);
            if (r == null) return;
            if (r.Stage == StageFollowing)
            {
                ScEncRequests.Close(r, ScEncRequests.Failed, p.Name + " died on the way home");
                return;
            }
            r.Stage = StageDead;
            r.HasSpot = true;
            r.Spot = World.worldToBlockPos(at);
            World w = ScCommunity.World;
            if (w != null) KeepRemains(w, r);
            ScEncRequests.Reshape(r);
            ScLog.Out(p.Name + " (p" + p.Id + ") died before the player reached them - their remains lie at " + r.Spot);
            ScRegistry.SaveAtomic();
        }

        // ------------------------------------------------------------------ the remains ----

        private static int RemainsType()
        {
            if (remainsType < 0)
            {
                Block b = Block.GetBlockByName(RemainsBlock);
                remainsType = b != null ? b.blockID : 0;
            }
            return remainsType;
        }

        private static bool IsRemains(BlockValue bv)
        {
            int t = RemainsType();
            return t > 0 && bv.type == t;
        }

        /// <summary>
        /// The review's L3: the remains block is not loaded (Config/blocks.xml trimmed, a rename) - nothing can be placed or
        /// searched, so the remains count as searched at once: dead in the records, the journal's "Tell &lt;enclave&gt;", a
        /// tell and a ledger line, and the request can still be told. Logged once. True: the record changed.
        /// </summary>
        private static bool NoRemainsBlock(EnclaveRequest r)
        {
            ScLog.Once("encsearch-noblock", "the block " + RemainsBlock + " is not loaded (Config/blocks.xml) - a missing survivor found dead counts as searched at once");
            EncPerson p = PersonById(r.PersonId);
            Enclave e = ScEnclaves.ById(r.EnclaveId);
            string name = p != null ? p.Name : PersonName(r), enc = e != null ? e.Name : "";
            r.Stage = StageSearched;
            if (p != null && p.State != EncPerson.StateDead) MarkDead(p);
            EntityPlayerLocal pl = ScCommunity.LocalPlayer;
            if (pl != null) ScCommunity.Tell(pl, string.Format(Loc("scEncRemainsFound"), name, enc), "");
            ScOutposts.NoteLedger(string.Format(Loc("scEncSearchLedgerFound"), name, enc, r.TargetName));
            ScEncRequests.Reshape(r);
            ScLog.Out("enclave request #" + r.Id + ": " + name + " was found dead - with no remains block they count as searched; tell " + enc);
            return true;
        }

        /// <summary>
        /// The remains at the spot of a search found dead, once its chunk is settled: there already - nothing; else placed (the
        /// cell free over a floor, or the nearest such place: Respot) - after a quest's reset of the building erased them, too.
        /// True when the record changed (a new spot).
        /// </summary>
        private static bool KeepRemains(World w, EnclaveRequest r)
        {
            if (!r.HasSpot) return false;
            if (RemainsType() <= 0) return NoRemainsBlock(r);       // the review's L3
            Vector3 c = SpotCentre(r);
            if (!ScRunners.Settled(w, c)) return false;
            if (IsRemains(w.GetBlock(r.Spot))) return false;
            bool changed = false;
            string why;
            if (!ScCommunity.GoodPost(w, r.Spot, false, out why))
            {
                if (!Respot(w, r, why)) return false;
                changed = true;
                if (!ScRunners.Settled(w, SpotCentre(r)) || !ScCommunity.GoodPost(w, r.Spot, false, out why)) return true;
            }
            BlockValue bv = Block.GetBlockValue(RemainsBlock);
            if (bv.isair) return changed;
            bv.rotation = (byte)(Mathf.RoundToInt(Mathf.Repeat(r.SpotYaw, 360f) / 90f) & 3);
            w.SetBlockRPC(r.Spot, bv);
            ScLog.Out("enclave request #" + r.Id + ": " + PersonName(r) + "'s remains placed at " + r.Spot);
            return changed;
        }

        /// <summary>
        /// "Search the body" (BlockScEncRemains): the open search whose remains lie here - the person is dead (when they were not
        /// already), the journal turns to "Return to &lt;enclave&gt;" (Searched), a tell and a ledger line. Remains no open search
        /// owns (a world saved ahead of the registry) are taken away. Host only.
        /// </summary>
        public static void SearchAt(Vector3i pos, EntityPlayerLocal player)
        {
            if (!ScCommunity.IsHost || !ScRegistry.Loaded) return;
            EnclaveRequest r = RemainsAt(pos);
            World w = ScCommunity.World;
            if (r == null)
            {
                if (w != null && IsRemains(w.GetBlock(pos))) w.SetBlockRPC(pos, BlockValue.Air);
                if (player != null) ScCommunity.Tell(player, Loc("scEncRemainsNothing"), "");
                ScLog.Out("remains at " + pos + " belong to no open search - taken away");
                return;
            }
            EncPerson p = PersonById(r.PersonId);
            Enclave e = ScEnclaves.ById(r.EnclaveId);
            string name = p != null ? p.Name : PersonName(r), enc = e != null ? e.Name : "";
            if (r.Stage == StageSearched)
            {
                if (player != null) ScCommunity.Tell(player, string.Format(Loc("scEncRemainsSearched"), name, enc), "");
                return;
            }
            r.Stage = StageSearched;
            if (p != null && p.State != EncPerson.StateDead) MarkDead(p);
            if (player != null) ScCommunity.Tell(player, string.Format(Loc("scEncRemainsFound"), name, enc), "");
            ScOutposts.NoteLedger(string.Format(Loc("scEncSearchLedgerFound"), name, enc, r.TargetName));
            ScEncRequests.Reshape(r);
            ScRegistry.SaveAtomic();
            ScLog.Out("enclave request #" + r.Id + ": the player searched " + name + "'s remains at " + pos + " - tell " + enc);
        }

        /// <summary>The remains of a search that ended go: now when their chunk is settled, else once it is (Community.EncRemains).</summary>
        private static void RemoveRemains(EnclaveRequest r)
        {
            if (!r.HasSpot) return;
            World w = ScCommunity.World;
            Vector3i pos = r.Spot;
            if (w != null && ScRunners.Settled(w, SpotCentre(r)))
            {
                if (IsRemains(w.GetBlock(pos)))
                {
                    w.SetBlockRPC(pos, BlockValue.Air);
                    ScLog.Out("enclave request #" + r.Id + ": the remains at " + pos + " taken away");
                }
                return;
            }
            List<Vector3i> q = ScRegistry.Community.EncRemains;
            if (!q.Contains(pos)) q.Add(pos);
        }

        /// <summary>Remains left behind by a search that ended while their chunk was away: taken out once it is settled (never another open search's).</summary>
        private static void SweepRemains(World w)
        {
            List<Vector3i> q = ScRegistry.Community.EncRemains;
            if (q.Count == 0) return;
            bool changed = false;
            for (int i = q.Count - 1; i >= 0; i--)
            {
                Vector3i pos = q[i];
                if (!ScRunners.Settled(w, new Vector3(pos.x + 0.5f, pos.y, pos.z + 0.5f))) continue;
                if (RemainsAt(pos) == null && IsRemains(w.GetBlock(pos)))
                {
                    w.SetBlockRPC(pos, BlockValue.Air);
                    ScLog.Out("remains at " + pos + " taken away (their search ended while the chunk was not loaded)");
                }
                q.RemoveAt(i);
                changed = true;
            }
            if (changed) ScRegistry.SaveAtomic();
        }

        // ------------------------------------------------------------------ endings (ScEncRequests.Close) ----

        /// <summary>
        /// A search ended (ScEncRequests.Close, after the record left the list): its remains go; a person still missing comes
        /// home when it was done or withdrawn while they were alive (on foot when their body is out: ComeHome), else stays lost -
        /// marked dead, a body still out let go as nobody's (Lost). One brought home or searched is settled already.
        /// </summary>
        internal static void OnClosed(EnclaveRequest r, string outcome)
        {
            sessions.Remove(r.Id);
            if (r.Stage == StageDead || r.Stage == StageSearched) RemoveRemains(r);
            EncPerson p = PersonById(r.PersonId);
            if (p == null || !p.Missing) return;
            Enclave e = ScEnclaves.ById(r.EnclaveId);
            bool alive = r.Stage == StageUnrolled || r.Stage == StageAlive || r.Stage == StageFollowing;
            if (alive && (outcome == ScEncRequests.Done || outcome == ScEncRequests.Withdrawn)) ComeHome(e, r, p);
            else Lost(e, r, p, outcome);
        }

        /// <summary>
        /// Home again with the request over: the escort's hire undone, state home; a body out walks home (E3's walk:
        /// ScEnclaveDeals.StartWalkHome, the SCEnclave package back on it) - else they are at their spot the next time.
        /// </summary>
        private static void ComeHome(Enclave e, EnclaveRequest r, EncPerson p)
        {
            World w = ScCommunity.World;
            EntityAlive npc = ScEnclavePeople.OwnBody(p);
            if (r.Stage == StageFollowing || r.EscortBody >= 0)
            {
                EntityPlayer pl = r.EscortBy > 0 && w != null ? w.GetEntity(r.EscortBy) as EntityPlayer : null;
                ScEnclaveDeals.Unhire(npc, pl, npc != null ? npc.entityId : r.EscortBody);
            }
            r.EscortBy = r.EscortBody = -1;
            p.State = EncPerson.StateHome;
            bool walking = npc != null && !npc.IsDead() && e != null && w != null;
            if (walking)
            {
                ScEnclavePeople.AddPackage(npc);
                ScEnclaveDeals.StartWalkHome(w, e, p, npc);
            }
            ScLog.Out(p.Name + " (p" + p.Id + ") of " + (e != null ? e.Name : "enclave " + p.EnclaveId) + " is home again" + (walking ? " (walking back)" : ""));
        }

        /// <summary>
        /// Never brought home (an expiry, a failure): marked dead, a body still out let go as nobody's (SweepStrays), a ledger line.
        /// Done here means found dead and told without the remains being searched (only the console's "complete" does that):
        /// dead in the records with no "never brought home" line - the request's own ledger line says it.
        /// </summary>
        private static void Lost(Enclave e, EnclaveRequest r, EncPerson p, string outcome)
        {
            World w = ScCommunity.World;
            EntityAlive npc = ScEnclavePeople.OwnBody(p);
            if (r.Stage == StageFollowing || r.EscortBody >= 0)
            {
                EntityPlayer pl = r.EscortBy > 0 && w != null ? w.GetEntity(r.EscortBy) as EntityPlayer : null;
                ScEnclaveDeals.Unhire(npc, pl, npc != null ? npc.entityId : r.EscortBody);
            }
            r.EscortBy = r.EscortBody = -1;
            if (p.EntityId >= 0)
            {
                ScEnclavePeople.ForgetBody(p);
                if (npc != null && !npc.IsDead()) LetGo(npc);
            }
            MarkDead(p);
            if (outcome == ScEncRequests.Done)
            {
                ScLog.Out(p.Name + " (p" + p.Id + ") was found dead - the request is done");
                return;
            }
            if (e != null) ScOutposts.NoteLedger(string.Format(Loc("scEncSearchLedgerLost"), p.Name, e.Name));
            ScLog.Out(p.Name + " (p" + p.Id + ") was never brought home (" + outcome + ") - they are lost");
        }

        /// <summary>
        /// The person is dead in the records (found dead, or never brought home): not one of the living, a favour of theirs off,
        /// their group's fall rule tested on the next tick (the review's L1: with nobody else alive the group is gone).
        /// </summary>
        private static void MarkDead(EncPerson p)
        {
            p.State = EncPerson.StateDead;
            p.DiedDay = ScCommunity.Today;
            p.Health = 0f;
            p.Role = EncPerson.MemberRole;
            ScEncMoves.NoteLoss(ScEnclaves.ById(p.EnclaveId));      // E11: the day of their last death (request clock)
            ScEnclaveDeals.DropFavour(p, "they are dead");
            fallChecks.Add(p.EnclaveId);
        }

        // ------------------------------------------------------------------ strays ----

        /// <summary>A body that is nobody's any more (ForgetBody first): taken out of the world by SweepStrays. Never saved meanwhile (the veto).</summary>
        private static void LetGo(EntityAlive npc)
        {
            if (npc == null) return;
            strays.Add(new Stray { Id = npc.entityId, Born = npc.WorldTimeBorn });
        }

        /// <summary>
        /// The strays, every second while there are any (the review's L2: nobody's body must not stay someone to talk to, or to
        /// hire through another mod's dialog): one hired meanwhile (a Leader cvar) goes at once, its hire undone; one with no
        /// player within StrayTalkReach goes at once, seen or not; one a player stands next to goes once nobody sees it.
        /// </summary>
        private static void SweepStrays(World w)
        {
            for (int i = strays.Count - 1; i >= 0; i--)
            {
                Stray s = strays[i];
                EntityAlive npc = ScNpc.As(ScNpc.Loaded(w, s.Id));
                if (npc == null || npc.WorldTimeBorn != s.Born || npc.IsDead() || ScEnclavePeople.PersonOf(npc) != null) { strays.RemoveAt(i); continue; }
                int leader = npc.Buffs != null ? (int)npc.Buffs.GetCustomVar("Leader") : 0;
                if (leader > 0)
                {
                    ScEnclaveDeals.Unhire(npc, w.GetEntity(leader) as EntityPlayer, npc.entityId);
                    ScLog.Out("a body that is nobody's any more (entity " + npc.entityId + ") was hired - taken out of the world, the hire undone");
                }
                else if (NearestXZ(w, npc.position) <= StrayTalkReach && ScEnclavePeople.SeenByAny(w, npc.position)) continue;
                ScEnclavePeople.RemoveStray(npc);
                strays.RemoveAt(i);
            }
        }

        // ------------------------------------------------------------------ the journal and the texts ----

        /// <summary>
        /// Where the search's quest points (ScEncRequests.RequestPlace): the building searched while they are not found, waiting
        /// or lying there; the spot itself when they wait (or lie) outside it; the enclave's own building once they follow the
        /// player or their remains were searched (the defaults, left as they are).
        /// </summary>
        internal static void Place(EnclaveRequest r, Enclave e, ref Vector3i at, ref Vector3i size, ref string name)
        {
            if (!r.IsAccepted || !r.HasTarget || r.Stage == StageFollowing || r.Stage == StageSearched) return;
            PrefabInstance pi = TargetPoi(r);
            Vector3i tsize = pi != null ? pi.boundingBoxSize : new Vector3i(1, 1, 1);
            name = r.TargetName;
            if (r.HasSpot && !ScEnclavePeople.InFootprint(r.Target, tsize, r.Spot.x, r.Spot.z))
            {
                at = r.Spot;
                size = new Vector3i(1, 1, 1);
                return;
            }
            at = r.Target;
            size = tsize;
        }

        /// <summary>The leader's words about a search under way (the wait statement), by its stage.</summary>
        internal static string WaitInfo(EnclaveRequest r)
        {
            string name = PersonName(r);
            switch (r.Stage)
            {
                case StageFollowing: return string.Format(Loc("scEncReqWaitInfoFollow"), name);
                case StageSearched: return string.Format(Loc("scEncReqWaitInfoSearched"), name);
                default: return string.Format(Loc("scEncReqWaitInfoMissing"), name, r.TargetName, r.DueDay);
            }
        }

        /// <summary>sc enclave request &lt;id&gt; fate alive|dead: the next roll's outcome (before the player gets there; this session).</summary>
        public static string ForceFate(EnclaveRequest r, bool alive)
        {
            if (!r.IsAccepted) return "#" + r.Id + " is not accepted yet (sc enclave request " + r.EnclaveId + " accept)";
            if (r.Stage != StageUnrolled) return "#" + r.Id + ": the fate was rolled already - " + StageText(r);
            SessionOf(r.Id).Forced = alive;
            return "#" + r.Id + ": " + PersonName(r) + " will be found " + (alive ? "alive" : "dead") + " when you get to " + r.TargetName + " " + r.Target + " (this session)";
        }

        // ------------------------------------------------------------------ geometry ----

        private static Vector3 SpotCentre(EnclaveRequest r) { return new Vector3(r.Spot.x + 0.5f, r.Spot.y, r.Spot.z + 0.5f); }

        /// <summary>The point one waiting faces: four metres out along the spot's yaw (as a home spot's).</summary>
        private static Vector3 LookPoint(EnclaveRequest r)
        {
            return SpotCentre(r) + Quaternion.Euler(0f, r.SpotYaw, 0f) * Vector3.forward * 4f;
        }

        /// <summary>Horizontal metres from the nearest player to a point.</summary>
        private static float NearestXZ(World w, Vector3 at)
        {
            float best = float.MaxValue;
            List<EntityPlayer> list = w.Players != null ? w.Players.list : null;
            if (list == null) return best;
            for (int i = 0; i < list.Count; i++) if (list[i] != null) best = Mathf.Min(best, ScRunners.XZ(list[i].position, at));
            return best;
        }

        /// <summary>Horizontal metres from the nearest player to a footprint (0 inside it).</summary>
        private static float NearestToBox(World w, Vector3i poi, Vector3i size)
        {
            float best = float.MaxValue;
            List<EntityPlayer> list = w.Players != null ? w.Players.list : null;
            if (list == null) return best;
            float x0 = poi.x, x1 = poi.x + Math.Max(1, size.x), z0 = poi.z, z1 = poi.z + Math.Max(1, size.z);
            for (int i = 0; i < list.Count; i++)
            {
                EntityPlayer p = list[i];
                if (p == null) continue;
                float x = p.position.x, z = p.position.z;
                float dx = x < x0 ? x0 - x : (x > x1 ? x - x1 : 0f);
                float dz = z < z0 ? z0 - z : (z > z1 ? z - z1 : 0f);
                best = Mathf.Min(best, Mathf.Sqrt(dx * dx + dz * dz));
            }
            return best;
        }
    }
}
