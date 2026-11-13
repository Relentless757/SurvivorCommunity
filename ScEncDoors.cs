using System;
using System.Collections.Generic;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round 32a (the user's report, 2026-11-12: "For enclaves, all the doors in the POI should be unlocked so it's easier to
    /// navigate and meet the people"): every locked door of a living enclave's building is unlocked - survivors' and raiders'.
    ///
    /// What is a door: in 3.x the old TileEntitySecureDoor is a TileEntityComposite with a TEFeatureDoor, its lock a
    /// TEFeatureLockable. That covers single and double doors, gates, hatches, garage, vault and cellar doors and drawbridges
    /// (vanilla 3.4: 540 composite door blocks, every one with both features and nothing else). Powered doors keep a
    /// TileEntityPowered and have no lock; loot containers (TEFeatureStorage: E2's container lock keeps them the enclave's),
    /// safes and chests (TEFeatureLockPickable) and signs are not doors and are never touched. A door another mod gives a
    /// lock pick that still needs picking is left as it is (unlocking it would swap its block), as is a door that is a
    /// player's (PlayerPlaced, which is "has an owner": a POI's door has none).
    ///
    /// What changes: only the lock flag (TEFeatureLockable.SetLocked(false), which marks the tile entity modified: the chunk
    /// saves it and clients get it). The owner stays nobody, so the door is not the player's (no Lock / Keypad entry for them,
    /// vanilla's rule) and anyone may open it, the player and any NPC that opens doors alike; no door is opened here.
    ///
    /// When: ScTicker's second, after ScEnclavePeople.Tick. For each living enclave (not Lost) with a player within
    /// EnclaveSpawnRange + EnclaveDespawnMargin of its footprint, each chunk the footprint covers is passed over once per
    /// session as soon as it is loaded and decorated (a new chunk gets the prefab's blocks and tile entities in its
    /// decoration), reading the chunk's own tile entity list - no block scan. When every chunk of the footprint has been
    /// passed, one line: "enclave &lt;id&gt; '&lt;name&gt;': unlocked N door(s) ..." (each door at Verbose). Existing saves are
    /// covered the same way: the pass is per session, not per record. An enclave that moves starts over at its new
    /// building; one that falls, moves or joins the player leaves its old building's doors as they are - unlocked. Host
    /// only (single player is the host): a client keeps no enclave records. EnclaveDoorsUnlocked false (or Enclaves false)
    /// stops the passes; doors already unlocked stay unlocked.
    /// </summary>
    public static class ScEncDoors
    {
        /// <summary>Session state per enclave id: the footprint it is for (a move starts over), the chunks done, the counts.</summary>
        private sealed class State
        {
            public Vector3i Poi;
            public Vector3i Size;
            public readonly HashSet<long> Done = new HashSet<long>();
            public int Chunks;
            public int Doors, Unlocked, Kept;
            public bool Logged;
        }

        private static readonly Dictionary<int, State> states = new Dictionary<int, State>();

        /// <summary>Per world (ModApi, GameStarting).</summary>
        public static void Reset()
        {
            states.Clear();
        }

        /// <summary>The 1 Hz pass (ScTicker, after ScEnclavePeople.Tick).</summary>
        public static void Tick(World w)
        {
            try
            {
                if (w == null || !ScCommunity.IsHost || !ScEnclaves.Ready || !ScRegistry.Loaded) return;
                ScConfig.EnsureParsed();
                if (!ScConfig.EnclaveDoorsUnlocked || !ScConfig.Enclaves) return;
                List<Enclave> all = ScEnclaves.All;
                if (all == null || all.Count == 0) return;
                float reach = ScConfig.EnclaveSpawnRange + ScConfig.EnclaveDespawnMargin;
                for (int i = 0; i < all.Count; i++)
                {
                    Enclave e = all[i];
                    if (e == null || e.Lost) continue;
                    try
                    {
                        State s = StateOf(e);
                        if (s.Logged) continue;
                        if (NearestPlayer(w, e) > reach) continue;
                        Pass(w, e, s);
                    }
                    catch (Exception ex)
                    {
                        ScLog.Once("encdoors-" + e.Id, ex);
                    }
                }
            }
            catch (Exception e)
            {
                ScLog.Once("encdoors-tick", e);
            }
        }

        /// <summary>The enclave's session state, started over when its footprint is not the one it was made for (a move).</summary>
        private static State StateOf(Enclave e)
        {
            State s;
            if (states.TryGetValue(e.Id, out s) && s.Poi == e.Poi && s.Size == e.Size) return s;
            s = new State { Poi = e.Poi, Size = e.Size };
            int[] b = e.Box;
            s.Chunks = (World.toChunkXZ(b[1]) - World.toChunkXZ(b[0]) + 1) * (World.toChunkXZ(b[3]) - World.toChunkXZ(b[2]) + 1);
            states[e.Id] = s;
            return s;
        }

        /// <summary>Horizontal meters from the nearest player to the footprint (0 inside it).</summary>
        private static float NearestPlayer(World w, Enclave e)
        {
            float best = float.MaxValue;
            List<EntityPlayer> list = w.Players != null ? w.Players.list : null;
            if (list == null) return best;
            for (int i = 0; i < list.Count; i++)
            {
                EntityPlayer p = list[i];
                if (p != null) best = Mathf.Min(best, e.DistanceXZ(p.position.x, p.position.z));
            }
            return best;
        }

        /// <summary>Every chunk of the footprint that is ready and not yet done this session; the line once all of them are.</summary>
        private static void Pass(World w, Enclave e, State s)
        {
            int[] b = e.Box;     // {xMin, xMax, zMin, zMax}, inclusive
            int yMin = e.Poi.y, yMax = e.Poi.y + Math.Max(1, e.Size.y) - 1;
            int cx0 = World.toChunkXZ(b[0]), cx1 = World.toChunkXZ(b[1]);
            int cz0 = World.toChunkXZ(b[2]), cz1 = World.toChunkXZ(b[3]);
            List<long> fresh = w.newlyLoadedChunksThisUpdate;
            for (int cx = cx0; cx <= cx1; cx++)
            {
                for (int cz = cz0; cz <= cz1; cz++)
                {
                    long key = WorldChunkCache.MakeChunkKey(cx, cz);
                    if (s.Done.Contains(key)) continue;
                    Chunk ch = w.GetChunkSync(cx, cz) as Chunk;
                    if (ch == null || ch.InProgressUnloading || ch.NeedsDecoration || ch.InProgressDecorating) continue;
                    if (fresh != null)
                    {
                        bool isNew;
                        lock (fresh) { isNew = fresh.Contains(key); }
                        if (isNew) continue;
                    }
                    PassChunk(e, s, ch, b, yMin, yMax);
                    s.Done.Add(key);
                }
            }
            if (s.Done.Count < s.Chunks) return;
            s.Logged = true;
            ScLog.Out("enclave " + e.Id + " '" + e.Name + "': unlocked " + s.Unlocked + " door(s) (" + s.Doors + " door(s) in "
                      + (string.IsNullOrEmpty(e.PoiName) ? "their building" : e.PoiName)
                      + (s.Kept > 0 ? ", " + s.Kept + " locked one(s) kept as they were: a player's, or needing a lock pick" : "") + ")");
        }

        /// <summary>The doors among one chunk's tile entities inside the footprint (its own block column range).</summary>
        private static void PassChunk(Enclave e, State s, Chunk ch, int[] b, int yMin, int yMax)
        {
            var tes = ch.tileEntities;
            if (tes == null || tes.list == null) return;
            for (int i = 0; i < tes.list.Count; i++)
            {
                TileEntityComposite te = tes.list[i] as TileEntityComposite;
                if (te == null) continue;
                TEFeatureDoor door = te.GetFeature<TEFeatureDoor>();
                if (door == null) continue;
                if (te.GetFeature<TEFeatureStorage>() != null) continue;         // a container with a door: the enclave's, never ours to open
                Vector3i pos = te.ToWorldPos();
                if (pos.x < b[0] || pos.x > b[1] || pos.z < b[2] || pos.z > b[3] || pos.y < yMin || pos.y > yMax) continue;
                s.Doors++;
                TEFeatureLockable lk = te.GetFeature<TEFeatureLockable>();
                if (lk == null || !lk.IsLocked()) continue;
                string why = null;
                if (te.PlayerPlaced) why = "it is a player's";       // = an owner (TileEntityComposite.PlayerPlaced is Owner != null)
                else
                {
                    TEFeatureLockPickable pick = te.GetFeature<TEFeatureLockPickable>();
                    if (pick != null && pick.NeedsLockpicking()) why = "it needs a lock pick";
                }
                string name = te.blockValue.Block != null ? te.blockValue.Block.GetBlockName() : "?";
                if (why != null)
                {
                    s.Kept++;
                    ScLog.Verbose("enclave " + e.Id + " '" + e.Name + "': the locked " + name + " at " + pos + " is kept locked (" + why + ")");
                    continue;
                }
                lk.SetLocked(false);
                s.Unlocked++;
                ScLog.Verbose("enclave " + e.Id + " '" + e.Name + "': unlocked the " + name + " at " + pos);
            }
        }
    }
}
