using System;
using HarmonyLib;

namespace SurvivorCommunity
{
    /// <summary>
    /// The hooks behind ScTrust (survivor trust toward the player).
    ///
    /// Who the player can hurt: SCore answers EntityTargetingUtilities.CanDamage(player, survivor) false for every
    /// survivor - whiteriver is Neutral (400) to the player and SCore counts Neutral as friendly fire for an EntityNPC -
    /// and its prefix on ItemActionDynamic.hitTarget returns that answer directly, so a melee swing on a survivor never
    /// landed at all. Bullets, arrows and thrown weapons went through CanTakeDamage, which Patches_Npc.Patch_FriendlyFire
    /// already opened. Patch_CanDamage applies the same rule (ScTrust.PlayerMayHurt) one level down, so melee lands too.
    ///
    /// Who the survivor fights: SCore's faction primitive EntityUtilities.GetFactionRelationship(self, target) feeds
    /// IsEnemy (the UAI target filter), CanDamage both ways and IsFriend. Patch_FactionRelationship answers 0 (hate) for
    /// a survivor and the player it is hostile to, in either order - the survivor attacks that player on sight and its
    /// hits land, the player's companions and members see it as an enemy - and leaves every other pair alone.
    ///
    /// The hit that starts it: Patch_DamageResponse, a postfix on EntityAliveSDXV4.ProcessDamageResponseLocal, which runs
    /// once on the host for every landed hit (the host's own, and a client's through NetPackageDamageEntity).
    ///
    /// E2: a hit on one of an enclave's people goes to the enclave's rules instead (ScEnclavePeople.OnHitBy: a warning,
    /// then the whole enclave hostile), and the faction check also answers 0 between a body of an enclave turned hostile
    /// and a player, one of the community or a player's companion (ScEnclavePeople.HostilePair) - residents share the
    /// survivors' faction, so without it they would watch such a body raid the base.
    ///
    /// E13a: a hit on one of a band's people goes to the band's rule (ScBands.OnHitBy: a drifter band turns as a whole);
    /// ScTrust.Subject leaves band bodies out, so they never carry a wild survivor's own grudge.
    /// </summary>
    public static class Patches_Trust
    {
        [HarmonyPatch(typeof(EntityTargetingUtilities), nameof(EntityTargetingUtilities.CanDamage))]
        public static class Patch_CanDamage
        {
            [HarmonyPostfix]
            public static void Postfix(EntityAlive self, Entity target, ref bool __result)
            {
                try
                {
                    if (__result) return;
                    EntityPlayer p = self as EntityPlayer;
                    if (p == null) return;
                    if (ScTrust.PlayerMayHurt(target as EntityAlive, p)) __result = true;
                }
                catch (Exception e)
                {
                    ScLog.Once("trust-candamage", e);
                }
            }
        }

        [HarmonyPatch(typeof(EntityUtilities), nameof(EntityUtilities.GetFactionRelationship))]
        public static class Patch_FactionRelationship
        {
            [HarmonyPostfix]
            public static void Postfix(EntityAlive self, EntityAlive target, ref float __result)
            {
                // hot: per target, per action, per AI decision, for every NPC - type tests and one dictionary hit
                if (__result <= 0f) return;
                // E2: a body of an enclave turned hostile against a player, one of the community or a player's companion, in
                // either order (one volatile read while no enclave is hostile; then two cvar reads and a set hit)
                if (ScEnclavePeople.AnyHostile && ScEnclavePeople.HostilePair(self, target)) { __result = 0f; return; }
                EntityPlayer p = self as EntityPlayer;
                EntityAlive npc = target;
                if (p == null)
                {
                    p = target as EntityPlayer;
                    npc = self;
                }
                if (p == null || npc == null || npc is EntityPlayer) return;
                if (ScTrust.IsHostileTo(npc, p)) __result = 0f;
            }
        }

        [HarmonyPatch(typeof(EntityAliveSDXV4), nameof(EntityAliveSDXV4.ProcessDamageResponseLocal))]
        public static class Patch_DamageResponse
        {
            [HarmonyPostfix]
            public static void Postfix(EntityAliveSDXV4 __instance, DamageResponse _dmResponse)
            {
                try
                {
                    // any landed hit, even one the armour soaked up (the user's rule); a killing blow ends it anyway
                    if (!ScCommunity.IsHost || __instance == null || _dmResponse.Fatal) return;
                    DamageSource src = _dmResponse.Source;
                    if (src == null || src.BuffClass != null) return;   // a bleed or other effect over time: the hit that caused it counted
                    // a turret's or a dart trap's hit names its owner as the attacker, with the turret as the creator
                    if (src.CreatorEntityId != -1 && src.CreatorEntityId != src.getEntityId()) return;
                    World w = ScCommunity.World;
                    EntityPlayer p = w != null ? w.GetEntity(src.getEntityId()) as EntityPlayer : null;
                    if (p == null) return;
                    // V2: a raider's hit or retreat taunt (ScTaunts; raiders only, never stops the rules below)
                    if (ScXnpc.IsRaider(__instance)) ScTaunts.OnHitBy(__instance, p);
                    // E2: one of an enclave's people - the enclave's warning and hostility rules, not a personal grudge
                    if (ScEnclavePeople.OnHitBy(__instance, p)) return;
                    // E13a: one of a band's people - the whole drifter band turns, not a wild survivor's grudge
                    if (ScBands.OnHitBy(__instance, p)) return;
                    ScTrust.OnHitBy(__instance, p);
                }
                catch (Exception e)
                {
                    ScLog.Once("trust-hit", e);
                }
            }
        }
    }
}
