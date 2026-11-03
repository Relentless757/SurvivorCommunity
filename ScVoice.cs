using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace SurvivorCommunity
{
    /// <summary>
    /// Voiced dialogue (the voice test, 2026-10-16; Enclaves_Plan.md section 6, Q1): when one of the community's survivors
    /// says a dialog line, its recorded clip plays if the mod ships one for that line in that survivor's voice.
    ///
    /// Clips are Resources/Voices/&lt;male|female&gt;_&lt;Name&gt;/&lt;key&gt;.ogg, mono Ogg Vorbis. Every survivor on the
    /// player body (ScXnpc.IsSurvivorBody: recruits, radio contacts, members, stand-ins) speaks in one voice of their sex,
    /// picked by a stable hash of their name, so they keep it while the voice list stays the same (a new voice of their
    /// sex may re-deal it). The key is the line's statement id: "&lt;dialog&gt;.&lt;statement&gt;" first, the dialog being
    /// the statement's own (SdcsNPC.start, 0-XNPCCore's greeting), then the bare statement id (SC's own lines, added to
    /// every dialog under the same ids). vo2: a line an enclave request picks at runtime ({reqask} and the like) is keyed by
    /// its Localization key (scEncAsk_gun_friendly), tried before the statement's. A line with no clip in that voice is not
    /// voiced. A .txt beside a clip holds the words it says, for whoever records it (a line with a name or a number in it
    /// says only the part without them: Tools/voice_lines.py).
    ///
    /// Playback goes through Voice Control (1-XNPCVoiceControl.dll, bound by name, no compile reference) when it has set
    /// up its NPCAudioPlayer on that NPC, as it does for survivors near the player: the clip plays from the AudioSource its
    /// face lip sync reads, at its own volume and range (modconfig TTS Volume, MinDistance, MaxDistance). Voice Control's
    /// cached loader (VoiceClipLoader) loads the clip first, so PlayClipFile starts it at once. SC never sets that player up
    /// itself: Voice Control's chat component carries a personality, billing and door rules it applies only to its own
    /// chat targets. Without the player (Voice Control's TTS off or not running, or not set up yet) SC loads the clip itself
    /// and plays it from its own AudioSource on a child of the NPC, with no lip sync. The child keeps it off the NPC's own
    /// GameObject, whose first AudioSource Voice Control's lip sync and voice relay take.
    ///
    /// One line plays at a time. A new line replaces whatever the NPC was saying, Voice Control's speech included (its
    /// player starts a clip by stopping what it plays). Stopping - the dialog closing (but not a line said as it closes:
    /// SayAfterTalk), an unvoiced line, sc voice stop - stops only SC's own clip: Voice Control's player is stopped only
    /// while its AudioSource still holds that clip. The
    /// same line from the same survivor is not spoken again within RepeatSeconds (it still shows), so returning to a
    /// talk's root or reopening the talk does not repeat the greeting. Main thread only (XUi, console).
    ///
    /// V1, the voice gate (Patches_VoiceControl, REPORT 1.5 B): while SC's talk is open on one of the community's survivors,
    /// or SC's line still plays on them, Voice Control keeps quiet on that NPC (HoldsVoice; MuteVcInTalk). Its greetings, say-
    /// lines and acks are skipped and every sentence it had queued is swallowed with its callback invoked, so its queue,
    /// greeting-busy flag and waiting state end as after a played line. When the player speaks to that NPC through Voice
    /// Control during the talk (push-to-talk, typed chat, the dialog's "What are you carrying?"), the NPC answers through
    /// Voice Control until SC's next line on them (the answer pass). Other NPCs, and these outside a talk, are untouched.
    /// </summary>
    public static class ScVoice
    {
        public const string Folder = "Voices";
        /// <summary>The same line from the same survivor is not spoken again within this many seconds.</summary>
        public const float RepeatSeconds = 30f;
        private const int RecentMax = 256;
        private const string OwnChild = "ScVoice";
        private const float OwnVolume = 0.8f, OwnMinDistance = 2f, OwnMaxDistance = 40f;   // Voice Control's defaults

        private sealed class Voice
        {
            public string Name;
            public bool Male;
            public readonly Dictionary<string, string> Lines = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        private static List<Voice> voices;               // null until scanned

        // the line playing now
        private static int token;                         // bumped by every play and stop: a load that finishes late checks it
        private static string playingTag;                 // "<entity id>|<key>" of the clip playing, or null
        private static Component playingPlayer;           // Voice Control's NPCAudioPlayer, or null
        private static AudioSource playingOwn;            // SC's own source, or null
        private static AudioClip playingClip;
        private static float playingUntil;
        /// <summary>vo2: the clip playing (or loading) is a line said as the talk closes (SayAfterTalk): the close leaves it be.</summary>
        private static bool parting;
        /// <summary>When each "&lt;entity id&gt;|&lt;key&gt;" last started playing: the repeat rule.</summary>
        private static readonly Dictionary<string, float> recent = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        // Voice Control, bound by name once
        private static bool vcBound;
        private static Type tPlayer, tEntry;
        private static MethodInfo mGetClip, mPlayClipFile, mStop;
        private static FieldInfo fPath, fSource;

        private static readonly Dictionary<string, AudioClip> ownClips = new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Every path SC has handed Voice Control's loader, which caches clips by path for the session (sc voice rescan).</summary>
        private static readonly HashSet<string> vcPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // V1: the voice gate's state. The clip's end is kept on Environment.TickCount, not a Unity clock: HoldsVoice uses no Unity API.
        /// <summary>True only around SC's own PlayClipFile call (StartVc): the gate lets that one through.</summary>
        public static bool SelfCall;
        private static int talkingTo = -1;               // the NPC SC's talk is open on (XUiC_ScDialogLine), or -1
        private static int playingEntity = -1;           // the NPC SC's clip plays on, until playingUntilMs; -1 after a Stop
        private static int playingUntilMs;
        private static int answerNpc = -1;               // the answer pass: the player spoke to this NPC through Voice Control
        private static readonly object gateLock = new object();
        private static readonly Dictionary<string, int> swallowedBy = new Dictionary<string, int>(StringComparer.Ordinal);
        private static int swallowedTotal, letThrough;
        private static bool subBound;
        private static PropertyInfo pSubInstance;
        private static MethodInfo mClearSub;

        // ------------------------------------------------------------------ voices ----

        private static List<Voice> Voices()
        {
            if (voices != null) return voices;
            var list = new List<Voice>();
            try
            {
                Mod mod = ModManager.GetMod("SurvivorCommunity");
                string root = mod != null ? Path.Combine(Path.Combine(mod.Path, "Resources"), Folder) : null;
                if (root != null && Directory.Exists(root))
                {
                    foreach (string dir in Directory.GetDirectories(root))
                    {
                        string name = Path.GetFileName(dir);
                        int us = name.IndexOf('_');
                        string sex = us > 0 ? name.Substring(0, us) : "";
                        bool male;
                        if (sex.Equals("male", StringComparison.OrdinalIgnoreCase)) male = true;
                        else if (sex.Equals("female", StringComparison.OrdinalIgnoreCase)) male = false;
                        else
                        {
                            ScLog.Once("voice-folder-" + name, "voice: Resources/Voices/" + name + " is not named male_<Name> or female_<Name> - skipped");
                            continue;
                        }
                        var v = new Voice { Name = name, Male = male };
                        foreach (string f in Directory.GetFiles(dir, "*.ogg")) v.Lines[Path.GetFileNameWithoutExtension(f)] = f;
                        if (v.Lines.Count > 0) list.Add(v);
                    }
                }
                list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception e)
            {
                ScLog.Once("voice-scan", e);
            }
            voices = list;
            ScLog.Out("voice: " + Describe());
            return voices;
        }

        /// <summary>The voices found, for the log and the console: "female_Jessica (7 lines), male_Vane (7 lines)".</summary>
        public static string Describe()
        {
            List<Voice> all = voices;
            if (all == null) return "not scanned yet";
            if (all.Count == 0) return "no voices in Resources/" + Folder;
            var sb = new StringBuilder();
            foreach (Voice v in all)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(v.Name).Append(" (").Append(v.Lines.Count).Append(v.Lines.Count == 1 ? " line)" : " lines)");
            }
            return sb.ToString();
        }

        /// <summary>The voice this survivor speaks in, or null: not one of the community's survivors, or no voice of their sex.</summary>
        private static Voice VoiceOf(EntityAlive npc)
        {
            if (npc == null || !ScXnpc.IsSurvivorBody(npc)) return null;
            List<Voice> all = Voices();
            bool male = ScLooks.ClassIsMale(ScCommunity.ClassName(npc));
            int n = 0;
            foreach (Voice v in all) if (v.Male == male) n++;
            if (n == 0) return null;
            int pick = (int)(Fnv(ScCommunity.DisplayName(npc)) % (uint)n);
            foreach (Voice v in all) if (v.Male == male && pick-- == 0) return v;
            return null;
        }

        /// <summary>FNV-1a: a stable string hash (string.GetHashCode is not stable across runtimes).</summary>
        private static uint Fnv(string s)
        {
            uint h = 2166136261u;
            if (s != null) foreach (char c in s) { h ^= c; h *= 16777619u; }
            return h;
        }

        /// <summary>
        /// The clip for this line in this voice, and the key it was found under; null when the line is not voiced. A line
        /// picked at runtime (vo2: lineKey, an enclave request's Localization key) is looked up by that key first. Then the
        /// statement: the dialog in the key is the statement's own (a child dialog SCore opened has its own id), else the
        /// innermost open one.
        /// </summary>
        private static string ClipFor(Voice v, Dialog dialog, BaseStatement st, string lineKey, out string key)
        {
            key = null;
            if (v == null || st == null || string.IsNullOrEmpty(st.ID)) return null;
            string path;
            if (!string.IsNullOrEmpty(lineKey) && v.Lines.TryGetValue(lineKey, out path))
            {
                key = lineKey;
                return path;
            }
            Dialog owner = st.OwnerDialog;
            if (owner == null && dialog != null)
            {
                owner = dialog;
                for (int depth = 0; owner.ChildDialog != null && depth < 8; depth++) owner = owner.ChildDialog;
            }
            if (owner != null && !string.IsNullOrEmpty(owner.ID) && v.Lines.TryGetValue(owner.ID + "." + st.ID, out path))
            {
                key = owner.ID + "." + st.ID;
                return path;
            }
            if (v.Lines.TryGetValue(st.ID, out path))
            {
                key = st.ID;
                return path;
            }
            return null;
        }

        // ------------------------------------------------------------------ the dialog's hook ----

        /// <summary>
        /// A new line is on screen (XUiC_ScDialogLine); lineKey is the Localization key of a line picked at runtime (vo2:
        /// ScEncRequests' {req...} tokens), else null. Whatever SC was playing stops, unless this is that very line still
        /// being said; then this line plays when the survivor has a clip for it and has not started it in the last
        /// RepeatSeconds.
        /// </summary>
        public static void OnLine(EntityAlive npc, Dialog dialog, BaseStatement st, string lineKey)
        {
            if (npc == null || st == null) return;
            string key = null;
            string path = ScConfig.VoiceLines ? ClipFor(VoiceOf(npc), dialog, st, lineKey, out key) : null;
            string tag = path != null ? npc.entityId + "|" + key : null;
            if (tag != null && tag == playingTag && Time.unscaledTime < playingUntil) return;   // still saying it
            Stop();
            if (tag == null) return;
            float at;
            if (recent.TryGetValue(tag, out at) && Time.unscaledTime - at < RepeatSeconds) return;
            Play(npc, tag, path);
        }

        /// <summary>
        /// vo2: a line said as the talk closes (an enclave leader's answer to "Let me think about it.", which comes as a
        /// tell): it plays as a dialog line does - in the survivor's voice, under the repeat rule - and the talk's close
        /// does not stop it (OnTalkClosed).
        /// </summary>
        public static void SayAfterTalk(EntityAlive npc, string key)
        {
            if (npc == null || string.IsNullOrEmpty(key) || !ScConfig.VoiceLines) return;
            Voice v = VoiceOf(npc);
            string path;
            if (v == null || !v.Lines.TryGetValue(key, out path)) return;
            string tag = npc.entityId + "|" + key;
            float at;
            if (recent.TryGetValue(tag, out at) && Time.unscaledTime - at < RepeatSeconds) return;
            Play(npc, tag, path);
            parting = true;   // after Play: its Stop clears the flag
        }

        /// <summary>
        /// The talk closed (XUiC_ScDialogLine): SC's clip stops, unless it is a line said as the talk closes (SayAfterTalk).
        /// V1: the gate lets go of the NPC, except while that parting line plays; the answer pass ends with the talk.
        /// </summary>
        public static void OnTalkClosed()
        {
            talkingTo = -1;
            answerNpc = -1;
            if (!parting) Stop();
        }

        // ------------------------------------------------------------------ V1: the voice gate ----

        /// <summary>
        /// V1: the talk is open on this NPC (XUiC_ScDialogLine.Update, every frame its Respondent is known). A new talk ends
        /// an earlier answer pass, and on one of the community's survivors hides Voice Control's HUD subtitle once.
        /// </summary>
        public static void TalkOn(EntityAlive npc)
        {
            int id = npc != null ? npc.entityId : -1;
            if (id == talkingTo) return;
            talkingTo = id;
            answerNpc = -1;
            if (id >= 0 && ScConfig.MuteVcInTalk && ScXnpc.IsSurvivorBody(npc)) ClearVcSubtitle();
        }

        /// <summary>Why SC holds this NPC's voice ("talk open", "SC line playing"), or null. Ignores the answer pass.</summary>
        private static string HeldWhy(EntityAlive npc)
        {
            if ((object)npc == null || !ScConfig.MuteVcInTalk) return null;
            int id = npc.entityId;
            string why = id == talkingTo ? "talk open" : id == playingEntity && ClipMsLeft() > 0 ? "SC line playing" : null;
            return why != null && ScXnpc.IsSurvivorBody(npc) ? why : null;
        }

        private static int ClipMsLeft()
        {
            return playingEntity < 0 ? 0 : unchecked(playingUntilMs - Environment.TickCount);
        }

        /// <summary>
        /// V1: Voice Control keeps quiet on this NPC: one of the community's survivors (on the player body), MuteVcInTalk on,
        /// and SC's talk open on them or SC's line still playing on them - unless the player spoke to them through Voice
        /// Control since SC's last line (the answer pass).
        /// </summary>
        public static bool HoldsVoice(EntityAlive npc)
        {
            return HeldWhy(npc) != null && npc.entityId != answerNpc;
        }

        /// <summary>A cheap first test on an entity id (the MP relay's prefix asks before it looks the entity up).</summary>
        public static bool MayHold(int entityId)
        {
            return entityId >= 0 && (entityId == talkingTo || entityId == playingEntity);
        }

        /// <summary>
        /// V1, the gate's question at each of Voice Control's sinks: true = swallow this call (counted, and one Verbose line
        /// naming the NPC and the sink). A call let through by the answer pass is counted too.
        /// </summary>
        public static bool Swallow(EntityAlive npc, string sink, string what)
        {
            string why = HeldWhy(npc);
            if (why == null) return false;
            int total;
            lock (gateLock)
            {
                if (npc.entityId == answerNpc)
                {
                    letThrough++;
                    return false;
                }
                int n;
                swallowedBy.TryGetValue(sink, out n);
                swallowedBy[sink] = n + 1;
                total = ++swallowedTotal;
            }
            ScLog.Verbose("voice gate: kept Voice Control quiet on " + ScCommunity.DisplayName(npc) + " (" + npc.entityId + "): " + what
                          + " at " + sink + " (" + why + "), " + total + " this session");
            return true;
        }

        /// <summary>
        /// V1: the player spoke to this NPC through Voice Control (NPCChatComponent.ProcessPlayerMessage: push-to-talk, typed
        /// chat): it answers through Voice Control until SC's next line on it, the talk's close, or a new talk.
        /// </summary>
        public static void PlayerSpoke(EntityAlive npc, string how)
        {
            if ((object)npc == null || !ScXnpc.IsSurvivorBody(npc)) return;   // another NPC never takes the pass from the one you talk to
            bool held = HeldWhy(npc) != null;
            answerNpc = npc.entityId;
            if (held) ScLog.Verbose("voice gate: " + ScCommunity.DisplayName(npc) + " (" + npc.entityId + ") answers through Voice Control until SC's next line (" + how + ")");
        }

        /// <summary>V1: the player asked Voice Control something from the open talk (a dialog row Voice Control answers): the pass for that talk's NPC.</summary>
        public static void PlayerAskedInTalk(string how)
        {
            if (talkingTo < 0) return;
            World w = ScCommunity.World;
            EntityAlive npc = w != null ? w.GetEntity(talkingTo) as EntityAlive : null;
            if (npc != null) PlayerSpoke(npc, how);
        }

        /// <summary>V1: hides Voice Control's HUD subtitle (SubtitleManager.Instance.ClearSubtitle(), bound by name).</summary>
        private static void ClearVcSubtitle()
        {
            try
            {
                if (!subBound)
                {
                    subBound = true;
                    Type t = ScTypes.Find("XNPCVoiceControl.UI.SubtitleManager");
                    pSubInstance = t != null ? t.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static) : null;
                    mClearSub = t != null ? t.GetMethod("ClearSubtitle", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null) : null;
                    if (t != null && (pSubInstance == null || mClearSub == null))
                        ScLog.Once("voice-gate-subtitle-bind", "voice gate: Voice Control's SubtitleManager.Instance / ClearSubtitle() not found - its HUD subtitle is not hidden as a talk opens (Voice Control update?)");
                }
                if (pSubInstance == null || mClearSub == null) return;
                object mgr = pSubInstance.GetValue(null, null);
                if (mgr != null) mClearSub.Invoke(mgr, null);
            }
            catch (Exception e)
            {
                ScLog.Once("voice-gate-subtitle", e);
            }
        }

        /// <summary>
        /// V1 test aid (sc voice sayline): after some seconds this survivor is given one of 0-XNPCCore's say-lines, the way
        /// XNPCCore gives it - its "XNPCCore.SayLine" hook, which Voice Control 3.4 installs - or, without the hook (Voice
        /// Control 3.2 has none), through SubtitleManager.AcknowledgeCommand with the line's words. So the gate can be watched
        /// with a talk open (run it, close the console, open the talk before it fires).
        /// </summary>
        public static string SayLineLater(EntityAlive npc, string line, float seconds)
        {
            GameManager gm = GameManager.Instance;
            if (npc == null || gm == null) return "no survivor";
            gm.StartCoroutine(SayLineAfter(npc, line, seconds));
            return ScCommunity.DisplayName(npc) + " (" + npc.entityId + ") is given the say-line " + line + " in "
                   + seconds.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " s";
        }

        private static IEnumerator SayLineAfter(EntityAlive npc, string line, float seconds)
        {
            if (seconds > 0f) yield return new WaitForSeconds(seconds);
            SayLineNow(npc, line);
        }

        private static void SayLineNow(EntityAlive npc, string line)
        {
            try
            {
                if (npc == null || npc.IsDead()) return;   // Unity null: gone meanwhile
                bool held = HoldsVoice(npc);
                string how;
                var hook = AppDomain.CurrentDomain.GetData("XNPCCore.SayLine") as Action<EntityAlive, string>;
                if (hook != null)
                {
                    how = "0-XNPCCore's say-line hook";
                    hook(npc, line);
                }
                else
                {
                    MethodBase ack = Patches_VoiceControl.AckTarget();
                    if (ack == null)
                    {
                        ScLog.Out("voice gate test: no say-line hook and no Voice Control - nothing said");
                        return;
                    }
                    how = "Voice Control's AcknowledgeCommand";
                    var args = new object[ack.GetParameters().Length];   // trigger, trace (and 3.4's subtitle) stay null
                    args[0] = npc;
                    args[2] = line == "ammo.dry" ? "I'm out of ammo." : line;
                    ack.Invoke(null, args);
                }
                ScLog.Out("voice gate test: " + ScCommunity.DisplayName(npc) + " (" + npc.entityId + ") was given the say-line " + line + " through " + how
                          + (held ? " - held: Voice Control stays quiet" : " - not held: Voice Control says it"));
            }
            catch (Exception e)
            {
                ScLog.Once("voice-gate-sayline", e);
            }
        }

        /// <summary>V1: per world (ModApi, GameStarting): no talk, no clip, no pass, counters at zero.</summary>
        public static void Reset()
        {
            SelfCall = false;
            talkingTo = -1;
            playingEntity = -1;
            answerNpc = -1;
            lock (gateLock)
            {
                swallowedBy.Clear();
                swallowedTotal = 0;
                letThrough = 0;
            }
        }

        private static string NameOf(int entityId)
        {
            World w = ScCommunity.World;
            EntityAlive e = w != null ? w.GetEntity(entityId) as EntityAlive : null;
            return (e != null ? ScCommunity.DisplayName(e) : "entity") + " (" + entityId + ")";
        }

        /// <summary>sc voice gate: the switch, the NPC held and why, the answer pass, and the session's counters.</summary>
        public static string GateStatus()
        {
            var sb = new StringBuilder();
            sb.Append("MuteVcInTalk=").Append(ScConfig.MuteVcInTalk).Append("; talk open on ");
            if (talkingTo < 0) sb.Append("nobody");
            else
            {
                World w = ScCommunity.World;
                Entity e = w != null ? w.GetEntity(talkingTo) : null;
                sb.Append(NameOf(talkingTo)).Append(e != null && ScXnpc.IsSurvivorBody(e) ? " (held)" : " (not one of the community's survivors: not held)");
            }
            int left = ClipMsLeft();
            sb.Append("; SC line playing on ").Append(left > 0 ? NameOf(playingEntity) + " for " + (left / 1000f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " s more" : "nobody");
            sb.Append("; answer pass: ").Append(answerNpc >= 0 ? NameOf(answerNpc) : "nobody");
            lock (gateLock)
            {
                sb.Append("; swallowed this session: ").Append(swallowedTotal);
                if (swallowedBy.Count > 0)
                {
                    var keys = new List<string>(swallowedBy.Keys);
                    keys.Sort(StringComparer.Ordinal);
                    sb.Append(" (");
                    for (int i = 0; i < keys.Count; i++) sb.Append(i > 0 ? ", " : "").Append(keys[i]).Append('=').Append(swallowedBy[keys[i]]);
                    sb.Append(')');
                }
                sb.Append("; let through by the answer pass: ").Append(letThrough);
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ playing ----

        private static void Play(EntityAlive npc, string tag, string path)
        {
            Stop();
            // V1: SC's new line on this NPC ends the answer pass - Voice Control's answer still queued is swallowed from here
            if (answerNpc == npc.entityId)
            {
                answerNpc = -1;
                ScLog.Verbose("voice gate: SC's line on " + ScCommunity.DisplayName(npc) + " (" + npc.entityId + ") ends the answer pass");
            }
            int id = npc.entityId;
            int my = token;
            Component player = VcPlayer(npc);
            if (player != null)
            {
                try
                {
                    object entry = Activator.CreateInstance(tEntry);
                    fPath.SetValue(entry, path);
                    vcPaths.Add(path);
                    Action<AudioClip> ready = clip =>
                    {
                        try { if (my == token) StartVc(player, entry, id, tag, path, clip); }
                        catch (Exception e) { ScLog.Once("voice-vc-play", e); }
                    };
                    mGetClip.Invoke(null, new object[] { entry, ready });
                    return;
                }
                catch (Exception e)
                {
                    ScLog.Once("voice-vc", e);   // fall through to SC's own source
                }
            }
            StartOwn(npc, tag, path, my);
        }

        private static void StartVc(Component player, object entry, int entityId, string tag, string path, AudioClip clip)
        {
            if (clip == null)
            {
                ScLog.Once("voice-load-" + path, "voice: " + path + " did not load (Voice Control's loader)");
                return;
            }
            if (player == null) return;   // Unity null: the NPC went away while the clip loaded
            SelfCall = true;              // V1: the gate's PlayClipFile prefix lets SC's own call through
            try { mPlayClipFile.Invoke(player, new object[] { entry, null }); }
            finally { SelfCall = false; }
            playingPlayer = player;
            Started(entityId, tag, clip);
        }

        /// <summary>A clip started: what Stop, the repeat rule and (V1) the voice gate read.</summary>
        private static void Started(int entityId, string tag, AudioClip clip)
        {
            playingTag = tag;
            playingClip = clip;
            playingUntil = Time.unscaledTime + clip.length;
            playingEntity = entityId;
            playingUntilMs = unchecked(Environment.TickCount + (int)(clip.length * 1000f) + 250);   // + a little for the start
            if (recent.Count >= RecentMax)
            {
                var old = new List<string>();
                foreach (KeyValuePair<string, float> kv in recent) if (Time.unscaledTime - kv.Value >= RepeatSeconds) old.Add(kv.Key);
                foreach (string k in old) recent.Remove(k);
                if (recent.Count >= RecentMax) recent.Clear();
            }
            recent[tag] = Time.unscaledTime;
        }

        /// <summary>Stops SC's own clip, if one is playing (never anything Voice Control started).</summary>
        public static void Stop()
        {
            token++;
            try
            {
                if (playingPlayer != null)   // Unity null: a destroyed NPC's player needs no stop
                {
                    bool ours;
                    if (fSource != null)
                    {
                        AudioSource src = fSource.GetValue(playingPlayer) as AudioSource;
                        ours = src != null && src.isPlaying && (object)src.clip == (object)playingClip;
                    }
                    else ours = Time.unscaledTime < playingUntil;
                    if (ours) mStop.Invoke(playingPlayer, null);
                }
                if (playingOwn != null) playingOwn.Stop();
            }
            catch (Exception e)
            {
                ScLog.Once("voice-stop", e);
            }
            playingTag = null;
            playingPlayer = null;
            playingOwn = null;
            playingClip = null;
            playingEntity = -1;
            parting = false;
        }

        /// <summary>Voice Control's player on this NPC, when Voice Control has set one up; null otherwise.</summary>
        private static Component VcPlayer(EntityAlive npc)
        {
            if (!BindVc() || npc == null || npc.gameObject == null) return null;
            return npc.gameObject.GetComponent(tPlayer);
        }

        private static bool BindVc()
        {
            if (vcBound) return tPlayer != null;
            vcBound = true;
            try
            {
                tPlayer = ScTypes.Find("XNPCVoiceControl.TTS.NPCAudioPlayer");
                tEntry = ScTypes.Find("XNPCVoiceControl.Core.ClipEntry");
                Type tLoader = ScTypes.Find("XNPCVoiceControl.Core.VoiceClipLoader");
                if (tPlayer == null || tEntry == null || tLoader == null)
                {
                    tPlayer = null;
                    ScLog.Out("voice: Voice Control is not loaded - clips play from SC's own source (no lip sync)");
                    return false;
                }
                fPath = tEntry.GetField("AudioPath", BindingFlags.Public | BindingFlags.Instance);
                mGetClip = tLoader.GetMethod("GetClip", BindingFlags.Public | BindingFlags.Static, null, new[] { tEntry, typeof(Action<AudioClip>) }, null);
                mPlayClipFile = tPlayer.GetMethod("PlayClipFile", BindingFlags.Public | BindingFlags.Instance, null, new[] { tEntry, typeof(Action) }, null);
                mStop = tPlayer.GetMethod("Stop", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                fSource = tPlayer.GetField("_audioSource", BindingFlags.NonPublic | BindingFlags.Instance);
                if (fPath == null || mGetClip == null || mPlayClipFile == null || mStop == null)
                {
                    ScLog.Err("voice: Voice Control's player has changed (AudioPath " + (fPath != null) + ", GetClip " + (mGetClip != null)
                              + ", PlayClipFile " + (mPlayClipFile != null) + ", Stop " + (mStop != null) + ") - clips play from SC's own source (no lip sync)");
                    tPlayer = null;
                    return false;
                }
                ScLog.Out("voice: clips play through Voice Control's player where it has one (lip sync)"
                          + (fSource == null ? "; its AudioSource is not readable, so a stop trusts the clip's length" : ""));
                return true;
            }
            catch (Exception e)
            {
                tPlayer = null;
                ScLog.Once("voice-bind", e);
                return false;
            }
        }

        // ------------------------------------------------------------------ SC's own source (no Voice Control player) ----

        private static void StartOwn(EntityAlive npc, string tag, string path, int my)
        {
            AudioClip clip;
            if (ownClips.TryGetValue(path, out clip) && clip != null)
            {
                PlayOwn(npc, tag, clip);
                return;
            }
            GameManager gm = GameManager.Instance;
            if (gm == null) return;
            gm.StartCoroutine(LoadOwn(path, loaded =>
            {
                if (loaded == null) return;
                ownClips[path] = loaded;
                if (my == token) PlayOwn(npc, tag, loaded);
            }));
        }

        private static IEnumerator LoadOwn(string path, Action<AudioClip> done)
        {
            UnityWebRequest req = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, AudioType.OGGVORBIS);
            try
            {
                yield return req.SendWebRequest();
                AudioClip clip = null;
                try
                {
                    if (req.result == UnityWebRequest.Result.Success) clip = DownloadHandlerAudioClip.GetContent(req);
                    else ScLog.Once("voice-load-" + path, "voice: " + path + " did not load: " + req.error);
                    done(clip);
                }
                catch (Exception e)
                {
                    ScLog.Once("voice-own", e);
                }
            }
            finally
            {
                req.Dispose();
            }
        }

        private static void PlayOwn(EntityAlive npc, string tag, AudioClip clip)
        {
            if (npc == null || npc.IsDead()) return;   // Unity null: gone while the clip loaded
            Transform t = npc.transform.Find(OwnChild);
            GameObject go;
            if (t != null) go = t.gameObject;
            else
            {
                go = new GameObject(OwnChild);
                go.transform.SetParent(npc.transform, false);
                go.transform.localPosition = new Vector3(0f, 1.6f, 0f);   // about the mouth
            }
            AudioSource src = go.GetComponent<AudioSource>();
            if (src == null)
            {
                src = go.AddComponent<AudioSource>();
                src.spatialBlend = 1f;
                src.minDistance = OwnMinDistance;
                src.maxDistance = OwnMaxDistance;
                src.playOnAwake = false;
                src.loop = false;
                src.bypassEffects = true;
                src.bypassListenerEffects = true;
                src.bypassReverbZones = true;
            }
            src.volume = OwnVolume;
            src.clip = clip;
            src.Play();
            playingOwn = src;
            Started(npc.entityId, tag, clip);
        }

        // ------------------------------------------------------------------ console ----

        /// <summary>sc voice: the switches, the player in use and the voices found.</summary>
        public static string Status()
        {
            Voices();
            bool vc = BindVc();
            return "DialogLine=" + ScConfig.DialogLine + " VoiceLines=" + ScConfig.VoiceLines + "; player: "
                   + (vc ? "Voice Control's where it has one (lip sync), else SC's own" : "SC's own (no lip sync)") + "; voices: " + Describe();
        }

        /// <summary>sc voice rescan: read Resources/Voices again and forget loaded clips, so new or replaced files play without a restart.</summary>
        public static string Rescan()
        {
            Stop();
            voices = null;
            ownClips.Clear();
            recent.Clear();
            int dropped = 0;
            try
            {
                // Voice Control caches clips by path for the session: drop the ones SC loaded so a replaced file reloads
                Type tLoader = ScTypes.Find("XNPCVoiceControl.Core.VoiceClipLoader");
                FieldInfo fCache = tLoader != null ? tLoader.GetField("_cache", BindingFlags.NonPublic | BindingFlags.Static) : null;
                FieldInfo fLock = tLoader != null ? tLoader.GetField("_lock", BindingFlags.NonPublic | BindingFlags.Static) : null;
                var cache = fCache != null ? fCache.GetValue(null) as Dictionary<string, AudioClip> : null;
                object gate = fLock != null ? fLock.GetValue(null) : null;
                if (cache != null && gate != null)
                {
                    lock (gate)
                    {
                        foreach (string k in vcPaths) if (cache.Remove(k)) dropped++;
                    }
                }
                vcPaths.Clear();
            }
            catch (Exception e)
            {
                ScLog.Once("voice-rescan", e);
            }
            Voices();
            return Describe() + (dropped > 0 ? "; " + dropped + " cached clip(s) dropped from Voice Control" : "");
        }

        /// <summary>sc voice play: this survivor says this line now, repeat rule or not (the key is a clip's name).</summary>
        public static string PlayLine(EntityAlive npc, string key)
        {
            if (npc == null) return "no survivor";
            Voice v = VoiceOf(npc);
            if (v == null) return ScCommunity.DisplayName(npc) + " has no voice (not one of the community's survivors, or no voice of their sex)";
            string path;
            if (!v.Lines.TryGetValue(key, out path)) return v.Name + " has no line " + key + " (it has: " + string.Join(", ", new List<string>(v.Lines.Keys).ToArray()) + ")";
            Play(npc, npc.entityId + "|" + key, path);
            return ScCommunity.DisplayName(npc) + " says " + key + " in " + v.Name;
        }

        /// <summary>The nearest survivor with a voice within r metres of a point, for sc voice play without a name.</summary>
        public static EntityAlive Nearest(Vector3 at, float r)
        {
            World w = ScCommunity.World;
            if (w == null) return null;
            EntityAlive best = null;
            float bestSq = r * r;
            List<Entity> list = w.Entities.list;
            for (int i = 0; i < list.Count; i++)
            {
                EntityAlive e = list[i] as EntityAlive;
                if (e == null || e.IsDead() || !ScXnpc.IsSurvivorBody(e)) continue;
                float sq = (e.position - at).sqrMagnitude;
                if (sq < bestSq && VoiceOf(e) != null) { best = e; bestSq = sq; }
            }
            return best;
        }
    }
}
