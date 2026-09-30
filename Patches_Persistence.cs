using System;
using System.IO;
using HarmonyLib;

namespace SurvivorCommunity
{
    /// <summary>
    /// Registry lifecycle on the same beats vanilla uses for players.xml (its directory is the save directory).
    /// </summary>
    public static class Patches_Persistence
    {
        // static PersistentPlayerList ReadXML(string filePath)  - PersistentPlayerList.cs:405
        [HarmonyPatch(typeof(PersistentPlayerList), nameof(PersistentPlayerList.ReadXML), new[] { typeof(string) })]
        public static class Patch_ReadXML
        {
            [HarmonyPostfix]
            public static void Postfix(string filePath, PersistentPlayerList __result)
            {
                try
                {
                    if (__result == null || string.IsNullOrEmpty(filePath)) return;
                    ScRegistry.Load(Path.GetDirectoryName(filePath));
                }
                catch (Exception e)
                {
                    ScLog.Once("PersistentPlayerList.ReadXML", e);
                }
            }
        }

        // void Write(string filePath) - PersistentPlayerList.cs:472 (a Write(BinaryWriter) overload exists)
        [HarmonyPatch(typeof(PersistentPlayerList), nameof(PersistentPlayerList.Write), new[] { typeof(string) })]
        public static class Patch_WriteXML
        {
            [HarmonyPostfix]
            public static void Postfix(string filePath)
            {
                try
                {
                    if (!ScCommunity.IsHost) return;
                    // WorldShuttingDown (our FlushAndReset: save, then clear + latch) fires BEFORE this write.
                    if (ScRegistry.IsShuttingDown) return;
                    if (!string.IsNullOrEmpty(filePath)) ScRegistry.SetDir(Path.GetDirectoryName(filePath));
                    ScRegistry.SaveAtomic();
                }
                catch (Exception e)
                {
                    ScLog.Once("PersistentPlayerList.Write", e);
                }
            }
        }
    }
}
