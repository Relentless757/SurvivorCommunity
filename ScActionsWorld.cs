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
    ///                                search whose remains lie there turns to "Return to ..." (ScEncSearch.SearchAt). A remote
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
            // a remote sender's tell is worked out here, from the search as it stood before (SearchAt tells only this machine's
            // own player while it takes EntityPlayerLocal; the host's own player is told inside it, exactly as before)
            EnclaveRequest r = local == null ? RemainsRequest(pos) : null;
            int stage = r != null ? r.Stage : -1;
            ScEncSearch.SearchAt(pos, local);       // MP5 merge: ScEncSearch.SearchAt takes EntityPlayerLocal (its tells)
            if (local == null && sender != null) TellRemote(sender, r, stage);
            reply.Done();
        }

        /// <summary>ScEncSearch.RemainsAt's own rule: the open search whose remains lie here (dead or searched), or null.</summary>
        private static EnclaveRequest RemainsRequest(Vector3i pos)
        {
            foreach (EnclaveRequest q in ScRegistry.EncRequests)
                if (q.Kind == EnclaveRequest.KindMissing && q.IsAccepted && q.HasSpot && q.Spot == pos
                    && (q.Stage == ScEncSearch.StageDead || q.Stage == ScEncSearch.StageSearched)) return q;
            return null;
        }

        /// <summary>SearchAt's three tells, for a remote sender: nobody's remains, searched already, or found now.</summary>
        private static void TellRemote(EntityPlayer to, EnclaveRequest r, int stageBefore)
        {
            try
            {
                if (r == null) { ScCommunity.TellKey(to, "scEncRemainsNothing", ""); return; }
                EncPerson p = ScEncSearch.PersonById(r.PersonId);
                Enclave e = ScEnclaves.ById(r.EnclaveId);
                string name = p != null ? p.Name : ScEncSearch.PersonName(r), enc = e != null ? e.Name : "";
                ScCommunity.TellKey(to, stageBefore == ScEncSearch.StageSearched ? "scEncRemainsSearched" : "scEncRemainsFound", "", name, enc);
            }
            catch (Exception ex)
            {
                ScLog.Once("actions-remains-tell", ex);
            }
        }
    }
}
