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
    ///  - "Walk-up": within ScGreetWalkUpRange (3 m) while you look at them, at most every ScGreetWalkUpSeconds (120 s).
    ///  - Never while they or you are in a fight (a live foe of theirs within 30 m), while a talk is open, while they are
    ///    already saying something; one greeting at a time, ScGreetGapSeconds (6 s) apart, the nearest first.
    ///  - Only where their voice has a clip of that greeting (VoiceBarkDebug: a silent one is shown and logged). The
    ///    survivor turns to you (Voice Control's FaceEntity).
    /// Where SC greets, Voice Control doesn't: Patches_VoiceControl's prefix on NPCChatComponent.TriggerProactiveGreeting
    /// asks OwnsGreeting - one of the community's survivors whose voice has a clip of their greeting - and skips Voice
    /// Control's greeting (canned or LLM) for them. Without a clip Voice Control greets them as before.
    ///
    /// Chatter (ScChatter): every ScChatterSeconds (90 s, a quarter either way), with ScChatterChance (0.5), one resident at
    /// home within ScChatterRange (15 m) of you, not fighting, not talking, says a chatter line - only one with a clip.
    /// </summary>
    public static class ScGreetings
    {
        private const float Interval = 0.5f;
        private const float FightRange = 30f;
        /// <summary>Looking at a survivor: within about 25 degrees of the look direction.</summary>
        private const float LookDot = 0.9f;

        private static float nextTick, nextChatter = -1f, lastSpoke = -999f;
        /// <summary>The world day each survivor last greeted you on a "meet", by entity id.</summary>
        private static readonly Dictionary<int, int> metDay = new Dictionary<int, int>();
        /// <summary>When each survivor last greeted you (real time), by entity id.</summary>
        private static readonly Dictionary<int, float> greetedAt = new Dictionary<int, float>();
        private static readonly System.Random rnd = new System.Random();
        private static readonly List<EntityAlive> pool = new List<EntityAlive>();
        private static int greetings, chatters;

        public static void Reset()
        {
            nextTick = 0f;
            nextChatter = -1f;
            lastSpoke = -999f;
            metDay.Clear();
            greetedAt.Clear();
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
                EntityPlayerLocal lp = ScCommunity.LocalPlayer;
                if (w == null || lp == null || lp.IsDead() || ScVoice.TalkOpen) return;
                if (ScConfig.ScGreetings) TickGreetings(w, lp, now);
                if (ScConfig.ScChatter) TickChatter(w, lp, now);
            }
            catch (Exception e)
            {
                ScLog.Once("voice-greetings", e);
            }
        }

        // ------------------------------------------------------------------ greetings ----

        /// <summary>
        /// How this survivor stands with the player, as the greeting family's middle part: stranger, recruit, member,
        /// follower, enclave; null for nobody SC greets for (hostile, a stand-in, a runner, a missing enclave person).
        /// </summary>
        public static string Relation(EntityAlive npc, EntityPlayer player)
        {
            if (npc == null || player == null) return null;
            if (ScTrust.IsHostileTo(npc, player) || ScEnclavePeople.IsHostileBody(npc)) return null;
            if (npc.Buffs != null && npc.Buffs.HasCustomVar(ScRadio.StandInCvar)) return null;
            Member m = ScRegistry.GetMember(npc.entityId);
            if (m != null)
            {
                if (m.Status == MemberStatus.Following || m.Status == MemberStatus.PendingStation) return "follower";
                return m.Status == MemberStatus.Resident ? "member" : null;
            }
            if (ScEnclavePeople.IsEnclaveNpc(npc))
            {
                if (ScEnclaveDeals.IsEnlistedBody(npc)) return "follower";
                EncPerson p = ScEnclavePeople.PersonOf(npc);
                Enclave e = ScEnclavePeople.EnclaveOf(p);
                if (p == null || e == null || e.IsHostile || p.Missing) return null;
                return "enclave";
            }
            if (ScCommunity.HiredBy(npc) == player.entityId) return "follower";
            if (ScRegistry.GetProspect(npc.entityId) != null) return "recruit";
            return "stranger";
        }

        /// <summary>
        /// Patches_VoiceControl's TriggerProactiveGreeting prefix: SC greets this survivor itself (they are one of the
        /// community's, SC greets for their relation and their voice has a clip of it), so Voice Control's greeting is skipped.
        /// </summary>
        public static bool OwnsGreeting(EntityAlive npc, EntityPlayer player)
        {
            if (!ScConfig.ScGreetings || npc == null || !ScXnpc.IsSurvivorBody(npc)) return false;
            string rel = Relation(npc, player);
            return rel != null && ScBarks.HasClip(npc, "greet." + rel);
        }

        private static void TickGreetings(World w, EntityPlayerLocal lp, float now)
        {
            if (now - lastSpoke < ScConfig.ScGreetGapSeconds) return;
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
                // the cheap tests first: a walk-up (close, looked at, not greeted lately) or a meet still due today
                float at;
                int d;
                bool walkUp = sq <= walk2 && (!greetedAt.TryGetValue(npc.entityId, out at) || now - at >= ScConfig.ScGreetWalkUpSeconds) && LooksAt(lp, npc);
                bool meetDue = !walkUp && (!metDay.TryGetValue(npc.entityId, out d) || d != day);
                if (!walkUp && !meetDue) continue;
                // a walk-up wins over a meet; then the nearest
                if (best != null && bestHow == "walk-up" && !walkUp) continue;
                if (best != null && (bestHow == "walk-up") == walkUp && sq >= bestSq) continue;
                string rel = Relation(npc, lp);
                if (rel == null || ScVoice.Holds(npc) || ScVoice.Busy(npc) || ScCommunity.LiveFoe(npc, FightRange) != null) continue;
                if (!ScConfig.VoiceBarkDebug && !ScBarks.HasClip(npc, "greet." + rel)) continue;
                if (!walkUp && !Sees(w, lp, npc)) continue;          // the raycast last
                string how = walkUp ? "walk-up" : "meet";
                best = npc;
                bestRel = rel;
                bestHow = how;
                bestSq = sq;
            }
            if (best == null) return;
            if (Greet(best, lp, bestRel, bestHow)) metDay[best.entityId] = day;
        }

        private static bool Greet(EntityAlive npc, EntityPlayerLocal lp, string rel, string how)
        {
            bool said = ScBarks.Say(npc, "greet." + rel, how + " greeting (" + rel + ")", true, false, lp);
            if (!said) return false;
            float now = Time.unscaledTime;
            greetedAt[npc.entityId] = now;
            lastSpoke = now;
            greetings++;
            return true;
        }

        /// <summary>The player looks at the survivor (about 25 degrees either way of their chest).</summary>
        private static bool LooksAt(EntityPlayerLocal lp, EntityAlive npc)
        {
            Vector3 to = npc.getChestPosition() - lp.getHeadPosition();
            if (to.sqrMagnitude < 0.01f) return true;
            return Vector3.Dot(lp.GetLookVector().normalized, to.normalized) >= LookDot;
        }

        /// <summary>A clear line between the player's head and the survivor's: no block in the way.</summary>
        private static bool Sees(World w, EntityPlayerLocal lp, EntityAlive npc)
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

        private static void TickChatter(World w, EntityPlayerLocal lp, float now)
        {
            if (nextChatter < 0f)
            {
                nextChatter = now + ScConfig.ScChatterSeconds * (0.5f + (float)rnd.NextDouble() * 0.5f);   // not right at load
                return;
            }
            if (now < nextChatter) return;
            nextChatter = now + ScConfig.ScChatterSeconds * (0.75f + (float)rnd.NextDouble() * 0.5f);
            if (rnd.NextDouble() > ScConfig.ScChatterChance) return;
            if (now - lastSpoke < Math.Max(4f, ScConfig.ScGreetGapSeconds)) return;   // one speaker at a time
            Candidates(w, lp);
            if (pool.Count == 0) return;
            EntityAlive npc = pool[rnd.Next(pool.Count)];
            Chatter(npc);
        }

        /// <summary>Residents at home within ScChatterRange of the player who could say a chatter line now.</summary>
        private static void Candidates(World w, EntityPlayerLocal lp)
        {
            pool.Clear();
            float r2 = ScConfig.ScChatterRange * ScConfig.ScChatterRange;
            foreach (Member m in ScRegistry.Members.Values)
            {
                if (m.Status != MemberStatus.Resident) continue;
                EntityAlive npc = ScCommunity.FindNpc(m.Id);
                if (npc == null || npc.IsDead() || (npc.position - lp.position).sqrMagnitude > r2 || !ScXnpc.IsSurvivorBody(npc)) continue;
                if (ScVoice.Holds(npc) || ScVoice.Busy(npc) || ScCommunity.LiveFoe(npc, FightRange) != null) continue;
                if (!ScConfig.VoiceBarkDebug && !ScBarks.HasClip(npc, "chatter")) continue;
                pool.Add(npc);
            }
        }

        private static bool Chatter(EntityAlive npc)
        {
            bool said = ScBarks.Say(npc, "chatter", "chatter at home", true, false, null);
            if (!said) return false;
            lastSpoke = Time.unscaledTime;
            chatters++;
            return true;
        }

        // ------------------------------------------------------------------ console ----

        /// <summary>sc voice greet: this survivor greets the player now (no range, gap or cooldown); what was said, or why not.</summary>
        public static string GreetNow(EntityAlive npc, EntityPlayerLocal lp)
        {
            if (npc == null || lp == null) return "no survivor";
            string who = ScCommunity.DisplayName(npc) + " (" + npc.entityId + ")";
            if (!ScXnpc.IsSurvivorBody(npc)) return who + " is not one of the community's survivors - SC greets only those";
            string rel = Relation(npc, lp);
            if (rel == null) return who + " is no one SC greets for (hostile to you, an outpost stand-in, a runner or a missing enclave person) - Voice Control's greeting stays theirs";
            ScBarks.Pick p = ScBarks.Resolve(npc, ScBarks.KeysOf("greet." + rel));
            string what = who + " (" + rel + ")";
            if (p == null) return what + " has no voice";
            if (p.Path == null && !ScConfig.VoiceBarkDebug)
                return what + ": greet." + rel + " has no recording in " + (p.Voice ?? "any voice") + " - silent, and Voice Control greets them as before (VoiceBarkDebug shows a silent one)";
            Greet(npc, lp, rel, "console");
            return what + " says " + p.Key + (p.Path != null ? " in " + p.Voice : " (silent: no recording, VoiceBarkDebug)") + ": \"" + p.Words + "\"";
        }

        /// <summary>sc voice chatter: this member (or one at home near you) says a chatter line now.</summary>
        public static string ChatterNow(EntityAlive npc)
        {
            if (npc == null)
            {
                World w = ScCommunity.World;
                EntityPlayerLocal lp = ScCommunity.LocalPlayer;
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
