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
  (This ends with the feature milestones of `research\ideas\DESIGN.md`: from I2, round 9, shared code changes both DLLs
  (its Q1). The live 3.2 game gets a build only when the user says.)
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

### Stage 2: XNPC 3.4 behaviour (2026-10-29, built, not run)

3.4 DLL `35a8b870` (0 errors, the 2 old CS0108 warnings); the 3.2 build still `81f6c2da`; HarmonyAudit 153/153 rows
bind on 3.4, 0 parameter issues; XML replay: 150/150 SC ops match on 3.4 (14 new, all inside two `xpath()` guards),
136/136 on 3.2 with every op and every post-patch document identical to Stage 1. Output in `research\sc34\stage2\`;
in-game steps in `research\sc34\stage2\TESTING_34_draft.md`.

- **D1** `Config/utilityai.xml`, guarded on XNPC 3.4's `SdcsNpcCore`: SC's actions keep the place they had among
  XNPC's actions on 3.2 (table below); `ScNotTravelling` (UaiOps, 3.4 only) gates MeleeApproach and BackupRanged for a
  runner on the move; `NoActiveChat` on the post, roam and quiet night (D8). XNPC's bands, medic, jobs and leave-area
  untouched.
- **D2** ScMedic: stops `buffInjuryBleeding` (cloth bandage, first aid bandage, first aid kit - cheapest first; out of a
  fight unless low); 1-2 cloth bandages stocked once per body (`sc_bandagesStocked`); counts and takes from the bag too
  (XNPC 3.4's start route moves medical items there); waits while XNPC's hand clips run (`SdcsAnimBusy`); XNPC's party
  medic: regen speed fixed on our patients (`SdcsMedic.ApplyHealOther` postfix), no start over a use of ours
  (`SdcsMedic.Begin` prefix).
- **D4** trade: X9 already lets XNPC's stock trader (64) through; while that trade is open SC's window writers wait
  (`ScAmmo.IsWindowOpen` + the trade lock, also the ammo move). Seeding: once per body by cvar, no SC flow copies a
  window between bodies; the only break was the medical move to the bag (D2's bag read). Creator faces: X17, a prefix
  on `SdcsCustomLook.ApplyCharacter` - a fresh rolled survivor takes the face SC's way (ScLooks.AdoptFace, SC's clothes
  kept, then stored like any look) and the name; every body with a look of SC's is refused. Sleepers:
  `SleeperVolume.TouchGroup` prefix keeps an enclave POI's rooms cleared whoever touches them.
- **D5** `XUiC_SCoreCompanion.BtnDismiss_Controller_OnPress` prefix: a member goes home (ScCommunity.ReturnHome).
- **D6** `Config/dialogs.xml`, guarded on XNPC's "Carried Weapons": `scUseHanded` off SdcsNPC's weapon menu.
- **D8** X7 stands aside (no relax, carry or aim writes) while the hand is holstered (VC's chat holster, XNPC's stow).
- **Risks** 19: AnyWeapon's equip of a handed weapon goes through ScArms.OnSwap (X16 finalizer); 20: `SdcsLastHeld`
  synced to SC's hand before XNPC's world-entry re-arm (X5); 21: SC's dialog swap postfix leaves VC's rows to
  Patch_VoiceSwap (`__runOriginal`); 22, 23 to test.

| SC action | 3.2 | 3.4 | Against XNPC 3.4 (weight) |
|---|---|---|---|
| ScAtPost, ScEncAtHome | 2.2 | 3.2 | over Guard/Stay 3, Chat 2; TooClose 3.5 x (1 - d^2/4) only within ~0.5 m (3.2: ~1 m) |
| ScRoam, ScEncRoam | 2.5 | 3.4 | over the post and Guard; TooClose within ~0.35 m; wild loot 1 under |
| ScNightIdle, ScEncNight | 3 | 3.6 | over all of TooClose (3.2: tie won as the later package) and the investigations |
| ScDefend*, ScEncDefend* | 4 | 5.2 | over BackupRanged/MeleeApproach 5 (3.2: over 3, tie at 4); never with MeleeAttack 5; under leash 5.5, shot 6 |
| ScReturn, ScEncReturn | 5.5 | 5.5 | over every 5; under shot 6, BackupKeepDistance 8 (as 3.2) |
| ScFlee, ScEncFlee | 6 | 6 | ties the shot 6 (later package wins, as 3.2); under BackupKeepDistance 8 (as under 7) |
| ScTravel | 4.5 | 4.5 | MeleeApproach/BackupRanged 5 gated off while travelling; MeleeAttack 5, shot 6, 4 m back-off 8 still win |
| ScRaider/ScEncInvestigate | 3 | 3.5 | over the raised roam and post (as 3.2), under the night idle |
| ScRaider/ScEncApproach | 4 | 4 | now under BackupRanged 5 (XNPC's paced back-off first: its 3.4 design, kept) |
| all | | | under medic 7, leave-area 9, busy 12 (accepted, D1); jobs 4 above the idles, but residents and enclave people have no leader, so no job, no party medic |

### Stage 3: review (2026-10-29)

Fable review of 15b0255..fd9d756 (`research\sc34\review\REVIEW.md`): DEPLOYABLE AFTER FIXES. Fixed in 1177b36 (3.4 DLL
`6148a65d`, 3.2 build still `81f6c2da`): H1 linked-storage view capped to the loot window's widgets unless the bridge
draws it; M1 MayEngage only overrides Voice Control's refusal; L1 a refused drop-one is returned on vanilla's grid; L2
XNPC's bare-hands equip leaves X12 on. Re-check: DEPLOYABLE. Left for after the first test: M2 (AnyWeapon may bounce
once after ScArms keeps a handed weapon - TESTING step 47), L3 (a creator face SC takes doesn't mark the character
alive for XNPC, so it can appear twice), and the re-check's new LOW: an enemy Voice Control refuses now scores 1 for
SC's leaderless people against 0.95 for their current target, so they may switch to it (fix: use VC's private
`StickScore` for the override). Test steps: TESTING.md "Game 3.4 port". Install: `Port33\tools\deploy_sc34.py`.

**Decided (the user, 2026-10-29): enclave people hold their ground inside their home when attacked.** XNPC's leave-area
(9: an unhired body inside a sleeper volume that a zombie targets leaves 30 m) is gated off for enclave people at home
by `ScNotEncHome` on `LeaveAreaSdcs` (guarded XML, 3.4 only; 3.4 DLL `bfff2469`). Everyone else keeps XNPC's rule -
residents of a base built inside a POI included (not asked; the same one-line gate would cover them).

### Installed (2026-10-29)

Installed into the 3.4 install with the user's go-ahead (game closed): `Port33\tools\deploy_sc34.py`, HEAD da7b222,
1928 files = the tracked files, the DLL swapped for the 3.4 build: installed = built = `bfff2469`; ilspycmd on the
installed DLL shows PatchClassByClass, ScLinkedContainer34, ScNotTravelling, ScNotEncHome. No SC was there before
(nothing backed up). The 3.2 game's SC untouched (`81f6c2da`). Next: the user's test, TESTING.md "Game 3.4 port".

### Round 3: linked storage and the RE Inventory bridge (2026-10-29, built, not installed)

The user's first test (start-up steps passed): "linked storage doesn't distribute items to the crates" - 6 rifles
shift-clicked in stayed in the linked view, the crates stayed empty, crate items didn't show. The 3.4 `Player.log`
(session 22:14-23:06) explains it:
- the view first opened with `1 box(es), 0 stack(s), 28 free` - the **land claim block's own storage** (keystoneBlock has
  a TEFeatureStorage, `playerLandClaimStorage` 7x4, on 3.2 too). The crate was crafted a minute later, so the rifles
  could only go there. In chunk order the claim block comes first anyway.
- with the crates linked (`3 box(es), 31 stack(s)`), RE Inventory 3.3+ **shrank the view** to its loot list's 9x4
  (`container scLinkedStorage shrunk from 9x14 to 9x4`, ContainerCapacity.EnsureLootable): the free band cut off.

Fixes:
- **SC (3.4 only):** a land claim block's own storage goes after every other box - Scan appends them last, DepositInZone
  walks the crates first. The 3.2 game has the same order (a follow-up there, not this branch).
- **SC (3.4 only), diagnostics under DebugLogging:** each deposit (`linked storage: <item> xN into <block> at <pos>, slot
  i`), a failed one with every box's slots/empty/room, a refused cell, and the boxes at open (`linked storage boxes:`).
- **SC (3.4 only), SizeFor:** the view's cells never exceed the loot window's widgets (RE: 182 -> 20 rows).
- **Bridge, 3.4 build** (`My Mods\Source\zzzz_GridInv_SurvivorCommunity`, `-p:BridgeGame=34`, define
  `GRIDINV_SC_V34`): adds `scLinkedStorage` to RE's `ContainerCapacity.NoScale`, so the view keeps SC's size; sets only
  the view hooks. RE 3.3+ is one cell per slot (footprints overflow -> the grid grows rows), so a box's room is its
  free slots: the 3.2 cell hooks (a 5x2 rifle = ten of a box's slot-count cells) would call a crate with empty slots
  full (Fable round 2, H1) and are not set.

Builds: SC 3.2 still `81f6c2da`, SC 3.4 `7a680c4d` (build\sc34); bridge 3.2 still `be634f59` (= the original and the
live 3.2 copy), bridge 3.4 `41c43b91` (build\bridge34). AshfallUI's 3.3+ edition now carries SC's Home Status strip
(AshfallUI c02cc17). Reviews: `research\sc34\review\round2` (H1: the cell hooks, fixed), `round3`: DEPLOYABLE (no
HIGH/MEDIUM; LOWs left: the brace-less pass loop in DepositInZone, the strip's "free cells" wording that means slots on
3.4, RE's re-bind after every shift-click logging `container opened` - not repeated opens). Test steps: TESTING.md
group I (52-57).
To install (game closed, the user's go-ahead): commit, `deploy_sc34.py <full md5> --go`; the bridge's ModInfo.xml,
README.md and build\bridge34 DLL into `<3.4>\Mods\zzzz_GridInv_SurvivorCommunity`; AshfallUI `python tools/deploy33.py`.

**Installed 2026-10-30** with the user's go-ahead (game closed): SC HEAD e679679, 1928 files, installed = built
`7a680c4d` (old install in `Port33\_backups\sc34_2026-10-30_000121`; its config had no user edits); the bridge's new
folder, installed = built `41c43b91` (loads after SurvivorCommunity and zzzz_1_REInventory); AshfallUI 3.3+, all 2229
files = the repo, `windows.xml` = c02cc17. Next: the user's test, TESTING.md group I (`DebugLogging` true for the
`linked storage` lines).

### Round 4: a crate takes only what its own grid fits (2026-10-30, built, not installed)

The user's second test: "I put a bunch of weapons in linked storage, but they only went to one crate, which made the
crate storage capacity bigger than it should be, rather than filling up the next available storage." Round 3 took
Fable's round-2 H1 at its word - "on 3.4 a crate's real capacity is its slot count" - but that is only true of a CLOSED
box: RE Inventory 3.3+ lays a box out one cell per slot and its own window lets an item in only where those cells fit
the footprint (`XUiC_ContainerGrid.HasRoomFor`: Pack the slots into the grid's cols x rows, place the newcomer).
`GridSpace.ContainerHasRoomFor` asks that only while the box is bound to an open grid, so linked storage's writes into
closed boxes were never refused, and the box's window then grew rows to seat them (`growToFit`). A crate's capacity
as the player sees it is the footprint room of its slot-count grid - 3.2's semantics, on 3.3+'s shape.

Fixes:
- **Bridge, 3.4 build:** sets `BoxHasRoomFor` and `BoxFreeCells` again, on 3.3+'s shape: `ChooseShape(ItemGrid.ContainerSize,
  ItemGrid.Length, GridConfig.Cols, 0, 0)` (RE's `ContainerStore.Hint` is `ItemGrid.ContainerSize`), `Pack` largest
  first, `CanFitAnywhere` (either orientation). A box whose contents already overflow its grid (the crate from the test)
  takes nothing and counts 0 free cells. (`StackArea` was left unset in the first build. After the review it is set and
  only sizes the view; see below.)
- **SC (3.4 only):** a shift-click (AddItemLinked) that no box takes now says `scStorageFull` ("No box has room for
  that."), as a drop on a cell already did. With the footprint test a free cell in the view no longer means a box takes
  a rifle, and the silent no-op read as broken. 3.2's AddItemLinked is unchanged (equally silent there).
- **Not here - AshfallUI's own session (user, 2026-10-30: AshfallUI is edited by its own session only):** the Home Status
  strip's supply icons are vanilla's under the Ashfall plate (user: "missing the new Ashfall sprites for food/water/etc.").
  Cause: SC's `windowScHomeStatus` rides the HUD's `toolbelt` group, which AshfallUI's `MenuMetal` leaves out of its icon
  swap (`HudGroups`); every icon in it (ui_game_symbol_fork, _water, _medical, _shape_ammo, _brick, _gas, _players,
  _noise) has a painted brass twin. Likely fix on that side: `windowScHomeStatus` in `MenuMetal.HudIconWindows`, as the
  quest/recipe trackers are (the mood dot, `ui_game_filled_circle`, has no twin). Same on 3.2.

Fable review (`research\sc34\review\round4\REVIEW_round4.md`): NOT DEPLOYABLE as first built (SC 3.4 `2c629885`, bridge
3.4 `df993533`); the box-side change itself checked correct (shape, Pack, CanFitAnywhere = RE's Bind/HasRoomFor; free
cells <= empty slots; the cache invalidated on every write). Its H1 was the AshfallUI line, reverted on the user's word.
Fixed after it:
- **H2: ROUND 3 DUPLICATED ITEMS on a shift-click into linked storage.** The game's Harmony (0_TFP_Harmony, HarmonyX,
  `HarmonyManipulator.WritePrefixes`) calls EVERY prefix in order and ANDs the bool results. A false never skips a later
  prefix. `ref __result` is one shared local, so the last writer wins. The order is priority (descending), then
  first-patched first. SC's InitMod runs before RE's (load order), so at equal priority SC's AddItem prefix ran FIRST:
  it deposited into a box, set `__result = true` and returned false. Then RE's `TEFeatureStorage_AddItem_GridSpace` gate
  ran on the view, now one stack fuller. It found no 5x2 room, logged its refusal and set `__result = false`.
  `XUiM_LootContainer.AddItem` then reported failure, and the backpack KEPT the rifle while the crate held a copy.
  - Evidence: one click in the user's log (Player-prev.log 07:47:48): `linked storage: Sniper Rifle (T3) x1 into
    cntWoodWritableCrate ..., slot 18`, then `scLinkedStorage: grid grown from 10 to 12 rows`, then `[GridInv] no grid
    space for gunRifleT3SniperRifle (5x2); refused`. The re-check also saw `slot 9, slot 10, refused` at 07:23:06 (that
    log has rotated out since).
  - This fits "crate capacity bigger than it should be". The crate the user filled (48 slots, 19 stacks after the test)
    probably holds duplicated rifles.
  - Round 2's L2 ("SC runs first") had the order right, but its conclusion (RE's prefix is then skipped) was wrong. My
    first reading of the log ("RE refused before SC ran") was wrong as well.
  - The review's own fix (a), "SC first", is round 3's duplicate.
  - Fix: SC's AddItem and TryStackItem prefixes are `[HarmonyPriority(Priority.Last)]`. They decide alone for the linked
    view (no `__runOriginal` guard) and overwrite `__result`, so nothing writes `__result` after a deposit. RE's gate
    still runs first and logs `no grid space ... refused` when the view looks full; with this build that line means
    nothing.
  - SCore's slot-tag prefix asks about the view's own block, which means nothing for a window over many boxes. Its
    shift-click rule still applies through its XUiM_LootContainer.AddItem prefix. Its TEFeatureStorage rule is now
    overridden for the view on Move All (LOW).
- **(b), drag-and-drop:** RE's drop code needs free cells in the VIEW's grid. The bridge sets `StackArea` on 3.4 again
  (`stackArea34`). SC 3.4's Rebuild sizes the view as on 3.2 (area + free band, in slots = cells on RE 3.3+) but hides
  stacks only by COUNT (`live.Count >= MaxCells` / StackCap). So the view has the free band as real cells, and H1(c)
  (rifles hiding stacks past 180 cells) stays fixed. Past 180 slots RE grows rows itself.
- **M1, drop-one duplicate:** RE 3.3+ keeps the whole held stack on the cursor until `writeSlot` confirms (dropOneAt
  writes 1 of N). `StillOnCursor` compared counts with `==`, so a refused drop-one gave 1 back to the player AND kept N on
  the cursor. Now the same count OR a written count of 1. The re-check's M1: `>=` (the v2 build b0605da7) could keep a
  stack that vanilla's sort moved into an unmapped cell, with every box full, off the player while the cursor held more
  of that item.
- Re-check (`RECHECK_round4.md`): DEPLOYABLE.
  - Harmony reading confirmed.
  - Only SCore and RE patch the three targets in the 3.4 install; no postfixes.
  - The view math checks: free cells = the band while stored footprints + band stay within 180 cells. Past that the band
    is cut, and a dragged rifle is refused by RE's drop code; shift-click and Move All still work.
- Left: M2 (stricter than the box's window: no make-room-by-arranging; an over-full box takes nothing, though its grown
  window would take a small item). That is conservative by design. LOWs: a refused swap can move the blocker to another
  box or the player; resize-on-open when a saved size differs from loot.xml.

Builds: SC 3.2 still `81f6c2da`, SC 3.4 `9ae3d333`; bridge 3.2 still `be634f59`, bridge 3.4 `9dcc08e8` (against the 3.4
install's RE DLL `7dc5b92c`). Test steps: TESTING.md group J (57a-63).

**Installed 2026-10-30 08:28** with the user's go-ahead (game closed):
- SC: `deploy_sc34.py`, HEAD 08d3f73, 1928 files, installed = built `9ae3d333`. The old install is in
  `Port33\_backups\sc34_2026-10-30_082831`. Its only user edit, `DebugLogging` true in Config/blocks.xml, was copied
  back.
- Bridge: `build\bridge34` DLL + ModInfo.xml + README.md into `<3.4>\Mods\zzzz_GridInv_SurvivorCommunity`; installed
  = built `9dcc08e8`. Round 3's copy is in `Port33\_backups\bridge34_2026-10-30_round3`.
- The 3.2 game is untouched (SC `81f6c2da`, bridge `be634f59`).
- Next: the user's test, group J.

### Round 5: Sort leaves the boxes alone; the grid draws only real cells (2026-10-30, built, not installed)

The user, testing round 4: "Free cells sometimes changes to a different value than the cells that I see are free on
screen." Then, of the filler cells: "I want to avoid adding cells that don't actually work, it would be confusing." The
footer was right: it is the boxes' real room (`ScStorage.FreeCells`). The screen was wrong in three ways (log kept at the
session's `player_round4_2147.log`):

- **Filler.** Rebuild has to give the view whole rows. Screenshot at 21:42: 74 cells of stacks + 2 free = 76, rounded up to
  9 rows = 81 cells, so 7 empty on screen against the footer's 2.
- **Extra rows.** RE's `growToFit` adds whole rows when the view is too tight to pack (`grid grown from 9 to 12 rows`).
- **Sort moved stacks between boxes (the real bug).** XUiC_LootWindow's button runs `te.ItemGrid.Sort()` on the view, then
  `NotifyUnlockedSlotsChanged` raises SlotChanged for every cell, and OnViewSlotChanged wrote each cell's new stack into
  the box slot behind that cell (`WriteThrough`, no room check). Stacks were dealt across the boxes by position.
  - 21:45:23 to 21:45:28: one Sort, same 66 stacks, crate 3 free cells to 1, footer 50 to 48, grid 15 rows to 14.
  - 21:42:30: the crate refused a Plaster Cast at 0 free; after a Sort it took three more with nothing removed.
  - 21:44:53: five deposits in one tick, alphabetical, the Sort's cells with no box behind them.
  - The land claim block (28 cells) worked out at 31-32 cells of stacks at 21:45:23: the second way a box goes past its
    grid. RE's drag swap is not a third: it clears the slot, the cell unmaps, and the write goes through Deposit.
  - RE's own repack (`RepackAfterSort`) was not the cause: it moves placements and never writes a slot.

Fixes:
- **SC (3.4 only):** `Patch_LinkedSort`, a prefix on `ItemStackGrid.Sort`: for the view's own grid, `MergeStacks` and no
  vanilla sort. It merges partial stacks through WriteThrough (out of one box, into the other, undone if the second write
  fails) and moves nothing else. What the player sees is unchanged: RE's repack orders by area, type, slot, so vanilla's
  order never reached the screen. No more than vanilla's rule: nothing with quality; a merge that would grow the target's
  footprint is left alone (RE's stack footprints option, off by default). Side effect, accepted: Sort no longer frees
  room in a crate by accident.
- **SC (3.4 only):** `CellsNow`, the stacks' area + `ShownFree`, at least 1; `ScStorage.CurrentCells` for the bridge, which
  references no SCore and so cannot touch ScLinkedContainer.
- **RE Inventory, by its own session on the user's word (port-3.3 3bf9871, DLL `a221f9f7`, 3.2 byte-identical,
  its own three-round Fable review):** `ContainerLayouts.CellLimitHook`. A container grid can have fewer cells than its
  rectangle, using the bag's void tail (GridMap.CellLimit). Asked once per layout; <= 0 or >= cols*rows = no limit; final
  limit max(answer, last occupied cell + 1). Steps: its PORT33.md 32-36.
- **Bridge, 3.4 build:** sets the hook to `ScStorage.CurrentCells` for `scLinkedStorage`, from its own method, so an RE
  Inventory without the hook (MissingFieldException) keeps the padded row and the log says so. To build before RE is
  installed: `-p:GridInvDll=...\build\reinv33\GridInventory.dll`.

The footer rewording first proposed is dropped: the grid now matches the footer. What can still differ, all stated to the
user (TESTING 70): gaps between big items (void is tail-only), room split across crates refusing a rifle, past 12 rows of
empty cells.

Fable review (`research\sc34\review\round5\REVIEW_round5.md`): DEPLOYABLE AFTER FIXES on the first build (SC 3.4 `efab580f`,
bridge 3.4 `90edfc27`), no HIGH, no way found to lose or duplicate items. Fixed after it:
- **M1, a near-empty crate reading as full.** RE's Pack was one first-fit pass, so for example two 5x2, a 4x2, a 3x2 and a
  1x5 overflowed an 8x6 crate at 39 of 48 cells. The log's `48 slots, 42 empty, 0 free cells` is a state of this kind (six
  stacks, only two of them named in the log). The crate's window would grow a row, the bridge called it over-full, and
  the cell limit would have hidden its room too.
  - RE Inventory, by its session (port-3.3 1d73fc6, DLL `92eb9888`): Pack retries other orders before it reports overflow.
    A pack that fits keeps its layout; RepackAfterSort and packKeepingPins got the same retry.
  - Bridge: `BoxHasRoomFor` packs the box's items WITH the candidate and answers overflow == 0, which is the question its
    window answers on the next open (it packs afresh, no arrangement is kept).
  - RE's note on the scratch map having no limit does not apply: the hook answers only for `scLinkedStorage`, never a box.
- **M2, Sort inert on vanilla's grid.** `SortView`: on vanilla's grid `SortCells` sorts the cells and the map together with
  the mirror off, so no box is written.
- N3: a failed rollback logs an error. L2, L3, N4: TESTING 64, 66, 67, 70 corrected; L1 stated in 68; new step 73 for M1.
- Left by decision: L1 (a stack lifted from an over-full box can't go back; that is the rule, pre-existing), L4 (3.2 has the
  same Sort bug; its build stays byte-identical, a separate change if the user wants it), L5 (an empty base shows one
  cell), N1, N2, N5.

Re-check (`RECHECK_round5.md`): DEPLOYABLE, no HIGH or MEDIUM. M1 and M2 closed; 6,000 random crates replayed against the
packer: 365 now taken that were refused (the fix), 9 now refused that were taken, all of which would have grown the crate's
window. Its L1: TESTING 73 used a shovel (1x4), which never showed M1; now a spear (1x5) and a 4x2. Its N2 left: SortCells
has no try/catch, and CompareForSort can't throw on what it sees.

Builds: SC 3.2 still `81f6c2da`, SC 3.4 `cf41c40b`; bridge 3.2 still `be634f59`, bridge 3.4 `d49b0cdb`; RE `92eb9888` (3.2 byte-identical). Test
steps: TESTING.md group K (64-73), RE's PORT33.md 32-36.

**Installed 2026-10-31 08:24** with the user's go-ahead (game closed), all three together:
- RE Inventory: `stage_reinv33.py --deploy`, port-3.3 1d73fc6, 505 files, installed = built `92eb9888`. The script deletes the
  old folder and keeps no backup, so it was copied first to `Port33\_backups\reinv34_2026-10-31_round5`. Only the DLL differs.
- SC: `deploy_sc34.py`, HEAD 0b7771e, 1928 files, installed = built `cf41c40b`. The old install is in
  `Port33\_backups\sc34_2026-10-31_082438`. Its only user edit, `DebugLogging` true in Config/blocks.xml, was copied back.
- Bridge: `build\bridge34` DLL + ModInfo.xml + README.md; installed = built `d49b0cdb`. Round 4's copy is in
  `Port33\_backups\bridge34_2026-10-31_round4`.
- The 3.2 game is untouched (SC `81f6c2da`, bridge `be634f59`).
- Next: the user's test, group K.

### Round 6: the land claim block out of linked storage; Sort doesn't grow a crate (2026-10-31, installed)

The user, testing round 5: "I still see crates growing in size when sorting. Also, I don't think the land claim should have
it's own storage, it's hard to determine what cells belong to the land claims own storage." Log kept at the session's
`player_round5_test.log`. Round 5's own fix held: each box's stacks and free cells were identical before and after a Sort of
the linked view (log :5645, :6005).

- **SC (3.4 only):** the land claim block's storage is not linked. Scan skips any box with TEFeatureLandClaim, and
  DepositInZone loses its second pass. Removing TEFeatureStorage from keystoneBlock instead was ruled out: outposts use it
  (the packing strip, ScStockpile.StationOf, and hauled bundles, ScOutpostPerks.Unstash, both reach it through the tile
  entity), and `TileEntityComposite.read` skips the payload of a feature the block no longer defines, so a placed block's
  items would be lost on load (the user's holds 20 stacks). They stay in the block, opened there. scOutpostFlag extends keystoneBlock, so it is left out too. A base with no crates gets a window with 0 boxes;
  a handed-back weapon falls through to the backpack, then a bag (ScArms.Return).
- **RE Inventory, asked of its session:** Sort in a crate's own window grew it. A crate at `48 slots, 40 empty, 4 free cells`
  opened at 8x6, and Sort logged `grid grown from 6 to 8 rows` BEFORE `repacked 8 item(s) after sort` (:5609-5610).
  Second, smaller case: a hand-arranged crate growing on open (:6076). Its session's fix, port-3.3 3972921, DLL `e33a4172`, 3.2
  byte-identical, two Fable reviews: a sort lays out once at the end, a grown grid goes back to the container's own rows if
  everything seats, Pack ends with a bounded exact search, and the `grid grown` line gives `(44 of 48 cells)`. This reaches
  every 3.3+ container: when one would have grown, its unlocked items can move to stay in shape.
- **Bridge, 3.4 build:** `packed34` adds up the footprints first and reports over without a pack when they exceed the grid.
  Pack now searches before it reports overflow, and a tight box is asked once per stack on a Move All. Past that gate a
  refused stack can still cost one bounded search per box (about 3 ms on net8, more on Mono).

Fable review (`research\sc34\review\round6\REVIEW_round6.md`): DEPLOYABLE, no HIGH or MEDIUM. Both reasons for keeping the
storage on the block confirmed. Fixed in docs: L2, L3, L4 (TESTING 78, 79, 81), N1. Left: L1, with no crate in the zone the view
is one cell and says "No box has room for that." rather than naming the cause (the user is told; a small change if wanted).
N2 goes to RE's session: reconcile and RepackAfterSort measure by ItemValue, Pack by ItemStack, which differ only with stack
footprints on.

Builds: SC 3.2 still `81f6c2da`, SC 3.4 `bbe3439c`; bridge 3.2 still `be634f59`, bridge 3.4 `7e93a8f8` (against RE `e33a4172`). Test
steps: TESTING.md group L (74-81), RE's PORT33.md 32-36.

**Installed 2026-10-31 10:15** with the user's go-ahead (3.4 game closed), all three together:
- RE Inventory: `stage_reinv33.py --deploy`, port-3.3 3972921, 505 files, installed = built `e33a4172`. The old folder was copied
  first to `Port33\_backups\reinv34_2026-10-31_round6`. Only the DLL differs.
- SC: `deploy_sc34.py`, HEAD 90ac67d, 1928 files, installed = built `bbe3439c`. The old install is in
  `Port33\_backups\sc34_2026-10-31_101515`. Its only user edit, `DebugLogging` true in Config/blocks.xml, was copied back.
- Bridge: `build\bridge34` DLL + ModInfo.xml + README.md; installed = built `7e93a8f8`. Round 5's copy is in
  `Port33\_backups\bridge34_2026-10-31_round5`.
- The 3.2 game is untouched (SC `81f6c2da`, bridge `be634f59`).
- Next: the user's test, group L.

### Round 7: a POI favour no longer re-arms the POI as it starts (2026-11-02, built, not installed)

Found while porting LandClaimUpgrade (its PORT_33.md). On 3.3+ `Quest.StartQuest` calls `QuestEventManager.ForceUpdate()`
for a new quest, and `QuestJournal.AddQuest` adds the quest to `quests` only after StartQuest returns. ClearSleepers subscribes
in StartQuest, so the first `SleeperEventData.Update`, vanilla's POI re-arm (`DespawnAndReset` on every sleeper volume), runs
inside AddQuest. `ScQuests.IsOurPoi` looked only in the journal, missed the new favour quest, and the POI's cleared rooms were
reset on the spot. 3.2 has no ForceUpdate: the first update came a frame later, with the quest in the journal.

- **SC (3.4 only):** `ScQuests.Start` holds the quest in `beingAdded` around `AddQuest`, and `IsOurPoi` checks it first. Only
  the favour quest (`QuestId`) has a ClearSleepers objective; the other AddQuest sites add quests that never subscribe.

Builds: SC 3.2 still `81f6c2da`, SC 3.4 `c0ea84c3`. Test step: TESTING.md group M (82).

Reviewed with LandClaimUpgrade's port (`research\lcbu33\review\REVIEW.md`): the fix is correct and complete. Its L4 (the
favour quest on an already-cleared POI) does not apply: the quest only advances to phase 2 (ScTalkToSurvivor) and stays
in progress, so its position records stay valid.

**Installed 2026-11-02 22:08** with the user's go-ahead (3.4 game closed): `deploy_sc34.py`, HEAD 8b560f8, 1928 files,
installed = built `c0ea84c3`. The old install is in `Port33\_backups\sc34_2026-11-02_220821`; only the round 7 files
differ from it. `DebugLogging` true copied back. The 3.2 game is untouched (`81f6c2da`).

### Round 8: VC no longer talks over SC's lines (V1) (2026-11-03, built, not installed)

The user: Voice Control's speech cut off SC's voiced dialogue lines on SC's survivors, sometimes more than once in one
line. Research: `research\voices\REPORT.md` 1.1-1.6 and 5. Every Voice Control sound on an NPC goes through one
`NPCAudioPlayer` with one AudioSource, the one SC's clip plays on, and nothing in Voice Control looks at the open dialog.
The walk-up greeting (LLM text, synthesized 1-4 s after the trigger, so it often lands just after the talk opened), a
follower's re-greet after 300 s and XNPCCore's say-lines (3.4) all reach it. `SpeakStreaming` starts with `StopSpeaking`
whenever the player is playing, so a new line cuts SC's clip at once. A greeting that was already playing keeps its queue:
after a watchdog (the cut sentence's length + 3 s) the next sentence plays over SC's clip, so a two-sentence greeting could
cut SC twice. 3.2 is the same, without say-lines and with NPC-to-NPC chat able to pick the NPC you are talking to.

- **SC (both builds): the voice gate, REPORT 1.5 B.** `ScVoice.HoldsVoice(npc)`: one of the community's survivors
  (`ScXnpc.IsSurvivorBody`), `MuteVcInTalk` (new key in Config/blocks.xml, default true), and SC's talk open on them
  (`XUiC_ScDialogLine.Update` -> `ScVoice.TalkOn`, cleared in `OnTalkClosed`) or SC's clip still playing on them (the
  entity is now kept with the clip's end; `Stop` clears it). Prefixes in `Patches_VoiceControl`, bound by name like the
  other Voice Control patches (Prepare off without Voice Control or on a changed signature or parameter name, every
  target in ModApi's Voice Control block), each in try/catch + `ScLog.Once`:
  - `NPCAudioPlayer.OnWavBytesReady(byte[], VcTrace, Action, bool)` (private): every TTS sentence ends here, those queued
    or prefetched before the talk opened included. Held: `onComplete` invoked, original skipped (no clip, no MP relay).
  - `NPCAudioPlayer.PlayClipFile(ClipEntry, Action)`: the same, except SC's own call (`ScVoice.SelfCall` set around it).
  - `NPCAudioPlayer.SpeakStreaming(string, bool, Action, VcTrace, bool)`: **added to REPORT 1.5 B**. Its entry
    `StopSpeaking` would cut SC's clip before the first sentence even reached OnWavBytesReady. Held: `onComplete`
    invoked, skipped. Every Voice Control caller sets its own state before the call and clears it in the callback
    (greeting busy flag, phrase-trigger waiting state) or passes an empty one, so a callback on the spot is safe.
  - `NetPackageNPCVoice.PlayVoiceLocally(int, byte[], float)` (private static): another machine's relayed TTS.
  - `NPCChatComponent.PlayProactiveGreeting(string, string, bool)` and `SubtitleManager.AcknowledgeCommand` (3.4:
    `(EntityAlive, PhraseTrigger, string, VcTrace, string)`; 3.2: no last string, bound under `#if SC_V34`): skipped
    whole, so no HUD subtitle over SC's line panel and no synthesis. Checked state-neutral: the greeting sets
    `s_proactiveGreetingBusy` only inside, together with the callback that clears it, returns at once when
    `_isWaitingForResponse`, and nothing subscribes to its speech events. Everything TriggerProactiveGreeting stamps
    (cooldown, greeted player, the 20 m reset, the buffered text) is set before the call, as for a played greeting, so
    the greeting is used up rather than retried after the talk. AcknowledgeCommand is static and keeps no state.
  - Voice Control's HUD subtitle is cleared once as a talk opens on one of SC's survivors (`SubtitleManager.Instance
    .ClearSubtitle()` by reflection), for a greeting that was already showing.
- **The answer pass, instead of REPORT's IsWaitingForResponse.** Voice Control clears `_isWaitingForResponse` before
  the answer reaches the sinks: on the first streamed LLM chunk (NPCChatComponent.cs:117,594), and in HandleLLMResponse
  before its SpeakStreaming (:2150). So that test would let the first words through and swallow the rest. Instead a
  prefix on `NPCChatComponent.ProcessPlayerMessage(string, EntityPlayer, bool, Action<string>, VcTrace)` (push-to-talk,
  typed chat, VC's console chat; a player and non-blank words) lets that NPC speak through Voice Control. The pass ends
  at SC's next recorded line on them (`ScVoice.Play`), the talk's close, or a new talk. 3.4 only:
  `DialogActionSayCarrying.PerformAction(EntityPlayer)`. Without it, the dialog's "What are you carrying?" row (answered
  with a say-line through AcknowledgeCommand) would go silent during the talk.
- SC never calls Voice Control's `StopSpeaking` (it drops the queue without onAllDone and can stick
  `s_proactiveGreetingBusy` for every NPC); every swallowed call has its callback invoked.
- Console: `sc voice` prints a second line, `[SC] voice gate: ...`, also `sc voice gate` on its own: `MuteVcInTalk`, the NPC
  held and why, the answer pass, lines swallowed this session by sink, and calls let through by the pass. With
  `DebugLogging` each swallowed call logs `voice gate: kept Voice Control quiet on <name> (<id>): <what> at <sink> (<why>),
  <n> this session`. Test aid: `sc voice sayline [line] [seconds]` gives the nearest survivor an XNPCCore say-line after
  a delay, through XNPCCore's own `XNPCCore.SayLine` hook (3.4), or AcknowledgeCommand where there is none (3.2).
- Left as they are: `NPCAudioPlayer.Speak(string, Action, VcTrace, bool)` (no Voice Control caller; its sentence still
  meets the OnWavBytesReady gate). The direct playback in `ProcessVoiceInputAsync` (no caller). `AbortConversation`'s
  StopSpeaking (only during the player's own VC exchange). Squad-keyword push-to-talk acks ("squad, follow"): they go
  to AcknowledgeCommand without ProcessPlayerMessage, so during a talk with a squad member they are kept quiet. A
  Voice Control sentence already playing when the talk opens finishes when SC's first line has no recording (SC's clip
  replaces it at once otherwise, as before). In MP the pass is per machine. When SC's clip cuts a Voice Control
  sentence, Voice Control logs its own watchdog warning once and goes on; that was already so before V1.

Checked: every target against the decompiles (`research\voices\work\vc34`, `vc32`) and the installed DLLs (ilspycmd:
3.4 `1-XNPCVoiceControl.dll` md5 `c30399c1`, 3.2 `10bd7658`); a net8 reflection probe that loads each game's
assemblies with SCore, XNPCCore and Voice Control: all targets bind (7 on 3.2, 8 on 3.4), every Prepare is true, every
injected prefix parameter names a real parameter or field of the right type. Harmony audit (`research\v1\audit34_*`,
`audit32_*`) against the round 7 DLLs (`research\v1\base34`, `base32`): only the new gate rows differ, all
DYNAMIC_UNRESOLVED (by-name targets, outside the audit's table). Expected targets: 3.2 111 with Voice Control (97
without), 3.4 126 (108).

Builds: SC 3.2 `2bed708b` (was `81f6c2da`), SC 3.4 `682e52d4`. Test steps: TESTING.md group N (83-93). Not reviewed,
not installed.

### Round 9: the zombie-cap fix (I2) (2026-11-03, built, not reviewed, not installed)

The first milestone of `research\ideas\DESIGN.md` (section 12; Q2 (a): layers A and C for every POI, B for infested rooms
only). Branch `ms/i2` from port-3.4 `a52c0b1`. The same code in both builds, no `SC_V34`: vanilla's SleeperVolume is the
same on 3.2 and 3.4 apart from its Read/Write stream types (3.2 lines are the 3.4 line + 1). From this round the 3.2 DLL
changes too (Q1).

The quirk, checked line by line in the V34 and V32 decompiles. `UpdateSpawn` (V34:941) reads `AIDirector.CanSpawn(2.1f)`
(V34:948; EnemyCount < MaxSpawnedZombies x 2.1, about 134 at the default 64, AIDirector.cs:181-184), and new spawns sit
behind it (V34:1013). A room whose step meets a full cap before anything spawned closes with `wasCleared` and "None
spawned, canSpawn False" (V34:1087-1100). Nothing on that path sets a respawn time, so it stays `ulong.MaxValue` (V34:199,
Reset :494): Touch and CheckTrigger only raise it (:745, :773), and UpdatePlayerTouched returns at once for a cleared room
not due (:554). The room is empty until a quest, a chunk reset, `sleeper r` or a POI reset calls DespawnAndReset. A room
cut short closes through ClearedUpdate (:1099) on a normal timer, and an infested room's special, appended last
(ScInfestations.MaybeHost), is what it drops. The enemy count is recounted every 20 ticks (GameStateManager.cs:143-165).

- **ScSleeperCap.cs (new).** `Unfought`, `Rolled` and `IsTriggerRoom` moved here from ScInfestations, which calls them
  (DueAgain, InfoLines, CapRoom, MaybeHost). `NeverDue` is the saved signature: wasCleared, respawnTime never, nothing
  spawned or tracked.
  - **Layer A, `AfterClose`** (UpdateSpawn's new postfix, for a room that stopped spawning in that call): closed empty,
    never due, the cap still full (the same frame and count vanilla's step read), `Unfought`, not an enclave's. It sets
    `respawnTime = now + SleeperCapRetryTicks`, vanilla's own due path. Touch keeps pushing it while a player stays near,
    and the first touch after it Resets and rolls the room. An outpost's or claimed room stays held by CheckTrigger's claim
    push (V34:769-777), as vanilla's.
  - **Layer B, `Hold`** (UpdateSpawn's prefix, now a bool prefix). It holds only a call where vanilla would run a step
    (spawnDelay down to 1). The room must be infested and not an enclave's. It must have spawn points, no restore list and
    no script running, and more rolled than spawned, with a stage for the next group at its gamestage. The cap must be
    full and a player (not a spectator) inside vanilla's spawned-room box, BoxMin - unpadding to BoxMax + unpadding with
    CheckTouching's +0.8 y (V34:147, 755-757). A held step sets spawnDelay 2, vanilla's own pace. The hold ends when the cap
    frees ("goes on"), nobody is near, or after SleeperCapPauseMaxTicks; then vanilla closes the room and layer A takes
    over. Pauses are a session `Dictionary<SleeperVolume, Pause>`. A pause not checked for 200 world ticks is stale, since
    the room was reset and rolled since (a quest's DespawnAndReset leaves no call to clear it).
  - **Layer C, `OnTouch`** (first in Patches_Infestations' UpdatePlayerTouched prefix, before I1's BeforeRoll). A room
    with the signature that is `Unfought` and not an enclave's gets `respawnTime = now`, and vanilla resets and rolls it in
    the same call (V34:558-561). It runs once per room per session and needs `ScRegistry.Loaded`, which brings the enclave
    list.
  - **Console `sc sleepers [status | scan | repair]`.** Status shows the switches, the enemy count against the limit and
    the session's counters (retries, repairs on touch and from the console, pauses: went on, timed out, nobody near), plus
    every room paused now. Scan lists every never-due unfought room of ScPoiIndex's POIs, nearest first and at most 30
    lines; it counts claimed ones and the enclave rooms it leaves alone. Repair makes those due now.
- **Config** (`Config/blocks.xml`, commented): `SleeperCapFix` true, `SleeperCapRetryTicks` 1000 (100-240000),
  `SleeperCapPause` true, `SleeperCapPauseMaxTicks` 3000 (0-240000; 0 = no pause). The `config:` line gains `sleeperCap=`.
- **Harmony targets: none new.** Both were already patched and in ModApi's expected list:
  - `SleeperVolume.UpdateSpawn(World)`, `public void UpdateSpawn(World _world)`, V34:941, V32:942. It has a bool prefix
    (`out bool __state`), the new postfix (`bool __state`) and I1's finalizer.
  - `SleeperVolume.UpdatePlayerTouched(World, EntityPlayer)`, `public void UpdatePlayerTouched(World _world, EntityPlayer
    _playerTouched)`, V34:552, V32:553. It has I1's prefix (layer C first in it) and postfix, and E1's prefix.
- **Other mods.** No installed mod patches UpdateSpawn, so the new bool prefix skips nothing anyone else expects. I
  searched every DLL under the 3.4 install's and the 3.2 game's `Mods` for the string, as ASCII and UTF-16.
  - Only SC has it. Ark's `UpdateSpawns` and SimpleSleepMod's `UpdateSpawnOnBedPlacement` are their own names.
  - On SleeperVolume, SCore (both installs) postfixes ClearedUpdate (its event) and AddEnemyToWorld.
  - ZombieAIOverhaul (the same DLL in both, and its source) patches Touch (a prefix on the wake touch only, setActive true,
    so never the respawn push A relies on), OnTriggered, CheckNoise and AddEnemyToWorld.
  - XNPCCore 3.4 calls TouchGroup and patches nothing there; POIInjector patches World.SetupSleeperVolumes.

Where the design was wrong or short, and the code followed the game:
1. **Layer C as written repeats.** `Unfought` cannot tell a cap-closed room from one whose spawn points are all blocked
   (FindSpawnIndex -1, World.CanSleeperSpawnAtPos), which closes the same way with "canSpawn True". A player standing near
   one touches it every couple of ticks, and a repair on every touch would reset and re-roll it each time. Hence once per
   room per session.
2. **"UpdateSpawn runs at most twice a tick (V34:409)" is not so.** `TickSpawnCount < 2` caps spawns per tick, not calls.
   Every spawning room's UpdateSpawn is called each tick while fewer than two spawns have happened, and vanilla runs a step
   every other call. So Hold decides only on a step call and keeps vanilla's pace.
3. **Layer B gained a condition:** the next group must have a stage at the room's gamestage. Without one vanilla closes
   the room with the cap free too, and a pause would only delay that.
4. **SleeperCapRetryTicks has a floor of 100.** A retry due before the next touch could push it would let a room at the
   cap roll and close every few ticks.
5. Trigger rooms stay out, as the design says (`Unfought`), so a trap room the cap closed at its trigger still never comes
   back. A trigger fires only from its block, and I1 counts such rooms cleared.

Builds: SC 3.2 `48797d89` (in place; was `81f6c2da`), SC 3.4 `3f921e50` (`build\sc34_i2`), both 0 errors.

HarmonyAudit (`research\i2\audit32_*`, `audit34_*`; the baselines `base32_*`, `base34_*` from `81f6c2da` and the
installed `c0ea84c3`):
- Every SleeperVolume row is OK on 3.2 and 3.4 in both DLLs: UpdateSpawn's prefix, postfix and finalizer, Spawn,
  ClearedUpdate, UpdatePlayerTouched (three patches), and CheckForAnyPlayerHome. The one exception is 3.4's TouchGroup,
  DYNAMIC_UNRESOLVED as before (a TargetMethod the json doesn't describe).
- Against the baselines, one OK row more in each (the new postfix) and the same non-OK rows.

Test steps: TESTING.md group O (90-103). Not reviewed (Fable), not installed. The task list needs its entry.
