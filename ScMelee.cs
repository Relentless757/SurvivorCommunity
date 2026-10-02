using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UAI;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// How fast and from how far our XNPC bodies swing a melee weapon (the M11 fix 2, MELEE_DIAG causes 1 and 5 and the
    /// 1.5 m reach fallback; its fixes F1 and F4). Survivors - members and strangers - and, with MeleePacingRaiders, SC's
    /// raiders (the user's decision: on).
    ///
    /// Pace (F1). SCore's attack task (UAITaskAttackTargetEntitySDX.Update; its V4 sibling is the same) counts
    /// attackTimeout down once per AI update and, at 0, re-arms it with GetAttackTimeoutTicks() BEFORE it tries
    /// Attack(false) / Attack(true): 20 ticks, vanilla's AttackTimeoutDay/Night default of 1 s, which npcSdcsBase never
    /// sets. The weapon has a clock of its own - vanilla's ItemActionDynamicMelee.canStartAttack refuses until
    /// 60 / AttacksPerMinute + 0.1 s after the last swing (the T1 bat: 52 APM, 1.254 s) - and a refusal still counts as a
    /// try (UseHoldingItem returns true), so the task swung on every other tick: one swing every 2.0 s with a weapon that
    /// allows one every 1.25 s, and a full second more after every restart (a new target, an approach/attack flip).
    /// The task's Update prefix (Patches_Npc.Patch_HoldFireAcross, on the aim stamp's two targets, so no new target) now
    /// sets that countdown from the weapon's own clock: to 1 - try on this update - once the weapon is ready, and never
    /// below 2 while it is not, so no try is spent on a refusal. The facing gates run before the countdown, so it still
    /// swings only at a target in front of it. The clock is the weapon's lastUseTime in real time, so vanilla's AI level
    /// of detail (an NPC away from the player updates every 3 or 10 ticks) no longer multiplies the interval, and a restart
    /// costs nothing: lastUseTime lives on the weapon, so its cycle carries over to the next target.
    /// Nothing can double-fire: one swing per weapon cycle is vanilla's own gate, the same canStartAttack the player's
    /// swing passes. The animation keeps up by design: AnimatorMeleeAttackState plays the clip at the weapon's
    /// AttacksPerMinute, and the hit is its timed Raycast, unchanged. Stamina is not a factor: vanilla drains it only from
    /// a player (Raycast and GrazeCast test EntityPlayerLocal), and the NPCs' attack is action 0, never the power attack.
    /// A try the weapon still refuses (a hit stun, a broken or disabled weapon, no stamina) is retried after RetryGap, not
    /// on every AI update: a refused try plays the entity's attack sound on its release.
    /// Guns keep SCore's own pace: their path is the task's IsActionRunning gate plus the 1 s countdown (a T1 pistol shoots
    /// about once a second), which the damage and ammo balance were tuned on.
    ///
    /// Reach (F4). SCore's InWeaponRange (MeleeAttack's gate, and MeleeApproach's through NotInWeaponRange, which calls it
    /// as its base) reads Actions[i].Range, the base ItemAction field - verified in the deployed SCore.dll, 95b0dbb4:
    /// ldfld ItemAction::Range - which only ItemActionAttack.ReadFrom writes. ItemActionDynamic keeps its reach in a field
    /// of its own and the MaxRange passive (the bat: 2.4 m), so SCore read 0 and fell back to 1.5 m: the NPC walked into a
    /// zombie's own reach before it swung, and every knock-back past 1.5 m flipped it back to the approach (a task restart).
    /// For our bodies, a postfix on that GetScore (one new target: the field is shared by every holder of the item, so
    /// writing it would change every other mod's NPCs too) answers with the weapon's real reach less MeleeReachMargin -
    /// about 2.0 m for the bat - so the swing's ray, from the head to the target's middle, still meets a target that steps
    /// back a little. SC's own melee checks (ScEngage, the console's probe) read the same Reach.
    /// </summary>
    public static class ScMelee
    {
        /// <summary>After a try the weapon refused, the next one waits this long (a refused try plays the attack sound).</summary>
        private const float RetryGap = 0.25f;
        /// <summary>
        /// A margin past canStartAttack's own gate (60 / APM + 0.1 s): the try lands on the first AI update after the gate
        /// opens, never on one the gate still refuses over a float rounding difference.
        /// </summary>
        private const float ReadySlack = 0.02f;
        /// <summary>SCore's InWeaponRange fallback when the action's Range field is 0 (and the floor of SC's answer).</summary>
        public const float SCoreFallbackReach = 1.5f;

        private sealed class PaceState
        {
            public float RetryAfter;
            public float Seen;
        }

        private static readonly Dictionary<int, PaceState> paces = new Dictionary<int, PaceState>();
        private static float nextPrune;
        /// <summary>Counters for sc xnpc: updates cleared to try, swings, refused tries, reach verdicts and how many SC turned to "in reach".</summary>
        private static int cleared, swings, refused, reachCalls, reachLonger;

        public static void Reset()
        {
            paces.Clear();
            nextPrune = 0f;
            cleared = swings = refused = reachCalls = reachLonger = 0;
            reachMemo.Clear();
            Patch_InWeaponRange.Reset();
        }

        /// <summary>A body whose melee SC paces: one of ours, on the host, and for a raider only with MeleePacingRaiders.</summary>
        private static bool Paced(EntityAlive e)
        {
            if (!ScConfig.MeleePacing || e == null || e.isEntityRemote || !ScXnpc.IsBody(e)) return false;
            return ScConfig.MeleePacingRaiders || !ScXnpc.IsRaider(e);
        }

        /// <summary>The held item's melee action data at this index (0 or 1: SCore's attack task fires those through UseHoldingItem), or null.</summary>
        private static ItemActionDynamicMelee.ItemActionDynamicMeleeData MeleeData(EntityAlive e, int index)
        {
            ItemInventoryData hd = e.inventory != null ? e.inventory.holdingItemData : null;
            if (hd == null || hd.actionData == null || index < 0 || index > 1 || index >= hd.actionData.Count) return null;
            return hd.actionData[index] as ItemActionDynamicMelee.ItemActionDynamicMeleeData;
        }

        /// <summary>Seconds from one swing to the next as canStartAttack computes them (its tags, its entity), plus ReadySlack.</summary>
        private static float Cycle(ItemActionDynamicMelee.ItemActionDynamicMeleeData md, EntityAlive e, int index)
        {
            FastTags<TagGroup.Global> tags = index == 0 ? ItemActionAttack.PrimaryTag : ItemActionAttack.SecondaryTag;
            ItemClass ic = md.invData.itemValue != null ? md.invData.itemValue.ItemClass : null;
            if (ic != null) tags |= ic.ItemTags;
            float apm = EffectManager.GetValue(PassiveEffects.AttacksPerMinute, md.invData.itemValue, 60f, e, null, tags);
            if (apm == 0f) apm = 0.0001f;
            return 60f / apm + 0.1f + ReadySlack;
        }

        private static PaceState State(int id, float now)
        {
            PaceState st;
            if (!paces.TryGetValue(id, out st)) paces[id] = st = new PaceState();
            st.Seen = now;
            if (now >= nextPrune)
            {
                nextPrune = now + 30f;
                var gone = new List<int>();
                foreach (var kv in paces) if (now - kv.Value.Seen > 60f) gone.Add(kv.Key);
                foreach (int g in gone) paces.Remove(g);
            }
            return st;
        }

        /// <summary>
        /// The attack task's Update prefix, for an action that does not shoot. Returns the weapon's lastUseTime when this
        /// update is cleared to try (the postfix, AfterUpdate, then sees whether it swung), else -1.
        /// </summary>
        internal static float BeforeUpdate(UAITaskAttackTargetEntity attack, EntityAlive self, int index)
        {
            if (attack == null || !Paced(self)) return -1f;
            ItemActionDynamicMelee.ItemActionDynamicMeleeData md = MeleeData(self, index);
            if (md == null) return -1f;
            float now = Time.time;
            PaceState st = State(self.entityId, now);
            if (now - md.lastUseTime >= Cycle(md, self, index) && now >= st.RetryAfter)
            {
                if (attack.attackTimeout > 1) attack.attackTimeout = 1;     // the countdown reaches 0 on this update
                cleared++;
                return Mathf.Max(0f, md.lastUseTime);
            }
            // not ready yet: SCore's own countdown would spend its try on a refusal and re-arm a full second
            if (attack.attackTimeout < 2) attack.attackTimeout = 2;
            return -1f;
        }

        /// <summary>
        /// The same Update's postfix after BeforeUpdate cleared it: a swing (lastUseTime moved), no try at all (a gate
        /// returned first: the facing gates leave the countdown at 5 or 15, airborne or SCore's throttle buff at 1), or a
        /// try the weapon refused (the countdown was re-armed to GetAttackTimeoutTicks() and nothing swung).
        /// </summary>
        internal static void AfterUpdate(UAITaskAttackTargetEntity attack, EntityAlive self, int index, float useBefore)
        {
            if (attack == null || self == null) return;
            ItemActionDynamicMelee.ItemActionDynamicMeleeData md = MeleeData(self, index);
            if (md == null) return;
            if (md.lastUseTime != useBefore) { swings++; return; }
            if (attack.attackTimeout != self.GetAttackTimeoutTicks()) return;
            refused++;
            PaceState st;
            if (paces.TryGetValue(self.entityId, out st)) st.RetryAfter = Time.time + RetryGap;
        }

        // ------------------------------------------------------------------ reach (F4) ----

        /// <summary>One body's reach, kept for the frame: GetScore runs for every candidate of two actions each decision.</summary>
        private struct ReachMemo
        {
            public int Frame, ItemType;
            public float Reach, Weapon;
        }

        private static readonly Dictionary<int, ReachMemo> reachMemo = new Dictionary<int, ReachMemo>();

        /// <summary>
        /// The melee reach SC gives SCore's in-range test for this body's action, or -1 when SCore's own verdict stands:
        /// the switch off, not one of our bodies, not a dynamic (melee) action, or one whose Range field a mod did set (SCore
        /// reads that). weapon = the weapon's own reach (its MaxRange), -1 when none. Never below SCore's 1.5 m, unless the
        /// weapon itself reaches less (the bare hand: 1.65 m).
        /// </summary>
        public static float Reach(EntityAlive e, int index, out float weapon)
        {
            weapon = -1f;
            if (!ScConfig.MeleeReach || e == null || !ScXnpc.IsBody(e)) return -1f;
            ItemClass held = e.inventory != null ? e.inventory.holdingItem : null;
            ItemAction[] acts = held != null ? held.Actions : null;
            if (acts == null || index < 0 || index >= acts.Length) return -1f;
            ItemActionDynamic dyn = acts[index] as ItemActionDynamic;
            if (dyn == null || ((ItemAction)dyn).Range != 0f) return -1f;
            int frame = Time.frameCount, key = e.entityId * 2 + index;
            ReachMemo memo;
            if (reachMemo.TryGetValue(key, out memo) && memo.Frame == frame && memo.ItemType == held.Id) { weapon = memo.Weapon; return memo.Reach; }
            if (reachMemo.Count > 256) reachMemo.Clear();
            // the tags and default ItemActionDynamic.GetExecuteActionTarget computes the swing's own Range with
            ItemInventoryData hd = e.inventory.holdingItemData;
            ItemActionData ad = hd != null && hd.actionData != null && index < hd.actionData.Count ? hd.actionData[index] : null;
            FastTags<TagGroup.Global> tags = (ad != null ? ad.ActionTags : (index == 0 ? ItemActionAttack.PrimaryTag : ItemActionAttack.SecondaryTag)) | held.ItemTags;
            weapon = EffectManager.GetValue(PassiveEffects.MaxRange, e.inventory.holdingItemItemValue, dyn.RangeDefault, e, null, tags);
            float reach = Mathf.Max(weapon - ScConfig.MeleeReachMargin, Mathf.Min(SCoreFallbackReach, weapon));
            reachMemo[key] = new ReachMemo { Frame = frame, ItemType = held.Id, Reach = reach, Weapon = weapon };
            return reach;
        }

        /// <summary>SCore's InWeaponRange.GetScore (SCore 3.2.36): the consideration XNPC's MeleeAttack and MeleeApproach ask.</summary>
        public static MethodBase InWeaponRangeTarget()
        {
            return AccessTools.DeclaredMethod(typeof(UAIConsiderationInWeaponRange), "GetScore", new[] { typeof(Context), typeof(object) });
        }

        /// <summary>
        /// F4: the in-range verdict with the weapon's real reach for our bodies' melee actions. NotInWeaponRange derives from
        /// it and calls this GetScore as its base, so the approach follows. Bound by Prepare (exact signature and SCore's
        /// private _actionIndex, fail-loud), never throwing out of it; ModApi's ExpectedTargets lists it. SCore's own verdict
        /// stands for guns, for every other NPC and whenever Reach declines.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_InWeaponRange
        {
            /// <summary>
            /// SCore's private _actionIndex. A plain FieldInfo, read once per consideration (one per action in the XML) and
            /// kept: nothing is emitted, so the offline patch checks can run Prepare outside the game, as ScArms' bindings.
            /// </summary>
            private static FieldInfo actionIndexField;
            private static readonly Dictionary<UAIConsiderationInWeaponRange, int> actionIndex = new Dictionary<UAIConsiderationInWeaponRange, int>();

            /// <summary>Per world: the XML is loaded again, with new consideration objects.</summary>
            internal static void Reset() { actionIndex.Clear(); }

            [HarmonyPrepare]
            public static bool Prepare()
            {
                try
                {
                    if (InWeaponRangeTarget() == null)
                    {
                        ScLog.Err("UAIConsiderationInWeaponRange.GetScore(Context, object) not found (SCore update?) - survivors judge their melee reach as 1.5 m again");
                        return false;
                    }
                    FieldInfo f = AccessTools.DeclaredField(typeof(UAIConsiderationInWeaponRange), "_actionIndex");
                    if (f == null || f.IsStatic || f.FieldType != typeof(int))
                    {
                        ScLog.Err("UAIConsiderationInWeaponRange._actionIndex not found (SCore update?) - survivors judge their melee reach as 1.5 m again");
                        return false;
                    }
                    actionIndexField = f;
                    return true;
                }
                catch (Exception e)
                {
                    ScLog.Once("melee-reach-bind", e);
                    return false;
                }
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return InWeaponRangeTarget(); }

            private static int ActionIndexOf(UAIConsiderationInWeaponRange c)
            {
                int i;
                if (!actionIndex.TryGetValue(c, out i)) actionIndex[c] = i = (int)actionIndexField.GetValue(c);
                return i;
            }

            [HarmonyPostfix]
            public static void Postfix(UAIConsiderationInWeaponRange __instance, Context context, object target, ref float __result)
            {
                try
                {
                    EntityAlive self = context != null ? context.Self : null;
                    if (self == null || actionIndexField == null || __instance == null || self.isEntityRemote || !ScConfig.MeleeReach || !ScXnpc.IsBody(self)) return;
                    float weapon;
                    float reach = Reach(self, ActionIndexOf(__instance), out weapon);
                    if (reach < 0f) return;
                    float dist;
                    EntityAlive t = UAIUtils.ConvertToEntityAlive(target);
                    if (t != null) dist = self.GetDistance(t);
                    else if (target is Vector3) dist = (self.position - (Vector3)target).magnitude;
                    else return;
                    float was = __result;
                    __result = dist <= reach ? 1f : 0f;
                    reachCalls++;
                    if (__result > was) reachLonger++;
                }
                catch (Exception ex)
                {
                    ScLog.Once("melee-reach", ex);
                }
            }
        }

        // ------------------------------------------------------------------ diagnostics ----

        public static string Report()
        {
            return "pacing " + (ScConfig.MeleePacing ? (ScConfig.MeleePacingRaiders ? "on (raiders too)" : "on (not raiders)") : "off")
                   + ": " + cleared + " update(s) cleared to swing, " + swings + " swing(s), " + refused + " refused by the weapon"
                   + "; reach " + (ScConfig.MeleeReach ? "weapon - " + ScConfig.MeleeReachMargin.ToString("0.0#") + " m" : "off (SCore's 1.5 m)")
                   + ": " + reachCalls + " verdict(s), " + reachLonger + " in reach only by the weapon's real reach";
        }

        /// <summary>
        /// One body's melee as SC paces and measures it, for sc xnpc &lt;id&gt;'s uai line: the reach the AI judges by (SC's,
        /// else scoreReach, SCore's own reading of the action) and the swing clock.
        /// </summary>
        public static string Describe(EntityAlive e, float dist, float scoreReach)
        {
            float weapon;
            float reach = Reach(e, 0, out weapon);
            string s = reach >= 0f
                ? "melee reach=" + reach.ToString("0.0#") + " (the weapon's " + weapon.ToString("0.0#") + " m less " + ScConfig.MeleeReachMargin.ToString("0.0#")
                  + "; SCore alone: " + scoreReach.ToString("0.0#") + ") InWeaponRange=" + (dist <= reach)
                : "melee reach=" + scoreReach.ToString("0.0#") + " (SCore's) InWeaponRange=" + (dist <= scoreReach);
            ItemActionDynamicMelee.ItemActionDynamicMeleeData md = e.inventory != null && !e.isEntityRemote ? MeleeData(e, 0) : null;
            if (md != null)
                s += " swing every " + Cycle(md, e, 0).ToString("0.00") + " s, last " + (Time.time - md.lastUseTime).ToString("0.0") + " s ago, paced=" + Paced(e);
            return s;
        }
    }
}
