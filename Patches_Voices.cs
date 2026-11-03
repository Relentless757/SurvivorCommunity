using System;
using System.Reflection;
using HarmonyLib;

namespace SurvivorCommunity
{
    /// <summary>
    /// V2: the vanilla and SCore hooks behind the raider taunts (ScTaunts; research\voices\REPORT.md section 4). The hit and
    /// the death ride SC's existing postfixes (Patches_Trust.Patch_DamageResponse, Patches_Npc.Patch_SetDead). All four
    /// here are listed in ModApi's expected targets, each class off by Prepare (with an error line) when its target is gone.
    ///  - EntityAlive.SetAttackTarget(EntityAlive, int) (3.4 EntityAlive.cs:5978, public, not virtual; SCore's V4 hides it
    ///    with a "new" method that calls it, and every UAI task calls it on an EntityAlive): a prefix keeps the old
    ///    attackTarget (public field, :709), a postfix sees whether it changed. Called for every entity's every target
    ///    refresh, so the postfix returns on a plain reference compare when nothing changed, and on a type test for
    ///    everything but an EntityAliveSDXV4. Never GetAttackTarget (AggressiveInlining, :5958).
    ///  - EntityAlive.AwardKill(EntityAlive killer) (:5403, virtual; the victim's own call from OnEntityDeath when not
    ///    remote, :5451; players don't override it, EntityTrader's and SCore's overrides call base): the local player's death.
    ///  - EntityAlive.PlayGiveUpSound() (:5482, virtual, no override in vanilla or SCore): SCore's MoveToInvestigate gives up.
    ///  - SCore's UAI.UAITaskMoveToInvestigate.Start(Context _context) (SC's ScRaiderInvestigate, Config/utilityai.xml).
    /// </summary>
    public static class Patches_Voices
    {
        public static MethodBase SetAttackTargetTarget()
        {
            return AccessTools.DeclaredMethod(typeof(EntityAlive), "SetAttackTarget", new[] { typeof(EntityAlive), typeof(int) });
        }

        public static MethodBase AwardKillTarget()
        {
            return AccessTools.DeclaredMethod(typeof(EntityAlive), "AwardKill", new[] { typeof(EntityAlive) });
        }

        public static MethodBase GiveUpTarget()
        {
            return AccessTools.DeclaredMethod(typeof(EntityAlive), "PlayGiveUpSound", Type.EmptyTypes);
        }

        public static MethodBase InvestigateTarget()
        {
            return AccessTools.DeclaredMethod(typeof(UAI.UAITaskMoveToInvestigate), "Start", new[] { typeof(UAI.Context) });
        }

        private static bool Found(MethodBase m, string label)
        {
            if (m != null) return true;
            ScLog.Err(label + " not found (game or SCore update?) - that raider taunt is off");
            return false;
        }

        /// <summary>Spot (a raider takes a living player as its target).</summary>
        [HarmonyPatch]
        public static class Patch_TauntTarget
        {
            [HarmonyPrepare]
            public static bool Prepare() { return Found(SetAttackTargetTarget(), "EntityAlive.SetAttackTarget(EntityAlive, int)"); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return SetAttackTargetTarget(); }

            [HarmonyPrefix]
            public static void Prefix(EntityAlive __instance, out EntityAlive __state)
            {
                __state = __instance != null ? __instance.attackTarget : null;
            }

            [HarmonyPostfix]
            public static void Postfix(EntityAlive __instance, EntityAlive __state)
            {
                // hot: the same target refreshed is the common case - one reference compare, no Unity null test
                if ((object)__instance == null || (object)__state == (object)__instance.attackTarget) return;
                if (!(__instance is EntityAliveSDXV4) || !ScConfig.RaiderTaunts) return;
                try { ScTaunts.OnTargetChanged(__instance, __state, __instance.attackTarget); }
                catch (Exception e) { ScLog.Once("voice-taunt-target", e); }
            }
        }

        /// <summary>Killed: the local player's own death names its killer.</summary>
        [HarmonyPatch]
        public static class Patch_TauntKill
        {
            [HarmonyPrepare]
            public static bool Prepare() { return Found(AwardKillTarget(), "EntityAlive.AwardKill(EntityAlive)"); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return AwardKillTarget(); }

            [HarmonyPostfix]
            public static void Postfix(EntityAlive __instance, EntityAlive killer)
            {
                if (!(__instance is EntityPlayerLocal) || !(killer is EntityAliveSDXV4) || !ScConfig.RaiderTaunts) return;
                try { ScTaunts.OnKilledPlayer(killer, (EntityPlayer)__instance); }
                catch (Exception e) { ScLog.Once("voice-taunt-kill", e); }
            }
        }

        /// <summary>Lost: SCore's investigation gave up.</summary>
        [HarmonyPatch]
        public static class Patch_TauntGiveUp
        {
            [HarmonyPrepare]
            public static bool Prepare() { return Found(GiveUpTarget(), "EntityAlive.PlayGiveUpSound()"); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return GiveUpTarget(); }

            [HarmonyPostfix]
            public static void Postfix(EntityAlive __instance)
            {
                if (!(__instance is EntityAliveSDXV4) || !ScConfig.RaiderTaunts) return;
                try { ScTaunts.OnGaveUp(__instance); }
                catch (Exception e) { ScLog.Once("voice-taunt-giveup", e); }
            }
        }

        /// <summary>Search: SCore's MoveToInvestigate starts (SC's ScRaiderInvestigate).</summary>
        [HarmonyPatch]
        public static class Patch_TauntInvestigate
        {
            [HarmonyPrepare]
            public static bool Prepare() { return Found(InvestigateTarget(), "UAI.UAITaskMoveToInvestigate.Start(Context)"); }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return InvestigateTarget(); }

            [HarmonyPostfix]
            public static void Postfix(UAI.Context _context)
            {
                EntityAlive self = _context != null ? _context.Self : null;
                if (!(self is EntityAliveSDXV4) || !ScConfig.RaiderTaunts) return;
                try { ScTaunts.OnInvestigate(self); }
                catch (Exception e) { ScLog.Once("voice-taunt-investigate", e); }
            }
        }
    }
}
