using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using HarmonyLib;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round 30 (S30, the user's spec of 2026-11-11): streamer names. A streamer pastes viewers' names into a text file
    /// (StreamerNamesFile, by default %APPDATA%\7DaysToDie\SurvivorCommunity\StreamerNames.txt: outside the mod folder, which
    /// players delete when they update) and SC gives them, in the file's order, to the new bodies it makes (StreamerNamesFor:
    /// survivors, raiders or all), each name once per save, so viewers turn up in the game as people to recruit.
    ///
    /// Where a body's name comes from today: SCore's EntityAliveSDXV4 rolls a first name from its class's Names when it is
    /// built (CopyPropertiesFromEntityClass), saves it with the body (Write's "identity" section) and reads it back on load
    /// (Read); the title (Titles) is rolled at every build and never saved. SC gives a re-made known survivor the record's
    /// name after the spawn (ScSpawn.Identify), and 0-XNPCCore 3.4's creator faces rename a share of fresh bodies in its
    /// World.SpawnEntityInWorld postfix (for our bodies through SC's Patches_Xnpc X17).
    ///
    /// The one choke point is a prefix on World.SpawnEntityInWorld. Every body of SC's classes comes into the world through
    /// it - SC's own (ScSpawn.Create's callers, ScOutpostRisk's raiders), the biome spawner's wild survivors and raiders,
    /// sleepers, a chunk's saved bodies, SCore's NPC placed back from its item - before its name is shown, recorded (SC's
    /// callers capture their records after the spawn), put in the spawn package for a client (built later in the same call)
    /// or met by a creator face (a postfix). Which bodies are new:
    ///  - a body read from a save (a chunk or a game loading, a runner's snapshot) went through EntityAliveSDXV4.Read, inside
    ///    EntityFactory's CompleteEntity, before its spawn: Read's prefix marks it Keep;
    ///  - a known survivor SC re-makes (ScSpawn.Create with a record: ScSpawn.Body, a runner's fresh body) is marked Keep;
    ///  - a stranger SC makes (ScSpawn.Create without a record; ScOutpostRisk's raiders) is marked New with its kind;
    ///  - an unmarked body is a wild one (the biome spawner, a sleeper volume, another mod's spawn) - unless it comes in as
    ///    StaticSpawner: SCore's NPC placed back from its item (ItemActionDeployNPCSDX and its two packages set that before the
    ///    spawn and put the item's name back), which keeps its name.
    /// The mark stays with the body (a ConditionalWeakTable) and is decided once: a second spawn of the same body changes nothing.
    ///
    /// Picking: in the file's order (a streamer can put favorites first), the first name not given in this save that fits
    /// the body's sex (ScLooks.ClassIsMale, as its look); a name with no sex fits anyone. None left: the body keeps its
    /// class's name. Only the first name changes (ScNpc.SetFirstName): a raider stays "... the Raider". A name given goes into
    /// the registry ([community] streamname=, saved within a second by Tick) and never comes again in that save, even after
    /// the NPC died; sc names reset clears the list.
    ///
    /// The file is read again when its time stamp or size changed, checked whenever a name is needed (and by sc names), so
    /// names added mid-stream go to the next body. With the switch on and no file, the file is made with a commented header.
    /// Host only, main thread. Both games.
    /// </summary>
    public static class ScStreamerNames
    {
        /// <summary>The longest name kept (Twitch allows 25).</summary>
        public const int MaxNameLength = 32;
        /// <summary>The folder in the game's user data folder that a relative StreamerNamesFile is read from.</summary>
        public const string FolderName = "SurvivorCommunity";
        /// <summary>The log's word for a body nothing marked: the biome spawner's, a sleeper, another mod's spawn.</summary>
        public const string WildKind = "wild";

        private const int SexAny = -1, SexFemale = 0, SexMale = 1;

        private sealed class Entry
        {
            public string Name;
            public int Sex;
            public int Line;
        }

        /// <summary>What SC said about a body before it was spawned: Kind null = keep its name; Done = decided.</summary>
        private sealed class Mark
        {
            public string Kind;
            public bool Done;
        }

        // ---- the list, as last read
        private static readonly List<Entry> entries = new List<Entry>();
        private static string loadedPath;
        private static bool loadedExists;
        private static DateTime loadedStamp;
        private static long loadedLength = -1;
        private static bool everRead;
        private static string readNote = "not read yet";
        private static int skippedNoName, skippedDuplicate, cutLong, wholeLine;

        // ---- the bodies
        private static ConditionalWeakTable<Entity, Mark> marks = new ConditionalWeakTable<Entity, Mark>();
        /// <summary>Entity ids given a list name this session (X17 keeps them).</summary>
        private static readonly HashSet<int> given = new HashSet<int>();
        /// <summary>The last names given this session, for sc names.</summary>
        private static readonly List<string> recent = new List<string>();
        private static bool saveDue;

        // ---- the used list as a set, rebuilt when the registry's list changes
        private static HashSet<string> usedSet;
        private static List<string> usedFrom;
        private static int usedCount = -1;

        // ---- names that are also text keys (Localization), per read
        private static readonly Dictionary<string, string> textKeys = new Dictionary<string, string>(StringComparer.Ordinal);

        // ---- one-time warnings, per read of the file
        private static bool warnedEmpty, warnedFemale, warnedMale;

        // ---- counters (this world)
        private static int countGiven, countKept, countPlaced, countNotFor, countNoName, countNoRegistry;

        /// <summary>Per world (ScConfig.ResetNames, at GameStarting).</summary>
        public static void Reset()
        {
            entries.Clear();
            loadedPath = null;
            loadedExists = false;
            loadedStamp = default(DateTime);
            loadedLength = -1;
            everRead = false;
            readNote = "not read yet";
            skippedNoName = skippedDuplicate = cutLong = wholeLine = 0;
            marks = new ConditionalWeakTable<Entity, Mark>();
            given.Clear();
            recent.Clear();
            saveDue = false;
            usedSet = null;
            usedFrom = null;
            usedCount = -1;
            textKeys.Clear();
            warnedEmpty = warnedFemale = warnedMale = false;
            countGiven = countKept = countPlaced = countNotFor = countNoName = countNoRegistry = 0;
        }

        // ------------------------------------------------------------------ marks (before the spawn) ----

        /// <summary>A body that keeps its name: read from a save, or a known survivor SC re-makes. Never throws.</summary>
        public static void Keep(Entity e)
        {
            if ((object)e == null) return;
            try
            {
                Mark m;
                if (marks.TryGetValue(e, out m)) { if (!m.Done) m.Kind = null; return; }
                marks.Add(e, new Mark());
            }
            catch (Exception ex)
            {
                ScLog.Once("streamer-names-keep", ex);
            }
        }

        /// <summary>A new body SC made, before it is spawned: it may take a list name then; kind is the log's word. Never throws.</summary>
        public static void New(Entity e, string kind)
        {
            if ((object)e == null) return;
            try
            {
                Mark m;
                if (marks.TryGetValue(e, out m)) return;           // read from a save, or decided already: unchanged
                marks.Add(e, new Mark { Kind = string.IsNullOrEmpty(kind) ? "new" : kind });
            }
            catch (Exception ex)
            {
                ScLog.Once("streamer-names-new", ex);
            }
        }

        /// <summary>Did this body take a list name this session? (X17: a creator face then leaves the name alone.)</summary>
        public static bool Gave(Entity e)
        {
            return (object)e != null && given.Contains(e.entityId);
        }

        // ------------------------------------------------------------------ the spawn ----

        /// <summary>World.SpawnEntityInWorld(Entity), the prefix's target (both games).</summary>
        public static MethodBase SpawnTarget()
        {
            return AccessTools.DeclaredMethod(typeof(World), "SpawnEntityInWorld", new[] { typeof(Entity) });
        }

        /// <summary>
        /// The prefix's work for a V4 body coming into the world (host only): decided once per body; a marked Keep, a body
        /// placed back from its item, one of a class the For setting leaves out or one met before the registry loaded keeps its
        /// name; any other new body of SC's classes takes the next list name that fits.
        /// </summary>
        private static void OnSpawn(EntityAlive e)
        {
            if (!ScCommunity.IsHost || !ScXnpc.IsBody(e)) return;      // IsBody reads the config on first use
            if (!ScConfig.StreamerNames) return;
            Mark m;
            bool marked = marks.TryGetValue(e, out m);
            if (marked && m.Done) return;
            if (!marked) { m = new Mark { Kind = WildKind }; marks.Add(e, m); }
            m.Done = true;
            if (m.Kind == null) { countKept++; return; }
            if (!marked && e.GetSpawnerSource() == EnumSpawnerSource.StaticSpawner)
            {
                // SCore's NPC placed back from its item: its own name comes back from the item right after the spawn
                countPlaced++;
                ScLog.Verbose("streamer names: " + ScCommunity.DisplayName(e) + " (" + e.entityId + ") comes in as StaticSpawner (an NPC placed back from its item?) - keeps its name");
                return;
            }
            if (!Wants(e)) { countNotFor++; return; }
            if (!ScRegistry.Loaded) { countNoRegistry++; return; }
            Give(e, m.Kind);
        }

        /// <summary>Is this body's class one the For setting names: survivors (never a raider class), raiders, or both?</summary>
        private static bool Wants(EntityAlive e)
        {
            bool raider = ScXnpc.IsRaiderClass(e.entityClass);
            bool survivor = !raider && ScXnpc.IsSurvivorClass(e.entityClass);
            switch (ScConfig.StreamerNamesFor)
            {
                case ScConfig.NamesForRaiders: return raider;
                case ScConfig.NamesForAll: return raider || survivor;
                default: return survivor;
            }
        }

        /// <summary>The next fitting name onto the body, into the used list and the log; or a one-time warning when none is left.</summary>
        private static void Give(EntityAlive e, string kind)
        {
            Refresh(false, true);
            string cls = ScCommunity.ClassName(e);
            bool male = ScLooks.ClassIsMale(cls);
            if (entries.Count == 0)
            {
                countNoName++;
                if (!warnedEmpty)
                {
                    warnedEmpty = true;
                    ScLog.Warn("streamer names: the list has no names (" + (loadedPath ?? "no file") + ", " + readNote + ") - new NPCs keep their"
                               + " normal names. Add names, one per line: the file is read again when it changes.");
                }
                return;
            }
            Entry en = NextFor(male, null);
            if (en == null)
            {
                countNoName++;
                if (male ? !warnedMale : !warnedFemale)
                {
                    if (male) warnedMale = true; else warnedFemale = true;
                    ScLog.Warn("streamer names: no unused name left for a " + (male ? "man" : "woman") + " (" + entries.Count + " name(s) on the list, "
                               + Used().Count + " given in this save) - new " + (male ? "men" : "women") + " keep their normal names until you add"
                               + " names to " + loadedPath + " (sc names reset starts the list again)");
                }
                return;
            }
            if (!ScNpc.SetFirstName(e, en.Name)) return;
            ScRegistry.Community.StreamerNamesUsed.Add(en.Name);
            given.Add(e.entityId);
            countGiven++;
            saveDue = true;
            string line = en.Name + " -> " + cls + "/" + kind + " " + e.entityId;
            World w = ScCommunity.World;
            recent.Add(line + (w != null ? " (day " + GameUtils.WorldTimeToDays(w.worldTime) + ")" : ""));
            while (recent.Count > 8) recent.RemoveAt(0);
            ScLog.Out("streamer names: " + line);
        }

        /// <summary>ScTicker, once a second: the names given since the last second go into the save.</summary>
        public static void Tick()
        {
            if (!saveDue) return;
            try
            {
                if (!ScRegistry.Loaded) return;
                saveDue = false;
                ScRegistry.SaveAtomic();
            }
            catch (Exception ex)
            {
                ScLog.Once("streamer-names-save", ex);
            }
        }

        /// <summary>GameStartDone (host): with the switch on, the list is read - and made, with its header, when missing.</summary>
        public static void OnGameStart()
        {
            try
            {
                ScConfig.EnsureParsed();
                if (!ScConfig.StreamerNames) return;
                ScLog.Out("streamer names: on, for " + ScConfig.NamesForText() + ", the list " + (FilePath() ?? "(no valid path)"));
                Refresh(true, true);                    // logs what it read
            }
            catch (Exception ex)
            {
                ScLog.Once("streamer-names-start", ex);
            }
        }

        // ------------------------------------------------------------------ picking ----

        /// <summary>The registry's used names as a case-insensitive set (rebuilt when that list changes).</summary>
        private static HashSet<string> Used()
        {
            List<string> l = ScRegistry.Community.StreamerNamesUsed;
            if (usedSet == null || !ReferenceEquals(usedFrom, l) || usedCount != l.Count)
            {
                usedSet = new HashSet<string>(l, StringComparer.OrdinalIgnoreCase);
                usedFrom = l;
                usedCount = l.Count;
            }
            return usedSet;
        }

        /// <summary>The first entry in the file's order not used in this save, not in skip, fitting the sex and shown as itself.</summary>
        private static Entry NextFor(bool male, HashSet<string> skip)
        {
            HashSet<string> used = Used();
            for (int i = 0; i < entries.Count; i++)
            {
                Entry en = entries[i];
                if (en.Sex != SexAny && (en.Sex == SexMale) != male) continue;
                if (used.Contains(en.Name)) continue;
                if (skip != null && skip.Contains(en.Name)) continue;
                if (ShowsAsOther(en.Name)) continue;
                return en;
            }
            return null;
        }

        /// <summary>
        /// Would this name show as something else? SCore shows an NPC's first name through Localization.Get (case-sensitive),
        /// so a name that is also one of the game's text keys would show that key's text. Such a name is passed over (warned
        /// once per read).
        /// </summary>
        private static bool ShowsAsOther(string name)
        {
            string shown;
            if (!textKeys.TryGetValue(name, out shown))
            {
                shown = null;
                try
                {
                    if (Localization.Exists(name))
                    {
                        string t = Localization.Get(name);
                        if (!string.Equals(t, name, StringComparison.Ordinal)) shown = t ?? "";
                    }
                }
                catch (Exception) { }
                textKeys[name] = shown;
                if (shown != null)
                    ScLog.Warn("streamer names: '" + name + "' is also one of the game's text keys and would show as '" + shown + "' - passed over"
                               + " (write it with other capital letters to use it)");
            }
            return shown != null;
        }

        // ------------------------------------------------------------------ the file ----

        /// <summary>The list's full path (StreamerNamesFile: relative to the SurvivorCommunity user data folder, or absolute), or null.</summary>
        public static string FilePath()
        {
            try
            {
                string f = (ScConfig.StreamerNamesFile ?? "").Trim();
                if (f.Length == 0) f = ScConfig.DefaultStreamerNamesFile;
                f = Environment.ExpandEnvironmentVariables(f);
                if (Path.IsPathRooted(f)) return Path.GetFullPath(f);
                return Path.GetFullPath(Path.Combine(Path.Combine(GameIO.GetUserGameDataDir(), FolderName), f));
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>The header a new list is made with.</summary>
        private static string Header()
        {
            string nl = Environment.NewLine;
            return "# Survivor Community: streamer names" + nl
                   + "#" + nl
                   + "# One name per line. With StreamerNames set to true in the mod's Config/blocks.xml, Survivor Community gives" + nl
                   + "# these names, in this order, to the new survivors it makes (StreamerNamesFor: survivors, raiders or all)." + nl
                   + "# Each name is given once per save. The file is read again when it changes, so you can add names while you play." + nl
                   + "#" + nl
                   + "# Optional sex after a comma: \"Sarah, female\" or \"Mike, male\" (f and m work too). A name without one fits anyone." + nl
                   + "# Lines starting with # are notes; blank lines are skipped. A name keeps letters, digits, spaces and _ - . '" + nl
                   + "# (anything else is dropped) and is cut at " + MaxNameLength + " characters. The same name twice counts once." + nl
                   + "#" + nl
                   + "# Examples (remove the # to use them):" + nl
                   + "# Sarah, female" + nl
                   + "# Mike, male" + nl
                   + "# Alex" + nl;
        }

        /// <summary>
        /// Reads the list again when the file's path, time stamp or size changed (or always: force). create: a missing file is
        /// made with the header first (the switch is on). A file that cannot be read now (an editor saving it) keeps the list
        /// as it was, and is tried again the next time.
        /// </summary>
        private static void Refresh(bool force, bool create)
        {
            string path = FilePath();
            if (path == null)
            {
                if (force || loadedPath != null || !everRead)
                {
                    Clear();
                    loadedPath = null;
                    everRead = true;
                    readNote = "StreamerNamesFile '" + ScConfig.StreamerNamesFile + "' is not a valid path";
                    ScLog.Once("streamer-names-path", "streamer names: " + readNote);
                }
                return;
            }
            FileInfo fi;
            try
            {
                fi = new FileInfo(path);
                if (!fi.Exists && create)
                {
                    Directory.CreateDirectory(fi.DirectoryName);
                    File.WriteAllText(path, Header(), new UTF8Encoding(false));
                    ScLog.Out("streamer names: made " + path + " - put your viewers' names in it, one per line");
                    fi.Refresh();
                }
            }
            catch (Exception ex)
            {
                ScLog.Once("streamer-names-create", "streamer names: could not make " + path + " (" + ex.Message + ")");
                try { fi = new FileInfo(path); }
                catch (Exception) { return; }
            }
            bool exists = fi.Exists;
            DateTime stamp = exists ? fi.LastWriteTimeUtc : default(DateTime);
            long length = exists ? fi.Length : -1;
            if (!force && everRead && path == loadedPath && exists == loadedExists && stamp == loadedStamp && length == loadedLength) return;
            if (!exists)
            {
                Clear();
                Took(path, false, stamp, length, "no file");
                return;
            }
            string[] lines;
            try
            {
                lines = File.ReadAllLines(path);       // UTF-8, or what its byte order mark says (Notepad's "Unicode" too)
            }
            catch (Exception ex)
            {
                readNote = "could not be read (" + ex.Message + ") - the list read before is kept";
                ScLog.Once("streamer-names-read-" + path, "streamer names: " + path + " " + readNote);
                return;
            }
            Clear();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < lines.Length; i++)
            {
                int sex;
                bool cut, whole;
                string name = ParseLine(lines[i], out sex, out cut, out whole);
                if (name == null) continue;            // a note or a blank line
                if (whole) wholeLine++;
                if (name.Length == 0) { skippedNoName++; continue; }
                if (cut) cutLong++;
                if (!seen.Add(name)) { skippedDuplicate++; continue; }
                entries.Add(new Entry { Name = name, Sex = sex, Line = i + 1 });
            }
            Took(path, true, stamp, length, "read " + DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
            ScLog.Out("streamer names: read " + path + ": " + Summary());
        }

        /// <summary>Empties the list and its per-read state (the warnings come again for the new list).</summary>
        private static void Clear()
        {
            entries.Clear();
            textKeys.Clear();
            skippedNoName = skippedDuplicate = cutLong = wholeLine = 0;
            warnedEmpty = warnedFemale = warnedMale = false;
        }

        private static void Took(string path, bool exists, DateTime stamp, long length, string note)
        {
            loadedPath = path;
            loadedExists = exists;
            loadedStamp = stamp;
            loadedLength = length;
            everRead = true;
            readNote = note;
        }

        /// <summary>
        /// One line of the file: null for a blank line or a note (#); else the name as SC keeps it (CleanName; empty when
        /// nothing is left of it) and its sex (-1 anyone, 0 female, 1 male) from a sex word after the last comma. whole: the
        /// line has a comma with no sex word after it ("Smith, John"), so the whole line is the name (its commas dropped).
        /// </summary>
        private static string ParseLine(string line, out int sex, out bool cut, out bool whole)
        {
            sex = SexAny;
            cut = false;
            whole = false;
            string raw = (line ?? "").Trim();
            if (raw.Length == 0 || raw[0] == '#') return null;
            string namePart = raw;
            int comma = raw.LastIndexOf(',');
            if (comma >= 0)
            {
                int s = SexWord(raw.Substring(comma + 1));
                if (s != int.MinValue) { sex = s; namePart = raw.Substring(0, comma); }
                else whole = true;
            }
            return CleanName(namePart, out cut);
        }

        /// <summary>The sex word after a name's comma: female / f, male / m (any case); empty = anyone; int.MinValue: not a sex word.</summary>
        private static int SexWord(string s)
        {
            switch ((s ?? "").Trim().ToLowerInvariant())
            {
                case "": return SexAny;
                case "female": case "f": return SexFemale;
                case "male": case "m": return SexMale;
                default: return int.MinValue;
            }
        }

        /// <summary>
        /// A name as SC keeps it: letters, digits, '_', '-', '.' and '\'' kept; runs of white space one space; everything else
        /// dropped - the registry's separators ('=', ';', ':', ',', '|'), the UI's color-code brackets ('[' ']'), quotes,
        /// braces, control characters and every character outside the basic plane (emoji). Empty when no letter or digit is
        /// left. Cut at MaxNameLength.
        /// </summary>
        private static string CleanName(string s, out bool cut)
        {
            cut = false;
            var sb = new StringBuilder(s != null ? s.Length : 0);
            bool space = false, any = false;
            if (s != null)
            {
                foreach (char ch in s)
                {
                    bool letter = char.IsLetterOrDigit(ch);
                    if (letter || ch == '_' || ch == '-' || ch == '.' || ch == '\'')
                    {
                        if (space && sb.Length > 0) sb.Append(' ');
                        space = false;
                        sb.Append(ch);
                        any |= letter;
                    }
                    else if (char.IsWhiteSpace(ch)) space = true;
                }
            }
            if (!any) return "";
            string r = sb.ToString();
            if (r.Length > MaxNameLength)
            {
                r = r.Substring(0, MaxNameLength).TrimEnd(new[] { ' ' });      // single spaces are the only white space left
                cut = true;
            }
            return r;
        }

        // ------------------------------------------------------------------ status ----

        private static int CountSex(int sex)
        {
            int n = 0;
            foreach (Entry en in entries) if (en.Sex == sex) n++;
            return n;
        }

        /// <summary>How many unused names a man's (male) or a woman's body could still get.</summary>
        private static int LeftFor(bool male)
        {
            HashSet<string> used = ScRegistry.Loaded ? Used() : new HashSet<string>();
            int n = 0;
            foreach (Entry en in entries)
                if ((en.Sex == SexAny || (en.Sex == SexMale) == male) && !used.Contains(en.Name)) n++;
            return n;
        }

        /// <summary>One line: the list's counts, what was skipped, used and left.</summary>
        private static string Summary()
        {
            var sb = new StringBuilder();
            sb.Append(entries.Count).Append(" name(s): ").Append(CountSex(SexFemale)).Append(" for a woman, ").Append(CountSex(SexMale))
              .Append(" for a man, ").Append(CountSex(SexAny)).Append(" for anyone");
            if (skippedDuplicate > 0) sb.Append("; ").Append(skippedDuplicate).Append(" duplicate(s) skipped");
            if (skippedNoName > 0) sb.Append("; ").Append(skippedNoName).Append(" line(s) with no letter or digit skipped");
            if (cutLong > 0) sb.Append("; ").Append(cutLong).Append(" cut at ").Append(MaxNameLength).Append(" characters");
            if (wholeLine > 0) sb.Append("; ").Append(wholeLine).Append(" line(s) with a comma not followed by male/female (taken whole)");
            if (ScRegistry.Loaded)
            {
                HashSet<string> used = Used();
                int onList = 0;
                foreach (Entry en in entries) if (used.Contains(en.Name)) onList++;
                sb.Append("; given in this save: ").Append(ScRegistry.Community.StreamerNamesUsed.Count).Append(" (").Append(onList)
                  .Append(" of them on this list); left: ").Append(entries.Count - onList).Append(" (a woman could get ").Append(LeftFor(false))
                  .Append(", a man ").Append(LeftFor(true)).Append(')');
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ console ----

        /// <summary>sc names [reload | reset | test [n] [male|female]].</summary>
        public static void ConsoleCmd(List<string> p, Action<string> output)
        {
            try
            {
                ScConfig.EnsureParsed();
                string sub = p.Count > 1 ? p[1].ToLowerInvariant() : "";
                switch (sub)
                {
                    case "":
                    case "status":
                        Refresh(false, ScConfig.StreamerNames);
                        Status(output);
                        return;
                    case "reload":
                        Refresh(true, ScConfig.StreamerNames);
                        output("[SC] streamer names: read again - " + (loadedPath ?? "no valid path") + " (" + readNote + ")");
                        output("[SC]   " + Summary());
                        return;
                    case "reset":
                    {
                        if (!ScCommunity.IsHost || !ScRegistry.Loaded) { output("[SC] streamer names: host only, with the save loaded"); return; }
                        List<string> used = ScRegistry.Community.StreamerNamesUsed;
                        int n = used.Count;
                        used.Clear();
                        warnedEmpty = warnedFemale = warnedMale = false;
                        saveDue = false;
                        ScRegistry.SaveAtomic();
                        ScLog.Out("streamer names: the used list was cleared (" + n + " name(s)) - sc names reset");
                        output("[SC] streamer names: the used list is cleared (" + n + " name(s)): every name on the list can be given again in"
                               + " this save. Bodies that have a list name keep it.");
                        return;
                    }
                    case "test":
                        Test(p, output);
                        return;
                    default:
                        output("[SC] sc names [reload | reset | test [n] [male|female]] - streamer names: status, read the file again, clear the"
                               + " used list, the next names that would be given");
                        return;
                }
            }
            catch (Exception ex)
            {
                ScLog.Once("streamer-names-console", ex);
                output("[SC] streamer names: failed (" + ex.GetType().Name + ": " + ex.Message + ")");
            }
        }

        private static void Status(Action<string> output)
        {
            output("[SC] streamer names: " + (ScConfig.StreamerNames ? "ON (StreamerNames true), for " + ScConfig.NamesForText() + " (StreamerNamesFor)"
                                                                    : "OFF (StreamerNames false in Config/blocks.xml): no body is renamed")
                   + (ScCommunity.IsHost ? "" : " - names are given on the host only"));
            string file = loadedPath ?? FilePath();
            string state = file == null ? "not a valid path (StreamerNamesFile '" + ScConfig.StreamerNamesFile + "')"
                         : loadedExists ? "there, " + loadedLength + " bytes, changed " + loadedStamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + ", " + readNote
                         : "not there" + (ScConfig.StreamerNames ? " (it could not be made)" : " - it is made when StreamerNames is true and a world starts");
            output("[SC]   file: " + (file ?? "-") + " - " + state);
            output("[SC]   list: " + Summary());
            output("[SC]   this session: " + countGiven + " given; kept their own name: " + countKept + " read from a save or re-made from a record, "
                   + countPlaced + " placed back from an item; not for this setting: " + countNotFor + "; no fitting name left: " + countNoName
                   + (countNoRegistry > 0 ? "; before the save's records loaded: " + countNoRegistry : ""));
            if (recent.Count > 0)
            {
                output("[SC]   last given:");
                for (int i = recent.Count - 1; i >= 0 && i >= recent.Count - 5; i--) output("[SC]     " + recent[i]);
            }
            else if (ScRegistry.Loaded && ScRegistry.Community.StreamerNamesUsed.Count > 0)
            {
                List<string> used = ScRegistry.Community.StreamerNamesUsed;
                var last = new List<string>();
                for (int i = used.Count - 1; i >= 0 && last.Count < 5; i--) last.Add(used[i]);
                output("[SC]   last given in this save (none this session): " + string.Join(", ", last.ToArray()));
            }
        }

        /// <summary>sc names test [n] [male|female]: the next names a woman's and a man's body would get, nothing used.</summary>
        private static void Test(List<string> p, Action<string> output)
        {
            int n = 5;
            int only = SexAny;
            for (int i = 2; i < p.Count; i++)
            {
                string a = p[i].ToLowerInvariant();
                int v;
                if (int.TryParse(a, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) n = Math.Max(1, Math.Min(100, v));
                else if (a == "male" || a == "m" || a == "man" || a == "men") only = SexMale;
                else if (a == "female" || a == "f" || a == "woman" || a == "women") only = SexFemale;
                else { output("[SC] sc names test [n] [male|female] - '" + p[i] + "' is neither a number nor a sex"); return; }
            }
            Refresh(false, ScConfig.StreamerNames);
            if (!ScRegistry.Loaded) { output("[SC] streamer names: the save's records are not loaded"); return; }
            output("[SC] streamer names, the next " + n + " (nothing is given; the file's order, names given in this save skipped):");
            if (only != SexMale) output("[SC]   for a woman: " + Peek(false, n));
            if (only != SexFemale) output("[SC]   for a man: " + Peek(true, n));
            if (!ScConfig.StreamerNames) output("[SC]   (StreamerNames is false: none of these is given until it is true)");
        }

        private static string Peek(bool male, int n)
        {
            var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var names = new List<string>();
            while (names.Count < n)
            {
                Entry en = NextFor(male, skip);
                if (en == null) break;
                skip.Add(en.Name);
                names.Add(en.Name + (en.Sex == SexAny ? "" : en.Sex == SexMale ? " (m)" : " (f)"));
            }
            return names.Count > 0 ? string.Join(", ", names.ToArray()) + (names.Count < n ? " - and then none left" : "") : "none left";
        }

        // ------------------------------------------------------------------ patches ----

        /// <summary>
        /// A body read from a save keeps its name: the prefix of SCore's V4 load (Patches_Xnpc.ReadTarget, the method X13's
        /// postfix is on; ModApi lists it once) marks it Keep before the read, so a body whose read fails part way keeps its name
        /// too. Host only (a client's bodies come from the server's spawn packages). Without this hook a saved body could not
        /// be told from a new one, so no list name is given at all (the spawn prefix checks Bound).
        /// </summary>
        [HarmonyPatch]
        public static class Patch_ReadKeepsName
        {
            internal static bool Bound;

            [HarmonyPrepare]
            public static bool Prepare()
            {
                Bound = Patches_Xnpc.ReadTarget() != null;
                if (!Bound) ScLog.Err("streamer names: EntityAliveSDXV4.Read not found (SCore update?) - no streamer names are given: a body loaded from a save could not be told from a new one");
                return Bound;
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return Patches_Xnpc.ReadTarget(); }

            [HarmonyPrefix]
            public static void Prefix(EntityAlive __instance)
            {
                try
                {
                    if (ScCommunity.IsHost) Keep(__instance);
                }
                catch (Exception ex)
                {
                    ScLog.Once("streamer-names-read", ex);
                }
            }
        }

        /// <summary>
        /// The choke point: every entity the world takes passes here; only an EntityAliveSDXV4 goes further (a type test - every
        /// zombie, item and falling block comes through). A prefix, so the name is in place before the rest of the call puts the
        /// body on the map, builds its spawn package for clients and runs the other mods' postfixes (0-XNPCCore's creator face).
        /// </summary>
        [HarmonyPatch]
        public static class Patch_SpawnGivesName
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                if (SpawnTarget() != null) return true;
                ScLog.Err("streamer names: World.SpawnEntityInWorld(Entity) not found (game update?) - no streamer names are given");
                return false;
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return SpawnTarget(); }

            [HarmonyPrefix]
            public static void Prefix(Entity __0)
            {
                if (!(__0 is EntityAliveSDXV4)) return;
                try
                {
                    if (Patch_ReadKeepsName.Bound) OnSpawn((EntityAlive)__0);
                }
                catch (Exception ex)
                {
                    ScLog.Once("streamer-names-spawn", ex);
                }
            }
        }
    }
}
