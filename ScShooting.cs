using System;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// The Shooting skill aims the gun (round sh, the user's decisions of 2026-10-13: everyone, a gradual fall-off, engage
    /// at twice the range). Before this every NPC with the player's body was an aimbot: SCore's attack task points the look
    /// ray from its head straight at the target's head in the same call that fires (UAITaskAttackTargetEntitySDX.Update,
    /// SetLookPosition), the shot is a hitscan ray, and 0-XNPCCore's SdcsNpcAccuracy cuts a standing NPC's spread to
    /// 0.02-0.07 degrees (research: scratchpad mig\aim_research.md). 0-XNPCCore 3.4.02 made its NPCs shoot like players, at a
    /// spread its kill level sets; on our bodies Patches_Xnpc X18/X19 skip that and hold the gun's own spread at nothing
    /// (ScConfig.XnpcAimSpread -1), so the error below is again the only one.
    ///
    /// Accuracy. Each shooter has a hit range of ShootingRangePerStar (5 m) per Shooting star - 5 m at 1 star, 35 m at 7 -
    /// and an aim error on every shot: a uniform disc that measures ShootingHitRadius (0.25 m, a torso's half-width) at
    /// the hit range, a half-angle of atan(radius / hit range) - about 2.9 degrees at 1 star and 0.4 at 7. Inside the hit
    /// range the angle stays, so the disc fits on the torso and nearly every shot lands (97% at the range itself). Past it
    /// the angle grows with the distance (x distance / hit range), so the disc grows with its square: against a whole body
    /// about 19% of shots land at twice the range, 4% at three times (a simulation of the disc against a torso, head and
    /// legs silhouette, 2026-10-13). With ShootingAimChest the shot is first turned from the target's head to its chest
    /// (Entity.getChestPosition): centred on the head, the disc's upper part passes over it and a third of the shots inside
    /// the range would miss; a headshot is still the shot that strays high. The shots of one trigger pull (a shotgun's
    /// pellets, one frame) share one error and keep their own spread. Hitscan guns through ItemActionRanged.getDirectionOffset;
    /// bows, crossbows and launchers through ItemActionLauncher.GetActionEffectsValues, whose direction the server sends to
    /// everyone (getDirectionOffset is skipped for them: they call it on every machine).
    ///
    /// Range. A gun engages no farther than ShootingEngageMult (2) times the hit range, and never closer than
    /// ShootingEngageMin (12 m: 0-XNPCCore's BackupRanged walks a shooter back to 10 m, which a 10 m limit would put outside
    /// it), or the gun's own range when that is shorter. Through SCore's InWeaponRange (ScMelee.Patch_InWeaponRange), which
    /// the packs' ranged attack asks and NotInWeaponRange inverts - so a raider walks in to its own range - and through
    /// ScEngage and the back-off aim.
    ///
    /// Return fire (the user's call, 2026-10-13: with nothing in the packs that walks a follower, a guard or a stranger
    /// toward a target, a 1-star one would stand and take fire from a raider at 40 m). A shooter being shot at answers its
    /// attacker out to the gun's own range, with the same aim error - so mostly misses: the attacker is its revenge target
    /// (whoever last hurt it, for 25 s), or holds a gun and has it as its target (a raider still missing it). It still
    /// starts no fight of its own past its engage range.
    ///
    /// Stars. A member's own (also for their radio stand-in), the stars stamped on an outpost stand-in, a wild survivor's
    /// profile (ScProfile, now rolled when they spawn), a raider's rolled from its tier at spawn (RaiderShootingStars,
    /// RaiderStarsVar), else ShootingDefaultStars. Our bodies only (ScXnpc.IsBody), on the machine that runs the NPC (the
    /// server: the AI, the shot's ray and the projectile that deals damage are all there).
    /// </summary>
    public static class ScShooting
    {
        /// <summary>A raider's Shooting stars, rolled at spawn from its tier (never 0, so the cvar is saved with it).</summary>
        public const string RaiderStarsVar = "sc_rshoot";
        /// <summary>The look ray counts as aimed at the action's target within this many degrees of its head.</summary>
        private const float SameTargetDegrees = 3f;
        /// <summary>The Shooting level ScSkills.StampCvars writes on members and stand-ins ("sc_shooting").</summary>
        private static readonly string StampedVar = "sc_" + ScNames.SkillKeys[(int)Skill.Shooting];

        private static int aimed, aimedChest, launched, capped, returnFire, raidersRolled;
        // one aim error per trigger pull (a point in the unit disc, scaled per ray): the rays of one shot share a frame
        private static int errId = -1, errFrame = -1;
        private static Vector2 err;

        public static void Reset()
        {
            aimed = aimedChest = launched = capped = returnFire = raidersRolled = 0;
            errId = errFrame = -1;
        }

        /// <summary>One of our bodies, on the machine that runs it, with the skill switch on.</summary>
        public static bool Applies(EntityAlive e)
        {
            return ScConfig.ShootingSkill && e != null && !e.isEntityRemote && ScXnpc.IsBody(e);
        }

        private static int Clamp(int stars) { return Math.Max(1, Math.Min(Member.MaxStars, stars)); }

        /// <summary>This shooter's Shooting stars, and where they came from (member, stand-in, stamped, profile, raider, default).</summary>
        public static int Stars(EntityAlive e, out string source)
        {
            source = "default";
            if (e == null) return Clamp(ScConfig.ShootingDefaultStars);
            Member m = ScRegistry.Loaded ? ScRegistry.GetMember(e.entityId) : null;
            if (m != null) { source = "member"; return m.Stars(Skill.Shooting); }
            EntityBuffs b = e.Buffs;
            if (b != null)
            {
                if (ScRegistry.Loaded && b.HasCustomVar(ScRadio.StandInCvar))
                {
                    m = ScRegistry.GetMember((int)b.GetCustomVar(ScRadio.StandInCvar));
                    if (m != null) { source = "stand-in"; return m.Stars(Skill.Shooting); }
                }
                // an outpost stand-in carries its member's stamped levels (ScSkills.StampCvars)
                if (b.HasCustomVar(StampedVar)) { source = "stamped"; return Clamp((int)b.GetCustomVar(StampedVar)); }
                int p = ScProfile.SkillStars(e, Skill.Shooting);
                if (p > 0) { source = "profile"; return p; }
                if (b.HasCustomVar(RaiderStarsVar)) { source = "raider"; return Clamp((int)b.GetCustomVar(RaiderStarsVar)); }
            }
            return Clamp(ScConfig.ShootingDefaultStars);
        }

        public static float HitRange(int stars) { return ScConfig.ShootingRangePerStar * Clamp(stars); }

        public static float EngageRange(int stars) { return Mathf.Max(ScConfig.ShootingEngageMin, ScConfig.ShootingEngageMult * HitRange(stars)); }

        /// <summary>The aim error's half-angle in degrees within the hit range (it grows past it, ErrorTan).</summary>
        public static float ConeDegrees(int stars)
        {
            return Mathf.Atan(ScConfig.ShootingHitRadius / Mathf.Max(0.5f, HitRange(stars))) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// The aim error's half-angle as a tangent, for a target this far away (-1 when unknown: the in-range angle):
        /// radius / hit range, times distance / hit range past it.
        /// </summary>
        public static float ErrorTan(int stars, float distance)
        {
            float range = Mathf.Max(0.5f, HitRange(stars));
            float t = ScConfig.ShootingHitRadius / range;
            return distance > range ? t * (distance / range) : t;
        }

        /// <summary>The engage limit for this body's action at that index when it is a gun, else -1 (SCore's verdict stands).</summary>
        public static float RangedCap(EntityAlive e, int index)
        {
            if (!Applies(e)) return -1f;
            ItemClass held = e.inventory != null ? e.inventory.holdingItem : null;
            ItemAction[] acts = held != null ? held.Actions : null;
            if (acts == null || index < 0 || index >= acts.Length || !(acts[index] is ItemActionRanged)) return -1f;
            string source;
            return EngageRange(Stars(e, out source));
        }

        /// <summary>
        /// A gun's range as far as this shooter engages target with it: the gun's own when the skill does not apply or the
        /// target is shooting at it (ReturnsFire), else no farther than its engage range.
        /// </summary>
        public static float CapRange(EntityAlive e, float gunRange, EntityAlive target)
        {
            if (!Applies(e) || ReturnsFire(e, target)) return gunRange;
            string source;
            return Mathf.Min(gunRange, EngageRange(Stars(e, out source)));
        }

        /// <summary>
        /// The target is shooting at this NPC: it is the NPC's revenge target (it hurt it in the last 25 s), or it holds a gun
        /// and its target - its attack target or its AI action's - is this NPC.
        /// </summary>
        public static bool ReturnsFire(EntityAlive e, EntityAlive t)
        {
            if (e == null || t == null || t == e || t.IsDead()) return false;
            if (e.GetRevengeTarget() == t) return true;
            ItemClass held = t.inventory != null ? t.inventory.holdingItem : null;
            if (held == null || held.Actions == null || held.Actions.Length == 0 || !(held.Actions[0] is ItemActionRanged)) return false;
            if (t.GetAttackTarget() == e) return true;
            UAI.Context ctx = t.utilityAIContext;
            return ctx != null && UAI.UAIUtils.ConvertToEntityAlive(ctx.ActionData.Target) == e;
        }

        /// <summary>ScMelee.Patch_InWeaponRange turned a gun's "in range" down: the target was beyond the shooter's engage range.</summary>
        public static void CountCapped() { capped++; }

        /// <summary>ScMelee.Patch_InWeaponRange let a gun answer fire from beyond its engage range.</summary>
        public static void CountReturnFire() { returnFire++; }

        private static float Rand(EntityAlive e) { return e.rand != null ? e.rand.RandomFloat : UnityEngine.Random.value; }

        /// <summary>Turns a direction by this shot's aim error: uniform in a disc whose half-angle has tangent tan.</summary>
        private static Vector3 Cone(EntityAlive e, Vector3 dir, float tan)
        {
            int frame = Time.frameCount;
            if (e.entityId != errId || frame != errFrame)
            {
                errId = e.entityId;
                errFrame = frame;
                float r = Mathf.Sqrt(Rand(e));
                float a = 2f * Mathf.PI * Rand(e);
                err = new Vector2(r * Mathf.Cos(a), r * Mathf.Sin(a));
            }
            Vector3 d = dir.normalized;
            Vector3 right = Vector3.Cross(Vector3.up, d);
            if (right.sqrMagnitude < 1e-6f) right = Vector3.Cross(Vector3.forward, d);
            right.Normalize();
            Vector3 up = Vector3.Cross(d, right);
            return (d + (right * err.x + up * err.y) * tan).normalized;
        }

        /// <summary>
        /// The target the look ray was pointed at: the AI action's own, else the attack or revenge target - whichever has its
        /// head within SameTargetDegrees of the ray (SCore aimed it there this very call). Null when none does.
        /// </summary>
        private static EntityAlive AimedAt(EntityAlive e, Vector3 forward)
        {
            UAI.Context ctx = e.utilityAIContext;
            EntityAlive[] tries =
            {
                ctx != null ? UAI.UAIUtils.ConvertToEntityAlive(ctx.ActionData.Target) : null,
                e.GetAttackTarget(),
                e.GetRevengeTarget()
            };
            Vector3 from = e.getHeadPosition();
            foreach (EntityAlive t in tries)
            {
                if (t == null || t == e || t.IsDead()) continue;
                Vector3 toHead = t.getHeadPosition() - from;
                if (toHead.sqrMagnitude > 0.01f && Vector3.Angle(forward, toHead) < SameTargetDegrees) return t;
            }
            return null;
        }

        /// <summary>A hitscan shot: turned to the chest (ShootingAimChest), then by the shooter's aim error. result keeps its spread.</summary>
        public static Vector3 Aim(EntityAlive e, Vector3 forward, Vector3 result)
        {
            string source;
            int stars = Stars(e, out source);
            Vector3 dir = result;
            EntityAlive t = AimedAt(e, forward);
            float distance = t != null ? (t.getHeadPosition() - e.getHeadPosition()).magnitude : -1f;
            if (ScConfig.ShootingAimChest && t != null)
            {
                Vector3 toChest = t.getChestPosition() - e.getHeadPosition();
                if (toChest.sqrMagnitude > 0.01f)
                {
                    dir = Quaternion.FromToRotation(forward, toChest) * result;
                    aimedChest++;
                }
            }
            aimed++;
            return Cone(e, dir, ErrorTan(stars, distance));
        }

        /// <summary>A launcher's shot (bow, crossbow, rocket): the aim error only - the arrow's drop already brings it lower.</summary>
        public static Vector3 AimLauncher(EntityAlive e, Vector3 direction)
        {
            string source;
            int stars = Stars(e, out source);
            EntityAlive t = AimedAt(e, direction);
            float distance = t != null ? (t.getHeadPosition() - e.getHeadPosition()).magnitude : -1f;
            launched++;
            return Cone(e, direction, ErrorTan(stars, distance));
        }

        /// <summary>
        /// Host, from the bodies' PostInit (Patches_Npc): a raider's Shooting stars from its tier, once; a wild survivor's
        /// profile, so their Shooting stars aim their gun from the start rather than from the first talk. Our bodies only
        /// (ScXnpc.IsBody, the only ones the skill aims): a pack NPC (another mod's survivor) still gets its profile when the
        /// player first talks to it.
        /// </summary>
        public static void OnPostInit(EntityAlive e)
        {
            if (!ScCommunity.IsHost || e == null || e.Buffs == null || !ScXnpc.IsBody(e)) return;
            if (ScXnpc.IsRaider(e))
            {
                if (e.Buffs.HasCustomVar(RaiderStarsVar)) return;
                // T1..T3 from the class name's suffix; a raider class without one counts as T3 (ScXnpc.TierOf, as ScLoadout's
                // weapons), so a renamed class rolls the strongest stars - name new raider classes ...T1/T2/T3
                int tier = ScXnpc.TierOf(ScCommunity.ClassName(e));
                int lo = ScConfig.RaiderShootingStars[tier, 0], hi = ScConfig.RaiderShootingStars[tier, 1];
                int stars = e.rand != null ? e.rand.RandomRange(lo, hi + 1) : lo;
                e.Buffs.SetCustomVar(RaiderStarsVar, Clamp(stars));
                raidersRolled++;
                return;
            }
            ScProfile.Ensure(e);
        }

        /// <summary>sc xnpc &lt;id&gt;: this shooter's skill, ranges and aim error.</summary>
        public static string Describe(EntityAlive e)
        {
            if (!ScConfig.ShootingSkill) return "shooting skill off (perfect aim)";
            if (e == null || !ScXnpc.IsBody(e)) return "shooting skill n/a (not one of our bodies)";
            string source;
            int stars = Stars(e, out source);
            return "shooting " + stars + " star(s) (" + source + "): hits within " + HitRange(stars).ToString("0") + " m, engages within "
                   + EngageRange(stars).ToString("0") + " m, aim error " + ConeDegrees(stars).ToString("0.00") + " deg (growing past "
                   + HitRange(stars).ToString("0") + " m)" + (ScConfig.ShootingAimChest ? ", at the chest" : ", at the head");
        }

        /// <summary>sc xnpc: the switch, the numbers and the counters.</summary>
        public static string Report()
        {
            if (!ScConfig.ShootingSkill) return "off (perfect aim, the gun's own range)";
            var sb = new StringBuilder();
            sb.Append("hit range ").Append(ScConfig.ShootingRangePerStar.ToString("0.#")).Append(" m a star, engage x").Append(ScConfig.ShootingEngageMult.ToString("0.#"))
              .Append(" (at least ").Append(ScConfig.ShootingEngageMin.ToString("0")).Append(" m), aim error at 1/4/7 stars ")
              .Append(ConeDegrees(1).ToString("0.00")).Append('/').Append(ConeDegrees(4).ToString("0.00")).Append('/').Append(ConeDegrees(7).ToString("0.00"))
              .Append(" deg").Append(ScConfig.ShootingAimChest ? " at the chest" : " at the head")
              .Append("; shots aimed ").Append(aimed).Append(" (turned to the chest ").Append(aimedChest).Append("), launcher shots ").Append(launched)
              .Append(", engagements beyond range refused ").Append(capped).Append(", fire returned from beyond it ").Append(returnFire)
              .Append(", raiders' stars rolled ").Append(raidersRolled);
            return sb.ToString();
        }

        // ------------------------------------------------------------------ patches ----

        /// <summary>A hitscan gun's shot direction (ItemActionRanged.fireShot asks it once per ray).</summary>
        [HarmonyPatch]
        public static class Patch_ShotDirection
        {
            public static MethodBase Target()
            {
                return AccessTools.DeclaredMethod(typeof(ItemActionRanged), "getDirectionOffset",
                    new[] { typeof(ItemActionRanged.ItemActionDataRanged), typeof(Vector3), typeof(int) });
            }

            [HarmonyPrepare]
            public static bool Prepare()
            {
                if (Target() != null) return true;
                ScLog.Err("ItemActionRanged.getDirectionOffset not found (game update?) - NPC guns keep their perfect aim");
                return false;
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return Target(); }

            [HarmonyPostfix]
            public static void Postfix(ItemActionRanged __instance, ItemActionRanged.ItemActionDataRanged _actionData, Vector3 _forward, ref Vector3 __result)
            {
                try
                {
                    // launchers call it on every machine for the projectile: theirs is aimed once, in GetActionEffectsValues
                    if (__instance is ItemActionLauncher || _actionData == null || _actionData.invData == null) return;
                    EntityAlive e = _actionData.invData.holdingEntity;
                    if (!Applies(e)) return;
                    __result = Aim(e, _forward, __result);
                }
                catch (Exception ex)
                {
                    ScLog.Once("shooting-aim", ex);
                }
            }
        }

        /// <summary>A launcher's shot direction, worked out on the server and sent to every machine with the shot.</summary>
        [HarmonyPatch]
        public static class Patch_LauncherDirection
        {
            public static MethodBase Target()
            {
                return AccessTools.DeclaredMethod(typeof(ItemActionLauncher), "GetActionEffectsValues",
                    new[] { typeof(ItemActionData), typeof(Vector3).MakeByRefType(), typeof(Vector3).MakeByRefType() });
            }

            [HarmonyPrepare]
            public static bool Prepare()
            {
                if (Target() != null) return true;
                ScLog.Err("ItemActionLauncher.GetActionEffectsValues not found (game update?) - NPC bows keep their perfect aim");
                return false;
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return Target(); }

            [HarmonyPostfix]
            public static void Postfix(ItemActionData _actionData, ref Vector3 _direction)
            {
                try
                {
                    EntityAlive e = _actionData != null && _actionData.invData != null ? _actionData.invData.holdingEntity : null;
                    if (!Applies(e)) return;
                    _direction = AimLauncher(e, _direction);
                }
                catch (Exception ex)
                {
                    ScLog.Once("shooting-launcher", ex);
                }
            }
        }
    }
}
