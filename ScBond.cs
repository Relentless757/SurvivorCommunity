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
    /// together +4; she goes down -2; you kill an enemy that was on her +1, at most BondKillPerDay (3) such kills a day (the Fable
    /// review's finding 2: the gift cap's twin, saved in her record as bondkills=). Bond is capped at +-BondCap (15) and fades
    /// BondDecayPerDay (1) toward 0 at every day rollover (Daily). Saved in her record as bond= (ScRegistry; old saves load 0).
    ///
    /// Where each comes from:
    ///  - a gift: 0-XNPCCore's RelationshipState.Apply with the signal "gift" (Patches_Relationship.Patch_Apply), after its gift bag
    ///    took the item - every gift route (the hold-E ring, the voice, the gift window) ends there;
    ///  - a down and a revive: 0-XNPCCore's NPCEventMemoryHooks.OnDowned / OnRevived (Patch_Downed, Patch_Revived), which its downed
    ///    state calls for every body, whoever revives her - 0-XNPCCore's own relationship then asks for her leader as the reviver;
    ///    those hooks run only through Voice Control's bridges, and 0-XNPCCore's "ragdoll ended while downed" revive skips its
    ///    own, so ScDowned notes both too - one "down" and at most one "revive" per down, whichever path is first (NoteDowned,
    ///    NoteRevived: the U2+U4 integration fix);
    ///  - a blood moon: SC's own check here (Tick), for every member alive at its end who was with you at both its start and its
    ///    end - following you, or a resident while you were home. 0-XNPCCore's own sees only followers;
    ///  - a kill on her: SC's own (OnEntityKilled, from ModEvents.EntityKilled): you killed an enemy that was after her, or that she
    ///    was fighting or had been hit by. 0-XNPCCore's own sees only the hired roster, which SC's J29 keeps residents out of.
    /// For SC's people every other 0-XNPCCore write is skipped (Patch_Apply): no per-hire trust or affinity, no ripple into your
    /// global trust (D8), and a member's death does not cost your global trust either (Patch_ApplyGlobal) - SC charges its own
    /// DeathPenalty. 0-XNPCCore's Progress line ("Trust: x  Feels: y") reads Mirror: Feels from her mood, Trust from her Bond.
    ///
    /// Purity (the round's rule): the hooks only queue an event (Note); Tick, on the ticker, writes it into her record.
    /// Round MP6 (part H): the queue is the world's (an event names its member, whichever community she is in); each community's
    /// clocks and blood-moon roll call are its own, and each community whose members changed recomputes its own morale.
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
        /// <summary>
        /// Round MP6 (part H): one community's Bond clocks and blood-moon roll call - one per community (ScHomeState): each sees its
        /// own blood moon through with its own players and members. The old statics below read the row of the community in scope.
        /// </summary>
        private sealed class BondState
        {
            public float NextTick, NextSweep, NextSave;
            public bool WasBloodMoon;
            public readonly HashSet<int> BloodMoonMembers = new HashSet<int>();
            /// <summary>The real time this community's Bond last ticked (-1: not yet) - a gap means it paused.</summary>
            public float LastTick = -1f;
        }
        private static readonly ScHomeState<BondState> homes = new ScHomeState<BondState>();
        private static float nextTick { get { return homes.Current.NextTick; } set { homes.Current.NextTick = value; } }
        private static float nextSweep { get { return homes.Current.NextSweep; } set { homes.Current.NextSweep = value; } }
        private static float nextSave { get { return homes.Current.NextSave; } set { homes.Current.NextSave = value; } }
        /// <summary>A Bond change not saved yet: the registry is saved at most every SaveSeconds for them (a fight's kills).</summary>
        private static bool unsaved;
        private const float SaveSeconds = 10f;
        /// <summary>The blood moon in progress: the members who were with you at its start (the Fable review's finding 5).</summary>
        private static bool wasBloodMoon { get { return homes.Current.WasBloodMoon; } set { homes.Current.WasBloodMoon = value; } }
        private static HashSet<int> bloodMoonMembers { get { return homes.Current.BloodMoonMembers; } }
        /// <summary>Round MP6 (part H): the communities whose members' Bond the last Drain changed (each recomputes its own morale).</summary>
        private static readonly HashSet<int> drained = new HashSet<int>();
        /// <summary>Members whose body was checked for 0-XNPCCore's own per-hire numbers this session (Sweep's log line once each).</summary>
        private static readonly HashSet<int> swept = new HashSet<int>();

        /// <summary>0-XNPCCore's per-hire relationship cvars (RelationshipRules): its gift bookkeeping (vcRelGiftDay/Today) stays - its daily cap.</summary>
        private static readonly string[] PerHireCvars = { "vcRelTrustMod", "vcRelAffinityMod", "vcRelScar" };

        public static void Reset()
        {
            lock (gate)
            {
                queue.Clear();
                episodes.Clear();   // the U2+U4 integration fix
            }
            history.Clear();
            counts.Clear();
            homes.Reset();                        // round MP6 (part H): every community's clocks and blood-moon roll call
            drained.Clear();
            unsaved = false;
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

        // ---- the U2+U4 integration fix (2026-11-16): one "down" and one "revive" per down, from either path ----

        /// <summary>
        /// One down of a member, for Bond. 0-XNPCCore calls its Downed and Revived hooks only through Voice Control's bridges
        /// (DownedBridge, RevivedBridge), and its "ragdoll ended while downed, treating as a revive" path skips the Revived hook,
        /// so ScDowned notes both too. Since: when the down was first noted (the hook: inside 0-XNPCCore's Enter; ScDowned: the
        /// tick it first saw her down). Under gate; every caller is on the main thread (the hooks run inside Enter and Revive).
        /// </summary>
        private sealed class Episode
        {
            public float Since;
            public bool Down, Revive;
        }

        private static readonly Dictionary<int, Episode> episodes = new Dictionary<int, Episode>();

        /// <summary>
        /// "down" (BondDowned) for a member's down. fromHook (0-XNPCCore's Downed hook, Patches_Relationship.Patch_Downed): its Enter
        /// runs only for a new down, so it always starts a new episode. From ScDowned (downSince: the tick it first saw her down):
        /// skipped when an episode of this same down already has its "down" - the hook runs inside Enter, before any tick can see
        /// her down, so an open episode noted at or before downSince with no revive yet is this down.
        /// </summary>
        public static void NoteDowned(int memberId, string why, bool fromHook, float downSince)
        {
            if (memberId < 0) return;
            lock (gate)
            {
                Episode ep;
                if (!fromHook && episodes.TryGetValue(memberId, out ep) && ep.Down && !ep.Revive && ep.Since <= downSince)
                {
                    Count("bond-down-deduped");
                    return;
                }
                episodes[memberId] = new Episode { Since = fromHook ? Time.time : downSince, Down = true };
                if (!fromHook) Count("bond-down-from-scdowned");
                Note(memberId, ScConfig.BondDowned, SigDown, why);
            }
        }

        /// <summary>
        /// "revive" (BondRevive) for a member back up, whoever revived her (an unknown reviver counts too). At most one per down:
        /// 0-XNPCCore's Revived hook (Patch_Revived) runs inside Revive, before ScDowned's tick sees her up, so ScDowned's back-up
        /// (fromHook false) is skipped when the hook already gave it. A bleed-out never gets here: ScDowned's rise needs a
        /// revive's clocks or RiseConfirmSeconds alive, and a death ends the episode (EndEpisode).
        /// </summary>
        public static void NoteRevived(int memberId, string why, bool fromHook)
        {
            if (memberId < 0) return;
            lock (gate)
            {
                Episode ep;
                if (episodes.TryGetValue(memberId, out ep))
                {
                    if (ep.Revive)
                    {
                        Count("bond-revive-deduped");
                        return;
                    }
                    ep.Revive = true;
                }
                else episodes[memberId] = new Episode { Since = Time.time, Revive = true };
                if (!fromHook) Count("bond-revive-from-scdowned");
                Note(memberId, ScConfig.BondRevive, SigRevive, why);
            }
        }

        /// <summary>
        /// ScDowned: her down is over - back up (after NoteRevived), dead (a bleed-out is no revive), unloaded or loaded again
        /// (0-XNPCCore stands her up with no revive), or no longer a member. Her next down starts a new episode.
        /// </summary>
        public static void EndEpisode(int memberId)
        {
            lock (gate) episodes.Remove(memberId);
        }

        /// <summary>
        /// Round MP6 (fix H-N1): the community in scope ticks again after a pause (ScTicker, several communities only) - a blood
        /// moon's roll call from before the pause goes (a later blood moon takes its own; one still on takes it from who is there now).
        /// </summary>
        internal static void OnResumed()
        {
            BondState st = homes.Current;
            if (!st.WasBloodMoon && st.BloodMoonMembers.Count == 0) return;
            st.WasBloodMoon = false;
            st.BloodMoonMembers.Clear();
            ScLog.Out("Bond: " + ScRegistry.Current.Label + " resumes - its blood-moon roll call from before the pause goes");
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
                // round MP6 (fix G-m5): her Bond is with her own community - only the killer's community's members (never with one)
                int killerCid = ScCommunities.Count > 1 ? ScPlayers.CommunityOf((EntityPlayer)killer) : -1;
                foreach (Member m in ScCommunities.AllMembers())      // round MP6 (part G): anyone's member she was on (a world loop)
                {
                    if (killerCid >= 0 && m.CommunityId != killerCid) continue;
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
        public static void Tick(World w, EntityPlayer player)     // round MP0: any player (the anchor)
        {
            try
            {
                if (w == null || player == null || !ScRegistry.Loaded) return;
                float now = Time.unscaledTime;
                if (now < nextTick) return;
                nextTick = now + 1f;
                // round MP6 (part H): a community that paused (none of its players online) through a blood moon's end did not see it
                // end - its roll call goes, as a blood moon whose end was not seen here gives nothing (only with two or more communities)
                BondState st = homes.Current;
                if (ScCommunities.Count > 1 && st.LastTick >= 0f && now - st.LastTick > 5f && st.WasBloodMoon && !ScSiege.BloodMoonActive(w))
                {
                    st.WasBloodMoon = false;
                    st.BloodMoonMembers.Clear();
                    ScLog.Out("Bond: " + ScRegistry.Current.Label + " paused through the end of a blood moon - no Bond for it");
                }
                st.LastTick = now;
                bool changed = Drain();
                BloodMoon(w);      // round MP0: any community player home counts (PlayerHome), so it no longer takes the host's player
                if (now >= nextSweep)
                {
                    nextSweep = now + 10f;
                    Sweep();
                }
                if (changed)
                {
                    // round MP6 (part H): Drain takes every community's events; each community whose members changed recomputes its
                    // own morale (with one community: once, as before)
                    foreach (int cid in drained)
                        using (ScRegistry.Use(cid)) ScMorale.Recompute();
                    drained.Clear();
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
                if (p.Signal == SigKill && !KillAllowed(m, p)) continue;   // the Fable review (finding 2): BondKillPerDay
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
                drained.Add(m.CommunityId);       // round MP6 (part H): her community recomputes its morale
            }
            return changed;
        }

        /// <summary>
        /// The Fable review of U4 (finding 2): a kill on her gives Bond at most BondKillPerDay times per in-game day. True: this
        /// one counts (her count for today goes up); false: today's are given (history and a verbose line say so). Ticker path.
        /// </summary>
        private static bool KillAllowed(Member m, Pending p)
        {
            int today = ScCommunity.Today;
            if (m.BondKillDay != today) { m.BondKillDay = today; m.BondKills = 0; }
            int cap = Math.Max(0, ScConfig.BondKillPerDay);
            if (m.BondKills < cap)
            {
                m.BondKills++;
                return true;
            }
            Count("bond-kill-capped");
            Remember(m, p.Why + " +0 (today's " + cap + " kill Bond already given)");
            ScLog.Verbose(m.Name + "'s Bond: " + p.Why + " - today's " + cap + " kill Bond already given (BondKillPerDay)");
            return false;
        }

        private static void Remember(Member m, string line)
        {
            List<string> h;
            if (!history.TryGetValue(m.Id, out h)) { h = new List<string>(); history[m.Id] = h; }
            h.Insert(0, "day " + ScCommunity.Today + ": " + line);
            while (h.Count > HistoryKeep) h.RemoveAt(h.Count - 1);
        }

        /// <summary>
        /// The blood moon survived together (the Fable review's finding 5: with you at both ends of it). When it starts (or when it
        /// is first seen here, after a load in the middle of it) the members with you are noted: following you, or a resident
        /// while you are home (ScBaseAttack.Near: the distance a base attack is fought from) - not one away on a run, posted at an
        /// outpost or still on the way. At its end each of them still a member - alive - who is with you again the same way
        /// (following you, or a resident with you home) gets +BondBloodMoon. A blood moon whose end was not seen here (quit before
        /// dawn) gives nothing.
        /// </summary>
        private static void BloodMoon(World w)
        {
            if (ScSiege.BloodMoonActive(w))
            {
                if (wasBloodMoon) return;
                wasBloodMoon = true;
                bloodMoonMembers.Clear();
                bool home = PlayerHome();
                foreach (Member m in ScRegistry.Members.Values)
                {
                    if (WithYou(m, home)) bloodMoonMembers.Add(m.Id);
                    else if (m.Status == MemberStatus.Resident) Remember(m, "a blood moon began with you away from home: no Bond for it");
                }
                ScLog.Out("Bond: a blood moon begins - " + bloodMoonMembers.Count + " member(s) with you (you are " + (home ? "home" : "away from home") + ")");
                return;
            }
            if (!wasBloodMoon) return;
            wasBloodMoon = false;
            bool homeAtEnd = PlayerHome();
            int given = 0;
            foreach (int id in bloodMoonMembers)
            {
                Member m = ScRegistry.GetMember(id);
                if (m == null) continue;                                        // died, or left the community
                if (!WithYou(m, homeAtEnd))
                {
                    Remember(m, "a blood moon survived, but not with you at its end (" + m.Status
                                + (m.Status == MemberStatus.Resident && !homeAtEnd ? ", you were away from home" : "") + "): no Bond");
                    continue;
                }
                bool follower = m.Status == MemberStatus.Following || m.Status == MemberStatus.PendingStation;
                Note(id, ScConfig.BondBloodMoon, SigBloodMoon, follower ? "survived the blood moon with you" : "survived the blood moon at home with you");
                given++;
            }
            ScLog.Out("Bond: the blood moon is over - " + given + " of " + bloodMoonMembers.Count + " member(s) with you at its start were with you at its end");
            bloodMoonMembers.Clear();
        }

        /// <summary>The blood moon's test, at its start and at its end: following you, or a resident while you are home.</summary>
        private static bool WithYou(Member m, bool playerHome)
        {
            if (m.Status == MemberStatus.Following || m.Status == MemberStatus.PendingStation) return true;
            return m.Status == MemberStatus.Resident && playerHome;
        }

        /// <summary>
        /// Round MP0 (D 4.11): any community player near home (ScBaseAttack.Near), not only the host's own - a client who stayed
        /// with the base through the blood moon counts. Single player: you, as before.
        /// </summary>
        private static bool PlayerHome()
        {
            if (!ScZone.HasHome) return false;
            List<EntityPlayer> all = ScPlayers.All;
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && ScPlayers.InCommunity(all[i]) && ScBaseAttack.Near(all[i])) return true;
            return false;
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
            o.Add(prefix + "  kills on " + ScMedic.HerObj(m) + " that gave Bond today: " + (m.BondKillDay == ScCommunity.Today ? m.BondKills : 0) + " of BondKillPerDay "
                  + ScConfig.BondKillPerDay);
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
