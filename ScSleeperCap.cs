using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// I2, the zombie-cap quirk (research\ideas\DESIGN.md section 12). Vanilla's SleeperVolume.UpdateSpawn spawns a room's
    /// sleepers only while AIDirector.CanSpawn(2.1f) holds: EnemyCount, counted world-wide every 20 ticks, below
    /// MaxSpawnedZombies x 2.1 (about 134 at the default 64). A room whose step meets a full cap before anything spawned
    /// closes cleared with nobody spawned ("None spawned, canSpawn False", V:SleeperVolume.cs:1087-1100) and nothing ever
    /// gives it a respawn time: respawnTime stays ulong.MaxValue, so it is empty for good. A room the cap cuts short closes
    /// early, and in an infested POI the special, added last, is the first thing it drops.
    ///
    /// Layer A (every POI, SleeperCapFix): the UpdateSpawn postfix gives a room that closed that way in this very step
    /// SleeperCapRetryTicks to come due - vanilla's own due path: Touch pushes it while a player stays near, and the first
    /// touch after it resets and rolls the room.
    /// Layer B (infested rooms, SleeperCapPause): the UpdateSpawn prefix holds a step the cap would end early while a player
    /// is inside vanilla's spawned-room box, at most SleeperCapPauseMaxTicks; the room goes on from where it stopped once the
    /// cap frees, so the special still comes. With nobody near, or once the wait runs out, vanilla closes it and Layer A
    /// takes over.
    /// Layer C (saves from before, SleeperCapFix): the UpdatePlayerTouched prefix makes such a room due as a player touches
    /// it, so vanilla rolls it in that same call - once per room per session: a room whose spawn points are all blocked
    /// closes the same way with the cap free, and repairing it on every touch would roll it every few ticks.
    ///
    /// Never an enclave's room: E1 keeps those cleared with the same saved state (ScEnclaves.KeepCleared). Unfought and
    /// Rolled live here, shared with I1 (ScInfestations). No save data: the pauses and counters are per session. Main
    /// thread (the world's sleeper tick, the console).
    /// </summary>
    public static class ScSleeperCap
    {
        /// <summary>Vanilla's priority for sleeper rooms: UpdateSpawn's AIDirector.CanSpawn(2.1f).</summary>
        public const float SleeperPriority = 2.1f;
        /// <summary>Vanilla's lift of a touching player's position (SleeperVolume.cPlayerYOffset, CheckTouching).</summary>
        private const float PlayerYOffset = 0.8f;
        /// <summary>A pause not checked for this many world ticks is an old one: the room was reset and rolled since.</summary>
        private const ulong PauseStale = 200;
        /// <summary>The scan's room lines (the count covers them all).</summary>
        private const int ScanLinesMax = 30;

        private struct Pause
        {
            public ulong Start, Last;
        }

        /// <summary>Layer B: the infested rooms held at the cap now, with when the hold began and when it was last checked.</summary>
        private static readonly Dictionary<SleeperVolume, Pause> pauses = new Dictionary<SleeperVolume, Pause>();
        /// <summary>Layer C: the rooms a touch has looked at this session (repaired or not), never looked at again.</summary>
        private static readonly HashSet<SleeperVolume> touched = new HashSet<SleeperVolume>();

        private static int retries, repairedTouch, repairedConsole, paused, resumed, timedOut, leftAlone;

        /// <summary>Per-world reset (ModEvents.GameStarting).</summary>
        public static void Reset()
        {
            pauses.Clear();
            touched.Clear();
            retries = repairedTouch = repairedConsole = paused = resumed = timedOut = leftAlone = 0;
        }

        // ------------------------------------------------------------------ shared with I1 ----

        /// <summary>
        /// A trigger room (flags &amp; 7 = Trigger) rolls only when its trigger fires - a door opened, a switch pressed - never
        /// on approach: CheckTouching skips one with no sleepers tracked (IsTriggerAndNoRespawn, V:SleeperVolume.cs:301-312,
        /// :656).
        /// </summary>
        public static bool IsTriggerRoom(SleeperVolume v)
        {
            return (v.flags & 7) == (int)SleeperVolume.ETriggerType.Trigger;
        }

        /// <summary>How many the room's roll spawns in all (every GroupCount of it).</summary>
        public static int Rolled(SleeperVolume v)
        {
            List<SleeperVolume.GroupCount> l = v.groupCountList;
            int total = 0;
            if (l != null) for (int i = 0; i < l.Count; i++) total += l[i].count;
            return total;
        }

        /// <summary>
        /// A room vanilla closed with nobody spawned while it has spawn points and something to roll at its gamestage: the
        /// enemy cap was full when it started (infestations.md 1.5), so it was never fought. A room whose group has no stage at
        /// its gamestage, or that rolls nothing (its max is 0, or a min-0 roll came out 0), is empty by design and counts as
        /// cleared; so does a trigger room, which approaching it would never roll again. It cannot tell a room whose spawn
        /// points were all blocked (World.CanSleeperSpawnAtPos) from one the cap closed.
        /// </summary>
        public static bool Unfought(SleeperVolume v)
        {
            if (!v.wasCleared || v.numSpawned != 0 || v.respawnMap.Count > 0 || v.isSpawning || v.spawnCountMax <= 0 || IsTriggerRoom(v)) return false;
            if (v.spawnPointList == null || v.spawnPointList.Count == 0) return false;
            // a room whose roll came out 0 (spawnCountMin 0) keeps no GroupCount: empty by design, as vanilla's clear quest
            // counts it (SleeperEventData drops any wasCleared room); the cap and blocked spawn points leave the roll there
            if (Rolled(v) <= 0) return false;
            GameStageGroup g = GameStageGroup.TryGet(v.groupName ?? "") ?? GameStageGroup.TryGet(GameStageGroup.cDefaultGroupName);
            return g != null && g.spawner != null && g.spawner.GetStage(v.gameStage) != null;
        }

        /// <summary>
        /// The state a cap-closed room is saved in, never due again: cleared, respawnTime never, nobody spawned or tracked.
        /// An enclave's rooms are saved the same way (ScEnclaves.KeepCleared).
        /// </summary>
        public static bool NeverDue(SleeperVolume v)
        {
            return v.wasCleared && v.respawnTime == ulong.MaxValue && v.numSpawned == 0 && v.respawnMap.Count == 0 && !v.isSpawned && !v.isSpawning;
        }

        // ------------------------------------------------------------------ the hooks (Patches_Infestations; main thread) ----

        /// <summary>
        /// Layer B, the UpdateSpawn prefix: true holds this step (the original is skipped; the room stays spawning). Only on a
        /// call where vanilla would run a step (its spawnDelay count is down), for an infested room - not an enclave's - with
        /// spawn points, no restore list and no script running, something left to spawn that its gamestage has a stage for
        /// (else vanilla closes it with the cap free too), the cap full and a player inside vanilla's spawned-room box
        /// (BoxMin - unpadding to BoxMax + unpadding, the box CheckTrigger keeps a spawned room touched in). A held step counts
        /// as vanilla's own (spawnDelay 2), so the next check comes at vanilla's pace.
        /// </summary>
        public static bool Hold(SleeperVolume v, World w)
        {
            if (v == null || w == null || !v.isSpawning || v.spawnDelay > 1) return false;
            Pause p = default(Pause);
            bool was = pauses.Count > 0 && pauses.TryGetValue(v, out p);
            ulong now = w.worldTime;
            if (was && (now < p.Last || now - p.Last > PauseStale))
            {
                pauses.Remove(v);       // an old pause: the room was reset (a quest, a re-arm) and rolled since
                was = false;
            }
            if (!Holdable(v))
            {
                if (was) pauses.Remove(v);
                return false;
            }
            PrefabInstance pi = v.PrefabInstance;
            if (AIDirector.CanSpawn(SleeperPriority))
            {
                if (was)
                {
                    pauses.Remove(v);
                    resumed++;
                    ScLog.Out("sleeper cap: infested room at " + v.BoxMin + " in " + Name(pi) + " goes on from " + v.numSpawned + " of " + Rolled(v)
                              + " after " + (now - p.Start) + " ticks at the cap");
                }
                return false;
            }
            if (!PlayerInBox(v, w))
            {
                if (was)
                {
                    pauses.Remove(v);
                    leftAlone++;
                    ScLog.Out("sleeper cap: infested room at " + v.BoxMin + " in " + Name(pi) + " - nobody near it any more, vanilla closes it at "
                              + v.numSpawned + " of " + Rolled(v));
                }
                return false;
            }
            if (!was)
            {
                pauses[v] = new Pause { Start = now, Last = now };
                paused++;
                ScLog.Out("sleeper cap: infested room at " + v.BoxMin + " in " + Name(pi) + " paused at " + v.numSpawned + " of " + Rolled(v) + " - the enemy cap is full ("
                          + CapText() + "); it waits while a player is near, at most " + ScConfig.SleeperCapPauseMaxTicks + " ticks");
            }
            else
            {
                if (now - p.Start >= (ulong)ScConfig.SleeperCapPauseMaxTicks)
                {
                    pauses.Remove(v);
                    timedOut++;
                    ScLog.Out("sleeper cap: infested room at " + v.BoxMin + " in " + Name(pi) + " waited " + (now - p.Start) + " ticks at the cap - vanilla closes it at "
                              + v.numSpawned + " of " + Rolled(v));
                    return false;
                }
                p.Last = now;
                pauses[v] = p;
            }
            v.spawnDelay = 2;
            return true;
        }

        private static bool Holdable(SleeperVolume v)
        {
            if (!ScConfig.SleeperCapPause || ScConfig.SleeperCapPauseMaxTicks <= 0) return false;
            PrefabInstance pi = v.PrefabInstance;
            if (ScInfestations.At(pi) == null || ScEnclaves.IsEnclavePoi(pi)) return false;
            if (v.spawnsAvailable == null || v.spawnPointList == null || v.spawnPointList.Count == 0) return false;
            if (v.respawnList != null && v.respawnList.Count > 0) return false;      // restoring: vanilla's restores ignore the cap
            if (v.minScript != null && v.minScript.IsRunning()) return false;      // a running script keeps the room open itself
            if (Rolled(v) <= v.numSpawned) return false;                           // nothing left: it closes as it should
            return NextHasStage(v);
        }

        /// <summary>Has the group vanilla spawns from next (the GroupCount past numSpawned) a stage at the room's gamestage?</summary>
        private static bool NextHasStage(SleeperVolume v)
        {
            List<SleeperVolume.GroupCount> l = v.groupCountList;
            if (l == null) return false;
            int sum = 0;
            for (int i = 0; i < l.Count; i++)
            {
                sum += l[i].count;
                if (sum <= v.numSpawned) continue;
                // vanilla falls back to GroupGenericZombie for a missing group (V:SleeperVolume.cs:1024-1030)
                GameStageGroup g = GameStageGroup.TryGet(l[i].groupName ?? "") ?? GameStageGroup.TryGet("GroupGenericZombie");
                return g != null && g.spawner != null && g.spawner.GetStage(v.gameStage) != null;
            }
            return false;
        }

        /// <summary>Is a player (not a spectator) inside vanilla's spawned-room box, measured as CheckTouching measures?</summary>
        private static bool PlayerInBox(SleeperVolume v, World w)
        {
            Vector3i min = v.BoxMin - SleeperVolume.unpadding, max = v.BoxMax + SleeperVolume.unpadding;
            List<EntityPlayer> players = w.Players.list;
            for (int i = 0; i < players.Count; i++)
            {
                EntityPlayer pl = players[i];
                if (pl == null || pl.IsSpectator) continue;
                Vector3 pos = pl.position;
                pos.y += PlayerYOffset;
                if (pos.x >= min.x && pos.x < max.x && pos.y >= min.y && pos.y < max.y && pos.z >= min.z && pos.z < max.z) return true;
            }
            return false;
        }

        /// <summary>
        /// Layer A, the UpdateSpawn postfix, for a room that stopped spawning in this call: closed with nobody spawned and
        /// never due again while the cap is full - the same frame and EnemyCount vanilla's step read - and unfought (spawn
        /// points, a roll, a stage): due again SleeperCapRetryTicks from now. Never an enclave's room.
        /// </summary>
        public static void AfterClose(SleeperVolume v, World w)
        {
            if (pauses.Count > 0) pauses.Remove(v);
            if (!ScConfig.SleeperCapFix || w == null) return;
            if (!v.wasCleared || v.numSpawned != 0 || v.respawnMap.Count != 0 || v.respawnTime != ulong.MaxValue) return;
            if (AIDirector.CanSpawn(SleeperPriority)) return;
            PrefabInstance pi = v.PrefabInstance;
            if (!Unfought(v) || ScEnclaves.IsEnclavePoi(pi)) return;
            ulong ticks = (ulong)ScConfig.SleeperCapRetryTicks;
            v.respawnTime = w.worldTime + ticks;
            retries++;
            ScLog.Out("sleeper cap: room at " + v.BoxMin + " in " + Name(pi) + " closed with nobody spawned at the enemy cap (" + CapText() + ") - due again in "
                      + ticks + " ticks");
        }

        /// <summary>
        /// Layer C, first in SC's UpdatePlayerTouched prefix (before I1's BeforeRoll copies vanilla's early-return test): a
        /// room saved cap-closed before this fix - never due, unfought, not an enclave's - is made due now, so vanilla resets
        /// and rolls it in this same call. Two field reads turn away nearly every call (a player near a cleared room touches
        /// it every few ticks); each room is looked at once per session. Needs the registry: the enclave list comes with it.
        /// </summary>
        public static void OnTouch(SleeperVolume v, World w)
        {
            if (!v.wasCleared || v.respawnTime != ulong.MaxValue) return;
            if (v.isSpawned || v.numSpawned != 0 || v.respawnMap.Count != 0) return;
            if (!ScConfig.SleeperCapFix || w == null || !ScRegistry.Loaded || !touched.Add(v)) return;
            PrefabInstance pi = v.PrefabInstance;
            if (ScEnclaves.IsEnclavePoi(pi) || !Unfought(v)) return;
            v.respawnTime = w.worldTime;
            repairedTouch++;
            ScLog.Out("sleeper cap: room at " + v.BoxMin + " in " + Name(pi) + " was closed empty by the enemy cap before - repaired, it spawns now");
        }

        // ------------------------------------------------------------------ helpers ----

        private static string Name(PrefabInstance pi)
        {
            return pi != null ? ScSecurity.DisplayName(pi) : "no POI";
        }

        private static float Limit()
        {
            return GamePrefs.GetInt(EnumGamePrefs.MaxSpawnedZombies) * SleeperPriority;
        }

        /// <summary>"12 enemies, limit 134.4": vanilla's test is EnemyCount &lt; MaxSpawnedZombies x 2.1.</summary>
        private static string CapText()
        {
            return GameStats.GetInt(EnumGameStats.EnemyCount) + " enemies, limit " + Limit().ToString("0.#", CultureInfo.InvariantCulture);
        }

        // ------------------------------------------------------------------ console (sc sleepers) ----

        /// <summary>sc sleepers [status]</summary>
        public static void StatusLines(List<string> into)
        {
            World w = ScCommunity.World;
            into.Add("zombie-cap fix (I2): SleeperCapFix " + (ScConfig.SleeperCapFix ? "on (a room the cap closed empty is due again after " + ScConfig.SleeperCapRetryTicks
                     + " ticks; one saved that way before is repaired as you come near)" : "OFF") + ", SleeperCapPause "
                     + (ScConfig.SleeperCapPause && ScConfig.SleeperCapPauseMaxTicks > 0 ? "on (an infested room the cap cuts short waits while a player is near, at most "
                        + ScConfig.SleeperCapPauseMaxTicks + " ticks)" : "OFF"));
            into.Add("now: " + CapText() + " (MaxSpawnedZombies " + GamePrefs.GetInt(EnumGamePrefs.MaxSpawnedZombies) + " x 2.1) - "
                     + (AIDirector.CanSpawn(SleeperPriority) ? "sleepers can spawn" : "AT THE CAP: no sleeper spawns"));
            into.Add("this session: " + retries + " room(s) the cap closed empty given a retry, " + repairedTouch + " repaired as a player came near, "
                     + repairedConsole + " from the console; " + paused + " infested room pause(s): " + resumed + " went on, " + timedOut + " timed out, "
                     + leftAlone + " closed with nobody near");
            if (w == null) return;
            ulong now = w.worldTime;
            foreach (KeyValuePair<SleeperVolume, Pause> kv in pauses)
            {
                SleeperVolume v = kv.Key;
                if (v == null || !v.isSpawning || now < kv.Value.Last || now - kv.Value.Last > PauseStale) continue;
                into.Add("paused now: room at " + v.BoxMin + " in " + Name(v.PrefabInstance) + " - " + v.numSpawned + " of " + Rolled(v) + " spawned, "
                         + (now - kv.Value.Start) + " of " + ScConfig.SleeperCapPauseMaxTicks + " ticks");
            }
        }

        /// <summary>Every room of the world's real POIs (ScPoiIndex) saved never due and unfought, enclave buildings apart.</summary>
        private static void Find(World w, List<SleeperVolume> found, List<PrefabInstance> owners, out int enclaveRooms, out int claimed)
        {
            enclaveRooms = claimed = 0;
            foreach (PoiEntry e in ScPoiIndex.All)
            {
                PrefabInstance pi = e.Pi;
                if (pi == null) continue;
                SleeperVolume[] rooms;
                try { rooms = pi.sleeperVolumes.ToArray(); }        // copied: the chunk thread appends to it
                catch (Exception) { continue; }
                bool enclave = ScEnclaves.IsEnclavePoi(pi);
                foreach (SleeperVolume v in rooms)
                {
                    if (v == null || !NeverDue(v)) continue;
                    if (enclave) { enclaveRooms++; continue; }
                    if (!Unfought(v)) continue;
                    found.Add(v);
                    owners.Add(pi);
                    if (GameUtils.CheckForAnyPlayerHome(w, v.BoxMin, v.BoxMax) != GameUtils.EPlayerHomeType.None) claimed++;
                }
            }
        }

        /// <summary>sc sleepers scan</summary>
        public static void ScanLines(List<string> into)
        {
            World w = ScCommunity.World;
            if (w == null || !ScCommunity.IsHost) { into.Add("host only, with a world"); return; }
            if (!ScPoiIndex.EnsureBuilt()) { into.Add("no POI list on this world"); return; }
            var found = new List<SleeperVolume>();
            var owners = new List<PrefabInstance>();
            int enclaveRooms, claimed;
            Find(w, found, owners, out enclaveRooms, out claimed);
            var pois = new HashSet<PrefabInstance>(owners);
            into.Add(found.Count + " room(s) in " + pois.Count + " POI(s) closed empty by the enemy cap and never due again" + (claimed > 0 ? " (" + claimed
                     + " in a claimed building: vanilla keeps a claimed room quiet)" : "") + "; " + enclaveRooms + " room(s) of enclave buildings, kept clear by the enclaves, left alone"
                     + (found.Count > 0 ? " - sc sleepers repair makes them due now" : ""));
            EntityPlayerLocal pl = ScCommunity.LocalPlayer;
            var order = new List<int>();
            for (int i = 0; i < found.Count; i++) order.Add(i);
            if (pl != null)
            {
                Vector3 at = pl.position;
                order.Sort((a, b) => (found[a].Center - at).sqrMagnitude.CompareTo((found[b].Center - at).sqrMagnitude));
            }
            for (int k = 0; k < order.Count && k < ScanLinesMax; k++)
            {
                SleeperVolume v = found[order[k]];
                PrefabInstance pi = owners[order[k]];
                bool home = GameUtils.CheckForAnyPlayerHome(w, v.BoxMin, v.BoxMax) != GameUtils.EPlayerHomeType.None;
                into.Add("  " + Name(pi) + " (" + (pi.prefab != null ? pi.prefab.PrefabName : "?") + ") room at " + v.BoxMin + ": rolled " + Rolled(v) + " at gamestage "
                         + v.gameStage + (home ? ", claimed" : "") + (pl != null ? " - " + ScEnclaves.Where(pl.position, v.Center) : ""));
            }
            if (order.Count > ScanLinesMax) into.Add("  ... and " + (order.Count - ScanLinesMax) + " more");
        }

        /// <summary>sc sleepers repair: every room the scan lists made due now; each rolls at its next touch, as a room never fought.</summary>
        public static void RepairLines(List<string> into)
        {
            World w = ScCommunity.World;
            if (w == null || !ScCommunity.IsHost) { into.Add("host only, with a world"); return; }
            if (!ScPoiIndex.EnsureBuilt()) { into.Add("no POI list on this world"); return; }
            var found = new List<SleeperVolume>();
            var owners = new List<PrefabInstance>();
            int enclaveRooms, claimed;
            Find(w, found, owners, out enclaveRooms, out claimed);
            ulong now = w.worldTime;
            foreach (SleeperVolume v in found) v.respawnTime = now;
            repairedConsole += found.Count;
            var pois = new HashSet<PrefabInstance>(owners);
            string line = found.Count + " room(s) in " + pois.Count + " POI(s) made due now: each spawns the next time a player comes near it"
                          + (claimed > 0 ? " (" + claimed + " in a claimed building stay quiet while it is claimed, as vanilla's)" : "") + "; " + enclaveRooms
                          + " room(s) of enclave buildings left alone";
            into.Add(line);
            if (found.Count > 0) ScLog.Out("sleeper cap: " + line + " (console)");
        }
    }
}
