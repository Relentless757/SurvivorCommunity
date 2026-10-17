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
                h.PatchAll(Assembly.GetExecutingAssembly());
                VerifyPatches(h);
                // the overwhelmed rule reads 0-XNPCCore's private close-range state: bound now, so a rename shows at startup
                if (ScArms.Bind()) ScLog.Out("overwhelmed rule: 0-XNPCCore's close-range swap state bound");
                LogFeatureHashes();

                ModEvents.GameStarting.RegisterHandler((ref ModEvents.SGameStartingData _d) =>
                {
                    ScConfig.Reset();
                    ScLog.ResetForNewWorld();
                    ScTicker.Reset();
                    ScZone.Reset();
                    ScTraits.Reset();
                ScSkills.Reset();
                ScRadio.Reset();
                ScOutposts.Reset();
                ScEnclaves.Reset();
                ScEnclavePeople.Reset();
                ScEnclaveDeals.Reset();
                ScEncRequests.Reset();
                ScInfestations.Reset();
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
                ScCategories.Reset();
                Patches_Npc.Reset();
                });
                ModEvents.GameStartDone.RegisterHandler((ref ModEvents.SGameStartDoneData _d) =>
                {
                    try { ScConfig.EnsureParsed(); ReportDialogCoverage(); }
                    catch (Exception e) { ScLog.Once("gamestartdone", e); }
                    // the crafted Watch Post (a shape helper) shows the post's own description
                    ScWatch.DescribeHelper();
                    // the well kit places Level 1 only; levels 2-5 are upgrades (round wl). Every machine: placement is local.
                    ScFacilities.RestrictWellKit();
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
                });
                ModEvents.GameUpdate.RegisterHandler((ref ModEvents.SGameUpdateData _d) =>
                {
                    ScTicker.OnGameUpdate();
                    ScWeaponWarm.Update();             // NPC weapon models in the background (round wp)
                });
                ModEvents.WorldShuttingDown.RegisterHandler((ref ModEvents.SWorldShuttingDownData _d) =>
                {
                    ScRunners.OnWorldShuttingDown();   // first: runners in the world go back to their snapshot before the world is saved
                    ScEnclavePeople.OnWorldShuttingDown();   // enclave bodies recorded and taken out before the save (E2)
                    // a dead NPC's window still held by a remote player drops at the body now (the world is saved after this event)
                    if (ScCommunity.IsHost) Patches_Npc.Patch_SetDead.ReleaseAll(true);
                    ScPortraits.Reset(); ScHomeLink.Reset(); ScHomeHold.Reset(); ScWeaponWarm.Reset(); ScRegistry.FlushAndReset();
                });
                ModEvents.GameShutdown.RegisterHandler((ref ModEvents.SGameShutdownData _d) => { ScPortraits.Reset(); ScRegistry.FlushAndReset(); });
                ModEvents.EntityKilled.RegisterHandler((ref ModEvents.SEntityKilledData _d) =>
                {
                    if (ScCommunity.IsHost && ScRegistry.Loaded) ScSkills.OnEntityKilled(_d.KilledEntitiy, _d.KillingEntity);
                });
                // a player who leaves with an NPC's inventory window open never sends its close (ScAmmo's remote-window guard)
                ModEvents.PlayerDisconnected.RegisterHandler((ref ModEvents.SPlayerDisconnectedData _d) =>
                {
                    try { if (_d.ClientInfo != null) ScAmmo.ForgetPlayer(_d.ClientInfo.entityId); }
                    catch (Exception e) { ScLog.Once("player-disconnected", e); }
                });

                // The only event SCore raises. Hiring a member through the vanilla popup should be impossible
                // (the response is hidden for members) - if it happens anyway, the member is simply following.
                EntityUtilities.OnHire += (hiredId, ownerId) =>
                {
                    try
                    {
                        // a hire ends a grudge, as joining does (ScTrust): suspended, it would come back on dismissal
                        EntityAlive hired = ScCommunity.World != null ? ScCommunity.World.GetEntity(hiredId) as EntityAlive : null;
                        if (hired != null) ScTrust.StripOnJoin(hired);
                        Member m = ScRegistry.GetMember(hiredId);
                        if (m != null && m.Status == MemberStatus.Resident)
                        {
                            m.Status = MemberStatus.Following;
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

        private struct Target
        {
            public string Label;
            public MethodBase Method;
        }

        private static void Add(List<Target> list, string label, MethodBase m)
        {
            list.Add(new Target { Label = label, Method = m });
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
            Add(t, "Quest::StartQuest", AccessTools.Method(typeof(Quest), "StartQuest", new[] { typeof(bool), typeof(bool) }));
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
            Add(t, "Entity::PhysicsSetHeight", AccessTools.Method(typeof(Entity), "PhysicsSetHeight", new[] { typeof(float) }));
            Add(t, "EModelBase::StartRagdoll", AccessTools.Method(typeof(EModelBase), "StartRagdoll", new[] { typeof(float) }));
            Add(t, "EModelBase::FrameUpdateRagdoll", AccessTools.Method(typeof(EModelBase), "FrameUpdateRagdoll", new Type[0]));
            Add(t, "EntityAlive::OnUpdateLive", AccessTools.Method(typeof(EntityAlive), "OnUpdateLive", new Type[0]));
            Add(t, "EntityBuffs::AddBuff", AccessTools.Method(typeof(EntityBuffs), "AddBuff", new[] { typeof(string), typeof(Vector3i), typeof(int), typeof(bool), typeof(bool), typeof(float) }));
            Add(t, "EntityAlive::UseHoldingItem", AccessTools.Method(typeof(EntityAlive), "UseHoldingItem", new[] { typeof(int), typeof(bool) }));
            Add(t, "EntityAliveSDX::ExecuteAction", AccessTools.Method(typeof(EntityAliveSDX), "ExecuteAction", new[] { typeof(bool), typeof(int) }));
            Add(t, "EntityAliveSDX::Write", AccessTools.Method(typeof(EntityAliveSDX), "Write", new[] { typeof(System.IO.BinaryWriter), typeof(bool) }));
            Add(t, "EntityAliveSDXV4::ExecuteAction", AccessTools.DeclaredMethod(typeof(EntityAliveSDXV4), "ExecuteAction", new[] { typeof(bool), typeof(int) }));
            Add(t, "EntityAliveSDXV4::Write", AccessTools.DeclaredMethod(typeof(EntityAliveSDXV4), "Write", new[] { typeof(System.IO.BinaryWriter), typeof(bool) }));
            Add(t, "EntityAliveSDXV4::SetModelLayer", Patches_Xnpc.Patch_ModelLayer.Target());
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
            // ScVanity.cs - Vanity Vault paints and tattoos on our bodies after every build (inert without Vanity Vault)
            Add(t, "SDCSUtils::CreateVizTP", Patches_Vanity.Patch_CreateVizTP.Target());
            Add(t, "EModelSDCS::SetVisible", Patches_Vanity.Patch_SetVisible.Target());
            // ScAmmo.cs - a remote player's open NPC inventory window holds off the ammo move and medical use out of it
            Add(t, "NetPackageHarvestInventoryRequest::ProcessPackage", ScAmmo.RemoteOpenTarget());
            Add(t, "NetPackageHarvestInventoryUpdate::ProcessPackage", ScAmmo.RemoteCloseTarget());
            // ScAim.cs - the aim pose (visual only)
            Add(t, "EntityAlive::get_AimingGun", AccessTools.PropertyGetter(typeof(EntityAlive), "AimingGun"));
            Add(t, "UAITaskAttackTargetEntitySDX::Start", AccessTools.DeclaredMethod(typeof(UAI.UAITaskAttackTargetEntitySDX), "Start", new[] { typeof(UAI.Context) }));
            Add(t, "UAITaskAttackTargetEntitySDX::Update", AccessTools.DeclaredMethod(typeof(UAI.UAITaskAttackTargetEntitySDX), "Update", new[] { typeof(UAI.Context) }));
            Add(t, "UAITaskAttackTargetEntitySDXV4::Start", ScAim.V4AttackTarget("Start"));
            Add(t, "UAITaskAttackTargetEntitySDXV4::Update", ScAim.V4AttackTarget("Update"));
            // ScAim.cs - the gun stays up while SCore's back-off task walks the NPC away from its target (M11 fix 2)
            Add(t, "UAITaskBackupFromTargetSDX::Update", ScAim.BackoffTarget());
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
            Add(t, "BlockCollector::GetBlockActivationCommands", AccessTools.DeclaredMethod(typeof(BlockCollector), "GetBlockActivationCommands", new[] { typeof(WorldBase), typeof(BlockValue), typeof(Vector3i), typeof(EntityAlive) }));
            Add(t, "Block::OnBlockPlaceBefore", AccessTools.DeclaredMethod(typeof(Block), "OnBlockPlaceBefore", new[] { typeof(WorldBase), typeof(BlockPlacement.Result).MakeByRefType(), typeof(EntityAlive), typeof(GameRandom) }));
            Add(t, "BlockCollector::OnBlockActivated(string)", AccessTools.DeclaredMethod(typeof(BlockCollector), "OnBlockActivated", new[] { typeof(string), typeof(WorldBase), typeof(Vector3i), typeof(BlockValue), typeof(EntityPlayerLocal) }));
            // Patches_VoiceControl.cs - members are never billed by 0-XNPCCore 3.2's Voice Control (only when it is loaded)
            if (Patches_VoiceControl.ChatType != null)
            {
                Add(t, "NPCChatComponent::MaybeStampHireDay", Patches_VoiceControl.StampTarget());
                Add(t, "NPCChatComponent::CheckBillingReactor", Patches_VoiceControl.ReactorTarget());
                Add(t, "PhraseTriggerHandler::Dismiss", Patches_VoiceControl.DismissTarget());
                Add(t, "PhraseTriggerHandler::Pickup", Patches_VoiceControl.PickupTarget());
                Add(t, "PhraseTriggerHandler::Hire", Patches_VoiceControl.HireTarget());
                // ScDoors.cs - doors Voice Control's follow opens for a member are shut behind them too
                Add(t, "EntityMovementActuator::TryOpenBlockingDoor", ScDoors.VcDoorTarget());
                // ScArms.cs - Voice Control's "use X" keeps a handed weapon the same way
                Add(t, "VCCommandRouter::MutateSwapWeapon", ScArms.VcSwapTarget());
            }
            else ScLog.Out("Voice Control (0-XNPCCore 3.2) not loaded - the member billing guard is idle");
            // Patches_Outposts.cs - outpost protection and the claim limit
            Add(t, "GameUtils::CheckForAnyPlayerHome", AccessTools.Method(typeof(GameUtils), "CheckForAnyPlayerHome", new[] { typeof(World), typeof(Vector3i), typeof(Vector3i) }));
            Add(t, "PersistentPlayerList::RemoveExtraLandClaims", AccessTools.Method(typeof(PersistentPlayerList), "RemoveExtraLandClaims", new[] { typeof(PersistentPlayerData) }));
            Add(t, "World::CanPlaceLandProtectionBlockAt", AccessTools.Method(typeof(World), "CanPlaceLandProtectionBlockAt", new[] { typeof(Vector3i), typeof(PersistentPlayerData) }));
            Add(t, "BlockToolSelection::ExecuteUseAction", AccessTools.Method(typeof(BlockToolSelection), "ExecuteUseAction", new[] { typeof(ItemInventoryData), typeof(bool), typeof(PlayerActionsLocal) }));
            // Patches_Enclaves.cs - no sleeper zombies in an enclave's POI (E1; its other protections share the outposts' entries)
            Add(t, "SleeperVolume::UpdatePlayerTouched", AccessTools.Method(typeof(SleeperVolume), "UpdatePlayerTouched", new[] { typeof(World), typeof(EntityPlayer) }));
            // E2: an enclave's containers refused while it lives; biome-spawned NPCs kept off enclave grounds (the class check
            // shares ScLoadout's GetRandomFromGroupList entry)
            Add(t, "TEFeatureStorage::OnBlockActivated", Patches_Enclaves.Patch_ContainerLock.Target());
            Add(t, "SpawnManagerBiomes::SpawnUpdate", Patches_Enclaves.Patch_BiomeSpawn.Target());
            Add(t, "World::GetRandomSpawnPositionInAreaMinMaxToPlayers", Patches_Enclaves.Patch_BiomeSpawnSpot.Target());
            // E3: an enclave leader's trade - prices by standing, the stock recorded when the trade window closes
            Add(t, "XUiM_Trader::GetBuyPrice", Patches_Enclaves.Patch_TradeBuyPrice.Target());
            Add(t, "XUiM_Trader::GetSellPrice", Patches_Enclaves.Patch_TradeSellPrice.Target());
            Add(t, "EntityTrader::OnUnlockedServer", Patches_Enclaves.Patch_TradeClosed.Target());
            // E4: the gun pick's rows - their text through the response entry's binding (the shared Text is never written)
            Add(t, "XUiC_DialogResponseEntry::GetBindingValueInternal", Patches_Enclaves.Patch_DialogRowText.Target());
            // Patches_Infestations.cs - an infested POI's rooms (I1; the count, spawn points and special share the UpdatePlayerTouched
            // entry above, the feral share ScLoadout's GetRandomFromGroupList entry)
            Add(t, "SleeperVolume::UpdateSpawn", AccessTools.Method(typeof(SleeperVolume), "UpdateSpawn", new[] { typeof(World) }));
            Add(t, "SleeperVolume::Spawn", AccessTools.Method(typeof(SleeperVolume), "Spawn", new[] { typeof(World), typeof(int), typeof(int), typeof(BlockSleeper) }));
            Add(t, "SleeperVolume::ClearedUpdate", AccessTools.Method(typeof(SleeperVolume), "ClearedUpdate", new[] { typeof(World) }));
            return t;
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
                else { ok++; ScLog.Out("patched " + t.Label); }
            }
            foreach (MethodBase m in patched)
            {
                bool known = false;
                for (int i = 0; i < expected.Count; i++) if (expected[i].Method == m) { known = true; break; }
                if (!known) ScLog.Warn("patched an unlisted method: " + (m.DeclaringType != null ? m.DeclaringType.Name : "?") + "::" + m.Name);
            }
            if (missing > 0) ScLog.Err("patch verification: " + ok + " ok, " + missing + " MISSING of " + expected.Count);
            else ScLog.Out("patch verification: all " + expected.Count + " targets patched");
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
