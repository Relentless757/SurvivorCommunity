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

        /// <summary>Can this member be sent right now? (error = localization key)</summary>
        public static bool CanSend(Member m, EntityAlive npc, out string error)
        {
            error = null;
            if (!ScCommunity.IsHost) { error = "scHostOnly"; return false; }
            if (m == null) { error = "scInternal"; return false; }
            if (m.Status == MemberStatus.OnRun) { error = "scOnRun"; return false; }
            if (m.Status != MemberStatus.Resident) { error = "scRunNotHome"; return false; }
            if (ScWatch.OnWatch(m)) { error = "scWatchBusy"; return false; }     // relieve them at the Watch post first (D3)
            if (npc == null) { error = "scNotLoaded"; return false; }
            if (m.Injured) { error = "scRunInjured"; return false; }
            if (m.Mood < 25) { error = "scRefuseMood"; return false; }
            if (Runners() >= ScConfig.MaxRunners) { error = "scRunTooMany"; return false; }
            return true;
        }

        public static bool Send(Member m, EntityAlive npc, Resource kind, EntityPlayerLocal player, out string error)
        {
            // luxuries are not fetched (round wl): the dialog and the window never offer it, the console could
            if (kind == Resource.Luxury) { error = "scRunNoLuxury"; return false; }
            if (!CanSend(m, npc, out error)) return false;
            if (ScConfig.PhysicalRuns && player != null && player.ChunkObserver != null)
            {
                World pw = ScCommunity.World;
                PrefabInstance dest = PickDestination(kind, pw != null ? pw.GetGameRandom() : null);
                if (dest != null) return SendPhysical(m, npc, kind, player, dest, out error);
                ScLog.Out("no POI " + ScConfig.RunMinDist + "-" + ScConfig.RunMaxDist + " m from home for a " + kind + " run - " + m.Name + " goes on a hidden run instead");
            }
            try
            {
                m.Status = MemberStatus.OnRun;
                m.RunKind = kind;
                m.RunEndDay = ScCommunity.Today + Math.Max(1, ScConfig.RunDays);
                Park(npc);
                ScNpc.SendOnMission(npc, true);
                m.RunDistress = false;
                m.RunSafe = false;
                m.EventSpawned = false;
                World rw = ScCommunity.World;
                bool distress = ScRadio.RollDistress(m, player, rw != null ? rw.GetGameRandom() : null);
                ScRegistry.SaveAtomic();
                if (player != null && !distress) ScCommunity.Tell(player, string.Format(Localization.Get("scRunSent"), m.Name, Localization.Get(ScNames.LocKey(kind)), m.RunEndDay), "");
                ScLog.Out(m.Name + " left on a supply run for " + kind + ", back on day " + m.RunEndDay);
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
        private static bool SendPhysical(Member m, EntityAlive npc, Resource kind, EntityPlayerLocal player, PrefabInstance dest, out string error)
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
                m.RunKind = kind;
                m.RunDest = dest.boundingBoxPosition;
                m.RunDestName = ScSecurity.DisplayName(dest);
                m.RunSite = Vector3i.zero;
                m.RunCarry = 0;
                m.RollPending = false;
                m.RunDistress = false;
                m.RunSafe = false;
                m.EventSpawned = false;
                m.StandInId = -1;
                m.EventZombies.Clear();
                m.Offline = false;
                if (npc.Buffs.HasCustomVar("onMission") || !npc.enabled) ScNpc.SendOnMission(npc, false);
                ScRunners.ApplyTravelOrder(npc);
                ScNpc.RestoreSpeed(npc);
                if (npc.getNavigator() != null) npc.getNavigator().clearPath();
                if (npc.moveHelper != null) npc.moveHelper.Stop();
                ScRunners.BeginLeg(m, RunLeg.Outbound, npc.position, ScRunners.EdgeNearest(w, dest, npc.position), now);
                m.Snap = snap;
                ScRunners.OnSent(m, npc);
                ScRegistry.SaveAtomic();
                int dist = Mathf.RoundToInt(ScOutposts.DistanceToHome(dest.boundingBoxPosition, dest.boundingBoxSize));
                if (player != null) ScCommunity.Tell(player, string.Format(Localization.Get("scRunDeparts"), m.Name, m.RunDestName, dist, Localization.Get(ScNames.LocKey(kind)), m.RunEndDay), "");
                ScLog.Out(m.Name + " set off on foot for " + m.RunDestName + " " + m.RunDest + " (" + dist + " m) to look for " + kind + ", back around day " + m.RunEndDay);
                return true;
            }
            catch (Exception e)
            {
                ScLog.Once("run-send-physical", e);
                // back to how they were: a resident at their post
                try
                {
                    npc.Buffs.RemoveCustomVar(ScRunners.OnRunCvar);
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
        /// RunMinDist..RunMaxDist from home, never inside the base, never an outpost, never where another runner,
        /// radio contact or favour already is.
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
                var list = new List<PrefabInstance>();
                dec.GetPrefabsAtXZ(home.x - max, home.x + max, home.z - max, home.z + max, list);
                var ok = new List<KeyValuePair<float, PrefabInstance>>();
                var seen = new HashSet<Vector3i>();
                foreach (PrefabInstance pi in list)
                {
                    if (!ScSecurity.IsRealPoi(pi) || !seen.Add(pi.boundingBoxPosition)) continue;
                    if (ScSecurity.OverlapsHome(pi) || ScOutposts.IsOutpostPoiKey(pi.boundingBoxPosition) || taken.Contains(pi.boundingBoxPosition)) continue;
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
        public static void Resolve(int day, EntityPlayerLocal player, StringBuilder summary)
        {
            var done = new System.Collections.Generic.List<Member>();
            var late = new System.Collections.Generic.List<Member>();
            foreach (var m in ScRegistry.Members.Values)
            {
                if (m.Status != MemberStatus.OnRun) continue;
                if (ScRunners.IsPhysical(m))
                {
                    // a physical run ends when they walk home; the rollover only rescues one far past its ETA
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
                    int yield = Yield(m, kind);
                    bool bad = roll < risk;
                    bool dead = bad && roll2 < ScConfig.RunDeathShare;
                    if (m.RunDistress)
                    {
                        // a radio distress run: the player's answer decides it, not the dice
                        if (m.RunSafe) { bad = false; dead = false; yield = Mathf.RoundToInt(yield * ScConfig.RescueBonus); }
                        else { bad = true; dead = roll2 < ScConfig.RunDeathShare; }
                        ScRadio.EndDistress(w, player, m);
                    }

                    if (dead)
                    {
                        summary.Append(' ').Append(string.Format(Localization.Get("scDailyRunDied"), m.Name));
                        ScLog.Out(m.Name + " did not come back from the supply run (risk " + risk.ToString("0.00") + ")");
                        Die(m, player);
                        continue;
                    }
                    if (bad) { m.Injured = true; yield = Math.Max(0, yield / 2); }
                    c.Add(kind, yield);
                    ScSkills.AddXp(m, Skill.Wits, ScConfig.RunWitsXp);
                    if (SpecialtyFor(kind) == m.Specialty) ScSkills.AddSpecialtyXp(m, 3f);
                    m.Status = MemberStatus.Resident;
                    m.RunEndDay = -1;
                    // the entity: un-hide and re-station now when loaded; the relink does it when it next loads
                    EntityAlive npc = ScCommunity.FindNpc(m.Id);
                    if (npc != null)
                    {
                        ScNpc.SendOnMission(npc, false);
                        ScCommunity.Station(m, npc);
                    }
                    summary.Append(' ').Append(string.Format(Localization.Get(bad ? "scDailyRunHurt" : "scDailyRunBack"), m.Name, yield, Localization.Get(ScNames.LocKey(kind))));
                    if (player != null) ScCommunity.Tell(player, string.Format(Localization.Get(bad ? "scRunHurt" : "scRunBack"), m.Name, yield, Localization.Get(ScNames.LocKey(kind))), bad ? "ui_denied" : "");
                    ScLog.Out(m.Name + " is back from the supply run: " + yield + " " + kind + (bad ? " (injured)" : "") + " (risk " + risk.ToString("0.00") + ")");
                }
                catch (Exception e)
                {
                    ScLog.Once("run-resolve", e);
                    m.Status = MemberStatus.Resident;
                    m.RunEndDay = -1;
                }
            }
        }

        public static int Yield(Member m, Resource kind)
        {
            float y = ScConfig.RunYieldBase;
            y *= 1f + ScConfig.RunYieldPerWitsStar * (m.Stars(Skill.Wits) - 1);
            if (SpecialtyFor(kind) == m.Specialty) y *= 1f + ScConfig.RunSpecialistPerStar * Math.Max(0, m.SpecLevel - 1);
            y *= ScTraits.RunYieldMult(m);
            if (ScOutpostPerks.PerkActive(Resource.Fuel)) y *= ScConfig.OutpostMotorPoolMult;   // a fuel outpost's motor pool
            if (ScRegistry.Community.Morale > ScConfig.HighMoraleThreshold) y *= 1.25f;
            return Math.Max(1, Mathf.RoundToInt(y));
        }

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

        private static void Die(Member m, EntityPlayerLocal player)
        {
            EntityAlive npc = ScCommunity.FindNpc(m.Id);
            ScCommunity.RecordDeath(m, player, "scRunDied");
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
            string res = Localization.Get(ScNames.LocKey(m.RunKind));
            if (!ScRunners.IsPhysical(m)) return string.Format(Localization.Get("scWinOnRun"), res, m.RunEndDay);
            int far = Mathf.RoundToInt(ScRunners.XZ(ScRunners.Where(m), ScCommunity.PostCenter(m)));
            switch (m.RunLeg)
            {
                case RunLeg.Outbound: return string.Format(Localization.Get("scWinRunOut"), res, m.RunDestName, far, m.RunEndDay);
                case RunLeg.AtSite: return string.Format(Localization.Get(ScRunners.Pinned(m) ? "scWinRunPinned" : "scWinRunSite"), res, m.RunDestName, far, m.RunEndDay);
                case RunLeg.Inbound: return string.Format(Localization.Get("scWinRunHome"), res, m.RunDestName, far, m.RunEndDay);
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
                case RunLeg.AtSite: key = ScRunners.Pinned(m) ? "scStRunPinned" : "scStRunSite"; break;
                case RunLeg.Inbound: key = "scStRunHome"; break;
                default: key = "scStRunBack"; break;
            }
            return string.Format(Localization.Get(key), m.RunDestName, m.RunEndDay);
        }
    }
}
