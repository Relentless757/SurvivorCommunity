using System;
using UnityEngine.Scripting;

// Block classes resolve as "Block" + the Class property, so blocks.xml Class="ScWatchPost, SurvivorCommunity" is this
// class in the global namespace (as BlockScRadio).

/// <summary>
/// The Watch post (base defence D3, SurvivorCommunity.ScWatch): a full cube a guard stands on. Placing it inside the home
/// zone registers it (at most WatchMaxPosts; the placement is refused elsewhere, with the reason on the press); its radial
/// offers "Assign guard" (the community window picks one), "Relieve guard" and vanilla's pickup; breaking or picking it up
/// ends its guard's watch. The facing is the block's rotation (Place=TowardsPlacer: the way the player looked).
/// </summary>
[Preserve]
public class BlockScWatchPost : Block
{
    private const string CmdAssign = "scwatchassign", CmdRelieve = "scwatchrelieve", CmdTake = "take";

    private readonly BlockActivationCommand[] watchCmds =
    {
        new BlockActivationCommand(CmdAssign, "talk", false),
        new BlockActivationCommand(CmdRelieve, "x", false),
        new BlockActivationCommand(CmdTake, "hand", false)
    };

    public override bool HasBlockActivationCommands(WorldBase _world, BlockValue _blockValue, Vector3i _blockPos, EntityAlive _entityFocusing)
    {
        return true;
    }

    public override BlockActivationCommand[] GetBlockActivationCommands(WorldBase _world, BlockValue _blockValue, Vector3i _blockPos, EntityAlive _entityFocusing)
    {
        try
        {
            bool post = SurvivorCommunity.ScCommunity.IsHost && SurvivorCommunity.ScWatch.IsPost(_blockPos);
            bool manned = post && SurvivorCommunity.ScWatch.GuardOf(_blockPos) != null;
            watchCmds[0].enabled = post && !manned;
            watchCmds[1].enabled = manned;
            watchCmds[2].enabled = CanPickup && _world.CanPickupBlockAt(_blockPos, _world.GetGameManager().GetPersistentLocalPlayer());
        }
        catch (Exception e)
        {
            SurvivorCommunity.ScLog.Once("watch-cmds", e);
        }
        return watchCmds;
    }

    public override string GetActivationText(WorldBase _world, BlockValue _blockValue, Vector3i _blockPos, EntityAlive _entityFocusing)
    {
        return SurvivorCommunity.ScWatch.ActivationText(_blockPos);
    }

    public override bool OnBlockActivated(string _commandName, WorldBase _world, Vector3i _blockPos, BlockValue _blockValue, EntityPlayerLocal _player)
    {
        try
        {
            switch (_commandName)
            {
                case CmdAssign: SurvivorCommunity.ScWatch.OpenAssign(_blockPos, _player); return true;
                case CmdRelieve: SurvivorCommunity.ScWatch.RelieveAt(_blockPos, _player); return true;
                case CmdTake: OnBlockActivated(_world, _blockPos, _blockValue, _player); return true;   // vanilla's pickup (CanPickup)
            }
        }
        catch (Exception e)
        {
            SurvivorCommunity.ScLog.Once("watch-activate", e);
        }
        return false;
    }

    /// <summary>
    /// Inside the home zone and under the cap (host only; a client and WatchPosts off place it anywhere). The preview calls
    /// this every frame and turns red; the reason shows only on a real placement (the press, stamped by Patches_Outposts).
    /// </summary>
    public override bool CanPlaceBlockAt(WorldBase _world, Vector3i _blockPos, BlockValue _blockValue, bool _bOmitCollideCheck = false)
    {
        if (!base.CanPlaceBlockAt(_world, _blockPos, _blockValue, _bOmitCollideCheck)) return false;
        try
        {
            string why = SurvivorCommunity.ScWatch.PlaceRefusal(_blockPos);
            if (why == null) return true;
            EntityPlayerLocal pressing = SurvivorCommunity.Patches_Outposts.PressingPlayer();
            if (pressing != null) SurvivorCommunity.ScWatch.TellRefusal(pressing, why);
            return false;
        }
        catch (Exception e)
        {
            SurvivorCommunity.ScLog.Once("watch-canplace", e);
            return true;
        }
    }

    /// <summary>Placed: registered at once (host, main thread; not for a block a chunk load brings back).</summary>
    public override void OnBlockAdded(WorldBase _world, Chunk _chunk, Vector3i _blockPos, BlockValue _blockValue, PlatformUserIdentifierAbs _addedByPlayer)
    {
        base.OnBlockAdded(_world, _chunk, _blockPos, _blockValue, _addedByPlayer);
        try
        {
            if (_blockValue.ischild || !ThreadManager.IsMainThread() || !SurvivorCommunity.ScCommunity.IsHost) return;
            SurvivorCommunity.ScWatch.NotePost(_blockPos, _addedByPlayer != null);
        }
        catch (Exception e)
        {
            SurvivorCommunity.ScLog.Once("watch-added", e);
        }
    }

    /// <summary>Broken or picked up: forgotten, and its guard goes back to their post.</summary>
    public override void OnBlockRemoved(WorldBase _world, Chunk _chunk, Vector3i _blockPos, BlockValue _blockValue)
    {
        base.OnBlockRemoved(_world, _chunk, _blockPos, _blockValue);
        try
        {
            if (_blockValue.ischild || !ThreadManager.IsMainThread() || !SurvivorCommunity.ScCommunity.IsHost) return;
            SurvivorCommunity.ScWatch.ForgetPost(_blockPos, "was broken or picked up");
        }
        catch (Exception e)
        {
            SurvivorCommunity.ScLog.Once("watch-removed", e);
        }
    }
}
