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
    ///
    /// Round m1p (the M1 polish bundle; memory survivor-voiced-dialogue, "Open (L4)"): the bound is in memory as well as a
    /// count. With 604 lines in each of 16 voices (9,664 clips, 2.5 s on average, 9.7 s at most), 48 clips decompressed take
    /// about 10 MB, and up to 34 MB when they are the longest lines; every line heard and kept would be about 2 GB. So besides
    /// VoiceClipCache (how many), the least recently used also go while the cache holds more than VoiceClipCacheMB (Estimate:
    /// 16-bit samples for a decompressed clip, the .ogg's size for a compressed one), and a sweep every 30 s (Tick, from
    /// ScBarks.Tick) drops any clip nobody has asked for in VoiceClipIdleMinutes, so a quiet base gives the memory back. A
    /// clip is never dropped while it plays (MarkPlaying: its length and a second) or in the HandOutSeconds after it was handed
    /// to a caller that has not started it yet (the radio's squelch first, a coroutine's wait). Only clips SC loaded are ever
    /// here: Voice Control's own cache (its NPC_Voices clips) is never touched.
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
            public float HandedUntil;                        // round m1p: handed to a caller that has not started it yet, until then
            public float LastUsed;                           // round m1p: last asked for (Time.unscaledTime), for the idle sweep
            public long Bytes;                               // round m1p: its memory, about (Estimate)
            public LinkedListNode<Entry> Node;
        }

        private static readonly Dictionary<string, Entry> byKey = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private static readonly LinkedList<Entry> lru = new LinkedList<Entry>();   // first = most recently used
        private static readonly Dictionary<string, List<Action<AudioClip>>> loading = new Dictionary<string, List<Action<AudioClip>>>(StringComparer.OrdinalIgnoreCase);
        private static int loads, hits, dropped, failed;
        private static bool compressedOk = true;
        // round m1p: the memory bound, the idle sweep and their counters
        private const float HandOutSeconds = 3f, SweepSeconds = 30f;
        private static long bytes;
        private static float nextSweep;
        private static int droppedIdle, droppedSize;

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
                e.LastUsed = Time.unscaledTime;
                e.HandedUntil = e.LastUsed + HandOutSeconds;     // round m1p: kept while the caller starts it
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
                float now = Time.unscaledTime;
                var e = new Entry
                {
                    Key = key, Path = path, Compressed = compressed, Clip = clip,
                    LastUsed = now, HandedUntil = now + HandOutSeconds, Bytes = Estimate(clip, path, compressed),   // round m1p
                };
                Entry old;
                if (byKey.TryGetValue(key, out old) && old != null && old.Node != null && old.Node.List != null)
                {
                    lru.Remove(old.Node);
                    bytes -= old.Bytes;
                }
                e.Node = lru.AddFirst(e);
                byKey[key] = e;
                bytes += e.Bytes;
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

        /// <summary>
        /// Drops the least recently used clips while there are more than VoiceClipCache of them or (round m1p) they hold more
        /// than VoiceClipCacheMB, skipping any still playing or just handed out.
        /// </summary>
        private static void Trim()
        {
            int max = Math.Max(4, ScConfig.VoiceClipCache);
            long maxBytes = MaxBytes;
            float now = Time.unscaledTime;
            LinkedListNode<Entry> n = lru.Last;
            while ((lru.Count > max || bytes > maxBytes) && n != null)
            {
                LinkedListNode<Entry> prev = n.Previous;
                if (!Busy(n.Value, now))
                {
                    if (lru.Count <= max) droppedSize++;          // over the memory bound only
                    Drop(n);
                    dropped++;
                }
                n = prev;
            }
        }

        /// <summary>Round m1p: VoiceClipCacheMB in bytes.</summary>
        private static long MaxBytes { get { return (long)Math.Max(1, ScConfig.VoiceClipCacheMB) * 1024L * 1024L; } }

        /// <summary>Round m1p: playing (MarkPlaying), or handed to a caller in the last HandOutSeconds and not started yet.</summary>
        private static bool Busy(Entry e, float now)
        {
            return e.BusyUntil > now || e.HandedUntil > now;
        }

        /// <summary>Round m1p: one entry out of the cache and its clip destroyed (SC's own clip: nothing else keeps it once it stops).</summary>
        private static void Drop(LinkedListNode<Entry> n)
        {
            Entry e = n.Value;
            lru.Remove(n);
            byKey.Remove(e.Key);
            bytes -= e.Bytes;
            Destroy(e.Clip);
        }

        /// <summary>
        /// Round m1p: a clip's memory, about: 16-bit samples for a decompressed one (Unity keeps it as PCM), the .ogg's size for
        /// one kept compressed (Unity decodes it as it plays).
        /// </summary>
        private static long Estimate(AudioClip clip, string path, bool compressed)
        {
            long pcm = clip != null ? (long)clip.samples * Math.Max(1, clip.channels) * 2L : 0L;
            if (!compressed) return pcm;
            try { return new System.IO.FileInfo(path).Length; }
            catch (Exception) { return pcm / 10; }
        }

        /// <summary>
        /// Round m1p (ScBarks.Tick, every frame; works every SweepSeconds): drops every clip nobody has asked for in
        /// VoiceClipIdleMinutes (0: never by age) that is not playing, then the least recently used over the two bounds.
        /// </summary>
        public static void Tick()
        {
            float now = Time.unscaledTime;
            if (now < nextSweep) return;
            nextSweep = now + SweepSeconds;
            try
            {
                float idle = ScConfig.VoiceClipIdleMinutes * 60f;
                if (idle > 0f)
                {
                    LinkedListNode<Entry> n = lru.Last;
                    while (n != null)
                    {
                        LinkedListNode<Entry> prev = n.Previous;
                        Entry e = n.Value;
                        if (now - e.LastUsed >= idle && !Busy(e, now))
                        {
                            Drop(n);
                            droppedIdle++;
                        }
                        n = prev;
                    }
                }
                Trim();
            }
            catch (Exception ex)
            {
                ScLog.Once("voice-cache-sweep", ex);
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
            bytes = 0;                                       // round m1p: the kept ones' only
            foreach (Entry e in keep)
            {
                e.Node = lru.AddLast(e);
                byKey[e.Key] = e;
                bytes += e.Bytes;
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
            // round m1p: the memory they hold and its bound, the idle sweep and what each dropped
            System.Globalization.CultureInfo inv = System.Globalization.CultureInfo.InvariantCulture;
            return lru.Count + " clip(s) loaded (" + secs.ToString("0", inv) + " s of speech, about " + (bytes / 1048576.0).ToString("0.0", inv) + " MB; "
                   + comp + " compressed, " + (lru.Count - comp) + " decompressed for lip sync; at most " + Math.Max(4, ScConfig.VoiceClipCache) + " clips and "
                   + Math.Max(1, ScConfig.VoiceClipCacheMB) + " MB, idle ones dropped after "
                   + (ScConfig.VoiceClipIdleMinutes > 0f ? ScConfig.VoiceClipIdleMinutes.ToString("0.#", inv) + " min" : "- never") + "); "
                   + loads + " load(s), " + hits + " hit(s), " + dropped + " dropped (" + droppedSize + " for memory), " + droppedIdle + " dropped idle, "
                   + failed + " failed" + (compressedOk ? "" : "; compressed loads unavailable");
        }
    }
}
