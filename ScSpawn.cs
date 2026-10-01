using System;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// One way to (re-)create the body of a survivor the community knows (migration design M3.5). Every site that puts a
    /// known survivor back into the world - a missing member called back, an outpost stand-in, a rescue stand-in, a
    /// runner's fresh body - comes through here, so the class, the look and the name are handled the same way for SC's
    /// own player-body classes and for the 0-XNPCCore survivor classes the community owns:
    ///  - the class is the record's own (m.Class); a record never changes class in place, so a member who joined as an
    ///    npcSdcs survivor comes back as one, and a legacy scSurvivor member as a legacy one;
    ///  - the record's look is handed to the model while it is built: SC's model shim (Patches_Sdcs.Patch_Init) or, for an
    ///    XNPC body, Patches_Xnpc's createModel prefix (X1) takes it as ScLooks.Pending;
    ///  - the record's name replaces the one the class rolled at construction (both NPC classes roll a random first
    ///    name and title from the class's Names / Titles);
    ///  - an XNPC member or stand-in loses XNPC's kill levels (decision D8; ScXnpc.StripLevels).
    /// Strangers (radio walk-ins) use Create alone: a rolled look, the class's own name.
    ///
    /// What each caller does afterwards (registry re-keying, its own cvars, spawner source, orders) stays with the caller.
    /// </summary>
    public static class ScSpawn
    {
        /// <summary>
        /// The class to re-create this survivor in: the record's own, while the game still defines it. False (and the
        /// caller gives up) when the record has no class or the class is gone - for example a save from before a class
        /// was removed, where spawning anything else would quietly turn the survivor into somebody new.
        /// </summary>
        public static bool ClassOf(Member m, out int classId)
        {
            classId = 0;
            if (m == null || string.IsNullOrEmpty(m.Class)) return false;
            classId = EntityClass.FromString(m.Class);
            return ScCommunity.ClassExists(classId);
        }

        /// <summary>
        /// Is this a friendly class that wears a look of ours - SC's survivor classes and the community's XNPC survivors -
        /// so that a stranger of it should be given a rolled look?
        /// </summary>
        public static bool IsSurvivorBodyClass(string className)
        {
            return ScLooks.IsSurvivorClass(className) || ScXnpc.IsManagedClass(className);
        }

        /// <summary>
        /// Creates (does not spawn) a framework NPC of the class, built wearing a copy of the look (null: the model
        /// resolves one itself, from the registry by id or a fresh roll). Null when the class does not make an SCore NPC.
        /// The pending look is always cleared again, so a failed creation cannot hand it to the next body built.
        /// </summary>
        public static EntityAlive Create(int classId, Vector3 at, Vector3 rotation, Look look)
        {
            try
            {
                if (look != null) ScLooks.Pending = look.Clone();
                return ScNpc.As(EntityFactory.CreateEntity(classId, at, rotation));
            }
            finally
            {
                ScLooks.Pending = null;
            }
        }

        /// <summary>
        /// Makes a freshly spawned body this survivor: the registry name, and for one of the community's XNPC survivors
        /// that is a member or a stand-in (community) no XNPC kill levels.
        /// </summary>
        public static void Identify(EntityAlive e, Member m, bool community)
        {
            if (e == null || m == null) return;
            ScNpc.SetName(e, m.Name, "");             // the class rolled a random name at construction: this body IS the survivor
            if (community && ScXnpc.IsBody(e)) ScXnpc.StripLevels(e);
        }

        /// <summary>
        /// The whole respawn: class, look, create, spawn into the world, name (and levels). Null when the class is gone
        /// or the factory made nothing; the caller then reports its own error.
        /// </summary>
        public static EntityAlive Body(World w, Member m, Vector3 at, Vector3 rotation, bool community)
        {
            int cid;
            if (w == null || !ClassOf(m, out cid)) return null;
            EntityAlive e = Create(cid, at, rotation, m.Look);
            if (e == null) return null;
            w.SpawnEntityInWorld(e);
            Identify(e, m, community);
            return e;
        }
    }
}
