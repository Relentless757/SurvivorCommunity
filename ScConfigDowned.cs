using System;
using System.Globalization;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round U2 (2026-11-16, research/npc306/part_downed.md; the user's decisions D4, D5 and D6): the config keys of the downed
    /// state on the community's people (ScDowned, Patches_Downed, UaiDowned.cs). Kept apart from ScConfig.cs's long lists, as
    /// ScConfigRetreat. Config/blocks.xml, block ConfigSurvivorCommunity, right after RegenCapShare; each key is commented
    /// there. ResetDowned also clears ScDowned's per-world state (ScConfig.Reset runs at GameStarting). 3.4 only.
    ///
    /// DownedAtHome=false: a resident at home or a guard on watch dies at once again, as before this round (followers and a
    /// resident on a work shift or in a talk still go down: that is 0-XNPCCore's own rule, Leader or Owner). Every guard
    /// (her shift or talk link held, no stationing, no teleport, team strength 0, no voice) stays on either way.
    /// ResidentMedicsRevive=false: nobody of the community revives anyone; the player does (E with a first aid bandage or kit).
    /// </summary>
    public static partial class ScConfig
    {
        /// <summary>D5: residents at home and guards on watch go down at a killing hit (0-XNPCCore's timer, marker and revive).</summary>
        public static bool DownedAtHome = true;
        /// <summary>D5: a resident with the Medicine specialty goes to a downed member at home and revives her from her own pack.</summary>
        public static bool ResidentMedicsRevive = true;
        /// <summary>A medic under this share of her max health does not go (she is badly hurt herself).</summary>
        public static float ReviveMedicMinHealth = 0.35f;
        /// <summary>A medic farther than this many meters from the downed member is not sent.</summary>
        public static float ReviveMedicRange = 60f;
        /// <summary>A medic who gets no meter closer in this many seconds (not counting a fight on the way) cannot reach her: the next one goes.</summary>
        public static float ReviveNoGainSeconds = 12f;
        /// <summary>
        /// D6: SC's own messages when a member goes down, when a medic goes to her, when she is back up, and when she bleeds out.
        /// false: 0-XNPCCore's own lines only ("is down!" and its warnings), which go to the player for every member.
        /// </summary>
        public static bool DownedTell = true;

        private static void ResetDowned()
        {
            DownedAtHome = true;
            ResidentMedicsRevive = true;
            ReviveMedicMinHealth = 0.35f;
            ReviveMedicRange = 60f;
            ReviveNoGainSeconds = 12f;
            DownedTell = true;
            // the per-world state: a new world starts with nobody down and no medic on the way
            ScDowned.Reset();
        }

        private static void ParseDowned(DynamicProperties p)
        {
            ParseBoolKey(p, "DownedAtHome", ref DownedAtHome);
            ParseBoolKey(p, "ResidentMedicsRevive", ref ResidentMedicsRevive);
            ParseFloat(p, "ReviveMedicMinHealth", ref ReviveMedicMinHealth, 0f, 1f);
            ParseFloat(p, "ReviveMedicRange", ref ReviveMedicRange, 5f, 300f);
            ParseFloat(p, "ReviveNoGainSeconds", ref ReviveNoGainSeconds, 4f, 60f);
            ParseBoolKey(p, "DownedTell", ref DownedTell);
        }

        /// <summary>Round U2: the downed keys in one line (the load log, sc downed).</summary>
        public static string DownedLine()
        {
            return (DownedAtHome ? "residents at home and guards on watch go down at a killing hit" : "residents at home and guards on watch die at once (DownedAtHome false)")
                   + "; " + (ResidentMedicsRevive
                       ? "a resident with the Medicine specialty goes to a downed member at home (not below " + F2(ReviveMedicMinHealth * 100f)
                         + "% health, within " + F2(ReviveMedicRange) + " m, the next one after " + F2(ReviveNoGainSeconds) + " s without a meter gained)"
                       : "no resident revives anyone (ResidentMedicsRevive false)")
                   + "; SC's messages " + (DownedTell ? "on" : "off (DownedTell false: 0-XNPCCore's own lines, to you for every member)");
        }
    }
}
