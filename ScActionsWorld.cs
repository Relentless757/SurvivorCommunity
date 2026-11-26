using System;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round MP5 (multiplayer, part W: the world around any player): the server's handlers for the world's own requests
    /// (ScActions' [ScAction] ops). The asking code calls ScActions.Request on every machine; on the server (single player, the
    /// listen host's own player) the handler runs inline with the local player, the same path as before.
    ///
    /// Most of part W needs no request: discovery, bands meeting and engaging, a band's halt for a talk, arrivals, raids, moves
    /// and the home's fights are judged on the server around every community player (ScEnclaves, ScInfestations, ScBands,
    /// ScEncJoin, ScBaseAttack). A band's talk rows already go through the talk context and band.join (round MP2, part M); the
    /// band halts for a remote player's talk through the server's talk lock (ScBands.InTalk).
    ///
    /// Ops (ints):
    ///   encremains.search  x, y, z   "Search the body" on a missing survivor's remains (BlockScEncRemains, E5): the open
    ///                                search whose remains lie there turns to "Return to ..." (ScEncSearch.SearchAtFor). A remote
    ///                                sender must stand within RemainsReach of the block, and the block must be the remains.
    /// </summary>
    public static class ScActionsWorld
    {
        /// <summary>A remote "Search the body" farther than this (meters) from the block is refused (activation reach plus slack for lag).</summary>
        public const float RemainsReach = 8f;

        [ScAction("encremains.search", ScActionAccess.Community)]
        private static void RemainsSearch(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            Vector3i pos = args.Pos(0);
            EntityPlayerLocal local = sender as EntityPlayerLocal;
            if (local == null)
            {
                World w = ScCommunity.World;
                if (w == null || sender == null || sender.IsDead() || !(w.GetBlock(pos).Block is BlockScEncRemains)) { reply.Fail(""); return; }   // gone meanwhile: silent
                Vector3 c = new Vector3(pos.x + 0.5f, pos.y + 0.5f, pos.z + 0.5f);
                if ((c - sender.position).sqrMagnitude > RemainsReach * RemainsReach) { reply.Fail(""); return; }
            }
            // round MP5 (merge): part E's SearchAtFor searches for any player and tells them itself (the host's own player
            // exactly as before) - the one "Search the body" path; part E's own enc.search op went with the merge
            ScEncSearch.SearchAtFor(pos, sender);
            reply.Done();
        }
    }
}
