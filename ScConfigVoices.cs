using System;
using System.Collections.Generic;
using System.Globalization;

namespace SurvivorCommunity
{
    /// <summary>
    /// V2 (voices by race and sex, SC's greetings and chatter, XNPCCore's lines in SC's voices, raider taunts): the config
    /// keys, kept apart from ScConfig.cs's long lists. Config/blocks.xml, block ConfigSurvivorCommunity, after
    /// VoiceEnclaveContext; each key is commented there.
    /// </summary>
    public static partial class ScConfig
    {
        /// <summary>The voice table when the config has no Voices list: two voices per race and sex (the user's picks, 2026-11-03).</summary>
        public static readonly string[] DefaultVoices =
        {
            "male_White_Vane", "male_White_David", "female_White_Jessica", "female_White_Beth",
            "male_Black_Nate", "male_Black_Desmond", "female_Black_Tiffany", "female_Black_Rene",
            "male_Asian_Jasper", "male_Asian_Julian", "female_Asian_Layla", "female_Asian_Jess",
            "male_Native_NativeM1", "male_Native_NativeM2", "female_Native_NativeF1", "female_Native_NativeF2"
        };

        /// <summary>Each survivor and raider speaks in one of the two voices of their race and sex (false: any voice of their sex, as before V2).</summary>
        public static bool VoiceByRace = true;
        /// <summary>The voice table: Resources/Voices folder names, &lt;male|female&gt;_&lt;Race&gt;_&lt;Name&gt;; a folder may not exist yet.</summary>
        public static readonly List<string> Voices = new List<string>(DefaultVoices);

        /// <summary>SC's own greetings on the community's survivors (ScGreetings), where their voice has a recording; Voice Control's greeting is skipped for them.</summary>
        public static bool ScGreetings = true;
        /// <summary>"Meet": a survivor greets you the first time each in-game day they see you this close.</summary>
        public static float ScGreetMeetRange = 12f;
        /// <summary>"Walk-up": a survivor you look at this close greets you ...</summary>
        public static float ScGreetWalkUpRange = 3f;
        /// <summary>... at most this often (real seconds, per survivor).</summary>
        public static float ScGreetWalkUpSeconds = 120f;
        /// <summary>At most one SC greeting this often (real seconds), so a crowd greets one by one.</summary>
        public static float ScGreetGapSeconds = 6f;

        /// <summary>Members at home say a line now and then when you are near (ScGreetings), only lines with a recording.</summary>
        public static bool ScChatter = true;
        /// <summary>A chatter line is tried this often (real seconds) ...</summary>
        public static float ScChatterSeconds = 90f;
        /// <summary>... and said with this chance ...</summary>
        public static float ScChatterChance = 0.5f;
        /// <summary>... by a member at home within this many metres of you.</summary>
        public static float ScChatterRange = 15f;

        /// <summary>0-XNPCCore's say-lines, Voice Control's order acks and its fixed lines play SC's recording in the survivor's voice when SC has one.</summary>
        public static bool ScBarks = true;

        /// <summary>Raiders taunt (ScTaunts): spotting you, searching, losing you, hit, killing you, idle, dying, badly hurt - only lines with a recording.</summary>
        public static bool RaiderTaunts = true;
        /// <summary>One raider taunts at most this often (real seconds; dying and a kill are let through).</summary>
        public static float RaiderTauntCooldown = 15f;
        /// <summary>Any two raider taunts at least this far apart (real seconds), so a band doesn't all shout at once.</summary>
        public static float RaiderTauntGlobalCooldown = 3f;
        /// <summary>A raider with nobody to fight mutters about this often (real seconds, randomised).</summary>
        public static float RaiderTauntIdleSeconds = 60f;
        /// <summary>The chance a hit draws a taunt.</summary>
        public static float RaiderTauntHitChance = 0.4f;
        /// <summary>Raiders taunt only within this many metres of you.</summary>
        public static float RaiderTauntRange = 40f;
        /// <summary>Below this share of its health a hit makes a raider call the retreat (once).</summary>
        public static float RaiderTauntRetreatHealth = 0.3f;

        /// <summary>SC's greetings, chatter and taunts show their words in Voice Control's subtitle bar.</summary>
        public static bool VoiceBarkSubtitles = true;
        /// <summary>Test aid: a greeting, chatter line or taunt with no recording still shows its words and logs a line.</summary>
        public static bool VoiceBarkDebug = false;
        /// <summary>SC's clips kept loaded (compressed), least recently used dropped first.</summary>
        public static int VoiceClipCache = 48;

        private static void ResetVoices()
        {
            VoiceByRace = true;
            Voices.Clear();
            Voices.AddRange(DefaultVoices);
            ScGreetings = true;
            ScGreetMeetRange = 12f;
            ScGreetWalkUpRange = 3f;
            ScGreetWalkUpSeconds = 120f;
            ScGreetGapSeconds = 6f;
            ScChatter = true;
            ScChatterSeconds = 90f;
            ScChatterChance = 0.5f;
            ScChatterRange = 15f;
            ScBarks = true;
            RaiderTaunts = true;
            RaiderTauntCooldown = 15f;
            RaiderTauntGlobalCooldown = 3f;
            RaiderTauntIdleSeconds = 60f;
            RaiderTauntHitChance = 0.4f;
            RaiderTauntRange = 40f;
            RaiderTauntRetreatHealth = 0.3f;
            VoiceBarkSubtitles = true;
            VoiceBarkDebug = false;
            VoiceClipCache = 48;
        }

        private static void ParseBoolKey(DynamicProperties p, string key, ref bool v)
        {
            if (!p.Values.ContainsKey(key)) return;
            bool tmp = v;
            p.ParseBool(key, ref tmp);
            v = tmp;
        }

        private static void ParseVoices(DynamicProperties p)
        {
            ParseBoolKey(p, "VoiceByRace", ref VoiceByRace);
            ParseList(p, "Voices", Voices);
            ParseBoolKey(p, "ScGreetings", ref ScGreetings);
            ParseFloat(p, "ScGreetMeetRange", ref ScGreetMeetRange, 3f, 30f);
            ParseFloat(p, "ScGreetWalkUpRange", ref ScGreetWalkUpRange, 1f, 8f);
            ParseFloat(p, "ScGreetWalkUpSeconds", ref ScGreetWalkUpSeconds, 10f, 3600f);
            ParseFloat(p, "ScGreetGapSeconds", ref ScGreetGapSeconds, 0f, 60f);
            ParseBoolKey(p, "ScChatter", ref ScChatter);
            ParseFloat(p, "ScChatterSeconds", ref ScChatterSeconds, 15f, 3600f);
            ParseFloat(p, "ScChatterChance", ref ScChatterChance, 0f, 1f);
            ParseFloat(p, "ScChatterRange", ref ScChatterRange, 3f, 40f);
            ParseBoolKey(p, "ScBarks", ref ScBarks);
            ParseBoolKey(p, "RaiderTaunts", ref RaiderTaunts);
            ParseFloat(p, "RaiderTauntCooldown", ref RaiderTauntCooldown, 2f, 300f);
            ParseFloat(p, "RaiderTauntGlobalCooldown", ref RaiderTauntGlobalCooldown, 0f, 60f);
            ParseFloat(p, "RaiderTauntIdleSeconds", ref RaiderTauntIdleSeconds, 10f, 3600f);
            ParseFloat(p, "RaiderTauntHitChance", ref RaiderTauntHitChance, 0f, 1f);
            ParseFloat(p, "RaiderTauntRange", ref RaiderTauntRange, 5f, 100f);
            ParseFloat(p, "RaiderTauntRetreatHealth", ref RaiderTauntRetreatHealth, 0f, 0.9f);
            ParseBoolKey(p, "VoiceBarkSubtitles", ref VoiceBarkSubtitles);
            ParseBoolKey(p, "VoiceBarkDebug", ref VoiceBarkDebug);
            ParseInt(p, "VoiceClipCache", ref VoiceClipCache, 8, 512);
        }

        /// <summary>The second config line (V2).</summary>
        private static string VoicesLine()
        {
            Func<float, string> f = x => x.ToString("0.##", CultureInfo.InvariantCulture);
            return "voiceByRace=" + VoiceByRace + " voices=" + Voices.Count
                   + " greetings=" + (ScGreetings ? "meet " + f(ScGreetMeetRange) + "m, walk-up " + f(ScGreetWalkUpRange) + "m every " + f(ScGreetWalkUpSeconds)
                                                    + "s, gap " + f(ScGreetGapSeconds) + "s" : "off")
                   + " chatter=" + (ScChatter ? "every " + f(ScChatterSeconds) + "s at " + f(ScChatterChance) + " within " + f(ScChatterRange) + "m" : "off")
                   + " barks=" + ScBarks
                   + " taunts=" + (RaiderTaunts ? "cooldown " + f(RaiderTauntCooldown) + "s/" + f(RaiderTauntGlobalCooldown) + "s, idle ~" + f(RaiderTauntIdleSeconds)
                                                  + "s, hit " + f(RaiderTauntHitChance) + ", retreat below " + f(RaiderTauntRetreatHealth) + ", within "
                                                  + f(RaiderTauntRange) + "m" : "off")
                   + " subtitles=" + VoiceBarkSubtitles + " debug=" + VoiceBarkDebug + " clipCache=" + VoiceClipCache;
        }
    }
}
