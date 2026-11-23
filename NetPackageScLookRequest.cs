using System;
using System.IO;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round MP1 (part looks): client to server - "send me this body's look". A client sends it for a copy of one of SC's
    /// bodies it built without the server's look (ScLooks.ForEntity: the join, before the hellos are through; or a copy whose
    /// look the server did not send first), once per copy, again after a few seconds while unanswered (ScLooks.NetTick).
    /// Only once the server's hello said it runs SC's protocol, and only when the server knows this package
    /// (ScNet.ServerKnows). The server answers that client alone with NetPackageScLook, and only for a body the game sends
    /// that player (ScLooks.OnLookRequest).
    /// Layout: entityId (int32).
    /// </summary>
    public class NetPackageScLookRequest : NetPackage
    {
        private int entityId;

        public override NetPackageDirection PackageDirection { get { return NetPackageDirection.ToServer; } }

        public NetPackageScLookRequest Setup(int _entityId)
        {
            entityId = _entityId;
            return this;
        }

        public override void read(PooledBinaryReader _reader)
        {
            BinaryReader r = _reader;
            entityId = r.ReadInt32();
        }

        public override void write(PooledBinaryWriter _writer)
        {
            base.write(_writer);
            BinaryWriter w = _writer;
            w.Write(entityId);
        }

        public override void ProcessPackage(World _world, GameManager _callbacks)
        {
            try
            {
                ConnectionManager cm = ScNet.Cm;
                if (cm == null || !cm.IsServer) return;
                ScLooks.OnLookRequest(Sender, entityId);
            }
            catch (Exception e)
            {
                ScLog.Once("net-look-request", e);
            }
        }
    }
}
