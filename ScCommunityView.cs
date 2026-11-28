using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SurvivorCommunity
{
    // round MP7 (part U), 2026-11-27: what players see of the community modes - the derived block's mode=, role=, ownops= and cpl=
    // keys (protocol 9 at the merge) and the view the community window and the land claim radial read on every machine.
    //
    // - The server writes mode= (PerPlayer / PerParty; absent = Shared) and cpl= (the community's players: role, online, entity id,
    //   name) into each community's derived block (ScMirror.BuildDerivedIn), and role= / ownops= into each client's own copy of it
    //   (ScMirror.Send: the block is one community's, the role is the receiver's). They are written only while they mean something:
    //   in PerPlayer/PerParty, or with two or more communities. Shared with one community sends every byte as before.
    // - A client reads them back (ScMirror.ParseDerived: CommunityPlayers; the rest through ScMirror.Str). The host's own player and
    //   single player compute the same answers from the registry (Take).
    // - Server trust: the role and the list come from the server's membership map and owner rule, never from a client's number;
    //   the window only grays or hides, and the server refuses what the sender may not do (ScActions.WhyNot).

    /// <summary>Round MP7 (part U): one player of a community as the players list shows them (cpl=).</summary>
    public sealed class ScCommunityPlayer
    {
        public string Name = "";
        /// <summary>ScCommunityView.RoleOwner / RoleHelper / RoleMember.</summary>
        public string Role = ScCommunityView.RoleMember;
        public bool Online;
        /// <summary>Their entity id while online (the window marks its own player's row), else -1.</summary>
        public int EntityId = -1;
    }

    /// <summary>Round MP7 (part U): the community modes' view for the window and the radial, and the derived block's keys (see the file's top).</summary>
    public static class ScCommunityView
    {
        // ---- the roles (role=, and each cpl= entry's) ----
        public const string RoleOwner = "owner";
        /// <summary>PerParty: the leader of a party holding one of the community's players while the owner is offline (decision 7).</summary>
        public const string RoleHelper = "helper";
        public const string RoleMember = "member";
        /// <summary>PerPlayer/PerParty: a player in no community - their own relations (a personal record).</summary>
        public const string RolePersonal = "personal";

        // ---- the derived block's keys ----
        public const string ModeKey = "mode";
        public const string RoleKey = "role";
        /// <summary>1: the receiver may use the Owner ops (ScOwnerRule.May: the owner, admins, PerParty's helper).</summary>
        public const string OwnOpsKey = "ownops";
        public const string PlayersKey = "cpl";
        /// <summary>
        /// Round MP7 (review U): m1 - the community's true player count, written beside cpl= only when cpl= holds fewer (MaxListed): the
        /// window's count and "not listed" line use it.
        /// </summary>
        public const string PlayersTotalKey = "cplt";
        /// <summary>
        /// At most this many players in cpl= (the block stays small). Round MP7 (review U): m1 - 200 (was 64), and the window pages
        /// through every one it gets; past this the true total goes in cplt= and the window says how many are not listed.
        /// </summary>
        public const int MaxListed = 200;

        // ------------------------------------------------------------------ the server ----

        /// <summary>
        /// Server: the MP7 keys go into the derived block - PerPlayer/PerParty, or two or more communities (Shared mode's admin-made
        /// ones: the players list and Leave). False with one Shared community, so its block is byte for byte as before.
        /// </summary>
        internal static bool KeysOn { get { return ScRegistry.Loaded && (ScMembership.Separate || ScCommunities.Count >= 2); } }

        /// <summary>
        /// Server: the player's role in community cs - personal (their own relations, PerPlayer/PerParty), owner, helper (PerParty: the
        /// leader of a party holding one of its players while its owner is offline - ScOwnerRule.May's own test, part AP's
        /// PartyLeaderMayAct) or member. online: their body when online (the helper test needs it), else null. "" for no community.
        /// </summary>
        internal static string RoleOf(CommunityState cs, string platformId, EntityPlayer online)
        {
            if (cs == null) return "";
            if (cs.Personal && ScMembership.Separate) return RolePersonal;
            if (ScMembership.IsOwner(cs, platformId, online != null ? online.PlayerDisplayName : null)) return RoleOwner;
            if (online != null && IsHelper(cs, online)) return RoleHelper;
            return RoleMember;
        }

        /// <summary>ScOwnerRule.May's PerParty branch (decision 7): the owner is offline and this player leads a party holding one of cs's players.</summary>
        private static bool IsHelper(CommunityState cs, EntityPlayer p)
        {
            if (!ScMembership.Separate || ScMembership.Mode != ScCommunityMode.PerParty) return false;
            return ScPlayers.ById(cs.Community.OwnerId) == null && ScOwnerRule.PartyLeaderMayAct(p, cs.Id);
        }

        /// <summary>
        /// Server: community cs's players for the list - those online in it, those the membership map records in it (offline: their
        /// name from the game's player list), and its owner (always in their own community, listed or not). Shared mode's community 0
        /// lists its online players and its owner only (everyone unlisted is in it). Owner first, then a helper, then online, then by
        /// name. Empty for a personal record. Main thread. Round MP7 (review U): m1 - every one (cpl= takes the first MaxListed).
        /// </summary>
        internal static List<ScCommunityPlayer> PlayersOf(CommunityState cs)
        {
            var rows = new List<ScCommunityPlayer>();
            if (cs == null || (cs.Personal && ScMembership.Separate)) return rows;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            List<EntityPlayer> all = ScPlayers.All;
            for (int i = 0; i < all.Count; i++)
            {
                EntityPlayer p = all[i];
                int c;
                if (p == null || !ScPlayers.TryCommunityOf(p, out c) || c != cs.Id) continue;
                string pid = ScPlayers.IdOf(p);
                if (pid.Length > 0) seen.Add(pid);
                rows.Add(new ScCommunityPlayer { Name = p.PlayerDisplayName ?? "?", Role = RoleOf(cs, pid, p), Online = true, EntityId = p.entityId });
            }
            bool zeroShared = cs.Id == ScPlayers.SharedCommunity && !ScMembership.Separate;
            if (!zeroShared)
                foreach (KeyValuePair<string, int> kv in ScRegistry.WorldState.Membership)
                {
                    if (kv.Value != cs.Id || !seen.Add(kv.Key)) continue;
                    rows.Add(new ScCommunityPlayer { Name = OfflineName(kv.Key, null), Role = RoleOf(cs, kv.Key, null) });
                }
            Community com = cs.Community;
            if (!string.IsNullOrEmpty(com.OwnerId))
            {
                // an owner the map lists elsewhere (Shared mode: an admin moved them) still shows here, online when they are
                if (seen.Add(com.OwnerId))
                {
                    EntityPlayer op = ScPlayers.ById(com.OwnerId);
                    rows.Add(new ScCommunityPlayer
                    {
                        Name = op != null ? op.PlayerDisplayName ?? "?" : OfflineName(com.OwnerId, com.Owner),
                        Role = RoleOwner,
                        Online = op != null,
                        EntityId = op != null ? op.entityId : -1
                    });
                }
            }
            else if (!string.IsNullOrEmpty(com.Owner))
            {
                // a save from before MP0 knows its owner by display name only (IsOwner matches it online)
                bool listed = false;
                for (int i = 0; i < rows.Count && !listed; i++) listed = rows[i].Role == RoleOwner;
                if (!listed) rows.Add(new ScCommunityPlayer { Name = com.Owner, Role = RoleOwner });
            }
            rows.Sort(Compare);
            return rows;
        }

        private static int Rank(string role) { return role == RoleOwner ? 0 : role == RoleHelper ? 1 : 2; }

        private static int Compare(ScCommunityPlayer a, ScCommunityPlayer b)
        {
            int r = Rank(a.Role).CompareTo(Rank(b.Role));
            if (r != 0) return r;
            if (a.Online != b.Online) return a.Online ? -1 : 1;
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>An offline player's name: the game's player list (its display name), else fallback, else "?" (never the platform id).</summary>
        private static string OfflineName(string platformId, string fallback)
        {
            try
            {
                PlatformUserIdentifierAbs uid;
                PersistentPlayerList pp = GameManager.Instance != null ? GameManager.Instance.persistentPlayers : null;
                if (pp != null && !string.IsNullOrEmpty(platformId) && PlatformUserIdentifierAbs.TryFromCombinedString(platformId, out uid) && uid != null)
                {
                    PersistentPlayerData ppd = pp.GetPlayerData(uid);
                    string n = ppd != null && ppd.PlayerName != null ? ppd.PlayerName.DisplayName : null;
                    if (!string.IsNullOrEmpty(n)) return n;
                }
            }
            catch (Exception e)
            {
                ScLog.Once("mp7u-offline-name", e);
            }
            return !string.IsNullOrEmpty(fallback) ? fallback : "?";
        }

        /// <summary>
        /// Server, ScMirror.BuildDerivedIn (community cid's scope): mode= (PerPlayer / PerParty; nothing in Shared) and cpl= (its players),
        /// as key=value lines - "" while KeysOn is false.
        /// </summary>
        internal static string CommunityLines(int cid)
        {
            if (!KeysOn) return "";
            var sb = new StringBuilder();
            if (ScMembership.Separate) sb.Append(ModeKey).Append('=').Append(ScMembership.ModeName(ScMembership.Mode)).Append('\n');
            List<ScCommunityPlayer> rows = PlayersOf(ScCommunities.ById(cid));
            // round MP7 (review U): m1 - the first MaxListed (the owner, a helper and those online come first), and the true total
            // beside them when that cut any
            int total = rows.Count;
            if (rows.Count > MaxListed) rows.RemoveRange(MaxListed, rows.Count - MaxListed);
            sb.Append(PlayersKey).Append('=').Append(EncodePlayers(rows)).Append('\n');
            if (total > rows.Count) sb.Append(PlayersTotalKey).Append('=').Append(total.ToString(CultureInfo.InvariantCulture)).Append('\n');
            return sb.ToString();
        }

        /// <summary>
        /// Server, ScMirror.Send: the receiver's own lines after its community's block - role= (RoleOf) and ownops=1 (ScOwnerRule.May:
        /// it may use the Owner ops). From the client's platform id and body on the server, never from a number it sent. "" while
        /// KeysOn is false.
        /// </summary>
        internal static string ReceiverLines(ClientInfo ci, int cid)
        {
            try
            {
                if (ci == null || !KeysOn) return "";
                CommunityState cs = ScCommunities.ById(cid);
                if (cs == null) return "";
                EntityPlayer p = ScPlayers.ByEntity(ci.entityId);
                PlatformUserIdentifierAbs id = ci.InternalId;
                string pid = id != null ? id.CombinedString ?? "" : "";
                string role = RoleOf(cs, pid, p);
                if (role.Length == 0) return "";
                // round MP7 (review U): m3 - before the client's body exists on the server, the parts of the owner rule that need no
                // body (MayWithoutBody); PerParty's helper needs it and follows at the spawn (ScMirror.OnPlayerSpawned resends)
                bool own = role != RolePersonal && (p != null ? ScOwnerRule.May(p, cs) : MayWithoutBody(ci, cs, role));
                return RoleKey + "=" + role + "\n" + (own ? OwnOpsKey + "=1\n" : "");
            }
            catch (Exception e)
            {
                ScLog.Once("mp7u-receiver-lines", e);
                return "";
            }
        }

        /// <summary>
        /// Round MP7 (review U): m3 - ScOwnerRule.May for a client whose body the server does not have yet: the owner, a community with
        /// no owner yet (IsOwnerOrAdmin's "anyone"), Shared mode without SharedOwnerOnly, or an admin by the client's permission level
        /// (ScActions.IsAdmin's test). PerParty's party leader needs the body: none here.
        /// </summary>
        private static bool MayWithoutBody(ClientInfo ci, CommunityState cs, string role)
        {
            if (role == RoleOwner) return true;
            Community c = cs.Community;
            if (string.IsNullOrEmpty(c.OwnerId) && string.IsNullOrEmpty(c.Owner)) return true;
            if (!ScMembership.Separate && !ScConfig.SharedOwnerOnly) return true;
            try
            {
                GameManager gm = GameManager.Instance;
                return ci != null && gm != null && gm.adminTools != null && gm.adminTools.Users.GetUserPermissionLevel(ci) <= 0;
            }
            catch (Exception e)
            {
                ScLog.Once("mp7u-admin-nobody", e);
                return false;
            }
        }

        // ---- cpl='s text: role:online:entityid:name entries joined by ',' (the name escaped) ----

        internal static string EncodePlayers(List<ScCommunityPlayer> rows)
        {
            var sb = new StringBuilder();
            if (rows == null) return "";
            for (int i = 0; i < rows.Count; i++)
            {
                ScCommunityPlayer r = rows[i];
                if (r == null) continue;
                if (sb.Length > 0) sb.Append(',');
                sb.Append(RoleLetter(r.Role)).Append(':').Append(r.Online ? '1' : '0').Append(':')
                  .Append((r.Online ? r.EntityId : -1).ToString(CultureInfo.InvariantCulture)).Append(':').Append(Escape(r.Name));
            }
            return sb.ToString();
        }

        /// <summary>cpl='s text back into rows (a bad entry is skipped). Never null.</summary>
        internal static List<ScCommunityPlayer> DecodePlayers(string text)
        {
            var rows = new List<ScCommunityPlayer>();
            if (string.IsNullOrEmpty(text)) return rows;
            foreach (string part in text.Split(','))
            {
                string[] f = part.Split(new[] { ':' }, 4);
                int eid;
                if (f.Length != 4 || f[0].Length != 1 || (f[1] != "0" && f[1] != "1")
                    || !int.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out eid)) continue;
                rows.Add(new ScCommunityPlayer { Role = RoleWord(f[0][0]), Online = f[1] == "1", EntityId = f[1] == "1" ? eid : -1, Name = Unescape(f[3]) });
            }
            return rows;
        }

        private static char RoleLetter(string role) { return role == RoleOwner ? 'o' : role == RoleHelper ? 'h' : 'm'; }

        private static string RoleWord(char c) { return c == 'o' ? RoleOwner : c == 'h' ? RoleHelper : RoleMember; }

        /// <summary>A name inside cpl=: the list's own separators and line breaks escaped (%25 first, so it reads back exactly).</summary>
        internal static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("%", "%25").Replace(",", "%2C").Replace(":", "%3A").Replace(";", "%3B").Replace("\n", "%0A").Replace("\r", "%0D");
        }

        internal static string Unescape(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('%') < 0) return s ?? "";
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '%' && i + 2 < s.Length)
                {
                    string hex = s.Substring(i + 1, 2);
                    char c;
                    switch (hex)
                    {
                        case "25": c = '%'; break;
                        case "2C": c = ','; break;
                        case "3A": c = ':'; break;
                        case "3B": c = ';'; break;
                        case "0A": c = '\n'; break;
                        case "0D": c = '\r'; break;
                        default: sb.Append(s[i]); continue;
                    }
                    sb.Append(c);
                    i += 2;
                    continue;
                }
                sb.Append(s[i]);
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ every machine ----

        /// <summary>
        /// What this machine's player sees: the mode, their role, whether the Owner ops are theirs, their community's id and players.
        /// The window takes one per refresh (Take); the radial asks the light form (no players).
        /// </summary>
        public sealed class View
        {
            public ScCommunityMode Mode = ScCommunityMode.Shared;
            /// <summary>RoleOwner / RoleHelper / RoleMember / RolePersonal; "" when the server sends no role (Shared with one community).</summary>
            public string Role = "";
            public bool OwnOps;
            /// <summary>The MP7 keys apply (the server's KeysOn): the players list and Leave may show.</summary>
            public bool ListOn;
            public int Cid;
            /// <summary>Single player (an offline game): one player, so no players list and no Leave.</summary>
            public bool Single;
            public List<ScCommunityPlayer> Players = NoPlayers;
            /// <summary>Round MP7 (review U): m1 - the community's player count (cplt= when the server listed fewer, else Players.Count).</summary>
            public int PlayerTotal;
            /// <summary>
            /// Round MP7 (review U): n8 - this player has a platform id (the host's own may have none in Shared mode): community.leave
            /// refuses one without, so Leave never shows for them.
            /// </summary>
            public bool HasId = true;

            /// <summary>A player with only their own relations (PerPlayer/PerParty): the window's no-community page.</summary>
            public bool Personal { get { return Mode != ScCommunityMode.Shared && Role == RolePersonal; } }
            /// <summary>PerPlayer/PerParty, a founded community, and the Owner ops are not this player's: those buttons gray or hide.</summary>
            public bool Gated { get { return Mode != ScCommunityMode.Shared && Role.Length > 0 && Role != RolePersonal && !OwnOps; } }
            /// <summary>The players list (header button and panel).</summary>
            public bool ShowPlayers { get { return ListOn && Role.Length > 0 && !Personal && !Single; } }
            /// <summary>Leave community: not the owner, not a personal record, and not community 0 in Shared mode (nowhere to leave to).</summary>
            public bool CanLeave
            {
                get { return ShowPlayers && HasId && (Role == RoleMember || Role == RoleHelper) && !(Mode == ScCommunityMode.Shared && Cid == ScPlayers.SharedCommunity); }
            }
        }

        private static readonly List<ScCommunityPlayer> NoPlayers = new List<ScCommunityPlayer>();

        /// <summary>The view before anything is known (bindings run before a world exists): Shared, no role - the window as before.</summary>
        public static readonly View Empty = new View();

        /// <summary>
        /// This machine's view for its player (null: the local player). The host (single player, a listen host) computes it from the
        /// registry as the server would send it; a client reads its copy's derived block. withPlayers: build the players list too.
        /// Main thread.
        /// </summary>
        public static View Take(EntityPlayer local, bool withPlayers)
        {
            var v = new View();
            try
            {
                ConnectionManager cm = ScNet.Cm;
                v.Single = cm != null && cm.CurrentMode == ProtocolManager.NetworkType.OfflineServer;
                if (local == null) local = ScCommunity.LocalPlayer;
                if (ScCommunity.IsHost && ScRegistry.Loaded)
                {
                    v.Mode = ScMembership.Separate ? ScMembership.Mode : ScCommunityMode.Shared;
                    v.ListOn = KeysOn;
                    if (local == null || !v.ListOn) return v;
                    int cid = ScPlayers.CommunityOf(local);
                    CommunityState cs = ScCommunities.ById(cid);
                    v.Cid = cid;
                    if (cs == null) return v;
                    string pid = ScPlayers.IdOf(local);
                    v.HasId = pid.Length > 0;                   // round MP7 (review U): n8
                    v.Role = RoleOf(cs, pid, local);
                    v.OwnOps = v.Role != RolePersonal && ScOwnerRule.May(local, cs);
                    if (withPlayers) v.Players = PlayersOf(cs);
                    v.PlayerTotal = v.Players.Count;
                    return v;
                }
                if (!ScMirror.Active) return v;
                ScCommunityMode m;
                v.Mode = ScMembership.TryParseMode(ScMirror.Str(ModeKey), out m) ? m : ScCommunityMode.Shared;
                v.Role = ScMirror.Str(RoleKey);
                v.OwnOps = ScMirror.Bool(OwnOpsKey);
                v.ListOn = v.Role.Length > 0;
                v.Cid = ScRegistry.MirroredCommunity;
                if (withPlayers) v.Players = ScMirror.CommunityPlayers;
                // round MP7 (review U): m1 - the true count when the server listed fewer
                v.PlayerTotal = Math.Max(v.Players.Count, ScMirror.Int(PlayersTotalKey, 0));
            }
            catch (Exception e)
            {
                ScLog.Once("mp7u-view", e);
            }
            return v;
        }
    }
}
