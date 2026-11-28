using System;
using UnityEngine.Scripting;

namespace SurvivorCommunity
{
    /// <summary>
    /// Composite feature appended to keystoneBlock (Config/blocks.xml): the land claim block's radial gets
    /// "Community" (open the roster), "Set as community home" and "Linked storage"; a claim inside a POI away
    /// from home also gets "Establish outpost", and an outpost's claim gets "Outpost" (the report), "Disband
    /// outpost" and "Linked storage" - which there opens the HOME storage over the radio (ScHomeLink) - but never
    /// "Community" or "Set as community home", so an outpost cannot become the home. No stored data - the
    /// registry holds the home and the outposts. AllowBlockActivationCommand / OnBlockActivated take ReadOnlySpan
    /// and are unpatchable, hence a subclass; APPEND only in XML so TEFeatureLandClaim keeps CustomOrder 0.
    ///
    /// Round 32a, the home is opt-in: with no home yet, a claim of yours offers "Start a community here" (the same
    /// action as "Set as community home", under its own label) and no "Linked storage"; "Community" opens the window
    /// without adopting the claim. "Set as community home" shows only while a home exists elsewhere (moving it), and
    /// the home's own claim offers neither. AutoHomeFirstClaim true restores the old adoption by Community / Linked storage.
    ///
    /// Round MP2 (multiplayer): the entries show on a client too, from its copy of the community, for its own claims; what
    /// they change goes to the server as ScActions requests (ScActionsClaim), which checks the claim against the sender.
    /// Round MP3: Linked storage and Outpost storage too (storage.open, ScStorageServer).
    /// </summary>
    [Preserve]
    public class TEFeatureSCCommunity : TEFeatureAbs
    {
        public const string CmdCommunity = "community";
        public const string CmdSetHome = "sethome";
        /// <summary>Round 32a: "Start a community here" - sethome's action while there is no home yet (blockcommand_TEFeatureSCCommunity:starthome).</summary>
        public const string CmdStartHome = "starthome";
        public const string CmdStorage = "storage";
        public const string CmdEstablish = "establish";
        public const string CmdOutpost = "outpost";
        public const string CmdDisband = "disband";
        public const string CmdOutpostStorage = "opstorage";

        public TEFeatureSCCommunity()
        {
        }

        public override void Init(TileEntityComposite _parent, TileEntityFeatureData _featureData)
        {
            base.Init(_parent, _featureData);
            try
            {
                string hash = (_featureData != null) ? _featureData.NameHash.ToString("X8") : "????????";
                ScLog.OnceOut("feature-registered", "community feature registered on the land claim block (hash 0x" + hash + ")");
            }
            catch (Exception e)
            {
                ScLog.Once("TEFeatureSCCommunity.Init", e);
            }
        }

        /// <summary>Must be public (TEFeatureAbs.CopyFromInternal is a publicized abstract).</summary>
        public override void CopyFromInternal(TileEntityComposite _other)
        {
        }

#if SC_V34
        // 3.4: the stream modes are top-level types (were nested in TileEntity); the same pass-through
        public override void Write(PooledBinaryWriter _bw, StreamModeWrite _eStreamMode)
        {
            base.Write(_bw, _eStreamMode);
        }

        public override void Read(PooledBinaryReader _br, StreamModeRead _eStreamMode)
        {
            base.Read(_br, _eStreamMode);
        }
#else
        public override void Write(PooledBinaryWriter _bw, TileEntity.StreamModeWrite _eStreamMode)
        {
            base.Write(_bw, _eStreamMode);
        }

        public override void Read(PooledBinaryReader _br, TileEntity.StreamModeRead _eStreamMode)
        {
            base.Read(_br, _eStreamMode);
        }
#endif

        public override void InitBlockActivationCommands(Action<BlockActivationCommand, TileEntityComposite.EBlockCommandOrder, TileEntityFeatureData> _addCallback)
        {
            base.InitBlockActivationCommands(_addCallback);
            _addCallback(new BlockActivationCommand(CmdCommunity, "frames", _enabled: false), TileEntityComposite.EBlockCommandOrder.Normal, base.FeatureData);
            _addCallback(new BlockActivationCommand(CmdSetHome, "block_upgrade", _enabled: false), TileEntityComposite.EBlockCommandOrder.Normal, base.FeatureData);
            _addCallback(new BlockActivationCommand(CmdStartHome, "block_upgrade", _enabled: false), TileEntityComposite.EBlockCommandOrder.Normal, base.FeatureData);
            _addCallback(new BlockActivationCommand(CmdStorage, "search", _enabled: false), TileEntityComposite.EBlockCommandOrder.Normal, base.FeatureData);
            _addCallback(new BlockActivationCommand(CmdEstablish, "map_town", _enabled: false), TileEntityComposite.EBlockCommandOrder.Normal, base.FeatureData);
            _addCallback(new BlockActivationCommand(CmdOutpost, "map_cabin", _enabled: false), TileEntityComposite.EBlockCommandOrder.Normal, base.FeatureData);
            _addCallback(new BlockActivationCommand(CmdDisband, "x", _enabled: false), TileEntityComposite.EBlockCommandOrder.Normal, base.FeatureData);
            _addCallback(new BlockActivationCommand(CmdOutpostStorage, "loot_sack", _enabled: false), TileEntityComposite.EBlockCommandOrder.Normal, base.FeatureData);
        }

        /// <summary>An Outpost Claim Block went down: it tries to establish itself on the next ticker second.</summary>
        public override void OnAdded(Vector3i _blockPos, BlockValue _blockValue)
        {
            base.OnAdded(_blockPos, _blockValue);
            try
            {
                if (!ScCommunity.IsHost || base.Parent == null) return;
                Block b = _blockValue.Block;
                if (b == null || !string.Equals(b.GetBlockName(), ScOutposts.FlagBlock, StringComparison.OrdinalIgnoreCase)) return;
                // round MP2: the PLACING player's ownership, tested here on the server - the composite's owner (set from the
                // placer as the block went down) is an online community player. LocalPlayerIsOwner is the host's own player's
                // only: false for a client's flag on a listen host and for every flag on a dedicated server, so those never
                // established themselves. A POI's or the console's block has no owner and is still left alone.
                // round MP6 (fix G-M1): asked in the placer's community's scope - PlacerOf's community test reads the scope, which
                // nothing set here (community 0 on a dedicated server: another community's flag never established)
                using (ScGates.ScopeOfUser(base.Parent.Owner))
                {
                    if (!base.Parent.LocalPlayerIsOwner && ScActionsClaim.PlacerOf(base.Parent) == null) return;
                    // round MP6 (part G): established for the placer's community (the composite's owner), in that community's tick
                    ScOutposts.QueueFlag(ToWorldPos(), ScGates.CommunityOfUser(base.Parent.Owner));
                }
            }
            catch (Exception e)
            {
                ScLog.Once("TEFeatureSCCommunity.OnAdded", e);
            }
        }

        /// <summary>The claim block is gone: let the outposts check their claims now (the claim map decides).</summary>
        public override void OnDestroy()
        {
            base.OnDestroy();
            try
            {
                if (ScCommunity.IsHost) ScOutposts.OnClaimDestroyed(ToWorldPos());
            }
            catch (Exception e)
            {
                ScLog.Once("TEFeatureSCCommunity.OnDestroy", e);
            }
        }

        public override bool AllowBlockActivationCommand(ITileEntityFeature _module, ReadOnlySpan<char> _commandName, WorldBase _world, Vector3i _blockPos, BlockValue _blockValue, EntityAlive _entityFocusing)
        {
            try
            {
                if (!base.AllowBlockActivationCommand(_module, _commandName, _world, _blockPos, _blockValue, _entityFocusing)) return false;
                if (!Equals(_module)) return true;      // never veto another feature's command
                bool community = CommandIs(_commandName, CmdCommunity);
                bool setHome = CommandIs(_commandName, CmdSetHome);
                bool startHome = CommandIs(_commandName, CmdStartHome);
                bool storage = CommandIs(_commandName, CmdStorage);
                bool establish = CommandIs(_commandName, CmdEstablish);
                bool outpost = CommandIs(_commandName, CmdOutpost);
                bool disband = CommandIs(_commandName, CmdDisband);
                bool opStorage = CommandIs(_commandName, CmdOutpostStorage);
                if (!community && !setHome && !startHome && !storage && !establish && !outpost && !disband && !opStorage) return true;
                using (ScGates.ScopeOfPlayer(_entityFocusing as EntityPlayer))     // round MP6 (part G): the focusing player's community
                    return Allow(setHome, startHome, storage, establish, outpost, disband, opStorage, _blockPos);
            }
            catch (Exception e)
            {
                ScLog.Once("TEFeatureSCCommunity.Allow", e);
                return false;
            }
        }

        /// <summary>Round MP6 (part G): AllowBlockActivationCommand's own tests, in the focusing player's community's scope.</summary>
        private bool Allow(bool setHome, bool startHome, bool storage, bool establish, bool outpost, bool disband, bool opStorage, Vector3i _blockPos)
        {
            {
                // round MP2: a client shows them from its copy of the community (the home, the outposts) - its requests act on
                // the server (OnBlockActivated). The ownership test below runs on the pressing machine for its own player:
                // a client's own claim passes there; the server checks it again against the sender.
                if (!ScCommunity.IsHost && !ScRegistry.Ready) return false;
                if (base.Parent == null || !base.Parent.LocalPlayerIsOwner) return false;
                TEFeatureLandClaim lc = base.Parent.GetFeature<TEFeatureLandClaim>();
                if (lc == null || !lc.IsPrimary()) return false;
                bool isOutpost = ScOutposts.AtClaim(_blockPos) != null;
                if (outpost || disband || opStorage) return isOutpost;
                // at an outpost it opens the HOME storage over the radio; with no home yet there is nothing to link (round
                // 32a), unless AutoHomeFirstClaim makes this claim the home on the spot as before
                if (storage) return isOutpost || ScZone.HasHome || ScConfig.AutoHomeFirstClaim;
                if (isOutpost) return false;                  // an outpost's claim never offers the home entries
                bool isHome = ScZone.HasHome && ScZone.Home == _blockPos;
                // MP7 (part U): "Start a community here" for a personal record (it has no home: shown already; it asks community.start),
                // "Set as community home" and Disband outpost hidden for non-owner roles (the derived block's role=)
                if (startHome) return !ScZone.HasHome;        // round 32a: opt-in - the first home is started here
                if (setHome) return ScZone.HasHome && !isHome; // moving an existing home here
                if (establish)
                {
                    // a claim inside a real POI away from home (the gates and their reasons come on activation)
                    if (!ScZone.HasHome || isHome) return false;
                    PrefabInstance pi = ScSecurity.PoiAt(_blockPos);
                    return pi != null && !ScSecurity.OverlapsHome(pi);
                }
                return true;
            }
        }

        public override bool OnBlockActivated(ReadOnlySpan<char> _commandName, WorldBase _world, Vector3i _blockPos, BlockValue _blockValue, EntityPlayerLocal _player)
        {
            try
            {
                using (ScGates.ScopeOfPlayer(_player))     // round MP6 (part G): the pressing player's community
                {
                // round MP2: every change goes through a request (ScActionsClaim: home.set, outpost.establish,
                // outpost.disbandclaim) - run inline in single player and for the host's own player, sent to the server from a
                // client. The windows open here, on the pressing machine (from its copy on a client).
                bool startHome = CommandIs(_commandName, CmdStartHome);
                if (startHome || CommandIs(_commandName, CmdSetHome))
                {
                    ScActionsClaim.RequestHome(_blockPos, startHome ? ScActionsClaim.HomeStart : ScActionsClaim.HomeSet, _player);
                    return true;
                }
                if (CommandIs(_commandName, CmdCommunity))
                {
                    // round 32a: opening the window starts nothing (the home is opt-in), unless AutoHomeFirstClaim
                    if (!ScZone.HasHome && ScConfig.AutoHomeFirstClaim) ScActionsClaim.RequestHome(_blockPos, ScActionsClaim.HomeAdoptCommunity, _player);
                    ScCommunity.OpenWindow(_player);
                    return true;
                }
                if (CommandIs(_commandName, CmdStorage))
                {
                    bool atOutpost = ScOutposts.AtClaim(_blockPos) != null;
                    // round MP3: linked storage (and the outpost's home link) for every player - the server scans, claims and
                    // answers with the view (ScStorage.RequestOpen; at once on the host). Round MP2 (review radials MINOR 1): a
                    // machine that cannot ask the server is refused BEFORE the AutoHomeFirstClaim adoption - a refused action
                    // must not make the client's claim the home. The adoption goes first, so the server (which takes requests
                    // in order) has the home when the open arrives.
                    string refusal = ScActionsWindow.RequestRefusal();
                    // round MP4 (part: tells): this file's tells go by localization key (ScCommunity.TellKey / ScPlayers.TellCommunityKey), so each player reads them in their own language
                    if (refusal != null) { ScCommunity.TellKey(_player, refusal, "ui_denied"); return true; }
                    if (atOutpost) { ScStorage.RequestOpen(_player, ScStorage.KindHomeLink, _blockPos); return true; }
                    if (!ScZone.HasHome && ScConfig.AutoHomeFirstClaim) ScActionsClaim.RequestHome(_blockPos, ScActionsClaim.HomeAdoptStorage, _player);
                    ScStorage.RequestOpen(_player, ScStorage.KindHome, _blockPos);
                    return true;
                }
                if (CommandIs(_commandName, CmdEstablish))
                {
                    ScActionsClaim.RequestEstablish(_blockPos, _player);
                    return true;
                }
                if (CommandIs(_commandName, CmdOutpost))
                {
                    Outpost o = ScOutposts.AtClaim(_blockPos);
                    if (o != null) ScCommunity.OpenOutposts(_player, o.Id);
                    return true;
                }
                if (CommandIs(_commandName, CmdDisband))
                {
                    ScOutposts.DisbandPressed(_blockPos, _player);
                    return true;
                }
                if (CommandIs(_commandName, CmdOutpostStorage))
                {
                    Outpost o = ScOutposts.AtClaim(_blockPos);
                    if (o == null) return true;
                    // round MP3: the outpost's own boxes for every player (the server scans its building and names the title)
                    ScStorage.RequestOpen(_player, ScStorage.KindOutpost, _blockPos);
                    return true;
                }
                }
            }
            catch (Exception e)
            {
                ScLog.Once("TEFeatureSCCommunity.OnBlockActivated", e);
                return true;
            }
            return base.OnBlockActivated(_commandName, _world, _blockPos, _blockValue, _player);
        }
    }
}
