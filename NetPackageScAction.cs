using System;
using System.IO;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round MP2: one request, client to server (ScActions.Request). The server answers every one with NetPackageScResult.
    /// The acting player is the package's Sender - never a player id from the payload.
    /// Layout:
    ///   int32  request    the client's request id (echoed in the answer)
    ///   int32  community  Stage 1: always 0 (ScPlayers.SharedCommunity). Round MP6 (core): the community of the client's copy
    ///                     (ScRegistry.CurrentId) - a staleness check only: the server acts in the sender's own community and refuses
    ///                     a mismatch (scActionCommunityChanged). The layout is unchanged.
    ///   string op         the action's name (ScActionAttribute)
    ///   byte   n          then n int32s: the whole-number arguments
    ///   byte   m          then m strings: the string arguments
    /// Everything is captured in Setup; write() only copies.
    /// </summary>
    public class NetPackageScAction : NetPackage
    {
        /// <summary>
        /// A string argument is cut to this many characters (names, keys and labels are far shorter; round MP3: an item stack
        /// encoded by ScItemTx is at most ScItemTx.MaxEncoded, 4000).
        /// </summary>
        private const int MaxString = 4096;
        /// <summary>The op's name is cut to this many characters (the review's m4: the longest is about 20).</summary>
        private const int MaxOp = 64;

        private int request;
        private int community;
        private string op = "";
        private int[] ints;
        private string[] strs;

        public override NetPackageDirection PackageDirection { get { return NetPackageDirection.ToServer; } }

        public NetPackageScAction Setup(int _request, int _community, string _op, int[] _ints, string[] _strs)
        {
            request = _request;
            community = _community;
            op = _op ?? "";
            int n = _ints != null ? Math.Min(_ints.Length, 255) : 0;
            ints = new int[n];
            if (n > 0) Array.Copy(_ints, ints, n);
            int m = _strs != null ? Math.Min(_strs.Length, 255) : 0;
            strs = new string[m];
            for (int i = 0; i < m; i++)
            {
                string s = _strs[i] ?? "";
                strs[i] = s.Length > MaxString ? s.Substring(0, MaxString) : s;
            }
            return this;
        }

        public override void read(PooledBinaryReader _reader)
        {
            BinaryReader r = _reader;
            request = r.ReadInt32();
            community = r.ReadInt32();
            op = r.ReadString();
            if (op.Length > MaxOp) op = op.Substring(0, MaxOp);
            int n = r.ReadByte();
            ints = new int[n];
            for (int i = 0; i < n; i++) ints[i] = r.ReadInt32();
            int m = r.ReadByte();
            strs = new string[m];
            for (int i = 0; i < m; i++)
            {
                string s = r.ReadString();
                strs[i] = s.Length > MaxString ? s.Substring(0, MaxString) : s;
            }
        }

        public override void write(PooledBinaryWriter _writer)
        {
            base.write(_writer);
            BinaryWriter w = _writer;
            w.Write(request);
            w.Write(community);
            w.Write(op ?? "");
            int[] a = ints ?? new int[0];
            w.Write((byte)a.Length);
            for (int i = 0; i < a.Length; i++) w.Write(a[i]);
            string[] s = strs ?? new string[0];
            w.Write((byte)s.Length);
            for (int i = 0; i < s.Length; i++) w.Write(s[i] ?? "");
        }

        public override void ProcessPackage(World _world, GameManager _callbacks)
        {
            try
            {
                ConnectionManager cm = ScNet.Cm;
                if (cm == null || !cm.IsServer) return;          // a request never lands on a client
                ScActions.OnRequest(Sender, request, community, op, ints, strs);
            }
            catch (Exception e)
            {
                ScLog.Once("net-action", e);
            }
        }
    }
}
