using System;
using System.Collections.Generic;

namespace SurvivorCommunity
{
    /// <summary>
    /// Gifts, milestone E7 (research\ideas\DESIGN.md section 6; the user's Q5: yes, +3 for any bundle, once a day, capped at
    /// 69). An enclave leader's greeting gains "I brought you something." (Config/dialogs.xml, DialogOpsEnclaves.cs): it lists
    /// up to Rows bundles the player carries, the smallest of each resource - the ones the group does not live on first, then
    /// their own, then luxuries - and the one picked is handed over for +EnclaveGiftStanding standing whatever its size, never
    /// past EnclaveGiftCap (so gifts can win a group to Friendly and mend a falling-out, but an alliance still takes their
    /// requests). One gift per enclave every EnclaveGiftDays days of the request clock (ScEncRequests.Now, so "sc day" moves
    /// it; Enclave.GiftDay, saved as [enclave] gift=N and shifted back by ScEnclaves.OnWorldDayBack). The row greys with the
    /// reason (at the cap, too soon, nothing carried). The leader's thanks come by band (scEncThanks_gift_&lt;band&gt;: ScEncRequests.
    /// LineKey's family, so the voicing round finds them), with "(standing +3)" after them; a ledger line names the bundle.
    /// Reuses E4's bundle code (ScEncRequests.Carried / TakeBundles: the exact size taken, a short take given back), E4's
    /// row-text postfix for the rows' words (Patches_Enclaves.Patch_DialogRowText -> ScEncRequests.FillRow -> FillRow here)
    /// and ScEnclaves.AddStanding (the clamp, the band's marker, E6's OnBandChanged). Host only, main thread.
    /// Round MP5 (part E): any community player gives. A client's row reads the record's half from the talk's facts
    /// (BlockedServer) and its own bag; its pick is built from its own bag (BuildPickClient), the bundle leaves its bag first
    /// (TakeRow) and enc.gift (ScActionsEnclave) applies it here (GiveFor) or refuses, and the bundle goes back.
    /// </summary>
    public static class ScEncGifts
    {
        /// <summary>The gift pick's rows (scEncGift0..3).</summary>
        public const int Rows = 4;
        public const string RowPrefix = "scEncGift";
        /// <summary>The thanks' kind in ScEncRequests.LineKey: scEncThanks_gift_neutral / _friendly / _allied.</summary>
        public const string LineKind = "gift";

        /// <summary>One row of the gift pick: a bundle of one resource and size, and the row's words, made once.</summary>
        public sealed class GiftRow
        {
            public Resource Res;
            public BundleSize Size;
            public int Units;
            public string Text = "";
        }

        /// <summary>The gift pick on screen: whose talk, which enclave, its rows.</summary>
        private sealed class Pick
        {
            public int PlayerId, NpcId, EnclaveId;
            public List<GiftRow> Rows = new List<GiftRow>();
        }

        /// <summary>What the last gift action left for the reply statement ({giftreply}): its line's key (a voice clip's name) and the whole text.</summary>
        private sealed class Reply
        {
            public int EnclaveId;
            public string Key = "";
            public string Text = "";
        }

        private static Pick pick;
        private static Reply reply;

        private static string Loc(string key) { return Localization.Get(key); }

        /// <summary>The session's pick and reply cleared (ScEncRequests.Reset: a new world, a registry load).</summary>
        public static void Reset()
        {
            pick = null;
            reply = null;
        }

        // ------------------------------------------------------------------ the rules ----

        /// <summary>The row shows: gifts on, and an enclave that is not hostile and is on the map.</summary>
        public static bool Shown(Enclave e)
        {
            return ScConfig.Enclaves && ScConfig.EnclaveGifts && e != null && !e.IsHostile && !e.Lost;
        }

        /// <summary>The standing a gift adds now: EnclaveGiftStanding, never past EnclaveGiftCap (0 at or above it).</summary>
        public static int GainNow(Enclave e)
        {
            if (e == null) return 0;
            return Math.Max(0, Math.Min(Math.Max(0, ScConfig.EnclaveGiftStanding), ScConfig.EnclaveGiftCap - e.Standing));
        }

        /// <summary>At the cap: their standing is EnclaveGiftCap or more.</summary>
        public static bool AtCap(Enclave e) { return e != null && e.Standing >= ScConfig.EnclaveGiftCap; }

        /// <summary>Request-clock days until the next gift (0: one is taken today).</summary>
        public static int DaysLeft(Enclave e) { return e == null ? 0 : Math.Max(0, e.GiftDay - ScEncRequests.Now); }

        /// <summary>
        /// Why no gift can be given now (the greyed row's reason), or null: at the cap first (a wait won't help), then too soon,
        /// then nothing to give.
        /// </summary>
        public static string Blocked(Enclave e, EntityPlayer player)
        {
            string why = BlockedServer(e);    // round MP5 (part E): the record's half, alone for a remote player's facts
            if (why != null) return why;
            if (RowsFor(player as EntityPlayerLocal, e).Count == 0) return Loc("scEncGiftWhyNone");
            return null;
        }

        /// <summary>
        /// Round MP5 (part E): Blocked's half that reads the record (off, the cap, too soon) - what the server tells a client's talk
        /// (ScEncFacts); the bundles carried are the client's own bag's half (ScEncTalk.GiftWhy).
        /// </summary>
        public static string BlockedServer(Enclave e)
        {
            if (!Shown(e)) return Loc("scEncGiftWhyOff");
            if (AtCap(e)) return Loc("scEncGiftWhyCap");
            int left = DaysLeft(e);
            if (left > 0) return left == 1 ? Loc("scEncGiftWhyTomorrow") : string.Format(Loc("scEncGiftWhyDays"), left);
            return null;
        }

        /// <summary>The greyed row's words: "I brought you something. (Come back tomorrow.)" - "" when it is open.</summary>
        public static string WhyText(Enclave e, EntityPlayer player)
        {
            string why = Blocked(e, player);
            return why == null ? "" : string.Format(Loc("scEncGiftWhy"), Loc("scEncGift"), why);
        }

        // ------------------------------------------------------------------ the rows ----

        /// <summary>"medicine", "water", "luxuries".</summary>
        public static string ResWord(Resource r) { return Loc("scEncReqRes" + r); }

        /// <summary>"a medium bundle of medicine" (the ledger's and the console's words).</summary>
        public static string What(Resource r, int units) { return string.Format(Loc("scEncReqWhatResource"), ScEncRequests.SizeWord(units), ResWord(r)); }

        /// <summary>The resources in the rows' order: those the group does not live on (the classic five, then water), their own, luxuries.</summary>
        private static List<Resource> Order(Enclave e)
        {
            return Order(e != null ? e.Kind : null);
        }

        /// <summary>Round MP5 (part E): Order by the group's kind (a client knows it from the talk's facts).</summary>
        private static List<Resource> Order(string kind)
        {
            int own = kind != null ? ScEncRequests.OwnResource(kind) : -1;
            var list = new List<Resource>(ScNames.ResourceCount);
            for (int i = 0; i < ScNames.ResourceCount; i++)
                if (i != own && (Resource)i != Resource.Luxury) list.Add((Resource)i);
            if (own >= 0) list.Add((Resource)own);
            list.Add(Resource.Luxury);
            return list;
        }

        /// <summary>
        /// The rows for this player and enclave: per resource carried, its smallest bundle (an old one-size bundle first - nothing
        /// makes them any more - then small, medium, large), in Order, at most Rows.
        /// </summary>
        public static List<GiftRow> RowsFor(EntityPlayerLocal pl, Enclave e)
        {
            return RowsFor(pl, e != null ? e.Kind : null);
        }

        /// <summary>Round MP5 (part E): RowsFor by the group's kind (a client's own bag, the kind from the talk's facts).</summary>
        public static List<GiftRow> RowsFor(EntityPlayerLocal pl, string kind)
        {
            var rows = new List<GiftRow>();
            if (pl == null) return rows;
            foreach (Resource res in Order(kind))
            {
                ScEncRequests.Bundles b = ScEncRequests.Carried(pl, res);
                if (b.Count == 0) continue;
                BundleSize size = b.Legacy > 0 ? BundleSize.Legacy : b.Small > 0 ? BundleSize.Small : b.Medium > 0 ? BundleSize.Medium : BundleSize.Large;
                int units = size == BundleSize.Legacy ? 1 : (int)size;
                rows.Add(new GiftRow { Res = res, Size = size, Units = units, Text = string.Format(Loc("scEncGiftRowText"), ScEncRequests.SizeWord(units), ResWord(res)) });
                if (rows.Count >= Rows) break;
            }
            return rows;
        }

        /// <summary>"I brought you something.": the pick for this talk (the player, the leader's body, their enclave).</summary>
        public static void BuildPick(EntityPlayer player, EntityAlive npc, Enclave e)
        {
            EntityPlayerLocal pl = player as EntityPlayerLocal;
            if (pl == null || npc == null || e == null) { pick = null; return; }
            pick = new Pick { PlayerId = pl.entityId, NpcId = npc.entityId, EnclaveId = e.Id, Rows = RowsFor(pl, e) };
        }

        /// <summary>Row i of the pick on screen (made again if it belongs to another talk), or null.</summary>
        public static GiftRow PickRow(int i, EntityPlayer player, EntityAlive npc, Enclave e)
        {
            if (player == null || npc == null || e == null || i < 0) return null;
            if (pick == null || pick.PlayerId != player.entityId || pick.NpcId != npc.entityId || pick.EnclaveId != e.Id) BuildPick(player, npc, e);
            return pick != null && i < pick.Rows.Count ? pick.Rows[i] : null;
        }

        // ---- round MP5 (part E): a client's half - its own bag, the enclave from the talk's facts ----

        /// <summary>Round MP5 (part E), client: "I brought you something." - the pick of this machine's own bundles for the talk with npcId.</summary>
        internal static void BuildPickClient(EntityPlayerLocal pl, int npcId, int enclaveId, string kind)
        {
            pick = pl != null && npcId > 0 ? new Pick { PlayerId = pl.entityId, NpcId = npcId, EnclaveId = enclaveId, Rows = RowsFor(pl, kind) } : null;
        }

        /// <summary>Round MP5 (part E), client: row i of the pick on screen (made again for another talk), or null.</summary>
        internal static GiftRow PickRowClient(int i, EntityPlayerLocal pl, int npcId, int enclaveId, string kind)
        {
            if (pl == null || npcId <= 0 || i < 0) return null;
            if (pick == null || pick.PlayerId != pl.entityId || pick.NpcId != npcId || pick.EnclaveId != enclaveId) BuildPickClient(pl, npcId, enclaveId, kind);
            return pick != null && i < pick.Rows.Count ? pick.Rows[i] : null;
        }

        /// <summary>
        /// Round MP5 (part E), client: the row's bundle (its resource and size, one) out of this machine's own bag by type
        /// (ScItemTx.TakeFromLocal), exactly what left in taken - sent with enc.gift, given back on a refusal. False: not carried.
        /// </summary>
        internal static bool TakeRow(EntityPlayerLocal pl, GiftRow g, List<ItemStack> taken)
        {
            if (pl == null || g == null || taken == null) return false;
            ItemValue v = ScStockpile.BundleValue(g.Res, g.Size);
            if (v == null) return false;
            int type = v.type;
            if (ScItemTx.TakeFromLocal(pl, iv => iv != null && iv.type == type, 1, taken) >= 1) return true;
            if (taken.Count > 0) ScItemTx.GiveToLocal(pl, taken);
            taken.Clear();
            return false;
        }

        /// <summary>Round MP5 (part E), client: the pick's rows are spent (a gift went, as the host's ActGive clears it).</summary>
        internal static void ClearPick() { pick = null; }

        /// <summary>
        /// Round MP5 (part E): a gift from a remote player (enc.gift) - their machine took the bundle from its own bag and sent it:
        /// the host's ActGive checks again (gifts on, under the cap, not too soon), one bundle that is a bundle, then Apply (its
        /// band-change tell to the giver). key and info: the reply line ({giftreply}). False: nothing kept - the caller refuses, and
        /// the player's machine gives the bundle back.
        /// </summary>
        internal static bool GiveFor(EntityPlayer player, EntityAlive npc, ItemStack bundle, out string key, out string info)
        {
            key = "scEncReqGone";
            info = "";
            Enclave e;
            EnclaveRequest r;
            if (!ScEncRequests.LeaderAt(npc, out e, out r)) return false;
            if (!Shown(e)) return false;
            if (AtCap(e)) { key = "scEncGiftRefuseCap"; return false; }
            if (DaysLeft(e) > 0) { key = "scEncGiftRefuseSoon"; return false; }
            Resource res;
            int units;
            if (bundle == null || bundle.count != 1 || !ScStockpile.IsBundle(bundle, out res, out units)) { key = "scEncReqNotCarried"; return false; }
            ScLog.Out("enclave " + e.Id + " '" + e.Name + "': a gift from the " + ScNames.Key(res) + " bundles, " + units + " unit(s), from " + player.EntityName + "'s own inventory");
            int moved = Apply(e, What(res, units), "handed over in the talk", player);
            key = ScEncRequests.LineKey("scEncThanks", LineKind, ScEnclaves.StandingKey(e));
            info = ScEncRequests.Aside(moved);
            return true;
        }

        /// <summary>
        /// A gift row's words (ScEncRequests.FillRow, from the row-text postfix: the response's Text is the shared "{gift}"):
        /// true for scEncGift0..9, with value the row's text made with the pick ("" past its rows).
        /// </summary>
        public static bool FillRow(string id, ref string value)
        {
            if (id == null || id.Length != RowPrefix.Length + 1 || !id.StartsWith(RowPrefix, StringComparison.Ordinal)) return false;
            int i = id[id.Length - 1] - '0';
            if (i < 0 || i > 9) return false;
            Pick p = pick;
            value = p != null && i < p.Rows.Count ? p.Rows[i].Text : "";
            return true;
        }

        // ------------------------------------------------------------------ the dialog's actions ----

        /// <summary>The gift actions: the leader at home of an enclave that is not hostile, checked again. Host only.</summary>
        private static bool Acting(EntityPlayer player, out Enclave e)
        {
            e = null;
            EnclaveRequest r;
            if (!ScCommunity.IsHost || !(player is EntityPlayerLocal)) return false;
            if (ScEncRequests.LeaderTalking(player, null, out e, out r)) return true;
            reply = null;
            return false;
        }

        /// <summary>"I brought you something.": the pick of the bundles the player carries (its rows' words made now).</summary>
        public static void ActList(EntityPlayer player)
        {
            Enclave e;
            if (!Acting(player, out e)) { pick = null; return; }
            BuildPick(player, ScFavours.CurrentNpc(player), e);
        }

        /// <summary>
        /// A gift row: checked again (the rows were built a moment ago) - gifts on, under the cap, not too soon - then that
        /// bundle taken from the bag or the toolbelt (its exact size; a short take is given back) and the gift applied (Apply).
        /// The leader's reply: their thanks by band and "(standing +3)"; a refusal by the rule that stopped it; "You don't seem
        /// to have it on you." when the bundle left the bag meanwhile.
        /// </summary>
        public static void ActGive(EntityPlayer player, int row)
        {
            Enclave e;
            if (!Acting(player, out e)) return;
            EntityPlayerLocal pl = (EntityPlayerLocal)player;
            EntityAlive npc = ScFavours.CurrentNpc(player);
            Pick p = pick;
            GiftRow g = p != null && npc != null && p.PlayerId == pl.entityId && p.NpcId == npc.entityId && p.EnclaveId == e.Id && row >= 0 && row < p.Rows.Count ? p.Rows[row] : null;
            if (!Shown(e)) { SetReply(e, "scEncReqGone", ""); return; }
            if (AtCap(e)) { SetReply(e, "scEncGiftRefuseCap", ""); return; }
            if (DaysLeft(e) > 0) { SetReply(e, "scEncGiftRefuseSoon", ""); return; }
            if (g == null || !Take(pl, g)) { SetReply(e, "scEncReqNotCarried", ""); return; }
            pick = null;
            ScLog.Out("enclave " + e.Id + " '" + e.Name + "': a gift from the " + ScNames.Key(g.Res) + " bundles, " + g.Size + " (" + g.Units + " unit(s))");
            int moved = Apply(e, What(g.Res, g.Units), "handed over in the talk");
            SetReply(e, ScEncRequests.LineKey("scEncThanks", LineKind, ScEnclaves.StandingKey(e)), ScEncRequests.Aside(moved));
        }

        /// <summary>One bundle of the row's resource and size out of the bag or the toolbelt (still carried? then TakeBundles).</summary>
        private static bool Take(EntityPlayerLocal pl, GiftRow g)
        {
            ScEncRequests.Bundles have = ScEncRequests.Carried(pl, g.Res);
            var take = new ScEncRequests.Bundles();
            switch (g.Size)
            {
                case BundleSize.Large: if (have.Large < 1) return false; take.Large = 1; break;
                case BundleSize.Medium: if (have.Medium < 1) return false; take.Medium = 1; break;
                case BundleSize.Small: if (have.Small < 1) return false; take.Small = 1; break;
                default: if (have.Legacy < 1) return false; take.Legacy = 1; break;
            }
            return ScEncRequests.TakeBundles(pl, g.Res, take);
        }

        /// <summary>
        /// A gift given (the dialog's, or the console's "as if handed over"): the next gift EnclaveGiftDays later on the request
        /// clock, +GainNow standing (ScEnclaves.AddStanding: the band's marker, E6's benefit on a band change; its own ledger
        /// line off), a ledger line with the bundle and the change, a tell when the band changed, the log, a save. Returns the
        /// standing it moved.
        /// </summary>
        private static int Apply(Enclave e, string what, string why, EntityPlayer giver = null)     // round MP5 (part E): giver - the band tell's player (null: the host's own, as before)
        {
            int gain = GainNow(e);
            int old = e.Standing;
            string before = ScEnclaves.StandingKey(e);
            e.GiftDay = ScEncRequests.Now + Math.Max(0, ScConfig.EnclaveGiftDays);
            if (gain > 0) ScEnclaves.AddStanding(e, gain, "a gift: " + what, false);
            int moved = e.Standing - old;
            bool bandChanged = ScEnclaves.StandingKey(e) != before;
            string delta = moved == 0 ? Loc("scEncReqLedgerNoChange")
                         : bandChanged ? string.Format(Loc("scEncReqLedgerDeltaBand"), "+" + moved, ScEnclaves.StandingWord(e))
                         : string.Format(Loc("scEncReqLedgerDelta"), "+" + moved);
            ScOutposts.NoteLedger(string.Format(Loc("scEncGiftLedger"), e.Name, what, delta));
            EntityPlayer pl = giver ?? ScCommunity.LocalPlayer;      // round MP5 (part E)
            // round MP4 (part: tells): this file's tells go by localization key (ScCommunity.TellKey / ScPlayers.TellCommunityKey), so each player reads them in their own language
            if (bandChanged && pl != null && !e.IsHostile) ScCommunity.TellKey(pl, "scEncReqBandTell", "", e.Name, ScCommunity.LocArg(ScEnclaves.StandingWordKey(e)));   // review tells m4
            ScLog.Out("enclave " + e.Id + " '" + e.Name + "': a gift (" + what + ", " + why + ") - standing " + old + " -> " + e.Standing + " ("
                      + ScEnclaves.StandingKey(e) + (gain < Math.Max(0, ScConfig.EnclaveGiftStanding) ? ", capped at " + ScConfig.EnclaveGiftCap : "")
                      + "); the next gift from day " + e.GiftDay + " (request clock day " + ScEncRequests.Now + ")");
            ScRegistry.SaveAtomic();
            return moved;
        }

        // ------------------------------------------------------------------ the reply ({giftreply}) ----

        /// <summary>The reply statement's line: the words of key (a voice clip's name), then info, the specifics.</summary>
        private static void SetReply(Enclave e, string key, string info)
        {
            reply = e != null ? new Reply { EnclaveId = e.Id, Key = key, Text = (Loc(key) + " " + (info ?? "")).Trim() } : null;
        }

        /// <summary>
        /// The line panel's {giftreply} (ScEncDialog.Fill): what the last gift action left for this enclave, else "Never mind
        /// that now. It's settled." voiceKey: that line's key (ScVoice plays its clip once one is recorded).
        /// </summary>
        public static string FillReply(string s, Enclave e, out string voiceKey)
        {
            bool mine = reply != null && e != null && reply.EnclaveId == e.Id;
            voiceKey = mine ? reply.Key : "scEncReqGone";
            return s.Replace("{giftreply}", mine ? reply.Text : Loc("scEncReqGone"));
        }

        // ------------------------------------------------------------------ the clock, console ----

        /// <summary>
        /// ScEnclaves.OnWorldDayBack: the request clock went back by back days - every enclave's next gift and next ally barter
        /// keep their remaining span (never below 0). Returns how many stamps moved.
        /// </summary>
        internal static int ShiftDays(int back)
        {
            if (back <= 0) return 0;
            int n = 0;
            foreach (Enclave e in ScEnclaves.All)
                for (int i = 0; i < e.Rels.Count; i++)      // round MP6 (part relations): every community's gift and barter days
                {
                    EncRel r = e.Rels.At(i);
                    if (r.GiftDay > 0) { r.GiftDay = Math.Max(0, r.GiftDay - back); n++; }
                    if (r.BarterDay > 0) { r.BarterDay = Math.Max(0, r.BarterDay - back); n++; }
                }
            return n;
        }

        /// <summary>"gifts: ...; ally barter: ..." for sc enclave info.</summary>
        public static string InfoLine(Enclave e)
        {
            string g;
            if (!ScConfig.EnclaveGifts) g = "off (EnclaveGifts=false)";
            else if (e.IsHostile) g = "never (hostile)";
            else
            {
                int left = DaysLeft(e);
                g = "+" + GainNow(e) + " now (EnclaveGiftStanding " + ScConfig.EnclaveGiftStanding + ", cap " + ScConfig.EnclaveGiftCap + ")"
                    + (AtCap(e) ? ", at the cap: they take none" : left > 0 ? ", the next from day " + e.GiftDay + " (" + left + " day(s))" : ", they take one today");
            }
            return "gifts: " + g + "; " + ScEncBarter.InfoText(e) + " (request clock day " + ScEncRequests.Now + ")";
        }

        /// <summary>
        /// sc enclave gift &lt;id&gt; [give &lt;resource&gt; [small|medium|large] [force]]: the gift state and what the player carries;
        /// "give" applies a gift as if handed over (no bundle needed) under the same rules - "force" ignores the day's wait
        /// (never the cap).
        /// </summary>
        public static void Command(Enclave e, List<string> p, List<string> into, EntityPlayer player = null)
        {
            string verb = p.Count > 3 ? p[3].ToLowerInvariant() : "";
            if (verb == "")
            {
                into.Add(e.Name + " (enclave " + e.Id + ", " + ScEnclaves.StandingWord(e) + ", standing " + e.Standing + "): " + InfoLine(e));
                // round MP5 (part E): the player who typed it (ConsoleCmdSc.Me); a remote player's bag is their own machine's, so their
                // row is the record's half only (their talk adds the bundles they carry)
                EntityPlayer who = player ?? ScCommunity.LocalPlayer;
                EntityPlayerLocal pl = who as EntityPlayerLocal;
                if (who != null && pl == null)
                {
                    string half = BlockedServer(e);
                    into.Add("  the leader's row now: " + (half == null ? "open while you carry a bundle (your bag is on your own machine)" : "greyed - " + half));
                    return;
                }
                string why = Blocked(e, pl);
                into.Add("  the leader's row now: " + (why == null ? "open" : "greyed - " + why));
                List<GiftRow> rows = RowsFor(pl, e);
                if (rows.Count == 0) into.Add("  you carry no bundles");
                foreach (GiftRow r in rows) into.Add("  row: " + r.Text + " (" + ScNames.Key(r.Res) + ", " + r.Size + ")");
                return;
            }
            if (verb != "give")
            {
                into.Add("sc enclave gift <id> [give <food|meds|ammo|mats|fuel|water|luxury> [small|medium|large] [force]]");
                return;
            }
            Resource res = Resource.Food;
            bool hasRes = false, force = false;
            int units = 1;
            for (int i = 4; i < p.Count; i++)
            {
                string a = p[i].ToLowerInvariant();
                int u = Array.IndexOf(ScEncRequests.Sizes, a) + 1;
                Resource rr;
                if (a == "force") force = true;
                else if (u > 0) units = u;
                else if (ScNames.TryResource(a, out rr)) { res = rr; hasRes = true; }
                else { into.Add("unknown argument '" + p[i] + "' (a resource: food|meds|ammo|mats|fuel|water|luxury; a size: small|medium|large; force)"); return; }
            }
            if (!hasRes) { into.Add("sc enclave gift " + e.Id + " give <food|meds|ammo|mats|fuel|water|luxury> [small|medium|large] [force]"); return; }
            if (!Shown(e)) { into.Add(e.Name + " takes no gift: " + Loc("scEncGiftWhyOff")); return; }
            if (AtCap(e)) { into.Add(e.Name + " takes no gift: standing " + e.Standing + " is at the cap (EnclaveGiftCap " + ScConfig.EnclaveGiftCap + ")"); return; }
            if (DaysLeft(e) > 0 && !force) { into.Add(e.Name + " takes no gift until day " + e.GiftDay + " (request clock day " + ScEncRequests.Now + "): add force to give one anyway"); return; }
            int moved = Apply(e, What(res, units), "the console", player);     // round MP5 (part E): the band tell to who typed it
            into.Add("gift to " + e.Name + ": " + What(res, units) + " - standing +" + moved + " -> " + e.Standing + " (" + ScEnclaves.StandingWord(e) + "); the next from day " + e.GiftDay);
        }
    }
}
