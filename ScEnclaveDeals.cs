using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Trade, enlisting and recruiting with an enclave's people, milestone E3 (E3_SPEC.md; Enclaves_Plan.md 2.7-2.9 and 6:
    /// Q8 a, Q9 a; research enclave_people.md 4-6). Host only, main thread: the dialog, the trade window, the lock hooks and
    /// the 1 Hz tick all run there.
    ///
    /// Trade. The leader of an enclave that is not hostile is a real vanilla trader: their body's TraderData - per entity;
    /// the shared NPCInfo and V4's ToggleTraderID are never touched - names their kind's trader_info (Config/traders.xml,
    /// EnclaveTraderIdBase + 0..5: general, growers, medics, militia, builders, roadcrew) and holds a copy of the enclave's
    /// own stock (Enclave.Stock, TraderData.Write packed). The stock is recorded whenever the leader's body goes (despawn,
    /// unload, shutdown, death) and whenever a trade window with them closes, so it and its restock clock outlive bodies,
    /// succession and reloads; vanilla restocks it every reset_interval days at the first trade after that. Prices scale by
    /// standing once per outermost XUiM_Trader call (the price postfixes; both methods recurse into an item's mods); the
    /// player's barter perks still apply (the trader_info has no override markups). A hostile enclave's leader never gets a
    /// trader id, so X9 refuses their Trade and X14 keeps vanilla's turn-ins off them.
    ///
    /// Enlisting (Q8 a). "Can you come with me?" to a member of an enclave that is not hostile: EnclaveEnlistCost* dukes by
    /// standing, for EnclaveEnlistDays days, one per enclave, counted toward MaxFollowers with the community's followers. The
    /// body takes SC's hire shape (ScCommunity.FollowInternal's: leader and owner, the follow order, Persist) and the
    /// sc_enlisted cvar, which keeps Voice Control's contract and billing off it; the record says enlisted (by whom, until
    /// when). The E2 tick leaves an enlisted body alone (no despawn, no watchdog, no hold) but it stays in the chunk-save
    /// veto: the record is the truth. It ends on "Go home now.", expiry, a side door (SCore's companion-window Dismiss and
    /// any other order that drops the leader; Voice Control's spoken dismiss), its enclave turning hostile (released before
    /// the flip) or the body going (an unload, the shutdown: back home). Going home: ScCommunity.Detach, then the walk - to
    /// their spot while their POI is within EnclaveSpawnRange and loaded, else away toward it until nobody sees them (E2's
    /// despawn rule then takes the body; they are at their spot next time). A death while enlisted costs
    /// EnclaveEnlistDeathPenalty standing. E9: a fighter an ally sends over the radio (ScEncCalls) is enlisted the same way
    /// (EnlistBody, no fee, for EnclaveCallFighterHours), so every rule here holds for them too.
    ///
    /// Recruiting (Q9 a). "Would you join us?" to a member, at home or enlisted: never the leader, the enclave keeps
    /// EnclaveRecruitMinLeft living, one every EnclaveRecruitCooldownDays, and SC's usual gates (CanRecruit's enclave path,
    /// a home, roster room). At Neutral their own favour comes first - SC's recruit favour, rolled unpinned and kept per
    /// person: the prospect and the favour quest's giver follow each new body (ParkedKey while they have none). The join
    /// costs EnclaveRecruitStanding at Neutral and Friendly, nothing at Allied. The person's own Member record joins
    /// (ScCommunity.Admit): the same face, weapons, rounds, skills, traits and name; they leave the enclave's records.
    /// </summary>
    public static class ScEnclaveDeals
    {
        /// <summary>On an enlisted body (host-side readers: Voice Control's billing guard, the dialog).</summary>
        public const string EnlistedCvar = "sc_enlisted";
        /// <summary>The trade kinds in trader id order from EnclaveTraderIdBase (Config/traders.xml).</summary>
        public static readonly string[] TradeKinds = { "general", "growers", "medics", "militia", "builders", "roadcrew" };
        /// <summary>A walk away from a POI out of reach goes in steps of at most this many metres, onto loaded ground.</summary>
        private const float LeaveStep = 40f;
        /// <summary>A body walking home is there within this many metres of its spot (XZ) and this far up or down.</summary>
        private const float ArrivedXZ = 2f, ArrivedDy = 2f;
        /// <summary>A walk home that has not arrived after this many real seconds ends once nobody sees them (they come back at their spot).</summary>
        private const float LeaveMaxSeconds = 300f;

        /// <summary>How deep the XUiM_Trader price calls are (they recurse into an item's mods): only the outermost one scales.</summary>
        private static int priceDepth;
        /// <summary>The once-per-session journal pass (SyncFavours) is done.</summary>
        private static bool synced;
        /// <summary>Bodies a save still recorded as enlisted (a crash): their hired_* is taken off the player at the first tick.</summary>
        private static readonly List<int> staleHires = new List<int>();

        public static void Reset()
        {
            priceDepth = 0;
            synced = false;
            staleHires.Clear();
        }

        private static string Loc(string key) { return Localization.Get(key); }

        // ------------------------------------------------------------------ registry ----

        /// <summary>
        /// ScEnclavePeople.OnRegistryChanged, after the person ids are squared: a person a save recorded as enlisted is home
        /// (their body never outlived the load) and that body's hire is cleared from the player at the first tick; every
        /// enclave favour is parked (no body yet); a favour whose person is gone or dead is dropped (its quest is closed by
        /// the first tick's journal pass).
        /// </summary>
        public static void OnRegistryChanged()
        {
            synced = false;
            staleHires.Clear();
            var byId = new Dictionary<int, EncPerson>();
            foreach (EncPerson p in ScRegistry.EncPeople)
            {
                byId[p.Id] = p;
                if (p.EnlistedBody >= 0) staleHires.Add(p.EnlistedBody);
                p.EnlistedBody = -1;
            }
            List<Prospect> mine = null;
            foreach (Prospect pr in ScRegistry.Prospects.Values)
                if (pr.EncPersonId > 0) (mine ?? (mine = new List<Prospect>())).Add(pr);
            if (mine == null) return;
            int parked = 0, dropped = 0;
            foreach (Prospect pr in mine)
            {
                ScRegistry.Prospects.Remove(pr.Id);
                EncPerson p;
                if (!byId.TryGetValue(pr.EncPersonId, out p) || !p.Alive) { dropped++; continue; }
                pr.Id = ParkedKey(p);
                if (p.HasHome) pr.NpcPos = p.Home;
                ScRegistry.Prospects[pr.Id] = pr;
                parked++;
            }
            ScLog.Out("enclave favours: " + parked + " waiting on their person's next body" + (dropped > 0 ? ", " + dropped + " dropped (the person is gone)" : ""));
        }

        /// <summary>
        /// ScEnclavePeople.Tick, once a session with the player in: the favour quests in the journal follow their person's
        /// prospect (a save keeps the giver id of a body that is gone), one whose prospect is gone is closed; a crashed
        /// session's enlisted hire comes off the player.
        /// </summary>
        public static void SyncOnce(EntityPlayerLocal player)
        {
            if (synced || player == null || player.QuestJournal == null) return;
            synced = true;
            try
            {
                int n = ScQuests.SyncEnclaveFavours(player);
                foreach (int id in staleHires)
                {
                    string cv = "hired_" + id;
                    if (!player.Buffs.HasCustomVar(cv)) continue;
                    player.Buffs.RemoveCustomVar(cv);
                    n++;
                }
                if (staleHires.Count > 0) EntityUtilities.CheckForDanglingHires(player.entityId);
                staleHires.Clear();
                if (n > 0) ScLog.Out("enclave deals: " + n + " favour quest(s) or stale hire(s) squared with the records after the load");
            }
            catch (Exception e)
            {
                ScLog.Once("encdeals-sync", e);
            }
        }

        // ------------------------------------------------------------------ trade ----

        /// <summary>The trader_info id an enclave's kind trades with (EnclaveTraderIdBase + its place in TradeKinds; general for any other kind).</summary>
        public static int TradeIdOf(Enclave e)
        {
            int i = Array.IndexOf(TradeKinds, e != null && e.Kind.Length > 0 ? e.Kind : "general");
            return ScConfig.EnclaveTraderIdBase + Math.Max(0, i);
        }

        /// <summary>TradeIdOf when Config/traders.xml defined it (the 256-slot list holds an entry there), else -1.</summary>
        public static int TraderIdFor(Enclave e)
        {
            int id = TradeIdOf(e);
            TraderInfo[] list = TraderInfo.traderInfoList;
            return list != null && id >= 0 && id < list.Length && list[id] != null ? id : -1;
        }

        /// <summary>The leader's body as their enclave's trader (ScEnclavePeople.Dress): a copy of the enclave's stock under its kind's id.</summary>
        private static void GiveStock(Enclave e, EntityAlive npc)
        {
            EntityTrader t = npc as EntityTrader;
            if (t == null || e.IsHostile) return;       // a hostile enclave never trades: no trader id, X9 refuses Trade
            int id = TraderIdFor(e);
            if (id < 0)
            {
                ScLog.Once("encdeals-noinfo-" + TradeIdOf(e), "enclave " + e.Id + " '" + e.Name + "': no trader_info " + TradeIdOf(e) + " for the kind '" + e.Kind
                           + "' (Config/traders.xml did not apply, or EnclaveTraderIdBase does not match it) - their leader does not trade");
                return;
            }
            TraderData d = DecodeStock(e.Stock);
            // none yet (the first trade stocks it: lastInventoryUpdate 0), or another kind's (an edited kind or id base)
            if (d == null || d.TraderID != id) d = new TraderData();
            d.TraderID = id;
            t.TraderData = d;
        }

        /// <summary>The leader's stock into the enclave record. True when it changed.</summary>
        public static bool CaptureStock(Enclave e, EntityAlive npc)
        {
            EntityTrader t = npc as EntityTrader;
            TraderData d = t != null ? t.TraderData : null;
            if (e == null || d == null || d.TraderID < 0 || d.TraderID != TraderIdFor(e)) return false;
            string s = EncodeStock(d);
            if (s == null || s == e.Stock) return false;
            e.Stock = s;
            return true;
        }

        /// <summary>TraderData.Write (the lock context's own form: id, restock time, inventory), packed for the registry. Null on failure.</summary>
        public static string EncodeStock(TraderData d)
        {
            try
            {
                using (var ms = new MemoryStream())
                {
#if SC_V34
                    // 3.4: TraderData writes to the game's pooled writer only (the same bytes: TraderData version 2, UTF-8 strings)
                    using (PooledBinaryWriter bw = MemoryPools.poolBinaryWriter.AllocSync(false))
                    {
                        bw.SetBaseStream(ms);
                        d.Write(bw);
                        bw.Flush();
                        return ScRunners.Pack(ms.ToArray());
                    }
#else
                    using (var bw = new BinaryWriter(ms))
                    {
                        d.Write(bw);
                        bw.Flush();
                        return ScRunners.Pack(ms.ToArray());
                    }
#endif
                }
            }
            catch (Exception ex)
            {
                ScLog.Once("encdeals-encode", ex);
                return null;
            }
        }

        /// <summary>
        /// EncodeStock's TraderData back, or null (none, unreadable). Item ids are the save's own (ItemValue.Write registers
        /// each in the save's name map, which loads before the registry); an entry whose item the game no longer has is
        /// dropped.
        /// </summary>
        public static TraderData DecodeStock(string s)
        {
            byte[] bytes = ScRunners.Unpack(s);
            if (bytes == null) return null;
            try
            {
                var d = new TraderData();
                using (var ms = new MemoryStream(bytes))
                {
#if SC_V34
                    using (PooledBinaryReader br = MemoryPools.poolBinaryReader.AllocSync(false)) { br.SetBaseStream(ms); d.Read(br); }
#else
                    using (var br = new BinaryReader(ms)) d.Read(br);
#endif
                }
                d.PrimaryInventory.RemoveAll(en => en == null || en.Item == null || en.Item.IsEmpty() || en.Item.itemValue.ItemClass == null);
                foreach (ItemStack[] g in d.TierItemGroups)
                {
                    if (g == null) continue;
                    foreach (ItemStack st in g)
                        if (st != null && !st.IsEmpty() && st.itemValue.ItemClass == null) { d.TierItemGroups.Clear(); return d; }
                }
                return d;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>The leader trades: theirs at home, of an enclave that is not hostile, and their body names a trader that exists (X9's HasTrader).</summary>
        public static bool CanTrade(EncPerson p, EntityAlive npc)
        {
            Enclave e = ScEnclavePeople.EnclaveOf(p);
            EntityTrader t = npc as EntityTrader;
            return e != null && t != null && p.IsLeader && p.State == EncPerson.StateHome && !e.IsHostile && Patches_Xnpc.Patch_NoTalkToRaiders.HasTrader(t);
        }

        /// <summary>The price factor of an enclave's standing (EnclaveBuy* / EnclaveSell*; 1 for a hostile one, which never trades).</summary>
        public static float Factor(Enclave e, bool buy)
        {
            switch (ScEnclaves.StandingKey(e))
            {
                case "allied": return buy ? ScConfig.EnclaveBuyAllied : ScConfig.EnclaveSellAllied;
                case "friendly": return buy ? ScConfig.EnclaveBuyFriendly : ScConfig.EnclaveSellFriendly;
                case "neutral": return buy ? ScConfig.EnclaveBuyNeutral : ScConfig.EnclaveSellNeutral;
                default: return 1f;
            }
        }

        /// <summary>The XUiM_Trader price prefix: one call deeper.</summary>
        public static void PriceEnter() { priceDepth++; }

        /// <summary>The finalizer: one call out, however it ended.</summary>
        public static void PriceLeave() { if (priceDepth > 0) priceDepth--; }

        /// <summary>
        /// The postfix: the outermost call's price scaled by the open trader's enclave standing. Any other trader is untouched
        /// after one cvar read (%sc_enc on the open trader entity); the factor is read at every call, so the window follows a
        /// standing change.
        /// </summary>
        public static void ScalePrice(XUi xui, bool buy, ref int price)
        {
            if (priceDepth != 1 || price <= 0 || xui == null || xui.Trader == null) return;
            EntityAlive t = xui.Trader.Trader as EntityAlive;
            if (t == null || t.Buffs == null || !t.Buffs.HasCustomVar(ScEnclavePeople.EncCvar)) return;
            Enclave e = ScEnclavePeople.EnclaveOf(ScEnclavePeople.PersonOf(t));
            if (e == null) return;
            float f = Factor(e, buy);
            if (f == 1f || f <= 0f) return;
            price = Math.Max(1, Mathf.RoundToInt(price * f));
        }

        /// <summary>EntityTrader.OnUnlockedServer's postfix: a trade window with an enclave's leader closed (channel 1) - their stock into the record.</summary>
        public static void OnTradeClosed(EntityTrader t, ushort channel)
        {
            if (channel != 1 || t == null || t.Buffs == null || !t.Buffs.HasCustomVar(ScEnclavePeople.EncCvar) || !ScCommunity.IsHost || !ScRegistry.Loaded) return;
            EncPerson p = ScEnclavePeople.PersonOf(t);
            Enclave e = ScEnclavePeople.EnclaveOf(p);
            if (e == null || !p.IsLeader || t.WorldTimeBorn != p.EntityBorn) return;
            if (!CaptureStock(e, t)) return;
            ScRegistry.SaveAtomic();
            ScLog.Verbose("enclave " + e.Id + " '" + e.Name + "': trade with " + p.Name + " closed - their stock recorded");
        }

        // ------------------------------------------------------------------ the body lifecycle (ScEnclavePeople) ----

        /// <summary>ScEnclavePeople.Dress, for every new body: a leader's trade stock, and the person's favour follows the body.</summary>
        public static void OnDressed(Enclave e, EncPerson p, EntityAlive npc)
        {
            if (p.IsLeader) GiveStock(e, npc);
            Prospect pr = ProspectByPerson(p.Id);
            if (pr != null) Rekey(pr, npc.entityId, npc.GetBlockPosition());
        }

        /// <summary>ScEnclavePeople's succession: the new leader's body, when it is out, becomes the trader (the stock is the enclave's).</summary>
        public static void OnNewLeader(Enclave e, EncPerson p)
        {
            EntityAlive npc = ScEnclavePeople.OwnBody(p);
            if (npc != null && !npc.IsDead()) GiveStock(e, npc);
        }

        /// <summary>
        /// ScEnclavePeople.Unbody: a body is no longer the person's (it went, unloaded, died or vanished). A leader's stock is
        /// recorded from it, an enlistment ends (they are home: their next body is built at their spot), a walk home ends,
        /// and their favour waits on ParkedKey for the next body. own: that body while it is still there, else null.
        /// </summary>
        public static void OnUnbodied(EncPerson p, EntityAlive own)
        {
            Enclave e = ScEnclavePeople.EnclaveOf(p);
            if (own != null && p.IsLeader && e != null) CaptureStock(e, own);
            if (p.Enlisted) EndEnlist(p, own, "scEncEnlistGone", "their body went");
            p.Leaving = false;
            p.LeaveTarget = Vector3.zero;
            Prospect pr = ProspectByPerson(p.Id);
            if (pr != null) Rekey(pr, ParkedKey(p), p.HasHome ? p.Home : pr.NpcPos);
        }

        // ------------------------------------------------------------------ enlisting ----

        /// <summary>The dukes enlisting costs at the enclave's standing now.</summary>
        public static int EnlistCost(Enclave e)
        {
            switch (ScEnclaves.StandingKey(e))
            {
                case "allied": return ScConfig.EnclaveEnlistCostAllied;
                case "friendly": return ScConfig.EnclaveEnlistCostFriendly;
                default: return ScConfig.EnclaveEnlistCostNeutral;
            }
        }

        /// <summary>The enlist row is there at all: a member (never the leader) at home, of an enclave that is not hostile.</summary>
        public static bool CanAskEnlist(EncPerson p)
        {
            Enclave e = ScEnclavePeople.EnclaveOf(p);
            return e != null && !p.IsLeader && p.State == EncPerson.StateHome && !e.IsHostile;
        }

        /// <summary>Why they cannot come now (null when they can): one of theirs with you already, the follower limit, the dukes.</summary>
        public static string EnlistBlocked(EncPerson p, EntityPlayer player)
        {
            if (!ScCommunity.IsHost) return Loc("scHostOnly");
            Enclave e = ScEnclavePeople.EnclaveOf(p);
            if (e == null) return Loc("scEncStays");
            if (EnlistedOf(e) != null) return Loc("scEncOneAlready");
            if (ScRegistry.FollowerCount + EnlistedCount() >= ScConfig.MaxFollowers) return Loc("scEncFollowersFull");
            int cost = EnlistCost(e);
            if (cost > 0 && DukesCarried(player as EntityPlayerLocal) < cost) return string.Format(Loc("scEncNeedDukes"), cost);
            return null;
        }

        /// <summary>
        /// "Deal.": the fee paid, then SC's hire shape (ScCommunity.FollowInternal's: leader and owner - the Follow order, hired_*
        /// and the leader cache with them - and Persist) and the enlisted cvar on the body; the record enlisted until world time
        /// now + EnclaveEnlistDays days. False with the reason (a tooltip) when it cannot be.
        /// </summary>
        public static bool Enlist(EntityPlayerLocal player, EntityAlive npc, out string reason)
        {
            reason = null;
            EncPerson p = ScEnclavePeople.PersonOf(npc);
            Enclave e = ScEnclavePeople.EnclaveOf(p);
            World w = ScCommunity.World;
            if (player == null || w == null || e == null || npc.IsDead() || npc.WorldTimeBorn != p.EntityBorn || !CanAskEnlist(p)) { reason = Loc("scEncStays"); return false; }
            reason = EnlistBlocked(p, player);
            if (reason != null) return false;
            int cost = EnlistCost(e);
            if (cost > 0 && !PayDukes(player, cost)) { reason = string.Format(Loc("scEncNeedDukes"), cost); return false; }
            int days = Math.Max(1, ScConfig.EnclaveEnlistDays);
            EnlistBody(p, npc, player, w.worldTime + (ulong)days * 24000UL);
            ScRegistry.SaveAtomic();
            ScCommunity.Tell(player, string.Format(Loc("scEncEnlistedTell"), p.Name, days), "");
            ScLog.Out("enclave " + e.Id + " '" + e.Name + "': " + p.Name + " (p" + p.Id + ", entity " + npc.entityId + ") enlisted with " + player.EntityName + " for "
                      + days + " day(s), " + cost + " dukes (" + ScEnclaves.StandingKey(e) + ") - home on day " + GameUtils.WorldTimeToDays(p.EnlistedUntil));
            return true;
        }

        /// <summary>
        /// The enlistment itself (Enlist's, after its fee; E9: a fighter an ally sent over the radio, ScEncCalls): SC's hire
        /// shape on the body (Hire) and the record enlisted with the player until that world time - their body, no walk home.
        /// The caller saves, tells and logs.
        /// </summary>
        internal static void EnlistBody(EncPerson p, EntityAlive npc, EntityPlayer player, ulong until)
        {
            Hire(npc, player);
            p.State = EncPerson.StateEnlisted;
            p.EnlistedBy = player.entityId;
            p.EnlistedBody = npc.entityId;
            p.EnlistedUntil = until;
            p.Leaving = false;
            p.LeaveTarget = Vector3.zero;
            ScCommunity.ResetStill(p.Who);
        }

        /// <summary>
        /// An enlistment over: the hire undone on the body when there is one (ScCommunity.Detach, the enlisted cvar, the
        /// enclave's own Dynamic source back), else the player's side of it alone; the record home again; with lead, a group
        /// left with nobody leading (its leader died meanwhile) gets this person as its leader - never for one who is dying or
        /// joining the community. tellKey: the player's tooltip ({0} the name, {1} the enclave), or null for none.
        /// </summary>
        public static void EndEnlist(EncPerson p, EntityAlive npc, string tellKey, string why, bool lead = true)
        {
            if (p == null || !p.Enlisted) return;
            World w = ScCommunity.World;
            EntityPlayer pl = p.EnlistedBy > 0 && w != null ? w.GetEntity(p.EnlistedBy) as EntityPlayer : null;
            Unhire(npc, pl, npc != null ? npc.entityId : p.EnlistedBody);
            p.State = EncPerson.StateHome;
            p.EnlistedBy = -1;
            p.EnlistedBody = -1;
            p.EnlistedUntil = 0;
            Enclave e = ScEnclavePeople.EnclaveOf(p);
            if (lead) ScEnclavePeople.EnsureLeader(e);
            if (tellKey != null && pl != null) ScCommunity.Tell(pl, string.Format(Loc(tellKey), p.Name, e != null ? e.Name : ""), "");
            ScLog.Out(p.Name + (e != null ? " of " + e.Name : "") + " is no longer with " + (pl != null ? pl.EntityName : "the player") + " (" + why + ")");
        }

        /// <summary>
        /// The hire shape on a body (Enlist's; E5: a missing survivor following the player home, ScEncSearch): SC's
        /// ScCommunity.FollowInternal shape - leader and owner (the Follow order, hired_* and the leader cache with them) and
        /// Persist - and the enlisted cvar, which keeps Voice Control's contract and billing off it.
        /// </summary>
        internal static void Hire(EntityAlive npc, EntityPlayer player)
        {
            ScCommunity.DropPath(npc);
            ScNpc.SetLeaderAndOwner(npc.entityId, player.entityId);
            npc.Buffs.SetCustomVar("Persist", 1f);
            npc.Buffs.SetCustomVar(EnlistedCvar, 1f);
        }

        /// <summary>
        /// The hire undone (EndEnlist's; E5's escort): on the body when there is one (ScCommunity.Detach, the enlisted cvar, the
        /// enclave's own Dynamic source back), else the player's side of it alone for the body that was hired (bodyId).
        /// </summary>
        internal static void Unhire(EntityAlive npc, EntityPlayer pl, int bodyId)
        {
            if (npc != null)
            {
                ScCommunity.Detach(npc, pl);
                npc.Buffs.RemoveCustomVar(EnlistedCvar);
                npc.SetSpawnerSource(EnumSpawnerSource.Dynamic);
            }
            else if (bodyId >= 0)
            {
                // the body is gone: the hire on the player's side alone (SCore's CheckForDanglingHires would drop a hired_* whose
                // entity is not loaded anyway; this ends it now)
                SphereCache.LeaderCache.Remove(bodyId);
                if (pl != null)
                {
                    DropCompanion(pl, bodyId);
                    pl.Buffs.RemoveCustomVar("hired_" + bodyId);
                    EntityUtilities.CheckForDanglingHires(pl.entityId);
                }
            }
        }

        /// <summary>E5: a body a save still recorded as following the player home (ScEncSearch): its hire comes off the player at the first tick.</summary>
        internal static void AddStaleHire(int bodyId)
        {
            if (bodyId >= 0 && !staleHires.Contains(bodyId)) staleHires.Add(bodyId);
        }

        /// <summary>A player's companion list without a body that is gone (a destroyed Unity object still has its id).</summary>
        private static void DropCompanion(EntityPlayer pl, int id)
        {
            CompanionGroup g = pl.Companions;
            if (g == null) return;
            for (int i = g.Count - 1; i >= 0; i--)
            {
                EntityAlive c = g[i];
                if ((object)c != null && c.entityId == id) g.Remove(c);
            }
        }

        /// <summary>
        /// "Go home now.", expiry, a side door, hostility: the enlistment ends and the body walks home (Steer) - not watched
        /// until it is back on its spot (ScEnclavePeople's tick hands it to TickLeaving).
        /// </summary>
        public static void SendHome(EncPerson p, EntityAlive npc, string tellKey, string why)
        {
            Enclave e = ScEnclavePeople.EnclaveOf(p);
            EndEnlist(p, npc, tellKey, why);
            World w = ScCommunity.World;
            if (npc == null || e == null || w == null || npc.IsDead()) return;
            StartWalkHome(w, e, p, npc);
        }

        /// <summary>
        /// The walk home (SendHome's; E5: a missing survivor whose request ended while their body was out) - not watched until
        /// it is back on its spot (ScEnclavePeople's tick hands it to TickLeaving).
        /// </summary>
        internal static void StartWalkHome(World w, Enclave e, EncPerson p, EntityAlive npc)
        {
            p.Leaving = true;
            p.LeavingSince = Time.unscaledTime;
            p.LeaveTarget = Vector3.zero;
            ScCommunity.ResetStill(p.Who);
            ScCommunity.DropPath(npc);
            Steer(w, e, p, npc, true);
        }

        /// <summary>The dialog's "Go home now." and Voice Control's spoken dismiss for an enlisted body. False when it is not one.</summary>
        public static bool GoHome(EntityAlive npc, string why)
        {
            EncPerson p = ScEnclavePeople.PersonOf(npc);
            if (p == null || !p.Enlisted || npc.WorldTimeBorn != p.EntityBorn) return false;
            SendHome(p, npc, "scEncHeadsHome", why);
            ScRegistry.SaveAtomic();
            return true;
        }

        /// <summary>ScEnclaves.TurnHostile, before the flip: one of theirs with the player goes home first, never turning on them at their side.</summary>
        public static void ReleaseEnlisted(Enclave e, string why)
        {
            if (e == null) return;
            foreach (EncPerson p in ScRegistry.EncPeople)
            {
                if (p.EnclaveId != e.Id || !p.Enlisted) continue;
                EntityAlive npc = ScEnclavePeople.OwnBody(p);
                if (npc != null && !npc.IsDead()) SendHome(p, npc, "scEncEnlistHostile", why);
                else EndEnlist(p, null, "scEncEnlistHostile", why);
            }
        }

        /// <summary>
        /// ScEnclavePeople's tick for an enlisted body (its days, its player, a side door). A side door is anything that took
        /// the leader off it: SCore's companion-window Dismiss (Leader 0, Persist 0, Biome, Wander), FarmHere or Loot - home,
        /// never a stranger. True when the record changed.
        /// </summary>
        public static bool TickEnlisted(World w, Enclave e, EncPerson p, EntityAlive npc)
        {
            EntityPlayer pl = p.EnlistedBy > 0 ? w.GetEntity(p.EnlistedBy) as EntityPlayer : null;
            if (pl == null) { SendHome(p, npc, null, "the player who enlisted them is gone"); return true; }
            if (w.worldTime >= p.EnlistedUntil) { SendHome(p, npc, "scEncEnlistOver", "their days with the player are up"); return true; }
            if ((int)npc.Buffs.GetCustomVar("Leader") != p.EnlistedBy) { SendHome(p, npc, "scEncHeadsHome", "dismissed through a side door (no Leader)"); return true; }
            return false;
        }

        /// <summary>
        /// ScEnclavePeople's tick for a body walking home (Leaving): arrived at the spot - watched again; else the next step;
        /// no progress for three times StuckSeconds, or LeaveMaxSeconds gone, and nobody sees them - the body goes (they are
        /// at their spot next time).
        /// </summary>
        public static void TickLeaving(World w, Enclave e, EncPerson p, EntityAlive npc)
        {
            Vector3 pos = npc.position;
            if (p.HasHome && ScRunners.XZ(pos, p.HomeCentre) <= ArrivedXZ && Mathf.Abs(pos.y - p.Home.y) <= ArrivedDy)
            {
                p.Leaving = false;
                p.LeaveTarget = Vector3.zero;
                ScCommunity.ResetStill(p.Who);
                p.Who.AwaySeconds = 0;
                ScLog.Verbose(p.Name + " of " + e.Name + " is back at their spot");
                return;
            }
            Steer(w, e, p, npc, false);
            Member m = p.Who;
            bool stuck = ScCommunity.StuckSample(m, npc) && m.StillSeconds >= ScConfig.StuckSeconds * 3;
            bool overdue = Time.unscaledTime - p.LeavingSince > LeaveMaxSeconds;
            if ((stuck || overdue) && !ScEnclavePeople.SeenByAny(w, pos))
            {
                ScLog.Out(p.Name + " of " + e.Name + " did not make it home on foot (" + (stuck ? "stuck" : "too long") + ") - the body goes, they are at their spot next time");
                ScEnclavePeople.Despawn(p, npc);
            }
        }

        /// <summary>
        /// The walk home: the Guard order at their spot while their POI is in reach (within EnclaveSpawnRange of the body, its
        /// chunks settled), else at a point up to LeaveStep m on toward it on loaded ground, the next one when it is reached.
        /// first: the walk starts now.
        /// </summary>
        private static void Steer(World w, Enclave e, EncPerson p, EntityAlive npc, bool first)
        {
            Vector3 pos = npc.position;
            bool reach = p.HasHome && e.DistanceXZ(pos.x, pos.z) <= ScConfig.EnclaveSpawnRange && ScEnclavePeople.ChunksSettled(w, e);
            if (reach)
            {
                if (!first && p.LeaveTarget == Vector3.zero) return;           // on its way to the spot already
                p.LeaveTarget = Vector3.zero;
                ScEnclavePeople.GuardAtSpot(p, npc);
                return;
            }
            if (!first && p.LeaveTarget != Vector3.zero && ScRunners.XZ(pos, p.LeaveTarget) > 3f) return;
            Vector3 next;
            if (!StepToward(w, pos, p.HasHome ? p.HomeCentre : e.Centre, out next)) return;
            p.LeaveTarget = next;
            Vector3 d = next - pos;
            d.y = 0f;
            ScEnclavePeople.GuardAt(npc, next, d.sqrMagnitude > 0.01f ? next + d.normalized * 4f : next + Vector3.forward);
        }

        /// <summary>A point up to LeaveStep m from 'from' toward 'to' in a loaded chunk, on the terrain (shorter steps when the far one is not loaded).</summary>
        private static bool StepToward(World w, Vector3 from, Vector3 to, out Vector3 at)
        {
            at = from;
            Vector3 d = to - from;
            d.y = 0f;
            float len = d.magnitude;
            if (len < 1f) return false;
            for (float step = Mathf.Min(LeaveStep, len); step >= 4f; step *= 0.5f)
            {
                Vector3 c = from + d / len * step;
                int x = Mathf.FloorToInt(c.x), z = Mathf.FloorToInt(c.z);
                if (w.GetChunkFromWorldPos(x, 0, z) == null) continue;
                at = new Vector3(x + 0.5f, w.GetTerrainHeight(x, z) + 1, z + 0.5f);
                return true;
            }
            return false;
        }

        /// <summary>People enlisted with the player now (MaxFollowers counts them with the community's followers).</summary>
        public static int EnlistedCount()
        {
            int n = 0;
            foreach (EncPerson p in ScRegistry.EncPeople) if (p.Enlisted) n++;
            return n;
        }

        /// <summary>The one of an enclave's people enlisted now, or null.</summary>
        public static EncPerson EnlistedOf(Enclave e)
        {
            if (e == null) return null;
            foreach (EncPerson p in ScRegistry.EncPeople) if (p.EnclaveId == e.Id && p.Enlisted) return p;
            return null;
        }

        /// <summary>An enlisted body, by its cvar (Voice Control's billing guard: one cvar read).</summary>
        public static bool IsEnlistedBody(EntityAlive e)
        {
            return e != null && e.Buffs != null && e.Buffs.HasCustomVar(EnlistedCvar);
        }

        /// <summary>{until} in the enlisted line: the day and hour they go home ("" when not enlisted).</summary>
        public static string UntilText(EncPerson p)
        {
            if (p == null || !p.Enlisted) return "";
            ulong t = p.EnlistedUntil;
            return string.Format(Loc("scEncUntil"), GameUtils.WorldTimeToDays(t), GameUtils.WorldTimeToHours(t).ToString("00") + ":" + GameUtils.WorldTimeToMinutes(t).ToString("00"));
        }

        /// <summary>{terms} in the enlist offer: the fee and the days at the enclave's standing now.</summary>
        public static string TermsText(Enclave e)
        {
            if (e == null) return "";
            int cost = EnlistCost(e), days = Math.Max(1, ScConfig.EnclaveEnlistDays);
            return cost > 0 ? string.Format(Loc("scEncTermsCost"), cost, days) : string.Format(Loc("scEncTermsFree"), days);
        }

        // ------------------------------------------------------------------ dukes ----

        private static XUiM_PlayerInventory Inventory(EntityPlayerLocal pl)
        {
            return pl != null && pl.PlayerUI != null && pl.PlayerUI.xui != null ? pl.PlayerUI.xui.PlayerInventory : null;
        }

        /// <summary>Vanilla's trade currency (traders.xml currency_item: casinoCoin), or null.</summary>
        private static ItemValue Currency()
        {
            string name = string.IsNullOrEmpty(TraderInfo.CurrencyItem) ? "casinoCoin" : TraderInfo.CurrencyItem;
            ItemValue v = ItemClass.GetItem(name);
            return v != null && !v.IsEmpty() ? v : null;
        }

        /// <summary>Dukes in the bag and on the toolbelt (where XUiM_PlayerInventory.RemoveItems takes them from).</summary>
        public static int DukesCarried(EntityPlayerLocal pl)
        {
            XUiM_PlayerInventory inv = Inventory(pl);
            ItemValue cur = Currency();
            return inv != null && cur != null ? inv.GetItemCount(cur) : 0;
        }

        /// <summary>
        /// The fee from the bag, then the toolbelt, measured as ScCategories.RemoveFromPlayer measures - what left the
        /// inventory, not what was asked (vanilla's removal can come up short) - and a short removal given back. True when paid.
        /// </summary>
        public static bool PayDukes(EntityPlayerLocal pl, int amount)
        {
            if (amount <= 0) return true;
            XUiM_PlayerInventory inv = Inventory(pl);
            ItemValue cur = Currency();
            if (inv == null || cur == null) return false;
            try
            {
                int before = inv.GetItemCount(cur);
                if (before < amount) return false;
                inv.RemoveItems(new[] { new ItemStack(cur.Clone(), amount) });
                int paid = Math.Max(0, before - inv.GetItemCount(cur));
                if (paid >= amount) return true;
                if (paid > 0 && !inv.AddItem(new ItemStack(cur.Clone(), paid))) ScLog.Warn("enlist fee: " + paid + " dukes could not be given back (a full bag)");
                ScLog.Warn("enlist fee: " + amount + " dukes asked, " + paid + " left the inventory - not enlisted, given back");
                return false;
            }
            catch (Exception e)
            {
                ScLog.Once("encdeals-pay", e);
                return false;
            }
        }

        // ------------------------------------------------------------------ recruiting ----

        /// <summary>The recruit rows are there at all: a member (never the leader), at home or enlisted, of an enclave that is not hostile.</summary>
        public static bool CanAskRecruit(EncPerson p)
        {
            Enclave e = ScEnclavePeople.EnclaveOf(p);
            return e != null && !p.IsLeader && p.Alive && !e.IsHostile;
        }

        /// <summary>Days until the enclave lets another of its people go (0: now).</summary>
        public static int CooldownLeft(Enclave e)
        {
            if (e == null || e.LastRecruitDay < 0 || ScConfig.EnclaveRecruitCooldownDays <= 0) return 0;
            return Math.Max(0, e.LastRecruitDay + ScConfig.EnclaveRecruitCooldownDays - ScCommunity.Today);
        }

        /// <summary>
        /// Why they cannot join now (null when they can): SC's usual gates first (CanRecruit's enclave path: a grudge, someone
        /// else's hire, a full roster ...; a home), then the enclave's - its group still being made, EnclaveRecruitMinLeft
        /// living left afterwards, one every EnclaveRecruitCooldownDays. The favour is not a gate here (NeedsFavour).
        /// </summary>
        public static string RecruitBlocked(EncPerson p, EntityAlive npc, EntityPlayer player)
        {
            if (!ScCommunity.IsHost) return Loc("scHostOnly");
            Enclave e = ScEnclavePeople.EnclaveOf(p);
            if (e == null || npc == null) return Loc("scEncStays");
            string why;
            if (!ScCommunity.CanRecruit(npc, player, out why, true)) return Loc(why ?? "scNotHireable");
            if (!ScZone.EnsureHome(player)) return Loc("scNeedHome");
            if (!e.PeopleRolled) return Loc("scEncRecruitSettling");
            if (ScEnclavePeople.Living(e).Count - 1 < Math.Max(1, ScConfig.EnclaveRecruitMinLeft)) return Loc("scEncRecruitFew");
            int wait = CooldownLeft(e);
            if (wait > 0) return string.Format(Loc("scEncRecruitSoon"), wait);
            return null;
        }

        /// <summary>At Neutral their own favour comes first, until it is done (ScFavours.CheckDone marks it the moment its quest is).</summary>
        public static bool NeedsFavour(EncPerson p, EntityAlive npc, EntityPlayer player)
        {
            Enclave e = ScEnclavePeople.EnclaveOf(p);
            if (e == null || ScEnclaves.StandingKey(e) != "neutral") return false;
            return !ScFavours.CheckDone(npc, player, ProspectByPerson(p.Id));
        }

        /// <summary>{recruitcost} in the recruit offer: what the join costs with the enclave at its standing now.</summary>
        public static string RecruitCostText(Enclave e)
        {
            if (e == null) return "";
            int cost = ScEnclaves.StandingKey(e) == "allied" ? 0 : ScConfig.EnclaveRecruitStanding;
            return cost > 0 ? string.Format(Loc("scEncRecruitCost"), e.Name, cost) : string.Format(Loc("scEncRecruitFree"), e.Name);
        }

        /// <summary>
        /// "Welcome to the community.": every gate again, the favour at Neutral; one enlisted with the player is released
        /// first (D9: a hire may join). Their own Member record joins - the face, skills, traits, name and specialty they have,
        /// the body's id and birth, the pair and rounds the body carries now (as MemberFromBody reads them) - after they left
        /// the enclave's records (ScEnclavePeople.LeaveEnclave); then the standing cost, the cooldown, a ledger line, the log,
        /// a save. False with the reason (a tooltip).
        /// </summary>
        public static bool Recruit(EntityPlayerLocal player, EntityAlive npc, out string reason)
        {
            reason = null;
            EncPerson p = ScEnclavePeople.PersonOf(npc);
            Enclave e = ScEnclavePeople.EnclaveOf(p);
            if (player == null || e == null || npc.IsDead() || npc.WorldTimeBorn != p.EntityBorn || !CanAskRecruit(p)) { reason = Loc("scEncStays"); return false; }
            reason = RecruitBlocked(p, npc, player);
            if (reason != null) return false;
            if (NeedsFavour(p, npc, player)) { reason = Loc("scFavourNotDone"); return false; }
            string standing = ScEnclaves.StandingKey(e);
            int cost = standing == "allied" ? 0 : ScConfig.EnclaveRecruitStanding;
            if (p.Enlisted) EndEnlist(p, npc, null, "joining the community", false);
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
            ScEnclavePeople.LeaveEnclave(p, npc);
            string error;
            if (!ScCommunity.Admit(m, npc, player, out error))
            {
                // they left the enclave already (its records, cvars and veto): a stranger now rather than a person in two places,
                // kept in the world's save as strangers with a favour are, not lost at the next chunk save (the E3 review's L3)
                npc.SetSpawnerSource(EnumSpawnerSource.StaticSpawner);
                npc.Buffs.SetCustomVar("Persist", 1f);
                ScRegistry.SaveAtomic();
                ScLog.Err(m.Name + " left " + e.Name + " to join the community, but the join failed (" + (error ?? "?") + ") - they are a stranger now");
                reason = Loc(error ?? "scInternal");
                return false;
            }
            e.LastRecruitDay = ScCommunity.Today;
            if (cost > 0) ScEnclaves.AddStanding(e, -cost, m.Name + " left them to join " + player.EntityName);
            ScOutposts.NoteLedger(string.Format(Loc("scEncLedgerRecruited"), m.Name, e.Name));
            ScLog.Out("enclave " + e.Id + " '" + e.Name + "': " + m.Name + " (p" + p.Id + ", entity " + npc.entityId + ") left to join the community (" + standing
                      + (cost > 0 ? ", -" + cost + " standing" : ", free") + ") - the next one can go from day " + (e.LastRecruitDay + ScConfig.EnclaveRecruitCooldownDays));
            ScRegistry.SaveAtomic();
            return true;
        }

        // ------------------------------------------------------------------ favours ----

        /// <summary>The prospect's registry key while the person has no body: no entity id ever reaches it, and never -1 (any NPC would be the giver).</summary>
        public static int ParkedKey(EncPerson p) { return int.MaxValue - p.Id; }

        /// <summary>The person's favour, wherever it is keyed now (a scan: a few prospects at most).</summary>
        public static Prospect ProspectByPerson(int personId)
        {
            if (personId <= 0) return null;
            foreach (Prospect pr in ScRegistry.Prospects.Values) if (pr.EncPersonId == personId) return pr;
            return null;
        }

        /// <summary>
        /// The favour of one of an enclave's people, rolled the first time the player asks at Neutral (ScFavours.Roll, as a
        /// wild survivor's) - never pinned: no Persist, no StaticSpawner, no wait order (their enclave keeps them at their
        /// spot) - and keyed by their body now (OnDressed / OnUnbodied keep it on the current one).
        /// </summary>
        public static Prospect EnsureFavour(EncPerson p, EntityAlive npc)
        {
            if (p == null || npc == null) return null;
            Prospect pr = ProspectByPerson(p.Id);
            if (pr != null) return pr;
            if (ScRegistry.Prospects.ContainsKey(npc.entityId)) return null;          // another's record under this id: never overwritten
            ScConfig.EnsureParsed();
            pr = new Prospect { Id = npc.entityId, Class = p.Who.Class, Name = p.Name, EncPersonId = p.Id, NpcPos = npc.GetBlockPosition() };
            World w = ScCommunity.World;
            ScFavours.Roll(pr, npc.position, npc.rand ?? (w != null ? w.GetGameRandom() : null));
            ScRegistry.Prospects[pr.Id] = pr;
            ScRegistry.SaveAtomic();
            ScLog.Out(p.Name + " of enclave " + p.EnclaveId + " asks for a favour first: " + ScFavours.Describe(pr));
            return pr;
        }

        /// <summary>A favour under a new key (a new body, or parked), its quest's giver with it; where: the return marker's place.</summary>
        private static void Rekey(Prospect pr, int key, Vector3i where)
        {
            pr.NpcPos = where;
            if (pr.Id == key) return;
            Prospect other;
            if (ScRegistry.Prospects.TryGetValue(key, out other) && other != pr)
            {
                ScLog.Warn("favour of " + pr.Name + ": the key " + key + " holds another prospect - left on " + pr.Id);
                return;
            }
            int old = pr.Id;
            ScRegistry.Prospects.Remove(old);
            pr.Id = key;
            ScRegistry.Prospects[key] = pr;
            EntityPlayerLocal pl = ScCommunity.LocalPlayer;
            if (pl != null) ScQuests.RekeyFavour(pl, old, key);
        }

        /// <summary>
        /// E11: their group moved to another building (ScEncMoves - none of their bodies is out, so every favour of theirs is
        /// parked): each favour's return point (Prospect.NpcPos: ScQuests.GiverPosition, the quest marker TickMarkers moves) goes
        /// to the new building; their next body there takes the favour as before (OnDressed). Returns how many.
        /// </summary>
        internal static int OnMoved(Enclave e, Vector3i at)
        {
            if (e == null) return 0;
            int n = 0;
            foreach (EncPerson p in ScRegistry.EncPeople)
            {
                if (p.EnclaveId != e.Id) continue;
                Prospect pr = ProspectByPerson(p.Id);
                if (pr == null) continue;
                pr.NpcPos = at;
                n++;
            }
            return n;
        }

        /// <summary>The person's favour is off (they died, their enclave is gone): its quest closed as failed, the prospect dropped.</summary>
        public static void DropFavour(EncPerson p, string why)
        {
            Prospect pr = p != null ? ProspectByPerson(p.Id) : null;
            if (pr == null) return;
            EntityPlayerLocal pl = ScCommunity.LocalPlayer;
            if (pl != null) ScQuests.CloseFavour(pl, pr.Id, false);
            ScRegistry.Prospects.Remove(pr.Id);
            ScLog.Out("the favour of " + p.Name + " is off (" + why + ")");
        }

        // ------------------------------------------------------------------ console ----

        /// <summary>sc enclave trade &lt;id&gt;: the trader id, the leader, the stock and its restock clock, the prices now.</summary>
        public static void TradeLines(Enclave e, List<string> into)
        {
            int want = TradeIdOf(e), id = TraderIdFor(e);
            EncPerson leader = ScEnclavePeople.LeaderOf(e);
            EntityAlive body = leader != null ? ScEnclavePeople.OwnBody(leader) : null;
            EntityTrader t = body as EntityTrader;
            TraderData d = t != null && t.TraderData != null && t.TraderData.TraderID >= 0 ? t.TraderData : DecodeStock(e.Stock);
            into.Add("#" + e.Id + " " + e.Name + ": " + (e.IsHostile ? "hostile - does not trade" : "trader_info " + want + " (" + (e.Kind.Length > 0 ? e.Kind : "general") + ")"
                     + (id < 0 ? " NOT DEFINED (Config/traders.xml or EnclaveTraderIdBase)" : "")) + ", leader " + (leader != null ? leader.Name + (body != null ? " (entity " + body.entityId
                     + (t != null && Patches_Xnpc.Patch_NoTalkToRaiders.HasTrader(t) ? ", trading" : ", not trading") + ")" : " (no body out)") : "none"));
            byte[] rec = ScRunners.Unpack(e.Stock);
            string record = e.Stock.Length == 0 ? "empty" : rec != null ? rec.Length + " bytes" : "unreadable";
            if (d == null) { into.Add("  no stock yet: the first trade stocks them (record " + record + ")"); return; }
            int items = 0;
            foreach (TraderData.Entry en in d.PrimaryInventory) if (en != null && en.Item != null) items += en.Item.count;
            ulong last = d.lastInventoryUpdate;
            string when = last == 0 ? "never" : "day " + GameUtils.WorldTimeToDays(last) + " " + GameUtils.WorldTimeToHours(last).ToString("00") + ":" + GameUtils.WorldTimeToMinutes(last).ToString("00");
            // NextResetTime indexes TraderInfo by the record's id unguarded: a corrupt or edited stock= must not throw here
            ulong next = id >= 0 && d.TraderID == id ? d.NextResetTime : 0;
            into.Add("  stock: " + d.PrimaryInventory.Count + " slot(s), " + items + " item(s), restocked " + when + (next > 0 && last > 0 ? ", next from day " + GameUtils.WorldTimeToDays(next) : "")
                     + (body != null ? " (read from the leader's body)" : " (read from the record)") + "; record " + record);
            if (!e.IsHostile)
                into.Add("  prices at " + ScEnclaves.StandingWord(e) + ": they sell at x" + Factor(e, true).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
                         + ", they buy at x" + Factor(e, false).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " (your barter perks on top)");
        }

        /// <summary>sc enclave release &lt;id&gt;: the enclave's enlisted person goes home now.</summary>
        public static string ReleaseNow(Enclave e)
        {
            if (!ScCommunity.IsHost) return "host only";
            EncPerson p = EnlistedOf(e);
            if (p == null) return "nobody of " + e.Name + " is with you";
            EntityAlive npc = ScEnclavePeople.OwnBody(p);
            if (npc != null && !npc.IsDead()) SendHome(p, npc, "scEncHeadsHome", "the console");
            else EndEnlist(p, null, "scEncHeadsHome", "the console");
            ScRegistry.SaveAtomic();
            return p.Name + " goes home (" + (npc != null ? "walking" : "no body out: home at once") + ")";
        }
    }
}
