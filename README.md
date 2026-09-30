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
- **Walking at all.** These classes use the player body, and the game's move helper only pushes an entity
  along its path while the animator reports a motion state, skipping that check only for the root-motion
  animators every pack NPC uses. Without a fix a survivor stands in idle forever with a path it never walks;
  the mod reports motion for them so the AI's intent reaches their feet.
- **Walking in a straight line.** The same move helper writes a path direction in world space and then, one
  tick later, replaces it with a direction measured from the body's nose, which only root-motion bodies are
  spared. A survivor could therefore travel only where she was already looking, which tied her speed to her
  turn rate and left her orbiting her destination on a circle several metres wide. The mod puts the world
  direction back after that overwrite: she still turns at her own rate, but her feet follow the path.
- **Moving at a sensible speed, and animating at it.** For a body without root motion the class's move speeds
  scale real velocity instead of feeding an animator, so the templates' values came out as a sprint;
  `MoveSpeed` in `Config/entityclasses.xml` is the roaming pace. Following is a different number: the NPC
  framework takes `MoveSpeedPanic` and scales it by the player's own running modifiers, which is how a
  follower keeps up with you. The animator is fed the survivor's measured speed in metres per second, the
  same units a player's body gets, so the walk and run clips blend to match what she is actually doing.
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
  member (trait multipliers apply, fractions carry over), uses one medical bundle per injured member
  (which heals them) and `AmmoPerRangedDay` (0.25) ammo bundles per armed resident. This happens whether
  or not the base is loaded. A tooltip and the ledger (`sc status`) report what was used and whether anyone
  went hungry; the hungry flag feeds morale in milestone 4.

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
- **The trip.** They walk to the site at `RunWalkMps`, spend `RunSiteHours` (6) game hours searching its ground
  floor, then walk home.
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
  walking speed. `sc run <name> skip|offline|recall|home` drives a run for testing.

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
- **Call for survivors.** One call every `RadioCallDays`: a hireable survivor answers from a real POI
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
- **Every weapon, both sides.** There is one class per weapon and sex in two allegiances. Survivors
  (`scSurvivor<Sex><Weapon>`) extend the NPC framework's hireable templates, so they talk, hire, fight and
  join the community like its own NPCs. Raiders (`scRaider<Sex><Weapon>`) follow the framework's own hostile
  pattern: SCore's bandit entity, the vanilla `bandits` faction (which hates everyone), and its hostile melee
  and ranged AI. They cannot be talked to or recruited, and they fight your survivors as well as you.
  43 weapons: the framework's 24 NPC weapons plus NPC versions of the 19 vanilla combat weapons it lacks
  (bone knife, all three knuckles, pipe and stun batons, steel club, all three sledgehammers, stone and steel
  spears, steel axe, .44 Magnum, double barrel, lever action, primitive and compound bows, compound crossbow).
  Those new weapons copy the closest framework weapon's damage and timing with the vanilla model, icon and
  sounds, and animate on these bodies with the vanilla weapon's own hold type. Left out on purpose: tools
  (chainsaw, auger, nailgun, pickaxes, shovels, wrenches), event variants and the plasma baton, which has no
  model in 3.2.
- The classes, spawn weights, new weapon items and their names are **generated** by `Tools/gen_arsenal.py`
  from one table; edit the table and rerun it rather than the XML. `Tools/verify_arsenal.py` replays the
  framework's and this mod's patches and checks every template, loadout, dropped weapon, spawn-group entry,
  name and duplicate. Weapons are tiered (0 early melee, 1 pipe weapons and bows, 2 late guns): the spawn
  groups follow the tiers, and a radio call brings a survivor whose weapon fits your gamestage.
- You meet them three ways. They spawn in the world on their **own layer** (below), a radio call brings one
  with `SurvivorLookChance`, and `sc spawn [hostile] [male|female] [weapon] [Set]` or the creative menu puts
  one in front of you (`sc weapons` lists the weapon names).
- **Wild spawns are ours, not a slice of the framework's.** They used to be appended into the NPC framework's
  own groups, where they shared the 3 points of NPC against 97 points of nothing that those groups carry and
  came out at 0.4% - you could roam for an hour and meet nobody. They now have their own entity groups and
  their own `<spawn>` entries in all five biomes (`Config/spawning.xml`), which is what the framework's own
  guide asks add-ons to do, so the rate is ours alone and does not move when character packs are added or
  removed. The world is diced into 80x80 m areas; an area rolls one spawn entry at a time against its group,
  and a miss burns that entry for the area until `respawndelay` has passed. So the knobs are exactly: the odds
  of one roll producing somebody (`SURVIVOR_CHANCE` 6%, `RAIDER_CHANCE` 4%, against the framework's own 3%)
  and how often an area may roll again (`SURVIVOR_RESPAWN` half a game day, `RAIDER_RESPAWN` one). Which band
  an area draws from follows the framework's layout: wilderness is melee by day and pipe weapons by night,
  towns melee by day and full guns at night, city centres the last two. `core SurvivorCommunityDev spawns`
  prints the live odds, the entries for the biome you are standing in and that area's counters.
- The look and the four armor pieces are stored in the registry for prospects and members and re-applied on
  every load (vanilla never saves or sends a non-player's equipment). A survivor sent on a run that radios
  for help appears in the rescue event wearing their own face. `sc look <name>` rerolls a member's face,
  `sc gear <name> <Set|none>` dresses them in a whole set.
- **Body armor is never rolled away.** Helmet, gloves and boots each turn up with `SurvivorGearPieceChance`
  (0.8), which is what makes them look scavenged rather than uniformed, but at that chance one survivor in
  five had a bare torso and read as a missing mesh instead. The chest slot now ignores the chance and falls
  back to any set that has the piece; set `SurvivorGearAlwaysChest` false for the old behaviour. Only affects
  looks rolled from then on - anyone already wearing a look keeps it, and `sc gear <name> none` still strips.
- How: `Patches_Sdcs.cs` patches the game's player body model (`EModelSDCS`) so that it works on an NPC
  (it assumed a player on its game object), and SCore's weapon swap so it finds the generated rig.
  Strangers that were never a prospect keep their face only for the session.
- The pack's NPC weapons use hold types for the pack's own animators; on a player body they are animated
  with the hold type of the vanilla weapon they mirror (`CompatibleWeapon`), so handing them any pack weapon
  keeps the right pose.
- Their eyes track you like a player's. That needs one repair: the eye shader spins each eyeball about a pivot
  given in head-bone space, while the game re-roots every untagged skinned mesh of a non-player entity onto the
  pelvis - which slid the eyeballs out of the face. The mod tags the generated eye mesh the way vanilla's own
  eye meshes are tagged and roots it at the head.
- **They heal only with items.** The NPC framework heals all of its NPCs for free (30 health every 15 seconds
  below 70%, no item, and on this rig no animation either). That is refused for these bodies. Instead each
  one is stocked once with up to two first aid bandages and, now and then, a first aid kit - a survivor in the
  inventory you see when you open them, a raider in its loot bag - and uses one when at or below 60% health
  and not already healing, 12 seconds apart. They use it the way you do: the item goes into their hand, the
  player's own third-person bandage animation plays, and when it finishes the item's vanilla effects fire and
  their weapon goes back in their hand, so a bandage heals what a bandage heals. They cannot attack while
  bandaging; a knockdown interrupts it and hands the item back; a save in the middle puts the weapon back first.
  Hand a survivor medical items and they will use those; loot a dead one and you get what they had left.
- **No spawning invisible.** The framework's template flags them as bandits, which makes the game fade them to
  nothing at spawn so they can fade in. The fade-in never ran for a survivor, because NPC-class entities use a
  visibility check that ignores the fade, and only the player body's shaders honour it. They now start fully
  visible.
- **No vibrating upper body.** The game's "turn to face" call flips the sign of the pitch on every call, so an
  NPC facing you across any height difference rocked its torso back and forth; only the player body's
  torso-look animation layer made that visible. The pitch is now stepped without the flip.
- Knocked down, they get up the way you do. The game picks the knockdown and get-up animations from a
  different animator layer for players than for NPCs, deciding by whether the entity is a player; these
  bodies run the player's animator without being players, so they asked for animations their layer does
  not have and skipped the get-up. Two more things had to go with it: the ragdoll keeps an NPC's position
  pinned to its pelvis until nearly the end, which on this rig lifted them several metres into the air as
  the get-up clip raised the pelvis, and their own movement during the ragdoll read as walking, so the
  locomotion animations cut the get-up short. The mod marks their ragdoll as a player's, leaves them on the
  ground once the physics part is over, and reports no movement to the animator until they are up.
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
`ScanRadius`, `FollowXpSeconds`, `DailyWitsXp`, and for the player-body survivors `SurvivorLookChance`,
`SurvivorGearSets`, `SurvivorGearPieceChance`, `SurvivorGearAlwaysChest`, `SurvivorGearMixChance`,
`SurvivorGearQualityMin/Max`,
`SurvivorBaldChance`, `SurvivorFacialHairChance`. Outposts: every `Outpost*` and `Radio*` property in the block
(commented there), the `OutpostRows` / `OutpostPrefabs` tables, and `OutpostAttacks` for the risk features.

A full game restart is needed after editing `Config/Localization.csv`.

## How it hangs together (for modders)

- NPCs are XNPCCore's `EntityAliveSDX`. A resident is SCore's *FarmHere* shape: `Leader`/`Owner` cvars
  cleared, `hired_<id>` removed from the player, leader cache evicted, `buffOrderGuard` with the guard
  position set to the post, `Persist=1`, static spawner source, plus the `SCResident` utility-AI package
  added at runtime (`Config/utilityai.xml`). A follower is a plain SCore hire.
- The recruit dialog is injected by xpath into **every** dialog that has a `Hire` response, so character
  packs join the pool without being listed. The custom operators (`ScCanRecruit`, `ScFavour`,
  `ScFavourDone`, `ScHasFavourItems`, `ScFavourBrief`, `ScStartPoiQuest`, `ScTakeFavourItems`, `ScJoin`)
  live in `SurvivorCommunity.dll`.
- NPC cvars (`sc_member`, `sc_owner`, `sc_cardio` ...) mirror the registry so XML can read them; a cvar of
  exactly 0 is never saved by the game, so "false" is always -1 or absent.
- Harmony targets: `PersistentPlayerList.ReadXML/Write(string)` (registry load/save),
  `EntityAliveSDX.SetDead` (deaths), `EntityAliveSDX.PostInit` (re-pin the spawner source),
  `SleeperEventData.Update` (no POI re-arm for favour quests). Everything else is XML or `ModEvents`.
