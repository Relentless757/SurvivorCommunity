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
    /// the world's day, ahead of it only after "sc day"). E5 adds the missing survivor (ScEncSearch), the infestation
    /// (ScEncInfest) and the siege (ScEncSiege): their fields are at the end, saved when set.
    /// </summary>
    public sealed class EnclaveRequest : IScCommunityRecord      // round MP6 (core): a request belongs to one community
    {
        public const string KindResource = "resource", KindGenerator = "generator", KindGun = "gun";
        /// <summary>E5's kinds: a missing survivor and an infestation to clear (offered as E4's are), a siege (a call: never offered).</summary>
        public const string KindMissing = "missing", KindInfestation = "infestation", KindSiege = "siege";
        public const string StateOffered = "offered", StateAccepted = "accepted";

        public int Id;
        /// <summary>Round MP6 (core): the community it was offered to (stamped by ScRecordList; cid= in the save).</summary>
        public int CommunityId { get; set; }
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
        /// <summary>
        /// Round MP4: the platform id (ScPlayers.IdOf) of the player who accepted it - whose journal holds its quest
        /// (ScQuestNet). "" for a save from before MP4, and read as the host's own player. Saved as acceptedby= (never to a client).
        /// </summary>
        public string AcceptedBy = "";

        // ---- E5: a target POI, an infestation, a person, a world-time deadline ----
        public bool HasTarget;
        public Vector3i Target;
        public int InfestationId = -1;
        public int PersonId = -1;
        public ulong Deadline;
        /// <summary>
        /// Where an E5 request stands: a missing survivor (ScEncSearch.Stage*: not rolled yet, alive and waiting, following the
        /// player, dead - their remains -, the remains searched); an infestation (ScEncInfest: 0 its target live, 1 cleared
        /// while it was still an offer); a siege (ScEncSiege: 0 called, 1 live).
        /// </summary>
        public int Stage;
        /// <summary>E5: the target POI's name (the building searched, the infested one).</summary>
        public string TargetName = "";
        /// <summary>E5, a missing survivor: where they wait or lie (the remains) and the way they face there.</summary>
        public bool HasSpot;
        public Vector3i Spot;
        public float SpotYaw;
        /// <summary>E5, a missing survivor following the player: the player's entity id and the body's (a load ends it).</summary>
        public int EscortBy = -1, EscortBody = -1;
        /// <summary>E5, a siege: its size, how many came and died, and the world time it went live (0: not yet).</summary>
        public int SiegeSize, SiegeSpawned, SiegeKilled;
        public ulong SiegeStarted;

        public bool IsOffered { get { return State == StateOffered; } }
        public bool IsAccepted { get { return State == StateAccepted; } }
        /// <summary>One of E5's kinds.</summary>
        public bool IsE5 { get { return Kind == KindMissing || Kind == KindInfestation || Kind == KindSiege; } }
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
    /// is written. Every action checks everything again: the rows were built a moment earlier. vo2: the key of each line
    /// picked (FillTokens' voiceKey, the reply's Key) names its voice clip (ScVoice); the specifics after it are text only.
    ///
    /// The kinds: bundles of a resource from the bag and the toolbelt covering the units with the least overpay
    /// (EnclaveGainResource; ScStockpile.IsBundle's units, removed by type and measured before and after); one item whose
    /// block is a BlockGenerator (EnclaveGainGenerator); a gun or launcher - never a bow, crossbow or admin gun - taken from
    /// its exact slot, worth EnclaveGunTierPoints by tier x quality (Q11). Close is the one way out: the standing change
    /// for the band at that moment (EnclaveLoss*), the journal quest closed (completed, or removed as failed),
    /// SurvivorOrigins' favour event for one done (Q16 a), a ledger line, a tell when the band changed, the log, a save.
    /// E5 reuses it for its kinds: the rescue event for a survivor brought home and a siege beaten (Q16 a), the outcomes
    /// "held" (a siege that held without the player) and "lost" (a siege lost: the enclave falls), and each kind's own
    /// ending (ScEncSearch.OnClosed, ScEncSiege.OnClosed). Host only, main thread.
    ///
    /// E5 (E5_SPEC.md; Enclaves_Plan.md 2.10 kinds 4-6, Q12 a, Q13 a, Q16 a, Q17): the missing survivor (ScEncSearch) and the
    /// infestation to clear (ScEncInfest) join the daily roll (EnclaveRequestKindWeights, only where the enclave has someone
    /// who could go missing and a building to search, or an infestation near it), due EnclaveDueDaysSearch days after
    /// acceptance; the siege (ScEncSiege) has its own pace and is a call, never an offer.
    ///
    /// Round MP5 (part E): any community player talks to the leader. A client's rows read the talk's facts (ScEncFacts: the
    /// request's state, kind and stage, the lines' keys and specifics) and its own bag; its actions are ScActionsEnclave's ops,
    /// which run the same cores the host's actions do (AcceptAs, DeclineAs, CantDoAs, FoundAs, ClearedAs, ThinkAs) or their
    /// item twins (GiveFor, GunFor: the client takes first, a refusal gives back), and answer with the reply line's key and
    /// specifics. A request a client accepts is theirs (AcceptedBy: ScQuestNet sends them the quest). The records stay the
    /// community's.
    /// </summary>
    public static class ScEncRequests
    {
        public const string Done = "done", Declined = "declined", Failed = "failed", Expired = "expired", Lapsed = "lapsed", Withdrawn = "withdrawn";
        /// <summary>E5's siege outcomes: it held out without the player (no standing), it was lost (the enclave falls).</summary>
        public const string HeldOut = "held", Lost = "lost";
        /// <summary>Every kind this build keeps (E4's three, E5's three). A saved request of another kind is dropped at load.</summary>
        public static readonly string[] Kinds = { EnclaveRequest.KindResource, EnclaveRequest.KindGenerator, EnclaveRequest.KindGun,
                                                  EnclaveRequest.KindMissing, EnclaveRequest.KindInfestation, EnclaveRequest.KindSiege };
        /// <summary>The kinds the daily roll offers and EnclaveRequestKindWeights names (a siege has its own pace: ScEncSiege).</summary>
        public static readonly string[] RollKinds = { EnclaveRequest.KindResource, EnclaveRequest.KindGenerator, EnclaveRequest.KindGun,
                                                      EnclaveRequest.KindMissing, EnclaveRequest.KindInfestation };
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

        /// <summary>
        /// What the last request action left for the reply statement ({reqreply}): the key of its line (vo2: the name of
        /// its voice clip) and the whole text.
        /// </summary>
        private sealed class Reply
        {
            public int EnclaveId;
            public string Key = "";
            public string Text = "";
        }

        private struct Held
        {
            public bool Belt;
            public int Slot;
            public ItemStack Stack;
        }

        /// <summary>Bundles of one resource by size (units: Large 3, Medium 2, Small 1, the old one-size bundle 1). E7's gifts use it too.</summary>
        internal struct Bundles
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
        /// <summary>
        /// Round MP6 (part relations), session only: the world day whose daily last moved the request clock, and the request day it
        /// moved it to - the other communities' halves of the request pass run for that day (DailyCommunity), and only then.
        /// </summary>
        private static int clockRanFor = -1, clockRanDay = -1;

        public static List<EnclaveRequest> All { get { return ScRegistry.EncRequests; } }

        private static string Loc(string key) { return Localization.Get(key); }

        public static void Reset()
        {
            pick = null;
            reply = null;
            synced = false;
            syncTries = 0;
            clockSaid = false;
            clockRanFor = clockRanDay = -1;      // round MP6 (part relations)
            foundRowFor = -1;
            foundRowText = "";
            ScEncGifts.Reset();      // E7: the gift pick and the gift reply
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
            int max = 0;
            // round MP6 (core fix): every community's - the id counter is the world's, so a repaired id must clear them all (review C-m7)
            foreach (CommunityState cs in ScCommunities.All)
                foreach (EnclaveRequest r in cs.EncRequests) if (r.Id > max) max = r.Id;
            int dropped = 0;
            bool siege = false;
            // round MP6 (part relations): every community's requests, each in its own scope - one per (community, enclave), one
            // siege in the whole world (the first kept), ids unique across communities. With one community: community 0's, as before
            foreach (CommunityState rcs in ScCommunities.All)
            using (ScRegistry.Use(rcs.Id))
            {
                var enclaves = new HashSet<int>();
                foreach (EnclaveRequest r in new List<EnclaveRequest>(All))
                {
                    // E5: a request its kind cannot work without (a hand-edited save) and a second siege (one at a time) go too
                    string bad = E5Fault(r, ref siege);
                    if (!KnownKind(r.Kind) || (!r.IsOffered && !r.IsAccepted) || !enclaves.Add(r.EnclaveId) || bad != null)
                    {
                        if (bad != null) ScLog.Warn("enclave request #" + r.Id + ": " + bad);
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
            }
            if (dropped > 0) ScLog.Out("enclave requests: " + dropped + " dropped at load (see above)");
            ScEncSearch.OnRegistryChanged();     // E5: missing people squared with their requests, a following one waits again
            ScEncSiege.OnRegistryChanged();      // E5: a live siege resumes from its counts
        }

        /// <summary>Why an E5 request cannot be kept as it was saved (null: it can). One siege at a time: the first is kept.</summary>
        private static string E5Fault(EnclaveRequest r, ref bool siege)
        {
            switch (r.Kind)
            {
                case EnclaveRequest.KindMissing:
                    if (r.IsAccepted && (r.PersonId <= 0 || !r.HasTarget)) return "a missing survivor's request without its person or building";
                    return null;
                case EnclaveRequest.KindInfestation:
                    return r.InfestationId <= 0 || !r.HasTarget ? "an infestation request without its infestation" : null;
                case EnclaveRequest.KindSiege:
                    if (siege) return "a second siege (one at a time)";
                    siege = true;
                    if (r.IsOffered) r.State = EnclaveRequest.StateAccepted;     // a siege is a call: never an offer
                    return r.Deadline == 0 && r.Stage == 0 ? "a siege without its deadline" : null;
                default:
                    return null;
            }
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

        /// <summary>P2: the calls for help (offers and siege calls) made on the world's day today - EnclaveRequestMaxPerDay's count.</summary>
        public static int NewToday()
        {
            Community c = ScRegistry.Community;
            return c.EncOfferDay == ScCommunity.Today ? c.EncOffersToday : 0;
        }

        /// <summary>P2: today has had EnclaveRequestMaxPerDay calls for help - no new offer or siege call until the next world day.</summary>
        public static bool DayFull()
        {
            return NewToday() >= Math.Max(0, ScConfig.EnclaveRequestMaxPerDay);
        }

        /// <summary>P2: one more call for help on the world's day today (Offer, ScEncSiege.Call; the console's forced ones too).</summary>
        internal static void CountNew()
        {
            Community c = ScRegistry.Community;
            int today = ScCommunity.Today;
            if (c.EncOfferDay != today) { c.EncOfferDay = today; c.EncOffersToday = 0; }
            c.EncOffersToday++;
        }

        /// <summary>The open requests that count toward EnclaveRequestMaxOpen: every one but a siege (its own pace, ScEncSiege).</summary>
        public static int OpenCount()
        {
            int n = 0;
            foreach (EnclaveRequest r in All) if (r.Kind != EnclaveRequest.KindSiege) n++;
            return n;
        }

        /// <summary>
        /// E5: the POIs open requests send the player to - a missing survivor's building, an infestation to clear - for the other
        /// pickers' "in use" sets (radio contacts, supply runs, favours, scouting, the enclaves' and infestations' own rules).
        /// </summary>
        public static void AddTargets(ICollection<Vector3i> into)
        {
            foreach (EnclaveRequest r in All)
                if (r.HasTarget && (r.Kind == EnclaveRequest.KindMissing || r.Kind == EnclaveRequest.KindInfestation)) into.Add(r.Target);
        }

        /// <summary>E5: is this POI an open request's target (AddTargets)? A short list scan.</summary>
        public static bool IsTarget(Vector3i key)
        {
            List<EnclaveRequest> all = All;
            for (int i = 0; i < all.Count; i++)
            {
                EnclaveRequest r = all[i];
                if (r.HasTarget && r.Target == key && (r.Kind == EnclaveRequest.KindMissing || r.Kind == EnclaveRequest.KindInfestation)) return true;
            }
            return false;
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
        internal static string LineKey(string stem, string kind, string band)
        {
            string k = stem + "_" + kind + "_" + band;
            if (Localization.Exists(k)) return k;
            k = stem + "_" + kind;
            if (Localization.Exists(k)) return k;
            k = stem + "_" + band;
            return Localization.Exists(k) ? k : stem;
        }

        /// <summary>
        /// "a medium bundle of medicine", "a generator bank", "a gun": what the request asks for; E5's "word of Mara", "clearing
        /// the Shotgun Messiah factory", "help against a siege".
        /// </summary>
        public static string WhatText(EnclaveRequest r)
        {
            if (r == null) return "";
            switch (r.Kind)
            {
                case EnclaveRequest.KindResource: return string.Format(Loc("scEncReqWhatResource"), SizeWord(r.Units), Loc("scEncReqRes" + r.Res));
                case EnclaveRequest.KindGenerator: return Loc("scEncReqWhatGenerator");
                case EnclaveRequest.KindGun: return Loc("scEncReqWhatGun");
                case EnclaveRequest.KindMissing: return string.Format(Loc("scEncReqWhatMissing"), ScEncSearch.PersonName(r));
                case EnclaveRequest.KindInfestation: return string.Format(Loc("scEncReqWhatInfest"), r.TargetName);
                case EnclaveRequest.KindSiege: return Loc("scEncReqWhatSiege");
                default: return r.Kind;
            }
        }

        internal static string SizeWord(int units)
        {
            return Loc(units >= 3 ? "scBundleSizeLarge" : units == 2 ? "scBundleSizeMedium" : "scBundleSizeSmall");
        }

        /// <summary>The resource a kind of group lives on (they never ask for it; E7's gift rows list it after the others), or -1 for one that lives on anything.</summary>
        internal static int OwnResource(string kind)
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
        public static void Daily(int day, EntityPlayer player)         // round MP0: the ticker's anchor - any server
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
                clockRanFor = day;                // round MP6 (part relations): the other communities' halves pass for this day
                clockRanDay = d;
                Pass(d, player);
            }
            catch (Exception e)
            {
                ScLog.Once("encreq-daily", e);
            }
        }

        /// <summary>
        /// Round MP6 (part relations), ScRelations.DailyCommunity: the request pass for a community whose rollover did not run the
        /// world daily (the request clock moved in another community's) - its own requests lapse, expire or are withdrawn, then its
        /// day's roll and siege roll, for the request day the clock moved to. Nothing when the clock did not move for this day.
        /// </summary>
        internal static void DailyCommunity(int day, EntityPlayer player)
        {
            try
            {
                if (!ScEnclaves.Ready || !ScConfig.Enclaves || !ScRegistry.Loaded) return;
                // the clock moved for this day in this session; or (a restart since it moved) it stands at today exactly
                if (clockRanFor == day && clockRanDay >= 0) Pass(clockRanDay, player);
                else if (clockRanFor < 0 && ScRegistry.WorldState.EncRequestDay == day) Pass(day, player);
            }
            catch (Exception e)
            {
                ScLog.Once("encreq-daily-community", e);
            }
        }

        /// <summary>Round MP6 (part relations): the community half of the request pass (Daily's, as it always ran), for request day d.</summary>
        private static void Pass(int d, EntityPlayer player)
        {
            {
                foreach (EnclaveRequest r in All.ToArray())
                {
                    if (!All.Contains(r)) continue;      // closed meanwhile by another's ending
                    Enclave e = ScEnclaves.ById(r.EnclaveId);
                    if (e == null || e.IsHostile || e.Lost)
                    {
                        Close(r, Withdrawn, e == null ? "the enclave is gone" : e.Lost ? "their building is not in this world" : "they are hostile");
                        continue;
                    }
                    // E5: a siege's clock is the ticker's (ScEncSiege); an infestation that ended unseen is squared first
                    if (r.Kind == EnclaveRequest.KindSiege) continue;
                    if (r.Kind == EnclaveRequest.KindInfestation && ScEncInfest.Squared(r, d)) continue;
                    if (r.IsOffered && d > r.LapseDay) Close(r, Lapsed, "unanswered past day " + r.LapseDay);
                    else if (r.IsAccepted && d > r.DueDay)
                    {
                        // E5: one found alive and on the way home with the player does not run out on the road
                        if (r.Kind == EnclaveRequest.KindMissing && r.Stage == ScEncSearch.StageFollowing) continue;
                        Close(r, Expired, "not delivered by day " + r.DueDay);
                    }
                }
                if (d >= ScCommunity.Today)
                {
                    Roll(d, player);
                    ScEncSiege.Daily(d, player);         // E5: the day's siege roll and its hour
                }
            }
        }

        /// <summary>
        /// The world's day went back (an older save, a time command - never "sc day", whose own end is no jump), through the
        /// one hook ScTicker calls (ScEnclaves.OnWorldDayBack, E6's R2: it logs). The request clock follows it to the world's
        /// day and every open request and enclave cooldown keeps its remaining span - shifted back by the request clock's drop
        /// (back: from where the clock stood, the world's last day at the least), so lapses, expiries and rolls go on from the
        /// world's day instead of waiting for it to catch up (the E4 review's M1). Returns how many open requests moved.
        /// </summary>
        internal static int ShiftDays(int day, int back)
        {
            if (back <= 0) return 0;
            Community c = ScRegistry.Community;
            if (c.EncRequestDay > day) c.EncRequestDay = day;
            // round MP6 (part relations): every community's open requests and every community's cooldown (the world's clock went back)
            int n = 0;
            foreach (CommunityState cs in ScCommunities.All)
                foreach (EnclaveRequest r in cs.EncRequests)
                {
                    // never below 0 (E6's R2): -1 means "not yet" for the acceptance and the due day
                    r.OfferDay = Math.Max(0, r.OfferDay - back);
                    r.LapseDay = Math.Max(0, r.LapseDay - back);
                    if (r.AcceptDay >= 0) r.AcceptDay = Math.Max(0, r.AcceptDay - back);
                    if (r.DueDay >= 0) r.DueDay = Math.Max(0, r.DueDay - back);
                    n++;
                }
            foreach (Enclave e in ScEnclaves.All)
                for (int i = 0; i < e.Rels.Count; i++)
                {
                    EncRel row = e.Rels.At(i);
                    if (row.NextRequestDay > 0) row.NextRequestDay = Math.Max(0, row.NextRequestDay - back);
                }
            clockSaid = false;
            try
            {
                EntityPlayerLocal pl = ScCommunity.LocalPlayer;
                foreach (CommunityState cs in ScCommunities.All)
                    foreach (EnclaveRequest r in cs.EncRequests) if (r.IsAccepted) SyncDue(QuestOf(pl, r), r);
            }
            catch (Exception ex)
            {
                ScLog.Once("encreq-dayback", ex);      // the journal's due days only: the records moved already
            }
            return n;
        }

        /// <summary>
        /// The day's new offers: every eligible enclave in a shuffled order (a day's own seed), each rolling the chance for its
        /// band, until EnclaveRequestMaxOpen are open across the map (a siege is not counted: E5) or the world's day has had
        /// EnclaveRequestMaxPerDay calls for help (P2: offers and siege calls together). The kind is one this enclave can be given
        /// (E5: someone who could go missing and a building to search; an infestation near it). P2: one log line per roll.
        /// </summary>
        private static void Roll(int day, EntityPlayer player)         // round MP0: any player
        {
            int max = Math.Max(0, ScConfig.EnclaveRequestMaxOpen);
            var cands = new List<Enclave>();
            foreach (Enclave e in ScEnclaves.All)
                if (Eligible(e, day) == null) cands.Add(e);
            ScLog.Out("enclave requests: request day " + day + "'s roll (world day " + ScCommunity.Today + ") - " + OpenCount() + " open of " + max + ", "
                      + NewToday() + " new today of " + ScConfig.EnclaveRequestMaxPerDay + ", " + cands.Count + " enclave(s) could ask");
            if (OpenCount() >= max || DayFull()) return;
            if (cands.Count == 0) return;
            World w = ScCommunity.World;
            // round MP6 (part relations): each community its own draw (community 0's: the old seed)
            GameRandom rnd = GameRandomManager.Instance.CreateGameRandom((w != null ? w.Seed : 0) ^ Salt.GetStableHashCode() ^ unchecked(day * 19349663)
                                                                         ^ unchecked(ScRegistry.CurrentId * 7368787));
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
                    if (OpenCount() >= max || DayFull()) break;
                    float chance = ChanceFor(e);
                    if (rnd.RandomFloat >= chance) continue;
                    string kind = PickKind(e, rnd, day);
                    if (kind == null) continue;
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

        /// <summary>
        /// A kind by EnclaveRequestKindWeights among the ones this enclave can be given (CanOffer); generators only from day
        /// EnclaveGeneratorFromDay. Null when none weighs anything. (E4 picked among every kind: E5's two need something the
        /// enclave may not have, so the kinds it cannot take are left out of the draw rather than drawn and dropped.)
        /// </summary>
        private static string PickKind(Enclave e, GameRandom rnd, int day)
        {
            var ok = new List<KeyValuePair<string, float>>();
            float total = 0f;
            foreach (string k in RollKinds)
            {
                float wt = KindWeight(k, day);
                if (wt <= 0f || CanOffer(e, k) != null) continue;
                ok.Add(new KeyValuePair<string, float>(k, wt));
                total += wt;
            }
            if (total <= 0f) return null;
            float x = rnd.RandomFloat * total;
            string last = null;
            foreach (var kv in ok)
            {
                last = kv.Key;
                if (x < kv.Value) return kv.Key;
                x -= kv.Value;
            }
            return last;
        }

        private static float KindWeight(string kind, int day)
        {
            if (kind == EnclaveRequest.KindGenerator && day < ScConfig.EnclaveGeneratorFromDay) return 0f;
            if (kind == EnclaveRequest.KindInfestation && !ScConfig.Infestations) return 0f;     // E5: I1 switched off
            float wt;
            return ScConfig.EnclaveRequestKindWeights.TryGetValue(kind, out wt) ? Math.Max(0f, wt) : 0f;
        }

        /// <summary>
        /// Why this enclave cannot be given a request of this kind now (null when it can): E4's three always can; E5's missing
        /// survivor needs someone at home besides the leader and a building to search (ScEncSearch), the infestation one near
        /// them or room to start one (ScEncInfest). The siege is never offered.
        /// </summary>
        public static string CanOffer(Enclave e, string kind)
        {
            switch (kind)
            {
                case EnclaveRequest.KindMissing: return ScEncSearch.CanOffer(e);
                case EnclaveRequest.KindInfestation: return ScEncInfest.CanOffer(e);
                case EnclaveRequest.KindSiege: return "a siege is a call, never an offer (sc enclave siege)";
                default: return null;
            }
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
        /// person - the log and a save. E5: an infestation request names its target now (one near them, or one started for it:
        /// ScEncInfest.PickTarget) - null when there is none; a missing survivor's person and building are picked at acceptance.
        /// </summary>
        public static EnclaveRequest Offer(Enclave e, string kind, int day, GameRandom rnd, int res, int units, EntityPlayer player, string why)   // round MP0: any player
        {
            Infestation target = null;
            if (kind == EnclaveRequest.KindInfestation)
            {
                string no;
                target = ScEncInfest.PickTarget(e, player, out no);
                if (target == null)
                {
                    ScLog.Out("enclave " + e.Id + " '" + e.Name + "': no infestation request after all (" + no + ")");
                    return null;
                }
            }
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
            if (target != null) ScEncInfest.Name(r, target);
            e.Thanks = "";        // E5: a thanks still owed for an earlier request is overtaken by the new one
            All.Add(r);
            CountNew();           // P2: EnclaveRequestMaxPerDay
            bool heard = ScRadio.Online();
            if (heard)
            {
                // round MP4 (part: tells): this file's tells go by localization key (ScCommunity.TellKey / ScPlayers.TellCommunityKey), so each player reads them in their own language
                ScPlayers.TellCommunityKey("scEncReqHeard", "", e.Name);      // round MP0: the community's radio news
                if (player != null) ScEncRadio.Say(e, EncCall.Offer, player, kind);   // round m1v: the leader's voice (text only until round V-b records it)
                ScOutposts.NoteLedger(string.Format(Loc("scEncReqLedgerAsk"), e.Name));
            }
            ScLog.Out("enclave " + e.Id + " '" + e.Name + "': request #" + r.Id + " offered on day " + day + " - " + Describe(r) + ", the offer stands to the end of day "
                      + r.LapseDay + " (" + why + "; " + (heard ? "announced on the radio" : "no radio: the leader asks in person") + ")");
            ScRegistry.SaveAtomic();
            return r;
        }

        /// <summary>"resource: 2 meds (medium)" - the log's and the console's short form (E5: "missing: p12 at X (alive)", ...).</summary>
        public static string Describe(EnclaveRequest r)
        {
            if (r == null) return "-";
            switch (r.Kind)
            {
                case EnclaveRequest.KindResource: return r.Kind + ": " + r.Units + " " + ScNames.Key(r.Res) + " (" + Sizes[Mathf.Clamp(r.Units, 1, 3) - 1] + ")";
                case EnclaveRequest.KindMissing: return ScEncSearch.Describe(r);
                case EnclaveRequest.KindInfestation: return ScEncInfest.Describe(r);
                case EnclaveRequest.KindSiege: return ScEncSiege.Describe(r);
                default: return r.Kind;
            }
        }

        // ------------------------------------------------------------------ accepting and closing ----

        /// <summary>
        /// "Leave it with me." (and "sc enclave request accept"): due by the end of day now + EnclaveDueDaysItems - 1; the journal
        /// quest (quest_scEncRequest: the title "Help &lt;enclave&gt;", what and by when, the marker on their building) is
        /// added for the player. The record is the authority: without a journal (no player, the quest class missing) it is
        /// accepted all the same. False when it is not an open offer. E5: a missing survivor's person goes missing now and
        /// their building is picked (ScEncSearch.Prepare; withdrawn when that cannot be), an infestation's target becomes
        /// known; both are due EnclaveDueDaysSearch days on, in their own journal quests (ScQuests.StartRequest).
        /// </summary>
        public static bool Accept(EnclaveRequest r, EntityPlayer player, string why)     // round MP5 (part E): any player (was EntityPlayerLocal) - the quest is theirs
        {
            Enclave e = r != null ? ScEnclaves.ById(r.EnclaveId) : null;
            if (e == null || !r.IsOffered || !All.Contains(r)) return false;
            int now = Now;
            if (r.Kind == EnclaveRequest.KindMissing)
            {
                // E5: the person goes missing now, and the building they were last seen at is picked
                string no;
                if (!ScEncSearch.Prepare(r, e, out no))
                {
                    Close(r, Withdrawn, "it cannot be taken on: " + no);
                    return false;
                }
            }
            r.State = EnclaveRequest.StateAccepted;
            r.AcceptDay = now;
            int due = r.Kind == EnclaveRequest.KindMissing || r.Kind == EnclaveRequest.KindInfestation ? ScConfig.EnclaveDueDaysSearch : ScConfig.EnclaveDueDaysItems;
            r.DueDay = now + Math.Max(1, due) - 1;
            if (r.Kind == EnclaveRequest.KindInfestation) ScEncInfest.MakeKnown(r, player);     // E5: the leader told the player where
            string error = null;
            // round MP4: the quest is the accepting player's (AcceptedBy) - the host's own journal as before, a client's through the
            // quest op (ScQuestNet.StartRequestFor). Round MP5 (part E): a client accepts in its own talk (enc.req), so that is theirs
            // review m2: nothing for the host's own (empty reads as the host's) - a save loaded under another platform id keeps its quest
            r.AcceptedBy = player is EntityPlayerLocal ? "" : ScPlayers.IdOf(player);
            Quest q = player != null ? ScQuestNet.StartRequestFor(player, e, r, WhatText(r), out error) : null;
            r.QuestCode = q != null ? q.QuestCode : 0;
            if (q == null && error != ScQuestNet.SentToClient) ScLog.Warn("enclave request #" + r.Id + ": no journal quest (" + (error ?? "no player") + ") - accepted all the same");
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
        /// E5: ev - the Leadership event for one done (null: favour; the rescue event for a survivor brought home and a siege
        /// beaten, Q16 a); "held" (a siege held without the player: no standing, the quest completed) and "lost" (a siege lost:
        /// no standing - the enclave falls next) are outcomes too; each kind's own ending follows (KindClosed).
        /// Returns the standing it moved.
        /// </summary>
        public static int Close(EnclaveRequest r, string outcome, string reason, int gain = 0, string what = null, string ev = null)
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
            if (pl != null) CloseQuest(pl, r, outcome == Done || outcome == HeldOut);
            ScQuestNet.CloseRequestRemote(r, outcome == Done || outcome == HeldOut);     // round MP4: a client acceptor's own journal (the quest op)
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
                // round MP0: news for the whole community (the host alone in single player)
                if (outcome == Lapsed) ScPlayers.TellCommunityKey("scEncReqLapsedTell", "", e.Name);
                else if (outcome == Expired) ScPlayers.TellCommunityKey("scEncReqExpiredTell", "ui_denied", e.Name);
                if (bandChanged && !e.IsHostile) ScPlayers.TellCommunityKey("scEncReqBandTell", "", e.Name, ScCommunity.LocArg(ScEnclaves.StandingWordKey(e)));   // review tells m4
            }
            if (outcome == Done) ScLeadership.Count(ev ?? ScLeadership.FavourEvent);
            KindClosed(r, outcome);
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

        /// <summary>
        /// Round MP6 (part relations): every community's open request of this enclave withdrawn, each in its community's scope (the
        /// group fell, joined someone, lost its building). With one community: Withdraw.
        /// </summary>
        public static void WithdrawEverywhere(Enclave e, string why)
        {
            if (e == null) return;
            foreach (CommunityState cs in ScCommunities.All)
                using (ScRegistry.Use(cs.Id)) Withdraw(e, why);
        }

        /// <summary>Round MP6 (part relations): this enclave's open request in any community (the lowest id's), or null.</summary>
        public static EnclaveRequest OfAnywhere(Enclave e)
        {
            if (e == null) return null;
            foreach (CommunityState cs in ScCommunities.All)
                foreach (EnclaveRequest r in cs.EncRequests) if (r.EnclaveId == e.Id) return r;
            return null;
        }

        /// <summary>E5: each kind's own ending, after the record left the list (a missing survivor's person and remains, a siege's attackers).</summary>
        private static void KindClosed(EnclaveRequest r, string outcome)
        {
            try
            {
                if (r.Kind == EnclaveRequest.KindMissing) ScEncSearch.OnClosed(r, outcome);
                else if (r.Kind == EnclaveRequest.KindSiege) ScEncSiege.OnClosed(r, outcome);
            }
            catch (Exception ex)
            {
                ScLog.Once("encreq-kindclosed", ex);
            }
        }

        private static string LedgerLine(Enclave e, EnclaveRequest r, string outcome, int moved, bool bandChanged, string what)
        {
            string delta = moved == 0 ? Loc("scEncReqLedgerNoChange")
                         : bandChanged ? string.Format(Loc("scEncReqLedgerDeltaBand"), (moved > 0 ? "+" : "") + moved, ScEnclaves.StandingWord(e))
                         : string.Format(Loc("scEncReqLedgerDelta"), (moved > 0 ? "+" : "") + moved);
            // E5's own words for a request done, a siege held or lost
            switch (r.Kind + ":" + outcome)
            {
                case EnclaveRequest.KindMissing + ":" + Done:
                    return string.Format(Loc(r.Stage == ScEncSearch.StageSearched ? "scEncReqLedgerFound" : "scEncReqLedgerHome"), e.Name, what ?? ScEncSearch.PersonName(r), delta);
                case EnclaveRequest.KindMissing + ":" + Failed:
                {
                    // one who died following the player home (a give-up leaves them missing until OnClosed)
                    EncPerson p = ScEncSearch.PersonById(r.PersonId);
                    if (p != null && p.State == EncPerson.StateDead) return string.Format(Loc("scEncReqLedgerDiedHome"), e.Name, p.Name, delta);
                    break;
                }
                case EnclaveRequest.KindInfestation + ":" + Done: return string.Format(Loc("scEncReqLedgerCleared"), e.Name, what ?? r.TargetName, delta);
                case EnclaveRequest.KindSiege + ":" + Done: return string.Format(Loc("scEncReqLedgerSiegeWon"), e.Name, delta);
                case EnclaveRequest.KindSiege + ":" + HeldOut: return string.Format(Loc("scEncReqLedgerSiegeHeld"), e.Name);
                case EnclaveRequest.KindSiege + ":" + Lost: return string.Format(Loc("scEncReqLedgerSiegeLost"), e.Name);
            }
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

        /// <summary>The gain for a request done of a kind (a gun's is its own: GunPoints; E5's from EnclaveGainMissing*, ...Infestation, ...Siege).</summary>
        internal static int GainFor(EnclaveRequest r)
        {
            switch (r.Kind)
            {
                case EnclaveRequest.KindGenerator: return ScConfig.EnclaveGainGenerator;
                case EnclaveRequest.KindMissing: return r.Stage == ScEncSearch.StageSearched ? ScConfig.EnclaveGainMissingDead : ScConfig.EnclaveGainMissingAlive;
                case EnclaveRequest.KindInfestation: return ScConfig.EnclaveGainInfestation;
                case EnclaveRequest.KindSiege: return ScConfig.EnclaveGainSiege;
                default: return ScConfig.EnclaveGainResource;
            }
        }

        /// <summary>E5: the Leadership event for a request done (Q16 a): rescue for a survivor brought home alive and a siege beaten, else favour.</summary>
        internal static string EventFor(EnclaveRequest r)
        {
            bool rescue = r.Kind == EnclaveRequest.KindSiege || (r.Kind == EnclaveRequest.KindMissing && r.Stage != ScEncSearch.StageSearched);
            return rescue ? ScLeadership.RescueEvent : ScLeadership.FavourEvent;
        }

        // ------------------------------------------------------------------ the journal mirror ----

        /// <summary>
        /// The request's journal quest in progress: by the request id it carries, else by its code - two quests added in one
        /// frame can share a vanilla code (the E4 review's L1).
        /// </summary>
        internal static Quest QuestOf(EntityPlayer player, EnclaveRequest r)
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

        /// <summary>
        /// E5: where a request's journal quest points - E4's kinds and a siege at the enclave's building; a missing survivor's
        /// at the building searched (or, once they wait somewhere else, that spot) until they follow the player or their
        /// remains were searched, then at the enclave; an infestation's at the infested building.
        /// </summary>
        public static void RequestPlace(EnclaveRequest r, Enclave e, out Vector3i at, out Vector3i size, out string name)
        {
            at = e.Poi;
            size = e.Size;
            name = e.PoiName;
            if (r == null) return;
            if (r.Kind == EnclaveRequest.KindMissing) ScEncSearch.Place(r, e, ref at, ref size, ref name);
            else if (r.Kind == EnclaveRequest.KindInfestation) ScEncInfest.Place(r, ref at, ref size, ref name);
        }

        /// <summary>
        /// E5's stage word for the journal and the dialog (ScEncReqStage): a missing survivor "find", "follow", "return"; an
        /// infestation "clear", or "cleared" once it was cleared while still an offer; a siege "called" or "live". "" for E4's.
        /// </summary>
        public static string StageWord(EnclaveRequest r)
        {
            if (r == null) return "";
            switch (r.Kind)
            {
                case EnclaveRequest.KindMissing: return r.Stage == ScEncSearch.StageFollowing ? "follow" : r.Stage == ScEncSearch.StageSearched ? "return" : "find";
                case EnclaveRequest.KindInfestation: return r.Stage == ScEncInfest.StageCleared ? "cleared" : "clear";
                case EnclaveRequest.KindSiege: return r.Stage == ScEncSiege.StageLive ? "live" : "called";
                default: return "";
            }
        }

        /// <summary>E5: the request's journal quest made to match the record after a stage change (ScQuests.ShapeRequest), when there is one.</summary>
        internal static void Reshape(EnclaveRequest r)
        {
            EntityPlayerLocal pl = ScCommunity.LocalPlayer;
            Enclave e = r != null ? ScEnclaves.ById(r.EnclaveId) : null;
            Quest q = pl != null && e != null ? QuestOf(pl, r) : null;
            if (q != null) ScQuests.ShapeRequest(q, r, e);
            ScQuestNet.ShapeRequestRemote(r);         // round MP4: a client acceptor's own journal (the quest op)
        }

        /// <summary>
        /// ScTicker, every second: the once-a-session journal pass, as soon as the player's journal is there; then E5's live
        /// parts - a missing survivor's search (the fate, the body, the remains: ScEncSearch) and the siege (the call, the clock,
        /// the fight: ScEncSiege).
        /// </summary>
        public static void Tick(World w, EntityPlayer player)          // round MP0: the ticker's anchor - any server
        {
            try
            {
                // done, or tried a few seconds running while the player's record was not there to add a quest to.
                // Round MP0: the host's own journal only. Round MP4: a client's journal is squared with the records on its own
                // reports (ScQuestNet.OnReport), each request with the player who accepted it
                EntityPlayerLocal lp = player as EntityPlayerLocal;
                // round MP6 (part relations): the host's journal against its own community's requests (its pass: the anchor is the
                // host's own player only in its own community's pass - ScPlayers.Anchor)
                if (!synced && ScEnclaves.Ready && lp != null && lp.QuestJournal != null && ScRegistry.Loaded)
                    using (ScRegistry.Use(lp)) synced = SyncJournal(lp) || ++syncTries >= MaxSyncTries;
            }
            catch (Exception e)
            {
                synced = true;
                ScLog.Once("encreq-tick", e);
            }
            ScEncSearch.Tick(w, player);
            ScEncSiege.Tick(w, player);
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
                // E5: a quest of another kind's class than its request's (a hand-edited save) is made again as its own
                bool ownClass = r != null && string.Equals(q.ID, ScQuests.RequestQuestIdFor(r), StringComparison.OrdinalIgnoreCase);
                // round MP4: one another player accepted is theirs (AcceptedBy; ScQuestNet) - never the host's journal's
                if (r == null || !r.IsAccepted || e == null || !ownClass || !ScQuestNet.HostAccepted(r) || !mirrored.Add(r.Id)) { gone.Add(q); continue; }
                kept++;
                if (r.QuestCode != q.QuestCode) { r.QuestCode = q.QuestCode; changed = true; }
                if (r.IsE5) ScQuests.ShapeRequest(q, r, e);         // E5: its place, stage, phase and due day as the record has them
                else
                {
                    ScQuests.FollowRequestPoi(q, e);
                    SyncDue(q, r);                                  // the journal's due day follows the record (the E4 re-check's R1)
                }
            }
            foreach (Quest q in gone) ScQuests.CloseRequest(player, q, false);
            if (ScConfig.Enclaves)
            {
                foreach (EnclaveRequest r in All)
                {
                    if (!r.IsAccepted || mirrored.Contains(r.Id)) continue;
                    if (!ScQuestNet.HostAccepted(r)) continue;            // round MP4: another player's - added again on their own report
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
#if SC_V34
            ItemStack[] bag = pl.bag != null ? pl.bag.ItemGrid.items : null;      // 3.4: the bag's grid (read only)
#else
            ItemStack[] bag = pl.bag != null ? pl.bag.GetSlots() : null;
#endif
            if (bag != null)
                for (int i = 0; i < bag.Length; i++)
                    if (bag[i] != null && !bag[i].IsEmpty() && bag[i].itemValue != null && bag[i].itemValue.ItemClass != null)
                        list.Add(new Held { Belt = false, Slot = i, Stack = bag[i] });
            Inventory inv = pl.inventory;
            if (inv != null)
            {
#if SC_V34
                int n = Math.Min(inv.SlotCount, inv.Length);          // 3.4: the toolbelt has no hidden extra slot any more
#else
                int n = Math.Min(inv.PUBLIC_SLOTS, inv.GetSlotCount());
#endif
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
#if SC_V34
            // 3.4: EqualsExceptUseTimesAndAmmo is gone; EqualsExceptUseTimesAndMeta ignores Meta for every item, where the old
            // test ignored it only for a ranged weapon (its rounds) - so Meta is still compared for everything else
            if (s == null || s.IsEmpty() || s.itemValue == null || expect == null || s.itemValue.type != expect.type) return false;
            ItemClass ic = s.itemValue.ItemClass;
            bool ranged = ic != null && ic.Actions != null && ic.Actions.Length != 0 && ic.Actions[0] is ItemActionRanged;
            return s.itemValue.EqualsExceptUseTimesAndMeta(expect) && (ranged || s.itemValue.Meta == expect.Meta);
#else
            return s != null && !s.IsEmpty() && s.itemValue != null && expect != null && s.itemValue.type == expect.type && s.itemValue.EqualsExceptUseTimesAndAmmo(expect);
#endif
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
#if SC_V34
                if (inv == null || slot < 0 || slot >= Math.Min(inv.SlotCount, inv.Length)) return false;
#else
                if (inv == null || slot < 0 || slot >= Math.Min(inv.PUBLIC_SLOTS, inv.GetSlotCount())) return false;
#endif
                ItemStack s = inv.GetItem(slot);
                if (!Same(s, expect)) return false;
                int count = s.count;
                if (count > 1) inv.SetItem(slot, s.itemValue.Clone(), count - 1);
                else inv.SetItem(slot, ItemStack.Empty.Clone());
                ItemStack after = inv.GetItem(slot);
                return count > 1 ? after != null && after.count == count - 1 : after == null || after.IsEmpty();
            }
            Bag bag = pl.bag;
#if SC_V34
            // 3.4: the grid's slots; SetSlot always raises the slot's change event now (no notify flag)
            ItemStack[] items = bag != null ? bag.ItemGrid.items : null;
            if (items == null || slot < 0 || slot >= items.Length) return false;
            ItemStack b = items[slot];
            if (!Same(b, expect)) return false;
            int n = b.count;
            bag.SetSlot(slot, n > 1 ? new ItemStack(b.itemValue.Clone(), n - 1) : ItemStack.Empty.Clone());
            ItemStack[] now = bag.ItemGrid.items;
#else
            ItemStack[] items = bag != null ? bag.GetSlots() : null;
            if (items == null || slot < 0 || slot >= items.Length) return false;
            ItemStack b = items[slot];
            if (!Same(b, expect)) return false;
            int n = b.count;
            bag.SetSlot(slot, n > 1 ? new ItemStack(b.itemValue.Clone(), n - 1) : ItemStack.Empty.Clone(), true);
            ItemStack[] now = bag.GetSlots();
#endif
            ItemStack a = now != null && slot < now.Length ? now[slot] : null;
            return n > 1 ? a != null && a.count == n - 1 : a == null || a.IsEmpty();
        }

        private static XUiM_PlayerInventory InventoryOf(EntityPlayerLocal pl)
        {
            return pl != null && pl.PlayerUI != null && pl.PlayerUI.xui != null ? pl.PlayerUI.xui.PlayerInventory : null;
        }

        // ---- a resource request: bundles ----

        /// <summary>The bundles of a resource the player carries, by size (E7: the gift rows).</summary>
        internal static Bundles Carried(EntityPlayerLocal pl, Resource res)
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
        /// and nothing counts. E7: a gift's one bundle.
        /// </summary>
        internal static bool TakeBundles(EntityPlayerLocal pl, Resource res, Bundles take)
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

        /// <summary>
        /// Round MP5 (part E): LeaderTalking for a given body (a remote player's request names it: the server's copy of their
        /// CurrentNPC cvar can lag) - the leader of that body, alive and at home, their enclave not hostile and on the map, and
        /// its open request (null when none). False for anyone else. Server.
        /// </summary>
        internal static bool LeaderAt(EntityAlive npc, out Enclave e, out EnclaveRequest r)
        {
            e = null;
            r = null;
            EncPerson p = ScEnclavePeople.PersonOf(npc);
            if (p == null || !p.Alive || !p.IsLeader || p.State != EncPerson.StateHome) return false;
            e = ScEnclavePeople.EnclaveOf(p);
            if (e == null || e.IsHostile || e.Lost) return false;
            r = Of(e);
            return true;
        }

        /// <summary>The request's state for the dialog: "none", "offered" or "accepted".</summary>
        public static string StateWord(EnclaveRequest r) { return r == null ? "none" : r.State; }

        /// <summary>
        /// E5: "I can't do it." shows for an accepted request the player can give up - every E4 kind, an infestation, a search
        /// not at its end (never while the person follows the player, nor once their remains were found: then it is "I found
        /// ..."); never a siege (E6's radio answers a siege call).
        /// </summary>
        public static bool CanGiveUp(EnclaveRequest r)
        {
            if (r == null || !r.IsAccepted) return false;
            switch (r.Kind)
            {
                case EnclaveRequest.KindMissing: return r.Stage != ScEncSearch.StageFollowing && r.Stage != ScEncSearch.StageSearched;
                case EnclaveRequest.KindSiege: return false;
                default: return true;
            }
        }

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

        // ---- round MP5 (part E): a client's half - its own bag, the facts' request ----

        /// <summary>Round MP5 (part E), client: PlayerHas for the facts' request (kind, resource, units), from this machine's own bag.</summary>
        internal static bool ClientHas(EntityPlayerLocal pl, string kind, int res, int units)
        {
            if (pl == null) return false;
            switch (kind)
            {
                case EnclaveRequest.KindResource: return Carried(pl, ClampRes(res)).Units >= Math.Max(1, units);
                case EnclaveRequest.KindGenerator: { Held h; return FindGenerator(pl, out h); }
                case EnclaveRequest.KindGun:
                    foreach (Held h in Slots(pl)) if (IsGun(h.Stack.itemValue.ItemClass)) return true;
                    return false;
                default: return false;
            }
        }

        private static Resource ClampRes(int res) { return (Resource)Mathf.Clamp(res, 0, ScNames.ResourceCount - 1); }

        /// <summary>Round MP5 (part E), client: "Take one of these." - the pick of this machine's own guns for the talk with npcId (the facts' request reqId).</summary>
        internal static void BuildPickClient(EntityPlayerLocal pl, int npcId, int reqId)
        {
            pick = pl != null && npcId > 0 ? new GunPick { PlayerId = pl.entityId, NpcId = npcId, RequestId = reqId, Rows = PickOf(Guns(pl)) } : null;
        }

        /// <summary>Round MP5 (part E), client: row i of the pick on screen (made again for another talk or request), or null.</summary>
        internal static Gun PickRowClient(int i, EntityPlayerLocal pl, int npcId, int reqId)
        {
            if (pl == null || npcId <= 0 || i < 0) return null;
            if (pick == null || pick.PlayerId != pl.entityId || pick.NpcId != npcId || pick.RequestId != reqId) BuildPickClient(pl, npcId, reqId);
            return pick != null && i < pick.Rows.Count ? pick.Rows[i] : null;
        }

        /// <summary>
        /// Round MP5 (part E), client: "I have it right here." - what the facts' request asks, out of this machine's own bag: the
        /// bundles that cover the units with the least overpay (Choose, as the host's), taken by type (ScItemTx.TakeFromLocal), or
        /// one generator from its exact slot. taken gets exactly what left (sent with enc.reqgive, given back on a refusal). False
        /// when it is not carried: nothing left the bag (a short take is given back at once).
        /// </summary>
        internal static bool TakeForRequest(EntityPlayerLocal pl, string kind, int res, int units, List<ItemStack> taken)
        {
            if (pl == null || taken == null) return false;
            if (kind == EnclaveRequest.KindGenerator)
            {
                Held h;
                if (!FindGenerator(pl, out h)) return false;
                ItemValue exact = h.Stack.itemValue.Clone();          // before the take: the toolbelt's SetItem clears the stack in place
                if (!TakeOne(pl, h.Belt, h.Slot, exact)) return false;
                taken.Add(new ItemStack(exact, 1));
                return true;
            }
            if (kind != EnclaveRequest.KindResource) return false;
            Resource rr = ClampRes(res);
            Bundles take;
            if (!Choose(Carried(pl, rr), Math.Max(1, units), out take)) return false;
            BundleSize[] sizes = { BundleSize.Large, BundleSize.Medium, BundleSize.Small, BundleSize.Legacy };
            int[] want = { take.Large, take.Medium, take.Small, take.Legacy };
            for (int i = 0; i < 4; i++)
            {
                if (want[i] <= 0) continue;
                ItemValue v = ScStockpile.BundleValue(rr, sizes[i]);
                int type = v != null ? v.type : -1;
                int got = type >= 0 ? ScItemTx.TakeFromLocal(pl, iv => iv != null && iv.type == type, want[i], taken) : 0;
                if (got >= want[i]) continue;
                if (taken.Count > 0) ScItemTx.GiveToLocal(pl, taken);
                ScLog.Warn("enclave request: " + rr + " bundles came up short when taken (" + got + " of " + want[i] + " " + sizes[i] + ") - given back, not handed over");
                taken.Clear();
                return false;
            }
            return taken.Count > 0;
        }

        /// <summary>Round MP5 (part E), client: the pick row's gun out of its exact slot - still there, the same gun - as it was (wear, rounds), in taken.</summary>
        internal static bool TakeGun(EntityPlayerLocal pl, Gun g, out ItemStack taken)
        {
            taken = null;
            if (pl == null || g == null) return false;
            ItemStack now = SlotStack(pl, g.Belt, g.Slot);
            if (!Same(now, g.Value)) return false;
            ItemValue exact = now.itemValue.Clone();
            if (!TakeOne(pl, g.Belt, g.Slot, exact)) return false;
            taken = new ItemStack(exact, 1);
            return true;
        }

        /// <summary>Round MP5 (part E): the stack in one of the player's own slots (the toolbelt's or the bag's), or null.</summary>
        private static ItemStack SlotStack(EntityPlayerLocal pl, bool belt, int slot)
        {
            if (pl == null || slot < 0) return null;
            if (belt)
            {
                Inventory inv = pl.inventory;
                return inv != null && slot < Math.Min(inv.SlotCount, inv.Length) ? inv.GetItem(slot) : null;
            }
            ItemStack[] items = pl.bag != null ? pl.bag.ItemGrid.items : null;
            return items != null && slot < items.Length ? items[slot] : null;
        }

        /// <summary>E5: the leader's row "I found &lt;name&gt;. I'm sorry." (its words through the row's binding, as the gun pick's).</summary>
        public const string FoundRowId = "scEncReqFound";
        private static int foundRowFor = -1;
        private static string foundRowText = "";

        /// <summary>
        /// The response entry's "response" binding (Patches_Enclaves.Patch_DialogRowText) for a row whose text starts with '{':
        /// a gun pick row's text, made when the pick was. Every frame: one prefix test, then a list read. E5's found row: the
        /// person's name in it, made once per request. E7's gift rows: theirs, made when the gift pick was (ScEncGifts.FillRow).
        /// </summary>
        public static void FillRow(XUiC_DialogResponseEntry entry, ref string value)
        {
            // round MP5 (part E): a client's rows too - its own picks (built from its own bag), the found row from the talk's facts
            if (entry == null || !entry.HasRequirement) return;
            DialogResponse resp = entry.CurrentResponse;
            string id = resp != null ? resp.ID : null;
            if (id == FoundRowId) { value = ScCommunity.IsHost ? FoundRow() : ScEncTalk.FoundRow(); return; }
            if (ScEncGifts.FillRow(id, ref value)) return;      // E7: a gift row (scEncGift0..3)
            if (id == null || id.Length != GunRowPrefix.Length + 1 || !id.StartsWith(GunRowPrefix, StringComparison.Ordinal)) return;
            int i = id[id.Length - 1] - '0';
            GunPick p = pick;
            value = p != null && i >= 0 && i < p.Rows.Count ? p.Rows[i].Row : "";
        }

        /// <summary>"I found Mara. I'm sorry." for the leader being talked to (made again only for another request).</summary>
        private static string FoundRow()
        {
            Enclave e;
            EnclaveRequest r;
            EntityPlayerLocal pl = ScCommunity.LocalPlayer;
            if (pl == null || !LeaderTalking(pl, null, out e, out r) || r == null || r.Kind != EnclaveRequest.KindMissing) return "";
            if (r.Id != foundRowFor)
            {
                foundRowFor = r.Id;
                foundRowText = string.Format(Loc("scEncReqFoundRow"), ScEncSearch.PersonName(r));
            }
            return foundRowText;
        }

        /// <summary>
        /// The line panel's request tokens (ScEncDialog.Fill): {reqask} the offer, {reqwait} the wait, {reqreply} what the
        /// last request action left. Each is a line chosen by kind and band (the part a voice can say) and a sentence with
        /// the specifics. E5's {reqnone}: "nothing right now", or the thanks they owe for a request done while the player was
        /// away (a cleared infestation, a siege beaten, a survivor brought home: Enclave.Thanks) - once. vo2: voiceKey is the
        /// Localization key of the line put in, the name of its voice clip (ScVoice); null when there was no such token.
        /// </summary>
        public static string FillTokens(string s, Enclave e, out string voiceKey)
        {
            voiceKey = null;
            string key;
            if (s.IndexOf("{reqask}", StringComparison.Ordinal) >= 0) { s = s.Replace("{reqask}", AskText(e, out key)); voiceKey = key; }
            if (s.IndexOf("{reqwait}", StringComparison.Ordinal) >= 0) { s = s.Replace("{reqwait}", WaitText(e, out key)); voiceKey = key; }
            if (s.IndexOf("{reqreply}", StringComparison.Ordinal) >= 0)
            {
                bool mine = reply != null && reply.EnclaveId == e.Id;
                s = s.Replace("{reqreply}", mine ? reply.Text : Loc("scEncReqGone"));
                voiceKey = mine ? reply.Key : "scEncReqGone";
            }
            if (s.IndexOf("{reqnone}", StringComparison.Ordinal) >= 0) { s = s.Replace("{reqnone}", NoneText(e, out key)); voiceKey = key; }
            return s;
        }

        /// <summary>E5: the leader with nothing to ask - their thanks for a request done while the player was away (once), else "nothing right now".</summary>
        private static string NoneText(Enclave e, out string key)
        {
            key = NoneKey(e);                 // round MP5 (part E): the key alone is NoneKey's (a client's facts carry it)
            if (string.IsNullOrEmpty(e.Thanks)) return Loc(key);
            string line = Loc(key);
            e.Thanks = "";
            ScRegistry.SaveAtomic();
            return line;
        }

        /// <summary>Round MP5 (part E): {reqnone}'s line key with no side effect (NoneText spends the thanks when it is shown).</summary>
        internal static string NoneKey(Enclave e)
        {
            return string.IsNullOrEmpty(e.Thanks) ? "scEncReqNothingLine" : LineKey("scEncThanks", e.Thanks, ScEnclaves.StandingKey(e));
        }

        private static string AskText(Enclave e, out string key)
        {
            string info;
            EnclaveRequest known;
            AskParts(e, out key, out info, out known);     // round MP5 (part E): the parts with no side effect (a client's facts carry them)
            if (known != null) ScEncInfest.MakeKnown(known, ScCommunity.LocalPlayer);
            return info == null ? Loc(key) : Loc(key) + " " + info;
        }

        /// <summary>
        /// Round MP5 (part E): {reqask}'s line key and specifics (null: none, the key alone) with no side effect; makesKnown is the
        /// offer whose infestation the line names - AskText makes it known when shown (a client: ScEncTalk's enc.seen).
        /// </summary>
        internal static void AskParts(Enclave e, out string key, out string info, out EnclaveRequest makesKnown)
        {
            key = "scEncReqGone";
            info = null;
            makesKnown = null;
            EnclaveRequest r = Of(e);
            if (r == null || !r.IsOffered) return;
            int search = Math.Max(1, ScConfig.EnclaveDueDaysSearch);
            switch (r.Kind)
            {
                // E5: who is missing is said once the player agrees (they go missing then); the infestation is named - and known now
                case EnclaveRequest.KindMissing: info = string.Format(Loc("scEncReqOfferInfoMissing"), search, r.LapseDay); break;
                case EnclaveRequest.KindInfestation:
                    makesKnown = r;
                    info = string.Format(Loc("scEncReqOfferInfoInfest"), r.TargetName, ScEncInfest.WhereText(r, e), search, r.LapseDay);
                    break;
                default:
                    info = string.Format(Loc(r.Kind == EnclaveRequest.KindResource ? "scEncReqOfferInfoRes" : "scEncReqOfferInfo"), WhatText(r),
                                         Math.Max(1, ScConfig.EnclaveDueDaysItems), r.LapseDay);
                    break;
            }
            key = LineKey("scEncAsk", r.Kind, ScEnclaves.StandingKey(e));
        }

        private static string WaitText(Enclave e, out string key)
        {
            string info;
            WaitParts(e, out key, out info);    // round MP5 (part E)
            return info == null ? Loc(key) : Loc(key) + " " + info;
        }

        /// <summary>Round MP5 (part E): {reqwait}'s line key and specifics (null: none, the key alone).</summary>
        internal static void WaitParts(Enclave e, out string key, out string info)
        {
            key = "scEncReqGone";
            info = null;
            EnclaveRequest r = Of(e);
            if (r == null || !r.IsAccepted) return;
            switch (r.Kind)
            {
                case EnclaveRequest.KindMissing: info = ScEncSearch.WaitInfo(r); break;
                case EnclaveRequest.KindInfestation: info = string.Format(Loc("scEncReqWaitInfoInfest"), r.TargetName, r.DueDay); break;
                case EnclaveRequest.KindSiege: info = ScEncSiege.WaitInfo(r); break;
                default: info = string.Format(Loc("scEncReqWaitInfo"), WhatText(r), r.DueDay); break;
            }
            key = LineKey("scEncWait", r.Kind, ScEnclaves.StandingKey(e));
        }

        /// <summary>The reply statement's line: the words of key (vo2: the name of its voice clip), then info, the specifics.</summary>
        private static void SetReply(Enclave e, string key, string info)
        {
            reply = e != null ? new Reply { EnclaveId = e.Id, Key = key, Text = (Loc(key) + " " + (info ?? "")).Trim() } : null;
        }

        /// <summary>"(standing +10)" / "(standing -5)" after a reply line; "" when it did not move (E7: a gift's reply too).</summary>
        internal static string Aside(int moved)
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

        /// <summary>
        /// "Leave it with me.": the offer accepted (Accept), "Thank you. We'll be here." and by when. E5: a missing survivor's
        /// reply names them and where they were last seen; an infestation already cleared is done at once instead.
        /// </summary>
        public static void ActAccept(EntityPlayer player)
        {
            Enclave e;
            EnclaveRequest r;
            if (!Acting(player, out e, out r)) return;
            string key, info;
            AcceptAs(player, e, r, out key, out info);      // round MP5 (part E): the core is any player's (enc.req)
            SetReply(e, key, info);
        }

        /// <summary>
        /// Round MP5 (part E): ActAccept's work for any player (the host's own action, a client's enc.req): the offer accepted -
        /// the quest theirs - or an infestation cleared already done at once. key and info: the reply line ({reqreply}). True when
        /// it went through.
        /// </summary>
        internal static bool AcceptAs(EntityPlayer player, Enclave e, EnclaveRequest r, out string key, out string info)
        {
            key = "scEncReqGone";
            info = "";
            if (r == null || !r.IsOffered) return false;
            string band = ScEnclaves.StandingKey(e);
            if (r.Kind == EnclaveRequest.KindInfestation && ScEncInfest.IsCleared(r)) return DoneClearedAs(e, r, band, out key, out info);
            if (!Accept(r, player, "the player said yes")) return false;
            switch (r.Kind)
            {
                case EnclaveRequest.KindMissing: info = string.Format(Loc("scEncReqAcceptInfoMissing"), ScEncSearch.PersonName(r), r.TargetName, r.DueDay); break;
                case EnclaveRequest.KindInfestation: info = string.Format(Loc("scEncReqAcceptInfoInfest"), r.TargetName, r.DueDay); break;
                default: info = string.Format(Loc("scEncReqAcceptInfo"), WhatText(r), r.DueDay); break;
            }
            key = LineKey("scEncAccepted", r.Kind, band);
            return true;
        }

        /// <summary>
        /// E5, "I found &lt;name&gt;. I'm sorry." (a missing survivor whose remains the player searched): the request done
        /// (+EnclaveGainMissingDead, SurvivorOrigins' favour event), the leader's grief by band.
        /// </summary>
        public static void ActFound(EntityPlayer player)
        {
            Enclave e;
            EnclaveRequest r;
            if (!Acting(player, out e, out r)) return;
            string key, info;
            FoundAs(e, r, out key, out info);              // round MP5 (part E)
            SetReply(e, key, info);
        }

        /// <summary>Round MP5 (part E): ActFound's work for any player; key and info the reply line. True when it went through.</summary>
        internal static bool FoundAs(Enclave e, EnclaveRequest r, out string key, out string info)
        {
            key = "scEncReqGone";
            info = "";
            if (r == null || r.Kind != EnclaveRequest.KindMissing || !r.IsAccepted || r.Stage != ScEncSearch.StageSearched) return false;
            string band = ScEnclaves.StandingKey(e);
            int moved = Close(r, Done, "the player told them", GainFor(r), ScEncSearch.PersonName(r), EventFor(r));
            key = LineKey("scEncFound", r.Kind, band);
            info = Aside(moved);
            return true;
        }

        /// <summary>
        /// E5, "I already took care of it." (an infestation request whose target the player cleared before agreeing to it):
        /// checked again - cleared through I1's own path, or its record gone with the building reading clear - and done
        /// (+EnclaveGainInfestation, the favour event), their thanks by band.
        /// </summary>
        public static void ActCleared(EntityPlayer player)
        {
            Enclave e;
            EnclaveRequest r;
            if (!Acting(player, out e, out r)) return;
            string key, info;
            ClearedAs(e, r, out key, out info);            // round MP5 (part E)
            SetReply(e, key, info);
        }

        /// <summary>Round MP5 (part E): ActCleared's work for any player; key and info the reply line. True when it went through.</summary>
        internal static bool ClearedAs(Enclave e, EnclaveRequest r, out string key, out string info)
        {
            key = "scEncReqGone";
            info = "";
            if (r == null || r.Kind != EnclaveRequest.KindInfestation) return false;
            if (!ScEncInfest.IsCleared(r)) { key = "scEncReqNotCleared"; return false; }
            return DoneClearedAs(e, r, ScEnclaves.StandingKey(e), out key, out info);
        }

        /// <summary>An infestation cleared already: done (round MP5, part E: the reply line in key and info, for any player).</summary>
        private static bool DoneClearedAs(Enclave e, EnclaveRequest r, string band, out string key, out string info)
        {
            int moved = Close(r, Done, "the infestation was cleared already", GainFor(r), r.TargetName, EventFor(r));
            key = LineKey("scEncThanks", r.Kind, band);
            info = Aside(moved);
            return true;
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
            if (r == null || (r.Kind != EnclaveRequest.KindResource && r.Kind != EnclaveRequest.KindGenerator)) { SetReply(e, "scEncReqGone", ""); return; }
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
            if (!taken) { SetReply(e, "scEncReqNotCarried", ""); return; }
            int moved = Close(r, Done, "handed over in the talk", GainFor(r), what);
            SetReply(e, LineKey("scEncThanks", r.Kind, band), Aside(moved));
        }

        /// <summary>
        /// Round MP5 (part E): "I have it right here." from a remote player (enc.reqgive) - their machine took what it hands over
        /// (TakeForRequest) and sent it: checked here against the request now (its resource's bundles covering the units, or one
        /// generator; co-op trust in the stacks, as every round MP3 take), then the request done as the host's ActGive does. key
        /// and info: the reply line. False: nothing kept - the caller refuses, and the player's machine gives the items back.
        /// </summary>
        internal static bool GiveFor(EntityPlayer player, EntityAlive npc, List<ItemStack> stacks, out string key, out string info)
        {
            key = "scEncReqGone";
            info = "";
            Enclave e;
            EnclaveRequest r;
            if (!LeaderAt(npc, out e, out r) || r == null || (r.Kind != EnclaveRequest.KindResource && r.Kind != EnclaveRequest.KindGenerator)) return false;
            if (stacks == null || stacks.Count == 0) { key = "scEncReqNotCarried"; return false; }
            string band = ScEnclaves.StandingKey(e), what = WhatText(r);
            if (r.Kind == EnclaveRequest.KindResource)
            {
                int got = 0;
                foreach (ItemStack s in stacks)
                {
                    Resource rr;
                    int u;
                    if (!ScStockpile.IsBundle(s, out rr, out u) || rr != r.Res) { key = "scEncReqNotCarried"; return false; }
                    got += u * s.count;
                }
                if (got < Math.Max(1, r.Units)) { key = "scEncReqNotCarried"; return false; }
                if (got > r.Units) ScLog.Out("enclave request #" + r.Id + ": " + got + " " + ScNames.Key(r.Res) + " unit(s) handed over for " + r.Units + " (the rest a gift)");
            }
            else
            {
                ItemStack g = stacks[0];
                if (stacks.Count != 1 || g.count != 1 || !IsGenerator(g.itemValue.ItemClass)) { key = "scEncReqNotCarried"; return false; }
                string name = g.itemValue.ItemClass.GetLocalizedItemName();
                if (!string.IsNullOrEmpty(name)) what = name;
                ScLog.Out("enclave request #" + r.Id + ": " + g.itemValue.ItemClass.Name + " from " + player.EntityName + "'s own inventory");
            }
            int moved = Close(r, Done, "handed over in the talk", GainFor(r), what);
            key = LineKey("scEncThanks", r.Kind, band);
            info = Aside(moved);
            return true;
        }

        /// <summary>
        /// Round MP5 (part E): a gun pick row from a remote player (enc.reqgun) - their machine took that gun from its slot and sent
        /// it: a gun that counts (IsGun), its points from its own tier and quality, the request done as the host's ActGiveGun does.
        /// False: nothing kept (the player's machine gives the gun back).
        /// </summary>
        internal static bool GunFor(EntityPlayer player, EntityAlive npc, ItemStack gun, out string key, out string info)
        {
            key = "scEncReqGone";
            info = "";
            Enclave e;
            EnclaveRequest r;
            if (!LeaderAt(npc, out e, out r) || r == null || r.Kind != EnclaveRequest.KindGun) return false;
            if (gun == null || gun.IsEmpty() || gun.count != 1 || !IsGun(gun.itemValue.ItemClass)) { key = "scEncReqNotCarried"; return false; }
            ItemClass ic = gun.itemValue.ItemClass;
            int tier = TierOf(ic), q = Math.Max(1, (int)gun.itemValue.Quality), points = GunPoints(tier, q);
            string name = ic.GetLocalizedItemName();
            if (string.IsNullOrEmpty(name)) name = Loc(ic.Name);
            string band = ScEnclaves.StandingKey(e);
            ScLog.Out("enclave request #" + r.Id + ": " + ic.Name + " (T" + tier + ", quality " + q + ") from " + player.EntityName + "'s own inventory - " + points + " point(s)");
            int moved = Close(r, Done, "a gun handed over in the talk", points, string.Format(Loc("scEncReqGunWhat"), name, tier, q));
            key = LineKey("scEncThanks", r.Kind, band);
            info = Aside(moved);
            return true;
        }

        /// <summary>
        /// Round MP5 (part E): a remote player's line showed what the host's fill does as it shows it (enc.seen): what 1 - the offer's
        /// infestation is known now (AskText's MakeKnown); what 2 - the leader's thanks are spent (NoneText's). True when it did.
        /// </summary>
        internal static bool SeenFor(EntityPlayer player, EntityAlive npc, int what)
        {
            Enclave e;
            EnclaveRequest r;
            if (!LeaderAt(npc, out e, out r)) return false;
            if (what == 1)
            {
                if (r == null || !r.IsOffered || r.Kind != EnclaveRequest.KindInfestation) return false;
                ScEncInfest.MakeKnown(r, player);
                return true;
            }
            if (what != 2 || string.IsNullOrEmpty(e.Thanks)) return false;
            e.Thanks = "";
            ScRegistry.SaveAtomic();
            return true;
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
            if (r == null || r.Kind != EnclaveRequest.KindGun) { SetReply(e, "scEncReqGone", ""); return; }
            EntityAlive npc = ScFavours.CurrentNpc(player);
            GunPick p = pick;
            Gun g = p != null && npc != null && p.PlayerId == player.entityId && p.NpcId == npc.entityId && p.RequestId == r.Id && row >= 0 && row < p.Rows.Count ? p.Rows[row] : null;
            string band = ScEnclaves.StandingKey(e);
            if (g == null || !TakeOne((EntityPlayerLocal)player, g.Belt, g.Slot, g.Value)) { SetReply(e, "scEncReqNotCarried", ""); return; }
            pick = null;
            ScLog.Out("enclave request #" + r.Id + ": " + g.Value.ItemClass.Name + " (T" + g.Tier + ", quality " + g.Quality + ") from the " + (g.Belt ? "toolbelt" : "bag") + " slot " + g.Slot
                      + " - " + g.Points + " point(s)");
            int moved = Close(r, Done, "a gun handed over in the talk", g.Points, string.Format(Loc("scEncReqGunWhat"), g.Name, g.Tier, g.Quality));
            SetReply(e, LineKey("scEncThanks", r.Kind, band), Aside(moved));
        }

        /// <summary>"No, I can't help with that.": the offer declined - a failure, EnclaveLossDecline by band.</summary>
        public static void ActDecline(EntityPlayer player)
        {
            Enclave e;
            EnclaveRequest r;
            if (!Acting(player, out e, out r)) return;
            string key, info;
            DeclineAs(e, r, out key, out info);            // round MP5 (part E)
            SetReply(e, key, info);
        }

        /// <summary>Round MP5 (part E): ActDecline's work for any player; key and info the reply line. True when it went through.</summary>
        internal static bool DeclineAs(Enclave e, EnclaveRequest r, out string key, out string info)
        {
            key = "scEncReqGone";
            info = "";
            if (r == null || !r.IsOffered) return false;
            string band = ScEnclaves.StandingKey(e);
            // E5 (the review's N2): an infestation cleared while it was an offer is no work to refuse - it is done (the No row
            // hides then; this is a stale dialog's case)
            if (ScEncInfest.IsCleared(r)) return DoneClearedAs(e, r, band, out key, out info);
            int moved = Close(r, Declined, "the player said no");
            key = LineKey("scEncDeclined", r.Kind, band);
            info = Aside(moved);
            return true;
        }

        /// <summary>
        /// "Let me think about it.": nothing now (the talk closes); the leader's word, and how long the offer stands, as a
        /// tell after it. vo2: the word is said aloud too, and the talk's close does not cut it off (ScVoice.SayAfterTalk).
        /// </summary>
        public static void ActThink(EntityPlayer player)
        {
            Enclave e;
            EnclaveRequest r;
            if (!Acting(player, out e, out r)) return;
            ThinkAs(player, ScFavours.CurrentNpc(player), e, r);     // round MP5 (part E)
        }

        /// <summary>Round MP5 (part E): ActThink's work for any player: the leader's word as a tell and, as the talk closes, said aloud on the talker's machine.</summary>
        internal static bool ThinkAs(EntityPlayer player, EntityAlive npc, Enclave e, EnclaveRequest r)
        {
            if (r == null || !r.IsOffered) return false;
            EncPerson leader = ScEnclavePeople.LeaderOf(e);
            string key = LineKey("scEncThink", r.Kind, ScEnclaves.StandingKey(e));
            ScCommunity.TellKey(player, "scEncReqThinkTell", "", leader != null ? leader.Name : e.Name, ScCommunity.LocArg(key), r.LapseDay);   // the word: a key
            ScVoice.SayAfterTalkFor(player, npc, key);   // round MP4: on the talker's machine (a remote one's on theirs)
            ScLog.Verbose("enclave request #" + r.Id + ": the player will think about it (the offer stands to the end of day " + r.LapseDay + ")");
            return true;
        }

        /// <summary>"I can't do it.": the accepted request failed now - EnclaveLossFail by band (E5: only one that can be given up, CanGiveUp).</summary>
        public static void ActCantDo(EntityPlayer player)
        {
            Enclave e;
            EnclaveRequest r;
            if (!Acting(player, out e, out r)) return;
            string key, info;
            CantDoAs(e, r, out key, out info);             // round MP5 (part E)
            SetReply(e, key, info);
        }

        /// <summary>Round MP5 (part E): ActCantDo's work for any player; key and info the reply line. True when it went through.</summary>
        internal static bool CantDoAs(Enclave e, EnclaveRequest r, out string key, out string info)
        {
            key = "scEncReqGone";
            info = "";
            if (r == null || !r.IsAccepted || !CanGiveUp(r)) return false;
            string band = ScEnclaves.StandingKey(e);
            int moved = Close(r, Failed, "the player gave it up");
            key = LineKey("scEncCantDo", r.Kind, band);
            info = Aside(moved);
            return true;
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
            string head = "#" + r.Id + " " + (e != null ? e.Name + " (enclave " + e.Id + ", " + ScEnclaves.StandingKey(e) + ")" : "enclave " + r.EnclaveId + " (gone)") + ": "
                          + Describe(r) + " - " + WhatText(r) + " - ";
            string quest = "journal quest " + (r.QuestCode != 0 ? r.QuestCode.ToString() : "none");
            // E5: a siege runs on its own clock, not the day's
            if (r.Kind == EnclaveRequest.KindSiege) return head + "called on day " + r.AcceptDay + ", " + ScEncSiege.ClockText(r) + ", " + quest;
            return head + (r.IsOffered ? "offered on day " + r.OfferDay + ", stands to the end of day " + r.LapseDay + " (" + Left(r.LapseDay) + ")"
                                       : "accepted on day " + r.AcceptDay + ", due by the end of day " + r.DueDay + " (" + Left(r.DueDay) + "), " + quest);
        }

        /// <summary>sc enclave requests: every open one; E5: the siege clock (the day's roll, the hour of a call, a live one).</summary>
        public static void ListLines(List<string> into)
        {
            int today = ScCommunity.Today;
            into.Add(OpenCount() + " open request(s) of at most " + ScConfig.EnclaveRequestMaxOpen + " (a siege not counted), " + NewToday() + " new call(s) for help today of at most "
                     + ScConfig.EnclaveRequestMaxPerDay + " (sieges counted); the request clock is day " + Now
                     + (Now > today ? " (ahead of the world's day " + today + ": sc day)" : "") + "; a day's chance " + Pct(ScConfig.EnclaveRequestChanceNeutral) + "/"
                     + Pct(ScConfig.EnclaveRequestChanceFriendly) + "/" + Pct(ScConfig.EnclaveRequestChanceAllied) + " (neutral/friendly/allied), generators from day "
                     + ScConfig.EnclaveGeneratorFromDay + ", kinds " + KindsText());
            into.Add("  " + ScEncSiege.ScheduleLine());
            foreach (EnclaveRequest r in All) into.Add("  " + Line(r));
        }

        private static string KindsText()
        {
            var parts = new List<string>();
            foreach (string k in RollKinds)
            {
                float wt;
                ScConfig.EnclaveRequestKindWeights.TryGetValue(k, out wt);
                parts.Add(k + ":" + wt.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
            }
            return string.Join(",", parts.ToArray());
        }

        private static string Pct(float f) { return Mathf.RoundToInt(f * 100f) + "%"; }

        /// <summary>
        /// sc enclave request &lt;id&gt; [offer &lt;kind&gt; [resource] [size] [force] | accept | complete [points] | fail | lapse |
        /// clear | fate alive|dead] (testing): the enclave's request, or one made or moved now. offer ignores the chance and the
        /// cooldown and refuses at the EnclaveRequestMaxOpen cap without "force"; complete and fail apply the real outcome,
        /// standing and journal included; clear withdraws it and the cooldown with it. E5: offer missing|infestation (only
        /// where it can be given: CanOffer); a missing survivor's "fate alive|dead" settles the roll the next arrival makes; a
        /// siege is "sc enclave siege".
        /// </summary>
        public static void Command(Enclave e, List<string> p, List<string> into, EntityPlayer player = null)
        {
            EnclaveRequest r = Of(e);
            string verb = p.Count > 3 ? p[3].ToLowerInvariant() : "";
            // round MP5 (part E): the player who typed it (ConsoleCmdSc.Me: an accepted request's quest is theirs), else the host's own
            EntityPlayer pl = player ?? ScCommunity.LocalPlayer;
            switch (verb)
            {
                case "":
                {
                    into.Add(e.Name + " (enclave " + e.Id + ", " + ScEnclaves.StandingWord(e) + "): " + (r != null ? Line(r) : "no open request"));
                    string why = Eligible(e, Now);
                    into.Add("  the daily roll: " + (why ?? "eligible, " + Pct(ChanceFor(e)) + " a day") + (e.NextRequestDay > 0 ? "; cooldown to day " + e.NextRequestDay : "")
                             + "; " + OpenCount() + " of at most " + ScConfig.EnclaveRequestMaxOpen + " open");
                    into.Add("  E5: missing " + (CanOffer(e, EnclaveRequest.KindMissing) ?? "can be offered") + "; infestation "
                             + (CanOffer(e, EnclaveRequest.KindInfestation) ?? "can be offered") + "; siege " + (ScEncSiege.WhyNot(e, pl, false) ?? "can be called"));
                    return;
                }
                case "offer":
                {
                    if (r != null) { into.Add(e.Name + " has an open request already (#" + r.Id + "): clear it first"); return; }
                    if (e.IsHostile || e.Lost) { into.Add(e.Name + " makes no requests (" + (e.IsHostile ? "hostile" : "its building is not in this world") + ")"); return; }
                    if (!e.Known) { into.Add(e.Name + " is not found yet: sc enclave reveal " + e.Id); return; }
                    string kind = p.Count > 4 ? p[4].ToLowerInvariant() : "";
                    if (Array.IndexOf(RollKinds, kind) < 0)
                    {
                        into.Add(kind == EnclaveRequest.KindSiege ? "a siege is a call, not an offer: sc enclave siege " + e.Id + " now"
                                 : "sc enclave request " + e.Id + " offer <" + string.Join("|", RollKinds) + "> [resource] [small|medium|large] [force]");
                        return;
                    }
                    string cannot = CanOffer(e, kind);
                    if (cannot != null) { into.Add(e.Name + " cannot be given a " + kind + " request: " + cannot); return; }
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
                    if (OpenCount() >= ScConfig.EnclaveRequestMaxOpen && !force) { into.Add(OpenCount() + " requests are open (EnclaveRequestMaxOpen " + ScConfig.EnclaveRequestMaxOpen + "): add force to go past it"); return; }
                    if (DayFull() && !force) { into.Add(NewToday() + " calls for help today already (EnclaveRequestMaxPerDay " + ScConfig.EnclaveRequestMaxPerDay + "): add force to go past it"); return; }   // P2
                    World w = ScCommunity.World;
                    GameRandom rnd = w != null ? w.GetGameRandom() : null;
                    r = Offer(e, kind, Now, rnd, res, units, pl, "the console");
                    into.Add(r != null ? "offered: " + Line(r) : "no offer: no infestation could be named or started near " + e.Name + " (the log says why)");
                    return;
                }
            }
            if (r == null) { into.Add(e.Name + " has no open request (sc enclave request " + e.Id + " offer <kind>)"); return; }
            // E5: a siege is settled through its own command
            if (r.Kind == EnclaveRequest.KindSiege && verb != "clear") { into.Add("#" + r.Id + " is a siege: sc enclave siege " + e.Id + " [win|lose|clear]"); return; }
            switch (verb)
            {
                case "accept":
                    if (!r.IsOffered) { into.Add("#" + r.Id + " is accepted already"); return; }
                    if (ScEncInfest.IsCleared(r))
                    {
                        // E5: as the dialog's "Leave it with me." - an infestation cleared while it was an offer is done on the spot
                        int done = Close(r, Done, "the console (its infestation was cleared already)", GainFor(r), r.TargetName, EventFor(r));
                        into.Add("#" + r.Id + ": its infestation was cleared already - done at once, standing " + (done >= 0 ? "+" : "") + done + " - " + e.Name + " " + e.Standing);
                        return;
                    }
                    if (!Accept(r, pl, "the console")) { into.Add("#" + r.Id + " could not be accepted (the log says why)"); return; }
                    into.Add("accepted: " + Line(r));
                    return;
                case "complete":
                {
                    // E5: a missing survivor whose remains lie there is "found dead" (+EnclaveGainMissingDead); else they come home alive
                    if (r.Kind == EnclaveRequest.KindMissing && r.Stage == ScEncSearch.StageDead) r.Stage = ScEncSearch.StageSearched;
                    int gain = GainFor(r);
                    if (r.Kind == EnclaveRequest.KindGun)
                    {
                        int pts;
                        gain = p.Count > 4 && int.TryParse(p[4], out pts) ? Math.Max(0, pts) : GunPoints(1, 3);
                    }
                    int moved = Close(r, Done, "the console", gain, null, EventFor(r));
                    into.Add("#" + r.Id + " done: standing " + (moved >= 0 ? "+" : "") + moved + " - " + e.Name + " " + e.Standing + " (" + ScEnclaves.StandingWord(e) + ")");
                    return;
                }
                case "fate":
                {
                    if (r.Kind != EnclaveRequest.KindMissing) { into.Add("#" + r.Id + " is no missing survivor's request"); return; }
                    string f = p.Count > 4 ? p[4].ToLowerInvariant() : "";
                    if (f != "alive" && f != "dead") { into.Add("sc enclave request " + e.Id + " fate <alive|dead>"); return; }
                    into.Add(ScEncSearch.ForceFate(r, f == "alive"));
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
                    into.Add("sc enclave request <id> [offer <kind> [resource] [size] [force] | accept | complete [points] | fail | lapse | clear | fate <alive|dead>]");
                    return;
            }
        }
    }
}
