# SurvivorCommunity — console command reference

Everything lives under one command, `sc` (alias `survivorcommunity`), typed into the **F1** console.
`sc` on its own is the same as `sc status`. Every answer is also written to `Player.log` as `[SC console] …`,
so you can run a few commands, close the console and read the results afterwards.

Most commands take `<id|name>` — a member's entity id **or** their name (`sc arms Amy`). `sc members` lists both.

> **Where the log is:** `%USERPROFILE%\AppData\LocalLow\The Fun Pimps\7 Days To Die\Player.log`

---

## Start here — the ones worth memorizing

| Command | What it tells you |
|---|---|
| `sc status` | The community at a glance: stockpile, morale, home, counts |
| `sc members` | Every member, their id, status, post, skills |
| `sc dump` | Everything, for when you want to read it in the log afterwards |
| `sc xnpc [id\|name]` | The 0-XNPCCore side of one body, including whether 0-XNPCCore has marked her target "melee unreachable" (round n08). Bare, it prints the global patch/counter diagnostic and the session's unreachable-mark totals |
| `sc scan` | Re-scan the home zone now (beds, facilities, crops, defenses) |
| `sc day` | Simulate one day rollover, outposts included |
| `sc save` | Force a registry save |

## Making things happen fast (testing shortcuts)

| Command | Use |
|---|---|
| `sc spawn [hostile] [male\|female] [T1\|T2\|T3] [weapon] [weapon] [meleeonly] [ranged-lead\|melee-lead] [Set]` | Spawn to order. **Two** weapon tokens (one ranged, one melee) plus a trailing armor-set name; `sc weapons` lists the tokens |
| `sc give <food\|meds\|ammo\|mats\|fuel\|water\|luxury> <n>` | Fill the stockpile |
| `sc morale [n]` | Session-only morale override; bare prints the breakdown, `-1` clears it |
| `sc xp <id\|name> <cardio\|wits\|fighting\|shooting\|spec> <n>` | Grant skill xp |
| `sc injure <id\|name>` | **Toggles** the injured flag - run it on an injured member and it heals them |
| `sc recruit <id\|name>` | Recruit on the spot |
| `sc day` | Skip a rollover instead of waiting |
| `settime` | Vanilla. **Careful:** bare `settime day` resets the world to day 1 |

## People

- `sc relink` - re-link the registry to the loaded world
- `sc members` · `sc trust <id|name> [forgive]` · `sc recruit <id|name>` · `sc restore <member>` · `sc repost <member>`
- `sc arms <id|name> [melee|ranged]` — what she is holding and why (overwhelmed count, gun first on / standing aside
  for a melee order, 0-XNPCCore's close-melee state); force a hand
- `sc loadout <id|name> [weapon] [weapon] [meleeonly] [ranged-lead|melee-lead]` — her weapon pair, the quality and
  attachments of what is in her hand, the attachments rolled for each of her two weapons, and (a member) the items she
  was handed exactly through the inventory panel or the weapon menu, each marked STALE when it is no longer worn/carried
- `sc loadout near [r]` · `sc loadout sim [n] [hostile] [T1|T2|T3] [any]` — roll statistics without spawning
- `sc skills sim [T1|T2|T3] [n]` · `sc aim [id|name]` · `sc anim <id|name>`
- `sc names [reload | reset | test [n] [male|female]]` — the streamer name pool

## Looks

- `sc look <id|name> [tone]` · `sc paint <id|name> [body <id> | face <id> | tattoo <shape>[/ink] | none | roll]`
- `sc gear <id|name> <Set|Outfit|none>` · `sc gear sim [n] [hostile] [T1|T2|T3] [any] [male|female]`
- `sc portrait <id|name|all>` · `sc portrait frame [fov dist yaw eyelift aimlift]`

## Movable windows: the talk card and the member inventory panel *(rounds n10, n11)*

- **`sc ui`** — where SC's two movable windows sit: the talk card (the survivor card while you talk) and the member
  inventory panel ("Show me your inventory" on a member). Shows their saved places ("no window moved" when both are in
  their default places), the file the places are kept in (`%APPDATA%\7DaysToDie\SurvivorCommunity\ui_layout.txt`),
  where the talk card is, and, while the panel is open, whose inventory it shows and how full her pack and bag are.
  Read-only.
- **`sc ui reset`** — forgets both saved places: each window opens in its default place again (and jumps there if it is
  open). Right-clicking a window's title bar (the talk card's name bar) does the same for that window.

## Sleeping *(round n06)*

- **`sc sleep`** — every member: whether SC holds the night lease on her, the **exact reason** when it does not,
  and her `SdcsResident` / `.SdcsRoutineWalk` / `SdcsRoutinePhase` cvars (phase 3 = asleep, 4 = waking). It also
  shows how many of the zone's beds are inside the box 0-XNPCCore searches (within `SleepZoneHeight` of the home
  block's height) — only those count. This is the first thing to run if someone will not go to bed. It is read-only.

## Team strength *(round tm1)*

- **`sc team`** — every loaded member's team verdict: F (her side) against T (the awake enemies within 15 m, or the
  whole defended area during a horde at the base), matched or OUTMATCHED, and whether she is MELEE-LED or why not (on a
  run, on watch, Cowardly, your gun order, no melee weapon, hurt, raiders in the fight, outmatched). Read-only.
- **`sc team <id|name>`** — one member in full: the verdict and its age (STALE after 3 s), why she is or is not
  melee-led, a fresh count with every friend and enemy counted, its distance and weight and how the weight was reached
  (stars, health, weapon; tag weight, health weight; ARMED, melee-unsafe), her target and whether it needs the gun, and
  her hand: what she holds, your weapon order, the melee-first clock, the 5 s swap hold, the overwhelmed gun hold, a
  target kept out of reach, and the overwhelmed count against its need (one more while melee-led). Read-only.
- **`sc team <id|name> auto`** — clears your weapon orders on her (the weapon menu's, or "use your ..." by voice), so
  she picks her weapon by the rules again. The test shortcut after a gun or melee order.
- **`sc team <id|name> hurt [share]`** *(a test shortcut, like `sc run hurt`)* — her body's health down to that share
  of her max (default 0.45, just under `TeamLowHealth`): she stops being melee-led at the next count, and is again from
  60%. `hurt 1` puts her back to full health.
- `sc xnpc` counts the rule's work as `x5-team-*`: `recount`, `melee-first` (gun put away for the melee weapon),
  `bout-kept` (stayed on melee after 0-XNPCCore's close-range swap), `gun-held` (gun first held back for a target beyond
  5 m), `gun-needed` (gun first for a target that needs it), `gun-needed-close` (the same inside 5 m: one out of her
  reach), `swap-held` (a swap waited for the 5 s hold), `equip-refused` (a swap's equip did not take: that rule waits
  10 s for her), `unreachable`, `outmatched`, `matched-again`, `low-health`; `sc xnpc <name>` has a one-line
  `team strength:` summary. Neither command recounts the attackers (the overwhelmed line is the last count, with its age).

## Medical restock *(round md1)*

- **`sc meds`** — the setting and its keys (`MedsResupply...`), the mix as this world resolved it, the stockpile's Meds
  with the reserve kept for tonight's injured and what is left to spare, today's restocks, the `meds-*` counters, and one
  line per loaded member: her heal items (first aid kits plus first aid bandages, window and bag), cloth bandages, her
  last restock and the next one allowed at home, and DUE or why not. Read-only.
- **`sc meds <id|name>`** — one member's every check: what she carries, the thresholds, the cooldown with the world
  times, whether her window or a trade is open, the top-up she would get, the stockpile and its reserve, and the verdict
  at home and before a run. Read-only.
- **`sc meds <id|name> give`** *(a test shortcut)* — restocks her now for 1 Meds, ignoring the cooldown and the
  thresholds (and `MedsResupply`); the stockpile, its reserve, her open window and her pack still apply. It starts her
  cooldown like any restock.
- The counters (`sc meds`, also in `sc xnpc`): `meds-restock` (at home), `meds-restock-run`, `meds-restock-console`,
  `meds-refused-empty`, `meds-refused-reserve`, `meds-refused-full`, `meds-refused-nothing` (she already had the whole
  mix), `meds-refused-mix` (no usable item in `MedsResupplyMix`), `meds-held-window`, `meds-held-use` (sent on a run in the
  middle of a bandage), `meds-cooldown`, `meds-place-failed` (nothing went into a pack that had room). At home each
  outcome is counted once per episode, when it changes, not at every 5 s check. `sc xnpc <name>`'s `medic:` line shows
  her last and next restock (`restock (md1): ...`).

## Work, runs and mining

- `sc run <id|name> <res|mine|clay>` · `sc run <id|name> <recall|home|skip|offline>` · `sc runners`
- `sc run <id|name> hurt [share]` *(round r2, a test shortcut)* — a runner's health down to that share of her max
  (default 0.3); with nothing to heal with she gives the job up and heads home about 2 s later. `sc runners` shows her
  health, whether she has a way to heal, and a walk held or routed round a threat she would run from
- `sc distress <id|name>` · `sc craft [table | station [radius] | <id|name> [spec <Specialty> | end]]`
- `sc facility [list]` · `sc facility level <x y z> <1-5>` · `sc drinks [on|off]`

## Home, defense and the watch

- `sc home [set]` · `sc scan` · `sc noise [add <n> | clear]` · `sc attention <0-100>`
- `sc attack [now] [size]` · `sc attack status|end` · `sc attack settle [held|damaged|overrun]`
- `sc defence [horde]` · `sc siege [held|damaged|overrun]`
- `sc watch [list]` · `sc watch assign <post> <member>` · `sc watch relieve|end <post|member>`
- `sc watch rest <member> [clear]` · `sc watch place <post|member>` · `sc watch scan`
- `sc tower [info]` · `sc tower check|stamp [rot]`

## Outposts

`sc outpost list | add [row] | remove <id> | yield | pending <id> <n> | reclassify <id> | state <id> <state> |
post <id> <member> | recall <member> | upgrade <id> | level <id> <1-3> | mode <id> <normal|quiet|push> |
security <id> | attack <id> [raid] | demand <id> | pay <id> | refuse <id> | repair <id>`

## Enclaves

`sc enclave list | info <id> | people <id> | spawn <id> | despawn <id> | standing <id> <points> | hostile <id> |
kill <id> [<person id>|all] | trade <id> | release <id>`

Requests, sieges and diplomacy:
`sc enclave requests | request <id> [offer … | accept | complete [points] | fail | lapse | clear | fate <alive|dead>] |
siege <id> [now [force] | win | lose | clear] | refill [now] | gift <id> [give <res> [size] [force]] |
barter <id> [<give> [get]] | call <id> [fighter|drop [force] [now] | cancel] | raid <id> [<outpost id> [demand|now]] |
move <id> [sim|now] | join <id> [sim|now [force]] | arrivals [now] | reveal|hide <id|all> | sim [late] |
names [n] [hostile] | reseed confirm`

## Bands and infestations

- `sc band list | info <id> | spawn [drifters|raiders] [trouble] | here <id> | advance <id> [hours] | kill <id> [<person id>|all] | clear <id|all> | trust <id> | roll`
- `sc infest list | info <id> | add [<x> <z>] | clear <id|all> | spread <id> | roll | reveal|hide <id|all>`

## Radio and scouting

- `sc radio` · `sc radio give <member>` · `sc radio talk <member> <moment>` · `sc radio attacked <member> [force]`
- `sc radio horde <gather|spotted|clear|call|arrived|held|damaged|overrun> [side] [member] [force]`
- `sc scout <res>` · `sc barter <give> <get>`

## Voice

`sc voice play <line> [id|name] | stop | rescan | gate | sayline [line] [seconds] | context [id|name] |
who [id|name] | greet [id|name] | chatter [id|name] | taunt <event> [id|name] | barks | line <words>`

## POIs and sleepers

- `sc poi [near <r> | classify <prefab> | census]`
- `sc sleepers [status | scan | repair]`

## Raiders

- `sc raiders [r]` · `sc weapons` · `sc spawn hostile …` (see above)

---

## Not SC, but useful alongside it

| Command | Use |
|---|---|
| `dm` (alias `debugmenu`) | One toggle, two names. Debug mode - this is what gives you the UAI overlay below |
| the UAI overlay | With `dm` on, shows each NPC's **Active Action / Task / Target** above their head (the overlay in your screenshots) - how to see which utility-AI action is winning |
| `settime <day> <hour> <minute>` | Jump to dusk/night to test sleeping. Also takes `day`, `night` or a raw tick count - but **not** `hh:mm` |
| `spawnentity` | Vanilla spawner |
| `creativemenu` | Items |

### Reading the AI overlay

`Active Action` is the utility-AI action that won this scoring pass. For SC's people the ones worth recognizing:

- `ScAtPost` / `ScRoam` / `ScNightIdle` — SC's own idle behavior at home
- `ScDefendBase` / `ScDefendHold` — SC's base defense (outranks almost everything)
- `ScReturn` / `ScTravel` / `ScFlee` — walking home, traveling, fleeing
- `RoutineWalk` — **0-XNPCCore's resident routine**, which is what sleeping runs on
- `HarvestSdcsHere` / `LootSdcs*` / `BaseKeepSdcs*` — 0-XNPCCore's work actions
- `MeleeAttack` / `RangedMeleeAttack` / `ClearShot` / `BackupRanged` / `BackupKeepDistance` — 0-XNPCCore's combat
- `ScFleeHurt` / `ScEncFleeHurt` — badly hurt with nothing to heal with, running

If someone is doing the wrong thing, the overlay's action name plus `sc xnpc <name>` is usually enough to say which
mod owns the behavior.
