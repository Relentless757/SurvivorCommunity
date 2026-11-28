// round lk1: new file - falling trees spare friendly survivors (FallingTreesHurtSurvivors)
using System;
using System.Reflection;
using HarmonyLib;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round lk1 (2026-11-27; the user: "a survivor died when a tree landed on them, an option to prevent damage from fallen
    /// trees would be the solution as some may want that to happen for immersion").
    ///
    /// A felled tree falls as an EntityFallingTree, a rigid body. Its Collide (OnCollisionEnter/Stay) returns at once on a
    /// remote copy, so it runs only where the tree entity is local: the server (single player, a listen host, a dedicated
    /// server). There each collision with an entity's collider (an "E_" tag, or an "E_BP_" body part resolved to its root) goes
    /// through collidedWith, which asks treeCanDamageEntity(entity) - false for an entity already hit by this tree
    /// (hitEntities), for every player and for supply crates - and, when true, marks the entity hit and deals
    /// (int)(mass * 0.36) Crushing damage (DamageSource External) 0.05 s later (onEntityDamageLater). A survivor NPC is not a
    /// player, so a falling trunk could kill one.
    ///
    /// With FallingTreesHurtSurvivors false (the default) treeCanDamageEntity also answers false for a friendly survivor
    /// (ProtectedAs): one of SC's members (any community), an outpost or rescue stand-in, a prospect, one of the people of an
    /// enclave that is not hostile (friendly or neutral - round lk1 (review, LK1-n3): with several communities, an enclave
    /// hostile to any one of them is not protected for any), and any 0-XNPCCore survivor (SCore's EntityAliveSDXV4) hired by a
    /// player. Never a player (vanilla spares them already), a zombie, an animal, SC's raiders, 0-XNPCCore's bandits, or a
    /// stranger nobody hired. Round lk1 (review, LK1-m2): a spared survivor goes into the tree's own hitEntities, as a hit one
    /// does, so vanilla's first test refuses it from then on and the postfix decides once per tree and survivor (no damage
    /// follows: only collidedWith deals it, after a true answer). True: vanilla, for immersion. Server only (the postfix asks
    /// IsHost too). One target, listed in ModApi's ExpectedTargets. Both games.
    /// </summary>
    public static class ScFallingTrees
    {
        /// <summary>EntityFallingTree.treeCanDamageEntity(Entity) - vanilla, private, the only test before a falling tree's damage.</summary>
        public static MethodBase Target()
        {
            return AccessTools.DeclaredMethod(typeof(EntityFallingTree), "treeCanDamageEntity", new[] { typeof(Entity) });
        }

        /// <summary>Survivors a falling tree did not hurt this session (sc xnpc counts it too: lk1-tree-spared).</summary>
        public static int Spared;

        /// <summary>
        /// Why a falling tree must not hurt this entity, or null when it may (vanilla's answer stands). Server side: the records are
        /// the host's.
        /// </summary>
        internal static string ProtectedAs(Entity e)
        {
            EntityAlive a = e as EntityAlive;
            if ((object)a == null || a is EntityPlayer || a.IsDead() || a.Buffs == null) return null;
            if (!ScNpc.IsFramework(a)) return null;                                   // zombies, animals, vanilla bandits and traders
            if (ScXnpc.IsRaiderClass(a.entityClass)) return null;                     // SC's raiders (bandit raiders too)
            if (ScEnclavePeople.IsHostileBody(a)) return null;                        // an enclave turned hostile (to any community)
            if (ScRegistry.GetMember(a.entityId) != null || ScXnpc.IsCommunity(a)) return "a member or a stand-in";
            if (ScRegistry.GetProspect(a.entityId) != null) return "a prospect";
            if (ScEnclavePeople.IsEnclaveNpc(a)) return "one of an enclave's people";   // round lk1 (review, LK1-n3): friendly or neutral
            if (a is EntityAliveSDXV4 && HiredByPlayer(a)) return "hired by a player";
            return null;
        }

        /// <summary>
        /// SCore's hire link (the Leader cvar, else Owner) names a player: one in the world, or one of this save's players who is
        /// offline. Never forces a body that is still being built (ScNpc.Loaded).
        /// </summary>
        private static bool HiredByPlayer(EntityAlive a)
        {
            int id = (int)a.Buffs.GetCustomVar("Leader");
            if (id <= 0) id = (int)a.Buffs.GetCustomVar("Owner");
            if (id <= 0) return false;
            World w = ScCommunity.World;
            Entity leader = ScNpc.Loaded(w, id);
            if (leader != null) return leader is EntityPlayer;
            if (w != null && ScNpc.IsPending(w, id)) return false;                    // an NPC leader still being built
            PersistentPlayerList ppl = GameManager.Instance != null ? GameManager.Instance.persistentPlayers : null;
            return ppl != null && ppl.GetPlayerDataFromEntityID(id) != null;
        }

        /// <summary>
        /// The tree's test, FallingTreesHurtSurvivors false: a friendly survivor (ProtectedAs) cannot be hit. Counted (a private
        /// method Mono inlined would never run this; the counter shows it does) and logged with DebugLogging.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_TreeSparesSurvivors
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                if (Target() != null) return true;
                ScLog.Warn("EntityFallingTree.treeCanDamageEntity(Entity) not found (a game update?) - falling trees hurt survivors whatever FallingTreesHurtSurvivors says");
                return false;
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return Target(); }

            /// <summary>__0 the entity the tree touched (vanilla's _entity).</summary>
            [HarmonyPostfix]
            public static void Postfix(EntityFallingTree __instance, Entity __0, ref bool __result)
            {
                try
                {
                    if (!__result || ScConfig.FallingTreesHurtSurvivors || !ScCommunity.IsHost) return;
                    string why = ProtectedAs(__0);
                    if (why == null) return;
                    __result = false;
                    // round lk1 (review, LK1-m2): marked hit, as vanilla marks one it damages - every later collision of this tree with
                    // the survivor is refused by vanilla's own first test, so this runs (and counts, and logs) once per tree and survivor
                    if (__instance != null && __instance.hitEntities != null && !__instance.hitEntities.Contains(__0.entityId)) __instance.hitEntities.Add(__0.entityId);
                    Spared++;
                    Patches_Xnpc.Count("lk1-tree-spared");
                    ScLog.Verbose("falling tree: " + ScCommunity.DisplayName(__0 as EntityAlive) + " (id " + __0.entityId + ", " + why
                                  + ") is not hurt (FallingTreesHurtSurvivors false)");
                }
                catch (Exception ex)
                {
                    ScLog.Once("falling-tree", ex);
                }
            }
        }
    }
}
