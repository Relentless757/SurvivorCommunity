using System;
using System.Collections.Generic;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// The community verbs. Host / single player only: every mutator refuses on a client. Round MP2: a client asks the server
    /// through ScActions (ScActionsMembers: member.follow / home / leave / join); the verbs take any EntityPlayer, the sender.
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
            // an offline physical runner has no entity: whatever holds the id is a stale copy or a stranger; nor has one of a
            // whole enclave still on the way (E12, ScEncJoin: their key is no entity's). Round op1: nor a posting traveler offline,
            // nor a posted member whose body is the snapshot (ScRunners.HeldOffline)
            Member m = ScRegistry.GetMember(id);
            if (m != null && ScRunners.HeldOffline(m)) return null;
            if (m != null && m.Status == MemberStatus.Arriving) return null;
            return ScNpc.As(ScNpc.Loaded(w, id));       // a body still being built is not forced (ScNpc.IsPending)
        }

        // ---- recruiting ----

        /// <summary>
        /// Can this NPC be asked to join? (Host, hireable human framework NPC - SCore's legacy class or 0-XNPCCore's V4
        /// survivors - not hired by somebody else, not a member, roster not full.) enclavePath: E3's recruit of one of an
        /// enclave's people (ScEnclaveDeals.RecruitBlocked), which has its own gates - the only caller that passes the
        /// enclave refusal.
        /// </summary>
        public static bool CanRecruit(EntityAlive npc, EntityPlayer player, out string why, bool enclavePath = false)
        {
            why = null;
            if (npc != null && npc.Buffs != null && npc.Buffs.HasCustomVar(ScRadio.StandInCvar)) { why = "scNotHireable"; return false; }
            // one of an enclave's people (E2): they stay with their group - except through the enclave's own recruit (E3)
            if (!enclavePath && ScEnclavePeople.IsEnclaveNpc(npc)) { why = "scEncStays"; return false; }
            // one of a band on the road (E13a): they come as a band or not at all ("Come to our place.", ScBands)
            if (ScBands.IsBandNpc(npc)) { why = "scEncStays"; return false; }
            // a raider never joins (M10; belt and braces: its class is Hirable false, so the next checks refuse it too)
            if (ScXnpc.IsRaider(npc)) { why = "scNotHireable"; return false; }
            // nor one who holds a grudge against this player (ScTrust: they were hit). Round MP6 (part relations): against a player
            // of the recruiting player's community - another community's hit blocks only that community (the user's decision 5;
            // with one community: any grudge, as before)
            if (ScRelations.GrudgeBlocks(npc, ScPlayers.CommunityOf(player))) { why = "scTrustRefuses"; return false; }
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

        /// <summary>
        /// The recruit is done with their favour: make them a member (no dukes, ever). Round MP2: any player (the member.join
        /// request's sender, ScActionsMembers) - the server's verb.
        /// </summary>
        public static bool Join(EntityAlive npc, EntityPlayer player, out string error)
        {
            error = null;
            try
            {
                if (npc == null || player == null) { error = "scInternal"; return false; }
                if (!ScRegistry.Loaded) { error = "scHostOnly"; return false; }      // round MP2 (E10): never on a client's copy
                string why;
                if (!CanRecruit(npc, player, out why)) { error = why; return false; }
                // round MP2 (review members MINOR 3): a remote player's join never adopts their claim as the home (and them as the
                // owner); the host's own player keeps EnsureHome's AutoHomeFirstClaim adoption
                if (!ScZone.EnsureHomeFor(player)) { error = ScZone.NeedHomeKey(); return false; }
                ScConfig.EnsureParsed();
                return Admit(MemberFromBody(npc, ScRegistry.Community.Morale), npc, player, out error);
            }
            catch (Exception e)
            {
                ScLog.Once("join", e);
                error = "scInternal";
                return false;
            }
        }

        /// <summary>
        /// Join's second half, with the gates passed: this Member record - MemberFromBody's, or (E3) an enclave person's own
        /// (ScEnclaveDeals.Recruit: the same face, skills and traits) - becomes a member on this body: the roster, a favour's
        /// quest closed, a paid hire ended, the cvars, traits and AI package, then stationed at home or following until home.
        /// tell: false - no "joined" tooltip (E12: a whole enclave joining has one tell for all of them, ScEncJoin).
        /// Round MP2: any player. A remote player's favour quest is on their own machine: member.join's reply closes it there
        /// (ScActionsMembers); every other player's copy of that favour quest is removed (ScTalkContext.CloseFavourForOthers).
        /// </summary>
        public static bool Admit(Member m, EntityAlive npc, EntityPlayer player, out string error, bool tell = true)
        {
            error = null;
            if (!ScRegistry.Loaded) { error = "scHostOnly"; return false; }      // round MP2 (E10): never on a client's copy
            try
            {
                // round MP6 (core fix): the member joins the community in scope (the joiner's: Dispatch, the console) - never as a
                // second record under an id another community holds (entity ids are world-unique; the save would write her twice)
                // (review C-M2)
                int holder = ScRegistry.CommunityOfMember(m.Id);
                if (holder >= 0 && holder != ScRegistry.CurrentId)
                {
                    ScLog.Warn(m.Name + " (id " + m.Id + ") is already a member of community " + holder + " - not admitted to " + ScRegistry.Current.Label);
                    error = "scAlreadyMember";
                    return false;
                }
                m.JoinDay = Today;
                ScRegistry.Members[m.Id] = m;
                ScTrust.StripOnJoin(npc);
                // their favour quest ends with the join: one still open is waiting on "return to <name>" (completed),
                // or the favour was skipped (`sc recruit`, removed)
                Prospect pr = ScRegistry.GetProspect(m.Id);
                if (pr != null)
                {
                    // round MP2: the journal is the acting player's own machine's - the host's here, a client's through the reply
                    if (player is EntityPlayerLocal) ScQuests.CloseFavour(player, pr.Id, pr.Done);
                    // round MP2 (the E 4.4 decision): the favour was shared - anyone else who took it on loses their copy
                    ScTalkContext.CloseFavourForOthers(pr.Id, false, player);
                }
                // round MP6 (core fix): every community's favour for them closes with the join (the user's decision 2: whoever finishes
                // first recruits) - GetProspect above is global, the scope's table could miss it (review C-M2)
                ScRegistry.RemoveProspectEverywhere(m.Id);

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
                ClearSynced(npc, "sc_favour");          // round MP2 (E9): clients get the 0
                ClearSynced(npc, "sc_favourDone");
                StampCvars(m, npc, player);
                ScTraits.ApplyBuffs(m, npc);
                ApplyAiPackage(npc);

                bool home = ScZone.Contains(player.GetBlockPosition()) && ScZone.HomeLoaded;
                // round MP2 (review members MINOR 4): detached from the JOINER - a client's paid companion joining at home loses
                // their hired_ mark and Companions entry, not the host's (single player: the joiner is the local player, as before)
                if (home) Station(m, npc, true, player);
                else FollowInternal(m, npc, player, MemberStatus.PendingStation);

                ScRegistry.SaveAtomic();
                ScLog.Out(m.Name + " (" + m.Class + ", id " + m.Id + ") joined the community: traits [" + string.Join(",", m.Traits.ToArray())
                          + "] specialty " + m.Specialty + (home ? " - stationed" : " - following until home"));
                // round MP4 (part: tells): this file's tells go by localization key (ScCommunity.TellKey / ScPlayers.TellCommunityKey), so each player reads them in their own language
                if (tell) TellKey(player, home ? "scJoined" : "scJoinedFollow", "", m.Name);
                return true;
            }
            catch (Exception e)
            {
                ScLog.Once("join", e);
                error = "scInternal";
                return false;
            }
        }

        /// <summary>
        /// The person on this body as a Member record (Join's builder; E2's enclave people are captured with it too): the
        /// class, the class-rolled name, the birth time; the traits, skills and mood the player was shown when they first
        /// talked (ScProfile), else fresh ones (mood: the start mood then); the look, the weapon pair and the rounds and
        /// magazines they carry. Not in the registry: the caller files it.
        /// </summary>
        public static Member MemberFromBody(EntityAlive npc, int mood)
        {
            var m = new Member
            {
                Id = npc.entityId,
                Class = ClassName(npc),
                Name = DisplayName(npc),
                Born = npc.WorldTimeBorn,
                Mood = mood
            };
            GameRandom rnd = npc.rand ?? GameManager.Instance.World.GetGameRandom();
            if (!ScProfile.ApplyOnJoin(m, npc))
            {
                m.Traits.AddRange(ScTraits.Roll(ScConfig.TraitsPerRecruit, rnd));
                ScSkills.RollStart(m, rnd, ScSkills.RecruitTier(npc));
            }
            m.Ranged = ScSkills.HoldsRanged(npc);
            m.Look = ScLooks.Get(npc);          // null unless they wear the player's body
            m.Loadout = ScLoadout.Current(npc); // their two weapons, so every body re-created for them carries the same (null for others)
            m.Ammo = ScAmmo.Carried(npc, out m.Mags);   // and the rounds and magazines they carry for the gun (ScAmmo keeps it current every 5 s)
            return m;
        }

        // ---- stationing ----

        /// <summary>Makes the member a resident at their post (the FarmHere shape). The entity must be loaded. teleport false
        /// (round 28 review L6, a talk hold's end in front of the player): she walks back however far the post is.</summary>
        /// <summary>Is any player this close to the body? Works on a dedicated server, where there is no primary player.</summary>
        private static bool NearAnyPlayer(World w, EntityAlive npc, float metres)
        {
            List<EntityPlayer> list = w != null && w.Players != null ? w.Players.list : null;
            if (list == null) return false;
            float sq = metres * metres;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && list[i].GetDistanceSq(npc) <= sq) return true;
            return false;
        }

        // round MP2 (review members MINOR 4): detachFrom - whose party she leaves when the caller knows it better than her record
        // (Admit: the joiner, whose paid hire ends; a fresh member has no LeaderId). Null: her leader, else the host's own player.
        public static void Station(Member m, EntityAlive npc, bool teleport = true, EntityPlayer detachFrom = null)
        {
            if (m == null || npc == null) return;
            if (!ScRegistry.Loaded) return;           // round MP2 (E10): never on a client's copy (its record or the body's copy)
            try
            {
#if SC_V34
                if (ScDowned.DefersStation(m, npc)) return;   // round U2 (risks 1, 2): never on a downed body - her link, her bleed-out, no teleported ragdoll
#endif
                // round MP2: out of her own leader's party (a client's follower sent home), else the host's own player as before.
                // Round MP2 (review members MINOR 4): the caller's player first (Admit: the joiner)
                EntityPlayer player = detachFrom ?? ScPlayers.ById(m.LeaderId) ?? ScGates.LocalPlayerOf(m.CommunityId);     // round MP6 (part G): her community's
                World w = World;

                Detach(npc, player);
#if SC_V34
                ScWork.Forget(m.Id, npc);          // C1: any stationing path ends a work shift
#endif
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
                // She walked in with a player: let her WALK rather than be moved. The teleport below is for a member
                // stationed from somewhere nobody can see - from the roster, or one held far out - and firing it on
                // someone standing beside the player makes her vanish in front of them. It bites most with an enlarged
                // land claim, where the zone's edge is easily more than 40 m from the post, so simply entering the zone
                // teleported the escort away.
                // ANY player, not the primary one: on a listen server the primary is the host, so a remote player's
                // escort would be measured against the wrong body, and on a dedicated server there is no primary at all
                // and the whole thing would silently never apply.
                bool escorted = ScConfig.StationWalkIn && !npc.IsDead() && NearAnyPlayer(w, npc, ScConfig.StationWalkInMetres);
                if (teleport && !escorted && Vector3.Distance(npc.position, post) > 40f && ScZone.IsLoaded(m.Post))
                {
                    npc.SetPosition(post, true);
                }
                else if (escorted && teleport)
                    // She is left standing where she was. By day ScRoam (3.4) wins inside the zone - ScReturn scores 0
                    // there while ResidentRoamZone is on - so she roams from here rather than marching to the post; at
                    // night Guard and the routine take her to it. That is the intended "settles in" look, not a walk to
                    // a spot: if the post itself ever has to be reached by day, that needs a zone-aware pull, not this.
                    ScLog.Verbose(m.Name + " was escorted in - left to settle in rather than moved to the post " + m.Post);

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
                // round 29 review L5: the watchdog's no-gain count and a move back held while the player watched start again,
                // so a stale held move cannot teleport her in sight later
                ScStuck.ResetGain(m);
                ScLog.Verbose(m.Name + " stationed at " + m.Post);
            }
            catch (Exception e)
            {
                ScLog.Once("station", e);
            }
        }

        /// <summary>
        /// Out of the player's party for good, without SCore ever re-stamping it back - Station's detach, and (E3) the end of
        /// an enclave person's enlistment (ScEnclaveDeals.EndEnlist). player: whose party (null: the body's side only).
        /// </summary>
        public static void Detach(EntityAlive npc, EntityPlayer player)
        {
            // A follower hidden for the leader's vehicle ride (SCore SendOnMission) would stay invisible
            // for ever once the leader link is gone: un-hide before anything else.
            if (npc.Buffs.HasCustomVar("onMission") || !npc.enabled) ScNpc.SendOnMission(npc, false);
#if SC_V34
            // 3.4: 0-XNPCCore seats a follower in the leader's vehicle instead of hiding it (SendOnMission(true) ->
            // AttachToEntity, SdcsVehicleSeatHideV4Patch); a seat would hold them for good once the leader link is gone
            if (npc.AttachedToEntity != null) npc.Detach();
            // C1 review #1: 0-XNPCCore's craft order (in memory) is cleared and refunded before ClearVoiceMovement below takes its
            // mode off - after that nothing of 0-XNPCCore's ever clears it (a shift's end, Go home, any stationing)
            ScWork.DropJob(npc, "detached");
#endif

            // detach from the party without SCore ever re-stamping it back. Round mt1 (part H): through ClearSynced, as the Owner
            // below - a plain removal never reached a client, whose copy kept Leader = the player who last led her: its rows and
            // Voice Control's ring took that player for her leader while the server refused every order. SCore's GetLeader on a
            // client clears a 0 and falls back to Owner; the server sees what it always did (the cvar removed)
            ClearSynced(npc, "Leader");
            // round MP2 (review core/window m5): the Owner link (a work shift's, a talk hold's, a follower's) - clients get a 0
            // (a removal never reaches them), then it is removed here as before, so SCore's leader logic on the server sees exactly
            // what it did. A 0 is what SCore's own Dismiss and FarmHere leave, and every SCore / 0-XNPCCore reader tests > 0
            // (GetOwner, DialogRequirementHiredSDX, SdcsNpcRelation), so a client reads it as no owner
            ClearSynced(npc, "Owner");
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
        /// <summary>
        /// SCResident's flights: their target is the enemy she runs FROM, not a fight (LiveFoe, ActionFoe). Round rt1 (I13): the
        /// set names ScFleeHurt too - before it only the Cowardly ScFlee was named, so a hurt member running read as fighting
        /// and the watchdog gave her three times StuckSeconds - and ScRetreat (an IsSelf action, so its target is herself and
        /// never a foe anyway; named so the rule says it). The enclave twins (ScEncFlee, ScEncFleeHurt) are left as they were:
        /// enclave people are not in this round (decision 12).
        /// </summary>
        private static readonly HashSet<string> FleeActions = new HashSet<string> { "ScFlee", "ScFleeHurt", "ScRetreat" };

        /// <summary>Round rt1: the UAI action is one of SC's flights (FleeActions).</summary>
        private static bool Fleeing(UAI.Context ctx)
        {
            return ctx.ActionData.Action != null && ctx.ActionData.Action.Name != null && FleeActions.Contains(ctx.ActionData.Action.Name);
        }

        /// <summary>
        /// Once a second for a stationed resident: POI furniture, gaps and doorways trap NPC pathing. Stuck =
        /// trying to move (an active move helper or a path) without getting anywhere, the move helper reporting
        /// blocked, or SCore's own in-a-block flag - for StuckSeconds in a row; or far from the post for long.
        /// The cure is a short teleport to the post (or a fresh post when the old one is no longer standable).
        /// A resident with a live enemy within FightRange (LiveFoe) gets three times StuckSeconds (36 s by default): in a
        /// fight it pushes at a target the player or another zombie blocks, backs into walls and waits on the player's
        /// melee, and a playtest showed the 12 s rule teleport one out of a fight (GUARD_DIAG cause 4) - while a real wedge
        /// is still cured. The far rule stays (PostRadius x 3 + 12 m for 90 s, 36 m by default; with ResidentRoamZone, round
        /// hz, it only counts outside the home zone and its margin, where the roam never goes); in a fight it is never
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
        ///
        /// Round 29b (M29, the wreck findings' option c): a body pushed back and forth against a truck wreck's hull moves too
        /// fast to count as still, and the roam's pauses reset the still count, so it pushed, paused and pushed again for
        /// good. Also stuck now: wanting to move with no 1 m gain toward the path's end in 2 x StuckSeconds of trying, across
        /// walks and pauses (ScStuck.SampleGain; StuckNoGain; the fight multipliers and the back-off apply as above). And a
        /// move back waits while the player looks toward the body or the post, at most StuckSeenWaitSeconds (ScStuck.MayTeleport;
        /// a Repost moves nobody and does not wait). onShift (3.4, ScStuck.OnShift, a work shift whose job allows the rescue):
        /// no far rule, no new post, and the move back keeps the job's Guard spot.
        /// </summary>
        public static void Watchdog(Member m, EntityAlive npc, bool onShift = false)
        {
            try
            {
                if (m == null || npc == null || npc.IsDead()) return;
                if (!StuckSample(m, npc)) return;
                Vector3 pos = npc.position;
                bool fighting = LiveFoe(npc, FightRange) != null;
                float now = Time.unscaledTime;
                if (m.StuckShift > 0 && now - m.LastStuckAt > BackoffResetSeconds) m.StuckShift = 0;
                // a live home attack (base defence D2): a crowd at the walls blocks more, so a fight gets five times
                // StuckSeconds, and the whole defended area (+ LeashExtra) counts as near the post
                bool wide = ScBaseAttack.WideDefence;
                int stuckLimit = (fighting ? ScConfig.StuckSeconds * (wide ? 5 : 3) : ScConfig.StuckSeconds) << m.StuckShift;
                // round 29b: the no-gain rule waits twice as long as the still rule
                int gainLimit = stuckLimit * 2;
                float farLimit = ScConfig.PostRadius * 3 + 12;
                if (fighting) farLimit = Mathf.Max(farLimit, ScConfig.DefendBaseRadius + 14f);
                float away = Vector3.Distance(pos, PostCenter(m));
                // zone roaming (round hz): anywhere in the home zone (+ ResidentRoamZoneMargin) is near enough - the far rule
                // only counts outside it, so a resident walking the base is never teleported back to the post
                // round rt1: so is a member the retreat owns (a shelter spot or KITE's back-off point can be far from the post)
                bool homeGround = (ScConfig.ResidentRoamZone || ScRetreat.Owns(npc)) && ScZone.DistanceOutside(pos) <= ScConfig.ResidentRoamZoneMargin;
                if (away > farLimit && !(wide && ScBaseAttack.InDefenceArea(pos, ScBaseAttack.LeashExtra)) && !homeGround && !onShift) m.AwaySeconds++; else m.AwaySeconds = 0;
                int noGain = ScConfig.StuckNoGain ? ScStuck.GainSeconds(m) : 0;
                if (m.StillSeconds >= stuckLimit) Stuck(m, npc, "no progress for " + m.StillSeconds + " s" + (fighting ? ", fighting" : ""), fighting, now, onShift);
                else if (noGain >= gainLimit) Stuck(m, npc, "no 1 m gain toward the path's end in " + noGain + " s of trying" + (fighting ? ", fighting" : ""), fighting, now, onShift);
                else if (m.AwaySeconds >= 90) Stuck(m, npc, (int)away + " m from the post for " + m.AwaySeconds + " s" + (fighting ? ", fighting" : ""), fighting, now, onShift);
                else ScStuck.NoVerdict(m);
            }
            catch (Exception e)
            {
                ScLog.Once("watchdog", e);
            }
        }

        /// <summary>
        /// One second of the watchdog's detection (the residents' Watchdog; E2's enclave people share it): false - no sample -
        /// while the game is paused or the body did not tick since the last one, else the still count (StillSeconds and each
        /// signal's count) goes up while it wants to move and does not, the move helper reports blocked or SCore's in-a-block
        /// flag is set, and starts again otherwise. A frozen world makes no progress and needs none: the pause menu (single
        /// player stops time, the ticker runs on), or a chunk without colliders, a long hitch.
        /// </summary>
        public static bool StuckSample(Member m, EntityAlive npc)
        {
            if (GameManager.Instance != null && GameManager.Instance.IsPaused()) return false;
            int ticks = npc.ticksExisted;
            if (ticks == m.LastTicks) return false;
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
            // round 29b: the seconds it tries to move without getting 1 m closer to where it is going (ScStuck; Watchdog's no-gain rule)
            ScStuck.SampleGain(m, npc, pos, mh, nav, mhFar || navBusy);
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
            return true;
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

        internal static void ResetStill(Member m)
        {
            m.StillSeconds = 0;
            m.SigPlan = m.SigPath = m.SigMover = m.SigBlocked = m.SigIsStuck = 0;
        }

        /// <summary>
        /// A stuck verdict (Watchdog). Not fighting: stuck AT the post gets a new post and no teleport (Repost; with none to
        /// give, the back-off), and so does the RepeatCount-th move back within RepeatWindowSeconds (with none to give, it is
        /// moved back as before, the limit backed off). Everything else - a fight included - is the old cure, Unstick.
        /// </summary>
        private static void Stuck(Member m, EntityAlive npc, string why, bool fighting, float now, bool onShift = false)
        {
            // round 29b: a move back already held while the player watches only waits for them to look away (no second
            // Repost, no back-off each second meanwhile)
            if (ScStuck.Held(m))
            {
                if (!ScStuck.MayTeleport(m, npc, PostCenter(m), why)) return;
                m.LastStuckAt = now;
                m.UnstickTimes.Add(now);
                Unstick(m, npc, why, fighting, StuckDiag(m, npc), onShift);
                return;
            }
            m.LastStuckAt = now;
            string diag = StuckDiag(m, npc);
            if (onShift)
            {
                // a work shift (3.4): the post is not where she is meant to be, so no new post - a rescue that keeps coming
                // back backs off as a repost with no spot to give does
                for (int i = m.UnstickTimes.Count - 1; i >= 0; i--) if (now - m.UnstickTimes[i] > RepeatWindowSeconds) m.UnstickTimes.RemoveAt(i);
                if (m.UnstickTimes.Count >= RepeatCount - 1 && m.StuckShift < MaxStuckShift) m.StuckShift++;
            }
            else if (!fighting)
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
            // round 29b: the teleport only out of the player's sight, or after StuckSeenWaitSeconds
            if (!ScStuck.MayTeleport(m, npc, PostCenter(m), why)) return;
            m.UnstickTimes.Add(now);
            Unstick(m, npc, why, fighting, diag, onShift);
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
        /// before anything sets the attack target (MoveToAttackTargetSDX sets it only when it stops). Not a flight's target
        /// (the review's M6; round rt1: every action in FleeActions, ScFleeHurt included): a member running from an enemy is
        /// not fighting it, and one that flees into a corner is cured in StuckSeconds like any wedge. Main thread.
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
            if (ctx == null || Fleeing(ctx)) return null;
            t = ctx.ActionData.Target as EntityAlive;
            return IsLiveFoe(npc, t, r2, true) ? t : null;
        }

        private static bool IsLiveFoe(EntityAlive npc, EntityAlive t, float r2, bool mustBeEnemy)
        {
            return t != null && t != npc && !t.IsDead() && (t.position - npc.position).sqrMagnitude <= r2
                   && (!mustBeEnemy || EntityTargetingUtilities.IsEnemy(npc, t));
        }

#if SC_V34
        /// <summary>
        /// Round 33a (ScJobs.KitFightOne): the live enemy her current UAI action targets, at any range - LiveFoe's third test
        /// without its range. Null for none, a target that is no enemy, or the Cowardly flee's (the enemy she runs from).
        /// </summary>
        public static EntityAlive ActionFoe(EntityAlive npc)
        {
            UAI.Context ctx = npc != null ? npc.utilityAIContext : null;
            if (ctx == null || Fleeing(ctx)) return null;
            EntityAlive t = ctx.ActionData.Target as EntityAlive;
            return IsLiveFoe(npc, t, float.MaxValue, true) ? t : null;
        }
#endif

        /// <summary>
        /// Moves a stuck resident back to its post. "fighting" (a live enemy within FightRange when the watchdog fired, the
        /// review's M3): the base defence then pauses DefendRestSeconds, so an intruder it could not reach does not pull it
        /// straight back; a wedge with no enemy about leaves the defence alone. A post that is no real post any more (built
        /// over, its floor gone, or never one) is replaced first when a better spot exists (the M11 fix 2: GoodPost, not the
        /// old "two air cells over anything"); any path request still pending goes with the path (DropPath), so it cannot
        /// survive the teleport. diag: the watchdog's details (StuckDiag), appended to the log line. onShift (round 29b, 3.4: a
        /// work shift whose job allows the rescue): the body goes to the post, but the Guard spot stays the job's.
        /// </summary>
        public static void Unstick(Member m, EntityAlive npc, string why, bool fighting, string diag = null, bool onShift = false)
        {
            World w = World;
            if (w == null) return;
            ResetStill(m);
            ScStuck.ResetGain(m);
            m.AwaySeconds = 0;
            string bad;
            if (ScZone.IsLoaded(m.Post) && !GoodPost(w, m.Post, false, out bad))
            {
                m.BadPosts.Add(m.Post);
                int tier;
                Vector3i fresh = ChoosePost(w.GetGameRandom(), m.BadPosts, out tier);
                // a real floor; or anything at all when the post has no room to stand any more (built over, or inside a
                // wreck's hull - round 29b): never a teleport into a block
                if (fresh != Vector3i.zero && (tier <= TierRelaxed || RoomGone(w, m.Post)))
                {
                    ScLog.Out(m.Name + "'s post " + m.Post + " is " + bad + " - new post " + fresh + " (" + TierName(tier) + ")");
                    m.Post = fresh;
                }
            }
            Vector3 target = PostCenter(m);
            DropPath(npc);
            npc.SetPosition(target, true);
            m.LastPos = target;
            if (!onShift) ApplyPost(npc, target);
            UAI.ScRoamState.NextAllowed[npc.entityId] = UnityEngine.Time.time + 30f;
            // an intruder it could not reach would pull it straight back to the same spot: the defence pauses a while
            // (what it sees it still fights)
            if (fighting) UAI.ScDefendState.Rest(npc.entityId, DefendRestSeconds);
            ScLog.Out(m.Name + " was stuck (" + why + ") - moved back to the post " + m.Post + (onShift ? " (on a work shift: the job's spot kept)" : "")
                      + (diag != null ? " [" + diag + "]" : ""));
        }

        /// <summary>
        /// No room to stand at this post any more: built over (Standable fails), or (round 29b) the cell is inside an oversized
        /// block's hull - a truck wreck's other cells read as air. Then any spot at all beats it.
        /// </summary>
        private static bool RoomGone(World w, Vector3i p)
        {
            return !Standable(w, p.x, p.y, p.z) || ScStuck.InsideOversized(p);
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
            ScStuck.ResetGain(m);
            m.AwaySeconds = 0;
            string details = diag != null ? " [" + diag + "]" : "";
            // only a real floor - or anything at all when the old post has no room to stand any more (built over, inside a wreck)
            bool oldGone = old != Vector3i.zero && ScZone.IsLoaded(old) && RoomGone(w, old);
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
              .Append(", moveHelperFar ").Append(m.SigMover).Append(", blockedTime ").Append(m.SigBlocked).Append(", isStuck ").Append(m.SigIsStuck)
              .Append("; ").Append(ScStuck.GainLine(m)).Append("; ");
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
              .Append(", blocked ").Append(m.SigBlocked).Append(", isStuck ").Append(m.SigIsStuck).Append("), ").Append(ScStuck.GainLine(m))
              .Append(", away ").Append(m.AwaySeconds).Append(" s, ")
              .Append(m.UnstickTimes.Count).Append(" move(s) back lately, limit x").Append(1 << m.StuckShift).Append(", checked on load=").Append(m.PostChecked)
              .Append(", not again: ").Append(m.BadPosts.Count > 0 ? string.Join(" ", ToStrings(m.BadPosts)) : "-")
              .Append("; roam spots out of reach: ").Append(ScStuck.UnreachableLine(npc.entityId));
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
                case TierStandable: return "only standable: on furniture or a part block, or beside a wreck";
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
        /// Round 29b (M29, the wreck findings' option b), both: no oversized block - a truck wreck, a big vehicle prop - in the
        /// cell or its 8 neighbors at feet or head height (ScStuck.NearOversized): such a block exists in the block data only
        /// at its origin cell, so the rest of its hull reads as air and passed every test above. why: "in or beside
        /// &lt;block&gt; at &lt;its origin&gt;". This covers roam spots, posts, the post check on load, Unstick and enclave homes.
        /// </summary>
        public static bool GoodPost(World w, Vector3i p, bool strict, out string why)
        {
            return GoodPost(w, p, strict, true, out why);
        }

        /// <summary>GoodPost; wrecks false: the oversized blocks are left to the caller (FindPost asks first).</summary>
        private static bool GoodPost(World w, Vector3i p, bool strict, bool wrecks, out string why)
        {
            why = null;
            if (w == null) { why = "no world"; return false; }
            if (!Standable(w, p.x, p.y, p.z)) { why = "no room to stand (two air cells over a floor)"; return false; }
            BlockValue floor = w.GetBlock(new Vector3i(p.x, p.y - 1, p.z));
            if (!SolidFloor(floor)) { why = "on " + BlockName(floor); return false; }
            string wreck;
            if (wrecks && ScStuck.NearOversized(p, out wreck)) { why = "in or beside " + wreck; return false; }
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
        internal static bool FindPost(World w, int x, int z, int yRef, ICollection<Vector3i> exclude, out Vector3i post, out int tier)
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
                    // round 29b: never a cell inside an oversized block's hull (it reads as air); one beside it only as the
                    // last resort, as a spot on furniture is
                    string wreck;
                    bool inWreck;
                    bool nearWreck = ScStuck.NearOversized(p, out wreck, out inWreck);
                    if (inWreck) continue;
                    string why;
                    int t = nearWreck ? TierStandable
                          : GoodPost(w, p, true, false, out why) ? TierStrict : SolidFloor(w.GetBlock(new Vector3i(x, y - 1, z))) ? TierRelaxed : TierStandable;
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

        /// <summary>Round MP2: any player (member.follow's sender) - the leader is the player who asked.</summary>
        public static bool Follow(Member m, EntityAlive npc, EntityPlayer player, out string error)
        {
            error = null;
            if (!IsHost) { error = "scHostOnly"; return false; }
            if (m == null || npc == null || player == null) { error = "scNotLoaded"; return false; }
            if (m.Status == MemberStatus.Following) return true;
            if (m.Status == MemberStatus.OnRun) { error = "scOnRun"; return false; }
            if (m.Status == MemberStatus.Garrison) { error = "scOpPostedAway"; return false; }
            if (m.Status == MemberStatus.Transit) { error = "scOpOnTheWay"; return false; }     // round op1: on the road to or from an outpost
            if (ScWatch.OnWatch(m)) { error = "scWatchBusy"; return false; }     // relieve them at the Watch post first (D3)
#if SC_V34
            if (ScDowned.RefuseVerb(m)) { error = "scDownedBusy"; return false; }   // round U2: not while she is down (Follow ends a shift's link)
#endif
            // E3: an enclave's people enlisted with the player count toward the limit too. Round MP7 (part F): the asking player's own
            // followers and enlisted people (per player in every mode; single player: the community's, as before)
            if (ScFollowers.Full(player)) { error = "scFollowersFull"; return false; }
#if SC_V34
            // round U6 (D12): 0-XNPCCore's party cap, shared with paid hires; MaxFollowers above stays SC's own lower cap (ScPartyCap)
            if (ScPartyCap.Refuses(m, npc, player, out error)) return false;     // round MP2: the player she would follow, remote ones included
#endif
            if (ScConfig.RefusesForMood(m)) { error = "scRefuseMood"; return false; }   // P2: never by default
#if SC_V34
            ScWork.EndFor(m, "asked to follow");   // C1: off the work shift first (her job dropped, unmade items refunded)
#endif
            FollowInternal(m, npc, player, MemberStatus.Following);
            ScRegistry.SaveAtomic();
            return true;
        }

        private static void FollowInternal(Member m, EntityAlive npc, EntityPlayer player, MemberStatus status)
        {
            ScNpc.SetLeaderAndOwner(npc.entityId, player.entityId);   // Follow order, hired_*, cache, StaticSpawner, Owner
            // round mt1 (review fix H-m2): SCore writes Leader and Owner plainly (the slow bulk buff sync), while a talk hold's or
            // shift's end just before (EndFor -> Detach) force-sent Owner = 0: a client saw the 0 for seconds, and its hire-mark
            // sweep took the follow's fresh mark off. Both are force-sent now, so the clients read them in the same frame as the 0.
            // (SetSynced would skip them: the value is already set.) Single player: sent to nobody
            if (npc.Buffs.HasCustomVar("Leader")) npc.Buffs.SetCustomVar("Leader", npc.Buffs.GetCustomVar("Leader"), true, CVarOperation.set, true);
            if (npc.Buffs.HasCustomVar("Owner")) npc.Buffs.SetCustomVar("Owner", npc.Buffs.GetCustomVar("Owner"), true, CVarOperation.set, true);
            npc.Buffs.SetCustomVar("Persist", 1f);
            // round MP2: her leader's platform id from the start (the ticker's NoteLeader would write the same a second later), so
            // a client's follower whose link drops before that is never handed to whoever hosts (ScTicker.LeaderToRestore)
            string leaderId = ScPlayers.IdOf(player);
            if (leaderId.Length > 0) m.LeaderId = leaderId;
            m.Status = status;
            ScStuck.ResetGain(m);              // round 29 review L5: no held move back outlives the status
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

        /// <summary>Round MP2: any player (member.home's sender): "inside the base" is that player's position.</summary>
        public static bool ReturnHome(Member m, EntityAlive npc, EntityPlayer player, out string error)
        {
            error = null;
            if (!IsHost) { error = "scHostOnly"; return false; }
            if (m == null || npc == null) { error = "scNotLoaded"; return false; }
            if (m.Status == MemberStatus.Resident) return true;
#if SC_V34
            if (ScDowned.RefuseVerb(m)) { error = "scDownedBusy"; return false; }   // round U2 (risk 2): a downed follower is not sent home
#endif
            if (m.Status == MemberStatus.OnRun) { error = "scOnRun"; return false; }
            if (m.Status == MemberStatus.Garrison) { ScGarrison.Recall(m, player, "scOpRecalled"); ScRegistry.SaveAtomic(); return true; }
            // round op1: one on the way to an outpost turns round (already walking home: they are coming)
            if (m.Status == MemberStatus.Transit) return ScGarrison.TurnHome(m, player, "scOpTurnedBack", out error);
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
            // round MP1: only the real registry - a client's copy (ScMirror) is never changed. Round MP2 (E10): a client asks with
            // member.leave (ScActionsMembers); this refusal stays as the verb's own defense
            if (!ScRegistry.Loaded || !IsHost) return;
            // round MP6 (core fix): in her own community - its roster, its tells - whatever the caller's scope (review C-M2: callers
            // find her through the global GetMember)
            using ScScope inScope = ScRegistry.Use(m);
#if SC_V34
            // round U2: a downed member does not leave - Leave detaches her, 0-XNPCCore would find no leader and her bleed-out would
            // stop for good (an invulnerable stranger); asked again once she is up
            if (ScDowned.RefuseLeave(m, reasonKey)) return;
#endif
            try
            {
                ScGarrison.RemoveStandIn(m);          // a posted member leaving takes their stand-in with them
#if SC_V34
                ScWork.EndFor(m, "leaving");          // C1: off the work shift first
#endif
                if (m.Status == MemberStatus.OnRun && m.RunDistress) ScRadio.EndDistress(World, ScGates.LocalPlayerOf(m.CommunityId), m);   // no orphaned rescue quest (round MP6 (part G): her community's)
                // round op1: the body looked up before the snapshot goes - a posting traveler's snapshot is what says they have none
                EntityAlive npc = FindNpc(m.Id);
                m.Snap = null;
                // round MP0 (the review, S3): the hire mark comes off the member's own leader (else the host's own player, as before)
                EntityPlayer player = ScPlayers.ById(m.LeaderId) ?? ScGates.LocalPlayerOf(m.CommunityId);     // round MP6 (part G): her community's
                if (npc != null)
                {
                    ScProfile.Store(m, npc);          // the snapshot shows who they are now (and a re-join brings it)
                    CleanupStale(npc);
                }
                else if (m.Status != MemberStatus.Arriving) ScRegistry.Cleanup.Add(m.Id);    // E12: one still on the way never had a body under their key
                if (player != null)
                {
                    player.Buffs.RemoveCustomVar("hired_" + m.Id);
                    EntityUtilities.CheckForDanglingHires(player.entityId);
                }
                // round MP0 (the review, S3): a departure is the whole community's news (single player: the one player, as before).
                // round db1: not for a disband's (ScMembership.DisbandLeaveKey) - every member leaves at once and Disband tells it once
                if (reasonKey != ScMembership.DisbandLeaveKey) ScPlayers.TellCommunityKey(reasonKey ?? "scLeft", "ui_denied", m.Name);
#if SC_V34
                ScHanded.OnRecordGone(m, npc, "left the community");   // round n12 review: her handed items into her pack (or logged lost)
#endif
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

        /// <summary>
        /// Does this entity carry the mod's marks at all? (The cleanup queue must never strip an innocent NPC that inherited a purged
        /// id.) Round MP2: a mark of 0 is none (E9: a client's copy keeps the 0 a clear sent it; the server never holds a 0).
        /// </summary>
        public static bool CarriesOurMarks(EntityAlive npc)
        {
            return npc != null && npc.Buffs != null && (npc.Buffs.GetCustomVar("sc_member") > 0f || npc.Buffs.GetCustomVar("sc_favour") > 0f);
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
                ClearSynced(npc, "sc_favour");             // round MP2 (E9): clients get the 0
                ClearSynced(npc, "sc_favourDone");
                if (npc.AIPackages != null) npc.AIPackages.Remove(ResidentPackage);
                ScDoors.Enable(npc, false);                // a former member no longer opens the player's doors
                npc.Buffs.RemoveCustomVar("Persist");
                if (npc.Buffs.HasCustomVar("onMission")) ScNpc.SendOnMission(npc, false);
                ScOrders.ClearVoiceMovement(npc);          // a former member must not stay crouched or hold a formation slot
                npc.Buffs.AddBuff(OrderDismissBuff);       // zeroes Leader/Owner, wander
                if (ScNpc.IsV4(npc)) ScNpc.RestoreSpeed(npc);   // a follower's leader speed (SetLeader copies it) must not outlive the membership
                ScWatch.Unboost(npc);                           // nor a guard's raised sight (base defence D3)
#if SC_V34
                ScCraft.OnLeft(npc);                            // C1: 0-XNPCCore's own Progression next time she is hired; no role mark, no shift
#endif
                // round mt1 (part H): clients get the 0 (a removal never reaches them), then removed here as before
                ClearSynced(npc, "Leader");
                ClearSynced(npc, "Owner");
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
                // round MP6 (part relations): killed by a player of another community - her community's grudge (the user's decision 4;
                // the hit hook never sees a killing blow). Nothing with one community
                if (ScRelations.Several) ScRelations.OnMemberHit(npc, npc.entityThatKilledMe as EntityPlayer);
                Prospect p = ScRegistry.GetProspect(npc.entityId);
                if (p != null)
                {
                    // their favour quest would otherwise linger in the journal: failed while clearing or gathering,
                    // completed once the POI was secured and only "return to <name>" was left
                    if (LocalPlayer != null) ScQuests.CloseFavour(LocalPlayer, p.Id, p.Done);
                    // round MP2: the same for every client that took it on (the favour is shared: the E 4.4 decision)
                    ScTalkContext.CloseFavourForOthers(p.Id, p.Done, LocalPlayer);
                    // round MP6 (core fix): every community's favour for them ends with them - GetProspect above is global, the
                    // scope's table could miss it (review C-M2)
                    ScRegistry.RemoveProspectEverywhere(npc.entityId);
                    ScRegistry.SaveAtomic();
                }
                Member m = ScRegistry.GetMember(npc.entityId);
                if (m == null) return;
                // round MP6 (core fix): the SetDead postfix has no scope - her own community's anchor, roster and news (review C-M2)
                using (ScRegistry.Use(m))
                {
                    // round MP0: the anchor, not the local player - a dedicated server has none, and the news still goes out
#if SC_V34
                    RecordDeath(m, ScPlayers.Anchor(), ScDowned.DeathKey(m));   // round U2 (D6): "bled out" for one who was down
#else
                    RecordDeath(m, ScPlayers.Anchor(), "scDied");
#endif
                }
            }
            catch (Exception e)
            {
                ScLog.Once("died", e);
            }
        }

        /// <summary>Registry side of a death (SetDead or a supply run): roster, deaths list, morale penalty, tooltip.</summary>
        public static void RecordDeath(Member m, EntityPlayer player, string tooltipKey)     // round MP0: any player
        {
            if (m == null || !ScRegistry.Loaded) return;      // round MP2 (E10): never on a client's copy
            // round MP6 (core fix): her own community's roster, deaths, death penalty, morale and news, whatever the caller's scope
            // (review C-M2)
            using ScScope inScope = ScRegistry.Use(m);
            if (m.Status == MemberStatus.Garrison) ScGarrison.RemoveStandIn(m);
            if (m.Status == MemberStatus.OnRun)
            {
                if (m.RunDistress) ScRadio.EndDistress(World, player, m);     // the rescue quest fails with them
                m.Snap = null;
            }
#if SC_V34
            // round n12 review: a death in the world dropped her handed items already (Patches_Npc); one off-world loses them - logged
            ScHanded.OnRecordGone(m, null, "died away from her body");
#endif
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
                // round MP0 (the review, N3): the hire mark comes off the member's own leader when known
                (ScPlayers.ById(m.LeaderId) ?? player).Buffs.RemoveCustomVar("hired_" + m.Id);
                // round MP0: a member's death is the whole community's news (a caller passing no player keeps its own news)
                ScPlayers.TellCommunityKey(tooltipKey ?? "scDied", "ui_denied", m.Name);
            }
            ScLog.Out(m.Name + " died - community morale now " + c.Morale);
            ScRegistry.SaveAtomic();
        }

        // ---- mirrors ----

        public static void StampCvars(Member m, EntityAlive npc, EntityPlayer owner)
        {
            if (m == null || npc == null || npc.Buffs == null) return;
            // round MP2 (E9): the talk's member rows read it on every client. Round MP6 (part relations): her community's id + 1
            // (community 0's members: 1, as before) - the faction postfix and the gates find her community in one read.
            // round MP6 (merge): Config/dialogs.xml's member rows read ScOwnMember (part G), which reads this id + 1
            SetSynced(npc, "sc_member", m.CommunityId + 1f);
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
            // round MP2 (E9): the talk's rows (Follow me / Go home / Run / Head home) read these on every client - force-sent on a
            // change, a 0 instead of a removal (SetSynced and ClearSynced send nothing while the value stays: this runs every second)
            if (with) SetSynced(npc, "sc_following", 1f);
            else ClearSynced(npc, "sc_following");
            if (ScRunners.IsPhysical(m)) SetSynced(npc, ScRunners.OnRunCvar, 1f);
            else ClearSynced(npc, ScRunners.OnRunCvar);
        }

        public static void ClearCvars(EntityAlive npc)
        {
            if (npc == null || npc.Buffs == null) return;
            ClearSynced(npc, "sc_member");              // round MP2 (E9): clients get the 0
            npc.Buffs.RemoveCustomVar("sc_owner");
            ClearSynced(npc, "sc_following");
            ClearSynced(npc, ScRunners.OnRunCvar);
        }

        /// <summary>
        /// Round MP2 (E9, the brief's rule 5): a gating cvar the talk's rows read on every client (sc_member, sc_following,
        /// sc_onrun, sc_favour, sc_favourDone). Force-sent, and only when it changes - a plain server write reaches a client
        /// only through the slow bulk sync, if at all. Server only (a write on a client's copy would go to the server).
        /// </summary>
        public static void SetSynced(EntityAlive npc, string name, float value)
        {
            if (npc == null || npc.Buffs == null || string.IsNullOrEmpty(name)) return;
            if (npc.Buffs.HasCustomVar(name) && npc.Buffs.GetCustomVar(name) == value) return;     // unchanged: nothing to send
            npc.Buffs.SetCustomVar(name, value, true, CVarOperation.set, true);
        }

        /// <summary>
        /// Round MP2 (E9): clears a gating cvar. Clients are sent a 0 (a removal never reaches them: EntityBuffs.RemoveCustomVar is
        /// local, and the bulk sync carries no removals), and the cvar is then removed HERE, as before - so every reader on the
        /// server and the save see exactly what they always did (a 0 is never saved either). Readers that run on a client test
        /// the value (&gt; 0), as SCore's NPCHasCVarSDX operator="not" and value="1" already do. Nothing sent when there is none.
        /// </summary>
        public static void ClearSynced(EntityAlive npc, string name)
        {
            if (npc == null || npc.Buffs == null || string.IsNullOrEmpty(name) || !npc.Buffs.HasCustomVar(name)) return;
            npc.Buffs.SetCustomVar(name, 0f, true, CVarOperation.set, true);
            npc.Buffs.RemoveCustomVar(name);
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
            ScBackOff.CheckWideAction(UAI.UAIBase.AIPackages[ResidentPackage]);     // the Fable review of k5 (finding 4): its guarded ScKiteWide
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

        /// <summary>
        /// Round MP4 (part: tells): a tell by its localization key, so each player reads it in their own language. args are the
        /// row's arguments exactly as the old string.Format(Localization.Get(key), args) took them (numbers as numbers); one that
        /// is itself a localization key (a resource name, a status word) goes as LocArg(key).
        /// - The host's own player (single player too): formatted and shown here - Tell(player, TellText(key, args), sound), the
        ///   same text the old call site showed, byte for byte.
        /// - A remote player whose SC speaks this protocol (ScNet.ClientMatches): a NetPackageScTell, formatted on that machine.
        /// - Anyone else (a vanilla client, another SC protocol, more than NetPackageScTell.MaxArgs arguments, no client info):
        ///   this machine's words through Tell, as before.
        /// </summary>
        public static void TellKey(EntityPlayer player, string key, string sound, params object[] args)
        {
            if (player == null || string.IsNullOrEmpty(key)) return;
            try
            {
                if (!(player is EntityPlayerLocal) && ScNet.ClientMatches(player) && NetPackageScTell.Fits(args)
                    && ScNet.ToPlayer(player, () => NetPackageManager.GetPackage<NetPackageScTell>().Setup(key, sound, args), null))
                    return;
                string text;
                try { text = TellText(key, args); }
                catch (FormatException fe)
                {
                    // review tells n1: a row whose tokens do not fit shows as it is (as on a client), logged once per key
                    ScLog.Once("tell-key-format-" + key, fe);
                    text = Localization.Get(key);
                }
                Tell(player, text, sound);
            }
            catch (Exception e)
            {
                ScLog.Once("tell-key", e);
            }
        }

        /// <summary>Round MP4 (part: tells): a TellKey argument that is itself a localization key, localized where the tell shows.</summary>
        public static NetPackageScTell.LocKey LocArg(string key)
        {
            return new NetPackageScTell.LocKey(key);
        }

        /// <summary>
        /// Round MP4 (part: tells): a tell's words in this machine's language - the old call sites' own
        /// string.Format(Localization.Get(key), args), each LocArg localized here first; with no arguments the row as it is (as
        /// Tell(player, Localization.Get(key), sound) showed it: no Format, so a row's "{{" stays as written).
        /// </summary>
        internal static string TellText(string key, object[] args)
        {
            string row = Localization.Get(key);
            if (args == null || args.Length == 0) return row;
            object[] a = new object[args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                NetPackageScTell.LocKey k = args[i] as NetPackageScTell.LocKey;
                a[i] = k != null ? (object)Localization.Get(k.Key) : args[i];
            }
            return string.Format(row, a);
        }

        /// <summary>
        /// Round MP4 (part: tells), client: a NetPackageScTell from the server, shown as Tell shows one, in this machine's language. A
        /// row this language formats badly (its tokens differ) shows as it is rather than not at all.
        /// </summary>
        internal static void ShowTellKey(EntityPlayerLocal player, string key, string sound, object[] args)
        {
            if (player == null || string.IsNullOrEmpty(key)) return;
            string text;
            try { text = TellText(key, args); }
            catch (FormatException e)
            {
                ScLog.Once("tell-key-format-" + key, e);
                text = Localization.Get(key);
            }
            Tell(player, text, sound);
        }

        /// <summary>The community window on its Outposts tab, with one outpost selected (id &gt; 0) or none.</summary>
        public static void OpenOutposts(EntityPlayerLocal player, int outpostId)
        {
            XUiC_ScCommunity.PendingOutpost = outpostId > 0 ? outpostId : -1;
            OpenWindow(player);
        }

        /// <summary>
        /// Opens the community window for the local player (window group registered by Config/XUi_InGame) - every opener's
        /// way in: the ESC menu entry, the land claim radial, a member's dialog, a Watch post's "Assign guard", OpenOutposts.
        /// W1: with the player menu's Community tab there it goes through the tab bar as vanilla's pages do
        /// (XUiC_WindowSelector.OpenSelectorAndWindow: other modal windows close - the ESC menu, an NPC dialog -, the tab is
        /// selected, the bar opens and opens the page; XUiC_ScPageGroup keeps the bar with it). That call would shut the page
        /// instead when it already shows, and does nothing for a dead player: an open window is left as it is, and a dead
        /// player gets nothing either way (as vanilla's tabs). Without the tab (XUiC_ScPageGroup.Tab) it opens on its own.
        /// </summary>
        public static void OpenWindow(EntityPlayerLocal player)
        {
            try
            {
                if (player == null || player.PlayerUI == null || player.PlayerUI.windowManager == null) return;
                GUIWindowManager wm = player.PlayerUI.windowManager;
                GUIWindow win;
                if (!wm.TryGetWindow(WindowGroup, out win))
                {
                    TellKey(player, "scNoWindow", "ui_denied");
                    return;
                }
                if (player.IsDead() || wm.IsWindowOpen(WindowGroup))
                {
                    // not opened now (its OnOpen has run or won't): a Watch pick or an outpost asked for here must not
                    // carry over to the next opening
                    XUiC_ScCommunity.PendingOutpost = 0;
                    XUiC_ScCommunity.PendingWatch = Vector3i.zero;
                    return;
                }
                if (XUiC_ScPageGroup.Tab(XUiC_ScPageGroup.Selector(player.PlayerUI.xui)) != null)
                    XUiC_WindowSelector.OpenSelectorAndWindow(player, WindowGroup);
                else
                    wm.Open(WindowGroup, true);
                if (!wm.IsWindowOpen(WindowGroup))
                {
                    // W1 review LOW-3: nothing opened after all (the HUD fully off) - the asks must not wait for the next opening
                    XUiC_ScCommunity.PendingOutpost = 0;
                    XUiC_ScCommunity.PendingWatch = Vector3i.zero;
                }
            }
            catch (Exception e)
            {
                ScLog.Once("open-window", e);
            }
        }
    }
}
