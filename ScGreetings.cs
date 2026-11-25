using System;
using System.Collections.Generic;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// V2: SC's own greetings and the members' chatter (research\voices\REPORT.md section 4, option (b); SC_Ideas_and_Voices_Plan.md
    /// 4.4). A tick for the local player (ModEvents.GameUpdate, twice a second), host / single player, outside SC's talk.
    ///
    /// Greetings (ScGreetings), on the community's survivors (ScXnpc.IsSurvivorBody), by how they stand with you
    /// (Relation): greet.stranger (a wild survivor), greet.recruit (a prospect), greet.member (a resident at home),
    /// greet.follower (a member, an enlisted enclave person or a hire following you), greet.enclave (one of an enclave's
    /// people). Nobody hostile to you greets (a grudge, a hostile enclave), nor an outpost stand-in, a runner or a missing
    /// enclave person (Voice Control's greeting stays theirs).
    ///  - "Meet": the first time each in-game day a survivor within ScGreetMeetRange (12 m) is in your line of sight.
    ///  - "Walk-up": within ScGreetWalkUpRange (3 m) while you look at them and see them, at most every
    ///    ScGreetWalkUpSeconds (120 s).
    ///  - Both remembered by person (PersonKey: a member's name, an enclave person's id, else the entity id), so a member
    ///    re-created the same day (back from a run, sc restore) doesn't meet-greet you again.
    ///  - Never while they or you are in a fight (a live foe of theirs within 30 m), while a talk is open, while they are
    ///    already saying something; one greeting at a time, ScGreetGapSeconds (6 s) apart, the nearest first.
    ///  - Only where their voice has a clip of that greeting (VoiceBarkDebug: a silent one is shown and logged). The
    ///    survivor turns to you (Voice Control's FaceEntity).
    /// Where SC greets, Voice Control doesn't: Patches_VoiceControl's prefix on NPCChatComponent.TriggerProactiveGreeting
    /// asks OwnsGreeting - one of the community's survivors whose voice has a clip of their greeting - and skips Voice
    /// Control's greeting (canned or LLM) for them. Without a clip Voice Control greets them as before.
    /// Round m1w (P4, ScGreetingsOwnAll): SC owns the greetings of SC's people it means to keep silent - an outpost stand-in,
    /// someone hostile to you, a missing enclave person, and a member away from home when no member line fits away (GreetKeys:
    /// none of the home-only ones, HomeOnly) - so Voice Control greets none of them; a member away (a runner met on the road,
    /// one on the way to the base) greets with the member lines that fit anywhere. Review of adee167, fix 4: where SC has a
    /// greeting for someone but no recording of it, Voice Control greets them as before. Re-check fix 1: a body out of sight
    /// (SCore's onMission - a member posted to an outpost or on a hidden run, parked at home) greets no one.
    ///
    /// Chatter (ScChatter): every ScChatterSeconds (90 s, a quarter either way), with ScChatterChance (0.5), one resident at
    /// home within ScChatterRange (15 m) of you, not fighting, not talking, says a chatter line - only one with a clip.
    ///
    /// Round MP4 (multiplayer): the server greets every player for themselves - the same rules, each player's own memory of
    /// who met them today and when (PlayerKey), one greeting at a time per player - and a remote player's greeting is said on
    /// their machine only (ScBarks.SayFrom's to, NetPackageScVoice). Chatter comes from a resident near any player and is
    /// heard by everyone in earshot. On a client, OwnsGreeting answers from server-synced facts (the copy of the registry,
    /// the synced cvars), the same facts the server greets that player by - so a client hears SC's greeting or Voice
    /// Control's, never neither.
    /// </summary>
    public static class ScGreetings
    {
        private const float Interval = 0.5f;
        private const float FightRange = 30f;
        /// <summary>Looking at a survivor: within about 25 degrees of the look direction.</summary>
        private const float LookDot = 0.9f;

        private static float nextTick, nextChatter = -1f, lastSpoke = -999f;
        /// <summary>Round MP4: when the last chatter line was said (real time) - a greeting keeps its gap after it, for every player.</summary>
        private static float lastChatter = -999f;
        /// <summary>Round MP4: when someone last greeted each player (real time), by entity id: one greeting at a time, per player.</summary>
        private static readonly Dictionary<int, float> greetedPlayer = new Dictionary<int, float>();
        /// <summary>The world day each survivor last greeted you on a "meet", by player and person (PlayerKey + PersonKey).</summary>
        private static readonly Dictionary<string, int> metDay = new Dictionary<string, int>(StringComparer.Ordinal);
        /// <summary>When each survivor last greeted you (real time), by player and person (PlayerKey + PersonKey).</summary>
        private static readonly Dictionary<string, float> greetedAt = new Dictionary<string, float>(StringComparer.Ordinal);
        private static readonly System.Random rnd = new System.Random();
        private static readonly List<EntityAlive> pool = new List<EntityAlive>();
        private static int greetings, chatters;

        public static void Reset()
        {
            nextTick = 0f;
            nextChatter = -1f;
            lastSpoke = -999f;
            lastChatter = -999f;                                       // round MP4
            greetedPlayer.Clear();                                     // round MP4
            metDay.Clear();
            greetedAt.Clear();
#if SC_V34
            strangerGreets.Clear();                                    // round U3
#endif
            greetings = 0;
            chatters = 0;
        }

        public static void Tick()
        {
            if (!ScConfig.ScGreetings && !ScConfig.ScChatter) return;
            float now = Time.unscaledTime;
            if (now < nextTick) return;
            nextTick = now + Interval;
            try
            {
                if (!ScCommunity.IsHost || !ScRegistry.Loaded) return;
                World w = ScCommunity.World;
                if (w == null) return;
                // round MP4: every player on the server is greeted for themselves (a remote one on their own machine); the talk
                // gate is the host's own (a client drops a greeting that lands during its own talk: ScVoice.OnNetLine)
                if (ScConfig.ScGreetings)
                {
                    List<EntityPlayer> all = ScPlayers.All;
                    for (int i = 0; i < all.Count; i++)
                    {
                        EntityPlayer p = all[i];
                        if (!Listens(p) || (p is EntityPlayerLocal && ScVoice.TalkOpen)) continue;
                        TickGreetings(w, p, now);
                    }
                }
                // review m2: the host's own open talk keeps chatter away from the host only (was: from every player)
                if (ScConfig.ScChatter) TickChatter(w, now, ScVoice.TalkOpen);
            }
            catch (Exception e)
            {
                ScLog.Once("voice-greetings", e);
            }
        }

        /// <summary>Round MP4: a living player who hears SC's lines - the host's own, or a remote one who speaks SC's protocol.</summary>
        private static bool Listens(EntityPlayer p)
        {
            return p != null && !p.IsDead() && (p is EntityPlayerLocal || ScNet.ClientMatches(p));
        }

        /// <summary>Round MP4: a player's part of the greeting memory's keys (entity ids hold for the session the memory lives in).</summary>
        private static string PlayerKey(EntityPlayer p)
        {
            return p != null ? p.entityId + "/" : "";
        }

        // ------------------------------------------------------------------ greetings ----

        /// <summary>
        /// How this survivor stands with the player, as the greeting family's middle part: stranger, recruit, member,
        /// follower, enclave; round m1w: "away" for a member away from home (a runner, at an outpost, arriving), with
        /// ScGreetingsOwnAll (GreetKeys: the member lines that fit anywhere); null for nobody SC greets for (hostile, a
        /// stand-in, a missing enclave person; a member away without ScGreetingsOwnAll).
        /// </summary>
        public static string Relation(EntityAlive npc, EntityPlayer player)
        {
            if (npc == null || player == null) return null;
            // round m1w, re-check fix 1: a body out of sight - a member posted to an outpost or on a hidden run, parked at home
            // (SCore's onMission, as ScRuns.Park hides her) - greets no one, and Voice Control does not greet her either
            if (ScNpc.IsOnMission(npc) || !npc.enabled) return null;
            if (ScTrust.IsHostileTo(npc, player) || ScEnclavePeople.IsHostileBody(npc)) return null;
            if (npc.Buffs != null && npc.Buffs.HasCustomVar(ScRadio.StandInCvar)) return null;
            // round MP1: the real registry only - on a client (TriggerProactiveGreeting runs there) a copy's answer would change
            // which of Voice Control's greetings plays, which stays as before until the voices travel (round MP4).
            // Round MP4: the client's copy too (ScRegistry.Ready) - the server now greets that client by these same facts, so
            // the client's OwnsGreeting must answer from them (never "stranger" for a member)
            Member m = ScRegistry.Ready ? ScRegistry.GetMember(npc.entityId) : null;
            if (m != null)
            {
                if (m.Status == MemberStatus.Following || m.Status == MemberStatus.PendingStation) return "follower";
                if (m.Status == MemberStatus.Resident) return "member";
                // round m1w (P4): a runner on the road, one arriving - one of yours all the same, but away from home: none of the
                // home-only lines (GreetKeys). One posted to an outpost or on a hidden run is out of sight (above).
                return ScConfig.ScGreetingsOwnAll ? Away : null;
            }
            if (ScEnclavePeople.IsEnclaveNpc(npc))
            {
                if (ScEnclaveDeals.IsEnlistedBody(npc)) return "follower";
                // round MP4: a client has no enclave people (the copy leaves them out): one of theirs by the synced cvars - not
                // hostile (above: IsHostileBody), not enlisted - as the server sees them (a missing person's body is not out)
                if (!ScRegistry.Loaded) return "enclave";
                EncPerson p = ScEnclavePeople.PersonOf(npc);
                Enclave e = ScEnclavePeople.EnclaveOf(p);
                if (p == null || e == null || e.IsHostile || p.Missing) return null;
                return "enclave";
            }
            if (ScCommunity.HiredBy(npc) == player.entityId) return "follower";
            if (ScRegistry.Ready && ScRegistry.GetProspect(npc.entityId) != null) return "recruit";   // round MP1: as above (round MP4: the copy too)
            return "stranger";
        }

        /// <summary>
        /// Patches_VoiceControl's TriggerProactiveGreeting prefix: SC greets this survivor itself (they are one of the
        /// community's, SC greets for their relation and their voice has a clip of it), so Voice Control's greeting is skipped.
        /// Round m1w (P4): with ScGreetingsOwnAll, also the deliberate silences - someone SC greets for nobody (Relation null: a
        /// stand-in, someone hostile to you, a missing enclave person) and a member away with no line that fits away. Review fix
        /// 4: a greeting SC has but has no recording of is Voice Control's, as before.
        /// </summary>
        public static bool OwnsGreeting(EntityAlive npc, EntityPlayer player)
        {
            if (!ScConfig.ScGreetings || npc == null || !ScXnpc.IsSurvivorBody(npc)) return false;
            // round MP4: on a client SC greets only through the server - a server without SC (or another protocol) never does,
            // so Voice Control greets as before; with it, the server greets this player by the facts Relation reads here
            if (ScNet.IsClient && (!ScNet.ServerMatches || !ScRegistry.Ready)) return false;
            string rel = Relation(npc, player);
            if (ScConfig.ScGreetingsOwnAll)
            {
                // round m1w (P4): deliberate silence (with the switch off Relation is null for these too, and Voice Control greets)
                if (rel == null) return true;
                List<string> keys = GreetKeys(rel);
                if (keys.Count == 0) return true;              // a member away: no member line fits away from home
                return ScBarks.HasAnyClip(npc, keys);
            }
            return rel != null && ScBarks.HasAnyClip(npc, GreetKeys(rel));
        }

        /// <summary>Round m1w: Relation's middle part for a member away from home (ScGreetingsOwnAll).</summary>
        private const string Away = "away";

        /// <summary>
        /// Round m1w (the user's call on the m1w review): the member greetings that only make sense at home - "Good to see you
        /// back in one piece.", "Walls are holding. Mostly.", "There's coffee. Well, there's hot water.", "Welcome home,
        /// boss.", "Any luck out there?". A member away from home never says them; a new greet.member line is taken to fit
        /// anywhere until it is listed here.
        /// </summary>
        private static readonly HashSet<string> HomeOnly = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "greet.member.1", "greet.member.3", "greet.member.4", "greet.member.5", "greet.member.6"
        };

        /// <summary>The greeting family a relation draws from (a member away draws from the members').</summary>
        private static string GreetFamily(string rel)
        {
            return "greet." + (rel == Away ? "member" : rel);
        }

        /// <summary>
        /// The greeting lines a relation may say: its family's; round m1w: for a member away from home, the member lines that
        /// fit anywhere (HomeOnly left out) - empty when none is left, and then they say nothing.
        /// </summary>
        internal static List<string> GreetKeys(string rel)
        {
            List<string> all = ScBarks.KeysOf(GreetFamily(rel));
            if (rel != Away && !HasRetired(all)) return all;
            var keys = new List<string>();
            foreach (string k in all) if (!Retired.Contains(k) && (rel != Away || !HomeOnly.Contains(k))) keys.Add(k);
            return keys;
        }

        /// <summary>
        /// Round fx1 (2026-11-19, the user's call): greetings that stay recorded (in all 16 voices, so the clip check still
        /// passes) but are never said. "I keep hearing good things about your place." (greet.recruit.3): a prospect has no
        /// way to have heard of the place, and it played at any morale.
        /// </summary>
        private static readonly HashSet<string> Retired = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "greet.recruit.3"
        };

        private static bool HasRetired(List<string> keys)
        {
            for (int i = 0; i < keys.Count; i++) if (Retired.Contains(keys[i])) return true;
            return false;
        }

        private static void TickGreetings(World w, EntityPlayer lp, float now)     // round MP4: any player (was the host's own)
        {
            // round MP4: one greeting at a time per player, and the gap after a chatter line for everyone (the old shared
            // lastSpoke: in single player the later of the two is the same moment)
            float greeted;
            if (!greetedPlayer.TryGetValue(lp.entityId, out greeted)) greeted = -999f;
            if (now - Math.Max(greeted, lastChatter) < ScConfig.ScGreetGapSeconds) return;
            string pk = PlayerKey(lp);
            int day = ScCommunity.Today;
            float meet2 = ScConfig.ScGreetMeetRange * ScConfig.ScGreetMeetRange;
            float walk2 = ScConfig.ScGreetWalkUpRange * ScConfig.ScGreetWalkUpRange;
            EntityAlive best = null;
            string bestRel = null, bestHow = null;
            float bestSq = float.MaxValue;
            List<Entity> list = w.Entities.list;
            for (int i = 0; i < list.Count; i++)
            {
                EntityAlive npc = list[i] as EntityAliveSDXV4;
                if (npc == null) continue;
                float sq = (npc.position - lp.position).sqrMagnitude;
                if (sq > meet2 || npc.IsDead() || !ScXnpc.IsSurvivorBody(npc)) continue;
#if SC_V34
                if (ScDowned.IsDown(npc)) continue;     // round U2: no greeting from a downed member
#endif
                // the cheap tests first: a walk-up (close, looked at, not greeted lately) or a meet still due today
                float at;
                int d;
                string who = pk + PersonKey(npc);                    // round MP4: this player's memory
                bool walkUp = sq <= walk2 && (!greetedAt.TryGetValue(who, out at) || now - at >= ScConfig.ScGreetWalkUpSeconds) && LooksAt(lp, npc);
                bool meetDue = !walkUp && (!metDay.TryGetValue(who, out d) || d != day);
                if (!walkUp && !meetDue) continue;
                // a walk-up wins over a meet; then the nearest
                if (best != null && bestHow == "walk-up" && !walkUp) continue;
                if (best != null && (bestHow == "walk-up") == walkUp && sq >= bestSq) continue;
                string rel = Relation(npc, lp);
                if (rel == null || ScVoice.Holds(npc) || ScVoice.Busy(npc) || ScCommunity.LiveFoe(npc, FightRange) != null) continue;
#if SC_V34
                if (!walkUp && rel == "stranger" && StrangerGroupQuiet(npc, lp, now)) continue;   // round U3: one stranger greets a group
#endif
                if (!ScConfig.VoiceBarkDebug && !ScBarks.HasAnyClip(npc, GreetKeys(rel))) continue;
                if (!Sees(w, lp, npc)) continue;                      // the raycast last (a walk-up too: N4, not through a wall)
                string how = walkUp ? "walk-up" : "meet";
                best = npc;
                bestRel = rel;
                bestHow = how;
                bestSq = sq;
            }
            if (best == null) return;
            if (Greet(best, lp, bestRel, bestHow))
            {
                metDay[pk + PersonKey(best)] = day;                  // round MP4: this player's memory
#if SC_V34
                if (bestRel == "stranger" && bestHow == "meet") MetWithGroup(w, best, lp, day, now);   // round U3
#endif
            }
        }

#if SC_V34
        // ---- round U3: one stranger greets a group (Voice Control 3.4.06's "one greeting per group", for SC's own greetings) ----

        /// <summary>Round MP4: where and when a stranger last greeted a player on a "meet", and which player (entity id).</summary>
        private struct StrangerGreet
        {
            public Vector3 At;
            public float When;
            public int Player;
        }

        /// <summary>Where and when a stranger last greeted you on a "meet" (real time): the group's quiet window. Round MP4: per player.</summary>
        private static readonly List<StrangerGreet> strangerGreets = new List<StrangerGreet>();
        /// <summary>Voice Control's GreetGroupQuietSeconds: a stranger who comes along later greets once this has passed.</summary>
        private const float GroupQuietSeconds = 120f;

        /// <summary>
        /// A stranger's "meet" greeting is held while another stranger greeted you within ScGreetGroupRange of them in the last
        /// GroupQuietSeconds (ScGreetOnePerGroup). A walk-up greeting (you look at them up close) is never held. Round MP4: you -
        /// the player this greeting would be for.
        /// </summary>
        private static bool StrangerGroupQuiet(EntityAlive npc, EntityPlayer lp, float now)
        {
            if (!ScConfig.ScGreetOnePerGroup || strangerGreets.Count == 0) return false;
            float r2 = ScConfig.ScGreetGroupRange * ScConfig.ScGreetGroupRange;
            for (int i = strangerGreets.Count - 1; i >= 0; i--)
            {
                if (now - strangerGreets[i].When >= GroupQuietSeconds) { strangerGreets.RemoveAt(i); continue; }
                if (strangerGreets[i].Player == lp.entityId && (strangerGreets[i].At - npc.position).sqrMagnitude <= r2) return true;
            }
            return false;
        }

        /// <summary>
        /// A stranger greeted you on a "meet": every other stranger of the group (a survivor body within ScGreetGroupRange of
        /// them, a stranger to you) counts as met today too, so they do not greet you one after another. Round MP4: you - the
        /// player greeted (their own memory).
        /// </summary>
        private static void MetWithGroup(World w, EntityAlive greeter, EntityPlayer lp, int day, float now)
        {
            if (!ScConfig.ScGreetOnePerGroup) return;
            strangerGreets.Add(new StrangerGreet { At = greeter.position, When = now, Player = lp.entityId });
            float r2 = ScConfig.ScGreetGroupRange * ScConfig.ScGreetGroupRange;
            List<Entity> list = w.Entities.list;
            string pk = PlayerKey(lp);
            int n = 0;
            for (int i = 0; i < list.Count; i++)
            {
                EntityAlive e = list[i] as EntityAliveSDXV4;
                if (e == null || e == greeter || e.IsDead() || (e.position - greeter.position).sqrMagnitude > r2 || !ScXnpc.IsSurvivorBody(e)) continue;
                if (Relation(e, lp) != "stranger") continue;
                metDay[pk + PersonKey(e)] = day;
                n++;
            }
            if (n > 0) ScLog.Verbose("greetings: " + ScCommunity.DisplayName(greeter) + " greeted " + (lp is EntityPlayerLocal ? "you" : lp.PlayerDisplayName) + " for a group of " + (n + 1) + " strangers");
        }
#endif

        /// <summary>
        /// Who the greeting memory is about (the V2 review's N1): a member by name (a re-created member's body has a new
        /// entity id), one of an enclave's people by their person id, anyone else by entity id.
        /// </summary>
        private static string PersonKey(EntityAlive npc)
        {
            Member m = ScRegistry.GetMember(npc.entityId);
            if (m != null && !string.IsNullOrEmpty(m.Name)) return "m:" + m.Name;
            if (ScEnclavePeople.IsEnclaveNpc(npc))
            {
                EncPerson p = ScEnclavePeople.PersonOf(npc);
                if (p != null) return "enc:" + p.Id;
            }
            return "e:" + npc.entityId;
        }

        /// <summary>Round MP4: lp - the player greeted, any player; the line is said for them alone (a remote one's on their machine).</summary>
        private static bool Greet(EntityAlive npc, EntityPlayer lp, string rel, string how)
        {
            string why = how + " greeting (" + rel + ")" + (lp is EntityPlayerLocal ? "" : " to " + lp.PlayerDisplayName);
            bool said = ScBarks.SayFrom(npc, GreetFamily(rel), GreetKeys(rel), why, true, false, lp, lp);
            if (!said) return false;
            float now = Time.unscaledTime;
            greetedAt[PlayerKey(lp) + PersonKey(npc)] = now;             // round MP4: this player's memory
            greetedPlayer[lp.entityId] = now;
            lastSpoke = now;
            greetings++;
            return true;
        }

        /// <summary>The player looks at the survivor (about 25 degrees either way of their chest). Round MP4: any player (a remote one's look, as the server has it).</summary>
        private static bool LooksAt(EntityPlayer lp, EntityAlive npc)
        {
            Vector3 to = npc.getChestPosition() - lp.getHeadPosition();
            if (to.sqrMagnitude < 0.01f) return true;
            return Vector3.Dot(lp.GetLookVector().normalized, to.normalized) >= LookDot;
        }

        /// <summary>A clear line between the player's head and the survivor's: no block in the way.</summary>
        private static bool Sees(World w, EntityPlayer lp, EntityAlive npc)
        {
            Vector3 from = lp.getHeadPosition(), to = npc.getHeadPosition();
            Vector3 d = to - from;
            float len = d.magnitude;
            if (len < 1f) return true;
            d /= len;
            // start and stop short of both heads, so neither body is the hit
            var ray = new Ray(from + d * 0.4f, d);
            return !Voxel.Raycast(w, ray, len - 0.8f, false, false);
        }

        // ------------------------------------------------------------------ chatter ----

        private static void TickChatter(World w, float now, bool hostTalk)   // round MP4: near any player (was the host's own)
        {
            // round MP4: nobody alive to hear it (the old early return). Review m2: hostTalk - the host's own talk is open: only
            // remote players count, and nobody near the host speaks (single player: no listener, so no tick, as before)
            if (!AnyListener(hostTalk)) return;
            if (nextChatter < 0f)
            {
                nextChatter = now + ScConfig.ScChatterSeconds * (0.5f + (float)rnd.NextDouble() * 0.5f);   // not right at load
                return;
            }
            if (now < nextChatter) return;
            nextChatter = now + ScConfig.ScChatterSeconds * (0.75f + (float)rnd.NextDouble() * 0.5f);
            if (rnd.NextDouble() > ScConfig.ScChatterChance) return;
            if (now - lastSpoke < Math.Max(4f, ScConfig.ScGreetGapSeconds)) return;   // one speaker at a time
            Candidates(w, null, hostTalk);
            if (pool.Count == 0) return;
            EntityAlive npc = pool[rnd.Next(pool.Count)];
            Chatter(npc);
        }

        /// <summary>Round MP4: a living player who hears SC's lines is on the server (the host's own, or a remote one with SC).</summary>
        private static bool AnyListener(bool remoteOnly)
        {
            List<EntityPlayer> all = ScPlayers.All;
            for (int i = 0; i < all.Count; i++) if (Listens(all[i]) && !(remoteOnly && all[i] is EntityPlayerLocal)) return true;
            return false;
        }

        /// <summary>Round MP4: within ScChatterRange (squared: r2) of near, or with near null of any living player who hears SC's lines.</summary>
        private static bool Near(Vector3 pos, EntityPlayer near, float r2, bool hostTalk = false)
        {
            if (near != null) return (pos - near.position).sqrMagnitude <= r2;
            List<EntityPlayer> all = ScPlayers.All;
            bool any = false;
            for (int i = 0; i < all.Count; i++)
            {
                EntityPlayer p = all[i];
                if (!Listens(p) || (pos - p.position).sqrMagnitude > r2) continue;
                if (hostTalk && p is EntityPlayerLocal) return false;     // review m2: not within earshot of the host's open talk
                any = true;
            }
            return any;
        }

        /// <summary>
        /// Residents at home within ScChatterRange of the player who could say a chatter line now. Round MP4: lp null - of any
        /// living player (the line is heard by everyone in earshot); else of that one (the console's sender).
        /// </summary>
        private static void Candidates(World w, EntityPlayer lp, bool hostTalk = false)
        {
            pool.Clear();
            float r2 = ScConfig.ScChatterRange * ScConfig.ScChatterRange;
            foreach (Member m in ScRegistry.Members.Values)
            {
                if (m.Status != MemberStatus.Resident) continue;
                EntityAlive npc = ScCommunity.FindNpc(m.Id);
                if (npc == null || npc.IsDead() || !Near(npc.position, lp, r2, hostTalk) || !ScXnpc.IsSurvivorBody(npc)) continue;
#if SC_V34
                if (ScDowned.IsDown(npc)) continue;     // round U2: no chatter from a downed member
#endif
                if (ScVoice.Holds(npc) || ScVoice.Busy(npc) || ScCommunity.LiveFoe(npc, FightRange) != null) continue;
#if SC_V34
                if (ScSocial.InExchange(npc)) continue;                 // round U3: talking with a neighbor, not to nobody
#endif
                if (!ScConfig.VoiceBarkDebug && !ScBarks.HasClip(npc, "chatter")) continue;
                pool.Add(npc);
            }
        }

        private static bool Chatter(EntityAlive npc)
        {
            bool said = ScBarks.Say(npc, "chatter", "chatter at home", true, false, null);   // round MP4: heard by everyone in earshot
            if (!said) return false;
            lastSpoke = Time.unscaledTime;
            lastChatter = lastSpoke;                                   // round MP4: every player's greetings keep their gap after it
            chatters++;
            return true;
        }

        // ------------------------------------------------------------------ console ----

        /// <summary>
        /// sc voice greet: this survivor greets the player now (no range, gap or cooldown); what was said, or why not. Round MP4:
        /// lp - the console's sender, any player (a remote one hears it on their own machine).
        /// </summary>
        public static string GreetNow(EntityAlive npc, EntityPlayer lp)
        {
            if (npc == null || lp == null) return "no survivor";
            string who = ScCommunity.DisplayName(npc) + " (" + npc.entityId + ")";
            if (!ScXnpc.IsSurvivorBody(npc)) return who + " is not one of the community's survivors - SC greets only those";
            string rel = Relation(npc, lp);
            if (rel == null) return who + " is no one SC greets for (out of sight on a posting or a hidden run, hostile to you, an outpost stand-in, a missing enclave person"
                                    + (ScConfig.ScGreetingsOwnAll ? ") - they say nothing: SC owns their greeting (ScGreetingsOwnAll)" : ", or a member away) - Voice Control's greeting stays theirs");
            ScBarks.Pick p = ScBarks.Resolve(npc, GreetKeys(rel));
            string what = who + " (" + rel + ")";
            if (p == null) return what + (rel == Away ? ": no member greeting that fits away from home - silent" : " has no voice");
            if (p.Path == null && !ScConfig.VoiceBarkDebug)
                return what + ": " + GreetFamily(rel) + " has no recording in " + (p.Voice ?? "any voice")
                       + " - silent, and Voice Control greets them as before (VoiceBarkDebug shows a silent one)";
            Greet(npc, lp, rel, "console");
            return what + " says " + p.Key + (p.Path != null ? " in " + p.Voice : " (silent: no recording, VoiceBarkDebug)") + ": \"" + p.Words + "\"";
        }

        /// <summary>
        /// sc voice chatter: this member (or one at home near you) says a chatter line now. Round MP4: near - the console's
        /// sender (the host's own player when none is given); the line is heard by everyone in earshot.
        /// </summary>
        public static string ChatterNow(EntityAlive npc, EntityPlayer near = null)
        {
            if (npc == null)
            {
                World w = ScCommunity.World;
                EntityPlayer lp = near ?? ScCommunity.LocalPlayer;
                if (w == null || lp == null) return "no world";
                Candidates(w, lp);
                if (pool.Count == 0) return "no resident at home within " + ScConfig.ScChatterRange + " m who could chat now (fighting, talking, or no chatter recording"
                                            + (ScConfig.VoiceBarkDebug ? "" : "; VoiceBarkDebug lets silent ones through") + ")";
                npc = pool[rnd.Next(pool.Count)];
            }
            ScBarks.Pick p = ScBarks.Resolve(npc, ScBarks.KeysOf("chatter"));
            string who = ScCommunity.DisplayName(npc) + " (" + npc.entityId + ")";
            if (p == null) return who + " has no voice (not one of the community's survivors)";
            if (!Chatter(npc)) return who + ": no chatter recording in " + (p.Voice ?? "any voice") + " - silent (VoiceBarkDebug shows a silent one)";
            return who + " says " + p.Key + (p.Path != null ? " in " + p.Voice : " (silent: no recording)") + ": \"" + p.Words + "\"";
        }

        /// <summary>sc voice barks: the greeting and chatter counts this session.</summary>
        public static string Status()
        {
            return "greetings " + (ScConfig.ScGreetings ? "on" : "off") + " (" + greetings + " this session, " + metDay.Count + " met), chatter "
                   + (ScConfig.ScChatter ? "on" : "off") + " (" + chatters + " this session"
                   + (nextChatter > 0f ? ", next try in " + Mathf.Max(0f, nextChatter - Time.unscaledTime).ToString("0") + " s" : "") + ")";
        }
    }
}
