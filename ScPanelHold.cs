using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round mt1 (review fix G-m2; bug_gear_equip_window.md H2), the server: its own backup of Voice Control's interaction hold
    /// while a remote player has a member's inventory open on SC's member panel.
    ///
    /// Round mt1's H1 (Patches_VoiceControl.Patch_PanelHold) makes the panel count in Voice Control's hold on the machine that
    /// shows it; on a client that machine's component tells the server (NetPackageVCChatState -> SetRemoteChatActive for 45 s,
    /// refreshed every 20 s). The server then held her only on the client's word, so she could walk when that word never came
    /// or came late: Voice Control refuses it beyond 15 m for an NPC with no player leader, a client sends nothing without a
    /// chat component for her, and the client's dialog closes (false) before the server's inventory answer opens the panel
    /// (true again). SCore's session marks are the server's own record of who has whose inventory open (ScAmmo.RemoteOpened /
    /// RemoteClosed, with their 600 s stale cap), from the request on.
    ///
    /// From the 1 Hz ticker, for each mark on a member whose player is a live remote player within Reach of her, this calls
    /// Voice Control's public ChatComponentManager.GetOrCreate(npc).SetRemoteChatActive(player, true) - the very call its own
    /// package makes - at once, once more SecondSeconds later (the client's own "false" from its dialog's close can land after
    /// the first), then every RefreshSeconds (its window lasts 45 s). (player, false) when the mark goes (RemoteClosed: at once),
    /// goes stale, the player walks out of Reach, dies or leaves (ScAmmo.ForgetPlayer). Bound by reflection once per process;
    /// without Voice Control nothing happens, and a renamed method logs one error and turns this off. Nothing on a client or in
    /// single player (no marks there); the host's own panel is held by its own component.
    /// Known edge: Voice Control keeps one window per player and NPC, so the "false" at the close also ends the client's own
    /// window for her if its dialog with her is still open; its next keepalive (20 s at most) restores it.
    /// </summary>
    public static class ScPanelHold
    {
        /// <summary>The player stands within this many meters of her (the panel itself closes at about 6 m: XUiC_ScMemberInv.WalkedOff).</summary>
        private const float Reach = 10f;
        /// <summary>The second call after the first, and the refresh after that (Voice Control's remote window lasts 45 s).</summary>
        private const float SecondSeconds = 3f, RefreshSeconds = 20f;

        private sealed class Hold
        {
            public float Next;
            public int Calls;
        }

        private static readonly Dictionary<long, Hold> held = new Dictionary<long, Hold>();
        private static readonly List<KeyValuePair<int, int>> marks = new List<KeyValuePair<int, int>>();
        private static readonly HashSet<long> seen = new HashSet<long>();
        private static readonly List<long> scratch = new List<long>();
        private static bool bindTried;
        private static MethodInfo getOrCreate, setActive;
        private static int started, calls, released;

        /// <summary>Every machine, with ScAmmo.Reset (world start): the marks are gone, so are the backups (Voice Control's own windows lapse).</summary>
        public static void Reset()
        {
            held.Clear();
            marks.Clear();
            seen.Clear();
            started = calls = released = 0;
        }

        private static long Key(int npc, int player) { return ((long)npc << 32) | (uint)player; }
        private static int NpcOf(long key) { return (int)(key >> 32); }
        private static int PlayerOf(long key) { return (int)(key & 0xFFFFFFFFL); }

        /// <summary>Voice Control's ChatComponentManager.GetOrCreate(EntityAlive) and NPCChatComponent.SetRemoteChatActive(int, bool), once per process.</summary>
        private static bool Bind()
        {
            if (bindTried) return setActive != null;
            bindTried = true;
            try
            {
                Type comp = Patches_VoiceControl.ChatType;
                if (comp == null) return false;              // no Voice Control: nothing to back up, nothing to warn about
                Type mgr = ScTypes.Find("XNPCVoiceControl.Core.ChatComponentManager");
                MethodInfo g = mgr != null ? AccessTools.DeclaredMethod(mgr, "GetOrCreate", new[] { typeof(EntityAlive) }) : null;
                MethodInfo s = AccessTools.DeclaredMethod(comp, "SetRemoteChatActive", new[] { typeof(int), typeof(bool) });
                if (g == null || !g.IsStatic || !comp.IsAssignableFrom(g.ReturnType) || s == null || s.IsStatic)
                {
                    ScLog.Err("member panel: Voice Control's ChatComponentManager.GetOrCreate(EntityAlive) or NPCChatComponent.SetRemoteChatActive(int, bool)"
                              + " not found (Voice Control update?) - the server's backup of the panel's hold is off (a client's own Voice Control still holds her)");
                    return false;
                }
                getOrCreate = g;
                setActive = s;
                return true;
            }
            catch (Exception e)
            {
                ScLog.Once("panelhold-bind", e);
                return false;
            }
        }

        /// <summary>Voice Control's remote window for this player on her, on or off. False when it could not be set.</summary>
        private static bool Set(EntityAlive npc, int player, bool active)
        {
            if (npc == null || setActive == null) return false;
            try
            {
                object comp = getOrCreate.Invoke(null, new object[] { npc });
                if (comp == null) return false;
                setActive.Invoke(comp, new object[] { player, active });
                calls++;
                return true;
            }
            catch (Exception e)
            {
                ScLog.Once("panelhold-set", e);
                return false;
            }
        }

        /// <summary>ScTicker, once a second, the server: start, refresh and end the backups by SCore's session marks.</summary>
        public static void Tick(World w)
        {
            try
            {
                if (w == null || !ScCommunity.IsHost) return;
                ScAmmo.RemoteMarks(marks);
                if (marks.Count == 0 && held.Count == 0) return;
                if (!Bind()) { held.Clear(); return; }
                float now = Time.unscaledTime;
                seen.Clear();
                foreach (KeyValuePair<int, int> mk in marks)
                {
                    EntityAlive npc = w.GetEntity(mk.Key) as EntityAlive;
                    if (npc == null || npc.IsDead() || ScRegistry.GetMember(mk.Key) == null) continue;     // SC's panel opens for members only
                    EntityPlayer p = w.GetEntity(mk.Value) as EntityPlayer;
                    if (p == null || p is EntityPlayerLocal || !p.IsAlive()) continue;
                    if ((p.position - npc.position).sqrMagnitude > Reach * Reach) continue;
                    long key = Key(mk.Key, mk.Value);
                    seen.Add(key);
                    Hold h;
                    if (!held.TryGetValue(key, out h))
                    {
                        h = new Hold { Next = now };
                        held[key] = h;
                        started++;
                        ScLog.Verbose("member panel: the server holds " + ScCommunity.DisplayName(npc) + " for " + p.PlayerDisplayName
                                      + " while their panel is open (a backup of Voice Control's hold)");
                    }
                    if (now < h.Next) continue;
                    Set(npc, mk.Value, true);
                    h.Calls++;
                    h.Next = now + (h.Calls == 1 ? SecondSeconds : RefreshSeconds);
                }
                if (held.Count == seen.Count) return;
                // a mark that went stale, a player out of reach, dead or gone, a body gone: the backup ends
                scratch.Clear();
                foreach (long key in held.Keys) if (!seen.Contains(key)) scratch.Add(key);
                foreach (long key in scratch) End(w, key, "no longer at her panel", false);   // re-check F3: let it lapse (Voice Control's 45 s)
            }
            catch (Exception e)
            {
                ScLog.Once("panelhold-tick", e);
            }
        }

        /// <summary>
        /// One backup ends. sendFalse: tell Voice Control the hold is over (the panel's close, a disconnect). Round mt1 (re-check
        /// F3): not when it lapses by distance or a stale mark - a "false" there would also cancel the client's own Voice Control
        /// hold (a panel opened from farther away through its ring); Voice Control lets an unrefreshed remote hold lapse itself.
        /// </summary>
        private static void End(World w, long key, string why, bool sendFalse = true)
        {
            if (!held.Remove(key)) return;
            released++;
            EntityAlive npc = w != null ? w.GetEntity(NpcOf(key)) as EntityAlive : null;
            if (npc != null && sendFalse) Set(npc, PlayerOf(key), false);      // an unloaded body: Voice Control dropped its component with it
            ScLog.Verbose("member panel: the server's hold of " + (npc != null ? ScCommunity.DisplayName(npc) : "NPC " + NpcOf(key)) + " for player "
                          + PlayerOf(key) + " ends (" + why + ")");
        }

        /// <summary>ScAmmo.RemoteClosed, the server: that player's (player &lt; 0: every player's) backup on this NPC ends now.</summary>
        public static void Release(int npc, int player, string why)
        {
            if (held.Count == 0) return;
            try
            {
                World w = ScCommunity.World;
                scratch.Clear();
                foreach (long key in held.Keys)
                    if (NpcOf(key) == npc && (player < 0 || PlayerOf(key) == player)) scratch.Add(key);
                foreach (long key in scratch) End(w, key, why);
            }
            catch (Exception e)
            {
                ScLog.Once("panelhold-release", e);
            }
        }

        /// <summary>ScAmmo.ForgetPlayer (ModEvents.PlayerDisconnected), the server: that player's backups end.</summary>
        public static void ForgetPlayer(int player)
        {
            if (held.Count == 0) return;
            try
            {
                World w = ScCommunity.World;
                scratch.Clear();
                foreach (long key in held.Keys) if (PlayerOf(key) == player) scratch.Add(key);
                foreach (long key in scratch) End(w, key, "the player left the game");
            }
            catch (Exception e)
            {
                ScLog.Once("panelhold-forget", e);
            }
        }

        /// <summary>For the log and sc xnpc: the backups now and the session's counts.</summary>
        public static string Report()
        {
            return "member panel holds (server backup): " + held.Count + " now, " + started + " started, " + released + " ended, " + calls + " Voice Control calls"
                   + (bindTried && setActive == null ? " - OFF (Voice Control's methods not found)" : "");
        }
    }
}
