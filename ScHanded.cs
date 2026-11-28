using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round n12 (3.4): the EXACT items a member was handed - the weapon of each half of her pair and each of her four armor
    /// pieces as the player gave them: type, quality, wear (UseTimes), seed, attachment mods, cosmetic mods and metadata.
    ///
    /// Why it exists. A member's weapons and clothes are not items: the pair is two item names (ScLoadout) whose copies are
    /// rebuilt from the name all the time (SCore's toolbelt fill and UpdateWeapon(string) roll a random quality, the canonical
    /// mods are SC's own roll), and her armor is a look (Look.Gear + Look.Quality) worn as fresh items (ScLooks.MakeItem). So a
    /// Q6 gun with a scope handed over came back Q2 without it. Here the handed item itself is kept in her record, one per slot
    /// (Member.Handed), and:
    ///  - every copy of that weapon she gets carries it (Imprint: the toolbelt refill, the hand, every 0-XNPCCore re-equip -
    ///    ScLoadout.FillMods, reached from ApplyWeaponMods and X16's ModsAfterEquip), and the piece she wears is that item
    ///    (ScLooks.WornItem);
    ///  - taking it back (the member inventory panel's slots, the weapon menu's swap) returns exactly it.
    /// A weapon or piece she was never handed keeps the old behavior: canonical mods, a random quality, and her current copy
    /// (a new piece of that name and quality for armor) when it is taken.
    ///
    /// Valid or stale. A stored weapon counts while the pair still names its item (Weapon); a stored piece while the look
    /// still names it at its quality (Armor). Anything else that changes the pair or the look (sc loadout, sc gear, the
    /// Wardrobe) leaves the stored item stale but KEEPS it - it is a real item - and it is in force again if the pair or look
    /// comes back to it (the Wardrobe's "back to my own"). A stale item is never handed out by a copy; the next panel change
    /// of its slot moves it into her pack (ScMemberEquip), and her death drops it with her window (TakeForDeath).
    ///
    /// Stored clean: a ranged weapon unloaded (Meta 0) and without Locked &amp; Loaded's seated-magazine metadata (its rounds and
    /// magazine went into her bag when it was handed: ScArms). Saved in the registry as one line per slot,
    /// handed=&lt;slot&gt;|&lt;item name&gt;|&lt;ItemValue.Write's bytes in ScRunners.Pack's form&gt; (the registry's key=value form, no '='
    /// or ';'), read back with the name checked: within a save the item ids are the world's own mapping (ItemClass.AssignIds runs
    /// before players.xml, so before the registry, loads), and an item that is gone or renamed is dropped with one warning. A
    /// save from before n12 has no such line (nothing stored: today's behavior); an older build ignores the key and drops it at
    /// its next save. Host only: a multiplayer client has no registry (its panel only shows what it has, see ScMemberGear).
    /// Round mt1 (part G): a client's panel shows the server's exact items through the server's gear view (member.gearview,
    /// ScMemberGear.GearView); the handed lines are still never mirrored.
    /// </summary>
    public static class ScHanded
    {
        public const int Ranged = 0, Melee = 1, Head = 2, Chest = 3, Hands = 4, Feet = 5, Count = 6;
        private static readonly string[] Keys = { "ranged", "melee", "head", "chest", "hands", "feet" };

        /// <summary>Locked &amp; Loaded's metadata on a gun about the magazine seated in it (Magazines.SeatedKey, SeatedNameKey, GunOtherKey).</summary>
        internal static readonly string[] GunMagazineKeys = { "reinvMagSeated", "REMagsSeatedName", "REMagsSeatedOther" };

        public static int WeaponSlot(bool ranged) { return ranged ? Ranged : Melee; }

        /// <summary>The store slot of an equipment slot (Head 0 .. Feet 3).</summary>
        public static int ArmorSlot(int equipSlot) { return Head + equipSlot; }

        public static string KeyOf(int slot) { return slot >= 0 && slot < Count ? Keys[slot] : "?"; }

        // ------------------------------------------------------------------ the store ----

        /// <summary>The stored item of this slot (a stale one included), or null. The record's own object: callers clone.</summary>
        public static ItemValue Get(Member m, int slot)
        {
            if (m == null || m.Handed == null || slot < 0 || slot >= Count) return null;
            ItemValue iv = m.Handed[slot];
            return iv != null && !iv.IsEmpty() ? iv : null;
        }

        /// <summary>Stores a copy of this item in the slot (null: the slot holds nothing). Not saved here: the caller saves.</summary>
        public static void Put(Member m, int slot, ItemValue iv)
        {
            if (m == null || slot < 0 || slot >= Count) return;
            if (iv == null || iv.IsEmpty())
            {
                if (m.Handed == null) return;
                m.Handed[slot] = null;
                bool any = false;
                for (int i = 0; i < Count; i++) if (m.Handed[i] != null) any = true;
                if (!any) m.Handed = null;
                return;
            }
            if (m.Handed == null) m.Handed = new ItemValue[Count];
            m.Handed[slot] = iv.Clone();
        }

        /// <summary>
        /// Round wq1: the weapon now stored in a pair slot is the quality she has for it from now on - should this record go stale
        /// or be taken back, the copies SCore rebuilds keep that quality (ScLoadout.StableQuality), never the seeded one. Round
        /// wq1 (review, WQ1-n1): called by ScArms only once the swap is committed (SetGuarded succeeded), so a refused swap
        /// leaves no record; never while the save is read (an old save gains no wq= line).
        /// </summary>
        public static void RecordWeaponQuality(Member m, int slot)
        {
            if (slot != Ranged && slot != Melee) return;
            ItemValue iv = Get(m, slot);
            if (iv != null && iv.ItemClass != null && iv.ItemClass.HasQuality) ScLoadout.RecordQuality(m, iv.ItemClass.GetItemName(), iv.Quality);
        }

        public static bool Any(Member m)
        {
            if (m == null) return false;
            if ((m.HandedSpare != null && m.HandedSpare.Count > 0) || (m.HandedRaw != null && m.HandedRaw.Count > 0)) return true;
            if (m.Handed == null) return false;
            foreach (ItemValue iv in m.Handed) if (iv != null && !iv.IsEmpty()) return true;
            return false;
        }

        /// <summary>
        /// The item as it is kept: a copy, without Locked &amp; Loaded's seated-magazine metadata, and for a ranged weapon
        /// (unload) with no rounds in it - the caller has put them, or its magazine, into her bag. Null for nothing.
        /// </summary>
        public static ItemValue Clean(ItemValue iv, bool unload)
        {
            if (iv == null || iv.IsEmpty()) return null;
            ItemValue c = iv.Clone();
            if (unload) c.Meta = 0;
            if (c.Metadata != null)
            {
                foreach (string k in GunMagazineKeys) c.RemoveMetaData(k);
                if (c.Metadata.Count == 0) c.Metadata = null;      // vanilla's comparisons count an empty table and none as different
            }
            return c;
        }

        /// <summary>The record this body's copies belong to: its own member record, or the member a radio stand-in stands for.</summary>
        public static Member MemberOf(EntityAlive e)
        {
            if (e == null || !ScRegistry.Loaded) return null;
            Member m = ScRegistry.GetMember(e.entityId);
            if (m == null && e.Buffs != null && e.Buffs.HasCustomVar(ScRadio.StandInCvar)) m = ScRegistry.GetMember((int)e.Buffs.GetCustomVar(ScRadio.StandInCvar));
            return m;
        }

        public static string NameOf(ItemValue iv)
        {
            ItemClass ic = iv != null && !iv.IsEmpty() ? iv.ItemClass : null;
            return ic != null ? ic.GetItemName() : null;
        }

        /// <summary>The quality a look keeps for this piece (Look.Quality is 1..6).</summary>
        public static int LookQuality(ItemValue iv)
        {
            return iv == null ? 1 : Math.Max(1, Math.Min(6, (int)iv.Quality));
        }

        // ------------------------------------------------------------------ valid items ----

        /// <summary>The stored weapon of this half of the pair while the pair still names its item, else null.</summary>
        public static ItemValue Weapon(Member m, Loadout pair, bool ranged)
        {
            ItemValue iv = Get(m, WeaponSlot(ranged));
            if (iv == null || pair == null) return null;
            string token = pair.Of(ranged);
            return token != null && string.Equals(NameOf(iv), token, StringComparison.OrdinalIgnoreCase) ? iv : null;
        }

        /// <summary>The valid stored weapon whose copies have this type (ScLoadout.FillMods), or null.</summary>
        public static ItemValue ForCopy(EntityAlive e, Loadout pair, int type)
        {
            Member m = MemberOf(e);
            if (m == null || m.Handed == null || pair == null) return null;
            for (int k = 0; k < 2; k++)
            {
                ItemValue iv = Weapon(m, pair, k == 0);
                if (iv != null && iv.type == type) return iv;
            }
            return null;
        }

        /// <summary>The stored piece of this equipment slot while it is the one this item name and look quality describe, else null.</summary>
        public static ItemValue Piece(Member m, int equipSlot, string name, int quality)
        {
            if (equipSlot < 0 || equipSlot > 3 || string.IsNullOrEmpty(name)) return null;
            ItemValue iv = Get(m, ArmorSlot(equipSlot));
            if (iv == null || !string.Equals(NameOf(iv), name, StringComparison.OrdinalIgnoreCase)) return null;
            ItemClass ic = iv.ItemClass;
            return ic == null || !ic.HasQuality || LookQuality(iv) == Math.Max(1, Math.Min(6, quality)) ? iv : null;
        }

        /// <summary>The stored piece of this equipment slot while her look wears it, else null.</summary>
        public static ItemValue Armor(Member m, Look look, int equipSlot)
        {
            if (look == null || equipSlot < 0 || equipSlot > 3) return null;
            return Piece(m, equipSlot, look.Gear[equipSlot], look.Quality[equipSlot]);
        }

        // ------------------------------------------------------------------ copies ----

        /// <summary>
        /// Makes a copy of a handed item carry its identity: quality, wear, seed, mods, cosmetic mods and (round mt1) its rolled
        /// stats - never its Meta (a gun's rounds) or its metadata (Locked &amp; Loaded's magazine state, which belongs to the
        /// copy). True when what a model is built from changed (quality, seed, mods, stats: ItemInventoryData.NeedsRebuild
        /// ignores only wear and Meta), so a held copy needs its model rebuilt.
        /// </summary>
        public static bool Imprint(ItemValue copy, ItemValue exact)
        {
            if (copy == null || exact == null || copy.IsEmpty() || copy.type != exact.type) return false;
            bool rebuild = false;
            if (copy.Quality != exact.Quality) { copy.Quality = exact.Quality; rebuild = true; }
            if (copy.Seed != exact.Seed) { copy.Seed = exact.Seed; rebuild = true; }
            if (!ItemValue.Equals(copy.modifications, exact.modifications)) { copy.modifications = exact.modifications.CloneItemValueArray(); rebuild = true; }
            if (!ItemValue.Equals(copy.cosmeticMods, exact.cosmeticMods)) { copy.cosmeticMods = exact.cosmeticMods.CloneItemValueArray(); rebuild = true; }
            // round mt1 (part G, F1): 3.4's per-item stat rolls (ItemValue.Stats: Base_Random_Roll and its boosts, which scale
            // damage, attack speed, stamina and durability) - a copy built from the item's name has none, so a handed gun
            // fought without its rolled bonuses and the panel's numbers changed with the copy on show. Stat is a struct: the
            // array copy is deep.
            if (!ItemValue.Equals(copy.Stats, exact.Stats)) { copy.Stats = CloneStats(exact.Stats); rebuild = true; }
            if (copy.UseTimes != exact.UseTimes) copy.UseTimes = exact.UseTimes;
            return rebuild;
        }

        /// <summary>Round mt1 (part G, F1): a copy of an item's rolled stats (null stays null).</summary>
        private static ItemValue.Stat[] CloneStats(ItemValue.Stat[] stats)
        {
            if (stats == null) return null;
            var c = new ItemValue.Stat[stats.Length];
            Array.Copy(stats, c, stats.Length);
            return c;
        }

        /// <summary>
        /// The relink tick (ScTicker, once a second per loaded member): while she carries a handed weapon, her pair's copies are
        /// checked against it (ScLoadout.ApplyWeaponMods, a no-op when they match), so a copy made by a path that passes neither
        /// ScLoadout nor X16 - SCore's own by-name swaps, ScLoadout.Arm's clone on a body re-created before her record was
        /// re-keyed - carries the handed item within a second. Not during a medical use (the hand holds the item).
        /// </summary>
        public static void KeepCopies(Member m, EntityAlive npc)
        {
            if (m == null || npc == null || !ScXnpc.IsBody(npc)) return;
            if (m.HandedSpare != null && m.HandedSpare.Count > 0) DeliverSpares(m, npc);
            if (m.Handed == null || (Get(m, Ranged) == null && Get(m, Melee) == null)) return;
            if (ScMedic.IsUsing(npc.entityId)) return;
            ScLoadout.ApplyWeaponMods(npc, true);
        }

        /// <summary>
        /// The review's fix 10: handed items that lost their slot (HandedSpare) go into her pack - what does not fit drops at her
        /// feet - once her body is loaded and nobody holds her inventory (a holder's copy written back would undo it; the host's
        /// own panel counts too, which only delays this to its close).
        /// </summary>
        private static void DeliverSpares(Member m, EntityAlive npc)
        {
            if (!ScCommunity.IsHost || npc.IsDead() || !ScNpc.UsesWindow(npc) || ScAmmo.IsWindowOpen(npc.entityId)) return;
            SCoreLootContainer pack = HarvestManager.GetOrCreate(npc.entityId);
            var names = new List<string>();
            // one at a time, each off the list once it is delivered (re-check C): a throw on the next one (PutInPack's drop at
            // her feet) leaves only the undelivered ones for the next tick - for a single item a throw there means it went
            // neither into the pack nor onto the ground
            while (m.HandedSpare != null && m.HandedSpare.Count > 0)
            {
                ItemValue iv = m.HandedSpare[0];
                if (iv != null && !iv.IsEmpty())
                {
                    try { ScMemberEquip.PutInPack(npc, pack, new ItemStack(iv.Clone(), 1), "a handed item that had lost its slot"); }
                    catch (Exception e) { ScLog.Once("handed-spare-deliver", e); break; }
                    names.Add(NameOf(iv) ?? "?");
                }
                m.HandedSpare.RemoveAt(0);
            }
            if (m.HandedSpare != null && m.HandedSpare.Count == 0) m.HandedSpare = null;
            if (names.Count == 0) return;
            ScLog.Out(m.Name + ": " + names.Count + " handed item(s) whose slot had been filled again while their item was missing went into " + ScMedic.Her(m) + " pack: "
                      + string.Join(", ", names.ToArray()));
            if (ScRegistry.Loaded) ScRegistry.SaveAtomic();
        }

        /// <summary>The same item as far as a model and its stats go: type, quality, seed, mods, cosmetic mods and (round mt1) rolled stats (wear and Meta aside).</summary>
        public static bool SameBuild(ItemValue a, ItemValue b)
        {
            if (a == null || b == null || a.IsEmpty() || b.IsEmpty()) return false;
            return a.type == b.type && a.Quality == b.Quality && a.Seed == b.Seed
                   && ItemValue.Equals(a.modifications, b.modifications) && ItemValue.Equals(a.cosmeticMods, b.cosmeticMods)
                   && ItemValue.Equals(a.Stats, b.Stats);       // round mt1 (part G, F1)
        }

        /// <summary>A short key of an item's build (ScMemberGear.ArmorKey: a changed stored piece shows at once).</summary>
        public static string Fingerprint(ItemValue iv)
        {
            if (iv == null || iv.IsEmpty()) return "-";
            var sb = new StringBuilder();
            sb.Append(iv.type).Append('.').Append(iv.Quality).Append('.').Append(iv.Seed).Append('.').Append(iv.UseTimes.ToString("0.#", CultureInfo.InvariantCulture));
            for (int i = 0; i < iv.ModificationCount; i++) { ItemValue mod = iv.GetModification(i); sb.Append('.').Append(mod != null ? mod.type : 0); }
            for (int i = 0; i < iv.CosmeticModCount; i++) { ItemValue mod = iv.GetCosmeticMod(i); sb.Append('c').Append(mod != null ? mod.type : 0); }
            // round mt1 (part G, F1): the rolled stats too - the same piece handed again with other rolls shows at once
            if (iv.Stats != null)
                foreach (ItemValue.Stat s in iv.Stats) sb.Append('s').Append((int)s.type).Append(s.isBoosted ? 'b' : ':').Append(s.value);
            return sb.ToString();
        }

        // ------------------------------------------------------------------ her death ----

        /// <summary>
        /// Patches_Npc's death drop: every item she holds here - valid or stale - goes into the death backpack's list, and out of
        /// her record at once, so a repeated SetDead finds none. Her pair's copies and her look are not items and never drop
        /// (round M10-D6 drops a raider's pair only), so without this a handed gun or helmet was lost with her.
        /// </summary>
        public static int TakeForDeath(EntityAlive e, List<ItemStack> drop)
        {
            Member m = e != null && ScRegistry.Loaded ? ScRegistry.GetMember(e.entityId) : null;     // never a stand-in: the member lives on
            if (m == null || drop == null || (m.Handed == null && m.HandedSpare == null && m.HandedRaw == null)) return 0;
            var names = new List<string>();
            foreach (ItemValue iv in All(m))
            {
                drop.Add(new ItemStack(iv.Clone(), 1));
                names.Add(NameOf(iv) ?? "?");
            }
            int raw = m.HandedRaw != null ? m.HandedRaw.Count : 0;
            m.Handed = null;
            m.HandedSpare = null;
            m.HandedRaw = null;
            // round k2: the member's own pronouns (ScMedic's rule: her look, else her class) - "Paul died carrying ... she was handed"
            if (names.Count > 0) ScLog.Out(m.Name + " died carrying " + names.Count + " item(s) " + ScMedic.She(m) + " was handed - they drop with " + ScMedic.Her(m) + " backpack: " + string.Join(", ", names.ToArray()));
            if (raw > 0) ScLog.Warn(m.Name + " died with " + raw + " handed item(s) whose item is missing from the game (a mod not loaded) - they cannot be made and are lost");
            if (names.Count + raw > 0 && ScRegistry.Loaded) ScRegistry.SaveAtomic();
            return names.Count;
        }

        /// <summary>Every handed item of the record: the six slots (valid or stale) and the spares.</summary>
        private static List<ItemValue> All(Member m)
        {
            var list = new List<ItemValue>();
            if (m == null) return list;
            for (int i = 0; i < Count; i++) { ItemValue iv = Get(m, i); if (iv != null) list.Add(iv); }
            if (m.HandedSpare != null) foreach (ItemValue iv in m.HandedSpare) if (iv != null && !iv.IsEmpty()) list.Add(iv);
            return list;
        }

        /// <summary>
        /// The review's fix 4: her record goes some other way than a death in the world (TakeForDeath) - she leaves the community
        /// (ScCommunity.Leave), dies off-world (ScCommunity.RecordDeath: a run, a garrison, a defence, a radio distress, an
        /// offline runner), is purged as missing, or her id turned out to be reused (ScTicker). With her body there (leaving),
        /// her handed items go into her pack (dropped at her feet when it is full, or while a remote player holds her
        /// inventory); without one they are lost - and always logged, as a lost window is. Clears them from the record.
        /// </summary>
        public static void OnRecordGone(Member m, EntityAlive body, string why)
        {
            if (m == null || (m.Handed == null && m.HandedSpare == null && m.HandedRaw == null)) return;
            try
            {
                List<ItemValue> items = All(m);
                int raw = m.HandedRaw != null ? m.HandedRaw.Count : 0;
                var names = new List<string>();
                foreach (ItemValue iv in items) names.Add(ItemText(iv));
                bool alive = body != null && !body.IsDead() && ScCommunity.IsHost && ScCommunity.World != null
                             && ReferenceEquals(ScCommunity.World.GetEntity(body.entityId), body);
                if (alive && items.Count > 0)
                {
                    SCoreLootContainer pack = ScNpc.UsesWindow(body) && !ScAmmo.IsRemoteOpen(body.entityId) ? HarvestManager.GetOrCreate(body.entityId) : null;
                    foreach (ItemValue iv in items) ScMemberEquip.PutInPack(body, pack, new ItemStack(iv.Clone(), 1), "a handed item");
                    ScLog.Out(m.Name + " (" + why + "): the " + items.Count + " item(s) " + ScMedic.She(m) + " was handed went into " + ScMedic.Her(m) + " pack: " + string.Join("; ", names.ToArray()));
                }
                else if (items.Count > 0)
                    ScLog.Warn(m.Name + " (" + why + ") had " + items.Count + " item(s) " + ScMedic.She(m) + " was handed - lost with " + ScMedic.HerObj(m) + ", there is no body to leave them on: "
                               + string.Join("; ", names.ToArray()));
                if (raw > 0)
                    ScLog.Warn(m.Name + " (" + why + ") had " + raw + " more handed item(s) whose item is missing from the game (a mod not loaded) - lost");
            }
            catch (Exception e) { ScLog.Once("handed-record-gone", e); }
            m.Handed = null;
            m.HandedSpare = null;
            m.HandedRaw = null;
        }

        // ------------------------------------------------------------------ the registry ----

        /// <summary>The registry lines of a member's stored items: "&lt;slot&gt;|&lt;item&gt;|&lt;packed bytes&gt;" each (without "handed=").</summary>
        public static List<string> Lines(Member m)
        {
            var lines = new List<string>();
            if (m == null) return lines;
            int spares = m.HandedSpare != null ? m.HandedSpare.Count : 0;
            for (int i = 0; i < Count + spares; i++)
            {
                ItemValue iv = i < Count ? Get(m, i) : m.HandedSpare[i - Count];
                string key = i < Count ? Keys[i] : SpareKey;
                string name = NameOf(iv);
                if (name == null) continue;
                try
                {
                    byte[] bytes;
                    using (var ms = new MemoryStream())
                    {
                        using (PooledBinaryWriter w = MemoryPools.poolBinaryWriter.AllocSync(false))
                        {
                            w.SetBaseStream(ms);
                            iv.Write(w);
                            w.Flush();
                        }
                        bytes = ms.ToArray();
                    }
                    lines.Add(key + "|" + name + "|" + ScRunners.Pack(bytes));
                }
                catch (Exception e)
                {
                    ScLog.Once("handed-write-" + name, "handed items: " + m.Name + "'s " + name + " (" + key + ") could not be written (" + e.GetType().Name + ": " + e.Message + ") - not saved");
                }
            }
            if (m.HandedRaw != null) lines.AddRange(m.HandedRaw);     // the review's fix 10: back unchanged, for when the item exists again
            return lines;
        }

        /// <summary>The registry key of a handed item whose slot was taken (HandedSpare).</summary>
        private const string SpareKey = "spare";

        /// <summary>
        /// One registry line back into the record. A line that does not read is dropped with a warning. One whose item does not
        /// exist in this game (the review's fix 10: a mod not loaded this session) is KEPT verbatim (HandedRaw) and written back
        /// unchanged, never acted on, so the item is there again with the mod. A spare line, or a second line for a slot that
        /// is filled already, becomes a spare (HandedSpare: into her pack when her body is next loaded).
        /// </summary>
        public static void Read(Member m, string val)
        {
            if (m == null || string.IsNullOrEmpty(val)) return;
            string[] p = val.Split(new[] { '|' }, 3);
            string key = p.Length == 3 ? p[0].Trim().ToLowerInvariant() : null;
            int slot = key != null ? Array.IndexOf(Keys, key) : -1;
            bool spare = key == SpareKey;
            if (slot < 0 && !spare) { ScLog.Warn("handed items: an unreadable line for " + m.Name + " is dropped ('" + Short(val) + "')"); return; }
            byte[] bytes = ScRunners.Unpack(p[2].Trim());
            ItemValue iv = null;
            try
            {
                if (bytes != null)
                {
                    using (var ms = new MemoryStream(bytes))
                    using (PooledBinaryReader r = MemoryPools.poolBinaryReader.AllocSync(false))
                    {
                        r.SetBaseStream(ms);
                        iv = new ItemValue();
                        iv.Read(r);
                    }
                }
            }
            catch (Exception e)
            {
                ScLog.Warn("handed items: " + m.Name + "'s " + p[1] + " (" + p[0] + ") could not be read (" + e.GetType().Name + ") - dropped");
                return;
            }
            string name = p[1].Trim();
            if (iv == null)
            {
                ScLog.Warn("handed items: " + m.Name + "'s " + name + " (" + p[0] + ") could not be read - dropped from the record");
                return;
            }
            if (iv.IsEmpty() || !string.Equals(NameOf(iv), name, StringComparison.OrdinalIgnoreCase))
            {
                if (m.HandedRaw == null) m.HandedRaw = new List<string>();
                m.HandedRaw.Add(val);
                ScLog.Warn("handed items: " + m.Name + "'s " + name + " (" + p[0] + ") is not an item in this game now (a mod not loaded?) - kept in the record as it is, "
                           + "and back once the item exists again");
                return;
            }
            DropMissingMods(iv.modifications);
            DropMissingMods(iv.cosmeticMods);
            if (spare || Get(m, slot) != null)
            {
                if (m.HandedSpare == null) m.HandedSpare = new List<ItemValue>();
                m.HandedSpare.Add(iv);
                return;
            }
            Put(m, slot, iv);
        }

        /// <summary>A mod whose item no longer exists becomes an empty slot (the weapon itself keeps everything else).</summary>
        private static void DropMissingMods(ItemValue[] mods)
        {
            if (mods == null) return;
            for (int i = 0; i < mods.Length; i++)
                if (mods[i] != null && !mods[i].IsEmpty() && mods[i].ItemClass == null) mods[i] = ItemValue.None;
        }

        private static string Short(string s) { return s.Length > 60 ? s.Substring(0, 60) + "..." : s; }

        // ------------------------------------------------------------------ diagnostics ----

        /// <summary>"gunX Q6, wear 12/300, stats 3, mods [modA, modB], cosmetic [dye]" (round mt1: "stats n", her rolled stats, when it has any).</summary>
        public static string ItemText(ItemValue iv)
        {
            if (iv == null || iv.IsEmpty()) return "-";
            var sb = new StringBuilder(NameOf(iv) ?? ("type " + iv.type));
            if (iv.HasQuality) sb.Append(" Q").Append(iv.Quality);
            if (iv.MaxUseTimes > 0) sb.Append(", wear ").Append(iv.UseTimes.ToString("0.#", CultureInfo.InvariantCulture)).Append('/').Append(iv.MaxUseTimes);
            if (iv.Stats != null && iv.Stats.Length > 0) sb.Append(", stats ").Append(iv.Stats.Length);     // round mt1 (part G, F1)
            sb.Append(", mods ").Append(ModList(iv.modifications));
            if (iv.CosmeticModCount > 0) sb.Append(", cosmetic ").Append(ModList(iv.cosmeticMods));
            if (iv.Metadata != null && iv.Metadata.Count > 0) sb.Append(", metadata ").Append(string.Join(",", new List<string>(iv.Metadata.Keys).ToArray()));
            return sb.ToString();
        }

        private static string ModList(ItemValue[] mods)
        {
            if (mods == null || mods.Length == 0) return "(no slots)";
            var names = new List<string>();
            foreach (ItemValue mod in mods) if (mod != null && !mod.IsEmpty()) names.Add(NameOf(mod) ?? ("type " + mod.type));
            return "[" + string.Join(", ", names.ToArray()) + "] of " + mods.Length;
        }

        /// <summary>sc loadout: every stored item of the member, and whether it is in force (her pair or look still names it).</summary>
        public static string Describe(Member m, EntityAlive e)
        {
            if (m == null) return "handed items: - (not a member: nothing is kept)";
            if (!Any(m)) return "handed items: none (every weapon and piece is rolled: canonical mods, a random quality on each copy)";
            Loadout pair = e != null ? ScLoadout.Current(e) : m.Loadout;
            Look look = e != null ? ScLooks.Get(e) : m.Look;
            var sb = new StringBuilder("handed items (kept exactly; a copy carries them):");
            for (int i = 0; i < Count; i++)
            {
                ItemValue iv = Get(m, i);
                if (iv == null) continue;
                bool valid = i <= Melee ? Weapon(m, pair, i == Ranged) != null : Armor(m, look, i - Head) != null;
                sb.Append("\n[SC]      ").Append(Keys[i]).Append(": ").Append(ItemText(iv))
                  .Append(valid ? "" : i <= Melee ? "  (STALE: her pair names another weapon - goes to her pack on the next change of this slot)"
                                                  : "  (STALE: her look wears another piece - goes to her pack on the next change of this slot)");
            }
            if (m.HandedSpare != null)
                foreach (ItemValue iv in m.HandedSpare) sb.Append("\n[SC]      spare: ").Append(ItemText(iv)).Append("  (goes into her pack when her body is next loaded)");
            if (m.HandedRaw != null && m.HandedRaw.Count > 0)
                sb.Append("\n[SC]      ").Append(m.HandedRaw.Count).Append(" more whose item is missing from the game (a mod not loaded) - kept as they are");
            return sb.ToString();
        }
    }
}
