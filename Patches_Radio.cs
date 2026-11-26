using System;
using System.Collections.Generic;
using HarmonyLib;

namespace SurvivorCommunity
{
    /// <summary>
    /// Air drops only on request. AIDirectorAirDropComponent.Tick asks SpawnAirDrop whenever the sandbox
    /// frequency says a drop is due; with the radio installed that call is refused unless the radio itself is
    /// asking (ScRadio.DropRequested), so the sandbox frequency becomes the radio's cooldown instead of a
    /// schedule. RadioOnlyAirdrops=false in the config restores vanilla's own drops.
    /// Round MP5 (part R): the radio works for every community player, so:
    ///  - the radio's own drop flies to the player who asked (ScRadio.DropFor): vanilla's SpawnAirDrop, with that one player
    ///    instead of every living one (in single player the one player, as before);
    ///  - RadioOnlyAirdrops holds on a dedicated server too, while at least one player online runs the server's
    ///    SurvivorCommunity (ScNet.ClientMatches: they can ask at a radio). With nobody who could ask, vanilla's schedule
    ///    runs, as round MP0 kept it for every dedicated server.
    /// </summary>
    public static class Patches_Radio
    {
        /// <summary>Round MP5 (part R): vanilla's private activeAirDrop (bound once; null when the field is missing - vanilla's own pick then).</summary>
        private static AccessTools.FieldRef<AIDirectorAirDropComponent, AIAirDrop> activeRef;
        private static bool bound;

        [HarmonyPatch(typeof(AIDirectorAirDropComponent), nameof(AIDirectorAirDropComponent.SpawnAirDrop), new Type[0])]
        public static class Patch_SpawnAirDrop
        {
            [HarmonyPrefix]
            public static bool Prefix(AIDirectorAirDropComponent __instance, ref bool __result)
            {
                try
                {
                    if (ScRadio.DropRequested)
                    {
                        // round MP5 (part R): near the caller, not near every player
                        EntityPlayer to = ScRadio.DropFor;
                        if (to != null && !to.IsDead() && SpawnFor(__instance, to, ref __result)) return false;
                        return true;
                    }
                    ScConfig.EnsureParsed();
                    if (!ScConfig.RadioOnlyAirdrops) return true;
                    // round MP0 let a dedicated server (no local player) keep vanilla's drops: nobody could open the radio there.
                    // Round MP5 (part R): any community player can ask at a radio now - refused while one who can is online
                    if (ScCommunity.LocalPlayer == null && !AnyoneCanAsk()) return true;
                    __result = false;
                    return false;
                }
                catch (Exception e)
                {
                    ScLog.Once("AIDirectorAirDropComponent.SpawnAirDrop", e);
                    return true;
                }
            }
        }

        /// <summary>
        /// Round MP5 (part R), a dedicated server: a player online who can use the radio. Review R-m1: everyone but a client known
        /// to run another SurvivorCommunity - a joining player is tracked by the director before their hello arrives, and
        /// waiting for the hello let an overdue vanilla drop fly to them at login.
        /// </summary>
        private static bool AnyoneCanAsk()
        {
            List<EntityPlayer> all = ScPlayers.All;
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && ScPlayers.InCommunity(all[i]) && !ScNet.KnownMismatch(all[i])) return true;
            return false;
        }

        /// <summary>
        /// Round MP5 (part R): vanilla's SpawnAirDrop for one player - the same no-loot and drops-off answer, the same "a drop is
        /// on its way" refusal, and its AIAirDrop made with that player alone, so the planes fly to them. False when vanilla's
        /// field cannot be reached (vanilla's own pick runs instead).
        /// </summary>
        private static bool SpawnFor(AIDirectorAirDropComponent comp, EntityPlayer to, ref bool result)
        {
            if (!bound)
            {
                bound = true;
                try { activeRef = AccessTools.FieldRefAccess<AIDirectorAirDropComponent, AIAirDrop>("activeAirDrop"); }
                catch (Exception e) { ScLog.Once("radio-drop-bind", e); activeRef = null; }
                if (activeRef == null) ScLog.Warn("radio: AIDirectorAirDropComponent.activeAirDrop not found - the radio's drop falls near every player, as vanilla's");
            }
            if (activeRef == null || comp == null || comp.Director == null || comp.Director.World == null) return false;
            if (LootContainer.NoLoot || AIDirectorAirDropComponent.MaxDayCount == 0) { result = true; return true; }
            if (activeRef(comp) != null) { result = false; return true; }
            activeRef(comp) = new AIAirDrop(comp, comp.Director.World, new List<EntityPlayer> { to });
            result = true;
            ScLog.Verbose("radio: the air drop flies to " + to.PlayerDisplayName);
            return true;
        }
    }
}
