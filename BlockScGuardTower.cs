using System;
using UnityEngine.Scripting;

// Block classes resolve as "Block" + the Class property, so blocks.xml Class="ScGuardTower, SurvivorCommunity" is this
// class in the global namespace (as BlockScRadio and BlockScWatchPost).

/// <summary>
/// The Guard Tower kit (SurvivorCommunity.ScTower): placed like any block - the ghost of the whole tower follows the
/// cursor, R turns it, red when it can't go there (the reason on the press) - and the server puts the tower up in its
/// place a frame later. Once up, the tower is ordinary blocks; its Watch post registers as a hand-placed one does.
/// </summary>
[Preserve]
public class BlockScGuardTower : Block
{
    /// <summary>The whole tower must fit (ScTower.Check): the vanilla ghost asks every frame, the press once.</summary>
    public override bool CanPlaceBlockAt(WorldBase _world, Vector3i _blockPos, BlockValue _blockValue, bool _bOmitCollideCheck = false)
    {
        if (!base.CanPlaceBlockAt(_world, _blockPos, _blockValue, _bOmitCollideCheck)) return false;
        try
        {
            World w = _world as World;
            if (w == null) return true;
            EntityPlayerLocal pressing = SurvivorCommunity.Patches_Outposts.PressingPlayer();
            string arg;
            string key = SurvivorCommunity.ScTower.Refusal(w, _blockPos, _blockValue.rotation, pressing != null, out arg);
            if (key == null) return true;
            if (pressing != null) SurvivorCommunity.ScTower.Tell(pressing, SurvivorCommunity.ScTower.Text(key, arg), true);
            return false;
        }
        catch (Exception e)
        {
            SurvivorCommunity.ScLog.Once("tower-canplace", e);
            return true;
        }
    }

    /// <summary>Placed by a player: the server builds the tower here (ScTower.Expand checks again first).</summary>
    public override void OnBlockAdded(WorldBase _world, Chunk _chunk, Vector3i _blockPos, BlockValue _blockValue, PlatformUserIdentifierAbs _addedByPlayer)
    {
        base.OnBlockAdded(_world, _chunk, _blockPos, _blockValue, _addedByPlayer);
        try
        {
            if (_blockValue.ischild || _addedByPlayer == null || !ThreadManager.IsMainThread()) return;
            ConnectionManager cm = SingletonMonoBehaviour<ConnectionManager>.Instance;
            if (cm == null || !cm.IsServer) return;
            SurvivorCommunity.ScTower.Expand(_blockPos, _blockValue, _addedByPlayer);
        }
        catch (Exception e)
        {
            SurvivorCommunity.ScLog.Once("tower-added", e);
        }
    }
}
