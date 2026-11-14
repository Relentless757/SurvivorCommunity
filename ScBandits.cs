#if SC_V34
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using HarmonyLib;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round R1 (3.4 build; research DESIGN_R1_BANDITS.md, the user's answers: every recommendation). With 0-XNPCCore
    /// 3.4.02's bandits loaded, SC's six raider classes are thin children of them (Config/entityclasses.xml, Tools/
    /// gen_arsenal.py bandit_block; ScXnpc.IsBanditRaider: the BanditMarker class property). Who does what for such a
    /// raider:
    ///  - SC: the body, face, clothes, Vanity Vault paints and tattoos (X1, unchanged), the name (the class's Names, or a
    ///    record's), the Shooting stars (X18/X19 skip 0-XNPCCore's aim), the healing (ScMedic), spawning, bands, dens,
    ///    raids, taunts and markers.
    ///  - 0-XNPCCore: the weapon. Its look trigger (the SDCS Look group the bandits carry) runs its look applier for the
    ///    raider on every world entry; X1c (Patch_GearOnly, below) re-routes that one call to the applier's own gear-only
    ///    half, SdcsLookApplier.ApplyGearOnly - the weapon section alone: on the first entry it rolls an entry of the
    ///    class's SdcsWeapons list (1 + Mix(entity id, 31806) % length, kept in the SdcsWeapon cvar, saved with the
    ///    raider), grants SdcsSpawnAmmoCount (60) rounds of that gun's ammo into the inventory window and puts one copy of the
    ///    weapon in the bag; on every entry it puts the weapon in the hand (SCore's UpdateWeapon). The name roll, the
    ///    archetype and the armour set never run. Then its close-range swap to the kit melee, the dry-gun failover, the
    ///    world-entry re-arm, the bandit fight (SdcsBanditCombat), the player-damage setting and the loot bag at death.
    ///  - SC around it: X1c moves the granted rounds from the window into the bag (where a bow's arrows must be, ScAmmo
    ///    .AddRounds) and drops the then-empty window, so a raider stays window-less (ScNpc.Items); grants the ammo of a gun
    ///    0-XNPCCore's table does not know (IZY Classic, Q7); remembers the rolled weapon by hash (RolledHashCvar), so a
    ///    list that changed under a save is re-indexed at the next load (FixIndex); pre-marks 0-XNPCCore's trade stock and
    ///    water as seeded (Q8: no stock window); and, for a body made with a known weapon (a den's or band's person
    ///    re-made, Q6, or the console), sets the index itself and seeds the weapon and the ammo 0-XNPCCore does not grant
    ///    for an index it did not roll (SeedPending).
    /// The old raider code (ScLoadout's pair and lead, the backpack, the overwhelmed rule) stays for the 3.2 build and a
    /// 3.4 game whose 0-XNPCCore has no bandits; each piece stands aside for a bandit raider.
    /// </summary>
    public static class ScBandits
    {
        /// <summary>0-XNPCCore's index of the rolled entry in the class's SdcsWeapons list (1-based; out of range = roll).</summary>
        public const string WeaponCvar = "SdcsWeapon";
        /// <summary>The rolled weapon as SC's item hash (ScLoadout.Hash), the guard for a list that changed under a save.</summary>
        public const string RolledHashCvar = "sc_rWeapon";
        /// <summary>SC set the index itself (a known weapon): AfterPostInit seeds the weapon and its ammo once (value 1).</summary>
        public const string SeedCvar = "sc_rSeed";
        /// <summary>A dead bandit raider's bag was discarded (the death is counted once; SetDead runs again on every hit).</summary>
        public const string DiscardedCvar = "sc_rDiscarded";
        /// <summary>
        /// 0-XNPCCore's once-per-NPC marks for its trade stock and boiled water (SdcsStockSeedFrame, SdcsWaterSeedFrame) and,
        /// since 0-XNPCCore 3.4.03 (round X2), its thrown explosives (SdcsThrowSeedFrame: the class's SdcsThrowables, which
        /// SC's raiders inherit by sex - 2 pipe bombs on every female). SC rolls a raider's throwable itself (RollThrowable).
        /// </summary>
        private static readonly string[] SeededMarks = { "SdcsStockSeeded", "SdcsWaterSeeded", "SdcsThrowSeeded" };
        /// <summary>X2: SC rolled this raider's thrown explosive (value 1), once per body.</summary>
        public const string ThrowCvar = "sc_rThrow";
        /// <summary>The old raiders' cvars (the pair, the lead, the starting ammo, the pair's drop roll), taken off once.</summary>
        private static readonly string[] OldRaiderCvars = { ScLoadout.RangedCvar, ScLoadout.MeleeCvar, ScLoadout.LeadCvar, ScLoadout.AmmoSeededCvar, "sc_pairDropped" };

        /// <summary>Class id -> its parsed SdcsWeapons list (0-XNPCCore's ParseWeapons: comma split, trimmed, empties dropped, duplicates kept).</summary>
        private static readonly Dictionary<int, string[]> lists = new Dictionary<int, string[]>();

        public static void Reset()
        {
            lock (lists) lists.Clear();
            refused.Clear();
            warnedThrow.Clear();
        }

        // ------------------------------------------------------------------ the class's list ----

        /// <summary>The class's SdcsWeapons list as 0-XNPCCore reads it (empty when the class has none).</summary>
        public static string[] List(int classId)
        {
            lock (lists)
            {
                string[] l;
                if (lists.TryGetValue(classId, out l)) return l;
                EntityClass ec;
                string raw = null;
                if (EntityClass.list != null && EntityClass.list.TryGetValue(classId, out ec) && ec != null && ec.Properties != null)
                    ec.Properties.Values.TryGetValue("SdcsWeapons", out raw);
                else return new string[0];          // not loaded yet: not cached
                l = Parse(raw);
                lists[classId] = l;
                return l;
            }
        }

        /// <summary>LookRollRules.ParsePool, which WeaponRollRules.ParseWeapons is (0-XNPCCore 3.4.02 IL).</summary>
        private static string[] Parse(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return new string[0];
            var l = new List<string>();
            foreach (string p in raw.Split(','))
            {
                string t = p.Trim();
                if (t.Length > 0) l.Add(t);
            }
            return l.ToArray();
        }

        /// <summary>The 1-based index of the first entry naming this item, or 0.</summary>
        public static int IndexOf(int classId, string item)
        {
            if (string.IsNullOrEmpty(item)) return 0;
            string[] l = List(classId);
            for (int i = 0; i < l.Length; i++) if (string.Equals(l[i], item, StringComparison.OrdinalIgnoreCase)) return i + 1;
            return 0;
        }

        /// <summary>0-XNPCCore's reading of the index cvar (SdcsLookApplier.ReadCvar: rounded).</summary>
        public static int Index(EntityAlive e)
        {
            return e != null && e.Buffs != null ? (int)Math.Round(e.Buffs.GetCustomVar(WeaponCvar)) : 0;
        }

        /// <summary>
        /// The raider's weapon: the entry its index names, else - before 0-XNPCCore has rolled, on the machine that rolls -
        /// the entry it will roll (PickWeaponIndex with the same seed, the entity id), so a record taken right after the
        /// spawn (ScEnclavePeople.Capture, ScCommunity.MemberFromBody) already names it. Null when unknown.
        /// </summary>
        public static string Weapon(EntityAlive e, out bool predicted)
        {
            predicted = false;
            if (e == null) return null;
            string[] l = List(e.entityClass);
            if (l.Length == 0) return null;
            int i = Index(e);
            if (i >= 1 && i <= l.Length) return l[i - 1];
            if (e.isEntityRemote) return null;
            int p = Predict(e.entityId, l.Length);
            if (p < 1 || p > l.Length) return null;
            predicted = true;
            return l[p - 1];
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int PickIndex(int seed, int count) { return NPCSdcsCharacters.Pure.WeaponRollRules.PickWeaponIndex(seed, count); }

        /// <summary>0-XNPCCore's own roll (public in its Pure rules), or -1 when it cannot be called.</summary>
        private static int Predict(int seed, int count)
        {
            try { return PickIndex(seed, count); }
            catch (Exception ex) { ScLog.Once("bandit-predict", ex); return -1; }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string NpcAmmoFor(string weapon) { return NPCSdcsCharacters.Pure.WeaponRollRules.AmmoForWeapon(weapon); }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool NpcUnmapped(string weapon) { return NPCSdcsCharacters.Pure.WeaponRollRules.AmmoUnmapped(weapon); }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int NpcSpawnCount(EntityClass ec, out bool grant)
        {
            DynamicProperties p = ec.Properties;
            grant = NPCSdcsCharacters.Pure.WeaponRollRules.ResolveSpawnAmmoGrant(p.Contains("SdcsSpawnAmmo"), p.Contains("SdcsSpawnAmmo") ? p.GetString("SdcsSpawnAmmo") : null);
            bool bad;
            return NPCSdcsCharacters.Pure.WeaponRollRules.ResolveSpawnAmmoCount(p.Contains("SdcsSpawnAmmoCount"), p.Contains("SdcsSpawnAmmoCount") ? p.GetString("SdcsSpawnAmmoCount") : null, out bad);
        }

        /// <summary>The kit melee: the first melee item of the class kit (itemsOnEnterGame), or null.</summary>
        public static string Kit(EntityAlive e)
        {
            if (e == null || e.itemsOnEnterGame == null) return null;
            foreach (ItemStack st in e.itemsOnEnterGame)
            {
                ItemClass ic = st != null && st.itemValue != null ? st.itemValue.ItemClass : null;
                if (ic != null && ScAmmo.RangedAction(ic) == null) return ic.GetItemName();
            }
            return null;
        }

        /// <summary>
        /// The raider as a Loadout for SC's code that reads one (records, the console, the medic, the XP bonus): a gun or
        /// bow is the ranged weapon with the kit melee beside it; a melee weapon is melee-only. Null when unknown.
        /// </summary>
        public static Loadout Current(EntityAlive e)
        {
            bool predicted;
            string w = Weapon(e, out predicted);
            if (w == null) return null;
            ItemClass ic = ItemClass.GetItemClass(w, true);
            if (ic == null) return null;
            if (ScAmmo.RangedAction(ic) != null) return new Loadout(ic.GetItemName(), Kit(e));
            return new Loadout(Loadout.NoRanged, ic.GetItemName());
        }

        /// <summary>A carried copy of the item to put in the hand: the kit's, else the bag's (SCore's FindWeapon takes both).</summary>
        public static ItemValue Carried(EntityAlive e, string item)
        {
            ItemClass ic = ItemClass.GetItemClass(item, true);
            if (e == null || ic == null) return null;
            if (e.itemsOnEnterGame != null)
                foreach (ItemStack st in e.itemsOnEnterGame)
                    if (st != null && st.itemValue != null && st.itemValue.type == ic.Id) return st.itemValue.Clone();
            ItemStack[] bag = e.bag != null ? e.bag.ItemGrid.items : null;
            for (int i = 0; bag != null && i < bag.Length; i++)
                if (bag[i] != null && !bag[i].IsEmpty() && bag[i].itemValue.type == ic.Id) return bag[i].itemValue.Clone();
            return null;
        }

        // ------------------------------------------------------------------ PostInit ----

        /// <summary>
        /// ScLoadout.BeforePostInit for a bandit raider, host only, instead of the pair: 0-XNPCCore's trade stock and water
        /// marked as seeded (Q8); the old raiders' cvars off (a raider from a save made before R1); a known weapon (pending:
        /// a record's or the console's) set as the index, first the ranged weapon, then the melee one (Q6); else the saved
        /// index checked against the weapon it rolled (FixIndex).
        /// </summary>
        public static void BeforePostInit(EntityAlive e, Loadout pending)
        {
            WarnIfUnbound();
            if (e == null || e.Buffs == null || !ScCommunity.IsHost) return;
            foreach (string mark in SeededMarks)
            {
                if (e.Buffs.GetCustomVar(mark) > 0f) continue;
                e.Buffs.SetCustomVar(mark, 1f, false);
                Patches_Xnpc.Count("r1-stock-water-premarked");
            }
            bool old = false;
            foreach (string c in OldRaiderCvars)
                if (e.Buffs.HasCustomVar(c)) { e.Buffs.RemoveCustomVar(c); old = true; }
            if (old)
            {
                Patches_Xnpc.Count("r1-old-raider-migrated");
                ScLog.Verbose("raider " + e.entityId + " (" + ScCommunity.ClassName(e) + "): the old pair's cvars taken off - 0-XNPCCore arms it now");
            }
            if (pending != null)
            {
                // the R1 review's L2: a ranged weapon no longer in the list (IZY Classic removed) is not replaced by the melee
                // weapon beside it (the kit, always listed - the body would be melee-only on every re-make while the record
                // kept the gun): 0-XNPCCore rolls (FixIndex below), and ScSpawn.Body points the record at the roll
                string want = pending.HasRanged ? pending.Ranged : pending.Melee;
                int idx = IndexOf(e.entityClass, want);
                if (idx > 0)
                {
                    e.Buffs.SetCustomVar(WeaponCvar, idx, false);
                    e.Buffs.SetCustomVar(SeedCvar, 1f, false);
                    e.Buffs.SetCustomVar(RolledHashCvar, ScLoadout.HashOf(List(e.entityClass)[idx - 1]), false);
                    Patches_Xnpc.Count("r1-known-weapon-set");
                    return;
                }
                Patches_Xnpc.Count("r1-known-weapon-not-in-list");
                refused.Add(e.entityId);
                ScLog.Verbose("raider " + e.entityId + " (" + ScCommunity.ClassName(e) + "): " + pending + " is not in its class's weapon list - 0-XNPCCore rolls one, and its record follows");
            }
            FixIndex(e);
        }

        /// <summary>
        /// The index guard (design 6.3): a saved index whose entry is no longer the weapon the raider rolled (a list
        /// regenerated in another order, IZY Classic added or removed) moves to that weapon's first entry; with the weapon
        /// gone from the list, the index and the hash go, and 0-XNPCCore rolls once more (with its ammo grant).
        /// </summary>
        private static void FixIndex(EntityAlive e)
        {
            float h = e.Buffs.GetCustomVar(RolledHashCvar);
            if (h <= 0f) return;
            string[] l = List(e.entityClass);
            int i = Index(e);
            if (i >= 1 && i <= l.Length && ScLoadout.HashOf(l[i - 1]) == h) return;
            for (int j = 0; j < l.Length; j++)
            {
                if (ScLoadout.HashOf(l[j]) != h) continue;
                e.Buffs.SetCustomVar(WeaponCvar, j + 1, false);
                Patches_Xnpc.Count("r1-index-moved");
                ScLog.Verbose("raider " + e.entityId + ": its weapon " + l[j] + " moved in its class's list - index " + i + " -> " + (j + 1));
                return;
            }
            if (e.Buffs.HasCustomVar(WeaponCvar)) e.Buffs.RemoveCustomVar(WeaponCvar);
            e.Buffs.RemoveCustomVar(RolledHashCvar);
            Patches_Xnpc.Count("r1-index-cleared");
            ScLog.Verbose("raider " + e.entityId + ": its weapon is no longer in its class's list - 0-XNPCCore rolls a new one");
        }

        /// <summary>
        /// ScLoadout.AfterPostInit for a bandit raider (host), in place of the starting ammo: a body given its index by SC
        /// (SeedCvar) gets one copy of the weapon in the bag and that weapon's spawn ammo, as 0-XNPCCore gives a weapon it
        /// rolls; then the window, if any, is emptied into the bag and dropped.
        /// </summary>
        public static void AfterPostInit(EntityAlive e)
        {
            if (e == null || e.Buffs == null) return;
            if (e.Buffs.GetCustomVar(SeedCvar) > 0f)
            {
                e.Buffs.RemoveCustomVar(SeedCvar);
                string[] l = List(e.entityClass);
                int i = Index(e);
                if (i >= 1 && i <= l.Length)
                {
                    string w = l[i - 1];
                    ItemValue iv = ItemClass.GetItem(w, false);
                    if (e.bag != null && iv != null && !iv.IsEmpty() && e.bag.GetItemCount(iv, -1, -1, true) <= 0) e.bag.AddItem(new ItemStack(iv, 1));
                    int n = GrantSpawnAmmo(e, w, true);
                    Patches_Xnpc.Count("r1-known-weapon-seeded");
                    ScLog.Verbose("raider " + e.entityId + ": its known weapon " + w + " seeded in the bag with " + n + " round(s)");
                }
            }
            RollThrowable(e);
            DropWindow(e);
        }

        // ------------------------------------------------------------------ X2: thrown explosives ----

        /// <summary>
        /// X2 (0-XNPCCore 3.4.03; the user's odds, 2026-11-09). 0-XNPCCore's bandits throw explosives at groups
        /// (ThrowExplosive, from what the body carries: SdcsThrowSupply reads every store, the bag included). Its own grant
        /// comes from the class (SdcsThrowables), which BeforePostInit marks as done for SC's raiders (SeededMarks). Here,
        /// once per body (ThrowCvar), SC rolls one by the raider's tier: RaiderThrowChance[tier] to carry one at all, then
        /// the item by the weights in RaiderThrowT1-T3 (default: tier 1 a molotov only; tier 2 adds pipe bombs and grenades;
        /// tier 3 mostly grenades, some pipe bombs, few molotovs). It goes in the bag: a bandit raider keeps no inventory
        /// window (DropWindow). A body that already carries a throwable (one made under 3.4.03 before this round) gets none.
        /// </summary>
        private static void RollThrowable(EntityAlive e)
        {
            try
            {
                if (e.Buffs.GetCustomVar(ThrowCvar) > 0f) return;
                e.Buffs.SetCustomVar(ThrowCvar, 1f, false);
                if (CarriesThrowable(e)) { Patches_Xnpc.Count("x2-throw-already-carried"); return; }
                int tier = ScXnpc.TierOf(ScCommunity.ClassName(e));
                float chance = ScConfig.RaiderThrowChanceFor(tier);
                if (chance <= 0f || UnityEngine.Random.value >= chance) { Patches_Xnpc.Count("x2-throw-none"); return; }
                string item = PickThrowable(ScConfig.RaiderThrowItemsFor(tier));
                if (item == null) { Patches_Xnpc.Count("x2-throw-none"); return; }     // an empty RaiderThrowT list: nothing
                ItemValue iv = ItemClass.GetItem(item, false);
                if (iv == null || iv.IsEmpty())
                {
                    if (warnedThrow.Add(item)) ScLog.Warn("RaiderThrowT" + (tier + 1) + ": no item '" + item + "' in items.xml - that raider throws nothing");
                    return;
                }
                // review finding 5b: 0-XNPCCore throws only what its ThrowRules know; anything else would sit in the bag unused
                if (!NPCSdcsCharacters.Pure.ThrowRules.IsThrowable(item))
                {
                    if (warnedThrow.Add(item)) ScLog.Warn("RaiderThrowT" + (tier + 1) + ": '" + item + "' is not one of 0-XNPCCore's throwables (molotov, pipe bomb, grenade, contact grenade, timed charge) - not given");
                    return;
                }
                if (e.bag == null || !e.bag.AddItem(new ItemStack(iv, 1))) { Patches_Xnpc.Count("x2-throw-bag-full"); return; }
                Patches_Xnpc.Count("x2-throw-" + item);
                ScLog.Verbose("raider " + e.entityId + " (" + ScCommunity.ClassName(e) + "): carries a " + item);
            }
            catch (Exception ex)
            {
                ScLog.Once("bandit-throwable", ex);
            }
        }

        /// <summary>X2: items already warned about (a missing or unthrowable RaiderThrowT entry), once a session each.</summary>
        private static readonly HashSet<string> warnedThrow = new HashSet<string>();

        /// <summary>X2: one item by weight from "item:weight,item:weight" (a bare item weighs 1); null when nothing weighs.</summary>
        private static string PickThrowable(string spec)
        {
            if (string.IsNullOrEmpty(spec)) return null;
            var items = new List<string>();
            var weights = new List<float>();
            float total = 0f;
            foreach (string part in spec.Split(','))
            {
                string p = part.Trim();
                if (p.Length == 0) continue;
                int colon = p.IndexOf(':');
                string name = colon < 0 ? p : p.Substring(0, colon).Trim();
                float w = 1f;
                if (colon >= 0 && !float.TryParse(p.Substring(colon + 1).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out w)) w = 0f;
                if (name.Length == 0 || w <= 0f) continue;
                items.Add(name);
                weights.Add(w);
                total += w;
            }
            if (total <= 0f) return null;
            float r = UnityEngine.Random.value * total;
            for (int i = 0; i < items.Count; i++)
            {
                r -= weights[i];
                if (r < 0f) return items[i];
            }
            return items[items.Count - 1];
        }

        /// <summary>X2: the body already carries a thrown explosive 0-XNPCCore throws (its ThrowRules), in any store.</summary>
        private static bool CarriesThrowable(EntityAlive e)
        {
            if (e.itemsOnEnterGame != null)
                foreach (ItemStack st in e.itemsOnEnterGame)
                    if (IsThrowable(st)) return true;
            ItemStack[] bag = e.bag != null ? e.bag.ItemGrid.items : null;
            for (int i = 0; bag != null && i < bag.Length; i++) if (IsThrowable(bag[i])) return true;
            if (HarvestManager.Has(e.entityId))
            {
                SCoreLootContainer w = HarvestManager.GetOrCreate(e.entityId);
                if (w != null && w.items != null) foreach (ItemStack st in w.items) if (IsThrowable(st)) return true;
            }
            return false;
        }

        private static bool IsThrowable(ItemStack st)
        {
            if (st == null || st.IsEmpty() || st.itemValue == null || st.itemValue.ItemClass == null) return false;
            return NPCSdcsCharacters.Pure.ThrowRules.IsThrowable(st.itemValue.ItemClass.GetItemName());
        }

        /// <summary>
        /// The spawn ammo of a weapon by 0-XNPCCore's rules into the bag: its class's SdcsSpawnAmmo / SdcsSpawnAmmoCount, of
        /// the ammo 0-XNPCCore's table names - or, for a gun its table does not know (unmapped: IZY Classic), the gun's own
        /// first magazine item. mapped: also for a gun 0-XNPCCore knows (SC seeding an index it set itself); false gives
        /// only the unmapped ones (X1c, after 0-XNPCCore's own grant). The rounds given.
        /// </summary>
        private static int GrantSpawnAmmo(EntityAlive e, string weapon, bool mapped)
        {
            try
            {
                ItemClass gun = ItemClass.GetItemClass(weapon, true);
                ItemActionRanged r = ScAmmo.RangedAction(gun);
                EntityClass ec;
                if (r == null || !EntityClass.list.TryGetValue(e.entityClass, out ec) || ec == null) return 0;
                string ammo = NpcAmmoFor(weapon);
                bool unmapped = ammo == null && NpcUnmapped(weapon);
                if (ammo == null && !unmapped) return 0;
                if (ammo != null && !mapped) return 0;
                if (ammo == null) ammo = r.MagazineItemNames[0];
                bool grant;
                int count = NpcSpawnCount(ec, out grant);
                ItemValue iv = ItemClass.GetItem(ammo, true);
                if (!grant || count <= 0 || iv == null || iv.IsEmpty()) return 0;
                int n = ScAmmo.AddRounds(e, iv, count);
                if (unmapped) Patches_Xnpc.Count("x1c-unmapped-ammo");
                return n;
            }
            catch (Exception ex)
            {
                ScLog.Once("bandit-ammo", ex);
                return 0;
            }
        }

        /// <summary>
        /// A bandit raider keeps no inventory window (ScNpc.Items: its things are in the bag): 0-XNPCCore's spawn ammo lands
        /// in one, so the rounds go to the bag (ScAmmo.MoveWindowAmmoToBag) and an emptied window is dropped from
        /// HarvestManager, which would otherwise keep it in HarvestManager.bin for good.
        /// </summary>
        private static void DropWindow(EntityAlive e)
        {
            if (!HarvestManager.Has(e.entityId)) return;
            ScAmmo.MoveWindowAmmoToBag(e);
            SCoreLootContainer w = HarvestManager.GetOrCreate(e.entityId);
            if (w == null || w.IsUserAccessing() || ScAmmo.IsRemoteOpen(e.entityId)) return;
            if (w.items != null) foreach (ItemStack s in w.items) if (s != null && !s.IsEmpty()) return;
            HarvestManager.Remove(e.entityId);
            Patches_Xnpc.Count("x1c-window-removed");
        }

        // ------------------------------------------------------------------ death ----

        /// <summary>
        /// Patches_Npc's death drop for a bandit raider (decision 3: 0-XNPCCore's loot bag only): a medical use in progress
        /// ends (ScMedic.OnDied), the bag is cleared, a window is dropped from HarvestManager - no backpack. Counted once.
        /// </summary>
        public static void OnDied(EntityAlive e)
        {
            if (e == null) return;
            ScMedic.OnDied(e);
            if (e.bag != null) e.bag.Clear();
            if (HarvestManager.Has(e.entityId)) HarvestManager.Remove(e.entityId);
            if (e.Buffs != null && e.Buffs.GetCustomVar(DiscardedCvar) <= 0f)
            {
                e.Buffs.SetCustomVar(DiscardedCvar, 1f);
                Patches_Xnpc.Count("raider-death-discard");
                ScHandheld.OnRaiderDied(e);         // R2: now and then a handheld radio, in a backpack of its own (once per raider)
            }
        }

        // ------------------------------------------------------------------ the console ----

        /// <summary>
        /// sc loadout for a bandit raider: the weapon becomes its index (it must be in the class's list), it is seeded in
        /// the bag with fresh spawn ammo (freshAmmo) and put in the hand. False with a reason otherwise.
        /// </summary>
        public static bool SetWeapon(EntityAlive e, string item, bool freshAmmo, out string why)
        {
            why = null;
            int idx = IndexOf(e.entityClass, item);
            if (idx <= 0) { why = item + " is not in " + ScCommunity.ClassName(e) + "'s weapon list (sc raiders lists it)"; return false; }
            e.Buffs.SetCustomVar(WeaponCvar, idx);
            e.Buffs.SetCustomVar(RolledHashCvar, ScLoadout.HashOf(item));
            ItemValue iv = ItemClass.GetItem(item, false);
            if (e.bag != null && iv != null && !iv.IsEmpty() && e.bag.GetItemCount(iv, -1, -1, true) <= 0) e.bag.AddItem(new ItemStack(iv, 1));
            if (freshAmmo) GrantSpawnAmmo(e, item, true);
            ItemValue hand = Carried(e, item);
            if (hand == null || !ScNpc.UpdateWeapon(e, hand, true)) { why = "set, but " + item + " could not be put in the hand"; }
            ScLoadout.SetRaiderXp(e);
            return true;
        }

        /// <summary>A random entry of the class's list of this family (melee, bow, firearm; "ranged" = bow or firearm), or null.</summary>
        public static string RandomOf(int classId, string family, GameRandom rnd)
        {
            var hits = new List<string>();
            foreach (string w in List(classId))
            {
                ItemClass ic = ItemClass.GetItemClass(w, true);
                if (ic == null) continue;
                bool ranged = ScAmmo.RangedAction(ic) != null;
                if (family == "melee" ? !ranged : family == "ranged" ? ranged : ScLoadout.FamilyOf(w) == family) hits.Add(w);
            }
            return hits.Count == 0 ? null : hits[rnd != null ? rnd.RandomRange(0, hits.Count) : 0];
        }

        /// <summary>sc xnpc / sc raiders: the index, the weapon and where it came from, the kit, the hash guard, the seeded marks.</summary>
        public static string Describe(EntityAlive e)
        {
            if (e == null || e.Buffs == null) return "-";
            bool predicted;
            string w = Weapon(e, out predicted);
            string[] l = List(e.entityClass);
            var sb = new StringBuilder();
            sb.Append("bandit raider: SdcsWeapon=").Append(Index(e)).Append('/').Append(l.Length)
              .Append(" weapon=").Append(w ?? "-").Append(predicted ? " (not rolled yet: predicted)" : "")
              .Append(" kit=").Append(Kit(e) ?? "-")
              .Append(" hashGuard=").Append(e.Buffs.HasCustomVar(RolledHashCvar) ? (w != null && ScLoadout.HashOf(w) == e.Buffs.GetCustomVar(RolledHashCvar) ? "ok" : "MISMATCH") : "-")
              .Append(" window=").Append(HarvestManager.Has(e.entityId))
              .Append(" stock/water marked=").Append(e.Buffs.GetCustomVar(SeededMarks[0]) > 0f).Append('/').Append(e.Buffs.GetCustomVar(SeededMarks[1]) > 0f);
            return sb.ToString();
        }

        /// <summary>
        /// sc loadout sim raider, with bandit raiders: each class's list counted (0-XNPCCore's roll is uniform over the
        /// entries), by family and by the tier in the item's name, against SC's odds.
        /// </summary>
        public static string Simulate()
        {
            var sb = new StringBuilder("[SC] raider weapon lists (0-XNPCCore rolls one entry of the class's list, uniformly):");
            foreach (string c in ScConfig.XnpcRaiderClasses)
            {
                int id = EntityClass.FromString(c.Trim());
                if (!ScCommunity.ClassExists(id)) continue;
                string[] l = List(id);
                var fam = new Dictionary<string, int>();
                int izy = 0, unknown = 0;
                foreach (string w in l)
                {
                    string f = ScLoadout.FamilyOf(w) ?? "?";
                    if (f == "?") unknown++;
                    int n;
                    fam.TryGetValue(f, out n);
                    fam[f] = n + 1;
                    if (w.StartsWith("IZY", StringComparison.Ordinal)) izy++;
                }
                sb.Append("\n[SC]   ").Append(c.Trim()).Append(": ").Append(l.Length).Append(" entries,");
                foreach (string f in new[] { "melee", "bow", "firearm" })
                {
                    int n;
                    fam.TryGetValue(f, out n);
                    sb.Append(' ').Append(f).Append(' ').Append(l.Length > 0 ? (100.0 * n / l.Length).ToString("0.0") : "0").Append('%');
                }
                sb.Append(", IZY ").Append(izy).Append(unknown > 0 ? ", " + unknown + " not in the weapon catalogue" : "");
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ X1c: 0-XNPCCore's gear-only half for bandit raiders ----

        /// <summary>X1c: 0-XNPCCore 3.4.02's look applier (SdcsLookApplier, internal static; Apply(Entity) internal, ~1850 bytes of IL).</summary>
        public static MethodBase ApplyTarget()
        {
            Type t = typeof(AvatarSdcsNpcController).Assembly.GetType("SdcsLookApplier", false);
            return t != null ? AccessTools.DeclaredMethod(t, "Apply", new[] { typeof(Entity) }) : null;
        }

        /// <summary>X1c: its gear-only half, ApplyGearOnly(Entity, EntityAlive) (private static): the weapon section alone.</summary>
        public static MethodInfo GearOnlyMethod()
        {
            Type t = typeof(AvatarSdcsNpcController).Assembly.GetType("SdcsLookApplier", false);
            return t != null ? AccessTools.DeclaredMethod(t, "ApplyGearOnly", new[] { typeof(Entity), typeof(EntityAlive) }) : null;
        }

        /// <summary>
        /// Both X1c methods exist in the loaded 0-XNPCCore (ModApi's ExpectedTargets lists Apply only then: patching runs
        /// before the XML is loaded, so whether bandit raiders exist is not known yet; WarnIfUnbound covers the rest).
        /// </summary>
        public static bool Bindable()
        {
            try { return ApplyTarget() != null && GearOnlyMethod() != null; }
            catch (Exception) { return false; }
        }

        /// <summary>
        /// The first bandit raider made with X1c off (the R1 review's L3): one warning, once there are bandit raiders to be
        /// without their rolled weapon. An 0-XNPCCore without the bandits makes none, so it stays quiet.
        /// </summary>
        private static void WarnIfUnbound()
        {
            if (Patch_GearOnly.Bound) return;
            ScLog.Once("bandit-gearonly-unbound", (Patch_GearOnly.Missing ?? "SdcsLookApplier's gear-only re-route is off")
                       + " - SC's raiders on 0-XNPCCore's bandits get no rolled weapon, only their kit melee (0-XNPCCore update?)");
        }

        /// <summary>
        /// Bodies whose known weapon (a record's) was not in their class's list, so 0-XNPCCore rolls a new one (the R1
        /// review's L2): ScSpawn.Body takes the id back and points the record at the new weapon. Main thread.
        /// </summary>
        private static readonly HashSet<int> refused = new HashSet<int>();

        /// <summary>True once for a body whose record weapon was refused (BeforePostInit): its record should follow the roll.</summary>
        public static bool TakeRefused(int entityId)
        {
            return refused.Remove(entityId);
        }

        /// <summary>
        /// X1c (round R1). 0-XNPCCore's look trigger on a bandit raider (MinEventActionSdcsApplyLook, which X1b now lets
        /// through for these classes) queues its look applier; the queue's drain (AvatarSdcsNpcController
        /// .SdcsDrainDeferredLookApply, every Update) calls SdcsLookApplier.Apply for it. For a bandit raider this prefix
        /// calls the applier's own ApplyGearOnly instead - the same weapon section with the same seed (the entity id) - and
        /// skips the rest: the name roll, the archetype and the armour set (SC's look, name and clothes stay). The skipped
        /// Apply returns ApplyOutcome 0 (Done, the default result), so the drain does not retry. After it, on the host, SC
        /// empties the window into the bag and grants an unmapped gun's ammo (After). Everyone else: 0-XNPCCore's Apply.
        /// Bound by name like the other 0-XNPCCore targets: should either method be gone, the patch is off and the bandit
        /// raiders' empty look pool stops 0-XNPCCore's full applier before it changes anything (they keep their kit melee).
        /// That is reported only where it matters (the R1 review's L3): the warning when the first bandit raider is made
        /// (WarnIfUnbound - an 0-XNPCCore without its bandits has no bandit raiders and stays quiet), and ModApi lists Apply
        /// as an expected target only while both methods exist (Bindable). Apply is far too big to inline; the counter shows
        /// it runs.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_GearOnly
        {
            private static MethodInfo gearOnly;
            /// <summary>Prepare found both methods (the patch is on).</summary>
            internal static bool Bound;
            /// <summary>What Prepare missed, for WarnIfUnbound (null while bound or before Prepare).</summary>
            internal static string Missing;

            [HarmonyPrepare]
            public static bool Prepare()
            {
                try
                {
                    MethodBase apply = ApplyTarget();
                    gearOnly = GearOnlyMethod();
                    Bound = apply != null && gearOnly != null;
                    Missing = Bound ? null : "0-XNPCCore method " + (apply == null ? "SdcsLookApplier.Apply(Entity)" : "SdcsLookApplier.ApplyGearOnly(Entity, EntityAlive)") + " not found";
                }
                catch (Exception e)
                {
                    Bound = false;
                    Missing = "binding 0-XNPCCore's SdcsLookApplier failed (" + e.GetType().Name + ": " + e.Message + ")";
                }
                return Bound;
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return ApplyTarget(); }

            /// <summary>
            /// The R1 review's M1: the gear-only call and SC's step after it each in its own try, each failure counted
            /// (x1c-gear-only-failed, x1c-after-failed) and logged under its own key, so one failing After does not hide the
            /// next one's. A bandit raider never gets 0-XNPCCore's whole applier: false either way (outcome Done).
            /// </summary>
            [HarmonyPrefix]
            public static bool Prefix(Entity __0)
            {
                EntityAlive alive = __0 as EntityAlive;
                int before = 0;
                try
                {
                    // One of SC's own people, not a raider: never 0-XNPCCore's applier. It would roll
                    // her an archetype, an armor set, a name and a weapon, because SC writes none of
                    // the cvars it reads. SC's gear comes back within a second (ScLooks.Sync) but her
                    // FACE does not - UpdateEquipment never re-runs createModel - so she keeps a
                    // stranger's face until the world reloads.
                    //
                    // Until 3.4.05 the two ways in were both blocked upstream (X1b on
                    // MinEventActionSdcsApplyLook.Execute, X17 on SdcsCustomLook.ApplyCharacter). The
                    // new Wardrobe added two more that bypass both - SdcsWardrobe.Redress and
                    // NetPackageSdcsWardrobe.ProcessPackage, each calling EnqueueLookApply directly -
                    // so the refusal belongs HERE, at the one chokepoint every route has to pass.
                    // Nothing in SC calls the applier, so skipping it costs SC nothing.
                    if (alive != null && !ScXnpc.IsBanditRaider(alive) && ScXnpc.IsBody(alive))
                    {
                        Patches_Xnpc.Count("x1c-sc-body-skipped");
                        // Round n05: the applier is still refused, but the Wardrobe's pick is not lost with it. The
                        // only thing in Apply that SC wants is WearOverrides - the four SdcsWear* cvars - so SC reads
                        // them here and dresses her through its own look instead (ScWardrobe.Honor).
                        ScWardrobe.Honor(alive);
                        return false;
                    }
                    if (alive == null || !ScXnpc.IsBanditRaider(alive)) return true;
                    before = Index(alive);
                    gearOnly.Invoke(null, new object[] { __0, alive });
                    Patches_Xnpc.Count("x1c-raider-gear-only");
                }
                catch (Exception ex)
                {
                    Patches_Xnpc.Count("x1c-gear-only-failed");
                    ScLog.Once("xnpc-gearonly", ex);
                    return false;           // never 0-XNPCCore's whole applier on one of SC's raiders
                }
                if (alive.isEntityRemote || !ScCommunity.IsHost) return false;
                try
                {
                    After(alive, before);
                }
                catch (Exception ex)
                {
                    Patches_Xnpc.Count("x1c-after-failed");
                    ScLog.Once("xnpc-gearonly-after", ex);
                }
                return false;
            }

            /// <summary>
            /// After the gear-only call, host: the hash of the weapon it holds by (the index guard), an unmapped gun's ammo
            /// when this call rolled (0-XNPCCore grants none, and warns), the window emptied into the bag and dropped, the
            /// kill XP bonus of the weapon (Q2).
            /// </summary>
            private static void After(EntityAlive e, int before)
            {
                string[] l = List(e.entityClass);
                int now = Index(e);
                if (now < 1 || now > l.Length) return;
                string w = l[now - 1];
                bool rolled = before < 1 || before > l.Length;
                e.Buffs.SetCustomVar(RolledHashCvar, ScLoadout.HashOf(w));
                if (rolled)
                {
                    int n = GrantSpawnAmmo(e, w, false);
                    ScLog.Verbose("raider " + e.entityId + " (" + ScCommunity.ClassName(e) + ") rolled " + w + " (entry " + now + " of " + l.Length + ")"
                                  + (n > 0 ? ", " + n + " round(s) of its ammo from SC (0-XNPCCore's table has no ammo for it)" : ""));
                }
                DropWindow(e);
                ScLoadout.SetRaiderXp(e);
            }
        }
    }
}
#endif
