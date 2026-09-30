using System;
using HarmonyLib;

namespace SurvivorCommunity
{
    /// <summary>
    /// Two hooks on the NPC class itself. SetDead: EntityKilled needs a killer and falls / drowning have none.
    /// PostInit: it forces SetSpawnerSource(Biome) on every load (EntityAliveSDX.cs:580), which would make a
    /// stationed member eligible for ambient despawn until the ticker's next relink - re-stamp right there.
    /// </summary>
    public static class Patches_Npc
    {
        [HarmonyPatch(typeof(EntityAliveSDX), nameof(EntityAliveSDX.SetDead), new Type[0])]
        public static class Patch_SetDead
        {
            [HarmonyPostfix]
            public static void Postfix(EntityAliveSDX __instance)
            {
                try
                {
                    if (!ScCommunity.IsHost || !ScRegistry.Loaded) return;
                    if (__instance.Buffs != null && __instance.Buffs.HasCustomVar(ScRadio.StandInCvar)) { ScRadio.OnStandInDied(__instance); return; }
                    ScCommunity.OnMemberDied(__instance);
                }
                catch (Exception e)
                {
                    ScLog.Once("EntityAliveSDX.SetDead", e);
                }
            }
        }

        /// <summary>
        /// Who the player is allowed to hurt. SCore shields every friendly NPC twice over
        /// (EntityTargetingUtilities.CanDamage): one who follows you counts as your ally, so CanDamageAlly
        /// refuses flatly, and one with no leader falls to the faction rule instead, which refuses because you
        /// are Neutral to whiteriver and an EntityNPC target treats Neutral as friendly fire. Both gates are
        /// consulted by CanDamageEntity - what ItemActionAttack.Hit asks before it resolves a hit at all - and
        /// again inside EntityAliveSDX.DamageEntity, so a shot simply vanished. The community wants the
        /// opposite division: anyone who is not one of your people can be killed, and your own people are
        /// protected unless you ask for friendly fire. Traders keep their immunity either way (a pack trader
        /// runs on EntityAliveSDX, so it comes through this same rule rather than vanilla's flat refusal).
        /// Only the player's own damage is affected; NPCs still cannot shoot each other, and zombies were
        /// never shielded to begin with because they hate whiteriver.
        /// </summary>
        [HarmonyPatch(typeof(EntityTargetingUtilities), nameof(EntityTargetingUtilities.CanTakeDamage))]
        public static class Patch_FriendlyFire
        {
            [HarmonyPostfix]
            public static void Postfix(EntityAlive self, Entity damagingEntity, ref bool __result)
            {
                try
                {
                    if (__result) return;                                   // SCore already allows it
                    if (!(damagingEntity is EntityPlayer)) return;          // only the player's own damage
                    if (self == null || self is EntityPlayer) return;
                    // A trader stays a trader. Not by trader id - XNPCCore gives every hireable NPC
                    // trader_id 1, which is why SCore blanks it around its own damage call - but by the
                    // dialog it uses. A vanilla trader never arrives here anyway: EntityTrader refuses all
                    // damage outright and only EntityAliveSDX asks this rule.
                    EntityNPC npc = self as EntityNPC;
                    if (npc != null && npc.NPCInfo != null && !string.IsNullOrEmpty(npc.NPCInfo.DialogID)
                        && npc.NPCInfo.DialogID.StartsWith("trader", StringComparison.OrdinalIgnoreCase)) return;
                    // your own people are protected unless friendly fire is switched on
                    if (!ScConfig.CommunityFriendlyFire && (ScRegistry.GetMember(self.entityId) != null
                        || (self.Buffs != null && self.Buffs.HasCustomVar(ScRadio.StandInCvar)))) return;
                    __result = true;
                }
                catch (Exception e)
                {
                    ScLog.Once("sc-friendly-fire", e);
                }
            }
        }

        /// <summary>
        /// Raiders wear the player body too, but they are EntityNPCBandit (SCore's hostile class), not
        /// EntityAliveSDX, so the survivor PostInit hook never runs for them: without this their hit capsules stay
        /// on the no-collision layer EntityFactory put them on and shots pass straight through.
        /// </summary>
        [HarmonyPatch(typeof(EntityNPCBandit), nameof(EntityNPCBandit.PostInit), new Type[0])]
        public static class Patch_RaiderPostInit
        {
            [HarmonyPostfix]
            public static void Postfix(EntityNPCBandit __instance)
            {
                try
                {
                    if (__instance != null) Patches_Sdcs.FixColliderLayers(__instance);
                }
                catch (Exception e)
                {
                    ScLog.Once("EntityNPCBandit.PostInit", e);
                }
            }
        }

        /// <summary>
        /// A physical runner is never written into a chunk: the registry snapshot is its only persistent copy (the
        /// way vanilla keeps vehicles and drones out of chunk saves). Chunk saves run on the save thread too - this
        /// reads one immutable set and nothing else.
        /// </summary>
        [HarmonyPatch(typeof(EntityAliveSDX), nameof(EntityAliveSDX.IsSavedToFile), new Type[0])]
        public static class Patch_IsSavedToFile
        {
            [HarmonyPostfix]
            public static void Postfix(EntityAliveSDX __instance, ref bool __result)
            {
                if (__result && __instance != null && ScRunners.IsRunnerBody(__instance)) __result = false;
            }
        }

        [HarmonyPatch(typeof(EntityAliveSDX), nameof(EntityAliveSDX.PostInit), new Type[0])]
        public static class Patch_PostInit
        {
            [HarmonyPostfix]
            public static void Postfix(EntityAliveSDX __instance)
            {
                try
                {
                    if (__instance == null) return;
                    // after EntityFactory's collider pass: a player-bodied survivor is otherwise unhittable. Every
                    // player body needs it, not only members and prospects - a stranger found in the wild or spawned
                    // from the console used to keep its hit capsules on the no-collision layer.
                    Patches_Sdcs.FixColliderLayers(__instance);
                    if (!ScRegistry.Loaded) return;
                    if (ScRegistry.Cleanup.Contains(__instance.entityId))
                    {
                        if (ScCommunity.CarriesOurMarks(__instance)) ScCommunity.CleanupStale(__instance);
                        ScRegistry.Cleanup.Remove(__instance.entityId);
                        return;
                    }
                    if (ScRegistry.Doomed.Contains(__instance.entityId)) { __instance.SendOnMission(true); return; }
                    // an outpost stand-in never comes back from a save: a fresh one spawns while the player is there
                    if (__instance.Buffs != null && __instance.Buffs.HasCustomVar(ScGarrison.StandInOutpostCvar)) { ScGarrison.Orphans.Add(__instance.entityId); return; }
                    // an offline physical runner exists only as its snapshot: this is an old chunk copy (hidden now,
                    // removed next tick - never Doomed, the id comes back with the real runner) or a stranger who
                    // inherited the id (untouched: no spawner/Persist stamp for them)
                    Member rm = ScRegistry.GetMember(__instance.entityId);
                    if (rm != null && ScRunners.IsPhysical(rm) && rm.Offline && __instance.entityId != ScRunners.RecreatingId)
                    {
                        if (ScRunners.IsStaleCopy(rm, __instance)) { __instance.SendOnMission(true); ScRunners.StaleIds.Add(__instance.entityId); }
                        return;
                    }
                    if (ScRegistry.GetMember(__instance.entityId) == null && ScRegistry.GetProspect(__instance.entityId) == null) return;
                    __instance.SetSpawnerSource(EnumSpawnerSource.StaticSpawner);
                    // only an ABSENT Persist is raised here: Persist=0 is the signature of a SCore dismiss that
                    // the ticker turns into a Leave, and must survive a save/reload in between
                    if (!__instance.Buffs.HasCustomVar("Persist")) __instance.Buffs.SetCustomVar("Persist", 1f);
                }
                catch (Exception e)
                {
                    ScLog.Once("EntityAliveSDX.PostInit", e);
                }
            }
        }
    }
}
