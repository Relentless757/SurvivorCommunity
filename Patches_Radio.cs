using System;
using HarmonyLib;

namespace SurvivorCommunity
{
    /// <summary>
    /// Air drops only on request. AIDirectorAirDropComponent.Tick asks SpawnAirDrop whenever the sandbox
    /// frequency says a drop is due; with the radio installed that call is refused unless the radio itself is
    /// asking (ScRadio.DropRequested), so the sandbox frequency becomes the radio's cooldown instead of a
    /// schedule. RadioOnlyAirdrops=false in the config restores vanilla's own drops.
    /// </summary>
    public static class Patches_Radio
    {
        [HarmonyPatch(typeof(AIDirectorAirDropComponent), nameof(AIDirectorAirDropComponent.SpawnAirDrop), new Type[0])]
        public static class Patch_SpawnAirDrop
        {
            [HarmonyPrefix]
            public static bool Prefix(ref bool __result)
            {
                try
                {
                    if (ScRadio.DropRequested) return true;
                    ScConfig.EnsureParsed();
                    if (!ScConfig.RadioOnlyAirdrops) return true;
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
    }
}
