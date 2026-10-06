using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// The base's noise and the dead's attention (base defence D1, BaseDefence_Plan.md in the game root). Host only.
    ///   noise     = steady + events. Steady: NoisePerMember for every member living at home, NoisePerGenerator for every
    ///               running generator in the zone (read from the power graph, so it holds while the base is unloaded),
    ///               NoisePerFacility for every workstation the scan counted. Events: every vanilla heat event that lands
    ///               in the zone (AIDirector.NotifyActivity: burning stations, torches, the player's gunfire, explosions,
    ///               block destruction) at vanilla's own strength, and NPC gunfire, which vanilla never hears (its sound
    ///               path forwards players only), at the gun's own heat. Events fade with NoiseHalfLifeHours.
    ///   attention = 0-100. Each game hour it gains (noise - NoiseNoticeable) x AttentionPerNoiseHour and loses
    ///               AttentionDecayPerHour; it gains nothing while the blood moon is up. D1 only measures it: nothing is
    ///               called at 100 yet (D2 calls the horde).
    /// Vanilla's own heat map is not used as the meter: it wipes an area to 0 at 25, exists only near a player and
    /// never hears NPCs (_research\base_defence\engine_report.md).
    /// </summary>
    public static class ScNoise
    {
        public enum Level { Quiet, Noticeable, Loud, VeryLoud }

        /// <summary>The kind of an NPC shot in the counters (vanilla's own kinds are EnumAIDirectorChunkEvent values).</summary>
        public const int KindNpcShot = 100;
        private const int QueueCap = 512;
        /// <summary>Game hours one catch-up may cover (a long sleep or a time command), in steps of a quarter hour.</summary>
        private const float MaxCatchUpHours = 24f * 60f;

        private struct Heard
        {
            public Vector3i Pos;
            public float Value;
            public int Kind;
        }

        // the patches fill the queue (whatever thread vanilla calls from); the ticker drains it once a second
        private static readonly object gate = new object();
        private static readonly List<Heard> queue = new List<Heard>(64);
        private static readonly List<Heard> drain = new List<Heard>(64);

        // session counters for "sc noise"
        private static int heardHeat, heardShots, countedHeat, countedShots, dropped;
        private static readonly Dictionary<int, float> countedByKind = new Dictionary<int, float>();

        private static float nextSteady, nextSave;
        private static int steadyMembers, steadyGenerators;
        private static bool bloodMoon, fullLogged;

        public static void Reset()
        {
            lock (gate) { queue.Clear(); }
            drain.Clear();
            heardHeat = heardShots = countedHeat = countedShots = dropped = 0;
            countedByKind.Clear();
            nextSteady = nextSave = 0f;
            steadyMembers = steadyGenerators = 0;
            bloodMoon = fullLogged = false;
        }

        // ------------------------------------------------------------------ the meter ----

        public static int SteadyMembers { get { return steadyMembers; } }
        public static int SteadyGenerators { get { return steadyGenerators; } }
        public static bool BloodMoonPause { get { return bloodMoon; } }

        public static float Steady()
        {
            return steadyMembers * ScConfig.NoisePerMember + steadyGenerators * ScConfig.NoisePerGenerator
                   + ScRegistry.Community.Facilities * ScConfig.NoisePerFacility;
        }

        public static float Current()
        {
            return Steady() + ScRegistry.Community.NoiseEvents;
        }

        public static Level LevelOf(float noise)
        {
            if (noise >= ScConfig.NoiseVeryLoud) return Level.VeryLoud;
            if (noise >= ScConfig.NoiseLoud) return Level.Loud;
            if (noise >= ScConfig.NoiseNoticeable) return Level.Noticeable;
            return Level.Quiet;
        }

        public static string Word(Level l)
        {
            switch (l)
            {
                case Level.VeryLoud: return Localization.Get("scNoiseVeryLoud");
                case Level.Loud: return Localization.Get("scNoiseLoud");
                case Level.Noticeable: return Localization.Get("scNoiseNoticeable");
                default: return Localization.Get("scNoiseQuiet");
            }
        }

        /// <summary>Attention gained (or lost, negative) per game hour at this noise, outside a blood moon.</summary>
        public static float RatePerHour(float noise)
        {
            return Math.Max(0f, noise - ScConfig.NoiseNoticeable) * ScConfig.AttentionPerNoiseHour - ScConfig.AttentionDecayPerHour;
        }

        /// <summary>ScTicker, every second: drain what was heard into the zone's noise, then advance by the game time passed.</summary>
        public static void Tick(World w, EntityPlayerLocal player)
        {
            try
            {
                Community c = ScRegistry.Community;
                lock (gate) { drain.AddRange(queue); queue.Clear(); }
                if (!ScConfig.BaseNoise || !c.HasHome) { drain.Clear(); return; }

                for (int i = 0; i < drain.Count; i++)
                {
                    Heard h = drain[i];
                    if (!ScZone.Contains(h.Pos)) continue;
                    float v = h.Value * (h.Kind == KindNpcShot ? ScConfig.NoiseNpcShotMult : ScConfig.NoiseHeatMult);
                    if (v <= 0f) continue;
                    c.NoiseEvents += v;
                    if (h.Kind == KindNpcShot) countedShots++; else countedHeat++;
                    float sum;
                    countedByKind.TryGetValue(h.Kind, out sum);
                    countedByKind[h.Kind] = sum + v;
                }
                drain.Clear();
                if (c.NoiseEvents > ScConfig.NoiseEventCap) c.NoiseEvents = ScConfig.NoiseEventCap;

                float now = Time.unscaledTime;
                if (now >= nextSteady) { nextSteady = now + 10f; RefreshSteady(); }
                bloodMoon = w.aiDirector != null && w.aiDirector.BloodMoonComponent != null && w.aiDirector.BloodMoonComponent.BloodMoonActive;

                ulong t = w.worldTime;
                if (c.NoiseStamp == 0UL || t < c.NoiseStamp) { c.NoiseStamp = t; return; }   // first look, or the clock went back
                float hours = (t - c.NoiseStamp) / 1000f;
                if (hours < 0.05f) return;
                c.NoiseStamp = t;
                Advance(c, Math.Min(hours, MaxCatchUpHours));
                if (now >= nextSave) { nextSave = now + 300f; ScRegistry.SaveAtomic(); }
            }
            catch (Exception e)
            {
                ScLog.Once("noise", e);
            }
        }

        /// <summary>Members living at home and the zone's running generators (every 10 s; the power graph works unloaded).</summary>
        private static void RefreshSteady()
        {
            int members = 0;
            foreach (var m in ScRegistry.Members.Values) if (m.Status == MemberStatus.Resident) members++;
            int gens = 0;
            if (PowerManager.HasInstance)
            {
                List<PowerSource> sources = PowerManager.Instance.PowerSources;
                if (sources != null)
                {
                    for (int i = 0; i < sources.Count; i++)
                    {
                        PowerGenerator g = sources[i] as PowerGenerator;
                        if (g == null || !g.IsOn || g.CurrentFuel <= 0) continue;
                        if (ScZone.Contains(g.Position)) gens++;
                    }
                }
            }
            steadyMembers = members;
            steadyGenerators = gens;
        }

        /// <summary>
        /// Game hours pass: the event noise fades and attention integrates the noise, in quarter-hour steps (a long sleep or
        /// a time jump is one call). A step uses the event noise's average over the step.
        /// </summary>
        private static void Advance(Community c, float hours)
        {
            float steady = Steady();
            float halfLife = Math.Max(0.05f, ScConfig.NoiseHalfLifeHours);
            float lambda = 0.6931472f / halfLife;
            float left = hours;
            while (left > 1e-4f)
            {
                float dt = Math.Min(0.25f, left);
                left -= dt;
                float keep = (float)Math.Exp(-lambda * dt);
                float avgEvents = c.NoiseEvents * (1f - keep) / (lambda * dt);
                c.NoiseEvents *= keep;
                if (c.NoiseEvents < 0.01f) c.NoiseEvents = 0f;
                float rate = RatePerHour(steady + avgEvents);
                if (bloodMoon && rate > 0f) rate = 0f;       // the dead are busy with the blood moon: nothing gained
                c.Attention = Mathf.Clamp(c.Attention + rate * dt, 0f, 100f);
            }
            if (c.Attention >= 100f && !fullLogged)
            {
                fullLogged = true;
                ScLog.Out("base attention reached 100% (noise " + Current().ToString("0.#", CultureInfo.InvariantCulture)
                          + "); D1 only measures - no horde is called yet");
            }
            else if (c.Attention < 90f) fullLogged = false;
        }

        // ------------------------------------------------------------------ what was heard ----

        private static void Enqueue(Vector3i pos, float value, int kind)
        {
            if (!ScCommunity.IsHost || !ScRegistry.Loaded) return;
            lock (gate)
            {
                if (kind == KindNpcShot) heardShots++; else heardHeat++;
                if (queue.Count >= QueueCap) { dropped++; return; }
                queue.Add(new Heard { Pos = pos, Value = value, Kind = kind });
            }
        }

        /// <summary>A sound's vanilla heat (sounds.xml's Noise heat_map_strength; a Noise entry without one is 0, as for bows); false when the sound has no Noise entry.</summary>
        private static bool HeatOf(string clip, out float heat)
        {
            heat = 0f;
            AIDirectorData.Noise n;
            if (string.IsNullOrEmpty(clip) || !AIDirectorData.FindNoise(clip, out n)) return false;
            heat = n.heatMapStrength;
            return true;
        }

        /// <summary>Every vanilla heat event, at the strength vanilla asked for (before its sandbox multiplier and gates).</summary>
        [HarmonyPatch(typeof(AIDirector), nameof(AIDirector.NotifyActivity), new[] { typeof(EnumAIDirectorChunkEvent), typeof(Vector3i), typeof(float), typeof(float) })]
        public static class Patch_NotifyActivity
        {
            [HarmonyPostfix]
            public static void Postfix(EnumAIDirectorChunkEvent __0, Vector3i __1, float __2)
            {
                try
                {
                    if (__2 > 0f && ScConfig.BaseNoise) Enqueue(__1, __2, (int)__0);
                }
                catch (Exception e)
                {
                    ScLog.Once("AIDirector.NotifyActivity", e);
                }
            }
        }

        /// <summary>
        /// An NPC's shot: the effects call that plays the gun's sound (ItemActionLauncher calls this base). Players are left
        /// out here, because their shots already reach NotifyActivity through the sound path; so are zombies (a cop's or
        /// a plague spitter's vomit is an ItemActionVomit : ItemActionLauncher, and vanilla's own NotifyNoise drops every
        /// EntityEnemy) and drones, which vanilla does not hear either. The heat is the shot sound's own, exactly as for
        /// the player (a bow's Noise entry has none: 0); only a sound with no Noise entry at all counts NoiseNpcShotDefault.
        /// </summary>
        [HarmonyPatch(typeof(ItemActionRanged), nameof(ItemActionRanged.ItemActionEffects), new[] { typeof(GameManager), typeof(ItemActionData), typeof(int), typeof(Vector3), typeof(Vector3), typeof(int) })]
        public static class Patch_ShotEffects
        {
            [HarmonyPostfix]
            public static void Postfix(ItemActionData __1, int __2)
            {
                try
                {
                    if (__2 == 0 || !ScConfig.BaseNoise) return;
                    ItemActionRanged.ItemActionDataRanged d = __1 as ItemActionRanged.ItemActionDataRanged;
                    if (d == null || d.invData == null) return;
                    EntityAlive holder = d.invData.holdingEntity;
                    if (holder == null || holder is EntityPlayer || holder is EntityEnemy || holder is EntityDrone) return;
                    float v;
                    if (!HeatOf(__2 == 1 ? d.SoundStart : d.SoundLoop, out v) && !HeatOf(__2 == 1 ? d.SoundLoop : d.SoundStart, out v))
                        v = ScConfig.NoiseNpcShotDefault;
                    if (v > 0f) Enqueue(holder.GetBlockPosition(), v, KindNpcShot);
                }
                catch (Exception e)
                {
                    ScLog.Once("ItemActionRanged.ItemActionEffects", e);
                }
            }
        }

        // ------------------------------------------------------------------ console ----

        public static string Breakdown()
        {
            Community c = ScRegistry.Community;
            var sb = new StringBuilder();
            if (!ScConfig.BaseNoise) sb.Append("noise meter off (BaseNoise=false)\n");
            float steady = Steady(), cur = Current();
            sb.Append("noise ").Append(F(cur)).Append(" (").Append(Word(LevelOf(cur))).Append(") = steady ").Append(F(steady))
              .Append(" (members at home ").Append(steadyMembers).Append(" x").Append(F(ScConfig.NoisePerMember))
              .Append(", running generators ").Append(steadyGenerators).Append(" x").Append(F(ScConfig.NoisePerGenerator))
              .Append(", workstations ").Append(c.Facilities).Append(" x").Append(F(ScConfig.NoisePerFacility))
              .Append(") + events ").Append(F(c.NoiseEvents)).Append(" (half-life ").Append(F(ScConfig.NoiseHalfLifeHours)).Append(" h)\n");
            float rate = RatePerHour(cur);
            sb.Append("attention ").Append(F(c.Attention)).Append("% (");
            if (bloodMoon) sb.Append("paused: blood moon");
            else
            {
                sb.Append(rate >= 0f ? "+" : "").Append(rate.ToString("0.##", CultureInfo.InvariantCulture)).Append("/h at this noise");
                if (rate > 0f && c.Attention < 100f) sb.Append(", full in about ").Append(F((100f - c.Attention) / rate)).Append(" h");
            }
            sb.Append("); levels: noticeable ").Append(F(ScConfig.NoiseNoticeable)).Append(", loud ").Append(F(ScConfig.NoiseLoud))
              .Append(", very loud ").Append(F(ScConfig.NoiseVeryLoud)).Append('\n');
            sb.Append("heard this session: heat events ").Append(heardHeat).Append(" (in the zone ").Append(countedHeat)
              .Append("), NPC shots ").Append(heardShots).Append(" (in the zone ").Append(countedShots).Append("), dropped ").Append(dropped);
            if (countedByKind.Count > 0)
            {
                sb.Append("; counted:");
                foreach (var kv in countedByKind) sb.Append(' ').Append(KindName(kv.Key)).Append('=').Append(F(kv.Value));
            }
            return sb.ToString();
        }

        private static string KindName(int kind)
        {
            if (kind == KindNpcShot) return "npcShot";
            return ((EnumAIDirectorChunkEvent)kind).ToString();
        }

        private static string F(float v) { return v.ToString("0.#", CultureInfo.InvariantCulture); }
    }
}
