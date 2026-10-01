# SurvivorCommunity — in-game test checklist

Steps not yet tested in game. Tick a step, or remove it, once it has been confirmed. Everything below was added on
2026-09-28 and ships in the build `sc_stage7`, which is **not deployed yet**.

## Useful console commands (F1)

**Outposts and POIs**

- `sc poi` shows the POI you stand in: what it would produce, why, and whether it is cleared.
- `sc poi near 500` lists outpost candidates around you.
- `sc outpost list` shows each outpost's type, level, mode and state, and the network's radio state and debts.
- `sc outpost yield` shows tonight's numbers.
- `sc outpost security <id>` gives the security breakdown and the attack chance.

**Radio**

- `sc radio` lists the remembered radios and their live power.
- `sc scout <res>` scouts for a resource and ignores the once-a-day wait.
- `sc barter <give> <get>` trades once and ignores the once-a-day wait.

**Time and stockpile**

- `sc day` simulates one rollover, outposts included.
- `sc give mats 10` / `sc give fuel 5` fill the stockpile for fees and upkeep.
- `settime` also works, but bare `settime day` resets the world to day 1.

## Migration round 3 (M2, M4, build sc_r3): recruiting XNPC survivors, voice orders as member states, runners on V4

**Deployed 2026-10-01** (DLL md5 ae0ef852; Fable review: no defects, low fixes applied). **Needs a new game** (decision D5). 0-XNPCCore's survivors can now be recruited: the favour, join,
follow, go home, leave, death and clean-up flows run on them (EntityAliveSDXV4). A companion you hired the vanilla way
may join too (D9: joining ends the paid hire); somebody else's hire may not. Members and stand-ins get no 0-XNPCCore
kill levels (D8, new hook X3); strangers keep them. Following is 0-XNPCCore's own DynamicFollow (formations, follow
distances, stances, D6): SC's two follow actions are now only a fallback for a member without it. Every voice /
order-wheel order on a follower (stay, guard, guard and return, wander, a route, patrol, loot) is held as a member
STATE - still following, never "leaving", never "repaired" back to Follow. Runners on 0-XNPCCore bodies move at
`RunnerRunMps` / `RunnerWalkMps` (literal m/s). Only members and stand-ins (of either SCore class) use SC's door
opener now; doors 0-XNPCCore's follow opens for them are shut behind them too. A dead survivor's inventory window
(ammo, meds) drops as a backpack.

`sc xnpc <id>` gains a `member state:` line (order, Guarding, engage stance, follow distance, formation, move-to,
crouch; for a member status / heldOrder / followPending; hiredBy, dynamicFollow, ourDoorOpener), and its `move:` line
`passiveSpeedMod`, `running` and `runnerPath run/walk`. `sc members` prints the order description and the held state
in brackets. The hook line gains `x3-levels-off`.

- [ ] **Startup:** `patch verification: all 73 targets patched` (round 2's 69 plus AvatarSdcsNpcController
      SdcsLevelTick, UAITaskAttackTargetEntitySDXV4 Start and Update, and Voice Control's
      EntityMovementActuator.TryOpenBlockingDoor), no `MISSING PATCH`, no `patched an unlisted method`, no
      "0-XNPCCore method AvatarSdcsNpcController.SdcsLevelTick not found". No XML warning for SC's `utilityai.xml`
      or `dialogs.xml`, no "ai_package SCResident is not registered". The config line is unchanged.
- [ ] **P1, the dialog reaches 0-XNPCCore's survivors:** DevLink `xmlprobe dialogs` finds
      `/dialogs/dialog[@id='SdcsNPC']/response[@id='scJoinPoi']` and
      `.../statement[@id='start']/response_entry[@id='scJoinPoi']`. Talk to a wild `npcSdcsSurvivor…`: "I could use
      someone like you…" is offered (one of the two favours); no `HiredSDX` requirement is left on the four join rows.
- [ ] **Recruit a V4 survivor, both favours, at home and away:** do the favour, join. Inside the base she is stationed
      at once; outside she follows (PendingStation) until you walk in with her. `sc members` shows the
      `npcSdcsSurvivor…` class; `sc xnpc <id>`: `sc_member`, `Owner=` your entity id (SC stamps it on V4; SCore does
      not), `Persist=1`, `pair=` and `record=` the same two weapons (round 2's last prospect row: the member's
      `[member]` record now has `loadout=`).
- [ ] **P4, D8 levels off for members:** right after the join, `sc xnpc <id>` levels: every `SdcsLevel*` cvar `-`,
      `Progression=null`, buffs `SdcsNpcLevelBonus=False SdcsNoWear=False`; the hook line's `x3-levels-off` rises by
      about one per second per loaded member. Player.log has no `[SDCS-LEVEL] … start level` or `hired progression
      seeded` for her id after the join. A wild survivor nearby still shows `SdcsNpcLevel` and
      `SdcsNpcLevelBonus=True`. A posted member's outpost stand-in shows the same as the member (levels off).
- [ ] **D8, a member leaves:** "Ask to leave" (twice): within about a second Player.log has `[SDCS-LEVEL] <name>
      start level=…` for that id and `sc xnpc` shows the level cvars and buff again. *Open point for the user:* the
      level bonus included 0.2 HP/s regeneration, which members no longer have (D7 says healing is items only).
- [ ] **Kill XP still feeds SC's skills:** a member kills a zombie: `sc members` shows +5 Fighting (melee) or Shooting
      (ranged) XP; Player.log has no `[SDCS-LEVEL] xp <member>` line (no 0-XNPCCore progression on members).
- [ ] **P6, DynamicFollow replaces SC's follow:** walk and run 200 m with two V4 followers. They keep up without
      orbiting you, stand off at 0-XNPCCore's follow distance, and are teleported back when more than 60 m behind
      (V4's leader component). `sc xnpc <id>`: `dynamicFollow=True`. Player.log shows `[SDCS-BRIDGE]` lines, no
      orbiting. The radial "Distance" (close / normal / far) changes `followMin=`.
- [ ] **P5, voice orders are member states:** with a follower, type (or pick on the order wheel) in turn `@<name>
      stay`, `guard`, `guard and return`, `wander`, `show me a route`, then `@<name> follow`. After each: `sc xnpc`
      order is Stay(2) / Stay(2) with `guarding=1` / Guard(9) / Wander(3) / SetPatrolPoint(4) / Follow(1); Player.log
      has one `<name> now holds the order …` line per change and **no** `re-applying Follow` line for 30 s; the window
      shows "Following (holding)", "(guarding)", "(on guard)", "(wandering)", "(learning a route)", then plain
      "Following"; `sc members` the same in brackets. She stays where she was told and never leaves the community.
      Also: wheel **Formation** → `formation=<dist>m@<angle>` and she holds the slot; **Stance** aggressive / hold fire
      → `engage=…`; **Crouch** → `crouched`. Record a route of 2+ points and say `patrol`: Patrol(5), "(patrolling)".
- [ ] **"Show me a route" (expected quirk):** the Fable review found that SCore's SetPatrol re-adds buffOrderFollow, whose
      deferred start overwrites order 4 a frame later - so the order reads Follow(1) again and "(learning a route)" may
      never show. That is 0-XNPCCore/SCore behaviour; SC just keeps her following. Note what you see.
- [ ] **Guard and return far from home:** 300+ m from the base, `@<name> guard and return`. She guards that spot
      (0-XNPCCore's Guard), fights what comes and walks back to the spot - she is **not** walked toward her home post
      (SC's leash and roam are residents-only now). By day she does not roam toward home.
- [ ] **Holding while you go home:** join someone away from home (PendingStation), `@<name> stay`, walk into the
      base: she is **not** stationed or teleported home; the window says "Heading home (holding)". `@<name> follow`
      (she comes to you) and walk in together: stationed as usual ("<name> is home and settling in."). A member left
      holding far out for days (`sc day` several times) is never counted missing.
- [ ] **Relog while holding:** follower holding Stay, save and quit, reload: still Stay (the re-link keeps the held
      order; log `leader link lost (relog?) - re-hiring … keeping the order Stay` only if the leader id went stale).
- [ ] **Stationing clears 0-XNPCCore's movement state:** crouch a follower and put her in a formation slot, then
      "Go home" (dialog or window) inside the base: `sc xnpc` shows no `CrouchOverride` / `vcFormation*` /
      `vcMoveTo*`, `engage=` is kept; she stands upright at her post; the `move:` line shows `moveSpeed=2` (the class's
      walk, restored - not the 1 she had while following). Residents roam the base at a normal walk.
- [ ] **Dismiss is never fooled:** a spoken / typed `@<name> dismiss` sends her home (M0); SCore's companion window
      Dismiss still makes her leave (log `was dismissed through SCore - leaving the community`); none of the voice
      orders above ever does.
- [ ] **P7, D9 a hired companion joins:** hire a wild `npcSdcs…` through its dialog (pay the fee). The dialog now also
      offers "I could use someone like you…". Asking does **not** stop her following (no guard order for your own
      companion). Do the favour and join: log `<name> was your hired companion - joining ends the paid hire (Voice
      Control contract removed|none)`; `sc xnpc`: `Retainer*=none`, `hiredBy=` your id while following. `sc day` 8
      times (or `settime` a week on): no billing prompt, no auto-dismiss.
- [ ] *(MP only)* **Somebody else's hire** cannot be recruited: the join rows stay hidden ("scAlreadyHired").
- [ ] **Death on V4:** let a following member die: V4's own "Oh no! <name> has died. :(" and SC's death tooltip both
      show (accepted: the same pair the legacy class showed); a backpack with her inventory window (ammo, meds)
      lies where she fell; the roster, morale and deaths list update. Kill a wild V4 survivor: a backpack with its
      starting ammo. Player.log: no `npc-drop-window` error.
- [ ] **Leave and clean-up on V4:** "Ask to leave": sc_* cvars, SCResident, door flag, Persist and the leader link
      are gone (`sc xnpc`), CurrentOrder becomes 3 (buffOrderDismiss), no `CrouchOverride` or formation is left.
- [ ] **M4.4, missing member on V4:** make a resident missing - remove her body without killing it (a DevLink /
      world-tool entity remove), stay home 45 s: the window shows "Missing - Call back" - then "Call back" (or
      `sc restore <name>`): she respawns at her post with her face, name, pair and starting ammo, log `<name>: entity id
      A -> B (fresh body)` and `called back: respawned …`; the old id is on the doomed list; `sc xnpc <new id>` levels
      off.
- [ ] **P8, runner pace:** `sc run` (or the window) a V4 member on a physical run and walk beside her on the Outbound
      leg. `sc runners` shows `MeasuredMps` near 4.5 (Cardio / hunger / injury change it); `sc xnpc <id>` `move:`
      `running=True`, `runnerPath run/walk=2.81/2`. At the site and on the last stroll home she walks (about 1.6).
      Then set `RunWalkMps` to about 0.85-0.9 x the measured value (4 is 0.89 x 4.5).
- [ ] **P9, snapshot round trip on V4:** let the runner go offline (walk away beyond view distance, `sc runners`
      shows offline) and come back near her: `sc xnpc <id>` same name and face, the pair in `itemsOnEnterGame`, the
      same held weapon, the window's ammo kept (not refilled, `ammoSeeded=True`), levels still off. A quit and reload
      while she is out does the same. A stale same-id copy, if a chunk loads one, is hidden and removed (log
      `removing a stale copy of runner …`; 0-XNPCCore's own duplicate eviction may also remove it - harmless).
- [ ] **Legacy hidden runs on V4** (`PhysicalRuns` false, or no POI in range): she vanishes at home (SendOnMission),
      stays hidden and parked through a reload, and is visible again when the run resolves.
- [ ] **Garrison stand-in on V4:** post a member at an outpost and go there: the stand-in has her face, name and pair,
      guards near the flag, `sc xnpc <stand-in>` levels off and `ourDoorOpener=True`; talking to it does not save it
      with the chunk (it is gone after the outpost unloads and a fresh one appears next visit).
- [ ] **P10, doors:** a V4 follower walks through a double door and a gate (SC's opener: log `opened the door at …`
      with DebugLogging on), and through a hatch into the base (members may open hatches, D13); doors are shut
      behind her. A door 0-XNPCCore's follow opens for her (she is blocked by a closed unlocked door while following:
      log `… opened the door at … (Voice Control's follow) - shut behind them`) is shut behind her too. A wild
      survivor (`ourDoorOpener=False`) no longer opens your **locked** doors; it may still open unlocked ones
      (vanilla's opener). *Known gap:* with `MembersOpenHatches` false, 0-XNPCCore's follow can still open a hatch
      for a follower (its own opener, not SC's); it is then shut behind them.
- [ ] **P20, compass marker:** a V4 follower has no compass/map marker (npcSdcs classes have no `NavObject`, so V4
      registers none). Decide whether to append one to the six classes.
- [ ] **P21, vehicles:** a V4 follower vanishes when you get into a vehicle and reappears beside you when you get out
      (V4's leader component); arriving home in the vehicle with a PendingStation member stations her visible.
- [ ] **ScAim on the V4 attack task:** inert while 0-XNPCCore's packages name the SDX task. `sc aim <id>` stamp
      counts rise in a ranged fight exactly as in round 1 (the SDX task); nothing in the log about
      `UAITaskAttackTargetEntitySDXV4._actionIndex not found`.
- [ ] *Only with a non-XNPCCore pack NPC installed:* a recruited pack follower whose packages lack DynamicFollow
      (`sc xnpc` `dynamicFollow=False`) still follows with SC's own follow (walks in to 3 m and stands).

## Migration round 2 (M7a, M6, M8, M9, build sc_r2): XNPC survivors armed, spawned and the only survivors

**Deployed 2026-10-01** (DLL md5 9ae72bf9; Fable review: no defects, two low fixes applied). **Needs a new game** (decision D5). SurvivorCommunity no longer defines survivor classes of its
own: the `scSurvivor*` classes are gone (D11), and the community's survivors are 0-XNPCCore's
`npcSdcsSurvivor{Male,Female}T{1,2,3}`. Wild spawns (SC's own entries and rates, D10), radio contacts, outpost
walk-ins and `sc spawn` now make them. Each carries a pair of real vanilla weapons rolled by SC (one melee, one bow /
crossbow or, at `SurvivorFirearmChance` 12%, a firearm; tier capped by the lower of the class's T1-T3 and your
gamestage tier) and finite ammo: 30 rounds, or 20 arrows or bolts, once, in the inventory window
(`XnpcSpawnAmmo` / `XnpcSpawnArrows`). 0-XNPCCore's own weapon handling picks which of the two is in the hand (the
gun while it has ammo, the melee weapon up close and when dry); its melee choice is always the pair's (new hook
X6). Raiders are unchanged. They still cannot be recruited (M2); there is no ammo resupply yet (M7b).

`sc xnpc <id>` now also prints `pair=`, `record=`, `ammoSeeded=` and `SdcsLastMelee=`; the hook line gains
`x6-pair-melee` / `x6-xnpc-choice`. `sc loadout <id>` shows the pair with family, tier, the tier cap now and
whether the starting ammo was given.

- [ ] **Startup:** `patch verification: all 69 targets patched` (one more than round 1: AvatarSdcsNpcController
      SdcsChooseMeleeFallback), no `MISSING PATCH`, no "0-XNPCCore method ... not found". The config line ends with
      `xnpcWeapons=41 xnpcSpawnAmmo=30/20`. No `config XnpcWeapons: cannot parse`, `XnpcWeapons: no item`,
      `config has no XnpcWeapons block` or `no weapons for 0-XNPCCore's survivors` warning. No XML warning for SC's
      entityclasses, entitygroups, spawning or blocks.xml.
- [ ] **The XML took** (DevLink `xmlprobe`): no `scSurvivor…` entity class exists (`se` lists none); the
      `scSurvivorGroupGS01` / `GS50` / `GS100` groups hold `npcSdcsSurvivorMaleT1` + `FemaleT1` (T2, T3) at 3 each and
      `none` 94 last; the config block has a `XnpcWeapons` class with 41 entries; no biome has 0-XNPCCore's own
      `dnSdcs…` / `nnSdcs…` spawn entries, and SC's `scSurvivorWildDay…` entries are there.
- [ ] **`sc spawn female`** (probe for M7.1/M7.2): spawns `npcSdcsSurvivorFemaleT<your tier + 1>`; the output names the
      pair (two vanilla items) and the tier cap. `sc xnpc <id>`: `itemsOnEnterGame=[<melee>x1, <ranged>x1]` - no ammo
      stack, no pipe pistol, no club unless rolled - `_defaultWeapon` the melee one, `pair=` the same two,
      `ammoSeeded=True`, and the ammo line shows 30 (or 20 arrows/bolts) of the gun's basic ammo in the window.
      Within a second she holds the gun (Player.log `[SDCS-REARM] … -> '<gun>'` for her id). Open her inventory
      ("show me your inventory"): the ammo stack is there.
- [ ] **Weapon Menu:** the SdcsNPC dialog's weapon menu lists both of her weapons; picking the other one swaps it.
- [ ] **Fixed weapons:** `sc spawn male PrimitiveBow Club` (short names), `sc spawn female gunRifleT0PipeRifle
      Lumberjack` (an item and an armor set), `sc spawn firearm` and `sc spawn bow melee` (one of that family) give
      what was asked, the other kind rolled. `sc spawn PipeRifle PipePistol` refuses (both ranged).
- [ ] **Tiers by gamestage:** below gamestage 50, `sc spawn` a few times: only tier 0 weapons (pipe guns, primitive
      bow, the tier 0 melee list). `sc loadout sim 2000` prints about 12% firearms and the tier spread for your
      gamestage; at gamestage 100+ tier 1 and 2 weapons appear.
- [ ] **Finite ammo, given once:** let her shoot zombies until the window is empty. She reloads from the window
      while it has rounds, then 0-XNPCCore fails over to melee (`[SDCS-FAILOVER] … failover to melee: '<her melee
      weapon>'`) and `x6-pair-melee` rises. Save, quit, reload: the window is still as she left it (not refilled),
      `ammoSeeded=True`, same pair, same held weapon, the same two items on `itemsOnEnterGame` (probe P9, the
      weapons part). Hand her a stack of her gun's ammo through the window: 0-XNPCCore restores the gun
      (`[SDCS-FAILOVER] restore`).
- [ ] **Melee up close:** a zombie within 3 m: she swaps to the pair's melee weapon (`x6-pair-melee`), back to the
      gun at about 5 m when it has ammo.
- [ ] **`sc loadout <id> Rifle`** (or `gunMGT1AK47`, or `hunting`): the pair changes, `itemsOnEnterGame` follows,
      she holds the same kind as before, SC writes no `CurrentWeaponClass` (0-XNPCCore keeps it 0/1). An ambiguous
      name (`sc loadout <id> bow`) lists the matches. Note: a new gun gets no ammo of its own (the starting ammo was
      given once), so it runs dry unless you hand her some.
- [ ] **`sc arms <id> melee` / `ranged`** puts that weapon in her hand (0-XNPCCore may switch back by its own rules).
- [ ] **`sc loadout near`** lists 0-XNPCCore survivors and raiders with their pairs and the firearm share;
      **`sc weapons`** prints the survivors' vanilla weapons (with short names) and the raiders' tokens.
- [ ] **Radio call:** the contact line reads like "a survivor (Primitive Bow)"; the registry's `[contact]` has
      `loadout=`; when the chunk loads, the survivor there is an `npcSdcsSurvivor…T<tier>` with exactly that pair
      (`sc loadout <id>` source `spawn`), a rolled SC look and starting ammo. Same for an **outpost walk-in** (log
      `walk-in: npcSdcsSurvivor…`).
- [ ] **Wild spawns (D10):** play a day and a night in wilderness, a town and a city centre. 0-XNPCCore survivors
      turn up about as often as SC's survivors used to (6% per area roll): T1 in the wild and towns by day, T2 in the
      wild by night and city centres by day, T3 in towns and city centres at night. They wear SC looks and names,
      carry pairs within their cap (`sc loadout near`) and ammo. No pack NPC, no XNPC preset look.
- [ ] *Only if a POI uses 0-XNPCCore's NPC sleeper volumes (`S_-Group_NPC_*`):* the survivors it spawns are ours
      (SC look, pair, ammo).
- [ ] **Prospect record:** a radio contact becomes a prospect when it spawns; after the next save
      (`sc save`) its `[prospect]` record in `survivor_community.txt` has `loadout=<ranged>,<melee>`, the same pair as
      `sc loadout <id>`. Reload: `sc loadout <id>` source `record`. Joining (and so the `[member]` record and the
      respawn sites - call-back, stand-in, runner - bringing a member back with the same two weapons) needs M2:
      *round 3 re-runs this.*
- [ ] **Raiders unchanged (M8):** `sc spawn hostile`, `sc spawn hostile female Bat Pistol`, `sc loadout <raider>`
      and the W2 rows below behave as before (raider classes, groups, spawns, NPC weapon items and their
      Localization are byte-identical to round 1's). Raiders get no `no_voice` tag (see the report: 0-XNPCCore's
      chat component also turns on their vanilla door opening, so the tag would change raider doors; untested).
- [ ] **An old save (M9), on a copy only:** loading a pre-migration save logs `this save predates the XNPCCore
      migration: start a new game. N member(s) of entity classes that no longer exist, …` once. A pending radio
      contact of an `scSurvivor` class answers as an `npcSdcsSurvivor` (`… no longer spawns - … answers instead`).
      After `MissingDaysToPurge` (30) days with the base loaded (`sc day` repeatedly), each such member's removal is
      preceded by the same line naming them.

## Migration round 1 (M3 + M5, build sc_r1): looks, names, respawns, healing and aiming on XNPC survivors

**Deployed 2026-10-01** (DLL md5 5c75b40c; Fable review: no defects). Self-test 2026-10-01 (DevLink, test save): patch verification 68/68, XNPC look applier off, SC look/name/half armor on spawned npcSdcs survivors, x1/x2/x4/x7 hooks firing, ranged fight drives ScAim, no SC errors. SurvivorCommunity now takes over 0-XNPCCore's own survivors, `npcSdcsSurvivor{Male,Female}T{1,2,3}`
(the six classes in `XnpcSurvivorClasses`, Config/blocks.xml): they wear SC's rolled faces and mixed vanilla armor at
SC's half protection (no XNPC preset, no full-protection set, no no-wear buff), SC's names, heal only with medical items
and the bandage animation, and raise their gun or draw their bow while shooting. Everything SC's own survivors and
raiders do is unchanged. They still cannot be recruited (M2), nothing spawns them in the wild or from the radio yet
(M6), and they still carry 0-XNPCCore's starting weapons (M7). So spawn them by hand: `se` lists the entity ids, then
`se <your id> <entity id>` for e.g. npcSdcsSurvivorFemaleT2. Kill them afterwards. *Since round 2 (above) `sc spawn`,
the radio and wild spawns make them, armed with SC's pair; the rows below that mention SC's own survivors
(`sc spawn female Lumberjack` as an SC survivor, the radio walk-in "SC survivor") now get an XNPC survivor.*

`sc xnpc <id|name>` prints one NPC in detail (class, whether it is one of ours, where its look came from, archetype,
worn items, names, weapons and ammo, XNPC's level cvars and buffs, orders, speeds, medical state, and the hit counters of
the new hooks). Plain `sc xnpc` adds a line with those hook counters (`x1-look`, `x1b-applier-blocked`, `x2-eyes`,
`x4-wear`, `x5-…`, `x7-update`).

- [ ] **Startup:** `patch verification: all 68 targets patched` (8 more than M1: EModelSdcsNpc createModel and
      GenerateMeshes, MinEventActionSdcsApplyLook Execute, AvatarSdcsNpcController SdcsWearTick, SdcsFailoverTick,
      SdcsCloseRangeTick, SdcsTryWorldEntryRearm and Update), no `MISSING PATCH`, no "unlisted method", no "EModelSdcsNpc
      ... not found", no "0-XNPCCore method ... not found". No XML warning for SC's entityclasses.xml.
- [ ] **The XML took** (DevLink `xmlprobe entityclasses`): none of the six classes has an `SDCS Look` effect group, each
      has `SurvivorCommunity worn armor scale`, `BagItems` and `HarvestItems` are `none`, and `Names` is SC's list
      (Alex, Ben, Carl… for the Male classes, Amy, Beth, Cara… for the Female ones).
- [ ] **Look (probe P2):** spawn a female T2 next to you. She has an SC face (any race, hair, eye colour; not one of
      XNPC's presets) and mixed vanilla armor. `sc xnpc <id>`: `xnpcBody=True`, look `source=roll`, archetype
      `scSurvivor (Female)`, worn items listed, `x1-look` counted once. Player.log has no `[SDCS-LOOK]` or `[SDCS-NAME]`
      line for her id, and `x1b-applier-blocked` stays 0. If she is still there after save, quit and reload, she may
      have a new face: like SC's own strangers, only members and prospects have their look kept in the registry.
- [ ] **Name:** her name comes from SC's pool (Amy, Beth, …), not XNPC's (Alice, Angela, …).
- [ ] **Eyes (P3):** close up, her eyes sit in their sockets and follow you. `sc gear <id> Lumberjack` (a rebuild): she
      wears the lumberjack set, the eyes still sit right, and `x2-eyes` rose. `sc look <id>`: a new face, same clothes.
- [ ] **Armor wears (D2):** `sc xnpc <id>` shows `SdcsNoWear=False` and `x4-wear` rising about once a second. Hit her
      a few times with a club: the wear figure after each worn item in `sc xnpc` (used / max) goes up.
- [ ] **Half protection:** compared with an SC survivor in the same set (`sc gear <id> Lumberjack` on her,
      `sc spawn female Lumberjack` next to her), she takes about the same damage per hit, not far less.
- [ ] **Healing (P16):** hurt her below 60% (shoot her; she is not one of your people, so friendly fire is allowed).
      Within a second or two she takes a first aid bandage into her hand, plays the bandage animation (3P_Bandage on
      `UpperBodyUseAndReload`), puts her weapon back, and her health climbs. `sc xnpc <id>`: `SdcsIsHealing=False`,
      `SdcsIsHealing refused` counting, medic `stocked=True` with 0-2 bandages left. During the use no
      `[SDCS-FAILOVER] restore`, `[SDCS-CLOSEMELEE] exit` or `[SDCS-REARM]` line for her id; `x5-…` may count. If she
      had no bandage, nothing happens (no free heal): `sc xnpc` shows `bandages=0 kits=0`.
- [ ] **Bandage in the hand:** the bandage is visible in her right hand (it may sit at the hand's origin rather than in
      the palm: XNPC applies no per-item hand offset after a swap; note it if so).
- [ ] **Aim (P17):** give her a fight with a gun in hand (zombies near, or `sc spawn hostile` a raider some distance
      off). `sc aim <id>`: the attack-task count rises, her state reads `aiming`, layer 16 (`AdditiveAimPoses`) at weight
      about 1 while she shoots, and the gun comes down a couple of seconds after the last shot, during a reload, and when
      XNPC switches her to melee up close. `x7-update` rises every frame. `game reads AimingGun=False` (visual only).
      With a bow: layer 18 draws before each shot.
- [ ] **Get-up (P12, `XnpcBodyGetUp`):** knock her down (sledgehammer power attack): she plays the knockdown and gets
      up on the spot without floating up into the air. If she gets up badly, try `XnpcBodyGetUp` false and compare.
- [ ] **Jump (P11, `XnpcBodyJumpAnim`):** watch her follow a path over a one-block step: a real jump animation, not an
      idle-pose hop. Compare with `XnpcBodyJumpAnim` false if unsure.
- [ ] **Hits land (P15, `XnpcBodyColliderLayers`):** shooting her in the torso registers hits every time. With
      `XnpcBodyColliderLayers` false, check whether hits still land (if they do, XNPC does not need the fix).
- [ ] **Despawn (P13):** kill five of them and let them despawn (or walk away until they unload): no error from
      SurvivorCommunity in the log (`sdcs-release`), and memory stays flat over a few rounds.
- [ ] **No "no model child" flood (P14):** grep Player.log for `AvatarSdcsNpcController: no model child` after
      weapon swaps (XNPC's own, nothing from SC's bandage use).
- [ ] **XNPC's other classes untouched:** spawn an `npcSdcsTest` or `npcSdcsFemaleAK47`: it keeps XNPC's preset look and
      healing, and plain `sc xnpc` counts it under "0-XNPCCore's other classes" and its hooks under "stepped aside".
- [ ] **Compass marker (P20):** none of SC's survivor classes ever showed a member on the compass or map (their
      inherited NavObject only shows Twitch spawns and quest sleepers), so the XNPC survivors get none either. Nothing to
      test; say if you want one.
- [ ] **Respawn sites, SC's own survivors (regression):** these now go through one helper (ScSpawn). With an existing
      member: `sc restore <member>` on a missing one, an outpost with a posted member (stand-in appears), a distress
      call (`sc distress <member>`, stand-in at the POI), and a physical runner whose snapshot is unreadable (fresh body).
      Each comes back with their own face, clothes and name, exactly as before.
- [ ] **Radio walk-in (regression):** a radio contact still spawns an SC survivor with a rolled face and clothes.

## Migration task M1 (build sc_m1): the type seam

**Deployed 2026-10-01** (DLL md5 68a7aca1; Fable review: no defects). Code plumbing only: SurvivorCommunity now holds its NPCs as plain EntityAlive and asks the new
`ScNpc` for anything only SCore's two NPC classes have, so the same code can later drive 0-XNPCCore's survivors
(EntityAliveSDXV4). Nothing should look different with the current members, who are all still the old class. XNPC
survivors still cannot be recruited (M2).

- [ ] **Startup:** `patch verification: all 60 targets patched` (5 more than M0: SetDead, PostInit, IsSavedToFile,
      ExecuteAction and Write on EntityAliveSDXV4), no `MISSING PATCH`, no "unlisted method", no "EntityAliveSDXV4
      ... not found".
- [ ] **The existing save plays as before:** run the M0 rows below; in particular join, follow, go home, a supply run
      (hidden and physical), an outpost posting, a missing member's return, a death, and doors.
- [ ] **An XNPC survivor, spawned by hand** (as in the M0 row): its dialog still has none of the recruit lines,
      `sc anim <its name>` now finds it, and it does not open the base's locked doors (only members do).

## Migration task M0 (build sc_m0): SCore 3.2.36.628 + 0-XNPCCore 3.2.06

**Deployed 2026-10-01** (DLL md5 4a06d2d6, with the regenerated `Config/entitygroups.xml` and `spawning.xml`).
Not yet tested in game: the user tests everything once the whole migration (M0-M9) is done. The frameworks are upgraded: 0-SCore 3.2.7.728 -> 3.2.36.628, and 0-XNPCCore 3.1.00 -> 3.2.06,
core only (no voice add-on, so no spoken voice, voice input or AI chat; typed `@` commands and the order wheel work).
The old folders are backed up in `_backups\m0_2026-10-01\` in the game root. SurvivorCommunity still uses its own
NPC classes. What changed for it:
- its player-body code ignores 0-XNPCCore's new survivors (npcSdcs*), which build, animate and heal themselves;
- members and their outpost stand-ins are never billed by Voice Control's weekly hire fee, but keep their voice;
  a typed or spoken "dismiss" sends a member home, and "pick up" is refused;
- members open hatches and drawbridges on their way (and shut them after): `MembersOpenHatches` in Config/blocks.xml;
- raiders open unlocked doors (SCore does that now for every NPC);
- Cowardly members flee again (0-XNPCCore 3.2 dropped its flee action; SC has its own now);
- "Show me a route" / "Cancel patrol" are hidden on members, like the other vanilla orders;
- W0: 0-XNPCCore's new survivors and its NPC fox no longer spawn (its sleeper groups carry our survivors instead).

`sc xnpc` shows the billing guard's counters, the body hooks that met an npcSdcs survivor and stepped aside, and any
loaded member that still has a hire contract.

- [ ] **Startup:** the log shows SCore 3.2.36.628 and 0-XNPCCore 3.2.06 loading, no `TypeLoadException ...
      EntityItemStore`, and SC's `patch verification: all … targets patched` (4 more than before: the Voice Control
      ones), no `MISSING PATCH`, no "unlisted method", no "billing guard is idle". No XML warning for SC's
      entitygroups.xml, spawning.xml, dialogs.xml or utilityai.xml.
- [ ] **The existing save:** members are at their posts or following as before; outposts, runners and the community
      window behave as before. Then run the open W0-W2 rows below.
- [ ] **No bill:** say "Follow me" to a resident, then `sc xnpc`: `contract-refused` has counted and "0 with a hire
      contract". After 8+ in-game days with a follower, no payment prompt and no "I'm done. Pay up" dismissal.
- [ ] **Dismiss by chat:** next to a following member, press T and type `@dismiss`: they answer "Heading home." and
      go home (still in `sc members`, never "left the community"). `@pick up` gets "I'll walk, thanks." `@hire you`
      to a resident gets "Keep your money..." and no hire window opens (`sc xnpc`: hire-refused).
- [ ] **Voice still works** for members: typed `@follow me` / `@stay` reach them, and greetings appear. Known until M2:
      the community puts a following member back on Follow within a second, so a voice "stay" or "guard" on a follower
      is undone (log line "following but the order is ... re-applying Follow").
- [ ] **Hatches and drawbridges:** a member whose path runs through a hatch or a lowered-bridge gate opens it and
      shuts it behind them. With `MembersOpenHatches` false they leave both alone.
- [ ] **Raiders and doors:** a raider (`sc spawn hostile club` outside your base) opens an unlocked door; a locked one
      stops it.
- [ ] **Two door openers:** SCore's own opener now works too. No door slams on a member or on you, and none flickers
      open and shut.
- [ ] **Cowardly:** a Cowardly member runs from a zombie within 15 m instead of fighting it.
- [ ] **The patrol rows:** a following member's dialog has no "Show me a route" or "Cancel patrol".
- [ ] **No new XNPC survivors or NPC foxes** turn up in the wild or in POIs over a few days. Vanilla foxes still do.
- [ ] **An XNPC survivor, spawned by hand** (`se` its id from the entity list, e.g. npcSdcsSurvivorFemaleT1): it builds
      its own body and walks and fights normally, its dialog has none of the recruit lines, and `sc xnpc` counts body
      hooks stepping aside (carry, holdtype, ... ). Kill it afterwards.
- [ ] **Followers in a fight:** they now hold a defensive "cover me" stance (Voice Control's MayEngage): they fight
      what hits them or threatens you, and don't chase enemies more than 8 m from you. Residents, runners and
      stand-ins fight as before.
- [ ] **Bows hurt now:** SCore 3.2.29+ fires NPC bows and crossbows at full draw (they used to do 1 damage per
      arrow), so bow-armed survivors and raiders are much more dangerous. Watch the balance.
- [ ] **Crouching:** NPCs no longer duck under low overhangs (CrouchType 0). Check that the stuck/unstick log lines
      don't increase at low doorways or porches.
- [ ] **A following member stuck behind something** may be teleported to you by Voice Control's follow assist
      (after about 5 s). Note if it fights the community's own unstick.

## Weapons task W2 (build sc_w2): two weapons each

**Deployed 2026-10-01** (DLL md5 e77bb729, with the regenerated `Config/entityclasses.xml`, `Config/entitygroups.xml`
and the new settings in `Config/blocks.xml`). Not yet tested in game. Every survivor and raider with the player's body now carries two weapons: the one its class is
named for, and one of the other kind rolled for it (a club survivor gets a bow, crossbow or - rarely - a gun; a rifle
raider gets a melee weapon). Ranged weapons are mostly bows and crossbows: a ranged weapon is a firearm 12% of the
time (`SurvivorFirearmChance`), for wild spawns, console spawns and radio contacts alike. Pipe guns and the primitive
bow are allowed from day 1. Nobody switches weapons on their own yet (that is W4) - they hold their class weapon
unless you use `sc arms`. Members keep the pair only until they are re-created by a supply run return, a missing-member
call-back or as a stand-in (W3 stores it in the registry). Settings in Config/blocks.xml: `SurvivorSecondWeapon`,
`SurvivorFirearmChance`, `SurvivorRangedMainChance`.

*Since migration round 2 the survivor rows here apply to raiders only: SC's survivor classes are gone, and the
0-XNPCCore survivors that replace them roll both weapons (see round 2 at the top).*

Commands: `sc loadout <id|name>` shows the pair, what is held and the NPC's item list; `sc loadout <id|name> <weapon>
[weapon]` gives it another pair; `sc loadout near [r]` lists the pairs around you with the firearm share;
`sc loadout sim [n] [hostile]` rolls n console spawns without making anyone; `sc arms <id|name> melee|ranged` puts one
of the two in the hand; `sc spawn [hostile] <weapon> <second weapon>` spawns with both chosen.

- [ ] **Startup:** `patch verification: all … targets patched`, no `MISSING PATCH`, no "unlisted method". The config
      line ends `secondWeapon=True firearmChance=0.12 rangedMainChance=0.4`. No XML warning for entityclasses.xml or
      entitygroups.xml.
- [ ] **A club survivor** (`sc spawn club`): the spawn line shows `(<ranged> + Club)`; `sc loadout <id>` lists a
      ranged weapon (mostly a bow or crossbow) and the item list `meleeNPCClubWood <bow item> <arrows>`. It holds
      the club.
- [ ] **`sc arms <id> ranged`:** the gun or bow appears in the hand with the right third-person hold and walk, and it
      shoots zombies (a bow draws, a gun shoulders - W1). `sc loadout <id>` reads `CurrentWeaponClass=1..3`.
- [ ] **`sc arms <id> melee`** switches back to the club; `CurrentWeaponClass=0`; it swings at zombies.
- [ ] **A gun survivor** (`sc spawn rifle`): its second weapon is a melee weapon, and `sc arms` works both ways.
- [ ] **A raider** (`sc spawn hostile club`, then `sc arms <name> ranged`): the same, on a hostile.
- [ ] **Chosen pair:** `sc spawn club pipepistol` gives exactly that pair; `sc spawn club machete` refuses (both melee);
      `sc spawn club rocketl` refuses (the launcher only comes with its own class).
- [ ] **Save and reload a stranger holding its second weapon:** `sc spawn club`, `sc arms <id> ranged`, save and quit,
      load. `sc loadout <id>`: the same pair, holding the ranged weapon (or the club) - never empty hands - and
      no errors in the log. (Console spawns persist; wild survivors are never saved and roll anew.)
- [ ] **Save and reload a raider:** the same pair after the reload, holding its class weapon (raiders always come back
      on their class weapon).
- [ ] **An existing save:** survivors, raiders and members already in the world get a second weapon on the first load,
      and keep the same one on the next load (`sc loadout <id>` before and after).
- [ ] **Firearms are rare:** `sc loadout sim 2000` reads about 12% (and `sc loadout sim 2000 hostile`). In the world,
      `sc loadout near 200` after some roaming: few firearms. Optional: 50 x `sc spawn`, then `sc loadout near` - about
      6 of 50 with a firearm.
- [ ] **Day-1 world (gamestage under 50):** wild NPCs and radio contacts carry only early weapons: melee, the
      primitive bow, now and then a pipe gun.
- [ ] **Rocket launcher survivor** (`sc spawn rocketl`): its second weapon is melee; no NPC ever rolls a launcher.
- [ ] **Switch off:** `SurvivorSecondWeapon` false (world reload): new NPCs carry only their class weapon; `sc loadout`
      says it is off.
- [ ] **No new errors** (`loadout-…` entries) during a fight with several NPCs, and members still use bandages
      (`sc loadout` refuses while one is in use).

## Weapons task W1 (build sc_w1b): visible aiming

**Deployed 2026-10-01** (DLL md5 ace5b130, commit 0562d4d). Not yet tested in game. Survivors and raiders with the player's body shoulder their gun while they
shoot and lower it about 2 s after the last shot. Bows draw before each release. It is only a pose: their accuracy and
critical hits are unchanged. Settings in Config/blocks.xml: `SurvivorAimPose`, `SurvivorAimVisualOnly`,
`SurvivorAimHoldSeconds`. Already proven live before the code (2026-10-01, DevLink): the `IsAiming` flag shoulders the
rifle (standing and crouched), and the bow draw/release cycle works on the player rig.

`sc aim` lists every NPC in a ranged fight; `sc aim <id>` shows one in detail (why the gun is up or down, the
animator flags, the aim/bow/reload layers) and the hook counters. `core SurvivorCommunityDev list 40` finds ids.

- [ ] **Startup:** the log says `patch verification: all … targets patched`, including `patched EntityAlive::get_AimingGun`
      and `patched UAITaskAttackTargetEntitySDX::Start` / `::Update`, with no `MISSING PATCH` and no "unlisted method".
- [ ] **A rifle survivor fighting zombies** (`sc spawn rifle` near a zombie): the rifle comes up to the shoulder
      (head down at the sights) within about a quarter second of the fight starting, stays up between shots, and
      comes down about 2 s after the last shot. `sc aim <id>` during the fight: state `aiming`, animator
      `IsAiming=True`, layer AdditiveAimPoses playing a `…_Additive_Aim` clip.
- [ ] **A pistol raider** (`sc spawn hostile pistol`) raises the pistol when it shoots at you or at zombies.
- [ ] **Crouched:** an NPC that crouches while shooting shows the crouched aim (layer clip `…_Additive_crouchAim`).
      To force it: `core SurvivorCommunityDev setparam <id> IsCrouching 1`.
- [ ] **Reload:** when the magazine runs out the gun comes down for the reload clip, then back up. `sc aim <id>`
      reads `reloading` during the clip.
- [ ] **Gun-butt:** when a zombie reaches a gun survivor and they bash it with the gun, the gun is not shouldered
      (`sc aim` reads `melee`). Back to shooting, it comes up again.
- [ ] **Melee weapon:** a club or knife survivor never raises anything (`sc aim <id>`: state none).
- [ ] **Bow** (`sc spawn primitivebow`, or `compoundbow` / `lbow`, near a zombie): draws before each release, draws again after it, and lowers the bow
      about 2 s after the last shot. At 75 shots a minute a shot can come mid-draw; a short draw is expected, no draw
      at all is a bug. Kill its target mid-draw: the drawn bow lowers within about a second (`sc aim <id>`
      then reads `calm` 2 s later, and the BowDrawAndFire layer is back at BowHold). Note: the probe survivor **Opal (id 1092)** near where you stood at 19:40 on day 3 holds a
      primitive bow through the dev core (her class is Rifle). She is a stray test NPC; use her for this row, then
      kill her.
- [ ] **Crossbow** (`sc spawn xbow` or `compoundcrossbow`): shoulders like a gun (crossbows use the aim pose, not the bow draw).
- [ ] **Rocket launcher** (`sc spawn rocketl`): unchanged (its hold pose is already shouldered).
- [ ] **Using an item mid-fight** (a member bandaging themselves while armed): the gun lowers for the use clip.
- [ ] **Visual only:** while an NPC is aiming, `sc aim <id>` shows animator `IsAiming=True` but `game reads
      AimingGun=False`, and the `AimingGun getter` count rises. That is what keeps spread and criticals unchanged.
      Optional comparison: with `SurvivorAimVisualOnly` false (and a world reload) the same line shows
      `AimingGun=True` and their shots group visibly tighter.
- [ ] **Switch off:** with `SurvivorAimPose` false (world reload) nobody raises a gun; `sc aim <id>` reads `off`.
- [ ] **No new errors** in the log during a 2-minute fight with several shooters (no `aim-drive` / `aim-stamp` entries).

## Weapons task W0 (build sc_w0): no new pack NPCs

**Deployed 2026-09-30** (DLL md5 572d05a6, with the regenerated `Config/entitygroups.xml` and
`Config/spawning.xml`). Nurse, Baker, Harley and the NPC fox (and any character pack's NPCs that sit in the same
framework groups) no longer spawn. Those already in your world stay as they are. Side effect: XNPCCore's two
plain wild animals (arctic fox, Jeerkoa) rode the same spawn entries and no longer spawn in the wild either.
*Since migration round 2 "player-body survivor" in these rows means a 0-XNPCCore survivor (`npcSdcsSurvivor…`), and the
log line reads `radio contact: npcSdcsSurvivor…`.*

- [ ] **An existing save loads cleanly.** The log has no "Empty entity groups not allowed", no XML patch warning
      naming SurvivorCommunity's `entitygroups.xml` or `spawning.xml`, and no exception while entity groups load.
      Any pack NPC already in the world (or in the community) is still there and behaves as before.
- [ ] **A fresh game, played for an in-game day and night** (wilderness, a town, a city centre): no Nurse, Baker,
      Harley or NPC fox turns up. Player-body survivors and raiders still do, about as often as before.
      `core SurvivorCommunityDev spawns` lists no `npcHarleyGroup*` or `npcFriendlyCoreGroup*` entries in any biome.
- [ ] **Radio "call for survivors"**, two or three times: every contact is a player-body survivor. The log line reads
      `radio contact: scSurvivor…`.
- [ ] **An outpost walk-in** is a player-body survivor.
- [ ] **A radio contact rolled before this build as a pack NPC** (only if one is still pending): a player-body
      survivor waits at the POI instead. The log says `pack NPC … no longer spawns - … answers instead`.
- [ ] **`sc spawn` and `sc spawn hostile`** still give only scSurvivor / scRaider classes (unchanged).
- [ ] *Later, only if you install an NPC POI pack:* its NPC sleeper volumes now spawn our survivors (friendly
      groups) or raiders (bandit groups). Player-body NPCs have never been tested as POI sleepers: expect them
      standing upright where a pack NPC would lie or sit (the player animator has no sleeper poses).

## 2026-09-30 build (sc_stage10): doors, and runners run

**Confirmed by the user on 2026-09-30: doors work and runners run.** The task is closed. The detailed items below are
kept for regression checks.

Turn on `DebugLogging` (Verbose) in Config/blocks.xml to see "opened the door at …" and "shut the door at … behind …".
The startup log should say `patched EntityMoveHelper::CheckForDoorAndOpen` with no "unlisted method" warning.

- [ ] **A resident roaming** through a closed door: it opens before they reach it, and shuts 2-3 s after they are
      past it.
- [ ] **A follower** follows you through a door you closed, with no teleport.
- [ ] **Runners** leave through a closed base door or gate. At a POI they open the ground-floor doors they wander
      through.
- [ ] **Your own locked door** (you locked it): members open it and shut it again, and it is still locked. Set
      `MembersOpenOwnerLockedDoors` to false and they are stopped by it (stuck, then the usual teleport to the post).
- [ ] **A locked POI door** (not yours) is never opened.
- [ ] **Double doors and 2-wide gates:** crossing either leaf opens the door.
- [ ] **Drawbridges and hatches** are never touched.
- [ ] **Standing in or right beside a doorway** keeps it open. The door never swings shut on you.
- [ ] **Strangers and traders** don't open your doors. A member who leaves stops opening them.
- [ ] **No new "was stuck" lines** at doorways. A resident no longer turns back from a door it just opened.
- [ ] **Runners RUN** the way out and the way home (legs running, not sliding). They walk while searching the site
      and on the last stretch from the edge of the base to their post. They don't stop at every waypoint.
- [ ] **Offline pace.** Escort a runner for a minute, then compare `sc runners` "moves at X m/s" with the offline
      pace (now 4.0). Set `RunWalkMps` to about 0.85-0.9 x the measured figure if it is far off.

## 2026-09-30 build (sc_stage9): physical supply runs

Runners now walk to a real POI and back instead of vanishing. `sc runners` shows each runner's leg, online/offline
state, position, distance, snapshot size, measured walking speed and how long the last re-create took. Test
controls: `sc run <name> skip` (end the current leg now), `sc run <name> offline`, `sc run <name> recall`,
`sc run <name> home`.

- [ ] **Sending.** Send a resident for food. The tooltip names the destination POI, its distance and an ETA day. The
      runner walks out of the base on foot (no vanish), toward the POI.
- [ ] **Escort.** Follow them. They walk waypoint to waypoint, fight zombies that reach them, then carry on. At the
      POI they wander its ground floor, pausing now and then, for about 6 game hours, then head home.
- [ ] **Out of range.** Stay home and watch them leave. Past about 96 m (view distance 7) they are captured out of
      sight. The log says "went offline … snapshot N bytes". `sc runners` shows them moving along the route.
- [ ] **Meeting them on the road.** Drive toward their route. They reappear on the road as the same survivor
      (name, look, health, weapon), with no pop-in right in front of you. The log says "is back in the world …
      built in N ms". **Note the ms** (this is the one-time cost of building the NPC).
- [ ] **Coming home.** At the edge of the base the haul is added ("is back with N bundles"). They walk in and are a
      resident at their post again.
- [ ] **Quit mid-walk.** Quit while a runner is in view far from home, then reload. There is exactly one of them,
      carried on from where they were, and no duplicate at the base.
- [ ] **Crash mid-walk.** Kill the game process while a runner is walking in view, then reload. They resume from the
      last snapshot (up to 60 s old). There is no duplicate.
- [ ] **Bed respawn / teleport.** Die (or teleport) while a runner is in view. The log says "captured as their chunk
      unloaded". They carry on offline.
- [ ] **Distress with the real runner.** `sc run <name> skip` until they are AtSite, then `sc distress <name>` (radio
      on). The rescue quest points at their POI. Go there: zombies spawn around the runner themself and go for them.
      No stand-in. Kill them all: rescued, bonus haul, and they walk home.
- [ ] **Runner dies in the field.** Let the zombies kill them. They leave a real body there, the quest fails, and the
      death is in the ledger.
- [ ] **Walking away from a distress fight.** The event resets (zombies removed). It spawns again when you return.
- [ ] **Recall.** Pick a runner in the Community window. The Follow button reads **Recall**: they turn for home. A
      runner met on the road has **Head home now.** in their dialog, and no Follow or Run entries.
- [ ] **A run saved before this version** (hidden at home) still resolves at the rollover as before.
- [ ] **No POI in range** (or `PhysicalRuns` false in Config/blocks.xml): the old hidden run is used.
- [ ] **Walking speed.** (Superseded by the sc_stage10 running check above.)

## 2026-09-29 build (sc_stage8)

- [ ] **Missing members can be called back.** A resident whose body is gone from the world (Gina in TestSurvivor3)
      shows "Missing - Call back" about 45 s after you reach home, with a tooltip. Pick her and press **Call back**:
      she respawns at her post with her look, gear, skills and traits. The console equivalent is
      `sc restore <name>`. Anything she was carrying is not restored.
- [ ] **No more "LootContainer '…' unknown" errors.** Opening and closing linked storage, outpost storage, or home
      storage from an outpost writes no ERR lines. The window title is unchanged, and it still opens as the grid.

## Fixes to features you already have (in the same build)

- [ ] **Radio contacts now spawn for every NPC class.** About half of the classes (any whose id hash is negative,
      e.g. the Nurse) used to be dropped with "radio contact class … no longer exists" in the log, and the same
      survivors got no distress stand-in. Call a few contacts; each should appear when you reach its POI.
- [ ] **Distress fights get their full pack of zombies** (half of the zombie classes used to be skipped).
      Walking away until they unload no longer counts as a rescue: come back and finish it, or it resolves at the
      rollover.
- [ ] **Stand-ins are protected.** A survivor's stand-in (distress or outpost) can't be hired, ordered or picked up,
      and your own shots don't hurt it unless community friendly fire is on.

## v1: establishing and daily production

- [ ] Standing in a POI, `sc poi` names its type. `sc poi census` gives sensible counts for the world.
- [ ] Place a land claim inside a **cleared** POI at least 150 m from home. The radial shows **Establish outpost**, and it does not appear on the home claim or on claims outside POIs.
- [ ] Establish with the home radio powered and 2 materials + 1 fuel in stock. You get a tooltip with the daily yield, a map icon, a ledger line, and the fee is taken.
- [ ] Each gate refuses with its own message:
  - the POI is not cleared;
  - it is too close to home;
  - it is already an outpost;
  - there is no free slot (2 outposts with fewer than 4 members);
  - the radio is off;
  - the fee cannot be paid.
- [ ] Run `sc day`: the stockpile rises by the yield, and the ledger/tooltip reads "outposts +n …, upkeep …".
- [ ] Unpower the home radio, then run `sc day` twice. The bundles wait at the outpost ("n waiting"). Power it again and run `sc day`: they arrive.
- [ ] With an empty materials stockpile the outposts go **run-down** (half yield), and they recover after `sc give mats`.
- [ ] Pick up the outpost's claim. Within ~10 s it is marked **Lost**, the marker goes, and morale shows a Territory penalty that fades.
- [ ] Place more claims than LandClaimCount allows. The oldest plain claim deactivates, never the home or an outpost. When only home and outpost claims are left, the new one is refused with a tooltip.
- [ ] Stay away more than 7 days: nothing respawns in the outpost POI and yields continue.
- [ ] Accept a trader quest for a POI, establish there, then walk in. No quiet sleeper reset.
- [ ] The outpost claim's radial **Linked storage** shows "Radioing home…", then opens the home storage view at the outpost. It stays open, and Refresh keeps working.
- [ ] The outpost claim's box shows the Pack / Deposit / Withdraw strip, and deposits land in the stockpile.
- [ ] **Community window, Outposts tab:** rows, the detail panel, the network summary, "+n/day" under each resource, Track on map (compass waypoint), and Disband (two presses).
- [ ] The morale panel shows **Territory**, and the radio window lists what each outpost is sending.

## v1.1: people, levels, perks, scouting

- [ ] Use **Assign** on the Outposts tab, then click a resident at home. They are posted: hidden at home, with status "Holding X", and the yield goes up.
- [ ] A second post opens at level 2.
- [ ] Walk within 60 m of a staffed outpost: a stand-in with the member's name and look guards the claim, and it disappears when you leave.
- [ ] If a stand-in dies there, the member dies (ledger and tooltip).
- [ ] A posted member's mood: −3, a Loner +5, and +3 after you visit.
- [ ] Recall with the × on a staff row, or "Recall home" on the member. The member is back at home, visible.
- [ ] **Upgrade** is refused until 3 days have passed (L2) and until someone is posted (L3). When allowed it takes the cost, and the yield and the "Level" line change.
- [ ] Perks listed with no outpost selected:
  - [ ] **Canteen** (food): hunger morale capped at −10.
  - [ ] **Field clinic** (meds): one injury healed free a day.
  - [ ] **Armed escort** (ammo): runs are safer.
  - [ ] **Salvage yard** (materials): fee and upgrade costs ×0.75.
  - [ ] **Motor pool** (fuel): runs bring more.
- [ ] Radio **Scout** buttons mark up to 3 sites for that resource on the map, once a day. The marks fade after 3 days.
- [ ] Walk-ins: a staffed outpost sometimes gets a radio contact at it (8 % a day).

## v1.2 / v1.3: extras

- [ ] **Mode** button: normal → quiet (×0.5) → push (×1.5). The yield changes in `sc outpost yield`.
- [ ] **Radio relay:** place and power a radio inside an outpost POI. `sc outpost list` shows "[relay]", and the outpost still delivers with the home radio off.
- [ ] **Workshop:** post a crafter with 12+ materials in stock and get "made 1 ammo" at the rollover. A chemist makes medical from materials and fuel.
- [ ] **Radio barter:** Give/Get buttons, then Trade, 3 for 1, once a day.
- [ ] **Distress calls** sometimes pin a runner's rescue at one of your outposts (30 %).
- [ ] **Built inside an outpost:** beds, workbench/campfire, and crops for food outposts raise its yield. The detail shows "(+n% built)", and `sc outpost list` shows the counts.
- [ ] **Outpost storage** on an outpost claim: one view over that POI's player boxes.
- [ ] **Outpost Flag** (craftable: 5 small rocks, 5 cloth, 5 scrap iron) placed in a cleared, eligible POI establishes itself. Anywhere else it is a plain claim, with a tooltip saying why.
- [ ] Off by default, test only if switched on in `Config/blocks.xml`:
  - [ ] `OutpostStashAndHaul`
  - [ ] `OutpostCheckInDays`
  - [ ] `OutpostDepletion`

## v2: risk (set `OutpostAttacks` to `true` in Config/blocks.xml, ConfigSurvivorCommunity)

- [ ] `sc outpost security <id>` gives a sensible breakdown. Defenses built there (spikes, wire, traps, turrets) raise it.
- [ ] Attack call:
  - [ ] `sc outpost attack <id>` gives a tooltip and a red skull on the map and compass. The detail reads "UNDER ATTACK – n h".
  - [ ] Go there: zombies spawn around the POI and go for the stand-in or you. Kill them all and it holds, with a security bonus and morale.
  - [ ] Ignore it until the deadline: it holds, is damaged (no yield for 2 days; **Repair** for 2 materials), or is overrun (sleepers back, staff home injured). Nobody dies.
- [ ] `sc outpost attack <id> raid` sends raiders instead of zombies.
- [ ] Raider demand: `sc outpost demand <id>` puts a row in the radio window. **Pay** takes 2 ammo + 2 food. **Refuse** starts a raider attack, and so does an unanswered demand at the next rollover.
- [ ] Blood moon: at its end, every outpost you were not standing at rolls on security (−15).
- [ ] Neglect: an unstaffed outpost unvisited for 10 days gets one sleeper area re-armed and becomes Contested.
- [ ] Good news (on by default): a cache of 2–4 bundles appears in the claim box when you next visit.
