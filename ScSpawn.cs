using System;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// One way to (re-)create the body of a survivor the community knows (migration design M3.5). Every site that puts a
    /// known survivor back into the world - a missing member called back, an outpost stand-in, a rescue stand-in, a
    /// runner's fresh body - comes through here, so the class, the look, the weapons and the name are handled the same way
    /// at every site:
    ///  - the class is the record's own (m.Class); a record never changes class in place. SurvivorCommunity's own survivor
    ///    classes are gone (decision D11), so a record of one of them - an old save's - finds no class and is not brought
    ///    back (ClassOf; ScRegistry.PreMigrationLine says why);
    ///  - the record's look is handed to the model while it is built: Patches_Xnpc's createModel prefix (X1) takes it
    ///    as ScLooks.Pending;
    ///  - the record's two weapons (m.Loadout) go to ScLoadout as Pending, so the new body carries the same pair, and
    ///    the rounds they last carried (m.Ammo) go with them instead of a new survivor's starting stack (ScAmmo);
    ///  - the record's name replaces the one the class rolled at construction (both NPC classes roll a random first
    ///    name and title from the class's Names / Titles);
    ///  - an XNPC member or stand-in loses XNPC's kill levels (decision D8; ScXnpc.StripLevels).
    /// Strangers (radio walk-ins, console spawns) use Create alone: a rolled look, the contact's pair or a rolled one, the
    /// class's own name.
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
        /// Is this a friendly class that wears a look of ours - the community's XNPC survivors, the only friendly
        /// player-body classes since D11 - so that a stranger of it should be given a rolled look? (SC's raiders wear one
        /// too since M10, but are never friendly: they roll their own in ScLooks.ForEntity.)
        /// </summary>
        public static bool IsSurvivorBodyClass(string className)
        {
            return ScXnpc.IsSurvivorClass(className);
        }

        /// <summary>
        /// Creates (does not spawn) a framework NPC of the class, built wearing a copy of the look (null: the model
        /// resolves one itself, from the registry by id or a fresh roll) and carrying a copy of the pair (null, or a half:
        /// ScLoadout resolves or rolls the rest). known: the member this body is re-created for - it then starts with the
        /// rounds their record says they carry (ScAmmo), never a new survivor's starting stack; null for a genuinely new
        /// survivor (a radio walk-in, a console spawn). Null when the class does not make an SCore NPC. The pending values
        /// are always cleared again, so a failed creation cannot hand them to the next body built.
        /// </summary>
        public static EntityAlive Create(int classId, Vector3 at, Vector3 rotation, Look look, Loadout loadout = null, Member known = null)
        {
            try
            {
                if (look != null) ScLooks.Pending = look.Clone();
                if (loadout != null) ScLoadout.Pending = loadout.Clone();
                if (known != null) { ScAmmo.PendingKnown = true; ScAmmo.PendingRecord = ScAmmo.Copy(known.Ammo); ScAmmo.PendingMags = known.Mags; }
                return ScNpc.As(EntityFactory.CreateEntity(classId, at, rotation));
            }
            finally
            {
                ScLooks.Pending = null;
                ScLoadout.Pending = null;
                ScAmmo.PendingKnown = false;
                ScAmmo.PendingRecord = null;
                ScAmmo.PendingMags = null;
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
            if (community && ScXnpc.HasXnpcLevels(e)) ScXnpc.StripLevels(e);
        }

        /// <summary>
        /// The whole respawn: class, look, weapons, create, spawn into the world, name (and levels). Null when the class
        /// is gone or the factory made nothing; the caller then reports its own error.
        /// </summary>
        public static EntityAlive Body(World w, Member m, Vector3 at, Vector3 rotation, bool community)
        {
            int cid;
            if (w == null || !ClassOf(m, out cid)) return null;
            EntityAlive e = Create(cid, at, rotation, m.Look, m.Loadout, m);
            if (e == null) return null;
            w.SpawnEntityInWorld(e);
            Identify(e, m, community);
            return e;
        }
    }
}
