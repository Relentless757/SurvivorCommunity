using System;
using System.Collections.Generic;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// The outpost's "Linked storage" (plan decision Q6 b): standing at an outpost, the claim's radial opens the
    /// HOME linked storage. The home zone is far out of the player's chunk radius, so it is loaded with a chunk
    /// observer ("Radioing home..."); once every zone chunk is in, the same view as at home opens, and the
    /// observer stays until that view closes. It goes over the radio, so the home radio must have power.
    ///
    /// Round MP3 (part L, multiplayer): the server's, per player. storage.open with ScStorage.KindHomeLink (ScStorageServer) calls
    /// Request for the player who pressed; each player has their own observer and their own wait, and the observer is let go
    /// when that player's view ends (OnSessionEnded) - closed, walked away, replaced, disconnected. The wait for the player's
    /// other window to close happens on their own machine (ScStorage.OnView). Linked storage's sessions are swept by ScTicker
    /// itself (round MP3, review storage m7: in their own try).
    /// </summary>
    public static class ScHomeLink
    {
        /// <summary>A zone bigger than this many chunks from its center is only partly loaded (the view says so).</summary>
        private const int MaxViewDim = 10;
        private const float TimeoutSeconds = 25f;

        private sealed class Link
        {
            public int EntityId;
            /// <summary>The outpost claim the player radioed from: the view is anchored there (the loot window closes
            /// itself 32 m from its container).</summary>
            public Vector3i Claim;
            public ChunkManager.ChunkObserver Observer;
            /// <summary>The observer's center chunk and reach: readiness only waits on chunks it can load.</summary>
            public int ObsCx, ObsCz, ObsDim;
            public bool Waiting;
            public float Deadline;
            public float NextNote;
            /// <summary>The linked storage session the home is held for (-1: none yet).</summary>
            public int Session = -1;
        }

        private static readonly Dictionary<int, Link> links = new Dictionary<int, Link>();
        private static readonly HashSet<long> pending = new HashSet<long>();
        private static readonly List<int> scratch = new List<int>();

        /// <summary>Some player's home link is calling or holding the home zone.</summary>
        public static bool Busy { get { return links.Count > 0; } }

        /// <summary>Every machine, at the world's start and end: no home link, linked storage view or claim carries over.</summary>
        public static void Reset()
        {
            foreach (Link l in links.Values) Release(l);
            links.Clear();
            ScStorage.ResetView();
            ScStorageServer.Reset();
        }

        /// <summary>
        /// Server: the radial's Linked storage on an outpost's claim (claim = where the player stands; the caller checked it is
        /// theirs). Opens the home's view at once when the home zone is loaded, else loads it and opens it from Tick. Answers the
        /// request: the refusal, "Radioing home...", or done.
        /// </summary>
        internal static void Request(EntityPlayer player, Vector3i claim, ScActionReply reply)
        {
            try
            {
                if (player == null) { reply.Fail("scActionNoPlayer"); return; }
                if (!ScZone.HasHome) { reply.Fail("scNeedHome"); return; }
                if (!ScOutposts.RadioOnlineAnywhere()) { reply.Fail("scOpDoorSilent"); return; }
                Link l;
                if (links.TryGetValue(player.entityId, out l) && l.Waiting) { reply.Fail("scOpDoorWaiting", ""); return; }
                if (l == null)
                {
                    l = new Link { EntityId = player.entityId };
                    links[player.entityId] = l;
                }
                l.Claim = claim;
                l.Session = -1;
                if (ZoneReady(l))
                {
                    string why = OpenFor(l, player);
                    if (why != null) { Drop(l); reply.Fail(why); }
                    else reply.Done();
                    return;
                }
                if (l.Observer == null)
                {
                    int xMin, xMax, zMin, zMax;
                    ScZone.Bounds(out xMin, out xMax, out zMin, out zMax);
                    Vector3 center = new Vector3((xMin + xMax) * 0.5f, ScZone.Home.y, (zMin + zMax) * 0.5f);
                    int half = Math.Max(xMax - xMin, zMax - zMin) / 2;
                    int dim = Mathf.Clamp(half / 16 + 2, 2, MaxViewDim);
                    l.Observer = GameManager.Instance.AddChunkObserver(center, false, dim, -1);
                    l.ObsCx = World.toChunkXZ(Mathf.FloorToInt(center.x));
                    l.ObsCz = World.toChunkXZ(Mathf.FloorToInt(center.z));
                    l.ObsDim = dim;
                    ScLog.Out("home link: loading the home zone around " + center + " (" + dim + " chunk radius) for the linked storage"
                              + (player is EntityPlayerLocal ? "" : " (for " + (player.PlayerDisplayName ?? "?") + ")"));
                }
                l.Waiting = true;
                l.Deadline = Time.unscaledTime + TimeoutSeconds;
                l.NextNote = Time.unscaledTime + 6f;
                reply.Done("scOpDoorCalling", "");
            }
            catch (Exception e)
            {
                ScLog.Once("homelink-request", e);
                Link l;
                if (player != null && links.TryGetValue(player.entityId, out l)) Drop(l);
                reply.Fail("scActionFailed");
            }
        }

        /// <summary>The home's view for the link's player, standing at the outpost claim. Returns a refusal, or null.</summary>
        private static string OpenFor(Link l, EntityPlayer p)
        {
            l.Waiting = false;
            string why = ScStorageServer.Open(p, ScStorage.KindHomeLink, l.Claim, null, null);    // the HOME zone's boxes, standing at the outpost
            if (why != null) return why;
            l.Session = ScStorageServer.SessionIdOf(p.entityId);
            ScLog.Out("home link: linked storage opened from afar" + (l.Observer != null ? " (home held loaded until it closes)" : " (home was already loaded)"));
            return null;
        }

        /// <summary>Server (ScStorageServer.End): a view ended - the home link that held the home zone for it lets go.</summary>
        internal static void OnSessionEnded(int entityId)
        {
            Link l;
            if (!links.TryGetValue(entityId, out l) || l.Waiting || l.Session < 0) return;
            if (ScStorageServer.HasSession(entityId, l.Session)) return;
            Drop(l);
        }

        /// <summary>ScTicker, every second (the server): each home link - load, open, release. Round MP3 (review storage m7):
        /// linked storage's sweep moved to the start of ScTicker's tick, in its own try.</summary>
        public static void Tick(EntityPlayer player)
        {
            if (links.Count == 0) return;
            try
            {
                float now = Time.unscaledTime;
                scratch.Clear();
                scratch.AddRange(links.Keys);
                for (int i = 0; i < scratch.Count; i++)
                {
                    Link l;
                    if (!links.TryGetValue(scratch[i], out l)) continue;
                    EntityPlayer p = ScPlayers.ByEntity(l.EntityId);
                    if (!l.Waiting)
                    {
                        // open: keep the home loaded while that view is open, let it go once it ends
                        if (l.Session < 0 || !ScStorageServer.HasSession(l.EntityId, l.Session)) Drop(l);
                        continue;
                    }
                    if (p == null || p.IsDead())
                    {
                        Drop(l);
                        continue;
                    }
                    if (ZoneReady(l))
                    {
                        // never open on a player who left: drop the call if they walked away from the claim (the view would close
                        // itself) or got in a vehicle. Another window up is waited for on their own machine (ScStorage.OnView).
                        if (p.AttachedToEntity != null || Distance(p, l.Claim) > 28f)
                        {
                            Drop(l);
                            // round MP4 (part: tells): this file's tells go by localization key (ScCommunity.TellKey / ScPlayers.TellCommunityKey), so each player reads them in their own language
                            ScCommunity.TellKey(p, "scOpDoorCancelled", "ui_denied");
                            continue;
                        }
                        string why = OpenFor(l, p);
                        if (why != null)
                        {
                            Drop(l);
                            ScCommunity.TellKey(p, why, "ui_denied");
                        }
                    }
                    else if (now > l.Deadline)
                    {
                        Drop(l);
                        ScCommunity.TellKey(p, "scOpDoorNoAnswer", "ui_denied");
                        ScLog.Warn("home link: the home zone did not load in " + TimeoutSeconds + " s - gave up");
                    }
                    else if (now >= l.NextNote)
                    {
                        l.NextNote = now + 6f;
                        ScCommunity.TellKey(p, "scOpDoorCalling", "");
                    }
                }
            }
            catch (Exception e)
            {
                ScLog.Once("homelink-tick", e);
            }
        }

        private static float Distance(EntityPlayer p, Vector3i claim)
        {
            if (p == null) return float.MaxValue;
            Vector3 pos = p.position;
            return Mathf.Sqrt((pos.x - claim.x - 0.5f) * (pos.x - claim.x - 0.5f) + (pos.z - claim.z - 0.5f) * (pos.z - claim.z - 0.5f));
        }

        /// <summary>Every chunk of the zone's bounds is loaded and settled (the view's scan skips the others).</summary>
        private static bool ZoneReady(Link l)
        {
            World w = ScCommunity.World;
            if (w == null || !ScZone.HasHome) return false;
            int xMin, xMax, zMin, zMax;
            ScZone.Bounds(out xMin, out xMax, out zMin, out zMax);
            int cxMin = World.toChunkXZ(xMin), cxMax = World.toChunkXZ(xMax);
            int czMin = World.toChunkXZ(zMin), czMax = World.toChunkXZ(zMax);
            // a zone wider than the observer reaches: settle for the part it loads (the view skips the rest)
            if (l.Observer != null)
            {
                int reach = Math.Max(1, l.ObsDim - 1);
                cxMin = Math.Max(cxMin, l.ObsCx - reach); cxMax = Math.Min(cxMax, l.ObsCx + reach);
                czMin = Math.Max(czMin, l.ObsCz - reach); czMax = Math.Min(czMax, l.ObsCz + reach);
            }
            pending.Clear();
            var fresh = w.newlyLoadedChunksThisUpdate;
            if (fresh != null) lock (fresh) { for (int i = 0; i < fresh.Count; i++) pending.Add(fresh[i]); }   // filled off-thread under this lock
            for (int cx = cxMin; cx <= cxMax; cx++)
            {
                for (int cz = czMin; cz <= czMax; cz++)
                {
                    Chunk ch = w.GetChunkSync(cx, cz) as Chunk;
                    if (ch == null || ch.NeedsDecoration || ch.InProgressUnloading) return false;
                    if (pending.Contains(WorldChunkCache.MakeChunkKey(cx, cz))) return false;
                }
            }
            return true;
        }

        /// <summary>The link is over: its observer goes and it is forgotten.</summary>
        private static void Drop(Link l)
        {
            if (l == null) return;
            Release(l);
            links.Remove(l.EntityId);
        }

        private static void Release(Link l)
        {
            try
            {
                if (l == null || l.Observer == null) return;
                if (GameManager.Instance != null && GameManager.Instance.World != null) GameManager.Instance.RemoveChunkObserver(l.Observer);
                ScLog.Out("home link: released the home zone");
            }
            catch (Exception e)
            {
                ScLog.Once("homelink-release", e);
            }
            finally
            {
                if (l != null) l.Observer = null;
            }
        }
    }
}
