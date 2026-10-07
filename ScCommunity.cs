using System;
using System.Collections.Generic;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// The community verbs. Host / single player only: every mutator refuses on a client.
    ///
    /// Membership is layered on SCore's hire machinery rather than replacing it:
    ///   - a FOLLOWING member is a normal SCore hire (Leader/Owner cvars, hired_* on the player, Follow order). The player
    ///     may give them 0-XNPCCore's voice and order-wheel orders (stay, guard, wander, a route, formations); those
    ///     are held as member states, never read as leaving (ScOrders, ScTicker.Relink);
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
            EntityAlive sdx = ScNpc.As(npc);
            if (sdx != null && !string.IsNullOrEmpty(sdx.EntityName)) return sdx.EntityName;
            return npc != null ? (npc.EntityName ?? "?") : "?";
        }

        /// <summary>
        /// An entity class id is the class name's string hash (EntityClass.FromString) - negative for about half
        /// of all classes - so "id &lt; 0" never means "missing": ask the class list.
        /// </summary>
        public static bool ClassExists(int classId) { return EntityClass.list != null && EntityClass.list.ContainsKey(classId); }

        public static EntityAlive FindNpc(int id)
        {
            World w = World;
            if (w == null) return null;
            // an offline physical runner has no entity: whatever holds the id is a stale copy or a stranger
            Member m = ScRegistry.GetMember(id);
            if (m != null && ScRunners.IsPhysical(m) && m.Offline) return null;
            return ScNpc.As(w.GetEntity(id));
        }

        // ---- recruiting ----

        /// <summary>
        /// Can this NPC be asked to join? (Host, hireable human framework NPC - SCore's legacy class or 0-XNPCCore's V4
        /// survivors - not hired by somebody else, not a member, roster not full.)
        /// </summary>
        public static bool CanRecruit(EntityAlive npc, EntityPlayer player, out string why)
        {
            why = null;
            if (npc != null && npc.Buffs != null && npc.Buffs.HasCustomVar(ScRadio.StandInCvar)) { why = "scNotHireable"; return false; }
            // a raider never joins (M10; belt and braces: its class is Hirable false, so the next checks refuse it too)
            if (ScXnpc.IsRaider(npc)) { why = "scNotHireable"; return false; }
            if (!IsHost) { why = "scHostOnly"; return false; }
            if (!ScNpc.IsFramework(npc) || !ScNpc.IsHirable(npc)) { why = "scNotHireable"; return false; }
            if (!EntityUtilities.IsHuman(npc.entityId) || npc.HasAnyTags(AnimalTags)) { why = "scNotHireable"; return false; }
            if (ScRegistry.GetMember(npc.entityId) != null) { why = "scAlreadyMember"; return false; }
            // Decision D9: a companion THIS player hired the vanilla way may join, and joining ends the paid hire (Join).
            // Someone else's hire may not - the community must not poach another player's companion.
            int hirer = HiredBy(npc);
            if (hirer > 0 && !IsThisPlayer(hirer, player)) { why = "scAlreadyHired"; return false; }
            if (ScRegistry.Members.Count >= ScConfig.MaxMembers) { why = "scRosterFull"; return false; }
            return true;
        }

        /// <summary>
        /// Who hired this NPC (an entity id), or 0: the Leader cvar, else the Owner cvar (SCore's Loot order keeps only
        /// Owner), else SCore's FarmHere owner (Leader and Owner zeroed, the farmer still assigned). The cvars, not
        /// GetLeaderOrOwner: SCore's leader cache answers with the old leader for seconds after a dismiss or a re-hire.
        /// </summary>
        public static int HiredBy(EntityAlive npc)
        {
            if (npc == null || npc.Buffs == null) return 0;
            int id = (int)npc.Buffs.GetCustomVar("Leader");
            if (id <= 0) id = (int)npc.Buffs.GetCustomVar("Owner");
            if (id <= 0) id = (int)npc.Buffs.GetCustomVar("FarmOwnerEntityId");
            return Math.Max(0, id);
        }

        /// <summary>
        /// Is this hirer id the player asking? The player's own entity id, or a dangling id: a stale id of this same
        /// player (the hire link outlives a change of the player's entity id, which ScTicker re-links the same way) is not
        /// "somebody else". An id held by any other loaded entity (another player, an NPC leader) or by another player of
        /// this save who is offline is theirs.
        /// </summary>
        private static bool IsThisPlayer(int hirer, EntityPlayer player)
        {
            if (player == null) return false;
            if (hirer == player.entityId) return true;
            try
            {
                World w = World;
                if (w != null && w.GetEntity(hirer) != null) return false;
                PersistentPlayerList ppl = GameManager.Instance != null ? GameManager.Instance.persistentPlayers : null;
                return ppl == null || ppl.GetPlayerDataFromEntityID(hirer) == null;
            }
            catch (Exception e)
            {
                ScLog.Once("recruit-hirer", e);
                return false;
            }
        }

        /// <summary>The recruit is done with their favour: make them a member (no dukes, ever).</summary>
        public static bool Join(EntityAlive npc, EntityPlayerLocal player, out string error)
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
                m.Loadout = ScLoadout.Current(npc); // their two weapons, so every body re-created for them carries the same (null for others)
                m.Ammo = ScAmmo.Carried(npc, out m.Mags);   // and the rounds and magazines they carry for the gun (ScAmmo keeps it current every 5 s)
                ScRegistry.Members[m.Id] = m;
                // their favour quest ends with the join: one still open is waiting on "return to <name>" (completed),
                // or the favour was skipped (`sc recruit`, removed)
                Prospect pr = ScRegistry.GetProspect(m.Id);
                if (pr != null && pr.Favour == FavourKind.Poi) ScQuests.CloseFavour(player, pr.Id, pr.Done);
                ScRegistry.Prospects.Remove(m.Id);

                // D9: a companion the player hired the vanilla way joins on the same terms as anyone - the paid hire ends
                // here. Voice Control's contract (Retainer*) goes, and as a member it is never seeded again or billed
                // (Patches_VoiceControl); SCore's own hire state (Leader, Owner, hired_*) is what following/stationing
                // re-stamps or removes below, and a FarmHere assignment is the community's to make from now on.
                bool wasHired = HiredBy(npc) > 0;
                bool hadContract = Patches_VoiceControl.StripContract(npc);
                if (npc.Buffs.HasCustomVar("FarmOwnerEntityId")) npc.Buffs.RemoveCustomVar("FarmOwnerEntityId");
                if (wasHired || hadContract)
                    ScLog.Out(m.Name + " was your hired companion - joining ends the paid hire (Voice Control contract " + (hadContract ? "removed" : "none") + ")");

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
        public static void Station(Member m, EntityAlive npc)
        {
            if (m == null || npc == null) return;
            try
            {
                EntityPlayerLocal player = LocalPlayer;
                World w = World;

                // A follower hidden for the leader's vehicle ride (SCore SendOnMission) would stay invisible
                // for ever once the leader link is gone: un-hide before anything else.
                if (npc.Buffs.HasCustomVar("onMission") || !npc.enabled) ScNpc.SendOnMission(npc, false);

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
                // Voice Control's movement state (a formation slot, a MoveTo target, crouch) means nothing at a post, and
                // SCore's SetCrouching honours CrouchOverride for ANY NPC, so a member told to crouch while following would
                // crouch at home for good. The engage stance and follow distance stay for when they follow again (ScOrders).
                ScOrders.ClearVoiceMovement(npc);
                // SCore's SetLeader copied the player's moveSpeed (1.0) onto the follower. On 0-XNPCCore's V4 bodies that is
                // literal metres per second, half the class's walk, so a resident would roam the base at a crawl until the
                // next load; RestoreSpeed reads the class's MoveSpeed back. SCore's legacy class is left as it always was
                // (it restores its own speed whenever it takes an attack target).
                if (ScNpc.IsV4(npc)) ScNpc.RestoreSpeed(npc);
                m.HeldOrder = EntityUtilities.Orders.Guard;
                m.FollowIssued = 0f;

                bool none = m.Post == Vector3i.zero || !ScZone.Contains(m.Post);
                // a guard on watch keeps the Watch post whatever the post rules say (base defence D3: ScWatch checks it and
                // ends the watch when it is gone or blocked)
                if (!ScWatch.OnWatch(m) && (none || (ScZone.IsLoaded(m.Post) && !ValidPost(w, m.Post))))
                {
                    if (!none) m.BadPosts.Add(m.Post);
                    int tier;
                    Vector3i fresh = ChoosePost(npc.rand ?? (w != null ? w.GetGameRandom() : null), m.BadPosts, out tier);
                    if (fresh != Vector3i.zero)
                    {
                        m.Post = fresh;
                        if (tier > TierRelaxed) ScLog.Out(m.Name + "'s post " + m.Post + " is " + TierName(tier) + " (the best spot within PostRadius of the land claim)");
                    }
                    else if (none)
                    {
                        // nowhere at all (every column outside the zone): where they stand, which they evidently can
                        m.Post = npc.GetBlockPosition();
                        ScLog.Warn(m.Name + ": no spot for a post within PostRadius of the land claim - posted where they stand, " + m.Post);
                    }
                    else ScLog.Warn(m.Name + " keeps the post " + m.Post + " although it is not a real one: no other spot within PostRadius of the land claim");
                }
                if (ScZone.IsLoaded(m.Post)) m.PostChecked = true;      // validated just now (CheckPostOnce)
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

        /// <summary>A live enemy within this many metres means the resident is fighting (Watchdog).</summary>
        public const float FightRange = 30f;
        /// <summary>
        /// After a move back to the post made while FIGHTING, the base defence pauses this long (UAI.ScDefendState): an
        /// intruder it could not reach. A plain wedge or the far rule with no enemy about leaves the defence as it was.
        /// </summary>
        public const float DefendRestSeconds = 60f;
        /// <summary>SCResident's Cowardly flee: its target is the enemy it runs FROM, not a fight (LiveFoe).</summary>
        private const string FleeAction = "ScFlee";

        /// <summary>
        /// Once a second for a stationed resident: POI furniture, gaps and doorways trap NPC pathing. Stuck =
        /// trying to move (an active move helper or a path) without getting anywhere, the move helper reporting
        /// blocked, or SCore's own in-a-block flag - for StuckSeconds in a row; or far from the post for long.
        /// The cure is a short teleport to the post (or a fresh post when the old one is no longer standable).
        /// A resident with a live enemy within FightRange (LiveFoe) gets three times StuckSeconds (36 s by default): in a
        /// fight it pushes at a target the player or another zombie blocks, backs into walls and waits on the player's
        /// melee, and a playtest showed the 12 s rule teleport one out of a fight (GUARD_DIAG cause 4) - while a real wedge
        /// is still cured. The far rule stays (PostRadius x 3 + 12 m for 90 s, 36 m by default); in a fight it is never
        /// shorter than DefendBaseRadius + 14 m (36 m at 22), so a defence at the edge of its reach on a small PostRadius
        /// cannot trip it either - the 25 m leash (ScReturn) walks a resident home long before.
        ///
        /// The M11 fix 2 (STUCK_DIAG): Kyle's post was the air over a bathtub, which SCore's Guard can never reach - it
        /// counts a guard as arrived only when the floor of its height is the post's own - so Guard re-planned forever and
        /// this teleported him back onto the tub every 13 s. Now: no sample while the game is paused or the body did not
        /// tick since the last one (the pause menu stops time but not this ticker: a path request pending at the pause kept
        /// "wants to move" true, one teleport every 12 s for the whole pause); stuck AT its post (within AtPostXZ, AtPostDy,
        /// not pushing at a player) a resident gets a new post instead of a teleport to where it already stands, and so does
        /// one moved back RepeatCount times within RepeatWindowSeconds (Repost); with no better post the limit backs off
        /// (StuckShift: 24 s, 48 s ... up to 32 x StuckSeconds) until BackoffResetSeconds pass without a stuck verdict.
        /// The log line names the signal that counted and where the body stood (StuckDiag).
        /// </summary>
        public static void Watchdog(Member m, EntityAlive npc)
        {
            try
            {
                if (m == null || npc == null || npc.IsDead()) return;
                // a frozen world makes no progress and needs none: the pause menu (single player stops time, this ticker
                // runs on), or a body that did not tick since the last sample (a chunk without colliders, a long hitch)
                if (GameManager.Instance != null && GameManager.Instance.IsPaused()) return;
                int ticks = npc.ticksExisted;
                if (ticks == m.LastTicks) return;
                m.LastTicks = ticks;
                Vector3 pos = npc.position;
                float moved = (pos - m.LastPos).sqrMagnitude;
                m.LastPos = pos;
                EntityMoveHelper mh = npc.moveHelper;
                GamePath.PathNavigate nav = npc.getNavigator();
                // "wants to move" = a move target more than 1.5 m away (standing at the post or in melee range is not stuck)
                bool mhFar = mh != null && mh.IsActive && (mh.moveToPos - pos).sqrMagnitude > 2.25f;
                bool navBusy = nav != null && !nav.noPathAndNotPlanningOne();
                bool blockedTime = mh != null && mh.BlockedTime > 1.5f;
                bool isStuck = npc.IsStuck;
                if (blockedTime || isStuck || ((mhFar || navBusy) && moved < 0.04f))
                {
                    if (m.StillSeconds == 0) m.StuckTicks0 = ticks;
                    m.StillSeconds++;
                    // each signal on its own, for the log line (STUCK_DIAG 5c)
                    if (PathPending(npc)) m.SigPlan++;
                    if (nav != null && !nav.noPath()) m.SigPath++;
                    if (mhFar) m.SigMover++;
                    if (blockedTime) m.SigBlocked++;
                    if (isStuck) m.SigIsStuck++;
                }
                else ResetStill(m);
                bool fighting = LiveFoe(npc, FightRange) != null;
                float now = Time.unscaledTime;
                if (m.StuckShift > 0 && now - m.LastStuckAt > BackoffResetSeconds) m.StuckShift = 0;
                // a live home attack (base defence D2): a crowd at the walls blocks more, so a fight gets five times
                // StuckSeconds, and the whole defended area (+ LeashExtra) counts as near the post
                bool wide = ScBaseAttack.WideDefence;
                int stuckLimit = (fighting ? ScConfig.StuckSeconds * (wide ? 5 : 3) : ScConfig.StuckSeconds) << m.StuckShift;
                float farLimit = ScConfig.PostRadius * 3 + 12;
                if (fighting) farLimit = Mathf.Max(farLimit, ScConfig.DefendBaseRadius + 14f);
                float away = Vector3.Distance(pos, PostCenter(m));
                if (away > farLimit && !(wide && ScBaseAttack.InDefenceArea(pos, ScBaseAttack.LeashExtra))) m.AwaySeconds++; else m.AwaySeconds = 0;
                if (m.StillSeconds >= stuckLimit) Stuck(m, npc, "no progress for " + m.StillSeconds + " s" + (fighting ? ", fighting" : ""), fighting, now);
                else if (m.AwaySeconds >= 90) Stuck(m, npc, (int)away + " m from the post for " + m.AwaySeconds + " s" + (fighting ? ", fighting" : ""), fighting, now);
            }
            catch (Exception e)
            {
                ScLog.Once("watchdog", e);
            }
        }

        /// <summary>Stuck at the post: within this many metres of it horizontally (STUCK_DIAG's loop protection)...</summary>
        private const float AtPostXZ = 1f;
        /// <summary>...and this far above or below its floor (on a tub at 0.72 m, on a low terrain surface).</summary>
        private const float AtPostDy = 1.5f;
        /// <summary>This many moves back to the post within RepeatWindowSeconds (real time) give it a new post instead.</summary>
        private const int RepeatCount = 3;
        private const float RepeatWindowSeconds = 180f;
        /// <summary>StuckShift's cap (StuckSeconds x 32, 6.4 min at 12 s) and the quiet time that resets it (ten minutes).</summary>
        private const int MaxStuckShift = 5;
        private const float BackoffResetSeconds = 600f;

        /// <summary>A path request for this NPC is pending in the path finder (or its finished path is not collected yet).</summary>
        public static bool PathPending(EntityAlive npc)
        {
            return GamePath.PathFinderThread.Instance != null && GamePath.PathFinderThread.Instance.IsCalculatingPath(npc.entityId);
        }

        private static void ResetStill(Member m)
        {
            m.StillSeconds = 0;
            m.SigPlan = m.SigPath = m.SigMover = m.SigBlocked = m.SigIsStuck = 0;
        }

        /// <summary>
        /// A stuck verdict (Watchdog). Not fighting: stuck AT the post gets a new post and no teleport (Repost; with none to
        /// give, the back-off), and so does the RepeatCount-th move back within RepeatWindowSeconds (with none to give, it is
        /// moved back as before, the limit backed off). Everything else - a fight included - is the old cure, Unstick.
        /// </summary>
        private static void Stuck(Member m, EntityAlive npc, string why, bool fighting, float now)
        {
            m.LastStuckAt = now;
            string diag = StuckDiag(m, npc);
            if (!fighting)
            {
                Vector3 pos = npc.position;
                float xz = XZ(pos, PostCenter(m)), dy = pos.y - m.Post.y;
                EntityMoveHelper mh = npc.moveHelper;
                if (xz <= AtPostXZ && Mathf.Abs(dy) <= AtPostDy && !(mh != null && mh.BlockedEntity is EntityPlayer))
                {
                    // a teleport to where it already stands cannot help: the post is the problem
                    Repost(m, npc, "stuck at the post itself (" + xz.ToString("0.0", Inv) + " m, dy " + dy.ToString("0.00", Inv) + "; " + why + ")", diag, true);
                    return;
                }
                for (int i = m.UnstickTimes.Count - 1; i >= 0; i--) if (now - m.UnstickTimes[i] > RepeatWindowSeconds) m.UnstickTimes.RemoveAt(i);
                if (m.UnstickTimes.Count >= RepeatCount - 1
                    && Repost(m, npc, RepeatCount + " moves back to the same post within " + (RepeatWindowSeconds / 60f).ToString("0", Inv) + " min (" + why + ")", diag, true))
                    return;
            }
            m.UnstickTimes.Add(now);
            Unstick(m, npc, why, fighting, diag);
        }

        private static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;

        private static float XZ(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>
        /// The live enemy an NPC is busy with within "range" metres, or null: its attack target, its revenge target, or
        /// the target of the UAI action it runs when that is an enemy - SCResident's ScDefendBase walks to an intruder
        /// before anything sets the attack target (MoveToAttackTargetSDX sets it only when it stops). Not ScFlee's target
        /// (the review's M6): a Cowardly member running from an enemy is not fighting it, and one that flees into a corner
        /// is cured in StuckSeconds like any wedge. Main thread.
        /// </summary>
        public static EntityAlive LiveFoe(EntityAlive npc, float range)
        {
            if (npc == null) return null;
            float r2 = range * range;
            EntityAlive t = npc.GetAttackTarget();
            if (IsLiveFoe(npc, t, r2, false)) return t;
            t = npc.GetRevengeTarget();
            if (IsLiveFoe(npc, t, r2, false)) return t;
            UAI.Context ctx = npc.utilityAIContext;
            if (ctx == null || (ctx.ActionData.Action != null && ctx.ActionData.Action.Name == FleeAction)) return null;
            t = ctx.ActionData.Target as EntityAlive;
            return IsLiveFoe(npc, t, r2, true) ? t : null;
        }

        private static bool IsLiveFoe(EntityAlive npc, EntityAlive t, float r2, bool mustBeEnemy)
        {
            return t != null && t != npc && !t.IsDead() && (t.position - npc.position).sqrMagnitude <= r2
                   && (!mustBeEnemy || EntityTargetingUtilities.IsEnemy(npc, t));
        }

        /// <summary>
        /// Moves a stuck resident back to its post. "fighting" (a live enemy within FightRange when the watchdog fired, the
        /// review's M3): the base defence then pauses DefendRestSeconds, so an intruder it could not reach does not pull it
        /// straight back; a wedge with no enemy about leaves the defence alone. A post that is no real post any more (built
        /// over, its floor gone, or never one) is replaced first when a better spot exists (the M11 fix 2: GoodPost, not the
        /// old "two air cells over anything"); any path request still pending goes with the path (DropPath), so it cannot
        /// survive the teleport. diag: the watchdog's details (StuckDiag), appended to the log line.
        /// </summary>
        public static void Unstick(Member m, EntityAlive npc, string why, bool fighting, string diag = null)
        {
            World w = World;
            if (w == null) return;
            ResetStill(m);
            m.AwaySeconds = 0;
            string bad;
            if (ScZone.IsLoaded(m.Post) && !GoodPost(w, m.Post, false, out bad))
            {
                m.BadPosts.Add(m.Post);
                int tier;
                Vector3i fresh = ChoosePost(w.GetGameRandom(), m.BadPosts, out tier);
                // a real floor; or anything at all when the post has no room to stand any more (built over): never a
                // teleport into a block
                if (fresh != Vector3i.zero && (tier <= TierRelaxed || !Standable(w, m.Post.x, m.Post.y, m.Post.z)))
                {
                    ScLog.Out(m.Name + "'s post " + m.Post + " is " + bad + " - new post " + fresh + " (" + TierName(tier) + ")");
                    m.Post = fresh;
                }
            }
            Vector3 target = PostCenter(m);
            DropPath(npc);
            npc.SetPosition(target, true);
            m.LastPos = target;
            ApplyPost(npc, target);
            UAI.ScRoamState.NextAllowed[npc.entityId] = UnityEngine.Time.time + 30f;
            // an intruder it could not reach would pull it straight back to the same spot: the defence pauses a while
            // (what it sees it still fights)
            if (fighting) UAI.ScDefendState.Rest(npc.entityId, DefendRestSeconds);
            ScLog.Out(m.Name + " was stuck (" + why + ") - moved back to the post " + m.Post + (diag != null ? " [" + diag + "]" : ""));
        }

        /// <summary>
        /// Gives a resident a new post (the M11 fix 2, STUCK_DIAG fix b). The old one goes on the member's session blacklist
        /// (Member.BadPosts), ChoosePost picks the best spot left - only a strict or relaxed one: a post on furniture again
        /// would only move the problem (any spot at all only when the old post has no room to stand any more, built over) -
        /// the guard position follows, any path and pending path request is dropped and the
        /// counters start again, and roaming is allowed at once. No teleport: Guard walks it there, and the normal rule still
        /// cures a real wedge on the way. Warned once per member and post, with the watchdog's details (diag). With no such
        /// spot: backOff (the watchdog) doubles the stuck limit, up to 32x (Member.StuckShift), the console and the load check
        /// only say so. True when the post moved.
        /// </summary>
        public static bool Repost(Member m, EntityAlive npc, string why, string diag, bool backOff)
        {
            World w = World;
            if (w == null || m == null || npc == null) return false;
            Vector3i old = m.Post;
            if (old != Vector3i.zero) m.BadPosts.Add(old);
            int tier;
            Vector3i fresh = ChoosePost(npc.rand ?? w.GetGameRandom(), m.BadPosts, out tier);
            ResetStill(m);
            m.AwaySeconds = 0;
            string details = diag != null ? " [" + diag + "]" : "";
            // only a real floor - or anything at all when the old post has no room to stand any more (built over)
            bool oldGone = old != Vector3i.zero && ScZone.IsLoaded(old) && !Standable(w, old.x, old.y, old.z);
            if (fresh == Vector3i.zero || (tier > TierRelaxed && !oldGone))
            {
                if (!backOff)
                {
                    ScLog.Warn(m.Name + "'s post " + old + " cannot be used (" + why + ")" + details + " - but there is no other spot with a real floor within PostRadius; it stays");
                    return false;
                }
                if (m.StuckShift < MaxStuckShift) m.StuckShift++;
                ScLog.Warn(m.Name + "'s post " + old + " cannot be used (" + why + ")" + details + " - no other spot with a real floor within PostRadius: the stuck check now waits "
                           + (ScConfig.StuckSeconds << m.StuckShift) + " s");
                return false;
            }
            m.Post = fresh;
            m.PostChecked = true;
            ApplyPost(npc, PostCenter(m));
            DropPath(npc);
            m.UnstickTimes.Clear();
            m.LastPos = npc.position;
            UAI.ScRoamState.NextAllowed.Remove(npc.entityId);
            ScRegistry.SaveAtomic();
            string line = m.Name + "'s post " + old + " cannot be used (" + why + ")" + details + " - new post " + fresh + " (" + TierName(tier) + ")";
            if (m.WarnedPosts.Add(old)) ScLog.Warn(line);
            else ScLog.Out(line);
            return true;
        }

        /// <summary>
        /// Once per member and session, as soon as the post's chunk is loaded (ScTicker.Relink, residents): a post that is no
        /// real post - built over or its floor broken since, or one chosen before the M11 fix 2 (Kyle's on top of a bathtub)
        /// - is replaced (Repost), with a log line naming what it stood on. Station validates the same way when it runs.
        /// </summary>
        public static void CheckPostOnce(Member m, EntityAlive npc)
        {
            if (m == null || npc == null || m.PostChecked || m.Post == Vector3i.zero || !ScZone.IsLoaded(m.Post)) return;
            m.PostChecked = true;
            World w = World;
            string why;
            if (w == null || GoodPost(w, m.Post, false, out why)) return;
            Repost(m, npc, "the post is " + why + ", checked on load", null, false);
        }

        /// <summary>
        /// Drops the NPC's path, the path request that may still be pending in the path finder (clearPath does not touch
        /// it; vanilla's EAIRunAway drops it the same way - the path "thread" is a main-thread coroutine) and its move target.
        /// </summary>
        public static void DropPath(EntityAlive npc)
        {
            if (npc.getNavigator() != null) npc.getNavigator().clearPath();
            if (GamePath.PathFinderThread.Instance != null) GamePath.PathFinderThread.Instance.RemovePathsFor(npc.entityId);
            if (npc.moveHelper != null) npc.moveHelper.Stop();
        }

        /// <summary>
        /// Writes the guard position (and a look direction) for the post: the way the NPC faces now, or for a guard on watch
        /// the way the Watch post faces (base defence D3, ScWatch.LookAt; HoldPostSC turns them to it).
        /// </summary>
        public static void ApplyPost(EntityAlive npc, Vector3 post)
        {
            Vector3 look;
            if (ScWatch.LookAt(ScRegistry.GetMember(npc.entityId), post, out look)) { ScNpc.SetGuard(npc, post, look); return; }
            ScNpc.SetGuard(npc, post, post + npc.GetForwardVector());
        }

        // ---- the stuck line's details and the console's nav: / post: lines (the M11 fix 2, STUCK_DIAG 5c) ----

        /// <summary>
        /// What the watchdog saw, for the stuck line: per signal the seconds it held while the count ran, where the body
        /// stands against the post (and on what), the cell SCore's Guard compares with the body's (Guard counts itself
        /// arrived only when they are equal), the move helper, the path, the UAI action, how far the body ticked, the pause.
        /// Invariant formatting. Only built for a verdict or the console.
        /// </summary>
        public static string StuckDiag(Member m, EntityAlive npc)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(m.StillSeconds).Append(" s counted: navPlanning ").Append(m.SigPlan).Append(", navPath ").Append(m.SigPath)
              .Append(", moveHelperFar ").Append(m.SigMover).Append(", blockedTime ").Append(m.SigBlocked).Append(", isStuck ").Append(m.SigIsStuck).Append("; ");
            sb.Append(PostLine(m, npc)).Append("; ").Append(NavLine(npc))
              .Append("; entity ticks +").Append(npc.ticksExisted - m.StuckTicks0)
              .Append("; paused=").Append(GameManager.Instance != null && GameManager.Instance.IsPaused() ? "T" : "F");
            return sb.ToString();
        }

        /// <summary>The body against its post: where it stands, the distance and height to the post, what it stands on, Guard's cell against its own.</summary>
        public static string PostLine(Member m, EntityAlive npc)
        {
            var sb = new System.Text.StringBuilder();
            Vector3 pos = npc.position;
            Vector3 post = PostCenter(m);
            sb.Append("at ").Append(V(pos)).Append(" = ").Append(XZ(pos, post).ToString("0.0", Inv)).Append(" m from the post ").Append(m.Post)
              .Append(", dy ").Append((pos.y - m.Post.y).ToString("0.00", Inv));
            World w = World;
            if (w != null) sb.Append(", standing on ").Append(BlockName(w.GetBlock(World.worldToBlockPos(pos - new Vector3(0f, 0.05f, 0f)))));
            Vector3 guard = ScNpc.GuardPosition(npc);
            Vector3 gc = EntityUtilities.CenterPosition(guard), bc = EntityUtilities.CenterPosition(pos);
            sb.Append("; Guard cell ").Append(World.worldToBlockPos(gc)).Append(" vs body cell ").Append(World.worldToBlockPos(bc))
              .Append(gc == bc ? " (Guard: arrived)" : " (Guard: not there yet)");
            if (XZ(guard, post) > 0.01f || Mathf.Abs(guard.y - post.y) > 0.01f) sb.Append(" GUARD POSITION ").Append(V(guard)).Append(" IS NOT THE POST");
            return sb.ToString();
        }

        /// <summary>
        /// A member's post for sc xnpc &lt;id&gt;: the post and the floor under it, GoodPost's strict and relaxed verdicts with
        /// their reasons, where the body stands against it (PostLine), and the watchdog's state: the count and its signals,
        /// recent moves back, the back-off and the posts this member will not get again.
        /// </summary>
        public static string PostReport(Member m, EntityAlive npc)
        {
            var sb = new System.Text.StringBuilder();
            World w = World;
            sb.Append(m.Post);
            if (w != null && ScZone.IsLoaded(m.Post))
            {
                string strictWhy, relaxedWhy;
                bool strict = GoodPost(w, m.Post, true, out strictWhy), relaxed = GoodPost(w, m.Post, false, out relaxedWhy);
                sb.Append(" floor ").Append(BlockName(w.GetBlock(new Vector3i(m.Post.x, m.Post.y - 1, m.Post.z))))
                  .Append(", strict ").Append(strict ? "good" : "NO (" + strictWhy + ")").Append(", relaxed ").Append(relaxed ? "good" : "NO (" + relaxedWhy + ")");
            }
            else sb.Append(" (chunk not loaded)");
            sb.Append("; ").Append(PostLine(m, npc))
              .Append("; watchdog: still ").Append(m.StillSeconds).Append(" s (plan ").Append(m.SigPlan).Append(", path ").Append(m.SigPath).Append(", mh ").Append(m.SigMover)
              .Append(", blocked ").Append(m.SigBlocked).Append(", isStuck ").Append(m.SigIsStuck).Append("), away ").Append(m.AwaySeconds).Append(" s, ")
              .Append(m.UnstickTimes.Count).Append(" move(s) back lately, limit x").Append(1 << m.StuckShift).Append(", checked on load=").Append(m.PostChecked)
              .Append(", not again: ").Append(m.BadPosts.Count > 0 ? string.Join(" ", ToStrings(m.BadPosts)) : "-");
            return sb.ToString();
        }

        private static string[] ToStrings(HashSet<Vector3i> set)
        {
            var list = new List<string>();
            foreach (Vector3i v in set) list.Add(v.ToString());
            return list.ToArray();
        }

        /// <summary>The move helper, the path and the path finder, and the UAI action, for an NPC.</summary>
        public static string NavLine(EntityAlive npc)
        {
            var sb = new System.Text.StringBuilder();
            Vector3 pos = npc.position;
            EntityMoveHelper mh = npc.moveHelper;
            if (mh != null)
                sb.Append("mh active=").Append(mh.IsActive ? "T" : "F").Append(" to ").Append(V(mh.moveToPos)).Append(" (").Append((mh.moveToPos - pos).magnitude.ToString("0.0", Inv))
                  .Append(" m) blockedTime ").Append(mh.BlockedTime.ToString("0.00", Inv)).Append(" flags ").Append(mh.BlockedFlags)
                  .Append(" blockedBy ").Append(mh.BlockedEntity != null ? (mh.BlockedEntity is EntityPlayer ? "PLAYER " : "") + mh.BlockedEntity.entityId : "-");
            else sb.Append("mh -");
            GamePath.PathNavigate nav = npc.getNavigator();
            GamePath.PathEntity path = nav != null ? nav.getPath() : null;
            sb.Append("; nav ").Append(path != null && !path.isFinished() ? "path " + path.getCurrentPathIndex() + "/" + path.getCurrentPathLength() + " end " + V(path.rawEndPos) : "no path")
              .Append(", planning=").Append(PathPending(npc) ? "T" : "F").Append(", onGround=").Append(npc.onGround ? "T" : "F").Append(", isStuck=").Append(npc.IsStuck ? "T" : "F");
            UAI.Context ctx = npc.utilityAIContext;
            if (ctx != null)
            {
                object t = ctx.ActionData.Target;
                EntityAlive ta = t as EntityAlive;
                sb.Append("; uai ").Append(ctx.ActionData.Action != null ? ctx.ActionData.Action.Name : "-").Append('/')
                  .Append(ctx.ActionData.CurrentTask != null ? ctx.ActionData.CurrentTask.GetType().Name : "-").Append(" target ")
                  .Append(ta != null ? (ta == npc ? "self" : ta.entityId.ToString()) : t is Vector3 ? V((Vector3)t) : "-");
            }
            return sb.ToString();
        }

        private static string V(Vector3 v)
        {
            return v.x.ToString("0.00", Inv) + ", " + v.y.ToString("0.00", Inv) + ", " + v.z.ToString("0.00", Inv);
        }

        private static string BlockName(BlockValue v)
        {
            if (v.isair) return "air";
            Block b = v.Block;
            return (b != null ? b.GetBlockName() : "?") + (v.ischild ? " (multi-block part)" : "");
        }

        // ---- what makes a post (the M11 fix 2, STUCK_DIAG fix A1) ----

        /// <summary>ChoosePost's tiers, best first: GoodPost strict, GoodPost relaxed, the old Standable, a column not loaded.</summary>
        public const int TierStrict = 0, TierRelaxed = 1, TierStandable = 2, TierUnloaded = 3;
        /// <summary>A strict post wants at least this many of its 8 neighbours open (two air cells over a real floor).</summary>
        private const int MinOpenNeighbours = 5;
        /// <summary>A strict spot at least this far (XZ) from every other post ends the search (the old "good enough").</summary>
        private const float MinPostSpacing = 3f;

        public static string TierName(int tier)
        {
            switch (tier)
            {
                case TierStrict: return "an open spot on a real floor";
                case TierRelaxed: return "a real floor, but cramped or next to furniture";
                case TierStandable: return "only standable: on furniture or a part block";
                case TierUnloaded: return "unchecked: its chunk is not loaded";
                default: return "none";
            }
        }

        /// <summary>Two blocks of air over something solid that is not water (water is a block in 3.2, not air).</summary>
        public static bool Standable(World w, int x, int y, int z)
        {
            if (y < 2 || y > 250) return false;
            BlockValue floor = w.GetBlock(new Vector3i(x, y - 1, z));
            return w.GetBlock(new Vector3i(x, y, z)).isair && w.GetBlock(new Vector3i(x, y + 1, z)).isair && !floor.isair && !floor.isWater;
        }

        /// <summary>
        /// A floor a body stands ON at the cell's top - what SCore's Guard needs, since it counts a guard as arrived only
        /// when the floor of the body's height is the post's own. Terrain; or a block the pathfinder treats as solid
        /// (Block.PathType &gt; 0: "Path=solid" and the cube models, AstarVoxelGrid's own test) that fills the cell to its top:
        /// its rotated top face is flagged full (BlockShapeNew reads the flag off the model's "T_F" face mesh - verified on
        /// shapes_assets_all.bundle: Cube has it, ramp, plate, wedge_stairs, pole and the 0.75 m cube do not; the old cube
        /// shape is full on every face), or it is one of the three whole-cube models the game itself gives PathType 1
        /// (BlocksFromXml: cube, cube_glass, cube_frame - a frame and a glass cube flag their faces partial because they
        /// are see-through, not short). So a cube, a frame or glass, never a ramp, a pole, a pillar or stairs, which pass
        /// PathType too. Never a model (ModelEntity: furniture, doors, the bathtub), a multi-block or one of its child cells.
        /// Not BlockShape.IsSolidCube: for the shape blocks it only comes from a "Solid" child in the model, which the
        /// shapes' Cube.fbx does not have (STUCK_DIAG), so woodShapes:cube reports false.
        /// </summary>
        public static bool SolidFloor(BlockValue f)
        {
            if (f.isair || f.isWater || f.ischild) return false;
            Block b = f.Block;
            if (b == null || b.shape == null || b.isMultiBlock || b.shape is BlockShapeModelEntity) return false;
            if (b.shape.IsTerrain()) return true;
            // a Watch post of any shape whose rotated top is a full face - the cube, or a plate or catwalk turned upside
            // down (catwalk_plate flags its bottom face full, B_F): the walking surface is the cell's top, as on a cube.
            // The shapes leave Path unset, so a plate never passes PathType (the user's call, 2026-10-07: a post in the
            // catwalk deck). Only for posts; any other plate still is no post floor.
            if (b is BlockScWatchPost && (b.shape.getFacesDrawnFullBitfield(f) & (1 << (int)BlockFace.Top)) != 0) return true;
            if (b.PathType <= 0) return false;
            if ((b.shape.getFacesDrawnFullBitfield(f) & (1 << (int)BlockFace.Top)) != 0) return true;
            BlockShapeNew sn = b.shape as BlockShapeNew;
            return sn != null && IsWholeCubeModel(sn.ShapeName);
        }

        /// <summary>The models BlocksFromXml gives PathType 1 by themselves: whole cubes, whatever their faces' flags.</summary>
        private static bool IsWholeCubeModel(string model)
        {
            return model != null && (model.Equals("@:Shapes/cube.fbx", StringComparison.OrdinalIgnoreCase)
                                     || model.Equals("@:Shapes/cube_glass.fbx", StringComparison.OrdinalIgnoreCase)
                                     || model.Equals("@:Shapes/cube_frame.fbx", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Something a body cannot stand among: a model (furniture, a door), a multi-block or one of its child cells.</summary>
        private static bool IsFixture(BlockValue v)
        {
            if (v.isair) return false;
            Block b = v.Block;
            return v.ischild || (b != null && (b.isMultiBlock || b.shape is BlockShapeModelEntity));
        }

        /// <summary>
        /// A post a resident can reach and stand at. Relaxed: Standable over a SolidFloor. Strict, also: no fixture in the 8
        /// cells around it (at its feet or its head), and at least MinOpenNeighbours of them open - two air cells over a real
        /// floor - so a post is not wedged between furniture or against a wall in a closet. why: the first reason it fails
        /// (e.g. "on decoClawFootBathTub"), null when it is good.
        /// </summary>
        public static bool GoodPost(World w, Vector3i p, bool strict, out string why)
        {
            why = null;
            if (w == null) { why = "no world"; return false; }
            if (!Standable(w, p.x, p.y, p.z)) { why = "no room to stand (two air cells over a floor)"; return false; }
            BlockValue floor = w.GetBlock(new Vector3i(p.x, p.y - 1, p.z));
            if (!SolidFloor(floor)) { why = "on " + BlockName(floor); return false; }
            if (!strict) return true;
            int open = 0;
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (dx == 0 && dz == 0) continue;
                    BlockValue a = w.GetBlock(new Vector3i(p.x + dx, p.y, p.z + dz));
                    BlockValue b = w.GetBlock(new Vector3i(p.x + dx, p.y + 1, p.z + dz));
                    if (IsFixture(a) || IsFixture(b)) { why = "next to " + BlockName(IsFixture(a) ? a : b); return false; }
                    if (a.isair && b.isair && SolidFloor(w.GetBlock(new Vector3i(p.x + dx, p.y - 1, p.z + dz)))) open++;
                }
            }
            if (open < MinOpenNeighbours) { why = "cramped (" + open + " of 8 neighbours open)"; return false; }
            return true;
        }

        /// <summary>A post that is fit to keep (GoodPost relaxed: a real floor); true for an unloaded chunk (unknown).</summary>
        public static bool ValidPost(World w, Vector3i p)
        {
            if (w == null || w.GetChunkSync(World.toChunkXZ(p.x), World.toChunkXZ(p.z)) == null) return true;
            string why;
            return GoodPost(w, p, false, out why);
        }

        /// <summary>
        /// The standable cell NEAREST the land claim's own height in this column (0, -1, +1, -2, +2 ... within
        /// -10..+6) - the live block column, not the world's static heightmap. The claim's level is by far the
        /// most likely floor: searching top-down would put every post in a roofed base on the roof, and every
        /// post of a shallow underground base on the surface. False when the column has none in range. For a post,
        /// FindPost asks more of the floor; this is the plain rule (ScRadio's and ScRunners' drop spots).
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
        /// The best post in one column: the standable heights in FindStandable's order (nearest the claim's level first), the
        /// best tier any of them reaches, at the first height that reaches it. Excluded cells are skipped. False: none.
        /// </summary>
        private static bool FindPost(World w, int x, int z, int yRef, ICollection<Vector3i> exclude, out Vector3i post, out int tier)
        {
            post = Vector3i.zero;
            tier = -1;
            for (int step = 0; step <= 10; step++)
            {
                for (int side = 0; side < 2; side++)
                {
                    if (side == 1 && (step == 0 || step > 6)) continue;
                    int y = side == 0 ? yRef - step : yRef + step;
                    if (!Standable(w, x, y, z)) continue;
                    Vector3i p = new Vector3i(x, y, z);
                    if (exclude != null && exclude.Contains(p)) continue;
                    string why;
                    int t = GoodPost(w, p, true, out why) ? TierStrict : SolidFloor(w.GetBlock(new Vector3i(x, y - 1, z))) ? TierRelaxed : TierStandable;
                    if (tier < 0 || t < tier) { tier = t; post = p; }
                    if (tier == TierStrict) return true;
                }
            }
            return tier >= 0;
        }

        /// <summary>How far (XZ) a column is from the nearest member's post: 100 when there is none.</summary>
        private static float Spacing(int x, int z)
        {
            float score = float.MaxValue;
            foreach (var other in ScRegistry.Members.Values)
            {
                if (other.Post == Vector3i.zero) continue;
                float d = Vector3.Distance(new Vector3(other.Post.x, 0, other.Post.z), new Vector3(x, 0, z));
                if (d < score) score = d;
            }
            return score == float.MaxValue ? 100f : score;
        }

        /// <summary>
        /// A spot near the home block inside the zone for a post, spread away from other posts (the M11 fix 2, STUCK_DIAG fix
        /// A1; it was 24 random tries of "two air cells over anything", which put about 2 in 5 posts of a cabin on furniture,
        /// railings or plates, with an unchecked home + (2, 0, 2) when every try failed). Every column within PostRadius, in
        /// random order, at the height nearest the land claim's own that passes the best tier it can (FindPost): strict, then
        /// relaxed, then standable; the first strict spot at least MinPostSpacing from every other post ends the search, else
        /// the best-spread spot of the best tier wins. A column whose chunk is not loaded is only the last resort, at the
        /// claim's own height (Station re-validates it once the chunk is there). exclude: posts this member must not get again
        /// (Member.BadPosts). tier: the tier of the answer. Vector3i.zero when nothing at all qualifies (every column
        /// excluded or outside the zone): callers keep what they have. Once per stationing or re-post: a few thousand
        /// GetBlock calls.
        /// </summary>
        public static Vector3i ChoosePost(GameRandom rnd, ICollection<Vector3i> exclude, out int tier)
        {
            tier = -1;
            Vector3i home = ScZone.Home;
            World w = World;
            int r = Math.Max(2, ScConfig.PostRadius);
            int side = 2 * r + 1;
            var order = new List<int>(side * side);
            for (int i = 0; i < side * side; i++) order.Add(i);
            for (int i = order.Count - 1; i > 0; i--)
            {
                int j = rnd != null ? rnd.RandomRange(0, i + 1) : UnityEngine.Random.Range(0, i + 1);
                int s = order[i]; order[i] = order[j]; order[j] = s;
            }
            var best = new Vector3i[4];
            var bestScore = new[] { -1f, -1f, -1f, -1f };
            foreach (int idx in order)
            {
                int x = home.x + idx / side - r, z = home.z + idx % side - r;
                if (x == home.x && z == home.z) continue;
                if (!ScZone.Contains(new Vector3i(x, home.y, z))) continue;
                Vector3i p;
                int t;
                if (w == null || w.GetChunkSync(World.toChunkXZ(x), World.toChunkXZ(z)) == null)
                {
                    p = new Vector3i(x, home.y, z);
                    t = TierUnloaded;
                    if (exclude != null && exclude.Contains(p)) continue;
                }
                else if (!FindPost(w, x, z, home.y, exclude, out p, out t)) continue;      // wall / solid column: the next
                float score = Spacing(x, z);
                if (score > bestScore[t]) { bestScore[t] = score; best[t] = p; }
                if (t == TierStrict && score >= MinPostSpacing) break;                     // a random open spot clear of the others
            }
            for (int t = TierStrict; t <= TierUnloaded; t++)
            {
                if (bestScore[t] < 0f) continue;
                tier = t;
                return best[t];
            }
            return Vector3i.zero;
        }

        // ---- following ----

        public static bool Follow(Member m, EntityAlive npc, EntityPlayerLocal player, out string error)
        {
            error = null;
            if (!IsHost) { error = "scHostOnly"; return false; }
            if (m == null || npc == null || player == null) { error = "scNotLoaded"; return false; }
            if (m.Status == MemberStatus.Following) return true;
            if (m.Status == MemberStatus.OnRun) { error = "scOnRun"; return false; }
            if (m.Status == MemberStatus.Garrison) { error = "scOpPostedAway"; return false; }
            if (ScWatch.OnWatch(m)) { error = "scWatchBusy"; return false; }     // relieve them at the Watch post first (D3)
            if (ScRegistry.FollowerCount >= ScConfig.MaxFollowers) { error = "scFollowersFull"; return false; }
            if (m.Mood < 25) { error = "scRefuseMood"; return false; }
            FollowInternal(m, npc, player, MemberStatus.Following);
            ScRegistry.SaveAtomic();
            return true;
        }

        private static void FollowInternal(Member m, EntityAlive npc, EntityPlayerLocal player, MemberStatus status)
        {
            ScNpc.SetLeaderAndOwner(npc.entityId, player.entityId);   // Follow order, hired_*, cache, StaticSpawner, Owner
            npc.Buffs.SetCustomVar("Persist", 1f);
            m.Status = status;
            // SCore's Follow lands a tick later (buffOrderFollow): until the ticker sees it, a Guard still on the NPC is the
            // favour wait or the old station, not a voice order (ScTicker.Relink)
            m.HeldOrder = EntityUtilities.Orders.Follow;
            m.FollowIssued = Mathf.Max(0.001f, Time.unscaledTime);
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

        public static bool ReturnHome(Member m, EntityAlive npc, EntityPlayerLocal player, out string error)
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
                if (m.Status == MemberStatus.OnRun && m.RunDistress) ScRadio.EndDistress(World, LocalPlayer, m);   // no orphaned rescue quest
                m.Snap = null;
                EntityAlive npc = FindNpc(m.Id);
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
                ScRunners.RebuildRunnerSet();
                ScLog.Out(m.Name + " left the community (" + (reasonKey ?? "dismissed") + ")");
                ScRegistry.SaveAtomic();
            }
            catch (Exception e)
            {
                ScLog.Once("leave", e);
            }
        }

        /// <summary>Does this entity carry the mod's marks at all? (The cleanup queue must never strip an innocent NPC that inherited a purged id.)</summary>
        public static bool CarriesOurMarks(EntityAlive npc)
        {
            return npc != null && npc.Buffs != null && (npc.Buffs.HasCustomVar("sc_member") || npc.Buffs.HasCustomVar("sc_favour"));
        }

        /// <summary>
        /// Strips everything the mod ever put on an NPC (cvars, buffs, AI package, persistence pin, leader
        /// link). Used when a member leaves and for an entity that comes back after its record was purged.
        /// </summary>
        public static void CleanupStale(EntityAlive npc)
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
                ScDoors.Enable(npc, false);                // a former member no longer opens the player's doors
                npc.Buffs.RemoveCustomVar("Persist");
                if (npc.Buffs.HasCustomVar("onMission")) ScNpc.SendOnMission(npc, false);
                ScOrders.ClearVoiceMovement(npc);          // a former member must not stay crouched or hold a formation slot
                npc.Buffs.AddBuff(OrderDismissBuff);       // zeroes Leader/Owner, wander
                if (ScNpc.IsV4(npc)) ScNpc.RestoreSpeed(npc);   // a follower's leader speed (SetLeader copies it) must not outlive the membership
                ScWatch.Unboost(npc);                           // nor a guard's raised sight (base defence D3)
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

        /// <summary>SetDead postfix (both framework classes, Patches_Npc).</summary>
        public static void OnMemberDied(EntityAlive npc)
        {
            if (npc == null) return;
            try
            {
                Prospect p = ScRegistry.GetProspect(npc.entityId);
                if (p != null)
                {
                    // their favour quest would otherwise linger in the journal: failed while clearing, completed once
                    // the POI was secured and only "return to <name>" was left
                    if (p.Favour == FavourKind.Poi && LocalPlayer != null) ScQuests.CloseFavour(LocalPlayer, p.Id, p.Done);
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
            if (m.Status == MemberStatus.OnRun)
            {
                if (m.RunDistress) ScRadio.EndDistress(World, player, m);     // the rescue quest fails with them
                m.Snap = null;
            }
            ScRegistry.Members.Remove(m.Id);
            ScRunners.RebuildRunnerSet();
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

        public static void StampCvars(Member m, EntityAlive npc, EntityPlayer owner)
        {
            if (m == null || npc == null || npc.Buffs == null) return;
            npc.Buffs.SetCustomVar("sc_member", 1f);
            if (owner != null) npc.Buffs.SetCustomVar("sc_owner", owner.entityId);
            Patches_VoiceControl.StripContract(npc);   // hired and billed before joining: members are never billed
            if (ScXnpc.HasXnpcLevels(npc)) ScXnpc.StripLevels(npc);   // D8: SC's skills only (Patches_Xnpc X3 keeps them off)
            StampFollowing(m, npc);
            ScSkills.StampCvars(m, npc);
        }

        /// <summary>Mirrors "is with the player right now" so the dialog offers only the order that applies.</summary>
        public static void StampFollowing(Member m, EntityAlive npc)
        {
            if (m == null || npc == null || npc.Buffs == null) return;
            bool with = m.Status == MemberStatus.Following || m.Status == MemberStatus.PendingStation;
            if (with) npc.Buffs.SetCustomVar("sc_following", 1f);
            else if (npc.Buffs.HasCustomVar("sc_following")) npc.Buffs.RemoveCustomVar("sc_following");
            if (ScRunners.IsPhysical(m)) npc.Buffs.SetCustomVar(ScRunners.OnRunCvar, 1f);
            else if (npc.Buffs.HasCustomVar(ScRunners.OnRunCvar)) npc.Buffs.RemoveCustomVar(ScRunners.OnRunCvar);
        }

        public static void ClearCvars(EntityAlive npc)
        {
            if (npc == null || npc.Buffs == null) return;
            npc.Buffs.RemoveCustomVar("sc_member");
            npc.Buffs.RemoveCustomVar("sc_owner");
            npc.Buffs.RemoveCustomVar("sc_following");
            npc.Buffs.RemoveCustomVar(ScRunners.OnRunCvar);
        }

        /// <summary>Adds the resident AI package at runtime (the list is rebuilt from the class on every load).</summary>
        public static void ApplyAiPackage(EntityAlive npc)
        {
            if (npc == null) return;
            // doors: vanilla's move-helper opener, extended in ScDoors (double doors, look-ahead, shut behind them).
            // Every member path runs through here once a second (ScTicker.Relink), so it survives reloads and re-creates.
            ScDoors.Enable(npc, true);
            if (npc.AIPackages == null) return;
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
