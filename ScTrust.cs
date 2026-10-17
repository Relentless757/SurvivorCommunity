using System;
using System.Collections.Generic;

namespace SurvivorCommunity
{
    /// <summary>Where a wild survivor stands with a player (the user's design, 2026-10-07).</summary>
    public enum Trust { Neutral, Friendly, Hostile }

    /// <summary>
    /// Trust between a wild survivor and a player: Neutral by default, Friendly once their recruit favour is done (the
    /// existing Prospect.Done / sc_favourDone), Hostile for good once the player's damage lands on them - any hit at all,
    /// explosions too (the user's choice). A hostile survivor attacks that player on sight and has no talk prompt for
    /// them. Community members and stand-ins have no trust (their mood is the mechanic), raiders are hostile by class,
    /// traders are left alone.
    ///
    /// Hostility is a synced cvar on the survivor: %sc_hostile_&lt;player entity id&gt; (per player, read by the faction
    /// postfix and the talk prompt, which also run on clients) and %sc_hostile (anyone: turrets, POI security). Cvars
    /// are saved with the body (EntityAliveSDXV4.Write keeps every non-zero cvar), and Persist keeps the body itself.
    /// The hooks are Patches_Trust.
    /// </summary>
    public static class ScTrust
    {
        public const string AnyHostileCvar = "%sc_hostile";
        private const string HostilePrefix = "%sc_hostile_";
        private static readonly Dictionary<int, string> varNames = new Dictionary<int, string>();
        private static int madeHostile;

        /// <summary>The per-player hostility cvar name, cached (the faction postfix asks it constantly).</summary>
        public static string HostileVar(int playerId)
        {
            string name;
            if (!varNames.TryGetValue(playerId, out name))
            {
                name = HostilePrefix + playerId;
                varNames[playerId] = name;
            }
            return name;
        }

        /// <summary>
        /// A survivor trust applies to: a framework NPC (SCore's two classes) that is not a raider, not a member or
        /// stand-in, not one of an enclave's people (E2: the enclave's own rules, ScEnclavePeople.OnHitBy), and not a
        /// trader. Type and cvar tests only.
        /// </summary>
        public static bool Subject(EntityAlive npc)
        {
            if (npc == null || npc.Buffs == null || !ScNpc.IsFramework(npc)) return false;
            if (ScXnpc.IsRaider(npc) || ScXnpc.IsCommunity(npc) || ScEnclavePeople.IsEnclaveNpc(npc)) return false;
            if (ScCommunity.HiredBy(npc) > 0) return false;      // somebody's hired companion
            return !IsTrader(npc);
        }

        public static bool IsTrader(EntityAlive npc)
        {
            EntityNPC n = npc as EntityNPC;
            return n != null && n.NPCInfo != null && !string.IsNullOrEmpty(n.NPCInfo.DialogID)
                   && n.NPCInfo.DialogID.StartsWith("trader", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>This survivor is hostile to this player (the per-player cvar). Hot path: two type tests and a dictionary hit.</summary>
        public static bool IsHostileTo(EntityAlive npc, EntityPlayer player)
        {
            if (npc == null || player == null || npc.Buffs == null) return false;
            if (npc.Buffs.GetCustomVar(HostileVar(player.entityId)) <= 0f) return false;
            return StillHolds(npc);
        }

        /// <summary>This survivor is hostile to some player (turrets, POI security).</summary>
        public static bool IsHostileToAny(Entity e)
        {
            EntityAlive a = e as EntityAlive;
            if (a == null || a.Buffs == null || a.Buffs.GetCustomVar(AnyHostileCvar) <= 0f) return false;
            return StillHolds(a);
        }

        /// <summary>A grudge lapses once they are one of the community or somebody's hired companion (both also clear it:
        /// ScCommunity.Join and the OnHire handler in ModApi call StripOnJoin).</summary>
        private static bool StillHolds(EntityAlive npc)
        {
            return !ScXnpc.IsCommunity(npc) && ScCommunity.HiredBy(npc) <= 0;
        }

        public static Trust Level(EntityAlive npc, EntityPlayer player)
        {
            if (IsHostileTo(npc, player)) return Trust.Hostile;
            if (npc != null && npc.Buffs != null && npc.Buffs.HasCustomVar("sc_favourDone")) return Trust.Friendly;
            Prospect p = npc != null ? ScRegistry.GetProspect(npc.entityId) : null;
            return p != null && p.Done ? Trust.Friendly : Trust.Neutral;
        }

        /// <summary>
        /// Who the player may hurt (CanDamage / CanTakeDamage postfixes): any framework NPC that is not a trader, not one of
        /// the player's own people (members and stand-ins, unless CommunityFriendlyFire) and nobody's hired companion
        /// (SCore's ally and PvP rules decide those - a stray shot in a shared fight must not turn one hostile for good).
        /// </summary>
        public static bool PlayerMayHurt(EntityAlive npc, EntityPlayer player)
        {
            if (npc == null || player == null || npc is EntityPlayer || npc.IsDead()) return false;
            if (!ScNpc.IsFramework(npc)) return false;
            if (IsTrader(npc)) return false;
            // your own people: friendly fire decides (before the hire test - a member who ever followed carries Owner)
            if (ScRegistry.GetMember(npc.entityId) != null
                || (npc.Buffs != null && (npc.Buffs.HasCustomVar(ScRadio.StandInCvar) || npc.Buffs.HasCustomVar("sc_member")))) return ScConfig.CommunityFriendlyFire;
            return ScCommunity.HiredBy(npc) <= 0;
        }

        /// <summary>
        /// Host: the player's damage landed on a survivor (Patches_Trust, after the hit). The first one makes them hostile
        /// to that player for good: both cvars, Persist so the body keeps its grudge through a chunk unload, and an open
        /// recruit favour ends (prospect dropped, its quest closed as failed).
        /// </summary>
        public static void OnHitBy(EntityAlive npc, EntityPlayer player)
        {
            if (!Subject(npc) || player == null || IsHostileTo(npc, player)) return;
            npc.Buffs.SetCustomVar(HostileVar(player.entityId), 1f);
            npc.Buffs.SetCustomVar(AnyHostileCvar, 1f);
            npc.Buffs.SetCustomVar("Persist", 1f);
            // a prospect's wait-here order lives in the order cvars (buffOrderGuard only sets them, for a second)
            if (EntityUtilities.GetCurrentOrder(npc.entityId) == EntityUtilities.Orders.Guard) npc.Buffs.AddBuff("buffOrderWander");
            madeHostile++;
            string name = ScCommunity.DisplayName(npc);
            Prospect p = ScRegistry.GetProspect(npc.entityId);
            if (p != null)
            {
                ScQuests.CloseFavour(player, p.Id, false);
                ScRegistry.Prospects.Remove(p.Id);
                npc.Buffs.RemoveCustomVar("sc_favour");
                npc.Buffs.RemoveCustomVar("sc_favourDone");
                ScRegistry.SaveAtomic();
            }
            ScCommunity.Tell(player, string.Format(Localization.Get("scTrustHostile"), name), "");
            ScLog.Out(name + " (" + npc.entityId + ") is hostile to " + player.EntityName + " for good" + (p != null ? " - their favour is off" : ""));
        }

        /// <summary>sc trust: forgives (testing). Clears this player's grudge, and the any-player marker when no other is left.</summary>
        public static bool Forgive(EntityAlive npc, EntityPlayer player)
        {
            if (npc == null || player == null || npc.Buffs == null || !IsHostileTo(npc, player)) return false;
            // set to 0 rather than removed: a removal never reaches clients, a 0 does (and a 0 is never saved)
            npc.Buffs.SetCustomVar(HostileVar(player.entityId), 0f);
            bool other = false;
            foreach (KeyValuePair<string, float> kv in npc.Buffs.CVars)
                if (kv.Value > 0f && kv.Key.StartsWith(HostilePrefix, StringComparison.Ordinal)) { other = true; break; }
            if (!other) npc.Buffs.SetCustomVar(AnyHostileCvar, 0f);
            npc.SetRevengeTarget(null);
            npc.SetAttackTarget(null, 0);
            return true;
        }

        /// <summary>Join: whatever grudges the survivor held are gone (a member has no trust).</summary>
        public static void StripOnJoin(EntityAlive npc)
        {
            if (npc == null || npc.Buffs == null) return;
            var names = new List<string>();
            foreach (KeyValuePair<string, float> kv in npc.Buffs.CVars)
                if (kv.Value > 0f && kv.Key.StartsWith(AnyHostileCvar, StringComparison.Ordinal)) names.Add(kv.Key);
            for (int i = 0; i < names.Count; i++) npc.Buffs.SetCustomVar(names[i], 0f);
        }

        public static string Report() { return madeHostile + " survivor(s) turned hostile this session"; }
    }
}
