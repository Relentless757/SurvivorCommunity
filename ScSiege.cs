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
        private static bool wasBloodMoon;
        private static float nextTick;

        public static void Reset()
        {
            wasBloodMoon = false;
            nextTick = 0f;
        }

        public static bool BloodMoonActive(World w)
        {
            try { return w != null && w.aiDirector != null && w.aiDirector.BloodMoonComponent != null && w.aiDirector.BloodMoonComponent.BloodMoonActive; }
            catch (Exception) { return false; }
        }

        /// <summary>ScTicker, every second, after ScBaseAttack (its zone is fresh): who was home, and the siege at the end.</summary>
        public static void Tick(World w, EntityPlayerLocal player)
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
                    if (on && c.BloodMoonHomeDay == 0 && c.HasHome && ScBaseAttack.Near(player))
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
        public static void Besiege(World w, EntityPlayerLocal player, int forced, string why)
        {
            int horde = ScBaseAttack.Size(player);
            ScDefence.Settle(w, player, horde, Math.Max(0, ScConfig.BaseSiegePenalty), true, forced, why);
            ScRegistry.SaveAtomic();
        }
    }
}
