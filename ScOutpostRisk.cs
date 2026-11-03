using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Outposts v2, risk (Outposts_Plan.md section 3), all behind OutpostAttacks (off by default) except the
    /// good-news caches (OutpostGoodNews, on):
    ///   Security   0-100 from the registry: base, staff and their combat stars, the POI tier, a recent visit, ammo
    ///              in the stockpile, defenses built there (ScScan counts them), and the afterglow of won fights.
    ///   Attacks    each rollover an outpost may be hit (chance falls with security, output mode scales it). With the
    ///              home radio on, the call comes in with OutpostAttackHours to get there. Arrive and the attackers
    ///              spawn around the POI (the garrison's stand-ins fight too); kill them all and it holds, better for
    ///              it. Unanswered, it is settled on security: Hold, Damaged (no yield for OutpostDamagedDays, or
    ///              repair now for OutpostRepairMats), or Overrun (the POI's sleepers are re-armed: clear it again;
    ///              the garrison falls back home hurt - nobody dies in a fight the player did not attend).
    ///   Siege      at the end of a blood moon every outpost rolls the same, unless the player stood there.
    ///   Neglect    an unstaffed outpost unvisited for OutpostNeglectDays gets one sleeper area re-armed.
    ///   Raiders    past OutpostRaiderGamestage, raiders may demand supplies for an outpost: pay on the radio, or
    ///              refuse (or ignore until the next rollover) and they attack it. E10 (ScEncRaids, with EnclaveRaids): the
    ///              demand and the raid are a hostile enclave's in range (Outpost.RaidEnclave), its own people come, and
    ///              with none in range there is no raider demand.
    ///   Good news  a cache too big to carry: bundles wait at the outpost for the player (the claim's box).
    /// Host only. Attack entities and blood-moon presence are session state; everything else persists.
    /// </summary>
    public static class ScOutpostRisk
    {
        public const string AttackNav = "sc_outpost_attack";
        /// <summary>Outcome's results (E10: ScEncRaids.End reads them).</summary>
        internal const string HeldResult = "held", DamagedResult = "damaged", OverrunResult = "overrun";

        private sealed class Live
        {
            public bool Spawned;
            public readonly List<int> Ids = new List<int>();
            public bool BloodMoonHere;
            /// <summary>E10: an enclave's raid found nobody who could come yet with the player there (ScEncRaids.SpawnParty's -1; the E11 review's L1).</summary>
            public bool Waited;
        }

        private static readonly Dictionary<int, Live> live = new Dictionary<int, Live>();
        private static readonly Dictionary<int, Vector3> markers = new Dictionary<int, Vector3>();
        private static bool markersRestored;
        private static bool wasBloodMoon;
        private static float nextTick;
        private static int lastClass = -1;

        public static void Reset()
        {
            live.Clear();
            markers.Clear();
            markersRestored = false;
            wasBloodMoon = false;
            nextTick = 0f;
        }

        public static void OnRegistryChanged()
        {
            live.Clear();
            markers.Clear();
            markersRestored = false;
            ScEncRaids.OnRegistryChanged();      // E10: no raid party is out after a load
        }

        private static Live L(Outpost o)
        {
            Live l;
            if (!live.TryGetValue(o.Id, out l)) live[o.Id] = l = new Live();
            return l;
        }

        // ------------------------------------------------------------------ security ----

        public static int Security(Outpost o)
        {
            if (o == null) return 0;
            float s = ScConfig.OutpostSecBase;
            List<Member> staff = ScGarrison.Staff(o);
            s += ScConfig.OutpostSecPerStaffer * staff.Count;
            foreach (Member m in staff) s += ScConfig.OutpostSecPerCombatStar * Math.Max(0, m.Stars(Skill.Fighting) + m.Stars(Skill.Shooting) - 2);
            s -= ScConfig.OutpostSecPerTier * o.Tier;
            if (o.LastVisitDay >= 0 && ScCommunity.Today - o.LastVisitDay <= 3) s += ScConfig.OutpostSecVisited;
            if (ScRegistry.Community.Get(Resource.Ammo) <= 0) s -= ScConfig.OutpostSecNoAmmo;
            s += Math.Min(ScConfig.OutpostSecDefenseCap, ScConfig.OutpostSecPerDefense * o.Invest[3]);
            s += o.SecBonus;
            return Mathf.Clamp(Mathf.RoundToInt(s), 0, 100);
        }

        /// <summary>Is the rollover into 'day' damaged (no yield)? The UI asks about the next one: IsDamaged(o, Today + 1).</summary>
        public static bool IsDamaged(Outpost o, int day) { return o != null && o.DamagedUntil >= day; }

        public static bool DamagedNow(Outpost o) { return IsDamaged(o, ScCommunity.Today + 1); }

        public static bool UnderAttack(Outpost o) { return o != null && o.AttackUntil > 0; }

        public static bool HasDemand(Outpost o) { return o != null && o.DemandUntil >= 0; }

        /// <summary>An outpost attack is being fought (its attackers are in the world): a home attack waits (one live attack at a time).</summary>
        public static bool AnyLive()
        {
            foreach (Outpost o in ScRegistry.Outposts)
            {
                Live l;
                if (UnderAttack(o) && live.TryGetValue(o.Id, out l) && l.Spawned) return true;
            }
            return false;
        }

        /// <summary>
        /// An outpost attack is being fought near 'at': AnyLive's test, and that outpost's footprint within twice
        /// RescueTriggerRange of the point (the range its fight is judged in). A fight the player walked away from stays live
        /// in name until its AttackUntil (OutpostAttackHours) with nobody fighting it, so it must not hold what happens
        /// elsewhere: an enclave's siege (its call and arrival) and the base attack's call (the E5 review's M1 and N5).
        /// </summary>
        public static bool AnyLiveNear(Vector3 at)
        {
            float range = ScConfig.RescueTriggerRange * 2f;
            foreach (Outpost o in ScRegistry.Outposts)
            {
                Live l;
                if (UnderAttack(o) && live.TryGetValue(o.Id, out l) && l.Spawned && ScOutposts.DistanceToBox(o, at) <= range) return true;
            }
            return false;
        }

        /// <summary>Game hours (rounded up) left until an open attack's deadline.</summary>
        internal static int HoursLeft(Outpost o)
        {
            World w = ScCommunity.World;
            ulong now = w != null ? w.worldTime : 0UL;
            return o != null && o.AttackUntil > now ? (int)((o.AttackUntil - now + 999UL) / 1000UL) : 0;
        }

        /// <summary>A few words for the window / radio: under attack, damaged, raiders' demand (E10: naming the enclave behind it).</summary>
        public static string Alert(Outpost o)
        {
            if (o == null) return "";
            Enclave raiders = ScEncRaids.EnclaveOf(o);
            if (UnderAttack(o) && o.AttackHeard)
            {
                int hours = HoursLeft(o);
                if (o.AttackRaiders && raiders != null) return string.Format(Localization.Get("scEncRaidAlertRaid"), hours, raiders.Name);
                return string.Format(Localization.Get(o.AttackRaiders ? "scRiskAlertRaid" : "scRiskAlertAttack"), hours);
            }
            if (HasDemand(o))
                return raiders != null ? string.Format(Localization.Get("scEncRaidAlertDemand"), o.DemandAmmo, o.DemandFood, raiders.Name)
                                       : string.Format(Localization.Get("scRiskAlertDemand"), o.DemandAmmo, o.DemandFood);
            if (DamagedNow(o)) return string.Format(Localization.Get("scRiskAlertDamaged"), o.DamagedUntil + 1);
            return "";
        }

        /// <summary>The radio window's demand row (E10: the enclave behind it named).</summary>
        public static string DemandText(Outpost o)
        {
            if (o == null) return "";
            Enclave raiders = ScEncRaids.EnclaveOf(o);
            return raiders != null ? string.Format(Localization.Get("scEncRaidDemandText"), o.DemandAmmo, o.DemandFood, o.Name, raiders.Name)
                                   : string.Format(Localization.Get("scRadioDemandText"), o.DemandAmmo, o.DemandFood, o.Name);
        }

        // ------------------------------------------------------------------ the day ----

        /// <summary>ScOutposts.Daily, after the deliveries: fades, rolls attacks, demands, neglect and good news.</summary>
        public static void Daily(int day, EntityPlayerLocal player, StringBuilder summary)
        {
            try
            {
                World w = ScCommunity.World;
                GameRandom rnd = w != null ? w.GetGameRandom() : null;
                bool radio = ScOutposts.RadioOnlineAnywhere();   // (triumph fades in ScOutposts.Daily, with or without outposts)
                int gs = 0;
                try { gs = player != null ? player.gameStage : 0; } catch (Exception) { }
                var list = new List<Outpost>(ScRegistry.Outposts);
                foreach (Outpost o in list)
                {
                    if (!o.Holding) continue;
                    o.SecBonus = Math.Max(0f, o.SecBonus - ScConfig.OutpostSecBonusFade);
                    // an unpaid demand runs out: the raiders come (with the risk features off it simply lapses). E10: an
                    // enclave's demand comes only while that group can still raid
                    if (HasDemand(o) && day > o.DemandUntil)
                    {
                        o.DemandUntil = -1;
                        if (ScConfig.OutpostAttacks && ScEncRaids.MayCome(o, player)) StartAttack(o, true, player, radio);
                        else ScEncRaids.Unstamp(o);
                    }
                    if (o.State != OutpostState.Active) continue;

                    if (ScConfig.OutpostGoodNews && Roll(rnd) < ScConfig.OutpostGoodNewsChance) GoodNews(o, player, radio, rnd, summary);
                    if (!ScConfig.OutpostAttacks) continue;
                    if (day - o.Day < 2) continue;                   // a fresh outpost gets a couple of quiet days

                    if (!UnderAttack(o) && Roll(rnd) < AttackChance(o)) { StartAttack(o, false, player, radio); continue; }
                    if (!UnderAttack(o) && !HasDemand(o) && gs >= ScConfig.OutpostRaiderGamestage)
                    {
                        if (ScEncRaids.Configured)
                        {
                            // E10: the nearest hostile enclave in range that can raid rolls its chance; with none in range,
                            // no raider demand at all (the nameless roll below is replaced; a world still starting skips it)
                            Enclave raider = ScEncRaids.On ? ScEncRaids.RaiderFor(o) : null;
                            if (raider != null && Roll(rnd) < ScEncRaids.ChanceFor(raider))
                            {
                                ScEncRaids.Demand(o, raider, day, player, summary, "the day's roll");
                                continue;
                            }
                        }
                        else if (Roll(rnd) < ScConfig.OutpostRaiderChance)
                        {
                            o.DemandUntil = day;                          // answer before the next rollover
                            o.DemandAmmo = ScConfig.OutpostDemandAmmo;
                            o.DemandFood = ScConfig.OutpostDemandFood;
                            ScEncRaids.Unstamp(o);
                            if (player != null) ScCommunity.Tell(player, string.Format(Localization.Get("scRiskDemand"), o.DemandAmmo, o.DemandFood, o.Name), "ui_denied");
                            if (summary.Length > 0) summary.Append(' ');
                            summary.Append(string.Format(Localization.Get("scRiskDemandLine"), o.Name));
                            ScLog.Out("raiders demand " + o.DemandAmmo + " ammo + " + o.DemandFood + " food for outpost " + o.Id + " (" + o.Name + ")");
                            continue;
                        }
                    }
                    int quiet = Math.Max(Math.Max(o.LastVisitDay, o.NeglectDay), o.Day);
                    if (ScGarrison.StaffCount(o) == 0 && day - quiet >= Math.Max(1, ScConfig.OutpostNeglectDays))
                        Reinfest(o, player, summary);
                }
            }
            catch (Exception e)
            {
                ScLog.Once("risk-daily", e);
            }
        }

        private static float Roll(GameRandom rnd) { return rnd != null ? rnd.RandomFloat : UnityEngine.Random.value; }

        public static float AttackChance(Outpost o)
        {
            float c = ScConfig.OutpostAttackChance * ScOutpostPerks.ModeRisk(o) * (100 - Security(o)) / 65f;
            return Mathf.Clamp(c, 0f, 0.5f);
        }

        /// <summary>An attack call: the deadline, the marker, and (radio on) the call itself (E10: an enclave's raid names them).</summary>
        public static void StartAttack(Outpost o, bool raiders, EntityPlayerLocal player, bool radio)
        {
            World w = ScCommunity.World;
            if (w == null || o == null) return;
            ScEncRaids.OnAttackStart(o);        // E10: a raid party still out there goes home first
            o.AttackUntil = w.worldTime + (ulong)Mathf.Max(1, ScConfig.OutpostAttackHours) * 1000UL;
            o.AttackRaiders = raiders;
            o.AttackHeard = radio;
            Live l = L(o);
            l.Spawned = false;
            l.Waited = false;
            l.Ids.Clear();
            if (radio) AddMarker(o, player);
            Enclave by = raiders ? ScEncRaids.EnclaveOf(o) : null;
            if (player != null && radio)
                ScCommunity.Tell(player, by != null ? string.Format(Localization.Get("scEncRaidCall"), o.Name, ScConfig.OutpostAttackHours, by.Name)
                                                    : string.Format(Localization.Get(raiders ? "scRiskRaidCall" : "scRiskAttackCall"), o.Name, ScConfig.OutpostAttackHours), "ui_denied");
            ScLog.Out((raiders ? "raiders attack" : "attack on") + " outpost " + o.Id + " (" + o.Name + ")" + (by != null ? " - enclave " + by.Id + " '" + by.Name + "'" : "")
                      + ", security " + Security(o) + (radio ? "" : " - the radio is silent, nobody heard"));
            ScRegistry.SaveAtomic();
        }

        /// <summary>E10: an open attack called off with no outcome (ScEncRaids: the enclave behind a raid can't come): no deadline, no fight, no marker.</summary>
        internal static void CallOff(Outpost o)
        {
            if (o == null) return;
            o.AttackUntil = 0;
            Live l;
            if (live.TryGetValue(o.Id, out l))
            {
                l.Spawned = false;
                l.Ids.Clear();
            }
            RemoveMarker(o);
        }

        private static void GoodNews(Outpost o, EntityPlayerLocal player, bool radio, GameRandom rnd, StringBuilder summary)
        {
            int n = rnd != null ? rnd.RandomRange(2, 5) : 3;
            for (int i = 0; i < n; i++)
            {
                // mostly what the outpost makes, now and then something else
                Resource r = (!o.Scavenge && Roll(rnd) < 0.6f) ? o.Primary : (Resource)(rnd != null ? rnd.RandomRange(0, ScNames.ClassicCount) : 0);   // the five classic ones: no luxury caches
                o.Stash[(int)r]++;
            }
            if (player != null && radio) ScCommunity.Tell(player, string.Format(Localization.Get("scRiskGoodNews"), o.Name, n), "");
            if (summary.Length > 0) summary.Append(' ');
            summary.Append(string.Format(Localization.Get("scRiskGoodNewsLine"), o.Name, n));
            ScLog.Out("good news: a cache of " + n + " bundle(s) at outpost " + o.Id + " (" + o.Name + ")");
        }

        /// <summary>Neglect: one cleared sleeper area of the POI is re-armed.</summary>
        private static void Reinfest(Outpost o, EntityPlayerLocal player, StringBuilder summary)
        {
            World w = ScCommunity.World;
            PrefabInstance pi = ScSecurity.PoiByKey(o.Poi);
            if (w == null || pi == null || pi.sleeperVolumes == null) return;
            var cleared = new List<SleeperVolume>();
            foreach (SleeperVolume v in pi.sleeperVolumes) if (v != null && !v.isQuestExclude && v.wasCleared) cleared.Add(v);
            if (cleared.Count == 0) return;
            GameRandom rnd = w.GetGameRandom();
            cleared[rnd != null ? rnd.RandomRange(0, cleared.Count) : 0].DespawnAndReset(w);
            o.NeglectDay = ScCommunity.Today;         // the next one waits another OutpostNeglectDays
            o.State = OutpostState.Contested;
            ScOutposts.Rebuild();
            if (player != null) ScCommunity.Tell(player, string.Format(Localization.Get("scRiskNeglect"), o.Name), "ui_denied");
            if (summary.Length > 0) summary.Append(' ');
            summary.Append(string.Format(Localization.Get("scRiskNeglectLine"), o.Name));
            ScLog.Out("neglect: a sleeper area of outpost " + o.Id + " (" + o.Name + ") re-armed");
        }

        // ------------------------------------------------------------------ the ticker ----

        /// <summary>ScTicker, every second: attack fights and deadlines, blood-moon presence and the siege at its end.</summary>
        public static void Tick(World w, EntityPlayerLocal player)
        {
            try
            {
                if (w == null || player == null) return;
                float now = Time.unscaledTime;
                if (now < nextTick) return;
                nextTick = now + 1f;
                if (!markersRestored)
                {
                    markersRestored = true;
                    foreach (Outpost o in ScRegistry.Outposts) if (UnderAttack(o) && o.AttackHeard) AddMarker(o, player);
                }
                ScEncRaids.Tick(w, player);        // E10: enclave demands and raids squared with their groups (off, gone, nobody left)
                bool bm = false;
                try { bm = w.aiDirector != null && w.aiDirector.BloodMoonComponent != null && w.aiDirector.BloodMoonComponent.BloodMoonActive; }
                catch (Exception) { }
                Vector3 pp = player.position;
                var list = new List<Outpost>(ScRegistry.Outposts);
                foreach (Outpost o in list)
                {
                    if (!o.Holding) continue;
                    float d = ScOutposts.DistanceToBox(o, pp);
                    if (bm && d <= ScConfig.OutpostStandInRange) L(o).BloodMoonHere = true;
                    if (!UnderAttack(o)) continue;
                    Live l = L(o);
                    if (!l.Spawned)
                    {
                        if (w.worldTime >= o.AttackUntil)
                        {
                            // E10 (the E11 review's L1): an enclave's raid that never came out while you stood here - those who
                            // could come in view at their den - is one you attended, and held
                            if (l.Waited && d <= ScConfig.RescueTriggerRange) Settle(o, player, true, "they never came out while you were there", true);
                            else Settle(o, player, false, "unanswered");
                            continue;
                        }
                        // one live attack at a time: an outpost next to a home (E5: or an enclave's siege) being fought waits
                        // for that fight
                        if (d <= ScConfig.RescueTriggerRange && ScZone.IsLoaded(o.Flag) && !ScBaseAttack.Live && !ScEncSiege.Live) SpawnAttack(w, player, o);
                        continue;
                    }
                    if (w.worldTime >= o.AttackUntil) { Settle(o, player, true, "fought, not finished"); continue; }
                    // walking off until the attackers unload is not a win: judged only while the player is there
                    if (d > ScConfig.RescueTriggerRange * 2f || !ScZone.IsLoaded(o.Flag)) continue;
                    bool anyAlive = false;
                    foreach (int id in l.Ids)
                    {
                        EntityAlive a = w.GetEntity(id) as EntityAlive;
                        if (a != null && !a.IsDead()) { anyAlive = true; break; }
                    }
                    if (anyAlive) continue;
                    // E10: an enclave's raiders whose bodies went alive (an unload) come back round the outpost before it is won
                    if (o.AttackRaiders && o.RaidEnclave > 0 && ScEncRaids.Regroup(w, player, o, l.Ids) > 0) continue;
                    Won(o, player);
                }
                if (wasBloodMoon && !bm && ScConfig.OutpostAttacks) Siege(player);
                wasBloodMoon = bm;
            }
            catch (Exception e)
            {
                ScLog.Once("risk-tick", e);
            }
        }

        private static int AttackerCount(Outpost o, EntityPlayerLocal player)
        {
            int gs = 0;
            try { gs = player != null ? player.gameStage : 0; } catch (Exception) { }
            float n = (ScConfig.OutpostAttackBase + gs * ScConfig.OutpostAttackPerGamestage) * (1f + 0.1f * o.Tier) * ScOutpostPerks.ModeRisk(o);
            return Mathf.Clamp(Mathf.RoundToInt(n), 2, Math.Max(2, ScConfig.OutpostAttackMax));
        }

        /// <summary>E10: the attack's size (ScEncRaids: the party an unattended raid would have sent).</summary>
        internal static int AttackerCountFor(Outpost o, EntityPlayerLocal player) { return AttackerCount(o, player); }

        /// <summary>How far out round the POI's centre the attackers come in (at least 12 m; half the POI's wider side + 6).</summary>
        internal static float Reach(Outpost o) { return Mathf.Max(12f, Mathf.Max(o.Size.x, o.Size.z) * 0.5f + 6f); }

        /// <summary>A standable spot on the attackers' ring: a random bearing, reach to reach + 7 m out, at the flag's level (SpotNear).</summary>
        internal static Vector3 RingSpot(World w, Outpost o, GameRandom rnd, float reach)
        {
            Vector3 c = o.Centre;
            float ang = rnd.RandomFloat * Mathf.PI * 2f;
            float dist = reach + rnd.RandomRange(0, 8);
            Vector3i at = new Vector3i(Mathf.RoundToInt(c.x + Mathf.Cos(ang) * dist), o.Flag.y, Mathf.RoundToInt(c.z + Mathf.Sin(ang) * dist));
            return ScRadio.SpotNear(w, at, 3);
        }

        /// <summary>The first live stand-in of the outpost's staff (whoever holds it, the attackers' target), or null.</summary>
        internal static EntityAlive StandIn(World w, Outpost o)
        {
            if (w == null || o == null) return null;
            foreach (Member m in ScGarrison.Staff(o))
            {
                EntityAlive s = m.StandInId >= 0 ? w.GetEntity(m.StandInId) as EntityAlive : null;
                if (s != null && !s.IsDead()) return s;
            }
            return null;
        }

        /// <summary>
        /// The raider band for the player's gamestage, 0..2 (T1 below 50, T2 below 100, T3 from 100): the thresholds the
        /// wild raider spawn groups scRaiderGroupGS01/GS50/GS100 stand for.
        /// </summary>
        private static int RaiderBand(EntityPlayerLocal player)
        {
            int gs = 0;
            try { gs = player != null ? player.gameStage : 0; } catch (Exception) { }
            return gs >= 100 ? 2 : gs >= 50 ? 1 : 0;
        }

        /// <summary>
        /// The player is here: the attackers come in around the POI, at whoever holds it.
        ///
        /// Raiders are picked as classes, never rolled from a spawn group (M10, design 3.6): the wild raider groups are 96%
        /// "none" (class id 0, which this loop skips), so rolling them once per attacker spawned about one raider in 25 and
        /// most raids settled as "no attackers". Each raider is the band's class of a random sex; their weapons, looks and
        /// ammo are rolled by the body itself (ScLoadout, ScLooks) like any wild raider's. Zombie attacks keep their group,
        /// which has no "none" entry.
        /// </summary>
        private static void SpawnAttack(World w, EntityPlayerLocal player, Outpost o)
        {
            Live l = L(o);
            l.Spawned = true;
            l.Ids.Clear();
            try
            {
                GameRandom rnd = w.GetGameRandom();
                int band = RaiderBand(player);
                string group = o.AttackRaiders ? "raider classes T" + (band + 1) : ScConfig.DistressZombieGroup;
                int count = AttackerCount(o, player);
                float reach = Reach(o);
                EntityAlive target = StandIn(w, o);
                Enclave by = o.AttackRaiders ? ScEncRaids.EnclaveOf(o) : null;
                if (by != null && ScEncRaids.Configured)
                {
                    // E10: an enclave's raid - their own people come (ScEncRaids.SpawnParty); with nobody left to send, it is off.
                    // The enclaves still being validated (the first seconds of a session), or those who could come in view at
                    // their den or still being built there (SpawnParty's -1): the next second (Waited: the deadline holds it)
                    if (!ScEnclaves.Ready) { l.Spawned = false; return; }
                    int came = ScEncRaids.SpawnParty(w, player, o, count, target, l.Ids);
                    if (came < 0) { l.Spawned = false; l.Ids.Clear(); l.Waited = true; return; }
                    if (came == 0)
                    {
                        l.Spawned = false;
                        l.Ids.Clear();
                        ScEncRaids.NobodyCame(o, by, player);
                        ScRegistry.SaveAtomic();
                        return;
                    }
                    ScCommunity.Tell(player, string.Format(Localization.Get("scEncRaidHere"), o.Name, came, by.Name), "ui_denied");
                    ScLog.Out("attack at outpost " + o.Id + " (" + o.Name + "): " + came + " of enclave " + by.Id + " '" + by.Name + "'");
                    return;
                }
                for (int i = 0; i < count; i++)
                {
                    int cls;
                    if (o.AttackRaiders)
                    {
                        string name = ScXnpc.RaiderClassFor(rnd.RandomFloat < 0.5f, band);
                        cls = name != null ? EntityClass.FromString(name) : 0;
                    }
                    else cls = EntityGroups.GetRandomFromGroup(group, ref lastClass, rnd);
                    if (!ScCommunity.ClassExists(cls)) continue;              // ids are string hashes: negative is fine
                    Vector3 pos = RingSpot(w, o, rnd, reach);
                    EntityAlive a = EntityFactory.CreateEntity(cls, pos) as EntityAlive;
                    if (a == null) continue;
                    w.SpawnEntityInWorld(a);
                    a.SetSpawnerSource(EnumSpawnerSource.Dynamic);             // never written with the chunk
                    a.SetAttackTarget(target != null ? target : player, 1200);
                    l.Ids.Add(a.entityId);
                }
                if (l.Ids.Count == 0)
                {
                    // nothing could be spawned (an empty or missing group): settled like an unanswered call
                    ScLog.Warn("attack at outpost " + o.Id + ": no attacker could be spawned from " + group);
                    Settle(o, player, true, "no attackers");
                    return;
                }
                ScCommunity.Tell(player, string.Format(Localization.Get(o.AttackRaiders ? "scRiskRaidHere" : "scRiskAttackHere"), o.Name, l.Ids.Count), "ui_denied");
                ScLog.Out("attack at outpost " + o.Id + " (" + o.Name + "): " + l.Ids.Count + " " + (o.AttackRaiders ? "raider(s)" : "zombie(s)") + " from " + group);
            }
            catch (Exception e)
            {
                ScLog.Once("risk-spawn", e);
            }
        }

        private static void Won(Outpost o, EntityPlayerLocal player)
        {
            o.AttackUntil = 0;
            L(o).Ids.Clear();
            L(o).Spawned = false;
            RemoveMarker(o);
            o.SecBonus = Math.Min(ScConfig.OutpostSecBonusMax, o.SecBonus + ScConfig.OutpostSecWinBonus);
            ScRegistry.Community.OutpostTriumph = Math.Min(10f, ScRegistry.Community.OutpostTriumph + ScConfig.OutpostWinMorale);
            ScLeadership.Count(ScLeadership.RescueEvent);
            if (player != null) ScCommunity.Tell(player, string.Format(Localization.Get("scRiskHeld"), o.Name), "");
            ScOutposts.NoteLedger(string.Format(Localization.Get("scRiskLedgerHeld"), o.Name));
            ScLog.Out("outpost " + o.Id + " (" + o.Name + ") held against the attack");
            if (o.AttackRaiders) ScEncRaids.End(o, player, true, "won");       // E10: an enclave's raid - their losses, the rest home
            ScRegistry.SaveAtomic();
        }

        /// <summary>
        /// The deadline passed: settled on security (a fight the player joined never ends worse than damaged). Waited (E10, the
        /// E11 review's L1): an enclave's raid that never came out while the player stood there is held, no roll (Guarded).
        /// </summary>
        private static void Settle(Outpost o, EntityPlayerLocal player, bool attended, string why, bool waited = false)
        {
            o.AttackUntil = 0;
            L(o).Spawned = false;
            L(o).Waited = false;
            L(o).Ids.Clear();
            RemoveMarker(o);
            string result = waited ? Guarded(o, player, why) : Outcome(o, player, attended ? ScConfig.OutpostAttendedBonus : 0, attended, why);
            if (o.AttackRaiders && o.RaidEnclave > 0)
            {
                // E10: an enclave's raid - held unattended, it costs them people; their party home, their rest
                ScEncRaids.End(o, player, attended, result, waited);
                ScRegistry.SaveAtomic();
            }
        }

        /// <summary>
        /// E10, the E11 review's L1: held with the player there and nobody come to fight (an enclave's raid whose people stayed
        /// in view at their den, Live.Waited) - Outcome's held result without its roll, told as a hold the player was at.
        /// </summary>
        private static string Guarded(Outpost o, EntityPlayerLocal player, string why)
        {
            o.SecBonus = Math.Min(ScConfig.OutpostSecBonusMax, o.SecBonus + ScConfig.OutpostSecWinBonus * 0.5f);
            if (player != null) ScCommunity.Tell(player, string.Format(Localization.Get("scRiskHeld"), o.Name), "");
            ScOutposts.NoteLedger(string.Format(Localization.Get("scRiskLedgerHeld"), o.Name));
            ScLog.Out("outpost " + o.Id + " (" + o.Name + ") held (" + why + ")");
            ScRegistry.SaveAtomic();
            return HeldResult;
        }

        /// <summary>The end of a blood moon: every outpost the player did not stand at rolls, a little harder.</summary>
        private static void Siege(EntityPlayerLocal player)
        {
            var list = new List<Outpost>(ScRegistry.Outposts);
            foreach (Outpost o in list)
            {
                Live l = L(o);
                bool here = l.BloodMoonHere;
                l.BloodMoonHere = false;                     // every blood moon starts from nobody there
                if (o.State != OutpostState.Active) continue;
                if (here)
                {
                    if (player != null) ScCommunity.Tell(player, string.Format(Localization.Get("scRiskSiegeHeldByYou"), o.Name), "");
                    continue;
                }
                Outcome(o, player, -ScConfig.OutpostSiegePenalty, false, "blood moon");
            }
        }

        /// <summary>Settled on security: held, damaged or overrun (the result, HeldResult / DamagedResult / OverrunResult; E10 reads it).</summary>
        private static string Outcome(Outpost o, EntityPlayerLocal player, int bonus, bool attended, string why)
        {
            World w = ScCommunity.World;
            GameRandom rnd = w != null ? w.GetGameRandom() : null;
            int sec = Mathf.Clamp(Security(o) + bonus, 0, 100);
            int roll = rnd != null ? rnd.RandomRange(0, 100) : UnityEngine.Random.Range(0, 100);
            string result;
            if (roll < sec)
            {
                result = HeldResult;
                o.SecBonus = Math.Min(ScConfig.OutpostSecBonusMax, o.SecBonus + ScConfig.OutpostSecWinBonus * 0.5f);
                if (player != null) ScCommunity.Tell(player, string.Format(Localization.Get("scRiskHeldAlone"), o.Name), "");
                ScOutposts.NoteLedger(string.Format(Localization.Get("scRiskLedgerHeld"), o.Name));
                ScLog.Out("outpost " + o.Id + " (" + o.Name + ") held on its own (" + why + ", roll " + roll + " < security " + sec + ")");
            }
            else if (attended || roll < sec + ScConfig.OutpostDamageBand || !HasSleepers(o))
            {
                result = DamagedResult;
                o.DamagedUntil = ScCommunity.Today + Math.Max(1, ScConfig.OutpostDamagedDays);   // that many rollovers yield nothing
                if (player != null) ScCommunity.Tell(player, string.Format(Localization.Get("scRiskDamaged"), o.Name, ScConfig.OutpostDamagedDays, ScConfig.OutpostRepairMats), "ui_denied");
                ScOutposts.NoteLedger(string.Format(Localization.Get("scRiskLedgerDamaged"), o.Name));
                ScLog.Out("outpost " + o.Id + " (" + o.Name + ") damaged (" + why + ", roll " + roll + ", security " + sec + ")");
            }
            else
            {
                result = OverrunResult;
                Overrun(o, player);
                ScLog.Out("outpost " + o.Id + " (" + o.Name + ") OVERRUN (" + why + ", roll " + roll + ", security " + sec + ")");
            }
            ScRegistry.SaveAtomic();
            return result;
        }

        private static bool HasSleepers(Outpost o)
        {
            PrefabInstance pi = ScSecurity.PoiByKey(o.Poi);
            if (pi == null || pi.sleeperVolumes == null) return false;
            foreach (SleeperVolume v in pi.sleeperVolumes) if (v != null && !v.isQuestExclude) return true;
            return false;
        }

        /// <summary>Overrun: the POI's sleepers are back (clear it again to retake it); the garrison falls back hurt.</summary>
        private static void Overrun(Outpost o, EntityPlayerLocal player)
        {
            World w = ScCommunity.World;
            PrefabInstance pi = ScSecurity.PoiByKey(o.Poi);
            if (w != null && pi != null && pi.sleeperVolumes != null)
                foreach (SleeperVolume v in pi.sleeperVolumes) if (v != null && !v.isQuestExclude) v.DespawnAndReset(w);
            foreach (Member m in ScGarrison.Staff(o))
            {
                m.Injured = true;
                ScGarrison.Recall(m, player, null);
            }
            o.State = OutpostState.Contested;
            for (int i = 0; i < ScNames.ResourceCount; i++) { o.Pending[i] = 0; o.Stash[i] = 0; }
            ScRegistry.Community.OutpostLoss += ScConfig.OutpostLostPenalty * 0.5f;
            ScOutposts.Rebuild();
            if (player != null) ScCommunity.Tell(player, string.Format(Localization.Get("scRiskOverrun"), o.Name), "ui_denied");
            ScOutposts.NoteLedger(string.Format(Localization.Get("scRiskLedgerOverrun"), o.Name));
        }

        // ------------------------------------------------------------------ player answers ----

        public static bool PayDemand(Outpost o, EntityPlayerLocal player, out string message)
        {
            message = null;
            if (o == null || !HasDemand(o)) { message = Localization.Get("scRiskNoDemand"); return false; }
            Community c = ScRegistry.Community;
            if (c.Get(Resource.Ammo) < o.DemandAmmo || c.Get(Resource.Food) < o.DemandFood)
            {
                message = string.Format(Localization.Get("scRiskCannotPay"), o.DemandAmmo, o.DemandFood, c.Get(Resource.Ammo), c.Get(Resource.Food));
                return false;
            }
            c.Add(Resource.Ammo, -o.DemandAmmo);
            c.Add(Resource.Food, -o.DemandFood);
            o.DemandUntil = -1;
            string theirs = ScEncRaids.OnPaid(o);          // E10: an enclave's demand - its ledger line, their rest, their words
            if (theirs == null) ScOutposts.NoteLedger(string.Format(Localization.Get("scRiskLedgerPaid"), o.Name));
            ScRegistry.SaveAtomic();
            message = theirs ?? string.Format(Localization.Get("scRiskPaid"), o.Name);
            ScLog.Out("paid the raiders for outpost " + o.Id + " (" + o.Name + ")");
            return true;
        }

        public static bool RefuseDemand(Outpost o, EntityPlayerLocal player, out string message)
        {
            message = null;
            if (o == null || !HasDemand(o)) { message = Localization.Get("scRiskNoDemand"); return false; }
            o.DemandUntil = -1;
            StartAttack(o, true, player, true);
            message = ScEncRaids.RefusedText(o) ?? string.Format(Localization.Get("scRiskRefused"), o.Name, ScConfig.OutpostAttackHours);
            return true;
        }

        /// <summary>"Repair now": the damage is paid off in materials and the outpost works again tomorrow.</summary>
        public static bool Repair(Outpost o, EntityPlayerLocal player, out string message)
        {
            message = null;
            if (o == null || !DamagedNow(o)) { message = Localization.Get("scRiskNotDamaged"); return false; }
            Community c = ScRegistry.Community;
            int mats = ScOutpostPerks.Salvaged(ScConfig.OutpostRepairMats);
            if (c.Get(Resource.Materials) < mats) { message = string.Format(Localization.Get("scRiskRepairShort"), mats, c.Get(Resource.Materials)); return false; }
            c.Add(Resource.Materials, -mats);
            o.DamagedUntil = -1;
            ScRegistry.SaveAtomic();
            message = string.Format(Localization.Get("scRiskRepaired"), o.Name, mats);
            return true;
        }

        /// <summary>The first outpost with an open demand (the radio window answers one at a time).</summary>
        public static Outpost FirstDemand()
        {
            foreach (Outpost o in ScRegistry.Outposts) if (o.Holding && HasDemand(o)) return o;
            return null;
        }

        /// <summary>The outpost was disbanded or lost: its attack marker and fight state go.</summary>
        public static void Forget(Outpost o)
        {
            if (o == null) return;
            ScEncRaids.OnOutpostGone(o);        // E10: a raid party out there goes home, their demand or raid is off
            RemoveMarker(o);
            live.Remove(o.Id);
            o.AttackUntil = 0;
            o.DemandUntil = -1;
        }

        // ------------------------------------------------------------------ markers ----

        private static void AddMarker(Outpost o, EntityPlayer owner)
        {
            try
            {
                NavObjectManager nav = NavObjectManager.Instance;
                if (nav == null || owner == null || NavObjectClass.GetNavObjectClass(AttackNav) == null) return;
                RemoveMarker(o);
                Vector3 at = o.Centre + new Vector3(0f, 2f, 0f);
                NavObject obj = nav.RegisterNavObject(AttackNav, at, "", false, -1, owner);
                if (obj != null) obj.name = string.Format(Localization.Get("scRiskMarker"), o.Name);
                markers[o.Id] = at;
            }
            catch (Exception e)
            {
                ScLog.Once("risk-marker", e);
            }
        }

        private static void RemoveMarker(Outpost o)
        {
            try
            {
                Vector3 at;
                if (!markers.TryGetValue(o.Id, out at)) return;
                markers.Remove(o.Id);
                NavObjectManager nav = NavObjectManager.Instance;
                if (nav != null) nav.UnRegisterNavObjectByPosition(at, AttackNav);
            }
            catch (Exception e)
            {
                ScLog.Once("risk-unmarker", e);
            }
        }

        /// <summary>Console / window: the security breakdown.</summary>
        public static string SecurityText(Outpost o)
        {
            if (o == null) return "";
            List<Member> staff = ScGarrison.Staff(o);
            int stars = 0;
            foreach (Member m in staff) stars += Math.Max(0, m.Stars(Skill.Fighting) + m.Stars(Skill.Shooting) - 2);
            var sb = new StringBuilder();
            sb.Append(Security(o)).Append(" = base ").Append(ScConfig.OutpostSecBase)
              .Append(", staff +").Append(ScConfig.OutpostSecPerStaffer * staff.Count + ScConfig.OutpostSecPerCombatStar * stars)
              .Append(", tier -").Append(ScConfig.OutpostSecPerTier * o.Tier)
              .Append(", visited ").Append(o.LastVisitDay >= 0 && ScCommunity.Today - o.LastVisitDay <= 3 ? "+" + ScConfig.OutpostSecVisited : "0")
              .Append(", ammo ").Append(ScRegistry.Community.Get(Resource.Ammo) <= 0 ? "-" + ScConfig.OutpostSecNoAmmo : "0")
              .Append(", defenses +").Append(Math.Min(ScConfig.OutpostSecDefenseCap, ScConfig.OutpostSecPerDefense * o.Invest[3]))
              .Append(", won fights +").Append(Mathf.RoundToInt(o.SecBonus).ToString(CultureInfo.InvariantCulture))
              .Append("; attack chance ").Append((AttackChance(o) * 100f).ToString("0.#", CultureInfo.InvariantCulture)).Append("% a day");
            return sb.ToString();
        }
    }
}
