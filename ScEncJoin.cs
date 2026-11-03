using System;
using System.Collections.Generic;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Whole-enclave join, milestone E12 (research\ideas\DESIGN.md section 9, idea 8, with the user's decision Q10: they make
    /// their own way and turn up at home when you're next there; Allied, standing 90 and room for everyone - when the community
    /// has no room for all of them the option is not offered at all; a refill is still owed and there is no Enclave-lost
    /// morale).
    ///
    /// The row (Config/dialogs.xml, DialogOpsEnclaves.cs). The Allied leader's "Would your people join ours?" shows only with
    /// EnclaveJoinStanding standing or more and room in the community for every one of them (Shown, HiddenWhy): the room is the
    /// roster's own limit, MaxMembers - ScCommunity.CanRecruit's "your community is full" - less the members now (one on the
    /// way counts: they moved in), against the living and, while the group is still being made, the places not made yet. It
    /// greys with the reason (Blocked) while one of theirs is out with the player (E3, E9's fighter), missing (E5) or raiding
    /// (E10), a request of theirs is open (offered: you owe them an answer; accepted, a siege too), a radio call of theirs is
    /// on (E9), the group is still being made, or the player has no home. The confirm ({joinfacts}, FactsText) says how many
    /// come, what they bring and how.
    ///
    /// The join (JoinNow). Every living person's own Member record joins - the face, name, weapons, rounds, skills, traits and
    /// specialty they have; a body out gives its pair and rounds, as E3's recruit reads them - and leaves the enclave's records;
    /// their favours close; EnclaveJoinBundles units of their kind's resource each go into the stockpile (a general group's
    /// split across the classic five). With EnclaveJoinArrive "own" (the default) each makes their own way: a member on the
    /// way (MemberStatus.Arriving: no body, keyed by Key until one is made) who may turn up at home from EnclaveJoinArriveHours
    /// game hours on, and a body of theirs out now walks off toward the player's home and goes once nobody sees it (husks).
    /// With "follow" each one with a body - all of them, made now (ScEnclavePeople.SpawnAll) while their building is loaded -
    /// follows the player until home, as one recruit does (ScCommunity.Admit); anyone without one comes on their own. The
    /// leader's body gives up the group's trade stock either way (DropStock). Then the
    /// group ends (ScEnclaves.Disband, cause "joined": the building is free and its zombies come back after the loot respawn
    /// wait; E6 owes a refill, with no Enclave-lost morale). One tell (the leader's words, no name or number in them, then the
    /// facts), a ledger line, the log, a save. Should one person fail (an exception), the rest still join and the group stands
    /// with whoever is left.
    ///
    /// The arrival (Tick, from ScTicker after ScMissing's). A member on the way whose time has come is made at home while the
    /// player is there and the zone is loaded - one a second: a ground spot at the zone's edge (at most ArriveReach m from the
    /// claim, so Station walks them to their post rather than moving them), on the side facing the building they left (any
    /// side after AnySideSeconds), never in anyone's sight - re-keyed to the body (Rekey) and stationed, with a tell; the last
    /// of a group writes a ledger line. A member on the way eats, needs a bed and counts for morale and the roster (they moved
    /// in), but has no body: no orders, runs, postings or watches (the window says "On the way"), never missing (ScMissing,
    /// ScDaily's count, ScTicker.Relink, ScCommunity.FindNpc), never one who threatens to leave, no specialty work and no
    /// resident's noise until they are home. A group whose people are still on the way to you (a join cut short) neither moves
    /// (E11), answers a radio call (E9) nor raids (E10).
    ///
    /// Saved: [member] arrive=at:enclave:x,y,z:name (the world time they may turn up from, the group they left - its id and its
    /// building's centre, the side they come in on - and its name) with status=Resident, so an older build has a resident with
    /// no body (missing, and "Call back" brings them); shifted by ScEnclaves.OnWorldDayBack (the world's clock). Host only,
    /// main thread, single player (ScCommunity.LocalPlayer); no Harmony patch.
    /// </summary>
    public static class ScEncJoin
    {
        public const string ArriveOwn = "own", ArriveFollow = "follow";
        /// <summary>The arrival spot is at most this many metres from the claim: Station moves a body more than 40 m from its post.</summary>
        private const float ArriveReach = 30f;
        /// <summary>The spot is searched on the side they came from, within this many degrees of it, then on any side.</summary>
        private const float SideSpread = 35f, AnySideSeconds = 60f;
        private const int SpotTries = 8;
        /// <summary>A husk gets its next point when within this many metres of the last, or this many real seconds after it.</summary>
        private const float HuskReached = 3f, HuskResteerSeconds = 15f;
        private static readonly Vector3 Nudge = new Vector3(0f, 0.002f, 0f);

        /// <summary>A body of someone who joined and makes their own way: walking off toward the player's home until nobody sees it.</summary>
        private sealed class Husk
        {
            public int Id;
            public ulong Born;
            public string Name = "";
            public Vector3 Next;
            public float SteeredAt = -1f;
        }

        private static readonly List<Husk> husks = new List<Husk>();
        /// <summary>The member whose arrival spot is being searched for (their key; -1: none) and since when (real time).</summary>
        private static int searchFor = -1;
        private static float searchSince;
        /// <summary>Members on the way no body can be made for this session (their class is gone): skipped, said once.</summary>
        private static readonly HashSet<int> noBody = new HashSet<int>();

        public static void Reset()
        {
            husks.Clear();
            searchFor = -1;
            searchSince = 0f;
            noBody.Clear();
        }

        private static string Loc(string key) { return Localization.Get(key); }

        // ------------------------------------------------------------------ the switches ----

        public static bool Configured { get { return ScConfig.Enclaves && ScConfig.EnclaveJoin; } }

        /// <summary>Why the join is off ("on" when it is not).</summary>
        public static string OffWhy()
        {
            if (!ScConfig.Enclaves) return "Enclaves=false";
            return ScConfig.EnclaveJoin ? "on" : "EnclaveJoin=false";
        }

        /// <summary>EnclaveJoinArrive "follow": they follow the player home instead of making their own way.</summary>
        public static bool Follow { get { return ScConfig.EnclaveJoinArrive == ArriveFollow; } }

        // ------------------------------------------------------------------ members on the way ----

        /// <summary>A member's key while they are on the way: no entity id ever reaches it (ScEnclaveDeals.ParkedKey's idea, in the members' table).</summary>
        public static int Key(EncPerson p) { return int.MaxValue - p.Id; }

        /// <summary>Members still on the way who came from this group (a join cut short leaves the rest standing).</summary>
        public static int ArrivingFrom(Enclave e)
        {
            if (e == null) return 0;
            int n = 0;
            foreach (Member m in ScRegistry.Members.Values) if (m.Status == MemberStatus.Arriving && m.ArriveEnclave == e.Id) n++;
            return n;
        }

        // ------------------------------------------------------------------ the rules ----

        /// <summary>How many would come: the living, and - a group still being made - the places not made yet.</summary>
        public static int Coming(Enclave e)
        {
            if (e == null) return 0;
            int n = ScEnclavePeople.Living(e).Count;
            if (!e.PeopleRolled) n += Math.Max(0, ScEnclavePeople.GroupSize(e) - ScEnclavePeople.PeopleOf(e).Count);
            return n;
        }

        /// <summary>The roster's room: MaxMembers (ScCommunity.CanRecruit's limit) less the members now, on the way included.</summary>
        public static int RoomLeft() { return Math.Max(0, ScConfig.MaxMembers - ScRegistry.Members.Count); }

        /// <summary>
        /// Why the row is not offered at all (console words), or null when it is: the join off, hostile, lost, not Allied or
        /// below EnclaveJoinStanding (force: the console - these two ignored), nobody alive, no room for all of them.
        /// </summary>
        public static string HiddenWhy(Enclave e, bool force)
        {
            if (!Configured) return "the join is off (" + OffWhy() + ")";
            if (e == null) return "no such enclave";
            if (e.IsHostile) return "they are hostile";
            if (e.Lost) return "their building is not in this world";
            if (!force && ScEnclaves.StandingKey(e) != "allied") return "they are not Allied (" + ScEnclaves.StandingWord(e) + ", standing " + e.Standing + ")";
            if (!force && e.Standing < ScConfig.EnclaveJoinStanding) return "standing " + e.Standing + " is below " + ScConfig.EnclaveJoinStanding;
            int coming = Coming(e), room = RoomLeft();
            if (coming <= 0) return "nobody of theirs is alive";
            if (coming > room) return "your community has room for " + room + " more, they are " + coming + " (MaxMembers " + ScConfig.MaxMembers + ")";
            return null;
        }

        /// <summary>The row shows: the leader of an Allied group at EnclaveJoinStanding or more, with room for all of them.</summary>
        public static bool Shown(Enclave e) { return HiddenWhy(e, false) == null; }

        /// <summary>
        /// Why they can't come now (the greyed row's reason), or null: one of theirs out with the player, missing or away
        /// raiding first (by name), then a request of theirs (offered or accepted), a radio call of theirs, the group still
        /// being made, no home.
        /// </summary>
        public static string Blocked(Enclave e, EntityPlayer player)
        {
            if (!ScCommunity.IsHost) return Loc("scHostOnly");
            if (e == null) return Loc("scEncStays");
            foreach (EncPerson p in ScRegistry.EncPeople)
            {
                if (p.EnclaveId != e.Id) continue;
                if (p.Enlisted) return string.Format(Loc("scEncJoinWhyWithYou"), p.Name);
                if (p.Missing) return string.Format(Loc("scEncJoinWhyMissing"), p.Name);
                if (p.Raiding) return string.Format(Loc("scEncJoinWhyAway"), p.Name);
            }
            EnclaveRequest r = ScEncRequests.Of(e);
            if (r != null) return Loc(r.IsOffered ? "scEncJoinWhyAnswer" : "scEncJoinWhyRequest");
            if (ScEncCalls.Busy(e)) return Loc("scEncJoinWhyCall");
            if (!e.PeopleRolled) return Loc("scEncRecruitSettling");
            if (!ScZone.EnsureHome(player)) return Loc("scEncJoinWhyHome");
            return null;
        }

        /// <summary>The greyed row's words: "Would your people join ours? (Not while Mara is out with you)" - "" when it is open.</summary>
        public static string WhyText(Enclave e, EntityPlayer player)
        {
            string why = Blocked(e, player);
            return why == null ? "" : string.Format(Loc("scEncJoinWhy"), Loc("scEncJoinAsk"), why);
        }

        // ------------------------------------------------------------------ what they bring ----

        /// <summary>EnclaveJoinBundles units a person of their kind's resource (a general group's split across the classic five, in turn).</summary>
        private static int[] Bundles(Enclave e, int people)
        {
            var units = new int[ScNames.ResourceCount];
            int n = Math.Max(0, ScConfig.EnclaveJoinBundles) * Math.Max(0, people);
            int own = e != null ? ScEncRequests.OwnResource(e.Kind) : -1;
            if (own >= 0) units[own] = n;
            else for (int i = 0; i < n; i++) units[i % ScNames.ClassicCount]++;
            return units;
        }

        /// <summary>"10 food", "2 food, 2 medicine, 2 ammo, 2 building materials, 2 fuel"; "only what they carry" for none.</summary>
        private static string Words(int[] units)
        {
            var parts = new List<string>();
            for (int i = 0; i < units.Length; i++) if (units[i] > 0) parts.Add(units[i] + " " + ScEncGifts.ResWord((Resource)i));
            return parts.Count > 0 ? string.Join(", ", parts.ToArray()) : Loc("scEncJoinBringsNothing");
        }

        /// <summary>{joinfacts} in the confirm: how many come, what they bring and how they come ("5 of them, bringing 10 food. ...").</summary>
        public static string FactsText(Enclave e)
        {
            if (e == null) return "";
            int coming = Coming(e);
            return string.Format(Loc(Follow ? "scEncJoinFactsFollow" : "scEncJoinFactsOwn"), coming, Words(Bundles(e, coming)));
        }

        // ------------------------------------------------------------------ the join ----

        /// <summary>
        /// "Welcome to the community." (DialogActionScEncJoin): the leader being talked to, every gate again (HiddenWhy, Blocked),
        /// then the join (JoinNow). False with the reason (a tooltip).
        /// </summary>
        public static bool Join(EntityPlayerLocal player, EntityAlive npc, out string reason)
        {
            reason = null;
            EncPerson p = ScEnclavePeople.PersonOf(npc);
            Enclave e = ScEnclavePeople.EnclaveOf(p);
            if (player == null || e == null || npc.IsDead() || npc.WorldTimeBorn != p.EntityBorn || !p.IsLeader || p.State != EncPerson.StateHome)
            {
                reason = Loc("scEncStays");
                return false;
            }
            string hidden = HiddenWhy(e, false);
            if (hidden != null)
            {
                ScLog.Out("enclave " + e.Id + " '" + e.Name + "': the join was refused - " + hidden);
                reason = Loc("scEncStays");
                return false;
            }
            reason = Blocked(e, player);
            if (reason != null) return false;
            JoinNow(e, player, "the dialog");
            return true;
        }

        /// <summary>A person's favour (E3) closes with the join: completed when it was done, failed otherwise; the prospect dropped.</summary>
        private static void CloseFavour(EncPerson p, EntityPlayerLocal player)
        {
            Prospect pr = ScEnclaveDeals.ProspectByPerson(p.Id);
            if (pr == null) return;
            if (player != null) ScQuests.CloseFavour(player, pr.Id, pr.Done);
            ScRegistry.Prospects.Remove(pr.Id);
        }

        /// <summary>
        /// The join, the gates passed (see the class): each living person joins - following (a body, "follow") or on their own
        /// way - the stockpile gets what they bring, the group ends when nobody is left; the tell, the ledger, the log, a save.
        /// Returns the console's line.
        /// </summary>
        internal static string JoinNow(Enclave e, EntityPlayerLocal player, string cause)
        {
            World w = ScCommunity.World;
            Community c = ScRegistry.Community;
            bool follow = Follow;
            int made = follow ? ScEnclavePeople.SpawnAll(e) : 0;      // follow: everyone at home a body now (-1: their building's chunks not settled)
            if (!ScEnclaves.All.Contains(e)) return "nobody of " + e.Name + " was alive - the group is gone";
            List<EncPerson> people = ScEnclavePeople.Living(e);
            EncPerson leader = ScEnclavePeople.LeaderOf(e);
            string leaderName = leader != null ? leader.Name : "";
            string name = e.Name;
            Vector3 centre = e.Centre;
            ulong now = w != null ? w.worldTime : 0UL;
            ulong at = Math.Max(1UL, now + (ulong)Math.Max(0, ScConfig.EnclaveJoinArriveHours) * 1000UL);
            var names = new List<string>();
            int following = 0, arriving = 0, walking = 0, failed = 0;
            foreach (EncPerson p in people)
            {
                try
                {
                    EntityAlive body = ScEnclavePeople.OwnBody(p);
                    if (body != null && body.IsDead()) body = null;
                    bool wasLeader = p.IsLeader;
                    CloseFavour(p, player);
                    if (follow && body != null)
                    {
                        if (JoinFollowing(e, p, body, player)) { following++; names.Add(p.Name + (wasLeader ? " (leader)" : "")); }
                        else failed++;
                        continue;
                    }
                    JoinArriving(w, e, p, body, at, centre);
                    arriving++;
                    if (body != null) walking++;
                    names.Add(p.Name + (wasLeader ? " (leader)" : ""));
                }
                catch (Exception ex)
                {
                    failed++;
                    ScLog.Once("encjoin-person-" + p.Id, ex);
                }
            }
            int joined = following + arriving;
            int[] units = Bundles(e, joined);
            for (int r = 0; r < ScNames.ResourceCount; r++) if (units[r] > 0) c.Add((Resource)r, units[r]);
            int left = ScEnclavePeople.Living(e).Count;
            int released = left == 0 ? ScEnclaves.Disband(e, ScEnclaves.CauseJoined) : 0;
            string brought = Words(units);
            if (player != null && joined > 0)
            {
                string said = string.Format(Loc("scEncCallSays"), leaderName.Length > 0 ? leaderName : name, Loc(follow ? "scEncJoinSaidFollow" : "scEncJoinSaid"));
                string building = "";
                if (left == 0)
                {
                    int days = GamePrefs.GetInt(EnumGamePrefs.LootRespawnDays);
                    building = " " + (days > 0 ? string.Format(Loc("scEncJoinBuilding"), days) : Loc("scEncJoinBuildingFree"));
                }
                ScCommunity.Tell(player, said + " " + string.Format(Loc("scEncJoinTold"), name, joined, brought) + building, "");
            }
            if (joined > 0) ScOutposts.NoteLedger(string.Format(Loc("scEncJoinLedger"), name, joined));
            string how = (following > 0 ? following + " following you home" + (arriving > 0 ? ", " : "") : "")
                         + (arriving > 0 ? arriving + " on their own way, at home from " + Clock(at) + " (" + Math.Max(0, ScConfig.EnclaveJoinArriveHours) + " h; "
                                           + walking + " walk off now)" : "");
            string line = name + " joined your community (" + cause + "): " + joined + " people - " + string.Join(", ", names.ToArray()) + "; " + how
                          + "; they brought " + brought
                          + (left == 0 ? "; the group is gone, their building " + e.PoiName + " is free (" + released + " sleeper area(s) respawn after the loot respawn wait)"
                                       : "; " + left + " stay (" + failed + " failed) - the group stands")
                          + (follow && made < 0 ? "; their building's chunks were not settled, so nobody had a body to follow with" : "");
            ScLog.Out("enclave " + e.Id + " '" + name + "': " + line);
            ScRegistry.SaveAtomic();
            return line;
        }

        /// <summary>
        /// "follow": the person's own record joins on this body, as E3's recruit does (ScEnclaveDeals.Recruit: the body's id and
        /// birth, the pair and rounds it carries; LeaveEnclave; ScCommunity.Admit - following until home), with no tooltip of its
        /// own. A join that fails leaves them a stranger, as the recruit's does. True when they joined.
        /// </summary>
        private static bool JoinFollowing(Enclave e, EncPerson p, EntityAlive npc, EntityPlayerLocal player)
        {
            Member m = p.Who;
            m.Id = npc.entityId;
            m.Born = npc.WorldTimeBorn;
            m.Mood = ScRegistry.Community.Morale;
            Loadout lo = ScLoadout.Current(npc);
            if (lo != null) m.Loadout = lo;
            m.Ammo = ScAmmo.Carried(npc, out m.Mags);
            m.Ranged = ScSkills.HoldsRanged(npc);
            ScCommunity.ResetStill(m);
            m.AwaySeconds = 0;
            m.StuckShift = 0;
            if (p.IsLeader) DropStock(npc, true);
            ScEnclavePeople.LeaveEnclave(p, npc);
            string error;
            if (ScCommunity.Admit(m, npc, player, out error, false)) return true;
            npc.SetSpawnerSource(EnumSpawnerSource.StaticSpawner);
            npc.Buffs.SetCustomVar("Persist", 1f);
            ScLog.Err(m.Name + " left " + e.Name + " to join the community, but the join failed (" + (error ?? "?") + ") - they are a stranger now");
            return false;
        }

        /// <summary>
        /// The leader's body leaving the group (E3 never recruits a leader): the group's trade stock (ScEnclaveDeals' copy under
        /// its kind's trader id) goes with the group. Joining on it ("follow"), the body gets back what its class gives every
        /// survivor body - EntityTrader.PostInit's rule, NPCInfo's trader id when it has one (0-XNPCCore 3.4's stock trader) -
        /// with an empty stock; walking off as a husk (classTrader false), no trader at all (X9's trade half refuses "trade").
        /// </summary>
        private static void DropStock(EntityAlive npc, bool classTrader)
        {
            EntityTrader t = npc as EntityTrader;
            if (t == null) return;
            var d = new TraderData();
            if (classTrader && t.NPCInfo != null && t.NPCInfo.TraderID > 0) d.TraderID = t.NPCInfo.TraderID;
            t.TraderData = d;
        }

        /// <summary>
        /// "own" (or no body to follow with): a body out gives its pair and rounds and walks off as a husk; then the person's own
        /// record is a member on the way - keyed by Key, Arriving, due at 'at', from this group (its id, centre and name) - and
        /// leaves the enclave's records in the same step, and a post is chosen (so an older build's "Call back" has one).
        /// </summary>
        private static void JoinArriving(World w, Enclave e, EncPerson p, EntityAlive body, ulong at, Vector3 centre)
        {
            Member m = p.Who;
            if (body != null)
            {
                Loadout lo = ScLoadout.Current(body);
                if (lo != null) m.Loadout = lo;
                m.Ammo = ScAmmo.Carried(body, out m.Mags);
                m.Ranged = ScSkills.HoldsRanged(body);
                if (p.IsLeader) DropStock(body, false);
                ScEnclavePeople.Husk(p, body);
                husks.Add(new Husk { Id = body.entityId, Born = body.WorldTimeBorn, Name = p.Name, Next = body.position });
            }
            m.Id = Key(p);
            m.Born = 0;
            m.Mood = ScRegistry.Community.Morale;
            m.JoinDay = ScCommunity.Today;
            m.Status = MemberStatus.Arriving;
            m.ArriveAt = at;
            m.ArriveEnclave = e.Id;
            m.ArriveFrom = new Vector3i(Mathf.FloorToInt(centre.x), Mathf.FloorToInt(centre.y), Mathf.FloorToInt(centre.z));
            m.ArriveName = e.Name;
            m.MissingDays = 0;
            ScCommunity.ResetStill(m);
            m.AwaySeconds = 0;
            m.StuckShift = 0;
            ScRegistry.Members[m.Id] = m;
            ScEnclavePeople.LeaveEnclave(p, null);
            try
            {
                if (ScZone.HasHome && (m.Post == Vector3i.zero || !ScZone.Contains(m.Post)))
                {
                    int tier;
                    Vector3i post = ScCommunity.ChoosePost(w != null ? w.GetGameRandom() : null, m.BadPosts, out tier);
                    if (post != Vector3i.zero) m.Post = post;          // Station checks it again once its chunk is loaded
                }
            }
            catch (Exception ex)
            {
                ScLog.Once("encjoin-post", ex);         // they joined all the same: Station chooses one on arrival
            }
        }

        // ------------------------------------------------------------------ the bodies walking off ----

        private static bool HomePoint(out Vector3 home)
        {
            home = Vector3.zero;
            if (!ScZone.HasHome) return false;
            Vector3i h = ScZone.Home;
            home = new Vector3(h.x + 0.5f, h.y, h.z + 0.5f);
            return true;
        }

        /// <summary>Each husk: gone with its body (an unload - never saved - or a death), out of the world once nobody sees it, else on its way.</summary>
        private static void TickHusks(World w)
        {
            Vector3 home;
            bool hasHome = HomePoint(out home);
            for (int i = husks.Count - 1; i >= 0; i--)
            {
                Husk h = husks[i];
                EntityAlive npc = ScNpc.As(ScNpc.Loaded(w, h.Id));
                if (npc == null || npc.WorldTimeBorn != h.Born || npc.IsDead()) { husks.RemoveAt(i); continue; }
                if (!ScEnclavePeople.SeenByAny(w, npc.position))
                {
                    ScEnclavePeople.RemoveStray(npc);
                    husks.RemoveAt(i);
                    ScLog.Verbose(h.Name + " is out of sight on the way to your home - the body goes (they turn up at home when due)");
                    continue;
                }
                if (hasHome) Steer(w, h, npc, home);
            }
        }

        /// <summary>
        /// A husk's walk (E3's walk home, ScEnclaveDeals.StepToward): a point up to 40 m on toward the player's home on loaded
        /// ground, the Guard order there; the next when it is reached, or again after HuskResteerSeconds (a closing talk may have
        /// reset the order).
        /// </summary>
        private static void Steer(World w, Husk h, EntityAlive npc, Vector3 home)
        {
            float t = Time.unscaledTime;
            Vector3 pos = npc.position;
            if (h.SteeredAt >= 0f && t - h.SteeredAt < HuskResteerSeconds && ScRunners.XZ(pos, h.Next) > HuskReached) return;
            if (h.SteeredAt < 0f) ScCommunity.DropPath(npc);
            h.SteeredAt = t;
            Vector3 next;
            if (!ScEnclaveDeals.StepToward(w, pos, home, out next)) return;        // there already, or nothing loaded ahead: it stands
            h.Next = next;
            Vector3 d = next - pos;
            d.y = 0f;
            ScEnclavePeople.GuardAt(npc, next, d.sqrMagnitude > 0.01f ? next + d.normalized * 4f : next + Vector3.forward);
        }

        // ------------------------------------------------------------------ the arrival ----

        /// <summary>
        /// ScTicker, every second: the husks; then, with the player at home and the zone loaded, the member on the way who is due
        /// first (the earliest, then the one made first) turns up at a spot ArriveSpot finds, one a second.
        /// </summary>
        public static void Tick(World w, EntityPlayerLocal player)
        {
            try
            {
                if (w == null) return;
                if (husks.Count > 0) TickHusks(w);
                if (player == null || !ScRegistry.Loaded) return;
                Member m = NextDue(w);
                if (m == null || !ScZone.HasHome || !ScZone.HomeLoaded || !ScZone.Contains(player.GetBlockPosition())) { searchFor = -1; return; }
                float t = Time.unscaledTime;
                if (searchFor != m.Id) { searchFor = m.Id; searchSince = t; }
                Vector3 spot;
                if (!ArriveSpot(w, m, t - searchSince >= AnySideSeconds, out spot)) return;
                Arrive(w, player, m, spot);
            }
            catch (Exception e)
            {
                ScLog.Once("encjoin-tick", e);
            }
        }

        /// <summary>The member on the way due first (ArriveAt reached), the one made first among equals; none whose class is gone.</summary>
        private static Member NextDue(World w)
        {
            Member best = null;
            foreach (Member m in ScRegistry.Members.Values)
            {
                if (m.Status != MemberStatus.Arriving || m.ArriveAt > w.worldTime || noBody.Contains(m.Id)) continue;
                int cid;
                if (!ScSpawn.ClassOf(m, out cid))
                {
                    noBody.Add(m.Id);
                    ScLog.Warn(m.Name + " (from " + m.ArriveName + ") cannot turn up: their class " + m.Class + " is not in the game (sc enclave arrivals lists them)");
                    continue;
                }
                if (best == null || m.ArriveAt < best.ArriveAt || (m.ArriveAt == best.ArriveAt && m.Id > best.Id)) best = m;
            }
            return best;
        }

        /// <summary>How far along this line from the claim the zone goes (one block inside its edge), at most ArriveReach.</summary>
        private static float EdgeAlong(Vector3 home, Vector3 dir, int y)
        {
            float last = 0f;
            for (float r = 1f; r <= ArriveReach; r += 1f)
            {
                Vector3 p = home + dir * r;
                if (!ScZone.Contains(new Vector3i(Mathf.FloorToInt(p.x), y, Mathf.FloorToInt(p.z)))) break;
                last = r;
            }
            return Mathf.Max(0f, last - 1f);
        }

        /// <summary>
        /// Where they turn up: up to SpotTries lines from the claim a second, within SideSpread degrees of the building they left
        /// (any side: anySide), each to the zone's edge (EdgeAlong); a ground spot there (ScRunners.GroundSpot), inside the zone,
        /// its chunks settled, and out of every player's sight (ScEnclavePeople.SeenByAny).
        /// </summary>
        private static bool ArriveSpot(World w, Member m, bool anySide, out Vector3 spot)
        {
            spot = Vector3.zero;
            Vector3 home;
            if (!HomePoint(out home)) return false;
            float dx = m.ArriveFrom.x + 0.5f - home.x, dz = m.ArriveFrom.z + 0.5f - home.z;
            bool side = !anySide && dx * dx + dz * dz > 1f;
            float bearing = side ? Mathf.Atan2(dx, dz) * Mathf.Rad2Deg : 0f;
            int hy = ScZone.Home.y;
            for (int i = 0; i < SpotTries; i++)
            {
                float a = (side ? bearing + UnityEngine.Random.Range(-SideSpread, SideSpread) : UnityEngine.Random.Range(0f, 360f)) * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                float r = EdgeAlong(home, dir, hy);
                if (r < 4f) continue;
                Vector3 c = home + dir * r;
                int x = Mathf.FloorToInt(c.x), z = Mathf.FloorToInt(c.z), y;
                if (!ScRunners.GroundSpot(w, x, z, out y)) continue;
                Vector3 at = new Vector3(x + 0.5f, y, z + 0.5f);
                if (!ScZone.Contains(new Vector3i(x, y, z)) || !ScRunners.Settled(w, at) || ScEnclavePeople.SeenByAny(w, at)) continue;
                spot = at;
                return true;
            }
            return false;
        }

        /// <summary>The member's key moves to their first body (no body ever had the old key: nothing to doom).</summary>
        private static void Rekey(Member m, EntityAlive npc, int key)
        {
            ScRegistry.Members.Remove(key);
            m.Id = npc.entityId;
            m.Born = npc.WorldTimeBorn;
            ScRegistry.Members[m.Id] = m;
            Community c = ScRegistry.Community;
            if (c.ThreatMemberId == key) c.ThreatMemberId = m.Id;
        }

        /// <summary>
        /// They turn up: their body from the record (ScSpawn.Body: class, look, pair, rounds, name) at the spot, facing the claim,
        /// re-keyed (Rekey), made a member's body (ScMissing.Restore's shape: the spawner source, Persist, the cvars, traits and AI
        /// package) and stationed - they walk to their post. A tell; the last of their group writes a ledger line; the log, a save.
        /// </summary>
        private static void Arrive(World w, EntityPlayerLocal player, Member m, Vector3 spot)
        {
            Vector3 home;
            HomePoint(out home);
            float yaw = Mathf.Atan2(home.x - spot.x, home.z - spot.z) * Mathf.Rad2Deg;
            EntityAlive npc = null;
            try { npc = ScSpawn.Body(w, m, spot + Nudge, new Vector3(0f, yaw, 0f), true); }
            catch (Exception ex) { ScLog.Once("encjoin-body-" + m.Id, ex); }
            if (npc == null)
            {
                noBody.Add(m.Id);
                ScLog.Warn(m.Name + " (from " + m.ArriveName + "): no body could be made for the class " + m.Class + " - they cannot turn up (sc enclave arrivals lists them)");
                return;
            }
            int key = m.Id;
            string from = m.ArriveName;
            int fromId = m.ArriveEnclave;
            Rekey(m, npc, key);
            m.Status = MemberStatus.Resident;       // here now (Station says so too; never left on the way with a body)
            m.ArriveAt = 0UL;
            m.ArriveEnclave = 0;
            m.ArriveFrom = Vector3i.zero;
            m.ArriveName = "";
            m.MissingDays = 0;
            m.StandInId = -1;
            npc.SetSpawnerSource(EnumSpawnerSource.StaticSpawner);
            npc.Buffs.SetCustomVar("Persist", 1f);
            ScCommunity.StampCvars(m, npc, player);
            ScTraits.ApplyBuffs(m, npc);
            ScCommunity.ApplyAiPackage(npc);
            ScCommunity.Station(m, npc);        // a resident at their post, which they walk to (within ArriveReach + PostRadius of the claim)
            searchFor = -1;
            bool last = true;
            foreach (Member o in ScRegistry.Members.Values)
                if (o.Status == MemberStatus.Arriving && o.ArriveEnclave == fromId) { last = false; break; }
            ScCommunity.Tell(player, string.Format(Loc("scEncJoinArrived"), m.Name, from), "");
            if (last) ScOutposts.NoteLedger(string.Format(Loc("scEncJoinLedgerArrived"), from));
            ScLog.Out(m.Name + " (from " + from + ") turned up at home at " + World.worldToBlockPos(spot) + ", " + Mathf.RoundToInt(ScRunners.XZ(spot, home))
                      + " m from the claim, as entity " + m.Id + " (was key " + key + ") - stationed, their post " + m.Post + (last ? "; the last of " + from : ""));
            ScRegistry.SaveAtomic();
        }

        // ------------------------------------------------------------------ the clock, texts, the console ----

        /// <summary>ScEnclaves.OnWorldDayBack: the world's day went back worldBack days - each arrival keeps its remaining span (never 0). Returns how many moved.</summary>
        internal static int ShiftDays(int worldBack)
        {
            if (worldBack <= 0) return 0;
            ulong ticks = (ulong)worldBack * 24000UL;
            int n = 0;
            foreach (Member m in ScRegistry.Members.Values)
            {
                if (m.Status != MemberStatus.Arriving || m.ArriveAt == 0UL) continue;
                m.ArriveAt = m.ArriveAt > ticks ? m.ArriveAt - ticks : 1UL;
                n++;
            }
            return n;
        }

        /// <summary>"day 12, 18:00": a world time (E9's scEncCallClock).</summary>
        private static string Clock(ulong t)
        {
            return string.Format(Loc("scEncCallClock"), GameUtils.WorldTimeToDays(t), GameUtils.WorldTimeToHours(t).ToString("00") + ":" + GameUtils.WorldTimeToMinutes(t).ToString("00"));
        }

        /// <summary>The community window's status: "On the way" (a row), "On the way from The Holdouts" (the detail panel).</summary>
        public static string StatusText(Member m, bool brief)
        {
            if (brief || m == null || string.IsNullOrEmpty(m.ArriveName)) return Loc("scStArriving");
            return string.Format(Loc("scStArrivingFrom"), m.ArriveName);
        }

        /// <summary>The console's words for one on the way: when they may turn up, or why they can't.</summary>
        public static string WhenText(Member m)
        {
            if (m == null || m.Status != MemberStatus.Arriving) return "";
            World w = ScCommunity.World;
            ulong now = w != null ? w.worldTime : 0UL;
            string from = "on the way from " + (m.ArriveName.Length > 0 ? m.ArriveName : "?") + " (enclave " + m.ArriveEnclave + ", " + m.ArriveFrom + ")";
            if (noBody.Contains(m.Id)) return from + ", no body can be made for them (class " + m.Class + ")";
            if (m.ArriveAt > now) return from + ", at home from " + Clock(m.ArriveAt) + " (in " + ScEncCalls.TimeText(m.ArriveAt - now) + ")";
            return from + ", due - they turn up while you're home";
        }

        /// <summary>"join: ..." for sc enclave info: how many would come and the room, the standing, the row's state, what they'd bring, how they'd come.</summary>
        public static string InfoLine(Enclave e)
        {
            string s;
            if (!Configured) s = "off (" + OffWhy() + ")";
            else if (e.IsHostile) s = "never (they are hostile)";
            else if (e.Lost) s = "none (their building is not in this world)";
            else
            {
                int coming = Coming(e);
                string hidden = HiddenWhy(e, false);
                string blocked = hidden == null ? Blocked(e, ScCommunity.LocalPlayer) : null;
                s = coming + " would come, room for " + RoomLeft() + " more (" + ScRegistry.Members.Count + " of " + ScConfig.MaxMembers + " members); standing " + e.Standing
                    + " (" + ScEnclaves.StandingWord(e) + ", " + ScConfig.EnclaveJoinStanding + " and Allied needed); "
                    + (hidden != null ? "not offered: " + hidden : blocked != null ? "offered, greyed: " + blocked : "offered")
                    + "; they'd bring " + Words(Bundles(e, coming)) + " and " + (Follow ? "follow you home" : "make their own way (" + Math.Max(0, ScConfig.EnclaveJoinArriveHours) + " h)");
            }
            int joining = ArrivingFrom(e);
            return "join: " + s + (joining > 0 ? "; " + joining + " of theirs on the way to you" : "");
        }

        /// <summary>
        /// sc enclave join &lt;id&gt; [sim|now [force]]: their join line; sim - the row as the leader would show it, who would come
        /// (bodies out) and how (nothing changes); now - they join at once, every gate kept (force: the Allied and standing rule
        /// ignored; the room and the rest kept).
        /// </summary>
        public static void Command(Enclave e, List<string> p, List<string> into)
        {
            string verb = p.Count > 3 ? p[3].ToLowerInvariant() : "";
            bool force = p.Count > 4 && p[4].Equals("force", StringComparison.OrdinalIgnoreCase);
            into.Add(e.Name + " (enclave " + e.Id + ", " + ScEnclaves.StandingWord(e) + "): " + InfoLine(e));
            if (verb == "") return;
            if (verb != "sim" && verb != "now") { into.Add("sc enclave join <id> [sim|now [force]]"); return; }
            EntityPlayerLocal pl = ScCommunity.LocalPlayer;
            if (!ScCommunity.IsHost || pl == null) { into.Add("host only, with a player"); return; }
            string hidden = HiddenWhy(e, force);
            string blocked = hidden == null ? Blocked(e, pl) : null;
            if (verb == "sim")
            {
                into.Add("  the row: " + (hidden != null ? "not offered (" + hidden + ")" : blocked != null ? "greyed: " + WhyText(e, pl) : "open: " + Loc("scEncJoinAsk")));
                var who = new List<string>();
                int bodies = 0;
                foreach (EncPerson q in ScEnclavePeople.Living(e))
                {
                    bool body = ScEnclavePeople.OwnBody(q) != null;
                    if (body) bodies++;
                    who.Add(q.Name + (q.IsLeader ? " (leader)" : "") + (body ? " [body out]" : ""));
                }
                int unmade = Coming(e) - who.Count;
                into.Add("  who would come: " + (who.Count > 0 ? string.Join(", ", who.ToArray()) : "nobody made yet") + (unmade > 0 ? " and " + unmade + " not made yet" : ""));
                World w = ScCommunity.World;
                ulong at = (w != null ? w.worldTime : 0UL) + (ulong)Math.Max(0, ScConfig.EnclaveJoinArriveHours) * 1000UL;
                into.Add("  they'd bring " + Words(Bundles(e, Coming(e))) + "; " + (Follow ? "those with a body follow you home (the rest come on their own)"
                         : "they'd make their own way, at home from " + Clock(at) + "; " + bodies + " body(ies) out would walk off"));
                return;
            }
            if (hidden != null) { into.Add("refused: " + hidden); return; }
            if (blocked != null) { into.Add("refused: " + blocked); return; }
            into.Add(JoinNow(e, pl, "the console"));
        }

        /// <summary>sc enclave arrivals [now]: everyone on the way to you and when; now - each may turn up at once (you still need to be home).</summary>
        public static void ArrivalsCommand(List<string> p, List<string> into)
        {
            bool now = p.Count > 2 && p[2].Equals("now", StringComparison.OrdinalIgnoreCase);
            var list = new List<Member>();
            foreach (Member m in ScRegistry.Members.Values) if (m.Status == MemberStatus.Arriving) list.Add(m);
            if (list.Count == 0) { into.Add("nobody is on the way to you"); return; }
            list.Sort((a, b) => a.ArriveAt.CompareTo(b.ArriveAt));
            World w = ScCommunity.World;
            EntityPlayerLocal pl = ScCommunity.LocalPlayer;
            if (now && w != null)
            {
                ulong t = Math.Max(1UL, w.worldTime);
                foreach (Member m in list) if (m.ArriveAt > t) m.ArriveAt = t;
                ScRegistry.SaveAtomic();
            }
            bool home = pl != null && ScZone.HasHome && ScZone.Contains(pl.GetBlockPosition());
            into.Add(list.Count + " on the way to you" + (now ? " - all due now" : "") + "; you are " + (home ? "home: they turn up one a second, out of sight at the edge of the zone"
                     : "not home: they turn up once you are"));
            foreach (Member m in list) into.Add("  " + m.Id + " " + m.Name + " (" + m.Class + ") - " + WhenText(m));
        }
    }
}
