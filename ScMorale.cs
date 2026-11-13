using System;
using System.Text;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Community morale (0-100) and each survivor's mood, recomputed from the registry (State of Decay
    /// style: food days, thirst, medicine, beds, facilities, the other resources, traits, recent deaths, outposts,
    /// the Activities' round of drinks, E6's enclaves: allies, Comfort in Numbers, a group lost; I3's infestations near home). Morale
    /// drives: XP x1.5 above HighMoraleThreshold (ScSkills.AddXp), (P2) no refusal of orders by default (RefuseOrdersBelowMood)
    /// (ScCommunity.Follow), and below LowMoraleThreshold for LowMoraleDays in a row the unhappiest survivor
    /// threatens to leave and does so the next day unless morale recovers. The player sees it as the
    /// buffSCMorale HUD icon (cvar sc_morale) while the community has members.
    /// </summary>
    public static class ScMorale
    {
        public const string HudBuff = "buffSCMorale";
        public const string HudCvar = "sc_morale";

        /// <summary>Console override for testing (-1 = none).</summary>
        public static int Override = -1;

        private static float nextHud;

        public struct Parts
        {
            public int Food, Meds, Beds, Facilities, Supplies, Traits, Deaths;
            /// <summary>SurvivorOrigins' Leadership: a positive part ($soLeadMorale); its shield already shrank the negative ones.</summary>
            public int Leadership;
            /// <summary>Outposts: +OutpostMoralePer per producing outpost (capped), minus the fading penalty for lost ones.</summary>
            public int Territory;
            /// <summary>Base defence: home attacks held lately, less those lost without the player (DefenceTriumph - DefenceLoss, each fading a point a day).</summary>
            public int Defence;
            /// <summary>Thirst (round wl): -20 while the last rollover ran short of water (a water outpost's cistern floors it), else 0.</summary>
            public int Water;
            /// <summary>Activities (round wl): the round of drinks while it lasts.</summary>
            public int Activity;
            /// <summary>
            /// E6 (ScEncAllies): Allies, +EnclaveAllyMorale per Allied enclave up to EnclaveAllyMoraleCap; Comfort in Numbers,
            /// +EnclaveBenefitMorale for each such ally (past that cap); Enclave lost, the fading -EnclaveLostMorale for each
            /// Friendly or Allied group lost (Community.EnclaveLoss).
            /// </summary>
            public int Allies, Comfort, EnclaveLost;
            /// <summary>
            /// I3, infestations near home (ScInfestations.NearHome): minus each of the nearest InfestHomeMoraleNests nests' distance
            /// band cost (InfestHomeMoraleBands), at most InfestHomeMoraleCap in all; 0 with no home.
            /// </summary>
            public int Nests;
            public float FoodDays;
            /// <summary>Days the water lasts at today's use less what the wells and dew collectors add (99 = no need / more made than drunk).</summary>
            public float WaterDays;
            public int Total;
        }

        public static float DailyFoodNeed()
        {
            float need = 0f;
            foreach (var m in ScRegistry.Members.Values) need += ScConfig.FoodPerMemberDay * ScTraits.FoodMult(m);
            return need;
        }

        /// <summary>Water drunk a day beyond what the home's wells and dew collectors make (0 when they cover it).</summary>
        public static float DailyWaterShortfall()
        {
            return Math.Max(0f, ScRegistry.Members.Count * ScConfig.WaterPerMemberDay - Math.Max(0, ScRegistry.Community.HomeWater));
        }

        /// <summary>Computes every term without storing anything.</summary>
        public static Parts Compute()
        {
            Community c = ScRegistry.Community;
            var p = new Parts();
            int members = ScRegistry.Members.Count;
            float need = DailyFoodNeed();
            p.FoodDays = need > 0.01f ? c.Get(Resource.Food) / need : 99f;
            if (c.Hungry) p.Food = ScOutpostPerks.PerkActive(Resource.Food) ? Math.Max(-20, ScConfig.OutpostCanteenFloor) : -20;   // a food outpost's canteen
            else p.Food = Mathf.Clamp(Mathf.RoundToInt((Mathf.Min(p.FoodDays, 30f) - 2f) * 4f), -10, 20);

            bool injuredNoMeds = false;
            foreach (var m in ScRegistry.Members.Values) if (m.Injured && c.Get(Resource.Meds) <= 0) { injuredNoMeds = true; break; }
            p.Meds = injuredNoMeds ? -10 : (c.Get(Resource.Meds) > 0 ? 5 : 0);

            int deficit = members - ScEncAllies.TotalBeds();       // E6: an ally's Overflow Barracks counts
            p.Beds = members == 0 ? 0 : (deficit <= 0 ? 10 : Math.Max(-15, -5 * deficit));

            p.Facilities = Math.Min(15, 3 * c.Facilities);
            p.Supplies = (c.Get(Resource.Ammo) > 0 ? 2 : 0) + (c.Get(Resource.Materials) > 0 ? 2 : 0) + (c.Get(Resource.Fuel) > 0 ? 2 : 0);
            foreach (var m in ScRegistry.Members.Values) p.Traits += ScTraits.MoraleOffset(m);
            p.Deaths = -Mathf.RoundToInt(c.DeathPenalty);
            int producing = 0;
            foreach (Outpost o in ScRegistry.Outposts) if (o.State == OutpostState.Active) producing++;
            p.Territory = Math.Min(ScConfig.OutpostMoraleCap, ScConfig.OutpostMoralePer * producing) - Mathf.RoundToInt(c.OutpostLoss)
                          + Mathf.RoundToInt(c.OutpostTriumph);
            p.Defence = Mathf.RoundToInt(c.DefenceTriumph - c.DefenceLoss);       // D4: fights lost without the player count against it
            // round wl: thirst works like hunger (a water outpost's cistern is the canteen's twin); the round of drinks
            float shortfall = DailyWaterShortfall();
            p.WaterDays = shortfall > 0.01f ? c.Get(Resource.Water) / shortfall : 99f;
            if (c.Thirsty) p.Water = ScOutpostPerks.PerkActive(Resource.Water) ? Math.Max(-20, ScConfig.OutpostCisternFloor) : -20;
            p.Activity = ScActivities.MoraleBonus();
            // E6: the allies, an ally's Comfort in Numbers, and the groups lost lately
            p.Allies = ScEncAllies.AlliesMorale();
            p.Comfort = ScEncAllies.ComfortMorale();
            p.EnclaveLost = -Mathf.RoundToInt(c.EnclaveLoss);
            p.Nests = -ScInfestations.NestsMorale();             // I3: the infestations near home

            // SurvivorOrigins' Leadership (0 / 0 without it): a part of its own, and a shield that shrinks every
            // negative part toward 0 (never past it)
            p.Leadership = ScLeadership.MoraleBonus();
            int shield = ScLeadership.Shield();
            if (shield > 0)
            {
                p.Food = ScLeadership.Shielded(p.Food, shield);
                p.Meds = ScLeadership.Shielded(p.Meds, shield);
                p.Beds = ScLeadership.Shielded(p.Beds, shield);
                p.Traits = ScLeadership.Shielded(p.Traits, shield);
                p.Deaths = ScLeadership.Shielded(p.Deaths, shield);
                p.Territory = ScLeadership.Shielded(p.Territory, shield);
                p.Defence = ScLeadership.Shielded(p.Defence, shield);
                p.Water = ScLeadership.Shielded(p.Water, shield);
                p.EnclaveLost = ScLeadership.Shielded(p.EnclaveLost, shield);
                p.Nests = ScLeadership.Shielded(p.Nests, shield);
            }

            int total = 50 + p.Food + p.Meds + p.Beds + p.Facilities + p.Supplies + p.Traits + p.Deaths + p.Leadership + p.Territory + p.Defence
                        + p.Water + p.Activity + p.Allies + p.Comfort + p.EnclaveLost + p.Nests;
            p.Total = Mathf.Clamp(total, 0, 100);
            if (Override >= 0) p.Total = Mathf.Clamp(Override, 0, 100);
            return p;
        }

        /// <summary>Stores morale and every member's mood. Returns the morale.</summary>
        public static int Recompute()
        {
            try
            {
                Community c = ScRegistry.Community;
                Parts p = Compute();
                c.Morale = p.Total;
                foreach (var m in ScRegistry.Members.Values)
                {
                    int mood = c.Morale + ScTraits.MoodOffset(m) - (m.Injured ? 15 : 0) - (c.Hungry ? 10 : 0) - (c.Thirsty ? 10 : 0) + ScGarrison.MoodOffset(m);
                    m.Mood = Mathf.Clamp(mood, 0, 100);
                }
                // a threat is withdrawn the moment morale is back above the line (not only at the next rollover)
                if (c.ThreatMemberId >= 0 && c.Morale >= ScConfig.LowMoraleThreshold)
                {
                    Member t = ScRegistry.GetMember(c.ThreatMemberId);
                    c.ThreatMemberId = -1;
                    c.LowMoraleDays = 0;
                    if (t != null)
                    {
                        EntityPlayerLocal player = ScCommunity.LocalPlayer;
                        if (player != null) ScCommunity.Tell(player, string.Format(Localization.Get("scThreatCalmed"), t.Name), "");
                        ScLog.Out(t.Name + " withdrew the threat to leave (morale " + c.Morale + ")");
                    }
                    ScRegistry.SaveAtomic();
                }
                return c.Morale;
            }
            catch (Exception e)
            {
                ScLog.Once("morale", e);
                return ScRegistry.Community.Morale;
            }
        }

        public static string Breakdown()
        {
            Parts p = Compute();
            var sb = new StringBuilder();
            sb.Append(string.Format(Localization.Get("scMoraleLine"), p.Total));
            sb.Append('\n').Append(Term("scMoraleFood", p.Food)).Append(p.FoodDays < 99f ? string.Format(Localization.Get("scMoraleFoodDays"), p.FoodDays.ToString("0.0")) : "");
            if (ScRegistry.Members.Count > 0 || ScRegistry.Community.HomeWater > 0)
                sb.Append('\n').Append(Term("scMoraleWater", p.Water)).Append(p.WaterDays < 99f ? string.Format(Localization.Get("scMoraleFoodDays"), p.WaterDays.ToString("0.0")) : "")
                  .Append(' ').Append(string.Format(Localization.Get("scMoraleWaterMade"), ScRegistry.Community.HomeWater));
            if (p.Activity != 0) sb.Append('\n').Append(Term("scMoraleActivity", p.Activity)).Append(" (").Append(ScActivities.TimeLeft()).Append(')');
            sb.Append('\n').Append(Term("scMoraleMeds", p.Meds));
            sb.Append('\n').Append(Term("scMoraleBeds", p.Beds)).Append(' ').Append(string.Format(Localization.Get("scMoraleBedsDetail"), ScEncAllies.TotalBeds(), ScRegistry.Members.Count));
            int allyBeds = ScEncAllies.ExtraBeds();
            if (allyBeds > 0) sb.Append(' ').Append(string.Format(Localization.Get("scMoraleBedsAllies"), allyBeds));
            sb.Append('\n').Append(Term("scMoraleFacilities", p.Facilities)).Append(" (").Append(ScRegistry.Community.Facilities).Append(')');
            sb.Append('\n').Append(Term("scMoraleSupplies", p.Supplies));
            if (p.Traits != 0) sb.Append('\n').Append(Term("scMoraleTraits", p.Traits));
            if (p.Deaths != 0) sb.Append('\n').Append(Term("scMoraleDeaths", p.Deaths));
            if (p.Leadership != 0) sb.Append('\n').Append(Term("scMoraleLeadership", p.Leadership));
            if (p.Territory != 0) sb.Append('\n').Append(Term("scMoraleTerritory", p.Territory));
            if (p.Defence != 0) sb.Append('\n').Append(Term("scMoraleDefence", p.Defence));
            if (p.Allies != 0) sb.Append('\n').Append(Term("scMoraleAllies", p.Allies)).Append(ScEncAllies.AlliesDetail());
            if (p.Comfort != 0) sb.Append('\n').Append(Term("scEncBenefit_comfort", p.Comfort));
            if (p.EnclaveLost != 0) sb.Append('\n').Append(Term("scMoraleEnclaveLost", p.EnclaveLost));
            if (p.Nests != 0) sb.Append('\n').Append(Term("scMoraleNests", p.Nests)).Append(ScInfestations.NestsDetail());    // I3
            return sb.ToString();
        }

        private static string Term(string key, int v)
        {
            return Localization.Get(key) + " " + (v >= 0 ? "+" : "") + v;
        }

        /// <summary>Day rollover: the death penalty fades, then the threat / departure logic runs.</summary>
        public static void Daily(int day, EntityPlayerLocal player, StringBuilder summary)
        {
            Community c = ScRegistry.Community;
            c.DeathPenalty = Math.Max(0f, c.DeathPenalty - ScConfig.DeathDecayPerDay);
            c.EnclaveLoss = Math.Max(0f, c.EnclaveLoss - 1f);       // E6: a group lost fades a point a day
            int morale = Recompute();
            summary.Append(' ').Append(string.Format(Localization.Get("scDailyMorale"), morale));

            if (ScRegistry.Members.Count == 0) { c.LowMoraleDays = 0; c.ThreatMemberId = -1; return; }

            if (morale < ScConfig.LowMoraleThreshold) c.LowMoraleDays++; else c.LowMoraleDays = 0;

            // somebody already threatened to leave
            if (c.ThreatMemberId >= 0)
            {
                Member t = ScRegistry.GetMember(c.ThreatMemberId);
                if (t == null) { c.ThreatMemberId = -1; }
                else if (morale >= ScConfig.LowMoraleThreshold)
                {
                    c.ThreatMemberId = -1;
                    summary.Append(' ').Append(string.Format(Localization.Get("scDailyStays"), t.Name));
                    if (player != null) ScCommunity.Tell(player, string.Format(Localization.Get("scThreatCalmed"), t.Name), "");
                }
                else
                {
                    c.ThreatMemberId = -1;
                    summary.Append(' ').Append(string.Format(Localization.Get("scDailyLeft"), t.Name));
                    ScCommunity.Leave(t, "scLeftMorale");    // tooltip + registry + entity cleanup
                    c.LowMoraleDays = 0;
                }
                return;
            }

            if (c.LowMoraleDays >= ScConfig.LowMoraleDays)
            {
                Member worst = null;
                foreach (var m in ScRegistry.Members.Values)
                {
                    if (ScRunners.IsPhysical(m)) continue;            // out on a run: they cannot pack up and leave from there
                    if (m.Status == MemberStatus.Arriving) continue;  // nor before they have even arrived (E12, ScEncJoin)
                    if (worst == null || m.Mood < worst.Mood) worst = m;
                }
                if (worst != null)
                {
                    c.ThreatMemberId = worst.Id;
                    summary.Append(' ').Append(string.Format(Localization.Get("scDailyThreat"), worst.Name));
                    if (player != null) ScCommunity.Tell(player, string.Format(Localization.Get("scThreatens"), worst.Name), "ui_denied");
                    ScLog.Out(worst.Name + " threatens to leave (morale " + morale + " for " + c.LowMoraleDays + " days)");
                }
            }
            else if (morale > ScConfig.HighMoraleThreshold)
            {
                summary.Append(' ').Append(Localization.Get("scDailyHigh"));
            }
        }

        /// <summary>Ticker: keeps morale fresh and the player's HUD icon in step (every few seconds).</summary>
        public static void SyncHud(EntityPlayerLocal player)
        {
            try
            {
                if (player == null || player.Buffs == null) return;
                float now = Time.unscaledTime;
                if (now < nextHud) return;
                nextHud = now + 5f;
                int morale = Recompute();
                BuffClass hud = BuffManager.GetBuff(HudBuff);
                bool want = ScRegistry.Members.Count > 0 && hud != null;
                if (want)
                {
                    // R32b (U32): the icon takes the morale's band color; the HUD reads BuffClass.IconColor every frame
                    hud.IconColor = MoodColor32(morale);
                    if (player.Buffs.GetCustomVar(HudCvar) != morale) player.Buffs.SetCustomVar(HudCvar, morale);
                    if (!player.Buffs.HasBuff(HudBuff)) player.Buffs.AddBuff(HudBuff);
                }
                else if (player.Buffs.HasBuff(HudBuff))
                {
                    player.Buffs.RemoveBuff(HudBuff);
                }
            }
            catch (Exception e)
            {
                ScLog.Once("morale-hud", e);
            }
        }

        public static string StatusText(Member m)
        {
            if (m == null) return "";
            string held = ScOrders.StateWord(m);   // a voice / order-wheel order the follower holds
            switch (m.Status)
            {
                case MemberStatus.Following: return Localization.Get("scStatusFollowing") + (held.Length > 0 ? " (" + held + ")" : "");
                case MemberStatus.PendingStation: return Localization.Get("scStatusPending") + (held.Length > 0 ? " (" + held + ")" : "");
                case MemberStatus.OnRun: return Localization.Get("scStatusOnRun");
                case MemberStatus.Garrison: return Localization.Get("scStatusPosted");
                case MemberStatus.Arriving: return Localization.Get("scStatusArriving");     // E12: on the way to the base
                default: return Localization.Get("scStatusResident");
            }
        }

        public static string MoodWord(int mood)
        {
            if (mood >= 75) return Localization.Get("scMoodHappy");
            if (mood >= 50) return Localization.Get("scMoodContent");
            if (mood >= 25) return Localization.Get("scMoodUneasy");
            return Localization.Get("scMoodMiserable");
        }

        /// <summary>
        /// R32b (U32): MoodWord's bands as a color, for the morale indicators outside the community window - the Home
        /// Status dot, the snapshot's mood word and the HUD buff's icon: green while content or happy, amber while uneasy,
        /// red while miserable. (The community window's bars follow its own words, XUiC_ScCommunity.BandColor: the same
        /// bands at the default LowMoraleThreshold of 25.)
        /// </summary>
        public static string MoodColor(int mood)
        {
            if (mood >= 50) return "96,200,96,255";
            if (mood >= 25) return "232,170,60,255";
            return "235,72,60,255";
        }

        /// <summary>MoodColor as a Color32 (the buff icon's tint).</summary>
        public static Color32 MoodColor32(int mood)
        {
            if (mood >= 50) return new Color32(96, 200, 96, 255);
            if (mood >= 25) return new Color32(232, 170, 60, 255);
            return new Color32(235, 72, 60, 255);
        }
    }
}
