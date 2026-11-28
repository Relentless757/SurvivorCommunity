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
        // round db1: the disband op, and the reason a disband's members leave with
        /// <summary>Round db1: the owner (or an admin) disbands their community (DisbandAction).</summary>
        public const string OpDisband = "community.disband";
        /// <summary>
        /// Round db1: ScCommunity.Leave's reason for a member a disband sends back to the wild - Leave tells the community nothing for it
        /// (Disband sends one summary tell, not one per member); the log names it.
        /// </summary>
        public const string DisbandLeaveKey = "scDb1MemberLeft";

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
                ScParties.OnRegistryLoaded();           // round MP7 (review AP/F): m5 - the mixed-party tells of a new session
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
                    // round MP7 (review AP/F): m3 - an admin-made community (PerPlayer/PerParty) starts as a founding does: its owner's
                    // allies and party in no community join it (nothing in Shared)
                    ScJoins.OwnerHooks(cs, sender);
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
            Retire(cs, false);
        }

        // round db1: Retire with its log line's reason - the one-argument form above keeps every old caller (and the harnesses) as they were
        /// <summary>Round db1: Retire, and disbanded says so in its log line (Disband retires a founded community, not a merged record).</summary>
        internal static void Retire(CommunityState cs, bool disbanded)
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
            // round db1: a disbanded community's line says so
            string how = disbanded ? "disbanded" : "merged into another community";
            ScLog.Out("community: " + cs.Label + (cid == ScPlayers.SharedCommunity ? " emptied (" + how + "; free for the next player with no community)"
                                                                                    : " retired (" + how + "; its id stays taken)"));
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
            ScJoins.OwnerSet(pid);                      // round MP7 (review AP/F): m2 - an owner has no join record (a stale "ally" one goes)
            ScRegistry.SaveAtomic();
            if (online != null) ScCommunity.TellKey(online, "scMp7OwnerYou", "", Whose(cs));
            using (ScRegistry.Use(cs.Id)) ScPlayers.TellCommunityKey("scMp7OwnerTell", "", cs.Community.Owner);
            ScLog.Out("community: " + cs.Label + " handed over from " + before + " to " + who + " (" + pid + ") by an admin" + moved);
            // round MP7 (review AP/F): m3 - the new owner's allies (PerPlayer) and party (PerParty) in no community join it now, as at a
            // founding (not at their next spawn)
            ScJoins.OwnerHooks(cs, online);
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
                // round MP7 (review AP/F): m1 - skipped only when still in the same community (an owner, a listed player, an owner's
                // ally the reconcile kept in it): one the allies reconcile moved into another community gets the old one's leaving half
                // too (its Move saw no old community: in a Separate mode they were already in none), told and given a record only when
                // in none now
                int now = ScPlayers.CommunityOfId(id);
                if (id.Length == 0 || now == kv.Value) continue;
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
                if (now != ScPlayers.NoCommunity) continue;     // in another community now: Move told them they joined it
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

        // ------------------------------------------------------------------ disbanding (round db1) ----

        // round db1: 2026-11-28 (the MP7 design's "Addendum 2026-11-28: disbanding a community"; the user's three decisions are the spec):
        // the owner (community.disband, the window's players panel) or an admin (sc community disband <id> confirm) disbands a
        // community. Its members go back to the wild through the dismissal path (ScCommunity.Leave), its outposts through theirs
        // (ScOutposts.Disband), its stockpile is lost, its storage boxes stay as they are (linked storage is a view of the home zone's
        // boxes, so nothing in them moves), its home goes with the record. Its players keep what it knew: in PerPlayer/PerParty each
        // gets their own relations with a copy of its relations; in Shared a community 1+ is merged into community 0, and community 0
        // resets in place. The record retires (community 0 is never retired: Retire empties it).

        /// <summary>Round db1: what Disband did (the console, the op's reply, the log).</summary>
        public sealed class DisbandResult
        {
            public bool Ok;
            /// <summary>Why not (a localization key: ScActions.Localize gives the console its words).</summary>
            public string Error = "";
            public int Cid = ScPlayers.NoCommunity;
            public string Label = "";
            /// <summary>"own relations" (PerPlayer/PerParty), "merged into community 0" (Shared 1+), "reset in place" (Shared community 0).</summary>
            public string How = "";
            /// <summary>Members back to the wild, and those still in it when it stopped (they could not leave: run it again).</summary>
            public int Members, MembersLeft;
            /// <summary>Outposts abandoned, and those still held when it stopped.</summary>
            public int Outposts, OutpostsLeft;
            /// <summary>Stockpile units lost.</summary>
            public int StockLost;
            /// <summary>Its players (owner, listed players, those online in it), those online, and the personal records made for them.</summary>
            public int Players, Online, Records;
            /// <summary>Relation rows copied to each record (PerPlayer/PerParty), scouting marks kept, radio contacts kept.</summary>
            public int Enclaves, Infestations, Bands, Scouts, Contacts;
            /// <summary>
            /// Accepted requests passed to their acceptor's record (Shared community 0: kept), accepted ones withdrawn (no acceptor of
            /// it), offers closed; favors passed on (or kept), dropped (nobody to take them), and other holders' quests closed.
            /// </summary>
            public int RequestsMoved, RequestsWithdrawn, RequestsClosed, FavoursMoved, FavoursDropped, QuestsClosed;
            /// <summary>Shared 1+: Merge's summary.</summary>
            public string MergeNote = "";
        }

        /// <summary>
        /// Round db1: why community cs cannot be disbanded now (a localization key), null when it can - a player's own relations
        /// (scNoCommunity), a community not started (scDb1NotStarted), an enclave siege of its open, called or live (scDb1Siege), its
        /// base attack being fought, live or by its defenders alone (scDb1Attack), one of its members down (scDb1Downed: a downed
        /// member cannot leave). Any mode; reads no scope.
        /// </summary>
        internal static string WhyNotDisband(CommunityState cs)
        {
            if (cs == null || ScCommunities.ById(cs.Id) != cs) return "scActionDenied";
            if (cs.Personal && Separate) return "scNoCommunity";
            if (!IsFounded(cs)) return "scDb1NotStarted";
            foreach (EnclaveRequest r in cs.EncRequests) if (r != null && r.Kind == EnclaveRequest.KindSiege) return "scDb1Siege";
            int attack = cs.Community.AttackState;
            if (attack == ScBaseAttack.StateLive || attack == ScBaseAttack.StateAway) return "scDb1Attack";
#if SC_V34
            foreach (Member m in cs.Members.Values) if (ScDowned.MemberDown(m)) return "scDb1Downed";
#endif
            return null;
        }

        /// <summary>
        /// Round db1, the user's decision 1: the player (platform id, display name) may disband community cs - its owner, or an admin
        /// (the caller's ScActions.IsAdmin). PerParty's helper (the party leader while the owner is away) may not; in a community with
        /// no owner only an admin may. Server only: the op's check (a client only shows or hides the button).
        /// </summary>
        internal static bool MayDisband(CommunityState cs, string platformId, string displayName, bool admin)
        {
            if (cs == null) return false;
            return admin || IsOwner(cs, platformId, displayName);
        }

        /// <summary>
        /// community.disband: the sender's community is disbanded (the window's Disband community, its second press). Community access
        /// (a player's own relations are refused by ScActions.WhyNot: scNoCommunity), then the owner or an admin only (MayDisband: never
        /// the Owner rule, which lets PerParty's helper in), then Disband's own refusals.
        /// </summary>
        [ScAction(OpDisband, ScActionAccess.Community)]
        private static void DisbandAction(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            CommunityState cs = ScCommunities.ById(ScPlayers.CommunityOf(sender));
            if (cs == null) { reply.Fail("scActionDenied"); return; }
            string why = !MayDisband(cs, ScPlayers.IdOf(sender), sender.PlayerDisplayName, ScActions.IsAdmin(sender)) ? "scDb1OwnerOnly" : null;
            DisbandResult r = why == null ? Disband(cs, sender.PlayerDisplayName ?? "") : null;
            if (r != null && !r.Ok) why = r.Error;
            if (why != null)
            {
                // a refusal (a siege, a live attack, a member down ...) is told, in the sender's language, beside the window's answer
                ScCommunity.TellKey(sender, why, "ui_denied");
                reply.Fail(why, "");
                return;
            }
            reply.Done();                               // Disband told every player of it (scDb1Disbanded*)
        }

        /// <summary>
        /// Round db1: the players of community cs, each once - its owner first (while they are of it: always in PerPlayer/PerParty),
        /// then the players the membership map lists in it (offline too), then those online in it (Shared mode's community 0: everyone
        /// unlisted).
        /// </summary>
        internal static List<string> DisbandPlayers(CommunityState cs)
        {
            var pids = new List<string>();
            if (cs == null) return pids;
            string owner = cs.Community.OwnerId ?? "";
            if (owner.Length > 0 && ScPlayers.CommunityOfId(owner) == cs.Id) pids.Add(owner);
            var listed = new List<string>();
            foreach (KeyValuePair<string, int> kv in ScRegistry.WorldState.Membership)
                if (kv.Value == cs.Id && !string.IsNullOrEmpty(kv.Key) && !pids.Contains(kv.Key)) listed.Add(kv.Key);
            listed.Sort(StringComparer.Ordinal);
            pids.AddRange(listed);
            foreach (EntityPlayer p in ScPlayers.Community(cs.Id).ToArray())
            {
                string id = ScPlayers.IdOf(p);
                if (id.Length > 0 && !pids.Contains(id)) pids.Add(id);
            }
            return pids;
        }

        /// <summary>
        /// Round db1 (the addendum's decisions 1-3): community cs is disbanded, refused (Error) by WhyNotDisband. In order:
        /// 1. its players online: their talk holds and work shifts end, and their links (EndLinks: enlisted people head home, the
        ///    home link, the linked storage view and the panel hold end);
        /// 2. every member leaves through ScCommunity.Leave, the dismissal path: back to the wild, recruitable again, her stand-in
        ///    removed, no longer following anyone. One that could not leave stops it here (scDb1Stopped: run it again to finish);
        /// 3. every outpost goes through ScOutposts.Disband (its garrison and those on the way turn home, raid demands and raids
        ///    off, markers off); one left stops it here;
        /// 4. lights out ends (the generators it switched off go back on), and the stockpile is lost;
        /// 5. Shared 1+ (DisbandMerged): merged into community 0 as the MP7 merge, then retired - its players are community 0's.
        ///    Shared community 0 (DisbandZeroShared): reset in place, unstarted and ownerless, its players stay with its relations.
        ///    PerPlayer/PerParty (DisbandSeparate): each player of it gets their own relations with a copy of its relations; accepted
        ///    requests and favors go to their acceptor's; the record retires (community 0: emptied and free);
        /// 6. tells to its players online (in each one's language), one log line with the counts, a save.
        /// by: who disbanded it, for the tells and the log ("" - the console: "An admin"). Server, main thread.
        /// </summary>
        internal static DisbandResult Disband(CommunityState cs, string by)
        {
            var res = new DisbandResult();
            string step = "";
            try
            {
                if (!ScRegistry.Loaded) { res.Error = "scActionNotReady"; return res; }
                string why = WhyNotDisband(cs);
                if (why != null) { res.Error = why; return res; }
                bool separate = Separate, zero = cs.Id == ScPlayers.SharedCommunity;
                res.Cid = cs.Id;
                res.Label = cs.Label;
                res.How = separate ? "own relations" : zero ? "reset in place" : "merged into community 0";
                string whose = Whose(cs);
                string owner = cs.Community.OwnerId ?? "";
                by = by ?? "";
                object byArg = by.Length > 0 ? (object)by : ScCommunity.LocArg("scDb1AnAdmin");
                List<string> pids = DisbandPlayers(cs);
                EntityPlayer[] online = ScPlayers.Community(cs.Id).ToArray();      // copied: Community reuses its list
                res.Players = pids.Count;
                res.Online = online.Length;
                using (ScRegistry.Use(cs.Id))
                {
                    // 1. its players' live links end, as Move's leaving half does
                    step = "the players' links";
                    for (int i = 0; i < online.Length; i++)
                    {
                        EntityPlayer p = online[i];
                        if (p == null) continue;
#if SC_V34
                        ScWork.ForgetPlayer(p.entityId);
#endif
                        EndLinks(cs, p, p.PlayerDisplayName);
                    }
                    // 2. the members: the dismissal path for each (Leave tells the community nothing for this reason: one tell below)
                    step = "the members";
                    foreach (Member m in new List<Member>(cs.Members.Values))
                    {
                        ScCommunity.Leave(m, DisbandLeaveKey);
                        if (!cs.Members.ContainsKey(m.Id)) res.Members++;
                    }
                    if (cs.Members.Count > 0)
                    {
                        res.MembersLeft = cs.Members.Count;
                        res.Error = "scDb1Stopped";
                        ScLog.Warn("community disband of " + cs.Label + " stopped: " + res.MembersLeft + " member(s) could not leave (see the lines above) - "
                                   + res.Members + " left; nothing else changed. Run it again to finish");
                        ScRegistry.SaveAtomic();
                        return res;
                    }
                    // 3. the outposts, through their own disband path
                    step = "the outposts";
                    foreach (Outpost o in new List<Outpost>(cs.Outposts))
                    {
                        try { ScOutposts.Disband(o, null); }
                        catch (Exception e) { ScLog.Once("db1-outpost-" + e.GetType().Name, e); }
                        if (!cs.Outposts.Contains(o)) res.Outposts++;
                    }
                    if (cs.Outposts.Count > 0)
                    {
                        res.OutpostsLeft = cs.Outposts.Count;
                        res.Error = "scDb1Stopped";
                        ScLog.Warn("community disband of " + cs.Label + " stopped: " + res.OutpostsLeft + " outpost(s) could not be abandoned (see the log) - its "
                                   + res.Members + " member(s) left, " + res.Outposts + " outpost(s) went. Run it again to finish");
                        ScRegistry.SaveAtomic();
                        return res;
                    }
                    // 4. lights out ends (its generators back on: they are the players' blocks), and the stockpile is lost
                    step = "the stockpile";
                    try { string msg; if (ScLightsOut.Active) ScLightsOut.CancelByOrder(null, out msg); }
                    catch (Exception e) { ScLog.Once("db1-lightsout", e); }
                    Community c = cs.Community;
                    for (int i = 0; i < ScNames.ResourceCount; i++) { res.StockLost += c.Stock[i]; c.Stock[i] = 0; }
                    c.AmmoOpenPoints = 0;
                }
                // 5. its players keep what it knew, by mode
                step = "the relations";
                if (!separate && !zero) DisbandMerged(cs, pids, whose, res);
                else if (!separate) DisbandZeroShared(cs, res);
                else DisbandSeparate(cs, pids, owner, res);
                // 6. the tells, the log, the save
                step = "the tells";
                string key = separate ? "scDb1Disbanded" : zero ? "scDb1DisbandedZero" : "scDb1DisbandedShared";
                for (int i = 0; i < online.Length; i++)
                    if (online[i] != null) ScCommunity.TellKey(online[i], key, "ui_denied", byArg);
                ScRegistry.SaveAtomic();
                ScLog.Out("community disbanded: " + res.Label + " (" + whose + ") by " + (by.Length > 0 ? by : "an admin") + " - " + (separate ? ModeName(Mode) : "Shared")
                          + ", " + res.How + ": " + res.Members + " member(s) back to the wild, " + res.Outposts + " outpost(s) abandoned, " + res.StockLost
                          + " stockpile unit(s) lost, " + res.Players + " player(s) (" + res.Online + " online)"
                          + (separate ? ", " + res.Records + " given their own relations with " + res.Enclaves + " enclave, " + res.Infestations + " infestation and "
                                        + res.Bands + " band relation(s)" : "")
                          + ", " + res.Scouts + " scouting mark(s) and " + res.Contacts + " radio contact(s) kept; requests: " + res.RequestsMoved
                          + (separate ? " passed to their acceptors, " : " kept, ") + res.RequestsWithdrawn + " withdrawn, " + res.RequestsClosed + " offer(s) closed; favors: "
                          + res.FavoursMoved + (separate ? " passed on, " : " kept, ") + res.FavoursDropped + " dropped, " + res.QuestsClosed + " other quest(s) closed"
                          + (res.MergeNote.Length > 0 ? "; merged: " + res.MergeNote : ""));
                res.Ok = true;
            }
            catch (Exception e)
            {
                // each distinct failure logged once (keyed by its site), and what already ran said
                ScLog.Once("db1-disband-" + e.GetType().Name + "-" + (e.TargetSite != null ? e.TargetSite.Name : "?"), e);
                ScLog.Warn("community disband" + (cs != null ? " of " + cs.Label : "") + " failed at " + (step.Length > 0 ? step : "its start") + " - run it again to finish");
                res.Error = step.Length == 0 ? "scActionFailed" : "scDb1Stopped";
            }
            return res;
        }

        /// <summary>
        /// Round db1, Shared mode, a community 1+: merged into community 0 as the MP7 merge (Merge: favors and requests move, the one
        /// further along kept on a clash; radio contacts move; enclave standing the higher, known OR, community 0's hostility kept; band
        /// trust the higher; infestations known OR), its scouting marks too, then it retires (its rows dropped, its hostility cvars
        /// cleared to 0, its players unlisted: community 0's). Community 0's grudge against each of its players ends (as Move's).
        /// </summary>
        private static void DisbandMerged(CommunityState cs, List<string> pids, string whose, DisbandResult res)
        {
            CommunityState into = ScCommunities.Zero;
            res.Scouts = MoveScouts(cs.Community, into.Community);
            res.Contacts = cs.Contacts.Count;
            res.FavoursMoved = cs.Prospects.Count;
            res.RequestsMoved = cs.EncRequests.Count;
            res.MergeNote = Merge(cs, into, "", null, whose);
            Retire(cs, true);
            foreach (string pid in pids) ScRelations.Forgive(into.Id, pid);
        }

        /// <summary>
        /// Round db1, Shared mode, community 0: it resets in place (ScCommunities.ResetZero: unstarted, ownerless, no home, no stockpile,
        /// none of its home's state) and its players stay in it with its relations - the relation rows (kept on the world's records),
        /// its favors, its accepted requests while their acceptor is still community 0's (else withdrawn), its radio contacts, its
        /// grudges, its scouting marks and its name. Its offered requests close (withdrawn, no standing lost).
        /// </summary>
        private static void DisbandZeroShared(CommunityState cs, DisbandResult res)
        {
            foreach (EnclaveRequest r in new List<EnclaveRequest>(cs.EncRequests))
            {
                if (r.IsAccepted && r.Kind != EnclaveRequest.KindSiege && (r.AcceptedBy.Length == 0 || ScPlayers.CommunityOfId(r.AcceptedBy) == cs.Id)) continue;
                CloseForDisband(cs, r, r.IsAccepted ? "its community was disbanded and its acceptor is in another community" : "its community was disbanded");
                if (r.IsAccepted) res.RequestsWithdrawn++;
                else res.RequestsClosed++;
            }
            var prospects = new List<Prospect>(cs.Prospects.Values);
            var requests = new List<EnclaveRequest>(cs.EncRequests);
            var contacts = new List<Contact>(cs.Contacts);
            var grudges = new List<string>(cs.Grudges);
            var scouts = new List<Scout>(cs.Community.Scouts);
            string name = cs.Community.Name ?? "";
            ScCommunities.ResetZero(false);
            CommunityState z = ScCommunities.Zero;
            z.Community.Name = name;
            foreach (Prospect p in prospects) z.Prospects[p.Id] = p;
            foreach (EnclaveRequest r in requests) z.EncRequests.Add(r);
            foreach (Contact ct in contacts) z.Contacts.Add(ct);
            z.Grudges.UnionWith(grudges);
            z.Community.Scouts.AddRange(scouts);
            res.FavoursMoved = prospects.Count;
            res.RequestsMoved = requests.Count;
            res.Contacts = contacts.Count;
            res.Scouts = scouts.Count;
            ScLog.Out("community: " + z.Label + " reset in place (disbanded; unstarted and ownerless - its players stay, with its relations)");
        }

        /// <summary>
        /// Round db1, PerPlayer/PerParty: what each player of the community keeps.
        /// - An accepted request passes to its acceptor's own relations while they are its player (else withdrawn, no standing lost,
        ///   with a tell); an offer closes (withdrawn). A favor passes to the player whose journal holds its quest (the owner first),
        ///   else to the owner's (no longer "taken on" unless done); any other holder's quest closes with a tell. Its radio contacts
        ///   go to the owner's (one record: a contact is one survivor).
        /// - Join records and opt-outs of it go; it retires (Retire: its rows dropped, its hostility cvars cleared to 0, its players
        ///   unlisted; community 0 emptied and free).
        /// - Each player (the owner, listed players, offline too) gets their own relations (EnsurePersonal: community 0 when it is
        ///   free) holding a copy of its relations: enclave standing, known and hostility (a war it earned follows its players) and
        ///   the enclaves' clocks, infestations known, bands known and trust, and its scouting marks - SC's markers follow from these.
        /// - PerPlayer: each one's allies reconcile is queued (another founder's ally joins that community; a retired one is never
        ///   found: FoundedBy and IsFounded see only communities that exist).
        /// </summary>
        private static void DisbandSeparate(CommunityState cs, List<string> pids, string owner, DisbandResult res)
        {
            // the heir of what no acceptor holds: the owner, else the first of its players (none: an ownerless community nobody is in)
            string first = owner.Length > 0 && pids.Contains(owner) ? owner : (pids.Count > 0 ? pids[0] : null);
            var reqTo = new List<KeyValuePair<EnclaveRequest, string>>();
            var favTo = new List<KeyValuePair<Prospect, string>>();
            var held = new HashSet<int>();
            using (ScRegistry.Use(cs.Id))
            {
                foreach (EnclaveRequest r in new List<EnclaveRequest>(cs.EncRequests))
                {
                    string heir = r.IsAccepted && r.Kind != EnclaveRequest.KindSiege ? RequestHeir(r, pids) : null;
                    if (heir == null)
                    {
                        CloseForDisband(cs, r, r.IsAccepted ? "its community was disbanded and its acceptor is none of its players" : "its community was disbanded");
                        if (r.IsAccepted) res.RequestsWithdrawn++;
                        else res.RequestsClosed++;
                        continue;
                    }
                    cs.EncRequests.Remove(r);
                    reqTo.Add(new KeyValuePair<EnclaveRequest, string>(r, heir));
                }
                foreach (Prospect pr in new List<Prospect>(cs.Prospects.Values))
                {
                    var holders = new List<string>();
                    foreach (string pid in pids) if (HoldsFavour(pid, pr.Id)) holders.Add(pid);
                    string heir = owner.Length > 0 && holders.Contains(owner) ? owner : (holders.Count > 0 ? holders[0] : first);
                    cs.Prospects.Remove(pr.Id);
                    foreach (string h in holders)
                        if (h != heir) { CloseFavourQuest(h, pr); res.QuestsClosed++; }
                    if (heir == null)
                    {
                        // nobody to take it: dropped - the body's favor marks go while no other community courts them
                        res.FavoursDropped++;
                        if (ScRegistry.GetProspect(pr.Id) == null)
                        {
                            EntityAlive npc = ScCommunity.FindNpc(pr.Id);
                            if (npc != null) { ScCommunity.ClearSynced(npc, "sc_favour"); ScCommunity.ClearSynced(npc, "sc_favourDone"); }
                        }
                        continue;
                    }
                    if (holders.Contains(heir)) held.Add(pr.Id);
                    else if (pr.Started && !pr.Done) pr.Started = false;        // no player of it holds its quest: free to take on again
                    favTo.Add(new KeyValuePair<Prospect, string>(pr, heir));
                }
            }
            var contacts = new List<Contact>(cs.Contacts);
            cs.Contacts.Clear();
            // the copy of its relations (after the closes: an enclave's request cooldown comes along)
            RelSnap snap = SnapRelations(cs);
            // how its players joined it, and every opt-out of it, mean nothing once it is gone (community 0 can be founded again)
            ScWorldState ws = ScRegistry.WorldState;
            foreach (string pid in pids) { ws.JoinedHow.Remove(pid); ws.JoinedVia.Remove(pid); }
            foreach (string k in new List<string>(ws.OptOuts.Keys)) ScJoins.ClearOptOut(k, cs.Id);
            Retire(cs, true);
            // each player's own relations, with the copy
            var recs = new Dictionary<string, CommunityState>(StringComparer.Ordinal);
            foreach (string pid in pids)
            {
                int rid = EnsurePersonal(ScPlayers.ById(pid), pid, NameOf(pid));
                CommunityState rec = ScCommunities.ById(rid);
                if (rec == null || !rec.Personal)
                {
                    ScLog.Warn("community disband: no own relations could be made for " + NameOf(pid) + " now - they get fresh ones at the next ask, without the copy");
                    continue;
                }
                recs[pid] = rec;
                res.Records++;
                CopyRelations(snap, rec);
            }
            res.Enclaves = snap.Enc.Count;
            res.Infestations = snap.Inf.Count;
            res.Bands = snap.Band.Count;
            res.Scouts = snap.Scouts.Count;
            CommunityState fallback = first != null && recs.ContainsKey(first) ? recs[first] : null;
            foreach (KeyValuePair<EnclaveRequest, string> kv in reqTo)
            {
                CommunityState rec;
                if (!recs.TryGetValue(kv.Value, out rec)) rec = fallback;
                Enclave e = ScEnclaves.ById(kv.Key.EnclaveId);
                if (rec == null) { ScLog.Warn("community disband: request #" + kv.Key.Id + " had nobody to go to - dropped with the community"); continue; }
                rec.EncRequests.Add(kv.Key);
                res.RequestsMoved++;
                EntityPlayer to = ScPlayers.ById(kv.Value);
                if (to != null && e != null) ScCommunity.TellKey(to, "scDb1RequestYours", "", e.Name);
            }
            foreach (KeyValuePair<Prospect, string> kv in favTo)
            {
                CommunityState rec;
                if (!recs.TryGetValue(kv.Value, out rec)) rec = fallback;
                if (rec == null) { res.FavoursDropped++; continue; }
                rec.Prospects[kv.Key.Id] = kv.Key;
                res.FavoursMoved++;
                EntityPlayer to = ScPlayers.ById(kv.Value);
                if (to != null && held.Contains(kv.Key.Id)) ScCommunity.TellKey(to, "scDb1FavourYours", "", kv.Key.Name);
            }
            if (fallback != null) foreach (Contact ct in contacts) fallback.Contacts.Add(ct);
            res.Contacts = fallback != null ? contacts.Count : 0;
            if (Mode == ScCommunityMode.PerPlayer) foreach (string pid in recs.Keys) ScAllies.QueueReconcile(pid);
        }

        /// <summary>
        /// Round db1: the player of the community (pids) whose journal holds this accepted request - its acceptor (AcceptedBy; empty is
        /// the host's own, a save from before MP4 or the host's own accept), or null when that is none of its players.
        /// </summary>
        private static string RequestHeir(EnclaveRequest r, List<string> pids)
        {
            string by = r.AcceptedBy ?? "";
            if (by.Length == 0)
            {
                EntityPlayerLocal lp = ScCommunity.LocalPlayer;
                by = lp != null ? ScPlayers.IdOf(lp) : "";
            }
            return by.Length > 0 && pids.Contains(by) ? by : null;
        }

        /// <summary>
        /// Round db1: a request the disband cannot pass on closes as withdrawn (no standing lost; ScEncRequests.Close, each kind's own
        /// ending: a missing survivor's goes home), in its community's scope, and its acceptor online is told. It leaves the community's
        /// list whatever Close managed (a throw is logged).
        /// </summary>
        private static void CloseForDisband(CommunityState cs, EnclaveRequest r, string reason)
        {
            Enclave e = ScEnclaves.ById(r.EnclaveId);
            EntityPlayer acceptor = r.IsAccepted ? (r.AcceptedBy.Length > 0 ? ScPlayers.ById(r.AcceptedBy) : ScCommunity.LocalPlayer) : null;
            try { using (ScRegistry.Use(cs.Id)) ScEncRequests.Close(r, ScEncRequests.Withdrawn, reason); }
            catch (Exception ex) { ScLog.Once("db1-request-close-" + ex.GetType().Name, ex); }
            cs.EncRequests.Remove(r);
            if (acceptor != null && e != null) ScCommunity.TellKey(acceptor, "scDb1RequestWithdrawn", "", e.Name);
        }

        /// <summary>
        /// Round db1: the player holds this favor's quest - online, the host's own journal or a remote player's last report; offline,
        /// their last report this session (not known counts as not held).
        /// </summary>
        private static bool HoldsFavour(string pid, int prospectId)
        {
            EntityPlayer p = ScPlayers.ById(pid);
            if (p == null) return ScQuestNet.ReportedFavour(pid, prospectId) == 1;
            if (p is EntityPlayerLocal) return ScQuests.FindFavour(p, prospectId) != null;
            return ScQuestNet.ReportHoldsFavour(p, prospectId);
        }

        /// <summary>Round db1: a holder's quest for a favor that went to another player closes (removed), with a tell; offline, at their next report.</summary>
        private static void CloseFavourQuest(string pid, Prospect pr)
        {
            EntityPlayer p = ScPlayers.ById(pid);
            if (p is EntityPlayerLocal) ScQuests.CloseFavour(p, pr.Id, false);
            else if (p != null) ScQuestNet.CloseFavourFor(p, pr.Id, false);
            else ScQuestNet.CloseFavourAtNextReport(pid, pr.Id);
            if (p != null) ScCommunity.TellKey(p, "scDb1FavourWithdrawn", "", pr.Name);
        }

        /// <summary>Round db1: Shared 1+, its scouting marks go to community 0 (one per building). Returns how many moved.</summary>
        private static int MoveScouts(Community from, Community into)
        {
            int n = 0;
            foreach (Scout s in from.Scouts)
            {
                bool have = false;
                foreach (Scout t in into.Scouts) if (t.Poi == s.Poi) { have = true; break; }
                if (have) continue;
                into.Scouts.Add(s);
                n++;
            }
            from.Scouts.Clear();
            return n;
        }

        /// <summary>Round db1: a community's relations as they stood - copies, never the rows themselves (Retire drops those).</summary>
        private sealed class RelSnap
        {
            public readonly List<KeyValuePair<Enclave, EncRel>> Enc = new List<KeyValuePair<Enclave, EncRel>>();
            public readonly List<KeyValuePair<Infestation, InfRel>> Inf = new List<KeyValuePair<Infestation, InfRel>>();
            public readonly List<KeyValuePair<Band, BandRel>> Band = new List<KeyValuePair<Band, BandRel>>();
            public readonly List<Scout> Scouts = new List<Scout>();
        }

        /// <summary>
        /// Round db1: community cs's saved relation values (every row it has a saved value in) and its scouting marks. An enclave's
        /// call in flight (CallKind, CallAt, CallPid, CallUntil) stays behind: it needs the radio, which goes with the home.
        /// </summary>
        private static RelSnap SnapRelations(CommunityState cs)
        {
            var s = new RelSnap();
            int cid = cs.Id;
            foreach (Enclave e in ScRegistry.Enclaves)
            {
                EncRel r = e.Rels.Peek(cid);
                if (r == null || !r.HasSaved) continue;
                s.Enc.Add(new KeyValuePair<Enclave, EncRel>(e, new EncRel
                {
                    Standing = r.Standing, Known = r.Known, KnownDay = r.KnownDay, TurnedHostile = r.TurnedHostile, HostileDay = r.HostileDay,
                    NextRequestDay = r.NextRequestDay, Thanks = r.Thanks ?? "", AllyRevealDay = r.AllyRevealDay, GiftDay = r.GiftDay,
                    BarterDay = r.BarterDay, CallDay = r.CallDay
                }));
            }
            foreach (Infestation inf in ScRegistry.Infestations)
            {
                InfRel r = inf.Rels.Peek(cid);
                if (r == null || !r.HasSaved) continue;
                s.Inf.Add(new KeyValuePair<Infestation, InfRel>(inf, new InfRel { Known = r.Known, KnownDay = r.KnownDay }));
            }
            foreach (Band b in ScRegistry.Bands)
            {
                BandRel r = b.Rels.Peek(cid);
                if (r == null || !r.HasSaved) continue;
                s.Band.Add(new KeyValuePair<Band, BandRel>(b, new BandRel { Known = r.Known, KnownDay = r.KnownDay, Trust = r.Trust }));
            }
            foreach (Scout sc in cs.Community.Scouts)
                if (sc != null) s.Scouts.Add(new Scout { Poi = sc.Poi, Size = sc.Size, Name = sc.Name, Res = sc.Res, ExpireDay = sc.ExpireDay });
            return s;
        }

        /// <summary>
        /// Round db1: the copy into one player's own relations (rec). A group at war with the community is at war with them too: its live
        /// bodies get rec's hostile cvar (ScEnclavePeople.OnTurnedHostile in rec's scope, no grace; a body spawned later gets it when it
        /// is dressed) - a disband never ends a war the community earned.
        /// </summary>
        private static void CopyRelations(RelSnap s, CommunityState rec)
        {
            int rid = rec.Id;
            var hostile = new List<Enclave>();
            foreach (KeyValuePair<Enclave, EncRel> kv in s.Enc)
            {
                EncRel f = kv.Value, t = kv.Key.Rels.Get(rid);
                t.Standing = f.Standing;
                t.Known = f.Known;
                t.KnownDay = f.KnownDay;
                t.TurnedHostile = f.TurnedHostile;
                t.HostileDay = f.HostileDay;
                t.NextRequestDay = f.NextRequestDay;
                t.Thanks = f.Thanks;
                t.AllyRevealDay = f.AllyRevealDay;
                t.GiftDay = f.GiftDay;
                t.BarterDay = f.BarterDay;
                t.CallDay = f.CallDay;
                if (f.TurnedHostile) hostile.Add(kv.Key);
            }
            foreach (KeyValuePair<Infestation, InfRel> kv in s.Inf)
            {
                InfRel t = kv.Key.Rels.Get(rid);
                t.Known = kv.Value.Known;
                t.KnownDay = kv.Value.KnownDay;
            }
            foreach (KeyValuePair<Band, BandRel> kv in s.Band)
            {
                BandRel t = kv.Key.Rels.Get(rid);
                t.Known = kv.Value.Known;
                t.KnownDay = kv.Value.KnownDay;
                t.Trust = kv.Value.Trust;
            }
            foreach (Scout sc in s.Scouts)
                rec.Community.Scouts.Add(new Scout { Poi = sc.Poi, Size = sc.Size, Name = sc.Name, Res = sc.Res, ExpireDay = sc.ExpireDay });
            foreach (Enclave e in hostile)
            {
                try { using (ScRegistry.Use(rid)) ScEnclavePeople.OnTurnedHostile(e, 0f); }
                catch (Exception ex) { ScLog.Once("db1-hostile-" + ex.GetType().Name, ex); }
            }
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
        /// Round MP7 (review AP/F): n4 - the leader being of cid, their party holds one of its players (them): the member walk that
        /// could never fail is gone, and the cheap tests (mode, leads a party) come first - part U asks it for online players every
        /// two seconds.
        /// </summary>
        public static bool PartyLeaderMayAct(EntityPlayer player, int cid)
        {
            try
            {
                if (player == null || !ScMembership.Separate || ScMembership.Mode != ScCommunityMode.PerParty) return false;
                Party party = player.Party;
                if (party == null || !ReferenceEquals(party.Leader, player)) return false;
                CommunityState cs = ScCommunities.ById(cid);
                if (cs == null || cs.Personal) return false;
                if (ScPlayers.CommunityOf(player) != cid) return false;
                return !ScParties.OwnerOnline(cs);
            }
            catch (Exception e)
            {
                ScLog.Once("mp7-party-leader", e);
                return false;
            }
        }
    }
}
