#if SC_V34
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round U3 (2026-11-16, the user's decision D11 "SC layer, SC voices"; research npc306\part_social.md Q5 option A): the
    /// community's social life. 0-XNPCCore 3.4.06 gave its POI residents small talk, walks over to a neighbor and an evening
    /// campfire, but all of it hangs off its resident routine, which SC's people only enter at night to sleep (round n06's
    /// lease, phase 3) - so for members and enclave people every piece was inert. This is SC's own layer, at home and in an
    /// enclave's building:
    ///
    ///  - SMALL TALK. Two free people of the same place (the home zone, or one enclave's building) within SmallTalkRange (4 m),
    ///    standing still, exchange an opener and an answer: they turn to each other (ScChat's hold, UaiSocial) and gesture
    ///    (0-XNPCCore's own line reaction for smalltalk.open / smalltalk.reply, the Talk emote it plays for its residents), and
    ///    each line plays in the speaker's recorded voice where it has the clip, else shows as a caption in Voice Control's
    ///    subtitle bar (ScSocialVoice). The lines are SC's curated pairs (ScSocialLines, Tools/smalltalk_lines.py), picked by
    ///    the opener's traits and mood; with SmallTalkTtsFallback an opener with no recording uses Voice Control's own 220
    ///    pairs through 0-XNPCCore's small-talk hook (XNPCCore.SmallTalk / SayLine), as its residents do.
    ///  - Voice Control's player-distance rule: only while the nearest player is 10-32 m from one of the two
    ///    (SmallTalkHearMin / Max); a player coming within 10 m ends the exchange and its answer is dropped.
    ///  - One exchange at a time anywhere (the subtitle bar shows one line), SmallTalkGapSeconds apart (a quarter either way),
    ///    the same two people once per SmallTalkPairMinutes; at a campfire CampfireTalkGapSeconds instead of the gap.
    ///  - WALKING OVER. When no two free people stand close, every MeetSeconds a roll of SmallTalkMeetChance picks a resident
    ///    pausing in her day roam (ScRoamState) and a free neighbor within SmallTalkMeetRange: her next roam walk goes to
    ///    1.5 m from the neighbor (UAITaskWanderHomeSC.ZoneTarget reads the plan, MeetSpot), the neighbor waits for her,
    ///    turned to her (the same hold), and they talk on arrival.
    ///  - THE CAMPFIRE. In the evening window - 0-XNPCCore's own phase 2, dusk - HomeBeforeDuskHours to dusk +
    ///    SleepAfterDuskHours (read from it, B7 below), never on a blood moon or during a base attack - and during a round of
    ///    drinks at home, each free person walks once to a campfire in their place (CampfireBlocks inside the home zone, or
    ///    the enclave's building), stands on a ring 2 m out facing it for CampfireMinSeconds..MaxSeconds, then goes back to
    ///    her routine (ScGather, UaiSocial). The fire shows lit while anyone sits at it: 0-XNPCCore's four SdcsWorkFlame cvars
    ///    on each sitter, which its own Follow turns into the station's flames on every machine (no fuel, no block change);
    ///    SC drops them when she leaves and sweeps any left on an SC body (the cvars are saved with the entity).
    ///  - B7 (research): the night lease used to mirror 0-XNPCCore's SleepAfterDuskHours by hand. ScNpcClock reads it, and
    ///    the lease (ScSleep.SleepWhy) never opens before 0-XNPCCore's own phase 3 - in its phase 2 it would now walk every
    ///    leased member to the base campfire (GatherChance 1.0).
    ///
    /// Never for anyone asleep or in bed for the night (ScSleep.IsAsleep, the SdcsResident cvar), on a work shift or in a talk
    /// (ScWork, the Owner cvar, ScVoice.Holds, Voice Control's chat), on watch, following (status and the Leader cvar), on a
    /// run or away (only Resident members are seen), missing, downed (0-XNPCCore's SdcsDowned.IsDowned), healing, retreating
    /// or sheltering (ScRetreat), in any posture but none or HOLD (ScPosture), with a live foe within 30 m
    /// (ScCommunity.LiveFoe) or an enemy in sight within 30 m (SCore's IsEnemyNearby - the gate ScChat and ScGather carry as
    /// EnemyNotNear), during a base attack, or while a talk window is open. See Why.
    ///
    /// Purity: the state below is written only here, by Tick (the 1 Hz host ticker) and the console; the UAI considerations
    /// (ScSmallTalkHeld, ScGathering) and the meet hook only read it. Host / single player (SC is single-player), main thread.
    /// </summary>
    public static class ScSocial
    {
        private const float FightRange = 30f;
        /// <summary>How long a planned walk-over waits for the walker's next roam walk to start, and for it to arrive.</summary>
        private const float MeetPlanSeconds = 30f, MeetWalkSeconds = 40f;
        /// <summary>Between two walk-over rolls.</summary>
        private const float MeetSeconds = 20f;
        /// <summary>The walker stops this far from the neighbor (0-XNPCCore's ChatMeetStandMetres).</summary>
        private const float MeetStandMeters = 1.5f;
        /// <summary>The answer comes this long after the opener ends (0-XNPCCore's ChatReplyGapSeconds), and they stay turned to each other this long after it.</summary>
        private const float ReplyGapSeconds = 0.6f, HoldAfterReplySeconds = 3f;
        /// <summary>The answer waits at most this long for the opener's clip to finish.</summary>
        private const float ReplyWaitMax = 6f;
        /// <summary>The ring around the fire (0-XNPCCore's GatherRingMetres), and a second one farther out.</summary>
        private const float RingMeters = 2f, RingOuter = 3.2f;
        /// <summary>A sitter has this long to get to her place at the fire.</summary>
        private const float GatherWalkSeconds = 60f;
        /// <summary>Each person sets off for the fire within this many seconds of the window opening (by her name, so the same order each evening).</summary>
        private const float GatherStagger = 45f;
        /// <summary>A found fire (or none) is kept this long before the place is searched again.</summary>
        private const float FireCacheSeconds = 30f;
        /// <summary>sc chat gather: a gathering at home for this long, whatever the hour.</summary>
        private const float ConsoleGatherSeconds = 180f;

        // ------------------------------------------------------------------ who, this pass ----

        /// <summary>One body the pass looked at: a resident at home or one of an enclave's people at home.</summary>
        internal sealed class Body
        {
            public EntityAlive Npc;
            /// <summary>The person, not the entity: "m:&lt;name&gt;" for a member, "enc:&lt;id&gt;" for an enclave person.</summary>
            public string Key;
            public string Name;
            /// <summary>HomePlace, or the Enclave: only people of one place talk and gather together.</summary>
            public object Place;
            /// <summary>The member, or the enclave person's record (traits, mood).</summary>
            public Member Rec;
            public EncPerson Person;
            /// <summary>Null when free; else why not.</summary>
            public string Why;
            public bool Moving;
            /// <summary>The nearest living player, meters.</summary>
            public float PlayerDist;
        }

        internal static readonly object HomePlace = new object();

        private sealed class Exchange
        {
            public int A, B;
            public string AKey, BKey, AName, BName;
            /// <summary>The pair's number ("07"); null for Voice Control's own pair (the TTS fallback).</summary>
            public string Stem;
            public bool Vc, AtFire, Meet, Forced;
            public object Place;
            public float StartedAt, ReplyAt, EndAt;
            public bool ReplySaid;
            public string Open, Reply, OpenHow, ReplyHow;
        }

        private sealed class Meet
        {
            public int Walker, Partner;
            public string WalkerKey, PartnerKey, WalkerName, PartnerName;
            public object Place;
            public float PlannedAt, Until;
            public bool Walking, Forced;
            /// <summary>How far apart they were as she set off, and when the ticker checks she is coming closer.</summary>
            public float StartDist, CheckAt;
        }

        private sealed class Gather
        {
            public int Id;
            public string Key, Name, Window;
            public object Place;
            public Vector3i Fire;
            public Vector3 Spot;
            public float SetOffAt, ArriveBy, ArrivedAt = -1f, LeaveAt;
            public bool Lit;
        }

        private sealed class Fire
        {
            public bool Found;
            public Vector3i Pos;
            public float Until;
        }

        private static Exchange live;
        private static Meet meet;
        private static readonly Dictionary<int, Gather> gathers = new Dictionary<int, Gather>();
        /// <summary>
        /// The windows each person has gathered in (person key -> window ids, the last few): once per evening, once per round of
        /// drinks. A set, not the last one (the review's finding 4): the id flips evening -> drinks -> evening when drinks are
        /// poured in the evening, and the last id alone sent everyone back to the fire a third time.
        /// </summary>
        private static readonly Dictionary<string, List<string>> gatheredIn = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        private const int WindowsKept = 6;

        private static bool GatheredIn(string key, string window)
        {
            List<string> l;
            return gatheredIn.TryGetValue(key, out l) && l.Contains(window);
        }

        private static void MarkGathered(string key, string window)
        {
            List<string> l;
            if (!gatheredIn.TryGetValue(key, out l)) gatheredIn[key] = l = new List<string>();
            if (l.Contains(window)) return;
            l.Add(window);
            if (l.Count > WindowsKept) l.RemoveAt(0);
        }
        /// <summary>When each place's current window was first seen open (real time), and its id.</summary>
        private static readonly Dictionary<object, KeyValuePair<string, float>> windowSeen = new Dictionary<object, KeyValuePair<string, float>>();
        private static readonly Dictionary<object, Fire> fires = new Dictionary<object, Fire>();
        /// <summary>When each pair last talked ("a|b", the person keys in order), real time.</summary>
        private static readonly Dictionary<string, float> pairLast = new Dictionary<string, float>(StringComparer.Ordinal);
        private static float nextExchange = -1f, nextFireTalk, nextMeetRoll, nextFlameSweep;
        private static float consoleGatherUntil = -1f;
        private static int consoleGatherId;

        private static readonly List<Body> bodies = new List<Body>();
        private static readonly Dictionary<int, Body> byId = new Dictionary<int, Body>();
        private static readonly List<int> scratchIds = new List<int>();
        private static readonly System.Random rnd = new System.Random();

        private static int nExchanges, nReplies, nDropped, nVc, nMeetsPlanned, nMeetsMet, nMeetsLapsed, nGathered, nGatherLapsed, nFlamesCleared;
        private static string lastEnd = "", lastMeetEnd = "";

        public static void Reset()
        {
            live = null;
            meet = null;
            gathers.Clear();
            gatheredIn.Clear();
            windowSeen.Clear();
            fires.Clear();
            pairLast.Clear();
            nextExchange = -1f;
            nextFireTalk = nextMeetRoll = nextFlameSweep = 0f;
            consoleGatherUntil = -1f;
            bodies.Clear();
            byId.Clear();
            nExchanges = nReplies = nDropped = nVc = nMeetsPlanned = nMeetsMet = nMeetsLapsed = nGathered = nGatherLapsed = nFlamesCleared = 0;
            lastEnd = lastMeetEnd = "";
            ScSocialVoice.Reset();
        }

        /// <summary>From ScConfig's Reset and Parse: the campfire block list is parsed again on its next use.</summary>
        internal static void ResetConfig()
        {
            campfireTypes = null;
        }

        // ------------------------------------------------------------------ the UAI's reads ----

        /// <summary>
        /// The Fable review's finding 1: every hold the UAI reads also runs out by itself a little after it was due to end, so a
        /// tick that stopped stepping (an exception every second, say) can never keep two people facing each other or a sitter
        /// at the fire all day. The live exchange: EndAt + 5 s; a neighbor waiting for a walker: the walk's limit + 5 s; a place
        /// at the fire: the walk's limit, or the time to leave, + 10 s.
        /// </summary>
        private static bool Fresh(Exchange x) { return x != null && Time.time <= x.EndAt + 5f; }

        private static bool Fresh(Meet m) { return m != null && m.Walking && Time.time <= m.Until + 5f; }

        private static bool Fresh(Gather g) { return g != null && Time.time <= (g.ArrivedAt >= 0f ? g.LeaveAt : g.ArriveBy) + 10f; }

        /// <summary>ScSmallTalkHeld: in an exchange, or waiting for a neighbor who walks over to her. A pure read.</summary>
        public static bool Holds(EntityAlive self)
        {
            if ((object)self == null) return false;
            int id = self.entityId;
            Exchange x = live;
            if (Fresh(x) && (x.A == id || x.B == id)) return true;
            Meet m = meet;
            return Fresh(m) && m.Partner == id;
        }

        /// <summary>In an exchange right now (ScGreetings keeps its chatter off them). A pure read.</summary>
        public static bool InExchange(EntityAlive npc)
        {
            Exchange x = live;
            return (object)npc != null && Fresh(x) && (x.A == npc.entityId || x.B == npc.entityId);
        }

        /// <summary>ScGathering: she has a place at a fire (walking to it or sitting there). A pure read.</summary>
        public static bool Gathering(EntityAlive self)
        {
            Gather g;
            return (object)self != null && gathers.Count > 0 && gathers.TryGetValue(self.entityId, out g) && Fresh(g);
        }

        /// <summary>ChatHoldSC: who she turns to - her partner in the exchange, or the neighbor walking over. A pure read.</summary>
        public static EntityAlive FaceTarget(EntityAlive self)
        {
            if ((object)self == null) return null;
            int id = self.entityId, other = -1;
            Exchange x = live;
            Meet m = meet;
            if (Fresh(x) && x.A == id) other = x.B;
            else if (Fresh(x) && x.B == id) other = x.A;
            else if (Fresh(m) && m.Partner == id) other = m.Walker;
            if (other < 0) return null;
            World w = ScCommunity.World;
            EntityAlive e = w != null ? w.GetEntity(other) as EntityAlive : null;
            return e != null && !e.IsDead() ? e : null;
        }

        /// <summary>GatherSC: her place at the fire and the fire. A pure read.</summary>
        public static bool GatherSpotOf(EntityAlive self, out Vector3 spot, out Vector3i fire, out bool arrived)
        {
            spot = Vector3.zero;
            fire = Vector3i.zero;
            arrived = false;
            Gather g;
            if ((object)self == null || !gathers.TryGetValue(self.entityId, out g) || !Fresh(g)) return false;
            spot = g.Spot;
            fire = g.Fire;
            arrived = g.ArrivedAt >= 0f;
            return true;
        }

        /// <summary>Turns one body to another: her look at its head, and her body around to it (0-XNPCCore's SdcsResidentChat.Face; no pitch on an SDCS body).</summary>
        public static void Face(EntityAlive self, EntityAlive other)
        {
            if (self == null || other == null) return;
            self.SetLookPosition(other.getHeadPosition());
            Vector3 p = other.position;
            self.RotateTo(p.x, p.y, p.z, 45f, 0f);
        }

        /// <summary>
        /// UAITaskWanderHomeSC.ZoneTarget, at the start of a roam walk: when the ticker planned a walk-over for her, the spot
        /// 1.5 m from the neighbor on the side she comes from (a real floor inside her ground). A pure read: the plan is the
        /// ticker's, and it sees her set off by herself.
        /// </summary>
        public static bool MeetSpot(EntityAlive self, World w, UAI.ScArea area, out Vector3 spot)
        {
            spot = Vector3.zero;
            if (meet == null || meet.Walking || (object)self == null || meet.Walker != self.entityId || w == null || area == null) return false;
            EntityAlive p = w.GetEntity(meet.Partner) as EntityAlive;
            if (p == null || p.IsDead()) return false;
            Vector3 from = self.position - p.position;
            from.y = 0f;
            if (from.sqrMagnitude < 0.01f) return false;
            Vector3 at = p.position + from.normalized * MeetStandMeters;
            Vector3i feet = World.worldToBlockPos(new Vector3(at.x, p.position.y + 0.05f, at.z));
            Vector3i[] tries =
            {
                feet, new Vector3i(feet.x + 1, feet.y, feet.z), new Vector3i(feet.x - 1, feet.y, feet.z),
                new Vector3i(feet.x, feet.y, feet.z + 1), new Vector3i(feet.x, feet.y, feet.z - 1)
            };
            for (int i = 0; i < tries.Length; i++)
            {
                Vector3i c = tries[i];
                if (!area.Contains(c)) continue;
                string why;
                if (!ScCommunity.GoodPost(w, c, false, out why)) continue;
                spot = i == 0 ? new Vector3(at.x, c.y, at.z) : new Vector3(c.x + 0.5f, c.y, c.z + 0.5f);
                return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ the tick ----

        /// <summary>1 Hz from ScTicker (host): the live exchange, the walk-over, the gatherings, new ones, the flame sweep.</summary>
        public static void Tick(World w, EntityPlayerLocal player)
        {
            if (w == null || !ScCommunity.IsHost || !ScRegistry.Loaded) return;
            try
            {
                float now = Time.time;
                windowNow.Clear();
                bool on = ScConfig.ResidentSmallTalk || ScConfig.CampfireGathering;
                if (on || live != null || meet != null || gathers.Count > 0) Collect(w, false);
                else { bodies.Clear(); byId.Clear(); }
                StepExchange(w, now);
                StepMeet(w, now);
                StepGathers(w, now);
                if (ScConfig.CampfireGathering) StartGathers(w, now);
                if (ScConfig.ResidentSmallTalk) StartTalk(w, now);
                if (now >= nextFlameSweep)
                {
                    nextFlameSweep = now + 5f;
                    SweepFlames(w);
                }
            }
            catch (Exception e)
            {
                ScLog.Once("social-tick", e);
                LetEveryoneGo();   // the review's finding 1: a tick that throws must not hold anyone (the reads expire as well)
            }
        }

        /// <summary>After a tick threw: no exchange, no walk-over, nobody at a fire (their flames put out), so nobody is held.</summary>
        private static void LetEveryoneGo()
        {
            live = null;
            meet = null;
            if (gathers.Count == 0) return;
            try
            {
                World w = ScCommunity.World;
                foreach (Gather g in gathers.Values)
                {
                    EntityAlive npc = w != null && g.Lit ? w.GetEntity(g.Id) as EntityAlive : null;
                    if (npc != null) Unlight(npc);
                }
            }
            catch (Exception e)
            {
                ScLog.Once("social-let-go", e);
            }
            gathers.Clear();
        }

        /// <summary>The bodies of this pass: residents at home (and an enclave's people at home), with their verdicts.</summary>
        private static void Collect(World w, bool all)
        {
            bodies.Clear();
            byId.Clear();
            windowNow.Clear();
            foreach (Member m in ScRegistry.Members.Values)
            {
                if (m.Status != MemberStatus.Resident && !all) continue;
                EntityAlive npc = ScCommunity.FindNpc(m.Id);
                if (npc == null || npc.IsDead()) continue;
                Body b = new Body { Npc = npc, Key = "m:" + (string.IsNullOrEmpty(m.Name) ? m.Id.ToString(CultureInfo.InvariantCulture) : m.Name),
                                    Name = m.Name, Place = HomePlace, Rec = m };
                Fill(w, b);
                b.Why = Needed(w, b, all) ? MemberWhy(m, npc) : FarWhy(b);
            }
            if (!ScConfig.SmallTalkEnclaves || !ScConfig.Enclaves) return;
            foreach (EncPerson p in ScRegistry.EncPeople)
            {
                if (p == null || p.State != EncPerson.StateHome) continue;
                Enclave en = ScEnclavePeople.EnclaveOf(p);
                if (en == null) continue;
                EntityAlive npc = ScEnclavePeople.OwnBody(p);
                if (npc == null || npc.IsDead()) continue;
                Body b = new Body { Npc = npc, Key = "enc:" + p.Id.ToString(CultureInfo.InvariantCulture), Name = p.Name, Place = en, Rec = p.Who, Person = p };
                Fill(w, b);
                b.Why = en.IsHostile ? "a hostile enclave"
                      : !p.HasHome ? "no home spot"                                  // the review's finding 5: as ScEncHome reads it
                      : p.Leaving ? "walking home"
                      : ScEnclaveDeals.IsEnlistedBody(npc) ? "enlisted with you"
                      : !en.Contains(Mathf.FloorToInt(npc.position.x), Mathf.FloorToInt(npc.position.z)) ? "outside their building"
                      : !Needed(w, b, all) ? FarWhy(b)
                      : BodyWhy(npc);
            }
        }

        /// <summary>
        /// Is the full test worth running for her this pass (it asks SCore for enemies in sight)? Not while she can neither talk
        /// (you are out of earshot of anyone she could walk to) nor gather (no window open), unless she is in something now.
        /// </summary>
        private static bool Needed(World w, Body b, bool all)
        {
            if (all) return true;
            int id = b.Npc.entityId;
            if ((live != null && (live.A == id || live.B == id)) || (meet != null && (meet.Walker == id || meet.Partner == id)) || gathers.ContainsKey(id)) return true;
            if (ScConfig.ResidentSmallTalk && b.PlayerDist <= ScConfig.SmallTalkHearMax + ScConfig.SmallTalkMeetRange) return true;
            return ScConfig.CampfireGathering && WindowFor(w, b.Place) != null;
        }

        private static string FarWhy(Body b)
        {
            return "out of earshot and no gathering (you are " + (b.PlayerDist < 100000f ? b.PlayerDist.ToString("0", CultureInfo.InvariantCulture) + " m" : "not") + " away)";
        }

        private static void Fill(World w, Body b)
        {
            b.Moving = b.Npc.moveHelper != null && b.Npc.moveHelper.IsActive;
            b.PlayerDist = NearestPlayer(w, b.Npc.position);
            bodies.Add(b);
            byId[b.Npc.entityId] = b;
        }

        /// <summary>Why this member may not talk or gather now (null: free).</summary>
        internal static string MemberWhy(Member m, EntityAlive npc)
        {
            if (m.Status != MemberStatus.Resident) return "not at home (" + m.Status + ")";
            if (ScMissing.IsMissing(m)) return "missing";
            if (ScWatch.OnWatch(m)) return "on watch";
            if (ScWork.OnShift(m.Id)) return ScWork.InTalk(m.Id) ? "in a talk with you" : "on a work shift";
            if (!ScZone.Contains(npc.GetBlockPosition())) return "outside the home zone";
            if (ScBaseAttack.Open) return "a base attack";
            return BodyWhy(npc);
        }

        /// <summary>The body's own tests, members and enclave people alike (null: free). Reads only.</summary>
        internal static string BodyWhy(EntityAlive npc)
        {
            if (npc == null || npc.IsDead()) return "gone";
            if (npc.isEntityRemote) return "not a local body";
            if (!ScXnpc.IsSurvivorBody(npc)) return "not a survivor body";
            if (npc.Buffs == null) return "no buffs";
            if (Downed(npc)) return "downed";
            if (ScSleep.IsAsleep(npc)) return "asleep";
            if (npc.Buffs.GetCustomVar("SdcsResident") >= 0.5f) return "in bed for the night (the night lease)";
            if (npc.Buffs.GetCustomVar("Leader") > 0f) return "following";
            if (npc.Buffs.GetCustomVar("Owner") > 0f) return "busy with you (a talk or a work shift)";
            if (ScVoice.Holds(npc)) return "in a talk with you";
            if (VcChatting(npc)) return "chatting with you through Voice Control";
            if (npc.Buffs.HasBuff("SdcsIsHealing")) return "healing";
            if (ScRetreat.Owns(npc)) return "retreating or sheltering";
            ScPosture.View v;
            if (ScPosture.Of(npc, out v) && v.P != Posture.None && v.P != Posture.Hold) return "posture " + ScPosture.Name(v.P, v.Kiting);
            if (ScCommunity.LiveFoe(npc, FightRange) != null) return "in a fight";
            if (EnemyInSight(npc)) return "an enemy in sight";
            return null;
        }

        private static float NearestPlayer(World w, Vector3 at)
        {
            float best = float.MaxValue;
            List<EntityPlayer> ps = w.Players != null ? w.Players.list : null;
            if (ps == null) return best;
            for (int i = 0; i < ps.Count; i++)
            {
                EntityPlayer p = ps[i];
                if (p == null || p.IsDead()) continue;
                float d = Vector3.Distance(p.position, at);
                if (d < best) best = d;
            }
            return best;
        }

        /// <summary>Voice Control's rule on the nearer of the two: farther than SmallTalkHearMin, within SmallTalkHearMax.</summary>
        private static bool Hears(float nearest)
        {
            return nearest > ScConfig.SmallTalkHearMin && nearest <= ScConfig.SmallTalkHearMax;
        }

        private static string PairKey(string a, string b)
        {
            return string.CompareOrdinal(a, b) <= 0 ? a + "|" + b : b + "|" + a;
        }

        private static bool PairCooling(string a, string b, float now)
        {
            float t;
            return pairLast.TryGetValue(PairKey(a, b), out t) && now - t < ScConfig.SmallTalkPairMinutes * 60f;
        }

        /// <summary>Forgets the pairs whose cooldown is over (the table only grows with new pairs).</summary>
        private static void PrunePairs(float now)
        {
            float span = ScConfig.SmallTalkPairMinutes * 60f;
            var stale = new List<string>();
            foreach (KeyValuePair<string, float> kv in pairLast) if (now - kv.Value >= span) stale.Add(kv.Key);
            foreach (string k in stale) pairLast.Remove(k);
        }

        private static float Jitter(float seconds)
        {
            return seconds * (0.75f + (float)rnd.NextDouble() * 0.5f);
        }

        // ------------------------------------------------------------------ small talk ----

        private struct Candidate { public Body A, B; public bool AtFire; }
        private static readonly List<Candidate> candidates = new List<Candidate>();

        private static void StartTalk(World w, float now)
        {
            if (live != null || meet != null || ScVoice.TalkOpen) return;
            if (nextExchange < 0f) { nextExchange = now + Jitter(ScConfig.SmallTalkGapSeconds) * 0.5f; return; }   // not right at load
            bool gapOpen = now >= nextExchange, fireOpen = now >= nextFireTalk;
            if (!gapOpen && !fireOpen) return;
            candidates.Clear();
            float r2 = ScConfig.SmallTalkRange * ScConfig.SmallTalkRange;
            for (int i = 0; i < bodies.Count; i++)
            {
                Body a = bodies[i];
                if (a.Why != null || a.Moving) continue;
                for (int j = i + 1; j < bodies.Count; j++)
                {
                    Body b = bodies[j];
                    if (b.Why != null || b.Moving || b.Place != a.Place) continue;
                    if ((a.Npc.position - b.Npc.position).sqrMagnitude > r2) continue;
                    if (!Hears(Mathf.Min(a.PlayerDist, b.PlayerDist)) || PairCooling(a.Key, b.Key, now)) continue;
                    if (ScVoice.Busy(a.Npc) || ScVoice.Busy(b.Npc)) continue;
                    bool atFire = SatTogether(a, b);
                    if (!gapOpen && !atFire) continue;
                    candidates.Add(new Candidate { A = a, B = b, AtFire = atFire });
                }
            }
            if (candidates.Count > 0)
            {
                // the review's finding 6: a greeting or a chat owns the subtitle bar. Asked only once a pair is ready (the
                // re-review: before the scan it ran its entity query every second while the gap was open and nobody stood close)
                if (SubtitleBusy(w) != null) return;
                // a pair at a fire first: that is where the talk is
                int fire = 0;
                for (int i = 0; i < candidates.Count; i++) if (candidates[i].AtFire) fire++;
                Candidate c;
                if (fire > 0)
                {
                    int k = rnd.Next(fire);
                    c = candidates[0];
                    for (int i = 0; i < candidates.Count; i++) if (candidates[i].AtFire && k-- == 0) { c = candidates[i]; break; }
                }
                else c = candidates[rnd.Next(candidates.Count)];
                string why;
                if (!Begin(w, c.A, c.B, c.AtFire, false, false, now, out why))
                {
                    nextExchange = now + Jitter(ScConfig.SmallTalkGapSeconds);   // not again every second
                    nextFireTalk = now + Jitter(ScConfig.CampfireTalkGapSeconds); // nor at a fire (the review's finding 2)
                    ScLog.Verbose("small talk: no exchange - " + why);
                }
                return;
            }
            if (gapOpen && now >= nextMeetRoll)
            {
                nextMeetRoll = now + MeetSeconds;
                if (rnd.NextDouble() < ScConfig.SmallTalkMeetChance) PlanMeet(w, now, null, null, false);
            }
        }

        /// <summary>Her line can be heard or read: a recording of the small talk in her voices, or a caption that can show for her.</summary>
        private static bool CanSaySomething(EntityAlive npc)
        {
            return ScSocialVoice.CanCaption(npc) || ScSocialVoice.HasAnyRecording(npc);
        }

        /// <summary>
        /// The review's finding 6: Voice Control's subtitle bar shows one line, so no small-talk line goes over a proactive
        /// greeting it is playing (NPCChatComponent.ProactiveGreetingPlaying) or a chat a player is having with someone near
        /// them (0-XNPCCore's SdcsEmoteReactions.Chatting, Voice Control's chat pause, on any living NPC - a survivor, a
        /// trader: never a zombie, an animal or a player - within ChatScanMeters of a player). The reason, or null when the bar
        /// is free.
        /// </summary>
        private static string SubtitleBusy(World w)
        {
            if (VcGreetingPlaying()) return "Voice Control is playing a greeting";
            if (w == null || w.Players == null) return null;
            List<EntityPlayer> ps = w.Players.list;
            for (int i = 0; i < ps.Count; i++)
            {
                EntityPlayer p = ps[i];
                if (p == null || p.IsDead()) continue;
                chatScan.Clear();
                w.GetEntitiesInBounds(typeof(EntityNPC), new Bounds(p.position, Vector3.one * (ChatScanMeters * 2f)), chatScan);
                for (int k = 0; k < chatScan.Count; k++)
                {
                    EntityNPC e = chatScan[k] as EntityNPC;      // the survivors (SCore's bodies) and traders: who Voice Control chats with
                    if (e == null || e.IsDead()) continue;
                    if (VcChatting(e)) { chatScan.Clear(); return ScCommunity.DisplayName(e) + " is chatting with you through Voice Control"; }
                }
                chatScan.Clear();
            }
            return null;
        }

        private const float ChatScanMeters = 20f;
        private static readonly List<Entity> chatScan = new List<Entity>();

        /// <summary>Both sit at the same fire.</summary>
        private static bool SatTogether(Body a, Body b)
        {
            Gather ga, gb;
            return gathers.TryGetValue(a.Npc.entityId, out ga) && gathers.TryGetValue(b.Npc.entityId, out gb)
                   && ga.ArrivedAt >= 0f && gb.ArrivedAt >= 0f && ga.Fire == gb.Fire;
        }

        /// <summary>
        /// Starts an exchange: the opener of the two (traits weigh it), a pair of lines for her, the opener said now and the
        /// answer due after it. False (why) when the opener's line could be neither played nor shown.
        /// </summary>
        private static bool Begin(World w, Body x, Body y, bool atFire, bool fromMeet, bool forced, float now, out string why)
        {
            // who opens: a People Person or a Cheerful one more often, a Loner less (a walker who came over always opens)
            Body a = x, b = y;
            if (!fromMeet)
            {
                float wx = ScSocialVoice.OpenerWeight(x.Rec), wy = ScSocialVoice.OpenerWeight(y.Rec);
                if (rnd.NextDouble() * (wx + wy) >= wx) { a = y; b = x; }
                // the review's finding 3: the one who can say something opens (a recording, or a caption that can show)
                if (!forced && !CanSaySomething(a.Npc) && CanSaySomething(b.Npc)) { Body s = a; a = b; b = s; }
            }
            var ex = new Exchange
            {
                A = a.Npc.entityId, B = b.Npc.entityId, AKey = a.Key, BKey = b.Key, AName = a.Name, BName = b.Name,
                Place = a.Place, AtFire = atFire, Meet = fromMeet, Forced = forced, StartedAt = now
            };
            float openSecs = -1f;
            string how;
            // the TTS fallback (off by default): an opener with no recording of SC's lines says Voice Control's own pair
            if (ScConfig.SmallTalkTtsFallback && !ScSocialVoice.HasAnyRecording(a.Npc))
            {
                float d = ScSocialVoice.VcBegin(a.Npc, b.Npc, forced);
                if (d > 0f)
                {
                    ex.Vc = true;
                    ex.Open = "(Voice Control's own pair)";
                    ex.OpenHow = "Voice Control (text to speech)";
                    openSecs = d;
                    nVc++;
                }
            }
            if (!ex.Vc)
            {
                string stem = ScSocialVoice.PickPair(a.Rec, a.Place == HomePlace, atFire, rnd);
                if (stem == null) { why = "no small-talk pair (ScSocialLines is empty)"; return false; }
                ex.Stem = stem;
                openSecs = ScSocialVoice.Say(a.Npc, ScSocialVoice.OpenKey(stem), "smalltalk.open", out how);
                if (openSecs < 0f)
                {
                    why = ScCommunity.DisplayName(a.Npc) + "'s line could be neither played nor shown: " + how;
                    return false;
                }
                ex.Open = ScSocialVoice.Words(ScSocialVoice.OpenKey(stem));
                ex.OpenHow = how;
            }
            Face(a.Npc, b.Npc);                     // ScChat keeps them turned to each other from the next AI update
            Face(b.Npc, a.Npc);
            ex.ReplyAt = now + openSecs + ReplyGapSeconds;
            ex.EndAt = ex.ReplyAt + ReplyWaitMax + 12f;           // the answer resets it
            live = ex;
            if (pairLast.Count > 256) PrunePairs(now);
            pairLast[PairKey(a.Key, b.Key)] = now;
            nextExchange = now + Jitter(ScConfig.SmallTalkGapSeconds);
            nextFireTalk = now + Jitter(ScConfig.CampfireTalkGapSeconds);
            nExchanges++;
            why = a.Name + " says to " + b.Name + (ex.Vc ? " Voice Control's pair" : " pair " + ex.Stem + ": \"" + ex.Open + "\" (" + ex.OpenHow + ")")
                  + (atFire ? " at the fire" : fromMeet ? " (walked over)" : "");
            ScLog.Verbose("small talk: " + why);
            return true;
        }

        private static void StepExchange(World w, float now)
        {
            Exchange x = live;
            if (x == null) return;
            EntityAlive a = w.GetEntity(x.A) as EntityAlive, b = w.GetEntity(x.B) as EntityAlive;
            if (a == null || b == null || a.IsDead() || b.IsDead()) { End("one of them is gone"); return; }
            string why = x.Forced ? ForcedStop(a) ?? ForcedStop(b) : StopWhy(a) ?? StopWhy(b);
            if (why != null) { End(why); return; }
            if (!x.Forced && !x.ReplySaid && Mathf.Min(NearestPlayer(w, a.position), NearestPlayer(w, b.position)) <= ScConfig.SmallTalkHearMin)
            {
                nDropped++;
                End("you walked up (the answer is dropped)");
                return;
            }
            if (!x.ReplySaid && now >= x.ReplyAt)
            {
                if (ScVoice.Busy(a) && now < x.ReplyAt + ReplyWaitMax) return;   // her clip still plays
                string busy = x.Forced ? null : SubtitleBusy(w);
                if (busy != null)
                {
                    // the review's finding 6: the answer waits for Voice Control's greeting or chat, then is dropped
                    if (now < x.ReplyAt + ReplyWaitMax) return;
                    nDropped++;
                    End("the answer was dropped: " + busy);
                    return;
                }
                float secs;
                string how;
                if (x.Vc)
                {
                    ScSocialVoice.VcReply(b);
                    secs = 6f;
                    how = "Voice Control (text to speech)";
                    x.Reply = "(Voice Control's answer)";
                }
                else
                {
                    string key = ScSocialVoice.ReplyKey(x.Stem);
                    secs = ScSocialVoice.Say(b, key, "smalltalk.reply", out how);
                    x.Reply = ScSocialVoice.Words(key);
                    if (secs < 0f) secs = 2f;                           // nothing to play or show: a nod and done
                }
                x.ReplyHow = how;
                x.ReplySaid = true;
                x.EndAt = now + secs + HoldAfterReplySeconds;
                nReplies++;
                ScLog.Verbose("small talk: " + x.BName + " answers \"" + x.Reply + "\" (" + how + ")");
                return;
            }
            if (now >= x.EndAt) End(x.ReplySaid ? "over" : "the answer never came");
        }

        /// <summary>What ends an exchange or a gathering early: everything that keeps one from starting, but walking.</summary>
        private static string StopWhy(EntityAlive npc)
        {
            Body b;
            if (byId.TryGetValue(npc.entityId, out b)) return b.Why;
            // not collected this pass (no longer a resident at home, or an enclave person who left): the reason
            Member m = ScRegistry.GetMember(npc.entityId);
            if (m != null) return MemberWhy(m, npc);
            EncPerson p = ScEnclavePeople.PersonOf(npc);
            if (p == null) return "no longer one of yours";
            return p.State != EncPerson.StateHome ? "not at home" : !p.HasHome ? "no home spot" : p.Leaving ? "walking home" : BodyWhy(npc);
        }

        /// <summary>sc chat ... force: only what makes talking impossible ends it.</summary>
        private static string ForcedStop(EntityAlive npc)
        {
            if (npc == null || npc.IsDead()) return "gone";
            if (Downed(npc)) return "downed";
            if (ScSleep.IsAsleep(npc)) return "asleep";
            if (ScVoice.Holds(npc)) return "in a talk with you";
            return null;
        }

        private static void End(string why)
        {
            if (live == null) return;
            ScLog.Verbose("small talk: " + live.AName + " and " + live.BName + " - " + why);
            lastEnd = live.AName + " and " + live.BName + ": " + why;
            live = null;
        }

        // ------------------------------------------------------------------ walking over ----

        /// <summary>
        /// Plans a walk-over: a resident pausing in her day roam (or the console's walker) and a free neighbor of the same place
        /// farther than SmallTalkRange and within SmallTalkMeetRange, whom the player can overhear and who has not talked with
        /// her lately. Her next roam walk reads it (MeetSpot).
        /// </summary>
        private static string PlanMeet(World w, float now, Body walker, Body partner, bool forced)
        {
            if (walker == null)
            {
                if (!w.IsDaytime()) return "the roam is a daytime walk";
                var walkers = new List<Body>();
                foreach (Body b in bodies)
                {
                    if (b.Why != null || b.Moving || (b.Person != null && b.Person.IsLeader) || Gathering(b.Npc)) continue;
                    if (b.Person == null && !ScConfig.ResidentRoamZone) continue;     // a post roam never calls ZoneTarget (MeetSpot)
                    if (!UAI.ScRoamState.Active.Contains(b.Npc.entityId)) continue;   // in a roam pause: her next walk is the roam's
                    if (!CanSaySomething(b.Npc)) continue;                           // she opens: a walk over to say nothing (review, finding 3)
                    walkers.Add(b);
                }
                while (walkers.Count > 0 && partner == null)
                {
                    int k = rnd.Next(walkers.Count);
                    walker = walkers[k];
                    walkers.RemoveAt(k);
                    partner = PartnerFor(walker, now);
                    if (partner == null) walker = null;
                }
                if (walker == null) return "nobody pausing in the roam has a free neighbor in reach";
            }
            meet = new Meet
            {
                Walker = walker.Npc.entityId, Partner = partner.Npc.entityId, WalkerKey = walker.Key, PartnerKey = partner.Key,
                WalkerName = walker.Name, PartnerName = partner.Name, Place = walker.Place, PlannedAt = now, Until = now + MeetPlanSeconds,
                Forced = forced
            };
            nMeetsPlanned++;
            string s = walker.Name + " will walk over to " + partner.Name + " (" + Vector3.Distance(walker.Npc.position, partner.Npc.position).ToString("0", CultureInfo.InvariantCulture) + " m) at the next roam walk";
            ScLog.Verbose("small talk: " + s);
            return s;
        }

        private static Body PartnerFor(Body walker, float now)
        {
            float lo = ScConfig.SmallTalkRange, hi = ScConfig.SmallTalkMeetRange;
            Body best = null;
            float bestD = float.MaxValue;
            foreach (Body p in bodies)
            {
                if (p == walker || p.Why != null || p.Moving || p.Place != walker.Place) continue;
                float d = Vector3.Distance(p.Npc.position, walker.Npc.position);
                if (d <= lo || d > hi) continue;
                if (!Hears(p.PlayerDist) || PairCooling(walker.Key, p.Key, now)) continue;
                if (d < bestD) { best = p; bestD = d; }
            }
            return best;
        }

        private static void StepMeet(World w, float now)
        {
            Meet m = meet;
            if (m == null) return;
            Body walker, partner;
            byId.TryGetValue(m.Walker, out walker);
            byId.TryGetValue(m.Partner, out partner);
            if (walker == null || partner == null) { DropMeet("one of them is gone or no longer at home"); return; }
            if (walker.Why != null && !m.Forced) { DropMeet(walker.Name + ": " + walker.Why); return; }
            if (partner.Why != null && !m.Forced) { DropMeet(partner.Name + ": " + partner.Why); return; }
            if (!m.Walking)
            {
                if (walker.Moving)
                {
                    m.Walking = true;                       // her next roam walk set off: it is the one to the neighbor
                    m.Until = now + MeetWalkSeconds;
                    m.StartDist = Vector3.Distance(walker.Npc.position, partner.Npc.position);
                    m.CheckAt = now + 5f;
                }
                else if (partner.Moving) DropMeet(partner.Name + " walked off first");
                else if (now > m.Until) DropMeet(walker.Name + "'s next walk never started");
                return;
            }
            float d = Vector3.Distance(walker.Npc.position, partner.Npc.position);
            // the walk the roam started went elsewhere (no floor beside the neighbor: ZoneTarget fell back to a random spot)
            if (m.CheckAt > 0f && now >= m.CheckAt)
            {
                if (d > m.StartDist - 1f && d > ScConfig.SmallTalkRange) { DropMeet(m.WalkerName + " went somewhere else"); return; }
                m.CheckAt = 0f;
            }
            if (d <= ScConfig.SmallTalkRange && (!walker.Moving || d <= 2f))
            {
                if (!m.Forced && SubtitleBusy(w) != null) { if (now > m.Until) DropMeet("the subtitle bar stayed busy"); return; }   // finding 6
                meet = null;
                if (!m.Forced && !Hears(Mathf.Min(walker.PlayerDist, partner.PlayerDist)))
                {
                    lastMeetEnd = m.WalkerName + " reached " + m.PartnerName + ", but you were " + (Mathf.Min(walker.PlayerDist, partner.PlayerDist) <= ScConfig.SmallTalkHearMin ? "too close" : "out of earshot");
                    nMeetsLapsed++;
                    return;
                }
                string why;
                if (Begin(w, walker, partner, false, true, m.Forced, now, out why)) { nMeetsMet++; lastMeetEnd = m.WalkerName + " reached " + m.PartnerName + " and they talked"; }
                else { nMeetsLapsed++; lastMeetEnd = m.WalkerName + " reached " + m.PartnerName + ": " + why; }
                return;
            }
            if (now > m.Until) DropMeet(m.WalkerName + " did not reach " + m.PartnerName + " in time");
        }

        private static void DropMeet(string why)
        {
            if (meet == null) return;
            ScLog.Verbose("small talk: the walk-over " + meet.WalkerName + " -> " + meet.PartnerName + " is off: " + why);
            lastMeetEnd = meet.WalkerName + " -> " + meet.PartnerName + ": " + why;
            nMeetsLapsed++;
            meet = null;
        }

        // ------------------------------------------------------------------ the campfire ----

        /// <summary>
        /// The gathering window open for a place now, as an id (null: none). The evening is 0-XNPCCore's phase 2 (ScNpcClock),
        /// never a blood moon night or a base attack; at home a round of drinks is one too, and so is sc chat gather.
        /// </summary>
        private static readonly Dictionary<object, string> windowNow = new Dictionary<object, string>();

        private static string WindowFor(World w, object place)
        {
            string id;
            if (windowNow.TryGetValue(place, out id)) return id;
            id = WindowNowFor(w, place);
            windowNow[place] = id;
            return id;
        }

        private static string WindowNowFor(World w, object place)
        {
            if (ScBaseAttack.BloodMoonWindow(w)) return null;
            Enclave en = place as Enclave;
            if (en != null && en.IsHostile) return null;
            if (place == HomePlace)
            {
                if (ScBaseAttack.Open) return null;
                if (consoleGatherUntil > Time.time) return "console:" + consoleGatherId;
                if (ScActivities.DrinksActive) return "drinks:" + ScRegistry.Community.DrinksDay;
            }
            string eve;
            return ScNpcClock.Evening(w, out eve) ? "evening:" + eve : null;
        }

        private static void StartGathers(World w, float now)
        {
            if (bodies.Count == 0) return;
            for (int i = 0; i < bodies.Count; i++)
            {
                Body b = bodies[i];
                if (b.Why != null || gathers.ContainsKey(b.Npc.entityId)) continue;
                if (b.Person != null && b.Person.IsLeader) continue;          // the player finds an enclave's leader at their spot
                string window = WindowFor(w, b.Place);
                if (window == null) continue;
                if (GatheredIn(b.Key, window)) continue;
                KeyValuePair<string, float> seen;
                if (!windowSeen.TryGetValue(b.Place, out seen) || seen.Key != window)
                {
                    windowSeen[b.Place] = seen = new KeyValuePair<string, float>(window, now);
                    ScLog.Verbose("campfire: the gathering window " + window + " opened for " + PlaceName(b.Place));
                }
                // staggered by name, so they do not all set off in the same second (the console's gathering at once)
                float setOff = seen.Value + (window.StartsWith("console:", StringComparison.Ordinal) ? 0f : (ScVoice.Fnv(b.Key) % 1000u) / 1000f * GatherStagger);
                if (now < setOff) continue;
                Fire f = FireOf(w, b.Place, now);
                if (f == null || !f.Found) continue;                          // no fire (yet): a fire placed later this evening still counts
                int count = 0;
                foreach (Gather g in gathers.Values) if (g.Place == b.Place && g.Fire == f.Pos) count++;
                if (count >= ScConfig.CampfireMaxPeople) continue;
                Vector3 spot;
                if (!RingSpot(w, b.Place, f.Pos, count, out spot)) { MarkGathered(b.Key, window); continue; }
                MarkGathered(b.Key, window);
                gathers[b.Npc.entityId] = new Gather
                {
                    Id = b.Npc.entityId, Key = b.Key, Name = b.Name, Window = window, Place = b.Place, Fire = f.Pos, Spot = spot,
                    SetOffAt = now, ArriveBy = now + GatherWalkSeconds
                };
                ScLog.Verbose("campfire: " + b.Name + " walks to the fire at " + f.Pos + " (" + window + ")");
            }
        }

        private static void StepGathers(World w, float now)
        {
            if (gathers.Count == 0) return;
            scratchIds.Clear();
            scratchIds.AddRange(gathers.Keys);
            foreach (int id in scratchIds)
            {
                Gather g = gathers[id];
                EntityAlive npc = w.GetEntity(id) as EntityAlive;
                if (npc == null || npc.IsDead()) { Leave(g, null, "gone", false); continue; }
                string window = WindowFor(w, g.Place);
                if (window == null) { Leave(g, npc, "the gathering is over", false); continue; }
                if (window != g.Window)
                {
                    // the review's finding 4: one open window giving way to another (drinks poured in the evening, or over) is
                    // the same gathering for her - she stays, and counts as having gathered in the new one too
                    g.Window = window;
                    MarkGathered(g.Key, window);
                }
                string why = StopWhy(npc);
                if (why != null) { Leave(g, npc, why, false); continue; }
                if (g.ArrivedAt < 0f)
                {
                    Vector3 d = npc.position - g.Spot;
                    float xz = Mathf.Sqrt(d.x * d.x + d.z * d.z);
                    if (xz <= 1.2f && Mathf.Abs(d.y) < 1.6f)
                    {
                        float lo = ScConfig.CampfireMinSeconds, hi = Mathf.Max(lo, ScConfig.CampfireMaxSeconds);
                        g.ArrivedAt = now;
                        g.LeaveAt = now + lo + (float)rnd.NextDouble() * (hi - lo);
                        nGathered++;
                        ScLog.Verbose("campfire: " + g.Name + " sits at the fire for " + (g.LeaveAt - now).ToString("0", CultureInfo.InvariantCulture) + " s");
                    }
                    else if (now > g.ArriveBy) { Leave(g, npc, "could not get to the fire", true); continue; }
                }
                else if (now >= g.LeaveAt) { Leave(g, npc, "done", false); continue; }
                if (g.ArrivedAt >= 0f && ScConfig.CampfireLit && !g.Lit) Light(npc, g);
            }
        }

        private static void Leave(Gather g, EntityAlive npc, string why, bool lapsed)
        {
            gathers.Remove(g.Id);
            if (npc != null && g.Lit) Unlight(npc);
            if (lapsed) nGatherLapsed++;
            ScLog.Verbose("campfire: " + g.Name + " leaves the fire (" + why + ")");
        }

        /// <summary>The fire's flames on her: 0-XNPCCore's four work-flame cvars (its SdcsWorkFlame.Want, written the same way).</summary>
        private static void Light(EntityAlive npc, Gather g)
        {
            if (npc.Buffs == null) return;
            npc.Buffs.SetCustomVar(FlameX, g.Fire.x, true, CVarOperation.set, true);
            npc.Buffs.SetCustomVar(FlameY, g.Fire.y, true, CVarOperation.set, true);
            npc.Buffs.SetCustomVar(FlameZ, g.Fire.z, true, CVarOperation.set, true);
            npc.Buffs.SetCustomVar(FlameFlag, 1f, true, CVarOperation.set, true);
            g.Lit = true;
        }

        private static void Unlight(EntityAlive npc)
        {
            if (npc.Buffs != null && npc.Buffs.GetCustomVar(FlameFlag) != 0f) npc.Buffs.SetCustomVar(FlameFlag, 0f, true, CVarOperation.set, true);
        }

        private const string FlameFlag = "SdcsWorkFlame", FlameX = "SdcsWorkFlameX", FlameY = "SdcsWorkFlameY", FlameZ = "SdcsWorkFlameZ";

        /// <summary>
        /// Every 5 s: a work flame left on an SC body that is not sitting at a fire for SC now is dropped - the cvars are saved
        /// with the entity, so a body saved mid-gathering would come back with the fire lit for good. A body in 0-XNPCCore's
        /// own routine (SdcsResident: the night lease, or a wild resident) is its, and left alone.
        /// </summary>
        private static void SweepFlames(World w)
        {
            foreach (Member m in ScRegistry.Members.Values) SweepFlame(ScCommunity.FindNpc(m.Id));
            foreach (EncPerson p in ScRegistry.EncPeople) if (p != null && p.EntityId >= 0) SweepFlame(ScEnclavePeople.OwnBody(p));
        }

        private static void SweepFlame(EntityAlive npc)
        {
            if (npc == null || npc.Buffs == null || npc.isEntityRemote) return;
            if (npc.Buffs.GetCustomVar(FlameFlag) < 0.5f) return;
            Gather g;
            if (gathers.TryGetValue(npc.entityId, out g) && g.Lit) return;
            if (npc.Buffs.GetCustomVar("SdcsResident") >= 0.5f) return;
            npc.Buffs.SetCustomVar(FlameFlag, 0f, true, CVarOperation.set, true);
            nFlamesCleared++;
            ScLog.Verbose("campfire: a work flame left on " + ScCommunity.DisplayName(npc) + " (" + npc.entityId + ") put out");
        }

        // ---- the fire and the ring ----

        private static bool[] campfireTypes;

        /// <summary>Block types that are a campfire: CampfireBlocks names (exact, or a prefix ending in *) that are work stations (their flames are the station's).</summary>
        private static bool[] CampfireTypes()
        {
            if (campfireTypes != null) return campfireTypes;
            Block[] list = Block.list;
            if (list == null) return null;
            string[] entries = (ScConfig.CampfireBlocks ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            var t = new bool[list.Length];
            for (int i = 0; i < list.Length; i++)
            {
                Block b = list[i];
                if (b == null || !(b is BlockWorkstation)) continue;
                string name = b.GetBlockName();
                foreach (string raw in entries)
                {
                    string e = raw.Trim();
                    if (e.Length == 0) continue;
                    bool hit = e[e.Length - 1] == '*'
                        ? e.Length > 1 && name.StartsWith(e.Substring(0, e.Length - 1), StringComparison.OrdinalIgnoreCase)
                        : string.Equals(name, e, StringComparison.OrdinalIgnoreCase);
                    if (hit) { t[i] = true; break; }
                }
            }
            campfireTypes = t;
            return t;
        }

        /// <summary>The place's box: the home zone within SleepZoneHeight of the claim block, or the enclave's building.</summary>
        private static bool BoxOf(object place, out Vector3i min, out Vector3i max, out Vector3 center)
        {
            min = max = Vector3i.zero;
            center = Vector3.zero;
            Enclave en = place as Enclave;
            if (en != null)
            {
                min = en.Poi;
                max = new Vector3i(en.Poi.x + Math.Max(1, en.Size.x) - 1, en.Poi.y + Math.Max(1, en.Size.y), en.Poi.z + Math.Max(1, en.Size.z) - 1);
                EncPerson lead = ScEnclavePeople.LeaderOf(en);
                center = lead != null && lead.HasHome ? lead.HomeCentre : new Vector3((min.x + max.x) * 0.5f, min.y, (min.z + max.z) * 0.5f);
                return true;
            }
            if (!ScZone.HasHome) return false;
            int xMin, xMax, zMin, zMax;
            ScZone.Bounds(out xMin, out xMax, out zMin, out zMax);
            Vector3i home = ScZone.Home;
            int band = Math.Max(2, ScConfig.SleepZoneHeight);
            min = new Vector3i(xMin, home.y - band, zMin);
            max = new Vector3i(xMax, home.y + band, zMax);
            center = new Vector3(home.x + 0.5f, home.y, home.z + 0.5f);
            return true;
        }

        private static bool InPlace(object place, Vector3i c)
        {
            Enclave en = place as Enclave;
            return en != null ? en.Contains(c.x, c.z) : ScZone.Contains(c);
        }

        /// <summary>The place's campfire (the one nearest its center: the claim block, or the enclave leader's spot), cached FireCacheSeconds.</summary>
        private static Fire FireOf(World w, object place, float now)
        {
            Fire f;
            if (fires.TryGetValue(place, out f) && now < f.Until && (!f.Found || IsCampfire(w, f.Pos))) return f;
            f = new Fire { Until = now + FireCacheSeconds };
            fires[place] = f;
            Vector3i min, max;
            Vector3 center;
            bool[] types = CampfireTypes();
            if (types == null || !BoxOf(place, out min, out max, out center)) return f;
            float best = float.MaxValue;
            for (int cx = World.toChunkXZ(min.x); cx <= World.toChunkXZ(max.x); cx++)
            {
                for (int cz = World.toChunkXZ(min.z); cz <= World.toChunkXZ(max.z); cz++)
                {
                    Chunk ch = w.GetChunkSync(cx, cz) as Chunk;
                    if (ch == null || ch.NeedsDecoration || ch.InProgressUnloading) continue;
                    var stubs = ch.blockEntityStubs;
                    if (stubs == null) continue;
                    List<BlockEntityData> list = stubs.list;
                    for (int i = 0; i < list.Count; i++)
                    {
                        BlockEntityData d = list[i];
                        if (d == null) continue;
                        BlockValue bv = d.blockValue;
                        if (bv.ischild || bv.type <= 0 || bv.type >= types.Length || !types[bv.type]) continue;
                        Vector3i p = d.pos;
                        if (p.x < min.x || p.x > max.x || p.z < min.z || p.z > max.z || p.y < min.y || p.y > max.y || !InPlace(place, p)) continue;
                        float dist = (new Vector3(p.x + 0.5f, p.y, p.z + 0.5f) - center).sqrMagnitude;
                        if (dist < best) { best = dist; f.Found = true; f.Pos = p; }
                    }
                }
            }
            return f;
        }

        private static bool IsCampfire(World w, Vector3i p)
        {
            bool[] types = CampfireTypes();
            BlockValue bv = w.GetBlock(p);
            return types != null && !bv.ischild && bv.type > 0 && bv.type < types.Length && types[bv.type];
        }

        /// <summary>
        /// A place on the ring around the fire for the next sitter (0-XNPCCore's GatherSpot: 45-degree steps 2 m out, on the
        /// fire's level or one off it, a real floor inside the place), not one another sitter has; a second ring farther out
        /// when the first is full or blocked.
        /// </summary>
        private static bool RingSpot(World w, object place, Vector3i fire, int index, out Vector3 spot)
        {
            spot = Vector3.zero;
            for (int k = 0; k < 16; k++)
            {
                int i = (index + k) % 16;
                float radius = i < 8 ? RingMeters : RingOuter;
                double ang = ((i % 8) * 45.0 + (i < 8 ? 0.0 : 22.5)) * Math.PI / 180.0;
                float x = fire.x + 0.5f + (float)Math.Cos(ang) * radius, z = fire.z + 0.5f + (float)Math.Sin(ang) * radius;
                int cx = Mathf.FloorToInt(x), cz = Mathf.FloorToInt(z);
                for (int dy = 0; dy < 3; dy++)
                {
                    Vector3i c = new Vector3i(cx, fire.y + (dy == 0 ? 0 : dy == 1 ? -1 : 1), cz);
                    if (!InPlace(place, c) || Taken(c)) continue;
                    string why;
                    if (!ScCommunity.GoodPost(w, c, false, out why)) continue;
                    spot = new Vector3(x, c.y, z);
                    return true;
                }
            }
            return false;
        }

        private static bool Taken(Vector3i c)
        {
            foreach (Gather g in gathers.Values) if (World.worldToBlockPos(g.Spot) == c) return true;
            return false;
        }

        private static string PlaceName(object place)
        {
            Enclave en = place as Enclave;
            return en != null ? "the enclave " + en.Name + " (" + en.Id + ")" : "home";
        }

        // ------------------------------------------------------------------ 0-XNPCCore, bound once ----

        private static bool downedFailed;

        /// <summary>0-XNPCCore 3.4.06's own downed query (SdcsDowned.IsDowned), else its cvar (an older 0-XNPCCore: never downed).</summary>
        internal static bool Downed(EntityAlive npc)
        {
            if (!downedFailed)
            {
                try { return DownedCore(npc); }
                catch (Exception) { downedFailed = true; }   // a 0-XNPCCore without the type: TypeLoadException / MissingMethodException
            }
            return npc != null && npc.Buffs != null && npc.Buffs.GetCustomVar("SdcsDowned") != 0f;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool DownedCore(EntityAlive npc) { return SdcsDowned.IsDowned(npc); }

        private static bool chatBound;
        private static MethodInfo mChatting;

        /// <summary>Voice Control is chatting with the player on her (0-XNPCCore's SdcsEmoteReactions.Chatting, internal: by reflection).</summary>
        private static bool VcChatting(EntityAlive npc)
        {
            if (!chatBound)
            {
                chatBound = true;
                try
                {
                    Type t = typeof(AvatarSdcsNpcController).Assembly.GetType("SdcsEmoteReactions", false);
                    mChatting = t != null ? t.GetMethod("Chatting", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public, null, new[] { typeof(EntityAlive) }, null) : null;
                }
                catch (Exception e) { ScLog.Once("social-bind-chatting", e); }
            }
            if (mChatting == null) return false;
            try { return (bool)mChatting.Invoke(null, new object[] { npc }); }
            catch (Exception e) { ScLog.Once("social-chatting", e); return false; }
        }

        private static bool greetBound;
        private static PropertyInfo pGreetPlaying;

        /// <summary>Voice Control's NPCChatComponent.ProactiveGreetingPlaying (internal static: by reflection, bound once; false when absent).</summary>
        private static bool VcGreetingPlaying()
        {
            if (!greetBound)
            {
                greetBound = true;
                try
                {
                    Type t = Patches_VoiceControl.ChatType;
                    pGreetPlaying = t != null ? t.GetProperty("ProactiveGreetingPlaying", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public) : null;
                    if (t != null && pGreetPlaying == null)
                        ScLog.Once("social-bind-greeting", "small talk: Voice Control's NPCChatComponent.ProactiveGreetingPlaying not found - small talk may cover one of its greetings in the subtitle bar");
                }
                catch (Exception e) { ScLog.Once("social-bind-greeting", e); }
            }
            if (pGreetPlaying == null) return false;
            try { return (bool)pGreetPlaying.GetValue(null, null); }
            catch (Exception e) { ScLog.Once("social-greeting", e); return false; }
        }

        /// <summary>SCore's IsEnemyNearby (an enemy that can be seen within 30 m, or a fresh revenge target): what EnemyNotNear on ScChat and ScGather asks.</summary>
        private static bool EnemyInSight(EntityAlive npc)
        {
            try { return UAI.SCoreUtils.IsEnemyNearby(npc, FightRange); }
            catch (Exception e) { ScLog.Once("social-enemy-near", e); return false; }
        }

        // ------------------------------------------------------------------ console ----

        /// <summary>
        /// sc chat: the switches, the clock, the cooldowns, what is going on and every body's verdict.
        /// sc chat &lt;a&gt; &lt;b&gt; [force]: a starts an exchange with b now (the gap, the pair cooldown, the distance between them and
        /// your distance ignored; force: every other gate too, but dead, downed, asleep and a talk with you).
        /// sc chat meet &lt;a&gt; &lt;b&gt;: a walks over to b at her next roam walk.
        /// sc chat gather [end]: a gathering at the home fire now, for ConsoleGatherSeconds whatever the hour; end: everyone leaves.
        /// sc chat lines [id|name]: the pairs, and how many of them a body's voices have recorded.
        /// </summary>
        public static void ConsoleCmd(List<string> p, Action<string> output)
        {
            ScConfig.EnsureParsed();
            World w = ScCommunity.World;
            if (w == null || !ScRegistry.Loaded) { output("[SC] no world, or the registry is not loaded"); return; }
            float now = Time.time;
            string sub = p.Count > 1 ? p[1].ToLowerInvariant() : "";
            if (sub == "" || sub == "status")
            {
                Report(w, now, output);
                return;
            }
            Collect(w, true);
            if (sub == "gather")
            {
                if (p.Count > 2 && p[2].Equals("end", StringComparison.OrdinalIgnoreCase))
                {
                    consoleGatherUntil = -1f;
                    int n = gathers.Count;
                    scratchIds.Clear();
                    scratchIds.AddRange(gathers.Keys);
                    foreach (int id in scratchIds) Leave(gathers[id], w.GetEntity(id) as EntityAlive, "sc chat gather end", false);
                    output("[SC] campfire: " + n + " sitter(s) sent back to their routine");
                    return;
                }
                if (!ScConfig.CampfireGathering) output("[SC] note: CampfireGathering is off - nobody goes until it is on");
                Fire f = FireOf(w, HomePlace, now);
                fires.Remove(HomePlace);                     // searched again on the tick, so a fire placed just now counts
                if (f == null || !f.Found) { output("[SC] campfire: no campfire (" + ScConfig.CampfireBlocks + ") inside the home zone - place one first"); return; }
                consoleGatherId++;
                consoleGatherUntil = now + ConsoleGatherSeconds;
                output("[SC] campfire: a gathering at the fire at " + f.Pos + " for the next " + ConsoleGatherSeconds + " s - every free resident walks over within a second or two");
                return;
            }
            if (sub == "lines")
            {
                EntityAlive npc = p.Count > 2 ? FindBody(w, p[2]) : null;
                output("[SC] small talk: " + ScSocialVoice.PairCount() + " pairs (" + ScSocialVoice.PairCount() * 2 + " lines a voice; Tools/smalltalk_lines.py), TTS fallback " + ScConfig.SmallTalkTtsFallback);
                if (npc != null) output("[SC]   " + ScCommunity.DisplayName(npc) + ": " + ScSocialVoice.Coverage(npc));
                else foreach (Body b in bodies) output("[SC]   " + b.Name + ": " + ScSocialVoice.Coverage(b.Npc));
                return;
            }
            bool meetCmd = sub == "meet";
            int at = meetCmd ? 2 : 1;
            if (p.Count < at + 2) { output("[SC] usage: sc chat [status] | sc chat <a> <b> [force] | sc chat meet <a> <b> | sc chat gather [end] | sc chat lines [id|name]"); return; }
            Body x = FindCollected(w, p[at]), y = FindCollected(w, p[at + 1]);
            if (x == null) { output("[SC] no member or enclave person '" + p[at] + "' loaded"); return; }
            if (y == null) { output("[SC] no member or enclave person '" + p[at + 1] + "' loaded"); return; }
            if (x == y) { output("[SC] two different people, please"); return; }
            bool force = p.Count > at + 2 && p[at + 2].Equals("force", StringComparison.OrdinalIgnoreCase);
            if (meetCmd)
            {
                if (meet != null) DropMeet("replaced by sc chat meet");
                string s = PlanMeet(w, now, x, y, true);
                output("[SC] " + s + (UAI.ScRoamState.Active.Contains(x.Npc.entityId) ? " (she is roaming)" : " - she is not roaming now; the plan lapses in " + MeetPlanSeconds + " s if no roam walk starts"));
                return;
            }
            if (!force)
            {
                if (x.Why != null) { output("[SC] " + x.Name + " cannot talk now: " + x.Why + " (add force to try anyway)"); return; }
                if (y.Why != null) { output("[SC] " + y.Name + " cannot talk now: " + y.Why + " (add force to try anyway)"); return; }
            }
            else
            {
                string hard = ForcedStop(x.Npc) ?? ForcedStop(y.Npc);
                if (hard != null) { output("[SC] not even with force: " + hard); return; }
            }
            if (live != null) End("replaced by sc chat");
            if (meet != null) DropMeet("replaced by sc chat");
            string why;
            bool ok = Begin(w, x, y, SatTogether(x, y), false, true, now, out why);
            output("[SC] " + (ok ? "small talk: " : "no exchange: ") + why
                   + (ok ? " - the answer in " + Mathf.Max(0f, live.ReplyAt - now).ToString("0.0", CultureInfo.InvariantCulture) + " s; they are " + Vector3.Distance(x.Npc.position, y.Npc.position).ToString("0.0", CultureInfo.InvariantCulture) + " m apart" : ""));
        }

        private static Body FindCollected(World w, string key)
        {
            EntityAlive npc = FindBody(w, key);
            if (npc == null) return null;
            Body b;
            return byId.TryGetValue(npc.entityId, out b) ? b : null;
        }

        /// <summary>A member (id, or a case-insensitive part of the name) or an enclave person (their name, or their body's entity id) with a loaded body.</summary>
        private static EntityAlive FindBody(World w, string key)
        {
            int id;
            if (int.TryParse(key, out id))
            {
                EntityAlive e = w.GetEntity(id) as EntityAlive;
                if (e != null) return e;
            }
            foreach (Member m in ScRegistry.Members.Values)
                if (m.Name != null && m.Name.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    EntityAlive e = ScCommunity.FindNpc(m.Id);
                    if (e != null) return e;
                }
            foreach (EncPerson ep in ScRegistry.EncPeople)
                if (ep != null && ep.Name != null && ep.Name.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    EntityAlive e = ScEnclavePeople.OwnBody(ep);
                    if (e != null) return e;
                }
            return null;
        }

        private static void Report(World w, float now, Action<string> output)
        {
            output("[SC] social life (round U3): " + ScConfig.SocialLine());
            output("[SC] clock: " + ScNpcClock.Describe(w));
            string window = WindowFor(w, HomePlace);
            Fire hf = FireOf(w, HomePlace, now);
            output("[SC] home: gathering window " + (window ?? "closed") + ", campfire " + (hf != null && hf.Found ? "at " + hf.Pos : "none found (" + ScConfig.CampfireBlocks + ")")
                   + (ScActivities.DrinksActive ? ", a round of drinks is on" : "") + (ScBaseAttack.Open ? ", a base attack (nothing social)" : "")
                   + (ScVoice.TalkOpen ? ", a talk is open (no new exchange)" : ""));
            output("[SC] cooldowns: next exchange " + Until(nextExchange, now) + ", next at a fire " + Until(nextFireTalk, now) + ", next walk-over roll " + Until(nextMeetRoll, now)
                   + "; pairs cooling (" + ScConfig.SmallTalkPairMinutes + " min): " + Cooling(now));
            output("[SC] counts this session: " + nExchanges + " exchange(s) (" + nReplies + " answered, " + nDropped + " dropped as you walked up, " + nVc
                   + " Voice Control's), walk-overs " + nMeetsPlanned + " planned / " + nMeetsMet + " met / " + nMeetsLapsed + " off, " + nGathered + " sat at a fire ("
                   + nGatherLapsed + " never got there), " + nFlamesCleared + " stale flame(s) put out; last end: " + (lastEnd.Length > 0 ? lastEnd : "-")
                   + "; last walk-over: " + (lastMeetEnd.Length > 0 ? lastMeetEnd : "-") + "; " + ScSocialVoice.Counters());
            if (live != null)
                output("[SC] now: " + live.AName + " -> " + live.BName + (live.Vc ? " (Voice Control's pair)" : " pair " + live.Stem) + ": \"" + live.Open + "\" (" + live.OpenHow + ")"
                       + (live.ReplySaid ? ", answered \"" + live.Reply + "\" (" + live.ReplyHow + ")" : ", the answer in " + Mathf.Max(0f, live.ReplyAt - now).ToString("0.0", CultureInfo.InvariantCulture) + " s")
                       + (live.AtFire ? ", at the fire" : "") + (live.Meet ? ", walked over" : "") + (live.Forced ? ", forced" : ""));
            if (meet != null)
                output("[SC] walk-over: " + meet.WalkerName + " -> " + meet.PartnerName + (meet.Walking ? " walking" : " waiting for her next roam walk") + ", lapses " + Until(meet.Until, now));
            foreach (Gather g in gathers.Values)
                output("[SC] fire " + g.Fire + ": " + g.Name + (g.ArrivedAt >= 0f ? " sitting, leaves " + Until(g.LeaveAt, now) + (g.Lit ? ", lit" : "") : " walking, due " + Until(g.ArriveBy, now)) + " (" + g.Window + ")");
            Collect(w, true);
            if (bodies.Count == 0) { output("[SC] nobody loaded"); return; }
            foreach (Body b in bodies)
            {
                List<string> doneList;
                string done = gatheredIn.TryGetValue(b.Key, out doneList) && doneList.Count > 0 ? string.Join(", ", doneList.ToArray()) : null;
                output("[SC]   " + b.Name + " (" + b.Npc.entityId + ", " + PlaceName(b.Place) + "): " + (b.Why == null ? "free" : b.Why) + (b.Moving ? ", walking" : "")
                       + ", you " + (b.PlayerDist < 10000f ? b.PlayerDist.ToString("0", CultureInfo.InvariantCulture) + " m" : "-") + (Hears(b.PlayerDist) ? " (in earshot)" : "")
                       + (done != null ? ", gathered in " + done : "") + (UAI.ScRoamState.Active.Contains(b.Npc.entityId) ? ", roaming" : ""));
            }
        }

        private static string Until(float t, float now)
        {
            if (t <= now) return "now";
            return "in " + (t - now).ToString("0", CultureInfo.InvariantCulture) + " s";
        }

        private static string Cooling(float now)
        {
            var sb = new StringBuilder();
            float span = ScConfig.SmallTalkPairMinutes * 60f;
            foreach (KeyValuePair<string, float> kv in pairLast)
            {
                float left = span - (now - kv.Value);
                if (left <= 0f) continue;
                sb.Append(sb.Length > 0 ? ", " : "").Append(kv.Key.Replace("m:", "").Replace("enc:", "enclave person ")).Append(' ').Append((left / 60f).ToString("0.0", CultureInfo.InvariantCulture)).Append(" min");
            }
            return sb.Length > 0 ? sb.ToString() : "none";
        }
    }

    /// <summary>
    /// Round U3 (research B7): 0-XNPCCore's own routine hours, read rather than mirrored. Its phase 2 (the evening: home, the
    /// campfire) runs from dusk - HomeBeforeDuskHours to dusk + SleepAfterDuskHours, then phase 3 (sleep). SC's night lease
    /// must open only in phase 3 (round n06: no strolling, and since 3.4.06 every phase-2 resident walks to the campfire), so
    /// the lease waits the larger of SC's SleepAfterDuskHours and 0-XNPCCore's; the campfire window is that same phase 2.
    /// Both are internal static properties on AvatarSdcsNpcController (they load its Settings.xml on first read): reflection.
    /// </summary>
    public static class ScNpcClock
    {
        private static bool bound;
        private static PropertyInfo pSleepAfter, pHomeBefore;

        private static void Bind()
        {
            if (bound) return;
            bound = true;
            const BindingFlags S = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            try
            {
                pSleepAfter = typeof(AvatarSdcsNpcController).GetProperty("RoutineSleepAfterDuskHours", S);
                pHomeBefore = typeof(AvatarSdcsNpcController).GetProperty("RoutineHomeBeforeDuskHours", S);
            }
            catch (Exception e) { ScLog.Once("npc-clock-bind", e); }
            if (pSleepAfter == null || pHomeBefore == null)
                ScLog.Warn("0-XNPCCore's Routine/SleepAfterDuskHours or HomeBeforeDuskHours was not found - the night lease and the campfire use SC's "
                           + "SleepAfterDuskHours and 1 hour before dusk instead");
        }

        private static float Read(PropertyInfo pi)
        {
            if (pi == null) return -1f;
            try
            {
                object v = pi.GetValue(null, null);
                return v is float ? Mathf.Clamp((float)v, 0f, 12f) : -1f;   // 0-XNPCCore clamps its offsets to 0-12 h (RoutineRules.ClampOffsetHours)
            }
            catch (Exception e)
            {
                ScLog.Once("npc-clock-read", e);
                return -1f;
            }
        }

        /// <summary>The two values as last read, and when (real time): read again every CacheSeconds (the review's nit).</summary>
        private static float sleepAfter = -1f, homeBefore = -1f, readAt = -1000f;
        private const float CacheSeconds = 5f;

        private static void Refresh()
        {
            float now = Time.unscaledTime;
            if (readAt >= 0f && now - readAt < CacheSeconds) return;
            readAt = now;
            Bind();
            sleepAfter = Read(pSleepAfter);
            homeBefore = Read(pHomeBefore);
        }

        /// <summary>0-XNPCCore's Routine/SleepAfterDuskHours, or -1 when it cannot be read.</summary>
        public static float NpcSleepAfterDusk() { Refresh(); return sleepAfter; }

        /// <summary>0-XNPCCore's Routine/HomeBeforeDuskHours, or -1 when it cannot be read.</summary>
        public static float NpcHomeBeforeDusk() { Refresh(); return homeBefore; }

        /// <summary>Hours after dusk before the night lease may open: SC's SleepAfterDuskHours, never less than 0-XNPCCore's own.</summary>
        public static float LeaseAfterDusk()
        {
            float sc = Mathf.Max(0f, ScConfig.SleepAfterDuskHours), npc = NpcSleepAfterDusk();
            return npc >= 0f ? Mathf.Max(sc, npc) : sc;
        }

        /// <summary>Hours before dusk the evening starts: 0-XNPCCore's HomeBeforeDuskHours, else 1 (its default).</summary>
        public static float EveningBeforeDusk()
        {
            float npc = NpcHomeBeforeDusk();
            return npc >= 0f ? npc : 1f;
        }

        /// <summary>
        /// Is it 0-XNPCCore's evening (phase 2) now: from dusk - EveningBeforeDusk to dusk + LeaseAfterDusk, by the world's own
        /// hour and dusk (as SdcsRoutine reads them). id: the evening's day (the day it started), so a window that crosses
        /// midnight keeps one id.
        /// </summary>
        public static bool Evening(World w, out string id)
        {
            id = null;
            if (w == null) return false;
            float hour = (w.worldTime % 24000UL) / 1000f;
            float start = w.DuskHour - EveningBeforeDusk(), len = EveningBeforeDusk() + LeaseAfterDusk();
            float into = hour - start;
            while (into < 0f) into += 24f;
            while (into >= 24f) into -= 24f;
            if (len <= 0f || into >= len) return false;
            ulong back = (ulong)(into * 1000f);
            ulong t = w.worldTime >= back ? w.worldTime - back : 0UL;
            id = GameUtils.WorldTimeToDays(t).ToString(CultureInfo.InvariantCulture);
            return true;
        }

        /// <summary>For sc chat and sc sleep: both sides' hours and the window.</summary>
        public static string Describe(World w)
        {
            string id;
            bool eve = Evening(w, out id);
            float hour = w != null ? (w.worldTime % 24000UL) / 1000f : 0f;
            return "hour " + hour.ToString("0.00", CultureInfo.InvariantCulture) + ", dusk " + (w != null ? w.DuskHour : 0)
                   + "; 0-XNPCCore HomeBeforeDuskHours " + Show(NpcHomeBeforeDusk()) + ", SleepAfterDuskHours " + Show(NpcSleepAfterDusk())
                   + "; SC SleepAfterDuskHours " + ScConfig.SleepAfterDuskHours.ToString("0.##", CultureInfo.InvariantCulture)
                   + " -> the lease opens " + LeaseAfterDusk().ToString("0.##", CultureInfo.InvariantCulture) + " h after dusk; evening "
                   + (eve ? "NOW (day " + id + ")" : "not now");
        }

        private static string Show(float v) { return v < 0f ? "unknown" : v.ToString("0.##", CultureInfo.InvariantCulture); }
    }
}
#endif
