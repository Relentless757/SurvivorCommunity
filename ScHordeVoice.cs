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
        /// </summary>
        public static string SayAtHome(HordeMoment moment, Member m, EntityAlive body, string side, EntityPlayerLocal player, bool force)
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
                // m1v re-check N1: 3D, as ScRadioTalk.PlayInPerson checks it (a bark is 3D audio cut off at 40 m) - a guard high up a
                // tower is on the radio by the rule below, decided and logged here, not dropped at play time
                bool inPerson = player != null && Vector3.Distance(body.position, player.position) <= ScRadioTalk.InPersonRange;
                string how = "in person";
                if (!inPerson)
                {
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
        /// </summary>
        public static string SayAway(HordeMoment moment, string side, Member speaker, EntityPlayerLocal player, bool force)
        {
            try
            {
                if (!force)
                {
                    if (!ScConfig.HordeVoiceLines) { ScHandheld.Count("horde off"); return "HordeVoiceLines is off"; }
                    if (!ScDefence.Radio()) { ScHandheld.Count("horde unsaid: no station with power"); return "no radio station at home has power"; }
                    if (player == null || player.IsDead() || !ScHandheld.Carries(player))
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
                ScRadioTalk.QueueHorde(m, key, path, voice, false, player);
                ScLog.Out(m.Name + " on the radio from home says " + key + " in " + voice + (ScWatch.OnWatch(m) ? " (on watch)" : " (at home)"));
                return "queued " + key + " in " + voice + ", " + m.Name + " on the radio from home";
            }
            catch (Exception e)
            {
                ScLog.Once("horde-voice-away", e);
                return "error";
            }
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
