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
    /// items and they will use those.
    ///
    /// Decision D7: items only, with the bandage animation. XNPC heals its own bodies through a different buff,
    /// SdcsIsHealing (added by its class buff SdcsStatusCheck: for free with no leader, from a medical item without an
    /// animation when hired), which is refused for ours like IsHealing. Their medical stock is this file's roll alone
    /// (Config/entityclasses.xml sets HarvestItems to none on the survivor classes and on the raider templates). While a
    /// use is in progress XNPC's own weapon logic is held off (Patches_Xnpc X5), so its failover or close-range swap
    /// cannot take the bandage out of the hand.
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

        /// <summary>A medical use is in progress (ScRunners skips its periodic snapshot - a Write(false) would finish the use).</summary>
        public static bool IsUsing(int id) { return uses.ContainsKey(id); }

        public static void Reset()
        {
            nextCheck.Clear();
            readyAt.Clear();
            uses.Clear();
            Patch_NoFreeHeal.SdcsRefused = 0;
        }

        /// <summary>For sc xnpc: the stock flag, whether a use is in progress, and how often XNPC's own heal was refused.</summary>
        public static string Describe(EntityAlive e)
        {
            if (e == null || e.Buffs == null) return "-";
            Use u;
            string use = uses.TryGetValue(e.entityId, out u) ? "using " + u.Item + (u.Triggered ? " (animation started)" : " (waiting for the hold pose)") : "idle";
            return "stocked=" + (e.Buffs.GetCustomVar(StockedCvar) > 0f) + " " + use + " bandages=" + Count(e, "medicalFirstAidBandage")
                   + " kits=" + Count(e, "medicalFirstAidKit") + " SdcsIsHealing refused (all bodies)=" + Patch_NoFreeHeal.SdcsRefused;
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
            ScNpc.AddItem(e, new ItemStack(iv, count));
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
            return ScNpc.Items(e);
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
                ScNpc.SetItem(e, i, left);
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
        /// </summary>
        [HarmonyPatch]
        public static class Patch_SaveMidUse
        {
            private static readonly Type[] Args = { typeof(System.IO.BinaryWriter), typeof(bool) };

            [HarmonyPrepare]
            public static bool Prepare() { return ScNpc.Declared("Write", Args).Count > 0; }

            [HarmonyTargetMethods]
            public static IEnumerable<MethodBase> TargetMethods() { return ScNpc.Declared("Write", Args); }

            [HarmonyPrefix]
            public static void Prefix(EntityAlive __instance, bool bNetworkWrite)
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
