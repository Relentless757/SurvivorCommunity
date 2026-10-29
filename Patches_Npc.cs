using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace SurvivorCommunity
{
    /// <summary>
    /// Hooks on the NPC classes themselves. SetDead: EntityKilled needs a killer and falls / drowning have none.
    /// PostInit: up to SCore 3.2.7 it forced SetSpawnerSource(Biome) on every load, which made a stationed member
    /// eligible for ambient despawn until the ticker's next relink. SCore 3.2.36 no longer does
    /// (ApplySpawnerSourceOnPostInit); the re-stamp stays, harmless, for older SCore builds.
    ///
    /// SCore has two friendly classes, EntityAliveSDX and 0-XNPCCore's EntityAliveSDXV4. They are siblings, each
    /// declaring its own SetDead, PostInit and IsSavedToFile (neither calls the other's), so each hook below patches
    /// both declarations (ScNpc.Declared) and runs the same logic on either: everything in it is keyed on registry ids
    /// and the mod's own cvars, which mean the same thing on both. Prepare skips a hook whose targets are all gone
    /// rather than letting the class throw inside PatchAll, which would silently drop every patch class after it.
    /// </summary>
    public static class Patches_Npc
    {
        private static readonly Type[] NoArgs = new Type[0];

        /// <summary>Per-world reset (ModEvents.GameStarting): a dead NPC's held window means nothing in another world.</summary>
        public static void Reset()
        {
            Patch_SetDead.Reset();
        }

        [HarmonyPatch]
        public static class Patch_SetDead
        {
            /// <summary>Set on a dead raider once its pair weapons have had their drop roll (M10-D6).</summary>
            private const string PairDroppedCvar = "sc_pairDropped";

            /// <summary>Where a held window's backpack goes (the body's death spot, as for the first one) and its label.</summary>
            private sealed class HeldWindow
            {
                public UnityEngine.Vector3 Pos;
                public string Name;
            }

            /// <summary>
            /// Dead NPCs whose inventory window a remote player still had open (or the host, if closing the host's grid
            /// did not clear the flag), by entity id: the window is kept in HarvestManager until its last holder lets go
            /// and is then dropped at the body (Release). Host only, main thread.
            /// </summary>
            private static readonly Dictionary<int, HeldWindow> heldWindows = new Dictionary<int, HeldWindow>();

            public static void Reset() { heldWindows.Clear(); }

            /// <summary>Dead NPCs' windows waiting for their holders, for sc xnpc.</summary>
            public static int HeldCount { get { return heldWindows.Count; } }

            [HarmonyPrepare]
            public static bool Prepare() { return ScNpc.Declared("SetDead", NoArgs).Count > 0; }

            [HarmonyTargetMethods]
            public static IEnumerable<MethodBase> TargetMethods() { return ScNpc.Declared("SetDead", NoArgs); }

            [HarmonyPostfix]
#if SC_V34
            // 3.4: 0-XNPCCore 3.4 defers a SetDead it meets during PostInit (SdcsDeadSpawnSetDeadPatch: its prefix returns false,
            // then it calls SetDead again) - the postfix runs on both calls, and only the one that really ran counts
            public static void Postfix(EntityAlive __instance, bool __runOriginal)
            {
                if (!__runOriginal) return;
#else
            public static void Postfix(EntityAlive __instance)
            {
#endif
                try
                {
                    if (!ScCommunity.IsHost) return;
                    if (ScXnpc.IsBody(__instance) || (ScNpc.IsV4(__instance) && ScXnpc.IsCommunity(__instance))) DropWindow(__instance);
                    if (!ScRegistry.Loaded) return;
                    if (__instance.Buffs != null && __instance.Buffs.HasCustomVar(ScRadio.StandInCvar)) { ScRadio.OnStandInDied(__instance); return; }
                    // one of an enclave's people (E2): the person dies with the body
                    if (__instance.Buffs != null && __instance.Buffs.HasCustomVar(ScEnclavePeople.EncCvar)) { ScEnclavePeople.OnBodyDied(__instance); return; }
                    ScCommunity.OnMemberDied(__instance);
                }
                catch (Exception e)
                {
                    ScLog.Once("npc.SetDead", e);
                }
            }

            /// <summary>
            /// The inventory window of one of the community's 0-XNPCCore survivors (any of the six classes, wild or not, or
            /// any V4 member or stand-in), dropped as a backpack where they fell - the medical stock and the ammo the
            /// community gave them (ScMedic, ScLoadout, decision D3). SCore's legacy
            /// class drops its window (the HarvestManager container) for anyone and then forgets it; V4's SetDead drops only
            /// its separate lootContainer, and only when the leader is the local player, and never removes the window - so
            /// without this the window's contents were lost with the body and its HarvestManager entry leaked (M1's
            /// SetDead finding). The same code SCore's EntityAliveSDX.SetDead runs.
            ///
            /// SC's raiders (M10-D6) go through here too, being managed bodies: their bag holds what is left of their
            /// starting ammo and their meds (they never have a window, ScNpc.Items), and each weapon of their pair goes in
            /// with RaiderWeaponDropChance, rolled per weapon - the toolbelt copy SCore made at PostInit, so a real vanilla
            /// weapon of quality 1-6. That replaces the old raiders' 20% loot bag with their class weapon. V4's own drop
            /// adds nothing: its SetDead nulls lootContainer before dropItemOnDeath runs.
            ///
            /// A window somebody is looking at when the NPC dies (M10 round 3 review, F1): its items must not go into the
            /// backpack while the same items can still be dragged out of the open grid. The host's own grid is closed first
            /// (CloseHostWindow), so the host's case is the normal drop. A remote player's grid is a copy the server cannot
            /// close: the window stays out of this backpack and in HarvestManager (so SCore's close handler writes the
            /// player's copy into it instead of re-creating a container for the dead id), and Release drops what is left
            /// in a second backpack at the same spot once nobody holds it - the close, the player's disconnect, the 600 s
            /// expiry of the open (swept every second by ScTicker) or the world shutting down. Repeated SetDead calls see
            /// the window still held, or gone, so nothing drops twice. Raiders never have a window.
            /// </summary>
            private static void DropWindow(EntityAlive e)
            {
                try
                {
                    // M11: the rounds loaded in the gun in its hand go into the bag first, so they drop with it - the held gun
                    // itself never drops (a raider's pair roll below drops a copy, which is then empty, not loaded twice). A
                    // seated Locked & Loaded magazine is already in the bag: L&L's EntityAlive.SetDead prefix runs inside
                    // V4's base.SetDead() call, before this postfix, and empties the gun as it gives it back.
                    if (ScXnpc.IsBody(e) && e.inventory != null)
                    {
                        // dying in the middle of a medical use: the item back into the window, the displaced gun's rounds
                        // into the bag (M11 re-check), before either is collected
                        ScMedic.OnDied(e);
                        int back = ScAmmo.ReturnLoose(e, e.inventory.holdingItemItemValue, "died", true);
                        if (back > 0) ScAmmo.NoteDeathReturn(back);
                    }
                    // the window (meds, anything handed over) and, for the community's 0-XNPCCore survivors and SC's
                    // raiders, the bag (their ammo, ScAmmo.AddRounds): V4's own dropItemOnDeath drops neither
                    var drop = new List<ItemStack>();
                    bool hadWindow = HarvestManager.Has(e.entityId);
                    SCoreLootContainer window = hadWindow ? HarvestManager.GetOrCreate(e.entityId) : null;
                    if (window != null && window.IsUserAccessing()) CloseHostWindow(window);
                    bool held = window != null && (window.IsUserAccessing() || ScAmmo.IsRemoteOpen(e.entityId));
                    if (window != null && !held && window.items != null)
                        foreach (ItemStack s in window.items) if (s != null && !s.IsEmpty()) drop.Add(s.Clone());
                    if (ScXnpc.IsBody(e) && e.bag != null)
                    {
#if SC_V34
                        foreach (ItemStack s in e.bag.ItemGrid.items) if (s != null && !s.IsEmpty()) drop.Add(s.Clone());
#else
                        foreach (ItemStack s in e.bag.GetSlots()) if (s != null && !s.IsEmpty()) drop.Add(s.Clone());
#endif
                        e.bag.Clear();
                    }
                    // once per raider: vanilla's Kill calls SetDead again for every hit on a body that is already dead,
                    // and the weapons, unlike the bag, are still in its toolbelt then
                    if (ScXnpc.IsRaider(e) && ScConfig.RaiderWeaponDropChance > 0f && e.Buffs != null && e.Buffs.GetCustomVar(PairDroppedCvar) <= 0f)
                    {
                        e.Buffs.SetCustomVar(PairDroppedCvar, 1f);
                        GameRandom rnd = e.world != null ? e.world.GetGameRandom() : null;
                        int dropped = 0;
                        foreach (ItemValue iv in ScLoadout.CarriedPair(e))
                        {
                            float roll = rnd != null ? rnd.RandomFloat : UnityEngine.Random.value;
                            if (roll >= ScConfig.RaiderWeaponDropChance) continue;
                            drop.Add(new ItemStack(iv, 1));
                            dropped++;
                        }
                        if (dropped > 0) ScLog.Verbose("raider " + e.entityId + " dropped " + dropped + " of its pair weapon(s)");
                    }
                    UnityEngine.Vector3 at = e.position + e.transform.up;
                    string label = Localization.Get(e.EntityName);
                    DropBackpack(drop, at, label);
                    if (held)
                    {
                        if (!heldWindows.ContainsKey(e.entityId))
                        {
                            heldWindows[e.entityId] = new HeldWindow { Pos = at, Name = label };
                            Patches_Xnpc.Count("window-death-held");
                            ScLog.Out(label + " (id " + e.entityId + ") died with their inventory window open - its contents drop at the body once it is closed");
                        }
                    }
                    else if (hadWindow)
                    {
                        HarvestManager.Remove(e.entityId);
                        heldWindows.Remove(e.entityId);
                    }
                }
                catch (Exception ex)
                {
                    ScLog.Once("npc-drop-window", ex);
                }
            }

            /// <summary>
            /// Drops stacks as one backpack (BackpackNPC when it exists, else vanilla's Backpack) at a spot, labelled with
            /// the NPC's name - the drop SCore's EntityAliveSDX.SetDead makes. Nothing for an empty list.
            /// </summary>
            private static void DropBackpack(List<ItemStack> drop, UnityEngine.Vector3 at, string label)
            {
                if (drop == null || drop.Count == 0) return;
                string className = ScCommunity.ClassExists(EntityClass.FromString("BackpackNPC")) ? "BackpackNPC" : "Backpack";
                EntityItem pack = EntityFactory.CreateEntity(EntityClass.FromString(className), new Vector3i(at)) as EntityItem;
                if (pack == null) return;
#if SC_V34
                // 3.4: a grid Bag filled through SetSlot, carried as the creation data's blob (SCore 3.4's own drop); ToClient = 3.2's true
                Bag bag = new Bag(new Vector2i(Math.Max(drop.Count, 1), 1), XUiC_ItemStack.StackLocationTypes.LootContainer, null);
                for (int i = 0; i < drop.Count; i++) bag.SetSlot(i, drop[i]);
                var ecd = new EntityCreationData(pack, StreamModeWrite.ToClient) { entityName = label, id = -1 };
                ecd.bagData = StreamUtils.ToBlob(pbw => bag.Write(pbw, StreamModeWrite.ToClient));
                GameManager.Instance.RequestToSpawnEntityServer(ecd);
#else
                Bag bag = new Bag(Math.Max(drop.Count, 1));
                for (int i = 0; i < drop.Count; i++) bag.items[i] = drop[i];
                GameManager.Instance.RequestToSpawnEntityServer(new EntityCreationData(pack, true) { entityName = label, id = -1, bag = bag });
#endif
                pack.OnEntityUnload();
            }

            /// <summary>
            /// The host's own loot window showing this window is closed before the drop (F1, the host's case). Its OnClose
            /// clears the window's user-accessing flag at once, so the items go into the one death backpack and the grid
            /// that showed them is gone. Chosen over a postfix on XUiC_LootWindowGroup.OnClose that would release the window
            /// later: no new Harmony target, nothing deferred, one backpack, and nobody keeps looting a dead NPC's window.
            /// A flag still set afterwards (some other screen) makes the window held like a remote one.
            /// </summary>
            private static void CloseHostWindow(SCoreLootContainer window)
            {
                try
                {
                    EntityPlayerLocal p = ScCommunity.LocalPlayer;
                    LocalPlayerUI ui = p != null ? p.PlayerUI : null;
                    if (ui == null || ui.windowManager == null || ui.xui == null || !ui.windowManager.IsWindowOpen(XUiC_LootWindowGroup.ID)) return;
                    XUiC_LootWindowGroup lg = ui.xui.FindWindowGroupByName(XUiC_LootWindowGroup.ID) as XUiC_LootWindowGroup;
                    if (lg == null || !ReferenceEquals(lg.te, window)) return;
                    ui.windowManager.Close(XUiC_LootWindowGroup.ID);
                    Patches_Xnpc.Count("window-death-host-closed");
                }
                catch (Exception ex)
                {
                    ScLog.Once("npc-close-host-window", ex);
                }
            }

            /// <summary>
            /// Drops a held dead NPC's window at its body once nobody holds it any more (force: at once, whoever holds it -
            /// the world is shutting down) and removes it from HarvestManager. True when it dropped. The entry goes before
            /// the drop, so a failure loses the items rather than leaving them to drop twice. HarvestManager.bin is saved
            /// again because SCore's close handler has just written the dead id's window into it.
            /// </summary>
            internal static bool Release(int id, bool force)
            {
                HeldWindow h;
                if (!heldWindows.TryGetValue(id, out h)) return false;
                try
                {
                    if (!HarvestManager.Has(id)) { heldWindows.Remove(id); return false; }    // gone some other way: nothing to drop
                    SCoreLootContainer window = HarvestManager.GetOrCreate(id);
                    if (!force && (window.IsUserAccessing() || ScAmmo.IsRemoteOpen(id))) return false;
                    var drop = new List<ItemStack>();
                    if (window.items != null)
                        foreach (ItemStack s in window.items) if (s != null && !s.IsEmpty()) drop.Add(s.Clone());
                    heldWindows.Remove(id);
                    HarvestManager.Remove(id);
                    HarvestManager.Save();
                    DropBackpack(drop, h.Pos, h.Name);
                    Patches_Xnpc.Count("window-death-released");
                    ScLog.Out(h.Name + " (id " + id + "): the inventory window left open at their death was closed - " + drop.Count
                              + " stack(s) dropped at the body" + (force ? " (world shutting down)" : ""));
                    return true;
                }
                catch (Exception ex)
                {
                    ScLog.Once("npc-release-window", ex);
                    return false;
                }
            }

            /// <summary>Every held window that is free (ScTicker, once a second: the expiry of a remote open), or all of them (force).</summary>
            internal static void ReleaseAll(bool force)
            {
                if (heldWindows.Count == 0) return;
                foreach (int id in new List<int>(heldWindows.Keys)) Release(id, force);
            }
        }

        /// <summary>
        /// Who the player is allowed to hurt. SCore shields every friendly NPC twice over
        /// (EntityTargetingUtilities.CanDamage): one who follows you counts as your ally, so CanDamageAlly
        /// refuses flatly, and one with no leader falls to the faction rule instead, which refuses because you
        /// are Neutral to whiteriver and an EntityNPC target treats Neutral as friendly fire. Both gates are
        /// consulted by CanDamageEntity - what ItemActionAttack.Hit asks before it resolves a hit at all - and
        /// again inside SCore's DamageEntity (on both of its NPC classes), so a shot simply vanished. The community wants the
        /// opposite division: anyone who is not one of your people can be killed, and your own people are
        /// protected unless you ask for friendly fire. Traders keep their immunity either way (a pack trader
        /// runs on EntityAliveSDX, so it comes through this same rule rather than vanilla's flat refusal).
        /// Only the player's own damage is affected; NPCs still cannot shoot each other, and zombies were
        /// never shielded to begin with because they hate whiteriver.
        /// </summary>
        [HarmonyPatch(typeof(EntityTargetingUtilities), nameof(EntityTargetingUtilities.CanTakeDamage))]
        public static class Patch_FriendlyFire
        {
            [HarmonyPostfix]
            public static void Postfix(EntityAlive self, Entity damagingEntity, ref bool __result)
            {
                try
                {
                    if (__result) return;                                   // SCore already allows it
                    if (!(damagingEntity is EntityPlayer)) return;          // only the player's own damage
                    if (self == null || self is EntityPlayer) return;
                    // A trader stays a trader. Not by trader id - XNPCCore gives every hireable NPC
                    // trader_id 1, which is why SCore blanks it around its own damage call - but by the
                    // dialog it uses. A vanilla trader never arrives here anyway: EntityTrader refuses all
                    // damage outright and only SCore's own NPC classes ask this rule.
                    EntityNPC npc = self as EntityNPC;
                    if (npc != null && npc.NPCInfo != null && !string.IsNullOrEmpty(npc.NPCInfo.DialogID)
                        && npc.NPCInfo.DialogID.StartsWith("trader", StringComparison.OrdinalIgnoreCase)) return;
                    // your own people are protected unless friendly fire is switched on (decided before the hire test:
                    // a member who ever followed carries Owner)
                    if (ScRegistry.GetMember(self.entityId) != null
                        || (self.Buffs != null && self.Buffs.HasCustomVar(ScRadio.StandInCvar)))
                    {
                        if (ScConfig.CommunityFriendlyFire) __result = true;
                        return;
                    }
                    // nor anyone's hired companion (SCore's ally and PvP rules decide those; this postfix used to override
                    // them for shots, and a stray hit would now turn one hostile for good - ScTrust)
                    if (ScCommunity.HiredBy(self) > 0) return;
                    __result = true;
                }
                catch (Exception e)
                {
                    ScLog.Once("sc-friendly-fire", e);
                }
            }
        }

        /// <summary>
        /// A physical runner is never written into a chunk: the registry snapshot is its only persistent copy (the
        /// way vanilla keeps vehicles and drones out of chunk saves). Chunk saves run on the save thread too - this
        /// reads one immutable set and nothing else. Both classes' IsSavedToFile apply the same rule (saved when the
        /// Leader or Persist cvar exists, else not for Dynamic or Biome spawns), so the veto is the same on both.
        /// The M11 fix: a reference test on __instance (was Unity's != null). The body of an unloaded chunk is written
        /// after its GameObject was destroyed, so the old test skipped the veto on every unload save and only
        /// ScRunners.OnEntityUnloaded's Delete-source backstop kept the copy from coming back. ScRunners.VetoChunkSave
        /// also keeps the verdict for such a body fixed, so Chunk.write's count and write passes agree. E2: an enclave's body
        /// is vetoed the same way (VetoChunkSave asks ScEnclavePeople.IsEnclaveBody too).
        /// </summary>
        [HarmonyPatch]
        public static class Patch_IsSavedToFile
        {
            [HarmonyPrepare]
            public static bool Prepare() { return ScNpc.Declared("IsSavedToFile", NoArgs).Count > 0; }

            [HarmonyTargetMethods]
            public static IEnumerable<MethodBase> TargetMethods() { return ScNpc.Declared("IsSavedToFile", NoArgs); }

            [HarmonyPostfix]
            public static void Postfix(EntityAlive __instance, ref bool __result)
            {
                // runs inside Chunk.write on the save thread, where vanilla catches nothing: an exception would abort that
                // chunk's save (the review's M4). On one, the game's own verdict stands - both of Chunk.write's passes then
                // answer alike - and ScLog.Once (locked) reports it.
                try
                {
                    if (__result && (object)__instance != null && ScRunners.VetoChunkSave(__instance)) __result = false;
                }
                catch (Exception ex)
                {
                    ScLog.Once("runner-chunk-veto", ex);
                }
            }
        }

        /// <summary>
        /// The M11 fix re-check's R1, at the shot itself. SCore's CanSee calls an NPC's attack or revenge target seen WITHOUT
        /// a ray within sqrt(20) = 4.47 m, and InWeaponRange's CanShoot passes on any entity in the line, so the pack's
        /// RangedMeleeAttack fires across whoever stands between - the player stepping into a doorway fight, one member in
        /// front of another - however the attack target got set (MoveToAttackTargetSDX.Stop on every stop, the attack task's
        /// own 60 s, a hit). SCore's faction gates stop the damage, not the shot. For the community's own NPCs (members and
        /// stand-ins) firing a ranged action, this casts CanSee's own ray at that range and, while a protected one
        /// (UAI.ScEngage.Protected: a player, or a living non-enemy NPC) is first in the line, holds the shot: the task's
        /// countdown to its next shot is kept above zero (it still turns and aims), and the target is dropped as ATTACK
        /// target, so the next decision's CanSee has to cast its ray, fails on the blocker, and the defence holds
        /// (ScDefendHold) or the pack backs off. A revenge target keeps SCore's self-defence choosing the attack; its shot
        /// stays held until the line clears (melee, from 3 m, is 0-XNPCCore's swap and untouched). Beyond 4.47 m CanSee
        /// casts its ray at every decision, as for every NPC. The same two targets as ScAim's Patch_AttackUpdate (both
        /// attack tasks' Update), so no new target; one ray per frame only while such an NPC attacks within 4.47 m.
        ///
        /// The M11 fix 2 (MELEE_DIAG F1) adds the melee half to the same prefix: for an action that does not shoot, held by
        /// one of our bodies (members, strangers and - with MeleePacingRaiders - raiders), ScMelee.BeforeUpdate sets the
        /// countdown from the weapon's own swing clock instead of SCore's 1 s ticks, and the postfix (ScMelee.AfterUpdate)
        /// spaces out a try the weapon refused. One prefix for both halves, so their order never depends on how Harmony
        /// chains two prefixes of one mod; the ranged half is unchanged.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_HoldFireAcross
        {
            [HarmonyPrepare]
            public static bool Prepare() { return ScAim.AttackTargets("Update").Count > 0; }

            [HarmonyTargetMethods]
            public static IEnumerable<MethodBase> TargetMethods() { return ScAim.AttackTargets("Update"); }

            /// <summary>__state: the melee weapon's lastUseTime when ScMelee cleared this update to swing, else -1.</summary>
            [HarmonyPrefix]
            public static bool Prefix(UAI.UAITaskBase __instance, UAI.Context _context, out float __state)
            {
                __state = -1f;
                try
                {
                    EntityAlive self = _context != null ? _context.Self : null;
                    if (self == null || self.isEntityRemote) return true;
                    int index = ScAim.ActionIndex(__instance);
                    if (!ScAim.IsRangedAction(self, index))
                    {
                        // melee: swing on the weapon's own clock (the M11 fix 2); SCore's update then runs as always
                        __state = ScMelee.BeforeUpdate(__instance as UAI.UAITaskAttackTargetEntity, self, index);
                        return true;
                    }
                    if (!ScXnpc.IsCommunity(self)) return true;
                    // the task's own choice of target: its action target, or for a self-target the attack/revenge target
                    EntityAlive t = _context.ActionData.Target as EntityAlive;
                    if (t != null && t.entityId == self.entityId) t = EntityUtilities.GetAttackOrRevengeTarget(self.entityId) as EntityAlive;
                    if (t == null || t.IsDead() || self.GetDistanceSq(t) >= UAI.ScEngage.ShortcutDistanceSq) return true;
                    bool block;
                    if (!UAI.ScEngage.Protected(self, UAI.ScEngage.FirstOnSeeRay(self, t, out block))) return true;
                    if (self.GetAttackTarget() == t)
                    {
                        self.SetAttackTarget(null, 0);
                        Patches_Xnpc.Count("hold-fire-across");     // once per attack start (AttackTargetEntitySDX.Start sets it)
                    }
                    UAI.UAITaskAttackTargetEntity attack = __instance as UAI.UAITaskAttackTargetEntity;
                    if (attack == null) return false;               // not the task we know: skip this frame's update
                    if (attack.attackTimeout < 2) attack.attackTimeout = 2;   // Update counts down once: no shot this frame
                    return true;
                }
                catch (Exception ex)
                {
                    ScLog.Once("hold-fire-across", ex);
                    return true;
                }
            }

            /// <summary>After a melee update ScMelee cleared to swing: did it swing, find no chance, or meet a refusal?</summary>
            [HarmonyPostfix]
            public static void Postfix(UAI.UAITaskBase __instance, UAI.Context _context, float __state)
            {
                if (__state < 0f) return;
                try
                {
                    EntityAlive self = _context != null ? _context.Self : null;
                    if (self == null) return;
                    ScMelee.AfterUpdate(__instance as UAI.UAITaskAttackTargetEntity, self, ScAim.ActionIndex(__instance), __state);
                }
                catch (Exception ex)
                {
                    ScLog.Once("melee-pace-after", ex);
                }
            }
        }

        [HarmonyPatch]
        public static class Patch_PostInit
        {
            [HarmonyPrepare]
            public static bool Prepare() { return ScNpc.Declared("PostInit", NoArgs).Count > 0; }

            [HarmonyTargetMethods]
            public static IEnumerable<MethodBase> TargetMethods() { return ScNpc.Declared("PostInit", NoArgs); }

            /// <summary>
            /// Both weapons on the item list before SCore's EntityAlive.PostInit postfix fills the slots from it, before
            /// SetupStartingItems, and before PostInit re-equips the saved weapon (ScLoadout). BeforePostInit acts on the
            /// community's 0-XNPCCore survivor classes and SC's raider classes (EntityAliveSDXV4: their rolled pair,
            /// migration M7 and M10) and passes every other class through untouched (pack NPCs, XNPC's other classes).
            /// </summary>
            [HarmonyPrefix]
            public static void Prefix(EntityAlive __instance)
            {
                try { ScLoadout.BeforePostInit(__instance); }
                catch (Exception e) { ScLog.Once("loadout-postinit", e); }
            }

            [HarmonyPostfix]
            public static void Postfix(EntityAlive __instance)
            {
                try
                {
                    if (__instance == null) return;
                    // after EntityFactory's collider pass: a player-bodied survivor is otherwise unhittable. Every
                    // player body needs it, not only members and prospects - a stranger found in the wild or spawned
                    // from the console used to keep its hit capsules on the no-collision layer.
                    Patches_Sdcs.FixColliderLayers(__instance);
                    // a new body's starting ammo, once per entity (its own catch; decisions D3 and M10-D2)
                    ScLoadout.AfterPostInit(__instance);
                    // M10-D5: a raider opens unlocked doors through SCore's own opener (its move tasks, for every human
                    // NPC; it shuts them again). Vanilla's move-helper opener - the old EntityBandit raiders' - only on request.
                    if (ScConfig.RaiderVanillaDoors && __instance.moveHelper != null && ScXnpc.IsRaider(__instance)) __instance.moveHelper.CanOpenDoors = true;
                    if (!ScRegistry.Loaded) return;
                    // round sh: a raider's Shooting stars from its tier, a wild survivor's profile (its own catch)
                    try { ScShooting.OnPostInit(__instance); }
                    catch (Exception e) { ScLog.Once("shooting-postinit", e); }
                    if (ScRegistry.Cleanup.Contains(__instance.entityId))
                    {
                        if (ScCommunity.CarriesOurMarks(__instance)) ScCommunity.CleanupStale(__instance);
                        ScRegistry.Cleanup.Remove(__instance.entityId);
                        return;
                    }
                    if (ScRegistry.Doomed.Contains(__instance.entityId)) { ScNpc.SendOnMission(__instance, true); return; }
                    // an outpost stand-in never comes back from a save: a fresh one spawns while the player is there
                    if (__instance.Buffs != null && __instance.Buffs.HasCustomVar(ScGarrison.StandInOutpostCvar)) { ScGarrison.Orphans.Add(__instance.entityId); return; }
                    // nor does an enclave's body (E2: SC stamps %sc_enc after PostInit, so a body that has it here came back
                    // from a chunk or a save - a crash, a save race): removed on the next tick
                    if (__instance.Buffs != null && __instance.Buffs.HasCustomVar(ScEnclavePeople.EncCvar)) { ScEnclavePeople.Orphans.Add(__instance.entityId); return; }
                    // an offline physical runner exists only as its snapshot: this is an old chunk copy (hidden now,
                    // removed next tick - never Doomed, the id comes back with the real runner) or a stranger who
                    // inherited the id (untouched: no spawner/Persist stamp for them)
                    Member rm = ScRegistry.GetMember(__instance.entityId);
                    if (rm != null && ScRunners.IsPhysical(rm) && rm.Offline && __instance.entityId != ScRunners.RecreatingId)
                    {
                        if (ScRunners.IsStaleCopy(rm, __instance)) { ScNpc.SendOnMission(__instance, true); ScRunners.StaleIds.Add(__instance.entityId); }
                        return;
                    }
                    if (ScRegistry.GetMember(__instance.entityId) == null && ScRegistry.GetProspect(__instance.entityId) == null) return;
                    __instance.SetSpawnerSource(EnumSpawnerSource.StaticSpawner);
                    // only an ABSENT Persist is raised here: Persist=0 is the signature of a SCore dismiss that
                    // the ticker turns into a Leave, and must survive a save/reload in between
                    if (!__instance.Buffs.HasCustomVar("Persist")) __instance.Buffs.SetCustomVar("Persist", 1f);
                }
                catch (Exception e)
                {
                    ScLog.Once("npc.PostInit", e);
                }
            }
        }
#if SC_V34

        // ------------------------------------------------------------------ the NPC toolbelt (3.4) ----

        /// <summary>The toolbelt our bodies get on 3.4: 3.2's NPC toolbelt (Inventory.PUBLIC_SLOTS in play mode, 10).</summary>
        public const int ToolbeltSlots = 10;

        /// <summary>
        /// The entity class EntityFactory is building on this thread (Patch_CreatingClass), 0 outside a build. A save of the
        /// value it replaced is restored on the way out, so a build inside a build (none today) cannot leave it wrong.
        /// </summary>
        [ThreadStatic] private static int creatingClass;

        /// <summary>
        /// 3.4: which class is being built, for Patch_NpcToolbelt. EntityFactory.CreateEntityOperation.CompleteEntity is
        /// where every entity is made (EntityFactory.CreateEntity and the async chunk path both end in it): it instantiates the
        /// prefab and AddComponent's the class's type - whose Awake runs then and there, InitInventory included - and only
        /// afterwards sets entityId and calls Init(entityClass). So inside Awake the entity does not know its class yet; the
        /// operation does (ecd.entityClass, resolved by LoadAssets), and this prefix keeps it for the length of the call.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_CreatingClass
        {
            public static MethodBase Target()
            {
                return AccessTools.DeclaredMethod(typeof(EntityFactory.CreateEntityOperation), "CompleteEntity", NoArgs);
            }

            [HarmonyPrepare]
            public static bool Prepare()
            {
                try
                {
                    if (Target() != null) return true;
                    ScLog.Err("EntityFactory.CreateEntityOperation.CompleteEntity() not found (game update?) - the community's NPCs get a one-slot toolbelt: their weapon pair and raider drops break");
                }
                catch (Exception e) { ScLog.Once("npc-bind-creating", e); }
                return false;
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return Target(); }

            [HarmonyPrefix]
            public static void Prefix(EntityFactory.CreateEntityOperation __instance, out int __state)
            {
                __state = creatingClass;
                try { creatingClass = __instance != null && __instance.ecd != null ? __instance.ecd.entityClass : 0; }
                catch (Exception) { creatingClass = 0; }
            }

            [HarmonyFinalizer]
            public static void Finalizer(int __state)
            {
                creatingClass = __state;
            }
        }

        /// <summary>
        /// 3.4: the community's bodies keep 3.2's ten-slot toolbelt. 3.4's EntityAlive.InitInventory gives every NPC a one-slot
        /// Inventory (only EntityPlayer and EntityBandit override it; neither SCore 3.4 nor 0-XNPCCore 3.4 does), where 3.2
        /// gave ten. SCore's SetupStartingItems copies itemsOnEnterGame into slots 0..n and Inventory.SetItem drops a slot
        /// that does not exist, so with one slot only the first item stayed: ScLoadout's pair (melee and ranged) lost a
        /// weapon, its Refill wrote slots that are not there, a raider's death roll (CarriedPair) found only the gun in hand
        /// and ScArms' carried copy fell back to a fresh weapon (no quality, no mods).
        ///
        /// Only for an EntityAliveSDXV4 of a class SC manages (ScXnpc.IsManagedClass: the six npcSdcsSurvivor classes the
        /// community takes over - every one of them is SC's in an SC game, SC removes XNPC's own spawns of them - and SC's
        /// six scRaider classes), told apart by the class Patch_CreatingClass keeps for the build: InitInventory runs from
        /// EntityAlive.Awake, before the entity knows its class. Every other NPC keeps 3.4's one slot. The same on every
        /// machine (the class list is config, read on clients too), so host and client agree on the toolbelt's size.
        ///
        /// Checked against 0-XNPCCore 3.4 and SCore 3.4 (decompiled): their weapon code writes and reads slot 0 and the held
        /// slot (SelectedSlot, 0 on an NPC) - SCore's UpdateWeapon SetItem(0, ...), XNPC's equip and bandage SetItem(0, ...),
        /// GetItemStack(holdingItemIdx) - and scans the whole toolbelt only to count or find items (EntityUtilities' item
        /// stores, Voice Control's NpcHasItem / NpcRemoveItem), which is how both ran on 3.2's ten slots. Nothing assumes one.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_NpcToolbelt
        {
            public static MethodBase Target()
            {
                return AccessTools.DeclaredMethod(typeof(EntityAlive), "InitInventory", NoArgs);
            }

            [HarmonyPrepare]
            public static bool Prepare()
            {
                try
                {
                    if (Target() == null)
                    {
                        ScLog.Err("EntityAlive.InitInventory() not found (game update?) - the community's NPCs get a one-slot toolbelt");
                        return false;
                    }
                    // an override on SCore's V4 would bypass this one (its body never calls the base we patch)
                    if (AccessTools.DeclaredMethod(typeof(EntityAliveSDXV4), "InitInventory", NoArgs) != null)
                        ScLog.Warn("EntityAliveSDXV4 declares its own InitInventory (SCore update?) - SC's ten-slot toolbelt may not reach its bodies");
                    return true;
                }
                catch (Exception e) { ScLog.Once("npc-bind-toolbelt", e); }
                return false;
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return Target(); }

            [HarmonyPrefix]
            public static bool Prefix(EntityAlive __instance)
            {
                try
                {
                    if (__instance == null || __instance.inventory != null || !(__instance is EntityAliveSDXV4)) return true;
                    int cls = creatingClass;
                    if (cls == 0 || !ScXnpc.IsManagedClass(cls)) return true;
                    __instance.inventory = new Inventory(__instance, ToolbeltSlots);
                    Patches_Xnpc.Count("toolbelt-" + ToolbeltSlots);
                    return false;
                }
                catch (Exception e)
                {
                    ScLog.Once("npc-toolbelt", e);
                    return true;
                }
            }
        }
#endif
    }
}
