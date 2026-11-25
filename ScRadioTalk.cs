using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>R2: the moments of a supply run a runner with a radio speaks up at - Member.RunTalk's bits, each said at most once a run.</summary>
    [Flags]
    public enum RadioTalk
    {
        None = 0,
        /// <summary>Physical run, Outbound: OutRange meters from their post ("Heading out now.").</summary>
        Out = 1,
        /// <summary>Physical run: they reach the site (ScRunners.EnterSite) and do not radio for help there.</summary>
        Arrived = 2,
        /// <summary>Physical run: the site search is over with at least an average haul (RunYieldBase) - LeaveSite.</summary>
        Found = 4,
        /// <summary>Physical run: the site search is over with less than an average haul.</summary>
        Empty = 8,
        /// <summary>Any run: they radio for help (ScRadio.ForceDistress).</summary>
        Trouble = 16,
        /// <summary>Physical run, Inbound: HomeRange meters from the site ("Done here. Heading back home.").</summary>
        Home = 32,
        /// <summary>Physical run: they reach the gate near home, where the haul counts (ScRunners.EnterBack).</summary>
        Near = 64,
        /// <summary>Told to come home from the radio window (XUiC_ScRadio's Call home).</summary>
        RecallAck = 128,
        /// <summary>Round r2: badly hurt with no way to heal, they give the job up and head home (ScRunners.TurnHomeHurt). Caption only.</summary>
        HurtHome = 256
    }

    /// <summary>
    /// R2, decisions 4 and 6 (research_radio_DESIGN_R2.md): a runner who carries a handheld radio (Member.RunRadio, ScHandheld)
    /// talks to the player during a supply run. Each moment of the run (RadioTalk) has one or two recorded lines - Localization
    /// keys scRadioRun&lt;Event&gt;_&lt;n&gt;, the clip &lt;key&gt;.ogg in their SC voice (ScVoice.MemberClip: their body's chain, or one built
    /// from their record while the body is unloaded), the caption the row &lt;key&gt;Said - said at most once a run, and only
    /// heard while the player carries a handheld radio too (radio to radio, no station needed; the acknowledgment of a recall
    /// is heard on whichever radio sent it). A moment that passes unheard (no radio either side, the player without one, the
    /// runner beside the player, RunnerRadioTalk off) is spent all the same.
    ///
    /// Lines play one at a time from a queue (Tick, once a second): the caption is a tell, "Mara on the radio: "...""; with a
    /// recording the voice plays as a radio - SC's own 2D source (heard wherever the player is), through a high-pass, a
    /// low-pass and a little distortion, a squelch of noise in and out (RunnerRadioEffect) - and the caption shows as it
    /// starts. Without one the caption shows alone. The clip asset is the one the same survivor's talk uses (ScClipCache), so a
    /// line in person sounds as before: the filters belong to the radio's source only.
    ///
    /// Hidden ("at home") runs have no route, so of these moments only Trouble happens to them (at their send, as before);
    /// the rest are physical runs'. Host only, main thread.
    ///
    /// vo4 (2026-11-14): the horde warnings (ScHordeVoice) share the queue (QueueHorde): on the same radio source with the same
    /// effect and volume, or said in person from the speaker's body (ScVoice.PlayBark, plain and positional, as a greeting
    /// is). They carry no caption of the queue's own - the moment's own on-screen text is the caption - and they never touch a
    /// runner's RunTalk marks.
    ///
    /// Round m1v: anyone's line (RadioSpeaker: an enclave's or a band's person, later the Military's operator) comes in the
    /// same way (QueueSpoken: ScRadioSpeaker.Say, ScEncRadio), and the queue keeps an order (RadioPriority): a siege call, then
    /// the horde warnings, then a runner's moments, then every other enclave line - the highest waiting plays first (the
    /// oldest of them first), and a full queue (QueueMax) drops its lowest, or the new line when every waiting one is higher.
    ///
    /// Round MP4 (multiplayer, C_world 15): the server decides that a line is said, which one and in which voice, and sends it
    /// to the players it is for (NetPackageScVoice); each client runs this same queue for its own player (OnRemote, ClientTick)
    /// with its own hearing rule - its own bag's handheld radio, the stations near it, the body beside it. The host's own
    /// player hears its lines here, exactly as before.
    /// </summary>
    public static class ScRadioTalk
    {
        /// <summary>A runner this far from their post on the way out says they are off.</summary>
        public const float OutRange = 60f;
        /// <summary>A runner this far from the site on the way back says they are heading home.</summary>
        public const float HomeRange = 40f;
        /// <summary>A runner's body this close to the player talks in person, not on the radio: the moment passes unsaid.</summary>
        public const float InPersonRange = 30f;
        /// <summary>A caption-only line holds the queue this long (real seconds), so the next one does not cover it.</summary>
        private const float CaptionSeconds = 4f;

        private static readonly Dictionary<RadioTalk, string[]> Keys = new Dictionary<RadioTalk, string[]>
        {
            { RadioTalk.Out, new[] { "scRadioRunOut_1", "scRadioRunOut_2" } },
            { RadioTalk.Arrived, new[] { "scRadioRunArrived_1", "scRadioRunArrived_2" } },
            { RadioTalk.Found, new[] { "scRadioRunFound_1", "scRadioRunFound_2" } },
            { RadioTalk.Empty, new[] { "scRadioRunEmpty_1", "scRadioRunEmpty_2" } },
            { RadioTalk.Trouble, new[] { "scRadioRunTrouble_1", "scRadioRunTrouble_2" } },
            { RadioTalk.Home, new[] { "scRadioRunHome_1", "scRadioRunHome_2" } },
            { RadioTalk.Near, new[] { "scRadioRunNear_1" } },
            { RadioTalk.RecallAck, new[] { "scRadioRecallAck_1" } },
            { RadioTalk.HurtHome, new[] { "scRadioRunHurtHome_1" } },                // round r2: no recording - the caption alone
        };

        /// <summary>
        /// Round 33c (M33): a mining run's own lines where the supply run's do not fit (no recording: the caption alone) - reaching
        /// the spot and a poor haul; the good-haul line and the rest are the supply run's.
        /// </summary>
        private static readonly Dictionary<RadioTalk, string[]> MineKeys = new Dictionary<RadioTalk, string[]>
        {
            { RadioTalk.Arrived, new[] { "scRadioRunMineArrived_1" } },
            { RadioTalk.Empty, new[] { "scRadioRunDigEmpty_1" } },
        };
        private static readonly Dictionary<RadioTalk, string[]> ClayKeys = new Dictionary<RadioTalk, string[]>
        {
            { RadioTalk.Arrived, new[] { "scRadioRunClayArrived_1" } },
            { RadioTalk.Empty, new[] { "scRadioRunDigEmpty_1" } },
        };

        private sealed class Line
        {
            public int MemberId;
            public string Name, Key, Words, Path, Voice;
            public RadioTalk Event;
            /// <summary>vo4: false for a horde warning - no caption of the queue's own (the moment shows its own text).</summary>
            public bool Caption = true;
            /// <summary>vo4: a horde warning said in person, from the speaker's body (ScVoice.PlayBark), not on the radio.</summary>
            public bool InPerson;
            // ---- round m1v ----
            /// <summary>Round m1v: the line's place in the queue (RadioPriority): the highest waiting plays first, the lowest goes when it is full.</summary>
            public RadioPriority Priority = RadioPriority.Runner;
            /// <summary>
            /// Round m1v: an in-person line of someone outside the community (an enclave's or a band's person): their body's id
            /// and birth time (-1: a member's body, ScCommunity.FindNpc(MemberId), as vo4's).
            /// </summary>
            public int EntityId = -1;
            public ulong EntityBorn;
            /// <summary>Round m1v: the counters' word for a line with no caption of the queue's own ("horde", "enc").</summary>
            public string Tag = "horde";
            /// <summary>The m1v review's fix 7: who says it (QueueSpoken), so a line that waited is skipped once they are gone; null for vo4's and R2's.</summary>
            public RadioSpeaker Speaker;
            /// <summary>
            /// Round MP4: a line the server sent to this client (OnRemote) - its body by entity id alone (a client has no records
            /// and no birth times to check), and a horde warning that is out of earshot by play time is dropped, not moved to the
            /// radio (the guard's radio is the server's to judge: MP5).
            /// </summary>
            public bool Remote;
        }

        /// <summary>Round m1v: a list, so the next line is picked by priority (Enqueue, Next); never more than QueueMax.</summary>
        private static readonly List<Line> queue = new List<Line>();
        /// <summary>The most lines that wait (never a backlog).</summary>
        public const int QueueMax = 8;
        private static readonly System.Random rnd = new System.Random();
        /// <summary>Real time until which the queue waits (a caption on screen, a clip loading or playing).</summary>
        private static float busyUntil;
        /// <summary>Bumped by Reset: a coroutine or a clip load that finishes late checks it.</summary>
        private static int token;
        /// <summary>
        /// The m1v review's fix 3: real time until which an in-person line said outside the queue (ScVoice.SayAfterTalk: a join's
        /// parting line) plays - the queue waits for it as for its own (HoldFor), so no radio line talks over it.
        /// </summary>
        private static float holdUntil;

        // the radio's own source: a 2D AudioSource on a GameObject of its own, with its filters
        private static GameObject audioGo;
        private static AudioSource source;
        private static AudioHighPassFilter highPass;
        private static AudioLowPassFilter lowPass;
        private static AudioDistortionFilter distortion;
        private static AudioClip squelchIn, squelchOut;

        /// <summary>Per world (ScConfig.Reset) and as the world shuts down: the queue, the source and the clips go.</summary>
        public static void Reset()
        {
            token++;
            queue.Clear();
            busyUntil = 0f;
            holdUntil = 0f;                                            // the m1v review's fix 3
            nextClientTick = 0f;                                       // round MP4
            try
            {
                if (source != null) source.Stop();
                if (audioGo != null) UnityEngine.Object.Destroy(audioGo);
                if (squelchIn != null) UnityEngine.Object.Destroy(squelchIn);
                if (squelchOut != null) UnityEngine.Object.Destroy(squelchOut);
            }
            catch (Exception e)
            {
                ScLog.Once("radio-talk-reset", e);
            }
            audioGo = null;
            source = null;
            highPass = null;
            lowPass = null;
            distortion = null;
            squelchIn = null;
            squelchOut = null;
        }

        // ------------------------------------------------------------------ the moments ----

        /// <summary>Marks moments as spent without a word (a recall makes the site's haul and the way home moot).</summary>
        public static void Mark(Member m, RadioTalk bits)
        {
            if (m != null) m.RunTalk |= (int)bits;
        }

        public static bool Said(Member m, RadioTalk ev) { return m != null && (m.RunTalk & (int)ev) != 0; }

        /// <summary>The site search is over (ScRunners.LeaveSite, not a rescue, a recall or a failed distress): a good haul or a poor one.</summary>
        public static void OnLeftSite(Member m, EntityPlayer player)    // round MP0: any player (runner ticks); Say decides who hears
        {
            if (m == null) return;
            // round 33c: a mining run's yardstick is the haul a tier 1 tool brings at 1 star (ScMining.GoodHaul)
            bool good = ScMining.IsDig(m) ? ScMining.GoodHaul(m) : m.RunCarry >= Math.Max(1, ScConfig.RunYieldBase);
            Mark(m, good ? RadioTalk.Empty : RadioTalk.Found);       // one line of the two
            Say(m, good ? RadioTalk.Found : RadioTalk.Empty, player, false);
        }

        /// <summary>Told to come home over the radio (XUiC_ScRadio): the acknowledgment; the way home needs no other line.</summary>
        public static void OnRecalled(Member m, EntityPlayer player)      // round MP2: any player (Say takes one)
        {
            Mark(m, RadioTalk.Found | RadioTalk.Empty | RadioTalk.Home);
            Say(m, RadioTalk.RecallAck, player, false);
            ScHandheld.Count("recalled by radio");
        }

        /// <summary>
        /// A runner's moment: said on the radio when they carry one, the player can hear it and it was not said this run (force:
        /// the console - any runner, said or not, heard or not). Spent either way. The result says what happened (the console).
        /// The caller saves (the registry keeps RunTalk).
        /// </summary>
        public static string Say(Member m, RadioTalk ev, EntityPlayer player, bool force)     // round MP0: any player; heard by the host's own only
        {
            try
            {
                if (m == null || ev == RadioTalk.None) return "nobody";
                if (!force && Said(m, ev)) return "already said this run";
                Mark(m, ev);
                if (!force)
                {
                    if (!ScConfig.RunnerRadioTalk) { ScHandheld.Count("talk off"); return "RunnerRadioTalk is off"; }
                    if (!m.RunRadio) { ScHandheld.Count("talk unsaid: runner has no radio"); return m.Name + " carries no radio"; }
                }
                // round MP4: the line and the voice are picked once, here, so every player hears the same one; the remote players
                // get it first (each client judges its own radio: OnRemote), then the host's own player as before
                string[] keys;
                Dictionary<RadioTalk, string[]> own = !ScMining.IsDig(m) ? null : m.RunJob == RunJob.Clay ? ClayKeys : MineKeys;   // round 33c
                if ((own == null || !own.TryGetValue(ev, out keys)) && !Keys.TryGetValue(ev, out keys)) return "no line for " + ev;
                if (keys == null || keys.Length == 0) return "no line for " + ev;
                string key = keys[keys.Length == 1 ? 0 : rnd.Next(keys.Length)];
                string voice;
                string path = ScVoice.MemberClip(m, key, out voice);
                int sent = SendRunner(m, ev, key, path != null ? voice : null, player, force);
                string also = sent > 0 ? " (sent to " + sent + " other player(s))" : "";
                // round MP4: a forced line (the console) is for whoever typed it - a remote one's plays on their machine only
                if (force && player != null && !(player is EntityPlayerLocal)) return "sent " + key + " to " + player.PlayerDisplayName + also;
                if (!force)
                {
                    // round MP0: the queue plays on this machine for its own player - a remote player (a dedicated server's anchor)
                    // does not hear it here, and the moment is spent as unheard here. Round MP4: they heard it on their own machine
                    EntityPlayerLocal lp = player as EntityPlayerLocal;
                    if (lp == null || lp.IsDead() || (ev != RadioTalk.RecallAck && !ScHandheld.Carries(lp)))
                    {
                        ScHandheld.Count("talk unheard: you carry no radio");
                        ScLog.Verbose(m.Name + "'s radio moment " + ev + " went unheard (you carry no handheld radio)" + also);
                        return "unheard: you carry no handheld radio" + also;
                    }
                    if (InPerson(m, lp)) { ScHandheld.Count("talk unsaid: in person"); return m.Name + " is right here" + also; }
                }
                string words = Localization.Get(key + "Said");
                if (string.IsNullOrEmpty(words) || words == key + "Said") words = key;
                // round m1v: at a runner's priority; a full queue drops its lowest line (the oldest of them), this one when it is the lowest
                if (!Enqueue(new Line { MemberId = m.Id, Name = m.Name, Key = key, Words = words, Path = path, Voice = voice, Event = ev, Priority = RadioPriority.Runner }))
                    return "dropped: the radio queue is full of more urgent lines";
                ScLog.Out(m.Name + " on the radio (" + ev + "): " + key + (path != null ? " in " + voice : " - no recording, the caption alone"));
                return "queued " + key + (path != null ? " in " + voice : " (no recording: the caption alone)");
            }
            catch (Exception e)
            {
                ScLog.Once("radio-talk-say", e);
                return "error";
            }
        }

        /// <summary>
        /// Round MP4 (C_world 15), server: a runner's moment to the remote players - every community player online, each judging
        /// on their own machine whether they hear it (their own handheld radio; the runner's body beside them: in person, unsaid);
        /// a recall's answer to the player who recalled them only (heard on the radio that sent it), a forced line (the console)
        /// to whoever typed it. voice null: no recording - the caption alone. Returns how many got it.
        /// </summary>
        private static int SendRunner(Member m, RadioTalk ev, string key, string voice, EntityPlayer player, bool force)
        {
            EntityAlive body = ScRunners.IsPhysical(m) && !m.Offline ? ScCommunity.FindNpc(m.Id) : null;
            var line = new NetPackageScVoice.Line
            {
                Kind = NetPackageScVoice.KindRadio, Npc = body != null ? body.entityId : -1, Key = key, Voice = voice ?? "",
                Caption = NetPackageScVoice.CaptionLoc, CaptionKey = key + "Said", Name = m.Name ?? "", Priority = (byte)RadioPriority.Runner,
                Flags = (byte)(NetPackageScVoice.FlagRunner | (ev == RadioTalk.RecallAck ? NetPackageScVoice.FlagNoHandheld : 0) | (force ? NetPackageScVoice.FlagForced : 0))
            };
            if (ev == RadioTalk.RecallAck || force) return ScVoice.SendTo(player, line) ? 1 : 0;
            return ScVoice.SendCommunity(line);
        }

        /// <summary>A physical runner's body within InPersonRange of the player: they would just talk (and the community window
        /// may turn them round without a radio: ScHandheld.WindowRecallAllowed).</summary>
        internal static bool InPerson(Member m, EntityPlayer player)      // round MP2: any player (a position)
        {
            if (player == null || !ScRunners.IsPhysical(m) || m.Offline) return false;
            EntityAlive body = ScCommunity.FindNpc(m.Id);
            return body != null && ScRunners.XZ(body.position, player.position) <= InPersonRange;
        }

        /// <summary>
        /// vo4 (ScHordeVoice): a horde warning into the queue - its clip (path, in voice) on the radio's source, or in person from
        /// the speaker's body - with no caption of the queue's own and no RunTalk mark. It starts at once when the queue is idle,
        /// with the moment's own text, rather than at the next tick.
        /// </summary>
        internal static void QueueHorde(Member m, string key, string path, string voice, bool inPerson, EntityPlayerLocal player)
        {
            if (m == null || string.IsNullOrEmpty(path)) return;
            // round m1v: at the horde's priority (above a runner's moments and enclave lines, below a siege call)
            if (!Enqueue(new Line { MemberId = m.Id, Name = m.Name, Key = key, Words = key, Path = path, Voice = voice, Caption = false, InPerson = inPerson, Priority = RadioPriority.Horde }))
                return;
            PlayNext(player);
        }

        // ------------------------------------------------------------------ round m1v: anyone's line, the priorities ----

        /// <summary>
        /// Round m1v (RadioSpeaker): a speaker's line into the queue at its priority - its clip (path, in voice) on the radio's
        /// source, or said in person from their body (inPerson: their body near the player; ScVoice.PlayBark, as vo4's), with no
        /// caption of the queue's own (the caller shows its text) and no runner's RunTalk marks. It starts at once when the
        /// queue is idle. False: the queue was full of higher lines and this one was dropped.
        /// </summary>
        internal static bool QueueSpoken(RadioSpeaker s, string key, string path, string voice, EntityAlive inPerson, RadioPriority prio, string tag, EntityPlayerLocal player)
        {
            if (s == null || string.IsNullOrEmpty(path)) return false;
            bool member = s.Kind == RadioSpeaker.Kinds.Member;
            var line = new Line
            {
                MemberId = member ? s.MemberId : 0, Name = s.Name, Key = key, Words = key, Path = path, Voice = voice, Caption = false,
                InPerson = inPerson != null, Priority = prio, Tag = string.IsNullOrEmpty(tag) ? "enc" : tag, Speaker = s,
                EntityId = inPerson != null && !member ? inPerson.entityId : -1, EntityBorn = inPerson != null ? inPerson.WorldTimeBorn : 0UL
            };
            if (!Enqueue(line)) return false;
            PlayNext(player);
            return true;
        }

        // ------------------------------------------------------------------ round MP4: a client's own radio ----

        /// <summary>Round MP4: when a client's queue next looks for a line (ClientTick).</summary>
        private static float nextClientTick;

        /// <summary>
        /// Round MP4 (C_world 15), client: a radio line the server sent (NetPackageScVoice, kinds radio and horde). The server
        /// decided that it is said, which line and in which voice; this machine decides whether its own player hears it, by the
        /// host's rules, and plays it in its own queue at its priority, as the host does:
        ///  - a runner's moment: the player's own handheld radio (a recall's answer always), and not with the runner's body beside
        ///    them (in person: unsaid); the caption "Mara on the radio: ..." in this machine's language, alone without a recording;
        ///  - a speaker's line (an enclave's, a band's): in person when their body stands within InPersonRange of the player, else
        ///    on the radio when ScRadioSpeaker.Hears (the player's handheld, or a powered SC station near them; a station's power
        ///    on a client is what the client's world shows);
        ///  - a guard's horde warning: in person (the server sent it to the players near the guard). MP5: on the radio.
        /// A forced line (the console) skips the hearing rule. A voice this copy of the mod has no clip of is not said (a runner's
        /// caption still shows).
        /// </summary>
        internal static void OnRemote(NetPackageScVoice.Line d)
        {
            try
            {
                EntityPlayerLocal lp = ScCommunity.LocalPlayer;
                if (d == null || lp == null) return;
                bool forced = d.Has(NetPackageScVoice.FlagForced);
                string path = ScVoice.PathIn(d.Voice, d.Key);
                World w = ScCommunity.World;
                EntityAlive body = w != null && d.Npc >= 0 ? w.GetEntity(d.Npc) as EntityAlive : null;
                string name = string.IsNullOrEmpty(d.Name) ? "?" : d.Name;
                var prio = (RadioPriority)d.Priority;
                if (d.Kind == NetPackageScVoice.KindHorde)
                {
                    if (path == null) { ScHandheld.Count("horde unsaid: no recording here"); return; }
                    if (Enqueue(new Line { Name = name, Key = d.Key, Words = d.Key, Path = path, Voice = d.Voice, Caption = false, InPerson = true, Priority = RadioPriority.Horde, Tag = "horde", EntityId = d.Npc, Remote = true }))
                        PlayNext(lp);
                    return;
                }
                if (d.Has(NetPackageScVoice.FlagRunner))
                {
                    if (!forced)
                    {
                        if (lp.IsDead() || (!d.Has(NetPackageScVoice.FlagNoHandheld) && !ScHandheld.Carries(lp)))
                        {
                            ScHandheld.Count("talk unheard: you carry no radio");
                            ScLog.Verbose(name + "'s radio moment " + d.Key + " went unheard (you carry no handheld radio)");
                            return;
                        }
                        if (body != null && ScRunners.XZ(body.position, lp.position) <= InPersonRange) { ScHandheld.Count("talk unsaid: in person"); return; }
                    }
                    string words = ScVoice.CaptionOf(d) ?? d.Key;
                    // the next tick plays it, as the host's (ClientTick, like the server's Tick)
                    Enqueue(new Line { Name = name, Key = d.Key, Words = words, Path = path, Voice = d.Voice, Priority = prio, Remote = true });
                    return;
                }
                string tag = string.IsNullOrEmpty(d.Tag) ? "enc" : d.Tag;
                if (path == null) { ScHandheld.Count(tag + " unsaid: no recording here"); return; }
                if (d.Has(NetPackageScVoice.FlagInPerson) && body != null && !body.IsDead() && Vector3.Distance(body.position, lp.position) <= InPersonRange)
                {
                    if (ScDowned.IsDown(body)) { ScHandheld.Count(tag + " unsaid: they are down"); return; }
                    if (Enqueue(new Line { Name = name, Key = d.Key, Words = d.Key, Path = path, Voice = d.Voice, Caption = false, InPerson = true, Priority = prio, Tag = tag, EntityId = d.Npc, Remote = true }))
                        PlayNext(lp);
                    return;
                }
                string how;
                if (!ScRadioSpeaker.Hears(lp, out how) && !forced)
                {
                    ScHandheld.Count(tag + " unheard");
                    ScLog.Verbose(name + " on the radio, " + d.Key + ", went unheard: " + how);
                    return;
                }
                if (Enqueue(new Line { Name = name, Key = d.Key, Words = d.Key, Path = path, Voice = d.Voice, Caption = false, Priority = prio, Tag = tag, Remote = true }))
                    PlayNext(lp);
            }
            catch (Exception e)
            {
                ScLog.Once("radio-talk-remote", e);
            }
        }

        /// <summary>Round MP4, client (ScBarks.Tick, every frame; looks four times a second): the next line of this machine's own queue.</summary>
        internal static void ClientTick()
        {
            if (queue.Count == 0) return;
            float now = Time.unscaledTime;
            if (now < nextClientTick) return;
            nextClientTick = now + 0.25f;
            EntityPlayerLocal lp = ScCommunity.LocalPlayer;
            if (lp != null) PlayNext(lp);
        }

        /// <summary>
        /// Round m1v: a line into the queue. Full (QueueMax): the lowest waiting line goes - the oldest of them - or this one,
        /// when every waiting line is higher. False: this one was dropped.
        /// </summary>
        private static bool Enqueue(Line line)
        {
            if (queue.Count >= QueueMax)
            {
                int low = 0;
                for (int i = 1; i < queue.Count; i++) if (queue[i].Priority < queue[low].Priority) low = i;
                Line gone = line.Priority < queue[low].Priority ? line : queue[low];
                if (!ReferenceEquals(gone, line)) queue.RemoveAt(low);
                ScHandheld.Count("queue full: dropped a line (" + gone.Priority + ")");
                ScLog.Verbose("the radio queue is full: " + gone.Name + "'s " + gone.Key + " (" + gone.Priority + ") dropped"
                              + (ReferenceEquals(gone, line) ? " - every waiting line is more urgent" : " for " + line.Name + "'s " + line.Key + " (" + line.Priority + ")"));
                if (ReferenceEquals(gone, line)) return false;
            }
            queue.Add(line);
            return true;
        }

        /// <summary>Round m1v: the next line - the highest priority waiting, the oldest of those. The queue is not empty.</summary>
        private static Line Next()
        {
            int best = 0;
            for (int i = 1; i < queue.Count; i++) if (queue[i].Priority > queue[best].Priority) best = i;
            Line l = queue[best];
            queue.RemoveAt(best);
            return l;
        }

        /// <summary>Round m1v: an in-person line's body when the speaker is not a member - by id, and only while it is the same body.</summary>
        private static EntityAlive BodyOf(Line line)
        {
            World w = ScCommunity.World;
            // round MP4: a line from the server (Remote) by id alone - a client's copy has no birth time to check it by
            if (line.Remote) return w != null && line.EntityId >= 0 ? w.GetEntity(line.EntityId) as EntityAlive : null;
            EntityAlive npc = w != null && line.EntityId >= 0 ? ScNpc.As(ScNpc.Loaded(w, line.EntityId)) : null;
            return npc != null && npc.WorldTimeBorn == line.EntityBorn ? npc : null;
        }

        /// <summary>
        /// The m1v review's fix 3: the queue waits at least this many seconds from now (an in-person line said outside it, as a
        /// talk closes, is playing or loading). Never shortens a wait already set.
        /// </summary>
        internal static void HoldFor(float seconds, bool replace = false)
        {
            float until = Time.unscaledTime + Mathf.Max(0f, seconds);
            if (replace || until > holdUntil) holdUntil = until;   // m1v N3: replace - the clip started, its own length now
        }

        /// <summary>The m1v re-check's N3: the in-person line the queue held for never played - the queue goes on.</summary>
        internal static void ReleaseHold()
        {
            holdUntil = 0f;
        }

        /// <summary>Round m1v: the log's word for a line with no caption of its own.</summary>
        private static string What(Line line) { return line.Tag == "horde" ? "horde warning" : line.Tag + " line"; }

        /// <summary>
        /// ScTicker, once a second (host): the moments that are places on the route - leaving home behind (Out), leaving the
        /// site behind (Home) - then the next line of the queue.
        /// </summary>
        public static void Tick(World w, EntityPlayer player)          // round MP0: the ticker's anchor - any server
        {
            try
            {
                if (w == null || player == null || !ScRegistry.Loaded) return;
                bool changed = false;
                foreach (Member m in ScRegistry.Members.Values)
                {
                    if (!m.RunRadio || !ScRunners.IsRun(m)) continue;          // round op1: runs only (a posting journey says nothing)
                    if (m.RunLeg == RunLeg.Outbound && !Said(m, RadioTalk.Out))
                    {
                        if (ScRunners.XZ(ScRunners.Where(m), ScCommunity.PostCenter(m)) >= OutRange) { Say(m, RadioTalk.Out, player, false); changed = true; }
                    }
                    else if (m.RunLeg == RunLeg.Inbound && !Said(m, RadioTalk.Home) && m.RunSite != Vector3i.zero)
                    {
                        if (ScRunners.XZ(ScRunners.Where(m), ScRunners.C(m.RunSite)) >= HomeRange) { Say(m, RadioTalk.Home, player, false); changed = true; }
                    }
                }
                if (changed) ScRegistry.SaveAtomic();
                EntityPlayerLocal lp = player as EntityPlayerLocal;
                if (lp != null) PlayNext(lp);         // round MP0: played on the host's own speakers only. Round MP4: a client plays its own (ClientTick)
            }
            catch (Exception e)
            {
                ScLog.Once("radio-talk-tick", e);
            }
        }

        // ------------------------------------------------------------------ recall (decision 6) ----

        /// <summary>
        /// Can this runner be told to come home over the radio now? A physical run (a hidden one has no route to turn round on),
        /// the runner's radio, not pinned down (help them or wait), not already on the way home. why: a short reason (the button).
        /// </summary>
        public static bool CanRecall(Member m, out string why)
        {
            why = null;
            if (m == null || m.Status != MemberStatus.OnRun) { why = Localization.Get("scRadioRunNone"); return false; }
            if (!ScRunners.IsPhysical(m)) { why = Localization.Get("scRadioRunNoRoute"); return false; }
            if (!m.RunRadio) { why = Localization.Get("scRadioRunNoRadio"); return false; }
            if (ScRunners.Pinned(m)) { why = Localization.Get("scRadioRunPinnedShort"); return false; }
            if (m.RunLeg != RunLeg.Outbound && m.RunLeg != RunLeg.AtSite) { why = Localization.Get("scRadioRunHeadingShort"); return false; }
            return true;
        }

        // ------------------------------------------------------------------ playing ----

        private static void PlayNext(EntityPlayerLocal player)
        {
            // round MP0: the radio's source and the barks are this machine's own - nothing plays with no player of its own (a
            // dedicated server: a horde warning or a forced line queued there waits unplayed). Round MP4: the remote players got
            // their copy from the server (OnRemote) and play it on their own machine
            if (player == null && ScCommunity.LocalPlayer == null) return;
            float now = Time.unscaledTime;
            if (queue.Count == 0 || now < busyUntil || now < holdUntil) return;   // m1v fix 3: a parting line in person holds it too
            Line line = Next();                                        // round m1v: the highest priority first
            if (line.InPerson) { PlayInPerson(line, player); return; } // vo4: a horde warning from the guard's body (m1v: anyone's)
            PlayRadio(line, player);
        }

        /// <summary>
        /// A line on the radio's source (a runner's caption with it), or its caption alone. The m1v review's fix 7: a speaker's
        /// line whose speaker is no longer alive, or whose group is gone, is skipped (its text showed when it was queued).
        /// </summary>
        private static void PlayRadio(Line line, EntityPlayerLocal player)
        {
            float now = Time.unscaledTime;
            string gone;
            if (line.Speaker != null && !line.Speaker.StillThere(out gone))
            {
                ScHandheld.Count(line.Tag + " unsaid: the speaker is gone");
                ScLog.Verbose(line.Name + "'s " + What(line) + " " + line.Key + " was not said: " + gone);
                return;                                                // nothing plays: the next line may go at the next tick
            }
            if (line.Path == null)
            {
                if (!line.Caption) return;                             // vo4: nothing to say (a horde warning always comes with its clip)
                Caption(player, line);
                ScHandheld.Count("talk said: caption alone");
                busyUntil = now + CaptionSeconds;
                return;
            }
            busyUntil = now + 30f;                                     // while the clip loads (lowered when it plays)
            int my = token;
            ScClipCache.Get(line.Path, true, clip =>
            {
                try
                {
                    if (my != token) return;
                    EntityPlayerLocal p = ScCommunity.LocalPlayer;
                    if (line.Caption) Caption(p, line);                // vo4: a horde warning's caption is the moment's own text
                    if (clip == null || GameManager.Instance == null)
                    {
                        ScHandheld.Count(line.Caption ? "talk said: caption alone (clip did not load)" : line.Tag + " unsaid: clip did not load");
                        busyUntil = Time.unscaledTime + (line.Caption ? CaptionSeconds : 0.3f);
                        return;
                    }
                    ScHandheld.Count(line.Caption ? "talk said: voiced" : line.Tag + " said: on the radio");
                    bool effect = ScConfig.RunnerRadioEffect;
                    busyUntil = Time.unscaledTime + clip.length + (effect ? 0.6f : 0.3f) + 30f;   // the coroutine lowers it when the line ends
                    GameManager.Instance.StartCoroutine(Guarded(PlayLine(clip, Mathf.Clamp01(ScConfig.RunnerRadioVolume), effect, my)));
                }
                catch (Exception e)
                {
                    ScLog.Once("radio-talk-play", e);
                    busyUntil = Time.unscaledTime + CaptionSeconds;
                }
            });
        }

        /// <summary>
        /// vo4: a horde warning said in person - the plain voice from the speaker's body, positional, as a greeting or chatter
        /// is (ScVoice.PlayBark: through Voice Control's player for the lip sync, else SC's own source on a child; SC's own
        /// source while Voice Control is saying something on them, so its queue is never cut). The queue waits for the line.
        /// </summary>
        private static void PlayInPerson(Line line, EntityPlayerLocal player)
        {
            EntityAlive npc = line.EntityId >= 0 ? BodyOf(line) : ScCommunity.FindNpc(line.MemberId);   // round m1v: anyone's body
            if (npc == null || npc.IsDead() || line.Path == null)
            {
                ScHandheld.Count(line.Tag + " unsaid: body gone");
                ScLog.Verbose(line.Name + "'s " + What(line) + " " + line.Key + " was not said: their body is gone");
                return;                                                // nothing plays: the next line may go at once
            }
            // the m1v review's fix 2: they may have walked off (or you) while the line waited - a bark is 3D audio cut off at 40 m,
            // so beyond InPersonRange (3D) the line goes on the radio when it can be heard there, else it is dropped (its text
            // showed when it was queued); never played out of earshot, holding the queue
            EntityPlayerLocal p = player ?? ScCommunity.LocalPlayer;
            if (p != null && Vector3.Distance(npc.position, p.position) > InPersonRange)
            {
                string how;
                if (RadioHeard(line, npc, p, out how))
                {
                    line.InPerson = false;
                    ScHandheld.Count(line.Tag + " moved to the radio: out of earshot");
                    ScLog.Verbose(line.Name + "'s " + What(line) + " " + line.Key + " goes on the radio: they are " + Vector3.Distance(npc.position, p.position).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " m from you (" + how + ")");
                    PlayRadio(line, p);
                    return;
                }
                ScHandheld.Count(line.Tag + " unsaid: out of earshot");
                ScLog.Verbose(line.Name + "'s " + What(line) + " " + line.Key + " was not said: they are out of earshot and the radio is not heard (" + how + ")");
                return;
            }
            if (ScVoice.Holds(npc))
            {
                // the player is talking with this guard (or SC's talk line still plays on them): the text shows, the voice would
                // cut the conversation's line - every SC bark keeps this gate (ScBarks, ScGreetings)
                ScHandheld.Count(line.Tag + " unsaid: in a talk with them");
                ScLog.Verbose(line.Name + "'s " + What(line) + " " + line.Key + " was not said: you are talking with them");
                return;
            }
            busyUntil = Time.unscaledTime + 30f;                       // while the clip loads (set to its length when it plays)
            int my = token;
            bool begun = false;
            ScVoice.PlayBark(npc, line.Path, !ScVoice.VcBusy(npc), false,
                clip =>
                {
                    begun = true;
                    if (my != token) return;
                    ScHandheld.Count(line.Tag + " said: in person");
                    busyUntil = Time.unscaledTime + clip.length + 0.5f;
                },
                () =>
                {
                    // the clip did not load or the body went meanwhile: the queue goes on (a line that played set its own wait)
                    if (my == token && !begun) busyUntil = Time.unscaledTime + 0.3f;
                });
        }

        /// <summary>
        /// The m1v review's fix 2: can an in-person line that is now out of earshot go on the radio instead? A horde warning by its
        /// own rule (vo4: your handheld, and the guard's own handheld or the home station with power); anyone else's by
        /// ScRadioSpeaker.Hears (your handheld, or a powered station of yours within 30 m).
        /// </summary>
        private static bool RadioHeard(Line line, EntityAlive npc, EntityPlayerLocal p, out string how)
        {
            if (line.Tag == "horde")
            {
                // MP5: a guard's warning on the radio reaches another player's machine with round MP5's radio work (the server
                // sent this one in person; whether the guard can send is the server's to judge)
                if (line.Remote) { how = "a horde warning on the radio reaches a player's own machine from round MP5 on"; return false; }
                if (!ScHandheld.Carries(p)) { how = "you carry no handheld radio"; return false; }
                if (ScHandheld.NpcCarries(npc)) { how = "their handheld"; return true; }
                if (ScDefence.Radio()) { how = "the home station"; return true; }
                how = "no handheld radio on them and no station at home has power";
                return false;
            }
            return ScRadioSpeaker.Hears(p, out how);
        }

        private static void Caption(EntityPlayerLocal player, Line line)
        {
            if (player == null) return;
            // round MP4 (part: tells): this file's tells go by localization key (ScCommunity.TellKey / ScPlayers.TellCommunityKey), so each player reads them in their own language
            ScCommunity.TellKey(player, "scRadioRunTalk", "", line.Name, line.Words);
        }

        /// <summary>
        /// The review's L1: a line's coroutine that throws is stopped by Unity, and the queue would wait out its 30 s guard. Each
        /// step runs inside a try here (a yield cannot sit in one): an exception is logged once and frees the queue.
        /// </summary>
        private static IEnumerator Guarded(IEnumerator inner)
        {
            while (true)
            {
                object current;
                try
                {
                    if (!inner.MoveNext()) yield break;
                    current = inner.Current;
                }
                catch (Exception e)
                {
                    ScLog.Once("radio-talk-line", e);
                    busyUntil = Time.unscaledTime + CaptionSeconds;
                    yield break;
                }
                yield return current;
            }
        }

        /// <summary>The line on the radio's source: a squelch in, the voice, a squelch out (the plain voice without the effect).</summary>
        private static IEnumerator PlayLine(AudioClip voice, float volume, bool effect, int my)
        {
            AudioSource s = Source(effect);
            if (s == null) { busyUntil = Time.unscaledTime + CaptionSeconds; yield break; }
            s.volume = volume;
            if (effect)
            {
                s.PlayOneShot(SquelchIn(), 0.45f);
                yield return new WaitForSecondsRealtime(0.14f);
                if (my != token || s == null) yield break;
            }
            s.clip = voice;
            s.Play();
            ScClipCache.MarkPlaying(voice);
            // until the source is done with it (a paused game holds it), but never longer than its length and half a minute
            float guard = Time.unscaledTime + voice.length + 30f;
            yield return null;
            while (my == token && s != null && s.isPlaying && (object)s.clip == (object)voice && Time.unscaledTime < guard) yield return null;
            if (my != token || s == null) yield break;
            if (effect) s.PlayOneShot(SquelchOut(), 0.45f);
            busyUntil = Time.unscaledTime + (effect ? 0.6f : 0.3f);
        }

        /// <summary>
        /// The radio's AudioSource: 2D (heard wherever the player is), on its own GameObject with a high-pass at 450 Hz, a
        /// low-pass at 3200 Hz and a little distortion - a voice on a cheap radio. The effect off: the filters are bypassed.
        /// </summary>
        private static AudioSource Source(bool effect)
        {
            try
            {
                if (source == null)
                {
                    audioGo = new GameObject("ScRadioVoice");
                    source = audioGo.AddComponent<AudioSource>();
                    source.spatialBlend = 0f;
                    source.playOnAwake = false;
                    source.loop = false;
                    source.bypassReverbZones = true;
                    source.bypassListenerEffects = true;
                    source.priority = 32;
                    highPass = audioGo.AddComponent<AudioHighPassFilter>();
                    highPass.cutoffFrequency = 450f;
                    highPass.highpassResonanceQ = 1.2f;
                    lowPass = audioGo.AddComponent<AudioLowPassFilter>();
                    lowPass.cutoffFrequency = 3200f;
                    lowPass.lowpassResonanceQ = 1.4f;
                    distortion = audioGo.AddComponent<AudioDistortionFilter>();
                    distortion.distortionLevel = 0.3f;
                }
                source.bypassEffects = !effect;
                if (highPass != null) highPass.enabled = effect;
                if (lowPass != null) lowPass.enabled = effect;
                if (distortion != null) distortion.enabled = effect;
                return source;
            }
            catch (Exception e)
            {
                ScLog.Once("radio-talk-source", e);
                return null;
            }
        }

        private static AudioClip SquelchIn()
        {
            if (squelchIn == null) squelchIn = Noise("ScRadioSquelchIn", 0.18f, 0.7f, 11);
            return squelchIn;
        }

        private static AudioClip SquelchOut()
        {
            if (squelchOut == null) squelchOut = Noise("ScRadioSquelchOut", 0.12f, 1.6f, 23);
            return squelchOut;
        }

        /// <summary>A burst of noise with crackle, a fast attack and a decay (the radio's squelch; its filters band-limit it).</summary>
        private static AudioClip Noise(string name, float seconds, float decay, int seed)
        {
            const int rate = 22050;
            int n = Math.Max(64, (int)(seconds * rate));
            var data = new float[n];
            var r = new System.Random(seed);
            float attack = 0.004f * rate;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float env = Mathf.Min(1f, i / attack) * Mathf.Pow(1f - t, decay);
                float hiss = (float)(r.NextDouble() * 2.0 - 1.0) * 0.5f;
                float crackle = r.NextDouble() < 0.015 ? (float)(r.NextDouble() * 2.0 - 1.0) * 0.6f : 0f;
                data[i] = (hiss + crackle) * env;
            }
            AudioClip c = AudioClip.Create(name, n, 1, rate, false);
            c.SetData(data, 0);
            return c;
        }

        // ------------------------------------------------------------------ the console ----

        /// <summary>A runner's radio state for sc radio: carries one, the moments said this run.</summary>
        public static string Describe(Member m)
        {
            if (m == null) return "-";
            var sb = new StringBuilder();
            foreach (RadioTalk ev in Keys.Keys)
            {
                if (!Said(m, ev)) continue;
                sb.Append(sb.Length > 0 ? "," : "").Append(ev);
            }
            return (m.RunRadio ? "radio" : "no radio") + ", said: " + (sb.Length > 0 ? sb.ToString() : "nothing yet");
        }

        /// <summary>The queue's state for sc radio.</summary>
        public static string QueueState()
        {
            float left = busyUntil - Time.unscaledTime;
            var sb = new StringBuilder();
            foreach (Line l in queue) sb.Append(sb.Length > 0 ? ", " : "").Append(l.Priority).Append(' ').Append(l.Key);   // round m1v: what waits, oldest first
            float held = holdUntil - Time.unscaledTime;                // the m1v review's fix 3
            return queue.Count + " line(s) waiting" + (sb.Length > 0 ? " (" + sb + ")" : "")
                   + (left > 0f ? ", busy " + left.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " s" : "")
                   + (held > 0f ? ", held " + held.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " s for a line in person" : "");
        }

        /// <summary>sc radio talk: a moment's name (out, arrived, found, empty, trouble, home, near, recall, hurt).</summary>
        public static bool TryEvent(string s, out RadioTalk ev)
        {
            ev = RadioTalk.None;
            switch ((s ?? "").ToLowerInvariant())
            {
                case "out": ev = RadioTalk.Out; return true;
                case "arrived": ev = RadioTalk.Arrived; return true;
                case "found": ev = RadioTalk.Found; return true;
                case "empty": ev = RadioTalk.Empty; return true;
                case "trouble": ev = RadioTalk.Trouble; return true;
                case "home": ev = RadioTalk.Home; return true;
                case "near": ev = RadioTalk.Near; return true;
                case "recall": case "recallack": ev = RadioTalk.RecallAck; return true;
                case "hurt": case "hurthome": ev = RadioTalk.HurtHome; return true;          // round r2
            }
            return false;
        }
    }
}
