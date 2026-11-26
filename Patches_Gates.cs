using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round MP6 (part G), 2026-11-26: the AI's community scope. Every UAI consideration and task SC adds (UaiOps, UaiRetreat,
    /// UaiSocial, UaiDowned, ScHealBackOff, ScPosture, Patches_VoiceControl's MayEngage ...) and every SC hook that runs from an
    /// NPC's UAI tick (0-XNPCCore's craft, loot and book tasks calling into Patches_Craft) reads "the community" - the home zone,
    /// the stockpile, the post rules - for the NPC it decides for. All of them run inside vanilla's UAIBase.Update(Context), the
    /// one entry of an NPC's utility AI: this patch opens the NPC's community's scope around it (ScGates.ScopeOf: a member's
    /// community, nothing for a stranger, who reads no community). With one community nothing is opened (Current is always
    /// community 0 there). The scope opens first of every prefix (Priority.First, before SCore's own on the same method) and
    /// closes in a finalizer, so it closes whatever throws; a stack keeps nested updates (none in vanilla) in order. Main
    /// thread only, as the UAI.
    /// </summary>
    public static class Patches_Gates
    {
        public static MethodBase UaiUpdateTarget()
        {
            return AccessTools.DeclaredMethod(typeof(UAI.UAIBase), "Update", new[] { typeof(UAI.Context) });
        }

        [HarmonyPatch]
        public static class Patch_UaiScope
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                if (UaiUpdateTarget() != null) return true;
                ScLog.Err("UAIBase.Update(Context) not found (game update?) - with several communities an NPC's AI reads the default community");
                return false;
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return UaiUpdateTarget(); }

            [HarmonyPrefix]
            [HarmonyPriority(Priority.First)]
            public static void Prefix(UAI.Context _context)
            {
                ScScope s = default(ScScope);
                try { s = ScGates.ScopeOf(_context != null ? _context.Self : null); }
                catch (Exception e) { ScLog.Once("gates-uai-scope", e); }
                ScGates.PushHeld(s);
            }

            [HarmonyFinalizer]
            public static void Finalizer()
            {
                ScGates.PopHeld();
            }
        }
    }
}
