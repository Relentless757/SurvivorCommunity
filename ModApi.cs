using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace SurvivorCommunity
{
    /// <summary>
    /// Entry point: dependency check, Harmony patches verified target by target, ModEvents wiring.
    /// Inert (with an error line) when 0-SCore / 0-XNPCCore are missing.
    /// </summary>
    public class SurvivorCommunityModApi : IModApi
    {
        public const string HarmonyId = "SurvivorCommunity";
        private static bool initialized;

        public void InitMod(Mod _modInstance)
        {
            try
            {
                if (initialized) { ScLog.Warn("InitMod called twice - ignoring"); return; }
                initialized = true;
                string ver = (_modInstance != null ? _modInstance.VersionString : null) ?? "?";
                ScLog.Out("init SurvivorCommunity v" + ver);

                if (!DependenciesPresent()) return;

                // fully qualified: SCore declares a namespace called Harmony, which shadows the type
                var h = new HarmonyLib.Harmony(HarmonyId);
#if SC_V34
                // 3.4: class by class (PatchClassByClass), and nothing between here and the wiring below can end InitMod:
                // on 3.2 one patch class whose target was gone threw out of PatchAll, every class after it stayed unpatched
                // and the shared catch skipped the ModEvents handlers and the OnHire hook - the whole mod inert.
                PatchClassByClass(h);
                try { VerifyPatches(h); }
                catch (Exception e) { ScLog.Err("patch verification failed: " + e.Message); }
                // round U2: 0-XNPCCore's downed state for SC's people at home - ON only when all five re-pointed calls were found
                try { Patches_Downed.Settle(); }
                catch (Exception e) { ScLog.Err("downed (round U2): " + e.Message + " - the feature is OFF"); }
                // W2: the copy of vanilla's biome spawn step that SC's wild raiders get when another mod turns the game's
                // enemy biome spawns off (a reverse patch; logs and stays off on any surprise)
                ScWildRaiders.Build();
                // the overwhelmed rule reads 0-XNPCCore's private close-range state: bound now, so a rename shows at startup
                try { if (ScArms.Bind()) ScLog.Out("overwhelmed rule: 0-XNPCCore's close-range swap state bound"); }
                catch (Exception e) { ScLog.Err("overwhelmed rule: binding 0-XNPCCore's close-range state failed: " + e.Message); }
                LogFeatureHashes();
#else
                h.PatchAll(Assembly.GetExecutingAssembly());
                VerifyPatches(h);
                // the overwhelmed rule reads 0-XNPCCore's private close-range state: bound now, so a rename shows at startup
                if (ScArms.Bind()) ScLog.Out("overwhelmed rule: 0-XNPCCore's close-range swap state bound");
                LogFeatureHashes();
#endif
                // E6: a group lost (E2's Fall, any cause) costs morale when it was a friend, and owes a refill - once per process
                // round MP6 (merge): an enclave falling concerns every community with a standing there - OnFallen walks them itself
                // (part relations: each one's morale loss and tell) while its refill is the world's; it runs once
                ScEnclaves.Fallen += ScEncAllies.OnFallen;

                ModEvents.GameStarting.RegisterHandler((ref ModEvents.SGameStartingData _d) =>
                {
                    ScConfig.Reset();
                    ScLog.ResetForNewWorld();
                    ScTicker.Reset();
                    ScMirror.Reset();                // round MP1: no client copy or push state carries into a new world
                    ScActions.Reset();               // round MP2: no unanswered request carries into a new world
                    ScTalkContext.Reset();           // round MP2 (part M): no talk context or hand-over carries into a new world
                    ScCraftFromBase.Reset();         // round MP4 (review cfs1 M1): no open-box set or base list carries into a new world
#if SC_V34
                    ScSleep.Reset();                 // round n06: no night lease carries into a new world
                    ScSocial.Reset();                // round U3: no exchange, walk-over or gathering carries into a new world
                    ScReach.Reset();                 // round n08: mark counts are per session
                    ScLoadout.ResetModCache();       // round n08: canonical weapon mods are rolled per world
                    UAI.UAIConsiderationScIntruder.ResetHunts();   // round n08: the home rule's "after somebody" latches
#endif
                    ScZone.Reset();
                    ScTraits.Reset();
                ScSkills.Reset();
                ScRadio.Reset();
                ScOutposts.Reset();
                ScEnclaves.Reset();
                ScEnclavePeople.Reset();
                ScEncDoors.Reset();                    // round 32a: the doors passed this session
                ScEnclaveDeals.Reset();
                ScEncRequests.Reset();
                ScEncSearch.Reset();
                ScEncSiege.Reset();
                ScEncCalls.Reset();
                ScEncRaids.Reset();
                ScEncMoves.Reset();
                ScEncJoin.Reset();
                ScBands.Reset();
#if SC_V34
                ScWildRaiders.Reset();                 // W2: its counters and entry cache
                UAI.UAIConsiderationScHoldEngagedHome.Reset();   // H1: its counters and the once-per-body log set
#endif
                ScInfestations.Reset();
                ScSleeperCap.Reset();
                ScPoiIndex.Reset();
                ScHomeLink.Reset();
                ScHomeHold.Reset();
                ScWeaponWarm.Reset();
                ScGarrison.Reset();
                ScScouting.Reset();
                ScOutpostRisk.Reset();
                ScMissing.Reset();
                ScRunners.Reset();
                ScDoors.Reset();
                ScScan.Reset();
                ScPortraits.Reset();
                ScLooks.Reset();
                Patches_Sdcs.Reset();
                ScVanity.Reset();
                ScAim.Reset();
                ScLoadout.Reset();
                ScMedic.Reset();
                Patches_VoiceControl.Reset();
                ScVoice.Reset();
                ScXnpc.Reset();
                Patches_Xnpc.Reset();
                ScAmmo.Reset();
                ScMags.Reset();
                ScArms.Reset();
                ScMelee.Reset();
                ScShooting.Reset();
                ScRelax.Reset();
                ScNoise.Reset();
                ScBaseAttack.Reset();
                ScWatch.Reset();
                ScDefence.Reset();
                ScSiege.Reset();
                ScTower.Reset();
                ScFacilities.Reset();
                ScActivities.Reset();
                ScFacilityTab.Reset();                 // round m1f: the Facilities tab
                ScLightsOut.Reset();                   // round m1f (BD1): Lights out
                ScWatchFatigue.Reset();                // round m1f (BD1): watch fatigue's last terms
                ScSalvage.Reset();                     // round m1f (BD1): salvage's noted horde size
                Patches_TraderArea.Reset();            // round tl1: which NPCs were logged
                ScCategories.Reset();
                Patches_Npc.Reset();
                });
                ModEvents.GameStartDone.RegisterHandler((ref ModEvents.SGameStartDoneData _d) =>
                {
                    // round mt1 (part C): a client's GameStartDone comes BEFORE the server's blocks, items and dialogs arrive, so
                    // there the steps wait (ScConfig.HoldStartDone) and the GameUpdate handler below runs them once the tables
                    // are in. Single player, a listen host and a dedicated server run them here, as before
                    bool held = false;
                    try { held = ScConfig.HoldStartDone(); }
                    catch (Exception e) { ScLog.Once("gamestartdone-hold", e); }
                    if (!held) OnGameStartDone();
                });
                ModEvents.GameUpdate.RegisterHandler((ref ModEvents.SGameUpdateData _d) =>
                {
                    // round mt1 (part C): a client's held game-start steps, once per world when the server's tables are in
                    // (first, so the ticks below already see this world's config; a bool test while nothing is held)
                    try { if (ScConfig.StartDoneDue()) OnGameStartDone(); }
                    catch (Exception e) { ScLog.Once("gamestartdone-late", e); }
                    ScTicker.OnGameUpdate();
                    ScWeaponWarm.Update();             // NPC weapon models in the background (round wp)
                    ScBarks.Tick();                    // V2: SC's greetings, chatter and raider taunts (each paces itself)
                    ScLooks.NetTick();                 // round MP1: a client asks for the server's looks and rebuilds a body with one (out at once elsewhere)
                });
                ModEvents.WorldShuttingDown.RegisterHandler((ref ModEvents.SWorldShuttingDownData _d) =>
                {
                    // round MP0: first, on its own (the review's N9) - the next session says hello again whatever throws below
                    try { ScNet.Reset(); } catch (Exception e) { ScLog.Once("net-reset", e); }
                    try { ScLooks.ResetNet(); } catch (Exception e) { ScLog.Once("looks-net-reset", e); }   // round MP1: the looks sent and received
                    try { ScActions.Reset(); } catch (Exception e) { ScLog.Once("actions-reset", e); }      // round MP2: a client's unanswered requests
                    try { ScTalkContext.Reset(); } catch (Exception e) { ScLog.Once("talk-reset", e); }     // round MP2 (part M): the talk context
                    ScMirror.OnWorldShuttingDown();          // round MP1: a client forgets the server's copy (its own try)
                    ScConfig.DropHeldStartDone();            // round mt1 (part C): a client's held game-start steps end with the world
                    ScRunners.OnWorldShuttingDown();   // first: runners in the world go back to their snapshot before the world is saved
                    ScEnclavePeople.OnWorldShuttingDown();   // enclave bodies recorded and taken out before the save (E2)
                    ScBands.OnWorldShuttingDown();           // band bodies the same, their walk re-planned from where they were (E13a)
                    ScGarrison.OnWorldShuttingDown();        // round op1 review (M1): no brief load of a parked body outlives the world
                    // a dead NPC's window still held by a remote player drops at the body now (the world is saved after this event)
                    if (ScCommunity.IsHost) Patches_Npc.Patch_SetDead.ReleaseAll(true);
#if SC_V34
                    ScRadioTalk.Reset();                     // R2: no runner's line left playing or queued into the menu
                    ScRunnerCall.Reset();                    // round 29b: no runner's call for help (or its marker) into the next world
#endif
                    // round 30 review M2: streamer names on bodies the save will not keep go back to the list, before the last save
                    ScStreamerNames.OnWorldShuttingDown();
                    ScPortraits.Reset(); ScHomeLink.Reset(); ScHomeHold.Reset(); ScWeaponWarm.Reset(); ScRegistry.FlushAndReset();
                });
                ModEvents.GameShutdown.RegisterHandler((ref ModEvents.SGameShutdownData _d) => { ScPortraits.Reset(); ScRegistry.FlushAndReset(); });
                ModEvents.EntityKilled.RegisterHandler((ref ModEvents.SEntityKilledData _d) =>
                {
                    // round MP6 (part G): in the killer's community's scope (a member's skill tells go to her community)
                    if (ScCommunity.IsHost && ScRegistry.Loaded)
                        using (ScGates.ScopeOfActor(_d.KillingEntity)) ScSkills.OnEntityKilled(_d.KilledEntitiy, _d.KillingEntity);
                });
#if SC_V34
                // round U4: an enemy you kill that was on a member is Bond for her (ScBond: queued here, written by the ticker)
                ModEvents.EntityKilled.RegisterHandler((ref ModEvents.SEntityKilledData _d) => ScBond.OnEntityKilled(_d.KilledEntitiy, _d.KillingEntity));
#endif
                // a player who leaves with an NPC's inventory window open never sends its close (ScAmmo's remote-window guard)
                ModEvents.PlayerDisconnected.RegisterHandler((ref ModEvents.SPlayerDisconnectedData _d) =>
                {
                    try
                    {
                        if (_d.ClientInfo != null) ScAmmo.ForgetPlayer(_d.ClientInfo.entityId);
                        if (_d.ClientInfo != null) ScNet.ForgetClient(_d.ClientInfo.entityId);   // round MP0
#if SC_V34
                        // round mt1 (part H): their talk holds and work shifts end now (she walks back to her post), not at the
                        // heartbeat's grace; nothing at a shutdown (shifts live in memory, the resident rule stations her at the next load)
                        if (_d.ClientInfo != null && !_d.GameShuttingDown) ScWork.ForgetPlayer(_d.ClientInfo.entityId);
#endif
                    }
                    catch (Exception e) { ScLog.Once("player-disconnected", e); }
                });
                // round MP0: a client says hello to the server once its own player has spawned (SC's protocol check). Fires on
                // every machine for every player spawn; IsLocalPlayer marks this machine's own
                ModEvents.PlayerSpawnedInWorld.RegisterHandler((ref ModEvents.SPlayerSpawnedInWorldData _d) =>
                {
                    try { if (_d.IsLocalPlayer) ScNet.OnLocalPlayerSpawned(); }
                    catch (Exception e) { ScLog.Once("player-spawned", e); }
                    // round MP7 (core): on the server, the spawning player's record (PerPlayer/PerParty: their own relations when they
                    // have none) - and the allies reconcile there (round MP7 (part AP))
                    ScMembership.OnPlayerSpawned(_d.EntityId, _d.ClientInfo, _d.IsLocalPlayer);
                    // round MP7 (review U): m3 - a client's role= / ownops= again now that the server has its body
                    if (!_d.IsLocalPlayer) ScMirror.OnPlayerSpawned(_d.ClientInfo);
                });
                // round MP7 (part AP): vanilla's land-claim ally changes (PerPlayer: a founder's ally joins, a removed one loses access;
                // ScAllies acts only on the server)
                ScAllies.Subscribe();

                // The only event SCore raises. Hiring a member through the vanilla popup should be impossible
                // (the response is hidden for members) - if it happens anyway, the member is simply following.
                EntityUtilities.OnHire += (hiredId, ownerId) =>
                {
                    try
                    {
                        // a hire ends a grudge, as joining does (ScTrust): suspended, it would come back on dismissal
                        EntityAlive hired = ScCommunity.World != null ? ScCommunity.World.GetEntity(hiredId) as EntityAlive : null;
                        if (hired != null) ScTrust.StripOnJoin(hired);
                        // round MP1: SCore raises this on the hiring client too - a client's copy of the registry is never changed
                        Member m = ScRegistry.Loaded ? ScRegistry.GetMember(hiredId) : null;
                        if (m != null && m.Status == MemberStatus.Resident)
                        using (ScRegistry.Use(m))       // round MP6 (part G): her community's
                        {
                            m.Status = MemberStatus.Following;
                            ScStuck.ResetGain(m);   // round 29 review L5: no held move back outlives the status
                            // SCore's hire sets the follow order itself, but the cvar can lag a tick; the ticker
                            // reconciles it either way. Recorded because this silently changes a member's state.
                            ScLog.Warn(m.Name + " was hired through the vanilla popup - treated as Follow (order now "
                                       + EntityUtilities.GetCurrentOrder(hiredId) + ")");
                        }
                    }
                    catch (Exception e) { ScLog.Once("onhire", e); }
                    return true;
                };

                ScLog.Out("init complete (Harmony id " + HarmonyId + ")");
            }
            catch (Exception e)
            {
                ScLog.Err("InitMod failed - the mod is inert for this session");
                Log.Exception(e);
            }
        }

        /// <summary>
        /// SC's game-start steps (the GameStartDone handler's body, unchanged and in the same order). Round mt1 (part C): run
        /// from GameStartDone on a server; on a client from the first game update once the server's blocks and items are in
        /// (ScConfig.HoldStartDone / StartDoneDue) - every step reads them, and each is safe to run that late, once per world.
        /// </summary>
        private static void OnGameStartDone()
        {
            try { ScConfig.EnsureParsed(); ReportDialogCoverage(); }
            catch (Exception e) { ScLog.Once("gamestartdone", e); }
            // the crafted Watch Post (a shape helper) shows the post's own description
            ScWatch.DescribeHelper();
#if SC_V34
            // round m1p: its description names this game's numbers (ScWatchDesc), not only the defaults
            ScWatchDesc.Apply();
#endif
            // the well kit places Level 1 only; levels 2-5 are upgrades (round wl). Every machine: placement is local.
            ScFacilities.RestrictWellKit();
            // round 33b: the bundles' Open action kept (with a usable pool) or taken off (BundleOpen false), once per world
            ScBundleOpen.Apply();
            // round md1: MedsResupplyMix validated against this game's items like the pools above (one line, warnings once)
            ScMedic.ResolveMix();
            // Locked & Loaded's NPC magazines (M11): bound once per game, its state logged every game
            try { ScMags.Resolve(); }
            catch (Exception e) { ScLog.Once("gamestartdone-mags", e); }
            // Vanity Vault bound inside the loading screen, not in the first painted body's build (round vb)
            try { ScVanity.Warm(); }
            catch (Exception e) { ScLog.Once("gamestartdone-vanity", e); }
            // enclaves (E1): seeded, found again or topped up now that the world, its POI list, the registry and the
            // player are all here (host only; registry-only work - only the chunks round the player have started
            // loading, and the pre-clear covers any of those)
            if (ScCommunity.IsHost) ScEnclaves.OnGameStart();
            // infestations (I1): each one's POI found again - after the enclaves, so none is left on an enclave's POI
            if (ScCommunity.IsHost) ScInfestations.OnGameStart();
            // streamer names (round 30): the list read (made with its header when the switch is on and it is missing)
            if (ScCommunity.IsHost) ScStreamerNames.OnGameStart();
            // round MP7 (part AP): PerPlayer - the land-claim allies reconciled with the communities at load (the ally store is the
            // game's by now; no ally event fires at load)
            if (ScCommunity.IsHost) ScAllies.OnGameStartDone();
        }

        private static bool DependenciesPresent()
        {
            bool ok = true;
            if (ModManager.GetMod("0-SCore_sphereii") == null && ModManager.GetMod("0-SCore") == null)
            {
                ScLog.Err("0-SCore is not loaded - SurvivorCommunity is inert");
                ok = false;
            }
            if (ModManager.GetMod("0-XNPCCore") == null)
            {
                ScLog.Err("0-XNPCCore is not loaded - SurvivorCommunity is inert");
                ok = false;
            }
            try
            {
                if (Type.GetType("EntityAliveSDX, SCore") == null)
                {
                    ScLog.Err("EntityAliveSDX (SCore.dll) not found - SurvivorCommunity is inert");
                    ok = false;
                }
                // 0-XNPCCore 3.2's survivors are SCore's EntityAliveSDXV4, and ScNpc names it in every NPC check: on an
                // SCore without it each of those checks would throw on first use, so the mod stays out of the way instead.
                if (Type.GetType("EntityAliveSDXV4, SCore") == null)
                {
                    ScLog.Err("EntityAliveSDXV4 (SCore.dll) not found - this SCore is older than 0-XNPCCore 3.2 needs; SurvivorCommunity is inert");
                    ok = false;
                }
                // 0-XNPCCore 3.2's player-body survivors (EModelSdcsNpc, AvatarSdcsNpcController) are named directly by
                // ScXnpc and the looks code: without NPCSdcsCharacters.dll every body check would throw on first use
                if (Type.GetType("EModelSdcsNpc, NPCSdcsCharacters") == null)
                {
                    ScLog.Err("EModelSdcsNpc (0-XNPCCore's NPCSdcsCharacters.dll) not found - 0-XNPCCore is older than 3.2 or incomplete; SurvivorCommunity is inert");
                    ok = false;
                }
            }
            catch (Exception e)
            {
                ScLog.Err("SCore type probe failed: " + e.Message);
                ok = false;
            }
            return ok;
        }

#if SC_V34
        /// <summary>
        /// 3.4: Harmony's PatchAll, one patch class at a time (the same classes in the same order: Harmony's own
        /// GetTypesFromAssembly, then CreateClassProcessor(type).Patch() for each), each in its own try. A class whose target
        /// is gone - or whose patch method no longer binds - is logged with its exception and counted, and the classes after
        /// it are still patched; VerifyPatches then names every expected target left unpatched (MISSING PATCH). Logs one
        /// line: classes applied, classes whose Prepare turned them off, classes that failed.
        /// </summary>
        private static void PatchClassByClass(HarmonyLib.Harmony h)
        {
            int applied = 0, off = 0, failed = 0;
            Type[] types;
            try { types = AccessTools.GetTypesFromAssembly(Assembly.GetExecutingAssembly()); }
            catch (Exception e)
            {
                ScLog.Err("patching: the assembly's types could not be listed - nothing is patched: " + e.Message);
                return;
            }
            foreach (Type type in types)
            {
                if (type == null) continue;
                try
                {
                    List<MethodInfo> done = h.CreateClassProcessor(type).Patch();
                    if (done == null) continue;                  // not a patch class
                    if (done.Count > 0) applied++;
                    else off++;                                  // its Prepare said no (each logs why)
                }
                catch (Exception e)
                {
                    failed++;
                    ScLog.Err("PATCH CLASS FAILED " + type.FullName + " - its patches are off for this session: " + e.Message);
                    Log.Exception(e);
                }
            }
            if (failed > 0) ScLog.Err("patching: " + applied + " class(es) applied, " + off + " off by their Prepare, " + failed + " FAILED");
            else ScLog.Out("patching: " + applied + " class(es) applied, " + off + " off by their Prepare, none failed");
        }

#endif
        private struct Target
        {
            public string Label;
            public MethodBase Method;
        }

        private static void Add(List<Target> list, string label, MethodBase m)
        {
            list.Add(new Target { Label = label, Method = m });
        }

        /// <summary>X2c's targets (0-XNPCCore 3.4.03 only); a separate method so a missing type fails here, not in ExpectedTargets.</summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void AddBooks(List<Target> t)
        {
#if SC_V34
            Add(t, "SdcsBooks::List", Patches_Craft.BookListTarget());
            Add(t, "SdcsBooks::Teaches", Patches_Craft.BookTeachesTarget());
            Add(t, "SdcsBooks::Order", Patches_Craft.BookOrderTarget());
            Add(t, "MinEventActionAddProgressionLevel::Execute", Patches_Craft.MagazineLevelTarget());
            Add(t, "SdcsBooks::Learned", Patches_Craft.BookLearnedTarget());   // 3.4.06's five parameters, or 3.4.03-3.4.05's four
            Add(t, "SdcsSayLine::Say", Patches_Craft.SayLineTarget());
            Add(t, "SdcsProgress::List", Patches_Craft.ProgressListTarget());
            if (Patches_Craft.CraftWindowType != null) Add(t, "XUiC_NpcCraftWindow::CancelJob", Patches_Craft.CraftCancelTarget());
#endif
        }

        /// <summary>
        /// Round U4's targets (Patches_Relationship), each listed when the 0-XNPCCore type it belongs to is loaded - so a renamed
        /// method there shows as a MISSING PATCH line, while a 0-XNPCCore older than 3.4.06 (no RelationshipState) expects none of
        /// the Voice Control ones. A separate method, as AddBooks.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void AddNpc306(List<Target> t)
        {
#if SC_V34
            bool rel = Patches_Relationship.RelType != null;
            if (rel)
            {
                Add(t, "RelationshipState::Apply", Patches_Relationship.ApplyTarget());
                Add(t, "RelationshipState::ApplyGlobal", Patches_Relationship.ApplyGlobalTarget());
                Add(t, "RelationshipState::TryEffective", Patches_Relationship.TryEffectiveTarget());
                if (Patches_Relationship.HooksType != null)
                {
                    Add(t, "NPCEventMemoryHooks::OnRevived", Patches_Relationship.RevivedTarget());
                    Add(t, "NPCEventMemoryHooks::OnDowned", Patches_Relationship.DownedTarget());
                    Add(t, "NPCEventMemoryHooks::OnEntityKilledRelationship", Patches_Relationship.KilledTarget());
                }
                if (Patches_VoiceControl.ChatType != null) Add(t, "NPCChatComponent::TrySettleWeekFromGift", Patches_Relationship.SettleWeekTarget());
                if (Patches_Relationship.ConsoleType != null) Add(t, "ConsoleCmdLLMChat::RunStandingCommand", Patches_Relationship.StandingTarget());
            }
            if (Patches_Relationship.GiftBagType != null) Add(t, "SdcsGiftBag::TryAddGift", Patches_Relationship.GiftAddTarget());
#endif
        }

        private static List<Target> ExpectedTargets()
        {
            var t = new List<Target>();
            Add(t, "PersistentPlayerList::ReadXML", AccessTools.Method(typeof(PersistentPlayerList), "ReadXML", new[] { typeof(string) }));
            Add(t, "PersistentPlayerList::Write(string)", AccessTools.Method(typeof(PersistentPlayerList), "Write", new[] { typeof(string) }));
            // Patches_Npc.cs - both of SCore's friendly NPC classes declare their own (ScNpc.Declared)
            Add(t, "EntityAliveSDX::SetDead", AccessTools.Method(typeof(EntityAliveSDX), "SetDead", new Type[0]));
            Add(t, "EntityAliveSDX::PostInit", AccessTools.Method(typeof(EntityAliveSDX), "PostInit", new Type[0]));
            Add(t, "EntityAliveSDX::IsSavedToFile", AccessTools.DeclaredMethod(typeof(EntityAliveSDX), "IsSavedToFile", new Type[0]));
            Add(t, "EntityAliveSDXV4::SetDead", AccessTools.DeclaredMethod(typeof(EntityAliveSDXV4), "SetDead", new Type[0]));
            Add(t, "EntityAliveSDXV4::PostInit", AccessTools.DeclaredMethod(typeof(EntityAliveSDXV4), "PostInit", new Type[0]));
            Add(t, "EntityAliveSDXV4::IsSavedToFile", AccessTools.DeclaredMethod(typeof(EntityAliveSDXV4), "IsSavedToFile", new Type[0]));
            // ScDoors.cs - members open (and shut) doors
            Add(t, "EntityMoveHelper::CheckForDoorAndOpen", AccessTools.Method(typeof(EntityMoveHelper), "CheckForDoorAndOpen", new Type[0]));
            Add(t, "SleeperEventData::Update", AccessTools.Method(typeof(SleeperEventData), "Update", new Type[0]));
            // Patches_QuestTitle.cs - per-POI favour quest title + marker
            Type[] binding = { typeof(string).MakeByRefType(), typeof(string) };
            Add(t, "XUiC_QuestEntry::GetBindingValueInternal", AccessTools.Method(typeof(XUiC_QuestEntry), "GetBindingValueInternal", binding));
            Add(t, "XUiC_QuestTrackerWindow::GetBindingValueInternal", AccessTools.Method(typeof(XUiC_QuestTrackerWindow), "GetBindingValueInternal", binding));
            Add(t, "XUiC_QuestDescriptionWindow::GetBindingValueInternal", AccessTools.Method(typeof(XUiC_QuestDescriptionWindow), "GetBindingValueInternal", binding));
            Add(t, "XUiC_QuestObjectivesWindow::GetBindingValueInternal", AccessTools.Method(typeof(XUiC_QuestObjectivesWindow), "GetBindingValueInternal", binding));
#if SC_V34
            Add(t, "TileEntity::OnUnlockedServer", Patches_Craft.UnlockedTarget());   // round MP3 (part G): a client's station craft with a member
            Add(t, "RemoteCraftingUtils::GetTileEntities(EntityAlive, float, bool)", Patch_CraftFromBase.Target());   // round cfs1: Craft from Storage in the base (round t2: every count and take)
            Add(t, "RemoteCraftingUtils::AddToNearbyContainer", Patch_CraftFromBaseDropBox.Target());   // round t2: the drop box keeps 0-SCore's range
            Add(t, "Configuration::CheckFeatureStatus(string, string, bool)", Patch_UpgradeRepairFromStorage.Target());   // round t2: upgrades and repairs from storage
            // round MP3 (review packing/gear m2): a client's late station write-back after a forced unlock is not paid twice
            Add(t, "TileEntityWorkstation::read", Patches_Craft.WorkstationReadTarget());
            // round MP3 (part L): a box opened by hand leaves the views that show it, and no throw while a change is in flight
            Add(t, "TEFeatureStorage::OnLockRequestServer", Patches_LinkedStorage.Patch_LinkedVanillaOpen.Target());
            Add(t, "XUiC_DragAndDropWindow::DropCurrentItem()", AccessTools.DeclaredMethod(typeof(XUiC_DragAndDropWindow), "DropCurrentItem", Type.EmptyTypes));
            Add(t, "XUiC_DragAndDropWindow::DropCurrentItem(int)", AccessTools.DeclaredMethod(typeof(XUiC_DragAndDropWindow), "DropCurrentItem", new[] { typeof(int) }));
            // round MP3 (review storage m3): nor through the window's own put-back
            Add(t, "XUiC_DragAndDropWindow::PlaceItemBackInInventory", Patches_LinkedStorage.Patch_LinkedHoldPlaceBack.Target());
            // 3.4: StartQuest(Quest.QuestSource source, bool newQuest = true, bool notify = true) - the source is new
            Add(t, "Quest::StartQuest", AccessTools.Method(typeof(Quest), "StartQuest", new[] { typeof(Quest.QuestSource), typeof(bool), typeof(bool) }));
#else
            Add(t, "Quest::StartQuest", AccessTools.Method(typeof(Quest), "StartQuest", new[] { typeof(bool), typeof(bool) }));
#endif
            Add(t, "Quest::CloseQuest", AccessTools.Method(typeof(Quest), "CloseQuest", new[] { typeof(Quest.QuestState), typeof(List<BaseReward>) }));
            Add(t, "XUiC_QuestListWindow::Update", AccessTools.Method(typeof(XUiC_QuestListWindow), "Update", new[] { typeof(float) }));
            // Patches_Menu.cs - the ESC menu entry
            Add(t, "XUiC_InGameMenuWindow::Init", AccessTools.Method(typeof(XUiC_InGameMenuWindow), "Init", new Type[0]));
            // Patches_Radio.cs - air drops only on request
            Add(t, "AIDirectorAirDropComponent::SpawnAirDrop", AccessTools.Method(typeof(AIDirectorAirDropComponent), "SpawnAirDrop", new Type[0]));
            // Patches_Sdcs.cs - body fixes for the community's XNPC bodies (M10 round 2 removed SC's own player-body shim)
            Add(t, "EModelSDCS::OnDestroy", AccessTools.DeclaredMethod(typeof(EModelSDCS), "OnDestroy", new Type[0]));
            Add(t, "CharacterGazeController::LateUpdate", AccessTools.DeclaredMethod(typeof(CharacterGazeController), "LateUpdate", new Type[0]));
            Add(t, "LegacyAvatarController::StartAnimationJump", AccessTools.DeclaredMethod(typeof(LegacyAvatarController), "StartAnimationJump", new[] { typeof(AnimJumpMode) }));
            Add(t, "EntityTargetingUtilities::CanTakeDamage", AccessTools.Method(typeof(EntityTargetingUtilities), "CanTakeDamage", new[] { typeof(EntityAlive), typeof(Entity) }));
            // Patches_Trust.cs - survivor trust: the player's melee lands, a hit makes a survivor hostile for good
            Add(t, "EntityTargetingUtilities::CanDamage", AccessTools.Method(typeof(EntityTargetingUtilities), "CanDamage", new[] { typeof(EntityAlive), typeof(Entity) }));
            Add(t, "EntityUtilities::GetFactionRelationship", AccessTools.Method(typeof(EntityUtilities), "GetFactionRelationship", new[] { typeof(EntityAlive), typeof(EntityAlive) }));
            Add(t, "EntityAliveSDXV4::ProcessDamageResponseLocal", AccessTools.DeclaredMethod(typeof(EntityAliveSDXV4), "ProcessDamageResponseLocal", new[] { typeof(DamageResponse) }));
            // ScTeam.cs (round k1) - a player's landed hit on an enemy: you count in the team verdict only while you fight
            Add(t, "EntityAlive::ProcessDamageResponseLocal", AccessTools.DeclaredMethod(typeof(EntityAlive), "ProcessDamageResponseLocal", new[] { typeof(DamageResponse) }));
            Add(t, "Entity::PhysicsSetHeight", AccessTools.Method(typeof(Entity), "PhysicsSetHeight", new[] { typeof(float) }));
            Add(t, "EModelBase::StartRagdoll", AccessTools.Method(typeof(EModelBase), "StartRagdoll", new[] { typeof(float) }));
            Add(t, "EModelBase::FrameUpdateRagdoll", AccessTools.Method(typeof(EModelBase), "FrameUpdateRagdoll", new Type[0]));
            Add(t, "EntityAlive::OnUpdateLive", AccessTools.Method(typeof(EntityAlive), "OnUpdateLive", new Type[0]));
            Add(t, "EntityBuffs::AddBuff", AccessTools.Method(typeof(EntityBuffs), "AddBuff", new[] { typeof(string), typeof(Vector3i), typeof(int), typeof(bool), typeof(bool), typeof(float) }));
            Add(t, "EntityAlive::UseHoldingItem", AccessTools.Method(typeof(EntityAlive), "UseHoldingItem", new[] { typeof(int), typeof(bool) }));
            Add(t, "EntityAliveSDX::ExecuteAction", AccessTools.Method(typeof(EntityAliveSDX), "ExecuteAction", new[] { typeof(bool), typeof(int) }));
#if SC_V34
            // 3.4 (SCore 3.4): Write(PooledBinaryWriter, StreamModeWrite) - ScMedic's mid-use save; V4's also carries X13
            Add(t, "EntityAliveSDX::Write", AccessTools.Method(typeof(EntityAliveSDX), "Write", new[] { typeof(PooledBinaryWriter), typeof(StreamModeWrite) }));
            Add(t, "EntityAliveSDXV4::ExecuteAction", AccessTools.DeclaredMethod(typeof(EntityAliveSDXV4), "ExecuteAction", new[] { typeof(bool), typeof(int) }));
            Add(t, "EntityAliveSDXV4::Write", AccessTools.DeclaredMethod(typeof(EntityAliveSDXV4), "Write", new[] { typeof(PooledBinaryWriter), typeof(StreamModeWrite) }));
#else
            Add(t, "EntityAliveSDX::Write", AccessTools.Method(typeof(EntityAliveSDX), "Write", new[] { typeof(System.IO.BinaryWriter), typeof(bool) }));
            Add(t, "EntityAliveSDXV4::ExecuteAction", AccessTools.DeclaredMethod(typeof(EntityAliveSDXV4), "ExecuteAction", new[] { typeof(bool), typeof(int) }));
            Add(t, "EntityAliveSDXV4::Write", AccessTools.DeclaredMethod(typeof(EntityAliveSDXV4), "Write", new[] { typeof(System.IO.BinaryWriter), typeof(bool) }));
#endif
            Add(t, "EntityAliveSDXV4::SetModelLayer", Patches_Xnpc.Patch_ModelLayer.Target());
#if SC_V34
            // 3.4: the community's bodies keep 3.2's ten-slot toolbelt (Patches_Npc: the class being built, then InitInventory)
            Add(t, "EntityFactory.CreateEntityOperation::CompleteEntity", Patches_Npc.Patch_CreatingClass.Target());
            Add(t, "EntityAlive::InitInventory", Patches_Npc.Patch_NpcToolbelt.Target());
            // 3.4: linked storage's shift-click and move-all (ScLinkedContainer34: 3.2's AddItem / TryStackItem logic)
            Add(t, "TEFeatureStorage::AddItem", ScLinkedContainer.Patch_LinkedAddItem.Target());
            Add(t, "TEFeatureStorage::TryStackItem", ScLinkedContainer.Patch_LinkedTryStackItem.Target());
            Add(t, "ItemStackGrid::Sort", ScLinkedContainer.Patch_LinkedSort.Target());
            // 3.4 (0-XNPCCore 3.4): X15 no XNPC self-heal on our bodies, X16 X12 steps aside inside XNPC's own swaps
            Add(t, "AvatarSdcsNpcController::SdcsSelfHealScan", Patches_Xnpc.SelfHealScanTarget());
            Add(t, "AvatarSdcsNpcController::SdcsEquipByName", Patches_Xnpc.EquipByNameTarget());
            // round lr1: X25 the magazine a gun is put away with goes back in, seated, when the gun is drawn
            Add(t, "Inventory::SetStackAt", Patches_Xnpc.SetStackAtTarget());
            // round nc1 (0-XNPCCore 3.4.06 final): X26 X13 alone saves the held gun's rounds - 0-XNPCCore's save-time stage and
            // unload hand-off of it are skipped on SC's bodies; and X18's own target, patched since its round but never listed
            Add(t, "AvatarSdcsNpcController::SdcsMagStageForWrite", Patches_Xnpc.MagStageForWriteTarget());
            Add(t, "AvatarSdcsNpcController::SdcsMagHandOffForUnload", Patches_Xnpc.MagHandOffTarget());
            Add(t, "AvatarSdcsNpcController::SdcsMagSaveHeld", Patches_Xnpc.MagSaveHeldTarget());
            // 3.4 Stage 2: X17 creator faces only on fresh bodies, SC's way (D4); ScMedic and 0-XNPCCore's party medic (D2);
            // no sleeper touch in an enclave's POI (D4); SCore's companion-window Dismiss is a member's Go Home (D5)
            Add(t, "SdcsCustomLook::ApplyCharacter", Patches_Xnpc.ApplyCharacterTarget());
            // 3.4 X1 (0-XNPCCore 3.4.02): X18 no level-based aim spread on our bodies, X19 no bandit spread on SC's raiders
            Add(t, "AvatarSdcsNpcController::SdcsAimTick", Patches_Xnpc.AimTickTarget());
            Add(t, "AvatarSdcsNpcController::SdcsBanditAimTick", Patches_Xnpc.BanditAimTickTarget());
            // X20: no base ammo restock from the player's chests for members (SC's stockpile only)
            Add(t, "AvatarSdcsNpcController::SdcsAmmoRestockTick", Patches_Xnpc.RestockTickTarget());
            // X21: the player's Charismatic Nature on members who are not hires (residents, guards, stand-ins)
            Add(t, "AvatarSdcsNpcController::SdcsLeadershipTick", Patches_Xnpc.LeadershipTickTarget());
            // X22 (0-XNPCCore 3.4.03): SC's raiders say no bandit target line, they taunt in their own voice
            Add(t, "AvatarSdcsNpcController::SdcsBanditLineOnNewTarget", Patches_Xnpc.BanditLineTarget());
            // X23: a band's raider is never a posted bandit; X24: the Shooting-star engage cap on 0-XNPCCore's new shot gate
            Add(t, "AvatarSdcsNpcController::SdcsPostRead", Patches_Xnpc.PostReadTarget());
            Add(t, "UAIConsiderationSdcsLineOfFire::GetScore", Patches_Xnpc.LineOfFireTarget());
            // round R1: X1c a bandit raider gets 0-XNPCCore's gear-only look apply (its weapon roll), never the rest. Listed
            // only while 0-XNPCCore has both methods (review L3: an 0-XNPCCore without its bandits stays quiet; a missing
            // method is warned about when the first bandit raider is made, ScBandits.WarnIfUnbound)
            if (ScBandits.Bindable()) Add(t, "SdcsLookApplier::Apply", ScBandits.ApplyTarget());
            Add(t, "SdcsMedic::ApplyHealOther", ScMedic.HealOtherTarget());
            Add(t, "SdcsMedic::Begin", ScMedic.MedicBeginTarget());
            Add(t, "SleeperVolume::TouchGroup", Patches_Enclaves.TouchGroupTarget());
            Add(t, "XUiC_SCoreCompanion::BtnDismiss_Controller_OnPress", Patches_VoiceControl.CompanionDismissTarget());
            // C1 (round 22): members craft by their stars - C1a/C1b no 0-XNPCCore XP or role learning on members; C1c-C1e SC's XP per
            // craft (backpack, station, a station's list when the player opens it); C1f a shift's job ends where she is
            Add(t, "SdcsNpcXp::Pay", Patches_Craft.PayTarget());
            Add(t, "AvatarSdcsNpcController::SdcsCountCrafts", Patches_Craft.CountCraftsTarget());
            Add(t, "SdcsCraft::PayXp", Patches_Craft.BackpackPayTarget());
            Add(t, "SdcsStationCraft::PayXp", Patches_Craft.StationPayTarget());
            Add(t, "TileEntityWorkstation::CheckForCraftComplete", Patches_Craft.CraftCompleteTarget());
            // round c2: a member's station crafts never become the player's - C2a her item in his open window (the output only, it
            // waits when that is full, no quest signal), C2b vanilla's stale "no room" retry off her item, C2c a station's list of
            // finished crafts kept crafter by crafter
            Add(t, "XUiC_RecipeStack::outputStack", Patches_Craft.RecipeOutputTarget());
            Add(t, "XUiC_RecipeStack::Update", Patches_Craft.RecipeUpdateTarget());
            Add(t, "TileEntityWorkstation::AddCraftComplete", Patches_Craft.CraftCompleteAddTarget());
            Add(t, "SdcsLootHere::Finish", Patches_Craft.LootFinishTarget());
            // C1g (review #3): 0-XNPCCore's owner-gated weapon picks skip a resident on a work shift
            Add(t, "AvatarSdcsNpcController::SdcsAnyWeaponTick", Patches_Craft.AnyWeaponTickTarget());
            Add(t, "AvatarSdcsNpcController::SdcsAnyWeaponMenuListing", Patches_Craft.AnyWeaponListingTarget());
            Add(t, "AvatarSdcsNpcController::SdcsTryWorldEntryMelee", Patches_Craft.WorldEntryMeleeTarget());
            // X2c (round 25b, 0-XNPCCore 3.4.03): C1h-C1m crafting magazines train a member's specialty (her rows, 0-XNPCCore's
            // gate, the read order's context, vanilla's level action, the line and its say-line seam); C1o "Your progress" role
            // rows; C1n the craft window's Cancel keeps a resident's work shift (Voice Control's window: only when it is loaded)
            // (Fable review X2, finding 3: the lookups name 3.4.03's types; an older 0-XNPCCore throws a type load here, which
            // would end the whole verification - caught, warned once, and the rest of the list still checked)
            try { AddBooks(t); }
            catch (Exception e) { ScLog.Warn("patch verification: 0-XNPCCore 3.4.03's books and progress page are not there (" + e.GetType().Name + ") - the X2c crafting patches are off (0-XNPCCore before 3.4.03?)"); }
            // round U2 (Patches_Downed.cs): 0-XNPCCore 3.4.06's downed calls re-pointed for SC's people at home - listed only when all
            // five exist (an 0-XNPCCore without them is Patches_Downed.Settle's one error, not five MISSING PATCH lines)
            try { foreach (var dt in Patches_Downed.Targets()) Add(t, dt.Key, dt.Value); }
            catch (Exception e) { ScLog.Warn("patch verification: the downed targets could not be listed (" + e.GetType().Name + ")"); }
            // round 28 (X3): X3a the talk hold (a talk links a resident at home); B1's first layer, SCore's Collect refusing SC's
            // people (with or without Voice Control: any caller)
            Add(t, "XUiC_DialogWindowGroup::OnOpen", Patches_Craft.TalkOpenTarget());
            Add(t, "EntitySyncUtils::Collect", Patches_VoiceControl.CollectTarget());
            // round 29a (J29): a home job's output noted for the crates; a picked crop replanted; the work kit never a fighting
            // weapon (the thrust and block-breaker picks; the Carried weapons listing shares C1g's entry above)
            Add(t, "SdcsStores::WindowAdd", ScJobs.WindowAddTarget());
            Add(t, "UAITaskHarvestSdcs::PickPlant", ScJobs.PickPlantTarget());
            Add(t, "AvatarSdcsNpcController::SdcsCarriedThrustWeapon", ScJobs.ThrustTarget());
            Add(t, "SdcsMeleeReach::BestBreaker", ScJobs.BreakerTarget());
#endif
            // Patches_Xnpc.cs - the classes SC drives on 0-XNPCCore's body (declared on XNPC's own types; X9 and X10 on
            // vanilla's: no talk prompt on a raider, turrets and motion sensors take raiders as enemies; X11-X13 on
            // SCore's V4: raiders' map markers, a swapped-out gun's rounds, the held gun across a save)
            Add(t, "EModelSdcsNpc::createModel", Patches_Xnpc.CreateModelTarget());
            Add(t, "MinEventActionSdcsApplyLook::Execute", Patches_Xnpc.ApplyLookTarget());
            Add(t, "EModelSdcsNpc::GenerateMeshes", Patches_Xnpc.GenerateMeshesTarget());
            Add(t, "AvatarSdcsNpcController::SdcsLevelTick", Patches_Xnpc.LevelTickTarget());
            Add(t, "AvatarSdcsNpcController::SdcsWearTick", Patches_Xnpc.WearTickTarget());
            Add(t, "AvatarSdcsNpcController::SdcsFailoverTick", Patches_Xnpc.FailoverTarget());
            Add(t, "AvatarSdcsNpcController::SdcsCloseRangeTick", Patches_Xnpc.CloseRangeTarget());
            Add(t, "AvatarSdcsNpcController::SdcsTryWorldEntryRearm", Patches_Xnpc.RearmTarget());
            Add(t, "AvatarSdcsNpcController::SdcsChooseMeleeFallback", Patches_Xnpc.MeleeFallbackTarget());
            Add(t, "AvatarSdcsNpcController::Update", Patches_Xnpc.UpdateTarget());
            Add(t, "EntityTrader::AllowActivationCommand", Patches_Xnpc.TalkPromptTarget());
            // round tl1: SCore NPCs that are no trader never take on a trader compound (talk refused while it is closed)
            Add(t, "EntityTrader::OnUpdateLive", Patches_TraderArea.UpdateTarget());
            Add(t, "EntityTrader::OnLockRequestServer", Patches_TraderArea.LockTarget());
            Add(t, "AutoTurretFireController::shouldIgnoreTarget", Patches_Xnpc.AutoTurretTarget());
            Add(t, "MiniTurretFireController::shouldIgnoreTarget", Patches_Xnpc.MiniTurretTarget());
            Add(t, "MotionSensorController::shouldIgnoreTarget", Patches_Xnpc.MotionSensorTarget());
            Add(t, "EntityAliveSDXV4::HandleNavObject", Patches_Xnpc.NavObjectTarget());
            // X12 (M11): the rounds in a gun SCore's weapon swap writes over go back to the bag
            Add(t, "EntityAliveSDXV4::UpdateWeapon(ItemValue,bool)", Patches_Xnpc.UpdateWeaponTarget());
            // X13 (M11 review F1): the held gun is saved after SCore's section (its Write postfix shares the
            // EntityAliveSDXV4::Write entry above) and read back here
            Add(t, "EntityAliveSDXV4::Read", Patches_Xnpc.ReadTarget());
            // X14: no quest turn-in window on an NPC body that has no trader
            Add(t, "QuestJournal::GetNextCompletedQuest", Patches_Xnpc.TurnInTarget());
            // ScStreamerNames.cs (round 30) - a new body of SC's takes a streamer name as it comes into the world; a body read
            // from a save keeps its own (its prefix shares the EntityAliveSDXV4::Read entry above)
            Add(t, "World::SpawnEntityInWorld", ScStreamerNames.SpawnTarget());
            // ScVanity.cs - Vanity Vault paints and tattoos on our bodies after every build (inert without Vanity Vault)
            Add(t, "SDCSUtils::CreateVizTP", Patches_Vanity.Patch_CreateVizTP.Target());
            Add(t, "EModelSDCS::SetVisible", Patches_Vanity.Patch_SetVisible.Target());
            // ScLooks.cs (round MP1) - the server sends a body's look to a client right before the body
            Add(t, "NetEntityDistributionEntry::updatePlayerEntity", Patches_LookNet.Patch_TrackStart.Target());
            // ScAmmo.cs - a remote player's open NPC inventory window holds off the ammo move and medical use out of it
            Add(t, "NetPackageHarvestInventoryRequest::ProcessPackage", ScAmmo.RemoteOpenTarget());
            Add(t, "NetPackageHarvestInventoryUpdate::ProcessPackage", ScAmmo.RemoteCloseTarget());
#if SC_V34
            // Patches_MemberInv.cs (round n10) - a member's inventory on SC's member panel; the panel counts as the host's open
            // loot window for SCore's one-copy-out rule
            Add(t, "EntityUtilities::OpenContainer", Patches_MemberInv.OpenContainerTarget());
            Add(t, "StorageLockUtils::IsOpenInLocalLootWindow", Patches_MemberInv.LocalLootWindowTarget());
            // no Use/Read/Open on a BAG cell: vanilla would run it on the pack's slot of the same number
            Add(t, "XUiC_ItemActionList::SetCraftingActionList", Patches_MemberInv.ActionListTarget());
            // Patches_QuestTitle.cs (round n11b) - a quest with no unique id or a null data value never breaks the player save
            Add(t, "Quest::Write", Patches_ScQuestSave.WriteTarget());
#endif
            // ScAim.cs - the aim pose (visual only)
            Add(t, "EntityAlive::get_AimingGun", AccessTools.PropertyGetter(typeof(EntityAlive), "AimingGun"));
            Add(t, "UAITaskAttackTargetEntitySDX::Start", AccessTools.DeclaredMethod(typeof(UAI.UAITaskAttackTargetEntitySDX), "Start", new[] { typeof(UAI.Context) }));
            Add(t, "UAITaskAttackTargetEntitySDX::Update", AccessTools.DeclaredMethod(typeof(UAI.UAITaskAttackTargetEntitySDX), "Update", new[] { typeof(UAI.Context) }));
            Add(t, "UAITaskAttackTargetEntitySDXV4::Start", ScAim.V4AttackTarget("Start"));
            Add(t, "UAITaskAttackTargetEntitySDXV4::Update", ScAim.V4AttackTarget("Update"));
#if SC_V34
            // ScAim.cs - on 3.4 the back-off task is Voice Control's (UAITaskBackupFromTargetPaced): listed with VC's below
#else
            // ScAim.cs - the gun stays up while SCore's back-off task walks the NPC away from its target (M11 fix 2)
            Add(t, "UAITaskBackupFromTargetSDX::Update", ScAim.BackoffTarget());
#endif
            // ScMelee.cs - SCore's in-range test with the melee weapon's real reach for our bodies (M11 fix 2)
            Add(t, "UAIConsiderationInWeaponRange::GetScore", ScMelee.InWeaponRangeTarget());
            // ScShooting.cs - the Shooting skill aims the gun: hitscan shots and launchers (round sh)
            Add(t, "ItemActionRanged::getDirectionOffset", ScShooting.Patch_ShotDirection.Target());
            Add(t, "ItemActionLauncher::GetActionEffectsValues", ScShooting.Patch_LauncherDirection.Target());
            // ScRelax.cs - a relaxed body's controller writes the bare-hands hold type, and a reload starting raises it (its
            // attack prefixes share the EntityAlive::UseHoldingItem and the two ExecuteAction entries above)
            Add(t, "LegacyAvatarController::_setInt(int)", ScRelax.HoldTypeTarget());
            Add(t, "LegacyAvatarController::StartAnimationReloading", ScRelax.ReloadStartTarget());
            // ScArms.cs - a weapon handed to a member through the weapon menu joins their pair; the replaced one comes back
            Add(t, "DialogActionSwapWeapon::PerformAction", ScArms.DialogSwapTarget());
            // ScLoadout.cs - decision D4 at spawn time: a group's pick of our tiered class above the gamestage tier comes in at that tier
            Add(t, "EntityGroups::GetRandomFromGroupList", ScLoadout.Patch_ClassByGamestage.Target());
            // ScNoise.cs - the base's noise (base defence D1): every vanilla heat event, and the NPC shots vanilla never hears
            Add(t, "AIDirector::NotifyActivity", AccessTools.Method(typeof(AIDirector), "NotifyActivity", new[] { typeof(EnumAIDirectorChunkEvent), typeof(Vector3i), typeof(float), typeof(float) }));
            Add(t, "ItemActionRanged::ItemActionEffects", AccessTools.DeclaredMethod(typeof(ItemActionRanged), "ItemActionEffects", new[] { typeof(GameManager), typeof(ItemActionData), typeof(int), typeof(UnityEngine.Vector3), typeof(UnityEngine.Vector3), typeof(int) }));
            // ScTower.cs - the Guard Tower kit's ghost of the whole tower rides vanilla's placement ghost (visual only)
            Add(t, "RenderDisplacedCube::Update", AccessTools.Method(typeof(RenderDisplacedCube), "Update", new[] { typeof(bool), typeof(World), typeof(WorldRayHitInfo), typeof(Vector3i), typeof(EntityAlive), typeof(PersistentPlayerData), typeof(bool) }));
            // ScFacilities.cs - the facility radial (hold E): wells are plain Block, dew collectors BlockCollector (round wl)
            Add(t, "Block::HasBlockActivationCommands", AccessTools.DeclaredMethod(typeof(Block), "HasBlockActivationCommands", new[] { typeof(WorldBase), typeof(BlockValue), typeof(Vector3i), typeof(EntityAlive) }));
            Add(t, "Block::GetBlockActivationCommands", AccessTools.DeclaredMethod(typeof(Block), "GetBlockActivationCommands", new[] { typeof(WorldBase), typeof(BlockValue), typeof(Vector3i), typeof(EntityAlive) }));
            Add(t, "Block::OnBlockActivated(string)", AccessTools.DeclaredMethod(typeof(Block), "OnBlockActivated", new[] { typeof(string), typeof(WorldBase), typeof(Vector3i), typeof(BlockValue), typeof(EntityPlayerLocal) }));
            Add(t, "Block::GetActivationText", AccessTools.DeclaredMethod(typeof(Block), "GetActivationText", new[] { typeof(WorldBase), typeof(BlockValue), typeof(Vector3i), typeof(EntityAlive) }));
            // ScMothUI.cs (round ui2) - MothUI's dialog statement shows SC's filled line (only when MothUI is loaded)
            MethodBase mothStatement = ScMothUI.StatementTarget();
            if (mothStatement != null) Add(t, "MothUI.XUiC_DialogStatement::GetBindingValueInternal", mothStatement);
            // The land claim model's shader swap (Patches_LandClaimModel). Listed so it joins the
            // MISSING PATCH net and does not report itself as an unlisted patch every startup.
            Add(t, "BlockShapeModelEntity::PoolLoadCallback", AccessTools.DeclaredMethod(typeof(BlockShapeModelEntity), "PoolLoadCallback", new Type[0]));
            Add(t, "BlockCollector::GetBlockActivationCommands", AccessTools.DeclaredMethod(typeof(BlockCollector), "GetBlockActivationCommands", new[] { typeof(WorldBase), typeof(BlockValue), typeof(Vector3i), typeof(EntityAlive) }));
            Add(t, "Block::OnBlockPlaceBefore", AccessTools.DeclaredMethod(typeof(Block), "OnBlockPlaceBefore", new[] { typeof(WorldBase), typeof(BlockPlacement.Result).MakeByRefType(), typeof(EntityAlive), typeof(GameRandom) }));
            Add(t, "BlockCollector::OnBlockActivated(string)", AccessTools.DeclaredMethod(typeof(BlockCollector), "OnBlockActivated", new[] { typeof(string), typeof(WorldBase), typeof(Vector3i), typeof(BlockValue), typeof(EntityPlayerLocal) }));
            // Patches_VoiceControl.cs - members are never billed by 0-XNPCCore 3.2's Voice Control (only when it is loaded)
            if (Patches_VoiceControl.ChatType != null)
            {
                Add(t, "NPCChatComponent::MaybeStampHireDay", Patches_VoiceControl.StampTarget());
                Add(t, "NPCChatComponent::CheckBillingReactor", Patches_VoiceControl.ReactorTarget());
#if SC_V34
                // X1 (Voice Control 3.4.02): no contract seeded (SeedContract, a guard) and no reply to a bill taken, for members
                Add(t, "NPCChatComponent::SeedContract", Patches_VoiceControl.SeedTarget());
                Add(t, "NPCChatComponent::HandleBillingResponse", Patches_VoiceControl.BillingReplyTarget());
                // X2 (Voice Control 3.4.03 takes the money before HandleBillingResponse): nothing deducted for members
                Add(t, "NPCChatComponent::TryDeductPayment", Patches_VoiceControl.DeductTarget());
#endif
                Add(t, "PhraseTriggerHandler::Dismiss", Patches_VoiceControl.DismissTarget());
                Add(t, "PhraseTriggerHandler::Pickup", Patches_VoiceControl.PickupTarget());
                Add(t, "PhraseTriggerHandler::Hire", Patches_VoiceControl.HireTarget());
#if SC_V34
                // round n08: "follow me" by voice to a member who is not following yet (Voice Control 3.4 answers only her leader)
                Add(t, "PhraseTriggerHandler::ExecuteMatch", Patches_VoiceControl.ExecuteMatchTarget());
#endif
                // ScDoors.cs - doors Voice Control's follow opens for a member are shut behind them too
                Add(t, "EntityMovementActuator::TryOpenBlockingDoor", ScDoors.VcDoorTarget());
                // ScArms.cs - Voice Control's "use X" keeps a handed weapon the same way
                Add(t, "VCCommandRouter::MutateSwapWeapon", ScArms.VcSwapTarget());
                // Patches_VoiceControl.cs (V1) - the voice gate: Voice Control keeps quiet on a survivor while SC's talk is open
                // on them or SC's line plays; the player's own words through Voice Control open the answer pass
                Add(t, "NPCAudioPlayer::OnWavBytesReady", Patches_VoiceControl.WavReadyTarget());
                Add(t, "NPCAudioPlayer::PlayClipFile", Patches_VoiceControl.PlayClipFileTarget());
                Add(t, "NPCAudioPlayer::SpeakStreaming", Patches_VoiceControl.SpeakStreamingTarget());
                Add(t, "NetPackageVCVoiceRelay::PlayVoiceLocally", Patches_VoiceControl.RelayTarget());   // NetPackageNPCVoice before Voice Control 3.4.02
                Add(t, "NPCChatComponent::PlayProactiveGreeting", Patches_VoiceControl.GreetingTarget());
                Add(t, "SubtitleManager::AcknowledgeCommand", Patches_VoiceControl.AckTarget());
                Add(t, "NPCChatComponent::ProcessPlayerMessage", Patches_VoiceControl.PlayerMessageTarget());
#if SC_V34
                // round mt1 (part G, H1): Voice Control's interaction hold counts SC's member panel (and its own equipment window)
                Add(t, "NPCChatComponent::LocalHarvestWindowOpenOn", Patches_VoiceControl.HarvestWindowTarget());
#endif
                // Patches_VoiceControl.cs (V2) - SC greets its survivors itself where it has the clip; XNPCCore's say-lines and
                // Voice Control's acks and fixed lines in SC's voices share the AcknowledgeCommand and SpeakStreaming entries above
                Add(t, "NPCChatComponent::TriggerProactiveGreeting", Patches_VoiceControl.TriggerGreetingTarget());
                // round MP4: a say-line Voice Control sends to the clients itself is not relayed again by SC (ScBarks.RelayVc)
                Add(t, "SubtitleManager::SayToEarshot", Patches_VoiceControl.SayToEarshotTarget());
                // Patches_VoiceControl.cs (E8) - Voice Control's LLM chat knows the enclave; a hostile enclave's people refuse
                // the spoken trade
                Add(t, "LLMService::SendStreamingChatRequest", Patches_VoiceControl.ChatRequestTarget());
                Add(t, "PhraseTriggerHandler::OpenTraderInventory", Patches_VoiceControl.TradeTarget());
#if SC_V34
                // 3.4: ScAim - the gun stays up while Voice Control's paced back-off walks the NPC away (M11 fix 2); the Fable
                // review of k1 (F1, F5): ScBackOff.Patch_BoundUpdate prefixes the same method (a bounded back-off's gain test, its
                // own end)
                Add(t, "UAITaskBackupFromTargetPaced::Update", ScAim.BackoffTarget());
                // round k1: ScBackOff - for members at home that back-off goes to a point inside the base (its Start picks it,
                // SCore's GetMoveToLocation takes it in)
                Add(t, "UAITaskBackupFromTargetPaced::Start", ScBackOff.StartTarget());
                Add(t, "SCoreUtils::GetMoveToLocation", ScBackOff.PointTarget());
                // 3.4: SC's leaderless raiders, residents and enclave people fight at range (3.2's MayEngage answer)
                Add(t, "UAIConsiderationMayEngage::GetScore", Patches_VoiceControl.MayEngageTarget());
                // 3.4: ScDoors - the Point formation's doors are shut behind a member too
                Add(t, "EntityMovementActuator::TryOpenDoorAtBlock", ScDoors.VcPointDoorTarget());
                // 3.4 (V1): the dialog's "What are you carrying?" row opens the voice gate's answer pass - only while Voice Control
                // has that row's type (round U1, B3: 0-XNPCCore 3.4.06 removed it; a later version may bring it back)
                if (Patches_VoiceControl.SayCarryingType != null)
                    Add(t, "DialogActionSayCarrying::PerformAction", Patches_VoiceControl.SayCarryingTarget());
                // round U1: 0-XNPCCore 3.4.06's equipment window opens SC's member panel for members; its debug Remove refuses
                // SC's people (Patches_EquipWindow.cs) - each only while Voice Control has that type
                if (ScEquipRedirect.WindowType != null) Add(t, "XUiC_NpcEquipWindow::Open", ScEquipRedirect.OpenTarget());
                if (ScEquipRedirect.AdminRemoveType != null) Add(t, "DialogActionAdminRemove::PerformAction", ScEquipRedirect.AdminRemoveTarget());
                // round U1: Voice Control's own ammo pick on a member becomes her ammo choice in SC's record (ScAmmoChoice.Noted)
                Add(t, "VCCommandRouter::MutateSetAmmo", ScEquipRedirect.SetAmmoTarget());
                // round nc1 (ScArms, 0-XNPCCore 3.4.06 final): the weapon toggle is SC's weapon order (D4); a quiet kill's draw is
                // no order (a Voice Control before 3.4.06 final has neither command: MISSING PATCH, as for any other)
                Add(t, "VCCommandRouter::MutateSetWeaponPref", ScArms.VcWeaponPrefTarget());
                Add(t, "VCCommandRouter::MutateStealthKill", ScArms.VcStealthKillTarget());
                // round 28 (X3): B1 - Voice Control 3.4.04's hold-E ring never picks up SC's people (its pick-up answers Refused, the
                // ring's entry and "Jobs and gear"'s page drop it); the spoken inventory phrase is told apart from the trade
                if (Patches_VoiceControl.HasHoldERing)
                {
                    Add(t, "NpcPickup::TryPickup", Patches_VoiceControl.TryPickupTarget());
                    Add(t, "Entity::UpdateActivationCommands", Patches_VoiceControl.ActivationTarget());
                    Add(t, "HerRingPage::Open", Patches_VoiceControl.HerRingOpenTarget());
                }
                Add(t, "PhraseTriggerHandler::OpenInventoryRouted", Patches_VoiceControl.InventoryRoutedTarget());
                // round 29a (J29): field jobs refused for residents (the job order, the spoken "loot"); squad orders and the order
                // wheel's roster leave residents out
                Add(t, "VCCommandRouter::MutateLoot", ScJobs.MutateLootTarget());   // Voice Control 3.4.06's (npc, arg, leader), or (npc, arg)
                Add(t, "PhraseTriggerHandler::Loot", ScJobs.VoiceLootTarget());
                Add(t, "XNPCVoiceControlMod::GetHiredRoster", ScJobs.RosterTarget());
                Add(t, "VCCommandRouter::ExecuteForSquad", ScJobs.SquadTarget());
                // round m1w (P2): every order Voice Control runs is noted, so the order wheel's spoken slot label becomes the
                // order's reply in SC's recorded voice (ScVoiceRoutes)
                Add(t, "VCCommandRouter::Execute", ScVoiceRoutes.ExecuteTarget());
                // round m1w re-check fix 2: only an order Voice Control ran (Dispatch) and 0-XNPCCore's party cap did not refuse
                // (SdcsParty.Refuse, 3.4.06+) gets SC's reply on the wheel
                Add(t, "VCCommandRouter::Dispatch", ScVoiceRoutes.DispatchTarget());
                if (ScPartyCap.Available) Add(t, "SdcsParty::Refuse", ScVoiceRoutes.PartyRefuseTarget());
#endif
            }
            else ScLog.Out("Voice Control (0-XNPCCore 3.2) not loaded - the member billing guard is idle");
#if SC_V34
            AddNpc306(t);   // rounds U4-U6: 0-XNPCCore 3.4.06's relationships, gift coins and "vc standing" on SC's people (Patches_Relationship)
#endif
            // Patches_Voices.cs (V2) - raider taunts: a new target (spot), the player's death (killed), an investigation given up
            // (lost) or started (search); the hit and the death share the ProcessDamageResponseLocal and SetDead entries above
            Add(t, "EntityAlive::SetAttackTarget", Patches_Voices.SetAttackTargetTarget());
            Add(t, "EntityAlive::AwardKill", Patches_Voices.AwardKillTarget());
            Add(t, "EntityAlive::PlayGiveUpSound", Patches_Voices.GiveUpTarget());
            Add(t, "UAITaskMoveToInvestigate::Start", Patches_Voices.InvestigateTarget());
            // Patches_Outposts.cs - outpost protection and the claim limit
            Add(t, "GameUtils::CheckForAnyPlayerHome", AccessTools.Method(typeof(GameUtils), "CheckForAnyPlayerHome", new[] { typeof(World), typeof(Vector3i), typeof(Vector3i) }));
            Add(t, "PersistentPlayerList::RemoveExtraLandClaims", AccessTools.Method(typeof(PersistentPlayerList), "RemoveExtraLandClaims", new[] { typeof(PersistentPlayerData) }));
            Add(t, "World::CanPlaceLandProtectionBlockAt", AccessTools.Method(typeof(World), "CanPlaceLandProtectionBlockAt", new[] { typeof(Vector3i), typeof(PersistentPlayerData) }));
            Add(t, "BlockToolSelection::ExecuteUseAction", AccessTools.Method(typeof(BlockToolSelection), "ExecuteUseAction", new[] { typeof(ItemInventoryData), typeof(bool), typeof(PlayerActionsLocal) }));
            // Patches_Enclaves.cs - no sleeper zombies in an enclave's POI (E1; its other protections share the outposts' entries)
            Add(t, "SleeperVolume::UpdatePlayerTouched", AccessTools.Method(typeof(SleeperVolume), "UpdatePlayerTouched", new[] { typeof(World), typeof(EntityPlayer) }));
            // E2: an enclave's containers refused while it lives; biome-spawned NPCs kept off enclave grounds (the class check
            // shares ScLoadout's GetRandomFromGroupList entry). 3.4's W2 (ScWildRaiders: Patch_Arrive, Patch_Pass) shares the
            // SpawnUpdate entry; its copy of SpawnUpdate is a reverse patch, which patches nothing
            Add(t, "TEFeatureStorage::OnBlockActivated", Patches_Enclaves.Patch_ContainerLock.Target());
            Add(t, "SpawnManagerBiomes::SpawnUpdate", Patches_Enclaves.Patch_BiomeSpawn.Target());
            Add(t, "World::GetRandomSpawnPositionInAreaMinMaxToPlayers", Patches_Enclaves.Patch_BiomeSpawnSpot.Target());
            // E3: an enclave leader's trade - prices by standing, the stock recorded when the trade window closes
            Add(t, "XUiM_Trader::GetBuyPrice", Patches_Enclaves.Patch_TradeBuyPrice.Target());
            Add(t, "XUiM_Trader::GetSellPrice", Patches_Enclaves.Patch_TradeSellPrice.Target());
            Add(t, "EntityTrader::OnUnlockedServer", Patches_Enclaves.Patch_TradeClosed.Target());
            // E4: the gun pick's rows - their text through the response entry's binding (the shared Text is never written)
            Add(t, "XUiC_DialogResponseEntry::GetBindingValueInternal", Patches_Enclaves.Patch_DialogRowText.Target());
            // round 28 (X3): the talk page's pager (ScDialogPager) - P1's transpiler and P4's stick on Update, P2's wheel on Init,
            // P3's page 1 on OnOpen
            Add(t, "XUiC_DialogResponseList::Update", AccessTools.DeclaredMethod(typeof(XUiC_DialogResponseList), "Update", new[] { typeof(float) }));
            Add(t, "XUiC_DialogResponseList::Init", AccessTools.DeclaredMethod(typeof(XUiC_DialogResponseList), "Init", new Type[0]));
            Add(t, "XUiC_DialogResponseList::OnOpen", AccessTools.DeclaredMethod(typeof(XUiC_DialogResponseList), "OnOpen", new Type[0]));
            // Patches_Infestations.cs - an infested POI's rooms (I1; the count, spawn points and special share the UpdatePlayerTouched
            // entry above, the feral share ScLoadout's GetRandomFromGroupList entry). I2's zombie-cap fix (ScSleeperCap) rides the
            // UpdateSpawn entry (layer A's postfix, layer B's hold) and the UpdatePlayerTouched one (layer C's repair).
            Add(t, "SleeperVolume::UpdateSpawn", AccessTools.Method(typeof(SleeperVolume), "UpdateSpawn", new[] { typeof(World) }));
            Add(t, "SleeperVolume::Spawn", AccessTools.Method(typeof(SleeperVolume), "Spawn", new[] { typeof(World), typeof(int), typeof(int), typeof(BlockSleeper) }));
            Add(t, "SleeperVolume::ClearedUpdate", AccessTools.Method(typeof(SleeperVolume), "ClearedUpdate", new[] { typeof(World) }));
            Add(t, "UAIBase::Update", Patches_Gates.UaiUpdateTarget());     // round MP6 (part G): an NPC's AI in her community's scope
            // round MP7 (part AP): ScParties (PerParty) - an accepted invite brings players in, a leave or a kick takes the one who went
            // out (never the owner), a new leader may act for an absent owner; never the disconnect handler
            Add(t, "Party::ServerHandleAcceptInvite", AccessTools.Method(typeof(Party), "ServerHandleAcceptInvite", new[] { typeof(EntityPlayer), typeof(EntityPlayer) }));
            Add(t, "Party::ServerHandleAutoJoinParty", AccessTools.Method(typeof(Party), "ServerHandleAutoJoinParty", new[] { typeof(EntityPlayer) }));   // AutoParty: as an accept
            Add(t, "Party::ServerHandleLeaveParty", AccessTools.Method(typeof(Party), "ServerHandleLeaveParty", new[] { typeof(EntityPlayer), typeof(int) }));
            Add(t, "Party::ServerHandleKickParty", AccessTools.Method(typeof(Party), "ServerHandleKickParty", new[] { typeof(int) }));
            Add(t, "Party::ServerHandleChangeLead", AccessTools.Method(typeof(Party), "ServerHandleChangeLead", new[] { typeof(EntityPlayer) }));
            return t;
        }

        /// <summary>
        /// Round fd2 (t88): the targets VerifyPatches found patched. Their one-line-each list is DebugLogging's (ScConfig.Verbose),
        /// which is only known once the config is read at world load - LogPatchedTargets writes it then; the summary line and
        /// every MISSING line are written at start-up whatever the setting.
        /// </summary>
        private static readonly List<string> patchedLabels = new List<string>();
        private static bool patchedListed;

        /// <summary>ScConfig, once DebugLogging is read: with it on, the "patched X" line of each target (once per game launch).</summary>
        public static void LogPatchedTargets()
        {
            if (patchedListed || !ScConfig.Verbose) return;
            patchedListed = true;
            for (int i = 0; i < patchedLabels.Count; i++) ScLog.Out("patched " + patchedLabels[i]);
        }

        private static void VerifyPatches(HarmonyLib.Harmony h)
        {
            HashSet<MethodBase> patched;
            try { patched = new HashSet<MethodBase>(h.GetPatchedMethods()); }
            catch (Exception e) { ScLog.Err("could not enumerate patched methods: " + e.Message); return; }

            List<Target> expected = ExpectedTargets();
            int ok = 0, missing = 0;
            for (int i = 0; i < expected.Count; i++)
            {
                Target t = expected[i];
                if (t.Method == null) { missing++; ScLog.Err("MISSING PATCH " + t.Label + " - method not found (signature changed?)"); }
                else if (!patched.Contains(t.Method)) { missing++; ScLog.Err("MISSING PATCH " + t.Label + " - no patch applied"); }
                else { ok++; patchedLabels.Add(t.Label); }      // t88: listed by LogPatchedTargets with DebugLogging
            }
            foreach (MethodBase m in patched)
            {
                bool known = false;
                for (int i = 0; i < expected.Count; i++) if (expected[i].Method == m) { known = true; break; }
                if (!known) ScLog.Warn("patched an unlisted method: " + (m.DeclaringType != null ? m.DeclaringType.Name : "?") + "::" + m.Name);
            }
            if (missing > 0) ScLog.Err("patch verification: " + ok + " ok, " + missing + " MISSING of " + expected.Count);
            else ScLog.Out("patch verification: all " + expected.Count + " targets patched (DebugLogging lists them at world load)");
        }

        private static void LogFeatureHashes()
        {
            try
            {
                ScLog.Out("feature hashes: TEFeatureLandClaim=" + Hex("TEFeatureLandClaim")
                          + " TEFeatureStorage=" + Hex("TEFeatureStorage")
                          + " TEFeatureSCCommunity=" + Hex("TEFeatureSCCommunity")
                          + " TEFeatureSCPacking=" + Hex("TEFeatureSCPacking"));
            }
            catch (Exception e) { ScLog.Err("feature hash logging failed: " + e.Message); }
        }

        private static string Hex(string s) { return "0x" + s.GetStableHashCode().ToString("X8"); }

        /// <summary>Which loaded dialogs got the recruit response (packs whose Hire response id differs are uncovered).</summary>
        private static void ReportDialogCoverage()
        {
            try
            {
                if (Dialog.DialogList == null) return;
                var covered = new List<string>();
                var uncovered = new List<string>();
                foreach (var kv in Dialog.DialogList)
                {
                    Dialog d = kv.Value;
                    if (d == null || d.Responses == null) continue;
                    bool hasHire = false, hasOurs = false;
                    for (int i = 0; i < d.Responses.Count; i++)
                    {
                        DialogResponse r = d.Responses[i];
                        if (r == null) continue;
                        if (r.ID == "Hire") hasHire = true;
                        if (r.ID == "scJoinPoi") hasOurs = true;
                    }
                    if (hasOurs) covered.Add(kv.Key);
                    else if (hasHire) uncovered.Add(kv.Key);
                }
                ScLog.Out("recruit dialog injected into " + covered.Count + " dialog(s): " + string.Join(", ", covered.ToArray())
                          + (uncovered.Count > 0 ? " - NOT injected (Hire response present but no scJoinPoi): " + string.Join(", ", uncovered.ToArray()) : ""));
            }
            catch (Exception e)
            {
                ScLog.Once("dialog-coverage", e);
            }
        }
    }
}
