using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Base defense BD1, Watch fatigue (round m1f, 2026-11-17; BaseDefence_Plan.md's idea menu, ROADMAP.md section 3 item 5): a
    /// long watch costs a little mood. A term of the member's mood (ScMorale.Recompute), the pattern of round U4's Bond: a read of
    /// state that is already saved (the shift's start, the rest after it), so it has no save key of its own.
    ///   tired      on watch WatchFatigueHours or longer: -WatchFatigueMood; and through the rest after a full shift
    ///              (ScWatch's WatchRestUntil) when a full shift is that long. Relieved early, it ends at once.
    ///   night      a member with one of WatchNightTraits (Insomniac by default) on watch while it is dark (vanilla's dusk to
    ///              dawn): +WatchNightMood, and never tired then. The traits are read by name: a trait no build defines is simply
    ///              never matched. Night Owl is not in the default: round m1t gives it its own night-watch mood (trait key
    ///              nightwatch=6, ScTraitEffects.SituationalMood), and its exemption from the tiredness is wired through
    ///              ScTraitEffects.LikesNightWatch at the ms/m1all merge (the coordinator's decision of 2026-11-17).
    /// The term follows the clock (the shift's hours, dusk and dawn, the rest's end), so Tick recomputes the moods as soon as any
    /// member's term changes (the Fable review's finding 1) instead of waiting for the HUD's five-second recompute.
    /// </summary>
    public static class ScWatchFatigue
    {
        /// <summary>Each member's term at the last look (Tick): a change recomputes the moods.</summary>
        private static readonly Dictionary<int, int> lastTerm = new Dictionary<int, int>();
        private static readonly List<int> gone = new List<int>();
        private static float nextTick;

        public static void Reset()
        {
            lastTerm.Clear();
            gone.Clear();
            nextTick = 0f;
        }

        /// <summary>
        /// ScTicker, every second: every member's term against the last look - a shift started or ended, the fatigue threshold
        /// passed, a rest over, dusk or dawn - and on any change ScMorale.Recompute writes the moods at once.
        /// </summary>
        public static void Tick(World w)
        {
            try
            {
                if (w == null || !ScRegistry.Loaded) return;
                float now = Time.unscaledTime;
                if (now < nextTick) return;
                nextTick = now + 1f;
                bool changed = false;
                foreach (Member m in ScRegistry.Members.Values)
                {
                    int t = MoodTerm(m), was;
                    bool known = lastTerm.TryGetValue(m.Id, out was);
                    if (known && was == t) continue;
                    if (known || t != 0) changed = true;
                    lastTerm[m.Id] = t;
                }
                if (lastTerm.Count > ScRegistry.Members.Count)
                {
                    gone.Clear();
                    foreach (int id in lastTerm.Keys) if (ScRegistry.GetMember(id) == null) gone.Add(id);
                    for (int i = 0; i < gone.Count; i++) lastTerm.Remove(gone[i]);
                }
                if (!changed) return;
                ScMorale.Recompute();
                ScLog.Verbose("watch fatigue: a member's watch term changed - moods recomputed");
            }
            catch (Exception e)
            {
                ScLog.Once("watch-fatigue-tick", e);
            }
        }

        /// <summary>The member likes a night watch (one of WatchNightTraits).</summary>
        public static bool LikesNight(Member m)
        {
            if (m == null || m.Traits == null) return false;
            for (int i = 0; i < m.Traits.Count; i++)
                for (int j = 0; j < ScConfig.WatchNightTraits.Count; j++)
                    if (string.Equals(m.Traits[i], ScConfig.WatchNightTraits[j], StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>ScMorale.Recompute: the watch's mood term (0 when off, not on watch and not resting from a long one). A read.</summary>
        public static int MoodTerm(Member m)
        {
            if (m == null || !ScConfig.WatchFatigue) return 0;
            World w = ScCommunity.World;
            if (w == null) return 0;
            ulong t = w.worldTime;
            if (ScWatch.OnWatch(m))
            {
                if (w.IsDark() && LikesNight(m)) return Math.Max(0, ScConfig.WatchNightMood);
                ulong after = (ulong)(Math.Max(0.5f, ScConfig.WatchFatigueHours) * 1000f);
                return m.WatchStart > 0UL && t >= m.WatchStart + after ? -Math.Max(0, ScConfig.WatchFatigueMood) : 0;
            }
            // resting after a full shift (only a full one rests): still tired when a full shift counts as long
            if (m.WatchRestUntil > t && ScConfig.WatchShiftHours >= ScConfig.WatchFatigueHours) return -Math.Max(0, ScConfig.WatchFatigueMood);
            return 0;
        }

        /// <summary>" - tired (-5)" / " - enjoys the night watch (+3)" for the community window, "" at 0.</summary>
        public static string Suffix(Member m)
        {
            int t = MoodTerm(m);
            if (t == 0) return "";
            return " - " + string.Format(Localization.Get(t > 0 ? "scWatchNightTerm" : "scWatchTiredTerm"), (t > 0 ? "+" : "") + t.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>The Watch posts entry's line on fatigue.</summary>
        public static string AboutText()
        {
            return string.Format(Localization.Get("scWatchFatigueAbout"), ScConfig.WatchFatigueHours.ToString("0.#", CultureInfo.InvariantCulture), ScConfig.WatchFatigueMood,
                                 ScConfig.WatchNightMood, NightTraitNames());
        }

        /// <summary>The night traits by their display names ("Insomniac"; two or more joined with "and").</summary>
        private static string NightTraitNames()
        {
            var names = new List<string>();
            foreach (string t in ScConfig.WatchNightTraits) names.Add(ScTraits.Display(t));
            if (names.Count == 0) return Localization.Get("scUiFacNobody");
            if (names.Count == 1) return names[0];
            return string.Join(", ", names.GetRange(0, names.Count - 1).ToArray()) + " " + Localization.Get("scAnd") + " " + names[names.Count - 1];
        }

        /// <summary>sc watch fatigue: each guard's and resting member's term, and why.</summary>
        public static string ConsoleText()
        {
            World w = ScCommunity.World;
            var lines = new List<string>();
            lines.Add("watch fatigue " + (ScConfig.WatchFatigue ? "on" : "OFF (WatchFatigue=false)") + ": tired after " + ScConfig.WatchFatigueHours.ToString("0.#", CultureInfo.InvariantCulture)
                      + " h on watch (-" + ScConfig.WatchFatigueMood + "), night watch +" + ScConfig.WatchNightMood + " for " + string.Join("/", ScConfig.WatchNightTraits.ToArray())
                      + "; it is " + (w != null && w.IsDark() ? "dark" : "light") + " now");
            foreach (Member m in ScRegistry.Members.Values)
            {
                bool on = ScWatch.OnWatch(m), resting = w != null && m.WatchRestUntil > w.worldTime;
                if (!on && !resting && !LikesNight(m)) continue;
                string served = on && w != null && m.WatchStart > 0UL && w.worldTime > m.WatchStart ? ((w.worldTime - m.WatchStart) / 1000f).ToString("0.#", CultureInfo.InvariantCulture) + " h on watch" : "";
                lines.Add("  " + m.Name + ": " + (on ? served : resting ? "resting after a watch" : "not on watch") + (LikesNight(m) ? ", likes a night watch" : "")
                          + ", term " + MoodTerm(m) + ", mood " + m.Mood);
            }
            return string.Join("\n", lines.ToArray());
        }
    }
}
