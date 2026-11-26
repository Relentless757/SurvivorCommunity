using System;
using System.Collections.Generic;
using System.IO;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round MP4 (review cfs1 M1): the home zone's linked storage boxes that someone has open on the server (a player's loot window,
    /// the host's own included: ScStorageServer.OpenBySomeone), server to client. A client crafting from storage
    /// (ScCraftFromBase) leaves those boxes out: a client cannot see another player's open (0-SCore's own test is always false
    /// there), and its craft would overwrite the box on the server while the player with it open writes their old copy back on
    /// close - the materials would come back. The server sends it only to remote clients that said hello with SC's protocol
    /// (ScNet.ClientMatches), the whole set each time it changes (at most once a second, ScTicker) and to a client that has not
    /// had the current set yet. A client never sends it, and one that arrives on a server is ignored.
    /// Layout:
    ///   int32  community  the community the set is for (Stage 1: always 0; round MP6 (part N): the recipient's own - the open boxes
    ///                     of its own community's home; layout unchanged)
    ///   int32  n          then n box positions (n at most MaxBoxes):
    ///     int32  x, y, z    the box's block position
    /// Everything is captured in Setup: write() runs on a network thread, once per recipient, and only copies.
    /// </summary>
    public class NetPackageScOpenBoxes : NetPackage
    {
        /// <summary>A count past this is a corrupt or foreign package (the server caps the set it sends at this).</summary>
        public const int MaxBoxes = 512;

        private int community;
        private Vector3i[] boxes = new Vector3i[0];

        public override NetPackageDirection PackageDirection { get { return NetPackageDirection.ToClient; } }

        /// <summary>The positions are copied (the list is the server's own, kept for the next comparison).</summary>
        public NetPackageScOpenBoxes Setup(int _community, ICollection<Vector3i> open)
        {
            community = _community;
            if (open == null || open.Count == 0) { boxes = new Vector3i[0]; return this; }
            var list = new List<Vector3i>(Math.Min(open.Count, MaxBoxes));
            foreach (Vector3i v in open)
            {
                if (list.Count >= MaxBoxes) break;
                list.Add(v);
            }
            boxes = list.ToArray();
            return this;
        }

        public override void read(PooledBinaryReader _reader)
        {
            BinaryReader r = _reader;
            community = r.ReadInt32();
            int n = r.ReadInt32();
            if (n < 0 || n > MaxBoxes) throw new InvalidDataException("NetPackageScOpenBoxes: " + n + " boxes");
            var list = new Vector3i[n];
            for (int i = 0; i < n; i++)
            {
                int x = r.ReadInt32(), y = r.ReadInt32(), z = r.ReadInt32();
                list[i] = new Vector3i(x, y, z);
            }
            boxes = list;
        }

        public override void write(PooledBinaryWriter _writer)
        {
            base.write(_writer);
            BinaryWriter w = _writer;
            Vector3i[] list = boxes ?? new Vector3i[0];
            w.Write(community);
            w.Write(list.Length);
            for (int i = 0; i < list.Length; i++)
            {
                w.Write(list[i].x);
                w.Write(list[i].y);
                w.Write(list[i].z);
            }
        }

        public override void ProcessPackage(World _world, GameManager _callbacks)
        {
            try
            {
                ConnectionManager cm = ScNet.Cm;
                if (cm == null || cm.IsServer) return;          // the server knows its own opens: this never lands there
                ScCraftFromBase.ReceiveOpen(community, boxes);
            }
            catch (Exception e)
            {
                ScLog.Once("net-open-boxes", e);
            }
        }
    }
}
