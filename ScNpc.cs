using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// The type seam between this mod and SCore's two friendly NPC classes (migration task M1).
    ///
    /// SCore 3.2.36 has two of them: the legacy EntityAliveSDX, which every NPC this mod spawns still is, and
    /// EntityAliveSDXV4, the class 0-XNPCCore 3.2's survivors use. They are SIBLINGS - both derive from EntityTrader
    /// and implement IEntityAliveSDX and IEntityOrderReceiverSDX - so neither is an instance of the other, and most of
    /// what this mod calls on them (SendOnMission, UpdateWeapon, FirstName, IsHirable, the private _defaultWeapon) is
    /// declared separately on each with no common base. Everything else in the mod holds an NPC as a plain EntityAlive
    /// and comes here for those members; this is the only file that names the two concrete classes, apart from the
    /// Harmony target lists and the dependency probe in ModApi.
    ///
    /// For an EntityAliveSDX every call does exactly what the code it replaced did. Anything that is not one of the two
    /// classes (a raider, a vanilla trader, a zombie) gets the neutral answer the old "as EntityAliveSDX" gave it:
    /// null, false, or nothing done.
    /// </summary>
    public static class ScNpc
    {
        // ------------------------------------------------------------------ what it is ----

        /// <summary>Is this SCore's legacy class? (The class every NPC this mod spawns still is.)</summary>
        public static bool IsSdx(Entity e) { return e is EntityAliveSDX; }

        /// <summary>Is this 0-XNPCCore 3.2's class (SCore's EntityAliveSDXV4)?</summary>
        public static bool IsV4(Entity e) { return e is EntityAliveSDXV4; }

        /// <summary>Either friendly framework class: what "is EntityAliveSDX" used to test for.</summary>
        public static bool IsFramework(Entity e) { return e is EntityAliveSDX || e is EntityAliveSDXV4; }

        /// <summary>The entity as a framework NPC, or null: what "as EntityAliveSDX" used to do.</summary>
        public static EntityAlive As(Entity e) { return IsFramework(e) ? (EntityAlive)e : null; }

        /// <summary>
        /// The loaded framework NPC with this entity id, or null. A plain world lookup: unlike ScCommunity.FindNpc it
        /// knows nothing about the registry, so an offline runner's stale copy is returned like anyone else.
        /// </summary>
        public static EntityAlive Find(int id)
        {
            World w = GameManager.Instance != null ? GameManager.Instance.World : null;
            return w != null ? As(w.GetEntity(id)) : null;
        }

        /// <summary>
        /// The two classes, for Harmony target lists. A method rather than a static field: a field would run in the
        /// class initializer, and an initializer that fails (an SCore without V4) poisons every call into this class
        /// for the rest of the process instead of failing one call.
        /// </summary>
        public static Type[] Classes() { return new[] { typeof(EntityAliveSDX), typeof(EntityAliveSDXV4) }; }

        /// <summary>
        /// The method each framework class itself DECLARES under this name and signature (SDX first, then V4), for a
        /// patch's TargetMethods. Declared only: the two are siblings, so a base-class fallback would land on
        /// EntityTrader or EntityAlive and patch every NPC in the game. A class that does not declare it is skipped, so
        /// the list never holds a null (Harmony throws on one, and a throwing patch class aborts the rest of PatchAll).
        /// </summary>
        public static List<MethodBase> Declared(string name, Type[] args)
        {
            var list = new List<MethodBase>();
            try
            {
                foreach (Type t in Classes())
                {
                    MethodInfo m = AccessTools.DeclaredMethod(t, name, args);
                    if (m != null) list.Add(m);
                }
            }
            catch (Exception e)
            {
                ScLog.Once("npc-declared-" + name, e);
            }
            return list;
        }

        // ------------------------------------------------------------------ identity ----

        /// <summary>The class's Hirable flag (IEntityAliveSDX.IsHirable); false for anything else.</summary>
        public static bool IsHirable(EntityAlive e)
        {
            EntityAliveSDX sdx = e as EntityAliveSDX;
            if (sdx != null) return sdx.IsHirable;
            EntityAliveSDXV4 v4 = e as EntityAliveSDXV4;
            return v4 != null && v4.IsHirable;
        }

        /// <summary>
        /// Gives the NPC a name: both classes roll a random first name and title at spawn, and a stand-in or a re-created
        /// runner IS a known survivor. Nothing happens to anything else. (V4 also invalidates its cached display name in
        /// the setters, so the new name shows at once.)
        /// </summary>
        public static void SetName(EntityAlive e, string first, string title)
        {
            EntityAliveSDX sdx = e as EntityAliveSDX;
            if (sdx != null) { sdx.FirstName = first; sdx.Title = title; return; }
            EntityAliveSDXV4 v4 = e as EntityAliveSDXV4;
            if (v4 != null) { v4.FirstName = first; v4.Title = title; }
        }

        // ------------------------------------------------------------------ hiding (missions) ----

        /// <summary>SCore's own test: the onMission cvar is 1. False for anything that is not a framework NPC.</summary>
        public static bool IsOnMission(EntityAlive e)
        {
            EntityAliveSDX sdx = e as EntityAliveSDX;
            if (sdx != null) return sdx.IsOnMission();
            EntityAliveSDXV4 v4 = e as EntityAliveSDXV4;
            return v4 != null && v4.IsOnMission();
        }

        /// <summary>
        /// SCore's hide (scale 0, invisible, ignored by AI, onMission=1) or un-hide. It does not stop the entity
        /// ticking. Both classes implement it identically; nothing happens to anything else.
        /// </summary>
        public static void SendOnMission(EntityAlive e, bool send)
        {
            EntityAliveSDX sdx = e as EntityAliveSDX;
            if (sdx != null) { sdx.SendOnMission(send); return; }
            EntityAliveSDXV4 v4 = e as EntityAliveSDXV4;
            if (v4 != null) v4.SendOnMission(send);
        }

        /// <summary>Resets move speed to the class's own (IEntityAliveSDX.RestoreSpeed).</summary>
        public static void RestoreSpeed(EntityAlive e)
        {
            EntityAliveSDX sdx = e as EntityAliveSDX;
            if (sdx != null) { sdx.RestoreSpeed(); return; }
            EntityAliveSDXV4 v4 = e as EntityAliveSDXV4;
            if (v4 != null) v4.RestoreSpeed();
        }

        // ------------------------------------------------------------------ weapons ----

        /// <summary>
        /// SCore's UpdateWeapon(ItemValue, force): records the weapon for the save (_currentWeapon and the
        /// CurrentWeaponID cvar), puts it in hand slot 0 if FindWeapon allows it (else the class's default weapon) and
        /// refreshes the hand model; no network broadcast. False, with nothing done, when the entity is not a framework
        /// NPC - the caller then has to put the item in the hand itself.
        /// </summary>
        public static bool UpdateWeapon(EntityAlive e, ItemValue item, bool force)
        {
            EntityAliveSDX sdx = e as EntityAliveSDX;
            if (sdx != null) { sdx.UpdateWeapon(item, force); return true; }
            EntityAliveSDXV4 v4 = e as EntityAliveSDXV4;
            if (v4 != null) { v4.UpdateWeapon(item, force); return true; }
            return false;
        }

        /// <summary>SCore's UpdateWeapon(string): as above by item name (empty = the saved weapon), plus the hand-item broadcast.</summary>
        public static bool UpdateWeapon(EntityAlive e, string itemName)
        {
            EntityAliveSDX sdx = e as EntityAliveSDX;
            if (sdx != null) { sdx.UpdateWeapon(itemName); return true; }
            EntityAliveSDXV4 v4 = e as EntityAliveSDXV4;
            if (v4 != null) { v4.UpdateWeapon(itemName); return true; }
            return false;
        }

        /// <summary>
        /// The weapon the NPC saves and re-equips by name (the public _currentWeapon field, the same on both classes);
        /// null when the entity is not a framework NPC.
        /// </summary>
        public static string CurrentWeapon(EntityAlive e)
        {
            EntityAliveSDX sdx = e as EntityAliveSDX;
            if (sdx != null) return sdx._currentWeapon;
            EntityAliveSDXV4 v4 = e as EntityAliveSDXV4;
            return v4 != null ? v4._currentWeapon : null;
        }

        /// <summary>
        /// The class's fallback weapon (the private _defaultWeapon on both classes: the first itemsOnEnterGame entry,
        /// set by SetupStartingItems), which UpdateWeapon falls back to when FindWeapon refuses an item. Read by
        /// reflection because SCore keeps it private; null when the entity is not a framework NPC or the field is gone.
        /// Diagnostic use only - it is not cheap.
        /// </summary>
        public static string DefaultWeapon(EntityAlive e)
        {
            try
            {
                if (!IsFramework(e)) return null;
                FieldInfo f = AccessTools.Field(e.GetType(), "_defaultWeapon");
                return f != null ? f.GetValue(e) as string : null;
            }
            catch (Exception ex)
            {
                ScLog.Once("npc-default-weapon", ex);
                return null;
            }
        }

        // ------------------------------------------------------------------ the inventory window ----

        /// <summary>
        /// The slots the player sees in the NPC's inventory window: a framework NPC's lives in SCore's HarvestManager
        /// (saved with the world - both classes write it in their Write - and dropped as a backpack on death); anything
        /// else, a raider included, uses its vanilla bag (null when it has none).
        /// </summary>
        public static ItemStack[] Items(EntityAlive e)
        {
            if (e == null) return null;
            if (IsFramework(e)) return HarvestManager.GetOrCreate(e.entityId).GetItems();
            return e.bag != null ? e.bag.GetSlots() : null;
        }

        /// <summary>Adds a stack to that inventory (a raider without a bag gets the vanilla 45-slot one first).</summary>
        public static void AddItem(EntityAlive e, ItemStack stack)
        {
            if (e == null || stack == null) return;
            if (IsFramework(e)) HarvestManager.AddItem(e.entityId, stack);
            else
            {
                if (e.bag == null) e.bag = new Bag(45);
                e.bag.AddItem(stack);
            }
        }

        /// <summary>Replaces one slot of that inventory.</summary>
        public static void SetItem(EntityAlive e, int slot, ItemStack stack)
        {
            if (e == null) return;
            if (IsFramework(e)) HarvestManager.GetOrCreate(e.entityId).UpdateSlot(slot, stack);
            else if (e.bag != null) e.bag.SetSlot(slot, stack);
        }

        // ------------------------------------------------------------------ orders ----

        /// <summary>The guard spot (IEntityOrderReceiverSDX.GuardPosition); Vector3.zero for anything else.</summary>
        public static Vector3 GuardPosition(EntityAlive e)
        {
            IEntityOrderReceiverSDX r = Orders(e);
            return r != null ? r.GuardPosition : Vector3.zero;
        }

        /// <summary>
        /// Writes the guard spot and the point the NPC faces while guarding. Through the interface, because the two
        /// classes store them differently (SDX in plain fields, V4 in its patrol component, or in holding fields until
        /// PostInit has built the component).
        /// </summary>
        public static void SetGuard(EntityAlive e, Vector3 position, Vector3 lookPosition)
        {
            IEntityOrderReceiverSDX r = Orders(e);
            if (r == null) return;
            r.GuardPosition = position;
            r.GuardLookPosition = lookPosition;
        }

        /// <summary>The order interface of a framework NPC, or null (other mods' classes implement it too: not ours to drive).</summary>
        public static IEntityOrderReceiverSDX Orders(EntityAlive e)
        {
            return IsFramework(e) ? e as IEntityOrderReceiverSDX : null;
        }

        // ------------------------------------------------------------------ leader and owner ----

        /// <summary>
        /// SCore's EntityUtilities.SetLeaderAndOwner (Leader cvar, hired_* on the leader, leader cache, StaticSpawner,
        /// the Follow order when defaultOrder), and then the Owner cvar for a V4 NPC. SCore's SetOwner looks the NPC up
        /// "as EntityAliveSDX", so on a V4 it silently does nothing: without this a V4 follower would have a Leader but
        /// no Owner, and what reads Owner would see a different NPC from an SDX one - SCore's GetOwner (GetLeaderOrOwner's
        /// fallback once the Leader id is stale, as after a relog), its StaticSpawner re-assert on load, and the HiredSDX
        /// dialog requirement. The V4 branch mirrors SetOwner line for line: both cvars, and only when both entities are
        /// loaded. For an SDX NPC this is exactly SCore's call.
        /// </summary>
        public static void SetLeaderAndOwner(int entityId, int leaderId, bool defaultOrder = true)
        {
            EntityUtilities.SetLeaderAndOwner(entityId, leaderId, defaultOrder);
            try
            {
                World w = GameManager.Instance != null ? GameManager.Instance.World : null;
                if (w == null) return;
                EntityAliveSDXV4 v4 = w.GetEntity(entityId) as EntityAliveSDXV4;
                EntityAlive leader = w.GetEntity(leaderId) as EntityAlive;
                if (v4 == null || leader == null) return;
                v4.Buffs.SetCustomVar("Owner", leaderId);
                leader.Buffs.SetCustomVar("EntityID", leaderId);
            }
            catch (Exception e)
            {
                ScLog.Once("npc-set-owner", e);
            }
        }
    }
}
