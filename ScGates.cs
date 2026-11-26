using System;
using SurvivorCommunity;
using UnityEngine;

// round MP6 (part G), 2026-11-26: the entry points' scopes and the cross-community gates (MP6_DESIGN.md, the user's decisions 3
// and 4: another community's members are talk-only; another community's stations, storage, home and outposts are not yours).
//
// - Who a body belongs to: MemberCommunityOf - on the server her record's community (ScRegistry.GetMember answers across every
//   community), else the synced sc_member cvar, which holds community id + 1 on a member (1 = community 0, the only value before
//   MP6). A client has no other community's records, so it always reads the cvar; its own community is the mirrored one
//   (ScPlayers.CommunityOf of its own player = ScRegistry.MirroredCommunity).
// - Which community a position belongs to: CommunityAt - a community's home zone (ScZone.Contains inside that community's scope),
//   else a held outpost's POI footprint. Main thread only, as the registry.
// - Scopes for the engine's entry points (Harmony, UAI, blocks, tile entities, events): ScopeOf(npc) opens her community's scope
//   when she is a member and nothing for a stranger (strangers read no community); ScopeAt(pos) the position's community.
// With one community (everyone in community 0) every test here answers as before MP6: no body or position is anyone else's.

namespace SurvivorCommunity
{
    /// <summary>Round MP6 (part G): which community a body or a position belongs to, the entry points' scopes and the gates.</summary>
    public static class ScGates
    {
        /// <summary>The member flag on a member's body: community id + 1 (round MP6, part relations; 1 = community 0 before).</summary>
        public const string MemberCvar = "sc_member";

        // ------------------------------------------------------------------ bodies ----

        /// <summary>
        /// The community whose member this body is, or -1 (none): the server's record when it has one (GetMember: any community),
        /// else the sc_member cvar (id + 1; a client's only source). Any machine.
        /// </summary>
        public static int MemberCommunityOf(EntityAlive npc)
        {
            if (npc == null) return -1;
            if (ScRegistry.Loaded)
            {
                Member m = MemberOf(npc);
                if (m != null) return m.CommunityId;
            }
            return CvarCommunity(npc);
        }

        /// <summary>
        /// The member this body is (any community): her record by its entity id, or - an outpost or rescue stand-in, which carries
        /// her member id in sc_standin - the record it stands in for. Null for anyone else.
        /// </summary>
        public static Member MemberOf(EntityAlive npc)
        {
            if (npc == null) return null;
            Member m = ScRegistry.GetMember(npc.entityId);
            if (m == null && npc.Buffs != null && npc.Buffs.HasCustomVar(ScRadio.StandInCvar)) m = ScRegistry.GetMember((int)npc.Buffs.GetCustomVar(ScRadio.StandInCvar));
            return m;
        }

        /// <summary>The community the body's sc_member cvar names (its value - 1), or -1 with none (absent or 0: cleared).</summary>
        public static int CvarCommunity(EntityAlive npc)
        {
            if (npc == null || npc.Buffs == null) return -1;
            float v = npc.Buffs.GetCustomVar(MemberCvar);
            return v > 0f ? Mathf.RoundToInt(v) - 1 : -1;
        }

        /// <summary>
        /// The dialog's "a member of the talker's community": the sc_member flag set (as the old value="1" rows read it) and her
        /// community the player's. With one community: exactly the old flag test. Any machine (a client: the cvar and its mirrored
        /// community).
        /// </summary>
        public static bool IsOwnMember(EntityAlive npc, EntityPlayer player)
        {
            if (npc == null || npc.Buffs == null || npc.Buffs.GetCustomVar(MemberCvar) <= 0f) return false;
            int mc = MemberCommunityOf(npc);
            return mc >= 0 && mc == ScPlayers.CommunityOf(player);
        }

        /// <summary>A member of another community than the player's (talk only: no orders, hold, Equipment or panel). Never with one community.</summary>
        public static bool IsForeignMember(EntityAlive npc, EntityPlayer player)
        {
            int mc = MemberCommunityOf(npc);
            return mc >= 0 && mc != ScPlayers.CommunityOf(player);
        }

        /// <summary>
        /// Server: the member with this id when she belongs to the community in scope (ScActions.Dispatch: the sender's), else
        /// null - an action naming another community's member acts on nobody. With one community: GetMember.
        /// </summary>
        public static Member OwnMember(int memberId)
        {
            Member m = ScRegistry.GetMember(memberId);
            return m != null && m.CommunityId == ScRegistry.CurrentId ? m : null;
        }

        /// <summary>Her community's scope when the body is a member's or her stand-in's (MemberOf), nothing for a stranger.</summary>
        public static ScScope ScopeOf(EntityAlive npc)
        {
            if (npc == null || ScCommunities.Count <= 1) return default(ScScope);      // one community: it is always the one in scope
            Member m = MemberOf(npc);
            return m != null ? ScRegistry.Use(m) : default(ScScope);
        }

        /// <summary>The member's community's scope by her entity id; nothing when no community holds her.</summary>
        public static ScScope ScopeOf(int memberId)
        {
            if (ScCommunities.Count <= 1) return default(ScScope);        // one community: it is always the one in scope
            Member m = ScRegistry.GetMember(memberId);
            return m != null ? ScRegistry.Use(m) : default(ScScope);
        }

        /// <summary>The scope of the community a kill (or a hit) by this entity is for: a player's community, a member's; else none.</summary>
        public static ScScope ScopeOfActor(Entity actor)
        {
            if (ScCommunities.Count <= 1 || actor == null) return default(ScScope);
            EntityPlayer p = actor as EntityPlayer;
            if (p != null) return ScRegistry.Use(p);
            return ScopeOf(actor.entityId);
        }

        // ---- a scope a Harmony prefix opens and its finalizer closes (Patches_Gates' UAI scope, the book patches) ----

        private static readonly System.Collections.Generic.Stack<ScScope> held = new System.Collections.Generic.Stack<ScScope>();

        /// <summary>
        /// Keeps a scope a Harmony prefix opened until its finalizer's PopHeld (one each, always: push default(ScScope) when there
        /// is none, so the pairs stay matched). Main thread only - off it nothing is kept (ScRegistry.Use did nothing there either).
        /// </summary>
        internal static void PushHeld(ScScope s)
        {
            if (!ThreadManager.IsMainThread()) return;
            held.Push(s);
        }

        /// <summary>Closes the scope the matching PushHeld kept.</summary>
        internal static void PopHeld()
        {
            if (!ThreadManager.IsMainThread() || held.Count == 0) return;
            held.Pop().Dispose();
        }

        /// <summary>The player's community's scope (ScRegistry.Use(p)); nothing for null or with one community.</summary>
        public static ScScope ScopeOfPlayer(EntityPlayer p)
        {
            return p != null && ScCommunities.Count > 1 ? ScRegistry.Use(p) : default(ScScope);
        }

        /// <summary>The community of the player with this platform id (a block's or a claim's owner; ScPlayers.CommunityOfId), -1 for none.</summary>
        public static int CommunityOfUser(PlatformUserIdentifierAbs id)
        {
            return id != null && ScRegistry.Loaded ? ScPlayers.CommunityOfId(id.CombinedString) : -1;
        }

        /// <summary>
        /// The scope of the community of the player with this platform id (the server's membership map); nothing for none, with one
        /// community, or on a client (which has no map: its copy is its own community's, Current with no scope).
        /// </summary>
        public static ScScope ScopeOfUser(PlatformUserIdentifierAbs id)
        {
            return id != null && ScRegistry.Loaded && ScCommunities.Count > 1 ? ScRegistry.Use(ScPlayers.CommunityOfId(id.CombinedString)) : default(ScScope);
        }

        /// <summary>
        /// The "host's own player" fallback of server code that picks a recipient (a hire mark, a leader to restore, a tell): that
        /// player when they belong to this community (single player, a listen host - exactly as before MP6); on a listen host whose
        /// player is in another community, this community's anchor (ScPlayers.Anchor: one of its players online, or null); null on a
        /// dedicated server, as before.
        /// </summary>
        public static EntityPlayer LocalPlayerOf(int community)
        {
            EntityPlayerLocal local = ScCommunity.LocalPlayer;
            if (local == null) return null;
            if (ScCommunities.Count <= 1 || ScPlayers.CommunityOf(local) == community) return local;
            return ScPlayers.Anchor(community);
        }

        // ------------------------------------------------------------------ world loops ----

        /// <summary>
        /// The POIs every community's tasks send someone to, added to into - each community's radio contacts, open POI favours,
        /// physical runners' destinations and radio scouting marks: the "in use" set the enclaves' and infestations' seeding and
        /// the missing-survivor pick keep clear of (a world loop: anyone's task). With one community: exactly the four loops it
        /// replaces, in their order.
        /// </summary>
        public static void AddTakenPois(System.Collections.Generic.ICollection<Vector3i> into)
        {
            CommunityState[] all = ScCommunities.Array;
            for (int i = 0; i < all.Length; i++)
            {
                CommunityState cs = all[i];
                foreach (Contact ct in cs.Contacts) into.Add(ct.PoiKey);
                foreach (Prospect p in cs.Prospects.Values) if (p.Favour == FavourKind.Poi && !p.Done) into.Add(p.PoiPos);
                foreach (Member m in cs.Members.Values) if (ScRunners.IsPhysical(m)) into.Add(m.RunDest);
                foreach (Scout s in cs.Community.Scouts) into.Add(s.Poi);
            }
        }

        // ------------------------------------------------------------------ positions ----

        /// <summary>
        /// The community a block position belongs to, or -1: the first community (lowest id) whose home zone holds it, else the
        /// community of the held outpost whose POI footprint holds it. Main thread only (each home is read in its own scope).
        /// </summary>
        public static int CommunityAt(Vector3i pos)
        {
            CommunityState[] all = ScCommunities.Array;
            for (int i = 0; i < all.Length; i++)
            {
                if (!all[i].Community.HasHome) continue;
                using (ScRegistry.Use(all[i].Id))
                    if (ScZone.Contains(pos)) return all[i].Id;
            }
            foreach (Outpost o in ScCommunities.AllOutposts())
                if (o.Holding && o.Contains(pos.x, pos.z)) return o.CommunityId;
            return -1;
        }

        /// <summary>The scope of the community at this position (CommunityAt); nothing when it is nobody's.</summary>
        public static ScScope ScopeAt(Vector3i pos)
        {
            if (ScCommunities.Count <= 1) return default(ScScope);
            int c = CommunityAt(pos);
            return c >= 0 ? ScRegistry.Use(c) : default(ScScope);
        }

        /// <summary>
        /// The scope for a block added at pos: the community whose home or outpost holds it, else the placer's (their platform id;
        /// a block placed out in the wild is theirs), else nothing.
        /// </summary>
        public static ScScope ScopeAt(Vector3i pos, PlatformUserIdentifierAbs placer)
        {
            if (ScCommunities.Count <= 1) return default(ScScope);
            int c = CommunityAt(pos);
            if (c < 0 && placer != null) c = ScPlayers.CommunityOfId(placer.CombinedString);
            return c >= 0 ? ScRegistry.Use(c) : default(ScScope);
        }

        /// <summary>
        /// Is this position another community's ground for the player - inside another community's home zone or one of its
        /// outposts? Server (a client knows its own community only, so it never answers true there). Never with one community.
        /// </summary>
        public static bool OutsiderAt(Vector3i pos, EntityPlayer player)
        {
            if (ScCommunities.Count <= 1 || player == null || !ScRegistry.Loaded) return false;
            int c = CommunityAt(pos);
            return c >= 0 && c != ScPlayers.CommunityOf(player);
        }

        /// <summary>
        /// The community other than 'own' whose home zone (its bounds, ScZone.Bounds - as ScSecurity.OverlapsHome tests a POI
        /// against the home) or held outpost footprint touches the box (inclusive XZ), or -1: home.set and CanEstablish refuse
        /// another community's ground. Main thread only. Never with one community.
        /// </summary>
        public static int OtherCommunityIn(Vector3i min, Vector3i max, int own)
        {
            if (ScCommunities.Count <= 1) return -1;
            foreach (Outpost o in ScCommunities.AllOutposts())
            {
                if (!o.Holding || o.CommunityId == own) continue;
                int[] b = o.Box;
                if (min.x <= b[1] && max.x >= b[0] && min.z <= b[3] && max.z >= b[2]) return o.CommunityId;
            }
            CommunityState[] all = ScCommunities.Array;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].Id == own || !all[i].Community.HasHome) continue;
                using (ScRegistry.Use(all[i].Id))
                {
                    int xMin, xMax, zMin, zMax;
                    ScZone.Bounds(out xMin, out xMax, out zMin, out zMax);
                    if (min.x <= xMax && max.x >= xMin && min.z <= zMax && max.z >= zMin) return all[i].Id;
                }
            }
            return -1;
        }

        /// <summary>
        /// Round MP6 (fix G-m4): this land claim is some community's home (the claim limit never drops one, whoever owns it now). With
        /// one community: ScZone's home test, as before. Server (a client's copy holds only its own community).
        /// </summary>
        public static bool IsAnyHome(Vector3i claim)
        {
            if (ScCommunities.Count <= 1) return ScZone.HasHome && ScZone.Home == claim;
            CommunityState[] all = ScCommunities.Array;
            for (int i = 0; i < all.Length; i++)
            {
                Community c = all[i].Community;
                if (c.HasHome && c.Home == claim) return true;
            }
            return false;
        }

        /// <summary>OtherCommunityIn for a land claim's own zone (the claim's rectangles, ScZone.ClaimRects: an upgraded claim's pieces).</summary>
        public static int OtherCommunityInClaim(Vector3i claim, int own)
        {
            if (ScCommunities.Count <= 1) return -1;
            var rects = new System.Collections.Generic.List<int[]>();
            ScZone.ClaimRects(claim, rects);
            if (rects.Count == 0) return OtherCommunityIn(claim, claim, own);
            for (int i = 0; i < rects.Count; i++)
            {
                int[] r = rects[i];
                int c = OtherCommunityIn(new Vector3i(r[0], claim.y, r[2]), new Vector3i(r[1], claim.y, r[3]), own);
                if (c >= 0) return c;
            }
            return -1;
        }
    }
}

// ---------------------------------------------------------------- dialog requirements (global: type="ScOwnMember, SurvivorCommunity") ----

/// <summary>
/// Round MP6 (part G): the talk's member rows (Config/dialogs.xml: Community, Follow me, Go home, Supply run, Ask to leave, That's
/// all for now, Equipment) - the NPC is a member of the talker's community (ScGates.IsOwnMember). It replaces the old
/// NPCHasCVarSDX sc_member value="1", which reads community 0's members only once sc_member holds community id + 1. With one
/// community it answers as that test did. value="not" inverts. Any machine (a client: the synced cvar and its mirrored community).
/// </summary>
public class DialogRequirementScOwnMember : BaseDialogRequirement
{
    public override bool CheckRequirement(EntityPlayer player, EntityNPC talkingTo)
    {
        bool not = (Value ?? "").Trim().Equals("not", StringComparison.OrdinalIgnoreCase);
        try
        {
            EntityAlive npc = (EntityAlive)talkingTo ?? ScFavours.CurrentNpc(player);
            bool own = ScGates.IsOwnMember(npc, player);
            return not ? !own : own;
        }
        catch (Exception e) { ScLog.Once("req-ownmember", e); return false; }
    }
}

/// <summary>
/// Round MP6 (part G): the NPC is a member of another community than the talker's (ScGates.IsForeignMember) - value="not" hides
/// 0-XNPCCore's rows that act on her (her inventory, her Jobs and gear page) from an outsider, while a stranger, a hired
/// companion and the talker's own members keep them. Never true with one community. Any machine.
/// </summary>
public class DialogRequirementScForeignMember : BaseDialogRequirement
{
    public override bool CheckRequirement(EntityPlayer player, EntityNPC talkingTo)
    {
        bool not = (Value ?? "").Trim().Equals("not", StringComparison.OrdinalIgnoreCase);
        try
        {
            EntityAlive npc = (EntityAlive)talkingTo ?? ScFavours.CurrentNpc(player);
            bool foreign = ScGates.IsForeignMember(npc, player);
            return not ? !foreign : foreign;
        }
        catch (Exception e) { ScLog.Once("req-foreignmember", e); return not; }
    }
}
