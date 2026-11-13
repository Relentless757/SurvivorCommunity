using System;
using System.Collections.Generic;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round 31 (V31): the config keys of skin-matched voices and hair colors, kept apart from ScConfig.cs's long lists as
    /// ScConfigNames. With Vanity Vault a body's race is one of its skin tones (Alabaster ... Espresso), not one of the voice
    /// table's four races: VoiceRaceBySkin maps each tone to the race whose voices it speaks in (ScVoice.RaceFor). And the
    /// natural hair colors follow the skin: a mid or a dark skin is rolled (and an existing look corrected to) only the natural
    /// colors its group allows (ScLooks.NaturalPick, ScLooks.FixHairColor), gray and white (SurvivorGrayHairColors) kept at
    /// their share of all the natural colors; dyed colors stay open to every skin.
    /// Round 32c (F32): with Vanity Vault the TONE sets how light or dark the skin is and the FACE (the variant, 1-16: 1-4
    /// White, 5-8 Native, 9-12 Black, 13-16 Asian - Vanity Vault's SonjaFaceDecoupler) sets the features a player reads as
    /// ethnicity. So the voice race comes from the face (VoiceRaceByFace; a White or Asian face with a dark tone of its sex,
    /// VoiceDarkTonesMale / Female, speaks with a Black voice; VoiceRaceBySkin only for a variant outside 1-16), the natural
    /// hair group is the darker of the face's (HairGroupByFace) and the tone's per-sex level (SurvivorMidSkinsMale ...
    /// SurvivorDarkSkinsFemale, which replace round 31's two shared lists: the tables of the r32vv investigation put some tones
    /// at another level for each sex), and a new survivor is given its face first and a tone that suits it
    /// (SurvivorTonesByFace, ScLooks.Roll). Config/blocks.xml, block ConfigSurvivorCommunity: the voice keys after Voices, the
    /// hair and tone keys after SurvivorDyedHairChanceMale; each key is commented there. Both games.
    /// </summary>
    public static partial class ScConfig
    {
        /// <summary>
        /// Vanity Vault's 15 natural skin tones (its GetRaceList postfix: the other 9 are the tinted ones SurvivorExcludeSkins
        /// leaves out) and the voice race each speaks in - the user's picks, 2026-11-12. Round 32c: only the fallback for a
        /// face variant outside 1-16 (or with VoiceRaceByFace off).
        /// </summary>
        public const string DefaultVoiceRaceBySkin = "Alabaster:White,Porcelain:White,Ivory:White,Warm Ivory:White,Cool Beige:White,Warm Beige:White,"
                                                   + "Honey:Asian,Golden Peach:Asian,Sand:Native,Taupe:Native,Caramel:Native,Bronze:Native,"
                                                   + "Chestnut:Black,Umber:Black,Espresso:Black";

        /// <summary>
        /// A skin tone that is not one of the voice table's races (ScVoice.Races) -> the race(s) whose voices it speaks in,
        /// tone names case-insensitive. Several races: each NPC keeps one, picked by a stable hash of their voice seed. A
        /// tone not in here speaks in any voice of its sex, as before round 31. The four races themselves are never mapped.
        /// Round 32c: asked only when the face gives no race (ScVoice.RaceFor).
        /// </summary>
        public static readonly Dictionary<string, string[]> VoiceRaceBySkin = SkinRaces(DefaultVoiceRaceBySkin);

        /// <summary>
        /// Round 32c: a Vanity Vault tone speaks in the voices of its FACE's race (the variant: ScLooks.FaceRace), a White or
        /// Asian face with a dark tone of its sex (VoiceDarkTonesMale / Female) in Black ones. False: the tone alone
        /// (VoiceRaceBySkin), as round 31.
        /// </summary>
        public static bool VoiceRaceByFace = true;

        // Declared array first, list after: static fields initialize in text order (round 26's H1).
        /// <summary>Round 32c: a man's tones that make a White or Asian face speak with a Black voice (the r32vv tables' dark men).</summary>
        public static readonly string[] DefaultVoiceDarkTonesMale = { "Bronze", "Umber", "Espresso" };
        public static readonly List<string> VoiceDarkTonesMale = new List<string>(DefaultVoiceDarkTonesMale);
        /// <summary>Round 32c: a woman's tones that make a White or Asian face speak with a Black voice.</summary>
        public static readonly string[] DefaultVoiceDarkTonesFemale = { "Chestnut", "Umber", "Espresso" };
        public static readonly List<string> VoiceDarkTonesFemale = new List<string>(DefaultVoiceDarkTonesFemale);

        /// <summary>
        /// Round 32c: a Vanity Vault face's race narrows the natural hair colors too - the group is the darker of the face's
        /// (White any, Asian and Native mid, Black dark) and the tone's per-sex level (ScLooks.AllowedHairColors). False: the
        /// tone's level alone.
        /// </summary>
        public static bool HairGroupByFace = true;

        // Natural hair colors by skin, per sex since round 32c (the tones' apparent lightness differs by sex: r32vv tables.md).
        /// <summary>A man's skins (Vanity Vault tones or vanilla races; a trailing '*' matches a prefix) whose natural hair colors are SurvivorMidSkinHairColors.</summary>
        public static readonly string[] DefaultMidSkinsMale = { "Warm Ivory", "Cool Beige", "Warm Beige", "Honey", "Golden Peach", "Taupe", "Caramel", "Chestnut", "Asian", "Native" };
        public static readonly List<string> SurvivorMidSkinsMale = new List<string>(DefaultMidSkinsMale);
        /// <summary>A man's skins whose natural hair colors are SurvivorDarkSkinHairColors (asked before the mid ones).</summary>
        public static readonly string[] DefaultDarkSkinsMale = { "Bronze", "Umber", "Espresso", "Black" };
        public static readonly List<string> SurvivorDarkSkinsMale = new List<string>(DefaultDarkSkinsMale);
        /// <summary>A woman's mid skins.</summary>
        public static readonly string[] DefaultMidSkinsFemale = { "Cool Beige", "Warm Beige", "Honey", "Golden Peach", "Sand", "Taupe", "Caramel", "Bronze", "Asian", "Native" };
        public static readonly List<string> SurvivorMidSkinsFemale = new List<string>(DefaultMidSkinsFemale);
        /// <summary>A woman's dark skins (asked before the mid ones).</summary>
        public static readonly string[] DefaultDarkSkinsFemale = { "Chestnut", "Umber", "Espresso", "Black" };
        public static readonly List<string> SurvivorDarkSkinsFemale = new List<string>(DefaultDarkSkinsFemale);
        /// <summary>The natural hair colors a mid skin may have (the game's prefab names: "04 Brown").</summary>
        public static readonly string[] DefaultMidSkinHairColors = { "03 LightBrown", "04 Brown", "05 DarkBrown", "06 Black", "07 Gray", "08 White" };
        public static readonly List<string> SurvivorMidSkinHairColors = new List<string>(DefaultMidSkinHairColors);
        /// <summary>The natural hair colors a dark skin may have.</summary>
        public static readonly string[] DefaultDarkSkinHairColors = { "04 Brown", "05 DarkBrown", "06 Black", "07 Gray", "08 White" };
        public static readonly List<string> SurvivorDarkSkinHairColors = new List<string>(DefaultDarkSkinHairColors);
        /// <summary>
        /// Round 31 refinement: the natural hair colors that read as age. A natural roll is gray or white as often as these are
        /// among all the natural colors (2 of 10: 0.2, as before round 31, on every skin), and a fixed look never turns from a
        /// young-looking color to one of these (ScLooks.NaturalPool, ScLooks.FixedHairColor). Empty: no split by age.
        /// </summary>
        public static readonly string[] DefaultGrayHairColors = { "07 Gray", "08 White" };
        public static readonly List<string> SurvivorGrayHairColors = new List<string>(DefaultGrayHairColors);

        /// <summary>
        /// Round 32c: the Vanity Vault tones a new face of each race is given (ScLooks.Roll: the face first, then one of these,
        /// less SurvivorExcludeSkins) - each set is Vanity Vault's own source materials for that race plus extras the r32vv
        /// investigation measured as no visible conflict on any face of that race, both sexes.
        /// </summary>
        public const string DefaultSurvivorTonesByFace = "White:Porcelain|Alabaster|Cool Beige|Ivory|Warm Ivory,"
                                                       + "Native:Caramel|Honey|Chestnut|Taupe|Golden Peach|Warm Beige,"
                                                       + "Black:Espresso|Umber|Bronze|Warm Beige|Chestnut|Caramel,"
                                                       + "Asian:Ivory|Sand|Golden Peach|Warm Ivory|Porcelain|Alabaster";

        /// <summary>Round 32c: face race (White, Black, Asian, Native) -> the tones a new face of it may get. Empty: tone and face rolled apart, as before.</summary>
        public static readonly Dictionary<string, string[]> SurvivorTonesByFace = FaceTones(DefaultSurvivorTonesByFace);

        private static void ResetSkins()
        {
            VoiceRaceBySkin.Clear();
            ParseSkinRaces(DefaultVoiceRaceBySkin, VoiceRaceBySkin, null);
            VoiceRaceByFace = true;
            VoiceDarkTonesMale.Clear(); VoiceDarkTonesMale.AddRange(DefaultVoiceDarkTonesMale);
            VoiceDarkTonesFemale.Clear(); VoiceDarkTonesFemale.AddRange(DefaultVoiceDarkTonesFemale);
            HairGroupByFace = true;
            SurvivorMidSkinsMale.Clear(); SurvivorMidSkinsMale.AddRange(DefaultMidSkinsMale);
            SurvivorDarkSkinsMale.Clear(); SurvivorDarkSkinsMale.AddRange(DefaultDarkSkinsMale);
            SurvivorMidSkinsFemale.Clear(); SurvivorMidSkinsFemale.AddRange(DefaultMidSkinsFemale);
            SurvivorDarkSkinsFemale.Clear(); SurvivorDarkSkinsFemale.AddRange(DefaultDarkSkinsFemale);
            SurvivorMidSkinHairColors.Clear(); SurvivorMidSkinHairColors.AddRange(DefaultMidSkinHairColors);
            SurvivorDarkSkinHairColors.Clear(); SurvivorDarkSkinHairColors.AddRange(DefaultDarkSkinHairColors);
            SurvivorGrayHairColors.Clear(); SurvivorGrayHairColors.AddRange(DefaultGrayHairColors);
            SurvivorTonesByFace.Clear();
            ParseFaceTones(DefaultSurvivorTonesByFace, SurvivorTonesByFace, null);
        }

        private static void ParseSkins(DynamicProperties p)
        {
            string s;
            if (p.Values.TryGetValue("VoiceRaceBySkin", out s))
            {
                // an empty value empties the map (every tone then speaks in any voice of its sex); a value with no readable
                // Tone:Race entry keeps the defaults, with a warning
                var problems = new List<string>();
                var parsed = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
                int n = ParseSkinRaces(s ?? "", parsed, problems);
                foreach (string w in problems) ScLog.Warn("config VoiceRaceBySkin: " + w);
                if (n == 0 && !string.IsNullOrEmpty(s) && s.Trim().Length > 0)
                    ScLog.Warn("config VoiceRaceBySkin '" + s + "' names no Tone:Race entry - the default map is kept");
                else
                {
                    VoiceRaceBySkin.Clear();
                    foreach (var kv in parsed) VoiceRaceBySkin[kv.Key] = kv.Value;
                }
            }
            ParseBoolKey(p, "VoiceRaceByFace", ref VoiceRaceByFace);        // round 32c
            ParseList(p, "VoiceDarkTonesMale", VoiceDarkTonesMale);           // empty: no override for men
            ParseList(p, "VoiceDarkTonesFemale", VoiceDarkTonesFemale);
            ParseBoolKey(p, "HairGroupByFace", ref HairGroupByFace);        // round 32c
            // an empty list is meaningful for these: no skin in that group / the group's skins keep every natural color
            // (an empty color list falls back to all of them, logged once: ScLooks.NaturalPick)
            ParseList(p, "SurvivorMidSkinsMale", SurvivorMidSkinsMale);       // round 32c: per sex
            ParseList(p, "SurvivorDarkSkinsMale", SurvivorDarkSkinsMale);
            ParseList(p, "SurvivorMidSkinsFemale", SurvivorMidSkinsFemale);
            ParseList(p, "SurvivorDarkSkinsFemale", SurvivorDarkSkinsFemale);
            ParseList(p, "SurvivorMidSkinHairColors", SurvivorMidSkinHairColors);
            ParseList(p, "SurvivorDarkSkinHairColors", SurvivorDarkSkinHairColors);
            ParseList(p, "SurvivorGrayHairColors", SurvivorGrayHairColors);   // empty: no split by age
            // round 31's two shared lists (never in a public release) gave way to the per-sex ones above
            foreach (string old in new[] { "SurvivorMidSkins", "SurvivorDarkSkins" })
                if (p.Values.ContainsKey(old)) ScLog.Warn("config " + old + " is replaced by " + old + "Male and " + old + "Female (round 32c) - ignored");
            if (p.Values.TryGetValue("SurvivorTonesByFace", out s))
            {
                // an empty value empties the map (tone and face rolled apart, as before round 32c); a value with no readable
                // Race:Tone entry keeps the defaults, with a warning
                var problems = new List<string>();
                var parsed = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
                int n = ParseFaceTones(s ?? "", parsed, problems);
                foreach (string w in problems) ScLog.Warn("config SurvivorTonesByFace: " + w);
                if (n == 0 && !string.IsNullOrEmpty(s) && s.Trim().Length > 0)
                    ScLog.Warn("config SurvivorTonesByFace '" + s + "' names no Race:Tone entry - the default sets are kept");
                else
                {
                    SurvivorTonesByFace.Clear();
                    foreach (var kv in parsed) SurvivorTonesByFace[kv.Key] = kv.Value;
                }
            }
        }

        /// <summary>A new map from a VoiceRaceBySkin value (the field's initializer: the defaults, which have no problems).</summary>
        private static Dictionary<string, string[]> SkinRaces(string s)
        {
            var map = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            ParseSkinRaces(s, map, null);
            return map;
        }

        /// <summary>A new map from a SurvivorTonesByFace value (the field's initializer: the defaults, which have no problems).</summary>
        private static Dictionary<string, string[]> FaceTones(string s)
        {
            var map = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            ParseFaceTones(s, map, null);
            return map;
        }

        /// <summary>
        /// "Tone:Race,Tone:Race|Race,..." into the map (a tone named twice: the later entry wins). An entry with no ':', no
        /// tone, a tone that is itself one of the four races (never mapped), or no race of White/Black/Asian/Native (any case)
        /// is skipped, and an unknown race in a list is left out; each is described in problems when one is given. Returns how
        /// many entries were read. Pure (no logging, no game state): the offline test calls it.
        /// </summary>
        internal static int ParseSkinRaces(string s, Dictionary<string, string[]> into, List<string> problems)
        {
            int n = 0;
            if (string.IsNullOrEmpty(s) || into == null) return 0;
            foreach (string part in SplitList(s))
            {
                int c = part.IndexOf(':');
                string tone = c > 0 ? part.Substring(0, c).Trim() : "";
                if (tone.Length == 0)
                {
                    if (problems != null) problems.Add("entry '" + part + "' is not Tone:Race - skipped");
                    continue;
                }
                if (ScVoice.CanonRace(tone) != null)
                {
                    if (problems != null) problems.Add("'" + tone + "' is one of the voice races itself and is never mapped - entry '" + part + "' skipped");
                    continue;
                }
                var races = new List<string>();
                foreach (string r in part.Substring(c + 1).Split('|'))
                {
                    string t = r.Trim();
                    if (t.Length == 0) continue;
                    string canon = ScVoice.CanonRace(t);
                    if (canon == null)
                    {
                        if (problems != null) problems.Add("'" + t + "' in entry '" + part + "' is not White, Black, Asian or Native - left out");
                        continue;
                    }
                    if (!races.Contains(canon)) races.Add(canon);
                }
                if (races.Count == 0)
                {
                    if (problems != null) problems.Add("entry '" + part + "' names no race of White, Black, Asian or Native - skipped");
                    continue;
                }
                into[tone] = races.ToArray();
                n++;
            }
            return n;
        }

        /// <summary>
        /// Round 32c: "Race:Tone|Tone,..." (SurvivorTonesByFace) into the map, keyed by the face race's own spelling (White,
        /// Black, Asian, Native; a race named twice: the later entry wins). An entry with no ':', a race that is not one of the
        /// four, or no tone is skipped, a tone named twice in one entry is kept once; each is described in problems when one
        /// is given. Tones are kept as written (trimmed): the roll matches them case-insensitively against the game's list.
        /// Returns how many entries were read. Pure: the offline test calls it.
        /// </summary>
        internal static int ParseFaceTones(string s, Dictionary<string, string[]> into, List<string> problems)
        {
            int n = 0;
            if (string.IsNullOrEmpty(s) || into == null) return 0;
            foreach (string part in SplitList(s))
            {
                int c = part.IndexOf(':');
                string race = c > 0 ? ScVoice.CanonRace(part.Substring(0, c).Trim()) : null;
                if (race == null)
                {
                    if (problems != null) problems.Add("entry '" + part + "' is not Race:Tone|Tone with a race of White, Black, Asian or Native - skipped");
                    continue;
                }
                var tones = new List<string>();
                foreach (string t0 in part.Substring(c + 1).Split('|'))
                {
                    string t = t0.Trim();
                    if (t.Length == 0) continue;
                    bool dup = false;
                    foreach (string have in tones) if (string.Equals(have, t, StringComparison.OrdinalIgnoreCase)) dup = true;
                    if (!dup) tones.Add(t);
                }
                if (tones.Count == 0)
                {
                    if (problems != null) problems.Add("entry '" + part + "' names no tone - skipped");
                    continue;
                }
                into[race] = tones.ToArray();
                n++;
            }
            return n;
        }

        /// <summary>The config line's skins part: how voices and hair groups are decided, and the face-first roll's sets.</summary>
        private static string SkinsLine()
        {
            return "voice race " + (VoiceRaceByFace ? "by face (a dark tone on a White or Asian face Black: men " + VoiceDarkTonesMale.Count + ", women "
                                                      + VoiceDarkTonesFemale.Count + " tone(s)), else " : "")
                   + "by skin for " + VoiceRaceBySkin.Count + " tone(s); natural hair colors " + (HairGroupByFace ? "by face and skin" : "by skin")
                   + ": men mid " + SurvivorMidSkinsMale.Count + " / dark " + SurvivorDarkSkinsMale.Count + " skin(s), women mid " + SurvivorMidSkinsFemale.Count
                   + " / dark " + SurvivorDarkSkinsFemale.Count + " skin(s) -> mid " + SurvivorMidSkinHairColors.Count + " color(s), dark "
                   + SurvivorDarkSkinHairColors.Count + " color(s), every other skin all of them; gray " + SurvivorGrayHairColors.Count
                   + " color(s) at their share; new faces: " + (SurvivorTonesByFace.Count > 0 ? "tones by face for " + SurvivorTonesByFace.Count + " face race(s)" : "tone and face apart");
        }
    }
}
