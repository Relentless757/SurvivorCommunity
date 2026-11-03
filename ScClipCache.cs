using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace SurvivorCommunity
{
    /// <summary>
    /// V2 (research\voices\REPORT.md 5.6): SC's own clips, loaded by SC, at most ScConfig.VoiceClipCache of them, the least
    /// recently used dropped (destroyed) first - never one still playing. Every SC clip goes through here: the talk's lines,
    /// greetings, chatter, taunts and the XNPCCore lines in SC's voices. Voice Control's own loader keeps every clip it
    /// loads, decompressed, for the session (VoiceClipLoader._cache); SC hands Voice Control's player a clip it loaded here
    /// (ScVoice.PlayOnVc seeds that cache for the one call and takes it out again), so its cache never grows by SC's clips.
    /// SC's clips live in Resources/Voices, never under NPC_Voices, which Voice Control preloads whole. Main thread only.
    ///
    /// Two ways to load, cached apart: compressed in memory (DownloadHandlerAudioClip.compressed, Unity decodes as it plays;
    /// about a tenth of the memory) for a clip SC plays from its own source - a raider's taunt, a survivor without Voice
    /// Control's player - and decompressed for one played through Voice Control's player, whose face lip sync reads the
    /// samples with AudioClip.GetData (NPCFaceLipSync.Update), which a compressed clip refuses (an error every frame).
    /// </summary>
    public static class ScClipCache
    {
        private sealed class Entry
        {
            public string Key;                               // "c|path" or "d|path"
            public string Path;
            public bool Compressed;
            public AudioClip Clip;
            public float BusyUntil;                          // Time.unscaledTime: playing (or just handed to a source) until then
            public LinkedListNode<Entry> Node;
        }

        private static readonly Dictionary<string, Entry> byKey = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private static readonly LinkedList<Entry> lru = new LinkedList<Entry>();   // first = most recently used
        private static readonly Dictionary<string, List<Action<AudioClip>>> loading = new Dictionary<string, List<Action<AudioClip>>>(StringComparer.OrdinalIgnoreCase);
        private static int loads, hits, dropped, failed;
        private static bool compressedOk = true;

        private static string KeyOf(string path, bool compressed) { return (compressed && compressedOk ? "c|" : "d|") + path; }

        /// <summary>
        /// The clip for this .ogg, at once when it is loaded (ready runs before this returns), else when the load finishes
        /// (null: it failed). compressed: for SC's own source only - never hand a compressed clip to Voice Control's player.
        /// </summary>
        public static void Get(string path, bool compressed, Action<AudioClip> ready)
        {
            if (string.IsNullOrEmpty(path)) { ready?.Invoke(null); return; }
            string key = KeyOf(path, compressed);
            Entry e;
            if (byKey.TryGetValue(key, out e) && e.Clip != null)
            {
                hits++;
                lru.Remove(e.Node);
                lru.AddFirst(e.Node);
                ready?.Invoke(e.Clip);
                return;
            }
            List<Action<AudioClip>> waiting;
            if (loading.TryGetValue(key, out waiting))
            {
                waiting.Add(ready);
                return;
            }
            GameManager gm = GameManager.Instance;
            if (gm == null) { ready?.Invoke(null); return; }
            loading[key] = new List<Action<AudioClip>> { ready };
            gm.StartCoroutine(Load(key, path, key.StartsWith("c|", StringComparison.Ordinal)));
        }

        /// <summary>A clip just started on a source: it is not dropped before it ends.</summary>
        public static void MarkPlaying(AudioClip clip)
        {
            if (clip == null) return;
            foreach (Entry e in lru)
            {
                if ((object)e.Clip != (object)clip) continue;
                e.BusyUntil = Time.unscaledTime + clip.length + 1f;
                return;
            }
        }

        private static IEnumerator Load(string key, string path, bool compressed)
        {
            AudioClip clip = null;
            UnityWebRequest req = null;
            try
            {
                req = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, AudioType.OGGVORBIS);
                if (compressed)
                {
                    try
                    {
                        var dh = req.downloadHandler as DownloadHandlerAudioClip;
                        if (dh != null) dh.compressed = true;
                    }
                    catch (Exception ex)
                    {
                        compressedOk = false;
                        compressed = false;
                        ScLog.Once("voice-cache-compressed", "voice: compressed clip loads are not available (" + ex.Message + ") - clips load decompressed");
                    }
                }
            }
            catch (Exception ex)
            {
                ScLog.Once("voice-cache-request", ex);
                req = null;
            }
            if (req != null)
            {
                yield return req.SendWebRequest();
                try
                {
                    if (req.result == UnityWebRequest.Result.Success) clip = DownloadHandlerAudioClip.GetContent(req);
                    else ScLog.Once("voice-load-" + path, "voice: " + path + " did not load: " + req.error);
                }
                catch (Exception ex)
                {
                    ScLog.Once("voice-cache-load", ex);
                    clip = null;
                }
                finally
                {
                    req.Dispose();
                }
            }
            loads++;
            if (clip == null) failed++;
            else
            {
                clip.name = System.IO.Path.GetFileNameWithoutExtension(path);
                var e = new Entry { Key = key, Path = path, Compressed = compressed, Clip = clip };
                Entry old;
                if (byKey.TryGetValue(key, out old) && old != null && old.Node != null && old.Node.List != null) lru.Remove(old.Node);
                e.Node = lru.AddFirst(e);
                byKey[key] = e;
                Trim();
            }
            List<Action<AudioClip>> waiting;
            if (!loading.TryGetValue(key, out waiting)) waiting = new List<Action<AudioClip>>();
            loading.Remove(key);
            foreach (Action<AudioClip> a in waiting)
            {
                try { a?.Invoke(clip); }
                catch (Exception ex) { ScLog.Once("voice-cache-ready", ex); }
            }
        }

        /// <summary>Drops the least recently used clips over the limit, skipping any still playing.</summary>
        private static void Trim()
        {
            int max = Math.Max(4, ScConfig.VoiceClipCache);
            float now = Time.unscaledTime;
            LinkedListNode<Entry> n = lru.Last;
            while (lru.Count > max && n != null)
            {
                LinkedListNode<Entry> prev = n.Previous;
                Entry e = n.Value;
                if (e.BusyUntil <= now)
                {
                    lru.Remove(n);
                    byKey.Remove(e.Key);
                    Destroy(e.Clip);
                    dropped++;
                }
                n = prev;
            }
        }

        private static void Destroy(AudioClip clip)
        {
            try { if (clip != null) UnityEngine.Object.Destroy(clip); }
            catch (Exception ex) { ScLog.Once("voice-cache-destroy", ex); }
        }

        /// <summary>sc voice rescan: every clip not playing is dropped (replaced files load again). Returns how many.</summary>
        public static int Clear()
        {
            float now = Time.unscaledTime;
            int n = 0;
            var keep = new List<Entry>();
            foreach (Entry e in lru)
            {
                if (e.BusyUntil > now) { keep.Add(e); continue; }
                Destroy(e.Clip);
                n++;
            }
            lru.Clear();
            byKey.Clear();
            foreach (Entry e in keep)
            {
                e.Node = lru.AddLast(e);
                byKey[e.Key] = e;
            }
            return n;
        }

        /// <summary>For sc voice: what is loaded and what the cache did this session.</summary>
        public static string Describe()
        {
            float secs = 0f;
            int comp = 0;
            foreach (Entry e in lru)
            {
                if (e.Clip != null) secs += e.Clip.length;
                if (e.Compressed) comp++;
            }
            return lru.Count + " clip(s) loaded (" + secs.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " s of speech; "
                   + comp + " compressed, " + (lru.Count - comp) + " decompressed for lip sync; at most " + Math.Max(4, ScConfig.VoiceClipCache) + "); "
                   + loads + " load(s), " + hits + " hit(s), " + dropped + " dropped, " + failed + " failed"
                   + (compressedOk ? "" : "; compressed loads unavailable");
        }
    }
}
