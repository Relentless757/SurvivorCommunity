using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace SurvivorCommunity
{
    /// <summary>[SurvivorCommunity]-prefixed logging with once-only dedupe (per-frame code must never spam).</summary>
    public static class ScLog
    {
        private const string Prefix = "[SurvivorCommunity] ";

        /// <summary>A persistently failing patch is re-reported (with a count) this often, so it cannot hide.</summary>
        private const long ExceptionRepeatMs = 300000;

        private static readonly HashSet<string> onceKeys = new HashSet<string>();
        private static readonly Dictionary<string, int> excCounts = new Dictionary<string, int>();
        private static readonly Dictionary<string, long> excLastMs = new Dictionary<string, long>();
        private static readonly Stopwatch clock = Stopwatch.StartNew();

        public static void Out(string msg) { Log.Out(Prefix + msg); }
        public static void Warn(string msg) { Log.Warning(Prefix + msg); }
        public static void Err(string msg) { Log.Error(Prefix + msg); }

        public static void Verbose(string msg)
        {
            if (ScConfig.Verbose) Log.Out(Prefix + msg);
        }

        /// <summary>Warn-level, emitted once per key for the process lifetime (per world - see ResetForNewWorld).</summary>
        public static void Once(string key, string msg)
        {
            if (Add(key)) Warn(msg);
        }

        /// <summary>Out-level, emitted once per key.</summary>
        public static void OnceOut(string key, string msg)
        {
            if (Add(key)) Out(msg);
        }

        /// <summary>
        /// Error + stack trace on the first occurrence, then a counted reminder every few minutes while the
        /// failure keeps happening. Used by every patch / tick catch block.
        /// </summary>
        public static void Once(string key, Exception e)
        {
            bool first;
            bool emit;
            int since;
            lock (onceKeys)
            {
                first = onceKeys.Add("exc:" + key);
                int n;
                excCounts.TryGetValue(key, out n);
                n++;
                long now = clock.ElapsedMilliseconds;
                long last;
                emit = first || !excLastMs.TryGetValue(key, out last) || now - last >= ExceptionRepeatMs;
                since = n;
                if (emit)
                {
                    excLastMs[key] = now;
                    excCounts[key] = 0;
                }
                else
                {
                    excCounts[key] = n;
                }
            }
            if (!emit) return;
            Err(key + " threw " + e.GetType().Name + ": " + e.Message
                + (first ? "" : " (" + since + " occurrence(s) since the last report)"));
            if (first) Log.Exception(e);
        }

        /// <summary>Per-world reset (ModEvents.GameStarting): the once/throttle keys are diagnostics for THIS world.</summary>
        public static void ResetForNewWorld()
        {
            lock (onceKeys)
            {
                onceKeys.Clear();
                excCounts.Clear();
                excLastMs.Clear();
            }
        }

        private static bool Add(string key)
        {
            lock (onceKeys) return onceKeys.Add(key);
        }
    }
}
