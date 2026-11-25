#if SC_V34
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round U3: how a small-talk line is said (ScSocial) - 0-XNPCCore's small-talk presentation, with SC's lines and voices.
    ///  - The gesture: 0-XNPCCore's own line reaction (SdcsAnimBridgeHandlers.AnyWeaponSdcsAnimLineReaction) with its line ids
    ///    smalltalk.open / smalltalk.reply - the Talk emote its Settings.xml gives those two (Emote TalkLines), exactly what
    ///    Voice Control's SayToEarshot fires for its residents' small talk.
    ///  - The words: the speaker's recorded clip of the line (scTalkOpen_&lt;nn&gt; / scTalkReply_&lt;nn&gt; in one of her voices,
    ///    ScVoice.Chain), played through Voice Control's player with lip sync (ScVoice.PlayBark, as SC's greetings), its words
    ///    in Voice Control's subtitle bar; with no clip yet, the words as a caption only (the user: captions until the
    ///    recordings exist). The caption follows SC's rule for its barks: VoiceBarkSubtitles, and Voice Control's own subtitle
    ///    mode for that NPC (ShowsGenericTextFor). A line with neither a clip nor a caption to show is not started.
    ///  - SmallTalkTtsFallback: Voice Control's 220 pairs through 0-XNPCCore's hook slots (XNPCCore.SmallTalk to open,
    ///    XNPCCore.SayLine "smalltalk.reply" to answer): its text-to-speech, captions and gestures, as for its POI residents.
    /// Voice Control is bound by name (no compile reference), 0-XNPCCore by its public bridge.
    /// Round MP4 (multiplayer): the server says each line and sends it to every player in earshot (NetPackageScVoice, kind small
    /// talk), in the speaker's voice, its caption in each player's own language; the TTS fallback's lines reach them through
    /// Voice Control's own say-line package.
    /// </summary>
    public static class ScSocialVoice
    {
        private const string OpenPrefix = "scTalkOpen_", ReplyPrefix = "scTalkReply_";

        private static readonly List<string> recent = new List<string>();
        private static readonly Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);

        public static void Reset()
        {
            recent.Clear();
            counts.Clear();
        }

        public static string OpenKey(string stem) { return OpenPrefix + stem; }
        public static string ReplyKey(string stem) { return ReplyPrefix + stem; }

        /// <summary>The words a line says: Localization's &lt;key&gt;Said row (written by Tools/smalltalk_lines.py), or null.</summary>
        public static string Words(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            string k = key + "Said", s = Localization.Get(k);
            return string.IsNullOrEmpty(s) || s == k ? null : s;
        }

        public static int PairCount() { return ScSocialLines.Pairs.Length / 2; }

        // ------------------------------------------------------------------ picking ----

        private static bool Has(Member m, string trait)
        {
            if (m == null || m.Traits == null) return false;
            for (int i = 0; i < m.Traits.Count; i++) if (string.Equals(m.Traits[i], trait, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>A good mood: an Optimist, Cheerful, People Person or Hardy member, or a mood of 65 or more (and not a Pessimist).</summary>
        private static bool Up(Member m)
        {
            if (m == null) return false;
            if (Has(m, "Pessimist")) return false;
            return m.Mood >= 65 || Has(m, "Optimist") || Has(m, "Cheerful") || Has(m, "PeoplePerson") || Has(m, "Hardy");
        }

        /// <summary>A low mood: a Pessimist, Insomniac, Hoarder or Cowardly member, or a mood under 35 (and not an Optimist or Cheerful).</summary>
        private static bool Down(Member m)
        {
            if (m == null) return false;
            if (Has(m, "Optimist") || Has(m, "Cheerful")) return false;
            return m.Mood < 35 || Has(m, "Pessimist") || Has(m, "Insomniac") || Has(m, "Hoarder") || Has(m, "Cowardly");
        }

        /// <summary>Who opens: a People Person 1.6, Cheerful 1.2, a Loner 0.5, anyone else 1.</summary>
        public static float OpenerWeight(Member m)
        {
            float w = 1f;
            if (Has(m, "PeoplePerson")) w *= 1.6f;
            if (Has(m, "Cheerful")) w *= 1.2f;
            if (Has(m, "Loner")) w *= 0.5f;
            return w;
        }

        /// <summary>
        /// A pair's weight for this opener: home only at your base, fire only at a campfire gathering (3x there, where the plain
        /// pairs count half), up / down by her mood and traits (2 fitting, 0.6 neutral, 0.2 against), a trait tag 4 when she
        /// has one of its traits and 0.15 otherwise; a pair said in the last few exchanges 0.1.
        /// </summary>
        private static float Weight(string stem, string tags, Member m, bool home, bool fire)
        {
            float w = 1f;
            bool traitTag = false, traitHit = false, fireTag = false;
            if (!string.IsNullOrEmpty(tags))
            {
                foreach (string raw in tags.Split(','))
                {
                    string t = raw.Trim();
                    if (t.Length == 0) continue;
                    switch (t)
                    {
                        case "home": if (!home) return 0f; break;
                        case "fire": if (!fire) return 0f; fireTag = true; w *= 3f; break;
                        case "up": w *= Up(m) ? 2f : Down(m) ? 0.2f : 0.6f; break;
                        case "down": w *= Down(m) ? 2f : Up(m) ? 0.2f : 0.6f; break;
                        default:
                            traitTag = true;
                            if (Has(m, t)) traitHit = true;
                            break;
                    }
                }
            }
            if (traitTag) w *= traitHit ? 4f : 0.15f;
            if (fire && !fireTag) w *= 0.5f;
            if (recent.Contains(stem)) w *= 0.1f;
            return w;
        }

        /// <summary>A pair for this opener (its number), by the weights above; null when the table is empty.</summary>
        public static string PickPair(Member opener, bool home, bool atFire, System.Random rnd)
        {
            string[] p = ScSocialLines.Pairs;
            bool fire = atFire;
            float total = 0f;
            for (int i = 0; i + 1 < p.Length; i += 2) total += Weight(p[i], p[i + 1], opener, home, fire);
            if (total <= 0f) return null;
            double roll = rnd.NextDouble() * total;
            string pick = null;
            for (int i = 0; i + 1 < p.Length; i += 2)
            {
                float w = Weight(p[i], p[i + 1], opener, home, fire);
                if (w <= 0f) continue;
                pick = p[i];
                roll -= w;
                if (roll < 0) break;
            }
            if (pick != null)
            {
                recent.Remove(pick);
                recent.Add(pick);
                if (recent.Count > 8) recent.RemoveAt(0);
            }
            return pick;
        }

        // ------------------------------------------------------------------ saying ----

        /// <summary>The clip of this line in one of her voices (the first of her chain that has it), and that voice; null: none (or VoiceLines off).</summary>
        public static string ClipFor(EntityAlive npc, string key, out string voice)
        {
            voice = null;
            if (!ScConfig.VoiceLines || npc == null || string.IsNullOrEmpty(key)) return null;
            List<ScVoice.Voice> chain = ScVoice.Chain(npc);
            if (chain == null) return null;
            foreach (ScVoice.Voice v in chain)
            {
                string path;
                if (v.Lines.TryGetValue(key, out path)) { voice = v.Name; return path; }
            }
            return null;
        }

        /// <summary>Does one of her voices hold any small-talk opener (the TTS fallback's test: an opener with no recording says Voice Control's pair)?</summary>
        public static bool HasAnyRecording(EntityAlive npc)
        {
            if (!ScConfig.VoiceLines || npc == null) return false;
            List<ScVoice.Voice> chain = ScVoice.Chain(npc);
            if (chain == null) return false;
            string[] p = ScSocialLines.Pairs;
            foreach (ScVoice.Voice v in chain)
                for (int i = 0; i + 1 < p.Length; i += 2)
                    if (v.Lines.ContainsKey(OpenPrefix + p[i])) return true;
            return false;
        }

        /// <summary>
        /// Says one line on her: the gesture, then her clip (its words as the caption) or the caption alone. The seconds it
        /// takes to say (from its words; the clip's own length is waited out by ScVoice.Busy), or -1 when nothing could be
        /// played or shown (no clip and no caption) - then nothing was done.
        /// </summary>
        public static float Say(EntityAlive npc, string key, string lineId, out string how)
        {
            string words = Words(key);
            if (npc == null || words == null) { how = "no words for " + key + " in Localization.csv"; Count("no words"); return -1f; }
            string voice;
            string path = ClipFor(npc, key, out voice);
            bool caption = CanCaption(npc);
            if (path == null && !caption)
            {
                how = "no recording and no caption (VoiceBarkSubtitles, or Voice Control's subtitle mode for " + ScCommunity.DisplayName(npc) + ")";
                Count("unshown");
                return -1f;
            }
            Gesture(npc, lineId);
            float secs = SpeakSeconds(words);
            string name = ScCommunity.DisplayName(npc);
            bool here = ScVoice.PlaysHere;                             // round MP4: a dedicated server only sends it
            if (path != null)
            {
                if (here) ScVoice.PlayBark(npc, path, true, false, clip => Caption(npc, name, words, clip.length + 1f), null);
                else ScVoice.NoteBusy(npc, secs);
                how = "recorded, " + voice;
                Count("clip");
            }
            else
            {
                if (here) Caption(npc, name, words, secs + 1.5f);
                how = "caption only (no recording yet)";
                Count("caption");
            }
            // round MP4: everyone in earshot hears it in the same voice, its caption (<key>Said) in their own language; the
            // gesture rides 0-XNPCCore's own synced clip
            ScVoice.SendNear(npc.position, new NetPackageScVoice.Line
            {
                Kind = NetPackageScVoice.KindSmallTalk, Npc = npc.entityId, Key = key, Voice = path != null ? voice ?? "" : "",
                Caption = NetPackageScVoice.CaptionLoc, CaptionKey = key + "Said", Name = name, Flags = NetPackageScVoice.FlagLipSync
            }, ScVoice.EarshotRange);
            return secs;
        }

        /// <summary>About how long the words take to say: 2.6 words a second plus a breath, 1.6 to 8 s.</summary>
        private static float SpeakSeconds(string words)
        {
            int n = 1;
            for (int i = 0; i < words.Length; i++) if (words[i] == ' ') n++;
            return Mathf.Clamp(0.8f + n / 2.6f, 1.6f, 8f);
        }

        // ------------------------------------------------------------------ Voice Control's pairs (the TTS fallback) ----

        /// <summary>0-XNPCCore's small-talk hook (Voice Control's SmallTalkBridge.Begin): says its opener and returns the reply delay, 0 when refused.</summary>
        public static float VcBegin(EntityAlive a, EntityAlive b, bool force)
        {
            try
            {
                var hook = AppDomain.CurrentDomain.GetData("XNPCCore.SmallTalk") as Func<EntityAlive, EntityAlive, bool, float>;
                if (hook == null) { Count("vc: no hook"); return 0f; }
                float d = hook(a, b, force);
                Count(d > 0f ? "vc opened" : "vc refused");
                return d;
            }
            catch (Exception e)
            {
                ScLog.Once("social-vc-begin", e);
                return 0f;
            }
        }

        /// <summary>The answer: 0-XNPCCore's say-line hook with smalltalk.reply (Voice Control drops it when you are near or chatting).</summary>
        public static void VcReply(EntityAlive b)
        {
            try
            {
                var hook = AppDomain.CurrentDomain.GetData("XNPCCore.SayLine") as Action<EntityAlive, string>;
                if (hook != null) hook(b, "smalltalk.reply");
            }
            catch (Exception e)
            {
                ScLog.Once("social-vc-reply", e);
            }
        }

        // ------------------------------------------------------------------ 0-XNPCCore's gesture ----

        private static bool gestureFailed;

        private static void Gesture(EntityAlive npc, string lineId)
        {
            if (gestureFailed) return;
            try { GestureCore(npc, lineId); }
            catch (Exception e)
            {
                gestureFailed = true;   // an older 0-XNPCCore without the bridge: no gesture, the words still come
                ScLog.Warn("small talk: 0-XNPCCore's line reaction (SdcsAnimBridgeHandlers.AnyWeaponSdcsAnimLineReaction) failed - no gestures: " + e.Message);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void GestureCore(EntityAlive npc, string lineId)
        {
            SdcsAnimBridgeHandlers.AnyWeaponSdcsAnimLineReaction(npc, lineId);
        }

        // ------------------------------------------------------------------ Voice Control's subtitle bar, by name ----

        private static bool vcBound;
        private static MethodInfo mShowsText, mShowSubtitle;
        private static PropertyInfo pSubInstance;

        private static void BindVc()
        {
            if (vcBound) return;
            vcBound = true;
            try
            {
                Type tChat = Patches_VoiceControl.ChatType;
                Type tSub = ScTypes.Find("XNPCVoiceControl.UI.SubtitleManager");
                if (tChat == null || tSub == null) return;   // no Voice Control: no captions (a clip still plays)
                mShowsText = tChat.GetMethod("ShowsGenericTextFor", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(int) }, null);
                pSubInstance = tSub.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                // as ScBarks: ShowSubtitle(string, string, float or float?, optional ...), by name and arity (3.4.05 widened it)
                foreach (MethodInfo mi in tSub.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (mi.Name != "ShowSubtitle") continue;
                    ParameterInfo[] ps = mi.GetParameters();
                    if (ps.Length < 3 || ps[0].ParameterType != typeof(string) || ps[1].ParameterType != typeof(string)) continue;
                    Type dur = Nullable.GetUnderlyingType(ps[2].ParameterType) ?? ps[2].ParameterType;
                    if (dur != typeof(float)) continue;
                    bool rest = true;
                    for (int i = 3; i < ps.Length; i++) if (!ps[i].IsOptional) { rest = false; break; }
                    if (!rest) continue;
                    mShowSubtitle = mi;
                    break;
                }
                if (mShowsText == null || pSubInstance == null || mShowSubtitle == null)
                    ScLog.Warn("small talk: Voice Control's ShowsGenericTextFor / SubtitleManager.ShowSubtitle not found - small talk without a recording shows nothing");
            }
            catch (Exception e)
            {
                ScLog.Once("social-bind-vc", e);
            }
        }

        /// <summary>A caption can show for her: VoiceBarkSubtitles on, Voice Control's subtitle bar there, and its subtitle mode for her shows text.</summary>
        public static bool CanCaption(EntityAlive npc)
        {
            if (!ScConfig.VoiceBarkSubtitles || npc == null) return false;
            BindVc();
            if (mShowsText == null || pSubInstance == null || mShowSubtitle == null) return false;
            try { return (bool)mShowsText.Invoke(null, new object[] { npc.entityId }); }
            catch (Exception e)
            {
                ScLog.Once("social-showstext", e);
                return false;
            }
        }

        private static void Caption(EntityAlive npc, string name, string words, float seconds)
        {
            if (!CanCaption(npc) || string.IsNullOrEmpty(words)) return;
            try
            {
                object mgr = pSubInstance.GetValue(null, null);
                if (mgr == null) return;
                ParameterInfo[] ps = mShowSubtitle.GetParameters();
                object[] args = new object[ps.Length];
                args[0] = name ?? "";
                args[1] = words;
                args[2] = Mathf.Clamp(seconds, 2f, 15f);
                for (int i = 3; i < ps.Length; i++) args[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue : null;
                mShowSubtitle.Invoke(mgr, args);
            }
            catch (Exception e)
            {
                ScLog.Once("social-caption", e);
            }
        }

        // ------------------------------------------------------------------ console ----

        private static void Count(string what)
        {
            int n;
            counts.TryGetValue(what, out n);
            counts[what] = n + 1;
        }

        public static string Counters()
        {
            if (counts.Count == 0) return "lines: none said yet";
            var keys = new List<string>(counts.Keys);
            keys.Sort(StringComparer.Ordinal);
            var sb = new StringBuilder("lines: ");
            for (int i = 0; i < keys.Count; i++) sb.Append(i > 0 ? ", " : "").Append(keys[i]).Append('=').Append(counts[keys[i]]);
            return sb.ToString();
        }

        /// <summary>sc chat lines: how many of the small-talk lines her voices have recorded, and where a caption can show for her.</summary>
        public static string Coverage(EntityAlive npc)
        {
            List<ScVoice.Voice> chain = ScVoice.Chain(npc);
            if (chain == null || chain.Count == 0) return "no voice (not one of SC's survivors)";
            string[] p = ScSocialLines.Pairs;
            int total = 0, have = 0;
            for (int i = 0; i + 1 < p.Length; i += 2)
            {
                foreach (string key in new[] { OpenPrefix + p[i], ReplyPrefix + p[i] })
                {
                    total++;
                    string v;
                    if (ClipFor(npc, key, out v) != null) have++;
                }
            }
            return have + "/" + total + " lines recorded (own voice " + chain[0].Name + ")" + (CanCaption(npc) ? ", captions show" : ", NO caption (VoiceBarkSubtitles off, no Voice Control, or its subtitle mode)")
                   + (ScConfig.VoiceLines ? "" : "; VoiceLines is off");
        }
    }
}
#endif
