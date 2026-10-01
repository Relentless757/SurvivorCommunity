using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Outposts v1.1, people (Outposts_Plan.md section 3): members posted to an outpost.
    ///   Garrison  - a stationed, healthy, willing member is posted (MemberStatus.Garrison). Like a runner, the
    ///               real entity stays hidden and parked at home (the home logic - purge, relink, watchdog - never
    ///               has to chase a member across the map); the posting is registry bookkeeping. Staff still eat.
    ///               Each staffer adds OutpostStaffBonus to the yield, and the best one whose specialty fits the
    ///               outpost's resource adds OutpostMatchBase + OutpostMatchPerStar x stars (cooks and gardeners for
    ///               food, medics and chemists for meds, shooters for ammo, crafters for materials, mechanics for fuel).
    ///   Stand-in  - within OutpostStandInRange of a loaded, staffed outpost, a copy of each staffer (their class and
    ///               look, skills, traits) stands guard at the claim; it goes when the player leaves. It IS the
    ///               survivor: if it dies there, they died (ScRadio.OnStandInDied routes here). Never saved.
    ///   Moods     - posted away from home: -3 (a Loner: +5), +3 while the player visited in the last 3 days.
    ///   Walk-ins  - a staffed outpost sometimes becomes the spawn point of a radio contact.
    /// Host only.
    /// </summary>
    public static class ScGarrison
    {
        /// <summary>On a stand-in: which outpost it guards (with ScRadio.StandInCvar = the member id).</summary>
        public const string StandInOutpostCvar = "sc_standin_op";

        private static float nextTick;
        private static readonly Dictionary<int, float> nextCount = new Dictionary<int, float>();
        private static readonly int[] countScratch = new int[4];

        public static void Reset()
        {
            nextTick = 0f;
            nextCount.Clear();
            Orphans.Clear();
        }

        /// <summary>Stand-ins that loaded from a save (the framework PostInit postfix): removed on the next tick.</summary>
        public static readonly HashSet<int> Orphans = new HashSet<int>();

        private static void SweepOrphans(World w)
        {
            if (Orphans.Count == 0) return;
            foreach (int id in Orphans)
            {
                EntityAlive s = ScNpc.As(w.GetEntity(id));
                if (s == null) continue;
                bool current = false;
                foreach (var m in ScRegistry.Members.Values) if (m.StandInId == id) { current = true; break; }
                if (!current) { ScRuns.RemoveEntity(s); ScLog.Out("removed a stale outpost stand-in (entity " + id + ")"); }
            }
            Orphans.Clear();
        }

        // ------------------------------------------------------------------ queries ----

        public static int StaffCap(Outpost o) { return o != null && o.Level >= 2 ? 2 : 1; }

        public static List<Member> Staff(Outpost o)
        {
            var list = new List<Member>();
            if (o == null) return list;
            foreach (var m in ScRegistry.Members.Values)
                if (m.Status == MemberStatus.Garrison && m.OutpostId == o.Id) list.Add(m);
            list.Sort((a, b) => a.Id.CompareTo(b.Id));
            return list;
        }

        public static int StaffCount(Outpost o)
        {
            int n = 0;
            if (o == null) return 0;
            foreach (var m in ScRegistry.Members.Values) if (m.Status == MemberStatus.Garrison && m.OutpostId == o.Id) n++;
            return n;
        }

        public static Outpost PostOf(Member m)
        {
            return m != null && m.Status == MemberStatus.Garrison ? ScOutposts.ById(m.OutpostId) : null;
        }

        /// <summary>How many stars a member brings to an outpost of this resource (0 = no match).</summary>
        public static int MatchStars(Member m, Resource r)
        {
            if (m == null) return 0;
            switch (r)
            {
                case Resource.Food: return m.Specialty == Specialty.Cooking || m.Specialty == Specialty.Gardening ? m.SpecLevel : 0;
                case Resource.Meds: return m.Specialty == Specialty.Medicine || m.Specialty == Specialty.Chemistry ? m.SpecLevel : 0;
                case Resource.Ammo: return m.Stars(Skill.Shooting) >= 2 ? m.Stars(Skill.Shooting) - 1 : 0;
                case Resource.Materials: return m.Specialty == Specialty.Craftsmanship ? m.SpecLevel : 0;
                case Resource.Fuel: return m.Specialty == Specialty.Mechanics ? m.SpecLevel : 0;
                default: return 0;
            }
        }

        /// <summary>One staffer's share of the yield bonus at this outpost, for the window ("+50%, cook +40%").</summary>
        public static float MatchBonus(Member m, Resource r)
        {
            int stars = MatchStars(m, r);
            return stars > 0 ? ScConfig.OutpostMatchBase + ScConfig.OutpostMatchPerStar * stars : 0f;
        }

        /// <summary>1 + OutpostStaffBonus per staffer + the best matching specialty's bonus.</summary>
        public static float StaffMult(Outpost o, Resource primary)
        {
            List<Member> staff = Staff(o);
            if (staff.Count == 0) return 1f;
            float best = 0f;
            foreach (Member m in staff) best = Math.Max(best, MatchBonus(m, primary));
            return 1f + ScConfig.OutpostStaffBonus * staff.Count + best;
        }

        // ------------------------------------------------------------------ posting ----

        /// <summary>error = a localization key.</summary>
        public static bool CanPost(Member m, EntityAlive npc, Outpost o, out string error)
        {
            error = null;
            if (!ScCommunity.IsHost) { error = "scHostOnly"; return false; }
            if (m == null || o == null) { error = "scInternal"; return false; }
            if (!o.Holding) { error = "scOpPostLost"; return false; }
            if (m.Status == MemberStatus.Garrison) { error = m.OutpostId == o.Id ? "scOpPostAlready" : "scOpPostElsewhere"; return false; }
            if (m.Status == MemberStatus.OnRun) { error = "scOnRun"; return false; }
            if (m.Status != MemberStatus.Resident) { error = "scOpPostNotHome"; return false; }
            if (npc == null) { error = "scNotLoaded"; return false; }
            if (m.Injured) { error = "scOpPostInjured"; return false; }
            if (m.Mood < 25) { error = "scRefuseMood"; return false; }
            if (StaffCount(o) >= StaffCap(o)) { error = "scOpPostFull"; return false; }
            return true;
        }

        public static bool Post(Member m, EntityAlive npc, Outpost o, EntityPlayerLocal player, out string error)
        {
            if (!CanPost(m, npc, o, out error)) return false;
            try
            {
                m.Status = MemberStatus.Garrison;
                m.OutpostId = o.Id;
                ScRuns.Park(npc);
                ScNpc.SendOnMission(npc, true);
                ScRegistry.SaveAtomic();
                if (player != null) ScCommunity.Tell(player, string.Format(Localization.Get("scOpPosted"), m.Name, o.Name), "");
                ScLog.Out(m.Name + " posted to outpost " + o.Id + " at " + o.Name);
                return true;
            }
            catch (Exception e)
            {
                ScLog.Once("garrison-post", e);
                error = "scInternal";
                return false;
            }
        }

        /// <summary>Home again: resident, unhidden and stationed when loaded (the relink does it when it next loads).</summary>
        public static void Recall(Member m, EntityPlayerLocal player, string tellKey)
        {
            if (m == null || m.Status != MemberStatus.Garrison) return;
            Outpost o = ScOutposts.ById(m.OutpostId);
            RemoveStandIn(m);
            m.Status = MemberStatus.Resident;
            m.OutpostId = -1;
            EntityAlive npc = ScCommunity.FindNpc(m.Id);
            if (npc != null)
            {
                ScNpc.SendOnMission(npc, false);
                ScCommunity.Station(m, npc);
            }
            if (player != null && tellKey != null)
                ScCommunity.Tell(player, string.Format(Localization.Get(tellKey), m.Name, o != null ? o.Name : "?"), "");
            ScLog.Out(m.Name + " recalled home from " + (o != null ? o.Name : "an outpost"));
        }

        /// <summary>Anyone posted to an outpost that no longer exists or is lost comes home (rollover, after a load).</summary>
        public static void Validate(EntityPlayerLocal player)
        {
            var bad = new List<Member>();
            foreach (var m in ScRegistry.Members.Values)
            {
                if (m.Status != MemberStatus.Garrison) continue;
                Outpost o = ScOutposts.ById(m.OutpostId);
                if (o == null || !o.Holding) bad.Add(m);
            }
            foreach (Member m in bad) Recall(m, player, "scOpRecalledGone");
        }

        /// <summary>The outpost was disbanded or lost: everyone posted there comes home.</summary>
        public static void OnOutpostGone(Outpost o, EntityPlayerLocal player)
        {
            foreach (Member m in Staff(o)) Recall(m, player, "scOpRecalledGone");
        }

        // ------------------------------------------------------------------ mood ----

        /// <summary>ScMorale.Recompute: a posted member's mood term.</summary>
        public static int MoodOffset(Member m)
        {
            if (m == null || m.Status != MemberStatus.Garrison) return 0;
            bool loner = false;
            foreach (string t in m.Traits) if (string.Equals(t, "Loner", StringComparison.OrdinalIgnoreCase)) { loner = true; break; }
            int v = loner ? 5 : -3;
            Outpost o = ScOutposts.ById(m.OutpostId);
            if (o != null && o.LastVisitDay >= 0 && ScCommunity.Today - o.LastVisitDay <= 3) v += 3;
            return v;
        }

        // ------------------------------------------------------------------ stand-ins ----

        /// <summary>ScTicker, every second: stand-ins at the outpost the player is at; visits stamped for moods.</summary>
        public static void Tick(World w, EntityPlayerLocal player)
        {
            try
            {
                if (w == null || player == null) return;
                SweepOrphans(w);
                if (ScRegistry.Outposts.Count == 0) return;
                float now = Time.unscaledTime;
                if (now < nextTick) return;
                nextTick = now + 1f;
                int today = ScCommunity.Today;
                Vector3 pp = player.position;
                foreach (Outpost o in ScRegistry.Outposts)
                {
                    if (!o.Holding) continue;
                    float d = ScOutposts.DistanceToBox(o, pp);
                    bool near = d <= ScConfig.OutpostStandInRange;
                    if (near && o.LastVisitDay != today) { o.LastVisitDay = today; }
                    if (near && ScZone.IsLoaded(o.Flag)) Visit(w, player, o, now);
                    List<Member> staff = Staff(o);
                    if (staff.Count == 0) continue;
                    bool keep = d <= ScConfig.OutpostStandInRange * 1.8f && ScZone.IsLoaded(o.Flag);
                    foreach (Member m in staff)
                    {
                        if (m.StandInId >= 0)
                        {
                            EntityAlive s = ScNpc.As(w.GetEntity(m.StandInId));
                            if (s == null || s.IsDead()) { if (s == null) m.StandInId = -1; continue; }
                            if (!keep) RemoveStandIn(m);
                            continue;
                        }
                        if (near && ScZone.IsLoaded(o.Flag)) SpawnStandIn(w, m, o);
                    }
                }
            }
            catch (Exception e)
            {
                ScLog.Once("garrison-tick", e);
            }
        }

        /// <summary>The player is at the outpost: recount what they built (every 10 s) and hand over any stash.</summary>
        private static void Visit(World w, EntityPlayerLocal player, Outpost o, float now)
        {
            float due;
            if (!nextCount.TryGetValue(o.Id, out due) || now >= due)
            {
                nextCount[o.Id] = now + 10f;
                int[] box = o.Box;
                if (ScScan.CountArea(box[0], box[1], box[2], box[3], countScratch))
                {
                    bool changed = false;
                    if (!o.HasInvestBase)
                    {
                        // established while unloaded (the console) or by an older build: this first count is the baseline
                        for (int i = 0; i < 4; i++) o.InvestBase[i] = countScratch[i];
                        o.HasInvestBase = true;
                        changed = true;
                    }
                    for (int i = 0; i < 4; i++)
                    {
                        int above = Math.Max(0, countScratch[i] - o.InvestBase[i]);
                        if (o.Invest[i] != above) { o.Invest[i] = above; changed = true; }
                    }
                    if (changed)
                    {
                        ScLog.Verbose("outpost " + o.Id + " at " + o.Name + ": " + o.Invest[0] + " bed(s), " + o.Invest[1] + " facility(ies), "
                                      + o.Invest[2] + " crop(s), " + o.Invest[3] + " defense(s)");
                        ScRegistry.SaveAtomic();
                    }
                }
            }
            if (o.StashTotal > 0 && ScOutpostPerks.Unstash(o, w, player) > 0) ScRegistry.SaveAtomic();
        }

        private static void SpawnStandIn(World w, Member m, Outpost o)
        {
            try
            {
                int cid = EntityClass.FromString(m.Class);
                if (!ScCommunity.ClassExists(cid)) return;
                int k = Staff(o).IndexOf(m);
                Vector3i near = new Vector3i(o.Flag.x + (k == 0 ? 2 : -2), o.Flag.y, o.Flag.z + 1);
                Vector3 at = ScRadio.SpotNear(w, near, 4);
                EntityAlive s = null;
                try
                {
                    if (m.Look != null) ScLooks.Pending = m.Look.Clone();   // the stand-in IS the survivor
                    s = ScNpc.As(EntityFactory.CreateEntity(cid, at));
                }
                finally { ScLooks.Pending = null; }
                if (s == null) return;
                w.SpawnEntityInWorld(s);
                ScNpc.SetName(s, m.Name, "");          // the class rolls a random name at spawn: the stand-in IS this survivor
                // Dynamic + no Persist: SCore's IsSavedToFile (both classes) skips Dynamic (and Biome) sources, so a stand-in is
                // never written with the chunk - it goes when the outpost unloads (a StaticSpawner one would come back)
                s.SetSpawnerSource(EnumSpawnerSource.Dynamic);
                s.Buffs.SetCustomVar(ScRadio.StandInCvar, m.Id);
                s.Buffs.SetCustomVar(StandInOutpostCvar, o.Id);
                ScSkills.StampCvars(m, s);
                ScTraits.ApplyBuffs(m, s);
                s.Buffs.AddBuff("buffOrderGuard");
                m.StandInId = s.entityId;
                ScLog.Verbose("garrison stand-in for " + m.Name + " at " + o.Name + " (entity " + s.entityId + ")");
            }
            catch (Exception e)
            {
                ScLog.Once("garrison-standin", e);
            }
        }

        public static void RemoveStandIn(Member m)
        {
            try
            {
                if (m == null || m.StandInId < 0) return;
                World w = ScCommunity.World;
                EntityAlive s = w != null ? ScNpc.As(w.GetEntity(m.StandInId)) : null;
                if (s != null && !s.IsDead()) ScRuns.RemoveEntity(s);
                m.StandInId = -1;
            }
            catch (Exception e)
            {
                ScLog.Once("garrison-standin-remove", e);
            }
        }

        /// <summary>ScRadio.OnStandInDied for a posted member: the survivor fell defending the outpost.</summary>
        public static void OnStandInDied(Member m, EntityPlayerLocal player)
        {
            Outpost o = ScOutposts.ById(m.OutpostId);
            m.StandInId = -1;
            EntityAlive real = ScCommunity.FindNpc(m.Id);
            if (real != null) ScRuns.RemoveEntity(real); else ScRegistry.Doomed.Add(m.Id);
            ScCommunity.RecordDeath(m, player, "scOpStaffDied");
            ScLog.Out(m.Name + " died defending " + (o != null ? o.Name : "an outpost"));
        }

        // ------------------------------------------------------------------ workshops ----

        /// <summary>
        /// ScOutposts.Daily: an outpost with a crafter posted turns 2 materials into 1 ammo, one with a chemist
        /// 1 fuel + 1 materials into 1 meds - only out of surplus (the stockpile keeps OutpostWorkshopKeep).
        /// </summary>
        public static void Workshops(int day, EntityPlayerLocal player, StringBuilder summary)
        {
            if (!ScConfig.OutpostWorkshop) return;
            try
            {
                Community c = ScRegistry.Community;
                int keep = Math.Max(0, ScConfig.OutpostWorkshopKeep);
                foreach (Outpost o in ScRegistry.Outposts)
                {
                    if (o.State != OutpostState.Active) continue;
                    bool crafter = false, chemist = false;
                    foreach (Member m in Staff(o))
                    {
                        if (m.Specialty == Specialty.Craftsmanship) crafter = true;
                        if (m.Specialty == Specialty.Chemistry) chemist = true;
                    }
                    string made = null;
                    if (crafter && c.Get(Resource.Materials) >= keep + 2)
                    {
                        c.Add(Resource.Materials, -2);
                        c.Add(Resource.Ammo, 1);
                        made = string.Format(Localization.Get("scOpWorkshopAmmo"), o.Name);
                    }
                    else if (chemist && c.Get(Resource.Materials) >= keep + 1 && c.Get(Resource.Fuel) >= Math.Max(1, keep / 2) + 1)
                    {
                        c.Add(Resource.Materials, -1);
                        c.Add(Resource.Fuel, -1);
                        c.Add(Resource.Meds, 1);
                        made = string.Format(Localization.Get("scOpWorkshopMeds"), o.Name);
                    }
                    if (made == null) continue;
                    if (summary.Length > 0) summary.Append(' ');
                    summary.Append(made);
                    ScLog.Out("workshop: " + made);
                }
            }
            catch (Exception e)
            {
                ScLog.Once("garrison-workshop", e);
            }
        }

        // ------------------------------------------------------------------ walk-ins ----

        /// <summary>
        /// ScOutposts.Daily: each staffed outpost may draw a survivor in - a radio contact whose spawn is the
        /// outpost, so they are there the next time the player visits (the usual favour then applies).
        /// </summary>
        public static void WalkIns(int day, EntityPlayerLocal player, StringBuilder summary)
        {
            try
            {
                if (ScConfig.OutpostWalkInChance <= 0f || ScRegistry.Contacts.Count >= ScConfig.RadioMaxContacts) return;
                World w = ScCommunity.World;
                GameRandom rnd = w != null ? w.GetGameRandom() : null;
                foreach (Outpost o in ScRegistry.Outposts)
                {
                    if (o.State != OutpostState.Active || StaffCount(o) == 0) continue;
                    if (ScRegistry.Contacts.Count >= ScConfig.RadioMaxContacts) break;
                    bool taken = false;
                    foreach (var ct in ScRegistry.Contacts) if (ct.PoiKey == o.Poi) { taken = true; break; }
                    if (taken) continue;
                    float roll = rnd != null ? rnd.RandomFloat : UnityEngine.Random.value;
                    if (roll >= ScConfig.OutpostWalkInChance) continue;
                    Contact ct2 = ScRadio.AddContactAt(o.Poi, o.Name, new Vector3i(o.Flag.x + 3, o.Flag.y, o.Flag.z + 3), player, rnd);
                    if (ct2 == null) continue;
                    if (summary.Length > 0) summary.Append(' ');
                    summary.Append(string.Format(Localization.Get("scOpWalkIn"), ct2.Label, o.Name));
                    if (player != null) ScCommunity.Tell(player, string.Format(Localization.Get("scOpWalkInTip"), ct2.Label, o.Name), "");
                    ScLog.Out("walk-in: " + ct2.Class + " turned up at outpost " + o.Id + " (" + o.Name + ")");
                }
            }
            catch (Exception e)
            {
                ScLog.Once("garrison-walkin", e);
            }
        }

        public static string StatusText(Member m, bool brief)
        {
            Outpost o = PostOf(m);
            return string.Format(Localization.Get(brief ? "scStPostedShort" : "scStPosted"), o != null ? o.Name : "?");
        }
    }
}
