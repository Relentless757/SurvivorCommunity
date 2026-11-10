using System;
using System.Text;

namespace SurvivorCommunity
{
    /// <summary>
    /// Orders a member is given through 0-XNPCCore's Voice Control - typed "@name stay", the order wheel, spoken
    /// commands - read as member STATES, never as the member leaving (migration M2.4, decision D6).
    ///
    /// Voice Control obeys only the NPC's leader (VCCommandRouter.ExecuteAuthoritative), so only a Following or
    /// PendingStation member can receive one: residents, runners and stand-ins have no leader. Every order it gives is
    /// one of SCore's, written to the CurrentOrder cvar (SCore's buffOrder* buffs, or ExecuteCMD):
    ///   Follow, a formation slot, MoveTo   1  (buffOrderFollow; the vcFormation* / vcMoveTo* cvars say where)
    ///   Stay / wait / hold position        2  (buffOrderStay: GuardThere, the NPC's own spot)
    ///   Guard (wheel, squad)               2  (buffOrderGuardHere: Guarding 1, the leader's spot)
    ///   Guard and return                   9  (buffOrderGuard: Guarding 2 - the very order a resident holds)
    ///   Wander                             3
    ///   Show me a route (SetPatrolPoint)   4  (ExecuteCMD SetPatrol, then the cvar directly)
    ///   Patrol                             5
    ///   Loot (SCore's dialog / pack menus) 7  (keeps Owner, drops Leader; V4 teleports and hides it like a follower)
    /// None of them touches Owner, Voice Control never writes Leader or Owner, and a spoken "dismiss" is already a Go
    /// Home for members (Patches_VoiceControl). So a member holding one of these is still a follower - in the party,
    /// counted against MaxFollowers - who stays where the player put them until called back ("@name follow") or
    /// fetched. Engage stances (vcEngage), the follow distance (vcFollowMin) and crouch (CrouchOverride) are cvars
    /// beside the order; they change nothing here and are only shown.
    /// </summary>
    public static class ScOrders
    {
        /// <summary>
        /// Voice Control's per-NPC movement state, cleared when a member is stationed (Station) or leaves (CleanupStale):
        /// a formation slot or a MoveTo target means nothing at a post, and SCore's SetCrouching honours CrouchOverride
        /// for ANY NPC, so a resident told to crouch while following would otherwise crouch at home for good. vcEngage and
        /// vcFollowMin stay: they are the player's preferences for when that member follows again.
        /// </summary>
#if SC_V34
        // 3.4: and 0-XNPCCore 3.4's job orders - a field job (SdcsLootMode 1-9: loot, harvest, mine, dig, pick, butcher,
        // repair, base keeper; its filters), the party-medic role, a vehicle order and a remembered base - which would keep
        // a stationed member looting or driving over their post (VCCommandRouter sets them, nothing at a post clears them).
        // X2 (0-XNPCCore 3.4.03): SdcsHolsteredByOrder, "put your weapon away" kept through a save (the world-entry re-arm
        // leaves the hand bare while it is set; only Voice Control's dismiss clears it), so a resident is armed at the post
        public static readonly string[] VoiceMovementCvars =
        {
            "vcFormationDist", "vcFormationAngle", "vcMoveToActive", "vcMoveToFailed", "vcMoveToX", "vcMoveToY", "vcMoveToZ",
            "vcTargetX", "vcTargetY", "vcTargetZ", "CrouchOverride", "$XVC_PendingCommand", "$XVC_LadderHold", "$XVC_LadderFollowMode",
            "SdcsLootMode", "SdcsMedicRole", "SdcsVehicleOrder", "SdcsRememberBase", "SdcsMineFilter", "SdcsPickFilter",
            "SdcsHolsteredByOrder"
        };
#else
        public static readonly string[] VoiceMovementCvars =
        {
            "vcFormationDist", "vcFormationAngle", "vcMoveToActive", "vcMoveToFailed", "vcMoveToX", "vcMoveToY", "vcMoveToZ",
            "vcTargetX", "vcTargetY", "vcTargetZ", "CrouchOverride", "$XVC_PendingCommand", "$XVC_LadderHold", "$XVC_LadderFollowMode"
        };
#endif

        /// <summary>
        /// The order in the CurrentOrder cvar; false when the cvar is absent. Read here rather than through SCore's
        /// GetCurrentOrder, which answers Wander for a missing cvar - that would read as a held voice order.
        /// </summary>
        public static bool TryCurrent(EntityAlive npc, out EntityUtilities.Orders order)
        {
            order = EntityUtilities.Orders.None;
            if (npc == null || npc.Buffs == null || !npc.Buffs.HasCustomVar("CurrentOrder")) return false;
            order = (EntityUtilities.Orders)(int)npc.Buffs.GetCustomVar("CurrentOrder");
            return true;
        }

        /// <summary>
        /// An order a following member may hold: Follow itself and every order Voice Control (or SCore's own order menus)
        /// gives a follower. False for None (0), Hire (6) and Task (8, a supply runner's): nothing gives a follower those,
        /// so they are a broken state the ticker repairs.
        /// </summary>
        public static bool IsHeld(EntityUtilities.Orders o)
        {
            switch (o)
            {
                case EntityUtilities.Orders.Follow:
                case EntityUtilities.Orders.Stay:
                case EntityUtilities.Orders.Wander:
                case EntityUtilities.Orders.SetPatrolPoint:
                case EntityUtilities.Orders.Patrol:
                case EntityUtilities.Orders.Loot:
                case EntityUtilities.Orders.Guard:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// The member is actually with the player: Follow, or Loot (which V4's leader component treats the same way -
        /// teleported after the player beyond 60 m and hidden for a vehicle ride). Everything else is held somewhere.
        /// </summary>
        public static bool WithLeader(EntityUtilities.Orders o)
        {
            return o == EntityUtilities.Orders.Follow || o == EntityUtilities.Orders.Loot;
        }

        /// <summary>Removes Voice Control's movement cvars (VoiceMovementCvars). Idempotent; any NPC.</summary>
        private static readonly string[] CombatModeBuffs = { "buffNPCModFullControlMode", "buffNPCModThreatControlMode", "buffNPCModStopAttacking" };

        public static void ClearVoiceMovement(EntityAlive npc)
        {
            if (npc == null || npc.Buffs == null) return;
            try
            {
                for (int i = 0; i < VoiceMovementCvars.Length; i++)
                    if (npc.Buffs.HasCustomVar(VoiceMovementCvars[i])) npc.Buffs.RemoveCustomVar(VoiceMovementCvars[i]);
                // XNPCCore's player-wide combat modes stamp these on NPCs whose Leader is the player and remove them the
                // same way; a resident has no Leader, so one stationed under "full control" would keep never attacking
                for (int i = 0; i < CombatModeBuffs.Length; i++)
                    if (npc.Buffs.HasBuff(CombatModeBuffs[i])) npc.Buffs.RemoveBuff(CombatModeBuffs[i]);
            }
            catch (Exception e)
            {
                ScLog.Once("orders-clear-voice", e);
            }
        }

        /// <summary>
        /// The held order as a word for the community window ("holding", "guarding" ...), or "" while the member simply
        /// follows. Stay and the wheel's Guard are both order 2; SCore's Guarding cvar (1) tells the guard apart, read
        /// from the loaded NPC when there is one.
        /// </summary>
        public static string StateWord(Member m)
        {
            if (m == null || (m.Status != MemberStatus.Following && m.Status != MemberStatus.PendingStation)) return "";
            switch (m.HeldOrder)
            {
                case EntityUtilities.Orders.Stay:
                    EntityAlive npc = ScCommunity.FindNpc(m.Id);
                    bool guarding = npc != null && npc.Buffs != null && npc.Buffs.GetCustomVar("Guarding") >= 1f;
                    return Localization.Get(guarding ? "scHeldGuardHere" : "scHeldStay");
                case EntityUtilities.Orders.Guard: return Localization.Get("scHeldGuard");
                case EntityUtilities.Orders.Wander: return Localization.Get("scHeldWander");
                case EntityUtilities.Orders.SetPatrolPoint: return Localization.Get("scHeldRoute");
                case EntityUtilities.Orders.Patrol: return Localization.Get("scHeldPatrol");
                case EntityUtilities.Orders.Loot: return Localization.Get("scHeldLoot");
                default: return "";
            }
        }

        /// <summary>The order and Voice Control's state beside it, for the console and the log.</summary>
        public static string Describe(EntityAlive npc)
        {
            if (npc == null || npc.Buffs == null) return "-";
            var sb = new StringBuilder();
            EntityUtilities.Orders o;
            sb.Append("order ").Append(TryCurrent(npc, out o) ? o + "(" + (int)o + ")" : "unset");
            if (npc.Buffs.HasCustomVar("Guarding")) sb.Append(" guarding=").Append(npc.Buffs.GetCustomVar("Guarding").ToString("0"));
            if (npc.Buffs.HasCustomVar("vcEngage"))
            {
                int engage = (int)npc.Buffs.GetCustomVar("vcEngage");
                sb.Append(" engage=").Append(engage == 1 ? "aggressive" : engage == 2 ? "hold fire" : engage == 3 ? "disengage" : "defensive");
            }
            if (npc.Buffs.GetCustomVar("vcFollowMin") > 0f) sb.Append(" followMin=").Append(npc.Buffs.GetCustomVar("vcFollowMin").ToString("0.#"));
            if (npc.Buffs.GetCustomVar("vcFormationDist") > 0f)
                sb.Append(" formation=").Append(npc.Buffs.GetCustomVar("vcFormationDist").ToString("0.#")).Append("m@").Append(npc.Buffs.GetCustomVar("vcFormationAngle").ToString("0"));
            if (npc.Buffs.GetCustomVar("vcMoveToActive") > 0f) sb.Append(" moveTo=active");
            if (npc.Buffs.GetCustomVar("CrouchOverride") >= 1f) sb.Append(" crouched");
#if SC_V34
            if (npc.Buffs.GetCustomVar("SdcsLootMode") > 0f) sb.Append(" job=").Append(npc.Buffs.GetCustomVar("SdcsLootMode").ToString("0"));   // C1: 10 = crafting
#endif
            return sb.ToString();
        }
    }
}
