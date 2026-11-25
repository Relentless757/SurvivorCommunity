using System;
using System.Collections.Generic;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// V2: raider taunts (research\voices\REPORT.md section 4; SC_Ideas_and_Voices_Plan.md 4.4). SC's raiders carry Voice
    /// Control's no_voice tag, so nothing of Voice Control's runs on them: SC has its own triggers and plays the clip from
    /// its own AudioSource on a child of the raider (ScVoice.PlayBark, no lip sync), in the raider's voice (ScVoice.Chain:
    /// their race and sex, dealt by name and id). Clip-only (VoiceBarkDebug: a silent one is shown and logged). Single
    /// player, so no network relay. Keys taunt.&lt;event&gt;.&lt;n&gt;:
    ///  - spot: a raider takes a living player as its attack target and hadn't had one for SpotForgetSeconds (postfix on
    ///    EntityAlive.SetAttackTarget, the previous target kept by its prefix - Patches_Voices; LastHadPlayer, refreshed
    ///    every second while it holds a player, decides it - a lost line in between doesn't shorten the 20 s);
    ///  - search: SCore's MoveToInvestigate starts on a raider not fighting a player (its Start postfix: SC's
    ///    ScRaiderInvestigate goes to a noise);
    ///  - lost: SCore gives up an investigation (EntityAlive.PlayGiveUpSound postfix), or a raider that had a player as its
    ///    target has had none for LostAfterSeconds while that player lives (the tick: the target also times out unseen,
    ///    EntityAlive.OnUpdateLive clearing the field directly);
    ///  - hit: a player's landed hit (Patches_Trust's ProcessDamageResponseLocal postfix), with RaiderTauntHitChance;
    ///  - retreat: instead of that, once, when the hit leaves the raider under RaiderTauntRetreatHealth of its health;
    ///  - killed: the raider that killed the local player (EntityAlive.AwardKill postfix on the dying player names it),
    ///    once in KillRepeatSeconds - only the killer (the V2 review's L2: the other raiders whose target was you stay quiet);
    ///  - idle: every RaiderTauntIdleSeconds or so (randomised) one raider near you with nobody to fight mutters;
    ///  - dying: the raider's death (Patches_Npc's SetDead postfix), once.
    /// Only within RaiderTauntRange of the local player. Pacing: one raider at most every RaiderTauntCooldown (dying,
    /// killed and retreat pass it), any two taunts RaiderTauntGlobalCooldown apart (dying passes it), never over a line of
    /// theirs still playing (dying aside), so a band doesn't all shout at once. Main thread, host.
    /// Round MP4 (multiplayer): only the server taunts (a client's own hooks - its player's death, a body dying on its screen -
    /// say nothing: the server's line reaches it), within RaiderTauntRange of any player, and every remote player in earshot
    /// hears it (ScBarks.Say, NetPackageScVoice). A remote player's death is never awarded on the server (EntityAlive
    /// .OnEntityDeath calls AwardKill on the player's own machine only), so "killed" comes from the raider whose target they
    /// were as it lets go of them (OnTargetChanged) - the killer when the server knows it.
    /// </summary>
    public static class ScTaunts
    {
        public const string Spot = "spot", Search = "search", Lost = "lost", Hit = "hit", Killed = "killed", Idle = "idle", Dying = "dying", Retreat = "retreat";
        public static readonly string[] Events = { Spot, Search, Lost, Hit, Killed, Idle, Dying, Retreat };

        private const float Interval = 1f;
        /// <summary>A raider that had no player target for this long spots you afresh.</summary>
        private const float SpotForgetSeconds = 20f;
        /// <summary>A raider with no player target for this long, after having one, has lost you.</summary>
        private const float LostAfterSeconds = 8f;
        private const float KillRepeatSeconds = 30f;

        private sealed class State
        {
            public float Next;                     // its own cooldown
            public bool Engaged;                   // has had a player target since it last lost one
            public float LastHadPlayer = -999f;
            public float KilledAt = -999f;
            public bool Retreated, Died;
        }

        private static readonly Dictionary<int, State> states = new Dictionary<int, State>();
        private static readonly System.Random rnd = new System.Random();
        private static readonly List<EntityAlive> near = new List<EntityAlive>();
        private static readonly List<int> gone = new List<int>();
        private static float nextTick, globalNext, nextIdle = -1f, nextPrune;
        private static readonly Dictionary<string, int> fired = new Dictionary<string, int>(StringComparer.Ordinal);

        public static void Reset()
        {
            states.Clear();
            fired.Clear();
            nextTick = 0f;
            globalNext = 0f;
            nextIdle = -1f;
            nextPrune = 0f;
        }

        private static State Get(EntityAlive raider)
        {
            State s;
            if (!states.TryGetValue(raider.entityId, out s)) states[raider.entityId] = s = new State();
            return s;
        }

        // ------------------------------------------------------------------ hooks (Patches_Voices, Patches_Trust, Patches_Npc) ----

        /// <summary>EntityAlive.SetAttackTarget's postfix, when the target changed: spot (killed is AwardKill's: OnKilledPlayer).</summary>
        public static void OnTargetChanged(EntityAlive self, EntityAlive was, EntityAlive now)
        {
            if (!ScConfig.RaiderTaunts || self == null || !ScXnpc.IsRaider(self) || self.IsDead()) return;
            State s = Get(self);
            float t = Time.unscaledTime;
            EntityPlayer np = now as EntityPlayer;
            bool npAlive = np != null && !np.IsDead();
            // the old target a dead player: no lost line for them (the killer's taunt comes from AwardKill, OnKilledPlayer)
            EntityPlayer wp = was as EntityPlayer;
            if (wp != null && wp.IsDead())
            {
                // round MP4: a remote player's AwardKill runs on their own machine, never here - the raider that was fighting them
                // says it as it lets go (the killer, when the server knows it; the global cooldown keeps a band to one line).
                // Review m7: tested before the new target, so a raider turning straight to another living player says it too
                if (s.Engaged && !(wp is EntityPlayerLocal) && (wp.entityThatKilledMe == null || wp.entityThatKilledMe == self)) OnKilledPlayer(self, wp);
                if (!npAlive) s.Engaged = false;
            }
            if (npAlive)
            {
                bool fresh = t - s.LastHadPlayer > SpotForgetSeconds;    // L1: no player target for 20 s, lost line or not
                s.Engaged = true;
                s.LastHadPlayer = t;
                if (fresh) Fire(self, Spot, s, false, false);
                return;
            }
        }

        /// <summary>EntityAlive.AwardKill's postfix on the dying local player: their killer, a raider, taunts.</summary>
        public static void OnKilledPlayer(EntityAlive killer, EntityPlayer victim)
        {
            if (!ScConfig.RaiderTaunts || killer == null || victim == null || !ScXnpc.IsRaider(killer) || killer.IsDead()) return;
            State s = Get(killer);
            float t = Time.unscaledTime;
            if (t - s.KilledAt < KillRepeatSeconds) return;
            s.Engaged = false;
            if (Fire(killer, Killed, s, true, false)) s.KilledAt = t;
        }

        /// <summary>EntityAlive.PlayGiveUpSound's postfix (SCore: an investigation blocked): lost.</summary>
        public static void OnGaveUp(EntityAlive self)
        {
            if (!ScConfig.RaiderTaunts || self == null || !ScXnpc.IsRaider(self) || self.IsDead()) return;
            State s = Get(self);
            s.Engaged = false;
            Fire(self, Lost, s, false, false);
        }

        /// <summary>SCore's UAITaskMoveToInvestigate.Start postfix: a raider not fighting a player goes to look - search.</summary>
        public static void OnInvestigate(EntityAlive self)
        {
            if (!ScConfig.RaiderTaunts || self == null || !ScXnpc.IsRaider(self) || self.IsDead()) return;
            if (self.GetAttackTarget() is EntityPlayer) return;
            Fire(self, Search, Get(self), false, false);
        }

        /// <summary>A player's landed, non-fatal hit (Patches_Trust): retreat once under RaiderTauntRetreatHealth, else hit with RaiderTauntHitChance.</summary>
        public static void OnHitBy(EntityAlive self, EntityPlayer player)
        {
            if (!ScConfig.RaiderTaunts || self == null || player == null || !ScXnpc.IsRaider(self) || self.IsDead()) return;
            State s = Get(self);
            int max = self.GetMaxHealth();
            float share = max > 0 ? self.Health / (float)max : 1f;
            if (!s.Retreated && share < ScConfig.RaiderTauntRetreatHealth)
            {
                s.Retreated = true;
                Fire(self, Retreat, s, true, false);
                return;
            }
            if (rnd.NextDouble() < ScConfig.RaiderTauntHitChance) Fire(self, Hit, s, false, false);
        }

        /// <summary>The raider's death (Patches_Npc's SetDead postfix, which runs again for every hit on the body): dying, once.</summary>
        public static void OnDied(EntityAlive self)
        {
            if (!ScConfig.RaiderTaunts || self == null || !ScXnpc.IsRaider(self)) return;
            State s = Get(self);
            if (s.Died) return;
            s.Died = true;
            Fire(self, Dying, s, true, true);
        }

        // ------------------------------------------------------------------ the tick ----

        /// <summary>Once a second: lost (no player target for a while), the idle mutter, the state of raiders gone.</summary>
        public static void Tick()
        {
            if (!ScConfig.RaiderTaunts) return;
            float now = Time.unscaledTime;
            if (now < nextTick) return;
            nextTick = now + Interval;
            try
            {
                if (!ScCommunity.IsHost) return;
                World w = ScCommunity.World;
                // round MP4: any player who hears SC's lines (was the host's own player)
                if (w == null || !AnyListener()) return;
                if (now >= nextPrune) Prune(w, now);
                near.Clear();
                List<Entity> list = w.Entities.list;
                for (int i = 0; i < list.Count; i++)
                {
                    EntityAlive e = list[i] as EntityAliveSDXV4;
                    if (e == null || !PlayerNear(e.position, true) || e.IsDead() || !ScXnpc.IsRaider(e)) continue;
                    State s = Get(e);
                    EntityAlive t = e.GetAttackTarget();
                    if (t is EntityPlayer && !t.IsDead())
                    {
                        s.Engaged = true;               // the hook saw it, or it was set before you came near (then no spot)
                        s.LastHadPlayer = now;
                        continue;
                    }
                    if (s.Engaged && now - s.LastHadPlayer >= LostAfterSeconds)
                    {
                        s.Engaged = false;
                        if (PlayerNear(e.position, false)) Fire(e, Lost, s, false, false);   // round MP4: a living player near (was: you alive)
                        continue;
                    }
                    if (!s.Engaged && t == null && e.GetRevengeTarget() == null) near.Add(e);
                }
                if (nextIdle < 0f) nextIdle = now + IdleGap();
                else if (now >= nextIdle)
                {
                    nextIdle = now + IdleGap();
                    // round MP4: a raider with a living player near mutters (was: one near you, while you live)
                    for (int i = near.Count - 1; i >= 0; i--) if (!PlayerNear(near[i].position, false)) near.RemoveAt(i);
                    if (near.Count > 0)
                    {
                        EntityAlive e = near[rnd.Next(near.Count)];
                        Fire(e, Idle, Get(e), false, false);
                    }
                }
            }
            catch (Exception e)
            {
                ScLog.Once("voice-taunts", e);
            }
        }

        /// <summary>Round MP4: a player who hears SC's lines - the host's own, or a remote one with SC's protocol; living ones only unless alsoDead.</summary>
        private static bool Listens(EntityPlayer p, bool alsoDead)
        {
            return p != null && (alsoDead || !p.IsDead()) && (p is EntityPlayerLocal || ScNet.ClientMatches(p));
        }

        /// <summary>Round MP4: such a player is on the server, dead or alive (the old test: the host's own player exists).</summary>
        private static bool AnyListener()
        {
            List<EntityPlayer> all = ScPlayers.All;
            for (int i = 0; i < all.Count; i++) if (Listens(all[i], true)) return true;
            return false;
        }

        /// <summary>Round MP4: such a player within RaiderTauntRange (3D) of pos - the old test was the host's own player's, dead or alive (alsoDead).</summary>
        private static bool PlayerNear(Vector3 pos, bool alsoDead)
        {
            float r2 = ScConfig.RaiderTauntRange * ScConfig.RaiderTauntRange;
            List<EntityPlayer> all = ScPlayers.All;
            for (int i = 0; i < all.Count; i++)
            {
                EntityPlayer p = all[i];
                if (Listens(p, alsoDead) && (pos - p.position).sqrMagnitude <= r2) return true;
            }
            return false;
        }

        private static float IdleGap()
        {
            return ScConfig.RaiderTauntIdleSeconds * (0.5f + (float)rnd.NextDouble());
        }

        private static void Prune(World w, float now)
        {
            nextPrune = now + 30f;
            gone.Clear();
            foreach (int id in states.Keys)
            {
                Entity e = w.GetEntity(id);
                if (e == null || !ScXnpc.IsRaider(e)) gone.Add(id);
            }
            foreach (int id in gone) states.Remove(id);
        }

        // ------------------------------------------------------------------ saying ----

        /// <summary>
        /// A taunt on this raider when it is near you and its pacing allows; true when something was said. Round MP4: the server
        /// only (a client's hooks stay quiet: the server's line reaches it), near any player; heard by everyone in earshot.
        /// </summary>
        private static bool Fire(EntityAlive raider, string ev, State s, bool passOwn, bool passGlobal)
        {
            try
            {
                if (!ScCommunity.IsHost || raider == null) return false;
                if (!PlayerNear(raider.position, true)) return false;
                float now = Time.unscaledTime;
                if (!passOwn && now < s.Next) return false;
                if (!passGlobal && now < globalNext) return false;
                if (ev != Dying && ScVoice.Busy(raider)) return false;
                if (!ScBarks.Say(raider, "taunt." + ev, "taunt (" + ev + ")", false, ev == Dying, null)) return false;
                s.Next = now + ScConfig.RaiderTauntCooldown;
                globalNext = now + ScConfig.RaiderTauntGlobalCooldown;
                int n;
                fired.TryGetValue(ev, out n);
                fired[ev] = n + 1;
                return true;
            }
            catch (Exception e)
            {
                ScLog.Once("voice-taunt-" + ev, e);
                return false;
            }
        }

        // ------------------------------------------------------------------ console ----

        /// <summary>The nearest living raider within r metres of a point, or null.</summary>
        public static EntityAlive NearestRaider(Vector3 at, float r)
        {
            World w = ScCommunity.World;
            if (w == null) return null;
            EntityAlive best = null;
            float bestSq = r * r;
            List<Entity> list = w.Entities.list;
            for (int i = 0; i < list.Count; i++)
            {
                EntityAlive e = list[i] as EntityAliveSDXV4;
                if (e == null || e.IsDead() || !ScXnpc.IsRaider(e)) continue;
                float sq = (e.position - at).sqrMagnitude;
                if (sq < bestSq) { best = e; bestSq = sq; }
            }
            return best;
        }

        /// <summary>sc voice taunt &lt;event&gt;: this raider taunts now, its pacing and range aside; what was said, or why not.</summary>
        public static string TauntNow(EntityAlive raider, string ev)
        {
            if (raider == null) return "no raider";
            string who = ScCommunity.DisplayName(raider) + " (" + raider.entityId + ")";
            if (!ScXnpc.IsRaider(raider)) return who + " is not one of SC's raiders";
            if (Array.IndexOf(Events, ev) < 0) return "no taunt event " + ev + " (" + string.Join(", ", Events) + ")";
            ScBarks.Pick p = ScBarks.Resolve(raider, ScBarks.KeysOf("taunt." + ev));
            if (p == null) return who + " has no voice";
            if (!ScBarks.Say(raider, "taunt." + ev, "taunt (" + ev + ", console)", false, ev == Dying, null))
                return who + ": taunt." + ev + " has no recording in " + (p.Voice ?? "any voice") + " - silent (VoiceBarkDebug shows a silent one)";
            return who + " says " + p.Key + (p.Path != null ? " in " + p.Voice : " (silent: no recording, VoiceBarkDebug)") + ": \"" + p.Words + "\"";
        }

        /// <summary>sc voice barks: the taunts said this session, by event.</summary>
        public static string Status()
        {
            var parts = new List<string>();
            foreach (string ev in Events)
            {
                int n;
                if (fired.TryGetValue(ev, out n)) parts.Add(ev + "=" + n);
            }
            return "taunts " + (ScConfig.RaiderTaunts ? "on" : "off") + " (" + (parts.Count > 0 ? string.Join(", ", parts.ToArray()) : "none yet") + ", "
                   + states.Count + " raider(s) tracked)";
        }
    }
}
