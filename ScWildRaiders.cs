using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// W2 (round 23, 3.4 build only): SC's wild raiders spawn again when another mod switches off the game's enemy biome
    /// spawns. WalkerSim2 (its BiomeSpawnerHook prefix: <c>if (!_isSpawnEnemy) return; _isSpawnEnemy = false;</c>) does that
    /// to place its own zombies, and SpawnManagerBiomes.SpawnUpdate then skips every enemy group - SC's raider entries in
    /// spawning.xml (scRaiderWild/Town/City Day/Night, the scRaiderGroupGS01/50/100 groups, enemy groups because their
    /// entry 0 is a raider) with the zombies.
    ///
    /// How: the update itself is left exactly as the other mod leaves it (animals and SC's wild survivors spawn as before,
    /// with the animal distance and cap). When it ARRIVED with enemies on (Patch_Arrive, Priority.First, before any other
    /// prefix) and is HANDED to the game with them off (Patch_Pass's prefix, Priority.Last, after the others), Patch_Pass's
    /// postfix runs vanilla's own SpawnUpdate a second time for SC's raider entries alone, with enemies on: PassCopy, a
    /// Harmony reverse patch (an unpatched copy of vanilla's IL, built once by Build) whose group loop sees only those
    /// entries. So the raiders get vanilla's enemy rules (AIDirector.CanSpawn's enemy cap, no spawn in a blood moon, the
    /// 28 m enemy distance, POI tags, day/night, maxcount scaled by the enemy-spawn setting, respawn delays, the player's
    /// safe zone, despawn counting), and every other enemy group stays off: the copy's loop treats every entry that is not
    /// an SC raider entry as "not this time of day" (PassDaytime), which is tested before the loop reads or writes that
    /// group's per-area spawn data (ChunkAreaBiomeSpawnData counts and delays), so the zombies' entries are never touched
    /// and the other mod keeps owning the zombies. Nothing names WalkerSim: the test is "arrived on, handed off".
    ///
    /// Not done: when the update arrives with enemies off (the game's enemy spawning off), nothing runs; when another mod
    /// skips the whole update (a prefix returning false) or turns enemies off later than SC's Priority.Last prefix (or in
    /// a transpiler), nothing runs either - the raiders then stay off, as before W2.
    ///
    /// Odds: each pick is one roll of the entry's group by vanilla's GetRandomFromGroup - 4% a raider - with a fresh re-roll
    /// memory. Vanilla re-rolls (up to 3 times) a roll equal to the previous biome roll anywhere near a player
    /// (SpawnManagerBiomes.lastClassId): after a "none" (SC's survivor groups are 94% none) a raider roll comes to about
    /// 11.5%, after a zombie or an animal 4%. With the other mod's zombies gone from that sequence a "none" would come
    /// first far more often than in the game the odds were set for; the pass rolls at the group's own odds instead and
    /// neither reads nor writes the manager's memory, so SC's wild survivors roll exactly as before. SC's enclave-grounds
    /// rule (ScEnclavePeople.FilterWild) and the class-by-gamestage swap apply to the pass's roll as to any biome roll
    /// (GetRandomFromGroupList's postfix; the pass raises the biome-spawn flag itself).
    ///
    /// Counters: sc raiders wild. Config: WildRaidersDespiteMods (Config/blocks.xml).
    /// </summary>
    public static class ScWildRaiders
    {
        /// <summary>SC's wild raider groups (Tools/gen_arsenal.py: scRaiderGroupGS01, GS50, GS100).</summary>
        public const string GroupPrefix = "scRaiderGroupGS";

        /// <summary>The ids of SC's raider entries in spawning.xml, for the counters' names (the game keys an entry by the id's hash).</summary>
        private static readonly string[] KnownIds =
        {
            "scRaiderWildDay", "scRaiderWildNight", "scRaiderTownDay", "scRaiderTownNight", "scRaiderCityDay", "scRaiderCityNight"
        };

        /// <summary>What PassDaytime answers for an entry the pass must skip: neither Any nor Day nor Night.</summary>
        private const EDaytime NotThisPass = (EDaytime)(-1);

        // ---- the per-update state (SpawnUpdate runs on the main thread, in the world's tick)
        [ThreadStatic] private static bool arrived;
        [ThreadStatic] private static bool handed;

        // ---- the pass in progress
        private static bool inPass;
        private static BiomeSpawnEntityGroupData lastEntry;
        private static string passBiome;

        // ---- the copy
        private static bool ready;
        private static string buildNote = "not built";
        private static FieldInfo daytimeField;
        private static MethodInfo isEnemyMethod, rollMethod, spawnMethod;
        private static int anchorsDaytime, anchorsEnemy, anchorsRoll, anchorsSpawn;

        // ---- SC's raider entries per biome list (index lists, and the entries themselves for PassDaytime)
        private static readonly Dictionary<BiomeSpawnEntityGroupList, int[]> entriesByList = new Dictionary<BiomeSpawnEntityGroupList, int[]>();
        private static readonly HashSet<BiomeSpawnEntityGroupData> raiderEntries = new HashSet<BiomeSpawnEntityGroupData>();

        // ---- counters (this world)
        private static int calls, arrivedOn, offByMod, updateSkipped, keyOff, notBuilt, notDue, enemyGate, passes, passErrors;
        private static int considered, rolled, rolledNone, refusedEnclave, queued, spawned;
        private static ulong firstOffTime;

        private sealed class EntryStats
        {
            public string Name;
            public int Considered, Rolled, Spawned;
        }

        private static readonly Dictionary<int, EntryStats> perEntry = new Dictionary<int, EntryStats>();
        private static Dictionary<int, string> knownNames;

        /// <summary>Per world (ModEvents.GameStarting): the counters and the entry cache (the biome lists are loaded again).</summary>
        public static void Reset()
        {
            arrived = handed = false;
            inPass = false;
            lastEntry = null;
            passBiome = null;
            entriesByList.Clear();
            raiderEntries.Clear();
            perEntry.Clear();
            calls = arrivedOn = offByMod = updateSkipped = keyOff = notBuilt = notDue = enemyGate = passes = passErrors = 0;
            considered = rolled = rolledNone = refusedEnclave = queued = spawned = 0;
            firstOffTime = 0;
        }

        // ------------------------------------------------------------------ the copy ----

        /// <summary>
        /// Once, at InitMod (after the patch classes): the copy of vanilla's SpawnUpdate the pass runs, with the four hooks in
        /// its IL (PassTranspiler). Any surprise in vanilla's IL leaves it unbuilt - the pass then never runs (no raiders
        /// while another mod has the enemy spawns off, as before W2), with one error line.
        /// </summary>
        public static void Build()
        {
            ready = false;
            try
            {
                MethodBase original = Patches_Enclaves.Patch_BiomeSpawn.Target();
                daytimeField = AccessTools.Field(typeof(BiomeSpawnEntityGroupData), "daytime");
                isEnemyMethod = AccessTools.Method(typeof(EntityGroups), "IsEnemyGroup", new[] { typeof(string) });
                rollMethod = AccessTools.Method(typeof(EntityGroups), "GetRandomFromGroup", new[] { typeof(string), typeof(int).MakeByRefType(), typeof(GameRandom) });
                spawnMethod = AccessTools.Method(typeof(Chunk), "SpawnEntityAsync", new[] { typeof(World), typeof(EntityCreationData), typeof(Action<Entity>) });
                if (original == null || daytimeField == null || isEnemyMethod == null || rollMethod == null || spawnMethod == null)
                {
                    buildNote = "NOT BUILT (a game member is gone: SpawnUpdate " + (original != null) + ", daytime " + (daytimeField != null)
                                + ", IsEnemyGroup " + (isEnemyMethod != null) + ", GetRandomFromGroup " + (rollMethod != null)
                                + ", SpawnEntityAsync " + (spawnMethod != null) + ")";
                    ScLog.Err("wild raiders (W2): " + buildNote + " - SC's wild raiders stay off while another mod has the game's enemy biome spawns off");
                    return;
                }
                anchorsDaytime = anchorsEnemy = anchorsRoll = anchorsSpawn = 0;
                // fully qualified: SCore declares a namespace called Harmony (ModApi.cs)
                HarmonyLib.Harmony.ReversePatch(original, new HarmonyMethod(AccessTools.Method(typeof(ScWildRaiders), nameof(PassCopy))),
                                     AccessTools.Method(typeof(ScWildRaiders), nameof(PassTranspiler)), null);
                ready = anchorsDaytime == 2 && anchorsEnemy == 1 && anchorsRoll == 1 && anchorsSpawn == 1;
                buildNote = (ready ? "built" : "NOT USED") + " (time-of-day gates " + anchorsDaytime + "/2, enemy check " + anchorsEnemy
                            + "/1, roll " + anchorsRoll + "/1, spawn " + anchorsSpawn + "/1)";
                if (ready) ScLog.Out("wild raiders (W2): vanilla's SpawnUpdate copied for SC's raider entries - " + buildNote);
                else ScLog.Err("wild raiders (W2): the copy of SpawnUpdate is " + buildNote + " - SC's wild raiders stay off while another mod has the game's enemy biome spawns off");
            }
            catch (Exception e)
            {
                ready = false;
                buildNote = "NOT BUILT (" + e.GetType().Name + ": " + e.Message + ")";
                ScLog.Err("wild raiders (W2): copying SpawnManagerBiomes.SpawnUpdate failed - SC's wild raiders stay off while another mod has the game's enemy biome spawns off: " + e.Message);
                Log.Exception(e);
            }
        }

        /// <summary>
        /// The stand-in Harmony's reverse patch turns into the copy of vanilla's SpawnUpdate (this method's body is replaced;
        /// the instance comes first). Called only once Build succeeded.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void PassCopy(SpawnManagerBiomes instance, string _spawnerName, bool _isSpawnEnemy, ChunkAreaBiomeSpawnData _spawnData)
        {
            throw new NotImplementedException("SurvivorCommunity W2: the copy of SpawnManagerBiomes.SpawnUpdate was not built");
        }

        /// <summary>
        /// The copy's four hooks, each a same-stack swap of one instruction (3.4's IL, checked by Cecil: two loads of
        /// BiomeSpawnEntityGroupData.daytime in the group loop, one IsEnemyGroup, one GetRandomFromGroup, one
        /// Chunk.SpawnEntityAsync). Any other count throws, so Build leaves the copy unbuilt rather than run a loop that
        /// could pick a zombie group.
        /// </summary>
        public static IEnumerable<CodeInstruction> PassTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo mDaytime = AccessTools.Method(typeof(ScWildRaiders), nameof(PassDaytime));
            MethodInfo mIsEnemy = AccessTools.Method(typeof(ScWildRaiders), nameof(PassIsEnemy));
            MethodInfo mRoll = AccessTools.Method(typeof(ScWildRaiders), nameof(PassRoll));
            MethodInfo mSpawn = AccessTools.Method(typeof(ScWildRaiders), nameof(PassSpawn));
            var list = new List<CodeInstruction>(instructions);
            int d = 0, e = 0, r = 0, s = 0;
            foreach (CodeInstruction ci in list)
            {
                if (ci.opcode == OpCodes.Ldfld && Equals(ci.operand, daytimeField)) { ci.opcode = OpCodes.Call; ci.operand = mDaytime; d++; }
                else if (ci.opcode == OpCodes.Call && Equals(ci.operand, isEnemyMethod)) { ci.operand = mIsEnemy; e++; }
                else if (ci.opcode == OpCodes.Call && Equals(ci.operand, rollMethod)) { ci.operand = mRoll; r++; }
                else if ((ci.opcode == OpCodes.Callvirt || ci.opcode == OpCodes.Call) && Equals(ci.operand, spawnMethod)) { ci.opcode = OpCodes.Call; ci.operand = mSpawn; s++; }
            }
            anchorsDaytime = d; anchorsEnemy = e; anchorsRoll = r; anchorsSpawn = s;
            if (d != 2 || e != 1 || r != 1 || s != 1)
                throw new InvalidOperationException("SpawnUpdate's IL is not the one W2 was written for: time-of-day loads " + d + " (2), IsEnemyGroup " + e
                                                    + " (1), GetRandomFromGroup " + r + " (1), SpawnEntityAsync " + s + " (1)");
            return list;
        }

        // ------------------------------------------------------------------ the hooks inside the copy ----

        /// <summary>
        /// The copy's group loop reads an entry's time of day here first (after its POI-tag bit), before anything touches the
        /// entry's spawn data: SC's raider entries answer their own, every other entry an impossible one, so the loop moves
        /// on to the next entry exactly as vanilla does for an entry of the other time of day.
        /// </summary>
        public static EDaytime PassDaytime(BiomeSpawnEntityGroupData g)
        {
            if (g != null && inPass && raiderEntries.Contains(g))
            {
                lastEntry = g;
                return g.daytime;
            }
            return NotThisPass;
        }

        /// <summary>The loop's enemy check - reached only by an SC raider entry of this time of day: counted, answered as vanilla.</summary>
        public static bool PassIsEnemy(string group)
        {
            bool enemy = EntityGroups.IsEnemyGroup(group);
            if (lastEntry != null)
            {
                considered++;
                Stats(lastEntry).Considered++;
            }
            return enemy;
        }

        /// <summary>
        /// The pick's group roll (vanilla's GetRandomFromGroup, so SC's GetRandomFromGroupList postfix still applies: the
        /// enclave-grounds rule and the class-by-gamestage swap), with a fresh re-roll memory: -1 is no roll's answer (a
        /// class, 0 for "none"; -1 only when no entry matched), so the first roll stands - the group's own odds. The
        /// manager's own memory (<paramref name="last"/>, lastClassId) is neither read nor written.
        /// </summary>
        public static int PassRoll(string group, ref int last, GameRandom random)
        {
            int memory = -1;
            int refusedBefore = ScEnclavePeople.WildRefused;
            int cls = EntityGroups.GetRandomFromGroup(group, ref memory, random);
            rolled++;
            if (lastEntry != null) Stats(lastEntry).Rolled++;
            if (cls == 0)
            {
                if (ScEnclavePeople.WildRefused != refusedBefore) refusedEnclave++;
                else rolledNone++;
            }
            return cls;
        }

        /// <summary>The spawn itself, as vanilla's (the same chunk, creation data and callback), with one log line once the body exists.</summary>
        public static void PassSpawn(Chunk chunk, World world, EntityCreationData ecd, Action<Entity> onCreated)
        {
            queued++;
            BiomeSpawnEntityGroupData entry = lastEntry;
            string biome = passBiome;
            chunk.SpawnEntityAsync(world, ecd, e =>
            {
                onCreated?.Invoke(e);
                try { OnSpawned(e, entry, biome); }
                catch (Exception ex) { ScLog.Once("w2-spawned", ex); }
            });
        }

        private static void OnSpawned(Entity e, BiomeSpawnEntityGroupData entry, string biome)
        {
            spawned++;
            if (entry != null) Stats(entry).Spawned++;
            if (e == null) return;
            EntityClass ec;
            string cls = EntityClass.list.TryGetValue(e.entityClass, out ec) && ec != null ? ec.entityClassName : e.entityClass.ToString();
            World w = GameManager.Instance != null ? GameManager.Instance.World : null;
            ScLog.Verbose("wild raider (W2): " + cls + " #" + e.entityId + " at " + World.worldToBlockPos(e.position) + " from "
                          + (entry != null ? NameOf(entry) : "?") + " in " + (biome ?? "?") + (w != null ? (w.IsDaytime() ? " by day" : " at night") : "")
                          + " - another mod has the game's enemy biome spawns off");
        }

        // ------------------------------------------------------------------ the patches ----

        /// <summary>Before every other prefix: what the update arrived with.</summary>
        [HarmonyPatch]
        public static class Patch_Arrive
        {
            [HarmonyPrepare]
            public static bool Prepare() { return Patches_Enclaves.Patch_BiomeSpawn.Target() != null; }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return Patches_Enclaves.Patch_BiomeSpawn.Target(); }

            [HarmonyPrefix]
            [HarmonyPriority(Priority.First)]
            public static void Prefix(bool _isSpawnEnemy)
            {
                calls++;
                arrived = _isSpawnEnemy;
                handed = _isSpawnEnemy;
                if (_isSpawnEnemy) arrivedOn++;
            }
        }

        /// <summary>After every other prefix: what the game is handed (the prefix); then the raider pass (the postfix).</summary>
        [HarmonyPatch]
        public static class Patch_Pass
        {
            [HarmonyPrepare]
            public static bool Prepare() { return Patches_Enclaves.Patch_BiomeSpawn.Target() != null; }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return Patches_Enclaves.Patch_BiomeSpawn.Target(); }

            [HarmonyPrefix]
            [HarmonyPriority(Priority.Last)]
            public static void Prefix(bool _isSpawnEnemy)
            {
                handed = _isSpawnEnemy;
            }

            [HarmonyPostfix]
            public static void Postfix(SpawnManagerBiomes __instance, string _spawnerName, ChunkAreaBiomeSpawnData _spawnData, bool __runOriginal)
            {
                bool off = arrived && !handed;
                arrived = handed = false;
                if (!off) return;
                try { AfterUpdate(__instance, _spawnerName, _spawnData, __runOriginal); }
                catch (Exception e)
                {
                    passErrors++;
                    ScLog.Once("w2-pass", e);
                }
            }
        }

        /// <summary>The update arrived with enemies on and was handed them off: SC's raider entries get their own pass.</summary>
        private static void AfterUpdate(SpawnManagerBiomes mgr, string spawnerName, ChunkAreaBiomeSpawnData data, bool ranOriginal)
        {
            offByMod++;
            ScConfig.EnsureParsed();
            World w = mgr != null ? mgr.world : null;
            if (firstOffTime == 0 && w != null)
            {
                firstOffTime = w.worldTime == 0 ? 1 : w.worldTime;
                ScLog.Out("wild raiders (W2): another mod turned the game's enemy biome spawns off (SpawnManagerBiomes.SpawnUpdate arrived with them on and was handed them off) - "
                          + (!ScConfig.WildRaidersDespiteMods ? "WildRaidersDespiteMods is false: SC's wild raiders stay off with them"
                             : ready ? "SC's wild raider entries spawn on their own, on the game's rules for enemies (sc raiders wild)"
                             : "but the copy of SpawnUpdate is " + buildNote + ": SC's wild raiders stay off"));
            }
            if (!ranOriginal) { updateSkipped++; return; }         // a mod skipped the whole update: no biome spawn at all
            if (!ScConfig.WildRaidersDespiteMods) { keyOff++; return; }
            if (!ready) { notBuilt++; return; }
            if (w == null || data == null) return;
            if (!MayPick(w, data)) { notDue++; return; }
            // vanilla's own enemy gates (the copy would turn enemies off for them too): the enemy cap, a blood moon
            if (!AIDirector.CanSpawn() || (w.aiDirector != null && w.aiDirector.BloodMoonComponent != null && w.aiDirector.BloodMoonComponent.BloodMoonActive))
            {
                enemyGate++;
                return;
            }
            passes++;
            inPass = true;
            lastEntry = null;
            ScEnclavePeople.BeginBiomeSpawn();   // the enclave-grounds rule (Patch_BiomeSpawn's flag): this pass is a biome spawn
            try { PassCopy(mgr, spawnerName, true, data); }
            finally
            {
                inPass = false;
                lastEntry = null;
                passBiome = null;
                ScEnclavePeople.EndBiomeSpawn();
            }
        }

        /// <summary>
        /// Could the copy's loop pick one of SC's raider entries in this area now? (Conservative: true whenever vanilla's loop
        /// could - the time of day, the entry's POI-tag bit once the area's tags are known, and a free slot or a respawn
        /// delay run out.) Read only. When false the pass would only draw random numbers, so it is not run.
        /// </summary>
        private static bool MayPick(World w, ChunkAreaBiomeSpawnData data)
        {
            BiomeDefinition biome = w.Biomes != null ? w.Biomes.GetBiome(data.biomeId) : null;
            if (biome == null) return false;
            BiomeSpawnEntityGroupList list = BiomeSpawningClass.list[biome.m_sBiomeName];
            if (list == null || list.list == null) return false;
            int[] idx = EntriesOf(list);
            if (idx.Length == 0) return false;
            passBiome = biome.m_sBiomeName;
            EDaytime now = w.IsDaytime() ? EDaytime.Day : EDaytime.Night;
            for (int i = 0; i < idx.Length; i++)
            {
                int k = idx[i];
                BiomeSpawnEntityGroupData g = list.list[k];
                if (g.daytime != EDaytime.Any && g.daytime != now) continue;
                if (data.checkedPOITags && (data.groupsEnabledFlags & (1 << k)) == 0) continue;   // vanilla's bit (k & 31)
                if (w.worldTime > data.GetDelayWorldTime(g.idHash) || data.CanSpawn(g.idHash)) return true;
            }
            return false;
        }

        /// <summary>SC's raider entries in a biome's list (by group name, and only while the group is an enemy group: a
        /// non-enemy one is spawned by the game's own update whatever another mod does).</summary>
        private static int[] EntriesOf(BiomeSpawnEntityGroupList list)
        {
            int[] idx;
            if (entriesByList.TryGetValue(list, out idx)) return idx;
            var found = new List<int>();
            for (int i = 0; i < list.list.Count; i++)
            {
                BiomeSpawnEntityGroupData g = list.list[i];
                if (g == null || g.entityGroupName == null || !g.entityGroupName.StartsWith(GroupPrefix, StringComparison.Ordinal)) continue;
                bool enemy;
                try { enemy = EntityGroups.IsEnemyGroup(g.entityGroupName); }
                catch (Exception) { enemy = false; }
                if (!enemy)
                {
                    ScLog.Once("w2-not-enemy-" + g.entityGroupName, "wild raiders (W2): " + g.entityGroupName + " is not an enemy group - the game's own update spawns it, W2 leaves it alone");
                    continue;
                }
                found.Add(i);
                raiderEntries.Add(g);
            }
            idx = found.ToArray();
            entriesByList[list] = idx;
            return idx;
        }

        private static EntryStats Stats(BiomeSpawnEntityGroupData g)
        {
            EntryStats s;
            if (!perEntry.TryGetValue(g.idHash, out s))
            {
                s = new EntryStats { Name = NameOf(g) };
                perEntry[g.idHash] = s;
            }
            return s;
        }

        /// <summary>The entry's spawning.xml id when it is one of SC's six (the game keeps only the id's hash), else its group.</summary>
        private static string NameOf(BiomeSpawnEntityGroupData g)
        {
            if (knownNames == null)
            {
                var m = new Dictionary<int, string>();
                foreach (string id in KnownIds) m[id.GetHashCode()] = id;
                knownNames = m;
            }
            string n;
            return knownNames.TryGetValue(g.idHash, out n) ? n : g.entityGroupName + " " + g.daytime;
        }

        // ------------------------------------------------------------------ console ----

        /// <summary>sc raiders: one line.</summary>
        public static string Summary()
        {
            return "wild raiders (W2, sc raiders wild): " + (offByMod == 0 ? "no mod turned the enemy biome spawns off in " + calls + " update(s)"
                       : "the enemy biome spawns were off by another mod in " + offByMod + " of " + calls + " update(s); SC's raider entries considered "
                         + considered + ", rolled " + rolled + ", spawned " + spawned);
        }

        /// <summary>sc raiders wild: every counter.</summary>
        public static void Lines(List<string> into)
        {
            ScConfig.EnsureParsed();
            World w = GameManager.Instance != null ? GameManager.Instance.World : null;
            into.Add("wild raiders when another mod turns the game's enemy biome spawns off (W2): WildRaidersDespiteMods=" + ScConfig.WildRaidersDespiteMods
                     + ", the copy of SpawnUpdate " + buildNote + ", the game's enemy spawning " + (GameStats.GetBool(EnumGameStats.IsSpawnEnemies) ? "on" : "OFF"));
            into.Add("  biome updates " + calls + ": arrived with enemies on " + arrivedOn + ", of those handed to the game with them off (another mod) " + offByMod
                     + (firstOffTime > 0 ? " (first on day " + GameUtils.WorldTimeToDays(firstOffTime) + " " + GameUtils.WorldTimeToHours(firstOffTime) + "h)" : ""));
            into.Add("  raider passes run " + passes + "; not run: " + notDue + " no raider entry due in that area, " + enemyGate + " enemy cap or blood moon, "
                     + keyOff + " key off, " + updateSkipped + " update skipped by a mod, " + notBuilt + " copy not built, " + passErrors + " error(s)");
            into.Add("  SC's raider entries considered " + considered + " (POI tags and time of day matching), rolled " + rolled + " (none " + rolledNone
                     + ", refused near an enclave " + refusedEnclave + "), spawn requested " + queued + ", spawned " + spawned);
            if (perEntry.Count > 0)
            {
                var parts = new List<string>();
                foreach (EntryStats s in perEntry.Values) parts.Add(s.Name + " " + s.Considered + "/" + s.Rolled + "/" + s.Spawned);
                parts.Sort(StringComparer.Ordinal);
                into.Add("  by entry (considered/rolled/spawned): " + string.Join(", ", parts.ToArray()));
            }
            if (w != null)
            {
                int n = 0;
                foreach (var kv in entriesByList) n += kv.Value.Length;
                into.Add("  raider entries known: " + n + " in " + entriesByList.Count + " biome list(s) seen so far (" + GroupPrefix + "*)");
            }
        }
    }
}
