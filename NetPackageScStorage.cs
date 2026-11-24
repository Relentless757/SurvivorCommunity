using System;
using System.Collections.Generic;
using System.IO;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round MP3 (part L): linked storage, server to client (ScStorageServer). The server sends it only to remote clients that
    /// said hello with SC's protocol (ScNet.ClientMatches); the host's own player gets the same data handed over directly
    /// (ScNet.ToPlayer). A client never sends it, and one that arrives on a server is ignored. The client's changes go the
    /// other way as ScActions requests (storage.cells).
    /// Layout:
    ///   byte   kind       1 = a view (the answer to storage.open), 2 = slot updates (another view wrote boxes this view
    ///                     shows), 3 = the server ended the view
    ///   int32  session    the server's session id
    ///   kind 1:
    ///     int32  viewKind     ScStorage.KindHome / KindOutpost / KindHomeLink
    ///     int32  x, y, z      the anchor: the claim the view stands on
    ///     string name         the outpost's name (KindOutpost), else ""
    ///     int32  boxes, freeSlots, freeCells (-1 = no grid bridge on the server), skippedChunks, skippedBoxes
    ///   kind 1 and 2:
    ///     int32  n            then n slots: int32 x, y, z, int32 index, string code (ScItemTx.Encode; "" = empty)
    ///   kind 2:
    ///     int32  freeSlots, freeCells
    ///   kind 3:
    ///     string reason       a localization key
    /// Everything is captured in Setup: write() runs on a network thread, once per recipient, and only copies.
    /// </summary>
    public class NetPackageScStorage : NetPackage
    {
        public const byte KindView = 1, KindSlots = 2, KindClosed = 3;
        /// <summary>A count past this is a corrupt or foreign package (a view never has this many slots).</summary>
        private const int MaxSlots = 200000;

        private byte kind;
        private int session;
        private int viewKind;
        private int ax, ay, az;
        private string name = "";
        private int boxes, freeSlots, freeCells = -1, skippedChunks, skippedBoxes;
        private int[] xs = new int[0], ys = new int[0], zs = new int[0], idx = new int[0];
        private string[] codes = new string[0];
        private string reason = "";

        public override NetPackageDirection PackageDirection { get { return NetPackageDirection.ToClient; } }

        public NetPackageScStorage SetupView(ScStorageView v)
        {
            kind = KindView;
            session = v.Session;
            viewKind = v.Kind;
            ax = v.Anchor.x; ay = v.Anchor.y; az = v.Anchor.z;
            name = v.Name ?? "";
            boxes = v.Boxes;
            freeSlots = v.FreeSlots;
            freeCells = v.FreeCells;
            skippedChunks = v.SkippedChunks;
            skippedBoxes = v.SkippedBoxes;
            CopySlots(v.Slots);
            return this;
        }

        public NetPackageScStorage SetupSlots(int _session, List<ScStorage.Slot> _slots, int _freeSlots, int _freeCells)
        {
            kind = KindSlots;
            session = _session;
            freeSlots = _freeSlots;
            freeCells = _freeCells;
            CopySlots(_slots);
            return this;
        }

        public NetPackageScStorage SetupClosed(int _session, string _reason)
        {
            kind = KindClosed;
            session = _session;
            reason = _reason ?? "";
            return this;
        }

        private void CopySlots(List<ScStorage.Slot> list)
        {
            int n = list != null ? list.Count : 0;
            xs = new int[n]; ys = new int[n]; zs = new int[n]; idx = new int[n]; codes = new string[n];
            for (int i = 0; i < n; i++)
            {
                ScStorage.Slot s = list[i];
                xs[i] = s.Pos.x; ys[i] = s.Pos.y; zs[i] = s.Pos.z; idx[i] = s.Index; codes[i] = s.Code ?? "";
            }
        }

        public override void read(PooledBinaryReader _reader)
        {
            BinaryReader r = _reader;
            kind = r.ReadByte();
            session = r.ReadInt32();
            if (kind == KindClosed)
            {
                reason = r.ReadString();
                return;
            }
            if (kind == KindView)
            {
                viewKind = r.ReadInt32();
                ax = r.ReadInt32(); ay = r.ReadInt32(); az = r.ReadInt32();
                name = r.ReadString();
                boxes = r.ReadInt32();
                freeSlots = r.ReadInt32();
                freeCells = r.ReadInt32();
                skippedChunks = r.ReadInt32();
                skippedBoxes = r.ReadInt32();
            }
            int n = r.ReadInt32();
            if (n < 0 || n > MaxSlots) throw new InvalidDataException("NetPackageScStorage: slot count " + n);
            xs = new int[n]; ys = new int[n]; zs = new int[n]; idx = new int[n]; codes = new string[n];
            for (int i = 0; i < n; i++)
            {
                xs[i] = r.ReadInt32(); ys[i] = r.ReadInt32(); zs[i] = r.ReadInt32(); idx[i] = r.ReadInt32();
                codes[i] = r.ReadString();
            }
            if (kind == KindSlots)
            {
                freeSlots = r.ReadInt32();
                freeCells = r.ReadInt32();
            }
        }

        public override void write(PooledBinaryWriter _writer)
        {
            base.write(_writer);
            BinaryWriter w = _writer;
            w.Write(kind);
            w.Write(session);
            if (kind == KindClosed)
            {
                w.Write(reason ?? "");
                return;
            }
            if (kind == KindView)
            {
                w.Write(viewKind);
                w.Write(ax); w.Write(ay); w.Write(az);
                w.Write(name ?? "");
                w.Write(boxes);
                w.Write(freeSlots);
                w.Write(freeCells);
                w.Write(skippedChunks);
                w.Write(skippedBoxes);
            }
            int n = codes != null ? codes.Length : 0;
            w.Write(n);
            for (int i = 0; i < n; i++)
            {
                w.Write(xs[i]); w.Write(ys[i]); w.Write(zs[i]); w.Write(idx[i]);
                w.Write(codes[i] ?? "");
            }
            if (kind == KindSlots)
            {
                w.Write(freeSlots);
                w.Write(freeCells);
            }
        }

        private List<ScStorage.Slot> Slots(bool decode)
        {
            var list = new List<ScStorage.Slot>(codes.Length);
            for (int i = 0; i < codes.Length; i++)
            {
                var s = new ScStorage.Slot { Pos = new Vector3i(xs[i], ys[i], zs[i]), Index = idx[i], Code = codes[i] ?? "" };
                if (decode) s.Stack = ScItemTx.Decode(s.Code);
                list.Add(s);
            }
            return list;
        }

        public override void ProcessPackage(World _world, GameManager _callbacks)
        {
            try
            {
                ConnectionManager cm = ScNet.Cm;
                if (cm == null || cm.IsServer) return;          // the server's boxes are the truth: a view never lands there
                switch (kind)
                {
                    case KindView:
                    {
                        var v = new ScStorageView
                        {
                            Session = session, Kind = viewKind, Anchor = new Vector3i(ax, ay, az), Name = name ?? "", Boxes = boxes,
                            FreeSlots = freeSlots, FreeCells = freeCells, SkippedChunks = skippedChunks, SkippedBoxes = skippedBoxes
                        };
                        v.Slots.AddRange(Slots(true));
                        ScStorage.OnView(v);
                        break;
                    }
                    case KindSlots:
                        ScStorage.OnSlots(session, Slots(false), freeSlots, freeCells);
                        break;
                    case KindClosed:
                        ScStorage.OnClosed(session, reason);
                        break;
                }
            }
            catch (Exception e)
            {
                ScLog.Once("net-storage", e);
            }
        }
    }
}
