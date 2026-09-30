using System;
using System.Collections.Generic;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// The community verbs. Host / single player only: every mutator refuses on a client.
    ///
    /// Membership is layered on SCore's hire machinery rather than replacing it:
    ///   - a FOLLOWING member is a normal SCore hire (Leader/Owner cvars, hired_* on the player, Follow order);
    ///   - a RESIDENT is the FarmHere shape (EntityUtilities.cs:1443-1471): no Leader/Owner, so the NPC never
    ///     teleports after the player and is never in the party, a Guard order at a post inside the zone
    ///     (buffOrderGuard sets CurrentOrder=9 and stamps the guard position from the NPC's spot; the post is
    ///     written over it), Persist=1 so the chunk saves them, StaticSpawner so nothing despawns them, and the
    ///     SCResident AI package for roaming around the post by day.
    /// The registry is the truth; NPC cvars (sc_member, sc_owner, skills) are a mirror for XML.
    /// </summary>
    public static class ScCommunity
    {
        public const string ResidentPackage = "SCResident";
        public const string OrderDismissBuff = "buffOrderDismiss";
        public const string WindowGroup = "sccommunity";
        /// <summary>SCore's IsHuman counts every "npc"-tagged entity - the fox included - so animals are excluded here.</summary>
        public static readonly FastTags<TagGroup.Global> AnimalTags = FastTags<TagGroup.Global>.Parse("animal,pets,mech");

        public static bool IsHost
        {
            get
            {
                ConnectionManager cm = SingletonMonoBehaviour<ConnectionManager>.Instance;
                return cm != null && cm.IsServer;
            }
        }

        public static World World { get { return GameManager.Instance != null ? GameManager.Instance.World : null; } }

        public static EntityPlayerLocal LocalPlayer
        {
            get { World w = World; return w != null ? w.GetPrimaryPlayer() : null; }
        }

        public static int Today
        {
            get { World w = World; return w != null ? GameUtils.WorldTimeToDays(w.worldTime) : 0; }
        }

        public static string ClassName(EntityAlive npc)
        {
            try
            {
                EntityClass ec;
                if (npc != null && EntityClass.list.TryGetValue(npc.entityClass, out ec) && ec != null) return ec.entityClassName ?? "";
            }
            catch (Exception) { }
            return "";
        }

        public static string DisplayName(EntityAlive npc)
        {
            EntityAliveSDX sdx = npc as EntityAliveSDX;
            if (sdx != null && !string.IsNullOrEmpty(sdx.EntityName)) return sdx.EntityName;
            return npc != null ? (npc.EntityName ?? "?") : "?";
        }

        /// <summary>
        /// An entity class id is the class name's string hash (EntityClass.FromString) - negative for about half
        /// of all classes - so "id &lt; 0" never means "missing": ask the class list.
        /// </summary>
        public static bool ClassExists(int classId) { return EntityClass.list != null && EntityClass.list.ContainsKey(classId); }

        public static EntityAliveSDX FindNpc(int id)
        {
            World w = World;
            return w != null ? w.GetEntity(id) as EntityAliveSDX : null;
        }

        // ---- recruiting ----

        /// <summary>Can this NPC be asked to join? (Host, hireable human EntityAliveSDX, not hired, not a member, roster not full.)</summary>
        public static bool CanRecruit(EntityAlive npc, EntityPlayer player, out string why)
        {
            why = null;
            if (npc != null && npc.Buffs != null && npc.Buffs.HasCustomVar(ScRadio.StandInCvar)) { why = "scNotHireable"; return false; }
            if (!IsHost) { why = "scHostOnly"; return false; }
            EntityAliveSDX sdx = npc as EntityAliveSDX;
            if (sdx == null || !sdx.IsHirable) { why = "scNotHireable"; return false; }
            if (!EntityUtilities.IsHuman(npc.entityId) || npc.HasAnyTags(AnimalTags)) { why = "scNotHireable"; return false; }
            if (ScRegistry.GetMember(npc.entityId) != null) { why = "scAlreadyMember"; return false; }
            if (EntityUtilities.IsHired(npc.entityId)) { why = "scAlreadyHired"; return false; }
            if (ScRegistry.Members.Count >= ScConfig.MaxMembers) { why = "scRosterFull"; return false; }
            return true;
        }

        /// <summary>The recruit is done with their favour: make them a member (no dukes, ever).</summary>
        public static bool Join(EntityAliveSDX npc, EntityPlayerLocal player, out string error)
        {
            error = null;
            try
            {
                if (npc == null || player == null) { error = "scInternal"; return false; }
                string why;
                if (!CanRecruit(npc, player, out why)) { error = why; return false; }
                if (!ScZone.EnsureHome(player)) { error = "scNeedHome"; return false; }
                ScConfig.EnsureParsed();

                var m = new Member
                {
                    Id = npc.entityId,
                    Class = ClassName(npc),
                    Name = DisplayName(npc),
                    Born = npc.WorldTimeBorn,
                    JoinDay = Today,
                    Mood = ScRegistry.Community.Morale
                };
                GameRandom rnd = npc.rand ?? GameManager.Instance.World.GetGameRandom();
                m.Traits.AddRange(ScTraits.Roll(ScConfig.TraitsPerRecruit, rnd));
                ScSkills.RollStart(m, rnd);
                m.Ranged = ScSkills.HoldsRanged(npc);
                m.Look = ScLooks.Get(npc);          // null unless they wear the player's body
                ScRegistry.Members[m.Id] = m;
                ScRegistry.Prospects.Remove(m.Id);

                npc.Buffs.SetCustomVar("Persist", 1f);
                npc.Buffs.RemoveCustomVar("sc_favour");
                npc.Buffs.RemoveCustomVar("sc_favourDone");
                StampCvars(m, npc, player);
                ScTraits.ApplyBuffs(m, npc);
                ApplyAiPackage(npc);

                bool home = ScZone.Contains(player.GetBlockPosition()) && ScZone.HomeLoaded;
                if (home) Station(m, npc);
                else FollowInternal(m, npc, player, MemberStatus.PendingStation);

                ScRegistry.SaveAtomic();
                ScLog.Out(m.Name + " (" + m.Class + ", id " + m.Id + ") joined the community: traits [" + string.Join(",", m.Traits.ToArray())
                          + "] specialty " + m.Specialty + (home ? " - stationed" : " - following until home"));
                Tell(player, string.Format(Localization.Get(home ? "scJoined" : "scJoinedFollow"), m.Name), "");
                return true;
            }
            catch (Exception e)
            {
                ScLog.Once("join", e);
                error = "scInternal";
                return false;
            }
        }

        // ---- stationing ----

        /// <summary>Makes the member a resident at their post (the FarmHere shape). The entity must be loaded.</summary>
        public static void Station(Member m, EntityAliveSDX npc)
        {
            if (m == null || npc == null) return;
            try
            {
                EntityPlayerLocal player = LocalPlayer;
                World w = World;

                // A follower hidden for the leader's vehicle ride (SCore SendOnMission) would stay invisible
                // for ever once the leader link is gone: un-hide before anything else.
                if (npc.Buffs.HasCustomVar("onMission") || !npc.enabled) npc.SendOnMission(false);

                // detach from the party without SCore ever re-stamping it back
                npc.Buffs.RemoveCustomVar("Leader");
                npc.Buffs.RemoveCustomVar("Owner");
                SphereCache.LeaderCache.Remove(npc.entityId);
                if (player != null)
                {
                    if (player.Companions != null) player.Companions.Remove(npc);
                    player.Buffs.RemoveCustomVar("hired_" + npc.entityId);
                    EntityUtilities.CheckForDanglingHires(player.entityId);
                }

                if (m.Post == Vector3i.zero || !ScZone.Contains(m.Post) || (ScZone.IsLoaded(m.Post) && !ValidPost(w, m.Post)))
                    m.Post = ChoosePost(npc.rand ?? (w != null ? w.GetGameRandom() : null));
                Vector3 post = PostCenter(m);
                if (Vector3.Distance(npc.position, post) > 40f && ScZone.IsLoaded(m.Post))
                {
                    npc.SetPosition(post, true);
                }

                // The Guard order WITHOUT buffOrderGuard: that buff's onSelfBuffStart runs one tick later and
                // its GuardHere would overwrite the post with wherever the NPC happens to stand (no leader).
                // EntityUtilities.SetCurrentOrder is a no-op for Guard, so the cvars are written directly -
                // the same two the buff sets - and the post is applied in the same call.
                npc.Buffs.SetCustomVar("CurrentOrder", (float)EntityUtilities.Orders.Guard);
                npc.Buffs.SetCustomVar("Guarding", 2f);
                ApplyPost(npc, post);
                if (npc.moveHelper != null) npc.moveHelper.Stop();

                npc.Buffs.SetCustomVar("Persist", 1f);
                npc.SetSpawnerSource(EnumSpawnerSource.StaticSpawner);
                ApplyAiPackage(npc);
                m.Status = MemberStatus.Resident;
                m.MissingDays = 0;
                ScLog.Verbose(m.Name + " stationed at " + m.Post);
            }
            catch (Exception e)
            {
                ScLog.Once("station", e);
            }
        }

        public static Vector3 PostCenter(Member m)
        {
            return new Vector3(m.Post.x + 0.5f, m.Post.y, m.Post.z + 0.5f);
        }

        /// <summary>
        /// Once a second for a stationed resident: POI furniture, gaps and doorways trap NPC pathing. Stuck =
        /// trying to move (an active move helper or a path) without getting anywhere, the move helper reporting
        /// blocked, or SCore's own in-a-block flag - for StuckSeconds in a row; or far from the post for long.
        /// The cure is a short teleport to the post (or a fresh post when the old one is no longer standable).
        /// </summary>
        public static void Watchdog(Member m, EntityAliveSDX npc)
        {
            try
            {
                if (m == null || npc == null || npc.IsDead()) return;
                Vector3 pos = npc.position;
                float moved = (pos - m.LastPos).sqrMagnitude;
                m.LastPos = pos;
                EntityMoveHelper mh = npc.moveHelper;
                // "wants to move" = a move target more than 1.5 m away (standing at the post or in melee range is not stuck)
                bool wantsToMove = (mh != null && mh.IsActive && (mh.moveToPos - pos).sqrMagnitude > 2.25f)
                                   || (npc.getNavigator() != null && !npc.getNavigator().noPathAndNotPlanningOne());
                bool blocked = (mh != null && mh.BlockedTime > 1.5f) || npc.IsStuck;
                if (blocked || (wantsToMove && moved < 0.04f)) m.StillSeconds++; else m.StillSeconds = 0;
                float away = Vector3.Distance(pos, PostCenter(m));
                if (away > ScConfig.PostRadius * 3 + 12) m.AwaySeconds++; else m.AwaySeconds = 0;
                if (m.StillSeconds >= ScConfig.StuckSeconds) Unstick(m, npc, "no progress for " + m.StillSeconds + " s");
                else if (m.AwaySeconds >= 90) Unstick(m, npc, (int)away + " m from the post for " + m.AwaySeconds + " s");
            }
            catch (Exception e)
            {
                ScLog.Once("watchdog", e);
            }
        }

        public static void Unstick(Member m, EntityAliveSDX npc, string why)
        {
            World w = World;
            if (w == null) return;
            m.StillSeconds = 0;
            m.AwaySeconds = 0;
            if (!Standable(w, m.Post.x, m.Post.y, m.Post.z))
            {
                Vector3i fresh = ChoosePost(w.GetGameRandom());
                if (fresh != Vector3i.zero) m.Post = fresh;
            }
            Vector3 target = PostCenter(m);
            if (npc.getNavigator() != null) npc.getNavigator().clearPath();
            if (npc.moveHelper != null) npc.moveHelper.Stop();
            npc.SetPosition(target, true);
            m.LastPos = target;
            ApplyPost(npc, target);
            UAI.ScRoamState.NextAllowed[npc.entityId] = UnityEngine.Time.time + 30f;
            ScLog.Out(m.Name + " was stuck (" + why + ") - moved back to the post " + m.Post);
        }

        /// <summary>Writes the guard position (and a look direction) for the post.</summary>
        public static void ApplyPost(EntityAliveSDX npc, Vector3 post)
        {
            IEntityOrderReceiverSDX r = npc;
            r.GuardPosition = post;
            r.GuardLookPosition = post + npc.GetForwardVector();
        }

        /// <summary>Two blocks of air over something solid that is not water (water is a block in 3.2, not air).</summary>
        public static bool Standable(World w, int x, int y, int z)
        {
            if (y < 2 || y > 250) return false;
            BlockValue floor = w.GetBlock(new Vector3i(x, y - 1, z));
            return w.GetBlock(new Vector3i(x, y, z)).isair && w.GetBlock(new Vector3i(x, y + 1, z)).isair && !floor.isair && !floor.isWater;
        }

        /// <summary>Standable cell; true for an unloaded chunk (unknown).</summary>
        public static bool ValidPost(World w, Vector3i p)
        {
            if (w == null || w.GetChunkSync(World.toChunkXZ(p.x), World.toChunkXZ(p.z)) == null) return true;
            return Standable(w, p.x, p.y, p.z);
        }

        /// <summary>
        /// The standable cell NEAREST the land claim's own height in this column (0, -1, +1, -2, +2 ... within
        /// -10..+6) - the live block column, not the world's static heightmap. The claim's level is by far the
        /// most likely floor: searching top-down would put every post in a roofed base on the roof, and every
        /// post of a shallow underground base on the surface. False when the column has none in range.
        /// </summary>
        public static bool FindStandable(World w, int x, int z, int yRef, out int y)
        {
            for (int step = 0; step <= 10; step++)
            {
                if (Standable(w, x, yRef - step, z)) { y = yRef - step; return true; }
                if (step > 0 && step <= 6 && Standable(w, x, yRef + step, z)) { y = yRef + step; return true; }
            }
            y = yRef;
            return false;
        }

        /// <summary>
        /// A free spot near the home block inside the zone, spread away from other posts. While the chunk is
        /// unloaded the height is the land claim's own; Station re-validates it once the chunk is there.
        /// </summary>
        public static Vector3i ChoosePost(GameRandom rnd)
        {
            Vector3i home = ScZone.Home;
            World w = World;
            Vector3i best = new Vector3i(home.x + 2, home.y, home.z + 2);
            float bestScore = -1f;
            int r = Math.Max(2, ScConfig.PostRadius);
            for (int attempt = 0; attempt < 24; attempt++)
            {
                int x = home.x + (rnd != null ? rnd.RandomRange(-r, r + 1) : attempt - r);
                int z = home.z + (rnd != null ? rnd.RandomRange(-r, r + 1) : r - attempt);
                if (x == home.x && z == home.z) continue;
                Vector3i p = new Vector3i(x, home.y, z);
                if (!ScZone.Contains(p)) continue;
                if (w != null && w.GetChunkSync(World.toChunkXZ(x), World.toChunkXZ(z)) != null)
                {
                    int y;
                    if (!FindStandable(w, x, z, home.y, out y)) continue;      // wall / solid column: try another
                    p = new Vector3i(x, y, z);
                }
                float score = float.MaxValue;
                foreach (var other in ScRegistry.Members.Values)
                {
                    if (other.Post == Vector3i.zero) continue;
                    float d = Vector3.Distance(new Vector3(other.Post.x, 0, other.Post.z), new Vector3(x, 0, z));
                    if (d < score) score = d;
                }
                if (score == float.MaxValue) score = 100f;
                if (score > bestScore) { bestScore = score; best = p; }
                if (bestScore >= 3f && attempt >= 8) break;
            }
            return best;
        }

        // ---- following ----

        public static bool Follow(Member m, EntityAliveSDX npc, EntityPlayerLocal player, out string error)
        {
            error = null;
            if (!IsHost) { error = "scHostOnly"; return false; }
            if (m == null || npc == null || player == null) { error = "scNotLoaded"; return false; }
            if (m.Status == MemberStatus.Following) return true;
            if (m.Status == MemberStatus.OnRun) { error = "scOnRun"; return false; }
            if (m.Status == MemberStatus.Garrison) { error = "scOpPostedAway"; return false; }
            if (ScRegistry.FollowerCount >= ScConfig.MaxFollowers) { error = "scFollowersFull"; return false; }
            if (m.Mood < 25) { error = "scRefuseMood"; return false; }
            FollowInternal(m, npc, player, MemberStatus.Following);
            ScRegistry.SaveAtomic();
            return true;
        }

        private static void FollowInternal(Member m, EntityAliveSDX npc, EntityPlayerLocal player, MemberStatus status)
        {
            EntityUtilities.SetLeaderAndOwner(npc.entityId, player.entityId);   // Follow order, hired_*, cache, StaticSpawner
            npc.Buffs.SetCustomVar("Persist", 1f);
            m.Status = status;
            ScLog.Verbose(m.Name + " now " + status);
        }

        /// <summary>The mod frame that asked for a member-changing verb, for the log (dev aid, not hot-path).</summary>
        private static string Caller()
        {
            try
            {
                var trace = new System.Diagnostics.StackTrace(2, false);
                for (int i = 0; i < trace.FrameCount && i < 6; i++)
                {
                    var mb = trace.GetFrame(i).GetMethod();
                    if (mb == null || mb.DeclaringType == null) continue;
                    string t = mb.DeclaringType.Name;
                    if (t == "ScCommunity") continue;
                    return t + "." + mb.Name;
                }
            }
            catch (Exception) { }
            return "?";
        }

        public static bool ReturnHome(Member m, EntityAliveSDX npc, EntityPlayerLocal player, out string error)
        {
            error = null;
            if (!IsHost) { error = "scHostOnly"; return false; }
            if (m == null || npc == null) { error = "scNotLoaded"; return false; }
            if (m.Status == MemberStatus.Resident) return true;
            if (m.Status == MemberStatus.OnRun) { error = "scOnRun"; return false; }
            if (m.Status == MemberStatus.Garrison) { ScGarrison.Recall(m, player, "scOpRecalled"); ScRegistry.SaveAtomic(); return true; }
            if (!ScZone.HasHome) { error = "scNeedHome"; return false; }
            Vector3i here = npc.GetBlockPosition();
            bool nearPost = m.Post != Vector3i.zero && Vector3.Distance(npc.position, new Vector3(m.Post.x, m.Post.y, m.Post.z)) < 40f;
            if (ScZone.Contains(here) || nearPost)
            {
                ScLog.Out(m.Name + ": sent home from " + Caller() + " (status " + m.Status + ", order " + EntityUtilities.GetCurrentOrder(m.Id) + ")");
                Station(m, npc);
            }
            else if (player != null && ScZone.Contains(player.GetBlockPosition()) && ScZone.HomeLoaded)
            {
                Station(m, npc);    // teleports to the post (chunk loaded)
            }
            else
            {
                error = "scBringThemHome";
                return false;
            }
            ScRegistry.SaveAtomic();
            return true;
        }

        // ---- leaving / dying ----

        /// <summary>
        /// Removes a member. The entity may be unloaded (null): the registry is cleaned either way and the
        /// entity is queued for cleanup the next time it loads, so no stale sc_* mirror survives on it.
        /// </summary>
        public static void Leave(Member m, string reasonKey)
        {
            ScLog.Out("Leave requested for " + (m != null ? m.Name : "?") + " (" + (reasonKey ?? "-") + ") from " + Caller());
            if (m == null) return;
            try
            {
                ScGarrison.RemoveStandIn(m);          // a posted member leaving takes their stand-in with them
                EntityAliveSDX npc = FindNpc(m.Id);
                EntityPlayerLocal player = LocalPlayer;
                if (npc != null) CleanupStale(npc);
                else ScRegistry.Cleanup.Add(m.Id);
                if (player != null)
                {
                    player.Buffs.RemoveCustomVar("hired_" + m.Id);
                    EntityUtilities.CheckForDanglingHires(player.entityId);
                    Tell(player, string.Format(Localization.Get(reasonKey ?? "scLeft"), m.Name), "ui_denied");
                }
                ScRegistry.Members.Remove(m.Id);
                ScLog.Out(m.Name + " left the community (" + (reasonKey ?? "dismissed") + ")");
                ScRegistry.SaveAtomic();
            }
            catch (Exception e)
            {
                ScLog.Once("leave", e);
            }
        }

        /// <summary>Does this entity carry the mod's marks at all? (The cleanup queue must never strip an innocent NPC that inherited a purged id.)</summary>
        public static bool CarriesOurMarks(EntityAliveSDX npc)
        {
            return npc != null && npc.Buffs != null && (npc.Buffs.HasCustomVar("sc_member") || npc.Buffs.HasCustomVar("sc_favour"));
        }

        /// <summary>
        /// Strips everything the mod ever put on an NPC (cvars, buffs, AI package, persistence pin, leader
        /// link). Used when a member leaves and for an entity that comes back after its record was purged.
        /// </summary>
        public static void CleanupStale(EntityAliveSDX npc)
        {
            if (npc == null) return;
            try
            {
                ScConfig.EnsureParsed();     // the trait table must be known to strip the trait buffs
                EntityPlayerLocal player = LocalPlayer;
                ClearCvars(npc);
                ScSkills.ClearCvars(npc);
                ScTraits.RemoveAllBuffs(npc);
                npc.Buffs.RemoveCustomVar("sc_favour");
                npc.Buffs.RemoveCustomVar("sc_favourDone");
                if (npc.AIPackages != null) npc.AIPackages.Remove(ResidentPackage);
                npc.Buffs.RemoveCustomVar("Persist");
                if (npc.Buffs.HasCustomVar("onMission")) npc.SendOnMission(false);
                npc.Buffs.AddBuff(OrderDismissBuff);       // zeroes Leader/Owner, wander
                npc.Buffs.RemoveCustomVar("Leader");
                npc.Buffs.RemoveCustomVar("Owner");
                npc.SetSpawnerSource(EnumSpawnerSource.Biome);
                SphereCache.LeaderCache.Remove(npc.entityId);
                if (player != null)
                {
                    if (player.Companions != null) player.Companions.Remove(npc);
                    player.Buffs.RemoveCustomVar("hired_" + npc.entityId);
                }
            }
            catch (Exception e)
            {
                ScLog.Once("cleanup-stale", e);
            }
        }

        /// <summary>EntityAliveSDX.SetDead postfix.</summary>
        public static void OnMemberDied(EntityAliveSDX npc)
        {
            if (npc == null) return;
            try
            {
                Prospect p = ScRegistry.GetProspect(npc.entityId);
                if (p != null)
                {
                    // an unfinished favour quest would otherwise linger in the journal
                    if (p.Favour == FavourKind.Poi && !p.Done && LocalPlayer != null) ScQuests.Close(LocalPlayer, p.PoiPos, false);
                    ScRegistry.Prospects.Remove(npc.entityId);
                    ScRegistry.SaveAtomic();
                }
                Member m = ScRegistry.GetMember(npc.entityId);
                if (m == null) return;
                RecordDeath(m, LocalPlayer, "scDied");
            }
            catch (Exception e)
            {
                ScLog.Once("died", e);
            }
        }

        /// <summary>Registry side of a death (SetDead or a supply run): roster, deaths list, morale penalty, tooltip.</summary>
        public static void RecordDeath(Member m, EntityPlayerLocal player, string tooltipKey)
        {
            if (m == null) return;
            if (m.Status == MemberStatus.Garrison) ScGarrison.RemoveStandIn(m);
            ScRegistry.Members.Remove(m.Id);
            Community c = ScRegistry.Community;
            c.Deaths.Insert(0, new KeyValuePair<int, string>(Today, m.Name));
            while (c.Deaths.Count > 5) c.Deaths.RemoveAt(c.Deaths.Count - 1);
            c.DeathPenalty += ScConfig.DeathPenalty;
            if (c.ThreatMemberId == m.Id) c.ThreatMemberId = -1;
            ScMorale.Recompute();
            if (player != null)
            {
                player.Buffs.RemoveCustomVar("hired_" + m.Id);
                Tell(player, string.Format(Localization.Get(tooltipKey ?? "scDied"), m.Name), "ui_denied");
            }
            ScLog.Out(m.Name + " died - community morale now " + c.Morale);
            ScRegistry.SaveAtomic();
        }

        // ---- mirrors ----

        public static void StampCvars(Member m, EntityAliveSDX npc, EntityPlayer owner)
        {
            if (m == null || npc == null || npc.Buffs == null) return;
            npc.Buffs.SetCustomVar("sc_member", 1f);
            if (owner != null) npc.Buffs.SetCustomVar("sc_owner", owner.entityId);
            StampFollowing(m, npc);
            ScSkills.StampCvars(m, npc);
        }

        /// <summary>Mirrors "is with the player right now" so the dialog offers only the order that applies.</summary>
        public static void StampFollowing(Member m, EntityAliveSDX npc)
        {
            if (m == null || npc == null || npc.Buffs == null) return;
            bool with = m.Status == MemberStatus.Following || m.Status == MemberStatus.PendingStation;
            if (with) npc.Buffs.SetCustomVar("sc_following", 1f);
            else if (npc.Buffs.HasCustomVar("sc_following")) npc.Buffs.RemoveCustomVar("sc_following");
        }

        public static void ClearCvars(EntityAliveSDX npc)
        {
            if (npc == null || npc.Buffs == null) return;
            npc.Buffs.RemoveCustomVar("sc_member");
            npc.Buffs.RemoveCustomVar("sc_owner");
            npc.Buffs.RemoveCustomVar("sc_following");
        }

        /// <summary>Adds the resident AI package at runtime (the list is rebuilt from the class on every load).</summary>
        public static void ApplyAiPackage(EntityAlive npc)
        {
            if (npc == null || npc.AIPackages == null) return;
            if (!UAI.UAIBase.AIPackages.ContainsKey(ResidentPackage))
            {
                ScLog.Once("no-resident-package", "ai_package " + ResidentPackage + " is not registered - Config/utilityai.xml did not apply (residents will only guard)");
                return;
            }
            if (!npc.AIPackages.Contains(ResidentPackage)) npc.AIPackages.Add(ResidentPackage);
        }

        public static void Tell(EntityPlayer player, string text, string sound)
        {
            try
            {
                if (player == null || string.IsNullOrEmpty(text)) return;
                GameManager.ShowTooltipMP(player, text.Replace("{", "{{").Replace("}", "}}"), sound ?? "");
            }
            catch (Exception e)
            {
                ScLog.Once("tell", e);
            }
        }

        /// <summary>Opens the community window for the local player (window group registered by Config/XUi_InGame).</summary>
        /// <summary>The community window on its Outposts tab, with one outpost selected (id &gt; 0) or none.</summary>
        public static void OpenOutposts(EntityPlayerLocal player, int outpostId)
        {
            XUiC_ScCommunity.PendingOutpost = outpostId > 0 ? outpostId : -1;
            OpenWindow(player);
        }

        public static void OpenWindow(EntityPlayerLocal player)
        {
            try
            {
                if (player == null || player.PlayerUI == null || player.PlayerUI.windowManager == null) return;
                GUIWindow win;
                if (!player.PlayerUI.windowManager.TryGetWindow(WindowGroup, out win))
                {
                    Tell(player, Localization.Get("scNoWindow"), "ui_denied");
                    return;
                }
                player.PlayerUI.windowManager.Open(WindowGroup, true);
            }
            catch (Exception e)
            {
                ScLog.Once("open-window", e);
            }
        }
    }
}
