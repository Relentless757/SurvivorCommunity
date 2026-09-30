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

        /// <summary>Set around our own re-create: the PostInit guard lets exactly this id through.</summary>
        public static int RecreatingId = -1;
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
            capturingId = -1;
            StaleIds.Clear();
            sessions.Clear();
            runnerIds = new Dictionary<int, KeyValuePair<int, ulong>>();
            Unhook();
        }

        public static bool IsPhysical(Member m) { return m != null && m.Status == MemberStatus.OnRun && m.RunLeg != RunLeg.None; }

        /// <summary>
        /// Thread-safe (the IsSavedToFile postfix asks from the chunk-save thread): is this entity a physical runner's
        /// body - same id, class and birth time? A stranger that inherited the id after a crash is saved as normal.
        /// </summary>
        public static bool IsRunnerBody(Entity e)
        {
            KeyValuePair<int, ulong> who;
            if (e == null || !runnerIds.TryGetValue(e.entityId, out who)) return false;
            if (who.Key != 0 && e.entityClass != who.Key) return false;
            return who.Value == 0 || e.WorldTimeBorn == 0 || e.WorldTimeBorn == who.Value;
        }

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
        public static bool IsStaleCopy(Member m, EntityAliveSDX npc)
        {
            if (npc == null || !IsPhysical(m) || !m.Offline || npc.entityId == RecreatingId) return false;
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
                m.LegEnd = now + (ulong)Mathf.RoundToInt((m.RunDistress && !m.RunSafe ? ScConfig.RunDistressHours : ScConfig.RunSiteHours) * TicksPerHour);
            else m.LegEnd = now;
            m.RunEndDay = GameUtils.WorldTimeToDays(now + EtaTicks(m));   // shown in the window; physical runs do not resolve at the rollover
            RebuildRunnerSet();
        }

        /// <summary>ScRuns.SendPhysical: the runner is in the world, walking out of the base.</summary>
        public static void OnSent(Member m, EntityAliveSDX npc)
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
            Vector3 site = C(m.RunSite == Vector3i.zero ? m.LegTo : m.RunSite);
            ulong home = (ulong)(XZ(site, ScCommunity.PostCenter(m)) / MetresPerTick);
            ulong leg = m.LegEnd > m.LegStart ? m.LegEnd - m.LegStart : 0UL;
            switch (m.RunLeg)
            {
                case RunLeg.Outbound: return leg + (ulong)(ScConfig.RunSiteHours * TicksPerHour) + home;
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
                EntityAliveSDX npc = w.GetEntity(m.Id) as EntityAliveSDX;
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

        public static PrefabInstance Dest(Member m)
        {
            Session s = S(m.Id);
            if (s.Poi == null || s.PoiKey != m.RunDest) { s.Poi = ScSecurity.PoiByKey(m.RunDest); s.PoiKey = m.RunDest; }
            return s.Poi;
        }

        public static bool Pinned(Member m) { return m.RunLeg == RunLeg.AtSite && m.RunDistress && !m.RunSafe; }

        /// <summary>The gate: a point on the site-to-post line close enough to the post to always be inside the online radius.</summary>
        public static Vector3 GatePoint(Member m, Vector3 from, EntityPlayerLocal player)
        {
            Vector3 post = ScCommunity.PostCenter(m);
            int d = ViewDim(player);
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
            PrefabInstance pi = Dest(m);
            if (pi == null) return C(m.RunSite);
            Vector3i a = pi.boundingBoxPosition, sz = pi.boundingBoxSize;
            GameRandom r = self.rand ?? w.GetGameRandom();
            int y;
            for (int i = 0; i < 8; i++)
            {
                int x = a.x + 2 + r.RandomRange(0, Math.Max(1, sz.x - 4));
                int z = a.z + 2 + r.RandomRange(0, Math.Max(1, sz.z - 4));
                if (GroundSpot(w, x, z, out y)) return new Vector3(x + 0.5f, y, z + 0.5f);
            }
            // the footprint is all roofs and rubble from here: walk its edge instead
            float ang = r.RandomFloat * Mathf.PI * 2f;
            Vector3 c = new Vector3(a.x + sz.x * 0.5f, 0f, a.z + sz.z * 0.5f);
            float rad = Mathf.Max(sz.x, sz.z) * 0.5f + 3f;
            int ex = Mathf.FloorToInt(c.x + Mathf.Cos(ang) * rad), ez = Mathf.FloorToInt(c.z + Mathf.Sin(ang) * rad);
            if (GroundSpot(w, ex, ez, out y)) return new Vector3(ex + 0.5f, y, ez + 0.5f);
            return C(m.RunSite);
        }

        // ------------------------------------------------------------------ the 1 Hz driver ----

        private static int ViewDim(EntityPlayerLocal player)
        {
            return player != null && player.ChunkObserver != null && player.ChunkObserver.viewDim > 0 ? player.ChunkObserver.viewDim : 6;
        }

        /// <summary>Chebyshev chunk distance from the player's observer (int.MaxValue before the observer's first update).</summary>
        private static int Ring(EntityPlayerLocal player, Vector3 pos)
        {
            if (player == null || player.ChunkObserver == null) return int.MaxValue;
            Vector3i cp = player.ChunkObserver.curChunkPos;
            if (cp.x == int.MaxValue || cp.z == int.MaxValue) return int.MaxValue;
            long cx = World.toChunkXZ(Mathf.FloorToInt(pos.x)), cz = World.toChunkXZ(Mathf.FloorToInt(pos.z));
            long r = Math.Max(Math.Abs(cx - cp.x), Math.Abs(cz - cp.z));
            return r > int.MaxValue ? int.MaxValue : (int)r;
        }

        /// <summary>ScTicker.OnGameUpdate, right after Relink. Host, main thread, once a second.</summary>
        public static void Tick(World w, EntityPlayerLocal player)
        {
            // runners already out keep going even if PhysicalRuns was switched off: only new sends read the switch
            if (w == null || player == null || player.ChunkObserver == null) return;
            Hook(w);
            float t = Time.unscaledTime;
            ulong now = w.worldTime;

            if (StaleIds.Count > 0)
            {
                var ids = new List<int>(StaleIds);
                StaleIds.Clear();
                foreach (int id in ids)
                {
                    EntityAliveSDX e = w.GetEntity(id) as EntityAliveSDX;
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
                    if (m.LegStart > now + TicksPerHour)
                    {
                        // the world clock went backwards (an older save, a time command): the leg keeps its length from now
                        ulong len = m.LegEnd > m.LegStart ? m.LegEnd - m.LegStart : 0UL;
                        m.LegStart = now;
                        m.LegEnd = now + len;
                    }
                    if (!m.Offline)
                    {
                        EntityAliveSDX npc = w.GetEntity(m.Id) as EntityAliveSDX;
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

        private static void OnlineTick(World w, EntityPlayerLocal player, Member m, EntityAliveSDX npc, ulong now, float t)
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

            switch (m.RunLeg)
            {
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

            // an A* grid around a runner the player's own grid does not cover (locations expire after 4 s)
            if (s.HasWaypoint && XZ(npc.position, player.position) > ScConfig.RunGridFarFromPlayer && AstarManager.Instance != null)
                AstarManager.Instance.AddLocationLine(npc.position, s.Waypoint, ScConfig.RunGridSize);

            Watchdog(w, player, m, npc, s, t);
            if (ScRegistry.GetMember(m.Id) != m || m.Offline) return;           // the watchdog handed them off

            // periodic snapshot: the crash fallback (never mid medical use - a Write(false) finishes the use)
            if (t >= s.NextSnap && !ScMedic.IsUsing(m.Id))
            {
                string snap;
                if (TryCapture(npc, out snap)) { m.Snap = snap; ReplanFrom(m, npc.position, now); ScRegistry.SaveAtomic(); }
                s.NextSnap = t + Math.Max(10f, ScConfig.RunSnapRefreshSeconds);
            }

            // online -> offline
            int D = ViewDim(player);
            int ring = Ring(player, npc.position);
            float dist = XZ(npc.position, player.position);
            if (!npc.CanUpdateEntity()) { if (s.FrozenSince < 0f) s.FrozenSince = t; }      // a chunk without colliders: vanilla freezes it
            else s.FrozenSince = -1f;
            bool eventLive = m.RunDistress && m.EventSpawned;
            bool must = (ring >= D + 1 && (dist >= NoDrawRange || ring >= D + 2))                  // never pop out inside drawing range if it can wait
                        || (s.FrozenSince >= 0f && t - s.FrozenSince >= ScConfig.RunFrozenSeconds) || player.IsDead();
            bool may = ring >= D - ScConfig.RunOfflineRingMargin && dist >= NoDrawRange && !eventLive && t - s.OnlineSince >= ScConfig.RunOnlineDwellSeconds;
            if (must || may) GoOffline(w, m, npc, must ? "forced, ring " + ring : "out of range, ring " + ring, true);
        }

        private static bool NearHome(Member m, Vector3 pos)
        {
            return XZ(pos, ScCommunity.PostCenter(m)) <= 38f;   // Station teleports beyond 40 m: walk right in first
        }

        private static void Watchdog(World w, EntityPlayerLocal player, Member m, EntityAliveSDX npc, Session s, float t)
        {
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
            bool unseen = XZ(npc.position, player.position) > ScConfig.RunSkipMinPlayerDist && !player.IsInViewCone(npc.position);
            bool desperate = still >= ScConfig.RunStuckSkipSeconds * 4f;                         // a minute against a wall, watched or not
            if (!unseen && !desperate) return;
            Vector3 wp;
            if (NextWaypoint(w, npc.position, s.Sidestep, goal, out wp) && XZ(wp, npc.position) > 4f && (still < ScConfig.RunStuckSkipSeconds * 2f || desperate))
            {
                npc.SetPosition(wp, true);                                                      // hop past the obstacle out of sight
                ScLog.Verbose(m.Name + ": stuck " + still.ToString("0") + " s - skipped ahead to " + World.worldToBlockPos(wp));
                s.ProgressPos = wp;
                s.ProgressSince = t;
                s.Sidestep = 0;
                s.HasWaypoint = false;
            }
            else if (still >= ScConfig.RunStuckSkipSeconds * 2f && (unseen || desperate))
            {
                GoOffline(w, m, npc, "stuck", false);                                          // the virtual walk carries on
            }
        }

        // ------------------------------------------------------------------ legs ----

        private static void EnterSite(World w, EntityPlayerLocal player, Member m, Vector3 at, ulong now)
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
            // distress is rolled on arrival - never with the player standing right there (that is an escort, not a radio call)
            bool escorted = player != null && XZ(player.position, at) <= ScConfig.RescueTriggerRange;
            if (!escorted && ScRadio.RollDistress(m, player, w != null ? w.GetGameRandom() : null)) BeginLeg(m, RunLeg.AtSite, at, at, now);   // re-timed to RunDistressHours
            ScLog.Out(m.Name + " reached " + m.RunDestName + (m.Offline ? " (offline)" : "") + (m.RunDistress ? " - and radios for help" : ""));
            ScRegistry.SaveAtomic();
        }

        /// <summary>The site search is over: bank the find (prorated for a recall) and head for the gate.</summary>
        public static void LeaveSite(EntityPlayerLocal player, Member m, Vector3 at, ulong now, bool rescued, float share)
        {
            int y = Mathf.RoundToInt(ScRuns.Yield(m, m.RunKind) * Mathf.Clamp01(share));
            m.RunCarry = rescued ? Mathf.RoundToInt(y * ScConfig.RescueBonus) : y;
            m.RollPending = !rescued;
            BeginLeg(m, RunLeg.Inbound, at, GatePoint(m, at, player), now);
            ScLog.Out(m.Name + " leaves " + m.RunDestName + " with " + m.RunCarry + " " + m.RunKind + (rescued ? " (rescued)" : ""));
            ScRegistry.SaveAtomic();
        }

        /// <summary>At the gate: the haul counts now, whether or not the player is home to see it.</summary>
        private static void EnterBack(EntityPlayerLocal player, Member m, ulong now)
        {
            bool visited = m.RunSite != Vector3i.zero;                                          // recalled before reaching the site: nothing found, nothing learnt
            if (visited)
            {
                ScRegistry.Community.Add(m.RunKind, m.RunCarry);
                ScSkills.AddXp(m, Skill.Wits, ScConfig.RunWitsXp);
                if (ScRuns.SpecialtyFor(m.RunKind) == m.Specialty) ScSkills.AddSpecialtyXp(m, 3f);
                string res = Localization.Get(ScNames.LocKey(m.RunKind));
                if (player != null) ScCommunity.Tell(player, string.Format(Localization.Get(m.Injured ? "scRunHurt" : "scRunBack"), m.Name, m.RunCarry, res), m.Injured ? "ui_denied" : "");
            }
            else if (player != null) ScCommunity.Tell(player, string.Format(Localization.Get("scRunBackEmpty"), m.Name, m.RunDestName), "");
            ScLog.Out(m.Name + " is back from " + m.RunDestName + (visited ? " with " + m.RunCarry + " " + m.RunKind : " empty-handed (turned back early)") + (m.Injured ? " (injured)" : ""));
            m.RunCarry = 0;
            m.RollPending = false;                                                              // an escorted runner reaching home was never rolled
            Vector3 gate = C(m.LegTo);
            BeginLeg(m, RunLeg.Back, gate, gate, now);
            ScRegistry.SaveAtomic();
        }

        /// <summary>Inside the zone / near the post: a resident again, saved with their chunk as before.</summary>
        public static void Arrive(EntityPlayerLocal player, Member m, EntityAliveSDX npc)
        {
            npc.Buffs.RemoveCustomVar(OnRunCvar);
            ClearRun(m);
            ScCommunity.Station(m, npc);                                                      // Resident, Guard order; no teleport within 40 m
            RebuildRunnerSet();
            ScRegistry.SaveAtomic();
            ScLog.Out(m.Name + " walked back in and is at their post again");
        }

        public static void ClearRun(Member m)
        {
            m.RunLeg = RunLeg.None;
            m.Offline = false;
            m.Snap = null;
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
            m.RunDistress = false;
            m.RunSafe = false;
            m.EventSpawned = false;
            sessions.Remove(m.Id);
        }

        private static void AdvanceOffline(World w, EntityPlayerLocal player, Member m, ulong now)
        {
            switch (m.RunLeg)
            {
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
        private static void RollRisk(World w, EntityPlayerLocal player, Member m)
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
            ScLog.Out(m.Name + " was hurt on the way home (risk " + risk.ToString("0.00") + ")");
            ScRegistry.SaveAtomic();
        }

        private static void DistressTimedOut(World w, EntityPlayerLocal player, Member m, ulong now)
        {
            ScRadio.EndDistress(w, player, m);                                                // the rescue quest closes as failed
            if (w.GetGameRandom().RandomFloat < ScConfig.RunDeathShare) { DieOffline(player, m, "scRescueDied"); return; }
            m.Injured = true;
            LeaveSite(player, m, C(m.RunSite), now, false, 1f);
            m.RunCarry = Math.Max(0, m.RunCarry / 2);
            m.RollPending = false;
            ScRegistry.SaveAtomic();
        }

        /// <summary>An offline runner dies: no body anywhere; any old copy of theirs is removed when it loads.</summary>
        private static void DieOffline(EntityPlayerLocal player, Member m, string key)
        {
            int id = m.Id;
            ScCommunity.RecordDeath(m, player, key);
            ScRegistry.Doomed.Add(id);
            ScLooks.Forget(id);
            World w = ScCommunity.World;
            try { if (w == null || w.GetEntity(id) == null) HarvestManager.Remove(id); } catch (Exception e) { ScLog.Once("runner-harvest", e); }
            sessions.Remove(id);
            RebuildRunnerSet();
            ScRegistry.SaveAtomic();
        }

        /// <summary>
        /// The deadline fallback (the rollover, RunDeadlineGraceDays past the ETA, offline only): a route that never
        /// finishes - the virtual walk cannot get stuck, but a re-create can fail for ever - ends at the gate.
        /// </summary>
        /// <returns>true when this brought them to the gate with their haul (the daily summary says so)</returns>
        public static bool ForceHome(Member m, EntityPlayerLocal player)
        {
            if (!IsPhysical(m)) return false;
            World w = ScCommunity.World;
            if (w == null) return false;
            ulong now = w.worldTime;
            ScLog.Warn(m.Name + ": the run is " + ScConfig.RunDeadlineGraceDays + " day(s) past its ETA (" + m.RunLeg + ") - bringing them home");
            if (m.RunLeg == RunLeg.Outbound || m.RunLeg == RunLeg.AtSite)
            {
                if (m.RunDistress) ScRadio.EndDistress(w, player, m);
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
            m.MissingDays = 0;
            RebuildRunnerSet();
            ScRegistry.SaveAtomic();
            return false;
        }

        /// <summary>Is the player close enough to the post for a runner waiting there to be re-created?</summary>
        public static bool PlayerNearPost(Member m, EntityPlayerLocal player)
        {
            if (player == null) return false;
            int d = ViewDim(player);
            return XZ(player.position, ScCommunity.PostCenter(m)) <= 16f * (d - ScConfig.RunOnlineRingMargin);
        }

        /// <summary>Recall from the window or the dialog: turn round now (a site visit cut short brings a share of the find).</summary>
        public static bool Recall(Member m, EntityPlayerLocal player, out string error)
        {
            error = null;
            if (!ScCommunity.IsHost) { error = "scHostOnly"; return false; }
            if (!IsPhysical(m)) { error = "scRunNotOut"; return false; }
            World w = ScCommunity.World;
            if (w == null) { error = "scInternal"; return false; }
            ulong now = w.worldTime;
            Vector3 at = Where(m);
            switch (m.RunLeg)
            {
                case RunLeg.Outbound:
                    m.RunCarry = 0;
                    m.RollPending = false;
                    BeginLeg(m, RunLeg.Inbound, at, GatePoint(m, at, player), now);
                    break;
                case RunLeg.AtSite:
                {
                    if (Pinned(m)) { error = "scRunPinned"; return false; }
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

        // ------------------------------------------------------------------ offline -> online ----

        private static bool TryMaterialize(World w, EntityPlayerLocal player, Member m, ulong now, float t)
        {
            Session s = S(m.Id);
            if (t - s.OfflineSince < ScConfig.RunOfflineDwellSeconds) return false;
            Vector3 v = VirtualPos(m, now);
            int D = ViewDim(player);
            int margin = ScConfig.RunOnlineRingMargin;
            float dist = XZ(v, player.position);
            if (player.IsDead() || Ring(player, v) > D - margin || dist > 16f * (D - margin)) { s.SettledSince = -1f; s.PopInBlockedSince = -1f; return false; }
            // vanilla's pop-in rule: nothing appears within 50 m in front of the player - given up after a while so a
            // runner waiting at the gate still comes home while the player looks that way
            if (dist < ScConfig.RunNoPopInRange && player.IsInViewCone(v))
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
                EntityAliveSDX sdx = occupant as EntityAliveSDX;
                if (sdx != null && IsStaleCopy(m, sdx)) { StaleIds.Add(m.Id); return false; }     // removed next tick, then we come back
                rekey = true;                                                                    // a stranger has the id
            }
            else if (w.entityAsyncManager != null && w.entityAsyncManager.IsEntityPending(m.Id)) return false;
            if (ScRegistry.Doomed.Contains(m.Id) || ScRegistry.Cleanup.Contains(m.Id)) rekey = true;
            return Materialize(w, player, m, v, now, t, rekey);
        }

        private static bool Materialize(World w, EntityPlayerLocal player, Member m, Vector3 v, ulong now, float t, bool rekey)
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
                // the post, not the gate: the pop-in rule was judged at the gate, judge it here
                if (XZ(spot, player.position) < ScConfig.RunNoPopInRange && player.IsInViewCone(spot))
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
            EntityAliveSDX e = null;
            var sw = Stopwatch.StartNew();
            RecreatingId = newId ? -1 : m.Id;
            try
            {
                if (newId && m.Look != null) ScLooks.Pending = m.Look.Clone();                // the id is not the member's yet: hand the look over
                if (fresh) e = EntityFactory.CreateEntity(cid, spot + new Vector3(0f, 0.002f, 0f), rot) as EntityAliveSDX;
                else
                {
                    ecd.pos = spot + new Vector3(0f, 0.002f, 0f);                               // VehicleManager nudges y the same way
                    ecd.rot = rot;
                    ecd.id = rekey ? -1 : m.Id;                                                 // never the id inside the blob: after a re-key it is the old one
                    e = EntityFactory.CreateEntity(ecd) as EntityAliveSDX;
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
            }
            sw.Stop();

            // SCore's SetupBagItems re-appends the class BagItems on every PostInit: put the saved bag back
            if (!fresh && ecd.bag != null && e.bag != null) e.bag.SetSlots(ecd.bag.Clone().GetSlots());
            if (newId)
            {
                Rekey(w, player, m, e, oldId, fresh);
                string resnap;
                if (TryCapture(e, out resnap)) m.Snap = resnap;                                 // the crash fallback under the new id
            }
            s = S(m.Id);
            if (fresh)
            {
                e.FirstName = m.Name;
                e.Title = "";
                e.Buffs.SetCustomVar("Persist", 1f);
                ScCommunity.StampCvars(m, e, player);
                ScTraits.ApplyBuffs(m, e);
            }
            e.SetSpawnerSource(EnumSpawnerSource.StaticSpawner);
            if (e.Buffs.HasCustomVar("onMission") || !e.enabled) e.SendOnMission(false);
            ScCommunity.ApplyAiPackage(e);
            ApplyTravelOrder(e);
            m.Offline = false;
            s.CreateFailures = 0;
            s.SpotFailures = 0;
            s.OnlineSince = t;
            s.SettledSince = -1f;
            s.PopInBlockedSince = -1f;
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
            ScLog.Out(m.Name + " is back in the world at " + World.worldToBlockPos(spot) + " (" + m.RunLeg + ", " + XZ(spot, player.position).ToString("0")
                      + " m from you, entity " + m.Id + (newId ? ", was " + oldId : "") + ", built in " + sw.ElapsedMilliseconds + " ms)");
            return true;
        }

        /// <summary>The runner came back under a new entity id: move every record keyed by the old one.</summary>
        private static void Rekey(World w, EntityPlayerLocal player, Member m, EntityAliveSDX e, int oldId, bool fresh)
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
            try { if (w.GetEntity(oldId) == null) HarvestManager.Remove(oldId); } catch (Exception ex) { ScLog.Once("runner-harvest", ex); }
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

        /// <summary>The VehicleManager gate plus the 3x3 settled checks: loaded, decorated, lit, colliders built, not brand new.</summary>
        private static bool Settled(World w, Vector3 v)
        {
            if (!w.IsChunkAreaCollidersLoaded(v)) return false;
            int cx = World.toChunkXZ(Mathf.FloorToInt(v.x)), cz = World.toChunkXZ(Mathf.FloorToInt(v.z));
            List<long> fresh = w.newlyLoadedChunksThisUpdate;
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    Chunk ch = w.GetChunkSync(cx + dx, cz + dz) as Chunk;
                    if (ch == null || ch.InProgressUnloading || ch.NeedsDecoration || ch.NeedsLightCalculation || !ch.IsCollisionMeshGenerated) return false;
                    if (fresh != null)
                    {
                        long key = WorldChunkCache.MakeChunkKey(cx + dx, cz + dz);
                        lock (fresh) { if (fresh.Contains(key)) return false; }
                    }
                }
            }
            return true;
        }

        /// <summary>The Task order: no resident action runs, the pack's combat does, and ScTravel walks the route.</summary>
        public static void ApplyTravelOrder(EntityAliveSDX npc)
        {
            npc.Buffs.RemoveBuff("buffOrderGuard");
            npc.Buffs.RemoveCustomVar("Guarding");
            npc.Buffs.RemoveCustomVar("Leader");
            npc.Buffs.RemoveCustomVar("Owner");
            SphereCache.LeaderCache.Remove(npc.entityId);
            if (npc.Buffs.GetCustomVar("CurrentOrder") != OrderTask) npc.Buffs.SetCustomVar("CurrentOrder", OrderTask);   // SetCurrentOrder is a no-op for Task
            npc.Buffs.SetCustomVar(OnRunCvar, 1f);
        }

        public static bool HasTravelOrder(EntityAliveSDX npc)
        {
            return npc.Buffs.GetCustomVar("CurrentOrder") == OrderTask && !npc.Buffs.HasCustomVar("Leader") && !npc.Buffs.HasCustomVar("Owner");
        }

        // ------------------------------------------------------------------ online -> offline ----

        /// <summary>Snapshot, registry saved, then the clean removal. forced: keep the previous snapshot when a fresh one fails.</summary>
        public static bool GoOffline(World w, Member m, EntityAliveSDX npc, string why, bool forced)
        {
            if (w == null || m == null || npc == null || npc.IsDead() || npc.IsMarkedForUnload()) return false;
            int id = npc.entityId;
            string snap;
            if (TryCapture(npc, out snap)) m.Snap = snap;
            else if (!forced || string.IsNullOrEmpty(m.Snap)) { ScLog.Warn(m.Name + ": snapshot failed - staying in the world (" + why + ")"); return false; }
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

        private static void ClearScoreCaches(int id)
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

        /// <summary>Backstop: a chunk unload (teleport, bed respawn, fast travel) or a fall out of the world beat the 1 Hz tick.</summary>
        private static void OnEntityUnloaded(Entity e, EnumRemoveEntityReason reason)
        {
            try
            {
                if (e == null || e.entityId == capturingId || !ScCommunity.IsHost || !ScRegistry.Loaded) return;
                EntityAliveSDX npc = e as EntityAliveSDX;
                if (npc == null) return;
                Member m = ScRegistry.GetMember(npc.entityId);
                if (!IsPhysical(m) || m.Offline || npc.IsDead()) return;
                bool fell = npc.position.y < 0f && (reason == EnumRemoveEntityReason.Killed || reason == EnumRemoveEntityReason.Despawned);
                if (reason != EnumRemoveEntityReason.Unloaded && !fell) return;
                Session s = S(m.Id);
                string snap;
                if (TryCapture(npc, out snap)) m.Snap = snap;
                else ScLog.Warn(m.Name + ": capture at unload failed - keeping the previous snapshot");
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
                    EntityAliveSDX npc = w.GetEntity(m.Id) as EntityAliveSDX;
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

        public static int SnapBytes(Member m)
        {
            if (m == null || string.IsNullOrEmpty(m.Snap)) return 0;
            int a = m.Snap.IndexOf(':'), b = a < 0 ? -1 : m.Snap.IndexOf(':', a + 1);
            int n;
            return b > a && int.TryParse(m.Snap.Substring(a + 1, b - a - 1), out n) ? n : 0;
        }

        /// <summary>new EntityCreationData(npc, false) - the persistence form VehicleManager uses - serialised at once, read back and checked.</summary>
        public static bool TryCapture(EntityAliveSDX npc, out string snap)
        {
            snap = null;
            try
            {
                var ecd = new EntityCreationData(npc, false);
                byte[] bytes;
                using (var ms = new MemoryStream())
                {
                    using (PooledBinaryWriter bw = MemoryPools.poolBinaryWriter.AllocSync(false))
                    {
                        bw.SetBaseStream(ms);
                        ecd.write(bw, false);
                        bw.Flush();
                    }
                    bytes = ms.ToArray();
                }
                string s = "1:" + bytes.Length + ":" + Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
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
                if (string.IsNullOrEmpty(s)) return null;
                int a = s.IndexOf(':'), b = a < 0 ? -1 : s.IndexOf(':', a + 1);
                if (a < 0 || b < 0 || s.Substring(0, a) != "1") return null;
                int len = int.Parse(s.Substring(a + 1, b - a - 1));
                string p = s.Substring(b + 1).Replace('-', '+').Replace('_', '/');
                p = p.PadRight(p.Length + (4 - p.Length % 4) % 4, '=');
                byte[] bytes = Convert.FromBase64String(p);
                if (bytes.Length != len) return null;
                var ecd = new EntityCreationData();
                using (var ms = new MemoryStream(bytes))
                {
                    using (PooledBinaryReader br = MemoryPools.poolBinaryReader.AllocSync(false))
                    {
                        br.SetBaseStream(ms);
                        ecd.read(br, false);
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

        public static string Describe(Member m, EntityPlayerLocal player)
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
                   + " leg left " + (left / (float)TicksPerHour).ToString("0.0") + " h, ETA day " + m.RunEndDay
                   + ", carry " + m.RunCarry + (m.RollPending ? " (dice pending)" : "")
                   + ", snapshot " + SnapBytes(m) + " b"
                   + (s.MeasuredMps > 0f ? ", walks " + s.MeasuredMps.ToString("0.00") + " m/s (config " + ScConfig.RunWalkMps.ToString("0.00") + ")" : "")
                   + (s.LastCreateMs >= 0 ? ", last build " + s.LastCreateMs + " ms" : "");
        }
    }
}
