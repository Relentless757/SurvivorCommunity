using System;
using System.Collections.Generic;
using GamePath;
using HarmonyLib;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Doors for community members.
    ///
    /// SCore's NPC door opener (SCoreUtils.CheckForClosedDoor -> EntityUtilities.OpenDoor) is a silent no-op on 3.x
    /// doors: it calls the 4-argument Block.OnBlockActivated, which BlockCompositeTileEntity does not override (that
    /// overload is Block's pick-up handler and returns false), and it reads "open" from block meta bit 1, which 3.x
    /// moved into TEFeatureDoor.isOpen. It also only runs from some SCore tasks (never Guard, IdleSDX or our own
    /// WanderHomeSC / TravelSC). Meanwhile the A* planner routes paths through closed doors anyway, so members walked
    /// into shut doors and stood there until a watchdog teleported them.
    ///
    /// Vanilla 3.2 has a working opener in the move helper: EntityMoveHelper.CheckForDoorAndOpen, run from the move
    /// helper's obstacle check (every 4th move tick, while roughly facing the move target) when moveHelper.CanOpenDoors
    /// is set. Only EntityBandit sets it. Members get the flag (ScCommunity.ApplyAiPackage), which covers every task
    /// that walks a path. For SDX NPCs the prefix below replaces the vanilla body to:
    ///   (a) resolve a multi-block child leaf (double doors, gates) to its parent - vanilla looks up the tile entity on
    ///       the child cell and finds none;
    ///   (b) look ahead along the path (every node within LookAhead metres), so a running NPC does not meet a
    ///       still-swinging leaf;
    ///   (c) let members through the community owner's own LOCKED doors (MembersOpenOwnerLockedDoors) - a locked door
    ///       without that owner's permission (a POI's, another player's) stays shut, as in vanilla and SCore;
    ///   (d) leave drawbridges and hatches alone (base defences; a member never lowers the bridge);
    ///   (e) remember the door and shut it behind them, as SCore meant to - vanilla never closes.
    /// Host only: the move helper exists on the server.
    /// </summary>
    public static class ScDoors
    {
        private const float CloseDelay = 2f;        // real seconds after opening before it may shut (SCore used 2 s)
        private const float ClearDistance = 2f;     // XZ metres the opener must be from the door
        private const float MaxHold = 15f;          // after this, standing near the door no longer keeps it open (a post beside a door)
        private const float LookAhead = 4.5f;       // XZ metres of path ahead in which doors are opened
        private const float Margin = 0.75f;         // anyone this close to the door's cells keeps it open

        private struct Opened
        {
            public int By;
            public float Since;
        }

        private static readonly Dictionary<Vector3i, Opened> opened = new Dictionary<Vector3i, Opened>();   // keyed by the door's parent block
        private static readonly List<Vector3i> done = new List<Vector3i>();
        private static readonly List<Entity> near = new List<Entity>();
        /// <summary>The community owner (the host player), refreshed once a second by Tick.</summary>
        private static PlatformUserIdentifierAbs ownerId;

        public static void Reset()
        {
            opened.Clear();
            ownerId = null;
        }

        /// <summary>Switches vanilla's NPC door opener on or off. The move helper only exists on the server (EntityAlive.Awake).</summary>
        public static void Enable(EntityAlive npc, bool on)
        {
            if (npc != null && npc.moveHelper != null && npc.moveHelper.CanOpenDoors != on) npc.moveHelper.CanOpenDoors = on;
        }

        /// <summary>The door occupying a cell: a Door-tagged composite block, child leaves resolved to the parent.</summary>
        public static bool FindDoor(World w, Vector3i pos, out TileEntityComposite te, out TEFeatureDoor door, out Vector3i at)
        {
            te = null;
            door = null;
            at = pos;
            if (w == null) return false;
            BlockValue bv = w.GetBlock(pos);
            Block b = bv.Block;
            if (b == null || !b.HasTag(BlockTags.Door) || !(b is BlockCompositeTileEntity)) return false;
            if (bv.ischild) at = new Vector3i(pos.x + bv.parentx, pos.y + bv.parenty, pos.z + bv.parentz);   // = Block.MultiBlockArray.GetParentPos
            te = w.GetTileEntity(at) as TileEntityComposite;
            return te != null && te.TryGetSelfOrFeature<TEFeatureDoor>(out door) && door != null;
        }

        /// <summary>A drawbridge or a hatch: base defences a member leaves as they are.</summary>
        private static bool Defence(World w, Vector3i at, TEFeatureDoor door)
        {
            if (door.isDrawBridge) return true;
            Block b = w.GetBlock(at).Block;
            string name = b != null ? b.GetBlockName() : null;
            return name != null && name.IndexOf("hatch", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Locked against members: locked, and (unless allowed) not a lock the community owner may open.</summary>
        private static bool LockedForMembers(TileEntityComposite te)
        {
            TEFeatureLockable lk;
            if (!te.TryGetSelfOrFeature<TEFeatureLockable>(out lk) || lk == null || !lk.IsLocked()) return false;
            return !(ScConfig.MembersOpenOwnerLockedDoors && ownerId != null && lk.IsUserAllowed(ownerId));
        }

        private static PathEntity LivePath(EntityAlive npc)
        {
            PathNavigate nav = npc.getNavigator();
            PathEntity path = nav != null ? nav.getPath() : null;
            return path != null && path.HasPoints() && !path.isFinished() ? path : null;
        }

        /// <summary>The CheckForDoorAndOpen body for SDX NPCs: opens the closed doors on the path within LookAhead metres.</summary>
        public static void TryOpen(EntityAlive npc)
        {
            PathEntity path = LivePath(npc);
            if (path == null) return;
            int from = path.getCurrentPathIndex(), len = path.getCurrentPathLength();
            for (int i = from; i < len && i < from + 4; i++)
            {
                PathPoint pt = path.getPathPointFromIndex(i);
                if (pt == null) break;
                if (i > from && ScRunners.XZ(pt.projectedLocation, npc.position) > LookAhead) break;
                TryOpenAt(npc, pt);
            }
        }

        private static void TryOpenAt(EntityAlive npc, PathPoint pt)
        {
            TileEntityComposite te;
            TEFeatureDoor door;
            Vector3i at;
            if (!FindDoor(npc.world, pt.GetBlockPos(), out te, out door, out at) || door.IsOpen()) return;
            if (Defence(npc.world, at, door) || LockedForMembers(te)) return;
            door.SetOpen(true, true);        // what vanilla does: animation, sound, SetBlockRPC sync
            opened[at] = new Opened { By = npc.entityId, Since = Time.time };
            ScLog.Verbose(npc.EntityName + " (" + npc.entityId + ") opened the door at " + at);
        }

        private static bool HeadingFor(EntityAlive npc, Vector3i door)
        {
            PathEntity path = LivePath(npc);
            if (path == null) return false;
            int from = path.getCurrentPathIndex(), len = path.getCurrentPathLength();
            for (int i = from; i < len && i < from + 4; i++)
            {
                PathPoint pt = path.getPathPointFromIndex(i);
                if (pt == null) break;
                if (i > from && ScRunners.XZ(pt.projectedLocation, npc.position) > LookAhead) break;
                TileEntityComposite te;
                TEFeatureDoor d;
                Vector3i at;
                if (FindDoor(npc.world, pt.GetBlockPos(), out te, out d, out at) && at == door) return true;
            }
            return false;
        }

        /// <summary>Anyone alive (player, NPC, zombie) in the door's cells - every leaf of a multi-block door - or within margin of them.</summary>
        private static bool Occupied(World w, Vector3i at, float margin)
        {
            BlockValue bv = w.GetBlock(at);
            Block b = bv.Block;
            Vector3 min = at;
            Vector3 max = min + Vector3.one;
            if (b != null && b.isMultiBlock && b.multiBlockPos != null)
            {
                for (int i = 0; i < b.multiBlockPos.Length; i++)
                {
                    Vector3 c = at + b.multiBlockPos.Get(i, bv.type, bv.rotation);
                    min = Vector3.Min(min, c);
                    max = Vector3.Max(max, c + Vector3.one);
                }
            }
            Bounds box = new Bounds();
            box.SetMinMax(min - new Vector3(margin, 0.1f, margin), max + new Vector3(margin, 0.1f, margin));
            near.Clear();
            w.GetEntitiesInBounds(typeof(EntityAlive), box, near);
            for (int i = 0; i < near.Count; i++)
            {
                EntityAlive e = near[i] as EntityAlive;
                if (e != null && !e.IsDead()) return true;
            }
            return false;
        }

        /// <summary>Another loaded member whose path runs through this door.</summary>
        private static bool MemberHeadingFor(World w, Vector3i at, int except)
        {
            foreach (var m in ScRegistry.Members.Values)
            {
                if (m.Id == except) continue;
                EntityAliveSDX npc = ScCommunity.FindNpc(m.Id);
                if (npc != null && !npc.IsDead() && HeadingFor(npc, at)) return true;
            }
            return false;
        }

        /// <summary>1 Hz (ScTicker): shuts each door a member opened once they are past it and the doorway is clear.</summary>
        private const float PlayerHold = 2.5f;      // the player this close to a door (XZ) keeps it open: never shut it in your face

        public static void Tick(World w, EntityPlayerLocal player)
        {
            try
            {
                if (player != null && GameManager.Instance != null && GameManager.Instance.persistentPlayers != null)
                {
                    PersistentPlayerData ppd = GameManager.Instance.persistentPlayers.GetPlayerDataFromEntityID(player.entityId);
                    if (ppd != null) ownerId = ppd.PrimaryId;
                }
                if (opened.Count == 0 || w == null) return;
                float now = Time.time;
                done.Clear();
                foreach (var kv in opened)
                {
                    if (now - kv.Value.Since < CloseDelay) continue;
                    TileEntityComposite te;
                    TEFeatureDoor door;
                    Vector3i at;
                    // gone, unloaded, or already shut by someone else: forget it
                    if (!FindDoor(w, kv.Key, out te, out door, out at) || !door.IsOpen()) { done.Add(kv.Key); continue; }
                    // the opener still coming through (its path runs through this door), or still beside it for a while
                    EntityAlive by = w.GetEntity(kv.Value.By) as EntityAlive;
                    if (by != null && !by.IsDead())
                    {
                        Vector3 c = new Vector3(at.x + 0.5f, at.y, at.z + 0.5f);
                        if (HeadingFor(by, at) || (now - kv.Value.Since < MaxHold && ScRunners.XZ(by.position, c) < ClearDistance)) continue;
                    }
                    // right beside the door only counts for MaxHold (a member posted next to a door must not hold it open all
                    // night); anyone actually in the doorway, or the player close by, always does
                    bool fresh = now - kv.Value.Since < MaxHold;
                    Vector3 dc = new Vector3(at.x + 0.5f, at.y, at.z + 0.5f);
                    if (player != null && !player.IsDead() && ScRunners.XZ(player.position, dc) < PlayerHold && Mathf.Abs(player.position.y - dc.y) < 3f) continue;
                    if (Occupied(w, at, fresh ? Margin : 0f) || MemberHeadingFor(w, at, kv.Value.By)) continue;
                    door.SetOpen(false, true);
                    done.Add(kv.Key);
                    ScLog.Verbose("shut the door at " + at + " behind " + (by != null ? by.EntityName : "entity " + kv.Value.By));
                }
                for (int i = 0; i < done.Count; i++) opened.Remove(done[i]);
                done.Clear();
            }
            catch (Exception e)
            {
                ScLog.Once("doors-tick", e);
            }
        }

        /// <summary>
        /// EntityMoveHelper.CheckForDoorAndOpen runs from the move helper's obstacle check while CanOpenDoors is set.
        /// Vanilla bandits (EntityBandit) keep the vanilla body; SDX NPCs - only members have the flag - get ScDoors.TryOpen.
        /// </summary>
        [HarmonyPatch(typeof(EntityMoveHelper), "CheckForDoorAndOpen")]
        public static class Patch_CheckForDoorAndOpen
        {
            // a missing target must not throw inside PatchAll (it would skip the patch classes after it)
            [HarmonyPrepare]
            public static bool Prepare()
            {
                return AccessTools.Method(typeof(EntityMoveHelper), "CheckForDoorAndOpen", new Type[0]) != null;
            }

            [HarmonyPrefix]
            public static bool Prefix(EntityAlive ___entity)
            {
                if (!(___entity is EntityAliveSDX)) return true;
                try
                {
                    ScDoors.TryOpen(___entity);
                }
                catch (Exception e)
                {
                    ScLog.Once("doors-open", e);
                }
                return false;
            }
        }
    }
}
