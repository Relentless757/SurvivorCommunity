using System;
using System.IO;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round MP2: the server's answer to one NetPackageScAction, server to that client.
    /// Layout:
    ///   int32  request    the client's request id
    ///   bool   ok
    ///   string message    a localization key or text (ScActionReply)
    ///   string sound
    ///   string hint       key=value;key=value for the UI that asked
    /// </summary>
    public class NetPackageScResult : NetPackage
    {
        private int request;
        private bool ok;
        private string message = "";
        private string sound = "";
        private string hint = "";

        public override NetPackageDirection PackageDirection { get { return NetPackageDirection.ToClient; } }

        public NetPackageScResult Setup(int _request, ScActionReply reply)
        {
            request = _request;
            ok = reply != null && reply.Ok;
            message = reply != null ? reply.Message ?? "" : "";
            sound = reply != null ? reply.Sound ?? "" : "";
            hint = reply != null ? reply.Hint ?? "" : "";
            return this;
        }

        public override void read(PooledBinaryReader _reader)
        {
            BinaryReader r = _reader;
            request = r.ReadInt32();
            ok = r.ReadBoolean();
            message = r.ReadString();
            sound = r.ReadString();
            hint = r.ReadString();
        }

        public override void write(PooledBinaryWriter _writer)
        {
            base.write(_writer);
            BinaryWriter w = _writer;
            w.Write(request);
            w.Write(ok);
            w.Write(message ?? "");
            w.Write(sound ?? "");
            w.Write(hint ?? "");
        }

        public override void ProcessPackage(World _world, GameManager _callbacks)
        {
            try
            {
                ConnectionManager cm = ScNet.Cm;
                if (cm == null || cm.IsServer) return;           // an answer never lands on the server
                ScActions.OnResult(request, new ScActionReply { Ok = ok, Message = message ?? "", Sound = sound ?? "", Hint = hint ?? "" });
            }
            catch (Exception e)
            {
                ScLog.Once("net-result", e);
            }
        }
    }
}
