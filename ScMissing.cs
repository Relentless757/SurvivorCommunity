using System;
using System.Collections.Generic;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Members whose entity is gone from the world - not merely unloaded. A resident is missing when the player is
    /// home, the resident's post chunk has been loaded and settled for GraceSeconds, and no entity with their id
    /// exists anywhere loaded (the watchdog keeps residents at their post, so one that far out is not "wandered off").
    /// Something outside the mod removed the body - a chunk rebuilt under them, a world tool - while the registry
    /// still holds the survivor.
    ///
    /// The window shows them as Missing, and "Call back" (or the console's "sc restore") respawns them at their post
    /// from their record: the class, look and worn gear, name, skills, specialty, traits and mood. What they carried
    /// is lost. They come back under a new entity id; the old id goes on the Doomed list, so if the old body ever
    /// loads again it is removed rather than doubled. Host only.
    /// </summary>
    public static class ScMissing
    {
        public const float GraceSeconds = 45f;

        private static readonly Dictionary<int, float> absentSince = new Dictionary<int, float>();
        private static readonly HashSet<int> missing = new HashSet<int>();
        private static readonly HashSet<int> told = new HashSet<int>();
        private static float nextTick;

        public static void Reset()
        {
            absentSince.Clear();
            missing.Clear();
            told.Clear();
            nextTick = 0f;
        }

        public static bool IsMissing(Member m) { return m != null && missing.Contains(m.Id); }

        public static int Count { get { return missing.Count; } }

        /// <summary>ScTicker, every second (works every 2).</summary>
        public static void Tick(World w, EntityPlayerLocal player)
        {
            try
            {
                if (w == null || player == null || ScRegistry.Members.Count == 0) return;
                float now = Time.unscaledTime;
                if (now < nextTick) return;
                nextTick = now + 2f;
                bool atHome = ScZone.HasHome && ScZone.Contains(player.GetBlockPosition());
                foreach (var m in ScRegistry.Members.Values)
                {
                    // followers, runners and posted members are hidden or away by design (E12: and one still on the way, Arriving)
                    if (m.Status != MemberStatus.Resident) { Clear(m.Id); continue; }
                    if (ScNpc.HasBody(w, m.Id)) { Clear(m.Id); told.Remove(m.Id); continue; }    // being built counts, unforced
                    if (!atHome || !PostSettled(w, m.Post)) { absentSince.Remove(m.Id); continue; }
                    float since;
                    if (!absentSince.TryGetValue(m.Id, out since)) { absentSince[m.Id] = now; continue; }
                    if (now - since < GraceSeconds || !missing.Add(m.Id)) continue;
                    ScLog.Warn(m.Name + " (id " + m.Id + ") is a resident but no such entity exists with their post loaded for "
                               + GraceSeconds + " s - marked missing (Community window: Call back)");
                    if (told.Add(m.Id)) ScCommunity.Tell(player, string.Format(Localization.Get("scMissingTip"), m.Name), "ui_denied");
                }
            }
            catch (Exception e)
            {
                ScLog.Once("missing-tick", e);
            }
        }

        private static void Clear(int id)
        {
            absentSince.Remove(id);
            missing.Remove(id);
        }

        /// <summary>The post's chunk and its eight neighbours are loaded and past decoration.</summary>
        private static bool PostSettled(World w, Vector3i post)
        {
            int cx = World.toChunkXZ(post.x), cz = World.toChunkXZ(post.z);
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    Chunk ch = w.GetChunkSync(cx + dx, cz + dz) as Chunk;
                    if (ch == null || ch.NeedsDecoration || ch.InProgressUnloading) return false;
                }
            }
            return true;
        }

        /// <summary>"Call back": a missing (or, from the console, any unloaded) resident respawns at their post.</summary>
        public static bool Restore(Member m, EntityPlayerLocal player, bool force, out string error)
        {
            error = null;
            try
            {
                if (!ScCommunity.IsHost) { error = "scHostOnly"; return false; }
                World w = ScCommunity.World;
                if (m == null || w == null) { error = "scInternal"; return false; }
                if (m.Status == MemberStatus.OnRun) { error = "scOnRun"; return false; }      // a runner out of range has no body by design
                if (m.Status == MemberStatus.Arriving) { error = "scEncJoinOnTheWay"; return false; }     // E12: still on the way - they turn up at home
                if (ScNpc.IsFramework(w.GetEntity(m.Id))) { error = "scMissingNotMissing"; return false; }
                if (!force && !IsMissing(m)) { error = "scMissingNotYet"; return false; }
                if (!ScZone.IsLoaded(m.Post)) { error = "scNotLoaded"; return false; }
                if (!ScSpawn.ClassOf(m, out _)) { error = "scMissingNoClass"; return false; }

                // the same class, face, hair, worn gear and name (ScSpawn); the registry is re-keyed below
                Vector3 at = ScRadio.SpotNear(w, m.Post, 4);
                EntityAlive npc = ScSpawn.Body(w, m, at, Vector3.zero, true);
                if (npc == null) { error = "scInternal"; return false; }

                // the one re-key every respawn under a new id uses (migration M4.4): the registry entry, the birth time,
                // the threat marker, the doomed list (should the old body ever load again, it goes rather than doubles), the
                // look cache, a rescue quest's member id, the runner session and the old body's inventory window
                int oldId = m.Id;
                ScRunners.Rekey(w, player, m, npc, oldId, true);
                m.MissingDays = 0;
                m.StandInId = -1;
                Clear(oldId);
                told.Remove(oldId);

                npc.SetSpawnerSource(EnumSpawnerSource.StaticSpawner);
                npc.Buffs.SetCustomVar("Persist", 1f);
                ScCommunity.StampCvars(m, npc, player);
                ScTraits.ApplyBuffs(m, npc);
                ScCommunity.ApplyAiPackage(npc);
                ScCommunity.Station(m, npc);
                ScRegistry.SaveAtomic();
                if (player != null) ScCommunity.Tell(player, string.Format(Localization.Get("scMissingRestored"), m.Name), "");
                ScLog.Out(m.Name + " called back: respawned at the post " + m.Post + " as entity " + m.Id + " (was " + oldId + ", now on the doomed list)");
                return true;
            }
            catch (Exception e)
            {
                ScLog.Once("missing-restore", e);
                error = "scInternal";
                return false;
            }
        }
    }
}
