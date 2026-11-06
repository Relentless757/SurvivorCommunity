using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// The hand of the community's 0-XNPCCore survivors, on top of 0-XNPCCore's own weapon state machine (its close-range
    /// melee swap, dry-gun failover and world-entry re-arm own the hand slot; the weapons task's planned ScArms switcher
    /// is retired - migration design M7.5). Two pieces:
    ///
    /// Overwhelmed (M7.5, the weapons task's W4/W6 decision). 0-XNPCCore swaps a gun for the melee weapon when the target
    /// is within 3 m and back at 5 m. A survivor that is overwhelmed - at least X enemies targeting it within
    /// OverwhelmedRadius (12 m), X = OverwhelmedMin + (Fighting stars - 1) / OverwhelmedStarsPerStep, at most
    /// OverwhelmedMax (2..5 for 1..7 stars; OverwhelmedStrangers, 3, for anyone who is not a member) - fights with the gun
    /// instead, as long as it has something to shoot: no swap to melee, and one already on melee goes back to the gun.
    /// 0-XNPCCore's BackupKeepDistance (weight 7 inside 4 m) then provides the "back off". Run from the X5 prefix on
    /// AvatarSdcsNpcController.SdcsCloseRangeTick (Patches_Xnpc), on 0-XNPCCore's private state bound by name (Bind).
    /// Hysteresis since the M11 fix 2 (MELEE_DIAG F3): with zombies switching their target between the player and the
    /// survivor the count sat at the threshold, and every flip between bat and gun cost a swap and, with Locked &amp; Loaded,
    /// a 2 s reload. The way back from melee to the gun needs the count to have held for OverwhelmedSustainSeconds (and at
    /// point-blank, the target within PointBlankDist, one enemy more); after such a return the gun stays for
    /// OverwhelmedGunSeconds whatever the count. A swarm still takes the gun: two zombies on a 1-star member for 1.5 s.
    ///
    /// The melee hold (the M11 fix 2, MELEE_DIAG F2). 0-XNPCCore leaves close melee once its target - the attack target,
    /// else the revenge target - is dead, gone or beyond 5 m, whoever else is within reach: a kill swapped the gun back in
    /// with the next zombie at 0.7 m, and SCore's 1 s attack-target lease from MoveToAttackTargetSDX.Stop running out read
    /// as "target-gone" with the zombie alive. While any awake enemy is within 3 m (0-XNPCCore's own enter distance), and
    /// for MeleeExitGraceSeconds after its target was lost, that exit is held off. Its real exit - the fight moved away -
    /// still comes, at most MeleeExitGraceSeconds later.
    ///
    /// The swap-dialog return (M7.4, the weapons task's W3 decision). A member handed a weapon through their inventory
    /// window and told to use it (the SdcsNPC dialog's weapon menu - SCore's SwapWeapon action - or Voice Control's
    /// SwapWeapon) keeps it: it replaces the pair's weapon of the same kind (registry loadout=, cvars, item list), the
    /// handed item leaves the window (it lives in the pair now), and the weapon it replaced comes back as a real item -
    /// into the base's linked storage, else the player's inventory, else a bag dropped at the player's feet. Only weapons
    /// the survivors can carry (the XnpcWeapons list) are kept; anything else the NPC simply holds from the window, as
    /// 0-XNPCCore does. Host only.
    ///
    /// The raiders' lead weapon (M10-D1: the old bands' mix, "30% early / 40% mid / 50% late rely on a ranged weapon; the
    /// rest fight melee first; everyone still carries both"). A ranged-lead raider is left to 0-XNPCCore exactly as a
    /// survivor is: its world-entry re-arm takes the gun, the close-range swap goes to the pair's melee weapon at 3 m and
    /// back at 5 m, the failover to melee when the gun is dry. A melee-lead raider keeps the melee weapon it spawns with
    /// (its re-arm is held off) and takes the gun only for a target that stays out of reach - farther than
    /// RaiderLeadRangedDist, or above it, or with no path to it - for RaiderLeadRangedSeconds, and only while the gun has
    /// rounds in its stores. The swap goes through 0-XNPCCore's own SdcsSwapToHand, which remembers the melee weapon it
    /// puts away: when 0-XNPCCore's close-range swap later takes the raider back to melee at 3 m, its exit at 5 m
    /// "restores" that melee weapon, so the raider stays on melee until the target is out of reach again. It also goes
    /// back to melee by itself - through 0-XNPCCore's own restore - when its target is gone (dead, despawned, lost) or
    /// back in reach within 5 m, so a melee-lead raider never stays a gun user. The melee weapon of every swap is the
    /// pair's (X6). Run from the X5 prefixes (Patches_Xnpc), on the private state Bind binds.
    /// </summary>
    public static class ScArms
    {
        // ------------------------------------------------------------------ overwhelmed ----

        /// <summary>0-XNPCCore's minimum time on close-range melee before it may leave it (a private constant, SdcsCloseMinHold).</summary>
        private const float XnpcCloseMinHold = 3f;
        /// <summary>The distance at which 0-XNPCCore swaps a gun for melee (a private constant, SdcsCloseEnterDist).</summary>
        private const float XnpcCloseEnterDist = 3f;
        /// <summary>The distance beyond which 0-XNPCCore leaves close-range melee (a private constant, SdcsCloseExitDist).</summary>
        private const float XnpcCloseExitDist = 5f;
        /// <summary>How often the attackers around one survivor are recounted (the close-range tick runs at 5 Hz).</summary>
        private const float CountSeconds = 0.5f;
        /// <summary>
        /// Point-blank (F3): with its target this close the survivor is in the middle of a swing exchange, where two seconds of
        /// swap and reload lose more than they gain, so the way back to the gun takes one enemy more than the threshold.
        /// </summary>
        private const float PointBlankDist = 1.5f;
        /// <summary>
        /// F2: 0-XNPCCore's exit for a lost target is held this long - SCore's attack-target lease is 1 s, a shove carries a
        /// zombie past 5 m for a moment, and after a kill the next zombie becomes the target within a decision or two.
        /// </summary>
        private const float MeleeExitGraceSeconds = 1.5f;

        private sealed class Watch
        {
            public float NextCount;
            public int Count;
            public float OverUntil;
            public float Seen;
            /// <summary>F3: since when the count has been at the threshold, and at the threshold + 1 (0 = not now); the last recount.</summary>
            public float OverSince, OverPlusSince, LastCount = -1000f;
            /// <summary>F3: after an overwhelmed return to the gun, 0-XNPCCore's melee swap is held off until then.</summary>
            public float GunUntil;
            /// <summary>F2: since when 0-XNPCCore has wanted to leave close melee (0 = it does not), in the bout that began at ExitBout.</summary>
            public float ExitWantSince, ExitBout;
        }

        private static readonly Dictionary<int, Watch> watch = new Dictionary<int, Watch>();
        private static readonly List<Entity> near = new List<Entity>();
        private static float nextPrune;

        // 0-XNPCCore's private close-range state (AvatarSdcsNpcController, 3.2.06), bound once by name
        private static bool bound, bindFailed;
        // plain FieldInfo, not FieldRefAccess: read only once a survivor is overwhelmed, so the reflection cost is nothing,
        // and nothing is emitted (the offline probe can check the bindings outside the game)
        private static FieldInfo closeMeleeField, closeSinceField, rememberedField;
        private static MethodInfo restoreHeld;
        /// <summary>0-XNPCCore's own swap (it remembers what it puts away); optional - null falls back to the same steps by hand.</summary>
        private static MethodInfo swapToHand;

        // swap-return counters for sc xnpc
        private static int swapsKept, swapsUnknown, returnedStorage, returnedInventory, returnedGround;
#if SC_V34
        /// <summary>3.4: dialog weapon rows Voice Control ran itself (kept from its MutateSwapWeapon, Patch_VoiceSwap).</summary>
        private static int swapsLeftToVc;
#endif

        public static void Reset()
        {
            watch.Clear();
            leads.Clear();
            near.Clear();
            nextPrune = 0f;
            leadSwitches = leadBacks = leadRearmsHeld = 0;
            swapsKept = swapsUnknown = returnedStorage = returnedInventory = returnedGround = 0;
#if SC_V34
            swapsLeftToVc = 0;
#endif
        }

        /// <summary>
        /// Binds 0-XNPCCore's private close-range swap state: the on-melee flag and its start time, the weapon it will
        /// restore, and the restore itself. Also run once at startup (ModApi), so a renamed member shows up in the log at
        /// once rather than in the first fight. A failure switches the overwhelmed rule off for the session.
        /// </summary>
        public static bool Bind()
        {
            if (bound) return true;
            if (bindFailed) return false;
            try
            {
                closeMeleeField = Field("sdcsCloseMelee", typeof(bool));
                closeSinceField = Field("sdcsCloseSinceTime", typeof(float));
                rememberedField = Field("sdcsHeldWeaponRemembered", typeof(string));
                restoreHeld = AccessTools.DeclaredMethod(typeof(AvatarSdcsNpcController), "SdcsRestoreHeldWeapon", Type.EmptyTypes);
                if (restoreHeld == null || restoreHeld.ReturnType != typeof(bool)) throw new MissingMethodException("AvatarSdcsNpcController", "SdcsRestoreHeldWeapon");
                swapToHand = AccessTools.DeclaredMethod(typeof(AvatarSdcsNpcController), "SdcsSwapToHand", new[] { typeof(string) });
                if (swapToHand != null && swapToHand.ReturnType != typeof(bool)) swapToHand = null;
                if (swapToHand == null) ScLog.Warn("0-XNPCCore's SdcsSwapToHand(string) not found - a melee-lead raider takes its gun through SCore's UpdateWeapon instead (0-XNPCCore update?)");
                bound = true;
                return true;
            }
            catch (Exception e)
            {
                bindFailed = true;
                ScLog.Warn("0-XNPCCore's close-range swap state could not be bound (" + e.Message + ") - the overwhelmed rule is off, and raiders all lead with their ranged weapon (0-XNPCCore update?)");
                return false;
            }
        }

        /// <summary>An instance field the controller itself declares, of exactly this type (else a throw: Bind fails loud).</summary>
        private static FieldInfo Field(string name, Type type)
        {
            FieldInfo f = AccessTools.DeclaredField(typeof(AvatarSdcsNpcController), name);
            if (f == null || f.IsStatic || f.FieldType != type) throw new MissingFieldException("AvatarSdcsNpcController", name);
            return f;
        }

        private static bool CloseMelee(AvatarSdcsNpcController c) { return (bool)closeMeleeField.GetValue(c); }
        private static float CloseSince(AvatarSdcsNpcController c) { return (float)closeSinceField.GetValue(c); }
        private static string Remembered(AvatarSdcsNpcController c) { return rememberedField.GetValue(c) as string; }

        /// <summary>
        /// The X5 prefix's close-range part (Patches_Xnpc.Patch_NoSwapWhileUsing, on SdcsCloseRangeTick): true runs
        /// 0-XNPCCore's own tick, false skips it. Not on melee, with a gun that has rounds and the target within 3 m: right
        /// after an overwhelmed return to the gun (OverwhelmedGunSeconds), or overwhelmed - skip (no swap to melee). On
        /// melee past 0-XNPCCore's own minimum hold: a swarm that has held (SwarmHeld) - restore the gun, as its exit would,
        /// and skip; else 0-XNPCCore's exit while an enemy is still within reach, or just after the target was lost - skip
        /// (HoldMelee). Anything else is 0-XNPCCore's own logic.
        /// </summary>
        public static bool CloseRange(AvatarSDCSController ctrl, EntityAlive e)
        {
            try
            {
                if (e == null || e.isEntityRemote || e.inventory == null || !ScXnpc.IsBody(e)) return true;
#if SC_V34
                if (ScXnpc.IsBanditRaider(e)) return true;   // round R1 (Q9): a bandit raider swaps up close by 0-XNPCCore's rule alone
#endif
                AvatarSdcsNpcController c = ctrl as AvatarSdcsNpcController;
                if (c == null || !Bind()) return true;
                float now = Time.time;
                int count, need;
                // the cheap tests first: the enemies are only counted when 0-XNPCCore is about to decide something
                if (!CloseMelee(c))
                {
                    // a guard on watch (base defence D3) keeps the gun up close: on a tower the melee swap only leads to a
                    // walk off the edge, and the gun still hits what climbs up
                    bool onWatch = ScWatch.Guarding(e.entityId);
                    if (!ScConfig.Overwhelmed && !onWatch) return true;
                    if (!TargetWithin(e, XnpcCloseEnterDist)) return true;                               // 0-XNPCCore would not swap yet
                    ItemValue held = e.inventory.holdingItemItemValue;
                    ItemClass gun = held != null && !held.IsEmpty() ? held.ItemClass : null;
                    if (ScAmmo.RangedAction(gun) == null || ScAmmo.Rounds(e, gun) <= 0) return true;   // no gun to keep: melee it is
                    if (onWatch)
                    {
                        Patches_Xnpc.Count("x5-watch-keep-gun");
                        return false;
                    }
                    if (!ScConfig.Overwhelmed) return true;
                    Watch gw;
                    if (watch.TryGetValue(e.entityId, out gw) && now < gw.GunUntil)
                    {
                        Patches_Xnpc.Count("x5-overwhelmed-gun-hold");
                        return false;
                    }
                    if (!IsOverwhelmed(e, out count, out need)) return true;
                    Patches_Xnpc.Count("x5-overwhelmed-keep-gun");
                    return false;
                }
                // on melee: with a gun to go back to, the count runs from the swap on, so a swarm that was there at once
                // can take the gun back as soon as 0-XNPCCore's own minimum hold is over
                string name = null;
                bool gunBack = ScConfig.Overwhelmed && RememberedGun(c, e, out name);
                if (gunBack) IsOverwhelmed(e, out count, out need);
                if (now - CloseSince(c) < XnpcCloseMinHold) return true;                                 // 0-XNPCCore holds the first 3 s itself
                if (gunBack && RestoreGun(c, e, now, name)) return false;
                return !HoldMelee(c, e, now);
            }
            catch (Exception ex)
            {
                ScLog.Once("arms-closerange", ex);
                return true;
            }
        }

        /// <summary>
        /// The gun 0-XNPCCore will restore when the survivor leaves close melee, when it is a ranged weapon with rounds in
        /// the survivor's stores. Reserve rounds only: SCore's UpdateWeapon builds the hand item fresh (an empty magazine),
        /// so a stored magazine alone would bring back a gun that fires nothing until 0-XNPCCore's failover swaps it out again.
        /// </summary>
        private static bool RememberedGun(AvatarSdcsNpcController c, EntityAlive e, out string name)
        {
            name = Remembered(c);
            ItemClass rem = string.IsNullOrEmpty(name) ? null : ItemClass.GetItemClass(name, true);
            return ScAmmo.RangedAction(rem) != null && ScAmmo.Rounds(e, rem) > 0;
        }

        /// <summary>
        /// F3: on melee, a swarm that has held takes the survivor back to its remembered gun (RememberedGun: name) -
        /// 0-XNPCCore's own exit, restore and clear the close-range state - and keeps it there OverwhelmedGunSeconds. True
        /// when it did.
        /// </summary>
        private static bool RestoreGun(AvatarSdcsNpcController c, EntityAlive e, float now, string name)
        {
            int count, need;
            bool pointBlank;
            if (!SwarmHeld(e, now, out count, out need, out pointBlank)) return false;
            restoreHeld.Invoke(c, null);
            closeMeleeField.SetValue(c, false);
            closeSinceField.SetValue(c, 0f);
            Watch st = WatchOf(e, now);
            st.GunUntil = now + ScConfig.OverwhelmedGunSeconds;
            st.ExitWantSince = 0f;
            Patches_Xnpc.Count("x5-overwhelmed-exit");
            ScLog.Verbose(ScCommunity.DisplayName(e) + " (id " + e.entityId + ") is overwhelmed (" + count + " enemies on them, " + need + (pointBlank ? " + 1 at point-blank" : "")
                          + " needed, for " + ScConfig.OverwhelmedSustainSeconds.ToString("0.#") + " s) - back to the " + name + ", kept for " + ScConfig.OverwhelmedGunSeconds.ToString("0.#") + " s");
            return true;
        }

        /// <summary>
        /// F3's swarm: the count has stayed at the threshold for OverwhelmedSustainSeconds - at the threshold + 1 with the
        /// target within PointBlankDist. Unlike IsOverwhelmed, no hold after the count drops: it decides a swap, not a stay.
        /// </summary>
        private static bool SwarmHeld(EntityAlive e, float now, out int count, out int need, out bool pointBlank)
        {
            IsOverwhelmed(e, out count, out need);
            Watch st = WatchOf(e, now);
            pointBlank = TargetWithin(e, PointBlankDist);
            float since = pointBlank ? st.OverPlusSince : st.OverSince;
            return since > 0f && now - since >= ScConfig.OverwhelmedSustainSeconds;
        }

        /// <summary>
        /// F2: true while 0-XNPCCore's close-melee exit is held off. Its rule (past the minimum hold) exits on a target that
        /// is dead, gone or beyond 5 m; that is held while any awake enemy is within 3 m, and for MeleeExitGraceSeconds after
        /// the wish to exit began in this bout (a new bout - a new enter time - starts the grace afresh).
        /// </summary>
        private static bool HoldMelee(AvatarSdcsNpcController c, EntityAlive e, float now)
        {
            Watch st = WatchOf(e, now);
            float bout = CloseSince(c);
            if (st.ExitBout != bout) { st.ExitBout = bout; st.ExitWantSince = 0f; }
            EntityAlive t = e.GetAttackTarget();
            if (t == null) t = e.GetRevengeTarget();
            bool wouldExit = t == null || t.IsDead() || (t.position - e.position).sqrMagnitude > XnpcCloseExitDist * XnpcCloseExitDist;
            if (!wouldExit) { st.ExitWantSince = 0f; return false; }
            bool first = st.ExitWantSince <= 0f;
            if (first) st.ExitWantSince = now;
            if (now - st.ExitWantSince >= MeleeExitGraceSeconds && !EnemyWithin(e, XnpcCloseEnterDist)) return false;   // the fight moved away: 0-XNPCCore exits
            if (first) Patches_Xnpc.Count("x5-melee-exit-held");                                                         // once per wish to exit
            return true;
        }

        /// <summary>
        /// Any awake, living enemy of this NPC (SCore's IsEnemy) within this distance and with no block between the two heads
        /// (SCore's CanShoot ray, which passes entities - you shoving it is not a wall): a zombie at the far side of a wall
        /// or on the floor below is not in reach, and must not keep the melee weapon in hand against a target farther off.
        /// </summary>
        private static bool EnemyWithin(EntityAlive e, float dist)
        {
            World w = e.world;
            if (w == null) return false;
            near.Clear();
            w.GetEntitiesInBounds(typeof(EntityAlive), new Bounds(e.position, new Vector3(2f * dist, 2f * dist, 2f * dist)), near);
            float d2 = dist * dist;
            bool found = false;
            for (int i = 0; i < near.Count && !found; i++)
            {
                EntityAlive a = near[i] as EntityAlive;
                if (a == null || a == e || a.IsDead() || a.IsSleeping || (a.position - e.position).sqrMagnitude > d2) continue;
                found = EntityTargetingUtilities.IsEnemy(e, a) && UAI.SCoreUtils.CanShoot(e, a, dist + 1f);
            }
            near.Clear();
            return found;
        }

        private static Watch WatchOf(EntityAlive e, float now)
        {
            Watch st;
            if (!watch.TryGetValue(e.entityId, out st)) watch[e.entityId] = st = new Watch();
            st.Seen = now;
            return st;
        }

        /// <summary>
        /// Are enough enemies on this survivor (recounted every half second; the state outlasts the count by
        /// OverwhelmedHoldSeconds, so a zombie stepping in and out of the radius does not flip the weapon twice a second)?
        /// Each recount also keeps F3's sustain clocks (since when the count has been at the threshold, and at it + 1).
        /// </summary>
        public static bool IsOverwhelmed(EntityAlive e, out int count, out int need)
        {
            need = Threshold(e);
            float now = Time.time;
            Watch st = WatchOf(e, now);
            if (now >= st.NextCount)
            {
                // the sustain clocks only run over recounts in a row: after a gap (not asked for a while - off melee, or no
                // gun to go back to) they start again, so an old swarm never counts as one that has held
                bool inRow = now - st.LastCount <= CountSeconds * 3f;
                st.LastCount = now;
                st.NextCount = now + CountSeconds;
                st.Count = Attackers(e);
                if (st.Count >= need)
                {
                    st.OverUntil = now + ScConfig.OverwhelmedHoldSeconds;
                    if (st.OverSince <= 0f || !inRow) st.OverSince = now;
                }
                else st.OverSince = 0f;
                if (st.Count >= need + 1) { if (st.OverPlusSince <= 0f || !inRow) st.OverPlusSince = now; }
                else st.OverPlusSince = 0f;
            }
            count = st.Count;
            if (now >= nextPrune) Prune(now);
            return st.Count >= need || now < st.OverUntil;
        }

        /// <summary>
        /// Living entities within OverwhelmedRadius whose target is this survivor: the attack target, else the revenge
        /// target - the same "who is it fighting" 0-XNPCCore's close-range tick reads (SdcsCurrentTargetDist).
        /// </summary>
        private static int Attackers(EntityAlive e)
        {
            World w = e.world;
            if (w == null) return 0;
            float r = ScConfig.OverwhelmedRadius;
            near.Clear();
            w.GetEntitiesInBounds(typeof(EntityAlive), new Bounds(e.position, new Vector3(2f * r, 2f * r, 2f * r)), near);
            int n = 0;
            float r2 = r * r;
            for (int i = 0; i < near.Count; i++)
            {
                EntityAlive a = near[i] as EntityAlive;
                if (a == null || a == e || a.IsDead() || (a.position - e.position).sqrMagnitude > r2) continue;
                EntityAlive t = a.GetAttackTarget();
                if (t == null) t = a.GetRevengeTarget();
                if (t == e) n++;
            }
            near.Clear();
            return n;
        }

        /// <summary>Its target (attack target, else revenge target, alive) within this distance - 0-XNPCCore's own reading.</summary>
        private static bool TargetWithin(EntityAlive e, float dist)
        {
            EntityAlive t = e.GetAttackTarget();
            if (t == null) t = e.GetRevengeTarget();
            return t != null && !t.IsDead() && (t.position - e.position).sqrMagnitude <= dist * dist;
        }

        /// <summary>X for this survivor: from the member's Fighting stars (a stand-in fights as the member), else OverwhelmedStrangers.</summary>
        public static int Threshold(EntityAlive e)
        {
            Member m = MemberOf(e);
            if (m == null) return ScConfig.OverwhelmedStrangers;
            int steps = (m.Stars(Skill.Fighting) - 1) / Math.Max(1, ScConfig.OverwhelmedStarsPerStep);
            return Mathf.Clamp(ScConfig.OverwhelmedMin + steps, ScConfig.OverwhelmedMin, ScConfig.OverwhelmedMax);
        }

        private static Member MemberOf(EntityAlive e)
        {
            if (e == null || !ScRegistry.Loaded) return null;
            Member m = ScRegistry.GetMember(e.entityId);
            if (m == null && e.Buffs != null && e.Buffs.HasCustomVar(ScRadio.StandInCvar)) m = ScRegistry.GetMember((int)e.Buffs.GetCustomVar(ScRadio.StandInCvar));
            return m;
        }

        private static void Prune(float now)
        {
            nextPrune = now + 30f;
            var gone = new List<int>();
            foreach (var kv in watch) if (now - kv.Value.Seen > 60f) gone.Add(kv.Key);
            foreach (int id in gone) watch.Remove(id);
            gone.Clear();
            foreach (var kv in leads) if (now - kv.Value.Seen > 60f) gone.Add(kv.Key);
            foreach (int id in gone) leads.Remove(id);
        }

        // ------------------------------------------------------------------ raiders' lead weapon (M10-D1) ----

        /// <summary>Within this the melee weapon reaches: "no path" only counts beyond it (a raider swinging needs none).</summary>
        private const float MeleeReach = 3f;
        /// <summary>A target this far above the raider's feet stands where a melee weapon cannot reach it (a roof, a ledge).</summary>
        private const float ReachAbove = 2.2f;
        /// <summary>The path ends this far short of the target (horizontally): it cannot be reached (vanilla's own unreachable test uses 1.2 m).</summary>
        private const float PathShortBy = 2f;
        /// <summary>"No path" counts only with a real move block of at least this long (or the target in sight): see OutOfReach.</summary>
        private const float BlockedSeconds = 0.5f;
        /// <summary>
        /// Back to melee with the target in reach within this distance - 0-XNPCCore's own close-range exit distance, so
        /// the two never fight: 3 m (its enter) and 5 m (this) against 8 m out of reach leave a dead band, no flapping.
        /// </summary>
        private const float BackToMeleeDist = 5f;
        /// <summary>How long the target must stay in reach (or be gone) before a melee-lead raider puts its gun away.</summary>
        private const float BackToMeleeSeconds = 1.5f;
        private const float TargetGoneSeconds = 2f;

        private sealed class LeadWatch
        {
            public float OutSince;
            public float BackSince;
            public float Seen;
            public string Why = "";
            public string BackWhy = "";
        }

        private static readonly Dictionary<int, LeadWatch> leads = new Dictionary<int, LeadWatch>();
        private static int leadSwitches, leadBacks, leadRearmsHeld;

        /// <summary>
        /// The X5 prefix on SdcsTryWorldEntryRearm: false (0-XNPCCore's re-arm is skipped) for a melee-lead raider, which
        /// keeps the melee weapon a new body holds. 0-XNPCCore marks the re-arm done before it calls it, so it is never
        /// retried. Only while the lead rule can work (Bind): otherwise the raider is left to 0-XNPCCore, ranged-lead.
        /// </summary>
        public static bool WorldEntryRearm(EntityAlive e)
        {
            try
            {
                if (e == null || e.isEntityRemote || !ScLoadout.IsMeleeLead(e) || !Bind()) return true;
                leadRearmsHeld++;
                Patches_Xnpc.Count("x5-raider-melee-lead-no-rearm");
                return false;
            }
            catch (Exception ex)
            {
                ScLog.Once("arms-lead-rearm", ex);
                return true;
            }
        }

        /// <summary>
        /// The X5 prefix's raider part on SdcsCloseRangeTick (5 Hz, before the overwhelmed rule), for a melee-lead raider
        /// outside 0-XNPCCore's close-range melee. On its pair's melee weapon: a target that has stayed out of reach for
        /// RaiderLeadRangedSeconds makes it take its ranged weapon, if that has rounds in its stores (SCore builds the hand
        /// item with an empty magazine; the reload comes from the bag). On its pair's ranged weapon (BackToMelee): a target
        /// gone for TargetGoneSeconds, or back in reach within BackToMeleeDist for BackToMeleeSeconds, puts it back on the
        /// melee weapon. True when it swapped: 0-XNPCCore's close-range tick is skipped for this round.
        /// </summary>
        public static bool RaiderLead(AvatarSDCSController ctrl, EntityAlive e)
        {
            try
            {
                if (e == null || e.isEntityRemote || e.inventory == null || !ScLoadout.IsMeleeLead(e)) return false;
                AvatarSdcsNpcController c = ctrl as AvatarSdcsNpcController;
                if (c == null || !Bind()) return false;
                float now = Time.time;
                LeadWatch w;
                if (!leads.TryGetValue(e.entityId, out w)) leads[e.entityId] = w = new LeadWatch();
                w.Seen = now;
                if (now >= nextPrune) Prune(now);
                Loadout l = ScLoadout.Current(e);
                ItemValue held = e.inventory.holdingItemItemValue;
                string heldName = held != null && !held.IsEmpty() && held.ItemClass != null ? held.ItemClass.GetItemName() : null;
                // never inside 0-XNPCCore's close-range melee (its exit decides that)
                if (l == null || !l.HasRanged || l.Melee == null || CloseMelee(c))
                {
                    w.OutSince = 0f;
                    w.BackSince = 0f;
                    return false;
                }
                EntityAlive t = e.GetAttackTarget();
                if (t == null) t = e.GetRevengeTarget();
                if (string.Equals(heldName, l.Ranged, StringComparison.OrdinalIgnoreCase)) { w.OutSince = 0f; return BackToMelee(c, e, l, t, w, now); }
                w.BackSince = 0f;
                if (!string.Equals(heldName, l.Melee, StringComparison.OrdinalIgnoreCase)) { w.OutSince = 0f; return false; }
                string why = t != null && !t.IsDead() ? OutOfReach(e, t) : null;
                if (why == null) { w.OutSince = 0f; w.Why = ""; return false; }
                w.Why = why;
                if (w.OutSince <= 0f) { w.OutSince = now; return false; }
                if (now - w.OutSince < ScConfig.RaiderLeadRangedSeconds) return false;
                ItemClass gun = ItemClass.GetItemClass(l.Ranged, true);
                if (ScAmmo.RangedAction(gun) == null || ScAmmo.Rounds(e, gun) <= 0) return false;   // nothing to shoot: melee it is
                w.OutSince = 0f;
                if (!SwapToHand(c, e, heldName, l.Ranged)) return false;
                leadSwitches++;
                Patches_Xnpc.Count("x5-raider-lead-to-ranged");
                ScLog.Verbose("melee-lead raider " + ScCommunity.DisplayName(e) + " (id " + e.entityId + ") takes its " + l.Ranged + ": target " + why);
                return true;
            }
            catch (Exception ex)
            {
                ScLog.Once("arms-lead", ex);
                return false;
            }
        }

        /// <summary>
        /// A melee-lead raider holding its pair's ranged weapon goes back to the melee weapon when its target has been gone
        /// (dead, despawned, lost) for TargetGoneSeconds, or has been back in reach (OutOfReach null) within
        /// BackToMeleeDist for BackToMeleeSeconds. Only when the gun is ours to put away: 0-XNPCCore remembers the pair's
        /// melee weapon (our own switch, SwapToHand) - then its own SdcsRestoreHeldWeapon brings it back and clears the
        /// memory - or remembers nothing (a raider that was saved with its gun in hand, or armed from the console) - then
        /// SCore's UpdateWeapon, which leaves the memory empty. Anything else 0-XNPCCore remembered is its own business.
        /// </summary>
        private static bool BackToMelee(AvatarSdcsNpcController c, EntityAlive e, Loadout l, EntityAlive t, LeadWatch w, float now)
        {
            string rem = Remembered(c);
            if (rem != null && !string.Equals(rem, l.Melee, StringComparison.OrdinalIgnoreCase)) { w.BackSince = 0f; return false; }
            string why = null;
            float wait = BackToMeleeSeconds;
            if (t == null || t.IsDead()) { why = "target gone"; wait = TargetGoneSeconds; }
            else if ((t.position - e.position).sqrMagnitude <= BackToMeleeDist * BackToMeleeDist && OutOfReach(e, t) == null)
            {
                // overwhelmed, the gun stays up close: that is the overwhelmed rule's call (CloseRange), as for a survivor
                int count, need;
                if (!(ScConfig.Overwhelmed && IsOverwhelmed(e, out count, out need))) why = "target in reach";
            }
            if (why == null) { w.BackSince = 0f; w.BackWhy = ""; return false; }
            w.BackWhy = why;
            if (w.BackSince <= 0f) { w.BackSince = now; return false; }
            if (now - w.BackSince < wait) return false;
            w.BackSince = 0f;
            bool done = rem != null ? (bool)restoreHeld.Invoke(c, null) : ScNpc.UpdateWeapon(e, l.Melee);
            if (!done) return false;
            leadBacks++;
            Patches_Xnpc.Count("x5-raider-lead-to-melee");
            ScLog.Verbose("melee-lead raider " + ScCommunity.DisplayName(e) + " (id " + e.entityId + ") back to its " + l.Melee + ": " + why);
            return true;
        }

        /// <summary>
        /// Why the target is out of the melee weapon's reach, or null: farther than RaiderLeadRangedDist; standing above
        /// the raider; or, beyond melee reach, no way there - the raider's path ends short of it (vanilla's own unreachable
        /// test, EntityMoveHelper.CalcIfUnreachablePos, which only the zombie AI runs), or there is no path and none is
        /// being planned while the raider sees the target or is really blocked. Not on a plain loss of sight: the approach
        /// tasks only ask for a path while CanSeeTarget scores (the Fable review of round 1), so a target round a corner has
        /// no path because nobody is pathing to it - that one is investigated (SCRaider), not shot at.
        /// </summary>
        private static string OutOfReach(EntityAlive e, EntityAlive t)
        {
            Vector3 d = t.position - e.position;
            float dist = d.magnitude;
            if (dist > ScConfig.RaiderLeadRangedDist) return "at " + dist.ToString("0.#") + " m";
            if (dist <= MeleeReach) return null;
            if (d.y > ReachAbove) return "above (" + d.y.ToString("0.#") + " m)";
            GamePath.PathNavigate nav = e.navigator;
            if (nav == null) return null;
            GamePath.PathEntity path = nav.getPath();
            if (path != null && !path.isFinished())
            {
                float gx = path.toPos.x - path.rawEndPos.x, gz = path.toPos.z - path.rawEndPos.z;
                if (gx * gx + gz * gz > PathShortBy * PathShortBy || path.toPos.y - path.rawEndPos.y > ReachAbove) return "unreachable (path ends short)";
                return null;
            }
            if (!nav.noPathAndNotPlanningOne()) return null;
            // the same vision test SCore's CanSeeTarget consideration uses, so "no path" means what the approach task saw
            bool blocked = e.moveHelper != null && e.moveHelper.BlockedTime > BlockedSeconds;
            return blocked || UAI.SCoreUtils.CanSee(e, t) ? "unreachable (no path)" : null;
        }

        /// <summary>
        /// 0-XNPCCore's SdcsSwapToHand: it remembers the held weapon (here the melee one) unless something is remembered
        /// already, then SCore's UpdateWeapon. Without the method the same two steps run by hand.
        /// </summary>
        private static bool SwapToHand(AvatarSdcsNpcController c, EntityAlive e, string heldName, string item)
        {
            if (swapToHand != null) return (bool)swapToHand.Invoke(c, new object[] { item });
            if (Remembered(c) == null && heldName != null) rememberedField.SetValue(c, heldName);
            return ScNpc.UpdateWeapon(e, item);
        }

        /// <summary>A raider's lead and the lead rule's state, for the console: "ranged-lead", or "melee-lead" with what it waits for.</summary>
        public static string LeadState(EntityAlive e)
        {
            if (e == null || e.Buffs == null || !ScXnpc.IsRaider(e)) return "lead -";
            int lead = (int)e.Buffs.GetCustomVar(ScLoadout.LeadCvar);
            if (lead != Loadout.LeadMelee) return "lead " + Loadout.LeadName(lead);
            LeadWatch w;
            if (!leads.TryGetValue(e.entityId, out w)) return "lead melee-lead";
            if (w.BackSince > 0f) return "lead melee-lead (on the gun, " + w.BackWhy + " for " + (Time.time - w.BackSince).ToString("0.#") + " s)";
            if (w.OutSince <= 0f) return "lead melee-lead (target in reach)";
            return "lead melee-lead (target " + w.Why + " for " + (Time.time - w.OutSince).ToString("0.#") + " of " + ScConfig.RaiderLeadRangedSeconds.ToString("0.#") + " s)";
        }

        // ------------------------------------------------------------------ the swap-dialog return ----

        public static MethodBase DialogSwapTarget()
        {
            return AccessTools.DeclaredMethod(typeof(DialogActionSwapWeapon), "PerformAction", new[] { typeof(EntityPlayer) });
        }

        /// <summary>Voice Control's swap (XNPCVoiceControl.Net.VCCommandRouter.MutateSwapWeapon, internal); null without Voice Control.</summary>
        public static MethodBase VcSwapTarget()
        {
            Type t = ScTypes.Find("XNPCVoiceControl.Net.VCCommandRouter");
            return t != null ? AccessTools.DeclaredMethod(t, "MutateSwapWeapon", new[] { typeof(EntityAlive), typeof(string) }) : null;
        }

        /// <summary>
        /// After the NPC was told to use an item (it is already in their hand, from the window): a member's handed weapon
        /// joins the pair and the one it replaces comes back as a real item. route is "dialog" or "voice" (3.4: also
        /// "any-weapon", 0-XNPCCore's AnyWeapon equip of a handed weapon - Patches_Xnpc X16), for the log.
        /// </summary>
        public static void OnSwap(EntityAlive npc, string itemName, EntityPlayer player, string route)
        {
            try
            {
                if (!ScCommunity.IsHost || npc == null || string.IsNullOrEmpty(itemName) || !ScRegistry.Loaded || !ScXnpc.IsBody(npc)) return;
                Member m = ScRegistry.GetMember(npc.entityId);
                if (m == null) return;                       // a stranger's or a prospect's hand stays 0-XNPCCore's business
                ItemClass ic = ItemClass.GetItemClass(itemName, true);
                if (ic == null) return;
                string token = ic.GetItemName();
                Loadout cur = ScLoadout.Current(npc);
                if (cur == null) return;
                if (string.Equals(cur.Ranged, token, StringComparison.OrdinalIgnoreCase) || string.Equals(cur.Melee, token, StringComparison.OrdinalIgnoreCase)) return;
                int slot = WindowSlot(npc, ic.Id);
                if (slot < 0) return;                        // not handed over through the window: nothing to keep
                // somebody has this window open (ScAmmo.IsWindowOpen): a remote player's copy, written back over every slot
                // when they close it, would put the weapon back in the window while it is also in the pair, and the host's
                // own grid is not told the weapon left - it is only held for now, as anything that is not kept
                if (ScAmmo.IsWindowOpen(npc.entityId))
                {
                    Patches_Xnpc.Count("window-swap-held");
                    ScLog.Warn(m.Name + ": could not keep the handed " + token + " (someone has their inventory window open - try again once it is closed)");
                    return;
                }
                if (!ScLoadout.Knows(token))
                {
                    swapsUnknown++;
                    ScLog.Out(m.Name + " holds " + token + " from the inventory window - not one of the weapons the survivors carry (XnpcWeapons), so it is not kept in their pair");
                    return;
                }
                Keep(npc, m.Name, token, slot, player, route);
            }
            catch (Exception e)
            {
                ScLog.Once("arms-swap", e);
            }
        }

        /// <summary>
        /// The handed weapon in this window slot joins the NPC's pair in place of the one of its kind (a melee-only NPC's
        /// first ranged weapon replaces nothing), and the replaced one and any mods on the handed one come back as real
        /// items. Shared by 0-XNPCCore's weapon menu (OnSwap, members) and SC's own "Use the weapons I gave you" (UseHanded).
        /// </summary>
        private static bool Keep(EntityAlive npc, string name, string token, int slot, EntityPlayer player, string route)
        {
            Loadout cur = ScLoadout.Current(npc);
            if (cur == null) return false;
            bool ranged = ScLoadout.IsRangedToken(token);
            string old = cur.Of(ranged);
            if (Loadout.IsNoRanged(old)) old = null;                     // a melee-only NPC's first ranged weapon
            ItemValue oldIv = old != null ? CarriedCopy(npc, old) : null;     // before Set refills the slots
            var change = new Loadout();
            change.Set(ranged, token);
            string why;
            // no starting stack for a handed gun: its ammo is what the player hands over, or a resupply at home
            if (!ScLoadout.Set(npc, change, out why, false))
            {
                ScLog.Warn(name + ": could not keep the handed " + token + " (" + why + ")");
                return false;
            }
            RetargetRemembered(npc, old, token);
            var back = new List<ItemStack>();
            TakeFromWindow(npc, slot, back);              // the handed item lives in the pair now; any mods on it come back
            if (oldIv != null) back.Insert(0, new ItemStack(oldIv, 1));
            string where = back.Count > 0 ? Return(back, player, npc) : null;
            swapsKept++;
            Loadout now = ScLoadout.Current(npc);
            ScLog.Out(name + " keeps the " + token + " (" + route + ") in place of the " + (old ?? "-") + (where != null ? ", returned to " + where : "")
                      + " - loadout now " + (now != null ? now.ToString() : "-"));
            if (player != null && old != null)
                ScCommunity.Tell(player, string.Format(Localization.Get("scSwapKept"), name, ItemLabel(token), ItemLabel(old), Localization.Get(WhereKey(where))), "");
            if (ScRegistry.GetMember(npc.entityId) != null || ScRegistry.GetProspect(npc.entityId) != null) ScRegistry.SaveAtomic();
            return true;
        }

        /// <summary>
        /// The weapon of this kind the player handed the NPC through its inventory window that it could carry in its pair:
        /// one of XnpcWeapons, not the one it carries already - the highest tier; on a tie an unmodded copy before a modded
        /// one, else the first. Null when there is none (a weapon the survivors never carry, such as IZY's dual pistols,
        /// cannot join a pair).
        /// </summary>
        public static string HandedWeapon(EntityAlive npc, bool ranged, out int slot)
        {
            slot = -1;
            if (npc == null || !ScXnpc.IsBody(npc) || !HarvestManager.Has(npc.entityId)) return null;
            ItemStack[] items = ScNpc.Items(npc);
            Loadout cur = ScLoadout.Current(npc);
            if (items == null || cur == null) return null;
            string best = null;
            int bestLevel = -1;
            for (int i = 0; i < items.Length; i++)
            {
                ItemStack s = items[i];
                if (s == null || s.IsEmpty() || s.itemValue == null || s.itemValue.ItemClass == null) continue;
                string token = s.itemValue.ItemClass.GetItemName();
                if (!ScLoadout.Knows(token) || ScLoadout.IsRangedToken(token) != ranged) continue;
                if (string.Equals(token, cur.Of(ranged), StringComparison.OrdinalIgnoreCase)) continue;
                int level = ScLoadout.LevelOf(token);
                if (level > bestLevel || (level == bestLevel && !s.itemValue.HasMods() && items[slot].itemValue.HasMods()))
                {
                    best = token;
                    bestLevel = level;
                    slot = i;
                }
            }
            return best;
        }

        /// <summary>
        /// The weapon menu's "Use the weapons I gave you" (Config/dialogs.xml): the handed weapon of each kind
        /// (HandedWeapon: a gun or bow, a melee weapon) joins the pair, and the ranged one, else the melee one, goes into
        /// the hand. 0-XNPCCore's own menu lists only vanilla weapons, so this is how IZY Classic's guns, and every other
        /// weapon the survivors carry, are handed over. One line for both kinds: that menu shows ten lines at most and
        /// 0-XNPCCore's has nine. It shows for the NPC's leader only (a follower, a hired companion), so no other check.
        /// </summary>
        public static void UseHanded(EntityAlive npc, EntityPlayer player)
        {
            try
            {
                if (!ScCommunity.IsHost || npc == null || !ScXnpc.IsBody(npc)) return;
                string name = ScCommunity.DisplayName(npc);
                int slot;
                if (HandedWeapon(npc, true, out slot) == null && HandedWeapon(npc, false, out slot) == null)
                {
                    ScCommunity.Tell(player, string.Format(Localization.Get("scUseHandedNone"), name), "ui_denied");
                    return;
                }
                if (ScAmmo.IsWindowOpen(npc.entityId))
                {
                    ScCommunity.Tell(player, string.Format(Localization.Get("scUseHandedWindowOpen"), name), "ui_denied");
                    return;
                }
                var kept = new List<string>();
                bool rangedKept = false;
                foreach (bool ranged in new[] { true, false })
                {
                    string token = HandedWeapon(npc, ranged, out slot);       // again: the first Keep took its item out of the window
                    if (token == null) continue;
                    if (!Keep(npc, name, token, slot, player, "use handed"))
                    {
                        ScCommunity.Tell(player, string.Format(Localization.Get("scUseHandedFailed"), name, ItemLabel(token)), "ui_denied");
                        continue;
                    }
                    kept.Add(ItemLabel(token));
                    if (ranged) rangedKept = true;
                }
                if (kept.Count == 0) return;
                string why;
                if (!ScLoadout.Arm(npc, rangedKept, out why)) ScLog.Warn(name + ": kept the handed weapon(s) but could not take one in hand (" + why + ")");
                ScCommunity.Tell(player, kept.Count > 1 ? string.Format(Localization.Get("scUseHandedNowBoth"), name, kept[0], kept[1])
                                                        : string.Format(Localization.Get("scUseHandedNow"), name, kept[0]), "");
            }
            catch (Exception e)
            {
                ScLog.Once("arms-use-handed", e);
            }
        }

        /// <summary>
        /// 0-XNPCCore remembers the weapon its close-range swap or failover took out of the hand and restores it later. If
        /// that is the weapon the member just gave back, the restore would ask SCore for an item it no longer finds and fall
        /// back to the melee weapon for good; it remembers the new one instead.
        /// </summary>
        private static void RetargetRemembered(EntityAlive npc, string old, string token)
        {
            AvatarSdcsNpcController c = npc.emodel != null ? npc.emodel.avatarController as AvatarSdcsNpcController : null;
            if (c == null || old == null || !Bind()) return;
            if (string.Equals(Remembered(c), old, StringComparison.OrdinalIgnoreCase)) rememberedField.SetValue(c, token);
        }

        /// <summary>The window slot holding this item, an unmodded one first (its mods then need not come back separately).</summary>
        private static int WindowSlot(EntityAlive npc, int type)
        {
            if (!HarvestManager.Has(npc.entityId)) return -1;
            ItemStack[] items = ScNpc.Items(npc);
            if (items == null) return -1;
            int modded = -1;
            for (int i = 0; i < items.Length; i++)
            {
                ItemStack s = items[i];
                if (s == null || s.IsEmpty() || s.itemValue == null || s.itemValue.type != type) continue;
                if (!s.itemValue.HasMods()) return i;
                if (modded < 0) modded = i;
            }
            return modded;
        }

        /// <summary>Takes one of the stack in this window slot; its mods (and cosmetic mods) go into back.</summary>
        private static void TakeFromWindow(EntityAlive npc, int slot, List<ItemStack> back)
        {
            ItemStack[] items = ScNpc.Items(npc);
            ItemStack s = items != null && slot >= 0 && slot < items.Length ? items[slot] : null;
            if (s == null || s.IsEmpty()) return;
            ItemValue iv = s.itemValue;
#if SC_V34
            // 3.4: the mod arrays are private; counted accessors instead
            for (int mi = 0; mi < iv.ModificationCount; mi++) { ItemValue mod = iv.GetModification(mi); if (mod != null && !mod.IsEmpty()) back.Add(new ItemStack(mod.Clone(), 1)); }
            for (int mi = 0; mi < iv.CosmeticModCount; mi++) { ItemValue mod = iv.GetCosmeticMod(mi); if (mod != null && !mod.IsEmpty()) back.Add(new ItemStack(mod.Clone(), 1)); }
#else
            if (iv.Modifications != null) foreach (ItemValue mod in iv.Modifications) if (mod != null && !mod.IsEmpty()) back.Add(new ItemStack(mod.Clone(), 1));
            if (iv.CosmeticMods != null) foreach (ItemValue mod in iv.CosmeticMods) if (mod != null && !mod.IsEmpty()) back.Add(new ItemStack(mod.Clone(), 1));
#endif
            ItemStack rest = s.Clone();
            rest.count--;
            ScNpc.SetItem(npc, slot, rest.count > 0 ? rest : ItemStack.Empty.Clone());
        }

        /// <summary>
        /// The replaced weapon as the NPC carried it: its copy in the toolbelt (SCore filled it from the item list, quality
        /// 1-6), else a fresh one the way SCore makes those copies.
        /// </summary>
        private static ItemValue CarriedCopy(EntityAlive npc, string item)
        {
            ItemClass ic = ItemClass.GetItemClass(item, true);
            if (ic == null) return null;
#if SC_V34
            ItemStack[] belt = npc.inventory != null ? npc.inventory.ItemGrid.items : null;     // 3.4: the toolbelt's grid (read only)
#else
            ItemStack[] belt = npc.inventory != null ? npc.inventory.GetSlots() : null;
#endif
            if (belt != null)
                foreach (ItemStack s in belt)
                    if (s != null && !s.IsEmpty() && s.itemValue != null && s.itemValue.type == ic.Id) { ItemValue v = s.itemValue.Clone(); v.Meta = 0; return v; }
            return ic.HasQuality ? new ItemValue(ic.Id, 1, 6, false, null, 1f) : new ItemValue(ic.Id);
        }

        /// <summary>
        /// Real items back to the player, each in order of availability: the base's linked storage (a box with room inside
        /// the home zone, loaded), the player's own inventory (the host's), else one bag at the player's feet (at the NPC
        /// without a player). Returns where the first item - the weapon - went: storage, inventory or ground.
        /// </summary>
        private static string Return(List<ItemStack> items, EntityPlayer player, EntityAlive npc)
        {
            string first = null;
            var ground = new List<ItemStack>();
            EntityPlayerLocal local = player as EntityPlayerLocal;
            foreach (ItemStack st in items)
            {
                string where;
                Vector3i box;
                if (ScStorage.DepositInZone(st, out box)) { where = "storage"; returnedStorage++; }
                else if (local != null && local.PlayerUI != null && local.PlayerUI.xui != null && local.PlayerUI.xui.PlayerInventory != null
                         && local.PlayerUI.xui.PlayerInventory.AddItem(st.Clone())) { where = "inventory"; returnedInventory++; }
                else { ground.Add(st); where = "ground"; returnedGround++; }
                if (first == null) first = where;
            }
            if (ground.Count > 0) DropBag(player != null ? player.GetDropPosition() : npc.position + Vector3.up, ground, npc);
            return first;
        }

        /// <summary>One backpack holding the items (the container SC drops a dead survivor's window in: it stays until looted).</summary>
        private static void DropBag(Vector3 at, List<ItemStack> items, EntityAlive npc)
        {
            string className = ScCommunity.ClassExists(EntityClass.FromString("BackpackNPC")) ? "BackpackNPC" : "Backpack";
            EntityItem pack = EntityFactory.CreateEntity(EntityClass.FromString(className), at) as EntityItem;
            if (pack == null)
            {
                // no bag class: loose items, which despawn like any drop
                foreach (ItemStack st in items) GameManager.Instance.ItemDropServer(st, at, Vector3.zero, -1, 60f);
                return;
            }
#if SC_V34
            // 3.4: a Bag is a grid (empty slots to begin with), filled through SetSlot (it clones), and the creation data carries
            // it as a blob - SCore 3.4's own backpack drop (EntityAliveSDX.SetDead). ToClient: 3.2's true
            var bag = new Bag(new Vector2i(Math.Max(8, items.Count), 1), XUiC_ItemStack.StackLocationTypes.LootContainer, null);
            for (int i = 0; i < items.Count; i++) bag.SetSlot(i, items[i]);
            var ecd = new EntityCreationData(pack, StreamModeWrite.ToClient) { entityName = Localization.Get(npc.EntityName), id = -1 };
            ecd.bagData = StreamUtils.ToBlob(pbw => bag.Write(pbw, StreamModeWrite.ToClient));
            GameManager.Instance.RequestToSpawnEntityServer(ecd);
#else
            var bag = new Bag(Math.Max(8, items.Count));      // empty slots to begin with (ItemStack.CreateArray)
            for (int i = 0; i < items.Count; i++) bag.items[i] = items[i].Clone();
            GameManager.Instance.RequestToSpawnEntityServer(new EntityCreationData(pack, true) { entityName = Localization.Get(npc.EntityName), id = -1, bag = bag });
#endif
            pack.OnEntityUnload();
        }

        private static string WhereKey(string where)
        {
            switch (where)
            {
                case "storage": return "scSwapToStorage";
                case "inventory": return "scSwapToInventory";
                default: return "scSwapToGround";
            }
        }

        private static string ItemLabel(string item)
        {
            ItemClass ic = ItemClass.GetItemClass(item, true);
            return ic != null ? ic.GetLocalizedItemName() : item;
        }

        /// <summary>SCore's dialog action: the SdcsNPC weapon menu's "Use X" (DialogActionSwapWeapon) on a member.</summary>
        [HarmonyPatch]
        public static class Patch_DialogSwap
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                try { return DialogSwapTarget() != null; }
                catch (Exception e) { ScLog.Once("arms-dialog-bind", e); return false; }
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return DialogSwapTarget(); }

            [HarmonyPostfix]
#if SC_V34
            public static void Postfix(BaseDialogAction __instance, EntityPlayer player, bool __runOriginal)
            {
                try
                {
                    if (__instance == null || !ScCommunity.IsHost) return;
                    // 3.4 (the risk list's item 21): Voice Control 3.4 takes 0-XNPCCore's own weapon rows (SdcsNPC's) from the
                    // dialog action (SwapWeaponStaticRowPatch skips it and sends its SwapWeapon command, which ends in
                    // MutateSwapWeapon): nothing was swapped yet when this runs, and Patch_VoiceSwap keeps the weapon once the
                    // swap is made - a client's row included, now that the swap runs on the host. Only a row the action ran
                    // itself (GenericNPC's) is kept from here.
                    if (!__runOriginal) { swapsLeftToVc++; return; }
#else
            public static void Postfix(BaseDialogAction __instance, EntityPlayer player)
            {
                try
                {
                    if (__instance == null || !ScCommunity.IsHost) return;
#endif
                    // the same respondent lookup the action itself makes
                    LocalPlayerUI ui = LocalPlayerUI.GetUIForPrimaryPlayer();
                    EntityAlive npc = ui != null && ui.xui != null && ui.xui.Dialog != null ? ui.xui.Dialog.Respondent as EntityAlive : null;
                    OnSwap(npc, __instance.ID, player, "dialog");
                }
                catch (Exception e)
                {
                    ScLog.Once("arms-dialog-swap", e);
                }
            }
        }

        /// <summary>
        /// Voice Control's SwapWeapon (a typed or spoken "use X"), which makes the same two calls as the dialog action; only
        /// the NPC's leader can give it (VCCommandRouter.ExecuteAuthoritative), so the returns go to the leader. Off without
        /// Voice Control (Prepare).
        /// </summary>
        [HarmonyPatch]
        public static class Patch_VoiceSwap
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                try { return VcSwapTarget() != null; }
                catch (Exception e) { ScLog.Once("arms-vc-bind", e); return false; }
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return VcSwapTarget(); }

            /// <summary>__0 the NPC, __1 the item name (bound by position: Voice Control is not referenced).</summary>
            [HarmonyPostfix]
            public static void Postfix(EntityAlive __0, string __1)
            {
                try
                {
                    if (__0 == null || !ScCommunity.IsHost) return;
                    OnSwap(__0, __1, EntityUtilities.GetLeaderOrOwner(__0.entityId) as EntityPlayer, "voice");
                }
                catch (Exception e)
                {
                    ScLog.Once("arms-voice-swap", e);
                }
            }
        }

        // ------------------------------------------------------------------ diagnostics ----

        public static string Report()
        {
#if SC_V34
            return "overwhelmed " + (ScConfig.Overwhelmed ? (bound ? "on" : bindFailed ? "OFF (0-XNPCCore state not bound)" : "on (not bound yet)") : "off")
                   + ", " + watch.Count + " survivor(s) watched; swap returns: " + swapsKept + " kept, " + swapsUnknown + " not a survivor weapon, returned to storage "
                   + returnedStorage + " / inventory " + returnedInventory + " / ground " + returnedGround
                   + "; dialog rows run by Voice Control: " + swapsLeftToVc
                   + "; melee-lead raiders: " + leadRearmsHeld + " re-arm(s) held off, " + leadSwitches + " switch(es) to the ranged weapon, " + leadBacks + " back to melee"
                   + (bound && swapToHand == null ? " (SdcsSwapToHand missing: by hand)" : "");
#else
            return "overwhelmed " + (ScConfig.Overwhelmed ? (bound ? "on" : bindFailed ? "OFF (0-XNPCCore state not bound)" : "on (not bound yet)") : "off")
                   + ", " + watch.Count + " survivor(s) watched; swap returns: " + swapsKept + " kept, " + swapsUnknown + " not a survivor weapon, returned to storage "
                   + returnedStorage + " / inventory " + returnedInventory + " / ground " + returnedGround
                   + "; melee-lead raiders: " + leadRearmsHeld + " re-arm(s) held off, " + leadSwitches + " switch(es) to the ranged weapon, " + leadBacks + " back to melee"
                   + (bound && swapToHand == null ? " (SdcsSwapToHand missing: by hand)" : "");
#endif
        }

        /// <summary>One survivor as the overwhelmed rule sees it, for sc xnpc &lt;id&gt;.</summary>
        public static string Describe(EntityAlive e)
        {
            if (e == null || !ScXnpc.IsBody(e)) return "not one of the community's 0-XNPCCore survivors";
            var sb = new StringBuilder();
            int count, need;
            bool over = IsOverwhelmed(e, out count, out need);
            sb.Append(count).Append(" enemy(ies) on them within ").Append(ScConfig.OverwhelmedRadius.ToString("0.#")).Append(" m, overwhelmed at ").Append(need)
              .Append(MemberOf(e) != null ? " (Fighting stars)" : " (stranger)").Append(": ").Append(over ? "OVERWHELMED" : "no");
            float now = Time.time;
            Watch st;
            if (watch.TryGetValue(e.entityId, out st))
            {
                // F3: how long the swarm has held (the way back to the gun needs OverwhelmedSustainSeconds), the gun hold
                sb.Append(" (at ").Append(need).Append(st.OverSince > 0f ? " for " + (now - st.OverSince).ToString("0.0") + " s" : ": no")
                  .Append(", at ").Append(need + 1).Append(st.OverPlusSince > 0f ? " for " + (now - st.OverPlusSince).ToString("0.0") + " s" : ": no")
                  .Append(" of ").Append(ScConfig.OverwhelmedSustainSeconds.ToString("0.0")).Append(" s needed)");
                if (now < st.GunUntil) sb.Append(" GUN HELD ").Append((st.GunUntil - now).ToString("0.0")).Append(" s more");
                if (st.ExitWantSince > 0f) sb.Append("; 0-XNPCCore wants to leave melee for ").Append((now - st.ExitWantSince).ToString("0.0")).Append(" s")
                                             .Append(EnemyWithin(e, XnpcCloseEnterDist) ? " (held: an enemy within 3 m)" : "");
            }
            AvatarSdcsNpcController c = e.emodel != null ? e.emodel.avatarController as AvatarSdcsNpcController : null;
            if (c != null && Bind())
                sb.Append("; 0-XNPCCore close melee=").Append(CloseMelee(c)).Append(CloseMelee(c) ? " for " + (now - CloseSince(c)).ToString("0.0") + " s" : "")
                  .Append(" remembered='").Append(Remembered(c) ?? "-").Append('\'');
            return sb.ToString();
        }
    }
}
