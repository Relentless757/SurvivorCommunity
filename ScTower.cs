using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace SurvivorCommunity
{
    /// <summary>
    /// The Guard Tower (base defence, BaseDefence_Plan.md milestone 6): a kit block (BlockScGuardTower) that puts the
    /// user's tower prefab into the world in one go. The prefab is Prefabs/scGuardTower, copied from the user's
    /// LocalPrefabs/Guard_Tower by Tools/import_guard_tower.py with its Watch post turned to look away from the ladder.
    ///
    /// Placing: the kit's cell is the prefab's bottom-centre cell at ground level (YOffset sinks the rest), and the tower
    /// turns with the kit's rotation plus 180 degrees - Place=TowardsPlacer points the kit's forward the way the player
    /// looks, and the prefab's forward (+Z) is its ladder side, so by default the ladder comes toward the player and the
    /// guard looks the way the player looked. The kit's CanPlaceBlockAt runs Check (the vanilla ghost and the press), the
    /// ghost of the whole tower follows vanilla's own (Patch_FocusPreview), and the server builds it one frame after the
    /// kit lands (Expand: Check again, then one SetBlocksRPC with the placer as owner; on a refusal the kit is dropped).
    ///
    /// Check: inside the home zone with room for one more Watch post (when SC runs the host and watch posts are on), off
    /// other claims and trader land, chunks loaded, and the box clear - only air, grass and wild plants (not a player's
    /// crops), and terrain up to GuardTowerCut layers above the ground (cut away; higher is "too uneven"). Below ground
    /// level only the tower's own blocks are written: terrain gives way to them, a built block that carries weight is left
    /// to carry them. A bottom-layer block over air reaches down to the ground with copies of itself, at most
    /// GuardTowerFill, and stops only on terrain or a weight-carrying block. Nothing built is ever replaced.
    /// </summary>
    public static class ScTower
    {
        public const string PrefabName = "scGuardTower";

        private static Prefab prefab;
        private static bool loadTried;
        private static Vector3i postCell;
        private static bool hasPost;

        // the preview asks every frame (the vanilla ghost and ours): the last answer is kept a quarter second
        private static bool cacheValid;
        private static Vector3i cachePos;
        private static int cacheRot;
        private static float cacheTime;
        private static string cacheKey, cacheArg;
        private static float nextTip;

        public static void Reset()
        {
            prefab = null;
            loadTried = false;
            hasPost = false;
            cacheValid = false;
            nextTip = 0f;
            ScTowerGhost.Reset();
        }

        /// <summary>The tower prefab from this mod's Prefabs folder (loaded once per game: block ids are per save).</summary>
        public static Prefab Get()
        {
            if (prefab != null || loadTried) return prefab;
            loadTried = true;
            try
            {
                Mod mod = ModManager.GetMod("SurvivorCommunity");
                string file = mod != null ? Path.Combine(Path.Combine(mod.Path, "Prefabs"), PrefabName + ".tts") : null;
                if (file == null || !File.Exists(file))
                {
                    ScLog.Err("guard tower: " + (file ?? "the mod folder") + " not found - the Guard Tower can't be placed");
                    return null;
                }
                var loc = new PathAbstractions.AbstractedLocation(PathAbstractions.EAbstractedLocationType.Mods, PrefabName, file, "Prefabs", false, mod);
                var p = new Prefab();
                if (!p.Load(loc, true, true, false))
                {
                    ScLog.Err("guard tower: " + file + " did not load (a block it names is missing?) - the Guard Tower can't be placed");
                    return null;
                }
                hasPost = false;
                int children = 0;
                for (int y = 0; y < p.size.y; y++)
                    for (int x = 0; x < p.size.x; x++)
                        for (int z = 0; z < p.size.z; z++)
                        {
                            BlockValue bv = p.GetBlock(x, y, z);
                            if (bv.ischild) children++;
                            else if (!hasPost && bv.Block is BlockScWatchPost) { postCell = new Vector3i(x, y, z); hasPost = true; }
                        }
                // the stamp leaves a multi-block's cells to its parent's OnBlockAdded, untested for a tower (none in the user's)
                if (children > 0) ScLog.Warn("guard tower: " + PrefabName + " has " + children + " multi-block cell(s) (doors, gates...) - they may not come out right");
                prefab = p;
                ScLog.Out("guard tower: " + PrefabName + " loaded, size " + P(p.size) + ", yOffset " + p.yOffset + ", anchor " + P(Anchor(p))
                          + (hasPost ? ", Watch post at " + P(postCell) + " (rotation " + p.GetBlock(postCell.x, postCell.y, postCell.z).rotation + ")" : ", no Watch post"));
            }
            catch (Exception e)
            {
                ScLog.Once("tower-load", e);
            }
            return prefab;
        }

        /// <summary>A position in brackets (Vector3i prints bare "x, y, z").</summary>
        public static string P(Vector3i v)
        {
            return "(" + v + ")";
        }

        /// <summary>The prefab cell that lands on the kit's cell: bottom centre, at ground level.</summary>
        public static Vector3i Anchor(Prefab p)
        {
            return new Vector3i(p.size.x / 2, -p.yOffset, p.size.z / 2);
        }

        /// <summary>Quarter turns (+90 degrees about up, +Z to +X) for a kit rotation: its own plus 180 (see the class summary).</summary>
        public static int Turns(int kitRotation)
        {
            return ((kitRotation & 3) + 2) & 3;
        }

        /// <summary>A prefab cell's world position for a kit at kit turned by turns.</summary>
        public static Vector3i ToWorld(Vector3i kit, Vector3i anchor, int turns, int x, int y, int z)
        {
            int dx = x - anchor.x, dz = z - anchor.z;
            for (int i = 0; i < turns; i++)
            {
                int t = dx;
                dx = dz;
                dz = -t;
            }
            return new Vector3i(kit.x + dx, kit.y + y - anchor.y, kit.z + dz);
        }

        /// <summary>The rotation vanilla's TowardsPlacer gives a block at pos placed from 'from' (the console's default).</summary>
        public static int PlacerRotation(Vector3 from, Vector3i pos)
        {
            float dx = from.x - (pos.x + 0.5f), dz = from.z - (pos.z + 0.5f);
            if (Mathf.Abs(dx) > Mathf.Abs(dz)) return dx > 0f ? 3 : 1;
            return dz > 0f ? 2 : 0;
        }

        /// <summary>What a fitting tower writes: the block changes bottom-up, and the terrain to snap under its legs.</summary>
        public sealed class Plan
        {
            public readonly List<BlockChangeInfo> Changes = new List<BlockChangeInfo>();
            public readonly List<Vector3i> SnapAt = new List<Vector3i>();
            public int Blocks, Legs, Cleared;
            public bool HasPost;
            public Vector3i Post;
            public int Turns;
        }

        private struct Pending
        {
            public int Y, Order;
            public BlockChangeInfo Change;
        }

        /// <summary>
        /// Air, grass and wild plants: cleared out of the tower's way. A player's crops are ground cover too, but someone
        /// planted them: growing (BlockPlantGrowing) or ripe ("...3HarvestPlayer"). The ripe plants biomes scatter in the
        /// wild ("planted...3Harvest": cotton, goldenrod, ...) are cleared like any wild plant.
        /// </summary>
        private static bool Clearable(BlockValue bv)
        {
            if (bv.isair) return true;
            Block b = bv.Block;
            if (b is BlockPlantGrowing || b.GetBlockName().EndsWith("HarvestPlayer", StringComparison.OrdinalIgnoreCase)) return false;
            return b.CanBlocksReplaceOrGroundCover();
        }

        /// <summary>What a tower block may rest on: terrain, or a block that carries weight (not a plant, a torch, a sign).</summary>
        private static bool Carries(BlockValue bv)
        {
            if (bv.isair) return false;
            Block b = bv.Block;
            return b.shape.IsTerrain() || (b.StabilitySupport && (b.blockMaterial == null || !b.blockMaterial.IsPlant));
        }

        /// <summary>
        /// Does the tower fit with its kit at kit? Null when it does, else the refusal's localization key (arg: its {0}).
        /// ppd: whose claims count (null: none checked). plan: filled with the changes when it fits; owner is the placer's
        /// entity id for them (-1: none).
        /// </summary>
        public static string Check(World w, Vector3i kit, int kitRotation, PersistentPlayerData ppd, int owner, Plan plan, out string arg)
        {
            arg = null;
            Prefab p = Get();
            if (p == null) return "scTowerNoPrefab";
            if (w == null) return "scTowerUnloaded";
            int turns = Turns(kitRotation);
            Vector3i a = Anchor(p);
            Vector3i size = p.size;
            int fill = Math.Max(0, ScConfig.GuardTowerFill), cut = Math.Max(0, ScConfig.GuardTowerCut);
            int bottom = kit.y - a.y;
            if (bottom - fill < 1 || bottom + size.y >= 254) return "scTowerHeight";

            Vector3i c0 = ToWorld(kit, a, turns, 0, 0, 0), c1 = ToWorld(kit, a, turns, size.x - 1, 0, size.z - 1);
            int xMin = Math.Min(c0.x, c1.x), xMax = Math.Max(c0.x, c1.x), zMin = Math.Min(c0.z, c1.z), zMax = Math.Max(c0.z, c1.z);
            for (int cx = World.toChunkXZ(xMin); cx <= World.toChunkXZ(xMax); cx++)
                for (int cz = World.toChunkXZ(zMin); cz <= World.toChunkXZ(zMax); cz++)
                    if (w.GetChunkSync(cx, cz) == null) return "scTowerUnloaded";

            Vector3i[] corners =
            {
                new Vector3i(xMin, kit.y, zMin), new Vector3i(xMin, kit.y, zMax), new Vector3i(xMax, kit.y, zMin), new Vector3i(xMax, kit.y, zMax)
            };
            Vector3i postWorld = hasPost ? ToWorld(kit, a, turns, postCell.x, postCell.y, postCell.z) : Vector3i.zero;
            if (ScCommunity.IsHost && ScRegistry.Loaded && ScConfig.WatchPosts)
            {
                if (!ScRegistry.Community.HasHome) return "scTowerNoHome";
                for (int i = 0; i < corners.Length; i++)
                    if (!ScZone.Contains(corners[i])) return "scTowerOutside";
                if (hasPost)
                {
                    string why = ScWatch.PlaceRefusal(postWorld);
                    if (why == "scWatchNoHome") return "scTowerNoHome";
                    if (why == "scWatchOutside") return "scTowerOutside";
                    if (why != null) return why;
                }
            }
            if (ppd != null)
                for (int i = 0; i < corners.Length; i++)
                    if (!w.CanPlaceBlockAt(corners[i], ppd)) return "scTowerClaim";
            var box = new Bounds();
            box.SetMinMax(new Vector3(xMin, bottom - fill, zMin), new Vector3(xMax + 1, bottom + size.y, zMax + 1));
            if (w.IsWithinTraderPlacingProtection(box)) return "scTowerTrader";

            var pending = plan != null ? new List<Pending>() : null;
            var written = new HashSet<Vector3i>();
            var solids = new List<Bounds>();
            int order = 0;
            for (int y = 0; y < size.y; y++)
            {
                bool buried = y < a.y;
                int above = y - a.y;
                for (int x = 0; x < size.x; x++)
                {
                    for (int z = 0; z < size.z; z++)
                    {
                        Vector3i wp = ToWorld(kit, a, turns, x, y, z);
                        BlockValue pb = p.GetBlock(x, y, z);
                        BlockValue cur = wp == kit ? BlockValue.Air : w.GetBlock(wp);
                        if (w.GetWater(wp).HasMass()) return "scTowerWater";
                        bool terrain = !cur.isair && cur.Block.shape.IsTerrain();
                        bool clearable = !terrain && Clearable(cur);
                        if (buried)
                        {
                            // below ground level only the tower's own blocks: terrain and loose cover give way, a built
                            // block that carries weight is left to carry them, and the ground around them stays as it is
                            if (pb.isair) continue;
                            if (!terrain && !clearable)
                            {
                                if (Carries(cur)) continue;
                                arg = cur.Block.GetLocalizedBlockName();
                                return "scTowerBlocked";
                            }
                        }
                        else
                        {
                            if (terrain && above >= cut) return "scTowerSteep";
                            if (!terrain && !clearable)
                            {
                                arg = cur.Block.GetLocalizedBlockName();
                                return "scTowerBlocked";
                            }
                            if (pb.isair)
                            {
                                if (cur.isair) continue;
                                // terrain within the cut, grass and plants: cleared
                                if (plan != null) plan.Cleared++;
                                written.Add(wp);
                                if (pending != null) pending.Add(new Pending { Y = wp.y, Order = order++, Change = new BlockChangeInfo(wp, BlockValue.Air, MarchingCubes.DensityAir, owner) });
                                continue;
                            }
                        }
                        // a tower block (a multi-block's child cells are left to its parent's OnBlockAdded)
                        if (pb.Block.IsCollideMovement) solids.Add(CellBounds(wp));
                        if (pb.ischild) continue;
                        BlockValue rb = pb.Block.shape.RotateY(false, pb, turns);
                        sbyte density = p.GetDensity(x, y, z);
                        TextureFullArray tex = p.GetTexture(x, y, z);
                        written.Add(wp);
                        if (plan != null) plan.Blocks++;
                        if (pending != null) pending.Add(new Pending { Y = wp.y, Order = order++, Change = Change(wp, rb, density, tex, owner) });
                        if (y != 0) continue;
                        // a bottom-layer block over air reaches down to the ground with copies of itself
                        Vector3i d = wp - Vector3i.up;
                        for (int n = 0; ; n++)
                        {
                            BlockValue below = w.GetBlock(d);
                            if (!below.isair && below.Block.shape.IsTerrain())
                            {
                                if (plan != null) plan.SnapAt.Add(d);
                                break;
                            }
                            if (!Clearable(below))
                            {
                                // a built block carries the leg; a crop, a torch or a sign does not
                                if (Carries(below)) break;
                                arg = below.Block.GetLocalizedBlockName();
                                return "scTowerBlocked";
                            }
                            if (w.GetWater(d).HasMass()) return "scTowerWater";
                            if (n >= fill) return "scTowerSteep";
                            if (pb.Block.IsCollideMovement) solids.Add(CellBounds(d));
                            written.Add(d);
                            if (plan != null) { plan.Blocks++; plan.Legs++; }
                            if (pending != null) pending.Add(new Pending { Y = d.y, Order = order++, Change = Change(d, rb, density, tex, owner) });
                            d -= Vector3i.up;
                        }
                    }
                }
            }

            // nobody inside a cell that gets a solid block
            if (solids.Count > 0)
            {
                Bounds all = solids[0];
                for (int i = 1; i < solids.Count; i++) all.Encapsulate(solids[i]);
                List<Entity> found = w.GetEntitiesInBounds(typeof(EntityAlive), all, new List<Entity>());
                for (int i = 0; i < found.Count; i++)
                {
                    EntityAlive e = found[i] as EntityAlive;
                    if (e == null || e.IsDead()) continue;
                    for (int j = 0; j < solids.Count; j++)
                    {
                        if (!solids[j].Intersects(e.boundingBox)) continue;
                        arg = e is EntityPlayer ? ((EntityPlayer)e).PlayerDisplayName : e.LocalizedEntityName;
                        return "scTowerSomeone";
                    }
                }
            }

            if (plan != null)
            {
                // the kit's own cell goes too, when no tower block takes it
                if (!written.Contains(kit)) pending.Add(new Pending { Y = kit.y, Order = order++, Change = new BlockChangeInfo(kit, BlockValue.Air, MarchingCubes.DensityAir, owner) });
                // bottom-up, so every block has its support before it lands (stable for one layer)
                pending.Sort((l, r) => l.Y != r.Y ? l.Y.CompareTo(r.Y) : l.Order.CompareTo(r.Order));
                for (int i = 0; i < pending.Count; i++) plan.Changes.Add(pending[i].Change);
                plan.HasPost = hasPost;
                plan.Post = postWorld;
                plan.Turns = turns;
            }
            return null;
        }

        private static BlockChangeInfo Change(Vector3i pos, BlockValue bv, sbyte density, TextureFullArray tex, int owner)
        {
            var c = new BlockChangeInfo(pos, bv, density, owner);
            c.textureFull = tex;
            c.bChangeTexture = true;
            return c;
        }

        private static Bounds CellBounds(Vector3i p)
        {
            // a little under a full block, so an entity standing on top of it or beside it does not count
            return new Bounds(new Vector3(p.x + 0.5f, p.y + 0.5f, p.z + 0.5f), new Vector3(0.9f, 0.9f, 0.9f));
        }

        /// <summary>
        /// The kit's CanPlaceBlockAt (the vanilla ghost every frame, the press, and our ghost): Check with the local
        /// player's claims, the last answer reused for a quarter second at the same cell and rotation (not on a press).
        /// </summary>
        public static string Refusal(World w, Vector3i kit, int rotation, bool fresh, out string arg)
        {
            if (!fresh && cacheValid && cachePos == kit && cacheRot == rotation && Time.time - cacheTime < 0.25f)
            {
                arg = cacheArg;
                return cacheKey;
            }
            PersistentPlayerData ppd = GameManager.Instance != null ? GameManager.Instance.persistentLocalPlayer : null;
            string key = Check(w, kit, rotation, ppd, -1, null, out arg);
            cacheValid = true;
            cachePos = kit;
            cacheRot = rotation;
            cacheTime = Time.time;
            cacheKey = key;
            cacheArg = arg;
            return key;
        }

        /// <summary>A refusal's text ({0}: arg; the post cap uses the Watch post's own text).</summary>
        public static string Text(string key, string arg)
        {
            if (key == "scWatchTooMany") return ScWatch.ErrorText(key, null, Vector3i.zero);
            string s = Localization.Get(key);
            try
            {
                return string.Format(s, arg ?? "");
            }
            catch (FormatException)
            {
                return s;
            }
        }

        /// <summary>The reason on a refused press (at most once a second), or a refused build.</summary>
        public static void Tell(EntityPlayer player, string text, bool limited)
        {
            if (player == null) return;
            if (limited)
            {
                if (Time.unscaledTime < nextTip) return;
                nextTip = Time.unscaledTime + 1f;
            }
            ScCommunity.Tell(player, text, "ui_denied");
        }

        /// <summary>BlockScGuardTower.OnBlockAdded (server, main thread, placed by a player): built a frame later.</summary>
        public static void Expand(Vector3i kit, BlockValue kitBv, PlatformUserIdentifierAbs by)
        {
            GameManager gm = GameManager.Instance;
            if (gm == null) return;
            gm.StartCoroutine(ExpandLater(kit, kitBv, by));
        }

        private static IEnumerator ExpandLater(Vector3i kit, BlockValue kitBv, PlatformUserIdentifierAbs by)
        {
            // OnBlockAdded runs inside ChangeBlocks: the tower's own SetBlocksRPC waits for the next frame
            yield return null;
            try
            {
                ExpandNow(kit, kitBv, by);
            }
            catch (Exception e)
            {
                ScLog.Once("tower-expand", e);
            }
        }

        private static void ExpandNow(Vector3i kit, BlockValue kitBv, PlatformUserIdentifierAbs by)
        {
            GameManager gm = GameManager.Instance;
            World w = gm != null ? gm.World : null;
            if (w == null) return;
            BlockValue now = w.GetBlock(kit);
            if (now.type != kitBv.type)
            {
                ScLog.Out("guard tower: the kit at " + P(kit) + " was gone before the tower went up - nothing built");
                return;
            }
            PersistentPlayerList players = gm.persistentPlayers;
            PersistentPlayerData ppd = players != null && by != null ? players.GetPlayerData(by) : null;
            int owner = Owner(ppd != null ? ppd.EntityId : -1);
            EntityPlayer placer = owner != -1 ? w.GetEntity(owner) as EntityPlayer : null;

            var plan = new Plan();
            string arg;
            string key = Check(w, kit, now.rotation, ppd, owner, plan, out arg);
            if (key != null)
            {
                // a client can't see the zone or the post cap, so this is how a remote player hears of them: the kit
                // comes off and goes back (vanilla takes it from the hand 0.1 s after the press, so this nets to nothing)
                string why = Text(key, arg);
                w.SetBlockRPC(kit, BlockValue.Air);
                // the prefab editor never takes the kit from the hand (decInventoryLater), so nothing goes back there
                bool kept = gm.IsEditMode() || GiveBack(placer, now.type);
                if (!kept)
                    gm.ItemDropServer(new ItemStack(new ItemValue(now.type), 1), new Vector3(kit.x + 0.5f, kit.y + 0.5f, kit.z + 0.5f), Vector3.zero, owner, DroppedKitSeconds);
                ScLog.Out("guard tower at " + P(kit) + " not built: " + why + " - the kit " + (kept ? "went back to the placer's inventory" : "was dropped there"));
                Tell(placer, Text(kept ? "scTowerFailed" : "scTowerFailedDropped", why), false);
                return;
            }
            Stamp(w, plan);
            ScLog.Out("guard tower built at " + P(kit) + " for " + (placer != null ? placer.PlayerDisplayName : "?") + " (kit rotation " + now.rotation
                      + ", turned " + plan.Turns * 90 + " deg): " + Counts(plan));
        }

        /// <summary>A refused kit left on the ground lasts this long (vanilla's default is 60 s, and the kit is 782 wood).</summary>
        private const float DroppedKitSeconds = 3600f;

        /// <summary>A refused kit back into the local placer's toolbelt or backpack (false: a remote player, or no room).</summary>
        private static bool GiveBack(EntityPlayer placer, int type)
        {
            EntityPlayerLocal local = placer as EntityPlayerLocal;
            if (local == null || local.inventory == null) return false;
            int slot;
            if (local.inventory.AddItem(new ItemStack(new ItemValue(type), 1), out slot)) return true;
            return local.bag != null && local.bag.AddItem(new ItemStack(new ItemValue(type), 1));
        }

        /// <summary>A player's entity id as the owner of the tower's blocks, or -1 (Chunk.SetBlock looks it up with no null check).</summary>
        public static int Owner(int entityId)
        {
            PersistentPlayerList players = GameManager.Instance != null ? GameManager.Instance.persistentPlayers : null;
            if (entityId == -1 || players == null || players.GetPlayerDataFromEntityID(entityId) == null) return -1;
            return entityId;
        }

        /// <summary>Writes a fitting plan: one SetBlocksRPC, then the terrain under the legs snapped as a normal placement does.</summary>
        public static void Stamp(World w, Plan plan)
        {
            GameManager.Instance.SetBlocksRPC(plan.Changes);
            for (int i = 0; i < plan.SnapAt.Count; i++) w.ChunkCache.SnapTerrainToPositionAroundRPC(w, plan.SnapAt[i]);
        }

        public static string Counts(Plan plan)
        {
            return plan.Blocks + " block(s), " + plan.Legs + " of them reaching down to the ground, " + plan.Cleared + " cell(s) cleared"
                   + (plan.HasPost ? ", Watch post at " + P(plan.Post) : ", no Watch post");
        }

        /// <summary>sc tower info: the prefab, its anchor and post, and its blocks by name.</summary>
        public static string InfoText()
        {
            Prefab p = Get();
            if (p == null) return Localization.Get("scTowerNoPrefab");
            var counts = new SortedDictionary<string, int>();
            for (int y = 0; y < p.size.y; y++)
                for (int x = 0; x < p.size.x; x++)
                    for (int z = 0; z < p.size.z; z++)
                    {
                        BlockValue bv = p.GetBlock(x, y, z);
                        if (bv.isair || bv.ischild) continue;
                        string n = bv.Block.GetBlockName();
                        int c;
                        counts.TryGetValue(n, out c);
                        counts[n] = c + 1;
                    }
            var sb = new StringBuilder();
            sb.Append(PrefabName).Append(": size ").Append(P(p.size)).Append(", yOffset ").Append(p.yOffset).Append(", anchor ").Append(P(Anchor(p)));
            if (hasPost) sb.Append(", Watch post at ").Append(P(postCell)).Append(" rotation ").Append(p.GetBlock(postCell.x, postCell.y, postCell.z).rotation);
            sb.Append('\n').Append("blocks:");
            foreach (var kv in counts) sb.Append(' ').Append(kv.Key).Append(" x").Append(kv.Value).Append(',');
            sb.Length--;
            sb.Append('\n').Append("legs reach down at most ").Append(ScConfig.GuardTowerFill).Append(", terrain cut at most ").Append(ScConfig.GuardTowerCut)
              .Append(" layer(s), preview ").Append(ScConfig.GuardTowerGhost);
            return sb.ToString();
        }

        /// <summary>
        /// The ghost of the whole tower rides vanilla's placement ghost: shown while that ghost is (the kit held, aimed in
        /// range), at its cell and rotation, green / yellow / white / red as vanilla colours its own. Client side, visual.
        /// </summary>
        [HarmonyPatch(typeof(RenderDisplacedCube), nameof(RenderDisplacedCube.Update))]
        public static class Patch_FocusPreview
        {
            [HarmonyPostfix]
            public static void Postfix(RenderDisplacedCube __instance, World _world, EntityAlive _player, PersistentPlayerData _ppLocal)
            {
                try
                {
                    Inventory inv = _player != null ? _player.inventory : null;
                    if (inv == null || !(inv.holdingItemItemValue.ToBlockValue().Block is BlockScGuardTower) || __instance.transformBlockPreview == null || _world == null)
                    {
                        ScTowerGhost.Hide();
                        return;
                    }
                    Vector3i pos = __instance.focusTransformPosition;
                    BlockValue bv = __instance.focusTransformBlockValue;
                    // the kit's own test (vanilla's base tests on its cell + the whole tower, cached), then vanilla's
                    // other tests on the cell: claims and a body in it - exactly what makes vanilla's cube red
                    bool ok = bv.Block.CanPlaceBlockAt(_world, pos, bv);
                    if (ok && _ppLocal != null) ok = _world.CanPlaceBlockAt(pos, _ppLocal);
                    if (ok) ok = _player.IsGodMode.Value || !GameUtils.IsColliderWithinBlock(pos, bv);
                    Color color = Color.red;
                    if (ok)
                    {
                        color = Color.white;
                        switch (_world.GetLandClaimOwner(pos, _ppLocal))
                        {
                            case EnumLandClaimOwner.Self: color = Color.green; break;
                            case EnumLandClaimOwner.Ally: color = Color.yellow; break;
                        }
                    }
                    ScTowerGhost.Show(Get(), pos, Turns(bv.rotation), ok, color, __instance.transparencyFade, __instance);
                }
                catch (Exception e)
                {
                    ScLog.Once("tower-preview", e);
                    ScTowerGhost.Hide();
                }
            }
        }
    }

    /// <summary>
    /// The see-through tower: Prefab.ToTransform once per game (vanilla's paste preview), colliders off, its materials
    /// swapped to the shaders vanilla's block ghost uses. GuardTowerGhost: glow (additive, both states), solid (vanilla's
    /// look: opaque when it fits, additive when not), plain (one flat transparent tint), off.
    /// </summary>
    public static class ScTowerGhost
    {
        private static GameObject root;
        private static Transform pivot;
        private static readonly List<Material> mats = new List<Material>();
        private static Material plain;
        private static Prefab builtFor;
        private static string style;
        private static bool shown;
        private static int shownFrame = -10;

        public static void Reset()
        {
            try
            {
                for (int i = 0; i < mats.Count; i++) if (mats[i] != null && mats[i] != plain) UnityEngine.Object.Destroy(mats[i]);
                if (plain != null) UnityEngine.Object.Destroy(plain);
                if (root != null)
                {
                    // the chunk meshes ToTransform made come from VoxelMesh's pool: back to it, as vanilla's ghost does
                    // (only those under a "Meshes" node - a model block's meshes are its prefab's own)
                    foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
                    {
                        if (mf.sharedMesh == null || mf.transform.parent == null || mf.transform.parent.name != "Meshes") continue;
                        VoxelMesh.AddPooledMesh(mf.sharedMesh);
                        mf.sharedMesh = null;   // pooled once only, even if an upload callback still holds the filter
                    }
                    UnityEngine.Object.Destroy(root);
                }
            }
            catch (Exception e)
            {
                ScLog.Once("tower-ghost-reset", e);
            }
            mats.Clear();
            plain = null;
            root = null;
            pivot = null;
            builtFor = null;
            style = null;
            shown = false;
        }

        public static void Hide()
        {
            if (!shown) return;
            shown = false;
            if (pivot != null) pivot.gameObject.SetActive(false);
        }

        /// <summary>The keeper's LateUpdate: hidden as soon as vanilla stops updating its ghost (a menu open, the item put away).</summary>
        internal static void Tick()
        {
            if (shown && Time.frameCount - shownFrame > 1) Hide();
        }

        public static void Show(Prefab p, Vector3i kit, int turns, bool ok, Color color, float fade, RenderDisplacedCube rdc)
        {
            if (p == null) { Hide(); return; }
            if (style == null) style = (ScConfig.GuardTowerGhost ?? "glow").ToLowerInvariant();
            if (style == "off") return;
            if (root == null || builtFor != p)
            {
                Reset();
                style = (ScConfig.GuardTowerGhost ?? "glow").ToLowerInvariant();
                Build(p, rdc);
                if (root == null) return;
            }
            shownFrame = Time.frameCount;
            if (!shown)
            {
                shown = true;
                pivot.gameObject.SetActive(true);
            }
            root.transform.SetPositionAndRotation(new Vector3(kit.x + 0.5f, kit.y, kit.z + 0.5f) - Origin.position, Quaternion.AngleAxis(90f * turns, Vector3.up));
            if (style == "plain")
            {
                if (plain != null) plain.color = new Color(color.r, color.g, color.b, 0.35f * fade);
                return;
            }
            // vanilla's block ghost: a dimmed tint, the alpha fading in while the player stands still, half when it can't go
            Color c = new Color(color.r * 0.5f, color.g * 0.5f, color.b * 0.5f, color.a);
            float alpha = ok ? fade : fade * 0.5f;
            bool opaque = style == "solid" && ok;
            for (int i = 0; i < mats.Count; i++)
            {
                Material m = mats[i];
                if (m == null) continue;
                m.SetInt("_BlendModeSrc", opaque ? 1 : 5);
                m.SetInt("_BlendModeDest", opaque ? 0 : 1);
                m.SetFloat("_Alpha", alpha);
                m.SetColor("_Color", c);
                m.SetFloat("_Stability", 1f);
            }
        }

        private static void Build(Prefab p, RenderDisplacedCube rdc)
        {
            try
            {
                root = new GameObject("ScGuardTowerGhost");
                root.AddComponent<ScTowerGhostKeeper>();
                pivot = new GameObject("Pivot").transform;
                pivot.SetParent(root.transform, false);
                // the anchor cell's bottom centre sits on the root, which goes to the kit cell's bottom centre and turns there
                Vector3i a = ScTower.Anchor(p);
                pivot.localPosition = new Vector3(-(a.x + 0.5f), -a.y, -(a.z + 0.5f));
                ThreadManager.RunCoroutineSync(p.ToTransform(true, true, true, false, pivot, "PrefabImposter", Vector3.zero, 0));
                Transform imposter = pivot.Find("PrefabImposter");
                if (imposter != null)
                {
                    imposter.localPosition = Vector3.zero;
                    imposter.localRotation = Quaternion.identity;
                }
                foreach (Collider col in root.GetComponentsInChildren<Collider>(true)) col.enabled = false;
                int layer = LayerMask.NameToLayer("NotInReflections");
                if (layer >= 0) Utils.SetLayerRecursively(root, layer);
                if (style == "plain")
                {
                    Shader s = Shader.Find("Transparent/Diffuse");
                    if (s != null) plain = new Material(s);
                }
                foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                {
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    r.receiveShadows = false;
                    if (style == "plain")
                    {
                        if (plain == null) continue;
                        var arr = new Material[Math.Max(1, r.sharedMaterials.Length)];
                        for (int i = 0; i < arr.Length; i++) arr[i] = plain;
                        r.sharedMaterials = arr;
                        continue;
                    }
                    Material[] ms = r.materials;
                    for (int i = 0; i < ms.Length; i++)
                    {
                        Material m = ms[i];
                        if (m == null || m.shader == null) continue;
                        string n = m.shader.name ?? "";
                        if (n.StartsWith("Game/Debug")) continue;
                        if (n.EndsWith("_TA"))
                        {
                            if (rdc.objectShader_TA != null) m.shader = rdc.objectShader_TA;
                        }
                        else if (n.IndexOf("Terrain", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            if (rdc.terrainShader != null) m.shader = rdc.terrainShader;
                        }
                        else if (rdc.objectShader != null)
                        {
                            m.shader = rdc.objectShader;
                            m.SetFloat("_Cutoff", 0.038f);
                            if (m.HasProperty("_Albedo") && m.HasProperty("_MainTex")) m.SetTexture("_MainTex", m.GetTexture("_Albedo"));
                        }
                        mats.Add(m);
                    }
                    r.materials = ms;
                }
                if (plain != null) mats.Add(plain);
                pivot.gameObject.SetActive(false);
                shown = false;
                builtFor = p;
                ScLog.Out("guard tower preview built (" + mats.Count + " material(s), style " + style + ")");
            }
            catch (Exception e)
            {
                ScLog.Once("tower-ghost-build", e);
                Reset();
                style = "off";   // not rebuilt every frame after a failure; a new game tries again
            }
        }
    }

    /// <summary>Hides the tower ghost the frame vanilla stops showing its own.</summary>
    public class ScTowerGhostKeeper : MonoBehaviour
    {
        private void LateUpdate()
        {
            ScTowerGhost.Tick();
        }
    }
}
