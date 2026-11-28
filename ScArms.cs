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
    ///
    /// The one posture's hand (round k4, ScPosture; research/combat/SC_VS_NPCCORE.md 4.1). A member's hand follows her posture
    /// through one rule (Hand) with one clock: MELEE takes her melee weapon (a gun holder's one swap), KITE draws her gun and
    /// holds 0-XNPCCore's 3 m swap (her back-offs and the shot instead; cornered and reloading or with no shot, R2's melee until
    /// room - never at a target she cannot swing at or a melee-unsafe one, and ended by 0-XNPCCore's own bout exit), HOLD
    /// keeps the gun up on watch, SHELTER and no fight leave the hand as it is. After any change of her hand - SC's or
    /// 0-XNPCCore's - SC swaps nothing for TeamSwapHoldSeconds; only a target that needs the gun draws it at once. A melee
    /// weapon in the MELEE posture rides in 0-XNPCCore's own close-melee bout, so its own exits judge the swing (a wall, bars,
    /// an unreachable target: the gun back, and the posture KITE against that target); only its exit for a target that is
    /// gone or beyond 5 m is held. Round tm1's MeleeFirst and AdoptBout, round n08's GunFirst and round tm2's no-swing clock,
    /// reach-gun hold and no-swing spot are folded into it. Strangers, enclave people and raiders keep the overwhelmed rule
    /// above (CloseRange). A gun order from the player (OrderedRangedCvar) is KITE, a melee order (OrderedMeleeCvar) MELEE.
    ///
    /// Round nc1 (0-XNPCCore 3.4.06 final). Its weapon toggle (Voice Control's SetWeaponPref, the cvar SdcsWeaponPref) is SC's weapon
    /// order (the user's D4: the toggle wins; Patch_VcWeaponPref, NoteOrderKind writes it back for SC's own orders); with the ranged
    /// preference 0-XNPCCore vetoes its own 3 m swap, so R2's melee is SC's swap then (KiteHand). Nothing is swapped while its quiet
    /// kill runs (StealthKillRuns), and the draw its quiet-kill order makes is no order (Patch_VcStealthKill).
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
            /// <summary>F3 (strangers, CloseRange): after an overwhelmed return to the gun, 0-XNPCCore's melee swap is held off until then.</summary>
            public float GunUntil;
            /// <summary>
            /// The re-review's R2 (the user: "Stay on melee until room"): a cornered reload put her on melee (x5-cornered-reload-melee
            /// at CorneredAt) and she stays on it while CorneredMeleeHolds - CorneredGun is the gun to go back to; RoomSince when her
            /// target last had room again (0: no room now).
            /// </summary>
            public bool CorneredMelee;
            public string CorneredGun;
            public float CorneredAt, RoomSince;
            /// <summary>F2 (strangers, CloseRange): since when 0-XNPCCore has wanted to leave close melee (0 = it does not), in the bout that began at ExitBout.</summary>
            public float ExitWantSince, ExitBout;
            /// <summary>
            /// The Fable review of round tm1 (finding 7): an equip that left the hand as it was backs the hand rule's swap to the
            /// melee weapon / to the gun off this body until then (EquipBackoffSeconds), so a refused equip is not retried at 5 Hz.
            /// </summary>
            public float MeleeBackoffUntil, GunBackoffUntil;
            /// <summary>Round wp1 (review WP1-m6): the last X5 tick saw the gun in her hand dry - DryGunSwap needs two in a row.</summary>
            public bool DrySeen;
            /// <summary>
            /// Round wp1 (review WP1-M1): the no-swing watchdog - the target watched (-1 none), since when with no swing, the swing
            /// marker then, the last tick, and whether this target's line was written.
            /// </summary>
            public int NsTarget = -1;
            public float NsSince, NsUse, NsTick;
            /// <summary>Round wp1 (re-check WP1R-n1): when her last line was written, and the targets it was written for (the last few).</summary>
            public float NsLineAt = -1000f;
            public readonly List<int> NsLoggedIds = new List<int>();
            /// <summary>
            /// Round k4, the Fable review of k3+k4 (F1): R2's "no shot" clocks, kept only over KITE's gun-in-hand ticks that follow
            /// one another (KiteTickAt: at most KiteTickGapSeconds apart, the same gun KiteGun, no hand change between). NoShotSince
            /// restarts on any such gap, on any change of her magazine (KiteMeta: a round fired or loaded), during a reload and
            /// while her target is farther than 3 m; CorneredSince is the start of her continuous cornered time (ScBackOff.NoPointFor
            /// for her target - a break of up to CorneredBreakSeconds since UncorneredAt is bridged), 0 when she is not cornered.
            /// </summary>
            public int KiteMeta = -1;
            public string KiteGun;
            public float KiteTickAt, NoShotSince, CorneredSince, UncorneredAt;
            /// <summary>
            /// F1: R2's melee - she was in 0-XNPCCore's bout during it (CorneredInBout: its own end of that bout ends R2), and the
            /// melee weapon CorneredHeld in her hand since CorneredHeldAt, with its last swing's time then (CorneredUse) and whether
            /// she has swung since (CorneredSwung: no swing for CorneredNoSwingSeconds ends R2). SkipNoted: the target R2 last
            /// skipped (logged once).
            /// </summary>
            public bool CorneredInBout, CorneredSwung;
            public string CorneredHeld;
            public float CorneredHeldAt, CorneredUse;
            /// <summary>The Fable re-review of k3+k4 (R4): R2's last tick here - a gap over KiteTickGapSeconds (a heal walk, a
            /// medical use) restarts CorneredHeldAt, so time she could not swing never counts as "no swing".</summary>
            public float CorneredTickAt;
            public int SkipNoted = -1;
            /// <summary>
            /// Round k5 (the final Fable check's C1): R2's room probe (ScBackOff.HasRoom) - when it runs next, and whether the last
            /// found no step back. On melee neither back-off scores, so nothing else renews "no room": without it the gun came back
            /// for a moment about every 6 s while she stayed truly pinned.
            /// </summary>
            public float RoomProbeAt;
            public bool RoomProbeNone;
            /// <summary>Round k5 (the Fable re-review's R2-rotation): after a no-swing end R2 rests until then, whatever the target.</summary>
            public float R2RestUntil;
            /// <summary>
            /// Round k5 (the Fable re-review's R3): since when R2's target has been lost (SCore's 1 s attack-target lease; 0: it is
            /// there), and whether that lapse is held this tick (0-XNPCCore's own tick is skipped, so its "target gone" exit waits).
            /// </summary>
            public float CorneredLostAt;
            public bool CorneredLapse;
            /// <summary>
            /// Round hr1: the last stand's hand (StandHand) - the stand these belong to (ScPosture's StandSince); a slow gun's loaded
            /// round fired (StandFired, from its magazine StandMeta); she was in 0-XNPCCore's bout (its own end of it is a refusal);
            /// melee refused member-wide until StandRefusedUntil (a bout exit or the no-swing window: the gun for the commitment, no
            /// per-target refusal, no 10 s rest), when and why; the no-swing clock (the melee weapon in hand since, its last swing).
            /// </summary>
            public float StandSince = -1f, StandRefusedUntil, StandRefusedAt = -100f, StandHeldAt, StandUse, StandTickAt;
            public bool StandFired, StandInBout, StandSwung;
            public int StandMeta = -1;
            public string StandHeld, StandRefusedWhy;
        }

        /// <summary>Round hr1: the last stand's weapon classes (ScPosture's View.StandClass): no gun with rounds, a close gun, a slow gun.</summary>
        internal const int StandNoGun = 0, StandClose = 1, StandSlow = 2;
        /// <summary>Round hr1: in a stand a close gun reloads only with no enemy within this; "an enemy in reach" for the commitment is within StandReach.</summary>
        private const float StandReloadClear = 2f, StandReach = 3f;
        /// <summary>Round hr1: the no-swing window's draw (the window is the larger of CorneredNoSwingSeconds and the draw plus two swings).</summary>
        private const float NoSwingDraw = 1f;

        /// <summary>The Fable review of round tm1 (finding 7): how long the hand rule waits after an equip that did not take.</summary>
        private const float EquipBackoffSeconds = 10f;
        /// <summary>
        /// R2: she has room again once ScBackOff.NoPointFor has not held for her target this long (no flapping); a swap to melee
        /// 0-XNPCCore had not made yet is waited for this long too.
        /// </summary>
        private const float CorneredRoomSeconds = 1f, CorneredSwapGrace = 1f;
        /// <summary>0-XNPCCore publishes its vanilla reload as this cvar (its RangedMeleeAttack is off meanwhile).</summary>
        private const string XnpcReloadingCvar = "SdcsReloading";
        /// <summary>
        /// Round k4: KITE holds 0-XNPCCore's 3 m swap in every fight it covers, not only an overwhelmed one - so a member with no
        /// room to back off whose loaded gun has not fired for this long with her target within 3 m (no line of fire: a crawler
        /// below her, a step in the way) gets R2's melee until room too. 3 s: past a bow's draw and a bolt action.
        /// </summary>
        private const float KiteNoShotSeconds = 3f;
        /// <summary>F1: KITE's gun-in-hand ticks further apart than this (0-XNPCCore's close-range tick runs every 0.2 s) restart R2's clocks.</summary>
        private const float KiteTickGapSeconds = 0.5f;
        /// <summary>F1: a break in her cornered time (ScBackOff.NoPointFor false) up to this long is bridged.</summary>
        private const float CorneredBreakSeconds = 0.5f;
        /// <summary>
        /// F1: R2's melee ends - and the swing counts as refused (KITE against that target) - when she has not swung for this long
        /// since the melee weapon came into her hand: a fence, bars or a block 0-XNPCCore's own checks let through.
        /// </summary>
        private const float CorneredNoSwingSeconds = 2.5f;
        /// <summary>Round k5 (C1): R2's room probe runs at most this often.</summary>
        private const float RoomProbeSeconds = 1f;
        /// <summary>Round k5 (R2-rotation): after a no-swing end R2 rests this long, whatever the target (3 or 4 zombies at a fence take turns).</summary>
        private const float R2RestSeconds = 10f;

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
        /// <summary>
        /// Round tm1 (the Fable review, finding 8): 0-XNPCCore's sdcsHandsOwnedByCapture - its own hand managers stand aside
        /// while a capture holds her hands, and SC's hand rule does too. Optional: null reads as "not held".
        /// </summary>
        private static FieldInfo captureField;
        /// <summary>
        /// Round k4: two optional reads of 0-XNPCCore's own swing verdicts - sdcsCloseWallExitTime (when its bout last ended for
        /// a wall: the reason in the log) and sdcsCloseMeleeRefused (why its tick refused to swap a gun holder to melee: "wall
        /// between", "unreachable", "wall re-enter", "not closing"). Either missing is logged once; the posture then learns of a
        /// refused swing from a bout's end alone. They replace round tm2's reflection on SdcsMeleeReach.s_episodes,
        /// SdcsMeleeReach.BlockKind, AttackGateRules.SwingAllowed and SdcsAimPoint.s_answers.
        /// </summary>
        private static FieldInfo wallExitField, closeRefusedField;

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
            apmOf.Clear();                             // round hr1
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
                BindTeamReads();
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

        /// <summary>
        /// Round tm1 (the Fable review), round k4: the optional reads - the capture flag (SC's hand rule stands aside while
        /// 0-XNPCCore's capture holds her hands) and its two swing verdicts (wallExitField, closeRefusedField). Each missing one
        /// is logged once and reads as nothing found (the rule works without it, a little blinder).
        /// </summary>
        private static void BindTeamReads()
        {
            try
            {
                captureField = AccessTools.DeclaredField(typeof(AvatarSdcsNpcController), "sdcsHandsOwnedByCapture");
                if (captureField != null && (captureField.IsStatic || captureField.FieldType != typeof(bool))) captureField = null;
                if (captureField == null) ScLog.Warn("0-XNPCCore's sdcsHandsOwnedByCapture not found - SC's hand rule cannot see a hand capture (0-XNPCCore update?)");
            }
            catch (Exception e) { captureField = null; ScLog.Once("arms-bind-capture", e); }
            wallExitField = OptionalField("sdcsCloseWallExitTime", typeof(float), "a bout that ended for a wall is logged without its reason");
            closeRefusedField = OptionalField("sdcsCloseMeleeRefused", typeof(string), "a gun holder's refused swap to melee is learned only from a bout's end");
        }

        /// <summary>Round k4: an optional instance field of the controller of exactly this type, or null with one warning.</summary>
        private static FieldInfo OptionalField(string name, Type type, string without)
        {
            try
            {
                FieldInfo f = AccessTools.DeclaredField(typeof(AvatarSdcsNpcController), name);
                if (f != null && !f.IsStatic && f.FieldType == type) return f;
                ScLog.Warn("0-XNPCCore's " + name + " not found - " + without + " (0-XNPCCore update?)");
            }
            catch (Exception e) { ScLog.Once("arms-bind-" + name, e); }
            return null;
        }

        /// <summary>Round tm1: 0-XNPCCore's capture holds her hands (its hand managers stand aside; so do SC's swap rules).</summary>
        private static bool HandsCaptured(AvatarSdcsNpcController c)
        {
            return captureField != null && (bool)captureField.GetValue(c);
        }

        private static bool CloseMelee(AvatarSdcsNpcController c) { return (bool)closeMeleeField.GetValue(c); }
        private static float CloseSince(AvatarSdcsNpcController c) { return (float)closeSinceField.GetValue(c); }
        private static string Remembered(AvatarSdcsNpcController c) { return rememberedField.GetValue(c) as string; }

        /// <summary>
        /// The X5 prefix's close-range part for a body with no posture - a stranger, an enclave's person, a raider (Hand runs
        /// the members' one hand rule): true runs 0-XNPCCore's own tick, false skips it. Not on melee, with a gun that has
        /// rounds and the target within 3 m: right after an overwhelmed return to the gun (OverwhelmedGunSeconds), or
        /// overwhelmed - skip (no swap to melee). On melee past 0-XNPCCore's own minimum hold: a swarm that has held (SwarmHeld)
        /// - restore the gun, as its exit would, and skip; else 0-XNPCCore's exit while an enemy is still within reach, or just
        /// after the target was lost - skip (HoldMelee). Anything else is 0-XNPCCore's own logic. Round k4: the members' parts
        /// (a guard's gun up close, the hurt member's gun, R2) moved into Hand.
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
                    if (!ScConfig.Overwhelmed) return true;
                    if (!TargetWithin(e, XnpcCloseEnterDist)) return true;                               // 0-XNPCCore would not swap yet
                    ItemValue held = e.inventory.holdingItemItemValue;
                    ItemClass gun = held != null && !held.IsEmpty() ? held.ItemClass : null;
                    if (ScAmmo.RangedAction(gun) == null || ScAmmo.Rounds(e, gun) <= 0) return true;   // no gun to keep: melee it is
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
            if (ScAmmo.RangedAction(rem) != null) return ScAmmo.Rounds(e, rem) > 0;
            // Round n08: 0-XNPCCore remembers nothing, or a melee weapon. Its close-range swap only remembers what it takes
            // out of the hand while nothing is remembered yet, so a stale melee name (an order, an earlier swap) survives a
            // swap FROM the gun - and the overwhelmed rule had nothing to go back to: "never switched to the pistol when
            // overwhelmed". A survivor's pair gun is the answer then (RestoreGun writes it in before the restore runs). Not
            // for a raider (its lead rule owns that), nor when the player ordered her melee weapon.
            if (ScXnpc.IsRaider(e) || OrderedMelee(e)) return false;
            Loadout l = ScLoadout.Current(e);
            if (l == null || !l.HasRanged) return false;
            ItemClass gun = ItemClass.GetItemClass(l.Ranged, true);
            if (ScAmmo.RangedAction(gun) == null || ScAmmo.Rounds(e, gun) <= 0) return false;
            name = l.Ranged;
            return true;
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
            if (!string.Equals(Remembered(c), name, StringComparison.OrdinalIgnoreCase)) rememberedField.SetValue(c, name);   // n08: the pair's gun
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

        /// <summary>A ranged weapon in her hand.</summary>
        private static bool HoldsRangedWeapon(EntityAlive e)
        {
            ItemValue hv = e != null && e.inventory != null ? e.inventory.holdingItemItemValue : null;
            return hv != null && !hv.IsEmpty() && hv.ItemClass != null && ScAmmo.RangedAction(hv.ItemClass) != null;
        }

        /// <summary>
        /// Are enough enemies on this survivor (recounted every half second; the state outlasts the count by
        /// OverwhelmedHoldSeconds, so a zombie stepping in and out of the radius does not flip the weapon twice a second)?
        /// Each recount also keeps F3's sustain clocks (since when the count has been at the threshold, and at it + 1).
        /// MUTATES the watch entry, so it must only be called from the X5 tick - never from a UAI consideration.
        /// </summary>
        public static bool IsOverwhelmed(EntityAlive e, out int count, out int need)
        {
            // round tm1 (I4, decision 7): in a matched fight - while she is melee-led - it takes TeamOverwhelmedExtra (1)
            // attacker more; an outmatched fight is unchanged
            need = Threshold(e) + (ScTeam.MeleeLed(e) ? ScConfig.TeamOverwhelmedExtra : 0);
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
        /// Round tm1 (the Fable review, finding 8): IsOverwhelmed as a pure read for the console (sc team, sc xnpc &lt;id&gt;) -
        /// the last recount and the state's hold, with no recount and no clock touched; ago is the recount's age in seconds
        /// (-1: never counted).
        /// </summary>
        public static bool PeekOverwhelmed(EntityAlive e, out int count, out int need, out float ago)
        {
            need = Threshold(e) + (ScTeam.MeleeLed(e) ? ScConfig.TeamOverwhelmedExtra : 0);
            count = 0;
            ago = -1f;
            Watch st;
            if (e == null || !watch.TryGetValue(e.entityId, out st)) return false;
            float now = Time.time;
            count = st.Count;
            if (st.LastCount > 0f) ago = now - st.LastCount;
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

        /// <summary>
        /// The Fable review (F7): the shot has her target's beat - no back-off from it now (ScBackOff.NoPointFor: no point inside
        /// the base, a back-off that gained nothing, a shooting beat between wide steps). Her attack target, else her revenge target.
        /// </summary>
        private static bool CorneredOnTarget(EntityAlive e)
        {
            EntityAlive t = e.GetAttackTarget();
            if (t == null) t = e.GetRevengeTarget();
            return t != null && !t.IsDead() && ScBackOff.NoPointFor(e, t);
        }

        /// <summary>
        /// The re-review's R2 (the user: "Stay on melee until room"), round k4 a sub-rule of KITE: after R2's trigger put her on
        /// melee (KiteHand), she stays on it - the hand rule draws no gun - while this is true. It ends - and the hand rule
        /// applies again - when her target is dead or gone; when she has had room for CorneredRoomSeconds (ScBackOff.NoPointFor
        /// not holding for her target), and not before the hand clock (TeamSwapHoldSeconds since her hand last changed) is over;
        /// when a gun is in her hand again (after CorneredSwapGrace: 0-XNPCCore's swap may come a tick late); when her posture is
        /// no longer KITE (Hand); and - the Fable review of k3+k4, F1 - when 0-XNPCCore ends its bout itself (CorneredBoutEnded),
        /// or when she has not swung for CorneredNoSwingSeconds since the melee weapon came into her hand (NoSwing: a fence or
        /// bars 0-XNPCCore let her into its bout through; the swing then counts as refused - KITE against that target, R2 skips
        /// it). F1 also stopped clearing 0-XNPCCore's remembered gun in its bout (round k1 did): its exits within 5 m are its
        /// verdicts that she cannot swing there (shoot-through, a wall, unreachable), and they must bring the gun back.
        /// Round k5: "no room" is renewed on melee by ScBackOff.HasRoom at most every RoomProbeSeconds (C1: nothing else renews
        /// it there, and the gun came back for a moment about every 6 s while she stayed pinned); a no-swing end rests R2 for
        /// R2RestSeconds whatever the target (R2-rotation); and a target lost for a moment (SCore's 1 s attack-target lease)
        /// with an enemy within 3 m is held MeleeExitGraceSeconds, 0-XNPCCore's tick skipped meanwhile (R3). X5 only.
        /// </summary>
        private static bool CorneredMeleeHolds(AvatarSdcsNpcController c, EntityAlive e, float now)
        {
            Watch st;
            if (e == null || !watch.TryGetValue(e.entityId, out st) || !st.CorneredMelee) return false;
            if (HoldsRangedWeapon(e))
            {
                if (now - st.CorneredAt < CorneredSwapGrace) return false;
                EndCornered(c, st, e, "a gun is in " + ScRetreat.Her(e) + " hand again");
                return false;
            }
            EntityAlive t = e.GetAttackTarget();
            if (t == null || t.IsDead()) t = e.GetRevengeTarget();
            st.CorneredLapse = false;
            if (t == null || t.IsDead())
            {
                // round k5 (the Fable re-review's R3): SCore leases the attack target for 1 s, so with the zombie at 1 m it can
                // read as gone for a moment - held MeleeExitGraceSeconds while an enemy is within 3 m (HoldMelee's rule for
                // strangers), with 0-XNPCCore's own tick skipped meanwhile (KiteHand), whose "target gone" exit would end R2
                bool firstLapse = st.CorneredLostAt <= 0f;
                if (firstLapse) st.CorneredLostAt = now;
                if (now - st.CorneredLostAt < MeleeExitGraceSeconds && EnemyWithin(e, XnpcCloseEnterDist))
                {
                    st.CorneredLapse = true;
                    if (firstLapse) Patches_Xnpc.Count("x5-cornered-lapse-held");          // once per lapse
                    return true;
                }
                EndCornered(c, st, e, ScRetreat.Her(e) + " target is dead or gone");
                return false;
            }
            st.CorneredLostAt = 0f;
            float apm;
            float window = NoSwingWindow(e, HeldItemName(e), out apm);       // round hr1: sized to the weapon (was 2.5 s for all)
            if (NoSwing(st, e, now, window))
            {
                string why = "no swing in " + window.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " s with " + ScRetreat.Her(e)
                             + " melee weapon (a fence, bars or a block in " + ScRetreat.Her(e) + " swing)";
                Patches_Xnpc.Count("x5-cornered-noswing-end");
                EndCornered(c, st, e, why);
                ScPosture.NoteRefused(e, t, why + " - R2's melee ended");
                st.R2RestUntil = now + R2RestSeconds;                     // round k5 (R2-rotation): no R2 for a while, whatever the target
                return false;
            }
            // round k5 (C1): "no room" is renewed by the back-offs' own verdicts (NoPointFor) or, on melee - where neither
            // back-off scores - by a probe at most once a second (ScBackOff.HasRoom: the same bounded pick)
            bool noRoom = ScBackOff.NoPointFor(e, t);
            if (!noRoom)
            {
                if (now >= st.RoomProbeAt)
                {
                    st.RoomProbeAt = now + RoomProbeSeconds;
                    string probeWhy;
                    st.RoomProbeNone = !ScBackOff.HasRoom(e, t, out probeWhy);
                    Patches_Xnpc.Count(st.RoomProbeNone ? "x5-cornered-probe-none" : "x5-cornered-probe-room");
                }
                noRoom = st.RoomProbeNone;
            }
            if (noRoom) st.RoomSince = 0f;
            else if (st.RoomSince <= 0f) st.RoomSince = now;
            bool swapHold = !ScPosture.HandClockAllows(e, now);       // round k4: the one hand clock (was the shared swap hold)
            if (!swapHold && st.RoomSince > 0f && now - st.RoomSince >= CorneredRoomSeconds)
            {
                EndCornered(c, st, e, "room to back off from " + t.EntityName + " (" + t.entityId + ") again");
                return false;
            }
            if (CloseMelee(c))
            {
                // F1: 0-XNPCCore's remembered gun stays (its exits are its verdicts) - only noted as the gun to go back to
                string rem = Remembered(c);
                if (!string.IsNullOrEmpty(rem) && ScAmmo.RangedAction(ItemClass.GetItemClass(rem, true)) != null) st.CorneredGun = rem;
            }
            return true;
        }

        /// <summary>
        /// F1: R2's melee weapon has not swung since it came into her hand, for CorneredNoSwingSeconds. A swing is a new
        /// lastUseTime on the held item's primary or power action (0-XNPCCore's power-swing patch reads the same marker);
        /// unreadable: never true. Keeps the clock in st. X5 only.
        /// </summary>
        private static bool NoSwing(Watch st, EntityAlive e, float now, float window)
        {
            float used;
            if (!LastSwing(e, out used)) return false;
            string held = HeldItemName(e);
            if (st.CorneredHeldAt <= 0f || !string.Equals(held, st.CorneredHeld, StringComparison.OrdinalIgnoreCase))
            {
                st.CorneredHeldAt = now;
                st.CorneredHeld = held;
                st.CorneredUse = used;
                st.CorneredSwung = false;
                st.CorneredTickAt = now;
                return false;
            }
            if (used != st.CorneredUse) st.CorneredSwung = true;
            if (now - st.CorneredTickAt > KiteTickGapSeconds) st.CorneredHeldAt = now;   // R4: the clock restarts after a gap
            st.CorneredTickAt = now;
            return !st.CorneredSwung && now - st.CorneredHeldAt >= window;            // round hr1: the weapon-sized window
        }

        /// <summary>
        /// Round hr1 (the user's decision 5): the no-swing window for this melee weapon - the larger of CorneredNoSwingSeconds (2.5 s)
        /// and a NoSwingDraw (1 s) draw plus two swings at the weapon's AttacksPerMinute (its own passive effect, the item's tags, as
        /// vanilla's melee reads it): about 4.5 s for a sledgehammer (34 a minute), 3.3 s for a bat (52). apm: the rate read. A read.
        /// </summary>
        internal static float NoSwingWindow(EntityAlive e, string melee, out float apm)
        {
            apm = 60f;
            ItemClass ic = string.IsNullOrEmpty(melee) ? null : ItemClass.GetItemClass(melee, true);
            if (ic == null) return CorneredNoSwingSeconds;
            if (!apmOf.TryGetValue(ic.Id, out apm))
            {
                try
                {
                    ItemValue held = e != null && e.inventory != null ? e.inventory.holdingItemItemValue : null;
                    ItemValue iv = held != null && !held.IsEmpty() && held.type == ic.Id ? held : new ItemValue(ic.Id);
                    FastTags<TagGroup.Global> tags = ItemActionAttack.PrimaryTag | ic.ItemTags;
                    apm = EffectManager.GetValue(PassiveEffects.AttacksPerMinute, iv, 60f, null, null, tags);
                }
                catch (Exception ex) { ScLog.Once("arms-noswing-apm", ex); apm = 60f; }
                if (apm < 1f) apm = 60f;
                apmOf[ic.Id] = apm;                                      // one read per weapon type (X5 asks at 5 Hz)
            }
            return Mathf.Max(CorneredNoSwingSeconds, NoSwingDraw + 2f * 60f / apm);
        }

        /// <summary>Round hr1: AttacksPerMinute per melee weapon type, read once (NoSwingWindow).</summary>
        private static readonly Dictionary<int, float> apmOf = new Dictionary<int, float>();

        /// <summary>
        /// Round hr1 (the last stand's weapon class, from her pair's gun): a CLOSE gun has a magazine of 5 or more (pistols, SMGs,
        /// rifles, shotguns - vanilla's MagazineSize on the item, its mods counted when she holds it); a SLOW gun 2 or less (bows,
        /// crossbows, pipe guns, the double barrel), or 3-4 with a Reload_time of 2.5 s or more. Reload_time decides only below 5:
        /// vanilla gives 2.6 s to every shotgun and 4.1 s to the SMG, which are close guns. StandNoGun with no ranged action. A read.
        /// </summary>
        internal static int GunClass(EntityAlive e, ItemClass gun, out int mag, out float reload)
        {
            mag = 0;
            reload = 0f;
            ItemActionRanged ra = ScAmmo.RangedAction(gun);
            if (ra == null) return StandNoGun;
            try
            {
                ItemValue held = e != null && e.inventory != null ? e.inventory.holdingItemItemValue : null;
                ItemValue iv = held != null && !held.IsEmpty() && held.type == gun.Id ? held : new ItemValue(gun.Id);
                mag = (int)EffectManager.GetValue(PassiveEffects.MagazineSize, iv, ra.BulletsPerMagazine);
            }
            catch (Exception ex) { ScLog.Once("arms-gun-class", ex); mag = ra.BulletsPerMagazine; }
            reload = ra.reloadingTime;
            if (mag >= 5) return StandClose;
            if (mag <= 2) return StandSlow;
            return reload >= 2.5f ? StandSlow : StandClose;
        }

        /// <summary>The latest lastUseTime of the held item's first two actions (the swing and the power swing). False when unreadable. A read.</summary>
        private static bool LastSwing(EntityAlive e, out float last)
        {
            last = 0f;
            ItemInventoryData d = e != null && e.inventory != null ? e.inventory.holdingItemData : null;
            if (d == null || d.actionData == null) return false;
            bool any = false;
            for (int i = 0; i < d.actionData.Count && i < 2; i++)
            {
                ItemActionData a = d.actionData[i];
                if (a == null) continue;
                if (!any || a.lastUseTime > last) last = a.lastUseTime;
                any = true;
            }
            return any;
        }

        /// <summary>
        /// F1: R2's melee in 0-XNPCCore's bout ends when 0-XNPCCore ends that bout itself - its own verdict (shoot-through, a
        /// wall, unreachable: the gun back; a target gone or beyond its exit distance). With her target alive within that
        /// distance it is a refusal of her swing: KITE against it (ScPosture), and R2 skips it. Before TidyCornered. X5 only.
        /// </summary>
        private static void CorneredBoutEnded(AvatarSdcsNpcController c, Watch st, EntityAlive e, bool bout)
        {
            if (!st.CorneredMelee) return;
            if (bout) { st.CorneredInBout = true; return; }
            if (!st.CorneredInBout) return;
            EntityAlive t = CurrentTarget(e);
            bool near = t != null && (t.position - e.position).sqrMagnitude <= XnpcCloseExitDist * XnpcCloseExitDist;
            string why = near ? BoutExitWhy(c, e, t) : t == null ? "the target is dead or gone" : "the target is beyond " + XnpcCloseExitDist.ToString("0") + " m";
            Patches_Xnpc.Count("x5-cornered-bout-ended");
            EndCornered(c, st, e, "0-XNPCCore ended " + ScRetreat.Her(e) + " bout itself - " + why);
            if (near) ScPosture.NoteRefused(e, t, why + " - R2's melee ended");
        }

        /// <summary>
        /// F1 (the Fable review of k3+k4): why R2 must not swap her to melee at t, else null - a swing at it refused before
        /// (ScPosture.RefusedWhy: 0-XNPCCore's refusal exits and its unreachable mark, R2's own "no swing"), 0-XNPCCore's last
        /// refusal of its swap ("wall between", "unreachable", "wall re-enter"), a melee-unsafe target, or (R5) an armed one -
        /// and round k5 (R2-rotation): R2RestSeconds after a no-swing end, any target (a dry gun at a fence with three or four
        /// zombies taking turns started R2 again on each). A read.
        /// </summary>
        private static string R2Skip(AvatarSdcsNpcController c, EntityAlive e, EntityAlive t)
        {
            if (t == null) return "no target";
            Watch rw;
            float now = Time.time;
            if (watch.TryGetValue(e.entityId, out rw) && now < rw.R2RestUntil)
                return "R2 rests " + (rw.R2RestUntil - now).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " s more after a swing that never came (round k5)";
            string r = ScPosture.RefusedWhy(e, t);
            if (r != null) return r;
            r = SwapRefusal(c);
            if (r != null) return "0-XNPCCore refused " + ScRetreat.Her(e) + " swap to melee (" + r + ")";
            if (ScTeam.MeleeUnsafe(t)) return "melee-unsafe";
            if (ScTeam.Armed(t)) return "an armed enemy";        // R5: as ScPosture's urgent draw and AllowsApproach
            return null;
        }

        /// <summary>F1: R2's two clocks - her continuous cornered time and no shot - over KITE's gun-in-hand ticks only (see Watch). X5 only.</summary>
        private static void KiteClocks(Watch st, EntityAlive e, ItemValue held, ItemClass hg, bool close, bool reloading, bool cornered, float now)
        {
            string gun = hg != null ? hg.GetItemName() : null;
            int meta = held != null ? held.Meta : -1;
            // a gap: the last gun-in-hand tick of KITE too long ago (MELEE, the heal walk, a medical use, a bout, another posture,
            // a fresh draw), another gun, or a change of her hand since (SC's or 0-XNPCCore's)
            bool gap = st.KiteTickAt <= 0f || now - st.KiteTickAt > KiteTickGapSeconds || !string.Equals(gun, st.KiteGun, StringComparison.OrdinalIgnoreCase)
                       || ScPosture.HandChangedAt(e) > st.KiteTickAt;
            if (gap) { st.CorneredSince = 0f; st.UncorneredAt = 0f; }
            if (cornered)
            {
                if (st.CorneredSince <= 0f) st.CorneredSince = now;
                st.UncorneredAt = 0f;
            }
            else if (st.CorneredSince > 0f)
            {
                if (st.UncorneredAt <= 0f) st.UncorneredAt = now;
                else if (now - st.UncorneredAt > CorneredBreakSeconds) { st.CorneredSince = 0f; st.UncorneredAt = 0f; }
            }
            if (gap || meta != st.KiteMeta || reloading || !close) st.NoShotSince = now;
            st.KiteTickAt = now;
            st.KiteGun = gun;
            st.KiteMeta = meta;
        }

        /// <summary>
        /// R2: the cornered melee ends - in 0-XNPCCore's bout her gun goes back in as its remembered weapon (when none is), so its
        /// exit and RestoreGun bring it back by the normal rules; counted x5-cornered-melee-end.
        /// </summary>
        private static void EndCornered(AvatarSdcsNpcController c, Watch st, EntityAlive e, string why)
        {
            st.CorneredMelee = false;
            st.RoomSince = 0f;
            st.CorneredInBout = false;
            st.CorneredSwung = false;
            st.CorneredHeld = null;
            st.CorneredHeldAt = 0f;
            st.CorneredTickAt = 0f;
            st.RoomProbeAt = 0f;
            st.RoomProbeNone = false;
            st.CorneredLostAt = 0f;
            st.CorneredLapse = false;
            st.NoShotSince = Time.time;                 // F1: another "no shot" trigger needs KiteNoShotSeconds again
            if (c != null && CloseMelee(c) && string.IsNullOrEmpty(Remembered(c)) && !string.IsNullOrEmpty(st.CorneredGun))
                rememberedField.SetValue(c, st.CorneredGun);
            Patches_Xnpc.Count("x5-cornered-melee-end");
            ScLog.Verbose(ScCommunity.DisplayName(e) + " (id " + e.entityId + ") is no longer cornered on melee: " + why + " - the hand rule again (the "
                          + (st.CorneredGun ?? "gun") + " by it; the re-review, R2)");
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

        // ------------------------------------------------------------------ the player's weapon orders (rounds n08, tm1) ----

        /// <summary>
        /// Saved cvar: 1 while the player's last weapon order for this body (the weapon menu, or "use your ..." by voice)
        /// named a MELEE weapon. Gun first, and the overwhelmed rule's way back to the pair's gun, stand aside for it; an
        /// order of the gun clears it.
        /// </summary>
        public const string OrderedMeleeCvar = "sc_order_melee";
        /// <summary>
        /// Round tm1 (I8, decision 9): saved cvar, 1 while the player's last weapon order for this body named her GUN or bow.
        /// The team rule stands aside for her (she is never melee-led: ScTeam); an order of her melee weapon clears it, and so
        /// does "sc team &lt;id&gt; auto".
        /// </summary>
        public const string OrderedRangedCvar = "sc_order_ranged";

        /// <summary>
        /// Round nc1 (D4): 0-XNPCCore 3.4.06 final's weapon preference, written by Voice Control's weapon toggle (the order wheel's
        /// "Melee -> Ranged" / "Ranged -> Melee", its SetWeaponPref: 1 melee, 2 ranged, 0 none) and read by 0-XNPCCore's close-range
        /// swap (ranged: no swap to melee up close), its dry failover, its world-entry step and its AnyWeapon pick
        /// (WeaponPreferenceRules). The user's decision D4: the toggle WINS - it is an SC weapon order exactly like ordering her gun
        /// or her melee weapon by talking, so SC's orders and the toggle are kept in sync both ways: the toggle writes SC's two
        /// order cvars (Patch_VcWeaponPref), SC's orders write this one (NoteOrderKind), "sc team &lt;id&gt; auto" clears all three
        /// (ScTeam.ClearOrders), and OrderedMelee / OrderedRanged read it when neither of SC's cvars is set (a toggle given before
        /// this round). SC's no-ammo and safety rules still apply to it as to any order (ScPosture.Want, R2 below).
        /// </summary>
        public const string WeaponPrefCvar = "SdcsWeaponPref";
        private const int PrefMelee = 1, PrefRanged = 2;
        /// <summary>Round nc1 (re-check): under a Stay, the distance at which a reloading member with a ranged preference counts as cornered.</summary>
        private const float StayCorneredDist = 1.5f;

        /// <summary>Round nc1: 0-XNPCCore's weapon preference (WeaponPrefCvar): 1 melee, 2 ranged, else 0 (none). A read.</summary>
        internal static int PrefKind(EntityAlive e)
        {
            if (e == null || e.Buffs == null) return 0;
            int k = Mathf.RoundToInt(e.Buffs.GetCustomVar(WeaponPrefCvar));
            return k == PrefMelee || k == PrefRanged ? k : 0;
        }

        public static bool OrderedMelee(EntityAlive e)
        {
            if (e == null || e.Buffs == null) return false;
            if (e.Buffs.GetCustomVar(OrderedMeleeCvar) >= 0.5f) return true;
            return e.Buffs.GetCustomVar(OrderedRangedCvar) < 0.5f && PrefKind(e) == PrefMelee;   // round nc1 (D4): the toggle alone
        }

        /// <summary>Round tm1: the player's last weapon order for her named her gun or bow (OrderedRangedCvar; round nc1: or the toggle's ranged).</summary>
        public static bool OrderedRanged(EntityAlive e)
        {
            if (e == null || e.Buffs == null) return false;
            if (e.Buffs.GetCustomVar(OrderedRangedCvar) >= 0.5f) return true;
            return e.Buffs.GetCustomVar(OrderedMeleeCvar) < 0.5f && PrefKind(e) == PrefRanged;   // round nc1 (D4): the toggle alone
        }

        /// <summary>
        /// A weapon order from the player (route "dialog" or "voice"; never "any-weapon", 0-XNPCCore's own pick-up of a newly
        /// handed weapon): a melee weapon sets OrderedMeleeCvar and clears OrderedRangedCvar, a gun or bow the other way round
        /// (round tm1: before it a gun order only cleared the melee order). Round nc1: not inside Voice Control's quiet-kill order
        /// or its weapon toggle (vcOrderScope) - the quiet kill's draw is no order, and the toggle makes its own (Patch_VcWeaponPref).
        /// </summary>
        private static void NoteOrder(EntityAlive npc, string itemName, string route)
        {
            if (npc == null || npc.Buffs == null || route == "any-weapon") return;
            string family = ScLoadout.FamilyOf(itemName);
            if (family == null) return;
            NoteOrderKind(npc, family != "melee");
        }

        /// <summary>
        /// The player's weapon order for her: her gun or bow (ranged), else her melee weapon - SC's two order cvars and, since round
        /// nc1 (D4), 0-XNPCCore's weapon preference with them (2 ranged, 1 melee), so its own rules agree with the order: a gun order
        /// is its "ranged" (no 3 m swap of its own - R2's melee is then SC's swap, KiteHand), a melee order its "melee" (no gun
        /// re-armed as she comes into the world).
        /// </summary>
        internal static void NoteOrderKind(EntityAlive npc, bool ranged)
        {
            if (npc == null || npc.Buffs == null) return;
            if (!ranged)
            {
                npc.Buffs.SetCustomVar(OrderedMeleeCvar, 1f);
                if (npc.Buffs.HasCustomVar(OrderedRangedCvar)) npc.Buffs.RemoveCustomVar(OrderedRangedCvar);
            }
            else
            {
                if (npc.Buffs.HasCustomVar(OrderedMeleeCvar)) npc.Buffs.RemoveCustomVar(OrderedMeleeCvar);
                npc.Buffs.SetCustomVar(OrderedRangedCvar, 1f);
            }
            SetPref(npc, ranged ? PrefRanged : PrefMelee);
        }

        /// <summary>
        /// Round nc1: 0-XNPCCore's weapon preference set (0 clears it), the way Voice Control's toggle writes it (synced and sent to
        /// the clients; with forceSendToClients the packet goes even for an unchanged value - harmless).
        /// </summary>
        internal static void SetPref(EntityAlive npc, int kind)
        {
            if (npc == null || npc.Buffs == null) return;
            if (kind == 0 && !npc.Buffs.HasCustomVar(WeaponPrefCvar)) return;
            npc.Buffs.SetCustomVar(WeaponPrefCvar, kind, true, CVarOperation.set, true);
        }

        /// <summary>
        /// Round nc1 (D4, ScLoadout.AfterPostInit): a body loaded with a weapon order of SC's gets the matching weapon preference,
        /// so 0-XNPCCore's rules agree with that order too (the sync both ways). The review's finding 5: SC's order also replaces
        /// a different preference left from before this round (a melee order with an old ranged toggle would leave 0-XNPCCore's
        /// veto keeping her gun up close); with no SC order a preference is left alone.
        /// </summary>
        internal static void SyncOrderPref(EntityAlive e)
        {
            if (e == null || e.Buffs == null) return;
            int want = e.Buffs.GetCustomVar(OrderedRangedCvar) >= 0.5f ? PrefRanged : e.Buffs.GetCustomVar(OrderedMeleeCvar) >= 0.5f ? PrefMelee : 0;
            if (want != 0 && PrefKind(e) != want) SetPref(e, want);
        }

        /// <summary>
        /// Round nc1 (items 6 and D4): above 0 while Voice Control's MutateStealthKill or MutateSetWeaponPref runs - the swap either
        /// makes through MutateSwapWeapon reaches Patch_VoiceSwap / OnSwap, where it must not count as a "use X" order. Main
        /// thread (Voice Control's commands run there), a depth for safety.
        /// </summary>
        private static int vcOrderScope;

        // ------------------------------------------------------------------ the one hand rule (round k4) ----

        /// <summary>
        /// Round k4 (research/combat/SC_VS_NPCCORE.md 4.1; the X5 prefix after ScPosture.Update): true runs 0-XNPCCore's own
        /// close-range tick, false skips it. A member's hand follows her posture (ScPosture) with one clock - after any change of
        /// her hand, SC's or 0-XNPCCore's, SC swaps nothing for TeamSwapHoldSeconds, and nothing while a switch of stance waits
        /// for the dwell. Every other body (a stranger, an enclave's person, a raider) keeps the overwhelmed rule (CloseRange).
        /// Never while 0-XNPCCore's capture holds her hands or a job's tool is in them.
        ///   MELEE (INVESTIGATE with the melee stance): the gun in hand with her target farther than 0-XNPCCore's 3 m - SC takes
        ///     her melee weapon straight into 0-XNPCCore's bout; within 3 m its own swap decides (it enters a bout, or refuses:
        ///     then ScPosture reads its refusal and she keeps the gun, KITE against that target). A melee weapon outside a bout
        ///     joins one with her target within 3 m. In a bout its refusal exits run (a wall, bars, unreachable: the gun back,
        ///     KITE against that target); its exit for a target that is gone or beyond 5 m is held - she keeps the melee weapon
        ///     (was round tm1's AdoptBout and the M11 fix 2's HoldMelee).
        ///   KITE (INVESTIGATE with the gun stance): a melee weapon in hand - her gun (at once for a target that needs it);
        ///     0-XNPCCore's 3 m swap is held while her gun has rounds - her back-offs and the shot instead - except R2: no room to
        ///     back off while reloading, or KiteNoShotSeconds of continuous cornered time with no shot (the hand clock allowing) -
        ///     its swap to melee runs and she stays on melee until there is room (CorneredMeleeHolds); not at a target she cannot
        ///     swing at or a melee-unsafe one (R2Skip), and 0-XNPCCore's own bout exit or no swing ends it (F1).
        ///     A runner on a run (NativeClose) keeps 0-XNPCCore's 3 m swap and draws only for a target beyond 5 m (round n08).
        ///   HOLD: on watch the gun stays up close (no 3 m swap); a melee weapon goes for the gun for a target beyond 5 m.
        ///   SHELTER: the hand as it is. No fight: the hand as it is - a bout whose fight is over is closed, the melee weapon kept.
        /// </summary>
        public static bool Hand(AvatarSDCSController ctrl, EntityAlive e)
        {
            try
            {
                if (e == null || e.isEntityRemote || e.inventory == null || !ScXnpc.IsBody(e)) return true;
#if SC_V34
                if (ScXnpc.IsBanditRaider(e)) return true;
                // round nc1 (the audit, item 5): while 0-XNPCCore's quiet kill runs (its stalk and strike) SC swaps nothing - not the
                // posture's weapon, not the overwhelmed rule's gun; 0-XNPCCore's own close-range tick runs as it would without SC
                if (StealthKillRuns(e)) { Patches_Xnpc.Count("x5-stealth-no-swap"); return true; }
#endif
                ScPosture.View pv;
                if (ScXnpc.IsRaider(e) || MemberOf(e) == null || !ScPosture.Of(e, out pv)) return CloseRange(ctrl, e);
                AvatarSdcsNpcController c = ctrl as AvatarSdcsNpcController;
                if (c == null || !Bind() || HandsCaptured(c) || UAI.UAITaskHarvestSdcs.HoldsJobTool(e.entityId)) return true;
                float now = Time.time;
                if (now >= nextPrune) Prune(now);
                Watch st = WatchOf(e, now);
                bool bout = CloseMelee(c);
                NoSwingWatch(c, e, st, pv, bout, now);                               // round wp1 (review WP1-M1): a read, logs once per target
                CorneredBoutEnded(c, st, e, bout);                                   // F1: 0-XNPCCore's own end of its bout ends R2
                if (!bout) TidyCornered(c, st, e, now);
                // round hr1: the last stand owns her hand (its weapon class, its commitment, its no-swing window) - not on watch or a
                // Stay (HOLD), and not against your weapon order (the posture's rules keep it)
                if (pv.Stand && pv.P != Posture.Hold && pv.P != Posture.None && !OrderedMelee(e) && !OrderedRanged(e)) return StandHand(c, e, st, pv, bout, now);
                // R2 is a KITE sub-rule: another posture ends a cornered melee (MELEE wants that weapon anyway; the others leave the hand)
                if (st.CorneredMelee && pv.P != Posture.Kite && !(pv.P == Posture.Investigate && pv.Kiting))
                    EndCornered(c, st, e, "the posture is " + ScPosture.Name(pv.P, pv.Kiting) + " now");
                // round wp1 (decision 1: an empty gun is no gun): in a fight posture a gun with no rounds in it or her stores goes for
                // her pair's melee weapon at once - no hand clock, no dwell, no 3 m wait for 0-XNPCCore's 2 s failover. HOLD (watch,
                // Stay) keeps 0-XNPCCore's own failover; the stand above has its own dry rule
                if ((pv.P == Posture.Melee || pv.P == Posture.Kite || pv.P == Posture.Investigate) && DryGunSwap(c, e, st, bout, now)) return false;
                switch (pv.P)
                {
                    case Posture.Melee: return MeleeHand(c, e, st, pv, bout, now);
                    case Posture.Kite: return KiteHand(c, e, st, pv, bout, now);
                    case Posture.Investigate: return pv.Kiting ? KiteHand(c, e, st, pv, bout, now) : MeleeHand(c, e, st, pv, bout, now);
                    case Posture.Hold: return HoldHand(c, e, st, bout, now);
                    case Posture.None: return IdleHand(c, e, bout);
                    default: return true;                                            // SHELTER: the hand as it is
                }
            }
            catch (Exception ex)
            {
                ScLog.Once("arms-hand", ex);
                return true;
            }
        }

        /// <summary>
        /// Round wp1 (the user: "When they have a gun with no ammo, they are just jogging around zombies and not attacking"): the gun
        /// in her hand is dry for good (GunDry) and her pair has a melee weapon - SC's swap to it now, through 0-XNPCCore's own
        /// equip (TakeMelee; a refused equip backs off as MELEE's swap does). Logged at each swap: one per dry episode, since nothing
        /// draws a dry gun again. True when it took.
        /// </summary>
        private static bool DryGunSwap(AvatarSdcsNpcController c, EntityAlive e, Watch st, bool bout, float now)
        {
            // round wp1 (review WP1-m6): dry on two X5 ticks in a row (GunDry itself is never true during a reload) - a one-tick
            // zero while a reload moves the last loose rounds is not "empty"
            bool dry = !bout && HoldsRangedWeapon(e) && !Reloading(e) && GunDryNow(e);   // round wp1 (re-check WP1R-n6): two real reads, not the cache
            bool twice = dry && st.DrySeen;
            st.DrySeen = dry;
            if (!twice) return false;
            Loadout l = ScLoadout.Current(e);
            if (l == null || string.IsNullOrEmpty(l.Melee) || now < st.MeleeBackoffUntil) return false;
            string gun = HeldItemName(e);
            if (!TakeMelee(c, e, l, st, now)) return false;
            ScPosture.NoteSwap(e, l.Melee, false, true);
            JoinBout(c, e, l, now);                                              // no-op while dry (no gun to go back to)
            Patches_Xnpc.Count("x5-dry-gun-melee");
            ScLog.Out(ScRetreat.Name(e) + "'s " + gun + " is empty and " + ScRetreat.Her(e) + " stores hold no rounds for it - the " + l.Melee
                      + " at once (round wp1: an empty gun is no gun - no hand clock, no back-offs while dry)");
            return true;
        }

        /// <summary>
        /// Round wp1 (decision 1): the gun in her hand - else her pair's gun - has no rounds in it or her stores, and no other gun of
        /// hers does: an empty gun is no gun (no back-offs of any kind, melee at once). False with no gun at all. A read.
        /// </summary>
        internal static bool GunDry(EntityAlive e)
        {
            if (e == null || e.inventory == null) return false;
            // round wp1 (review WP1-m6): never during 0-XNPCCore's reload (its rounds may sit between the stores and the magazine);
            // (review WP1-n1) one answer per body per DryCacheSeconds - the UAI asks per candidate target per decision
            if (Reloading(e)) return false;
            float now = Time.time;
            DryAnswer da;
            if (dryCache.TryGetValue(e.entityId, out da) && now - da.At < DryCacheSeconds) return da.Dry;
            if (dryCache.Count > 256) dryCache.Clear();
            bool dry = GunDryNow(e);
            dryCache[e.entityId] = new DryAnswer { At = now, Dry = dry };
            return dry;
        }

        private struct DryAnswer { public float At; public bool Dry; }
        private static readonly Dictionary<int, DryAnswer> dryCache = new Dictionary<int, DryAnswer>();
        /// <summary>Round wp1 (review WP1-n1): GunDry's answer lives this long (one UAI decision).</summary>
        private const float DryCacheSeconds = 0.2f;

        /// <summary>
        /// Round wp1 (review WP1-m5): dry (GunDry) and her pair has a melee weapon to fight with instead - only then are the
        /// back-offs off. Round wp1 (re-check WP1R-m3): with no melee weapon the posture is still MELEE ("nothing to shoot with",
        /// older than wp1), whose AllowsBackOff lets her back away only from a melee-unsafe or armed enemy - that is what this
        /// keeps; from any other she stands until 0-XNPCCore's dry failover gives her fists, then fights with them. A read.
        /// </summary>
        internal static bool DryWithMelee(EntityAlive e)
        {
            if (!GunDry(e)) return false;
            Loadout l = ScLoadout.Current(e);
            return l != null && !string.IsNullOrEmpty(l.Melee) && ItemClass.GetItemClass(l.Melee, true) != null;
        }

        private static bool GunDryNow(EntityAlive e)
        {
            ItemValue hv = e.inventory.holdingItemItemValue;
            ItemClass held = hv != null && !hv.IsEmpty() ? hv.ItemClass : null;
            if (ScAmmo.RangedAction(held) == null) held = null;
            if (held != null && ScAmmo.Rounds(e, held) > 0) return false;
            ItemClass pair = ScAmmo.PairGun(e);
            if (pair != null && (held == null || pair.Id != held.Id) && ScAmmo.Rounds(e, pair) > 0) return false;
            return held != null || pair != null;
        }

        /// <summary>Round wp1 (review WP1-m3): a "matched again" swap to melee still waits this long after her last hand change.</summary>
        private const float UrgentMeleeHandFloor = 1.5f;

        // ------------------------------------------------------------------ the no-swing watchdog (round wp1 review) ----

        /// <summary>Round wp1 (review WP1-M1): no swing for this long at a target in reach in the melee stance - the facts are logged.</summary>
        private const float NoSwingWatchSeconds = 3f;

        /// <summary>Round wp1 (review WP1-M1): MayEngage's last verdict per (member, target) - Voice Control's own score, SC's after the postfix.</summary>
        private struct EngageSeen { public float Raw, Final, At; }
        private static readonly Dictionary<long, EngageSeen> engageSeen = new Dictionary<long, EngageSeen>();

        /// <summary>Round wp1 (review WP1-M1): Patch_MayEngage's note of one verdict for a member (the watchdog reads the last one). Main thread.</summary>
        internal static void NoteEngage(EntityAlive self, EntityAlive t, float raw, float final)
        {
            if (self == null || t == null || self.isEntityRemote || ScRegistry.GetMember(self.entityId) == null) return;
            if (engageSeen.Count > 1024) engageSeen.Clear();
            engageSeen[((long)self.entityId << 32) | (uint)t.entityId] = new EngageSeen { Raw = raw, Final = final, At = Time.time };
        }

        /// <summary>
        /// Round wp1 (review WP1-M1; the user: "She had her melee weapon out, but she didn't swing at all even though she approached
        /// zombies as if she was going to, until I started attacking"). The cause is not proven from the log: 0-XNPCCore's own
        /// swing verdicts are verbose-only. This watchdog writes them down: a member with a weapon that is not a gun in her hand
        /// (a melee weapon or her fists) and an awake enemy within MeleeReach, with no swing (LastSwing unchanged) for
        /// NoSwingWatchSeconds, logs what she holds, the target and whom it is after, and each gate's verdict. A read; X5 only.
        /// Round wp1 (re-check WP1R-m2): whatever the posture - none included, the state round n08 leaves her in - not asleep or
        /// down (a job tool in hand never gets here: Hand returns first). (WP1R-m1) The enemy watched is the nearest within 3 m
        /// that is the posture's target, after her, or her attack, revenge or action target; else the nearest enemy within 3 m.
        /// Never a friend (EntityTargetingUtilities.IsEnemy). (WP1R-n1) At most one line per NoSwingLineGapSeconds per member and
        /// never twice for one of her last NoSwingRememberIds targets.
        /// </summary>
        private static void NoSwingWatch(AvatarSdcsNpcController c, EntityAlive e, Watch st, ScPosture.View pv, bool bout, float now)
        {
            try
            {
                EntityAlive t = null;
                float used = 0f;
                string pick = null;
                bool armed = !HoldsRangedWeapon(e) && HeldItemName(e) != null && LastSwing(e, out used);
#if SC_V34
                if (armed && (ScSleep.IsAsleep(e) || ScDowned.IsDown(e))) armed = false;
#endif
                if (armed) t = NoSwingTarget(e, pv, out pick);
                if (t == null) { st.NsTarget = -1; return; }
                // round wp1 (final check WP1F-m1): the clock starts when an enemy first comes within reach (or after a gap in the
                // watch), not whenever the nearest one changes - in a scrum the nearest changes often, and the clock must run on
                if (st.NsTarget < 0 || now - st.NsTick > KiteTickGapSeconds)
                {
                    st.NsTarget = t.entityId;
                    st.NsSince = now;
                    st.NsUse = used;
                    st.NsTick = now;
                    return;
                }
                st.NsTarget = t.entityId;
                st.NsTick = now;
                if (used != st.NsUse) { st.NsSince = now; st.NsUse = used; return; }   // she swung: the clock starts again
                if (now - st.NsSince < NoSwingWatchSeconds) return;
                if (now - st.NsLineAt < NoSwingLineGapSeconds || st.NsLoggedIds.Contains(t.entityId)) return;
                st.NsLineAt = now;                                                   // before the line: a failing build retries 20 s later, not every tick
                string line;
                using (ScGates.ScopeOf(e)) line = NoSwingFacts(c, e, t, pv, bout, now - st.NsSince, pick);
                ScLog.Out(line);
                // round wp1 (re-check WP1R-n2): remembered only once the line is out
                st.NsLoggedIds.Add(t.entityId);
                if (st.NsLoggedIds.Count > NoSwingRememberIds) st.NsLoggedIds.RemoveAt(0);
                Patches_Xnpc.Count("x5-noswing-watch");
            }
            catch (Exception ex) { ScLog.Once("arms-noswing-watch", ex); }
        }

        /// <summary>Round wp1 (re-check WP1R-n1): one watchdog line per member at most this often.</summary>
        private const float NoSwingLineGapSeconds = 20f;
        /// <summary>Round wp1 (re-check WP1R-n1): a member's last this many watched targets get no second line.</summary>
        private const int NoSwingRememberIds = 4;
        private static readonly List<Entity> nsNear = new List<Entity>();

        /// <summary>
        /// Round wp1 (re-check WP1R-m1): the enemy the watchdog watches - awake, alive, an enemy of hers, within MeleeReach; the
        /// nearest of those that is the posture's target, after her, or her attack, revenge or action target, else the nearest
        /// one. pick: which. A read (its own scratch list).
        /// </summary>
        private static EntityAlive NoSwingTarget(EntityAlive e, ScPosture.View pv, out string pick)
        {
            pick = null;
            World w = e.world;
            if (w == null) return null;
            EntityAlive own = e.GetAttackTarget(), rev = e.GetRevengeTarget(), act = ActionTarget(e);
            EntityAlive best = null;
            bool bestPref = false;
            float bestD2 = float.MaxValue;
            float r2 = MeleeReach * MeleeReach;
            nsNear.Clear();
            try
            {
                w.GetEntitiesInBounds(typeof(EntityAlive), new Bounds(e.position, new Vector3(2f * MeleeReach, 2f * MeleeReach, 2f * MeleeReach)), nsNear);
                for (int i = 0; i < nsNear.Count; i++)
                {
                    EntityAlive a = nsNear[i] as EntityAlive;
                    if (a == null || a == e || a.IsDead() || a.IsSleeping) continue;
                    float d2 = (a.position - e.position).sqrMagnitude;
                    if (d2 > r2 || !EntityTargetingUtilities.IsEnemy(e, a)) continue;
                    string why = a.entityId == pv.TargetId ? "the posture's target" : a.GetAttackTarget() == e ? "after her"
                               : a == own ? "her attack target" : a == rev ? "her revenge target" : a == act ? "her action's target" : null;
                    bool pref = why != null;
                    if (best != null && (bestPref && !pref || (pref == bestPref && d2 >= bestD2))) continue;
                    best = a;
                    bestPref = pref;
                    bestD2 = d2;
                    pick = why ?? "an enemy within " + MeleeReach.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " m";
                }
            }
            finally { nsNear.Clear(); }
            return best;
        }

        /// <summary>The running UAI action's entity target, or null. A read.</summary>
        private static EntityAlive ActionTarget(EntityAlive e)
        {
            UAI.Context ctx = e != null ? e.utilityAIContext : null;
            return ctx != null ? UAI.UAIUtils.ConvertToEntityAlive(ctx.ActionData.Target) : null;
        }

        /// <summary>Round wp1 (review WP1-M1): the watchdog's line - every gate MeleeAttack and MeleeApproach read, as far as SC can read them.</summary>
        private static string NoSwingFacts(AvatarSdcsNpcController c, EntityAlive e, EntityAlive t, ScPosture.View pv, bool bout, float quiet, string pick)
        {
            var ic = System.Globalization.CultureInfo.InvariantCulture;
            var sb = new System.Text.StringBuilder();
            float dist = (t.position - e.position).magnitude;
            sb.Append(ScRetreat.Name(e)).Append(" has not swung in ").Append(quiet.ToString("0.0", ic)).Append(" s at ").Append(t.EntityName).Append(" (").Append(t.entityId)
              .Append(") at ").Append(dist.ToString("0.0", ic)).Append(" m, ").Append(pick ?? "-").Append(" (round wp1 review: the no-swing watchdog) -");
            // her hand and her action
            float wc = e.Buffs != null ? e.Buffs.GetCustomVar("CurrentWeaponClass") : -1f;
            sb.Append(" holds ").Append(HeldItemName(e) ?? "nothing").Append(" (CurrentWeaponClass ").Append(wc.ToString("0", ic)).Append(")");
            UAI.Context ctx = e.utilityAIContext;
            string act = ctx != null && ctx.ActionData.Action != null ? ctx.ActionData.Action.Name : "none";
            EntityAlive at = ActionTarget(e);
            sb.Append("; UAI action ").Append(act).Append(at != null ? " on " + at.EntityName + " (" + at.entityId + ")" : "");
            EntityAlive own = e.GetAttackTarget(), rev = e.GetRevengeTarget();
            sb.Append("; ").Append(ScRetreat.Her(e)).Append(" attack target ").Append(Who(own)).Append(", revenge ").Append(Who(rev));   // round wp2: the pronoun
            // the target
            sb.Append("; it is after ").Append(Who(t.GetAttackTarget())).Append(" (revenge ").Append(Who(t.GetRevengeTarget())).Append(")")
              .Append(", hunts someone: ").Append(YesNo(UAI.UAIConsiderationScIntruder.HuntsSomeone(t)));
            // MayEngage
            EngageSeen es;
            if (engageSeen.TryGetValue(((long)e.entityId << 32) | (uint)t.entityId, out es))
                sb.Append("; MayEngage: Voice Control ").Append(es.Raw.ToString("0.##", ic)).Append(" -> SC ").Append(es.Final.ToString("0.##", ic))
                  .Append(" (").Append((Time.time - es.At).ToString("0.0", ic)).Append(" s ago)");
            else sb.Append("; MayEngage: not asked for it lately");
            sb.Append(", n08 leaves it alone: ").Append(YesNo(UAI.UAIConsiderationScIntruder.LeavesAlone(e, t)));
            // the posture
            string refused = ScPosture.RefusedWhy(e, t);
            sb.Append("; posture ").Append(ScPosture.Name(pv.P, pv.Kiting)).Append(pv.Waiting ? " (a switch waits)" : "").Append(pv.Stand ? " in a last stand" : "")
              .Append(", refusal: ").Append(refused ?? "none").Append(", holds ").Append(ScRetreat.Her(e)).Append(" spot: ").Append(YesNo(ScPosture.Holds(e)));
            // 0-XNPCCore's swing checks
            float reach = XnpcMeleeRange(e);
            sb.Append("; in weapon reach: ").Append(reach > 0f ? YesNo(e.GetDistance(t) <= reach) + " (" + reach.ToString("0.0#", ic) + " m)" : "unread");
            sb.Append(", reach block (0-XNPCCore's last verdict for this pair, of any age): ").Append(XnpcReachBlock(e, t));   // round wp1 (re-check WP1R-n2)
#if SC_V34
            sb.Append(", unreachable mark: ").Append(YesNo(ScReach.Marked(e, t)));
#endif
            sb.Append(", bout: ").Append(YesNo(bout));
            string sr = SwapRefusal(c);
            if (sr != null) sb.Append(" (its swap refused: ").Append(sr).Append(")");
            sb.Append(", sees it: ").Append(YesNo(UAI.SCoreUtils.CanSee(e, t)));
            bool canMove = true;
            try { canMove = e.bodyDamage.CurrentStun.CanMove(); } catch (Exception) { }
            sb.Append(", stunned: ").Append(YesNo(!canMove)).Append(", healing buff: ").Append(YesNo(e.Buffs != null && e.Buffs.HasBuff("SdcsIsHealing")));
#if SC_V34
            sb.Append(", on a work shift: ").Append(YesNo(ScWork.OnShift(e.entityId)));
#endif
            sb.Append(", hurt with no meds: ").Append(YesNo(ScRetreat.HurtNoMedsMember(e)));
            // where she stands
            if (e.world != null)
            {
                Vector3i bp = e.GetBlockPosition();
                Block feet = e.world.GetBlock(bp).Block, under = e.world.GetBlock(bp - Vector3i.up).Block;
                sb.Append("; stands in ").Append(feet != null ? feet.GetBlockName() : "?").Append(" on ").Append(under != null ? under.GetBlockName() : "?");
            }
            return sb.ToString();
        }

        private static string Who(EntityAlive a)
        {
            if (a == null) return "nobody";
            return (a is EntityPlayer ? "you" : ScXnpc.IsBody(a) ? ScCommunity.DisplayName(a) : a.EntityName) + " (" + a.entityId + (a.IsDead() ? ", dead)" : ")");
        }

        private static string YesNo(bool b) { return b ? "yes" : "no"; }

        private static MethodInfo xnpcRangeFor, xnpcBlockKind;
        private static bool xnpcDiagBound;

        /// <summary>
        /// Round wp1 (review WP1-M1): 0-XNPCCore's own melee reach (internal SdcsMeleeRange.For) and its last swing-block verdict for
        /// this pair (internal SdcsMeleeReach.BlockKind: a pure read of its episode, null = clear or never probed) - by name, for the
        /// watchdog's line only; missing reads as "unread". Never called per tick.
        /// </summary>
        private static void BindXnpcDiag()
        {
            if (xnpcDiagBound) return;
            xnpcDiagBound = true;
            try
            {
                Type rt = typeof(AvatarSdcsNpcController).Assembly.GetType("SdcsMeleeRange", false);
                xnpcRangeFor = rt != null ? AccessTools.DeclaredMethod(rt, "For", new[] { typeof(EntityAlive) }) : null;
                // round wp1 (re-check WP1R-n2): by name too, so a 0-XNPCCore without the type costs this read only, not the line
                Type kt = typeof(AvatarSdcsNpcController).Assembly.GetType("SdcsMeleeReach", false);
                xnpcBlockKind = kt != null ? AccessTools.DeclaredMethod(kt, "BlockKind", new[] { typeof(EntityAlive), typeof(EntityAlive) }) : null;
                if (xnpcRangeFor == null || xnpcBlockKind == null)
                    ScLog.Warn("no-swing watchdog: 0-XNPCCore's SdcsMeleeRange.For or SdcsMeleeReach.BlockKind was not found - its line says \"unread\" there");
            }
            catch (Exception ex) { ScLog.Once("arms-xnpc-diag", ex); }
        }

        private static float XnpcMeleeRange(EntityAlive e)
        {
            try
            {
                BindXnpcDiag();                                                  // round wp1 (re-check WP1R-n2): inside the try
                return xnpcRangeFor != null ? (float)xnpcRangeFor.Invoke(null, new object[] { e }) : -1f;
            }
            catch (Exception ex) { ScLog.Once("arms-xnpc-range", ex); return -1f; }
        }

        private static string XnpcReachBlock(EntityAlive e, EntityAlive t)
        {
            try
            {
                BindXnpcDiag();                                                  // round wp1 (re-check WP1R-n2): inside the try
                if (xnpcBlockKind == null) return "unread";
                object k = xnpcBlockKind.Invoke(null, new object[] { e, t });
                return k == null ? "clear (or not probed)" : k.ToString() + " (0-XNPCCore's swing ray is blocked)";
            }
            catch (Exception ex) { ScLog.Once("arms-xnpc-block", ex); return "unread"; }
        }

        /// <summary>Round wp2 (2b): the enemy after her that makes a spent slow gun in KITE melee (meters).</summary>
        private const float KiteSlowCloseDist = 2.5f;

        /// <summary>Round wp2 (2b): the gun in her hand is a slow gun (GunClass: a bow, a crossbow, a pipe gun) whose loaded round is fired (Meta 0). A read.</summary>
        private static bool SlowGunSpent(EntityAlive e, ItemValue held, ItemClass hg)
        {
            if (held == null || hg == null || held.Meta > 0) return false;
            int mag;
            float reload;
            return GunClass(e, hg, out mag, out reload) == StandSlow;
        }

        /// <summary>Round wp2 (2b; review WP2-m3): the nearest awake enemy whose attack or revenge target is her within r, or null. A read (its own scratch list).</summary>
        private static EntityAlive NearestAfterHer(EntityAlive e, float r)
        {
            World w = e.world;
            if (w == null) return null;
            EntityAlive best = null;
            float bd = r * r;
            nsNear.Clear();
            try
            {
                w.GetEntitiesInBounds(typeof(EntityAlive), new Bounds(e.position, new Vector3(2f * r, 2f * r, 2f * r)), nsNear);
                for (int i = 0; i < nsNear.Count; i++)
                {
                    EntityAlive a = nsNear[i] as EntityAlive;
                    if (a == null || a == e || a.IsDead() || a.IsSleeping) continue;
                    float d2 = (a.position - e.position).sqrMagnitude;
                    if (d2 > bd) continue;
                    if ((a.GetAttackTarget() == e || a.GetRevengeTarget() == e) && EntityTargetingUtilities.IsEnemy(e, a)) { best = a; bd = d2; }
                }
            }
            finally { nsNear.Clear(); }
            return best;
        }

        /// <summary>Round k4: MELEE (see Hand).</summary>
        private static bool MeleeHand(AvatarSdcsNpcController c, EntityAlive e, Watch st, ScPosture.View pv, bool bout, float now)
        {
            EntityAlive ct = CurrentTarget(e);
            if (bout)
            {
                // its exit for a target that is gone or beyond 5 m would bring the gun back: held - MELEE keeps the melee weapon
                if (ct == null || (ct.position - e.position).sqrMagnitude > XnpcCloseExitDist * XnpcCloseExitDist)
                {
                    ScPosture.NoteBoutHeld(e);
                    return false;
                }
                ScPosture.NoteBout(e, ct.entityId);                                 // its refusal exits judge the swing
                return true;
            }
            Loadout l = ScLoadout.Current(e);
            if (HoldsRangedWeapon(e))
            {
                // within its enter distance 0-XNPCCore's own swap decides: a bout, or a refusal ScPosture reads next tick. Not held
                // by the hand clock: the posture never turns MELEE while the clock runs (ScPosture), so a gun here up close means
                // 0-XNPCCore's own hand change - and holding its swap would leave her with neither a back-off nor a swing
                if (ct != null && (ct.position - e.position).sqrMagnitude <= XnpcCloseEnterDist * XnpcCloseEnterDist)
                {
                    ScPosture.NoteSwapWatch(e, ct.entityId);
                    return true;
                }
                if (pv.Waiting || l == null || string.IsNullOrEmpty(l.Melee) || now < st.MeleeBackoffUntil) return true;
                // round wp1 (decision 2): matched again moments ago - the swap does not wait for the hand clock; (review WP1-m3) but
                // never within UrgentMeleeHandFloor of her last hand change
                bool clockOk = pv.UrgentMelee ? now - ScPosture.HandChangedAt(e) >= UrgentMeleeHandFloor : ScPosture.HandClockAllows(e, now);
                if (!clockOk) { ScPosture.NoteHeld(e); return true; }
                if (!TakeMelee(c, e, l, st, now)) return true;
                ScPosture.NoteSwap(e, l.Melee, false, pv.UrgentMelee);
                JoinBout(c, e, l, now);
                return false;
            }
            // a melee weapon outside a bout (SC's own swap, or held since the load): with her target within 3 m it joins one, so
            // 0-XNPCCore's own exits judge the swing (only with a pair gun that has rounds to go back to)
            if (ct != null && (ct.position - e.position).sqrMagnitude <= XnpcCloseEnterDist * XnpcCloseEnterDist && JoinBout(c, e, l, now))
                ScPosture.NoteBout(e, ct.entityId);
            return true;
        }

        /// <summary>Round k4: KITE (see Hand).</summary>
        private static bool KiteHand(AvatarSdcsNpcController c, EntityAlive e, Watch st, ScPosture.View pv, bool bout, float now)
        {
            EntityAlive ct = CurrentTarget(e);
            bool clock = ScPosture.HandClockAllows(e, now) || pv.UrgentGun;
            if (bout)
            {
                if (CorneredMeleeHolds(c, e, now)) return !st.CorneredLapse;          // R2: melee until room (or its own exit, or no swing); R3: a lapse held
                if (pv.NativeClose) return true;                                     // a runner: 0-XNPCCore's own bout and exit
                if (pv.Waiting) return true;
                if (!clock) { ScPosture.NoteHeld(e); return true; }                  // its own exit at 5 m agrees with KITE
                string name;
                if (!RememberedGun(c, e, out name)) return true;                     // nothing to shoot: melee it is
                if (!string.Equals(Remembered(c), name, StringComparison.OrdinalIgnoreCase)) rememberedField.SetValue(c, name);
                restoreHeld.Invoke(c, null);
                closeMeleeField.SetValue(c, false);
                closeSinceField.SetValue(c, 0f);
                if (!EquipTook(e, name, "the gun draw", ref st.GunBackoffUntil, now)) return false;
                ScPosture.NoteSwap(e, name, true, pv.UrgentGun);
                return false;
            }
            if (!HoldsRangedWeapon(e))
            {
                if (CorneredMeleeHolds(c, e, now)) return true;                      // R2
                // a runner keeps round n08's rule: her gun only for a target beyond 0-XNPCCore's exit distance
                if (pv.NativeClose && (ct == null || (ct.position - e.position).sqrMagnitude <= XnpcCloseExitDist * XnpcCloseExitDist)) return true;
                if (pv.Waiting || OrderedMelee(e) || now < st.GunBackoffUntil) return true;
                Loadout l = ScLoadout.Current(e);
                if (l == null || !l.HasRanged) return true;
                ItemClass gun = ItemClass.GetItemClass(l.Ranged, true);
                if (ScAmmo.RangedAction(gun) == null || ScAmmo.Rounds(e, gun) <= 0) return true;   // nothing to shoot: melee it is
                if (!clock) { ScPosture.NoteHeld(e); return true; }
                if (!DrawGun(c, e, l, st, now)) return true;
                ScPosture.NoteSwap(e, l.Ranged, true, pv.UrgentGun);
                return false;
            }
            // her gun in hand: 0-XNPCCore's 3 m swap waits - she kites (her back-offs within BackupKeepDistanceMax, the shot beyond)
            ItemValue held = e.inventory.holdingItemItemValue;
            ItemClass hg = held != null && !held.IsEmpty() ? held.ItemClass : null;
            bool close = TargetWithin(e, XnpcCloseEnterDist);
            bool reloading = Reloading(e);
            bool cornered = CorneredOnTarget(e);
            KiteClocks(st, e, held, hg, close, reloading, cornered, now);
            if (pv.NativeClose && close && PrefCorneredSwap(c, e, st, ct, now, false)) return false;   // round nc1 (review): a runner too
            // round wp2 (2b; review WP2-m3): the slow-gun rule looks at the nearest enemy after her within KiteSlowCloseDist, whatever
            // her target, with no back-off point from that one; the hand clock gives way to it (an enemy within 2.5 m, as hr1's stand
            // with an enemy in reach) but not within UrgentMeleeHandFloor of her last hand change
            EntityAlive slowFoe = !pv.NativeClose && !reloading ? NearestAfterHer(e, KiteSlowCloseDist) : null;
            bool slowSpent = slowFoe != null && SlowGunSpent(e, held, hg) && ScBackOff.NoPointFor(e, slowFoe)
                             && now - ScPosture.HandChangedAt(e) >= UrgentMeleeHandFloor;
            if (pv.NativeClose || (!close && !slowSpent)) return true;
            if (ScAmmo.RangedAction(hg) == null || ScAmmo.Rounds(e, hg) <= 0) return true;        // no rounds: its failover and swap
            // R2 (the Fable review's F7, the re-review): reloading with no back-off from her target - 0-XNPCCore's swap to melee
            // runs, so she swings rather than stands idle through the reload, and she stays on melee while no room holds.
            // Round k4: likewise after KiteNoShotSeconds of no shot (a loaded gun with no line of fire up close) - the Fable review
            // of k3+k4 (F1): only KiteNoShotSeconds of CONTINUOUS cornered time with this gun in hand and not one round fired,
            // and only once the hand clock allows. Neither trigger at a target she cannot swing at or a melee-unsafe one (R2Skip).
            bool noShot = !reloading && cornered && st.CorneredSince > 0f && now - Mathf.Max(st.CorneredSince, st.NoShotSince) >= KiteNoShotSeconds
                          && ScPosture.HandClockAllows(e, now);
            // round wp2 (2b; the user: "I also noticed Dave using his Bow at melee range"): hr1's slow-gun rule in KITE - a slow gun
            // (a bow, a crossbow, a pipe gun: GunClass) with its loaded round fired (its magazine empty in her hand), an enemy after
            // her within KiteSlowCloseDist and no back-off point (cornered): melee until she has room, R2's own hold and ends - without
            // waiting for 0-XNPCCore's reload to start. A close gun (magazine 5+) keeps shooting, as hr1 decided
            if (noShot || (reloading && cornered)) slowSpent = false;              // the older triggers first (their lines and counters)
            bool trigger = (cornered && (reloading || noShot)) || slowSpent;
            EntityAlive rt = slowSpent ? slowFoe : ct;                             // round wp2 (review WP2-m3): the one the rule is about
            string skip = trigger ? R2Skip(c, e, rt) : null;
            if (trigger && skip != null && rt != null && st.SkipNoted != rt.entityId)
            {
                st.SkipNoted = rt.entityId;
                Patches_Xnpc.Count("x5-cornered-skipped");
                ScLog.Verbose(ScCommunity.DisplayName(e) + " (id " + e.entityId + ") is cornered and " + (reloading ? "reloading" : slowSpent ? "has fired " + ScRetreat.Her(e) + " slow gun" : "has had no shot")
                              + ", but no melee at " + rt.EntityName + " (" + rt.entityId + "): " + skip + " - the " + hg.GetItemName() + " stays (R2 skipped; F1)");
            }
            if (trigger && skip == null)
            {
                st.SkipNoted = -1;
                if (rt != null) ScPosture.NoteSwapWatch(e, rt.entityId);           // 0-XNPCCore's refusal of the swap is read next tick
                if (!st.CorneredMelee)
                {
                    Patches_Xnpc.Count(reloading ? "x5-cornered-reload-melee" : slowSpent ? "x5-cornered-slow-melee" : "x5-cornered-noshot-melee");
                    ScLog.Verbose(ScCommunity.DisplayName(e) + " (id " + e.entityId + ") is cornered and " + (reloading ? "reloading" : slowSpent
                                  ? "has fired " + ScRetreat.Her(e) + " slow gun with an enemy after " + ScRetreat.HerObj(e) + " within " + KiteSlowCloseDist.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " m (round wp2)"
                                  : "has had no shot for " + KiteNoShotSeconds.ToString("0") + " s") + " - melee until " + ScRetreat.She(e)
                                  + " has room to back off again (the " + hg.GetItemName() + " after; the re-review, R2)");
                }
                st.CorneredMelee = true;
                st.CorneredGun = hg.GetItemName();
                st.CorneredAt = now;
                st.RoomSince = 0f;
                // round nc1 (D4: SC's safety rules still apply under the weapon toggle): with her weapon preference ranged - the
                // toggle, or a gun order of SC's, which now writes it - 0-XNPCCore vetoes its own 3 m swap to melee
                // (WeaponPreferenceRules.CloseMeleeVeto), so R2's melee never came. SC makes that swap itself then, as MELEE's swap
                // does: the pair's melee weapon through 0-XNPCCore's own equip and into its bout, her gun remembered (TakeMelee,
                // JoinBout); R2's hold, its ends and the gun's return run as after 0-XNPCCore's swap. An equip that does not take
                // leaves her with the gun (the veto's outcome, as before this round)
                // round wp2 (review WP2-m3): likewise for the slow-gun rule when her own target is beyond 0-XNPCCore's 3 m (its swap
                // looks at that target, so it would not come)
                if (PrefKind(e) == PrefRanged || (slowSpent && !close))
                {
                    Loadout rl = ScLoadout.Current(e);
                    if (rl != null && !string.IsNullOrEmpty(rl.Melee) && now >= st.MeleeBackoffUntil && TakeMelee(c, e, rl, st, now))
                    {
                        ScPosture.NoteSwap(e, rl.Melee, false, reloading || slowSpent);
                        JoinBout(c, e, rl, now);
                        Patches_Xnpc.Count("x5-cornered-pref-swap");
                        return false;
                    }
                }
                return true;
            }
            if (!close) return true;                                               // round wp2 (review): her target beyond 3 m - 0-XNPCCore's own tick, as before
            Patches_Xnpc.Count("x5-kite-keep-gun");
            return false;
        }

        /// <summary>
        /// Round hr1 (the last stand, the user's decision 5): her hand while ScPosture says she makes a last stand. The weapon her
        /// pair's gun class wants (GunClass): a CLOSE gun keeps shooting (0-XNPCCore's 3 m swap held) and goes to melee only when its
        /// magazine is empty with an enemy within StandReloadClear (2 m) - back to the gun (and its reload) once none is; a SLOW gun
        /// fires its loaded round, then melee for the rest of the stand (no reload with an enemy within StandReach); no melee weapon,
        /// or melee refused, the gun anyway; nothing to shoot, melee. The commitment: no swap for StandCommitSeconds (6 s) since her
        /// hand last changed while an enemy is within StandReach, unless the gun ran dry or melee was just refused. Melee rides in
        /// 0-XNPCCore's bout (JoinBout): its own exit with her target in reach (a wall in her swing, unreachable) refuses melee, as
        /// does no swing in the weapon's window (NoSwingWindow) - both member-wide for the commitment (the gun anyway), never per
        /// target and with no 10 s R2 rest; its exit for a target gone or beyond 5 m is held. R2 is ended at the stand's start. True
        /// runs 0-XNPCCore's own close-range tick, false skips it. X5 only.
        /// </summary>
        private static bool StandHand(AvatarSdcsNpcController c, EntityAlive e, Watch st, ScPosture.View pv, bool bout, float now)
        {
            if (st.StandSince != pv.StandSince)
            {
                // a new stand: its own clocks (and R2 gives way)
                st.StandSince = pv.StandSince;
                st.StandFired = false;
                st.StandMeta = -1;
                st.StandRefusedUntil = 0f;
                st.StandRefusedAt = -100f;
                st.StandRefusedWhy = null;
                st.StandInBout = bout;
                st.StandHeld = null;
                st.StandHeldAt = 0f;
                st.StandSwung = false;
                st.StandTickAt = 0f;
                if (st.CorneredMelee) EndCornered(c, st, e, "the last stand takes over (round hr1)");
            }
            Loadout l = ScLoadout.Current(e);
            EntityAlive ct = CurrentTarget(e);
            ItemClass gun = l != null && l.HasRanged ? ItemClass.GetItemClass(l.Ranged, true) : null;
            if (ScAmmo.RangedAction(gun) == null) gun = null;
            bool gunRounds = gun != null && ScAmmo.Rounds(e, gun) > 0;
            bool hasMelee = l != null && !string.IsNullOrEmpty(l.Melee) && ItemClass.GetItemClass(l.Melee, true) != null;
            bool holdsGun = HoldsRangedWeapon(e);
            ItemValue held = e.inventory.holdingItemItemValue;
            bool holdsPairGun = holdsGun && gun != null && held != null && held.type == gun.Id;
            int meta = holdsGun && held != null ? held.Meta : -1;
            bool reach = EnemyWithin(e, StandReach);
            // 0-XNPCCore ended its bout itself with her target in reach - its verdict that she cannot swing there: the gun anyway
            if (bout) st.StandInBout = true;
            else if (st.StandInBout)
            {
                st.StandInBout = false;
                if (ct != null && (ct.position - e.position).sqrMagnitude <= XnpcCloseExitDist * XnpcCloseExitDist) StandRefuse(e, st, BoutExitWhy(c, e, ct), now);
            }
            // the no-swing window on her melee weapon (sized to it)
            if (!holdsGun && hasMelee && string.Equals(HeldItemName(e), l.Melee, StringComparison.OrdinalIgnoreCase))
            {
                float apm;
                float window = NoSwingWindow(e, l.Melee, out apm);
                if (now >= st.StandRefusedUntil && StandNoSwing(st, e, now, window))
                {
                    Patches_Xnpc.Count("x5-stand-noswing-exit");
                    StandRefuse(e, st, "no swing in " + window.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " s with " + ScRetreat.Her(e) + " " + l.Melee
                                       + " (" + apm.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " attacks/min: a fence, bars or a block in " + ScRetreat.Her(e) + " swing)", now);
                }
            }
            else st.StandHeldAt = 0f;
            bool meleeOk = hasMelee && now >= st.StandRefusedUntil;
            // the weapon the class wants
            bool wantGun;
            if (!gunRounds) wantGun = false;                                     // nothing to shoot: melee
            else if (!meleeOk) wantGun = true;                                   // no melee weapon, or melee refused: the gun anyway
            else if (pv.StandClass == StandSlow)
            {
                if (holdsPairGun)
                {
                    if (st.StandMeta >= 0 && meta < st.StandMeta) st.StandFired = true;   // its loaded round went
                    if (meta <= 0) st.StandFired = true;
                    st.StandMeta = meta;
                }
                // the loaded round in her hand, then melee for the rest of the stand; a fired slow gun reloads only with nobody in reach
                wantGun = holdsPairGun && (!st.StandFired || !reach);
            }
            // a close gun: shoot; empty, melee while one is within 2 m - round hr1 (review HR1-m3): not in hand, it is wanted while its
            // magazine outside the hand is loaded (a close gun keeps shooting), else once nothing is within 2 m (its reload)
            else wantGun = holdsPairGun ? meta > 0 || !EnemyWithin(e, StandReloadClear) : StoredRounds(e, gun) > 0 || !EnemyWithin(e, StandReloadClear);
            if (wantGun == holdsGun) return StandKeep(c, e, st, holdsGun, gunRounds, bout, ct, l, now);
            // a swap: the commitment holds it while an enemy is in reach - unless the gun ran dry or melee was just refused
            bool dry = holdsGun && meta <= 0;
            bool refusedNow = now - st.StandRefusedAt < 0.5f;
            if (reach && !dry && !refusedNow && now - ScPosture.HandChangedAt(e) < ScPosture.StandCommitSeconds)
            {
                ScPosture.NoteStandHeld(e);
                return StandKeep(c, e, st, holdsGun, gunRounds, bout, ct, l, now);
            }
            if (wantGun)
            {
                // round hr1 (review HR1-n2): a slow gun is only ever drawn after a refusal (the reasons below are a close gun's)
                string why = refusedNow ? st.StandRefusedWhy : !hasMelee ? "no melee weapon: the gun" : StoredRounds(e, gun) > 0 ? "a close gun with a loaded magazine: back to shooting"
                             : "nothing within " + StandReloadClear.ToString("0") + " m: back to the gun to reload";
                if (bout)
                {
                    // out of 0-XNPCCore's bout the way its own exit leaves it: the pair's gun restored
                    if (!string.Equals(Remembered(c), l.Ranged, StringComparison.OrdinalIgnoreCase)) rememberedField.SetValue(c, l.Ranged);
                    restoreHeld.Invoke(c, null);
                    closeMeleeField.SetValue(c, false);
                    closeSinceField.SetValue(c, 0f);
                    st.StandInBout = false;
                    if (!EquipTook(e, l.Ranged, "the stand's gun", ref st.GunBackoffUntil, now)) return false;
                }
                else if (now < st.GunBackoffUntil || !DrawGun(c, e, l, st, now)) return true;
                ScPosture.NoteStandSwap(e, l.Ranged, true, why);
                return false;
            }
            string mwhy = dry ? "the " + (held != null && held.ItemClass != null ? held.ItemClass.GetItemName() : "gun") + " is empty"
                                + (pv.StandClass == StandSlow ? " (a slow gun: melee for the rest of the stand)" : " with an enemy within " + StandReloadClear.ToString("0") + " m (no reload)")
                         : pv.StandClass == StandSlow ? "a slow gun: melee for the rest of the stand" : "an enemy within " + StandReloadClear.ToString("0") + " m and the gun empty";
            // round hr1 (review HR1-m4): no melee weapon (a dry gun and nothing else) - 0-XNPCCore's own tick, never a swap to nothing
            if (!hasMelee || l == null) return true;
            if (now < st.MeleeBackoffUntil || !TakeMelee(c, e, l, st, now)) return true;      // the equip did not take: 0-XNPCCore's own tick
            ScPosture.NoteStandSwap(e, l.Melee, false, mwhy);
            JoinBout(c, e, l, now);
            st.StandInBout = CloseMelee(c);
            return false;
        }

        /// <summary>
        /// Round hr1 (review HR1-m3): the rounds in her pair gun's magazine while it is out of her hand - the fullest copy in her stores
        /// (ScAmmo.StoredMagazine, 0-XNPCCore's own dry read) or 0-XNPCCore's magazine table for that gun (its SdcsMagRounds cvar), 0
        /// when neither says. With Locked &amp; Loaded a put-away gun holds no rounds (its magazine is in the bag): 0, and the gun comes
        /// back only for a reload with nothing within 2 m. A read.
        /// </summary>
        private static int StoredRounds(EntityAlive e, ItemClass gun)
        {
            if (e == null || gun == null) return 0;
            float n = 0f;
            try
            {
                n = ScAmmo.StoredMagazine(e, gun);
                if (e.Buffs != null) n = Mathf.Max(n, e.Buffs.GetCustomVar(NPCSdcsCharacters.Pure.GivenWeaponRules.TableRoundsCVar(gun.Id)));
            }
            catch (Exception ex) { ScLog.Once("arms-stored-rounds", ex); }
            return Mathf.Max(0, Mathf.RoundToInt(n));
        }

        /// <summary>
        /// Round hr1: the stand keeps her weapon - the gun with rounds: 0-XNPCCore's 3 m swap held (skip its tick); the melee weapon
        /// in its bout: its exits judge (its exit for a target gone or beyond 5 m held); out of a bout with her target within 3 m,
        /// into one. X5 only.
        /// </summary>
        private static bool StandKeep(AvatarSdcsNpcController c, EntityAlive e, Watch st, bool holdsGun, bool gunRounds, bool bout, EntityAlive ct, Loadout l, float now)
        {
            if (holdsGun)
            {
                if (!gunRounds) return true;                                     // dry for good: its failover and swap
                Patches_Xnpc.Count("x5-stand-keep-gun");
                return false;
            }
            if (bout)
            {
                if (ct == null || (ct.position - e.position).sqrMagnitude > XnpcCloseExitDist * XnpcCloseExitDist)
                {
                    ScPosture.NoteBoutHeld(e);
                    return false;
                }
                return true;
            }
            if (ct != null && (ct.position - e.position).sqrMagnitude <= XnpcCloseEnterDist * XnpcCloseEnterDist && JoinBout(c, e, l, now)) st.StandInBout = true;
            return true;
        }

        /// <summary>
        /// Round hr1: melee is refused in the stand (0-XNPCCore's bout exit with her target in reach, or no swing in the window) - the
        /// gun for StandCommitSeconds, member-wide: no per-target refusal and no R2RestSeconds rest. Logged once per refusal.
        /// </summary>
        private static void StandRefuse(EntityAlive e, Watch st, string why, float now)
        {
            st.StandRefusedUntil = now + ScPosture.StandCommitSeconds;
            st.StandRefusedAt = now;
            st.StandRefusedWhy = why;
            st.StandHeldAt = 0f;
            Patches_Xnpc.Count("x5-stand-melee-refused");
            // round cb1 (point 5): a swing refused with her target in reach is an obstacle (a truck, a fence, a block), not distance -
            // a short step to open ground with a clear swing at it (ScStepClear), whatever she then holds. And the line says what she
            // really does: with no rounds the stand keeps her melee weapon (StandHand's "nothing to shoot: melee"), never an empty gun
            EntityAlive ct = CurrentTarget(e);
            bool stepping = false;
            if (ct != null && (ct.position - e.position).sqrMagnitude <= MeleeReach * MeleeReach)
            {
                // round cb1 (review CB1-m2): ScArms.Hand runs after the X5 tick's scope closed - the home zone is her community's
                using (ScGates.ScopeOf(e))
                    stepping = ScBackOff.RequestStep(e, ct, true, ScRetreat.Her(e) + " swing is blocked in " + ScRetreat.Her(e) + " last stand (" + why + ")");
            }
            ItemClass gun = ScAmmo.PairGun(e);
            bool gunRounds = gun != null && ScAmmo.Rounds(e, gun) > 0;
            ScLog.Out(ScCommunity.DisplayName(e) + " (id " + e.entityId + ") cannot swing in " + ScRetreat.Her(e) + " last stand: " + why + " - "
                      + (gunRounds ? "the gun for " + ScPosture.StandCommitSeconds.ToString("0") + " s, then melee again"
                                   : "nothing to shoot with (no rounds): " + ScRetreat.She(e) + " keeps " + ScRetreat.Her(e) + " melee weapon")
                      + (stepping ? "; " + ScRetreat.She(e) + " steps to open ground first (round cb1)" : "")
                      + " (no 10 s rest, no per-target refusal; round hr1)");
        }

        /// <summary>Round hr1: the stand's no-swing clock (as R2's NoSwing, its own fields): the melee weapon in hand for window with no swing.</summary>
        private static bool StandNoSwing(Watch st, EntityAlive e, float now, float window)
        {
            float used;
            if (!LastSwing(e, out used)) return false;
            string held = HeldItemName(e);
            if (st.StandHeldAt <= 0f || !string.Equals(held, st.StandHeld, StringComparison.OrdinalIgnoreCase))
            {
                st.StandHeldAt = now;
                st.StandHeld = held;
                st.StandUse = used;
                st.StandSwung = false;
                st.StandTickAt = now;
                return false;
            }
            if (used != st.StandUse) st.StandSwung = true;
            if (now - st.StandTickAt > KiteTickGapSeconds) st.StandHeldAt = now;      // a gap (a medical use) restarts the clock
            st.StandTickAt = now;
            return !st.StandSwung && now - st.StandHeldAt >= window;
        }

        /// <summary>Round k4: HOLD (see Hand) - on watch, or a Stay order.</summary>
        private static bool HoldHand(AvatarSdcsNpcController c, EntityAlive e, Watch st, bool bout, float now)
        {
            if (bout) return true;                                                   // 0-XNPCCore's own bout and exit (Stay: its reach rule)
            EntityAlive ct = CurrentTarget(e);
            if (!HoldsRangedWeapon(e))
            {
                // the gun for a target beyond 0-XNPCCore's exit distance (round n08's rule), once the clock allows
                if (OrderedMelee(e) || ct == null || (ct.position - e.position).sqrMagnitude <= XnpcCloseExitDist * XnpcCloseExitDist || now < st.GunBackoffUntil) return true;
                Loadout l = ScLoadout.Current(e);
                if (l == null || !l.HasRanged) return true;
                ItemClass gun = ItemClass.GetItemClass(l.Ranged, true);
                if (ScAmmo.RangedAction(gun) == null || ScAmmo.Rounds(e, gun) <= 0) return true;
                if (!ScPosture.HandClockAllows(e, now)) { ScPosture.NoteHeld(e); return true; }
                if (!DrawGun(c, e, l, st, now)) return true;
                ScPosture.NoteSwap(e, l.Ranged, true, false);
                return false;
            }
            // round nc1 (review): HOLD leaves the 3 m swap to 0-XNPCCore, which a ranged preference vetoes - R2's safety swap here
            if (!ScWatch.Guarding(e.entityId) && PrefCorneredSwap(c, e, st, ct, now, true)) return false;
            // a guard on watch (base defence D3) keeps the gun up close: on a tower the melee swap only leads to a walk off the
            // edge, and the gun still hits what climbs up
            if (!ScWatch.Guarding(e.entityId) || !TargetWithin(e, XnpcCloseEnterDist)) return true;
            ItemValue held = e.inventory.holdingItemItemValue;
            ItemClass hg = held != null && !held.IsEmpty() ? held.ItemClass : null;
            if (ScAmmo.RangedAction(hg) == null || ScAmmo.Rounds(e, hg) <= 0) return true;
            Patches_Xnpc.Count("x5-watch-keep-gun");
            return false;
        }

        /// <summary>
        /// Round k4: no fight (or asleep in bed) - the hand as it is. A bout whose target is gone is closed with the melee weapon
        /// kept: 0-XNPCCore's exit would bring the gun back for nothing (the next fight's posture decides, one swap at most).
        /// </summary>
        private static bool IdleHand(AvatarSdcsNpcController c, EntityAlive e, bool bout)
        {
            if (!bout || CurrentTarget(e) != null) return true;
            rememberedField.SetValue(c, null);
            closeMeleeField.SetValue(c, false);
            closeSinceField.SetValue(c, 0f);
            Patches_Xnpc.Count("posture-bout-closed");
            return true;
        }

        /// <summary>
        /// Round nc1 (the review's finding 1; D4: SC's safety rules still apply under the weapon toggle): where SC leaves her
        /// close-range swap to 0-XNPCCore - HOLD (a Stay), a runner's NativeClose - a ranged preference (the toggle, or a gun order
        /// of SC's, which writes it) vetoes that swap (WeaponPreferenceRules.CloseMeleeVeto), so she would stand reloading with a
        /// zombie on her. R2's rule here too: within 0-XNPCCore's enter distance, reloading, with no room to back off, at a target
        /// she can swing at (R2Skip) - her pair's melee weapon through 0-XNPCCore's own equip and into its bout (TakeMelee,
        /// JoinBout), its exit bringing the gun back. True when it swapped.
        /// The re-check: under a Stay (HOLD) no back-off ever runs (every back-off action is gated NotHasOrder Stay), so she counts
        /// as cornered - but only for a target within StayCorneredDist (1.5 m), inside 0-XNPCCore's Stay exit (its melee reach +
        /// 0.5 m, a weapon's reach being at least SCore's 1.5 m), so the bout never exits at once and loops back to the gun. The
        /// hand clock (TeamSwapHoldSeconds after any change of her hand) also lets a reload that started after a bout finish.
        /// </summary>
        private static bool PrefCorneredSwap(AvatarSdcsNpcController c, EntityAlive e, Watch st, EntityAlive ct, float now, bool stay)
        {
            if (PrefKind(e) != PrefRanged || ct == null || !Reloading(e)) return false;
            if (stay ? !TargetWithin(e, Mathf.Min(XnpcCloseEnterDist, StayCorneredDist)) : (!TargetWithin(e, XnpcCloseEnterDist) || !CorneredOnTarget(e))) return false;
            if (now < st.MeleeBackoffUntil || !ScPosture.HandClockAllows(e, now) || R2Skip(c, e, ct) != null) return false;
            Loadout l = ScLoadout.Current(e);
            if (l == null || string.IsNullOrEmpty(l.Melee) || !TakeMelee(c, e, l, st, now)) return false;
            ScPosture.NoteSwap(e, l.Melee, false, true);
            JoinBout(c, e, l, now);
            Patches_Xnpc.Count("x5-cornered-pref-swap-native");
            return true;
        }

        /// <summary>Round k4: her pair's melee weapon in her hand through 0-XNPCCore's own equip (what it remembers cleared first). True when it took.</summary>
        private static bool TakeMelee(AvatarSdcsNpcController c, EntityAlive e, Loadout l, Watch st, float now)
        {
            if (ItemClass.GetItemClass(l.Melee, true) == null) return false;
            rememberedField.SetValue(c, null);
            MethodBase equip = Patches_Xnpc.EquipByNameTarget();
            if (equip != null)
            {
                // the site name round tm1 used: X16's finalizer treats only "any-weapon-equip" as a hand-over, never this site
                object ok = equip.Invoke(c, new object[] { l.Melee, "sc-melee-first" });
                if (!(ok is bool) || !(bool)ok) return false;
            }
            else if (!ScNpc.UpdateWeapon(e, l.Melee)) return false;
            return EquipTook(e, l.Melee, "the melee swap", ref st.MeleeBackoffUntil, now);
        }

        /// <summary>
        /// Round k4: her pair's gun in her hand - 0-XNPCCore's own equip, as its re-arm does it (its magazine table, its chat draw,
        /// and X16's finalizer puts the attachments back); SCore's UpdateWeapon by hand only when that cannot be bound. What
        /// 0-XNPCCore remembers is cleared first. True when it took.
        /// </summary>
        private static bool DrawGun(AvatarSdcsNpcController c, EntityAlive e, Loadout l, Watch st, float now)
        {
            rememberedField.SetValue(c, null);
            MethodBase equip = Patches_Xnpc.EquipByNameTarget();
            if (equip != null)
            {
                // the site name round n08 used, kept for whatever reads it in the equip path
                object ok = equip.Invoke(c, new object[] { l.Ranged, "sc-gun-first" });
                if (!(ok is bool) || !(bool)ok) return false;
            }
            else
            {
                if (!ScNpc.UpdateWeapon(e, l.Ranged)) return false;
                ScLoadout.ModsAfterEquip(e);                 // a gun SCore builds from its name has no attachments
            }
            return EquipTook(e, l.Ranged, "the gun draw", ref st.GunBackoffUntil, now);
        }

        /// <summary>
        /// Round k4: puts the melee weapon in her hand into 0-XNPCCore's own close-melee bout - its flag, its start time, and her
        /// pair's gun as the weapon its exits restore - so its own refusal exits (a wall or bars in her swing, an unreachable
        /// target) judge the swing; SC holds only its exit for a target gone or beyond 5 m (MeleeHand). Only with a pair gun that
        /// has rounds: with nothing to go back to she keeps the melee weapon whatever its check says. True when joined.
        /// </summary>
        private static bool JoinBout(AvatarSdcsNpcController c, EntityAlive e, Loadout l, float now)
        {
            if (l == null || !l.HasRanged || HoldsRangedWeapon(e)) return false;
            ItemClass gun = ItemClass.GetItemClass(l.Ranged, true);
            if (ScAmmo.RangedAction(gun) == null || ScAmmo.Rounds(e, gun) <= 0) return false;
            rememberedField.SetValue(c, l.Ranged);
            closeSinceField.SetValue(c, now);
            closeMeleeField.SetValue(c, true);
            Patches_Xnpc.Count("posture-bout-joined");
            return true;
        }

        /// <summary>
        /// The final Fable check's C2: a cornered flag 0-XNPCCore never acted on (it refused its swap to melee - a wall, not
        /// closing, unreachable) stayed set on the gun and later held her on melee for no reason. Off a bout, with a gun in her
        /// hand past CorneredSwapGrace and R2's trigger (reloading with no back-off from her target) no longer
        /// true, it ends.
        /// </summary>
        private static void TidyCornered(AvatarSdcsNpcController c, Watch st, EntityAlive e, float now)
        {
            if (st == null || !st.CorneredMelee || !HoldsRangedWeapon(e) || now - st.CorneredAt < CorneredSwapGrace) return;
            if (Reloading(e) && CorneredOnTarget(e) && R2Skip(c, e, CurrentTarget(e)) == null) return;   // R2's reload trigger still holds
            EndCornered(c, st, e, "0-XNPCCore never swapped " + ScRetreat.HerObj(e) + " to melee, and R2's trigger is over (C2)");
        }

        /// <summary>0-XNPCCore says she is reloading (its SdcsReloading cvar). A read.</summary>
        private static bool Reloading(EntityAlive e)
        {
            return e != null && e.Buffs != null && e.Buffs.GetCustomVar(XnpcReloadingCvar) >= 0.5f;
        }

        /// <summary>0-XNPCCore's own target (SdcsCurrentTarget): the attack target, else the revenge target - null when dead or none.</summary>
        private static EntityAlive CurrentTarget(EntityAlive e)
        {
            EntityAlive t = e.GetAttackTarget();
            if (t == null) t = e.GetRevengeTarget();
            return t != null && !t.IsDead() ? t : null;
        }

        /// <summary>Round k4: 0-XNPCCore has her in its close-melee bout. A read.</summary>
        internal static bool InBout(AvatarSDCSController ctrl)
        {
            AvatarSdcsNpcController c = ctrl as AvatarSdcsNpcController;
            return c != null && Bind() && CloseMelee(c);
        }

        /// <summary>Round k4: a ranged weapon in her hand (ScPosture). A read.</summary>
        internal static bool HoldsRanged(EntityAlive e) { return HoldsRangedWeapon(e); }

        /// <summary>Round wp2 (2a): R2's cornered melee holds her now (melee until she has room) - ScPosture starts no hold then. A read.</summary>
        internal static bool CorneredMeleeOn(EntityAlive e)
        {
            Watch st;
            return e != null && watch.TryGetValue(e.entityId, out st) && st.CorneredMelee;
        }

        /// <summary>Round k4: the item in her hand, or null (ScPosture's hand clock). A read.</summary>
        internal static string HeldName(EntityAlive e) { return HeldItemName(e); }

        /// <summary>Round k4: her pair has a gun with rounds. A read.</summary>
        internal static bool HasPairGun(EntityAlive e)
        {
            ItemClass gun = ScAmmo.PairGun(e);
            return gun != null && ScAmmo.Rounds(e, gun) > 0;
        }

        /// <summary>Round k4: why 0-XNPCCore's bout just ended (ScPosture) - its own verdicts, read only. With the melee weapon still in her
        /// hand it held the gun back dry (its deferred restore): a refusal all the same, so SC never rejoins her into a bout each tick.</summary>
        internal static string BoutExitWhy(AvatarSDCSController ctrl, EntityAlive e, EntityAlive t)
        {
#if SC_V34
            if (ScReach.Marked(e, t)) return "0-XNPCCore marked it melee-unreachable (its bout ended)";
#endif
            AvatarSdcsNpcController c = ctrl as AvatarSdcsNpcController;
            if (c != null && wallExitField != null)
            {
                try
                {
                    float at = (float)wallExitField.GetValue(c);
                    if (at >= 0f && Time.time - at <= 1f) return "a wall in " + ScRetreat.Her(e) + " swing (0-XNPCCore's bout ended: wall)";
                }
                catch (Exception ex) { ScLog.Once("arms-wall-exit", ex); }
            }
            if (!HoldsRangedWeapon(e)) return "0-XNPCCore ended " + ScRetreat.Her(e) + " bout and kept the melee weapon (its gun dry)";
            return "0-XNPCCore ended " + ScRetreat.Her(e) + " bout (bars to shoot through, or no swing)";
        }

        /// <summary>
        /// Round k4: 0-XNPCCore's last tick refused to swap this gun holder to melee for a reason that means she cannot swing -
        /// "wall between", "unreachable" or "wall re-enter" (not "not closing": the target only stays off) - else null. A read.
        /// </summary>
        internal static string SwapRefusal(AvatarSDCSController ctrl)
        {
            AvatarSdcsNpcController c = ctrl as AvatarSdcsNpcController;
            if (c == null || closeRefusedField == null) return null;
            try
            {
                string s = closeRefusedField.GetValue(c) as string;
                return s == "wall between" || s == "unreachable" || s == "wall re-enter" ? s : null;
            }
            catch (Exception ex) { ScLog.Once("arms-close-refused", ex); return null; }
        }

        /// <summary>Round tm1: the name of the item in her hand, or null.</summary>
        private static string HeldItemName(EntityAlive e)
        {
            ItemValue hv = e != null && e.inventory != null ? e.inventory.holdingItemItemValue : null;
            return hv != null && !hv.IsEmpty() && hv.ItemClass != null ? hv.ItemClass.GetItemName() : null;
        }

        /// <summary>
        /// Round tm1 (the Fable review, finding 7): after an equip that reported success, is the item really in her hand?
        /// SdcsEquipByName returns true whatever SCore's UpdateWeapon then did (FindWeapon can refuse an item she no longer
        /// carries). If not: a warning once per body and rule, the "x5-team-equip-refused" counter, and the rule backs off
        /// this body for EquipBackoffSeconds - no swap counted, no hold started.
        /// </summary>
        private static bool EquipTook(EntityAlive e, string item, string rule, ref float backoffUntil, float now)
        {
            string held = HeldItemName(e);
            if (string.Equals(held, item, StringComparison.OrdinalIgnoreCase)) return true;
            backoffUntil = now + EquipBackoffSeconds;
            Patches_Xnpc.Count("x5-team-equip-refused");
            ScLog.Once("arms-equip-" + rule + "-" + e.entityId, ScCommunity.DisplayName(e) + " (id " + e.entityId + "): " + rule + " equipped the " + item
                       + " but she holds " + (held ?? "nothing") + " - " + rule + " waits " + EquipBackoffSeconds.ToString("0") + " s for her (logged once)");
            return false;
        }

        /// <summary>
        /// Round tm1, simplified in round k4: why this target is out of her melee weapon's reach, or null - a swing 0-XNPCCore
        /// refused at it (ScPosture's memory: its bout ended for a wall, bars or an unreachable target, or it refused the swap;
        /// TeamUnreachableSeconds, doubling for the same target); standing above her; marked unreachable by 0-XNPCCore
        /// (ScReach, its own 3 s mark); beyond melee reach, her path leads to it and ends short; or (onMelee: she holds her melee
        /// weapon, so she is the one who would walk there) no path and none planned while really blocked. A path that leads
        /// elsewhere (a back-off) says nothing, and plain "no path" is never enough for a gun holder: she plans none. remember (X5
        /// only) keeps a path finding the same way (ScPosture.NoteRefused); the console passes false. Round tm2's reads of
        /// 0-XNPCCore's swing-check episodes and its no-swing clock are gone: 0-XNPCCore's own bout exits judge the swing.
        /// </summary>
        internal static string ReachWhy(EntityAlive e, EntityAlive t, bool onMelee, bool remember)
        {
            string kept = ScPosture.RefusedWhy(e, t);
            if (kept != null) return kept;
            Vector3 d = t.position - e.position;
            if (d.y > ReachAbove) return "above " + ScRetreat.HerObj(e) + " (" + d.y.ToString("0.#") + " m)";
#if SC_V34
            if (ScReach.Marked(e, t)) return "0-XNPCCore marked it melee-unreachable";
#endif
            string why = null;
            if (d.magnitude > MeleeReach)
            {
                GamePath.PathNavigate nav = e.navigator;
                GamePath.PathEntity path = nav != null ? nav.getPath() : null;
                if (path != null && !path.isFinished())
                {
                    float hx = path.toPos.x - t.position.x, hz = path.toPos.z - t.position.z;
                    if (hx * hx + hz * hz <= PathOnTarget * PathOnTarget)
                    {
                        float gx = path.toPos.x - path.rawEndPos.x, gz = path.toPos.z - path.rawEndPos.z;
                        if (gx * gx + gz * gz > PathShortBy * PathShortBy || path.toPos.y - path.rawEndPos.y > ReachAbove) why = "unreachable (her path to it ends short)";
                    }
                }
                else if (onMelee && nav != null && nav.noPathAndNotPlanningOne() && e.moveHelper != null && e.moveHelper.BlockedTime > BlockedSeconds)
                    why = "unreachable (blocked, no path)";
            }
            if (why != null && remember && ScConfig.TeamUnreachableSeconds > 0f)
            {
                ScPosture.NoteRefused(e, t, why);
                Patches_Xnpc.Count("x5-team-unreachable");
            }
            return why;
        }

        /// <summary>A path whose goal is this close (XZ) to the target leads to it (a moving target drifts off the goal).</summary>
        private const float PathOnTarget = 3f;

        /// <summary>
        /// Round k4: her hand for sc team / sc xnpc - what she holds and her pair, the player's orders, R2's cornered melee, an
        /// equip that did not take, 0-XNPCCore's bout and capture, and the overwhelmed read (never a recount).
        /// </summary>
        public static string HandState(EntityAlive e)
        {
            if (e == null) return "-";
            string held = HeldItemName(e) ?? "nothing";
            var sb = new StringBuilder("holding ").Append(held);
            Loadout l = ScLoadout.Current(e);
            if (l != null) sb.Append(" (pair ").Append(l.Ranged ?? "-").Append(" / ").Append(l.Melee ?? "-").Append(')');
            if (OrderedMelee(e)) sb.Append(", YOU ORDERED HER MELEE WEAPON");
            if (OrderedRanged(e)) sb.Append(", YOU ORDERED HER GUN");
            int pref = PrefKind(e);                                                 // round nc1 (D4): the toggle, kept in sync
            if (pref != 0) sb.Append(" (0-XNPCCore's weapon toggle: ").Append(pref == PrefRanged ? "ranged" : "melee").Append(')');
            float now = Time.time;
            Watch st;
            if (watch.TryGetValue(e.entityId, out st))
            {
                if (now < st.MeleeBackoffUntil) sb.Append(", MELEE SWAP BACKED OFF ").Append((st.MeleeBackoffUntil - now).ToString("0")).Append(" s more (an equip did not take)");
                if (now < st.GunBackoffUntil) sb.Append(", GUN DRAW BACKED OFF ").Append((st.GunBackoffUntil - now).ToString("0")).Append(" s more (an equip did not take)");
                if (st.CorneredMelee)
                    sb.Append(", CORNERED ON MELEE ").Append((now - st.CorneredAt).ToString("0.0")).Append(" s (until room for ").Append(CorneredRoomSeconds.ToString("0"))
                      .Append(" s").Append(st.RoomSince > 0f ? ": room " + (now - st.RoomSince).ToString("0.0") + " s" : ": no room").Append("; then the ")
                      .Append(st.CorneredGun ?? "gun").Append(" by the hand rule - R2)");
            }
            AvatarSdcsNpcController c = e.emodel != null ? e.emodel.avatarController as AvatarSdcsNpcController : null;
            if (c != null && bound)
            {
                if (CloseMelee(c)) sb.Append(", in 0-XNPCCore's bout ").Append((now - CloseSince(c)).ToString("0.0")).Append(" s (it restores '").Append(Remembered(c) ?? "-").Append("')");
                if (HandsCaptured(c)) sb.Append(", HANDS CAPTURED (0-XNPCCore: no swap rule acts)");
            }
            int count, need;
            float ago;
            if (ScXnpc.IsBody(e))
            {
                bool over = PeekOverwhelmed(e, out count, out need, out ago);
                sb.Append("; overwhelmed ").Append(count).Append('/').Append(need).Append(over ? " YES" : " no")
                  .Append(ago >= 0f ? " (counted " + ago.ToString("0.0") + " s ago)" : " (not counted yet)");
            }
            return sb.ToString();
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
                // round n08: a melee order stops gun first. Round nc1 (items 6 and D4): not the draw of a quiet kill (a torch in her
                // hand: Voice Control swaps to her melee weapon for the stalk - before, that set the melee order for good) nor the
                // weapon toggle's swap (Patch_VcWeaponPref makes the toggle's own order, whatever the swap could find)
                if (vcOrderScope > 0) Patches_Xnpc.Count("arms-swap-no-order");
                else NoteOrder(npc, itemName, route);
                Member m = ScRegistry.GetMember(npc.entityId);
                if (m == null) return;                       // a stranger's or a prospect's hand stays 0-XNPCCore's business
                ItemClass ic = ItemClass.GetItemClass(itemName, true);
                if (ic == null) return;
                string token = ic.GetItemName();
                Loadout cur = ScLoadout.Current(npc);
                if (cur == null) return;
                if (string.Equals(cur.Ranged, token, StringComparison.OrdinalIgnoreCase) || string.Equals(cur.Melee, token, StringComparison.OrdinalIgnoreCase)) return;
                int slot = WindowSlot(npc, ic.Id);
#if SC_V34
                // round 29a (J29): a work-kit tool she was told to fight with (the weapon menu, "use the bone knife", AnyWeapon's
                // fallback) goes back in the kit and the pair's melee weapon in the hand - kit tools are for jobs only
                if (slot < 0 && ScLoadout.IsKitOnly(npc, token)) { ScJobs.KitToolRefused(npc, m, token, player, route); return; }
#endif
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
                if (!ScLoadout.TakesHanded(token))
                {
                    swapsUnknown++;
                    ScLog.Out(m.Name + " holds " + token + " from the inventory window - not a weapon a survivor can carry in a pair (a launcher, dual pistols,"
                              + " a tool, an item a mod marks not ready, or no weapon at all), so it is not kept in their pair");
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
        /// items. 0-XNPCCore's weapon swaps (OnSwap, members: its weapon menu up to 3.4.05, Voice Control's "use X"); SC's own
        /// "Use the weapons I gave you" (UseHanded) was retired in round U1 - the member inventory panel's slots do it.
        ///
        /// Round n12 (decided: the same store as the member inventory panel's slots). A MEMBER keeps the handed weapon as the
        /// exact item (ScHanded: its quality, wear and mods, so every copy of it carries them and it comes back exactly) - its
        /// mods stay on it instead of coming back separately - and the one it replaces comes back exactly as it was handed when
        /// it was (else her current copy, as before). The window copy's own rounds go into her bag (its seated Locked &amp; Loaded
        /// magazine too) unless 0-XNPCCore has already put that copy in her hand, which moves the rounds with it (its equip
        /// zeroes the window copy's Meta); before n12 they were lost with the window copy. 0-XNPCCore's banked rounds for the
        /// gun that left the pair become loose rounds (ScAmmo.ReleaseXnpcTable). A stale stored item of that slot comes back
        /// with the rest. Anyone without a member record (a hired stranger) keeps the old behavior.
        /// </summary>
        private static bool Keep(EntityAlive npc, string name, string token, int slot, EntityPlayer player, string route)
        {
            Loadout cur = ScLoadout.Current(npc);
            if (cur == null) return false;
            bool ranged = ScLoadout.IsRangedToken(token);
            string old = cur.Of(ranged);
            if (Loadout.IsNoRanged(old)) old = null;                     // a melee-only NPC's first ranged weapon
            Member m = ScRegistry.GetMember(npc.entityId);
            int hslot = ScHanded.WeaponSlot(ranged);
            ItemValue keptOld = ScHanded.Weapon(m, cur, ranged);           // n12: the one it replaces, if it was handed exactly
            ItemValue staleOld = keptOld == null ? ScHanded.Get(m, hslot) : null;
            // before Set refills the slots; never with Locked & Loaded's seated-magazine flag (its magazine stays with her)
            ItemValue oldIv = old == null ? null : keptOld != null ? keptOld.Clone() : ScHanded.Clean(CarriedCopy(npc, old), true);
            ItemStack[] items = ScNpc.Items(npc);
            ItemValue handed = items != null && slot >= 0 && slot < items.Length && items[slot] != null && !items[slot].IsEmpty() ? items[slot].itemValue.Clone() : null;
            bool exact = m != null && handed != null;
            ItemValue held = npc.inventory != null ? npc.inventory.holdingItemItemValue : null;
            bool inHand = exact && held != null && !held.IsEmpty() && held.type == handed.type;   // 0-XNPCCore moved its magazine into the hand
            // a seated L&L magazine is a real item only with L&L loaded (the review's fix 8: without it the flag is dropped)
            bool magazine = exact && ranged && !inHand && ScMags.Loaded && ScMags.SeatedFlag(handed);
            if (magazine && !ScMags.CanEject)
            {
                ScLog.Warn(name + ": could not keep the handed " + token + " - it has a Locked & Loaded magazine in it that SC cannot give back to her bag (take the magazine out first)");
                return false;
            }
            if (exact) ScHanded.Put(m, hslot, ScHanded.Clean(handed, ranged));
            var change = new Loadout();
            change.Set(ranged, token);
            string why;
            // no starting stack for a handed gun: its ammo is what the player hands over, or a resupply at home
            if (!SetGuarded(npc, change, ranged, token, out why))
            {
                if (exact) ScHanded.Put(m, hslot, keptOld ?? staleOld);   // nothing changed
                ScLog.Warn(name + ": could not keep the handed " + token + " (" + why + ")");
                return false;
            }
            if (exact) ScHanded.RecordWeaponQuality(m, hslot);      // round wq1 (review, WQ1-n1): only once the swap is committed
            RetargetRemembered(npc, old, token);
            var back = new List<ItemStack>();
            // the handed item lives in the pair now; a non-member's mods come back, a member's stay on it (n12). Its own loose
            // rounds always go to her bag (the review's fix 5: 0-XNPCCore banks into the window copy only when that copy is at
            // 0, so it can hold rounds of its own); its magazine only when it is not the one 0-XNPCCore put in her hand
            TakeFromWindow(npc, slot, back, !exact, exact && ranged, magazine);
            // Round n08: Set's own fill skipped this weapon while the handed copy was still in her window (FillMods leaves
            // a gun with a window twin alone), so fill the pair's copies - the hand included - now that it is gone.
            ScLoadout.ApplyWeaponMods(npc, true);
            if (exact && old != null && !string.Equals(old, token, StringComparison.OrdinalIgnoreCase))
            {
                try { ScAmmo.ReleaseXnpcTable(npc, ItemClass.GetItemClass(old, true), "weapon menu"); }
                catch (Exception e) { ScLog.Once("arms-keep-table", e); }
            }
            if (oldIv != null) back.Insert(0, new ItemStack(oldIv, 1));
            if (exact && staleOld != null) back.Add(new ItemStack(staleOld.Clone(), 1));
            string where = back.Count > 0 ? Return(back, player, npc) : null;
            swapsKept++;
            Loadout now = ScLoadout.Current(npc);
            ScLog.Out(name + " keeps the " + token + " (" + route + ") in place of the " + (old ?? "-") + (where != null ? ", returned to " + where : "")
                      + " - loadout now " + (now != null ? now.ToString() : "-"));
            if (player != null && old != null)
                // round MP4 (part: tells): this file's tells go by localization key (ScCommunity.TellKey / ScPlayers.TellCommunityKey), so each player reads them in their own language
                ScCommunity.TellKey(player, "scSwapKept", "", name, ItemLabel(token), ItemLabel(old), ScCommunity.LocArg(WhereKey(where)));
            if (ScRegistry.GetMember(npc.entityId) != null || ScRegistry.GetProspect(npc.entityId) != null) ScRegistry.SaveAtomic();
            return true;
        }

        // ------------------------------------------------------------------ the member inventory panel's weapon slots (round n12) ----

        /// <summary>
        /// Round n12: the member inventory panel's RANGED and MELEE slots (ScMemberEquip, host only) - Keep's semantics through
        /// the panel's own entry point. Keep refuses while the container is in use (ScAmmo.IsWindowOpen), and on the host the
        /// panel itself holds that flag; this path never touches her window, so the panel's own open is no obstacle, while a
        /// REMOTE player's open copy (ScAmmo.IsRemoteOpen) or 0-XNPCCore's stock trade still refuse it (ScMemberEquip.Gate).
        ///
        /// handed (from the cursor, a copy): it becomes that half of her pair (ScLoadout.Set, no starting stack: its ammo is what
        /// she carries) and is kept as the exact item (ScHanded; every copy carries it). The same weapon type as now is allowed
        /// too: the pair stays, the handed item replaces the kept one (a better copy of the same gun). Its own rounds go into her
        /// bag as loose rounds of its loaded ammo (refused first when the bag has no room for all of them), a seated Locked &amp;
        /// Loaded magazine as the magazine (refused when it cannot be given back: ScMags.CanEject). handed null: the gun is taken
        /// out of RANGED - she is melee-only (Loadout.NoRanged) and draws her melee weapon.
        ///
        /// back: the weapon the slot held, for the player's cursor - exactly as it was handed when it was, else her current copy
        /// (CarriedCopy), unloaded and without Locked &amp; Loaded's magazine flag; null for none. Its rounds stay with her: the
        /// hand's go to her bag as the hand changes (X12, L&amp;L's own swap patch), 0-XNPCCore's banked ones for it become loose
        /// rounds (ScAmmo.ReleaseXnpcTable). A stale stored item of the slot (her pair had moved on, ScHanded) goes into her
        /// pack, refused first when the pack is full. The hand: a new weapon of the kind she holds goes in her hand (Set keeps
        /// the kind) and is noted as ordered (NoteOrdered); taking the gun arms her melee weapon, noted the same way.
        ///
        /// Returns null when done, else the refusal for the player ("" for nothing to do). Nothing changes on a refusal; past
        /// the pair change nothing throws out of here, so the caller can always hand back over.
        /// </summary>
        internal static string PanelWeapon(EntityPlayerLocal player, EntityAlive npc, Member m, TEFeatureStorage pack, bool ranged, ItemValue handed, out ItemValue back)
        {
            back = null;
            string name = m.Name;
            if (ScMedic.IsUsing(npc.entityId)) return ScMemberEquip.Text("scInvRefuseBusy", name);
            Loadout cur = ScLoadout.Current(npc);
            if (cur == null || cur.Melee == null || !ScNpc.IsV4(npc)) return ScMemberEquip.Text("scInvRefuseNoPair", name);
            int hslot = ScHanded.WeaponSlot(ranged);
            string old = cur.Of(ranged);
            if (Loadout.IsNoRanged(old)) old = null;
            if (handed == null && (!ranged || old == null)) return "";
            string token = handed != null ? handed.ItemClass.GetItemName() : null;
            bool gun = handed != null && ScAmmo.RangedAction(handed.ItemClass) != null;
            // a seated L&L magazine is a real item only with L&L loaded (the review's fix 8: without it the flag is dropped, its
            // Meta is loose rounds)
            bool seated = gun && ScMags.Loaded && ScMags.SeatedFlag(handed);
            if (seated && !ScMags.CanEject) return ScMemberEquip.Text("scInvRefuseUnloadMag");
            if (gun && !seated && handed.Meta > 0 && ScAmmo.RoomForLoaded(npc, handed) < handed.Meta) return ScMemberEquip.Text("scInvRefuseNoRoomRounds", handed.Meta);
            ItemValue keptOld = ScHanded.Weapon(m, cur, ranged);
            ItemValue stale = keptOld == null ? ScHanded.Get(m, hslot) : null;
            // the review's fix 7 (and re-check B): a stale copy of the very weapon joining the pair goes to the PLAYER - not into
            // her pack (a copy in her window is the one 0-XNPCCore equips, and the handed one's copies would stop carrying it:
            // FillMods), nor her bag (a fresh body of hers does not get her old bag back) - into the backpack, else at the
            // player's feet; never onto the cursor, which takes the swap's own result
            bool staleToPlayer = stale != null && handed != null && stale.type == handed.type;
            if (stale != null && !staleToPlayer && !ScMemberEquip.PackRoom(pack, new ItemStack(stale, 1)))
                return ScMemberEquip.Text("scInvRefuseStaleRoom", ScMemberEquip.Label(stale));
            back = old == null ? null : keptOld != null ? keptOld.Clone() : ScHanded.Clean(CarriedCopy(npc, old), true);
            ItemValue prev = ScHanded.Get(m, hslot);
            ScHanded.Put(m, hslot, handed != null ? ScHanded.Clean(handed, gun) : null);
            bool changed = token == null || !string.Equals(token, old, StringComparison.OrdinalIgnoreCase);
            if (changed)
            {
                var change = new Loadout();
                change.Set(ranged, token ?? Loadout.NoRanged);
                string why;
                if (!SetGuarded(npc, change, ranged, token ?? Loadout.NoRanged, out why))
                {
                    ScHanded.Put(m, hslot, prev);                 // nothing changed
                    back = null;
                    return ScMemberEquip.Text("scInvRefuseFailed", why ?? "?");
                }
            }
            if (handed != null) ScHanded.RecordWeaponQuality(m, hslot);   // round wq1 (review, WQ1-n1): only once the swap is committed
            // committed: every step below on its own, none throws out (the caller hands `back` to the cursor next)
            if (gun)
                PanelStep("unload", () =>
                {
                    if (seated) ScMags.Eject(npc, handed, "member panel");
                    // the review's fix 3: what her bag cannot take any more (X12 may have filled it during Set), or the rounds of
                    // a magazine L&L declined to give back, drop at her body - none vanish with this copy
                    if (handed.Meta > 0) ScAmmo.ReturnLoose(npc, handed, "member panel", true);
                });
            if (stale != null)
                PanelStep("stale", () =>
                {
                    if (staleToPlayer) ScMemberEquip.GiveToPlayer(player, npc, new ItemStack(stale.Clone(), 1), "a weapon she no longer carried");
                    else ScMemberEquip.PutInPack(npc, pack, new ItemStack(stale.Clone(), 1), "a weapon she no longer carried");
                });
            PanelStep("remembered", () => RetargetRemembered(npc, old, token));
            PanelStep("mods", () => ScLoadout.ApplyWeaponMods(npc, true));
            if (changed && old != null) PanelStep("table", () => ScAmmo.ReleaseXnpcTable(npc, ItemClass.GetItemClass(old, true), "member panel"));
            PanelStep("hand", () =>
            {
                // round mt1 (review fix G-M1): the slot's item while her hand is holstered (the panel holds her stowed now)
                ItemValue h = ScSkills.HeldOrStowed(npc);
                string heldName = h != null && !h.IsEmpty() && h.ItemClass != null ? h.ItemClass.GetItemName() : null;
                if (handed == null)
                {
                    string w;
                    if (!string.Equals(heldName, cur.Melee, StringComparison.OrdinalIgnoreCase) && !ScLoadout.Arm(npc, false, out w))
                        ScLog.Warn(name + ": melee-only now, but could not take the " + cur.Melee + " in hand (" + w + ")");
                    NoteOrdered(npc);
                }
                else if (string.Equals(heldName, token, StringComparison.OrdinalIgnoreCase)) NoteOrdered(npc);
            });
            PanelStep("save", ScRegistry.SaveAtomic);
            swapsKept++;
            Patches_Xnpc.Count(handed == null ? "n12-panel-ranged-taken" : changed ? "n12-panel-weapon" : "n12-panel-weapon-same");
            Loadout now = ScLoadout.Current(npc);
            if (handed == null)
                ScLog.Out(name + " is melee-only now: her " + ScHanded.ItemText(back) + " went to the player (member panel) - loadout now " + (now != null ? now.ToString() : "-"));
            else
                ScLog.Out(name + " now carries " + ScHanded.ItemText(ScHanded.Get(m, hslot)) + " as her " + (ranged ? "ranged" : "melee") + " weapon"
                          + (old == null ? "" : changed ? " in place of the " + old : " (the same weapon: the one handed now is kept)") + " (member panel)"
                          + (back != null ? " - " + ScHanded.ItemText(back) + " to the player" : "") + " - loadout now " + (now != null ? now.ToString() : "-"));
            return null;
        }

        /// <summary>
        /// The review's fix 2: ScLoadout.Set, which reaches SCore's UpdateWeapon and every patch on it through Refill and Arm,
        /// may throw. True when the pair took the change - Set said so, or it threw after her pair already names the new weapon
        /// (want: the token, or Loadout.NoRanged): the caller then finishes the change rather than leave it half done. False,
        /// with the reason, when it did not.
        /// </summary>
        private static bool SetGuarded(EntityAlive npc, Loadout change, bool ranged, string want, out string why)
        {
            try
            {
                return ScLoadout.Set(npc, change, out why, false);
            }
            catch (Exception e)
            {
                ScLog.Once("arms-set-throw", e);
                Loadout now = null;
                try { now = ScLoadout.Current(npc); }
                catch (Exception) { }
                bool took = now != null && string.Equals(now.Of(ranged), want, StringComparison.OrdinalIgnoreCase);
                why = took ? null : "threw " + e.GetType().Name;
                if (took) ScLog.Warn(ScCommunity.DisplayName(npc) + ": the weapon change threw part way (" + e.GetType().Name + ") after her pair took the "
                                     + want + " - finished");
                return took;
            }
        }

        private static void PanelStep(string what, Action step)
        {
            try { step(); }
            catch (Exception e) { ScLog.Once("arms-panel-" + what, e); }
        }

        /// <summary>
        /// Tell 0-XNPCCore that this weapon was ORDERED, right after SC put it in her hand.
        ///
        /// 3.4.05 gives a survivor a torch at night and takes her weapon out of her hand for it, once
        /// a second. Its own weapon menu calls AnyWeaponNoteEquipped, which sets SdcsTorchSuppressed
        /// for the rest of the night; without it the torch tick sees the weapon, decides it still
        /// wants the torch, and swaps SC's order straight back out about a second later. ScJobs does
        /// this already for a work pair; an order from the player deserves the same.
        ///
        /// Deliberately NOT done on a medic re-arm or an overwhelmed swap: those are not orders, and
        /// they happen in a fight, where 0-XNPCCore has already put the torch away itself.
        /// </summary>
        private static void NoteOrdered(EntityAlive npc)
        {
            try
            {
                // What is actually IN her hand after Arm, not the pair's melee weapon: a swap arms the RANGED one when
                // the player handed over a firearm, and 0-XNPCCore restores whatever it is told here (its close-range melee
                // exit, its dry-gun failover and its world-entry re-arm all read sdcsHeldWeaponRemembered). Naming the club
                // while she holds the handed AK would make it put the club back at the next one of those.
                // Round mt1 (review fix G-m1): while her hand is holstered (Voice Control's stow, which the member panel now holds
                // up) the held item is the bare hand, and naming it told 0-XNPCCore the bare hand was ordered (its world-entry re-arm
                // then kept her unarmed): the selected slot's item is noted instead - what she draws again - and nothing over an
                // empty slot
                ItemValue iv = ScSkills.HeldOrStowed(npc);
                string held = iv != null && !iv.IsEmpty() && iv.ItemClass != null ? iv.ItemClass.GetItemName() : null;
                if (!string.IsNullOrEmpty(held) && !ScLoadout.IsKitOnly(npc, held) && !ScXnpc.IsLightTool(iv.ItemClass))
                    AnyWeaponBridge.AnyWeaponNoteEquipped(npc, held);
            }
            catch (Exception e)
            {
                ScLog.Once("arms-note-ordered", e);
            }
        }

        /// 0-XNPCCore remembers the weapon its close-range swap or failover took out of the hand and restores it later. If
        /// that is the weapon the member just gave back, the restore would ask SCore for an item it no longer finds and fall
        /// back to the melee weapon for good; it remembers the new one instead.
        /// </summary>
        private static void RetargetRemembered(EntityAlive npc, string old, string token)
        {
            AvatarSdcsNpcController c = npc.emodel != null ? npc.emodel.avatarController as AvatarSdcsNpcController : null;
            if (c == null || old == null || !Bind()) return;
            // round n12: token null - the gun was taken and she is melee-only: nothing to come back to
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

        /// <summary>
        /// Takes one of the stack in this window slot; with modsBack its mods (and cosmetic mods) go into back (round n12: not
        /// for a member, who keeps the exact item, mods on). unload (n12, a member's gun): its own rounds go into her bag as
        /// loose rounds - what does not fit drops at her body - instead of being lost with the window copy; eject first gives
        /// a seated Locked &amp; Loaded magazine back as the magazine (should L&amp;L decline, its rounds still come back loose).
        /// </summary>
        private static void TakeFromWindow(EntityAlive npc, int slot, List<ItemStack> back, bool modsBack, bool unload, bool eject)
        {
            ItemStack[] items = ScNpc.Items(npc);
            ItemStack s = items != null && slot >= 0 && slot < items.Length ? items[slot] : null;
            if (s == null || s.IsEmpty()) return;
            ItemValue iv = s.itemValue;
            if (modsBack)
            {
#if SC_V34
                // 3.4: the mod arrays are private; counted accessors instead
                for (int mi = 0; mi < iv.ModificationCount; mi++) { ItemValue mod = iv.GetModification(mi); if (mod != null && !mod.IsEmpty()) back.Add(new ItemStack(mod.Clone(), 1)); }
                for (int mi = 0; mi < iv.CosmeticModCount; mi++) { ItemValue mod = iv.GetCosmeticMod(mi); if (mod != null && !mod.IsEmpty()) back.Add(new ItemStack(mod.Clone(), 1)); }
#else
                if (iv.Modifications != null) foreach (ItemValue mod in iv.Modifications) if (mod != null && !mod.IsEmpty()) back.Add(new ItemStack(mod.Clone(), 1));
                if (iv.CosmeticMods != null) foreach (ItemValue mod in iv.CosmeticMods) if (mod != null && !mod.IsEmpty()) back.Add(new ItemStack(mod.Clone(), 1));
#endif
            }
            ItemValue taken = iv.Clone();
            ItemStack rest = s.Clone();
            rest.count--;
            ScNpc.SetItem(npc, slot, rest.count > 0 ? rest : ItemStack.Empty.Clone());
            if (!unload) return;
            try
            {
                if (eject) ScMags.Eject(npc, taken, "weapon menu");            // Keep refused a seated gun it could not eject
                if (taken.Meta > 0) ScAmmo.ReturnLoose(npc, taken, "weapon menu", true);
            }
            catch (Exception e) { ScLog.Once("arms-keep-unload", e); }
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

        /// <summary>
        /// One backpack holding the items (the container SC drops a dead survivor's window in: it stays until looted). Round MP3
        /// (review storage M2): internal, for ScJobs.CarryWindow's overflow too; true once the items are on their way into the
        /// world (the bag's spawn requested, or loose drops made) - false only when it failed before that, so a caller may drop
        /// them another way without doubling them.
        /// </summary>
        internal static bool DropBag(Vector3 at, List<ItemStack> items, EntityAlive npc)
        {
            string className = ScCommunity.ClassExists(EntityClass.FromString("BackpackNPC")) ? "BackpackNPC" : "Backpack";
            EntityItem pack = EntityFactory.CreateEntity(EntityClass.FromString(className), at) as EntityItem;
            if (pack == null)
            {
                // no bag class: loose items, which despawn like any drop
                foreach (ItemStack st in items) GameManager.Instance.ItemDropServer(st, at, Vector3.zero, -1, 60f);
                return true;
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
            // round MP3 (review storage M2): the bag is requested - a fault in the template's cleanup must not read as "not dropped"
            try { pack.OnEntityUnload(); }
            catch (Exception e) { ScLog.Once("arms-dropbag-unload", e); }
            return true;
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
                    using (ScGates.ScopeOf(npc)) OnSwap(npc, __instance.ID, player, "dialog");     // round MP6 (part G): her community's
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
                    using (ScGates.ScopeOf(__0))      // round MP6 (part G): her community's (Voice Control's command runs on the server unscoped)
                        OnSwap(__0, __1, EntityUtilities.GetLeaderOrOwner(__0.entityId) as EntityPlayer, "voice");
                }
                catch (Exception e)
                {
                    ScLog.Once("arms-voice-swap", e);
                }
            }
        }

        // ------------------------------------------------------------------ the weapon toggle and the quiet kill (round nc1) ----

        /// <summary>Round nc1: Voice Control 3.4.06 final's weapon toggle (VCCommandRouter.MutateSetWeaponPref(EntityAlive, string), internal).</summary>
        public static MethodBase VcWeaponPrefTarget()
        {
            Type t = ScTypes.Find("XNPCVoiceControl.Net.VCCommandRouter");
            return t != null ? AccessTools.DeclaredMethod(t, "MutateSetWeaponPref", new[] { typeof(EntityAlive), typeof(string) }) : null;
        }

        /// <summary>Round nc1: Voice Control 3.4.06 final's quiet-kill order (VCCommandRouter.MutateStealthKill(EntityAlive, EntityPlayer, string), internal).</summary>
        public static MethodBase VcStealthKillTarget()
        {
            Type t = ScTypes.Find("XNPCVoiceControl.Net.VCCommandRouter");
            return t != null ? AccessTools.DeclaredMethod(t, "MutateStealthKill", new[] { typeof(EntityAlive), typeof(EntityPlayer), typeof(string) }) : null;
        }

        /// <summary>
        /// Round nc1 (the user's decision D4: "the toggle wins"). Voice Control's weapon toggle ("Melee -> Ranged" / "Ranged -> Melee"
        /// on the order wheel, a squad's too, and its spoken form) writes 0-XNPCCore's SdcsWeaponPref and swaps her hand when the
        /// pick differs. SC's hand rule ignored the preference, and with no swap (she already held that kind) SC never saw an
        /// order at all, so the toggle and SC's posture fought over her hand. Now the toggle IS SC's weapon order, exactly as
        /// "use your ..." by voice: when it has run, SC's two order cvars follow its argument ("ranged" is her gun, anything else her
        /// melee weapon - Voice Control's own reading of it), whatever its pick could find (with no usable gun Voice Control takes
        /// her melee weapon and says so, and ScPosture's no-ammo rule does the same). The swap inside it makes no order of its own
        /// (vcOrderScope, OnSwap). SC's bodies on the host only; off without Voice Control (Prepare). __0 the NPC, __1 the argument
        /// (bound by position: Voice Control is not referenced).
        /// </summary>
        [HarmonyPatch]
        public static class Patch_VcWeaponPref
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                try { return VcWeaponPrefTarget() != null; }
                catch (Exception e) { ScLog.Once("arms-vc-pref-bind", e); return false; }
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return VcWeaponPrefTarget(); }

            [HarmonyPrefix]
            public static void Prefix() { vcOrderScope++; }

            [HarmonyFinalizer]
            public static void Finalizer(EntityAlive __0, string __1, Exception __exception)
            {
                vcOrderScope = Math.Max(0, vcOrderScope - 1);
                try
                {
                    if (__exception != null || __0 == null || !ScCommunity.IsHost || !ScXnpc.IsBody(__0)) return;
                    bool ranged = string.Equals(__1, "ranged", StringComparison.OrdinalIgnoreCase);
                    NoteOrderKind(__0, ranged);
                    Patches_Xnpc.Count(ranged ? "arms-toggle-ranged" : "arms-toggle-melee");
                    ScLog.Verbose(ScCommunity.DisplayName(__0) + " (id " + __0.entityId + "): the weapon toggle - SC's order is now "
                                  + (ranged ? "her gun" : "her melee weapon") + " (round nc1, D4)");
                }
                catch (Exception e)
                {
                    ScLog.Once("arms-vc-pref", e);
                }
            }
        }

        /// <summary>
        /// Round nc1 (the audit, item 6). Voice Control 3.4.06 final's quiet-kill order ("take them out quietly" on an enemy you
        /// look at) swaps a torch in her hand for her melee weapon through MutateSwapWeapon, which ends in Patch_VoiceSwap - and
        /// OnSwap took that draw for a melee order and kept her on her melee weapon for good. Inside the order no swap is an order
        /// (vcOrderScope). Off without Voice Control (Prepare).
        /// </summary>
        [HarmonyPatch]
        public static class Patch_VcStealthKill
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                try { return VcStealthKillTarget() != null; }
                catch (Exception e) { ScLog.Once("arms-vc-stealth-bind", e); return false; }
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return VcStealthKillTarget(); }

            [HarmonyPrefix]
            public static void Prefix() { vcOrderScope++; }

            [HarmonyFinalizer]
            public static void Finalizer() { vcOrderScope = Math.Max(0, vcOrderScope - 1); }
        }

        /// <summary>
        /// Round nc1 (the audit, item 5): 0-XNPCCore 3.4.06 final's quiet kill is running on her - its UAI task
        /// (UAITaskStealthKillSdcs: a crouched stalk to the target and one strike with what she holds). Its own UAI action owns her
        /// hand meanwhile. False on an 0-XNPCCore without it (logged once), or on any fault. Main thread (the X5 tick).
        /// </summary>
        private static bool StealthKillRuns(EntityAlive e)
        {
            if (stealthTypeMissing) return false;
            try { return StealthTaskRunning(e); }
            catch (Exception ex)
            {
                stealthTypeMissing = true;
                ScLog.Warn("0-XNPCCore's quiet kill (UAI.UAITaskStealthKillSdcs) is not there (" + ex.GetType().Name + ") - SC's hand rule cannot tell when it runs (0-XNPCCore before 3.4.06 final?)");
                return false;
            }
        }

        private static bool stealthTypeMissing;

        /// <summary>The type test in a method of its own (NoInlining): on an 0-XNPCCore without the type the JIT throws here, inside StealthKillRuns' try.</summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static bool StealthTaskRunning(EntityAlive e)
        {
            UAI.Context ctx = e != null ? e.utilityAIContext : null;
            return ctx != null && ctx.ActionData.Action != null && ctx.ActionData.CurrentTask is UAI.UAITaskStealthKillSdcs;
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
            float ago;
            // round tm1 (the Fable review): a read - the console never recounts, so it never moves the sustain clocks
            bool over = PeekOverwhelmed(e, out count, out need, out ago);
            sb.Append(count).Append(" enemy(ies) on them within ").Append(ScConfig.OverwhelmedRadius.ToString("0.#")).Append(" m")
              .Append(ago >= 0f ? " (counted " + ago.ToString("0.0") + " s ago)" : " (not counted yet)").Append(", overwhelmed at ").Append(need)
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
            ScPosture.View pv;
            sb.Append("; ").Append(ScPosture.Of(e, out pv) ? "posture " + ScPosture.Name(pv.P, pv.Kiting) + " (round k4: the one hand rule - sc team " + e.entityId + ")"
                      : MemberOf(e) != null ? "no fresh posture (the overwhelmed rule meanwhile)" : "no posture (not a member: the overwhelmed rule)");
            AvatarSdcsNpcController c = e.emodel != null ? e.emodel.avatarController as AvatarSdcsNpcController : null;
            if (c != null && Bind())
                sb.Append("; 0-XNPCCore close melee=").Append(CloseMelee(c)).Append(CloseMelee(c) ? " for " + (now - CloseSince(c)).ToString("0.0") + " s" : "")
                  .Append(" remembered='").Append(Remembered(c) ?? "-").Append('\'');
            return sb.ToString();
        }
    }
}
