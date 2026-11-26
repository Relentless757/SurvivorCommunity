using System;
using System.IO;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round MP4 (part V): server to client - one of SC's spoken lines, so every player in earshot hears the same line in the
    /// same voice (ScVoice, "SC's lines on every machine"). The server decides that the line is said, which line (the clip's
    /// key) and in which voice (the voice folder's name: every machine resolves the file in its own copy of the mod, so no
    /// path travels), and to whom: every matched player within earshot of the speaker (ScVoice.EarshotRange, 40 m - the
    /// distance SC's and Voice Control's 3D voice fades out at; Voice Control's own relay range, 32 m, for a line SC said in
    /// Voice Control's place), or one player (a greeting, a talk's line, a radio line). The client plays it with that voice
    /// (ScVoice.PlayBark, the talk's SayAfterTalk, or its own radio queue, ScRadioTalk) and shows the caption in its own
    /// language where the caption is a key. The host's own player never gets one: the host plays its lines as it always did.
    /// Layout (BinaryWriter):
    ///   byte   version   1
    ///   byte   kind      0 bark (chatter, a spoken refusal), 1 talk (a line said as a talk closes: to the talking player only),
    ///                    2 small talk, 3 greeting (to the greeted player only), 4 taunt, 5 horde (a guard's warning in person),
    ///                    6 radio (a runner's or a radio speaker's line: the client's own hearing rule decides), 7 vc (a line of
    ///                    Voice Control's that SC said in its place: the relay Voice Control would have sent)
    ///   byte   flags     bit 0 lip sync (through Voice Control's player where it has one), bit 1 allow dead (a dying raider's
    ///                    last words), bit 2 forced (the console: the hearing rule ignored), bit 3 no handheld needed (a recall's
    ///                    answer: heard on the radio that sent it), bit 4 a runner's line (the radio's caption "{0} on the radio:"),
    ///                    bit 5 in person allowed (a radio speaker whose body may stand near the player), bit 6 a horde warning
    ///                    that may go on the player's radio (ScRadioTalk.FlagHordeRadio; round MP5, part R - the layout is
    ///                    unchanged, the protocol counts it)
    ///   int32  npc       the speaker's entity id (-1: no body - a voice on the radio from afar)
    ///   string key       the clip's key: its file name in the voice folder (greet.member.3, scHordeGather_north)
    ///   string voice     the voice folder (male_White_Vane); "" for a line with no recording (its caption alone)
    ///   byte   caption   0 none, 1 SC's words for the key (ScBarks.WordsOf on the client), 2 text as it is (Voice Control's
    ///                    caption, in the server's language), 3 a Localization key, localized on the client (with the arguments)
    ///   string captionKey
    ///   byte   argc, then argc strings: the caption's arguments
    ///   string name      the speaker's name (the caption's name)
    ///   byte   priority  RadioPriority (radio and horde lines)
    ///   string tag       the radio counters' word ("enc", "horde"; "" for a runner)
    ///   float  range     the meters the server measured earshot by (0: sent to one player)
    /// Everything is captured in Setup (a copy of the line); write() only copies (once per recipient, on a network thread).
    /// A line is a few hundred bytes. ScNet.Protocol counts this package from round MP4 on (the merge sets it).
    /// </summary>
    public class NetPackageScVoice : NetPackage
    {
        public const byte KindBark = 0, KindTalk = 1, KindSmallTalk = 2, KindGreeting = 3, KindTaunt = 4, KindHorde = 5, KindRadio = 6, KindVc = 7;
        public const byte FlagLipSync = 1, FlagAllowDead = 2, FlagForced = 4, FlagNoHandheld = 8, FlagRunner = 16, FlagInPerson = 32;
        public const byte CaptionNone = 0, CaptionWords = 1, CaptionText = 2, CaptionLoc = 3;
        private const byte Version = 1;
        /// <summary>A string is cut to this many characters (keys, names and captions are far shorter).</summary>
        private const int MaxString = 512;
        private const int MaxArgs = 8;

        /// <summary>One spoken line, as the server decided it. Plain data, copied for each package.</summary>
        public sealed class Line
        {
            public byte Kind;
            public byte Flags;
            public int Npc = -1;
            public string Key = "";
            public string Voice = "";
            public byte Caption;
            public string CaptionKey = "";
            public string[] Args;
            public string Name = "";
            public byte Priority;
            public string Tag = "";
            public float Range;

            public bool Has(byte flag) { return (Flags & flag) != 0; }

            public Line Copy()
            {
                Line l = (Line)MemberwiseClone();
                l.Args = Args != null ? (string[])Args.Clone() : null;
                return l;
            }

            /// <summary>For the log: "greeting greet.member.3 in male_White_Vane by 1234".</summary>
            public override string ToString()
            {
                return KindName(Kind) + " " + Key + (string.IsNullOrEmpty(Voice) ? " (caption only)" : " in " + Voice) + (Npc >= 0 ? " by " + Npc : "");
            }
        }

        public static string KindName(byte kind)
        {
            switch (kind)
            {
                case KindBark: return "bark";
                case KindTalk: return "talk";
                case KindSmallTalk: return "small talk";
                case KindGreeting: return "greeting";
                case KindTaunt: return "taunt";
                case KindHorde: return "horde";
                case KindRadio: return "radio";
                case KindVc: return "vc";
            }
            return "kind " + kind;
        }

        private Line line = new Line();

        public override NetPackageDirection PackageDirection { get { return NetPackageDirection.ToClient; } }

        public NetPackageScVoice Setup(Line l)
        {
            line = l != null ? l.Copy() : new Line();
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
            var l = new Line();
            r.ReadByte();                     // version (1): a later layout bumps ScNet.Protocol as well
            l.Kind = r.ReadByte();
            l.Flags = r.ReadByte();
            l.Npc = r.ReadInt32();
            l.Key = r.ReadString();
            l.Voice = r.ReadString();
            l.Caption = r.ReadByte();
            l.CaptionKey = r.ReadString();
            int argc = r.ReadByte();
            l.Args = new string[argc];
            for (int i = 0; i < argc; i++) l.Args[i] = r.ReadString();
            l.Name = r.ReadString();
            l.Priority = r.ReadByte();
            l.Tag = r.ReadString();
            l.Range = r.ReadSingle();
            line = l;
        }

        public override void write(PooledBinaryWriter _writer)
        {
            base.write(_writer);
            BinaryWriter w = _writer;
            Line l = line ?? new Line();
            w.Write(Version);
            w.Write(l.Kind);
            w.Write(l.Flags);
            w.Write(l.Npc);
            w.Write(Cut(l.Key));
            w.Write(Cut(l.Voice));
            w.Write(l.Caption);
            w.Write(Cut(l.CaptionKey));
            int argc = l.Args != null ? Math.Min(l.Args.Length, MaxArgs) : 0;
            w.Write((byte)argc);
            for (int i = 0; i < argc; i++) w.Write(Cut(l.Args[i]));
            w.Write(Cut(l.Name));
            w.Write(l.Priority);
            w.Write(Cut(l.Tag));
            w.Write(l.Range);
        }

        public override void ProcessPackage(World _world, GameManager _callbacks)
        {
            try
            {
                ConnectionManager cm = ScNet.Cm;
                if (cm == null || cm.IsServer) return;     // the server says its own lines
                ScVoice.OnNetLine(line);
            }
            catch (Exception e)
            {
                ScLog.Once("net-voice", e);
            }
        }
    }
}
