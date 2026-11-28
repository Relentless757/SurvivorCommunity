using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Our XNPC bodies - the community's survivors and SC's raiders (ScXnpc.IsBody) - heal only by using a medical item
    /// they actually carry, and you see them do it.
    ///
    /// The NPC framework heals every one of its NPCs for free: its status buff (buffNPCStatusCheck, effect group
    /// NPCHealing) adds IsHealing whenever health is at or below 70% and a 15 s cooldown has run out, and IsHealing
    /// queues 30 health at 2 per second - no item, and no animation on this rig (it fires an animator trigger the
    /// player animator does not have). Measured on a raider of SC's former player-body classes: 50 to 82 health within
    /// a few seconds of a fight. That buff is refused here for our bodies; the framework's own NPCs keep it.
    ///
    /// Instead each body is stocked once with a few first aid bandages and, now and then, a first aid kit, in the
    /// inventory SCore shows for a friendly NPC (HarvestManager, saved with the world and dropped as a backpack on
    /// death) or, for a raider, the vanilla bag (dropped with its loot). When hurt and not already regenerating it
    /// uses one the way a player does: the item leaves the inventory and goes into the NPC's hand, the player's own
    /// third-person use animation plays (the rig picks it from the held item's hold type plus the UseItem trigger -
    /// 3P_Bandage_Hold and 3P_Bandage_Use for medical items, verified live), and when that animation finishes the
    /// item's onSelfPrimaryActionEnd effects fire on the NPC and the weapon goes back in the hand. Interrupted by a
    /// knockdown, the weapon comes back and the item returns to the inventory. You can also hand a survivor medical
    /// items and they will use those. While anyone has a survivor's inventory window open (the host, or a remote player:
    /// ScAmmo.IsWindowOpen) the survivor neither stocks nor uses anything from it, since the open grid would not show the
    /// change, or the copy written back on close would undo it.
    ///
    /// Decision D7: items only, with the bandage animation. XNPC heals its own bodies through a different buff,
    /// SdcsIsHealing (added by its class buff SdcsStatusCheck: for free with no leader, from a medical item without an
    /// animation when hired), which is refused for ours like IsHealing. Their medical stock is this file's roll alone
    /// (Config/entityclasses.xml sets HarvestItems to none on the survivor classes and on the raider templates). While a
    /// use is in progress XNPC's own weapon logic is held off (Patches_Xnpc X5), so its failover or close-range swap
    /// cannot take the bandage out of the hand.
    ///
    /// 3.4 build (decision D2, 2026-10-29). 0-XNPCCore 3.4 dropped the bleeding resistances from npcSdcsBase, so our bodies
    /// bleed (buffInjuryBleeding, which vanilla's bleed carriers - BleedingOne, Two, BarbedWire - all start; lacerations
    /// stay resisted), and its own self-heal is skipped on them (Patches_Xnpc X15): this file treats it. Vanilla's cloth
    /// bandage, first aid bandage and first aid kit each stop it (RemoveBuff buffInjuryBleeding on onSelfPrimaryActionEnd,
    /// items.xml). A bleed is bandaged with the cheapest of them when health is to spare and the body is not fighting (an
    /// attack or revenge target); low on health, the heal item comes first, as it stops the bleed too. Every body is
    /// stocked once with one or two cloth bandages (sc_bandagesStocked, so bodies stocked before also get them). Items
    /// are found in the bag as well as the window: 0-XNPCCore 3.4 moves a survivor's medical items into its bag once
    /// (its start route) and puts what it loots there. While one of its own hand sequences runs (SdcsAnimBusy: its
    /// bandage, heal, douse or party-medic clip) no use starts here; its party medic waits for one running here; and a
    /// body of ours that its party medic treated gets the regen speed the item's "other" effects leave at 0 on an NPC.
    ///
    /// Round md1 (research/flee_meds/DESIGN.md Part B, decisions 14-23): a member who runs out restocks from the stockpile.
    /// At home (ScAmmo.Tick's 5 s member loop, HomeTick) a resident, follower or member on the way to her post with fewer
    /// than MedsResupplyBelow heal items gets 1 Meds' worth - a top-up to MedsResupplyMix, by default 1 first aid kit, 2 first
    /// aid bandages and 2 cloth bandages - instantly, at most once per MedsResupplyCooldownHours (her record's meds=), never
    /// taking the Meds tonight's injured need; before a run (BeforeRun, from ScRuns.Send and ScMining.Send) the same below
    /// MedsResupplyRunBelow, the cooldown ignored. All of the top-up fits her window and bag or nothing is given; nothing is
    /// ever dropped. MedsResupply false: never. sc meds shows it.
    ///
    /// Round k2 (2026-11-15, research/combat/SC_VS_NPCCORE.md root causes 2 and 5, section 4.4): members heal sensibly.
    /// (1) At home a member with no heal item at all, nothing still healing her and under MedsResupplyEmergencyBelow of her
    /// health is restocked with her cooldown ignored and started, as before a run, at most once per
    /// MedsResupplyEmergencyCooldownHours (Evaluate's Emergency and EmergencyLeft; meds-restock-emergency). (2) A member - or
    /// a stand-in - starts no medical use while an enemy that is after her is within MedicSafeDistance (HeldByReach): she
    /// fights on and uses it once the enemy is gone, dead or farther off; waited MedicMaxWait and under DesperateBelow of her
    /// health, she uses it anyway (WaitingToHeal reads the wait). (3) Under KitFirstBelow she takes a first aid kit before a first aid bandage. (4) A member
    /// regains RegenRatePerSecond health out of combat, up to RegenCapShare of her max (RegenTick, from ScAmmo.Tick's 5 s
    /// member loop: her own loaded, visible body on the host only). Out of combat since the Fable review (F3; the user: "Only
    /// threats to her"): no enemy within RegenEnemyRadius after her or able to see her, none at all within RegenNearRadius
    /// (RegenThreat). Raiders and strangers heal as before.
    ///
    /// Round k2b (2026-11-15; the user: "They should back off and heal if they can for any situation"): a member held by (2)
    /// backs off until the enemy after her is beyond MedicSafeDistance + 1 m and then uses the item (ScHealBackOff); with no
    /// room to back off, sheltering in her room, on watch or on a Stay order she uses it at once, and after MedicMaxWait of
    /// backing off without getting clear as well. This replaces (2)'s MedicMaxWait / DesperateBelow exception for members;
    /// a stand-in, and every member with HealBackOff false, keep it.
    /// </summary>
    public static class ScMedic
    {
        /// <summary>Items that restore health, best first. Plain bandages only stop bleeding, so they are not here.</summary>
        public static readonly string[] HealItems = { "medicalFirstAidKit", "medicalFirstAidBandage" };
#if SC_V34
        /// <summary>3.4 (D2): what stops a bleed, cheapest first (0-XNPCCore's own order for a bleed, MedicRules.CheapestItem).</summary>
        public static readonly string[] BleedItems = { "medicalBandage", "medicalFirstAidBandage", "medicalFirstAidKit" };
        /// <summary>Round hr1: what stops a bleed in a last stand - no cloth bandage (it heals nothing and costs the same use).</summary>
        private static readonly string[] BleedItemsInStand = { "medicalFirstAidBandage", "medicalFirstAidKit" };
        /// <summary>3.4: every item a use here can put in the hand (a load finding one there gives the weapon back).</summary>
        private static readonly string[] UseItems = { "medicalFirstAidKit", "medicalFirstAidBandage", "medicalBandage", Painkillers };
        /// <summary>Round U4 (D9): painkillers, a member's last-resort heal (MedicPainkillers; they come as 0-XNPCCore gifts).</summary>
        internal const string Painkillers = "drugPainkillers";
        /// <summary>The bleed every vanilla carrier starts (buffs.xml: BleedingOne, Two and BarbedWire add it).</summary>
        private const string BleedBuff = "buffInjuryBleeding";
        private const string BandagesStockedCvar = "sc_bandagesStocked";
        private static int bleedsStopped, xnpcBusyWaits, xnpcHealSpeedFixed, xnpcMedicHeld;
#endif

        internal const float HealBelow = 0.6f;       // use something at or below this share of max health (round r2: ScRunners reads it too)
        private const float Cooldown = 12f;          // seconds between two uses (counted from the start of one)
        private const float CheckEvery = 1f;         // how often a body looks at its health
        private const int KitWorthMissing = 90;      // below this much missing health a kit would be wasted
        /// <summary>
        /// Round md2 (was TriggerTimeout 0.5 s, after which the trigger fired whatever the animator showed): the item must be in her
        /// hand (Hand.Item) and the animator's WeaponHoldType its hold type within this, or the use is called off with the item back
        /// in her pack (CancelUse) - a clip triggered before the pose is up never plays (Holly after her revive, the hand still stowed).
        /// </summary>
        private const float HoldPoseWaitSeconds = 1f;
        /// <summary>Round md2: the hold pose must have shown this long without a break before the trigger (its blend in).</summary>
        private const float HoldPoseSettleSeconds = 0.1f;
        /// <summary>Round md2: after the use trigger the use clip must start on its layer within this, else the trigger once more (MaxTriggers).</summary>
        private const float ClipStartWaitSeconds = 0.8f;
        private const int MaxTriggers = 2;
        /// <summary>Round md2: the use ends this long after the clip's own end when its layer's leaving is not seen first.</summary>
        private const float ClipEndMargin = 0.25f;
        /// <summary>
        /// Round md2: the use's length after the trigger when the clip cannot be watched (no UpperBodyUseAndReload layer) or, as a last
        /// resort, never started. The clip itself, measured on the dedicated server before round hr1, ends about 2.05 s after the trigger.
        /// </summary>
        private const float ClipFallbackSeconds = 2.1f;
        /// <summary>
        /// Round md2: a called-off use is tried again this soon (not the item's 12 s cooldown: no item was spent); after MaxCalledOff
        /// in a row, RetryLongSeconds.
        /// </summary>
        private const float RetrySeconds = 2f;
        private const float RetryLongSeconds = 10f;
        /// <summary>Round md2: after a ragdoll (a knockdown, a down) the get-up clip still plays about 0.9 s - no use until this long after it ended.</summary>
        private const float GetUpSettleSeconds = 1.5f;
        /// <summary>
        /// Round md2: her hand stowed (vanilla's Hand in Bare mode: Voice Control's chat stow during a talk and up to 20 s after it,
        /// 0-XNPCCore's station or loot stow, one left over from a down), she waits this long for it to be drawn; then she draws it
        /// herself for the use (as 0-XNPCCore's own bandage does) and the stow comes back after it if 0-XNPCCore still holds its chat
        /// stow (RestoreStow). A gap over StowSeenGap between two looks starts the wait again. Round md2 (review MD2-M1): in a fight
        /// (InFightNow) she draws at once.
        /// </summary>
        private const float StowWaitSeconds = 20f;
        private const float StowSeenGap = 3f;
        /// <summary>
        /// Round md2: she is about to go down - the only case a use goes on without its clip (logged): at or under this share of her
        /// max health AND hit in the last LastResortHurtSeconds or an enemy after her within LastResortReach. Low health alone is not
        /// it: 0-XNPCCore revives her at a few health (Holly: 10/162, out of the fight), and that use must show its clip.
        /// </summary>
        private const float LastResortBelow = 0.25f;
        private const float LastResortHurtSeconds = 3f;
        private const float LastResortReach = 4f;
        /// <summary>
        /// Round md2: after this many called-off uses in a row the animator is taken not to play the clip for her, and with no
        /// player within UnseenMetres (no one to see it) the next use goes on without it (logged). With a player near she keeps
        /// trying (RetryLongSeconds apart) and spends nothing.
        /// </summary>
        private const int MaxCalledOff = 3;
        private const float UnseenMetres = 90f;
        /// <summary>
        /// Round md2 (review MD2-M2): the hard stop - after this many call-offs in a row for the same item, or CallOffStopSeconds
        /// after the row's first, the next use of it goes on without the clip even with a player near (a warning, once per row).
        /// A row whose last call-off is CallOffRowGap old is over.
        /// </summary>
        private const int CallOffStop = 6;
        private const float CallOffStopSeconds = 30f;
        private const float CallOffRowGap = 60f;
        /// <summary>
        /// Round md2 (review MD2-M2): the hold types whose use clip on UpperBodyUseAndReload is verified - 64 (bandages, kits:
        /// 3P_Bandage_Hold and Bandage_Use, also 0-XNPCCore's SdcsAnimPlayback states). Any other (drugPainkillers is 16) gets a
        /// plain timed use of ClipFallbackSeconds after the trigger, logged "no clip for hold type N" once per type; a clip seen
        /// for it still ends the use and is logged once.
        /// </summary>
        private static readonly int[] VerifiedClipHoldTypes = { 64 };
        /// <summary>Round md2 (review MD2-m3): a use triggered without its hold pose (a last resort) ends this soon - nothing is on screen to wait for.</summary>
        private const float NoPoseUseSeconds = 0.5f;
        /// <summary>
        /// Round md2 (review MD2-M1): in a fight - hit in the last FightHurtSeconds, a live attack or revenge target, or an awake
        /// enemy after her within FightReach - a stowed hand is drawn at once, not after StowWaitSeconds.
        /// </summary>
        private const float FightHurtSeconds = 5f;
        private const float FightReach = 15f;
        /// <summary>Round md2 (review MD2-n2): no use runs longer than this from its start, whatever the animator says.</summary>
        private const float UseDeadlineSeconds = 8f;
        /// <summary>
        /// medRegHealthIncSpeed for an NPC. The vanilla items set it from the player's Physician perk cvar, which an
        /// NPC does not have, leaving 0 - the queued health would drain without healing. 2 is the no-perk value, and
        /// the one the framework uses for its own heal.
        /// </summary>
        private const float NpcRegenSpeed = 2f;
        private const string StockedCvar = "sc_medsStocked";
        /// <summary>Round k2: under this share of her max health a member takes a first aid kit before a first aid bandage, whatever she is missing.</summary>
        private const float KitFirstBelow = 0.5f;
        /// <summary>
        /// Round k2: the MedicMaxWait exception (she uses it with the enemy still in reach) needs her under this share of her max
        /// health. Round k2b: only for a stand-in and with HealBackOff false - a member backs off instead (ScHealBackOff), and her
        /// own MedicMaxWait exception has no health floor.
        /// </summary>
        private const float DesperateBelow = 0.25f;

        private static readonly Dictionary<int, float> nextCheck = new Dictionary<int, float>();
        private static readonly Dictionary<int, float> readyAt = new Dictionary<int, float>();
        /// <summary>Round k2: body id -> the Time.time she first wanted to start a use and was held by an enemy in reach (her wait).</summary>
        private static readonly Dictionary<int, float> heldSince = new Dictionary<int, float>();
        /// <summary>Round k2 (Fable review 5): body id -> the Time.time of her latest refusal in that wait (TryHeal asks every second).</summary>
        private static readonly Dictionary<int, float> heldLast = new Dictionary<int, float>();
        /// <summary>
        /// Round k2 (Fable review 5): a wait whose latest refusal is older than this (seconds) is over - an unload, a knockdown,
        /// a break in the fight or a use's cooldown came between - and the next refusal starts a fresh MedicMaxWait.
        /// </summary>
        private const float WaitStaleSeconds = 3f;
        /// <summary>Round k2: the enemy scans' scratch list (main thread only).</summary>
        private static readonly List<Entity> near = new List<Entity>();

        /// <summary>An item in use: what it is, how long it takes, and the stack it displaced from the hand.</summary>
        private sealed class Use
        {
            public string Item;
            public int HoldType;
            public int Slot;
            public ItemStack Displaced;
            public float Started;
            public float Duration;
            public float EndAt;
            public bool Triggered;
            public int UseLayer = -2;               // animator layer the use clip plays on (-1: none, -2: not looked up)
            public bool SawClip;
            /// <summary>Round hr1: when the use trigger fired, whether the hold pose showed first (else TriggerTimeout; round md2: HoldPoseWaitSeconds, a last resort only), and what ended it - for the log.</summary>
            public float TriggerAt;
            public bool Shown;
            public string EndedBy = "";
            /// <summary>
            /// Round md2: the item's type; the use layer's state when the trigger fired (the use clip is a NEW state with a clip there -
            /// a reload or other clip already on the layer is not it); the use clip's state, name and length; the last trigger and how
            /// many; whether it may go on without its clip (a last resort, or MaxCalledOff reached) and why.
            /// </summary>
            public int ItemType;
            public int PreState, UseState;
            public string ClipName;
            public float ClipLength, LastTriggerAt;
            public int Triggers;
            public bool NoClipOk;
            public string NoClipWhy;
            /// <summary>Round md2: she drew her stowed hand for this use (RestoreStow after it).</summary>
            public bool DrewHand;
            /// <summary>Round md2: since when the item and its hold pose have shown without a break (-1: not showing).</summary>
            public float ShownAt = -1f;
            /// <summary>
            /// Round md2 (review): the hold type has no verified use clip (MD2-M2: a timed use); the trigger fired without the pose
            /// (MD2-m3); the reach wait this use was released from, -1 for none (MD2-M1: a call-off puts it back); a bleed use
            /// (MD2-n3: counted when it completes); SC wrote SdcsChatStowed 0 for the draw (MD2-m4: RestoreStow puts it back).
            /// </summary>
            public bool NoClipHoldType, NoPose, Bleed, ClearedChatCvar;
            public float WaitSince = -1f;
        }

        /// <summary>
        /// Round md2 (was round hr1's MemberUseSeconds, a 1.5 s cap from the trigger): the user's decision 4 - as short as the
        /// animation allows - is kept by ending every use when its clip ends (the clip's own length, read live, plus ClipEndMargin as
        /// the cap). The 1.5 s cap cut the clip, which runs about 2.05 s from the trigger on 3.4's rig: the weapon came back into the
        /// hand mid-wrap and the animation was not seen.
        /// </summary>
        private static readonly Dictionary<int, Use> uses = new Dictionary<int, Use>();
        /// <summary>Round md2: body id -> the last frame her ragdoll was not Off (a knockdown, a down: the get-up follows).</summary>
        private static readonly Dictionary<int, float> ragdollAt = new Dictionary<int, float>();
        /// <summary>
        /// Round md2: body id -> called-off uses in a row (a use whose clip played ends the row). Round md2 (review MD2-M2): per item,
        /// with the row's first and last call-off and whether its hard stop was logged.
        /// </summary>
        private sealed class CallOffs
        {
            public string Item;
            public int N;
            public float First, Last;
            public bool StopLogged;
        }
        private static readonly Dictionary<int, CallOffs> calledOff = new Dictionary<int, CallOffs>();
        /// <summary>
        /// Round md2 (final check R28-m2): HeldByReach's last release (a false return) let a use go with an enemy after her still in
        /// reach - a verdict, a timeout, a safe room, sheltering or a post - not because nothing was in reach any more. Main thread,
        /// read right after the call.
        /// </summary>
        private static bool releasedInReach;
        /// <summary>Round md2 (review MD2-m6): body id -> when her last use that was not called off began (UsedOrWorking).</summary>
        private static readonly Dictionary<int, float> lastUseAt = new Dictionary<int, float>();
        /// <summary>Round md2 (review MD2-M2): the hold types whose missing (or seen) clip was logged.</summary>
        private static readonly HashSet<int> holdTypesLogged = new HashSet<int>();
        /// <summary>Round md2 (review MD2-m4): AvatarSdcsNpcController.SdcsChatHolsterStowed (internal), looked up once; null if gone.</summary>
        private static System.Reflection.MethodInfo chatStowedGetter;
        private static bool chatStowedLooked;
        /// <summary>Round md2: body id -> when her "hand not ready" line was last logged (once per such spell).</summary>
        private static readonly Dictionary<int, float> notReadyAt = new Dictionary<int, float>();
        /// <summary>Round md2: body id -> her health last frame and the last frame it dropped (LastResort's "hit just now").</summary>
        private static readonly Dictionary<int, int> lastHp = new Dictionary<int, int>();
        private static readonly Dictionary<int, float> hurtAt = new Dictionary<int, float>();
        /// <summary>Round md2: body id -> when her hand was first and last seen stowed by a use that waits for it (StowWaitSeconds).</summary>
        private static readonly Dictionary<int, float> stowSince = new Dictionary<int, float>();
        private static readonly Dictionary<int, float> stowSeen = new Dictionary<int, float>();
        private static int handNotReady, usesCalledOff, usesNoClip, handsDrawn;

        /// <summary>The weapon a use took out of the hand and the item in the hand instead, for the held-gun save.</summary>
        internal sealed class Displaced
        {
            public readonly ItemValue Weapon;
            public readonly string Item;
            public Displaced(ItemValue weapon, string item) { Weapon = weapon; Item = item; }
        }

        /// <summary>
        /// Every use in progress, mirrored for ScAmmo.WriteHeldGun (the re-check's step 2): a body saved while it bandages
        /// holds the medical item, so its gun - rounds and all - is only here. Written on the main thread only (Begin,
        /// Finish, OnDied, FixLoadedHand, Reset); read from the chunk save, which runs on the save thread for an unloaded
        /// chunk (ChunkCluster.RemoveChunk unloads the entities first, RegionFileManager writes them later on
        /// thread_SaveChunks), hence a concurrent dictionary of immutable entries. For the same reason an entry outlives
        /// its body's unload: the save that needs it comes after. It goes when the use ends, the body dies, the body is
        /// loaded again (FixLoadedHand) or the game ends.
        /// </summary>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, Displaced> displaced =
            new System.Collections.Concurrent.ConcurrentDictionary<int, Displaced>();

        /// <summary>A medical use is in progress (ScRunners skips its periodic snapshot - a Write(false) would finish the use).</summary>
        public static bool IsUsing(int id) { return uses.ContainsKey(id); }

        /// <summary>
        /// Round cb1: when her last medical use began (Time.time), -1 with none this session - ScPosture's hold after a heal reads it
        /// (a newer start with no use running: a heal just ended). A pure read.
        /// </summary>
        internal static float LastUseStart(EntityAlive e)
        {
            float ready;
            return e != null && readyAt.TryGetValue(e.entityId, out ready) ? ready - Cooldown : -1f;
        }

        /// <summary>
        /// Round hr1 (re-check HR1R-m3, the last stand's hold): a use is running, an item still heals her, or her last use began at
        /// most seconds ago. A pure read. Round md2 (review MD2-m6): from lastUseAt, not readyAt - Cooldown (a called-off use sets
        /// readyAt to its retry and read as a use).
        /// </summary>
        internal static bool UsedOrWorking(EntityAlive e, float seconds)
        {
            if (e == null) return false;
            if (uses.ContainsKey(e.entityId)) return true;
            try { if (e.Buffs != null && e.Buffs.GetCustomVar("medicalRegHealthAmount") > 0f) return true; }
            catch (Exception) { }
            float at;
            return lastUseAt.TryGetValue(e.entityId, out at) && Time.time - at <= seconds;
        }

        /// <summary>
        /// Round k2 (Fable review 8): is this body's medical use being held right now because an enemy after her is within
        /// MedicSafeDistance (TryHeal's HeldByReach), and for how long (seconds, 0 when not)? A pure read, main thread. A wait
        /// whose latest refusal is more than WaitStaleSeconds old counts as over.
        /// </summary>
        public static bool WaitingToHeal(EntityAlive e, out float seconds)
        {
            seconds = 0f;
            float since;
            if (e == null || !heldSince.TryGetValue(e.entityId, out since)) return false;
            float now = Time.time;
            if (!Fresh(e.entityId, now)) return false;
            seconds = Mathf.Max(0f, now - since);
            return true;
        }

        /// <summary>Round k2: her wait's latest refusal is at most WaitStaleSeconds old.</summary>
        private static bool Fresh(int id, float now)
        {
            float last;
            return heldLast.TryGetValue(id, out last) && now - last <= WaitStaleSeconds;
        }

        /// <summary>Round k2: her wait is over.</summary>
        private static void ClearWait(int id)
        {
            heldSince.Remove(id);
            heldLast.Remove(id);
        }

        /// <summary>
        /// Round k2b (ScHealBackOff's task, when she got clear or has no room to back off): her next medical check runs on the next
        /// frame instead of up to CheckEvery later, so the item goes into her hand before the enemy closes in again.
        /// </summary>
        internal static void CheckSoon(int id)
        {
            nextCheck.Remove(id);
        }

        /// <summary>
        /// Has she any way to heal herself - a use already running, an item still working on her, or a heal item on her
        /// body? TryHeal simply returns when the pick is null, so without this a member with nothing keeps fighting at
        /// any health at all, which is what was killing them. Used by the hurt-flee consideration (UaiOps).
        /// It asks only what she CARRIES: a stocked crate at home does not count, because she cannot reach it mid-fight.
        /// </summary>
        public static bool CanTreatSelf(EntityAlive e)
        {
            if (e == null || e.Buffs == null) return false;
            if (uses.ContainsKey(e.entityId)) return true;
            try { if (e.Buffs.GetCustomVar("medicalRegHealthAmount") > 0f) return true; }
            catch (Exception) { }
            if (FirstCarried(e, HealItems) != null) return true;
#if SC_V34
            // round U4 (D9): a member's painkillers are a heal too, her last one
            if (ScConfig.MedicPainkillers && ScXnpc.IsCommunity(e) && Count(e, Painkillers) > 0) return true;
#endif
            return false;
        }

        /// <summary>
        /// Round r2: one of our bodies at or below this share of her max health with no way to heal herself (CanTreatSelf) - the
        /// hurt-flee's test (UAIConsiderationScHurtNoMeds, at FleeBelowHealth) and a runner's give-up-and-go-home test
        /// (ScRunners, at RunHomeBelowHealth), one rule for both. False for anything that is not one of ours: everything else
        /// heals its own way. A pure read.
        /// </summary>
        public static bool HurtNoMeds(EntityAlive e, float below)
        {
            if (e == null || below <= 0f || !IsBody(e) || e.IsDead()) return false;
            int max = e.GetMaxHealth();
            if (max <= 0 || e.Health > max * below) return false;
            return !CanTreatSelf(e);
        }

        /// <summary>The weapon a use in progress took out of this body's hand, and the item it holds instead (any thread).</summary>
        internal static bool TryDisplaced(int id, out Displaced d) { return displaced.TryGetValue(id, out d); }

        public static void Reset()
        {
            nextCheck.Clear();
            readyAt.Clear();
            uses.Clear();
            displaced.Clear();
            ragdollAt.Clear();                     // round md2
            calledOff.Clear();
            lastUseAt.Clear();                     // round md2 (review)
            holdTypesLogged.Clear();
            notReadyAt.Clear();
            lastHp.Clear();
            hurtAt.Clear();
            stowSince.Clear();
            stowSeen.Clear();
            handNotReady = usesCalledOff = usesNoClip = handsDrawn = 0;
            Patch_NoFreeHeal.SdcsRefused = 0;
#if SC_V34
            bleedsStopped = xnpcBusyWaits = xnpcHealSpeedFixed = xnpcMedicHeld = 0;
#endif
            // round md1: the restock's log pacing and episodes, and this world's mix (resolved again at GameStartDone)
            restockLogAt.Clear();
            restockLast.Clear();
            mix = null;
            mixSkipped = 0;
            // round k2: the waits for an enemy in reach, and the regeneration's clocks, reasons and totals
            heldSince.Clear();
            heldLast.Clear();
#if SC_V34
            gapLogged.Clear();                     // round hr1
            gapOnlyAt.Clear();                     // round hr1 (re-check)
#endif
            regenAt.Clear();
            regenWhy.Clear();
            regenGained.Clear();
            regenTotal = 0f;
        }

        /// <summary>For sc xnpc: the stock flag, whether a use is in progress, and how often XNPC's own heal was refused.</summary>
        public static string Describe(EntityAlive e)
        {
            if (e == null || e.Buffs == null) return "-";
            Use u;
            // round md2: the trigger count and the clip once seen
            string use = uses.TryGetValue(e.entityId, out u) ? "using " + u.Item + (u.Triggered ? (u.SawClip ? " (clip " + u.ClipName + " playing)" : " (triggered x" + u.Triggers + ", no clip yet)") : " (waiting for the hold pose)") : "idle";
#if SC_V34
            // 3.4 (D2): the cloth bandages and the bleed too; the counts take the bag in (0-XNPCCore moves medical items there)
            return "stocked=" + (e.Buffs.GetCustomVar(StockedCvar) > 0f) + "/" + (e.Buffs.GetCustomVar(BandagesStockedCvar) > 0f) + " " + use
                   + (e.Buffs.HasBuff(BleedBuff) ? " BLEEDING" : "") + " cloth=" + Count(e, "medicalBandage") + " bandages=" + Count(e, "medicalFirstAidBandage")
                   + " kits=" + Count(e, "medicalFirstAidKit") + " SdcsIsHealing refused (all bodies)=" + Patch_NoFreeHeal.SdcsRefused
                   + "; restock (md1): " + RestockTimes(e)
                   + "; k2: " + WaitText(e) + ", regen " + RegenText(e)
                   + "; all bodies: bleeds stopped " + bleedsStopped + ", waits for XNPC's hand clips " + xnpcBusyWaits
                   + ", XNPC medic regen fixed " + xnpcHealSpeedFixed + ", XNPC medic held for a use " + xnpcMedicHeld
                   // round md2: her hand and clip state, and the counts of uses that waited for the hand, were called off, drew a stowed
                   // hand or went on without the clip
                   + "; md2: hand " + HandText(e) + (HandReadyText(e)) + ", called off in a row " + (calledOff.ContainsKey(e.entityId) ? calledOff[e.entityId].N + " (" + calledOff[e.entityId].Item + ")" : "0")
                   + "; all bodies: waits for the hand " + handNotReady + ", called off " + usesCalledOff + ", stowed hands drawn " + handsDrawn + ", used without the clip " + usesNoClip;
#else
            return "stocked=" + (e.Buffs.GetCustomVar(StockedCvar) > 0f) + " " + use + " bandages=" + Count(e, "medicalFirstAidBandage")
                   + " kits=" + Count(e, "medicalFirstAidKit") + " SdcsIsHealing refused (all bodies)=" + Patch_NoFreeHeal.SdcsRefused;
#endif
        }

        /// <summary>One of our XNPC bodies (ScXnpc.IsBody: a survivor class the community owns, or a raider); XNPC's other classes heal their own way.</summary>
        public static bool IsBody(Entity e)
        {
            return ScXnpc.IsBody(e);
        }

        public static bool IsUsingItem(Entity e)
        {
            return e != null && uses.ContainsKey(e.entityId);
        }

        /// <summary>Runs from EntityAlive.OnUpdateLive for every entity: must stay cheap for everything that is not ours.</summary>
        public static void Tick(EntityAlive e)
        {
            if (!IsBody(e)) return;
            // round MP0: a client's copy of a body is the server's to heal (OnUpdateLive runs on every machine); acting here
            // would use items the client's copy only seems to hold
            if (e.isEntityRemote) return;
            // round md2: the get-up's clock - the last frame her ragdoll was not Off - and her last hit (cheap: a compare a frame)
            if (e.emodel != null && e.emodel.ragdollState != EModelBase.ERagdollState.Off) ragdollAt[e.entityId] = Time.time;
            int hp = e.Health, hpWas;
            if (!lastHp.TryGetValue(e.entityId, out hpWas)) lastHp[e.entityId] = hp;
            else if (hp != hpWas)
            {
                if (hp < hpWas) hurtAt[e.entityId] = Time.time;
                lastHp[e.entityId] = hp;
            }
            Use u;
            if (uses.TryGetValue(e.entityId, out u)) { Advance(e, u); return; }
            float now = Time.time;
            float next;
            if (nextCheck.TryGetValue(e.entityId, out next) && now < next) return;
            nextCheck[e.entityId] = now + CheckEvery;
            if (e.IsDead() || e.Health <= 0 || e.Buffs == null) return;
            if (GameManager.Instance == null || GameManager.Instance.gameStateManager == null || !GameManager.Instance.gameStateManager.IsGameStarted()) return;
            // its inventory window is open (ScAmmo.IsWindowOpen): a remote player's copy is written back over every slot
            // when they close it, and even the host's own grid is not told about a change, so a bandage taken out now
            // could be had twice and one stocked now could be lost - both wait for the close (raiders keep theirs in the
            // bag, never held)
            if (ScNpc.UsesWindow(e) && ScAmmo.IsWindowOpen(e.entityId)) { Patches_Xnpc.Count("window-medic-held"); return; }
            if (!e.world.IsRemote() && e.Buffs.GetCustomVar(StockedCvar) <= 0f) Stock(e);
#if SC_V34
            if (!e.world.IsRemote() && e.Buffs.GetCustomVar(BandagesStockedCvar) <= 0f) StockBandages(e);
#endif
            TryHeal(e, now);
        }

        /// <summary>
        /// A body id SC lets go of (ScLooks.Forget: a member purged or dead off-world, a runner's old id after a re-key, an
        /// enclave person's or band member's body leaving the world - entity ids are recycled). Round k2 (Fable review 5) wired
        /// it in: its check and cooldown clocks, her wait for an enemy in reach and her regeneration state go. A use in
        /// progress and its displaced weapon are NOT dropped here: their mirror must outlive an unload for the chunk save
        /// that comes after it (see displaced), and they end with the use, the death, the next load (FixLoadedHand) or the game.
        /// </summary>
        public static void Forget(int entityId)
        {
            nextCheck.Remove(entityId);
            readyAt.Remove(entityId);
            ClearWait(entityId);
            ragdollAt.Remove(entityId);           // round md2
            calledOff.Remove(entityId);
            lastUseAt.Remove(entityId);           // round md2 (review)
            notReadyAt.Remove(entityId);
            lastHp.Remove(entityId);
            hurtAt.Remove(entityId);
            stowSince.Remove(entityId);
            stowSeen.Remove(entityId);
#if SC_V34
            gapLogged.Remove(entityId);           // round hr1
            gapOnlyAt.Remove(entityId);           // round hr1 (re-check)
#endif
            regenAt.Remove(entityId);
            regenWhy.Remove(entityId);
            regenGained.Remove(entityId);
        }

        // ---------------------------------------------------------------------------------------------------------
        // stock
        // ---------------------------------------------------------------------------------------------------------

        private static void Stock(EntityAlive e)
        {
            e.Buffs.SetCustomVar(StockedCvar, 1f);
            GameRandom rnd = e.world.GetGameRandom();
            float r = rnd.RandomFloat;
            int bandages = r < 0.3f ? 0 : (r < 0.75f ? 1 : 2);
            int kits = rnd.RandomFloat < 0.15f ? 1 : 0;
            if (bandages > 0) Add(e, "medicalFirstAidBandage", bandages);
            if (kits > 0) Add(e, "medicalFirstAidKit", kits);
            ScLog.Verbose("medic: stocked " + e.entityId + " with " + bandages + " first aid bandage(s), " + kits + " kit(s)");
        }

#if SC_V34
        /// <summary>3.4 (D2): one or two cloth bandages, once per body (bodies stocked before 3.4 get them too).</summary>
        private static void StockBandages(EntityAlive e)
        {
            e.Buffs.SetCustomVar(BandagesStockedCvar, 1f);
            int n = e.world.GetGameRandom().RandomFloat < 0.5f ? 1 : 2;
            Add(e, "medicalBandage", n);
            ScLog.Verbose("medic: stocked " + e.entityId + " with " + n + " cloth bandage(s)");
        }
#endif

        /// <summary>
        /// Round md2 (review MD2-m5): one item into the bag - never the window - or, with no room, onto the ground at the body
        /// (host only), as ScNpc.AddItem does with a full window.
        /// </summary>
        private static void ReturnToBag(EntityAlive e, string item)
        {
            ItemValue iv = ItemClass.GetItem(item, true);
            if (iv == null || iv.IsEmpty()) return;
            var stack = new ItemStack(iv, 1);
            if (e.bag != null && e.bag.AddItem(stack)) { Patches_Xnpc.Count("medic-returned-to-bag"); return; }
            if (!ScCommunity.IsHost || GameManager.Instance == null) return;
            GameManager.Instance.ItemDropServer(stack.Clone(), e.position + Vector3.up, Vector3.zero, -1, 60f);
            ScLog.Verbose(BodyName(e) + ": " + item + " dropped at the body (the window open, the bag full; round md2, review)");
        }

        private static void Add(EntityAlive e, string item, int count)
        {
            ItemValue iv = ItemClass.GetItem(item, true);
            if (iv == null || iv.IsEmpty()) return;
            ScNpc.AddItem(e, new ItemStack(iv, count));
        }

        // ---------------------------------------------------------------------------------------------------------
        // decide
        // ---------------------------------------------------------------------------------------------------------

#if SC_V34
        /// <summary>
        /// 3.4 (D2): the 3.2 heal below HealBelow, plus the bleed. A bleed with health to spare is stopped with the cheapest
        /// item that stops it (BleedItems), and only out of a fight (no live attack or revenge target): a 20 s bleed costs
        /// less than lowering the gun mid-fight for it. Low on health, the heal item comes first - a first aid bandage or
        /// kit stops the bleed as well - and with none left a cloth bandage at least stops the bleed. A heal still working
        /// (medicalRegHealthAmount) waits as before, but does not keep a bleed from being stopped. Nothing starts while one
        /// of 0-XNPCCore's own hand clips runs (SdcsAnimBusy: its bandage, heal, douse or party-medic sequence holds the
        /// hand slot this would write).
        /// Round k2, for a member or a stand-in (ScXnpc.IsCommunity; raiders and strangers as before): under KitFirstBelow the
        /// kit comes before the first aid bandage, and no use - a heal or a bleed - starts while an enemy after her is within
        /// MedicSafeDistance (HeldByReach).
        /// </summary>
        private static void TryHeal(EntityAlive e, float now)
        {
            int max = e.GetMaxHealth();
            if (max <= 0) return;
            bool low = e.Health <= max * HealBelow;
            bool bleeding = e.Buffs.HasBuff(BleedBuff);
            if (!low && !bleeding) { ClearWait(e.entityId); return; }
            if (e.emodel == null || e.emodel.IsRagdollActive || e.inventory == null) return;
            float ready;
            if (readyAt.TryGetValue(e.entityId, out ready) && now < ready)
            {
                // round md2 (review MD2-M1): a reach wait a call-off put back stays fresh through its retry delay - round md2 (final check
                // R28-m2): only while an enemy after her is still in reach (without one the wait is over, and a later one starts fresh)
                if (heldSince.ContainsKey(e.entityId) && EnemyInWaitReach(e)) heldLast[e.entityId] = now;
                return;
            }
            if (e.GetCVar("SdcsAnimBusy") >= 0.5f)
            {
                // round hr1 (step 5): her wait stays fresh through 0-XNPCCore's roll, so its "heal now" verdict still names this wait after it
                if (heldSince.ContainsKey(e.entityId) && ScHealBackOff.Rolling(e)) heldLast[e.entityId] = now;
                xnpcBusyWaits++;
                return;
            }
            bool working = e.Buffs.GetCustomVar("medicalRegHealthAmount") > 0f;    // an item is already working
            bool member = ScXnpc.IsCommunity(e);                                    // round k2's rules: members and stand-ins

            string pick = null;
            if (low && !working)
            {
                int missing = max - e.Health;
                // round k2: low enough, the kit first whatever she is missing (a first aid bandage's 30 is gone in two hits)
                bool kitFirst = missing >= KitWorthMissing || (member && e.Health < max * KitFirstBelow);
                if (kitFirst && Count(e, "medicalFirstAidKit") > 0) pick = "medicalFirstAidKit";
                else if (Count(e, "medicalFirstAidBandage") > 0) pick = "medicalFirstAidBandage";
                else if (Count(e, "medicalFirstAidKit") > 0) pick = "medicalFirstAidKit";
                // round U4 (D9): with no first aid kit or bandage left, a member's painkillers - her last resort
                else if (member && ScConfig.MedicPainkillers && Count(e, Painkillers) > 0) pick = Painkillers;
            }
            // round hr1: no cloth bandage for a bleed in a last stand (a heal item stops it too; the kit comes first below KitFirstBelow)
            if (pick == null && bleeding && (low || !InFight(e))) pick = FirstCarried(e, member && ScPosture.InStand(e) ? BleedItemsInStand : BleedItems);
            if (pick == null) { ClearWait(e.entityId); return; }
            // round md2: no item is spent while the animation cannot play - she is down, her ragdoll or get-up still runs, she is
            // seated, her hand is switching, or it is stowed (a talk, a station, loot) and has not been drawn in StowWaitSeconds (then
            // she draws it herself; at once in a fight - review MD2-M1 - or when she is about to go down, LastResort, the one case the
            // use may go on without its clip). Round md2 (review MD2-M1): asked BEFORE the reach rules - HeldByReach spends its
            // heal-now verdict and clears the wait when it lets a use go - and a wait it refuses stays alive (heldLast)
            string notReady, lastWhy, fightWhy;
            bool drawHand;
            bool lastResort = LastResort(e, now, out lastWhy);
            fightWhy = null;
            bool fight = lastResort || (e.Hand != null && e.Hand.IsHolstered && InFightNow(e, now, out fightWhy));
            bool brief;
            if (!HandReady(e, now, fight, lastResort, out notReady, out drawHand, out brief))
            {
                // round md2 (final check R28-m2): only a short hold (the get-up, a hand switch, the stow wait) keeps a wait alive - a
                // down or a seat would keep it fresh for their whole length and hand an old MedicMaxWait to the next enemy
                if (brief && heldSince.ContainsKey(e.entityId)) heldLast[e.entityId] = now;
                NoteNotReady(e, pick, notReady, now);
                return;
            }
            // round md2 (final check R28-m2): the wait a call-off may put back - the one live now (fresh) and only if HeldByReach lets
            // the use go with the enemy still in reach (a verdict or a timeout); a stale wait, or one that ends because nothing is in
            // reach any more, is not
            float waitSince;
            bool liveWait = heldSince.TryGetValue(e.entityId, out waitSince) && Fresh(e.entityId, now);
            if (member && HeldByReach(e, pick, max, now)) return;
            bool keepWait = member && liveWait && releasedInReach;
            if (!TakeOne(e, pick)) return;
            Begin(e, pick, now, lastResort ? lastWhy : null, drawHand, keepWait ? waitSince : -1f, fightWhy);
            readyAt[e.entityId] = now + Cooldown;
            if (bleeding)
            {
                // round md2 (review MD2-n3): counted when the use completes (Finish), not when it starts - a call-off stops no bleed
                Use bu;
                if (uses.TryGetValue(e.entityId, out bu)) bu.Bleed = true;
                ScLog.Verbose("medic: " + e.entityId + " is bleeding - " + pick);
            }
        }

        /// <summary>
        /// Round md2 (final check R28-m2): an awake enemy after her within a held wait's reach (MedicSafeDistance + the back-off's
        /// ClearMargin, as HeldByReach holds an ongoing wait) - while there is none, a wait is over.
        /// </summary>
        private static bool EnemyInWaitReach(EntityAlive e)
        {
            float r = ScConfig.MedicSafeDistance, dist;
            if (r <= 0f) return false;
            return NearestEnemy(e, r + ScHealBackOff.ClearMargin, true, out dist) != null;
        }

        /// <summary>
        /// Round md2 (review MD2-M1): she is in a fight - hit in the last FightHurtSeconds, a live attack or revenge target
        /// (InFight), or an awake enemy after her within FightReach. why: which. A read.
        /// </summary>
        private static bool InFightNow(EntityAlive e, float now, out string why)
        {
            why = null;
            float hit, dist;
            if (hurtAt.TryGetValue(e.entityId, out hit) && now - hit <= FightHurtSeconds) { why = "hit " + F1(now - hit) + " s ago"; return true; }
            if (InFight(e)) { why = "a live attack or revenge target"; return true; }
            EntityAlive t = NearestEnemy(e, FightReach, true, out dist);
            if (t != null) { why = ScCommunity.ClassName(t) + " (" + t.entityId + ") after " + HerObj(e) + " at " + F1(dist) + " m"; return true; }
            return false;
        }

        /// <summary>A live attack or revenge target (the bleed waits: ScRelax's alert reasons, the AI-free part).</summary>
        private static bool InFight(EntityAlive e)
        {
            EntityAlive t = e.GetAttackTarget();
            if (t != null && t != e && !t.IsDead()) return true;
            t = e.GetRevengeTarget();
            return t != null && t != e && !t.IsDead();
        }

        /// <summary>The first of these items the body carries (window or bag), or null.</summary>
        private static string FirstCarried(EntityAlive e, string[] items)
        {
            for (int i = 0; i < items.Length; i++) if (Count(e, items[i]) > 0) return items[i];
            return null;
        }

        /// <summary>
        /// Round k2 (research/combat/SC_VS_NPCCORE.md root cause 5): TryHeal's last test for a member or a stand-in who has
        /// picked an item. The item takes her weapon out of her hand for about 2.5 s, and every use in the fight that killed
        /// Gina and Paul was made with a zombie 0.9-1.3 m away and cost 14-27 health in hits - a first aid bandage gives 30.
        /// So no use starts while an awake enemy that is after her (its attack or revenge target) is within MedicSafeDistance:
        /// she keeps her weapon and fights, and TryHeal asks again every second. Her wait starts at the first refusal and ends
        /// when she no longer needs the item, uses it, or the enemy is gone, dead or farther off. The exception: once she has
        /// waited MedicMaxWait and is under DesperateBelow of her health, she uses it with the enemy still there - a heal now
        /// beats none. A bleed follows the same rule. True: held at this check. 0-XNPCCore rolls away before its own bandage
        /// (its heal sequence); SC's members do not roll (round k2's report: not built).
        /// A wait whose latest refusal is more than WaitStaleSeconds old is dropped first (Fable review 5): a reload, a
        /// knockdown or a break in the fight starts a fresh MedicMaxWait rather than counting the old one.
        /// Round k2b (HealBackOff; the user: "They should back off and heal if they can for any situation"), for a member - a
        /// stand-in keeps the rule above: held, she backs off (ScHealBackOff's action, 8.3, reads her wait) until nothing after
        /// her is within MedicSafeDistance + 1 m, and the next check (ScHealBackOff asks for it at once) finds nothing in reach
        /// and uses the item. While she is held the hold's own reach is that MedicSafeDistance + 1 m as well (hysteresis: it
        /// starts at 3 m and lets go at 4 m, so the use never starts a step outside the hold). She uses it NOW, at any health: when she cannot back off (sheltering in her room, on watch, on a
        /// Stay order: heal-now-shelter / heal-now-post), when the back-off found no room for this wait or the enemy keeps up
        /// with her (its heal-now verdict; heal-now-cornered / heal-now-gained, counted by the task), or once the wait has run
        /// MedicMaxWait without her getting clear (heal-now-timeout; its line says whether a back-off ran at all). DesperateBelow
        /// plays no part for her. Round nc1 (D2): with no room to back off and her health above CorneredHealBelow she fights on
        /// instead - no heal-now verdict, and the MedicMaxWait timeout waits too (ScHealBackOff.CorneredFightsOn).
        /// </summary>
        private static bool HeldByReach(EntityAlive e, string pick, int max, float now)
        {
            releasedInReach = false;                // round md2 (final check R28-m2): set on a release with the enemy still in reach
            float r = ScConfig.MedicSafeDistance;
            if (r <= 0f) { ClearWait(e.entityId); return false; }
            float since;
            bool waiting = heldSince.TryGetValue(e.entityId, out since);
            if (waiting && !Fresh(e.entityId, now))
            {
                ClearWait(e.entityId);
                waiting = false;
            }
            // round k2b (HealBackOff): a member - not a stand-in - backs off and then heals (ScHealBackOff). Once held she stays
            // held until nothing after her is within MedicSafeDistance + ClearMargin (the back-off's own clear test): the use
            // never starts with the zombie a step outside the hold, and the hold cannot flip at its edge
            Member bm = ScConfig.HealBackOff && ScRegistry.Loaded ? ScRegistry.GetMember(e.entityId) : null;
            float reach = bm != null && waiting ? r + ScHealBackOff.ClearMargin : r;
            float dist;
            EntityAlive t = NearestEnemy(e, reach, true, out dist);
            if (t == null)
            {
                // round hr1 (review HR1-m5): at home the gap rule here too - an enemy within 2.5 m that sees her holds the use even when
                // it is after someone else
                // round hr1 (re-check HR1R-m2): a hold of its own - no reach wait (ScHealBackOff stays out, her attack is not restarted, no
                // MedicMaxWait used up); a reach wait that was on is over, so a pursuer that comes later starts a fresh one
                if (bm != null && ScHealBackOff.AtHome(e) && GapOnlyHolds(e, pick, "nothing after " + HerObj(e) + " within " + F1(reach) + " m", now))
                {
                    if (waiting) ClearWait(e.entityId);
                    return true;
                }
                if (waiting)
                {
                    ClearWait(e.entityId);
                    ScLog.Verbose(BodyName(e) + ": nothing after " + HerObj(e) + " within " + F1(reach) + " m any more - uses " + pick
                                  + " after waiting " + F1(now - since) + " s");
                }
                return false;
            }
            string who = ScCommunity.ClassName(t) + " (" + t.entityId + ") at " + F1(dist) + " m";
            // round hr1: a member at home (round hl1's AtHome) takes the heal order - a safe room at once, and the gap rule on every path
            bool home = bm != null && ScHealBackOff.AtHome(e);
            string why;
            if (home && ScSafeRoom.SafeToHeal(e, out why))
            {
                // round hr1 (step 1, decision 3): an enclosed room with no enemy inside, none armed in sight and none within 2 m that
                // sees her - every hold is lifted (the gap rule's sight test is part of the safe-room test)
                ClearWait(e.entityId);
                releasedInReach = true;             // round md2 (final check R28-m2)
                Patches_Xnpc.Count("heal-now-saferoom");
                ScLog.Out(BodyName(e) + " uses " + pick + " at once with " + who + " after " + HerObj(e) + ": " + why + " (round hr1, step 1)");
                return false;
            }
            // round k2b: where she cannot back off (sheltering in her room, on watch, on a Stay order) she uses it at once
            bool shelter;
            if (bm != null && !ScHealBackOff.MayBackOff(e, bm, out why, out shelter))
            {
                // round hr1 (decision 6): at home not with an enemy within 2.5 m that sees her while she has a usable weapon
                if (home && GapHolds(e, pick, who, why, waiting, since, now)) return true;
                ClearWait(e.entityId);
                releasedInReach = true;             // round md2 (final check R28-m2)
                Patches_Xnpc.Count(shelter ? "heal-now-shelter" : "heal-now-post");
                ScLog.Out(BodyName(e) + " uses " + pick + " at once with " + who + " after " + HerObj(e) + ": " + why + " (round k2b)");
                return false;
            }
            if (!waiting)
            {
                heldSince[e.entityId] = now;
                since = now;
                Patches_Xnpc.Count("meds-use-held-reach");
                ScLog.Verbose(BodyName(e) + " waits to use " + pick + " at " + e.Health + "/" + max + ": " + who + " is after " + HerObj(e)
                              + " (MedicSafeDistance " + F1(r) + " m) - " + (bm != null ? She(e) + " backs off to use it (round k2b)" : She(e) + " fights on"));
            }
            heldLast[e.entityId] = now;
            float waited = now - since;
            if (bm != null)
            {
                // round k2b: the back-off found no room or the enemy keeps up with her (its task's heal-now verdict for this very
                // wait), or she has been held MedicMaxWait without getting clear: she uses it now, at any health
                bool timeout = false;
                if (!ScHealBackOff.HealNowFor(e, since, out why))
                {
                    // round hr1: at home MedicMaxWait counts from the first walk start (the walk's path length over her run speed + 2 s,
                    // at most 12 s), not the wait's start; before any walk, and away from home, as before
                    bool walked = false;
                    float until = home ? ScHealBackOff.TimeoutAt(e, since, out walked) : since + ScConfig.MedicMaxWait;
                    if (now < until) return true;                       // held: ScHealBackOff (8.3) walks her clear
                    // round nc1 (D2): cornered above CorneredHealBelow she fights on - no "anyway" with the zombie on her
                    if (ScHealBackOff.CorneredFightsOn(e, since)) return true;
                    timeout = true;
                    // the Fable review (F8): "backing off" only when a back-off walk did start in this wait
                    why = walked
                        ? "walking " + F1(waited) + " s did not get " + HerObj(e) + " clear (MedicMaxWait from " + Her(e) + " first walk's start: its path over " + Her(e) + " run + 2 s, at most 12 s; round hr1)"
                        : ScHealBackOff.BackedOffFor(e, since)
                        ? "backing off " + F1(waited) + " s did not get " + HerObj(e) + " clear (MedicMaxWait " + F1(ScConfig.MedicMaxWait) + " s)"
                        : "held " + F1(waited) + " s and the back-off never ran (" + ScHealBackOff.NoRunWhy(e) + "; MedicMaxWait " + F1(ScConfig.MedicMaxWait) + " s)";
                }
                // round hr1 (decision 6): at home every heal path obeys the gap rule - the back-off's verdicts and the timeout too
                if (home && GapHolds(e, pick, who, why, true, since, now))
                {
                    // round hr1 (review HR1-M1): a timeout the gap rule held ends the walking - she fights on, no more walks this wait,
                    // and the last stand may start; she heals the moment the gap opens
                    if (timeout) ScHealBackOff.TimedOutHeld(e, since);
                    return true;
                }
                ClearWait(e.entityId);
                releasedInReach = true;             // round md2 (final check R28-m2)
                if (timeout) Patches_Xnpc.Count("heal-now-timeout");
                ScLog.Out(BodyName(e) + " uses " + pick + " now with " + who + " after " + HerObj(e) + ": " + why + (home ? " - no enemy within 2.5 m sees " + HerObj(e) + " (round hr1's gap)" : "") + " (round k2b)");
                return false;
            }
            // round k2's rule, kept for a stand-in and with HealBackOff false
            if (e.Health < max * DesperateBelow && waited >= ScConfig.MedicMaxWait)
            {
                ClearWait(e.entityId);
                releasedInReach = true;             // round md2 (final check R28-m2)
                Patches_Xnpc.Count("meds-use-desperate");
                ScLog.Out(BodyName(e) + " uses " + pick + " with " + who + " after " + HerObj(e) + ": waited " + F1(waited) + " s and down to "
                          + e.Health + "/" + max + " (under " + (DesperateBelow * 100f).ToString("0", System.Globalization.CultureInfo.InvariantCulture)
                          + "% after MedicMaxWait " + F1(ScConfig.MedicMaxWait) + " s: a heal now beats none)");
                return false;
            }
            return true;
        }

        /// <summary>Round hr1: body id -> the wait (heldSince) whose gap-rule line was logged (once per wait).</summary>
        private static readonly Dictionary<int, float> gapLogged = new Dictionary<int, float>();
        /// <summary>Round hr1 (re-check HR1R-m2): body id -> the last check a gap-only hold held her (its line once per hold, WaitStaleSeconds apart).</summary>
        private static readonly Dictionary<int, float> gapOnlyAt = new Dictionary<int, float>();

        /// <summary>
        /// Round hr1 (re-check HR1R-m2): the gap rule where nothing is after her within reach (an enemy within 2.5 m sees her but targets
        /// someone else) - the use waits, but no reach wait starts: ScHealBackOff's action stays out (her attack goes on) and the
        /// MedicMaxWait clock is not run. Logged once per hold. True: held.
        /// </summary>
        private static bool GapOnlyHolds(EntityAlive e, string pick, string pathWhy, float now)
        {
            string fw;
            if (!ScHealBackOff.FightsInstead(e, out fw)) { gapOnlyAt.Remove(e.entityId); return false; }
            float last;
            bool first = !gapOnlyAt.TryGetValue(e.entityId, out last) || now - last > WaitStaleSeconds;
            gapOnlyAt[e.entityId] = now;
            if (first)
            {
                Patches_Xnpc.Count("heal-gap-held-only");
                ScLog.Out(BodyName(e) + " would use " + pick + " now (" + pathWhy + ") but " + fw + " - " + She(e) + " fights and heals in a gap (round hr1, decision 6)");
            }
            return true;
        }

        /// <summary>
        /// Round hr1 (the user's decision 6, "heal only in a gap"): at home a use that would start now (pathWhy: which path - the
        /// back-off's verdict, the MedicMaxWait timeout, sheltering, on watch, told to stay) is held while an enemy within 2.5 m can
        /// see her and she has a usable weapon (ScHealBackOff.FightsInstead) - she fights, and the next check (every second) heals
        /// her in a gap. Her wait starts or goes on; the line is logged once per wait. True: held.
        /// </summary>
        private static bool GapHolds(EntityAlive e, string pick, string who, string pathWhy, bool waiting, float since, float now)
        {
            string fw;
            if (!ScHealBackOff.FightsInstead(e, out fw)) return false;
            if (!waiting)
            {
                heldSince[e.entityId] = now;
                since = now;
                Patches_Xnpc.Count("meds-use-held-reach");
            }
            heldLast[e.entityId] = now;
            float had;
            if (!gapLogged.TryGetValue(e.entityId, out had) || Mathf.Abs(had - since) > 0.05f)
            {
                gapLogged[e.entityId] = since;
                Patches_Xnpc.Count("heal-gap-held");
                ScLog.Out(BodyName(e) + " would use " + pick + " now" + (who != null ? " with " + who + " after " + HerObj(e) : "") + " (" + pathWhy + ") but " + fw + " - " + She(e)
                          + " fights instead and heals in a gap (round hr1, decision 6)");
            }
            return true;
        }
#else
        private static void TryHeal(EntityAlive e, float now)
        {
            int max = e.GetMaxHealth();
            if (max <= 0 || e.Health > max * HealBelow) return;
            if (e.emodel == null || e.emodel.IsRagdollActive || e.inventory == null) return;
            float ready;
            if (readyAt.TryGetValue(e.entityId, out ready) && now < ready) return;
            if (e.Buffs.GetCustomVar("medicalRegHealthAmount") > 0f) return;       // an item is already working

            int missing = max - e.Health;
            string pick = null;
            if (missing >= KitWorthMissing && Count(e, "medicalFirstAidKit") > 0) pick = "medicalFirstAidKit";
            else if (Count(e, "medicalFirstAidBandage") > 0) pick = "medicalFirstAidBandage";
            else if (Count(e, "medicalFirstAidKit") > 0) pick = "medicalFirstAidKit";
            if (pick == null) return;
            if (!TakeOne(e, pick)) return;
            Begin(e, pick, now);
            readyAt[e.entityId] = now + Cooldown;
        }
#endif

        // ---------------------------------------------------------------------------------------------------------
        // use: item into the hand, animation, effects when it ends, weapon back
        // ---------------------------------------------------------------------------------------------------------

        private static void Begin(EntityAlive e, string item, float now, string lastResort = null, bool drawHand = false, float waitSince = -1f, string fightWhy = null)
        {
            ItemClass ic = ItemClass.GetItemClass(item, true);
            if (ic == null) return;
            var u = new Use
            {
                Item = item,
                HoldType = ic.HoldType != null ? ic.HoldType.Value : 0,
                Slot = e.inventory.holdingItemIdx,
                Started = now,
                // round md2: the use lasts as long as its clip (read when it starts); this is only the length when the clip cannot be
                // watched or, as a last resort, never starts (round hr1's 1.5 s cap cut the clip)
                Duration = ClipFallbackSeconds,
                ItemType = ic.Id,
                WaitSince = waitSince,                       // round md2 (review MD2-M1)
            };
            // round md2 (review MD2-M2): a hold type with no verified use clip gets a timed use
            u.NoClipHoldType = Array.IndexOf(VerifiedClipHoldTypes, u.HoldType) < 0;
            if (u.NoClipHoldType && holdTypesLogged.Add(u.HoldType))
            {
                Patches_Xnpc.Count("medic-use-unverified-holdtype");
                ScLog.Out("medic: no clip for hold type " + u.HoldType + " (" + item + ") is verified - its uses are timed, " + F1(ClipFallbackSeconds)
                          + " s after the trigger; a clip seen for it is logged once (round md2, review)");
            }
            // round md2: about to go down, or the animator has not played the clip for her MaxCalledOff times in a row and no player is
            // near enough to see it - the use goes on without its clip if it must (logged once it does). Round md2 (review MD2-M2): or
            // the row for this item reached its hard stop (CallOffStop, or CallOffStopSeconds since its first call-off), player or not
            CallOffs row;
            if (!calledOff.TryGetValue(e.entityId, out row) || row.Item != item || now - row.Last > CallOffRowGap) row = null;
            if (lastResort != null) { u.NoClipOk = true; u.NoClipWhy = She(e) + " is about to go down (" + lastResort + "; a last resort)"; }
            else if (row != null && (row.N >= CallOffStop || now - row.First >= CallOffStopSeconds))
            {
                u.NoClipOk = true;
                u.NoClipWhy = "called off " + row.N + " time(s) in " + F1(now - row.First) + " s - the hard stop (round md2, review)";
                if (!row.StopLogged)
                {
                    row.StopLogged = true;
                    Patches_Xnpc.Count("medic-calloff-hard-stop");
                    ScLog.Warn(BodyName(e) + "'s " + item + " was called off " + row.N + " time(s) in " + F1(now - row.First) + " s - the next use goes on without its clip if it must (round md2, review MD2-M2)");
                }
            }
            else if (row != null && row.N >= MaxCalledOff && e.world != null && e.world.GetClosestPlayer(e.position, UnseenMetres, false) == null)
            {
                u.NoClipOk = true;
                u.NoClipWhy = "the clip did not play for " + HerObj(e) + " " + row.N + " times in a row and no player is within " + F1(UnseenMetres) + " m to see it";
            }
            ItemStack held = e.inventory.GetItem(u.Slot);
            // the weapon's own ItemValue, not a copy (M11): Locked & Loaded's Inventory.SetItem prefix gives a seated
            // magazine back to the bag when the medical item goes into the hand and empties this very ItemValue, so a copy
            // taken first would bring the magazine back with the gun while it also sits in the bag. Nothing else touches
            // it once it leaves the slot (SetItem stores a clone of whatever goes in).
            u.Displaced = held != null && !held.IsEmpty() ? new ItemStack(held.itemValue, held.count) : ItemStack.Empty.Clone();
#if SC_V34
            // 3.4: a grid slot that is EMPTIED is cleared in place (ItemStack.Clear resets the slot's own ItemValue, type 0),
            // where 3.2 swapped in a fresh one - so the weapon's own ItemValue could turn into nothing under us. A copy is
            // kept for that case alone; L&L's emptying (Meta 0, the type kept) still reaches the weapon's own value.
            ItemValue displacedCopy = u.Displaced.IsEmpty() ? null : u.Displaced.itemValue.Clone();
            int displacedCount = u.Displaced.count;
#endif
            u.EndAt = 0f;                                    // round md2: set by the clip (or the fallback) once the trigger fired
            uses[e.entityId] = u;
            lastUseAt[e.entityId] = now;                     // round md2 (review MD2-m6): CancelUse takes it back
#if SC_V34
            // round md2: a stow nobody drew in StowWaitSeconds (or a last resort) - she draws for the use, as 0-XNPCCore's own bandage
            // does (Hand.SelectHoldingMode(Current), instant for an NPC), and RestoreStow puts the stow back after it
            Hand hand = e.Hand;
            if (drawHand && hand != null && hand.IsHolstered)
            {
                float since;
                string stowFor = stowSince.TryGetValue(e.entityId, out since) ? F1(now - since) + " s" : "a while";
                // round md2 (review MD2-m4): clients show 0-XNPCCore's chat stow from SdcsChatStowed (HeldSyncRules.StowShowAction
                // stows their hand while it is 1), so it reads 0 for the use - 0-XNPCCore publishes only when its own flag changes,
                // and RestoreStow writes 1 back while that flag still stands. Only when the flag can be read
                if (e.Buffs != null && e.Buffs.GetCustomVar("SdcsChatStowed") >= 0.5f && ChatStowFlag(e) == true)
                {
                    e.Buffs.SetCustomVar("SdcsChatStowed", 0f, true, CVarOperation.set, true);
                    u.ClearedChatCvar = true;
                }
                hand.SelectHoldingMode(Hand.HoldingMode.Current, 0.2f);
                u.DrewHand = true;
                handsDrawn++;
                Patches_Xnpc.Count("medic-drew-stowed-hand");
                ScLog.Out(BodyName(e) + "'s hand has been stowed for " + stowFor + " - " + She(e) + " draws to use " + item + (lastResort != null ? " at once (about to go down: " + lastResort + ")" : fightWhy != null ? " at once (in a fight: " + fightWhy + ")" : "")
                          + ", and the stow comes back after it if it still stands (round md2)");
            }
            stowSince.Remove(e.entityId);
            stowSeen.Remove(e.entityId);
#endif
            Hold(e, u.Slot, new ItemValue(ic.Id), 1);
#if SC_V34
            if (displacedCopy != null && u.Displaced.itemValue.type == 0)
            {
                u.Displaced = new ItemStack(displacedCopy, displacedCount);
                Patches_Xnpc.Count("medic-displaced-cleared-in-place");
            }
#endif
            // after the hold: an L&L gun has just been emptied in place (its magazine is in the bag), a loose one keeps its rounds
            displaced[e.entityId] = new Displaced(u.Displaced.IsEmpty() ? null : u.Displaced.itemValue, item);
            string sound;
            DynamicProperties a0;
            if (ic.Properties != null && ic.Properties.Classes.TryGetValue("Action0", out a0) && a0.Values.TryGetValue("Sound_start", out sound) && !string.IsNullOrEmpty(sound))
                e.PlayOneShot(sound);
            ScLog.Verbose("medic: " + e.entityId + " starts using " + item + " at " + e.Health + "/" + e.GetMaxHealth() + " (until its clip ends; round md2)");
        }

        private static void Advance(EntityAlive e, Use u)
        {
            if (e.IsDead()) { Finish(e, u, false, false); return; }
            if (e.emodel == null || e.emodel.IsRagdollActive) { Finish(e, u, false, true); return; }
#if SC_V34
            if (ScDowned.IsDown(e)) { u.EndedBy = She(e) + " went down"; Finish(e, u, false, true); return; }     // round md2 (review MD2-n5)
#endif
            float now = Time.time;
            // round md2 (review MD2-n2): one absolute deadline per use
            if (now - u.Started > UseDeadlineSeconds)
            {
                u.EndedBy = "the " + F1(UseDeadlineSeconds) + " s deadline";
                Finish(e, u, u.Triggered, !u.Triggered);
                return;
            }
            AvatarController ac = e.emodel.avatarController;
            Animator anim = ac != null ? ac.anim : null;
            // round md2: the use layer (looked up once per use)
            if (u.UseLayer == -2) u.UseLayer = anim != null ? anim.GetLayerIndex("UpperBodyUseAndReload") : -1;
            if (!u.Triggered)
            {
                // round md2: the trigger only once the item is really in her hand and the animator shows its hold pose - else the clip
                // never plays; not up within HoldPoseWaitSeconds: called off, the item back in her pack (a last resort goes on)
                ItemValue hv = e.inventory != null ? e.inventory.holdingItemItemValue : null;
                bool inHand = hv != null && hv.type == u.ItemType;
                bool shown = inHand && anim != null && anim.GetInteger(AvatarController.weaponHoldTypeHash) == u.HoldType;
                // round md2: the pose shown for HoldPoseSettleSeconds without a break - its blend in is done before the trigger
                if (!shown) u.ShownAt = -1f;
                else if (u.ShownAt < 0f) u.ShownAt = now;
                if (shown && now - u.ShownAt >= HoldPoseSettleSeconds) { FireTrigger(ac, anim, u, now, true); return; }
                if (shown) return;
                if (now - u.Started <= HoldPoseWaitSeconds) return;
                if (u.NoClipOk)
                {
                    // round md2 (review MD2-m3): no pose, nothing on screen - one trigger and a short end, no clip wait
                    FireTrigger(ac, anim, u, now, false);
                    u.NoPose = true;
                    u.EndAt = now + NoPoseUseSeconds;
                    usesNoClip++;
                    Patches_Xnpc.Count("medic-use-no-clip");
                    ScLog.Out(BodyName(e) + " uses " + u.Item + " without its clip (no hold pose in " + F1(HoldPoseWaitSeconds) + " s): " + u.NoClipWhy + " (round md2)");
                    return;
                }
                CancelUse(e, u, now, "the hold pose did not come up in " + F1(HoldPoseWaitSeconds) + " s (" + (inHand ? "the animator shows hold type "
                          + (anim != null ? anim.GetInteger(AvatarController.weaponHoldTypeHash).ToString() : "-") + ", not " + u.HoldType : "the item is not in " + Her(e) + " hand: " + HandText(e)) + ")");
                return;
            }
            if (u.NoPose) { }                                                        // round md2 (review MD2-m3): EndAt only
            else if (u.UseLayer >= 0 && anim != null)
            {
                AnimatorStateInfo si = anim.GetCurrentAnimatorStateInfo(u.UseLayer);
                bool hasClip = anim.GetCurrentAnimatorClipInfoCount(u.UseLayer) > 0;
                if (!u.SawClip)
                {
                    // round md2: the use clip is a NEW state with a clip on the layer (round hr1 took any clip there - a reload's - and
                    // ended the use when that one left: 0.15 s, no animation)
                    AnimatorClipInfo[] ci = hasClip && si.fullPathHash != u.PreState ? anim.GetCurrentAnimatorClipInfo(u.UseLayer) : null;
                    AnimationClip clip = ci != null && ci.Length > 0 ? ci[0].clip : null;
                    // round md2: the item's hold pose (3P_Bandage_Hold) coming up on the layer after the trigger is not the use clip
                    if (clip != null && clip.name != null && clip.name.IndexOf("Hold", StringComparison.OrdinalIgnoreCase) >= 0) u.PreState = si.fullPathHash;
                    else if (hasClip && si.fullPathHash != u.PreState)
                    {
                        u.SawClip = true;
                        u.UseState = si.fullPathHash;
                        // round md2 (review MD2-n1): the animator's own speed too
                        float speed = Mathf.Abs(si.speed * si.speedMultiplier * anim.speed);
                        if (speed < 0.05f || float.IsNaN(speed)) speed = 1f;
                        u.ClipName = clip != null ? clip.name : "?";
                        u.ClipLength = clip != null ? clip.length / speed : ClipFallbackSeconds;
                        float left = clip != null ? Mathf.Max(0f, 1f - si.normalizedTime) * u.ClipLength : ClipFallbackSeconds;
                        // round md2 (review MD2-n2): a NaN or runaway length is clamped
                        if (float.IsNaN(left) || float.IsInfinity(left)) left = ClipFallbackSeconds;
                        left = Mathf.Clamp(left, 0f, ClipFallbackSeconds * 2f);
                        u.EndAt = now + left + ClipEndMargin;
                        calledOff.Remove(e.entityId);
                        if (u.NoClipHoldType && holdTypesLogged.Add(-u.HoldType - 1))
                            ScLog.Out("medic: hold type " + u.HoldType + " (" + u.Item + ") played the clip " + u.ClipName + " (" + F2(u.ClipLength) + " s) on UpperBodyUseAndReload - its uses end with it (round md2, review)");
                    }
                    else if (u.NoClipHoldType) { if (u.EndAt <= 0f) u.EndAt = u.TriggerAt + ClipFallbackSeconds; }   // round md2 (review MD2-M2): timed
                    else if (now - u.LastTriggerAt > ClipStartWaitSeconds)
                    {
                        if (u.Triggers < MaxTriggers) { FireTrigger(ac, anim, u, now, u.Shown); return; }   // once more
                        if (!u.NoClipOk)
                        {
                            CancelUse(e, u, now, "the use clip never started (" + u.Triggers + " triggers, the layer in state " + si.shortNameHash + (hasClip ? " with a clip" : ", empty") + ")");
                            return;
                        }
                        if (u.EndAt <= 0f)
                        {
                            u.EndAt = u.TriggerAt + ClipFallbackSeconds;
                            usesNoClip++;
                            Patches_Xnpc.Count("medic-use-no-clip");
                            ScLog.Out(BodyName(e) + " uses " + u.Item + " without its clip: " + u.NoClipWhy + " (round md2)");
                        }
                    }
                }
                else if (!hasClip || si.fullPathHash != u.UseState)
                {
                    u.EndedBy = "the clip leaving its layer (" + u.ClipName + ", " + F2(u.ClipLength) + " s)";
                    Finish(e, u, true, false);
                    return;
                }
            }
            else if (u.EndAt <= 0f) u.EndAt = u.TriggerAt + ClipFallbackSeconds;     // no layer to watch: the clip's measured length
            if (u.EndAt > 0f && now >= u.EndAt)
            {
                u.EndedBy = u.SawClip ? "the clip's own end + " + F2(ClipEndMargin) + " s (" + u.ClipName + ", " + F2(u.ClipLength) + " s; its layer still showed it)"
                          : u.NoPose ? "the " + F1(NoPoseUseSeconds) + " s use without its hold pose (round md2, review)"
                          : u.NoClipHoldType ? "the " + F1(ClipFallbackSeconds) + " s timed use (no clip for hold type " + u.HoldType + ")"
                          : "the " + F1(ClipFallbackSeconds) + " s fallback (" + (u.UseLayer < 0 ? "no use layer to watch" : "no clip seen") + ")";
                Finish(e, u, true, false);
            }
        }

        /// <summary>
        /// Round md2: the use trigger (vanilla's UseItem, sent to every client by the avatar controller's own animation sync -
        /// _setTrigger with netsync, NetPackageEntityAnimationData), the use layer's state at that moment (the use clip is a new one),
        /// and the clocks.
        /// </summary>
        private static void FireTrigger(AvatarController ac, Animator anim, Use u, float now, bool shown)
        {
            if (u.Triggers == 0)
            {
                u.TriggerAt = now;
                u.Shown = shown;
                u.PreState = u.UseLayer >= 0 && anim != null ? anim.GetCurrentAnimatorStateInfo(u.UseLayer).fullPathHash : 0;
            }
            if (ac != null) ac._setTrigger(AvatarController.useItemHash);
            u.Triggered = true;
            u.Triggers++;
            u.LastTriggerAt = now;
        }

        /// <summary>
        /// Round md2: a use that cannot show its animation is called off before any effect - the item back in her pack, her weapon
        /// back in her hand - and tried again in RetrySeconds (not the 12 s cooldown: nothing was spent; RetryLongSeconds from
        /// MaxCalledOff in a row on). Logged; counted toward MaxCalledOff (after which, with no player near, a use goes on without it).
        /// </summary>
        private static void CancelUse(EntityAlive e, Use u, float now, string why)
        {
            u.EndedBy = "called off: " + why;
            Finish(e, u, false, true);
            // round md2 (review MD2-m6): no use began
            lastUseAt.Remove(e.entityId);
            // round md2 (review MD2-M2): the row is per item; a row whose last call-off is CallOffRowGap old starts again
            CallOffs row;
            if (!calledOff.TryGetValue(e.entityId, out row) || row.Item != u.Item || now - row.Last > CallOffRowGap)
            {
                row = new CallOffs { Item = u.Item, First = now };
                calledOff[e.entityId] = row;
            }
            row.N++;
            row.Last = now;
            int n = row.N;
            float retry = n >= MaxCalledOff ? RetryLongSeconds : RetrySeconds;
            readyAt[e.entityId] = now + retry;
            // round md2 (review MD2-M1): the reach wait this use was released from comes back (its clock and verdict too), so the
            // retry does not start a fresh MedicMaxWait
            // round md2 (final check R28-m2): only that same wait (its start time), only if no other wait began since and it has not
            // ended - an enemy after her still in reach; otherwise the retry starts fresh under the reach rules
            if (u.WaitSince >= 0f && !heldSince.ContainsKey(e.entityId) && EnemyInWaitReach(e))
            {
                heldSince[e.entityId] = u.WaitSince;
                heldLast[e.entityId] = now;
            }
            usesCalledOff++;
            Patches_Xnpc.Count("medic-use-called-off");
            // round md2 (review MD2-M2): the line once per row; the rest verbose
            string line = BodyName(e) + "'s " + u.Item + " is called off: " + why + " - the item goes back, again in " + F1(retry) + " s (called off " + n
                          + " time(s) in a row; from " + MaxCalledOff + " on with no player within " + F1(UnseenMetres) + " m, or at " + CallOffStop + " or "
                          + F1(CallOffStopSeconds) + " s anyway, " + She(e) + " uses one without its clip; round md2)";
            if (n == 1) ScLog.Out(line);
            else ScLog.Verbose(line);
        }

        /// <summary>
        /// Round md2 (review MD2-m4): 0-XNPCCore's own chat stow flag (AvatarSdcsNpcController.SdcsChatHolsterStowed, internal,
        /// read by reflection once looked up), or null when it cannot be read.
        /// </summary>
        private static bool? ChatStowFlag(EntityAlive e)
        {
            try
            {
                if (!chatStowedLooked)
                {
                    chatStowedLooked = true;
                    chatStowedGetter = AccessTools.PropertyGetter(typeof(AvatarSdcsNpcController), "SdcsChatHolsterStowed");
                    if (chatStowedGetter == null) ScLog.Out("medic: 0-XNPCCore's SdcsChatHolsterStowed is not there - a use drawn in a chat stow leaves SdcsChatStowed as it is (round md2, review)");
                }
                var ctl = e != null && e.emodel != null ? e.emodel.avatarController as AvatarSdcsNpcController : null;
                if (chatStowedGetter == null || ctl == null) return null;
                return (bool)chatStowedGetter.Invoke(ctl, null);
            }
            catch (Exception ex)
            {
                ScLog.Once("medic-chat-stow-flag", ex);
                chatStowedGetter = null;
                return null;
            }
        }

        /// <summary>
        /// Round md2: can a use show its animation now? Not while she is down, her ragdoll is on or its get-up still plays
        /// (GetUpSettleSeconds after the ragdoll ended) - not even in a last resort: they are short, and the clip cannot play over
        /// them - or her hand is switching, or her hand is stowed (vanilla's Hand in Bare mode: Hand.Item is then her bare hand
        /// whatever the slot holds, so the item would never show) and has been for less than StowWaitSeconds - after that, or at
        /// once in a fight or a last resort (drawNow, round md2 review MD2-M1), drawHand: the use draws it (Begin). Seated in a
        /// vehicle: no use (round md2 review MD2-m2), a last resort excepted. why: what holds it. Keeps the stow wait's clock.
        /// </summary>
        private static bool HandReady(EntityAlive e, float now, bool drawNow, bool lastResort, out string why, out bool drawHand, out bool brief)
        {
            why = null;
            drawHand = false;
            brief = false;                          // round md2 (final check R28-m2): a short hold - the get-up, a hand switch, the stow wait
#if SC_V34
            if (ScDowned.IsDown(e)) { why = HerObj(e) + " being down"; return false; }
#endif
            if (e.emodel != null && e.emodel.ragdollState != EModelBase.ERagdollState.Off) { why = Her(e) + " ragdoll (" + e.emodel.ragdollState + ")"; return false; }
            float rag;
            if (ragdollAt.TryGetValue(e.entityId, out rag) && now - rag < GetUpSettleSeconds) { why = Her(e) + " get-up (" + F1(now - rag) + " s since the ragdoll ended)"; brief = true; return false; }
            // round md2 (review MD2-m2): no use and no draw in a vehicle seat (vanilla stows the hand there, and nothing would stow it
            // again after the use; the clip is not verified in a seat pose) - in a last resort the use goes on without the clip
            if (e.AttachedToEntity != null)
            {
                if (lastResort) return true;
                why = HerObj(e) + " being seated";
                return false;
            }
#if SC_V34
            Hand h = e.Hand;
            if (h != null && h.IsSwitching) { why = Her(e) + " hand switching items"; brief = true; return false; }
            if (h != null && h.IsHolstered)
            {
                float since, seen;
                if (!stowSince.TryGetValue(e.entityId, out since) || !stowSeen.TryGetValue(e.entityId, out seen) || now - seen > StowSeenGap) since = now;
                stowSince[e.entityId] = since;
                stowSeen[e.entityId] = now;
                if (now - since < StowWaitSeconds && !drawNow)
                {
                    why = Her(e) + " hand stowed (a talk, a station or loot; " + F1(now - since) + " of " + F1(StowWaitSeconds) + " s before " + She(e) + " draws it; at once in a fight)";
                    brief = true;
                    return false;
                }
                drawHand = true;
                return true;
            }
            stowSince.Remove(e.entityId);
            stowSeen.Remove(e.entityId);
#endif
            return true;
        }

        /// <summary>
        /// Round md2: she is about to go down - at or under LastResortBelow of her max health and hit in the last
        /// LastResortHurtSeconds or an enemy after her within LastResortReach. why: which. A pure read.
        /// </summary>
        private static bool LastResort(EntityAlive e, float now, out string why)
        {
            why = null;
            int max = e != null ? e.GetMaxHealth() : 0;
            if (max <= 0 || e.Health > max * LastResortBelow) return false;
            float hit;
            if (hurtAt.TryGetValue(e.entityId, out hit) && now - hit <= LastResortHurtSeconds) { why = e.Health + "/" + max + ", hit " + F1(now - hit) + " s ago"; return true; }
            EntityAlive t = e.GetAttackTarget();
            if (t == null || t == e || t.IsDead()) t = e.GetRevengeTarget();
            if (t != null && t != e && !t.IsDead() && (t.position - e.position).sqrMagnitude <= LastResortReach * LastResortReach)
            {
                why = e.Health + "/" + max + ", " + (t.EntityName ?? "an enemy") + " " + F1((t.position - e.position).magnitude) + " m away";
                return true;
            }
            return false;
        }

        /// <summary>
        /// Round md2: after a use that drew her stowed hand (Begin), the stow comes back if 0-XNPCCore still holds its chat stow (a
        /// talk, a station, loot). A stow it does not hold (one left over from a down) stays drawn. Round md2 (review MD2-m1): an
        /// order (SdcsHolsteredByOrder) is not a hand stow - 0-XNPCCore keeps the bare-hand item in the slot, the Hand in Current -
        /// so it plays no part. Round md2 (review MD2-m4): with SdcsChatStowed cleared for the use, 0-XNPCCore's own flag decides,
        /// and the cvar is written back while it stands.
        /// </summary>
        private static void RestoreStow(EntityAlive e, Use u)
        {
#if SC_V34
            Hand h = e.Hand;
            if (h == null || e.Buffs == null) return;
            bool chat;
            if (u.ClearedChatCvar)
            {
                bool? flag = ChatStowFlag(e);
                chat = flag != false;                                   // unreadable now: put it back as it was found
                if (chat && e.Buffs.GetCustomVar("SdcsChatStowed") < 0.5f) e.Buffs.SetCustomVar("SdcsChatStowed", 1f, true, CVarOperation.set, true);
            }
            else chat = e.Buffs.GetCustomVar("SdcsChatStowed") >= 0.5f;
            if (h.IsHolstered) return;
            if (!chat) { ScLog.Verbose("medic: " + e.entityId + " keeps " + Her(e) + " hand drawn after the use - 0-XNPCCore's chat stow does not stand (round md2)"); return; }
            h.SelectHoldingMode(Hand.HoldingMode.Bare, 0.2f);
            ScLog.Verbose("medic: " + e.entityId + "'s hand is stowed again after the use (0-XNPCCore's chat stow still stands; round md2)");
#endif
        }

        /// <summary>Round md2: a use waits for her hand (counted; logged at most once per 10 s - round md2 review MD2-n4: its own clock is the last line's).</summary>
        private static void NoteNotReady(EntityAlive e, string pick, string why, float now)
        {
            handNotReady++;
            Patches_Xnpc.Count("medic-hand-not-ready");
            float last;
            if (notReadyAt.TryGetValue(e.entityId, out last) && now - last < 10f) return;
            notReadyAt[e.entityId] = now;
            ScLog.Out(BodyName(e) + " waits to use " + pick + " at " + e.Health + "/" + e.GetMaxHealth() + ": " + why + " - the animation could not play (round md2)");
        }

        /// <summary>Round md2: her hand in a few words (the log).</summary>
        private static string HandText(EntityAlive e)
        {
            ItemClass ic = e.inventory != null ? e.inventory.holdingItem : null;
#if SC_V34
            Hand h = e.Hand;
            return (ic != null ? ic.GetItemName() : "nothing") + (h != null && h.IsHolstered ? ", stowed" : "") + (h != null && h.IsSwitching ? ", switching" : "");
#else
            return ic != null ? ic.GetItemName() : "nothing";
#endif
        }

        /// <summary>Round md2: for sc medic-style reports - what would hold a use now (HandReady without its clock writes).</summary>
        private static string HandReadyText(EntityAlive e)
        {
            float now = Time.time, rag, since;
#if SC_V34
            if (ScDowned.IsDown(e)) return " (down)";
#endif
            if (e.emodel != null && e.emodel.ragdollState != EModelBase.ERagdollState.Off) return " (ragdoll " + e.emodel.ragdollState + ")";
            if (ragdollAt.TryGetValue(e.entityId, out rag) && now - rag < GetUpSettleSeconds) return " (get-up)";
            if (stowSince.TryGetValue(e.entityId, out since)) return " (a use waits for the stow " + F1(now - since) + " of " + F1(StowWaitSeconds) + " s)";
            return " (ready)";
        }

        private static string F2(float f) { return f.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture); }

        /// <summary>
        /// Ends a use. completed: the item's effects fire. Otherwise the use was interrupted, and returnItem puts the
        /// item back in the inventory (not for the dead - their inventory has already been dropped). The displaced
        /// weapon always goes back in the hand.
        /// </summary>
        private static void Finish(EntityAlive e, Use u, bool completed, bool returnItem)
        {
            uses.Remove(e.entityId);
            Displaced gone;
            displaced.TryRemove(e.entityId, out gone);
            try
            {
                if (completed)
                {
                    ApplyEffects(e, u.Item);
                    if (u.Bleed) bleedsStopped++;                       // round md2 (review MD2-n3)
                    // round md2 (review MD2-M2): a use whose clip played ends the row (Advance); one that went on without it keeps the
                    // row and its hard stop alive, so the next use does not go through the call-offs again
                    CallOffs row;
                    if (!u.SawClip && calledOff.TryGetValue(e.entityId, out row) && row.Item == u.Item) row.Last = Time.time;
                }
                else if (returnItem)
                {
                    // round md2 (review MD2-m5): a window a player opened during the use would write its copy back over the item on
                    // close - it goes to the bag instead (ScNpc.AddItem's own fallback when the window is full)
                    if (ScNpc.UsesWindow(e) && ScAmmo.IsWindowOpen(e.entityId)) ReturnToBag(e, u.Item);
                    else Add(e, u.Item, 1);
                }
            }
            finally
            {
                if (u.Displaced != null && !u.Displaced.IsEmpty()) Hold(e, u.Slot, u.Displaced.itemValue, u.Displaced.count);
                else Hold(e, u.Slot, ItemValue.None, 0);
                if (u.DrewHand && !e.IsDead()) RestoreStow(e, u);       // round md2
            }
            // round hr1: the use's timing (decision 4's sync check: the hold pose wait, the trigger, what ended it)
            float nowF = Time.time;
            // round md2: the trigger count, and an end before any trigger (called off)
            string timing = u.Triggered ? " after " + (nowF - u.Started).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " s (trigger at "
                                          + (u.TriggerAt - u.Started).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " s, " + (u.Shown ? "the hold pose shown" : "the hold pose not shown")
                                          + (u.Triggers > 1 ? ", " + u.Triggers + " triggers" : "") + (string.IsNullOrEmpty(u.EndedBy) ? "" : "; ended by " + u.EndedBy) + ")"
                                        : " after " + (nowF - u.Started).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " s, no trigger" + (string.IsNullOrEmpty(u.EndedBy) ? "" : " (" + u.EndedBy + ")");
            ScLog.Verbose("medic: " + e.entityId + (completed ? " finished " : " stopped ") + u.Item + " at " + e.Health + "/" + e.GetMaxHealth() + timing);
        }

        /// <summary>
        /// Puts an item in the hand slot and refreshes the hand model: SCore's EntityAliveSDX.UpdateWeapon sequence
        /// (SetItem, SwitchModelAndView, OnUpdate, ForceHoldingItemUpdate) without its bookkeeping - _currentWeapon and
        /// the CurrentWeaponID cvar keep naming the real weapon, so nothing re-equips a bandage later.
        ///
        /// The SwitchModelAndView step is left out: on one of our XNPC bodies XNPC's controller only warns and keeps its
        /// model when it is handed a class name (it has no child of that name), so the call would change nothing but the
        /// log. The rest is the sequence XNPC's own weapon swaps effectively get. (ScLoadout.Arm's fallback for an entity
        /// that is not a framework NPC comes here too; nothing that reaches it today is one.)
        /// </summary>
        internal static void Hold(EntityAlive e, int slot, ItemValue item, int count)
        {
            if (e == null || e.inventory == null) return;
            e.inventory.SetItem(slot, item, count);
#if SC_V34
            // 3.4: no OnUpdate / ForceHoldingItemUpdate any more - the slot write raises the toolbelt grid's SlotChanged and the
            // inventory's Hand rebuilds the held item and its model from it (Inventory.ReconcileHeldItem is the manual nudge,
            // left out unless the model is seen to lag)
#else
            e.inventory.OnUpdate();
            e.inventory.ForceHoldingItemUpdate();
#endif
        }

        /// <summary>
        /// Patches_Npc's death drop, first thing: a body that dies in the middle of a use ends it here, before its bag
        /// and window are collected - the item goes back into the window and the weapon it displaced gives its loaded
        /// rounds to the bag (ScAmmo.ReturnLoose; an L&amp;L gun's magazine went to the bag when the use began), so both
        /// drop with the body instead of being lost with the hand. No effects, no hand change on the dead. True when a use
        /// was ended (SetDead runs again for every hit on the corpse; by then there is none).
        /// </summary>
        internal static bool OnDied(EntityAlive e)
        {
            Use u;
            if (e == null || !uses.TryGetValue(e.entityId, out u)) return false;
            uses.Remove(e.entityId);
            Displaced gone;
            displaced.TryRemove(e.entityId, out gone);
            Add(e, u.Item, 1);
            int n = u.Displaced != null && !u.Displaced.IsEmpty() ? ScAmmo.ReturnLoose(e, u.Displaced.itemValue, "died while using " + u.Item, true) : 0;
            if (n > 0) ScAmmo.NoteDeathReturn(n);
            Patches_Xnpc.Count("medic-died-mid-use");
            return true;
        }

        /// <summary>
        /// ScLoadout.AfterPostInit, first thing (the re-check's bandage case): a body saved while using a medical item
        /// comes back holding it. Chunk saves and unloads write the entity with bNetworkWrite true, so Patch_SaveMidUse
        /// never runs for them, and SCore's load re-equips the saved hand item by name whenever the window still holds one
        /// (FindWeapon) - which 0-XNPCCore never swaps away from (neither melee nor ranged). Here:
        ///  - a use left over from before this body was unloaded in this game ends without effect, and its mirror goes;
        ///  - when the save says a use was in progress (X13's record: ScAmmo.SavedHand), or that leftover use exists, its
        ///    item goes back into the window once: it was taken out when the use began and the hand at the save was
        ///    written nowhere, so the count is the one before the use;
        ///  - the hand gets the pair weapon again: the gun when X13 saved the gun the use displaced (a fresh copy, which
        ///    RestoreHeldGun, next, replaces with the saved one, rounds and magazine), else the melee weapon
        ///    (0-XNPCCore's world-entry re-arm then draws the gun by its own rules, from an L&amp;L gun's magazines in the
        ///    bag).
        /// A medical item in the hand with no such record (a save from before this build, or one handed over and held
        /// through the dialog's weapon menu, whose window copy was never taken) only gets the weapon back: no item is made.
        /// </summary>
        internal static void FixLoadedHand(EntityAlive e)
        {
            if (e == null || e.inventory == null) return;
            int id = e.entityId;
            Use stale;
            bool hadUse = uses.TryGetValue(id, out stale);
            if (hadUse) uses.Remove(id);
            Displaced gone;
            displaced.TryRemove(id, out gone);
            ItemValue savedGun;
            string savedUse;
            bool record = ScAmmo.SavedHand(id, out savedGun, out savedUse);
            string item = !string.IsNullOrEmpty(savedUse) ? savedUse : hadUse ? stale.Item : null;
            ItemValue held = e.inventory.holdingItemItemValue;
            string inHand = held != null && !held.IsEmpty() && held.ItemClass != null ? held.ItemClass.GetItemName() : null;
#if SC_V34
            bool medicalInHand = inHand != null && Array.IndexOf(UseItems, inHand) >= 0;      // 3.4: the cloth bandage too (D2)
#else
            bool medicalInHand = inHand != null && Array.IndexOf(HealItems, inHand) >= 0;
#endif
            if (item == null && !medicalInHand) return;
            if (item != null) Add(e, item, 1);
            if (medicalInHand || item != null)
            {
                Loadout l = ScLoadout.Current(e);
                ItemClass pairGun = l != null && l.HasRanged ? ItemClass.GetItemClass(l.Ranged, true) : null;
                bool ranged = record && savedGun != null && pairGun != null && savedGun.type == pairGun.Id;
                string why;
                if (!ScLoadout.Arm(e, ranged, out why))
                    ScLog.Warn(ScCommunity.DisplayName(e) + " (id " + id + ") loaded holding " + (inHand ?? "-") + " and could not be re-armed: " + why);
            }
            Patches_Xnpc.Count("medic-load-hand-fixed");
            ScLog.Verbose("medic: " + id + " loaded " + (item != null ? "in the middle of using " + item + " - put back in its inventory" : "holding " + inHand)
                          + ", pair weapon back in hand");
        }

        private static void ApplyEffects(EntityAlive e, string item)
        {
#if SC_V34
            if (item == Painkillers) { ApplyPainkiller(e); return; }   // round U4 (D9)
#endif
            ItemClass ic = ItemClass.GetItemClass(item, true);
            if (ic == null) return;
            MinEventParams p = e.MinEventContext;
            p.Self = e;
            p.Other = null;
            p.ItemValue = new ItemValue(ic.Id);
            ic.FireEvent(MinEventTypes.onSelfPrimaryActionEnd, p);
            if (e.Buffs.GetCustomVar("medRegHealthIncSpeed") <= 0f) e.Buffs.SetCustomVar("medRegHealthIncSpeed", NpcRegenSpeed);
            ScLog.Verbose("medic: " + e.entityId + " used " + item + " at " + e.Health + "/" + e.GetMaxHealth()
                          + " (queued " + e.Buffs.GetCustomVar("medicalRegHealthAmount") + ")");
        }

#if SC_V34
        /// <summary>
        /// Round U4 (D9): a painkiller used by one of SC's bodies. Vanilla's painkillers heal 40 once through buffDrugPainkillers,
        /// which counts down by the player's $MetabolismDuration - a cvar an NPC never gets (a perk sets it), so on her the buff
        /// would never end and a second pill would never heal. Here PainkillerHeal goes into the same queue a bandage fills
        /// (medicalRegHealthAmount, worked off by buffProcessConsumables at the NPC regen speed) and a concussion is cured, as the
        /// pill does; no buff stays on her.
        /// </summary>
        private static void ApplyPainkiller(EntityAlive e)
        {
            if (e.Buffs.HasBuff("buffInjuryConcussion")) e.Buffs.RemoveBuff("buffInjuryConcussion");
            e.Buffs.SetCustomVar("medicalRegHealthAmount", e.Buffs.GetCustomVar("medicalRegHealthAmount") + Math.Max(0, ScConfig.PainkillerHeal));
            if (e.Buffs.GetCustomVar("medRegHealthIncSpeed") <= 0f) e.Buffs.SetCustomVar("medRegHealthIncSpeed", NpcRegenSpeed);
            if (!e.Buffs.HasBuff("buffProcessConsumables")) e.Buffs.AddBuff("buffProcessConsumables");
            Patches_Xnpc.Count("medic-painkiller-used");
            ScLog.Out(BodyName(e) + " took painkillers at " + e.Health + "/" + e.GetMaxHealth() + " - the last thing " + She(e) + " had to heal with (round U4)");
        }
#endif

        // ---------------------------------------------------------------------------------------------------------
        // inventory
        // ---------------------------------------------------------------------------------------------------------

        private static ItemStack[] Slots(EntityAlive e)
        {
            return ScNpc.Items(e);
        }

#if SC_V34
        /// <summary>
        /// 3.4 (D2, bag seeding): a survivor's medical items are in its window (SC stocks them there, handed ones land
        /// there) and, since 0-XNPCCore 3.4, in its bag too: XNPC's start route moves the window's medical items into the
        /// bag once per body, and its looting puts them there. So a window user counts both; a raider has only its bag
        /// (Slots). Read only - every write goes through TakeOne.
        /// </summary>
        private static int Count(EntityAlive e, string item)
        {
            int type = ItemClass.GetItem(item, true).type;
            int n = CountIn(Slots(e), type);
            if (ScNpc.UsesWindow(e) && e.bag != null) n += CountIn(e.bag.ItemGrid.items, type);
            return n;
        }

        private static int CountIn(ItemStack[] slots, int type)
        {
            if (slots == null) return 0;
            int n = 0;
            for (int i = 0; i < slots.Length; i++)
                if (slots[i] != null && !slots[i].IsEmpty() && slots[i].itemValue.type == type) n += slots[i].count;
            return n;
        }

        /// <summary>One of the item out of the window first (ScNpc.SetItem, as on 3.2), else out of a window user's bag (Bag.SetSlot).</summary>
        private static bool TakeOne(EntityAlive e, string item)
        {
            int type = ItemClass.GetItem(item, true).type;
            ItemStack[] slots = Slots(e);
            int i = IndexIn(slots, type);
            if (i >= 0)
            {
                ItemStack s = slots[i];
                ScNpc.SetItem(e, i, s.count > 1 ? new ItemStack(s.itemValue, s.count - 1) : ItemStack.Empty.Clone());
                return true;
            }
            if (!ScNpc.UsesWindow(e) || e.bag == null) return false;
            ItemStack[] bag = e.bag.ItemGrid.items;
            i = IndexIn(bag, type);
            if (i < 0) return false;
            ItemStack b = bag[i];
            e.bag.SetSlot(i, b.count > 1 ? new ItemStack(b.itemValue, b.count - 1) : ItemStack.Empty.Clone());
            Patches_Xnpc.Count("medic-took-from-bag");
            return true;
        }

        private static int IndexIn(ItemStack[] slots, int type)
        {
            if (slots == null) return -1;
            for (int i = 0; i < slots.Length; i++)
                if (slots[i] != null && !slots[i].IsEmpty() && slots[i].itemValue.type == type) return i;
            return -1;
        }
#else
        private static int Count(EntityAlive e, string item)
        {
            ItemStack[] slots = Slots(e);
            if (slots == null) return 0;
            int type = ItemClass.GetItem(item, true).type, n = 0;
            for (int i = 0; i < slots.Length; i++)
                if (slots[i] != null && !slots[i].IsEmpty() && slots[i].itemValue.type == type) n += slots[i].count;
            return n;
        }

        private static bool TakeOne(EntityAlive e, string item)
        {
            ItemStack[] slots = Slots(e);
            if (slots == null) return false;
            int type = ItemClass.GetItem(item, true).type;
            for (int i = 0; i < slots.Length; i++)
            {
                ItemStack s = slots[i];
                if (s == null || s.IsEmpty() || s.itemValue.type != type) continue;
                ItemStack left = s.count > 1 ? new ItemStack(s.itemValue, s.count - 1) : ItemStack.Empty.Clone();
                ScNpc.SetItem(e, i, left);
                return true;
            }
            return false;
        }
#endif

        // ---------------------------------------------------------------------------------------------------------
        // restock from the stockpile (round md1, research/flee_meds/DESIGN.md Part B, decisions 14-23)
        // ---------------------------------------------------------------------------------------------------------

        /// <summary>World ticks in one in-game hour (a day is 24000).</summary>
        private const ulong TicksPerHour = 1000UL;
        /// <summary>A member's refusal at home (cooldown, no Meds to spare, a full pack) for the same reason is logged at most this often (real seconds); a new reason at once.</summary>
        private const float RestockLogSeconds = 300f;
        /// <summary>Member id -> the Time.unscaledTime from which her next refusal line at home for the same reason may be written.</summary>
        private static readonly Dictionary<int, float> restockLogAt = new Dictionary<int, float>();
        /// <summary>Member id -> the meds-* counter her last home check ended at: each outcome is counted once per episode, not every 5 s.</summary>
        private static readonly Dictionary<int, string> restockLast = new Dictionary<int, string>();

        /// <summary>One entry of MedsResupplyMix as this world resolved it: the item and the count she is topped up to.</summary>
        private sealed class MixItem
        {
            public string Name;
            public ItemValue Item;
            public int Target;
            /// <summary>
            /// A stack's limit in her window (SCore's HarvestManager.AddItem: Stacknumber) and in her bag (vanilla's
            /// ItemStack.CanStack: ItemClass.MaxCount, Stacknumber x the sandbox MaxStackSizeModifier).
            /// </summary>
            public int WindowMax, BagMax;
        }
        /// <summary>MedsResupplyMix for this world (null: not resolved yet - ResolveMix at GameStartDone, else the first use).</summary>
        private static List<MixItem> mix;
        /// <summary>Entries of MedsResupplyMix that were warned about and skipped when it was resolved.</summary>
        private static int mixSkipped;

        private enum RestockMode { Home, Run, Console }

        /// <summary>What a restock would do for her now, and why not when it would not (Evaluate, a pure read).</summary>
        private sealed class Check
        {
            public bool Due;
            /// <summary>Why not (null when Due): a clause that reads after "... but " and after "...: ".</summary>
            public string Why;
            /// <summary>The meds-* counter the outcome counts under (null: nothing to count - she has enough, she is away ...).</summary>
            public string Counter;
            /// <summary>A refusal worth a log line (at home: at once for a new reason, the same reason at most once per member per 5 minutes).</summary>
            public bool Refusal;
            /// <summary>Heal items she has: first aid kits plus first aid bandages, window and bag, and one in her hand mid-use.</summary>
            public int Heal;
            /// <summary>Meds kept for tonight's injured (MedsResupplyKeepForInjured), and the Meds a restock may take.</summary>
            public int Reserve, Spare;
            /// <summary>The top-up: each mix item and how many she lacks of it.</summary>
            public readonly List<KeyValuePair<MixItem, int>> Gives = new List<KeyValuePair<MixItem, int>>();
            /// <summary>Round k2: at home, an emergency (no heal item, under MedsResupplyEmergencyBelow) that passes her cooldown.</summary>
            public bool Emergency;
            /// <summary>Round k2: the cooldown an emergency passed (world ticks left), for the log line.</summary>
            public ulong CooldownPassed;
        }

        /// <summary>
        /// At GameStartDone (ModApi), and on the first use if that has not run: MedsResupplyMix into items and counts, validated
        /// the way the bundle pools are (ScConfig.ParseBundlePool's rules: item[:count], a count left out is 1, 0 leaves the item
        /// out). An entry that cannot be read, an item this game does not have, or one with a quality (a tool, weapon or armor)
        /// is warned about once and skipped; an item named twice adds its counts. One log line.
        /// </summary>
        public static void ResolveMix()
        {
            try
            {
                List<MixItem> list = Mix();
                ScLog.Out("medical restock: " + (ScConfig.MedsResupply ? "on" : "off (MedsResupply false)") + " - 1 Meds tops a member up to "
                          + (list.Count > 0 ? MixText(list) : "NOTHING (MedsResupplyMix names no usable item: no restock)")
                          + (mixSkipped > 0 ? " (" + mixSkipped + " entr" + (mixSkipped == 1 ? "y" : "ies") + " of MedsResupplyMix skipped, see the warnings above)" : ""));
            }
            catch (Exception e)
            {
                ScLog.Once("meds-mix", e);
            }
        }

        private static List<MixItem> Mix()
        {
            if (mix != null) return mix;
            ScConfig.EnsureParsed();
            var list = new List<MixItem>();
            var names = new List<string>();
            var counts = new List<float>();
            var problems = new List<string>();
            mixSkipped = 0;
            ScConfig.ParseBundlePool(ScConfig.MedsResupplyMix, names, counts, problems);
            foreach (string p in problems)
            {
                mixSkipped++;
                ScLog.Once("meds-mix-parse-" + p, "config MedsResupplyMix: " + p + " - skipped");
            }
            for (int i = 0; i < names.Count; i++)
            {
                ItemClass ic;
                try { ic = ItemClass.GetItemClass(names[i], true); }
                catch (Exception) { ic = null; }
                int n = (int)Math.Floor(counts[i] + 0.5f);
                string why = ic == null ? "no such item" : ic.HasQuality ? "it has a quality (a tool, weapon or armor, not a supply)"
                             : n < 1 ? "its count rounds to 0" : null;
                if (why != null)
                {
                    mixSkipped++;
                    ScLog.Once("meds-mix-skip-" + names[i], "config MedsResupplyMix: '" + names[i] + "' is skipped - " + why);
                    continue;
                }
                if (Math.Abs(counts[i] - n) > 0.001f)
                    ScLog.Once("meds-mix-round-" + names[i], "config MedsResupplyMix: '" + names[i] + "' - a count is whole items, " + n + " used");
                MixItem same = list.Find(x => x.Name == ic.Name);
                if (same != null) { same.Target = Math.Min(100, same.Target + n); continue; }
                list.Add(new MixItem
                {
                    Name = ic.Name,
                    Item = new ItemValue(ic.Id),
                    Target = Math.Min(100, n),
                    WindowMax = ic.CanStack() ? Math.Max(1, ic.Stacknumber != null ? ic.Stacknumber.Value : 1) : 1,
                    BagMax = Math.Max(1, ic.MaxCount)
                });
            }
            mix = list;
            return mix;
        }

        private static string MixText(List<MixItem> list)
        {
            var parts = new List<string>();
            foreach (MixItem x in list) parts.Add(x.Target + " " + x.Name);
            return string.Join(", ", parts.ToArray());
        }

        /// <summary>How many of the item she has: window and bag (Count), plus the one in her hand while a use of it runs.</summary>
        private static int Have(EntityAlive e, string item)
        {
            Use u;
            return Count(e, item) + (uses.TryGetValue(e.entityId, out u) && u.Item == item ? 1 : 0);
        }

        /// <summary>
        /// Round U4 (the Fable review's note): the painkillers she would use as a last resort (MedicPainkillers, a member or a
        /// stand-in), window and bag, one in her hand mid-use; 0 otherwise. For log lines only: HealItemCount (the restock's
        /// threshold) leaves them out.
        /// </summary>
        public static int PainkillerCount(EntityAlive e)
        {
#if SC_V34
            if (e != null && ScConfig.MedicPainkillers && ScXnpc.IsCommunity(e)) return Have(e, Painkillers);
#endif
            return 0;
        }

        /// <summary>Heal items she has (HealItems: first aid kits plus first aid bandages, window and bag, one in her hand mid-use).</summary>
        public static int HealItemCount(EntityAlive e)
        {
            if (e == null) return 0;
            int n = 0;
            foreach (string item in HealItems) n += Have(e, item);
            return n;
        }

        /// <summary>Decision 18: the statuses the stockpile restocks (the ammo resupply's, without its garrison stand-ins).</summary>
        private static bool RestockStatus(Member m)
        {
            return m != null && (m.Status == MemberStatus.Resident || m.Status == MemberStatus.Following || m.Status == MemberStatus.PendingStation);
        }

        /// <summary>Has this body had its one-time stock (Stock, and on 3.4 the cloth bandages)? Before it, its count means nothing yet.</summary>
        private static bool Stocked(EntityAlive e)
        {
            if (e.Buffs == null || e.Buffs.GetCustomVar(StockedCvar) <= 0f) return false;
#if SC_V34
            if (e.Buffs.GetCustomVar(BandagesStockedCvar) <= 0f) return false;
#endif
            return true;
        }

        /// <summary>World ticks since her last restock (0: never, or the clock went back past it).</summary>
        private static ulong Since(Member m, ulong now)
        {
            return m.MedsAt == 0UL || now < m.MedsAt ? 0UL : now - m.MedsAt;
        }

        /// <summary>World ticks until she may be restocked at home again (0: now). Decision 16: MedsResupplyCooldownHours from her last restock.</summary>
        private static ulong CooldownLeft(Member m, ulong now)
        {
            if (m.MedsAt == 0UL) return 0UL;
            ulong cd = (ulong)(Math.Max(0f, ScConfig.MedsResupplyCooldownHours) * TicksPerHour);
            ulong since = Since(m, now);
            return since >= cd ? 0UL : cd - since;
        }

        /// <summary>
        /// Round k2 (research/combat/SC_VS_NPCCORE.md root cause 2): no heal item at all (heal: HealItemCount) and under
        /// MedsResupplyEmergencyBelow of her max health - the case in which md1's 24 h cooldown left Paul fighting 5.5 minutes
        /// with nothing that heals. Not while an item is still healing her (Fable review 2: a kit queues up to 180 health over
        /// 90 s - medicalRegHealthAmount, and foodHealthAmount as RegenBlock counts it): she is not out of healing then. False
        /// with the key at 0. Its own pacing (MedsResupplyEmergencyCooldownHours) is EmergencyLeft's.
        /// </summary>
        private static bool Emergency(EntityAlive e, int heal)
        {
            float below = ScConfig.MedsResupplyEmergencyBelow;
            if (below <= 0f || heal > 0 || e == null) return false;
            if (e.Buffs != null && (e.Buffs.GetCustomVar("medicalRegHealthAmount") > 0f || e.Buffs.GetCustomVar("foodHealthAmount") > 0f)) return false;
            int max = e.GetMaxHealth();
            return max > 0 && e.Health < max * below;
        }

        /// <summary>
        /// Round k2 (Fable review 6): world ticks until an emergency restock may come (0: now) - at least
        /// MedsResupplyEmergencyCooldownHours since her last restock of any kind (her record's meds=, saved, which every restock
        /// sets), so emptying a hurt member's pack through the panel gets her at most one emergency top-up per that many hours.
        /// </summary>
        private static ulong EmergencyLeft(Member m, ulong now)
        {
            if (m.MedsAt == 0UL) return 0UL;
            ulong cd = (ulong)(Math.Max(0f, ScConfig.MedsResupplyEmergencyCooldownHours) * TicksPerHour);
            ulong since = Since(m, now);
            return since >= cd ? 0UL : cd - since;
        }

        private static string Pct(float share) { return (share * 100f).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "%"; }

        /// <summary>World ticks as in-game hours, "6.5 h" (up: rounded up to the tenth, for a wait).</summary>
        private static string Hours(ulong ticks, bool up)
        {
            double tenths = up ? Math.Ceiling(ticks / 100.0) : Math.Floor(ticks / 100.0);
            return (tenths / 10.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " h";
        }

        /// <summary>A member's pronouns for the log lines: her look, else her class (round k2: ScHanded uses them too).</summary>
        internal static bool Male(Member m) { return m.Look != null ? m.Look.Male : ScLooks.ClassIsMale(m.Class); }
        internal static string Her(Member m) { return Male(m) ? "his" : "her"; }
        internal static string HerObj(Member m) { return Male(m) ? "him" : "her"; }
        internal static string She(Member m) { return Male(m) ? "he" : "she"; }
        private static string Who(Member m, EntityAlive e) { return m.Name + " (id " + e.entityId + ")"; }

        /// <summary>Round k2: a body's pronouns - a member's by her record (Male(Member)), a stand-in's or anyone else's by the class.</summary>
        private static bool Male(EntityAlive e)
        {
            Member m = e != null && ScRegistry.Loaded ? ScRegistry.GetMember(e.entityId) : null;
            if (m != null) return Male(m);
            return e != null && ScLooks.ClassIsMale(ScCommunity.ClassName(e));
        }
        internal static string HerObj(EntityAlive e) { return Male(e) ? "him" : "her"; }
        internal static string She(EntityAlive e) { return Male(e) ? "he" : "she"; }
        /// <summary>Round k2b: the possessive, for ScHealBackOff's lines.</summary>
        internal static string Her(EntityAlive e) { return Male(e) ? "his" : "her"; }
        internal static string BodyName(EntityAlive e) { return ScCommunity.DisplayName(e) + " (id " + e.entityId + ")"; }
        private static string F1(float f) { return f.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture); }

        /// <summary>
        /// Round k2: the nearest awake, living enemy of hers (SCore's IsEnemy, not an inert one) within r - with afterHer, only
        /// one whose attack or revenge target she is. Null when none (dist -1). Main thread only (the shared scratch list).
        /// </summary>
        internal static EntityAlive NearestEnemy(EntityAlive e, float r, bool afterHer, out float dist)
        {
            dist = -1f;
            World w = e != null ? e.world : null;
            if (w == null || r <= 0f) return null;
            EntityAlive best = null;
            float bestD2 = r * r;
            near.Clear();
            try
            {
                w.GetEntitiesInBounds(typeof(EntityAlive), new Bounds(e.position, new Vector3(2f * r, 2f * r, 2f * r)), near);
                for (int i = 0; i < near.Count; i++)
                {
                    EntityAlive a = near[i] as EntityAlive;
                    if (a == null || a == e || a.IsDead() || a.IsSleeping) continue;
                    float d2 = (a.position - e.position).sqrMagnitude;
                    if (d2 > bestD2) continue;
                    if (afterHer && a.GetAttackTarget() != e && a.GetRevengeTarget() != e) continue;
                    if (!EntityTargetingUtilities.IsEnemy(e, a) || global::Harmony.ZombieFeatures.InertEntity.IsInert(a)) continue;
                    best = a;
                    bestD2 = d2;
                }
            }
            finally { near.Clear(); }
            if (best != null) dist = Mathf.Sqrt(bestD2);
            return best;
        }

        /// <summary>
        /// At home as the stockpile's resupply counts it - ScAmmo.Where's "home": inside the home zone, or in the defended area of
        /// a live base attack - tested on its own terms rather than by Where's text, which is an outpost's name elsewhere: an
        /// outpost someone named "home" never passes.
        /// </summary>
        private static bool AtHome(EntityAlive e)
        {
            Vector3i p = e.GetBlockPosition();
            if (ScZone.Contains(p)) return true;
            return ScBaseAttack.WideDefence && ScBaseAttack.InDefenceArea(new Vector3(p.x + 0.5f, p.y, p.z + 0.5f));
        }

        /// <summary>Where she is, for the log and sc meds: at home, at an outpost (its name), or away from home.</summary>
        private static string WhereText(EntityAlive e)
        {
            if (AtHome(e)) return "at home";
            string at = ScAmmo.Where(e.GetBlockPosition());
            return at == null ? "away from home" : "at the outpost '" + at + "'";
        }

        /// <summary>
        /// Round k5: where a restock at home stands for her right now, in a few words - sc retreat and ScRetreat's exit lines (the
        /// shelter's restock exit waits for HomeTick's restock). A pure read.
        /// </summary>
        internal static string RestockText(EntityAlive e)
        {
            try
            {
                Member m = e != null ? ScRegistry.GetMember(e.entityId) : null;
                if (m == null || e.world == null) return "-";
                Check c = Evaluate(m, e, RestockMode.Home, e.world.worldTime);
                return c.Due ? (c.Emergency ? "due at the next 5 s check (an emergency: the cooldown ignored)" : "due at the next 5 s check") : "not now - " + c.Why;
            }
            catch (Exception ex)
            {
                ScLog.Once("medic-restock-text", ex);
                return "?";
            }
        }

        /// <summary>
        /// Where a restock stands for her right now - a pure read (UAI never calls it; HomeTick, BeforeRun and sc meds do).
        /// Home: decisions 14 (fewer than MedsResupplyBelow heal items), 16 (the cooldown), 18 (her status, AtHome), not mid-use.
        /// Run: fewer than MedsResupplyRunBelow, the cooldown ignored (decision 16), not mid-use (the run goes all the same).
        /// Console (sc meds give): neither threshold nor cooldown, not mid-use. All three: her window closed (the window copy,
        /// design trap 16), a mix item she lacks, a Meds above the reserve for tonight's injured (decision 19), and room for the
        /// whole top-up (design 3.7).
        /// </summary>
        private static Check Evaluate(Member m, EntityAlive e, RestockMode mode, ulong now)
        {
            var c = new Check();
            c.Heal = HealItemCount(e);
            if (mode != RestockMode.Console && !ScConfig.MedsResupply) { c.Why = "MedsResupply is off (members never restock themselves)"; return c; }
            if (!ScCommunity.IsHost || e.world == null || e.world.IsRemote()) { c.Why = "this is not the host (the stockpile is the host's)"; return c; }
            if (m == null || !IsBody(e) || ScXnpc.IsRaider(e) || e.IsDead()) { c.Why = "this is not a living member's body"; return c; }
            if (!RestockStatus(m)) { c.Why = She(m) + " is " + m.Status + " (a resident, a follower or one on the way to " + Her(m) + " post is restocked)"; return c; }
            if (mode != RestockMode.Console && !Stocked(e)) { c.Why = Her(m) + " first stock is not given yet"; return c; }
            if (mode == RestockMode.Home)
            {
                if (!AtHome(e))
                {
                    string at = ScAmmo.Where(e.GetBlockPosition());
                    c.Why = at == null ? She(m) + " is away from home" : She(m) + " is at the outpost '" + at + "' (no restock at an outpost this round)";
                    return c;
                }
                if (ScConfig.MedsResupplyBelow <= 0) { c.Why = "MedsResupplyBelow is 0 (never at home)"; return c; }
                if (c.Heal >= ScConfig.MedsResupplyBelow)
                {
                    c.Why = She(m) + " carries " + c.Heal + " heal item" + (c.Heal == 1 ? "" : "s") + " (restocked below " + ScConfig.MedsResupplyBelow + ")";
                    return c;
                }
                if (IsUsing(e.entityId)) { c.Why = She(m) + " is using a medical item"; return c; }
                ulong left = CooldownLeft(m, now);
                if (left > 0UL)
                {
                    // round k2: an emergency - no heal item at all, nothing still healing her, under MedsResupplyEmergencyBelow -
                    // passes the cooldown, as a run does, at most once per MedsResupplyEmergencyCooldownHours (Fable review 6);
                    // the reserve, her window, the mix, the stockpile and her pack below still apply
                    bool emergency = Emergency(e, c.Heal);
                    ulong emergencyLeft = emergency ? EmergencyLeft(m, now) : 0UL;
                    if (emergency && emergencyLeft == 0UL)
                    {
                        c.Emergency = true;
                        c.CooldownPassed = left;
                    }
                    else if (emergency && emergencyLeft < left)
                    {
                        c.Why = She(m) + " restocked " + Hours(Since(m, now), false) + " ago - an emergency restock (no heal item, under "
                                + Pct(ScConfig.MedsResupplyEmergencyBelow) + " health) comes in " + Hours(emergencyLeft, true)
                                + " (MedsResupplyEmergencyCooldownHours " + ScConfig.MedsResupplyEmergencyCooldownHours.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + ")";
                        c.Counter = "meds-emergency-cooldown";
                        c.Refusal = true;
                        return c;
                    }
                    else
                    {
                        c.Why = She(m) + " restocked " + Hours(Since(m, now), false) + " ago - next in " + Hours(left, true)
                                + (ScConfig.MedsResupplyEmergencyBelow > 0f
                                   ? " (sooner in an emergency: no heal item, nothing still healing " + HerObj(m) + ", under "
                                     + Pct(ScConfig.MedsResupplyEmergencyBelow) + " health)" : "");
                        c.Counter = "meds-cooldown";
                        c.Refusal = true;
                        return c;
                    }
                }
            }
            else if (mode == RestockMode.Run)
            {
                if (ScConfig.MedsResupplyRunBelow <= 0) { c.Why = "MedsResupplyRunBelow is 0 (no restock before a run)"; return c; }
                if (c.Heal >= ScConfig.MedsResupplyRunBelow)
                {
                    c.Why = She(m) + " carries " + c.Heal + " heal item" + (c.Heal == 1 ? "" : "s") + ", enough for the trip (restocked below " + ScConfig.MedsResupplyRunBelow + ")";
                    return c;
                }
                // a use in progress holds an item in her hand that an interrupted use puts back: as at home, no restock
                // meanwhile - but a run does not wait, so this one is simply missed (logged)
                if (IsUsing(e.entityId)) { c.Why = She(m) + " was using a medical item when sent"; c.Counter = "meds-held-use"; c.Refusal = true; return c; }
            }
            else if (IsUsing(e.entityId)) { c.Why = She(m) + " is using a medical item - try again in a moment"; return c; }
            // the window copy: anything written while her inventory window (or 0-XNPCCore's stock trade) is open is undone or
            // doubled when it closes (ScAmmo.IsWindowOpen); at home the next 5 s check after the close does it
            if (ScNpc.UsesWindow(e) && ScAmmo.IsWindowOpen(e.entityId))
            {
                c.Why = Her(m) + " inventory window (or a trade with " + (Male(m) ? "him" : "her") + ") is open";
                c.Counter = "meds-held-window";
                c.Refusal = mode != RestockMode.Home;
                return c;
            }
            List<MixItem> items = Mix();
            if (items.Count == 0) { c.Why = "MedsResupplyMix names no usable item"; c.Counter = "meds-refused-mix"; c.Refusal = true; return c; }
            foreach (MixItem x in items)
            {
                int lack = x.Target - Have(e, x.Name);
                if (lack > 0) c.Gives.Add(new KeyValuePair<MixItem, int>(x, lack));
            }
            if (c.Gives.Count == 0) { c.Why = Her(m) + " pack already holds everything MedsResupplyMix gives"; c.Counter = "meds-refused-nothing"; c.Refusal = true; return c; }
            int stock = ScRegistry.Community.Get(Resource.Meds);
            c.Reserve = ScConfig.MedsResupplyKeepForInjured ? ScStockpile.MedsForRollover() : 0;
            c.Spare = Math.Max(0, stock - c.Reserve);
            if (stock <= 0)
            {
                c.Why = "the stockpile has no Meds (pack some at the packing station)";
                c.Counter = "meds-refused-empty";
                c.Refusal = true;
                return c;
            }
            if (c.Spare < 1)
            {
                c.Why = "the stockpile has no Meds to spare (" + c.Reserve + " kept for tonight's injured)";
                c.Counter = "meds-refused-reserve";
                c.Refusal = true;
                return c;
            }
            if (!Fits(e, c.Gives))
            {
                c.Why = "there is no room in " + Her(m) + " pack for a medical restock";
                c.Counter = "meds-refused-full";
                c.Refusal = true;
                return c;
            }
            c.Due = true;
            return c;
        }

        /// <summary>
        /// Does the whole top-up fit (design 3.7: all of it or nothing, never anything dropped at her feet)? Simulated one item
        /// at a time on copies of her stores, in the order Place fills them: her inventory window (SCore's HarvestManager.AddItem:
        /// a stack of the item with room first, then an empty slot), then her bag (vanilla's ItemStackGrid.AddItem: the same,
        /// one item at a time). A pure read.
        /// </summary>
        private static bool Fits(EntityAlive e, List<KeyValuePair<MixItem, int>> gives)
        {
            int[] wt, wc, bt, bc;
            CopySlots(ScNpc.UsesWindow(e) ? ScNpc.Items(e) : null, out wt, out wc);
            CopySlots(e.bag != null ? e.bag.ItemGrid.items : null, out bt, out bc);
            foreach (var g in gives)
            {
                int type = g.Key.Item.type;
                for (int k = 0; k < g.Value; k++)
                    if (!FitOne(wt, wc, type, g.Key.WindowMax) && !FitOne(bt, bc, type, g.Key.BagMax)) return false;
            }
            return true;
        }

        private static void CopySlots(ItemStack[] slots, out int[] types, out int[] counts)
        {
            int n = slots != null ? slots.Length : 0;
            types = new int[n];
            counts = new int[n];
            for (int i = 0; i < n; i++)
            {
                ItemStack s = slots[i];
                if (s == null || s.IsEmpty()) continue;
                types[i] = s.itemValue.type;
                counts[i] = s.count;
            }
        }

        private static bool FitOne(int[] types, int[] counts, int type, int stackMax)
        {
            for (int i = 0; i < types.Length; i++)
                if (types[i] == type && counts[i] < stackMax) { counts[i]++; return true; }
            for (int i = 0; i < types.Length; i++)
                if (types[i] == 0) { types[i] = type; counts[i] = 1; return true; }
            return false;
        }

        /// <summary>
        /// The top-up's items into her inventory window, else her bag, one at a time as Fits simulated it. Returns how many went
        /// in - also when a store throws part-way (error: the exception, null otherwise), so what went in is still paid for.
        /// Never drops anything (ScNpc.AddItem would, at the body, for what neither takes).
        /// </summary>
        private static int Place(EntityAlive e, ItemValue item, int count, out Exception error)
        {
            error = null;
            bool window = ScNpc.UsesWindow(e);
            int placed = 0;
            try
            {
                for (int k = 0; k < count; k++)
                {
                    if (window && HarvestManager.AddItem(e.entityId, new ItemStack(item.Clone(), 1))) { placed++; continue; }
                    if (e.bag != null && e.bag.AddItem(new ItemStack(item.Clone(), 1))) { placed++; continue; }
                    break;
                }
            }
            catch (Exception ex)
            {
                error = ex;
            }
            return placed;
        }

        /// <summary>
        /// The restock itself, once Evaluate found it due: the top-up placed, 1 Meds taken, today's ledger (decision 23), her
        /// cooldown set (decision 16: a restock before a run sets it too), the counter, the log line and a registry save. Returns
        /// null when she was restocked, else why nothing went in (then nothing is taken; the caller counts meds-place-failed
        /// and logs it as a refusal). A store that throws part-way stops the placing; what went in before it is charged as a
        /// restock all the same.
        /// </summary>
        private static string Restock(Member m, EntityAlive e, Check c, RestockMode mode, string how, ulong now)
        {
            var given = new List<string>();
            int placed = 0, missed = 0;
            Exception error = null;
            foreach (var g in c.Gives)
            {
                if (error != null) { missed += g.Value; continue; }
                int put = Place(e, g.Key.Item, g.Value, out error);
                placed += put;
                missed += g.Value - put;
                if (put > 0) given.Add(put + " " + g.Key.Name);
            }
            if (error != null) ScLog.Once("meds-place", error);
            if (placed <= 0)
            {
                // Fits said yes: a store that refused (or failed on) what it had room for. Nothing given, nothing taken
                return error != null ? "putting the items into " + Her(m) + " pack failed (the error is in the log) - no Meds taken"
                                     : "nothing went into " + Her(m) + " pack although it fitted - no Meds taken";
            }
            Community cm = ScRegistry.Community;
            cm.Add(Resource.Meds, -1);
            cm.MedsRestocked++;
            m.MedsAt = now;
            restockLast.Remove(m.Id);
            Patches_Xnpc.Count(mode == RestockMode.Run ? "meds-restock-run" : mode == RestockMode.Console ? "meds-restock-console"
                               : c.Emergency ? "meds-restock-emergency" : "meds-restock");
            ScLog.Out(Who(m, e) + " restocked " + how + ": " + string.Join(", ", given.ToArray()) + " for 1 Meds (" + cm.Get(Resource.Meds) + " left) - "
                      + She(m) + " had " + c.Heal + " heal item" + (c.Heal == 1 ? "" : "s"));
            if (missed > 0)
            {
                Patches_Xnpc.Count("meds-place-short");
                ScLog.Warn(Who(m, e) + ": " + missed + " item(s) of the medical restock did not go into " + Her(m) + " pack although it fitted - not dropped");
            }
            ScRegistry.SaveAtomic();
            return null;
        }

        /// <summary>
        /// ScAmmo.Tick, every 5 s, for each member's own loaded body (never a stand-in): a member at home who is out of heal items
        /// (decision 14) is restocked for 1 Meds, instantly and mid-fight too (decisions 17, 20), once her cooldown has run
        /// (16), with a Meds to spare (19) and room in her pack. Each outcome is counted once per episode (meds-*), not at every
        /// check; a refusal is logged at once when its reason changes (a new episode), and the same reason again at most once
        /// per member per 5 minutes. Host only (ScTicker); SC is single-player.
        /// </summary>
        internal static void HomeTick(Member m, EntityAlive e)
        {
            try
            {
#if SC_V34
                ScDrinks.HomeTick(m, e);   // round U4 (D9): a follower's drinks for putting out fire, from the Water stockpile (its own switch)
#endif
                if (m == null || e == null || !ScConfig.MedsResupply) return;
                World w = e.world;
                if (w == null || w.IsRemote() || !ScCommunity.IsHost) return;
                ulong now = w.worldTime;
                if (m.MedsAt > now)
                {
                    // the clock went back (a time command, an older save): her cooldown runs its full length from now
                    m.MedsAt = now;
                    ScLog.Out(Who(m, e) + ": the world clock went back - " + Her(m) + " medical restock cooldown starts again");
                    ScRegistry.SaveAtomic();
                }
                Check c = Evaluate(m, e, RestockMode.Home, now);
                if (c.Due)
                {
                    // round k2: an emergency restock says so, and what it passed
                    string how = Her(m) + " medical supplies at home"
                                 + (c.Emergency ? " - an EMERGENCY restock: no heal item left at " + e.Health + "/" + e.GetMaxHealth() + " health (under "
                                                  + Pct(ScConfig.MedsResupplyEmergencyBelow) + "), the cooldown (" + Hours(c.CooldownPassed, true) + " left) ignored"
                                                : "");
                    string failed = Restock(m, e, c, RestockMode.Home, how, now);
                    if (failed == null) return;
                    // nothing went in: a refusal like the others, counted once per episode and paced below
                    c.Due = false;
                    c.Why = failed;
                    c.Counter = "meds-place-failed";
                    c.Refusal = true;
                }
                string last;
                restockLast.TryGetValue(m.Id, out last);
                if (c.Counter == null)
                {
                    if (last != null) restockLast.Remove(m.Id);
                    return;
                }
                bool changed = c.Counter != last;
                if (changed)
                {
                    restockLast[m.Id] = c.Counter;
                    Patches_Xnpc.Count(c.Counter);
                    if (!c.Refusal) ScLog.Verbose(Who(m, e) + " needs medical supplies at home, held: " + c.Why);
                }
                if (!c.Refusal) return;
                // a new reason is logged at once; the same reason again at most every RestockLogSeconds
                float t = Time.unscaledTime, due;
                if (!changed && restockLogAt.TryGetValue(m.Id, out due) && t < due) return;
                restockLogAt[m.Id] = t + RestockLogSeconds;
                ScLog.Out(Who(m, e) + " needs medical supplies at home but " + c.Why);
            }
            catch (Exception ex)
            {
                ScLog.Once("meds-home", ex);
            }
        }

        /// <summary>
        /// Before a run (design 3.4, decision 21): ScRuns.Send and ScMining.Send call this once the run is accepted and before
        /// its snapshot (ScRunners.TryCapture), so the items leave with her - on a hidden run they stay with her parked body.
        /// With fewer than MedsResupplyRunBelow heal items she is topped up for 1 Meds, the cooldown ignored and set. It never
        /// blocks the run: with no Meds to spare, no room or her window open she leaves as she is, with a log line. what: the
        /// run in the log line ("food run", "mining run").
        /// </summary>
        public static void BeforeRun(Member m, EntityAlive npc, string what = null)
        {
            try
            {
                if (m == null || npc == null || npc.world == null || !ScConfig.MedsResupply) return;
                string run = string.IsNullOrEmpty(what) ? "run" : what;
                Check c = Evaluate(m, npc, RestockMode.Run, npc.world.worldTime);
                if (c.Due)
                {
                    string failed = Restock(m, npc, c, RestockMode.Run, "before " + Her(m) + " " + run, npc.world.worldTime);
                    if (failed == null) return;
                    c.Why = failed;
                    c.Counter = "meds-place-failed";
                    c.Refusal = true;
                }
                if (c.Counter != null) Patches_Xnpc.Count(c.Counter);
                // a send is a single event, not the 5 s check: its refusal is always logged
                if (c.Refusal) ScLog.Out(Who(m, npc) + " leaves for " + Her(m) + " " + run + " without a medical restock: " + c.Why);
                else ScLog.Verbose(Who(m, npc) + ": no medical restock before " + Her(m) + " " + run + " - " + c.Why);
            }
            catch (Exception ex)
            {
                ScLog.Once("meds-before-run", ex);
            }
        }

        // ---------------------------------------------------------------------------------------------------------
        // regeneration out of combat (round k2, the user's decision: "Regen out of combat, up to 60%")
        // ---------------------------------------------------------------------------------------------------------

        /// <summary>A tick further apart than this (real seconds of game time) from her last one starts her clock again: nothing is paid back for a gap.</summary>
        private const float RegenMaxStep = 10f;
        /// <summary>Body id -> the Time.time of her last tick that regenerated (or started to): present while she regenerates.</summary>
        private static readonly Dictionary<int, float> regenAt = new Dictionary<int, float>();
        /// <summary>Body id -> why she did not regenerate at her last tick (absent while she regenerates, or before her first tick).</summary>
        private static readonly Dictionary<int, string> regenWhy = new Dictionary<int, string>();
        /// <summary>Body id -> health regenerated since the world loaded (sc meds), and the total of all members.</summary>
        private static readonly Dictionary<int, float> regenGained = new Dictionary<int, float>();
        private static float regenTotal;

        /// <summary>
        /// ScAmmo.Tick, every 5 s, for each member's own loaded body (never a stand-in; host only, ScTicker): out of combat she
        /// regains RegenRatePerSecond health per second of game time, up to RegenCapShare of her max. SC took away all three
        /// of 0-XNPCCore's free heals (its self-heal, SdcsIsHealing and its level regeneration: Patch_NoFreeHeal, Patches_Xnpc
        /// X3 and X15) and a member out of heal items stayed where the fight left her - Paul sat at 26% for three minutes.
        /// This is SC's own, slow and capped: above the cap only items heal. It writes her health stat directly (the way
        /// vanilla's EntityStats.UpdateNPCStatsOverTime applies a HealthChangeOT), so no buff is added and nothing that
        /// Patch_NoFreeHeal or X15 refuse is touched. RegenBlock lists everything that holds it; the enemy scan comes last.
        /// </summary>
        internal static void RegenTick(Member m, EntityAlive e)
        {
            try
            {
                if (m == null || e == null) return;
                int id = e.entityId;
                float now = Time.time;
                float cap;
                string why = RegenBlock(m, e, out cap);
                float last;
                bool running = regenAt.TryGetValue(id, out last);
                if (why != null)
                {
                    if (running)
                    {
                        regenAt.Remove(id);
                        bool capped = cap > 0f && e.Stats != null && e.Stats.Health.Value >= cap - 0.01f;
                        if (capped) Patches_Xnpc.Count("meds-regen-capped");
                        ScLog.Verbose(Who(m, e) + " stops regenerating at " + e.Health + "/" + e.GetMaxHealth() + ": " + why);
                    }
                    regenWhy[id] = why;
                    return;
                }
                regenWhy.Remove(id);
                regenAt[id] = now;
                if (!running || now - last > RegenMaxStep)
                {
                    // her clock starts now: the first tick of an episode (or one after a gap) regenerates nothing
                    if (!running)
                    {
                        Patches_Xnpc.Count("meds-regen-started");
                        ScLog.Verbose(Who(m, e) + " regenerates out of combat from " + e.Health + "/" + e.GetMaxHealth() + ", "
                                      + ScConfig.RegenRatePerSecond.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " health/s up to "
                                      + Mathf.FloorToInt(cap) + " (" + Pct(ScConfig.RegenCapShare) + ")");
                    }
                    return;
                }
                float dt = now - last;
                if (dt <= 0f) return;
                Stat hp = e.Stats.Health;
                float before = hp.Value;
                float after = Mathf.Min(cap, before + ScConfig.RegenRatePerSecond * dt);
                if (after <= before) return;
                hp.Value = after;
                float gained = hp.Value - before;
                if (gained <= 0f) return;
                float had;
                regenGained.TryGetValue(id, out had);
                regenGained[id] = had + gained;
                regenTotal += gained;
            }
            catch (Exception ex)
            {
                ScLog.Once("meds-regen", ex);
            }
        }

        /// <summary>
        /// Why her regeneration is held now, or null when it runs (cap: RegenCapShare of her max health, 0 when not known).
        /// A pure read. The double-up guards: a medical use in progress, a heal item still working (medicalRegHealthAmount,
        /// foodHealthAmount: what vanilla's buffHealHealth drains), 0-XNPCCore's level or leadership regeneration, and any
        /// other positive HealthChangeOT on her - each of those already heals her, so this stands aside.
        /// </summary>
        private static string RegenBlock(Member m, EntityAlive e, out float cap)
        {
            cap = 0f;
            if (!ScConfig.RegenOutOfCombat) return "RegenOutOfCombat is off";
            if (ScConfig.RegenRatePerSecond <= 0f || ScConfig.RegenCapShare <= 0f) return "RegenRatePerSecond or RegenCapShare is 0";
            if (!ScCommunity.IsHost || e.world == null || e.world.IsRemote()) return "not the host";
            if (e.IsDead() || e.Health <= 0 || e.Stats == null || e.Stats.Health == null || e.Buffs == null) return "not alive";
            if (!ScXnpc.IsSurvivorBody(e) || ScXnpc.IsRaider(e)) return "not a member's survivor body";
#if SC_V34
            if (ScDowned.IsDown(e)) return She(m) + " is down (0-XNPCCore holds her at its floor health until she is revived)";   // round U2: no regeneration while she is down
#endif
            if (!ScRegistry.Loaded || ScRegistry.GetMember(e.entityId) != m) return "not " + Her(m) + " own body (a stand-in, or an id taken by another)";
            if (m.Offline || ScNpc.IsOnMission(e)) return She(m) + " is hidden or offline (a run, a posting)";
            int max = e.GetMaxHealth();
            if (max <= 0) return "no max health";
            cap = max * ScConfig.RegenCapShare;
            if (e.Stats.Health.Value >= cap - 0.01f) return "at the cap (" + Mathf.FloorToInt(cap) + ", " + Pct(ScConfig.RegenCapShare) + "): above it only items heal";
            if (IsUsing(e.entityId)) return She(m) + " is using a medical item";
            if (e.Buffs.GetCustomVar("medicalRegHealthAmount") > 0f || e.Buffs.GetCustomVar("foodHealthAmount") > 0f) return "a heal item is still working on " + HerObj(m);
            if (e.Buffs.HasBuff("IsHealing") || e.Buffs.HasBuff("SdcsIsHealing")) return "a framework heal is on " + HerObj(m);
            if (e.Buffs.HasBuff("SdcsNpcLevelBonus") && e.Buffs.GetCustomVar(ScXnpc.LevelRegenCvar) > 0f) return "0-XNPCCore's level regeneration is on " + HerObj(m);
            if (e.Buffs.HasBuff("SdcsNpcLeadership") && e.Buffs.GetCustomVar("SdcsLeadRegen") > 0f) return "0-XNPCCore's leadership regeneration (your Charismatic Nature) is on " + HerObj(m);
            float ot = EffectManager.GetValue(PassiveEffects.HealthChangeOT, null, 0f, e);
            if (ot > 0f) return "another regeneration runs on " + HerObj(m) + " (+" + ot.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " health/s)";
            float dist;
            string how;
            EntityAlive t = RegenThreat(e, out dist, out how);
            if (t != null) return "in combat: " + ScCommunity.ClassName(t) + " (" + t.entityId + ") at " + F1(dist) + " m " + how;
            return null;
        }

        /// <summary>
        /// The Fable review (F3; the user: "Only threats to her"): the nearest awake, living enemy of hers (SCore's IsEnemy, not
        /// an inert one) that holds her regeneration - one within RegenNearRadius, whatever it is doing; or one within
        /// RegenEnemyRadius that is after her (attack or revenge target) or can see her (vanilla's CanEntityBeSeen, the test
        /// rt1's threat scan uses). A zombie behind a wall that is not after her, or one chasing someone else beyond
        /// RegenNearRadius, does not hold it. how: which rule, for the line. Null when none. A pure read (the raycast only for
        /// one nearer than the best so far); main thread only (the shared scratch list).
        /// </summary>
        private static EntityAlive RegenThreat(EntityAlive e, out float dist, out string how)
        {
            dist = -1f;
            how = null;
            World w = e != null ? e.world : null;
            float far = ScConfig.RegenEnemyRadius, close = ScConfig.RegenNearRadius;
            float r = Mathf.Max(far, close);
            if (w == null || r <= 0f) return null;
            EntityAlive best = null;
            float bestD2 = float.MaxValue;
            near.Clear();
            try
            {
                w.GetEntitiesInBounds(typeof(EntityAlive), new Bounds(e.position, new Vector3(2f * r, 2f * r, 2f * r)), near);
                for (int i = 0; i < near.Count; i++)
                {
                    EntityAlive a = near[i] as EntityAlive;
                    if (a == null || a == e || a.IsDead() || a.IsSleeping) continue;
                    float d2 = (a.position - e.position).sqrMagnitude;
                    if (d2 >= bestD2 || d2 > r * r) continue;
                    if (!EntityTargetingUtilities.IsEnemy(e, a) || global::Harmony.ZombieFeatures.InertEntity.IsInert(a)) continue;
                    string rule;
                    if (close > 0f && d2 <= close * close) rule = "(within " + F1(close) + " m: RegenNearRadius)";
                    else if (far <= 0f || d2 > far * far) continue;
                    else if (a.GetAttackTarget() == e || a.GetRevengeTarget() == e) rule = "after " + HerObj(e) + " (RegenEnemyRadius " + F1(far) + " m)";
                    else if (a.CanEntityBeSeen(e)) rule = "can see " + HerObj(e) + " (RegenEnemyRadius " + F1(far) + " m)";
                    else continue;
                    best = a;
                    bestD2 = d2;
                    how = rule;
                }
            }
            finally { near.Clear(); }
            if (best != null) dist = Mathf.Sqrt(bestD2);
            return best;
        }

        /// <summary>Round k2: her regeneration as her last tick found it (sc meds, sc xnpc).</summary>
        private static string RegenText(EntityAlive e)
        {
            if (e == null) return "-";
            if (regenAt.ContainsKey(e.entityId)) return "RUNNING (" + ScConfig.RegenRatePerSecond.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
                                                         + " health/s up to " + Pct(ScConfig.RegenCapShare) + ")";
            string why;
            return regenWhy.TryGetValue(e.entityId, out why) ? "held - " + why : "not checked yet (a member's own loaded body, every 5 s; never a stand-in, raider or stranger)";
        }

        private static string RegenGainedText(EntityAlive e)
        {
            float g;
            return e != null && regenGained.TryGetValue(e.entityId, out g) && g > 0f
                ? "; " + g.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " health regenerated since the world loaded" : "";
        }

        /// <summary>Round k2: her wait for an enemy in reach before a medical use (TryHeal's HeldByReach), for sc xnpc and sc meds.</summary>
        private static string WaitText(EntityAlive e)
        {
            float waited;
            if (!WaitingToHeal(e, out waited)) return "no wait" + ScHealBackOff.LastText(e);
            return "WAITING " + F1(waited) + " s to use an item: an enemy after " + HerObj(e) + " within " + F1(ScConfig.MedicSafeDistance) + " m"
                   + "; " + ScHealBackOff.Text(e);
        }

        /// <summary>Her last restock and the next one at home, for sc xnpc's medic line and sc meds.</summary>
        private static string RestockTimes(Member m, ulong now)
        {
            if (m == null) return "not a member";
            if (m.MedsAt == 0UL) return "never restocked";
            ulong left = CooldownLeft(m, now);
            return "last restock " + Hours(Since(m, now), false) + " ago, " + (left > 0UL ? "next at home in " + Hours(left, true) : "the next allowed now");
        }

        private static string RestockTimes(EntityAlive e)
        {
            World w = e != null ? e.world : null;
            Member m = ScRegistry.Loaded && e != null ? ScRegistry.GetMember(e.entityId) : null;
            return RestockTimes(m, w != null ? w.worldTime : 0UL);
        }

        // ---- sc meds ----

        private static Member FindMember(string key)
        {
            int id;
            if (int.TryParse(key, out id)) return ScRegistry.GetMember(id);
            foreach (Member m in ScRegistry.Members.Values)
                if (m.Name != null && m.Name.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0) return m;
            return null;
        }

        /// <summary>
        /// sc meds - the setting, the keys, the mix, the stockpile and its reserve, the counters and one line per loaded member;
        /// sc meds &lt;id|name&gt; - one member's every check; sc meds &lt;id|name&gt; give - restock her now, the cooldown and the thresholds
        /// ignored (not the stockpile, its reserve, her window or her pack), for TESTING.
        /// </summary>
        public static void ConsoleCmd(List<string> p, Action<string> output)
        {
            ScConfig.EnsureParsed();
            if (!ScRegistry.Loaded) { output("[SC] the registry is not loaded"); return; }
            World w = ScCommunity.World;
            ulong now = w != null ? w.worldTime : 0UL;
            Community cm = ScRegistry.Community;
            if (p.Count < 2)
            {
                List<MixItem> list = Mix();
                int reserve = ScConfig.MedsResupplyKeepForInjured ? ScStockpile.MedsForRollover() : 0;
                output("[SC] medical restock (round md1): " + ScConfig.MedsLine());
                output("[SC] healing (round k2): " + ScConfig.HealLine() + " - regenerated so far this session: "
                       + regenTotal.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " health");
                output("[SC] mix: 1 Meds tops a member up to " + (list.Count > 0 ? MixText(list) : "NOTHING (no usable item)")
                       + (mixSkipped > 0 ? " - " + mixSkipped + " entr" + (mixSkipped == 1 ? "y" : "ies") + " skipped (see the log)" : ""));
                output("[SC] stockpile: " + cm.Get(Resource.Meds) + " Meds, " + reserve + " kept for tonight's injured"
                       + (ScConfig.MedsResupplyKeepForInjured ? "" : " (MedsResupplyKeepForInjured false: none kept)")
                       + " - " + Math.Max(0, cm.Get(Resource.Meds) - reserve) + " to spare; restocks since the last rollover: " + cm.MedsRestocked);
                output("[SC] counters (since the world loaded; at home each outcome once per episode): " + Patches_Xnpc.ReportPrefix("meds-"));
                output("[SC] back off, then heal (round k2b): " + ScHealBackOff.Report() + " - counters: " + Patches_Xnpc.ReportPrefix("heal-"));
                int shown = 0, away = 0;
                foreach (Member m in ScRegistry.Members.Values)
                {
                    bool standIn = false;
                    EntityAlive e = w != null ? ScAmmo.BodyOf(w, m, out standIn) : null;
                    if (e == null || standIn) { away++; continue; }
                    shown++;
                    Check c = Evaluate(m, e, RestockMode.Home, now);
                    output("[SC]   " + Who(m, e) + " " + m.Status + ", " + WhereText(e) + ": "
                           + c.Heal + " heal item(s) (" + Have(e, "medicalFirstAidKit") + " kits, " + Have(e, "medicalFirstAidBandage") + " first aid bandages), "
                           + Have(e, "medicalBandage") + " cloth; " + RestockTimes(m, now) + "; at home: "
                           + (c.Due ? (c.Emergency ? "DUE - an EMERGENCY, the cooldown ignored (next check)" : "DUE (next check)") : "not now - " + c.Why)
                           + "; health " + e.Health + "/" + e.GetMaxHealth() + ", regen " + RegenText(e));
                }
                output("[SC] " + shown + " member(s) loaded" + (away > 0 ? ", " + away + " not loaded, away on a run or posted (a stand-in is never restocked)" : ""));
                return;
            }
            Member mm = FindMember(p[1]);
            if (mm == null) { output("[SC] no member '" + p[1] + "' - sc meds [id|name [give]]"); return; }
            bool si = false;
            EntityAlive body = w != null ? ScAmmo.BodyOf(w, mm, out si) : null;
            if (body == null) { output("[SC] " + mm.Name + " (id " + mm.Id + ", " + mm.Status + ") is not loaded here - " + RestockTimes(mm, now)); return; }
            if (si) { output("[SC] " + mm.Name + "'s stand-in (id " + body.entityId + ") is standing in - a stand-in is never restocked (decision 18)"); return; }
            if (p.Count >= 3 && p[2].Equals("give", StringComparison.OrdinalIgnoreCase))
            {
                Check g = Evaluate(mm, body, RestockMode.Console, now);
                if (!g.Due)
                {
                    if (g.Counter != null) Patches_Xnpc.Count(g.Counter);
                    output("[SC] no restock: " + g.Why);
                    return;
                }
                string failed = Restock(mm, body, g, RestockMode.Console, Her(mm) + " medical supplies (sc meds give)", now);
                if (failed != null) Patches_Xnpc.Count("meds-place-failed");
                output("[SC] " + (failed == null ? Who(mm, body) + " restocked (the log has the line): " + HealItemCount(body) + " heal item(s) now, "
                                                   + cm.Get(Resource.Meds) + " Meds left, " + RestockTimes(mm, now)
                                                 : "no restock: " + failed));
                return;
            }
            if (p.Count >= 3) { output("[SC] sc meds [id|name [give]]"); return; }
            Check home = Evaluate(mm, body, RestockMode.Home, now);
            Check run = Evaluate(mm, body, RestockMode.Run, now);
            int res = ScConfig.MedsResupplyKeepForInjured ? ScStockpile.MedsForRollover() : 0;
            output("[SC] " + Who(mm, body) + ", " + mm.Status + ", " + WhereText(body)
                   + (ScConfig.MedsResupply ? "" : " - MedsResupply is OFF"));
            output("[SC]   carries: " + home.Heal + " heal item(s) - " + Have(body, "medicalFirstAidKit") + " medicalFirstAidKit, "
                   + Have(body, "medicalFirstAidBandage") + " medicalFirstAidBandage; " + Have(body, "medicalBandage") + " medicalBandage"
                   + (IsUsing(body.entityId) ? " (one in " + Her(mm) + " hand: a use is running)" : "") + (Stocked(body) ? "" : "; FIRST STOCK NOT GIVEN YET"));
            output("[SC]   thresholds: at home below " + ScConfig.MedsResupplyBelow + ", before a run below " + ScConfig.MedsResupplyRunBelow
                   + (ScConfig.MedsResupplyEmergencyBelow > 0f
                      ? "; an emergency at home (no heal item, nothing still healing " + HerObj(mm) + ", under " + Pct(ScConfig.MedsResupplyEmergencyBelow)
                        + " health) ignores the cooldown, at most every " + ScConfig.MedsResupplyEmergencyCooldownHours.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
                        + " in-game h - " + (Emergency(body, home.Heal) ? "IT IS ONE NOW" + (EmergencyLeft(mm, now) > 0UL ? " (next allowed in " + Hours(EmergencyLeft(mm, now), true) + ")" : "") : "not now")
                      : "; no emergency restock (MedsResupplyEmergencyBelow 0)"));
            output("[SC]   " + RestockTimes(mm, now) + " (cooldown " + ScConfig.MedsResupplyCooldownHours.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
                   + " in-game h at home; a run ignores it)" + (mm.MedsAt > 0UL ? ", world time " + mm.MedsAt + " (now " + now + ")" : ""));
            output("[SC]   window or trade open: " + (ScNpc.UsesWindow(body) && ScAmmo.IsWindowOpen(body.entityId) ? "YES (held until it closes)" : "no"));
            var lack = new List<string>();
            foreach (MixItem x in Mix())
            {
                int l = x.Target - Have(body, x.Name);
                if (l > 0) lack.Add(l + " " + x.Name);
            }
            output("[SC]   top-up now: " + (lack.Count > 0 ? string.Join(", ", lack.ToArray()) : "nothing (" + She(mm) + " has the whole mix)"));
            output("[SC]   stockpile: " + cm.Get(Resource.Meds) + " Meds, " + res + " kept for tonight's injured, " + Math.Max(0, cm.Get(Resource.Meds) - res) + " to spare");
            output("[SC]   at home now: " + (home.Due ? "DUE - the next 5 s check restocks " + HerObj(mm) + (home.Emergency ? " (an EMERGENCY: the cooldown ignored)" : "") : "no - " + home.Why));
            output("[SC]   before a run now: " + (run.Due ? "would restock (the cooldown ignored)" : "no - " + run.Why));
            // round k2: her health, the self-use rule and the regeneration
            output("[SC]   health " + body.Health + "/" + body.GetMaxHealth() + "; medical use: " + WaitText(body)
                   + (ScConfig.MedicSafeDistance > 0f ? " (none while an enemy after " + HerObj(mm) + " is within " + F1(ScConfig.MedicSafeDistance) + " m)" : " (MedicSafeDistance 0: no wait)"));
            output("[SC]   back off, then heal (round k2b): " + ScHealBackOff.Describe(body, mm));
            output("[SC]   regeneration (k2): " + RegenText(body) + RegenGainedText(body));
        }

        // ---------------------------------------------------------------------------------------------------------
        // patches
        // ---------------------------------------------------------------------------------------------------------

        [HarmonyPatch(typeof(EntityAlive), nameof(EntityAlive.OnUpdateLive))]
        public static class Patch_Tick
        {
            [HarmonyPostfix]
            public static void Postfix(EntityAlive __instance)
            {
                try
                {
                    if (__instance == null || !ScXnpc.IsBody(__instance)) return;
                    using (ScGates.ScopeOf(__instance)) Tick(__instance);     // round MP6 (part G): a member's restock is her community's
                }
                catch (Exception ex)
                {
                    ScLog.Once("medic-tick", ex);
                }
            }
        }

        /// <summary>
        /// Refuses the frameworks' own heals for our bodies (see the class summary): the NPC framework's free IsHealing,
        /// and 0-XNPCCore's SdcsIsHealing. Both are added through this AddBuff by a buff's triggered effect. XNPC's attack
        /// actions gate on SdcsIsHealing, so refusing it also keeps them from standing down for a heal that never comes.
        /// </summary>
        [HarmonyPatch(typeof(EntityBuffs), nameof(EntityBuffs.AddBuff),
            new[] { typeof(string), typeof(Vector3i), typeof(int), typeof(bool), typeof(bool), typeof(float) })]
        public static class Patch_NoFreeHeal
        {
            /// <summary>Times the XNPC heal was refused (sc xnpc). Main thread only.</summary>
            internal static int SdcsRefused;

            [HarmonyPrefix]
            public static bool Prefix(EntityBuffs __instance, string _name, ref EntityBuffs.BuffStatus __result)
            {
                if (_name == null) return true;
                bool xnpcHeal = string.Equals(_name, "SdcsIsHealing", StringComparison.OrdinalIgnoreCase);
                if (!xnpcHeal && !string.Equals(_name, "IsHealing", StringComparison.OrdinalIgnoreCase)) return true;
                if (__instance == null || !IsBody(__instance.parent)) return true;
                if (xnpcHeal) SdcsRefused++;
                __result = EntityBuffs.BuffStatus.FailedImmune;
                return false;
            }
        }

        /// <summary>No attacking with a bandage: the vanilla and raider attack path, while an item is in use.</summary>
        [HarmonyPatch(typeof(EntityAlive), nameof(EntityAlive.UseHoldingItem))]
        public static class Patch_NoAttackWhileUsing
        {
            [HarmonyPrefix]
            public static bool Prefix(EntityAlive __instance, ref bool __result)
            {
                if (__instance == null || !uses.ContainsKey(__instance.entityId)) return true;
                __result = false;
                return false;
            }
        }

        /// <summary>
        /// No attacking with a bandage: SCore's own attack path, ExecuteAction(bool, int), which each of its two NPC classes
        /// declares for itself (ScNpc.Declared). Only our bodies ever have a use in progress, so on anyone else it is a no-op.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_NoSdxAttackWhileUsing
        {
            private static readonly Type[] Args = { typeof(bool), typeof(int) };

            [HarmonyPrepare]
            public static bool Prepare() { return ScNpc.Declared("ExecuteAction", Args).Count > 0; }

            [HarmonyTargetMethods]
            public static IEnumerable<MethodBase> TargetMethods() { return ScNpc.Declared("ExecuteAction", Args); }

            [HarmonyPrefix]
            public static bool Prefix(EntityAlive __instance, ref bool __result)
            {
                if (__instance == null || !uses.ContainsKey(__instance.entityId)) return true;
                __result = false;
                return false;
            }
        }

        /// <summary>
        /// A world save in the middle of a use would record the bandage as the NPC's hand item (SCore writes the held
        /// item's name). Put the weapon back and the item back in the inventory before the entity is written. Both of
        /// SCore's NPC classes write the held item that way, each in its own Write override.
        /// Only for a non-network write, which in practice is ScRunners' snapshot alone: chunk saves and unloads write
        /// every entity with bNetworkWrite true (vanilla Chunk.cs), and finishing a use there would run Unity calls on the
        /// save thread and end a heal on every spawn sent to a client. Those saves keep the use (ScAmmo.WriteHeldGun saves
        /// the displaced gun and the item) and the load undoes it (FixLoadedHand).
        /// </summary>
        [HarmonyPatch]
        public static class Patch_SaveMidUse
        {
#if SC_V34
            // 3.4 (SCore 3.4): Write(PooledBinaryWriter _bw, StreamModeWrite _eStreamMode) on both classes
            private static readonly Type[] Args = { typeof(PooledBinaryWriter), typeof(StreamModeWrite) };
#else
            private static readonly Type[] Args = { typeof(System.IO.BinaryWriter), typeof(bool) };
#endif

            [HarmonyPrepare]
            public static bool Prepare() { return ScNpc.Declared("Write", Args).Count > 0; }

            [HarmonyTargetMethods]
            public static IEnumerable<MethodBase> TargetMethods() { return ScNpc.Declared("Write", Args); }

            [HarmonyPrefix]
#if SC_V34
            public static void Prefix(EntityAlive __instance, StreamModeWrite _eStreamMode)
            {
                // 3.2's bNetworkWrite: everything but a Persistency write. 3.4's chunk saves and unloads, spawns sent to a
                // client and player data write ToClient (3.2's true); ScRunners' snapshot writes Persistency (3.2's false).
                bool bNetworkWrite = _eStreamMode != StreamModeWrite.Persistency;
#else
            public static void Prefix(EntityAlive __instance, bool bNetworkWrite)
            {
#endif
                try
                {
                    Use u;
                    if (bNetworkWrite || __instance == null || !uses.TryGetValue(__instance.entityId, out u)) return;
                    Finish(__instance, u, false, !__instance.IsDead());
                }
                catch (Exception ex)
                {
                    ScLog.Once("medic-save", ex);
                }
            }
        }
#if SC_V34

        // ---------------------------------------------------------------------------------------------------------
        // 0-XNPCCore 3.4's party medic (SdcsNpcMedic: MoveToPatientSdcs / FirstAidSdcs) on and around our bodies (D2)
        // ---------------------------------------------------------------------------------------------------------

        /// <summary>3.4: 0-XNPCCore's heal of another (SdcsMedic.ApplyHealOther(healer, patient, itemName, via), public static).</summary>
        public static MethodBase HealOtherTarget()
        {
            return AccessTools.DeclaredMethod(typeof(SdcsMedic), "ApplyHealOther", new[] { typeof(EntityAlive), typeof(EntityAlive), typeof(string), typeof(string) });
        }

        /// <summary>3.4: 0-XNPCCore's start of a first-aid sequence on another (SdcsMedic.Begin, public static).</summary>
        public static MethodBase MedicBeginTarget()
        {
            return AccessTools.DeclaredMethod(typeof(SdcsMedic), "Begin", new[] { typeof(EntityAlive), typeof(EntityAlive), typeof(NPCSdcsCharacters.Pure.MedicCondition), typeof(string) });
        }

        /// <summary>
        /// 3.4 (D2). A follower of ours that 0-XNPCCore made the party medic (its leader is its patient, or anyone with the
        /// medic role) spends one item out of its stores and fires the item's "other" effects on the patient
        /// (onSelfSecondaryActionEnd with the item in the event): the bleed stops, the item's health is queued
        /// (medicalRegHealthAmount), and the regen speed is set from the PATIENT's $medicRegHealthIncreaseSpeed - which only
        /// a player's Physician perk writes - while the perk-level fallbacks to 2 read the healer's progression, which an NPC
        /// has none of. On a player patient that is fine; on an NPC the speed stays 0 and buffHealHealth drains the queued
        /// health without healing a point (the hole ScMedic's own ApplyEffects plugs) - an item spent for nothing, and
        /// ScMedic's own heal held off meanwhile (a heal "working"). So when the patient is one of our bodies the speed gets
        /// NpcRegenSpeed (2, the no-perk value). Nothing in this path is refused by Patch_NoFreeHeal: it adds no
        /// SdcsIsHealing to anyone.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_XnpcHealOther
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                try
                {
                    if (HealOtherTarget() != null) return true;
                    ScLog.Warn("0-XNPCCore's SdcsMedic.ApplyHealOther not found - its party medic's heals on the community's bodies may not heal (0-XNPCCore update?)");
                }
                catch (Exception e) { ScLog.Once("medic-bind-healother", e); }
                return false;
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return HealOtherTarget(); }

            [HarmonyPostfix]
            public static void Postfix(EntityAlive patient, bool __result)
            {
                try
                {
                    if (!__result || patient == null || patient.Buffs == null || !IsBody(patient)) return;
                    if (patient.Buffs.GetCustomVar("medicalRegHealthAmount") <= 0f || patient.Buffs.GetCustomVar("medRegHealthIncSpeed") > 0f) return;
                    patient.Buffs.SetCustomVar("medRegHealthIncSpeed", NpcRegenSpeed);
                    xnpcHealSpeedFixed++;
                }
                catch (Exception ex)
                {
                    ScLog.Once("medic-healother", ex);
                }
            }
        }

        /// <summary>
        /// 3.4 (D2): 0-XNPCCore's party medic does not start on a body of ours in the middle of a use here. Its sequence
        /// puts the item in hand slot 0 (SdcsAnimPlayback.StartBandage) and puts back whatever was there when it began - the
        /// medical item this file holds - so the weapon this use displaced would be lost to the hand. The other order is
        /// TryHeal's SdcsAnimBusy wait. False out of Begin is XNPC's own refusal: the task stops and nothing is spent.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_XnpcMedicWaits
        {
            [HarmonyPrepare]
            public static bool Prepare()
            {
                try
                {
                    if (MedicBeginTarget() != null) return true;
                    ScLog.Warn("0-XNPCCore's SdcsMedic.Begin not found - its party medic may start over a medical use of the community's own (0-XNPCCore update?)");
                }
                catch (Exception e) { ScLog.Once("medic-bind-begin", e); }
                return false;
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return MedicBeginTarget(); }

            [HarmonyPrefix]
            public static bool Prefix(EntityAlive healer, ref bool __result)
            {
                try
                {
                    if (healer == null || !uses.ContainsKey(healer.entityId)) return true;
                    xnpcMedicHeld++;
                    __result = false;
                    return false;
                }
                catch (Exception ex)
                {
                    ScLog.Once("medic-xnpc-begin", ex);
                    return true;
                }
            }
        }
#endif
    }
}
