using System;
using System.Collections.Generic;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// One enclave's request (E4; Enclaves_Plan.md 2.4 and 2.10): a registry record, at most one per enclave, in its own
    /// [encrequest] section (forward-only). Offered: the leader asks, and the offer stands to the end of LapseDay. Accepted:
    /// the player took it on - a journal quest mirrors it (QuestCode) - and it is due by the end of DueDay. A closed request
    /// leaves the records (ScEncRequests.Close), its ledger line stays. Days are the request clock's (ScEncRequests.Now:
    /// the world's day, ahead of it only after "sc day"). E5 adds the missing survivor, the infestation and the siege; their
    /// fields are reserved at the end (saved when set, unused by E4).
    /// </summary>
    public sealed class EnclaveRequest
    {
        public const string KindResource = "resource", KindGenerator = "generator", KindGun = "gun";
        public const string StateOffered = "offered", StateAccepted = "accepted";

        public int Id;
        public int EnclaveId;
        public string Kind = KindResource;
        public string State = StateOffered;
        public int OfferDay;
        /// <summary>The last day the offer stands: an offered request lapses at the next day's pass.</summary>
        public int LapseDay;
        public int AcceptDay = -1;
        /// <summary>The last day to deliver: an accepted request expires at the next day's pass.</summary>
        public int DueDay = -1;
        /// <summary>A resource request: the resource and the units wanted (1 small, 2 medium, 3 large).</summary>
        public Resource Res = Resource.Food;
        public int Units;
        /// <summary>The journal quest that mirrors an accepted request (Quest.QuestCode; 0: none).</summary>
        public int QuestCode;

        // ---- E5's room, unused by E4: a target POI, an infestation, a person, a world-time deadline ----
        public bool HasTarget;
        public Vector3i Target;
        public int InfestationId = -1;
        public int PersonId = -1;
        public ulong Deadline;

        public bool IsOffered { get { return State == StateOffered; } }
        public bool IsAccepted { get { return State == StateAccepted; } }
    }

    /// <summary>
    /// Requests, part 1 (milestone E4; E4_SPEC.md; Enclaves_Plan.md 2.4, 2.10 and 6: Q5 a, Q10 a, Q11, Q16 a, Q17; research
    /// missions.md 1.1, 2.1-2.4, 3.1, 3.3, 3.5, 4-6): what an enclave's leader asks of the player, the standing it earns or
    /// costs, and the pace they ask at.
    ///
    /// Pacing (ScDaily.Run, Daily): an offer nobody answers lapses after EnclaveOfferLapseDays (EnclaveLossLapse by band),
    /// an accepted request expires after EnclaveDueDaysItems (EnclaveLossFail); then each discovered enclave that is not
    /// hostile, has no open request and is past its cooldown (EnclaveRequestCooldownDays) rolls EnclaveRequestChance* for
    /// its band - at most EnclaveRequestMaxOpen open across the map, generators from day EnclaveGeneratorFromDay, the kind
    /// and a resource's size weighted (EnclaveRequestKindWeights, EnclaveRequestSizeWeights), the resource one their kind
    /// does not live on. A powered radio announces a new offer (a tell and a ledger line); otherwise the leader asks in
    /// person. Idempotent per day and catch-up safe: a missed day's lapses and expiries run in order, new offers come only
    /// on the newest day of a catch-up (never a pile of them, never one that lapses in the same frame).
    ///
    /// The dialog (Config/dialogs.xml, DialogOpsEncRequests.cs): the leader's "What do you need?" leads to the statement of
    /// the request's state - nothing, the offer (accept, hand it over, a gun from the pick, no, let me think) or the wait
    /// (hand it over, a gun, I can't do it). Their words are chosen by kind and band at runtime (LineKey: stem_kind_band,
    /// stem_kind, stem_band, stem) and filled into SC's line panel ({reqask}, {reqwait}, {reqreply}: ScEncDialog.Fill); the
    /// gun pick's rows through the response entry's binding (Patches_Enclaves.Patch_DialogRowText). No shared Text field
    /// is written. Every action checks everything again: the rows were built a moment earlier.
    ///
    /// The kinds: bundles of a resource from the bag and the toolbelt covering the units with the least overpay
    /// (EnclaveGainResource; ScStockpile.IsBundle's units, removed by type and measured before and after); one item whose
    /// block is a BlockGenerator (EnclaveGainGenerator); a gun or launcher - never a bow, crossbow or admin gun - taken from
    /// its exact slot, worth EnclaveGunTierPoints by tier x quality (Q11). Close is the one way out: the standing change
    /// for the band at that moment (EnclaveLoss*), the journal quest closed (completed, or removed as failed),
    /// SurvivorOrigins' favour event for one done (Q16 a), a ledger line, a tell when the band changed, the log, a save.
    /// E5 reuses it for its kinds. Host only, main thread.
    /// </summary>
    public static class ScEncRequests
    {
        public const string Done = "done", Declined = "declined", Failed = "failed", Expired = "expired", Lapsed = "lapsed", Withdrawn = "withdrawn";
        /// <summary>The kinds E4 makes (E5 adds missing, infestation, siege). A saved request of another kind is dropped at load.</summary>
        public static readonly string[] Kinds = { EnclaveRequest.KindResource, EnclaveRequest.KindGenerator, EnclaveRequest.KindGun };
        /// <summary>The sizes of a resource request, by units (EnclaveRequestSizeWeights).</summary>
        public static readonly string[] Sizes = { "small", "medium", "large" };
        /// <summary>The gun pick's rows (scEncReqGun0..3): the 2 least and the 2 most valuable guns carried, or all of 4 or fewer.</summary>
        public const int GunRows = 4;
        public const string GunRowPrefix = "scEncReqGun";
        private const string Salt = "sc.encrequests.v1";
        /// <summary>The five resources a request asks for (the classic ones: water and luxuries are the community's).</summary>
        private const int Asked = ScNames.ClassicCount;

        private static readonly FastTags<TagGroup.Global> GunTags = FastTags<TagGroup.Global>.Parse("gun,launcher");
        private static readonly FastTags<TagGroup.Global> NotGunTags = FastTags<TagGroup.Global>.Parse("bow,crossbow,admin");
        private static readonly FastTags<TagGroup.Global>[] TierTags =
        {
            FastTags<TagGroup.Global>.Parse("T0"), FastTags<TagGroup.Global>.Parse("T1"), FastTags<TagGroup.Global>.Parse("T2"),
            FastTags<TagGroup.Global>.Parse("T3"), FastTags<TagGroup.Global>.Parse("T4"), FastTags<TagGroup.Global>.Parse("T5")
        };

        /// <summary>One gun the player carries, as the pick lists it (Value: the item when it was listed).</summary>
        public sealed class Gun
        {
            public bool Belt;
            public int Slot;
            public ItemValue Value;
            public string Name = "";
            public int Tier, Quality, Points;
            public float Economic;
            /// <summary>The row's text, made once: the binding asks for it every frame.</summary>
            public string Row = "";
        }

        /// <summary>The gun pick on screen: whose talk, which request, and its rows.</summary>
        private sealed class GunPick
        {
            public int PlayerId, NpcId, RequestId;
            public List<Gun> Rows = new List<Gun>();
        }

        /// <summary>What the last request action left for the reply statement ({reqreply}).</summary>
        private sealed class Reply
        {
            public int EnclaveId;
            public string Text = "";
        }

        private struct Held
        {
            public bool Belt;
            public int Slot;
            public ItemStack Stack;
        }

        /// <summary>Bundles of one resource by size (units: Large 3, Medium 2, Small 1, the old one-size bundle 1).</summary>
        private struct Bundles
        {
            public int Large, Medium, Small, Legacy;
            public int Units { get { return Large * 3 + Medium * 2 + Small + Legacy; } }
            public int Count { get { return Large + Medium + Small + Legacy; } }
        }

        private static GunPick pick;
        private static Reply reply;
        /// <summary>The once-a-session journal pass (SyncJournal) is done; how often it found the player's record not in yet.</summary>
        private static bool synced;
        private static int syncTries;
        private const int MaxSyncTries = 30;
        private static bool clockSaid;

        public static List<EnclaveRequest> All { get { return ScRegistry.EncRequests; } }

        private static string Loc(string key) { return Localization.Get(key); }

        public static void Reset()
        {
            pick = null;
            reply = null;
            synced = false;
            syncTries = 0;
            clockSaid = false;
        }

        /// <summary>
        /// The registry was (re)loaded or reset (ScEnclaves.OnRegistryChanged): ids squared and the counter ahead of them; a
        /// request of a kind or state this build does not know (an E5 save) and a second one for the same enclave are
        /// dropped (their journal quests close at the first tick's pass); session state cleared.
        /// </summary>
        public static void OnRegistryChanged()
        {
            Reset();
            Community c = ScRegistry.Community;
            var ids = new HashSet<int>();
            var enclaves = new HashSet<int>();
            int max = 0;
            foreach (EnclaveRequest r in All) if (r.Id > max) max = r.Id;
            int dropped = 0;
            foreach (EnclaveRequest r in new List<EnclaveRequest>(All))
            {
                if (!KnownKind(r.Kind) || (!r.IsOffered && !r.IsAccepted) || !enclaves.Add(r.EnclaveId))
                {
                    All.Remove(r);
                    dropped++;
                    ScLog.Warn("enclave request #" + r.Id + " (" + r.Kind + ", " + r.State + ", enclave " + r.EnclaveId + ") is not one this build can keep - dropped");
                    continue;
                }
                if (r.Id <= 0 || !ids.Add(r.Id))
                {
                    int old = r.Id;
                    r.Id = Math.Max(c.NextEncRequestId, max) + 1;
                    max = r.Id;
                    ids.Add(r.Id);
                    ScLog.Warn("enclave request had a duplicate id " + old + " - now " + r.Id);
                }
                if (r.Id > c.NextEncRequestId) c.NextEncRequestId = r.Id;
                // a hand-edited resource request: the classic five, 1 to 3 units
                if (r.Kind == EnclaveRequest.KindResource && ((int)r.Res >= Asked || r.Units < 1 || r.Units > 3))
                {
                    ScLog.Warn("enclave request #" + r.Id + " asked for " + r.Units + " " + r.Res + " - now " + Mathf.Clamp(r.Units, 1, 3) + " "
                               + ((int)r.Res >= Asked ? Resource.Food : r.Res));
                    if ((int)r.Res >= Asked) r.Res = Resource.Food;
                    r.Units = Mathf.Clamp(r.Units, 1, 3);
                }
            }
            if (dropped > 0) ScLog.Out("enclave requests: " + dropped + " dropped at load (see above)");
        }

        // ------------------------------------------------------------------ lookups ----

        public static bool KnownKind(string kind) { return Array.IndexOf(Kinds, kind) >= 0; }

        /// <summary>The open request of an enclave, or null.</summary>
        public static EnclaveRequest Of(Enclave e)
        {
            if (e == null) return null;
            foreach (EnclaveRequest r in All) if (r.EnclaveId == e.Id) return r;
            return null;
        }

        public static EnclaveRequest ById(int id)
        {
            foreach (EnclaveRequest r in All) if (r.Id == id) return r;
            return null;
        }

        /// <summary>
        /// The request clock's day: the world's day, or the day the request pass last ran when that is ahead of it ("sc day"
        /// moves the clock a day per call). Offers, acceptances and due days count in it.
        /// </summary>
        public static int Now { get { return Math.Max(ScCommunity.Today, ScRegistry.Community.EncRequestDay); } }

        /// <summary>0 Neutral, 1 Friendly, 2 Allied (the losses' and chances' column).</summary>
        private static int Band(Enclave e)
        {
            switch (ScEnclaves.StandingKey(e))
            {
                case "allied": return 2;
                case "friendly": return 1;
                default: return 0;
            }
        }

        private static int Loss(int[] byBand, Enclave e)
        {
            return byBand == null || byBand.Length == 0 ? 0 : Math.Max(0, byBand[Math.Min(Band(e), byBand.Length - 1)]);
        }

        /// <summary>The day's chance of a new request at the enclave's band (EnclaveRequestChanceNeutral/Friendly/Allied).</summary>
        public static float ChanceFor(Enclave e)
        {
            switch (Band(e))
            {
                case 2: return ScConfig.EnclaveRequestChanceAllied;
                case 1: return ScConfig.EnclaveRequestChanceFriendly;
                default: return ScConfig.EnclaveRequestChanceNeutral;
            }
        }

        /// <summary>
        /// The words a statement says, by kind and band: stem_kind_band, then stem_kind, stem_band and stem (missions 4.3's
        /// fallbacks). The first one Localization has.
        /// </summary>
        private static string LineKey(string stem, string kind, string band)
        {
            string k = stem + "_" + kind + "_" + band;
            if (Localization.Exists(k)) return k;
            k = stem + "_" + kind;
            if (Localization.Exists(k)) return k;
            k = stem + "_" + band;
            return Localization.Exists(k) ? k : stem;
        }

        /// <summary>"a medium bundle of medicine", "a generator bank", "a gun": what the request asks for.</summary>
        public static string WhatText(EnclaveRequest r)
        {
            if (r == null) return "";
            switch (r.Kind)
            {
                case EnclaveRequest.KindResource: return string.Format(Loc("scEncReqWhatResource"), SizeWord(r.Units), Loc("scEncReqRes" + r.Res));
                case EnclaveRequest.KindGenerator: return Loc("scEncReqWhatGenerator");
                case EnclaveRequest.KindGun: return Loc("scEncReqWhatGun");
                default: return r.Kind;
            }
        }

        private static string SizeWord(int units)
        {
            return Loc(units >= 3 ? "scBundleSizeLarge" : units == 2 ? "scBundleSizeMedium" : "scBundleSizeSmall");
        }

        /// <summary>The resource a kind of group lives on (they never ask for it), or -1 for one that lives on anything.</summary>
        private static int OwnResource(string kind)
        {
            switch (kind)
            {
                case "growers": return (int)Resource.Food;
                case "medics": return (int)Resource.Meds;
                case "militia": return (int)Resource.Ammo;
                case "builders": return (int)Resource.Materials;
                case "roadcrew": return (int)Resource.Fuel;
                default: return -1;
            }
        }

        // ------------------------------------------------------------------ pacing (ScDaily.Run) ----

        /// <summary>
        /// The day's request pass, after the outposts' and before morale's. The request clock moves to the day (one day
        /// further for each "sc day"); a day it already passed does nothing. Offers past their day lapse and accepted ones
        /// past their due day expire (losses by band); a request whose enclave is gone, hostile or off the map this load is
        /// withdrawn. New offers come only on the newest day of a catch-up. ScDaily saves.
        /// </summary>
        public static void Daily(int day, EntityPlayerLocal player)
        {
            try
            {
                if (!ScEnclaves.Ready || !ScConfig.Enclaves || !ScRegistry.Loaded) return;
                Community c = ScRegistry.Community;
                int d = day;
                if (ScTicker.SimulatedDay == day) d = Math.Max(day, c.EncRequestDay + 1);       // "sc day": the next day each time
                if (d <= c.EncRequestDay)
                {
                    if (!clockSaid)
                    {
                        clockSaid = true;
                        ScLog.Out("enclave requests: the request clock is at day " + c.EncRequestDay + " (sc day, or the world's clock went back) - day "
                                  + day + " passes for them once the world gets there");
                    }
                    return;
                }
                c.EncRequestDay = d;
                foreach (EnclaveRequest r in All.ToArray())
                {
                    Enclave e = ScEnclaves.ById(r.EnclaveId);
                    if (e == null || e.IsHostile || e.Lost)
                    {
                        Close(r, Withdrawn, e == null ? "the enclave is gone" : e.Lost ? "their building is not in this world" : "they are hostile");
                        continue;
                    }
                    if (r.IsOffered && d > r.LapseDay) Close(r, Lapsed, "unanswered past day " + r.LapseDay);
                    else if (r.IsAccepted && d > r.DueDay) Close(r, Expired, "not delivered by day " + r.DueDay);
                }
                if (d >= ScCommunity.Today) Roll(d, player);
            }
            catch (Exception e)
            {
                ScLog.Once("encreq-daily", e);
            }
        }

        /// <summary>
        /// ScTicker: the world's day went back (an older save, a time command - never "sc day", whose own end is no jump). The
        /// request clock follows it and every open request and enclave cooldown keeps its remaining span, so lapses, expiries
        /// and rolls go on from the world's day instead of waiting for it to catch up (the E4 review's M1).
        /// </summary>
        public static void OnWorldDayBack(int day)
        {
            try
            {
                Community c = ScRegistry.Community;
                int back = c.EncRequestDay - day;
                if (back <= 0) return;
                c.EncRequestDay = day;
                foreach (EnclaveRequest r in All)
                {
                    r.OfferDay -= back;
                    r.LapseDay -= back;
                    if (r.AcceptDay >= 0) r.AcceptDay -= back;
                    if (r.DueDay >= 0) r.DueDay -= back;
                }
                foreach (Enclave e in ScEnclaves.All) if (e.NextRequestDay > 0) e.NextRequestDay = Math.Max(0, e.NextRequestDay - back);
                EntityPlayerLocal pl = ScCommunity.LocalPlayer;
                foreach (EnclaveRequest r in All) if (r.IsAccepted) SyncDue(QuestOf(pl, r), r);
                clockSaid = false;
                ScLog.Out("enclave requests: the world's day went back to " + day + " - the request clock and " + All.Count + " open request(s) moved back "
                          + back + " day(s)");
            }
            catch (Exception e)
            {
                ScLog.Once("encreq-dayback", e);
            }
        }

        /// <summary>
        /// The day's new offers: every eligible enclave in a shuffled order (a day's own seed), each rolling the chance for its
        /// band, until EnclaveRequestMaxOpen are open across the map.
        /// </summary>
        private static void Roll(int day, EntityPlayerLocal player)
        {
            int max = Math.Max(0, ScConfig.EnclaveRequestMaxOpen);
            if (All.Count >= max) return;
            var cands = new List<Enclave>();
            foreach (Enclave e in ScEnclaves.All)
                if (Eligible(e, day) == null) cands.Add(e);
            if (cands.Count == 0) return;
            World w = ScCommunity.World;
            GameRandom rnd = GameRandomManager.Instance.CreateGameRandom((w != null ? w.Seed : 0) ^ Salt.GetStableHashCode() ^ unchecked(day * 19349663));
            try
            {
                for (int i = cands.Count - 1; i > 0; i--)
                {
                    int j = rnd.RandomRange(0, i + 1);
                    Enclave t = cands[i];
                    cands[i] = cands[j];
                    cands[j] = t;
                }
                foreach (Enclave e in cands)
                {
                    if (All.Count >= max) break;
                    float chance = ChanceFor(e);
                    if (rnd.RandomFloat >= chance) continue;
                    string kind = PickKind(rnd, day);
                    if (kind == null) break;
                    Offer(e, kind, day, rnd, -1, 0, player, "the day's roll (" + Mathf.RoundToInt(chance * 100f) + "%)");
                }
            }
            finally
            {
                GameRandomManager.Instance.FreeGameRandom(rnd);
            }
        }

        /// <summary>Why this enclave cannot be asked to make a request today (null when it can): the daily roll's filter.</summary>
        public static string Eligible(Enclave e, int day)
        {
            if (e == null) return "no enclave";
            if (!e.Known) return "not found yet";
            if (e.IsHostile) return "hostile";
            if (e.Lost) return "its building is not in this world";
            if (Of(e) != null) return "one is open already";
            // people, but nobody leading at home (the leader died while the others are out with you): nobody to answer, and an
            // unanswered offer would lapse with a penalty (the E4 review's L2). An enclave whose people were never made still asks.
            if (ScEnclavePeople.LeaderOf(e) == null && ScEnclavePeople.Living(e).Count > 0) return "nobody leads them just now";
            if (day < e.NextRequestDay) return "the cooldown runs to day " + e.NextRequestDay;
            return null;
        }

        /// <summary>A kind by EnclaveRequestKindWeights; generators only from day EnclaveGeneratorFromDay. Null when no kind weighs anything.</summary>
        private static string PickKind(GameRandom rnd, int day)
        {
            float total = 0f;
            foreach (string k in Kinds) total += KindWeight(k, day);
            if (total <= 0f) return null;
            float x = rnd.RandomFloat * total;
            string last = null;
            foreach (string k in Kinds)
            {
                float wt = KindWeight(k, day);
                if (wt <= 0f) continue;
                last = k;
                if (x < wt) return k;
                x -= wt;
            }
            return last;
        }

        private static float KindWeight(string kind, int day)
        {
            if (kind == EnclaveRequest.KindGenerator && day < ScConfig.EnclaveGeneratorFromDay) return 0f;
            float wt;
            return ScConfig.EnclaveRequestKindWeights.TryGetValue(kind, out wt) ? Math.Max(0f, wt) : 0f;
        }

        /// <summary>The units a resource request asks (1 small, 2 medium, 3 large) by EnclaveRequestSizeWeights; small when none weighs anything.</summary>
        private static int PickUnits(GameRandom rnd)
        {
            float total = 0f;
            foreach (string s in Sizes) total += SizeWeight(s);
            if (total <= 0f || rnd == null) return 1;
            float x = rnd.RandomFloat * total;
            for (int i = 0; i < Sizes.Length; i++)
            {
                float wt = SizeWeight(Sizes[i]);
                if (wt <= 0f) continue;
                if (x < wt) return i + 1;
                x -= wt;
            }
            return 1;
        }

        private static float SizeWeight(string size)
        {
            float wt;
            return ScConfig.EnclaveRequestSizeWeights.TryGetValue(size, out wt) ? Math.Max(0f, wt) : 0f;
        }

        /// <summary>What they lack: any of the five but the one their kind lives on (a general group lacks any of them), equally likely.</summary>
        private static Resource PickResource(Enclave e, GameRandom rnd)
        {
            int own = OwnResource(e.Kind);
            var pool = new List<int>();
            for (int i = 0; i < Asked; i++) if (i != own) pool.Add(i);
            return (Resource)pool[rnd != null ? rnd.RandomRange(0, pool.Count) : 0];
        }

        /// <summary>
        /// A new offer from an enclave (the daily roll, or "sc enclave request offer"): the record (res &lt; 0 and units 0:
        /// rolled), the radio's announcement when one is powered (a tell and a ledger line) - otherwise the leader asks in
        /// person - the log and a save.
        /// </summary>
        public static EnclaveRequest Offer(Enclave e, string kind, int day, GameRandom rnd, int res, int units, EntityPlayerLocal player, string why)
        {
            Community c = ScRegistry.Community;
            var r = new EnclaveRequest
            {
                Id = ++c.NextEncRequestId,
                EnclaveId = e.Id,
                Kind = kind,
                State = EnclaveRequest.StateOffered,
                OfferDay = day,
                LapseDay = day + Math.Max(1, ScConfig.EnclaveOfferLapseDays) - 1
            };
            if (kind == EnclaveRequest.KindResource)
            {
                r.Res = res >= 0 && res < Asked ? (Resource)res : PickResource(e, rnd);
                r.Units = units >= 1 && units <= 3 ? units : PickUnits(rnd);
            }
            All.Add(r);
            bool heard = ScRadio.Online();
            if (heard)
            {
                if (player != null) ScCommunity.Tell(player, string.Format(Loc("scEncReqHeard"), e.Name), "");
                ScOutposts.NoteLedger(string.Format(Loc("scEncReqLedgerAsk"), e.Name));
            }
            ScLog.Out("enclave " + e.Id + " '" + e.Name + "': request #" + r.Id + " offered on day " + day + " - " + Describe(r) + ", the offer stands to the end of day "
                      + r.LapseDay + " (" + why + "; " + (heard ? "announced on the radio" : "no radio: the leader asks in person") + ")");
            ScRegistry.SaveAtomic();
            return r;
        }

        /// <summary>"resource: 2 meds (medium)" - the log's and the console's short form.</summary>
        public static string Describe(EnclaveRequest r)
        {
            if (r == null) return "-";
            return r.Kind + (r.Kind == EnclaveRequest.KindResource ? ": " + r.Units + " " + ScNames.Key(r.Res) + " (" + Sizes[Mathf.Clamp(r.Units, 1, 3) - 1] + ")" : "");
        }

        // ------------------------------------------------------------------ accepting and closing ----

        /// <summary>
        /// "Leave it with me." (and "sc enclave request accept"): due by the end of day now + EnclaveDueDaysItems - 1; the journal
        /// quest (quest_scEncRequest: the title "Help &lt;enclave&gt;", what and by when, the marker on their building) is
        /// added for the player. The record is the authority: without a journal (no player, the quest class missing) it is
        /// accepted all the same. False when it is not an open offer.
        /// </summary>
        public static bool Accept(EnclaveRequest r, EntityPlayerLocal player, string why)
        {
            Enclave e = r != null ? ScEnclaves.ById(r.EnclaveId) : null;
            if (e == null || !r.IsOffered || !All.Contains(r)) return false;
            int now = Now;
            r.State = EnclaveRequest.StateAccepted;
            r.AcceptDay = now;
            r.DueDay = now + Math.Max(1, ScConfig.EnclaveDueDaysItems) - 1;
            string error = null;
            Quest q = player != null ? ScQuests.StartRequest(player, e, r, WhatText(r), out error) : null;
            r.QuestCode = q != null ? q.QuestCode : 0;
            if (q == null) ScLog.Warn("enclave request #" + r.Id + ": no journal quest (" + (error ?? "no player") + ") - accepted all the same");
            ScLog.Out("enclave " + e.Id + " '" + e.Name + "': request #" + r.Id + " (" + Describe(r) + ") accepted on day " + now + ", due by the end of day " + r.DueDay
                      + " (" + why + ")" + (q != null ? ", journal quest " + q.QuestCode : ""));
            ScRegistry.SaveAtomic();
            return true;
        }

        /// <summary>
        /// The one way a request ends - done, declined, failed, expired, lapsed or withdrawn (E5 too): the standing change for
        /// the enclave's band at that moment (gain for one done; EnclaveLossDecline / EnclaveLossFail / EnclaveLossLapse;
        /// nothing for a withdrawn one or a hostile enclave), the journal quest closed (completed, or removed as failed), the
        /// enclave's cooldown, SurvivorOrigins' favour event for one done, a ledger line, a tell when the band changed (and
        /// when it lapsed or ran out), the log, a save. what: the ledger's words for what was handed over (null: the request's).
        /// Returns the standing it moved.
        /// </summary>
        public static int Close(EnclaveRequest r, string outcome, string reason, int gain = 0, string what = null)
        {
            if (r == null || !All.Contains(r)) return 0;
            Enclave e = ScEnclaves.ById(r.EnclaveId);
            EntityPlayerLocal pl = ScCommunity.LocalPlayer;
            int delta = 0;
            if (e != null && !e.IsHostile)
            {
                switch (outcome)
                {
                    case Done: delta = Math.Max(0, gain); break;
                    case Declined: delta = -Loss(ScConfig.EnclaveLossDecline, e); break;
                    case Failed:
                    case Expired: delta = -Loss(ScConfig.EnclaveLossFail, e); break;
                    case Lapsed: delta = -Loss(ScConfig.EnclaveLossLapse, e); break;
                }
            }
            // the journal mirror first, while the record still names it
            if (pl != null) CloseQuest(pl, r, outcome == Done);
            All.Remove(r);
            if (pick != null && pick.RequestId == r.Id) pick = null;
            int moved = 0;
            bool bandChanged = false;
            if (e != null)
            {
                // never a new one in the day it closed, then EnclaveRequestCooldownDays whole days of rest
                e.NextRequestDay = Now + 1 + Math.Max(0, ScConfig.EnclaveRequestCooldownDays);
                string before = ScEnclaves.StandingKey(e);
                int old = e.Standing;
                if (delta != 0) ScEnclaves.AddStanding(e, delta, "request #" + r.Id + " " + outcome, false);
                moved = e.Standing - old;
                bandChanged = ScEnclaves.StandingKey(e) != before;
                ScOutposts.NoteLedger(LedgerLine(e, r, outcome, moved, bandChanged, what));
                if (pl != null)
                {
                    if (outcome == Lapsed) ScCommunity.Tell(pl, string.Format(Loc("scEncReqLapsedTell"), e.Name), "");
                    else if (outcome == Expired) ScCommunity.Tell(pl, string.Format(Loc("scEncReqExpiredTell"), e.Name), "ui_denied");
                    if (bandChanged && !e.IsHostile) ScCommunity.Tell(pl, string.Format(Loc("scEncReqBandTell"), e.Name, ScEnclaves.StandingWord(e)), "");
                }
            }
            if (outcome == Done) ScLeadership.Count(ScLeadership.FavourEvent);
            ScLog.Out("enclave " + r.EnclaveId + (e != null ? " '" + e.Name + "'" : "") + ": request #" + r.Id + " (" + Describe(r) + ", " + r.State + ") " + outcome
                      + (moved != 0 ? ", standing " + (moved > 0 ? "+" : "") + moved + (e != null ? " (" + ScEnclaves.StandingKey(e) + ")" : "") : "")
                      + " - " + reason + (e != null ? "; the next one from day " + e.NextRequestDay : ""));
            ScRegistry.SaveAtomic();
            return moved;
        }

        /// <summary>
        /// The enclave's open request ends with no standing change: it turned hostile (ScEnclaves.TurnHostile, before the flip),
        /// fell (ScEnclaves.Fall), lost its building (ScEnclaves.Validate) or was reseeded away.
        /// </summary>
        public static void Withdraw(Enclave e, string why)
        {
            EnclaveRequest r = Of(e);
            if (r != null) Close(r, Withdrawn, why);
        }

        private static string LedgerLine(Enclave e, EnclaveRequest r, string outcome, int moved, bool bandChanged, string what)
        {
            string delta = moved == 0 ? Loc("scEncReqLedgerNoChange")
                         : bandChanged ? string.Format(Loc("scEncReqLedgerDeltaBand"), (moved > 0 ? "+" : "") + moved, ScEnclaves.StandingWord(e))
                         : string.Format(Loc("scEncReqLedgerDelta"), (moved > 0 ? "+" : "") + moved);
            switch (outcome)
            {
                case Done: return string.Format(Loc("scEncReqLedgerDone"), e.Name, what ?? WhatText(r), delta);
                case Declined: return string.Format(Loc("scEncReqLedgerDeclined"), e.Name, delta);
                case Failed: return string.Format(Loc("scEncReqLedgerFailed"), e.Name, delta);
                case Expired: return string.Format(Loc("scEncReqLedgerExpired"), e.Name, delta);
                case Lapsed: return string.Format(Loc("scEncReqLedgerLapsed"), e.Name, delta);
                default: return string.Format(Loc("scEncReqLedgerWithdrawn"), e.Name);
            }
        }

        /// <summary>The gain for a request done of a kind (a gun's is its own: GunPoints).</summary>
        private static int GainFor(EnclaveRequest r)
        {
            return r.Kind == EnclaveRequest.KindGenerator ? ScConfig.EnclaveGainGenerator : ScConfig.EnclaveGainResource;
        }

        // ------------------------------------------------------------------ the journal mirror ----

        /// <summary>
        /// The request's journal quest in progress: by the request id it carries, else by its code - two quests added in one
        /// frame can share a vanilla code (the E4 review's L1).
        /// </summary>
        private static Quest QuestOf(EntityPlayer player, EnclaveRequest r)
        {
            QuestJournal j = player != null ? player.QuestJournal : null;
            if (j == null || j.quests == null || r == null) return null;
            Quest byCode = null;
            for (int i = 0; i < j.quests.Count; i++)
            {
                Quest q = j.quests[i];
                if (q == null || q.CurrentState != Quest.QuestState.InProgress || !ScQuests.IsRequestQuest(q)) continue;
                if (ScQuests.RequestIdOf(q) == r.Id) return q;
                if (byCode == null && r.QuestCode != 0 && q.QuestCode == r.QuestCode) byCode = q;
            }
            return byCode;
        }

        private static void CloseQuest(EntityPlayer player, EnclaveRequest r, bool completed)
        {
            Quest q = QuestOf(player, r);
            if (q != null) ScQuests.CloseRequest(player, q, completed);
        }

        /// <summary>ScTicker, every second: the once-a-session journal pass, as soon as the player's journal is there.</summary>
        public static void Tick(World w, EntityPlayerLocal player)
        {
            try
            {
                if (synced || !ScEnclaves.Ready || player == null || player.QuestJournal == null || !ScRegistry.Loaded) return;
                // done, or tried a few seconds running while the player's record was not there to add a quest to
                synced = SyncJournal(player) || ++syncTries >= MaxSyncTries;
            }
            catch (Exception e)
            {
                synced = true;
                ScLog.Once("encreq-tick", e);
            }
        }

        /// <summary>
        /// The journal squared with the records once a session (the record is the authority; the player's file and the
        /// registry are saved apart, so a crash can leave either ahead): an in-progress request quest whose request is gone,
        /// not accepted or already mirrored is closed (removed as failed); one that is kept takes the record's code and its
        /// enclave's building now (an enclave that moved: the marker follows); an accepted request with no quest gets one
        /// again (Enclaves on). False when a quest could not be added yet (the player's record is not in): tried again.
        /// </summary>
        /// <summary>
        /// The journal line's due day follows the record (a world day set back moved it, or a hand-edited registry): the
        /// variable, the objectives' cached text and the tracker (the E4 re-check's R1).
        /// </summary>
        private static void SyncDue(Quest q, EnclaveRequest r)
        {
            if (q == null || r == null) return;
            string due = r.DueDay.ToString(), had;
            if (q.DataVariables.TryGetValue(ScQuests.RequestDueVar, out had) && had == due) return;
            q.DataVariables[ScQuests.RequestDueVar] = due;
            foreach (BaseObjective o in q.Objectives) if (o != null) o.SetupDisplay();
            if (q.OwnerJournal != null) q.OwnerJournal.RefreshQuest(q);
        }

        private static bool SyncJournal(EntityPlayerLocal player)
        {
            QuestJournal j = player.QuestJournal;
            if (j == null || j.quests == null) return false;
            var mirrored = new HashSet<int>();
            var gone = new List<Quest>();
            bool changed = false, retry = false;
            int kept = 0, readded = 0;
            for (int i = 0; i < j.quests.Count; i++)
            {
                Quest q = j.quests[i];
                if (q == null || q.CurrentState != Quest.QuestState.InProgress || !ScQuests.IsRequestQuest(q)) continue;
                EnclaveRequest r = ById(ScQuests.RequestIdOf(q));
                Enclave e = r != null ? ScEnclaves.ById(r.EnclaveId) : null;
                if (r == null || !r.IsAccepted || e == null || !mirrored.Add(r.Id)) { gone.Add(q); continue; }
                kept++;
                if (r.QuestCode != q.QuestCode) { r.QuestCode = q.QuestCode; changed = true; }
                ScQuests.FollowRequestPoi(q, e);
                SyncDue(q, r);
            }
            foreach (Quest q in gone) ScQuests.CloseRequest(player, q, false);
            if (ScConfig.Enclaves)
            {
                foreach (EnclaveRequest r in All)
                {
                    if (!r.IsAccepted || mirrored.Contains(r.Id)) continue;
                    Enclave e = ScEnclaves.ById(r.EnclaveId);
                    if (e == null || e.Lost || e.IsHostile) continue;     // no building this load / withdrawn at the next day's pass
                    string error;
                    Quest q = ScQuests.StartRequest(player, e, r, WhatText(r), out error);
                    if (q == null)
                    {
                        if (error == ScQuests.RequestJournalNotReady) retry = true;
                        continue;
                    }
                    r.QuestCode = q.QuestCode;
                    mirrored.Add(r.Id);
                    readded++;
                    changed = true;
                }
            }
            if (changed) ScRegistry.SaveAtomic();
            if (gone.Count > 0 || readded > 0 || kept > 0)
                ScLog.Out("enclave requests: the journal has " + kept + " request quest(s) of open requests" + (gone.Count > 0 ? ", " + gone.Count + " closed (their request is gone)" : "")
                          + (readded > 0 ? ", " + readded + " added again (the journal had lost them)" : ""));
            return !retry;
        }

        // ------------------------------------------------------------------ the inventory ----

        /// <summary>The player's slots that hold something: the bag, then the toolbelt's own slots (never its hidden extra one).</summary>
        private static List<Held> Slots(EntityPlayerLocal pl)
        {
            var list = new List<Held>();
            if (pl == null) return list;
            ItemStack[] bag = pl.bag != null ? pl.bag.GetSlots() : null;
            if (bag != null)
                for (int i = 0; i < bag.Length; i++)
                    if (bag[i] != null && !bag[i].IsEmpty() && bag[i].itemValue != null && bag[i].itemValue.ItemClass != null)
                        list.Add(new Held { Belt = false, Slot = i, Stack = bag[i] });
            Inventory inv = pl.inventory;
            if (inv != null)
            {
                int n = Math.Min(inv.PUBLIC_SLOTS, inv.GetSlotCount());
                for (int i = 0; i < n; i++)
                {
                    ItemStack s = inv.GetItem(i);
                    if (s != null && !s.IsEmpty() && s.itemValue != null && s.itemValue.ItemClass != null) list.Add(new Held { Belt = true, Slot = i, Stack = s });
                }
            }
            return list;
        }

        /// <summary>The slot still holds that item (its type, and equal but for use and rounds: the same gun).</summary>
        private static bool Same(ItemStack s, ItemValue expect)
        {
            return s != null && !s.IsEmpty() && s.itemValue != null && expect != null && s.itemValue.type == expect.type && s.itemValue.EqualsExceptUseTimesAndAmmo(expect);
        }

        /// <summary>
        /// One item taken from an exact slot after checking it still holds 'expect' - never XUiM_PlayerInventory.RemoveItems,
        /// which passes over an item with mods and takes the first stack of the type (missions 0.4). The bag's SetSlot raises
        /// its change event (the backpack window; RE Inventory's grid reconciles on it and drops the slot's placement), the
        /// toolbelt's SetItem its own. True when the slot shows the item gone.
        /// </summary>
        private static bool TakeOne(EntityPlayerLocal pl, bool belt, int slot, ItemValue expect)
        {
            if (pl == null || expect == null) return false;
            if (belt)
            {
                Inventory inv = pl.inventory;
                if (inv == null || slot < 0 || slot >= Math.Min(inv.PUBLIC_SLOTS, inv.GetSlotCount())) return false;
                ItemStack s = inv.GetItem(slot);
                if (!Same(s, expect)) return false;
                int count = s.count;
                if (count > 1) inv.SetItem(slot, s.itemValue.Clone(), count - 1);
                else inv.SetItem(slot, ItemStack.Empty.Clone());
                ItemStack after = inv.GetItem(slot);
                return count > 1 ? after != null && after.count == count - 1 : after == null || after.IsEmpty();
            }
            Bag bag = pl.bag;
            ItemStack[] items = bag != null ? bag.GetSlots() : null;
            if (items == null || slot < 0 || slot >= items.Length) return false;
            ItemStack b = items[slot];
            if (!Same(b, expect)) return false;
            int n = b.count;
            bag.SetSlot(slot, n > 1 ? new ItemStack(b.itemValue.Clone(), n - 1) : ItemStack.Empty.Clone(), true);
            ItemStack[] now = bag.GetSlots();
            ItemStack a = now != null && slot < now.Length ? now[slot] : null;
            return n > 1 ? a != null && a.count == n - 1 : a == null || a.IsEmpty();
        }

        private static XUiM_PlayerInventory InventoryOf(EntityPlayerLocal pl)
        {
            return pl != null && pl.PlayerUI != null && pl.PlayerUI.xui != null ? pl.PlayerUI.xui.PlayerInventory : null;
        }

        // ---- a resource request: bundles ----

        /// <summary>The bundles of a resource the player carries, by size.</summary>
        private static Bundles Carried(EntityPlayerLocal pl, Resource res)
        {
            var b = new Bundles();
            string legacy = ScNames.BundleItem(res);
            foreach (Held h in Slots(pl))
            {
                Resource r;
                int units;
                if (!ScStockpile.IsBundle(h.Stack, out r, out units) || r != res) continue;
                if (units >= 3) b.Large += h.Stack.count;
                else if (units == 2) b.Medium += h.Stack.count;
                else if (h.Stack.itemValue.ItemClass.Name == legacy) b.Legacy += h.Stack.count;
                else b.Small += h.Stack.count;
            }
            return b;
        }

        /// <summary>
        /// The bundles that cover 'units' with the least overpay (any overpay is a gift), then the fewest, then the most of the
        /// old one-size bundles (nothing makes them any more). At most four sizes and three units: a tiny search.
        /// </summary>
        private static bool Choose(Bundles have, int units, out Bundles take)
        {
            take = new Bundles();
            bool found = false;
            int bestOver = int.MaxValue, bestCount = int.MaxValue, bestLegacy = -1;
            int cap = Math.Max(1, units);
            for (int l = 0; l <= Math.Min(have.Large, cap); l++)
                for (int m = 0; m <= Math.Min(have.Medium, cap); m++)
                    for (int s = 0; s <= Math.Min(have.Small, cap); s++)
                        for (int g = 0; g <= Math.Min(have.Legacy, cap); g++)
                        {
                            int total = l * 3 + m * 2 + s + g;
                            if (total < units) continue;
                            int over = total - units, count = l + m + s + g;
                            if (over > bestOver || (over == bestOver && (count > bestCount || (count == bestCount && g <= bestLegacy)))) continue;
                            bestOver = over;
                            bestCount = count;
                            bestLegacy = g;
                            take = new Bundles { Large = l, Medium = m, Small = s, Legacy = g };
                            found = true;
                        }
            return found;
        }

        /// <summary>
        /// The chosen bundles out of the bag and the toolbelt (XUiM_PlayerInventory.RemoveItems by type: bundles have no mods),
        /// measured per type before and after as ScCategories measures; when any came up short, whatever left is given back
        /// and nothing counts.
        /// </summary>
        private static bool TakeBundles(EntityPlayerLocal pl, Resource res, Bundles take)
        {
            XUiM_PlayerInventory inv = InventoryOf(pl);
            if (inv == null) return false;
            BundleSize[] sizes = { BundleSize.Large, BundleSize.Medium, BundleSize.Small, BundleSize.Legacy };
            int[] want = { take.Large, take.Medium, take.Small, take.Legacy };
            var values = new ItemValue[4];
            var before = new int[4];
            var stacks = new List<ItemStack>();
            for (int i = 0; i < 4; i++)
            {
                if (want[i] <= 0) continue;
                values[i] = ScStockpile.BundleValue(res, sizes[i]);
                if (values[i] == null) return false;
                before[i] = inv.GetItemCount(values[i]);
                stacks.Add(new ItemStack(values[i].Clone(), want[i]));
            }
            if (stacks.Count == 0) return false;
            try { inv.RemoveItems(stacks); }
            catch (Exception e) { ScLog.Once("encreq-takebundles", e); }
            bool ok = true;
            var removed = new int[4];
            for (int i = 0; i < 4; i++)
            {
                if (want[i] <= 0) continue;
                removed[i] = Math.Max(0, before[i] - inv.GetItemCount(values[i]));
                if (removed[i] < want[i]) ok = false;
            }
            if (ok) return true;
            for (int i = 0; i < 4; i++)
            {
                if (removed[i] <= 0) continue;
                var back = new ItemStack(values[i].Clone(), removed[i]);
                if (!inv.AddItem(back)) GameManager.Instance.ItemDropServer(back, pl.GetDropPosition(), Vector3.zero, -1, 60f);
            }
            ScLog.Warn("enclave request: " + res + " bundles came up short when taken (" + string.Join(",", Array.ConvertAll(removed, x => x.ToString())) + " of "
                       + string.Join(",", Array.ConvertAll(want, x => x.ToString())) + ") - given back, not handed over");
            return false;
        }

        // ---- a generator request ----

        /// <summary>A generator: any item whose block is a BlockGenerator (vanilla's generatorbank, another mod's generator).</summary>
        public static bool IsGenerator(ItemClass ic)
        {
            return ic != null && ic.IsBlock() && ic.GetBlock() is BlockGenerator;
        }

        private static bool FindGenerator(EntityPlayerLocal pl, out Held at)
        {
            foreach (Held h in Slots(pl))
            {
                if (!IsGenerator(h.Stack.itemValue.ItemClass)) continue;
                at = h;
                return true;
            }
            at = default(Held);
            return false;
        }

        // ---- a gun request ----

        /// <summary>A gun that counts (Q11): tagged gun or launcher, never bow, crossbow or admin.</summary>
        public static bool IsGun(ItemClass ic)
        {
            return ic != null && !ic.IsBlock() && ic.HasAnyTags(GunTags) && !ic.HasAnyTags(NotGunTags);
        }

        /// <summary>
        /// A gun's tier: a T# tag (vanilla T0-T3), else T and a digit in its name (IZY's IZYgunT0..T5), else the NPC weapon
        /// catalogue's level + 1 (ScLoadout.LevelOf), else its EconomicValue against EnclaveGunValueBands.
        /// </summary>
        public static int TierOf(ItemClass ic)
        {
            if (ic == null) return 0;
            for (int t = TierTags.Length - 1; t >= 0; t--) if (ic.HasAnyTags(TierTags[t])) return t;
            string n = ic.Name ?? "";
            for (int i = 0; i + 1 < n.Length; i++)
                if (n[i] == 'T' && n[i + 1] >= '0' && n[i + 1] <= '9') return Math.Min(TierTags.Length - 1, n[i + 1] - '0');
            int level = ScLoadout.LevelOf(n);
            if (level >= 0) return Mathf.Clamp(level + 1, 0, TierTags.Length - 1);
            int[] bands = ScConfig.EnclaveGunValueBands;
            int tier = 0;
            while (tier < bands.Length && ic.EconomicValue >= bands[tier]) tier++;
            return Math.Min(TierTags.Length - 1, tier);
        }

        /// <summary>Points = EnclaveGunTierPoints[tier] x (EnclaveGunQualityBase + EnclaveGunQualityStep x quality), rounded, clamped to EnclaveGunPointsMin..Max.</summary>
        public static int GunPoints(int tier, int quality)
        {
            int[] t = ScConfig.EnclaveGunTierPoints;
            int b = t.Length > 0 ? t[Mathf.Clamp(tier, 0, t.Length - 1)] : 0;
            double v = Math.Round(b * ((double)ScConfig.EnclaveGunQualityBase + ScConfig.EnclaveGunQualityStep * (double)quality), MidpointRounding.AwayFromZero);
            int lo = ScConfig.EnclaveGunPointsMin, hi = Math.Max(lo, ScConfig.EnclaveGunPointsMax);
            return Mathf.Clamp((int)v, lo, hi);
        }

        /// <summary>What a gun's points mean to them: scEncReqWorth0..4 by EnclaveGunWorthBands (a band's upper bound).</summary>
        private static string WorthWord(int points)
        {
            int[] b = ScConfig.EnclaveGunWorthBands;
            int i = 0;
            while (i < b.Length && points > b[i]) i++;
            return Loc("scEncReqWorth" + Math.Min(i, 4));
        }

        /// <summary>Every gun the player carries that counts, from the least to the most valuable.</summary>
        public static List<Gun> Guns(EntityPlayerLocal pl)
        {
            var list = new List<Gun>();
            foreach (Held h in Slots(pl))
            {
                ItemClass ic = h.Stack.itemValue.ItemClass;
                if (!IsGun(ic)) continue;
                int tier = TierOf(ic), q = Math.Max(1, (int)h.Stack.itemValue.Quality);
                string name = ic.GetLocalizedItemName();
                if (string.IsNullOrEmpty(name)) name = Loc(ic.Name);
                var g = new Gun { Belt = h.Belt, Slot = h.Slot, Value = h.Stack.itemValue.Clone(), Name = name, Tier = tier, Quality = q, Points = GunPoints(tier, q), Economic = ic.EconomicValue };
                g.Row = string.Format(Loc("scEncReqGunRow"), g.Name, g.Tier, g.Quality, WorthWord(g.Points));
                list.Add(g);
            }
            list.Sort((a, b) =>
            {
                int k = a.Points.CompareTo(b.Points);
                if (k != 0) return k;
                k = a.Economic.CompareTo(b.Economic);
                if (k != 0) return k;
                k = a.Quality.CompareTo(b.Quality);
                if (k != 0) return k;
                k = a.Belt.CompareTo(b.Belt);
                return k != 0 ? k : a.Slot.CompareTo(b.Slot);
            });
            return list;
        }

        /// <summary>The pick's rows (Q11 a): all of them when 4 or fewer, else the 2 least and the 2 most valuable.</summary>
        private static List<Gun> PickOf(List<Gun> all)
        {
            if (all.Count <= GunRows) return all;
            return new List<Gun> { all[0], all[1], all[all.Count - 2], all[all.Count - 1] };
        }

        // ------------------------------------------------------------------ the dialog ----

        /// <summary>
        /// The leader being talked to (alive, at home), their enclave - not hostile, on the map - and its open request (null
        /// when there is none). False for anyone else. Host only (ScEncDialog.PersonTalking).
        /// </summary>
        public static bool LeaderTalking(EntityPlayer player, EntityNPC talkingTo, out Enclave e, out EnclaveRequest r)
        {
            e = null;
            r = null;
            EncPerson p = ScEncDialog.PersonTalking(player, talkingTo);
            if (p == null || !p.IsLeader || p.State != EncPerson.StateHome) return false;
            e = ScEnclavePeople.EnclaveOf(p);
            if (e == null || e.IsHostile || e.Lost) return false;
            r = Of(e);
            return true;
        }

        /// <summary>The request's state for the dialog: "none", "offered" or "accepted".</summary>
        public static string StateWord(EnclaveRequest r) { return r == null ? "none" : r.State; }

        /// <summary>The player carries what the request asks: the units in bundles, a generator, a gun that counts.</summary>
        public static bool PlayerHas(EnclaveRequest r, EntityPlayer player)
        {
            EntityPlayerLocal pl = player as EntityPlayerLocal;
            if (r == null || pl == null) return false;
            switch (r.Kind)
            {
                case EnclaveRequest.KindResource: return Carried(pl, r.Res).Units >= Math.Max(1, r.Units);
                case EnclaveRequest.KindGenerator: { Held h; return FindGenerator(pl, out h); }
                case EnclaveRequest.KindGun:
                    foreach (Held h in Slots(pl)) if (IsGun(h.Stack.itemValue.ItemClass)) return true;
                    return false;
                default: return false;
            }
        }

        /// <summary>"Take one of these.": the pick for this talk (the player, the leader's body, the request).</summary>
        public static void BuildPick(EntityPlayer player, EntityAlive npc, EnclaveRequest r)
        {
            EntityPlayerLocal pl = player as EntityPlayerLocal;
            if (pl == null || npc == null || r == null) { pick = null; return; }
            pick = new GunPick { PlayerId = pl.entityId, NpcId = npc.entityId, RequestId = r.Id, Rows = PickOf(Guns(pl)) };
        }

        /// <summary>Row i of the pick on screen (made again if it belongs to another talk), or null.</summary>
        public static Gun PickRow(int i, EntityPlayer player, EntityAlive npc, EnclaveRequest r)
        {
            if (player == null || npc == null || r == null || i < 0) return null;
            if (pick == null || pick.PlayerId != player.entityId || pick.NpcId != npc.entityId || pick.RequestId != r.Id) BuildPick(player, npc, r);
            return pick != null && i < pick.Rows.Count ? pick.Rows[i] : null;
        }

        /// <summary>
        /// The response entry's "response" binding (Patches_Enclaves.Patch_DialogRowText) for a row whose text starts with '{':
        /// a gun pick row's text, made when the pick was. Every frame: one prefix test, then a list read.
        /// </summary>
        public static void FillRow(XUiC_DialogResponseEntry entry, ref string value)
        {
            if (entry == null || !entry.HasRequirement || !ScCommunity.IsHost) return;
            DialogResponse resp = entry.CurrentResponse;
            string id = resp != null ? resp.ID : null;
            if (id == null || id.Length != GunRowPrefix.Length + 1 || !id.StartsWith(GunRowPrefix, StringComparison.Ordinal)) return;
            int i = id[id.Length - 1] - '0';
            GunPick p = pick;
            value = p != null && i >= 0 && i < p.Rows.Count ? p.Rows[i].Row : "";
        }

        /// <summary>
        /// The line panel's request tokens (ScEncDialog.Fill): {reqask} the offer, {reqwait} the wait, {reqreply} what the
        /// last request action left. Each is a line chosen by kind and band (the part a voice can say) and a sentence with
        /// the specifics.
        /// </summary>
        public static string FillTokens(string s, Enclave e)
        {
            if (s.IndexOf("{reqask}", StringComparison.Ordinal) >= 0) s = s.Replace("{reqask}", AskText(e));
            if (s.IndexOf("{reqwait}", StringComparison.Ordinal) >= 0) s = s.Replace("{reqwait}", WaitText(e));
            if (s.IndexOf("{reqreply}", StringComparison.Ordinal) >= 0) s = s.Replace("{reqreply}", reply != null && reply.EnclaveId == e.Id ? reply.Text : Loc("scEncReqGone"));
            return s;
        }

        private static string AskText(Enclave e)
        {
            EnclaveRequest r = Of(e);
            if (r == null || !r.IsOffered) return Loc("scEncReqGone");
            string info = string.Format(Loc(r.Kind == EnclaveRequest.KindResource ? "scEncReqOfferInfoRes" : "scEncReqOfferInfo"), WhatText(r),
                                        Math.Max(1, ScConfig.EnclaveDueDaysItems), r.LapseDay);
            return Loc(LineKey("scEncAsk", r.Kind, ScEnclaves.StandingKey(e))) + " " + info;
        }

        private static string WaitText(Enclave e)
        {
            EnclaveRequest r = Of(e);
            if (r == null || !r.IsAccepted) return Loc("scEncReqGone");
            return Loc(LineKey("scEncWait", r.Kind, ScEnclaves.StandingKey(e))) + " " + string.Format(Loc("scEncReqWaitInfo"), WhatText(r), r.DueDay);
        }

        private static void SetReply(Enclave e, string text)
        {
            reply = e != null ? new Reply { EnclaveId = e.Id, Text = (text ?? "").Trim() } : null;
        }

        /// <summary>"(standing +10)" / "(standing -5)" after a reply line; "" when it did not move.</summary>
        private static string Aside(int moved)
        {
            return moved > 0 ? string.Format(Loc("scEncReqStandingUp"), moved) : moved < 0 ? string.Format(Loc("scEncReqStandingDown"), -moved) : "";
        }

        /// <summary>The dialog's request actions: the leader and their open request, checked again (the rows were built a moment ago). Host only.</summary>
        private static bool Acting(EntityPlayer player, out Enclave e, out EnclaveRequest r)
        {
            e = null;
            r = null;
            if (!ScCommunity.IsHost || !(player is EntityPlayerLocal)) return false;
            if (LeaderTalking(player, null, out e, out r)) return true;
            reply = null;
            return false;
        }

        /// <summary>"Leave it with me.": the offer accepted (Accept), "Thank you. We'll be here." and by when.</summary>
        public static void ActAccept(EntityPlayer player)
        {
            Enclave e;
            EnclaveRequest r;
            if (!Acting(player, out e, out r)) return;
            if (r == null || !r.IsOffered) { SetReply(e, Loc("scEncReqGone")); return; }
            string band = ScEnclaves.StandingKey(e);
            if (!Accept(r, player as EntityPlayerLocal, "the player said yes")) { SetReply(e, Loc("scEncReqGone")); return; }
            SetReply(e, Loc(LineKey("scEncAccepted", r.Kind, band)) + " " + string.Format(Loc("scEncReqAcceptInfo"), WhatText(r), r.DueDay));
        }

        /// <summary>
        /// "I have it right here." (a resource or a generator, offered or accepted): taken from the player - checked again -
        /// and the request done (+EnclaveGainResource / +EnclaveGainGenerator); the leader's thanks by kind and band.
        /// </summary>
        public static void ActGive(EntityPlayer player)
        {
            Enclave e;
            EnclaveRequest r;
            if (!Acting(player, out e, out r)) return;
            if (r == null || (r.Kind != EnclaveRequest.KindResource && r.Kind != EnclaveRequest.KindGenerator)) { SetReply(e, Loc("scEncReqGone")); return; }
            EntityPlayerLocal pl = (EntityPlayerLocal)player;
            string band = ScEnclaves.StandingKey(e), what = WhatText(r);
            bool taken = false;
            if (r.Kind == EnclaveRequest.KindResource)
            {
                Bundles take;
                Bundles have = Carried(pl, r.Res);
                if (Choose(have, Math.Max(1, r.Units), out take) && TakeBundles(pl, r.Res, take))
                {
                    taken = true;
                    if (take.Units > r.Units) ScLog.Out("enclave request #" + r.Id + ": " + take.Units + " " + ScNames.Key(r.Res) + " unit(s) handed over for " + r.Units + " (the rest a gift)");
                }
            }
            else
            {
                Held h;
                if (FindGenerator(pl, out h))
                {
                    // its name first: the toolbelt's SetItem clears the slot's stack in place
                    ItemValue iv = h.Stack.itemValue.Clone();
                    string name = iv.ItemClass != null ? iv.ItemClass.GetLocalizedItemName() : null;
                    if (TakeOne(pl, h.Belt, h.Slot, iv))
                    {
                        taken = true;
                        if (!string.IsNullOrEmpty(name)) what = name;
                        ScLog.Out("enclave request #" + r.Id + ": " + iv.ItemClass.Name + " from the " + (h.Belt ? "toolbelt" : "bag") + " slot " + h.Slot);
                    }
                }
            }
            if (!taken) { SetReply(e, Loc("scEncReqNotCarried")); return; }
            int moved = Close(r, Done, "handed over in the talk", GainFor(r), what);
            SetReply(e, Loc(LineKey("scEncThanks", r.Kind, band)) + " " + Aside(moved));
        }

        /// <summary>"Take one of these.": the pick of the guns the player carries (its rows' texts made now).</summary>
        public static void ActGunList(EntityPlayer player)
        {
            Enclave e;
            EnclaveRequest r;
            if (!Acting(player, out e, out r) || r == null || r.Kind != EnclaveRequest.KindGun) { pick = null; return; }
            BuildPick(player, ScFavours.CurrentNpc(player), r);
        }

        /// <summary>A gun pick row: that gun taken from its exact slot - still there, the same item - and the request done (+its points).</summary>
        public static void ActGiveGun(EntityPlayer player, int row)
        {
            Enclave e;
            EnclaveRequest r;
            if (!Acting(player, out e, out r)) return;
            if (r == null || r.Kind != EnclaveRequest.KindGun) { SetReply(e, Loc("scEncReqGone")); return; }
            EntityAlive npc = ScFavours.CurrentNpc(player);
            GunPick p = pick;
            Gun g = p != null && npc != null && p.PlayerId == player.entityId && p.NpcId == npc.entityId && p.RequestId == r.Id && row >= 0 && row < p.Rows.Count ? p.Rows[row] : null;
            string band = ScEnclaves.StandingKey(e);
            if (g == null || !TakeOne((EntityPlayerLocal)player, g.Belt, g.Slot, g.Value)) { SetReply(e, Loc("scEncReqNotCarried")); return; }
            pick = null;
            ScLog.Out("enclave request #" + r.Id + ": " + g.Value.ItemClass.Name + " (T" + g.Tier + ", quality " + g.Quality + ") from the " + (g.Belt ? "toolbelt" : "bag") + " slot " + g.Slot
                      + " - " + g.Points + " point(s)");
            int moved = Close(r, Done, "a gun handed over in the talk", g.Points, string.Format(Loc("scEncReqGunWhat"), g.Name, g.Tier, g.Quality));
            SetReply(e, Loc(LineKey("scEncThanks", r.Kind, band)) + " " + Aside(moved));
        }

        /// <summary>"No, I can't help with that.": the offer declined - a failure, EnclaveLossDecline by band.</summary>
        public static void ActDecline(EntityPlayer player)
        {
            Enclave e;
            EnclaveRequest r;
            if (!Acting(player, out e, out r)) return;
            if (r == null || !r.IsOffered) { SetReply(e, Loc("scEncReqGone")); return; }
            string band = ScEnclaves.StandingKey(e);
            int moved = Close(r, Declined, "the player said no");
            SetReply(e, Loc(LineKey("scEncDeclined", r.Kind, band)) + " " + Aside(moved));
        }

        /// <summary>"Let me think about it.": nothing now (the talk closes); the leader's word, and how long the offer stands, as a tell after it.</summary>
        public static void ActThink(EntityPlayer player)
        {
            Enclave e;
            EnclaveRequest r;
            if (!Acting(player, out e, out r) || r == null || !r.IsOffered) return;
            EncPerson leader = ScEnclavePeople.LeaderOf(e);
            string line = Loc(LineKey("scEncThink", r.Kind, ScEnclaves.StandingKey(e)));
            ScCommunity.Tell(player, string.Format(Loc("scEncReqThinkTell"), leader != null ? leader.Name : e.Name, line, r.LapseDay), "");
            ScLog.Verbose("enclave request #" + r.Id + ": the player will think about it (the offer stands to the end of day " + r.LapseDay + ")");
        }

        /// <summary>"I can't do it.": the accepted request failed now - EnclaveLossFail by band.</summary>
        public static void ActCantDo(EntityPlayer player)
        {
            Enclave e;
            EnclaveRequest r;
            if (!Acting(player, out e, out r)) return;
            if (r == null || !r.IsAccepted) { SetReply(e, Loc("scEncReqGone")); return; }
            string band = ScEnclaves.StandingKey(e);
            int moved = Close(r, Failed, "the player gave it up");
            SetReply(e, Loc(LineKey("scEncCantDo", r.Kind, band)) + " " + Aside(moved));
        }

        // ------------------------------------------------------------------ console ----

        private static string Left(int lastDay)
        {
            int n = lastDay - Now + 1;
            return n <= 0 ? "due now" : n + " day(s) left";
        }

        private static string Line(EnclaveRequest r)
        {
            Enclave e = ScEnclaves.ById(r.EnclaveId);
            return "#" + r.Id + " " + (e != null ? e.Name + " (enclave " + e.Id + ", " + ScEnclaves.StandingKey(e) + ")" : "enclave " + r.EnclaveId + " (gone)") + ": "
                   + Describe(r) + " - " + WhatText(r) + " - "
                   + (r.IsOffered ? "offered on day " + r.OfferDay + ", stands to the end of day " + r.LapseDay + " (" + Left(r.LapseDay) + ")"
                                  : "accepted on day " + r.AcceptDay + ", due by the end of day " + r.DueDay + " (" + Left(r.DueDay) + "), journal quest "
                                    + (r.QuestCode != 0 ? r.QuestCode.ToString() : "none"));
        }

        /// <summary>sc enclave requests: every open one.</summary>
        public static void ListLines(List<string> into)
        {
            int today = ScCommunity.Today;
            into.Add(All.Count + " open request(s) of at most " + ScConfig.EnclaveRequestMaxOpen + "; the request clock is day " + Now
                     + (Now > today ? " (ahead of the world's day " + today + ": sc day)" : "") + "; a day's chance " + Pct(ScConfig.EnclaveRequestChanceNeutral) + "/"
                     + Pct(ScConfig.EnclaveRequestChanceFriendly) + "/" + Pct(ScConfig.EnclaveRequestChanceAllied) + " (neutral/friendly/allied), generators from day "
                     + ScConfig.EnclaveGeneratorFromDay);
            foreach (EnclaveRequest r in All) into.Add("  " + Line(r));
        }

        private static string Pct(float f) { return Mathf.RoundToInt(f * 100f) + "%"; }

        /// <summary>
        /// sc enclave request &lt;id&gt; [offer &lt;kind&gt; [resource] [size] [force] | accept | complete [points] | fail | lapse |
        /// clear] (testing): the enclave's request, or one made or moved now. offer ignores the chance and the cooldown and
        /// refuses at the EnclaveRequestMaxOpen cap without "force"; complete and fail apply the real outcome, standing and
        /// journal included; clear withdraws it and the cooldown with it.
        /// </summary>
        public static void Command(Enclave e, List<string> p, List<string> into)
        {
            EnclaveRequest r = Of(e);
            string verb = p.Count > 3 ? p[3].ToLowerInvariant() : "";
            EntityPlayerLocal pl = ScCommunity.LocalPlayer;
            switch (verb)
            {
                case "":
                {
                    into.Add(e.Name + " (enclave " + e.Id + ", " + ScEnclaves.StandingWord(e) + "): " + (r != null ? Line(r) : "no open request"));
                    string why = Eligible(e, Now);
                    into.Add("  the daily roll: " + (why ?? "eligible, " + Pct(ChanceFor(e)) + " a day") + (e.NextRequestDay > 0 ? "; cooldown to day " + e.NextRequestDay : "")
                             + "; " + All.Count + " of at most " + ScConfig.EnclaveRequestMaxOpen + " open");
                    return;
                }
                case "offer":
                {
                    if (r != null) { into.Add(e.Name + " has an open request already (#" + r.Id + "): clear it first"); return; }
                    if (e.IsHostile || e.Lost) { into.Add(e.Name + " makes no requests (" + (e.IsHostile ? "hostile" : "its building is not in this world") + ")"); return; }
                    if (!e.Known) { into.Add(e.Name + " is not found yet: sc enclave reveal " + e.Id); return; }
                    string kind = p.Count > 4 ? p[4].ToLowerInvariant() : "";
                    if (!KnownKind(kind)) { into.Add("sc enclave request " + e.Id + " offer <" + string.Join("|", Kinds) + "> [resource] [small|medium|large] [force]"); return; }
                    bool force = false;
                    int res = -1, units = 0;
                    for (int i = 5; i < p.Count; i++)
                    {
                        string a = p[i].ToLowerInvariant();
                        Resource rr;
                        int u = Array.IndexOf(Sizes, a) + 1;
                        if (a == "force") force = true;
                        else if (u > 0) units = u;
                        else if (int.TryParse(a, out u) && u >= 1 && u <= 3) units = u;
                        else if (ScNames.TryResource(a, out rr) && (int)rr < Asked) res = (int)rr;
                        else { into.Add("unknown argument '" + p[i] + "' (a resource: food|meds|ammo|mats|fuel; a size: small|medium|large; force)"); return; }
                    }
                    if (All.Count >= ScConfig.EnclaveRequestMaxOpen && !force) { into.Add(All.Count + " requests are open (EnclaveRequestMaxOpen " + ScConfig.EnclaveRequestMaxOpen + "): add force to go past it"); return; }
                    World w = ScCommunity.World;
                    GameRandom rnd = w != null ? w.GetGameRandom() : null;
                    r = Offer(e, kind, Now, rnd, res, units, pl, "the console");
                    into.Add("offered: " + Line(r));
                    return;
                }
            }
            if (r == null) { into.Add(e.Name + " has no open request (sc enclave request " + e.Id + " offer <kind>)"); return; }
            switch (verb)
            {
                case "accept":
                    if (!r.IsOffered) { into.Add("#" + r.Id + " is accepted already"); return; }
                    Accept(r, pl, "the console");
                    into.Add("accepted: " + Line(r));
                    return;
                case "complete":
                {
                    int gain = GainFor(r);
                    if (r.Kind == EnclaveRequest.KindGun)
                    {
                        int pts;
                        gain = p.Count > 4 && int.TryParse(p[4], out pts) ? Math.Max(0, pts) : GunPoints(1, 3);
                    }
                    int moved = Close(r, Done, "the console", gain);
                    into.Add("#" + r.Id + " done: standing " + (moved >= 0 ? "+" : "") + moved + " - " + e.Name + " " + e.Standing + " (" + ScEnclaves.StandingWord(e) + ")");
                    return;
                }
                case "fail":
                {
                    int moved = Close(r, r.IsOffered ? Declined : Failed, "the console");
                    into.Add("#" + r.Id + " " + (r.IsOffered ? "declined" : "failed") + ": standing " + moved + " - " + e.Name + " " + e.Standing + " (" + ScEnclaves.StandingWord(e) + ")");
                    return;
                }
                case "lapse":
                {
                    int moved = Close(r, r.IsOffered ? Lapsed : Expired, "the console");
                    into.Add("#" + r.Id + " " + (r.IsOffered ? "lapsed" : "expired") + ": standing " + moved + " - " + e.Name + " " + e.Standing + " (" + ScEnclaves.StandingWord(e) + ")");
                    return;
                }
                case "clear":
                    Close(r, Withdrawn, "the console");
                    e.NextRequestDay = 0;
                    ScRegistry.SaveAtomic();
                    into.Add("#" + r.Id + " withdrawn, no standing change; the cooldown is cleared too");
                    return;
                default:
                    into.Add("sc enclave request <id> [offer <kind> [resource] [size] [force] | accept | complete [points] | fail | lapse | clear]");
                    return;
            }
        }
    }
}
