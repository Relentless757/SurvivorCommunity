using System;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Base defence D4, the blood-moon siege (decision Q7; kept apart and simple because the blood moon will be reworked):
    /// a blood moon that passes while the player was never at the base (ScBaseAttack.Near, the distance an attack is fought
    /// live from, at any moment of it) besieges
    /// the base. At its end it is settled like a fight without the player (ScDefence.Settle) against a horde of the usual
    /// size, BaseSiegePenalty below the rating. Told then: by a powered radio at once, else when the player is back. The day
    /// the player was home for it is saved (bmhome=), so a quit and reload during the blood moon keeps it; a mark older
    /// than yesterday is dropped, so an end of the blood moon that was never seen (saved in its last second) cannot carry it
    /// into the next one.
    /// </summary>
    public static class ScSiege
    {
        /// <summary>Round MP6 (part H): one home's blood-moon watch - one per community (ScHomeState), so each community's siege is its own.</summary>
        private sealed class SiegeState
        {
            public bool WasBloodMoon;
            public float SeenAt;
            public float NextTick;
        }
        private static readonly ScHomeState<SiegeState> homes = new ScHomeState<SiegeState>();
        // round MP6 (part H): the old statics, now the row of the community in scope. The flag is written every tick of a blood
        // moon; with two or more communities one last written more than 5 real seconds ago reads false - the community paused, so
        // the blood moon's end was not seen there and brings no siege (as an end never seen before a reload)
        private static bool wasBloodMoon
        {
            get { SiegeState s = homes.Current; return s.WasBloodMoon && (ScCommunities.Count < 2 || Time.unscaledTime - s.SeenAt <= 5f); }
            set { SiegeState s = homes.Current; s.WasBloodMoon = value; s.SeenAt = Time.unscaledTime; }
        }
        private static float nextTick { get { return homes.Current.NextTick; } set { homes.Current.NextTick = value; } }

        public static void Reset()
        {
            homes.Reset();                        // round MP6 (part H): every community's
        }

        /// <summary>
        /// Round MP6 (fix H-N2): the community in scope ticks again after a pause (ScTicker, several communities only) - a "home for
        /// the blood moon" mark from an older blood moon goes (it was neither cleared nor retaken while paused, and would count the
        /// next blood moon as one the player was home for). One from the blood moon still on stays.
        /// </summary>
        internal static void OnResumed(int day)
        {
            Community c = ScRegistry.Community;
            if (c.BloodMoonHomeDay <= 0 || (day <= c.BloodMoonHomeDay + 1 && day >= c.BloodMoonHomeDay)) return;
            ScLog.Out("siege: " + ScRegistry.Current.Label + " resumes - the home mark of the blood moon of day " + c.BloodMoonHomeDay + " goes");
            c.BloodMoonHomeDay = 0;
            ScRegistry.SaveAtomic();
        }

        public static bool BloodMoonActive(World w)
        {
            try { return w != null && w.aiDirector != null && w.aiDirector.BloodMoonComponent != null && w.aiDirector.BloodMoonComponent.BloodMoonActive; }
            catch (Exception) { return false; }
        }

        /// <summary>ScTicker, every second, after ScBaseAttack (its zone is fresh): who was home, and the siege at the end.</summary>
        public static void Tick(World w, EntityPlayer player)
        {
            try
            {
                if (w == null || player == null || !ScRegistry.Loaded) return;
                float now = Time.unscaledTime;
                if (now < nextTick) return;
                nextTick = now + 1f;
                if (GameManager.Instance != null && GameManager.Instance.IsPaused()) return;
                Community c = ScRegistry.Community;
                int day = GameUtils.WorldTimeToDays(w.worldTime);
                bool on = ScConfig.BaseAttacks && ScConfig.BaseSiege;
                if (BloodMoonActive(w))
                {
                    wasBloodMoon = true;
                    // round MP0: any community player near the base counts (a client holding it all night too)
                    if (on && c.BloodMoonHomeDay == 0 && c.HasHome && ScBaseAttack.AnyNear())
                    {
                        c.BloodMoonHomeDay = Math.Max(1, day);
                        ScLog.Out("siege: the player is home for the blood moon - no siege at its end");
                        ScRegistry.SaveAtomic();
                    }
                    return;
                }
                if (!wasBloodMoon)
                {
                    if (c.BloodMoonHomeDay > 0 && (day > c.BloodMoonHomeDay + 1 || day < c.BloodMoonHomeDay))   // or the clock went back
                    {
                        c.BloodMoonHomeDay = 0;               // a blood moon whose end was never seen here: its mark goes
                        ScRegistry.SaveAtomic();
                    }
                    return;
                }
                wasBloodMoon = false;
                bool home = c.BloodMoonHomeDay > 0;
                c.BloodMoonHomeDay = 0;                       // every blood moon starts from nobody there
                string why = null;
                if (!on) why = "sieges are off";
                else if (!c.HasHome) why = "no community home";
                else if (home) why = "the player was home for it";
                else if (ScRegistry.CountStatus(MemberStatus.Resident) == 0) why = "nobody lives at home";
                if (why != null)
                {
                    ScLog.Out("siege: the blood moon is over - no siege (" + why + ")");
                    if (home) ScRegistry.SaveAtomic();
                    return;
                }
                Besiege(w, player, -1, "the blood moon passed with the player away");
            }
            catch (Exception e)
            {
                ScLog.Once("siege-tick", e);
            }
        }

        /// <summary>The siege: a horde of the usual size, BaseSiegePenalty below the rating ('forced' 0-2 from the console).</summary>
        public static void Besiege(World w, EntityPlayer player, int forced, string why)
        {
            // round MP0: the anchor's gamestage. Round MP5 (part W): the players involved (ScBaseAttack.InvolvedPlayer - nobody was
            // near for it, so the community player online with the highest gamestage; single player: the one player, as before)
            int horde = ScBaseAttack.Size(player);
            ScDefence.Settle(w, player, horde, Math.Max(0, ScConfig.BaseSiegePenalty), true, forced, why);
            ScRegistry.SaveAtomic();
        }
    }
}
