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
    /// Server-side reads; on a client World.Players.list only holds the players near it.
    /// </summary>
    public static class ScPlayers
    {
        /// <summary>The only community in Stage 1 (Shared mode). Every record and network message carries it.</summary>
        public const int SharedCommunity = 0;

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

        /// <summary>Stage 1: every player belongs to the one shared community.</summary>
        public static bool InCommunity(EntityPlayer p, int community = SharedCommunity)
        {
            return p != null && community == SharedCommunity;
        }

        /// <summary>The community's players online (Stage 1: everyone). The list is reused: do not keep it.</summary>
        public static List<EntityPlayer> Community(int community = SharedCommunity)
        {
            scratch.Clear();
            List<EntityPlayer> all = All;
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && InCommunity(all[i], community)) scratch.Add(all[i]);
            return scratch;
        }

        /// <summary>
        /// The community's owner if they are online (by platform id, else by the old display name of a save from before MP0).
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
        /// </summary>
        public static EntityPlayer Anchor()
        {
            EntityPlayerLocal local = ScCommunity.LocalPlayer;
            if (local != null) return local;
            EntityPlayer owner = OwnerOnline();
            if (owner != null && !owner.IsDead()) return owner;      // the review (S3): a dead owner anchors nothing
            List<EntityPlayer> mine = Community();
            for (int i = 0; i < mine.Count; i++)
                if (!mine[i].IsDead() && ScZone.Contains(mine[i].GetBlockPosition())) return mine[i];
            for (int i = 0; i < mine.Count; i++)
                if (!mine[i].IsDead()) return mine[i];
            return mine.Count > 0 ? mine[0] : null;
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

        /// <summary>The nearest living player (any community) within maxDist meters of pos, or null. maxDist &lt;= 0: no limit.</summary>
        public static EntityPlayer Nearest(Vector3 pos, float maxDist = 0f)
        {
            EntityPlayer best = null;
            float bestSq = maxDist > 0f ? maxDist * maxDist : float.MaxValue;
            List<EntityPlayer> all = All;
            for (int i = 0; i < all.Count; i++)
            {
                EntityPlayer p = all[i];
                if (p == null || p.IsDead()) continue;
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

        /// <summary>A tell to every community player online (Stage 1: everyone) - the news the whole community should hear.</summary>
        public static void TellCommunity(string text, string sound, int community = SharedCommunity)
        {
            List<EntityPlayer> mine = Community(community);
            // copied: Tell can re-enter code that reuses the scratch list
            EntityPlayer[] to = mine.ToArray();
            for (int i = 0; i < to.Length; i++) ScCommunity.Tell(to[i], text, sound);
        }
    }
}
