using System;
using System.Collections.Generic;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round 31 (V31): the config keys of skin-matched voices and hair colors, kept apart from ScConfig.cs's long lists as
    /// ScConfigNames. With Vanity Vault a body's race is one of its skin tones (Alabaster ... Espresso), not one of the voice
    /// table's four races: VoiceRaceBySkin maps each tone to the race whose voices it speaks in (ScVoice.RaceFor). And the
    /// natural hair colors follow the skin: a mid or a dark skin is rolled (and an existing look corrected to) only the natural
    /// colors its group allows (ScLooks.NaturalFor, ScLooks.FixHairColor); dyed colors stay open to every skin. Config/
    /// blocks.xml, block ConfigSurvivorCommunity: VoiceRaceBySkin after Voices, the hair keys after SurvivorDyedHairChanceMale;
    /// each key is commented there. Both games.
    /// </summary>
    public static partial class ScConfig
    {
        /// <summary>
        /// Vanity Vault's 15 natural skin tones (its GetRaceList postfix: the other 9 are the tinted ones SurvivorExcludeSkins
        /// leaves out) and the voice race each speaks in - the user's picks, 2026-11-12.
        /// </summary>
        public const string DefaultVoiceRaceBySkin = "Alabaster:White,Porcelain:White,Ivory:White,Warm Ivory:White,Cool Beige:White,Warm Beige:White,"
                                                   + "Honey:Asian,Golden Peach:Asian,Sand:Native,Taupe:Native,Caramel:Native,Bronze:Native,"
                                                   + "Chestnut:Black,Umber:Black,Espresso:Black";

        /// <summary>
        /// A skin tone that is not one of the voice table's races (ScVoice.Races) -> the race(s) whose voices it speaks in,
        /// tone names case-insensitive. Several races: each NPC keeps one, picked by a stable hash of their voice seed. A
        /// tone not in here speaks in any voice of its sex, as before round 31. The four races themselves are never mapped.
        /// </summary>
        public static readonly Dictionary<string, string[]> VoiceRaceBySkin = SkinRaces(DefaultVoiceRaceBySkin);

        // Natural hair colors by skin. Declared array first, list after: static fields initialize in text order (round 26's H1).
        /// <summary>Skins (Vanity Vault tones or vanilla races; a trailing '*' matches a prefix) whose natural hair colors are SurvivorMidSkinHairColors.</summary>
        public static readonly string[] DefaultMidSkins = { "Honey", "Golden Peach", "Sand", "Taupe", "Caramel", "Bronze", "Asian", "Native" };
        public static readonly List<string> SurvivorMidSkins = new List<string>(DefaultMidSkins);
        /// <summary>The natural hair colors a mid skin may have (the game's prefab names: "04 Brown").</summary>
        public static readonly string[] DefaultMidSkinHairColors = { "03 LightBrown", "04 Brown", "05 DarkBrown", "06 Black", "07 Gray", "08 White" };
        public static readonly List<string> SurvivorMidSkinHairColors = new List<string>(DefaultMidSkinHairColors);
        /// <summary>Skins whose natural hair colors are SurvivorDarkSkinHairColors (asked before the mid ones).</summary>
        public static readonly string[] DefaultDarkSkins = { "Chestnut", "Umber", "Espresso", "Black" };
        public static readonly List<string> SurvivorDarkSkins = new List<string>(DefaultDarkSkins);
        /// <summary>The natural hair colors a dark skin may have.</summary>
        public static readonly string[] DefaultDarkSkinHairColors = { "04 Brown", "05 DarkBrown", "06 Black", "07 Gray", "08 White" };
        public static readonly List<string> SurvivorDarkSkinHairColors = new List<string>(DefaultDarkSkinHairColors);

        private static void ResetSkins()
        {
            VoiceRaceBySkin.Clear();
            ParseSkinRaces(DefaultVoiceRaceBySkin, VoiceRaceBySkin, null);
            SurvivorMidSkins.Clear(); SurvivorMidSkins.AddRange(DefaultMidSkins);
            SurvivorMidSkinHairColors.Clear(); SurvivorMidSkinHairColors.AddRange(DefaultMidSkinHairColors);
            SurvivorDarkSkins.Clear(); SurvivorDarkSkins.AddRange(DefaultDarkSkins);
            SurvivorDarkSkinHairColors.Clear(); SurvivorDarkSkinHairColors.AddRange(DefaultDarkSkinHairColors);
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
            // an empty list is meaningful for these: no skin in that group / the group's skins keep every natural color
            // (an empty color list falls back to all of them, logged once: ScLooks.NaturalFor)
            ParseList(p, "SurvivorMidSkins", SurvivorMidSkins);
            ParseList(p, "SurvivorMidSkinHairColors", SurvivorMidSkinHairColors);
            ParseList(p, "SurvivorDarkSkins", SurvivorDarkSkins);
            ParseList(p, "SurvivorDarkSkinHairColors", SurvivorDarkSkinHairColors);
        }

        /// <summary>A new map from a VoiceRaceBySkin value (the field's initializer: the defaults, which have no problems).</summary>
        private static Dictionary<string, string[]> SkinRaces(string s)
        {
            var map = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            ParseSkinRaces(s, map, null);
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

        /// <summary>The config line's skins part: how many tones map to a voice race, and the two hair groups.</summary>
        private static string SkinsLine()
        {
            return "voice races for " + VoiceRaceBySkin.Count + " skin tone(s); natural hair colors: mid " + SurvivorMidSkins.Count + " skin(s) -> "
                   + SurvivorMidSkinHairColors.Count + " color(s), dark " + SurvivorDarkSkins.Count + " skin(s) -> " + SurvivorDarkSkinHairColors.Count
                   + " color(s), every other skin all of them";
        }
    }
}
