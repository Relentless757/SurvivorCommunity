using System;
using System.Collections.Generic;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Keeps the home zone loaded while a player is near it (round hz, the user's call 2026-10-12). A survivor whose spot
    /// sits on the edge of the player's view distance was unloaded and rebuilt every time the player crossed a chunk line
    /// (two of them 8 times in 4 minutes, each rebuild about 0.16 s of main thread: the hitch the user tied to the
    /// LIPSYNC line), and residents roaming the whole zone cross that edge more. A chunk observer like ScHomeLink's (no
    /// meshes built, host only) holds every zone chunk while any player is within HomeKeepLoadedRange metres of the zone,
    /// and lets go ReleaseExtra metres further out, so walking along the edge does not load and drop it over and over.
    /// It is placed again when the zone's bounds change (a claim moved or upgraded).
    /// </summary>
    public static class ScHomeHold
    {
        /// <summary>As ScHomeLink: a zone wider than this many chunks from its centre is only partly held.</summary>
        private const int MaxViewDim = 10;
        /// <summary>Metres past HomeKeepLoadedRange before a held zone is let go.</summary>
        private const float ReleaseExtra = 50f;
        private const float CheckSeconds = 2f;

        /// <summary>Round MP6 (part H): one home's hold - its observer and the bounds it holds. One per community (ScHomeState).</summary>
        private sealed class HoldState
        {
            public ChunkManager.ChunkObserver Observer;
            public int HeldXMin, HeldXMax, HeldZMin, HeldZMax;
        }
        private static readonly ScHomeState<HoldState> homes = new ScHomeState<HoldState>();
        // round MP6 (part H): the old statics, now the row of the community in scope (nextCheck stays one: Tick holds every home)
        private static ChunkManager.ChunkObserver observer { get { return homes.Current.Observer; } set { homes.Current.Observer = value; } }
        private static int heldXMin { get { return homes.Current.HeldXMin; } set { homes.Current.HeldXMin = value; } }
        private static int heldXMax { get { return homes.Current.HeldXMax; } set { homes.Current.HeldXMax = value; } }
        private static int heldZMin { get { return homes.Current.HeldZMin; } set { homes.Current.HeldZMin = value; } }
        private static int heldZMax { get { return homes.Current.HeldZMax; } set { homes.Current.HeldZMax = value; } }
        private static float nextCheck;

        /// <summary>The home zone of the community in scope is held loaded.</summary>
        public static bool Held { get { return observer != null; } }

        /// <summary>Per world (GameStarting) and at its shutdown, with the world still there to take the observer back.</summary>
        public static void Reset()
        {
            // round MP6 (part H): every community's hold goes back (by its row: at a world start community 1+ may no longer exist)
            homes.ForEach((cid, s) => Release(s, null));
            homes.Reset();
            nextCheck = 0f;
        }

        /// <summary>
        /// ScTicker, every second (works every CheckSeconds). Round MP6 (part H): every community's home in one pass, each in its own
        /// scope - one with nobody of its own online too, so none is ever left held when its players log off (a player of any
        /// community near a home holds it, as any player did); a hold whose community no longer exists goes.
        /// </summary>
        public static void Tick(World w)
        {
            try
            {
                float now = Time.unscaledTime;
                if (now < nextCheck) return;
                nextCheck = now + CheckSeconds;
                if (w == null || !ScCommunity.IsHost) return;
                foreach (KeyValuePair<int, HoldState> kv in homes.Rows())
                    if (kv.Value.Observer != null && ScCommunities.ById(kv.Key) == null) Release(kv.Value, "its community is gone");
                if (ScCommunities.Count < 2) { TickOne(w); return; }      // one community: exactly as before
                IReadOnlyList<CommunityState> all = ScCommunities.All;
                for (int i = 0; i < all.Count; i++)
                    using (ScRegistry.Use(all[i].Id)) TickOne(w);
            }
            catch (Exception e)
            {
                ScLog.Once("homehold-tick", e);
            }
        }

        /// <summary>Round MP6 (part H): Tick for the community in scope (the body Tick always had after its throttle).</summary>
        private static void TickOne(World w)
        {
            try
            {
                if (!ScConfig.HomeKeepLoaded || !ScZone.HasHome) { Release("off, or no home"); return; }
                float near = NearestPlayer(w);
                float limit = ScConfig.HomeKeepLoadedRange + (observer != null ? ReleaseExtra : 0f);
                if (near > limit) { Release("no player within " + (int)limit + " m"); return; }
                int xMin, xMax, zMin, zMax;
                ScZone.Bounds(out xMin, out xMax, out zMin, out zMax);
                if (observer != null && xMin == heldXMin && xMax == heldXMax && zMin == heldZMin && zMax == heldZMax) return;
                Release(observer != null ? "the zone changed" : null);
                Hold(xMin, xMax, zMin, zMax, near);
            }
            catch (Exception e)
            {
                ScLog.Once("homehold-tick", e);
            }
        }

        /// <summary>Horizontal metres from the nearest player to the zone (0 inside it).</summary>
        private static float NearestPlayer(World w)
        {
            float best = float.MaxValue;
            var list = w.Players != null ? w.Players.list : null;
            if (list == null) return best;
            for (int i = 0; i < list.Count; i++)
            {
                EntityPlayer p = list[i];
                if (p == null) continue;
                best = Mathf.Min(best, ScZone.DistanceOutside(p.position));
            }
            return best;
        }

        private static void Hold(int xMin, int xMax, int zMin, int zMax, float near)
        {
            Vector3 centre = new Vector3((xMin + xMax) * 0.5f, ScZone.Home.y, (zMin + zMax) * 0.5f);
            int half = Math.Max(xMax - xMin, zMax - zMin) / 2;
            // ChunkManager holds rings 0..viewDim+1 round the centre chunk, so ceil(half / 16) already covers the zone with a
            // ring to spare (the review's F4: half / 16 + 2 held (2 dim + 3)^2 chunks, 81 for a vanilla claim)
            int want = (half + 15) / 16;
            int dim = Mathf.Clamp(want, 1, MaxViewDim);
            observer = GameManager.Instance.AddChunkObserver(centre, false, dim, -1);
            heldXMin = xMin; heldXMax = xMax; heldZMin = zMin; heldZMax = zMax;
            ScLog.Out("home hold: keeping the home zone loaded around " + centre + " (" + dim + " chunk radius"
                      + (want > MaxViewDim ? ", wider than that: partly" : "") + ") - a player is " + (int)near + " m from it");
        }

        /// <summary>The community in scope's hold goes back. Round MP6 (part H): by its row (Release(s, why)).</summary>
        private static void Release(string why)
        {
            Release(homes.Current, why);
        }

        private static void Release(HoldState s, string why)
        {
            if (s == null || s.Observer == null) return;
            try
            {
                if (GameManager.Instance != null && GameManager.Instance.World != null) GameManager.Instance.RemoveChunkObserver(s.Observer);
                if (why != null) ScLog.Out("home hold: released the home zone (" + why + ")");
            }
            catch (Exception e)
            {
                ScLog.Once("homehold-release", e);
            }
            finally
            {
                s.Observer = null;
            }
        }
    }
}
