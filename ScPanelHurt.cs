using System;
using HarmonyLib;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round m1p (the M1 polish bundle; the user's decision, memory sc-member-inventory-panel and sc-m1-rounds): the member
    /// inventory panel closes when the member it shows takes a hit, and says why. Only a hit on her closes it: the player's own
    /// fight, or a hit on anyone else, leaves it open.
    ///
    /// The panel watches one body while it is open (Watch, from XUiC_ScMemberInv.SetTarget and OnClose). The hit is seen in a
    /// postfix on vanilla's EntityAlive.ProcessDamageResponseLocal - every landed hit on the host, which SCore's NPC classes
    /// reach through their own overrides (EntityAliveSDX and EntityAliveSDXV4 call the base after their invulnerability test,
    /// so an invulnerable member is never "hit"). It is ModApi's existing "EntityAlive::ProcessDamageResponseLocal" target
    /// (round k1's ScTeam postfix), so no new patch target. A hit counts when it did damage or wore her armor; a bleed, burn or
    /// any other effect over time (DamageSource.BuffClass set) does not, so the panel stays open to bandage a bleeding member
    /// (the hit that made her bleed closed it already). A killing hit is left to the panel's own check (she is dead).
    ///
    /// The postfix only notes the hit (one int compare for any other entity); the panel's Update closes itself on its next
    /// frame (TakeHit) - never from inside the damage call, where its close would re-arm her weapons (SCore's close hook).
    /// Host / single player, main thread.
    /// </summary>
    public static class ScPanelHurt
    {
        /// <summary>The body the open panel shows (-1: none), and whether it was hit since the panel opened.</summary>
        private static int watched = -1;
        private static bool hit;
        private static int closedByHit;

        /// <summary>The panel opened on this member (null: it closed). A hit from before the open never closes it.</summary>
        internal static void Watch(EntityAlive member)
        {
            watched = member != null ? member.entityId : -1;
            hit = false;
        }

        /// <summary>The panel's Update: was the member it shows hit since it opened? True once (the panel closes and says why).</summary>
        internal static bool TakeHit(EntityAlive member)
        {
            if (!hit || member == null || member.entityId != watched) return false;
            hit = false;
            closedByHit++;
            return true;
        }

        /// <summary>Tells the player why the panel closed ("{0} took a hit, so the inventory closed.").</summary>
        internal static void Tell(EntityPlayer player, EntityAlive member)
        {
            string name = member != null ? ScCommunity.DisplayName(member) : "";
            ScCommunity.Tell(player, string.Format(Localization.Get("scInvClosedHit"), name), "ui_denied");
            ScLog.Verbose("member inventory: closed - " + name + " (id " + (member != null ? member.entityId : -1) + ") took a hit (round m1p; "
                          + closedByHit + " this session)");
        }

        [HarmonyPatch(typeof(EntityAlive), nameof(EntityAlive.ProcessDamageResponseLocal))]
        public static class Patch_PanelMemberHit
        {
            [HarmonyPostfix]
            public static void Postfix(EntityAlive __instance, DamageResponse _dmResponse)
            {
                try
                {
                    if (watched < 0 || hit || __instance == null || __instance.entityId != watched) return;
                    if (_dmResponse.Fatal) return;                                    // dead: the panel's own check closes it
                    DamageSource src = _dmResponse.Source;
                    if (src == null || src.BuffClass != null) return;                  // a bleed or other effect over time
                    if (_dmResponse.Strength <= 0 && _dmResponse.ArmorDamage <= 0) return;   // nothing landed
                    hit = true;
                }
                catch (Exception e)
                {
                    ScLog.Once("panel-hurt", e);
                }
            }
        }
    }
}
