using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Radio commands, milestone E9 (research\ideas\DESIGN.md section 2, with the user's decisions: calls only at a radio block,
    /// so only from the radio window as it opens today - no Radio button in the community window, no remote calling, Q7 b -
    /// and a cooldown as the only cost, Q7 a). The radio window's Allies panel (XUiC_ScRadio, Tools/generate_radio_ui.py)
    /// calls the picked Allied enclave (ScEncAllies.IsAlly) for one of two things:
    ///
    /// Send a fighter. The best of their people at home (PickFighter: never the leader, the highest class tier, then the most
    /// Fighting + Shooting stars; at least the leader and one more stay home) comes after EnclaveCallFighterDelay game minutes.
    /// They are spawned EnclaveCallSpawnMin..Max metres from the player on the side of their enclave, out of everyone's sight
    /// (ScBaseAttack.LooseSpot, ScEnclavePeople.SeenByAny; any side after AnySideSeconds; the call fails and the cooldown is
    /// refunded after GiveUpSeconds without a spot), and walk up as E3's enlisted do: ScEnclaveDeals.EnlistBody (the hire,
    /// the record enlisted) for EnclaveCallFighterHours game hours. One whose body is out already (the player is near their
    /// building) is hired where they stand. From then on they are an enlisted person like any other: one per enclave,
    /// counted toward MaxFollowers, "Go home now." and Voice Control's dismiss, the time running out, a turn to hostile, a
    /// death (EnclaveEnlistDeathPenalty standing) - each ends the call (the tick sees the enlistment gone). A save and load
    /// keeps the call (bodies never outlive a load): the fighter comes back near the player ResumeSeconds after the world is
    /// up while their time lasts.
    ///
    /// Supply drop. After EnclaveDropDelay game minutes vanilla's supply crate (EntitySupplyCrate: parachute, smoke, the
    /// supply_drop marker) falls from EnclaveDropHeight metres onto an open column EnclaveDropRangeMin..Max metres from the
    /// player (the terrain is the top block there and its neighbours', never water, never a trader's area). Its class is the
    /// group's kind's, scEncDrop&lt;Kind&gt; (Config/entityclasses.xml, extending sc_General), whose LootList
    /// (Config/loot.xml) holds what they make and a Medium bundle of their resource. It is registered with the air drop
    /// director as vanilla's own crates are (AddSupplyCrate, SetSupplyCratePosition, RefreshCrates), so breaking it warns
    /// nothing; from then on it is vanilla's.
    ///
    /// One call at a time per ally, and each ally answers one every EnclaveCallCooldownDays days of the request clock
    /// (Enclave.CallDay, [enclave] callday=N, shifted by ScEnclaves.OnWorldDayBack); the call itself is Enclave.Call*
    /// ([enclave] call=kind:at:pid:until: the arrival's world time, the fighter's person and, once they came, the world time
    /// they go home). A pending call is called off with the cooldown refunded when the ally stops being one (Friendly again,
    /// hostile, its building lost), when its people come under siege (a fighter), or when calls or enclaves are switched
    /// off. Host only (the radio window is), main thread; no Harmony patch.
    /// </summary>
    public static class ScEncCalls
    {
        public const string Fighter = "fighter", Drop = "drop";
        /// <summary>A fighter's spot is searched on their enclave's side first, within this many degrees of it, then anywhere.</summary>
        private const float SideSpread = 35f, AnySideSeconds = 60f;
        /// <summary>No spot (fighter) or open column (drop) found in this many real seconds: the call fails, the cooldown is refunded.</summary>
        private const float GiveUpSeconds = 300f;
        /// <summary>A fighter whose call outlived a load comes back this many real seconds after the world is up.</summary>
        private const float ResumeSeconds = 15f;
        /// <summary>The fighter says hello once within this many metres of the player, if within GreetMaxSeconds of coming.</summary>
        private const float GreetRange = 8f, GreetMaxSeconds = 240f;
        private const int FighterTries = 16, DropTries = 24;
        /// <summary>At least this many of a group at home - the leader with them - before one can be sent.</summary>
        private const int HomeAtLeast = 3;
        /// <summary>The highest a crate may start (vanilla's planes fly at most 276).</summary>
        private const float CrateCeiling = 250f;
        private const string CrateFallback = "sc_General";

        /// <summary>Session-only state of one enclave's call.</summary>
        private sealed class Session
        {
            /// <summary>When the search for a spot began (real time; -1: not searching).</summary>
            public float SearchSince = -1f;
            /// <summary>The fighter's body this session (-1: none came yet - pending, or a live call just loaded).</summary>
            public int Body = -1;
            public float CameAt;
            public bool Greeted;
            /// <summary>The call was made with the console's force: it may stay pending for a group that is not an ally
            /// (session only: after a load the normal rule applies again).</summary>
            public bool Forced;
        }

        private static readonly Dictionary<int, Session> sessions = new Dictionary<int, Session>();
        /// <summary>The first tick with the world up (real time): a loaded fighter comes back ResumeSeconds after it.</summary>
        private static float readySince = -1f;

        public static void Reset()
        {
            sessions.Clear();
            readySince = -1f;
        }

        /// <summary>ScEnclaves.OnRegistryChanged: no body of a call is out (bodies never outlive a load); the resume waits again.</summary>
        public static void OnRegistryChanged()
        {
            Reset();
        }

        private static string Loc(string key) { return Localization.Get(key); }

        private static Session SessionOf(int enclaveId)
        {
            Session s;
            if (!sessions.TryGetValue(enclaveId, out s)) sessions[enclaveId] = s = new Session();
            return s;
        }

        // ------------------------------------------------------------------ the rules ----

        /// <summary>Request-clock days until this ally answers another call (0: today).</summary>
        public static int DaysLeft(Enclave e) { return e == null ? 0 : Math.Max(0, e.CallDay - ScEncRequests.Now); }

        /// <summary>A call of theirs is pending (on its way) or live (their fighter is with the player).</summary>
        public static bool Busy(Enclave e) { return e != null && e.CallKind.Length > 0; }

        /// <summary>Their fighter has come (live), as opposed to still on the way.</summary>
        public static bool Live(Enclave e) { return e != null && e.CallKind == Fighter && e.CallUntil > 0UL; }

        private static EncPerson PersonById(int pid)
        {
            if (pid <= 0) return null;
            foreach (EncPerson p in ScRegistry.EncPeople) if (p.Id == pid) return p;
            return null;
        }

        /// <summary>A siege of theirs is called or being fought (E5): nobody can come.</summary>
        private static bool UnderSiege(Enclave e)
        {
            EnclaveRequest r = ScEncSiege.Open;
            return r != null && e != null && r.EnclaveId == e.Id;
        }

        private static bool FollowersFull()
        {
            return ScRegistry.FollowerCount + ScEnclaveDeals.EnlistedCount() >= ScConfig.MaxFollowers;
        }

        /// <summary>The fighter's rank: the class tier, then Fighting + Shooting stars, then the one made first.</summary>
        private static bool Better(EncPerson a, EncPerson b)
        {
            int ta = ScXnpc.TierOf(a.Who.Class), tb = ScXnpc.TierOf(b.Who.Class);
            if (ta != tb) return ta > tb;
            int sa = a.Who.Stars(Skill.Fighting) + a.Who.Stars(Skill.Shooting), sb = b.Who.Stars(Skill.Fighting) + b.Who.Stars(Skill.Shooting);
            if (sa != sb) return sa > sb;
            return a.Id < b.Id;
        }

        /// <summary>
        /// The one they send: the best of the living at home who is not the leader (Better), when at least HomeAtLeast are at
        /// home, so the leader and one more stay. Enlisted (E3) and missing (E5) people are not at home. Null when nobody can go.
        /// </summary>
        public static EncPerson PickFighter(Enclave e)
        {
            if (e == null) return null;
            int home = 0;
            EncPerson best = null;
            foreach (EncPerson p in ScRegistry.EncPeople)
            {
                if (p.EnclaveId != e.Id || p.State != EncPerson.StateHome) continue;
                home++;
                if (p.IsLeader || p.SpawnFailures >= ScEnclavePeople.MaxSpawnFailures) continue;   // no body can be made for them
                if (best == null || Better(p, best)) best = p;
            }
            return home >= HomeAtLeast ? best : null;
        }

        /// <summary>
        /// Whether this call can be made now, and why not: off, no ally, not an ally (force: any group that is not hostile),
        /// no radio, a call of theirs not over yet, a siege, the cooldown (force ignores it); a fighter also needs nobody of
        /// theirs with the player, room among the followers and somebody to spare (people met). why is the panel's words.
        /// </summary>
        public static bool CanCall(Enclave e, string kind, bool force, out string why)
        {
            why = null;
            if (!ScCommunity.IsHost) { why = Loc("scHostOnly"); return false; }
            if (!ScConfig.Enclaves || !ScConfig.EnclaveCalls) { why = Loc("scEncCallOff"); return false; }
            if (e == null) { why = Loc("scEncBarterNoAlly"); return false; }
            if (e.Lost || e.IsHostile || (!force && !ScEncAllies.IsAlly(e))) { why = Loc("scEncCallNotAlly"); return false; }
            if (!ScRadio.Usable()) { why = Loc("scRadioNoPower"); return false; }
            if (Busy(e))
            {
                EncPerson f = PersonById(e.CallPid);
                string name = f != null ? f.Name : "?";
                why = e.CallKind == Drop ? Loc("scEncCallBusyDrop") : string.Format(Loc(Live(e) ? "scEncCallWithYou" : "scEncCallBusyFighter"), name);
                return false;
            }
            if (UnderSiege(e)) { why = Loc("scEncCallSiege"); return false; }
            int left = DaysLeft(e);
            if (!force && left > 0) { why = string.Format(Loc("scEncCallWait"), left); return false; }
            if (kind == Fighter)
            {
                EncPerson with = ScEnclaveDeals.EnlistedOf(e);
                if (with != null) { why = string.Format(Loc("scEncCallWithYou"), with.Name); return false; }
                if (FollowersFull()) { why = Loc("scEncFollowersFull"); return false; }
                if (PickFighter(e) == null) { why = Loc(e.PeopleRolled || ScEnclavePeople.PeopleOf(e).Count > 0 ? "scEncCallNobody" : "scEncCallNotMet"); return false; }
            }
            else if (kind != Drop) { why = Loc("scInternal"); return false; }
            return true;
        }

        /// <summary>How many of the group are at home (the leader too): PickFighter's rule.</summary>
        private static int HomeCount(Enclave e)
        {
            int n = 0;
            foreach (EncPerson p in ScRegistry.EncPeople) if (p.EnclaveId == e.Id && p.State == EncPerson.StateHome) n++;
            return n;
        }

        private static AIDirectorAirDropComponent AirDrops()
        {
            World w = ScCommunity.World;
            return w != null && w.aiDirector != null ? w.aiDirector.GetComponent<AIDirectorAirDropComponent>() : null;
        }

        /// <summary>Game minutes as world ticks (1000 an hour).</summary>
        private static ulong Minutes(int minutes) { return (ulong)Math.Max(0, minutes) * 1000UL / 60UL; }

        /// <summary>"0:30": a span of world ticks as game hours and minutes (minutes rounded up).</summary>
        public static string TimeText(ulong ticks)
        {
            ulong minutes = (ticks * 60UL + 999UL) / 1000UL;
            return (minutes / 60UL).ToString(CultureInfo.InvariantCulture) + ":" + (minutes % 60UL).ToString("00", CultureInfo.InvariantCulture);
        }

        /// <summary>"day 12, 18:00": a world time.</summary>
        private static string ClockText(ulong t)
        {
            return string.Format(Loc("scEncCallClock"), GameUtils.WorldTimeToDays(t), GameUtils.WorldTimeToHours(t).ToString("00") + ":" + GameUtils.WorldTimeToMinutes(t).ToString("00"));
        }

        // ------------------------------------------------------------------ the call ----

        /// <summary>
        /// The call (checked again): the record on its way - the arrival EnclaveCallFighterDelay / EnclaveDropDelay game minutes
        /// from now (now: at once, the console's), the fighter picked - the ally's next call EnclaveCallCooldownDays later on
        /// the request clock, the radio's answer told, the log, a save. message: the panel's words, or why not.
        /// </summary>
        public static bool Call(Enclave e, string kind, EntityPlayerLocal player, bool force, bool now, out string message)
        {
            if (!CanCall(e, kind, force, out message)) return false;
            World w = ScCommunity.World;
            if (w == null) { message = Loc("scInternal"); return false; }
            bool fighter = kind == Fighter;
            EncPerson p = fighter ? PickFighter(e) : null;
            ulong delay = now ? 0UL : Minutes(fighter ? ScConfig.EnclaveCallFighterDelay : ScConfig.EnclaveDropDelay);
            e.CallKind = kind;
            e.CallAt = w.worldTime + delay;
            e.CallPid = p != null ? p.Id : 0;
            e.CallUntil = 0UL;
            e.CallDay = ScEncRequests.Now + Math.Max(0, ScConfig.EnclaveCallCooldownDays);
            sessions.Remove(e.Id);
            if (force) SessionOf(e.Id).Forced = true;
            message = fighter ? string.Format(Loc("scEncCallFighterOnWay"), p.Name, TimeText(delay)) : string.Format(Loc("scEncCallDropOnWay"), TimeText(delay));
            if (player != null) ScCommunity.Tell(player, Radio(e, fighter ? "scEncCallFighterSaid" : "scEncCallDropSaid") + " " + message, "");
            ScLog.Out("radio call to enclave " + e.Id + " '" + e.Name + "': " + (fighter ? "a fighter, " + p.Name + " (p" + p.Id + ", " + p.Who.Class + ")" : "a supply drop")
                      + ", arriving " + (delay == 0UL ? "now" : "in " + TimeText(delay) + " (world time " + e.CallAt + ")") + (force ? " (forced)" : "")
                      + "; their next call from day " + e.CallDay + " (request clock day " + ScEncRequests.Now + ")");
            ScRegistry.SaveAtomic();
            return true;
        }

        /// <summary>"The X on the radio: "...."": their words (a line with no name or number in it, for the voicing round).</summary>
        private static string Radio(Enclave e, string saidKey)
        {
            return string.Format(Loc("scEncCallRadio"), e.Name, Loc(saidKey));
        }

        /// <summary>The call record gone (the session with it).</summary>
        private static void Clear(Enclave e)
        {
            e.CallKind = "";
            e.CallAt = 0UL;
            e.CallPid = 0;
            e.CallUntil = 0UL;
            sessions.Remove(e.Id);
        }

        /// <summary>A pending call off before it came: the cooldown refunded, a tell, the log. The caller saves.</summary>
        private static void Cancel(Enclave e, EntityPlayer player, string why)
        {
            string kind = e.CallKind;
            Clear(e);
            e.CallDay = 0;
            if (player != null) ScCommunity.Tell(player, string.Format(Loc("scEncCallCancelled"), e.Name), "ui_denied");
            ScLog.Out("radio call to enclave " + e.Id + " '" + e.Name + "' (" + kind + ") is off: " + why + " - the cooldown is refunded");
        }

        /// <summary>A pending call that found no way through (no spot, no body): their words, the cooldown refunded. The caller saves.</summary>
        private static void Fail(Enclave e, EntityPlayer player, string why)
        {
            string kind = e.CallKind;
            Clear(e);
            e.CallDay = 0;
            if (player != null) ScCommunity.Tell(player, Radio(e, "scEncCallFailSaid"), "ui_denied");
            ScLog.Out("radio call to enclave " + e.Id + " '" + e.Name + "' (" + kind + ") failed: " + why + " - the cooldown is refunded");
        }

        /// <summary>Why a pending call can't go on, or null: calls or enclaves off, the ally gone or no longer one, a siege (a fighter).</summary>
        private static string PendingBlocked(Enclave e, bool forced)
        {
            if (!ScConfig.Enclaves || !ScConfig.EnclaveCalls) return "radio calls are off";
            if (e.Lost) return "their building is gone";
            if (e.IsHostile) return "they turned hostile";
            if (!forced && !ScEncAllies.IsAlly(e)) return "they are no longer your allies (" + ScEnclaves.StandingKey(e) + ")";
            if (e.CallKind == Fighter && UnderSiege(e)) return "they are under siege";
            return null;
        }

        // ------------------------------------------------------------------ the 1 Hz driver ----

        /// <summary>ScTicker, every second, after ScEncRequests.Tick: each ally's call - its arrival, the spot, the resume after a load.</summary>
        public static void Tick(World w, EntityPlayerLocal player)
        {
            try
            {
                if (!ScEnclaves.Ready || w == null || player == null || !ScRegistry.Loaded) return;
                float now = Time.unscaledTime;
                if (readySince < 0f) readySince = now;
                bool changed = false;
                List<int> stale = null;
                foreach (int id in sessions.Keys)
                {
                    Enclave had = ScEnclaves.ById(id);
                    if (had == null || had.CallKind.Length == 0) (stale ?? (stale = new List<int>())).Add(id);
                }
                if (stale != null) foreach (int id in stale) sessions.Remove(id);
                bool any = false;
                foreach (Enclave e in ScEnclaves.All) if (e.CallKind.Length > 0) { any = true; break; }
                if (!any) return;
                foreach (Enclave e in ScEnclaves.All.ToArray())
                {
                    if (e.CallKind.Length == 0) continue;
                    try
                    {
                        if (TickCall(w, player, e, now)) changed = true;
                    }
                    catch (Exception ex)
                    {
                        ScLog.Once("enccalls-tick-" + e.Id, ex);
                    }
                }
                if (changed) ScRegistry.SaveAtomic();
            }
            catch (Exception e)
            {
                ScLog.Once("enccalls-tick", e);
            }
        }

        /// <summary>One call's second. True when the record changed.</summary>
        private static bool TickCall(World w, EntityPlayerLocal player, Enclave e, float now)
        {
            if (e.CallKind != Fighter && e.CallKind != Drop) { Clear(e); return true; }
            Session s = SessionOf(e.Id);
            if (Live(e)) return TickLive(w, player, e, s, now);
            string why = PendingBlocked(e, s.Forced);
            if (why != null) { Cancel(e, player, why); return true; }
            if (w.worldTime < e.CallAt) return false;
            if (player.IsDead()) { s.SearchSince = -1f; return false; }      // they wait until the player is back
            if (s.SearchSince < 0f) s.SearchSince = now;
            return e.CallKind == Fighter ? ArriveFighter(w, player, e, s, now, false) : ArriveDrop(w, player, e, s, now);
        }

        /// <summary>
        /// The fighter's arrival (or, resume, their return after a load): the picked person - picked again when they can no
        /// longer go - hired where they stand when their body is out, else spawned at a spot out of sight on their enclave's
        /// side (any side after AnySideSeconds; a resume any side at once). No spot within GiveUpSeconds: the call fails.
        /// </summary>
        private static bool ArriveFighter(World w, EntityPlayerLocal player, Enclave e, Session s, float now, bool resume)
        {
            EncPerson p = PersonById(e.CallPid);
            if (!resume && (p == null || p.EnclaveId != e.Id || p.State != EncPerson.StateHome || p.IsLeader || HomeCount(e) < HomeAtLeast))
            {
                EncPerson again = PickFighter(e);
                if (again == null) { Cancel(e, player, "nobody of theirs can go any more"); return true; }
                if (again != p)
                    ScLog.Out("radio call to enclave " + e.Id + " '" + e.Name + "': " + (p != null ? p.Name + " can't go any more" : "the one picked is gone") + " - " + again.Name + " (p" + again.Id + ") comes instead");
                p = again;
                e.CallPid = p.Id;
            }
            EncPerson with = ScEnclaveDeals.EnlistedOf(e);
            if (with != null)
            {
                if (resume) { ClearLive(e, player, "one of theirs is with you already (" + with.Name + ")", false); return true; }
                Cancel(e, player, with.Name + " of theirs is with you already");
                return true;
            }
            if (FollowersFull())
            {
                if (resume) { ClearLive(e, player, "you lead all the followers you can", true); return true; }
                Cancel(e, player, "you lead all the followers you can");
                return true;
            }
            EntityAlive npc = ScEnclavePeople.OwnBody(p);
            if (npc != null && !npc.IsDead())
            {
                Join(w, player, e, p, npc, s, now, resume, "hired where they stood, " + Mathf.RoundToInt(ScRunners.XZ(npc.position, player.position)) + " m away");
                return true;
            }
            if (p.EntityId >= 0 && ScNpc.IsPending(w, p.EntityId)) return false;      // a body of theirs still being built
            Vector3 pos;
            bool anySide = resume || now - s.SearchSince > AnySideSeconds;
            if (FighterSpot(w, player, e, anySide, out pos))
            {
                Vector3 d = player.position - pos;
                float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                npc = ScEnclavePeople.SpawnAt(w, e, p, pos, yaw);
                if (npc == null)
                {
                    p.SpawnFailures++;      // as ScEnclavePeople counts its own; the next call picks somebody else
                    if (resume) { ClearLive(e, player, "no body could be made for " + p.Name + " (class " + p.Who.Class + ")", true); return true; }
                    Fail(e, player, "no body could be made for " + p.Name + " (class " + p.Who.Class + ")");
                    return true;
                }
                ScEnclavePeople.AddPackage(npc);       // as every body at home has: back on their spot, they behave as one
                Join(w, player, e, p, npc, s, now, resume, "spawned " + Mathf.RoundToInt(ScRunners.XZ(pos, player.position)) + " m away at " + World.worldToBlockPos(pos)
                     + (anySide ? "" : ", on their side"));
                return true;
            }
            if (now - s.SearchSince <= GiveUpSeconds) return false;
            if (resume) { ClearLive(e, player, "no spot near you within " + Mathf.RoundToInt(GiveUpSeconds) + " s", true); return true; }
            Fail(e, player, "no spot within " + ScConfig.EnclaveCallSpawnMin + "-" + ScConfig.EnclaveCallSpawnMax + " m out of sight in " + Mathf.RoundToInt(GiveUpSeconds) + " s");
            return true;
        }

        /// <summary>
        /// The fighter is here: enlisted with the player (E3's shape) until EnclaveCallFighterHours from now - or, resume, until
        /// the call's own end - a tell with where they are, a ledger line on the first coming, the log.
        /// </summary>
        private static void Join(World w, EntityPlayerLocal player, Enclave e, EncPerson p, EntityAlive npc, Session s, float now, bool resume, string how)
        {
            ulong until = resume ? e.CallUntil : w.worldTime + (ulong)Math.Max(1, ScConfig.EnclaveCallFighterHours) * 1000UL;
            ScEnclaveDeals.EnlistBody(p, npc, player, until);
            e.CallPid = p.Id;
            e.CallUntil = until;
            s.Body = npc.entityId;
            s.CameAt = now;
            s.Greeted = resume;
            s.SearchSince = -1f;
            string where = ScEnclaves.Where(player.position, npc.position);
            ScCommunity.Tell(player, string.Format(Loc(resume ? "scEncCallReturned" : "scEncCallArrived"), p.Name, e.Name, where), "");
            if (!resume) ScOutposts.NoteLedger(string.Format(Loc("scEncCallLedgerFighter"), e.Name, p.Name));
            ScLog.Out("radio call to enclave " + e.Id + " '" + e.Name + "': " + p.Name + " (p" + p.Id + ", entity " + npc.entityId + ") " + (resume ? "is back with " : "came to ")
                      + player.EntityName + " - " + how + "; with them until " + ClockText(until));
        }

        /// <summary>
        /// A live call over (their time is up, they went home, died, were let go ...): the record goes, no refund. lost: a
        /// fighter loaded live who could not come back (no spot, no body, no room) - the player is told they went home. The
        /// caller saves.
        /// </summary>
        private static void ClearLive(Enclave e, EntityPlayer player, string why, bool lost = false)
        {
            EncPerson p = PersonById(e.CallPid);
            Clear(e);
            if (lost && player != null && p != null) ScCommunity.Tell(player, string.Format(Loc("scEncCallLost"), p.Name, e.Name), "");
            ScLog.Out("radio call to enclave " + e.Id + " '" + e.Name + "' is over: " + (p != null ? p.Name + " - " : "") + why);
        }

        /// <summary>
        /// A fighter who came: this session's body - the call ends when their enlistment has (any of E3's ways), and they say
        /// hello once near the player. A call loaded live: back near the player ResumeSeconds after the world is up while their
        /// time lasts (else it ends).
        /// </summary>
        private static bool TickLive(World w, EntityPlayerLocal player, Enclave e, Session s, float now)
        {
            EncPerson p = PersonById(e.CallPid);
            if (s.Body >= 0)
            {
                if (p == null || !p.Enlisted || p.EnlistedBody != s.Body)
                {
                    string state = p == null ? "they left their group's records (joined you, or the group is gone)" : p.State == EncPerson.StateDead ? "they died" : "their time with you ended";
                    ClearLive(e, player, state);
                    return true;
                }
                if (!s.Greeted)
                {
                    EntityAlive npc = ScEnclavePeople.OwnBody(p);
                    if (npc == null || now - s.CameAt > GreetMaxSeconds) s.Greeted = true;
                    else if (ScRunners.XZ(npc.position, player.position) <= GreetRange && Mathf.Abs(npc.position.y - player.position.y) < 4f)
                    {
                        s.Greeted = true;
                        ScCommunity.Tell(player, string.Format(Loc("scEncCallSays"), p.Name, Loc("scEncCallHello")), "");
                    }
                }
                return false;
            }
            // loaded live: nobody came this session yet
            if (w.worldTime >= e.CallUntil) { ClearLive(e, player, "their time with you was up by the time the world was back"); return true; }
            if (p == null || p.EnclaveId != e.Id || !p.Alive) { ClearLive(e, player, "the one who came can't any more (" + (p == null ? "gone" : p.State) + ")"); return true; }
            if (p.Enlisted)
            {
                // a body of theirs with the player already (not after a load: bodies never outlive one) - it is the call's
                s.Body = p.EnlistedBody;
                s.Greeted = true;
                return false;
            }
            if (!ScConfig.Enclaves || !ScConfig.EnclaveCalls || e.Lost || e.IsHostile) { ClearLive(e, player, !ScConfig.Enclaves || !ScConfig.EnclaveCalls ? "radio calls are off" : "their group can't send them now"); return true; }
            if (now < readySince + ResumeSeconds || player.IsDead()) return false;
            if (s.SearchSince < 0f) s.SearchSince = now;
            return ArriveFighter(w, player, e, s, now, true);
        }

        /// <summary>
        /// The supply drop: an open column found, the crate of their kind spawned EnclaveDropHeight above it, registered with
        /// the air drop director; a tell, a ledger line, the log; the call is done (the crate is vanilla's from here on).
        /// </summary>
        private static bool ArriveDrop(World w, EntityPlayerLocal player, Enclave e, Session s, float now)
        {
            Vector3 ground;
            if (!DropSpot(w, player, out ground))
            {
                if (now - s.SearchSince <= GiveUpSeconds) return false;
                Fail(e, player, "no open ground within " + ScConfig.EnclaveDropRangeMin + "-" + ScConfig.EnclaveDropRangeMax + " m in " + Mathf.RoundToInt(GiveUpSeconds) + " s");
                return true;
            }
            string cls = CrateClass(e);
            int cid = EntityClass.FromString(cls);
            if (!ScCommunity.ClassExists(cid))
            {
                ScLog.Once("enccalls-crate-" + cls, "radio calls: no entity class " + cls + " (Config/entityclasses.xml did not apply) - the drop uses vanilla's " + CrateFallback);
                cls = CrateFallback;
                cid = EntityClass.FromString(cls);
            }
            if (!ScCommunity.ClassExists(cid)) { Fail(e, player, "no supply crate class (" + cls + ")"); return true; }
            GameRandom r = w.GetGameRandom();
            float top = Mathf.Min(ground.y + Math.Max(10, ScConfig.EnclaveDropHeight), CrateCeiling);
            Vector3 at = new Vector3(ground.x, Mathf.Max(top, ground.y + 10f), ground.z);
            Entity crate = EntityFactory.CreateEntity(cid, at, new Vector3(0f, r != null ? r.RandomFloat * 360f : 0f, 0f));
            if (crate == null) { Fail(e, player, "the factory made no " + cls); return true; }
            w.SpawnEntityInWorld(crate);
            AIDirectorAirDropComponent comp = AirDrops();
            if (comp != null)
            {
                // as AIDirectorAirDropComponent.SpawnSupplyCrate registers its own (without its random class and its eviction of
                // the oldest of 12): the crate's landing sets its position, and breaking it removes it, with no warning
                comp.AddSupplyCrate(crate.entityId);
                comp.SetSupplyCratePosition(crate.entityId, World.worldToBlockPos(ground));
                comp.RefreshCrates();
            }
            else ScLog.Once("enccalls-noairdrops", "radio calls: the world has no air drop director - the crate is not registered with it (breaking it may warn)");
            Clear(e);
            ScCommunity.Tell(player, string.Format(Loc("scEncCallDropFalling"), e.Name), "");
            ScOutposts.NoteLedger(string.Format(Loc("scEncCallLedgerDrop"), e.Name));
            ScLog.Out("radio call to enclave " + e.Id + " '" + e.Name + "': a supply crate (" + cls + ", entity " + crate.entityId + ") falls from " + Mathf.RoundToInt(at.y)
                      + " onto " + World.worldToBlockPos(ground) + ", " + Mathf.RoundToInt(ScRunners.XZ(ground, player.position)) + " m from " + player.EntityName);
            return true;
        }

        /// <summary>The crate class of the group's kind (Config/entityclasses.xml): scEncDrop + Growers, Medics, Militia, Builders, Roadcrew or General.</summary>
        public static string CrateClass(Enclave e)
        {
            switch (e != null ? e.Kind : "")
            {
                case "growers": return "scEncDropGrowers";
                case "medics": return "scEncDropMedics";
                case "militia": return "scEncDropMilitia";
                case "builders": return "scEncDropBuilders";
                case "roadcrew": return "scEncDropRoadcrew";
                default: return "scEncDropGeneral";
            }
        }

        // ------------------------------------------------------------------ spots ----

        /// <summary>
        /// A spot for the fighter: EnclaveCallSpawnMin..Max m from the player - toward their enclave's centre within SideSpread
        /// degrees, or anySide - standable (LooseSpot: the player's level first, loaded, no trader area or bedroll zone, not
        /// nearer than the minimum to any player), out of everyone's sight and on settled chunks.
        /// </summary>
        private static bool FighterSpot(World w, EntityPlayerLocal player, Enclave e, bool anySide, out Vector3 pos)
        {
            pos = Vector3.zero;
            GameRandom r = w.GetGameRandom();
            if (r == null) return false;
            float min = Mathf.Max(5f, ScConfig.EnclaveCallSpawnMin), max = Mathf.Max(min, ScConfig.EnclaveCallSpawnMax);
            Vector3 d = e.Centre - player.position;
            d.y = 0f;
            float toward = d.sqrMagnitude > 1f ? Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg : r.RandomFloat * 360f;
            for (int i = 0; i < FighterTries; i++)
            {
                float ang = anySide ? r.RandomFloat * 360f : toward + (r.RandomFloat * 2f - 1f) * SideSpread;
                float dist = min + r.RandomFloat * (max - min);
                Vector3 at = player.position + Quaternion.Euler(0f, ang, 0f) * Vector3.forward * dist;
                Vector3 p;
                if (!ScBaseAttack.LooseSpot(w, at, player.position.y, min * 0.8f, out p)) continue;
                if (ScEnclavePeople.SeenByAny(w, p) || !ScRunners.Settled(w, p)) continue;
                pos = p;
                return true;
            }
            return false;
        }

        /// <summary>
        /// An open column for the crate EnclaveDropRangeMin..Max m from the player, any side: loaded, the terrain its top block
        /// (a plant on it at most) and its eight neighbours' too within two blocks of it - no roof, wall or tree in the way -
        /// never water, never a trader's area. ground: the cell on the terrain.
        /// </summary>
        private static bool DropSpot(World w, EntityPlayerLocal player, out Vector3 ground)
        {
            ground = Vector3.zero;
            GameRandom r = w.GetGameRandom();
            if (r == null) return false;
            float min = Mathf.Max(3f, ScConfig.EnclaveDropRangeMin), max = Mathf.Max(min, ScConfig.EnclaveDropRangeMax);
            for (int i = 0; i < DropTries; i++)
            {
                float ang = r.RandomFloat * 360f, dist = min + r.RandomFloat * (max - min);
                Vector3 at = player.position + Quaternion.Euler(0f, ang, 0f) * Vector3.forward * dist;
                int x = Mathf.FloorToInt(at.x), z = Mathf.FloorToInt(at.z), t;
                if (!OpenColumn(w, x, z, out t)) continue;
                bool open = true;
                for (int dx = -1; dx <= 1 && open; dx++)
                {
                    for (int dz = -1; dz <= 1 && open; dz++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        int tn;
                        if (!OpenColumn(w, x + dx, z + dz, out tn) || Math.Abs(tn - t) > 2) open = false;
                    }
                }
                if (!open || w.IsWithinTraderArea(new Vector3i(x, t, z))) continue;
                ground = new Vector3(x + 0.5f, t + 1, z + 0.5f);
                return true;
            }
            return false;
        }

        /// <summary>A loaded column whose highest block is the terrain or one block on it (grass, a plant), and no water at the top.</summary>
        private static bool OpenColumn(World w, int x, int z, out int terrain)
        {
            terrain = 0;
            if (w.GetChunkFromWorldPos(x, 0, z) == null) return false;
            terrain = w.GetTerrainHeight(x, z);
            int top = w.GetHeight(x, z);
            if (terrain <= 0 || top > terrain + 1) return false;
            return !w.IsWater(x, terrain + 1, z) && !w.IsWater(x, top, z);
        }

        // ------------------------------------------------------------------ the panel, the clock ----

        /// <summary>
        /// The panel's hint while a call of theirs is on: on the way (about when), almost here (the spot search), with you
        /// until when, or on the way back after a load. "" with none.
        /// </summary>
        public static string StateText(Enclave e)
        {
            World w = ScCommunity.World;
            if (!Busy(e) || w == null) return "";
            EncPerson p = PersonById(e.CallPid);
            string name = p != null ? p.Name : "?";
            if (e.CallKind == Drop)
                return w.worldTime < e.CallAt ? string.Format(Loc("scEncCallDropOnWay"), TimeText(e.CallAt - w.worldTime)) : Loc("scEncCallDropNear");
            if (Live(e))
            {
                Session s;
                bool here = sessions.TryGetValue(e.Id, out s) && s.Body >= 0;
                return here ? string.Format(Loc("scEncCallFighterWith"), name, ClockText(e.CallUntil)) : string.Format(Loc("scEncCallFighterBack"), name);
            }
            return w.worldTime < e.CallAt ? string.Format(Loc("scEncCallFighterOnWay"), name, TimeText(e.CallAt - w.worldTime)) : string.Format(Loc("scEncCallFighterNear"), name);
        }

        /// <summary>
        /// ScEnclaves.OnWorldDayBack: the request clock went back reqBack days (the next call's day), the world worldBack days
        /// (a call's arrival and its fighter's end, as E3's enlistments) - each keeps its remaining span, never below 0 (an end
        /// never reaches 0, which means "not come yet"). Returns how many stamps moved.
        /// </summary>
        internal static int ShiftDays(int reqBack, int worldBack)
        {
            int n = 0;
            ulong ticks = (ulong)Math.Max(0, worldBack) * 24000UL;
            foreach (Enclave e in ScEnclaves.All)
            {
                if (reqBack > 0 && e.CallDay > 0) { e.CallDay = Math.Max(0, e.CallDay - reqBack); n++; }
                if (ticks == 0UL || e.CallKind.Length == 0) continue;
                if (e.CallAt > 0UL) { e.CallAt = e.CallAt > ticks ? e.CallAt - ticks : 0UL; n++; }
                if (e.CallUntil > 0UL) { e.CallUntil = e.CallUntil > ticks ? e.CallUntil - ticks : 1UL; n++; }
            }
            return n;
        }

        // ------------------------------------------------------------------ console ----

        private static string CallText(Enclave e)
        {
            if (!Busy(e)) return "none";
            World w = ScCommunity.World;
            ulong now = w != null ? w.worldTime : 0UL;
            EncPerson p = PersonById(e.CallPid);
            string who = p != null ? p.Name + " (p" + p.Id + ")" : "nobody picked";
            if (e.CallKind == Drop)
                return "a supply drop, " + (now < e.CallAt ? "falling in " + TimeText(e.CallAt - now) + " (" + ClockText(e.CallAt) + ")" : "due now - looking for open ground near you");
            if (Live(e))
            {
                Session s;
                bool here = sessions.TryGetValue(e.Id, out s) && s.Body >= 0;
                return "a fighter, " + who + ", with you until " + ClockText(e.CallUntil) + (here ? " (entity " + s.Body + ")" : " - back near you " + Mathf.RoundToInt(ResumeSeconds) + " s after the world is up");
            }
            return "a fighter, " + who + ", " + (now < e.CallAt ? "arriving in " + TimeText(e.CallAt - now) + " (" + ClockText(e.CallAt) + ")" : "due now - looking for a spot near you");
        }

        /// <summary>"radio calls: ..." for sc enclave info.</summary>
        public static string InfoLine(Enclave e)
        {
            string s;
            if (!ScConfig.EnclaveCalls) s = "off (EnclaveCalls=false)";
            else if (e.IsHostile) s = "never (hostile)";
            else
            {
                int left = DaysLeft(e);
                s = "a fighter for " + Math.Max(1, ScConfig.EnclaveCallFighterHours) + " h or a supply drop (" + CrateClass(e) + ")"
                    + (ScEncAllies.IsAlly(e) ? "" : ", only while Allied")
                    + (left > 0 ? ", the next call from day " + e.CallDay + " (" + left + " day(s))" : ", they answer today")
                    + "; the call now: " + CallText(e);
            }
            return "radio calls: " + s + " (request clock day " + ScEncRequests.Now + ")";
        }

        /// <summary>
        /// sc enclave call &lt;id&gt; [fighter|drop [force] [now] | cancel]: the calls' state, who would go and whether each can be
        /// made now; a call as from the radio window (force: the cooldown and the Allied rule ignored; now: it comes at once);
        /// cancel: a pending call off, the cooldown refunded.
        /// </summary>
        public static void Command(Enclave e, List<string> p, List<string> into)
        {
            string verb = p.Count > 3 ? p[3].ToLowerInvariant() : "";
            if (verb == "")
            {
                into.Add(e.Name + " (enclave " + e.Id + ", " + ScEnclaves.StandingWord(e) + "): " + InfoLine(e));
                EncPerson f = PickFighter(e);
                into.Add("  who would go: " + (f != null ? f.Name + " (p" + f.Id + ", " + f.Who.Class + ", tier " + ScXnpc.TierOf(f.Who.Class) + ", fighting "
                         + f.Who.Stars(Skill.Fighting) + " + shooting " + f.Who.Stars(Skill.Shooting) + " stars)" : "nobody")
                         + "; " + HomeCount(e) + " at home (" + HomeAtLeast + " needed: the leader and one more stay)");
                string why;
                into.Add("  fighter now: " + (CanCall(e, Fighter, false, out why) ? "can call" : why));
                into.Add("  supply drop now: " + (CanCall(e, Drop, false, out why) ? "can call" : why));
                return;
            }
            if (verb == "cancel")
            {
                if (!Busy(e)) { into.Add(e.Name + " has no call on"); return; }
                if (Live(e)) { into.Add(e.Name + "'s fighter is with you already: sc enclave release " + e.Id + " sends them home"); return; }
                Cancel(e, ScCommunity.LocalPlayer, "the console");
                ScRegistry.SaveAtomic();
                into.Add("the call to " + e.Name + " is off (the cooldown refunded: they answer today)");
                return;
            }
            if (verb != Fighter && verb != Drop)
            {
                into.Add("sc enclave call <id> [fighter|drop [force] [now] | cancel]");
                return;
            }
            bool force = false, now = false;
            for (int i = 4; i < p.Count; i++)
            {
                string a = p[i].ToLowerInvariant();
                if (a == "force") force = true;
                else if (a == "now") now = true;
                else { into.Add("unknown argument '" + p[i] + "' (force, now)"); return; }
            }
            string msg;
            bool done = Call(e, verb, ScCommunity.LocalPlayer, force, now, out msg);
            into.Add((done ? "called " + e.Name + ": " : "refused: ") + msg);
        }
    }
}
