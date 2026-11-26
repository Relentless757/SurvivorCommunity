using System;
using System.IO;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round MP2 (part M): the talk context (ScTalkContext). Both directions, one byte saying which of five it is (three before round mt1):
    ///   0 Open   client to server: a talk with an NPC opened on the client. The server rolls the profile and the favour (the
    ///            side effects a dialog requirement had on the host), and answers with 1.
    ///   1 Facts  server to that client: what the dialog's requirements need for this NPC that the client's copy of the registry
    ///            lacks (ScTalkFacts) - the recruit verdict, the home, the favour, the POI verdict, a band's answers.
    ///   2 Close  server to a client: close your favour quest for this prospect (the favour is shared: whoever joins them, a
    ///            death or a grudge ends everyone else's copy - the E 4.4 decision).
    ///   round mt1 (part H): a remote talker's talk hold (ScWork.HoldForRemote: the server links a member to the client whose
    ///   talk opened, and says so in the facts' Linked flag):
    ///   3 Held   client to server: its talk with this member is still open (ScWork.TalkOpen on its own windows, or within its
    ///            grace after they closed) - about once a second (ScWork.RemoteBeat).
    ///   4 TalkClosed client to server: its talk with her is over (ScWork.RemoteClosed): the server ends the hold.
    /// Layout (all little-endian, BinaryWriter):
    ///   byte   version      3 (round MP5: the facts' enclave block; 2 since round mt1: kinds 3 and 4, the facts' Linked bit; 1 before)
    ///   byte   kind         0 | 1 | 2 | 3 | 4
    ///   int32  id           the NPC's entity id (0, 1, 3, 4), or the prospect id (2)
    ///   kind 0: byte flags  bit 0: this player's journal holds this NPC's POI favour quest past its clearing
    ///   kind 2: bool completed
    ///   kind 3: byte flags  bit 0: the talk window with her is open (the hold's cap clock restarts)
    ///   kind 4: nothing more
    ///   kind 1 (ScTalkFacts):
    ///     byte   flags      bit 0 Recruit, 1 HasHome, 2 Started, 3 Done, 4 PoiCleared, 5 Linked (round mt1: the server linked her
    ///                       to you for this talk - her Owner cvar is on its way)
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
    ///     round MP5 (part E), version 3: one of an enclave's people (ScTalkFacts.Enc*; ScEncFacts.Fill):
    ///     int32  encFlags   ScTalkFacts.Enc* bits; 0: not an enclave body, and nothing more follows
    ///     int32  encId
    ///     string standing, kind
    ///     string enlistWhy, recruitWhy, giftWhy, joinWhy (the server's part of each greyed row's reason, its language; "" passes)
    ///     int32  enlistCost
    ///     int32  reqId
    ///     string reqState, reqKind, reqStage
    ///     int32  reqRes, reqUnits
    ///     string foundRow
    ///     byte   search     0 none, 1 alive and waiting, 2 following
    ///     bool   hasWhere, then (when true) float x, y, z: where their leader is
    ///     byte   n tokens, then per token: string token, string key (localized on the client), string text (the server's words)
    /// Everything is captured in Setup*; write() only copies (it runs once per recipient on a network thread).
    /// </summary>
    public class NetPackageScTalkContext : NetPackage
    {
        public const byte KindOpen = 0, KindFacts = 1, KindClose = 2;
        /// <summary>Round mt1 (part H): a remote talker's hold - the client's heartbeat and its close.</summary>
        public const byte KindHeld = 3, KindTalkClosed = 4;
        private const byte Version = 3;     // round mt1 (part H): 2 (kinds 3, 4 and the facts' Linked bit); round MP5 (part E): 3 (the facts' enclave block)
        /// <summary>A string is cut to this many characters (names and the band's facts are far shorter).</summary>
        private const int MaxString = 512;
        /// <summary>Round MP5 (part E): at most this many enclave line tokens (the server sends about 15).</summary>
        private const int MaxTokens = 40;

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

        /// <summary>Round mt1 (part H), client: the talk with this member is still open (dialog: its talk window with her).</summary>
        public NetPackageScTalkContext SetupHeld(int npcId, bool dialog)
        {
            kind = KindHeld;
            id = npcId;
            openFlags = (byte)(dialog ? 1 : 0);
            return this;
        }

        /// <summary>Round mt1 (part H), client: the talk with this member is over.</summary>
        public NetPackageScTalkContext SetupTalkClosed(int npcId)
        {
            kind = KindTalkClosed;
            id = npcId;
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
            byte version = r.ReadByte();      // version (3 since round MP5): a later layout bumps ScNet.Protocol as well
            kind = r.ReadByte();
            id = r.ReadInt32();
            if (kind == KindOpen || kind == KindHeld) { openFlags = r.ReadByte(); return; }     // round mt1 (part H): Held's flags
            if (kind == KindClose) { completed = r.ReadBoolean(); return; }
            if (kind == KindTalkClosed) return;                                                // round mt1 (part H)
            var f = new ScTalkFacts { Npc = id };
            byte flags = r.ReadByte();
            f.Recruit = (flags & 1) != 0;
            f.HasHome = (flags & 2) != 0;
            f.Started = (flags & 4) != 0;
            f.Done = (flags & 8) != 0;
            f.PoiCleared = (flags & 16) != 0;
            f.Linked = (flags & 32) != 0;     // round mt1 (part H)
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
            if (version >= 3) ReadEnclave(r, f);      // round MP5 (part E)
            facts = f;
        }

        /// <summary>Round MP5 (part E): the facts' enclave block (see the layout).</summary>
        private static void ReadEnclave(BinaryReader r, ScTalkFacts f)
        {
            f.EncFlags = r.ReadInt32();
            if (f.EncFlags == 0) return;
            f.EncId = r.ReadInt32();
            f.EncStanding = Cut(r.ReadString());
            f.EncKind = Cut(r.ReadString());
            f.EncEnlistWhy = Cut(r.ReadString());
            f.EncRecruitWhy = Cut(r.ReadString());
            f.EncGiftWhy = Cut(r.ReadString());
            f.EncJoinWhy = Cut(r.ReadString());
            f.EncEnlistCost = r.ReadInt32();
            f.EncReqId = r.ReadInt32();
            f.EncReqState = Cut(r.ReadString());
            f.EncReqKind = Cut(r.ReadString());
            f.EncReqStage = Cut(r.ReadString());
            f.EncReqRes = r.ReadInt32();
            f.EncReqUnits = r.ReadInt32();
            f.EncFoundRow = Cut(r.ReadString());
            f.EncSearch = r.ReadByte();
            f.EncHasWhere = r.ReadBoolean();
            if (f.EncHasWhere) f.EncWhere = new UnityEngine.Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            int n = r.ReadByte();
            var list = new System.Collections.Generic.List<ScEncToken>(Math.Min(n, MaxTokens));
            for (int i = 0; i < n; i++)
            {
                var t = new ScEncToken { Token = Cut(r.ReadString()), Key = Cut(r.ReadString()), Text = Cut(r.ReadString()) };
                if (i < MaxTokens) list.Add(t);
            }
            f.EncTokens = list;
        }

        /// <summary>Round MP5 (part E): the facts' enclave block (see the layout).</summary>
        private static void WriteEnclave(BinaryWriter w, ScTalkFacts f)
        {
            w.Write(f.EncFlags);
            if (f.EncFlags == 0) return;
            w.Write(f.EncId);
            w.Write(Cut(f.EncStanding));
            w.Write(Cut(f.EncKind));
            w.Write(Cut(f.EncEnlistWhy));
            w.Write(Cut(f.EncRecruitWhy));
            w.Write(Cut(f.EncGiftWhy));
            w.Write(Cut(f.EncJoinWhy));
            w.Write(f.EncEnlistCost);
            w.Write(f.EncReqId);
            w.Write(Cut(f.EncReqState));
            w.Write(Cut(f.EncReqKind));
            w.Write(Cut(f.EncReqStage));
            w.Write(f.EncReqRes);
            w.Write(f.EncReqUnits);
            w.Write(Cut(f.EncFoundRow));
            w.Write((byte)Math.Max(0, Math.Min(255, f.EncSearch)));
            w.Write(f.EncHasWhere);
            if (f.EncHasWhere)
            {
                w.Write(f.EncWhere.x);
                w.Write(f.EncWhere.y);
                w.Write(f.EncWhere.z);
            }
            System.Collections.Generic.List<ScEncToken> list = f.EncTokens;
            int n = list != null ? Math.Min(list.Count, MaxTokens) : 0;
            w.Write((byte)n);
            for (int i = 0; i < n; i++)
            {
                ScEncToken t = list[i] ?? new ScEncToken();
                w.Write(Cut(t.Token));
                w.Write(Cut(t.Key));
                w.Write(Cut(t.Text));
            }
        }

        public override void write(PooledBinaryWriter _writer)
        {
            base.write(_writer);
            BinaryWriter w = _writer;
            w.Write(Version);
            w.Write(kind);
            w.Write(id);
            if (kind == KindOpen || kind == KindHeld) { w.Write(openFlags); return; }     // round mt1 (part H): Held's flags
            if (kind == KindClose) { w.Write(completed); return; }
            if (kind == KindTalkClosed) return;                                          // round mt1 (part H)
            ScTalkFacts f = facts ?? new ScTalkFacts();
            byte flags = 0;
            if (f.Recruit) flags |= 1;
            if (f.HasHome) flags |= 2;
            if (f.Started) flags |= 4;
            if (f.Done) flags |= 8;
            if (f.PoiCleared) flags |= 16;
            if (f.Linked) flags |= 32;        // round mt1 (part H)
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
            WriteEnclave(w, f);               // round MP5 (part E)
        }

        public override void ProcessPackage(World _world, GameManager _callbacks)
        {
            try
            {
                ConnectionManager cm = ScNet.Cm;
                if (cm == null) return;
                if (cm.IsServer)
                {
                    // round MP6 (core): in the sender's community
                    using (ScRegistry.Use(ScPlayers.CommunityOf(Sender)))
                    {
                        // a client sends only Open, Held and TalkClosed (round mt1, part H: the last two)
                        if (kind == KindOpen) ScTalkContext.OnTalkOpen(Sender, id, openFlags);
                        else if (kind == KindHeld) ScTalkContext.OnTalkHeld(Sender, id, openFlags);
                        else if (kind == KindTalkClosed) ScTalkContext.OnTalkClosed(Sender, id);
                    }
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
