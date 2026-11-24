using System;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round MP2 (multiplayer, part R): the land claim radial's actions (TEFeatureSCCommunity) as ScActions requests, so a
    /// client can start the community, move its home, establish and disband outposts. Single player and the listen host's own
    /// radial go through the same handlers, run inline (ScActions.Request).
    /// - home.set x y z [mode]: the claim becomes the home, owned by the SENDER (ScZone.SetHome records their platform id).
    ///   mode (optional, 0 when absent): HomeSet = the radial's "Set as community home" and any other caller (moves the home);
    ///   HomeStart = "Start a community here" (refused once a home exists: two players starting at once get one community);
    ///   HomeAdoptCommunity / HomeAdoptStorage = AutoHomeFirstClaim's adoption by "Community" / "Linked storage" (silent, and
    ///   nothing happens once a home exists).
    /// - outpost.establish x y z: ScOutposts.Establish on the sender's claim.
    /// - outpost.disbandclaim x y z: ScOutposts.Disband of the outpost on the sender's claim (the radial's two-press confirm is
    ///   held on the pressing machine: ScOutposts.DisbandPressed).
    /// The server checks what the radial's visibility checked on the pressing machine: the claim is an active land claim that
    /// belongs to the sender (the claim map, the server's own), and then the verb's own tests.
    /// </summary>
    public static class ScActionsClaim
    {
        public const string OpHomeSet = "home.set";
        public const string OpEstablish = "outpost.establish";
        public const string OpDisbandClaim = "outpost.disbandclaim";

        /// <summary>home.set's mode (its fourth argument).</summary>
        public const int HomeSet = 0, HomeStart = 1, HomeAdoptCommunity = 2, HomeAdoptStorage = 3;

        // ------------------------------------------------------------------ asking (the radial, every machine) ----

        /// <summary>
        /// The radial's home entries: asks for home.set with the mode. Start and Set tell what happened (as the radial always
        /// did); an adoption by Community / Linked storage stays silent unless refused.
        /// </summary>
        internal static void RequestHome(Vector3i claim, int mode, EntityPlayerLocal player)
        {
            ScActions.Request(OpHomeSet, new[] { claim.x, claim.y, claim.z, mode }, null, r =>
            {
                if (mode == HomeSet || mode == HomeStart || !r.Ok) TellReply(player, r);
            });
        }

        /// <summary>The radial's "Establish outpost": the answer is told (the outpost's yield, or why not).</summary>
        internal static void RequestEstablish(Vector3i claim, EntityPlayerLocal player)
        {
            ScActions.Request(OpEstablish, r => TellReply(player, r), claim.x, claim.y, claim.z);
        }

        /// <summary>The radial's second "Disband outpost" press (ScOutposts.DisbandPressed).</summary>
        internal static void RequestDisband(Vector3i claim, EntityPlayer player)
        {
            ScActions.Request(OpDisbandClaim, r => TellReply(player, r), claim.x, claim.y, claim.z);
        }

        /// <summary>A reply's message as a tooltip to this machine's player (nothing for an empty one).</summary>
        private static void TellReply(EntityPlayer player, ScActionReply r)
        {
            if (r == null) return;
            EntityPlayer to = ScCommunity.LocalPlayer ?? player;      // a client's answer can come after a respawn
            ScCommunity.Tell(to, r.Text, r.Sound);
        }

        // ------------------------------------------------------------------ checks (the server) ----

        /// <summary>
        /// Why the sender may not use this claim, as a localization key; null = an active land claim of theirs. The server's
        /// form of the radial's LocalPlayerIsOwner + TEFeatureLandClaim.IsPrimary, against the claim map (vanilla's own list
        /// of active claims and their owners).
        /// </summary>
        internal static string WhyNotOwnClaim(Vector3i claim, EntityPlayer player)
        {
            PersistentPlayerList pp = GameManager.Instance != null ? GameManager.Instance.persistentPlayers : null;
            if (pp == null || pp.m_lpBlockMap == null || player == null) return "scActionNotReady";
            PersistentPlayerData owner;
            if (!pp.m_lpBlockMap.TryGetValue(claim, out owner) || owner == null) return "scClaimInactive";
            PersistentPlayerData mine = pp.GetPlayerDataFromEntityID(player.entityId);
            if (mine == null) return "scClaimNotYours";
            if (ReferenceEquals(owner, mine)) return null;
            return owner.PrimaryId != null && owner.PrimaryId.Equals(mine.PrimaryId) ? null : "scClaimNotYours";
        }

        /// <summary>
        /// The online community player who placed this block (the composite's owner, set from the placer when the block went
        /// down), or null: a POI's or the console's block has no owner. Server side (TEFeatureSCCommunity.OnAdded).
        /// </summary>
        internal static EntityPlayer PlacerOf(TileEntityComposite te)
        {
            try
            {
                if (te == null || te.Owner == null) return null;
                PersistentPlayerList pp = GameManager.Instance != null ? GameManager.Instance.persistentPlayers : null;
                EntityPlayer p = pp != null ? pp.GetEntityPlayerFromUserId(te.Owner) : null;
                return p != null && ScPlayers.InCommunity(p) ? p : null;
            }
            catch (Exception e)
            {
                ScLog.Once("claim-placer", e);
                return null;
            }
        }

        /// <summary>" by Name" for a remote player (the server's log), "" for the host's own (the log lines as before).</summary>
        private static string By(EntityPlayer sender)
        {
            return sender == null || sender is EntityPlayerLocal ? "" : " (by " + (sender.PlayerDisplayName ?? "?") + ")";
        }

        // ------------------------------------------------------------------ handlers (the server) ----

        /// <summary>home.set x y z [mode]: the radial's Start a community here / Set as community home (and the adoptions).</summary>
        [ScAction(OpHomeSet, ScActionAccess.Owner)]
        private static void HomeSetAction(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            Vector3i claim = args.Pos(0);
            int mode = args.Int(3, HomeSet);
            string why = WhyNotOwnClaim(claim, sender);
            if (why != null) { reply.Fail(why); return; }
            if (ScOutposts.IsOutpostClaim(claim)) { reply.Fail("scHomeNotOutpost"); return; }     // an outpost is never the home
            bool adopt = mode == HomeAdoptCommunity || mode == HomeAdoptStorage;
            if (adopt && !ScConfig.AutoHomeFirstClaim) { reply.Done(); return; }   // the server's setting decides an adoption
            bool started = !ScZone.HasHome;
            if (!started)
            {
                // the client's copy said "no home yet", but someone started one meanwhile: a start must not move it
                if (mode == HomeStart) { reply.Fail("scHomeAlreadyStarted"); return; }
                if (adopt) { reply.Done(); return; }                        // nothing to adopt any more
            }
            ScZone.SetHome(claim, sender);
            ScRegistry.SaveAtomic();
            if (adopt)
                ScLog.Out("community home set to " + claim + " by the radial's " + (mode == HomeAdoptStorage ? "Linked storage" : "Community") + " (AutoHomeFirstClaim)" + By(sender));
            else
                ScLog.Out((started ? "community started: home set to " + claim + " by radial (Start a community here)"
                                   : "community home set to " + claim + " by radial") + By(sender));
            reply.Done(adopt ? "" : (started ? "scHomeStarted" : "scHomeSet"), "");
        }

        /// <summary>outpost.establish x y z: the radial's Establish outpost on the sender's claim.</summary>
        [ScAction(OpEstablish, ScActionAccess.Community)]
        private static void EstablishAction(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            Vector3i claim = args.Pos(0);
            string why = WhyNotOwnClaim(claim, sender);
            if (why != null) { reply.Fail(why); return; }
            string msg;
            Outpost o = ScOutposts.Establish(claim, sender, false, out msg);     // every other gate (CanEstablish), the fee, the save
            if (o == null) { reply.Fail(string.IsNullOrEmpty(msg) ? "scInternal" : msg, "ui_denied"); return; }
            ScOutposts.HostMarkersFollow(sender);
            reply.Done(msg, "");
        }

        /// <summary>outpost.disbandclaim x y z: the radial's second Disband outpost press, on the sender's claim.</summary>
        [ScAction(OpDisbandClaim, ScActionAccess.Owner)]
        private static void DisbandClaimAction(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            Vector3i claim = args.Pos(0);
            Outpost o = ScOutposts.AtClaim(claim);
            if (o == null) { reply.Fail("scOpNotHere"); return; }            // disbanded or lost since the client's copy
            string why = WhyNotOwnClaim(claim, sender);
            if (why != null) { reply.Fail(why); return; }
            ScOutposts.Disband(o, sender);                                 // tells the sender (scOpDisbanded) and saves, as before
            reply.Done();
        }
    }
}
