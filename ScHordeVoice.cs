using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>vo4: the moments of a horde at the base that are spoken (ScHordeVoice).</summary>
    public enum HordeMoment
    {
        /// <summary>At home: a guard on watch tells of the horde gathering (ScWatch's call-out, with scWatchGathering).</summary>
        Gather,
        /// <summary>At home: a guard on watch first sees an attacker (ScWatch's call-out, with scWatchSpotted).</summary>
        Spotted,
        /// <summary>At home: the attack was held with the player at home - a guard on watch says it is over (with scWatchClear).</summary>
        Clear,
        /// <summary>Away, on the radio from home: the horde is called (with scDefRadioCall).</summary>
        Call,
        /// <summary>Away, on the radio from home: it arrives and the defenders fight it alone (with scDefRadioArrived).</summary>
        Arrived,
        /// <summary>Away, on the radio from home: the fight is over and the base held (with scDefRadio).</summary>
        Held,
        /// <summary>Away, on the radio from home: the base was damaged (with scDefRadio).</summary>
        Damaged,
        /// <summary>Away, on the radio from home: the base was overrun (with scDefRadio).</summary>
        Overrun
    }

    /// <summary>
    /// vo4 (the user's script of 2026-11-14): the base's horde warnings, spoken in the speaker's own SC voice beside the texts
    /// that already show (those stay as they are: they carry the numbers). Each moment is said once, exactly when its text
    /// shows - so the attack's once-only marks (Community.AttackWarned, AttackHeard) and its one ending keep it from being
    /// said twice, a reload included (the queue is not saved: a line still waiting at a reload is never said).
    ///
    /// The lines: scHorde&lt;Gather|Spotted|Call|Arrived&gt;_&lt;side&gt; (the side one of Sides, ScBaseAttack.Side's words) and
    /// scHorde&lt;Clear|Held|Damaged|Overrun&gt;_&lt;1|2&gt; (one of the two at random); the clip &lt;key&gt;.ogg in the speaker's voice
    /// (ScVoice.MemberClip: their body's chain, or one built from their record), the words the Localization row &lt;key&gt;Said
    /// (Tools/voice_lines.py's family "horde").
    ///
    /// Who and how (the user's rules):
    ///  - At home (Gather, Spotted, Clear): the guard on watch who raised the call, from their body. Within
    ///    ScRadioTalk.InPersonRange of the player: in person - the plain voice from the body, positional (ScVoice.PlayBark, as
    ///    a greeting). Farther: on the radio, if the player carries a handheld radio (ScHandheld.Carries) and the guard can
    ///    send - a handheld radio in their inventory window or bag (ScHandheld.NpcCarries), or a radio station at home with
    ///    power (ScDefence.Radio). Otherwise no voice: the text shows alone.
    ///  - Away (Call, Arrived, Held / Damaged / Overrun): only where the radio's text shows (a station at home with power), and
    ///    only while the player carries a handheld radio. The guard on watch speaks if there is one, else a member at home (a
    ///    resident, not on a run, not missing) picked at random; nobody at home, no voice.
    /// Every line goes through ScRadioTalk's queue (QueueHorde: one line at a time with the runners', the radio's source,
    /// effect and volume - RunnerRadioEffect, RunnerRadioVolume), never touching a runner's RunTalk marks. HordeVoiceLines
    /// false: no voice at all; VoiceLines false: no clip (MemberClip), so no voice either. Host only, main thread.
    /// Round MP4 (multiplayer): a guard's warning at home is heard in person by every remote player near the guard too (the
    /// server sends it: NetPackageScVoice); a warning on the radio - farther away, or from home while away - stays the host's
    /// own player's until round MP5's radio work.
    /// Round MP5 (part R): the radio too, by the same rules on each player's machine. The server decides that the line is said,
    /// which one, in whose voice and whether the radio carries it (the guard's own handheld or the home station with power; the
    /// radio from home: a station with power) and marks it ScRadioTalk.FlagHordeRadio; each client hears it in person beside
    /// the guard, else on the radio while it carries a handheld radio (ScRadioTalk.OnRemote). At home it goes to the remote
    /// players near the guard and, when the radio carries it, to those at home (PlayerHome: the call-out's own text goes to
    /// them); away, to the remote players not at home. The host's own player is judged here, exactly as before.
    /// </summary>
    public static class ScHordeVoice
    {
        /// <summary>The eight sides, as ScBaseAttack.Side words them: the key suffix of the side lines.</summary>
        public static readonly string[] Sides = { "north", "northeast", "east", "southeast", "south", "southwest", "west", "northwest" };

        private static readonly System.Random rnd = new System.Random();

        /// <summary>A moment said by a guard at the base (the rest are said on the radio from home while the player is away).</summary>
        public static bool AtHome(HordeMoment m)
        {
            return m == HordeMoment.Gather || m == HordeMoment.Spotted || m == HordeMoment.Clear;
        }

        /// <summary>A moment whose line names the side (the rest have two variants).</summary>
        public static bool HasSide(HordeMoment m)
        {
            return m == HordeMoment.Gather || m == HordeMoment.Spotted || m == HordeMoment.Call || m == HordeMoment.Arrived;
        }

        /// <summary>ScDefence's result (Held, Damaged, Overrun) as its moment.</summary>
        public static HordeMoment ResultMoment(int result)
        {
            return result == ScDefence.Held ? HordeMoment.Held : result == ScDefence.Damaged ? HordeMoment.Damaged : HordeMoment.Overrun;
        }

        /// <summary>
        /// A side's key suffix: one of Sides, from ScBaseAttack.Side's words (or "north-east", "NE" and the like from the
        /// console); null for anything else ("center": a horde seen on the home column has no line).
        /// </summary>
        public static string SideKey(string side)
        {
            if (string.IsNullOrEmpty(side)) return null;
            string s = side.Trim().ToLowerInvariant().Replace("-", "").Replace(" ", "").Replace("_", "");
            switch (s)
            {
                case "n": return "north";
                case "ne": return "northeast";
                case "e": return "east";
                case "se": return "southeast";
                case "s": return "south";
                case "sw": return "southwest";
                case "w": return "west";
                case "nw": return "northwest";
            }
            foreach (string k in Sides) if (k == s) return k;
            return null;
        }

        /// <summary>The line's key: scHorde&lt;Moment&gt;_&lt;side&gt;, or _&lt;1|2&gt; at random; null when a side line has no known side.</summary>
        public static string KeyFor(HordeMoment m, string side)
        {
            if (HasSide(m))
            {
                string s = SideKey(side);
                return s != null ? "scHorde" + m + "_" + s : null;
            }
            return "scHorde" + m + "_" + (rnd.Next(2) + 1);
        }

        // ------------------------------------------------------------------ the speakers ----

        /// <summary>The guard on watch with a body in the world who calls out at home (the first, as ScWatch's call-outs pick), or null.</summary>
        public static Member HomeGuard(out EntityAlive body)
        {
            body = null;
            if (!ScRegistry.Loaded) return null;
            foreach (Member m in ScRegistry.Members.Values)
            {
                if (!ScWatch.OnWatch(m)) continue;
                EntityAlive npc = ScCommunity.FindNpc(m.Id);
                if (npc == null || npc.IsDead()) continue;
                if (ScDowned.IsDown(npc)) continue;     // round U2: a downed guard calls nothing out
                body = npc;
                return m;
            }
            return null;
        }

        /// <summary>Who speaks on the radio from home: the guard on watch (the first), else a member at home picked at random; null with nobody at home.</summary>
        public static Member AwaySpeaker()
        {
            if (!ScRegistry.Loaded) return null;
            var home = new List<Member>();
            foreach (Member m in ScRegistry.Members.Values)
            {
                if (m.Status != MemberStatus.Resident || ScMissing.IsMissing(m)) continue;
                if (ScWatch.OnWatch(m)) return m;
                home.Add(m);
            }
            return home.Count == 0 ? null : home[rnd.Next(home.Count)];
        }

        // ------------------------------------------------------------------ saying ----

        /// <summary>
        /// At home (Gather, Spotted, Clear): the guard m, whose body is 'body', says the moment - in person within
        /// ScRadioTalk.InPersonRange of the player, else on the radio when the player carries a handheld and the guard can send
        /// (their own handheld, or the home station with power). The caller has just shown the moment's text. force (the
        /// console): HordeVoiceLines and the radio rules ignored - in person in range, else on the radio. Returns what happened.
        /// Round MP4: anyPlayer - the host's own player is judged here as before (any other: nobody here); every remote player
        /// within InPersonRange of the guard hears it in person on their own machine (NetPackageScVoice), forced lines aside.
        /// Round t107: a forced line with a remote anyPlayer (the console's sender) goes to that player alone (SentForced).
        /// </summary>
        public static string SayAtHome(HordeMoment moment, Member m, EntityAlive body, string side, EntityPlayer anyPlayer, bool force)   // round MP4: any player (the host's own decides here)
        {
            try
            {
                if (m == null || body == null || body.IsDead()) return "nobody: no guard with a body";
                if (!force && !ScConfig.HordeVoiceLines) { ScHandheld.Count("horde off"); return "HordeVoiceLines is off"; }
                string key = KeyFor(moment, side);
                if (key == null) { ScHandheld.Count("horde unsaid: no line for the side"); return "no line for the side '" + side + "'"; }
                string voice;
                string path = ScVoice.MemberClip(m, key, out voice);
                if (path == null) return NoClip(m, key);
                // round t107: a forced line (the console) typed by a remote player is theirs alone - sent to their machine, which
                // plays it whatever its hearing rule; nothing is queued here (a dedicated server has nobody to hear it)
                if (force && anyPlayer != null && !(anyPlayer is EntityPlayerLocal)) return SentForced(anyPlayer, m, body, key, voice);
                // round t107 (review NIT 4): a dedicated server's own console or telnet - no sender and no player on this machine
                if (force && anyPlayer == null && ScCommunity.LocalPlayer == null) return "nobody on this machine hears a forced line - type it in a player's own console";
                // round MP4: the remote players within InPersonRange of the guard hear it in person on their own machine (the same
                // line, the guard's voice). Round MP5 (part R): when the guard can send (their handheld, the home station with
                // power), the remote players at home farther away get it too and hear it on their own handheld radio
                int near = force ? 0 : SendAtHome(m, body, key, voice);
                if (near > 0) ScLog.Out(m.Name + " says " + key + " in " + voice + ", sent to " + near + " other player(s) (in person, or their radio)");
                EntityPlayerLocal player = anyPlayer as EntityPlayerLocal;   // round MP4: the host's own player hears it here, as before
                // review tells n5: a dedicated server (no player of this machine's) - the remote ones were sent it above; the host's
                // radio rule below is not theirs, and would only count it as "unheard"
                if (player == null && !force && ScCommunity.LocalPlayer == null) return near > 0 ? "sent to " + near + " player(s)" : "nobody near the guard or at home to hear it";
                // m1v re-check N1: 3D, as ScRadioTalk.PlayInPerson checks it (a bark is 3D audio cut off at 40 m) - a guard high up a
                // tower is on the radio by the rule below, decided and logged here, not dropped at play time
                bool inPerson = player != null && Vector3.Distance(body.position, player.position) <= ScRadioTalk.InPersonRange;
                string how = "in person";
                if (!inPerson)
                {
                    // round MP5 (part R): the host's own player's radio, judged here (a remote player's on their machine: SendAtHome)
                    bool own = ScHandheld.NpcCarries(body);
                    bool station = !own && ScDefence.Radio();
                    if (!force)
                    {
                        if (player == null || player.IsDead() || !ScHandheld.Carries(player))
                        {
                            ScHandheld.Count("horde unheard: you carry no radio");
                            ScLog.Verbose(m.Name + "'s horde warning " + key + " went unheard (you are " + Far(body, player) + " away and carry no handheld radio)");
                            return "unheard: you are " + Far(body, player) + " away and carry no handheld radio";
                        }
                        if (!own && !station)
                        {
                            ScHandheld.Count("horde unsaid: the guard cannot send");
                            ScLog.Verbose(m.Name + "'s horde warning " + key + " went unsaid (no handheld radio on them, no station at home with power)");
                            return m.Name + " has no handheld radio and no station at home has power";
                        }
                    }
                    how = "on the radio (" + (own ? "their handheld" : station ? "the home station" : "forced") + ")";
                }
                ScRadioTalk.QueueHorde(m, key, path, voice, inPerson, player);
                ScLog.Out(m.Name + (ScWatch.OnWatch(m) ? " on watch" : "") + " says " + key + " in " + voice + ", " + how);
                return "queued " + key + " in " + voice + ", " + how;
            }
            catch (Exception e)
            {
                ScLog.Once("horde-voice-home", e);
                return "error";
            }
        }

        /// <summary>
        /// Away (Call, Arrived, Held / Damaged / Overrun): the radio from home says the moment - the station has power and the
        /// player carries a handheld; speaker null: the guard on watch, else a member at home. The caller has just shown the
        /// radio's text. force (the console): HordeVoiceLines, the station and the handheld ignored. Returns what happened.
        /// Round t107: a forced line with a remote anyPlayer (the console's sender) goes to that player alone (SentForced).
        /// </summary>
        public static string SayAway(HordeMoment moment, string side, Member speaker, EntityPlayer anyPlayer, bool force)   // round MP5 (part R): any player
        {
            try
            {
                // round MP5 (part R): the host's own player is judged here as before; the remote players away from home get the line
                // (once per moment and frame, however many players a caller loops over: SendAway) and judge their own handheld.
                // With no remote player away this runs exactly as before round MP5
                EntityPlayerLocal player = anyPlayer as EntityPlayerLocal;
                bool remote = !force && AnyRemoteAway(moment);
                if (!force)
                {
                    if (!ScConfig.HordeVoiceLines) { ScHandheld.Count("horde off"); return "HordeVoiceLines is off"; }
                    if (!ScDefence.Radio()) { ScHandheld.Count("horde unsaid: no station with power"); return "no radio station at home has power"; }
                    if (!remote && (player == null || player.IsDead() || !ScHandheld.Carries(player)))
                    {
                        ScHandheld.Count("horde unheard: you carry no radio");
                        ScLog.Verbose("the radio from home (" + moment + ") went unheard: you carry no handheld radio");
                        return "unheard: you carry no handheld radio";
                    }
                }
                Member m = speaker ?? AwaySpeaker();
                if (m == null) { ScHandheld.Count("horde unsaid: nobody at home"); return "nobody at home to speak"; }
                string key = KeyFor(moment, side);
                if (key == null) { ScHandheld.Count("horde unsaid: no line for the side"); return "no line for the side '" + side + "'"; }
                string voice;
                string path = ScVoice.MemberClip(m, key, out voice);
                if (path == null) return NoClip(m, key);
                // round t107: a forced line (the console) typed by a remote player is theirs alone, as SayAtHome's
                if (force && anyPlayer != null && !(anyPlayer is EntityPlayerLocal)) return SentForced(anyPlayer, m, null, key, voice);
                // round t107 (review NIT 4): a dedicated server's own console or telnet - no sender and no player on this machine
                if (force && anyPlayer == null && ScCommunity.LocalPlayer == null) return "nobody on this machine hears a forced line - type it in a player's own console";
                int sent = remote ? SendAway(moment, m, key, voice) : 0;
                string also = sent > 0 ? " (sent to " + sent + " other player(s))" : "";
                if (!force && (player == null || player.IsDead() || !ScHandheld.Carries(player)))
                {
                    ScHandheld.Count("horde unheard: you carry no radio");
                    ScLog.Verbose("the radio from home (" + moment + ") went unheard here: you carry no handheld radio" + also);
                    return "unheard here: you carry no handheld radio" + also;
                }
                ScRadioTalk.QueueHorde(m, key, path, voice, false, player);
                ScLog.Out(m.Name + " on the radio from home says " + key + " in " + voice + (ScWatch.OnWatch(m) ? " (on watch)" : " (at home)") + also);
                return "queued " + key + " in " + voice + ", " + m.Name + " on the radio from home" + also;
            }
            catch (Exception e)
            {
                ScLog.Once("horde-voice-away", e);
                return "error";
            }
        }

        // ------------------------------------------------------------------ round MP5 (part R): the remote players ----

        /// <summary>The moment and frame SendAway last sent (a caller that loops over the players sends it once).</summary>
        private static int awayFrame = -1;
        private static HordeMoment awayMoment;

        /// <summary>
        /// Round MP5 (part R), the server: an at-home warning to the remote players - every one within InPersonRange (3D) of the
        /// guard (in person, as round MP4 sent it; living or dead), and, when the guard can send (their handheld, or the home
        /// station with power: the host's rule), every living community player at home farther away (on their radio, if they
        /// carry a handheld). Marked FlagHordeRadio when the guard can send, so a player who walks off before it plays still
        /// hears it on their radio. Returns how many got it.
        /// </summary>
        private static int SendAtHome(Member m, EntityAlive body, string key, string voice)
        {
            if (!ScCommunity.IsHost || body == null) return 0;
            EntityPlayer[] all = ScPlayers.All.ToArray();     // copied: a send must not meet a list that changes under it
            bool anyRemote = false;
            for (int i = 0; i < all.Length && !anyRemote; i++) anyRemote = all[i] != null && !(all[i] is EntityPlayerLocal);
            if (!anyRemote) return 0;                          // single player, or the host alone: nothing to send
            bool send = ScHandheld.NpcCarries(body) || ScDefence.Radio();
            var line = new NetPackageScVoice.Line
            {
                Kind = NetPackageScVoice.KindHorde, Npc = body.entityId, Key = key, Voice = voice ?? "", Name = m.Name ?? "",
                Priority = (byte)RadioPriority.Horde, Tag = "horde", Range = ScRadioTalk.InPersonRange,
                Flags = (byte)(NetPackageScVoice.FlagLipSync | (send ? ScRadioTalk.FlagHordeRadio : 0))
            };
            float sq = ScRadioTalk.InPersonRange * ScRadioTalk.InPersonRange;
            int n = 0;
            for (int i = 0; i < all.Length; i++)
            {
                EntityPlayer p = all[i];
                if (p == null || p is EntityPlayerLocal) continue;
                bool near = (p.position - body.position).sqrMagnitude <= sq;
                if (!near && !(send && !p.IsDead() && ScPlayers.InCommunity(p) && ScBaseAttack.PlayerHome(p))) continue;
                if (ScVoice.SendTo(p, line)) n++;
            }
            return n;
        }

        /// <summary>
        /// Round t107, the server: a forced warning (the console) to the remote player who typed it, alone - FlagForced (their
        /// machine skips the hearing rule) and FlagHordeRadio (out of earshot it goes on their radio); in person from the guard's
        /// body when there is one beside them, else on the radio. Returns what happened, for the console.
        /// </summary>
        private static string SentForced(EntityPlayer to, Member m, EntityAlive body, string key, string voice)
        {
            var line = new NetPackageScVoice.Line
            {
                Kind = NetPackageScVoice.KindHorde, Npc = body != null ? body.entityId : -1, Key = key, Voice = voice ?? "", Name = m.Name ?? "",
                Priority = (byte)RadioPriority.Horde, Tag = "horde", Range = 0f,
                Flags = (byte)((body != null ? NetPackageScVoice.FlagLipSync : 0) | ScRadioTalk.FlagHordeRadio | NetPackageScVoice.FlagForced)
            };
            string who = to.PlayerDisplayName ?? to.EntityName;
            if (!ScVoice.SendTo(to, line))
                return "not sent: " + who + (ScNet.KnownMismatch(to) ? " runs another SurvivorCommunity version"
                                                                      : "'s game has not answered the server yet (still joining), or they left");
            ScLog.Out(m.Name + " says " + key + " in " + voice + ", forced - sent to " + who + " alone");
            return "sent " + key + " in " + voice + " to " + who + " (forced: their own radio rules are skipped)";
        }

        /// <summary>Round MP5 (part R), the server: a living remote community player away from home who was not sent this moment this frame.</summary>
        private static bool AnyRemoteAway(HordeMoment moment)
        {
            if (!ScCommunity.IsHost || (awayFrame == Time.frameCount && awayMoment == moment)) return false;
            List<EntityPlayer> all = ScPlayers.All;
            for (int i = 0; i < all.Count; i++)
            {
                EntityPlayer p = all[i];
                if (p != null && !(p is EntityPlayerLocal) && !p.IsDead() && ScPlayers.InCommunity(p) && !ScBaseAttack.PlayerHome(p)) return true;
            }
            return false;
        }

        /// <summary>
        /// Round MP5 (part R), the server: the radio from home to every living remote community player away from home (no body:
        /// on their radio, while they carry a handheld - FlagHordeRadio), once per moment and frame. Returns how many got it.
        /// </summary>
        private static int SendAway(HordeMoment moment, Member m, string key, string voice)
        {
            awayFrame = Time.frameCount;
            awayMoment = moment;
            var line = new NetPackageScVoice.Line
            {
                Kind = NetPackageScVoice.KindHorde, Npc = -1, Key = key, Voice = voice ?? "", Name = m.Name ?? "",
                Priority = (byte)RadioPriority.Horde, Tag = "horde", Flags = ScRadioTalk.FlagHordeRadio
            };
            EntityPlayer[] all = ScPlayers.All.ToArray();     // copied: a send must not meet a list that changes under it
            int n = 0;
            for (int i = 0; i < all.Length; i++)
            {
                EntityPlayer p = all[i];
                if (p == null || p is EntityPlayerLocal || p.IsDead() || !ScPlayers.InCommunity(p) || ScBaseAttack.PlayerHome(p)) continue;
                if (ScVoice.SendTo(p, line)) n++;
            }
            return n;
        }

        private static string NoClip(Member m, string key)
        {
            ScHandheld.Count("horde unsaid: no recording");
            string why = ScConfig.VoiceLines ? "none of " + m.Name + "'s voices has " + key : "VoiceLines is off";
            ScLog.Verbose("horde warning " + key + " not said: " + why);
            return why;
        }

        private static string Far(EntityAlive body, EntityPlayerLocal player)
        {
            if (body == null || player == null) return "?";
            return Vector3.Distance(body.position, player.position).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " m";   // m1v N1: 3D
        }

        // ------------------------------------------------------------------ the console ----

        /// <summary>sc radio horde: a moment's name (gather, spotted, clear, call, arrived, held, damaged, overrun).</summary>
        public static bool TryMoment(string s, out HordeMoment m)
        {
            m = HordeMoment.Gather;
            switch ((s ?? "").ToLowerInvariant())
            {
                case "gather": case "gathering": m = HordeMoment.Gather; return true;
                case "spotted": case "spot": m = HordeMoment.Spotted; return true;
                case "clear": case "allclear": m = HordeMoment.Clear; return true;
                case "call": m = HordeMoment.Call; return true;
                case "arrived": case "arrival": m = HordeMoment.Arrived; return true;
                case "held": m = HordeMoment.Held; return true;
                case "damaged": m = HordeMoment.Damaged; return true;
                case "overrun": m = HordeMoment.Overrun; return true;
            }
            return false;
        }

        /// <summary>For sc radio: the switch, who would speak, and how each kind of moment would be heard now.</summary>
        public static string Describe(EntityPlayerLocal player)
        {
            var sb = new StringBuilder();
            sb.Append("horde voice: HordeVoiceLines=").Append(ScConfig.HordeVoiceLines).Append(" VoiceLines=").Append(ScConfig.VoiceLines);
            EntityAlive body;
            Member g = HomeGuard(out body);
            if (g == null) sb.Append("; at home: no guard on watch with a body (no call-outs)");
            else
            {
                bool near = player != null && Vector3.Distance(body.position, player.position) <= ScRadioTalk.InPersonRange;   // m1v N1: 3D
                bool own = ScHandheld.NpcCarries(body);
                sb.Append("; at home: ").Append(g.Name).Append(" on watch, ").Append(Far(body, player)).Append(" from you - ")
                  .Append(near ? "in person"
                          : !ScHandheld.Carries(player) ? "unheard (you carry no handheld)"
                          : own ? "on the radio (their handheld)"
                          : ScDefence.Radio() ? "on the radio (the home station)"
                          : "unsaid (no handheld on them, no station with power)");
            }
            Member a = AwaySpeaker();
            sb.Append("; away: ").Append(a == null ? "nobody at home" : (ScWatch.OnWatch(a) ? a.Name + " on watch" : "a resident (random, e.g. " + a.Name + ")"))
              .Append(", ").Append(!ScDefence.Radio() ? "no station with power (no radio text, no voice; the result waits for your return)" : !ScHandheld.Carries(player) ? "the text only (you carry no handheld)" : "voiced with the text");
            return sb.ToString();
        }
    }
}
