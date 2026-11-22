using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round m1v: a line's place in ScRadioTalk's shared queue (eight lines at most). The highest waiting plays first (the
    /// oldest of them first), and a full queue drops its lowest (the oldest of those), or the new line when it is lower than
    /// every waiting one. The user's order (2026-11-17): a siege call, then the base's horde warnings, then a runner's
    /// moments, then every other enclave line.
    /// </summary>
    public enum RadioPriority
    {
        Enclave = 0,
        Runner = 1,
        Horde = 2,
        Siege = 3
    }

    /// <summary>
    /// Round m1v (ROADMAP item 2, round V-a; the user's decision 2026-11-17): someone who speaks to the player over the radio -
    /// one of the community's members (a runner, a guard), an enclave's person (the leader, or the next living one), a drifter
    /// band's person, and later the Military's operator (the Kinds' Military: a record of theirs and no body is all it needs).
    ///
    /// A speaker is a record their voice is dealt from (Who: the look's sex and race, the name - ScVoice.RecordClip) and, when
    /// it is in the world, their body (a member's through ScCommunity.FindNpc; anyone else's by entity id and birth time, so a
    /// recycled id is never theirs). With the body loaded the voice is the body's own chain - the one their talk, greetings
    /// and taunts use - so they sound the same on the radio as in person; without it the chain is built from the record the
    /// same way. Enclave people's records carry Id 0, which is why ScVoice.MemberClip (by member id) could not find them.
    /// </summary>
    public sealed class RadioSpeaker
    {
        public enum Kinds { Member, Enclave, Band, Military }

        public Kinds Kind;
        /// <summary>The record the voice comes from (never null for a speaker made by Of).</summary>
        public Member Who;
        /// <summary>Kind Member: the member's id (their body is ScCommunity.FindNpc's).</summary>
        public int MemberId;
        /// <summary>Anyone else: their body's entity id (-1: none in the world) and birth time.</summary>
        public int EntityId = -1;
        public ulong EntityBorn;
        /// <summary>For the log and the console: "leader of The Holdouts", "p12 of Mara's band".</summary>
        public string Role = "";
        // ---- the m1v review's fix 7: what they are, so a line still waiting can tell whether they are still there ----
        /// <summary>Kind Enclave: their record and their group; Kind Band: theirs (null for the other kinds, or when not known).</summary>
        public EncPerson EncPersonRef;
        public Enclave EnclaveRef;
        public BandPerson BandPersonRef;
        public Band BandRef;

        public string Name { get { return Who != null && !string.IsNullOrEmpty(Who.Name) ? Who.Name : "?"; } }

        /// <summary>Their body in the world (alive or not), or null: a member's survivor body; anyone else's by id and birth time.</summary>
        public EntityAlive Body()
        {
            try
            {
                if (Kind == Kinds.Member)
                {
                    EntityAlive m = ScCommunity.FindNpc(MemberId);
                    return m != null && ScXnpc.IsSurvivorBody(m) ? m : null;
                }
                if (EntityId < 0) return null;
                World w = ScCommunity.World;
                EntityAlive npc = w != null ? ScNpc.As(ScNpc.Loaded(w, EntityId)) : null;
                return npc != null && npc.WorldTimeBorn == EntityBorn ? npc : null;
            }
            catch (Exception e)
            {
                ScLog.Once("radio-speaker-body", e);
                return null;
            }
        }

        /// <summary>
        /// The clip of one of these keys in their voice: the first voice of their chain with any of them, one of its keys at
        /// random (key: the one picked); null when none of their voices has any (or VoiceLines is off).
        /// </summary>
        public string Clip(IList<string> keys, System.Random rnd, out string key, out string voice)
        {
            return ScVoice.RecordClip(Who, Body(), keys, rnd, out key, out voice);
        }

        /// <summary>
        /// The m1v review's fix 7: are they still there to say a line that waited in the queue? A record that joined the
        /// community (E12, a band's join: the same Member is one of the members now) always is; else an enclave's person must
        /// still be one of its living people and their group one of the enclaves, a band's person alive in a band that is still
        /// on the road, a member still a member. why: what is gone. Never throws (true on an error).
        /// </summary>
        public bool StillThere(out string why)
        {
            why = null;
            try
            {
                if (!ScRegistry.Loaded) return true;
                if (Who != null && ScRegistry.Members.ContainsValue(Who)) return true;
                switch (Kind)
                {
                    case Kinds.Member:
                        if (ScRegistry.GetMember(MemberId) == null) { why = Name + " is no longer one of the community"; return false; }
                        return true;
                    case Kinds.Enclave:
                        if (EncPersonRef != null && (!ScRegistry.EncPeople.Contains(EncPersonRef) || !EncPersonRef.Alive)) { why = Name + " is dead or gone from their group"; return false; }
                        if (EnclaveRef != null && !ScEnclaves.All.Contains(EnclaveRef)) { why = "their group is gone"; return false; }
                        return true;
                    case Kinds.Band:
                        if (BandPersonRef != null && (!ScRegistry.BandPeople.Contains(BandPersonRef) || !BandPersonRef.Alive)) { why = Name + " is dead or gone from their band"; return false; }
                        if (BandRef != null && !ScRegistry.Bands.Contains(BandRef)) { why = "their band is gone"; return false; }
                        return true;
                }
            }
            catch (Exception e)
            {
                ScLog.Once("radio-speaker-still", e);
            }
            return true;
        }

        // ------------------------------------------------------------------ making one ----

        /// <summary>A community member (a runner on a run, a guard at home).</summary>
        public static RadioSpeaker Of(Member m)
        {
            try
            {
                if (m == null) return null;
                return new RadioSpeaker { Kind = Kinds.Member, Who = m, MemberId = m.Id, Role = "member " + m.Id };
            }
            catch (Exception ex)
            {
                ScLog.Once("radio-speaker-of-member", ex);     // the m1v review's fix 6: a maker never throws into the caller
                return null;
            }
        }

        /// <summary>An enclave's person, their body as it is now (taken now: a join or a move may let go of it later). Never throws.</summary>
        public static RadioSpeaker Of(EncPerson p, Enclave e)
        {
            try
            {
                if (p == null || p.Who == null) return null;
                if (e == null) e = ScEnclaves.ById(p.EnclaveId);
                return new RadioSpeaker
                {
                    Kind = Kinds.Enclave, Who = p.Who, EntityId = p.EntityId, EntityBorn = p.EntityBorn, EncPersonRef = p, EnclaveRef = e,
                    Role = (p.IsLeader ? "leader" : "p" + p.Id) + " of " + (e != null ? e.Name : "enclave " + p.EnclaveId)
                };
            }
            catch (Exception ex)
            {
                ScLog.Once("radio-speaker-of-person", ex);
                return null;
            }
        }

        /// <summary>A drifter band's person, their body as it is now (taken now: a join lets go of it). Never throws.</summary>
        public static RadioSpeaker Of(BandPerson p, Band b)
        {
            try
            {
                if (p == null || p.Who == null) return null;
                if (b == null) b = ScBands.ById(p.BandId);
                return new RadioSpeaker
                {
                    Kind = Kinds.Band, Who = p.Who, EntityId = p.EntityId, EntityBorn = p.EntityBorn, BandPersonRef = p, BandRef = b,
                    Role = (p.IsLeader ? "leader" : "p" + p.Id) + " of " + (b != null ? ScBands.DisplayName(b) : "band " + p.BandId)
                };
            }
            catch (Exception ex)
            {
                ScLog.Once("radio-speaker-of-band", ex);
                return null;
            }
        }

        /// <summary>Anyone with a record and maybe a body (the Military's operator, later): role is for the log. Never throws.</summary>
        public static RadioSpeaker OfRecord(Kinds kind, Member who, EntityAlive body, string role)
        {
            try
            {
                if (who == null) return null;
                return new RadioSpeaker
                {
                    Kind = kind, Who = who, EntityId = body != null ? body.entityId : -1, EntityBorn = body != null ? body.WorldTimeBorn : 0UL,
                    Role = role ?? ""
                };
            }
            catch (Exception ex)
            {
                ScLog.Once("radio-speaker-of-record", ex);
                return null;
            }
        }
    }

    /// <summary>
    /// Round m1v: the one path a speaker's line takes (Say), and the hearing rule (Hears).
    ///
    /// In person: their body stands within ScRadioTalk.InPersonRange of the player - the plain voice from the body, positional
    /// (ScRadioTalk's in-person line: ScVoice.PlayBark, after the lines ahead of it), or, for a line said as a talk with them
    /// closes, as the talk's own parting line (ScVoice.SayAfterTalk: the close does not cut it).
    /// On the radio: anywhere else, heard aloud only when the player carries a handheld radio or stands within
    /// StationHearRange of a powered SC radio station (the user, 2026-11-17: at home, an outpost's relay, or with no home any
    /// of them - as ScRadio.Online counts them); then into ScRadioTalk's queue at its
    /// priority, on the radio's own source with its filters and squelch (RunnerRadioEffect, RunnerRadioVolume). Unheard: the
    /// text the caller shows is all the player gets, as before.
    /// A key with no recording in the speaker's voices is not said: the text alone (the siege call and the request offer
    /// until round V-b records them). The caller shows its text first; no line here has a caption of its own.
    /// Host only, main thread.
    /// </summary>
    public static class ScRadioSpeaker
    {
        /// <summary>The user's rule: a powered SC radio station within this many meters of the player lets them hear the radio.</summary>
        public const float StationHearRange = 30f;

        private static readonly System.Random rnd = new System.Random();

        /// <summary>
        /// Can the player hear a transmission aloud now? Their handheld radio anywhere, or a powered SC radio station within
        /// StationHearRange (3D, to the block's center): one at home, an outpost's relay, or - with no home - any SC station
        /// (the m1v review's fix 5: as ScRadio.Online counts them, relays added). how: what lets them hear it, or why not.
        /// </summary>
        public static bool Hears(EntityPlayer player, out string how)
        {
            how = null;
            if (player == null || player.IsDead()) { how = "no living player"; return false; }
            if (ScHandheld.Carries(player)) { how = "your handheld radio"; return true; }
            Vector3i at;
            float d;
            if (NearStation(player.position, out at, out d))
            {
                how = "the radio station at " + at + ", " + d.ToString("0", CultureInfo.InvariantCulture) + " m from you";
                return true;
            }
            how = "you carry no handheld radio and no powered radio station of yours is within " + StationHearRange.ToString("0", CultureInfo.InvariantCulture) + " m";
            return false;
        }

        /// <summary>
        /// The nearest powered SC radio station within StationHearRange of pos: the remembered ones at home and the outposts'
        /// relays, and those seen this session - with a home, one inside the home zone or a remembered relay; with no home, any
        /// (ScRadio.Online's rule).
        /// </summary>
        internal static bool NearStation(Vector3 pos, out Vector3i at, out float dist)
        {
            at = Vector3i.zero;
            dist = float.MaxValue;
            try
            {
                PowerManager pm = PowerManager.HasInstance ? PowerManager.Instance : null;
                World w = ScCommunity.World;
                bool home = ScRegistry.Loaded && ScZone.HasHome;
                List<Vector3i> relays = ScRegistry.Loaded ? ScRegistry.Community.Relays : null;
                if (ScRegistry.Loaded)
                {
                    Consider(ScRegistry.Community.Radios, pos, pm, w, home, relays, ref at, ref dist);
                    Consider(relays, pos, pm, w, home, relays, ref at, ref dist);
                }
                Consider(ScRadio.Radios, pos, pm, w, home, relays, ref at, ref dist);
            }
            catch (Exception e)
            {
                ScLog.Once("radio-speaker-station", e);
            }
            return dist <= StationHearRange;
        }

        private static void Consider(IEnumerable<Vector3i> radios, Vector3 pos, PowerManager pm, World w, bool home, List<Vector3i> relays, ref Vector3i at, ref float dist)
        {
            if (radios == null) return;
            foreach (Vector3i p in radios)
            {
                if (home && !ScZone.Contains(p) && (relays == null || !relays.Contains(p))) continue;   // fix 5: home or a relay
                float d = Vector3.Distance(pos, new Vector3(p.x + 0.5f, p.y + 0.5f, p.z + 0.5f));
                if (d > StationHearRange || d >= dist) continue;
                PowerItem item = pm != null ? pm.GetPowerItemByWorldPos(p) : null;
                bool powered = (item != null && item.IsPowered) || ScRadio.IsPoweredAt(w, p);
                if (!powered) continue;
                at = p;
                dist = d;
            }
        }

        /// <summary>
        /// The speaker says one of these keys (picked at random among those their voice has): in person when their body is near,
        /// else on the radio when the player hears it. afterTalk: said as a talk closes (a join's answer) - in person through
        /// ScVoice.SayAfterTalk. force (the console): the hearing rule ignored. tag: the counters' word ("enc"). Returns what
        /// happened, for the log and the console; the caller has shown its text.
        /// </summary>
        public static string Say(RadioSpeaker s, IList<string> keys, RadioPriority prio, EntityPlayer player, bool afterTalk, bool force, string tag)
        {
            try
            {
                if (s == null || s.Who == null) { ScHandheld.Count(tag + " unsaid: nobody to speak"); return "nobody of theirs to speak: the text alone"; }
                if (keys == null || keys.Count == 0) return "no line";
                string key, voice;
                string path = s.Clip(keys, rnd, out key, out voice);
                if (path == null)
                {
                    ScHandheld.Count(tag + " unsaid: no recording");
                    string what = keys.Count == 1 ? keys[0] : keys[0] + " .. " + keys[keys.Count - 1];
                    return ScConfig.VoiceLines ? "no recording of " + what + " in " + s.Name + "'s voices yet: the text alone" : "VoiceLines is off: the text alone";
                }
                // round MP0: heard and played for this machine's own player only (none on a dedicated server: unheard, the text
                // alone - ScRadioTalk.PlayNext plays nothing there either). MP4: voices to clients
                EntityPlayerLocal local = player as EntityPlayerLocal ?? ScCommunity.LocalPlayer;
                EntityAlive body = s.Body();
                // the m1v review's fix 1: 3D - a bark is 3D audio cut off at 40 m (ScVoice's OwnMaxDistance), so a body far above or
                // below (a tall building, a mine) is not "in person" however close it stands on the map
                if (body != null && !body.IsDead() && local != null && Vector3.Distance(body.position, local.position) <= ScRadioTalk.InPersonRange)
                {
                    if (ScDowned.IsDown(body)) { ScHandheld.Count(tag + " unsaid: they are down"); return s.Name + " is down: the text alone"; }
                    if (afterTalk)
                    {
                        // fix 3: counted and logged only when a clip really starts; the radio queue waits while it plays (HoldFor), so
                        // no radio line talks over it
                        string said = tag, k = key, v = voice;
                        // the m1v re-check's N3: the hold is set first (about 5 s for a slow first load), set to the clip's length when it
                        // starts, and let go when it never does (counted unsaid then) - so a failed or synchronous callback never leaves a
                        // stale hold behind
                        ScRadioTalk.HoldFor(5f);
                        bool playing = ScVoice.SayAfterTalk(body, key,
                            clip =>
                            {
                                ScRadioTalk.HoldFor(clip.length + 0.3f, true);
                                ScHandheld.Count(said + " said: in person, as the talk closes");
                                Note(s, k, v, "in person, as the talk closes");
                            },
                            () =>
                            {
                                ScRadioTalk.ReleaseHold();
                                ScHandheld.Count(said + " unsaid: in person, as the talk closes (it did not play)");
                                ScLog.Verbose(s.Name + "'s " + k + " as the talk closed did not play (the clip did not load, they went, or another line took over)");
                            });
                        if (!playing)
                        {
                            ScRadioTalk.ReleaseHold();
                            ScHandheld.Count(tag + " unsaid: in person, as the talk closes");
                            return "not said: " + key + " was said by them less than " + ScVoice.RepeatSeconds.ToString("0", CultureInfo.InvariantCulture)
                                   + " s ago, or their talk voice has no recording of it - the text alone";
                        }
                        return "saying " + key + " in " + voice + ", in person as the talk closes";
                    }
                    if (!ScRadioTalk.QueueSpoken(s, key, path, voice, body, prio, tag, local)) return "dropped: the radio queue is full of more urgent lines";
                    Note(s, key, voice, "in person");
                    return "queued " + key + " in " + voice + ", in person (" + prio + ")";
                }
                string how;
                if (!Hears(local, out how))
                {
                    if (!force)
                    {
                        ScHandheld.Count(tag + " unheard");
                        ScLog.Verbose(s.Name + " (" + s.Role + ") on the radio, " + key + ", went unheard: " + how);
                        return "unheard: " + how + " - the text alone";
                    }
                    how = "forced: " + how;
                }
                if (!ScRadioTalk.QueueSpoken(s, key, path, voice, null, prio, tag, local)) return "dropped: the radio queue is full of more urgent lines";
                Note(s, key, voice, "on the radio (" + how + ")");
                return "queued " + key + " in " + voice + ", on the radio (" + how + "; " + prio + ")";
            }
            catch (Exception e)
            {
                ScLog.Once("radio-speaker-say", e);
                return "error";
            }
        }

        private static void Note(RadioSpeaker s, string key, string voice, string how)
        {
            ScLog.Out(s.Name + " (" + s.Role + ") says " + key + " in " + voice + ", " + how);
        }
    }
}
