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
    /// classes (a vanilla EntityNPCBandit, a vanilla trader, a zombie) gets the neutral answer the old "as
    /// EntityAliveSDX" gave it: null, false, or nothing done. SC's raiders are EntityAliveSDXV4 since M10.
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

        // ------------------------------------------------------------------ lookups that never force a body ----

        /// <summary>Lookups that found a body still being built and left it alone (sc xnpc: pending-body-skips).</summary>
        internal static int PendingSkips;

        /// <summary>
        /// Is the game still building this entity? A chunk that loads queues its entities with 3.2's EntityAsyncManager,
        /// which builds them over the next frames. World.GetEntity on a queued id finishes the build there and then
        /// (EnsureEntity: "[EntityAsync] force spawning pending entity", and the survivor's player prefab loaded again
        /// synchronously), so every survivor whose chunk loaded was built inside one frame by SC's own lookups - the
        /// hitch the user saw on 2026-10-12, when two residents on the edge of the view distance were rebuilt 8 times in
        /// 4 minutes, each one forced in registry order by the ticker's relink.
        /// </summary>
        public static bool IsPending(World w, int id)
        {
            EntityAsyncManager am = w != null ? w.entityAsyncManager : null;
            return am != null && am.IsEntityPending(id);
        }

        /// <summary>
        /// World.GetEntity for a body the mod wants to act on now, without forcing one that is still being built: that
        /// one reads as not loaded yet - which every caller handles, it is the unloaded case a moment before the load -
        /// and is there on a later tick. Duplicate and missing checks want HasBody / Exists instead.
        /// </summary>
        public static Entity Loaded(World w, int id)
        {
            if (w == null) return null;
            if (IsPending(w, id)) { PendingSkips++; return null; }
            return w.GetEntity(id);
        }

        /// <summary>Is there an entity with this id, loaded or still being built? Never forces the build.</summary>
        public static bool Exists(World w, int id)
        {
            if (w == null) return false;
            if (IsPending(w, id)) { PendingSkips++; return true; }
            return w.GetEntity(id) != null;
        }

        /// <summary>
        /// Is there a framework body with this id, loaded or still being built? A queued id is a chunk's saved copy of
        /// that body, so it counts before its class can be checked. Never forces the build.
        /// </summary>
        public static bool HasBody(World w, int id)
        {
            if (w == null) return false;
            if (IsPending(w, id)) { PendingSkips++; return true; }
            return IsFramework(w.GetEntity(id));
        }

        /// <summary>
        /// The loaded framework NPC with this entity id, or null. A plain world lookup: unlike ScCommunity.FindNpc it
        /// knows nothing about the registry, so an offline runner's stale copy is returned like anyone else.
        /// </summary>
        public static EntityAlive Find(int id)
        {
            World w = GameManager.Instance != null ? GameManager.Instance.World : null;
            return w != null ? As(Loaded(w, id)) : null;
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
        /// C4 (round 28, 3.4): a body hidden while seated in a vehicle leaves the seat first. 0-XNPCCore 3.4 seats a follower
        /// instead of hiding her (its SendOnMission(true) prefix: a seated body is not hidden at all, a leader in a vehicle
        /// boards her), and 3.4.04 undoes any hide on a seated body every frame - so a member SC hides for a supply run, an
        /// outpost posting or a stale copy would stay in the seat, or flicker back.
        /// </summary>
        public static void SendOnMission(EntityAlive e, bool send)
        {
#if SC_V34
            if (send && e != null) LeaveSeat(e);
#endif
            EntityAliveSDX sdx = e as EntityAliveSDX;
            if (sdx != null) { sdx.SendOnMission(send); return; }
            EntityAliveSDXV4 v4 = e as EntityAliveSDXV4;
            if (v4 != null) v4.SendOnMission(send);
        }

#if SC_V34
        /// <summary>
        /// C4 (round 28): out of a vehicle seat before SC hides the body (findings_x3 C4), and 0-XNPCCore's board request
        /// (SdcsVehicleOrder) dropped, so its hide prefix neither keeps her seated nor boards her. Every SC caller parks the body on
        /// Stay first (ScRuns.Park), which ends 0-XNPCCore's auto-boarding (it follows the Follow order).
        /// </summary>
        private static void LeaveSeat(EntityAlive e)
        {
            try
            {
                if (e.AttachedToEntity != null)
                {
                    ScLog.Verbose(ScCommunity.DisplayName(e) + " (" + e.entityId + ") leaves the vehicle seat before being hidden");
                    e.Detach();
                }
                if (e.Buffs != null && e.Buffs.GetCustomVar("SdcsVehicleOrder") != 0f) e.Buffs.SetCustomVar("SdcsVehicleOrder", 0f);
            }
            catch (Exception ex)
            {
                ScLog.Once("npc-leave-seat", ex);
            }
        }

#endif
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
        /// else uses its vanilla bag (null when it has none).
        ///
        /// SC's raiders on XNPC's body (M10, design 3.3) are framework NPCs too, but keep everything in the BAG: a
        /// HarvestManager container, once created, is written to its own save file for good (HarvestManager persists every
        /// container it holds), which for unsaved wild or raid raiders would leak one entry per raider. The bag is saved
        /// with the entity (when it is saved at all) and emptied into the death backpack (Patches_Npc). So ScMedic's stock
        /// and use, and a console give, go to a raider's bag, and SC itself never creates a window for one. 0-XNPCCore does
        /// (on 3.4): its trade stock and water seeding and, for a raider on its bandits (round R1), its spawn-ammo grant
        /// put things in one; for those raiders SC pre-marks the first two as done, moves the ammo into the bag and drops
        /// the emptied window (ScBandits), and the death drop removes any window left.
        /// </summary>
        public static ItemStack[] Items(EntityAlive e)
        {
            if (e == null) return null;
            if (UsesWindow(e)) return DeadWithoutWindow(e) ? null : HarvestManager.GetOrCreate(e.entityId).GetItems();
#if SC_V34
            // 3.4: the bag's grid. Its stacks are bound to their slots: read them, write through ScNpc.SetItem / AddItem
            return e.bag != null ? e.bag.ItemGrid.items : null;
#else
            return e.bag != null ? e.bag.GetSlots() : null;
#endif
        }

        /// <summary>Adds a stack to that inventory (an NPC without a bag gets the vanilla 45-slot one first).</summary>
        public static void AddItem(EntityAlive e, ItemStack stack)
        {
            if (e == null || stack == null) return;
#if SC_V34
            if (UsesWindow(e))
            {
                if (DeadWithoutWindow(e)) return;
                // 3.4 (SCore 3.4): a full window takes what fits and leaves the rest in the stack, returning false - where
                // 3.2's simply lost the rest. The rest goes to the bag, else to the ground at the body (host only)
                if (HarvestManager.AddItem(e.entityId, stack) || stack.count <= 0) return;
                if (e.bag != null && e.bag.AddItem(stack)) return;
                if (!ScCommunity.IsHost || GameManager.Instance == null) return;
                GameManager.Instance.ItemDropServer(stack.Clone(), e.position + UnityEngine.Vector3.up, UnityEngine.Vector3.zero, -1, 60f);
                ScLog.Verbose(ScCommunity.DisplayName(e) + " (id " + e.entityId + "): the inventory window is full - " + stack.count + " "
                              + (stack.itemValue.ItemClass != null ? stack.itemValue.ItemClass.GetItemName() : "?") + " dropped at the body");
            }
            else
            {
                // 3.4: a Bag is a grid now - 45 slots in one row, like 3.2's new Bag(45), no holder (nothing listens)
                if (e.bag == null) e.bag = new Bag(new Vector2i(45, 1), XUiC_ItemStack.StackLocationTypes.LootContainer, null);
                e.bag.AddItem(stack);
            }
#else
            if (UsesWindow(e)) { if (!DeadWithoutWindow(e)) HarvestManager.AddItem(e.entityId, stack); }
            else
            {
                if (e.bag == null) e.bag = new Bag(45);
                e.bag.AddItem(stack);
            }
#endif
        }

        /// <summary>Replaces one slot of that inventory.</summary>
        public static void SetItem(EntityAlive e, int slot, ItemStack stack)
        {
            if (e == null) return;
            if (UsesWindow(e)) { if (!DeadWithoutWindow(e)) HarvestManager.GetOrCreate(e.entityId).UpdateSlot(slot, stack); }
            else if (e.bag != null) e.bag.SetSlot(slot, stack);
        }

        /// <summary>
        /// A dead NPC whose window is gone (dropped with it, Patches_Npc): HarvestManager.GetOrCreate would make a new,
        /// never-dropped container for the dead id (HarvestManager saves every container it holds), so none is made.
        /// </summary>
        private static bool DeadWithoutWindow(EntityAlive e)
        {
            return e.IsDead() && !HarvestManager.Has(e.entityId);
        }

        /// <summary>A framework NPC that keeps its things in SCore's inventory window: every one but SC's raiders (see Items).</summary>
        internal static bool UsesWindow(EntityAlive e)
        {
            return IsFramework(e) && !ScXnpc.IsRaider(e);
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
