# Survivor Community (SurvivorCommunity) - 7 Days To Die 3.2

A State-of-Decay-style community on top of the NPC mods: survivors you recruit **live in your land claim**,
have **skills and traits**, share a **morale** that reflects how well the base is supplied and housed, eat
from a **stockpile of resource bundles**, and can be sent out or taken along.

**Requires** `0-SCore` and `0-XNPCCore` (the mod stays inert, with an error in the log, when either is
missing). Works with any NPC character pack built on the XNPCCore templates. Optional: `LandClaimUpgrade`
(the community then lives in the upgraded claim zone instead of the plain 41x41 square).
Harmony mod: EAC must be off. **Single player / host only in this version** - a remote player sees the
NPCs but cannot manage a community (v2).

## Milestone 1 - what works now

- **Recruiting.** Talk to any hireable NPC. Next to *"I am interested in hiring you"* there is now
  *"I could use someone like you in my community."* The survivor asks a **favour** first, rolled once per
  NPC: either **secure the nearest POI** (a journal quest with sleeper markers - no rally point, so nothing
  built there is ever reset; once the building is clear the quest's second step is **Return to <name>**
  and the marker moves to the survivor, who waits where you left them) or **bring supplies** (e.g. 15 food
  items, 5 medical items or 60 rounds). When the favour is done, the join option appears and they join
  **for free** - no dukes.
- **Home.** The community lives in your land claim. The first land claim you own becomes home
  automatically; the land claim block's radial menu gets **Set as community home** and **Community**.
- **Walking.** The survivors (and the raiders) are 0-XNPCCore's player-body NPCs, and 0-XNPCCore's own controller
  drives their walking and running and the animator's locomotion. The fixes this mod once needed for its own
  player-body classes (a move helper that would not start a non-root-motion body, a path direction overwritten by
  the body's facing, an animator fed a tenth of the real speed) went with those classes; see "Survivors with the
  player's body" for the few body fixes that remain.
- **Friendly fire.** The NPC framework shields every friendly NPC from the player twice over - as allies while
  they follow you, and by faction while they do not - so shots at any of them passed through without a mark.
  The mod turns that into the division a community needs: an NPC who is not one of your people can be killed,
  and your own members cannot, unless you set `CommunityFriendlyFire` true in the config block. Traders stay
  immune either way, NPCs still cannot shoot each other, and zombies were never shielded.
- **Staying home.** Residents hold a Guard order, and the NPC framework's combat actions will chase a target
  as far as it runs, so the resident package adds a leash: more than 25 m from their post they walk back
  instead of chasing (they still defend themselves at arm's length). Followers standing next to you hold
  position rather than letting the framework's wander behaviour walk them off.
- **Residents.** A recruit who joins inside the base is stationed at a post near the land claim block;
  one who joins elsewhere follows you until you walk into the base. Residents guard their post, roam
  around it by day, stand quiet at night, fight whatever comes close and walk back. They are not in your
  party: no teleporting after you, no hired-NPC HUD entry. They survive save/reload and chunk unloads.
  A resident that gets wedged in furniture or a doorway (no progress for `StuckSeconds`, or far from the
  post for long) is moved back to their post; the roam task also gives up on a blocked target.
- **Followers.** From the roster (or the SCore companion window once they follow) a member can follow
  you like a normal hire - at most `MaxFollowers` (2) at a time - and be sent home again.
- **Skills and traits (data).** Each survivor rolls 2 traits, starting stars in Cardio / Wits / Fighting /
  Shooting and a specialty (Medicine, Cooking, Mechanics, Gardening, Chemistry, Craftsmanship). Kills
  already earn Fighting / Shooting XP. Gameplay effects come with milestone 3.
- **Persistence.** `<save>/survivor_community.txt` is the source of truth (atomic write, `.bak`).
- **Console:** `sc status | members | day | morale <n> | give <res> <n> | relink | home | dump | save`.

## Milestone 2 - packing station, stockpile, daily needs

- **Packing Station** (`scPackingStation`, workbench recipe: 30 wood, 5 forged iron, 2 duct tape). It is
  a lockable crate with an 8x4 box. Open it and a strip appears under the grid: **Pack food / medical /
  ammo / materials / fuel** turns what is in the box into **bundles** (points = the items' economic value,
  `PointsPerBundle<Resource>` per bundle; the most valuable stacks go first and anything short of a whole
  bundle stays in the box), **Deposit bundles** moves every bundle in the box into the community
  **stockpile**, and **Take ...** puts one bundle back in the box. The strip only shows on a station.
- **Bundles** (`scBundleFood/Meds/Ammo/Materials/Fuel`) are plain items: they cannot be opened, sold or
  scrapped back into goods. Food = anything tagged `food`/`drinks`, medical = `medical`, ammo = `ammo`,
  fuel = gasoline (the `gasoline` tag or the `FuelItems` list), materials = the `Resources` group or
  `resource*` items (wood and coal are materials even though they burn).
- **Bundles in the world** (`Config/loot.xml`): a small chance in the containers they belong in - food
  piles and cupboards; medical piles, medicine cabinets, medical coolers and Pop-N-Pills crates; ammo
  piles, military lockers, Shotgun Messiah crates and ammo displays; Pass-N-Gas crates (fuel);
  construction crates and toolboxes (materials) - every kind in air drops, and as trader quest rewards
  (food / medical / ammo rewards from tier 1, more per bundle at higher tiers; resource rewards offer a
  materials or fuel bundle).
- **Daily needs.** At each day rollover the community eats `FoodPerMemberDay` (0.5) food bundles per
  member (trait multipliers apply, fractions carry over) and uses one medical bundle per injured member
  (which heals them). This happens whether or not the base is loaded. **Ammo has no daily upkeep:** it is
  spent only when a survivor's gun is refilled in the base or at an outpost (see *Ammo* under the survivors
  below), and the day's report counts the ammo bundles that took. A tooltip and the ledger (`sc status`) report
  what was used, whether anyone went hungry, and whether a survivor ran low while the stockpile had no ammo; the
  hungry flag feeds morale in milestone 4.

## Milestone 3 - skills and traits that do something

- **Core skills** (1-7 stars each). Fighting adds melee damage and Shooting adds ranged damage
  (`MeleePerStar` / `RangedPerStar`, 10% per star above the first); Cardio adds run speed and max health
  (`SpeedPerStar` 4%, `HealthPerStar` 8%). The bonuses ride on the NPC as `buffSCSkills`, which reads the
  mod's cvars, so they survive reloads and show in `sc members`. XP: kills (Fighting or Shooting by the
  weapon held), time spent following you (Cardio, one point per `FollowXpSeconds`), every day lived
  (Wits, `DailyWitsXp`).
- **Specialties** work at the day rollover, for stationed members only. The best **cook** cuts food use
  (`CookFoodPerStar`, floor 50%). **Gardeners** turn living crops inside the base into food bundles
  (`CropFoodPerStar` x stars per crop per day). A **medic** stretches medical bundles (one bundle heals
  1 + floor(stars x `MedicPerStar`) injuries). **Craftsmen** lower the packing threshold at the station
  (`PackBonusPerStar`, floor 60%). Mechanics and Chemistry pay off on supply runs (milestone 5). Doing the
  job earns specialty XP.
- **Traits** now show on the NPC as buffs with a description, food-use multipliers apply, Quick Study
  speeds XP, and **Cowardly** survivors flee instead of fighting (XNPCCore's own Flee action).
- **Hungry / Injured** are real: a community out of food makes every member 10% slower and weaker,
  an untreated injury 15-20%, until the next rollover with supplies.
- **Base scan**: beds, facilities and crops inside the whole claim zone feed the specialties and morale.
  They are counted from each loaded chunk's list of model blocks, so a count is cheap: every 15 s, every
  second while the community window is open, at each new day and with `sc scan`. A chunk that is not
  loaded keeps its last count (saved in the registry), so leaving the base does not lose beds.
  **Beds** are every sleeping-bag class block (bedrolls, king beds, bunk beds, and bed mods that use
  that class) with `BedsBySleepingBag`, plus the `BedBlocks` name prefixes (bare-frame beds, mattresses,
  hospital beds), minus `BedBlocksExclude` (a bunk frame with no mattress, a rolled-up mattress).
  Facilities match `FacilityBlocks` by name; crops are model-shaped `planted*` blocks (the flat flower
  crops are not food and do not count). `ScanRadius` above 0 limits the count to that many blocks
  around the land claim block.
- **Console:** `sc xp <id|name> <cardio|wits|fighting|shooting|spec> <n>`, `sc injure <id|name>`, `sc scan`.

## Milestone 4 - morale and the community window

- **Morale** (0-100) is recomputed from the registry: 50, plus days of food in the stockpile (or -20 when
  hungry), medicine (+5, or -10 with an untreated injury and no bundles), beds against members (+10, or
  -5 per missing bed), facilities (+3 each, campfire / workbench / forge / chemistry station / cement
  mixer, up to +15), +2 each for ammo, materials and fuel in stock, trait offsets, and recent deaths
  (`DeathPenalty` 15 each, fading `DeathDecayPerDay` 3 per day). Each survivor's **mood** is morale plus
  their trait offset, minus 15 while injured and 10 while the community is hungry.
- **Consequences.** Above `HighMoraleThreshold` (75) everyone earns XP half again as fast. Below
  `LowMoraleThreshold` (25) for `LowMoraleDays` (2) days in a row the unhappiest survivor threatens to
  leave (tooltip, ledger, window flag) and leaves at the next rollover unless morale is back above the
  line. A survivor whose mood is under 25 refuses to follow you.
- **HUD.** A morale icon with the number sits in your buff bar while the community has members.
- **The community window** opens from the ESC menu (**Community**, under SCore's entries), the land
  claim block's radial **Community**, and a member's dialog (*Let's talk about the community*). Left:
  one row per survivor (status, mood, skills, specialty; injured rows are red) with paging; morale
  breakdown, recent days and the stockpile under it. Right: the selected survivor's skills, specialty,
  traits and flags, and the orders **Follow me** / **Go home** / **Ask to leave** (press twice).
- **Member dialog orders.** Talking to a member also offers *Come with me*, *Go back home* and *It is
  time for you to leave the community* (with a confirmation line).
- **Console:** `sc morale` prints the breakdown; `sc morale <n>` is a session-only override (-1 clears).

## Milestone 5 - supply runs

- **Send a survivor out** from the community window (five buttons under the orders, one per resource)
  or their dialog (*I need you to go find supplies* > what to look for). Only a stationed, uninjured
  survivor with mood 25 or better goes, at most `MaxRunners` (3) at once. They vanish from the base
  (hidden and parked in place, not teleported) and are back at the next day rollover after `RunDays` (1).
- **What they bring back**: `RunYieldBase` (2) bundles, +15% per Wits star above the first, +15% per
  specialty star for the matching job (Mechanics for materials and fuel, Chemistry for medicine, Cooking
  for food), trait multipliers (Tinkerer, Hoarder), and x1.25 when morale is above 75. Straight into the
  stockpile before that day's needs are taken.
- **Risk**: `RunRiskBase` (15%) minus 2% per Fighting and Shooting star above the first. A bad roll means
  they come back injured with half the haul, or - a quarter of bad rolls - never come back: a death like
  any other (morale penalty, deaths list); their hidden entity is removed when its chunk next loads.
- Runs earn Wits XP (`RunWitsXp`) and specialty XP for the matching job. A runner keeps eating from the
  stockpile but is not counted as a guard for ammo, a cook, a medic or a gardener while away.
- **Console:** `sc run <id|name> <food|meds|ammo|mats|fuel>`; `sc day` resolves runs at the rollover.

### Physical runs (`PhysicalRuns`, on by default)

The runner no longer vanishes. They **walk out of the base** to a real POI that has what they were sent for, and
walk back.
- **Destination.** The POI is `RunMinDist`-`RunMaxDist` (150-700) m from home. The first choice is a site the
  radio scouted for that resource. Next, a POI whose type produces it, primary producers and nearer sites first.
  Last resort: any scavenging site. Never an outpost, and never a POI where another runner, radio contact or
  favour already is.
- **The trip.** They run to the site, spend `RunSiteHours` (6) game hours walking round its ground floor, then
  run home and walk the last stretch to their post. Out of range they cover the distance at `RunWalkMps` (4 m/s,
  a little under their real running pace to allow for detours).
- **In the world while you are near.** Within your view distance they are a real NPC you can follow, escort, talk
  to (*Head home now.*) or watch fight. Further out, the mod captures the NPC into a snapshot kept in the
  registry: the same data the game uses to park vehicles and drones (name, health, buffs, bag, weapon, pack). It
  removes the NPC and walks them on along the route on the game clock. When you come near where they are by then,
  the same NPC (same entity id) is rebuilt there, out of your line of sight.
- **Only one copy, ever.** A runner is never saved in a chunk; the registry is their only copy. A chunk unload
  (teleport, bed respawn) captures them first. Quitting takes every runner out of the world before the save. Any
  old copy a chunk still holds is removed on load.
- **Risk.** The run's dice are rolled only while the runner is out of your sight. Escort them and real zombies
  decide instead.
- **The haul** counts the moment they reach the edge of the base, then they walk in to their post.
- **Distress calls** are rolled when they reach the site: they themself are pinned there (no stand-in), and the
  zombies go for them. Help them and they come home with the rescue bonus. Leave them for `RunDistressHours`
  and it ends the way bad runs do. Walking away mid-fight resets the fight.
- **Recall** (the Follow button in the window, or *Head home now.* in their dialog) turns them round. A search cut
  short brings a share of the find.
- **Fallback.** With no POI in range, `PhysicalRuns=false`, or on a dedicated server, the old hidden run above is
  used. Runs saved before this version finish the old way too.
- **Console:** `sc runners` lists each runner: leg, in world or not, position, distance, snapshot size, measured
  speed. `sc run <name> skip|offline|recall|home` drives a run for testing.

## Doors

- Members open closed doors on their way (roaming, following, going home, supply runs) and shut them again
  behind them once they are through and nobody is in or right beside the doorway. Double doors and gates work from
  either leaf.
- **Locked doors.** A member opens a locked door only if it is your lock (you own it or are on its list), and
  `MembersOpenOwnerLockedDoors` is true (the default). A locked door in a POI, or anyone else's, stays shut. Set it to
  false and your locked doors keep your own people out too.
- **Drawbridges and hatches** are opened too, and shut (the bridge raised) behind them, so a base whose only way
  out is a hatch or a bridge doesn't trap its people. Set `MembersOpenHatches` to false to keep members off them.
- **Raiders** open unlocked doors (SCore 3.2.36 does that for every NPC); locked doors stop them.
- Why this was needed: SCore's own door opener only runs from some of its tasks (and did nothing on 3.x doors
  before SCore 3.2.36), and the game's opener (which bandits use) is off for SCore NPCs, while the pathfinder still
  routes them through closed doors. Members used to walk into a shut door and stand there until they were teleported
  home.

## Linked storage

- **One container for every box in the base.** The land claim block's radial gets **Linked storage**
  (also a button in the community window). It opens in the normal loot window - left panel, backpack
  beside it, drag and drop, shift-click both ways, the transfer buttons - showing every stack in every
  player-placed storage box inside the zone, sorted by name, as one grid. With Grid Inventory the grid
  scrolls: every stack, up to about 240 at once (Grid Inventory's loot window has 260 item widgets and
  slot N can only be drawn by widget N), then twelve empty rows to drop into. The boxes' whole free room
  is in the strip's header, however many boxes there are; Refresh lays the view out again once the empty
  rows are used. On vanilla's loot grid it shows 10 rows. The strip under the grid shows the box count,
  stacks and free room, how many stacks did not fit, and the Refresh button. `LinkedStorageRows` in the
  config can lower the row limit.
- **It is a real view, not a copy.** Taking, half-taking, swapping or merging in a cell happens in the
  real box slot behind it, so the box on the wall changes with it. Anything dropped on an empty cell, or
  shift-clicked from the backpack, goes into the **first box with room for it**. If no box has room the
  item comes back to you. With Grid Inventory and its `zzzz_GridInv_SurvivorCommunity` patch, room means
  cells: a box only takes an item its own grid could seat, the linked view is sized in cells, and the
  strip counts free cells - a box with a free slot but no run of cells for a rifle does not
  count.
- Boxes in chunks that are not loaded are out of reach (the strip says how many chunks were skipped).
  The packing station is not part of it. Host only.

## Radio station

- **The block.** `scRadioStation` (workbench: forged iron, electric and mechanical parts, polymers) is a
  powered consumer - wire it like a light. **Use radio** opens its window while it has power.
- **Air drops on request.** With this mod installed vanilla's scheduled air drops never come; the radio
  requests one, and the sandbox **air drop frequency** is its cooldown - the same day range vanilla would
  have scheduled (every 3 days, 1-7 days...). Turn that setting off and the radio cannot call a plane.
  `RadioOnlyAirdrops=false` restores vanilla's own drops.
- **Call for survivors.** One call every `RadioCallDays`: one of our player-body survivors (never a pack NPC,
  see *No new pack NPCs* below) answers from a real POI
  `RadioContactMinDist`-`RadioContactMaxDist` metres from home, marked on the map. They exist as a record
  (not an entity) until you get near: the first time that chunk loads the NPC spawns there, standing in
  the building with a favour to ask like any recruit. Unvisited contacts fade after `RadioContactDays`.
- **Distress calls.** While a radio is powered, a survivor sent on a supply run may radio for help
  (`RadioDistressChance`): a quest marks the building they are pinned in. Get within `RescueTriggerRange`
  and the survivor appears there with zombies closing in - `DistressZombiesBase` plus your gamestage, but
  never fewer than `DistressZombiesPerCombatStar` per combat star they have, so a veteran calling for
  help means real trouble. Clear them and the run comes home safe with `RescueBonus` on the haul; ignore
  the call and the run ends the way bad runs do. If the survivor falls out there, they are dead.
- The radio is also what outposts report in to (see **Outposts**).

All five milestones, linked storage and the radio are in. See the plan's non-goals for what v1 deliberately leaves
out (multiplayer clients, a player-menu tab, portraits, NPC hunger, base building by survivors).

## Outposts

Hold cleared buildings away from home and they supply the community. Everything is registry bookkeeping, so it keeps
working with the outpost and the home both unloaded. The in-game checklist is `TESTING.md`.

**Establishing and daily production**
- **Establish.** Clear a building at least `OutpostMinDistance` (150 m) from home and place an ordinary land claim in
  it (or craft an **Outpost Flag**, which establishes itself when placed). Then pick **Establish outpost** on the
  claim. It needs the home radio powered and the fee (`OutpostFeeMats` + `OutpostFeeFuel` from the stockpile).
- **What it makes** depends on what the building is (`sc poi`, and the `OutpostRows` / `OutpostPrefabs` tables):
  groceries, diners and farms make food; clinics make medical; army sites, gun stores and police make ammo;
  hardware, industry and junkyards make materials; gas stations and refineries make fuel. A building that matches
  no row scavenges whatever you are shortest of. More outposts of the same kind give less each.
- **Every rollover** each outpost adds its yield to the stockpile. The yield is scaled by tier, level, the people
  posted there, what you built inside it (beds, facilities, crops), and its output mode (quiet / normal / push).
  - Upkeep costs materials; hauling costs fuel. Unpaid upkeep halves the yield (run-down); unpaid haulage cuts it
    to three quarters (on foot).
  - Without power at the home radio (or a powered **relay** radio inside the outpost), bundles wait there,
    at most `OutpostPendingDays` of yield, and arrive once it reports in.
- **Slots:** 1 + one per 4 members + one per 5 points of Leadership morale, at most `OutpostMax`. Outposts over the
  limit go idle; nothing is destroyed.

**Protection**
- Sleepers never respawn in the building, trader quests and rally resets pass it by, and the claim limit never
  switches off the home or an outpost.
- If its claim goes, the outpost is lost, with a morale penalty that fades.

**At the outpost** (the claim's menu)
- **Outpost** opens the community window on the Outposts tab.
- **Linked storage** radios home and opens the home storage right there.
- **Outpost storage** links that building's own boxes.
- The claim's box has the Pack / Deposit strip.
- **Disband outpost** needs two presses; the claim stays yours.

**People and growth**
- Post up to two members (a second post at level 2). They are hidden at home like runners, still eat, and add
  yield: more when their specialty fits.
- While you visit, their stand-ins guard the claim. If a stand-in dies there, that survivor died.
- Levels 2 and 3 are bought from the stockpile.
- Each kind of producing outpost gives a perk:
  - **canteen:** hunger hurts less;
  - **field clinic:** one free heal a day;
  - **armed escort:** safer runs;
  - **salvage yard:** cheaper fees;
  - **motor pool:** bigger runs.
- Posted crafters and chemists turn surplus into ammo and medical.

**Radio**
- **Scout** marks the nearest sites for a resource.
- **Barter** trades 3 bundles of one resource for 1 of another, once a day.
- It lists every outpost and answers raiders.
- Staffed outposts sometimes draw survivors in, and runs in distress sometimes hole up at an outpost.

**Risk** (`OutpostAttacks`, off by default)
- Each outpost has a **security** rating from its staff, tier, your visits, ammo, the defenses you built
  (spikes, wire, traps, turrets) and fights won.
- **Attack calls** give you `OutpostAttackHours` to get there and fight.
- Ignored attacks are settled on security:
  - **held;**
  - **damaged:** no yield for 2 days, or repair it for materials;
  - **overrun:** the sleepers are back, and the staff come home hurt. Nobody dies in a fight you did not attend.
- Blood moons roll every outpost you were not standing at.
- Neglected outposts get squatters.
- Past gamestage 30, raiders may demand supplies.
- Good-news caches (on by default) leave bundles in the claim's box.

**Console:** `sc poi [near r | classify prefab | census]`,
`sc outpost [list | yield | add | remove | post | recall | upgrade | mode | security | attack | demand | pay | refuse | repair ...]`,
`sc radio`, `sc scout <res>`, `sc barter <give> <get>`.

**SurvivorOrigins:** the fact `sc_outposts` and the events `sc_ev_outpost` (established) and `sc_ev_outpost_retaken` are
published for a future Leadership release; won outpost fights count as `sc_ev_rescue`.

## Survivors with the player's body

- Player-body NPCs (`Config/entityclasses.xml`) wear the **player character model**: a random face, race,
  hair, eye colour and (for men) facial hair from the game's own character catalogue, plus **real vanilla
  armor** in their equipment slots - the same meshes you wear. It protects them at half the rating it would
  give you, and the framework's flat difficulty resistance tags (`DRLow/Mid/High`) are left off because the
  armor replaces them: stacked, a raider in three medium pieces measured 84.5% and barely noticed a bat. An
  unarmored one now sits at 5%, a full top set under 50% (`WORN_ARMOR_SCALE` in `Tools/gen_arsenal.py`). The
  armor does not break, and wear is not kept across loads.
- **Survivors are 0-XNPCCore's own.** Since the XNPCCore migration the community's survivors are 0-XNPCCore 3.2's
  player-body survivors, `npcSdcsSurvivor{Male,Female}T{1,2,3}` (`XnpcSurvivorClasses`), dressed, named, healed and
  armed by this mod; SurvivorCommunity no longer defines survivor classes of its own. **A save from before the
  migration needs a new game:** its members were `scSurvivor` classes, which are gone, so they can never be found or
  re-created; the log says `this save predates the XNPCCore migration: start a new game` when such a save loads,
  and again when the daily tick finally removes them.
- **Following is 0-XNPCCore's; voice orders are member states.** A following member uses 0-XNPCCore's own follow
  (`DynamicFollow`): its formations, follow distances and engage stances. The community's own follow actions are only
  a fallback for a pack NPC that lacks it. The order wheel and `@name` commands work on followers - **stay**, **guard**,
  **guard and return**, **wander**, **show me a route** / **patrol**, crouch, formations, stances - and the member
  simply holds that order, still following you (the window shows *Following (holding)*, *(guarding)* ...) until you
  call them (`@name follow`) or fetch them; none of it ever counts as leaving, and a spoken *dismiss* sends them home.
  A member holding a spot outside the base is not stationed when you walk home without them, and is never counted
  missing. Stationing clears 0-XNPCCore's formation, move-to and crouch state; the stance and follow distance stay.
- **Hired companions may join.** A companion you hired the vanilla way can be asked to join like anyone (do the
  favour first); joining ends the paid hire - members are never billed. Somebody else's hire cannot be recruited.
- **No 0-XNPCCore kill levels for members.** Members and their stand-ins grow through the community's skills only;
  0-XNPCCore's level bonus (health, damage, spread, regeneration) and NPC progression are kept off them. Strangers
  keep 0-XNPCCore's levels, and a member who leaves gets them back.
- **Runners on 0-XNPCCore bodies** run at `RunnerRunMps` (4.5 m/s) and walk at `RunnerWalkMps` (1.6 m/s) in the
  world, in literal metres per second, with Cardio, hunger and injury applied on top.
- **A member's inventory window** (their ammo and medical stock) drops as a backpack where they die. If you have it
  open then, it closes; if another player has it open, its contents drop at the body in a second backpack once they
  close it (or leave the game), so nothing in it can be taken twice.
- **Raiders (M10): the survivors' body, hostile.** Raiders are six classes on 0-XNPCCore's player body, the same
  body as the survivors: `scRaider{Male,Female}T{1,2,3}`, the tier being the band they turned up in (T1 early, T2
  middle, T3 late). They are hostile by class (the `bandits` faction, which hates you, your survivors and the
  zombies, never another raider), cannot be talked to or recruited (looking at one shows no talk prompt, and E does
  nothing), and use 0-XNPCCore's fighting AI with real
  vanilla weapons, plus a small package of this mod's (`SCRaider`: closing in until the gun or bow reaches, and
  going to look when they hear you). Voice Control leaves them alone (`no_voice`). They get this mod's faces and
  rougher clothes (`RaiderGearSets`), names like "Rook the Raider", items-only healing, the aim pose, and no
  0-XNPCCore kill levels (`RaiderXnpcLevels` false). They open unlocked doors (SCore's opener, which shuts them
  again; `RaiderVanillaDoors` adds the vanilla one); locked doors stop them. Your turrets shoot them as enemies, as
  they shot the old raiders: the powered auto turret with "target zombies" on, the junk turret and junk sledge with
  "target enemies" on, and a motion sensor set to "target zombies" trips on them (`RaiderTurretsAsEnemies`; false
  leaves only "target strangers", and survivors, members and traders are never affected). A raider spawned as a sleeper shows the red dot of a clear quest, like a zombie.
- The raider classes, the spawn groups, the `XnpcWeapons` list and their names are **generated** by
  `Tools/gen_arsenal.py` from one table; edit the table and rerun it rather than the XML. `Tools/verify_arsenal.py`
  replays the framework's and this mod's patches and checks every class, AI package, archetype, spawn-group entry,
  name and duplicate. Weapons are tiered (0 early melee plus the pipe guns and the primitive bow, 1 wooden bow,
  iron crossbow and double barrel, 2 late guns): a body's weapons are capped by its class's tier and your
  gamestage's, and a radio call brings a survivor whose weapons fit your gamestage.
- **Two weapons each.** Every survivor and every raider carries one melee and one ranged weapon (a raider's
  `RaiderRangedShare` only decides which of the two it leads with). Ranged weapons are mostly bows and crossbows: a survivor's is a firearm only
  `SurvivorFirearmChance` (12%) of the time, a raider's `RaiderFirearmChance` (12%).
  - A **survivor** carries real vanilla weapons, both rolled for that NPC from the generated `XnpcWeapons` list
    (the raiders' weapons and tiers as their vanilla items, without fists and the rocket launcher: 41, in
    `Config/blocks.xml`), at or below the lower of its class's
    tier (T1 early, T2 middle, T3 late - the place and time of day it turned up) and your gamestage's. It fights
    with 0-XNPCCore's own weapon handling: the gun while it has ammo, the melee weapon up close and when the gun
    runs dry. The pair is saved with the NPC and, for a prospect or member, in the registry (`loadout=`), so every
    body the community re-creates for them (a call-back, a stand-in, a runner) carries the same two.
  - **Ammo is finite.** A new survivor starts with `XnpcSpawnAmmo` (30) rounds of its gun's basic ammo, or
    `XnpcSpawnArrows` (20) arrows or bolts, in the inventory window you see when you open them, and what it shoots
    is gone. Hand them more through that window whenever you like. (Ammo put in the window moves to their bag, where a
    bow can shoot it from, within a few seconds - but never while anyone has the window open, in multiplayer too, so
    nothing in it can be taken twice. On a dedicated server it moves when the window is closed. While the window is
    open they also don't bandage or swap weapons from it.)
  - **The stockpile refills members - only at home or at an outpost.** A member (or a posted member's stand-in)
    whose gun is down to fewer than `XnpcResupplyBelow` (10) rounds while standing inside the base's claim or at one
    of your outposts gets `XnpcResupplyAmmo` (30) rounds, or `XnpcResupplyArrows` (20) arrows or bolts, from the
    stockpile's ammo, at most once a `XnpcResupplyCooldown` (60 s). Out in the world nobody refills them: once the
    gun is dry they fight on with their melee weapon until you bring them home or hand them ammo. Strangers are never
    refilled. The stockpile pays in packing points: a round costs what it is worth at the packing station (its
    economic value, times `XnpcResupplyPointsMult`, 1), and an ammo bundle holds `PointsPerBundleAmmo` (2000) points,
    opened only when the last one is used up - about 220 9 mm rounds or 165 stone arrows a bundle. The log names
    every refill; `sc xnpc` counts them. With Locked & Loaded a member who carries magazines counts the rounds in
    them too, and the refill goes into those magazines: the stockpile pays only for the rounds they take, and nothing
    when they are full.
  - **Nobody comes back with new ammo.** A body the community re-creates for a member (a call-back, an outpost
    stand-in, a rescue stand-in, a runner's fresh body) starts with the rounds that member last carried (kept in the
    registry as `ammo=`), not a new survivor's 30 - and with Locked & Loaded, with the same magazines holding the
    same rounds (the magazines ride in the same `ammo=` value; an older SurvivorCommunity reading it keeps the loose
    rounds and skips them). If Locked & Loaded is gone by then, or one of the magazine kinds is, its rounds come back
    loose instead.
  - **Magazines, with Locked & Loaded** (1.4 or later). An NPC that carries a magazine for its gun reloads like you:
    the fullest magazine goes in and the old one comes back with what it had left. So with Locked & Loaded installed
    a new survivor's or raider's gun that takes magazines starts with Locked & Loaded's loaded magazines for its
    tier (T1 2, T2 3, T3 4; at T2 and T3 some are extended magazines or drums) instead of the loose rounds above.
    Of the weapons they carry, Locked & Loaded 1.4 has magazines for the pistol, the desert vulture, the SMG, the pipe
    machine gun, the AK-47, the tactical rifle, the M60, the sniper rifle and the auto shotgun. Bows, crossbows and
    the guns without magazines (the pipe pistol, pipe rifle and pipe shotgun, the double barrel, the hunting rifle,
    the lever-action rifle, the pump shotgun, the magnum) start with `XnpcSpawnAmmo` / `RaiderSpawnAmmo` as before,
    and so does every gun without Locked & Loaded. Loose rounds of its gun's ammo that a member has - handed over
    through their window, or from a save made before - go into their magazines whenever those have room, so none is
    dead weight; magazines for their gun handed over through the window go into their bag, where they use them. A
    member whose gun takes magazines but who has none (one from an older save, or given such a gun through the
    weapon menu) is issued empty ones the first time they are at home or at an outpost with it, and the stockpile
    fills them. `remags npc <id>` (Locked & Loaded's console) shows an NPC's magazines; `sc raiders` and
    `sc xnpc <id>` count them.
  - **A gun put away keeps its rounds.** 0-XNPCCore swaps the gun for the melee weapon up close, and the rounds left
    in the gun used to vanish with it (up to a magazine's worth per melee bout). Now they go back to the NPC's bag as
    loose rounds and are loaded again when the gun comes back (with Locked & Loaded the magazine itself goes back,
    as Locked & Loaded does it). The same for the gun an NPC is holding when it dies: its rounds drop in the
    backpack. And the gun in its hand is saved with it: the game keeps only the name of what an NPC holds, so every
    time its chunk unloaded behind you (or the game was saved and loaded) it came back with an empty gun, and with
    Locked & Loaded a magazine fewer. Now it comes back holding the same gun, loaded the same, magazine and all.
  - **Giving a member a better weapon.** Put the weapon in their inventory window and pick it in the dialog's weapon
    menu ("Use ..."), or tell them to use it by voice: they keep it for good in place of their weapon of that kind
    (the registry's `loadout=` follows), and the weapon it replaces comes back to you as a real item - into the
    base's storage boxes when they are loaded and have room, else your inventory, else a bag at your feet; any mods
    on the weapon you gave come back too. Only the weapons the survivors can carry (`XnpcWeapons`) are kept this way;
    anything else they just hold for now. The handed weapon's own quality is not kept (the NPC's copy is rolled 1-6,
    as 0-XNPCCore's are), and a gun given this way brings no ammo of its own.
  - **Overwhelmed, they keep shooting.** 0-XNPCCore puts the gun away for the melee weapon when an enemy is within
    3 m. With enough enemies on them within `OverwhelmedRadius` (12 m) a survivor keeps the gun instead, or goes back
    to it, and backs off as it shoots, as long as it has rounds: 2 enemies for a 1-2 star fighter, 3 at 3-4 stars, 4
    at 5-6, 5 at 7 (`OverwhelmedMin` / `OverwhelmedMax` / `OverwhelmedStarsPerStep`), 3 for anyone who is not a
    member (`OverwhelmedStrangers`). `Overwhelmed` false leaves 0-XNPCCore's swap alone.
  - **Not yet: holstering.** Survivors keep their weapon in hand when calm. 0-XNPCCore's controller drives their
    weapon pose and swaps the hand item several times a fight, so hiding the weapon alone would leave them miming
    an invisible gun; a proper holster (and the sling the plan wants) is a later task.
  - A **raider** rolls both weapons from the same list at the same tier cap, and a lead: `RaiderRangedShare` of them
    (30% / 40% / 50% for T1 / T2 / T3) rely on the ranged weapon and fight like a survivor (melee within 3 m, back to
    the gun at 5 m); the rest lead with the melee weapon - they charge in, and take the gun or bow only when you stay
    out of reach (farther than `RaiderLeadRangedDist`, 8 m, above them or where their path cannot get, for
    `RaiderLeadRangedSeconds`, 3 s), back to melee once you are within 3 m. Every raider starts with
    `RaiderSpawnAmmo` (24) rounds or `RaiderSpawnArrows` (15) arrows or bolts in its bag (with Locked & Loaded, a gun
    that takes magazines starts with its tier's loaded magazines instead) and is **never resupplied**:
    once the gun is dry it fights with its melee weapon. When it dies it drops a
    backpack with its leftover ammo and medical items, and each of its two weapons is in it `RaiderWeaponDropChance`
    (25%) of the time. The rocket launcher is never rolled. Killing one pays 600 XP, more the better its ranged
    weapon: a bow or crossbow 798 (tier 0) or 1002, a firearm 900, 1200 or 1398 (tier 0, 1, 2) - the old raiders'
    range (before party and difficulty scaling; `sc raiders` shows each one's `killXp`).
  - `sc loadout <name>` shows the pair and `sc loadout <name> <weapon> [weapon]` changes it (vanilla item names or
    their short names; `ranged-lead` / `melee-lead` for a raider; `sc weapons` lists them), `sc loadout near [r]` lists the pairs
    around you, `sc arms <name> melee|ranged` switches by hand. How: the NPC's own starting-item list gets both
    weapons before SCore copies it into the inventory, which is what lets SCore's weapon swap and its save of the
    held weapon accept either one.
- You meet them three ways. They spawn in the world on their **own layer** (below), every radio call brings
  one (the radio names the ranged weapon they carry), and `sc spawn [hostile] [male|female] [T1|T2|T3] [weapon]
  [ranged-lead|melee-lead] [Set]` or the creative menu puts one in front of you (`sc weapons` lists the weapon names;
  `melee`, `bow` or `firearm` picks one of that family). `sc raiders [r]` lists the raiders around you.
- **No new pack NPCs.** While this mod is installed, the NPC framework's sample cast (Nurse, Baker, Harley, the
  NPC fox) and any character pack's NPCs filed in the same groups no longer spawn; ours are the only NPCs that
  turn up. Those already in a world stay and behave as before, and their classes still load. The generator
  removes the framework's own biome spawn entries, and in every framework group (the POI sleeper groups behind
  `S_-Group_NPC_*` included) it swaps each pack NPC for our class of its weapon's tier and its side, since a group
  may not be empty: in a friendly group the 0-XNPCCore survivor class, in a hostile one our raider class. 0-XNPCCore's own survivors stay where its groups list them, but its own biome spawns of them are removed:
  they turn up at this mod's rates. Spawning one by hand (creative menu, test spawn cubes) still works. The radio
  and outpost walk-ins roll only our survivors, so `SurvivorLookChance` is no longer used.
- **Wild spawns are ours, not a slice of the framework's.** They used to be appended into the NPC framework's
  own groups, where they shared the 3 points of NPC against 97 points of nothing that those groups carry and
  came out at 0.4% - you could roam for an hour and meet nobody. They now have their own entity groups and
  their own `<spawn>` entries in all five biomes (`Config/spawning.xml`), which is what the framework's own
  guide asks add-ons to do, so the rate is ours alone and does not move when character packs are added or
  removed. The world is diced into 80x80 m areas; an area rolls one spawn entry at a time against its group,
  and a miss burns that entry for the area until `respawndelay` has passed. So the knobs are exactly: the odds
  of one roll producing somebody (`SURVIVOR_CHANCE` 6%, `RAIDER_CHANCE` 4%; the framework's own cast, now
  removed, rolled about 1-4%)
  and how often an area may roll again (`SURVIVOR_RESPAWN` half a game day, `RAIDER_RESPAWN` one). Which band
  an area draws from follows the framework's layout: wilderness has the early band by day and the middle one
  by night, towns the early band by day and the late one at night, city centres the last two. A survivor band is
  its tier's two classes (T1, T2, T3), whose weapons are rolled as above, and so is a raider band (its raiders lead
  with the melee weapon 70% of the time in the early band, 60% in the middle, 50% late).
  `core SurvivorCommunityDev spawns` prints the live odds, the entries for the biome you are standing in and that
  area's counters.
- The look and the four armor pieces are stored in the registry for prospects and members and re-applied on
  every load (vanilla never saves or sends a non-player's equipment). A survivor sent on a run that radios
  for help appears in the rescue event wearing their own face. `sc look <name>` rerolls a member's face,
  `sc gear <name> <Set|none>` dresses them in a whole set.
- **Body armor is never rolled away.** Helmet, gloves and boots each turn up with `SurvivorGearPieceChance`
  (0.8), which is what makes them look scavenged rather than uniformed, but at that chance one survivor in
  five had a bare torso and read as a missing mesh instead. The chest slot now ignores the chance and falls
  back to any set that has the piece; set `SurvivorGearAlwaysChest` false for the old behaviour. Only affects
  looks rolled from then on - anyone already wearing a look keeps it, and `sc gear <name> none` still strips.
- How: 0-XNPCCore builds and animates the body (its `EModelSdcsNpc` model and controller); this mod hands it the
  look before the first build (`Patches_Xnpc.cs`) and adds the few fixes it lacks for these classes
  (`Patches_Sdcs.cs`: the eyes, hit colliders shots can reach, the jump animation, the get-up, and freeing a
  despawned body's meshes). Strangers that were never a prospect keep their face only for the session.
- Their eyes track you like a player's. That needs one repair: the eye shader spins each eyeball about a pivot
  given in head-bone space, while the game re-roots every untagged skinned mesh of a non-player entity onto the
  pelvis - which slid the eyeballs out of the face. The mod tags the generated eye mesh the way vanilla's own
  eye meshes are tagged and roots it at the head.
- **They heal only with items.** The NPC framework heals all of its NPCs for free (30 health every 15 seconds
  below 70%, no item, and on this rig no animation either). That is refused for these bodies. Instead each
  one is stocked once with up to two first aid bandages and, now and then, a first aid kit - a survivor in the
  inventory you see when you open them, a raider in its bag (dropped in its backpack) - and uses one when at or below 60% health
  and not already healing, 12 seconds apart. They use it the way you do: the item goes into their hand, the
  player's own third-person bandage animation plays, and when it finishes the item's vanilla effects fire and
  their weapon goes back in their hand, so a bandage heals what a bandage heals. They cannot attack while
  bandaging; a knockdown interrupts it and hands the item back. Saved in the middle of it (its chunk unloading behind
  you, or the game), it comes back with the item back in its inventory and its weapon - its gun loaded as it was -
  in hand; killed in the middle of it, the item and the gun's rounds are in its backpack.
  Hand a survivor medical items and they will use those; loot a dead one and you get what they had left.
- Knocked down, they get up the way you do. The game picks the knockdown and get-up animations from a
  different animator layer for players than for NPCs, deciding by whether the entity is a player; these
  bodies run the player's animator without being players, so they asked for animations their layer does
  not have and skipped the get-up. One more thing had to go with it: the ragdoll keeps an NPC's position
  pinned to its pelvis until nearly the end, which on this rig lifted them several metres into the air as
  the get-up clip raised the pelvis. The mod marks their ragdoll as a player's and leaves them on the ground
  once the physics part is over (`XnpcBodyGetUp`; `XnpcBodyJumpAnim` and `XnpcBodyColliderLayers` switch the
  jump animation and the hit colliders the same way).
- Known limits: no per-weapon hand joints (the player rig has one weapon hand, which vanilla's third-person
  weapon offsets already handle), no dismemberment, other players in multiplayer would see them undressed
  (host/single player only, like the rest of this mod).

## With SurvivorOrigins: the Leadership skill

The two mods talk only through CVars on the local player; without SurvivorOrigins nothing changes.
- **Published** every second: `sc_followers` (members following you), `sc_inzone` (1 inside the claim),
  `sc_morale_high` (the high-morale threshold), plus `sc_morale` and the `buffSCMorale` HUD buff as before.
- **Counted** (+1, SurvivorOrigins pays XP and resets them): `sc_ev_favour` when a recruit favour is done,
  `sc_ev_rescue` when a runner in distress is rescued.
- **Applied**: `$soLeadXpMult` multiplies every member's skill and specialty XP; `$soLeadMorale` is a
  "Leadership +N" morale part; `$soLeadShield` shrinks every negative morale part by N, never past 0.

## Configuration (`Config/blocks.xml`, block `ConfigSurvivorCommunity`)

`MaxMembers`, `MaxFollowers`, `PostRadius`, `FoodPerMemberDay`, `AmmoPerRangedDay`, `PointsPerBundle*`,
`ValueOverrides`, `FuelItems`, `BedsBySleepingBag`, `BedBlocks`, `BedBlocksExclude`, `FacilityBlocks`, `LowMoraleDays`, `MissingDaysToPurge`, `RunRiskBase`,
`RunYieldBase`, `SuppliesFavours`, `PoiFavourRange`, `TraitsPerRecruit`, `DebugLogging`, and the
`Traits` table (weight, food-use multiplier, morale/mood offsets, starting skill bonus, preferred
specialty, XP / run-yield multipliers, timid), plus the milestone-3 numbers `MeleePerStar`, `RangedPerStar`,
`SpeedPerStar`, `HealthPerStar`, `CookFoodPerStar`, `CropFoodPerStar`, `MedicPerStar`, `PackBonusPerStar`,
`ScanRadius`, `FollowXpSeconds`, `DailyWitsXp`, and for the player-body survivors `SurvivorGearSets`, `SurvivorGearPieceChance`, `SurvivorGearAlwaysChest`, `SurvivorGearMixChance`,
`SurvivorGearQualityMin/Max`,
`SurvivorBaldChance`, `SurvivorFacialHairChance`. Weapons and ammo: `SurvivorFirearmChance`, `XnpcSpawnAmmo` /
`XnpcSpawnArrows`, `XnpcResupply`, `XnpcResupplyBelow`, `XnpcResupplyAmmo` / `XnpcResupplyArrows`,
`XnpcResupplyCooldown`, `XnpcResupplyPointsMult`, `Overwhelmed`, `OverwhelmedRadius`, `OverwhelmedMin` / `OverwhelmedMax`,
`OverwhelmedStarsPerStep`, `OverwhelmedStrangers`, `OverwhelmedHoldSeconds` (`AmmoPerRangedDay` is no longer charged; the
outposts only use it to guess which resource runs out first). Outposts: every `Outpost*` and `Radio*` property in the block
(commented there), the `OutpostRows` / `OutpostPrefabs` tables, and `OutpostAttacks` for the risk features.

A full game restart is needed after editing `Config/Localization.csv`.

## How it hangs together (for modders)

- NPCs are 0-XNPCCore's `EntityAliveSDXV4` survivors (SCore's legacy `EntityAliveSDX` still works for pack NPCs). A resident is SCore's *FarmHere* shape: `Leader`/`Owner` cvars
  cleared, `hired_<id>` removed from the player, leader cache evicted, `buffOrderGuard` with the guard
  position set to the post, `Persist=1`, static spawner source, plus the `SCResident` utility-AI package
  added at runtime (`Config/utilityai.xml`). A follower is a plain SCore hire; the order it holds (`CurrentOrder`) is
  read as a member state (`ScOrders`), never repaired unless it is one nothing gives a follower (None, Hire, Task).
- The recruit dialog is injected by xpath into **every** dialog that has a `Hire` response, so character
  packs join the pool without being listed. The custom operators (`ScCanRecruit`, `ScFavour`,
  `ScFavourDone`, `ScHasFavourItems`, `ScFavourBrief`, `ScStartPoiQuest`, `ScTakeFavourItems`, `ScJoin`)
  live in `SurvivorCommunity.dll`.
- NPC cvars (`sc_member`, `sc_owner`, `sc_cardio` ...) mirror the registry so XML can read them; a cvar of
  exactly 0 is never saved by the game, so "false" is always -1 or absent.
- Harmony targets: every one is listed in `ModApi.ExpectedTargets` and checked at startup (`patch verification:
  all 70 targets patched` with Voice Control loaded, 63 without; a `MISSING PATCH` line names one that did not
  bind). Among them
  `PersistentPlayerList.ReadXML/Write(string)` (registry load/save), the SCore NPC classes' `SetDead` (deaths) and
  `PostInit` (re-pin the spawner source), `SleeperEventData.Update` (no POI re-arm for favour quests), the two turrets'
  and the motion sensor's target filters and `EntityAliveSDXV4.HandleNavObject` (raiders), SCore's two NPC
  inventory packages (`NetPackageHarvestInventoryRequest` / `...Update`: a remote player's open window),
  `EntityAliveSDXV4.UpdateWeapon(ItemValue, bool)` (a gun put away keeps its rounds), and `EntityAliveSDXV4.Write` /
  `Read` (the gun in hand is saved after SCore's own data, as `SCM1` + the item, and put back when the NPC loads; an
  older SurvivorCommunity or SCore alone skips those bytes).
- Locked & Loaded is reached by name only (`ScMags.cs`: `ModManager.ModLoaded("LockedAndLoaded")`, then
  `REInvMags.NpcMagazines`' `Arm`, `Refill`, `Rounds`, `Record`, `Restore` and `Carries`, bound with their exact
  signatures once per game); there is no reference to its DLL. Missing or throwing, it is switched off for the game
  with one log line, and SC's NPCs use loose rounds.
