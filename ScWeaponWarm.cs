using System;
using System.Collections.Generic;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Loads the held models of the weapons SC's NPCs can be given, in the background, before an NPC needs one (round wp,
    /// the user's call 2026-10-12). A fresh stranger's first spawn of a session took 0.7-0.9 s inside one frame, about
    /// 0.3 s of it its Izayo Tec9's model loaded synchronously: ItemClass.CloneModel -> DataLoader.LoadAsset, the first
    /// use of that model in the session (later spawns with it took 50-200 ms). LoadManager.LoadAsset with _loadSync false
    /// loads the same asset asynchronously (a mod bundle's LoadAssetAsync, an addressable's async operation); once it is in,
    /// the synchronous load returns the loaded object at once.
    ///
    /// One model at a time, StartDelaySeconds after the world is up, and only the weapons the gamestage lets an NPC roll
    /// now (ScLoadout.RollableNow) - the catalogue is 93 weapons with IZY Classic, 52 of them in Izayo bundles of 80-556
    /// MB - checked again every RecheckSeconds as the gamestage rises. The models are held here, as
    /// ItemClass.renderGameObject holds one for the rest of the session once anything has held that item, so Unity's
    /// unused-asset sweep cannot drop them again: the memory is what a long session ends up with anyway, only sooner. Each
    /// batch logs its count, time and the process memory it added. Off with XnpcWarmWeapons false; never on a dedicated
    /// server (it draws nothing). Main thread (ModEvents.GameUpdate).
    /// </summary>
    public static class ScWeaponWarm
    {
        private const float StartDelaySeconds = 20f;
        private const float RecheckSeconds = 120f;
        /// <summary>A request that has not finished by then is given up (and not asked for again this session).</summary>
        private const float MaxWaitSeconds = 30f;

        private static readonly List<UnityEngine.Object> held = new List<UnityEngine.Object>();
        /// <summary>Model paths already asked for this session (two items can share a model).</summary>
        private static readonly HashSet<string> asked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Queue<KeyValuePair<string, string>> queue = new Queue<KeyValuePair<string, string>>();   // item, model path

        private static LoadManager.AssetRequestTask<GameObject> current;
        private static string currentItem;
        private static float currentSince;
        private static float startAt = -1f, nextCheck;
        private static bool inBatch;
        private static int batchLoaded, batchFailed;
        private static float batchStart;
        private static long batchMemBefore;

        public static int Held { get { return held.Count; } }

        /// <summary>Per world (GameStarting) and at its shutdown: the next world asks again.</summary>
        public static void Reset()
        {
            held.Clear();
            asked.Clear();
            queue.Clear();
            current = null;
            currentItem = null;
            startAt = -1f;
            nextCheck = 0f;
            inBatch = false;
            batchLoaded = batchFailed = 0;
        }

        /// <summary>Every frame (ModEvents.GameUpdate).</summary>
        public static void Update()
        {
            try
            {
                if (!ScConfig.XnpcWarmWeapons || GameManager.IsDedicatedServer) return;
                GameManager gm = GameManager.Instance;
                if (gm == null || gm.World == null || gm.World.GetPrimaryPlayer() == null) return;
                float now = Time.unscaledTime;
                if (startAt < 0f) { startAt = now + StartDelaySeconds; return; }
                if (now < startAt) return;
                if (current != null) { Poll(now); return; }
                if (queue.Count > 0) { Next(now); return; }
                if (inBatch) EndBatch(now);
                if (now >= nextCheck)
                {
                    nextCheck = now + RecheckSeconds;
                    Fill(now);
                }
            }
            catch (Exception e)
            {
                ScLog.Once("weaponwarm", e);
            }
        }

        /// <summary>Queues every rollable weapon whose held model has not been asked for yet.</summary>
        private static void Fill(float now)
        {
            if (!ScConfig.Parsed) return;
            int tier = ScLoadout.GamestageTier();
            List<string> items = ScLoadout.RollableNow(tier);
            for (int i = 0; i < items.Count; i++)
            {
                ItemClass ic = ItemClass.GetItemClass(items[i], true);
                if (ic == null) continue;
                // the model a hand holds (ItemClass.CloneModel, MeshPurpose.Hold): HandMeshFile, else MeshFile
                string path = !string.IsNullOrEmpty(ic.HandMeshFile) ? ic.HandMeshFile : ic.MeshFile;
                if (string.IsNullOrEmpty(path) || !asked.Add(path)) continue;
                queue.Enqueue(new KeyValuePair<string, string>(items[i], path));
            }
            if (queue.Count == 0) return;
            inBatch = true;
            batchLoaded = batchFailed = 0;
            batchStart = now;
            batchMemBefore = MemMb();
            ScLog.Out("weapon warm: loading " + queue.Count + " NPC weapon model(s) in the background (gamestage tier " + tier
                      + ", " + items.Count + " weapon(s) an NPC can carry now)");
        }

        private static void Next(float now)
        {
            KeyValuePair<string, string> job = queue.Dequeue();
            currentItem = job.Key;
            currentSince = now;
            current = LoadManager.LoadAsset<GameObject>(job.Value, null, null, false, false);
            if (current == null) { batchFailed++; ScLog.Verbose("weapon warm: no load for " + job.Key + " (" + job.Value + ")"); }
        }

        private static void Poll(float now)
        {
            if (current.IsDone)
            {
                GameObject go = current.Asset;
                if (go != null) { held.Add(go); batchLoaded++; }
                else { batchFailed++; ScLog.Verbose("weapon warm: " + currentItem + "'s model did not load"); }
                current = null;
                return;
            }
            if (now - currentSince > MaxWaitSeconds)
            {
                batchFailed++;
                ScLog.Warn("weapon warm: " + currentItem + "'s model took over " + (int)MaxWaitSeconds + " s - left to load on first use");
                current = null;
            }
        }

        private static void EndBatch(float now)
        {
            inBatch = false;
            long added = MemMb() - batchMemBefore;
            ScLog.Out("weapon warm: " + batchLoaded + " model(s) loaded" + (batchFailed > 0 ? ", " + batchFailed + " failed" : "") + " in "
                      + (now - batchStart).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " s, process memory "
                      + (added >= 0 ? "+" : "") + added + " MB (" + held.Count + " held this session)");
        }

        private static long MemMb()
        {
            try { return System.Diagnostics.Process.GetCurrentProcess().PrivateMemorySize64 / (1024L * 1024L); }
            catch (Exception) { return 0; }
        }
    }
}
