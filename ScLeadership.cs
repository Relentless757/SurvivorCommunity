using System;
using System.Collections.Generic;
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
        /// <summary>A contested outpost cleared again (SurvivorOrigins pays Leadership XP for it since 2026-11-20, RetakeXp).</summary>
        public const string OutpostRetakenEvent = "sc_ev_outpost_retaken";
        /// <summary>A home attack held (base defence D2; same: for a future SurvivorOrigins release).</summary>
        public const string DefendEvent = "sc_ev_defend";
        /// <summary>Fact: outposts producing right now.</summary>
        public const string OutpostsCvar = "sc_outposts";
        public const string XpMultCvar = "$soLeadXpMult";
        public const string MoraleCvar = "$soLeadMorale";
        public const string ShieldCvar = "$soLeadShield";
        /// <summary>Round MP5 (part W): Publish's followers per player (entity id), reused.</summary>
        private static readonly Dictionary<int, int> followCount = new Dictionary<int, int>();

        /// <summary>
        /// Ticker: the facts SurvivorOrigins pays kill XP and daily morale XP from. Written only on change. Round MP0: on every
        /// community player online (a server-side SetCustomVar on a remote player's entity is sent to the clients: EntityBuffs),
        /// each with their own sc_inzone; player is the anchor (ScPlayers.Anchor), and only says someone is online.
        /// </summary>
        public static void Publish(EntityPlayer player)
        {
            try
            {
                if (player == null || player.Buffs == null) return;
                // round MP5 (part W): each player's own followers - a follower counts for the player their record names as leader
                // (Member.LeaderId) when that player is online, else for the anchor (a save from before MP0, a leader offline): in
                // single player and for a host alone every follower is the one player's, as before
                followCount.Clear();
                int unowned = 0;
                foreach (var m in ScRegistry.Members.Values)
                {
                    if (m.Status != MemberStatus.Following && m.Status != MemberStatus.PendingStation) continue;
                    EntityPlayer lead = string.IsNullOrEmpty(m.LeaderId) ? null : ScPlayers.ById(m.LeaderId);
                    if (lead == null) { unowned++; continue; }
                    int n;
                    followCount.TryGetValue(lead.entityId, out n);
                    followCount[lead.entityId] = n + 1;
                }
                int producing = 0;
                foreach (Outpost o in ScRegistry.Outposts) if (o.State == OutpostState.Active) producing++;
                List<EntityPlayer> all = ScPlayers.All;
                for (int i = 0; i < all.Count; i++)
                {
                    EntityPlayer p = all[i];
                    if (p == null || p.Buffs == null || !ScPlayers.InCommunity(p)) continue;
                    bool inZone = ScZone.HasHome && ScZone.Contains(p.GetBlockPosition());
                    int followers;
                    followCount.TryGetValue(p.entityId, out followers);
                    if (ReferenceEquals(p, player)) followers += unowned;
                    Set(p, FollowersCvar, followers);       // round MP5 (part W): per player (the followers of each one)
                    Set(p, InZoneCvar, inZone ? 1f : 0f);
                    Set(p, MoraleHighCvar, ScConfig.HighMoraleThreshold);
                    Set(p, OutpostsCvar, producing);
                }
            }
            catch (Exception e)
            {
                ScLog.Once("leadership-publish", e);
            }
        }

        private static void Set(EntityPlayer player, string cvar, float v)     // round MP0: any player
        {
            if (player.Buffs.GetCustomVar(cvar) != v) player.Buffs.SetCustomVar(cvar, v);
        }

        /// <summary>+1 on an event counter; SurvivorOrigins pays the XP and resets it.</summary>
        public static void Count(string cvar)
        {
            Count(cvar, null);
        }

        /// <summary>
        /// Round MP5 (part W): +1 on an event counter of this player (a server-side change to a remote player's cvar is sent to
        /// them: EntityBuffs); null: the host's own player, as before - and on a dedicated server, which has none, the anchor.
        /// </summary>
        public static void Count(string cvar, EntityPlayer to)
        {
            try
            {
                EntityPlayer player = to ?? ScCommunity.LocalPlayer ?? (ScRegistry.Loaded ? ScPlayers.Anchor() : null);
                if (player == null || player.Buffs == null) return;
                // round MP5 (review W-M1): an add, not a set - for a remote player the server's copy never sees their machine's
                // reset (the skill drains it there), so "copy + 1" would pay every past event again; NetPackageModifyCVar carries
                // the operation. The host's own player: the same +1 as before
                player.Buffs.SetCustomVar(cvar, 1f, true, CVarOperation.add);
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
                // round MP5 (part W): the host's own player, as before; a dedicated server (none) reads the anchor's (MP6: the
                // community's leader)
                EntityPlayer player = ScCommunity.LocalPlayer ?? (ScRegistry.Loaded ? ScPlayers.Anchor() : null);
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
