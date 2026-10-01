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
