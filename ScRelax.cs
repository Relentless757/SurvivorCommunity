using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// "Relaxed": a body of ours that is out of danger lowers its arms and hides its weapon without putting it away (the
    /// user's holster request, 2026-10-03, replacing M7.6's deferred holster; decisions: the NPC's own sight range with
    /// awake enemies only, survivors and raiders alike, every kind of weapon). The weapon stays the held item the whole
    /// time - XNPC's weapon logic, ammo, swaps and the AI never see a difference - only the animator and the renderers do.
    ///
    /// The pose. The player animator these bodies run picks its arm pose from two int parameters: WeaponHoldType (the
    /// hold-pose layers 10-14 and 20, each of whose states leaves for Empty on [WeaponHoldType != its own]) and WeaponCarry
    /// (the idle, crouch and locomotion carry styles on layers 0, 4 and 5). Both at 0 is the player's bare-hands body, arms
    /// swinging when it walks. LegacyAvatarController.Update writes WeaponHoldType from the held item every frame through
    /// _setInt, so a prefix there (HoldTypeTarget) turns the value into 0 for a relaxed body - one write per change, never
    /// a per-frame fight or a network message per frame. WeaponCarry is written by 0-XNPCCore (straight to the animator,
    /// only when its own value changes) and re-asserted by ScAim.KeepCarry every frame, which writes 0 while relaxed.
    ///
    /// The weapon. Every renderer under the held item's model (Inventory.GetHoldingItemTransform) gets
    /// Renderer.forceRenderingOff while relaxed - nothing is deactivated, so the gun's own scripts, muzzle and attachments
    /// keep running. Only renderers that were rendering are touched and only those are restored; a model swapped while
    /// relaxed is restored before the new one is hidden, and the hidden set is re-scanned every couple of seconds for parts
    /// added later. Raising restores them in the same frame as the hold pose comes back.
    ///
    /// Who decides. The entity's owner (the host; !isEntityRemote) checks every CheckSeconds: an alert reason - knocked
    /// down, an attack or revenge target, or an awake living enemy (SCore's IsEnemy) within the NPC's sight range
    /// (XnpcRelaxRange when above 0) - raises it at once; XnpcRelaxCalmSeconds without one lowers it. The answer is the
    /// cvar sc_relaxed, sent to clients (an NPC's cvar only reaches them with forceSendToClients). Every machine applies the
    /// cvar to its own copy each frame, together with its own checks that need no AI (alive, a weapon in hand, no item use
    /// running, no reload requested or running and no empty magazine it could load, XnpcRelaxRaiders). The weapon comes up
    /// before anything that needs it: SCore's attack task starting (ScAim's Start postfix), every swing or shot
    /// (EntityAlive.UseHoldingItem, the path SCore's task fires through) and every reload (the avatar's
    /// StartAnimationReloading, on the owner; clients follow its cvar) - each keeps it up for AlertHoldSeconds whatever
    /// the cvar says.
    ///
    /// Main thread only: Patches_Xnpc X7 (the controller's Update), the controller's _setInt and StartAnimationReloading,
    /// UseHoldingItem / ExecuteAction, the attack task's Start and the model's OnDestroy.
    /// </summary>
    public static class ScRelax
    {
        /// <summary>The host's verdict, 1 = relaxed. Saved with the body; re-checked within CheckSeconds of loading.</summary>
        public const string Cvar = "sc_relaxed";
        private const float CheckSeconds = 0.5f;
        private const float RescanSeconds = 2f;

        /// <summary>After an attack or a reload starts, the body stays raised this long whatever the cvar says (a client's cvar lags the host).</summary>
        private const float AlertHoldSeconds = 1f;

        private sealed class State
        {
            public float NextCheck;
            public float AlertUntil;
            public float CalmSince = -1f;
            public string Why = "not checked yet";
            public Transform Hidden;
            public readonly List<Renderer> Off = new List<Renderer>();
            public float NextRescan;
        }

        /// <summary>Bodies relaxed on this machine (the hold-type prefix's whole test, so it stays one set lookup).</summary>
        private static readonly HashSet<int> relaxed = new HashSet<int>();
        private static readonly Dictionary<int, State> states = new Dictionary<int, State>();
        private static readonly List<Entity> near = new List<Entity>();
        private static readonly List<Renderer> scan = new List<Renderer>();
        private static int lowered, raised, raisedByAttack, raisedByReload, hiddenRenderers;

        public static void Reset()
        {
            relaxed.Clear();
            states.Clear();
            lowered = raised = raisedByAttack = raisedByReload = hiddenRenderers = 0;
        }

        public static bool IsRelaxed(EntityAlive e)
        {
            return relaxed.Count > 0 && (object)e != null && relaxed.Contains(e.entityId);
        }

        public static string Report()
        {
            return (ScConfig.XnpcRelax ? "on" : "off") + ", " + relaxed.Count + " relaxed now; lowered " + lowered + ", raised " + raised
                   + " (" + raisedByAttack + " by an attack starting, " + raisedByReload + " by a reload starting), " + hiddenRenderers
                   + " weapon renderer(s) hidden";
        }

        public static string Describe(EntityAlive e)
        {
            State st;
            states.TryGetValue(e.entityId, out st);
            float calm = st != null && st.CalmSince >= 0f ? Time.time - st.CalmSince : -1f;
            return "relax: " + (IsRelaxed(e) ? "RELAXED (arms down" + (st != null && st.Off.Count > 0 ? ", " + st.Off.Count + " weapon renderer(s) hidden" : "") + ")" : "alert")
                   + ", cvar " + (e.Buffs != null ? e.Buffs.GetCustomVar(Cvar).ToString("0", CultureInfo.InvariantCulture) : "?")
                   + (st != null ? ", " + st.Why : "")
                   + (calm >= 0f ? ", calm for " + calm.ToString("0.0", CultureInfo.InvariantCulture) + " s of " + ScConfig.XnpcRelaxCalmSeconds : "")
                   + ", range " + Range(e).ToString("0", CultureInfo.InvariantCulture) + " m";
        }

        /// <summary>From Patch_OnDestroy: the body and its model are gone with their renderers.</summary>
        public static void Forget(int entityId)
        {
            if (states.Count == 0 && relaxed.Count == 0) return;
            states.Remove(entityId);
            relaxed.Remove(entityId);
        }

        /// <summary>Every frame for every body of ours on every machine, from Patches_Xnpc X7, before ScAim.KeepCarry.</summary>
        public static void Tick(AvatarSDCSController c, EntityAlive e)
        {
            State st;
            if (!states.TryGetValue(e.entityId, out st))
            {
                if (!ScConfig.XnpcRelax) return;
                states[e.entityId] = st = new State();
            }
            float now = Time.time;
            if (ScConfig.XnpcRelax && !e.isEntityRemote && now >= st.NextCheck)
            {
                st.NextCheck = now + CheckSeconds;
                Decide(e, st, now);
            }
            bool want = ScConfig.XnpcRelax && now >= st.AlertUntil && e.Buffs != null && e.Buffs.GetCustomVar(Cvar) > 0f
                        && Allowed(e) == null;
            bool was = relaxed.Contains(e.entityId);
            if (want && !was) Lower(c, e, st);
            else if (!want && was) Raise(c, e, st);
            if (want && ScConfig.XnpcRelaxHideWeapon) KeepHidden(e, st, now);
            else if (st.Off.Count > 0) Restore(st);
        }

        /// <summary>
        /// From the attack and reload hooks: an attack (SCore's task starting, or its swing or shot through UseHoldingItem)
        /// or a reload animation is starting, so the weapon comes up now, before it. The calm clock restarts, the body stays
        /// raised for AlertHoldSeconds whatever the (possibly lagging) cvar says, and on the host the cvar goes to 0.
        /// </summary>
        public static void OnAlert(EntityAlive e, string why, bool reload)
        {
            State st;
            if (!states.TryGetValue(e.entityId, out st)) return;
            st.CalmSince = -1f;
            st.AlertUntil = Time.time + AlertHoldSeconds;
            st.Why = why;
            if (!e.isEntityRemote) Publish(e, 0f);
            if (!relaxed.Contains(e.entityId)) return;
            if (reload) raisedByReload++; else raisedByAttack++;
            AvatarSDCSController c = e.emodel != null ? e.emodel.avatarController as AvatarSDCSController : null;
            if (c == null) { relaxed.Remove(e.entityId); Restore(st); raised++; return; }
            Raise(c, e, st);
        }

        // ---- the host's verdict ----

        private static void Decide(EntityAlive e, State st, float now)
        {
            string why = Alert(e);
            if (why != null)
            {
                st.CalmSince = -1f;
                st.Why = why;
                Publish(e, 0f);
                return;
            }
            if (st.CalmSince < 0f) st.CalmSince = now;
            st.Why = "calm";
            if (now - st.CalmSince >= ScConfig.XnpcRelaxCalmSeconds) Publish(e, 1f);
        }

        private static void Publish(EntityAlive e, float v)
        {
            if (e.Buffs == null || e.Buffs.GetCustomVar(Cvar) == v) return;
            e.Buffs.SetCustomVar(Cvar, v, true, CVarOperation.set, true);
        }

        /// <summary>Why this body must keep its weapon up, or null when it may relax.</summary>
        private static string Alert(EntityAlive e)
        {
            string no = Allowed(e);
            if (no != null) return no;
            if (e.emodel == null || e.emodel.IsRagdollActive) return "knocked down";
            if (e.bodyDamage.CurrentStun != EnumEntityStunType.None) return "stunned";
            EntityAlive t = e.GetAttackTarget();
            if (t != null && t != e && !t.IsDead()) return "fighting #" + t.entityId;
            t = e.GetRevengeTarget();
            if (t != null && t != e && !t.IsDead()) return "hurt by #" + t.entityId;
            return EnemyInRange(e);
        }

        /// <summary>
        /// The checks every machine can make on its own copy: alive, a weapon in hand (an attack action on Action0 or 1 -
        /// guns, launchers, bows, melee and dynamic melee; never a bandage, food or bare hands), no item use running, and a
        /// raider only with XnpcRelaxRaiders. Null when it may relax.
        /// </summary>
        private static string Allowed(EntityAlive e)
        {
            if (e.IsDead()) return "dead";
            if (ScXnpc.IsRaider(e) && !ScConfig.XnpcRelaxRaiders) return "raider (XnpcRelaxRaiders off)";
            ItemClass held = e.inventory != null ? e.inventory.holdingItem : null;
            if (held == null || held.HoldType == null || held.HoldType.Value == 0) return "empty hands";
            ItemAction[] acts = held.Actions;
            bool weapon = acts != null && ((acts.Length > 0 && IsAttack(acts[0])) || (acts.Length > 1 && IsAttack(acts[1])));
            if (!weapon) return "not holding a weapon";
            if (ScMedic.IsUsingItem(e)) return "using an item";
            return Reloading(e, held, acts);
        }

        /// <summary>
        /// The reload rule (Fable review HIGH-2): the reload clips that set isReloading and call CompleteReload
        /// (AnimatorRangedReloadState on layers 18/19) are entered only on [WeaponHoldType == n and Reload], so a body
        /// relaxed to hold type 0 could never reload, and 0-XNPCCore requests its empty-magazine preload exactly when the
        /// NPC is calm (WeaponReadyRules.ShouldRequestReload: Meta 0, CanReload, not reloading, not requested). So a body
        /// with a reload requested or running (any ranged action's data - the fields XNPC's SdcsReloading cvar is derived
        /// from; the cvar itself is not read, because a save made mid-reload can leave it at 1 for good), or whose ranged
        /// weapon is empty and can be reloaded - XNPC's own preload condition - stays up until the gun is loaded.
        /// </summary>
        private static string Reloading(EntityAlive e, ItemClass held, ItemAction[] acts)
        {
            ItemInventoryData inv = e.inventory != null ? e.inventory.holdingItemData : null;
            if (inv == null || inv.actionData == null) return null;
            bool empty = e.inventory.holdingItemItemValue.Meta == 0;
            for (int i = 0; i < acts.Length && i < inv.actionData.Count; i++)
            {
                var data = inv.actionData[i] as ItemActionRanged.ItemActionDataRanged;
                ItemActionRanged ranged = acts[i] as ItemActionRanged;
                if (data == null || ranged == null) continue;
                if (data.isReloading || data.isReloadRequested) return "reloading";
                if (empty && ranged.CanReload(data)) return "magazine empty (loading it first)";
            }
            return null;
        }

        private static bool IsAttack(ItemAction a)
        {
            return a is ItemActionAttack || a is ItemActionDynamic;
        }

        /// <summary>The NPC's own sight range (its class's SightRange), or XnpcRelaxRange when that is above 0.</summary>
        private static float Range(EntityAlive e)
        {
            if (ScConfig.XnpcRelaxRange > 0f) return ScConfig.XnpcRelaxRange;
            return Mathf.Max(e.sightRangeBase, 1f);
        }

        /// <summary>An awake, living enemy of this NPC (SCore's IsEnemy) within its range, as a reason; null when none.</summary>
        private static string EnemyInRange(EntityAlive e)
        {
            World w = e.world;
            if (w == null) return null;
            float range = Range(e);
            near.Clear();
            w.GetEntitiesInBounds(typeof(EntityAlive), new Bounds(e.position, new Vector3(2f * range, 2f * range, 2f * range)), near);
            float r2 = range * range;
            string found = null;
            for (int i = 0; i < near.Count && found == null; i++)
            {
                EntityAlive a = near[i] as EntityAlive;
                if (a == null || a == e || a.IsDead() || a.IsSleeping) continue;
                float d2 = (a.position - e.position).sqrMagnitude;
                if (d2 > r2 || !EntityTargetingUtilities.IsEnemy(e, a)) continue;
                found = "enemy " + ScCommunity.ClassName(a) + " #" + a.entityId + " at "
                        + Mathf.Sqrt(d2).ToString("0", CultureInfo.InvariantCulture) + " m";
            }
            near.Clear();
            return found;
        }

        // ---- applying it to this machine's copy ----

        private static void Lower(AvatarSDCSController c, EntityAlive e, State st)
        {
            relaxed.Add(e.entityId);
            lowered++;
            // the hold-type prefix makes the controller's own write 0 from now on; write it now for this frame
            if (c.anim != null) c._setInt(AvatarController.weaponHoldTypeHash, 0);
        }

        private static void Raise(AvatarSDCSController c, EntityAlive e, State st)
        {
            relaxed.Remove(e.entityId);
            raised++;
            Restore(st);
            ItemClass held = e.inventory != null ? e.inventory.holdingItem : null;
            if (c.anim != null && held != null && held.HoldType != null) c._setInt(AvatarController.weaponHoldTypeHash, held.HoldType.Value);
            // WeaponCarry comes back from ScAim.KeepCarry in the same X7 call
        }

        private static void KeepHidden(EntityAlive e, State st, float now)
        {
            Transform model = e.inventory != null ? e.inventory.GetHoldingItemTransform() : null;
            if (model == null)
            {
                if (st.Off.Count > 0) Restore(st);
                return;
            }
            if ((object)model == (object)st.Hidden && now < st.NextRescan) return;
            if ((object)model != (object)st.Hidden) Restore(st);
            st.Hidden = model;
            st.NextRescan = now + RescanSeconds;
            scan.Clear();
            model.GetComponentsInChildren(true, scan);
            for (int i = 0; i < scan.Count; i++)
            {
                Renderer r = scan[i];
                if (r == null || r.forceRenderingOff || st.Off.Contains(r)) continue;
                r.forceRenderingOff = true;
                st.Off.Add(r);
                hiddenRenderers++;
            }
            scan.Clear();
        }

        private static void Restore(State st)
        {
            for (int i = 0; i < st.Off.Count; i++)
            {
                Renderer r = st.Off[i];
                if (r != null) r.forceRenderingOff = false;
            }
            st.Off.Clear();
            st.Hidden = null;
        }

        // ---- patches ----

        /// <summary>LegacyAvatarController._setInt(int, int, bool), the override every player-body controller writes ints through.</summary>
        public static MethodBase HoldTypeTarget()
        {
            return AccessTools.DeclaredMethod(typeof(LegacyAvatarController), "_setInt", new[] { typeof(int), typeof(int), typeof(bool) });
        }

        /// <summary>
        /// A relaxed body's controller writes 0 for WeaponHoldType (and WeaponCarry, should anything write it this way): its
        /// own per-frame write from the held item becomes the bare-hands value, so nothing fights it and the parameter changes
        /// - and, on the host, goes out on the network - once per lowering. Everyone else returns at the first test.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_RelaxedHoldType
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                if (HoldTypeTarget() != null) return true;
                ScLog.Err("LegacyAvatarController._setInt(int, int, bool) not found (game update?) - relaxed NPCs keep their weapon pose");
                return false;
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return HoldTypeTarget(); }

            [HarmonyPrefix]
            public static void Prefix(LegacyAvatarController __instance, int _propertyHash, ref int _value)
            {
                if (relaxed.Count == 0 || _value == 0) return;
                if (_propertyHash != AvatarController.weaponHoldTypeHash && _propertyHash != AvatarController.weaponCarryHash) return;
                EntityAlive e = __instance != null ? __instance.entity : null;
                if ((object)e != null && relaxed.Contains(e.entityId)) _value = 0;
            }
        }

        /// <summary>LegacyAvatarController.StartAnimationReloading(), the reload start every ranged and launcher reload reaches.</summary>
        public static MethodBase ReloadStartTarget()
        {
            return AccessTools.DeclaredMethod(typeof(LegacyAvatarController), "StartAnimationReloading", new Type[0]);
        }

        private static void AlertFromHook(EntityAlive e, string why, bool reload, string key)
        {
            try
            {
                if (states.Count == 0 || (object)e == null || !states.ContainsKey(e.entityId)) return;
                OnAlert(e, why, reload);
            }
            catch (Exception ex)
            {
                ScLog.Once(key, ex);
            }
        }

        /// <summary>
        /// A swing or a shot raises the weapon first (OnAlert). SCore's attack task fires our bodies' Action0 through
        /// EntityAlive.Attack and Action1 through UseHoldingItem - both EntityAlive.UseHoldingItem(int, bool) - so the
        /// prefix there runs before the item action does: the hold type is back before WeaponFire picks the attack state and
        /// before a hitscan shot leaves. The press only, never the release; never stops anything. (The same target as
        /// ScMedic's Patch_NoAttackWhileUsing.)
        /// </summary>
        [HarmonyPatch(typeof(EntityAlive), nameof(EntityAlive.UseHoldingItem), new[] { typeof(int), typeof(bool) })]
        public static class Patch_RaiseOnAttack
        {
            [HarmonyPrefix]
            public static void Prefix(EntityAlive __instance, bool _isReleased)
            {
                if (_isReleased) return;
                AlertFromHook(__instance, "attacking", false, "relax-attack");
            }
        }

        /// <summary>
        /// SCore's other attack path, ExecuteAction(bool, int) on its two NPC classes (ScNpc.Declared), taken by its attack
        /// task only for an action index of 2 or more - no package of ours uses one today, kept so one that does is covered.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_RaiseOnSdxAttack
        {
            private static readonly Type[] Args = { typeof(bool), typeof(int) };

            [HarmonyPrepare]
            public static bool Prepare() { return ScNpc.Declared("ExecuteAction", Args).Count > 0; }

            [HarmonyTargetMethods]
            public static IEnumerable<MethodBase> TargetMethods() { return ScNpc.Declared("ExecuteAction", Args); }

            [HarmonyPrefix]
            public static void Prefix(EntityAlive __instance)
            {
                AlertFromHook(__instance, "attacking", false, "relax-sdx-attack");
            }
        }

        /// <summary>
        /// A reload starting raises the weapon first (Fable review HIGH-2): ItemActionRanged.ReloadGun and
        /// ItemActionLauncher.ReloadGun both call EntityAlive.OnReloadStart, which calls the avatar's
        /// StartAnimationReloading - which sets the Reload bool that the reload clips need together with the real hold
        /// type. The prefix puts the hold type back before that bool is set, so the clip enters and CompleteReload runs.
        /// This runs on the owner only: ReloadGun also runs on clients (NetPackageItemReload), but OnReloadStart is gated on
        /// !isEntityRemote there. A client's copy raises from the cvar instead, which this prefix's OnAlert sets to 0 on the
        /// host before the Reload bool is even queued, and the Reload bool waits for its clip.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_RaiseOnReload
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                if (ReloadStartTarget() != null) return true;
                ScLog.Err("LegacyAvatarController.StartAnimationReloading() not found (game update?) - a relaxed NPC raises its gun for a reload only at the next danger check");
                return false;
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return ReloadStartTarget(); }

            [HarmonyPrefix]
            public static void Prefix(LegacyAvatarController __instance)
            {
                if (states.Count == 0 || __instance == null) return;
                AlertFromHook(__instance.entity, "reloading", true, "relax-reload");
            }
        }
    }
}
