using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Supply runs (State of Decay style): a stationed, healthy, willing survivor is sent out for one of the
    /// five resources. With PhysicalRuns (the default) they walk to a real POI for it and back - see ScRunners;
    /// what follows describes the legacy run, still used without a destination, on a dedicated server and for
    /// runs saved before physical runs existed (RunLeg None).
    /// The entity is hidden in place through SCore's SendOnMission (scale 0, invisible,
    /// ignored by AI) and parked with a Stay order - SendOnMission does NOT stop the entity ticking - and
    /// the run itself is bookkeeping in the registry, so it resolves at the day rollover whether or not the
    /// base is loaded. Outcome: yield (RunYieldBase x Wits x specialty x traits x morale) into the stockpile,
    /// with a risk (RunRiskBase minus combat stars) of coming back injured or not at all. A survivor who dies
    /// on a run is removed like any death; their hidden entity is removed when it is loaded (Doomed list).
    /// </summary>
    public static class ScRuns
    {
        public static int Runners()
        {
            return ScRegistry.CountStatus(MemberStatus.OnRun);
        }

        /// <summary>
        /// Can this member be sent right now? (error = localization key) Round MP2: also a read - a client's window shows its run
        /// buttons from its copy (Ready; it said "host only" there), and Send keeps the server-only gate. A body beyond the
        /// client's 80 m that the server has loaded (the copy's health line) counts as there: the server judges it.
        /// </summary>
        public static bool CanSend(Member m, EntityAlive npc, out string error)
        {
            error = null;
            if (!ScRegistry.Ready) { error = "scHostOnly"; return false; }
            if (m == null) { error = "scInternal"; return false; }
            if (m.Status == MemberStatus.OnRun) { error = "scOnRun"; return false; }
            if (m.Status == MemberStatus.Transit) { error = "scOpOnTheWay"; return false; }     // round op1: on the road to or from an outpost
            if (m.Status != MemberStatus.Resident) { error = "scRunNotHome"; return false; }
            if (ScWatch.OnWatch(m)) { error = "scWatchBusy"; return false; }     // relieve them at the Watch post first (D3)
            if (npc == null && !BodyOnServer(m)) { error = "scNotLoaded"; return false; }      // round MP2: BodyOnServer (a client)
#if SC_V34
            if (ScDowned.RefuseVerb(m)) { error = "scDownedBusy"; return false; }   // round U2: no run for a downed member (mining too)
#endif
            if (m.Injured) { error = "scRunInjured"; return false; }
            if (ScConfig.RefusesForMood(m)) { error = "scRefuseMood"; return false; }   // P2: never by default
            if (Runners() >= ScConfig.MaxRunners) { error = "scRunTooMany"; return false; }
            return true;
        }

        /// <summary>
        /// Round MP2: a client's copy says the server has this member's body loaded (the derived block's health line, ScMirror) -
        /// a body beyond the client's own 80 m. Never on the server: it has the body or not.
        /// </summary>
        private static bool BodyOnServer(Member m)
        {
            return m != null && ScMirror.Has("hp." + m.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        /// <summary>A supply run (member.run on the server, the console). Round MP2: never on a client's copy (E10).</summary>
        public static bool Send(Member m, EntityAlive npc, Resource kind, EntityPlayer player, out string error)
        {
            // luxuries are not fetched (round wl): the dialog and the window never offer it, the console could
            if (kind == Resource.Luxury) { error = "scRunNoLuxury"; return false; }
            if (!ScCommunity.IsHost || !ScRegistry.Loaded) { error = "scHostOnly"; return false; }
            if (!CanSend(m, npc, out error)) return false;
            // round md1: her medical kit topped up from the stockpile before the snapshot (TryCapture) takes it along; a hidden
            // run's parked body keeps it. Never blocks the run
            ScMedic.BeforeRun(m, npc, kind.ToString().ToLowerInvariant() + " run");
#if SC_V34
            ScWork.EndFor(m, "sent on a supply run");   // C1: off the work shift first
            ScMining.ClearJob(m);                       // round 33c (M33): a supply run, not a mining one
#endif
            // round MP0: physical runs only for the host's own player until MP5 rebuilds the runner's capture and re-creation around
            // the nearest player (B: today they follow the host's chunk range and view) - a client's run goes hidden
            if (ScConfig.PhysicalRuns && player is EntityPlayerLocal && player.ChunkObserver != null)
            {
                World pw = ScCommunity.World;
                PrefabInstance dest = PickDestination(kind, pw != null ? pw.GetGameRandom() : null);
                if (dest != null) return SendPhysical(m, npc, kind, player, dest, out error);
                ScLog.Out("no POI " + ScConfig.RunMinDist + "-" + ScConfig.RunMaxDist + " m from home for a " + kind + " run - " + m.Name + " goes on a hidden run instead");
            }
            return SendHidden(m, npc, kind, player, out error);
        }

        /// <summary>
        /// What the run is for, in its lines: the resource ("food"), or (round 33c, M33) a mining run's "stone and ore" / "clay".
        /// </summary>
        public static string What(Member m)
        {
#if SC_V34
            if (ScMining.IsDig(m)) return ScMining.What(m.RunJob);
#endif
            return Localization.Get(ScNames.LocKey(m.RunKind));
        }

        /// <summary>
        /// A hidden ("legacy") run: parked at home, invisible, resolved at the day rollover (Resolve). Also a mining run with no
        /// natural spot in reach (ScMining.Send, round 33c), whose job fields are set before the call.
        /// </summary>
        internal static bool SendHidden(Member m, EntityAlive npc, Resource kind, EntityPlayer player, out string error)
        {
            error = null;
            try
            {
                m.Status = MemberStatus.OnRun;
                ScStuck.ResetGain(m);   // round 29 review L5: no held move back outlives the status
                m.RunKind = kind;
                m.RunEndDay = ScCommunity.Today + Math.Max(1, ScConfig.RunDays);
                Park(npc);
                ScNpc.SendOnMission(npc, true);
                m.RunDistress = false;
                m.RunSafe = false;
                m.EventSpawned = false;
#if SC_V34
                m.RunTalk = 0;
                m.RunRadio = false;                                        // a fresh snapshot (the re-check's LOW-3)
                ScHandheld.NoteCarried(m, npc, "sent on a hidden run");   // R2: a radio of theirs lets them call for help (decision 5)
#endif
                World rw = ScCommunity.World;
                // round MP0: ScRadio takes the local player (another slice's file) - null on a dedicated server: no distress roll there
                bool distress = ScRadio.RollDistress(m, player as EntityPlayerLocal, rw != null ? rw.GetGameRandom() : null);
                ScRegistry.SaveAtomic();
                // round MP4 (part: tells): this file's tells go by localization key (ScCommunity.TellKey / ScPlayers.TellCommunityKey), so each player reads them in their own language
                if (player != null && !distress) ScCommunity.TellKey(player, "scRunSent", "", m.Name, What(m), m.RunEndDay);
                ScLog.Out(m.Name + " left on a supply run for " + WhatLog(m) + ", back on day " + m.RunEndDay);
                return true;
            }
            catch (Exception e)
            {
                ScLog.Once("run-send", e);
                error = "scInternal";
                return false;
            }
        }

        /// <summary>A physical run: the runner walks out of the base toward the destination POI right now.</summary>
        private static bool SendPhysical(Member m, EntityAlive npc, Resource kind, EntityPlayer player, PrefabInstance dest, out string error)
        {
            Vector3 arrive = ScRunners.EdgeNearest(ScCommunity.World, dest, npc.position);
            int dist = Mathf.RoundToInt(ScOutposts.DistanceToHome(dest.boundingBoxPosition, dest.boundingBoxSize));
            return SendPhysicalTo(m, npc, kind, player, dest.boundingBoxPosition, ScSecurity.DisplayName(dest), arrive, dist, out error);
        }

        /// <summary>The log's name for what the run is for: the resource, or (round 33c) mine / clay.</summary>
        private static string WhatLog(Member m)
        {
#if SC_V34
            if (ScMining.IsDig(m)) return ScMining.Key(m.RunJob);
#endif
            return m.RunKind.ToString();
        }

        /// <summary>
        /// A physical run to a destination: destKey (a POI's boundingBoxPosition, or round 33c's natural spot), its name, the point
        /// the runner walks to and the distance the lines name. A mining run's job fields are set before the call.
        /// </summary>
        internal static bool SendPhysicalTo(Member m, EntityAlive npc, Resource kind, EntityPlayer player, Vector3i destKey, string destName, Vector3 arrive, int dist, out string error)
        {
            error = null;
            World w = ScCommunity.World;
            // the snapshot first: without one the registry could not carry them once they are out of sight, and a
            // failed capture must leave the NPC and the record exactly as they were (re-creates re-apply the travel order)
            string snap;
            if (!ScRunners.TryCapture(npc, out snap)) { error = "scInternal"; return false; }
            try
            {
                ulong now = w.worldTime;
                m.Status = MemberStatus.OnRun;
                ScStuck.ResetGain(m);   // round 29 review L5
                m.RunKind = kind;
                m.RunDest = destKey;
                m.RunDestName = destName ?? "";
                m.RunSite = Vector3i.zero;
                m.RunCarry = 0;
                m.RollPending = false;
                m.RunHurt = false;                                         // round r2: a fresh run is not one given up hurt
                m.RunDistress = false;
                m.RunSafe = false;
                m.EventSpawned = false;
                m.StandInId = -1;
                m.EventZombies.Clear();
                m.Offline = false;
#if SC_V34
                m.RunTalk = 0;
                m.RunRadio = false;                                        // a fresh snapshot (the re-check's LOW-3)
                ScHandheld.NoteCarried(m, npc, "set off on a supply run");   // R2: talks on the radio, may radio for help, can be called home
#endif
                if (npc.Buffs.HasCustomVar("onMission") || !npc.enabled) ScNpc.SendOnMission(npc, false);
                ScRunners.ApplyTravelOrder(npc);
                ScNpc.RestoreSpeed(npc);
                if (npc.getNavigator() != null) npc.getNavigator().clearPath();
                if (npc.moveHelper != null) npc.moveHelper.Stop();
                ScRunners.BeginLeg(m, RunLeg.Outbound, npc.position, arrive, now);
                m.Snap = snap;
                ScRunners.OnSent(m, npc);
                ScRegistry.SaveAtomic();
                if (player != null) ScCommunity.TellKey(player, "scRunDeparts", "", m.Name, m.RunDestName, dist, What(m), m.RunEndDay);
                ScLog.Out(m.Name + " set off on foot for " + m.RunDestName + " " + m.RunDest + " (" + dist + " m) to look for " + WhatLog(m) + ", back around day " + m.RunEndDay);
                return true;
            }
            catch (Exception e)
            {
                ScLog.Once("run-send-physical", e);
                // back to how they were: a resident at their post
                try
                {
                    ScCommunity.ClearSynced(npc, ScRunners.OnRunCvar);      // round MP2 (E9)
                    ScRunners.ClearRun(m);
                    ScCommunity.Station(m, npc);
                    ScRunners.RebuildRunnerSet();
                }
                catch (Exception e2) { ScLog.Once("run-send-physical-undo", e2); }
                error = "scInternal";
                return false;
            }
        }

        /// <summary>
        /// Where to look for a resource: a site the radio scouted for it, else a POI whose type produces it (primary
        /// producers first, nearest first, one of the best three), else any scavenging site, else any real POI -
        /// RunMinDist..RunMaxDist from home, never inside the base, never an outpost or an enclave's POI, never where
        /// another runner, radio contact or favour already is.
        /// </summary>
        public static PrefabInstance PickDestination(Resource kind, GameRandom rnd)
        {
            try
            {
                DynamicPrefabDecorator dec = GameManager.Instance != null ? GameManager.Instance.GetDynamicPrefabDecorator() : null;
                if (dec == null || !ScZone.HasHome) return null;
                Vector3i home = ScZone.Home;
                int min = ScConfig.RunMinDist, max = Math.Max(ScConfig.RunMinDist + 50, ScConfig.RunMaxDist);
                var taken = new HashSet<Vector3i>();
                foreach (var om in ScRegistry.Members.Values) if (ScRunners.IsPhysical(om)) taken.Add(om.RunDest);
                foreach (var ct in ScRegistry.Contacts) taken.Add(ct.PoiKey);
                foreach (var p in ScRegistry.Prospects.Values) if (p.Favour == FavourKind.Poi && !p.Done) taken.Add(p.PoiPos);
                ScEncRequests.AddTargets(taken);     // E5: a missing survivor's building, an infestation an enclave asked to clear
                var list = new List<PrefabInstance>();
                dec.GetPrefabsAtXZ(home.x - max, home.x + max, home.z - max, home.z + max, list);
                var ok = new List<KeyValuePair<float, PrefabInstance>>();
                var seen = new HashSet<Vector3i>();
                foreach (PrefabInstance pi in list)
                {
                    if (!ScSecurity.IsRealPoi(pi) || !seen.Add(pi.boundingBoxPosition)) continue;
                    if (ScSecurity.OverlapsHome(pi) || ScOutposts.IsOutpostPoiKey(pi.boundingBoxPosition) || taken.Contains(pi.boundingBoxPosition)) continue;
                    if (ScEnclaves.IsEnclavePoi(pi)) continue;
                    float d = ScOutposts.DistanceToHome(pi.boundingBoxPosition, pi.boundingBoxSize);
                    if (d < min || d > max) continue;
                    ok.Add(new KeyValuePair<float, PrefabInstance>(d, pi));
                }
                if (ok.Count == 0) return null;

                // 1: a site the radio scouted for this resource
                var scouted = new List<PrefabInstance>();
                int today = ScCommunity.Today;
                foreach (Scout sc in ScRegistry.Community.Scouts)
                {
                    if (sc.Res != kind || today >= sc.ExpireDay) continue;
                    foreach (var kv in ok) if (kv.Value.boundingBoxPosition == sc.Poi) { scouted.Add(kv.Value); break; }
                }
                if (scouted.Count > 0) return scouted[rnd != null ? rnd.RandomRange(0, scouted.Count) : 0];

                // 2: a POI type that produces it (primary producers first), 3: a scavenging site, 4: anything
                var rows = new List<KeyValuePair<float, PrefabInstance>>();
                var scav = new List<KeyValuePair<float, PrefabInstance>>();
                foreach (var kv in ok)
                {
                    PoiClass cls = ScPoiTypes.Classify(kv.Value);
                    if (cls == null || !cls.Producing) continue;
                    if (cls.Kind == "row" && cls.Row != null && cls.Row.Weight[(int)kind] > 0f)
                        rows.Add(new KeyValuePair<float, PrefabInstance>(kv.Key + (cls.Row.Primary == kind ? 0f : 100000f), kv.Value));
                    else if (cls.Kind == "scavenge") scav.Add(kv);
                }
                List<KeyValuePair<float, PrefabInstance>> pool = rows.Count > 0 ? rows : scav.Count > 0 ? scav : ok;
                pool.Sort((a, b) => a.Key.CompareTo(b.Key));
                int n = Math.Min(3, pool.Count);
                return pool[rnd != null ? rnd.RandomRange(0, n) : 0].Value;
            }
            catch (Exception e)
            {
                ScLog.Once("run-pickdest", e);
                return null;
            }
        }

        /// <summary>A hidden entity still runs its AI: a Stay order keeps it from roaming or fighting unseen.</summary>
        public static void Park(EntityAlive npc)
        {
            try
            {
                if (npc == null) return;
                if (npc.getNavigator() != null) npc.getNavigator().clearPath();
                if (npc.moveHelper != null) npc.moveHelper.Stop();
                if (EntityUtilities.GetCurrentOrder(npc.entityId) != EntityUtilities.Orders.Stay) EntityUtilities.SetCurrentOrder(npc.entityId, EntityUtilities.Orders.Stay);
            }
            catch (Exception e)
            {
                ScLog.Once("run-park", e);
            }
        }

        /// <summary>Day rollover: every run whose day has come resolves; the rest keep going.</summary>
        public static void Resolve(int day, EntityPlayer player, StringBuilder summary)
        {
            var done = new System.Collections.Generic.List<Member>();
            var late = new System.Collections.Generic.List<Member>();
            foreach (var m in ScRegistry.Members.Values)
            {
                // round op1: a posting traveler on the legs is late the same way (only ever waiting at the gate: ScRunners.ForceHome)
                if (m.Status != MemberStatus.OnRun && !(m.Status == MemberStatus.Transit && ScRunners.IsPhysical(m))) continue;
                if (ScRunners.IsPhysical(m))
                {
                    // a physical run ends when they walk home; the rollover only rescues one far past its ETA
                    // round MP0: near the post = the anchor, as the runner's re-create (MP5: any player)
                    if (m.Offline && day > m.RunEndDay + ScConfig.RunDeadlineGraceDays && (m.RunLeg != RunLeg.Back || ScRunners.PlayerNearPost(m, player))) late.Add(m);
                }
                else if (day >= m.RunEndDay) done.Add(m);
            }
            for (int i = 0; i < late.Count; i++)
            {
                try
                {
                    if (ScRunners.ForceHome(late[i], player)) summary.Append(' ').Append(string.Format(Localization.Get("scDailyRunLate"), late[i].Name));
                }
                catch (Exception e)
                {
                    ScLog.Once("run-forcehome", e);
                }
            }
            if (done.Count == 0) return;
            World w = ScCommunity.World;
            GameRandom rnd = w != null ? w.GetGameRandom() : null;
            Community c = ScRegistry.Community;
            for (int i = 0; i < done.Count; i++)
            {
                Member m = done[i];
                try
                {
                    Resource kind = m.RunKind;
                    float roll = rnd != null ? rnd.RandomFloat : UnityEngine.Random.value;
                    float roll2 = rnd != null ? rnd.RandomFloat : UnityEngine.Random.value;
                    float risk = Mathf.Clamp(ScConfig.RunRiskBase - ScConfig.RunRiskPerCombatStar * (m.Stars(Skill.Fighting) + m.Stars(Skill.Shooting) - 2)
                                             - ScOutpostPerks.EscortRisk(), 0.02f, 0.5f);   // an ammo outpost's armed escort
                    int yield = Yield(m, kind);                            // round ie1: whole bundles, no chance part
                    bool bad = roll < risk;
                    bool dead = bad && roll2 < ScConfig.RunDeathShare;
                    bool rescued = m.RunDistress && m.RunSafe;      // EndDistress below clears the flags (round 33c's haul reads it)
                    if (m.RunDistress)
                    {
                        // a radio distress run: the player's answer decides it, not the dice
                        if (m.RunSafe) { bad = false; dead = false; yield += ScConfig.RescueBundles; }   // round ie1: +1, was x1.25 rounded
                        else { bad = true; dead = roll2 < ScConfig.RunDeathShare; }
                        ScRadio.EndDistress(w, player as EntityPlayerLocal, m);     // round MP0: the local player's journal (another slice's file). MP4: quests to the right player
                    }

                    if (dead)
                    {
                        summary.Append(' ').Append(string.Format(Localization.Get("scDailyRunDied"), m.Name));
                        ScLog.Out(m.Name + " did not come back from the supply run (risk " + risk.ToString("0.00") + ")");
                        Die(m, player);
                        continue;
                    }
                    if (bad) { m.Injured = true; yield = Math.Max(0, yield / 2); }
                    string got = null;
#if SC_V34
                    // round 33c (M33): a hidden mining run brings real items - onto the record, put away by ScMining.Tick
                    if (ScMining.IsDig(m)) got = ScMining.ResolveHidden(m, bad, rescued);
                    else
#endif
                    {
                        c.Add(kind, yield);
                        ScSkills.AddXp(m, Skill.Wits, ScConfig.RunWitsXp);
                        if (SpecialtyFor(kind) == m.Specialty) ScSkills.AddSpecialtyXp(m, 3f);
                    }
                    m.Status = MemberStatus.Resident;
                    m.RunEndDay = -1;
#if SC_V34
                    m.RunRadio = false;     // R2: the run's radio state ends with it
                    m.RunTalk = 0;
                    ScMining.ClearJob(m);
#endif
                    // the entity: un-hide and re-station now when loaded; the relink does it when it next loads
                    EntityAlive npc = ScCommunity.FindNpc(m.Id);
                    if (npc != null)
                    {
                        ScNpc.SendOnMission(npc, false);
                        ScCommunity.Station(m, npc);
                    }
                    if (got != null)
                    {
                        // round 33c: the haul by name; the crates line follows from ScMining.Tick
                        summary.Append(' ').Append(string.Format(Localization.Get(bad ? "scDailyMineHurt" : "scDailyMineBack"), m.Name, got));
                        if (player != null) ScPlayers.TellCommunityKey(bad ? "scMineHurt" : "scMineBack", bad ? "ui_denied" : "", m.Name, got);   // round MP0: community news
                        ScLog.Out(m.Name + " is back from the hidden mining run (risk " + risk.ToString("0.00") + ")");
                    }
                    else
                    {
                        summary.Append(' ').Append(string.Format(Localization.Get(bad ? "scDailyRunHurt" : "scDailyRunBack"), m.Name, yield, Localization.Get(ScNames.LocKey(kind))));
                        if (player != null) ScPlayers.TellCommunityKey(bad ? "scRunHurt" : "scRunBack", bad ? "ui_denied" : "", m.Name, yield, ScCommunity.LocArg(ScNames.LocKey(kind)));   // round MP0: community news
                        ScLog.Out(m.Name + " is back from the supply run: " + yield + " " + kind + (bad ? " (injured)" : "") + " (risk " + risk.ToString("0.00") + ")");
                    }
                }
                catch (Exception e)
                {
                    ScLog.Once("run-resolve", e);
                    m.Status = MemberStatus.Resident;
                    m.RunEndDay = -1;
#if SC_V34
                    m.RunRadio = false;
                    m.RunTalk = 0;
                    ScMining.ClearJob(m);
#endif
                }
            }
        }

        /// <summary>
        /// The run's haul in whole bundles - the one function the hidden runs (Resolve), the physical ones (ScRunners.LeaveSite)
        /// and the console's "expected" read. Round ie1 (the user's decisions): RunYieldBase, +1 per RunWitsStarsPerBundle Wits
        /// stars (+1 at 3, +2 at 6), +1 per RunSpecialistStarsPerBundle stars of the matching specialty, +OutpostMotorPoolBundles
        /// with a fuel outpost's motor pool, +HighMoraleRunBundles above the high-morale mark, then her traits' runbundles=
        /// (Hoarder +1, Tinkerer +1 on materials and fuel runs, Scavenger +1 on a haul of 4 or more); at least 1. A rescue (+1),
        /// a hurt runner (half, rounded down) and a recall (the share, rounded down) are applied where the haul is fixed. It was
        /// a chain of multipliers rounded half to even, so Hoarder did nothing on a 2-bundle run, and Scavenger a dice roll.
        /// </summary>
        public static int Yield(Member m, Resource kind)
        {
            int y = ScConfig.RunYieldBase;
            y += Per(m.Stars(Skill.Wits), ScConfig.RunWitsStarsPerBundle);
            if (SpecialtyFor(kind) != Specialty.None && SpecialtyFor(kind) == m.Specialty) y += Per(m.SpecLevel, ScConfig.RunSpecialistStarsPerBundle);
            if (ScOutpostPerks.PerkActive(Resource.Fuel)) y += ScConfig.OutpostMotorPoolBundles;   // a fuel outpost's motor pool
            if (ScRegistry.Community.Morale > ScConfig.HighMoraleThreshold) y += ScConfig.HighMoraleRunBundles;
            y += ScTraits.RunBundles(m, kind, y);
            return Math.Max(1, y);
        }

        /// <summary>1 per that many stars, the remainder dropped (0 stars per bundle = no bonus).</summary>
        private static int Per(int stars, int starsPerBundle) { return starsPerBundle > 0 ? Math.Max(0, stars) / starsPerBundle : 0; }

        /// <summary>
        /// Which specialty knows where to find a resource: Mechanics for materials and fuel, Chemistry for medicine, Cooking
        /// for food, Gardening for water (round wl: wells, springs, rain barrels).
        /// </summary>
        public static Specialty SpecialtyFor(Resource kind)
        {
            switch (kind)
            {
                case Resource.Materials:
                case Resource.Fuel: return Specialty.Mechanics;
                case Resource.Meds: return Specialty.Chemistry;
                case Resource.Food: return Specialty.Cooking;
                case Resource.Water: return Specialty.Gardening;
                default: return Specialty.None;
            }
        }

        private static void Die(Member m, EntityPlayer player)
        {
            EntityAlive npc = ScCommunity.FindNpc(m.Id);
            ScCommunity.RecordDeath(m, player, "scRunDied");     // round MP0: any player - the news goes to the community
            if (npc != null)
            {
                RemoveEntity(npc);
            }
            else
            {
                ScRegistry.Doomed.Add(m.Id);        // the hidden entity is removed when its chunk next loads
            }
        }

        /// <summary>Removes a hidden runner's entity for good (no corpse, no chunk record).</summary>
        public static void RemoveEntity(EntityAlive npc)
        {
            try
            {
                World w = ScCommunity.World;
                if (w == null || npc == null) return;
                npc.Buffs.RemoveCustomVar("Persist");
                w.RemoveEntity(npc.entityId, EnumRemoveEntityReason.Killed);
            }
            catch (Exception e)
            {
                ScLog.Once("run-remove", e);
            }
        }

        public static string StatusLine(Member m)
        {
            if (m == null || m.Status != MemberStatus.OnRun) return "";
            string res = What(m);
            if (!ScRunners.IsPhysical(m)) return string.Format(Localization.Get("scWinOnRun"), res, m.RunEndDay);
            int far = Mathf.RoundToInt(ScRunners.XZ(ScRunners.Where(m), ScCommunity.PostCenter(m)));
            string site = "scWinRunSite";
#if SC_V34
            if (ScMining.IsDig(m)) site = m.RunJob == RunJob.Clay ? "scWinClaySite" : "scWinMineSite";   // round 33c: digging, not searching
#endif
            switch (m.RunLeg)
            {
                case RunLeg.Outbound: return string.Format(Localization.Get("scWinRunOut"), res, m.RunDestName, far, m.RunEndDay);
                case RunLeg.AtSite: return string.Format(Localization.Get(ScRunners.Pinned(m) ? "scWinRunPinned" : site), res, m.RunDestName, far, m.RunEndDay);
                case RunLeg.Inbound: return string.Format(Localization.Get(m.RunHurt ? "scWinRunHomeHurt" : "scWinRunHome"), res, m.RunDestName, far, m.RunEndDay);   // round r2
                default: return string.Format(Localization.Get("scWinRunBack"), res, m.RunDestName, far, m.RunEndDay);
            }
        }

        /// <summary>The roster's short status for a runner.</summary>
        public static string ShortStatus(Member m, bool brief)
        {
            if (!ScRunners.IsPhysical(m)) return string.Format(Localization.Get(brief ? "scStOnRunShort" : "scStOnRun"), m.RunEndDay);
            string key;
            switch (m.RunLeg)
            {
                case RunLeg.Outbound: key = "scStRunOut"; break;
                case RunLeg.AtSite:
                    key = ScRunners.Pinned(m) ? "scStRunPinned" : "scStRunSite";
#if SC_V34
                    if (!ScRunners.Pinned(m) && ScMining.IsDig(m)) key = m.RunJob == RunJob.Clay ? "scStClaySite" : "scStMineSite";   // round 33c
#endif
                    break;
                case RunLeg.Inbound: key = m.RunHurt ? "scStRunHomeHurt" : "scStRunHome"; break;   // round r2
                default: key = "scStRunBack"; break;
            }
            return string.Format(Localization.Get(key), m.RunDestName, m.RunEndDay);
        }
    }
}
