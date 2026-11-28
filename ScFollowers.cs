using System;
using System.Globalization;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round MP7 (part F, the user's decision 9): MaxFollowers is per player in every community mode, Shared included. A player's
    /// followers are the members following them - Following, or PendingStation (heading home with them) - whose Member.LeaderId
    /// is the player's platform id, plus the enclave people they enlisted (E3's deal and E9's radio fighter: EncPerson.EnlistedById,
    /// else the entity id EnlistedBy). Every community's roster is walked: a player's followers are of their own community, and one
    /// whose leader just moved away is still theirs until the ticker sends her home. Checked by ScCommunity.Follow,
    /// ScEnclaveDeals.EnlistBlocked and the radio's ally fighter (ScEncCalls) through Full; shown by sc party (ScPartyCap.Line) and
    /// sc status through Line. MaxMembers stays per community.
    ///
    /// Single player: the one player leads every follower (ScTicker.LeaderToRestore's rule, whatever leader id a save recorded),
    /// so the count is exactly the old one - the community's followers plus its enlisted people. In multiplayer each player's
    /// limit is their own, so in Shared the community as a whole can have more followers out at once than before (MP7_DESIGN.md
    /// risk 4). Server only (the registry is there); a read, main thread.
    /// </summary>
    public static class ScFollowers
    {
        /// <summary>The player leads MaxFollowers or more already (members and enlisted enclave people together).</summary>
        public static bool Full(EntityPlayer player)
        {
            return Count(player) >= ScConfig.MaxFollowers;
        }

        /// <summary>How many follow this player now: members (Members) plus the enclave people they enlisted (Enlisted).</summary>
        public static int Count(EntityPlayer player)
        {
            return Members(player) + Enlisted(player);
        }

        /// <summary>
        /// The members following this player (Following or PendingStation). Single player: the community's followers, as before
        /// round MP7. No player (telnet, a dedicated server's own console): 0.
        /// </summary>
        public static int Members(EntityPlayer player)
        {
            if (Solo) return ScRegistry.FollowerCount;
            if (player == null) return 0;
            string pid = ScPlayers.IdOf(player);
            int n = 0;
            CommunityState[] all = ScCommunities.Array;
            for (int i = 0; i < all.Length; i++)
            {
                foreach (Member m in all[i].Members.Values)
                {
                    if (m.Status != MemberStatus.Following && m.Status != MemberStatus.PendingStation) continue;
                    if (Leads(m, player, pid)) n++;
                }
            }
            return n;
        }

        /// <summary>
        /// The enclave people this player enlisted (with them now). Single player: every enlisted person of the community, as before
        /// round MP7 (ScEnclaveDeals.EnlistedCount). No player: 0.
        /// </summary>
        public static int Enlisted(EntityPlayer player)
        {
            if (Solo) return ScEnclaveDeals.EnlistedCount();
            if (player == null) return 0;
            string pid = ScPlayers.IdOf(player);
            int n = 0;
            foreach (EncPerson p in ScRegistry.EncPeople) if (EnlistedWith(p, player, pid)) n++;
            return n;
        }

        /// <summary>
        /// Whether this follower is the player's. Her leader's platform id (Member.LeaderId: written at Follow and kept by the ticker
        /// while the link holds) decides when both ids are known. With no leader id recorded (a save from before round MP0) or none
        /// known for the player: her live leader while her body is in the world; else whose the ticker makes her when it re-links -
        /// the host's own player of her community (ScGates.LocalPlayerOf; nobody on a dedicated server), as ScTicker.LeaderToRestore.
        /// </summary>
        private static bool Leads(Member m, EntityPlayer player, string pid)
        {
            string lid = m.LeaderId;
            if (!string.IsNullOrEmpty(lid) && pid.Length > 0) return string.Equals(lid, pid, StringComparison.Ordinal);
            EntityPlayer live = LiveLeader(m.Id);
            if (live != null) return live.entityId == player.entityId;
            return string.IsNullOrEmpty(lid) && ReferenceEquals(ScGates.LocalPlayerOf(m.CommunityId), player);
        }

        /// <summary>
        /// Whether this enclave person is enlisted with the player: by the enlister's platform id when both are known; else (a record
        /// from before round MP7, or no platform id known) by the entity id, while that player is online - the one asked about is.
        /// </summary>
        private static bool EnlistedWith(EncPerson p, EntityPlayer player, string pid)
        {
            if (p == null || !p.Enlisted) return false;
            if (!string.IsNullOrEmpty(p.EnlistedById) && pid.Length > 0) return string.Equals(p.EnlistedById, pid, StringComparison.Ordinal);
            return p.EnlistedBy > 0 && p.EnlistedBy == player.entityId;
        }

        /// <summary>The player the member's body follows now (SCore's Leader, else Owner), or null when none or her body is not in the world.</summary>
        private static EntityPlayer LiveLeader(int memberId)
        {
            try { return EntityUtilities.GetLeaderOrOwner(memberId) as EntityPlayer; }
            catch (Exception e) { ScLog.Once("followers-live-leader", e); return null; }
        }

        /// <summary>Single player (not a listen host with no clients): one player, who leads every follower.</summary>
        private static bool Solo
        {
            get
            {
                ConnectionManager cm = SingletonMonoBehaviour<ConnectionManager>.Instance;
                return cm != null && cm.CurrentMode == ProtocolManager.NetworkType.OfflineServer;
            }
        }

        /// <summary>sc party and sc status: the player's followers against MaxFollowers, members and enlisted people apart. A read.</summary>
        public static string Line(EntityPlayer player)
        {
            string max = ScConfig.MaxFollowers.ToString(CultureInfo.InvariantCulture);
            if (player == null && !Solo) return "SC followers: no player sent this (telnet or the server's own console) - MaxFollowers " + max + " counts each player's own";
            int members = Members(player), enlisted = Enlisted(player);
            return "SC followers " + (members + enlisted).ToString(CultureInfo.InvariantCulture) + " of MaxFollowers " + max + " (yours: members "
                   + members.ToString(CultureInfo.InvariantCulture) + " + enlisted enclave people " + enlisted.ToString(CultureInfo.InvariantCulture)
                   + "; per player in every community mode)";
        }
    }
}
