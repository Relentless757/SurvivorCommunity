using System;
using UnityEngine.Scripting;

// Block classes resolve as "Block" + the Class property, so blocks.xml Class="ScRadio, SurvivorCommunity" is this
// class in the global namespace (ReflectionHelpers.GetTypeWithPrefix in BlocksFromXml).

/// <summary>
/// The radio station: a powered consumer block (vanilla's BlockPowered + TileEntityPoweredBlock, wired like a
/// light) whose "Use radio" command opens the radio window while it has power. "Take" is vanilla's pickup.
/// Round MP5 (part R): any player's "Use radio" opens the window on their own machine (ScRadio.Open: a client checks the
/// power its copy of the block shows, and the server checks the station again with every request - ScActionsRadio).
/// </summary>
[Preserve]
public class BlockScRadio : BlockPowered
{
    private readonly BlockActivationCommand[] radioCmds =
    {
        new BlockActivationCommand("radio", "talk", true),
        new BlockActivationCommand("take", "hand", false)
    };

    public override BlockActivationCommand[] GetBlockActivationCommands(WorldBase _world, BlockValue _blockValue, Vector3i _blockPos, EntityAlive _entityFocusing)
    {
        try
        {
            bool mine = _world.IsMyLandProtectedBlock(_blockPos, _world.GetGameManager().GetPersistentLocalPlayer());
            radioCmds[0].enabled = true;
            radioCmds[1].enabled = mine && TakeDelay > 0f;
        }
        catch (Exception e)
        {
            SurvivorCommunity.ScLog.Once("radio-cmds", e);
        }
        return radioCmds;
    }

    /// <summary>Placed: remembered at once (a radio at an outpost becomes its relay without being used first).</summary>
    public override void OnBlockAdded(WorldBase _world, Chunk _chunk, Vector3i _blockPos, BlockValue _blockValue, PlatformUserIdentifierAbs _addedByPlayer)
    {
        base.OnBlockAdded(_world, _chunk, _blockPos, _blockValue, _addedByPlayer);
        try
        {
            if (_blockValue.ischild || !ThreadManager.IsMainThread() || !SurvivorCommunity.ScCommunity.IsHost) return;
            // round MP6 (part G): remembered by the community whose home or outpost holds it, else the placer's (a relay set up
            // before its outpost); nobody's: the community in scope, as before MP6
            using (SurvivorCommunity.ScGates.ScopeAt(_blockPos, _addedByPlayer))
                SurvivorCommunity.ScRadio.NoteRadio(_blockPos);
        }
        catch (Exception e)
        {
            SurvivorCommunity.ScLog.Once("radio-added", e);
        }
    }

    public override void OnBlockRemoved(WorldBase _world, Chunk _chunk, Vector3i _blockPos, BlockValue _blockValue)
    {
        base.OnBlockRemoved(_world, _chunk, _blockPos, _blockValue);
        try
        {
            if (_blockValue.ischild || !ThreadManager.IsMainThread()) return;
            // round MP6 (part G): forgotten by every community that remembered it (a removal by zombies or an explosion names no
            // player); with one community: once, as before
            SurvivorCommunity.ScCommunities.ForEach(cs => SurvivorCommunity.ScOutposts.ForgetRadio(_blockPos));
        }
        catch (Exception e)
        {
            SurvivorCommunity.ScLog.Once("radio-removed", e);
        }
    }

    public override string GetActivationText(WorldBase _world, BlockValue _blockValue, Vector3i _blockPos, EntityAlive _entityFocusing)
    {
        return Localization.Get("scRadioUse");
    }

    public override bool OnBlockActivated(string _commandName, WorldBase _world, Vector3i _blockPos, BlockValue _blockValue, EntityPlayerLocal _player)
    {
        try
        {
            if (_blockValue.ischild)
            {
                Vector3i parentPos = _blockValue.Block.multiBlockPos.GetParentPos(_blockPos, _blockValue);
                return OnBlockActivated(_commandName, _world, parentPos, _world.GetBlock(parentPos), _player);
            }
            if (_commandName == "radio")
            {
                // round MP6 (part G): another community's radio station is not yours (the user's decision 3). The host checks here
                // (its own requests are not checked again); a client's requests are refused by the server (ScActionsRadio)
                if (SurvivorCommunity.ScGates.OutsiderAt(_blockPos, _player))
                {
                    SurvivorCommunity.ScCommunity.TellKey(_player, "scGateNotYourStation", "ui_denied");
                    return true;
                }
                SurvivorCommunity.ScRadio.Open(_world as World, _blockPos, _player);
                return true;
            }
        }
        catch (Exception e)
        {
            SurvivorCommunity.ScLog.Once("radio-activate", e);
        }
        return base.OnBlockActivated(_commandName, _world, _blockPos, _blockValue, _player);
    }
}
