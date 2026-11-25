using System;
using System.IO;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round MP4 (part Q): one quest op, server to one client (ScQuestNet). The client runs the existing ScQuests functions on
    /// its own journal - a journal is its own machine's (QuestJournal.AddQuest is local). The host's own player never gets one:
    /// its path is direct (ScNet.ToPlayer would run it locally; ScQuestNet sends to remote players only).
    /// Layout (all little-endian, BinaryWriter):
    ///   byte   version    1
    ///   byte   kind       0 favour close, 1 favour re-key, 2 rescue start, 3 rescue close, 4 rescue re-key,
    ///                     5 request start, 6 request shape, 7 request close
    ///   int32  id         the favour's prospect id (0; 1: the old one), the runner's member id (2, 3; 4: the old one), the
    ///                     request's id (5, 6, 7)
    ///   int32  to         a re-key's new id (1, 4); 0 otherwise
    ///   bool   completed  a close's outcome (0, 3, 7): completed, or removed as failed
    ///   byte   n          then n int32s:
    ///                       kind 2: the marker's box - corner x, y, z, size x, y, z
    ///                       kinds 5, 6: the place - corner x, y, z, size x, y, z; the due day; flags (1 one of E5's kinds,
    ///                       2 a search back from the remains: its "return" phase)
    ///   byte   m          then m strings (each cut to 512 characters):
    ///                       kind 2: the runner's name, the place's name
    ///                       kinds 5, 6: the quest class, the request's kind, the place's name, what is wanted (the server's
    ///                       words), the enclave's name, the person's name, the target's name, the stage word
    /// Titles are made on the client, in its language. Everything is captured in Setup; write() only copies (it runs on a
    /// network thread).
    /// </summary>
    public class NetPackageScQuestOp : NetPackage
    {
        public const byte KindFavourClose = 0, KindFavourRekey = 1, KindRescueStart = 2, KindRescueClose = 3, KindRescueRekey = 4,
                          KindRequestStart = 5, KindRequestShape = 6, KindRequestClose = 7;
        private const byte Version = 1;
        private const int MaxString = 512;

        private byte kind;
        private int id, to;
        private bool completed;
        private int[] ints;
        private string[] strs;

        public override NetPackageDirection PackageDirection { get { return NetPackageDirection.ToClient; } }

        public NetPackageScQuestOp Setup(byte _kind, int _id, int _to, bool _completed, int[] _ints, string[] _strs)
        {
            kind = _kind;
            id = _id;
            to = _to;
            completed = _completed;
            int n = _ints != null ? Math.Min(_ints.Length, 255) : 0;
            ints = new int[n];
            if (n > 0) Array.Copy(_ints, ints, n);
            int m = _strs != null ? Math.Min(_strs.Length, 255) : 0;
            strs = new string[m];
            for (int i = 0; i < m; i++) strs[i] = Cut(_strs[i]);
            return this;
        }

        private static string Cut(string s)
        {
            if (s == null) return "";
            return s.Length > MaxString ? s.Substring(0, MaxString) : s;
        }

        public override void read(PooledBinaryReader _reader)
        {
            BinaryReader r = _reader;
            r.ReadByte();                     // version (1): a later layout bumps ScNet.Protocol as well
            kind = r.ReadByte();
            id = r.ReadInt32();
            to = r.ReadInt32();
            completed = r.ReadBoolean();
            int n = r.ReadByte();
            ints = new int[n];
            for (int i = 0; i < n; i++) ints[i] = r.ReadInt32();
            int m = r.ReadByte();
            strs = new string[m];
            for (int i = 0; i < m; i++) strs[i] = Cut(r.ReadString());
        }

        public override void write(PooledBinaryWriter _writer)
        {
            base.write(_writer);
            BinaryWriter w = _writer;
            w.Write(Version);
            w.Write(kind);
            w.Write(id);
            w.Write(to);
            w.Write(completed);
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
                if (cm == null || cm.IsServer) return;          // an op never lands on the server
                ScQuestNet.OnOp(kind, id, to, completed, ints, strs);
            }
            catch (Exception e)
            {
                ScLog.Once("net-questop", e);
            }
        }
    }
}
