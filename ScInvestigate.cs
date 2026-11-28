using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round k4 (the user, 2026-11-15: "the changes to home defense may need to be dialed back. Several times zombies have been
    /// lurking inside and members don't do anything about it. If a zombie not engaged with any member or the player is inside the
    /// base, members should investigate."). Round n08 made a resident at home leave alone an enemy that hunts nobody (the MayEngage
    /// postfix through UAIConsiderationScIntruder.LeavesAlone), and the base defense (ScDefendBase / ScDefendHold, ScIntruder) only
    /// walks to an enemy that is after a member or a player - so an idle zombie inside the base was ignored.
    ///
    /// Once a second (ScTicker, host only) this scans the home zone for INTRUDERS: an awake enemy of the community's members
    /// (SCore's IsEnemy, not inert; a dormant POI sleeper is left alone - the sweeps of ScTeam, ScIntruder and the census skip
    /// sleepers too, and a base built in a POI's sleeper volume would otherwise send members to wake them) that is after nobody
    /// (UAIConsiderationScIntruder.HuntsSomeone false, its 10 s latch included) and stands inside the home zone (the Fable review
    /// of k3+k4, F4: the user's "inside the base" - InvestigateEnterMargin, 0, widens it, never past ResidentRoamZoneMargin less
    /// the defense walk's stray band, so a responder never strays chasing it), or anywhere in the defended area during a live
    /// base attack - and on the base's levels (F5: within InvestigateHeight, 6 m, below the lowest and above the highest of the
    /// home block and the residents' posts, so a cellar, a mine or a roof under or over the base is not "inside"). Each gets
    /// InvestigateResponders (2) RESPONDERS: the nearest free
    /// residents within InvestigateRadius (0: the whole zone) - at home, on the Guard order, not on watch, not on a work shift, not
    /// Cowardly, not sheltering or kiting hurt (rt1, k5), at TeamLowHealth health or more (the MELEE line), not asleep, not in another
    /// fight, not resting after a stray, and (a resident who does not roam the zone) within DefendBaseRadius of her post - that
    /// one also while she answers (F7: past it ScReturn's leash would walk her back, and she would pace). One intruder per
    /// responder. A responder stays one while she stays free (no flip to a nearer member); only one who is not is replaced.
    /// Everyone else keeps her routine.
    ///
    /// For its responders only: LeavesAlone no longer applies to that intruder (the MayEngage postfix lets the pack's attacks at
    /// it), and ScIntruder accepts it (no "after nobody", no DefendBaseRadius, the zone test this file's own), so ScDefendBase walks
    /// her to it - seen or not - until she can engage (farther than FarWalk, beyond what SCore's UAI scores for her, SCResident's
    /// ScInvestigateWalk runs her toward it first), ScDefendHold holds while you or an ally stand in the line, and H1's
    /// HoldEngaged may face it. She fights it under her posture (ScPosture: INVESTIGATE with the melee or the gun stance); a gun
    /// holder shoots from range once she sees it. Outside the zone nothing changes: a wanderer there is still left alone.
    ///
    /// Release: the intruder dies or goes; it falls asleep; it stays outside the zone + ResidentRoamZoneMargin (or off the base's
    /// levels) for LeaveSeconds (5 s); or someone is fighting it (it is after a member or you): then the base defense has it, as for any intruder that
    /// hunts somebody. A released intruder that goes idle again within RememberSeconds gets its old responders back first.
    /// InvestigateIntruders false: nothing is tracked - round n08's rule as before.
    /// Round MP6 (part H): one per community - each community's base has its own intruders, answered by its own residents.
    /// </summary>
    public static class ScInvestigate
    {
        private const float ScanSeconds = 1f;
        /// <summary>Outside the zone + ResidentRoamZoneMargin this long: released (the user's "about 5 s").</summary>
        private const float LeaveSeconds = 5f;
        /// <summary>A released intruder keeps its responders this long for a re-pick (no flip if it goes idle again).</summary>
        private const float RememberSeconds = 30f;
        /// <summary>At most this many intruders are tracked at once (the scan's cost is bounded).</summary>
        private const int MaxTracked = 12;
        /// <summary>A live base attack's defended area is scanned this far above and below the home's claim block.</summary>
        private const float HalfHeight = 40f;
        /// <summary>
        /// SCore's UAI scores only entities in the NPC's bounds grown by its see distance - about 30 m each way - so ScDefendBase
        /// never sees an intruder farther off. Beyond FarWalk the far walk (ScInvestigateWalk, UAITaskInvestigateWalkSC) runs her
        /// toward it; it stops at FarWalkDone, where the defense walk and the pack's attacks have it.
        /// </summary>
        public const float FarWalk = 22f, FarWalkDone = 18f;

        private sealed class Intruder
        {
            public int Id;
            public string Name = "";
            public Vector3 Pos;
            public float FirstAt, SeenAt, OutsideSince, ReleasedAt;
            public bool Live, Logged, NoneLogged;
            public string ReleaseWhy;
            public readonly List<int> Responders = new List<int>();
        }

        private sealed class Candidate
        {
            public Member M;
            public EntityAlive Npc;
        }

        /// <summary>
        /// Round MP6 (part H): one home's intruders - who is tracked, who answers whom, its residents, its levels and its clock.
        /// One per community (ScHomeState): each community's base is scanned for its own intruders and answers with its own
        /// residents. The old statics below read the row of the community in scope; the pure reads (Responds, IntruderOf) read
        /// the asking resident's own community's.
        /// </summary>
        private sealed class HomeState
        {
            public readonly Dictionary<int, Intruder> Tracked = new Dictionary<int, Intruder>();
            public readonly Dictionary<int, int> ResponderOf = new Dictionary<int, int>();
            public readonly List<Candidate> Cands = new List<Candidate>();
            public float NextScan;
            public float LevelMin, LevelMax;
        }
        private static readonly ScHomeState<HomeState> homes = new ScHomeState<HomeState>();

        private static Dictionary<int, Intruder> tracked { get { return homes.Current.Tracked; } }
        /// <summary>A responder's body id -> the live intruder she answers.</summary>
        private static Dictionary<int, int> responderOf { get { return homes.Current.ResponderOf; } }
        private static readonly List<Entity> near = new List<Entity>();
        private static List<Candidate> cands { get { return homes.Current.Cands; } }
        private static readonly List<int> scratch = new List<int>();
        private static float nextScan { get { return homes.Current.NextScan; } set { homes.Current.NextScan = value; } }
        /// <summary>F5: the lowest and highest of the home block and the residents' posts (Levels, each scan).</summary>
        private static float levelMin { get { return homes.Current.LevelMin; } set { homes.Current.LevelMin = value; } }
        private static float levelMax { get { return homes.Current.LevelMax; } set { homes.Current.LevelMax = value; } }
        private static int seen, assigned, unanswered, replaced, released;

        public static void Reset()
        {
            homes.Reset();                        // round MP6 (part H): every community's intruders, responders and clocks
            near.Clear();
            scratch.Clear();
            seen = assigned = unanswered = replaced = released = 0;
        }

        /// <summary>A despawned body: as a responder and as an intruder (entity ids are recycled). Round MP6 (part H): in every community's.</summary>
        public static void Forget(int entityId)
        {
            if (ScCommunities.Count < 2) { HomeState z = homes.For(ScPlayers.SharedCommunity); z.ResponderOf.Remove(entityId); z.Tracked.Remove(entityId); return; }
            homes.ForEach((cid, s) => { s.ResponderOf.Remove(entityId); s.Tracked.Remove(entityId); });
        }

        /// <summary>
        /// Round MP6 (part H): the row of the community this resident belongs to (her member record's), for the pure reads; null for
        /// anyone who is no member (a responder always is one). With one community, community 0's at once.
        /// </summary>
        private static HomeState StateOf(EntityAlive self)
        {
            if (ScCommunities.Count < 2) return homes.For(ScPlayers.SharedCommunity);
            int cid = ScCommunities.CommunityOfMember(self.entityId);
            return cid >= 0 ? homes.For(cid) : null;
        }

        /// <summary>
        /// The enter area, on the base's levels (InLevels): inside the home zone (F4; InvestigateEnterMargin meters outside it
        /// too, at most ResidentRoamZoneMargin less the defense walk's stray band and 1 m of slack); during a live base attack the
        /// defended area. A pure read.
        /// </summary>
        public static bool InArea(Vector3 p)
        {
            if (!InLevels(p.y)) return false;
            if (ScBaseAttack.WideDefence) return ScBaseAttack.InDefenceArea(p);
            float m = EnterMargin;
            return m <= 0f ? ScZone.Contains(p) : ScZone.DistanceOutside(p) <= m;
        }

        private static float EnterMargin
        {
            get { return Mathf.Min(ScConfig.InvestigateEnterMargin, Mathf.Max(0f, ScConfig.ResidentRoamZoneMargin - UAI.UAIConsiderationScIntruder.StrayMarginInside - 1f)); }
        }

        /// <summary>F5: feet at this height are on the base's levels - InvestigateHeight below the lowest and above the highest of
        /// the home block and the residents' posts. A pure read (the levels are taken at each scan).</summary>
        public static bool InLevels(float y)
        {
            return y >= levelMin - ScConfig.InvestigateHeight && y <= levelMax + ScConfig.InvestigateHeight;
        }

        /// <summary>F5: the base's levels - the home block and every resident's post at home.</summary>
        private static void Levels()
        {
            levelMin = levelMax = ScZone.Home.y;
            foreach (Member m in ScRegistry.Members.Values)
            {
                if (m.Status != MemberStatus.Resident || m.Post == Vector3i.zero) continue;
                if (!UAI.UAIConsiderationScNotHomeMember.AtHome(m.Post.ToVector3())) continue;
                levelMin = Mathf.Min(levelMin, m.Post.y);
                levelMax = Mathf.Max(levelMax, m.Post.y);
            }
        }

        // ------------------------------------------------------------------ the scan (ScTicker, every second) ----

        public static void Tick(World w, EntityPlayer player)     // round MP0: any player (the anchor); not used here
        {
            try
            {
                if (!ScConfig.InvestigateIntruders || w == null || !ScRegistry.Loaded || !ScZone.HasHome)
                {
                    if (tracked.Count > 0) ReleaseAll("off", "investigation off, or no home");
                    return;
                }
                float now = Time.time;
                if (now < nextScan) return;
                nextScan = now + ScanSeconds;
                if (GameManager.Instance != null && GameManager.Instance.IsPaused()) return;
                Candidates();
                if (cands.Count == 0)
                {
                    if (tracked.Count > 0) ReleaseAll("no-residents", "no resident's body in the world");
                    return;
                }
                Levels();
                Scan(w, cands[0].Npc, now);
                Assign(now);
            }
            catch (Exception e)
            {
                ScLog.Once("investigate-tick", e);
            }
        }

        /// <summary>Every resident with a post whose body is in the world (the probe for IsEnemy, and the pool of responders).</summary>
        private static void Candidates()
        {
            cands.Clear();
            foreach (Member m in ScRegistry.Members.Values)
            {
                if (m.Status != MemberStatus.Resident || m.Post == Vector3i.zero) continue;
                EntityAlive npc = ScCommunity.FindNpc(m.Id);
                if (npc == null || npc.IsDead() || !ScXnpc.IsBody(npc)) continue;
                cands.Add(new Candidate { M = m, Npc = npc });
            }
        }

        private static void Scan(World w, EntityAlive probe, float now)
        {
            int xMin, xMax, zMin, zMax;
            ScZone.Bounds(out xMin, out xMax, out zMin, out zMax);
            float pad = ScConfig.ResidentRoamZoneMargin + 1f;
            Vector3 home = ScZone.Home.ToVector3();
            var box = new Bounds();
            box.SetMinMax(new Vector3(xMin - pad, levelMin - ScConfig.InvestigateHeight - 1f, zMin - pad),
                          new Vector3(xMax + 1 + pad, levelMax + ScConfig.InvestigateHeight + 2f, zMax + 1 + pad));
            Bounds wide;
            if (ScBaseAttack.WideDefence && ScBaseAttack.DefenceBounds(home.y, HalfHeight, 0f, out wide)) box.Encapsulate(wide);
            near.Clear();
            w.GetEntitiesInBounds(typeof(EntityAlive), box, near);
            try
            {
                for (int i = 0; i < near.Count; i++)
                {
                    EntityAlive a = near[i] as EntityAlive;
                    if (a == null || a is EntityPlayer || a.IsDead() || a.IsSleeping) continue;
                    if (!EntityTargetingUtilities.IsEnemy(probe, a) || global::Harmony.ZombieFeatures.InertEntity.IsInert(a)) continue;
                    Intruder it;
                    tracked.TryGetValue(a.entityId, out it);
                    bool live = it != null && it.Live;
                    if (!UAI.UAIConsiderationScNotHomeMember.AtHome(a.position) || !InLevels(a.position.y))
                    {
                        if (live && it.OutsideSince <= 0f) it.OutsideSince = now;      // past the zone + margin, or off its levels: the leave clock
                        continue;
                    }
                    if (UAI.UAIConsiderationScIntruder.HuntsSomeone(a))
                    {
                        if (live) Release(it, "engaged", "it is after a member or you now - the base defense has it", now);
                        continue;
                    }
                    if (live)
                    {
                        it.SeenAt = now;
                        it.Pos = a.position;
                        if (InArea(a.position)) it.OutsideSince = 0f;                 // between the enter area and the margin: kept, no clock
                        continue;
                    }
                    if (!InArea(a.position)) continue;                                 // not yet inside: no new intruder
                    if (it == null)
                    {
                        if (CountLive() >= MaxTracked) continue;
                        tracked[a.entityId] = it = new Intruder { Id = a.entityId, Name = a.EntityName, FirstAt = now };
                        seen++;
                        Patches_Xnpc.Count("investigate-seen");
                    }
                    it.Live = true;
                    it.SeenAt = now;
                    it.OutsideSince = 0f;
                    it.Pos = a.position;
                    it.ReleaseWhy = null;
                }
            }
            finally { near.Clear(); }
            // the live ones this scan did not find inside: dead, gone, asleep, outside the box, or past the margin too long
            scratch.Clear();
            foreach (var kv in tracked) scratch.Add(kv.Key);
            foreach (int id in scratch)
            {
                Intruder it = tracked[id];
                if (!it.Live)
                {
                    if (now - it.ReleasedAt > RememberSeconds) tracked.Remove(id);
                    continue;
                }
                EntityAlive a = w.GetEntity(id) as EntityAlive;
                if (a == null || a.IsDead()) { Release(it, "dead", "it is dead or gone", now); continue; }
                if (a.IsSleeping) { Release(it, "asleep", "it went to sleep", now); continue; }
                if (it.SeenAt < now - 0.5f && it.OutsideSince <= 0f) it.OutsideSince = now;   // left the scan's box
                if (it.OutsideSince > 0f && now - it.OutsideSince >= LeaveSeconds)
                    Release(it, "left", "it stayed outside the zone + " + F0(ScConfig.ResidentRoamZoneMargin) + " m (or off its levels) for " + F0(LeaveSeconds) + " s", now);
            }
        }

        private static int CountLive()
        {
            int n = 0;
            foreach (var kv in tracked) if (kv.Value.Live) n++;
            return n;
        }

        /// <summary>Keeps each live intruder's free responders, drops the ones who are not, fills up to InvestigateResponders.</summary>
        private static void Assign(float now)
        {
            scratch.Clear();
            foreach (var kv in tracked) if (kv.Value.Live) scratch.Add(kv.Key);
            scratch.Sort((a, b) => tracked[a].FirstAt.CompareTo(tracked[b].FirstAt));   // the oldest intruder chooses first
            World w = GameManager.Instance != null ? GameManager.Instance.World : null;
            foreach (int id in scratch)
            {
                Intruder it = tracked[id];
                EntityAlive t = w != null ? w.GetEntity(id) as EntityAlive : null;
                if (t == null) continue;
                // keep the free ones (no flip to a nearer member), drop the rest
                for (int i = it.Responders.Count - 1; i >= 0; i--)
                {
                    int rid = it.Responders[i];
                    Candidate c = Find(rid);
                    int other;
                    string why = c == null ? "no longer a resident in the world" : responderOf.TryGetValue(rid, out other) && other != id ? "sent to another intruder"
                               : NotFree(c.M, c.Npc, t, false);
                    if (why == null) { responderOf[rid] = id; continue; }
                    it.Responders.RemoveAt(i);
                    if (responderOf.TryGetValue(rid, out other) && other == id) responderOf.Remove(rid);
                    if (!it.Logged) continue;                                          // a memory from an earlier round: quietly
                    replaced++;
                    Patches_Xnpc.Count("investigate-responder-dropped");
                    ScLog.Verbose("investigate: " + (c != null ? c.M.Name : "#" + rid) + " stops going to " + it.Name + " (" + id + "): " + why);
                }
                // fill up with the nearest free residents
                while (it.Responders.Count < ScConfig.InvestigateResponders)
                {
                    Candidate best = null;
                    float bestD2 = float.MaxValue;
                    foreach (Candidate c in cands)
                    {
                        if (responderOf.ContainsKey(c.Npc.entityId) || it.Responders.Contains(c.Npc.entityId)) continue;
                        if (NotFree(c.M, c.Npc, t, true) != null) continue;
                        float d2 = (c.Npc.position - t.position).sqrMagnitude;
                        if (d2 < bestD2) { bestD2 = d2; best = c; }
                    }
                    if (best == null) break;
                    it.Responders.Add(best.Npc.entityId);
                    responderOf[best.Npc.entityId] = id;
                    if (it.Logged)
                    {
                        Patches_Xnpc.Count("investigate-responder-added");
                        ScLog.Verbose("investigate: " + best.M.Name + " also goes to " + it.Name + " (" + id + "), " + F0(Mathf.Sqrt(bestD2)) + " m off");
                    }
                }
                if (it.Responders.Count > 0 && !it.Logged)
                {
                    it.Logged = true;
                    assigned++;
                    Patches_Xnpc.Count("investigate-assigned");
                    ScLog.Out("investigate: " + it.Name + " (" + id + ") is inside the base at " + ScRetreat.Cell(it.Pos) + " and after nobody - " + Names(it.Responders)
                              + " go" + (it.Responders.Count == 1 ? "es" : "") + " to it (round k4; the others keep their routine)");
                }
                else if (it.Responders.Count == 0 && !it.NoneLogged)
                {
                    it.NoneLogged = true;
                    unanswered++;
                    Patches_Xnpc.Count("investigate-unanswered");
                    ScLog.Verbose("investigate: " + it.Name + " (" + id + ") is inside the base at " + ScRetreat.Cell(it.Pos) + " and after nobody, but no resident is free to go to it ("
                                  + FirstRefusal(t) + ")");
                }
            }
        }

        /// <summary>
        /// Why this resident is not free to answer t, or null. fresh: a new pick (also not mid medical use, not resting after a stray,
        /// within InvestigateRadius); a responder already answering keeps it through those short states, so she is not swapped for
        /// another (no flip). Not roaming the zone: within DefendBaseRadius of her post, fresh or not (F7).
        /// </summary>
        private static string NotFree(Member m, EntityAlive npc, EntityAlive t, bool fresh)
        {
            if (m == null || npc == null || npc.IsDead()) return "not in the world";
#if SC_V34
            if (ScDowned.IsDown(npc)) return "down";                                              // round U2: down, or a medic on her way to one
            if (ScDowned.IsReviving(npc.entityId)) return "on the way to revive a downed member";
#endif
            if (m.Status != MemberStatus.Resident || m.Post == Vector3i.zero) return "not a resident at a post";
            if (!UAI.UAIConsiderationScNotHomeMember.AtHome(npc.position)) return "not at home";
            if (ScWatch.OnWatch(m)) return "on watch";
#if SC_V34
            if (ScWork.OnShift(m.Id)) return "on a work shift";
            if (ScSleep.IsAsleep(npc)) return "asleep";
#endif
            if (npc.Buffs == null || !npc.Buffs.HasCustomVar("CurrentOrder")
                || Mathf.RoundToInt(npc.Buffs.GetCustomVar("CurrentOrder")) != (int)EntityUtilities.Orders.Guard) return "not on the Guard order";
            if (ScTraits.IsTimid(m)) return "Cowardly";
            if (ScRetreat.Owns(npc)) return "sheltering or kiting hurt";
            if (ScPosture.Holds(npc)) return "holding her spot after a back-off, a heal or a reload (round cb1)";
            int max = npc.GetMaxHealth();
            if (max > 0 && (float)npc.Health / max < ScConfig.TeamLowHealth) return "hurt below " + F0(ScConfig.TeamLowHealth * 100f) + "%";
            EntityAlive own = npc.GetAttackTarget();
            if (own == null || own.IsDead()) own = npc.GetRevengeTarget();
            // round wp1 (review WP1-M2): an own target round n08 leaves her alone with (a wanderer she may not attack) is no fight -
            // she stood by it, "in another fight", and nobody went to the intruder (log: "Mia: in another fight; Holly: down")
            if (own != null && !own.IsDead() && own != t && EntityTargetingUtilities.IsEnemy(npc, own)
                && !UAI.UAIConsiderationScIntruder.LeavesAlone(npc, own)) return "in another fight";
            // the Fable review of k3+k4 (F7): a resident who does not roam the zone answers only within DefendBaseRadius of her
            // post, also while she answers - past it ScReturn's leash walks her back and the defense walk out again (pacing)
            if (!ScBaseAttack.WideDefence && !UAI.UAIConsiderationScIntruder.Roams(m)
                && (t.position - ScCommunity.PostCenter(m)).sqrMagnitude > (float)ScConfig.DefendBaseRadius * ScConfig.DefendBaseRadius)
                return "beyond DefendBaseRadius of her post";
            if (!fresh) return null;
            if (ScMedic.IsUsing(npc.entityId)) return "using a medical item";
            if (UAI.ScDefendState.RestLeft(npc.entityId) > 0f) return "resting after a stray";
            if (ScConfig.InvestigateRadius > 0f && (t.position - npc.position).sqrMagnitude > ScConfig.InvestigateRadius * ScConfig.InvestigateRadius)
                return "farther than InvestigateRadius";
            return null;
        }

        private static Candidate Find(int id)
        {
            foreach (Candidate c in cands) if (c.Npc.entityId == id) return c;
            return null;
        }

        private static string FirstRefusal(EntityAlive t)
        {
            var parts = new List<string>();
            foreach (Candidate c in cands)
            {
                int other;
                string why = responderOf.TryGetValue(c.Npc.entityId, out other) ? "answers another intruder" : NotFree(c.M, c.Npc, t, true);
                if (why != null) parts.Add(c.M.Name + ": " + why);
                if (parts.Count >= 4) break;
            }
            return parts.Count > 0 ? string.Join("; ", parts.ToArray()) : "none";
        }

        private static void Release(Intruder it, string key, string why, float now)
        {
            if (!it.Live) return;
            it.Live = false;
            it.ReleasedAt = now;
            it.ReleaseWhy = why;
            foreach (int rid in it.Responders)
            {
                int other;
                if (responderOf.TryGetValue(rid, out other) && other == it.Id) responderOf.Remove(rid);
            }
            released++;
            Patches_Xnpc.Count("investigate-released-" + key);
            if (it.Logged) ScLog.Verbose("investigate: " + it.Name + " (" + it.Id + ") released - " + why + (it.Responders.Count > 0 ? " (" + Names(it.Responders) + " back to their routine)" : ""));
        }

        private static void ReleaseAll(string key, string why)
        {
            float now = Time.time;
            foreach (var kv in tracked) Release(kv.Value, key, why, now);
            tracked.Clear();
            responderOf.Clear();
        }

        private static string Names(List<int> ids)
        {
            var parts = new List<string>();
            foreach (int id in ids)
            {
                Member m = ScRegistry.GetMember(id);
                parts.Add(m != null ? m.Name : "#" + id);
            }
            return parts.Count == 0 ? "nobody" : parts.Count == 1 ? parts[0] : string.Join(", ", parts.GetRange(0, parts.Count - 1).ToArray()) + " and " + parts[parts.Count - 1];
        }

        private static string F0(float f) { return f.ToString("0", CultureInfo.InvariantCulture); }

        // ------------------------------------------------------------------ pure reads ----

        /// <summary>
        /// This resident answers t, a live intruder after nobody (LeavesAlone and ScIntruder stand aside for her). A pure read: it
        /// runs inside the MayEngage postfix and the defense's considerations for every candidate.
        /// </summary>
        public static bool Responds(EntityAlive self, EntityAlive t)
        {
            int id;
            Intruder it;
            HomeState s = self != null ? StateOf(self) : null;      // round MP6 (part H): her own community's intruders
            return s != null && t != null && s.ResponderOf.TryGetValue(self.entityId, out id) && id == t.entityId
                   && s.Tracked.TryGetValue(id, out it) && it.Live;
        }

        /// <summary>The live intruder this resident answers, or null. A pure read.</summary>
        public static EntityAlive IntruderOf(EntityAlive self)
        {
            int id;
            Intruder it;
            HomeState s = self != null ? StateOf(self) : null;      // round MP6 (part H): her own community's intruders
            if (s == null || self.world == null || !s.ResponderOf.TryGetValue(self.entityId, out id) || !s.Tracked.TryGetValue(id, out it) || !it.Live) return null;
            EntityAlive t = self.world.GetEntity(id) as EntityAlive;
            return t != null && !t.IsDead() ? t : null;
        }

        // ------------------------------------------------------------------ console ----

        /// <summary>sc team: the intruders now, their responders and why nobody else goes.</summary>
        public static void ListLines(List<string> lines)
        {
            if (!ScConfig.InvestigateIntruders) { lines.Add("[SC] intruders (round k4): off (InvestigateIntruders false: n08's rule - a wanderer at home is left alone)"); return; }
            int live = CountLive();
            lines.Add("[SC] intruders (round k4): " + live + " inside the base and after nobody, " + ScConfig.InvestigateResponders + " responder(s) each "
                      + (ScConfig.InvestigateRadius > 0f ? "within " + F0(ScConfig.InvestigateRadius) + " m" : "from anywhere in the zone") + "; levels y "
                      + F0(levelMin - ScConfig.InvestigateHeight) + " to " + F0(levelMax + ScConfig.InvestigateHeight) + ", "
                      + (EnterMargin > 0f ? "up to " + F0(EnterMargin) + " m outside the zone" : "inside the zone only") + "; " + Report());
            World w = GameManager.Instance != null ? GameManager.Instance.World : null;
            float now = Time.time;
            foreach (var kv in tracked)
            {
                Intruder it = kv.Value;
                if (!it.Live) continue;
                EntityAlive t = w != null ? w.GetEntity(it.Id) as EntityAlive : null;
                var sb = new StringBuilder("[SC]   ").Append(it.Name).Append(" (").Append(it.Id).Append(") at ").Append(ScRetreat.Cell(it.Pos))
                    .Append(", tracked ").Append((now - it.FirstAt).ToString("0", CultureInfo.InvariantCulture)).Append(" s")
                    .Append(it.OutsideSince > 0f ? ", OUTSIDE the zone or its levels for " + (now - it.OutsideSince).ToString("0.0", CultureInfo.InvariantCulture) + " s" : "")
                    .Append(" - responders: ").Append(it.Responders.Count > 0 ? Names(it.Responders) : "nobody free (" + (t != null ? FirstRefusal(t) : "-") + ")");
                foreach (int rid in it.Responders)
                {
                    EntityAlive r = w != null ? w.GetEntity(rid) as EntityAlive : null;
                    if (r != null && t != null) sb.Append("; ").Append(ScCommunity.DisplayName(r)).Append(' ').Append((r.position - t.position).magnitude.ToString("0", CultureInfo.InvariantCulture)).Append(" m off");
                }
                lines.Add(sb.ToString());
            }
        }

        /// <summary>The totals (sc team, sc xnpc).</summary>
        public static string Report()
        {
            return seen + " seen, " + assigned + " answered, " + unanswered + " with nobody free, " + replaced + " responder(s) dropped, " + released
                   + " released this session (counters investigate-*)";
        }

        /// <summary>One line for sc xnpc &lt;id&gt;.</summary>
        public static string Line(EntityAlive e)
        {
            if (!ScConfig.InvestigateIntruders) return "investigate: off";
            EntityAlive t = IntruderOf(e);
            if (t != null)
            {
                float d = (t.position - e.position).magnitude;
                return "investigate: RESPONDER - sent to " + t.EntityName + " (" + t.entityId + ") at " + d.ToString("0", CultureInfo.InvariantCulture) + " m"
                       + (d > FarWalk ? " (the far walk: beyond the defense walk's reach)" : "");
            }
            Member m = ScRegistry.GetMember(e.entityId);
            if (m == null || m.Status != MemberStatus.Resident) return "investigate: not a resident";
            return "investigate: not answering an intruder (" + CountLive() + " tracked)";
        }
    }
}

namespace UAI
{
    using SurvivorCommunity;

    /// <summary>
    /// Round k4 (SCResident's ScInvestigateWalk, IsSelf): 1 while this resident answers an intruder (ScInvestigate) that is
    /// farther than ScInvestigate.FarWalk - beyond what SCore's UAI scores for her (about 30 m each way), so ScDefendBase cannot
    /// take her there - else 0. A pure read; a throw is logged once and means 0.
    /// </summary>
    public class UAIConsiderationScInvestigateFar : UAIConsiderationBase
    {
        public override float GetScore(Context _context, object target)
        {
            try
            {
                EntityAlive self = _context.Self;
                EntityAlive t = ScInvestigate.IntruderOf(self);
                return t != null && (t.position - self.position).sqrMagnitude > ScInvestigate.FarWalk * ScInvestigate.FarWalk ? 1f : 0f;
            }
            catch (System.Exception e)
            {
                ScLog.Once("uai-investigate-far", e);
                return 0f;
            }
        }
    }

    /// <summary>
    /// Round k4, ScInvestigateWalk's task: runs a responder toward her intruder (FindPath at ScRetreat.RunSpeed, as the retreat's
    /// walks - never an SCore move task), re-planned every RepathSeconds as it moves, until it is within
    /// ScInvestigate.FarWalkDone (then ScDefendBase and the pack's attacks have it) or she no longer answers it. Shared singleton,
    /// per-entity state.
    /// </summary>
    public class UAITaskInvestigateWalkSC : UAITaskBase
    {
        private const float RepathSeconds = 1f;
        private readonly Dictionary<int, float> nextPath = new Dictionary<int, float>();

        public override void Start(Context _context)
        {
            base.Start(_context);
            try
            {
                EntityAlive self = _context.Self;
                EntityAlive t = ScInvestigate.IntruderOf(self);
                if (t == null) { Stop(_context); return; }
                self.FindPath(t.position, ScRetreat.RunSpeed(self), false, null);
                nextPath[self.entityId] = Time.time + RepathSeconds;
                Patches_Xnpc.Count("investigate-far-walk");
            }
            catch (System.Exception e)
            {
                ScLog.Once("investigate-walk-start", e);
                Stop(_context);
            }
        }

        public override void Update(Context _context)
        {
            try
            {
                EntityAlive self = _context.Self;
                EntityAlive t = ScInvestigate.IntruderOf(self);
                if (t == null || (t.position - self.position).sqrMagnitude <= ScInvestigate.FarWalkDone * ScInvestigate.FarWalkDone) { Stop(_context); return; }
                float due;
                if (!nextPath.TryGetValue(self.entityId, out due) || Time.time >= due)
                {
                    nextPath[self.entityId] = Time.time + RepathSeconds;
                    self.FindPath(t.position, ScRetreat.RunSpeed(self), false, null);
                }
            }
            catch (System.Exception e)
            {
                ScLog.Once("investigate-walk-update", e);
                Stop(_context);
            }
        }

        public override void Stop(Context _context)
        {
            try
            {
                EntityAlive self = _context.Self;
                if (self != null)
                {
                    nextPath.Remove(self.entityId);
                    if (self.getNavigator() != null) self.getNavigator().clearPath();
                }
            }
            catch (System.Exception e) { ScLog.Once("investigate-walk-stop", e); }
            base.Stop(_context);
        }
    }
}
