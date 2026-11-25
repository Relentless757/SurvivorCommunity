using System;
using System.IO;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round MP4 (part: tells): server to one client - one of SC's tells (ScCommunity.TellKey, ScPlayers.TellCommunityKey) as
    /// its localization key and arguments, so each player reads it in their own language. Before, a remote player got the
    /// server's finished words through GameManager.ShowTooltipMP (NetPackageShowToolbeltMessage: in the server's language, and
    /// that package plays no sound - it passes the sound as the tooltip's argument). The client formats the row exactly as the
    /// host does (ScCommunity.TellText: string.Format(Localization.Get(key), args), a key argument localized first) and shows
    /// it as SC shows a tell (ScCommunity.Tell: the tooltip with its sound). Sent only to a client that said hello with SC's
    /// protocol (ScNet.ClientMatches); the host's own player never gets one (ScNet.ToPlayer runs nothing for it: TellKey shows
    /// the host's tells itself). Anyone else - a vanilla client, another SC protocol, more than MaxArgs arguments - gets the
    /// server's words as before.
    /// Layout (BinaryWriter):
    ///   byte   version  1
    ///   string key      the tell's localization key (scRunBack)
    ///   string sound    the tooltip's sound ("" none, ui_denied)
    ///   byte   argc     0..MaxArgs (8), then argc arguments, each:
    ///     byte kind     0 text as it is (a name; a helper's words, in the server's language), 1 a localization key (a resource
    ///                   name, a status word: localized on the client), 2 int32, 3 int64, 4 float, 5 double
    ///     the value     string (0, 1), int32 (2), int64 (3), single (4), double (5)
    /// Numbers travel as numbers, so the client's string.Format applies the row's own format ({2:0.0}) exactly as the host's
    /// does; any other value travels as string.Format shows it on the server. Strings are cut to 2048 characters (a tell is a
    /// line). Everything is captured in Setup (plain values only); write() only copies (once per recipient, on a network thread).
    /// ScNet.Protocol counts this package from round MP4 on (protocol 5).
    /// </summary>
    public class NetPackageScTell : NetPackage
    {
        /// <summary>The most arguments a tell carries (the longest row today takes 5).</summary>
        public const int MaxArgs = 8;
        private const byte Version = 1;
        private const int MaxString = 2048;
        private const byte ArgText = 0, ArgKey = 1, ArgInt = 2, ArgLong = 3, ArgFloat = 4, ArgDouble = 5;

        /// <summary>
        /// A tell's argument that is itself a localization key (ScCommunity.LocArg): localized on the machine that shows the tell.
        /// ToString is that machine's words, exactly Localization.Get(Key) as the old call sites passed it.
        /// </summary>
        public sealed class LocKey
        {
            public readonly string Key;
            public LocKey(string key) { Key = key ?? ""; }
            public override string ToString() { return Localization.Get(Key); }
        }

        private string key = "";
        private string sound = "";
        private byte[] kinds = new byte[0];
        private object[] values = new object[0];

        public override NetPackageDirection PackageDirection { get { return NetPackageDirection.ToClient; } }

        /// <summary>Whether a tell with these arguments fits the package (else ScCommunity.TellKey sends the server's words).</summary>
        public static bool Fits(object[] args)
        {
            return args == null || args.Length <= MaxArgs;
        }

        public NetPackageScTell Setup(string _key, string _sound, object[] _args)
        {
            key = Cut(_key);
            sound = Cut(_sound);
            int n = _args != null ? Math.Min(_args.Length, MaxArgs) : 0;
            kinds = new byte[n];
            values = new object[n];
            for (int i = 0; i < n; i++) Capture(_args[i], out kinds[i], out values[i]);
            return this;
        }

        /// <summary>One argument as the wire carries it: a key, a number as a number, anything else as string.Format shows it.</summary>
        private static void Capture(object a, out byte kind, out object value)
        {
            LocKey lk = a as LocKey;
            if (lk != null) { kind = ArgKey; value = Cut(lk.Key); return; }
            if (a is int || a is short || a is byte || a is sbyte || a is ushort) { kind = ArgInt; value = Convert.ToInt32(a); return; }
            if (a is uint || a is long) { kind = ArgLong; value = Convert.ToInt64(a); return; }
            if (a is ulong && (ulong)a <= long.MaxValue) { kind = ArgLong; value = (long)(ulong)a; return; }
            if (a is float) { kind = ArgFloat; value = (float)a; return; }
            if (a is double) { kind = ArgDouble; value = (double)a; return; }
            kind = ArgText;
            value = Cut(a == null ? "" : string.Format("{0}", a));
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
            key = r.ReadString();
            sound = r.ReadString();
            int n = r.ReadByte();
            int keep = Math.Min(n, MaxArgs);                 // review tells n7: the stream is read through, at most MaxArgs kept
            kinds = new byte[keep];
            values = new object[keep];
            for (int i = 0; i < n; i++)
            {
                byte k = r.ReadByte();
                object v;
                switch (k)
                {
                    case ArgInt: v = r.ReadInt32(); break;
                    case ArgLong: v = r.ReadInt64(); break;
                    case ArgFloat: v = r.ReadSingle(); break;
                    case ArgDouble: v = r.ReadDouble(); break;
                    default: v = r.ReadString(); break;      // ArgText, ArgKey
                }
                if (i >= keep) continue;
                kinds[i] = k;
                values[i] = v;
            }
        }

        public override void write(PooledBinaryWriter _writer)
        {
            base.write(_writer);
            BinaryWriter w = _writer;
            w.Write(Version);
            w.Write(key ?? "");
            w.Write(sound ?? "");
            int n = kinds != null && values != null ? Math.Min(Math.Min(kinds.Length, values.Length), MaxArgs) : 0;
            w.Write((byte)n);
            for (int i = 0; i < n; i++)
            {
                byte k = kinds[i];
                object v = values[i];
                switch (k)
                {
                    case ArgInt: w.Write(k); w.Write(v is int ? (int)v : 0); break;
                    case ArgLong: w.Write(k); w.Write(v is long ? (long)v : 0L); break;
                    case ArgFloat: w.Write(k); w.Write(v is float ? (float)v : 0f); break;
                    case ArgDouble: w.Write(k); w.Write(v is double ? (double)v : 0.0); break;
                    case ArgKey: w.Write(k); w.Write(v as string ?? ""); break;
                    default: w.Write(ArgText); w.Write(v as string ?? ""); break;
                }
            }
        }

        public override void ProcessPackage(World _world, GameManager _callbacks)
        {
            try
            {
                ConnectionManager cm = ScNet.Cm;
                if (cm == null || cm.IsServer || _world == null) return;     // the server shows its own tells
                EntityPlayerLocal p = _world.GetPrimaryPlayer();
                if (p == null) return;
                int n = kinds != null && values != null ? Math.Min(kinds.Length, values.Length) : 0;
                object[] args = new object[n];
                for (int i = 0; i < n; i++) args[i] = kinds[i] == ArgKey ? new LocKey(values[i] as string) : values[i];
                ScCommunity.ShowTellKey(p, key, sound, args);
            }
            catch (Exception e)
            {
                ScLog.Once("net-tell", e);
            }
        }
    }
}
