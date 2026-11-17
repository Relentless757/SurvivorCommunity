using System;
using System.Collections.Generic;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round U1 (2026-11-16): which of her gun's ammo types a member uses - the one thing 0-XNPCCore 3.4.06's equipment window
    /// had that SC's member inventory panel lacked (the user: "SC panel already has armor slots, so the only extra option
    /// would be ammo type"). A host-only line under RANGED in the panel (XUiC_ScMemberInv), shown for a gun that takes more
    /// than one ammo type: the type and her rounds of it; a click picks the next type she has rounds of (a right click the
    /// one before), and a type she has none of is skipped - with none other, the click says so.
    ///
    /// The state is 0-XNPCCore's own: the gun's ItemValue.SelectedAmmoTypeIndex. Its equipment window's pick
    /// (VCCommandRouter.MutateSetAmmo -> AnyWeaponBridge.AmmoChoose -> AvatarSdcsNpcController.SdcsChooseAmmo) gives the gun's
    /// loaded rounds back to her pack and swaps the ammo (ItemActionRanged.SwapAmmoType) on the gun in her hand, or sets the
    /// index on a gun she has put away; its reload (PickReloadableAmmoType) and its base ammo restock (AmmoRestockRules
    /// .TypeOrder) start from that index, and Locked &amp; Loaded's reload looks for a magazine of that ammo first
    /// (Magazines.SelectedAmmoOf). The choice here drives that same state - through AmmoChoose itself for the gun in her hand.
    ///
    /// SC rebuilds her gun's copies far more often than the player picks (ScLoadout.Refill, SCore's PostInit after every load,
    /// 0-XNPCCore's equip by name), and SCore saves only the held item's name, so the choice is also kept in her record
    /// (Member.AmmoGun / AmmoPick, saved as ammopick=gun|ammo) and put on every EMPTY copy of that gun SC touches: the item
    /// list, the toolbelt and the hand in ScLoadout.ApplyWeaponMods, and the hand after every 0-XNPCCore equip
    /// (ScLoadout.ModsAfterEquip). A copy that holds rounds keeps its type (changing the index would relabel them). When
    /// 0-XNPCCore's reload fell back to another type because hers ran out, Tick (ScAmmo's 5 s loop) switches the gun in her
    /// hand back once she has the chosen type again, out of a fight.
    ///
    /// SC's own refills follow it too: the stockpile's resupply at home and at an outpost hands her the chosen type, and
    /// counts that type against XnpcResupplyBelow (ScAmmo.Resupply, ResupplyAmmo); with Locked &amp; Loaded it goes into her
    /// empty magazines (NpcMagazines.Refill fills only an empty magazine or one of the same ammo), a magazine seated in the
    /// gun goes back whole through L&amp;L's ReturnSeated before a switch (ScMags.Eject), and round lr1's re-seat on the draw
    /// passes over a put-away magazine of another type while she has a magazine of the chosen one (ScMags.ReseatOnDraw,
    /// ReseatAllowed).
    ///
    /// What she can load (Loadable): a member who reloads the gun from L&amp;L magazines (ScMags.Carries) loads only what is
    /// IN a magazine - loose rounds of the chosen type wait until a magazine is empty (SC's LoadLoose, a refill). So with
    /// L&amp;L only magazine rounds count before her gun is switched to the chosen type (the choice, Tick, Apply) or a
    /// put-away magazine of another type is passed over; without L&amp;L every round in her stores does. The fallback guard
    /// (the Fable review of U1, finding 1): no copy takes the chosen type while she has none of it she can load, or while she
    /// reloads - 0-XNPCCore's own switch to a type she has (SdcsTrySwitchAmmoType) must stand, or vanilla's reload reads
    /// the chosen type, finds none and loads nothing, again and again, and she never fails over to melee.
    ///
    /// Members only (her record), on the host, while MemberInventoryPanel is on (with it off 0-XNPCCore's own window chooses,
    /// and SC leaves the gun's index alone). A choice applies only while her pair's gun is the one it was made for: another
    /// gun starts from its own default, and the old choice comes back with that gun. Voice Control's own ammo pick (the order
    /// wheel's ammo page, "use AP" by voice: VCCommandRouter.MutateSetAmmo) becomes her choice too (Noted), so the two never
    /// fight. Main thread.
    /// </summary>
    internal static class ScAmmoChoice
    {
        /// <summary>Tick: a switch back 0-XNPCCore declined is not tried again before this real time, by entity id.</summary>
        private static readonly Dictionary<int, float> retryAt = new Dictionary<int, float>();
        private const float RetrySeconds = 60f;

        // ------------------------------------------------------------------ the record ----

        /// <summary>The member a body stands for: her own record, or the one a stand-in carries (host only).</summary>
        private static Member MemberOf(EntityAlive e)
        {
            if (e == null || !ScRegistry.Loaded) return null;
            Member m = ScRegistry.GetMember(e.entityId);
            if (m == null && e.Buffs != null && e.Buffs.HasCustomVar(ScRadio.StandInCvar)) m = ScRegistry.GetMember((int)e.Buffs.GetCustomVar(ScRadio.StandInCvar));
            return m;
        }

        /// <summary>The position of an ammo item in the gun's list (MagazineItemNames), or -1.</summary>
        private static int IndexOf(ItemActionRanged r, string ammo)
        {
            if (r == null || r.MagazineItemNames == null || string.IsNullOrEmpty(ammo)) return -1;
            for (int i = 0; i < r.MagazineItemNames.Length; i++)
                if (string.Equals(r.MagazineItemNames[i], ammo, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        /// <summary>The ammo type her record chose for this gun (its index in the gun's list), when it applies. A read.</summary>
        private static bool Wanted(Member m, ItemClass gun, out int index)
        {
            index = -1;
            if (!ScConfig.MemberInventoryPanel) return false;
            if (m == null || gun == null || string.IsNullOrEmpty(m.AmmoGun) || !string.Equals(m.AmmoGun, gun.GetItemName(), StringComparison.OrdinalIgnoreCase)) return false;
            index = IndexOf(ScAmmo.RangedAction(gun), m.AmmoPick);
            return index >= 0;
        }

        /// <summary>The ammo type chosen for this body's gun (its index), when the gun is her pair's and a choice applies. A read.</summary>
        internal static bool Wanted(EntityAlive e, ItemClass gun, out int index)
        {
            index = -1;
            ItemClass pair = ScAmmo.PairGun(e);
            return pair != null && gun != null && pair.Id == gun.Id && Wanted(MemberOf(e), gun, out index);
        }

        // ------------------------------------------------------------------ every copy SC makes ----

        /// <summary>
        /// One copy of her pair's gun (ScLoadout.ApplyWeaponMods, ModsAfterEquip): an EMPTY copy - no rounds, no seated
        /// Locked &amp; Loaded magazine - takes the chosen ammo type, but only while she has rounds of it she can load
        /// (Loadable) and is not reloading (the fallback guard, the Fable review of U1, finding 1). Anything else, or no
        /// choice, is left as it is.
        /// </summary>
        internal static void Apply(EntityAlive e, Loadout pair, ItemValue iv)
        {
            try
            {
                if (iv == null || iv.IsEmpty() || iv.Meta > 0 || pair == null || !pair.HasRanged || iv.ItemClass == null) return;
                if (!string.Equals(iv.ItemClass.GetItemName(), pair.Ranged, StringComparison.OrdinalIgnoreCase)) return;
                int want;
                if (!Wanted(MemberOf(e), iv.ItemClass, out want) || iv.SelectedAmmoTypeIndex == want || ScMags.SeatedFlag(iv)) return;
                if (Reloading(e)) return;
                ItemActionRanged r = ScAmmo.RangedAction(iv.ItemClass);
                if (Loadable(e, iv.ItemClass, r, want, Entries(e, iv.ItemClass)) <= 0) return;   // 0-XNPCCore's fallback stands
                iv.SelectedAmmoTypeIndex = (byte)want;
            }
            catch (Exception ex) { ScLog.Once("ammo-choice-apply", ex); }
        }

        /// <summary>0-XNPCCore's reload cvar (ScBackOff and ScArms read the same one): she is reloading right now.</summary>
        private static bool Reloading(EntityAlive e)
        {
            return e != null && e.Buffs != null && e.Buffs.GetCustomVar("SdcsReloading") > 0.5f;
        }

        /// <summary>
        /// ScAmmo.Resupply: the ammo the stockpile hands her body e for this gun - the chosen type, else the gun's first (its
        /// basic one, as before). have: her rounds of the chosen type (RoundsOf), which Resupply then measures against
        /// XnpcResupplyBelow instead of all her rounds; -1 with no choice.
        /// </summary>
        internal static ItemValue ResupplyAmmo(Member m, EntityAlive e, ItemClass gun, ItemActionRanged r, out int have)
        {
            int want;
            have = -1;
            if (!Wanted(m, gun, out want)) return ItemClass.GetItem(r.MagazineItemNames[0], true);
            have = Loadable(e, gun, r, want, Entries(e, gun));      // with L&L her magazines' rounds of it: the refill fills magazines
            return ItemClass.GetItem(r.MagazineItemNames[want], true);
        }

        /// <summary>
        /// ScMags.ReseatOnDraw (round lr1): may the magazine she put this gun away with go back in, ammo type magIndex (L&amp;L
        /// keeps a magazine's type as an index, the same one it copies onto the gun it seats)? Not when a choice applies, the
        /// magazine holds another type and she has a MAGAZINE of the chosen one (Loadable: loose rounds of it cannot go in -
        /// the Fable review of U1, finding 2): the draw then reloads, and L&amp;L's reload takes that magazine. A read.
        /// </summary>
        internal static bool ReseatAllowed(EntityAlive e, ItemClass gun, int magIndex)
        {
            int want;
            if (!Wanted(e, gun, out want) || magIndex == want) return true;
            ItemActionRanged r = ScAmmo.RangedAction(gun);
            return Loadable(e, gun, r, want, Entries(e, gun)) <= 0;
        }

        // ------------------------------------------------------------------ her rounds of each type ----

        /// <summary>Locked &amp; Loaded's magazines for the gun (the seated one included), or null without L&amp;L.</summary>
        private static List<ScMags.Entry> Entries(EntityAlive e, ItemClass gun)
        {
            if (!ScMags.Active || e == null || gun == null) return null;
            string rec = ScMags.Record(e, gun.GetItemName());
            return string.IsNullOrEmpty(rec) ? null : ScMags.Entries(rec);
        }

        /// <summary>
        /// Her rounds of one of the gun's ammo types: loose in her stores (toolbelt, bag, pack), in her Locked &amp; Loaded
        /// magazines for it, and loaded in the gun in her hand (a seated magazine is counted with the magazines).
        /// </summary>
        private static int RoundsOf(EntityAlive e, ItemClass gun, ItemActionRanged r, int index, List<ScMags.Entry> mags)
        {
            if (e == null || r == null || index < 0 || index >= r.MagazineItemNames.Length) return 0;
            ItemValue ammo = ItemClass.GetItem(r.MagazineItemNames[index], true);
            if (ammo == null || ammo.IsEmpty() || ammo.ItemClass == null) return 0;
            int n = ScAmmo.InStores(e, ammo.type) + MagRoundsOf(r, index, mags);
            ItemValue held = e.inventory != null ? e.inventory.holdingItemItemValue : null;
            if (held != null && !held.IsEmpty() && held.type == gun.Id && held.Meta > 0 && held.SelectedAmmoTypeIndex == index
                && !(mags != null && ScMags.SeatedFlag(held)))
                n += held.Meta;
            return n;
        }

        /// <summary>Rounds of one of the gun's ammo types in her Locked &amp; Loaded magazines for it (the seated one included).</summary>
        private static int MagRoundsOf(ItemActionRanged r, int index, List<ScMags.Entry> mags)
        {
            if (mags == null || r == null || index < 0 || index >= r.MagazineItemNames.Length) return 0;
            ItemValue ammo = ItemClass.GetItem(r.MagazineItemNames[index], true);
            string name = ammo != null && ammo.ItemClass != null ? ammo.ItemClass.GetItemName() : r.MagazineItemNames[index];
            int n = 0;
            foreach (ScMags.Entry x in mags)
                if (string.Equals(x.Ammo, name, StringComparison.OrdinalIgnoreCase)) n += x.Rounds;
            return n;
        }

        /// <summary>Does she reload this gun from Locked &amp; Loaded magazines (ScMags.Carries: one seated in it, or one for it in her stores)?</summary>
        private static bool ByMagazine(EntityAlive e, ItemClass gun)
        {
            return ScMags.Active && e != null && gun != null && ScMags.Carries(e, ScMags.GunValue(e, gun));
        }

        /// <summary>
        /// Rounds of one of the gun's types she can load NOW: reloading from L&amp;L magazines, only those in her magazines
        /// (a loose round waits for an empty magazine); else every round in her stores and her hand (RoundsOf). The Fable
        /// review of U1, finding 2.
        /// </summary>
        private static int Loadable(EntityAlive e, ItemClass gun, ItemActionRanged r, int index, List<ScMags.Entry> mags)
        {
            return ByMagazine(e, gun) ? MagRoundsOf(r, index, mags) : RoundsOf(e, gun, r, index, mags);
        }

        /// <summary>The type the line shows: the chosen one, else the gun in her hand's, else a toolbelt copy's, else the first.</summary>
        private static int Current(EntityAlive e, Member m, ItemClass gun, ItemActionRanged r)
        {
            int n = r.MagazineItemNames.Length, want;
            if (Wanted(m, gun, out want)) return want;
            ItemValue held = e.inventory != null ? e.inventory.holdingItemItemValue : null;
            if (held != null && !held.IsEmpty() && held.type == gun.Id) return held.SelectedAmmoTypeIndex < n ? held.SelectedAmmoTypeIndex : 0;
            ItemStack[] belt = e.inventory != null && e.inventory.ItemGrid != null ? e.inventory.ItemGrid.items : null;
            if (belt != null)
                foreach (ItemStack s in belt)
                    if (s != null && !s.IsEmpty() && s.itemValue != null && s.itemValue.type == gun.Id)
                        return s.itemValue.SelectedAmmoTypeIndex < n ? s.itemValue.SelectedAmmoTypeIndex : 0;
            return 0;
        }

        private static string Label(string item)
        {
            ItemClass ic = ItemClass.GetItemClass(item, true);
            return ic != null ? ic.GetLocalizedItemName() : item;
        }

        // ------------------------------------------------------------------ the panel ----

        /// <summary>
        /// The panel's ammo line for her: "9mm HP Ammo · 45" (her rounds of it she can load: with L&amp;L the ones in her
        /// magazines; "· 30 loose" when she has that type only as loose rounds, which wait for an empty magazine) - false (no
        /// line) on a multiplayer client (her bag is the host's), for anyone but a member, and for a gun with only one ammo
        /// type (a bow with stone arrows only, most mod guns). A read.
        /// </summary>
        internal static bool Line(EntityAlive npc, out string text)
        {
            text = "";
            try
            {
                if (!ScCommunity.IsHost || npc == null || npc.inventory == null) return false;
                Member m = ScRegistry.Loaded ? ScRegistry.GetMember(npc.entityId) : null;
                ItemClass gun = m != null ? ScAmmo.PairGun(npc) : null;
                ItemActionRanged r = ScAmmo.RangedAction(gun);
                if (r == null || r.MagazineItemNames.Length < 2) return false;
                int cur = Current(npc, m, gun, r);
                List<ScMags.Entry> mags = Entries(npc, gun);
                int can = Loadable(npc, gun, r, cur, mags), all = RoundsOf(npc, gun, r, cur, mags);
                text = can > 0 || all <= 0 ? string.Format(Localization.Get("scInvAmmoLine"), Label(r.MagazineItemNames[cur]), can)
                                           : string.Format(Localization.Get("scInvAmmoLineLoose"), Label(r.MagazineItemNames[cur]), all);
                return true;
            }
            catch (Exception e)
            {
                ScLog.Once("ammo-choice-line", e);
                return false;
            }
        }

        /// <summary>
        /// The line clicked: step +1 (the next type) or -1 (the one before) to the nearest type she has rounds of (loose ones
        /// count: the choice stands for when she can load them), which she then uses (Choose). Null when done; else the
        /// refusal for the player ("" for nothing to do). note: done, but with L&amp;L she has that type only as loose rounds -
        /// her gun keeps its type until one of her magazines is empty and takes them (the panel shows it, without the refusal's
        /// sound). Host only (ScMemberEquip.Gate, as the panel's slots).
        /// </summary>
        internal static string Cycle(EntityPlayerLocal player, EntityAlive npc, int step, out string note)
        {
            note = null;
            try
            {
                Member m;
                string why = ScMemberEquip.Gate(player, npc, out m);
                if (why != null) return why;
                string name = ScCommunity.DisplayName(npc);
                ItemClass gun = ScAmmo.PairGun(npc);
                ItemActionRanged r = ScAmmo.RangedAction(gun);
                if (r == null || r.MagazineItemNames.Length < 2) return "";
                if (ScMedic.IsUsing(npc.entityId)) return ScMemberEquip.Text("scInvRefuseBusy", name);
                int n = r.MagazineItemNames.Length, cur = Current(npc, m, gun, r), pick = -1;
                List<ScMags.Entry> mags = Entries(npc, gun);
                for (int k = 1; k < n && pick < 0; k++)
                {
                    int i = ((cur + (step < 0 ? -k : k)) % n + n) % n;
                    if (RoundsOf(npc, gun, r, i, mags) > 0) pick = i;
                }
                if (pick < 0) return ScMemberEquip.Text("scInvAmmoNoOther", name, gun.GetLocalizedItemName());
                Choose(npc, m, gun, r, pick, "the member panel");
                if (Loadable(npc, gun, r, pick, Entries(npc, gun)) <= 0)
                    note = ScMemberEquip.Text("scInvAmmoNeedsMag", name, Label(r.MagazineItemNames[pick]));
                return null;
            }
            catch (Exception e)
            {
                ScLog.Once("ammo-choice-cycle", e);
                return ScMemberEquip.Text("scInvRefuseFailed", e.GetType().Name);
            }
        }

        /// <summary>
        /// She uses this type from now on: her record, then every copy of the gun at once (Convert). Saved at once (a choice is
        /// rare, and a crash must not undo it).
        /// </summary>
        private static void Choose(EntityAlive npc, Member m, ItemClass gun, ItemActionRanged r, int pick, string route)
        {
            ItemValue ammo = ItemClass.GetItem(r.MagazineItemNames[pick], true);
            m.AmmoGun = gun.GetItemName();
            m.AmmoPick = ammo != null && ammo.ItemClass != null ? ammo.ItemClass.GetItemName() : r.MagazineItemNames[pick];
            ScRegistry.SaveAtomic();
            string how = Convert(npc, gun, r, pick, "ammo choice");
            Patches_Xnpc.Count("ammo-choice");
            ScLog.Out(m.Name + " (id " + npc.entityId + ") uses " + m.AmmoPick + " in the " + m.AmmoGun + " (" + route + ", round U1)" + how);
        }

        /// <summary>
        /// Every copy of the gun takes ammo type pick. The gun in her hand through 0-XNPCCore's own switch (AmmoChoose: its loaded
        /// rounds back to her pack, the ammo swapped) - a seated Locked &amp; Loaded magazine first back whole through L&amp;L's
        /// ReturnSeated (ScMags.Eject) - or, should 0-XNPCCore decline an empty gun (it counts no rounds of that type), the index
        /// alone; a toolbelt copy's rounds back to her bag (a seated magazine through L&amp;L) and its index; the item list's
        /// entries their index. Returns a note for the log.
        /// </summary>
        private static string Convert(EntityAlive npc, ItemClass gun, ItemActionRanged r, int pick, string why)
        {
            string note = "";
            // nothing she can load yet (with L&L: none of it in a magazine): every copy keeps its type - ejecting her magazine
            // now would only see L&L's reload take one of the old type again (the Fable review of U1, finding 2); Tick and
            // Apply switch once she can
            if (Loadable(npc, gun, r, pick, Entries(npc, gun)) <= 0) return "; nothing of it she can load yet (with Locked & Loaded: none in a magazine) - her gun keeps its type for now";
            ItemValue held = npc.inventory != null ? npc.inventory.holdingItemItemValue : null;
            bool inHand = held != null && !held.IsEmpty() && held.type == gun.Id;
            if (inHand && held.SelectedAmmoTypeIndex != pick)
            {
                if (ScMags.SeatedFlag(held) && ScMags.CanEject && ScMags.Eject(npc, held, why)) note += "; its magazine went back to her bag (Locked & Loaded)";
                string res = AnyWeaponBridge.AmmoChoose(npc, "ammo:" + r.MagazineItemNames[pick]);
                if (res != "switched" && held.Meta <= 0 && !ScMags.SeatedFlag(held)) held.SelectedAmmoTypeIndex = (byte)pick;
                note += "; the gun in her hand: 0-XNPCCore answered " + res;
            }
            // the toolbelt's slots as ScLoadout.ApplyWeaponMods reads them; the hand slot is the gun just switched when she
            // holds it (never a second time: its rounds went back once)
            for (int i = 0; npc.inventory != null && i < npc.inventory.SlotCount; i++)
            {
                if (inHand && i == npc.inventory.holdingItemIdx) continue;
                ItemStack s = npc.inventory.GetItem(i);
                if (s == null || s.IsEmpty() || s.itemValue == null || s.itemValue.type != gun.Id || ReferenceEquals(s.itemValue, held)) continue;
                if (s.itemValue.SelectedAmmoTypeIndex == pick) continue;
                if (ScMags.SeatedFlag(s.itemValue) && ScMags.CanEject) ScMags.Eject(npc, s.itemValue, why);
                if (s.itemValue.Meta > 0 && !ScMags.SeatedFlag(s.itemValue)) ScAmmo.ReturnLoose(npc, s.itemValue, why, true);
                if (s.itemValue.Meta <= 0) s.itemValue.SelectedAmmoTypeIndex = (byte)pick;
            }
            if (npc.itemsOnEnterGame != null)
                foreach (ItemStack s in npc.itemsOnEnterGame)
                    if (s != null && s.itemValue != null && s.itemValue.type == gun.Id) s.itemValue.SelectedAmmoTypeIndex = (byte)pick;
            return note;
        }

        // ------------------------------------------------------------------ back to the choice ----

        /// <summary>
        /// ScAmmo's 5 s loop, per member body: when 0-XNPCCore's reload fell back to another type because the chosen one ran
        /// out, the gun in her hand goes back to the chosen type once she has rounds of it she can load again (Loadable: with
        /// L&amp;L a magazine of it) - out of a fight (no attack target), not mid-reload, not during a medical use - through the
        /// same switch as a choice (Convert). Empty toolbelt copies take it too (Apply). Every attempt arms RetrySeconds
        /// before the next (the Fable review of U1, finding 2: an attempt L&amp;L's reload undid must not repeat every 5 s);
        /// a later tick that still sees the chosen type in her hand counts it as done and clears the wait.
        /// </summary>
        internal static void Tick(Member m, EntityAlive e)
        {
            try
            {
                if (m == null || e == null || e.inventory == null || string.IsNullOrEmpty(m.AmmoGun) || e.IsDead()) return;
                if (ScDowned.IsDown(e)) return;                      // the combined review, finding 2: not while she is down
                ItemClass gun = ScAmmo.PairGun(e);
                int want;
                if (!Wanted(m, gun, out want) || ScMedic.IsUsing(e.entityId)) return;
                ItemActionRanged r = ScAmmo.RangedAction(gun);
                ItemValue held = e.inventory.holdingItemItemValue;
                bool inHand = held != null && !held.IsEmpty() && held.type == gun.Id;
                if (Reloading(e)) return;                                   // a switch or a fallback still going: judge it after
                float now = UnityEngine.Time.unscaledTime, at;
                bool waiting = retryAt.TryGetValue(e.entityId, out at);
                if (!inHand || held.SelectedAmmoTypeIndex == want)
                {
                    if (inHand && waiting)
                    {
                        retryAt.Remove(e.entityId);                         // the last attempt held
                        Patches_Xnpc.Count("ammo-choice-again");
                        ScLog.Verbose(m.Name + " (id " + e.entityId + ") is back on " + m.AmmoPick + " in the " + m.AmmoGun);
                    }
                    Loadout pair = ScLoadout.Current(e);
                    for (int i = 0; i < e.inventory.SlotCount; i++)
                    {
                        ItemStack s = e.inventory.GetItem(i);
                        if (s != null && !s.IsEmpty()) Apply(e, pair, s.itemValue);
                    }
                    return;
                }
                if (e.GetAttackTarget() != null) return;
                if (waiting && now < at) return;
                if (Loadable(e, gun, r, want, Entries(e, gun)) <= 0) return;
                string how = Convert(e, gun, r, want, "ammo choice again");
                if (retryAt.Count > 256) retryAt.Clear();
                retryAt[e.entityId] = now + RetrySeconds;                   // armed after every attempt; a later tick confirms it
                Patches_Xnpc.Count("ammo-choice-again-try");
                ScLog.Verbose(m.Name + " (id " + e.entityId + ") has " + m.AmmoPick + " again: switching back to it in the " + m.AmmoGun + how
                              + " (next try no sooner than " + RetrySeconds.ToString("0") + " s)");
            }
            catch (Exception ex) { ScLog.Once("ammo-choice-tick", ex); }
        }

        /// <summary>
        /// The ammo type index of her pair's gun as 0-XNPCCore's ammo pick sees it - the gun in her hand, or the one put away in
        /// her hand slot - or -1. A read (Patch_VcSetAmmo's prefix and postfix compare it).
        /// </summary>
        internal static int GunIndex(EntityAlive e)
        {
            ItemValue iv = PickedGun(e);
            return iv != null ? iv.SelectedAmmoTypeIndex : -1;
        }

        private static ItemValue PickedGun(EntityAlive e)
        {
            if (e == null || e.inventory == null) return null;
            ItemClass gun = ScAmmo.PairGun(e);
            if (gun == null) return null;
            ItemValue iv = e.inventory.holdingItemItemValue;
            if (iv != null && !iv.IsEmpty() && iv.type == gun.Id) return iv;
            ItemStack slot = e.inventory.GetItem(e.inventory.holdingItemIdx);
            return slot != null && !slot.IsEmpty() && slot.itemValue != null && slot.itemValue.type == gun.Id ? slot.itemValue : null;
        }

        /// <summary>
        /// After Voice Control's own ammo pick CHANGED her gun's type (ScEquipRedirect.Patch_VcSetAmmo: the index before and
        /// after differ - a refused pick, "none" or "same", changes nothing and is not taken): the type her pair's gun now uses
        /// becomes her choice, so Tick never switches her back from what you just told her. Members only, the panel on, the host.
        /// </summary>
        internal static void Noted(EntityAlive e)
        {
            try
            {
                if (!ScConfig.MemberInventoryPanel || !ScCommunity.IsHost || e == null || e.inventory == null) return;
                Member m = ScRegistry.Loaded ? ScRegistry.GetMember(e.entityId) : null;
                ItemClass gun = m != null ? ScAmmo.PairGun(e) : null;
                ItemActionRanged r = ScAmmo.RangedAction(gun);
                if (r == null || r.MagazineItemNames.Length < 2) return;
                ItemValue iv = PickedGun(e);
                if (iv == null || iv.SelectedAmmoTypeIndex >= r.MagazineItemNames.Length) return;
                ItemValue ammo = ItemClass.GetItem(r.MagazineItemNames[iv.SelectedAmmoTypeIndex], true);
                string pick = ammo != null && ammo.ItemClass != null ? ammo.ItemClass.GetItemName() : r.MagazineItemNames[iv.SelectedAmmoTypeIndex];
                if (string.Equals(m.AmmoGun, gun.GetItemName(), StringComparison.OrdinalIgnoreCase) && string.Equals(m.AmmoPick, pick, StringComparison.OrdinalIgnoreCase)) return;
                m.AmmoGun = gun.GetItemName();
                m.AmmoPick = pick;
                ScRegistry.SaveAtomic();
                Patches_Xnpc.Count("ammo-choice-voice");
                ScLog.Out(m.Name + " (id " + e.entityId + ") uses " + pick + " in the " + m.AmmoGun + " (Voice Control's ammo pick, round U1)");
            }
            catch (Exception ex) { ScLog.Once("ammo-choice-noted", ex); }
        }

        /// <summary>For sc xnpc / sc team: her choice in a few words ("-" for none).</summary>
        internal static string Describe(Member m)
        {
            return m == null || string.IsNullOrEmpty(m.AmmoGun) ? "-" : m.AmmoPick + " in the " + m.AmmoGun;
        }
    }
}
