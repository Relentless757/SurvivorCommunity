using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Our XNPC bodies - the community's survivors and SC's raiders (ScXnpc.IsBody) - heal only by using a medical item
    /// they actually carry, and you see them do it.
    ///
    /// The NPC framework heals every one of its NPCs for free: its status buff (buffNPCStatusCheck, effect group
    /// NPCHealing) adds IsHealing whenever health is at or below 70% and a 15 s cooldown has run out, and IsHealing
    /// queues 30 health at 2 per second - no item, and no animation on this rig (it fires an animator trigger the
    /// player animator does not have). Measured on a raider of SC's former player-body classes: 50 to 82 health within
    /// a few seconds of a fight. That buff is refused here for our bodies; the framework's own NPCs keep it.
    ///
    /// Instead each body is stocked once with a few first aid bandages and, now and then, a first aid kit, in the
    /// inventory SCore shows for a friendly NPC (HarvestManager, saved with the world and dropped as a backpack on
    /// death) or, for a raider, the vanilla bag (dropped with its loot). When hurt and not already regenerating it
    /// uses one the way a player does: the item leaves the inventory and goes into the NPC's hand, the player's own
    /// third-person use animation plays (the rig picks it from the held item's hold type plus the UseItem trigger -
    /// 3P_Bandage_Hold and 3P_Bandage_Use for medical items, verified live), and when that animation finishes the
    /// item's onSelfPrimaryActionEnd effects fire on the NPC and the weapon goes back in the hand. Interrupted by a
    /// knockdown, the weapon comes back and the item returns to the inventory. You can also hand a survivor medical
    /// items and they will use those. While anyone has a survivor's inventory window open (the host, or a remote player:
    /// ScAmmo.IsWindowOpen) the survivor neither stocks nor uses anything from it, since the open grid would not show the
    /// change, or the copy written back on close would undo it.
    ///
    /// Decision D7: items only, with the bandage animation. XNPC heals its own bodies through a different buff,
    /// SdcsIsHealing (added by its class buff SdcsStatusCheck: for free with no leader, from a medical item without an
    /// animation when hired), which is refused for ours like IsHealing. Their medical stock is this file's roll alone
    /// (Config/entityclasses.xml sets HarvestItems to none on the survivor classes and on the raider templates). While a
    /// use is in progress XNPC's own weapon logic is held off (Patches_Xnpc X5), so its failover or close-range swap
    /// cannot take the bandage out of the hand.
    ///
    /// 3.4 build (decision D2, 2026-10-29). 0-XNPCCore 3.4 dropped the bleeding resistances from npcSdcsBase, so our bodies
    /// bleed (buffInjuryBleeding, which vanilla's bleed carriers - BleedingOne, Two, BarbedWire - all start; lacerations
    /// stay resisted), and its own self-heal is skipped on them (Patches_Xnpc X15): this file treats it. Vanilla's cloth
    /// bandage, first aid bandage and first aid kit each stop it (RemoveBuff buffInjuryBleeding on onSelfPrimaryActionEnd,
    /// items.xml). A bleed is bandaged with the cheapest of them when health is to spare and the body is not fighting (an
    /// attack or revenge target); low on health, the heal item comes first, as it stops the bleed too. Every body is
    /// stocked once with one or two cloth bandages (sc_bandagesStocked, so bodies stocked before also get them). Items
    /// are found in the bag as well as the window: 0-XNPCCore 3.4 moves a survivor's medical items into its bag once
    /// (its start route) and puts what it loots there. While one of its own hand sequences runs (SdcsAnimBusy: its
    /// bandage, heal, douse or party-medic clip) no use starts here; its party medic waits for one running here; and a
    /// body of ours that its party medic treated gets the regen speed the item's "other" effects leave at 0 on an NPC.
    /// </summary>
    public static class ScMedic
    {
        /// <summary>Items that restore health, best first. Plain bandages only stop bleeding, so they are not here.</summary>
        public static readonly string[] HealItems = { "medicalFirstAidKit", "medicalFirstAidBandage" };
#if SC_V34
        /// <summary>3.4 (D2): what stops a bleed, cheapest first (0-XNPCCore's own order for a bleed, MedicRules.CheapestItem).</summary>
        public static readonly string[] BleedItems = { "medicalBandage", "medicalFirstAidBandage", "medicalFirstAidKit" };
        /// <summary>3.4: every item a use here can put in the hand (a load finding one there gives the weapon back).</summary>
        private static readonly string[] UseItems = { "medicalFirstAidKit", "medicalFirstAidBandage", "medicalBandage" };
        /// <summary>The bleed every vanilla carrier starts (buffs.xml: BleedingOne, Two and BarbedWire add it).</summary>
        private const string BleedBuff = "buffInjuryBleeding";
        private const string BandagesStockedCvar = "sc_bandagesStocked";
        private static int bleedsStopped, xnpcBusyWaits, xnpcHealSpeedFixed, xnpcMedicHeld;
#endif

        private const float HealBelow = 0.6f;        // use something at or below this share of max health
        private const float Cooldown = 12f;          // seconds between two uses (counted from the start of one)
        private const float CheckEvery = 1f;         // how often a body looks at its health
        private const int KitWorthMissing = 90;      // below this much missing health a kit would be wasted
        private const float TriggerTimeout = 0.5f;   // fire the use trigger even if the animator is slow to show the hold type
        /// <summary>
        /// medRegHealthIncSpeed for an NPC. The vanilla items set it from the player's Physician perk cvar, which an
        /// NPC does not have, leaving 0 - the queued health would drain without healing. 2 is the no-perk value, and
        /// the one the framework uses for its own heal.
        /// </summary>
        private const float NpcRegenSpeed = 2f;
        private const string StockedCvar = "sc_medsStocked";

        private static readonly Dictionary<int, float> nextCheck = new Dictionary<int, float>();
        private static readonly Dictionary<int, float> readyAt = new Dictionary<int, float>();

        /// <summary>An item in use: what it is, how long it takes, and the stack it displaced from the hand.</summary>
        private sealed class Use
        {
            public string Item;
            public int HoldType;
            public int Slot;
            public ItemStack Displaced;
            public float Started;
            public float Duration;
            public float EndAt;
            public bool Triggered;
            public int UseLayer = -2;               // animator layer the use clip plays on (-1: none, -2: not looked up)
            public bool SawClip;
        }
        private static readonly Dictionary<int, Use> uses = new Dictionary<int, Use>();

        /// <summary>The weapon a use took out of the hand and the item in the hand instead, for the held-gun save.</summary>
        internal sealed class Displaced
        {
            public readonly ItemValue Weapon;
            public readonly string Item;
            public Displaced(ItemValue weapon, string item) { Weapon = weapon; Item = item; }
        }

        /// <summary>
        /// Every use in progress, mirrored for ScAmmo.WriteHeldGun (the re-check's step 2): a body saved while it bandages
        /// holds the medical item, so its gun - rounds and all - is only here. Written on the main thread only (Begin,
        /// Finish, OnDied, FixLoadedHand, Reset); read from the chunk save, which runs on the save thread for an unloaded
        /// chunk (ChunkCluster.RemoveChunk unloads the entities first, RegionFileManager writes them later on
        /// thread_SaveChunks), hence a concurrent dictionary of immutable entries. For the same reason an entry outlives
        /// its body's unload: the save that needs it comes after. It goes when the use ends, the body dies, the body is
        /// loaded again (FixLoadedHand) or the game ends.
        /// </summary>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, Displaced> displaced =
            new System.Collections.Concurrent.ConcurrentDictionary<int, Displaced>();

        /// <summary>A medical use is in progress (ScRunners skips its periodic snapshot - a Write(false) would finish the use).</summary>
        public static bool IsUsing(int id) { return uses.ContainsKey(id); }

        /// <summary>The weapon a use in progress took out of this body's hand, and the item it holds instead (any thread).</summary>
        internal static bool TryDisplaced(int id, out Displaced d) { return displaced.TryGetValue(id, out d); }

        public static void Reset()
        {
            nextCheck.Clear();
            readyAt.Clear();
            uses.Clear();
            displaced.Clear();
            Patch_NoFreeHeal.SdcsRefused = 0;
#if SC_V34
            bleedsStopped = xnpcBusyWaits = xnpcHealSpeedFixed = xnpcMedicHeld = 0;
#endif
        }

        /// <summary>For sc xnpc: the stock flag, whether a use is in progress, and how often XNPC's own heal was refused.</summary>
        public static string Describe(EntityAlive e)
        {
            if (e == null || e.Buffs == null) return "-";
            Use u;
            string use = uses.TryGetValue(e.entityId, out u) ? "using " + u.Item + (u.Triggered ? " (animation started)" : " (waiting for the hold pose)") : "idle";
#if SC_V34
            // 3.4 (D2): the cloth bandages and the bleed too; the counts take the bag in (0-XNPCCore moves medical items there)
            return "stocked=" + (e.Buffs.GetCustomVar(StockedCvar) > 0f) + "/" + (e.Buffs.GetCustomVar(BandagesStockedCvar) > 0f) + " " + use
                   + (e.Buffs.HasBuff(BleedBuff) ? " BLEEDING" : "") + " cloth=" + Count(e, "medicalBandage") + " bandages=" + Count(e, "medicalFirstAidBandage")
                   + " kits=" + Count(e, "medicalFirstAidKit") + " SdcsIsHealing refused (all bodies)=" + Patch_NoFreeHeal.SdcsRefused
                   + "; all bodies: bleeds stopped " + bleedsStopped + ", waits for XNPC's hand clips " + xnpcBusyWaits
                   + ", XNPC medic regen fixed " + xnpcHealSpeedFixed + ", XNPC medic held for a use " + xnpcMedicHeld;
#else
            return "stocked=" + (e.Buffs.GetCustomVar(StockedCvar) > 0f) + " " + use + " bandages=" + Count(e, "medicalFirstAidBandage")
                   + " kits=" + Count(e, "medicalFirstAidKit") + " SdcsIsHealing refused (all bodies)=" + Patch_NoFreeHeal.SdcsRefused;
#endif
        }

        /// <summary>One of our XNPC bodies (ScXnpc.IsBody: a survivor class the community owns, or a raider); XNPC's other classes heal their own way.</summary>
        public static bool IsBody(Entity e)
        {
            return ScXnpc.IsBody(e);
        }

        public static bool IsUsingItem(Entity e)
        {
            return e != null && uses.ContainsKey(e.entityId);
        }

        /// <summary>Runs from EntityAlive.OnUpdateLive for every entity: must stay cheap for everything that is not ours.</summary>
        public static void Tick(EntityAlive e)
        {
            if (!IsBody(e)) return;
            Use u;
            if (uses.TryGetValue(e.entityId, out u)) { Advance(e, u); return; }
            float now = Time.time;
            float next;
            if (nextCheck.TryGetValue(e.entityId, out next) && now < next) return;
            nextCheck[e.entityId] = now + CheckEvery;
            if (e.IsDead() || e.Health <= 0 || e.Buffs == null) return;
            if (GameManager.Instance == null || GameManager.Instance.gameStateManager == null || !GameManager.Instance.gameStateManager.IsGameStarted()) return;
            // its inventory window is open (ScAmmo.IsWindowOpen): a remote player's copy is written back over every slot
            // when they close it, and even the host's own grid is not told about a change, so a bandage taken out now
            // could be had twice and one stocked now could be lost - both wait for the close (raiders keep theirs in the
            // bag, never held)
            if (ScNpc.UsesWindow(e) && ScAmmo.IsWindowOpen(e.entityId)) { Patches_Xnpc.Count("window-medic-held"); return; }
            if (!e.world.IsRemote() && e.Buffs.GetCustomVar(StockedCvar) <= 0f) Stock(e);
#if SC_V34
            if (!e.world.IsRemote() && e.Buffs.GetCustomVar(BandagesStockedCvar) <= 0f) StockBandages(e);
#endif
            TryHeal(e, now);
        }

        public static void Forget(int entityId)
        {
            nextCheck.Remove(entityId);
            readyAt.Remove(entityId);
            uses.Remove(entityId);
            Displaced gone;
            displaced.TryRemove(entityId, out gone);
        }

        // ---------------------------------------------------------------------------------------------------------
        // stock
        // ---------------------------------------------------------------------------------------------------------

        private static void Stock(EntityAlive e)
        {
            e.Buffs.SetCustomVar(StockedCvar, 1f);
            GameRandom rnd = e.world.GetGameRandom();
            float r = rnd.RandomFloat;
            int bandages = r < 0.3f ? 0 : (r < 0.75f ? 1 : 2);
            int kits = rnd.RandomFloat < 0.15f ? 1 : 0;
            if (bandages > 0) Add(e, "medicalFirstAidBandage", bandages);
            if (kits > 0) Add(e, "medicalFirstAidKit", kits);
            ScLog.Verbose("medic: stocked " + e.entityId + " with " + bandages + " first aid bandage(s), " + kits + " kit(s)");
        }

#if SC_V34
        /// <summary>3.4 (D2): one or two cloth bandages, once per body (bodies stocked before 3.4 get them too).</summary>
        private static void StockBandages(EntityAlive e)
        {
            e.Buffs.SetCustomVar(BandagesStockedCvar, 1f);
            int n = e.world.GetGameRandom().RandomFloat < 0.5f ? 1 : 2;
            Add(e, "medicalBandage", n);
            ScLog.Verbose("medic: stocked " + e.entityId + " with " + n + " cloth bandage(s)");
        }
#endif

        private static void Add(EntityAlive e, string item, int count)
        {
            ItemValue iv = ItemClass.GetItem(item, true);
            if (iv == null || iv.IsEmpty()) return;
            ScNpc.AddItem(e, new ItemStack(iv, count));
        }

        // ---------------------------------------------------------------------------------------------------------
        // decide
        // ---------------------------------------------------------------------------------------------------------

#if SC_V34
        /// <summary>
        /// 3.4 (D2): the 3.2 heal below HealBelow, plus the bleed. A bleed with health to spare is stopped with the cheapest
        /// item that stops it (BleedItems), and only out of a fight (no live attack or revenge target): a 20 s bleed costs
        /// less than lowering the gun mid-fight for it. Low on health, the heal item comes first - a first aid bandage or
        /// kit stops the bleed as well - and with none left a cloth bandage at least stops the bleed. A heal still working
        /// (medicalRegHealthAmount) waits as before, but does not keep a bleed from being stopped. Nothing starts while one
        /// of 0-XNPCCore's own hand clips runs (SdcsAnimBusy: its bandage, heal, douse or party-medic sequence holds the
        /// hand slot this would write).
        /// </summary>
        private static void TryHeal(EntityAlive e, float now)
        {
            int max = e.GetMaxHealth();
            if (max <= 0) return;
            bool low = e.Health <= max * HealBelow;
            bool bleeding = e.Buffs.HasBuff(BleedBuff);
            if (!low && !bleeding) return;
            if (e.emodel == null || e.emodel.IsRagdollActive || e.inventory == null) return;
            float ready;
            if (readyAt.TryGetValue(e.entityId, out ready) && now < ready) return;
            if (e.GetCVar("SdcsAnimBusy") >= 0.5f) { xnpcBusyWaits++; return; }
            bool working = e.Buffs.GetCustomVar("medicalRegHealthAmount") > 0f;    // an item is already working

            string pick = null;
            if (low && !working)
            {
                int missing = max - e.Health;
                if (missing >= KitWorthMissing && Count(e, "medicalFirstAidKit") > 0) pick = "medicalFirstAidKit";
                else if (Count(e, "medicalFirstAidBandage") > 0) pick = "medicalFirstAidBandage";
                else if (Count(e, "medicalFirstAidKit") > 0) pick = "medicalFirstAidKit";
            }
            if (pick == null && bleeding && (low || !InFight(e))) pick = FirstCarried(e, BleedItems);
            if (pick == null) return;
            if (!TakeOne(e, pick)) return;
            Begin(e, pick, now);
            readyAt[e.entityId] = now + Cooldown;
            if (bleeding)
            {
                bleedsStopped++;
                ScLog.Verbose("medic: " + e.entityId + " is bleeding - " + pick);
            }
        }

        /// <summary>A live attack or revenge target (the bleed waits: ScRelax's alert reasons, the AI-free part).</summary>
        private static bool InFight(EntityAlive e)
        {
            EntityAlive t = e.GetAttackTarget();
            if (t != null && t != e && !t.IsDead()) return true;
            t = e.GetRevengeTarget();
            return t != null && t != e && !t.IsDead();
        }

        /// <summary>The first of these items the body carries (window or bag), or null.</summary>
        private static string FirstCarried(EntityAlive e, string[] items)
        {
            for (int i = 0; i < items.Length; i++) if (Count(e, items[i]) > 0) return items[i];
            return null;
        }
#else
        private static void TryHeal(EntityAlive e, float now)
        {
            int max = e.GetMaxHealth();
            if (max <= 0 || e.Health > max * HealBelow) return;
            if (e.emodel == null || e.emodel.IsRagdollActive || e.inventory == null) return;
            float ready;
            if (readyAt.TryGetValue(e.entityId, out ready) && now < ready) return;
            if (e.Buffs.GetCustomVar("medicalRegHealthAmount") > 0f) return;       // an item is already working

            int missing = max - e.Health;
            string pick = null;
            if (missing >= KitWorthMissing && Count(e, "medicalFirstAidKit") > 0) pick = "medicalFirstAidKit";
            else if (Count(e, "medicalFirstAidBandage") > 0) pick = "medicalFirstAidBandage";
            else if (Count(e, "medicalFirstAidKit") > 0) pick = "medicalFirstAidKit";
            if (pick == null) return;
            if (!TakeOne(e, pick)) return;
            Begin(e, pick, now);
            readyAt[e.entityId] = now + Cooldown;
        }
#endif

        // ---------------------------------------------------------------------------------------------------------
        // use: item into the hand, animation, effects when it ends, weapon back
        // ---------------------------------------------------------------------------------------------------------

        private static void Begin(EntityAlive e, string item, float now)
        {
            ItemClass ic = ItemClass.GetItemClass(item, true);
            if (ic == null) return;
            float delay = ic.Actions != null && ic.Actions.Length > 0 && ic.Actions[0] != null ? ic.Actions[0].Delay : 0f;
            var u = new Use
            {
                Item = item,
                HoldType = ic.HoldType != null ? ic.HoldType.Value : 0,
                Slot = e.inventory.holdingItemIdx,
                Started = now,
                Duration = Mathf.Clamp(delay > 0f ? delay : 2.5f, 1f, 6f),
            };
            ItemStack held = e.inventory.GetItem(u.Slot);
            // the weapon's own ItemValue, not a copy (M11): Locked & Loaded's Inventory.SetItem prefix gives a seated
            // magazine back to the bag when the medical item goes into the hand and empties this very ItemValue, so a copy
            // taken first would bring the magazine back with the gun while it also sits in the bag. Nothing else touches
            // it once it leaves the slot (SetItem stores a clone of whatever goes in).
            u.Displaced = held != null && !held.IsEmpty() ? new ItemStack(held.itemValue, held.count) : ItemStack.Empty.Clone();
#if SC_V34
            // 3.4: a grid slot that is EMPTIED is cleared in place (ItemStack.Clear resets the slot's own ItemValue, type 0),
            // where 3.2 swapped in a fresh one - so the weapon's own ItemValue could turn into nothing under us. A copy is
            // kept for that case alone; L&L's emptying (Meta 0, the type kept) still reaches the weapon's own value.
            ItemValue displacedCopy = u.Displaced.IsEmpty() ? null : u.Displaced.itemValue.Clone();
            int displacedCount = u.Displaced.count;
#endif
            u.EndAt = now + u.Duration + TriggerTimeout;
            uses[e.entityId] = u;
            Hold(e, u.Slot, new ItemValue(ic.Id), 1);
#if SC_V34
            if (displacedCopy != null && u.Displaced.itemValue.type == 0)
            {
                u.Displaced = new ItemStack(displacedCopy, displacedCount);
                Patches_Xnpc.Count("medic-displaced-cleared-in-place");
            }
#endif
            // after the hold: an L&L gun has just been emptied in place (its magazine is in the bag), a loose one keeps its rounds
            displaced[e.entityId] = new Displaced(u.Displaced.IsEmpty() ? null : u.Displaced.itemValue, item);
            string sound;
            DynamicProperties a0;
            if (ic.Properties != null && ic.Properties.Classes.TryGetValue("Action0", out a0) && a0.Values.TryGetValue("Sound_start", out sound) && !string.IsNullOrEmpty(sound))
                e.PlayOneShot(sound);
            ScLog.Verbose("medic: " + e.entityId + " starts using " + item + " at " + e.Health + "/" + e.GetMaxHealth() + " (" + u.Duration + " s)");
        }

        private static void Advance(EntityAlive e, Use u)
        {
            if (e.IsDead()) { Finish(e, u, false, false); return; }
            if (e.emodel == null || e.emodel.IsRagdollActive) { Finish(e, u, false, true); return; }
            float now = Time.time;
            if (!u.Triggered)
            {
                AvatarController ac = e.emodel.avatarController;
                bool shown = ac != null && ac.anim != null && ac.anim.GetInteger(AvatarController.weaponHoldTypeHash) == u.HoldType;
                if (shown || now - u.Started > TriggerTimeout)
                {
                    if (ac != null) ac._setTrigger(AvatarController.useItemHash);
                    u.Triggered = true;
                    u.EndAt = now + u.Duration;          // the animation gets its whole length from the trigger
                }
                return;
            }
            // Done when the use animation has played and left its layer - measured live, 3P_Bandage_Use runs about
            // 1.4 s against the bandage's 2.5 s use time, so waiting for the item's delay left the NPC standing
            // there holding it. The delay stays as the fallback if the clip never shows.
            AvatarController anim = e.emodel.avatarController;
            if (anim != null && anim.anim != null)
            {
                if (u.UseLayer == -2) u.UseLayer = anim.anim.GetLayerIndex("UpperBodyUseAndReload");
                if (u.UseLayer >= 0)
                {
                    bool playing = anim.anim.GetCurrentAnimatorClipInfoCount(u.UseLayer) > 0;
                    if (playing) u.SawClip = true;
                    else if (u.SawClip) { Finish(e, u, true, false); return; }
                }
            }
            if (now >= u.EndAt) Finish(e, u, true, false);
        }

        /// <summary>
        /// Ends a use. completed: the item's effects fire. Otherwise the use was interrupted, and returnItem puts the
        /// item back in the inventory (not for the dead - their inventory has already been dropped). The displaced
        /// weapon always goes back in the hand.
        /// </summary>
        private static void Finish(EntityAlive e, Use u, bool completed, bool returnItem)
        {
            uses.Remove(e.entityId);
            Displaced gone;
            displaced.TryRemove(e.entityId, out gone);
            try
            {
                if (completed) ApplyEffects(e, u.Item);
                else if (returnItem) Add(e, u.Item, 1);
            }
            finally
            {
                if (u.Displaced != null && !u.Displaced.IsEmpty()) Hold(e, u.Slot, u.Displaced.itemValue, u.Displaced.count);
                else Hold(e, u.Slot, ItemValue.None, 0);
            }
            ScLog.Verbose("medic: " + e.entityId + (completed ? " finished " : " stopped ") + u.Item + " at " + e.Health + "/" + e.GetMaxHealth());
        }

        /// <summary>
        /// Puts an item in the hand slot and refreshes the hand model: SCore's EntityAliveSDX.UpdateWeapon sequence
        /// (SetItem, SwitchModelAndView, OnUpdate, ForceHoldingItemUpdate) without its bookkeeping - _currentWeapon and
        /// the CurrentWeaponID cvar keep naming the real weapon, so nothing re-equips a bandage later.
        ///
        /// The SwitchModelAndView step is left out: on one of our XNPC bodies XNPC's controller only warns and keeps its
        /// model when it is handed a class name (it has no child of that name), so the call would change nothing but the
        /// log. The rest is the sequence XNPC's own weapon swaps effectively get. (ScLoadout.Arm's fallback for an entity
        /// that is not a framework NPC comes here too; nothing that reaches it today is one.)
        /// </summary>
        internal static void Hold(EntityAlive e, int slot, ItemValue item, int count)
        {
            if (e == null || e.inventory == null) return;
            e.inventory.SetItem(slot, item, count);
#if SC_V34
            // 3.4: no OnUpdate / ForceHoldingItemUpdate any more - the slot write raises the toolbelt grid's SlotChanged and the
            // inventory's Hand rebuilds the held item and its model from it (Inventory.ReconcileHeldItem is the manual nudge,
            // left out unless the model is seen to lag)
#else
            e.inventory.OnUpdate();
            e.inventory.ForceHoldingItemUpdate();
#endif
        }

        /// <summary>
        /// Patches_Npc's death drop, first thing: a body that dies in the middle of a use ends it here, before its bag
        /// and window are collected - the item goes back into the window and the weapon it displaced gives its loaded
        /// rounds to the bag (ScAmmo.ReturnLoose; an L&amp;L gun's magazine went to the bag when the use began), so both
        /// drop with the body instead of being lost with the hand. No effects, no hand change on the dead. True when a use
        /// was ended (SetDead runs again for every hit on the corpse; by then there is none).
        /// </summary>
        internal static bool OnDied(EntityAlive e)
        {
            Use u;
            if (e == null || !uses.TryGetValue(e.entityId, out u)) return false;
            uses.Remove(e.entityId);
            Displaced gone;
            displaced.TryRemove(e.entityId, out gone);
            Add(e, u.Item, 1);
            int n = u.Displaced != null && !u.Displaced.IsEmpty() ? ScAmmo.ReturnLoose(e, u.Displaced.itemValue, "died while using " + u.Item, true) : 0;
            if (n > 0) ScAmmo.NoteDeathReturn(n);
            Patches_Xnpc.Count("medic-died-mid-use");
            return true;
        }

        /// <summary>
        /// ScLoadout.AfterPostInit, first thing (the re-check's bandage case): a body saved while using a medical item
        /// comes back holding it. Chunk saves and unloads write the entity with bNetworkWrite true, so Patch_SaveMidUse
        /// never runs for them, and SCore's load re-equips the saved hand item by name whenever the window still holds one
        /// (FindWeapon) - which 0-XNPCCore never swaps away from (neither melee nor ranged). Here:
        ///  - a use left over from before this body was unloaded in this game ends without effect, and its mirror goes;
        ///  - when the save says a use was in progress (X13's record: ScAmmo.SavedHand), or that leftover use exists, its
        ///    item goes back into the window once: it was taken out when the use began and the hand at the save was
        ///    written nowhere, so the count is the one before the use;
        ///  - the hand gets the pair weapon again: the gun when X13 saved the gun the use displaced (a fresh copy, which
        ///    RestoreHeldGun, next, replaces with the saved one, rounds and magazine), else the melee weapon
        ///    (0-XNPCCore's world-entry re-arm then draws the gun by its own rules, from an L&amp;L gun's magazines in the
        ///    bag).
        /// A medical item in the hand with no such record (a save from before this build, or one handed over and held
        /// through the dialog's weapon menu, whose window copy was never taken) only gets the weapon back: no item is made.
        /// </summary>
        internal static void FixLoadedHand(EntityAlive e)
        {
            if (e == null || e.inventory == null) return;
            int id = e.entityId;
            Use stale;
            bool hadUse = uses.TryGetValue(id, out stale);
            if (hadUse) uses.Remove(id);
            Displaced gone;
            displaced.TryRemove(id, out gone);
            ItemValue savedGun;
            string savedUse;
            bool record = ScAmmo.SavedHand(id, out savedGun, out savedUse);
            string item = !string.IsNullOrEmpty(savedUse) ? savedUse : hadUse ? stale.Item : null;
            ItemValue held = e.inventory.holdingItemItemValue;
            string inHand = held != null && !held.IsEmpty() && held.ItemClass != null ? held.ItemClass.GetItemName() : null;
#if SC_V34
            bool medicalInHand = inHand != null && Array.IndexOf(UseItems, inHand) >= 0;      // 3.4: the cloth bandage too (D2)
#else
            bool medicalInHand = inHand != null && Array.IndexOf(HealItems, inHand) >= 0;
#endif
            if (item == null && !medicalInHand) return;
            if (item != null) Add(e, item, 1);
            if (medicalInHand || item != null)
            {
                Loadout l = ScLoadout.Current(e);
                ItemClass pairGun = l != null && l.HasRanged ? ItemClass.GetItemClass(l.Ranged, true) : null;
                bool ranged = record && savedGun != null && pairGun != null && savedGun.type == pairGun.Id;
                string why;
                if (!ScLoadout.Arm(e, ranged, out why))
                    ScLog.Warn(ScCommunity.DisplayName(e) + " (id " + id + ") loaded holding " + (inHand ?? "-") + " and could not be re-armed: " + why);
            }
            Patches_Xnpc.Count("medic-load-hand-fixed");
            ScLog.Verbose("medic: " + id + " loaded " + (item != null ? "in the middle of using " + item + " - put back in its inventory" : "holding " + inHand)
                          + ", pair weapon back in hand");
        }

        private static void ApplyEffects(EntityAlive e, string item)
        {
            ItemClass ic = ItemClass.GetItemClass(item, true);
            if (ic == null) return;
            MinEventParams p = e.MinEventContext;
            p.Self = e;
            p.Other = null;
            p.ItemValue = new ItemValue(ic.Id);
            ic.FireEvent(MinEventTypes.onSelfPrimaryActionEnd, p);
            if (e.Buffs.GetCustomVar("medRegHealthIncSpeed") <= 0f) e.Buffs.SetCustomVar("medRegHealthIncSpeed", NpcRegenSpeed);
            ScLog.Verbose("medic: " + e.entityId + " used " + item + " at " + e.Health + "/" + e.GetMaxHealth()
                          + " (queued " + e.Buffs.GetCustomVar("medicalRegHealthAmount") + ")");
        }

        // ---------------------------------------------------------------------------------------------------------
        // inventory
        // ---------------------------------------------------------------------------------------------------------

        private static ItemStack[] Slots(EntityAlive e)
        {
            return ScNpc.Items(e);
        }

#if SC_V34
        /// <summary>
        /// 3.4 (D2, bag seeding): a survivor's medical items are in its window (SC stocks them there, handed ones land
        /// there) and, since 0-XNPCCore 3.4, in its bag too: XNPC's start route moves the window's medical items into the
        /// bag once per body, and its looting puts them there. So a window user counts both; a raider has only its bag
        /// (Slots). Read only - every write goes through TakeOne.
        /// </summary>
        private static int Count(EntityAlive e, string item)
        {
            int type = ItemClass.GetItem(item, true).type;
            int n = CountIn(Slots(e), type);
            if (ScNpc.UsesWindow(e) && e.bag != null) n += CountIn(e.bag.ItemGrid.items, type);
            return n;
        }

        private static int CountIn(ItemStack[] slots, int type)
        {
            if (slots == null) return 0;
            int n = 0;
            for (int i = 0; i < slots.Length; i++)
                if (slots[i] != null && !slots[i].IsEmpty() && slots[i].itemValue.type == type) n += slots[i].count;
            return n;
        }

        /// <summary>One of the item out of the window first (ScNpc.SetItem, as on 3.2), else out of a window user's bag (Bag.SetSlot).</summary>
        private static bool TakeOne(EntityAlive e, string item)
        {
            int type = ItemClass.GetItem(item, true).type;
            ItemStack[] slots = Slots(e);
            int i = IndexIn(slots, type);
            if (i >= 0)
            {
                ItemStack s = slots[i];
                ScNpc.SetItem(e, i, s.count > 1 ? new ItemStack(s.itemValue, s.count - 1) : ItemStack.Empty.Clone());
                return true;
            }
            if (!ScNpc.UsesWindow(e) || e.bag == null) return false;
            ItemStack[] bag = e.bag.ItemGrid.items;
            i = IndexIn(bag, type);
            if (i < 0) return false;
            ItemStack b = bag[i];
            e.bag.SetSlot(i, b.count > 1 ? new ItemStack(b.itemValue, b.count - 1) : ItemStack.Empty.Clone());
            Patches_Xnpc.Count("medic-took-from-bag");
            return true;
        }

        private static int IndexIn(ItemStack[] slots, int type)
        {
            if (slots == null) return -1;
            for (int i = 0; i < slots.Length; i++)
                if (slots[i] != null && !slots[i].IsEmpty() && slots[i].itemValue.type == type) return i;
            return -1;
        }
#else
        private static int Count(EntityAlive e, string item)
        {
            ItemStack[] slots = Slots(e);
            if (slots == null) return 0;
            int type = ItemClass.GetItem(item, true).type, n = 0;
            for (int i = 0; i < slots.Length; i++)
                if (slots[i] != null && !slots[i].IsEmpty() && slots[i].itemValue.type == type) n += slots[i].count;
            return n;
        }

        private static bool TakeOne(EntityAlive e, string item)
        {
            ItemStack[] slots = Slots(e);
            if (slots == null) return false;
            int type = ItemClass.GetItem(item, true).type;
            for (int i = 0; i < slots.Length; i++)
            {
                ItemStack s = slots[i];
                if (s == null || s.IsEmpty() || s.itemValue.type != type) continue;
                ItemStack left = s.count > 1 ? new ItemStack(s.itemValue, s.count - 1) : ItemStack.Empty.Clone();
                ScNpc.SetItem(e, i, left);
                return true;
            }
            return false;
        }
#endif

        // ---------------------------------------------------------------------------------------------------------
        // patches
        // ---------------------------------------------------------------------------------------------------------

        [HarmonyPatch(typeof(EntityAlive), nameof(EntityAlive.OnUpdateLive))]
        public static class Patch_Tick
        {
            [HarmonyPostfix]
            public static void Postfix(EntityAlive __instance)
            {
                try
                {
                    if (__instance == null || !ScXnpc.IsBody(__instance)) return;
                    Tick(__instance);
                }
                catch (Exception ex)
                {
                    ScLog.Once("medic-tick", ex);
                }
            }
        }

        /// <summary>
        /// Refuses the frameworks' own heals for our bodies (see the class summary): the NPC framework's free IsHealing,
        /// and 0-XNPCCore's SdcsIsHealing. Both are added through this AddBuff by a buff's triggered effect. XNPC's attack
        /// actions gate on SdcsIsHealing, so refusing it also keeps them from standing down for a heal that never comes.
        /// </summary>
        [HarmonyPatch(typeof(EntityBuffs), nameof(EntityBuffs.AddBuff),
            new[] { typeof(string), typeof(Vector3i), typeof(int), typeof(bool), typeof(bool), typeof(float) })]
        public static class Patch_NoFreeHeal
        {
            /// <summary>Times the XNPC heal was refused (sc xnpc). Main thread only.</summary>
            internal static int SdcsRefused;

            [HarmonyPrefix]
            public static bool Prefix(EntityBuffs __instance, string _name, ref EntityBuffs.BuffStatus __result)
            {
                if (_name == null) return true;
                bool xnpcHeal = string.Equals(_name, "SdcsIsHealing", StringComparison.OrdinalIgnoreCase);
                if (!xnpcHeal && !string.Equals(_name, "IsHealing", StringComparison.OrdinalIgnoreCase)) return true;
                if (__instance == null || !IsBody(__instance.parent)) return true;
                if (xnpcHeal) SdcsRefused++;
                __result = EntityBuffs.BuffStatus.FailedImmune;
                return false;
            }
        }

        /// <summary>No attacking with a bandage: the vanilla and raider attack path, while an item is in use.</summary>
        [HarmonyPatch(typeof(EntityAlive), nameof(EntityAlive.UseHoldingItem))]
        public static class Patch_NoAttackWhileUsing
        {
            [HarmonyPrefix]
            public static bool Prefix(EntityAlive __instance, ref bool __result)
            {
                if (__instance == null || !uses.ContainsKey(__instance.entityId)) return true;
                __result = false;
                return false;
            }
        }

        /// <summary>
        /// No attacking with a bandage: SCore's own attack path, ExecuteAction(bool, int), which each of its two NPC classes
        /// declares for itself (ScNpc.Declared). Only our bodies ever have a use in progress, so on anyone else it is a no-op.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_NoSdxAttackWhileUsing
        {
            private static readonly Type[] Args = { typeof(bool), typeof(int) };

            [HarmonyPrepare]
            public static bool Prepare() { return ScNpc.Declared("ExecuteAction", Args).Count > 0; }

            [HarmonyTargetMethods]
            public static IEnumerable<MethodBase> TargetMethods() { return ScNpc.Declared("ExecuteAction", Args); }

            [HarmonyPrefix]
            public static bool Prefix(EntityAlive __instance, ref bool __result)
            {
                if (__instance == null || !uses.ContainsKey(__instance.entityId)) return true;
                __result = false;
                return false;
            }
        }

        /// <summary>
        /// A world save in the middle of a use would record the bandage as the NPC's hand item (SCore writes the held
        /// item's name). Put the weapon back and the item back in the inventory before the entity is written. Both of
        /// SCore's NPC classes write the held item that way, each in its own Write override.
        /// Only for a non-network write, which in practice is ScRunners' snapshot alone: chunk saves and unloads write
        /// every entity with bNetworkWrite true (vanilla Chunk.cs), and finishing a use there would run Unity calls on the
        /// save thread and end a heal on every spawn sent to a client. Those saves keep the use (ScAmmo.WriteHeldGun saves
        /// the displaced gun and the item) and the load undoes it (FixLoadedHand).
        /// </summary>
        [HarmonyPatch]
        public static class Patch_SaveMidUse
        {
#if SC_V34
            // 3.4 (SCore 3.4): Write(PooledBinaryWriter _bw, StreamModeWrite _eStreamMode) on both classes
            private static readonly Type[] Args = { typeof(PooledBinaryWriter), typeof(StreamModeWrite) };
#else
            private static readonly Type[] Args = { typeof(System.IO.BinaryWriter), typeof(bool) };
#endif

            [HarmonyPrepare]
            public static bool Prepare() { return ScNpc.Declared("Write", Args).Count > 0; }

            [HarmonyTargetMethods]
            public static IEnumerable<MethodBase> TargetMethods() { return ScNpc.Declared("Write", Args); }

            [HarmonyPrefix]
#if SC_V34
            public static void Prefix(EntityAlive __instance, StreamModeWrite _eStreamMode)
            {
                // 3.2's bNetworkWrite: everything but a Persistency write. 3.4's chunk saves and unloads, spawns sent to a
                // client and player data write ToClient (3.2's true); ScRunners' snapshot writes Persistency (3.2's false).
                bool bNetworkWrite = _eStreamMode != StreamModeWrite.Persistency;
#else
            public static void Prefix(EntityAlive __instance, bool bNetworkWrite)
            {
#endif
                try
                {
                    Use u;
                    if (bNetworkWrite || __instance == null || !uses.TryGetValue(__instance.entityId, out u)) return;
                    Finish(__instance, u, false, !__instance.IsDead());
                }
                catch (Exception ex)
                {
                    ScLog.Once("medic-save", ex);
                }
            }
        }
#if SC_V34

        // ---------------------------------------------------------------------------------------------------------
        // 0-XNPCCore 3.4's party medic (SdcsNpcMedic: MoveToPatientSdcs / FirstAidSdcs) on and around our bodies (D2)
        // ---------------------------------------------------------------------------------------------------------

        /// <summary>3.4: 0-XNPCCore's heal of another (SdcsMedic.ApplyHealOther(healer, patient, itemName, via), public static).</summary>
        public static MethodBase HealOtherTarget()
        {
            return AccessTools.DeclaredMethod(typeof(SdcsMedic), "ApplyHealOther", new[] { typeof(EntityAlive), typeof(EntityAlive), typeof(string), typeof(string) });
        }

        /// <summary>3.4: 0-XNPCCore's start of a first-aid sequence on another (SdcsMedic.Begin, public static).</summary>
        public static MethodBase MedicBeginTarget()
        {
            return AccessTools.DeclaredMethod(typeof(SdcsMedic), "Begin", new[] { typeof(EntityAlive), typeof(EntityAlive), typeof(NPCSdcsCharacters.Pure.MedicCondition), typeof(string) });
        }

        /// <summary>
        /// 3.4 (D2). A follower of ours that 0-XNPCCore made the party medic (its leader is its patient, or anyone with the
        /// medic role) spends one item out of its stores and fires the item's "other" effects on the patient
        /// (onSelfSecondaryActionEnd with the item in the event): the bleed stops, the item's health is queued
        /// (medicalRegHealthAmount), and the regen speed is set from the PATIENT's $medicRegHealthIncreaseSpeed - which only
        /// a player's Physician perk writes - while the perk-level fallbacks to 2 read the healer's progression, which an NPC
        /// has none of. On a player patient that is fine; on an NPC the speed stays 0 and buffHealHealth drains the queued
        /// health without healing a point (the hole ScMedic's own ApplyEffects plugs) - an item spent for nothing, and
        /// ScMedic's own heal held off meanwhile (a heal "working"). So when the patient is one of our bodies the speed gets
        /// NpcRegenSpeed (2, the no-perk value). Nothing in this path is refused by Patch_NoFreeHeal: it adds no
        /// SdcsIsHealing to anyone.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_XnpcHealOther
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                try
                {
                    if (HealOtherTarget() != null) return true;
                    ScLog.Warn("0-XNPCCore's SdcsMedic.ApplyHealOther not found - its party medic's heals on the community's bodies may not heal (0-XNPCCore update?)");
                }
                catch (Exception e) { ScLog.Once("medic-bind-healother", e); }
                return false;
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return HealOtherTarget(); }

            [HarmonyPostfix]
            public static void Postfix(EntityAlive patient, bool __result)
            {
                try
                {
                    if (!__result || patient == null || patient.Buffs == null || !IsBody(patient)) return;
                    if (patient.Buffs.GetCustomVar("medicalRegHealthAmount") <= 0f || patient.Buffs.GetCustomVar("medRegHealthIncSpeed") > 0f) return;
                    patient.Buffs.SetCustomVar("medRegHealthIncSpeed", NpcRegenSpeed);
                    xnpcHealSpeedFixed++;
                }
                catch (Exception ex)
                {
                    ScLog.Once("medic-healother", ex);
                }
            }
        }

        /// <summary>
        /// 3.4 (D2): 0-XNPCCore's party medic does not start on a body of ours in the middle of a use here. Its sequence
        /// puts the item in hand slot 0 (SdcsAnimPlayback.StartBandage) and puts back whatever was there when it began - the
        /// medical item this file holds - so the weapon this use displaced would be lost to the hand. The other order is
        /// TryHeal's SdcsAnimBusy wait. False out of Begin is XNPC's own refusal: the task stops and nothing is spent.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_XnpcMedicWaits
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                try
                {
                    if (MedicBeginTarget() != null) return true;
                    ScLog.Warn("0-XNPCCore's SdcsMedic.Begin not found - its party medic may start over a medical use of the community's own (0-XNPCCore update?)");
                }
                catch (Exception e) { ScLog.Once("medic-bind-begin", e); }
                return false;
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return MedicBeginTarget(); }

            [HarmonyPrefix]
            public static bool Prefix(EntityAlive healer, ref bool __result)
            {
                try
                {
                    if (healer == null || !uses.ContainsKey(healer.entityId)) return true;
                    xnpcMedicHeld++;
                    __result = false;
                    return false;
                }
                catch (Exception ex)
                {
                    ScLog.Once("medic-xnpc-begin", ex);
                    return true;
                }
            }
        }
#endif
    }
}
