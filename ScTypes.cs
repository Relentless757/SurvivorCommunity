using System;
using System.Reflection;

namespace SurvivorCommunity
{
    /// <summary>
    /// Another mod's type by its full name ("Sonja.VanityVault.FacepaintManager", a nested one as "Outer+Inner"), for the
    /// mods SC binds by name, with no compile-time reference. Not Harmony's AccessTools.TypeByName: the game's Harmony
    /// answers that by walking every type of every loaded assembly and building each one's full name
    /// (AllTypes().FirstOrDefault(t => t.FullName == name)), about 65 ms a call in this modded game. ScVanity's six such
    /// calls were a 0.4 s hitch inside the build of the first painted survivor of a session (Kyle, 2026-10-13). Asking each
    /// loaded assembly for the name is a lookup in its own type table, so a whole search here takes well under a
    /// millisecond. Same first match in the same assembly order as TypeByName's full-name pass; no short-name fallback (every
    /// caller passes a full name). Null when no loaded assembly has the type.
    /// </summary>
    public static class ScTypes
    {
        public static Type Find(string fullName)
        {
            if (string.IsNullOrEmpty(fullName)) return null;
            Type t = Type.GetType(fullName, false);
            if (t != null) return t;
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    t = a.GetType(fullName, false);
                    if (t != null) return t;
                }
                catch (Exception)
                {
                    // an assembly that cannot answer (a dynamic or half-loaded one) has nothing of ours
                }
            }
            return null;
        }
    }
}
