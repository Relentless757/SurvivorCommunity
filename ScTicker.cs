using System;
using System.Collections.Generic;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// The host's once-a-second heartbeat (ModEvents.GameUpdate): day rollover, member relink, pending
    /// stationing and secure-quest sync. There is no day-change event in the game, so the day is polled.
    /// </summary>
    public static class ScTicker
    {
        private const float Interval = 1f;
        /// <summary>
        /// Real seconds SC's own Follow order has to land before a Guard still on the follower stops being SC's leftover
        /// and counts as the player's "guard and return" (Member.FollowIssued). buffOrderFollow starts on the NPC's next
        /// buff update, well inside one relink; the margin only covers a hitch.
        /// </summary>
        private const float FollowConfirmSeconds = 5f;
        private static float nextTick;
        private static readonly List<int> scratch = new List<int>();
        /// <summary>
        /// The day "sc day" last simulated (ConsoleCmdSc.Day runs ScDaily for LastDay + 1, ahead of the world): the resync
        /// that follows a second later is then SC's own, and says so quietly instead of warning. -1 = none pending.
        /// </summary>
        public static int SimulatedDay = -1;
        /// <summary>The world's day when "sc day" ran: the quiet resync needs both, so a real jump right after it still warns.</summary>
        public static int SimulatedFromDay = -1;

        public static void Reset()
        {
            nextTick = 0f;
            SimulatedDay = -1;
            SimulatedFromDay = -1;
        }

        public static void OnGameUpdate()
        {
            try
            {
                float now = Time.unscaledTime;
                if (now < nextTick) return;
                nextTick = now + Interval;

                if (!ScCommunity.IsHost) return;
                // before the local-player test below, so a dedicated server sweeps too: a dead NPC's window a remote
                // player held drops at the body once its open mark has expired (Patches_Npc)
                Patches_Npc.Patch_SetDead.ReleaseAll(false);
                World w = ScCommunity.World;
                if (w == null || !ScRegistry.Loaded) return;
                EntityPlayerLocal player = ScCommunity.LocalPlayer;
                if (player == null) return;
                ScConfig.EnsureParsed();

                int day = GameUtils.WorldTimeToDays(w.worldTime);
                Community c = ScRegistry.Community;
                if (c.LastDay < 0)
                {
                    c.LastDay = day;                       // baseline: the first observed day is not charged
                }
                else if (day < c.LastDay)
                {
                    // the world clock went backwards (an older save loaded, or a time command): without this the
                    // community is frozen until the world catches up - no consumption, no morale, and supply
                    // runs never come home. "sc day" runs ahead of the world on purpose: its own day ending is no fault.
                    if (c.LastDay == SimulatedDay && day == SimulatedFromDay) ScLog.Out("sc day: the simulated day " + c.LastDay + " is over - back to the world's day " + day);
                    else
                    {
                        ScLog.Warn("the world day went backwards (" + c.LastDay + " -> " + day + "); resyncing the community to day " + day);
                        // E4/E6: the request clock and every forward-only enclave stamp follow a real jump back (never sc day's own end);
                        // within a second of "sc day" the community's day is still the simulated one: the world went back from its own
                        // day (the E6 review's N1)
                        ScEnclaves.OnWorldDayBack(day, c.LastDay == SimulatedDay && SimulatedFromDay >= 0 ? SimulatedFromDay : c.LastDay);
                    }
                    SimulatedDay = -1;
                    c.LastDay = day;
                    foreach (var m in ScRegistry.Members.Values)
                        if (m.Status == MemberStatus.OnRun && m.RunEndDay > day) m.RunEndDay = day;   // land the runs that were already due
                    ScRegistry.SaveAtomic();
                }
                else
                {
                    SimulatedDay = -1;      // the world has caught up with any simulated day: no resync of ours is coming
                    int guard = 0;
                    while (c.LastDay < day && guard++ < 60) { c.LastDay++; ScDaily.Run(c.LastDay, player); }
                }

                Relink(w, player);
                ScAmmo.Tick(w);                       // every 5 s: each member's ammo into the record; resupply in the base / at an outpost
#if SC_V34
                ScWork.Sweep(player);                 // C1: work shifts whose member left, moved on or unloaded are dropped
#endif
                ScDoors.Tick(w, player);              // shut the doors members opened once they are through
                ScRunners.Tick(w, player);            // physical supply runs: walk, capture out of range, re-create near the player
                TrackProspects(w);
                ScQuests.SyncCompleted(player);
                ScQuests.TickMarkers(player);
                ScScan.RunIfDue(15f);                  // beds / facilities / crops from the loaded chunks, so the HUD morale follows what you build
                ScNoise.Tick(w, player);               // the base's noise (what was heard in the zone) and the dead's attention
                ScActivities.Tick(player);             // the round of drinks: its real-time countdown (round wl)
                ScMorale.SyncHud(player);
                ScLeadership.Publish(player);          // sc_followers / sc_inzone / sc_morale_high for SurvivorOrigins' Leadership
                ScRadio.Tick(w, player);
#if SC_V34
                ScRadioTalk.Tick(w, player);          // R2: runners with a handheld radio - leaving home, leaving the site, the next line on the radio
                ScRunnerCall.Tick(w, player);         // round 29b: a runner's call for help when attacked - the marker follows them, the call ends with the fight
#endif
                ScOutposts.Tick(w, player);           // radio report-in stamp, claim check, markers (every 10 s)
                ScHomeLink.Tick(player);              // an outpost's door into the home storage: load, open, release
                ScHomeHold.Tick(w);                   // the home zone held loaded while a player is near (round hz)
                ScGarrison.Tick(w, player);           // stand-ins at the staffed outpost the player is at, visit stamps
                ScScouting.Tick(player);              // radio scouting marks: fade, restore after a load
                ScEnclaves.Tick(w, player);           // enclaves found within reach, their markers restored after a load (E1)
                ScEnclavePeople.Tick(w, player);      // their people: made, spawned near a player, gone when none is, the watchdog (E2)
                ScBands.Tick(w, player);              // bands on the road: bodies near a player, their walk, camp, trouble, markers (E13a)
                ScEncRequests.Tick(w, player);        // their requests: the journal squared with the records once a session (E4)
                ScEncCalls.Tick(w, player);           // allies' radio calls: a fighter's arrival and return after a load, a supply drop (E9)
                ScInfestations.Tick(w, player);       // infestations found within reach, their markers restored after a load (I1)
                ScOutposts.TickFlags(player);         // Outpost Flags placed since the last second establish themselves
                ScOutpostRisk.Tick(w, player);        // attack fights and deadlines, blood-moon sieges
                ScBaseAttack.Tick(w, player);         // the home's horde: the call at full attention, the arrival, the fight (with or without the player)
                ScSiege.Tick(w, player);              // a blood moon passed with the player away besieges the base
                ScWatch.Tick(w, player);              // watch posts: shifts, the zone scan, a guard's call-outs and marker
                ScMissing.Tick(w, player);            // residents whose body is gone from the world (Call back)
                ScEncJoin.Tick(w, player);            // a whole enclave that joined: their bodies walk off, each turns up at home when due (E12)
                ScStreamerNames.Tick();               // round 30: the streamer names given in the last second into the save
            }
            catch (Exception e)
            {
                ScLog.Once("ticker", e);
            }
        }

        /// <summary>Remembers where each prospect is while they are loaded (the return marker when they are not).</summary>
        private static void TrackProspects(World w)
        {
            if (ScRegistry.Prospects.Count == 0) return;
            bool changed = false;
            foreach (var p in ScRegistry.Prospects.Values)
            {
                // an enclave person's favour (E3) follows the person, whose body roams or follows the player: its giver
                // position comes from the live body or their home spot, never from here (the E3 review's L2: a registry
                // write for every block they walk)
                if (p.EncPersonId > 0) continue;
                EntityAlive npc = ScNpc.As(ScNpc.Loaded(w, p.Id));
                if (npc == null || npc.IsDead()) continue;
                Vector3i at = npc.GetBlockPosition();
                if (at != p.NpcPos) { p.NpcPos = at; changed = true; }
            }
            if (changed) ScRegistry.SaveAtomic();
        }

        /// <summary>
        /// Every loaded member is re-stamped each second: the spawner source (PostInit resets it to Biome on
        /// load), Persist, the cvar mirror, the AI package (rebuilt from the class on load), the guard order
        /// for residents, and - for followers whose leader id went stale across a relog - the leader link. A
        /// follower's order is read as a member state (ScOrders), not repaired: voice and order-wheel orders are
        /// the player's.
        /// </summary>
        public static void Relink(World w, EntityPlayerLocal player)
        {
            scratch.Clear();
            // entities whose record was purged or left while unloaded: strip the mod's leftovers once they load
            if (ScRegistry.Cleanup.Count > 0)
            {
                foreach (int id in ScRegistry.Cleanup)
                {
                    EntityAlive stale = ScNpc.As(ScNpc.Loaded(w, id));    // still being built: stays listed for a later tick
                    if (stale == null) continue;
                    // an id can be inherited by a stranger after an id-counter rewind: only strip an entity
                    // that actually carries the mod's marks, and drop the entry either way
                    if (ScCommunity.CarriesOurMarks(stale)) ScCommunity.CleanupStale(stale);
                    scratch.Add(int.MinValue + id);   // marker: remove from the cleanup set below
                }
            }
            // survivors who died on a supply run while unloaded: their hidden entity goes when it loads
            if (ScRegistry.Doomed.Count > 0)
            {
                foreach (int id in ScRegistry.Doomed)
                {
                    EntityAlive gone = ScNpc.As(ScNpc.Loaded(w, id));     // the same
                    if (gone == null) continue;
                    if (ScCommunity.CarriesOurMarks(gone)) ScRuns.RemoveEntity(gone);
                    scratch.Add(int.MinValue / 4 + id);   // marker: remove from the doomed set below
                }
            }
            foreach (var kv in ScRegistry.Members)
            {
                Member m = kv.Value;
                // E12: one of a whole enclave still on the way has no body until they turn up (ScEncJoin); their key is no entity's
                if (m.Status == MemberStatus.Arriving) continue;
                // every member every second: forcing a body a chunk just queued built it inside this frame (the hitch)
                EntityAlive npc = ScNpc.As(ScNpc.Loaded(w, m.Id));
                if (npc == null)
                {
                    continue;   // unloaded, still being built (linked next tick), or gone - the daily tick tracks MissingDays
                }
                if (npc.IsDead()) continue;   // SetDead postfix handles it
                if (ScRunners.IsPhysical(m) && m.Offline)
                {
                    // the snapshot IS this runner: an entity under the id is an old chunk copy (hidden, then removed by
                    // ScRunners) or a stranger who inherited the id (left alone - the runner returns under a new id)
                    if (ScRunners.IsStaleCopy(m, npc))
                    {
                        if (!npc.Buffs.HasCustomVar("onMission")) ScNpc.SendOnMission(npc, true);
                        ScRunners.StaleIds.Add(m.Id);
                    }
                    continue;
                }

                // Entity ids are stable in a session and restored from the world save, but a crash between a
                // join and the next world save rewinds the id counter: the class AND the birth time must match.
                string cls = ScCommunity.ClassName(npc);
                bool classMismatch = !string.IsNullOrEmpty(m.Class) && !string.Equals(cls, m.Class, StringComparison.OrdinalIgnoreCase);
                bool bornMismatch = m.Born != 0 && npc.WorldTimeBorn != 0 && npc.WorldTimeBorn != m.Born;
                if (classMismatch || bornMismatch)
                {
                    ScLog.Warn("member " + m.Name + " (id " + m.Id + ") is not the entity that joined (" + (classMismatch ? "class " + cls : "different birth time") + ") - the entity id was reused; dropping the member");
                    scratch.Add(m.Id);
                    continue;
                }
                if (m.Born == 0) m.Born = npc.WorldTimeBorn;
                m.MissingDays = 0;

                // A SCore Dismiss (the vanilla dialog entry or the companion window's button) writes
                // Persist=0 and zeroes the Leader AND Owner cvars: that is the player letting a follower go,
                // not a lost link to repair. Read the cvars, not GetLeaderOrOwner - SCore's leader cache
                // still answers with the player for a few seconds after a dismiss. After a relog the Owner
                // cvar keeps the stale player id (> 0) and Persist stays 1, so that case falls through to
                // the re-hire below. Detect it BEFORE anything re-stamps the member. Voice Control's orders never
                // match this (they never write Leader, Owner or Persist; SCore's Loot drops Leader but keeps Owner),
                // and its spoken "dismiss" is a Go Home for members (Patches_VoiceControl).
                if ((m.Status == MemberStatus.Following || m.Status == MemberStatus.PendingStation)
                    && npc.Buffs.HasCustomVar("Persist") && npc.Buffs.GetCustomVar("Persist") < 1f
                    && npc.Buffs.GetCustomVar("Leader") <= 0f && npc.Buffs.GetCustomVar("Owner") <= 0f)
                {
                    ScLog.Out(m.Name + " was dismissed through SCore - leaving the community");
                    scratch.Add(-m.Id);     // negative = leave (handled below, outside the enumeration)
                    continue;
                }

                if (npc.GetSpawnerSource() != EnumSpawnerSource.StaticSpawner) npc.SetSpawnerSource(EnumSpawnerSource.StaticSpawner);
                if (!npc.Buffs.HasCustomVar("Persist") || npc.Buffs.GetCustomVar("Persist") < 1f) npc.Buffs.SetCustomVar("Persist", 1f);
                if (!npc.Buffs.HasCustomVar("sc_member") || npc.Buffs.GetCustomVar("sc_owner") != player.entityId)
                    ScCommunity.StampCvars(m, npc, player);
                Patches_VoiceControl.StripContract(npc);   // members are never billed (0-XNPCCore 3.2's Voice Control)
                ScCommunity.ApplyAiPackage(npc);
                ScCommunity.StampFollowing(m, npc);   // keeps the dialog's order options in step with the status
                ScSkills.StampCvars(m, npc);          // levels and derived bonuses (daily XP changes them while loaded)
                ScTraits.ApplyBuffs(m, npc);
                ScLooks.Sync(m, npc);                 // player-body survivors: equipment is never saved for NPCs
                ScPortraits.OnMemberLoaded(m, npc);   // pack NPCs can only be pictured while loaded (and near)
                ScSkills.SyncStatusBuffs(m, npc);

                switch (m.Status)
                {
                    case MemberStatus.Resident:
                        if (npc.Buffs.HasCustomVar("onMission") || !npc.enabled) ScNpc.SendOnMission(npc, false);   // never a hidden resident
#if SC_V34
                        // C1: on a work shift she has an owner and a craft spot on purpose - ScWork ticks her instead of the rules below;
                        // round 29b: the stuck rescue still runs where her job allows it (ScStuck.OnShift)
                        if (ScWork.Resident(m, npc, player)) { ScStuck.OnShift(m, npc); break; }
#endif
                        if (EntityUtilities.GetCurrentOrder(m.Id) != EntityUtilities.Orders.Guard
                            || EntityUtilities.GetLeaderOrOwner(m.Id) != null)
                        {
                            ScLog.Verbose(m.Name + ": re-applying the resident station (order " + EntityUtilities.GetCurrentOrder(m.Id) + ")");
                            ScCommunity.Station(m, npc);
                        }
                        else if (ScWatch.OnWatch(m))
                        {
                            // a guard on watch (base defence D3): the post is a Watch post - no load check, self-heal or
                            // watchdog (each would hand out a ground post); ScWatch keeps the post, the facing and the sight,
                            // and places a guard that does not get there
                            ScWatch.TickGuard(m, npc, player);
                        }
                        else
                        {
                            // a post that is no real post (one chosen before the M11 fix 2 may sit on furniture) is
                            // replaced once per session, as soon as its chunk is loaded
                            ScCommunity.CheckPostOnce(m, npc);
                            // self-heal the post: SCore's GuardHere/GuardThere minevents (pathing cubes, order
                            // buffs) may rewrite the guard position with the NPC's own spot
                            Vector3 post = ScCommunity.PostCenter(m);
                            if (ScNpc.GuardPosition(npc) != post) ScCommunity.ApplyPost(npc, post);
                            ScCommunity.Watchdog(m, npc);
                        }
                        break;
                    case MemberStatus.Following:
                    case MemberStatus.PendingStation:
                    {
                        // Orders on a follower are the player's (decision D6): 0-XNPCCore's voice and order-wheel commands
                        // write SCore orders - Stay, Guard here, Guard and return (9, the resident's own order), Wander, a
                        // route, Patrol, Loot - and every one of them is a member STATE, kept as it is: the member is still
                        // following, holding where they were told until called back (ScOrders). Only an order nothing ever
                        // gives a follower (None, Hire, a runner's Task, or no order cvar at all) is broken and repaired.
                        EntityUtilities.Orders order;
                        bool hasOrder = ScOrders.TryCurrent(npc, out order);
                        bool held = hasOrder && ScOrders.IsHeld(order);
                        float now = Time.unscaledTime;
                        // SC's own Follow (join, the Follow verb, a re-link) lands a tick later through buffOrderFollow:
                        // until the NPC has been seen on it, a Guard still on them is the favour wait or the old station,
                        // which leaves the member guarding a spot they were never told to hold - that one is SC's, not the
                        // player's, and repaired
#if SC_V34
                        // C1: a 0-XNPCCore job order (craft, loot here ...) writes Guard too - never SC's leftover
                        bool ownGuard = m.FollowIssued > 0f && now - m.FollowIssued < FollowConfirmSeconds && order == EntityUtilities.Orders.Guard
                                        && npc.Buffs.GetCustomVar("SdcsLootMode") == 0f;
#else
                        bool ownGuard = m.FollowIssued > 0f && now - m.FollowIssued < FollowConfirmSeconds && order == EntityUtilities.Orders.Guard;
#endif
                        if (EntityUtilities.GetLeaderOrOwner(m.Id) == null)
                        {
                            // the leader id went stale (relog?): re-link, but a held voice order stays held
                            bool follow = !held || order == EntityUtilities.Orders.Follow || ownGuard;
                            ScLog.Verbose(m.Name + ": leader link lost (relog?) - re-hiring to " + player.entityId + (follow ? "" : ", keeping the order " + order));
                            ScNpc.SetLeaderAndOwner(m.Id, player.entityId, follow);
                            if (follow) m.FollowIssued = Mathf.Max(0.001f, now);
                        }
                        else if (!held || ownGuard)
                        {
                            ScLog.Out(m.Name + ": following but the order is " + (hasOrder ? order.ToString() : "unset") + " - re-applying Follow");
                            npc.Buffs.RemoveBuff("buffOrderGuard");
                            npc.Buffs.SetCustomVar("CurrentOrder", (float)EntityUtilities.Orders.Follow);
                            ScNpc.SetLeaderAndOwner(m.Id, player.entityId);
                            m.FollowIssued = Mathf.Max(0.001f, now);
                            order = EntityUtilities.Orders.Follow;
                        }
                        if (order == EntityUtilities.Orders.Follow || (m.FollowIssued > 0f && now - m.FollowIssued >= FollowConfirmSeconds)) m.FollowIssued = 0f;
                        if (ScOrders.IsHeld(order) && order != m.HeldOrder)
                        {
                            // logged once per change, so a voice order can be traced (probe P5); never a reason to leave
                            ScLog.Out(m.Name + " now holds the order " + order + " (" + ScOrders.Describe(npc) + ") - still " + m.Status);
                            m.HeldOrder = order;
                        }
                        if (ScOrders.WithLeader(order)) ScSkills.OnFollowTick(m, npc);   // Cardio: the road builds stamina (not while held)
                        // Home with the player: only a member who is actually with them (or already inside the zone) is
                        // stationed - one told to stay 2 km out would otherwise be teleported to their post the moment the
                        // player walks in. They stay "heading home, holding" until called or fetched.
                        if (m.Status == MemberStatus.PendingStation && ScZone.Contains(player.GetBlockPosition()) && ScZone.HomeLoaded
                            && (ScOrders.WithLeader(order) || ScZone.Contains(npc.GetBlockPosition())))
                        {
                            ScCommunity.Station(m, npc);
                            ScCommunity.Tell(player, string.Format(Localization.Get("scStationed"), m.Name), "");
                            ScRegistry.SaveAtomic();
                        }
                        break;
                    }
                    case MemberStatus.OnRun when ScRunners.IsPhysical(m):
                        // a physical runner is out in the world, walking: never hidden, always on the travel order
                        if (npc.Buffs.HasCustomVar("onMission") || !npc.enabled) ScNpc.SendOnMission(npc, false);
                        if (!ScRunners.HasTravelOrder(npc)) ScRunners.ApplyTravelOrder(npc);
                        break;
                    case MemberStatus.OnRun:
                    case MemberStatus.Garrison:
                        // hidden NPCs come back visible from a chunk load; keep them hidden (and parked) until the run
                        // resolves / the posting ends (a posted member is bookkeeping at the outpost, the body waits home)
                        if (!npc.Buffs.HasCustomVar("onMission") || npc.enabled) { ScRuns.Park(npc); ScNpc.SendOnMission(npc, true); }
                        break;
                }
            }
            for (int i = 0; i < scratch.Count; i++)
            {
                int id = scratch[i];
                // markers: cleanup = MinValue + id (lowest), doomed = MinValue/4 + id, leave = -id, drop = id
                if (id <= int.MinValue / 2) ScRegistry.Cleanup.Remove(id - int.MinValue);
                else if (id <= int.MinValue / 8) ScRegistry.Doomed.Remove(id - int.MinValue / 4);
                else if (id < 0) ScCommunity.Leave(ScRegistry.GetMember(-id), "scLeft");   // saves itself
                else { ScRegistry.Members.Remove(id); ScLooks.Forget(id); }
            }
            if (scratch.Count > 0) ScRegistry.SaveAtomic();
            scratch.Clear();
        }
    }

    /// <summary>
    /// The day rollover. In M1 it only keeps the ledger and the missing-member bookkeeping; consumption,
    /// morale, runs and specialties are added by the later milestones in this same method.
    /// </summary>
    public static class ScDaily
    {
        public static void Run(int day, EntityPlayerLocal player)
        {
            try
            {
                Community c = ScRegistry.Community;
                World w = ScCommunity.World;
                var gone = new List<int>();
                foreach (var m in ScRegistry.Members.Values)
                {
                    if (ScRunners.IsPhysical(m)) continue;     // a runner out of range has no entity by design
                    if (m.Status == MemberStatus.Arriving) continue;    // nor has one still on the way to the base (E12, ScEncJoin)
                    // a follower left holding a spot far out (a voice "stay", "guard", a patrol - ScOrders) is unloaded by
                    // design too, for as long as the player stays away: never counted missing. Only an old save's record
                    // of a class that no longer exists still runs out (M9).
                    if ((m.Status == MemberStatus.Following || m.Status == MemberStatus.PendingStation) && !ScRegistry.PredatesMigration(m)) continue;
                    bool loaded = ScNpc.HasBody(w, m.Id);      // a body still being built counts, unforced
                    if (!loaded) m.MissingDays++;
                    if (m.MissingDays > ScConfig.MissingDaysToPurge && ScZone.HomeLoaded) gone.Add(m.Id);
                }
                for (int i = 0; i < gone.Count; i++)
                {
                    Member m = ScRegistry.GetMember(gone[i]);
                    // a member of a class that no longer exists was never going to be seen: say why (migration M9)
                    if (ScRegistry.PredatesMigration(m)) ScLog.Warn(ScRegistry.PreMigrationLine + " - " + m.Name + " (" + m.Class + ") is removed");
                    ScLog.Warn("member " + (m != null ? m.Name : gone[i].ToString()) + " has not been seen for " + ScConfig.MissingDaysToPurge + " days while the base was loaded - removed");
                    ScRegistry.Members.Remove(gone[i]);
                    ScLooks.Forget(gone[i]);
                    ScRegistry.Cleanup.Add(gone[i]);      // strip the mod's cvars/buffs if that entity ever loads again
                }
                var summary = new System.Text.StringBuilder();
                ScRuns.Resolve(day, player, summary);    // runs land first: the yield counts for today's needs
                ScOutposts.Daily(day, player, summary);  // then the outposts' deliveries, upkeep and haulage
                ScEnclaves.Daily(day, player);           // a powered radio picks up an enclave every few days (E1)
                ScEnclavePeople.Daily(day);              // their people healed and their rounds refilled (E2)
                ScEncRequests.Daily(day, player);        // offers lapse, accepted requests expire, the day's new requests (E4)
                ScInfestations.Daily(day, player);       // cleared ones end, old ones may spread, the day's roll for a new one (I1)
                ScEncMoves.Daily(day, player);           // a group that lost people, or has the dead next door, may move (E11)
                ScBands.Daily(day, player);              // old trips end, the day's roll for a new band on the road (E13a)
                ScEncAllies.Daily(day, player, summary); // allies' bundles, refills of lost enclaves, allies telling of more (E6)
                ScBaseAttack.Daily(day);                 // the Defence morale part fades
                ScScan.Run(false);                     // beds / facilities / crops (unloaded chunks keep their last count)
                ScStockpile.DailyConsume(day, summary);
                ScMorale.Daily(day, player, summary);     // fades the death penalty, recomputes, threat / departure
                string line = "day " + day + ": " + ScRegistry.Members.Count + " member(s)" + (summary.Length > 0 ? " - " + summary : "");
                c.Ledger.Insert(0, line);
                while (c.Ledger.Count > 7) c.Ledger.RemoveAt(c.Ledger.Count - 1);
                ScLog.Out("daily tick " + line);
                if (ScRegistry.Members.Count > 0 && player != null)
                    ScCommunity.Tell(player, string.Format(Localization.Get("scDaily"), ScRegistry.Members.Count, summary), c.Hungry || c.Thirsty ? "ui_denied" : "");
                ScRegistry.SaveAtomic();
            }
            catch (Exception e)
            {
                ScLog.Once("daily", e);
            }
        }
    }
}
