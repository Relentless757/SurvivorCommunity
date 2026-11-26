using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Physical supply runs. A runner walks out of the base to a real POI, looks around there for a few hours
    /// and walks home again. While the player is near they are a real entity (ONLINE). Further out the entity is
    /// captured into a snapshot - the vanilla EntityCreationData the game itself uses to park vehicles and drones:
    /// name, health, cvars, buffs, bag, held weapon, SCore pack - that lives in the registry, the entity is
    /// removed, and the registry walks them on along the route on the world clock (OFFLINE). When the player
    /// comes near their virtual position the same entity, under the same id, is re-created there.
    ///
    /// The registry is the only persistent copy of a runner: chunks never save one (the IsSavedToFile postfix),
    /// a chunk unload captures it first (World.EntityUnloadedDelegates), a quit takes every online runner offline
    /// before the world is saved, and a stale copy that a chunk loads while the runner is offline is hidden and
    /// removed (PostInit guard). The run's dice are only rolled while the runner is offline: an escorted runner
    /// faces real zombies instead.
    ///
    /// Round op1 (2026-11-19): a member posted to an outpost walks there and back on these same legs (MemberStatus.Transit,
    /// ScGarrison): Outbound ends beside the claim (ScGarrison.ArriveOnline / ArriveOffline instead of EnterSite), Inbound at the
    /// gate with nothing to bank, Back as a runner's. A safe journey: no dice, no distress, no radio, never given up hurt (IsRun
    /// gates those steps).
    ///
    /// Round MP5 (part N, multiplayer): "the player" above is every community player this server keeps a chunk observer for -
    /// the host's own, each client's server-side one, a dedicated server's remote players (Observes). A body stays in the world
    /// while ANY living one keeps it (their own ring, view distance and drawing range: Keeps), is captured only when none does,
    /// is re-created near whichever one comes within reach (Reacher), out of the view of every living one near the spot
    /// (PopInSeen), and the watchdog hops it only where none of them sees (UnseenByAll). The tick's anchor (ScPlayers.Anchor)
    /// is still who the tells, the radio lines and a fresh body's stamps reason from. Single player: the one player, so every
    /// test reads as the old one did.
    /// </summary>
    public static class ScRunners
    {
        public const string OnRunCvar = "sc_onrun";
        /// <summary>EntityUtilities.Orders.Task: no SCore or XNPCCore action uses it, so it switches every resident action off.</summary>
        private const float OrderTask = 8f;
        private const int TicksPerHour = 1000;
        /// <summary>EntityNPC hides its model beyond 90 m: a runner is only captured out of sight (unless forced).</summary>
        private const float NoDrawRange = 90f;
        private const int MaxCreateFailures = 5;
        /// <summary>Round r2: badly hurt with no way to heal for this long (real seconds) before she gives the job up - not on one reading.</summary>
        private const float HurtConfirmSeconds = 2f;
        /// <summary>
        /// Round r2: how close an enemy has to be for SC's flee actions to run from it - ScFlee's and ScFleeHurt's TargetDistance
        /// max in Config/utilityai.xml (keep the two in step).
        /// </summary>
        public const float FleeRadius = 15f;
        /// <summary>Round r2: how far those flights take her (their FleeFromTarget max_distance): a threat farther off is not routed round.</summary>
        public const float FleeClear = 30f;
        /// <summary>Round r2: the route's memo lives this long (Time.time seconds): UAI asks several times a second.</summary>
        private const float RouteCacheSeconds = 0.5f;
        /// <summary>Round r2 review (fix 2b): a walk held this long in all (real seconds) on the way out, home or in is ended (HoldClock).</summary>
        private const float HoldMaxSeconds = 30f;

        /// <summary>Set around our own re-create: the PostInit guard lets exactly this id through.</summary>
        public static int RecreatingId = -1;

        /// <summary>
        /// Set around the creation of a body from its snapshot, whose bag is replaced with the snapshot's right after
        /// (M11 re-check, R1): ScLoadout.AfterPostInit leaves the bag alone then, and AfterRunnerRestore does that part on
        /// the bag that stays. Main thread.
        /// </summary>
        public static bool Restoring;
        private static int capturingId = -1;
        /// <summary>Same-id copies a chunk loaded while the runner was offline: hidden in PostInit, removed next tick. Never Doomed.</summary>
        public static readonly HashSet<int> StaleIds = new HashSet<int>();
        /// <summary>Runner id -> (class id, birth time). Swapped whole on the main thread; read by the IsSavedToFile postfix, which runs on the chunk-save thread too.</summary>
        private static volatile Dictionary<int, KeyValuePair<int, ulong>> runnerIds = new Dictionary<int, KeyValuePair<int, ulong>>();
        private static World hooked;
        private static readonly Dictionary<int, Session> sessions = new Dictionary<int, Session>();
        private static readonly List<Member> scratch = new List<Member>();

        /// <summary>Session-only state per runner (never saved).</summary>
        public sealed class Session
        {
            public float OnlineSince, OfflineSince, SettledSince = -1f, NextSnap, FrozenSince = -1f, PopInBlockedSince = -1f;
            public Vector3 ProgressPos;
            public float ProgressSince;
            public Vector3 LastGoodPos;
            public bool HasGoodPos;
            public Vector3 Waypoint;
            public bool HasWaypoint;
            public float WaypointSince;
            public int Sidestep;
            public float SidestepSince;
            public int CreateFailures;
            public int SpotFailures;
            public PrefabInstance Poi;
            public Vector3i PoiKey;
            public Vector3 LastTickPos;
            public float LastTickTime = -1f;
            public float MeasuredMps;
            public long LastCreateMs = -1;
            /// <summary>The watchdog's last sample: its real time and the body's ticksExisted (the M11 fix 2: frozen time is not stuck time).</summary>
            public float WatchAt = -1f;
            public int WatchTicks = -1;
#if SC_V34
            /// <summary>R2: when the online body is next looked at for a handheld radio (ScHandheld.NoteCarried).</summary>
            public float NextRadioCheck;
#endif
            /// <summary>Round r2: since when (real time) she has been badly hurt with no way to heal on the job (-1: not), for HurtConfirmSeconds.</summary>
            public float HurtSince = -1f;
            /// <summary>
            /// Round r2, the route's memo (RouteHeld): good until RouteUntil (Time.time) - the threat she would run from (-1: none),
            /// where it stood, and whether the walk is held because nothing keeps her clear of it.
            /// </summary>
            public float RouteUntil = -1f;
            public int RouteThreat = -1;
            public Vector3 RouteThreatPos;
            public bool RouteHeldNow;
            /// <summary>Round r2: TravelSC's last re-plan round a threat that moved onto her way (Time.time), at most every RouteCacheSeconds.</summary>
            public float RouteReplanAt = -1f;
            /// <summary>
            /// Round r2 review (fix 2b): the hold's own clock (HoldClock) - real seconds held in all on this leg (HeldLeg), and the
            /// threats she no longer waits for after HoldMaxSeconds in your sight. A set for the whole leg (re-review R1): with one id,
            /// two threats took turns holding her, each release freeing the other. Flight and fighting still apply to them.
            /// </summary>
            public float HeldTotal;
            public RunLeg HeldLeg = RunLeg.None;
            public readonly HashSet<int> HeldReleased = new HashSet<int>();
            /// <summary>
            /// Round op1: the next re-create takes the place of an outpost stand-in that was just removed on this spot (a recall
            /// at the outpost) - no offline dwell and no pop-in wait, so the real body steps in where the stand-in stood.
            /// Cleared by the re-create.
            /// </summary>
            public bool SwapIn;
        }

        public static Session S(int id)
        {
            Session s;
            if (!sessions.TryGetValue(id, out s)) sessions[id] = s = new Session();
            return s;
        }

        public static void Reset()
        {
            RecreatingId = -1;
            Restoring = false;
            capturingId = -1;
            StaleIds.Clear();
            sessions.Clear();
            runnerIds = new Dictionary<int, KeyValuePair<int, ulong>>();
            Unhook();
        }

        /// <summary>
        /// A member on the runner legs whose body is the walker or the snapshot: a physical supply (or mining) run, and round op1
        /// (2026-11-19) a posting journey (Transit) with a snapshot. A parked traveler (Transit, no snapshot: the body waits
        /// hidden at home, ScGarrison walks the legs on the clock) is not one - its body must stay in its chunk's save.
        /// </summary>
        public static bool IsPhysical(Member m)
        {
            return m != null && m.RunLeg != RunLeg.None
                   && (m.Status == MemberStatus.OnRun || (m.Status == MemberStatus.Transit && !string.IsNullOrEmpty(m.Snap)));
        }

        /// <summary>Round op1: a supply or mining run on the legs (not a posting journey) - the dice, distress, haul and radio steps.</summary>
        public static bool IsRun(Member m) { return m != null && m.Status == MemberStatus.OnRun && m.RunLeg != RunLeg.None; }

        /// <summary>
        /// Round op1: no entity may represent this member - the snapshot is them. An offline physical runner or traveler, and a
        /// posted member whose body walked to the outpost (Garrison with a snapshot: their stand-in is a separate entity). An
        /// entity under the id is an old chunk copy (hidden and removed) or a stranger who inherited the id.
        /// </summary>
        public static bool HeldOffline(Member m)
        {
            if (m == null) return false;
            if (m.Status == MemberStatus.Garrison) return !string.IsNullOrEmpty(m.Snap);
            return IsPhysical(m) && m.Offline;
        }

        /// <summary>
        /// Thread-safe (the IsSavedToFile postfix asks from the chunk-save thread): is this entity a physical runner's
        /// body - same id, class and birth time? A stranger that inherited the id after a crash is saved as normal.
        /// A reference test (the M11 fix): a body whose chunk unloaded is written after Entity.OnEntityUnload destroyed its
        /// GameObject, and Unity's == null - true for a destroyed body - made this answer "not a runner" on exactly the
        /// save the veto exists for. The id, class and birth time are managed fields that outlive the GameObject. Its one
        /// caller is that postfix (through VetoChunkSave); no main-thread caller relies on a destroyed body reading as
        /// "not a runner".
        /// </summary>
        public static bool IsRunnerBody(Entity e)
        {
            KeyValuePair<int, ulong> who;
            if ((object)e == null || !runnerIds.TryGetValue(e.entityId, out who)) return false;
            if (who.Key != 0 && e.entityClass != who.Key) return false;
            return who.Value == 0 || e.WorldTimeBorn == 0 || e.WorldTimeBorn == who.Value;
        }

        /// <summary>Boxed verdicts for the per-body memo below (ConditionalWeakTable wants a reference type).</summary>
        private static readonly object Vetoed = new object(), NotVetoed = new object();

        /// <summary>
        /// The verdict for each unloaded (destroyed) body, kept with the body object itself: weak keys by reference, so
        /// it goes when the body is collected, and thread-safe.
        /// </summary>
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Entity, object> unloadedVerdicts =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Entity, object>();

        /// <summary>
        /// The IsSavedToFile postfix's question (the M11 fix): keep this body out of its chunk's save? IsRunnerBody, with
        /// one guard for the save thread. Chunk.write asks IsSavedToFile twice per entity - once to count, once to write -
        /// and an unloaded chunk is written on thread_SaveChunks while RebuildRunnerSet may swap the set on the main
        /// thread: two different answers for one body would put a count in the chunk that does not match the entities
        /// after it. So a destroyed body (Unity's bool operator: a managed check of the cached native pointer, safe on any
        /// thread) gets one verdict for good - it is never live again, and a later write of the same object (the
        /// chunk-cache reload's entity stubs) must agree with the first. A live body is answered fresh: a world save
        /// snapshots it on the main thread, where the set cannot change between the two passes.
        /// E2: an enclave's body is vetoed the same way (ScEnclavePeople.IsEnclaveBody, its own immutable set): bodies are
        /// Dynamic, but talking to one or hitting it writes Persist, which would save it.
        /// </summary>
        public static bool VetoChunkSave(Entity e)
        {
            if ((object)e == null) return false;
            if (e) return Vetoes(e);
            return unloadedVerdicts.GetValue(e, k => Vetoes(k) ? Vetoed : NotVetoed) == Vetoed;
        }

        /// <summary>A runner's body, an enclave person's (E2) or a band's (E13a, ScBands.IsBandBody: Dynamic, never written either).</summary>
        private static bool Vetoes(Entity e) { return IsRunnerBody(e) || ScEnclavePeople.IsEnclaveBody(e) || ScBands.IsBandBody(e); }

        public static void RebuildRunnerSet()
        {
            var s = new Dictionary<int, KeyValuePair<int, ulong>>();
            foreach (var m in ScRegistry.Members.Values)
                if (IsPhysical(m)) s[m.Id] = new KeyValuePair<int, ulong>(string.IsNullOrEmpty(m.Class) ? 0 : EntityClass.FromString(m.Class), m.Born);
            runnerIds = s;
        }

        public static int Count()
        {
            int n = 0;
            foreach (var m in ScRegistry.Members.Values) if (IsPhysical(m)) n++;
            return n;
        }

        /// <summary>
        /// A loaded entity under an offline runner's id that we did not create ourselves, and that IS the runner
        /// (same class, same birth time): an old copy from a chunk saved before they left. Anything else holding
        /// the id is a stranger who inherited it after a crash rewound the id counter - never touched.
        /// </summary>
        public static bool IsStaleCopy(Member m, EntityAlive npc)
        {
            if (npc == null || !HeldOffline(m) || npc.entityId == RecreatingId) return false;     // round op1: posted staff held offline too
            if (!string.IsNullOrEmpty(m.Class) && !string.Equals(ScCommunity.ClassName(npc), m.Class, StringComparison.OrdinalIgnoreCase)) return false;
            return m.Born == 0 || npc.WorldTimeBorn == 0 || npc.WorldTimeBorn == m.Born;
        }

        // ------------------------------------------------------------------ clock & route ----

        /// <summary>Walking pace in world ticks (TimeOfDayIncPerSec ticks pass per real second).</summary>
        private static float MetresPerTick
        {
            get
            {
                int inc = Math.Max(1, GameStats.GetInt(EnumGameStats.TimeOfDayIncPerSec));
                return Mathf.Max(0.1f, ScConfig.RunWalkMps) / inc;
            }
        }

        public static Vector3 C(Vector3i v) { return new Vector3(v.x + 0.5f, v.y, v.z + 0.5f); }

        public static float XZ(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

        public static void BeginLeg(Member m, RunLeg leg, Vector3 from, Vector3 to, ulong now)
        {
            m.RunLeg = leg;
            m.LegFrom = World.worldToBlockPos(from);
            m.LegTo = World.worldToBlockPos(to);
            m.LegStart = now;
            if (leg == RunLeg.Outbound || leg == RunLeg.Inbound)
                m.LegEnd = now + (ulong)Mathf.CeilToInt(XZ(from, to) / MetresPerTick);
            else if (leg == RunLeg.AtSite)
                m.LegEnd = now + (ulong)Mathf.RoundToInt((m.RunDistress && !m.RunSafe ? ScConfig.RunDistressHours : SiteHours(m)) * TicksPerHour);
            else m.LegEnd = now;
            m.RunEndDay = GameUtils.WorldTimeToDays(now + EtaTicks(m));   // shown in the window; physical runs do not resolve at the rollover
            RebuildRunnerSet();
        }

        /// <summary>The hours a runner spends at the site: RunSiteHours, or (round 33c, M33) a mining run's ScMining.SiteHours.</summary>
        public static float SiteHours(Member m)
        {
#if SC_V34
            if (ScMining.IsDig(m)) return ScMining.SiteHours();
#endif
            return ScConfig.RunSiteHours;
        }

        /// <summary>ScRuns.SendPhysical: the runner is in the world, walking out of the base.</summary>
        public static void OnSent(Member m, EntityAlive npc)
        {
            float t = Time.unscaledTime;
            Session s = S(m.Id);
            s.OnlineSince = t;
            s.ProgressPos = npc.position;
            s.ProgressSince = t;
            s.LastGoodPos = npc.position;
            s.HasGoodPos = true;
            s.HasWaypoint = false;
            s.Sidestep = 0;
            s.LastTickTime = -1f;
            s.NextSnap = t + Math.Max(10f, ScConfig.RunSnapRefreshSeconds);
        }

        /// <summary>Re-plans the current walking leg from where the runner really is (capture, snapshot refresh).</summary>
        public static void ReplanFrom(Member m, Vector3 pos, ulong now)
        {
            if (m.RunLeg == RunLeg.Outbound || m.RunLeg == RunLeg.Inbound) BeginLeg(m, m.RunLeg, pos, C(m.LegTo), now);
        }

        private static ulong EtaTicks(Member m)
        {
            ulong leg = m.LegEnd > m.LegStart ? m.LegEnd - m.LegStart : 0UL;
            if (m.Status == MemberStatus.Transit) return leg;                                  // round op1: no site, and out there they stay
            Vector3 site = C(m.RunSite == Vector3i.zero ? m.LegTo : m.RunSite);
            ulong home = (ulong)(XZ(site, ScCommunity.PostCenter(m)) / MetresPerTick);
            switch (m.RunLeg)
            {
                case RunLeg.Outbound: return leg + (ulong)(SiteHours(m) * TicksPerHour) + home;
                case RunLeg.AtSite: return leg + home;
                default: return leg;
            }
        }

        /// <summary>Where an offline runner is: derived from the leg, never stored.</summary>
        public static Vector3 VirtualPos(Member m, ulong now)
        {
            switch (m.RunLeg)
            {
                case RunLeg.Outbound:
                case RunLeg.Inbound:
                {
                    float t = m.LegEnd <= m.LegStart ? 1f : Mathf.Clamp01((float)(now - Math.Min(now, m.LegStart)) / (m.LegEnd - m.LegStart));
                    return Vector3.Lerp(C(m.LegFrom), C(m.LegTo), t);
                }
                case RunLeg.AtSite: return C(m.RunSite);
                case RunLeg.Back: return C(m.LegTo);
                default: return ScCommunity.PostCenter(m);
            }
        }

        /// <summary>Where the runner is right now: the live entity while online, else the virtual position.</summary>
        public static Vector3 Where(Member m)
        {
            World w = ScCommunity.World;
            if (!m.Offline && w != null)
            {
                EntityAlive npc = ScNpc.As(ScNpc.Loaded(w, m.Id));
                if (npc != null) return npc.position;
            }
            return VirtualPos(m, w != null ? w.worldTime : 0UL);
        }

        public static Vector3 LegGoal(Member m)
        {
            switch (m.RunLeg)
            {
                case RunLeg.Outbound: case RunLeg.Inbound: return C(m.LegTo);
                case RunLeg.AtSite: return C(m.RunSite);
                default: return ScCommunity.PostCenter(m);
            }
        }

        /// <summary>
        /// Round op1 (the window's "about N h" for a posting journey): game hours left on the current walking leg - from the
        /// walker's distance to the leg's goal at the offline pace while their body is in the world (it may be ahead of the plan
        /// or behind it), else from the leg's end on the world clock. 0 on the Back leg and at the end.
        /// </summary>
        public static float HoursLeft(Member m)
        {
            if (m == null || (m.RunLeg != RunLeg.Outbound && m.RunLeg != RunLeg.Inbound)) return 0f;
            World w = ScCommunity.World;
            ulong now = w != null ? w.worldTime : 0UL;
            if (!m.Offline && w != null && IsPhysical(m))
            {
                EntityAlive npc = ScNpc.As(ScNpc.Loaded(w, m.Id));
                if (npc != null) return XZ(npc.position, LegGoal(m)) / MetresPerTick / TicksPerHour;
            }
            return m.LegEnd > now ? (m.LegEnd - now) / (float)TicksPerHour : 0f;
        }

        /// <summary>
        /// The world clock went backwards (an older save, a time command): the leg keeps its length from now. Round op1: shared
        /// by the runner tick and ScGarrison's parked journeys.
        /// </summary>
        public static void ClockBack(Member m, ulong now)
        {
            if (m == null || m.LegStart <= now + TicksPerHour) return;
            ulong len = m.LegEnd > m.LegStart ? m.LegEnd - m.LegStart : 0UL;
            m.LegStart = now;
            m.LegEnd = now + len;
        }

        public static PrefabInstance Dest(Member m)
        {
#if SC_V34
            if (ScMining.IsDig(m)) return null;                                                // round 33c: a natural spot, no POI
#endif
            Session s = S(m.Id);
            if (s.Poi == null || s.PoiKey != m.RunDest) { s.Poi = ScSecurity.PoiByKey(m.RunDest); s.PoiKey = m.RunDest; }
            return s.Poi;
        }

        public static bool Pinned(Member m) { return m.RunLeg == RunLeg.AtSite && m.RunDistress && !m.RunSafe; }

        /// <summary>
        /// The gate: a point on the site-to-post line close enough to the post to always be inside the online radius. Round MP5
        /// (part N): the radius of the community player online with the shortest view distance (GateViewDim), so a runner
        /// waiting at the gate is re-created with any of them at the post; 'player' only when nobody has an observer.
        /// </summary>
        public static Vector3 GatePoint(Member m, Vector3 from, EntityPlayer player)
        {
            Vector3 post = ScCommunity.PostCenter(m);
            int d = GateViewDim(player);
            // always inside the radius a runner can be re-created in (16 x (D - margin) m) with the player at the post
            float dist = Mathf.Clamp(16f * (d - ScConfig.RunOnlineRingMargin) - 8f, 16f, Math.Max(16f, ScConfig.RunGateDistance));
            Vector3 dir = from - post;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1f) dir = Vector3.forward;
            dir.Normalize();
            float reach = Mathf.Min(dist, XZ(from, post));
            Vector3 gate = post + dir * reach;
            gate.y = post.y;
            // on standable ground if the chunk is here to look at (a lake between base and site): walk it back toward the post
            World w = ScCommunity.World;
            if (w != null && w.GetChunkFromWorldPos(Mathf.FloorToInt(gate.x), 0, Mathf.FloorToInt(gate.z)) != null)
            {
                for (float back = reach; back > 4f; back -= 4f)
                {
                    Vector3 p = post + dir * back;
                    int y;
                    if (GroundSpot(w, Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.z), out y)) return new Vector3(Mathf.Floor(p.x) + 0.5f, y, Mathf.Floor(p.z) + 0.5f);
                }
                return post;
            }
            return gate;
        }

        /// <summary>The cell of a POI's footprint nearest a point (where the runner reaches the site).</summary>
        public static Vector3 EdgeNearest(World w, PrefabInstance pi, Vector3 from)
        {
            Vector3i a = pi.boundingBoxPosition, s = pi.boundingBoxSize;
            int x = Mathf.FloorToInt(Mathf.Clamp(from.x, a.x, a.x + s.x - 1));
            int z = Mathf.FloorToInt(Mathf.Clamp(from.z, a.z, a.z + s.z - 1));
            int y;
            if (w == null || !GroundSpot(w, x, z, out y)) y = a.y + Math.Max(0, -pi.prefab.yOffset);
            return new Vector3(x + 0.5f, y, z + 0.5f);
        }

        public static bool InsideXZ(PrefabInstance pi, Vector3 p, int pad)
        {
            Vector3i a = pi.boundingBoxPosition, sz = pi.boundingBoxSize;
            return p.x >= a.x - pad && p.x <= a.x + sz.x + pad && p.z >= a.z - pad && p.z <= a.z + sz.z + pad;
        }

        /// <summary>
        /// A standable cell near the ground in this column. The reference is the TERRAIN height, never the block
        /// heightmap (World.GetHeight is the highest non-air block: the roof anywhere inside a POI, the top of a
        /// wall), and a floor more than 5 blocks above the terrain (a roof, a wall top) is refused.
        /// </summary>
        public static bool GroundSpot(World w, int x, int z, out int y)
        {
            y = 0;
            if (w == null || w.GetChunkFromWorldPos(x, 0, z) == null) return false;
            int terrain = w.GetTerrainHeight(x, z);
            if (!ScCommunity.FindStandable(w, x, z, terrain + 1, out y)) return false;
            return y <= terrain + 5;
        }

        /// <summary>Next waypoint toward a goal: RunWaypointSpacing ahead on standable ground, sidestepping up to 24 m.</summary>
        public static bool NextWaypoint(World w, Vector3 from, int bias, Vector3 goal, out Vector3 wp)
        {
            wp = goal;
            Vector3 d = goal - from;
            d.y = 0f;
            float dist = d.magnitude;
            float spacing = Mathf.Max(8f, ScConfig.RunWaypointSpacing);
            Vector3 dir = dist > 0.01f ? d / dist : Vector3.forward;
            Vector3 raw = dist <= spacing ? goal : from + dir * spacing;
            Vector3 perp = new Vector3(-dir.z, 0f, dir.x);
            for (int k = 0; k <= 3; k++)
            {
                for (int sgn = -1; sgn <= 1; sgn += 2)
                {
                    if (k == 0 && sgn > 0) continue;
                    // the AddLocationLine grid is 64 wide around the line: keep sidesteps within +-24 m
                    Vector3 p = raw + perp * (Mathf.Clamp(bias + sgn * k, -3, 3) * 8f);
                    int x = Mathf.FloorToInt(p.x), z = Mathf.FloorToInt(p.z), y;
                    if (!GroundSpot(w, x, z, out y)) continue;
                    wp = new Vector3(x + 0.5f, y, z + 0.5f);
                    return true;
                }
            }
            return false;
        }

        /// <summary>A spot to wander to inside the destination POI (ground floor only), else just outside it.</summary>
        public static Vector3 SiteWanderGoal(World w, EntityAlive self, Member m)
        {
#if SC_V34
            if (ScMining.IsDig(m))
            {
                // round 33c (M33): working the spot - moving about within 6 m of where they reached it
                Vector3 dig;
                Vector3i c = m.RunSite != Vector3i.zero ? m.RunSite : m.LegTo;
                return WanderSpot(w, self.rand ?? w.GetGameRandom(), new Vector3i(c.x - 6, c.y, c.z - 6), new Vector3i(12, 1, 12), out dig) ? dig : C(c);
            }
#endif
            PrefabInstance pi = Dest(m);
            if (pi == null) return C(m.RunSite);
            Vector3 spot;
            return WanderSpot(w, self.rand ?? w.GetGameRandom(), pi.boundingBoxPosition, pi.boundingBoxSize, out spot) ? spot : C(m.RunSite);
        }

        /// <summary>
        /// A ground-floor spot inside a footprint (min corner a, size sz): eight random columns, else a column on a ring just
        /// outside it (the footprint is all roofs and rubble from there). False when neither has standable ground. A runner's
        /// site wander, and E2's last resort for an enclave person's home spot.
        /// </summary>
        public static bool WanderSpot(World w, GameRandom r, Vector3i a, Vector3i sz, out Vector3 spot)
        {
            int y;
            for (int i = 0; i < 8; i++)
            {
                int x = a.x + 2 + r.RandomRange(0, Math.Max(1, sz.x - 4));
                int z = a.z + 2 + r.RandomRange(0, Math.Max(1, sz.z - 4));
                if (GroundSpot(w, x, z, out y)) { spot = new Vector3(x + 0.5f, y, z + 0.5f); return true; }
            }
            // the footprint is all roofs and rubble from here: walk its edge instead
            float ang = r.RandomFloat * Mathf.PI * 2f;
            Vector3 c = new Vector3(a.x + sz.x * 0.5f, 0f, a.z + sz.z * 0.5f);
            float rad = Mathf.Max(sz.x, sz.z) * 0.5f + 3f;
            int ex = Mathf.FloorToInt(c.x + Mathf.Cos(ang) * rad), ez = Mathf.FloorToInt(c.z + Mathf.Sin(ang) * rad);
            if (GroundSpot(w, ex, ez, out y)) { spot = new Vector3(ex + 0.5f, y, ez + 0.5f); return true; }
            spot = Vector3.zero;
            return false;
        }

        // ------------------------------------------------------------------ the 1 Hz driver ----

        private static int ViewDim(EntityPlayer player)
        {
            return player != null && player.ChunkObserver != null && player.ChunkObserver.viewDim > 0 ? player.ChunkObserver.viewDim : 6;
        }

        /// <summary>Chebyshev chunk distance from the player's observer (int.MaxValue before the observer's first update).</summary>
        private static int Ring(EntityPlayer player, Vector3 pos)
        {
            if (player == null || player.ChunkObserver == null) return int.MaxValue;
            Vector3i cp = player.ChunkObserver.curChunkPos;
            if (cp.x == int.MaxValue || cp.z == int.MaxValue) return int.MaxValue;
            long cx = World.toChunkXZ(Mathf.FloorToInt(pos.x)), cz = World.toChunkXZ(Mathf.FloorToInt(pos.z));
            long r = Math.Max(Math.Abs(cx - cp.x), Math.Abs(cz - cp.z));
            return r > int.MaxValue ? int.MaxValue : (int)r;
        }

        // ------------------------------------------------------------------ round MP5 (part N): every community player ----

        /// <summary>
        /// Round MP5 (part N): a community player this server keeps a chunk observer for - the host's own, a client's (the server
        /// gives every remote player one, with that client's own view distance), a dedicated server's remote players - alive or
        /// dead. The players a runner is weighed against; a living one can keep her body in the world (Keeps) and have it re-created
        /// near them (Reacher). Single player: the one player. MP6: Stage 2's InCommunity says whose community she is.
        /// </summary>
        private static bool Observes(EntityPlayer p)
        {
            return p != null && ScPlayers.InCommunity(p) && p.ChunkObserver != null;
        }

        /// <summary>Round MP5 (part N): any community player has an observer here (single player: the player's own, as Tick asked before).</summary>
        private static bool AnyObserver()
        {
            List<EntityPlayer> all = ScPlayers.All;
            for (int i = 0; i < all.Count; i++) if (Observes(all[i])) return true;
            return false;
        }

        /// <summary>
        /// Round MP5 (part N), the capture rule against every living observer (each by their own ring, view distance and distance):
        /// forced - some player still has her inside their ring D, or within drawing range inside D+1 (the old "must" for one player,
        /// turned round: she is captured by force only when no player keeps her so); voluntary - some player has her inside D -
        /// RunOfflineRingMargin or within drawing range (the old "may", turned round). ring: the nearest living observer's ring, for
        /// the log (int.MaxValue with none). With no living observer neither keeps her: single player's "the player is dead".
        /// </summary>
        private static void Keeps(Vector3 pos, out bool forced, out bool voluntary, out int ring)
        {
            forced = false;
            voluntary = false;
            ring = int.MaxValue;
            List<EntityPlayer> all = ScPlayers.All;
            for (int i = 0; i < all.Count; i++)
            {
                EntityPlayer p = all[i];
                if (!Observes(p) || p.IsDead()) continue;
                int D = ViewDim(p);
                int r = Ring(p, pos);
                float dist = XZ(pos, p.position);
                if (r < ring) ring = r;
                if (!(r >= D + 1 && (dist >= NoDrawRange || r >= D + 2))) forced = true;
                if (!(r >= D - ScConfig.RunOfflineRingMargin && dist >= NoDrawRange)) voluntary = true;
            }
        }

        /// <summary>
        /// Round MP5 (part N): the nearest living observer whose own ring and view distance take pos in for a re-create (ring within
        /// D - RunOnlineRingMargin, within 16 x (D - margin) m), or null - "near whichever player comes". Single player: the player
        /// when alive and near enough, as before.
        /// </summary>
        private static EntityPlayer Reacher(Vector3 pos)
        {
            EntityPlayer best = null;
            float bestDist = float.MaxValue;
            int margin = ScConfig.RunOnlineRingMargin;
            List<EntityPlayer> all = ScPlayers.All;
            for (int i = 0; i < all.Count; i++)
            {
                EntityPlayer p = all[i];
                if (!Observes(p) || p.IsDead()) continue;
                int D = ViewDim(p);
                float dist = XZ(pos, p.position);
                if (Ring(p, pos) > D - margin || dist > 16f * (D - margin) || dist >= bestDist) continue;
                best = p;
                bestDist = dist;
            }
            return best;
        }

        /// <summary>
        /// Round MP5 (part N), vanilla's pop-in rule for every living observer: one within RunNoPopInRange has pos in their view
        /// cone (the server has each remote player's look: their synced rotation). Single player: the player's old test.
        /// </summary>
        private static bool PopInSeen(Vector3 pos)
        {
            List<EntityPlayer> all = ScPlayers.All;
            for (int i = 0; i < all.Count; i++)
            {
                EntityPlayer p = all[i];
                if (!Observes(p) || p.IsDead()) continue;
                if (XZ(pos, p.position) < ScConfig.RunNoPopInRange && p.IsInViewCone(pos)) return true;
            }
            return false;
        }

        /// <summary>
        /// Round MP5 (part N): no observer is within RunSkipMinPlayerDist of pos or has it in view - where the watchdog may hop a
        /// stuck body or hand it to the registry. Dead ones count too (the old test read the one player dead or alive; a body is
        /// forced offline that same tick anyway when nobody is alive). True with no observer at all.
        /// </summary>
        private static bool UnseenByAll(Vector3 pos)
        {
            List<EntityPlayer> all = ScPlayers.All;
            for (int i = 0; i < all.Count; i++)
            {
                EntityPlayer p = all[i];
                if (!Observes(p)) continue;
                if (XZ(pos, p.position) <= ScConfig.RunSkipMinPlayerDist) return false;
                // round MP5 (review N-M1): the view cone is an angle alone - only a player whose loaded ring reaches pos sees it
                // (always so for the one player: a body is only in the world inside their ring)
                if (Ring(p, pos) <= ViewDim(p) + 1 && p.IsInViewCone(pos)) return false;
            }
            return true;
        }

        /// <summary>
        /// Round MP5 (part N): the flat distance from pos to the nearest player in the world (any player, dead or alive: vanilla's
        /// AstarManager lays a grid round every one of them), float.MaxValue with none.
        /// </summary>
        private static float NearestAnyPlayer(Vector3 pos)
        {
            float best = float.MaxValue;
            List<EntityPlayer> all = ScPlayers.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] == null) continue;
                float d = XZ(pos, all[i].position);
                if (d < best) best = d;
            }
            return best;
        }

        /// <summary>
        /// Round MP5 (part N): the shortest view distance among the community players with an observer (GatePoint: the gate is
        /// inside every one's re-create radius), else 'fallback''s (6 without one) - single player: the player's.
        /// </summary>
        private static int GateViewDim(EntityPlayer fallback)
        {
            int best = int.MaxValue;
            List<EntityPlayer> all = ScPlayers.All;
            for (int i = 0; i < all.Count; i++)
                if (Observes(all[i])) best = Math.Min(best, ViewDim(all[i]));
            return best == int.MaxValue ? ViewDim(fallback) : best;
        }

        /// <summary>Round MP5 (part N): for the log - a player's name, "you" for this machine's own (the host reads the log).</summary>
        private static string Who(EntityPlayer p)
        {
            return p is EntityPlayerLocal ? "you" : PlayerName(p);
        }

        /// <summary>ScTicker.OnGameUpdate, right after Relink. Host, main thread, once a second.</summary>
        public static void Tick(World w, EntityPlayer player)
        {
            // round MP0: 'player' is the anchor (the host's own player whenever there is one, as before; on a dedicated server a
            // remote player). Round MP5 (part N): it is still who the tells, the radio and a fresh body's stamps reason from, but
            // the capture, the re-create and the hiding weigh every community player's own observer (Keeps, Reacher, PopInSeen,
            // UnseenByAll) - nothing to weigh with no observer at all (single player: the player's own, as the old test asked)
            // runners already out keep going even if PhysicalRuns was switched off: only new sends read the switch
            if (w == null || player == null || !AnyObserver()) return;
            Hook(w);
            float t = Time.unscaledTime;
            ulong now = w.worldTime;

            if (StaleIds.Count > 0)
            {
                var ids = new List<int>(StaleIds);
                StaleIds.Clear();
                foreach (int id in ids)
                {
                    EntityAlive e = ScNpc.As(w.GetEntity(id));
                    Member sm = ScRegistry.GetMember(id);
                    if (e == null || !IsStaleCopy(sm, e)) continue;
                    ScLog.Out("removing a stale copy of runner " + sm.Name + " (id " + id + ") that a chunk loaded");
                    ClearScoreCaches(id);
                    capturingId = id;
                    try { w.RemoveEntity(id, EnumRemoveEntityReason.Unloaded); }
                    finally { capturingId = -1; }
                }
            }

            scratch.Clear();
            foreach (var m in ScRegistry.Members.Values) if (IsPhysical(m)) scratch.Add(m);
            if (scratch.Count == 0) return;
            int made = 0;
            foreach (Member m in scratch)
            {
                try
                {
                    if (ScRegistry.GetMember(m.Id) != m || !IsPhysical(m)) continue;     // died, left or arrived during this loop
                    ClockBack(m, now);                                                   // the world clock went backwards: the leg keeps its length from now
                    if (!m.Offline)
                    {
                        EntityAlive npc = ScNpc.As(w.GetEntity(m.Id));
                        if (npc == null)
                        {
                            // lost without passing our unload handler: carry on from the last snapshot
                            ScLog.Warn(m.Name + ": the runner's entity is gone while online - continuing from the last snapshot");
                            m.Offline = true;
                            S(m.Id).OfflineSince = t;
                            ScRegistry.SaveAtomic();
                            continue;
                        }
                        if (npc.IsDead()) continue;                                          // SetDead postfix handles it
                        OnlineTick(w, player, m, npc, now, t);
                    }
                    else
                    {
                        AdvanceOffline(w, player, m, now);
                        if (made < Math.Max(1, ScConfig.RunMaterializePerTick) && ScRegistry.GetMember(m.Id) == m && m.Offline && IsPhysical(m)
                            && TryMaterialize(w, player, m, now, t)) made++;
                    }
                }
                catch (Exception e)
                {
                    ScLog.Once("runner-tick-" + m.Id, e);
                }
            }
            scratch.Clear();
        }

        private static void OnlineTick(World w, EntityPlayer player, Member m, EntityAlive npc, ulong now, float t)
        {
            Session s = S(m.Id);
            if (npc.onGround && npc.position.y > 1f) { s.LastGoodPos = npc.position; s.HasGoodPos = true; }

            // measured walking pace (sc runners), so RunWalkMps can be matched to what the AI really does
            if (s.LastTickTime > 0f && (m.RunLeg == RunLeg.Outbound || m.RunLeg == RunLeg.Inbound))
            {
                float dt = t - s.LastTickTime;
                float v = dt > 0.2f ? XZ(npc.position, s.LastTickPos) / dt : 0f;
                if (v < 12f) s.MeasuredMps = s.MeasuredMps <= 0f ? v : s.MeasuredMps * 0.95f + v * 0.05f;
            }
            s.LastTickPos = npc.position;
            s.LastTickTime = t;
#if SC_V34
            // R2: whether they carry a handheld radio, every 5 s while the body is here (the record answers while it is not).
            // Round op1: not on a posting journey - it is logistics, no radio talk and no call for help (RunRadio stays false)
            if (IsRun(m) && t >= s.NextRadioCheck)
            {
                s.NextRadioCheck = t + 5f;
                if (ScHandheld.NoteCarried(m, npc, "seen on the run")) ScRegistry.SaveAtomic();
            }
#endif
            HurtCheck(w, player, m, npc, s, now, t);                                            // round r2: too hurt to go on, nothing to heal with

            switch (m.RunLeg)
            {
                case RunLeg.Outbound when m.Status == MemberStatus.Transit:
                    // round op1: a posting journey ends beside the claim (not at the POI's edge): the walker becomes the post's staff
                    if (XZ(npc.position, C(m.LegTo)) <= ScConfig.RunArriveSite) { ScGarrison.ArriveOnline(w, player, m, npc); return; }
                    break;
                case RunLeg.Outbound:
                {
                    PrefabInstance pi = Dest(m);
                    if (XZ(npc.position, C(m.LegTo)) <= ScConfig.RunArriveSite || (pi != null && InsideXZ(pi, npc.position, 4)))
                    {
                        EnterSite(w, player, m, npc.position, now);
                        s.HasWaypoint = false;
                    }
                    break;
                }
                case RunLeg.AtSite:
                    if (now >= m.LegEnd)
                    {
                        if (Pinned(m)) m.LegEnd = now + TicksPerHour;               // the player is here: the fight decides, not the clock
                        else { LeaveSite(player, m, npc.position, now, false, 1f); s.HasWaypoint = false; }
                    }
                    break;
                case RunLeg.Inbound:
                    if (XZ(npc.position, C(m.LegTo)) <= ScConfig.RunArriveSite || NearHome(m, npc.position)) { EnterBack(player, m, now); s.HasWaypoint = false; }
                    break;
                case RunLeg.Back:
                    if (NearHome(m, npc.position)) { Arrive(player, m, npc); return; }
                    break;
            }

            // an A* grid around a runner the player's own grid does not cover (locations expire after 4 s). Round MP5 (part N): no
            // player's - vanilla lays one round every player in the world, a client's included
            if (s.HasWaypoint && NearestAnyPlayer(npc.position) > ScConfig.RunGridFarFromPlayer && AstarManager.Instance != null)
                AstarManager.Instance.AddLocationLine(npc.position, s.Waypoint, ScConfig.RunGridSize);

            Watchdog(w, player, m, npc, s, t);
            if (ScRegistry.GetMember(m.Id) != m || m.Offline) return;           // the watchdog handed them off

            // periodic snapshot: the crash fallback (never mid medical use - a Write(false) finishes the use)
            if (t >= s.NextSnap && !ScMedic.IsUsing(m.Id))
            {
                string snap;
                if (TryCapture(npc, out snap, Zip(m))) { m.Snap = snap; ReplanFrom(m, npc.position, now); ScRegistry.SaveAtomic(); }
                s.NextSnap = t + Math.Max(10f, ScConfig.RunSnapRefreshSeconds);
            }

            // online -> offline. Round MP5 (part N): against every living community player's own ring, view distance and drawing
            // range (Keeps) - captured only when NO player keeps her, so a runner a client escorts stays in the world for them and
            // keeps walking. One player's death forces nothing; with nobody alive to keep her (single player: the player died)
            // the capture is forced, as before
            bool keptForced, keptMay;
            int ring;
            Keeps(npc.position, out keptForced, out keptMay, out ring);
            if (!npc.CanUpdateEntity()) { if (s.FrozenSince < 0f) s.FrozenSince = t; }      // a chunk without colliders: vanilla freezes it
            else s.FrozenSince = -1f;
            bool eventLive = m.RunDistress && m.EventSpawned;
            // round MP5 (review N-m1): under the server rule a body is made where no colliders are built yet and brings them
            // with it - give the engine longer than RunFrozenSeconds before a forced capture (never so near the host's own player)
            float frozenLimit = ServerRule(npc.position) ? Math.Max(ScConfig.RunFrozenSeconds, ServerFrozenSeconds) : ScConfig.RunFrozenSeconds;
            bool must = !keptForced                                                                   // never pop out inside drawing range if it can wait
                        || (s.FrozenSince >= 0f && t - s.FrozenSince >= frozenLimit);
            bool may = !keptMay && !eventLive && t - s.OnlineSince >= ScConfig.RunOnlineDwellSeconds;
            string rt = ring == int.MaxValue ? "- (no living player)" : ring.ToString();
            if (must || may) GoOffline(w, m, npc, must ? "forced, ring " + rt : "out of range, ring " + rt, true);
        }

        private static bool NearHome(Member m, Vector3 pos)
        {
            return XZ(pos, ScCommunity.PostCenter(m)) <= 38f;   // Station teleports beyond 40 m: walk right in first
        }

        private static void Watchdog(World w, EntityPlayer player, Member m, EntityAlive npc, Session s, float t)
        {
            // The M11 fix 2 (STUCK_DIAG section 7): its clocks run on real time, so the pause menu (single player stops the
            // world's time, not this ticker) or a body that does not tick (a chunk without colliders) counted as a runner
            // making no progress, and a long pause could skip one ahead or send it offline. Frozen time is moved past instead:
            // every clock here is pushed forward by it, so a runner picks up where it was.
            bool frozen = (GameManager.Instance != null && GameManager.Instance.IsPaused()) || npc.ticksExisted == s.WatchTicks;
            float gap = s.WatchAt >= 0f ? t - s.WatchAt : 0f;
            s.WatchAt = t;
            s.WatchTicks = npc.ticksExisted;
            if (frozen)
            {
                if (s.ProgressSince > 0f) s.ProgressSince += gap;
                if (s.SidestepSince > 0f) s.SidestepSince += gap;
                if (s.WaypointSince > 0f) s.WaypointSince += gap;
                return;
            }
            // round r2 review (fix 2b): a new leg starts the hold's clock afresh, and waits for every threat again
            if (s.HeldLeg != m.RunLeg) { s.HeldLeg = m.RunLeg; s.HeldTotal = 0f; s.HeldReleased.Clear(); }
            if (m.RunLeg == RunLeg.AtSite)
            {
                // looking around the site: a wander spot not reached in 40 s is dropped for another
                s.ProgressSince = t;
                if (!Pinned(m) && s.HasWaypoint && t - s.WaypointSince > 40f)
                {
                    s.HasWaypoint = false;
                    if (npc.getNavigator() != null) npc.getNavigator().clearPath();
                }
                return;
            }
            // round r2 review (fix 2b): the route hold's own clock, before "fighting is not being stuck" below resets the rest
            if (HoldClock(w, player, m, npc, s, gap)) return;
            Vector3 goal = LegGoal(m);
            if (s.ProgressSince <= 0f || XZ(npc.position, goal) < XZ(s.ProgressPos, goal) - 2f)
            {
                s.ProgressPos = npc.position;
                s.ProgressSince = t;
                s.Sidestep = 0;
                return;
            }
            if (npc.GetAttackTarget() != null) { s.ProgressSince = t; s.SidestepSince = t; return; }   // fighting is not being stuck
            float still = t - s.ProgressSince;
            // a detour round a wide obstacle makes no progress toward the goal for a while either: only a runner that is
            // actually blocked (or has no path) is sent another way after the first try
            bool blocked = (npc.moveHelper != null && npc.moveHelper.BlockedTime > 1.5f) || npc.getNavigator() == null || npc.getNavigator().noPathAndNotPlanningOne();
            float every = s.Sidestep == 0 ? ScConfig.RunStuckRepathSeconds : ScConfig.RunStuckRepathSeconds * 2f;
            if (still >= ScConfig.RunStuckRepathSeconds && t - s.SidestepSince >= every && (s.Sidestep == 0 || blocked))
            {
                // another way round: alternate sides, a little wider each time (+-1, -1, +2, -2, +3, -3 x 8 m)
                s.Sidestep = s.Sidestep == 0 ? (npc.rand != null && npc.rand.RandomFloat < 0.5f ? -1 : 1)
                           : s.Sidestep > 0 ? -s.Sidestep : Math.Min(3, -s.Sidestep + 1);
                s.SidestepSince = t;
                s.HasWaypoint = false;
                if (npc.getNavigator() != null) npc.getNavigator().clearPath();
            }
            if (still < ScConfig.RunStuckSkipSeconds) return;
            bool unseen = UnseenByAll(npc.position);                                             // round MP5 (part N): unseen by every community player (was the anchor's view)
            bool desperate = still >= ScConfig.RunStuckSkipSeconds * 4f;                         // a minute against a wall, watched or not
            if (!unseen && !desperate) return;
            Vector3 wp;
            // round r2 review (fix 2a): with a threat she would run from on the memo, only a hop that keeps clear of it (never one
            // toward it); with none, the virtual walk carries on instead
            bool threat = RouteMemoFresh(s);
            bool step = threat ? ClearWaypointSafe(w, npc.position, s.Sidestep, goal, s.RouteThreatPos, out wp) : NextWaypoint(w, npc.position, s.Sidestep, goal, out wp);
            if (step && XZ(wp, npc.position) > 4f && (still < ScConfig.RunStuckSkipSeconds * 2f || desperate))
            {
                npc.SetPosition(wp, true);                                                      // hop past the obstacle out of sight
                ScLog.Verbose(m.Name + ": stuck " + still.ToString("0") + " s - skipped ahead to " + World.worldToBlockPos(wp));
                s.ProgressPos = wp;
                s.ProgressSince = t;
                s.Sidestep = 0;
                s.HasWaypoint = false;
            }
            else if (threat && !step)
            {
                ScLog.Out(m.Name + ": stuck " + still.ToString("0") + " s with " + ThreatName(w, s.RouteThreat) + " in the way and no hop clear of it - the walk goes on offline");
                GoOffline(w, m, npc, "stuck behind a threat", false);                         // the virtual walk carries on
            }
            else if (still >= ScConfig.RunStuckSkipSeconds * 2f && (unseen || desperate))
            {
                GoOffline(w, m, npc, "stuck", false);                                          // the virtual walk carries on
            }
        }

        // ------------------------------------------------------------------ legs ----

        private static void EnterSite(World w, EntityPlayer player, Member m, Vector3 at, ulong now)
        {
            if (w != null)
            {
                int y;
                if (GroundSpot(w, Mathf.FloorToInt(at.x), Mathf.FloorToInt(at.z), out y)) at.y = y;
            }
            m.RunSite = World.worldToBlockPos(at);
            m.RunDistress = false;
            m.RunSafe = false;
            m.EventSpawned = false;
            BeginLeg(m, RunLeg.AtSite, at, at, now);
            // distress is rolled on arrival - never with the player standing right there (that is an escort, not a radio call).
            // Round MP5 (part N): any community player there - the anchor first, dead or alive, as before, else any living one:
            // the rescue's own test (ScRadio.RescuerNear; was the anchor or any player, MP4 re-check F4)
            bool escorted = ScRadio.RescuerNear(player, at, ScConfig.RescueTriggerRange) != null;
            // round MP5 (part N): the real player (the anchor) - RollDistress picks the handheld it can read itself (the host's own;
            // a dedicated server: a powered station at home), so a dedicated server rolls distress too
            if (!escorted && ScRadio.RollDistress(m, player, w != null ? w.GetGameRandom() : null)) BeginLeg(m, RunLeg.AtSite, at, at, now);   // re-timed to RunDistressHours
#if SC_V34
            if (!m.RunDistress) ScRadioTalk.Say(m, RadioTalk.Arrived, ScCommunity.LocalPlayer, false);       // R2 (a distress call said its own line); MP4: voices to clients. Round MP5 (part N): the host's own hears it here whoever reasons
#endif
            ScLog.Out(m.Name + " reached " + m.RunDestName + (m.Offline ? " (offline)" : "") + (m.RunDistress ? " - and radios for help" : ""));
            ScRegistry.SaveAtomic();
        }

        /// <summary>The site search is over: bank the find (prorated for a recall) and head for the gate.</summary>
        public static void LeaveSite(EntityPlayer player, Member m, Vector3 at, ulong now, bool rescued, float share)
        {
#if SC_V34
            if (ScMining.IsDig(m)) m.RunCarry = ScMining.TakeHaul(m, share, rescued);           // round 33c (M33): real items, onto RunHaul
            else
#endif
            {
                // round ie1: whole bundles - a recall brings the share of the search done, rounded down; a rescue +RescueBundles
                int y = (int)Math.Floor(ScRuns.Yield(m, m.RunKind) * Mathf.Clamp01(share) + 1e-4f);   // the epsilon: float noise in the share
                m.RunCarry = rescued ? y + ScConfig.RescueBundles : y;
            }
            m.RollPending = !rescued;
            BeginLeg(m, RunLeg.Inbound, at, GatePoint(m, at, player), now);
#if SC_V34
            // R2: a good haul or a poor one on the radio - not after a rescue (you were there) or a failed distress call (hurt);
            // a recall marked both as said already
            if (!rescued && !m.Injured) ScRadioTalk.OnLeftSite(m, ScCommunity.LocalPlayer);   // round MP0: the local player's radio (another slice's file). MP4: voices to clients. Round MP5 (part N): the host's own whoever recalled
#endif
            ScLog.Out(m.Name + " leaves " + m.RunDestName + " with " + m.RunCarry + " " + Carried(m) + (rescued ? " (rescued)" : ""));
            ScRegistry.SaveAtomic();
        }

        /// <summary>At the gate: the haul counts now, whether or not the player is home to see it.</summary>
        internal static void EnterBack(EntityPlayer player, Member m, ulong now)
        {
            if (m.Status == MemberStatus.Transit)
            {
                // round op1: walking home from an outpost - nothing to bank, nothing said on the radio; the last stroll in, as a runner's
                Vector3 at = C(m.LegTo);
                BeginLeg(m, RunLeg.Back, at, at, now);
                ScLog.Out(m.Name + " is at the edge of the base, home from " + m.RunDestName + (m.Offline ? " (offline: walks in once you are near)" : ""));
                ScRegistry.SaveAtomic();
                return;
            }
#if SC_V34
            ScRadioTalk.Say(m, RadioTalk.Near, ScCommunity.LocalPlayer, false);                 // R2: "Almost home." Round MP0: the local player's radio (another slice's file). MP4: voices to clients. Round MP5 (part N): the host's own whoever reasons
#endif
            // round r2: she gave the job up hurt - she comes home injured (the community's flag: treated with meds at the rollover),
            // unless she was patched up on the way (her body here, above the line ScMedic bandages at); with her body not here to
            // read (offline), injured
            if (m.RunHurt && !m.Injured)
            {
                EntityAlive body = m.Offline ? null : ScCommunity.FindNpc(m.Id);
                if (body == null || body.IsDead() || body.Health <= body.GetMaxHealth() * ScMedic.HealBelow) m.Injured = true;
            }
            bool visited = m.RunSite != Vector3i.zero;                                          // recalled before reaching the site: nothing found, nothing learnt
#if SC_V34
            if (visited && ScMining.IsDig(m)) ScMining.AtGate(player, m);                       // round 33c (M33): the haul into the home crates
            else
#endif
            if (visited)
            {
                ScRegistry.Community.Add(m.RunKind, m.RunCarry);
                ScSkills.AddXp(m, Skill.Wits, ScConfig.RunWitsXp);
                if (ScRuns.SpecialtyFor(m.RunKind) == m.Specialty) ScSkills.AddSpecialtyXp(m, 3f);
                // round MP0: a runner's homecoming is the community's news (every community player; single player: the player)
                // round MP4 (part: tells): this file's tells go by localization key (ScCommunity.TellKey / ScPlayers.TellCommunityKey), so each player reads them in their own language
                if (player != null) ScPlayers.TellCommunityKey(m.Injured ? "scRunHurt" : "scRunBack", m.Injured ? "ui_denied" : "", m.Name, m.RunCarry,
                                                               ScCommunity.LocArg(ScNames.LocKey(m.RunKind)));   // the resource's word: a key
            }
            else if (player != null) ScPlayers.TellCommunityKey("scRunBackEmpty", "", m.Name, m.RunDestName);
            ScLog.Out(m.Name + " is back from " + m.RunDestName + (visited ? " with " + m.RunCarry + " " + Carried(m) : " empty-handed (turned back early)") + (m.Injured ? " (injured)" : ""));
            m.RunCarry = 0;
            m.RollPending = false;                                                              // an escorted runner reaching home was never rolled
            Vector3 gate = C(m.LegTo);
            BeginLeg(m, RunLeg.Back, gate, gate, now);
            ScRegistry.SaveAtomic();
        }

        /// <summary>Inside the zone / near the post: a resident again, saved with their chunk as before.</summary>
        public static void Arrive(EntityPlayer player, Member m, EntityAlive npc)
        {
            bool posting = m.Status == MemberStatus.Transit;                                  // round op1: home from an outpost
            string from = m.RunDestName;
            ScCommunity.ClearSynced(npc, OnRunCvar);      // round MP2 (E9): clients get the 0 (the talk rows gate on it)
            ClearRun(m);
            if (posting) { m.OutpostId = -1; m.Status = MemberStatus.Resident; }                // Station says so too, unless it defers (downed)
            ScCommunity.Station(m, npc);                                                     // Resident, Guard order; no teleport within 40 m
            RebuildRunnerSet();
            ScRegistry.SaveAtomic();
            if (posting) ScGarrison.HomeAgain(m, player, from);
            ScLog.Out(m.Name + " walked back in and is at their post again");
        }

        public static void ClearRun(Member m) { ClearRun(m, false); }

        /// <summary>The run's fields back to none. keepSnap (round op1): an arrived posting traveler keeps the snapshot - it is them.</summary>
        public static void ClearRun(Member m, bool keepSnap)
        {
            m.RunLeg = RunLeg.None;
            m.Offline = false;
            if (!keepSnap) m.Snap = null;
            m.RunEndDay = -1;
            m.RunDest = Vector3i.zero;
            m.RunDestName = "";
            m.RunSite = Vector3i.zero;
            m.LegFrom = Vector3i.zero;
            m.LegTo = Vector3i.zero;
            m.LegStart = 0;
            m.LegEnd = 0;
            m.RunCarry = 0;
            m.RollPending = false;
            m.RunHurt = false;                                                                  // round r2
            m.RunDistress = false;
            m.RunSafe = false;
            m.EventSpawned = false;
#if SC_V34
            m.RunRadio = false;                                                                 // R2: the run's radio state ends with it
            m.RunTalk = 0;
            ScMining.ClearJob(m);                                                               // round 33c: a haul still to put away stays (RunHaul)
#endif
            sessions.Remove(m.Id);
        }

        /// <summary>The log's word for a runner's carry: the resource, or (round 33c) a mining run's items.</summary>
        private static string Carried(Member m)
        {
#if SC_V34
            if (ScMining.IsDig(m)) return "item(s) [" + m.RunHaul + "]";
#endif
            return m.RunKind.ToString();
        }

        private static void AdvanceOffline(World w, EntityPlayer player, Member m, ulong now)
        {
            switch (m.RunLeg)
            {
                case RunLeg.Outbound when m.Status == MemberStatus.Transit:
                    if (now >= m.LegEnd) ScGarrison.ArriveOffline(player, m);                   // round op1: they reached the claim out of sight
                    return;                                                                     // no dice on a posting journey
                case RunLeg.Outbound:
                    if (now >= m.LegEnd) EnterSite(w, player, m, C(m.LegTo), now);
                    break;
                case RunLeg.AtSite:
                    if (now < m.LegEnd) break;
                    if (Pinned(m)) { DistressTimedOut(w, player, m, now); return; }
                    LeaveSite(player, m, C(m.RunSite), now, false, 1f);
                    break;
                case RunLeg.Inbound:
                    if (now >= m.LegEnd) EnterBack(player, m, now);
                    break;
            }
            if (m.RollPending && ScRegistry.GetMember(m.Id) == m) RollRisk(w, player, m);
        }

        private static float Risk(Member m)
        {
            return Mathf.Clamp(ScConfig.RunRiskBase - ScConfig.RunRiskPerCombatStar * (m.Stars(Skill.Fighting) + m.Stars(Skill.Shooting) - 2)
                               - ScOutpostPerks.EscortRisk(), 0.02f, 0.5f);
        }

        /// <summary>The run's dice, rolled once and only while the runner is out of the player's sight.</summary>
        private static void RollRisk(World w, EntityPlayer player, Member m)
        {
            m.RollPending = false;
            GameRandom rnd = w.GetGameRandom();
            float risk = Risk(m);
            float roll = rnd.RandomFloat, roll2 = rnd.RandomFloat;
            if (roll >= risk) { ScRegistry.SaveAtomic(); return; }
            if (roll2 < ScConfig.RunDeathShare)
            {
                ScLog.Out(m.Name + " did not survive the way home (risk " + risk.ToString("0.00") + ")");
                DieOffline(player, m, "scRunDied");
                return;
            }
            m.Injured = true;
            m.RunCarry = Math.Max(0, m.RunCarry / 2);
#if SC_V34
            ScMining.Halve(m);                                                                  // round 33c: half the items too
#endif
            ScLog.Out(m.Name + " was hurt on the way home (risk " + risk.ToString("0.00") + ")");
            ScRegistry.SaveAtomic();
        }

        private static void DistressTimedOut(World w, EntityPlayer player, Member m, ulong now)
        {
            ScRadio.EndDistress(w, player, m);                                                // the rescue quest closes as failed - every player's copy (round MP5, part N: the real player; MP4's ScQuestNet closes them all)
            if (w.GetGameRandom().RandomFloat < ScConfig.RunDeathShare) { DieOffline(player, m, "scRescueDied"); return; }
            m.Injured = true;
            LeaveSite(player, m, C(m.RunSite), now, false, 1f);
            m.RunCarry = Math.Max(0, m.RunCarry / 2);
#if SC_V34
            ScMining.Halve(m);                                                                  // round 33c: half the items too
#endif
            m.RollPending = false;
            ScRegistry.SaveAtomic();
        }

        /// <summary>An offline runner dies: no body anywhere; any old copy of theirs is removed when it loads.</summary>
        private static void DieOffline(EntityPlayer player, Member m, string key)
        {
            int id = m.Id;
            ScCommunity.RecordDeath(m, player, key);     // round MP0: any player - the news goes to the community
            ScRegistry.Doomed.Add(id);
            ScLooks.Forget(id);
            World w = ScCommunity.World;
#if SC_V34
            // round 29a (J29): what her inventory window held is lost with her on the road - said and logged, not silent
            ScJobs.LoseWindow(w, m, id, player);
#else
            try { if (w == null || w.GetEntity(id) == null) HarvestManager.Remove(id); } catch (Exception e) { ScLog.Once("runner-harvest", e); }
#endif
            sessions.Remove(id);
            RebuildRunnerSet();
            ScRegistry.SaveAtomic();
        }

        /// <summary>
        /// The deadline fallback (the rollover, RunDeadlineGraceDays past the ETA, offline only): a route that never
        /// finishes - the virtual walk cannot get stuck, but a re-create can fail for ever - ends at the gate.
        /// </summary>
        /// <returns>true when this brought them to the gate with their haul (the daily summary says so)</returns>
        public static bool ForceHome(Member m, EntityPlayer player)
        {
            if (!IsPhysical(m)) return false;
            // round op1: a posting journey's walking legs end on their own clock (no site, no dice): only one waiting at the gate is
            // ever brought in here
            if (m.Status == MemberStatus.Transit && m.RunLeg != RunLeg.Back) return false;
            World w = ScCommunity.World;
            if (w == null) return false;
            ulong now = w.worldTime;
            ScLog.Warn(m.Name + ": the run is " + ScConfig.RunDeadlineGraceDays + " day(s) past its ETA (" + m.RunLeg + ") - bringing them home");
            if (m.RunLeg == RunLeg.Outbound || m.RunLeg == RunLeg.AtSite)
            {
                if (m.RunDistress) ScRadio.EndDistress(w, player, m);                          // round MP5 (part N): the real player - every player's copy closes (ScQuestNet)
                if (m.RunSite == Vector3i.zero) m.RunSite = m.LegTo;
                LeaveSite(player, m, C(m.RunSite), now, false, 1f);
            }
            if (m.RollPending) RollRisk(w, player, m);
            if (ScRegistry.GetMember(m.Id) != m) return false;                                 // died
            if (m.RunLeg == RunLeg.Inbound) { EnterBack(player, m, now); return true; }
            if (m.RunLeg != RunLeg.Back) return false;
            if (m.LegTo != m.Post && m.Post != Vector3i.zero)
            {
                // the gate never worked out (water, a cliff): wait at the post instead - re-created there when the base loads
                m.LegTo = m.Post;
                m.LegFrom = m.Post;
                S(m.Id).CreateFailures = 0;
                ScRegistry.SaveAtomic();
                return false;
            }
            // Only a re-create that actually failed ends the run without a body: a runner who simply has not been
            // near enough to the player since (waiting at the post, you 70 m away) keeps waiting - and their gear.
            Session fs = S(m.Id);
            bool classGone = !string.IsNullOrEmpty(m.Class) && !ScCommunity.ClassExists(EntityClass.FromString(m.Class));
            if (fs.SpotFailures == 0 && fs.CreateFailures == 0 && !classGone)
            {
                ScLog.Out(m.Name + " waits at the post to be re-created - they come in when you are near");
                return false;
            }
            // not even at the post: the run ends without a body. They are a resident again; the old body a chunk may
            // still hold is adopted, or the Community window offers Call back (their record respawns them)
            ScLog.Warn(m.Name + " could not be brought back into the world at the post - the run ends, use Call back if they do not appear");
            ClearRun(m);
            m.Status = MemberStatus.Resident;
            m.OutpostId = -1;                                                                   // round op1: one walking home from an outpost too
            m.MissingDays = 0;
            RebuildRunnerSet();
            ScRegistry.SaveAtomic();
            return false;
        }

        /// <summary>
        /// Is the player close enough to the post for a runner waiting there to be re-created? Round MP5 (part N): ANY community
        /// player online, each by their own view distance (dead or alive: the old position test read the one player either way);
        /// 'player' is no longer read - kept for the callers. Single player: the player, as before.
        /// </summary>
        public static bool PlayerNearPost(Member m, EntityPlayer player)
        {
            if (m == null) return false;
            Vector3 post = ScCommunity.PostCenter(m);
            List<EntityPlayer> all = ScPlayers.All;
            for (int i = 0; i < all.Count; i++)
            {
                EntityPlayer p = all[i];
                if (p == null || !ScPlayers.InCommunity(p)) continue;
                if (XZ(p.position, post) <= 16f * (ViewDim(p) - ScConfig.RunOnlineRingMargin)) return true;
            }
            return false;
        }

        /// <summary>Recall from the window or the dialog: turn round now (a site visit cut short brings a share of the find).</summary>
        public static bool Recall(Member m, EntityPlayer player, out string error)
        {
            error = null;
            if (!ScCommunity.IsHost) { error = "scHostOnly"; return false; }
            // round op1: a posting traveler met on the road ("Head home now", sc run recall) - ScGarrison turns them round
            if (m != null && m.Status == MemberStatus.Transit) return ScGarrison.TurnHome(m, player, null, out error);   // the caller says "turns for home"
            if (!IsPhysical(m)) { error = "scRunNotOut"; return false; }
            World w = ScCommunity.World;
            if (w == null) { error = "scInternal"; return false; }
            ulong now = w.worldTime;
            Vector3 at = Where(m);
            switch (m.RunLeg)
            {
                case RunLeg.Outbound:
#if SC_V34
                    ScRadioTalk.Mark(m, RadioTalk.Out | RadioTalk.Found | RadioTalk.Empty | RadioTalk.Home);   // R2: turned round, nothing more to report on the way
#endif
                    m.RunCarry = 0;
                    m.RollPending = false;
                    BeginLeg(m, RunLeg.Inbound, at, GatePoint(m, at, player), now);
                    break;
                case RunLeg.AtSite:
                {
                    if (Pinned(m)) { error = "scRunPinned"; return false; }
#if SC_V34
                    ScRadioTalk.Mark(m, RadioTalk.Found | RadioTalk.Empty | RadioTalk.Home);   // R2: LeaveSite below says no haul line
#endif
                    float span = m.LegEnd > m.LegStart ? m.LegEnd - m.LegStart : 1f;
                    float share = Mathf.Clamp01((float)(now - Math.Min(now, m.LegStart)) / span);
                    LeaveSite(player, m, at, now, m.RunSafe, share);
                    break;
                }
                default:
                    error = "scRunHeadingHome";
                    return false;
            }
            S(m.Id).HasWaypoint = false;
            ScRegistry.SaveAtomic();
            ScLog.Out(m.Name + " was recalled (" + m.RunLeg + ", carrying " + m.RunCarry + ")");
            return true;
        }

        // ------------------------------------------------------------------ too hurt to go on (round r2) ----

        /// <summary>
        /// Round r2 (the user, 2026-11-15: "If their health is low and they have no way of healing, they should abandon their job
        /// and return to base"): is she too hurt for the job - on the way out or at the site, at or below RunHomeBelowHealth
        /// with no way to heal (ScMedic.HurtNoMeds: no first aid bandage or kit in her pack or bag, no use or heal already
        /// working)? One on the way home, or who already gave the job up, is past the question. A pure read.
        /// </summary>
        public static bool TooHurtForTheJob(Member m, EntityAlive npc)
        {
            // round op1: a run's question only - a posting journey is not a job to give up (it is safe: the walk goes on)
            return IsRun(m) && !m.RunHurt && (m.RunLeg == RunLeg.Outbound || m.RunLeg == RunLeg.AtSite)
                   && ScMedic.HurtNoMeds(npc, ScConfig.RunHomeBelowHealth);
        }

        /// <summary>
        /// Round r2, once a second while she is in the world (OnlineTick): too hurt for the job for HurtConfirmSeconds in a row
        /// and she gives it up - never on one reading. While you have her inventory or a talk with her open (you may be handing
        /// her the bandage she lacks) the count is dropped, and starts again once you close it. While the game is paused nothing
        /// is decided, but the count is not dropped: the time it was paused still counts, so she may turn on the first tick after.
        /// </summary>
        private static void HurtCheck(World w, EntityPlayer player, Member m, EntityAlive npc, Session s, ulong now, float t)
        {
            if (!TooHurtForTheJob(m, npc)) { s.HurtSince = -1f; return; }
            if (GameManager.Instance != null && GameManager.Instance.IsPaused()) return;
            // round MP0: a talk is the local player's window (ScQuests, another slice's file). MP2: window. Round MP5 (part N): her
            // inventory window counts for every player (ScAmmo.IsWindowOpen knows a remote open); a remote player's open talk with
            // a runner is not known to the server (no talk hold for a runner: ScWork holds residents only) - the host's own is
            EntityPlayerLocal lp = ScCommunity.LocalPlayer;
            if (ScAmmo.IsWindowOpen(m.Id) || (lp != null && ScQuests.TalkingTo(lp) == m.Id)) { s.HurtSince = -1f; return; }
            if (s.HurtSince < 0f) { s.HurtSince = t; return; }
            if (t - s.HurtSince < HurtConfirmSeconds) return;
            if (!TurnHomeHurt(w, player, m, npc, now, false, true)) s.HurtSince = t;           // not turned (fix 6): asked again in HurtConfirmSeconds
        }

        /// <summary>
        /// Round r2 review (fix 4): the capture paths' turn (GoOffline, a chunk unload) - its own try, so a throw in it can never
        /// cost the capture itself. True when she turned.
        /// </summary>
        private static bool TurnHomeHurtAtCapture(World w, Member m, EntityAlive npc, bool despawnNow)
        {
            try
            {
                // round MP0: the anchor (the host's own player whenever there is one, as before). Round MP5 (part N): kept - it only
                // names who the tells and the radio reason from; the gate (GatePoint) and the rescuer (any community player at the
                // call) no longer read it
                return TooHurtForTheJob(m, npc) && TurnHomeHurt(w, ScPlayers.Anchor(), m, npc, w != null ? w.worldTime : 0UL, true, despawnNow);
            }
            catch (Exception e)
            {
                ScLog.Once("runner-hurt-capture-" + (m != null ? m.Id : -1), e);
                return false;
            }
        }

        /// <summary>
        /// Round r2: she gives the job up and heads home. The run turns Inbound from where she stands, as a recall turns it, and
        /// stays turned: the legs only go on from Inbound to the gate, and TooHurtForTheJob never asks again (RunHurt). The haul is
        /// a recall's - nothing on the way out, the share of the site time she worked at the site (LeaveSite, the dice to come
        /// while she is out of sight). A distress call she is pinned by ends first: with you there fighting beside her - the event
        /// is live and you are where ScRadio.TickDistress judges a rescue from (within RescueTriggerRange of the call's spot,
        /// loaded) - the rescue counts: ScRadio.Rescued, which completes the quest and turns her home with the rescue's haul and no
        /// dice, as every rescue does. Otherwise the call ends unanswered (EndDistress: the quest fails). capturing: her body is
        /// leaving the world (GoOffline, a chunk unload) - a live event resets as she goes (walking away is never a rescue:
        /// ScRadio.OnRunnerOffline), so the call ends unanswered then too; despawnNow is false inside an unload callback, where
        /// other entities must not be removed. With no local player to close the quest (review fix 6) a pinned runner is not
        /// turned at all: she stays pinned, and no quest is left in progress with nobody to close it. The radio moments the turn
        /// makes moot are marked said. RunHurt is set once the leg has turned (review fix 4: also when a throw comes after it).
        /// True when she turned.
        /// </summary>
        internal static bool TurnHomeHurt(World w, EntityPlayer player, Member m, EntityAlive npc, ulong now, bool capturing, bool despawnNow)
        {
            bool distress = m.RunDistress && !m.RunSafe;
            // round MP4 (merge): the rescue quest is every community player's copy now (ScQuestNet), closed by ScRadio.Rescued /
            // EndDistress for all of them - no local player is needed any more, so a dedicated server no longer pins her
            int hp = npc != null ? npc.Health : 0, max = npc != null ? npc.GetMaxHealth() : 0;
            Vector3 at = npc != null ? npc.position : Where(m);
            RunLeg was = m.RunLeg;
            Session s = S(m.Id);
            s.HurtSince = -1f;
            // round MP4 (merge): any community player there fighting beside her counts (the first one found), not only the anchor
            EntityPlayer rescuer = null;
            if (ScZone.IsLoaded(m.RunSpawn))
            {
                if (player != null && !player.IsDead() && XZ(player.position, C(m.RunSpawn)) <= ScConfig.RescueTriggerRange) rescuer = player;
                else
                {
                    List<EntityPlayer> all = ScPlayers.All;
                    for (int i = 0; i < all.Count && rescuer == null; i++)
                    {
                        EntityPlayer q = all[i];
                        if (q != null && !q.IsDead() && ScPlayers.InCommunity(q) && XZ(q.position, C(m.RunSpawn)) <= ScConfig.RescueTriggerRange) rescuer = q;
                    }
                }
            }
            bool youThere = rescuer != null;
            bool rescue = distress && m.EventSpawned && !capturing && w != null && youThere;
            try
            {
                // the decision, once (RunHurt keeps it from coming again); what it leads to logs its own lines after
                ScLog.Out(m.Name + " is hurt (hp " + hp + "/" + max + ") with no way to heal - giving up the " + JobWords(m) + " and heading home ("
                          + "was " + was + (Pinned(m) ? ", pinned by a distress call" : "") + (capturing ? ", turned as they left the world" : "") + ")"
                          + (rescue ? " - you are there fighting beside them, so the rescue counts" : distress ? " - their distress call ends unanswered" : ""));
#if SC_V34
                // no haul line and no "heading home" line: the hurt line below says it (and, turned on the way out, no "I'm here")
                ScRadioTalk.Mark(m, RadioTalk.Found | RadioTalk.Empty | RadioTalk.Home | (was == RunLeg.Outbound ? RadioTalk.Out | RadioTalk.Arrived : RadioTalk.None));
#endif
                if (rescue) ScRadio.Rescued(w, rescuer, m);                                     // the quest completes (every copy); LeaveSite turns her home
                else if (distress)
                {
                    if (capturing && w != null) ScRadio.OnRunnerOffline(w, m, despawnNow);      // a live event resets as she leaves the world
                    ScRadio.EndDistress(w, player, m);                                          // the call ends unanswered: its quest fails (every copy)
                }
                if (m.RunLeg == RunLeg.Outbound)
                {
                    m.RunCarry = 0;
                    m.RollPending = false;
                    BeginLeg(m, RunLeg.Inbound, at, GatePoint(m, at, player), now);
                }
                else if (m.RunLeg == RunLeg.AtSite)
                {
                    float span = m.LegEnd > m.LegStart ? m.LegEnd - m.LegStart : 1f;
                    float share = Mathf.Clamp01((float)(now - Math.Min(now, m.LegStart)) / span);
                    LeaveSite(player, m, at, now, m.RunSafe, share);
                }
            }
            finally
            {
                // on the way home now (whatever came after the turn): never asked again, and injured at the gate
                if (m.RunLeg != was && m.RunLeg != RunLeg.Outbound && m.RunLeg != RunLeg.AtSite) m.RunHurt = true;
                s.HasWaypoint = false;                                                          // re-review R3: never the old site waypoint first
            }
            if (!capturing && npc != null && npc.getNavigator() != null) npc.getNavigator().clearPath();
            if (was == RunLeg.Outbound) ScLog.Out(m.Name + " turned back before reaching " + m.RunDestName + " (nothing found yet)");   // LeaveSite logs the haul at the site
            SayHurtHome(m, player);
            ScRegistry.SaveAtomic();
            return m.RunHurt;
        }

        /// <summary>The log's word for the job she gives up.</summary>
        private static string JobWords(Member m)
        {
#if SC_V34
            if (ScMining.IsDig(m)) return m.RunJob == RunJob.Clay ? "clay run" : "mining run";
#endif
            return "supply run";
        }

        /// <summary>
        /// Round r2: she tells you she is coming home hurt - in person when her body is within ScRadioTalk.InPersonRange of you
        /// (a message, radio or not), else on the radio (RadioTalk.HurtHome: the caption alone, there is no recording; heard only
        /// when she carries a radio and you carry one too, as every radio line). Spent either way.
        /// </summary>
        private static void SayHurtHome(Member m, EntityPlayer player)
        {
#if SC_V34
            // round MP0: ScRadioTalk takes the local player (another slice's file) - null on a dedicated server. MP4: voices to clients
            EntityPlayerLocal lp = player as EntityPlayerLocal;
            // round MP5 (part N): every remote community player beside her is told in person too (their machine drops the radio
            // line as "in person" by itself, MP4) - the host's own as before
            bool hostThere = lp != null && ScRadioTalk.InPerson(m, lp);
            List<EntityPlayer> all = ScPlayers.All;
            for (int i = 0; i < all.Count; i++)
            {
                EntityPlayer p = all[i];
                if (p == null || p is EntityPlayerLocal || p.IsDead() || !ScPlayers.InCommunity(p) || !ScRadioTalk.InPerson(m, p)) continue;
                ScCommunity.TellKey(p, "scRunHurtHome", "ui_denied", m.Name);
            }
            // round MP5 (review N-m2): the host beside her is told in person, and Say still runs - it spends the moment, sends
            // the radio line to the remote players elsewhere, and plays nothing here for a host she stands beside (InPerson)
            if (hostThere) ScCommunity.TellKey(lp, "scRunHurtHome", "ui_denied", m.Name);
            ScRadioTalk.Say(m, RadioTalk.HurtHome, lp, false);
#endif
        }

        // ------------------------------------------------------------------ the route and what she runs from (round r2) ----

        private static readonly List<Entity> threatScratch = new List<Entity>();

        /// <summary>
        /// Round r2: would one of SC's flee actions run her from an enemy - ScFlee (a coward: the $Timid ScTraits writes) or
        /// ScFleeHurt (ScMedic.HurtNoMeds at FleeBelowHealth)? A pure read.
        /// </summary>
        public static bool WouldFlee(EntityAlive self)
        {
            if (self == null || self.Buffs == null) return false;
            if (self.Buffs.GetCustomVar("$Timid") >= 1f) return true;
            return ScMedic.HurtNoMeds(self, ScConfig.FleeBelowHealth);
        }

        /// <summary>
        /// The nearest awake, living enemy of hers (SCore's IsEnemy) within FleeClear m that her flight could start on, or null.
        /// Review fix 3: only one that is after her (her as its attack or revenge target) or that can see her - the test ScFlee
        /// and ScFleeHurt run on (vanilla's SelfVisible: the enemy's CanEntityBeSeen of her, its see distance, view cone and line
        /// of sight) - so a zombie behind a wall, or one that has not noticed her, never holds her. Not the one the hold's clock
        /// let go of (HeldReleased). The sight test (a ray) is only made for one nearer than the best so far. Main thread.
        /// </summary>
        private static EntityAlive NearestThreat(EntityAlive self, Session s)
        {
            World w = self.world;
            if (w == null) return null;
            threatScratch.Clear();
            w.GetEntitiesInBounds(typeof(EntityAlive), new Bounds(self.position, new Vector3(2f * FleeClear, 2f * FleeClear, 2f * FleeClear)), threatScratch);
            EntityAlive best = null;
            float bestD2 = FleeClear * FleeClear;
            for (int i = 0; i < threatScratch.Count; i++)
            {
                EntityAlive a = threatScratch[i] as EntityAlive;
                if (a == null || a == self || a.IsDead() || a.IsSleeping || s.HeldReleased.Contains(a.entityId)) continue;
                float d2 = (a.position - self.position).sqrMagnitude;
                if (d2 > bestD2 || !EntityTargetingUtilities.IsEnemy(self, a)) continue;
                bool after = a.GetAttackTarget() == self || a.GetRevengeTarget() == self;
                if (!after && !a.CanEntityBeSeen(self)) continue;
                best = a;
                bestD2 = d2;
            }
            threatScratch.Clear();
            return best;
        }

        /// <summary>Round r2 review: the route memo names a threat and is recent (UAI refreshes it every RouteCacheSeconds while it asks).</summary>
        private static bool RouteMemoFresh(Session s)
        {
            return s.RouteThreat >= 0 && Time.time < s.RouteUntil + 1f;
        }

        /// <summary>For the log: the threat's class and id.</summary>
        private static string ThreatName(World w, int id)
        {
            Entity e = w != null && id >= 0 ? w.GetEntity(id) : null;
            string cls = ScCommunity.ClassName(e as EntityAlive);
            return (string.IsNullOrEmpty(cls) ? "an enemy" : cls) + " #" + id;
        }

        /// <summary>
        /// Round r2 review (fix 2b), from the stuck watchdog once a second on the way out, home or in: the hold's own clock. Every
        /// second her walk is held (the memo fresh and held) counts, fighting or not - the watchdog's "fighting is not being stuck"
        /// reset never touches it - up to HoldMaxSeconds in all on this leg. Then, out of your sight, she goes offline and the
        /// registry walks her on; in your sight she stops waiting for that threat (HeldReleased): the walk goes on, and she still
        /// runs from it or fights it as before. Each logs once. True when she went offline (the watchdog stops there).
        /// </summary>
        private static bool HoldClock(World w, EntityPlayer player, Member m, EntityAlive npc, Session s, float gap)
        {
            if (!s.RouteHeldNow || !RouteMemoFresh(s)) return false;
            s.HeldTotal += Mathf.Clamp(gap, 0f, 2f);
            if (s.HeldTotal < HoldMaxSeconds) return false;
            s.HeldTotal = 0f;
            string what = ThreatName(w, s.RouteThreat);
            bool unseen = UnseenByAll(npc.position);                                             // round MP5 (part N): every community player's view (was the anchor's)
            if (unseen)
            {
                ScLog.Out(m.Name + ": held " + HoldMaxSeconds.ToString("0") + " s in all by " + what + " with no way round, out of your sight - the walk goes on offline");
                if (GoOffline(w, m, npc, "held by a threat", false)) return true;
            }
            s.HeldReleased.Add(s.RouteThreat);                                                   // for the rest of this leg (R1)
            s.RouteUntil = -1f;                                                                 // the memo again, without it
            ScLog.Out(m.Name + ": held " + HoldMaxSeconds.ToString("0") + " s in all by " + what + " in your sight - stops waiting for it and moves on (still runs from it or fights it)");
            return false;
        }

        /// <summary>The closest the straight walk from a to b comes to p, on the ground plane.</summary>
        internal static float PassDistance(Vector3 a, Vector3 b, Vector3 p)
        {
            a.y = 0f;
            b.y = 0f;
            p.y = 0f;
            Vector3 ab = b - a;
            float len2 = ab.sqrMagnitude;
            float k = len2 > 0.0001f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2) : 0f;
            return Vector3.Distance(a + ab * k, p);
        }

        /// <summary>
        /// Round r2: the next waypoint for a runner who would run from a threat at threatPos - the one NextWaypoint plans (with
        /// her sidestep) when the straight walk to it stays out of FleeRadius of the threat, else one RunWaypointSpacing ahead
        /// and 8 to 24 m to the side away from it. Already inside FleeRadius (a threat that cannot see her does not start the
        /// flight), a walk that never takes her nearer than she is will do - she is never left standing beside it. False when
        /// none does, or the goal itself lies within FleeRadius of it: she cannot go on without walking back into what she ran
        /// from (ScRouteClear holds her then).
        /// </summary>
        public static bool ClearWaypoint(World w, Vector3 from, int sidestep, Vector3 goal, Vector3 threatPos, out Vector3 wp)
        {
            wp = goal;
            if (XZ(goal, threatPos) < FleeRadius) return false;
            float need = Mathf.Min(FleeRadius, XZ(from, threatPos) - 0.5f);
            if (NextWaypoint(w, from, sidestep, goal, out wp) && PassDistance(from, wp, threatPos) >= need) return true;
            Vector3 d = goal - from;
            d.y = 0f;
            Vector3 dir = d.sqrMagnitude > 0.0001f ? d.normalized : Vector3.forward;
            Vector3 perp = new Vector3(-dir.z, 0f, dir.x);                                     // NextWaypoint's positive side
            Vector3 toThreat = threatPos - from;
            toThreat.y = 0f;
            int away = Vector3.Dot(toThreat, perp) > 0f ? -1 : 1;
            for (int k = 1; k <= 3; k++)
                if (NextWaypoint(w, from, away * k, goal, out wp) && PassDistance(from, wp, threatPos) >= need) return true;
            return false;
        }

        /// <summary>ClearWaypoint for the watchdog's hop (review fix 2a): a throw is logged once and means no hop clear of it.</summary>
        private static bool ClearWaypointSafe(World w, Vector3 from, int sidestep, Vector3 goal, Vector3 threatPos, out Vector3 wp)
        {
            wp = goal;
            try { return ClearWaypoint(w, from, sidestep, goal, threatPos, out wp); }
            catch (Exception e) { ScLog.Once("runner-route-hop", e); return false; }
        }

        /// <summary>
        /// Round r2 review (TravelSC.PathTo): her next step toward goal - NextWaypoint, or with a threat she would run from
        /// (RouteThreat) a step that keeps clear of it (ClearWaypoint; at the site, wander, up to three other wander spots are
        /// tried). False with blocked: no step keeps her clear - she waits (ScRouteClear holds the walk while the threat is
        /// close). False without: no standable step at all (the watchdog sidesteps, skips or hands off). A throw in the threat
        /// part is logged once and falls back to the plain step.
        /// </summary>
        public static bool PlanStep(World w, EntityAlive self, Member m, Vector3 goal, bool wander, out Vector3 wp, out bool blocked)
        {
            blocked = false;
            Session s = S(m.Id);
            try
            {
                Vector3 threat;
                if (RouteThreat(self, m, out threat))
                {
                    bool ok = ClearWaypoint(w, self.position, s.Sidestep, goal, threat, out wp);
                    for (int i = 0; !ok && wander && i < 3; i++) ok = ClearWaypoint(w, self.position, s.Sidestep, SiteWanderGoal(w, self, m), threat, out wp);
                    if (ok) return true;
                    blocked = true;
                    return false;
                }
            }
            catch (Exception e)
            {
                ScLog.Once("runner-route-step", e);
                blocked = false;
            }
            return NextWaypoint(w, self.position, s.Sidestep, goal, out wp);
        }

        /// <summary>
        /// Round r2 (ScTravel's ScRouteClear gate, TravelSC): is her walk held? She would run (WouldFlee), a threat she could run
        /// from (NearestThreat: after her, or able to see her) is within FleeClear m, and no way on keeps her out of FleeRadius
        /// of it - on the way ClearWaypoint finds no step round it; at the site (review fix 3) it is within FleeRadius of her or
        /// of the spot she works (her walks there choose spots clear of anything farther off: PlanStep). This is what ends the
        /// flee/travel loop: the moment a flight took her past FleeRadius, ScTravel (4.5) used to win again and walk her straight
        /// back to the same waypoint, in among what she ran from, which started the flight again - for as long as it stood there.
        /// Memoized for RouteCacheSeconds in her session (UAI asks several times a second, and this sweeps for enemies); only a
        /// physical runner in the world is ever held, and the memo is the only thing written. A throw (review fix 5) is logged
        /// once and means not held, with no threat on the memo - the walk as before.
        /// </summary>
        public static bool RouteHeld(EntityAlive self, Member m)
        {
            if (self == null || !IsPhysical(m) || m.Offline || m.Id != self.entityId) return false;
            Session s = S(m.Id);
            float now = Time.time;
            if (now < s.RouteUntil) return s.RouteHeldNow;
            s.RouteUntil = now + RouteCacheSeconds;
            s.RouteHeldNow = false;
            s.RouteThreat = -1;
            try
            {
                if (!WouldFlee(self)) return false;
                EntityAlive t = NearestThreat(self, s);
                if (t == null) return false;
                s.RouteThreat = t.entityId;
                s.RouteThreatPos = t.position;
                if (m.RunLeg == RunLeg.AtSite)
                {
                    Vector3 spot = C(m.RunSite != Vector3i.zero ? m.RunSite : m.LegTo);
                    s.RouteHeldNow = XZ(t.position, self.position) < FleeRadius || XZ(t.position, spot) < FleeRadius;
                    return s.RouteHeldNow;
                }
                Vector3 wp;
                s.RouteHeldNow = !ClearWaypoint(self.world, self.position, s.Sidestep, LegGoal(m), t.position, out wp);
                return s.RouteHeldNow;
            }
            catch (Exception e)
            {
                ScLog.Once("runner-route-held", e);
                s.RouteHeldNow = false;
                s.RouteThreat = -1;
                return false;
            }
        }

        /// <summary>Round r2 (TravelSC): the threat she would run from, if any (RouteHeld's memo, refreshed when stale). Never throws.</summary>
        public static bool RouteThreat(EntityAlive self, Member m, out Vector3 threatPos)
        {
            threatPos = Vector3.zero;
            try
            {
                RouteHeld(self, m);
                Session s;
                if (m == null || !sessions.TryGetValue(m.Id, out s) || s.RouteThreat < 0) return false;
                threatPos = s.RouteThreatPos;
                return true;
            }
            catch (Exception e)
            {
                ScLog.Once("runner-route-threat", e);
                return false;
            }
        }

        /// <summary>
        /// Round r2 (TravelSC.Update): a threat she would run from has moved onto the walk to her waypoint - plan the step again
        /// (round it), at most every RouteCacheSeconds. False while the walk is held (ScRouteClear takes it off her). Never throws.
        /// </summary>
        public static bool ThreatOnWay(EntityAlive self, Member m)
        {
            try
            {
                Session s;
                if (m == null || m.RunLeg == RunLeg.AtSite || !sessions.TryGetValue(m.Id, out s) || !s.HasWaypoint) return false;
                float now = Time.time;
                if (now < s.RouteReplanAt) return false;
                if (RouteHeld(self, m) || s.RouteThreat < 0) return false;
                float need = Mathf.Min(FleeRadius, XZ(self.position, s.RouteThreatPos) - 0.5f);       // as ClearWaypoint
                if (PassDistance(self.position, s.Waypoint, s.RouteThreatPos) >= need) return false;
                s.RouteReplanAt = now + RouteCacheSeconds;
                return true;
            }
            catch (Exception e)
            {
                ScLog.Once("runner-route-onway", e);
                return false;
            }
        }

        // ------------------------------------------------------------------ offline -> online ----

        private static bool TryMaterialize(World w, EntityPlayer player, Member m, ulong now, float t)
        {
            Session s = S(m.Id);
            if (!s.SwapIn && t - s.OfflineSince < ScConfig.RunOfflineDwellSeconds) return false;
            Vector3 v = VirtualPos(m, now);
            // round MP5 (part N): near ANY living community player - the nearest one whose own ring and view distance take the spot
            // in (Reacher; was the anchor alone) - and out of the view of every living player near it (PopInSeen, below)
            EntityPlayer near = Reacher(v);
            if (near == null) { s.SettledSince = -1f; s.PopInBlockedSince = -1f; s.SwapIn = false; return false; }
            // vanilla's pop-in rule: nothing appears within 50 m in front of the player - given up after a while so a
            // runner waiting at the gate still comes home while the player looks that way. Round op1: not for the body that
            // takes a stand-in's place on its spot (SwapIn) - the stand-in just went from there. Round MP5 (part N): in front of
            // any community player
            if (!s.SwapIn && PopInSeen(v))
            {
                if (s.PopInBlockedSince < 0f) s.PopInBlockedSince = t;
                if (t - s.PopInBlockedSince < ScConfig.RunPopInMaxWait) return false;
            }
            if (!Settled(w, v)) { s.SettledSince = -1f; return false; }
            if (s.SettledSince < 0f) { s.SettledSince = t; return false; }                      // settled for one full tick first

            bool rekey = false;
            Entity occupant = w.GetEntity(m.Id);                                                // also flushes a pending chunk-stub create
            if (occupant != null)
            {
                EntityAlive sdx = ScNpc.As(occupant);
                if (sdx != null && IsStaleCopy(m, sdx)) { StaleIds.Add(m.Id); return false; }     // removed next tick, then we come back
                rekey = true;                                                                    // a stranger has the id
            }
            else if (w.entityAsyncManager != null && w.entityAsyncManager.IsEntityPending(m.Id)) return false;
            if (ScRegistry.Doomed.Contains(m.Id) || ScRegistry.Cleanup.Contains(m.Id)) rekey = true;
            return Materialize(w, player, near, m, v, now, t, rekey);
        }

        /// <summary>
        /// The re-create. player: the tick's anchor (a fresh body's stamps, the re-key's quest); near (round MP5, part N): the player
        /// it comes back near (Reacher) - the log's distance.
        /// </summary>
        private static bool Materialize(World w, EntityPlayer player, EntityPlayer near, Member m, Vector3 v, ulong now, float t, bool rekey)
        {
            Session s = S(m.Id);
            Vector3 spot;
            if (!SpotNear(w, v, out spot))
            {
                if (!(m.RunLeg == RunLeg.Back && SpotNear(w, ScCommunity.PostCenter(m), out spot)))
                {
                    s.SpotFailures++;
                    return false;
                }
                // the post, not the gate: the pop-in rule was judged at the gate, judge it here (round MP5, part N: every player's view)
                if (PopInSeen(spot))
                {
                    if (s.PopInBlockedSince < 0f) s.PopInBlockedSince = t;
                    if (t - s.PopInBlockedSince < ScConfig.RunPopInMaxWait) return false;
                }
            }
            EntityCreationData ecd = Deserialize(m.Snap);
            bool fresh = ecd == null || !ScCommunity.ClassExists(ecd.entityClass) || s.CreateFailures >= MaxCreateFailures;
            int cid = fresh ? EntityClass.FromString(m.Class) : ecd.entityClass;
            if (!ScCommunity.ClassExists(cid))
            {
                s.CreateFailures++;
                ScLog.Once("runner-noclass-" + m.Id, m.Name + ": the survivor class " + m.Class + " no longer exists - the runner cannot come back into the world");
                return false;
            }
            if (fresh && s.CreateFailures < MaxCreateFailures)
                ScLog.Warn(m.Name + ": the snapshot is " + (ecd == null ? "missing or unreadable" : "for a class that no longer exists") + " - a fresh body walks on in their place (gear they carried is lost)");
            bool newId = rekey || fresh;
            Vector3 face = LegGoal(m) - spot;
            face.y = 0f;
            Vector3 rot = new Vector3(0f, face.sqrMagnitude > 0.01f ? Quaternion.LookRotation(face).eulerAngles.y : 0f, 0f);

            int oldId = m.Id;
            EntityAlive e = null;
            var sw = Stopwatch.StartNew();
            RecreatingId = newId ? -1 : m.Id;
            try
            {
                // a fresh body is built like every other respawn (ScSpawn: the record's class, look and weapons; the name
                // follows below); a snapshot under a new id gets the look and the weapons handed over the same way, the id
                // not being the member's yet (its own saved cvars would give the same pair; the record is the truth)
                if (fresh) e = ScSpawn.Create(cid, spot + new Vector3(0f, 0.002f, 0f), rot, m.Look, m.Loadout, m);   // their recorded ammo too (ScAmmo)
                else
                {
                    if (newId && m.Look != null) { ScLooks.Pending = m.Look.Clone(); ScLooks.PendingName = m.Name; }   // round 31: the name for a hair color fix's log line
                    if (newId && m.Loadout != null) ScLoadout.Pending = m.Loadout.Clone();
                    ecd.pos = spot + new Vector3(0f, 0.002f, 0f);                               // VehicleManager nudges y the same way
                    ecd.rot = rot;
                    ecd.id = rekey ? -1 : m.Id;                                                 // never the id inside the blob: after a re-key it is the old one
                    Restoring = true;                                                           // its PostInit leaves the bag to AfterRunnerRestore
                    try { e = ScNpc.As(EntityFactory.CreateEntity(ecd)); }
                    finally { Restoring = false; }
                    // round 30 review L1: a member's body keeps its name - not only through the snapshot's saved data (Read's mark)
                    if (e != null) ScStreamerNames.Keep(e);
                }
                if (e == null) { s.CreateFailures++; ScLog.Warn(m.Name + ": re-create returned no entity (" + s.CreateFailures + ")"); return false; }
                w.SpawnEntityInWorld(e);
            }
            catch (Exception ex)
            {
                s.CreateFailures++;
                ScLog.Once("runner-create-" + m.Id, ex);
                return false;
            }
            finally
            {
                RecreatingId = -1;
                ScLooks.Pending = null;
                ScLooks.PendingName = null;
                ScLoadout.Pending = null;
            }
            sw.Stop();

            // SCore's SetupBagItems re-appends the class BagItems on every PostInit: put the saved bag back
#if SC_V34
            // 3.4: the snapshot carries the bag as a blob (EntityCreationData.bagData); read into the live bag, which takes its size
            if (!fresh && ecd.bagData != null && ecd.bagData.Length > 0 && e.bag != null)
                StreamUtils.FromBlob(ecd.bagData, pbr => e.bag.ReadInto(pbr, StreamModeRead.Persistency));
#else
            if (!fresh && ecd.bag != null && e.bag != null) e.bag.SetSlots(ecd.bag.Clone().GetSlots());
#endif
            // M11 re-check R1: the window move, the saved gun's loose fallback and the loose rounds into magazines that
            // AfterPostInit left out, now on the bag that stays
            if (!fresh) ScLoadout.AfterRunnerRestore(e);
            if (newId)
            {
                Rekey(w, player, m, e, oldId, fresh);
                string resnap;
                if (TryCapture(e, out resnap, Zip(m))) m.Snap = resnap;                         // the crash fallback under the new id
            }
            s = S(m.Id);
            if (fresh)
            {
                ScSpawn.Identify(e, m, true);
                e.Buffs.SetCustomVar("Persist", 1f);
                ScCommunity.StampCvars(m, e, player);
                ScTraits.ApplyBuffs(m, e);
            }
            e.SetSpawnerSource(EnumSpawnerSource.StaticSpawner);
            if (e.Buffs.HasCustomVar("onMission") || !e.enabled) ScNpc.SendOnMission(e, false);
            ScCommunity.ApplyAiPackage(e);
            ApplyTravelOrder(e);
            m.Offline = false;
            s.CreateFailures = 0;
            s.SpotFailures = 0;
            s.OnlineSince = t;
            s.SettledSince = -1f;
            s.PopInBlockedSince = -1f;
            s.SwapIn = false;                                                                   // round op1: once
            s.ProgressPos = e.position;
            s.ProgressSince = t;
            s.HasWaypoint = false;
            s.LastGoodPos = spot;
            s.HasGoodPos = true;
            s.LastTickTime = -1f;
            s.NextSnap = t + Math.Max(10f, ScConfig.RunSnapRefreshSeconds);
            s.LastCreateMs = sw.ElapsedMilliseconds;
            RebuildRunnerSet();
            ScRegistry.SaveAtomic();                                                            // Snap is kept: it is the crash fallback
            EntityPlayer by = near ?? player;                                                   // round MP5 (part N): who she came back near
            ScLog.Out(m.Name + " is back in the world at " + World.worldToBlockPos(spot) + " (" + m.RunLeg + ", " + XZ(spot, by.position).ToString("0")
                      + " m from " + Who(by) + ", entity " + m.Id + (newId ? ", was " + oldId : "") + ", built in " + sw.ElapsedMilliseconds + " ms)");
            return true;
        }

        /// <summary>
        /// A member came back under a new entity id - a runner re-created under a new id, or a missing resident called
        /// back (ScMissing.Restore): move every record keyed by the old one. fresh: a new body, so it takes the new birth time.
        /// </summary>
        public static void Rekey(World w, EntityPlayer player, Member m, EntityAlive e, int oldId, bool fresh)
        {
            ScRegistry.Members.Remove(oldId);
            m.Id = e.entityId;
            if (fresh || m.Born == 0) m.Born = e.WorldTimeBorn;
            ScRegistry.Members[m.Id] = m;
            Community c = ScRegistry.Community;
            if (c.ThreatMemberId == oldId) c.ThreatMemberId = m.Id;
            ScRegistry.Doomed.Add(oldId);                                                       // an old copy that loads later goes (a stranger is never touched)
            ScLooks.Forget(oldId);
            ScQuests.RekeyRescue(player, oldId, m.Id);
            Session s;
            if (sessions.TryGetValue(oldId, out s)) { sessions.Remove(oldId); sessions[m.Id] = s; }
#if SC_V34
            // round 29a (J29): her SCore inventory window (HarvestManager's container, keyed by the entity id) comes with her
            ScJobs.CarryWindow(w, m, oldId, e, fresh);
#else
            try { if (w.GetEntity(oldId) == null) HarvestManager.Remove(oldId); } catch (Exception ex) { ScLog.Once("runner-harvest", ex); }
#endif
            ScLog.Warn(m.Name + ": entity id " + oldId + " -> " + m.Id + (fresh ? " (fresh body)" : " (the old id is taken by another entity)"));
        }

        /// <summary>A standable ground spot near the virtual position (rings 3 blocks apart, up to 18 m out).</summary>
        private static bool SpotNear(World w, Vector3 v, out Vector3 spot)
        {
            spot = v;
            int bx = Mathf.FloorToInt(v.x), bz = Mathf.FloorToInt(v.z);
            for (int k = 0; k <= 6; k++)
            {
                for (int dx = -k; dx <= k; dx++)
                {
                    for (int dz = -k; dz <= k; dz++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != k) continue;
                        int x = bx + dx * 3, z = bz + dz * 3, y;
                        if (!GroundSpot(w, x, z, out y)) continue;
                        spot = new Vector3(x + 0.5f, y, z + 0.5f);
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>The VehicleManager gate plus the 3x3 settled checks: loaded, decorated, lit, colliders built, not brand new (E2's enclave bodies too).</summary>
        internal static bool Settled(World w, Vector3 v)
        {
            return SettledWhy(w, v) == null;
        }

        /// <summary>
        /// Round MP0.1: why a spot is not settled (null = settled) - Settled's test, for `sc settled` too.
        /// A server builds a collision-only chunk's colliders only around entities (ChunkManager.GenerateCollidersOnlyAroundEntites:
        /// an empty chunk's are freed and never rebuilt until an entity comes; the MP0 test on a dedicated server, 2026-11-23:
        /// empty chunks next to an enclave stayed collisionMesh=False for good, so its people were never made). Where a REMOTE
        /// player is near - a dedicated server, or a client's area on a listen host - colliders are not required: a body spawned
        /// there brings its chunk's colliders (the engine builds them around entities), as vanilla's own spawns do. Near the
        /// host's own player only (single player always) the test is the old one, colliders included.
        /// </summary>
        internal static string SettledWhy(World w, Vector3 v)
        {
            bool remote = ServerRule(v);
            if (!remote && !w.IsChunkAreaCollidersLoaded(v)) return "colliders not built (the VehicleManager gate)";
            int cx = World.toChunkXZ(Mathf.FloorToInt(v.x)), cz = World.toChunkXZ(Mathf.FloorToInt(v.z));
            List<long> fresh = w.newlyLoadedChunksThisUpdate;
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    Chunk ch = w.GetChunkSync(cx + dx, cz + dz) as Chunk;
                    // the reason's text only once something fails (the review's nit: this runs for every spot SC weighs, every tick)
                    if (ch == null) return "not loaded" + At(cx + dx, cz + dz);
                    if (ch.InProgressUnloading) return "unloading" + At(cx + dx, cz + dz);
                    if (ch.NeedsDecoration) return "not decorated yet" + At(cx + dx, cz + dz);
                    if (ch.NeedsLightCalculation) return "not lit yet" + At(cx + dx, cz + dz);
                    // near a remote player no colliders are required: a chunk an entity never entered was never given any (the engine
                    // skips it before it even marks it collision-only), and one that loses its last entity has them freed
                    if (!remote && !ch.IsCollisionMeshGenerated) return "colliders not built" + At(cx + dx, cz + dz);
                    if (fresh != null)
                    {
                        long key = WorldChunkCache.MakeChunkKey(cx + dx, cz + dz);
                        lock (fresh) { if (fresh.Contains(key)) return "loaded this very update" + At(cx + dx, cz + dz); }
                    }
                }
            }
            return null;
        }

        private static string At(int x, int z) { return " (chunk " + x + "," + z + ")"; }

        /// <summary>
        /// Round MP0.1: whether SettledWhy's server rule applies at v - a remote player is near (the chunks must still be loaded,
        /// which is what really bounds it: the review's S2, so the range is generous) and the spot is not in the host's own view,
        /// where the host's renderer builds the chunks and the old rule holds (the review's S1).
        /// </summary>
        internal static bool ServerRule(Vector3 v)
        {
            if (!ScPlayers.AnyRemoteWithin(v, RemoteSettleRange)) return false;
            EntityPlayerLocal host = ScCommunity.LocalPlayer;
            if (host == null) return true;
            float dx = host.position.x - v.x, dz = host.position.z - v.z;
            return dx * dx + dz * dz > HostViewRange * HostViewRange;
        }

        /// <summary>Round MP0.1: how near a remote player makes the server rule apply (loaded chunks bound it anyway).</summary>
        private const float RemoteSettleRange = 512f;
        /// <summary>Round MP5 (review N-m1): a frozen body under the server rule waits this long for its colliders before a forced capture.</summary>
        private const float ServerFrozenSeconds = 15f;
        /// <summary>Round MP0.1 (the review's S1): within this of the host's own player its renderer builds the chunks - the old rule.</summary>
        private const float HostViewRange = 160f;

        /// <summary>The Task order: no resident action runs, the pack's combat does, and ScTravel walks the route.</summary>
        public static void ApplyTravelOrder(EntityAlive npc)
        {
            npc.Buffs.RemoveBuff("buffOrderGuard");
            npc.Buffs.RemoveCustomVar("Guarding");
            // round mt1 (part H): clients get a 0 (a removal never reaches them, and their copy kept her last leader), then the
            // server removes them as before - HasTravelOrder still finds them absent
            ScCommunity.ClearSynced(npc, "Leader");
            ScCommunity.ClearSynced(npc, "Owner");
            SphereCache.LeaderCache.Remove(npc.entityId);
            if (npc.Buffs.GetCustomVar("CurrentOrder") != OrderTask) npc.Buffs.SetCustomVar("CurrentOrder", OrderTask);   // SetCurrentOrder is a no-op for Task
            ScCommunity.SetSynced(npc, OnRunCvar, 1f);    // round MP2 (E9): force-sent to clients
        }

        public static bool HasTravelOrder(EntityAlive npc)
        {
            return npc.Buffs.GetCustomVar("CurrentOrder") == OrderTask && !npc.Buffs.HasCustomVar("Leader") && !npc.Buffs.HasCustomVar("Owner");
        }

        // ------------------------------------------------------------------ online -> offline ----

        /// <summary>Snapshot, registry saved, then the clean removal. forced: keep the previous snapshot when a fresh one fails.</summary>
        public static bool GoOffline(World w, Member m, EntityAlive npc, string why, bool forced)
        {
            if (w == null || m == null || npc == null || npc.IsDead() || npc.IsMarkedForUnload()) return false;
            // round r2: too hurt for the job as she leaves the world - she turns home now, with her body here to read; offline her
            // health cannot change, so the walk the registry carries on is the way home, never more hours at the site
            TurnHomeHurtAtCapture(w, m, npc, true);
            int id = npc.entityId;
            string snap;
            if (TryCapture(npc, out snap, Zip(m))) m.Snap = snap;
            else if (!forced || string.IsNullOrEmpty(m.Snap)) { ScLog.Warn(m.Name + ": snapshot failed - staying in the world (" + why + ")"); return false; }
#if SC_V34
            if (IsRun(m)) ScHandheld.NoteCarried(m, npc, "went offline");                     // R2: the record answers until the body is back (round op1: runs only)
#endif
            ReplanFrom(m, npc.position, w.worldTime);
            m.Offline = true;
            Session s = S(id);
            s.OfflineSince = Time.unscaledTime;
            s.HasWaypoint = false;
            s.SettledSince = -1f;
            ScRadio.OnRunnerOffline(w, m);                                                    // a live distress event resets
            ScRegistry.SaveAtomic();                                                          // BEFORE the removal: a crash now finds Offline + a fresh snapshot
            ClearScoreCaches(id);
            capturingId = id;
            try { w.RemoveEntity(id, EnumRemoveEntityReason.Unloaded); }
            finally { capturingId = -1; }
            ScLog.Out(m.Name + " went offline (" + why + ") at " + World.worldToBlockPos(npc.position) + ", " + m.RunLeg + ", snapshot " + SnapBytes(m) + " bytes");
            return true;
        }

        /// <summary>
        /// Round op1: a member starts (or goes on) offline right now, with no body in the world - a posting traveler whose walk home
        /// begins at the outpost from their snapshot. swapIn: they take the place of the stand-in just removed on that spot (the
        /// next re-create skips the dwell and the pop-in wait, Session.SwapIn).
        /// </summary>
        public static void OfflineNow(Member m, bool swapIn)
        {
            Session s = S(m.Id);
            s.OfflineSince = Time.unscaledTime;
            s.HasWaypoint = false;
            s.SettledSince = -1f;
            s.PopInBlockedSince = -1f;
            s.SwapIn = swapIn;
        }

        /// <summary>
        /// Round op1: a body that waited parked at home (hidden, saved with its chunk) - a posting traveler's, or a posted member's
        /// (ScGarrison.TryEmbody) - is taken into a snapshot and out of the world, so from now on the registry carries them like a
        /// runner: a traveler can be met on the road, a posted member walks home from the outpost for real. The leg is
        /// left exactly as it was (unlike GoOffline, which re-plans from where the body stands: this one stands at home while the
        /// walk is somewhere along the way). The snapshot and the save come before the removal, as in GoOffline; a capture that
        /// fails leaves them parked. Removing the body marks its chunk modified, so the next chunk save drops it; a copy an older
        /// chunk file still holds is a stale copy (HeldOffline) and goes when it loads.
        /// </summary>
        public static bool Embody(World w, Member m, EntityAlive npc, string why)
        {
            if (w == null || m == null || npc == null || npc.IsDead() || npc.IsMarkedForUnload() || npc.entityId != m.Id) return false;
            string snap;
            if (!TryCapture(npc, out snap, true))
            {
                // the review's NIT: said once per member (a retry comes every EmbodyRetrySeconds)
                ScLog.Once("embody-fail-" + m.Id, m.Name + ": could not take their parked body into a snapshot (" + why + ") - it stays parked at home for now");
                return false;
            }
            int id = npc.entityId;
            m.Snap = snap;
            m.Offline = true;
            OfflineNow(m, false);
            // a traveler is physical from now on (the chunk-save veto knows them); a posted member is HeldOffline - for either, the
            // body itself is removed just below, and its chunk is saved without it
            RebuildRunnerSet();
            ScRegistry.SaveAtomic();                                                            // BEFORE the removal: a crash now finds the snapshot
            ClearScoreCaches(id);
            capturingId = id;
            try { w.RemoveEntity(id, EnumRemoveEntityReason.Unloaded); }
            finally { capturingId = -1; }
            ScLog.Out(m.Name + "'s parked body was taken into a snapshot (" + why + ") - " + m.Status + (m.RunLeg != RunLeg.None ? " " + m.RunLeg : "")
                      + ", snapshot " + SnapBytes(m) + " bytes, " + SnapChars(m) + " characters in the registry");
            return true;
        }

        /// <summary>SCore's per-id caches of a body leaving the world (a runner's, an enclave person's, E2).</summary>
        internal static void ClearScoreCaches(int id)
        {
            try
            {
                SphereCache.LeaderCache.Remove(id);
                SphereCache.RemovePaths(id);
                SphereCache.DoorCache.Remove(id);
                SphereCache.LastBlock.Remove(id);
                SphereCache.BreakingBlockCache.Remove(id);
            }
            catch (Exception e)
            {
                ScLog.Once("runner-caches", e);
            }
        }

        private static void Hook(World w)
        {
            if (hooked == w) return;
            Unhook();
            w.EntityUnloadedDelegates += OnEntityUnloaded;                                    // fired BEFORE OnEntityUnload: the entity is intact
            hooked = w;
        }

        private static void Unhook()
        {
            if (hooked != null) { try { hooked.EntityUnloadedDelegates -= OnEntityUnloaded; } catch (Exception) { } }
            hooked = null;
        }

        /// <summary>
        /// Backstop: a chunk unload (teleport, bed respawn, fast travel) or a fall out of the world beat the 1 Hz tick. Round MP5
        /// (part N): already every player's - the server unloads a chunk only once no player's observer holds it any more (a
        /// client leaving, dying or teleporting away from the runner it escorted included), so this is "nobody near" by itself.
        /// </summary>
        private static void OnEntityUnloaded(Entity e, EnumRemoveEntityReason reason)
        {
            try
            {
                if (e == null || e.entityId == capturingId || !ScCommunity.IsHost || !ScRegistry.Loaded) return;
                EntityAlive npc = ScNpc.As(e);
                if (npc == null) return;
                Member m = ScRegistry.GetMember(npc.entityId);
                if (!IsPhysical(m) || m.Offline || npc.IsDead()) return;
                bool fell = npc.position.y < 0f && (reason == EnumRemoveEntityReason.Killed || reason == EnumRemoveEntityReason.Despawned);
                if (reason != EnumRemoveEntityReason.Unloaded && !fell) return;
                // round r2: as GoOffline - too hurt for the job, she turns home before the snapshot (no removals in here: despawnNow false)
                TurnHomeHurtAtCapture(npc.world, m, npc, false);
                Session s = S(m.Id);
                string snap;
                if (TryCapture(npc, out snap, Zip(m))) m.Snap = snap;
                else ScLog.Warn(m.Name + ": capture at unload failed - keeping the previous snapshot");
#if SC_V34
                if (IsRun(m)) ScHandheld.NoteCarried(m, npc, "captured at an unload");       // R2: the record answers until the body is back (round op1: runs only)
#endif
                ulong now = npc.world != null ? npc.world.worldTime : 0UL;
                Vector3 from = fell ? (s.HasGoodPos ? s.LastGoodPos : VirtualPos(m, now)) : npc.position;
                ReplanFrom(m, from, now);
                m.Offline = true;
                s.OfflineSince = Time.unscaledTime;
                s.HasWaypoint = false;
                ScRadio.OnRunnerOffline(npc.world, m, false);                                   // inside an unload: despawn next tick
                // belt and braces should the IsSavedToFile postfix ever fail: a Delete-sourced copy destroys itself on
                // load, and MarkToUnload unhooks SCore's chunk-visible delegate
                npc.SetSpawnerSource(EnumSpawnerSource.Delete);
                if (!npc.IsMarkedForUnload()) npc.MarkToUnload();
                ClearScoreCaches(m.Id);
                ScRegistry.SaveAtomic();
                ScLog.Out(m.Name + " was captured as their chunk unloaded (" + reason + ")");
            }
            catch (Exception ex)
            {
                ScLog.Once("runner-unload-capture", ex);
            }
        }

        /// <summary>WorldShuttingDown, BEFORE ScRegistry.FlushAndReset and before the game saves the world.</summary>
        public static void OnWorldShuttingDown()
        {
            try
            {
                World w = ScCommunity.World;
                if (!ScCommunity.IsHost || !ScRegistry.Loaded || w == null) return;
                var online = new List<Member>();
                foreach (var m in ScRegistry.Members.Values) if (IsPhysical(m) && !m.Offline) online.Add(m);
                foreach (Member m in online)
                {
                    EntityAlive npc = ScNpc.As(w.GetEntity(m.Id));
                    if (npc == null || !GoOffline(w, m, npc, "quit", true)) { m.Offline = true; ScRegistry.SaveAtomic(); }
                }
            }
            catch (Exception e)
            {
                ScLog.Once("runner-shutdown", e);
            }
            finally
            {
                Unhook();
            }
        }

        /// <summary>ScRegistry.Load: only a crash leaves a runner online on disk - it resumes from its last snapshot.</summary>
        public static void OnRegistryLoaded()
        {
            sessions.Clear();
            StaleIds.Clear();
            foreach (var m in ScRegistry.Members.Values)
            {
                if (!IsPhysical(m) || m.Offline) continue;
                m.Offline = true;
                ScLog.Warn(m.Name + ": the runner was in the world when the game stopped (crash?) - resuming from the last snapshot");
            }
            RebuildRunnerSet();
        }

        // ------------------------------------------------------------------ snapshot bytes ----

        /// <summary>
        /// "1:&lt;bytes&gt;:&lt;base64url&gt;": a blob in the registry's key=value form (no '=', ';' or line break, so it is never
        /// Clean()ed) - the runner's snapshot, and E3's enclave trade stock (ScEnclaveDeals.EncodeStock).
        /// </summary>
        public static string Pack(byte[] bytes)
        {
            return "1:" + bytes.Length + ":" + Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        /// <summary>
        /// Round op1 review (L5): "2:&lt;bytes&gt;:&lt;base64url of the gzip'd bytes&gt;" - Pack's form for the snapshots a posted member
        /// carries for good (postsnap=: a key only this build reads, so an older build never meets a "2:" blob). &lt;bytes&gt; is the
        /// length before compression, so SnapBytes reads the same either way. A 3.4 snapshot of about 9.9 KB went from 13.2 K
        /// characters to 4.5 K (gzip, as vanilla's MapChunkDatabaseByRegion uses it). Runners' snap= and every other Pack user stay
        /// "1:" (an older build reads those).
        /// </summary>
        public static string PackZ(byte[] bytes)
        {
            using (var ms = new MemoryStream())
            {
                using (var gz = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionMode.Compress, true)) gz.Write(bytes, 0, bytes.Length);
                return "2:" + bytes.Length + ":" + Convert.ToBase64String(ms.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            }
        }

        /// <summary>Pack's (or PackZ's) bytes back, or null: empty, another version, a length that does not match, not base64url, bad gzip.</summary>
        public static byte[] Unpack(string s)
        {
            try
            {
                if (string.IsNullOrEmpty(s)) return null;
                int a = s.IndexOf(':'), b = a < 0 ? -1 : s.IndexOf(':', a + 1);
                if (a < 0 || b < 0) return null;
                string v = s.Substring(0, a);
                if (v != "1" && v != "2") return null;
                int len = int.Parse(s.Substring(a + 1, b - a - 1));
                string p = s.Substring(b + 1).Replace('-', '+').Replace('_', '/');
                p = p.PadRight(p.Length + (4 - p.Length % 4) % 4, '=');
                byte[] bytes = Convert.FromBase64String(p);
                if (v == "2")
                {
                    if (len < 0 || len > 4 * 1024 * 1024) return null;
                    using (var src = new MemoryStream(bytes))
                    using (var gz = new System.IO.Compression.GZipStream(src, System.IO.Compression.CompressionMode.Decompress))
                    using (var dst = new MemoryStream(len))
                    {
                        gz.CopyTo(dst);
                        bytes = dst.ToArray();
                    }
                }
                return bytes.Length == len ? bytes : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Round op1 review (L5): the characters a snapshot takes in the registry file (for the log).</summary>
        public static int SnapChars(Member m) { return m != null && m.Snap != null ? m.Snap.Length : 0; }

        /// <summary>
        /// Round op1 review (L5): a posting journey's snapshot, and a posted member's, is kept gzip'd (PackZ): they carry it for
        /// as long as they are posted. A runner's stays plain - snap= is read by older builds.
        /// </summary>
        private static bool Zip(Member m) { return m != null && (m.Status == MemberStatus.Transit || m.Status == MemberStatus.Garrison); }

        public static int SnapBytes(Member m)
        {
            if (m == null || string.IsNullOrEmpty(m.Snap)) return 0;
            int a = m.Snap.IndexOf(':'), b = a < 0 ? -1 : m.Snap.IndexOf(':', a + 1);
            int n;
            return b > a && int.TryParse(m.Snap.Substring(a + 1, b - a - 1), out n) ? n : 0;
        }

        /// <summary>
        /// new EntityCreationData(npc, false) - the persistence form VehicleManager uses - serialised at once, read back and checked.
        /// compress (round op1 review, L5): PackZ instead of Pack, read back through the same Unpack; the size limit is on the bytes.
        /// </summary>
        public static bool TryCapture(EntityAlive npc, out string snap) { return TryCapture(npc, out snap, false); }

        public static bool TryCapture(EntityAlive npc, out string snap, bool compress)
        {
            snap = null;
            try
            {
#if SC_V34
                // 3.4: the stream mode is named - Persistency is 3.2's false (StreamModeWrite.ToClient was true)
                var ecd = new EntityCreationData(npc, StreamModeWrite.Persistency);
#else
                var ecd = new EntityCreationData(npc, false);
#endif
                byte[] bytes;
                using (var ms = new MemoryStream())
                {
                    using (PooledBinaryWriter bw = MemoryPools.poolBinaryWriter.AllocSync(false))
                    {
                        bw.SetBaseStream(ms);
#if SC_V34
                        ecd.write(bw, StreamModeWrite.Persistency);
#else
                        ecd.write(bw, false);
#endif
                        bw.Flush();
                    }
                    bytes = ms.ToArray();
                }
                string s = compress ? PackZ(bytes) : Pack(bytes);
                EntityCreationData back = Deserialize(s);
                if (back == null || back.id != npc.entityId || back.entityClass != npc.entityClass || bytes.Length > ScConfig.RunSnapMaxBytes
                    || back.entityData == null || back.entityData.Length <= 0)
                {
                    ScLog.Warn("runner snapshot check failed for entity " + npc.entityId + " (" + bytes.Length + " bytes)");
                    return false;
                }
                snap = s;
                return true;
            }
            catch (Exception e)
            {
                ScLog.Once("runner-capture", e);
                return false;
            }
        }

        public static EntityCreationData Deserialize(string s)
        {
            try
            {
                byte[] bytes = Unpack(s);
                if (bytes == null) return null;
                var ecd = new EntityCreationData();
                using (var ms = new MemoryStream(bytes))
                {
                    using (PooledBinaryReader br = MemoryPools.poolBinaryReader.AllocSync(false))
                    {
                        br.SetBaseStream(ms);
#if SC_V34
                        ecd.read(br, StreamModeRead.Persistency);      // 3.4: 3.2's false (a snapshot from 3.2 takes read's legacy branch)
#else
                        ecd.read(br, false);
#endif
                    }
                }
                return ecd;
            }
            catch (Exception)
            {
                return null;
            }
        }

        // ------------------------------------------------------------------ display ----

        /// <summary>
        /// sc runners' line. Round MP5 (part N): player is whoever typed it (a client's own player too: the distance, ring and view
        /// distance are theirs - the server keeps an observer for them), null from a console with no player; the line also says
        /// which community player keeps her in the world (online) or would have her re-created near them (offline).
        /// </summary>
        public static string Describe(Member m, EntityPlayer player)
        {
            World w = ScCommunity.World;
            ulong now = w != null ? w.worldTime : 0UL;
            Vector3 at = Where(m);
            Session s = S(m.Id);
            float dist = player != null ? XZ(at, player.position) : -1f;
            long left = m.LegEnd > now ? (long)(m.LegEnd - now) : 0L;
            return m.Name + " [" + m.Id + "] " + m.RunLeg + (m.Offline ? " offline" : " ONLINE") + (Pinned(m) ? " PINNED" : "")
                   + " -> " + m.RunDestName + " " + m.RunDest
                   + " at " + World.worldToBlockPos(at) + " (" + dist.ToString("0") + " m, ring " + Ring(player, at) + "/D " + ViewDim(player) + ")"
                   + KeeperText(m, at)
                   + " leg left " + (left / (float)TicksPerHour).ToString("0.0") + " h, ETA day " + m.RunEndDay
                   + ", carry " + m.RunCarry + (m.RollPending ? " (dice pending)" : "")
#if SC_V34
                   + ScMining.DescribeRun(m)
#endif
                   + ", snapshot " + SnapBytes(m) + " b"
                   + (s.MeasuredMps > 0f ? ", moves at " + s.MeasuredMps.ToString("0.00") + " m/s (offline pace " + ScConfig.RunWalkMps.ToString("0.00") + ")" : "")
                   + (s.LastCreateMs >= 0 ? ", last build " + s.LastCreateMs + " ms" : "")
                   + HurtState(m, s);
        }

        /// <summary>
        /// Round MP5 (part N), for sc runners (read only): online - whether a living community player keeps her (Keeps; "nobody":
        /// she goes offline on the next tick); offline - who she would be re-created near now (Reacher), or that nobody is near.
        /// </summary>
        private static string KeeperText(Member m, Vector3 at)
        {
            if (!m.Offline)
            {
                bool forced, voluntary;
                int ring;
                Keeps(at, out forced, out voluntary, out ring);
                EntityPlayer by = ScPlayers.Nearest(at);
                return ", kept " + (forced ? (voluntary ? "in reach" : "at the edge") : "by nobody") + (by != null ? " (nearest " + PlayerName(by) + ", " + XZ(at, by.position).ToString("0") + " m)" : "");
            }
            EntityPlayer near = Reacher(at);
            return near != null ? ", re-created near " + PlayerName(near) + " when settled" : ", no player near enough to re-create her";
        }

        /// <summary>Round MP5 (part N): a player's display name for sc runners (read on any player's console).</summary>
        private static string PlayerName(EntityPlayer p)
        {
            return p == null ? "nobody" : !string.IsNullOrEmpty(p.PlayerDisplayName) ? p.PlayerDisplayName : "player " + p.entityId;
        }

        /// <summary>
        /// Round r2, for sc runners (read only): her health and whether she has a way to heal (her body in the world), the
        /// give-up count, a run given up hurt, and a walk held or routed round a threat she would run from (the memo as it stands).
        /// </summary>
        private static string HurtState(Member m, Session s)
        {
            var sb = new System.Text.StringBuilder();
            if (m.RunHurt) sb.Append(", GAVE THE JOB UP HURT (heading home)");
            World w = ScCommunity.World;
            EntityAlive npc = !m.Offline && w != null ? ScNpc.As(w.GetEntity(m.Id)) : null;
            if (npc != null && !npc.IsDead())
            {
                int max = npc.GetMaxHealth();
                sb.Append(", hp ").Append(npc.Health).Append('/').Append(max)
                  .Append(max > 0 ? " (" + (100f * npc.Health / max).ToString("0") + "%)" : "")
                  .Append(ScMedic.CanTreatSelf(npc) ? ", has a way to heal" : ", NOTHING TO HEAL WITH")
                  .Append(", gives up at ").Append((100f * ScConfig.RunHomeBelowHealth).ToString("0")).Append("%")
                  .Append(WouldFlee(npc) ? ", would flee" : "");
                if (s.HurtSince >= 0f) sb.Append(", too hurt for the job for ").Append((Time.unscaledTime - s.HurtSince).ToString("0.0")).Append(" s");
                if (s.RouteThreat >= 0 && Time.time < s.RouteUntil + 2f)
                {
                    Entity t = w.GetEntity(s.RouteThreat);
                    sb.Append(s.RouteHeldNow ? ", WALK HELD by " : ", going round ")
                      .Append(t != null ? ScCommunity.ClassName(t as EntityAlive) + " #" + s.RouteThreat : "#" + s.RouteThreat)
                      .Append(" at ").Append(XZ(s.RouteThreatPos, npc.position).ToString("0")).Append(" m");
                }
            }
            return sb.ToString();
        }
    }
}
