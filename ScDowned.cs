using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round U2 (2026-11-16, research/npc306/part_downed.md, FEATURES_306.md 3.2; the user's decisions D4, D5 and D6): 0-XNPCCore
    /// 3.4.06's downed state on the community's own people. A killing hit knocks her down instead of killing her; she lies
    /// invulnerable and ignored by the AI with 0-XNPCCore's red marker and countdown (SurviveSeconds, 60), and anyone revives her
    /// with E or a revive item (a first aid bandage or kit). If nobody does, she bleeds out and dies for real - and only then does
    /// SC record a death (its SetDead postfix: no SetDead and no EntityKilled at the knockdown, part_downed 2.2 and 4.1).
    ///
    /// Who goes down: 0-XNPCCore's own rule (a Leader or Owner cvar: a follower, a resident on a work shift or in a talk, D4),
    /// and since this round every resident at home and every guard on watch (D5, Patches_Downed: DownedAtHome). Runners on a run,
    /// raiders, bands and enclave people die as before.
    ///
    /// What this file does, host only, once a second from ScTicker (the ONLY writer of the state below, apart from the revive
    /// task's write-back NoteFight, as UAITaskRetreatSC writes back to ScRetreat):
    ///  - Tracks who is down (NPCCore's own query, SdcsDowned.IsDowned), and tells the player when she is back up (D6). The "is
    ///    down" line is sent at the knockdown itself (Patches_Downed's TellLeader swap, TellDown); her death is ScCommunity's
    ///    (DeathKey: "bled out" when she was down).
    ///  - Resident medics revive (D5): a member with the Medicine specialty at home, free, not badly hurt herself and carrying a
    ///    revive item, goes to a downed member at home - the nearest free one, one per downed member - through SCResident's
    ///    ScRevive (8.25, UaiDowned.cs). In reach she is handed to 0-XNPCCore's own medic sequence (SdcsMedic.Begin: she faces
    ///    her, bandages, the item leaves her pack and SdcsDowned.Revive runs - its grace, bleed cure, get-up clip and record).
    ///    On the way she fights back at what is after her within 2.5 m, then goes on. She never leaves a shelter (ScRetreat owns
    ///    her: hurt with nothing to heal with, or Cowardly) and never goes below ReviveMedicMinHealth: "a medic who is herself
    ///    badly hurt doesn't go". No medic, or none who can: nobody revives, and the player is told why.
    ///  - Guards every SC rule that would undo the downed state (Defers*, the one-line checks in ScTicker, ScWork, ScCommunity,
    ///    ScTeam, ScInvestigate, ScSleep, ScBarks ...): a downed member's shift or talk link is held until she is up or dead (D4),
    ///    she is never stationed, sent home, sent out, asked to leave or teleported, counts 0 in team strength, and says nothing.
    /// </summary>
    public static class ScDowned
    {
        /// <summary>0-XNPCCore's cvar behind SdcsDowned.IsDowned (the fallback read when its API is not there).</summary>
        public const string DownedCvar = "SdcsDowned";
        /// <summary>0-XNPCCore's busy flag (XNPCCoreBridge.SdcsBusyCVar): its SdcsAnimBusy action (12) holds her while it is set.</summary>
        private const string BusyCvar = "SdcsAnimBusy";
        /// <summary>A medic fights back at an enemy after her within this many meters, for FightSeconds, then goes on.</summary>
        internal const float FightBackRange = 2.5f, FightSeconds = 2.5f;
        /// <summary>The medic sequence starts within this distance (0-XNPCCore's ApplyHealOther accepts up to 4 m).</summary>
        private const float TreatXZ = 2.2f, TreatDy = 1.6f;
        /// <summary>A treatment that did not take (the sequence ended, she is still down) is tried this many times by one medic.</summary>
        private const int MaxBegins = 3;
        /// <summary>A medic who is busy (another hand sequence, a medical use of her own) or refused this many seconds in reach is let go.</summary>
        private const float MaxWaitInReach = 15f;
        /// <summary>One medic's whole try at one patient (a bleed-out paused for good must not hold her forever).</summary>
        private const float JobMaxSeconds = 120f;
        /// <summary>A revive this soon after her medic's treatment started is credited to the medic (D6's "got her back on her feet").</summary>
        private const float CreditSeconds = 15f;

        private enum Why { None = 0, NoMedic, NoItem, Hurt, Busy, Unreachable }

        private sealed class Down
        {
            public int Id;
            public string Name = "";
            public EntityAlive Body;
            public float Since, Seen, Bleed;
            public bool Home, Rising, StationLogged, ToldNoMedic, BusyLogged, LeaveTold;
            public int MedicId = -1;
            public string MedicName = "";
            public float MedicSince, GainAt, Best, InReachSince, BeganAt;
            public bool Treating;
            public int Begins;
            public string Item = "";
            public string LastMedic = "";
            public float LastBeganAt = -1000f;
            public readonly HashSet<int> Failed = new HashSet<int>();
        }

        private static readonly Dictionary<int, Down> downs = new Dictionary<int, Down>();
        /// <summary>Medic body id -> the patient's member id.</summary>
        private static readonly Dictionary<int, int> medicOf = new Dictionary<int, int>();
        /// <summary>Medic body id -> Time.time until which she fights back (the revive task's write-back).</summary>
        private static readonly Dictionary<int, float> fightUntil = new Dictionary<int, float>();
        private static readonly List<int> scratch = new List<int>();
        private static readonly List<Down> order = new List<Down>();
        private static readonly List<Entity> near = new List<Entity>();
        private static bool apiOk = true;
        private static int seenDowns, revived, revivedByMedic, bledOut, unloaded, medicsSent, medicsLetGo, treatments, noMedic,
                           stationsDeferred, shiftsHeld, verbsRefused, fights, busyRestored, zeroHealthFixed;

        public static void Reset()
        {
            downs.Clear();
            medicOf.Clear();
            fightUntil.Clear();
            scratch.Clear();
            order.Clear();
            near.Clear();
            seenDowns = revived = revivedByMedic = bledOut = unloaded = medicsSent = medicsLetGo = treatments = noMedic = 0;
            stationsDeferred = shiftsHeld = verbsRefused = fights = busyRestored = zeroHealthFixed = 0;
        }

        // ------------------------------------------------------------------ reads (any caller, pure) ----

        /// <summary>Is she down? 0-XNPCCore's own query (SdcsDowned.IsDowned); its cvar when that API is not there (before 3.4.06).</summary>
        public static bool IsDown(EntityAlive e)
        {
            if (e == null || e.Buffs == null) return false;
            if (apiOk)
            {
                try { return Api.IsDowned(e); }
                catch (Exception ex)
                {
                    apiOk = false;
                    ScLog.Warn("downed: 0-XNPCCore's SdcsDowned.IsDowned is not there (" + ex.GetType().Name + ") - its cvar is read instead (0-XNPCCore before 3.4.06?)");
                }
            }
            return e.Buffs.GetCustomVar(DownedCvar) != 0f;
        }

        /// <summary>This member's body is loaded and down (the verbs' refusal: scDownedBusy).</summary>
        public static bool MemberDown(Member m)
        {
            if (m == null) return false;
            EntityAlive e = ScCommunity.FindNpc(m.Id);
            return e != null && !e.IsDead() && IsDown(e);
        }

        /// <summary>A verb refused on a downed member (counted for sc downed). True: refuse it.</summary>
        public static bool RefuseVerb(Member m)
        {
            if (!MemberDown(m)) return false;
            verbsRefused++;
            return true;
        }

        /// <summary>
        /// ScCommunity.Leave's first line: a downed member does not leave (Leave detaches her - with no leader 0-XNPCCore would stop
        /// her bleed-out, an invulnerable stranger). Told once per down (a caller that asks every second says it once).
        /// </summary>
        public static bool RefuseLeave(Member m, string reasonKey)
        {
            if (!MemberDown(m)) return false;
            verbsRefused++;
            Down d;
            if (downs.TryGetValue(m.Id, out d))
            {
                if (d.LeaveTold) return true;
                d.LeaveTold = true;
            }
            EntityPlayerLocal p = ScCommunity.LocalPlayer;
            if (p != null) ScCommunity.Tell(p, string.Format(Localization.Get("scDownedNoLeave"), m.Name), "ui_denied");
            ScLog.Out(m.Name + " is down - not leaving now (" + (reasonKey ?? "-") + "); asked again once she is up");
            return true;
        }

        /// <summary>The community member this body is, checked against a reused id (her birth time). Null for anyone else.</summary>
        internal static Member MemberOfBody(EntityAlive npc)
        {
            if (npc == null || npc.Buffs == null || !ScRegistry.Loaded) return null;
            if (npc.Buffs.GetCustomVar("sc_member") <= 0f) return null;
            Member m = ScRegistry.GetMember(npc.entityId);
            if (m == null) return null;
            if (m.Born != 0 && npc.WorldTimeBorn != 0 && npc.WorldTimeBorn != m.Born) return null;
            return m;
        }

        /// <summary>
        /// D5 (Patches_Downed's eligibility swap): a resident at home or a guard on watch goes down at a killing hit. Runners
        /// (OnRun), posted members (hidden), the arriving and anyone else are left to 0-XNPCCore's own rule (they die).
        /// </summary>
        internal static bool HomeDownable(EntityAlive npc)
        {
            if (!ScConfig.DownedAtHome || !ScCommunity.IsHost) return false;
            Member m = MemberOfBody(npc);
            if (m == null || m.Status != MemberStatus.Resident) return false;
            return !ScNpc.IsOnMission(npc);
        }

        /// <summary>
        /// Patches_Downed's bleed swap: her bleed-out counts while you are alive, though 0-XNPCCore finds no leader for her (at
        /// home; or a link that was lost). Risk 1 of the research: without this a downed member with no link lies invulnerable for good.
        /// </summary>
        internal static bool BleedsWithoutLeader(EntityAlive npc)
        {
            Member m = MemberOfBody(npc);
            if (m == null || m.Status == MemberStatus.OnRun || m.Status == MemberStatus.Garrison || m.Status == MemberStatus.Arriving) return false;
            EntityPlayerLocal p = ScCommunity.LocalPlayer;
            return p != null && !p.IsDead();
        }

        /// <summary>A medic on her way to (or treating) a downed member: ScInvestigate and ScSleep leave her be.</summary>
        public static bool IsReviving(int entityId) { return medicOf.ContainsKey(entityId); }

        /// <summary>
        /// The revive action's consideration (ScReviving, a pure read): she is a medic sent to a downed member who is still down,
        /// the treatment has not started, and she is not fighting back.
        /// </summary>
        public static bool Walking(EntityAlive self)
        {
            EntityAlive p;
            if (!WalkTarget(self, out p)) return false;
            float until;
            return !fightUntil.TryGetValue(self.entityId, out until) || Time.time >= until;
        }

        /// <summary>Her patient while she is sent and the treatment has not started (the task's target; fighting back not counted).</summary>
        internal static bool WalkTarget(EntityAlive self, out EntityAlive patient)
        {
            patient = null;
            if (self == null) return false;
            int pid;
            Down d;
            if (!medicOf.TryGetValue(self.entityId, out pid) || !downs.TryGetValue(pid, out d)) return false;
            if (d.MedicId != self.entityId || d.Rising || d.Treating || d.Body == null || d.Body.IsDead() || !IsDown(d.Body)) return false;
            patient = d.Body;
            return true;
        }

        /// <summary>The revive task's write-back (as UAITaskRetreatSC writes to ScRetreat): she fights back for FightSeconds.</summary>
        internal static void NoteFight(EntityAlive self, EntityAlive foe)
        {
            if (self == null) return;
            fightUntil[self.entityId] = Time.time + FightSeconds;
            fights++;
            ScLog.Verbose(ScCommunity.DisplayName(self) + " (" + self.entityId + ") fights back at " + (foe != null ? ScCommunity.ClassName(foe) + " (" + foe.entityId + ")" : "?")
                          + " on the way to a downed member, then goes on");
        }

        /// <summary>An awake enemy after her (its attack or revenge target) within FightBackRange - the task's scan, 4 times a second.</summary>
        internal static EntityAlive FoeOnMe(EntityAlive self)
        {
            World w = self != null ? self.world : null;
            if (w == null) return null;
            near.Clear();
            try
            {
                float r = FightBackRange;
                w.GetEntitiesInBounds(typeof(EntityAlive), new Bounds(self.position, new Vector3(2f * r, 3f, 2f * r)), near);
                for (int i = 0; i < near.Count; i++)
                {
                    EntityAlive a = near[i] as EntityAlive;
                    if (a == null || a == self || a.IsDead() || a.IsSleeping) continue;
                    if ((a.position - self.position).sqrMagnitude > r * r) continue;
                    if (a.GetAttackTarget() != self && a.GetRevengeTarget() != self) continue;
                    if (!EntityTargetingUtilities.IsEnemy(self, a)) continue;
                    return a;
                }
                return null;
            }
            finally { near.Clear(); }
        }

        // ------------------------------------------------------------------ the guards' hooks ----

        /// <summary>
        /// Risks 1 and 2 (ScCommunity.Station's first line): never on a downed body. Station drops Leader and Owner (her bleed
        /// would stop for good under 0-XNPCCore's own rule), ends her shift and may teleport her ragdoll to her post. It happens
        /// once she is up (the ticker's resident rule, or Go home again), or never (she bled out). True: Station returns.
        /// </summary>
        public static bool DefersStation(Member m, EntityAlive npc)
        {
            if (m == null || npc == null || npc.IsDead() || !IsDown(npc)) return false;
            stationsDeferred++;
            Down d;
            if (downs.TryGetValue(m.Id, out d))
            {
                if (d.StationLogged) return true;
                d.StationLogged = true;
            }
            ScLog.Out(m.Name + " is down - not stationed now (her link and her bleed-out stay as they are; she goes home once she is up)");
            return true;
        }

        /// <summary>D4 (ScWork.Tick's and End's first line): her shift or talk link is held while she is down.</summary>
        public static bool HoldsShift(Member m, EntityAlive npc)
        {
            if (npc == null || npc.IsDead() || !IsDown(npc)) return false;
            shiftsHeld++;
            return true;
        }

        /// <summary>ScCommunity.OnMemberDied: "bled out" for a member who was down (D6), else the usual "has died". Forgets her.</summary>
        public static string DeathKey(Member m)
        {
            if (m == null) return "scDied";
            Down d;
            if (!downs.TryGetValue(m.Id, out d)) return "scDied";
            LetGo(d, "her patient died", false);
            downs.Remove(m.Id);
            bledOut++;
            ScLog.Out(m.Name + " bled out after " + Secs(Time.time - d.Since) + " down - the death is recorded now (not at the knockdown)");
            return ScConfig.DownedTell ? "scDownedBledOut" : "scDied";
        }

        // ------------------------------------------------------------------ tells (D6) ----

        /// <summary>The "is down" line, at the knockdown (Patches_Downed's TellLeader swap in Enter): SC's in place of 0-XNPCCore's.</summary>
        internal static void TellDown(Member m, EntityAlive npc, int seconds)
        {
            EntityPlayerLocal p = ScCommunity.LocalPlayer;
            if (p == null || m == null) return;
            ScCommunity.Tell(p, string.Format(Localization.Get("scDownedIsDown"), m.Name, seconds), "ui_denied");
        }

        /// <summary>0-XNPCCore's own warning text ("will bleed out in 30 s") to the player, for one with no leader to tell (at home).</summary>
        internal static void TellPlayerNpcCore(EntityAlive npc, string key, int seconds)
        {
            EntityPlayerLocal p = ScCommunity.LocalPlayer;
            if (p == null || npc == null) return;
            ScCommunity.Tell(p, string.Format(Localization.Get(key), Localization.Get(npc.EntityName), seconds), "");
        }

        private static void Tell(string key, params object[] args)
        {
            if (!ScConfig.DownedTell) return;
            EntityPlayerLocal p = ScCommunity.LocalPlayer;
            if (p != null) ScCommunity.Tell(p, string.Format(Localization.Get(key), args), "");
        }

        // ------------------------------------------------------------------ the ticker ----

        /// <summary>Once a second from ScTicker, right after Relink (so ScSleep and ScInvestigate see this second's medics).</summary>
        public static void Tick(World w, EntityPlayerLocal player)
        {
            if (w == null || !ScCommunity.IsHost || !ScRegistry.Loaded) return;
            try
            {
                float now = Time.time;
                // the records whose member is gone (left, dropped as a reused id; a death was DeathKey's)
                scratch.Clear();
                foreach (var kv in downs) if (ScRegistry.GetMember(kv.Key) == null) scratch.Add(kv.Key);
                for (int i = 0; i < scratch.Count; i++) { LetGo(downs[scratch[i]], "her patient is no longer a member", false); downs.Remove(scratch[i]); }
                scratch.Clear();

                foreach (Member m in ScRegistry.Members.Values)
                {
                    if (m.Status == MemberStatus.Arriving) continue;
                    EntityAlive e = ScCommunity.FindNpc(m.Id);
                    Down d;
                    downs.TryGetValue(m.Id, out d);
                    if (e == null || e.IsDead())
                    {
                        if (d != null && e == null)
                        {
                            // NPCCore clears the downed state when the body loads again (its boot clear): she stands at the floor health
                            unloaded++;
                            LetGo(d, "her patient unloaded", false);
                            scratch.Add(m.Id);
                            ScLog.Out(m.Name + "'s body unloaded while down - 0-XNPCCore stands her up at its floor health when she loads again");
                        }
                        continue;
                    }
                    if (d != null && !ReferenceEquals(d.Body, e))
                    {
                        unloaded++;
                        LetGo(d, "her patient's body was loaded again", false);
                        downs.Remove(m.Id);
                        ScLog.Out(m.Name + "'s body was loaded again while down - 0-XNPCCore stood her up at its floor health (no revive message)");
                        d = null;
                    }
                    if (IsDown(e))
                    {
                        if (d == null)
                        {
                            d = new Down { Id = m.Id, Name = m.Name, Body = e, Since = now };
                            downs[m.Id] = d;
                            seenDowns++;
                            ScLog.Out(m.Name + " is DOWN (" + m.Status + (ScWatch.OnWatch(m) ? ", on watch" : "") + (ScWork.OnShift(m.Id) ? (ScWork.InTalk(m.Id) ? ", talk link held" : ", work shift held") : "")
                                      + ", " + (UAI.UAIConsiderationScNotHomeMember.AtHome(e.position) ? "at home" : "away from home") + " at " + e.GetBlockPosition()
                                      + ") - " + Secs(Api.BleedLeftSafe(e)) + " to revive her; " + Path(e));
                        }
                        d.Rising = false;
                        d.Seen = now;
                        // 0-XNPCCore's busy flag keeps her own UAI still (SdcsAnimBusy, 12, over every action of SC's); Enter sets it, but an
                        // animation of 0-XNPCCore's that was playing when she went down (asleep in bed, an emote) clears it when it stops.
                        // Put back while she is down, so no action of SC's walks a downed body; the revive and the bleed-out clear it again
                        if (e.GetCVar(BusyCvar) < 0.5f)
                        {
                            e.SetCVar(BusyCvar, 1f);
                            busyRestored++;
                            if (!d.BusyLogged) ScLog.Out(m.Name + " is down but 0-XNPCCore's busy flag was off (an animation that ended under her?) - set again");
                            d.BusyLogged = true;
                        }
                        // 0-XNPCCore's 1 Hz tick (the bleed-out, the revive's grace) runs only above 0 health; a down at 0 (its kill catch
                        // has no dismember test, part_downed 2.6) would never end: the floor of 1 lets the bleed count
                        if (e.Health <= 0)
                        {
                            e.Health = 1;
                            zeroHealthFixed++;
                            ScLog.Warn(m.Name + " is down at 0 health - set to 1 so 0-XNPCCore's bleed-out counts (it skips a body at 0)");
                        }
                        d.Bleed = Api.BleedLeftSafe(e);
                        d.Home = UAI.UAIConsiderationScNotHomeMember.AtHome(e.position);
                    }
                    else if (d != null)
                    {
                        // 0-XNPCCore's bleed-out clears the cvar a frame before her marked kill: confirmed one second later
                        if (!d.Rising) { d.Rising = true; continue; }
                        Up(d, m);
                        downs.Remove(m.Id);
                    }
                }
                for (int i = 0; i < scratch.Count; i++) downs.Remove(scratch[i]);
                scratch.Clear();
                Medics(now);
            }
            catch (Exception ex)
            {
                ScLog.Once("downed-tick", ex);
            }
        }

        /// <summary>Which path made her go down, for the log: 0-XNPCCore's own (a Leader or Owner) or SC's home rule.</summary>
        private static string Path(EntityAlive e)
        {
            return Patches_Downed.NpcCoreEligible(e) ? "0-XNPCCore's own rule (a leader link)" : "SC's home rule (round U2)";
        }

        private static void Up(Down d, Member m)
        {
            string by = null;
            if (d.LastBeganAt > 0f && Time.time - d.LastBeganAt <= CreditSeconds && !string.IsNullOrEmpty(d.LastMedic)) by = d.LastMedic;
            LetGo(d, "her patient is up", false);
            // the stuck rescue starts again from nothing: counts from before the down never move her once she is up
            ScCommunity.ResetStill(m);
            ScStuck.ResetGain(m);
            m.AwaySeconds = 0;
            revived++;
            if (by != null) revivedByMedic++;
            ScLog.Out(m.Name + " is back up after " + Secs(Time.time - d.Since) + " down" + (by != null ? " - revived by " + by : " (you, a follower's medic, or 0-XNPCCore)"));
            if (by != null) Tell("scDownedRevivedBy", m.Name, by);
            else Tell("scDownedRevived", m.Name);
        }

        // ------------------------------------------------------------------ resident medics (D5) ----

        private static void Medics(float now)
        {
            // not behind Patches_Downed.Active: with the home rule off, a follower, a shift or a talk at home still goes down
            // (0-XNPCCore's own rule), and the medics need only 0-XNPCCore's public medic API
            if (!ScConfig.ResidentMedicsRevive)
            {
                foreach (Down d in downs.Values) if (d.MedicId >= 0) LetGo(d, "ResidentMedicsRevive is off", false);
                return;
            }
            // the sent medics first: still able, getting closer, in reach (0-XNPCCore's sequence), done
            foreach (Down d in downs.Values)
            {
                if (d.MedicId < 0) continue;
                if (d.Rising || !d.Home) { LetGo(d, d.Rising ? "her patient is up" : "her patient is not at home", false); continue; }
                Step(d, now);
            }
            // then everyone down at home with nobody coming, the shortest bleed first
            order.Clear();
            foreach (Down d in downs.Values) if (d.MedicId < 0 && d.Home && !d.Rising && d.Body != null) order.Add(d);
            if (order.Count == 0) return;
            order.Sort((a, b) => a.Bleed.CompareTo(b.Bleed));
            for (int i = 0; i < order.Count; i++) Send(order[i], now);
            order.Clear();
        }

        private static void Step(Down d, float now)
        {
            Member mm = ScRegistry.GetMember(d.MedicId);
            EntityAlive me = mm != null ? ScCommunity.FindNpc(mm.Id) : null;
            Why why;
            string refusal = Refusal(mm, me, d, true, out why);
            // never "failed" for these: one restocked, healed or done talking may be sent again (Send asks Refusal anew)
            if (refusal != null) { LetGo(d, refusal, false); return; }
            if (now - d.MedicSince > JobMaxSeconds) { LetGo(d, "no revive in " + (int)JobMaxSeconds + " s", true); return; }
            float until;
            bool fighting = fightUntil.TryGetValue(me.entityId, out until) && now < until;
            if (d.Treating)
            {
                // 0-XNPCCore's sequence holds her (SdcsMedicSeq, SdcsAnimBusy) until its ApplyHealOther; she is still down after it
                if (Busy(me) || now - d.BeganAt < 1f) return;
                d.Treating = false;
                d.InReachSince = now;
                ScLog.Out(mm.Name + "'s treatment of " + d.Name + " did not take (try " + d.Begins + " of " + MaxBegins + ")");
            }
            float dist = Vector3.Distance(me.position, d.Body.position);
            if (InReach(me, d.Body))
            {
                d.GainAt = now;
                if (fighting) return;
                if (d.InReachSince <= 0f) d.InReachSince = now;
                if (d.Begins >= MaxBegins) { LetGo(d, MaxBegins + " treatments did not take", true); return; }
                if (Busy(me))
                {
                    if (now - d.InReachSince > MaxWaitInReach) LetGo(d, "busy in reach for " + (int)MaxWaitInReach + " s", true);
                    return;
                }
                string item = Api.ItemFor(me, d.Body);
                if (item == null) { LetGo(d, "no revive item", true); return; }
                bool ok = Api.BeginSafe(me, d.Body, item);
                if (!ok)
                {
                    if (now - d.InReachSince > MaxWaitInReach) LetGo(d, "0-XNPCCore's medic sequence refused for " + (int)MaxWaitInReach + " s", true);
                    return;
                }
                d.Begins++;
                d.Treating = true;
                d.BeganAt = now;
                d.Item = item;
                d.LastMedic = mm.Name;
                d.LastBeganAt = now;
                treatments++;
                ScLog.Out(mm.Name + " treats " + d.Name + " with " + item + " (0-XNPCCore's medic sequence, try " + d.Begins + ")");
                return;
            }
            d.InReachSince = 0f;
            if (fighting) { d.GainAt = now; return; }     // the fight on the way is no lack of progress
            if (dist < d.Best - 1f) { d.Best = dist; d.GainAt = now; return; }
            if (now - d.GainAt > ScConfig.ReviveNoGainSeconds)
                LetGo(d, "no meter closer in " + Secs(ScConfig.ReviveNoGainSeconds) + " (" + dist.ToString("0.0", CultureInfo.InvariantCulture) + " m off)", true);
        }

        private static void Send(Down d, float now)
        {
            Member best = null;
            EntityAlive bestBody = null;
            float bestD2 = float.MaxValue, refusedD2 = float.MaxValue;
            Why firstWhy = Why.NoMedic;
            string firstName = null;
            foreach (Member mm in ScRegistry.Members.Values)
            {
                if (mm.Specialty != Specialty.Medicine || mm.Id == d.Id) continue;
                EntityAlive me = ScCommunity.FindNpc(mm.Id);
                Why why;
                if (Refusal(mm, me, d, false, out why) != null)
                {
                    // the reason the player is told: the nearest medic's who is at home and in the world (a medic away is "no medic")
                    if (why > Why.NoMedic && me != null)
                    {
                        float rd2 = (me.position - d.Body.position).sqrMagnitude;
                        if (rd2 < refusedD2) { refusedD2 = rd2; firstWhy = why; firstName = mm.Name; }
                    }
                    continue;
                }
                float d2 = (me.position - d.Body.position).sqrMagnitude;
                if (d2 < bestD2 || best == null) { bestD2 = d2; best = mm; bestBody = me; }
            }
            if (best == null)
            {
                if (d.ToldNoMedic) return;
                d.ToldNoMedic = true;
                noMedic++;
                string reason = firstName == null ? Localization.Get("scDownedWhyNoMedic") : string.Format(Localization.Get(WhyKey(firstWhy)), firstName);
                ScLog.Out("nobody revives " + d.Name + ": " + reason + " - the player must (E with a revive item)");
                Tell("scDownedNoMedic", d.Name, reason);
                return;
            }
            medicOf[bestBody.entityId] = d.Id;
            fightUntil.Remove(bestBody.entityId);
            d.MedicId = bestBody.entityId;
            d.MedicName = best.Name;
            d.MedicSince = now;
            d.GainAt = now;
            d.Best = Mathf.Sqrt(bestD2);
            d.InReachSince = 0f;
            d.Treating = false;
            d.Begins = 0;
            medicsSent++;
            ScLog.Out(best.Name + " (Medicine " + best.SpecLevel + ") goes to revive " + d.Name + ", " + d.Best.ToString("0", CultureInfo.InvariantCulture) + " m off"
                      + (ScSleep.IsAsleep(bestBody) ? " (woken: the night lease closes)" : ""));
            Tell("scDownedMedicGoes", d.Name, best.Name);
            d.ToldNoMedic = false;
        }

        /// <summary>The medic is let go (the action stops, her routine comes back). failed: she is not sent to this patient again.</summary>
        private static void LetGo(Down d, string why, bool failed)
        {
            if (d == null || d.MedicId < 0) return;
            int id = d.MedicId;
            medicOf.Remove(id);
            fightUntil.Remove(id);
            if (failed) d.Failed.Add(id);
            medicsLetGo++;
            ScLog.Out(d.MedicName + " stops going to " + d.Name + " - " + why + (failed ? " (not sent to her again)" : ""));
            d.MedicId = -1;
            d.MedicName = "";
            d.Treating = false;
            d.Begins = 0;
            d.InReachSince = 0f;
            if (failed) d.ToldNoMedic = false;      // the next try (another medic, or the player told why not) is said once more
        }

        /// <summary>Why this member may not go to this patient now, or null when she may. why: the kind the player is told.</summary>
        private static string Refusal(Member mm, EntityAlive me, Down d, bool current, out Why why)
        {
            why = Why.Busy;
            if (mm == null) { why = Why.NoMedic; return "no longer a member"; }
            if (mm.Specialty != Specialty.Medicine) { why = Why.NoMedic; return "not a medic"; }
            if (mm.Status != MemberStatus.Resident) { why = Why.NoMedic; return "not living at home (" + mm.Status + ")"; }
            if (me == null || me.IsDead()) { why = Why.NoMedic; return "not in the world"; }
            if (IsDown(me)) { why = Why.Hurt; return "down herself"; }
            if (!UAI.UAIConsiderationScNotHomeMember.AtHome(me.position)) { why = Why.NoMedic; return "not at home"; }
            if (ScWatch.OnWatch(mm)) return "on watch";
            if (ScWork.InTalk(mm.Id)) return "talking to you";
            if (ScAmmo.IsWindowOpen(me.entityId)) return "her inventory window is open";
            if (ScRetreat.Owns(me)) { why = Why.Hurt; return "sheltering or kiting hurt"; }
            if (ScRetreat.HurtNoMedsMember(me)) { why = Why.Hurt; return "badly hurt with nothing to heal with"; }
            int max = me.GetMaxHealth();
            if (max > 0 && (float)me.Health / max < ScConfig.ReviveMedicMinHealth) { why = Why.Hurt; return "under " + (ScConfig.ReviveMedicMinHealth * 100f).ToString("0", CultureInfo.InvariantCulture) + "% health"; }
            if (!Api.HoldsReviveItem(me)) { why = Why.NoItem; return "no revive item (" + Api.ReviveItemsText() + ") in her pack"; }
            if (!current && medicOf.ContainsKey(me.entityId)) return "helping someone else";
            if (d != null && d.Body != null)
            {
                if (!current && d.Failed.Contains(me.entityId)) { why = Why.Unreachable; return "could not get to her before"; }
                if ((me.position - d.Body.position).sqrMagnitude > ScConfig.ReviveMedicRange * ScConfig.ReviveMedicRange) { why = Why.Unreachable; return "farther than ReviveMedicRange"; }
            }
            why = Why.None;
            return null;
        }

        private static string WhyKey(Why w)
        {
            switch (w)
            {
                case Why.NoItem: return "scDownedWhyNoItem";
                case Why.Hurt: return "scDownedWhyHurt";
                case Why.Unreachable: return "scDownedWhyUnreachable";
                case Why.Busy: return "scDownedWhyBusy";
                default: return "scDownedWhyNoMedic";
            }
        }

        private static bool InReach(EntityAlive me, EntityAlive patient)
        {
            Vector3 a = me.position, b = patient.position;
            float dx = a.x - b.x, dz = a.z - b.z;
            return dx * dx + dz * dz <= TreatXZ * TreatXZ && Mathf.Abs(a.y - b.y) <= TreatDy;
        }

        /// <summary>A hand sequence of 0-XNPCCore's (its medic, heal, any busy clip) or a medical use of SC's own holds her.</summary>
        private static bool Busy(EntityAlive me)
        {
            return me.GetCVar("SdcsMedicSeq") >= 0.5f || me.GetCVar("SdcsAnimBusy") >= 0.5f || ScMedic.IsUsing(me.entityId);
        }

        // ------------------------------------------------------------------ the roster (D6) ----

        /// <summary>
        /// The community window's status while she is down (XUiC_ScCommunity.StatusText's first line): "Down - 42 s" on the row;
        /// in the detail panel also who is coming, or that nobody can. Null when she is not down.
        /// </summary>
        public static string StatusText(Member m, bool brief)
        {
            if (m == null) return null;
            Down d;
            if (!downs.TryGetValue(m.Id, out d) || d.Rising || d.Body == null || d.Body.IsDead() || !IsDown(d.Body)) return null;
            int s = Mathf.Max(0, Mathf.CeilToInt(Api.BleedLeftSafe(d.Body)));
            string t = string.Format(Localization.Get("scStDown"), s);
            if (brief) return t;
            if (d.MedicId >= 0) return t + " - " + string.Format(Localization.Get("scStDownMedic"), d.MedicName);
            if (d.Home && d.ToldNoMedic) return t + " - " + Localization.Get("scStDownNoMedic");
            return t;
        }

        // ------------------------------------------------------------------ console ----

        /// <summary>sc downed [id|name] | sc down &lt;id|name&gt; | sc revive &lt;id|name&gt;</summary>
        public static void ConsoleCmd(List<string> p, Action<string> output)
        {
            ScConfig.EnsureParsed();
            if (!ScRegistry.Loaded) { output("[SC] the registry is not loaded"); return; }
            string sub = p.Count > 0 ? p[0].ToLowerInvariant() : "downed";
            if (sub == "down" || sub == "revive")
            {
                Member t = p.Count > 1 ? FindMember(p[1]) : null;
                if (t == null) { output("[SC] usage: sc " + sub + " <id|name> - " + (sub == "down" ? "knocks a member down now through 0-XNPCCore (its own rules decide)" : "revives a downed member now (0-XNPCCore's own revive)")); return; }
                EntityAlive e = ScCommunity.FindNpc(t.Id);
                if (e == null || e.IsDead()) { output("[SC] " + t.Name + " is not loaded"); return; }
                if (!ScCommunity.IsHost) { output("[SC] host only"); return; }
                string r;
                try { r = sub == "down" ? Api.Enter(e) : Api.Revive(e, "the console"); }
                catch (Exception ex) { output("[SC] 0-XNPCCore's downed state is not there (" + ex.GetType().Name + ") - 3.4.06 or later is needed"); return; }
                output("[SC] 0-XNPCCore: " + r);
                return;
            }
            if (p.Count > 1)
            {
                Member one = FindMember(p[1]);
                if (one == null) { output("[SC] no member '" + p[1] + "' - sc downed [id|name]"); return; }
                Lines(one, output, true);
                return;
            }
            output("[SC] downed (round U2): patches " + Patches_Downed.State);
            output("[SC] settings: " + ScConfig.DownedLine());
            output("[SC] 0-XNPCCore: revive items " + Api.ReviveItemsText() + "; the 1 Hz tick counts the bleed only while a leader is present - SC's swap adds the "
                   + "community's own people while you are alive");
            output("[SC] counters: downs " + seenDowns + ", back up " + revived + " (by a resident medic " + revivedByMedic + "), bled out " + bledOut
                   + ", unloaded while down " + unloaded + "; medics sent " + medicsSent + ", let go " + medicsLetGo + ", treatments " + treatments
                   + ", fights on the way " + fights + ", nobody to send " + noMedic + "; held: stationings " + stationsDeferred + ", shift/talk ticks " + shiftsHeld
                   + ", verbs refused " + verbsRefused + ", busy flag put back " + busyRestored + ", 0 health raised " + zeroHealthFixed + "; swaps: home eligible " + Patches_Downed.EligibleHome + ", home bleed ticks " + Patches_Downed.BleedHome
                   + ", tells (down " + Patches_Downed.TellsDown + ", warnings to you " + Patches_Downed.TellsHome + ")");
            int n = 0;
            foreach (Member m in ScRegistry.Members.Values) { Lines(m, output, false); n++; }
            output("[SC] " + n + " member(s); sc downed <name> for one in full, sc down <name> / sc revive <name> to test");
        }

        private static void Lines(Member m, Action<string> output, bool full)
        {
            EntityAlive e = ScCommunity.FindNpc(m.Id);
            var sb = new StringBuilder("[SC]   ");
            sb.Append(m.Name).Append(" (").Append(m.Id).Append(", ").Append(m.Status);
            if (m.Specialty == Specialty.Medicine) sb.Append(", Medicine ").Append(m.SpecLevel);
            sb.Append("): ");
            if (e == null || e.IsDead()) { output(sb.Append("not loaded").ToString()); return; }
            bool down = IsDown(e);
            Down d;
            downs.TryGetValue(m.Id, out d);
            if (down)
            {
                bool paused = Api.BleedPausedSafe(e);
                sb.Append("DOWN, ").Append(Secs(Api.BleedLeftSafe(e))).Append(" left").Append(paused ? " (PAUSED: no leader present)" : "");
                if (d != null)
                {
                    sb.Append(", ").Append(d.Home ? "at home" : "away");
                    if (d.MedicId >= 0) sb.Append(", medic ").Append(d.MedicName).Append(d.Treating ? " treating (try " + d.Begins + ")" : " on the way");
                    else if (d.Home) sb.Append(d.ToldNoMedic ? ", nobody can come" : ", no medic yet");
                    if (d.Failed.Count > 0) sb.Append(", medics who could not: ").Append(d.Failed.Count);
                }
            }
            else
            {
                string how = Patches_Downed.NpcCoreEligible(e) ? "0-XNPCCore's rule (a Leader or Owner link)"
                             : HomeDownable(e) && Patches_Downed.Active ? "SC's home rule"
                             : !Patches_Downed.Active ? "no (the patches are off)"
                             : !ScConfig.DownedAtHome && m.Status == MemberStatus.Resident ? "no (DownedAtHome false)"
                             : "no (" + m.Status + ": dies at once)";
                sb.Append("up, health ").Append(e.Health).Append('/').Append(e.GetMaxHealth()).Append("; a killing hit downs her: ").Append(how);
                float grace = e.GetCVar("SdcsDownedGrace"), redown = e.GetCVar("SdcsDownedRedown");
                if (grace > 0f) sb.Append("; grace ").Append(Secs(grace)).Append(" (invulnerable)");
                if (redown > 0f) sb.Append("; a second down within ").Append(Secs(redown)).Append(" is a real death");
            }
            if (m.Specialty == Specialty.Medicine && (full || !down))
            {
                int pid;
                if (medicOf.TryGetValue(e.entityId, out pid)) sb.Append("; going to revive ").Append(downs.ContainsKey(pid) ? downs[pid].Name : pid.ToString());
                else
                {
                    Why why;
                    string r = Refusal(m, e, null, false, out why);
                    sb.Append("; as a medic: ").Append(r == null ? "free (carries " + Api.ReviveItemsText() + ": yes)" : r);
                }
            }
            output(sb.ToString());
        }

        private static Member FindMember(string key)
        {
            int id;
            if (int.TryParse(key, out id)) return ScRegistry.GetMember(id);
            foreach (var m in ScRegistry.Members.Values)
                if (m.Name != null && m.Name.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0) return m;
            return null;
        }

        private static string Secs(float s) { return Mathf.Max(0f, s).ToString("0", CultureInfo.InvariantCulture) + " s"; }

        // ------------------------------------------------------------------ 0-XNPCCore's API ----

        /// <summary>
        /// 0-XNPCCore 3.4.06's public downed and medic API, each call in a method of its own (NoInlining): on an 0-XNPCCore
        /// without it the JIT throws at the call, inside the caller's try, instead of taking the caller down with it.
        /// </summary>
        private static class Api
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            internal static bool IsDowned(EntityAlive e) { return SdcsDowned.IsDowned(e); }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static float BleedLeft(EntityAlive e) { return SdcsDowned.BleedSecondsLeft(e); }

            internal static float BleedLeftSafe(EntityAlive e)
            {
                try { return BleedLeft(e); }
                catch (Exception) { return e != null ? e.GetCVar("SdcsDownedBleed") : 0f; }
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static bool BleedPaused(EntityAlive e)
            {
                AvatarSdcsNpcController c = e.GetComponent<AvatarSdcsNpcController>();
                return c != null && c.SdcsDownedBleedPaused;
            }

            internal static bool BleedPausedSafe(EntityAlive e)
            {
                try { return BleedPaused(e); }
                catch (Exception) { return false; }
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            internal static string Enter(EntityAlive e) { return SdcsDowned.Enter(e); }

            [MethodImpl(MethodImplOptions.NoInlining)]
            internal static string Revive(EntityAlive e, string who) { return SdcsDowned.Revive(e, who); }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static string[] ReviveItems() { return AvatarSdcsNpcController.SdcsDownedReviveItems; }

            internal static string ReviveItemsText()
            {
                try
                {
                    string[] items = ReviveItems();
                    return items != null && items.Length > 0 ? string.Join(", ", items) : "none";
                }
                catch (Exception) { return "?"; }
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static bool HoldsAny(EntityAlive h) { return SdcsMedic.HoldsAnyItem(h, AvatarSdcsNpcController.SdcsDownedReviveItems); }

            internal static bool HoldsReviveItem(EntityAlive h)
            {
                try { return HoldsAny(h); }
                catch (Exception e) { ScLog.Once("downed-holds", e); return false; }
            }

            /// <summary>0-XNPCCore's own pick (SdcsMedic.ChooseFor: the first revive item she holds, in ReviveItems' order), else the same by hand.</summary>
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static string Choose(EntityAlive h, EntityAlive p)
            {
                NPCSdcsCharacters.Pure.MedicCondition c;
                string item;
                if (SdcsMedic.ChooseFor(h, p, false, 1000f, out c, out item) && c == NPCSdcsCharacters.Pure.MedicCondition.Downed && !string.IsNullOrEmpty(item)) return item;
                string[] names = AvatarSdcsNpcController.SdcsDownedReviveItems;
                if (names == null) return null;
                for (int i = 0; i < names.Length; i++)
                    if (SdcsMedic.HoldsAnyItem(h, new[] { names[i] })) return names[i];
                return null;
            }

            internal static string ItemFor(EntityAlive h, EntityAlive p)
            {
                try { return Choose(h, p); }
                catch (Exception e) { ScLog.Once("downed-choose", e); return null; }
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static bool Begin(EntityAlive h, EntityAlive p, string item)
            {
                return SdcsMedic.Begin(h, p, NPCSdcsCharacters.Pure.MedicCondition.Downed, item);
            }

            internal static bool BeginSafe(EntityAlive h, EntityAlive p, string item)
            {
                try { return Begin(h, p, item); }
                catch (Exception e) { ScLog.Once("downed-begin", e); return false; }
            }
        }
    }
}
