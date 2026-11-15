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
    /// Team strength (round tm1, ScTeam; TeamMelee). While a member is melee-led - a matched fight - she fights with her
    /// melee weapon: MeleeFirst takes it out of the gun's place for a target in reach (I2), GunFirst draws the gun only for
    /// a target that needs it or when she is overwhelmed (I1), a close-range bout of 0-XNPCCore's is adopted so its exit
    /// never brings the gun back (I3), and the overwhelmed threshold is one attacker higher (I4). MeleeFirst and GunFirst
    /// share one swap-hold clock (TeamSwapHoldSeconds). A gun order from the player (OrderedRangedCvar, I8) exempts her.
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
            /// <summary>Round n08 (GunFirst): since when she has been on her melee weapon with a reason to take the gun (0 = not now).</summary>
            public float FirstSince;
            /// <summary>Round tm1 (MeleeFirst): since when she has held her gun with a target in reach while melee-led (0 = not now).</summary>
            public float MeleeSince;
            /// <summary>
            /// Round tm1: the one swap-hold clock MeleeFirst and GunFirst share (TeamSwapHoldSeconds): until HoldUntil the swap
            /// in direction HoldDir (HoldToGun after a melee-first swap, HoldToMelee after a gun-first draw) waits.
            /// </summary>
            public float HoldUntil;
            public int HoldDir;
            /// <summary>Round tm1: the counters were told about this hold, this held gun-first draw (once each, not at 5 Hz).</summary>
            public bool HoldNoted, GunHeldNoted;
            /// <summary>Round tm1: a target found out of melee reach (ReachWhy), kept until UnreachUntil (TeamUnreachableSeconds).</summary>
            public int UnreachId = -1;
            public float UnreachUntil;
            public string UnreachWhy;
            /// <summary>
            /// Round tm2: the kept finding is a block in her swing (0-XNPCCore's swing check), so symmetric reach can drop it.
            /// </summary>
            public bool UnreachSwing;
            /// <summary>
            /// Round tm2: a block in her swing counts only once two reads in a row agree (Agrees) - the target, the block kind,
            /// the evidence stamp (the episode's At, or the read's own time when the episode cannot be read), when it was last
            /// read, and whether a second agreeing read has confirmed it (SwingPendSure).
            /// </summary>
            public int SwingPendId = -1;
            public string SwingPendKind;
            public float SwingPendAt, SwingPendSeen;
            public bool SwingPendSure;
            /// <summary>
            /// Round tm2: her state against each target (Pair: the notes, the no-swing clock, the hold after it ran out) - a small
            /// map, so two attackers at once each keep their own (the Fable review, finding 8); PairOf caps and prunes it.
            /// </summary>
            public readonly Dictionary<int, Pair> Pairs = new Dictionary<int, Pair>();
            /// <summary>
            /// Round tm2: the close reach draw's gun hold (GunFirst, StartReachGun): she stands and shoots ReachGunId until
            /// ReachGunUntil - ReachTick keeps it on while that target is after her within ReachGunDist and she holds her gun,
            /// plus ReachGunGrace.
            /// </summary>
            public int ReachGunId = -1;
            public float ReachGunUntil;
            /// <summary>
            /// The Fable review of round tm1 (finding 7): an equip that left the hand as it was backs MeleeFirst / GunFirst off
            /// this body until then (EquipBackoffSeconds), so a refused equip is not retried at 5 Hz.
            /// </summary>
            public float MeleeBackoffUntil, GunBackoffUntil;
        }

        /// <summary>The Fable review of round tm1 (finding 7): how long a member's swap rule waits after an equip that did not take.</summary>
        private const float EquipBackoffSeconds = 10f;

        /// <summary>Round tm1: Watch.HoldDir - the swap that waits.</summary>
        private const int HoldNone = 0, HoldToGun = 1, HoldToMelee = 2;

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
        /// while a capture holds her hands, and SC's swap rules do too. Optional: null reads as "not held".
        /// </summary>
        private static FieldInfo captureField;
        /// <summary>
        /// Round tm1 (the Fable review, finding 1): 0-XNPCCore's SdcsMeleeReach.BlockKind(self, target) - a read of the block
        /// its own swing check last found between the two (the reason its close-melee bout exits for a wall). Optional.
        /// </summary>
        private static MethodInfo blockKind;
        /// <summary>
        /// Round tm2: the episode table behind BlockKind - SdcsMeleeReach.s_episodes (a static Dictionary&lt;long, Episode&gt;),
        /// its key (Key(selfId, targetId)) and the episode's Kind, Choice (0-XNPCCore's decision: AttackGateRules.ReachChoice),
        /// At (when Evaluate made it) and ArcRays (more than 0: found by a side ray of the swing arc, the straight line to
        /// the target's chest clear). Optional: unbound, a swing block counts on two BlockKind reads 0.2 s apart instead.
        /// episodesField is set last and is the "bound" flag.
        /// </summary>
        private static FieldInfo episodesField, episodeKindField, episodeChoiceField, episodeAtField, episodeArcField;
        private static MethodInfo episodeKey;
        /// <summary>Round tm2: 0-XNPCCore's AttackGateRules.SwingAllowed(ReachChoice) - its melee attack's own swing gate. Optional (by name when unbound).</summary>
        private static MethodInfo swingAllowed;

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
            gunFirstDraws = 0;
            meleeFirstSwaps = boutsKept = 0;
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
        /// Round tm1 (the Fable review): the two optional reads the team rule uses - the capture flag and 0-XNPCCore's swing
        /// block. Either missing is logged once and reads as nothing found (the rule works without it, a little blinder).
        /// </summary>
        private static void BindTeamReads()
        {
            try
            {
                captureField = AccessTools.DeclaredField(typeof(AvatarSdcsNpcController), "sdcsHandsOwnedByCapture");
                if (captureField != null && (captureField.IsStatic || captureField.FieldType != typeof(bool))) captureField = null;
                if (captureField == null) ScLog.Warn("0-XNPCCore's sdcsHandsOwnedByCapture not found - SC's melee-first and gun-first swaps cannot see a hand capture (0-XNPCCore update?)");
            }
            catch (Exception e) { captureField = null; ScLog.Once("arms-bind-capture", e); }
            try
            {
                Type t = typeof(AvatarSdcsNpcController).Assembly.GetType("SdcsMeleeReach", false);
                blockKind = t != null ? t.GetMethod("BlockKind", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null,
                                                    new[] { typeof(EntityAlive), typeof(EntityAlive) }, null) : null;
                if (blockKind == null) ScLog.Warn("0-XNPCCore's SdcsMeleeReach.BlockKind(EntityAlive, EntityAlive) not found - the team rule cannot see a wall or bars in a member's swing (0-XNPCCore update?)");
            }
            catch (Exception e) { blockKind = null; ScLog.Once("arms-bind-blockkind", e); }
            // round tm2: its age and 0-XNPCCore's choice - apart, so a failure here leaves the BlockKind fallback bound
            try { BindEpisodes(typeof(AvatarSdcsNpcController).Assembly.GetType("SdcsMeleeReach", false)); }
            catch (Exception e) { episodesField = null; ScLog.Once("arms-bind-episodes", e); }
        }

        /// <summary>
        /// Round tm2: binds SdcsMeleeReach's episode table (see episodesField) and AttackGateRules.SwingAllowed. Missing
        /// pieces are logged once; the table unbound, the swing block falls back to BlockKind reads.
        /// </summary>
        private static void BindEpisodes(Type reach)
        {
            episodesField = null;
            const BindingFlags S = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            const BindingFlags I = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            FieldInfo table = reach != null ? reach.GetField("s_episodes", S) : null;
            Type ft = table != null ? table.FieldType : null;
            Type ep = ft != null && ft.IsGenericType && ft.GetGenericTypeDefinition() == typeof(Dictionary<,>) && ft.GetGenericArguments()[0] == typeof(long)
                      ? ft.GetGenericArguments()[1] : null;
            MethodInfo key = reach != null ? reach.GetMethod("Key", S, null, new[] { typeof(int), typeof(int) }, null) : null;
            FieldInfo kind = ep != null ? ep.GetField("Kind", I) : null;
            FieldInfo choice = ep != null ? ep.GetField("Choice", I) : null;
            FieldInfo at = ep != null ? ep.GetField("At", I) : null;
            FieldInfo arc = ep != null ? ep.GetField("ArcRays", I) : null;
            if (ep == null || key == null || key.ReturnType != typeof(long) || kind == null || !kind.FieldType.IsEnum
                || choice == null || !choice.FieldType.IsEnum || at == null || at.FieldType != typeof(float))
            {
                ScLog.Warn("0-XNPCCore's SdcsMeleeReach episode table (s_episodes: Kind, Choice, At) could not be read - a block in a member's swing counts "
                           + "on two BlockKind reads 0.2 s apart, blind to its age and to 0-XNPCCore's own choice (0-XNPCCore update?)");
                return;
            }
            episodeKey = key;
            episodeKindField = kind;
            episodeChoiceField = choice;
            episodeAtField = at;
            episodeArcField = arc != null && arc.FieldType == typeof(int) ? arc : null;
            if (episodeArcField == null)
                ScLog.Warn("0-XNPCCore's SdcsMeleeReach episode has no ArcRays - symmetric reach cannot tell a block right between them (bars) from one beside her swing, and sets both aside (0-XNPCCore update?)");
            Type rules = reach.Assembly.GetType("NPCSdcsCharacters.Pure.AttackGateRules", false);
            MethodInfo sa = rules != null ? rules.GetMethod("SwingAllowed", BindingFlags.Static | BindingFlags.Public, null, new[] { choice.FieldType }, null) : null;
            swingAllowed = sa != null && sa.ReturnType == typeof(bool) ? sa : null;
            if (swingAllowed == null)
                ScLog.Warn("0-XNPCCore's AttackGateRules.SwingAllowed(ReachChoice) not found - SC reads its choices NoSwing and Shoot as 'no swing' by name (0-XNPCCore update?)");
            episodesField = table;
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
                    // round tm2: the close reach draw's gun hold keeps the gun up close, as the overwhelmed gun hold does
                    Watch rw;
                    bool reachHold = watch.TryGetValue(e.entityId, out rw) && ReachGunHolds(rw, e, now);
                    if (!ScConfig.Overwhelmed && !onWatch && !reachHold) return true;
                    if (!TargetWithin(e, XnpcCloseEnterDist)) return true;                               // 0-XNPCCore would not swap yet
                    ItemValue held = e.inventory.holdingItemItemValue;
                    ItemClass gun = held != null && !held.IsEmpty() ? held.ItemClass : null;
                    if (ScAmmo.RangedAction(gun) == null || ScAmmo.Rounds(e, gun) <= 0) return true;   // no gun to keep: melee it is
                    if (onWatch)
                    {
                        Patches_Xnpc.Count("x5-watch-keep-gun");
                        return false;
                    }
                    if (reachHold) return false;                  // round tm2: her 3 m swap waits while she stands and shoots
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
                // round tm1 (I3): a melee-led member keeps the melee weapon 0-XNPCCore's close-range swap gave her - its bout
                // is adopted (cleared), so its exit at 5 m never restores the gun; GunFirst still takes her back to it for a
                // target that needs it or when she is overwhelmed (+1 attacker in a matched fight, decision 7)
                if (ScTeam.MeleeLed(e) && AdoptBout(c, e, now)) return true;
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

        /// <summary>
        /// Round n07: is SC deliberately keeping a gun in this body's hand at close range right now? True while the
        /// overwhelmed rule holds her on the gun (the 6 s GunUntil window, or the live overwhelmed state).
        ///
        /// It exists because of a weight inversion in 0-XNPCCore 3.4: inside 4 m, BackupKeepDistance (8) outranks
        /// RangedMeleeAttack (6), and BackupFromTargetPaced contains no shot - so a gun holder at close range RETREATS
        /// and never fires, while the melee actions are gated off by CurrentWeaponClass. SC's overwhelmed rule was
        /// written against the older build where that action merely backed her off; today it means a member SC has
        /// deliberately put on the gun can neither shoot nor swing, which is the "runs around switching weapons and
        /// never attacks" the user saw. Config/utilityai.xml uses this to score BackupKeepDistance to 0 for exactly
        /// those bodies, so she fires instead. Note she then STANDS AND FIRES point-blank rather than backing off -
        /// UAI runs one action at a time, so with the back-off scored to 0 the shot (6) simply beats BackupRanged (5).
        /// That is the intended trade: a member who can shoot is better off than one who can do neither.
        /// Round tm2: also while the close reach draw's gun hold is on (ReachGunHolds): she shoots the attacker she could
        /// not swing at, rather than backing away from it and coming back.
        /// </summary>
        public static bool HoldingGunUpClose(EntityAlive e)
        {
            try
            {
                if (e == null || !ScXnpc.IsBody(e) || !HoldsRangedWeapon(e)) return false;
                // A PURE READ of the state X5's own tick already maintains. It must not call IsOverwhelmed: that mutates
                // the watch entry (it advances LastCount, OverSince and OverUntil and runs a 12 m entity sweep), and a
                // consideration runs for every candidate target several times a second - UAI does not short-circuit a
                // zero, so it would run even when the distance and sight gates above it have already scored 0. Driving
                // IsOverwhelmed from here would keep LastCount permanently fresh, which makes the swarm look sustained
                // on the first melee tick and defeats the 1.5 s anti-flap window that stops the weapon flipping.
                Watch gw;
                if (!watch.TryGetValue(e.entityId, out gw)) return false;
                float now = Time.time;
                if (ScConfig.Overwhelmed && (now < gw.GunUntil || now < gw.OverUntil)) return true;
                // round tm2 (the Fable review, finding 4): after a close reach draw she stands and shoots the attacker
                return ReachGunHolds(gw, e, now);
            }
            catch (Exception ex) { ScLog.Once("arms-gun-upclose", ex); return false; }
        }

        /// <summary>
        /// Round tm2: HoldingGunUpClose's overwhelmed part alone (ScTeam.HoldsGround: then the back-off is today's). A pure read.
        /// </summary>
        public static bool OverwhelmedGunHold(EntityAlive e)
        {
            try
            {
                if (e == null || !ScConfig.Overwhelmed || !ScXnpc.IsBody(e) || !HoldsRangedWeapon(e)) return false;
                Watch gw;
                if (!watch.TryGetValue(e.entityId, out gw)) return false;
                float now = Time.time;
                return now < gw.GunUntil || now < gw.OverUntil;
            }
            catch (Exception ex) { ScLog.Once("arms-gun-overwhelmed", ex); return false; }
        }

        /// <summary>
        /// Round tm2: she stands and shoots this target after a close reach draw (the reach gun hold; t null: any target) -
        /// ScTeam.HoldsGround scores 0-XNPCCore's BackupKeepDistance and BackupRanged to 0 for it. A pure read.
        /// </summary>
        public static bool ReachGunHolds(EntityAlive e, EntityAlive t)
        {
            Watch st;
            return e != null && watch.TryGetValue(e.entityId, out st) && ReachGunHolds(st, e, Time.time)
                   && (t == null || t.entityId == st.ReachGunId) && HoldsRangedWeapon(e);
        }

        /// <summary>Round tm2: the reach gun hold is on (TeamMelee, still melee-led: else her back-off and swaps are today's). A pure read.</summary>
        private static bool ReachGunHolds(Watch st, EntityAlive e, float now)
        {
            return st != null && st.ReachGunId >= 0 && now < st.ReachGunUntil && ScConfig.TeamMelee && ScTeam.MeleeLed(e);
        }

        /// <summary>Round tm2: a ranged weapon in her hand.</summary>
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

        // ------------------------------------------------------------------ gun first (round n08) ----

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
        /// <summary>How long the reason to take the gun must hold before she does (a zombie stepping in and out of 5 m).</summary>
        private const float GunFirstSeconds = 1f;
        private static int gunFirstDraws;

        public static bool OrderedMelee(EntityAlive e)
        {
            return e != null && e.Buffs != null && e.Buffs.GetCustomVar(OrderedMeleeCvar) >= 0.5f;
        }

        /// <summary>Round tm1: the player's last weapon order for her named her gun or bow (OrderedRangedCvar).</summary>
        public static bool OrderedRanged(EntityAlive e)
        {
            return e != null && e.Buffs != null && e.Buffs.GetCustomVar(OrderedRangedCvar) >= 0.5f;
        }

        /// <summary>
        /// A weapon order from the player (route "dialog" or "voice"; never "any-weapon", 0-XNPCCore's own pick-up of a newly
        /// handed weapon): a melee weapon sets OrderedMeleeCvar and clears OrderedRangedCvar, a gun or bow the other way round
        /// (round tm1: before it a gun order only cleared the melee order).
        /// </summary>
        private static void NoteOrder(EntityAlive npc, string itemName, string route)
        {
            if (npc == null || npc.Buffs == null || route == "any-weapon") return;
            string family = ScLoadout.FamilyOf(itemName);
            if (family == "melee")
            {
                npc.Buffs.SetCustomVar(OrderedMeleeCvar, 1f);
                if (npc.Buffs.HasCustomVar(OrderedRangedCvar)) npc.Buffs.RemoveCustomVar(OrderedRangedCvar);
            }
            else if (family != null)
            {
                if (npc.Buffs.HasCustomVar(OrderedMeleeCvar)) npc.Buffs.RemoveCustomVar(OrderedMeleeCvar);
                npc.Buffs.SetCustomVar(OrderedRangedCvar, 1f);
            }
        }

        /// <summary>
        /// Round n08, "gun first" (SurvivorGunFirst; play test 2026-11-14: a member given a pistol never drew it, and an iron
        /// crossbow was never used). The design has always been "the gun when it has ammo, melee up close and when it runs
        /// dry", but nothing actually DRAWS the gun for a survivor who is on her melee weapon outside 0-XNPCCore's close-range
        /// swap: that swap only goes gun -> melee -> back; its world-entry step first re-equips the last-held weapon (and SC's
        /// own hand at load is the pair's melee weapon, XnpcOrder); and the overwhelmed rule only keeps a gun or returns from
        /// close melee. Once she was on melee for any reason - a stow, a talk, a medic use, a load - she stayed there.
        /// So, for an SC survivor (never a raider: its lead rule decides) on her pair's melee weapon, outside close melee, not
        /// holding a job's tool, with no melee order from the player: when her target (attack, else revenge) has stayed
        /// beyond 0-XNPCCore's close-melee exit distance (5 m) or she is overwhelmed, for GunFirstSeconds, and the pair's gun
        /// has rounds in her stores, she takes it. 0-XNPCCore's close-range swap then does melee up close and back to this
        /// gun as designed: what it remembers is cleared first, since it only remembers the gun it takes out of the hand
        /// while nothing is remembered. When overwhelmed the gun is held for OverwhelmedGunSeconds (CloseRange keeps it up
        /// close). True when it swapped: 0-XNPCCore's close-range tick is skipped for this round.
        /// Round tm1 (I1): while she is melee-led (ScTeam) a target beyond 5 m is no reason - only one that needs the gun
        /// (ScTeam.NeedsGun: then today's rule) or being overwhelmed is. A draw holds MeleeFirst for TeamSwapHoldSeconds, and
        /// waits out MeleeFirst's own hold (the one swap-hold clock).
        /// </summary>
        public static bool GunFirst(AvatarSDCSController ctrl, EntityAlive e)
        {
            try
            {
                if (!ScConfig.SurvivorGunFirst || e == null || e.isEntityRemote || e.inventory == null || e.Buffs == null) return false;
                if (!ScXnpc.IsBody(e) || ScXnpc.IsRaider(e)) return false;
                AvatarSdcsNpcController c = ctrl as AvatarSdcsNpcController;
                // round tm1 (the Fable review): never while 0-XNPCCore's capture holds her hands
                if (c == null || !Bind() || CloseMelee(c) || HandsCaptured(c)) return false;
                float now = Time.time;
                if (now >= nextPrune) Prune(now);
                Watch st = WatchOf(e, now);
                if (now < st.GunBackoffUntil) return false;          // round tm1: her last draw did not take (finding 7)
                Loadout l = ScLoadout.Current(e);
                string held = HeldItemName(e);
                if (l == null || !l.HasRanged || l.Melee == null || !string.Equals(held, l.Melee, StringComparison.OrdinalIgnoreCase)
                    || OrderedMelee(e) || UAI.UAITaskHarvestSdcs.HoldsJobTool(e.entityId))
                {
                    st.FirstSince = 0f;
                    st.GunHeldNoted = false;
                    return false;
                }
                EntityAlive t = e.GetAttackTarget();
                if (t == null || t.IsDead()) t = e.GetRevengeTarget();
                if (t == null || t.IsDead()) { st.FirstSince = 0f; st.GunHeldNoted = false; return false; }
                int count, need;
                bool over = ScConfig.Overwhelmed && IsOverwhelmed(e, out count, out need);
                bool far = (t.position - e.position).sqrMagnitude > XnpcCloseExitDist * XnpcCloseExitDist;
                // Round tm1 (I1): a melee-led member keeps her melee weapon for a target beyond 5 m - unless the target needs
                // the gun (melee-unsafe, armed, out of reach, past her leash: decision 10, then today's rule) or she is
                // overwhelmed (with the matched fight's extra attacker, decision 7)
                string needs = null;
                ScTeam.GunNeed kind = ScTeam.GunNeed.None;
                bool led = !over && ScTeam.MeleeLed(e);
                if (led)
                {
                    needs = ScTeam.NeedsGun(e, t, true, true, out kind);
                    if (needs == null)
                    {
                        if (far && !st.GunHeldNoted) { st.GunHeldNoted = true; Patches_Xnpc.Count("x5-team-gun-held"); }
                        st.FirstSince = 0f;
                        return false;
                    }
                }
                st.GunHeldNoted = false;
                // The Fable review of round tm1 (finding 1): a target out of her reach gets the gun at any distance. Her
                // adopted bout (CloseRange, I3) has no 0-XNPCCore exit any more - its wall, shoot-through and unreachable exits
                // were what handed the gun back - so inside 5 m nothing else would: a walker behind bars at 3 m left her idle
                // on melee. Only for out of reach: a melee-unsafe, armed or past-the-leash target keeps today's 5 m gate, or
                // 0-XNPCCore's 3 m swap would take her straight back to melee. No loop: MeleeFirst refuses this target while
                // the finding is kept (TeamUnreachableSeconds), and the draw starts the shared swap hold. Round tm2: a swing
                // block draws only when SwingBlockWhy trusts it; one dropped for a target attacking her up close is no Reach
                // here (NeedsGun returns null), and MeleeFirst takes her back to melee once the swap hold allows.
                bool reachDraw = led && kind == ScTeam.GunNeed.Reach;
                if (!far && !over && !reachDraw) { st.FirstSince = 0f; return false; }
                // round tm2: a target held out of reach by the no-swing clock has already been TeamNoSwingConfirmSeconds in
                // the making - no second wait (GunFirstSeconds) on top of it
                bool proven = reachDraw && !far && PairHeld(st, t.entityId, now);
                if (!proven)
                {
                    if (st.FirstSince <= 0f) { st.FirstSince = now; return false; }
                    if (now - st.FirstSince < GunFirstSeconds) return false;
                }
                // round tm1: the swap-hold clock MeleeFirst shares - her melee weapon came out less than TeamSwapHoldSeconds ago
                if (HeldBack(st, HoldToGun, now)) return false;
                ItemClass gun = ItemClass.GetItemClass(l.Ranged, true);
                if (ScAmmo.RangedAction(gun) == null || ScAmmo.Rounds(e, gun) <= 0) return false;   // nothing to shoot: melee it is
                st.FirstSince = 0f;
                rememberedField.SetValue(c, null);
                // 0-XNPCCore's own equip, as its re-arm does it: its magazine table, its chat draw, and X16's finalizer puts
                // the attachments back. SCore's UpdateWeapon by hand only when that cannot be bound.
                MethodBase equip = Patches_Xnpc.EquipByNameTarget();
                if (equip != null)
                {
                    object ok = equip.Invoke(c, new object[] { l.Ranged, "sc-gun-first" });
                    if (!(ok is bool) || !(bool)ok) return false;
                }
                else
                {
                    if (!ScNpc.UpdateWeapon(e, l.Ranged)) return false;
                    ScLoadout.ModsAfterEquip(e);                 // a gun SCore builds from its name has no attachments
                }
                // round tm1 (the Fable review, finding 7): the gun really is in her hand, or nothing is counted or held
                if (!EquipTook(e, l.Ranged, "gun first", ref st.GunBackoffUntil, now)) return false;
                if (over) st.GunUntil = now + ScConfig.OverwhelmedGunSeconds;
                Hold(st, HoldToMelee, now);                     // round tm1: MeleeFirst waits as long
                // round tm2 (the Fable review, finding 4): drawn for an attacker up close she could not swing at - she stands
                // and shoots it (no back-off, no 3 m swap) while it is after her within ReachGunDist
                float rxz;
                if (reachDraw && !far && AttacksHerUpClose(e, t, out rxz)) StartReachGun(st, e, t, rxz, now);
                gunFirstDraws++;
                Patches_Xnpc.Count(over ? "x5-gunfirst-overwhelmed" : "x5-gunfirst-range");
                if (needs != null) Patches_Xnpc.Count(reachDraw && !far ? "x5-team-gun-needed-close" : "x5-team-gun-needed");
                ScLog.Verbose(ScCommunity.DisplayName(e) + " (id " + e.entityId + ") takes the " + l.Ranged + " (gun first: "
                              + (over ? "overwhelmed" : "target at " + (t.position - e.position).magnitude.ToString("0.#") + " m") + (needs != null ? ", " + needs : "") + ")");
                return true;
            }
            catch (Exception ex)
            {
                ScLog.Once("arms-gunfirst", ex);
                return false;
            }
        }

        // ------------------------------------------------------------------ melee first (round tm1) ----

        /// <summary>Melee-first swaps and adopted 0-XNPCCore close-melee bouts this session (sc team, sc xnpc).</summary>
        private static int meleeFirstSwaps, boutsKept;

        /// <summary>
        /// Round tm1 (I2, the X5 prefix between RaiderLead and GunFirst): a melee-led member (ScTeam: a matched fight, decision
        /// 13 "pure melee") holding her pair's gun, outside 0-XNPCCore's close melee, not on a job's tool, with a target in
        /// reach (ScTeam.Target: her attack or revenge target, else the nearest enemy of the count that she sees - so she does
        /// not shoot it while it comes in) inside the area the count weighed (ScTeam.InArea - farther out she keeps what she
        /// holds) that does not need the gun, not overwhelmed and not in the overwhelmed rule's gun
        /// hold, with no pending recount below the leave margin (ScTeam.Steady), for TeamMeleeFirstSeconds: she takes her
        /// pair's melee weapon. Through 0-XNPCCore's own equip (its magazine table keeps the gun's rounds; X16's finalizer
        /// treats only "any-weapon-equip" as a hand-over, never this site), with what it remembers cleared first, so none of
        /// its restores brings the gun back. GunFirst's draw waits TeamSwapHoldSeconds after (the one swap-hold clock); this
        /// waits as long after a GunFirst draw. True when it swapped: 0-XNPCCore's close-range tick is skipped for this round.
        /// </summary>
        public static bool MeleeFirst(AvatarSDCSController ctrl, EntityAlive e)
        {
            try
            {
                if (!ScConfig.TeamMelee || e == null || e.isEntityRemote || e.inventory == null || e.Buffs == null) return false;
                if (!ScXnpc.IsBody(e) || ScXnpc.IsRaider(e)) return false;
                Watch st;
                if (!ScTeam.MeleeLed(e))
                {
                    if (watch.TryGetValue(e.entityId, out st)) st.MeleeSince = 0f;
                    return false;
                }
                AvatarSdcsNpcController c = ctrl as AvatarSdcsNpcController;
                // the Fable review: never while 0-XNPCCore's capture holds her hands
                if (c == null || !Bind() || CloseMelee(c) || HandsCaptured(c)) return false;
                float now = Time.time;
                if (now >= nextPrune) Prune(now);
                st = WatchOf(e, now);
                if (now < st.MeleeBackoffUntil) { st.MeleeSince = 0f; return false; }   // her last swap did not take (finding 7)
                Loadout l = ScLoadout.Current(e);
                string held = HeldItemName(e);
                if (l == null || !l.HasRanged || string.IsNullOrEmpty(l.Melee) || !string.Equals(held, l.Ranged, StringComparison.OrdinalIgnoreCase)
                    || UAI.UAITaskHarvestSdcs.HoldsJobTool(e.entityId) || !ScTeam.Steady(e))
                {
                    st.MeleeSince = 0f;
                    return false;
                }
                // a target the count weighed (inside its area) that does not need the gun; not in the overwhelmed gun hold
                EntityAlive t = ScTeam.Target(e);
                if (t == null || !ScTeam.InArea(e, t) || ScTeam.NeedsGun(e, t, false, true) != null || now < st.GunUntil
                    || ReachGunHolds(st, e, now)) { st.MeleeSince = 0f; return false; }   // round tm2: the reach gun hold
                int count, need;
                if (ScConfig.Overwhelmed && IsOverwhelmed(e, out count, out need)) { st.MeleeSince = 0f; return false; }
                if (st.MeleeSince <= 0f) { st.MeleeSince = now; return false; }
                if (now - st.MeleeSince < ScConfig.TeamMeleeFirstSeconds) return false;
                if (HeldBack(st, HoldToMelee, now)) return false;                                // GunFirst drew the gun just now
                if (ItemClass.GetItemClass(l.Melee, true) == null) return false;
                st.MeleeSince = 0f;
                rememberedField.SetValue(c, null);
                MethodBase equip = Patches_Xnpc.EquipByNameTarget();
                if (equip != null)
                {
                    object ok = equip.Invoke(c, new object[] { l.Melee, "sc-melee-first" });
                    if (!(ok is bool) || !(bool)ok) return false;
                }
                else if (!ScNpc.UpdateWeapon(e, l.Melee)) return false;
                // the Fable review (finding 7): the melee weapon really is in her hand, or nothing is counted or held
                if (!EquipTook(e, l.Melee, "melee first", ref st.MeleeBackoffUntil, now)) return false;
                Hold(st, HoldToGun, now);
                meleeFirstSwaps++;
                Patches_Xnpc.Count("x5-team-melee-first");
                ScLog.Verbose(ScCommunity.DisplayName(e) + " (id " + e.entityId + ") takes the " + l.Melee + " (melee first: a matched fight, "
                              + t.EntityName + " at " + (t.position - e.position).magnitude.ToString("0.#") + " m)");
                return true;
            }
            catch (Exception ex)
            {
                ScLog.Once("arms-meleefirst", ex);
                return false;
            }
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

        /// <summary>Round tm1: starts the shared swap-hold clock - the swap in direction dir waits TeamSwapHoldSeconds.</summary>
        private static void Hold(Watch st, int dir, float now)
        {
            st.HoldUntil = now + ScConfig.TeamSwapHoldSeconds;
            st.HoldDir = dir;
            st.HoldNoted = false;
        }

        /// <summary>Round tm1: is a swap in direction dir held by the shared clock right now (counted once per hold)?</summary>
        private static bool HeldBack(Watch st, int dir, float now)
        {
            if (st.HoldDir != dir || now >= st.HoldUntil) return false;
            if (!st.HoldNoted) { st.HoldNoted = true; Patches_Xnpc.Count("x5-team-swap-held"); }
            return true;
        }

        /// <summary>
        /// Round tm1 (I3): 0-XNPCCore's close-range swap put a melee-led member on her melee weapon - adopt its bout as
        /// RestoreGun's steps do (the close-melee flag, its start time and the weapon it would restore cleared), so its exit
        /// never brings the gun back. Not for a target that needs the gun: then its bout runs as today (its exit at 5 m
        /// restores the gun). The Fable review (finding 6): only with her pair's melee weapon in hand - a thrust weapon its
        /// swing check reached for, or a tool, stays 0-XNPCCore's bout (GunFirst only ever draws from the pair's melee
        /// weapon, so an adopted bout on anything else would never end). True when adopted.
        /// </summary>
        private static bool AdoptBout(AvatarSdcsNpcController c, EntityAlive e, float now)
        {
            Loadout l = ScLoadout.Current(e);
            if (l == null || string.IsNullOrEmpty(l.Melee) || !string.Equals(HeldItemName(e), l.Melee, StringComparison.OrdinalIgnoreCase)) return false;
            EntityAlive t = e.GetAttackTarget();
            if (t == null || t.IsDead()) t = e.GetRevengeTarget();
            if (t != null && !t.IsDead() && ScTeam.NeedsGun(e, t, true, true) != null) return false;
            string had = Remembered(c);
            rememberedField.SetValue(c, null);
            closeMeleeField.SetValue(c, false);
            closeSinceField.SetValue(c, 0f);
            Watch st = WatchOf(e, now);
            st.ExitWantSince = 0f;
            boutsKept++;
            Patches_Xnpc.Count("x5-team-bout-kept");
            ItemValue hv = e.inventory != null ? e.inventory.holdingItemItemValue : null;
            string held = hv != null && !hv.IsEmpty() && hv.ItemClass != null ? hv.ItemClass.GetItemName() : "melee weapon";
            ScLog.Verbose(ScCommunity.DisplayName(e) + " (id " + e.entityId + ") keeps the " + held + " after 0-XNPCCore's close-range swap (a matched fight; it would have restored '"
                          + (had ?? "-") + "')");
            return true;
        }

        /// <summary>
        /// Round tm1: why this target is out of her melee weapon's reach, or null - the raiders' OutOfReach without its
        /// distance clause, made safe for a gun holder: standing above her; marked unreachable by 0-XNPCCore (ScReach);
        /// beyond melee reach, her path leads to it and ends short; or (onMelee: she holds her melee weapon, so she is the one
        /// who would walk there) no path and none planned while really blocked. A path that leads elsewhere (a back-off) says
        /// nothing, and plain "no path" is never enough for a gun holder: she plans none. remember (X5 only) keeps a finding
        /// (not "above": that is checked live) for TeamUnreachableSeconds, so a target behind a wall is not tried again at once.
        /// Round tm2: a block in her swing counts only as SwingBlockWhy trusts it. Against a target that attacks her up close
        /// (symmetric reach) it waits on the no-swing clock; once that ran out the pair is held out of reach (Pair.HeldUntil,
        /// per target, doubling). A kept finding is dropped (X5) or looked past (a read) only on positive evidence.
        /// </summary>
        internal static string ReachWhy(EntityAlive e, EntityAlive t, bool onMelee, bool remember)
        {
            float now = Time.time;
            Watch st;
            watch.TryGetValue(e.entityId, out st);
            // round tm2: 0-XNPCCore would not let her swing at this attacker for TeamNoSwingConfirmSeconds - held out of reach
            Pair hp = PeekPair(st, t.entityId);
            if (hp != null && now < hp.HeldUntil)
                return hp.HeldWhy + " (held " + (hp.HeldUntil - now).ToString("0") + " s more)";
            float xz;
            bool mutual = AttacksHerUpClose(e, t, out xz);
            if (st != null && st.UnreachId == t.entityId && now < st.UnreachUntil)
            {
                // round tm2 (symmetric reach): a kept block in her swing gives way for a target attacking her up close only on
                // positive evidence (DropEvidence) - a stale read is "unknown" and keeps it (the Fable review, finding 6)
                if (!(mutual && st.UnreachSwing && DropEvidence(st, e, t, now)))
                    return st.UnreachWhy + " (kept " + (st.UnreachUntil - now).ToString("0") + " s more)";
                if (remember) DropKept(e, t, st, xz, now);   // X5: dropped; a read (the console) only looks past it
            }
            Vector3 d = t.position - e.position;
            if (d.y > ReachAbove) return "above her (" + d.y.ToString("0.#") + " m)";
            string why = null;
            bool swing = false, held = false;
#if SC_V34
            if (ScReach.Marked(e, t)) why = "0-XNPCCore marked it melee-unreachable";
#endif
            // the Fable review (finding 1): the block 0-XNPCCore's own swing check found between the two - solid (its bout's
            // "wall" exit) or see-through (bars, a fence, glass: no swing passes). Read only where that check runs every tick,
            // so the read is fresh: for a gun holder within its close-range distance (3 m), for a melee holder within her
            // weapon's reach (2 m when it cannot be read). Round tm2: and only as SwingBlockWhy trusts it.
            if (why == null)
            {
                float trust = XnpcCloseEnterDist;
                if (onMelee)
                {
                    float weapon;
                    ScMelee.Reach(e, 0, out weapon);
                    trust = weapon > 0f ? Math.Min(weapon, XnpcCloseEnterDist) : 2f;
                }
                if (d.magnitude <= trust)
                {
                    why = SwingBlockWhy(e, t, mutual, xz, remember, now, out held);
                    swing = why != null;
                }
                else if (remember) ClearPend(st);       // round tm2: no read this time - the run of agreeing reads is broken
            }
            if (why == null && d.magnitude > MeleeReach)
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
            // round tm2: a pair held by the no-swing clock keeps its own memory (Pair.HeldUntil) - not the one slot below,
            // which a second attacker would take over
            if (why != null && remember && !held && ScConfig.TeamUnreachableSeconds > 0f)
            {
                st = WatchOf(e, now);
                if (st.UnreachId != t.entityId || now >= st.UnreachUntil) Patches_Xnpc.Count("x5-team-unreachable");
                st.UnreachId = t.entityId;
                st.UnreachUntil = now + ScConfig.TeamUnreachableSeconds;
                st.UnreachWhy = why;
                st.UnreachSwing = swing;                 // round tm2: symmetric reach may drop this one
                if (swing) PairOf(st, t.entityId, now).Noted = 0;   // a new finding: its drop is logged afresh
            }
            return why;
        }

        /// <summary>A path whose goal is this close (XZ) to the target leads to it (a moving target drifts off the goal).</summary>
        private const float PathOnTarget = 3f;

        // ------------------------------------------------------------------ the swing block (round tm2) ----

        /// <summary>
        /// Round tm2: a swing-check read is trusted this long after 0-XNPCCore made it. Its Evaluate restamps an episode at
        /// most once per UAIBase.ActionChoiceDelay (0.2 s: inside that it answers from the episode), and its close-range tick
        /// - which re-evaluates every 0.2 s while she holds a gun within 3 m or a swing weapon within its reach, and runs right
        /// AFTER SC's X5 prefix - leaves a live finding at most ActionChoiceDelay + one tick old (0.4 s) when the prefix reads
        /// it; + 0.1 s for frame jitter = 0.5 s. Anything older is a leftover nobody has evaluated since: SdcsMeleeReach
        /// keeps an episode until a later Evaluate finds the line clear.
        /// </summary>
        private static float SwingFreshSeconds { get { return Mathf.Clamp(UAI.UAIBase.ActionChoiceDelay, 0.05f, 1f) + CloseTickSeconds + 0.1f; } }
        /// <summary>0-XNPCCore's close-range tick (and so SC's X5 prefix) runs every 0.2 s.</summary>
        private const float CloseTickSeconds = 0.2f;
        /// <summary>Round tm2: with the episode table unbound, a second BlockKind read must come at least this much later.</summary>
        private const float FallbackReadGap = 0.2f;
        /// <summary>
        /// Round tm2: two agreeing reads further apart than this are not "in a row" - 2 s (the Fable review, finding 7): a
        /// thrust weapon's check runs only when 0-XNPCCore's UAI re-decides (UaiDecisionSeconds, 0.6 s), and must still confirm.
        /// </summary>
        private const float PendMaxGap = 2f;
        /// <summary>Round tm2 (symmetric reach): her attacker this close on the ground plane, and at most this far above or below her.</summary>
        private const float MutualReachXZ = 2.2f, MutualReachDy = 1f;
        /// <summary>Round tm2: one X5 tick adds at most this much to the no-swing clock (5 Hz; a gap - a capture - is one tick).</summary>
        private const float NoSwingTickMax = 0.3f;
        /// <summary>Round tm2: the hold after the no-swing clock ran out doubles per time for the pair, up to this (the Fable review, finding 8).</summary>
        private const float NoSwingHoldMax = 180f;
        /// <summary>Round tm2: the reach gun hold lasts while its target is after her within ReachGunDist, plus ReachGunGrace.</summary>
        private const float ReachGunDist = 4f, ReachGunGrace = 1.5f;
        /// <summary>Round tm2: at most this many targets in a member's Pairs (pruned: unseen 10 s and not held first, then the oldest).</summary>
        private const int PairsMax = 16;
        /// <summary>Round tm2: the reasons FirstNote logs once per pair - a stale read, a choice that lets her swing, symmetric reach.</summary>
        private const int NoteStale = 1, NoteSwings = 2, NoteMutual = 4;
        private const string KindSeeThrough = "see-through";

        /// <summary>
        /// Round tm2: her state against one target (Watch.Pairs). NoSwingSum: the no-swing clock - seconds of X5 ticks in a row
        /// with a fresh read of 0-XNPCCore's swing check refusing the swing (NoSwingLast: the last tick it looked); a fresh
        /// read that is clear or lets her swing resets it, a stale one leaves it, a target switch or a hit never touch it.
        /// HeldUntil / HeldWhy: the clock ran out against an attacker up close - out of reach until then (Fails: how often).
        /// Noted: the FirstNote bits. Seen: the last X5 tick that used it.
        /// </summary>
        private sealed class Pair
        {
            public float Seen, NoSwingSum, NoSwingLast, HeldUntil;
            public int Noted, Fails;
            public string HeldWhy;
        }

        /// <summary>Round tm2: one read of 0-XNPCCore's swing check for her and a target (ReadSwing).</summary>
        private struct SwingRead
        {
            /// <summary>"solid" or "see-through".</summary>
            public string Kind;
            /// <summary>0-XNPCCore's choice for it ("NoSwing", "Break", ...); null from BlockKind alone.</summary>
            public string Choice;
            /// <summary>In reach: the choice lets her swing (Swing, Break) or open the door (OpenDoor) - not BarsBreak.</summary>
            public bool Swings;
            /// <summary>Break through bars (see-through) while her pair has a gun with rounds: out of reach - the gun, not the player's bars.</summary>
            public bool BarsBreak;
            /// <summary>When Evaluate made it (Time.time), and how long ago.</summary>
            public float At, Age;
            /// <summary>The block lay on the straight line to its chest (ArcRays 0), not on a side ray of her swing arc.</summary>
            public bool Between;
            /// <summary>Read from the episode table (else from BlockKind: no age, no choice).</summary>
            public bool FromEpisode;
        }

        /// <summary>
        /// Round tm2: "out of reach (a solid|see-through block in her swing; 0-XNPCCore: NoSwing|Shoot)" when 0-XNPCCore's
        /// swing check (SdcsMeleeReach) has a block between her and this target she cannot swing past, else null. Trusted only:
        ///   fresh - the episode made within SwingFreshSeconds (a leftover episode, read once, was kept 45 s);
        ///   by 0-XNPCCore's own decision, not the kind - its choice refuses the swing (AttackGateRules.SwingAllowed false:
        ///     NoSwing or Shoot); Break, Swing and OpenDoor are in reach - except Break through bars while her pair has a gun
        ///     with rounds (BarsBreak: she shoots instead of smashing the player's bars - the Fable review, finding 5);
        ///   against a target that attacks her up close (symmetric reach) only once the no-swing clock has run
        ///     TeamNoSwingConfirmSeconds - so a flicker of her swing arc does not draw the gun, while a lasting "no swing" (her
        ///     MeleeAttack scores 0 on it: utilityai.xml of 0-XNPCCore) does. Then the pair is held (HoldNoSwing, held = true).
        ///     Bars right between them (see-through on the straight line) are no case for symmetric reach: it cannot hit her
        ///     either. A solid block on that line (the floor or a step in front of a crawler) is (the Fable review, finding 3);
        ///   else two reads in a row (X5): a second agreeing read of a NEWER evaluation (its At), or with the table unbound a
        ///     second BlockKind read FallbackReadGap later. A stale read in between says nothing either way.
        /// remember (X5): runs the no-swing clock, and a finding set aside is counted and logged once per pair (FirstNote,
        /// Noted). A read (the console) writes nothing.
        /// </summary>
        private static string SwingBlockWhy(EntityAlive e, EntityAlive t, bool mutual, float xz, bool remember, float now, out bool held)
        {
            held = false;
            Watch st;
            if (remember) st = WatchOf(e, now);
            else watch.TryGetValue(e.entityId, out st);
            Pair p = remember ? PairOf(st, t.entityId, now) : PeekPair(st, t.entityId);
            SwingRead r;
            bool found = ReadSwing(e, t, now, out r);
            if (remember) TickNoSwing(p, found, r, now);
            if (!found)
            {
                if (remember) ClearPend(st);            // no block (the line is clear, or a door): a read that does not agree
                return null;
            }
            if (r.FromEpisode && r.Age > SwingFreshSeconds)
            {
                // a stale read says nothing either way: the run of agreeing reads and the clock are left as they are
                if (remember && FirstNote(p, NoteStale))
                    Noted("x5-team-reach-stale", Name(e) + ": 0-XNPCCore's swing check last found a " + r.Kind + " block between her and " + Who(t) + " "
                          + r.Age.ToString("0.0") + " s ago - too old to trust (under " + SwingFreshSeconds.ToString("0.0") + " s), not out of reach");
                return null;
            }
            if (r.Swings)
            {
                if (remember)
                {
                    ClearPend(st);
                    if (FirstNote(p, NoteSwings))
                        Noted("x5-team-reach-swing-allowed", Name(e) + ": a " + r.Kind + " block in her swing at " + Who(t)
                              + ", but 0-XNPCCore still lets her swing (" + r.Choice + ") - not out of reach");
                }
                return null;
            }
            string choice = r.Choice != null ? "; 0-XNPCCore: " + r.Choice + (r.BarsBreak ? " - not through the bars while she has her gun" : "") : "";
            if (mutual && !(r.Kind == KindSeeThrough && r.Between))
            {
                float sum = p != null ? p.NoSwingSum : 0f;
                if (sum < ScConfig.TeamNoSwingConfirmSeconds)
                {
                    if (remember)
                    {
                        ClearPend(st);
                        if (FirstNote(p, NoteMutual))
                            Noted("x5-team-reach-symmetric", Name(e) + ": " + Who(t) + " attacks her from " + xz.ToString("0.0") + " m - a " + r.Kind
                                  + " block in her swing waits " + ScConfig.TeamNoSwingConfirmSeconds.ToString("0.0#") + " s of 'no swing' before it counts (if it can hit her, she can hit it)");
                    }
                    return null;
                }
                // the no-swing clock ran out: 0-XNPCCore keeps refusing the swing at an attacker up close - out of reach after all
                string why = "out of reach (0-XNPCCore has not let her swing at it for " + sum.ToString("0.0") + " s: a " + r.Kind + " block in her swing" + choice + ")";
                if (remember) HoldNoSwing(st, p, e, t, xz, why, now);
                held = true;
                return why;
            }
            string whyBlock = "out of reach (a " + r.Kind + " block in her swing" + choice + ")";
            if (!remember)
                return st != null && st.SwingPendSure && st.SwingPendId == t.entityId ? whyBlock : whyBlock + " - seen once, a second read in a row counts it";
            return Agrees(st, t, r, now) ? whyBlock : null;
        }

        /// <summary>
        /// Round tm2 (the Fable review, findings 1-3): one X5 tick of the pair's no-swing clock. found/r: this tick's read.
        /// Clear (no episode, a door) or a swing she may take resets it; stale leaves it; a fresh refusal adds the time since
        /// the pair was last looked at (at most NoSwingTickMax). Health and target switches play no part.
        /// </summary>
        private static void TickNoSwing(Pair p, bool found, SwingRead r, float now)
        {
            if (p == null) return;
            float dt = Mathf.Clamp(now - p.NoSwingLast, 0f, NoSwingTickMax);
            p.NoSwingLast = now;
            if (!found || r.Swings) { p.NoSwingSum = 0f; return; }
            if (r.FromEpisode && r.Age > SwingFreshSeconds) return;
            p.NoSwingSum += dt;
        }

        /// <summary>
        /// Round tm2 (X5): the no-swing clock ran out against an attacker up close - the pair is held out of reach for
        /// TeamUnreachableSeconds, doubled each time it happens again for the pair, up to NoSwingHoldMax (the Fable review,
        /// finding 8); ReachTick keeps the hold on while her gun is on the target. Counted (x5-team-reach-noswing) and logged
        /// each time. Nothing while a hold is on.
        /// </summary>
        private static void HoldNoSwing(Watch st, Pair p, EntityAlive e, EntityAlive t, float xz, string why, float now)
        {
            if (now < p.HeldUntil) return;
            float span = Mathf.Min(ScConfig.TeamUnreachableSeconds * (1 << Math.Min(p.Fails, 8)), NoSwingHoldMax);
            p.Fails++;
            p.HeldUntil = now + span;
            p.HeldWhy = why;
            p.Noted = 0;                                // its later set-asides are logged afresh
            ClearPend(st);
            Patches_Xnpc.Count("x5-team-reach-noswing");
            ScLog.Verbose(Name(e) + ": " + Who(t) + " attacks her from " + xz.ToString("0.0") + " m, and 0-XNPCCore has not let her swing at it for "
                          + p.NoSwingSum.ToString("0.0") + " s - out of reach, held " + span.ToString("0") + " s (and while her gun is on it; time " + p.Fails + " for it)");
        }

        /// <summary>
        /// Round tm2: the second read in a row. True once a read agrees with the run (same target and kind, within PendMaxGap
        /// of the last) on newer evidence than the first: a newer evaluation of 0-XNPCCore's (its At) - the same episode read
        /// twice is one read - or, with the table unbound, FallbackReadGap later. A confirmed run stays so while reads agree.
        /// </summary>
        private static bool Agrees(Watch st, EntityAlive t, SwingRead r, float now)
        {
            float stamp = r.FromEpisode ? r.At : now;
            if (st.SwingPendId != t.entityId || st.SwingPendKind != r.Kind || now - st.SwingPendSeen > PendMaxGap)
            {
                st.SwingPendId = t.entityId;
                st.SwingPendKind = r.Kind;
                st.SwingPendAt = stamp;
                st.SwingPendSeen = now;
                st.SwingPendSure = false;
                return false;
            }
            st.SwingPendSeen = now;
            if (!st.SwingPendSure)
            {
                bool newer = r.FromEpisode ? stamp > st.SwingPendAt : now - st.SwingPendAt >= FallbackReadGap;
                if (!newer) return false;               // the same evaluation again, or too soon: still one read
                st.SwingPendSure = true;
            }
            st.SwingPendAt = stamp;
            return true;
        }

        private static void ClearPend(Watch st)
        {
            if (st == null) return;
            st.SwingPendId = -1;
            st.SwingPendKind = null;
            st.SwingPendSure = false;
        }

        /// <summary>Round tm2: her Pair for this target, made if new (X5 only: it writes). Caps the map at PairsMax.</summary>
        private static Pair PairOf(Watch st, int id, float now)
        {
            Pair p;
            if (!st.Pairs.TryGetValue(id, out p))
            {
                if (st.Pairs.Count >= PairsMax) PrunePairs(st, now);
                st.Pairs[id] = p = new Pair { NoSwingLast = now };
            }
            p.Seen = now;
            return p;
        }

        /// <summary>Round tm2: her Pair for this target, or null. A pure read.</summary>
        private static Pair PeekPair(Watch st, int id)
        {
            Pair p;
            return st != null && st.Pairs.TryGetValue(id, out p) ? p : null;
        }

        /// <summary>Round tm2: makes room in a full Pairs - first every target unseen 10 s with no hold on, then the oldest.</summary>
        private static void PrunePairs(Watch st, float now)
        {
            var gone = new List<int>();
            foreach (var kv in st.Pairs) if (now - kv.Value.Seen > 10f && now >= kv.Value.HeldUntil) gone.Add(kv.Key);
            foreach (int id in gone) st.Pairs.Remove(id);
            while (st.Pairs.Count >= PairsMax)
            {
                int oldest = -1;
                float seen = float.MaxValue;
                foreach (var kv in st.Pairs) if (kv.Value.Seen < seen) { seen = kv.Value.Seen; oldest = kv.Key; }
                st.Pairs.Remove(oldest);
            }
        }

        /// <summary>Round tm2: this pair is held out of reach by the no-swing clock right now. A pure read.</summary>
        private static bool PairHeld(Watch st, int id, float now)
        {
            Pair p = PeekPair(st, id);
            return p != null && now < p.HeldUntil;
        }

        /// <summary>
        /// Round tm2: reads 0-XNPCCore's swing-check episode for her and this target - false when there is none or it is no
        /// solid or see-through block (a door: she opens it). Never runs the check, and writes nothing (it serves the pure
        /// reads too): with the table unbound, or when a read of it throws (logged, Once), it falls back to BlockKind - a kind
        /// with no age and no choice.
        /// </summary>
        private static bool ReadSwing(EntityAlive e, EntityAlive t, float now, out SwingRead r)
        {
            r = new SwingRead();
            if (episodesField != null)
            {
                try
                {
                    var table = episodesField.GetValue(null) as System.Collections.IDictionary;
                    object ep = table != null ? table[episodeKey.Invoke(null, new object[] { e.entityId, t.entityId })] : null;
                    if (ep == null) return false;
                    r.Kind = KindName(episodeKindField.GetValue(ep));
                    if (r.Kind == null) return false;
                    object choice = episodeChoiceField.GetValue(ep);
                    r.Choice = choice != null ? choice.ToString() : null;
                    r.Swings = ChoiceSwings(choice);
                    // the Fable review (finding 5): 0-XNPCCore's Break through bars would have her smash the player's bars -
                    // with a gun to shoot through them, that is out of reach. A solid block with Break stays in reach.
                    if (r.Swings && r.Kind == KindSeeThrough && r.Choice == "Break" && PairGunReady(e)) { r.Swings = false; r.BarsBreak = true; }
                    r.At = (float)episodeAtField.GetValue(ep);
                    r.Age = now - r.At;
                    r.Between = episodeArcField != null && (int)episodeArcField.GetValue(ep) <= 0;
                    r.FromEpisode = true;
                    return true;
                }
                catch (Exception ex)
                {
                    ScLog.Once("arms-swing-episodes", ex);
                    r = new SwingRead();
                }
            }
            if (blockKind == null) return false;
            try
            {
                r.Kind = KindName(blockKind.Invoke(null, new object[] { e, t }));
                return r.Kind != null;
            }
            catch (Exception ex) { ScLog.Once("arms-blockkind", ex); return false; }
        }

        /// <summary>Round tm2: her pair has a gun with rounds in her stores (BarsBreak). A pure read; false when it cannot be read.</summary>
        private static bool PairGunReady(EntityAlive e)
        {
            try
            {
                Loadout l = ScLoadout.Current(e);
                if (l == null || !l.HasRanged) return false;
                ItemClass gun = ItemClass.GetItemClass(l.Ranged, true);
                return ScAmmo.RangedAction(gun) != null && ScAmmo.Rounds(e, gun) > 0;
            }
            catch (Exception ex) { ScLog.Once("arms-pair-gun", ex); return false; }
        }

        /// <summary>"solid" or "see-through" for 0-XNPCCore's ReachBlock, else null (none, a door).</summary>
        private static string KindName(object kind)
        {
            string k = kind != null ? kind.ToString() : null;
            return k == "Solid" ? "solid" : k == "SeeThrough" ? KindSeeThrough : null;
        }

        /// <summary>
        /// Round tm2: does 0-XNPCCore's choice let her swing? Its melee attack's own gate, AttackGateRules.SwingAllowed: Swing
        /// and Break yes, Shoot and NoSwing no. OpenDoor, which SwingAllowed refuses, is in reach all the same: she opens the
        /// door (OpenChosenDoor). A choice that cannot be read finds nothing.
        /// </summary>
        private static bool ChoiceSwings(object choice)
        {
            string name = choice != null ? choice.ToString() : null;
            if (name == null || name == "OpenDoor") return true;
            if (swingAllowed != null)
            {
                try { return (bool)swingAllowed.Invoke(null, new[] { choice }); }
                catch (Exception ex) { ScLog.Once("arms-swingallowed", ex); }
            }
            return name != "NoSwing" && name != "Shoot";
        }

        /// <summary>
        /// Round tm2 (symmetric reach): this target's attack target is her, within MutualReachXZ on the ground plane and
        /// MutualReachDy up or down - if it can hit her, she can hit it. A pure read; xz: its distance on the ground plane.
        /// </summary>
        internal static bool AttacksHerUpClose(EntityAlive e, EntityAlive t, out float xz)
        {
            xz = 0f;
            if (e == null || t == null || t.IsDead() || t.GetAttackTarget() != e) return false;
            Vector3 d = t.position - e.position;
            xz = Mathf.Sqrt(d.x * d.x + d.z * d.z);
            return xz <= MutualReachXZ && Mathf.Abs(d.y) <= MutualReachDy;
        }

        /// <summary>
        /// Round tm2 (the Fable review, finding 6): positive evidence that a kept block in her swing no longer stands between
        /// her and an attacker up close - no episode (0-XNPCCore's check found the line clear), or a fresh read she may swing
        /// past, or one symmetric reach still sets aside (not bars right between them, and the no-swing clock short of
        /// TeamNoSwingConfirmSeconds). A stale read is "unknown": false. A pure read.
        /// </summary>
        private static bool DropEvidence(Watch st, EntityAlive e, EntityAlive t, float now)
        {
            SwingRead r;
            if (!ReadSwing(e, t, now, out r)) return true;
            if (r.FromEpisode && r.Age > SwingFreshSeconds) return false;
            if (r.Swings) return true;
            if (r.Kind == KindSeeThrough && r.Between) return false;
            Pair p = PeekPair(st, t.entityId);
            return p == null || p.NoSwingSum < ScConfig.TeamNoSwingConfirmSeconds;
        }

        /// <summary>
        /// Round tm2 (X5): symmetric reach drops a kept swing-block finding on positive evidence, and ends the reach gun hold
        /// on that target - counted and logged once per pair (FirstNote, Noted).
        /// </summary>
        private static void DropKept(EntityAlive e, EntityAlive t, Watch st, float xz, float now)
        {
            string was = st.UnreachWhy;
            st.UnreachId = -1;
            st.UnreachUntil = 0f;
            st.UnreachWhy = null;
            st.UnreachSwing = false;
            ClearPend(st);
            // the block gave way (bars broken, a wall no longer in her swing): no reason left to stand and shoot it
            if (st.ReachGunId == t.entityId) { st.ReachGunId = -1; st.ReachGunUntil = 0f; }
            if (FirstNote(PairOf(st, t.entityId, now), NoteMutual))
                Noted("x5-team-reach-symmetric", Name(e) + " drops the kept '" + was + "' for " + Who(t) + ": it attacks her from "
                      + xz.ToString("0.0") + " m (if it can hit her, she can hit it)");
        }

        /// <summary>
        /// Round tm2 (the X5 prefix, after the team recount; nothing with TeamMelee off). For her own target within 3 m: the
        /// no-swing clock ticks, and a pair held by it stays held while her gun is on it (the Fable review, finding 8). The
        /// reach gun hold stays on while its target is after her within ReachGunDist and she holds her gun (else it lapses
        /// after ReachGunGrace). A kept swing-block finding is dropped the moment its target attacks her up close with
        /// positive evidence (DropEvidence) - not only when a swap rule next asks about it.
        /// </summary>
        public static void ReachTick(EntityAlive e)
        {
            try
            {
                if (!ScConfig.TeamMelee || e == null || e.world == null) return;
                Watch st;
                if (!watch.TryGetValue(e.entityId, out st)) return;
                float now = Time.time;
                EntityAlive own = e.GetAttackTarget();
                if (own == null || own.IsDead()) own = e.GetRevengeTarget();
                if (own != null && own.IsDead()) own = null;
                if (own != null)
                {
                    if ((own.position - e.position).sqrMagnitude <= XnpcCloseEnterDist * XnpcCloseEnterDist)
                    {
                        SwingRead r;
                        bool found = ReadSwing(e, own, now, out r);
                        TickNoSwing(PairOf(st, own.entityId, now), found, r, now);
                    }
                    Pair hp = PeekPair(st, own.entityId);
                    if (hp != null && now < hp.HeldUntil && HoldsRangedWeapon(e)) hp.HeldUntil = Mathf.Max(hp.HeldUntil, now + 1f);
                }
                if (st.ReachGunId >= 0)
                {
                    EntityAlive g = e.world.GetEntity(st.ReachGunId) as EntityAlive;
                    if (g != null && !g.IsDead() && g.GetAttackTarget() == e && HoldsRangedWeapon(e)
                        && (g.position - e.position).sqrMagnitude <= ReachGunDist * ReachGunDist)
                        st.ReachGunUntil = now + ReachGunGrace;
                    else if (now >= st.ReachGunUntil) st.ReachGunId = -1;
                }
                if (st.UnreachId < 0 || !st.UnreachSwing || now >= st.UnreachUntil) return;
                EntityAlive t = e.world.GetEntity(st.UnreachId) as EntityAlive;
                float xz;
                if (t != null && !PairHeld(st, t.entityId, now) && AttacksHerUpClose(e, t, out xz) && DropEvidence(st, e, t, now)) DropKept(e, t, st, xz, now);
            }
            catch (Exception ex) { ScLog.Once("arms-reachtick", ex); }
        }

        /// <summary>
        /// Round tm2 (X5, GunFirst): the gun was drawn for an attacker up close she could not swing at - the reach gun hold
        /// starts: BackupKeepDistance and BackupRanged score 0 for it (HoldingGunUpClose, ScTeam.HoldsGround), 0-XNPCCore's 3 m
        /// swap and MeleeFirst wait (CloseRange, MeleeFirst), so she stands and shoots it. Counted once per hold.
        /// </summary>
        private static void StartReachGun(Watch st, EntityAlive e, EntityAlive t, float xz, float now)
        {
            bool fresh = st.ReachGunId != t.entityId || now >= st.ReachGunUntil;
            st.ReachGunId = t.entityId;
            st.ReachGunUntil = now + ReachGunGrace;
            if (!fresh) return;
            Patches_Xnpc.Count("x5-team-reach-gun-hold");
            ScLog.Verbose(Name(e) + " stands and shoots " + Who(t) + " (it attacks her from " + xz.ToString("0.0") + " m, and she cannot swing at it): no back-off, "
                          + "no swap to melee while it is after her within " + ReachGunDist.ToString("0") + " m");
        }

        /// <summary>
        /// Round tm2 (X5): true the first time this reason sets aside a swing-block finding for this pair (Noted then counts
        /// and logs it) - so the message is only built once. A newly kept or held finding for the target starts it afresh.
        /// </summary>
        private static bool FirstNote(Pair p, int reason)
        {
            if (p == null || (p.Noted & reason) != 0) return false;
            p.Noted |= reason;
            return true;
        }

        /// <summary>Round tm2 (X5): a swing-block finding set aside - its x5-team-reach-* counter and a verbose line.</summary>
        private static void Noted(string counter, string msg)
        {
            Patches_Xnpc.Count(counter);
            ScLog.Verbose(msg + " (logged once per pair)");
        }

        private static string Name(EntityAlive e) { return ScCommunity.DisplayName(e) + " (id " + e.entityId + ")"; }
        private static string Who(EntityAlive t) { return t.EntityName + " (id " + t.entityId + ")"; }

        /// <summary>Round tm1 (the Fable review, finding 5): ReachWhy's live "above her" test, as a pure read.</summary>
        public static bool IsAbove(EntityAlive e, EntityAlive t)
        {
            return e != null && t != null && t.position.y - e.position.y > ReachAbove;
        }

        /// <summary>
        /// Round tm1: a pure read of ReachWhy's memory - this target was found out of her reach and still is kept so. Round
        /// tm2: a pair held by the no-swing clock counts; a kept block in her swing does not count against a target that
        /// attacks her up close when there is positive evidence it gave way (DropEvidence; the X5 path drops it next tick).
        /// </summary>
        public static bool RememberedOutOfReach(EntityAlive e, EntityAlive t)
        {
            Watch st;
            if (e == null || t == null || !watch.TryGetValue(e.entityId, out st)) return false;
            float now = Time.time;
            if (PairHeld(st, t.entityId, now)) return true;
            if (st.UnreachId != t.entityId || now >= st.UnreachUntil) return false;
            float xz;
            return !(st.UnreachSwing && AttacksHerUpClose(e, t, out xz) && DropEvidence(st, e, t, now));
        }

        /// <summary>Round tm1: her swap state for sc team / sc xnpc - the melee-first clock, the shared hold, the gun hold, a target kept out of reach.</summary>
        public static string TeamState(EntityAlive e)
        {
            if (e == null) return "-";
            ItemValue hv = e.inventory != null ? e.inventory.holdingItemItemValue : null;
            string held = hv != null && !hv.IsEmpty() && hv.ItemClass != null ? hv.ItemClass.GetItemName() : "nothing";
            var sb = new StringBuilder("holding ").Append(held);
            Loadout l = ScLoadout.Current(e);
            if (l != null) sb.Append(" (pair ").Append(l.Ranged ?? "-").Append(" / ").Append(l.Melee ?? "-").Append(')');
            if (OrderedMelee(e)) sb.Append(", YOU ORDERED HER MELEE WEAPON");
            if (OrderedRanged(e)) sb.Append(", YOU ORDERED HER GUN");
            float now = Time.time;
            Watch st;
            if (watch.TryGetValue(e.entityId, out st))
            {
                if (st.MeleeSince > 0f) sb.Append(", melee first in ").Append(Math.Max(0f, ScConfig.TeamMeleeFirstSeconds - (now - st.MeleeSince)).ToString("0.0")).Append(" s");
                if (now < st.HoldUntil && st.HoldDir != HoldNone)
                    sb.Append(", ").Append(st.HoldDir == HoldToGun ? "gun first" : "melee first").Append(" held ").Append((st.HoldUntil - now).ToString("0.0")).Append(" s more (swap hold)");
                if (now < st.GunUntil) sb.Append(", overwhelmed gun hold ").Append((st.GunUntil - now).ToString("0.0")).Append(" s more");
                if (st.UnreachId >= 0 && now < st.UnreachUntil)
                {
                    sb.Append(", target #").Append(st.UnreachId).Append(" kept out of reach ").Append((st.UnreachUntil - now).ToString("0")).Append(" s more (").Append(st.UnreachWhy).Append(')');
                    // round tm2: symmetric reach looks past it (the X5 tick drops it)
                    EntityAlive ut = st.UnreachSwing && e.world != null ? e.world.GetEntity(st.UnreachId) as EntityAlive : null;
                    float uxz;
                    if (ut != null && AttacksHerUpClose(e, ut, out uxz) && DropEvidence(st, e, ut, now))
                        sb.Append(" - SET ASIDE: it attacks her from ").Append(uxz.ToString("0.0")).Append(" m");
                }
                // round tm2: the per-target state - holds after the no-swing clock ran out, and running clocks
                foreach (var kv in st.Pairs)
                {
                    Pair p = kv.Value;
                    if (now < p.HeldUntil)
                        sb.Append(", #").Append(kv.Key).Append(" HELD OUT OF REACH ").Append((p.HeldUntil - now).ToString("0")).Append(" s more (0-XNPCCore would not let her swing at it; time ")
                          .Append(p.Fails).Append(')');
                    else if (p.NoSwingSum > 0f && now - p.NoSwingLast <= 1f)
                        sb.Append(", no-swing clock on #").Append(kv.Key).Append(' ').Append(p.NoSwingSum.ToString("0.0")).Append(" of ")
                          .Append(ScConfig.TeamNoSwingConfirmSeconds.ToString("0.0#")).Append(" s");
                }
                if (ReachGunHolds(st, e, now)) sb.Append(", STANDS AND SHOOTS #").Append(st.ReachGunId).Append(" (reach gun hold, ").Append((st.ReachGunUntil - now).ToString("0.0")).Append(" s grace)");
                if (st.SwingPendId >= 0 && !st.SwingPendSure && now - st.SwingPendSeen <= PendMaxGap)
                    sb.Append(", a ").Append(st.SwingPendKind).Append(" block in her swing at #").Append(st.SwingPendId).Append(" seen once (a second read in a row counts it)");
                if (now < st.MeleeBackoffUntil) sb.Append(", MELEE FIRST BACKED OFF ").Append((st.MeleeBackoffUntil - now).ToString("0")).Append(" s more (an equip did not take)");
                if (now < st.GunBackoffUntil) sb.Append(", GUN FIRST BACKED OFF ").Append((st.GunBackoffUntil - now).ToString("0")).Append(" s more (an equip did not take)");
            }
            AvatarSdcsNpcController c = e.emodel != null ? e.emodel.avatarController as AvatarSdcsNpcController : null;
            if (c != null && bound && HandsCaptured(c)) sb.Append(", HANDS CAPTURED (0-XNPCCore: no swap rule acts)");
            // the Fable review: a read - the console never recounts the attackers, so it never moves the sustain clocks
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

        /// <summary>Round tm1: the swap counters for sc team.</summary>
        public static string TeamReport()
        {
            return meleeFirstSwaps + " melee-first swap(s), " + boutsKept + " close-melee bout(s) kept on melee";
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
                NoteOrder(npc, itemName, route);             // round n08: a melee order stops gun first
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
        /// items. Shared by 0-XNPCCore's weapon menu (OnSwap, members) and SC's own "Use the weapons I gave you" (UseHanded).
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
                ScCommunity.Tell(player, string.Format(Localization.Get("scSwapKept"), name, ItemLabel(token), ItemLabel(old), Localization.Get(WhereKey(where))), "");
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
                ItemValue h = npc.inventory != null ? npc.inventory.holdingItemItemValue : null;
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
        /// The weapon of this kind the player handed the NPC through its inventory window that it could carry in its pair:
        /// any weapon ScLoadout.TakesHanded - the roll list (XnpcWeapons) and, since round n08, any other gun, bow or melee
        /// weapon a survivor can wield (ScLoadout.WieldableFamily: IZY Classic's pack, other weapon mods), never one a mod
        /// marks not ready - not the one it carries already; the highest tier, on a tie an unmodded copy before a modded one,
        /// else the first. Null when there is none (a launcher, dual pistols, a tool or a dev-only gun cannot join a pair).
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
                if (!ScLoadout.TakesHanded(token) || ScLoadout.IsRangedToken(token) != ranged) continue;
                if (string.Equals(token, cur.Of(ranged), StringComparison.OrdinalIgnoreCase)) continue;
                int level = ScLoadout.HandLevelOf(token);
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
        /// weapon the survivors carry, are handed over. One line for both kinds, first: 0-XNPCCore 3.4.04+'s menu has eleven
        /// rows of its own, and ScDialogPager pages it. Round n09: back on 3.4 too, since 0-XNPCCore's Carried Weapons lists
        /// only a gun with rounds - an unloaded one handed over was offered nowhere. It shows when 0-XNPCCore's weapon menu is
        /// shown at all (the NPC's leader, or a resident through the talk link), so no other check.
        /// </summary>
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
                // What is actually IN her hand after Arm, not the pair's melee weapon: UseHanded arms the RANGED one when
                // the player handed over a firearm, and 0-XNPCCore restores whatever it is told here (its close-range melee
                // exit, its dry-gun failover and its world-entry re-arm all read sdcsHeldWeaponRemembered). Naming the club
                // while she holds the handed AK would make it put the club back at the next one of those.
                ItemValue iv = npc != null && npc.inventory != null ? npc.inventory.holdingItemItemValue : null;
                string held = iv != null && !iv.IsEmpty() && iv.ItemClass != null ? iv.ItemClass.GetItemName() : null;
                if (!string.IsNullOrEmpty(held) && !ScLoadout.IsKitOnly(npc, held) && !ScXnpc.IsLightTool(iv.ItemClass))
                    AnyWeaponBridge.AnyWeaponNoteEquipped(npc, held);
            }
            catch (Exception e)
            {
                ScLog.Once("arms-note-ordered", e);
            }
        }

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
                else NoteOrdered(npc);
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
            sb.Append("; gun first ").Append(!ScConfig.SurvivorGunFirst ? "off (SurvivorGunFirst)" : ScXnpc.IsRaider(e) ? "- (a raider: its lead)"
                      : OrderedMelee(e) ? "stands aside (the player ordered her melee weapon - order the gun to undo)" : "on")
              .Append(" (").Append(gunFirstDraws).Append(" draw(s) this session)")
              .Append(ScTeam.MeleeLed(e) ? "; MELEE-LED (round tm1): only a target that needs the gun, or being overwhelmed, draws it" : "");
            AvatarSdcsNpcController c = e.emodel != null ? e.emodel.avatarController as AvatarSdcsNpcController : null;
            if (c != null && Bind())
                sb.Append("; 0-XNPCCore close melee=").Append(CloseMelee(c)).Append(CloseMelee(c) ? " for " + (now - CloseSince(c)).ToString("0.0") + " s" : "")
                  .Append(" remembered='").Append(Remembered(c) ?? "-").Append('\'');
            return sb.ToString();
        }
    }
}
