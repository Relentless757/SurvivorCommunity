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
    /// Main thread only. Host / single player (SC is single-player).
    /// </summary>
    public static class ScBarks
    {
        private static Dictionary<string, string> textOf;                       // key -> words
        private static Dictionary<string, List<string>> families;               // family -> keys, in order
        private static Dictionary<string, List<string>> byWords;                // normalised words -> keys
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
        }

        /// <summary>ModEvents.GameUpdate: SC's greetings and chatter, and the raiders' taunt tick (each paces itself).</summary>
        public static void Tick()
        {
            ScGreetings.Tick();
            ScTaunts.Tick();
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
            families = f;
            byWords = w;
            textOf = t;
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
        /// The keys that say these words (Voice Control's English, whitespace and case aside), or none. Any "I need X for
        /// that." that is no fixed line is 0-XNPCCore's basekeep.need, said with its stand-in.
        /// </summary>
        public static List<string> KeysForWords(string words)
        {
            Build();
            if (string.IsNullOrEmpty(words)) return NoKeys;
            string n = Norm(words);
            List<string> list;
            if (byWords.TryGetValue(n, out list)) return list;
            if (n.StartsWith("i need ", StringComparison.Ordinal) && n.EndsWith(" for that.", StringComparison.Ordinal)) return KeysOf("xn.basekeep.need");
            return NoKeys;
        }

        private static string Norm(string s)
        {
            if (s == null) return "";
            var sb = new StringBuilder(s.Length);
            bool space = false;
            foreach (char c in s.Trim())
            {
                if (char.IsWhiteSpace(c)) { space = true; continue; }
                if (space && sb.Length > 0) sb.Append(' ');
                space = false;
                sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
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
            List<string> keys = KeysOf(family);
            if (keys.Count == 0) return false;
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
            Pick p = Resolve(npc, KeysOf(family));
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
        /// Voice Control's ack is skipped; false = Voice Control speaks as before.
        /// </summary>
        public static bool SubstituteAck(EntityAlive npc, object trigger, string fallbackText, string localizedSubtitle)
        {
            if (!ScConfig.ScBarks || npc == null || !ScXnpc.IsSurvivorBody(npc) || ScVoice.Holds(npc)) return false;
            List<string> keys;
            string what;
            if (trigger != null)
            {
                string type, param;
                if (!TriggerOf(trigger, out type, out param)) return false;
                string fam = IsGeneric(type) ? "ack." + type : "ack." + type + (string.IsNullOrEmpty(param) ? "" : "." + NonAlnum.Replace(param, "_"));
                keys = KeysOf(fam);
                what = "ack " + fam;
                // Voice Control speaks the order's own reply even when the order was refused; where the words it was handed
                // are one of its fixed lines (a refusal, "Are you sure?"), SC says those instead
                List<string> said = KeysForWords(fallbackText);
                foreach (string k in said)
                {
                    if (!k.StartsWith("vc.", StringComparison.Ordinal)) continue;
                    keys = said;
                    what = "ack " + fam + " answered \"" + fallbackText + "\"";
                    break;
                }
            }
            else
            {
                keys = KeysForWords(fallbackText);
                what = "line \"" + fallbackText + "\"";
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
            string caption = trigger == null && !string.IsNullOrWhiteSpace(localizedSubtitle) ? localizedSubtitle : p.Words;
            if (ShowsText(npc)) Subtitle(npc.EntityName, caption, 8f);
            lastKey[FamilyOf(p.Key)] = p.Key;
            Count("vc swapped " + Group(p.Key));
            ScLog.Verbose("voice barks: " + ScCommunity.DisplayName(npc) + " (" + npc.entityId + ") " + what + ": SC's " + p.Key + " in " + p.Voice
                          + " instead of Voice Control's speech");
            return true;
        }

        /// <summary>
        /// NPCAudioPlayer.SpeakStreaming's prefix (Patches_VoiceControl): true = SC plays its clip on that player with Voice
        /// Control's onComplete, its speech is skipped; false = Voice Control speaks as before.
        /// </summary>
        public static bool SubstituteSpeech(EntityAlive npc, string text, Action onComplete, bool isAck)
        {
            if (!ScConfig.ScBarks || npc == null || string.IsNullOrWhiteSpace(text) || !ScXnpc.IsSurvivorBody(npc) || ScVoice.Holds(npc)) return false;
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
            if (generic && ShowsText(npc)) Subtitle(npc.EntityName, p.Words, 8f);
            lastKey[FamilyOf(p.Key)] = p.Key;
            Count("vc swapped " + Group(p.Key));
            ScLog.Verbose("voice barks: " + ScCommunity.DisplayName(npc) + " (" + npc.entityId + ") \"" + text + "\": SC's " + p.Key + " in " + p.Voice
                          + " instead of Voice Control's speech");
            return true;
        }

        /// <summary>The ack types whose replies name a weapon or a side: one generic family each.</summary>
        private static bool IsGeneric(string type)
        {
            return type == "swapweapon" || type == "setformation";
        }

        private static string Group(string key)
        {
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
                // parameter would too; the extras are passed as Type.Missing (they are optional).
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
                    // exactly the silent caption loss this binding exists to fix. Likewise the duration is boxed as the
                    // float? the 3.4.05 signature declares instead of a bare float.
                    ParameterInfo[] ps = mShowSubtitle.GetParameters();
                    object[] args = new object[ps.Length];
                    args[0] = name ?? "";
                    args[1] = text;
                    float secs = Mathf.Clamp(seconds, 2f, 25f);
                    args[2] = Nullable.GetUnderlyingType(ps[2].ParameterType) != null ? (float?)secs : (object)secs;
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
