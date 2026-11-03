using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Enclaves move, milestone E11 (research\ideas\DESIGN.md section 8, with the user's decision Q9: a Neutral or hostile group
    /// that moves drops off the map until found again, a Friendly or Allied one tells you where; both reasons apply).
    ///
    /// The reasons, on the request clock (ScEncRequests.Now: "sc day" moves them):
    ///   lost   at least EnclaveMoveLostShare of the group is dead - the dead records, and the people E10's held raids cost a
    ///          group before it was made (Enclave.RaidLost), out of its records (a group still being made: its full size) and
    ///          those - EnclaveMoveWaitDays or more after the last death (Enclave.LossDay, stamped at every death), and that
    ///          death after their last move (a move answers the losses before it);
    ///   nest   an infestation within EnclaveMoveInfestRange m of their building (footprint to footprint, while Infestations is
    ///          on) for EnclaveMoveInfestDays days. The daily pass keeps the stamp (Enclave.NestDay): at its first sight the day
    ///          before the pass (so the first rollover after it came counts as its first day), cleared when none is in range.
    /// Once a reason holds and nothing holds them (WhyHeld), the daily pass (Daily, ScDaily.Run after the infestations) rolls
    /// EnclaveMoveChance and picks the building (ScEnclaves.MoveTarget), both from the world seed, the group and the day. No
    /// building fits: they stay and try again the next day.
    ///
    /// What holds a group where it is (WhyHeld; the design's "never while", made exact): their building is not in this world;
    /// a request of theirs that is accepted (a siege is one - an offered request holds nothing: it is withdrawn, no loss, as they
    /// go); a radio call of theirs that is on (E9); a demand, raid call or raid party of theirs on one of your outposts (E10);
    /// anyone of theirs away - enlisted with you (E3, E9's fighter), missing (E5) or raiding (E10), or on the way to join you
    /// (E12: a whole-group join cut short leaves the rest standing); a player within
    /// EnclaveSpawnRange + EnclaveDespawnMargin m of their building; their building in loaded chunks or the player's view
    /// distance (the late seed's "loaded"); any body of theirs in the world. A group still being made may move (E10's raider
    /// dens are mostly never met): it is made to the new building's size.
    ///
    /// The move (Move): an offered request withdrawn, the visit let go, Validate's building swap (ScEnclaves.MoveTo: the old
    /// building's rooms respawn after the loot respawn wait and its loot, claims and quests are free; the new one's rooms are
    /// kept clear), the stamps, their favours' return points at the new building. A Friendly or Allied group says why on the
    /// radio and where they went (its marker moves; a ledger line); any other drops off the map until it is found again (E1's
    /// approach, the radio, an ally - E6), with "X have left Y" when you knew them. They keep their id, name, kind, nature,
    /// benefit, standing, trade stock, thanks owed and their dead.
    ///
    /// Saved: [enclave] moved=day:x,y,z:name, lossday=N, nestday=N (ScRegistry; shifted by ScEnclaves.OnWorldDayBack). Host
    /// only, main thread; no Harmony patch.
    /// </summary>
    public static class ScEncMoves
    {
        public const string ReasonLost = "lost", ReasonNest = "nest", ReasonConsole = "console";
        private const string Salt = "sc.encmoves.v1";

        /// <summary>The request-clock day of this session's last daily pass: a catch-up's later days (the same clock day) do nothing.</summary>
        private static int passDay = -1;

        public static void Reset()
        {
            passDay = -1;
        }

        private static string Loc(string key) { return Localization.Get(key); }

        private static string Pct(float f) { return Mathf.RoundToInt(f * 100f) + "%"; }

        // ------------------------------------------------------------------ the switches ----

        public static bool Configured { get { return ScConfig.Enclaves && ScConfig.EnclaveMoves; } }

        /// <summary>Configured, and this session's enclaves validated (ScEnclaves.Ready).</summary>
        public static bool On { get { return Configured && ScEnclaves.Ready; } }

        /// <summary>Why moves are off ("on" when they are not).</summary>
        public static string OffWhy()
        {
            if (!ScConfig.Enclaves) return "Enclaves=false";
            if (!ScConfig.EnclaveMoves) return "EnclaveMoves=false";
            return ScEnclaves.Ready ? "on" : "the world is still starting";
        }

        // ------------------------------------------------------------------ reason 1: losses ----

        /// <summary>A death of theirs (ScEnclavePeople.Died, ScEncSearch's lost survivor, E10's held raid): the request-clock day.</summary>
        internal static void NoteLoss(Enclave e)
        {
            if (e != null) e.LossDay = ScEncRequests.Now;
        }

        /// <summary>
        /// Their dead and the group they came from: the dead records plus Enclave.RaidLost, out of the records (a group still being
        /// made: its full size, GroupSize, when that is more) plus Enclave.RaidLost.
        /// </summary>
        public static void Losses(Enclave e, out int dead, out int total)
        {
            int records = 0;
            dead = 0;
            foreach (EncPerson p in ScRegistry.EncPeople)
            {
                if (p.EnclaveId != e.Id) continue;
                records++;
                if (p.State == EncPerson.StateDead) dead++;
            }
            int raidLost = Math.Max(0, e.RaidLost);
            total = (e.PeopleRolled ? records : Math.Max(records, ScEnclavePeople.GroupSize(e))) + raidLost;
            dead += raidLost;
        }

        /// <summary>The day of their last death: Enclave.LossDay, or for deaths from before E11 the newest dead record's day (-1: none).</summary>
        public static int LastLossDay(Enclave e)
        {
            if (e.LossDay >= 0) return e.LossDay;
            int last = -1;
            foreach (EncPerson p in ScRegistry.EncPeople)
                if (p.EnclaveId == e.Id && p.State == EncPerson.StateDead && p.DiedDay > last) last = p.DiedDay;
            return last;
        }

        /// <summary>Reason 1 (see the class): true when it holds; text: its state for the console.</summary>
        public static bool LostReason(Enclave e, int now, out string text)
        {
            int dead, total;
            Losses(e, out dead, out total);
            float share = total > 0 ? dead / (float)total : 0f;
            var sb = new StringBuilder();
            sb.Append("lost ").Append(dead).Append(" of ").Append(total).Append(" (").Append(Pct(share)).Append("; ").Append(Pct(ScConfig.EnclaveMoveLostShare))
              .Append(" moves them)");
            if (dead <= 0) { text = sb.ToString(); return false; }
            bool enough = share + 0.0001f >= ScConfig.EnclaveMoveLostShare;
            int last = LastLossDay(e);
            if (last >= 0) sb.Append(", the last on day ").Append(last);
            bool before = e.MovedDay >= 0 && last <= e.MovedDay;
            if (before) sb.Append(", before their move on day ").Append(e.MovedDay);
            int from = last >= 0 ? last + Math.Max(0, ScConfig.EnclaveMoveWaitDays) : 0;
            bool waiting = enough && !before && now < from;
            if (waiting) sb.Append(", a reason from day ").Append(from);
            text = sb.ToString();
            return enough && !before && !waiting;
        }

        // ------------------------------------------------------------------ reason 2: the dead next door ----

        /// <summary>XZ metres between two inclusive footprints {xMin, xMax, zMin, zMax} (0 when they touch): ScEncRaids.RangeTo's measure.</summary>
        public static float Gap(int[] a, int[] b)
        {
            float dx = Math.Max(0, Math.Max(a[0] - b[1], b[0] - a[1]));
            float dz = Math.Max(0, Math.Max(a[2] - b[3], b[2] - a[3]));
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>The nearest infestation within EnclaveMoveInfestRange m of a footprint (footprint to footprint) while Infestations is on, or null.</summary>
        public static Infestation NestNear(Vector3i key, Vector3i size, out float metres)
        {
            metres = float.MaxValue;
            if (!ScConfig.Infestations || ScConfig.EnclaveMoveInfestRange <= 0) return null;
            int[] a = { key.x, key.x + Math.Max(1, size.x) - 1, key.z, key.z + Math.Max(1, size.z) - 1 };
            Infestation best = null;
            foreach (Infestation inf in ScInfestations.All)
            {
                float d = Gap(a, inf.Box);
                if (d <= ScConfig.EnclaveMoveInfestRange && d < metres) { metres = d; best = inf; }
            }
            return best;
        }

        public static Infestation NestNear(Vector3i key, Vector3i size)
        {
            float m;
            return NestNear(key, size, out m);
        }

        /// <summary>The daily pass's nest stamp: set at the first sight (the day before the pass), cleared with none in range. True when it changed.</summary>
        private static bool UpdateNest(Enclave e, int now)
        {
            if (NestNear(e.Poi, e.Size) == null)
            {
                if (e.NestDay < 0) return false;
                e.NestDay = -1;
                return true;
            }
            if (e.NestDay >= 0) return false;
            e.NestDay = Math.Max(0, now - 1);
            return true;
        }

        /// <summary>Reason 2 (see the class): true when it holds; text: its state for the console.</summary>
        public static bool NestReason(Enclave e, int now, out string text)
        {
            if (!ScConfig.Infestations || ScConfig.EnclaveMoveInfestRange <= 0)
            {
                text = "nests not counted (" + (!ScConfig.Infestations ? "Infestations=false" : "EnclaveMoveInfestRange 0") + ")";
                return false;
            }
            float m;
            Infestation inf = NestNear(e.Poi, e.Size, out m);
            if (inf == null) { text = "no infestation within " + ScConfig.EnclaveMoveInfestRange + " m"; return false; }
            int days = e.NestDay >= 0 ? Math.Max(0, now - e.NestDay) : 0;
            text = "infestation #" + inf.Id + " at " + inf.PoiName + " " + Mathf.RoundToInt(m) + " m away"
                   + (e.NestDay >= 0 ? ", next door since day " + e.NestDay + " (" + days + " of " + ScConfig.EnclaveMoveInfestDays + " day(s))" : ", counted from the next rollover");
            return e.NestDay >= 0 && days >= ScConfig.EnclaveMoveInfestDays;
        }

        /// <summary>The reason that holds today (lost first), or null.</summary>
        public static string Reason(Enclave e, int now)
        {
            string t;
            if (LostReason(e, now, out t)) return ReasonLost;
            if (NestReason(e, now, out t)) return ReasonNest;
            return null;
        }

        private static string ReasonWords(string reason)
        {
            switch (reason)
            {
                case ReasonLost: return "they lost too many of their people";
                case ReasonNest: return "the dead next door";
                default: return "the console";
            }
        }

        private static string SaidKey(string reason)
        {
            switch (reason)
            {
                case ReasonLost: return "scEncMoveLostSaid";
                case ReasonNest: return "scEncMoveNestSaid";
                default: return "scEncMoveSaid";
            }
        }

        // ------------------------------------------------------------------ what holds them ----

        /// <summary>What holds the group where it is now (see the class), or null when nothing does.</summary>
        public static string WhyHeld(Enclave e, World w)
        {
            if (e.Lost) return "their building is not in this world";
            EnclaveRequest r = ScEncRequests.Of(e);
            if (r != null && !r.IsOffered)
                return r.Kind == EnclaveRequest.KindSiege ? "a siege of theirs is open (request #" + r.Id + ")" : "they have an accepted request (#" + r.Id + ", " + ScEncRequests.Describe(r) + ")";
            if (ScEncCalls.Busy(e)) return "a radio call of theirs is on (" + e.CallKind + ")";
            Outpost o = ScEncRaids.TargetOf(e);
            if (o != null) return "they are leaning on " + o.Name + " (outpost " + o.Id + ")";
            int enlisted = 0, missing = 0, raiding = 0, bodies = 0;
            foreach (EncPerson p in ScRegistry.EncPeople)
            {
                if (p.EnclaveId != e.Id) continue;
                if (p.Enlisted) enlisted++;
                else if (p.Missing) missing++;
                else if (p.Raiding) raiding++;
                if (p.EntityId >= 0) bodies++;
            }
            if (enlisted + missing + raiding > 0)
                return "people of theirs are away (" + enlisted + " enlisted, " + missing + " missing, " + raiding + " raiding)";
            // E12: people who left them to join you and are still on the way (a join cut short: the rest stand) come from here
            int joining = ScEncJoin.ArrivingFrom(e);
            if (joining > 0) return joining + " who left them to join you are still on the way";
            float keep = ScConfig.EnclaveSpawnRange + ScConfig.EnclaveDespawnMargin;
            if (ScEnclaves.PlayerWithin(w, e.Poi, e.Size, keep)) return "a player is within " + Mathf.RoundToInt(keep) + " m of their building";
            if (ScEnclaves.InView(e)) return "their building is in loaded chunks or within your view distance";
            if (bodies > 0) return bodies + " of their people are out in the world";
            return null;
        }

        // ------------------------------------------------------------------ the day ----

        private static int Seed(World w, Enclave e, int day, string what)
        {
            return w.Seed ^ Salt.GetStableHashCode() ^ unchecked(e.Id * 73856093) ^ unchecked(day * 19349663) ^ what.GetStableHashCode();
        }

        /// <summary>The day's EnclaveMoveChance for this group (from the world seed, the group and the day: the same answer all day).</summary>
        private static bool Roll(World w, Enclave e, int day)
        {
            if (ScConfig.EnclaveMoveChance <= 0f) return false;
            GameRandom r = GameRandomManager.Instance.CreateGameRandom(Seed(w, e, day, "roll"));
            try { return r.RandomFloat < ScConfig.EnclaveMoveChance; }
            finally { GameRandomManager.Instance.FreeGameRandom(r); }
        }

        private static int PickSeed(World w, Enclave e, int day) { return Seed(w, e, day, "pick"); }

        /// <summary>
        /// ScDaily.Run, after the infestations (each rollover, "sc day" too): once per request-clock day, every group's nest stamp,
        /// then each group with a reason that nothing holds rolls EnclaveMoveChance and moves to the building MoveTarget picks
        /// (none fits: tomorrow). ScDaily saves.
        /// </summary>
        public static void Daily(int day, EntityPlayerLocal player)
        {
            try
            {
                if (!On || !ScRegistry.Loaded || ScEnclaves.All.Count == 0) return;
                int now = ScEncRequests.Now;
                if (now == passDay) return;
                World w = ScCommunity.World;
                if (w == null || !ScPoiIndex.EnsureBuilt()) return;
                passDay = now;
                foreach (Enclave e in ScEnclaves.All.ToArray())
                {
                    if (!ScEnclaves.All.Contains(e) || e.Lost) continue;
                    UpdateNest(e, now);
                    string reason = Reason(e, now);
                    if (reason == null) continue;
                    string held = WhyHeld(e, w);
                    if (held != null)
                    {
                        ScLog.Verbose("enclave " + e.Id + " '" + e.Name + "' would move (" + ReasonWords(reason) + ") but stays: " + held);
                        continue;
                    }
                    if (!Roll(w, e, now))
                    {
                        ScLog.Verbose("enclave " + e.Id + " '" + e.Name + "' thought of moving (" + ReasonWords(reason) + ") - not today (" + Pct(ScConfig.EnclaveMoveChance) + " a day)");
                        continue;
                    }
                    var s = new ScEnclaves.MoveSearch();
                    PoiEntry to = ScEnclaves.MoveTarget(e, PickSeed(w, e, now), s);
                    if (to == null)
                    {
                        ScLog.Out("enclave " + e.Id + " '" + e.Name + "' wants to move (" + ReasonWords(reason) + "), but no building fits: " + s.InRing + " building(s) "
                                  + ScConfig.EnclaveMoveMin + "-" + ScConfig.EnclaveMoveMax + " m away, refused: " + s.RefusedText() + " - they try again tomorrow");
                        continue;
                    }
                    Move(e, to, reason, player, "the day's roll (" + Pct(ScConfig.EnclaveMoveChance) + "), " + s.Candidates + " candidate(s) of " + s.InRing);
                }
            }
            catch (Exception ex)
            {
                ScLog.Once("encmoves-daily", ex);
            }
        }

        /// <summary>Where "900 m north-east" is measured from: home, or the player without one.</summary>
        private static bool From(EntityPlayerLocal player, out Vector3 at)
        {
            if (ScZone.HasHome)
            {
                Vector3i h = ScZone.Home;
                at = new Vector3(h.x, h.y, h.z);
                return true;
            }
            if (player != null) { at = player.position; return true; }
            at = Vector3.zero;
            return false;
        }

        /// <summary>
        /// The move (the daily pass, or the console's "now"; WhyHeld passed): see the class. A Friendly or Allied group's words on
        /// the radio (no name or number in them: scEncMoveLostSaid / NestSaid / Said through E9's scEncCallRadio), then the facts
        /// ("They have left Diner for Miller Farmhouse, 900 m north-east."), the marker at the new building, a ledger line; a
        /// known Neutral or hostile one: "The X have left Junkyard - nobody knows where they went." and a ledger line; one you never
        /// found: the log only. The log, a save.
        /// </summary>
        internal static void Move(Enclave e, PoiEntry to, string reason, EntityPlayerLocal player, string cause)
        {
            int now = ScEncRequests.Now;
            string band = ScEnclaves.StandingKey(e);
            bool told = band == "friendly" || band == "allied";
            bool wasKnown = e.Known;
            string oldName = e.PoiName, oldPrefab = e.Prefab;
            Vector3i oldKey = e.Poi;
            Vector3 oldCentre = e.Centre;
            EnclaveRequest r = ScEncRequests.Of(e);
            if (r != null && r.IsOffered) ScEncRequests.Withdraw(e, "they moved away");      // no loss: an offer they can't keep
            ScEnclavePeople.ReleaseVisit(e);
            int cleared;
            int released = ScEnclaves.MoveTo(e, to, told, player, out cleared);
            e.MovedDay = now;
            e.MovedFrom = oldKey;
            e.MovedFromName = oldName;
            e.NestDay = -1;
            int favours = ScEnclaveDeals.OnMoved(e, World.worldToBlockPos(ScEnclaves.MarkerSpot(e)));
            int size = ScEnclavePeople.AfterMove(e);
            string dir = ScEnclaves.Where(oldCentre, e.Centre);
            if (player != null)
            {
                if (told)
                {
                    Vector3 from;
                    string where = From(player, out from) ? ScEnclaves.Where(from, e.Centre) : dir;
                    ScCommunity.Tell(player, string.Format(Loc("scEncCallRadio"), e.Name, Loc(SaidKey(reason))) + " "
                                             + string.Format(Loc("scEncMoveTold"), oldName, e.PoiName, where), "");
                }
                else if (wasKnown) ScCommunity.Tell(player, string.Format(Loc("scEncMoveLeft"), e.Name, oldName), "");
            }
            if (told) ScOutposts.NoteLedger(string.Format(Loc("scEncMoveLedger"), e.Name, oldName, e.PoiName));
            else if (wasKnown) ScOutposts.NoteLedger(string.Format(Loc("scEncMoveLedgerLeft"), e.Name, oldName));
            ScLog.Out("enclave " + e.Id + " '" + e.Name + "' moved (" + ReasonWords(reason) + ") from " + oldName + " (" + oldPrefab + ") " + oldKey + " to " + e.PoiName
                      + " (" + e.Prefab + ", T" + e.Tier + ", " + e.Biome + ") " + e.Poi + ", " + dir + " - " + cause + "; "
                      + (told ? "told (" + band + "), their marker moved" : "off the map until found again (" + band + (wasKnown ? ", was known" : ", never found") + ")")
                      + "; " + released + " sleeper area(s) at the old building respawn after the loot respawn wait, " + cleared + " kept clear at the new one"
                      + (favours > 0 ? "; " + favours + " favour(s) point there now" : "") + (e.PeopleRolled ? "" : "; still being made: " + size + " in all"));
            ScRegistry.SaveAtomic();
        }

        // ------------------------------------------------------------------ the clock, the console ----

        /// <summary>ScEnclaves.OnWorldDayBack: the request clock went back reqBack days - each group's move, loss and nest stamps keep their span. Returns how many moved.</summary>
        internal static int ShiftDays(int reqBack)
        {
            if (reqBack <= 0) return 0;
            int n = 0;
            foreach (Enclave e in ScEnclaves.All)
            {
                if (e.MovedDay >= 0) { e.MovedDay = Math.Max(0, e.MovedDay - reqBack); n++; }
                if (e.LossDay >= 0) { e.LossDay = Math.Max(0, e.LossDay - reqBack); n++; }
                if (e.NestDay >= 0) { e.NestDay = Math.Max(0, e.NestDay - reqBack); n++; }
            }
            passDay = -1;       // the clock's day passes again when it comes
            return n;
        }

        /// <summary>"moves: ..." for sc enclave info: both reasons, the chance, what holds them now, their last move.</summary>
        public static string InfoLine(Enclave e)
        {
            int now = ScEncRequests.Now;
            string s;
            if (!Configured) s = "off (" + OffWhy() + ")";
            else if (e.Lost) s = "none (their building is not in this world)";
            else
            {
                string lost, nest;
                bool l = LostReason(e, now, out lost), n = NestReason(e, now, out nest);
                World w = ScCommunity.World;
                string held = w != null ? WhyHeld(e, w) : "no world";
                s = lost + (l ? " - a reason" : "") + "; " + nest + (n ? " - a reason" : "") + "; "
                    + (l || n ? Pct(ScConfig.EnclaveMoveChance) + " a day to move, " + (held != null ? "held now: " + held : "nothing holds them")
                              : "no reason to move" + (held != null ? " (held now: " + held + ")" : ""));
            }
            string moved = e.MovedDay >= 0 ? "; moved on day " + e.MovedDay + " from " + (e.MovedFromName.Length > 0 ? e.MovedFromName : "?") + " " + e.MovedFrom : "";
            return "moves: " + s + moved + " (request clock day " + now + ")";
        }

        private static string Describe(Enclave e, PoiEntry to)
        {
            return to.Display + " (" + to.Prefab + ") T" + to.Tier + " " + to.Biome + (to.Row.Length > 0 ? ", row " + to.Row : "") + " " + to.Key + " - "
                   + ScEnclaves.Where(e.Centre, new Vector3(to.X, 0f, to.Z)) + " from their building";
        }

        /// <summary>
        /// sc enclave move &lt;id&gt; [sim|now]: their moves line; sim - today's reason, what holds them, the day's roll and the
        /// building they would go to (nothing changes); now - they move at once (the reasons and the roll ignored, what holds them
        /// kept), to the building sim names.
        /// </summary>
        public static void Command(Enclave e, List<string> p, List<string> into)
        {
            string verb = p.Count > 3 ? p[3].ToLowerInvariant() : "";
            into.Add(e.Name + " (enclave " + e.Id + ", " + ScEnclaves.StandingWord(e) + "): " + InfoLine(e));
            if (verb == "") return;
            if (verb != "sim" && verb != "now") { into.Add("sc enclave move <id> [sim|now]"); return; }
            World w = ScCommunity.World;
            if (w == null || !ScCommunity.IsHost) { into.Add("host only, with a world"); return; }
            if (!ScPoiIndex.EnsureBuilt()) { into.Add("no POI list on this world"); return; }
            int now = ScEncRequests.Now;
            if (verb == "sim")
            {
                string reason = Reason(e, now);
                string held = !Configured ? "enclave moves are off (" + OffWhy() + ")" : WhyHeld(e, w);
                into.Add("  today (request clock day " + now + "): " + (reason != null ? "a reason holds (" + ReasonWords(reason) + ")" : "no reason holds") + "; "
                         + (held != null ? "held: " + held : "nothing holds them") + "; the day's roll " + (Roll(w, e, now) ? "says go" : "says stay")
                         + " (" + Pct(ScConfig.EnclaveMoveChance) + ")");
                if (e.Lost) return;
                var s = new ScEnclaves.MoveSearch();
                PoiEntry to = ScEnclaves.MoveTarget(e, PickSeed(w, e, now), s);
                into.Add("  " + s.InRing + " building(s) " + ScConfig.EnclaveMoveMin + "-" + ScConfig.EnclaveMoveMax + " m from theirs, " + s.Candidates + " candidate(s); refused: "
                         + s.RefusedText());
                into.Add(to != null ? "  the pick: " + Describe(e, to) : "  no building fits - they would stay and try again the next day");
                return;
            }
            if (!Configured) { into.Add("refused: enclave moves are off (" + OffWhy() + ")"); return; }
            string why = WhyHeld(e, w);
            if (why != null) { into.Add("refused: " + why); return; }
            var search = new ScEnclaves.MoveSearch();
            PoiEntry dest = ScEnclaves.MoveTarget(e, PickSeed(w, e, now), search);
            if (dest == null)
            {
                into.Add("refused: no building fits (" + search.InRing + " building(s) " + ScConfig.EnclaveMoveMin + "-" + ScConfig.EnclaveMoveMax + " m away, refused: "
                         + search.RefusedText() + ")");
                return;
            }
            string old = e.PoiName;
            Move(e, dest, Reason(e, now) ?? ReasonConsole, ScCommunity.LocalPlayer, "the console");
            into.Add(e.Name + " moved from " + old + " to " + e.PoiName + " - " + (e.Known ? "known: their marker is there" : "off the map until found again"));
        }
    }
}
