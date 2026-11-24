using System;
using System.Collections.Generic;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round MP2 (multiplayer): "scl" - SurvivorCommunity's console command for the typing player's OWN game: ui [reset] (the
    /// talk card and the member inventory panel), portrait &lt;id|name|all&gt; | portrait frame [...] (the community window's
    /// pictures), voice [play &lt;line&gt; [id|name] | stop | rescan] (this machine's audio). "sc" runs on the server for every
    /// player (a console command runs on the client only as a whole - IsExecuteOnClient), so those would act on the server's
    /// machine there; scl is a client command: the server only checks the permission and sends the line back to run here.
    /// Any player may use it (permission level 1000): it changes nothing but this game's own windows, pictures and sounds, and
    /// reads only this machine's copy of the community (the mirror on a client). In single player and for the host it does
    /// what sc ui / portrait / voice do (the same code, ConsoleCmdSc.ExecuteClientLocal).
    /// </summary>
    public class ConsoleCmdScl : ConsoleCmdAbstract
    {
        private const string Usage = "SurvivorCommunity in your own game (any player): ui [reset] | portrait <id|name|all> | portrait frame [fov dist yaw eyelift aimlift] | voice [play <line> [id|name] | stop | rescan]";

        public override string[] getCommands() { return new[] { "scl", "survivorcommunitylocal" }; }
        public override string getDescription() { return Usage; }
        public override string getHelp() { return getDescription(); }

        /// <summary>Runs on the machine that typed it (the server bounces a client's line back to it).</summary>
        public override bool IsExecuteOnClient { get { return true; } }

        /// <summary>Every player (vanilla's 1000): nothing here acts beyond the typing player's own game.</summary>
        public override int DefaultPermissionLevel { get { return 1000; } }

        public override void Execute(List<string> _params, CommandSenderInfo _senderInfo)
        {
            ConsoleCmdSc.ToLog("> scl " + (_params != null ? string.Join(" ", _params.ToArray()) : "") + " ("
                               + DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) + ")");
            try
            {
                if (GameManager.IsDedicatedServer)
                {
                    ConsoleCmdSc.Output("[SC] scl acts on a player's own game - type it in the game's console (F1), not on a dedicated server");
                    return;
                }
                if (_params == null || _params.Count == 0 || !ConsoleCmdSc.ExecuteClientLocal(_params))
                    ConsoleCmdSc.Output("[SC] " + Usage);
            }
            catch (Exception e)
            {
                ConsoleCmdSc.Output("[SC] error: " + e.Message);
                Log.Exception(e);
            }
        }
    }
}
