using System;
using System.Collections.Generic;
using Platform;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round MP0 (multiplayer, Stage 1): who is playing, who belongs to the community, and where they are - the one place SC
    /// asks those questions, so nothing assumes the host's own player any more.
    /// - Identity: a player is known by their platform id (ClientInfo.InternalId: the cross-platform id, else the platform id;
    ///   the host's own is PlatformManager.InternalLocalUserIdentifier). Entity ids can change between sessions.
    /// - Presence: World.Players.list holds every spawned player on the server - the host plus remote players on a listen
    ///   host, remote players only on a dedicated server. In single player it holds the one local player, so every "any
    ///   player" question answers exactly as the old "the player" did.
    /// - Community: Stage 1 has one shared community (id 0) that every player belongs to; Stage 2's CommunityMode decides.
    ///   Round MP6 (core): a server-side map platform id -> community id (ScWorldState.Membership, saved; "sc community join")
    ///   says who is in which; anyone not in it is in community 0, exactly the shared community of Stage 1. InCommunity,
    ///   Community, TellCommunity, TellCommunityKey, OwnerOnline and Anchor answer for the community in scope
    ///   (ScRegistry.Current) unless given one.
    /// Server-side reads; on a client World.Players.list only holds the players near it.
    /// </summary>
    public static class ScPlayers
    {
        /// <summary>
        /// The only community in Stage 1 (Shared mode), and community 0 since round MP6: every old save's, and everyone's who is in
        /// no other (ScWorldState.Membership).
        /// </summary>
        public const int SharedCommunity = 0;
        /// <summary>
        /// Round MP6 (core): "the community in scope" for the community parameters below (ScRegistry.CurrentId) - their default.
        /// </summary>
        public const int CurrentCommunity = -1;

        /// <summary>Round MP6 (core): the parameter's community: CurrentCommunity is the one in scope.</summary>
        private static int Resolve(int community)
        {
            return community == CurrentCommunity ? ScRegistry.CurrentId : community;
        }

        /// <summary>
        /// Round MP6 (core): the player's community. Server: the membership map by their platform id (not in it, or a community
        /// that no longer exists: community 0). Client: its own player is in the mirrored community (ScRegistry.MirroredCommunity);
        /// another player is answered community 0 - a client does not know (MP6 (part network): send it if a client needs it).
        /// Null: community 0. Cheap while nobody is outside community 0 (no platform id is looked up).
        /// </summary>
        public static int CommunityOf(EntityPlayer p)
        {
            int c;
            TryCommunityOf(p, out c);             // round MP6 (core fix): one path with TryCommunityOf (unknown: community 0, as before)
            return c;
        }

        /// <summary>
        /// Round MP6 (core fix): CommunityOf, false when the player's community cannot be told - on a server while somebody is outside
        /// community 0 and this player's platform id does not resolve (not mapped yet, or their connection already gone at a
        /// disconnect). community is 0 then. Callers that count players for a community skip such a player rather than count
        /// them as community 0's (review C-m5). Null: false. With nobody outside community 0 it is always true (no id looked up).
        /// </summary>
        public static bool TryCommunityOf(EntityPlayer p, out int community)
        {
            community = SharedCommunity;
            if (p == null) return false;
            if (!ScRegistry.Loaded)
            {
                community = p is EntityPlayerLocal ? ScRegistry.MirroredCommunity : SharedCommunity;
                return true;
            }
            if (ScRegistry.WorldState.Membership.Count == 0) return true;
            string id = IdOf(p);
            if (id.Length == 0) return false;
            community = CommunityOfId(id);
            return true;
        }

        /// <summary>Round MP6 (core), server: a connected client's community by its platform id (a package's sender, before or without a body).</summary>
        public static int CommunityOf(ClientInfo ci)
        {
            if (ci == null || ScRegistry.WorldState.Membership.Count == 0) return SharedCommunity;
            try
            {
                PlatformUserIdentifierAbs id = ci.InternalId;
                return CommunityOfId(id != null ? id.CombinedString : null);
            }
            catch (Exception e)
            {
                ScLog.Once("players-community-ci", e);
                return SharedCommunity;
            }
        }

        /// <summary>Round MP6 (core), server: the community of the player with this platform id (online or not); 0 when not in the map.</summary>
        public static int CommunityOfId(string platformId)
        {
            if (string.IsNullOrEmpty(platformId)) return SharedCommunity;
            int c;
            if (!ScRegistry.WorldState.Membership.TryGetValue(platformId, out c)) return SharedCommunity;
            return ScCommunities.ById(c) != null ? c : SharedCommunity;
        }

        /// <summary>
        /// Round MP6 (core), server: puts the player with this platform id in community c (0: out of the map, back in the shared
        /// community). False for an empty id or a community that does not exist. The caller saves. Their followers stay with the
        /// community they belong to (members are a community's, not a player's).
        /// </summary>
        public static bool SetCommunity(string platformId, int c)
        {
            if (string.IsNullOrEmpty(platformId) || ScCommunities.ById(c) == null) return false;
            if (c == SharedCommunity) ScRegistry.WorldState.Membership.Remove(platformId);
            else ScRegistry.WorldState.Membership[platformId] = c;
            return true;
        }

        private static readonly List<EntityPlayer> scratch = new List<EntityPlayer>();
        private static readonly List<EntityPlayer> homeScratch = new List<EntityPlayer>();     // the review (N1): Home's own
        private static readonly List<EntityPlayer> none = new List<EntityPlayer>();

        /// <summary>The players in the world (spawned, alive or dead), never null. The list is live: do not keep it.</summary>
        public static List<EntityPlayer> All
        {
            get
            {
                World w = ScCommunity.World;
                if (w != null && w.Players != null) return w.Players.list;
                none.Clear();
                return none;
            }
        }

        public static bool AnyOnline { get { return All.Count > 0; } }

        /// <summary>The player's platform id ("" when unknown: not yet logged in, or not a player).</summary>
        public static string IdOf(EntityPlayer p)
        {
            try
            {
                if (p == null) return "";
                if (p is EntityPlayerLocal)
                {
                    PlatformUserIdentifierAbs own = PlatformManager.InternalLocalUserIdentifier;
                    return own != null ? own.CombinedString ?? "" : "";
                }
                ConnectionManager cm = SingletonMonoBehaviour<ConnectionManager>.Instance;
                ClientInfo ci = cm != null && cm.Clients != null ? cm.Clients.ForEntityId(p.entityId) : null;
                PlatformUserIdentifierAbs id = ci != null ? ci.InternalId : null;
                return id != null ? id.CombinedString ?? "" : "";
            }
            catch (Exception e)
            {
                ScLog.Once("players-id", e);
                return "";
            }
        }

        /// <summary>The player online with this platform id, or null.</summary>
        public static EntityPlayer ById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            List<EntityPlayer> all = All;
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && string.Equals(IdOf(all[i]), id, StringComparison.Ordinal)) return all[i];
            return null;
        }

        /// <summary>The player with this entity id (alive or dead), or null.</summary>
        public static EntityPlayer ByEntity(int entityId)
        {
            World w = ScCommunity.World;
            return w != null && entityId > 0 ? w.GetEntity(entityId) as EntityPlayer : null;
        }

        /// <summary>
        /// Whether the player belongs to the community (round MP6 (core): the one in scope unless given; Stage 1 answered true for
        /// any player - with everyone in community 0 it still does).
        /// </summary>
        public static bool InCommunity(EntityPlayer p, int community = CurrentCommunity)
        {
            // round MP6 (core fix): a player whose community cannot be told is in none of them (review C-m5)
            int pc;
            return TryCommunityOf(p, out pc) && pc == Resolve(community);
        }

        /// <summary>The community's players online (round MP6 (core): the one in scope unless given). The list is reused: do not keep it.</summary>
        public static List<EntityPlayer> Community(int community = CurrentCommunity)
        {
            scratch.Clear();
            int c = Resolve(community), pc;
            List<EntityPlayer> all = All;
            // round MP6 (core fix): a player whose community cannot be told is in none of them (review C-m5)
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && TryCommunityOf(all[i], out pc) && pc == c) scratch.Add(all[i]);
            return scratch;
        }

        /// <summary>
        /// The community's owner if they are online (by platform id, else by the old display name of a save from before MP0).
        /// Round MP6 (core): the owner of the community in scope.
        /// </summary>
        public static EntityPlayer OwnerOnline()
        {
            Community c = ScRegistry.Community;
            if (c == null) return null;
            EntityPlayer p = ById(c.OwnerId);
            if (p != null) return p;
            if (!string.IsNullOrEmpty(c.OwnerId) || string.IsNullOrEmpty(c.Owner)) return null;
            List<EntityPlayer> all = All;
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && string.Equals(all[i].PlayerDisplayName, c.Owner, StringComparison.Ordinal)) return all[i];
            return null;
        }

        /// <summary>
        /// The player a server-wide tick reasons from when it needs one (a gamestage, a "from where", a tell with no better
        /// recipient): the host's own player on a listen host or in single player (exactly as before MP0); on a dedicated
        /// server the community's owner if online, else a community player at home, else any community player. Null with
        /// nobody online (a dedicated server's world time stands still then anyway).
        /// Round MP6 (core): per community (the one in scope unless given): the host's own player only if they belong to it, its
        /// owner only while online, alive and in it, its players at its home; null when none of its players is online.
        /// </summary>
        public static EntityPlayer Anchor()
        {
            return Anchor(CurrentCommunity);           // round MP6 (core): the old signature kept for any compiled caller
        }

        /// <summary>Round MP6 (core): Anchor() for the given community (CurrentCommunity: the one in scope).</summary>
        public static EntityPlayer Anchor(int community)
        {
            int c = Resolve(community);
            using (ScRegistry.Use(c))          // round MP6 (core): its owner and its home
            {
                EntityPlayerLocal local = ScCommunity.LocalPlayer;
                if (local != null && CommunityOf(local) == c) return local;
                EntityPlayer owner = OwnerOnline();
                if (owner != null && !owner.IsDead() && CommunityOf(owner) == c) return owner;      // the review (S3): a dead owner anchors nothing
                List<EntityPlayer> mine = Community(c);
                for (int i = 0; i < mine.Count; i++)
                    if (!mine[i].IsDead() && ScZone.Contains(mine[i].GetBlockPosition())) return mine[i];
                for (int i = 0; i < mine.Count; i++)
                    if (!mine[i].IsDead()) return mine[i];
                return mine.Count > 0 ? mine[0] : null;
            }
        }

        /// <summary>
        /// Whether a community player stands inside the home zone (the old "the player is home"): living ones only, unless
        /// includeDead (a check that used to read the player's position whether alive or not).
        /// </summary>
        public static bool AnyHome(bool includeDead = false)
        {
            return FirstHome(includeDead) != null;
        }

        /// <summary>The first community player inside the home zone (living ones only unless includeDead), or null.</summary>
        public static EntityPlayer FirstHome(bool includeDead = false)
        {
            if (!ScZone.HasHome) return null;
            List<EntityPlayer> all = All;
            for (int i = 0; i < all.Count; i++)
            {
                EntityPlayer p = all[i];
                if (p != null && (includeDead || !p.IsDead()) && InCommunity(p) && ScZone.Contains(p.GetBlockPosition())) return p;
            }
            return null;
        }

        /// <summary>The community's living players inside the home zone. The list is reused: do not keep it.</summary>
        public static List<EntityPlayer> Home()
        {
            homeScratch.Clear();
            if (!ScZone.HasHome) return homeScratch;
            List<EntityPlayer> all = All;
            for (int i = 0; i < all.Count; i++)
            {
                EntityPlayer p = all[i];
                if (p != null && !p.IsDead() && InCommunity(p) && ScZone.Contains(p.GetBlockPosition())) homeScratch.Add(p);
            }
            return homeScratch;
        }

        /// <summary>Round MP6 (part G): Nearest's community filter for "any community" (its default).</summary>
        public const int AnyCommunity = -2;

        /// <summary>
        /// The nearest living player within maxDist meters of pos, or null. maxDist &lt;= 0: no limit. Round MP6 (part G):
        /// community - AnyCommunity (the default: any player, as before), CurrentCommunity (the one in scope) or a community id.
        /// </summary>
        public static EntityPlayer Nearest(Vector3 pos, float maxDist = 0f, int community = AnyCommunity)
        {
            EntityPlayer best = null;
            float bestSq = maxDist > 0f ? maxDist * maxDist : float.MaxValue;
            List<EntityPlayer> all = All;
            int c = community == AnyCommunity ? AnyCommunity : Resolve(community);      // round MP6 (part G)
            for (int i = 0; i < all.Count; i++)
            {
                EntityPlayer p = all[i];
                if (p == null || p.IsDead()) continue;
                if (c != AnyCommunity && !InCommunity(p, c)) continue;                // round MP6 (part G)
                float sq = (p.position - pos).sqrMagnitude;
                if (sq <= bestSq) { bestSq = sq; best = p; }
            }
            return best;
        }

        /// <summary>Whether a living player stands within dist meters of pos (horizontal distance when flat).</summary>
        public static bool AnyWithin(Vector3 pos, float dist, bool flat = false)
        {
            float sq = dist * dist;
            List<EntityPlayer> all = All;
            for (int i = 0; i < all.Count; i++)
            {
                EntityPlayer p = all[i];
                if (p == null || p.IsDead()) continue;
                Vector3 d = p.position - pos;
                if (flat) d.y = 0f;
                if (d.sqrMagnitude <= sq) return true;
            }
            return false;
        }

        /// <summary>
        /// Round MP0.1: whether a living REMOTE player (not this machine's own) stands within dist meters of pos, flat - an area a
        /// server keeps loaded for a client (ScRunners.SettledWhy's server rule). Never in single player.
        /// </summary>
        public static bool AnyRemoteWithin(Vector3 pos, float dist)
        {
            float sq = dist * dist;
            List<EntityPlayer> all = All;
            for (int i = 0; i < all.Count; i++)
            {
                EntityPlayer p = all[i];
                if (p == null || p is EntityPlayerLocal || p.IsDead()) continue;
                float dx = p.position.x - pos.x, dz = p.position.z - pos.z;
                if (dx * dx + dz * dz <= sq) return true;
            }
            return false;
        }

        /// <summary>
        /// A tell to every community player online (Stage 1: everyone) - the news the whole community should hear. Round MP6
        /// (core): the community in scope unless given.
        /// </summary>
        public static void TellCommunity(string text, string sound, int community = CurrentCommunity)
        {
            List<EntityPlayer> mine = Community(community);
            // copied: Tell can re-enter code that reuses the scratch list
            EntityPlayer[] to = mine.ToArray();
            for (int i = 0; i < to.Length; i++) ScCommunity.Tell(to[i], text, sound);
        }

        /// <summary>
        /// Round MP4 (part: tells): TellCommunity by localization key - each community player online reads it in their own
        /// language (ScCommunity.TellKey: the host's own player formatted here as before, a matched client on its machine).
        /// Round MP6 (core): the players of the community in scope.
        /// </summary>
        public static void TellCommunityKey(string key, string sound, params object[] args)
        {
            List<EntityPlayer> mine = Community(CurrentCommunity);
            // copied: a tell can re-enter code that reuses the scratch list
            EntityPlayer[] to = mine.ToArray();
            for (int i = 0; i < to.Length; i++) ScCommunity.TellKey(to[i], key, sound, args);
        }
    }
}
