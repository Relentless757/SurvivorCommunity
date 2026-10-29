# SurvivorCommunity → 7 Days To Die 3.4 — port plan

Started 2026-10-29. Target: game **3.4** (b6 in `F:\7 Days to Die Version Control\3.4`) with **0-SCore 3.4.3.826**,
**0-XNPCCore 3.4.01** and **1-XNPCVoiceControl-Complete 3.4.01**. 3.3 is not a target: XNPCCore 3.4 needs game 3.4 and
SCore 3.4.

Research (evidence, decompiles, tools) in `F:\7daystodie mods 2026\Port33\research\sc34\`:
`game_api\REPORT.md`, `score\REPORT.md`, `xnpc\REPORT.md`, `xml\REPORT.md`. NPCCore 3.3.04 source:
`research\sc34\src\NPCCore`.

## Ground rules (as in the Close Quarters, Survivor Origins and RE Inventory ports)

- **One source, two builds.** `-p:ScGame=34` compiles against the 3.4 install with `SC_V34` defined; the default build
  stays the 3.2 one and its DLL stays **byte-identical** to the shipping `81f6c2da`. A 3.4 build is written outside any
  Mods folder until it is deployed to the 3.4 install on the usual routine (game closed, backup, md5/IL check).
- Work on branch `port-3.4` in the worktree `F:\7daystodie mods 2026\Port33\SurvivorCommunity`; `main` and the live 3.2
  mod are never touched. Merge to `main` once the 3.2 build is proven unchanged.
- Fable review before every deploy; test steps in `TESTING.md` and the task list (t15).
- The saves folder is shared between 3.2 and 3.4 and 3.4 converts a loaded save: test on a new world or a copy.

## What breaks (summary of the four reports)

| Area | Size | Worst item |
|---|---|---|
| Vanilla API | 95 compile errors from 26 API changes; 3 of 80 vanilla patch pairs | `Quest.StartQuest` target gone → PatchAll class #1 throws → **the whole mod is inert** |
| SCore | 1 signature, 1 rebased type, 3 serializer patch targets gone | NPC guns lose their rounds on every save/load (silent) |
| XNPCCore / VC | 0 binding breaks; 25 behaviour issues | leaderless NPCs (raiders, residents, enclave people) stop firing at range; XNPC self-heal burns SC's bandages |
| XML | 136/136 ops match | XNPC's new AI packages bury SC's resident ladder (Guard 3) |

## Stage 1 — compiles, every patch applies (no design choices)

1. Build switch: `ScGame=34` → `SC_V34`, GameDir = 3.4 install, OutputPath guard (no Mods folder).
2. **Quests:** `StartQuest(Quest.QuestSource, bool, bool)` in `Patches_QuestTitle.cs` (3 sites) + `ModApi.cs:246`;
   `InternalToParametersDictionary` on the 7 objectives; `AddQuest(q, Quest.QuestSource.QuestSystem)`.
3. **PatchAll hardening:** patch class by class with a per-class try/catch and a count; ModEvents/OnHire registration
   outside the patch failure path; `[HarmonyOptional]` where a target may legitimately be missing.
4. **Serializers:** retarget X13 held-gun save/load and ScMedic mid-use save to `Write(PooledBinaryWriter,
   StreamModeWrite)` / `Read(byte, PooledBinaryReader, StreamModeRead)`; `bNetworkWrite = mode != Persistency`.
5. **Storage:** `TEFeatureSCCommunity`/`TEFeatureSCPacking` stream modes; `ScStockpile`/`ScStorage` off
   `ITileEntityLootable`; **ScLinkedContainer rebuilt on `SCoreLootContainer`** (TEFeatureStorage) with the write-through
   on `ItemGrid.SlotChanged`; `OpenContainer`/`OpenLooting(title, te, false)`; RE Inventory's
   `zzzz_GridInv_SurvivorCommunity` delegate retyped (separate mod — its own port step).
6. **Inventory:** the mechanical table in `game_api\REPORT.md` §10 (ItemGrid, SetSlot, SlotCount, ModificationCount,
   EqualsExceptUseTimesAndMeta, Equipment grid, Pooled* blobs, `EntityCreationData.bagData`); delete
   `Inventory.OnUpdate`/`ForceHoldingItemUpdate`; never replace live grid slots (`ScAmmo.cs:516,567`); clone captured
   `ItemValue`s before a slot can empty (`Patches_Xnpc.cs:1038-1063`, `ScMedic.cs:225-229`).
7. **NPC toolbelt:** 3.4 gives `EntityAlive` a 1-slot inventory. Prefix `EntityAlive.InitInventory` so SC's bodies keep
   3.2's 10 slots (ScLoadout's weapon pair, raider drops, ScArms copies).
8. **SCore window protocol:** mark a remote open only when the session is really ours (`HasRemoteSession &&
   !IsOpenByOther`); `Parent?.NotifyListeners()`; handle `HarvestManager.AddItem`'s leftover.
9. **XNPC fixes that are not choices:** `MayEngage` (raiders, residents, enclave people fire at range again — SC's
   defence needs it); block XNPC's `SdcsSelfHealScan` on SC bodies (it spends the item first; D2); X14 ignores XNPC's
   stock trader 64 for turn-ins; X12 skips inside XNPC's own `SdcsEquipByName`; `XnpcModelY` -0.08; ScAim's back-off
   postfix retargeted to `UAITaskBackupFromTargetPaced`; dialogs member-hide xpath + `Patrol Menu`; SetDead
   `__runOriginal`; `SdcsLevelHarvest` in `LevelCvars`; Point-formation door target; Detach clears XNPC job/medic/vehicle
   cvars and unseats.
10. Gate: 0 errors on 3.4; 3.2 DLL byte-identical; HarmonyAudit on the 3.4 DLL — every class applies.

## Stage 2 — XNPC 3.4 behaviour on SC's people (needs the decisions below)

Per the decisions: re-rank SC's UAI ladder (residents, enclave people, travel, defence) above XNPC 3.4's bands where
SC's role must win, without blocking XNPC's medic/jobs/leave-area (D1); ScMedic treats bleeding (D2); X9 allows XNPC's
stock trade (D4); creator faces never overwrite a stored look (D4); a `SleeperVolume.TouchGroup` guard keeps enclave
buildings clear (D4); dialog weapon rows (D6); ScRelax vs VC's chat holster (D8). Then Stage 3: Fable review, deploy
to the 3.4 install, in-game tests.

## Decisions

| # | Question | Recommended default | Answer |
|---|---|---|---|
| D1 | XNPC 3.4's AI on SC's people (medic, jobs, wild loot, leave-area, Guard 3) | SC's 3.2 behaviour: SC's survivors get the 3.2 package set back | **Everyone** (user 2026-10-29): members, prospects, enclave people and strangers all run XNPC 3.4's packages; SC's actions are re-ranked around them (residents may leave posts to heal; members take job orders) |
| D2 | Bleeding on SC's bodies | immune again (as in 3.2) | **They bleed; SC's medic treats it** (self). XNPC's self-heal scan stays blocked on SC bodies (it spends the item before SC's refusal); XNPC's party medic (healing others) runs per D1 |
| D3 | Gun cadence on SC raiders and survivors | restore 3.2's one shot a second | **XNPC's rate for everyone** (no change: inherit `AttackTimeout` 0.05 + bursts) |
| D4 | XNPC extras on SC's people: stock trade, bag seeding (stock + water), character-creator faces, sleeper triggering | none | **All four.** Turn-ins stay hidden at survivors (X14 ignores trader 64); creator faces only on fresh bodies — never over a look SC stores and restores (members, enclave people, prospects with a look); sleeper triggering everywhere except enclave buildings, which stay sleeper-free (E1) |
| D5 | SCore's companion-window Dismiss on a member | reroute to SC's Go Home | (default) |
| D6 | "Weapon Classes" overflows the 10-row list | drop SC's `scUseHanded` on 3.4 (XNPC's Carried Weapons does it) | (default) |
| D7 | Rounds on an XNPC swap | keep SC's X12, skip it inside XNPC's own swaps | (default) |
| D8 | ScRelax vs VC's chat hold/holster | keep ScRelax; accept VC's face-and-holster while a window is open | (default) |
| D9 | SCore `blocks.xml` toggles you set on 3.2 (`ReadFromContainers`, `OneBlockCrouch` true) | yours to set in the 3.4 install's SCore | (yours) |
| D10 | Vanilla 3.4's new armour sets and backpacks in SC's outfit tables | later, after the port runs | (default) |

## Status

- 2026-10-29: research done; worktree and branch set up; this plan.
- 2026-10-29: **Stage 1 built, not run.** 3.4 build 0 errors (DLL `1776943b`, the same on a clean rebuild); the default
  3.2 build still `81f6c2da` byte for byte; HarmonyAudit on the 3.4 DLL: 148/148 patch rows bind, 0 parameter issues;
  XML replay 136/136 SC ops match on 3.2 and 3.4 (the member-hide op 39 hits on both). Build:
  `dotnet build -c Release --no-dependencies -p:ScGame=34 "-p:OutputPath=F:\7daystodie mods 2026\Port33\build\sc34\"`
  (`obj34\` via `Directory.Build.props`; `ScGameCheck` refuses a Mods-folder or in-place 3.4 output). Scratch, audit and
  replay output in `research\sc34\stage1\`.

### Stage 1: how it was done where the plan left a choice

- **Toolbelt:** `EntityAlive.InitInventory` runs in Awake, before the entity knows its class, so a `[ThreadStatic]` set
  around `EntityFactory.CreateEntityOperation.CompleteEntity` names the class being built; SC's managed classes (6
  npcSdcsSurvivor + 6 scRaider, `EntityAliveSDXV4`) get 10 slots, every other NPC keeps 3.4's one.
- **MayEngage:** a postfix on Voice Control's `UAIConsiderationMayEngage.GetScore`: raiders, and members / stand-ins /
  enclave people with no leader or owner, score 1 (3.2's answer); strangers, prospects and followers keep 3.4's rules.
- **XnpcModelY** keeps its meaning (0 = feet on the ground): the parent goes to `XnpcModelY` minus XNPC's
  `SdcsModelYOffset` (read by reflection, 0.08 fallback).
- **Linked storage:** `ScLinkedContainer34.cs : SCoreLootContainer`; the write-through is an idempotent mirror on the view
  grid's `SlotChanged`, plus prefixes on `TEFeatureStorage.AddItem` / `TryStackItem` for this class only (shift-click into
  full storage moves nothing, as on 3.2).
- **Captured ItemValues (X12, ScMedic):** the live value is kept and a copy used only when the slot was cleared in place
  (type 0) - a plain clone would break L&L's in-place emptying and return rounds twice.
- **Backpack blobs** written ToClient (3.2's `true`, SCore 3.4's own drop); runner snapshots Persistency.
- `[HarmonyOptional]` on the three 3.4 StartQuest patches; PatchAll replaced by class-by-class patching with a count line.

### Stage 1: not verified (needs the game - goes into TESTING.md at the deploy)

X13 reading a held-gun record a 3.2 save wrote; 3.2 runner snapshots through 3.4's `read(Persistency)`; the linked
mirror against the loot window (drag/drop, swap, half, sort, take-all, move-all) and against RE Inventory 3.4 (it can
resize a loot grid in place; the view grows its cell map); the held model updating with no `OnUpdate`; MayEngage giving
fire at range back; the Point-formation door patch (a tiny method Mono may inline). Known edge: the view's Sort can
deposit a stack before its old box slot is freed - with every box full it goes back to the player, never lost.
The RE Inventory add-on `zzzz_GridInv_SurvivorCommunity` needs `ScStorage.GridDraws` retyped to
`Func<TEFeatureStorage, bool>` and a 3.4 build (its own step).
