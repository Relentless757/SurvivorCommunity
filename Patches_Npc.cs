using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace SurvivorCommunity
{
    /// <summary>
    /// Hooks on the NPC classes themselves. SetDead: EntityKilled needs a killer and falls / drowning have none.
    /// PostInit: up to SCore 3.2.7 it forced SetSpawnerSource(Biome) on every load, which made a stationed member
    /// eligible for ambient despawn until the ticker's next relink. SCore 3.2.36 no longer does
    /// (ApplySpawnerSourceOnPostInit); the re-stamp stays, harmless, for older SCore builds.
    ///
    /// SCore has two friendly classes, EntityAliveSDX and 0-XNPCCore's EntityAliveSDXV4. They are siblings, each
    /// declaring its own SetDead, PostInit and IsSavedToFile (neither calls the other's), so each hook below patches
    /// both declarations (ScNpc.Declared) and runs the same logic on either: everything in it is keyed on registry ids
    /// and the mod's own cvars, which mean the same thing on both. Prepare skips a hook whose targets are all gone
    /// rather than letting the class throw inside PatchAll, which would silently drop every patch class after it.
    /// </summary>
    public static class Patches_Npc
    {
        private static readonly Type[] NoArgs = new Type[0];

        [HarmonyPatch]
        public static class Patch_SetDead
        {
            [HarmonyPrepare]
            public static bool Prepare() { return ScNpc.Declared("SetDead", NoArgs).Count > 0; }

            [HarmonyTargetMethods]
            public static IEnumerable<MethodBase> TargetMethods() { return ScNpc.Declared("SetDead", NoArgs); }

            [HarmonyPostfix]
            public static void Postfix(EntityAlive __instance)
            {
                try
                {
                    if (!ScCommunity.IsHost) return;
                    if (ScXnpc.IsBody(__instance) || (ScNpc.IsV4(__instance) && ScXnpc.IsCommunity(__instance))) DropWindow(__instance);
                    if (!ScRegistry.Loaded) return;
                    if (__instance.Buffs != null && __instance.Buffs.HasCustomVar(ScRadio.StandInCvar)) { ScRadio.OnStandInDied(__instance); return; }
                    ScCommunity.OnMemberDied(__instance);
                }
                catch (Exception e)
                {
                    ScLog.Once("npc.SetDead", e);
                }
            }

            /// <summary>
            /// The inventory window of one of the community's 0-XNPCCore survivors (any of the six classes, wild or not, or
            /// any V4 member or stand-in), dropped as a backpack where they fell - the medical stock and the ammo the
            /// community gave them (ScMedic, ScLoadout, decision D3). SCore's legacy
            /// class drops its window (the HarvestManager container) for anyone and then forgets it; V4's SetDead drops only
            /// its separate lootContainer, and only when the leader is the local player, and never removes the window - so
            /// without this the window's contents were lost with the body and its HarvestManager entry leaked (M1's
            /// SetDead finding). The same code SCore's EntityAliveSDX.SetDead runs.
            /// </summary>
            private static void DropWindow(EntityAlive e)
            {
                try
                {
                    // the window (meds, anything handed over) and, for the community's 0-XNPCCore survivors, the bag (their
                    // ammo, ScAmmo.AddRounds): V4's own dropItemOnDeath drops neither
                    var drop = new List<ItemStack>();
                    bool hadWindow = HarvestManager.Has(e.entityId);
                    if (hadWindow)
                    {
                        SCoreLootContainer window = HarvestManager.GetOrCreate(e.entityId);
                        if (window != null && window.items != null)
                            foreach (ItemStack s in window.items) if (s != null && !s.IsEmpty()) drop.Add(s.Clone());
                    }
                    if (ScXnpc.IsBody(e) && e.bag != null)
                    {
                        foreach (ItemStack s in e.bag.GetSlots()) if (s != null && !s.IsEmpty()) drop.Add(s.Clone());
                        e.bag.Clear();
                    }
                    if (drop.Count > 0)
                    {
                        string className = ScCommunity.ClassExists(EntityClass.FromString("BackpackNPC")) ? "BackpackNPC" : "Backpack";
                        EntityItem pack = EntityFactory.CreateEntity(EntityClass.FromString(className), new Vector3i(e.position + e.transform.up)) as EntityItem;
                        if (pack != null)
                        {
                            Bag bag = new Bag(Math.Max(drop.Count, 1));
                            for (int i = 0; i < drop.Count; i++) bag.items[i] = drop[i];
                            GameManager.Instance.RequestToSpawnEntityServer(new EntityCreationData(pack, true) { entityName = Localization.Get(e.EntityName), id = -1, bag = bag });
                            pack.OnEntityUnload();
                        }
                    }
                    if (hadWindow) HarvestManager.Remove(e.entityId);
                }
                catch (Exception ex)
                {
                    ScLog.Once("npc-drop-window", ex);
                }
            }
        }

        /// <summary>
        /// Who the player is allowed to hurt. SCore shields every friendly NPC twice over
        /// (EntityTargetingUtilities.CanDamage): one who follows you counts as your ally, so CanDamageAlly
        /// refuses flatly, and one with no leader falls to the faction rule instead, which refuses because you
        /// are Neutral to whiteriver and an EntityNPC target treats Neutral as friendly fire. Both gates are
        /// consulted by CanDamageEntity - what ItemActionAttack.Hit asks before it resolves a hit at all - and
        /// again inside SCore's DamageEntity (on both of its NPC classes), so a shot simply vanished. The community wants the
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
                    // damage outright and only SCore's own NPC classes ask this rule.
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
        /// Raiders wear the player body too, but they are EntityNPCBandit (SCore's hostile class), not one of its
        /// friendly classes, so the survivor PostInit hook never runs for them: without this their hit capsules stay
        /// on the no-collision layer EntityFactory put them on and shots pass straight through.
        /// </summary>
        [HarmonyPatch(typeof(EntityNPCBandit), nameof(EntityNPCBandit.PostInit), new Type[0])]
        public static class Patch_RaiderPostInit
        {
            /// <summary>W2: both weapons on the item list before the bandit's own fill and SCore's (ScLoadout).</summary>
            [HarmonyPrefix]
            public static void Prefix(EntityNPCBandit __instance)
            {
                try { ScLoadout.BeforePostInit(__instance); }
                catch (Exception e) { ScLog.Once("loadout-raider-postinit", e); }
            }

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
        /// reads one immutable set and nothing else. Both classes' IsSavedToFile apply the same rule (saved when the
        /// Leader or Persist cvar exists, else not for Dynamic or Biome spawns), so the veto is the same on both.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_IsSavedToFile
        {
            [HarmonyPrepare]
            public static bool Prepare() { return ScNpc.Declared("IsSavedToFile", NoArgs).Count > 0; }

            [HarmonyTargetMethods]
            public static IEnumerable<MethodBase> TargetMethods() { return ScNpc.Declared("IsSavedToFile", NoArgs); }

            [HarmonyPostfix]
            public static void Postfix(EntityAlive __instance, ref bool __result)
            {
                if (__result && __instance != null && ScRunners.IsRunnerBody(__instance)) __result = false;
            }
        }

        [HarmonyPatch]
        public static class Patch_PostInit
        {
            [HarmonyPrepare]
            public static bool Prepare() { return ScNpc.Declared("PostInit", NoArgs).Count > 0; }

            [HarmonyTargetMethods]
            public static IEnumerable<MethodBase> TargetMethods() { return ScNpc.Declared("PostInit", NoArgs); }

            /// <summary>
            /// Both weapons on the item list before SCore's EntityAlive.PostInit postfix fills the slots from it, before
            /// SetupStartingItems, and before PostInit re-equips the saved weapon (ScLoadout). BeforePostInit acts on the
            /// community's 0-XNPCCore survivor classes (EntityAliveSDXV4: their rolled pair, migration M7) and passes every
            /// other class through untouched (pack NPCs, XNPC's other classes).
            /// </summary>
            [HarmonyPrefix]
            public static void Prefix(EntityAlive __instance)
            {
                try { ScLoadout.BeforePostInit(__instance); }
                catch (Exception e) { ScLog.Once("loadout-postinit", e); }
            }

            [HarmonyPostfix]
            public static void Postfix(EntityAlive __instance)
            {
                try
                {
                    if (__instance == null) return;
                    // after EntityFactory's collider pass: a player-bodied survivor is otherwise unhittable. Every
                    // player body needs it, not only members and prospects - a stranger found in the wild or spawned
                    // from the console used to keep its hit capsules on the no-collision layer.
                    Patches_Sdcs.FixColliderLayers(__instance);
                    // a new 0-XNPCCore survivor's starting ammo, once per entity (its own catch; decision D3)
                    ScLoadout.AfterPostInit(__instance);
                    if (!ScRegistry.Loaded) return;
                    if (ScRegistry.Cleanup.Contains(__instance.entityId))
                    {
                        if (ScCommunity.CarriesOurMarks(__instance)) ScCommunity.CleanupStale(__instance);
                        ScRegistry.Cleanup.Remove(__instance.entityId);
                        return;
                    }
                    if (ScRegistry.Doomed.Contains(__instance.entityId)) { ScNpc.SendOnMission(__instance, true); return; }
                    // an outpost stand-in never comes back from a save: a fresh one spawns while the player is there
                    if (__instance.Buffs != null && __instance.Buffs.HasCustomVar(ScGarrison.StandInOutpostCvar)) { ScGarrison.Orphans.Add(__instance.entityId); return; }
                    // an offline physical runner exists only as its snapshot: this is an old chunk copy (hidden now,
                    // removed next tick - never Doomed, the id comes back with the real runner) or a stranger who
                    // inherited the id (untouched: no spawner/Persist stamp for them)
                    Member rm = ScRegistry.GetMember(__instance.entityId);
                    if (rm != null && ScRunners.IsPhysical(rm) && rm.Offline && __instance.entityId != ScRunners.RecreatingId)
                    {
                        if (ScRunners.IsStaleCopy(rm, __instance)) { ScNpc.SendOnMission(__instance, true); ScRunners.StaleIds.Add(__instance.entityId); }
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
                    ScLog.Once("npc.PostInit", e);
                }
            }
        }
    }
}
