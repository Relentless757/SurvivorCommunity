# Survivor Community: console commands for testing

The console commands of Survivor Community (SC) for 7 Days to Die 3.4 and of the bundled LandClaimUpgrade (LCBU). Many of them change your save (they give resources, force events or kill people), so test on a save you don't mind breaking. For what the mod does, see [README.md](README.md) (`README.txt` in the mod folder).

## How to use the console

- Press **F1** to open the console. Commands are built for single player (you are the host). Most of them refuse or do nothing on a pure client.
- SC has one command, `sc` (long name `survivorcommunity`). Type `sc` alone for the same output as `sc status`. An unknown subcommand prints the full one-line syntax list.
- Every `sc` answer is also written to the log, each line starting `[SC console]`, with a `> sc <what you typed> (time)` line before it. The log is `%USERPROFILE%\AppData\LocalLow\The Fun Pimps\7 Days To Die\Player.log`. Send that file with a bug report. (`lcbu` output goes to the console; do not count on it being in the log.)
- Words are not case sensitive. Aliases work: `facility`/`facilities`, `outpost`/`outposts`, `enclave`/`enclaves`, `band`/`bands`, `infest`/`infestation`/`infestations`, `sleepers`/`sleeper`, `defense`/`defence`.
- Resource words: `food`, `meds`, `ammo`, `mats`, `fuel`, `water`, `luxury` (also accepted: `medical`, `medicine`, `materials`, `material`, `luxuries`, `lux`, `alcohol`).
- Skill words: `cardio`, `wits`, `fighting`, `shooting`. Specialties: `Medicine`, `Cooking`, `Mechanics`, `Gardening`, `Chemistry`, `Craftsmanship`.
- Tier words `T1`, `T2`, `T3` are the three survivor/raider class tiers.

### Naming a person: `<id|name>` and `<member>`

- A number is an id. For a community **member** it is the member id, which is also the NPC's entity id. `sc members` lists every member with its id. Raiders, strangers and enclave people are not members: get their entity id from `sc raiders`, `sc loadout near`, the output of `sc spawn`, or the vanilla `le` command.
- A name is matched as a **case-insensitive substring** of the member's name, and the **first** member that matches wins. `sc xp ana wits 50` can hit "Hannah" before "Ana". Use the id when names overlap.
- Names with spaces: the console splits on spaces, so type a single word that is part of the name (a first name is enough, because of the substring match).
- Commands that act on a body in the world (`look`, `paint`, `gear`, `anim`, `aim`, `recruit`) first try a member name, then an entity id, then a display-name substring among survivors within about 200 m. The NPC must be loaded (near you).
- `<member>` means the same thing but wants a registered community member.
- Enclave, outpost, band and infestation ids are plain numbers shown by their `list` subcommand (`sc enclave list`, `sc outpost list`, `sc band list`, `sc infest list`).

### Warning labels used below

- **[READ]** read-only diagnostic.
- **[CHEAT]** changes the save: gives or takes resources, sets standing, forces an event, moves people.
- **[DESTRUCTIVE]** hard or impossible to undo. Make a backup of the save first.
- **[HOST]** host only (single player counts as host).

---

## Most useful for testing

| Command | What it is for |
|---|---|
| `sc status` | Home position, day, morale, hungry/thirsty, beds, stock of every resource, member counts, ledger. |
| `sc members` | Every member and prospect with id, status, post, skills, buffs. Where you find ids. |
| `sc dump` | `status` plus `members` plus a config line. Paste this into bug reports. |
| `sc day` | Simulates one day rollover (upkeep, morale, outposts, runs). [CHEAT] |
| `sc give <res> <n>` | Add to the stockpile (`sc give mats 10`; negative takes). [CHEAT] |
| `sc morale [n]` | Morale breakdown; `n` overrides it for the session (`-1` clears). |
| `sc spawn [hostile] [male\|female] [T1\|T2\|T3] [weapon]` | A survivor stranger, or a raider, in front of you. [CHEAT] |
| `sc recruit <id\|name>` | Join a loaded survivor to the community without the favor. [CHEAT] |
| `sc names` / `sc names test` | Streamer names: the list file, names given and left; the next names in line. [READ] |
| `sc xnpc [id\|name]` | Counters for all NPC hooks, or one NPC in full detail (AI, weapons, orders). Main bug-report probe. [READ] |
| `sc craft [<name>]` | Crafting/work-shift status for the community or one member. [READ] |
| `sc enclave list` / `reveal all` | List enclaves (settlements of other survivors); reveal them on the map. |
| `sc enclave standing <id> <points>` | Set an enclave's standing (75 or more is Allied). [CHEAT] |
| `sc enclave info <id>` | One enclave in detail. [READ] |
| `sc attack [now] [size]` | Call a horde on your home base. [CHEAT] |
| `sc attack status` / `end` | Show the open attack; end it. |
| `sc noise` / `sc attention <0-100>` | Base noise breakdown; set the attention that triggers hordes. |
| `sc defense [horde]` | Defense rating against a horde, term by term. [READ] |
| `sc watch` | Watch posts, guards, hours left. |
| `sc run <member> <res>` / `sc runners` | Send a supply run; list who is out. |
| `sc radio` | Radio stations, power, handheld radio state, runner talk. [READ] |
| `sc radio give <member>` | Put a handheld radio in a member's inventory. [CHEAT] |
| `sc outpost list` / `yield` | Outposts and tonight's yield. [READ] |
| `sc poi` / `sc poi near [r]` | The POI you stand in; outpost candidates nearby. [READ] |
| `sc band spawn [drifters\|raiders]` | Put a band on the road, walking at you. [CHEAT] |
| `sc infest add` | Infest the POI you stand in. [CHEAT] |
| `sc sleepers [scan\|repair]` | Zombie-cap fix status and repair. |
| `sc voice` / `sc voice who` | Voice system status; who the nearest NPC is and which voice lines it can say. [READ] |
| `sc restore <member>` | Respawn a resident whose body is gone. [CHEAT] |
| `sc relink` | Re-link NPC bodies to the registry. Try this before reporting a "lost" member. |
| `sc save` | Force-write the SC registry. |
| `lcbu list` / `lcbu verify` | Land claims and their upgrade records; consistency check. [READ] |

---

# Full reference

Syntax conventions: `<x>` required, `[x]` optional, `a|b` choose one. `sc` first, then the subcommand.

## 1. Community and members

### `sc status` (also bare `sc`) [READ]
Prints host/registry state, home position, owner, day, morale, hungry/thirsty flags, beds, facilities, crops, daily water, stock of all seven resources, member counts, zone bounds and the recent ledger.
Example: `sc status`

### `sc members` [READ]
One line per member (id, name, class, status, post, mood, injured, ranged/melee, run leg, where they are, order state, leader, AI packages) and a skills line (stars/XP, specialty, traits, damage/speed/HP bonuses, buffs). Prospects (people not yet recruited) are listed after.
Example: `sc members`

### `sc dump` [READ]
Runs `status`, then `members`, then prints a config summary line.

### `sc home`
Finds or re-establishes the community home from your land claim. Prints the home position, or "place a land claim".
Example: `sc home`

### `sc day` [HOST] [CHEAT]
Advances the community by one simulated day (runs the whole daily routine, outposts included), then prints status. Cannot be undone; it does not move the world clock. Do not run it repeatedly unless you mean to.
Example: `sc day`

### `sc morale [n]` [CHEAT when `n` is given]
Without `n`: recomputes and prints the morale breakdown plus low-morale days, threat, death penalty, enclave loss, allies and nests near home. With `n` (0-100): sets a session-only override; `-1` clears it.
Example: `sc morale 20`, then `sc morale -1`

### `sc xp <id|name> <cardio|wits|fighting|shooting|spec> <n>` [CHEAT]
Adds `n` skill XP (or specialty XP with `spec`) to a member, re-applies their bonuses and prints their stars.
Example: `sc xp Dana spec 60`

### `sc injure <id|name>` [CHEAT]
Toggles a member's Injured flag (run it twice to undo).
Example: `sc injure 17`

### `sc restore <member>` [CHEAT]
Respawns a resident whose body is gone (or merely unloaded) at their post. Refuses with a reason if it cannot.
Example: `sc restore Dana`

### `sc repost <member>` [HOST] [CHEAT]
Gives a loaded resident a new post (they walk there). Cure for a resident who is stuck. Refuses if they are not a resident, are on watch (relieve first) or are not loaded.
Example: `sc repost 17`

### `sc relink`
Re-links NPC bodies and the registry now. Safe.

### `sc recruit <id|name>` [CHEAT]
Makes a loaded survivor join the community without doing their favor. All other rules still apply (a home must exist, room on the roster, hireable, not someone else's hire).
Example: `sc recruit 4521`

### `sc trust <id|name> [forgive]`
Without `forgive`: where the survivor stands with you (trust level, hostile to anyone, whether you can melee them, whether they are your enemy). With `forgive`: clears a grudge so they are neutral again [CHEAT]. With no name: usage and a summary.
Example: `sc trust 4521 forgive`

### `sc save`
Writes the SC registry to disk now.

### `sc facility [list]` and `sc facility level <x> <y> <z> <level>` (alias `facilities`)
`list` [READ]: every well and dew collector in the loaded chunks of the home zone, with level and water per day. `level` [CHEAT]: sets one's level (a well is replaced by that level's block, free; the level is clamped to the block's maximum). Use the parent block's coordinates exactly as `list` prints them.
Example: `sc facility level 812 60 -144 3`

### `sc drinks [on|off]`
Prints the round-of-drinks state (cost, morale, whether you can pour now). `on` pours one free and marks today; `off` ends it and allows another today. [CHEAT for on/off]
Example: `sc drinks on`

### `sc scan` [READ]
Re-counts beds, facilities and crops in the home zone now and prints the timing.

## 2. Resources and morale

### `sc give <food|meds|ammo|mats|fuel|water|luxury> <n>` [CHEAT]
Adds `n` to that resource (negative takes). Prints the new value.
Example: `sc give water 20`

### `sc barter <give> <get>` [CHEAT]
Does the radio barter trade once and ignores the once-a-day wait.
Example: `sc barter food ammo`

### `sc scout <food|meds|ammo|mats|fuel>` [CHEAT]
Does the radio scouting once and ignores the once-a-day wait.
Example: `sc scout meds`

(`sc morale`, `sc drinks` and `sc day` are in section 1.)

## 3. Recruiting and trust

See `sc recruit`, `sc trust`, `sc spawn` (strangers to recruit) and `sc look`, all above or in section 13.

### `sc names` [READ]

Streamer names (README section 3: your viewers' names from a text file for the new survivors SC makes). Prints whether
the feature is on (`StreamerNames`) and for whom (`StreamerNamesFor`), the names file's full path, how many names it holds
(for a woman, for a man, for anyone, and the lines skipped or cut), how many were given in this save and how many are
left, this session's counts (given, kept their own name, not for this setting, no fitting name left) and the last names
given with the NPC's class, kind and entity id. Not to be confused with `sc enclave names` (sample enclave group names).

### `sc names reload` [READ]

Reads the names file again now and prints its summary. You rarely need it: SC reads the file again by itself whenever it
has changed, the next time a name is needed.

### `sc names test [n] [male|female]` [READ]

The next `n` names (default 5, at most 100) a woman and a man would get, in order, without giving them out. `male` or
`female` shows one side only. Works with the feature off too, to check a list before a stream.

### `sc names reset` [HOST] [CHEAT]

Clears the save's record of the names given out, so every name on the list can be given again in this save (for
testing). NPCs that already have a name from the list keep it.

## 4. Runs and runners

### `sc run <id|name> <food|water|meds|ammo|mats|fuel>` [CHEAT]
Sends a member on a supply run for that resource. Prints the destination and expected yield, or why it was refused.
Example: `sc run Dana ammo`

### `sc run <id|name> recall|home|skip|offline` [CHEAT]
Drives a **physical** run (tests): `recall` turns them home; `home` forces them to the gate now (the run's dice may kill them); `skip` ends the current leg now; `offline` takes the body offline.
Example: `sc run Dana skip`

### `sc runners` [READ]
Lists who is out, their leg and distance, and the physical-run settings.

### `sc distress <id|name>` [CHEAT]
Forces a radio distress call from a member on a run (a physical run must be at the site). Refuses if there is no POI in range or no radio online.
Example: `sc distress Dana`

## 5. Jobs, work and crafting

There is no `jobs`, `work` or `stuck` subcommand. Crafting and work shifts are under `craft`; stuck-NPC counters are in `sc xnpc`; the cure for a stuck resident is `sc repost`.

### `sc craft` [READ]
Crafting on/off, work shifts on/off, hook counters, XP saves, work and book summaries.

### `sc craft table` [READ]
Crafting levels by star count for every specialty.

### `sc craft <id|name>` [READ]
One member: specialty, stars, XP, which progression skills they have against what their stars want, which recipes they can make, their roles, their work shift and this session's craft counts.
Example: `sc craft Dana`

### `sc craft <id|name> spec <Specialty>` [CHEAT]
Changes the member's specialty (Medicine, Cooking, Mechanics, Gardening, Chemistry, Craftsmanship). Test aid.
Example: `sc craft Dana spec Cooking`

### `sc craft <id|name> end`
Ends the member's work shift now.

## 6. Defense: base attacks, sieges, noise, watch, towers

### `sc noise [add <n> | clear]`
Prints the base noise and attention breakdown and the attack state. `add` adds event noise (it fades); `clear` zeroes event noise. [CHEAT for add/clear]
Example: `sc noise add 20`

### `sc attention <0-100>` [CHEAT]
Sets the dead's attention. At 100 a horde is called once you are home.
Example: `sc attention 100`

### `sc attack [now] [size]` [CHEAT]
Calls a horde on the home base regardless of attention, gap or blood moon. It arrives 1-2 game hours later; `now` brings it at once; `size` sets the horde size. If you are far from the claim the defenders fight it without you.
Example: `sc attack now 12`

### `sc attack status` [READ]
The open attack and what the next call waits for.

### `sc attack end` [DESTRUCTIVE]
Despawns the attackers and closes the attack with no result recorded.

### `sc attack settle [held|damaged|overrun]` [CHEAT]
Ends the open attack now as a fight without the player: on the defense rating, or forced to the outcome you name. Members can die (the log line "base attack without the player" has the rolls).

### `sc defense [horde]` (alias `defence`) [READ]
The defense rating term by term against the open attack's horde (or the number you give, or the next one's usual size), the odds of a fight without you and the highest death risk.
Example: `sc defense 15`

### `sc siege [held|damaged|overrun]` [HOST] [CHEAT]
Runs the blood-moon siege now as if a blood moon passed with you away. Settled on the defense rating, or forced to the outcome you name. Members can die.
Example: `sc siege overrun`

### `sc watch [list]` [HOST] [READ]
Lists Watch posts (by number), guards, hours left, who is resting.

### `sc watch assign <post> <member>` [HOST] [CHEAT]
Puts a member on a shift at a post, with the same rules as the radial menu (ammo, rest, room). A post is its list number or `x,y,z` (no spaces).
Example: `sc watch assign 1 Dana`

### `sc watch relieve <post|member>`, `sc watch end <post|member>`, `sc watch place <post|member>` [HOST]
`relieve`: ends the shift early, no rest. `end`: ends it as if its hours ran out (rest applies). `place`: puts the guard on the post now. Needs the guard loaded.

### `sc watch rest <member> [clear]` [HOST]
Shows the member's rest left; `clear` lets them take a watch at once.

### `sc watch scan` [HOST]
Re-scans the zone's loaded chunks against the post list, then prints the list.

### `sc tower [info]` [READ]
Describes the Guard Tower prefab (size, anchor, Watch post, blocks).

### `sc tower check [rot]` [READ]
Look at the ground where the tower should stand; says whether the kit would fit there. `rot` is 0-3 (default: facing as if placed from where you stand).

### `sc tower stamp [rot]` [HOST] [DESTRUCTIVE]
Same as `check`, then **builds the tower at once** (no kit used). Overwrites blocks in the footprint. Back up first.

## 7. Radio

### `sc radio` [READ]
Remembered radio stations and their live power, whether the home station is online, the handheld radio state, each runner's radio talk, queue and counters.

### `sc radio give <member>` [CHEAT]
Puts a handheld radio in the member's inventory.
Example: `sc radio give Dana`

### `sc radio talk <member> <out|arrived|found|empty|trouble|home|near|recall>` [CHEAT]
Makes the member say that radio moment now (as a runner would).
Example: `sc radio talk Dana trouble`

### `sc radio attacked <member> [force]` [CHEAT]
The member's call for help as if a zombie had just hurt them. `force` ignores the rules.

(`sc scout`, `sc barter` and `sc distress` are radio features too; see sections 2 and 4.)

## 8. Outposts

An outpost is a POI you hold with a land claim. Establishing one needs you inside the POI with a claim of yours there.

### `sc outpost list` (default) [READ]
Slots used, radio state, debts, one line per outpost (id, type, level, mode, state, staff, yield, distance).

### `sc outpost yield` [READ]
Each outpost's raw yield and tonight's estimate, plus network totals, upkeep and haulage.

### `sc outpost security <id>` [READ]
Security breakdown and attack chance for one outpost.

### `sc outpost add [row|scavenge]` [HOST] [CHEAT]
Stand in the POI where you placed a claim. Makes it an outpost; the optional word forces the outpost type (a POI-type id from SC's table, or `scavenge`; an unknown word keeps the type it was given). Test aid.

### `sc outpost remove <id>` (alias `disband`) [DESTRUCTIVE]
Disbands the outpost.

### `sc outpost pending <id> <n>` [CHEAT]
Sets the waiting (undelivered) yield of the outpost's main resource.

### `sc outpost reclassify <id>`
Re-reads the POI and re-sets the type and tier.

### `sc outpost state <id> <Active|Idle|Contested|Lost>` [CHEAT]
Forces the state.

### `sc outpost level <id> <1-3>` [CHEAT]
Sets the level (free).

### `sc outpost mode <id> <normal|quiet|push>` [CHEAT]
Sets the outpost's work mode.

### `sc outpost post <id> <member>` and `sc outpost recall <member>`
Posts a member to an outpost's garrison; recalls a posted member.

### `sc outpost upgrade <id>`
Does the paid upgrade (costs stock).

### `sc outpost attack <id> [raid]` [CHEAT]
Starts an attack on the outpost (`raid` for a raid rather than a plain attack).

### `sc outpost demand <id>`, `pay <id>`, `refuse <id>`, `repair <id>` [CHEAT]
`demand`: raiders demand ammo and food now. `pay` / `refuse`: answers the demand (as on the radio). `repair`: repairs a damaged outpost.

### `sc poi` [READ]
Describes the POI you stand in: tier, sleepers, outpost type and why, cleared or not, distance from home, enclave or infestation there.

### `sc poi near [r]` [READ]
Outpost candidates within `r` meters (default 500, 20-5000), nearest 25.
Example: `sc poi near 500`

### `sc poi classify <prefab>` [READ]
What a prefab would produce as an outpost.

### `sc poi census` [READ]
Counts real POIs in the world by the resource they would supply.

## 9. Enclaves (other survivor settlements)

All enclave subcommands except `names` are host only. `<id>` is the enclave id from `list`. If enclaves are off in the config, some print that and stop.

### Overview and map
- `sc enclave list` (default) [READ]: every enclave, id, state.
- `sc enclave info <id>` [READ]: one enclave in detail (kind, standing, gifts, barter, calls).
- `sc enclave reveal <id|all>` and `sc enclave hide <id|all>`: marks enclaves as found (with map markers) or hides them again.
- `sc enclave sim [late]` [READ]: plans a fresh seeding as a dry run (new-save rules, or `late`). Changes nothing.
- `sc enclave names [n] [hostile]` [READ]: sample generated names (default 10, 1-200). Works anywhere.

### People
- `sc enclave people <id>` [READ]: their people with `p<id>`.
- `sc enclave spawn <id>` [CHEAT]: spawns their people now (makes them first if needed). Their chunks must be loaded.
- `sc enclave despawn <id>`: removes their bodies for a short time.
- `sc enclave kill <id> [<person id>|all]` [DESTRUCTIVE]: kills the leader (no argument), one person (`p3` or `3`) or all. Killing everyone makes the enclave fall for good.
- `sc enclave trade <id>` [READ]: the leader's trade list, stock and prices.
- `sc enclave release <id>`: sends an enlisted person home.

### Standing
- `sc enclave standing <id> <points>` [CHEAT]: sets standing (the usage line shows the minimum; the maximum is 100). 75 or more is Allied.
- `sc enclave hostile <id>` [DESTRUCTIVE]: turns the enclave hostile **for good**. Only `reseed confirm` undoes it.
- `sc enclave reseed confirm` [DESTRUCTIVE]: drops **every** enclave and seeds the world again. Testing only. Without `confirm` it prints a warning and does nothing.

### Requests
- `sc enclave requests` [READ]: all open requests.
- `sc enclave request <id>` [READ]: this enclave's request and why it can or cannot get one.
- `sc enclave request <id> offer <resource|generator|gun|missing|infestation> [food|meds|ammo|mats|fuel] [small|medium|large] [force]` [CHEAT]: makes the enclave ask now. `force` goes past the open-request and per-day limits.
- `sc enclave request <id> accept`, `complete [points]`, `fail`, `lapse`, `clear`, `fate <alive|dead>` [CHEAT]: moves the request along. `complete` pays the standing reward (for a gun request, `points` sets it). `fail` and `lapse` cost standing. `clear` withdraws it with no change and resets the cooldown. `fate` fixes whether a missing survivor is found alive or dead. A siege request is handled by `siege` instead.
Example: `sc enclave request 3 offer resource meds medium`

### Siege
- `sc enclave siege <id>` [READ]: state and what its horde would be.
- `sc enclave siege <id> now [force]` [CHEAT]: calls a siege on the enclave. `force` skips the radio, range, discovery and blood moon rules.
- `sc enclave siege <id> win` [CHEAT]; `sc enclave siege <id> lose` [DESTRUCTIVE: the enclave can fall]; `sc enclave siege <id> clear` (withdraws it, attackers despawned).

### Allies and trade
- `sc enclave refill [now]`: lists the refills owed after a lost enclave. `now` moves a new group in at once (one anyway if none is owed). [CHEAT for now]
- `sc enclave gift <id>` [READ]; `sc enclave gift <id> give <res> [small|medium|large] [force]` [CHEAT]: gives a gift as if handed over (+3 standing, once a day; `force` ignores the wait). Resources include `water` and `luxury`.
- `sc enclave barter <id>` [READ]; `sc enclave barter <id> <give> [get]` [CHEAT]: ally barter trade now (ignores the once-a-day wait).
- `sc enclave call <id>` [READ]; `sc enclave call <id> fighter|drop [force] [now]` [CHEAT]; `sc enclave call <id> cancel`: radio calls to an ally. `force` ignores the cooldown and the Allied rule; `now` brings them at once.

### Raids, moves, joins
- `sc enclave raid <id>` [READ]: a hostile enclave's raids and each outpost's distance. `sc enclave raid <id> <outpost id> demand` makes their demand now; `... now` makes it and refuses it at once, which calls the raid. [CHEAT]
- `sc enclave move <id>` [READ]; `... sim` [READ] shows where it would go today; `... now` [CHEAT] moves the group at once.
- `sc enclave join <id>` [READ]; `... sim` [READ]; `... now [force]` [CHEAT]: the whole group joins you. `force` ignores the Allied and standing rule.
- `sc enclave arrivals [now]`: lists everyone on the way to you; `now` makes them due at once (they still only turn up while you are home). [CHEAT for now]

Aliases accepted: `gifts`, `calls`, `raids`, `moves`.

## 10. Bands and wild raiders

All `band` subcommands are host only.

- `sc band [list]` [READ]: bands on the road.
- `sc band info <id>` [READ]: route, position, people.
- `sc band spawn [drifters|raiders] [trouble]` [CHEAT]: a new band about 140 m from you, walking through you.
- `sc band here <id>` [CHEAT]: brings the band to you.
- `sc band advance <id> [hours]` [CHEAT]: moves them on (0-240 h, default 1).
- `sc band trust <id>` [CHEAT]: drifters trust you. Refused for raiders or bands that are against you.
- `sc band kill <id> [<person id>|all]` [DESTRUCTIVE]
- `sc band clear <id|all>` [DESTRUCTIVE]: ends bands and removes them from the map.
- `sc band roll` [CHEAT]: the daily band roll now.

### `sc raiders [r]` [READ]
Every living raider within `r` meters (default 40, 2-400): id, class, weapons, rounds, level, targets, health, speed, kill XP, marker. Ends with a count and a one-line wild-raider summary.
Example: `sc raiders 100`

### `sc raiders wild` [READ]
Counters for SC's wild raider spawn entries.

## 11. Infestations

All host only.

- `sc infest [list]` [READ]
- `sc infest info <id>` [READ]
- `sc infest add [<x> <z>]` [CHEAT]: infests the POI you stand in (or the one at x z). No roll, no distance or tier rules.
- `sc infest clear <id|all>` [DESTRUCTIVE]: ends the infestation record and markers only; rooms are left as they are.
- `sc infest spread <id>` [CHEAT]: spreads now.
- `sc infest roll` [CHEAT]: the daily roll now.
- `sc infest reveal <id|all>` and `sc infest hide <id|all>`: marks as known (with markers) or hides.

## 12. Sleepers (zombie-cap fix)

All host only.

- `sc sleepers [status]` [READ]
- `sc sleepers scan` [READ]: lists sleeper rooms that were closed empty by the enemy cap.
- `sc sleepers repair` [CHEAT]: makes those rooms due, so they spawn when a player comes near.

## 13. NPC looks, gear, weapons, animation and voice

### `sc spawn [hostile|raider] [male|female] [T1|T2|T3] [weapon] [weapon|meleeonly] [ranged-lead|melee-lead] [Set]` [CHEAT]
Spawns an NPC in front of you: a survivor stranger (3 m away), or with `hostile` a raider (6 m away). Each weapon word fixes that kind of its pair: an item name, its short name, or `melee` / `bow` / `firearm` for one of that family. `meleeonly` carries no ranged weapon. The lead words only apply to raiders. `Set` is an armor set or outfit name. It is saved with the world. `sc weapons` lists weapon words.
Note: a raider of the 0-XNPCCore bandit classes carries one weapon from its class list and no lead; leave out `ranged-lead` / `melee-lead` and name at most one weapon.
Example: `sc spawn hostile male T2 Rifle`

### `sc weapons` [READ]
Weapon words, the firearm chances, ammo amounts and the magazine state.

### `sc loadout <id|name>` [READ] and `sc loadout <id|name> [weapon] [weapon|meleeonly] [ranged-lead|melee-lead]` [CHEAT]
Shows an NPC's two weapons, or gives it new ones (one weapon replaces the one of its kind).

### `sc loadout near [r]` [READ]
Pairs of survivors and raiders within `r` m (default 40).

### `sc loadout sim [n] [hostile] [T1|T2|T3] [any]` [READ]
Rolls `n` loadouts (default 1000) without creating anyone.

### `sc arms <id|name> [melee|ranged]` [CHEAT when a word is given]
Shows the NPC's weapons; with the word, puts that one in its hand.

### `sc skills sim [T1|T2|T3] [n]` [READ]
Starting-skill statistics for `n` new survivors per tier.

### `sc look <id|name>` [CHEAT]
Re-rolls the face of a loaded survivor (same sex and clothes). Saved.

### `sc paint <id|name> [body <id> | face <id> | tattoo <shape>[/ink] | none | roll]`
Name alone: shows their paints and what the lists offer [READ]. Otherwise sets, clears (`none`) or re-rolls (`roll`) the paints and rebuilds the body [CHEAT]. Paint names may contain spaces; the tattoo ink follows a `/`. Needs Sonja's Vanity Vault 3.3 (the paints come from it); without it, it says Vanity Vault is not loaded.
Example: `sc paint Dana tattoo Maori 03/Royal Lapis`

### `sc gear <id|name> <Set|Outfit|none>` [CHEAT]
Dresses a loaded survivor in a whole armor set or outfit, or strips them. `sc gear` with too few arguments lists the sets and outfits.

### `sc gear sim [n] [hostile] [T1|T2|T3] [any] [male|female]` [READ]
Clothes roll statistics.

### `sc anim <id|name>` [READ]
Animator diagnostics for a survivor (controller, clips, parameters).

### `sc aim [id|name]` [READ]
Aim-pose counters and every NPC in a ranged fight, or one NPC in detail. A number also finds raiders.

### `sc portrait <id|name|all>`
Re-shoots portraits for the community window. `sc portrait frame [fov dist yaw eyelift aimlift]` shows or sets the camera framing for this session.

### `sc voice` [READ]
Voice switches, player in use, voices found, and the voice gate.

Other voice subcommands:

- `sc voice play <line> [id|name]`: a survivor (that one, or the nearest with a voice within 20 m) says a clip now.
- `sc voice stop`, `sc voice rescan` (re-reads the voice folder without a restart), `sc voice gate` [READ].
- `sc voice sayline [line] [seconds]`: the nearest survivor is given a say-line through Voice Control after a delay (default `ammo.dry`, 5 s).
- `sc voice context [id|name]` [READ]: the paragraph Voice Control's chat is given for an enclave person.
- `sc voice who [id|name]` [READ]: race, sex, origin, voice chain and which lines the NPC can say.
- `sc voice greet [id|name]`, `sc voice chatter [id|name]`: make a greeting or chatter line play now.
- `sc voice taunt <spot|search|lost|hit|killed|idle|dying|retreat> [id|name]`: the nearest raider (40 m) taunts now.
- `sc voice barks` [READ]: bark switches and counters.
- `sc voice line <words>` [READ]: the clip key a phrase maps to and whether the nearest survivor has it.

## 14. Diagnostics

### `sc xnpc` [READ]
Counters for the NPC hooks (voice control, body hooks, stuck rescue, ammo, weapons, melee, shooting skill, get-ups, relaxed pose) and a count of loaded members, hire contracts, raiders and other NPCs.

### `sc xnpc <id|name>` [READ]
One NPC in full: class, hostility, AI action and the foe's visibility, navigation, post, record, look, worn armor, weapons, ammo, levels, orders, movement, medic state. This is the main probe for "why does this NPC do X".
Example: `sc xnpc Dana`

### `sc dump`, `sc status`, `sc members`, `sc scan`
See section 1.

## 15. LandClaimUpgrade (`lcbu`, long name `landclaimupgrade`)

`lcbu` alone prints its help. State-changing subcommands need the server/host. `[x y z]` omitted means the claim nearest to you.

| Syntax | What it does |
|---|---|
| `lcbu list` [READ] | Every land claim: owner, level, zone, record, tile entity, LPBlocks. |
| `lcbu at <x> <y> <z>` [READ] | Claim info at an exact block, with every zone piece. |
| `lcbu near [n]` [READ] | The `n` nearest claims to you (default 5). |
| `lcbu set <level> [x y z] [force]` [HOST] [CHEAT] | Rebuilds the claim's zone from its base with `level` growth steps (0-255). Records the number of steps that actually applied unless you add `force`. |
| `lcbu setradius <r> [x y z]` [HOST] [CHEAT] | Forces a square zone of half-extent `r`; `0` resets to the base zone. Clamped between the vanilla size and the cap. |
| `lcbu poi <x> <z> <r>` [READ] | Dumps every POI around that point and what a square of half-extent `r` would add. |
| `lcbu prune` [DESTRUCTIVE] | Drops registry records that have no matching claim. |
| `lcbu save` | Force-writes the upgrade file (server only). |
| `lcbu load` [CHEAT] | Reloads the upgrade file from the current save, discarding unsaved changes. |
| `lcbu config` [READ] | The parsed config, the wilderness ladder and per-level costs. |
| `lcbu verify` [READ] | Cross-checks the registry, the vanilla claim map and loaded tile entities. Ends in PASS or FAIL. |

Examples: `lcbu near 3`, `lcbu set 2`, `lcbu verify`

---

## Handy vanilla commands while testing

These are the game's own commands (`help <command>` shows the full usage of any of them, including `help sc` and `help lcbu`).

- `dm` (`debugmenu`): debug mode on/off. Needed for F6 spawning and for `buff god`.
- `cm` (`creativemenu`): the creative menu on/off.
- `giveself <item> [quality] [count] [putInInventory] [spawnWithMods]`: gives you an item (quality defaults to 6, count to 1). SC examples: `giveself scHandheldRadio` (the handheld radio), `giveself scBundleMedsMedium 1 1 true` (one medium meds bundle straight into your bag). Bundle names follow `scBundle<Food|Meds|Ammo|Materials|Fuel|Water|Luxury><Small|Medium|Large>`.
- `buff god` / `debuff god`: god mode on/off (turn on `dm` first). Handy when raiders are hostile.
- `settime <day> <hour> <minute>` (alias `st`): moves the clock, for example `settime 5 8 0` for day 5, 8:00. Careful: `settime day` and `settime night` jump to day 1 (noon) and day 2 (midnight), which can undo days of progress; `settime <time>` takes ticks (1000 = one hour).
- `sg <pref> <value>` (`setgamepref`): changes a game setting for this session, for example `sg MaxSpawnedZombies 64`.
- `le` (`listents`): lists all entities with their ids.
- `killall`: kills all enemies (never players). `killall alive` kills **every** living thing except players, your community included.
- `se <playerId> <entity#> [count]` (`spawnentity`): spawns an entity by its number around a player (0 for yourself); `se` alone lists the numbers.
- `tp <x> <y> <z>` or `tp <x> <z>` (`teleport`): moves you; `tp <player name or entity id>` takes you to them.
