using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round MP4 (part K): the community's map markers, server to client (ScMarkers). The server sends it only to remote
    /// clients that said hello with SC's protocol (ScNet.ClientMatches), through ScNet.ToPlayer; the host's own player draws
    /// its markers itself, as before (each subsystem's AddMarker). A client never sends it, and one that arrives on a server
    /// is ignored. The whole set comes at the server's first tick after the client's hello, then only changes.
    /// Layout:
    ///   int32  community  the community the set is for (Stage 1: always 0; round MP6 (part N): the recipient's own, the server's
    ///                     truth - a client keeps one community's set, and a whole set of another replaces it; layout unchanged)
    ///   bool   full       true: the whole set - the client forgets every marker of the server's it holds that is not in it
    ///   int32  n          then n changes:
    ///     byte   op         0 put (a new marker, or a new class or label), 1 move (the position alone), 2 drop
    ///     string kind       ScMarkers.Kind* ("enclave", "contact", "infest", "band", "outpost", "opattack", "scout", "runner",
    ///                       "watch")
    ///     string key        the subsystem's own key (a record id, "x,y,z" for a scouted site, "base" for the watch marker)
    ///     op 0:
    ///       string cls      the nav object class (Config/nav_objects.xml, or vanilla's "quest")
    ///       string label    the marker's name on the map, composed on the server
    ///     op 0 and 1:
    ///       float  x, y, z  the position
    /// Everything is captured in Setup: write() runs on a network thread, once per recipient, and only copies.
    /// </summary>
    public class NetPackageScMarkers : NetPackage
    {
        /// <summary>A count past this is a corrupt or foreign package (a community never has this many markers).</summary>
        private const int MaxOps = 50000;

        private int community;
        private bool full;
        private ScMarkers.Op[] ops = new ScMarkers.Op[0];

        public override NetPackageDirection PackageDirection { get { return NetPackageDirection.ToClient; } }

        /// <summary>The changes are copied (the list is the server's scratch, reused at once).</summary>
        public NetPackageScMarkers Setup(int _community, bool _full, List<ScMarkers.Op> changes)
        {
            community = _community;
            full = _full;
            ops = changes != null ? changes.ToArray() : new ScMarkers.Op[0];
            return this;
        }

        public override void read(PooledBinaryReader _reader)
        {
            BinaryReader r = _reader;
            community = r.ReadInt32();
            full = r.ReadBoolean();
            int n = r.ReadInt32();
            if (n < 0 || n > MaxOps) throw new InvalidDataException("NetPackageScMarkers: " + n + " changes");
            var list = new ScMarkers.Op[n];
            for (int i = 0; i < n; i++)
            {
                var op = new ScMarkers.Op { Code = r.ReadByte(), Kind = r.ReadString(), Key = r.ReadString(), Cls = "", Label = "" };
                if (op.Code == ScMarkers.Op.Put)
                {
                    op.Cls = r.ReadString();
                    op.Label = r.ReadString();
                }
                if (op.Code == ScMarkers.Op.Put || op.Code == ScMarkers.Op.Move)
                {
                    float x = r.ReadSingle(), y = r.ReadSingle(), z = r.ReadSingle();
                    op.Pos = new Vector3(x, y, z);
                }
                else if (op.Code != ScMarkers.Op.Drop) throw new InvalidDataException("NetPackageScMarkers: op " + op.Code);
                list[i] = op;
            }
            ops = list;
        }

        public override void write(PooledBinaryWriter _writer)
        {
            base.write(_writer);
            BinaryWriter w = _writer;
            ScMarkers.Op[] list = ops ?? new ScMarkers.Op[0];
            w.Write(community);
            w.Write(full);
            w.Write(list.Length);
            for (int i = 0; i < list.Length; i++)
            {
                ScMarkers.Op op = list[i];
                w.Write(op.Code);
                w.Write(op.Kind ?? "");
                w.Write(op.Key ?? "");
                if (op.Code == ScMarkers.Op.Put)
                {
                    w.Write(op.Cls ?? "");
                    w.Write(op.Label ?? "");
                }
                if (op.Code == ScMarkers.Op.Put || op.Code == ScMarkers.Op.Move)
                {
                    w.Write(op.Pos.x);
                    w.Write(op.Pos.y);
                    w.Write(op.Pos.z);
                }
            }
        }

        public override void ProcessPackage(World _world, GameManager _callbacks)
        {
            try
            {
                ConnectionManager cm = ScNet.Cm;
                if (cm == null || cm.IsServer) return;          // the server draws its own markers: this never lands there
                ScMarkers.Receive(community, full, ops);
            }
            catch (Exception e)
            {
                ScLog.Once("net-markers", e);
            }
        }
    }
}
