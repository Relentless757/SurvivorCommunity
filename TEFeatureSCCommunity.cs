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
    /// </summary>
    [Preserve]
    public class TEFeatureSCCommunity : TEFeatureAbs
    {
        public const string CmdCommunity = "community";
        public const string CmdSetHome = "sethome";
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
            _addCallback(new BlockActivationCommand(CmdStorage, "search", _enabled: false), TileEntityComposite.EBlockCommandOrder.Normal, base.FeatureData);
            _addCallback(new BlockActivationCommand(CmdEstablish, "map_town", _enabled: false), TileEntityComposite.EBlockCommandOrder.Normal, base.FeatureData);
            _addCallback(new BlockActivationCommand(CmdOutpost, "map_cabin", _enabled: false), TileEntityComposite.EBlockCommandOrder.Normal, base.FeatureData);
            _addCallback(new BlockActivationCommand(CmdDisband, "x", _enabled: false), TileEntityComposite.EBlockCommandOrder.Normal, base.FeatureData);
            _addCallback(new BlockActivationCommand(CmdOutpostStorage, "loot_sack", _enabled: false), TileEntityComposite.EBlockCommandOrder.Normal, base.FeatureData);
        }

        /// <summary>An Outpost Flag went down: it tries to establish itself on the next ticker second.</summary>
        public override void OnAdded(Vector3i _blockPos, BlockValue _blockValue)
        {
            base.OnAdded(_blockPos, _blockValue);
            try
            {
                if (!ScCommunity.IsHost || base.Parent == null || !base.Parent.LocalPlayerIsOwner) return;
                Block b = _blockValue.Block;
                if (b == null || !string.Equals(b.GetBlockName(), ScOutposts.FlagBlock, StringComparison.OrdinalIgnoreCase)) return;
                ScOutposts.QueueFlag(ToWorldPos());
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
                bool storage = CommandIs(_commandName, CmdStorage);
                bool establish = CommandIs(_commandName, CmdEstablish);
                bool outpost = CommandIs(_commandName, CmdOutpost);
                bool disband = CommandIs(_commandName, CmdDisband);
                bool opStorage = CommandIs(_commandName, CmdOutpostStorage);
                if (!community && !setHome && !storage && !establish && !outpost && !disband && !opStorage) return true;
                if (!ScCommunity.IsHost) return false;
                if (base.Parent == null || !base.Parent.LocalPlayerIsOwner) return false;
                TEFeatureLandClaim lc = base.Parent.GetFeature<TEFeatureLandClaim>();
                if (lc == null || !lc.IsPrimary()) return false;
                bool isOutpost = ScOutposts.AtClaim(_blockPos) != null;
                if (outpost || disband || opStorage) return isOutpost;
                if (storage) return true;                     // at an outpost it opens the HOME storage over the radio
                if (isOutpost) return false;                  // an outpost's claim never offers the home entries
                bool isHome = ScZone.HasHome && ScZone.Home == _blockPos;
                if (setHome) return !isHome;
                if (establish)
                {
                    // a claim inside a real POI away from home (the gates and their reasons come on activation)
                    if (!ScZone.HasHome || isHome) return false;
                    PrefabInstance pi = ScSecurity.PoiAt(_blockPos);
                    return pi != null && !ScSecurity.OverlapsHome(pi);
                }
                return true;
            }
            catch (Exception e)
            {
                ScLog.Once("TEFeatureSCCommunity.Allow", e);
                return false;
            }
        }

        public override bool OnBlockActivated(ReadOnlySpan<char> _commandName, WorldBase _world, Vector3i _blockPos, BlockValue _blockValue, EntityPlayerLocal _player)
        {
            try
            {
                if (CommandIs(_commandName, CmdSetHome))
                {
                    ScZone.SetHome(_blockPos, _player);
                    ScRegistry.SaveAtomic();
                    ScCommunity.Tell(_player, Localization.Get("scHomeSet"), "");
                    ScLog.Out("community home set to " + _blockPos + " by radial");
                    return true;
                }
                if (CommandIs(_commandName, CmdCommunity))
                {
                    if (!ScZone.HasHome) { ScZone.SetHome(_blockPos, _player); ScRegistry.SaveAtomic(); }
                    ScCommunity.OpenWindow(_player);
                    return true;
                }
                if (CommandIs(_commandName, CmdStorage))
                {
                    if (ScOutposts.AtClaim(_blockPos) != null) { ScHomeLink.Request(_player, _blockPos); return true; }
                    if (!ScZone.HasHome) { ScZone.SetHome(_blockPos, _player); ScRegistry.SaveAtomic(); }
                    ScStorage.OpenWindow(_player);
                    return true;
                }
                if (CommandIs(_commandName, CmdEstablish))
                {
                    string msg;
                    Outpost o = ScOutposts.Establish(_blockPos, _player, false, out msg);
                    ScCommunity.Tell(_player, msg, o != null ? "" : "ui_denied");
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
                    if (o != null) ScStorage.OpenWindow(_player, _blockPos, o.Box, string.Format(Localization.Get("scOpStorageTitle"), o.Name));
                    return true;
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
