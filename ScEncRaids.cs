using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Raider enclaves raid, milestone E10 (research\ideas\DESIGN.md section 4, with the user's decisions: the raiders are the
    /// hostile enclave's own people and their deaths count, Q8 a; OutpostAttacks off turns enclave raids off too; no raider
    /// demands where no hostile enclave is in range; a group that turned hostile raids at half the rate; no raids on the home
    /// base).
    ///
    /// With OutpostAttacks, EnclaveRaids and Enclaves on, an outpost's raider demand (ScOutpostRisk.Daily) comes from a named
    /// group: the nearest hostile enclave within EnclaveRaidRange of the outpost (footprint to footprint) that can raid - not
    /// lost, not leaning on another outpost already, past its rest (Enclave.RaidDay, request clock) and with someone to spare
    /// (a living non-leader at home, or, in a group still being made, a place left beside the leader's) - rolling
    /// EnclaveRaidChance a rollover, or that times EnclaveRaidTurnedFactor for a group that turned hostile (E2). With no such
    /// enclave in range there is no raider demand there at all (the old nameless roll is gone while this is on). The demand is
    /// stamped on the outpost (Outpost.RaidEnclave) and puts their building on the map. Paid: they rest. Refused, or left until
    /// the next rollover: the raid is called as before (ScOutpostRisk.StartAttack, OutpostAttackHours to get there).
    ///
    /// The raiders are their own people (SpawnParty, when the player arrives: ScOutpostRisk.SpawnAttack): min(the attack's
    /// size, EnclaveRaidPartyMax, those to spare), the strongest at home first (never the leader; one whose body is out at
    /// home leaves it where nobody sees it go, one in view stays), then - a group never met - new people made at the outpost
    /// as E2 makes them at home (ScEnclavePeople.GenerateAway: their records and faces; no spot until their first spawn at
    /// home). Each body is ScEnclavePeople.SpawnAt's (dressed as every body: never saved, the enclave's cvars, the record's
    /// health; a turned-hostile group's also the SCEnclave package, whose approach brings its survivors in) and each person
    /// is "raiding" (EncPerson.StateRaiding): alive - the fall rule, Living and the group count see them - but not at home.
    /// E2's tick hands the body here (TickBody: its target given again while the raid is on; once it is over, home, and the
    /// body walks off and goes once out of sight). Those killed are dead for good (E2's Died); their losses are counted on
    /// the outpost (Outpost.RaidLost: a reload keeps them) and told once, when the raid ends however it ends ("The X lost 3
    /// people raiding Diner."). A body that goes alive (an unload, the shutdown) leaves its person raiding off the map, and
    /// Regroup brings them back round the outpost before the fight is judged won.
    ///
    /// End (ScOutpostRisk.Won / Settle): survivors home (a body out walks off), the group rests EnclaveRaidRestDays, and an
    /// unattended raid the outpost held costs them EnclaveRaidLossHeld of the party they would have sent (records first, then
    /// people not made yet: Enclave.RaidLost) - unless the player fought it before a reload (its losses kept); damaged or
    /// overrun, none of them die. A raid whose group can no longer send anyone is called off; a group that falls
    /// (ScEnclavePeople.DropPeople) drops its demand and its call, and a party out fights on as plain raiders (OnDenFalling);
    /// switching any of the three off calls an open one off.
    ///
    /// Session state is the party only. The records persist: Outpost.RaidEnclave and RaidLost ([outpost] raidenc=,
    /// raidlost=), Enclave.RaidDay and RaidLost ([enclave] raidday=, raidlost=). A raiding person is saved as home (bodies
    /// never outlive a load), so an older build reads them right, and after a load the party is picked again when the player
    /// arrives. Host only, main thread; no Harmony patch.
    /// </summary>
    public static class ScEncRaids
    {
        /// <summary>A raid body's attack target is looked at this often (real seconds) and given again when it has none.</summary>
        private const float AssertSeconds = 5f;
        /// <summary>SetAttackTarget's time, as the outpost attack's own raiders get it (ScOutpostRisk.SpawnAttack).</summary>
        private const int AttackTargetTicks = 1200;
        /// <summary>How often (real seconds) a raiding person left without a party is looked for.</summary>
        private const float SweepSeconds = 10f;

        /// <summary>A raid party out this session: who went (person ids, the dead among them). Their losses are the outpost's (Outpost.RaidLost).</summary>
        private sealed class Party
        {
            public int OutpostId, EnclaveId;
            public string EnclaveName = "";
            public readonly List<int> Pids = new List<int>();
        }

        /// <summary>Outpost id -> the party raiding it (session only).</summary>
        private static readonly Dictionary<int, Party> parties = new Dictionary<int, Party>();
        /// <summary>Body id -> the next time its attack target is looked at (real time).</summary>
        private static readonly Dictionary<int, float> nextAssert = new Dictionary<int, float>();
        private static readonly List<EncPerson> scratch = new List<EncPerson>();
        private static float nextSweep;

        public static void Reset()
        {
            parties.Clear();
            nextAssert.Clear();
            scratch.Clear();
            nextSweep = 0f;
        }

        /// <summary>ScOutpostRisk.OnRegistryChanged: no party is out (bodies never outlive a load; raiding people load as home).</summary>
        public static void OnRegistryChanged()
        {
            Reset();
        }

        private static string Loc(string key) { return Localization.Get(key); }

        // ------------------------------------------------------------------ the rules ----

        /// <summary>The three switches (the user: OutpostAttacks off turns enclave raids off too).</summary>
        public static bool Configured { get { return ScConfig.OutpostAttacks && ScConfig.EnclaveRaids && ScConfig.Enclaves; } }

        /// <summary>Configured, and this session's enclaves validated (ScEnclaves.Ready).</summary>
        public static bool On { get { return Configured && ScEnclaves.Ready; } }

        /// <summary>Why enclave raids are off ("on" when they are not).</summary>
        public static string OffWhy()
        {
            if (!ScConfig.OutpostAttacks) return "OutpostAttacks=false";
            if (!ScConfig.EnclaveRaids) return "EnclaveRaids=false";
            if (!ScConfig.Enclaves) return "Enclaves=false";
            return ScEnclaves.Ready ? "on" : "the world is still starting";
        }

        /// <summary>Request-clock days until they may raid again (0: now).</summary>
        public static int DaysLeft(Enclave e) { return e == null ? 0 : Math.Max(0, e.RaidDay - ScEncRequests.Now); }

        /// <summary>Their chance of a demand on an outpost in range, a rollover: EnclaveRaidChance, x EnclaveRaidTurnedFactor for a group turned hostile.</summary>
        public static float ChanceFor(Enclave e)
        {
            if (e == null || !e.IsHostile) return 0f;
            return Mathf.Clamp01(ScConfig.EnclaveRaidChance * (e.Hostile ? 1f : ScConfig.EnclaveRaidTurnedFactor));
        }

        /// <summary>XZ metres from their building's footprint to the outpost's (0 when they touch).</summary>
        public static float RangeTo(Enclave e, Outpost o)
        {
            int[] a = e.Box, b = o.Box;
            float dx = Math.Max(0, Math.Max(a[0] - b[1], b[0] - a[1]));
            float dz = Math.Max(0, Math.Max(a[2] - b[3], b[2] - a[3]));
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>
        /// Who they can send: the living non-leaders at home (home), and - a group still being made - the places left in it
        /// beside the leader's (unmade: one is kept for a leader while no living leader is at home). The leader never raids.
        /// </summary>
        public static int Spare(Enclave e, out int home, out int unmade)
        {
            home = 0;
            unmade = 0;
            if (e == null) return 0;
            int records = 0;
            foreach (EncPerson p in ScRegistry.EncPeople)
            {
                if (p.EnclaveId != e.Id) continue;
                records++;
                if (p.State == EncPerson.StateHome && !p.IsLeader) home++;
            }
            if (!e.PeopleRolled) unmade = Math.Max(0, ScEnclavePeople.GroupSize(e) - records - (ScEnclavePeople.LeaderOf(e) != null ? 0 : 1));
            return home + unmade;
        }

        public static int Spare(Enclave e)
        {
            int home, unmade;
            return Spare(e, out home, out unmade);
        }

        /// <summary>The outpost this group leans on now (an open demand, a raid called or a party out), or null.</summary>
        public static Outpost TargetOf(Enclave e)
        {
            if (e == null) return null;
            foreach (Outpost o in ScRegistry.Outposts)
                if (o.RaidEnclave == e.Id && (ScOutpostRisk.HasDemand(o) || (ScOutpostRisk.UnderAttack(o) && o.AttackRaiders) || parties.ContainsKey(o.Id))) return o;
            return null;
        }

        /// <summary>The group behind an outpost's demand or raid, or null (a nameless one, or none).</summary>
        public static Enclave EnclaveOf(Outpost o)
        {
            return o != null && o.RaidEnclave > 0 ? ScEnclaves.ById(o.RaidEnclave) : null;
        }

        /// <summary>The stamp off: no group behind the outpost's demand or raid, and no losses of one counted there (Outpost.RaidLost).</summary>
        internal static void Unstamp(Outpost o)
        {
            o.RaidEnclave = -1;
            o.RaidLost = 0;
        }

        /// <summary>Why this group can't lean on an outpost now (null: it can). force: their rest ignored (the console).</summary>
        public static string WhyNot(Enclave e, bool force)
        {
            if (!On) return "enclave raids are off (" + OffWhy() + ")";
            if (e == null) return "no such enclave";
            if (!e.IsHostile) return "they are not hostile";
            if (e.Lost) return "their building is not in this world";
            Outpost busy = TargetOf(e);
            if (busy != null) return "they are leaning on " + busy.Name + " (outpost " + busy.Id + ") already";
            int left = DaysLeft(e);
            if (!force && left > 0) return "they rest after their last raid until day " + e.RaidDay + " (" + left + " day(s))";
            if (Spare(e) <= 0) return "they have nobody to spare (the leader never raids)";
            return null;
        }

        /// <summary>The daily roll's group for this outpost: the nearest hostile enclave in range that can raid now (WhyNot), or null.</summary>
        public static Enclave RaiderFor(Outpost o)
        {
            Enclave best = null;
            float bestD = float.MaxValue;
            foreach (Enclave e in ScEnclaves.All)
            {
                if (!e.IsHostile || e.Lost) continue;
                float d = RangeTo(e, o);
                if (d > ScConfig.EnclaveRaidRange || d >= bestD || WhyNot(e, false) != null) continue;
                best = e;
                bestD = d;
            }
            return best;
        }

        /// <summary>They rest EnclaveRaidRestDays request-clock days from now.</summary>
        private static void Rest(Enclave e)
        {
            if (e != null) e.RaidDay = ScEncRequests.Now + Math.Max(0, ScConfig.EnclaveRaidRestDays);
        }

        /// <summary>"The X on the radio: "..."": their words (a line with no name or number in it, for the voicing round).</summary>
        private static string Radio(Enclave e, string saidKey)
        {
            return string.Format(Loc("scEncCallRadio"), e.Name, Loc(saidKey));
        }

        private static EncPerson PersonById(int pid)
        {
            if (pid <= 0) return null;
            foreach (EncPerson p in ScRegistry.EncPeople) if (p.Id == pid) return p;
            return null;
        }

        // ------------------------------------------------------------------ the demand ----

        /// <summary>
        /// Their demand on the outpost (ScOutpostRisk.Daily's roll, or the console): the price and the day to answer by as the
        /// nameless one's, stamped with the group; their building on the map; their words and the facts as one tell (the voiced
        /// part has no name or number), a line in the day's summary, the log, a save.
        /// </summary>
        internal static void Demand(Outpost o, Enclave e, int day, EntityPlayerLocal player, StringBuilder summary, string why)
        {
            o.DemandUntil = day;                          // answer before the next rollover
            o.DemandAmmo = ScConfig.OutpostDemandAmmo;
            o.DemandFood = ScConfig.OutpostDemandFood;
            o.RaidEnclave = e.Id;
            o.RaidLost = 0;
            bool revealed = !e.Known && player != null && ScEnclaves.SetKnown(e, true, player) > 0;
            if (player != null)
                ScCommunity.Tell(player, Radio(e, "scEncRaidDemandSaid") + " " + string.Format(Loc("scEncRaidDemand"), o.DemandAmmo, o.DemandFood, o.Name)
                                         + (revealed ? " " + Loc("scEncRaidRevealed") : ""), "ui_denied");
            if (summary != null)
            {
                if (summary.Length > 0) summary.Append(' ');
                summary.Append(string.Format(Loc("scEncRaidDemandLine"), e.Name, o.Name));
            }
            ScLog.Out("enclave " + e.Id + " '" + e.Name + "' demands " + o.DemandAmmo + " ammo + " + o.DemandFood + " food for outpost " + o.Id + " (" + o.Name + "), "
                      + Mathf.RoundToInt(RangeTo(e, o)) + " m from their building (" + why + ")" + (revealed ? " - their building is on the map now" : ""));
            ScRegistry.SaveAtomic();
        }

        /// <summary>ScOutpostRisk.PayDemand, once paid: the ledger, their rest, the stamp off; the window's words. Null for a nameless demand.</summary>
        internal static string OnPaid(Outpost o)
        {
            Enclave e = EnclaveOf(o);
            Unstamp(o);
            if (e == null) return null;
            Rest(e);
            ScOutposts.NoteLedger(string.Format(Loc("scEncRaidLedgerPaid"), e.Name, o.Name));
            ScLog.Out("paid enclave " + e.Id + " '" + e.Name + "' for outpost " + o.Id + " (" + o.Name + ") - they rest until day " + e.RaidDay);
            return Radio(e, "scEncRaidPaidSaid") + " " + string.Format(Loc("scEncRaidPaid"), e.Name, o.Name);
        }

        /// <summary>
        /// ScOutpostRisk.Daily, a demand that ran out unpaid: true when its raid may come (a nameless one always). An enclave's
        /// comes only while enclave raids are on and the group can still send someone; otherwise the stamp is dropped here
        /// (with "The X never came ..." or "The X are gone ..." for the player) and false.
        /// </summary>
        internal static bool MayCome(Outpost o, EntityPlayerLocal player)
        {
            if (o == null || o.RaidEnclave <= 0) return true;
            Enclave e = ScEnclaves.ById(o.RaidEnclave);
            string why = null, tell = null;
            if (!Configured) why = "enclave raids are off (" + OffWhy() + ")";
            else if (e == null || e.Lost) { why = e == null ? "their enclave is gone" : "their building is not in this world"; tell = "scEncRaidGone"; }
            else if (!e.IsHostile) why = "they are not hostile";
            else if (Spare(e) <= 0) { why = "they have nobody left to send"; tell = "scEncRaidNobody"; }
            if (why == null) return true;
            Unstamp(o);
            Rest(e);
            if (tell != null && player != null && e != null) ScCommunity.Tell(player, string.Format(Loc(tell), e.Name, o.Name), "");
            ScLog.Out("the demand on outpost " + o.Id + " (" + o.Name + ") ran out, but no raid comes: " + why);
            return false;
        }

        /// <summary>ScOutpostRisk.RefuseDemand's words for the window: theirs and the facts. Null for a nameless demand.</summary>
        internal static string RefusedText(Outpost o)
        {
            Enclave e = EnclaveOf(o);
            return e == null ? null : Radio(e, "scEncRaidRefusedSaid") + " " + string.Format(Loc("scEncRaidRefused"), e.Name, o.Name, ScConfig.OutpostAttackHours);
        }

        // ------------------------------------------------------------------ the 1 Hz check ----

        /// <summary>
        /// ScOutpostRisk.Tick, every second: each outpost's demand or raid squared with its group - raids switched off, the
        /// group gone or out of this world, or (a raid called, its party not out) nobody left to send: called off. A stamp with
        /// nothing open any more is cleared. Every SweepSeconds a raiding person no party holds (never expected) goes home.
        /// </summary>
        public static void Tick(World w, EntityPlayerLocal player)
        {
            try
            {
                if (Configured && !ScEnclaves.Ready) return;
                bool changed = false;
                List<Outpost> list = null;
                foreach (Outpost o in ScRegistry.Outposts) if (o.RaidEnclave > 0) (list ?? (list = new List<Outpost>())).Add(o);
                if (list != null)
                {
                    foreach (Outpost o in list)
                    {
                        bool spawned = parties.ContainsKey(o.Id);
                        bool demand = ScOutpostRisk.HasDemand(o), raid = ScOutpostRisk.UnderAttack(o) && o.AttackRaiders;
                        if (!demand && !raid && !spawned) { Unstamp(o); changed = true; continue; }
                        Enclave e = ScEnclaves.ById(o.RaidEnclave);
                        string why = null, tell = null;
                        if (!Configured) why = "enclave raids are off (" + OffWhy() + ")";
                        else if (e == null) { why = "their enclave is gone"; tell = "scEncRaidGone"; }
                        else if (e.Lost) { why = "their building is not in this world"; tell = "scEncRaidGone"; }
                        else if (!e.IsHostile) why = "they are not hostile";
                        else if (raid && !spawned && Spare(e) <= 0) { why = "they have nobody left to send"; tell = "scEncRaidNobody"; }
                        if (why == null) continue;
                        CallOff(o, e, player, why, tell);
                        changed = true;
                    }
                }
                float t = Time.unscaledTime;
                if (t >= nextSweep)
                {
                    nextSweep = t + SweepSeconds;
                    foreach (EncPerson p in ScRegistry.EncPeople)
                    {
                        if (!p.Raiding || p.EntityId >= 0) continue;
                        Outpost at;
                        if (PartyOf(p.Id, out at) != null) continue;
                        p.State = EncPerson.StateHome;
                        changed = true;
                        ScLog.Out(p.Name + " (p" + p.Id + ") of enclave " + p.EnclaveId + " was raiding with no raid open - home again");
                    }
                }
                if (changed) ScRegistry.SaveAtomic();
            }
            catch (Exception e)
            {
                ScLog.Once("encraids-tick", e);
            }
        }

        /// <summary>
        /// A demand or raid off before its end: the demand dropped, a party out sent home (bodies walk off), the call off (no
        /// outcome), the stamp off, the group resting; tellKey ({0} the group, {1} the outpost) or null; the losses of a raid
        /// already fought (Outpost.RaidLost) told as at its end; the log. The caller saves.
        /// </summary>
        private static void CallOff(Outpost o, Enclave e, EntityPlayerLocal player, string why, string tellKey)
        {
            Party party;
            parties.TryGetValue(o.Id, out party);
            string name = e != null ? e.Name : party != null ? party.EnclaveName : "";
            bool demand = ScOutpostRisk.HasDemand(o), raid = ScOutpostRisk.UnderAttack(o) && o.AttackRaiders;
            int lost = o.RaidLost;
            if (demand) o.DemandUntil = -1;
            if (party != null)
            {
                SendPartyHome(party, e, "the raid was called off");
                parties.Remove(o.Id);
            }
            if (raid) ScOutpostRisk.CallOff(o);
            Unstamp(o);
            Rest(e);
            if (tellKey != null && player != null && name.Length > 0) ScCommunity.Tell(player, string.Format(Loc(tellKey), name, o.Name), "");
            TellLosses(player, name, lost, o);
            ScLog.Out("enclave raid on outpost " + o.Id + " (" + o.Name + ") by '" + name + "' is off: " + why + (demand ? " - the demand is dropped" : "")
                      + (raid ? " - the raid call is off" : "") + (party != null ? " - the party goes home" : "") + (lost > 0 ? " - " + lost + " of theirs lost in it" : ""));
        }

        /// <summary>ScOutpostRisk.SpawnAttack: the player is there, but nobody of theirs could come (none left, no body could be made): the raid is off. The caller saves.</summary>
        internal static void NobodyCame(Outpost o, Enclave e, EntityPlayerLocal player)
        {
            CallOff(o, e, player, "nobody of theirs could come (" + Spare(e) + " to spare)", "scEncRaidNobody");
        }

        // ------------------------------------------------------------------ the party ----

        /// <summary>The order the party is picked in: the class tier, then Fighting + Shooting stars, then the one made first (E9's rank).</summary>
        private static int Rank(EncPerson a, EncPerson b)
        {
            int ta = ScXnpc.TierOf(a.Who.Class), tb = ScXnpc.TierOf(b.Who.Class);
            if (ta != tb) return tb.CompareTo(ta);
            int sa = a.Who.Stars(Skill.Fighting) + a.Who.Stars(Skill.Shooting), sb = b.Who.Stars(Skill.Fighting) + b.Who.Stars(Skill.Shooting);
            if (sa != sb) return sb.CompareTo(sa);
            return a.Id.CompareTo(b.Id);
        }

        /// <summary>The yaw that faces a point from another.</summary>
        private static float Face(Vector3 from, Vector3 to)
        {
            float dx = to.x - from.x, dz = to.z - from.z;
            return dx * dx + dz * dz < 0.01f ? 0f : Mathf.Atan2(dx, dz) * Mathf.Rad2Deg;
        }

        /// <summary>The party's target: a stand-in holding the outpost, else the player while the fight is theirs (its judging range), else null.</summary>
        private static EntityAlive Target(World w, Outpost o)
        {
            EntityAlive s = ScOutpostRisk.StandIn(w, o);
            if (s != null) return s;
            EntityPlayerLocal pl = ScCommunity.LocalPlayer;
            return pl != null && !pl.IsDead() && ScOutposts.DistanceToBox(o, pl.position) <= ScConfig.RescueTriggerRange * 2f ? pl : null;
        }

        /// <summary>A body for a person of theirs on the ring round the outpost (the attack's own placement), facing it. Null: none.</summary>
        private static EntityAlive Place(World w, Enclave e, EncPerson p, Outpost o, GameRandom r, float reach)
        {
            Vector3 pos = ScOutpostRisk.RingSpot(w, o, r, reach);
            EntityAlive npc = ScEnclavePeople.SpawnAt(w, e, p, pos, Face(pos, o.Centre));
            if (npc != null) Hostile(e, npc);
            return npc;
        }

        /// <summary>
        /// A body of a group turned hostile gets the SCEnclave package (E9's fighter's way, ScEncCalls): its ScEncApproach and
        /// ScEncInvestigate (ScEncHostile) bring a survivor with a gun in - XNPC's own ranged package only shoots and backs
        /// off. Away from home every other action of it scores 0 (ScEncHome). Raider classes carry SCRaider's approach already.
        /// </summary>
        private static void Hostile(Enclave e, EntityAlive npc)
        {
            if (e.TurnedHostile) ScEnclavePeople.AddPackage(npc);
        }

        /// <summary>One of the party: raiding, in its list, the body in the fight's list with its target.</summary>
        private static void Join(Party party, EncPerson p, EntityAlive npc, EntityAlive target, List<int> ids)
        {
            p.State = EncPerson.StateRaiding;
            if (!party.Pids.Contains(p.Id)) party.Pids.Add(p.Id);
            Arm(npc, target, ids);
        }

        private static void Arm(EntityAlive npc, EntityAlive target, List<int> ids)
        {
            ids.Add(npc.entityId);
            if (target != null) npc.SetAttackTarget(target, AttackTargetTicks);
            nextAssert[npc.entityId] = Time.unscaledTime + AssertSeconds;
        }

        /// <summary>
        /// ScOutpostRisk.SpawnAttack, the player arrived at an enclave's raid: min(count, EnclaveRaidPartyMax, those to spare)
        /// of their people round the outpost - those at home first (the strongest; never the leader, never one whose body is
        /// still being built; one whose body is out at home - a den near the outpost, the player within its spawn range - leaves
        /// it where nobody sees it go, the review's M1, and one in view stays), then new people made at the outpost while the
        /// group has places left (GenerateAway). Each is raiding, its body in ids with the attack's target. Returns how many came
        /// (0: nobody - the caller calls the raid off; -1: nobody yet, as those who could come are in view at home or their
        /// bodies still being built - the caller tries again the next second).
        /// </summary>
        internal static int SpawnParty(World w, EntityPlayerLocal player, Outpost o, int count, EntityAlive target, List<int> ids)
        {
            Enclave e = EnclaveOf(o);
            if (w == null || e == null || e.Lost || !e.IsHostile) return 0;
            int home, unmade;
            int spare = Spare(e, out home, out unmade);
            int want = Math.Min(Math.Min(count, Math.Max(1, ScConfig.EnclaveRaidPartyMax)), spare);
            Party old;
            if (parties.TryGetValue(o.Id, out old))
            {
                SendPartyHome(old, e, "a new party came");
                parties.Remove(o.Id);
            }
            if (want <= 0) return 0;
            var party = new Party { OutpostId = o.Id, EnclaveId = e.Id, EnclaveName = e.Name };
            GameRandom r = w.GetGameRandom();
            float reach = ScOutpostRisk.Reach(o);
            if (target == null) target = player;
            int made = 0, fresh = 0, moved = 0, seen = 0, busy = 0;
            scratch.Clear();
            foreach (EncPerson p in ScRegistry.EncPeople)
            {
                if (p.EnclaveId != e.Id || p.State != EncPerson.StateHome || p.IsLeader) continue;
                if (p.EntityId >= 0 && ScNpc.IsPending(w, p.EntityId)) { busy++; continue; }                               // their body at home still being built
                if (p.EntityId < 0 && p.LastEntityId >= 0 && ScNpc.IsPending(w, p.LastEntityId)) { busy++; continue; }    // a copy of their last body still being built
                scratch.Add(p);
            }
            scratch.Sort(Rank);
            var picked = new List<EncPerson>(scratch);
            scratch.Clear();
            foreach (EncPerson p in picked)
            {
                if (made >= want) break;
                bool left = false;
                if (p.EntityId >= 0)
                {
                    // their body is out at home: it goes first (its health and rounds recorded), unless someone may see it go
                    EntityAlive own = ScEnclavePeople.OwnBody(p);
                    if (own == null || own.IsDead()) { busy++; continue; }      // gone or dying: their tick squares it first
                    if (ScEnclavePeople.SeenByAny(w, own.position)) { seen++; continue; }
                    ScEnclavePeople.Despawn(p, own);
                    left = true;
                }
                EntityAlive npc = Place(w, e, p, o, r, reach);
                if (npc == null) continue;
                Join(party, p, npc, target, ids);
                made++;
                if (left) moved++;
            }
            int guard = 0;
            while (made < want && guard++ < 16)
            {
                int h2, u2;
                Spare(e, out h2, out u2);
                if (u2 <= 0) break;
                Vector3 pos = ScOutpostRisk.RingSpot(w, o, r, reach);
                EncPerson np;
                EntityAlive npc = ScEnclavePeople.GenerateAway(w, e, pos, Face(pos, o.Centre), out np);
                if (npc == null || np == null) break;
                Hostile(e, npc);
                Join(party, np, npc, target, ids);
                made++;
                fresh++;
            }
            if (made == 0 && seen + busy > 0)
            {
                // nobody came only because those who could are in view at home or their bodies are still being built: not
                // "nobody left to send" - the next second (logged once a raid)
                ScLog.OnceOut("encraids-wait-" + o.Id + "-" + o.AttackUntil, "enclave " + e.Id + " '" + e.Name + "' raids outpost " + o.Id + " (" + o.Name + "): nobody yet ("
                              + (seen > 0 ? seen + " in view at home" : "") + (seen > 0 && busy > 0 ? ", " : "") + (busy > 0 ? busy + " with a body still being built" : "")
                              + ") - tried again each second");
                return -1;
            }
            if (made > 0) parties[o.Id] = party;
            if (fresh > 0 || moved > 0) ScRegistry.SaveAtomic();
            ScLog.Out("enclave " + e.Id + " '" + e.Name + "' raids outpost " + o.Id + " (" + o.Name + "): " + made + " of their people" + (fresh > 0 ? " (" + fresh + " met for the first time)" : "")
                      + " of " + want + " wanted (attack size " + count + ", party max " + Math.Max(1, ScConfig.EnclaveRaidPartyMax) + ", " + spare + " to spare: " + home + " at home, "
                      + unmade + " not met yet)" + (moved > 0 ? "; " + moved + " left their building for it" : "") + (seen > 0 ? "; " + seen + " stayed home, in view" : ""));
            return made;
        }

        /// <summary>
        /// ScOutpostRisk.Tick, before a raid's fight is judged won: the party's people who are raiding with no body (theirs
        /// went alive - an unload, the shutdown) come back round the outpost. One raiding with a body is in the fight already:
        /// raiding comes only with a raid body (Join), and E2 never builds a body at home for a raiding person (NextToSpawn
        /// wants StateHome). Returns how many; a tell when any did.
        /// </summary>
        internal static int Regroup(World w, EntityPlayerLocal player, Outpost o, List<int> ids)
        {
            Party party;
            if (w == null || !parties.TryGetValue(o.Id, out party)) return 0;
            Enclave e = ScEnclaves.ById(party.EnclaveId);
            if (e == null || e.Lost || o.RaidEnclave != e.Id) return 0;
            GameRandom r = w.GetGameRandom();
            float reach = ScOutpostRisk.Reach(o);
            EntityAlive target = Target(w, o) ?? player;
            int n = 0;
            foreach (int pid in party.Pids.ToArray())
            {
                EncPerson p = PersonById(pid);
                if (p == null || !p.Raiding || p.EntityId >= 0) continue;
                if (p.LastEntityId >= 0 && ScNpc.IsPending(w, p.LastEntityId)) continue;
                EntityAlive npc = Place(w, e, p, o, r, reach);
                if (npc == null) continue;
                Arm(npc, target, ids);
                n++;
            }
            if (n > 0)
            {
                if (player != null) ScCommunity.Tell(player, string.Format(Loc("scEncRaidHere"), o.Name, n, e.Name), "ui_denied");
                ScLog.Out("enclave " + e.Id + " '" + e.Name + "': " + n + " of the party raiding outpost " + o.Id + " (" + o.Name + ") are back round it");
            }
            return n;
        }

        /// <summary>The party a raiding person is in, while its raid is on (the outpost held, raided by that group, under attack); null otherwise.</summary>
        private static Party PartyOf(int pid, out Outpost o)
        {
            o = null;
            foreach (Party party in parties.Values)
            {
                if (!party.Pids.Contains(pid)) continue;
                Outpost at = ScOutposts.ById(party.OutpostId);
                if (at == null || !at.Holding || at.RaidEnclave != party.EnclaveId || !ScOutpostRisk.UnderAttack(at)) return null;
                o = at;
                return party;
            }
            return null;
        }

        /// <summary>
        /// ScEnclavePeople's tick for a raiding body: while its raid is on, its attack target is given again (every
        /// AssertSeconds, when it has none: a stand-in, else the player while they are at the fight); once the raid is over,
        /// home, and the body walks off (GoHome). True when the record changed.
        /// </summary>
        internal static bool TickBody(World w, Enclave e, EncPerson p, EntityAlive npc)
        {
            Outpost o;
            Party party = PartyOf(p.Id, out o);
            if (party == null)
            {
                GoHome(w, e, p, npc, "their raid is over");
                return true;
            }
            float t = Time.unscaledTime, next;
            if (nextAssert.TryGetValue(npc.entityId, out next) && t < next) return false;
            nextAssert[npc.entityId] = t + AssertSeconds;
            EntityAlive cur = npc.GetAttackTarget();
            if (cur != null && !cur.IsDead()) return false;
            EntityAlive target = Target(w, o);
            if (target != null) npc.SetAttackTarget(target, AttackTargetTicks);
            return false;
        }

        /// <summary>
        /// A raiding person home again with their body out: the SCEnclave package (as E5's walk home gives it) and E3's walk
        /// home - not watched, and gone once nobody sees it (ScEnclavePeople's tick, ScEnclaveDeals.TickLeaving).
        /// </summary>
        private static void GoHome(World w, Enclave e, EncPerson p, EntityAlive npc, string why)
        {
            p.State = EncPerson.StateHome;
            if (npc != null) nextAssert.Remove(npc.entityId);
            if (w != null && e != null && npc != null && !npc.IsDead())
            {
                ScEnclavePeople.AddPackage(npc);
                ScEnclaveDeals.StartWalkHome(w, e, p, npc);
            }
            ScLog.Out(p.Name + " (p" + p.Id + ") of " + (e != null ? e.Name : "enclave " + p.EnclaveId) + " heads home from the raid (" + why + ")");
        }

        /// <summary>Every raiding person of the party home (their bodies walk off); a group left with nobody leading gets a leader.</summary>
        private static void SendPartyHome(Party party, Enclave e, string why)
        {
            World w = ScCommunity.World;
            foreach (int pid in party.Pids)
            {
                EncPerson p = PersonById(pid);
                if (p == null || !p.Raiding) continue;
                EntityAlive npc = ScEnclavePeople.OwnBody(p);
                GoHome(w, e, p, npc, why);
            }
            if (e != null && ScEnclaves.All.Contains(e)) ScEnclavePeople.EnsureLeader(e);
        }

        // ------------------------------------------------------------------ bodies going and dying (ScEnclavePeople) ----

        /// <summary>
        /// ScEnclavePeople.Unbody, for a raiding person: a body that died leaves the rest to the death (OnDied); one that went
        /// alive (an unload, the shutdown, gone from the world) leaves them raiding off the map until the raid ends or Regroup.
        /// One no party holds any more (its group fell: OnDenFalling) is just gone.
        /// </summary>
        internal static void OnUnbodied(EncPerson p, EntityAlive own)
        {
            if (own != null) nextAssert.Remove(own.entityId);
            if (own != null && own.IsDead()) return;
            if (!InParty(p.Id)) return;
            ScLog.Verbose(p.Name + " (p" + p.Id + ") of enclave " + p.EnclaveId + " is off the map with their raid party");
        }

        private static bool InParty(int pid)
        {
            foreach (Party party in parties.Values) if (party.Pids.Contains(pid)) return true;
            return false;
        }

        /// <summary>
        /// ScEnclavePeople.Died, for one who died raiding: a loss of the raid on their party's outpost (Outpost.RaidLost, saved:
        /// a reload mid-raid keeps it; told when the raid ends). The caller saves.
        /// </summary>
        internal static void OnDied(Enclave e, EncPerson p)
        {
            foreach (Party party in parties.Values)
            {
                if (!party.Pids.Contains(p.Id)) continue;
                Outpost o = ScOutposts.ById(party.OutpostId);
                if (o == null || o.RaidEnclave != party.EnclaveId) return;
                o.RaidLost++;
                ScLog.Verbose("enclave raid on outpost " + party.OutpostId + ": " + p.Name + " of " + party.EnclaveName + " died - " + o.RaidLost + " lost so far");
                return;
            }
        }

        /// <summary>
        /// ScEnclavePeople.DropPeople, before a group's records go (it fell; sc enclave reseed): its demand is dropped and a raid
        /// not yet out called off ("The X are gone ..."); a party out fights on as plain raiders - a raider's body loses the
        /// enclave's cvars and stays in the fight (ScEnclavePeople.Abandon), any other goes with the group - and the raid's
        /// losses (Outpost.RaidLost, also those of a fight before a reload) are told. The stamps come off. The caller saves.
        /// </summary>
        internal static void OnDenFalling(Enclave e)
        {
            if (e == null) return;
            EntityPlayerLocal pl = ScCommunity.LocalPlayer;
            foreach (Outpost o in ScRegistry.Outposts)
            {
                if (o.RaidEnclave != e.Id) continue;
                Party party;
                if (parties.TryGetValue(o.Id, out party))
                {
                    parties.Remove(o.Id);          // first: a body abandoned below is no party's any more (OnUnbodied)
                    int kept = 0;
                    foreach (int pid in party.Pids)
                    {
                        EncPerson p = PersonById(pid);
                        if (p == null || !p.Raiding) continue;
                        EntityAlive npc = ScEnclavePeople.OwnBody(p);
                        if (npc == null || npc.IsDead() || !ScXnpc.IsRaider(npc)) continue;     // the rest go with the group (DropPeople)
                        ScEnclavePeople.Abandon(p, npc);
                        kept++;
                    }
                    TellLosses(pl, party.EnclaveName, o.RaidLost, o);
                    ScLog.Out("enclave " + e.Id + " '" + e.Name + "' is gone while raiding outpost " + o.Id + " (" + o.Name + "): " + o.RaidLost + " of theirs lost, "
                              + kept + " fight on as plain raiders");
                }
                else
                {
                    bool demand = ScOutpostRisk.HasDemand(o), raid = ScOutpostRisk.UnderAttack(o) && o.AttackRaiders;
                    if (demand) o.DemandUntil = -1;
                    if (raid) ScOutpostRisk.CallOff(o);
                    if ((demand || raid) && pl != null) ScCommunity.Tell(pl, string.Format(Loc("scEncRaidGone"), e.Name, o.Name), "");
                    if (raid) TellLosses(pl, e.Name, o.RaidLost, o);           // a raid fought before a reload
                    ScLog.Out("enclave " + e.Id + " '" + e.Name + "' is gone - " + (raid ? "its raid call on " : demand ? "its demand on " : "its stamp on ") + "outpost " + o.Id + " (" + o.Name + ") is off"
                              + (o.RaidLost > 0 ? " (" + o.RaidLost + " of theirs lost in it)" : ""));
                }
                Unstamp(o);
            }
        }

        /// <summary>ScOutpostRisk.StartAttack: a party still out at that outpost (a second call over it, the console) goes home first.</summary>
        internal static void OnAttackStart(Outpost o)
        {
            Party party;
            if (o == null || !parties.TryGetValue(o.Id, out party)) return;
            SendPartyHome(party, ScEnclaves.ById(party.EnclaveId), "a new attack was called on the outpost");
            parties.Remove(o.Id);
        }

        /// <summary>ScOutpostRisk.Forget (the outpost was lost or disbanded): a party out goes home, the group rests, the raid's losses are told, the stamp off.</summary>
        internal static void OnOutpostGone(Outpost o)
        {
            if (o == null) return;
            Party party;
            bool had = parties.TryGetValue(o.Id, out party);
            Enclave e = EnclaveOf(o) ?? (had ? ScEnclaves.ById(party.EnclaveId) : null);
            if (!had && o.RaidEnclave <= 0) return;
            if (had)
            {
                SendPartyHome(party, e, "the outpost is gone");
                parties.Remove(o.Id);
            }
            Rest(e);
            if (o.RaidEnclave > 0) TellLosses(ScCommunity.LocalPlayer, e != null ? e.Name : had ? party.EnclaveName : "", o.RaidLost, o);
            if (o.RaidEnclave > 0 || had)
                ScLog.Out("outpost " + o.Id + " (" + o.Name + ") is gone - " + (e != null ? "'" + e.Name + "'" : "the group") + (had ? " go home from their raid" : " drop their demand or raid")
                          + (o.RaidLost > 0 ? " (" + o.RaidLost + " of theirs lost in it)" : ""));
            Unstamp(o);
        }

        // ------------------------------------------------------------------ the end ----

        /// <summary>
        /// ScOutpostRisk.Won (result "won") and Settle (held, damaged or overrun; attended when their party was out): the raid is
        /// over. Unattended and held, the party they would have sent loses EnclaveRaidLossHeld (LoseHeld) - unless some of
        /// theirs already died fighting the player there before a reload (Outpost.RaidLost: that fight was its cost, the
        /// review's L2); otherwise those killed died in the fight. Survivors home (a body out walks off), the group rests, one
        /// tell and ledger line for their losses (Outpost.RaidLost, saved), the log. Nothing for a nameless raid. The caller saves.
        /// </summary>
        internal static void End(Outpost o, EntityPlayerLocal player, bool attended, string result)
        {
            if (o == null) return;
            Party party;
            bool had = parties.TryGetValue(o.Id, out party);
            if (o.RaidEnclave <= 0)
            {
                if (had)
                {
                    SendPartyHome(party, ScEnclaves.ById(party.EnclaveId), "the raid is over");
                    parties.Remove(o.Id);
                }
                return;
            }
            Enclave e = ScEnclaves.ById(o.RaidEnclave);
            string name = e != null ? e.Name : had ? party.EnclaveName : "";
            int lost = o.RaidLost;
            Unstamp(o);                  // first: a fall the losses below cause finds no raid of theirs open
            if (had)
            {
                SendPartyHome(party, e, "the raid is over: " + result);
                parties.Remove(o.Id);
            }
            int held = 0;
            bool fought = lost > 0;      // unattended: some of theirs died fighting the player there before a reload
            if (!attended && result == ScOutpostRisk.HeldResult && e != null)
            {
                if (fought)
                    ScLog.Out("enclave raid on outpost " + o.Id + " (" + o.Name + ") by '" + name + "': held with nobody there at its end, but " + lost
                              + " of theirs died fighting there before a reload - no more are lost for the hold");
                else held = LoseHeld(e, o, player);
            }
            lost += held;
            bool standing = e != null && ScEnclaves.All.Contains(e);
            if (standing) Rest(e);
            TellLosses(player, name, lost, o);
            ScLog.Out("enclave raid on outpost " + o.Id + " (" + o.Name + ") by '" + name + "' is over: " + result + (attended ? "" : fought ? " (nobody came back to fight it)" : " (nobody came to fight it)")
                      + ", " + lost + " of theirs lost" + (held > 0 ? " (" + held + " as the outpost held)" : "") + (standing ? "; they rest until day " + e.RaidDay : ""));
        }

        private static void TellLosses(EntityPlayerLocal player, string name, int lost, Outpost o)
        {
            if (lost <= 0 || string.IsNullOrEmpty(name)) return;
            if (player != null) ScCommunity.Tell(player, string.Format(Loc("scEncRaidLosses"), name, lost, o.Name), "");
            ScOutposts.NoteLedger(string.Format(Loc("scEncRaidLedgerLosses"), name, lost, o.Name));
        }

        /// <summary>
        /// An unattended raid the outpost held: EnclaveRaidLossHeld of the party they would have sent (min(attack size,
        /// EnclaveRaidPartyMax, those to spare)) die - records first (ScEnclavePeople.KillRecord), at random among the
        /// non-leaders at home whose body is not out or is out where nobody sees it (the player at their den: KillRecord takes
        /// it away, no corpse; one in view stays - the review's L3), then the people not made yet (Enclave.RaidLost: the group is
        /// made that many smaller). Returns how many.
        /// </summary>
        private static int LoseHeld(Enclave e, Outpost o, EntityPlayerLocal player)
        {
            int home, unmade;
            int spare = Spare(e, out home, out unmade);
            int party = Math.Min(Math.Min(ScOutpostRisk.AttackerCountFor(o, player), Math.Max(1, ScConfig.EnclaveRaidPartyMax)), spare);
            int n = Math.Min(Math.Max(0, ScConfig.EnclaveRaidLossHeld), party);
            if (n <= 0) return 0;
            World w = ScCommunity.World;
            GameRandom r = w != null ? w.GetGameRandom() : null;
            var pool = new List<EncPerson>();
            foreach (EncPerson p in ScRegistry.EncPeople)
            {
                if (p.EnclaveId != e.Id || p.State != EncPerson.StateHome || p.IsLeader) continue;
                if (p.EntityId >= 0)
                {
                    EntityAlive own = ScEnclavePeople.OwnBody(p);
                    if (own == null || own.IsDead() || w == null || ScEnclavePeople.SeenByAny(w, own.position)) continue;
                }
                pool.Add(p);
            }
            int done = 0;
            while (done < n && pool.Count > 0 && ScEnclaves.All.Contains(e))
            {
                int i = r != null ? r.RandomRange(0, pool.Count) : 0;
                EncPerson p = pool[i];
                pool.RemoveAt(i);
                ScEnclavePeople.KillRecord(e, p, "lost raiding " + o.Name);
                done++;
            }
            int never = Math.Min(n - done, unmade);
            if (never > 0 && ScEnclaves.All.Contains(e))
            {
                e.RaidLost += never;
                done += never;
                ScEncMoves.NoteLoss(e);         // E11: a loss all the same (request clock)
                ScLog.Out("enclave " + e.Id + " '" + e.Name + "': " + never + " of the people not made yet lost raiding " + o.Name + " - the group will be made " + e.RaidLost + " smaller");
            }
            return done;
        }

        // ------------------------------------------------------------------ the clock, the console ----

        /// <summary>ScEnclaves.OnWorldDayBack: the request clock went back reqBack days - every group's rest keeps its span. Returns how many moved.</summary>
        internal static int ShiftDays(int reqBack)
        {
            if (reqBack <= 0) return 0;
            int n = 0;
            foreach (Enclave e in ScEnclaves.All)
                if (e.RaidDay > 0) { e.RaidDay = Math.Max(0, e.RaidDay - reqBack); n++; }
            return n;
        }

        /// <summary>sc enclave people's words for a raiding person: where, and whether their body is in the fight.</summary>
        public static string WhereText(EncPerson p)
        {
            Outpost o;
            Party party = PartyOf(p.Id, out o);
            if (party == null) return "no raid open";
            return "at " + o.Name + ", outpost " + o.Id + (p.EntityId >= 0 ? "" : ", off the map");
        }

        private static string NowText(Enclave e)
        {
            Outpost o = TargetOf(e);
            if (o == null) return "none";
            Party party;
            if (parties.TryGetValue(o.Id, out party))
            {
                int inFight = 0, off = 0;
                foreach (int pid in party.Pids)
                {
                    EncPerson p = PersonById(pid);
                    if (p == null || !p.Raiding) continue;
                    if (p.EntityId >= 0) inFight++; else off++;
                }
                return "raiding " + o.Name + " (outpost " + o.Id + "): " + inFight + " in the fight" + (off > 0 ? ", " + off + " off the map" : "") + ", " + o.RaidLost + " lost";
            }
            if (ScOutpostRisk.UnderAttack(o) && o.AttackRaiders)
                return "a raid on " + o.Name + " (outpost " + o.Id + ") is called, " + ScOutpostRisk.HoursLeft(o) + " h to get there" + (o.RaidLost > 0 ? ", " + o.RaidLost + " lost" : "");
            return "a demand on " + o.Name + " (outpost " + o.Id + "): " + o.DemandAmmo + " ammo + " + o.DemandFood + " food, to answer by the rollover after day " + o.DemandUntil;
        }

        /// <summary>"raids: ..." for sc enclave info.</summary>
        public static string InfoLine(Enclave e)
        {
            string s;
            if (!e.IsHostile) s = "never (they are not hostile)";
            else if (!Configured) s = "off (" + OffWhy() + ")";
            else if (e.Lost) s = "none (their building is not in this world)";
            else
            {
                int home, unmade;
                int spare = Spare(e, out home, out unmade);
                int left = DaysLeft(e);
                var near = new List<string>();
                foreach (Outpost o in ScRegistry.Outposts)
                {
                    if (!o.Holding) continue;
                    float d = RangeTo(e, o);
                    if (d <= ScConfig.EnclaveRaidRange) near.Add(o.Name + " " + Mathf.RoundToInt(d) + " m");
                }
                s = (ChanceFor(e) * 100f).ToString("0.#", CultureInfo.InvariantCulture) + "% a day on an outpost within " + ScConfig.EnclaveRaidRange + " m ("
                    + (near.Count > 0 ? string.Join(", ", near.ToArray()) : "none of yours") + "), " + spare + " to spare (" + home + " at home"
                    + (unmade > 0 ? ", " + unmade + " not met yet" : "") + "; the leader never raids)"
                    + (e.RaidLost > 0 ? ", " + e.RaidLost + " lost before you met them" : "")
                    + (left > 0 ? ", resting until day " + e.RaidDay + " (" + left + " day(s))" : ", ready")
                    + "; now: " + NowText(e);
            }
            return "raids: " + s + " (request clock day " + ScEncRequests.Now + ")";
        }

        /// <summary>Why the console can't make this group's demand on this outpost now (null: it can): WhyNot without the rest, the outpost's state, the range.</summary>
        private static string CanDemandAt(Enclave e, Outpost o)
        {
            string why = WhyNot(e, true);
            if (why != null) return why;
            if (o.State != OutpostState.Active) return o.Name + " is not active (" + o.State + ")";
            if (ScOutpostRisk.UnderAttack(o)) return o.Name + " is under attack already";
            if (ScOutpostRisk.HasDemand(o)) return o.Name + " has a demand open already";
            float d = RangeTo(e, o);
            if (d > ScConfig.EnclaveRaidRange) return o.Name + " is " + Mathf.RoundToInt(d) + " m from their building, beyond EnclaveRaidRange (" + ScConfig.EnclaveRaidRange + " m)";
            return null;
        }

        /// <summary>
        /// sc enclave raid &lt;id&gt; [&lt;outpost id&gt; [demand|now]]: their raids' line and each outpost's distance; one outpost's
        /// distance and whether a demand can be made; a demand now (the day's roll, their rest, the gamestage and an outpost's
        /// quiet days ignored; the range kept), or a demand refused at once (the raid called, heard: the console is the radio).
        /// </summary>
        public static void Command(Enclave e, List<string> p, List<string> into)
        {
            EntityPlayerLocal pl = ScCommunity.LocalPlayer;
            if (p.Count <= 3)
            {
                into.Add(e.Name + " (enclave " + e.Id + ", " + ScEnclaves.StandingWord(e) + "): " + InfoLine(e));
                int n = 0;
                foreach (Outpost o in ScRegistry.Outposts)
                {
                    if (!o.Holding) continue;
                    float d = RangeTo(e, o);
                    into.Add("  outpost " + o.Id + " " + o.Name + ": " + Mathf.RoundToInt(d) + " m from their building" + (d <= ScConfig.EnclaveRaidRange ? "" : " (out of range)"));
                    n++;
                }
                if (n == 0) into.Add("  you hold no outposts");
                return;
            }
            int oid;
            Outpost t = int.TryParse(p[3], out oid) ? ScOutposts.ById(oid) : null;
            if (t == null) { into.Add("no outpost " + p[3] + " (sc outpost list)"); return; }
            string verb = p.Count > 4 ? p[4].ToLowerInvariant() : "";
            string why = CanDemandAt(e, t);
            if (verb == "")
            {
                into.Add(e.Name + " (enclave " + e.Id + ", " + ScEnclaves.StandingWord(e) + "): " + InfoLine(e));
                into.Add("  outpost " + t.Id + " " + t.Name + ": " + Mathf.RoundToInt(RangeTo(e, t)) + " m from their building (raid range " + ScConfig.EnclaveRaidRange
                         + " m); a demand now: " + (why ?? "can demand"));
                return;
            }
            if (verb != "demand" && verb != "now") { into.Add("sc enclave raid <id> [<outpost id> [demand|now]]"); return; }
            if (why != null) { into.Add("refused: " + why); return; }
            Demand(t, e, ScCommunity.Today, pl, null, "the console");
            if (verb == "demand")
            {
                into.Add(e.Name + " demand " + t.DemandAmmo + " ammo + " + t.DemandFood + " food for " + t.Name + " (answer on the radio, or sc outpost pay|refuse " + t.Id + ")");
                return;
            }
            string msg;
            ScOutpostRisk.RefuseDemand(t, pl, out msg);
            into.Add("refused at once: " + msg);
        }
    }
}
