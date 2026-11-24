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
    ///              banks are silent and not touched. Each one switched off is remembered with its block id (lightsout=) BEFORE
    ///              the switch, so a throw in the middle still brings it back. At dawn the ones still there (same block), still
    ///              off, with fuel, with their engines in (MaxOutput above 0) and not by water - vanilla's own tests for switching
    ///              one on (XUiC_PowerSourceStats) - are switched back on. One the player switches on meanwhile stays on.
    ///              The power graph is saved (PowerManager.SavePowerManager) after each change, so power.dat agrees with the
    ///              registry should the game die before its own two-minute save.
    /// It can be ordered from LightsOutBeforeDuskHours before dusk until dawn (24: any time), and cancelled from the same
    /// button ("Lights on": everything back now). The home it was ordered for lost or moved ends it (the review's finding 5:
    /// the home is kept with the order). Host only; the ticker ends it at dawn.
    /// </summary>
    public static class ScLightsOut
    {
        private const ulong TicksPerDay = 24000UL;
        private static float nextTick;
        /// <summary>A power.dat save that found vanilla's own save thread running (it would have done nothing): Tick tries again.</summary>
        private static bool powerSaveDue;

        public static void Reset()
        {
            nextTick = 0f;
            powerSaveDue = false;
        }

        public static bool Active { get { return ScRegistry.Ready && ScRegistry.Community.LightsOutUntil > 0UL; } }   // round MP1: a read - Ready (the Facilities tab on a client)

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

        /// <summary>
        /// Why it cannot be ordered now, as a finished line; null: it can. Round MP2: a read - a client's Facilities tab asks it of
        /// its copy (Ready; it said "host only" there); Order keeps the server-only gate.
        /// </summary>
        public static string WhyNot(World w)
        {
            if (!ScRegistry.Ready || !ScRegistry.Community.HasHome) return Localization.Get("scNeedHome");
            if (!ScConfig.LightsOut) return Localization.Get("scLightsOutOff");
            if (Active) return string.Format(Localization.Get("scLightsOutAlready"), ScFacilityTab.TimeOfDay(ScRegistry.Community.LightsOutUntil));
            if (!InWindow(w)) return string.Format(Localization.Get("scLightsOutTooEarly"), FromText(w));
            return null;
        }

        /// <summary>
        /// The Defense entry's Lights out: people's noise down, the zone's running generators off, until dawn. Round MP2: any
        /// player (facility.do, on the server: the order is told to the one who gave it); never on a client's copy (E10).
        /// </summary>
        public static bool Order(EntityPlayer player, out string message)
        {
            World w = ScCommunity.World;
            message = WhyNot(w);
            if (message != null) return false;
            if (!ScCommunity.IsHost || !ScRegistry.Loaded) { message = Localization.Get("scHostOnly"); return false; }
            Community c = ScRegistry.Community;
            int off = 0;
            try
            {
                // the order, the generators it switched off and the saves always go together (the review's finding 3): each
                // generator is remembered before it is switched and has its own try, and the registry and the power graph are
                // saved whatever happened, so a throw leaves an order that ends at dawn with everything it switched off
                c.LightsOutUntil = NextDawn(w, w.worldTime);
                c.LightsOutHome = c.Home;
                c.LightsOutGens.Clear();
                if (ScConfig.LightsOutGenerators) off = SwitchOff(c);
            }
            catch (Exception e)
            {
                ScLog.Once("lightsout-order", e);
            }
            finally
            {
                ScNoise.RefreshNow();
                ScRegistry.SaveAtomic();
                SavePower();
            }
            if (c.LightsOutUntil == 0UL) { message = Localization.Get("scInternal"); return false; }   // it threw before the order was set
            try
            {
                int pct = Mathf.RoundToInt(100f * (1f - Mathf.Clamp01(ScConfig.LightsOutPeopleNoise)));
                string until = ScFacilityTab.TimeOfDay(c.LightsOutUntil);
                message = !ScConfig.LightsOutGenerators ? string.Format(Localization.Get("scLightsOutOrderedNoGens"), until, pct)
                        : off > 0 ? string.Format(Localization.Get("scLightsOutOrdered"), until, pct, off)
                        : string.Format(Localization.Get("scLightsOutOrderedNone"), until, pct);
                if (player != null) ScCommunity.Tell(player, message, "");
                ScLog.Out("lights out until " + ScBaseAttack.Clock(c.LightsOutUntil) + ": people's noise x" + ScConfig.LightsOutPeopleNoise.ToString("0.##", CultureInfo.InvariantCulture)
                          + ", " + off + " generator(s) switched off" + (c.LightsOutGens.Count > 0 ? " (" + GensText(c) + ")" : "")
                          + "; noise now " + ScNoise.Current().ToString("0.#", CultureInfo.InvariantCulture));
            }
            catch (Exception e)
            {
                ScLog.Once("lightsout-order-message", e);
                message = Localization.Get("scInternal");
            }
            return true;
        }

        /// <summary>The Defense entry's "Lights on": it ends now. Round MP2: any player (facility.do, on the server).</summary>
        public static bool CancelByOrder(EntityPlayer player, out string message)
        {
            message = null;
            if (!ScCommunity.IsHost || !ScRegistry.Loaded) { message = Localization.Get("scHostOnly"); return false; }
            if (!Active) { message = Localization.Get("scLightsOutNotOn"); return false; }
            int on = End("cancelled from the community window");
            message = string.Format(Localization.Get("scLightsOnOrdered"), on);
            if (player != null) ScCommunity.Tell(player, message, "");
            return true;
        }

        private static string GensText(Community c)
        {
            var parts = new List<string>();
            foreach (KeyValuePair<Vector3i, int> g in c.LightsOutGens) parts.Add(g.Key + "#" + g.Value);
            return string.Join(" ", parts.ToArray());
        }

        /// <summary>
        /// Every generator ScNoise counts as running (a PowerGenerator in the zone, on, with fuel) switched off. Each is remembered
        /// with its block id first, then switched in its own try: a throw costs that one, never the others or the record.
        /// </summary>
        private static int SwitchOff(Community c)
        {
            if (!PowerManager.HasInstance || PowerManager.Instance.PowerSources == null) return 0;
            int n = 0;
            foreach (PowerSource s in new List<PowerSource>(PowerManager.Instance.PowerSources))
            {
                try
                {
                    PowerGenerator g = s as PowerGenerator;
                    if (g == null || !g.IsOn || g.CurrentFuel <= 0 || !ScZone.Contains(g.Position)) continue;
                    Vector3i pos = g.Position;
                    if (c.LightsOutGens.FindIndex(kv => kv.Key == pos) < 0) c.LightsOutGens.Add(new KeyValuePair<Vector3i, int>(pos, g.BlockID));
                    g.IsOn = false;
                    n++;
                }
                catch (Exception e)
                {
                    ScLog.Once("lightsout-switch-off", e);
                }
            }
            return n;
        }

        /// <summary>Vanilla's own test before a power source is switched on (XUiC_PowerSourceStats): water beside or above it.</summary>
        private static bool ByWater(World w, Vector3i p)
        {
            if (w == null) return false;
            return w.IsWater(p.x, p.y + 1, p.z) || w.IsWater(p.x + 1, p.y, p.z) || w.IsWater(p.x - 1, p.y, p.z)
                   || w.IsWater(p.x, p.y, p.z + 1) || w.IsWater(p.x, p.y, p.z - 1);
        }

        /// <summary>
        /// Ends it: the generators it switched off go back on when they are still there as the same block, still off, with fuel,
        /// with their engines in and not by water (one the player switched on meanwhile is left as it is). Returns how many went
        /// back on. Saves the registry and the power graph.
        /// </summary>
        private static int End(string why)
        {
            Community c = ScRegistry.Community;
            World w = ScCommunity.World;
            int on = 0, gone = 0, dry = 0, unfit = 0;
            try
            {
                if (PowerManager.HasInstance)
                {
                    foreach (KeyValuePair<Vector3i, int> rec in c.LightsOutGens)
                    {
                        try
                        {
                            PowerGenerator g = PowerManager.Instance.GetPowerItemByWorldPos(rec.Key) as PowerGenerator;
                            if (g == null || (rec.Value > 0 && g.BlockID != rec.Value)) { gone++; continue; }   // an old record has no id (0)
                            if (g.IsOn) continue;
                            if (g.CurrentFuel <= 0) { dry++; continue; }     // vanilla would switch it off again at once
                            if (g.MaxOutput == 0 || ByWater(w, rec.Key)) { unfit++; continue; }   // its engines pulled, or water by it
                            g.IsOn = true;
                            on++;
                        }
                        catch (Exception e)
                        {
                            ScLog.Once("lightsout-switch-on", e);
                        }
                    }
                }
            }
            finally
            {
                c.LightsOutGens.Clear();
                c.LightsOutUntil = 0UL;
                c.LightsOutHome = Vector3i.zero;
                ScNoise.RefreshNow();
                ScRegistry.SaveAtomic();
                SavePower();
            }
            ScLog.Out("lights out over (" + why + "): " + on + " generator(s) back on" + (gone > 0 ? ", " + gone + " gone or replaced" : "")
                      + (dry > 0 ? ", " + dry + " out of fuel left off" : "") + (unfit > 0 ? ", " + unfit + " with no engine or by water left off" : ""));
            return on;
        }

        /// <summary>
        /// The power graph to power.dat now (main thread; vanilla otherwise saves it every two minutes and with the world).
        /// SavePowerManager does nothing while vanilla's own "powerDataSave" thread is still writing (that write holds the
        /// state from before): then powerSaveDue makes Tick try again until it goes through (the re-check's N1).
        /// </summary>
        private static void SavePower()
        {
            try
            {
                if (!PowerManager.HasInstance) { powerSaveDue = false; return; }
                bool busy;
                Dictionary<string, ThreadManager.ThreadInfo> threads = ThreadManager.ActiveThreads;
                if (threads == null) busy = false;
                else lock (threads) { busy = threads.ContainsKey("powerDataSave"); }
                if (busy)
                {
                    if (!powerSaveDue) ScLog.Verbose("lights out: vanilla is still saving power.dat - our save waits for it");
                    powerSaveDue = true;
                    return;
                }
                PowerManager.Instance.SavePowerManager();
                powerSaveDue = false;
            }
            catch (Exception e) { ScLog.Once("lightsout-save-power", e); }
        }

        /// <summary>ScTicker, every second (works every 2): dawn ends it; the home lost or moved ends it; a clock gone back moves the end.</summary>
        public static void Tick(World w, EntityPlayer player)     // round MP0: any player (the anchor)
        {
            try
            {
                if (w == null || !ScRegistry.Loaded || !ScCommunity.IsHost) return;
                if (powerSaveDue) SavePower();      // N1: a power.dat save that waited for vanilla's own - also after it ended
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
                // an order saved before the home was kept with it adopts the home it finds
                if (c.LightsOutHome == Vector3i.zero && c.HasHome) { c.LightsOutHome = c.Home; ScRegistry.SaveAtomic(); }
                if (!c.HasHome || c.Home != c.LightsOutHome)
                {
                    End(c.HasHome ? "the community moved home" : "the community has no home");
                    return;
                }
                if (t < c.LightsOutUntil) return;
                int on = End("dawn");
                // round MP0: every community player at home hears it, no longer the host alone (single player: you, as before)
                List<EntityPlayer> all = ScPlayers.All;
                for (int i = 0; i < all.Count; i++)
                    if (all[i] != null && ScPlayers.InCommunity(all[i]) && ScBaseAttack.PlayerHome(all[i]))
                        ScCommunity.Tell(all[i], string.Format(Localization.Get("scLightsOutDawn"), on), "");
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
                    foreach (KeyValuePair<Vector3i, int> rec in c.LightsOutGens)
                    {
                        PowerGenerator g = PowerManager.Instance.GetPowerItemByWorldPos(rec.Key) as PowerGenerator;
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
            output("[SC] lights out: " + (Active ? "ON until " + ScBaseAttack.Clock(c.LightsOutUntil) + " for the home at " + c.LightsOutHome + ", "
                                                   + c.LightsOutGens.Count + " generator(s) switched off by it" : "off")
                   + "; people's noise x" + PeopleNoiseMult().ToString("0.##", CultureInfo.InvariantCulture) + "; can order now: " + (why == null ? "yes" : why)
                   + "; dawn " + w.DawnHour + ":00, dusk " + w.DuskHour + ":00, now " + ScBaseAttack.Clock(w.worldTime));
            if (PowerManager.HasInstance && PowerManager.Instance.PowerSources != null)
            {
                foreach (PowerSource s in PowerManager.Instance.PowerSources)
                {
                    PowerGenerator g = s as PowerGenerator;
                    if (g == null || !ScZone.Contains(g.Position)) continue;
                    Vector3i pos = g.Position;
                    output("[SC]   generator at " + pos + " (block " + g.BlockID + "): " + (g.IsOn ? "on" : "off") + ", fuel " + g.CurrentFuel + ", output " + g.MaxOutput
                           + (ByWater(w, pos) ? ", BY WATER" : "") + (c.LightsOutGens.FindIndex(kv => kv.Key == pos) >= 0 ? " (switched off by lights out)" : ""));
                }
            }
        }
    }
}
