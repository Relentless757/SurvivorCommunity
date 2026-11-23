using System;
using System.IO;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round MP1 (part looks): server to one client - the whole look of one of SC's bodies, so every machine builds the same
    /// person (ScLooks, "one look on every machine"). The server sends the look it already has for the body (ScLooks.Get:
    /// the session look, else the member's or the prospect's record); it never rolls one because a client asked.
    /// Sent:
    ///  - right before the game starts sending the body to that client (Patches_LookNet.Patch_TrackStart), so the copy is
    ///    built from it;
    ///  - in answer to the client's NetPackageScLookRequest;
    ///  - to every client that has the body when the server changes its look (ScLooks.LookChanged).
    /// Layout:
    ///  - entityId (int32);
    ///  - entityClass (int32): the class the look is for (a copy of another class, a recycled id, does not take it);
    ///  - has (bool): false when the server has no look of SC's for the body - look and gear are then empty, and the client
    ///    keeps its own and stops asking;
    ///  - look (string): Look.Encode - sex, race or tone, face, hair and its color, facial hair, eyes, Vanity Vault's body
    ///    paint, face paint and tattoo, the creator-face flag;
    ///  - gear (string): Look.EncodeGear - the four armor items with their qualities;
    ///  - source (string): where the server's look came from (member, prospect, session, roll, pending, creator face), for
    ///    the client's diagnostics and X17's refusal there.
    /// A body's look is the same for every viewer, whatever their community, so it carries no community id. Everything is
    /// captured in Setup on the main thread; write has no side effects.
    /// </summary>
    public class NetPackageScLook : NetPackage
    {
        private int entityId;
        private int entityClass;
        private bool has;
        private string look = "";
        private string gear = "";
        private string source = "";

        public override NetPackageDirection PackageDirection { get { return NetPackageDirection.ToClient; } }

        /// <summary>The body's look as it stands now (null: the server has none of SC's for it).</summary>
        public NetPackageScLook Setup(int _entityId, int _entityClass, Look _look, string _source)
        {
            entityId = _entityId;
            entityClass = _entityClass;
            has = _look != null;
            look = has ? _look.Encode() ?? "" : "";
            gear = has ? _look.EncodeGear() ?? "" : "";
            source = _source ?? "";
            return this;
        }

        public override void read(PooledBinaryReader _reader)
        {
            BinaryReader r = _reader;
            entityId = r.ReadInt32();
            entityClass = r.ReadInt32();
            has = r.ReadBoolean();
            look = r.ReadString();
            gear = r.ReadString();
            source = r.ReadString();
        }

        public override void write(PooledBinaryWriter _writer)
        {
            base.write(_writer);
            BinaryWriter w = _writer;
            w.Write(entityId);
            w.Write(entityClass);
            w.Write(has);
            w.Write(look ?? "");
            w.Write(gear ?? "");
            w.Write(source ?? "");
        }

        public override void ProcessPackage(World _world, GameManager _callbacks)
        {
            try
            {
                ConnectionManager cm = ScNet.Cm;
                if (cm == null || cm.IsServer) return;     // the server's own bodies are its own
                ScLooks.OnLookReceived(entityId, entityClass, has, look, gear, source);
            }
            catch (Exception e)
            {
                ScLog.Once("net-look", e);
            }
        }
    }
}
