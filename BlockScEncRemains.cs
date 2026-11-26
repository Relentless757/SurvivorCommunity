using System;
using UnityEngine.Scripting;

// Block classes resolve as "Block" + the Class property, so blocks.xml Class="ScEncRemains, SurvivorCommunity" is this
// class in the global namespace (as BlockScRadio).

/// <summary>
/// E5, a missing survivor's remains (SurvivorCommunity.ScEncSearch): a corpse block the search places where they were found
/// dead (or fell before the player reached them), put back when something erased it, taken away when the search ends. Its one
/// command, "Search the body" (the BlockScRadio activation pattern), tells the search; nothing to pick up or harvest. Round MP5
/// (part W): any player's - the command asks the server (encremains.search, ScActionsWorld), inline for the host's own player.
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
                // round MP5 (part W): a request on every machine (encremains.search, ScActionsWorld) - inline on the server, where the
                // host's own player searches exactly as before (ScEncSearch.SearchAt); a client's goes to the server, which tells it
                EntityPlayerLocal pl = _player;
                SurvivorCommunity.ScActions.Request("encremains.search", r =>
                {
                    // a client's refusal it can read (no SC on the server, a mismatch); the host's path stays silent, as before
                    if (r != null && !r.Ok && !SurvivorCommunity.ScCommunity.IsHost && pl != null && r.Text.Length > 0)
                        GameManager.ShowTooltip(pl, r.Text, string.Empty, r.Sound);
                }, _blockPos.x, _blockPos.y, _blockPos.z);
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
