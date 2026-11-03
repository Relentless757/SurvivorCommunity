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
    /// Clips are Resources/Voices/&lt;male|female&gt;_&lt;Race&gt;_&lt;Name&gt;/&lt;key&gt;.ogg, mono Ogg Vorbis. V2: the voices are a
    /// table (ScConfig.Voices: two per race and sex, White, Black, Asian, Native; a folder may not exist yet). Every survivor
    /// on the player body (ScXnpc.IsSurvivorBody: recruits, radio contacts, members, stand-ins) and every raider
    /// (ScXnpc.IsRaider) speaks in one of the two voices of their race and sex - the body's own (EModelSDCS.Archetype: for
    /// SC's bodies the look SC applied), else SC's saved look (ScLooks.Get), else the class's sex - picked by a stable hash
    /// of their name (a raider's name and id), so they keep it while the table stays the same. While that voice has no clip
    /// for a line, the other voice of their race and sex says it, then any recorded voice of their sex, in an order stable
    /// for that person (Chain): survivors keep talking while a race's recordings don't exist yet. VoiceByRace=false deals
    /// among every recorded voice of the sex, as before V2. The key is the line's statement id: "&lt;dialog&gt;.&lt;statement&gt;" first, the dialog being
    /// the statement's own (SdcsNPC.start, 0-XNPCCore's greeting), then the bare statement id (SC's own lines, added to
    /// every dialog under the same ids). vo2: a line an enclave request picks at runtime ({reqask} and the like) is keyed by
    /// its Localization key (scEncAsk_gun_friendly), tried before the statement's. A line with no clip in that voice is not
    /// voiced. A .txt beside a clip holds the words it says, for whoever records it (a line with a name or a number in it
    /// says only the part without them: Tools/voice_lines.py).
    ///
    /// Playback goes through Voice Control (1-XNPCVoiceControl.dll, bound by name, no compile reference) when it has set
    /// up its NPCAudioPlayer on that NPC, as it does for survivors near the player: the clip plays from the AudioSource its
    /// face lip sync reads, at its own volume and range (modconfig TTS Volume, MinDistance, MaxDistance). V2: SC loads the
    /// clip itself (ScClipCache: compressed, least recently used dropped) and hands it to PlayClipFile through Voice
    /// Control's loader cache for that one call (PlayOnVc), so Voice Control never keeps SC's clips. SC never sets that player up
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
    ///
    /// V2, barks (ScBarks: greetings, chatter, raider taunts, XNPCCore's lines in SC's voices): PlayBark plays a clip on an
    /// NPC outside the talk's one-line slot - it never stops the talk's line, and the gate does not hold Voice Control for
    /// it. A survivor's bark goes through Voice Control's player when there is one (lip sync), a raider's always from SC's
    /// own source on a child. Holds(npc) - the talk open on them, or SC's talk line still playing on them - keeps barks off them.
    /// </summary>
    public static class ScVoice
    {
        public const string Folder = "Voices";
        /// <summary>The same line from the same survivor is not spoken again within this many seconds.</summary>
        public const float RepeatSeconds = 30f;
        private const int RecentMax = 256;
        private const string OwnChild = "ScVoice";
        private const float OwnVolume = 0.8f, OwnMinDistance = 2f, OwnMaxDistance = 40f;   // Voice Control's defaults

        /// <summary>The races the voice table knows (SDCSDataUtils' list: the only race= values in vanilla and XNPCCore archetypes).</summary>
        public static readonly string[] Races = { "White", "Black", "Asian", "Native" };

        internal sealed class Voice
        {
            /// <summary>The folder name: male_White_Vane.</summary>
            public string Name;
            public bool Male;
            /// <summary>One of Races, or "" for an old &lt;sex&gt;_&lt;Name&gt; folder (any race: only a fallback).</summary>
            public string Race = "";
            /// <summary>In the config's voice table (else a folder found on disk only).</summary>
            public bool Listed;
            public bool OnDisk;
            public readonly Dictionary<string, string> Lines = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public bool Recorded { get { return Lines.Count > 0; } }
        }

        private static List<Voice> voices;               // null until scanned
        /// <summary>V2: each NPC's voice chain, by entity id, with the (sex|race|seed) it was built for.</summary>
        private static readonly Dictionary<int, KeyValuePair<string, List<Voice>>> chains = new Dictionary<int, KeyValuePair<string, List<Voice>>>();
        /// <summary>V2: when each NPC's bark ends (Time.unscaledTime), by entity id.</summary>
        private static readonly Dictionary<int, float> barkUntil = new Dictionary<int, float>();

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

        /// <summary>Every path SC has let Voice Control's loader load itself (only when its cache could not be seeded): it keeps them for the session (sc voice rescan).</summary>
        private static readonly HashSet<string> vcPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // V2: Voice Control's loader cache (VoiceClipLoader._cache / _lock), seeded for one PlayClipFile call (PlayOnVc)
        private static bool vcCacheBound;
        private static Dictionary<string, AudioClip> vcCache;
        private static object vcCacheLock;
        // V2: the player's busy state (NPCAudioPlayer._isPlaying / _draining): a bark never starts over Voice Control's speech
        private static FieldInfo fIsPlaying, fDraining;

        // V1: the voice gate's state. The clip's end is kept on Environment.TickCount, not a Unity clock: HoldsVoice uses no Unity API.
        /// <summary>True only around SC's own PlayClipFile call (PlayOnVc): the gate lets that one through.</summary>
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

        /// <summary>
        /// The voice table (ScConfig.Voices, in its order; a voice whose folder doesn't exist yet has no lines), then any
        /// other folder on disk named &lt;male|female&gt;_&lt;Race&gt;_&lt;Name&gt; (or the old &lt;male|female&gt;_&lt;Name&gt;, raceless).
        /// Scanned once per world (and by sc voice rescan).
        /// </summary>
        private static List<Voice> Voices()
        {
            if (voices != null) return voices;
            var list = new List<Voice>();
            var byName = new Dictionary<string, Voice>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!ScConfig.Parsed && ThreadManager.IsMainThread()) ScConfig.EnsureParsed();
                foreach (string n in ScConfig.Voices)
                {
                    Voice v = ParseName(n);
                    if (v == null)
                    {
                        ScLog.Once("voice-table-" + n, "voice: Voices lists '" + n + "', which is not <male|female>_<Race>_<Name> with a race of " + string.Join("/", Races) + " - left out");
                        continue;
                    }
                    if (byName.ContainsKey(v.Name)) continue;
                    v.Listed = true;
                    list.Add(v);
                    byName[v.Name] = v;
                }
                Mod mod = ModManager.GetMod("SurvivorCommunity");
                string root = mod != null ? Path.Combine(Path.Combine(mod.Path, "Resources"), Folder) : null;
                var extra = new List<Voice>();
                if (root != null && Directory.Exists(root))
                {
                    foreach (string dir in Directory.GetDirectories(root))
                    {
                        string name = Path.GetFileName(dir);
                        Voice v;
                        if (!byName.TryGetValue(name, out v))
                        {
                            v = ParseName(name);
                            if (v == null)
                            {
                                ScLog.Once("voice-folder-" + name, "voice: Resources/Voices/" + name + " is not named <male|female>_<Race>_<Name> - skipped");
                                continue;
                            }
                            extra.Add(v);
                            byName[v.Name] = v;
                        }
                        v.OnDisk = true;
                        foreach (string f in Directory.GetFiles(dir, "*.ogg")) v.Lines[Path.GetFileNameWithoutExtension(f)] = f;
                    }
                }
                extra.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                list.AddRange(extra);
            }
            catch (Exception e)
            {
                ScLog.Once("voice-scan", e);
            }
            voices = list;
            chains.Clear();
            ScLog.Out("voice: " + Describe());
            return voices;
        }

        /// <summary>A voice from its folder name: &lt;male|female&gt;_&lt;Race&gt;_&lt;Name&gt;, or the old &lt;male|female&gt;_&lt;Name&gt; (raceless); null when it is neither.</summary>
        private static Voice ParseName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            name = name.Trim();
            int us = name.IndexOf('_');
            if (us <= 0 || us == name.Length - 1) return null;
            string sex = name.Substring(0, us);
            bool male;
            if (sex.Equals("male", StringComparison.OrdinalIgnoreCase)) male = true;
            else if (sex.Equals("female", StringComparison.OrdinalIgnoreCase)) male = false;
            else return null;
            string rest = name.Substring(us + 1);
            int us2 = rest.IndexOf('_');
            string race = "";
            if (us2 > 0 && us2 < rest.Length - 1)
            {
                race = CanonRace(rest.Substring(0, us2));
                if (race == null) return null;             // a third part that is no race: a typo, not an old name
            }
            return new Voice { Name = name, Male = male, Race = race };
        }

        /// <summary>White, Black, Asian or Native (any case), as the table spells it; null for anything else.</summary>
        public static string CanonRace(string race)
        {
            if (string.IsNullOrEmpty(race)) return null;
            foreach (string r in Races) if (r.Equals(race.Trim(), StringComparison.OrdinalIgnoreCase)) return r;
            return null;
        }

        /// <summary>The voices found, for the log and the console: the recorded ones with their lines, and how many of the table have none yet.</summary>
        public static string Describe()
        {
            List<Voice> all = voices;
            if (all == null) return "not scanned yet";
            if (all.Count == 0) return "no voices (Voices is empty and Resources/" + Folder + " has none)";
            var sb = new StringBuilder();
            int silent = 0;
            foreach (Voice v in all)
            {
                if (!v.Recorded) { silent++; continue; }
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(v.Name).Append(" (").Append(v.Lines.Count).Append(v.Lines.Count == 1 ? " line" : " lines").Append(v.Listed ? ")" : ", not in Voices)");
            }
            if (sb.Length == 0) sb.Append("none recorded");
            if (silent > 0) sb.Append("; ").Append(silent).Append(silent == 1 ? " voice" : " voices").Append(" of the table with no recordings yet");
            return sb.ToString();
        }

        /// <summary>
        /// V2: the race and sex a voice is picked for: the body's own archetype (for SC's bodies the look SC applied), else
        /// SC's saved look (a re-created body's, ScLooks.Get), else the class's sex with no race. from: model, look or class.
        /// </summary>
        public static void RaceAndSex(EntityAlive npc, out string race, out bool male, out string from)
        {
            race = null;
            male = true;
            from = "class";
            bool sexKnown = false;
            try
            {
                EModelSDCS m = npc != null ? npc.emodel as EModelSDCS : null;
                Archetype a = m != null ? m.Archetype : null;
                if (a != null)
                {
                    male = a.IsMale;
                    sexKnown = true;
                    from = "model";
                    race = CanonRace(a.Race);
                    if (race != null) return;
                }
                Look l = npc != null ? ScLooks.Get(npc) : null;
                if (l != null)
                {
                    if (!sexKnown) male = l.Male;
                    sexKnown = true;
                    race = CanonRace(l.Race);
                    if (race != null) { from = "look"; return; }
                }
            }
            catch (Exception e)
            {
                ScLog.Once("voice-race", e);
            }
            if (!sexKnown) male = ScLooks.ClassIsMale(ScCommunity.ClassName(npc));
        }

        /// <summary>V2: the name a voice is dealt by: a survivor's name; a raider's name and entity id (raiders share names).</summary>
        private static string Seed(EntityAlive npc, bool raider)
        {
            return ScCommunity.DisplayName(npc) + (raider ? "#" + npc.entityId : "");
        }

        /// <summary>
        /// V2: the voices this NPC speaks in, first choice first (empty: none of their sex at all; null: not one of SC's
        /// survivors or raiders). By race (VoiceByRace and a race known): the two voices of their race and sex, the one
        /// their name picks first; then every other recorded voice of their sex, in an order their name sets (so one person
        /// borrows the same voice for every line). Otherwise every recorded voice of their sex, the one their name picks
        /// first (the pre-V2 deal). Cached per entity while their sex, race and name stay the same.
        /// </summary>
        internal static List<Voice> Chain(EntityAlive npc)
        {
            if (npc == null) return null;
            bool raider = ScXnpc.IsRaider(npc);
            if (!raider && !ScXnpc.IsSurvivorBody(npc)) return null;
            string race, from;
            bool male;
            RaceAndSex(npc, out race, out male, out from);
            string seed = Seed(npc, raider);
            List<Voice> all = Voices();
            string sig = (male ? "M" : "F") + "|" + (race ?? "") + "|" + seed + "|" + ScConfig.VoiceByRace;
            KeyValuePair<string, List<Voice>> cached;
            if (chains.TryGetValue(npc.entityId, out cached) && cached.Key == sig) return cached.Value;
            List<Voice> chain = ChainFor(all, male, race, seed);
            if (chains.Count > 512) chains.Clear();
            chains[npc.entityId] = new KeyValuePair<string, List<Voice>>(sig, chain);
            return chain;
        }

        private static List<Voice> ChainFor(List<Voice> all, bool male, string race, string seed)
        {
            var chain = new List<Voice>();
            if (ScConfig.VoiceByRace && race != null)
            {
                var pool = new List<Voice>();
                foreach (Voice v in all) if (v.Listed && v.Male == male && v.Race == race) pool.Add(v);
                if (pool.Count > 0)
                {
                    int first = (int)(Fnv(seed) % (uint)pool.Count);
                    for (int i = 0; i < pool.Count; i++) chain.Add(pool[(first + i) % pool.Count]);
                }
            }
            var rest = new List<Voice>();
            foreach (Voice v in all) if (v.Male == male && v.Recorded && !chain.Contains(v)) rest.Add(v);
            if (chain.Count == 0)
            {
                // the pre-V2 deal: every recorded voice of the sex by name, the one the name picks first
                rest.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                if (rest.Count == 0) return chain;
                int pick = (int)(Fnv(seed) % (uint)rest.Count);
                for (int i = 0; i < rest.Count; i++) chain.Add(rest[(pick + i) % rest.Count]);
                return chain;
            }
            rest.Sort((a, b) => Fnv(seed + "|" + a.Name).CompareTo(Fnv(seed + "|" + b.Name)));
            chain.AddRange(rest);
            return chain;
        }

        /// <summary>FNV-1a: a stable string hash (string.GetHashCode is not stable across runtimes).</summary>
        private static uint Fnv(string s)
        {
            uint h = 2166136261u;
            if (s != null) foreach (char c in s) { h ^= c; h *= 16777619u; }
            return h;
        }

        /// <summary>
        /// The clip for this line in this NPC's voices (the first voice of the chain that has it), the key it was found
        /// under and that voice; null when the line is not voiced. A line picked at runtime (vo2: lineKey, an enclave
        /// request's Localization key) is looked up by that key first. Then the statement: the dialog in the key is the
        /// statement's own (a child dialog SCore opened has its own id), else the innermost open one.
        /// </summary>
        private static string ClipFor(List<Voice> chain, Dialog dialog, BaseStatement st, string lineKey, out string key, out Voice used)
        {
            key = null;
            used = null;
            if (chain == null || chain.Count == 0 || st == null || string.IsNullOrEmpty(st.ID)) return null;
            Dialog owner = st.OwnerDialog;
            if (owner == null && dialog != null)
            {
                owner = dialog;
                for (int depth = 0; owner.ChildDialog != null && depth < 8; depth++) owner = owner.ChildDialog;
            }
            string ownKey = owner != null && !string.IsNullOrEmpty(owner.ID) ? owner.ID + "." + st.ID : null;
            foreach (Voice v in chain)
            {
                string path;
                if (!string.IsNullOrEmpty(lineKey) && v.Lines.TryGetValue(lineKey, out path)) { key = lineKey; used = v; return path; }
                if (ownKey != null && v.Lines.TryGetValue(ownKey, out path)) { key = ownKey; used = v; return path; }
                if (v.Lines.TryGetValue(st.ID, out path)) { key = st.ID; used = v; return path; }
            }
            return null;
        }

        /// <summary>V2: the first voice of the chain with a clip for any of these keys, and those keys in it (null: none has one).</summary>
        internal static Voice FirstWith(List<Voice> chain, IList<string> keys, List<string> found)
        {
            found.Clear();
            if (chain == null || keys == null) return null;
            foreach (Voice v in chain)
            {
                foreach (string k in keys) if (v.Lines.ContainsKey(k)) found.Add(k);
                if (found.Count > 0) return v;
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
            Voice used;
            string path = ScConfig.VoiceLines ? ClipFor(TalkChain(npc), dialog, st, lineKey, out key, out used) : null;
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
            var found = new List<string>();
            Voice v = FirstWith(TalkChain(npc), new[] { key }, found);
            string path;
            if (v == null || !v.Lines.TryGetValue(key, out path)) return;
            string tag = npc.entityId + "|" + key;
            float at;
            if (recent.TryGetValue(tag, out at) && Time.unscaledTime - at < RepeatSeconds) return;
            Play(npc, tag, path);
            parting = true;   // after Play: its Stop clears the flag
        }

        /// <summary>The voices a talk's line is said in: one of the community's survivors only (a raider is never talked to).</summary>
        private static List<Voice> TalkChain(EntityAlive npc)
        {
            return npc != null && ScXnpc.IsSurvivorBody(npc) ? Chain(npc) : null;
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
            // V2: the voice table is read again for the new world's config; barks, greetings and taunts start over
            voices = null;
            chains.Clear();
            barkUntil.Clear();
            ScBarks.Reset();
            ScGreetings.Reset();
            ScTaunts.Reset();
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
            // V2: SC loads the clip (ScClipCache, LRU) and then plays it through Voice Control's player where there is one -
            // decompressed for that player's lip sync, compressed for SC's own source
            bool forVc = VcPlayer(npc) != null;
            ScClipCache.Get(path, !forVc, clip =>
            {
                try
                {
                    if (my != token || clip == null) return;   // superseded meanwhile, or did not load (logged by the cache)
                    if (npc == null || npc.IsDead()) return;    // Unity null: gone while the clip loaded
                    Component player = forVc ? VcPlayer(npc) : null;
                    AudioClip on = player != null ? PlayOnVc(player, path, clip, null) : null;
                    if (on != null)
                    {
                        playingPlayer = player;
                        Started(id, tag, on);
                    }
                    else PlayOwn(npc, tag, clip);
                    ScClipCache.MarkPlaying(clip);
                }
                catch (Exception e)
                {
                    ScLog.Once("voice-play", e);
                }
            });
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
                if (playingOwn != null && (object)playingOwn.clip == (object)playingClip) playingOwn.Stop();
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
                // V2: its busy state (a bark never starts over Voice Control's speech) and its loader cache (PlayOnVc)
                FieldInfo f = tPlayer.GetField("_isPlaying", BindingFlags.NonPublic | BindingFlags.Instance);
                fIsPlaying = f != null && f.FieldType == typeof(bool) ? f : null;
                f = tPlayer.GetField("_draining", BindingFlags.NonPublic | BindingFlags.Instance);
                fDraining = f != null && f.FieldType == typeof(bool) ? f : null;
                if (fIsPlaying == null || fDraining == null)
                    ScLog.Warn("voice: Voice Control's NPCAudioPlayer._isPlaying / _draining not found (Voice Control update?) - SC's barks may start over its speech");
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

        /// <summary>V2: Voice Control's loader cache (VoiceClipLoader._cache, _lock), bound once; false when it has changed.</summary>
        private static bool BindVcCache()
        {
            if (vcCacheBound) return vcCache != null;
            vcCacheBound = true;
            try
            {
                Type tLoader = ScTypes.Find("XNPCVoiceControl.Core.VoiceClipLoader");
                FieldInfo fCache = tLoader != null ? tLoader.GetField("_cache", BindingFlags.NonPublic | BindingFlags.Static) : null;
                FieldInfo fLock = tLoader != null ? tLoader.GetField("_lock", BindingFlags.NonPublic | BindingFlags.Static) : null;
                vcCache = fCache != null ? fCache.GetValue(null) as Dictionary<string, AudioClip> : null;
                vcCacheLock = fLock != null ? fLock.GetValue(null) : null;
                if (vcCache == null || vcCacheLock == null)
                {
                    vcCache = null;
                    if (tLoader != null)
                        ScLog.Warn("voice: Voice Control's VoiceClipLoader._cache / _lock not found (Voice Control update?) - SC's clips played through its player stay in its cache for the session");
                }
            }
            catch (Exception e)
            {
                vcCache = null;
                ScLog.Once("voice-bind-cache", e);
            }
            return vcCache != null;
        }

        /// <summary>
        /// V2: plays a clip SC loaded on Voice Control's player (lip sync, its volume and range): the clip goes into Voice
        /// Control's loader cache under its path for the one PlayClipFile call - which then finds it and starts it at once,
        /// inside the call - and comes out again, so Voice Control never keeps SC's clips. Without that cache (a changed
        /// Voice Control) its loader loads the file itself and keeps it (vcPaths, sc voice rescan). V1: SelfCall lets the
        /// call through the gate. onComplete: Voice Control calls it when the clip ends (or at once when it cannot play).
        /// Returns the clip now on the player's source (the cache's own copy when Voice Control already had one), or null
        /// when nothing was handed over.
        /// </summary>
        private static AudioClip PlayOnVc(Component player, string path, AudioClip clip, Action onComplete)
        {
            if (player == null || clip == null || !BindVc()) return null;
            object entry = Activator.CreateInstance(tEntry);
            fPath.SetValue(entry, path);
            bool seeded = false;
            if (BindVcCache())
            {
                lock (vcCacheLock)
                {
                    if (!vcCache.ContainsKey(path))
                    {
                        vcCache[path] = clip;
                        seeded = true;
                    }
                }
            }
            else vcPaths.Add(path);
            SelfCall = true;              // V1: the gate's PlayClipFile prefix lets SC's own call through
            try { mPlayClipFile.Invoke(player, new object[] { entry, onComplete }); }
            finally
            {
                SelfCall = false;
                if (seeded)
                {
                    lock (vcCacheLock)
                    {
                        AudioClip c;
                        if (vcCache.TryGetValue(path, out c) && (object)c == (object)clip) vcCache.Remove(path);
                    }
                }
            }
            // started inside the call from the cache (ours or Voice Control's own copy); without the cache its loader is still
            // loading the file, which plays as long as ours
            if (!BindVcCache()) return clip;
            AudioSource src = fSource != null ? fSource.GetValue(player) as AudioSource : null;
            return src != null && src.clip != null ? src.clip : clip;
        }

        /// <summary>V2: Voice Control is saying something on this player (a clip, a sentence, or a queue still draining).</summary>
        private static bool VcBusy(Component player)
        {
            try
            {
                if (player == null) return false;
                if (fIsPlaying != null && (bool)fIsPlaying.GetValue(player)) return true;
                if (fDraining != null && (bool)fDraining.GetValue(player)) return true;
            }
            catch (Exception e)
            {
                ScLog.Once("voice-busy", e);
            }
            return false;
        }

        // ------------------------------------------------------------------ SC's own source (no Voice Control player) ----

        /// <summary>SC's own AudioSource on a child of the NPC (Voice Control's lip sync and relay take the root's first one).</summary>
        private static AudioSource OwnSource(EntityAlive npc)
        {
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
            return src;
        }

        private static void PlayOwn(EntityAlive npc, string tag, AudioClip clip)
        {
            if (npc == null || npc.IsDead()) return;   // Unity null: gone while the clip loaded
            AudioSource src = OwnSource(npc);
            src.clip = clip;
            src.Play();
            playingOwn = src;
            Started(npc.entityId, tag, clip);
        }

        // ------------------------------------------------------------------ V2: barks ----

        /// <summary>V2: the talk is open on this NPC, or SC's talk line still plays on them: no bark on them.</summary>
        public static bool Holds(EntityAlive npc)
        {
            if ((object)npc == null) return false;
            int id = npc.entityId;
            return id == talkingTo || (id == playingEntity && ClipMsLeft() > 0);
        }

        /// <summary>V2: SC's talk window is open (on anyone).</summary>
        public static bool TalkOpen { get { return talkingTo >= 0; } }

        /// <summary>V2: a bark of SC's is still playing on this NPC, or Voice Control is saying something on them.</summary>
        public static bool Busy(EntityAlive npc)
        {
            if ((object)npc == null) return false;
            float until;
            if (barkUntil.TryGetValue(npc.entityId, out until) && Time.unscaledTime < until) return true;
            return VcBusy(VcPlayer(npc));
        }

        /// <summary>V2: Voice Control is saying something on this NPC.</summary>
        public static bool VcBusy(EntityAlive npc)
        {
            return VcBusy(VcPlayer(npc));
        }

        /// <summary>
        /// V2: plays a bark's clip on an NPC: through Voice Control's player on it when lipSync and it has one (a survivor
        /// near the player), else from SC's own source on a child (a raider, or no player). allowDead: a dying raider's
        /// last words. started gets the clip once it plays; onComplete runs when it ends, or at once when it cannot play.
        /// </summary>
        internal static void PlayBark(EntityAlive npc, string path, bool lipSync, bool allowDead, Action<AudioClip> started, Action onComplete)
        {
            if (npc == null || string.IsNullOrEmpty(path)) { onComplete?.Invoke(); return; }
            int id = npc.entityId;
            barkUntil[id] = Time.unscaledTime + 1f;          // while it loads: a second bark on them waits
            bool forVc = lipSync && VcPlayer(npc) != null;   // decompressed for Voice Control's lip sync, else compressed
            ScClipCache.Get(path, !forVc, clip =>
            {
                bool handed = false;
                try
                {
                    if (clip == null || npc == null || (!allowDead && npc.IsDead()) || npc.transform == null)
                    {
                        barkUntil.Remove(id);
                        return;
                    }
                    Component player = forVc ? VcPlayer(npc) : null;
                    AudioClip on = player != null ? PlayOnVc(player, path, clip, onComplete) : null;
                    if (on != null) handed = true;
                    else
                    {
                        AudioSource src = OwnSource(npc);
                        src.clip = clip;
                        src.Play();
                        if (onComplete != null)
                        {
                            GameManager gm = GameManager.Instance;
                            if (gm != null) { gm.StartCoroutine(After(clip.length, onComplete)); handed = true; }
                        }
                    }
                    ScClipCache.MarkPlaying(clip);
                    barkUntil[id] = Time.unscaledTime + clip.length;
                    started?.Invoke(clip);
                }
                catch (Exception e)
                {
                    ScLog.Once("voice-bark-play", e);
                }
                finally
                {
                    if (!handed && onComplete != null)
                    {
                        try { onComplete(); }
                        catch (Exception e) { ScLog.Once("voice-bark-complete", e); }
                    }
                }
            });
        }

        private static IEnumerator After(float seconds, Action then)
        {
            yield return new WaitForSeconds(seconds);
            try { then(); }
            catch (Exception e) { ScLog.Once("voice-bark-after", e); }
        }

        // ------------------------------------------------------------------ console ----

        /// <summary>sc voice: the switches, the player in use, the voices found and the clip cache.</summary>
        public static string Status()
        {
            Voices();
            bool vc = BindVc();
            return "DialogLine=" + ScConfig.DialogLine + " VoiceLines=" + ScConfig.VoiceLines + " VoiceByRace=" + ScConfig.VoiceByRace + "; player: "
                   + (vc ? "Voice Control's where it has one (lip sync), else SC's own" : "SC's own (no lip sync)") + "; voices: " + Describe()
                   + "; clips: " + ScClipCache.Describe();
        }

        /// <summary>sc voice rescan: read Resources/Voices again and forget loaded clips, so new or replaced files play without a restart.</summary>
        public static string Rescan()
        {
            Stop();
            voices = null;
            chains.Clear();
            int cleared = ScClipCache.Clear();
            recent.Clear();
            int dropped = 0;
            try
            {
                // Voice Control caches clips by path for the session: drop the ones its loader loaded for SC so a replaced file reloads
                if (BindVcCache())
                {
                    lock (vcCacheLock)
                    {
                        foreach (string k in vcPaths) if (vcCache.Remove(k)) dropped++;
                    }
                }
                vcPaths.Clear();
            }
            catch (Exception e)
            {
                ScLog.Once("voice-rescan", e);
            }
            Voices();
            return Describe() + "; " + cleared + " loaded clip(s) forgotten" + (dropped > 0 ? "; " + dropped + " cached clip(s) dropped from Voice Control" : "");
        }

        /// <summary>sc voice play: this survivor says this line now, repeat rule or not (the key is a clip's name), in the first voice of their chain that has it.</summary>
        public static string PlayLine(EntityAlive npc, string key)
        {
            if (npc == null) return "no survivor";
            List<Voice> chain = TalkChain(npc);
            if (chain == null || chain.Count == 0) return ScCommunity.DisplayName(npc) + " has no voice (not one of the community's survivors, or no voice of their sex)";
            var found = new List<string>();
            Voice v = FirstWith(chain, new[] { key }, found);
            string path;
            if (v == null || !v.Lines.TryGetValue(key, out path))
                return "none of " + ScCommunity.DisplayName(npc) + "'s voices (" + Names(chain, 4) + ") has a line " + key;
            Play(npc, npc.entityId + "|" + key, path);
            return ScCommunity.DisplayName(npc) + " says " + key + " in " + v.Name + (v == chain[0] ? "" : " (their own voice, " + chain[0].Name + ", has no clip for it)");
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
                if (sq >= bestSq) continue;
                List<Voice> chain = Chain(e);
                if (chain != null && chain.Count > 0) { best = e; bestSq = sq; }
            }
            return best;
        }

        private static string Names(List<Voice> chain, int max)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < chain.Count && i < max; i++) sb.Append(i > 0 ? ", " : "").Append(chain[i].Name);
            if (chain.Count > max) sb.Append(", ...");
            return sb.ToString();
        }

        /// <summary>V2, sc voice who: how this NPC's voice is chosen, the chain, and which bark families it can say.</summary>
        public static List<string> Who(EntityAlive npc)
        {
            var o = new List<string>();
            if (npc == null) { o.Add("no NPC"); return o; }
            bool raider = ScXnpc.IsRaider(npc);
            string who = ScCommunity.DisplayName(npc) + " (" + npc.entityId + ")";
            if (!raider && !ScXnpc.IsSurvivorBody(npc))
            {
                o.Add(who + " is not one of the community's survivors or SC's raiders - SC gives them no voice");
                return o;
            }
            string race, from;
            bool male;
            RaceAndSex(npc, out race, out male, out from);
            List<Voice> chain = Chain(npc);
            o.Add(who + ", " + (raider ? "raider" : "survivor") + ": " + (race ?? "race unknown") + " " + (male ? "male" : "female") + " (from the "
                  + (from == "model" ? "body's archetype" : from == "look" ? "saved look" : "class name") + "), VoiceByRace " + (ScConfig.VoiceByRace ? "on" : "off")
                  + (raider ? ", dealt by name and id" : ", dealt by name"));
            if (chain == null || chain.Count == 0) { o.Add("  no voice of their sex in the table or on disk"); return o; }
            var sb = new StringBuilder("  voice: ");
            for (int i = 0; i < chain.Count; i++)
            {
                Voice v = chain[i];
                if (i == 1) sb.Append(" | then ");
                else if (i > 1) sb.Append(", ");
                sb.Append(v.Name).Append(" (").Append(v.Recorded ? v.Lines.Count + " clips" : v.OnDisk ? "no clips yet" : "no folder yet").Append(')');
            }
            o.Add(sb.ToString());
            Voice talk = null;
            foreach (Voice v in chain) if (v.Recorded) { talk = v; break; }
            o.Add("  " + (raider ? "taunts" : "talk lines") + " come from the first voice above with the line's clip"
                  + (talk == null ? ": none has any clip yet - silent" : talk == chain[0] ? "" : ": their own voice has none yet, so " + talk.Name + " speaks for now"));
            o.Add("  " + ScBarks.FamilyReport(npc, chain, raider));
            return o;
        }

        /// <summary>V2: the family report's lookups (ScBarks): does a voice have this clip, and its path.</summary>
        internal static bool HasClip(Voice v, string key, out string path)
        {
            path = null;
            return v != null && v.Lines.TryGetValue(key, out path);
        }
    }
}
