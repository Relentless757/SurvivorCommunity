using System;
using System.IO;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round MP4 (part Q): a client's journal, client to server (ScQuestNet) - sent when it changed, and every 30 s. The server
    /// keeps it for the session and answers with quest ops (NetPackageScQuestOp): a POI favour past its clearing marked done,
    /// stale favour, rescue and request quests closed or re-keyed, an open rescue or an accepted request started. The player
    /// is the package's Sender - never an id from the payload.
    /// Layout (all little-endian, BinaryWriter):
    ///   byte   version   1
    ///   int32  talking   the NPC whose dialog is open (-1: none)
    ///   byte   n         then n entries (at most 200), each:
    ///     byte   kind      0 a POI favour's quest, 1 a supplies favour's, 2 a rescue's, 3 an enclave request's
    ///     int32  id        the favour's giver (prospect id), the runner's member id, the request's id
    ///     int32  person    a favour of one of an enclave's people: the person's id; else 0
    ///     byte   flags     bit 0 in progress, bit 1 a POI favour's quest past its clearing (or completed)
    ///   byte   m         then m POIs (at most 64) every quest in progress leads to: int32 x, y, z (block keys)
    /// Everything is captured in Setup; write() only copies.
    /// </summary>
    public class NetPackageScJournalReport : NetPackage
    {
        private const byte Version = 1;

        private ScJournalReport report;

        public override NetPackageDirection PackageDirection { get { return NetPackageDirection.ToServer; } }

        public NetPackageScJournalReport Setup(ScJournalReport r)
        {
            // a copy: the client may build the next one while this one waits on the network thread
            report = new ScJournalReport { Talking = r != null ? r.Talking : -1 };
            if (r != null)
            {
                for (int i = 0; i < r.Entries.Count && i < ScJournalReport.MaxEntries; i++) report.Entries.Add(r.Entries[i]);
                for (int i = 0; i < r.Pois.Count && i < ScJournalReport.MaxPois; i++) report.Pois.Add(r.Pois[i]);
            }
            return this;
        }

        public override void read(PooledBinaryReader _reader)
        {
            BinaryReader r = _reader;
            r.ReadByte();                     // version (1): a later layout bumps ScNet.Protocol as well
            var rep = new ScJournalReport { Talking = r.ReadInt32() };
            int n = r.ReadByte();
            for (int i = 0; i < n; i++)
            {
                byte kind = r.ReadByte();
                int id = r.ReadInt32();
                int person = r.ReadInt32();
                byte flags = r.ReadByte();
                rep.Add(kind, id, person, flags);
            }
            int m = r.ReadByte();
            for (int i = 0; i < m; i++)
            {
                int x = r.ReadInt32(), y = r.ReadInt32(), z = r.ReadInt32();
                if (rep.Pois.Count < ScJournalReport.MaxPois) rep.Pois.Add(new Vector3i(x, y, z));
            }
            report = rep;
        }

        public override void write(PooledBinaryWriter _writer)
        {
            base.write(_writer);
            BinaryWriter w = _writer;
            ScJournalReport rep = report ?? new ScJournalReport();
            w.Write(Version);
            w.Write(rep.Talking);
            int n = Math.Min(rep.Entries.Count, ScJournalReport.MaxEntries);
            w.Write((byte)n);
            for (int i = 0; i < n; i++)
            {
                ScJournalReport.Entry e = rep.Entries[i];
                w.Write(e.Kind);
                w.Write(e.Id);
                w.Write(e.EncPerson);
                w.Write(e.Flags);
            }
            int m = Math.Min(rep.Pois.Count, ScJournalReport.MaxPois);
            w.Write((byte)m);
            for (int i = 0; i < m; i++)
            {
                w.Write(rep.Pois[i].x);
                w.Write(rep.Pois[i].y);
                w.Write(rep.Pois[i].z);
            }
        }

        public override void ProcessPackage(World _world, GameManager _callbacks)
        {
            try
            {
                ConnectionManager cm = ScNet.Cm;
                if (cm == null || !cm.IsServer) return;          // a report never lands on a client
                using (ScRegistry.Use(ScPlayers.CommunityOf(Sender))) ScQuestNet.OnReport(Sender, report);   // round MP6 (core): in the sender's community
            }
            catch (Exception e)
            {
                ScLog.Once("net-journalreport", e);
            }
        }
    }
}
