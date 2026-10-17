using System;
using UnityEngine.Scripting;

// Block classes resolve as "Block" + the Class property, so blocks.xml Class="ScEncRemains, SurvivorCommunity" is this
// class in the global namespace (as BlockScRadio).

/// <summary>
/// E5, a missing survivor's remains (SurvivorCommunity.ScEncSearch): a corpse block the search places where they were found
/// dead (or fell before the player reached them), put back when something erased it, taken away when the search ends. Its one
/// command, "Search the body" (the BlockScRadio activation pattern), tells the search; nothing to pick up or harvest. Host
/// only: on a client the command does nothing.
/// </summary>
[Preserve]
public class BlockScEncRemains : Block
{
    private const string CmdSearch = "scencsearch";

    private readonly BlockActivationCommand[] remainsCmds =
    {
        new BlockActivationCommand(CmdSearch, "search", true)
    };

    public override bool HasBlockActivationCommands(WorldBase _world, BlockValue _blockValue, Vector3i _blockPos, EntityAlive _entityFocusing)
    {
        return true;
    }

    public override BlockActivationCommand[] GetBlockActivationCommands(WorldBase _world, BlockValue _blockValue, Vector3i _blockPos, EntityAlive _entityFocusing)
    {
        remainsCmds[0].enabled = true;
        return remainsCmds;
    }

    public override string GetActivationText(WorldBase _world, BlockValue _blockValue, Vector3i _blockPos, EntityAlive _entityFocusing)
    {
        return Localization.Get("scEncRemainsText");
    }

    public override bool OnBlockActivated(string _commandName, WorldBase _world, Vector3i _blockPos, BlockValue _blockValue, EntityPlayerLocal _player)
    {
        try
        {
            if (_commandName == CmdSearch)
            {
                SurvivorCommunity.ScEncSearch.SearchAt(_blockPos, _player);
                return true;
            }
        }
        catch (Exception e)
        {
            SurvivorCommunity.ScLog.Once("remains-activate", e);
        }
        return false;
    }
}
