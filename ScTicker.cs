using System;
using System.Collections.Generic;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// The server's once-a-second heartbeat (ModEvents.GameUpdate): day rollover, member relink, pending
    /// stationing and secure-quest sync. There is no day-change event in the game, so the day is polled.
    /// Round MP0: it runs on any server - single player, a listen host, or a dedicated server with no local player. The
    /// player it passes down is ScPlayers.Anchor() (the host's own player whenever there is one, exactly as before); each
    /// tick that asks "is a player there" asks ScPlayers for any community player instead.
    /// Round MP6 (core): the steps run once per community with a player online, lowest id first, each inside that community's
    /// scope (ScRegistry.Use) with that community's anchor (ScPlayers.Anchor(cid)); the world steps run once, in the first of
    /// those communities' passes, where they always ran in the order. With one community the tick is exactly as before.
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
        /// <summary>Round MP6 (core): the community "sc day" simulated a day for (its resync is the quiet one).</summary>
        public static int SimulatedCommunity = ScPlayers.SharedCommunity;
        /// <summary>Round MP6 (core): the communities with a player online this tick, lowest id first.</summary>
        private static readonly List<int> online = new List<int>();

        public static void Reset()
        {
            nextTick = 0f;
            SimulatedDay = -1;
            SimulatedFromDay = -1;
            SimulatedCommunity = ScPlayers.SharedCommunity;
        }

        /// <summary>Round MP6 (core): the communities with a player (alive or dead) in the world, lowest id first.</summary>
        private static void OnlineCommunities(List<int> into)
        {
            into.Clear();
            List<EntityPlayer> all = ScPlayers.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] == null) continue;
                int c = ScPlayers.CommunityOf(all[i]);
                if (!into.Contains(c)) into.Add(c);
            }
            into.Sort();
        }

        public static void OnGameUpdate()
        {
            try
            {
                float now = Time.unscaledTime;
                ScShelter.Step();                     // round rt1: the shelter cache's scan, a few seeds a frame (nothing when idle)
                if (now < nextTick) return;
                nextTick = now + Interval;

                ScMarkers.ClientTick();               // round MP4 (part K): a client's map squared with the server's markers (nothing elsewhere)
                if (!ScCommunity.IsHost) return;
                // round MP3 (review storage m7): linked storage's sessions are swept first and in their own try - it used to run
                // late in this tick (ScHomeLink.Tick), after the "nobody online" return, so a fault in any earlier step stopped it
                // and no view's claim ever ended
                try { ScStorageServer.Sweep(); }
                catch (Exception e) { ScLog.Once("storage-sweep-tick", e); }
                // before the local-player test below, so a dedicated server sweeps too: a dead NPC's window a remote
                // player held drops at the body once its open mark has expired (Patches_Npc)
                Patches_Npc.Patch_SetDead.ReleaseAll(false);
                World w = ScCommunity.World;
                if (w == null || !ScRegistry.Loaded) return;
                // round MP6 (core): the three per-viewer pushes stay community 0's, as before MP6 (the snapshot is community 0's
                // text: BuildText(true)). MP6 (part network): per community - each client gets its own community's
                using (ScRegistry.Use(ScPlayers.SharedCommunity))
                {
                    // round MP1: the clients' copy of the registry - the last second's changes, at most once a second (its own try;
                    // nothing is built with no remote client, so single player is untouched)
                    ScMirror.ServerTick();
                    // round MP4 (part K): the community's map markers to remote clients (its own try; nothing is built with none)
                    ScMarkers.ServerTick();
                    // round MP4 (review cfs1 M1): the base's open boxes to remote clients, so their Craft from Storage leaves them out (its
                    // own try; nothing is built with none)
                    ScCraftFromBase.ServerTick();
                }
                // round MP0: no longer the local player - a dedicated server has none. With nobody online there is nothing
                // to tick (a dedicated server's world time stands still with no players). Round MP6 (core): each community with a
                // player online ticks, inside its own scope, with its own anchor; the world steps run in the first one's pass
                OnlineCommunities(online);
                if (online.Count == 0) return;
                ScConfig.EnsureParsed();
                int day = GameUtils.WorldTimeToDays(w.worldTime);
                int first = -1;
                EntityPlayer firstAnchor = null;
                for (int i = 0; i < online.Count; i++)
                {
                    int cid = online[i];
                    try
                    {
                        using (ScRegistry.Use(cid))
                        {
                            EntityPlayer player = ScPlayers.Anchor(cid);
                            if (player == null) continue;
                            bool world = first < 0;
                            if (world) { first = cid; firstAnchor = player; }
                            TickCommunity(w, player, cid, day, world);
                        }
                    }
                    catch (Exception e)
                    {
                        ScLog.Once("ticker", e);             // as before MP6: a fault ends this community's tick for this second
                    }
                }
                // round MP6 (core): the world daily's day guard ahead of the world with no community going back to reset it (with one
                // community it never is: the community's own rollover resets both)
                if (first >= 0)
                    using (ScRegistry.Use(first)) ScDaily.WorldDayBack(day, firstAnchor);
            }
            catch (Exception e)
            {
                ScLog.Once("ticker", e);
            }
        }

        /// <summary>
        /// Round MP6 (core): one community's tick, inside its scope (the caller's Use), with its anchor: its day rollover, then
        /// every step in the order they always ran. world: this is the first community's pass - the world steps run in it (only),
        /// in their old places. With one community this is exactly the old tick.
        /// The steps whose throttle is one static (a "next tick" time: ScAmmo, ScLightsOut, ScWatchFatigue, ScActivities' buff
        /// check, ScBond, ScMorale.SyncHud, ScRadio, ScMining, ScOutposts.Tick, ScHomeHold, ScGarrison, ScScouting, ScOutpostRisk,
        /// ScBaseAttack, ScSiege, ScWatch, ScInvestigate, ScMissing) pass it in the first community's pass of the second only.
        /// MP6 (part home): those throttles per community - else community 1+ never runs them
        /// </summary>
        private static void TickCommunity(World w, EntityPlayer player, int cid, int day, bool world)
        {
            {
                Community c = ScRegistry.Community;
                ScWorldState ws = ScRegistry.WorldState;
                if (c.LastDay < 0)
                {
                    c.LastDay = day;                       // baseline: the first observed day is not charged
                    if (ws.LastDay < 0) ws.LastDay = day;  // round MP6 (core): the world daily's guard starts with it
                }
                else if (day < c.LastDay)
                {
                    // the world clock went backwards (an older save loaded, or a time command): without this the
                    // community is frozen until the world catches up - no consumption, no morale, and supply
                    // runs never come home. "sc day" runs ahead of the world on purpose: its own day ending is no fault.
                    // Round MP6 (core): "sc day" ran for one community (SimulatedCommunity); the world's part (OnWorldDayBack and
                    // the world daily's guard) goes back once, with the first community whose day goes back
                    bool simulated = cid == SimulatedCommunity;
                    if (simulated && c.LastDay == SimulatedDay && day == SimulatedFromDay) ScLog.Out("sc day: the simulated day " + c.LastDay + " is over - back to the world's day " + day);
                    else
                    {
                        ScLog.Warn("the world day went backwards (" + c.LastDay + " -> " + day + "); resyncing the community to day " + day
                                   + (ScCommunities.Count > 1 ? " (" + ScRegistry.Current.Label + ")" : ""));
                        // E4/E6: the request clock and every forward-only enclave stamp follow a real jump back (never sc day's own end);
                        // within a second of "sc day" the community's day is still the simulated one: the world went back from its own
                        // day (the E6 review's N1). Round MP6 (core): once for the world - while its guard is still ahead
                        if (ws.LastDay > day)
                            ScEnclaves.OnWorldDayBack(day, simulated && ws.LastDay == SimulatedDay && SimulatedFromDay >= 0 ? SimulatedFromDay : ws.LastDay);
                    }
                    if (ws.LastDay > day) ws.LastDay = day;         // round MP6 (core): the world daily follows
                    if (simulated) SimulatedDay = -1;
                    c.LastDay = day;
                    foreach (var m in ScRegistry.Members.Values)
                        if (m.Status == MemberStatus.OnRun && m.RunEndDay > day) m.RunEndDay = day;   // land the runs that were already due
                    ScRegistry.SaveAtomic();
                }
                else
                {
                    if (cid == SimulatedCommunity) SimulatedDay = -1;      // the world has caught up with any simulated day: no resync of ours is coming
                    int guard = 0;
                    while (c.LastDay < day && guard++ < 60) { c.LastDay++; ScDaily.Run(c.LastDay, player); }
                }

                Relink(w, player);
#if SC_V34
                ScDowned.Tick(w, player);             // round U2: who is down, back up or bled out; resident medics sent to the downed at home
#endif
                ScAmmo.Tick(w);                       // every 5 s: each member's ammo into the record; resupply in the base / at an outpost
#if SC_V34
                ScWork.Sweep(player);                 // C1: work shifts whose member left, moved on or unloaded are dropped
                if (world) ScPanelHold.Tick(w);       // round mt1 (review fix G-m2): the server's backup of a remote player's member panel hold (round MP6 (core): per player, once)
#endif
                ScDoors.Tick(w, player);              // shut the doors members opened once they are through
#if SC_V34
                // round MP6 (core): world, once (it plans members and the enclaves' people together).
                // MP6 (part home): it plans the members of the community in scope only - community 1+'s are never put to bed
                if (world) ScSleep.Tick(w, player);   // round n06: the night lease - off-duty people sleep through 0-XNPCCore's routine
                ScSocial.Tick(w, player);             // round U3: small talk, walking over to chat, the evening campfire (after the lease)
                ScReach.Tick(w, player);              // round n08: watches 0-XNPCCore's melee-unreachable marks (measurement only)
#endif
                ScRunners.Tick(w, player);            // physical supply runs: walk, capture out of range, re-create near the player
                TrackProspects(w);
                ScQuests.SyncCompleted(player);
                ScQuests.TickMarkers(player);
                ScScan.RunIfDue(15f);                  // beds / facilities / crops from the loaded chunks, so the HUD morale follows what you build
                ScNoise.Tick(w, player);               // the base's noise (what was heard in the zone) and the dead's attention
                ScLightsOut.Tick(w, player);           // round m1f (BD1): Lights out ends at dawn - the generators it switched off back on
                ScWatchFatigue.Tick(w);                // round m1f (BD1): a guard's watch term changed (hours, dusk, dawn, rest) - moods now
                ScActivities.Tick(player);             // the round of drinks: its real-time countdown (round wl)
#if SC_V34
                ScBond.Tick(w, player);               // round U4: queued Bond events into the records, the blood moon survived together
#endif
                ScMorale.SyncHud(player);
                ScLeadership.Publish(player);          // sc_followers / sc_inzone / sc_morale_high for SurvivorOrigins' Leadership
                ScRadio.Tick(w, player);
#if SC_V34
                ScRadioTalk.Tick(w, player);          // R2: runners with a handheld radio - leaving home, leaving the site, the next line on the radio
                ScRunnerCall.Tick(w, player);         // round 29b: a runner's call for help when attacked - the marker follows them, the call ends with the fight
                ScMining.Tick(w, player);             // round 33c: a mining run's haul that waited for the home's chunks (or room) is put away
#endif
                ScOutposts.Tick(w, player);           // radio report-in stamp, claim check, markers (every 10 s)
                ScHomeLink.Tick(player);              // an outpost's door into the home storage: load, open, release
                ScHomeHold.Tick(w);                   // the home zone held loaded while a player is near (round hz)
                ScGarrison.Tick(w, player);           // stand-ins at the staffed outpost the player is at, visit stamps
                ScScouting.Tick(player);              // radio scouting marks: fade, restore after a load
                // round MP6 (core): the world steps below (if (world)) run once, in the first community's pass.
                // MP6 (part relations): ScEnclaves.Tick and ScInfestations.Tick set Known for whoever finds one - give them the
                // finder's scope (they run in the first community's now)
                if (world) ScEnclaves.Tick(w, player);           // enclaves found within reach, their markers restored after a load (E1)
                if (world) ScEnclavePeople.Tick(w, player);      // their people: made, spawned near a player, gone when none is, the watchdog (E2)
                if (world) ScEncDoors.Tick(w);                   // their building's locked doors unlocked, chunk by chunk as they load (round 32a)
                if (world) ScBands.Tick(w, player);              // bands on the road: bodies near a player, their walk, camp, trouble, markers (E13a)
                // MP6 (part relations): ScEncRequests and ScEncCalls run per community; their session state (ScEncCalls' sessions and
                // callers by enclave id) is one for the world
                ScEncRequests.Tick(w, player);        // their requests: the journal squared with the records once a session (E4)
                ScEncCalls.Tick(w, player);           // allies' radio calls: a fighter's arrival and return after a load, a supply drop (E9)
                if (world) ScInfestations.Tick(w, player);       // infestations found within reach, their markers restored after a load (I1)
                // MP6 (part gates): the flag queue keeps positions only - the first community's pass establishes every flag placed
                // this second as its own; store the placer's community with each flag
                ScOutposts.TickFlags(player);         // Outpost Claim Blocks placed since the last second establish themselves
                ScOutpostRisk.Tick(w, player);        // attack fights and deadlines, blood-moon sieges
                ScBaseAttack.Tick(w, player);         // the home's horde: the call at full attention, the arrival, the fight (with or without the player)
                ScSiege.Tick(w, player);              // a blood moon passed with the player away besieges the base
                ScWatch.Tick(w, player);              // watch posts: shifts, the zone scan, a guard's call-outs and marker
                ScInvestigate.Tick(w, player);        // round k4: idle intruders inside the base, and the residents who go to them
                ScMissing.Tick(w, player);            // residents whose body is gone from the world (Call back)
                ScEncJoin.Tick(w, player);            // a whole enclave that joined: their bodies walk off, each turns up at home when due (E12)
                if (world) ScStreamerNames.Tick();    // round 30: the streamer names given in the last second into the save (round MP6 (core): world)
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
        public static void Relink(World w, EntityPlayer player)
        {
            scratch.Clear();
            // round MP0: sc_owner names the community's owner (Stage 1: the owner while online, else the anchor - in single
            // player and on a listen host both are the host's own player, as before)
            EntityPlayer owner = ScPlayers.OwnerOnline() ?? player;
            Community oc = ScRegistry.Community;
            if (string.IsNullOrEmpty(oc.OwnerId) && !string.IsNullOrEmpty(oc.Owner) && ScPlayers.OwnerOnline() != null)
            {
                // the review (S4): a save from before MP0 knows its owner by display name only - their platform id from now on
                string oid = ScPlayers.IdOf(ScPlayers.OwnerOnline());
                if (oid.Length > 0) { oc.OwnerId = oid; leaderChanged = true; ScLog.Out("community owner " + oc.Owner + " recorded by platform id"); }
            }
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
                if (ScRunners.HeldOffline(m))
                {
                    // the snapshot IS this runner: an entity under the id is an old chunk copy (hidden, then removed by
                    // ScRunners) or a stranger who inherited the id (left alone - the runner returns under a new id). Round op1:
                    // the same for a posting traveler offline and a posted member whose body walked to the outpost
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
                ScGarrison.NoteBody(m, npc);          // round op1 review (M1): the chunk their body stands in, for a brief load later

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
                if (!npc.Buffs.HasCustomVar("sc_member") || npc.Buffs.GetCustomVar("sc_owner") != owner.entityId)
                    ScCommunity.StampCvars(m, npc, owner);
                Patches_VoiceControl.StripContract(npc);   // members are never billed (0-XNPCCore 3.2's Voice Control)
                ScCommunity.ApplyAiPackage(npc);
                ScCommunity.StampFollowing(m, npc);   // keeps the dialog's order options in step with the status
#if SC_V34
                UAI.UAIConsiderationScNotHomeMember.DisarmLeave(m, npc);   // round hl1: a leave 0-XNPCCore armed at home never fires later
#endif
                ScSkills.StampCvars(m, npc);          // levels and derived bonuses (daily XP changes them while loaded)
                ScTraits.ApplyBuffs(m, npc);
                ScLooks.Sync(m, npc);                 // player-body survivors: equipment is never saved for NPCs
#if SC_V34
                ScHanded.KeepCopies(m, npc);          // round n12: her copies of a weapon she was handed carry it exactly
#endif
                ScPortraits.OnMemberLoaded(m, npc);   // pack NPCs can only be pictured while loaded (and near)
                ScSkills.SyncStatusBuffs(m, npc);

                switch (m.Status)
                {
                    case MemberStatus.Resident:
                        if (npc.Buffs.HasCustomVar("onMission") || !npc.enabled) ScNpc.SendOnMission(npc, false);   // never a hidden resident
#if SC_V34
                        // round U2 (risks 1, 2 and the LOW ones): a downed resident is left as she lies until she is up or has bled out -
                        // no re-station (it drops a shift's or talk's link and may teleport her), no shift tick (D4: ScWork holds the
                        // link), no post self-heal, watchdog, stuck rescue or watch placing (each can move a ragdoll)
                        if (ScDowned.IsDown(npc)) break;
#endif
#if SC_V34
                        // C1: on a work shift she has an owner and a craft spot on purpose - ScWork ticks her instead of the rules below;
                        // round 29b: the stuck rescue still runs where her job allows it (ScStuck.OnShift)
                        if (ScWork.Resident(m, npc, player))
                        {
                            // round U2 (the Fable review, F7): no stuck rescue for a medic on her way to a downed member (it can teleport)
                            if (!ScDowned.IsReviving(npc.entityId)) ScStuck.OnShift(m, npc);
                            break;
                        }
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
#if SC_V34
                            // round U2 (the Fable review, F7): no stuck rescue for a medic on her way to a downed member - its 12 s still rule
                            // matches ReviveNoGainSeconds and a teleport to her post would end the revive; ScDowned's own no-gain clock judges it
                            if (!ScDowned.IsReviving(npc.entityId))
#endif
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
                        // round MP0: the leader is the member's own - the live link while it holds (its platform id kept in
                        // m.LeaderId), else the player with that id once they are online again. Never whoever happens to host:
                        // in multiplayer that handed a client's follower to the host at the first lost link
                        EntityPlayer leader = EntityUtilities.GetLeaderOrOwner(m.Id) as EntityPlayer;
                        if (leader != null) NoteLeader(m, leader);
                        // the review (S2): a live link to a non-player (an entity id reused after a crash) is lost too, as before MP0
                        if (leader == null)
                        {
                            // the leader id went stale (relog?): re-link, but a held voice order stays held
                            leader = LeaderToRestore(m);
                            if (leader != null)
                            {
                                bool follow = !held || order == EntityUtilities.Orders.Follow || ownGuard;
                                ScLog.Verbose(m.Name + ": leader link lost (relog?) - re-hiring to " + leader.entityId + (follow ? "" : ", keeping the order " + order));
                                ScNpc.SetLeaderAndOwner(m.Id, leader.entityId, follow);
                                if (follow) m.FollowIssued = Mathf.Max(0.001f, now);
                            }
                            else ScLog.Once("relink-wait-" + m.Id, m.Name + ": leader link lost and their leader is not online - they wait for them");
                        }
                        else if ((!held || ownGuard) && leader != null)
                        {
                            ScLog.Out(m.Name + ": following but the order is " + (hasOrder ? order.ToString() : "unset") + " - re-applying Follow");
                            npc.Buffs.RemoveBuff("buffOrderGuard");
                            npc.Buffs.SetCustomVar("CurrentOrder", (float)EntityUtilities.Orders.Follow);
                            ScNpc.SetLeaderAndOwner(m.Id, leader.entityId);
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
                        // round MP0: their own leader's homecoming (the player they follow), not the host's
                        if (m.Status == MemberStatus.PendingStation && leader != null && ScZone.Contains(leader.GetBlockPosition()) && ScZone.HomeLoaded
                            && (ScOrders.WithLeader(order) || ScZone.Contains(npc.GetBlockPosition()))
#if SC_V34
                            && !ScDowned.DefersStation(m, npc)    // round U2 (risk 2): a downed follower is stationed once she is up
#endif
                            )
                        {
                            ScCommunity.Station(m, npc);
                            // round MP4 (part: tells): this file's tells go by localization key (ScCommunity.TellKey / ScPlayers.TellCommunityKey), so each player reads them in their own language
                            ScCommunity.TellKey(leader, "scStationed", "", m.Name);
                            ScRegistry.SaveAtomic();
                        }
                        break;
                    }
                    case MemberStatus.OnRun when ScRunners.IsPhysical(m):
                    case MemberStatus.Transit when ScRunners.IsPhysical(m):         // round op1: a posting traveler on the road
                        // a physical runner is out in the world, walking: never hidden, always on the travel order
                        if (npc.Buffs.HasCustomVar("onMission") || !npc.enabled) ScNpc.SendOnMission(npc, false);
                        if (!ScRunners.HasTravelOrder(npc)) ScRunners.ApplyTravelOrder(npc);
                        break;
                    case MemberStatus.OnRun:
                    case MemberStatus.Garrison:
                    case MemberStatus.Transit:                                       // round op1: parked - ScGarrison takes the body into a snapshot
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
                else
                {
#if SC_V34
                    ScHanded.OnRecordGone(ScRegistry.GetMember(id), null, "dropped: her entity id was reused");   // n12 review: logged
#endif
                    ScRegistry.Members.Remove(id);
                    ScLooks.Forget(id);
                }
            }
            if (scratch.Count > 0 || leaderChanged) ScRegistry.SaveAtomic();
            scratch.Clear();
            leaderChanged = false;
        }

        private static bool leaderChanged;

        /// <summary>Round MP0: the follower's leader's platform id, kept while the link holds (saved with this relink's write).</summary>
        private static void NoteLeader(Member m, EntityPlayer leader)
        {
            string id = ScPlayers.IdOf(leader);
            if (id.Length == 0 || string.Equals(id, m.LeaderId, StringComparison.Ordinal)) return;
            m.LeaderId = id;
            leaderChanged = true;
        }

        /// <summary>
        /// Round MP0: who a follower whose leader link went stale belongs to: the player with their recorded leader id if online;
        /// with no id recorded (a save from before MP0) the host's own player, as before; else nobody yet (a dedicated server
        /// waits for their leader to come back).
        /// </summary>
        /// <summary>Round MP0 (the review's re-check): whether this platform id is a player of this save (has player data here).</summary>
        private static bool KnownPlayer(string id)
        {
            try
            {
                PersistentPlayerList pp = GameManager.Instance != null ? GameManager.Instance.persistentPlayers : null;
                PlatformUserIdentifierAbs uid;
                if (pp == null || !PlatformUserIdentifierAbs.TryFromCombinedString(id, out uid) || uid == null) return false;
                return pp.GetPlayerData(uid) != null;
            }
            catch (Exception e)
            {
                ScLog.Once("relink-known", e);
                return false;
            }
        }

        private static EntityPlayer LeaderToRestore(Member m)
        {
            // the review (S1): single player has one player - always theirs, whatever id was recorded (another account, an id
            // fallback). Not IsSinglePlayer: a listen host with no clients answers that too
            ConnectionManager cm = SingletonMonoBehaviour<ConnectionManager>.Instance;
            if (cm != null && cm.CurrentMode == ProtocolManager.NetworkType.OfflineServer) return ScCommunity.LocalPlayer;
            if (!string.IsNullOrEmpty(m.LeaderId) && KnownPlayer(m.LeaderId)) return ScPlayers.ById(m.LeaderId);
            // no id, or one that is no player of this save (a solo game started as a server, a save played by another account):
            // the host's own player, as before MP0 (null on a dedicated server).
            // MP6 (part gates): whatever community the host's player is in - only one of the follower's community should be
            return ScCommunity.LocalPlayer;
        }
    }

    /// <summary>
    /// The day rollover. In M1 it only keeps the ledger and the missing-member bookkeeping; consumption,
    /// morale, runs and specialties are added by the later milestones in this same method.
    /// Round MP6 (core): split in two. Run is a community's daily (in its scope, its own Community.LastDay the guard): the
    /// missing members, runs and outposts, then - where they always ran - the world daily (RunWorld: enclaves, their people,
    /// requests, infestations, moves, bands, allies) for every world day not run yet (ScWorldState.LastDay, its own guard: the
    /// first community to roll a day over runs it, inside its scope), then the base attack, the scan, the stockpile, Bond,
    /// morale, the ledger and the tell. With one community the side effects come in exactly the old order.
    /// </summary>
    public static class ScDaily
    {
        /// <summary>
        /// Round MP6 (core): the world daily for one day - the steps of the old daily that are the world's. Run calls it in their
        /// old place (summary: the rolling community's, as before).
        /// MP6 (part relations): ScEnclaves.Daily (this community's radio reveal), ScEncRequests.Daily (the day's requests - per
        /// community by the user's decision - besides the world's request clock) and ScEncAllies.Daily (allies' bundles into this
        /// community's stockpile and summary, besides the world's refills) mix world and community work; they run once, in the
        /// scope of the community whose rollover ran the world daily
        /// </summary>
        public static void RunWorld(int day, EntityPlayer player, System.Text.StringBuilder summary)
        {
            ScEnclaves.Daily(day, player);           // a powered radio picks up an enclave every few days (E1)
            ScEnclavePeople.Daily(day);              // their people healed and their rounds refilled (E2)
            ScEncRequests.Daily(day, player);        // offers lapse, accepted requests expire, the day's new requests (E4)
            ScInfestations.Daily(day, player);       // cleared ones end, old ones may spread, the day's roll for a new one (I1)
            ScEncMoves.Daily(day, player);           // a group that lost people, or has the dead next door, may move (E11)
            ScBands.Daily(day, player);              // old trips end, the day's roll for a new band on the road (E13a)
            ScEncAllies.Daily(day, player, summary); // allies' bundles, refills of lost enclaves, allies telling of more (E6)
        }

        /// <summary>
        /// Round MP6 (core), ScTicker after the communities' passes (in the first one's scope): the world daily's guard ahead of
        /// the world's day with no community whose day went back to reset it - reset it here (OnWorldDayBack, unless it is "sc
        /// day"'s own simulated day ending). With one community it never is ahead by then.
        /// </summary>
        public static void WorldDayBack(int day, EntityPlayer player)
        {
            ScWorldState ws = ScRegistry.WorldState;
            if (ws.LastDay <= day) return;
            if (!(ws.LastDay == ScTicker.SimulatedDay && day == ScTicker.SimulatedFromDay))
            {
                ScLog.Warn("the world day went backwards (" + ws.LastDay + " -> " + day + "); resyncing the world daily to day " + day);
                ScEnclaves.OnWorldDayBack(day, ws.LastDay);
            }
            ws.LastDay = day;
            ScRegistry.SaveAtomic();
        }

        public static void Run(int day, EntityPlayer player)
        {
            try
            {
                Community c = ScRegistry.Community;
                World w = ScCommunity.World;
                // round MP6 (core): the world days this rollover runs (the world daily's own guard, claimed here so a fault below
                // skips them for good, as a fault always skipped that day's world steps): with one community just this day
                ScWorldState ws = ScRegistry.WorldState;
                int worldFrom = -1;
                if (ws.LastDay < day)
                {
                    worldFrom = ws.LastDay < 0 ? day : Math.Max(ws.LastDay + 1, day - 59);
                    ws.LastDay = day;
                }
                var gone = new List<int>();
                foreach (var m in ScRegistry.Members.Values)
                {
                    if (ScRunners.IsPhysical(m)) continue;     // a runner out of range has no entity by design
                    // round op1: nor has a posting traveler (parked: ScGarrison sees them home) or a posted member whose body is the snapshot.
                    // The op1 review (L2): no posted member at all is counted - one whose body is gone would be dropped after
                    // MissingDaysToPurge days with no Call back (ScMissing lists residents only). Recalled, they walk home and become a
                    // resident with no body, whom ScMissing then lists. Only an old save's member of a class that no longer exists still runs out.
                    if ((m.Status == MemberStatus.Transit || m.Status == MemberStatus.Garrison) && !ScRegistry.PredatesMigration(m)) continue;
                    if (m.Status == MemberStatus.Arriving) continue;    // nor has one still on the way to the base (E12, ScEncJoin)
                    // a follower left holding a spot far out (a voice "stay", "guard", a patrol - ScOrders) is unloaded by
                    // design too, for as long as the player stays away: never counted missing. Only an old save's record
                    // of a class that no longer exists still runs out (M9).
                    if ((m.Status == MemberStatus.Following || m.Status == MemberStatus.PendingStation) && !ScRegistry.PredatesMigration(m)) continue;
                    bool loaded = ScNpc.HasBody(w, m.Id);      // a body still being built counts, unforced
                    if (!loaded) m.MissingDays++;
                    // the op1 re-check: never while a posting's body fetch has a few home chunks loaded - HomeLoaded then reads
                    // true with the player far away, and every resident outside those chunks would look gone
                    if (m.MissingDays > ScConfig.MissingDaysToPurge && ScZone.HomeLoaded && !ScGarrison.Fetching) gone.Add(m.Id);
                }
                for (int i = 0; i < gone.Count; i++)
                {
                    Member m = ScRegistry.GetMember(gone[i]);
                    // a member of a class that no longer exists was never going to be seen: say why (migration M9)
                    if (ScRegistry.PredatesMigration(m)) ScLog.Warn(ScRegistry.PreMigrationLine + " - " + m.Name + " (" + m.Class + ") is removed");
                    ScLog.Warn("member " + (m != null ? m.Name : gone[i].ToString()) + " has not been seen for " + ScConfig.MissingDaysToPurge + " days while the base was loaded - removed");
#if SC_V34
                    ScHanded.OnRecordGone(m, null, "not seen for " + ScConfig.MissingDaysToPurge + " days");   // round n12 review: logged
#endif
                    ScRegistry.Members.Remove(gone[i]);
                    ScLooks.Forget(gone[i]);
                    ScRegistry.Cleanup.Add(gone[i]);      // strip the mod's cvars/buffs if that entity ever loads again
                }
                var summary = new System.Text.StringBuilder();
                ScRuns.Resolve(day, player, summary);    // runs land first: the yield counts for today's needs
                ScOutposts.Daily(day, player, summary);  // then the outposts' deliveries, upkeep and haulage
                // round MP6 (core): the world daily, where its steps always ran (RunWorld), for each world day claimed above
                if (worldFrom >= 0)
                    for (int d = worldFrom; d <= day; d++) RunWorld(d, player, summary);
                ScBaseAttack.Daily(day);                 // the Defence morale part fades
                ScScan.Run(false);                     // beds / facilities / crops (unloaded chunks keep their last count)
                ScStockpile.DailyConsume(day, summary);
#if SC_V34
                ScBond.Daily(day);                       // round U4: every member's Bond fades toward 0 (before the moods are recomputed)
#endif
                ScMorale.Daily(day, player, summary);     // fades the death penalty, recomputes, threat / departure
                string line = "day " + day + ": " + ScRegistry.Members.Count + " member(s)" + (summary.Length > 0 ? " - " + summary : "");
                c.Ledger.Insert(0, line);
                while (c.Ledger.Count > 7) c.Ledger.RemoveAt(c.Ledger.Count - 1);
                ScLog.Out("daily tick " + line);
                // round MP0: the day's summary is the whole community's news (in single player: the one player)
                if (ScRegistry.Members.Count > 0)
                    ScPlayers.TellCommunityKey("scDaily", c.Hungry || c.Thirsty ? "ui_denied" : "", ScRegistry.Members.Count, summary);
                ScRegistry.SaveAtomic();
            }
            catch (Exception e)
            {
                ScLog.Once("daily", e);
            }
        }
    }
}
