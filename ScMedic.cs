using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Player-body survivors and raiders heal only by using a medical item they actually carry, and you see them do it.
    ///
    /// The NPC framework heals every one of its NPCs for free: its status buff (buffNPCStatusCheck, effect group
    /// NPCHealing) adds IsHealing whenever health is at or below 70% and a 15 s cooldown has run out, and IsHealing
    /// queues 30 health at 2 per second - no item, and no animation on this rig (it fires an animator trigger the
    /// player animator does not have). Measured on a raider: 50 to 82 health within a few seconds of a fight.
    /// That buff is refused here for our bodies; the framework's own NPCs keep it.
    ///
    /// Instead each body is stocked once with a few first aid bandages and, now and then, a first aid kit, in the
    /// inventory SCore shows for a friendly NPC (HarvestManager, saved with the world and dropped as a backpack on
    /// death) or, for a raider, the vanilla bag (dropped with its loot). When hurt and not already regenerating it
    /// uses one the way a player does: the item leaves the inventory and goes into the NPC's hand, the player's own
    /// third-person use animation plays (the rig picks it from the held item's hold type plus the UseItem trigger -
    /// 3P_Bandage_Hold and 3P_Bandage_Use for medical items, verified live), and when that animation finishes the
    /// item's onSelfPrimaryActionEnd effects fire on the NPC and the weapon goes back in the hand. Interrupted by a
    /// knockdown, the weapon comes back and the item returns to the inventory. You can also hand a survivor medical
    /// items and they will use those.
    /// </summary>
    public static class ScMedic
    {
        /// <summary>Items that restore health, best first. Plain bandages only stop bleeding, so they are not here.</summary>
        public static readonly string[] HealItems = { "medicalFirstAidKit", "medicalFirstAidBandage" };

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

        public static void Reset()
        {
            nextCheck.Clear();
            readyAt.Clear();
            uses.Clear();
        }

        public static bool IsBody(Entity e)
        {
            return e is EntityAlive && !(e is EntityPlayer) && e.emodel is EModelSDCS;
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
            if (!e.world.IsRemote() && e.Buffs.GetCustomVar(StockedCvar) <= 0f) Stock(e);
            TryHeal(e, now);
        }

        public static void Forget(int entityId)
        {
            nextCheck.Remove(entityId);
            readyAt.Remove(entityId);
            uses.Remove(entityId);
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

        private static void Add(EntityAlive e, string item, int count)
        {
            ItemValue iv = ItemClass.GetItem(item, true);
            if (iv == null || iv.IsEmpty()) return;
            var stack = new ItemStack(iv, count);
            if (e is EntityAliveSDX) HarvestManager.AddItem(e.entityId, stack);
            else
            {
                if (e.bag == null) e.bag = new Bag(45);
                e.bag.AddItem(stack);
            }
        }

        // ---------------------------------------------------------------------------------------------------------
        // decide
        // ---------------------------------------------------------------------------------------------------------

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
            u.Displaced = held != null ? held.Clone() : ItemStack.Empty.Clone();
            u.EndAt = now + u.Duration + TriggerTimeout;
            uses[e.entityId] = u;
            Hold(e, u.Slot, new ItemValue(ic.Id), 1);
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
        /// Puts an item in the hand slot and refreshes the hand model: the same sequence SCore's
        /// EntityAliveSDX.UpdateWeapon uses (SetItem, SwitchModelAndView, OnUpdate, ForceHoldingItemUpdate), without
        /// its bookkeeping - _currentWeapon and the CurrentWeaponID cvar keep naming the real weapon, so nothing
        /// re-equips a bandage later.
        /// </summary>
        private static void Hold(EntityAlive e, int slot, ItemValue item, int count)
        {
            if (e == null || e.inventory == null) return;
            e.inventory.SetItem(slot, item, count);
            EntityClass ec = EntityClass.list[e.entityClass];
            string meshName = ec.Properties.Values.ContainsKey("Mesh") ? ec.Properties.Values["Mesh"] : ec.entityClassName;
            if (e.emodel != null && e.emodel.avatarController != null)
                e.emodel.avatarController.SwitchModelAndView(meshName, e.emodel.IsFPV, e.IsMale);
            e.inventory.OnUpdate();
            e.inventory.ForceHoldingItemUpdate();
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
            if (e is EntityAliveSDX) return HarvestManager.GetOrCreate(e.entityId).GetItems();
            return e.bag != null ? e.bag.GetSlots() : null;
        }

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
                if (e is EntityAliveSDX) HarvestManager.GetOrCreate(e.entityId).UpdateSlot(i, left);
                else e.bag.SetSlot(i, left);
                return true;
            }
            return false;
        }

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
                    if (__instance == null || !(__instance.emodel is EModelSDCS)) return;
                    Tick(__instance);
                }
                catch (Exception ex)
                {
                    ScLog.Once("medic-tick", ex);
                }
            }
        }

        /// <summary>Refuses the framework's free IsHealing buff for our bodies (see the class summary).</summary>
        [HarmonyPatch(typeof(EntityBuffs), nameof(EntityBuffs.AddBuff),
            new[] { typeof(string), typeof(Vector3i), typeof(int), typeof(bool), typeof(bool), typeof(float) })]
        public static class Patch_NoFreeHeal
        {
            [HarmonyPrefix]
            public static bool Prefix(EntityBuffs __instance, string _name, ref EntityBuffs.BuffStatus __result)
            {
                if (_name == null || !string.Equals(_name, "IsHealing", StringComparison.OrdinalIgnoreCase)) return true;
                if (__instance == null || !IsBody(__instance.parent)) return true;
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

        /// <summary>No attacking with a bandage: SCore's own attack path for EntityAliveSDX.</summary>
        [HarmonyPatch(typeof(EntityAliveSDX), nameof(EntityAliveSDX.ExecuteAction))]
        public static class Patch_NoSdxAttackWhileUsing
        {
            [HarmonyPrefix]
            public static bool Prefix(EntityAliveSDX __instance, ref bool __result)
            {
                if (__instance == null || !uses.ContainsKey(__instance.entityId)) return true;
                __result = false;
                return false;
            }
        }

        /// <summary>
        /// A world save in the middle of a use would record the bandage as the NPC's hand item (SCore writes the held
        /// item's name). Put the weapon back and the item back in the inventory before the entity is written.
        /// </summary>
        [HarmonyPatch(typeof(EntityAliveSDX), nameof(EntityAliveSDX.Write))]
        public static class Patch_SaveMidUse
        {
            [HarmonyPrefix]
            public static void Prefix(EntityAliveSDX __instance, bool bNetworkWrite)
            {
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
    }
}
