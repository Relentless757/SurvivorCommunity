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

Test steps: TESTING.md group O (94-107, after the merge with V1). Merged with V1 on `dev/ideas` (d0740bc): 3.4 DLL
`d5979300`. Fable review of both (`research\review_v1_i2\REVIEW.md`): DEPLOYABLE, no HIGH or MEDIUM; doc findings
LOW-1 and LOW-4 fixed. Not installed.

### Round 10: infestations near home (I3) (2026-11-03, built, not reviewed, not installed)

The second milestone of `research\ideas\DESIGN.md` (section 3, idea 2), with the user's decisions of 2026-11-03 over the
design: no growth (idea 4 is dropped, so no grown weight and no `InfestHomeGrownWeight`); up to 5 nests count, for morale
and for attention; morale is a flat cost per nest by its distance band, at most 15 in all; attention is a flat +1/h per
nest (Q3 (a)); a nest that starts or spreads near home is known at once (Q3, yes). Branch `ms/i3` from `dev/ideas`
`d0740bc`. **3.4 only from this round** (the user's decision): only the 3.4 DLL is built, the tracked 3.2
`SurvivorCommunity.dll` is untouched, and the new code has no `SC_V34` guards. Single player only.

- **ScInfestations.cs.**
  - `NearNest` (new class): an infestation near home, its metres (XZ, rounded, from the home block to the footprint:
    `Infestation.DistanceXZ`), whether it is one of the nearest `InfestHomeMoraleNests` (`Counted`), and its band's
    `Cost` (0 when not counted).
  - `NearHome()`: the infestations of the `live` lookup within `InfestHomeRange` of the home block, nearest first (then
    by id). Empty with no home, `Infestations=false` (the lookup is empty then) or `InfestHomeRange` 0. Known or not.
    Cached until `Rebuild` (a `version` counter, also bumped by `Reset`) or the home block changes. Nothing is saved.
  - `NestsMorale()` (the counted costs, at most `InfestHomeMoraleCap`), `NestsForAttention()` (at most
    `InfestHomeAttentionCap`), `NestsAttention()` (times `InfestHomeAttention`), `NestsDetail()` (the breakdown's
    words: each counted nest with its metres and cost, then `; only the nearest N count` and `; at most -N in all` when
    they apply; an unknown nest is "a building not found yet").
  - `SpotNearHome`: with `InfestHomeKnown`, an unknown one within range of home becomes known with a tell, "Your
    people spotted the dead moving in close to home. <name> is infested, <D m direction>.", and a ledger line.
    `Infest` calls it after the console, siege and spread-from-a-known-one branches and before the radio's. The log
    line then ends `, spotted near home (<D m direction> from home)`.
  - `SpotAllNearHome`, from `Tick` after the markers are restored: the first tick after `OnGameStart` (which re-arms
    it), and again whenever the home block changes. It spots every unknown one near home, with the marker, the log line
    `infestation #N at <name> is near home (<where> from home) - your people spotted it` and one save.
  - `ListLines` (`sc infest list`): a second line `near home (within 500 m of home): N - morale -X (bands ..., the
    nearest 5 count, at most 15), attention +Y/h (+1/h each, at most 5); spotted at once: yes`. Each nest's line ends
    `- near home, D m: morale -C` (or `no morale (only the nearest N count)` / `no morale (past the last band)`).
- **ScMorale.cs.** `Parts.Nests` = `-NestsMorale()` after E6's parts, shielded by Leadership with the other negative
  parts, added to the total. `Breakdown` gets `Infestations near home -X (N within 500 m: <name> D m -C, ...)`.
- **XUiC_ScCommunity.BuildMoraleParts.** The part `Infestations near home: N`, icon `ui_game_symbol_infection`
  (vanilla's, in 3.4's buffs.xml).
- **ScNoise.cs.** `Advance` adds `NestsAttention()` to each quarter-hour step's rate right after `RatePerHour`, so the
  watch posts' cut, the blood-moon and open-attack pauses and "full stays full" apply to it as to the noise's.
  `Breakdown` (`sc noise`) gains `nests near home: N within 500 m, +1/h attention each, at most 5 counted: +N/h`, and
  its attention rate includes the nests (`... /h at this noise and N nest(s) near home`).
- **ScEnclaves.Where(from, to, metres)** (new overload): the direction to the centre with a given distance, so the tell
  says the same metres as the breakdown and the list (to the footprint, not the centre).
- **ConsoleCmdSc.** `sc morale`'s last line ends `nestsNearHome=N`.
- **Config** (`Config/blocks.xml`, commented; `ScConfig`, parsed and reset with the other infestation keys; the
  `config:` line's `infestations=` part ends `near home 500m (morale 150:5,300:3,500:2 for the nearest 5, at most 15;
  attention +1/h each for 5; known at once)`):
  - `InfestHomeRange` 500 (0-5000; 0 = no nest is near home).
  - `InfestHomeMoraleBands` `150:5,300:3,500:2`: metres:cost, sorted by distance. A nest costs the first band it fits,
    and nothing past the last. An entry that is not metres:cost (metres 1-20000, cost 0-100), or that repeats a
    distance, is skipped with a warning; if none can be read, the table is kept (`ParseHomeBands`, like
    `InfestTierByDay`).
  - `InfestHomeMoraleNests` 5 (0-100): the nearest that count for morale.
  - `InfestHomeMoraleCap` 15 (0-100).
  - `InfestHomeAttention` 1 (0-20) and `InfestHomeAttentionCap` 5 (0-100).
  - `InfestHomeKnown` true.
- **Localization.csv**: `scInfestNearHome`, `scInfestLedgerNearHome`, `scMoraleNests`, `scMoraleNestsCount`,
  `scMoraleNestsDetail`, `scMoraleNestsItem`, `scMoraleNestsUnknown`, `scMoraleNestsNearest`, `scMoraleNestsCapped`.
  New player-facing lines are text only. In the tell, the name and the distance are in a sentence of their own.
- **Harmony targets: none new.** ModApi's expected list is unchanged, so no audit was needed.

Checked offline: a net8 probe that loads the 3.4 assemblies and this DLL, with SC's Localization.csv in the game's
dictionary (`<scratchpad>\i3probe`). The band edges are right: 150 m costs 5, 151 m 3, 500 m 2, 501 m 0. Parsing sorts
and trims (the warning paths need Unity's Log, so they were read, not run). With nests at 120/250/420/600 m, three
count, nearest first, costing 5/3/2: morale 10, attention 3. Moving the home re-sorts them (the 420 m one is then 460 m
and the only one). With six nests, five count, the raw 23 is capped at 15 and attention at 5. An unknown nest shows as
"a building not found yet". No home, range 0 or an empty lookup gives 0. `ScMorale.Compute().Nests` is -15. The text of
`sc morale`, `sc noise` and `sc infest list` was printed and matches TESTING.

Where the design was off, and the code followed the tree:
1. **Line numbers drifted with I2.** In this tree, ScInfestations' `Infest` is at :507 (design :506), `Spread` at :577
   (:576) and `OnGameStart` at :1014 (:1034). I1's discovery tell and ledger are in `Tick` at :1166-1175 (:1186-1195),
   `AddMarker` is at :1196 (:1216) and `DistanceXZ` at :55-62 (the design's :53-59 starts at `Centre`). The
   ScMorale, ScNoise, ScConfig and XUiC_ScCommunity lines matched exactly.
2. **`sc noise`'s own rate.** The design only added a line. But `Breakdown` computes the attention rate and "full in
   about" from `RatePerHour` alone, so it would have disagreed with `Advance`. It now adds the nests too.
3. **The tell.** The design's "Your people spotted it: the Diner is infested, 420 m north." puts the name in the
   sentence a voice would say, so the sentence is split. `ScEnclaves.Where` measures to the centre while the range is
   to the footprint, so the new overload gives the footprint's metres.
4. **"The same check runs once in OnGameStart."** At GameStartDone the tick restores the markers, and a tell needs the
   player. So the check runs in the first tick after `OnGameStart`. It also runs again when the home block changes (an
   addition): otherwise a moved home would count nests the player was never told about.
5. **Unknown nests count.** The design counts every infestation within range. With `InfestHomeKnown=false`, or after
   `sc infest hide`, one can be unknown: it still costs morale and draws attention, and the breakdown leaves its name
   out.
6. **`NearHome(out count, out weight, names)`** lost its weight (no growth) and returns the cached list instead.
7. A spread from a known nest that lands near home keeps I1's spread tell and ledger line. It is known already, so it
   is not "spotted".
8. "Up to 5 nests count for morale" needed a key of its own (`InfestHomeMoraleNests`), because `InfestHomeAttentionCap`
   is attention's.

Build: SC 3.4 `515bc8b2` (`build\sc34_i3`), 0 errors. The 3.2 DLL was not rebuilt. Test steps: TESTING.md group I3
(110-119). Not reviewed (Fable), not installed. The task list needs its entry.

### Round 11: gifts and ally barter (E7) (2026-11-03, built, not reviewed, not installed)

`research\ideas\DESIGN.md` sections 6 (idea 5, gifts) and 7 (idea 6, enclave barter), with the user's answers: Q5 yes
(+3 standing for any bundle, once a day per enclave, never above 69, so gifts alone never make an ally), Q6 yes (2 of
yours for 1 of their own resource, once a day per ally), Q7 (b) for now: the radio window opens only at a radio block,
so there is no Radio button in the community window. Branch `ms/e7` from `dev/ideas` `d0740bc`. **3.4 only** (the user,
today): only the 3.4 DLL was built; the tracked 3.2 `SurvivorCommunity.dll` is untouched, and the new code has no
`SC_V34` (nothing in it differs between the games). Single player only, as SC.

- **Gifts, `ScEncGifts.cs` (new).** The leader's three greetings gain `scEncGift` "I brought you something." after "What
  do you need?" (8 entries, 6 on screen). Its first requirement `ScEncGift value="show"` (Hide: gifts on, the leader at
  home of an enclave that is not hostile and on the map) gives the greyed row's words, then `value="open"`
  (`ScEncGifts.Blocked`: at `EnclaveGiftCap` first, then the day's wait, then nothing carried). A greyed row shows only
  its first requirement's description (`XUiC_DialogResponseEntry` "response" binding ->
  `DialogResponse.GetRequiredDescription`, V34), so it reads "I brought you something. (Come back tomorrow.)", worked out
  at most twice a second per enclave as E3's enlist reason is. The row's action `ScEncGiftList` builds the pick;
  `scEncGiftPick` lists `scEncGift0..3` (`ScEncGiftRow value=i`, Hide), one row per resource carried, its smallest bundle
  (an old one-size bundle, then small, medium, large), the resources the group doesn't live on first
  (`ScEncRequests.OwnResource`, now internal), then theirs, then luxuries. The rows' words, "A medium bundle of
  medicine", come through E4's row-text postfix (`Patch_DialogRowText` -> `ScEncRequests.FillRow` -> `ScEncGifts.FillRow`
  for the `scEncGift<digit>` ids; their Text is "{gift}"). "Never mind." goes back to the greeting by band (three rows,
  the `scEncForget*` pattern); done.
  - A row's action `ScEncGift value=i` (`ActGive`) checks everything again, then takes exactly that bundle
    (`ScEncRequests.Carried` + `TakeBundles`, now internal with their `Bundles` struct: a short take is given back), and
    `Apply`: `GiftDay` = request clock + `EnclaveGiftDays`, `AddStanding(e, GainNow, .., ledger: false)` (the clamp, the
    marker on a band change, E6's `OnBandChanged`), its own ledger line "gift to The X: a medium bundle of medicine (+3
    standing)" (", now Friendly" on a band change, E4's `scEncReqLedgerDelta*` words), E4's band tell, the log, a save.
    `GainNow` = min(`EnclaveGiftStanding`, `EnclaveGiftCap` - standing), so 68 + a gift is 69.
  - `scEncGiftReply` ({giftreply}, `ScEncDialog.Fill` -> `ScEncGifts.FillReply`): the thanks by band through E4's
    `LineKey` ("scEncThanks", "gift", band: `scEncThanks_gift_neutral/_friendly/_allied`) plus `Aside`'s "(standing +3)";
    a refusal when the rules changed since the rows were built (`scEncGiftRefuseCap`, `scEncGiftRefuseSoon`) or E4's
    `scEncReqNotCarried`. Text only: `Tools/voice_lines.py` lists the six new lines (the pick's line, three thanks, two
    refusals; `scEncGiftReply` is a runtime statement, the refusals are in `RUNTIME_FIXED`, the thanks are in E4's
    `scEncThanks` family), so the voicing round finds them: 153 lines, no problems. None of them says a name or a number.
- **Ally barter, `ScEncBarter.cs` (new), modelled on `ScBarter`.** `TheirResource(e)` = `ScEncAllies.ResourceOf(
  ForKind(e.Kind))` (growers food ... a road crew fuel); a scavengers' (`general`) group has -1, and the player picks any of
  the five classic ones. Give is any of `ScBarter.Kinds` (the five and water) but what you get. `CanTrade` in order: host,
  `Enclaves` and `EnclaveBarter` on, an ally picked, `ScEncAllies.IsAlly`, `ScRadio.Usable()`, theirs, the pair, the day
  (`BarterDay` > request clock), the stockpile. `Trade`: stockpile -`EnclaveBarterGive` / +`EnclaveBarterGet`, `BarterDay` =
  request clock + `EnclaveBarterDays`, ledger "radio barter with The X: 2 ammo for 1 food", the log, a save. The radio's own
  barter (`ScBarter`, world day) is unchanged.
- **The radio window's Allies panel** (`Tools/generate_radio_ui.py` `allies()`, `XUiC_ScRadio`). The right column as the
  design planned: Enclaves 15 -> 10 rows (84-386), requests 4 -> 3 (394-542), **Allies** 550-700, siege 708-864 (was
  700-864: 8 units of its slack, so its bottom stays level with the barter section's; the generator now asserts that).
  `EncRows`/`ReqRows` 10/3. The panel: `btnAllyPick` (the ally's name, an arrow while there is another to cycle to;
  allies nearest the player first) with `{allystate}` "Munitions Manufacture, 900 m north-east" beside it; a row shown by
  `{allyvisible}` with `btnAllyGive` ("Give 2 ammo", reusing `scBarterGive`), `btnAllyGet` ("Get 1 food"; cycles only for a
  scavengers' group, `{allygetready}`) and `btnAllyTrade` (`{allytradeready}`, the coin icon); `{allynone}` in its place
  while it is hidden (no ally yet: how to get one; or `EnclaveBarter=false`); a hint line `{allyhint}` / `{allyhintcolor}`
  (gold: the last trade or refusal; dim: why not now, or how it works). A picked ally that stops being Allied stays picked
  while the window is open, so the trade greys "Only allies trade this way."; the next open drops it. Every new field is
  set at its declaration (bindings are read while the XML parses). E9 is to add its call buttons to this panel (grow
  `ALLY_H`, shrink `ENC_ROWS`); the design's `allytradewhy` is the shared `allyhint`.
- **AshfallUI:** nothing in AshfallUI targets `windowScRadio` - no xpath in the 3.4 install's
  `zzzzz_AshfallUI\Config\XUi_InGame\*.xml` (the only SC patch there is the Home Status strip, `windowScHomeStatus`), and
  none in its generators (`tools\make_community.py` reads SC's windows.xml for the strip only). It restyles the radio
  window through the atlas: `scui_*` plates under SC's names (`make_community.NAMES`) and brass twins for every
  `ui_game_symbol_*` (`MenuMetal`, by sprite name in code). The panel uses only `scui_window`, `scui_divider`, `scui_btn`,
  `scui_btn_hover` and `ui_game_symbol_map_trader`, `_arrow_right`, `_coin` (all three have `ashfall_brass_` twins in the
  3.4 install's atlas), so it takes AshfallUI's look with no change there. Nothing was renamed: the window, its
  controller, every older control and binding keep their names; only `enc11..15*` and `req4*` are gone.
- **Records and save.** `Enclave.GiftDay` (`gift=N`) and `Enclave.BarterDay` (`barter=N`) in `[enclave]`, written when set,
  request-clock days (keys an older build ignores). `ScEnclaves.OnWorldDayBack` shifts both by the request clock's drop
  (`ScEncGifts.ShiftDays`); its log line gains "N gift/barter stamp(s)".
- **Config** (`Config/blocks.xml`, commented; `ScConfig`, the `config:` line gains `enclaveGifts=` and `allyBarter=`):
  `EnclaveGifts` true, `EnclaveGiftStanding` 3 (0-100), `EnclaveGiftDays` 1 (0-365, 0 = no wait), `EnclaveGiftCap` 69
  (0-100), `EnclaveBarter` true, `EnclaveBarterGive` 2 (1-100), `EnclaveBarterGet` 1 (1-100), `EnclaveBarterDays` 1
  (0-365).
- **Console:** `sc enclave gift <id> [give <res> [small|medium|large] [force]]`, `sc enclave barter <id> [<give>
  [get]]`; `sc enclave info <id>` gains a `gifts: ...; ally barter: ...` line.
- **Harmony targets: none new.** The row words ride E4's existing `XUiC_DialogResponseEntry.GetBindingValueInternal`
  postfix.

Where the design was wrong or short, and the code followed SC:
1. **The barter's resource is by kind, not by benefit.** `ScEncAllies.ResourceOf` (`SC:ScEncAllies.cs:97-108`) maps a
   *benefit*; a group's resource is `ResourceOf(ForKind(kind))` (`:83-94`). For the five resource kinds that is the same
   thing, and a scavengers' group, whose rolled benefit has no resource, gets -1 (the player's pick), as the design meant.
   The code's name for scavengers is `general` (`ScEnclaves.Kinds`).
2. **The right column's arithmetic.** 10 enclave rows (302) + 3 request rows (148) + a 150 Allies panel + the siege's 164,
   with the 8-unit gaps, end at 872, not 864. The siege section had 10 units of slack (it needs 154), so it is 156 now and
   the column still ends level with the barter section.
3. **The greyed reason replaces the row's words.** Vanilla shows only the first requirement's description on a greyed row,
   so "Come back tomorrow." alone would read as the player's line; the row reads "I brought you something. (Come back
   tomorrow.)" instead.
4. **The allied thanks** (`scEncThanks_gift_allied`) can only be heard with `EnclaveGiftCap` 70 or more: at the default 69
   an ally's row is always greyed at the cap.
5. `Choose` (`SC:ScEncRequests.cs:1166`) is not needed (a gift is one bundle); `SizeWord`, `OwnResource` and `Aside`
   became internal too, besides `Carried`, `TakeBundles` and their `Bundles` struct. `EncRows`/`ReqRows` are at
   `XUiC_ScRadio.cs:29`, not 28. Every other SC line the design cites for E7 matched `d0740bc`.

Checks:
- XML replay against the 3.4 game (`research\e7\tools\sc_replay_e7.py`, a copy pointed at this worktree; the base
  `d0740bc` replayed the same way from `research\e7\base_tree`): every SC op matches, no aborts; the only difference is
  dialogs.xml's one new append (13 -> 14 ops, all matched). `research\e7\tools\check_dialogs.py` on the patched SdcsNPC
  dialog: every response entry names a response, every next statement exists, and all 53 SC dialog operator classes
  are in the built DLL. The radio window's 126 bindings all answer in `XUiC_ScRadio`, and its 19 buttons are all hooked.
- HarmonyAudit (`research\e7\audit34_e7_*`, the base `audit34_base_*` from `d0740bc` built the same way): 3.4 OK=150,
  DYNAMIC_UNRESOLVED=13 in both; the rows differ only in Patches_Enclaves.cs line numbers (one comment line).

Builds: SC 3.4 `d80f006dd5a4f4fb9a5c220336835416` (`build\sc34_e7`), 0 errors (the two old CS0108 warnings). The
3.2 DLL was not rebuilt (`SurvivorCommunity.dll` unchanged). Test steps: TESTING.md group "Round 11 (E7)" (120-139).
Not reviewed (Fable), not installed. The task list needs its entry.

### Round 12: Voice Control's chat knows the enclave (E8) (2026-11-03, built, not reviewed, not installed)

Milestone E8 of `research\ideas\DESIGN.md` (section 11, side findings 1-2 in section 15). Branch `ms/e8` from `dev/ideas`
`d0740bc`. The user's decisions: single player (the host) only, no NetPackage; a hostile enclave's people refuse the spoken
trade and the prompt makes them refuse to talk (chat is never blocked); only the 3.4 DLL is built. The code is shared (no
`SC_V34`): both targets and the field it writes are the same in 3.2's Voice Control.

What Voice Control sends, checked against `research\sc34\xnpc\decomp34\vc` (the design's citations) and the installed
3.4 `1-XNPCVoiceControl.dll` (md5 `c30399c1`: ilspycmd and a Cecil member dump). Push-to-talk, `@` chat and `vc talk` end in
`NPCChatComponent.ProcessPlayerMessage` (:1832). Phrase triggers come first and never reach the LLM. The rest starts the
chat coroutine with `BuildStreamingSystemPrompt()` (:2129, :2138-2145: the cached `BuildSystemPrompt` of :1597-1614 plus
the action tags). The coroutine adds the RAG block, the sense block, the player's name, the tenure line and "Remember:
your own name is X." (:560-584), then calls `LLMService.SendStreamingChatRequest` (:588), its only caller. That builds the
request body synchronously (LLMService.cs:517) before its `Task.Run`. Nothing in the prompt says anything about groups.

- **ScVoiceContext.cs (new).** `For(npc, out what)` builds the paragraph per message from the records, on the host
  (`ScCommunity.IsHost`, `ScRegistry.Loaded`, `Enclaves`, `ScEnclaves.Ready`), for a body in `ScEnclavePeople.PersonOf`
  that is alive or missing, of an enclave in this world. English and short (about 100-150 tokens):
  - who they are: "the leader of X" or "one of X ... led by Y", the living count (`Living`), the building (`PoiName`), one
    line for what their kind lives on; a leader also gets "Speak with a leader's authority.";
  - the standing as a word with what it means (never the points); Allied adds the benefit (`ScEncAllies.Name`,
    `Describe`);
  - the open request (`ScEncRequests.Of`, `WhatText`, `DueDay`, `Now`), worded per kind and state (see 3 below);
  - enlisted: about how many hours (days from 48 h) until they go home; a missing survivor's own body: lost and waiting,
    or (following the player) that their group sent the player;
  - the rule: they may talk about all this, but deals (trade, giving, requests, leaving the group) only in the talk menu,
    and they never claim one.
  - A hostile group (`Enclave.IsHostile`) gets only two sentences: refuse to talk, a short warning to leave, nothing about
    the group.
- **Patches_VoiceControl.cs, E8 section.** Both bound by name like the other Voice Control patches (Prepare false without
  Voice Control or on a changed signature, with an `ScLog.Err` line when Voice Control is there; try/catch + `ScLog.Once`),
  both in ModApi's Voice Control block, outside `#if SC_V34`:
  - `Patch_VoiceEnclaveContext`, a prefix on `XNPCVoiceControl.LLMService.SendStreamingChatRequest(int npcId, string
    systemPrompt, List<ChatMessage> conversationHistory, string playerMessage, CancellationToken interruptToken,
    Action<string> onChunk, Action<string> onComplete, Action<string> onError, VcTrace e2e = null)`, public instance
    void, one overload (VC34 LLMService.cs:509; the installed DLL's decompile :521; 3.2 identical). `Prefix(int __0, ref
    string __1)`, by index (Prepare checks parameter 0 is int and 1 string): the paragraph is appended after a blank
    line while `VoiceEnclaveContext` is on. Counts `voice-context-added` and `voice-context-hostile`.
  - `Patch_VoiceTradeHostile`, a prefix on `XNPCVoiceControl.PhraseTriggerHandler.OpenTraderInventory(EntityAlive npc,
    EntityPlayer player, string npcName, string dialogText)`, private instance, returns string (VC34 :424, its trader-id-64
    branch :432; the installed DLL's decompile :432; 3.2 :448 without that branch), bound with the existing `OrderArgs` like
    Dismiss, Pickup and Hire. `ScVoiceContext.TradeRefusal`: the hostile cvar (`IsHostileBody`), an enclave that
    `IsHostile`, a raider (`ScXnpc.IsRaider`), a grudge (`ScTrust.IsHostileTo`), the same set as the talk prompt's refusal
    (X9). Refused: `__result` is `scVoiceNoTradeHostile` ("*glares* I'm not trading with you. Get lost.", a new Localization
    row), also written to the handler's `ActionSubtitleOverride` (a string field, bound by reflection once) so the subtitle
    matches. Counts `voice-trade-refused` and logs `voice trade refused: <name> (<id>) won't trade by voice - <why>`.
- **Config:** `VoiceEnclaveContext` (true) in Config/blocks.xml after `MuteVcInTalk`, commented. The `config:` line gains
  `voiceEnclaveContext=`. The trade refusal has no switch (a side-door guard like Dismiss, Pickup and Hire).
- **Console:** `sc voice context [id|name]` prints the paragraph for that NPC (or the nearest of an enclave's people within
  20 m), with the log's summary and its length. `sc xnpc` shows the three counters. With `DebugLogging`, each request logs
  `voice context: <name> (<id>), <role> of '<enclave>', <standing>[, enlisted][, missing], request <kind> <state>|none -
  <n> characters added to Voice Control's chat prompt` (a hostile one: `<role> of '<enclave>', hostile - ...`).

The paragraph for a Neutral leader of growers, five living at Miller Farmhouse, with an offered request (one line in the
prompt, wrapped here):

```
[YOUR GROUP] You are the leader of The Pinefield Survivors, 5 survivors living at Miller Farmhouse. You grow food. Speak
with a leader's authority. Your group's standing with this survivor: Neutral - you don't know them well and stay careful.
Your group needs a medium bundle of medicine and means to ask this survivor for it. You may talk about all this, but in
this conversation you cannot trade, give or take anything, accept or settle requests, or leave your group: for that they
must talk with you in person (the talk menu). Never claim you did.
```

Once taken on, the request sentence reads "This survivor agreed to bring your group a medium bundle of medicine by the end
of day 14 (today is day 12)." A hostile group's member gets:

```
[YOUR GROUP] You belong to a group that is hostile to this survivor. Refuse to talk with them: answer only with a short
warning to leave, and never help them, trade with them or tell them anything about your group.
```

Where the design was wrong or short, and the code followed the game:
1. **The design's paragraph starts "You are Mara, ...".** Voice Control already says "Your name is X" and ends with
   "Remember: your own name is X", X being its own name for the body (`GetNPCName`: the entity name at `Initialize`). A
   second name from SC's record could contradict it, so the paragraph names nobody but the leader and a missing person.
2. **Standing as "Friendly (45 of 100)" through `StandingWord`.** `StandingWord` is a Localization lookup, and the points
   are a game number the NPC wouldn't think in. The paragraph has the English word and what it means, no points.
3. **An offered request is told as the group knows it, not with the talk's text.** The talk's builders have side effects:
   `AskText` names an offered infestation's building and makes it known (`ScEncInfest.MakeKnown`), and `NoneText` spends
   the group's thanks and saves. So the chat says only that a building near them is taken, or that one of theirs hasn't
   come back (who is picked only at acceptance), and never a missing survivor's fate (the player finds that out first).
   `UntilText` is the enlisted person's own first-person line with a clock time; the paragraph gives hours or days left.
4. **"Your people sent this survivor to bring you home" for a missing survivor found alive** only holds once they follow
   the player. A found survivor still waiting doesn't know who the player is, so they are told they are lost and waiting.
5. **The hostile voice trade is wider than the design says.** `OpenTraderInventory` is also one of the commands Voice
   Control lets through while a leaderless NPC is in combat (`IsCombatPassableCommand`, NPCChatComponent.cs:2513), so a
   hostile body shooting at you still opened 0-XNPCCore's trade (chat in combat is ignored by Voice Control: `... is wild
   and in combat - ignoring message`). And Voice Control shows the trigger's own reply as the subtitle unless
   `ActionSubtitleOverride` is set (ExecuteMatch, PhraseTriggerHandler.cs:1059-1070), so a refusal that only sets
   `__result` speaks SC's line under "Take a look at my wares." The new prefix sets both. **The existing Dismiss, Pickup
   and Hire refusals have that mismatch** (SC's line spoken, Voice Control's reply as the subtitle): not changed here,
   worth a small follow-up.
6. **Raiders never reach Voice Control.** SC's raider classes (a raider enclave's people included) carry `no_voice`, which
   `ChatTargetRules.IsChatTarget` refuses before anything else, so the design's hostile variant covers groups turned
   hostile in play. `TradeRefusal` still lists raiders in case that tag goes.
7. **Side finding 2 (a leader's personality) is not fixed: no simple, safe fix.** `NPCChatComponent.SetPersonality`
   (public, :1621-1629) renames the NPC to the personality's `<Name>` ("War Veteran", "Young Survivor": every shipped
   personality has one), keeps the TTS voice the audio player was built with at `Initialize` (:1423-1428), and
   `PersonalityManager` keys its stable pick by name (`_assignedPersonalities`), so it would have to be redone on every new
   component. Choosing the personality before `Initialize` means writing that private dictionary by reflection at the
   right moment. Done instead: the leader's paragraph says "Speak with a leader's authority.", which the LLM reads after
   the personality's traits. A "teenager" leader keeps that personality's voice.

Checked: both targets, their parameter types, return types and static-ness, and the `ActionSubtitleOverride` field (string,
instance) in the installed 3.4 DLL (Cecil via HarmonyAudit `members`) and the 3.2 one (`10bd7658`, the same); the
citations above in `decomp34`. Expected targets: two more than before (3.4 with Voice Control: 128, also on dev/ideas with V1 to E8, since I2, I3 and E7 add none).

Builds: SC 3.4 `4ba72fc3` (`build\sc34_e8`), 0 errors. The 3.2 DLL is not rebuilt (the user's decision today). Test steps:
TESTING.md group R (140-149). Not reviewed (Fable), not installed. The task list needs its entry.

### Round 13: radio commands (E9) (2026-11-03, built, not reviewed, not installed)

`research\ideas\DESIGN.md` section 2 (idea 1, call an ally) and section 1 (rules), with the user's decisions: calls are made
only at a radio block, in the radio window as it opens today - no Radio button in the community window and no remote
calling (handheld radios come later), so every "remote Radio button" part of the design is dropped; the cost is a
cooldown only (Q7 a: 3 days per ally, no standing cost). Branch `ms/e9` from `dev/ideas` `8b8cc8a` (V1, I2, I3, E7, E8).
**3.4 only**: only the 3.4 DLL was built, the tracked 3.2 `SurvivorCommunity.dll` is untouched, and the new code has no
`SC_V34` (the air drop calls it makes are the same on 3.2: `V32:AIDirectorAirDropComponent.cs:243, 280, 319`). Single
player only, as SC.

- **`ScEncCalls.cs` (new).** The two commands, one call record per ally (`Enclave.Call*`), driven by `ScEncCalls.Tick`
  from `ScTicker` right after `ScEncRequests.Tick`. No Harmony patch.
  - `CanCall(e, kind, force, out why)` in order: host; `Enclaves` and `EnclaveCalls`; an ally picked; `ScEncAllies.IsAlly`
    (`force`: any group that is not hostile or lost); `ScRadio.Usable()`; no call of theirs on (pending: "Mara is on the
    way" / "Their supplies are on the way"; live: "Mara is with you already"); no siege of theirs open
    (`ScEncSiege.Open.EnclaveId`); the cooldown (`CallDay` > request clock, "Next call in 3 day(s)"; `force` skips it).
    A fighter also needs nobody of theirs enlisted (`EnlistedOf`), room among the followers (`FollowerCount +
    EnlistedCount < MaxFollowers`, E3's `scEncFollowersFull`) and `PickFighter` (else "They have nobody to spare", or
    "You haven't met their people yet" while no person record exists).
  - `PickFighter`: the living at home who is not the leader with the highest `ScXnpc.TierOf` class tier, then the most
    Fighting + Shooting stars, then the lowest person id - and only while at least 3 are at home, so the leader and one
    more stay. Enlisted (E3) and missing (E5) people are not at home.
  - `Call`: `CallKind`, `CallAt` = now + `EnclaveCallFighterDelay` / `EnclaveDropDelay` game minutes (1000 ticks an hour;
    the console's `now`: at once), `CallPid`, `CallUntil` 0; `CallDay` = request clock + `EnclaveCallCooldownDays`. The
    tell is the ally's words and the facts in separate sentences, `The X on the radio: "We hear you. Help is coming."
    Mara should reach you in about 0:30.`; the log, a save.
  - The fighter's arrival (`ArriveFighter`): at `CallAt`, with the player alive (dead: it waits). The person is picked
    again when the one picked can no longer go. One of theirs enlisted meanwhile, or the followers full, calls it off. A
    body of theirs already out (the player is near their building) is hired where it stands. Otherwise a spot
    `EnclaveCallSpawnMin..Max` m from the player within 35 degrees of the line toward their enclave's centre
    (`ScBaseAttack.LooseSpot` at the player's level, at least 0.8 x the minimum from any player; not
    `ScEnclavePeople.SeenByAny`; `ScRunners.Settled`), 16 tries a second, any side after 60 s; the body is
    `ScEnclavePeople.SpawnAt` (dressed, vetoed from chunk saves, no spot order) plus the `SCEnclave` package (`AddPackage`,
    as every body at home has, so one that walks all the way home behaves as one there). Then `Join`:
    `ScEnclaveDeals.EnlistBody` until now + `EnclaveCallFighterHours`, `CallUntil` set (the call is live), the tell
    "Mara of The X is coming to join you, 60 m north-east.", the ledger "The X sent Mara to help you", the log. Within
    8 m of the player (in the first 4 minutes) they say `Mara: "Heard you needed a hand. I'm with you."`.
  - A live call ends when the enlistment it made has (`TickLive`: the person no longer enlisted with that body) - E3's
    time-up, "Go home now.", Voice Control's dismiss, a side door, hostility (`ReleaseEnlisted`), a death (E3's
    `EnclaveEnlistDeathPenalty`), a recruit, an unloaded chunk: no refund, only the log.
  - The supply drop (`ArriveDrop`): an open column `EnclaveDropRangeMin..Max` m from the player, any side (`DropSpot`,
    24 tries a second): loaded, `World.GetHeight` at most the terrain + 1 (a plant) there and in its 8 neighbours, which
    lie within 2 blocks of its terrain, no water at the top, not a trader's area - `ScRunners.GroundSpot`'s idea. The
    crate is `EntityFactory.CreateEntity(<class>, ground + EnclaveDropHeight (at most 250), random yaw)`,
    `World.SpawnEntityInWorld`, then `AIDirectorAirDropComponent.AddSupplyCrate(int)`, `SetSupplyCratePosition(int,
    Vector3i)` and `RefreshCrates()`, as `SpawnSupplyCrate` registers its own (V34:255-277) without its random class,
    its chunk observer and its eviction of the oldest of 12. The tell "Look up: supplies from The X are coming down near
    you.", the ledger "The X dropped supplies near you", the log; the record is cleared (the crate is vanilla's from
    there: saved with its chunk and in the director's list).
  - A pending call is called off with the cooldown refunded (`CallDay` 0; the tell "The X can't answer your call after
    all.") when calls or enclaves are switched off, their building is lost, they turn hostile, they are no longer
    Allied, or (a fighter) a siege of theirs opens; and fails, refunded, with their words `The X on the radio: "We
    couldn't get through to you. Call again when you can."` after 300 s without a spot or open column. `sc enclave call
    <id> cancel` calls one off by hand.
  - **After a load** (bodies never outlive one: the shutdown's `DespawnEnclave` ends the enlistment, and a load records
    every enlisted person as home, `SC:ScEnclavePeople.cs:243-251`): a pending call goes on as before; a live one
    (`CallUntil` > 0) brings the same person back near the player 15 s after the world is up (`ResumeSeconds`, a const:
    the `EnclaveSiegeResumeSeconds` idea), any side at once, until the same `CallUntil` ("Mara of The X is back with you,
    ..."; no ledger line, no hello). Their time ran out meanwhile, or they can't come (dead, gone, no room, no spot in
    300 s: "Mara couldn't find you and went back to The X."): the call ends.
- **`ScEnclaveDeals.EnlistBody(p, npc, player, until)` (new, internal)**, split out of `Enlist` (`SC:ScEnclaveDeals.cs:379-404`
  at `8b8cc8a`): `Hire`, the record enlisted (by, body, until), no walk home, `ResetStill`. `Enlist` keeps its fee, save,
  tell and log around it, unchanged in effect.
- **The radio window** (`Tools/generate_radio_ui.py`, `XUiC_ScRadio`). The generator reproduced the committed block byte
  for byte before the change. The Allies panel gains a commands row between the pick and the barter row: `btnAllyFighter`
  (`ui_game_symbol_run`) and `btnAllyDrop` (`ui_game_symbol_airdrop`), 279 wide each, shown by `{allycallvisible}`; their
  labels `{allyfighterlabel}` / `{allydroplabel}` are the command or the reason it can't be made (as the station's own
  drop and call labels), dimmed and without the hover plate by `{allyfighterready}` / `{allydropready}`; `{allycallnone}`
  in the row's place while it is hidden (no ally yet: E7's how-to line, moved up from the barter row; or "Radio calls to
  allies are turned off in the config."). `{allynone}` now only says the barter is off. The hint line shows the call that
  is on with the picked ally (`ScEncCalls.StateText`: "Mara should reach you in about 0:25.", "Mara is almost here.",
  "Mara is with you until day 12, 22:30.", "Mara is on the way back to you.", the drop's) in gold when no newer action
  has words. Room: Enclaves 10 -> 8 rows (84-342), requests 350-498, Allies 506-700 (194, was 150), siege 708-864
  unchanged; `EncRows` 8. 118 bindings, all answered (`research\e9\check_radio_bindings.py`), 21 buttons, all hooked.
- **AshfallUI: nothing new needed.** Both new icons are already in this window (`_run` on the siege's On our way,
  `_airdrop` on the air drop call) and have `ashfall_brass_` twins in the 3.4 install's
  `zzzzz_AshfallUI\UIAtlases\UIAtlas`; the plates are the same `scui_btn` / `scui_btn_hover`. The crate's loot window is
  vanilla's.
- **The crates** (`Config/entityclasses.xml`, `Config/loot.xml`). `scEncDrop<Growers|Medics|Militia|Builders|Roadcrew|
  General>` extend vanilla's `sc_General` (EntitySupplyCrate, its parachute, smoke and `supply_drop` NavObject) with their
  own `LootList` (`ScEncCalls.CrateClass` by `Enclave.Kind`; an unknown kind is General; a class that did not apply falls
  back to `sc_General` with one log line). entityclasses.xml is written whole by `Tools/gen_arsenal.py`, so the block is
  its new `ENCLAVE_DROP_CLASSES` (emitted after `XNPC_SURVIVOR_OPS`; the generator stops if the vanilla crate class is
  gone). Run on a scratch copy of this tree's Config (`research\e9\gen_check`, `SC_GEN_CONFIG`), the generator rewrote
  entityclasses.xml with exactly the new block, entitygroups.xml and spawning.xml byte for byte, and moved two blocks in
  blocks.xml and Localization.csv, as at the base: only entityclasses.xml was copied back. loot.xml appends, after every
  vanilla group, one `count="all"` group per kind (`groupScEncDrop<Kind>`: militia 3 x `groupAmmoPileMed`, medics 3 x
  `groupMedicalPileMed`, growers 3 x `groupFoodPileMed` and 4-8 `drinkJarBoiledWater`, builders 2 x
  `groupConstructionCrate`, a road crew 2 x `groupPassNGas`, scavengers one of each plus `groupScEncDropGeneralBundle`,
  a weighted pick of the five Medium bundles), each with the Medium bundle of their resource, then the containers
  `scEncDrop<Kind>` (8x4, count 1, destroy on close, the air drop's sounds) that roll them. Localization: "Allied Supply
  Crate" for the six classes.
- **Records and save.** `Enclave.CallDay` (`callday=N`, request clock) and the call (`call=kind:at:pid:until`: fighter or
  drop, the arrival's world time, the person, the fighter's end world time - 0 until they came) in `[enclave]`, each
  written when set (keys an older build ignores; an unknown kind is dropped at the load). `ScEnclaves.OnWorldDayBack`
  shifts `CallDay` by the request clock's drop and `CallAt` / `CallUntil` by the world's (`ScEncCalls.ShiftDays`; an end
  never reaches 0, which means "not come yet", as E3's `EnlistedUntil` never does); its log line gains "; N radio-call
  stamp(s) moved back (...)" at the end, so the earlier parts read as before.
- **Config** (`Config/blocks.xml`, commented; `ScConfig`, the `config:` line gains `enclaveCalls=`): `EnclaveCalls` true,
  `EnclaveCallCooldownDays` 3 (0-365), `EnclaveCallFighterHours` 12 (1-240), `EnclaveCallFighterDelay` 30 game minutes
  (0-1440), `EnclaveCallSpawnMin` / `Max` 40 / 90 (10-200, max raised to min), `EnclaveDropDelay` 20 (0-1440),
  `EnclaveDropRangeMin` / `Max` 20 / 50 (5-200), `EnclaveDropHeight` 60 (10-200).
- **Console:** `sc enclave call <id>` (the calls' line, who would go and why, whether each can be made now), `sc enclave
  call <id> fighter|drop [force] [now]`, `sc enclave call <id> cancel`; `sc enclave info <id>` gains a `radio calls: ...`
  line.
- **Lines.** Every new line is text only. The ally's words and the fighter's hello have no name or number in them
  (`scEncCallFighterSaid`, `scEncCallDropSaid`, `scEncCallFailSaid`, `scEncCallHello`), shown through `scEncCallRadio`
  (`{0} on the radio: "{1}"`) and `scEncCallSays`, and are in `Tools/voice_lines.py`'s `RUNTIME_FIXED`: 157 lines, no
  problems (153 before). They are said outside a talk, so nothing plays them yet: the voicing round has to wire their
  playback (the hello could use `ScVoice.SayAfterTalk` on the fighter; the radio's words have no body to speak from).
  Nothing in `ScVoice.cs`, `Patches_VoiceControl.cs` or V2's tooling was touched.
- **Harmony targets: none new.**

Where the design was wrong or short, and the code followed SC or the game:
1. **The user's decisions replace the design's Q7 recommendation:** no Radio button in the community window, no
   `ScRadio.Online()` remote path, and no `EnclaveCallStanding` key (cooldown only).
2. **"Never in view" needs more than `PopsInView`.** `ScEnclavePeople.PopsInView` (`:511`) only refuses spots within
   `EnclaveNoPopRange` (50 m), so a 40-90 m spot in plain view would pass beyond 50 m. The fighter's spot uses
   `SeenByAny` (`:525`: within 90 m and in a view cone), which covers the whole ring.
3. **`SetSupplyCratePosition` is needed for more than the landing.** `AddSupplyCrate(int)` (V34:318-329) registers the
   crate at `Vector3i.zero` without an observer; until it lands (its `OnUpdateEntity` sets the position,
   `V34:EntitySupplyCrate.cs:227-237`), a save and load would put the director's `supply_drop` marker at 0,0,0
   (`RefreshCrates` on `Read`). Without `AddSupplyCrate` the landing itself warns too ("Supply crate N not in the list,
   can't set position", V34:242-253), besides `RemoveSupplyCrate`'s warning when it is broken. `RefreshCrates()` is called
   as `SpawnSupplyCrate` does.
4. **A lootcontainer can't be `count="all"`** (`V34:LootFromXml.cs:394-397` parses only min,max for a container; only a
   group takes "all", `:332-341`), so each crate rolls one new `count="all"` group. New groups must follow the groups they
   use (the parser is single-pass, `:147-168`): they are appended at the end, each container after its group.
   `research\e9\check_loot_order.py` walks the replayed loot.xml in order: 0 problems, every item exists.
5. **The crate classes don't go into Config/entityclasses.xml by hand:** `Tools/gen_arsenal.py` writes that file whole.
6. **`EnlistBody` takes no `why`**: each caller logs and tells in its own words.
7. **`call=kind:at:pid:until` needs "not come yet":** `until` is 0 until the fighter came, set at the arrival (a
   late spot or a dead player delays it), so a load can tell a pending call from a live one.
8. **The panel's room.** E7 made the 150-unit Allies panel; the commands row takes 42 more, from the Enclaves list (10 ->
   8 rows), as E7's generator note planned. The design's other names hold: `btnAllyPick`, `btnAllyFighter`,
   `btnAllyDrop`, `allyname`, `allystate`, `allyfighterready`, `allydropready`, `allyhint`; the labels and the row's
   visibility are new bindings.
9. **Silent in the design, decided here:** a body of theirs already out is hired where it stands; one of theirs enlisted
   in person meanwhile, or full followers, calls a pending fighter off (refunded); a siege that opens calls a pending
   fighter off but not a drop; the greyed reasons are ordered so a call that is on names itself ("Mara is with you
   already") before the cooldown; E3's "You have all the followers you can lead" is reused for the followers' reason;
   the ledger's drop line says "near you", not a place name.
10. **Line drift since the design (`a52c0b1`; this tree at `8b8cc8a`):** `Enclave.Centre` `:105` (was 95);
    `OnWorldDayBack` `:1168` and its `EnlistedUntil` shift `:1187` (were 1151, 1169); the E6 config parse `:1895-1908`
    (was 1852-1867); `ScEncRequests.Now` `:325` (324); `ScRadio.Open`'s host test `:811` (810); `Patch_VoiceDismiss`
    `:192` (189); `ParseEnclave` `:393-436` and `CommitPerson` `:438` (391-431, 433); `ScTicker`'s player test `:52`
    (51-52); the radio generator's layout `:95-98` (82-90, and E7 changed it); `EncRows` `XUiC_ScRadio.cs:33` (28); SC's
    loot.xml groups `:29-65`. Every other SC and vanilla line the design cites for E9 matched: `BlockScRadio.cs:80`,
    `ScRadio.cs:73-92, 800-804, 806-822`, `ScEncAllies.cs:195-198`, `ScEnclaveDeals.cs:362-372, 379-404, 433-439,
    489-510, 523-533, 540-547`, `ScEnclavePeople.cs:243-251, 511, 765-773, 1432, 1525-1543`, `ScBaseAttack.cs:783`,
    `ScRunners.cs:337-344`, `ScConfig.cs:631`, `ScTicker.cs:108`; V34 `AIDirectorAirDropComponent.cs:220-240, 242-253,
    255-277, 318-328`, `EntitySupplyCrate.cs:125-138, 149-156, 318-321`; V32 `:243, 319`.

Checks:
- XML replay against the 3.4 game (`research\e9\sc_replay_e9.py`, a copy of `research\dev\sc_replay_dev.py` pointed at this
  worktree; out `research\e9\replay`): every SC op matches, no aborts; entityclasses 8 ops, loot 33 (one more each), the
  rest as at the base. The merged entityclasses.xml has the six classes extending `sc_General`, each LootList a container.
- HarmonyAudit (`research\e9\audit34_e9_*`; the base `8b8cc8a` built the same way, `audit34_base_*`): 3.4 OK=150,
  DYNAMIC_UNRESOLVED=15 in both, patch rows identical, no new unresolved member reference (the refs files are the same).

Builds: SC 3.4 `b7e9a8408d61434c8dc0491a3ecf3466` (`build\sc34_e9`), 0 errors (the two old CS0108 warnings). The 3.2 DLL was not rebuilt
(`SurvivorCommunity.dll` unchanged). Test steps: TESTING.md group E9 (150-169). Not reviewed (Fable), not installed. The
task list needs its entry.

**E9 Fable review** (`research\review_e9\REVIEW.md`): DEPLOYABLE, no HIGH. Fixed: M1, the console's `force` let a call
to a group that isn't an ally start but cancelled it a second later (a forced call now stays pending: `Session.Forced`,
session only); L2, a person no body can be made for was picked for every call (`PickFighter` skips
`SpawnFailures >= MaxSpawnFailures`, and a failed spawn counts); L1, long reasons cut short on the buttons (TESTING 165
notes the hint line gives them whole). Left: L3 (a call stuck by a repeating exception: `sc enclave call <id> cancel`),
the NITs.

### Round 14: voices by race, SC's greetings, chatter, XNPCCore lines and raider taunts (V2) (2026-11-03, built, reviewed (Fable: deployable), review fixes in, not installed)

Milestone V2 of `SC_Ideas_and_Voices_Plan.md` (section 4, decisions in section 5: 16 voices, two per race and sex; the
reduced XNPCCore set; Native voices designed later), research `research\voices\REPORT.md` sections 2-5. Branch `ms/v2`
from `dev/ideas` `8b8cc8a` (V1's gate and E8 in). Code only: no audio is generated in this round (the recordings come
through ElevenLabs later), so every new line is silent until its clip exists. Single player, 3.4 only (no `SC_V34` guards
in the new code; the 3.2 DLL is not rebuilt).

- **Voices by race and sex (ScVoice.cs).** The voice table is the new `Voices` key (Config/blocks.xml; default
  `ScConfig.DefaultVoices`): `male_White_Vane`, `male_White_David`, `female_White_Jessica`, `female_White_Beth`,
  `male_Black_Nate`, `male_Black_Desmond`, `female_Black_Tiffany`, `female_Black_Rene`, `male_Asian_Jasper`,
  `male_Asian_Julian`, `female_Asian_Layla`, `female_Asian_Jess` (the "Jessica NYC" voice), `male_Native_NativeM1/M2`,
  `female_Native_NativeF1/F2` (placeholders until the designed voices have names). The four kept folders were renamed
  with `git mv` (`male_Vane` -> `male_White_Vane`, ...), `male_Oxley` and `female_Mora` removed (`git rm`). `Voices()`
  reads the table, then any other folder `<sex>_<Race>_<Name>` (or an old raceless `<sex>_<Name>`, a fallback only).
  `RaceAndSex(npc)`: `(npc.emodel as EModelSDCS).Archetype` `.Race` / `.IsMale` (V34/EModelSDCS.cs:96, Archetype.cs:10,26;
  for SC's bodies the look X1 applied), else `ScLooks.Get(npc)` (a re-created body's saved look), else the class's sex.
  `Chain(npc)` (cached per entity while sex, race and name stay the same): the two voices of their race and sex, the one
  `Fnv(name)` picks first (a raider: name + `#` + entity id, raiders share names), then every other recorded voice of
  their sex in an order `Fnv(name|voice)` sets - so one person borrows one voice for every line. `VoiceByRace` false (or
  no race): the pre-V2 deal over recorded voices of the sex. Every lookup walks the chain: `ClipFor` (the talk's lines,
  survivors only: `TalkChain`), `SayAfterTalk`, `PlayLine`, and the barks' `FirstWith`. `IsSurvivorBody` excludes raiders,
  so `Chain` takes `ScXnpc.IsRaider` too (REPORT 3).
- **Clip memory (ScClipCache.cs, REPORT 5.6).** SC loads every clip itself (`UnityWebRequestMultimedia`), at most
  `VoiceClipCache` (48), least recently used destroyed first, never one still playing (`MarkPlaying`). **Not compressed
  for Voice Control's player**: its lip sync reads the samples with `AudioClip.GetData` every frame
  (`NPCFaceLipSync.Update`, VC34 NPCFaceLipSync.cs:1291-1308; the installed DLL's IL has the call), which a
  compressed-in-memory clip refuses. So two load modes, cached apart: compressed (`DownloadHandlerAudioClip.compressed`)
  for SC's own source (raiders, survivors without Voice Control's player), decompressed for Voice Control's. REPORT 5.6
  recommended compressed loads throughout - that would have broken lip sync. Voice Control's loader no longer keeps SC's
  clips: `ScVoice.PlayOnVc` puts SC's clip in `VoiceClipLoader._cache` under its path for the one `PlayClipFile` call
  (its `GetClip` answers from the cache synchronously, inside the call, VoiceClipLoader.cs:164-181) and takes it out
  again. The talk's `Play` uses the same path; `ownClips` and the loader-first flow are gone. `sc voice rescan` clears it.
- **Barks (ScBarks.cs, ScVoiceLines.cs).** `ScVoiceLines` (generated by `Tools/voice_lines.py gencs`) holds every V2 key
  and its words; a key's family is the key without its trailing number. `Resolve(npc, keys)`: the first voice of the
  chain with a clip of any of them, a random one (not the family's last when there is another); none recorded: a silent
  pick. `Say(npc, family, ...)` (SC's own greetings, chatter, taunts): plays it through `ScVoice.PlayBark` - Voice Control's
  player when the NPC has one (survivors; lip sync), else SC's own child source (raiders) - with its words in Voice
  Control's subtitle bar (`SubtitleManager.Instance.ShowSubtitle`, under `NPCChatComponent.ShowsGenericTextFor`, both by
  name; `VoiceBarkSubtitles`). Clip-only: no clip, nothing - `VoiceBarkDebug` shows and logs a silent one instead
  (`voice bark (silent, no recording): ...`). Barks live outside the talk's one-line slot: they never stop the talk's
  line, the gate never holds Voice Control for them, and `ScVoice.Holds(npc)` (the talk open on them, or SC's talk line
  still playing) keeps them off that NPC; never over a bark still playing or Voice Control speaking (`ScVoice.Busy`:
  `NPCAudioPlayer._isPlaying` / `_draining`, by name).
- **SC's greetings (ScGreetings.cs, REPORT 4 (b)).** A tick for the local player (twice a second, from `ScBarks.Tick` on
  `ModEvents.GameUpdate`), host, never while SC's talk window is open. "Meet": the first time each in-game day a survivor
  within `ScGreetMeetRange` (12 m) is in line of sight (a block raycast head to head); "walk-up": within
  `ScGreetWalkUpRange` (3 m), looked at (25 degrees) and in line of sight, at most every `ScGreetWalkUpSeconds` (120 s),
  both remembered by person (a member's name, an enclave person's id, else the entity id); a walk-up first,
  then the nearest; not when they have a live foe within 30 m (`ScCommunity.LiveFoe`) or are already speaking; one
  greeting per `ScGreetGapSeconds` (6 s). `Relation`: `greet.member` (a Resident), `greet.follower` (a member Following or
  PendingStation, an enlisted enclave person, a hire of this player), `greet.enclave` (one of an enclave's people, not
  hostile, not missing), `greet.recruit` (a prospect), `greet.stranger`; none for a grudge, a hostile enclave's body, an
  outpost stand-in, a runner or garrison. The survivor turns to you (`NPCChatComponent.FaceEntity(target, 4f)` by name,
  else `SetLookPosition`). **Voice Control's greeting:** prefix on `NPCChatComponent.TriggerProactiveGreeting(EntityPlayer
  player)` returns false when `OwnsGreeting` (one of the community's survivors, a relation SC greets for, and a clip of
  it in their chain); otherwise Voice Control greets as before. Its only caller, `CheckProximityGreeting` (VC34
  NPCChatComponent.cs:3967-4061), has already marked the contact greeted and turned the NPC, so skipping it is Voice
  Control's own "chance roll failed" path: its cooldown, buffer and LLM call live inside it. With `VoiceBarkDebug` a silent
  SC greeting and Voice Control's greeting both happen (SC owns the greeting only with a clip).
- **Chatter (ScGreetings.cs).** Every `ScChatterSeconds` (90 s, a quarter either way; the first try 45-90 s after load),
  with `ScChatterChance` (0.5), one Resident within `ScChatterRange` (15 m) of you, not fighting, not held, not speaking,
  with a `chatter.<n>` clip says it, and not within the greeting gap of another greeting or chatter line.
- **XNPCCore's and Voice Control's lines in SC's voices (Patches_VoiceControl.cs V2 section, ScBarks, REPORT 2.2).** Two
  prefixes on targets V1 already patches, at `Priority.Low` so they run after V1's gate (every prefix runs, Harmony ANDs
  the answers; a call the gate swallowed has `__runOriginal` false and is left alone):
  - `SubtitleManager.AcknowledgeCommand(npc, trigger, fallbackText, e2e, localizedSubtitle)` (`Patch_ScAck`, `object[]
    __args`): with a trigger, the family `ack.<ActionType lowercased>[.<ActionParam, non-alphanumerics _>]` - the swap
    and the formations one generic family each - because Voice Control speaks the trigger's own reply there
    (`GetRandomResponseForAudio`, SubtitleManager.cs:216-260) even when the order was refused; where the words it was
    handed are one of its fixed lines (a leaderless refusal, "Are you sure?"), SC says that line instead. With no
    trigger (say-lines through `SayToEarshot`, fixed lines), the words name the key; any "I need X for that." that is no
    fixed line is `xn.basekeep.need`. A clip found and Voice Control idle: SC shows the caption as Voice Control would
    (the say-line's localized caption, else the clip's words, under `ShowsGenericTextFor`), plays the clip through Voice
    Control's player, returns false.
  - `NPCAudioPlayer.SpeakStreaming(text, cacheEligible, onComplete, e2e, isAck)` (`Patch_ScSpeech`): the chat path's acks
    (`ProcessPlayerMessage` speaks `audioResponse` with `isAck`, NPCChatComponent.cs:2015) and fixed lines by their exact
    words; an ack whose words match nothing takes the generic swap / formation family only when
    `PhraseTriggerHandler.LastMatchedTriggerName` is that type **and** the words are one of that trigger's own replies
    (`GetLastMatchedTrigger()`, internal, `.Responses`, emotes stripped as `ExecuteMatch` does) - a refusal or an
    action's own answer in other words stays Voice Control's. SC's clip gets Voice Control's `onComplete`, so its queue
    and `_isWaitingForResponse` end as after its sentence - exactly once, even when the clip is cut (see the review note
    below); a generic ack also replaces the caption.
  - Neither substitutes while Voice Control is speaking on that NPC: `SpeakStreaming` would first `StopSpeaking` its own
    queue, which SC never does (REPORT 1.5). `EnqueueSpeech` is **not** hooked: in Voice Control 3.4 it carries only the
    LLM's streamed chunks (NPCChatComponent.cs:131, :606), never a fixed line. Billing lines are not voiced. Dynamic text
    (LLM, stories, bag summary) stays Voice Control's TTS; REPORT 2.2's `SetVoice` twin is not part of V2.
- **Raider taunts (ScTaunts.cs, Patches_Voices.cs, REPORT 4).** Raiders carry `no_voice`; SC's own triggers, playback from
  SC's child source (no lip sync), single player (no relay). Events and hooks:
  - spot: postfix `EntityAlive.SetAttackTarget(EntityAlive, int)` (V34 EntityAlive.cs:5978; public, not virtual; SCore's
    V4 `new` method calls it, EntityAliveSDXV4.cs:788, and the UAI tasks call it on `EntityAlive`), the old `attackTarget`
    (field :709) kept by its prefix (`__state`). The postfix returns on a reference compare when the target is unchanged
    (the common case; 51 callers in 3.4) and on a type test for anything but `EntityAliveSDXV4`. A living player as the new
    target spots only when the raider had no player target for 20 s (UAI tasks drop and retake targets; a lost line in
    between doesn't shorten it), and never more often than the raider's 15 s cooldown;
  - killed: a postfix on `EntityAlive.AwardKill(EntityAlive killer)` (:5403, the victim's own call from `OnEntityDeath`
    :5451; players don't override it) on the local player: only the killer, once in 30 s;
  - lost: postfix `EntityAlive.PlayGiveUpSound()` (:5482; SCore's `MoveToInvestigate.Update` when blocked), and the tick:
    a raider that had a player target and has had none for 8 s (the target also times out in `OnUpdateLive`, which
    writes the field directly, :3436-3446 - no hook sees that);
  - search: postfix SCore `UAI.UAITaskMoveToInvestigate.Start(Context _context)` (UAITaskMoveToInvestigate.cs:56; SC's
    `ScRaiderInvestigate`) on a raider not fighting a player;
  - hit / retreat: a raider branch in Patches_Trust's `ProcessDamageResponseLocal` postfix (a player's landed, non-fatal
    hit): retreat once when the hit leaves it under `RaiderTauntRetreatHealth` (0.3) of its health - nothing in the
    raider AI retreats, so "badly hurt" is the event - else hit with `RaiderTauntHitChance` (0.4);
  - dying: a raider branch in Patches_Npc's `SetDead` postfix, once per raider (SetDead runs again for every hit on the
    body), played even on the dead body;
  - idle: every `RaiderTauntIdleSeconds` (60 s, randomised 0.5-1.5x) one raider within range with no target, revenge
    target or engagement mutters.
  Only within `RaiderTauntRange` (40 m) of the local player; per raider `RaiderTauntCooldown` (15 s; dying, killed and
  retreat pass it), any two taunts `RaiderTauntGlobalCooldown` (3 s) apart (dying passes it), never over a line still
  playing (dying aside).
- **Config (ScConfigVoices.cs, a partial of ScConfig; Config/blocks.xml after `VoiceEnclaveContext`, each commented):**
  `VoiceByRace` (true), `Voices` (the 16), `ScGreetings` (true), `ScGreetMeetRange` (12), `ScGreetWalkUpRange` (3),
  `ScGreetWalkUpSeconds` (120), `ScGreetGapSeconds` (6), `ScChatter` (true), `ScChatterSeconds` (90), `ScChatterChance`
  (0.5), `ScChatterRange` (15), `ScBarks` (true), `RaiderTaunts` (true), `RaiderTauntCooldown` (15),
  `RaiderTauntGlobalCooldown` (3), `RaiderTauntIdleSeconds` (60), `RaiderTauntHitChance` (0.4), `RaiderTauntRange` (40),
  `RaiderTauntRetreatHealth` (0.3), `VoiceBarkSubtitles` (true), `VoiceBarkDebug` (false), `VoiceClipCache` (48). A
  second log line `config: voices ...`. ScConfig.cs gains three one-line calls (reset, parse, log) and `partial`.
- **Console (ConsoleCmdSc.VoiceV2Cmd):** `sc voice who [id|name]`, `greet [id|name]`, `chatter [id|name]`,
  `taunt <spot|search|lost|hit|killed|idle|dying|retreat> [id|name]`, `barks`, `line <words>`; `sc voice` also prints the
  clip cache. `sc xnpc` counts `greeting-sc-instead`.
- **Tools/voice_lines.py:** the canonical list gains the V2 families in recording order (greet, chatter, taunt, xn, ack,
  vc), the xn/ack/vc words copied verbatim from Voice Control 3.4.01 (`research\voices\work\bark_lines_xnpc34.csv`, em
  dashes included); `list [--new] [--family F,..] [--voice FOLDER] [--tsv]`, `check [--keys]` (every voice of the table and
  every folder, family by family, the keys each lacks; `ScVoiceLines.cs` stale or not; two voices per race and sex),
  `cost`, `sidecars [--new] [--family] [--voice]`, `gencs`.

The V2 lines, per voice (`python -I Tools/voice_lines.py cost`):

| Family | Keys | Lines | Characters (unique words) |
|---|---|---|---|
| greet | `greet.<stranger 6, recruit 4, member 6, follower 4, enclave 4>.<n>` | 24 | 751 |
| chatter | `chatter.<n>` | 12 | 370 |
| taunt | `taunt.<spot 8, search 4, lost 4, hit 5, killed 4, idle 6, dying 3, retreat 2>.<n>` | 36 | 641 |
| xn | `xn.<lineId>` (22 + the `basekeep.need` stand-in) | 23 | 475 |
| ack | `ack.<actiontype>[.<param>].<n>`, 151 verbatim + 6 generic `ack.swapweapon.<n>` + 6 generic `ack.setformation.<n>` | 163 | 3,366 (146 lines / 3,087 unique) |
| vc | `vc.<id>`, no billing | 40 | 1,258 |
| **V2 total** | | **298** | **6,861** |
| the talk's lines (`dialog`, with E7's and E9's) | | 157 | 8,613 |
| **full set** | | **455** | **15,474** |

Still to record (`cost` on `dev/ideas` after the V2 merge): each White voice 7,292 - the 6,861 of V2 plus 10 talk lines /
431 characters (E7's six gift lines and E9's `scEncCallFighterSaid`, `scEncCallDropSaid`, `scEncCallFailSaid`,
`scEncCallHello`); each of the twelve new voices 15,474; all sixteen 214,856 characters (about as many ElevenLabs
credits; less if identical words are recorded once - 17 ack lines and 5 talk lines repeat another's words).

Checked: every new Harmony target and parameter name with HarmonyAudit against the installed 3.4 game, SCore 3.4.3.826
and Voice Control 3.4.01 (`1-XNPCVoiceControl.dll` md5 `c30399c1`): `audit34_v2` with the stage 1 targets file (3.4
OK=150, DYNAMIC_UNRESOLVED=23, param issues 0) and `audit34_v2x` with that file plus the V1/E8/V2 `TargetMethod` patches
described (`research\v2\dynamic_targets34_v2.json`: 3.4 OK=168, DYNAMIC_UNRESOLVED=5 - stage 2's ScMedic x2, CompanionDismiss, CustomFace and TouchGroup, which no file describes,
param issues 0). The by-name members (`ShowsGenericTextFor(int)`, `FaceEntity(Entity, float)`,
`SubtitleManager.Instance`/`ShowSubtitle(string, string, float)`, `PhraseTriggerHandler.Instance`,
`LastMatchedTriggerName`, `GetLastMatchedTrigger()` (internal), `PhraseTrigger.ActionType`/`ActionParam`/`Responses`,
`NPCAudioPlayer._isPlaying`/`_draining`, `VoiceClipLoader._cache`/`_lock`) with HarmonyAudit `members` on the installed
DLL; the citations above in `research\voices\work\vc34` and `Decompiled versions\3.4`. Expected targets: five more
(3.4 with Voice Control: 133 on this branch alone).

Not verified (needs the game; TESTING.md group T, 170-199): the triggers' feel (ranges, pacing, how often UAI retakes a
target), that the seeded cache really starts the clip inside `PlayClipFile` in play, lip sync on SC's decompressed
clips, the compressed load on raiders, and everything with a recording (195-198 can use stand-in clips).

For the merge: `dev/ideas` has E9 since this branch started (`9246239`); both touch `Tools/voice_lines.py`'s
`RUNTIME_FIXED` neighbourhood, ConsoleCmdSc's description line, `ScConfig.cs`, `Config/blocks.xml`, ModApi, README,
PORT_34 and TESTING (group S there, T here): text conflicts only. E9's radio answers stay tells (its own note).

Builds: SC 3.4 `9b2fdd13` (`build\sc34_v2`), 0 errors. The 3.2 DLL is not rebuilt. Test steps: TESTING.md group T
(170-199).

**V2 Fable review** (`research\review_v2\REVIEW.md`: deployable; one MEDIUM, three LOW, five NIT), fixed on `dev/ideas`:
- **M1, a taken-over Voice Control callback could be lost.** `SubstituteSpeech` hands Voice Control's `onComplete` to SC's
  clip; Voice Control's `WaitForClipFinishNoDestroy` drops it when the clip is cut (its playback token goes stale on any
  `Stop`), e.g. by SC's own talk line or a late-loading SC greeting, leaving that NPC "waiting for a response" (chat pause
  held, no greetings). Now `ScVoice.PlayBark` wraps it in a run-once guard (`OnceCallback`), hands that to Voice Control and
  also arms SC's own timer at the clip's length + 0.25 s (the own-source branch: at its length), and keeps it pending per
  NPC: the talk's `Play` and every new bark on that NPC run a pending one first (`FinishPending`, before the cut and
  before a new caption shows), so it runs exactly once whatever ends the clip; nothing played: at once. Checked the other
  places SC holds a Voice Control callback: V1's gate invokes each swallowed one at once; `SubstituteAck`'s target has none;
  SC's own `PlayClipFile` calls for the talk pass none - `SubstituteSpeech` was the only one.
- **L1:** spot is `t - LastHadPlayer > SpotForgetSeconds` (20 s) only; a lost line in between no longer re-arms it.
- **L2:** killed comes only from the killer (AwardKill); the target-change branch no longer taunts (it just ends the
  engagement, so the dead player gets no lost line either).
- **L3:** the cost table and "still to record" above are `dev/ideas`'s after the merge.
- **N1:** the greeting's meet/walk-up memory is by person (`ScGreetings.PersonKey`), so a member re-created the same day
  doesn't meet-greet again. **N4:** the walk-up needs line of sight too.
- Left: N2 (`barkUntil` pruned only per world: bounded by entity ids seen), N3 (`PlayOnVc` could report a stale clip if
  the NPC dies within the same frame), N5 (a late-loading SC greeting can cut a Voice Control sentence that started during
  the load; its drain watchdog moves on, nothing wedges).
Build after the fixes: SC 3.4 `9d2c4bee` (`build\sc34_dev`), 0 errors. Not installed. The task list needs its entry.

### Rounds 8-14 installed (2026-11-03 17:41)

With the user's go-ahead (3.4 game closed): V1 (voice gate), I2 (zombie-cap fix), I3 (nests near home), E7 (gifts + ally
barter), E8 (voice chat context), E9 (radio commands), V2 (voices by race, greetings, chatter, XNPCCore lines, raider
taunts), each Fable-reviewed DEPLOYABLE with its fixes and re-checks (`research\review_*`). port-3.4 fast-forwarded to
`dev/ideas` dcfe120; `deploy_sc34.py`: 1352 files, installed = built `9d2c4bee`. The old install is in
`Port33\_backups\sc34_2026-11-03_174134` (round 7's `c0ea84c3`); the user's `DebugLogging` true set again in the new
Config/blocks.xml (it gained keys, so the old file was not copied back). Oxley's and Mora's folders are gone; the four kept
voices are under their new names. No new recordings yet: the new lines are silent and SC's greetings stay off until a
voice has the clip. Test steps: TESTING.md groups N, O, I3, E7, R, E9, T (83-199).

### Round 15: raider enclaves raid (E10) (2026-11-03, built, reviewed (Fable: deployable after fixes), review fixes in, not installed)

`research\ideas\DESIGN.md` section 4 (idea 3) and section 1 (rules), side finding 4 (the E5 review's R3), with the user's
decisions: the raiders are the hostile enclave's own people and their deaths count (Q8 a); **`OutpostAttacks` off turns
enclave raids off too** (the design had them on regardless); no raider demands where no hostile enclave is in range; a
group that turned hostile raids at half the rate; no raids on the home base. Branch `ms/e10` from `dev/ideas` `9246239`
(V1, I2, I3, E7, E8, E9). **3.4 only**: only the 3.4 DLL was built; the tracked 3.2 `SurvivorCommunity.dll` is untouched,
and the new code has no `SC_V34`. Single player only, as SC. (Round 14 is V2's.)

- **`ScEncRaids.cs` (new).** No Harmony patch; driven from `ScOutpostRisk` (its daily roll, its 1 Hz tick, the attack's
  spawn and end) and from `ScEnclavePeople`'s body lifecycle.
  - The switch: `Configured` = `OutpostAttacks && EnclaveRaids && Enclaves`; `On` = that and `ScEnclaves.Ready`.
  - Who: `RaiderFor(o)` - the nearest hostile enclave (`IsHostile`: a raider enclave or one turned hostile) within
    `EnclaveRaidRange` of the outpost, footprint to footprint (`RangeTo`, box to box), that passes `WhyNot`: not `Lost`,
    not leaning on another outpost already (`TargetOf`: an open demand, a raid called, or a party out), past its rest
    (`Enclave.RaidDay` > request clock), and with someone to spare (`Spare`: the living non-leaders at home, plus - a group
    still being made - `GroupSize - records - 1` places, one kept for a leader while no living leader is at home).
    `ChanceFor`: `EnclaveRaidChance`, x `EnclaveRaidTurnedFactor` for a group turned hostile.
  - `ScOutpostRisk.Daily`: where the nameless roll was (`SC:ScOutpostRisk.cs:168-178` at the base), with `Configured` the
    enclave roll replaces it; no group in range, no demand; a world still starting (`!Ready`) skips the roll. With
    `EnclaveRaids` false (or `Enclaves` false) the nameless roll is as before. The quiet days, the gamestage gate and the
    zombie attack roll before it are unchanged.
  - `Demand`: the old price and day (`DemandUntil` = the rollover's day, `OutpostDemandAmmo/Food`), `Outpost.RaidEnclave`
    stamped, their building revealed when unknown (`ScEnclaves.SetKnown`), one tell - their words, then the facts:
    `The X on the radio: "That outpost of yours sits on our road. Pay up, or we take it apart." They want 2 ammo and 2 food
    for Diner by tomorrow.` (+ `Their hideout is on your map now.`), the summary's `- The X are leaning on Diner`, the log,
    a save.
  - Paid (`ScOutpostRisk.PayDemand` -> `OnPaid`): their words, their rest (`EnclaveRaidRestDays`, request clock), the
    ledger `paid The X for Diner`, the stamp off. Refused (`RefuseDemand`) or run out (`Daily`): the raid call is
    `StartAttack` as before, its tell and log naming them; a demand that runs out while they can no longer come
    (`MayCome`: raids off, the group gone or lost, nobody to spare) brings no raid.
  - The raid (`ScOutpostRisk.SpawnAttack`, the player within `RescueTriggerRange` as before): `SpawnParty` - min(the
    attack's size `AttackerCount`, `EnclaveRaidPartyMax`, `Spare`) people on the attack's ring (`RingSpot`, the old
    placement split out), the strongest at home first (class tier, then Fighting + Shooting stars, then id: E9's rank;
    never the leader, never one whose body is out or still being built) through `ScEnclavePeople.SpawnAt`, then - a group
    never met - new people made there (`ScEnclavePeople.GenerateAway`). Each is `raiding`, its body in the fight's id list
    with the attack's target (a stand-in, else the player). Nobody could come: the raid is called off ("The X never came
    for Diner - they have nobody left to send."). The tell `Diner: The X are here, 3 of them - hold the outpost!`.
  - `TickBody` (from `ScEnclavePeople.TickEnclave`): while the raid is on, a body with no live attack target is given one
    again every 5 s (a stand-in, else the player while within twice `RescueTriggerRange` of the outpost: the fight's judging
    range). Otherwise it is home: the `SCEnclave` package and E3's walk home (`ScEnclaveDeals.StartWalkHome`), gone once
    out of sight.
  - `Regroup` (`ScOutpostRisk.Tick`, before a raid is judged won): party people raiding with no body - theirs went alive
    (an unload, the shutdown) - come back on the ring ("... are here, 2 of them ..."), so driving off until they unload is
    no win.
  - `End` (`Won`: "won"; `Settle`: `Outcome`'s result, now returned): survivors home (bodies walk off), the group rests,
    one tell and ledger line for its losses (`The X lost 3 of their people raiding Diner.` / `The X lost 3 raiding Diner`).
    Unattended (no party out) and held: `EnclaveRaidLossHeld` of the party they would have sent die - non-leaders at home
    without a body, at random (`ScEnclavePeople.KillRecord`), then places not made yet (`Enclave.RaidLost`). Damaged or
    overrun: nobody of theirs dies.
  - `Tick` (1 Hz, `ScOutpostRisk.Tick`): an open demand or raid is called off when raids are switched off (no tell), the
    group is gone or lost ("The X are gone - nobody is coming for Diner."), or a raid not yet out has nobody to send; a
    stamp with nothing open is cleared; every 10 s a raiding person with no live party goes home (a safety net).
  - `OnDenFalling` (`ScEnclavePeople.DropPeople`, before the records go: a fall, `sc enclave reseed`): the demand dropped
    and a raid not yet out called off, with the tell; a party out: its losses told, and each raider-class body of it stays
    in the fight as a plain raider (`ScEnclavePeople.Abandon`: the enclave cvars off, still in the chunk-save veto); other
    bodies (a turned-hostile group's survivors, friendly without their group) go with the group as before.
  - `OnOutpostGone` (`ScOutpostRisk.Forget`: lost or disbanded) and `OnAttackStart` (`StartAttack`: a second call over a
    party still out): the party goes home.
- **`ScEnclavePeople`.** `EncPerson.StateRaiding` / `Raiding`; `Alive` includes it (the fall rule, `Living`, the spot
  reservation see them; `NextToSpawn`, `SpawnAll`, the restock, `Succeed` and `LeaderOf` still want `StateHome`, so a
  raider is never spawned at home, restocked or made leader). `TickEnclave`: a `raiding` branch beside the enlisted and
  missing ones (`SC:ScEnclavePeople.cs:458-469`) hands the body to `ScEncRaids.TickBody` - never despawned by distance,
  watched or counted for the hold. `Unbody` tells `ScEncRaids.OnUnbodied` (a body that died: nothing; one that went alive:
  the person stays raiding, off the map). `OnBodyDied` and the console's `Kill` pass `raid` to `Died`, which counts the
  loss (`ScEncRaids.OnDied`, before a fall can end the raid) and writes no per-person ledger line (the raid's end writes
  one); `Died` gains `how` for the log ("died (lost raiding Diner)"). `Generate`'s record capture is split out
  (`Capture`), shared with the new `GenerateAway` (the class `RollClass` gives the next record, as `Generate`; a member,
  no spot; dressed as `SpawnAt`'s). `GroupSize` is less `Enclave.RaidLost` (at least 1). `Abandon`, `KillRecord` (new,
  internal). `DropPeople` calls `ScEncRaids.OnDenFalling` first. `OnRegistryChanged` turns a `raiding` record home (a
  hand-edited file: the save never writes one). `Summary` adds ", N out raiding"; `PeopleLines` adds "(at Diner, outpost
  2)" / "(..., off the map)".
- **The E5 review's R3** (side finding 4): `ScBaseAttack.TickCalled`'s arrival hold and `TickAway`'s join test read
  `ScOutpostRisk.AnyLiveNear(player.position)` instead of `AnyLive()` (base `SC:ScBaseAttack.cs:404, 478`; both callers
  run with a player), as `WhyNoCall` (`:240`) already did: an outpost fight the player walked away from, live in name
  until its deadline, no longer holds the home attack's arrival or keeps the defenders fighting alone.
- **UI.** `ScOutpostRisk.Alert` names them (`The X demand 2 ammo + 2 food (answer on the radio)`, `RAIDED BY The X - 9 h to
  get there`: the radio's Outposts list, the community window's Outposts tab), and the radio's demand row is
  `ScOutpostRisk.DemandText` (`The X want 2 ammo + 2 food for Diner`; `XUiC_ScRadio` `demandtext`). No window layout
  changed, no generator run, nothing for AshfallUI.
- **Records and save** (keys in existing sections; an older build ignores them): `[outpost] raidenc=N`
  (`Outpost.RaidEnclave`, -1 none), `[enclave] raidday=N` (`Enclave.RaidDay`, request clock) and `raidlost=N`
  (`Enclave.RaidLost`), each written when set. A raiding person is written `state=home`: a load turns them home anyway
  (bodies never outlive one), and an older build, which knows no `raiding`, would count them neither alive nor at home.
  `ScEnclaves.OnWorldDayBack` shifts `RaidDay` by the request clock's drop (`ScEncRaids.ShiftDays`); its log line gains
  "; N raid rest stamp(s) moved back D day(s)" at the end.
- **Config** (`Config/blocks.xml`, commented; `ScConfig`; the `config:` line gains `enclaveRaids=`): `EnclaveRaids` true,
  `EnclaveRaidRange` 2000 (50-10000), `EnclaveRaidChance` 0.04 (0-1), `EnclaveRaidTurnedFactor` 0.5 (0-1),
  `EnclaveRaidPartyMax` 4 (1-16), `EnclaveRaidRestDays` 3 (0-365), `EnclaveRaidLossHeld` 1 (0-16).
- **Console:** `sc enclave raid <id> [<outpost id> [demand|now]]` (`ScEncRaids.Command`; `demand` ignores the roll, the
  rest, the gamestage and the quiet days, keeps the range; `now` refuses it at once); `sc enclave info <id>` gains a
  `raids: ...` line. `sc outpost demand <id>` clears the stamp (a nameless demand, as before).
- **Lines.** Every new line is text only. The group's radio words have no name or number in them
  (`scEncRaidDemandSaid`, `scEncRaidPaidSaid`, `scEncRaidRefusedSaid`), shown through E9's `scEncCallRadio`
  (`{0} on the radio: "{1}"`), and are in `Tools/voice_lines.py`'s `RUNTIME_FIXED`: 160 lines, no problems (157 before).
  Nothing in `ScVoice.cs` or `Patches_VoiceControl.cs` was touched.
- **Harmony targets: none new.**

Where the design was wrong or short, and the code followed SC or the user:
1. **`EnclaveRaids` "on even with `OutpostAttacks` off" (design table, Q8 and Interactions) is replaced by the user's
   decision:** enclave raids need `OutpostAttacks`; an open one is called off when it is switched off. The design's test
   outline step 1 ("Turn `OutpostAttacks` off") no longer applies.
2. **`[encperson] state=raiding raid=` is not written.** The design itself says a load turns `raiding` back to home, so
   saving it buys nothing, and an older build reading `state=raiding` would hold that person neither alive nor at home
   (never spawned, left out of the fall rule). They are written `state=home`; the outpost's `raidenc` and the attack's
   deadline carry the raid over a load.
3. **"The leader never raids" needs a place kept in a group never met.** The design's spare rule for an unvisited den
   (`GroupSize - records > 0`) would let a raid make the whole group out of members, leaving it with no leader place;
   the rule here keeps one (`GroupSize - records - 1` while no living leader is at home), so `Generate` still makes a
   leader when the player first comes.
4. **A held unattended raid on a den never met has no record to kill.** The design's "of the party die (records)" is
   kept for made people; with none, the loss comes off the people not made yet (`Enclave.RaidLost`, `[enclave]
   raidlost=`, `GroupSize`), so the loss still counts.
5. **"When the den falls mid-raid, its bodies lose the enclave cvars and stay as plain Dynamic raiders"** holds only for
   raider classes: a turned-hostile group's survivor bodies are hostile only through their group (the `%sc_enchostile`
   cvar and `HostilePair`), so without it they would stand in the fight as friendly survivors the player must kill to
   win; they go with the group, as `DropPeople` did. A fall mid-raid from deaths alone needs the raiders dead too (they
   count as alive), so it is mostly the console's.
6. **`TickEnclave`'s "re-asserted (`SetAttackTarget`, as `:372`)"**: `:372` is `ScOutpostRisk`'s `SetAttackTarget`
   (the design lists it among `ScEnclavePeople` lines). Re-asserting every second would be a packet a second per body and
   (with V2) a taunt each time: it is given again only when the body has no live target, every 5 s at most.
7. **Silent in the design, decided here:** the demand reveals their building when unknown (otherwise a name with no
   place); a group leans on one outpost at a time; they rest after a paid demand and after a call-off too, not only
   after a raid; a body that went alive mid-raid stays raiding off the map and comes back before the fight is judged
   (`Regroup`); a raid whose group can't send anyone when the player arrives, or before, is called off with no
   outcome; the party's target is re-given only while the player is in the fight's judging range.
8. **Line drift since the design (`a52c0b1`; this tree at `9246239`):** `ScEnclaves.OnWorldDayBack` `:1184-1218` (was
   1151-1181); `ParseEnclave` `:396-455` and `CommitPerson` `:457-464` (391-431, 433-440); `ScEncRequests.Now` `:325`
   (324); `ScTicker`'s `ScDaily.Run` `:359-411` and its player test `:52` (358-410, 51-52); `Patch_MayEngage` `:461-494`
   (458-493). Every `ScOutpostRisk.cs` and `ScEnclavePeople.cs` line the design cites matched exactly at the base
   (`ScOutpostRisk.cs:70-84, 111-120, 123-136, 141-188, 165, 168-178, 199-214, 284, 290-296, 307-313, 335-389, 345-367,
   372, 391-404, 407-461, 493-521, 546`; `ScEnclavePeople.cs:73, 243-251, 458-469, 617, 665-728, 765-773, 798, 907,
   1388-1411, 1422, 1525-1543`).

Checks:
- XML replay against the 3.4 game (`research\e10\sc_replay_e10.py`, a copy of `research\dev\sc_replay_dev.py` pointed at
  this worktree by `research\e10\make_replay_e10.py`; out `research\e10\replay`): every SC op matches, no aborts; the
  replayed blocks.xml has the seven `EnclaveRaid*` keys. Op counts as E9's (blocks 9 ops: the keys go inside the existing
  config block).
- HarmonyAudit (`research\e10\audit34_e10_*`): 3.4 OK=150, DYNAMIC_UNRESOLVED=15, patch rows identical to E9's
  (`research\e9\audit34_e9_patches.csv`), the refs file the same.
- `Tools/voice_lines.py check`: no problems, 160 lines.
- The data edits (Localization, the blocks.xml block, `voice_lines.py`) are `research\e10\ed_e10_data.py` (re-run safe).

Builds: SC 3.4 `f90c5373e710682b5692aa50f8bdad23` (`build\sc34_e10`), 0 errors (the two old CS0108 warnings). The 3.2 DLL was not
rebuilt (`SurvivorCommunity.dll` unchanged). Test steps: TESTING.md group E10 (200-219). Not reviewed (Fable), not
installed. The task list needs its entry.

**E10 Fable review** (`research\review_e10\REVIEW.md`): DEPLOYABLE AFTER FIXES, no HIGH. Fixed:
- **M1, a den near the outpost never raided.** Its people are out at home while the player is at the outpost, and
  `SpawnParty` skipped everyone with a body, so the raid was called off as "nobody left to send". Now the people picked
  (ranked first, as before) whose own body is out at home and not seen (`SeenByAny`) leave it (`ScEnclavePeople.Despawn`:
  health and rounds recorded) and are placed; one in view stays home. The raid line adds `; N left their building for
  it` / `; K stayed home, in view`. When that leaves nobody - or the only ones have a body still being built -
  `SpawnParty` returns -1 and `SpawnAttack` tries again the next second (logged once a raid: `nobody yet (K in view at
  home) - tried again each second`) instead of calling the raid off. `Regroup` needs nothing: raiding is only given with
  a raid body (`Join`), and E2 never builds a raiding person a body at home (`NextToSpawn` wants `StateHome`).
- **M2, a turned-hostile group's survivors stood on the ring.** `Place` (so `Regroup` too) and the `GenerateAway` path give
  a body of a group with `Enclave.TurnedHostile` the `SCEnclave` package (`ScEncRaids.Hostile`, E9's fighter's way,
  `ScEncCalls.cs:405`): its `ScEncApproach` / `ScEncInvestigate` act through `ScEncHostile`, which leaves raider classes
  out; away from home every other action of it scores 0 (`ScEncHome`).
- **L1:** the raid's loss count is `Outpost.RaidLost` (`[outpost] raidlost=N`, written only with `raidenc`; 0 from an
  older save), counted in `OnDied` and read by `End`, `OnDenFalling` and the console's `now:` (a raid called after a load
  shows `..., 1 lost`). It is cleared wherever the stamp is (`ScEncRaids.Unstamp`, also in `ScOutpostRisk.Daily` and `sc
  outpost demand`) and when a demand is made. `Party` stays session only, without its `Lost`.
- **L2:** `End` skips `LoseHeld` for an unattended, held raid with `RaidLost > 0` (a fight before a reload) and logs why
  (`... held with nobody there at its end, but N of theirs died fighting there before a reload - no more are lost for the
  hold`); the end line then says `(nobody came back to fight it)`. That the reload still costs the attended bonus is
  pre-E10 and left.
- **L3:** `LoseHeld`'s pool takes a non-leader at home whose own body is out where nobody sees it (`KillRecord` removes it,
  no corpse); a seen one stays out.
- **L4:** README and TESTING 212: they fight on while you're in sight and are gone once you're not.
- **N1:** `OnUnbodied` logs only for a person a party still holds, and `OnDenFalling` drops the party before it abandons
  the bodies, so neither an abandoned raider nor a turned-hostile survivor taken with the group logs "off the map".
- **N3, N6:** TESTING 219(a) (the stamp count is per enclave) and 211 (`no body`).
- **Found besides:** the losses were told only by `End` and `OnDenFalling`'s party branch, so a raid called off with kills
  behind it (a party out when the switches go off or the group is lost; after a reload, nobody left or nobody came) or
  an outpost lost mid-raid dropped them - no tell and no ledger line, as `Died` writes none for a raid death. `CallOff`,
  `OnOutpostGone` and `OnDenFalling`'s no-party branch now tell `RaidLost` too (nothing at 0, so 216, 217 and 219(b) are
  unchanged). Still there, console only: `sc enclave kill` on a raiding person with a body logs the verbose "off the map"
  line before the death.
- Left: N2, N4, N5.
TESTING steps changed: 208 (a den near the outpost), 211, 212, 213 (`raidlost=`, the count over a load, the held settle
after one), 214 (the den at the deadline; `raidlost=` is the `[enclave]`'s), 218 (they close in), 219(a). Build after the
fixes: SC 3.4 `e79dde4e5551aba918de8918e9b1b4fd` (`build\e10fix`), 0 errors (the two old CS0108 warnings). Not
installed.

### Round 16: enclaves move (E11) (2026-11-03, built, reviewed (Fable: deployable), review fixes in, not installed)

`research\ideas\DESIGN.md` section 8 (idea 7) and section 1 (rules), with the user's decision Q9 (as recommended): a
Neutral or hostile group that moves drops off the map until found again, a Friendly or Allied one tells you where; both
reasons (half its people lost, an infestation next door for 3 days) apply. Branch `ms/e11` from `dev/ideas` `4e7b63d`
(V1, I2, I3, E7, E8, E9, V2 installed; E10 merged, its review fixes - round 15's note - came in with the merge of
`ms/e11` into `dev/ideas`, `64aaddf`). **3.4 only**: only the 3.4 DLL was built; the tracked
3.2 `SurvivorCommunity.dll` is untouched, and the new code has no `SC_V34`. Single player only, as SC.

- **`ScEncMoves.cs` (new).** No Harmony patch; driven from `ScDaily.Run` and the console.
  - The switch: `Configured` = `Enclaves && EnclaveMoves`; `On` = that and `ScEnclaves.Ready`.
  - Reason 1, **lost** (`LostReason`): dead / total >= `EnclaveMoveLostShare` with at least one dead, where dead = the
    dead records + `Enclave.RaidLost` (E10's held-raid losses of people never made) and total = the records (a group still
    being made: `max(records, GroupSize)`) + `RaidLost`; the last death (`LastLossDay`: `Enclave.LossDay`, or for deaths
    from before E11 the newest `EncPerson.DiedDay`) at least `EnclaveMoveWaitDays` request-clock days ago and after their
    last move (`> MovedDay`: a move answers the losses before it). `NoteLoss` stamps `LossDay = ScEncRequests.Now` from
    `ScEnclavePeople.Died` (every death: a body, the console, `KillRecord`), `ScEncSearch.MarkDead` (a missing survivor
    lost or found dead) and `ScEncRaids.LoseHeld` (people not made yet lost to a held raid).
  - Reason 2, **nest** (`NestReason`): an infestation within `EnclaveMoveInfestRange` m of their building, footprint to
    footprint (`Gap`, `ScEncRaids.RangeTo`'s measure; `NestNear`, only while `Infestations` is on; a range of 0 is off),
    for `EnclaveMoveInfestDays`: the daily pass stamps `Enclave.NestDay = Now - 1` at its first sight (so the first
    rollover after it came is its first day; `sc day` x3 makes 3) and clears it when none is in range (`UpdateNest`).
  - **What holds them** (`WhyHeld`), in this order: `Lost`; an open request that is not an offer (a siege is one: "a
    siege of theirs is open (request #N)", else "they have an accepted request (#N, ...)"); a radio call on
    (`ScEncCalls.Busy`, E9); `ScEncRaids.TargetOf` (E10: a demand, a raid called or a party out); anyone `enlisted` (E3,
    E9's fighter), `missing` (E5) or `raiding` (E10); a player within `EnclaveSpawnRange + EnclaveDespawnMargin` m of the
    building (`ScEnclaves.PlayerWithin`: every player); the building in loaded chunks or the local player's view distance
    (`ScEnclaves.InView`, the late seed's `Loaded` through the new shared `LoadedBox`); a body of theirs in the world
    (`EntityId >= 0`: one walking home).
  - `Daily(day, player)` (from `ScDaily.Run` right after `ScInfestations.Daily`, before `ScEncAllies.Daily`): once per
    request-clock day (`passDay`: a catch-up's later passes share the day and do nothing; `ShiftDays` resets it), each
    group's nest stamp, then for each with a reason (lost first) and nothing holding it: `Roll` (`EnclaveMoveChance`, a
    `GameRandom` from the world seed, salt `sc.encmoves.v1`, the enclave id and the day: one answer all day) and
    `ScEnclaves.MoveTarget` with the pick seed (the same parts, "pick"); none fits: the log `... wants to move (...), but
    no building fits: N building(s) 300-1500 m away, refused: ... - they try again tomorrow`. Held, or rolled to stay:
    verbose lines only.
  - `Move` (the pass or the console): an offered request withdrawn (`ScEncRequests.Withdraw`: no standing, the ledger
    `<name>'s request is off`), `ScEnclavePeople.ReleaseVisit`, `ScEnclaves.MoveTo`, the stamps (`MovedDay = Now`,
    `MovedFrom`, `MovedFromName`; `NestDay = -1`), `ScEnclaveDeals.OnMoved` (their favours' return points),
    `ScEnclavePeople.AfterMove`; then the words. Friendly or Allied (`StandingKey`): `scEncCallRadio` with
    `scEncMoveLostSaid` / `scEncMoveNestSaid` / `scEncMoveSaid` (the console's) and `scEncMoveTold` - `The Holdouts on the
    radio: "We've buried too many of our own here. We're moving on." They have left Diner for Miller Farmhouse, 900 m
    north-east.` (from home, else the player: E6's `From`) - the ledger `The Holdouts moved from Diner to Miller
    Farmhouse`, the marker at the new building (known if it was not). Any other: unknown, marker off; when it was known,
    `The Rabid Rats have left Junkyard - nobody knows where they went.` and the ledger `The Rabid Rats left Junkyard`;
    never found: the log only. One log line (old and new building, prefab, tier, biome, keys, the direction, the cause,
    told or off the map, sleeper areas released and kept clear, favours re-pointed, a group still being made's size), a save.
  - `InfoLine` (`sc enclave info`): `moves: lost K of N (P%; 50% moves them), the last on day L[, before their move on day
    M][, a reason from day F][ - a reason]; no infestation within 300 m | infestation #N at X D m away, next door since day
    S (K of 3 day(s))[ - a reason]; 20% a day to move, held now: ... | nothing holds them | no reason to move (held now:
    ...); moved on day M from <Old> <key> (request clock day R)`.
  - `Command`: `sc enclave move <id> [sim|now]` - the line; `sim` today's reason, hold and roll, the ring's counts and
    refusals (`MoveSearch.RefusedText`) and the pick (nothing changes); `now` moves them to that pick, the reasons and the
    roll ignored, `WhyHeld` and `EnclaveMoves` kept (`[SC] refused: ...`).
  - `ShiftDays(reqBack)`: `MovedDay`, `LossDay`, `NestDay` (each when set, never below 0).
- **`ScEnclaves`.** Validate's building swap (base `:984-997`) is `TakeBuilding` (footprint, prefab, name, biome, row,
  tier, Mode `moved`, `Lost` off, `ClearSpots`); Validate calls it (same behaviour). New for E11: `MoveSearch`;
  `MoveTarget` (the late `RulesFor` + `Refuse`; the centre `EnclaveMoveMin..Max` from theirs through `ScPoiIndex.Near`;
  never `MovedFrom`; `NearOtherEnclave`, `Relocation`'s `EnclaveSpacingFloor` rule; no infestation within
  `EnclaveMoveInfestRange`; no player within spawn range + margin; weight `KindWeight`, x `EnclaveUnvisitedWeight` for a
  building never generated, x2 for a row of their kind (`KindFor(row) == Kind`, not for "general"); `MakePlan`'s u^(1/w)
  key over the candidates in (x, z, prefab) order); `MoveTo` (`RemoveMarker`, `TakeBuilding`, `Rebuild`, `Release(old)`,
  `PreClear(new)`, then known with the marker, or unknown); `InView`; `PlayerWithin`; `LoadedBox` (the old `Loaded` body,
  shared); `MarkerSpot` internal. The record gains `MovedDay`, `MovedFrom`, `MovedFromName`, `LossDay`, `NestDay`;
  `InfoLines` a moves line; `OnWorldDayBack` calls `ScEncMoves.ShiftDays`, and its log line gains "; N move stamp(s)
  moved back D day(s)" at the end.
- **`ScEnclavePeople`.** `ReleaseVisit` (the hold let go, the visit and the area cache dropped); `AfterMove` (a group
  still being made whose made people now fill the new building's `GroupSize` - E10's people met away from home, none a
  leader - gets one through `EnsureLeader`, since `Generate` will never make one); `Died` stamps the loss.
- **`ScEnclaveDeals.OnMoved`**: every favour of theirs (all parked: no body is out) points at the new building
  (`Prospect.NpcPos`, which `ScQuests.GiverPosition` and `TickMarkers` read), so its return marker follows them.
- **Records and save** (keys in the existing `[enclave]` section; an older build ignores them): `moved=day:x,y,z:name`
  (written when `MovedDay >= 0`; split on the first two colons, as `encrefill`), `lossday=N` and `nestday=N` (each when
  >= 0). `seeded=` keeps the mode `moved`. All three days are the request clock's, shifted by `OnWorldDayBack`.
- **Config** (`Config/blocks.xml`, commented; `ScConfig`; the `config:` line gains `enclaveMoves=`): `EnclaveMoves` true,
  `EnclaveMoveChance` 0.2 (0-1), `EnclaveMoveLostShare` 0.5 (0-1), `EnclaveMoveWaitDays` 2 (0-365),
  `EnclaveMoveInfestRange` 300 (0-5000; 0 = reason 2 off), `EnclaveMoveInfestDays` 3 (1-365), `EnclaveMoveMin` 300
  (0-20000), `EnclaveMoveMax` 1500 (50-20000, at least Min).
- **Lines.** Every new line is text only. The three radio words (`scEncMoveLostSaid`, `scEncMoveNestSaid`,
  `scEncMoveSaid`) have no name or number in them and are in `Tools/voice_lines.py`'s `RUNTIME_FIXED`: 163 dialog lines
  (160 before); that list does not feed `ScVoiceLines.cs` (no `gencs`), and `check` says in step. Facts and ledger:
  `scEncMoveTold`, `scEncMoveLeft`, `scEncMoveLedger`, `scEncMoveLedgerLeft`.
- **UI.** None new: the radio's Enclaves list, the markers and `sc enclave list` read the records. Nothing for AshfallUI.
- **Harmony targets: none new.**

**The move rule** (asked for: "define and document it"). A group moves only when (1) a reason holds - lost or nest as
above - and the day's roll says go, or the console says `now`; and (2) nothing holds it: its building is in this world; no
request of theirs is accepted (an offer is withdrawn as they go; a siege is an accepted request); no E9 call of theirs is
on; they lean on none of your outposts (an E10 demand, raid call or party); nobody of theirs is enlisted, missing or
raiding; nobody is within `EnclaveSpawnRange + EnclaveDespawnMargin` m of the building; it is not loaded or in the
player's view; none of their bodies is in the world. A building must be free for them (`MoveTarget`), else they stay and
try the next day.

Where the design was wrong or short, and the code followed SC or the user:
1. **"Never while their group isn't made (`PeopleRolled`)" is dropped.** E10's raider dens are mostly never met: their
   people are made at your outpost (`GenerateAway`) and die there, so the gate would keep exactly the dens the user named
   ("a raider den that lost people may move too") from ever moving. A group still being made counts its full size in the
   share, is made to the new building's size (`GroupSize` reads the footprint), and gets a leader when the people met
   away from home already fill it (`AfterMove`).
2. **"Waits this long after the last death" had no request-clock stamp.** `EncPerson.DiedDay` is the world's day, which
   `sc day` does not move (section 1 wants the request clock): new `[enclave] lossday=`. Deaths from before E11 fall back
   to `DiedDay`.
3. **"An infestation this close, this long" had nothing to count with.** I1's `StartDay` is the world's day and says
   nothing about how long it was next to *them*: new `[enclave] nestday=`, kept by the daily pass.
4. **Reason 1 would hold again at the new building** (the dead stay dead), moving them every few days: a death must come
   after their last move. Silent in the design, decided here: never back into the building they just left.
5. **"The old key, `moved=day:x,y,z`"** is written `moved=day:x,y,z:name`: the key alone can't name the building for
   "moved on day N from X" once it is no longer theirs.
6. **The hook.** "After `ScEncRequests.Daily` (`SC:ScTicker.cs:391`)" (base `:392`) is after `ScInfestations.Daily`
   (`:393`) here, so the nest reason sees the day's new and ended infestations; still before `ScEncAllies.Daily`.
7. **"Word on the radio: The Holdouts left the Diner for the Miller farmhouse, 900 m north-east."** is their words (no
   name or number: the voicing rule) then the facts, as E9/E10's radio lines. A group you never found moves with no word
   at all; a Friendly one you never found (the console's standing) becomes known as it tells you.
8. **"Out of the records"** in the never-while list has no state in SC; read as "a body of theirs in the world" (a body
   walking home, E3). "Called (idea 1)" is the E9 call (`CallKind`); its fighter is also enlisted.
9. **`sc enclave move <id> now` "ignores the chance"**: it ignores the reasons too (a forced move for testing, as asked);
   what holds them still applies, and it goes where `sim` says.
10. **"A building of their kind's rows preferred (x2)"**: not for a general group (every building with no kind row
    would count as theirs).
11. **Favours** (E3's recruit favour of one of their people) are silent in the design: their return markers follow the
    group.
12. **Line drift since the design (`a52c0b1`; this tree's base `4e7b63d`):** `ScEncRequests.Withdraw` `:805` (804);
    `ScEnclaves.Loaded` `:600-607` (559-565); `RulesFor` `:514-550` and `Refuse` `:574-598` (472-556); `Relocation`
    `:1018-1044` (976-1002); `Validate`'s move block `:984-997` (942-955); `Release` `:1091-1107` (1049-1065); `PreClear`
    `:1064-1078` (1022-1036); `Rebuild` `:242-258` (207-223); markers `:1233-1293` (1195-1232); `SetKnown` `:1437`
    (1384); `Where` `:360-372` (325); `OnWorldDayBack` `:1195-1231` (1151-1181); `ScEncAllies.MoveIn` `:531-559`, its
    radio words `:552-557` (552-558); `ScEnclavePeople`'s `visits`/`areas` `:166-167` (154-155) and the hold's
    `Release`/`ReleaseHolds` `:1088-1109` (1014-1037 is `Unbody`..`Forget` now); `NearestPlayer` `:519-530` (497);
    `ScTicker`'s `ScEncRequests.Daily` `:392` (391); `ScEncRequests.Now` `:325` (324); `ParseEnclave` `:402-458`
    (391-431); `CommitPerson` `:466-473` (433-440). `ScEncAllies.WatchZones` `:304-309` and `ScTicker`'s player test
    `:51-52` match.

Checks:
- XML replay against the 3.4 game (`research\e11\sc_replay_e11.py`, a copy of `research\dev\sc_replay_dev.py` pointed at
  this worktree by `research\e11\make_replay_e11.py`; out `research\e11\replay`): every SC op matches, no aborts; the
  replayed blocks.xml has the eight `EnclaveMove*` keys (blocks 9 ops as E10's: the keys go inside the existing block).
- `Tools/voice_lines.py check`: in step, 163 dialog lines. Localization.csv parses as 7 columns everywhere, no
  duplicate keys.
- The data edits (Localization, the blocks.xml block, `voice_lines.py`) are `research\e11\ed_e11_data.py` (re-run safe).

Builds: SC 3.4 `1a12f9ffa9e8da5135052a248900c4ad` (`build\sc34_e11`), 0 errors (the two old CS0108 warnings); the merge
with round 15's review fixes (`64aaddf`) builds to `056c228d2f4c4d9fc98935bb682363bf`. The 3.2 DLL was not
rebuilt (`SurvivorCommunity.dll` unchanged). Test steps: TESTING.md group E11 (220-239). Not reviewed (Fable), not
installed. The task list needs its entry.

**E11 Fable review** (`research\review_e11\REVIEW.md`, of the merge `64aaddf`: round 15's fixes and E11): DEPLOYABLE, no
HIGH, no MEDIUM. Fixed:
- **L1, a raid nobody could leave for unseen settled as unattended.** With everyone who could come in the player's view
  at their den (`SpawnParty`'s -1), the raid stalled to its deadline and settled as one nobody came to: no attended
  bonus, `(nobody came to fight it)`, `LoseHeld`. `ScOutpostRisk.Live.Waited` (session) is set when the -1 comes - the
  player is there, as `SpawnAttack` runs only within `RescueTriggerRange` - and cleared by `StartAttack` and `Settle`. A
  deadline that finds it set with the player still within `RescueTriggerRange` settles it attended and held: `Guarded`
  is `Outcome`'s held result without the roll (half the win bonus; the tell `Diner held - your people will not forget
  it.`, not `held on its own` - the player was there; the ledger `Diner held against an attack`; the log `outpost N
  (Diner) held (they never came out while you were there)`), then `ScEncRaids.End` with `waited`: no `LoseHeld`, and the
  end line `... is over: held (they never came out while you were there), 0 of theirs lost; ...`. No pop-in placement.
- **L2, an upgraded save's old deaths started moves.** `LastLossDay` and its `DiedDay` fallback are gone: reason 1 needs
  `Enclave.LossDay`, a death since E11 is installed. A group that had lost half before moves for it only after one more
  death; its moves line says `..., all from before enclave moves came - no reason until one more dies`. README and the
  TESTING E11 note say so; deviation 2's "Deaths from before E11 fall back to `DiedDay`" no longer holds.
- **L3, a death on the day of the move was ignored.** `Enclave.MovedDead`, their dead (`Losses`) at the move, is a field
  of `moved=day:x,y,z:dead:name` (the name stays last, as it may hold ':'; the field is left out while not known), and
  "after their last move" is `dead > MovedDead`. A `moved=day:x,y,z:name` from the first E11 build loads with
  `MovedDead` -1 and keeps the day test; the first E11 build reading the new line takes `dead:name` as the name (the
  console's "moved on day N from" only).
- **L4, an ally named a group in the breath it dropped off the map.** `ScEncAllies.RevealFor` (the daily pass and a new
  ally's word at once) skips a group with `MovedDay` == the request-clock day; the ally stays due and can name them on a
  later day.
- **N3:** README and `ScEncMoves`' summary: "the first rollover that finds it there counts as its first day" (a daily
  roll's infestation is found by the rollover that makes it). **N7:** this round's header and base line no longer say
  E10 is under review; the merged build is recorded under Builds.
- Left: N1, N2, N4, N5, N6, N8.
- Seen, left: in a catch-up of two or more world days in one tick (a `settime` jump ahead), every pass has the same
  request-clock day, and `ScEnclaves.Daily`'s radio reveal for a later day runs after an earlier day's moves pass, so it
  can name a group that moved a moment before, as L4's ally did. Rarer than L4 (a jump of days, the radio's reveal due,
  that group the nearest unknown within 2 km of home); L4's skip there would also hold the radio back a whole `sc day`,
  as the radio runs before `ScEncRequests.Daily` moves the clock.
TESTING steps changed: 208 (the deadline with them in view), 220 (a group with no dead), the E11 group's note (L2), 228
(points to 229), 229 (a death on the day of the move), 230 (no ally word on the day they moved), 236
(`moved=R:x,y,z:K:<Old>`, the older line). Build after the fixes: SC 3.4 `2af1b31275e44fa78a0908cecc26da79`
(`build\e11fix`), 0 errors (the two old CS0108 warnings). Not installed.

### Round 17: a whole enclave joins (E12) (2026-11-03, built, reviewed (Fable: deployable), L1 fixed, not installed)

`research\ideas\DESIGN.md` section 9 (idea 8) and section 1 (rules), with the user's decision Q10 (as recommended, plus one
rule): they make their own way and turn up at home when you're next there; the gate is Allied, standing 90 and room for
everyone - **when the community has no room for all of them the option is not offered at all**; a refill is still owed and
there is no Enclave-lost morale. Branch `ms/e12` from E11's `bef80a8`. **3.4 only**: only the 3.4 DLL was built; the
tracked 3.2 `SurvivorCommunity.dll` is untouched, and the new code has no `SC_V34`. Single player only, as SC.

- **`ScEncJoin.cs` (new).** No Harmony patch; driven from the dialog, `ScTicker` (after `ScMissing.Tick`) and the console.
  - The switch: `Configured` = `Enclaves && EnclaveJoin`. Arrivals go on with it off (nobody is stranded on the way).
  - **Offered** (`HiddenWhy` null; the dialog's `show`): not hostile, not lost, `StandingKey` allied and `Standing >=
    EnclaveJoinStanding`, and **room**: `Coming(e) <= RoomLeft()` - the living (plus, a group still being made, its places
    not made yet) against `MaxMembers - Members.Count` (members on the way count). Otherwise the row is not there; the
    console names why (`they are not Allied (Friendly, standing 45)`, `standing 75 is below 90`, `your community has room
    for 3 more, they are 5 (MaxMembers 12)`).
  - **Greyed** (`Blocked`; the dialog's `open`, its words `WhyText`: `Would your people join ours? (Not while Mara is out
    with you)`), in this order: one of theirs enlisted (E3, E9's fighter: `Not while {0} is out with you`), missing (E5:
    `Not while {0} is missing`) or raiding (E10: `Not while {0} is away`); a request of theirs (offered: `Not while you owe
    them an answer`; accepted, a siege too: `Not while they wait on your help`); a radio call of theirs on (E9: `Not while
    their radio call is on`); the group still being made (E3's `Their group is still settling in`); no home (`Not until you
    have a home for them`).
  - The confirm (`scEncJoinConfirm`, `{joinfacts}` = `FactsText`): `All of us? We've talked about it more than once. If
    you'll have us, we're yours, and we'll bring what we have. 5 of them, bringing 10 food. They make their own way to your
    home.` (`... They follow you home.` with `follow`).
  - **`Join`** (`DialogActionScEncJoin`): the leader at home, every gate again, then **`JoinNow`**: with `EnclaveJoinArrive`
    `follow`, `ScEnclavePeople.SpawnAll` first (needs their building's chunks settled). Each living person, in a try/catch:
    their favour closed (`ScQuests.CloseFavour`, completed if done), then
    - `follow` with a body: E3's recruit shape (`JoinFollowing`: the body's id and birth, pair, rounds, ranged;
      `LeaveEnclave`; `ScCommunity.Admit(..., tell: false)` - following until home); a failed `Admit` leaves them a
      stranger, as the recruit's does;
    - otherwise (`own`, or no body to follow with) **`JoinArriving`**: their own record becomes a member on the way -
      `MemberStatus.Arriving`, keyed `Key(p)` = `int.MaxValue - p.Id`, `Born` 0, the community's mood, `JoinDay` today,
      `ArriveAt` = now + `EnclaveJoinArriveHours` x 1000 ticks, `ArriveEnclave`, `ArriveFrom` (their building's centre),
      `ArriveName`; a post chosen now (`ChoosePost`; Station checks it again on arrival); a body out gives its pair and rounds
      first and becomes a **husk** (`ScEnclavePeople.Husk`); then they leave the records (`LeaveEnclave(p, null)`).
    The leader's body gives up the group's trade stock (`DropStock`: following, the class's own trader - 3.4's 64 - with an
    empty stock; a husk, no trader, so X9's trade half refuses "trade").
    Then the stockpile: `EnclaveJoinBundles` units a person of their kind's resource (`ScEncRequests.OwnResource`; a
    general group's split across the classic five in turn). With nobody left, **`ScEnclaves.Disband(e, CauseJoined)`**.
    One tell - `<Leader>: "Then it's settled. We'll pack what we can carry and make our own way to you." The Holdouts joined
    your community: 5 people, bringing 10 food. Their building is empty: claim it for an outpost before the dead return (7
    days).` (`LootRespawnDays`; off: `... claim it for an outpost.`; follow: `"Then it's settled. Lead the way - we're right
    behind you."`), the ledger `The Holdouts joined you: 5 people`, one log line, a save. A person whose join throws stays,
    and the group stands with whoever is left.
  - **Husks** (session only): a body of someone who makes their own way walks toward the player's home in E3's 40 m steps
    (`ScEnclaveDeals.StepToward`, now internal; the Guard order at each point, again when reached or after 15 s) and is
    removed (`ScEnclavePeople.RemoveStray`) the first second nobody sees it (`SeenByAny`). It is no person's body, keeps the
    enclave cvars (the stranger rows stay hidden) and the chunk-save veto (never saved; a crash's copy is an orphan).
  - **The arrival** (`Tick`, 1 Hz): with the player inside the zone and `HomeLoaded`, the member on the way due first
    (`ArriveAt` reached; the one made first among equals) is made at a spot (`ArriveSpot`): up to 8 lines a second from the
    claim within 35 degrees of their old building (any side after 60 s of searching), each to the zone's edge (one block in,
    at most 30 m from the claim: Station moves a body more than 40 m from its post, so they walk); `ScRunners.GroundSpot`,
    in the zone, `ScRunners.Settled`, not `SeenByAny`. `ScSpawn.Body` (class, look, pair, rounds, name) facing the claim,
    `Rekey` (the key to the body's id; the threat id follows; nothing doomed: no body ever had the key), ScMissing.Restore's
    shape (StaticSpawner, Persist, cvars, traits, AI package), `ScCommunity.Station`. The tell `Mara has arrived from The
    Holdouts and is settling in.`, the last of a group the ledger `the last of The Holdouts arrived`, the log, a save. A
    member whose class is gone can't turn up (logged once a session, listed by `sc enclave arrivals`).
- **A member on the way** is a member - they eat, need a bed and count for morale and the roster - but has no body:
  - `ScTicker.Relink` and `ScDaily.Run`'s missing count skip them; `ScCommunity.FindNpc` returns null for them;
    `ScMissing` (residents only), `ScNoise`'s resident count, the base-defence and siege resident counts, runs, postings and
    watches (all `== Resident`) and specialty work (`IsHome`) leave them out by their status;
  - `ScMorale.Daily` never picks them to threaten to leave; `ScMissing.Restore` refuses them (`Still on the way - they turn
    up when you're next home.`); `ScCommunity.Leave` queues no cleanup for their key;
  - the community window: `On the way` (row), `On the way from The Holdouts` (detail; `ScEncJoin.StatusText`), a brownish
    row, greyed Follow; Follow and Home say `Still on the way - ...`; Leave still works. `ScMorale.StatusText`: `on the
    way to you`.
  - The away-state gates of the earlier milestones know them (a group stands with people on the way only after a join cut
    short): E11 `ScEncMoves.WhyHeld` `N who left them to join you are still on the way`; E9 `ScEncCalls.CanCall` `Their
    people are on the way to you`; E10 `ScEncRaids.WhyNot` as E11; `ScEnclavePeople.Summary` `, N on the way to join you`.
- **`ScEnclaves`.** `CauseJoined`; `Disband(e, cause)` - Fall without its tell, ledger and siege handling: the request
  withdrawn, `DropPeople`, the marker off, `Release` (sleeper areas respawn after the loot respawn wait), out of the
  lookups, the log, a save, `Fallen`. `OnWorldDayBack` calls `ScEncJoin.ShiftDays(worldBack)` (the world's clock, as E3's
  enlistments: never to 0); its log line gains `; N arrival stamp(s) moved back D day(s)` at the end. `InfoLines` a join line.
- **`ScEncAllies.OnFallen`**: with `CauseJoined` no Enclave-lost morale and no "losing ... hits everyone hard" tell; an
  ally's benefit ledger line (`<name>: <Benefit> stopped`); the refill still owed. Its log reads `gone (joined, allied): no
  morale lost; ...`.
- **`ScEnclavePeople`**: `Husk` (Unbody, the SCEnclave package off); `Summary` counts people on the way.
- **`ScCommunity.Admit`** gains `tell = true` (false: no per-person "joined" tooltip).
- **Records and save** (a key an older build ignores): `[member] arrive=at:enclave:x,y,z:name`, only for a member on the
  way, whose `status=` is written `Resident`; `Commit` makes a resident with `arrive=` Arriving again. An older build sees a
  resident with no body under a key no entity has: missing once their post is loaded, and "Call back" respawns them (their
  post was chosen at the join for that).
- **Config** (`Config/blocks.xml`, commented; `ScConfig`; the `config:` line gains `enclaveJoin=`): `EnclaveJoin` true,
  `EnclaveJoinStanding` 90 (70-100), `EnclaveJoinBundles` 2 (0-20), `EnclaveJoinArrive` own (own|follow; anything else
  warns), `EnclaveJoinArriveHours` 6 (0-240).
- **Dialog** (`Config/dialogs.xml`): `scEncJoinAsk` in the Allied leader greeting (9 entries, 7 on screen), the confirm
  `scEncJoinConfirm` (`scEncJoinYes` - the text `scEncRecruitYes`, "Welcome to the community." - and E2's
  `scEncForgetAllied`); `DialogRequirementScEncJoin` (`show` / `open`, the reason cached twice a second as E7's) and
  `DialogActionScEncJoin`; `ScEncDialog.Fill`'s `{joinfacts}`.
- **Console:** `sc enclave join <id> [sim|now [force]]` (`ScEncJoin.Command`: the line; sim - the row as shown, who would
  come and whose body is out, what they'd bring and how; now - the join, every gate kept, `force` skipping only Allied and
  the standing); `sc enclave arrivals [now]` (everyone on the way and when; now - all due at once); `sc enclave info` a
  `join:` line; `sc status` `, on the way N`; `sc members` their arrival text.
- **Lines.** The confirm (`scEncJoinConfirm`: its spoken part in `Tools/voice_lines.py`'s `SPOKEN`, `{joinfacts}` shown
  only) and the leader's two answers (`scEncJoinSaid`, `scEncJoinSaidFollow`, in `RUNTIME_FIXED`, shown as a tell through
  E9's `scEncCallSays`) have no name or number in them: 166 dialog lines (163 before), no problems; the list does not feed
  `ScVoiceLines.cs` (no `gencs`; `check` says in step). Nothing in `ScVoice.cs` or `Patches_VoiceControl.cs` was touched.
- **UI.** No window layout changed, no generator run, nothing for AshfallUI (it reads SC's bindings).
- **Harmony targets: none new.**

**The rule** (asked for: "the rule as built"). The Allied leader offers it only at standing 90+ with room for all of them
under `MaxMembers`; it is greyed while one of theirs is away, a request or radio call of theirs is open, their group is
still being made, or there is no home. On "Welcome to the community." every living person joins (the leader too) and the
group ends (refill owed, no morale lost, the building free). With `own` each is on the way - a member without a body,
due `EnclaveJoinArriveHours` later - and turns up at home only while the player is there and the zone is loaded, one a
second, out of sight at the zone's edge facing the old building; nothing ends the wait but the arrival (or dismissing them
from the window); a reload keeps it (`arrive=`), a world day set back shifts it.

Where the design was wrong or short, and the code followed SC or the user:
1. **Allied, 90 and the room hide the row instead of greying it** (the user's rule for the room, the caller's for all
   three); the design's "Needs Allied, standing 90" and "Your community has room for 3 more, they're 5" became console
   words (`HiddenWhy`). The other reasons still grey it.
2. **"Room" is `MaxMembers`, not beds.** The plan's Q10 says "beds for everyone", but beds in SC are only a morale term
   (`ScMorale`: a deficit costs morale; `ScCommunity.CanRecruit` never reads them); the roster's one hard limit is
   `MaxMembers` (`scRosterFull`), which the design's formula used. Members on the way count; a group still being made counts
   its places not made yet.
3. **"Arriving" is a member status (`MemberStatus.Arriving`), not an `ArriveAt` on a resident.** Every `== Resident` gate
   (runs, postings, watches, ScMissing, the defence, siege and noise counts) leaves them out by construction, which the
   design's list (`ScMissing`, the missing count, `ScNoise`, the window) did not cover; Relink, FindNpc, the missing count,
   the threat pick, Restore and Leave needed a line each. It is saved as a resident plus `arrive=` (E10's "raiding is
   written home" idea) so an older build degrades to a missing resident.
4. **The key.** The design kept the old body's id until the arrival; a person may have no body out at the join, and an
   entity id can come back after a crash, when Relink would drop a member whose id a stranger holds. A member on the way is
   keyed `int.MaxValue - person id` (E3's `ParkedKey` idea) with or without a body, and their old body is a husk, never the
   member's.
5. **The walk off.** "Despawn the body once nobody sees it (it walks off first, `StartWalkHome`'s steering toward home)":
   `StartWalkHome` steers toward the enclave's own spot; a husk is steered toward the player's home with E3's
   `StepToward`, keeps the enclave cvars and the save veto, and goes the first second nobody sees it.
6. **The spot.** `ScRadio.SpotNear` searches round one point; "the edge of the zone, on the side facing the old enclave"
   needed a line search (`ArriveSpot`), capped at 30 m from the claim because `Station` moves a body more than 40 m from its
   post (they should walk in); never in sight (E9's `SeenByAny`, E9's 35 degrees and 60 s for the side).
7. **A crash mid-join.** The design leaned on `Admit` saving per person; with `own` the join saves once at its end (a crash
   before it leaves the group whole), with `follow` Admit's per-person saves remain. A person whose join throws stays and
   the group stands smaller (`Disband` only with nobody left) - the one way the E9/E10/E11 gates can meet people on the way.
8. **SurvivorOrigins.** "One Leadership 'recruit' event per person, as one recruit sends today": a recruit sends none
   (`ScLeadership` counts favours, rescues, outposts and defences), so the join sends none.
9. **The dialog.** `scEncJoinAsk` is in the Allied greeting only (it never shows at Neutral or Friendly; the other two keep
   their 10-row room); the yes row reuses `scEncRecruitYes`'s words. The confirm's "All five of us. We'd bring what we have:
   10 food." has a number: the words come first, the facts after (`{joinfacts}`), as every line since E4.
10. **One tell, not two.** The joined line and "Their building is empty ..." follow the leader's words in one tell (E10's
    demand tell does the same), the days being `LootRespawnDays`.
11. **The console.** `sc enclave join <id> [force]` became `[sim|now [force]]` (as E11's move), and `sc enclave arrivals
    [now]` was added: after the join the group has no id to address its people by.
12. **Silent in the design, decided here:** a group still being made greys the row (E3's "still settling in"); the follow
    mode's bodies need the building's chunks settled (`SpawnAll`), anyone without one comes on their own; arrivals go on
    with `EnclaveJoin` switched off; a member on the way can be dismissed from the window; a member whose class leaves the
    game stays on the way (they can't be made); `EnclaveJoinStanding` is 70-100 (Allied begins at 70); the leader - whom E3
    never recruits - leaves the group's trade stock behind (`DropStock`).
13. **Line drift since the design (`a52c0b1`; this tree's base `bef80a8`):** `ScEnclaveDeals.Recruit` `:780-823` (769-812),
    its Member build `:792-802` (781-791); `ScEnclavePeople.LeaveEnclave` `:1662-1682` (1551-1571), `SpawnAll` `:2065`
    (1918); `ScEnclaves.Fall` `:1551-1575` (1303-1327); `ScRegistry.Members` `:76` (63); `ScTicker`'s `ScMissing.Tick`
    `:116` (115), `ScDaily.Run`'s missing count `:366-376` (365-375); `ScMissing.Tick` `:41-68`, its resident test `:53`
    (52-60); `ScNoise`'s resident count `:155` (154). `ScCommunity.Admit` `:178-225`, `FollowInternal` `:1029-1039`,
    `ScEncAllies.OnFallen` `:472-501`, `ScSpawn.Body` `:93`, `ScRadio.SpotNear` `:498`, `ScMissing.Restore`'s re-key
    `:106-127` and `ScConfig.MaxMembers` `:82` match.

Checks:
- XML replay against the 3.4 game (`research\e12\sc_replay_e12.py`, a copy of `research\dev\sc_replay_dev.py` pointed at
  this worktree by `research\e12\make_replay_e12.py`; out `research\e12\replay`): every SC op matches, no aborts; dialogs 15
  ops (E11's 14 + the new append), blocks 9 as E11's (the keys go inside the existing block). The merged dialogs.xml has
  `scEncJoinAsk` in `scEncLeaderAllied` before `scEncThreat`, the confirm and both responses; blocks.xml the five
  `EnclaveJoin*` keys.
- HarmonyAudit (`research\e12\audit34_e12_*`; E11's build `1a12f9ff` audited the same way, `audit34_base_e11_*`): 3.4 OK=150,
  DYNAMIC_UNRESOLVED=23 in both (V2's Voice Control targets since round 14), the patch rows identical, the refs files the
  same.
- `Tools/voice_lines.py check`: no problems, 166 dialog lines, `ScVoiceLines.cs` in step. Localization.csv parses as 7
  columns everywhere (1401 rows), no duplicate keys. LF, no BOM, in every file touched.
- The data edits (Localization, the blocks.xml block, `voice_lines.py`) are `research\e12\ed_e12_data.py` (re-run safe).

Builds: SC 3.4 `9f16253e574e83053978318c18025f9c` (`build\sc34_e12`), 0 errors (the two old CS0108 warnings). The 3.2 DLL was not rebuilt
(`SurvivorCommunity.dll` unchanged). Test steps: TESTING.md group E12 (240-259). Not reviewed (Fable), not installed. The
task list needs its entry.

**E12 Fable review** (`research\review_e12\REVIEW.md`, of the merge `11315a3`: round 16's review fixes and E12):
DEPLOYABLE, no HIGH, no MEDIUM; the E11 fixes all correct. Fixed: L1, an arrival spot at the zone's edge opposite the
person's post could be more than 40 m from it (up to `ArriveReach` + `PostRadius` x 1.4), and `ScCommunity.Station`
would then put them straight onto the post instead of letting them walk in: `ScEncJoin.ArriveSpot` skips a spot more
than `PostReach` (38 m) from their post. Left: the NITs (N1 an old-shape `moved=` name starting with digits and ':' -
only the never-installed first E11 build wrote it; N2 a raid stalled on `Waited` and then left settles unanswered; N3
a join cut short shrinks reason 1's total, held by `WhyHeld` until the arrivals are home; N5 `ScEncJoin.Key` equals
`ScEnclaveDeals.ParkedKey` in a different table; N6 `ArriveSpot`'s zone tests, bounded). N4: the merged tree's build is
recorded at the install.

### Round 18: bands on the road - drifters and raider bands (E13a) (2026-11-03, built, reviewed (Fable: deployable after fixes), review fixes in, not installed)

`research\ideas\DESIGN.md` section 10 (idea 9) and section 1 (rules), with the user's decisions (Q12 as recommended:
drifters and raider bands first, the rest later; single player only; `OutpostAttacks` off also turns off enclave raids -
for bands, decided below). Branch `ms/e13a` from E12's `ecb7c7b`. **3.4 only**: only the 3.4 DLL was built; the tracked
3.2 `SurvivorCommunity.dll` is untouched, and the new code has no `SC_V34`. Single player only, as SC.

- **`ScBands.cs` (new).** No new Harmony patch; driven from `ScTicker` (1 Hz, after `ScEnclavePeople.Tick`), `ScDaily.Run`
  (after `ScEncMoves.Daily`), the existing SetDead and PostInit postfixes, the dialog and the console.
  - **Records.** `Band` (id, kind `drifters`/`raiders`, name, size, the two buildings and their names, the destination's
    enclave, the walk - `At` where the stretch began, `Depart` the world time it began, `Via` the bend while ahead, `ToAt`
    the end - the request-clock start day, met and the day, the seed, trust -1/0/1, trouble 0 none/1 owed/2 on/3 over,
    arrived) and `BandPerson` (id, band, role, state, the day they died, health, `Who` - a `Member` as E2's `EncPerson`
    holds one). `ScRegistry.Bands` / `BandPeople`; band people are never `EncPeople` or `Members`.
  - **The day's roll** (`Daily`, once per request-clock day): a trip older than 3 request-clock days ends (never while its
    bodies are out); then, `Bands` on and fewer than `BandMax`, `BandDailyChance` for a new band - raiders with
    `BandRaiderShare`, `BandSizeMin..Max` people, trouble owed with `BandTroubleChance` (drifters) - setting out 0-3 h after
    the rollover. **`PickTrip`**: buildings from `ScPoiIndex.Near` within `BandPassRange + BandTripMax` of home (the player
    without one); up to 1500 random pairs `BandTripMin..Max` apart whose line passes within `BandPassRange`; `Refuse`:
    infested, an outpost, overlapping home, claimed (`CheckForAnyPlayerHome`), a start within `BandDespawnRange` of a
    player, an enclave as a start, as an end anything but a non-hostile enclave for drifters or a hostile one for raiders.
    The ends are just outside each footprint; a line through the home zone (a circle round the claim's bounds + 30 m)
    bends at one point outside it.
  - **The walk.** Nothing walks unloaded: the virtual place is `Along(route, walked)`, walked = the non-camp world ticks
    since `Depart` x `BandMetresPerHour` (`WalkTicks` leaves out `BandCampFrom..To`). A band at the end with no bodies
    ends ("arrived at <To>"). When bodies go, the walk starts again where the leader was (`Replan`: `At` there, `Depart`
    now, a bend behind them dropped); when the first body comes, the stretch is re-anchored at the virtual place
    (`GoOnline`) so the clock never counts twice.
  - **Bodies** (`TickBand`/`TickOnline`): with no body out, one comes when a player is within `BandSpawnRange` of the
    virtual place, the spot settled for a full tick, not `FightNear` (a live base attack near the player, an outpost
    fight, any enclave siege, the blood moon), at most one a second across all bands; the rest come one a second while a
    player is within `BandDespawnRange`, in file behind the leader (their slots), never within `BandNoPopRange` in a
    player's view. First time: E2's `Generate` shape - a class from `scSurvivorGroupGS*` / `scRaiderGroupGS*` at
    `ScLoadout.GamestageTier` (`RollClass`), a stranger spawned (`ScSpawn.Create`) and captured into the record (name,
    look, pair, rounds, profile; a raider's stars as their Shooting); later: `ScSpawn.Body` from the record. Each body: `Dynamic`, `%sc_band` = band id,
    doors, the record's health, the chunk-save veto (`ScRunners.Vetoes` gains `ScBands.IsBandBody`), raiders their
    stars, a band against the player ScTrust's grudge. They go (recorded, removed) when nobody sees them (`SeenByAny`)
    and no player is within `BandDespawnRange`, or (arrived) nobody sees them; an unload or the world's shutdown records
    and removes them; a body gone another way comes back from its record (a warning); a copy brought back by a chunk or a
    save is swept (`%sc_band` at PostInit, as E2's orphans).
  - **Steering** (E3's walk): the leader's next point (`ScRunners.NextWaypoint`, else `ScEnclaveDeals.StepToward`) by
    XNPC's Guard order (`ScEnclavePeople.GuardAt`), again when reached or after 15 s; the others at file slots behind it;
    a straggler more than 50 m back, unseen, is put back in file. Camp (and the wait before setting out): each at a slot
    in a circle round where the leader stopped. Stuck - the leader under 3 m in 60 s, walking, not fighting, nobody
    looking - the bodies go and the walk goes on unseen (no bodies for 90 s). Arrived: they hold at the end and go once
    unseen, ending the band. A talk with one of them holds the whole band where it stands (the dialog's `Respondent`).
  - **Met** (`MarkKnown`): seen, or a body within 30 m: known, the tell, the ledger, the marker (`sc_band_drifters` grey,
    map only; `sc_band_raiders` red, compass within 200 m; owner-only), which follows them every 5 s (moved 10 m) - from
    their bodies or the virtual place - and goes on arrival, the end or a load (restored on the next tick).
  - **Drifters.** A hit (ScTrust's `IsHostileTo` on any body: the wild survivor's "hostile for good") or a kill by the
    player or a follower turns the whole band (`Trust` -1, every body the grudge, later bodies too); a band against the
    player is engaged as raiders are. **Trouble** (owed, the band whole, the player within `BandSpawnRange` + 30 m):
    `BandTroubleZombies` zombies of the base attack's gamestage group (`ScBaseAttack.GroupFor` / `PickClass` /
    `SpawnHordeZombie`) 14-24 m from them away from the player, never popping in in view, each on one of them, re-aimed
    every 5 s. Over when none is left: a kill by the player or a follower (`KillerPlayer`) makes them trust the player
    (`Trust` 1; the leader's thanks and "They trust you now." as one tell, a ledger line); the bodies going cuts it short.
  - **Raiders** (`Engage`): once one sees the player within 60 m, every body takes the player as its attack target
    (again every 5 s to any with none) until the player is beyond `BandDespawnRange` of all of them.
  - **Deaths** (`OnBodyDied`, from the SetDead postfix before E2's): the person is dead (the corpse and its loot are
    vanilla's); a dead leader's role passes to the highest tier of the living; a drifter's death near the player is
    told; with nobody left the band is destroyed (a tell when near, a ledger line once met) and ends.
  - **"Come to our place."** (`HiddenWhy` null): drifters, not against the player, someone alive, a home, and room for all
    of the living under `MaxMembers` (`ScEncJoin.RoomLeft`: members on the way count) - otherwise the row isn't there.
    The answer: yes when they trust the player, else their own roll from the seed against `BandJoinChance` (the same
    every time). **`JoinNow`**: each living person becomes a member on the way through E12's path
    (`ScEncJoin.OnTheWay`, factored out of `JoinArriving`): keyed `int.MaxValue - 0x1000000 - person id`, `ArriveEnclave`
    the band's id negated, `ArriveName` the band's name, `ArriveFrom` where they are, due after the walk to the home at
    `BandMetresPerHour` (an hour at least); a body out gives its pair and rounds first and walks off as an E12 husk
    (`ScEncJoin.AddHusk`, factored out); one tell (the leader's word, then the facts), the ledger, the log; the band ends.
  - **The dialog** (`DialogOpsBands.cs`, global `DialogRequirementScBand` / `ScBandCome`, `DialogActionScBandJoin`):
    phases `scBandTrusted` / `scBandDrifter` in `SdcsNPC`; the greeting, "Where are you headed?" (`{bandwhere}`), "Come
    to our place." as two rows (yes: the confirm with `{bandjoinfacts}`; no: their refusal), "Stay safe."; the yes row
    reuses `scEncRecruitYes`, the back rows `scEncBack` / `scEncForget`. `ScEncDialog.Fill` hands a band respondent's
    line to `ScBands.Fill`. The stranger rows (the Hire append and the ShowMe append) hide on `%sc_band`.
- **Elsewhere.** `ScCommunity.CanRecruit` refuses band bodies (`scEncStays`); `DialogOps.Ensure` makes no prospect of one;
  Voice Control's spoken hire, pick-up and dismiss refuse them with the enclave lines, and they fight at will
  (`Patch_MayEngage`). `ScEncJoin.WhenText` names `band N` for a negative `ArriveEnclave`. `ScEnclaves.OnWorldDayBack`
  shifts `Depart` (world clock) and the start day (request clock). `ModApi`: `Reset` at GameStarting,
  `OnWorldShuttingDown` after E2's. `ScOutposts.OnRegistryChanged` calls `ScBands.OnRegistryChanged` (ids squared, no
  body known, markers restored). `ScConfig`: 18 keys, the `config:` part `bands=...`.
- **The E9-E12 gates.** Band people are neither `EncPeople` nor enclave members, and a member on the way from a band has
  a negative `ArriveEnclave`, which no enclave id matches: E9's `CanCall`, E10's `WhyNot`, E11's `WhyHeld`, E12's
  `Blocked` and the info lines never see them. `RoomLeft` counts them as members on the way, as it should.
- **Save.** Two forward-only sections (`ScRegistry` doc comment): `[band]` `id kind name size made from fromname to toname
  toenc toat at depart via day known knownday seed trust trouble arrived`, `[bandperson]` `pid band role state died
  health` + the member keys (`WriteMemberFields`); `bandid=` / `bandpersonid=` in `[community]`. An older build counts the
  lines as unreadable (a "degraded" load, which only holds off its portrait pruning) and drops them at its next save. A
  person whose band is missing is dropped (a warning). Trouble on (2) is written as over (3): its zombies are not saved.

**Design deviations** (each with its reason):
1. **`BandMetresPerHour` 250, not 1500.** At 1500 a band crosses the whole 600 m pass window in about 25 game minutes and
   is 3000 m on in 2 hours: you would almost never meet one, and bodies (which walk at about 240 m a game hour on a
   60-minute day) could not keep up with their own virtual place, so every despawn/respawn would jump them. 250 matches
   the bodies' pace; a trip still takes under a game day.
2. **The walk's record.** The design's `leg=start:end:from:to state=travel|camp` became `at`, `depart`, `via`, `toat`
   (the place is derived from the clock, camp hours left out, so no `state`); `role=leader|member` (not `lead`); `name`,
   `size`, `made`, `fromname`, `toenc`, `day`, `knownday`, `trust`, `trouble`, `arrived` added. A trouble fight is saved
   as over.
3. **Two clocks.** The walk is on world time (as the design's legs); the day's roll, a trip's age and the 3-day end are on
   the request clock (as every daily since E4), so `sc day` rolls and `sc band advance` walks.
4. **Arrivals end at the tick, not at the daily.** A band reaching its end ends then (with no bodies) or once nobody sees
   them (with bodies). The daily only ends trips older than 3 request-clock days (`MaxTripDays`, a safety net for a band
   held by camp hours or a stuck walk; never while its bodies are out).
5. **No SC utility-AI package on band bodies.** The design's risk list says band bodies need SC's package as enclave
   bodies have `SCEnclave`: that package's actions are anchored to an enclave's footprint and home spot (its leash and
   defence), which a band doesn't have. The bodies keep XNPC 3.4's own packages and walk by the Guard order, as E3's
   leaving people and E12's husks already do on 3.4; drifters fight at will (`Patch_MayEngage`), raiders by their class.
   Untested in game.
6. **A separate cvar and records.** `%sc_band` (not `%sc_enc`) and `BandPerson` (not `EncPerson`): an enclave person is
   tied to an enclave everywhere (E3-E12's gates, leash, defence, standing); a band person in those lists would need a
   guard in each.
7. **"Come to our place."'s answer.** The design says they join "as a group (the same path as idea 8)" and that helping
   "earns their trust" without saying what trust does. Built: a trusting band always says yes; an untrusting one rolls
   once (`BandJoinChance`, new, 0.6), the same answer every time. Help = a kill of one of the trouble's zombies by the
   player or a follower; the thanks line is the leader's word plus "They trust you now.".
8. **The join's path.** E12's arriving path, always "own way" (`EnclaveJoinArrive` `follow` is E12's only), no bundles to
   the stockpile (a band carries nothing), due after the walk to the home at the band's pace (an hour at least) rather
   than `EnclaveJoinArriveHours`; a band's people are told apart by a negative `ArriveEnclave` and their own key range.
9. **Hostile drifters.** Not in the design: a hit (ScTrust's wild-survivor rule) or a kill turns the whole band against
   the player for good - they attack on sight, no talk, no join.
10. **Raider bands and `OutpostAttacks`.** The user's decision ties enclave raids to `OutpostAttacks`; raider bands do not
    follow it. They never target outposts or the home, only whoever they meet on the road; their switch is `Bands` and
    `BandRaiderShare` 0 (documented in the blocks.xml comment and the README).
11. **Ends.** Nobody sets out from an enclave (errands are E13b's); drifters may head for a non-hostile enclave's building,
    raiders for a hostile one (the design names only drifters). On arrival E13a just ends the band.
12. **The bend.** One via point on a circle round the zone's bounds plus 30 m, pushed out until neither half crosses it.
13. **Fights hold off new bodies** in the design's spirit and a bit wider: a live base attack near the player, an outpost
    fight, any enclave siege and the blood moon.
14. **Raiders passing a friendly enclave.** No new code: E2's defence already fights an enemy inside the footprint within
    `EnclaveDefendRadius` (raider classes are enemies by faction), and routes never start at an enclave.
15. **A talk holds the band** (not in the design): otherwise the person you talk to walks away mid-sentence.
16. **The console.** `sc band list|info|spawn [drifters|raiders] [trouble]|here <id>|advance <id> [hours]|kill <id>
    [<person id>|all]|clear <id|all>|trust <id>|roll`: `here` takes a band id (the design's had none), `spawn` sets a band
    on a real trip 140 m from the player walking through them; `advance`, `kill`, `trust` and `roll` added for testing.
17. **Voices.** The five new dialog statements are V2 lines (`voice_lines.py`: the two with tokens speak their token-free
    part); the two tells said in the leader's word (`scBandThanksSaid`, `scBandJoinSaid`) are runtime-fixed lines, text
    only until the voicing round wires their playback (as E12's).

Checks:
- XML replay against the 3.4 game (`research\e13a\sc_replay_e13a.py`, made by `research\e13a\make_replay_e13a.py`; out
  `research\e13a\replay`): every SC op matches, no aborts; dialogs 16 ops (E12's 15 + the band append), blocks 9 as E12's
  (the keys go inside the existing block), nav_objects 1. The merged dialogs.xml has phases `scBandTrusted` and
  `scBandDrifter`, the 4 Hire and 4 ShowMe rows hidden on `%sc_band`; blocks.xml the 18 `Band*` keys; nav_objects.xml
  both classes.
- HarmonyAudit (`research\e13a\audit34_e13a_*`, compared with E12's `research\e12\audit34_e12_*` by
  `research\e13a\cmp_audit.py`): 3.4 OK=150, DYNAMIC_UNRESOLVED=23 in both, the 174 patch rows identical but for source
  lines; the refs gain one row, `ScBands::RollClass`'s generic `DictionarySave.TryGetValue` - the same false positive as
  `ScEnclavePeople::RollClass`'s.
- `Tools/voice_lines.py check`: no problems, 173 dialog lines (166 + 7), `ScVoiceLines.cs` in step. Localization.csv
  parses as 7 columns everywhere (1431 rows), no duplicate keys. LF, no BOM, in every file touched
  (`research\e13a\check_data.py`).
- The data edits (Localization, the blocks.xml block, `voice_lines.py`) are `research\e13a\ed_e13a_data.py` (re-run safe).

Builds: SC 3.4 `04e6ad3d8e1fa729c3dcbf3eb7036ea0` (`build\sc34_e13a`), 0 errors (the two old CS0108 warnings). The 3.2 DLL was not rebuilt
(`SurvivorCommunity.dll` unchanged). Test steps: TESTING.md group E13a (260-289). Not reviewed (Fable), not installed. The
task list needs its entry.

**E13a Fable review** (`research\review_e13a\REVIEW.md`, of the merge `151e6a4`: E13a with rounds 15-17 and 19):
DEPLOYABLE AFTER FIXES, no HIGH. Fixed: M1, the trouble's zombies kept the base attack's flags (`IsHordeZombie`, chunk
observer, scout) when the fight ended any way but by their deaths. `EndTrouble` lets them go first (`ReleaseTrouble`):
one alive that nobody sees (`SeenByAny`) is despawned, as the base attack's abandoned fight; one in view, or a corpse,
is `ScBaseAttack.Unflag`ged and left to vanilla's despawn and its chunk. `TickTrouble` unflags each corpse as it counts
it (the base attack's `Watch` does the same), and `End` calls `EndTrouble(.., "the band is gone")` when `Trouble == 2`
instead of clearing the list, so walking away, the stuck despawn, `Destroyed`, `sc band clear`, the daily end and a join
all release them; the cut-short log line gains ` - its zombies: G despawned unseen, L let go`. `Destroyed` only unflags:
it runs inside the SetDead postfix, where the zombie that killed the last of them may be mid-attack, and removing it
there would null its move helper and navigator under it (`EntityAlive.OnEntityUnload`). Across a save and quit: the
shutdown's `DespawnAll` -> `Despawn` -> `EndTrouble` releases them before the world is saved; a live trouble zombie is
`Dynamic` and never written (`EntityEnemy.IsSavedToFile`); a corpse is written, but both flags are runtime fields
(`IsHordeZombie` is never saved, `bIsChunkObserver` is re-read from the class in `InitCommon`); `Trouble` 2 loads as 3.
`Bands` is read once per world (`ScConfig.Reset` at GameStarting), so "switched off mid-fight" is a restart, which the
load covers. L1: `Patch_DamageResponse` calls `ScBands.OnHitBy` after E2's hook and ahead of ScTrust's (the same patch
method, no new target): any band body returns true; a drifter band's first hit calls `TurnOn` and saves.
`ScTrust.Subject` leaves `IsBandNpc` out - no `Persist`, no `buffOrderWander`, no `scTrustHostile` tell - and
`TickOnline`'s `IsHostileTo` loop is gone. The talk's snapshot card (`XUiC_ScSnapshot`), which showed band bodies only
through `Subject`, now names drifter band bodies itself. L2: `Despawn` skips a body the game is still building and keeps
its `EntityId` (TickBand's loop takes it once it is built, and the despawn rule runs again); `DespawnAll` keeps such an
entry in `bodies` rather than clearing them all (with Bands off the next tick's `DespawnAll` takes it); `End`, `JoinNow`
and `sc band kill`, which drop the person for good, hand it to `Orphans` (`SweepWhenBuilt`); both orphan sweeps, the
bands' and E2's `ScEnclavePeople.SweepOrphans`, keep a pending id for a later tick, and E2's `DespawnEnclave` skips a
pending body as `Despawn` does (its `DespawnAll` runs every tick while one is left). Found while fixing it: neither L2
case arises in 3.4 as written - SC's bodies are built in place (`ScSpawn` -> `World.SpawnEntityInWorld`; only chunk
loads, sleeper volumes and a client's net spawns queue on `EntityAsyncManager`), and a chunk copy's PostInit runs inside
`EntityCreateHandle.TryComplete` after `IsCompleted` is set, the entity entering the world in the same call - so these
are guards against a future change, not a behaviour change. L3: chose an arrival watch over snapping `ToAt` (at trip
time its chunk is rarely loaded, and a standable cell inside a walled yard is still out of reach): within `ArriveNear`
34 m of the end (`ArriveReach` plus `NextWaypoint`'s 24 m sidestep), walking and not fighting, a leader no nearer by 2 m
in 30 s has arrived, watched or not; `ToAt` becomes where they stand, so they hold there (`... arrived at <To> (as near
as they can get, D m off) - they go once nobody sees them`). The watch restarts with camp, a talk and each new body. N4:
`HiddenWhy` says `they are fighting` while `Trouble == 2`, which hides the row and refuses `Join`. N1, N2: TESTING
260/286 say "has", 280 says SC stops sending them past 180 m. Left: N3, N5, N6, N7.
TESTING steps changed: 260, 275 (no join row mid-fight), 277 (the released zombies; `sc band clear` mid-fight), 278 (one
tell), 280, 284 (an end they can't reach), 286 (the trouble's zombies over a save). Build after the fixes: SC 3.4
`7946f83d7297067b2dc89dd94047fe8e` (`build\e13afix`), 0 errors (the two old CS0108 warnings); HarmonyAudit
(`research\e13a\audit34_e13afix_*`): 3.4 OK=150, DYNAMIC_UNRESOLVED=23 (V2's Voice Control targets, as every round since
14), the patch rows and refs identical to E13a's but for source lines. Not installed.

**E13a re-check** (`research\review_e13a\RECHECK.md`, of `53d74ed`): DEPLOYABLE; M1, L1-L3, N1, N2, N4 fixed, the L2
guards confirmed inert in 3.4 (SC spawns in place: `SpawnEntityInWorld` is synchronous) and the always-taken branches
line for line the old code. Fixed after it: NIT-A, the ticker runs on real time through an ESC pause, so a pause of
`ArriveStallSeconds` near the end made a band "arrive" short - `Steer` now starts the stuck and arrival watches again
while the game is paused; NIT-B, the watches were the band's, not the leader body's - a new leader starts them again.
Left: NIT-C (`sc band kill all` lets the trouble's zombies go rather than despawning the unseen: they become ordinary
zombies), NIT-D (`here`/`advance` mid-fight log "they went off the map"), NIT-E (a `SweepWhenBuilt` id's sweep wording,
unreachable in 3.4).

### Round 19: the Community tab (W1) (2026-11-03, built, reviewed (Fable: deployable), LOW-1/3 fixed, not installed)

The user's decisions (2026-11-03): the community window is reached from an extra tab to the LEFT of the player menu's tabs
(Crafting, Character, ...); and, after a first proposal put it inside the bar: **"For the tab, it wouldn't be part of the
main bar. It would be similar to the creative menu tab (it's separate from the rest). Shouldn't require any work from
AshfallUI."** So the tab stands apart on a plate of its own just left of the bar, as Creative hangs off AshfallUI's bar
on the right, and SC alone makes it look and work right with vanilla's bar, SurvivorOrigins' and AshfallUI's (with or
without SurvivorOrigins). No new hotkey; radio commands stay at the radio. Research: `research\w1\TAB_RESEARCH.md` (its
claims checked against the 3.4 files; its proposal 1 - an iconbutton inside the bar, gated off under AshfallUI, with a
hand-over to AshfallUI's session - is what the user's second decision replaced). Branch `ms/w1` from `11315a3`. **3.4
only**: only the 3.4 DLL was built, the tracked 3.2 `SurvivorCommunity.dll` is untouched, the new code has no `SC_V34`.

- **The tab (`Config/XUi_InGame/windows.xml`).** One `rect` named `scCommunityTab`, appended to `windowPagingHeader`
  (controller `XUiC_WindowSelector`), holding the plate and the tab, at negative x so its right edge is 4 units left of the
  header's x=0, where every setup's bar starts. A `<conditional>` picks the look:
  - `mod_loaded('zzzzz_AshfallUI') and mod_loaded('SurvivorOrigins')`: AshfallUI's Creative plate and tab copied from its
    generator (`tools/make_nav.py` `_bar`/`_tab`, read only): `ashfall_hud_panel` 151x44, a `rect` named `ScCommunity`
    with `controller="ButtonSelectable"` 145x38 at 3,-3 (the bar's tab width with SurvivorOrigins), `chosen`
    (`ui_game_select_row`) and `hover` (`ashfall_btn_hover`) plates on `{% btn_selected }` / `{% btn_hovered and
    !btn_selected }`, the icon `ui_game_symbol_map_cabin` (the community window's own title icon) 22x22 at 31,-19 in
    AshfallUI's amber / white / warm grey, the label `xuiScCommunity` at 49,-20, 92 wide, font 20, upper case - make_nav's
    centring for a 9-letter name (`content_w` 22 + 7 + round(9 x 8.4) = 105);
  - `mod_loaded('zzzzz_AshfallUI')`: the same at AshfallUI's plain bar's tab width, 137 (plate 143x44, icon 27,-19, label
    45,-20, 88 wide);
  - otherwise (vanilla, SurvivorOrigins): vanilla's strip in small - `scTabBorder` (`ui_game_header_fill`, `[black]`, 52x43)
    and `scTabFill` (`ui_game_header_fill`, `[lightGrey]`, 46x37 at 3,-3), the paging styles' sprites and colours written
    out (not `style=`: SurvivorOrigins selects sprites by style) - and vanilla's tab, an `iconbutton` named `ScCommunity`
    (`selectable="true"`, `collider_scale="1.5"`, `snap`/`gamepad_selectable` false, tooltip `xuiScCommunity`) at 26,-21;
    `LB_Icon` (gamepad, pivot Right) moves from -8,-20 to -64,-20, left of the plate.
  Rect, plate and tab are hidden in the prefab editor (`visible="{# !is_prefab_editor}"`, as the ESC entry; on the tab
  itself too, because the selector only skips a button whose own view is hidden).

  Where it lands (header x; the screen's centre is the header's pos + x):

  | setup | header window (pos, size) | the bar | Community plate | tab |
  |---|---|---|---|---|
  | vanilla | -300,-9; 600x43 | 0..600 | -56..-4, 52x43 (screen -356..-304) | icon centre -30,-21 |
  | SurvivorOrigins | -375,-9; 750x43 | 0..750 | -56..-4 (screen -431..-379) | as vanilla |
  | AshfallUI | -488,-9; 977x44 | plate 0..977 | -147..-4, 143x44 (screen -635..-492) | 137x38 at -144,-3 |
  | AshfallUI + SurvivorOrigins | -663,-9; 1327x44 | plate 0..1327 | -155..-4, 151x44 (screen -818..-667) | 145x38 at -152,-3 |

  The 4-unit gap is AshfallUI's own for Creative (`cx = bar_w + 4`).
- **The page group (`Config/XUi_InGame/xui.xml`, `XUiC_ScPageGroup.cs` new).** The `sccommunity` group gets
  `controller="SurvivorCommunity.ScPageGroup, SurvivorCommunity"` (still no actionset). OnOpen: if the bar has a usable
  Community tab (`Tab`: `buttonsByName["sccommunity"]`, visible and enabled), `SetSelected("sccommunity")` **then**
  `Open("windowpaging", false)` - the bar's OnOpen opens the selected tab's page modally (`OpenSelectedWindow`), and a
  stale selection (the last page, Crafting at the start) would open that page and close this one; vanilla's groups open
  the bar first only because the selector opens them after selecting them. Without a usable tab (prefab editor, XML not
  applied) the window opens alone, logging once `community window: the tab bar has no Community tab (windowPagingHeader has
  no visible ScCommunity) - the window opens on its own, without the bar`. OnClose: `Close("windowpaging")`, as vanilla's
  pages (a tab press reopens it with the next page; Escape and the X leave it closed).
- **The openers (`ScCommunity.OpenWindow`).** The ESC menu entry (`Patches_Menu`), the land claim radial `Community`
  (`TEFeatureSCCommunity`), the dialog action (`DialogActionScOpenCommunity`), the Watch post's `Assign guard`
  (`ScWatch.OpenAssign`, `PendingWatch`) and the outpost radial (`OpenOutposts`, `PendingOutpost`) all call it, unchanged.
  With the tab it calls `XUiC_WindowSelector.OpenSelectorAndWindow(player, "sccommunity")`: other modal windows close (the
  ESC menu, the NPC dialog - as it closed under the old modal open), the tab is selected, the bar opens and opens the
  page. Its two traps are guarded: it shuts the page when it already shows (toggle), so an open window is left alone; it
  does nothing for a dead player, so neither does OpenWindow (vanilla's tabs too). In both cases the pending Watch pick /
  outpost is cleared, so it cannot carry over to the next opening. Without a usable tab: the old `Open("sccommunity",
  true)`. Opening from a dialog: the action runs inside the dialog's response press; the dialog closes first, its OnClose
  reopens the toolbelt, then the bar and the page open; the dialog's `RefreshDialog` afterwards finds no statement
  (`scCommunity` has no next one) and its `Close("dialog")` is a no-op.
- **`XUiC_ScCommunity.Close()`** (the X, `OPEN STORAGE`, a Watch pick) closes `windowpaging` after the page too (the page
  group already does; this covers a layout that drops the controller).
- **No AshfallUI change is needed.** Its patches remove the header's direct `sprite` and `label` children and the vanilla
  and SurvivorOrigins iconbuttons by name, re-lay the header out (pos, size, `RB_Icon`, `LB_Icon`) and append their own
  rect tabs; none reaches an element nested in `scCommunityTab`, and its bar still starts at the header's x=0. Its
  runtime code (`src`, read only) never touches the header. SurvivorOrigins' patches select by vanilla name and style, and
  its header is wider to the right only. The replay below shows both mods' ops matching exactly as with SC absent.

Decisions:
1. **A tab of the selector, not a stand-alone button.** `XUiC_WindowSelector.SetSelected` with a name no tab has does
   nothing (`buttonsByName.TryGetValue` fails): the old tab stays lit, the title plate keeps its page's name
   (`SelectedNameLocalized`), and opening the bar would open that page modally over ours. Being a `ButtonSelectable`
   under the header (the selector binds them depth-first, nested ones too: `XUiController.GetChildControllers`) gives the
   lit tab, nothing else lit, the title `COMMUNITY` in vanilla's plate and bumper paging for free - as Creative, which is a
   tab of the selector in vanilla and AshfallUI, only placed apart. Separate is the look, not the plumbing.
2. **Inside `windowPagingHeader`, not a window of its own in `windowpaging`.** Every setup's bar starts at the header's
   x=0, so one constant negative x places it beside any bar with no runtime positioning; a separate window would have to
   read the header's position after every re-layout and handle the selection itself (decision 1).
3. **Document order: appended.** With vanilla's and SurvivorOrigins' bars it is the last tab, so `Init`'s default
   selection (`buttons[0]`) stays Crafting; with AshfallUI its rect tabs come after ours, so ours is `buttons[0]` there.
   Either way the bumpers' cycle reads left to right (LB from Crafting comes here, RB from here goes to Crafting, RB from
   the right end wraps to here). The default only matters to code that opens the bar without selecting a page first; no
   vanilla, AshfallUI or SC path does (they go through `OpenSelectorAndWindow` or select first).
4. **AshfallUI's tab widths are copied, not read.** SC patches before AshfallUI, so an `xpath()` guard cannot see its bar.
   If its generator ever changes the tab width, SC's tab keeps 137/145 and still stands 4 units left of the bar (its right
   edge is fixed).
5. **The gamepad LB icon with AshfallUI** stays where AshfallUI puts it (-8,-22, after SC's patch), on the Community
   plate - as its RB icon sits on Creative's plate. Without AshfallUI SC moves it left of the plate.
6. **A dead player** gets no window from any opener, as vanilla's tabs refuse (before W1 the ESC entry opened it).

Checks:
- Build: `dotnet build -c Release --no-dependencies -p:ScGame=34 "-p:OutputPath=F:\7daystodie mods 2026\Port33\build\sc34_w1\\"`,
  0 errors (the two old CS0108 warnings).
- HarmonyAudit (`research\w1\audit34_w1_*`): no new or changed patch; 3.4 OK=150, DYNAMIC_UNRESOLVED=23, the patch rows,
  reflection and unresolved-refs files identical to E12's (`research\e12\audit34_e12_*`), so the new references
  (`XUiC_WindowSelector.OpenSelectorAndWindow`, `buttonsByName`, `SetSelected`) resolve.
- XML replay against 3.4 (`research\w1\sc_replay_w1.py`, made by `make_replay_w1.py` from `research\dev\sc_replay_dev.py`:
  SC pointed at this worktree, plus `WITH=` to apply 3.4-install mods after SC and `NO_SC=1`; out `research\w1\replay\`),
  four setups - SC alone, `WITH=SurvivorOrigins`, `WITH=zzzzz_AshfallUI`, both - and the last three again without SC.
  `research\w1\check_tab_w1.py` (output `check_tab_w1.txt`): every SC op matches in every setup (157 / 157 / 156 / 156; SC's
  windows.xml 10 ops without AshfallUI, 9 with: one conditional branch has no LB_Icon op), no aborts; SurvivorOrigins'
  1486 ops match exactly as without SC; AshfallUI's 2218 / 2319 all match, the 22 that differ from the run without SC are
  its `mod_loaded('SurvivorCommunity')` Home Status block (16) and broad selectors that also reach SC's older elements
  (`searchInput`, the packing strip's labels, every `window_group`'s `stack_panel_padding`) - none touches the tab, and the
  `sccommunity` group keeps its controller. The merged header in each setup is the table above; its tabs in document order:
  vanilla `Crafting ... Creative, ScCommunity`, SurvivorOrigins `... Skills, SchematicTree, Collections, ... Creative,
  ScCommunity`, AshfallUI (both) `ScCommunity, Crafting, ... Creative`.
- LF, no BOM in every file touched.

Only in game (TESTING.md group W1, steps 290-299): the look - the small plate beside each bar, the gap, the AshfallUI
plate against Creative's; **clicks and hover at negative x**, outside the header window's own rect (NGUI windows don't
clip - only `XUiV_Panel` sets clipping - and AshfallUI's Creative tab already sits outside its window's width on the
right, but neither has been seen working here); that a bar title and a lit tab follow every opener; the gamepad paging;
the dialog opener's close order.

Builds: SC 3.4 `1e2a2b1279af718bef3b872416af6518` (`build\sc34_w1`), 0 errors (the two old CS0108 warnings). The 3.2 DLL
was not rebuilt (`SurvivorCommunity.dll` unchanged). Not reviewed (Fable), not installed. The task list needs its entry.

**W1 Fable review** (`research\review_w1\REVIEW.md`, of `5d1b6c1`): DEPLOYABLE, no HIGH, no MEDIUM. Fixed: LOW-1, the
ESC menu's Community entry closed the menu for a dead player and then opened nothing - it now does nothing while the
player is dead (`Patches_Menu`); LOW-3, a Watch pick or an outpost asked for stayed armed when nothing opened after all
(the HUD fully off: `openInternal` refuses) - `OpenWindow` clears them when the group is not open after the call (an
open is synchronous: `IsWindowOpen` counts `windowsToOpen`, whose `OnOpen` has run). LOW-2, noted: under AshfallUI the
selector's `Init` default (`buttons[0]`) is now `ScCommunity`; it only matters to code that opens a page group
directly with the bar closed before the selector was ever used - vanilla 3.4 has none, AshfallUI's pages use the
vanilla pattern. Left: NIT-1 (under AshfallUI the gamepad LB icon sits over the tail of COMMUNITY, as its RB icon
over Creative's), NIT-2 (`Close()` closes `windowpaging` a second time: a no-op).

### Rounds 15-17 and 19 installed (2026-11-03 21:20)

With the user's go-ahead (3.4 game closed): E10 (raider enclaves raid outposts), E11 (enclaves move), E12 (a whole enclave
joins) and W1 (the Community tab), each Fable-reviewed DEPLOYABLE with its fixes and re-checks (`research\review_e10`,
`review_e11`, `review_e12`, `review_w1`), and round vo3's first day of clips: 992 new lines for the four White voices
(Vane 420, David 430, Jessica 440, Beth 290 of 461 lines). port-3.4 fast-forwarded to `dev/ideas` f2c3064;
`deploy_sc34.py`: 3604 files, installed = built `0b5626cefce49f510898fd64ace18ece`. The old install is in
`Port33\_backups\sc34_2026-11-03_212011` (rounds 8-14's `9d2c4bee`); the user's `DebugLogging` true set again in the new
Config/blocks.xml. Nate's first 16 clips are held back until his set is complete (a voice with a few clips would switch
voices mid-talk); the twelve new voices stay silent until their rounds. E13a (bands, round 18) is built, not reviewed, not
installed. Test steps: TESTING.md groups E10, E11, E12, W1 (200-259, 290-299).

### Round 18 installed (2026-11-03 22:22)

With the user's go-ahead (3.4 game closed): E13a (bands on the road: drifters and raider bands), Fable-reviewed DEPLOYABLE
AFTER FIXES, fixed (`53d74ed`), re-checked DEPLOYABLE and its two nits fixed (`080bd7e`). port-3.4 fast-forwarded to
`dev/ideas` 080bd7e; `deploy_sc34.py`: 3606 files, installed = built `1321ea8f5367d1035c58fef05d4afaaf`. The old install is in
`Port33\_backups\sc34_2026-11-03_222206` (rounds 15-17 and 19's `0b5626ce`); the user's `DebugLogging` true set again in the
new Config/blocks.xml. The band leader's two told lines and the five new band dialog lines are text only until the idea-menu
voicing round. Test steps: TESTING.md group E13a (260-289).

### Voices vo3 day 2 installed (2026-11-04 19:50)

With the user's go-ahead (3.4 game closed): round vo3's second day of clips (`9ef2043`, clips and sidecars only, no code
change): the White voices complete (Vane, David, Jessica, Beth) and Nate's whole set, 458 of the 471 lines each - the 13
left are the idea-menu dialog lines from E11 on, for their own voicing round. port-3.4 fast-forwarded to `dev/ideas`
9ef2043; `deploy_sc34.py`: 4827 files, installed = built `1321ea8f5367d1035c58fef05d4afaaf` (unchanged). The old install
is in `Port33\_backups\sc34_2026-11-04_195058`; the user's `DebugLogging` true set again in the new Config/blocks.xml.
Desmond's first 266 clips are held back until his set is complete; the other ten new voices stay silent until their
days.

### Voices vo3 day 3 installed (2026-11-05 15:16)

With the user's go-ahead (3.4 game closed): round vo3's third day of clips (`1ef065e`, clips and sidecars only, no code
change): Desmond's and Tiffany's whole sets, 458 of the 471 lines each (the 13 left are the idea-menu dialog lines from
E11 on). Seven voices are complete now: Vane, David, Jessica, Beth, Nate, Desmond, Tiffany. port-3.4 fast-forwarded to
`dev/ideas` 1ef065e; `deploy_sc34.py`: 6685 files, installed = built `1321ea8f5367d1035c58fef05d4afaaf` (unchanged). The
old install is in `Port33\_backups\sc34_2026-11-05_151651`; the user's `DebugLogging` true set again in the new
Config/blocks.xml. Rene's first 340 clips are held back until her set is complete.

### Round 20: NPCCore 3.4.02 compatibility (X1) (2026-11-05, built, not reviewed, not installed)

The user updated the 3.4 install to 0-XNPCCore 3.4.02 (`NPCSdcsCharacters.dll` and `1-XNPCVoiceControl.dll` both in
`Mods\0-XNPCCore` now; SCore 3.4.3.826 unchanged) and, during the round, asked to get Vanity Vault's paints and tattoos
working again (Vanity Vault 3.3.0 in the same install). Branch `ms/x1` from `5b8fac9`, worktree `Port33\wt\sc-x1`. **3.4
only**: only the 3.4 DLL was built; the tracked 3.2 `SurvivorCommunity.dll` is untouched.

- **0-XNPCCore's own children of the six survivor classes get back what SC takes (`Tools/gen_arsenal.py`
  `xnpc_child_ops`, emitted into `Config/entityclasses.xml` after `XNPC_SURVIVOR_OPS`).** Entity classes inherit properties
  (`EntityClass.CopyFrom`) and effect groups (`MinEffectController.ParseXml` walking the parent elements of the patched
  document) down `extends`, so SC's takeover of `npcSdcsSurvivor{Male,Female}T{1,2,3}` - no `SDCS Look` group, `BagItems` /
  `HarvestItems` `none`, SC's `Names`, SC's worn armor scale - cascaded onto 3.4.02's `npcSdcsCustomMale/Female`,
  `npcSdcsBanditMelee/Ranged` and `npcSdcsPickpocket`, which the DLL leaves to 0-XNPCCore (ScXnpc: the six exact classes).
  They spawned with no look, armour or weapon roll, an empty bag, no bandages and SC's names. Now every class whose
  `@extends` is one of the six (the xpath is built from `XNPC_SURVIVORS`) gets back, each only where it declares none of
  its own: the parent's `SDCS Look` group as 0-XNPCCore defines it, the parent's own `BagItems`, `HarvestItems` and `Names`
  (all read from 0-XNPCCore's entityclasses.xml at generation; the generator stops when a parent does not declare exactly
  one of each, since SC's own `set` would then miss it too), and `SurvivorCommunity worn armor scale undone` (`PhysicalDamageResist
  perc_add +0.5` against the inherited -0.5: `PassiveEffect.ModValue` adds every perc_add into one multiplier, and
  `AddEffectGroup`'s extends-suppression only touches base_set / perc_set, so they cancel). Each op sits in an `xpath()`
  conditional on its own target, so the four `Names` ops of the childless T2/T3 parents are skipped quietly instead of
  logging `did not apply`. Selected at load, so a child a mod loading before SC adds is covered; one loading after SC is
  not. New `SC_GEN_GAME` (default the 3.2 install) points VAN, IGP_Classic and Combat Sandbox at another game; the output
  never follows it. Regenerated with `SC_GEN_GAME`=3.4 install, `SC_GEN_XNPC`=its 0-XNPCCore Config, `SC_GEN_CONFIG`=this
  tree's Config: entityclasses.xml gained exactly the new block (65 lines, 0 removed). The run also rewrote three files
  the same way a run on the unmodified generator does (checked first, on a scratch copy): blocks.xml and
  Localization.csv only move a block (as at E9), and entitygroups.xml's W0 lists for 0-XNPCCore's six biome groups
  (`npcFriendlyCoreGroupGS*`, `npcHarleyGroupGS*`) would take 3.4.02's new `none` weights (97 -> 197 and the like) - those
  groups' spawn entries are removed by SC (spawning.xml), so nothing changes in game; all three were restored, left for a
  later regenerate. SC's own six-class ops still match only the six (replay below).
- **SC's survivors and raiders aim by their Shooting stars alone again (Patches_Xnpc X18, X19; `ScConfig.XnpcAimSpread`).**
  3.4.02's `AvatarSdcsNpcController.SdcsAimTick` (private, 446 bytes of IL, 1 Hz from `Update` after `SdcsLevelTick`, owner
  only) writes `SdcsAimSpread` = the gun's aimed spread x `NpcLevelRules.AimErrorFactor` (3 at level 1 to 1 at the top) -
  its hip spread, and npcSdcsBase's `SdcsNpcAccuracy` (now the player's stance bases, hip 1) adds it as
  `SpreadMultiplierHip perc_add @SdcsAimSpread`. SC strips the level from members and raiders, so they all shot at level
  1's spread under SC's own aim error. X18 skips the tick for `ScXnpc.IsBody` and holds the cvar at `XnpcAimSpread`, **-1
  by default, not 0 as the round's brief said**: an absent cvar is not "near zero" in 3.4.02 but a player's hip-fire
  spread (`ItemActionRanged.updateAccuracy` reads `SpreadMultiplierHip` while `AimingGun` is false, which ScAim keeps for
  our bodies): an AK-47 2.8 degrees before the noise, a hunting rifle 5, a sniper rifle 6, wider than level 1 (1.85, 1.2,
  1.08) and far wider than 3.4.01's, which 0-XNPCCore's changelog puts at "6 to 30 times less than a player aiming".
  -1 zeroes the hip multiplier, so the gun adds nothing and the stars place every shot, as before 3.4.02 (a shotgun's
  pellets fly together, as they nearly did then). 0 gives the hip spread; anything between is allowed (-1..2). X19 skips
  `SdcsBanditAimTick` (147 bytes, from `SdcsBanditTick`; SC's raiders carry the `bandit` tag `IsBanditClass` tests) and
  keeps `SdcsBanditSpread` off our bodies; the rest of `SdcsBanditTick`, the `SdcsPlayerDamage` write, still runs (the
  user's call). Both bound by name with `Bound` (Prepare false + one warning when missing), in ModApi's ExpectedTargets,
  counted (`x18-aim-tick-skipped`, `x18-aim-spread-held`, `x19-bandit-aim-skipped`, `x19-bandit-spread-removed`).
  `sc xnpc <id>` has a new `aim spread:` line (both cvars, the held item's hip multiplier, SC's value).
- **Voice Control's relay (`Patches_VoiceControl.RelayTarget`).** 3.4.02 renamed `NetPackageNPCVoice` to
  `NetPackageVCVoiceRelay` (same private static `PlayVoiceLocally(int npcEntityId, byte[] wavData, float volume)`, 130 bytes
  of IL, called from its `ProcessPackage`); the new name is tried first, then the old one. ModApi's label follows.
- **Vanity Vault 3.3.0 (`ScVanity.Bind`, `Apply`).** `TattooMakeupManager.ApplyCosmetics` went from (renderers, lipstick,
  eyeshadow, tattoo, nails, isMale) to (renderers, lipstick, eyeshadow, face tattoo, body tattoo 1-3, hand tattoo, nails,
  isMale), so the 6-parameter lookup failed and `ok` turned everything off: no paints or tattoos on anyone. Bind takes the
  10-parameter overload, else the 3.2 one. In 3.3 each slot draws only its own part of a tattoo (face: `AssetHead`, body:
  `AssetBody`, hand: `AssetHands`, read from the variant's `MaleDef` / `FemaleDef` `CosmeticDefinition`; a slot given a tattoo with
  no part there would get its tint over no texture), so the look's one tattoo goes into each slot it has a part for (Yakuza: face and
  body 1), "None" elsewhere; nothing for a sex the tattoo has no version for. Those fields are bound at Bind and required
  like the rest. Unchanged in 3.3 (metadata): `ApplyFacepaint` / `ApplyBodypaint (SkinnedMeshRenderer[], string, bool)`,
  `BodypaintRegistry` / `FacepaintRegistry.KnownGroups` (Dictionary of string to a group with public `HasMale` / `HasFemale`),
  `TattooMakeupRegistry.KnownTattoos` (string to `CosmeticVariant`) and its id format `Shape (Ink) [Blend|Vivid]`. Checked
  against every Sonja XML of the 3.4 install (`scratchpad x1\vv_check.py`, 3.3's ParseXml rules): every configured body
  paint, face paint and ink still exists and fits the same sexes; one tattoo is gone, `Tribal 01 - Full` (3.3 has `Tribal
  01 - Face`, `- Body`, `- Hands`), in both tattoo lists: Bind's existing "not found there (skipped)" note names it and the
  roll skips it; a stored look with it shows no tattoo. Rolls unchanged (3.3 allows a face, three body and a hand tattoo at
  once: a possible later feature). The hair pre-fill stays checked against 3.2.0 only, so 3.3.0 does its own lookup.

**Second part: the user's calls on the triage (2026-11-05, same branch).**
- **X20: members take no ammo from the player's chests (Patches_Xnpc.cs:1752-1793, `Patch_NoMemberBaseRestock`).** 3.4.02's
  `AvatarSdcsNpcController.SdcsAmmoRestockTick` (private, ~340 bytes of IL, every frame from `Update` on the owner, throttled
  to `<Ammo><RestockCheckSeconds>`; `SdcsAmmoRestockTake` is its only caller) tops a survivor with an `Owner` cvar up to
  `<RestockTarget>` 120 rounds from broadcast storage in the claims and unlocked chests within 15 m. It is skipped for
  `ScXnpc.IsCommunity` (members and outpost stand-ins, the billing hooks' test), who are resupplied only from the
  community's stockpile (ScAmmo); strangers, 0-XNPCCore's own hires and enlisted enclave people keep it. Plain counter
  `x20-member-base-restock-skipped-frames` (per frame), in ModApi's ExpectedTargets.
- **Charismatic Nature: nothing in SC defeats it, no change.** `SdcsLeadershipTick` (not patched by SC; it runs after the
  level tick X3 skips, as its own call in `Update`) needs `Owner` > 0 and a loaded leader (`GetLeaderOrOwner`), reads his
  `perkCharismaticNature` level and, when the rank changes, writes `SdcsLeadRegen` (rank 1+, 0.5/s), `SdcsLeadDamage`
  (rank 2+, 0.2) and `SdcsLeadResist` (rank 3+, 0.1) and adds `SdcsNpcLeadership` (`HealthChangeOT base_add`,
  `EntityDamage` / melee `BlockDamage perc_add`, `GeneralDamageResist base_add`). Checked against SC: ScMedic's AddBuff
  prefix refuses only `SdcsIsHealing` and `IsHealing` (ScMedic.cs:652-668); `StripLevels` removes only `SdcsNpcLevelBonus`,
  `SdcsNoWear` and the `SdcsLevel*` cvars (ScXnpc.cs:50-55, 330-333), X3's regen zeroing is `SdcsLevelRegen` on raiders
  that keep levels; SC writes no `HealthChangeOT` (ScMedic's "already working" test is `medicalRegHealthAmount`,
  ScMedic.cs:257, 302), and its damage buffs are perc_add too (Config/buffs.xml:11-54), which add to the leadership's.
  So a following member gets all three ranks. A resident did not: `ScCommunity.Station` detaches a member
  (`Detach` removes `Leader` and `Owner`, ScCommunity.cs:342-343), so 0-XNPCCore does not count them as a hire.
  `sc xnpc <id>` has a `leadership:` line (ConsoleCmdSc.cs:2193-2198).
- **X21: Charismatic Nature on every member (Patches_Xnpc.cs:1800-1928, `Patch_MemberLeadership`; the user: residents
  are members and get it in full).** A postfix on `SdcsLeadershipTick` (349 bytes of IL, 1 Hz from `Update`, owner side,
  alive) does for a member or stand-in with no `Owner` (residents, Watch guards, garrison and rescue stand-ins) what the
  tick does for a hire, mirrored from its IL: the leader is the community's player (`ScCommunity.LocalPlayer`; none while
  no player is in the world, rank 0); rank = `LeadershipRules.Rank(leader != null, perkCharismaticNature calculated
  level)`; `SdcsLeadRegen` / `SdcsLeadDamage` / `SdcsLeadResist` = `LeadershipRules.Regen` / `Damage` / `Resist` of the
  rank and 0-XNPCCore's own `<Leadership>` settings, read through its private getters (`get_LeadershipRegenPerSecond`,
  `get_LeadershipDamageBonus`, `get_LeadershipDamageResist`: Settings.xml, clamped and defaulted as for a hire); the
  `SdcsNpcLeadership` buff added with the same `AddBuff(name, -1, true, false, -1)`; nothing done at rank 0 without the
  buff. Values are written only when they differ. No double application: followers (an `Owner`) stay on 0-XNPCCore's
  path; when a follower is stationed, 0-XNPCCore's tick writes zeros once (its rank drops to 0) and then stays quiet, and
  this postfix, running right after it, writes the member's values; when the member follows again 0-XNPCCore's rank
  comes back from 0 and it writes its own, and this postfix only takes its values off if that rank is 0. A body this
  wrote for (the `sc_leadres` mark) that is no longer a member gets the zeros 0-XNPCCore would leave. Non-members are
  never touched. Counters `x21-resident-leadership-set`, `x21-leadership-cleared`; `byScAsResident=` on the
  `leadership:` line. Prepare is off, with one warning, when the tick or the getters are gone. In a multiplayer game the
  leader of a resident is the host (SC's community is the host's).
- **Pay: members stay off contracts (Patches_VoiceControl.cs:46-52, 225-290).** Correction to the triage below: Voice
  Control 3.4.02's `SeedContract` is called only from `MaybeStampHireDay` (`TryReadLeaderFromSCore` calls
  `MaybeStampHireDay` at its four sites, as in 3.4.01), so SC's existing skip (Patch_NoHireContract, :164) still stops every
  seeding; the triage line misread a FindStr listing. What was missing: `RetainerLeader`, new in 3.4.02, is now in
  `ContractCvars`, so `StripContract` (the join, every relink, the reactor skip) takes it off too. Added as guards:
  `Patch_NoContractSeed` (SeedContract skipped and the contract stripped for members, stand-ins and enlisted people, should
  another path ever seed one) and `Patch_NoBillingReply` (`HandleBillingResponse`, the only caller of `TryDeductPayment`:
  a "pay" / "yes" / "no" never pays for one of them, even in the 2 s before Patch_NoBilling, :191, strips an old
  contract). `ProcessPlayerMessage` takes a message as a bill reply only while `RetainerNextDueDay` is set
  (`BillingContractRules.ClassifyReply`), and there is no contract UI in 0-XNPCCore's XUi or dialogs: billing is Voice
  Control's prompt (`CheckBillingReactor`, skipped) and reply only. Counters `contract-seed-refused`,
  `billing-reply-refused`.
- **Guard: no change needed.** 3.4.02's three new guard behaviours all need a leader: the patrol (`SdcsGuardPatrol.NextLeg`:
  order 9, loot mode 0, `Leader` > 0), the walk around a workstation (`SdcsStationStay.WanderStation` /
  `CraftRules.WandersAtStation`: `Leader` > 0) and the spacing (`SdcsGuardSpread.Tick`: `Guarding` >= 0.5, order 2 or 9,
  loot mode 0, and a `Leader` or `Owner` id; the others it makes room for must share that leader). Without one,
  `UAITaskGuardSdcs` runs SCore's `UAITaskGuard` unchanged (its Start/Update fall through to the base). SC's residents,
  Watch guards and garrison stand-ins are posted through `ScCommunity.Station` (ScWatch.cs:561, ScGarrison.cs:169), which
  first detaches them (no `Leader`, no `Owner`) and then writes the Guard cvars (ScCommunity.cs:308-309); enclave people
  are held by `ScEnclavePeople.GuardAt` (ScEnclavePeople.cs:924-929) with no leader, and enlisted ones lose theirs when the
  enlistment ends (`Detach`, ScEnclaveDeals.cs:460). So nothing new moves them or fights SC's post logic. A following member
  given 0-XNPCCore's own Guard order does patrol: that is its feature for hires, not an SC post.
- **X19 confirmed:** it targets `SdcsBanditAimTick` alone (Patches_Xnpc.cs:1643); its prefix skips only that method's body,
  and `SdcsBanditTick` goes on to its `SdcsPlayerDamage` write (IL: the call at IL_002c, then `get_BanditPlayerDamage`,
  `PlayerDamageCVar`, `SetCustomVar`). SC patches nothing else there (no `SdcsBanditTick` or `SdcsPlayerDamage` in SC).

Checks:
- **XML replay** (`research\sc34\xml\tools\sc_replay.py` copied to scratch with the SC tree selectable; SCore, 0-XNPCCore,
  Voice Control, SC on the 3.4 install's vanilla entityclasses.xml): 0-XNPCCore 82/82 ops, SC 14/14 (+10 conditionals: six
  true, the four T2/T3 `Names` false). Effective classes (properties walked down `extends`, effect groups with
  `AddEffectGroup`'s order and suppression): all five children now have `SDCS Look` (`SdcsApplyLook` on
  `onSelfFirstSpawn`), PhysicalDamageResist perc_add -0.5 + 0.5 = 0, `BagItems` 0-XNPCCore's
  (`gunHandgunT0PipePistol,meleeWpnClubT0WoodenClub`; the melee bandit keeps her own `meleeWpnClubT0WoodenClub`),
  `HarvestItems` `medicalFirstAidBandage=5`, `Names` their parent's 0-XNPCCore list - identical to a replay without SC.
  Before: no look applier, -0.5, `none`, `none`, SC's names. SC's own survivor classes (e.g. `npcSdcsSurvivorMaleT1`,
  `FemaleT3`): unchanged - no look applier, -0.5, `none`, `none`, SC's names; the remove / append / set ops matched 6, 6, 6,
  6, 3, 3 nodes, all on the six.
- **HarmonyAudit** of the built DLL (`dynamic_targets34.json` copy with the new targets and the two billing ones): 3.4
  OK=157, no param issues; X21 postfix `private Void AvatarSdcsNpcController::SdcsLeadershipTick()` (caller `Update`); X20 `private Void AvatarSdcsNpcController::SdcsAmmoRestockTick()` (caller `Update`),
  `NPCChatComponent::SeedContract()` (caller `MaybeStampHireDay`), `HandleBillingResponse(Boolean)` (caller
  `ProcessPlayerMessage`), `MaybeStampHireDay(Entity)` (caller `TryReadLeaderFromSCore`); X18 `private Void AvatarSdcsNpcController::SdcsAimTick()` (caller `Update`), X19 `SdcsBanditAimTick()` (caller
  `SdcsBanditTick`), the relay `static private Void NetPackageVCVoiceRelay::PlayVoiceLocally(Int32, Byte[], Single)`.
  Vanity Vault 3.3.0's signatures checked by Cecil metadata; the DLL's ten-argument `Action` binds to System.Core, which the game has.

Only in game (TESTING.md group X1, steps 300-319): the bandits', the creator survivor's and the pickpocket's look, weapon
and names; SC's survivors unchanged; the `aim spread:` line and the counters; the stars deciding hits again; Vanity Vault
3.3's tattoos by part and the paints; the relay in multiplayer; no chest ammo for members (others keep it); Charismatic
Nature on following and resident members; posts holding; no pay for members.

0-XNPCCore 3.4.02's changelog against what SC drives (as first triaged; the second part above settled the restock,
leadership, pay and guard items):
- Looks: `SdcsSpawnAmmoCount` and the look roll are only read by `SdcsLookApplier` (removed from the six and refused by
  X1b): no change. 3.4.02's "survivors aim at the chest of people" matches SC's chest aim (ScShooting.cs:217-219).
- **Ammo restock at the base: done (X20, the user's call: block it for members).** `SdcsAmmoRestockTick` (gated on the `Owner` cvar, which
  SC writes on members, ScNpc.cs:377) tops a member up to 120 rounds from the player's broadcast containers and unlocked
  chests within 15 m, beside SC's own stockpile resupply (ScAmmo.cs:13-40): free ammo past SC's accounting. An X18-style
  skip for `IsBody` would end it. "Choose a survivor's ammo" (E > Ammo...) on members: check it against X12/X16
  (Patches_Xnpc.cs:1162, 1502) and ScAmmo's refill type.
- **Pay/contracts: done (see above; the first triage was wrong about the seeding).** `SeedContract` is reached only through
  `MaybeStampHireDay`, which SC already skips; `RetainerLeader` was missing from `ContractCvars`; two guard hooks added.
- **Healing: decided (the user: all of Charismatic Nature for every member).** It reaches following members untouched
  and residents through X21 (see above); self-heal stays off (X15, Patches_Xnpc.cs:1443).
- **Guard/patrol: checked, no change (see above).** The patrol, the station walk and the spacing all need a leader; SC's
  posted residents, guards, stand-ins and enclave people have none.
- Levels/XP: SC strips levels and Progression on members and raiders (Patches_Xnpc.cs:472, ScXnpc.cs:334); the new XP from
  harvesting, repairs and crafting and the XP multiplier need no change (check the log for errors from them on members);
  "E > Your progress" shows nothing useful for members.
- Crafting/roles: **needs a decision**: E > Craft... and the Roles page (Cook, Blacksmith, Chemist, Crafter) are offered on hired
  survivors, so presumably on members (not checked): that crafting uses her pack and the base's chests and would run beside
  SC's residents' activities; hide them for members or let them be.
- Trade/selling: wild survivors now buy meds and ammo from the player with their own coins (`SdcsStockSeedFrame`): D4 kept
  XNPC's stock trade for SC's strangers (PORT_34 D4), so no change; what they buy lands in the stores SC's ammo and medic
  count (ScAmmo.cs:152).
- Voice: the relay rename (fixed here); caption keys per voice folder concern Voice Control's packs, not SC's clips: no
  change.
- Pickup: a survivor with anything in her pack can no longer be picked up; members carry SC's ammo and meds there and SC
  already refuses their pickup (Patches_VoiceControl.cs:349): no change.
- Bandits: SC's raiders are `bandit`-tagged, so 3.4.02's bandit player damage (half by default) applies to them; kept (the
  user's call); `SdcsKeepTarget`, the bandits' AI package and loot are class XML SC's raiders don't use.

Builds: SC 3.4 `92fee5b0980d855f5b4c9712e354e4dd` (`build\sc34_x1`; the first part alone was `d977d9a6`, the second
`cbedf29e`), 0 errors
(the two old CS0108 warnings). The 3.2 DLL was not rebuilt (`SurvivorCommunity.dll` unchanged). Not reviewed (Fable), not
installed.
