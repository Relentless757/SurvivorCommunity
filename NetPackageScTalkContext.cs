using System;
using System.IO;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round MP2 (part M): the talk context (ScTalkContext). Both directions, one byte saying which of three it is:
    ///   0 Open   client to server: a talk with an NPC opened on the client. The server rolls the profile and the favour (the
    ///            side effects a dialog requirement had on the host), and answers with 1.
    ///   1 Facts  server to that client: what the dialog's requirements need for this NPC that the client's copy of the registry
    ///            lacks (ScTalkFacts) - the recruit verdict, the home, the favour, the POI verdict, a band's answers.
    ///   2 Close  server to a client: close your favour quest for this prospect (the favour is shared: whoever joins them, a
    ///            death or a grudge ends everyone else's copy - the E 4.4 decision).
    /// Layout (all little-endian, BinaryWriter):
    ///   byte   version      1
    ///   byte   kind         0 | 1 | 2
    ///   int32  id           the NPC's entity id (0, 1), or the prospect id (2)
    ///   kind 0: byte flags  bit 0: this player's journal holds this NPC's POI favour quest past its clearing
    ///   kind 2: bool completed
    ///   kind 1 (ScTalkFacts):
    ///     byte   flags      bit 0 Recruit, 1 HasHome, 2 Started, 3 Done, 4 PoiCleared
    ///     string recruitWhy (a localization key or "")
    ///     byte   favour     FavourKind (0: no prospect)
    ///     string name       the prospect's name
    ///     int32  poiX, poiY, poiZ, poiWidth, poiDepth
    ///     string poiName, poiWhy
    ///     int32  itemCategory, itemCount, encPerson
    ///     byte   band       0 none, 1 wary, 2 trusted
    ///     byte   bandCome   0 not offered, 1 yes, 2 no
    ///     string bandWhere, bandJoinFacts, bandName, bandLeader (text in the server's language: MP4 moves tells to keys)
    ///     int32  bandCount
    /// Everything is captured in Setup*; write() only copies (it runs once per recipient on a network thread).
    /// </summary>
    public class NetPackageScTalkContext : NetPackage
    {
        public const byte KindOpen = 0, KindFacts = 1, KindClose = 2;
        private const byte Version = 1;
        /// <summary>A string is cut to this many characters (names and the band's facts are far shorter).</summary>
        private const int MaxString = 512;

        private byte kind;
        private int id;
        private byte openFlags;
        private bool completed;
        private ScTalkFacts facts;

        public NetPackageScTalkContext SetupOpen(int npcId, bool journalDone)
        {
            kind = KindOpen;
            id = npcId;
            openFlags = (byte)(journalDone ? 1 : 0);
            return this;
        }

        public NetPackageScTalkContext SetupFacts(ScTalkFacts f)
        {
            kind = KindFacts;
            facts = f != null ? f.Copy() : new ScTalkFacts();
            id = facts.Npc;
            return this;
        }

        public NetPackageScTalkContext SetupClose(int prospectId, bool _completed)
        {
            kind = KindClose;
            id = prospectId;
            completed = _completed;
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
            if (kind == KindOpen) { openFlags = r.ReadByte(); return; }
            if (kind == KindClose) { completed = r.ReadBoolean(); return; }
            var f = new ScTalkFacts { Npc = id };
            byte flags = r.ReadByte();
            f.Recruit = (flags & 1) != 0;
            f.HasHome = (flags & 2) != 0;
            f.Started = (flags & 4) != 0;
            f.Done = (flags & 8) != 0;
            f.PoiCleared = (flags & 16) != 0;
            f.RecruitWhy = Cut(r.ReadString());
            f.Favour = r.ReadByte();
            f.Name = Cut(r.ReadString());
            int x = r.ReadInt32(), y = r.ReadInt32(), z = r.ReadInt32();
            f.PoiPos = new Vector3i(x, y, z);
            f.PoiWidth = r.ReadInt32();
            f.PoiDepth = r.ReadInt32();
            f.PoiName = Cut(r.ReadString());
            f.PoiWhy = Cut(r.ReadString());
            f.ItemCategory = r.ReadInt32();
            f.ItemCount = r.ReadInt32();
            f.EncPerson = r.ReadInt32();
            f.Band = r.ReadByte();
            f.BandCome = r.ReadByte();
            f.BandWhere = Cut(r.ReadString());
            f.BandJoinFacts = Cut(r.ReadString());
            f.BandName = Cut(r.ReadString());
            f.BandLeader = Cut(r.ReadString());
            f.BandCount = r.ReadInt32();
            facts = f;
        }

        public override void write(PooledBinaryWriter _writer)
        {
            base.write(_writer);
            BinaryWriter w = _writer;
            w.Write(Version);
            w.Write(kind);
            w.Write(id);
            if (kind == KindOpen) { w.Write(openFlags); return; }
            if (kind == KindClose) { w.Write(completed); return; }
            ScTalkFacts f = facts ?? new ScTalkFacts();
            byte flags = 0;
            if (f.Recruit) flags |= 1;
            if (f.HasHome) flags |= 2;
            if (f.Started) flags |= 4;
            if (f.Done) flags |= 8;
            if (f.PoiCleared) flags |= 16;
            w.Write(flags);
            w.Write(Cut(f.RecruitWhy));
            w.Write((byte)Math.Max(0, Math.Min(255, f.Favour)));
            w.Write(Cut(f.Name));
            w.Write(f.PoiPos.x);
            w.Write(f.PoiPos.y);
            w.Write(f.PoiPos.z);
            w.Write(f.PoiWidth);
            w.Write(f.PoiDepth);
            w.Write(Cut(f.PoiName));
            w.Write(Cut(f.PoiWhy));
            w.Write(f.ItemCategory);
            w.Write(f.ItemCount);
            w.Write(f.EncPerson);
            w.Write((byte)Math.Max(0, Math.Min(255, f.Band)));
            w.Write((byte)Math.Max(0, Math.Min(255, f.BandCome)));
            w.Write(Cut(f.BandWhere));
            w.Write(Cut(f.BandJoinFacts));
            w.Write(Cut(f.BandName));
            w.Write(Cut(f.BandLeader));
            w.Write(f.BandCount);
        }

        public override void ProcessPackage(World _world, GameManager _callbacks)
        {
            try
            {
                ConnectionManager cm = ScNet.Cm;
                if (cm == null) return;
                if (cm.IsServer)
                {
                    if (kind == KindOpen) ScTalkContext.OnTalkOpen(Sender, id, openFlags);    // only Open is a client's to send
                    return;
                }
                if (kind == KindFacts) ScTalkContext.OnFacts(facts);
                else if (kind == KindClose) ScTalkContext.OnFavourClose(id, completed);
            }
            catch (Exception e)
            {
                ScLog.Once("net-talkcontext", e);
            }
        }
    }
}
