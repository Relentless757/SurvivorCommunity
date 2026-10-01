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
        private static float nextTick;
        private static readonly List<int> scratch = new List<int>();

        public static void Reset()
        {
            nextTick = 0f;
        }

        public static void OnGameUpdate()
        {
            try
            {
                float now = Time.unscaledTime;
                if (now < nextTick) return;
                nextTick = now + Interval;

                if (!ScCommunity.IsHost) return;
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
                    // runs never come home
                    ScLog.Warn("the world day went backwards (" + c.LastDay + " -> " + day + "); resyncing the community to day " + day);
                    c.LastDay = day;
                    foreach (var m in ScRegistry.Members.Values)
                        if (m.Status == MemberStatus.OnRun && m.RunEndDay > day) m.RunEndDay = day;   // land the runs that were already due
                    ScRegistry.SaveAtomic();
                }
                else
                {
                    int guard = 0;
                    while (c.LastDay < day && guard++ < 60) { c.LastDay++; ScDaily.Run(c.LastDay, player); }
                }

                Relink(w, player);
                ScDoors.Tick(w, player);              // shut the doors members opened once they are through
                ScRunners.Tick(w, player);            // physical supply runs: walk, capture out of range, re-create near the player
                TrackProspects(w);
                ScQuests.SyncCompleted(player);
                ScQuests.TickMarkers(player);
                ScScan.RunIfDue(15f);                  // beds / facilities / crops from the loaded chunks, so the HUD morale follows what you build
                ScMorale.SyncHud(player);
                ScLeadership.Publish(player);          // sc_followers / sc_inzone / sc_morale_high for SurvivorOrigins' Leadership
                ScRadio.Tick(w, player);
                ScOutposts.Tick(w, player);           // radio report-in stamp, claim check, markers (every 10 s)
                ScHomeLink.Tick(player);              // an outpost's door into the home storage: load, open, release
                ScGarrison.Tick(w, player);           // stand-ins at the staffed outpost the player is at, visit stamps
                ScScouting.Tick(player);              // radio scouting marks: fade, restore after a load
                ScOutposts.TickFlags(player);         // Outpost Flags placed since the last second establish themselves
                ScOutpostRisk.Tick(w, player);        // attack fights and deadlines, blood-moon sieges
                ScMissing.Tick(w, player);            // residents whose body is gone from the world (Call back)
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
                EntityAlive npc = ScNpc.As(w.GetEntity(p.Id));
                if (npc == null || npc.IsDead()) continue;
                Vector3i at = npc.GetBlockPosition();
                if (at != p.NpcPos) { p.NpcPos = at; changed = true; }
            }
            if (changed) ScRegistry.SaveAtomic();
        }

        /// <summary>
        /// Every loaded member is re-stamped each second: the spawner source (PostInit resets it to Biome on
        /// load), Persist, the cvar mirror, the AI package (rebuilt from the class on load), the guard order
        /// for residents, and - for followers whose leader id went stale across a relog - the leader link.
        /// </summary>
        public static void Relink(World w, EntityPlayerLocal player)
        {
            scratch.Clear();
            // entities whose record was purged or left while unloaded: strip the mod's leftovers once they load
            if (ScRegistry.Cleanup.Count > 0)
            {
                foreach (int id in ScRegistry.Cleanup)
                {
                    EntityAlive stale = ScNpc.As(w.GetEntity(id));
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
                    EntityAlive gone = ScNpc.As(w.GetEntity(id));
                    if (gone == null) continue;
                    if (ScCommunity.CarriesOurMarks(gone)) ScRuns.RemoveEntity(gone);
                    scratch.Add(int.MinValue / 4 + id);   // marker: remove from the doomed set below
                }
            }
            foreach (var kv in ScRegistry.Members)
            {
                Member m = kv.Value;
                EntityAlive npc = ScNpc.As(w.GetEntity(m.Id));
                if (npc == null)
                {
                    continue;   // unloaded (or gone) - the daily tick tracks MissingDays
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
                // the re-hire below. Detect it BEFORE anything re-stamps the member.
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
                        if (EntityUtilities.GetCurrentOrder(m.Id) != EntityUtilities.Orders.Guard
                            || EntityUtilities.GetLeaderOrOwner(m.Id) != null)
                        {
                            ScLog.Verbose(m.Name + ": re-applying the resident station (order " + EntityUtilities.GetCurrentOrder(m.Id) + ")");
                            ScCommunity.Station(m, npc);
                        }
                        else
                        {
                            // self-heal the post: SCore's GuardHere/GuardThere minevents (pathing cubes, order
                            // buffs) may rewrite the guard position with the NPC's own spot
                            Vector3 post = ScCommunity.PostCenter(m);
                            if (ScNpc.GuardPosition(npc) != post) ScCommunity.ApplyPost(npc, post);
                            ScCommunity.Watchdog(m, npc);
                        }
                        break;
                    case MemberStatus.Following:
                    case MemberStatus.PendingStation:
                        if (EntityUtilities.GetLeaderOrOwner(m.Id) == null)
                        {
                            ScLog.Verbose(m.Name + ": leader link lost (relog?) - re-hiring to " + player.entityId);
                            ScNpc.SetLeaderAndOwner(m.Id, player.entityId);
                        }
                        // A follower left holding a Guard order obeys nothing: the pack's follow actions all
                        // require the Follow order, so they stand at a post they are no longer assigned to.
                        else if (EntityUtilities.GetCurrentOrder(m.Id) != EntityUtilities.Orders.Follow)
                        {
                            ScLog.Out(m.Name + ": following but the order is " + EntityUtilities.GetCurrentOrder(m.Id) + " - re-applying Follow");
                            npc.Buffs.RemoveBuff("buffOrderGuard");
                            npc.Buffs.SetCustomVar("CurrentOrder", (float)EntityUtilities.Orders.Follow);
                            ScNpc.SetLeaderAndOwner(m.Id, player.entityId);
                        }
                        ScSkills.OnFollowTick(m, npc);      // Cardio: the road builds stamina
                        if (m.Status == MemberStatus.PendingStation && ScZone.Contains(player.GetBlockPosition()) && ScZone.HomeLoaded)
                        {
                            ScCommunity.Station(m, npc);
                            ScCommunity.Tell(player, string.Format(Localization.Get("scStationed"), m.Name), "");
                            ScRegistry.SaveAtomic();
                        }
                        break;
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
                    bool loaded = w != null && ScNpc.IsFramework(w.GetEntity(m.Id));
                    if (!loaded) m.MissingDays++;
                    if (m.MissingDays > ScConfig.MissingDaysToPurge && ScZone.HomeLoaded) gone.Add(m.Id);
                }
                for (int i = 0; i < gone.Count; i++)
                {
                    Member m = ScRegistry.GetMember(gone[i]);
                    ScLog.Warn("member " + (m != null ? m.Name : gone[i].ToString()) + " has not been seen for " + ScConfig.MissingDaysToPurge + " days while the base was loaded - removed");
                    ScRegistry.Members.Remove(gone[i]);
                    ScLooks.Forget(gone[i]);
                    ScRegistry.Cleanup.Add(gone[i]);      // strip the mod's cvars/buffs if that entity ever loads again
                }
                var summary = new System.Text.StringBuilder();
                ScRuns.Resolve(day, player, summary);    // runs land first: the yield counts for today's needs
                ScOutposts.Daily(day, player, summary);  // then the outposts' deliveries, upkeep and haulage
                ScScan.Run(false);                     // beds / facilities / crops (unloaded chunks keep their last count)
                ScStockpile.DailyConsume(day, summary);
                ScMorale.Daily(day, player, summary);     // fades the death penalty, recomputes, threat / departure
                string line = "day " + day + ": " + ScRegistry.Members.Count + " member(s)" + (summary.Length > 0 ? " - " + summary : "");
                c.Ledger.Insert(0, line);
                while (c.Ledger.Count > 7) c.Ledger.RemoveAt(c.Ledger.Count - 1);
                ScLog.Out("daily tick " + line);
                if (ScRegistry.Members.Count > 0 && player != null)
                    ScCommunity.Tell(player, string.Format(Localization.Get("scDaily"), ScRegistry.Members.Count, summary), c.Hungry ? "ui_denied" : "");
                ScRegistry.SaveAtomic();
            }
            catch (Exception e)
            {
                ScLog.Once("daily", e);
            }
        }
    }
}
