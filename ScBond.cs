using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round U4 (2026-11-16, decision D7 "SC owns it"; research/npc306/part_relationship.md section 4.3): each member's Bond with
    /// the community, a term of her mood (ScMorale.Recompute) fed by 0-XNPCCore 3.4.06's relationship events.
    ///
    /// The events and their sizes (the user's, ScConfig.Bond*): a gift, its 0-XNPCCore points 1:1 (log2 of the gift's value, at
    /// most 6 a day per member - 0-XNPCCore's own daily cap); she is revived +3, whoever revives her; a blood moon survived
    /// together +4; she goes down -2; you kill an enemy that was on her +1. Bond is capped at +-BondCap (15) and fades
    /// BondDecayPerDay (1) toward 0 at every day rollover (Daily). Saved in her record as bond= (ScRegistry; old saves load 0).
    ///
    /// Where each comes from:
    ///  - a gift: 0-XNPCCore's RelationshipState.Apply with the signal "gift" (Patches_Relationship.Patch_Apply), after its gift bag
    ///    took the item - every gift route (the hold-E ring, the voice, the gift window) ends there;
    ///  - a down and a revive: 0-XNPCCore's NPCEventMemoryHooks.OnDowned / OnRevived (Patch_Downed, Patch_Revived), which its downed
    ///    state calls for every body, whoever revives her - 0-XNPCCore's own relationship then asks for her leader as the reviver;
    ///  - a blood moon: SC's own check here (Tick), for every member with the community at its start who is alive at its end and
    ///    was with you - following you, or at home while you were home for it at any moment. 0-XNPCCore's own sees only followers;
    ///  - a kill on her: SC's own (OnEntityKilled, from ModEvents.EntityKilled): you killed an enemy that was after her, or that she
    ///    was fighting or had been hit by. 0-XNPCCore's own sees only the hired roster, which SC's J29 keeps residents out of.
    /// For SC's people every other 0-XNPCCore write is skipped (Patch_Apply): no per-hire trust or affinity, no ripple into your
    /// global trust (D8), and a member's death does not cost your global trust either (Patch_ApplyGlobal) - SC charges its own
    /// DeathPenalty. 0-XNPCCore's Progress line ("Trust: x  Feels: y") reads Mirror: Feels from her mood, Trust from her Bond.
    ///
    /// Purity (the round's rule): the hooks only queue an event (Note); Tick, on the ticker, writes it into her record.
    /// </summary>
    public static class ScBond
    {
        public const string SigGift = "gift", SigRevive = "revive", SigDown = "down", SigBloodMoon = "bloodmoon", SigKill = "kill", SigConsole = "console";

        private struct Pending
        {
            public int MemberId;
            public int Delta;
            public string Signal;
            public string Why;
        }

        private static readonly object gate = new object();
        private static readonly List<Pending> queue = new List<Pending>();
        /// <summary>Session only: each member's last few Bond lines, newest first (sc bond).</summary>
        private static readonly Dictionary<int, List<string>> history = new Dictionary<int, List<string>>();
        private const int HistoryKeep = 8;
        private static readonly Dictionary<string, int> counts = new Dictionary<string, int>();
        private static float nextTick, nextSweep, nextSave;
        /// <summary>A Bond change not saved yet: the registry is saved at most every SaveSeconds for them (a fight's kills).</summary>
        private static bool unsaved;
        private const float SaveSeconds = 10f;
        /// <summary>The blood moon in progress: who was with the community at its start, and whether you were home for it.</summary>
        private static bool wasBloodMoon, playerHome;
        private static readonly HashSet<int> bloodMoonMembers = new HashSet<int>();
        /// <summary>Members whose body was checked for 0-XNPCCore's own per-hire numbers this session (Sweep's log line once each).</summary>
        private static readonly HashSet<int> swept = new HashSet<int>();

        /// <summary>0-XNPCCore's per-hire relationship cvars (RelationshipRules): its gift bookkeeping (vcRelGiftDay/Today) stays - its daily cap.</summary>
        private static readonly string[] PerHireCvars = { "vcRelTrustMod", "vcRelAffinityMod", "vcRelScar" };

        public static void Reset()
        {
            lock (gate) queue.Clear();
            history.Clear();
            counts.Clear();
            nextTick = 0f;
            nextSweep = 0f;
            nextSave = 0f;
            unsaved = false;
            wasBloodMoon = false;
            playerHome = false;
            bloodMoonMembers.Clear();
            swept.Clear();
        }

        internal static void Count(string what)
        {
            lock (gate)
            {
                int n;
                counts.TryGetValue(what, out n);
                counts[what] = n + 1;
            }
        }

        public static string Report()
        {
            lock (gate)
            {
                if (counts.Count == 0) return "nothing yet";
                var keys = new List<string>(counts.Keys);
                keys.Sort(StringComparer.Ordinal);
                var parts = new List<string>();
                foreach (string k in keys) parts.Add(k + "=" + counts[k]);
                return string.Join(" ", parts.ToArray());
            }
        }

        // ------------------------------------------------------------------ who ----

        /// <summary>
        /// The member a body is: her own record, or the member an outpost or rescue stand-in stands in for. Null for anyone else
        /// (strangers, enclave and band people, an enlisted enclave person, raiders). A read.
        /// </summary>
        public static Member MemberOf(EntityAlive e)
        {
            if (e == null || e.Buffs == null || !ScRegistry.Loaded || !Patches_VoiceControl.IsCommunityNpc(e)) return null;
            if (e.Buffs.HasCustomVar(ScRadio.StandInCvar)) return ScRegistry.GetMember((int)e.Buffs.GetCustomVar(ScRadio.StandInCvar));
            return ScRegistry.GetMember(e.entityId);
        }

        // ------------------------------------------------------------------ events ----

        /// <summary>
        /// A Bond event from any hook: queued only, and written into her record by Tick on the ticker within a second (the round's
        /// purity rule). A gift worth 0 points (0-XNPCCore's daily cap reached) is queued too, so sc bond shows it.
        /// </summary>
        public static void Note(int memberId, int delta, string signal, string why)
        {
            if (memberId < 0) return;
            lock (gate) queue.Add(new Pending { MemberId = memberId, Delta = delta, Signal = signal ?? "?", Why = why ?? "" });
        }

        /// <summary>
        /// ModEvents.EntityKilled (registered in ModApi; the host): an enemy killed by a player that was on a member - after her (its
        /// attack target), or her own attack or revenge target (0-XNPCCore's own test) - is +BondKillOnHer for her. Every member's
        /// body in the world counts, her outpost or rescue stand-in included. Queued (Note).
        /// </summary>
        public static void OnEntityKilled(Entity killed, Entity killer)
        {
            try
            {
                if (!ScCommunity.IsHost || !ScRegistry.Loaded || !(killer is EntityPlayer)) return;
                EntityAlive victim = killed as EntityAlive;
                World w = ScCommunity.World;
                if (victim == null || w == null || Patches_VoiceControl.IsCommunityNpc(victim)) return;
                EntityAlive after = victim.GetAttackTarget();
                foreach (Member m in ScRegistry.Members.Values)
                {
                    bool standIn;
                    EntityAlive body = ScAmmo.BodyOf(w, m, out standIn);
                    if (body == null || body.IsDead()) continue;
                    bool on = ReferenceEquals(after, body) || ReferenceEquals(body.GetAttackTarget(), victim) || ReferenceEquals(body.GetRevengeTarget(), victim);
                    if (!on) continue;
                    Note(m.Id, ScConfig.BondKillOnHer, SigKill, "you killed the " + ScCommunity.ClassName(victim) + " that was on " + ScMedic.HerObj(m));
                }
            }
            catch (Exception e)
            {
                ScLog.Once("bond-kill", e);
            }
        }

        // ------------------------------------------------------------------ the ticker ----

        /// <summary>
        /// ScTicker, every second (host, registry loaded): the queued events into the records (the cap, a log line, sc bond's
        /// history), then the blood moon check, and every 10 s 0-XNPCCore's own per-hire numbers taken off members' bodies (SC owns
        /// them: decision D7). A change recomputes morale and moods at once; the registry is saved then, at most every 10 s (a
        /// fight's kills on her come a second apart; the world's own save and the rollover save it too).
        /// </summary>
        public static void Tick(World w, EntityPlayerLocal player)
        {
            try
            {
                if (w == null || player == null || !ScRegistry.Loaded) return;
                float now = Time.unscaledTime;
                if (now < nextTick) return;
                nextTick = now + 1f;
                bool changed = Drain();
                BloodMoon(w, player);
                if (now >= nextSweep)
                {
                    nextSweep = now + 10f;
                    Sweep();
                }
                if (changed)
                {
                    ScMorale.Recompute();
                    unsaved = true;
                }
                if (!unsaved || now < nextSave) return;
                unsaved = false;
                nextSave = now + SaveSeconds;
                ScRegistry.SaveAtomic();
            }
            catch (Exception e)
            {
                ScLog.Once("bond-tick", e);
            }
        }

        private static bool Drain()
        {
            List<Pending> batch;
            lock (gate)
            {
                if (queue.Count == 0) return false;
                batch = new List<Pending>(queue);
                queue.Clear();
            }
            bool changed = false;
            int cap = Math.Max(0, ScConfig.BondCap);
            foreach (Pending p in batch)
            {
                Member m = ScRegistry.GetMember(p.MemberId);
                if (m == null) { Count("bond-member-gone"); continue; }
                if (!ScConfig.MemberBond && p.Signal != SigConsole)
                {
                    Count("bond-off-" + p.Signal);
                    Remember(m, p.Why + " (MemberBond is off: no Bond)");
                    continue;
                }
                int before = m.Bond;
                int after = Mathf.Clamp(before + p.Delta, -cap, cap);
                m.Bond = after;
                Count("bond-" + p.Signal);
                string line = p.Why + " " + Signed(p.Delta) + ": " + Signed(before) + " -> " + Signed(after)
                              + (before + p.Delta != after ? " (capped at +-" + cap + ")" : "");
                Remember(m, line);
                if (after == before) { ScLog.Verbose(m.Name + "'s Bond: " + line); continue; }   // at the cap (a horde's kills): no log line each
                ScLog.Out(m.Name + "'s Bond: " + line);
                changed = true;
            }
            return changed;
        }

        private static void Remember(Member m, string line)
        {
            List<string> h;
            if (!history.TryGetValue(m.Id, out h)) { h = new List<string>(); history[m.Id] = h; }
            h.Insert(0, "day " + ScCommunity.Today + ": " + line);
            while (h.Count > HistoryKeep) h.RemoveAt(h.Count - 1);
        }

        /// <summary>
        /// The blood moon survived together: at its start the members with the community (a resident, a guard on watch, a follower
        /// - not one away on a run, posted at an outpost or still on the way) are noted; while it lasts, whether you are ever home
        /// (ScBaseAttack.Near: the distance a base attack is fought from). At its end each of them still a member - alive - who
        /// is following you, or is a resident while you were home for it, gets +BondBloodMoon. A load in the middle of it notes
        /// them from then on; a blood moon whose end was not seen here (quit before dawn) gives nothing.
        /// </summary>
        private static void BloodMoon(World w, EntityPlayerLocal player)
        {
            if (ScSiege.BloodMoonActive(w))
            {
                if (!wasBloodMoon)
                {
                    wasBloodMoon = true;
                    playerHome = false;
                    bloodMoonMembers.Clear();
                    foreach (Member m in ScRegistry.Members.Values)
                        if (m.Status == MemberStatus.Resident || m.Status == MemberStatus.Following || m.Status == MemberStatus.PendingStation) bloodMoonMembers.Add(m.Id);
                    ScLog.Out("Bond: a blood moon begins - " + bloodMoonMembers.Count + " member(s) with the community");
                }
                if (!playerHome && ScZone.HasHome && ScBaseAttack.Near(player))
                {
                    playerHome = true;
                    ScLog.Out("Bond: you are home for the blood moon - the residents who survive it are with you");
                }
                return;
            }
            if (!wasBloodMoon) return;
            wasBloodMoon = false;
            int given = 0;
            foreach (int id in bloodMoonMembers)
            {
                Member m = ScRegistry.GetMember(id);
                if (m == null) continue;                                        // died, or left the community
                bool follower = m.Status == MemberStatus.Following || m.Status == MemberStatus.PendingStation;
                bool home = m.Status == MemberStatus.Resident && playerHome;
                if (!follower && !home)
                {
                    Remember(m, "a blood moon survived, but not with you (" + m.Status + (m.Status == MemberStatus.Resident ? ", you were away from home" : "") + "): no Bond");
                    continue;
                }
                Note(id, ScConfig.BondBloodMoon, SigBloodMoon, follower ? "survived the blood moon with you" : "survived the blood moon at home with you");
                given++;
            }
            ScLog.Out("Bond: the blood moon is over - " + given + " of " + bloodMoonMembers.Count + " member(s) survived it with you");
            bloodMoonMembers.Clear();
        }

        /// <summary>
        /// Every 10 s: 0-XNPCCore's own per-hire numbers (vcRelTrustMod, vcRelAffinityMod, vcRelScar) taken off each member's body in
        /// the world - left from before she joined (a companion hired the paid way), or from a 0-XNPCCore build before this round.
        /// SC owns her standing now; its gift bookkeeping stays (the daily gift cap).
        /// </summary>
        private static void Sweep()
        {
            foreach (Member m in ScRegistry.Members.Values)
            {
                EntityAlive e = ScCommunity.FindNpc(m.Id);
                if (e == null || e.Buffs == null || e.IsDead() || !Patches_VoiceControl.IsCommunityNpc(e)) continue;
                int cleared = 0;
                for (int i = 0; i < PerHireCvars.Length; i++)
                {
                    if (!e.Buffs.HasCustomVar(PerHireCvars[i])) continue;
                    e.Buffs.RemoveCustomVar(PerHireCvars[i]);
                    cleared++;
                }
                if (cleared == 0) continue;
                Count("bond-perhire-cleared");
                if (swept.Add(m.Id)) ScLog.Out(m.Name + ": 0-XNPCCore's own trust and affinity for " + ScMedic.HerObj(m) + " cleared (" + cleared + " cvar(s)) - SC owns " + ScMedic.Her(m) + " standing (round U4)");
            }
        }

        /// <summary>ScDaily.Run, once per day rollover, before ScMorale.Daily: every member's Bond fades BondDecayPerDay toward 0.</summary>
        public static void Daily(int day)
        {
            try
            {
                int d = Math.Max(0, ScConfig.BondDecayPerDay), cap = Math.Max(0, ScConfig.BondCap);
                var faded = new List<string>();
                foreach (Member m in ScRegistry.Members.Values)
                {
                    int before = m.Bond;
                    int after = before > 0 ? Math.Max(0, before - d) : before < 0 ? Math.Min(0, before + d) : 0;
                    after = Mathf.Clamp(after, -cap, cap);
                    if (after == before) continue;
                    m.Bond = after;
                    faded.Add(m.Name + " " + Signed(before) + " -> " + Signed(after));
                }
                if (faded.Count > 0) ScLog.Out("day " + day + ": Bond fades - " + string.Join(", ", faded.ToArray()));
            }
            catch (Exception e)
            {
                ScLog.Once("bond-daily", e);
            }
        }

        // ------------------------------------------------------------------ reads ----

        /// <summary>ScMorale.Recompute: her Bond as a mood term (0 with MemberBond off). A read.</summary>
        public static int MoodTerm(Member m)
        {
            if (m == null || !ScConfig.MemberBond) return 0;
            int cap = Math.Max(0, ScConfig.BondCap);
            return Mathf.Clamp(m.Bond, -cap, cap);
        }

        /// <summary>"Bond +4" for the talk card and the community window, "" at 0 or with MemberBond off.</summary>
        public static string Term(Member m)
        {
            int t = MoodTerm(m);
            return t == 0 ? "" : string.Format(Localization.Get("scBondTerm"), Signed(t));
        }

        /// <summary>
        /// 0-XNPCCore's Progress line for a member (Patch_TryEffective), on its scale (+-100, the words turn at +-25). Feels from her
        /// mood: mood - 49.5, so "warm" exactly while she is happy (75 and up) and "cold" while miserable (under 25). Trust from her
        /// Bond: BondLoyalAt and up reads "loyal", minus that and down "wary", and "wary" whatever her Bond while she threatens to
        /// leave. A read.
        /// </summary>
        public static void Mirror(Member m, out float trust, out float affinity)
        {
            affinity = Mathf.Clamp(m.Mood - 49.5f, -100f, 100f);
            float t = MoodTerm(m) * 25f / Math.Max(1, ScConfig.BondLoyalAt);
            if (Threatens(m)) t = Math.Min(t, -25.5f);
            trust = Mathf.Clamp(t, -100f, 100f);
        }

        private static bool Threatens(Member m)
        {
            return m != null && ScRegistry.Community.ThreatMemberId == m.Id;
        }

        /// <summary>0-XNPCCore's own words for a value (its Localization keys, English when one is missing).</summary>
        public static string TrustWord(float v)
        {
            return v <= -25f ? Word("vcRelTrustWary", "wary") : v >= 25f ? Word("vcRelTrustLoyal", "loyal") : Word("vcRelTrustSteady", "steady");
        }

        public static string FeelWord(float v)
        {
            return v <= -25f ? Word("vcRelAffinityCold", "cold") : v >= 25f ? Word("vcRelAffinityWarm", "warm") : Word("vcRelAffinityNeutral", "neutral");
        }

        private static string Word(string key, string english)
        {
            string s = Localization.Get(key);
            return string.IsNullOrEmpty(s) || s == key ? english : s;
        }

        internal static string Signed(int v) { return (v >= 0 ? "+" : "") + v.ToString(CultureInfo.InvariantCulture); }

        /// <summary>
        /// The lines of sc bond &lt;name&gt; and of Voice Control's "vc standing" on a member (Patches_Relationship.Patch_Standing):
        /// SC's view of her standing. body may be null (not loaded). A read.
        /// </summary>
        public static List<string> Lines(Member m, EntityAlive body, string prefix)
        {
            var o = new List<string>();
            float t, a;
            Mirror(m, out t, out a);
            int cap = Math.Max(0, ScConfig.BondCap);
            o.Add(prefix + m.Name + " (id " + m.Id + ", " + m.Status + "): a community member - Survivor Community owns " + ScMedic.Her(m) + " standing");
            o.Add(prefix + "  Bond " + Signed(m.Bond) + " of +-" + cap + (ScConfig.MemberBond ? ", in " + ScMedic.Her(m) + " mood as " + Signed(MoodTerm(m)) : " (MemberBond is off: not in the mood)")
                  + "; fades " + Math.Max(0, ScConfig.BondDecayPerDay) + " a day toward 0");
            o.Add(prefix + "  mood " + m.Mood + " (" + ScMorale.MoodWord(m.Mood) + "), community morale " + ScRegistry.Community.Morale
                  + (Threatens(m) ? "; THREATENS TO LEAVE" : ""));
            o.Add(prefix + "  0-XNPCCore's Progress page shows \"Trust: " + TrustWord(t) + "  Feels: " + FeelWord(a) + "\" (trust "
                  + t.ToString("0.#", CultureInfo.InvariantCulture) + " from Bond, feels " + a.ToString("0.#", CultureInfo.InvariantCulture) + " from mood)");
            lock (gate)
            {
                int pending = 0;
                foreach (Pending p in queue) if (p.MemberId == m.Id) pending++;
                if (pending > 0) o.Add(prefix + "  " + pending + " event(s) waiting for the next second");
            }
            List<string> h;
            if (history.TryGetValue(m.Id, out h) && h.Count > 0)
            {
                o.Add(prefix + "  this session (newest first):");
                foreach (string s in h) o.Add(prefix + "    " + s);
            }
            else o.Add(prefix + "  no Bond event this session");
            if (body != null && body.Buffs != null)
            {
                int day = (int)SkyManager.dayCount;
                int today = (int)body.Buffs.GetCustomVar("vcRelGiftDay") == day ? (int)body.Buffs.GetCustomVar("vcRelGiftToday") : 0;
                var stale = new List<string>();
                foreach (string c in PerHireCvars) if (body.Buffs.HasCustomVar(c)) stale.Add(c + "=" + body.Buffs.GetCustomVar(c).ToString("0.#", CultureInfo.InvariantCulture));
                o.Add(prefix + "  gift points today (0-XNPCCore's daily cap): " + today + "; its own per-hire numbers on " + ScMedic.HerObj(m) + ": "
                      + (stale.Count > 0 ? string.Join(", ", stale.ToArray()) + " (cleared within 10 s)" : "none (SC skips its writes)"));
            }
            else o.Add(prefix + "  " + ScMedic.Her(m) + " body is not loaded");
            EntityPlayerLocal p0 = ScCommunity.LocalPlayer;
            if (p0 != null && p0.Buffs != null)
                o.Add(prefix + "  your global 0-XNPCCore trust " + p0.Buffs.GetCustomVar("vcRelTrust").ToString("0.#", CultureInfo.InvariantCulture)
                      + ", affinity " + p0.Buffs.GetCustomVar("vcRelAffinity").ToString("0.#", CultureInfo.InvariantCulture)
                      + " - members never move them (decision D8); members are never billed, so no price applies");
            return o;
        }

        // ------------------------------------------------------------------ console ----

        /// <summary>
        /// sc bond: every member's Bond. sc bond &lt;id|name&gt;: one in full. sc bond &lt;id|name&gt; add &lt;n&gt; | set &lt;n&gt; | event
        /// &lt;gift [points] | revive | bloodmoon | down | kill&gt;: queued as an event (written within a second, capped). sc bond decay:
        /// one day's fade now.
        /// </summary>
        public static void ConsoleCmd(List<string> p, Action<string> output)
        {
            ScConfig.EnsureParsed();
            if (!ScRegistry.Loaded) { output("[SC] the registry is not loaded"); return; }
            if (p.Count < 2)
            {
                output("[SC] Bond (round U4): " + ScConfig.Npc306Line());
                output("[SC] counters: " + Report() + "; hooks: " + Patches_Relationship.Report());
                output("[SC] " + ScDrinks.Report());
                foreach (Member m in ScRegistry.Members.Values)
                {
                    float t, a;
                    Mirror(m, out t, out a);
                    output("[SC]   " + m.Name + " (" + m.Id + ", " + m.Status + "): Bond " + Signed(m.Bond) + ", mood " + m.Mood + " (" + ScMorale.MoodWord(m.Mood)
                           + ") - Progress \"Trust: " + TrustWord(t) + "  Feels: " + FeelWord(a) + "\"");
                }
                output("[SC] " + ScRegistry.Members.Count + " member(s) - sc bond <id|name> [add <n> | set <n> | event <gift [points]|revive|bloodmoon|down|kill>], sc bond decay");
                return;
            }
            if (p[1].Equals("decay", StringComparison.OrdinalIgnoreCase))
            {
                Daily(ScCommunity.Today);
                ScMorale.Recompute();
                ScRegistry.SaveAtomic();
                output("[SC] one day's Bond fade applied (the log has the line)");
                return;
            }
            Member mm = Find(p[1]);
            if (mm == null) { output("[SC] no member '" + p[1] + "' - sc members lists them"); return; }
            if (p.Count >= 4 && (p[2].Equals("add", StringComparison.OrdinalIgnoreCase) || p[2].Equals("set", StringComparison.OrdinalIgnoreCase)))
            {
                int n;
                if (!int.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) { output("[SC] sc bond <id|name> add|set <n>"); return; }
                bool set = p[2].Equals("set", StringComparison.OrdinalIgnoreCase);
                Note(mm.Id, set ? n - mm.Bond : n, SigConsole, set ? "sc bond set " + n : "sc bond add " + n);
                output("[SC] queued for " + mm.Name + " - written within a second (sc bond " + mm.Name + " shows it)");
                return;
            }
            if (p.Count >= 4 && p[2].Equals("event", StringComparison.OrdinalIgnoreCase))
            {
                string ev = p[3].ToLowerInvariant();
                switch (ev)
                {
                    case "gift":
                        {
                            int pts = 3;
                            if (p.Count >= 5 && !int.TryParse(p[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out pts)) pts = 3;
                            Note(mm.Id, pts * ScConfig.BondGiftPerPoint, SigGift, "a gift (" + pts + " point(s), sc bond event)");
                            break;
                        }
                    case "revive": Note(mm.Id, ScConfig.BondRevive, SigRevive, "revived (sc bond event)"); break;
                    case "bloodmoon": Note(mm.Id, ScConfig.BondBloodMoon, SigBloodMoon, "survived the blood moon with you (sc bond event)"); break;
                    case "down": Note(mm.Id, ScConfig.BondDowned, SigDown, "went down (sc bond event)"); break;
                    case "kill": Note(mm.Id, ScConfig.BondKillOnHer, SigKill, "you killed an enemy that was on " + ScMedic.HerObj(mm) + " (sc bond event)"); break;
                    default: output("[SC] sc bond <id|name> event <gift [points]|revive|bloodmoon|down|kill>"); return;
                }
                output("[SC] " + ev + " queued for " + mm.Name + " as the real event would be - written within a second");
                return;
            }
            foreach (string s in Lines(mm, ScCommunity.FindNpc(mm.Id), "[SC] ")) output(s);
        }

        private static Member Find(string key)
        {
            int id;
            if (int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out id)) return ScRegistry.GetMember(id);
            foreach (Member m in ScRegistry.Members.Values)
                if (m.Name != null && m.Name.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0) return m;
            return null;
        }
    }
}
