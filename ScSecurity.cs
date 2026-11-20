using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// "Cleared" = a POI with no remaining enemies, judged the way vanilla's own clear quests do: every
    /// non-quest-exclude sleeper volume the prefab defines exists as a world volume (else the POI was never
    /// explored), each was cleared by a player, none holds living or pending sleepers, and no living
    /// EntityEnemy stands in the footprint. Plus the POI lookups the recruit favour needs. Main thread only.
    /// </summary>
    public static class ScSecurity
    {
        private static readonly List<Entity> scratch = new List<Entity>();
        private static readonly List<PrefabInstance> prefabScratch = new List<PrefabInstance>();
        public static readonly FastTags<TagGroup.Poi> IgnoreTags = FastTags<TagGroup.Poi>.Parse("part,streettile,navonly,hideui");

        public static bool IsRealPoi(PrefabInstance pi)
        {
            if (pi == null || pi.prefab == null) return false;
            Prefab p = pi.prefab;
            string name = p.PrefabName ?? "";
            if (p.bTraderArea) return false;
            if (p.Tags.Test_AnySet(IgnoreTags)) return false;
            if (name.IndexOf("rwg_tile", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (name.StartsWith("part_", StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        public static string DisplayName(PrefabInstance pi)
        {
            if (pi == null || pi.prefab == null) return "?";
            try
            {
                string s = pi.prefab.LocalizedName;
                if (!string.IsNullOrEmpty(s)) return s;
            }
            catch (Exception) { }
            return pi.prefab.PrefabName ?? "?";
        }

        private static DynamicPrefabDecorator Decorator()
        {
            GameManager gm = GameManager.Instance;
            return gm != null ? gm.GetDynamicPrefabDecorator() : null;
        }

        public static bool FootprintContains(PrefabInstance pi, Vector3i p)
        {
            Vector3i a = pi.boundingBoxPosition, s = pi.boundingBoxSize;
            return p.x >= a.x && p.x < a.x + s.x && p.z >= a.z && p.z < a.z + s.z;
        }

        /// <summary>The real POI whose footprint holds the position (its min corner is the key used everywhere), or null.</summary>
        public static PrefabInstance PoiAt(Vector3i c)
        {
            DynamicPrefabDecorator dec = Decorator();
            if (dec == null) return null;
            dec.GetPrefabsAtXZ(c.x, c.x, c.z, c.z, prefabScratch);
            PrefabInstance hit = null;
            for (int i = 0; i < prefabScratch.Count; i++)
            {
                if (IsRealPoi(prefabScratch[i]) && FootprintContains(prefabScratch[i], c)) { hit = prefabScratch[i]; break; }
            }
            prefabScratch.Clear();
            return hit;
        }

        /// <summary>The POI whose footprint min corner is exactly this key, or null.</summary>
        public static PrefabInstance PoiByKey(Vector3i key)
        {
            PrefabInstance pi = PoiAt(key);
            return (pi != null && pi.boundingBoxPosition == key) ? pi : null;
        }

        /// <summary>
        /// The nearest real POI within maxDist of a point, preferring one that is not yet cleared and skipping
        /// any that touches the home zone (the player must not be sent to clear their own base).
        /// </summary>
        public static PrefabInstance NearestRealPoi(Vector3 from, int maxDist, World world)
        {
            DynamicPrefabDecorator dec = Decorator();
            if (dec == null) return null;
            int fx = (int)from.x, fz = (int)from.z;
            var list = new List<PrefabInstance>();
            dec.GetPrefabsAtXZ(fx - maxDist, fx + maxDist, fz - maxDist, fz + maxDist, list);
            PrefabInstance bestUncleared = null, bestAny = null;
            float dUn = float.MaxValue, dAny = float.MaxValue;
            for (int i = 0; i < list.Count; i++)
            {
                PrefabInstance pi = list[i];
                if (!IsRealPoi(pi)) continue;
                Vector3i a = pi.boundingBoxPosition, s = pi.boundingBoxSize;
                if (ScZone.HasHome && TouchesHome(a, s)) continue;
                if (ScOutposts.IsOutpostPoiKey(a)) continue;      // our own outposts are not favour targets
                if (ScEnclaves.IsEnclavePoiKey(a)) continue;      // nor an enclave's building (its rooms are kept clear)
                if (ScEncRequests.IsTarget(a)) continue;          // E5: nor where an enclave's request sends the player
                float cx = a.x + s.x * 0.5f, cz = a.z + s.z * 0.5f;
                float d = Mathf.Sqrt((cx - from.x) * (cx - from.x) + (cz - from.z) * (cz - from.z));
                if (d > maxDist) continue;
                if (d < dAny) { dAny = d; bestAny = pi; }
                string why;
                if (world != null && !IsCleared(world, pi, out why) && d < dUn) { dUn = d; bestUncleared = pi; }
            }
            return bestUncleared ?? bestAny;
        }

        public static bool OverlapsHome(PrefabInstance pi) { return ScZone.HasHome && TouchesHome(pi.boundingBoxPosition, pi.boundingBoxSize); }

        private static bool TouchesHome(Vector3i a, Vector3i s)
        {
            int xMin, xMax, zMin, zMax;
            ScZone.Bounds(out xMin, out xMax, out zMin, out zMax);
            return a.x <= xMax && a.x + s.x - 1 >= xMin && a.z <= zMax && a.z + s.z - 1 >= zMin;
        }

        public static Bounds FootprintBounds(PrefabInstance pi)
        {
            Vector3i p = pi.boundingBoxPosition, s = pi.boundingBoxSize;
            return new Bounds(new Vector3(p.x + s.x * 0.5f, 128f, p.z + s.z * 0.5f), new Vector3(s.x, 256f, s.z));
        }

        /// <summary>
        /// Vanilla's own test for a clear quest on this POI - SleeperEventData.SetupData + Update with the re-arm
        /// suppressed, as Patches_Quest does for SC's quests: every generated, non-quest-exclude sleeper volume of the
        /// prefab reads wasCleared. A favour quest on such a POI moves on the frame after it starts. False when none
        /// is generated (vanilla's quest would never finish there). Looser than IsCleared: no respawn timer, no enemy
        /// scan, ungenerated volumes ignored.
        /// </summary>
        public static bool QuestWouldClear(World w, PrefabInstance pi)
        {
            if (w == null || pi == null || pi.prefab == null || pi.prefab.SleeperVolumeList == null) return false;
            var pl = pi.prefab.SleeperVolumeList.List;
            if (pl == null) return false;
            int found = 0;
            for (int i = 0; i < pl.Count; i++)
            {
                if (pl[i] == null) continue;
                Vector3i a = pi.boundingBoxPosition + pl[i].startPos;
                int idx = w.FindSleeperVolume(a, a + pl[i].size);
                if (idx == -1) continue;
                SleeperVolume v = w.GetSleeperVolume(idx);
                if (v == null || v.isQuestExclude) continue;
                found++;
                if (!v.wasCleared) return false;
            }
            return found > 0;
        }

        /// <summary>
        /// True when the POI holds no remaining enemies; otherwise 'why' says what is left. Zombies and animals that wandered
        /// in after the clear don't count with PoiClearIgnoresWanderers (round fx1).
        /// </summary>
        public static bool IsCleared(World w, PrefabInstance pi, out string why)
        {
            return IsCleared(w, pi, out why, true);
        }

        /// <summary>
        /// hostileScan false = the persisted sleeper volumes only (readable while the POI is unloaded): used by the
        /// outposts' rollover, where a zombie that wandered in must not make a held POI "contested".
        /// </summary>
        public static bool IsCleared(World w, PrefabInstance pi, out string why, bool hostileScan)
        {
            return IsCleared(w, pi, out why, hostileScan, false);
        }

        /// <summary>
        /// heldOutpost (round oc1, the review's F1): a trigger room (one only its door or switch wakes) that never woke is
        /// not counted. A held outpost's rooms were all cleared once; vanilla never wakes an empty trigger room as the player
        /// walks in, and its trigger fires only once, so an older overrun that re-armed one left the outpost contested for
        /// good. To establish or for a favor such a room still counts ("behind a door or switch").
        /// </summary>
        public static bool IsCleared(World w, PrefabInstance pi, out string why, bool hostileScan, bool heldOutpost)
        {
            why = null;
            if (pi == null || pi.prefab == null) { why = "no prefab"; return false; }
            if (w == null) { why = "no world"; return false; }

            int defined = 0;
            var pl = (pi.prefab.SleeperVolumeList != null) ? pi.prefab.SleeperVolumeList.List : null;
            if (pl != null) for (int i = 0; i < pl.Count; i++) if (pl[i] != null && !pl[i].isQuestExclude) defined++;

            int volumes = 0, uncleared = 0, unentered = 0, behindTrigger = 0, alive = 0, due = 0;
            ulong now = w.worldTime;
            List<SleeperVolume> vols = pi.sleeperVolumes;
            if (vols != null)
            {
                for (int i = 0; i < vols.Count; i++)
                {
                    SleeperVolume v = vols[i];
                    if (v == null) continue;
                    bool questExcluded = v.isQuestExclude;
                    if (!questExcluded) volumes++;
                    if (v.respawnMap != null && v.respawnMap.Count > 0) { alive += v.respawnMap.Count; continue; }
                    if (questExcluded) continue;
                    // round oc1 (2026-11-19): a volume not cleared with no sleepers on record was never triggered - the player
                    // has not walked into that part of the building yet (vanilla spawns a volume's sleepers only then; an
                    // outpost's overrun or neglect re-arms them with DespawnAndReset). isSpawned is not saved, respawnMap is.
                    if (!v.wasCleared)
                    {
                        if (v.isSpawned) uncleared++;
                        else if (ScSleeperCap.IsTriggerRoom(v)) { if (!heldOutpost) behindTrigger++; }
                        else unentered++;
                        continue;
                    }
                    if (now >= v.respawnTime && GameUtils.CheckForAnyPlayerHome(w, v.BoxMin, v.BoxMax) == GameUtils.EPlayerHomeType.None) due++;
                }
            }
            int ungenerated = defined > volumes ? defined - volumes : 0;

            int hostile = 0;
            if (hostileScan)
            {
                scratch.Clear();
                // every living thing in the footprint, counting the enemies: zombies and animals (EntityEnemy) and SC's
                // raiders, which since M10 are EntityAliveSDXV4 (an EntityNPC, not an EntityEnemy) and would otherwise
                // never keep a POI "contested". Round fx1 (2026-11-19, the user's report: zombies walking into a cleared POI
                // kept it from becoming an outpost): with PoiClearIgnoresWanderers a zombie or animal counts only while it is
                // a sleeper (one the sleeper volumes no longer list, e.g. after a vanilla reset); raiders and hostile
                // survivors always count (the user's call: people holding the building are a real contest).
                bool wanderersCount = !ScConfig.PoiClearIgnoresWanderers;
                w.GetEntitiesInBounds(typeof(EntityAlive), FootprintBounds(pi), scratch);
                for (int i = 0; i < scratch.Count; i++)
                {
                    EntityAlive a = scratch[i] as EntityAlive;
                    if (a == null || a.IsDead()) continue;
                    if (ScXnpc.IsRaider(a) || ScTrust.IsHostileToAny(a)) hostile++;
                    else if (a is EntityEnemy && (wanderersCount || a.IsSleeper)) hostile++;
                }
                scratch.Clear();
            }

            if (alive == 0 && uncleared == 0 && unentered == 0 && behindTrigger == 0 && due == 0 && ungenerated == 0 && hostile == 0) return true;
            var sb = new StringBuilder();
            Part(sb, ungenerated > 0, ungenerated + " of " + defined + " sleeper areas not explored yet");
            Part(sb, hostile > 0, hostile + (hostile == 1 ? " enemy inside" : " enemies inside"));
            Part(sb, alive > 0, alive + (alive == 1 ? " sleeper alive" : " sleepers alive"));
            Part(sb, uncleared > 0, uncleared + " of " + volumes + " sleeper areas not cleared");
            Part(sb, unentered > 0, unentered + " of " + volumes + " sleeper areas not entered yet - walk through every room");
            Part(sb, behindTrigger > 0, behindTrigger + " sleeper area(s) behind a door or switch not opened yet");
            Part(sb, due > 0, due + " sleeper area(s) due to respawn - revisit");
            why = sb.ToString();
            return false;
        }

        /// <summary>
        /// Round oc1: what the sleeper volumes still hold, short, for a narrow label ("1 room not entered, 2 sleepers alive";
        /// "" when nothing). The same reading as IsCleared's volume part (heldOutpost: an unwoken trigger room left out).
        /// </summary>
        public static string RemainingShort(PrefabInstance pi, bool heldOutpost)
        {
            if (pi == null || pi.sleeperVolumes == null) return "";
            int alive = 0, unentered = 0, behind = 0, waking = 0;
            foreach (SleeperVolume v in pi.sleeperVolumes)
            {
                if (v == null) continue;
                if (v.respawnMap != null && v.respawnMap.Count > 0) { alive += v.respawnMap.Count; continue; }
                if (v.isQuestExclude || v.wasCleared) continue;
                if (v.isSpawned) waking++;
                else if (ScSleeperCap.IsTriggerRoom(v)) { if (!heldOutpost) behind++; }
                else unentered++;
            }
            var sb = new StringBuilder();
            Part(sb, unentered > 0, unentered + (unentered == 1 ? " room not entered" : " rooms not entered"));
            Part(sb, behind > 0, behind + (behind == 1 ? " room behind a door" : " rooms behind a door"));
            Part(sb, waking > 0, waking + (waking == 1 ? " room waking" : " rooms waking"));
            Part(sb, alive > 0, alive + (alive == 1 ? " sleeper alive" : " sleepers alive"));
            return sb.ToString();
        }

        private static void Part(StringBuilder sb, bool when, string text)
        {
            if (!when) return;
            if (sb.Length > 0) sb.Append(", ");
            sb.Append(text);
        }
    }
}
