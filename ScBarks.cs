using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// V2: SC's voiced lines outside a talk ("barks"; research\voices\REPORT.md sections 2.2 and 4, SC_Ideas_and_Voices_Plan.md
    /// section 4). The lines and their words are ScVoiceLines (written by Tools/voice_lines.py); a key's family is the key
    /// without its trailing number (greet.member.3 -> greet.member, ack.setengage.1.2 -> ack.setengage.1, xn.ammo.dry is its
    /// own family). Every bark is clip-only: a line plays when one of the NPC's voices (ScVoice.Chain: their own, the other
    /// of their race and sex, then any recorded voice of their sex) has its clip, else nothing is said - except with
    /// VoiceBarkDebug, which shows the words and logs a line for a silent one, so the triggers can be tested before the
    /// recordings exist.
    ///
    /// Say: SC's own lines (greetings and chatter, ScGreetings; raider taunts, ScTaunts): a random clip of the family (not
    /// the one said last when there is another), its words in Voice Control's subtitle bar (VoiceBarkSubtitles; Voice
    /// Control's own rule for that NPC, ShowsGenericTextFor), played by ScVoice.PlayBark.
    ///
    /// The XNPCCore and Voice Control lines (ScBarks, Patches_VoiceControl's V2 prefixes): where Voice Control is about to
    /// speak a line SC has recorded in that survivor's voice, SC plays its clip through Voice Control's player instead (lip
    /// sync kept) and Voice Control's speech is skipped; with no clip Voice Control speaks as before.
    ///  - SubtitleManager.AcknowledgeCommand (say-lines, wheel and voice orders): a trigger's ActionType (and ActionParam)
    ///    names the family, ack.&lt;type&gt;[.&lt;param&gt;] - the weapon swap and the formations one generic family each - as Voice
    ///    Control speaks the order's own reply there (unless the words it was handed are one of its fixed lines, a leaderless
    ///    refusal or "Are you sure?": that line); with no trigger (a say-line, a fixed line) the words name the key
    ///    (xn.basekeep.need for any "I need &lt;item&gt; for that.").
    ///  - NPCAudioPlayer.SpeakStreaming (the chat path's acks, Voice Control's fixed lines): the words name the key; an ack
    ///    whose words are a named weapon or side takes the generic family by PhraseTriggerHandler.LastMatchedTriggerName.
    ///    Voice Control's onComplete is handed to the clip, so its state ends as after its own sentence.
    ///  - Never while Voice Control is saying something on that NPC (it would stop its own queue first, which SC never
    ///    does: research\voices\REPORT.md 1.5), never on the NPC the talk is open on (V1's gate runs first and wins).
    /// Round m1w (2026-11-17, research\voices_v9\VOICE_INVENTORY.md section 3.1):
    ///  - P1: a say-line's id (AcknowledgeCommand's lineId, Voice Control 3.4.03+) names its family first, xn.&lt;id&gt; - an
    ///    argument line's id without its ":&lt;argument&gt;" (book.recipe:forgedIron is xn.book.recipe) - so variants
    ///    (xn.gift.thanks.1-3) and lines with names or numbers in them map to SC's clips; a family SC has no lines for falls
    ///    back to the words. The caption of an argument line is Voice Control's (it names the item), else SC's words.
    ///  - P2: the order wheel's spoken slot labels are taken for the order's reply (ScVoiceRoutes.WheelKeys).
    ///  - P3: SC's own lines that Voice Control speaks for it (ScVoiceLines.Own: an order's answer, the training line) are
    ///    matched by their Localization text, an *emote* left out; a refusal override accepts them as it does vc.* lines.
    ///  - P5: words match by their letters and digits only (Norm): punctuation and dashes never decide.
    ///  - P6: one line per NPC at a time - after SC put a line on an NPC in Voice Control's place (a swap, or one of SC's
    ///    spoken refusals: SayOwn), the known second lines of that moment are swallowed (review of adee167, fix 1): a
    ///    trigger-less, id-less ack (the order wheel's label) and an ack's SpeakStreaming (a spoken order's reply), in the same
    ///    or the next frame and within VoiceOneLineSeconds; a SpeakStreaming swallowed runs its callback and shows SC's caption
    ///    again. Never a line with a say-line id (downed.down: always heard), a trigger's ack, the speech of an ack SC left to
    ///    Voice Control (passFrame), or anything on a downed survivor. Voice Control counts as busy only for its own speech,
    ///    not for SC's clip on its player (ScVoice.VcBusy).
    /// Main thread only. Host / single player (SC is single-player).
    /// </summary>
    public static class ScBarks
    {
        private static Dictionary<string, string> textOf;                       // key -> words
        private static Dictionary<string, List<string>> families;               // family -> keys, in order
        private static Dictionary<string, List<string>> byWords;                // normalised words -> keys
        /// <summary>Round m1w (P3): SC's own lines Voice Control speaks (ScVoiceLines.Own), keyed by their Localization key.</summary>
        private static HashSet<string> own;
        /// <summary>Round m1w (P6): SC's last line on an NPC in Voice Control's place - its frame, its time, and its caption.</summary>
        private struct Said
        {
            public int Frame;
            public float At;
            public string Name, Caption;
        }

        /// <summary>Round m1w (P6): SC's last line in Voice Control's place on each NPC, by entity id.</summary>
        private static readonly Dictionary<int, Said> saidAt = new Dictionary<int, Said>();
        private static readonly List<int> staleSaid = new List<int>();
        /// <summary>
        /// Review fix 1: the NPC and frame of an ack SC left to Voice Control - its own speech (AcknowledgeCommand's SpeakText,
        /// next) is never swallowed; taken by the first SpeakStreaming on that NPC.
        /// </summary>
        private static int passFrame = -1, passNpc = -1;
        private static readonly System.Random rnd = new System.Random();
        private static readonly Dictionary<string, string> lastKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);
        private static readonly List<string> scratch = new List<string>();
        private static readonly List<string> NoKeys = new List<string>();
        /// <summary>The frame and NPC of the last ack SubstituteAck left to Voice Control (its speech then passes SubstituteSpeech: counted once).</summary>
        private static int keptFrame = -1, keptNpc = -1;
        private static readonly Regex NonAlnum = new Regex("[^A-Za-z0-9]+");
        private static readonly Regex Emote = new Regex(@"\*[^*]+\*");

        // Voice Control, bound by name once (no compile reference)
        private static bool vcBound;
        private static MethodInfo mShowsText, mShowSubtitle, mFace, mLastMatched;
        private static PropertyInfo pSubInstance, pHandlerInstance, pLastTrigger, pActionType, pActionParam, pResponses;
        private static Type tChat;

        /// <summary>A line picked for an NPC: the key, its words, the clip (null: silent) and the voice that has it.</summary>
        public sealed class Pick
        {
            public string Key, Words, Path, Voice;
            public bool Fallback;          // not the NPC's own voice
        }

        public static void Reset()
        {
            lastKey.Clear();
            counts.Clear();
            // round m1w: the lines are built again for the new world (SC's own lines' words come from its Localization, P3)
            textOf = null;
            families = null;
            byWords = null;
            own = null;
            saidAt.Clear();
            ScVoiceRoutes.Reset();
        }

        /// <summary>ModEvents.GameUpdate: SC's greetings and chatter, the raiders' taunt tick and (round m1p) the clip cache's sweep (each paces itself).</summary>
        public static void Tick()
        {
            ScGreetings.Tick();
            ScTaunts.Tick();
            ScClipCache.Tick();     // round m1p: the clip cache's idle sweep and memory bound (works every 30 s)
        }

        // ------------------------------------------------------------------ the lines ----

        private static void Build()
        {
            if (textOf != null) return;
            var t = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var f = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var w = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            string[] lines = ScVoiceLines.Lines;
            for (int i = 0; i + 1 < lines.Length; i += 2)
            {
                string key = lines[i], words = lines[i + 1];
                if (t.ContainsKey(key)) continue;
                t[key] = words;
                string fam = FamilyOf(key);
                List<string> list;
                if (!f.TryGetValue(fam, out list)) f[fam] = list = new List<string>();
                list.Add(key);
                string n = Norm(words);
                if (!w.TryGetValue(n, out list)) w[n] = list = new List<string>();
                list.Add(key);
            }
            // round m1w (P3): SC's own lines that Voice Control speaks for it (an order's answer SC hands it, the training line),
            // keyed by their Localization key; the words are that row's text as Voice Control says it - an *emote* left out -
            // read now, so the text has one source
            var o = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (string key in ScVoiceLines.Own)
                {
                    if (string.IsNullOrEmpty(key) || t.ContainsKey(key)) continue;
                    string text = Localization.Get(key);
                    string words = Spoken(text);
                    if (string.IsNullOrEmpty(text) || text == key || words.Length == 0 || words.IndexOf('{') >= 0)
                    {
                        ScLog.Once("voice-own-" + key, "voice barks: SC's line " + key + " has no Localization row to match (or a token in it) - its recording never plays");
                        continue;
                    }
                    t[key] = words;
                    f[key] = new List<string> { key };
                    o.Add(key);
                    string n = Norm(words);
                    List<string> list;
                    if (!w.TryGetValue(n, out list)) w[n] = list = new List<string>();
                    list.Add(key);
                }
            }
            catch (Exception e)
            {
                ScLog.Once("voice-own-lines", e);         // Localization not readable: the V2 lines still match
            }
            own = o;
            families = f;
            byWords = w;
            textOf = t;
        }

        /// <summary>Round m1w (P3): a line's words as Voice Control speaks them - every *emote* left out, whitespace collapsed.</summary>
        private static string Spoken(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return Regex.Replace(Emote.Replace(text, ""), @"\s+", " ").Trim();
        }

        /// <summary>Round m1w (P3): one of SC's own lines (ScVoiceLines.Own)?</summary>
        internal static bool IsOwn(string key)
        {
            Build();
            return key != null && own != null && own.Contains(key);
        }

        /// <summary>A key's family: the key without a trailing ".&lt;number&gt;" (greet.member.3 -> greet.member), else the key itself.</summary>
        public static string FamilyOf(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            int dot = key.LastIndexOf('.');
            if (dot <= 0 || dot == key.Length - 1) return key;
            for (int i = dot + 1; i < key.Length; i++) if (key[i] < '0' || key[i] > '9') return key;
            return key.Substring(0, dot);
        }

        /// <summary>The words a key says (the caption), or null.</summary>
        public static string WordsOf(string key)
        {
            Build();
            string s;
            return key != null && textOf.TryGetValue(key, out s) ? s : null;
        }

        /// <summary>The keys of a family (empty when none).</summary>
        public static List<string> KeysOf(string family)
        {
            Build();
            List<string> list;
            return family != null && families.TryGetValue(family, out list) ? list : NoKeys;
        }

        /// <summary>
        /// The keys that say these words (Voice Control's English; case, whitespace, punctuation and any *emote* aside), or
        /// none. Any "I need X for that." that is no fixed line is 0-XNPCCore's basekeep.need, said with its stand-in (round
        /// m1w: "I need fuel for that." is a fixed line now, xn.craft.fuel, so it is no longer taken for the stand-in).
        /// </summary>
        public static List<string> KeysForWords(string words)
        {
            Build();
            if (string.IsNullOrEmpty(words)) return NoKeys;
            string n = Norm(Emote.Replace(words, ""));
            if (n.Length == 0) return NoKeys;
            List<string> list;
            if (byWords.TryGetValue(n, out list)) return list;
            if (n.StartsWith("i need ", StringComparison.Ordinal) && n.EndsWith(" for that", StringComparison.Ordinal)) return KeysOf("xn.basekeep.need");
            return NoKeys;
        }

        /// <summary>
        /// Words as they are matched: letters and digits only, lowercased, one space between words. Round m1w (P5): punctuation,
        /// dashes and apostrophes are left out where they stand, so Voice Control 3.4.06's "I'm in your way - point past me."
        /// is SC's recorded "I'm in your way, point past me." (before, only case and whitespace were ignored).
        /// </summary>
        private static string Norm(string s)
        {
            if (s == null) return "";
            var sb = new StringBuilder(s.Length);
            bool space = false;
            foreach (char c in s)
            {
                if (char.IsWhiteSpace(c)) { space = true; continue; }
                if (!char.IsLetterOrDigit(c)) continue;
                if (space && sb.Length > 0) sb.Append(' ');
                space = false;
                sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Round m1w (P1): a say-line id's family, xn.&lt;id&gt; - an argument line's (book.recipe:forgedIron) without its
        /// argument, argLine then true; null without an id.
        /// </summary>
        private static string LineFamily(string lineId, out bool argLine)
        {
            argLine = false;
            if (string.IsNullOrEmpty(lineId)) return null;
            int colon = lineId.IndexOf(':');
            if (colon >= 0)
            {
                argLine = true;
                lineId = lineId.Substring(0, colon);
            }
            return lineId.Length > 0 ? "xn." + lineId : null;
        }

        // ------------------------------------------------------------------ picking ----

        /// <summary>
        /// A line of these keys in this NPC's voices: the first voice of their chain with a clip of any of them, a random one
        /// of those (not the family's last when there is another); with none recorded, a silent pick in their own voice
        /// (Path null). Null: not one of SC's survivors or raiders, or no keys.
        /// </summary>
        public static Pick Resolve(EntityAlive npc, IList<string> keys)
        {
            if (npc == null || keys == null || keys.Count == 0) return null;
            List<ScVoice.Voice> chain = ScVoice.Chain(npc);
            if (chain == null) return null;
            ScVoice.Voice v = ScVoice.FirstWith(chain, keys, scratch);
            string fam = FamilyOf(keys[0]);
            if (v == null)
            {
                string k = RandomOf(keys, fam);
                return new Pick { Key = k, Words = WordsOf(k), Voice = chain.Count > 0 ? chain[0].Name : null };
            }
            string key = RandomOf(scratch, fam);
            string path;
            ScVoice.HasClip(v, key, out path);
            return new Pick { Key = key, Words = WordsOf(key), Path = path, Voice = v.Name, Fallback = chain.Count > 0 && v != chain[0] };
        }

        private static string RandomOf(IList<string> keys, string family)
        {
            if (keys.Count == 1) return keys[0];
            string last;
            lastKey.TryGetValue(family, out last);
            for (int tries = 0; tries < 4; tries++)
            {
                string k = keys[rnd.Next(keys.Count)];
                if (!string.Equals(k, last, StringComparison.OrdinalIgnoreCase)) return k;
            }
            return keys[rnd.Next(keys.Count)];
        }

        /// <summary>Does one of this NPC's voices have a clip of this family?</summary>
        public static bool HasClip(EntityAlive npc, string family)
        {
            return HasAnyClip(npc, KeysOf(family));
        }

        /// <summary>Round m1w: does one of this NPC's voices have a clip of any of these keys (some of a family's: ScGreetings.GreetKeys)?</summary>
        internal static bool HasAnyClip(EntityAlive npc, IList<string> keys)
        {
            if (keys == null || keys.Count == 0) return false;
            return ScVoice.FirstWith(ScVoice.Chain(npc), keys, scratch) != null;
        }

        // ------------------------------------------------------------------ saying ----

        /// <summary>
        /// SC's own bark (a greeting, chatter, a taunt): a line of the family on this NPC. True when something was said - a
        /// clip started loading, or (VoiceBarkDebug) a silent line was shown and logged; false when the NPC has no clip of the
        /// family and debug is off (then nothing happens). lipSync: through Voice Control's player when it has one (survivors);
        /// allowDead: a dying raider's last words; faceTo: Voice Control turns the survivor to them for a few seconds.
        /// </summary>
        public static bool Say(EntityAlive npc, string family, string why, bool lipSync, bool allowDead, Entity faceTo)
        {
            return SayFrom(npc, family, KeysOf(family), why, lipSync, allowDead, faceTo);
        }

        /// <summary>
        /// Round m1w: Say from some of a family's keys only (a member away from home greets with the member lines that fit
        /// anywhere: ScGreetings.GreetKeys). family: for the counters and the family's last line. No keys: nothing is said.
        /// </summary>
        internal static bool SayFrom(EntityAlive npc, string family, IList<string> keys, string why, bool lipSync, bool allowDead, Entity faceTo)
        {
#if SC_V34
            // Round n06: no talking in her sleep. Every family that reaches here is speech - chatter, a greeting, a
            // taunt - and none of them belongs in a bed. 0-XNPCCore wakes her for a real reason (an enemy inside
            // RoutineWakeEnemyMetres, or the player starting a talk), and once awake she is no longer gated here.
            if (ScSleep.IsAsleep(npc)) { Count("asleep " + family); return false; }
            // round U2: not a word while she is down (0-XNPCCore's own pained line is its own; SC adds none)
            if (ScDowned.IsDown(npc)) { Count("down " + family); return false; }
#endif
            Pick p = Resolve(npc, keys);
            if (p == null) return false;
            string who = ScCommunity.DisplayName(npc) + " (" + npc.entityId + ")";
            if (p.Path == null)
            {
                Count("silent " + family);
                if (!ScConfig.VoiceBarkDebug) return false;
                lastKey[family] = p.Key;
                ScLog.Out("voice bark (silent, no recording): " + who + " " + why + ": " + p.Key + " in " + (p.Voice ?? "no voice") + ": \"" + p.Words + "\"");
                Caption(npc, ScCommunity.DisplayName(npc), p.Words, 2f + 0.06f * (p.Words ?? "").Length);
                if (faceTo != null) Face(npc, faceTo);
                return true;
            }
            lastKey[family] = p.Key;
            Count("said " + family);
            string words = p.Words;
            if (ScConfig.Verbose || ScConfig.VoiceBarkDebug)
                ScLog.Out("voice bark: " + who + " " + why + ": " + p.Key + " in " + p.Voice + (p.Fallback ? " (borrowed: their own voice has none)" : ""));
            if (faceTo != null) Face(npc, faceTo);
            string name = ScCommunity.DisplayName(npc);
            ScVoice.PlayBark(npc, p.Path, lipSync, allowDead, clip => Caption(npc, name, words, clip.length + 1f), null);
            return true;
        }

        /// <summary>
        /// Round m1w (P7, ScVoiceRoutes.SayRefusal): one of SC's own lines (a refusal) said now in this NPC's recorded voice,
        /// through Voice Control's player (lip sync), with its words as the caption; she turns to faceTo. True when a clip
        /// started loading - the one-line rule (P6) then swallows the Voice Control reply of the same order. False with no
        /// recording in any of her voices: nothing is said (VoiceBarkDebug logs it).
        /// </summary>
        internal static bool SayOwn(EntityAlive npc, string key, string why, Entity faceTo)
        {
            Pick p = Resolve(npc, KeysOf(key));
            if (p == null) return false;
            string who = ScCommunity.DisplayName(npc) + " (" + npc.entityId + ")";
            if (p.Path == null)
            {
                Count("refusal not recorded " + key);
                if (ScConfig.VoiceBarkDebug)
                    ScLog.Out("voice refusal (silent, not recorded yet): " + who + " " + why + ": " + key + " in " + (p.Voice ?? "no voice") + ": \"" + p.Words + "\"");
                return false;
            }
            Count("refusal said " + key);
            if (ScConfig.Verbose || ScConfig.VoiceBarkDebug)
                ScLog.Out("voice refusal: " + who + " " + why + ": " + p.Key + " in " + p.Voice + (p.Fallback ? " (borrowed: their own voice has none)" : ""));
            if (faceTo != null) Face(npc, faceTo);
            string name = ScCommunity.DisplayName(npc), words = p.Words;
            ScVoice.PlayBark(npc, p.Path, true, false, clip => Caption(npc, name, words, clip.length + 1f), null);
            lastKey[FamilyOf(p.Key)] = p.Key;
            MarkSaid(npc, name, ScConfig.VoiceBarkSubtitles ? words : null);
            return true;
        }

        /// <summary>
        /// Round m1w (P6): SC just put one of its lines on this NPC in Voice Control's place; caption: the words it showed (null:
        /// none), shown again when the second line of the moment is swallowed after Voice Control showed its own.
        /// </summary>
        private static void MarkSaid(EntityAlive npc, string name, string caption)
        {
            if (saidAt.Count > 64)
            {
                // review fix 8: only the marks older than last frame go
                int f = Time.frameCount;
                staleSaid.Clear();
                foreach (KeyValuePair<int, Said> kv in saidAt) if (f - kv.Value.Frame > 1) staleSaid.Add(kv.Key);
                foreach (int id in staleSaid) saidAt.Remove(id);
            }
            saidAt[npc.entityId] = new Said { Frame = Time.frameCount, At = Time.unscaledTime, Name = name, Caption = caption };
        }

        /// <summary>
        /// Round m1w (P6): one line per NPC at a time. True: SC put a line on this NPC in this frame or the last, within
        /// VoiceOneLineSeconds, so this Voice Control line - one of the known second lines of the same moment, as the callers
        /// pick them ("Come here" after "On my way.", a spoken gift's reply after "Thank you.") - is swallowed instead of
        /// cutting SC's clip. Never on a downed survivor (review fix 5). said: SC's line. sink and text: the counters, the log.
        /// </summary>
        private static bool OneLine(EntityAlive npc, string sink, string text, out Said said)
        {
            float window = ScConfig.VoiceOneLineSeconds;
            if (!saidAt.TryGetValue(npc.entityId, out said) || window <= 0f) return false;
            if (Time.frameCount - said.Frame > 1 || Time.unscaledTime - said.At >= window) return false;
#if SC_V34
            if (ScDowned.IsDown(npc)) return false;
#endif
            Count("vc one-line " + sink);
            ScLog.Verbose("voice barks: " + ScCommunity.DisplayName(npc) + " (" + npc.entityId + ") \"" + text + "\" at " + sink
                          + ": swallowed - SC's line of a moment ago stands (one line at a time)");
            return true;
        }

        /// <summary>Counters for sc voice barks.</summary>
        public static void Count(string what)
        {
            int n;
            counts.TryGetValue(what, out n);
            counts[what] = n + 1;
        }

        public static string Counters()
        {
            if (counts.Count == 0) return "nothing said or matched yet";
            var keys = new List<string>(counts.Keys);
            keys.Sort(StringComparer.Ordinal);
            var sb = new StringBuilder();
            foreach (string k in keys) sb.Append(sb.Length > 0 ? ", " : "").Append(k).Append('=').Append(counts[k]);
            return sb.ToString();
        }

        // ------------------------------------------------------------------ Voice Control's lines in SC's voices ----

        /// <summary>
        /// SubtitleManager.AcknowledgeCommand's prefix (Patches_VoiceControl): true = SC plays its clip and shows the caption,
        /// Voice Control's ack is skipped; false = Voice Control speaks as before. Round m1w: lineId is the say-line's id
        /// (Voice Control 3.4.03+; null before it and for anything that is no say-line) - P1; true also when the line is
        /// swallowed: the second line of a moment (P6), a wheel label that is no reply (P2).
        /// </summary>
        public static bool SubstituteAck(EntityAlive npc, object trigger, string fallbackText, string localizedSubtitle, string lineId)
        {
            if (!ScConfig.ScBarks || npc == null || !ScXnpc.IsSurvivorBody(npc) || ScVoice.Holds(npc)) return false;
            ScVoiceRoutes.NoteLine(npc, lineId);                    // review fix 5: comehere.ack said since the order's note
            if (TakeAck(npc, trigger, fallbackText, localizedSubtitle, lineId)) return true;
            // review fix 1: Voice Control says this one - its own speech, next, is never taken for a second line
            passFrame = Time.frameCount;
            passNpc = npc.entityId;
            return false;
        }

        private static bool TakeAck(EntityAlive npc, object trigger, string fallbackText, string localizedSubtitle, string lineId)
        {
            // round m1w (P6), review fix 1: of the acks only the order wheel's label - trigger-less, id-less - is a second line;
            // a say-line (downed.down) and a trigger's reply are never swallowed
            Said prior;
            if (trigger == null && string.IsNullOrEmpty(lineId) && OneLine(npc, "ack", fallbackText, out prior)) return true;
            List<string> keys = NoKeys;
            string what = null;
            bool argLine = false, wheel = false;
            if (trigger != null)
            {
                string type, param;
                if (!TriggerOf(trigger, out type, out param)) return false;
                string fam = IsGeneric(type) ? "ack." + type : "ack." + type + (string.IsNullOrEmpty(param) ? "" : "." + NonAlnum.Replace(param, "_"));
                keys = KeysOf(fam);
                what = "ack " + fam;
                // Voice Control speaks the order's own reply even when the order was refused; where the words it was handed
                // are one of its fixed lines (a refusal, "Are you sure?") or (round m1w, P3) one of SC's own answers ("Heading
                // home."), SC says those instead
                List<string> said = KeysForWords(fallbackText);
                foreach (string k in said)
                {
                    if (!k.StartsWith("vc.", StringComparison.Ordinal) && !IsOwn(k)) continue;
                    keys = said;
                    what = "ack " + fam + " answered \"" + fallbackText + "\"";
                    break;
                }
            }
            else
            {
                // round m1w (P1): the say-line's id names the family; one SC has no lines for falls back to the words
                string fam = LineFamily(lineId, out argLine);
                if (fam != null)
                {
                    keys = KeysOf(fam);
                    what = "line " + lineId;
                }
                // round m1w (P2): no id - the order wheel's spoken slot label is decided before the words (review fix 7: "Loser"
                // is the Emotes page's label, not ack.emote.loser.1's words; VoiceWheelAcks and a refusal are honored)
                if (keys.Count == 0 && string.IsNullOrEmpty(lineId))
                {
                    List<string> wk;
                    string wfam;
                    ScVoiceRoutes.Wheel w = ScVoiceRoutes.WheelKeys(npc, fallbackText, out wk, out wfam);
                    if (w == ScVoiceRoutes.Wheel.Silence)
                    {
                        Count("vc wheel silent");
                        ScLog.Verbose("voice barks: " + ScCommunity.DisplayName(npc) + " (" + npc.entityId + ") wheel label \"" + fallbackText
                                      + "\": swallowed - no reply of its own (its order's line already answered, or a gesture)");
                        return true;
                    }
                    if (w == ScVoiceRoutes.Wheel.Leave)
                    {
                        Count("vc wheel left");
                        return false;
                    }
                    if (w == ScVoiceRoutes.Wheel.Reply)
                    {
                        keys = wk;
                        wheel = true;
                        what = "wheel label \"" + fallbackText + "\" as " + wfam;
                    }
                }
                if (keys.Count == 0)
                {
                    argLine = false;
                    keys = KeysForWords(fallbackText);
                    what = "line \"" + fallbackText + "\"";
                }
            }
            if (keys.Count == 0) return false;
            Pick p = Resolve(npc, keys);
            if (p == null || p.Path == null)
            {
                Count("vc kept " + Group(keys[0]));
                keptFrame = Time.frameCount;               // its SpeakStreaming, this frame, is not counted again
                keptNpc = npc.entityId;
                ScLog.Verbose("voice barks: " + ScCommunity.DisplayName(npc) + " (" + npc.entityId + ") " + what + " -> " + keys[0]
                              + (keys.Count > 1 ? " (+" + (keys.Count - 1) + ")" : "") + ": no recording - Voice Control says it");
                return false;
            }
            if (ScVoice.VcBusy(npc))
            {
                Count("vc busy " + Group(p.Key));
                return false;
            }
            // the clip first: a Voice Control callback still waiting on this NPC (it may clear the subtitle) runs inside
            ScVoice.PlayBark(npc, p.Path, true, false, null, null);
            string caption = AckCaption(trigger != null || wheel, argLine, p.Words, fallbackText, localizedSubtitle);
            bool shown = ShowsText(npc);
            if (shown) Subtitle(npc.EntityName, caption, 8f);
            lastKey[FamilyOf(p.Key)] = p.Key;
            MarkSaid(npc, npc.EntityName, shown ? caption : null);  // round m1w (P6)
            Count("vc swapped " + Group(p.Key));
            ScLog.Verbose("voice barks: " + ScCommunity.DisplayName(npc) + " (" + npc.entityId + ") " + what + ": SC's " + p.Key + " in " + p.Voice
                          + " instead of Voice Control's speech");
            return true;
        }

        /// <summary>
        /// Round m1w: the caption of an ack SC says. SC's words for an order's reply (a trigger's or a wheel label's: other words
        /// than Voice Control's); Voice Control's localized subtitle for an argument line (P1: it names the item or the station
        /// SC's clip leaves out) and for a line SC says in Voice Control's own words (its translation); SC's words - the words
        /// the clip says, its .txt sidecar's - otherwise: every id-keyed variant whose words are not Voice Control's ("Thank
        /// you. I won't forget this.", "Thanks. I'll put it to good use.", "I'm hit! I can't get up!").
        /// </summary>
        private static string AckCaption(bool reply, bool argLine, string words, string fallbackText, string localizedSubtitle)
        {
            if (reply || string.IsNullOrWhiteSpace(localizedSubtitle)) return words;
            if (argLine) return localizedSubtitle;
            return Norm(words) == Norm(Emote.Replace(fallbackText ?? "", "")) ? localizedSubtitle : words;
        }

        /// <summary>
        /// NPCAudioPlayer.SpeakStreaming's prefix (Patches_VoiceControl): true = SC plays its clip on that player with Voice
        /// Control's onComplete, its speech is skipped; false = Voice Control speaks as before. Round m1w (P6): true also for
        /// the second line of a moment - an ack's speech (a spoken order's reply) in the same or the next frame as SC's line,
        /// never the speech of an ack SC left to Voice Control (review fix 1) - swallowed with its onComplete run at once and
        /// SC's caption shown again; and (review fix 9) for a spoken order's reply right after SC refused that order.
        /// </summary>
        public static bool SubstituteSpeech(EntityAlive npc, string text, Action onComplete, bool isAck)
        {
            if (!ScConfig.ScBarks || npc == null || string.IsNullOrWhiteSpace(text) || !ScXnpc.IsSurvivorBody(npc) || ScVoice.Holds(npc)) return false;
            bool passed = passNpc == npc.entityId && passFrame == Time.frameCount;     // the speech of an ack Voice Control says
            if (passed) passNpc = -1;
            Said said;
            if (isAck && !passed && OneLine(npc, "speech", text, out said))
            {
                Swallowed(npc, onComplete);
                // Voice Control's caller showed its own words before it spoke and its callback just released them: SC's line's again
                if (said.Caption != null && ShowsText(npc)) Subtitle(said.Name, said.Caption, 6f);
                return true;
            }
            if (isAck && !passed && ScVoiceRoutes.Refused(npc))
            {
                // review fix 9: SC refused this order a moment ago (a resident told to loot here, the work kit as a weapon) -
                // Voice Control's reply ("I'll clear this place out.") would say yes; the tooltip has said why not
                Count("vc refused reply");
                ScLog.Verbose("voice barks: " + ScCommunity.DisplayName(npc) + " (" + npc.entityId + ") \"" + text + "\": swallowed - SC refused that order");
                Swallowed(npc, onComplete);
                return true;
            }
            List<string> keys = KeysForWords(text);
            bool generic = false;
            if (keys.Count == 0)
            {
                if (!isAck) return false;
                // a weapon or side named in the words: the generic family, but only for one of that order's own replies
                // (a refusal or an action's own answer in other words stays Voice Control's)
                string last = LastTrigger();
                if (!IsGeneric(last) || !IsLastTriggersReply(text)) return false;
                keys = KeysOf("ack." + last);
                generic = true;
                if (keys.Count == 0) return false;
            }
            Pick p = Resolve(npc, keys);
            if (p == null || p.Path == null)
            {
                if (keptFrame != Time.frameCount || keptNpc != npc.entityId) Count("vc kept " + Group(keys[0]));
                return false;
            }
            if (ScVoice.VcBusy(npc))
            {
                Count("vc busy " + Group(p.Key));
                return false;
            }
            // Voice Control's onComplete goes with the clip and runs exactly once (ScVoice.PlayBark, the V2 review's M1)
            ScVoice.PlayBark(npc, p.Path, true, false, null, onComplete);
            // the caller showed its own words already; a generic ack says other words
            bool shown = ShowsText(npc);
            if (generic && shown) Subtitle(npc.EntityName, p.Words, 8f);
            lastKey[FamilyOf(p.Key)] = p.Key;
            MarkSaid(npc, npc.EntityName, shown ? (generic ? p.Words : text) : null);    // round m1w (P6)
            Count("vc swapped " + Group(p.Key));
            ScLog.Verbose("voice barks: " + ScCommunity.DisplayName(npc) + " (" + npc.entityId + ") \"" + text + "\": SC's " + p.Key + " in " + p.Voice
                          + " instead of Voice Control's speech");
            return true;
        }

        /// <summary>Round m1w: a SpeakStreaming SC swallowed - its callback runs now, so Voice Control's state moves on as after its own sentence (V1's gate does the same).</summary>
        private static void Swallowed(EntityAlive npc, Action onComplete)
        {
            try { if (onComplete != null) onComplete(); }
            catch (Exception e) { ScLog.Once("voice-barks-oneline-callback", e); }
        }

        /// <summary>The ack types whose replies name a weapon or a side: one generic family each.</summary>
        private static bool IsGeneric(string type)
        {
            return type == "swapweapon" || type == "setformation";
        }

        private static string Group(string key)
        {
            if (key != null && own != null && own.Contains(key)) return "own";     // round m1w (P3)
            int dot = key != null ? key.IndexOf('.') : -1;
            return dot > 0 ? key.Substring(0, dot) : key ?? "";
        }

        // ------------------------------------------------------------------ Voice Control, by name ----

        private static void BindVc()
        {
            if (vcBound) return;
            vcBound = true;
            try
            {
                tChat = Patches_VoiceControl.ChatType;
                Type tSub = ScTypes.Find("XNPCVoiceControl.UI.SubtitleManager");
                Type tHandler = Patches_VoiceControl.TriggerType;
                Type tTrigger = ScTypes.Find("XNPCVoiceControl.PhraseTrigger");
                if (tChat == null) return;          // no Voice Control: no captions, no substitution
                mShowsText = tChat.GetMethod("ShowsGenericTextFor", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(int) }, null);
                mFace = tChat.GetMethod("FaceEntity", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(Entity), typeof(float) }, null);
                pSubInstance = tSub != null ? tSub.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static) : null;
                // 3.4.05 ("subtitles stay until she stops speaking") widened this to
                // ShowSubtitle(string, string, float?, NPCAudioPlayer). An exact-type lookup for the
                // old (string, string, float) returns null against it, which switched SC's captions
                // off. Bound by name and arity instead, so both shapes work and a further trailing
                // parameter would too, as long as it is optional (Subtitle passes its default).
                mShowSubtitle = null;
                if (tSub != null)
                {
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
                }
                pHandlerInstance = tHandler != null ? tHandler.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static) : null;
                pLastTrigger = tHandler != null ? tHandler.GetProperty("LastMatchedTriggerName", BindingFlags.Public | BindingFlags.Instance) : null;
                pActionType = tTrigger != null ? tTrigger.GetProperty("ActionType", BindingFlags.Public | BindingFlags.Instance) : null;
                pActionParam = tTrigger != null ? tTrigger.GetProperty("ActionParam", BindingFlags.Public | BindingFlags.Instance) : null;
                pResponses = tTrigger != null ? tTrigger.GetProperty("Responses", BindingFlags.Public | BindingFlags.Instance) : null;
                mLastMatched = tHandler != null ? tHandler.GetMethod("GetLastMatchedTrigger", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null) : null;
                var missing = new List<string>();
                if (mShowsText == null) missing.Add("NPCChatComponent.ShowsGenericTextFor(int)");
                if (mFace == null) missing.Add("NPCChatComponent.FaceEntity(Entity, float)");
                if (pSubInstance == null || mShowSubtitle == null) missing.Add("SubtitleManager.Instance.ShowSubtitle(string, string, float[?], ...)");
                if (pHandlerInstance == null || pLastTrigger == null) missing.Add("PhraseTriggerHandler.Instance.LastMatchedTriggerName");
                if (pActionType == null || pActionParam == null) missing.Add("PhraseTrigger.ActionType / ActionParam");
                if (pResponses == null || mLastMatched == null) missing.Add("PhraseTriggerHandler.GetLastMatchedTrigger() / PhraseTrigger.Responses");
                if (missing.Count > 0)
                    ScLog.Warn("voice barks: Voice Control has changed (" + string.Join(", ", missing.ToArray()) + " not found) - SC's captions, facing or ack "
                               + "matching are off where they need it");
            }
            catch (Exception e)
            {
                ScLog.Once("voice-barks-bind", e);
            }
        }

        /// <summary>A trigger's ActionType (lowercased, as Voice Control names its clip folders) and ActionParam.</summary>
        private static bool TriggerOf(object trigger, out string type, out string param)
        {
            type = null;
            param = null;
            BindVc();
            if (trigger == null || pActionType == null) return false;
            try
            {
                object t = pActionType.GetValue(trigger, null);
                type = t != null ? t.ToString().ToLowerInvariant() : null;
                param = pActionParam != null ? pActionParam.GetValue(trigger, null) as string : null;
                return !string.IsNullOrEmpty(type);
            }
            catch (Exception e)
            {
                ScLog.Once("voice-barks-trigger", e);
                return false;
            }
        }

        /// <summary>PhraseTriggerHandler.Instance.LastMatchedTriggerName (the last order's ActionType lowercased), or null.</summary>
        private static string LastTrigger()
        {
            BindVc();
            if (pHandlerInstance == null || pLastTrigger == null) return null;
            try
            {
                object h = pHandlerInstance.GetValue(null, null);
                return h != null ? pLastTrigger.GetValue(h, null) as string : null;
            }
            catch (Exception e)
            {
                ScLog.Once("voice-barks-lasttrigger", e);
                return null;
            }
        }

        /// <summary>The words are one of the last matched order's own replies (PhraseTriggerHandler.GetLastMatchedTrigger().Responses, every language).</summary>
        private static bool IsLastTriggersReply(string text)
        {
            BindVc();
            if (pHandlerInstance == null || mLastMatched == null || pResponses == null) return false;
            try
            {
                object h = pHandlerInstance.GetValue(null, null);
                object trigger = h != null ? mLastMatched.Invoke(h, null) : null;
                var replies = trigger != null ? pResponses.GetValue(trigger, null) as System.Collections.Generic.IEnumerable<string> : null;
                if (replies == null) return false;
                string n = Norm(text);
                foreach (string r in replies) if (r != null && Norm(Emote.Replace(r, "")) == n) return true;   // as ExecuteMatch strips emotes
            }
            catch (Exception e)
            {
                ScLog.Once("voice-barks-replies", e);
            }
            return false;
        }

        /// <summary>Voice Control's subtitle rule for this NPC (its personality's subtitle mode; true without a chat component).</summary>
        private static bool ShowsText(EntityAlive npc)
        {
            BindVc();
            if (mShowsText == null) return false;
            try { return (bool)mShowsText.Invoke(null, new object[] { npc.entityId }); }
            catch (Exception e)
            {
                ScLog.Once("voice-barks-showstext", e);
                return false;
            }
        }

        private static void Subtitle(string name, string text, float seconds)
        {
            BindVc();
            if (pSubInstance == null || mShowSubtitle == null || string.IsNullOrEmpty(text)) return;
            try
            {
                object mgr = pSubInstance.GetValue(null, null);
                if (mgr != null)
                {
                    // Fill whatever optional parameters this build added (3.4.05's speaker) with the parameter's OWN
                    // default rather than Type.Missing: Mono's reflection has been less reliable than CoreCLR about
                    // Type.Missing through the two-arg Invoke, and a throw here lands in the catch below - which is
                    // exactly the silent caption loss this binding exists to fix. The duration needs no special case:
                    // a non-null float? boxes to a plain float, so the one boxed value suits both signatures.
                    ParameterInfo[] ps = mShowSubtitle.GetParameters();
                    object[] args = new object[ps.Length];
                    args[0] = name ?? "";
                    args[1] = text;
                    float secs = Mathf.Clamp(seconds, 2f, 25f);
                    args[2] = secs;
                    for (int i = 3; i < ps.Length; i++)
                        args[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue : null;
                    mShowSubtitle.Invoke(mgr, args);
                }
            }
            catch (Exception e)
            {
                ScLog.Once("voice-barks-subtitle", e);
            }
        }

        /// <summary>SC's own bark's caption: Voice Control's subtitle bar, under its rule for that NPC (VoiceBarkSubtitles).</summary>
        private static void Caption(EntityAlive npc, string name, string words, float seconds)
        {
            if (!ScConfig.VoiceBarkSubtitles || npc == null || string.IsNullOrEmpty(words)) return;
            if (ShowsText(npc)) Subtitle(name, words, seconds);
        }

        /// <summary>Turns a survivor to someone for a few seconds: Voice Control's FaceEntity where it has a chat component, else a look at them.</summary>
        private static void Face(EntityAlive npc, Entity target)
        {
            try
            {
                if (npc == null || target == null || npc.IsDead()) return;
                BindVc();
                Component chat = tChat != null && npc.gameObject != null ? npc.gameObject.GetComponent(tChat) : null;
                if (chat != null && mFace != null)
                {
                    mFace.Invoke(chat, new object[] { target, 4f });
                    return;
                }
                npc.SetLookPosition(target.getHeadPosition());
            }
            catch (Exception e)
            {
                ScLog.Once("voice-barks-face", e);
            }
        }

        // ------------------------------------------------------------------ console ----

        /// <summary>sc voice who: the bark families this NPC can say, and in which voice.</summary>
        internal static string FamilyReport(EntityAlive npc, List<ScVoice.Voice> chain, bool raider)
        {
            Build();
            string[] fams = raider
                ? new[] { "taunt.spot", "taunt.search", "taunt.lost", "taunt.hit", "taunt.killed", "taunt.idle", "taunt.dying", "taunt.retreat" }
                : new[] { "greet.stranger", "greet.recruit", "greet.member", "greet.follower", "greet.enclave", "chatter" };
            var sb = new StringBuilder(raider ? "taunts: " : "greetings and chatter: ");
            for (int i = 0; i < fams.Length; i++)
            {
                List<string> keys = KeysOf(fams[i]);
                ScVoice.Voice v = ScVoice.FirstWith(chain, keys, scratch);
                sb.Append(i > 0 ? ", " : "").Append(fams[i]).Append(' ').Append(v != null ? scratch.Count + "/" + keys.Count + " in " + v.Name : "silent");
            }
            if (!raider)
            {
                foreach (string g in new[] { "xn", "ack", "vc" })
                {
                    int total = 0, have = 0;
                    foreach (KeyValuePair<string, List<string>> kv in families)
                    {
                        if (!kv.Key.StartsWith(g + ".", StringComparison.Ordinal)) continue;
                        total += kv.Value.Count;
                        if (ScVoice.FirstWith(chain, kv.Value, scratch) != null) have += scratch.Count;
                    }
                    sb.Append("; ").Append(g).Append(' ').Append(have).Append('/').Append(total);
                }
                // round m1w (P3): SC's own lines Voice Control speaks
                int ownHave = 0;
                if (own != null)
                    foreach (string k in own)
                        if (ScVoice.FirstWith(chain, KeysOf(k), scratch) != null) ownHave++;
                sb.Append("; own ").Append(ownHave).Append('/').Append(own != null ? own.Count : 0);
            }
            return sb.ToString();
        }

        /// <summary>sc voice line &lt;words&gt;: the key Voice Control's words map to, and whether this NPC has its clip.</summary>
        public static string Match(EntityAlive npc, string words)
        {
            List<string> keys = KeysForWords(words);
            if (keys.Count == 0) return "\"" + words + "\" is none of SC's lines - Voice Control would say it itself";
            string s = "\"" + words + "\" is " + string.Join(", ", keys.ToArray());
            if (npc == null) return s;
            Pick p = Resolve(npc, keys);
            return s + "; " + ScCommunity.DisplayName(npc) + ": " + (p == null ? "no voice" : p.Path != null ? p.Key + " in " + p.Voice : "no recording (Voice Control says it)");
        }
    }
}
