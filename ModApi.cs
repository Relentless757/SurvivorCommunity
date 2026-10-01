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
                ScHomeLink.Reset();
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
                ScAim.Reset();
                ScLoadout.Reset();
                ScMedic.Reset();
                Patches_VoiceControl.Reset();
                });
                ModEvents.GameStartDone.RegisterHandler((ref ModEvents.SGameStartDoneData _d) =>
                {
                    try { ScConfig.EnsureParsed(); ReportDialogCoverage(); }
                    catch (Exception e) { ScLog.Once("gamestartdone", e); }
                });
                ModEvents.GameUpdate.RegisterHandler((ref ModEvents.SGameUpdateData _d) => ScTicker.OnGameUpdate());
                ModEvents.WorldShuttingDown.RegisterHandler((ref ModEvents.SWorldShuttingDownData _d) =>
                {
                    ScRunners.OnWorldShuttingDown();   // first: runners in the world go back to their snapshot before the world is saved
                    ScPortraits.Reset(); ScHomeLink.Reset(); ScRegistry.FlushAndReset();
                });
                ModEvents.GameShutdown.RegisterHandler((ref ModEvents.SGameShutdownData _d) => { ScPortraits.Reset(); ScRegistry.FlushAndReset(); });
                ModEvents.EntityKilled.RegisterHandler((ref ModEvents.SEntityKilledData _d) =>
                {
                    if (ScCommunity.IsHost && ScRegistry.Loaded) ScSkills.OnEntityKilled(_d.KilledEntitiy, _d.KillingEntity);
                });

                // The only event SCore raises. Hiring a member through the vanilla popup should be impossible
                // (the response is hidden for members) - if it happens anyway, the member is simply following.
                EntityUtilities.OnHire += (hiredId, ownerId) =>
                {
                    try
                    {
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
            Add(t, "EntityAliveSDX::SetDead", AccessTools.Method(typeof(EntityAliveSDX), "SetDead", new Type[0]));
            Add(t, "EntityAliveSDX::PostInit", AccessTools.Method(typeof(EntityAliveSDX), "PostInit", new Type[0]));
            Add(t, "EntityAliveSDX::IsSavedToFile", AccessTools.DeclaredMethod(typeof(EntityAliveSDX), "IsSavedToFile", new Type[0]));
            // ScDoors.cs - members open (and shut) doors
            Add(t, "EntityMoveHelper::CheckForDoorAndOpen", AccessTools.Method(typeof(EntityMoveHelper), "CheckForDoorAndOpen", new Type[0]));
            Add(t, "EntityNPCBandit::PostInit", AccessTools.Method(typeof(EntityNPCBandit), "PostInit", new Type[0]));
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
            // Patches_Sdcs.cs - survivors wearing the player's body
            Add(t, "EModelSDCS::Init", AccessTools.Method(typeof(EModelSDCS), "Init", new[] { typeof(World), typeof(Entity), typeof(EModelInstanceAssets) }));
            Add(t, "EModelSDCS::GenerateMeshes", AccessTools.Method(typeof(EModelSDCS), "GenerateMeshes", new Type[0]));
            Add(t, "EModelSDCS::SwitchModelAndView", AccessTools.Method(typeof(EModelSDCS), "SwitchModelAndView", new[] { typeof(bool), typeof(bool) }));
            Add(t, "EModelSDCS::XUiM_PlayerEquipment_HandleRefreshEquipment", AccessTools.Method(typeof(EModelSDCS), "XUiM_PlayerEquipment_HandleRefreshEquipment", new[] { typeof(XUiM_PlayerEquipment) }));
            Add(t, "EModelSDCS::OnDestroy", AccessTools.DeclaredMethod(typeof(EModelSDCS), "OnDestroy", new Type[0]));
            Add(t, "AvatarSDCSController::SwitchModelAndView", AccessTools.Method(typeof(AvatarSDCSController), "SwitchModelAndView", new[] { typeof(string), typeof(bool), typeof(bool) }));
            Add(t, "EModelPlayer::SetSkinTexture", AccessTools.Method(typeof(EModelPlayer), "SetSkinTexture", new[] { typeof(string) }));
            Add(t, "AvatarController::_setInt", AccessTools.Method(typeof(AvatarController), "_setInt", new[] { typeof(int), typeof(int), typeof(bool) }));
            Add(t, "AvatarSDCSController::Update", AccessTools.DeclaredMethod(typeof(AvatarSDCSController), "Update", new Type[0]));
            Add(t, "CharacterGazeController::LateUpdate", AccessTools.DeclaredMethod(typeof(CharacterGazeController), "LateUpdate", new Type[0]));
            Add(t, "LegacyAvatarController::IsAnimationWithMotionRunning", AccessTools.DeclaredMethod(typeof(LegacyAvatarController), "IsAnimationWithMotionRunning", new Type[0]));
            Add(t, "LegacyAvatarController::StartAnimationJump", AccessTools.DeclaredMethod(typeof(LegacyAvatarController), "StartAnimationJump", new[] { typeof(AnimJumpMode) }));
            Add(t, "EntityAlive::SetMoveForwardWithModifiers", AccessTools.Method(typeof(EntityAlive), "SetMoveForwardWithModifiers", new[] { typeof(float), typeof(float), typeof(float), typeof(bool) }));
            Add(t, "EntityTargetingUtilities::CanTakeDamage", AccessTools.Method(typeof(EntityTargetingUtilities), "CanTakeDamage", new[] { typeof(EntityAlive), typeof(Entity) }));
            Add(t, "Entity::PhysicsSetHeight", AccessTools.Method(typeof(Entity), "PhysicsSetHeight", new[] { typeof(float) }));
            Add(t, "EModelBase::StartRagdoll", AccessTools.Method(typeof(EModelBase), "StartRagdoll", new[] { typeof(float) }));
            Add(t, "EModelBase::FrameUpdateRagdoll", AccessTools.Method(typeof(EModelBase), "FrameUpdateRagdoll", new Type[0]));
            Add(t, "EntityAlive::OnUpdateLive", AccessTools.Method(typeof(EntityAlive), "OnUpdateLive", new Type[0]));
            Add(t, "EntityAlive::InitPostCommon", AccessTools.Method(typeof(EntityAlive), "InitPostCommon", new Type[0]));
            Add(t, "EntityAlive::RotateTo(xyz)", AccessTools.Method(typeof(EntityAlive), "RotateTo", new[] { typeof(float), typeof(float), typeof(float), typeof(float), typeof(float) }));
            Add(t, "EntityAlive::RotateTo(entity)", AccessTools.Method(typeof(EntityAlive), "RotateTo", new[] { typeof(Entity), typeof(float), typeof(float) }));
            Add(t, "AnimatorMeleeAttackState::OnStateEnter", AccessTools.Method(typeof(AnimatorMeleeAttackState), "OnStateEnter", new[] { typeof(UnityEngine.Animator), typeof(UnityEngine.AnimatorStateInfo), typeof(int) }));
            Add(t, "EntityBuffs::AddBuff", AccessTools.Method(typeof(EntityBuffs), "AddBuff", new[] { typeof(string), typeof(Vector3i), typeof(int), typeof(bool), typeof(bool), typeof(float) }));
            Add(t, "EntityAlive::UseHoldingItem", AccessTools.Method(typeof(EntityAlive), "UseHoldingItem", new[] { typeof(int), typeof(bool) }));
            Add(t, "EntityAliveSDX::ExecuteAction", AccessTools.Method(typeof(EntityAliveSDX), "ExecuteAction", new[] { typeof(bool), typeof(int) }));
            Add(t, "EntityAliveSDX::Write", AccessTools.Method(typeof(EntityAliveSDX), "Write", new[] { typeof(System.IO.BinaryWriter), typeof(bool) }));
            // ScAim.cs - the aim pose (visual only)
            Add(t, "EntityAlive::get_AimingGun", AccessTools.PropertyGetter(typeof(EntityAlive), "AimingGun"));
            Add(t, "UAITaskAttackTargetEntitySDX::Start", AccessTools.DeclaredMethod(typeof(UAI.UAITaskAttackTargetEntitySDX), "Start", new[] { typeof(UAI.Context) }));
            Add(t, "UAITaskAttackTargetEntitySDX::Update", AccessTools.DeclaredMethod(typeof(UAI.UAITaskAttackTargetEntitySDX), "Update", new[] { typeof(UAI.Context) }));
            // Patches_VoiceControl.cs - members are never billed by 0-XNPCCore 3.2's Voice Control (only when it is loaded)
            if (Patches_VoiceControl.ChatType != null)
            {
                Add(t, "NPCChatComponent::MaybeStampHireDay", Patches_VoiceControl.StampTarget());
                Add(t, "NPCChatComponent::CheckBillingReactor", Patches_VoiceControl.ReactorTarget());
                Add(t, "PhraseTriggerHandler::Dismiss", Patches_VoiceControl.DismissTarget());
                Add(t, "PhraseTriggerHandler::Pickup", Patches_VoiceControl.PickupTarget());
                Add(t, "PhraseTriggerHandler::Hire", Patches_VoiceControl.HireTarget());
            }
            else ScLog.Out("Voice Control (0-XNPCCore 3.2) not loaded - the member billing guard is idle");
            // Patches_Outposts.cs - outpost protection and the claim limit
            Add(t, "GameUtils::CheckForAnyPlayerHome", AccessTools.Method(typeof(GameUtils), "CheckForAnyPlayerHome", new[] { typeof(World), typeof(Vector3i), typeof(Vector3i) }));
            Add(t, "PersistentPlayerList::RemoveExtraLandClaims", AccessTools.Method(typeof(PersistentPlayerList), "RemoveExtraLandClaims", new[] { typeof(PersistentPlayerData) }));
            Add(t, "World::CanPlaceLandProtectionBlockAt", AccessTools.Method(typeof(World), "CanPlaceLandProtectionBlockAt", new[] { typeof(Vector3i), typeof(PersistentPlayerData) }));
            Add(t, "BlockToolSelection::ExecuteUseAction", AccessTools.Method(typeof(BlockToolSelection), "ExecuteUseAction", new[] { typeof(ItemInventoryData), typeof(bool), typeof(PlayerActionsLocal) }));
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
