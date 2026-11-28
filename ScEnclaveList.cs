using System;
using System.Collections.Generic;
using System.Globalization;
using Platform;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>Round en1: one row of the community window's ENCLAVES tab (and the no-community page's enclave list).</summary>
    public sealed class EnclaveEntry
    {
        public int Id;
        public string Name = "";
        /// <summary>The level: ScEnclaveList.Rank* (hostile 0, neutral 1, friendly 2, allied 3).</summary>
        public int Rank;
        public int Standing;
        /// <summary>The points the bar runs to: Friendly's 30, Allied's 70, the most there is (100) once Allied; -1 hostile (no bar).</summary>
        public int Next;
        public float Fill;
        /// <summary>From the viewer to the building's footprint (meters, XZ): the sort's second key.</summary>
        public float Distance;
        public bool Hostile;
        /// <summary>How many people they are (-1: not known here).</summary>
        public int People = -1;
        /// <summary>ScEnclaveList.Badge*: the request or favor the row shows.</summary>
        public int Badge;
        /// <summary>A vanilla waypoint of the viewer's marks it (ScEnclaveList.ToggleMark).</summary>
        public bool Marked;
        // ---- the texts (ScEnclaveList.Texts; Localization) ----
        public string Level = "", LevelColor = ScEnclaveList.ColorDim, Points = "", NoBar = "", Info = "", Sub = "",
                      BadgeText = "", BadgeColor = ScEnclaveList.ColorDim, Where = "", About = "", NextText = "", StandingText = "", RequestText = "";
    }

    /// <summary>
    /// Round en1 (2026-11-28, the user's decision 2): the ENCLAVES tab - every enclave the viewer's record knows (their community's, or
    /// their own relations in PerPlayer/PerParty), each with its standing level, a bar toward the next level ("42 / 70 to Allied";
    /// Allied runs to the most there is, 100; hostile has no bar), its kind and size, and a badge for a request or favor open or taken;
    /// sorted by level and standing (best first), then by distance. A row clicked marks the enclave with a vanilla waypoint on this
    /// machine (tracked: the compass and the on-screen distance show it; SC's own enclave marker is map-only) - client-side only.
    /// The numbers (Entries) read no Localization (the offline harness builds them); Texts adds the words.
    /// What a client cannot work out comes in the derived block (WriteDerived, ScMirror.BuildDerivedIn): each known enclave's head count
    /// ("encn.&lt;id&gt;": a client has no enclave people) and its favor state ("encf.&lt;id&gt;": a favor's enclave is its person's).
    /// The standing, kind, hostility, benefit and the community's requests are in the snapshot already ([enclave], [encrequest]).
    /// </summary>
    public static class ScEnclaveList
    {
        public const int FriendlyAt = 30, AlliedAt = 70, MostStanding = 100;
        public const int RankHostile = 0, RankNeutral = 1, RankFriendly = 2, RankAllied = 3;
        /// <summary>
        /// The badges, in the order one beats another: a siege, then a request taken, a favor taken, a request open, a favor open, and
        /// (round en1 (review): EN1-n2) a favor done - its person joins when asked.
        /// </summary>
        public const int BadgeNone = 0, BadgeFavourDone = 1, BadgeFavourOpen = 2, BadgeRequestOpen = 3, BadgeFavourTaken = 4, BadgeRequestTaken = 5, BadgeSiege = 6;
        /// <summary>A favor's state, as encf.&lt;id&gt; sends it: 1 offered, 2 taken on, 3 done (round en1 (review): EN1-n2 - done was "taken").</summary>
        public const int FavourNone = 0, FavourOffered = 1, FavourTaken = 2, FavourDone = 3;
        /// <summary>The derived block's keys (WriteDerived).</summary>
        public const string KeyPeople = "encn.", KeyFavour = "encf.";
        public const string ColorBody = "226,220,208,255", ColorDim = "190,182,166,255", ColorGold = "206,178,120,255", ColorGood = "142,208,108,255",
                            ColorBad = "226,108,86,255", ColorDanger = "240,150,130,255", ColorAlly = "90,170,255,255", ColorFriend = "140,220,120,255";
        /// <summary>The waypoint's icon (vanilla sprites; SC's enclave markers use the same).</summary>
        public const string IconEnclave = "ui_game_symbol_map_fortress", IconHostile = "ui_game_symbol_skull";

        public static int Rank(Enclave e)
        {
            return e.IsHostile ? RankHostile : e.Standing >= AlliedAt ? RankAllied : e.Standing >= FriendlyAt ? RankFriendly : RankNeutral;
        }

        /// <summary>The points the bar runs to (ScEnclaves.StandingKey's bands; AddStanding's clamp is the most); -1 hostile.</summary>
        public static int NextAt(Enclave e)
        {
            switch (Rank(e))
            {
                case RankHostile: return -1;
                case RankAllied: return MostStanding;
                case RankFriendly: return AlliedAt;
                default: return FriendlyAt;
            }
        }

        /// <summary>
        /// The bar's fill. Round en1 (review): EN1-n8 - the way through the level, as an XP bar fills (it read points over the next
        /// level's, so it jumped back at each level): Neutral 0 to 30 (below zero: empty), Friendly 30 to 70, Allied 70 to the most
        /// (100); hostile none. The text still says the points over the next level's ("42 / 70 to Allied").
        /// </summary>
        public static float FillOf(Enclave e)
        {
            int s = e.Standing;
            switch (Rank(e))
            {
                case RankHostile: return 0f;
                case RankAllied: return Mathf.Clamp01((s - AlliedAt) / (float)(MostStanding - AlliedAt));
                case RankFriendly: return Mathf.Clamp01((s - FriendlyAt) / (float)(AlliedAt - FriendlyAt));
                default: return Mathf.Clamp01(s / (float)FriendlyAt);
            }
        }

        /// <summary>
        /// How many people they are: the living once their group was made (ScEnclavePeople.Living: at home, enlisted, out raiding), else
        /// the size it will be made at (GroupSize). A client: the derived block's encn. (-1 until it came). -1 when it cannot be told.
        /// </summary>
        public static int People(Enclave e)
        {
            if (e == null) return -1;
            if (ScMirror.Active) return ScMirror.Int(KeyPeople + e.Id.ToString(CultureInfo.InvariantCulture), -1);
            try { return e.PeopleRolled ? ScEnclavePeople.Living(e).Count : ScEnclavePeople.GroupSize(e); }
            catch (Exception) { return -1; }
        }

        /// <summary>
        /// The server's side (and the host's own window): each enclave's favor state in the record in scope - a favor (E3) belongs to
        /// one of an enclave's people (Prospect.EncPersonId): taken (started or done) beats offered.
        /// </summary>
        public static Dictionary<int, int> FavourStates()
        {
            var map = new Dictionary<int, int>();
            var personEnclave = new Dictionary<int, int>();
            foreach (EncPerson p in ScRegistry.EncPeople) if (p != null) personEnclave[p.Id] = p.EnclaveId;
            foreach (Prospect pr in ScRegistry.Prospects.Values)
            {
                int eid, cur;
                if (pr == null || pr.EncPersonId <= 0 || !personEnclave.TryGetValue(pr.EncPersonId, out eid)) continue;
                int st = pr.Done ? FavourDone : pr.Started ? FavourTaken : FavourOffered;     // round en1 (review): EN1-n2
                if (!map.TryGetValue(eid, out cur) || FavourBadge(st) > FavourBadge(cur)) map[eid] = st;
            }
            return map;
        }

        /// <summary>Round en1 (review): EN1-n2 - the badge a favor state shows (taken beats offered beats done).</summary>
        public static int FavourBadge(int state)
        {
            return state == FavourTaken ? BadgeFavourTaken : state == FavourOffered ? BadgeFavourOpen : state == FavourDone ? BadgeFavourDone : BadgeNone;
        }

        /// <summary>An enclave's favor state: the host's map (FavourStates), a client's derived block (encf.).</summary>
        public static int FavourOf(Enclave e, Dictionary<int, int> hostMap)
        {
            if (ScMirror.Active) return Mathf.Clamp(ScMirror.Int(KeyFavour + e.Id.ToString(CultureInfo.InvariantCulture), FavourNone), FavourNone, FavourDone);
            int v;
            return hostMap != null && hostMap.TryGetValue(e.Id, out v) ? v : FavourNone;
        }

        /// <summary>The record in scope's requests per enclave, each enclave's strongest (a siege, taken, open). A client has them too ([encrequest]).</summary>
        public static Dictionary<int, int> RequestBadges()
        {
            var map = new Dictionary<int, int>();
            foreach (EnclaveRequest r in ScEncRequests.All)
            {
                if (r == null) continue;
                int b = r.Kind == EnclaveRequest.KindSiege ? BadgeSiege : r.IsAccepted ? BadgeRequestTaken : r.IsOffered ? BadgeRequestOpen : BadgeNone;
                int cur;
                if (b != BadgeNone && (!map.TryGetValue(r.EnclaveId, out cur) || b > cur)) map[r.EnclaveId] = b;
            }
            return map;
        }

        /// <summary>
        /// Every enclave the record in scope knows (and this load found: Lost ones are off the map), with the numbers - no words -
        /// sorted (Sort). from: the viewer's position (the distances).
        /// </summary>
        public static List<EnclaveEntry> Entries(Vector3 from)
        {
            var list = new List<EnclaveEntry>();
            Dictionary<int, int> requests = RequestBadges();
            Dictionary<int, int> favours = ScMirror.Active ? null : FavourStates();
            foreach (Enclave e in ScEnclaves.All)
            {
                if (e == null || !e.Known || e.Lost) continue;
                int req;
                requests.TryGetValue(e.Id, out req);
                int fav = FavourOf(e, favours);
                int favBadge = FavourBadge(fav);
                list.Add(new EnclaveEntry
                {
                    Id = e.Id,
                    Name = e.Name ?? "",
                    Rank = Rank(e),
                    Standing = e.Standing,
                    Next = NextAt(e),
                    Fill = FillOf(e),
                    Distance = e.DistanceXZ(from.x, from.z),
                    Hostile = e.IsHostile,
                    People = People(e),
                    Badge = Math.Max(req, favBadge)
                });
            }
            Sort(list);
            return list;
        }

        /// <summary>By level, then standing (best first), then the nearest; the name, then the id, last (a stable order).</summary>
        public static void Sort(List<EnclaveEntry> list)
        {
            list.Sort((a, b) =>
            {
                int c = b.Rank.CompareTo(a.Rank);
                if (c == 0) c = b.Standing.CompareTo(a.Standing);
                if (c == 0) c = a.Distance.CompareTo(b.Distance);
                if (c == 0) c = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                return c != 0 ? c : a.Id.CompareTo(b.Id);
            });
        }

        /// <summary>Entries with their words (Texts): what the window shows.</summary>
        public static List<EnclaveEntry> Build(Vector3 from)
        {
            List<EnclaveEntry> list = Entries(from);
            foreach (EnclaveEntry x in list)
            {
                Enclave e = ScEnclaves.ById(x.Id);
                if (e != null) Texts(x, e, from);
            }
            return list;
        }

        private static string Loc(string key) { return Localization.Get(key); }

        /// <summary>The row's and the detail's words, in this machine's language.</summary>
        public static void Texts(EnclaveEntry x, Enclave e, Vector3 from)
        {
            x.Level = ScEnclaves.StandingWord(e);
            x.LevelColor = x.Rank == RankHostile ? ColorDanger : x.Rank == RankAllied ? ColorAlly : x.Rank == RankFriendly ? ColorFriend : ColorDim;
            string nextWord = Loc(x.Next == AlliedAt ? "scEnclaveAllied" : "scEnclaveFriendly");
            if (x.Rank == RankHostile)
            {
                x.Points = "";
                x.NoBar = Loc("scUiEncHostileBar");
                x.NextText = Loc("scUiEncNextNone");
                x.StandingText = x.Level;
            }
            else
            {
                x.Points = x.Rank == RankAllied ? string.Format(Loc("scUiEncPointsMost"), x.Standing, MostStanding)
                         : string.Format(Loc("scUiEncPointsNext"), x.Standing, x.Next, nextWord);
                x.NextText = x.Rank != RankAllied ? string.Format(Loc("scUiEncNextTo"), x.Next - x.Standing, nextWord)
                           : x.Standing >= MostStanding ? Loc("scUiEncNextMost") : string.Format(Loc("scUiEncNextAllied"), MostStanding - x.Standing);
                x.StandingText = string.Format(Loc("scUiEncStandingValue"), x.Standing, x.Level);
            }
            string kind = e.Hostile ? Loc("scUiEncKindRaiders") : ScEnclavePeople.KindName(e.Kind);
            string people = x.People < 0 ? "" : x.People == 1 ? Loc("scUiEncPeopleOne") : string.Format(Loc("scUiEncPeople"), x.People);
            x.Where = ScEnclaves.Where(from, e.Centre);
            x.Info = people.Length > 0 ? string.Format(Loc("scUiEncInfo"), kind, people, x.Where) : string.Format(Loc("scUiEncInfoNoCount"), kind, x.Where);
            x.Sub = people.Length > 0 ? string.Format(Loc("scUiEncSub"), kind, people) : kind;
            switch (x.Badge)
            {
                case BadgeSiege: x.BadgeText = Loc("scUiEncBadgeSiege"); x.BadgeColor = ColorBad; break;
                case BadgeRequestTaken: x.BadgeText = Loc("scUiEncBadgeTaken"); x.BadgeColor = ColorGold; break;
                case BadgeFavourTaken: x.BadgeText = Loc("scUiEncBadgeFavourTaken"); x.BadgeColor = ColorGold; break;
                case BadgeRequestOpen: x.BadgeText = Loc("scUiEncBadgeOpen"); x.BadgeColor = ColorGood; break;
                case BadgeFavourOpen: x.BadgeText = Loc("scUiEncBadgeFavour"); x.BadgeColor = ColorGood; break;
                case BadgeFavourDone: x.BadgeText = Loc("scUiEncBadgeFavourDone"); x.BadgeColor = ColorGood; break;     // round en1 (review): EN1-n2
                default: x.BadgeText = ""; x.BadgeColor = ColorDim; break;
            }
            x.RequestText = x.Badge == BadgeNone ? Loc("scUiEncNoRequest") : x.BadgeText;
            string about = Loc(x.Rank == RankHostile ? "scUiEncAboutHostile" : x.Rank == RankAllied ? "scUiEncAboutAllied"
                               : x.Rank == RankFriendly ? "scUiEncAboutFriendly" : "scUiEncAboutNeutral");
            string benefit = !e.IsHostile && !string.IsNullOrEmpty(e.Benefit) ? ScEncAllies.Name(e.Benefit) : "";
            if (benefit.Length > 0) about += "\n\n" + string.Format(Loc(x.Rank == RankAllied ? "scUiEncBenefitNow" : "scUiEncBenefit"), benefit);
            x.About = about;
        }

        /// <summary>
        /// Server (ScMirror.BuildDerivedIn, in the community's scope): each known enclave's head count and its favor state (none: no
        /// line), for the clients' ENCLAVES tab.
        /// </summary>
        public static void WriteDerived(Action<string, int> kv)
        {
            Dictionary<int, int> favours = FavourStates();
            foreach (Enclave e in ScEnclaves.All)
            {
                if (e == null || !e.Known || e.Lost) continue;
                string id = e.Id.ToString(CultureInfo.InvariantCulture);
                kv(KeyPeople + id, People(e));
                int f;
                if (favours.TryGetValue(e.Id, out f) && f != FavourNone) kv(KeyFavour + id, f);
            }
        }

        // ------------------------------------------------------------------ the map mark (this machine only) ----

        /// <summary>Where the waypoint goes: the building's center, at its footprint's base.</summary>
        public static Vector3i MarkPos(Enclave e)
        {
            Vector3 c = e.Centre;
            return new Vector3i(Mathf.FloorToInt(c.x), e.Poi.y, Mathf.FloorToInt(c.z));
        }

        /// <summary>
        /// Round en1 (review): EN1-m4 - this machine's marks by enclave id: the name each mark was found or made under. A waypoint keeps
        /// only its spot, icon and name, so a mark is found again by SC's icons and the group's name (names are never given twice);
        /// the map remembers which group went (SyncMarks: its record is gone, so its name is too). Reset with the world.
        /// </summary>
        private static readonly Dictionary<int, string> marks = new Dictionary<int, string>();
        private static World marksWorld;

        private static bool IsScMark(Waypoint w)
        {
            return w != null && w.name != null && (w.icon == IconEnclave || w.icon == IconHostile);
        }

        /// <summary>The viewer's waypoint on this enclave (SC's icon and its name, wherever it stands - a group that moved keeps it), or null.</summary>
        private static Waypoint FindMark(EntityPlayerLocal pl, Enclave e)
        {
            if (pl == null || e == null || pl.Waypoints == null || pl.Waypoints.Collection == null) return null;
            return FindMarkByName(pl, e.Name ?? "");
        }

        private static Waypoint FindMarkByName(EntityPlayerLocal pl, string name)
        {
            List<Waypoint> list = pl.Waypoints.Collection.list;
            for (int i = 0; i < list.Count; i++)
                if (IsScMark(list[i]) && list[i].name.Text == name) return list[i];
            return null;
        }

        /// <summary>The ids of the enclaves (of these) the viewer has a waypoint on.</summary>
        public static HashSet<int> MarkedIds(EntityPlayerLocal pl, List<EnclaveEntry> entries)
        {
            var set = new HashSet<int>();
            if (pl == null) return set;
            foreach (EnclaveEntry x in entries)
                if (FindMark(pl, ScEnclaves.ById(x.Id)) != null) set.Add(x.Id);
            return set;
        }

        /// <summary>A waypoint out of the player's list and off the map and compass.</summary>
        private static void Unmark(EntityPlayerLocal pl, Waypoint w)
        {
            pl.Waypoints.Collection.Remove(w);
            if (w.navObject != null && NavObjectManager.Instance != null) NavObjectManager.Instance.UnRegisterNavObject(w.navObject);
            w.navObject = null;
        }

        /// <summary>The waypoint's nav object (vanilla's waypoint class), active while it is the tracked one.</summary>
        private static void Show(Waypoint w, string name)
        {
            if (NavObjectManager.Instance == null) return;
            w.navObject = NavObjectManager.Instance.RegisterNavObject("waypoint", w.pos.ToVector3(), w.icon);
            if (w.navObject == null) return;
            w.navObject.IsActive = w.bTracked;
            w.navObject.usingLocalizationId = false;
            w.navObject.name = name ?? "";
        }

        /// <summary>
        /// Round en1 (review): EN1-m4 - the viewer's marks kept with their groups (BuildEnclaves, each refresh): a group that moved has
        /// its mark moved to its new building (and its icon to its standing), a second mark on one group goes, and the mark of a group
        /// that fell goes (its record went this session, or it is a lost group a refill is owed for). A group this load did not find
        /// keeps its mark. This machine only.
        /// </summary>
        public static void SyncMarks(EntityPlayerLocal pl)
        {
            if (pl == null || pl.Waypoints == null || pl.Waypoints.Collection == null || !ScRegistry.Ready) return;
            World w = ScCommunity.World;
            if (!ReferenceEquals(w, marksWorld)) { marks.Clear(); marksWorld = w; }
            var byName = new Dictionary<string, Enclave>(StringComparer.Ordinal);
            foreach (Enclave e in ScEnclaves.All) if (e != null && !string.IsNullOrEmpty(e.Name) && !byName.ContainsKey(e.Name)) byName[e.Name] = e;
            if (byName.Count == 0) return;                       // no copy of the enclaves here yet: nothing to judge by
            var fell = new HashSet<string>(StringComparer.Ordinal);
            foreach (EncRefill r in ScRegistry.WorldState.EncRefills) if (r != null && !string.IsNullOrEmpty(r.Name) && !byName.ContainsKey(r.Name)) fell.Add(r.Name);
            foreach (KeyValuePair<int, string> kv in new List<KeyValuePair<int, string>>(marks))
                if (ScEnclaves.ById(kv.Key) == null) { if (!byName.ContainsKey(kv.Value)) fell.Add(kv.Value); marks.Remove(kv.Key); }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (Waypoint wp in new List<Waypoint>(pl.Waypoints.Collection.list))
            {
                if (!IsScMark(wp)) continue;
                string n = wp.name.Text ?? "";
                Enclave e;
                if (byName.TryGetValue(n, out e))
                {
                    if (!seen.Add(n)) { Unmark(pl, wp); continue; }      // never two marks on one group
                    marks[e.Id] = n;
                    if (e.Lost) continue;                                 // not found this load: it may come back where it was
                    // round en1 (re-check EN1R-m2): a group that moved without telling this player's community is off its map until
                    // found again - its mark stays where it was and never follows it (it would reveal the new building)
                    if (!e.Known) continue;
                    Vector3i at = MarkPos(e);
                    string icon = e.IsHostile ? IconHostile : IconEnclave;
                    if (wp.pos == at && wp.icon == icon) continue;
                    // moved (or turned hostile): out of the list while its spot changes - the list hashes the spot and the icon
                    Unmark(pl, wp);
                    wp.pos = at;
                    wp.icon = icon;
                    pl.Waypoints.Collection.Add(wp);
                    Show(wp, n);
                }
                else if (fell.Contains(n)) Unmark(pl, wp);
            }
        }

        /// <summary>
        /// A row clicked (or the detail's button): the enclave's waypoint goes when there is one, else one is made - tracked (the compass
        /// and the on-screen distance; vanilla's waypoint class), the others untracked, as the map's own track button does. This
        /// machine's player only: nothing is sent (the waypoint is the player's own, in their player data, removable on the map).
        /// True when it is marked now. Round en1 (review): EN1-m3 - the click sounds play in the player's head (vanilla's map does the
        /// same): PlayOneShot broadcast them; EN1-m4 - one mark per group, found by its name wherever it stands.
        /// </summary>
        public static bool ToggleMark(EntityPlayerLocal pl, Enclave e)
        {
            if (pl == null || e == null || pl.Waypoints == null) return false;
            Waypoint have = FindMark(pl, e);
            if (have != null)
            {
                Unmark(pl, have);
                marks.Remove(e.Id);
                Audio.Manager.PlayInsidePlayerHead("ui_waypoint_delete");
                return false;
            }
            var w = new Waypoint();
            w.pos = MarkPos(e);
            w.icon = e.IsHostile ? IconHostile : IconEnclave;
            w.name.Update(e.Name ?? "", PlatformManager.MultiPlatform.User.PlatformUserId);
            List<Waypoint> list = pl.Waypoints.Collection.list;
            for (int i = 0; i < list.Count; i++)
            {
                Waypoint o = list[i];
                if (o == null || !o.bTracked) continue;
                o.bTracked = false;
                if (o.navObject != null) o.navObject.IsActive = false;
            }
            w.bTracked = true;
            pl.Waypoints.Collection.Add(w);
            Show(w, e.Name);
            marks[e.Id] = e.Name ?? "";
            Audio.Manager.PlayInsidePlayerHead("ui_waypoint_add");
            return true;
        }
    }
}
