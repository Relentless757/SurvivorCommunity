using System;
using System.Collections.Generic;
using System.Text;

namespace SurvivorCommunity
{
    // round MP7 (core), 2026-11-27: the host's community mode, a player's own relations before they have a community, founding,
    // joining (a merge), leaving, and the one move function every way of changing a player's community goes through.
    //
    // How it fits together (MP7_DESIGN.md holds the user's twelve decisions):
    // - The mode (Shared, PerPlayer, PerParty) is ScConfig.CommunityMode until the save's first community record starts; then it is
    //   the save's ([world] mode=, ScWorldState.Mode). A save from before MP7 with a community going is Shared (AfterParse, no line
    //   written) unless an admin runs "sc community mode <PerPlayer|PerParty> confirm" once (SwitchMode).
    // - Shared: nothing here changes anything - every unlisted player is in community 0, as before MP7.
    // - PerPlayer/PerParty: every online player has a record. A player in no founded community has a personal record
    //   (CommunityState.Personal): made on the first ask on the server (ScPlayers.CommunityOf -> EnsurePersonal) or at spawn.
    //   Community 0 is the first player's personal record while it has no owner, home or members (single player and the first
    //   founder keep id 0); later players get new ids, never reused (retired ids are tombstones: ScWorldState.Retired).
    // - Founding (community.start, "Start a community here" on the player's own claim) turns the personal record into the
    //   community (Start). Joining another community merges the personal record into it (Merge) and retires it (Retire).
    //   Leaving (community.leave) or losing access hands things over (decision 8) and gives the player a fresh personal record.
    // - Every change of community goes through Move: the console, community.leave, and the ally and party hooks (round MP7 (part
    //   AP): ScAlliesParties.cs - ScAllies, ScParties, and ScJoins, which records how each player joined and Leave's opt-outs).
    // - ScOwnerRule: who may use the Owner ops in each mode; PartyLeaderMayAct is PerParty's helper rule (round MP7 (part AP)).
    // Server, main thread.

    /// <summary>Round MP7 (core), the user's decision 1: how players get communities on a server (ScConfig.CommunityMode, then the save's).</summary>
    public enum ScCommunityMode
    {
        /// <summary>Everyone unlisted is in community 0 (as before MP7). Admins may still make more communities.</summary>
        Shared,
        /// <summary>Each player founds their own community; the founder's land-claim allies with no community join it (part A).</summary>
        PerPlayer,
        /// <summary>Each player founds their own community; a party holding one of its players brings players in (part P).</summary>
        PerParty
    }

    /// <summary>Round MP7 (core): the community mode, personal records, founding, the merge, the one move function (see the file's top).</summary>
    public static class ScMembership
    {
        public const string OpStart = "community.start";
        public const string OpLeave = "community.leave";

        // ---- Move's reasons: who moved the player (the tells and the log) ----
        /// <summary>An admin's console (sc community join / leave / owner) or the mode switch.</summary>
        public const string ByAdmin = "admin";
        /// <summary>The player's own Leave (community.leave).</summary>
        public const string ByLeave = "leave";
        /// <summary>Round MP7 (part AP, ScAllies): a land-claim ally of a founder joined that founder's community (PerPlayer).</summary>
        public const string ByAlly = "ally";
        /// <summary>Round MP7 (part AP, ScAllies): the ally link to the founder was removed - the player loses access (PerPlayer).</summary>
        public const string ByAllyRemoved = "allyremoved";
        /// <summary>Round MP7 (part AP, ScParties): a party holding one of the community's players brought the player in (PerParty).</summary>
        public const string ByParty = "party";
        /// <summary>Round MP7 (part AP, ScParties): the player left the party or was kicked - they lose access (PerParty).</summary>
        public const string ByPartyLeft = "partyleft";

        // ------------------------------------------------------------------ the mode ----

        /// <summary>
        /// The community mode in force: the save's once locked (ScWorldState.ModeLocked), else the config's CommunityMode. Read on the
        /// server; a client has no save (its copy is always one community's).
        /// </summary>
        public static ScCommunityMode Mode
        {
            get
            {
                ScWorldState ws = ScRegistry.WorldState;
                return ws.ModeLocked ? ws.Mode : ScConfig.CommunityMode;
            }
        }

        /// <summary>
        /// PerPlayer or PerParty on the server with the registry loaded: players have their own records and nobody is in community 0
        /// by default. False in Shared, on a client and while a save is read.
        /// </summary>
        public static bool Separate { get { return ScRegistry.Loaded && Mode != ScCommunityMode.Shared; } }

        public static string ModeName(ScCommunityMode m)
        {
            switch (m)
            {
                case ScCommunityMode.PerPlayer: return "PerPlayer";
                case ScCommunityMode.PerParty: return "PerParty";
                default: return "Shared";
            }
        }

        /// <summary>Shared | PerPlayer | PerParty, any case (also "player", "party", "per player"). False for anything else.</summary>
        public static bool TryParseMode(string s, out ScCommunityMode m)
        {
            m = ScCommunityMode.Shared;
            if (string.IsNullOrEmpty(s)) return false;
            switch (s.Replace(" ", "").Replace("_", "").Replace("-", "").Trim().ToLowerInvariant())
            {
                case "shared": m = ScCommunityMode.Shared; return true;
                case "perplayer": case "player": m = ScCommunityMode.PerPlayer; return true;
                case "perparty": case "party": m = ScCommunityMode.PerParty; return true;
                default: return false;
            }
        }

        /// <summary>
        /// The save's mode is fixed now (the first community record started: a home start in Shared, a personal record or a founding
        /// in PerPlayer/PerParty, "sc community new"): the config's value is written as [world] mode=. Nothing once locked. The
        /// caller saves.
        /// </summary>
        internal static void LockMode()
        {
            if (!ScRegistry.Loaded) return;
            ScWorldState ws = ScRegistry.WorldState;
            if (ws.ModeLocked) return;
            ScConfig.EnsureParsed();
            ws.Mode = ScConfig.CommunityMode;
            ws.ModeLocked = true;
            ws.ModeWritten = true;
            ScLog.Out("community mode " + ModeName(ws.Mode) + " is fixed into this save from now on (its first community record started)");
        }

        /// <summary>The config's CommunityMode differs from the save's (the save's stays): told once per load.</summary>
        private static bool configDiffTold;

        /// <summary>ScRegistry.Load, at its end (server): the mode in force in the log, and a config that differs from the save's, once.</summary>
        internal static void OnRegistryLoaded()
        {
            configDiffTold = false;
            try
            {
                ScConfig.EnsureParsed();
                ScWorldState ws = ScRegistry.WorldState;
                if (!ws.ModeLocked)
                    ScLog.Out("community mode: " + ModeName(ScConfig.CommunityMode) + " (the config's; fixed into this save when its first community record starts)");
                else
                {
                    ScLog.Out("community mode: " + ModeName(ws.Mode) + (ws.ModeWritten ? " (the save's)"
                              : " (a save from before MP7: Shared, until an admin runs sc community mode <PerPlayer|PerParty> confirm)"));
                    TellConfigDiff();
                }
                // round MP7 (core review): m1 - the personal records that hold nothing go (their players get fresh ones at their next
                // spawn); m11 - AutoHomeFirstClaim adopts nothing for a player's own relations: said once
                if (Separate)
                {
                    int pruned = PruneEmptyPersonal();
                    if (pruned > 0) ScLog.Out("community: " + pruned + " personal record(s) that held nothing dropped at load - their players get fresh ones at their next spawn");
                    if (ScConfig.AutoHomeFirstClaim)
                        ScLog.Out("community mode " + ModeName(Mode) + ": AutoHomeFirstClaim adopts no claim for a player with only their own relations - their community starts from their land claim's radial (Start a community here)");
                }
                // round MP7 (part AP): PerPlayer - the land-claim allies reconciled with the communities (no ally event fires at load):
                // owed from here, run at GameStartDone or the first spawn (the game does not hold the ally store read here yet)
                ScAllies.OnRegistryLoaded();
            }
            catch (Exception e)
            {
                ScLog.Once("mp7-loaded", e);
            }
        }

        /// <summary>
        /// Round MP7 (core review): m1, m5 - the record holds nothing worth keeping: no home, members, outposts, favors, requests,
        /// radio contacts, scouting marks, radios or watch posts, grudges, stock, and no saved relation row with any enclave,
        /// infestation or band (what SC's markers and the held rewards are built from). Any mode; reads no scope.
        /// </summary>
        internal static bool HoldsNothing(CommunityState cs)
        {
            if (cs == null) return true;
            Community c = cs.Community;
            if (c.HasHome || cs.Members.Count > 0 || cs.Outposts.Count > 0 || cs.Prospects.Count > 0 || cs.EncRequests.Count > 0 || cs.Contacts.Count > 0
                || cs.Grudges.Count > 0 || c.Scouts.Count > 0 || c.Radios.Count > 0 || c.Relays.Count > 0 || c.WatchPosts.Count > 0 || c.Towers.Count > 0) return false;
            for (int i = 0; i < ScNames.ResourceCount; i++) if (c.Stock[i] > 0) return false;
            foreach (Enclave e in ScRegistry.Enclaves) { EncRel r = e.Rels.Peek(cs.Id); if (r != null && r.HasSaved) return false; }
            foreach (Infestation inf in ScRegistry.Infestations) { InfRel r = inf.Rels.Peek(cs.Id); if (r != null && r.HasSaved) return false; }
            foreach (Band b in ScRegistry.Bands) { BandRel r = b.Rels.Peek(cs.Id); if (r != null && r.HasSaved) return false; }
            return true;
        }

        /// <summary>
        /// Round MP7 (core review): m1 (the coordinator's decision) - at load, every personal record that holds nothing (HoldsNothing)
        /// goes, community 0 never: its player is unlisted and gets a fresh record at their next spawn, so the save and every "all
        /// communities" walk (GetMember, the ground and hostility lookups, the save) stay the size of what players built. No
        /// tombstone: nothing refers to an empty record, and the world's id counter (communityid=) already keeps its id from being
        /// handed out again. Returns how many went.
        /// </summary>
        internal static int PruneEmptyPersonal()
        {
            if (!Separate) return 0;
            var gone = new List<CommunityState>();
            foreach (CommunityState cs in ScCommunities.All)
                if (cs.Personal && cs.Id != ScPlayers.SharedCommunity && HoldsNothing(cs)) gone.Add(cs);
            if (gone.Count == 0) return 0;
            var ids = new HashSet<int>();
            foreach (CommunityState cs in gone) ids.Add(cs.Id);
            ScWorldState ws = ScRegistry.WorldState;
            var keys = new List<string>();
            foreach (KeyValuePair<string, int> kv in ws.Membership) if (ids.Contains(kv.Value)) keys.Add(kv.Key);
            foreach (string k in keys) ws.Membership.Remove(k);
            keys.Clear();
            foreach (KeyValuePair<string, int> kv in ws.ConsoleUse) if (ids.Contains(kv.Value)) keys.Add(kv.Key);
            foreach (string k in keys) ws.ConsoleUse.Remove(k);
            foreach (CommunityState cs in gone)
            {
                foreach (Enclave e in ScRegistry.Enclaves) e.Rels.Drop(cs.Id);
                foreach (Infestation inf in ScRegistry.Infestations) inf.Rels.Drop(cs.Id);
                foreach (Band b in ScRegistry.Bands) b.Rels.Drop(cs.Id);
                ScCommunities.Remove(cs);
            }
            return gone.Count;
        }

        /// <summary>
        /// Round MP7 (core review): n2 - a player's display name for the record and the tells: online their body's, else the game's
        /// persistent player data (players.xml), else the id itself.
        /// </summary>
        internal static string NameOf(string platformId)
        {
            if (string.IsNullOrEmpty(platformId)) return "";
            try
            {
                EntityPlayer online = ScPlayers.ById(platformId);
                if (online != null && !string.IsNullOrEmpty(online.PlayerDisplayName)) return online.PlayerDisplayName;
                PlatformUserIdentifierAbs uid;
                PersistentPlayerList pp = GameManager.Instance != null ? GameManager.Instance.persistentPlayers : null;
                if (pp != null && platformId.IndexOf('_') > 0 && PlatformUserIdentifierAbs.TryFromCombinedString(platformId, out uid) && uid != null)
                {
                    PersistentPlayerData ppd = pp.GetPlayerData(uid);
                    string n = ppd != null && ppd.PlayerName != null ? ppd.PlayerName.DisplayName : null;
                    if (!string.IsNullOrEmpty(n)) return n;
                }
            }
            catch (Exception e)
            {
                ScLog.Once("mp7-nameof", e);
            }
            return platformId;
        }

        /// <summary>The config's CommunityMode against the save's: one warning per load when they differ (the save's wins).</summary>
        private static void TellConfigDiff()
        {
            ScWorldState ws = ScRegistry.WorldState;
            if (configDiffTold || !ws.ModeLocked || ws.Mode == ScConfig.CommunityMode) return;
            configDiffTold = true;
            ScLog.Warn("community mode: the config says CommunityMode=" + ModeName(ScConfig.CommunityMode) + ", but this save is " + ModeName(ws.Mode)
                       + " - the save's mode stays (it is fixed when the save's first community record starts)");
        }

        /// <summary>For sc community list: the mode, the save's and the config's.</summary>
        internal static string ModeLine()
        {
            ScWorldState ws = ScRegistry.WorldState;
            ScConfig.EnsureParsed();
            string cfg = ModeName(ScConfig.CommunityMode);
            if (!ws.ModeLocked) return "mode " + cfg + " (the config's; not fixed yet - the save's first community record fixes it)";
            string line = "mode " + ModeName(ws.Mode) + (ws.ModeWritten ? " (the save's" : " (a save from before MP7, Shared until sc community mode <PerPlayer|PerParty> confirm")
                          + (ws.Mode == ScConfig.CommunityMode ? "; the config agrees)" : "; the config says " + cfg + ", which this save ignores)");
            return line + (ScConfig.MaxCommunities > 0 ? ", MaxCommunities " + ScConfig.MaxCommunities : "");
        }

        // ------------------------------------------------------------------ personal records ----

        /// <summary>EnsurePersonal is running (a save inside it may ask CommunityOf again).</summary>
        private static bool ensuring;

        /// <summary>
        /// PerPlayer/PerParty, server, main thread: the community of the player with this platform id, made when they are in none - a
        /// personal record: community 0 when it is claimable (ZeroClaimable: single player and the first founder keep id 0), else a
        /// new one (ScCommunities.CreatePersonal). The player is mapped to it, the mode is locked, the registry saved. p (their body,
        /// may be null before they spawn) and name give the record's owner name. NoCommunity when it cannot be made here (Shared, off
        /// the main thread, no id, the world closing).
        /// </summary>
        public static int EnsurePersonal(EntityPlayer p, string platformId, string name)
        {
            if (!Separate || string.IsNullOrEmpty(platformId) || ensuring || ScRegistry.IsShuttingDown || !ThreadManager.IsMainThread()) return ScPlayers.NoCommunity;
            int have = ScPlayers.CommunityOfId(platformId);
            if (have != ScPlayers.NoCommunity) return have;
            ensuring = true;
            try
            {
                // round MP7 (core review): n4 - a personal record this player owns that the map lost (a hand edit, a dropped line) is theirs again
                foreach (CommunityState own in ScCommunities.All)
                {
                    if (!own.Personal || !string.Equals(own.Community.OwnerId, platformId, StringComparison.Ordinal)) continue;
                    ScRegistry.WorldState.Membership[platformId] = own.Id;
                    ScLog.Out("community: " + own.Label + " is " + platformId + "'s own relations again (the map had lost them)");
                    ScRegistry.SaveAtomic();
                    return own.Id;
                }
                // round MP7 (core review): n2 - an offline player's name from the game's player data, never their id when it is there
                string who = p != null && !string.IsNullOrEmpty(p.PlayerDisplayName) ? p.PlayerDisplayName : (!string.IsNullOrEmpty(name) ? name : NameOf(platformId));
                CommunityState cs;
                bool zero = ZeroClaimable();
                if (zero)
                {
                    cs = ScCommunities.Zero;
                    cs.Personal = true;
                    cs.Community.OwnerId = platformId;
                    cs.Community.Owner = who;
                }
                else cs = ScCommunities.CreatePersonal(platformId, who);
                ScRegistry.WorldState.Membership[platformId] = cs.Id;
                LockMode();
                ScLog.Out("community: " + (who.Length > 0 ? who : platformId) + " has no community yet - their own relations are " + cs.Label
                          + (zero ? " (community 0 was free)" : "") + " (" + ModeName(Mode) + ")");
                ScRegistry.SaveAtomic();
                return cs.Id;
            }
            catch (Exception e)
            {
                ScLog.Once("mp7-personal", e);
                return ScPlayers.NoCommunity;
            }
            finally
            {
                ensuring = false;
            }
        }

        /// <summary>
        /// Community 0 may become a player's personal record: it has no owner, no home, no members or outposts, and nobody is mapped
        /// to it (a fresh save's, or one a merge emptied: ScCommunities.ResetZero). Round MP7 (core review): m5 - and it holds nothing
        /// else either (HoldsNothing: favors, requests, stock, relations ...): what others built there is never handed to whoever
        /// comes first.
        /// </summary>
        private static bool ZeroClaimable()
        {
            CommunityState z = ScCommunities.Zero;
            Community c = z.Community;
            if (!string.IsNullOrEmpty(c.OwnerId) || !string.IsNullOrEmpty(c.Owner) || !HoldsNothing(z)) return false;
            return !ScRegistry.WorldState.Membership.ContainsValue(ScPlayers.SharedCommunity);
        }

        /// <summary>
        /// The founded community this player owns (an owner is always in their own community), or NoCommunity - CommunityOfId's answer
        /// for a player the map does not list (PerPlayer/PerParty).
        /// </summary>
        public static int FoundedBy(string platformId)
        {
            if (string.IsNullOrEmpty(platformId)) return ScPlayers.NoCommunity;
            CommunityState[] all = ScCommunities.Array;
            for (int i = 0; i < all.Length; i++)
                if (!all[i].Personal && string.Equals(all[i].Community.OwnerId, platformId, StringComparison.Ordinal)) return all[i].Id;
            return ScPlayers.NoCommunity;
        }

        /// <summary>
        /// A founded community: not a personal record, and going - any community 1+ (founded, or made by sc community new), or
        /// community 0 once it has an owner, a home or members. MaxCommunities counts these.
        /// </summary>
        public static bool IsFounded(CommunityState cs)
        {
            if (cs == null || cs.Personal) return false;
            if (cs.Id != ScPlayers.SharedCommunity) return true;
            Community c = cs.Community;
            return c.HasHome || !string.IsNullOrEmpty(c.OwnerId) || !string.IsNullOrEmpty(c.Owner) || cs.Members.Count > 0;
        }

        /// <summary>How many founded communities this save has (IsFounded).</summary>
        public static int FoundedCount()
        {
            int n = 0;
            CommunityState[] all = ScCommunities.Array;
            for (int i = 0; i < all.Length; i++) if (IsFounded(all[i])) n++;
            return n;
        }

        /// <summary>This player owns the community (its owner id; a save from before MP0 knows only the display name).</summary>
        public static bool IsOwner(CommunityState cs, string platformId, string displayName)
        {
            if (cs == null) return false;
            Community c = cs.Community;
            if (!string.IsNullOrEmpty(c.OwnerId)) return string.Equals(c.OwnerId, platformId, StringComparison.Ordinal);
            return !string.IsNullOrEmpty(c.Owner) && !string.IsNullOrEmpty(displayName) && string.Equals(c.Owner, displayName, StringComparison.Ordinal);
        }

        /// <summary>
        /// ModApi's PlayerSpawnedInWorld handler, every machine: on the server, the spawning player's record - PerPlayer/PerParty: their
        /// personal one when they have none (the first ask makes it).
        /// </summary>
        public static void OnPlayerSpawned(int entityId, ClientInfo ci, bool local)
        {
            try
            {
                if (!ScRegistry.Loaded || !ScCommunity.IsHost) return;
                if (Separate)
                {
                    if (ci != null) ScPlayers.CommunityOf(ci);
                    else if (local && ScCommunity.LocalPlayer != null) ScPlayers.CommunityOf(ScCommunity.LocalPlayer);
                    else
                    {
                        EntityPlayer p = ScPlayers.ByEntity(entityId);
                        if (p != null) ScPlayers.CommunityOf(p);
                    }
                    // round MP7 (core review): n2 - an owner recorded by their id (made while they were offline) gets their name now
                    EntityPlayer sp = local ? (EntityPlayer)ScCommunity.LocalPlayer : ScPlayers.ByEntity(entityId);
                    string spid = sp != null ? ScPlayers.IdOf(sp) : "";
                    if (spid.Length > 0 && !string.IsNullOrEmpty(sp.PlayerDisplayName))
                        foreach (CommunityState cs in ScCommunities.All)
                        {
                            Community c = cs.Community;
                            if (string.Equals(c.OwnerId, spid, StringComparison.Ordinal) && (c.Owner.Length == 0 || c.Owner == spid)) { c.Owner = sp.PlayerDisplayName; ScRegistry.SaveAtomic(); }
                        }
                }
                // round MP7 (part AP): PerPlayer - the allies reconcile for this player at each spawn (allies change while they are offline)
                if (Separate) ScAllies.OnPlayerSpawned(ScAllies.SpawnedId(entityId, ci, local));
            }
            catch (Exception e)
            {
                ScLog.Once("mp7-spawned", e);
            }
        }

        /// <summary>For tells: whose community it is - its owner's name, else its name, else its id.</summary>
        internal static string Whose(CommunityState cs)
        {
            if (cs == null) return "?";
            if (!string.IsNullOrEmpty(cs.Community.Owner)) return cs.Community.Owner;
            if (!string.IsNullOrEmpty(cs.Community.Name)) return cs.Community.Name;
            return "#" + cs.Id;
        }

        // ------------------------------------------------------------------ founding ----

        /// <summary>
        /// community.start x y z: "Start a community here" on the sender's own land claim (TEFeatureSCCommunity's radial asks it through
        /// ScActionsClaim.RequestHome). Anyone may ask; Start decides.
        /// </summary>
        [ScAction(OpStart, ScActionAccess.Anyone)]
        private static void StartAction(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            string message;
            if (Start(sender, args.Pos(0), out message)) reply.Done(message, "");
            else reply.Fail(message);
        }

        /// <summary>
        /// Starts the sender's community on their claim. Shared (and a founded community with no home yet, such as one "sc community
        /// new" made): what home.set mode 1 always did - the Owner rule, refused once a home exists, the home set (the owner written
        /// only now, and only when it has none: ScZone.SetHome). PerPlayer/PerParty with a personal record: that record becomes the
        /// community (decision 3 and 4) - everything it built stays, the owner is already the founder; refused (scMp7MaxCommunities)
        /// when MaxCommunities founded communities exist. message: a localization key (or text) for the reply. Saves.
        /// </summary>
        internal static bool Start(EntityPlayer sender, Vector3i claim, out string message)
        {
            message = "scActionDenied";
            CommunityState cs = sender != null ? ScCommunities.ById(ScPlayers.CommunityOf(sender)) : null;
            if (cs == null) { message = Separate ? "scNoCommunity" : "scActionDenied"; return false; }
            string why = ScActionsClaim.WhyNotOwnClaim(claim, sender);
            if (why != null) { message = why; return false; }
            if (ScOutposts.IsOutpostClaim(claim)) { message = "scHomeNotOutpost"; return false; }     // an outpost is never the home
            string by = sender is EntityPlayerLocal ? "" : " (by " + (sender.PlayerDisplayName ?? "?") + ")";
            using (ScRegistry.Use(cs.Id))
            {
                // round MP6 (part G)'s gate, as home.set: never a home whose zone reaches another community's home or outposts
                if (ScGates.OtherCommunityInClaim(claim, cs.Id) >= 0) { message = "scHomeOtherCommunity"; return false; }
                if (!cs.Personal || !Separate)
                {
                    if (!ScOwnerRule.May(sender, cs)) { message = "scActionDenied"; return false; }
                    if (cs.Community.HasHome) { message = "scHomeAlreadyStarted"; return false; }
                    cs.Personal = false;                     // round MP7 (core review): n3 - a flag a Shared save carries means nothing
                    ScZone.SetHome(claim, sender);
                    ScRegistry.SaveAtomic();
                    ScLog.Out("community started: home set to " + claim + " by radial (Start a community here)" + (ScCommunities.Count > 1 ? " - " + cs.Label : "") + by);
                    message = "scHomeStarted";
                    return true;
                }
                int max = ScConfig.MaxCommunities;
                if (max > 0 && FoundedCount() >= max)
                {
                    ScLog.Out("community: " + sender.PlayerDisplayName + " may not start a community - " + FoundedCount() + " exist, MaxCommunities " + max);
                    message = "scMp7MaxCommunities";
                    return false;
                }
                cs.Personal = false;
                ScZone.SetHome(claim, sender);           // the owner: the founder's already (SetHome writes one only when there is none)
                string pid = ScPlayers.IdOf(sender);
                if (pid.Length > 0) ScRegistry.WorldState.Membership[pid] = cs.Id;
                LockMode();
                ScRegistry.SaveAtomic();
                ScLog.Out("community founded: " + sender.PlayerDisplayName + "'s own relations are " + cs.Label + " now, home " + claim + " (" + ModeName(Mode) + ")" + by);
                // round MP7 (part AP): PerParty - the founder's party members with personal records join the new community; PerPlayer -
                // the founder's land-claim allies with personal records join it (each through Move, merged)
                ScParties.OnFounded(sender, cs);
                ScAllies.OnFounded(sender, cs);
                message = "scMp7Founded";
                return true;
            }
        }

        // ------------------------------------------------------------------ leaving ----

        /// <summary>
        /// community.leave: the sender leaves their community (decision 10). Refused for its owner (an admin can hand it over: sc
        /// community owner), for community 0 in Shared mode (nowhere to leave to), and for a personal record. Shared: a community 1+
        /// player goes back to community 0; PerPlayer/PerParty: they get a fresh personal record. Move tells them and the community.
        /// </summary>
        [ScAction(OpLeave, ScActionAccess.Community)]
        private static void LeaveAction(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            string pid = ScPlayers.IdOf(sender);
            CommunityState cs = ScCommunities.ById(ScPlayers.CommunityOf(sender));
            if (cs == null || pid.Length == 0) { reply.Fail("scActionDenied"); return; }
            if (cs.Personal) { reply.Fail("scMp7LeaveNone"); return; }
            if (IsOwner(cs, pid, sender.PlayerDisplayName)) { reply.Fail("scMp7LeaveOwner"); return; }
            if (!Separate && cs.Id == ScPlayers.SharedCommunity) { reply.Fail("scMp7LeaveNowhere"); return; }
            // round MP7 (part AP): Leave sticks - Move's ScJoins.Noted stores the opt-out (this player, this community), so neither the
            // allies reconcile nor another player's party invite pulls them back in until the link is made again
            MoveResult r = Move(pid, ScPlayers.NoCommunity, ByLeave, sender.PlayerDisplayName);
            if (!r.Ok) { reply.Fail(r.Error); return; }
            reply.Done();                               // Move told them (scMp7Left)
        }

        // ------------------------------------------------------------------ the one move function ----

        /// <summary>What Move did (for the console and the log).</summary>
        public sealed class MoveResult
        {
            public bool Ok;
            /// <summary>Why not (text for the console, or a localization key for an op's reply).</summary>
            public string Error = "";
            /// <summary>The community they were in (NoCommunity: none) and the one they are in now.</summary>
            public int From = ScPlayers.NoCommunity, To = ScPlayers.NoCommunity;
            /// <summary>Their followers who stayed with the old community and went home to it.</summary>
            public int Followers;
            /// <summary>Accepted requests handed to another player of the old community, and those withdrawn (nobody else online).</summary>
            public int HandedOver, Withdrawn;
            /// <summary>Favor quests closed in their journal (the favors stay the old community's).</summary>
            public int FavoursClosed;
            /// <summary>Their personal record was merged into the community they joined (and retired); what it brought.</summary>
            public bool Merged;
            public string MergeNote = "";
            /// <summary>The new community's grudge against them was dropped.</summary>
            public bool Forgiven;
        }

        /// <summary>
        /// The one way a player changes community (the console, community.leave, the ally and party hooks of parts A and P): the
        /// player with this platform id goes to community toCid - NoCommunity for none (Shared: community 0; PerPlayer/PerParty: a
        /// fresh personal record). reason: who moved them (By*), for the tells. who: their name for the tells and the log (null: their
        /// display name online, else the id). In order:
        /// 1. their talk holds and work shifts end (ScWork.ForgetPlayer, the disconnect path);
        /// 2. decision 8, leaving a founded community: its requests they accepted pass to another online player of it (else
        ///    withdrawn, no standing lost); its favors they hold stay its own - their journal copies close (see ReleaseFavours'
        ///    decision-needed note);
        /// 3. their followers stay members of the old community and go home to it (stationed now, or resident when unloaded);
        /// 4. from a personal record into a community: the merge (Merge), then the record retires (Retire); to none: a fresh
        ///    personal record; else the map changes;
        /// 5. the new community's grudge against them ends;
        /// 6. tells to them and to both communities; the log; a save.
        /// Refused: a community that does not exist, the one they are in, a personal record as the target (PerPlayer/PerParty), and
        /// (PerPlayer/PerParty) the owner of a founded community leaving it - an owner is always in their own (hand it over first).
        /// Server, main thread.
        /// Round MP7 (part AP): the ally event and reconcile (ScAllies) call Move(id, founder's cid, ByAlly) and Move(id, NoCommunity,
        /// ByAllyRemoved); the party postfixes (ScParties: ServerHandleAcceptInvite, ServerHandleLeaveParty, ServerHandleKickParty)
        /// call Move(id, cid, ByParty) and Move(id, NoCommunity, ByPartyLeft) - an owner's leave is refused here, so the founder
        /// keeps it. ScJoins.Noted records how they joined and Leave's opt-out.
        /// </summary>
        public static MoveResult Move(string platformId, int toCid, string reason, string who = null)
        {
            var res = new MoveResult();
            string stage = "";                         // round MP7 (core review): m10 - what already ran, should a step throw
            try
            {
                if (!ScRegistry.Loaded || string.IsNullOrEmpty(platformId)) { res.Error = "scActionNotReady"; return res; }
                bool separate = Separate;
                bool leaving = toCid == ScPlayers.NoCommunity;
                if (leaving && !separate) toCid = ScPlayers.SharedCommunity;
                EntityPlayer mover = ScPlayers.ById(platformId);
                // round MP7 (core review): n2 - an offline player's name from the game's player data (the console passes their id)
                if (string.IsNullOrEmpty(who) || who == platformId) who = NameOf(platformId);
                int was = ScPlayers.CommunityOfId(platformId);
                res.From = was;
                CommunityState old = ScCommunities.ById(was);
                CommunityState target = toCid == ScPlayers.NoCommunity ? null : ScCommunities.ById(toCid);
                if (toCid != ScPlayers.NoCommunity && target == null) { res.Error = "there is no community " + toCid + " (sc community list)"; return res; }
                if (target != null && was == toCid) { res.Error = who + " is already in " + target.Label; return res; }
                if (target == null && (old == null || old.Personal)) { res.Error = who + " is in no community already" + (old != null ? " (their own relations: " + old.Label + ")" : ""); return res; }
                if (separate && target != null && target.Personal)
                {
                    // round MP7 (core review): n8 - community 0 emptied by a merge is given out with sc community owner, never joined
                    bool free = target.Id == ScPlayers.SharedCommunity && string.IsNullOrEmpty(target.Community.OwnerId) && string.IsNullOrEmpty(target.Community.Owner);
                    res.Error = free ? target.Label + " is free (no owner): sc community owner " + target.Id + " <player> makes it that player's community"
                                     : target.Label + " is a player's own relations, not a community anyone joins";
                    return res;
                }
                if (separate && old != null && !old.Personal && IsOwner(old, platformId, mover != null ? mover.PlayerDisplayName : null))
                {
                    res.Error = who + " owns " + old.Label + " - an owner is always in their own community: hand it over first (sc community owner " + old.Id + " <player>)";
                    return res;
                }
#if SC_V34
                // round MP6 (fix G-m1, F-m2), kept: a talk hold or work shift the mover owned ends now (the disconnect path), BEFORE the
                // move - so a listen host's own shift is detached from the host (still of her community), not from the old community's anchor
                if (mover != null) ScWork.ForgetPlayer(mover.entityId);
#endif
                // round MP7 (core review): m2 - their other live links to the old community end too (enlisted people, the storage view, the panel hold)
                if (old != null) EndLinks(old, mover, who);
                stage = "the hand-over";
                // 2. decision 8 (a founded community's things; a personal record's go along with the merge)
                if (old != null && !old.Personal) HandOver(old, platformId, mover, who, res);
                stage = "the hand-over and the followers";
                // 3. their followers stay with the community they belong to and go home to it (round MP6 (part G), the console's rule)
                if (old != null) res.Followers = StationFollowers(old, platformId, mover);
                stage = "the hand-over, the followers and the merge or map change";
                // 4. the move
                if (old != null && old.Personal && target != null)
                {
                    res.MergeNote = Merge(old, target, platformId, mover, who);
                    res.Merged = true;
                    ScRegistry.WorldState.Membership[platformId] = target.Id;
                    Retire(old);
                }
                else if (target != null) ScPlayers.SetCommunity(platformId, target.Id);
                else
                {
                    // none (PerPlayer/PerParty): a fresh, empty personal record
                    ScRegistry.WorldState.Membership.Remove(platformId);
                    int fresh = EnsurePersonal(mover, platformId, who);
                    target = ScCommunities.ById(fresh);
                    if (target == null)
                    {
                        // could not be made here (off the main thread): they are in none until the next ask makes one
                        ScLog.Warn("community: no personal record could be made for " + who + " now - the next time the server asks, one is");
                    }
                }
                res.To = target != null ? target.Id : ScPlayers.NoCommunity;
                // 5. the new community's grudge against them ends - its members do not fight their own player (round MP6 (fix R-m5))
                if (target != null) res.Forgiven = ScRelations.Forgive(target.Id, platformId);
                // round MP7 (part AP): how the player joined is recorded (admin, ally with its link, party) - an admin-joined player is
                // never removed by an ally or party change; leaving by Leave or an admin's move opts them out of the community left
                ScJoins.Noted(platformId, old, target, reason);
                Tells(old, target, mover, who, leaving, reason, res);
                ScRegistry.SaveAtomic();
                ScLog.Out("community: " + who + " (" + platformId + ") moved from " + (old != null ? old.Label : "no community") + " to "
                          + (target != null ? target.Label : "no community") + " (" + reason + ")"
                          + (res.HandedOver > 0 ? "; " + res.HandedOver + " request(s) handed over" : "")
                          + (res.Withdrawn > 0 ? "; " + res.Withdrawn + " request(s) withdrawn" : "")
                          + (res.FavoursClosed > 0 ? "; " + res.FavoursClosed + " favor quest(s) closed in their journal" : "")
                          + (res.Followers > 0 ? "; " + res.Followers + " follower(s) went home" : "")
                          + (res.Merged ? "; merged: " + res.MergeNote : "")
                          + (res.Forgiven ? "; the new community's grudge against them dropped" : ""));
                res.Ok = true;
            }
            catch (Exception e)
            {
                // round MP7 (core review): m10 - each distinct failure logged (keyed by its site), and what already ran said
                ScLog.Once("mp7-move-" + e.GetType().Name + "-" + (e.TargetSite != null ? e.TargetSite.Name : "?"), e);
                res.Error = stage.Length == 0 ? "scActionFailed" : "the move failed after " + stage + " ran (see the log) - running it again finishes it";
            }
            return res;
        }

        /// <summary>
        /// Round MP7 (core review): m2 - the mover's live links to the old community end with the move, each the way a disconnect ends
        /// it: their enlisted enclave people go home (a founded community's: a personal record never enlists), their home link (waiting or
        /// holding) and linked-storage view close, and the server's backup of their member panel hold ends. Nothing for an
        /// offline mover (an enlistment ends with its enlister gone: ScEnclaveDeals.TickEnlisted; a view or a hold dies with the connection).
        /// </summary>
        private static void EndLinks(CommunityState old, EntityPlayer mover, string who)
        {
            if (mover == null) return;
            int eid = mover.entityId;
            if (!old.Personal)
            {
                foreach (EncPerson p in ScRegistry.EncPeople.ToArray())
                {
                    if (p == null || !p.Enlisted || p.EnlistedBy != eid) continue;
                    EntityAlive npc = ScEnclavePeople.OwnBody(p);
                    if (npc != null && !npc.IsDead() && ScEnclaveDeals.GoHome(npc, who + " left the community")) continue;
                    ScEnclaveDeals.EndEnlist(p, null, "scEncHeadsHome", who + " left the community");
                }
            }
            ScHomeLink.ForgetPlayer(eid);
            ScStorageServer.End(eid, "scStorageSessionEnded");
#if SC_V34
            ScPanelHold.ForgetPlayer(eid);
#endif
        }

        /// <summary>
        /// The tells of a move (decision 8's "tells", in each receiver's language): the mover - joined (and merged), left, or lost
        /// access; the old community's players - they left; the new community's players - they joined.
        /// </summary>
        private static void Tells(CommunityState old, CommunityState target, EntityPlayer mover, string who, bool leaving, string reason, MoveResult res)
        {
            if (mover != null)
            {
                if (leaving)
                {
                    if (old != null) ScCommunity.TellKey(mover, reason == ByLeave ? "scMp7Left" : "scMp7LostAccess", reason == ByLeave ? "" : "ui_denied", Whose(old));
                }
                else if (target != null)
                {
                    ScCommunity.TellKey(mover, "scMp7Joined", "", Whose(target));
                    if (res.Merged) ScCommunity.TellKey(mover, "scMp7Merged", "", Whose(target));
                    else if (old != null && !old.Personal && reason != ByAdmin) ScCommunity.TellKey(mover, "scMp7LostAccess", "", Whose(old));
                }
            }
            if (old != null && !old.Personal && ScCommunities.ById(old.Id) != null)
                using (ScRegistry.Use(old.Id)) ScPlayers.TellCommunityKey("scMp7LeftTell", "", who);
            if (target != null && !target.Personal)
            {
                EntityPlayer[] to = ScPlayers.Community(target.Id).ToArray();     // copied: a tell can re-enter code that reuses the list
                for (int i = 0; i < to.Length; i++)
                    if (to[i] != null && !ReferenceEquals(to[i], mover)) ScCommunity.TellKey(to[i], "scMp7JoinedTell", "", who);
            }
        }

        /// <summary>
        /// Decision 8, leaving a founded community (Move, before the map changes): the requests the mover accepted pass to another
        /// online player of it (ScEncRequests.HandOver: the quest leaves the mover's journal and goes to the heir's), else are
        /// withdrawn (no standing lost); the favors they hold stay its own (ReleaseFavours).
        /// </summary>
        private static void HandOver(CommunityState old, string pid, EntityPlayer mover, string who, MoveResult res)
        {
            bool moverIsHost = mover is EntityPlayerLocal;
            var taken = new List<EnclaveRequest>();
            foreach (EnclaveRequest r in old.EncRequests)
                if (r.IsAccepted && (string.Equals(r.AcceptedBy, pid, StringComparison.Ordinal) || (moverIsHost && r.AcceptedBy.Length == 0))) taken.Add(r);
            if (taken.Count > 0)
            {
                EntityPlayer heir = Heir(old, pid);
                using (ScRegistry.Use(old.Id))
                {
                    foreach (EnclaveRequest r in taken)
                    {
                        Enclave e = ScEnclaves.ById(r.EnclaveId);
                        if (heir != null && e != null && !e.Lost)
                        {
                            ScEncRequests.HandOver(r, mover, heir);
                            res.HandedOver++;
                            ScCommunity.TellKey(heir, "scMp7RequestYours", "", e.Name, who);
                            ScLog.Out("enclave request #" + r.Id + " (" + e.Name + "): " + who + " left " + old.Label + " - it passes to " + heir.PlayerDisplayName);
                        }
                        else
                        {
                            ScEncRequests.Close(r, ScEncRequests.Withdrawn, who + " left the community and nobody else of it is online to take it on");
                            res.Withdrawn++;
                        }
                    }
                }
            }
            res.FavoursClosed = ReleaseFavours(old, pid, mover);
        }

        /// <summary>The old community's player online who takes the mover's requests: its owner when online, else the first of its players online.</summary>
        private static EntityPlayer Heir(CommunityState cs, string moverId)
        {
            List<EntityPlayer> mine = ScPlayers.Community(cs.Id);
            EntityPlayer first = null;
            for (int i = 0; i < mine.Count; i++)
            {
                EntityPlayer p = mine[i];
                if (p == null) continue;
                string id = ScPlayers.IdOf(p);
                if (id.Length == 0 || string.Equals(id, moverId, StringComparison.Ordinal)) continue;
                if (IsOwner(cs, id, p.PlayerDisplayName)) return p;
                if (first == null) first = p;
            }
            return first;
        }

        /// <summary>
        /// Decision 8's "held favors", leaving a founded community: a favor is the community's (Prospect), and its quest sits in the
        /// taker's journal on their own machine; no server op starts a favor quest in another player's journal. The closest
        /// safe behavior (MP7, decision needed) - the favor stays the community's (a done favor stays done), the mover's journal
        /// copy closes (removed), and when no other online player of the community holds its quest it is no longer "taken on"
        /// (Started false), so any of its players can take it up again at the survivor. An offline mover's copy closes with their
        /// next journal report (ScQuestNet.StaleFavours: not their new community's favor). Returns the copies closed.
        /// </summary>
        private static int ReleaseFavours(CommunityState old, string pid, EntityPlayer mover)
        {
            // round MP7 (core, decided at the merge - kept as built): decision 8 passes "held favors" to another online player, but a favor quest can only be started by
            // the taker's own dialog (no quest op starts one remotely) - so the favor stays the community's and is free to take on again
            if (old.Prospects.Count == 0) return 0;
            int n = 0;
            bool host = mover is EntityPlayerLocal;
            foreach (Prospect pr in new List<Prospect>(old.Prospects.Values))
            {
                if (pr == null || pr.Favour == FavourKind.None) continue;
                // round MP7 (core review): m4, m8 - an offline mover: their last report this session says (1 held, 0 not), or nobody knows (-1)
                int holds = mover == null ? ScQuestNet.ReportedFavour(pid, pr.Id)
                          : (host ? ScQuests.FindFavour(mover, pr.Id) != null : ScQuestNet.ReportHoldsFavour(mover, pr.Id)) ? 1 : 0;
                if (holds == 0) continue;
                if (mover != null)
                {
                    if (host) ScQuests.CloseFavour(mover, pr.Id, false);
                    else ScQuestNet.CloseFavourFor(mover, pr.Id, false);
                    n++;
                }
                // no longer "taken on" when nobody else of the community online holds it - an offline mover's copy closes at their next
                // report (ScQuestNet.StaleFavours); another offline holder's report takes it on again (ScQuestNet.FavoursHeld)
                if (!pr.Done && pr.Started && !ScQuestNet.AnyOtherHoldsFavour(old.Id, pr.Id, mover)) pr.Started = false;
            }
            return n;
        }

        /// <summary>
        /// The mover's followers in the old community (Following or PendingStation with their LeaderId): they stay its members and go
        /// home - a loaded one stationed at her post now, an unloaded one resident (the ticker stations her when she loads). The
        /// console's round MP6 (part G) rule, moved here. Returns how many.
        /// </summary>
        private static int StationFollowers(CommunityState old, string pid, EntityPlayer mover)
        {
            var theirs = new List<Member>();
            foreach (Member m in old.Members.Values)
                if ((m.Status == MemberStatus.Following || m.Status == MemberStatus.PendingStation) && string.Equals(m.LeaderId, pid, StringComparison.Ordinal)) theirs.Add(m);
            foreach (Member m in theirs)
                using (ScRegistry.Use(m))
                {
                    EntityAlive npc = ScCommunity.FindNpc(m.Id);
                    if (npc != null) ScCommunity.Station(m, npc, true, mover);
                    else m.Status = MemberStatus.Resident;
                    m.LeaderId = "";
                }
            return theirs.Count;
        }

        // ------------------------------------------------------------------ the merge ----

        /// <summary>A favor's progress: done 2, taken on 1, offered 0 (the merge keeps the one further along).</summary>
        private static int Rank(Prospect p) { return p == null ? -1 : p.Done ? 2 : p.Started ? 1 : 0; }

        /// <summary>
        /// Decision 5: a personal record joins a community - everything it built becomes the community's, the better value winning
        /// (Move calls it before the map changes, then retires the record). In order:
        /// - favors (prospects) move; where both court the same survivor the one further along stays (Rank; the community's on a
        ///   tie) and the other's quest closes, with a tell; the "held by a remote report" marks follow (ScQuestNet.RemapFavours);
        /// - enclave requests move; where both hold one from the same group the one further along stays (accepted over offered; the
        ///   community's on a tie) and the other is withdrawn (no standing lost), with a tell;
        /// - radio contacts move (and, unreachable by construction, members and outposts, with a warning);
        /// - the stockpile adds up (what allies sent the player waits there: the held rewards);
        /// - every relation row (enclave, infestation, band) the record has a saved value in: enclave standing max, known OR (the
        ///   earlier known day), the community's hostility kept (a war the community earned never ends by a player joining; the
        ///   record's own hostility is dropped), thanks owed kept; infestation known OR; band known OR and trust max; the record's own
        ///   hostility cvars cleared to 0 and its rows dropped. The community's request, gift, barter, call and reveal clocks stay its
        ///   own;
        /// - wild survivors' grudges against the joining player are dropped on the live bodies (the community's relation wins).
        /// Markers follow the records (each community's set is built from its records). Returns a one-line summary.
        /// </summary>
        internal static string Merge(CommunityState from, CommunityState into, string pid, EntityPlayer mover, string who)
        {
            int favors = 0, favorsKept = 0, requests = 0, requestsKept = 0, encs = 0, infs = 0, bands = 0, forgiven = 0, failed = 0;
            // favors (before the relation rows: a withdrawal below writes the record's row, which the rows step then drops).
            // Round MP7 (core review): m10 - each record's step on its own: one that throws is logged and skipped, never splits the merge
            foreach (Prospect pp in new List<Prospect>(from.Prospects.Values))
            {
                try { if (MergeFavour(from, into, pp, pid, mover, who)) favorsKept++; else favors++; }
                catch (Exception ex)
                {
                    failed++;
                    ScLog.Once("mp7-merge-favour-" + ex.GetType().Name, ex);
                    // never lost with the record: kept by the community unless it holds one under that key
                    from.Prospects.Remove(pp.Id);
                    if (!into.Prospects.ContainsKey(pp.Id)) into.Prospects[pp.Id] = pp;
                }
            }
            ScQuestNet.RemapFavours(from.Id, into.Id);
            // enclave requests: one per (community, enclave)
            foreach (EnclaveRequest rq in new List<EnclaveRequest>(from.EncRequests))
            {
                try { if (MergeRequest(from, into, rq, mover, who)) requestsKept++; else requests++; }
                catch (Exception ex)
                {
                    failed++;
                    ScLog.Once("mp7-merge-request-" + ex.GetType().Name, ex);
                    // never lost with the record: moved as it is (the community may hold two from that group until one ends)
                    if (from.EncRequests.Remove(rq) && !into.EncRequests.Contains(rq)) into.EncRequests.Add(rq);
                }
            }
            try
            {
                // radio contacts (a personal record has no radio: none, normally), and what a personal record never has
                foreach (Contact ct in new List<Contact>(from.Contacts)) { from.Contacts.Remove(ct); into.Contacts.Add(ct); }
                if (from.Members.Count > 0 || from.Outposts.Count > 0)
                {
                    ScLog.Warn("community merge: " + from.Label + " had " + from.Members.Count + " member(s) and " + from.Outposts.Count + " outpost(s) - moved to "
                               + into.Label + " (a personal record never should; their bodies' sc_member cvar follows at their next relink)");
                    foreach (Member m in new List<Member>(from.Members.Values)) { from.Members.Remove(m.Id); into.Members[m.Id] = m; }
                    foreach (Outpost o in new List<Outpost>(from.Outposts)) { from.Outposts.Remove(o); into.Outposts.Add(o); }
                    ScOutposts.OnRegistryChanged();
                }
                // the stockpile adds up (allies' bundles that waited there)
                for (int i = 0; i < ScNames.ResourceCount; i++)
                    if (from.Community.Stock[i] > 0) { into.Community.Add((Resource)i, from.Community.Stock[i]); from.Community.Stock[i] = 0; }
            }
            catch (Exception ex) { failed++; ScLog.Once("mp7-merge-stock-" + ex.GetType().Name, ex); }
            // relation rows: the better value wins
            foreach (Enclave e in ScRegistry.Enclaves)
            {
                EncRel pr = e.Rels.Peek(from.Id);
                if (pr == null || !pr.HasSaved) continue;
                EncRel tr = e.Rels.Get(into.Id);
                tr.Standing = Math.Max(tr.Standing, pr.Standing);
                if (pr.Known)
                {
                    if (!tr.Known) { tr.Known = true; tr.KnownDay = pr.KnownDay; }
                    else if (pr.KnownDay >= 0 && (tr.KnownDay < 0 || pr.KnownDay < tr.KnownDay)) tr.KnownDay = pr.KnownDay;
                }
                // round MP7 (core, decided at the merge): the community's hostility stays as it is - E2 made it permanent, and a war the
                // community earned must not end because a player joined; the record's own hostility is dropped with the record (Retire)
                if (tr.Thanks.Length == 0 && pr.Thanks.Length > 0) tr.Thanks = pr.Thanks;
                encs++;
            }
            foreach (Infestation inf in ScRegistry.Infestations)
            {
                InfRel pr = inf.Rels.Peek(from.Id);
                if (pr == null || !pr.HasSaved) continue;
                InfRel tr = inf.Rels.Get(into.Id);
                if (pr.Known)
                {
                    if (!tr.Known) { tr.Known = true; tr.KnownDay = pr.KnownDay; }
                    else if (pr.KnownDay >= 0 && (tr.KnownDay < 0 || pr.KnownDay < tr.KnownDay)) tr.KnownDay = pr.KnownDay;
                }
                infs++;
            }
            foreach (Band b in ScRegistry.Bands)
            {
                BandRel pr = b.Rels.Peek(from.Id);
                if (pr == null || !pr.HasSaved) continue;
                BandRel tr = b.Rels.Get(into.Id);
                if (pr.Known)
                {
                    if (!tr.Known) { tr.Known = true; tr.KnownDay = pr.KnownDay; }
                    else if (pr.KnownDay >= 0 && (tr.KnownDay < 0 || pr.KnownDay < tr.KnownDay)) tr.KnownDay = pr.KnownDay;
                }
                tr.Trust = Math.Max(tr.Trust, pr.Trust);
                bands++;
            }
            // the record's own rows and hostility cvars go with it (Retire drops them)
            // wild survivors' grudges against the joining player (ScTrust's per-player cvar) - the community's relation wins; a body not
            // loaded keeps its cvar (only live bodies can be changed)
            // round MP7 (core, decided at the merge - kept as built): ScTrust's grudge is per player and lives on the survivor's body - only loaded bodies can drop it
            World w = ScCommunity.World;
            if (mover != null && w != null && w.Entities != null)
            {
                List<Entity> list = w.Entities.list;
                for (int i = 0; i < list.Count; i++)
                {
                    EntityAlive a = list[i] as EntityAlive;
                    if (a == null || a is EntityPlayer || !ScTrust.Subject(a) || !ScTrust.IsHostileTo(a, mover)) continue;
                    if (ScTrust.Forgive(a, mover)) forgiven++;
                }
            }
            return favors + " favor(s) and " + requests + " request(s) moved" + (favorsKept + requestsKept > 0 ? " (" + favorsKept + " favor(s) and " + requestsKept + " request(s) clashed: the one further along kept)" : "")
                   + ", " + encs + " enclave, " + infs + " infestation and " + bands + " band relation(s) merged"
                   + (forgiven > 0 ? ", " + forgiven + " wild survivor grudge(s) dropped" : "")
                   + (failed > 0 ? "; " + failed + " step(s) failed - what they held was moved as it was (see the log)" : "");
        }

        /// <summary>
        /// Round MP7 (core review): Merge's step for one favor of the record (the rule as before). m6 - an enclave person's favor also
        /// clashes with the community's favor for the same person kept under another key (a parked one, an older body's). m8 - with the
        /// joining player offline, a dropped favor's quest closes at their next journal report. True on a clash.
        /// </summary>
        private static bool MergeFavour(CommunityState from, CommunityState into, Prospect pp, string pid, EntityPlayer mover, string who)
        {
            from.Prospects.Remove(pp.Id);
            Prospect tp;
            if (!into.Prospects.TryGetValue(pp.Id, out tp)) tp = null;
            if (tp == null && pp.EncPersonId > 0)
                foreach (Prospect x in into.Prospects.Values)
                    if (x != null && x.EncPersonId == pp.EncPersonId) { tp = x; break; }
            if (tp == null)
            {
                into.Prospects[pp.Id] = pp;
                return false;
            }
            if (Rank(pp) > Rank(tp))
            {
                // the record's is further along: it replaces the community's (under the record's key), whose holders' quests close
                if (tp.Id != pp.Id) into.Prospects.Remove(tp.Id);
                into.Prospects[pp.Id] = pp;
                ScTrust.CloseFavourForCommunity(tp.Id, false, mover, into.Id);
                using (ScRegistry.Use(into.Id)) ScPlayers.TellCommunityKey("scMp7FavourReplaced", "", pp.Name, who);
            }
            else
            {
                // the community's is as far along or further: the record's goes, and the joining player's quest with it
                if (mover is EntityPlayerLocal) ScQuests.CloseFavour(mover, pp.Id, false);
                else if (mover != null) ScQuestNet.CloseFavourFor(mover, pp.Id, false);
                else ScQuestNet.CloseFavourAtNextReport(pid, pp.Id);
                if (mover != null) ScCommunity.TellKey(mover, "scMp7FavourDropped", "", pp.Name);
            }
            return true;
        }

        /// <summary>
        /// Round MP7 (core review): Merge's step for one request of the record (one per community and enclave). The one further along
        /// stays: accepted over offered, and (n7) of two accepted ones of the same kind the one at the later stage; the community's on a
        /// tie. The other is withdrawn (no standing lost) with a tell. True on a clash.
        /// </summary>
        private static bool MergeRequest(CommunityState from, CommunityState into, EnclaveRequest rq, EntityPlayer mover, string who)
        {
            EnclaveRequest tq = null;
            foreach (EnclaveRequest x in into.EncRequests) if (x.EnclaveId == rq.EnclaveId) { tq = x; break; }
            if (tq == null)
            {
                from.EncRequests.Remove(rq);
                into.EncRequests.Add(rq);
                return false;
            }
            Enclave e = ScEnclaves.ById(rq.EnclaveId);
            string ename = e != null ? e.Name : "#" + rq.EnclaveId;
            bool recordAhead = rq.IsAccepted && (!tq.IsAccepted || (rq.Kind == tq.Kind && rq.Stage > tq.Stage));
            if (recordAhead)
            {
                using (ScRegistry.Use(into.Id))
                {
                    ScEncRequests.Close(tq, ScEncRequests.Withdrawn, who + " joined with a request from the same group further along");
                    ScPlayers.TellCommunityKey("scMp7RequestReplaced", "", ename, who);
                }
                from.EncRequests.Remove(rq);
                into.EncRequests.Add(rq);
            }
            else
            {
                using (ScRegistry.Use(from.Id)) ScEncRequests.Close(rq, ScEncRequests.Withdrawn, who + " joined a community that already has a request from this group");
                if (mover != null) ScCommunity.TellKey(mover, "scMp7RequestDropped", "", ename);
            }
            return true;
        }

        /// <summary>
        /// A personal record that was merged retires: its relation rows go (each enclave's hostile cvar for it cleared to 0 on the live
        /// bodies), it leaves the list and its id stays as a one-line tombstone ([world] retired=N: never run, never mirrored, never
        /// reused). Community 0 always exists: it is emptied instead (ScCommunities.ResetZero), unowned, for the next player with no
        /// record to claim. The caller saves.
        /// </summary>
        internal static void Retire(CommunityState cs)
        {
            if (cs == null) return;
            int cid = cs.Id;
            foreach (Enclave e in ScRegistry.Enclaves)
            {
                EncRel r = e.Rels.Peek(cid);
                if (r != null && r.TurnedHostile) ScEnclavePeople.ClearHostileCvar(e, cid);
                e.Rels.Drop(cid);
            }
            foreach (Infestation inf in ScRegistry.Infestations) inf.Rels.Drop(cid);
            foreach (Band b in ScRegistry.Bands) b.Rels.Drop(cid);
            ScWorldState ws = ScRegistry.WorldState;
            var gone = new List<string>();
            foreach (KeyValuePair<string, int> kv in ws.Membership) if (kv.Value == cid) gone.Add(kv.Key);
            foreach (string id in gone) ws.Membership.Remove(id);
            gone.Clear();
            foreach (KeyValuePair<string, int> kv in ws.ConsoleUse) if (kv.Value == cid) gone.Add(kv.Key);
            foreach (string id in gone) ws.ConsoleUse.Remove(id);
            if (cid == ScPlayers.SharedCommunity) ScCommunities.ResetZero(true);
            else
            {
                ScCommunities.Remove(cs);
                ws.Retired.Add(cid);
            }
            try { ScEnclavePeople.RebuildHostile(); }                  // the faction lookup without its rows
            catch (Exception e) { ScLog.Once("mp7-retire-hostile", e); }
            ScLog.Out("community: " + cs.Label + (cid == ScPlayers.SharedCommunity ? " emptied (merged into another community; free for the next player with no community)"
                                                                                    : " retired (merged into another community; its id stays taken)"));
        }

        // ------------------------------------------------------------------ admin tools ----

        /// <summary>
        /// sc community owner &lt;id&gt; &lt;player&gt; (decision 10's hand-over): the player owns the community from now on, and is moved into it
        /// first when they are elsewhere (an owner is always in their own community; their personal record merges). Not for a
        /// personal record. The old owner stays a player of it. Returns the console's answer; ok when done. Saves.
        /// </summary>
        internal static string SetOwner(CommunityState cs, string pid, string who, out bool ok)
        {
            ok = false;
            if (cs == null) return "there is no such community";
            // round MP7 (core review): n8 - community 0 emptied by a merge (personal, unowned) can be given to a player as a made community
            bool freeZero = cs.Id == ScPlayers.SharedCommunity && cs.Personal && string.IsNullOrEmpty(cs.Community.OwnerId) && string.IsNullOrEmpty(cs.Community.Owner);
            if (cs.Personal && !freeZero) return cs.Label + " is a player's own relations - it has no owner to hand over";
            if (freeZero) cs.Personal = false;
            if (string.IsNullOrEmpty(who) || who == pid) who = NameOf(pid);          // round MP7 (core review): n2
            int was = ScPlayers.CommunityOfId(pid);
            string moved = "";
            if (was != cs.Id)
            {
                MoveResult r = Move(pid, cs.Id, ByAdmin, who);
                if (!r.Ok)
                {
                    if (freeZero) cs.Personal = true;   // round MP7 (core review): n8 - as it was
                    return "refused: " + ScActions.Localize(r.Error);
                }
                moved = " (moved into it from " + (ScCommunities.ById(r.From) != null ? ScCommunities.ById(r.From).Label : "no community") + (r.Merged ? ", their own relations merged" : "") + ")";
            }
            string before = string.IsNullOrEmpty(cs.Community.Owner) ? (string.IsNullOrEmpty(cs.Community.OwnerId) ? "nobody" : cs.Community.OwnerId) : cs.Community.Owner;
            // the old owner stays a player of it: listed now when only the owner rule kept them in (PerPlayer/PerParty)
            string oldOwner = cs.Community.OwnerId;
            if (Separate && !string.IsNullOrEmpty(oldOwner) && !ScRegistry.WorldState.Membership.ContainsKey(oldOwner)) ScRegistry.WorldState.Membership[oldOwner] = cs.Id;
            EntityPlayer online = ScPlayers.ById(pid);
            cs.Community.OwnerId = pid;
            cs.Community.Owner = online != null ? online.PlayerDisplayName ?? "" : who;
            if (Separate) ScRegistry.WorldState.Membership[pid] = cs.Id;
            ScRegistry.SaveAtomic();
            if (online != null) ScCommunity.TellKey(online, "scMp7OwnerYou", "", Whose(cs));
            using (ScRegistry.Use(cs.Id)) ScPlayers.TellCommunityKey("scMp7OwnerTell", "", cs.Community.Owner);
            ScLog.Out("community: " + cs.Label + " handed over from " + before + " to " + who + " (" + pid + ") by an admin" + moved);
            ok = true;
            return cs.Label + " now belongs to " + who + " (was " + before + ")" + moved;
        }

        /// <summary>
        /// sc community mode &lt;PerPlayer|PerParty&gt; confirm (decision 2): once, on a save that loaded with no mode= (a save from before
        /// MP7, Shared). Community 0 and every made community keep their owners (listed in the map: an owner is always in their own)
        /// and their mapped players; an accepted request whose acceptor is in none of them now passes to that community's owner (their
        /// journal gets it with their next report); every other player gets a personal record at their next spawn - now, for those
        /// online. Round MP7 (part AP): in PerPlayer an owner's land-claim allies join that community first (ScAllies, before the
        /// requests pass). Returns the console's lines; ok when switched. Saves.
        /// </summary>
        internal static List<string> SwitchMode(ScCommunityMode to, out bool ok)
        {
            ok = false;
            var lines = new List<string>();
            ScWorldState ws = ScRegistry.WorldState;
            if (to == ScCommunityMode.Shared) { lines.Add("refused: the switch goes from Shared to PerPlayer or PerParty"); return lines; }
            if (!ws.ModeLocked || ws.ModeWritten)
            {
                lines.Add("refused: only a save that loaded with no mode= (one from before MP7, Shared) can be switched - this one is "
                          + (ws.ModeLocked ? ModeName(ws.Mode) + " for good" : "not fixed yet: set CommunityMode in the config instead"));
                return lines;
            }
            // round MP7 (core review): m4 - each online player's community before the switch (Shared: unlisted is community 0), so the
            // players it moves out get Move's leaving half below
            var before = new List<KeyValuePair<EntityPlayer, int>>();
            foreach (EntityPlayer p in ScPlayers.All.ToArray())
            {
                if (p == null) continue;
                string id = ScPlayers.IdOf(p);
                if (id.Length > 0) before.Add(new KeyValuePair<EntityPlayer, int>(p, ScPlayers.CommunityOfId(id)));
            }
            ws.Mode = to;
            ws.ModeWritten = true;
            int owners = 0, handed = 0, released = 0, freed = 0;
            foreach (CommunityState cs in ScCommunities.All)
            {
                Community c = cs.Community;
                if (string.IsNullOrEmpty(c.OwnerId) && !string.IsNullOrEmpty(c.Owner))
                {
                    // a save from before MP0 knows its owner by name: their id now when they are online
                    foreach (EntityPlayer p in ScPlayers.All)
                        if (p != null && string.Equals(p.PlayerDisplayName, c.Owner, StringComparison.Ordinal)) { string id = ScPlayers.IdOf(p); if (id.Length > 0) c.OwnerId = id; break; }
                }
                if (!string.IsNullOrEmpty(c.OwnerId))
                {
                    if (!ws.Membership.ContainsKey(c.OwnerId)) { ws.Membership[c.OwnerId] = cs.Id; owners++; }
                }
                else if (IsFounded(cs) || !HoldsNothing(cs))     // round MP7 (core review): m5 - an unowned community 0 that holds something too
                    lines.Add(cs.Label + " has no owner its players can be told by: sc community owner " + cs.Id + " <player> gives it one (its unlisted players now have none)");
            }
            // round MP7 (part AP): PerPlayer - the allies reconcile runs here at once: once the owners are listed, before the requests
            // pass and the personal records below, so an owner's land-claim ally stays in the community (offline too) with the
            // requests they accepted, and is never given a personal record only to have it merged straight back
            ScAllies.OnModeSwitched();
            foreach (CommunityState cs in ScCommunities.All)
            {
                string owner = cs.Community.OwnerId;
                if (string.IsNullOrEmpty(owner)) continue;
                foreach (EnclaveRequest r in cs.EncRequests.ToArray())
                {
                    if (!r.IsAccepted) continue;
                    string by = r.AcceptedBy;
                    EntityPlayer acceptor;
                    if (by.Length == 0)
                    {
                        // the host's own (a save from before MP4): theirs while they are of it
                        EntityPlayerLocal lp = ScCommunity.LocalPlayer;
                        if (lp == null || ScPlayers.CommunityOfId(ScPlayers.IdOf(lp)) == cs.Id) continue;
                        acceptor = lp;
                    }
                    else
                    {
                        if (ScPlayers.CommunityOfId(by) == cs.Id) continue;
                        acceptor = ScPlayers.ById(by);
                    }
                    EntityPlayer ownerOnline = ScPlayers.ById(owner);
                    if (ownerOnline != null)
                    {
                        // round MP7 (core review): m4 - Move's hand-over: the acceptor's quest closes, the owner's starts (and is told)
                        using (ScRegistry.Use(cs.Id)) ScEncRequests.HandOver(r, acceptor, ownerOnline);
                        Enclave e = ScEnclaves.ById(r.EnclaveId);
                        if (e != null) ScCommunity.TellKey(ownerOnline, "scMp7RequestYours", "", e.Name, acceptor != null ? acceptor.PlayerDisplayName : NameOf(by));
                    }
                    else
                    {
                        // the owner away: the record names them, their journal gets it with their next report
                        r.AcceptedBy = owner;
                        r.QuestCode = 0;
                    }
                    handed++;
                }
            }
            // round MP7 (core review): m4 - each online player it moves out gets Move's leaving half: talk holds and shifts end, their links
            // end (EndLinks), their favor quests of the old community close (ReleaseFavours), their followers go home, they are told
            int made = 0;
            var lost = new HashSet<int> { ScPlayers.SharedCommunity };
            foreach (KeyValuePair<EntityPlayer, int> kv in before)
            {
                EntityPlayer p = kv.Key;
                string id = ScPlayers.IdOf(p);
                if (id.Length == 0 || ScPlayers.CommunityOfId(id) != ScPlayers.NoCommunity) continue;      // still in it (an owner, a listed player)
                CommunityState old = ScCommunities.ById(kv.Value);
#if SC_V34
                ScWork.ForgetPlayer(p.entityId);
#endif
                if (old != null)
                {
                    lost.Add(old.Id);
                    EndLinks(old, p, p.PlayerDisplayName);
                    released += ReleaseFavours(old, id, p);
                    StationFollowers(old, id, p);
                }
                if (EnsurePersonal(p, id, null) != ScPlayers.NoCommunity) made++;
                if (old != null) ScCommunity.TellKey(p, "scMp7LostAccess", "ui_denied", Whose(old));
            }
            // and the favors of the communities that lost players that nobody of them online holds now (an offline player's): free to
            // take on again (a holder's next report takes it on again: ScQuestNet.FavoursHeld)
            foreach (int cid in lost)
            {
                CommunityState cs = ScCommunities.ById(cid);
                if (cs == null) continue;
                foreach (Prospect pr in cs.Prospects.Values)
                    if (pr != null && !pr.Done && pr.Started && pr.Favour != FavourKind.None && !ScQuestNet.AnyOtherHoldsFavour(cid, pr.Id, null)) { pr.Started = false; freed++; }
            }
            ScRegistry.SaveAtomic();
            ScLog.Out("community mode switched to " + ModeName(to) + " by an admin (a save from before MP7): " + owners + " owner(s) listed, "
                      + handed + " accepted request(s) passed to their community's owner, " + made + " player(s) online given their own relations, "
                      + released + " favor quest(s) closed in their journals, " + freed + " favor(s) free to take on again");
            lines.Insert(0, "this save is " + ModeName(to) + " from now on: " + owners + " owner(s) kept in their communities, " + handed
                         + " request(s) whose acceptor is in none of them passed to its owner, " + made + " player(s) online given their own relations"
                         + " (every other player gets theirs at their next spawn)");
            ok = true;
            return lines;
        }
    }

    /// <summary>
    /// Round MP7 (core): who may use the Owner ops (dismiss a member, disband an outpost, move the home, rationing) in each mode -
    /// ScActions.WhyNot's Owner rule. Shared: SharedOwnerOnly as before (anyone, or the owner and admins); PerPlayer: the owner and
    /// admins (decision 6); PerParty: the owner, admins and the leader of a party holding one of the community's players while the
    /// owner is offline (decision 7; PartyLeaderMayAct, round MP7 (part AP)).
    /// </summary>
    public static class ScOwnerRule
    {
        /// <summary>The player (of community cs, the sender's own) may use an Owner op there.</summary>
        public static bool May(EntityPlayer p, CommunityState cs)
        {
            if (p == null || cs == null) return false;
            ScCommunityMode mode = ScMembership.Separate ? ScMembership.Mode : ScCommunityMode.Shared;
            switch (mode)
            {
                case ScCommunityMode.PerPlayer:
                    return ScActions.IsOwnerOrAdmin(p);
                case ScCommunityMode.PerParty:
                    if (ScActions.IsOwnerOrAdmin(p)) return true;
                    return ScPlayers.ById(cs.Community.OwnerId) == null && PartyLeaderMayAct(p, cs.Id);
                default:
                    return !ScConfig.SharedOwnerOnly || ScActions.IsOwnerOrAdmin(p);
            }
        }

        /// <summary>
        /// Round MP7 (part AP): PerParty's helper rule (decision 7) - true when player leads a vanilla party holding one of community
        /// cid's players, is of cid themselves (the server's map: the sender must be in this community) and cid's owner is offline
        /// (checked here too, so part U's role= can ask it as is). Founded communities only; false in every other mode.
        /// </summary>
        public static bool PartyLeaderMayAct(EntityPlayer player, int cid)
        {
            try
            {
                if (player == null || !ScMembership.Separate || ScMembership.Mode != ScCommunityMode.PerParty) return false;
                CommunityState cs = ScCommunities.ById(cid);
                if (cs == null || cs.Personal) return false;
                if (ScPlayers.CommunityOf(player) != cid) return false;
                if (ScParties.OwnerOnline(cs)) return false;
                Party party = player.Party;
                if (party == null || party.MemberList == null || !ReferenceEquals(party.Leader, player)) return false;
                List<EntityPlayer> members = party.MemberList;
                for (int i = 0; i < members.Count; i++)
                    if (members[i] != null && ScPlayers.CommunityOfId(ScPlayers.IdOf(members[i])) == cid) return true;
                return false;
            }
            catch (Exception e)
            {
                ScLog.Once("mp7-party-leader", e);
                return false;
            }
        }
    }
}
