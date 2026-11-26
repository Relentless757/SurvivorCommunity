using System;
using System.IO;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round MP1 (part mirror): the community's data, server to client (ScMirror). The server sends it only to remote clients
    /// that said hello with SC's protocol (ScNet.ClientMatches); a client never sends it, and one that arrives on a server is
    /// ignored.
    /// Layout:
    ///   byte   version    2 (round MP6 (part N): the community id below; the MP1 layout had no version byte - its first byte was
    ///                     the flags)
    ///   byte   flags      1 = a snapshot follows (else the derived block alone)
    ///   int32  community  round MP6 (part N): the community the snapshot and the derived block are of - the client's own, the
    ///                     server's truth (ScRegistry.MirroredCommunity on the client; its copy is parsed into it)
    ///   int32  length     (with flag 1) the snapshot's byte count
    ///   bytes  snapshot   (with flag 1) ScRegistry.BuildText(true, community), UTF-8, gzip'd
    ///   string derived    the derived block: key=value lines (ScMirror.BuildDerived(community))
    /// Everything is captured in Setup: write() runs on a network thread, once per recipient, and only copies.
    /// </summary>
    public class NetPackageScState : NetPackage
    {
        private const byte HasSnapshot = 1;
        /// <summary>Round MP6 (part N): this layout's version (2: the community id). A later layout bumps it, and ScNet.Protocol.</summary>
        private const byte Version = 2;
        /// <summary>A length past this is a corrupt or foreign package (the sender caps a snapshot well below it).</summary>
        private const int MaxRead = 4 * 1024 * 1024;

        private byte[] snapshot;
        private string derived = "";
        /// <summary>Round MP6 (part N): the community the snapshot and the derived block are of.</summary>
        private int community;

        public override NetPackageDirection PackageDirection { get { return NetPackageDirection.ToClient; } }

        /// <summary>gz: the gzip'd snapshot, or null for the derived block alone. The array is shared, never changed after this.</summary>
        public NetPackageScState Setup(byte[] gz, string derivedBlock)
        {
            return Setup(ScPlayers.SharedCommunity, gz, derivedBlock);
        }

        /// <summary>Round MP6 (part N): the same, of the given community (the recipient's own).</summary>
        public NetPackageScState Setup(int _community, byte[] gz, string derivedBlock)
        {
            community = _community;
            snapshot = gz;
            derived = derivedBlock ?? "";
            return this;
        }

        public override void read(PooledBinaryReader _reader)
        {
            BinaryReader r = _reader;
            byte version = r.ReadByte();      // round MP6 (part N): 2 (a peer with another layout speaks another protocol: never sent one)
            if (version != Version) throw new InvalidDataException("NetPackageScState: layout version " + version);
            byte flags = r.ReadByte();
            community = r.ReadInt32();        // round MP6 (part N)
            snapshot = null;
            if ((flags & HasSnapshot) != 0)
            {
                int n = r.ReadInt32();
                if (n < 0 || n > MaxRead) throw new InvalidDataException("NetPackageScState: snapshot length " + n);
                snapshot = r.ReadBytes(n);
            }
            derived = r.ReadString();
        }

        public override void write(PooledBinaryWriter _writer)
        {
            base.write(_writer);
            BinaryWriter w = _writer;
            byte[] s = snapshot;
            w.Write(Version);                 // round MP6 (part N)
            w.Write((byte)(s != null ? HasSnapshot : 0));
            w.Write(community);               // round MP6 (part N)
            if (s != null)
            {
                w.Write(s.Length);
                w.Write(s);
            }
            w.Write(derived ?? "");
        }

        public override void ProcessPackage(World _world, GameManager _callbacks)
        {
            try
            {
                ConnectionManager cm = ScNet.Cm;
                if (cm == null || cm.IsServer) return;          // the server's registry is the truth: a state package never lands there
                ScMirror.Receive(community, snapshot, derived);     // round MP6 (part N): with its community
            }
            catch (Exception e)
            {
                ScLog.Once("net-state", e);
            }
        }
    }
}
