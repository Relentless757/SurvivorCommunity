using System;
using System.Globalization;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round m1p (the M1 polish bundle; README section 21's "The Watch Post's item description names the default numbers"):
    /// the Watch Post's item description names the numbers this game really uses - the settings ScConfig parsed from the
    /// config block - instead of the defaults written into it.
    ///
    /// Localization.csv keeps two rows. scWatchPostDesc is the full text with the default numbers (what shows before a game
    /// has started, and if the game reloads its localization mid-game). scWatchPostDescFmt is the same text with {0}..{5} in
    /// place of the numbers. Apply (ModApi's GameStartDone, after ScConfig.EnsureParsed and ScWatch.DescribeHelper) writes
    /// scWatchPostDescFmt, filled in, over scWatchPostDesc's entry in the game's Localization dictionary, language by
    /// language (each column the format row has). Every block and item that names scWatchPostDesc (the shape helper, the old
    /// cube) then shows it. The numbers are the ones ScWatch reads:
    ///   {0} WatchMaxPosts (ScWatch's "too many" test), {1} WatchShiftHours (ShiftTicks), {2} the sight gain in percent
    ///   (WatchSightMult, at least 1, as ScWatch's sight boost uses it), {3} WatchShiftAmmo (the bundles a shift takes),
    ///   {4} WatchAttentionCut and {5} WatchAttentionCutMax in percent (ScWatch.AttentionShare).
    /// With WatchPosts off, scWatchOff ("Watch posts are switched off ...") is added. Main thread; once per game.
    /// </summary>
    public static class ScWatchDesc
    {
        public const string DescKey = "scWatchPostDesc";
        public const string FormatKey = "scWatchPostDescFmt";

        public static void Apply()
        {
            try
            {
                var dict = Localization.Dictionary;
                string[] fmt, desc;
                if (dict == null || !dict.TryGetValue(FormatKey, out fmt) || !dict.TryGetValue(DescKey, out desc) || fmt == null || desc == null)
                {
                    ScLog.Warn("watch post: Localization keys " + DescKey + " / " + FormatKey + " not found - the Watch Post's description keeps its default numbers");
                    return;
                }
                object[] args = Args();
                string off = ScConfig.WatchPosts ? null : Localization.Get("scWatchOff");
                int written = 0;
                for (int i = 0; i < fmt.Length && i < desc.Length; i++)
                {
                    if (string.IsNullOrEmpty(fmt[i])) continue;
                    try
                    {
                        string text = string.Format(CultureInfo.InvariantCulture, fmt[i], args);
                        desc[i] = off != null ? text + " " + off : text;   // the same array the case-insensitive dictionary holds
                        written++;
                    }
                    catch (FormatException fe)
                    {
                        ScLog.Once("watch-desc-format-" + i, "watch post: the description's text in language column " + i + " is not a valid format ("
                                   + fe.Message + ") - that language keeps the default numbers");
                    }
                }
                ScLog.Out("watch post: item description written with this game's numbers (" + string.Join(", ", Array.ConvertAll(args, a => a.ToString()))
                          + (off != null ? ", posts switched off" : "") + "; " + written + " language column(s))");
            }
            catch (Exception e)
            {
                ScLog.Once("watch-desc", e);
            }
        }

        /// <summary>{0}..{5}: max posts, shift hours, sight gain %, ammo bundles a shift, attention cut % per post and in all.</summary>
        private static object[] Args()
        {
            return new object[]
            {
                Math.Max(0, ScConfig.WatchMaxPosts),
                Math.Max(0.25f, ScConfig.WatchShiftHours).ToString("0.##", CultureInfo.InvariantCulture),
                Mathf.RoundToInt((Math.Max(1f, ScConfig.WatchSightMult) - 1f) * 100f),
                Math.Max(0, ScConfig.WatchShiftAmmo),
                Mathf.RoundToInt(Mathf.Clamp01(ScConfig.WatchAttentionCut) * 100f),
                Mathf.RoundToInt(Mathf.Clamp01(ScConfig.WatchAttentionCutMax) * 100f),
            };
        }
    }
}
