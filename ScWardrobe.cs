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
    /// "Back to my own &lt;slot&gt;" clears a cvar. Her own piece is whatever she wore in that slot before the wardrobe
    /// first overrode it, so that is snapshotted PER SLOT the first time the slot is overridden (ownGear) and put back
    /// when its cvar clears - and then forgotten, so from there on SC's own changes to the slot stand. A slot the
    /// wardrobe never overrode is never touched. (Round n05 snapshotted all four slots at the first pick and restored
    /// every un-overridden slot from it on each later pick, so armor SC gave her afterwards was quietly taken back off.)
    /// The snapshot is this session's: after a world reload the piece she is wearing has become her own, and clearing
    /// the slot then leaves it as it is rather than undoing an older roll. The pick itself survives a reload, because
    /// it is written into her record like any other look change.
    /// </summary>
    public static class ScWardrobe
    {
        /// <summary>Her piece in each slot before the Wardrobe overrode that slot, by entity id - what "Back to my own" restores.</summary>
        private sealed class Own
        {
            public readonly bool[] Taken = new bool[4];        // this slot is overridden and Gear/Quality hold her own piece
            public readonly string[] Gear = { "", "", "", "" };
            public readonly int[] Quality = { 1, 1, 1, 1 };
            /// <summary>
            /// Round n12 review (A): the slot's "own piece" is itself a Wardrobe copy her record noted (a pick from before a
            /// restart or a re-created body, which forgot the snapshot): its name, so "back to my own" notes it again. Null: a
            /// real own piece.
            /// </summary>
            public readonly string[] Copy = new string[4];
        }
        private static readonly Dictionary<int, Own> ownGear = new Dictionary<int, Own>();

        /// <summary>Forgets a body's pre-wardrobe snapshot (its entity is gone).</summary>
        public static void Forget(int entityId) { ownGear.Remove(entityId); }

        /// <summary>
        /// Round n12: the piece the Wardrobe dresses her in on this slot right now - its cvar names a piece and her look wears
        /// it - or null. That piece is a copy: the real one stays in her pack (0-XNPCCore's Wardrobe only offers what she
        /// carries), so the member inventory panel takes the real one out of her pack when it takes the slot over.
        /// </summary>
        public static string ActiveOverride(EntityAlive npc, Look look, int slot)
        {
            if (npc == null || npc.Buffs == null || look == null || slot < 0 || slot > 3) return null;
            try
            {
                string want = Override(npc, slot);
                return want != null && string.Equals(want, look.Gear[slot] ?? "", StringComparison.OrdinalIgnoreCase) ? want : null;
            }
            catch (Exception ex) { ScLog.Once("wardrobe-active", ex); return null; }
        }

        /// <summary>
        /// Round n12: the member inventory panel just set this slot (a piece dropped on it, or taken off). Neither a Wardrobe
        /// pick still in its cvar nor this session's snapshot of "her own piece" may undo that at the next Honor, so both go:
        /// the cvar is cleared the way 0-XNPCCore clears one (SdcsLookApplier.ForceClearCvar: 0, sent to clients), and the slot's
        /// snapshot is dropped. Honor then leaves the slot as the panel made it until something changes it again.
        /// </summary>
        public static void PanelChanged(EntityAlive npc, int slot)
        {
            if (npc == null || npc.Buffs == null || slot < 0 || slot > 3) return;
            try
            {
                string cvar = WardrobeRules.CvarNames[slot];
                if (npc.Buffs.HasCustomVar(cvar) && npc.Buffs.GetCustomVar(cvar) != 0f)
                    npc.Buffs.SetCustomVar(cvar, 0f, true, CVarOperation.set, true);
                Own own;
                if (ownGear.TryGetValue(npc.entityId, out own)) { own.Taken[slot] = false; own.Gear[slot] = ""; own.Quality[slot] = 1; own.Copy[slot] = null; }
                SetWorn(ScRegistry.GetMember(npc.entityId), slot, null);      // the review's fix 1: the slot is the panel's now
            }
            catch (Exception ex) { ScLog.Once("wardrobe-panel", ex); }
        }

        // ---- the Wardrobe's picks in her record (round n12 review, fix 1) ----
        //
        // A Wardrobe pick dresses her in a COPY of a piece from her pack. The panel must never hand out a second real item for
        // it. Its cvar says so only while the pick stands, and the snapshot above is this session's only - after a restart or
        // on a re-created body (ScRunners.Rekey forgets it), with the real piece taken out of her pack (0-XNPCCore then clears
        // the cvar and Honor, without a snapshot, leaves the look as it is), she still wore the copy and nothing remembered it
        // was one. Member.WardrobeWorn remembers it, by look slot, in her record.

        /// <summary>The piece her record says the Wardrobe dresses her in on this slot, while her look still wears it; else null.</summary>
        public static string RecordedPick(Member m, Look look, int slot)
        {
            if (m == null || m.WardrobeWorn == null || look == null || slot < 0 || slot > 3) return null;
            string w = m.WardrobeWorn[slot];
            return !string.IsNullOrEmpty(w) && string.Equals(w, look.Gear[slot] ?? "", StringComparison.OrdinalIgnoreCase) ? w : null;
        }

        /// <summary>The piece she wears on this slot as a Wardrobe copy (its cvar standing, or her record), or null.</summary>
        public static string WardrobePick(EntityAlive npc, Member m, Look look, int slot)
        {
            return ActiveOverride(npc, look, slot) ?? RecordedPick(m, look, slot);
        }

        /// <summary>Notes (item) or forgets (null) the Wardrobe's piece of one slot in her record; true when that changed it.</summary>
        private static bool SetWorn(Member m, int slot, string item)
        {
            if (m == null || slot < 0 || slot > 3) return false;
            item = string.IsNullOrEmpty(item) ? null : item;
            string had = m.WardrobeWorn != null ? m.WardrobeWorn[slot] : null;
            if (string.Equals(had ?? "", item ?? "", StringComparison.Ordinal)) return false;
            if (m.WardrobeWorn == null) m.WardrobeWorn = new string[4];
            m.WardrobeWorn[slot] = item;
            bool any = false;
            for (int i = 0; i < 4; i++) if (!string.IsNullOrEmpty(m.WardrobeWorn[i])) any = true;
            if (!any) m.WardrobeWorn = null;
            return true;
        }

        /// <summary>sc gear dressed her anew: no slot is the Wardrobe's any more.</summary>
        public static void ClearWorn(Member m)
        {
            if (m != null) m.WardrobeWorn = null;
        }

        /// <summary>The registry's wardrobe= value back ("helmetX,,glovesY," by look slot).</summary>
        public static void ReadWorn(Member m, string val)
        {
            if (m == null || string.IsNullOrEmpty(val)) return;
            string[] p = val.Split(',');
            for (int i = 0; i < 4 && i < p.Length; i++) SetWorn(m, i, p[i].Trim());
        }

        /// <summary>The registry's wardrobe= value, or null when no slot is the Wardrobe's.</summary>
        public static string EncodeWorn(Member m)
        {
            if (m == null || m.WardrobeWorn == null) return null;
            return string.Join(",", Array.ConvertAll(m.WardrobeWorn, s => s ?? ""));
        }

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

                if (!ownGear.TryGetValue(npc.entityId, out Own own)) ownGear[npc.entityId] = own = new Own();
                Member worn = ScRegistry.GetMember(npc.entityId);    // round n12 review: her record notes which slots are copies

                Look next = look.Clone();
                bool changed = false, wornChanged = false;
                for (int i = 0; i < 4; i++)
                {
                    string want = Override(npc, i);
                    int quality;
                    if (want != null)
                    {
                        if (!own.Taken[i])                          // first override of this slot: remember her own piece
                        {
                            own.Taken[i] = true;
                            own.Gear[i] = next.Gear[i] ?? "";
                            own.Quality[i] = next.Quality[i];
                            // read BEFORE the note is overwritten below (review A): is that "own" piece a copy the record noted?
                            own.Copy[i] = RecordedPick(worn, next, i);
                        }
                        wornChanged |= SetWorn(worn, i, want);
                        // The quality has to travel with the name: ScLooks.SyncGear builds the piece with MakeItem(name,
                        // Quality[slot]), so leaving Quality behind would dress her in a Q1 copy of the Q6 helmet she was
                        // handed. Her real one is in her window or bag, the same place 0-XNPCCore's PackValueFor looks.
                        quality = QualityOf(npc, want, next.Quality[i]);
                    }
                    else
                    {
                        if (!own.Taken[i]) continue;                // never overridden: SC's own piece stands as it is
                        want = own.Gear[i];                         // override cleared: back to the piece she had
                        quality = own.Quality[i];
                        own.Taken[i] = false;
                        // her own piece again - noted as a Wardrobe copy when that is what the snapshot held (review A), else real
                        wornChanged |= SetWorn(worn, i, own.Copy[i]);
                        own.Copy[i] = null;
                    }
                    if (string.Equals(next.Gear[i] ?? "", want ?? "", StringComparison.Ordinal) && next.Quality[i] == quality) continue;
                    next.Gear[i] = want ?? "";
                    next.Quality[i] = quality;
                    changed = true;
                }
                if (!changed)
                {
                    if (wornChanged && ScRegistry.Loaded) ScRegistry.SaveAtomic();
                    return false;
                }

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
                          + string.Join(", ", next.Gear) + "]" + (records == 0 ? " (no record of SC's: kept with the save while the world saves the body, round lk1)" : ""));
                return true;
            }
            catch (Exception ex) { ScLog.Once("wardrobe-honor", ex); return false; }
        }
    }
}
#endif
