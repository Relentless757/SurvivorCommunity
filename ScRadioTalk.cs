using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>R2: the moments of a supply run a runner with a radio speaks up at - Member.RunTalk's bits, each said at most once a run.</summary>
    [Flags]
    public enum RadioTalk
    {
        None = 0,
        /// <summary>Physical run, Outbound: OutRange meters from their post ("Heading out now.").</summary>
        Out = 1,
        /// <summary>Physical run: they reach the site (ScRunners.EnterSite) and do not radio for help there.</summary>
        Arrived = 2,
        /// <summary>Physical run: the site search is over with at least an average haul (RunYieldBase) - LeaveSite.</summary>
        Found = 4,
        /// <summary>Physical run: the site search is over with less than an average haul.</summary>
        Empty = 8,
        /// <summary>Any run: they radio for help (ScRadio.ForceDistress).</summary>
        Trouble = 16,
        /// <summary>Physical run, Inbound: HomeRange meters from the site ("Done here. Heading back home.").</summary>
        Home = 32,
        /// <summary>Physical run: they reach the gate near home, where the haul counts (ScRunners.EnterBack).</summary>
        Near = 64,
        /// <summary>Told to come home from the radio window (XUiC_ScRadio's Call home).</summary>
        RecallAck = 128
    }

    /// <summary>
    /// R2, decisions 4 and 6 (research_radio_DESIGN_R2.md): a runner who carries a handheld radio (Member.RunRadio, ScHandheld)
    /// talks to the player during a supply run. Each moment of the run (RadioTalk) has one or two recorded lines - Localization
    /// keys scRadioRun&lt;Event&gt;_&lt;n&gt;, the clip &lt;key&gt;.ogg in their SC voice (ScVoice.MemberClip: their body's chain, or one built
    /// from their record while the body is unloaded), the caption the row &lt;key&gt;Said - said at most once a run, and only
    /// heard while the player carries a handheld radio too (radio to radio, no station needed; the acknowledgment of a recall
    /// is heard on whichever radio sent it). A moment that passes unheard (no radio either side, the player without one, the
    /// runner beside the player, RunnerRadioTalk off) is spent all the same.
    ///
    /// Lines play one at a time from a queue (Tick, once a second): the caption is a tell, "Mara on the radio: "...""; with a
    /// recording the voice plays as a radio - SC's own 2D source (heard wherever the player is), through a high-pass, a
    /// low-pass and a little distortion, a squelch of noise in and out (RunnerRadioEffect) - and the caption shows as it
    /// starts. Without one the caption shows alone. The clip asset is the one the same survivor's talk uses (ScClipCache), so a
    /// line in person sounds as before: the filters belong to the radio's source only.
    ///
    /// Hidden ("at home") runs have no route, so of these moments only Trouble happens to them (at their send, as before);
    /// the rest are physical runs'. Host only, main thread.
    /// </summary>
    public static class ScRadioTalk
    {
        /// <summary>A runner this far from their post on the way out says they are off.</summary>
        public const float OutRange = 60f;
        /// <summary>A runner this far from the site on the way back says they are heading home.</summary>
        public const float HomeRange = 40f;
        /// <summary>A runner's body this close to the player talks in person, not on the radio: the moment passes unsaid.</summary>
        public const float InPersonRange = 30f;
        /// <summary>A caption-only line holds the queue this long (real seconds), so the next one does not cover it.</summary>
        private const float CaptionSeconds = 4f;

        private static readonly Dictionary<RadioTalk, string[]> Keys = new Dictionary<RadioTalk, string[]>
        {
            { RadioTalk.Out, new[] { "scRadioRunOut_1", "scRadioRunOut_2" } },
            { RadioTalk.Arrived, new[] { "scRadioRunArrived_1", "scRadioRunArrived_2" } },
            { RadioTalk.Found, new[] { "scRadioRunFound_1", "scRadioRunFound_2" } },
            { RadioTalk.Empty, new[] { "scRadioRunEmpty_1", "scRadioRunEmpty_2" } },
            { RadioTalk.Trouble, new[] { "scRadioRunTrouble_1", "scRadioRunTrouble_2" } },
            { RadioTalk.Home, new[] { "scRadioRunHome_1", "scRadioRunHome_2" } },
            { RadioTalk.Near, new[] { "scRadioRunNear_1" } },
            { RadioTalk.RecallAck, new[] { "scRadioRecallAck_1" } },
        };

        private sealed class Line
        {
            public int MemberId;
            public string Name, Key, Words, Path, Voice;
            public RadioTalk Event;
        }

        private static readonly Queue<Line> queue = new Queue<Line>();
        private static readonly System.Random rnd = new System.Random();
        /// <summary>Real time until which the queue waits (a caption on screen, a clip loading or playing).</summary>
        private static float busyUntil;
        /// <summary>Bumped by Reset: a coroutine or a clip load that finishes late checks it.</summary>
        private static int token;

        // the radio's own source: a 2D AudioSource on a GameObject of its own, with its filters
        private static GameObject audioGo;
        private static AudioSource source;
        private static AudioHighPassFilter highPass;
        private static AudioLowPassFilter lowPass;
        private static AudioDistortionFilter distortion;
        private static AudioClip squelchIn, squelchOut;

        /// <summary>Per world (ScConfig.Reset) and as the world shuts down: the queue, the source and the clips go.</summary>
        public static void Reset()
        {
            token++;
            queue.Clear();
            busyUntil = 0f;
            try
            {
                if (source != null) source.Stop();
                if (audioGo != null) UnityEngine.Object.Destroy(audioGo);
                if (squelchIn != null) UnityEngine.Object.Destroy(squelchIn);
                if (squelchOut != null) UnityEngine.Object.Destroy(squelchOut);
            }
            catch (Exception e)
            {
                ScLog.Once("radio-talk-reset", e);
            }
            audioGo = null;
            source = null;
            highPass = null;
            lowPass = null;
            distortion = null;
            squelchIn = null;
            squelchOut = null;
        }

        // ------------------------------------------------------------------ the moments ----

        /// <summary>Marks moments as spent without a word (a recall makes the site's haul and the way home moot).</summary>
        public static void Mark(Member m, RadioTalk bits)
        {
            if (m != null) m.RunTalk |= (int)bits;
        }

        public static bool Said(Member m, RadioTalk ev) { return m != null && (m.RunTalk & (int)ev) != 0; }

        /// <summary>The site search is over (ScRunners.LeaveSite, not a rescue, a recall or a failed distress): a good haul or a poor one.</summary>
        public static void OnLeftSite(Member m, EntityPlayerLocal player)
        {
            if (m == null) return;
            bool good = m.RunCarry >= Math.Max(1, ScConfig.RunYieldBase);
            Mark(m, good ? RadioTalk.Empty : RadioTalk.Found);       // one line of the two
            Say(m, good ? RadioTalk.Found : RadioTalk.Empty, player, false);
        }

        /// <summary>Told to come home over the radio (XUiC_ScRadio): the acknowledgment; the way home needs no other line.</summary>
        public static void OnRecalled(Member m, EntityPlayerLocal player)
        {
            Mark(m, RadioTalk.Found | RadioTalk.Empty | RadioTalk.Home);
            Say(m, RadioTalk.RecallAck, player, false);
            ScHandheld.Count("recalled by radio");
        }

        /// <summary>
        /// A runner's moment: said on the radio when they carry one, the player can hear it and it was not said this run (force:
        /// the console - any runner, said or not, heard or not). Spent either way. The result says what happened (the console).
        /// The caller saves (the registry keeps RunTalk).
        /// </summary>
        public static string Say(Member m, RadioTalk ev, EntityPlayerLocal player, bool force)
        {
            try
            {
                if (m == null || ev == RadioTalk.None) return "nobody";
                if (!force && Said(m, ev)) return "already said this run";
                Mark(m, ev);
                if (!force)
                {
                    if (!ScConfig.RunnerRadioTalk) { ScHandheld.Count("talk off"); return "RunnerRadioTalk is off"; }
                    if (!m.RunRadio) { ScHandheld.Count("talk unsaid: runner has no radio"); return m.Name + " carries no radio"; }
                    if (player == null || player.IsDead() || (ev != RadioTalk.RecallAck && !ScHandheld.Carries(player)))
                    {
                        ScHandheld.Count("talk unheard: you carry no radio");
                        ScLog.Verbose(m.Name + "'s radio moment " + ev + " went unheard (you carry no handheld radio)");
                        return "unheard: you carry no handheld radio";
                    }
                    if (InPerson(m, player)) { ScHandheld.Count("talk unsaid: in person"); return m.Name + " is right here"; }
                }
                string[] keys;
                if (!Keys.TryGetValue(ev, out keys) || keys.Length == 0) return "no line for " + ev;
                string key = keys[keys.Length == 1 ? 0 : rnd.Next(keys.Length)];
                string voice;
                string path = ScVoice.MemberClip(m, key, out voice);
                string words = Localization.Get(key + "Said");
                if (string.IsNullOrEmpty(words) || words == key + "Said") words = key;
                if (queue.Count >= 8) queue.Dequeue();                 // never a backlog: the oldest goes
                queue.Enqueue(new Line { MemberId = m.Id, Name = m.Name, Key = key, Words = words, Path = path, Voice = voice, Event = ev });
                ScLog.Out(m.Name + " on the radio (" + ev + "): " + key + (path != null ? " in " + voice : " - no recording, the caption alone"));
                return "queued " + key + (path != null ? " in " + voice : " (no recording: the caption alone)");
            }
            catch (Exception e)
            {
                ScLog.Once("radio-talk-say", e);
                return "error";
            }
        }

        /// <summary>A physical runner's body within InPersonRange of the player: they would just talk.</summary>
        private static bool InPerson(Member m, EntityPlayerLocal player)
        {
            if (!ScRunners.IsPhysical(m) || m.Offline) return false;
            EntityAlive body = ScCommunity.FindNpc(m.Id);
            return body != null && ScRunners.XZ(body.position, player.position) <= InPersonRange;
        }

        /// <summary>
        /// ScTicker, once a second (host): the moments that are places on the route - leaving home behind (Out), leaving the
        /// site behind (Home) - then the next line of the queue.
        /// </summary>
        public static void Tick(World w, EntityPlayerLocal player)
        {
            try
            {
                if (w == null || player == null || !ScRegistry.Loaded) return;
                bool changed = false;
                foreach (Member m in ScRegistry.Members.Values)
                {
                    if (!m.RunRadio || !ScRunners.IsPhysical(m)) continue;
                    if (m.RunLeg == RunLeg.Outbound && !Said(m, RadioTalk.Out))
                    {
                        if (ScRunners.XZ(ScRunners.Where(m), ScCommunity.PostCenter(m)) >= OutRange) { Say(m, RadioTalk.Out, player, false); changed = true; }
                    }
                    else if (m.RunLeg == RunLeg.Inbound && !Said(m, RadioTalk.Home) && m.RunSite != Vector3i.zero)
                    {
                        if (ScRunners.XZ(ScRunners.Where(m), ScRunners.C(m.RunSite)) >= HomeRange) { Say(m, RadioTalk.Home, player, false); changed = true; }
                    }
                }
                if (changed) ScRegistry.SaveAtomic();
                PlayNext(player);
            }
            catch (Exception e)
            {
                ScLog.Once("radio-talk-tick", e);
            }
        }

        // ------------------------------------------------------------------ recall (decision 6) ----

        /// <summary>
        /// Can this runner be told to come home over the radio now? A physical run (a hidden one has no route to turn round on),
        /// the runner's radio, not pinned down (help them or wait), not already on the way home. why: a short reason (the button).
        /// </summary>
        public static bool CanRecall(Member m, out string why)
        {
            why = null;
            if (m == null || m.Status != MemberStatus.OnRun) { why = Localization.Get("scRadioRunNone"); return false; }
            if (!ScRunners.IsPhysical(m)) { why = Localization.Get("scRadioRunNoRoute"); return false; }
            if (!m.RunRadio) { why = Localization.Get("scRadioRunNoRadio"); return false; }
            if (ScRunners.Pinned(m)) { why = Localization.Get("scRadioRunPinnedShort"); return false; }
            if (m.RunLeg != RunLeg.Outbound && m.RunLeg != RunLeg.AtSite) { why = Localization.Get("scRadioRunHeadingShort"); return false; }
            return true;
        }

        // ------------------------------------------------------------------ playing ----

        private static void PlayNext(EntityPlayerLocal player)
        {
            float now = Time.unscaledTime;
            if (queue.Count == 0 || now < busyUntil) return;
            Line line = queue.Dequeue();
            if (line.Path == null)
            {
                Caption(player, line);
                ScHandheld.Count("talk said: caption alone");
                busyUntil = now + CaptionSeconds;
                return;
            }
            busyUntil = now + 30f;                                     // while the clip loads (lowered when it plays)
            int my = token;
            ScClipCache.Get(line.Path, true, clip =>
            {
                try
                {
                    if (my != token) return;
                    EntityPlayerLocal p = ScCommunity.LocalPlayer;
                    Caption(p, line);
                    if (clip == null || GameManager.Instance == null)
                    {
                        ScHandheld.Count("talk said: caption alone (clip did not load)");
                        busyUntil = Time.unscaledTime + CaptionSeconds;
                        return;
                    }
                    ScHandheld.Count("talk said: voiced");
                    bool effect = ScConfig.RunnerRadioEffect;
                    busyUntil = Time.unscaledTime + clip.length + (effect ? 0.6f : 0.3f) + 30f;   // the coroutine lowers it when the line ends
                    GameManager.Instance.StartCoroutine(PlayLine(clip, Mathf.Clamp01(ScConfig.RunnerRadioVolume), effect, my));
                }
                catch (Exception e)
                {
                    ScLog.Once("radio-talk-play", e);
                    busyUntil = Time.unscaledTime + CaptionSeconds;
                }
            });
        }

        private static void Caption(EntityPlayerLocal player, Line line)
        {
            if (player == null) return;
            ScCommunity.Tell(player, string.Format(Localization.Get("scRadioRunTalk"), line.Name, line.Words), "");
        }

        /// <summary>The line on the radio's source: a squelch in, the voice, a squelch out (the plain voice without the effect).</summary>
        private static IEnumerator PlayLine(AudioClip voice, float volume, bool effect, int my)
        {
            AudioSource s = Source(effect);
            if (s == null) { busyUntil = Time.unscaledTime + CaptionSeconds; yield break; }
            s.volume = volume;
            if (effect)
            {
                s.PlayOneShot(SquelchIn(), 0.45f);
                yield return new WaitForSecondsRealtime(0.14f);
                if (my != token || s == null) yield break;
            }
            s.clip = voice;
            s.Play();
            ScClipCache.MarkPlaying(voice);
            // until the source is done with it (a paused game holds it), but never longer than its length and half a minute
            float guard = Time.unscaledTime + voice.length + 30f;
            yield return null;
            while (my == token && s != null && s.isPlaying && (object)s.clip == (object)voice && Time.unscaledTime < guard) yield return null;
            if (my != token || s == null) yield break;
            if (effect) s.PlayOneShot(SquelchOut(), 0.45f);
            busyUntil = Time.unscaledTime + (effect ? 0.6f : 0.3f);
        }

        /// <summary>
        /// The radio's AudioSource: 2D (heard wherever the player is), on its own GameObject with a high-pass at 450 Hz, a
        /// low-pass at 3200 Hz and a little distortion - a voice on a cheap radio. The effect off: the filters are bypassed.
        /// </summary>
        private static AudioSource Source(bool effect)
        {
            try
            {
                if (source == null)
                {
                    audioGo = new GameObject("ScRadioVoice");
                    source = audioGo.AddComponent<AudioSource>();
                    source.spatialBlend = 0f;
                    source.playOnAwake = false;
                    source.loop = false;
                    source.bypassReverbZones = true;
                    source.bypassListenerEffects = true;
                    source.priority = 32;
                    highPass = audioGo.AddComponent<AudioHighPassFilter>();
                    highPass.cutoffFrequency = 450f;
                    highPass.highpassResonanceQ = 1.2f;
                    lowPass = audioGo.AddComponent<AudioLowPassFilter>();
                    lowPass.cutoffFrequency = 3200f;
                    lowPass.lowpassResonanceQ = 1.4f;
                    distortion = audioGo.AddComponent<AudioDistortionFilter>();
                    distortion.distortionLevel = 0.3f;
                }
                source.bypassEffects = !effect;
                if (highPass != null) highPass.enabled = effect;
                if (lowPass != null) lowPass.enabled = effect;
                if (distortion != null) distortion.enabled = effect;
                return source;
            }
            catch (Exception e)
            {
                ScLog.Once("radio-talk-source", e);
                return null;
            }
        }

        private static AudioClip SquelchIn()
        {
            if (squelchIn == null) squelchIn = Noise("ScRadioSquelchIn", 0.18f, 0.7f, 11);
            return squelchIn;
        }

        private static AudioClip SquelchOut()
        {
            if (squelchOut == null) squelchOut = Noise("ScRadioSquelchOut", 0.12f, 1.6f, 23);
            return squelchOut;
        }

        /// <summary>A burst of noise with crackle, a fast attack and a decay (the radio's squelch; its filters band-limit it).</summary>
        private static AudioClip Noise(string name, float seconds, float decay, int seed)
        {
            const int rate = 22050;
            int n = Math.Max(64, (int)(seconds * rate));
            var data = new float[n];
            var r = new System.Random(seed);
            float attack = 0.004f * rate;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float env = Mathf.Min(1f, i / attack) * Mathf.Pow(1f - t, decay);
                float hiss = (float)(r.NextDouble() * 2.0 - 1.0) * 0.5f;
                float crackle = r.NextDouble() < 0.015 ? (float)(r.NextDouble() * 2.0 - 1.0) * 0.6f : 0f;
                data[i] = (hiss + crackle) * env;
            }
            AudioClip c = AudioClip.Create(name, n, 1, rate, false);
            c.SetData(data, 0);
            return c;
        }

        // ------------------------------------------------------------------ the console ----

        /// <summary>A runner's radio state for sc radio: carries one, the moments said this run.</summary>
        public static string Describe(Member m)
        {
            if (m == null) return "-";
            var sb = new StringBuilder();
            foreach (RadioTalk ev in Keys.Keys)
            {
                if (!Said(m, ev)) continue;
                sb.Append(sb.Length > 0 ? "," : "").Append(ev);
            }
            return (m.RunRadio ? "radio" : "no radio") + ", said: " + (sb.Length > 0 ? sb.ToString() : "nothing yet");
        }

        /// <summary>The queue's state for sc radio.</summary>
        public static string QueueState()
        {
            float left = busyUntil - Time.unscaledTime;
            return queue.Count + " line(s) waiting" + (left > 0f ? ", busy " + left.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " s" : "");
        }

        /// <summary>sc radio talk: a moment's name (out, arrived, found, empty, trouble, home, near, recall).</summary>
        public static bool TryEvent(string s, out RadioTalk ev)
        {
            ev = RadioTalk.None;
            switch ((s ?? "").ToLowerInvariant())
            {
                case "out": ev = RadioTalk.Out; return true;
                case "arrived": ev = RadioTalk.Arrived; return true;
                case "found": ev = RadioTalk.Found; return true;
                case "empty": ev = RadioTalk.Empty; return true;
                case "trouble": ev = RadioTalk.Trouble; return true;
                case "home": ev = RadioTalk.Home; return true;
                case "near": ev = RadioTalk.Near; return true;
                case "recall": case "recallack": ev = RadioTalk.RecallAck; return true;
            }
            return false;
        }
    }
}
