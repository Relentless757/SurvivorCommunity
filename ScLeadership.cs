using System;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// The contract with SurvivorOrigins' Leadership skill (sc_leadership_spec.md, 2026-09-27). The two mods
    /// talk only through CVars on the local player, and neither references the other: without SurvivorOrigins
    /// the bonus CVars read 0 and change nothing, and the facts / event counters are simply never drained.
    ///
    ///   facts we publish (ticker, ~1 s)  sc_followers, sc_inzone, sc_morale_high   (sc_morale + buffSCMorale: ScMorale)
    ///   events we count (+1; it drains)  sc_ev_favour  (a recruit favour done),  sc_ev_rescue  (a runner rescued)
    ///   bonuses we apply (it publishes)  $soLeadXpMult (member XP x),  $soLeadMorale (+N morale part),
    ///                                    $soLeadShield (each negative morale part shrinks by N, never past 0)
    ///
    /// The leader is the local player: SP / host, the player the registry belongs to.
    /// </summary>
    public static class ScLeadership
    {
        public const string FollowersCvar = "sc_followers";
        public const string InZoneCvar = "sc_inzone";
        public const string MoraleHighCvar = "sc_morale_high";
        public const string FavourEvent = "sc_ev_favour";
        public const string RescueEvent = "sc_ev_rescue";
        /// <summary>An outpost established (not in the 2026-09-27 contract yet: SurvivorOrigins ignores it until it reads it).</summary>
        public const string OutpostEvent = "sc_ev_outpost";
        /// <summary>A contested outpost cleared again (same: for a future SurvivorOrigins release).</summary>
        public const string OutpostRetakenEvent = "sc_ev_outpost_retaken";
        /// <summary>A home attack held (base defence D2; same: for a future SurvivorOrigins release).</summary>
        public const string DefendEvent = "sc_ev_defend";
        /// <summary>Fact: outposts producing right now.</summary>
        public const string OutpostsCvar = "sc_outposts";
        public const string XpMultCvar = "$soLeadXpMult";
        public const string MoraleCvar = "$soLeadMorale";
        public const string ShieldCvar = "$soLeadShield";

        /// <summary>Ticker: the facts SurvivorOrigins pays kill XP and daily morale XP from. Written only on change.</summary>
        public static void Publish(EntityPlayerLocal player)
        {
            try
            {
                if (player == null || player.Buffs == null) return;
                int followers = 0;
                foreach (var m in ScRegistry.Members.Values)
                    if (m.Status == MemberStatus.Following || m.Status == MemberStatus.PendingStation) followers++;
                bool inZone = ScZone.HasHome && ScZone.Contains(player.GetBlockPosition());
                Set(player, FollowersCvar, followers);
                Set(player, InZoneCvar, inZone ? 1f : 0f);
                Set(player, MoraleHighCvar, ScConfig.HighMoraleThreshold);
                int producing = 0;
                foreach (Outpost o in ScRegistry.Outposts) if (o.State == OutpostState.Active) producing++;
                Set(player, OutpostsCvar, producing);
            }
            catch (Exception e)
            {
                ScLog.Once("leadership-publish", e);
            }
        }

        private static void Set(EntityPlayerLocal player, string cvar, float v)
        {
            if (player.Buffs.GetCustomVar(cvar) != v) player.Buffs.SetCustomVar(cvar, v);
        }

        /// <summary>+1 on an event counter; SurvivorOrigins pays the XP and resets it.</summary>
        public static void Count(string cvar)
        {
            try
            {
                EntityPlayerLocal player = ScCommunity.LocalPlayer;
                if (player == null || player.Buffs == null) return;
                player.Buffs.SetCustomVar(cvar, player.Buffs.GetCustomVar(cvar) + 1f);
                ScLog.Verbose("leadership event " + cvar);
            }
            catch (Exception e)
            {
                ScLog.Once("leadership-count", e);
            }
        }

        private static float Read(string cvar)
        {
            try
            {
                EntityPlayerLocal player = ScCommunity.LocalPlayer;
                if (player == null || player.Buffs == null) return 0f;
                float v = player.Buffs.GetCustomVar(cvar);
                return float.IsNaN(v) || float.IsInfinity(v) ? 0f : v;
            }
            catch (Exception)
            {
                return 0f;
            }
        }

        /// <summary>Multiplier on every member skill / specialty XP gain: 1 + level/100. Absent / 0 = 1.</summary>
        public static float XpMult()
        {
            float v = Read(XpMultCvar);
            return v <= 0f ? 1f : Mathf.Min(v, 10f);
        }

        /// <summary>The "Leadership +N" morale part (0 without SurvivorOrigins).</summary>
        public static int MoraleBonus()
        {
            return Mathf.Clamp(Mathf.RoundToInt(Read(MoraleCvar)), 0, 100);
        }

        /// <summary>How far each negative morale part is shrunk toward 0.</summary>
        public static int Shield()
        {
            return Mathf.Clamp(Mathf.RoundToInt(Read(ShieldCvar)), 0, 100);
        }

        /// <summary>A negative morale part after the shield: -6 with shield 2 is -4, -1 is 0. Positive parts are untouched.</summary>
        public static int Shielded(int part, int shield)
        {
            return part >= 0 ? part : Math.Min(0, part + shield);
        }
    }
}
