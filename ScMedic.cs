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
    /// member loop: her own loaded, visible body on the host only). Raiders and strangers heal as before.
    /// </summary>
    public static class ScMedic
    {
        /// <summary>Items that restore health, best first. Plain bandages only stop bleeding, so they are not here.</summary>
        public static readonly string[] HealItems = { "medicalFirstAidKit", "medicalFirstAidBandage" };
#if SC_V34
        /// <summary>3.4 (D2): what stops a bleed, cheapest first (0-XNPCCore's own order for a bleed, MedicRules.CheapestItem).</summary>
        public static readonly string[] BleedItems = { "medicalBandage", "medicalFirstAidBandage", "medicalFirstAidKit" };
        /// <summary>3.4: every item a use here can put in the hand (a load finding one there gives the weapon back).</summary>
        private static readonly string[] UseItems = { "medicalFirstAidKit", "medicalFirstAidBandage", "medicalBandage" };
        /// <summary>The bleed every vanilla carrier starts (buffs.xml: BleedingOne, Two and BarbedWire add it).</summary>
        private const string BleedBuff = "buffInjuryBleeding";
        private const string BandagesStockedCvar = "sc_bandagesStocked";
        private static int bleedsStopped, xnpcBusyWaits, xnpcHealSpeedFixed, xnpcMedicHeld;
#endif

        internal const float HealBelow = 0.6f;       // use something at or below this share of max health (round r2: ScRunners reads it too)
        private const float Cooldown = 12f;          // seconds between two uses (counted from the start of one)
        private const float CheckEvery = 1f;         // how often a body looks at its health
        private const int KitWorthMissing = 90;      // below this much missing health a kit would be wasted
        private const float TriggerTimeout = 0.5f;   // fire the use trigger even if the animator is slow to show the hold type
        /// <summary>
        /// medRegHealthIncSpeed for an NPC. The vanilla items set it from the player's Physician perk cvar, which an
        /// NPC does not have, leaving 0 - the queued health would drain without healing. 2 is the no-perk value, and
        /// the one the framework uses for its own heal.
        /// </summary>
        private const float NpcRegenSpeed = 2f;
        private const string StockedCvar = "sc_medsStocked";
        /// <summary>Round k2: under this share of her max health a member takes a first aid kit before a first aid bandage, whatever she is missing.</summary>
        private const float KitFirstBelow = 0.5f;
        /// <summary>Round k2: the MedicMaxWait exception (she uses it with the enemy still in reach) needs her under this share of her max health.</summary>
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
        }
        private static readonly Dictionary<int, Use> uses = new Dictionary<int, Use>();

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
            return FirstCarried(e, HealItems) != null;
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
            string use = uses.TryGetValue(e.entityId, out u) ? "using " + u.Item + (u.Triggered ? " (animation started)" : " (waiting for the hold pose)") : "idle";
#if SC_V34
            // 3.4 (D2): the cloth bandages and the bleed too; the counts take the bag in (0-XNPCCore moves medical items there)
            return "stocked=" + (e.Buffs.GetCustomVar(StockedCvar) > 0f) + "/" + (e.Buffs.GetCustomVar(BandagesStockedCvar) > 0f) + " " + use
                   + (e.Buffs.HasBuff(BleedBuff) ? " BLEEDING" : "") + " cloth=" + Count(e, "medicalBandage") + " bandages=" + Count(e, "medicalFirstAidBandage")
                   + " kits=" + Count(e, "medicalFirstAidKit") + " SdcsIsHealing refused (all bodies)=" + Patch_NoFreeHeal.SdcsRefused
                   + "; restock (md1): " + RestockTimes(e)
                   + "; k2: " + WaitText(e) + ", regen " + RegenText(e)
                   + "; all bodies: bleeds stopped " + bleedsStopped + ", waits for XNPC's hand clips " + xnpcBusyWaits
                   + ", XNPC medic regen fixed " + xnpcHealSpeedFixed + ", XNPC medic held for a use " + xnpcMedicHeld;
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
            if (readyAt.TryGetValue(e.entityId, out ready) && now < ready) return;
            if (e.GetCVar("SdcsAnimBusy") >= 0.5f) { xnpcBusyWaits++; return; }
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
            }
            if (pick == null && bleeding && (low || !InFight(e))) pick = FirstCarried(e, BleedItems);
            if (pick == null) { ClearWait(e.entityId); return; }
            if (member && HeldByReach(e, pick, max, now)) return;
            if (!TakeOne(e, pick)) return;
            Begin(e, pick, now);
            readyAt[e.entityId] = now + Cooldown;
            if (bleeding)
            {
                bleedsStopped++;
                ScLog.Verbose("medic: " + e.entityId + " is bleeding - " + pick);
            }
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
        /// </summary>
        private static bool HeldByReach(EntityAlive e, string pick, int max, float now)
        {
            float r = ScConfig.MedicSafeDistance;
            if (r <= 0f) { ClearWait(e.entityId); return false; }
            float dist;
            EntityAlive t = NearestEnemy(e, r, true, out dist);
            float since;
            bool waiting = heldSince.TryGetValue(e.entityId, out since);
            if (waiting && !Fresh(e.entityId, now))
            {
                ClearWait(e.entityId);
                waiting = false;
            }
            if (t == null)
            {
                if (waiting)
                {
                    ClearWait(e.entityId);
                    ScLog.Verbose(BodyName(e) + ": nothing after " + HerObj(e) + " within " + F1(r) + " m any more - uses " + pick
                                  + " after waiting " + F1(now - since) + " s");
                }
                return false;
            }
            string who = ScCommunity.ClassName(t) + " (" + t.entityId + ") at " + F1(dist) + " m";
            if (!waiting)
            {
                heldSince[e.entityId] = now;
                since = now;
                Patches_Xnpc.Count("meds-use-held-reach");
                ScLog.Verbose(BodyName(e) + " waits to use " + pick + " at " + e.Health + "/" + max + ": " + who + " is after " + HerObj(e)
                              + " (MedicSafeDistance " + F1(r) + " m) - " + She(e) + " fights on");
            }
            heldLast[e.entityId] = now;
            float waited = now - since;
            if (e.Health < max * DesperateBelow && waited >= ScConfig.MedicMaxWait)
            {
                ClearWait(e.entityId);
                Patches_Xnpc.Count("meds-use-desperate");
                ScLog.Out(BodyName(e) + " uses " + pick + " with " + who + " after " + HerObj(e) + ": waited " + F1(waited) + " s and down to "
                          + e.Health + "/" + max + " (under " + (DesperateBelow * 100f).ToString("0", System.Globalization.CultureInfo.InvariantCulture)
                          + "% after MedicMaxWait " + F1(ScConfig.MedicMaxWait) + " s: a heal now beats none)");
                return false;
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

        private static void Begin(EntityAlive e, string item, float now)
        {
            ItemClass ic = ItemClass.GetItemClass(item, true);
            if (ic == null) return;
            float delay = ic.Actions != null && ic.Actions.Length > 0 && ic.Actions[0] != null ? ic.Actions[0].Delay : 0f;
            var u = new Use
            {
                Item = item,
                HoldType = ic.HoldType != null ? ic.HoldType.Value : 0,
                Slot = e.inventory.holdingItemIdx,
                Started = now,
                Duration = Mathf.Clamp(delay > 0f ? delay : 2.5f, 1f, 6f),
            };
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
            u.EndAt = now + u.Duration + TriggerTimeout;
            uses[e.entityId] = u;
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
            ScLog.Verbose("medic: " + e.entityId + " starts using " + item + " at " + e.Health + "/" + e.GetMaxHealth() + " (" + u.Duration + " s)");
        }

        private static void Advance(EntityAlive e, Use u)
        {
            if (e.IsDead()) { Finish(e, u, false, false); return; }
            if (e.emodel == null || e.emodel.IsRagdollActive) { Finish(e, u, false, true); return; }
            float now = Time.time;
            if (!u.Triggered)
            {
                AvatarController ac = e.emodel.avatarController;
                bool shown = ac != null && ac.anim != null && ac.anim.GetInteger(AvatarController.weaponHoldTypeHash) == u.HoldType;
                if (shown || now - u.Started > TriggerTimeout)
                {
                    if (ac != null) ac._setTrigger(AvatarController.useItemHash);
                    u.Triggered = true;
                    u.EndAt = now + u.Duration;          // the animation gets its whole length from the trigger
                }
                return;
            }
            // Done when the use animation has played and left its layer - measured live, 3P_Bandage_Use runs about
            // 1.4 s against the bandage's 2.5 s use time, so waiting for the item's delay left the NPC standing
            // there holding it. The delay stays as the fallback if the clip never shows.
            AvatarController anim = e.emodel.avatarController;
            if (anim != null && anim.anim != null)
            {
                if (u.UseLayer == -2) u.UseLayer = anim.anim.GetLayerIndex("UpperBodyUseAndReload");
                if (u.UseLayer >= 0)
                {
                    bool playing = anim.anim.GetCurrentAnimatorClipInfoCount(u.UseLayer) > 0;
                    if (playing) u.SawClip = true;
                    else if (u.SawClip) { Finish(e, u, true, false); return; }
                }
            }
            if (now >= u.EndAt) Finish(e, u, true, false);
        }

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
                if (completed) ApplyEffects(e, u.Item);
                else if (returnItem) Add(e, u.Item, 1);
            }
            finally
            {
                if (u.Displaced != null && !u.Displaced.IsEmpty()) Hold(e, u.Slot, u.Displaced.itemValue, u.Displaced.count);
                else Hold(e, u.Slot, ItemValue.None, 0);
            }
            ScLog.Verbose("medic: " + e.entityId + (completed ? " finished " : " stopped ") + u.Item + " at " + e.Health + "/" + e.GetMaxHealth());
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
        private static string HerObj(EntityAlive e) { return Male(e) ? "him" : "her"; }
        private static string She(EntityAlive e) { return Male(e) ? "he" : "she"; }
        private static string BodyName(EntityAlive e) { return ScCommunity.DisplayName(e) + " (id " + e.entityId + ")"; }
        private static string F1(float f) { return f.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture); }

        /// <summary>
        /// Round k2: the nearest awake, living enemy of hers (SCore's IsEnemy, not an inert one) within r - with afterHer, only
        /// one whose attack or revenge target she is. Null when none (dist -1). Main thread only (the shared scratch list).
        /// </summary>
        private static EntityAlive NearestEnemy(EntityAlive e, float r, bool afterHer, out float dist)
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
            EntityAlive t = NearestEnemy(e, ScConfig.RegenEnemyRadius, false, out dist);
            if (t != null) return "in combat: " + ScCommunity.ClassName(t) + " (" + t.entityId + ") at " + F1(dist) + " m (RegenEnemyRadius " + F1(ScConfig.RegenEnemyRadius) + " m)";
            return null;
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
            if (!WaitingToHeal(e, out waited)) return "no wait";
            return "WAITING " + F1(waited) + " s to use an item: an enemy after " + HerObj(e) + " within " + F1(ScConfig.MedicSafeDistance) + " m";
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
                    Tick(__instance);
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
