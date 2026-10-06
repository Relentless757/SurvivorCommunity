using System;
using System.Text;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Community morale (0-100) and each survivor's mood, recomputed from the registry (State of Decay
    /// style: food days, medicine, beds, facilities, the other resources, traits, recent deaths, outposts). Morale
    /// drives: XP x1.5 above HighMoraleThreshold (ScSkills.AddXp), refusal to follow below mood 25
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
            /// <summary>Base defence: home attacks held lately (Community.DefenceTriumph, fades a point a day).</summary>
            public int Defence;
            public float FoodDays;
            public int Total;
        }

        public static float DailyFoodNeed()
        {
            float need = 0f;
            foreach (var m in ScRegistry.Members.Values) need += ScConfig.FoodPerMemberDay * ScTraits.FoodMult(m);
            return need;
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

            int deficit = members - c.Beds;
            p.Beds = members == 0 ? 0 : (deficit <= 0 ? 10 : Math.Max(-15, -5 * deficit));

            p.Facilities = Math.Min(15, 3 * c.Facilities);
            p.Supplies = (c.Get(Resource.Ammo) > 0 ? 2 : 0) + (c.Get(Resource.Materials) > 0 ? 2 : 0) + (c.Get(Resource.Fuel) > 0 ? 2 : 0);
            foreach (var m in ScRegistry.Members.Values) p.Traits += ScTraits.MoraleOffset(m);
            p.Deaths = -Mathf.RoundToInt(c.DeathPenalty);
            int producing = 0;
            foreach (Outpost o in ScRegistry.Outposts) if (o.State == OutpostState.Active) producing++;
            p.Territory = Math.Min(ScConfig.OutpostMoraleCap, ScConfig.OutpostMoralePer * producing) - Mathf.RoundToInt(c.OutpostLoss)
                          + Mathf.RoundToInt(c.OutpostTriumph);
            p.Defence = Mathf.RoundToInt(c.DefenceTriumph);

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
            }

            int total = 50 + p.Food + p.Meds + p.Beds + p.Facilities + p.Supplies + p.Traits + p.Deaths + p.Leadership + p.Territory + p.Defence;
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
                    int mood = c.Morale + ScTraits.MoodOffset(m) - (m.Injured ? 15 : 0) - (c.Hungry ? 10 : 0) + ScGarrison.MoodOffset(m);
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
            sb.Append('\n').Append(Term("scMoraleMeds", p.Meds));
            sb.Append('\n').Append(Term("scMoraleBeds", p.Beds)).Append(' ').Append(string.Format(Localization.Get("scMoraleBedsDetail"), ScRegistry.Community.Beds, ScRegistry.Members.Count));
            sb.Append('\n').Append(Term("scMoraleFacilities", p.Facilities)).Append(" (").Append(ScRegistry.Community.Facilities).Append(')');
            sb.Append('\n').Append(Term("scMoraleSupplies", p.Supplies));
            if (p.Traits != 0) sb.Append('\n').Append(Term("scMoraleTraits", p.Traits));
            if (p.Deaths != 0) sb.Append('\n').Append(Term("scMoraleDeaths", p.Deaths));
            if (p.Leadership != 0) sb.Append('\n').Append(Term("scMoraleLeadership", p.Leadership));
            if (p.Territory != 0) sb.Append('\n').Append(Term("scMoraleTerritory", p.Territory));
            if (p.Defence != 0) sb.Append('\n').Append(Term("scMoraleDefence", p.Defence));
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
                bool want = ScRegistry.Members.Count > 0 && BuffManager.GetBuff(HudBuff) != null;
                if (want)
                {
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
    }
}
