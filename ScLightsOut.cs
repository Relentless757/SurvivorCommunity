using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Base defense BD1, Lights out (round m1f, 2026-11-17; BaseDefence_Plan.md's idea menu, ROADMAP.md section 3 item 5): an
    /// order on the Facilities tab's Defense entry for a quiet night. Until the next dawn:
    ///   people     the people's part of the base's steady noise (NoisePerMember for each member at home) is multiplied by
    ///              LightsOutPeopleNoise (0.5: halved) - ScNoise.Steady reads PeopleNoiseMult;
    ///   generators with LightsOutGenerators, every generator SC knows of is switched off: the ones ScNoise counts as running
    ///              (a PowerGenerator power source inside the home zone, on, with fuel - read from the power graph, which the
    ///              host keeps whether or not their chunk is loaded). SC switches them through the power graph as vanilla's own
    ///              code does (PowerSource.IsOn; with the chunk loaded the setter brings the block's on bit along through
    ///              TileEntity.Activate, otherwise TileEntityPowered syncs it when the chunk loads). Solar panels and battery
    ///              banks are silent and not touched. The ones switched off are remembered (lightsout=), and at dawn the ones still
    ///              there, still off and with fuel are switched back on. One the player switches on meanwhile stays on.
    /// It can be ordered from LightsOutBeforeDuskHours before dusk until dawn (24: any time), and cancelled from the same
    /// button ("Lights on": everything back now). The home lost or moved ends it. Host only; the ticker ends it at dawn.
    /// </summary>
    public static class ScLightsOut
    {
        private const ulong TicksPerDay = 24000UL;
        private static float nextTick;

        public static void Reset()
        {
            nextTick = 0f;
        }

        public static bool Active { get { return ScRegistry.Loaded && ScRegistry.Community.LightsOutUntil > 0UL; } }

        /// <summary>ScNoise.Steady: the people's part of the noise is multiplied by this (1 without Lights out).</summary>
        public static float PeopleNoiseMult()
        {
            return Active ? Mathf.Clamp01(ScConfig.LightsOutPeopleNoise) : 1f;
        }

        /// <summary>The first dawn after t (World.DawnHour, from the day-length setting).</summary>
        public static ulong NextDawn(World w, ulong t)
        {
            int dawn = w != null ? Mathf.Clamp(w.DawnHour, 0, 23) : 4;
            ulong at = t - t % TicksPerDay + (ulong)dawn * 1000UL;
            if (at <= t) at += TicksPerDay;
            return at;
        }

        /// <summary>The hour it can be ordered from (dusk less LightsOutBeforeDuskHours), as "20:00".</summary>
        private static string FromText(World w)
        {
            float from = Mathf.Repeat((w != null ? w.DuskHour : 22) - ScConfig.LightsOutBeforeDuskHours, 24f);
            int h = Mathf.FloorToInt(from), m = Mathf.FloorToInt((from - h) * 60f);
            return h.ToString("00", CultureInfo.InvariantCulture) + ":" + m.ToString("00", CultureInfo.InvariantCulture);
        }

        /// <summary>From LightsOutBeforeDuskHours before dusk until dawn (24 or more: at any time).</summary>
        public static bool InWindow(World w)
        {
            if (w == null) return false;
            if (ScConfig.LightsOutBeforeDuskHours >= 24f || w.IsDark()) return true;
            float hour = (w.worldTime % TicksPerDay) / 1000f;
            return hour >= w.DuskHour - ScConfig.LightsOutBeforeDuskHours;
        }

        /// <summary>Why it cannot be ordered now, as a finished line; null: it can.</summary>
        public static string WhyNot(World w)
        {
            if (!ScCommunity.IsHost) return Localization.Get("scHostOnly");
            if (!ScRegistry.Loaded || !ScRegistry.Community.HasHome) return Localization.Get("scNeedHome");
            if (!ScConfig.LightsOut) return Localization.Get("scLightsOutOff");
            if (Active) return string.Format(Localization.Get("scLightsOutAlready"), ScFacilityTab.TimeOfDay(ScRegistry.Community.LightsOutUntil));
            if (!InWindow(w)) return string.Format(Localization.Get("scLightsOutTooEarly"), FromText(w));
            return null;
        }

        /// <summary>The Defense entry's Lights out: people's noise down, the zone's running generators off, until dawn.</summary>
        public static bool Order(EntityPlayerLocal player, out string message)
        {
            World w = ScCommunity.World;
            message = WhyNot(w);
            if (message != null) return false;
            try
            {
                Community c = ScRegistry.Community;
                c.LightsOutUntil = NextDawn(w, w.worldTime);
                c.LightsOutGens.Clear();
                int off = ScConfig.LightsOutGenerators ? SwitchOff(c) : 0;
                ScNoise.RefreshNow();
                ScRegistry.SaveAtomic();
                int pct = Mathf.RoundToInt(100f * (1f - Mathf.Clamp01(ScConfig.LightsOutPeopleNoise)));
                string until = ScFacilityTab.TimeOfDay(c.LightsOutUntil);
                message = !ScConfig.LightsOutGenerators ? string.Format(Localization.Get("scLightsOutOrderedNoGens"), until, pct)
                        : off > 0 ? string.Format(Localization.Get("scLightsOutOrdered"), until, pct, off)
                        : string.Format(Localization.Get("scLightsOutOrderedNone"), until, pct);
                if (player != null) ScCommunity.Tell(player, message, "");
                ScLog.Out("lights out until " + ScBaseAttack.Clock(c.LightsOutUntil) + ": people's noise x" + ScConfig.LightsOutPeopleNoise.ToString("0.##", CultureInfo.InvariantCulture)
                          + ", " + off + " generator(s) switched off" + (off > 0 ? " (" + string.Join(" ", c.LightsOutGens.ConvertAll(v => v.ToString()).ToArray()) + ")" : "")
                          + "; noise now " + ScNoise.Current().ToString("0.#", CultureInfo.InvariantCulture));
                return true;
            }
            catch (Exception e)
            {
                ScLog.Once("lightsout-order", e);
                message = Localization.Get("scInternal");
                return false;
            }
        }

        /// <summary>The Defense entry's "Lights on": it ends now.</summary>
        public static bool CancelByOrder(EntityPlayerLocal player, out string message)
        {
            message = null;
            if (!ScCommunity.IsHost) { message = Localization.Get("scHostOnly"); return false; }
            if (!Active) { message = Localization.Get("scLightsOutNotOn"); return false; }
            int on = End("cancelled from the community window");
            message = string.Format(Localization.Get("scLightsOnOrdered"), on);
            if (player != null) ScCommunity.Tell(player, message, "");
            return true;
        }

        /// <summary>Every generator ScNoise counts as running (a PowerGenerator in the zone, on, with fuel) switched off and remembered.</summary>
        private static int SwitchOff(Community c)
        {
            if (!PowerManager.HasInstance || PowerManager.Instance.PowerSources == null) return 0;
            int n = 0;
            foreach (PowerSource s in new List<PowerSource>(PowerManager.Instance.PowerSources))
            {
                PowerGenerator g = s as PowerGenerator;
                if (g == null || !g.IsOn || g.CurrentFuel <= 0 || !ScZone.Contains(g.Position)) continue;
                g.IsOn = false;
                if (!c.LightsOutGens.Contains(g.Position)) c.LightsOutGens.Add(g.Position);
                n++;
            }
            return n;
        }

        /// <summary>
        /// Ends it: the generators it switched off that are still there, still off and still have fuel go back on (one the player
        /// switched on meanwhile is left as it is). Returns how many went back on. Saves.
        /// </summary>
        private static int End(string why)
        {
            Community c = ScRegistry.Community;
            int on = 0, gone = 0, dry = 0;
            if (PowerManager.HasInstance)
            {
                foreach (Vector3i pos in c.LightsOutGens)
                {
                    PowerGenerator g = PowerManager.Instance.GetPowerItemByWorldPos(pos) as PowerGenerator;
                    if (g == null) { gone++; continue; }
                    if (g.IsOn) continue;
                    if (g.CurrentFuel <= 0) { dry++; continue; }     // vanilla would switch it off again at once
                    g.IsOn = true;
                    on++;
                }
            }
            c.LightsOutGens.Clear();
            c.LightsOutUntil = 0UL;
            ScNoise.RefreshNow();
            ScRegistry.SaveAtomic();
            ScLog.Out("lights out over (" + why + "): " + on + " generator(s) back on" + (gone > 0 ? ", " + gone + " gone" : "") + (dry > 0 ? ", " + dry + " out of fuel left off" : ""));
            return on;
        }

        /// <summary>ScTicker, every second (works every 2): dawn ends it; the home lost ends it; a clock gone back moves the end.</summary>
        public static void Tick(World w, EntityPlayerLocal player)
        {
            try
            {
                if (w == null || !ScRegistry.Loaded || !ScCommunity.IsHost) return;
                Community c = ScRegistry.Community;
                if (c.LightsOutUntil == 0UL) return;
                float now = Time.unscaledTime;
                if (now < nextTick) return;
                nextTick = now + 2f;
                ulong t = w.worldTime;
                if (c.LightsOutUntil > t + TicksPerDay)
                {
                    // the clock went back (a time command, an older save): it ends at the next dawn from now
                    c.LightsOutUntil = NextDawn(w, t);
                    ScRegistry.SaveAtomic();
                    ScLog.Out("lights out: the clock went back - it ends at the next dawn, " + ScBaseAttack.Clock(c.LightsOutUntil));
                }
                if (!c.HasHome)
                {
                    End("the community has no home");
                    return;
                }
                if (t < c.LightsOutUntil) return;
                int on = End("dawn");
                if (player != null && ScBaseAttack.PlayerHome(player))
                    ScCommunity.Tell(player, string.Format(Localization.Get("scLightsOutDawn"), on), "");
            }
            catch (Exception e)
            {
                ScLog.Once("lightsout-tick", e);
            }
        }

        /// <summary>The Defense entry's Lights out line: what it does and from when, or how long it lasts and what it switched off.</summary>
        public static string AboutText(World w)
        {
            if (!ScConfig.LightsOut && !Active) return Localization.Get("scLightsOutOff");
            int pct = Mathf.RoundToInt(100f * (1f - Mathf.Clamp01(ScConfig.LightsOutPeopleNoise)));
            if (Active)
            {
                Community c = ScRegistry.Community;
                int off = 0;
                if (PowerManager.HasInstance)
                    foreach (Vector3i pos in c.LightsOutGens)
                    {
                        PowerGenerator g = PowerManager.Instance.GetPowerItemByWorldPos(pos) as PowerGenerator;
                        if (g != null && !g.IsOn) off++;
                    }
                return string.Format(Localization.Get("scLightsOutAboutOn"), ScFacilityTab.TimeOfDay(c.LightsOutUntil), pct, off);
            }
            return string.Format(Localization.Get(ScConfig.LightsOutGenerators ? "scLightsOutAbout" : "scLightsOutAboutNoGens"), pct, FromText(w));
        }

        /// <summary>sc lightsout [on|off]: the state; on orders it (any hour), off ends it now.</summary>
        public static void ConsoleCmd(List<string> p, Action<string> output)
        {
            World w = ScCommunity.World;
            string sub = p.Count > 1 ? p[1].ToLowerInvariant() : "";
            if (!ScRegistry.Loaded || w == null) { output("[SC] no world"); return; }
            if (sub == "on")
            {
                float was = ScConfig.LightsOutBeforeDuskHours;
                ScConfig.LightsOutBeforeDuskHours = 24f;              // the console orders it at any hour
                string msg;
                try { output("[SC] " + (Order(ScCommunity.LocalPlayer, out msg) ? "" : "refused: ") + msg); }
                finally { ScConfig.LightsOutBeforeDuskHours = was; }
            }
            else if (sub == "off")
            {
                string msg;
                output("[SC] " + (CancelByOrder(ScCommunity.LocalPlayer, out msg) ? "" : "refused: ") + msg);
            }
            Community c = ScRegistry.Community;
            string why = WhyNot(w);
            output("[SC] lights out: " + (Active ? "ON until " + ScBaseAttack.Clock(c.LightsOutUntil) + ", " + c.LightsOutGens.Count + " generator(s) switched off by it" : "off")
                   + "; people's noise x" + PeopleNoiseMult().ToString("0.##", CultureInfo.InvariantCulture) + "; can order now: " + (why == null ? "yes" : why)
                   + "; dawn " + w.DawnHour + ":00, dusk " + w.DuskHour + ":00, now " + ScBaseAttack.Clock(w.worldTime));
            if (PowerManager.HasInstance && PowerManager.Instance.PowerSources != null)
            {
                foreach (PowerSource s in PowerManager.Instance.PowerSources)
                {
                    PowerGenerator g = s as PowerGenerator;
                    if (g == null || !ScZone.Contains(g.Position)) continue;
                    output("[SC]   generator at " + g.Position + ": " + (g.IsOn ? "on" : "off") + ", fuel " + g.CurrentFuel
                           + (c.LightsOutGens.Contains(g.Position) ? " (switched off by lights out)" : ""));
                }
            }
        }
    }
}
