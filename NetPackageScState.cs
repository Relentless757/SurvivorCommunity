using System;
using System.IO;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round MP1 (part mirror): the community's data, server to client (ScMirror). The server sends it only to remote clients
    /// that said hello with SC's protocol (ScNet.ClientMatches); a client never sends it, and one that arrives on a server is
    /// ignored.
    /// Layout:
    ///   byte   flags      1 = a snapshot follows (else the derived block alone)
    ///   int32  length     (with flag 1) the snapshot's byte count
    ///   bytes  snapshot   (with flag 1) ScRegistry.BuildText(true), UTF-8, gzip'd
    ///   string derived    the derived block: key=value lines (ScMirror.BuildDerived)
    /// Everything is captured in Setup: write() runs on a network thread, once per recipient, and only copies.
    /// </summary>
    public class NetPackageScState : NetPackage
    {
        private const byte HasSnapshot = 1;
        /// <summary>A length past this is a corrupt or foreign package (the sender caps a snapshot well below it).</summary>
        private const int MaxRead = 4 * 1024 * 1024;

        private byte[] snapshot;
        private string derived = "";

        public override NetPackageDirection PackageDirection { get { return NetPackageDirection.ToClient; } }

        /// <summary>gz: the gzip'd snapshot, or null for the derived block alone. The array is shared, never changed after this.</summary>
        public NetPackageScState Setup(byte[] gz, string derivedBlock)
        {
            snapshot = gz;
            derived = derivedBlock ?? "";
            return this;
        }

        public override void read(PooledBinaryReader _reader)
        {
            BinaryReader r = _reader;
            byte flags = r.ReadByte();
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
            w.Write((byte)(s != null ? HasSnapshot : 0));
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
                ScMirror.Receive(snapshot, derived);
            }
            catch (Exception e)
            {
                ScLog.Once("net-state", e);
            }
        }
    }
}
