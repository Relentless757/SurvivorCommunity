using System;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// The outpost's "Linked storage" (plan decision Q6 b): standing at an outpost, the claim's radial opens the
    /// HOME linked storage. The home zone is far out of the player's chunk radius, so it is loaded with a chunk
    /// observer ("Radioing home..."); once every zone chunk is in, the same view as at home opens, and the
    /// observer stays until that view closes. It goes over the radio, so the home radio must have power.
    /// Host only; one request at a time.
    /// </summary>
    public static class ScHomeLink
    {
        /// <summary>A zone bigger than this many chunks from its centre is only partly loaded (the view says so).</summary>
        private const int MaxViewDim = 10;
        private const float TimeoutSeconds = 25f;
        private const float GraceSeconds = 3f;

        private static ChunkManager.ChunkObserver observer;
        /// <summary>The observer's centre chunk and reach: readiness only waits on chunks it can load.</summary>
        private static int obsCx, obsCz, obsDim;
        /// <summary>The outpost claim the player radioed from: the view is anchored there (the loot window closes
        /// itself 32 m from its container).</summary>
        private static Vector3i anchor;
        private static EntityPlayerLocal requester;
        private static bool waiting;
        private static float deadline;
        private static float openedAt;
        private static float nextNote;

        public static bool Busy { get { return observer != null; } }

        private static readonly System.Collections.Generic.HashSet<long> pending = new System.Collections.Generic.HashSet<long>();

        public static void Reset()
        {
            Release();
            requester = null;
            waiting = false;
        }

        /// <summary>The radial on an outpost's claim (claim = where the player stands).</summary>
        public static void Request(EntityPlayerLocal player, Vector3i claim)
        {
            try
            {
                if (player == null) return;
                if (!ScCommunity.IsHost) { ScCommunity.Tell(player, Localization.Get("scHostOnly"), "ui_denied"); return; }
                if (!ScZone.HasHome) { ScCommunity.Tell(player, Localization.Get("scNeedHome"), "ui_denied"); return; }
                if (!ScOutposts.RadioOnlineAnywhere()) { ScCommunity.Tell(player, Localization.Get("scOpDoorSilent"), "ui_denied"); return; }
                if (waiting) { ScCommunity.Tell(player, Localization.Get("scOpDoorWaiting"), ""); return; }
                requester = player;
                anchor = claim;
                if (ZoneReady())
                {
                    Open();
                    return;
                }
                if (observer == null)
                {
                    int xMin, xMax, zMin, zMax;
                    ScZone.Bounds(out xMin, out xMax, out zMin, out zMax);
                    Vector3 centre = new Vector3((xMin + xMax) * 0.5f, ScZone.Home.y, (zMin + zMax) * 0.5f);
                    int half = Math.Max(xMax - xMin, zMax - zMin) / 2;
                    int dim = Mathf.Clamp(half / 16 + 2, 2, MaxViewDim);
                    observer = GameManager.Instance.AddChunkObserver(centre, false, dim, -1);
                    obsCx = World.toChunkXZ(Mathf.FloorToInt(centre.x));
                    obsCz = World.toChunkXZ(Mathf.FloorToInt(centre.z));
                    obsDim = dim;
                    ScLog.Out("home link: loading the home zone around " + centre + " (" + dim + " chunk radius) for the linked storage");
                }
                waiting = true;
                deadline = Time.unscaledTime + TimeoutSeconds;
                nextNote = Time.unscaledTime + 6f;
                ScCommunity.Tell(player, Localization.Get("scOpDoorCalling"), "");
            }
            catch (Exception e)
            {
                ScLog.Once("homelink-request", e);
                Release();
                waiting = false;
            }
        }

        /// <summary>ScTicker, every second.</summary>
        public static void Tick(EntityPlayerLocal player)
        {
            try
            {
                if (observer == null && !waiting) return;
                float now = Time.unscaledTime;
                if (waiting)
                {
                    if (requester == null || requester.IsDead())
                    {
                        waiting = false;
                        Release();
                        return;
                    }
                    if (ZoneReady())
                    {
                        // never yank the player out of something else: wait while another window is up, drop the
                        // call if they walked away from the claim (the view would close itself) or got in a vehicle
                        if (requester.AttachedToEntity != null || DistanceToAnchor() > 28f)
                        {
                            waiting = false;
                            Release();
                            ScCommunity.Tell(requester, Localization.Get("scOpDoorCancelled"), "ui_denied");
                            return;
                        }
                        if (requester.PlayerUI != null && requester.PlayerUI.windowManager != null && requester.PlayerUI.windowManager.IsModalWindowOpen()) return;
                        waiting = false;
                        Open();
                    }
                    else if (now > deadline)
                    {
                        waiting = false;
                        Release();
                        if (requester != null) ScCommunity.Tell(requester, Localization.Get("scOpDoorNoAnswer"), "ui_denied");
                        ScLog.Warn("home link: the home zone did not load in " + TimeoutSeconds + " s - gave up");
                    }
                    else if (now >= nextNote && requester != null)
                    {
                        nextNote = now + 6f;
                        ScCommunity.Tell(requester, Localization.Get("scOpDoorCalling"), "");
                    }
                    return;
                }
                // open: keep the home loaded while the linked storage view is up, let it go once it closes
                if (now - openedAt < GraceSeconds) return;
                if (!ViewOpen(requester ?? player)) Release();
            }
            catch (Exception e)
            {
                ScLog.Once("homelink-tick", e);
                Release();
                waiting = false;
            }
        }

        private static float DistanceToAnchor()
        {
            if (requester == null) return float.MaxValue;
            Vector3 p = requester.position;
            return Mathf.Sqrt((p.x - anchor.x - 0.5f) * (p.x - anchor.x - 0.5f) + (p.z - anchor.z - 0.5f) * (p.z - anchor.z - 0.5f));
        }

        private static void Open()
        {
            openedAt = Time.unscaledTime;
            ScStorage.OpenWindow(requester, anchor, null, null);   // the HOME zone's boxes, standing at the outpost
            ScLog.Out("home link: linked storage opened from afar" + (observer != null ? " (home held loaded until it closes)" : " (home was already loaded)"));
        }

        /// <summary>Every chunk of the zone's bounds is loaded and settled (ScStorage.Scan skips the others).</summary>
        private static bool ZoneReady()
        {
            World w = ScCommunity.World;
            if (w == null || !ScZone.HasHome) return false;
            int xMin, xMax, zMin, zMax;
            ScZone.Bounds(out xMin, out xMax, out zMin, out zMax);
            int cxMin = World.toChunkXZ(xMin), cxMax = World.toChunkXZ(xMax);
            int czMin = World.toChunkXZ(zMin), czMax = World.toChunkXZ(zMax);
            // a zone wider than the observer reaches: settle for the part it loads (the view skips the rest)
            if (observer != null)
            {
                int reach = Math.Max(1, obsDim - 1);
                cxMin = Math.Max(cxMin, obsCx - reach); cxMax = Math.Min(cxMax, obsCx + reach);
                czMin = Math.Max(czMin, obsCz - reach); czMax = Math.Min(czMax, obsCz + reach);
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

        /// <summary>Is the loot window open on the linked storage?</summary>
        public static bool ViewOpen(EntityPlayerLocal player)
        {
            try
            {
                if (player == null || player.PlayerUI == null || player.PlayerUI.windowManager == null) return false;
                if (!player.PlayerUI.windowManager.IsWindowOpen("looting")) return false;
                XUiC_LootWindowGroup lg = player.PlayerUI.xui != null ? player.PlayerUI.xui.FindWindowGroupByName("looting") as XUiC_LootWindowGroup : null;
                return lg == null || lg.te is ScLinkedContainer;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void Release()
        {
            try
            {
                if (observer == null) return;
                if (GameManager.Instance != null && GameManager.Instance.World != null) GameManager.Instance.RemoveChunkObserver(observer);
                ScLog.Out("home link: released the home zone");
            }
            catch (Exception e)
            {
                ScLog.Once("homelink-release", e);
            }
            finally
            {
                observer = null;
            }
        }
    }
}
