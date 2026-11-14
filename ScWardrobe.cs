#if SC_V34
using System;
using System.Collections.Generic;
using NPCSdcsCharacters.Pure;      // WardrobeRules (SdcsArmorSets is in the global namespace)

namespace SurvivorCommunity
{
    /// <summary>
    /// Round n05: 0-XNPCCore 3.4.05's Wardrobe ("Dress your hires") made to work on SurvivorCommunity's own people.
    ///
    /// 0-XNPCCore stores a wardrobe pick as four entity cvars - SdcsWearHead / SdcsWearChest / SdcsWearHands /
    /// SdcsWearFeet - each a 24-bit hash of an armor item name (WardrobeRules.CvarNames, SdcsArmorSets.PieceByHash).
    /// The only thing that ever READS them is SdcsLookApplier.WearOverrides, which runs inside SdcsLookApplier.Apply -
    /// and SC refuses Apply for its own bodies (ScBandits.Patch_GearOnly.Prefix), because Apply would also roll her a
    /// fresh archetype, armor set, name and weapon and leave her wearing a stranger's face. So out of the box the
    /// Wardrobe opened, listed her real pack, accepted a pick, made her SAY the success line - and changed nothing.
    ///
    /// SC owns its people's looks, so SC honors the pick itself. Both sides already agree on the shape: 0-XNPCCore's
    /// four cvars are in slot order Head, Chest, Hands, Feet, and so is SC's Look.Gear - a 1:1 match by index.
    ///
    /// "Back to my own &lt;slot&gt;" clears a cvar. Her own piece is whatever she wore before the first wardrobe pick, so
    /// that is snapshotted per body the first time a slot is overridden (ownGear) and put back when the cvar clears.
    /// The snapshot is this session's: after a world reload the piece she is wearing has become her own, and clearing
    /// the slot then leaves it as it is rather than undoing an older roll. The pick itself survives a reload, because
    /// it is written into her record like any other look change.
    /// </summary>
    public static class ScWardrobe
    {
        /// <summary>Her gear before the Wardrobe first touched it, by entity id - what "Back to my own" restores.</summary>
        private sealed class Own
        {
            public readonly string[] Gear = { "", "", "", "" };
            public readonly int[] Quality = { 1, 1, 1, 1 };
        }
        private static readonly Dictionary<int, Own> ownGear = new Dictionary<int, Own>();

        /// <summary>Forgets a body's pre-wardrobe snapshot (its entity is gone).</summary>
        public static void Forget(int entityId) { ownGear.Remove(entityId); }

        /// <summary>
        /// The armor item this slot's cvar names, or null when the slot carries no override (cvar missing, 0, or naming
        /// a piece no longer loaded - 0-XNPCCore warns about that one itself, so SC stays quiet and leaves the slot alone).
        /// </summary>
        private static string Override(EntityAlive npc, int slot)
        {
            string cvar = WardrobeRules.CvarNames[slot];
            if (!npc.Buffs.HasCustomVar(cvar)) return null;
            int hash = (int)npc.Buffs.GetCustomVar(cvar);
            if (hash <= 0) return null;
            return SdcsArmorSets.PieceByHash(hash, out WardrobeRules.Piece piece) ? piece.Item : null;
        }

        /// <summary>The quality of the piece she is actually carrying (her window first, then her bag), or the fallback.</summary>
        private static int QualityOf(EntityAlive npc, string item, int fallback)
        {
            try
            {
                if (HarvestManager.Has(npc.entityId))
                {
                    int q = Find(HarvestManager.GetOrCreate(npc.entityId).items, item);
                    if (q > 0) return q;
                }
                if (npc.bag != null)
                {
                    int q = Find(npc.bag.ItemGrid.items, item);
                    if (q > 0) return q;
                }
            }
            catch (Exception ex) { ScLog.Once("wardrobe-quality", ex); }
            return fallback;
        }

        /// <summary>That item's quality in these stacks, or 0 when it is not among them.</summary>
        private static int Find(ItemStack[] stacks, string item)
        {
            if (stacks == null) return 0;
            foreach (ItemStack s in stacks)
                if (s != null && !s.IsEmpty() && s.itemValue != null && s.itemValue.ItemClass != null
                    && string.Equals(s.itemValue.ItemClass.GetItemName(), item, StringComparison.Ordinal))
                    return Math.Max(1, (int)s.itemValue.Quality);
            return 0;
        }

        /// <summary>
        /// Folds the Wardrobe's four cvars into SC's own look for this body and applies it. Called at the one chokepoint
        /// every wardrobe route reaches (ScBandits.Patch_GearOnly.Prefix, in front of SC's refusal of 0-XNPCCore's
        /// applier), so a pick made by the player, by the hold-E ring or over the network all land here.
        /// Returns true when something actually changed.
        /// </summary>
        public static bool Honor(EntityAlive npc)
        {
            if (npc == null || npc.Buffs == null || npc.isEntityRemote || !ScCommunity.IsHost) return false;
            if (!ScXnpc.IsBody(npc) || ScXnpc.IsRaider(npc)) return false;   // raiders are not dressed by the player
            try
            {
                // Get, not ForEntity: ForEntity is the body-construction accessor - it consumes the global Pending slot,
                // writes the session caches, and ROLLS A FRESH RANDOM LOOK when it finds nothing. Honor would then clone
                // that into her records and apply it, changing her whole appearance from a wardrobe pick.
                Look look = ScLooks.Get(npc);
                if (look == null) return false;

                if (!ownGear.TryGetValue(npc.entityId, out Own own))
                {
                    own = new Own();
                    for (int i = 0; i < 4; i++) { own.Gear[i] = look.Gear[i] ?? ""; own.Quality[i] = look.Quality[i]; }
                    ownGear[npc.entityId] = own;
                }

                Look next = look.Clone();
                bool changed = false;
                for (int i = 0; i < 4; i++)
                {
                    string want = Override(npc, i);
                    // The quality has to travel with the name: ScLooks.SyncGear builds the piece with MakeItem(name,
                    // Quality[slot]), so leaving Quality behind would dress her in a Q1 copy of the Q6 helmet she was
                    // handed. Her real one is in her window or bag, the same place 0-XNPCCore's PackValueFor looks.
                    int quality = want != null ? QualityOf(npc, want, next.Quality[i]) : own.Quality[i];
                    if (want == null) want = own.Gear[i];           // no override: back to the piece she had
                    if (string.Equals(next.Gear[i] ?? "", want ?? "", StringComparison.Ordinal) && next.Quality[i] == quality) continue;
                    next.Gear[i] = want ?? "";
                    next.Quality[i] = quality;
                    changed = true;
                }
                if (!changed) return false;

                // Her record keeps the pick, so it survives a rebuild and a reload - the same way sc look writes one.
                int records = 0;
                Member m = ScRegistry.GetMember(npc.entityId);
                if (m != null) { m.Look = next; records++; }
                Prospect pr = ScRegistry.GetProspect(npc.entityId);
                if (pr != null) { pr.Look = next; records++; }
                EncPerson ep = ScEnclavePeople.PersonOf(npc);
                if (ep != null && ep.Who != null) { ep.Who.Look = next; records++; }
                BandPerson bp = ScBands.PersonOf(npc);
                if (bp != null && bp.Who != null) { bp.Who.Look = next; records++; }

                ScLooks.Apply(npc, next);
                if (records > 0 && ScRegistry.Loaded) ScRegistry.SaveAtomic();
                Patches_Xnpc.Count("n05-wardrobe-worn");
                ScLog.Out("wardrobe: " + npc.EntityName + " (" + npc.entityId + ") now wears ["
                          + string.Join(", ", next.Gear) + "]" + (records == 0 ? " (a stranger: this session only)" : ""));
                return true;
            }
            catch (Exception ex) { ScLog.Once("wardrobe-honor", ex); return false; }
        }
    }
}
#endif
