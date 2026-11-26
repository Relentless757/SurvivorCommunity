using System;
using System.Collections.Generic;
using System.IO;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round MP0 (multiplayer, Stage 1): SC's network layer - the protocol number, the hello at join, and the one way to
    /// reach a player from the server.
    /// - The game matches mod packages by class name only (an unknown name disconnects; a changed layout under the same name
    ///   desyncs silently), so SC carries its own protocol number: bump Protocol whenever any NetPackageSc* read/write changes.
    /// - "Send to all" and SendToServer never reach the listen host's own player: ToPlayer runs the local handler for an
    ///   EntityPlayerLocal and sends to a remote one.
    /// - Packages capture their data in Setup; read/write run on network threads (write once per recipient), ProcessPackage
    ///   on the main thread.
    /// </summary>
    public static class ScNet
    {
        /// <summary>Bump on any change to a NetPackageSc* layout or meaning.</summary>
        public const int Protocol = 7;      // 1: MP0 (hello). 2: MP1 (NetPackageScState, NetPackageScLook, NetPackageScLookRequest). 3: MP2 (NetPackageScAction, NetPackageScResult, NetPackageScTalkContext). 4: MP3 (item stacks in actions, linked storage). 5: MP4 (NetPackageScMarkers, NetPackageScQuestOp, NetPackageScJournalReport, NetPackageScVoice, NetPackageScTell, NetPackageScOpenBoxes). 6: round mt1 (NetPackageScTalkContext version 2: held/closed, Linked; member.gearview). 7: MP5 (NetPackageScTalkContext version 3: the enclave block; NetPackageScVoice flag bit 6, FlagHordeRadio; the enc.*, radio.* and encremains.search ops)

        /// <summary>This build's id (the assembly's module version id, short) - logged beside a protocol mismatch.</summary>
        public static readonly string Build = MakeBuild();

        /// <summary>Client only: the server's protocol from its hello (-1 = not heard yet; a server without SC never answers).</summary>
        public static int ServerProtocol = -1;
        public static string ServerBuild = "";
        /// <summary>Client only: the server runs SC with the same protocol.</summary>
        public static bool ServerMatches { get { return ServerProtocol == Protocol; } }

        /// <summary>Server only: each remote client's protocol by entity id, from its hello.</summary>
        private static readonly Dictionary<int, int> clientProtocol = new Dictionary<int, int>();
        private static bool helloSent;

        public static ConnectionManager Cm { get { return SingletonMonoBehaviour<ConnectionManager>.Instance; } }

        /// <summary>A client of a server (not single player, not the host).</summary>
        public static bool IsClient { get { ConnectionManager cm = Cm; return cm != null && cm.IsClient && !cm.IsServer; } }

        public static void Reset()
        {
            ServerProtocol = -1;
            ServerBuild = "";
            helloSent = false;
            clientProtocol.Clear();
        }

        private static string MakeBuild()
        {
            try { return typeof(ScNet).Assembly.ManifestModule.ModuleVersionId.ToString("N").Substring(0, 8); }
            catch (Exception) { return "?"; }
        }

        /// <summary>
        /// Server side: delivers to one player - the local handler for the host's own player (no package can reach it), the
        /// package for a remote one. False when the player is neither (no client info: not logged in, or gone).
        /// </summary>
        public static bool ToPlayer(EntityPlayer player, Func<NetPackage> package, Action local)
        {
            try
            {
                if (player == null) return false;
                if (player is EntityPlayerLocal)
                {
                    if (local != null) local();
                    return true;
                }
                ConnectionManager cm = Cm;
                ClientInfo ci = cm != null && cm.Clients != null ? cm.Clients.ForEntityId(player.entityId) : null;
                if (ci == null || package == null) return false;
                NetPackage p = package();
                if (p == null) return false;
                ci.SendPackage(p);
                return true;
            }
            catch (Exception e)
            {
                ScLog.Once("net-toplayer", e);
                return false;
            }
        }

        /// <summary>
        /// Client: whether the server knows this package type (its id table, complete by the time the player spawns). A package
        /// the server has no id for must never be queued: the network writer thread throws on its id and the client is
        /// disconnected (the MP0 review's B1) - a vanilla server, or one with SurvivorCommunity from before MP0. Every SC package
        /// sent from a client goes through this test.
        /// </summary>
        public static bool ServerKnows<T>() where T : NetPackage
        {
            Type[] map = NetPackageManager.PackageMappings;
            return map != null && Array.IndexOf(map, typeof(T)) >= 0;
        }

        /// <summary>The client's hello, once per session when the local player first spawns on a server (not single player or the host).</summary>
        public static void OnLocalPlayerSpawned()
        {
            try
            {
                if (helloSent || !IsClient) return;
                helloSent = true;
                if (!ServerKnows<NetPackageScHello>())
                {
                    ServerProtocol = 0;
                    ScLog.Warn("the server has no SurvivorCommunity network layer (no SurvivorCommunity, or a version from before multiplayer) - SurvivorCommunity stays inactive here");
                    return;
                }
                Cm.SendToServer(NetPackageManager.GetPackage<NetPackageScHello>().Setup(Protocol, Build, false));
            }
            catch (Exception e)
            {
                ScLog.Once("net-hello-send", e);
            }
        }

        /// <summary>Server: a remote client's hello. Logs and tells on a mismatch, and answers with the server's own.</summary>
        internal static void OnClientHello(ClientInfo sender, int protocol, string build)
        {
            if (sender == null) return;
            // round MP6 (core): in the sender's community. Round MP6 (part N): the snapshot and derived block sent below are the
            // sender's community's (ScMirror.OnClientMatched scopes them to it itself)
            using (ScRegistry.Use(ScPlayers.CommunityOf(sender))) ClientHello(sender, protocol, build);
        }

        private static void ClientHello(ClientInfo sender, int protocol, string build)
        {
            clientProtocol[sender.entityId] = protocol;
            if (protocol != Protocol)
            {
                ScLog.Warn("player " + sender.playerName + " runs SurvivorCommunity protocol " + protocol + " (build " + build
                           + "), this server runs protocol " + Protocol + " (build " + Build + "): install the same SurvivorCommunity version on both");
                EntityPlayer p = ScPlayers.ByEntity(sender.entityId);
                // round MP4 (part: tells): this file's tells go by localization key (ScCommunity.TellKey / ScPlayers.TellCommunityKey), so each player reads them in their own language
                if (p != null) ScCommunity.TellKey(p, "scNetMismatch", "ui_denied");
            }
            else ScLog.Verbose("player " + sender.playerName + " runs SurvivorCommunity protocol " + protocol + " (build " + build + ")");
            sender.SendPackage(NetPackageManager.GetPackage<NetPackageScHello>().Setup(Protocol, Build, true));
            // round MP1: a client that speaks SC's protocol gets the community's data at once (after the answer, on the same channel)
            if (protocol == Protocol) ScMirror.OnClientMatched(sender);
        }

        /// <summary>Client: the server's answer.</summary>
        internal static void OnServerHello(int protocol, string build)
        {
            ServerProtocol = protocol;
            ServerBuild = build ?? "";
            if (protocol != Protocol)
                ScLog.Warn("the server runs SurvivorCommunity protocol " + protocol + " (build " + ServerBuild + "), this game runs protocol "
                           + Protocol + " (build " + Build + "): install the same SurvivorCommunity version on both");
            else ScLog.Out("server runs the same SurvivorCommunity protocol (" + Protocol + ")");
        }

        /// <summary>Server: whether this remote client said hello with SC's protocol (the host's own player always matches).</summary>
        public static bool ClientMatches(EntityPlayer p)
        {
            if (p == null) return false;
            if (p is EntityPlayerLocal) return true;
            int v;
            return clientProtocol.TryGetValue(p.entityId, out v) && v == Protocol;
        }

        /// <summary>Round MP1, server: the same for a remote client by its entity id (ScMirror's push walks the ClientInfo list).</summary>
        public static bool ClientMatches(int entityId)
        {
            int v;
            return clientProtocol.TryGetValue(entityId, out v) && v == Protocol;
        }

        /// <summary>
        /// Round MP5 (review R-m1), server: a remote client that said hello with another protocol. A client whose hello has not
        /// come yet (still loading in) is not one: it may well be able to use the radio.
        /// </summary>
        public static bool KnownMismatch(EntityPlayer p)
        {
            int v;
            return p != null && !(p is EntityPlayerLocal) && clientProtocol.TryGetValue(p.entityId, out v) && v != Protocol;
        }

        public static void ForgetClient(int entityId)
        {
            clientProtocol.Remove(entityId);
        }
    }

    /// <summary>
    /// Round MP0: SC's hello. Client to server once per session when the local player spawns (protocol, build); the server's
    /// answer back (reply = true). The game itself checks only package names, so this is how a version mismatch is caught.
    /// FROZEN LAYOUT (the review's N7): this package is what detects a layout change, so its own read/write never changes -
    /// anything new goes in other packages (an older peer would throw reading a longer hello and disconnect).
    /// </summary>
    public class NetPackageScHello : NetPackage
    {
        private int protocol;
        private string build = "";
        private bool reply;

        public NetPackageScHello Setup(int _protocol, string _build, bool _reply)
        {
            protocol = _protocol;
            build = _build ?? "";
            reply = _reply;
            return this;
        }

        public override void read(PooledBinaryReader _reader)
        {
            BinaryReader r = _reader;
            protocol = r.ReadInt32();
            build = r.ReadString();
            reply = r.ReadBoolean();
        }

        public override void write(PooledBinaryWriter _writer)
        {
            base.write(_writer);
            BinaryWriter w = _writer;
            w.Write(protocol);
            w.Write(build ?? "");
            w.Write(reply);
        }

        public override void ProcessPackage(World _world, GameManager _callbacks)
        {
            try
            {
                ConnectionManager cm = ScNet.Cm;
                if (cm == null) return;
                if (cm.IsServer)
                {
                    if (!reply) ScNet.OnClientHello(Sender, protocol, build);
                }
                else if (reply) ScNet.OnServerHello(protocol, build);
            }
            catch (Exception e)
            {
                ScLog.Once("net-hello", e);
            }
        }
    }
}
