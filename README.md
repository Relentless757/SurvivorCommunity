# Survivor Community

**Version 0.1.0-pre.2, for 7 Days to Die 3.4. Single player.** Download it from the
[Releases page](https://github.com/Relentless757/SurvivorCommunity/releases). Console commands for testing are in
[CONSOLE_COMMANDS.md](CONSOLE_COMMANDS.md).

Survivor Community (SC) is a community-building mod in the spirit of State of Decay. The survivors you recruit live in your
land claim. They have skills, traits and a specialty, and they share a morale that depends on how well the base is
supplied. You can station them at home, take them along, send them on supply runs, put them on watch or set them to
craft and work. Out in the world there are survivor and raider enclaves, raider bands and drifters on the roads,
infested buildings, outposts you can set up, and radio contacts to trade with.

This is a test build. Expect bugs, and please play it on a new save made for testing, not one you care about.

---

## Contents

1. [Before you start](#1-before-you-start)
2. [Getting started: your community home](#2-getting-started-your-community-home)
3. [Recruiting survivors](#3-recruiting-survivors)
4. [Your members](#4-your-members)
5. [The stockpile, bundles and the packing station](#5-the-stockpile-bundles-and-the-packing-station)
6. [Talking to members](#6-talking-to-members)
7. [Orders: following, home, crafting and jobs](#7-orders-following-home-crafting-and-jobs)
8. [Supply runs and runners](#8-supply-runs-and-runners)
9. [Defending the base](#9-defending-the-base)
10. [The radio station and the handheld radio](#10-the-radio-station-and-the-handheld-radio)
11. [Outposts](#11-outposts)
12. [Enclaves](#12-enclaves)
13. [Bands on the road and wild raiders](#13-bands-on-the-road-and-wild-raiders)
14. [Infestations](#14-infestations)
15. [NPC weapons, gear and looks](#15-npc-weapons-gear-and-looks)
16. [Voices and subtitles](#16-voices-and-subtitles)
17. [The community window](#17-the-community-window)
18. [Blocks and items SC adds](#18-blocks-and-items-sc-adds)
19. [Included: Upgradable Land Claim Block](#19-included-upgradable-land-claim-block)
20. [Settings](#20-settings)
21. [Known limitations](#21-known-limitations)
22. [Credits](#22-credits)

---

## 1. Before you start

### Requirements

- 7 Days to Die **3.4** (this build is for 3.4 only).
- **0-SCore** 3.4.14 or newer.
- **0-XNPCCore** 3.4.04 with its **Voice Control** 3.4.04 (the Voice Control DLL is included in the 0-XNPCCore folder).
- **Easy Anti-Cheat off** (SC is a Harmony mod).
- **Single player** only for now.

### Installing

1. Install 0-SCore and 0-XNPCCore first.
2. Extract the zip into your 7 Days to Die `Mods` folder, so you end up with `Mods\SurvivorCommunity\ModInfo.xml` and
   `Mods\LandClaimUpgrade\ModInfo.xml`.
3. Start a **new game**.

### Works with (optional)

- **Locked & Loaded:** survivors and raiders use magazines.
- **Sonja's Vanity Vault 3.3:** body paints, face paints and tattoos on survivors and raiders; its skin tones get matching
  voices and hair colors (sections 15 and 16).
- **WalkerSim2:** wild raiders still spawn while WalkerSim2 handles the zombies.
- **IZY Classic** (with its Combat Sandbox): its guns show up in NPC weapon rolls.

### Two rules for your save

- **Use a new save.** SC sets up enclaves, spawns and other world records when a save first starts.
- **Do not remove the mod from a save that used it.** SC adds blocks (radio station, guard tower, watch post, packing
  station), and loading that save without the mod can damage the chunks those blocks are in.

---

## 2. Getting started: your community home

Your community lives inside your land claim. That area is the **home zone**: the claim's normal square, or the whole
grown claim if you use the included Upgradable Land Claim Block (section 19).

- **Starting a community is up to you.** No land claim becomes home by itself: hold **E** on a land claim block you own
  and pick **Start a community here**. That claim is the home from then on. You can move home later: hold **E** on
  another land claim block you own and pick **Set as community home**. (The old way, where your first land claim
  became home on its own, is the setting `AutoHomeFirstClaim`, section 20.)
- The land claim's radial menu (hold **E** on the block) gets these entries:
  - **Community:** opens the community window. It does not start a community.
  - **Start a community here:** only while you have no home yet. Makes this claim the home.
  - **Set as community home:** on your other claims once you have a home (never on the home's own claim). Moves the
    home here.
  - **Linked storage:** one window showing every storage box in the base (section 5). Only once you have a home.
- Nobody will agree to join until you have a home. Until then, a survivor you ask says you need a home for your people
  first, and a message tells you to start a community from a land claim's radial. The radio's survivor calls, outposts,
  watch posts and the Guard Tower wait for a home too.
- If you pick up the home's land claim block, the community keeps its home there but nobody new joins until you pick
  **Set as community home** on another claim of yours (or put a claim back on the same spot).

**A good first hour:**

1. Place a land claim where you want to live, hold **E** on it and pick **Start a community here**.
2. Craft a **Packing Station** (from your backpack) and put it inside the claim.
3. Put a few beds (bedrolls count) inside the claim, one per future member.
4. Find a survivor, ask them to join, and do their favor.
5. Pack some food and water into bundles and deposit them, so your people have something to eat.
6. Later, build a **Radio Station** at a workbench and wire it to power.

The **community window** opens from the **ESC menu** (Community), from the land claim radial, from a member's talk
(*"Let's talk about the community."*) and from the **Community tab**, which sits on its own small plate just left of the
player menu's tab bar (Crafting, Character, Map and so on). See section 17.

---

## 3. Recruiting survivors

### Where you meet survivors

- **In the wild:** SC's own survivors spawn in every biome, in the wilderness, in towns and in city centers, by day and by
  night. While SC is installed, they (and SC's raiders) are the only wild NPCs that turn up; character packs' NPCs that
  are already in your world stay as they are.
- **Radio contacts:** a powered radio station can call for survivors looking for a home (section 10).
- **Enclaves:** groups living in buildings. You can recruit some of their people (section 12).
- **Drifters on the road:** whole bands can come to live with you (section 13).
- **Hired companions:** someone you hired the normal way can be asked to join too. Joining ends the paid hire; members
  are never billed.

### Asking someone to join

Talk to a survivor and pick **"I could use someone like you in my community."** They ask a **favor** first, rolled once
for that survivor:

- **Secure a building.** They name a nearby building full of zombies (within about 300 m) and offer to mark it. Pick
  **"Mark it. I'll take care of it."** and a journal quest marks the sleepers. When the building is clear, the quest's
  next step is to return to the survivor, who waits where you left them. If you already cleared it, pick **"Actually,
  I've already cleared that."**
  - The favor only counts through its own quest. Clearing the building without the quest does nothing.
  - The quest has no rally point, so nothing you build there is ever reset.
- **Bring supplies.** One of: **15 food items**, **5 medical items** or **60 rounds of ammo**. **"Sure, I'll see what I
  can find."** puts a quest in your journal with a marker on the survivor. **"Here are the supplies you asked for."**
  shows whenever you carry enough (right away, if you already have it when they ask).

Once the favor is done they **join for free**:

- If they join **inside your base**, they settle at a post near your land claim block.
- If they join **elsewhere**, they follow you until you walk into the base, then they settle.

Your community holds up to **12 members**. When it is full, nobody else can join.

### Trust

A survivor who is not in your community is **Neutral** toward you, and **Friendly** once their favor is done.

- **Hurt them and they are Hostile for good.** Any hit of yours that lands counts: melee, a shot, a thrown weapon, an
  explosion, running them over. Your turrets and traps do not count, nor does a molotov's fire.
- A hostile survivor attacks you on sight, cannot be talked to, drops any favor they offered and will never join you.
- Your own members cannot be hurt by you (unless you turn on `CommunityFriendlyFire`), nor can hired companions, so a
  stray shot cannot turn them. Traders keep their protection.

### The snapshot card

While you talk to a survivor, a card on the left of the screen shows who they are: name and role, portrait, health,
stamina, mood (a face and a word), their traits, the four skills and their specialty as stars. A stranger's profile is
rolled the first time you talk to them and they bring exactly that profile when they join. Their traits read **????**
until their favor is done.

### Streamer names

For streamers: put your viewers' names in a text file and SC gives them to the new survivors it makes, so your viewers
turn up in the game as people you can meet and recruit. It is off by default.

- **Switch it on:** set `StreamerNames` to `true` in SC's settings (section 20) and restart the game.
- **The file:** `%APPDATA%\7DaysToDie\SurvivorCommunity\StreamerNames.txt`, outside the mod folder, so it survives mod
  updates. SC makes it, with a short explanation at the top, the first time you load a world with the switch on.
  `StreamerNamesFile` can name another file (a full path such as `C:\Stream\viewers.txt`).
- **The format:** one name per line. Add the sex after a comma when it matters: `Sarah, female` or `Mike, male` (`f`
  and `m` work too). A name without one can go to anyone. Lines starting with `#` are notes, and blank lines are skipped.
  A name keeps letters, digits, spaces and `_ - . '` (anything else is dropped) and is cut at 32 characters; the same
  name twice counts once.
- **Who gets them:** each new survivor SC makes takes the next unused name in the file's order (put your favorites first)
  that fits their sex: wild survivors, radio contacts, enclave people (leaders too) and drifter bands. With
  `StreamerNamesFor` you choose `survivors` (the default), `raiders` (SC's wild raiders, raider enclaves and bands, and
  outpost raids; they show as "Name the Raider") or `all`.
- **Once per save:** a name given out is recorded in your save and never given again there, even after that survivor
  dies. The exception: the game keeps only the NPCs you have met, so when an NPC you never met disappears (you leave its
  area, or it despawns), its name goes back on the list for a later survivor. When no fitting name is left, new
  survivors get their normal names.
- **Nobody is renamed:** people already in your world keep their names, and a viewer you recruit keeps the name for life,
  through saves, reloads and the area unloading.
- **Mid-stream edits:** SC reads the file again when it changes, so names you add while you play go to the next new
  survivor. `sc names` in the console (F1) shows the list and how many names are used and left, `sc names test` the next
  names in line.

---

## 4. Your members

### Skills

Every survivor has four core skills, each **1 to 7 stars**:

| Skill | What it does | How it grows |
|---|---|---|
| **Cardio** | +4% speed and +8% max health per star above the first | Time spent following you |
| **Wits** | +15% supply run haul per star above the first | Every day lived, and every supply run |
| **Fighting** | +10% melee damage per star above the first | Kills with a melee weapon |
| **Shooting** | +10% ranged damage per star above the first, and better aim (section 15) | Kills with a ranged weapon |

- Stars need 25, 60, 120, 220, 360 and 550 XP for stars 2 to 7.
- New survivors start better the later in the game you meet them: early survivors start every skill at 1 star, middle
  ones at 2, late ones at 3, with one random skill a star higher. Traits add on top. Starting skills are capped at 4
  stars, except for a rare **standout** (4 to 8% of survivors) who has one skill at 5, 6 or 7 stars.
- When community morale is above **75**, everyone earns XP half again as fast.

### Specialty

Each survivor has one **specialty**, starting at 1 star and growing to 7. Their role shows as their title.

| Specialty | Title | At home, each day | Supply runs | Crafting skills |
|---|---|---|---|---|
| **Medicine** | Medic | Your best medic stretches medical bundles: one bundle heals 1 + (stars x 0.5, rounded down) injuries | | Medical |
| **Cooking** | Cook | Your best cook cuts the community's food use by 8% per star (never below half) | Better food runs | Food |
| **Gardening** | Gardener | Living crops in the base become food: crops x 0.01 x all your gardeners' stars a day | Better water runs | Seeds |
| **Mechanics** | Mechanic | | Better materials and fuel runs | Workstations, vehicles, traps, robotics |
| **Chemistry** | Chemist | | Better medicine runs | Explosives, medical, electrician |
| **Craftsmanship** | Craftsman | Craftsmen pack more efficiently: the packing station needs 4% fewer points per bundle per star of all your craftsmen (never below 60%) | | Armor, backpacks, every weapon kind, tools |

- The daily specialty bonuses only count members who are **stationed at home** (not following you, not on a run).
- A matching specialty adds **+15% per star above the first** to that kind of supply run (section 8).
- Doing the job (cooking, healing, gardening, packing, a matching run, crafting) earns specialty XP.
- How crafting uses the specialty is in section 7.

### Traits

Each survivor has **2 traits**. Their effects show on the NPC as buffs with a description.

| Trait | Effect |
|---|---|
| Eats Like a Bird | Uses a quarter less food |
| Bottomless Stomach | Uses half again as much food |
| Optimist | Community morale +3, own mood +5 |
| Pessimist | Community morale -3, own mood -5 |
| Loner | Own mood -5 |
| People Person | Community morale +2 |
| Veteran | Starts with 2 extra Fighting stars |
| Sharpshooter | Starts with 2 extra Shooting stars |
| Marathon Runner | Starts with 2 extra Cardio stars |
| Quick Study | Earns XP half again as fast |
| Cowardly | Runs from enemies instead of fighting; own mood -5 |
| Hardy | Own mood +3 |
| Medic Training | Specialty is Medicine |
| Homesteader | Specialty is Gardening |
| Tinkerer | Specialty is Mechanics; supply runs bring back 15% more |
| Hoarder | Supply runs bring back 25% more; own mood -2 |
| Cheerful | Community morale +2, own mood +3 |
| Insomniac | Own mood -3 |

### Morale and mood

**Morale** (0 to 100) is the community's shared spirit. A morale icon with the number sits in your buff bar while you
have members, and the community window lists everything helping and hurting it. The icon, the morale bars in the
community window and the Home Status dot are **green** from 50, **amber** below 50 and **red** below 25 (the window's
bars: below `LowMoraleThreshold`, 25 by default), the same steps as the words beside them. It starts at **50** and adds:

| Part | Value |
|---|---|
| Food | From -10 to +20 by days of food in the stockpile (about +4 per day beyond two days); **-20 while hungry** |
| Water | **-20 while thirsty** |
| Medicine | +5 with medical bundles in stock; **-10** with an injured member and none in stock |
| Beds | **+10** when everyone has a bed; **-5 per missing bed** (at most -15) |
| Facilities | +3 each (campfire, workbench, forge, chemistry station, cement mixer), up to +15 |
| Ammo, materials, fuel | +2 each when there is any in stock |
| Traits | The members' trait offsets |
| Recent deaths | **-15 per death**, fading 5 a day (gone after three days) |
| Territory | +2 per producing outpost (up to +6); a lost outpost costs 5, fading a point a day |
| Defended the base | +4 per base attack held (up to +10), fading a point a day; losses from fights you missed |
| A round of drinks | +15 while it lasts (section 4, Activities) |
| Allies, enclaves | See section 12 |
| Infestations near home | See section 14 |

Each member's **mood** is the community's morale plus their own trait offset, **-15 while injured**, **-10 while the
community is hungry** and **-10 while it is thirsty**. Moods read happy, content, uneasy or miserable.

What morale does:

- **Above 75:** everyone earns XP half again as fast, and supply runs bring back 25% more.
- **Below 25 for 2 days in a row:** the unhappiest member threatens to leave. Sort out food, beds and medicine: if morale
  is not back above 25 by the next day's rollover, they leave. The threat is withdrawn the moment morale climbs back.
- **Members never refuse an order because of their mood** (by default). A death's morale hit fades in about three days.

### Daily needs

At each day rollover (whether or not the base is loaded) the community uses supplies from the stockpile:

- **Food:** 0.5 bundle units per member a day (traits and your best cook change it; fractions carry over).
- **Water:** 1 bundle unit per member a day, minus what your dew collectors and wells make.
- **Medical:** one medical unit per injured member, which heals them (a good medic heals several with one).
- **Ammo** has no daily upkeep. It is spent only when a member's gun is refilled at home or at an outpost (section 15).

If the stockpile runs out:

- **Hungry:** morale -20, every mood -10, and every member is 10% slower and does 10% less damage until there is food
  again.
- **Thirsty:** the same again on top of hunger (morale -20, mood -10, 10% slower and weaker).
- **Injured** members (hurt on a supply run, at an outpost, or in a base fight you missed) are 15% slower and do 20% less
  damage until a medical bundle is spent on them.

The day's summary shows as a message and is kept in the community window's **Recent days** list.

### Beds, facilities and crops

SC counts what you build inside the home zone every few seconds:

- **Beds:** every bedroll, bed and bunk bed (and bed mods that work like them), plus bare-frame beds, mattresses and
  hospital beds. A bunk frame with no mattress and a rolled-up mattress do not count. One bed per member.
- **Facilities:** campfire, workbench, forge, chemistry station and cement mixer.
- **Crops:** living planted crops (for your gardeners).
- A part of the claim that is not loaded keeps its last count, so walking away does not lose your beds.

### Water facilities

Inside the home zone, these give the stockpile water every day:

- **Dew collectors** (vanilla): **1 water a day per level**, levels 1 to 5. They still fill their jars as usual.
- **Wells** from a supported well mod (if installed; the `WellBlocks` setting lists them): **2 water a day per level**,
  levels 1 to 5.

Look at one inside your community and press **E** to **Check** it (its level, water a day and the next level's cost).
Hold **E** and pick **Upgrade** to raise its level with stockpile materials: **4, 8, 12 and 16 materials** for levels 2
to 5.

### Activities

The community window's **Activities** tab has one activity, **Prepare a round of drinks**:

- Costs **4 luxuries + 1 food** from the stockpile.
- Gives **+15 morale for 15 minutes** of real play time (the clock stops while the game is paused).
- Once per game day. It is meant for the days after a death.

### Staying home, roaming and defending

- Members at home are **residents**. By day they walk about the whole base, stopping 10 to 20 seconds at each spot. At
  night, or with an enemy within 30 m, they go back to their post.
- Residents **defend the base:** they go after any awake enemy inside the home zone within **22 m** of their post (or of
  themselves while roaming), even one they cannot see from there, fight it and walk back. Sleepers in a base built in a
  POI are left asleep, and Cowardly members stay put.
- Residents keep a leash: they will not chase an enemy far outside the base.
- While you are within **250 m** of the base, the whole home zone stays loaded, so your people are not unloaded and rebuilt
  as you walk around the edge of your view distance.
- **Getting unstuck:** a resident wedged in furniture, a doorway or against a vehicle wreck is moved back to their post.
  It never happens while you are looking at them or their post: they first try another path, and the move waits until
  you look away (at most 60 seconds). A resident who keeps getting stuck is given a new post. Posts and roaming spots are
  always on a real floor, and never in or right beside a wreck.

### Doors

- Members open closed doors on their way (roaming, following, going home, supply runs) and shut them again behind them.
  Double doors and gates work from either side.
- **Locked doors:** a member opens one only if it is your lock (you own it or are on its list). Locked doors in POIs, and
  anyone else's, stay shut.
- **Drawbridges and hatches** are opened and shut again too, so a base whose only way out is a hatch or a bridge does not
  trap its people.

### Missing members, leaving and death

- **Missing:** a member whose body has gone from the world shows as **Missing - Call back** in the community window. Pick
  them and press **Call back** to bring them back to their post. A member nobody has seen for 30 days while the base is
  loaded is written off.
- **Asking someone to leave:** the talk row *"It is time for you to leave the community."* (then *"Yes. Goodbye."*), or
  **Ask to leave** in the community window (press it twice within five seconds). They leave for good.
- **Death is permanent.** It costs morale (section above) and shows in the window's Recent days. What a member carried in
  their inventory drops as a backpack where they die.

---

## 5. The stockpile, bundles and the packing station

The community lives off a shared **stockpile** of seven resources: **food, water, medical, ammo, materials, fuel and
luxuries**. You fill it with **bundles**.

### Bundles

- Every resource comes in **Small, Medium and Large** bundles, worth **1, 2 and 3** stockpile units.
- Bundles stack to 10 and weigh nothing. They cannot be opened, sold or scrapped back into goods.
- You deposit bundles at a **packing station**. You can also carry them to hand to enclaves (section 12).

### The packing station

Craft a **Packing Station** from your backpack (30 wood, 5 forged iron, 2 duct tape). It is a lockable crate with an
8x4 box. Open it and a strip appears under the grid, as wide as the grid. Its buttons carry short names (Food, Water,
Meds, Ammo, Mats, Fuel, Lux); hold the pointer over one to see what it does. Under them, the stockpile's stock and the
last action's message.

- **Pack** (food, water, medical, ammo, materials, fuel, luxuries) turns what is in the box into bundles. Large bundles
  are made first, then one Medium or Small for the rest. The most valuable stacks go first, and anything short of a whole
  unit stays in the box.
- **Take** puts one Small bundle of that resource from the stockpile back in the box, to carry.
- **Deposit bundles** moves every bundle in the box into the stockpile.

What goes into each resource:

| Resource | What packs into it | Points per unit |
|---|---|---|
| Food | Anything tagged as food or drink that is not water or alcohol | 400 |
| Water | Drinks that hydrate and are not alcohol (river and boiled water, tea, coffee, juice, Mega Crush, smoothies) | 200 (about five boiled waters) |
| Medical | Medical items | 300 |
| Ammo | Ammunition | 2000 |
| Materials | Resources (wood and coal count here, even though they burn; empty jars too) | 1000 |
| Fuel | Gasoline | 600 |
| Luxuries | Beer and Grandpa's moonshine, Awesome Sauce and Learning Elixir | 300 (about three beers) |

Points are the items' trade value. Craftsmen in the community lower the points needed (section 4).

**Luxuries** are never used up by the day; they pay for **Activities**. Stews and canned soups hydrate but count as food.

### Where else bundles come from

- **Loot,** now and then, in the containers they belong in: food piles and cupboards; medical piles, medicine cabinets,
  medical coolers and Pop-N-Pills crates; ammo piles, military footlockers, Shotgun Messiah crates and ammo displays;
  Pass-N-Gas crates (fuel); construction crates and toolboxes (materials); liquor piles and beer coolers (luxuries).
  A pile drops a bundle of its own size.
- **Shamway crates** carry food and water bundles more often than anything else. They are the only place water bundles
  are found in loot. **Pop-N-Pills crates** likewise favor medical bundles.
- **Air drops** (from the radio) roll a Medium food, medical, ammo, materials and fuel bundle, each about 1 in 4.
- **Trader quest rewards:** food, medical and ammo rewards can be a bundle (Small at tier 1, Medium at tier 2, Large at
  tier 3); resource rewards can be a materials or fuel bundle.
- **Supply runs, outposts, allied enclaves and the radio's barter** (sections 8, 10, 11, 12).

### Linked storage

**Linked storage** (on the land claim radial, and the **Open storage** button in the community window) opens one loot
window showing every stack in every storage box you placed inside the home zone, as one grid.

- It is a real view, not a copy: taking, splitting, swapping or merging in a cell changes the real box behind it.
- Anything you drop on an empty cell, or shift-click in from your backpack, goes into the **first box with room** for it.
  If no box has room it comes back to you.
- The strip under the grid shows the box count, stacks and free room, and has a **Refresh** button.
- The packing station and the land claim block's own storage are not part of it. Boxes in chunks that are not loaded are
  out of reach (the strip says how many chunks were skipped).

---

## 6. Talking to members

### The talk rows

What you see when you talk to a member depends on where they are and what they are doing. With 0-XNPCCore 3.4.04 the
rows are:

| Member | Rows |
|---|---|
| **Resident at home** (you both inside the base) | Let's talk about the community. / Come with me. / I need you to go find supplies. / Show me your inventory / Craft... / Jobs and gear / It is time for you to leave the community. / Nevermind. |
| **Resident, but you or they are outside the base** | Let's talk about the community. / Come with me. / I need you to go find supplies. / Show me your inventory / It is time for you to leave the community. / Nevermind. |
| **Resident on a work shift** | Let's talk about the community. / Come with me. / That's all for now. / Show me your inventory / Craft... / Jobs and gear / Nevermind. |
| **Guard on watch** | As a resident outside the base (orders are refused until you relieve them at their post) |
| **Follower** | Let's talk about the community. / Go back home. / Come here / Show me your inventory / Craft... / Order wheel / Jobs and gear / It is time for you to leave the community. / Nevermind. |
| **Runner met on the road** | Let's talk about the community. / Head home now. / It is time for you to leave the community. / Nevermind. |
| **Outpost stand-in** | Nevermind. |

- **Jobs and gear** opens 0-XNPCCore's hold-E ring on them, where Weapons, Read, Your progress and Ammo live.
- A runner has no "Show me your inventory": items put in their inventory on the road could be lost.

### The talk link

When you talk to a **resident at home** (both of you inside the base, and they are not on watch), they are linked to you
while the talk or anything you open from it is open: the craft window, their inventory, the order wheel or their hold-E
ring. That is what lets 0-XNPCCore's **Craft...**, **Jobs and gear** and **Show me your inventory** work for a resident.

- Give them a craft order or a job, and the link becomes a **work shift** (section 7).
- Otherwise the link ends quietly **2 seconds** after the last of those windows closes, and they walk back to their post.

### Long talk pages

A talk page with more rows than the dialog window shows (10) splits into pages instead of losing its last rows. This works
for every dialog in the game, not only SC's.

- Roll the **mouse wheel** over the rows, push the gamepad's **right stick**, or click the **arrows** beside the "1/2" at
  the top right of the window.
- **"Nevermind."** stays the last row of every page.
- A page that fits is unchanged.

### What members will not do

- **No trading:** members and stand-ins never offer Trade (by talk, radial or voice). Ask to see their inventory instead.
- **No "Pick me up":** SC's people cannot be picked up as an item from 0-XNPCCore's hold-E ring.
- **No pay:** members are never on a Voice Control contract and are never billed.

---

## 7. Orders: following, home, crafting and jobs

### Follow me and go home

- **"Come with me."** (or **Follow me** in the community window) makes a member follow you like a hired companion. At
  most **2 members** follow you at once (enlisted enclave people count toward that).
- Followers use 0-XNPCCore's own following, its formations and stances. The **order wheel** and Voice Control's spoken
  orders work on them (stay, guard, guard and return, wander, patrol, crouch, formations, stances). They keep holding
  that order while still counting as your follower, until you call them back. A spoken "dismiss" sends them home.
- **"Go back home."** (or **Go home** in the window) stations them at home. They must be inside the base or near their
  post, or you must be standing inside the base (then they are moved to their post). Otherwise you are told to bring them
  to the base first.
- A follower who carries a grenade, pipe bomb or molotov can throw it in a fight.
- Members get the bonuses of your **Charismatic Nature** perk, at home as well as while following.

### Crafting

Members craft through 0-XNPCCore's own crafting (**Craft...** in their talk), and their **specialty stars decide their
crafting skills**:

- Each skill of their specialty is set to the skill's top level x stars / 7. A 7-star cook has Food at the top level, a
  3-star cook about 43%, a 1-star cook about 14%.
- Every other crafting skill is at level 1 (the recipes a fresh player has).
- Schematics teach them recipes the normal 0-XNPCCore way.
- Each craft of a recipe from their specialty's skills earns **0.5 specialty XP**.
- 0-XNPCCore's own learning-by-crafting and its NPC XP are off for members; they grow only through SC's skills.

| Specialty | Crafting skills | Starting roles |
|---|---|---|
| Cooking | Food | Cook |
| Medicine | Medical | Cook, Chemist |
| Gardening | Seeds | none |
| Chemistry | Explosives, Medical, Electrician | Chemist, Crafter |
| Mechanics | Workstations, Vehicles, Traps, Robotics | Blacksmith, Crafter, Chemist |
| Craftsmanship | Armor, Backpacks, Blades, Bows, Clubs, Handguns, Harvesting tools, Knuckles, Machine guns, Repair tools, Rifles, Salvage tools, Shotguns, Sledgehammers, Spears | Crafter |

**Roles** (Cook, Blacksmith, Chemist, Crafter) decide which stations a member may use. A member starts with their
specialty's roles; after that, your changes on the order wheel's Roles page, by voice, or in the community window's
**Work and roles** row are kept.

**Crafting magazines are training.** A crafting magazine for one of their specialty's skills gives **10 specialty XP per
level** it would have granted (so 55 magazines take a member from 1 to 7 stars, before bonuses). Magazines for other
skills, and all magazines once their specialty has 7 stars, show as "won't help her" on their Read page and are never
used up. Reading is under **Jobs and gear**.

### Work shifts (residents crafting at home)

A resident can craft at home on a **work shift**:

- **From the talk:** talk to them inside the base (stand at the station you want them to use) and pick **Craft...**.
  The station tabs list the stations within 5 m of you, or their usual home station within 60 m of them. A craft order
  starts the shift.
- **From the community window:** select them, open **Work and roles**, and press **Call to work**. They walk over to you.
- What they make on a shift moves from their inventory into the **home crates** as they work. When the crates are full,
  it stays in their inventory and you are told.
- The shift ends **90 seconds** after their last job (at once at night), **3 minutes** after a call with no order, when
  you say **"That's all for now."**, or when you press **End work**. They walk back to their post.
- The craft window's Cancel cancels the job, not the whole shift.
- On a shift a resident still counts as at home: they keep their daily specialty bonuses and still defend the base.

### Home jobs and the work kit

Residents also do 0-XNPCCore's **home jobs**, picked from **Jobs and gear** (the Jobs page) during the talk:

- **Allowed for residents:** harvesting wood, picking plants and crops, Butcher, Base Repair, Combat Repair, and Craft.
- **Followers only:** Mine (and clay), Loot here, and Follow + loot. A resident asked to do one of these says to take
  them along instead.

How home jobs work:

- **The work kit.** Every member carries a stone axe, a stone shovel and a bone knife beside their weapons, so the jobs
  always find a tool. **They never fight with the kit.** If a zombie reaches them mid-job, their weapon goes back in their
  hand. A better tool you hand over through **Show me your inventory** is used first.
- **Output into the crates.** What a resident gathers (wood, crops, meat and so on) moves into the home crates as they
  work. When their pack fills, it is emptied into the crates and they carry on.
- **Replanting.** A member who picks a ripe crop on your farm replants the plot with a seed of the same crop, from their
  inventory or the home crates. With no seed, the plot stays empty.
- **Repair duties.** Base Repair and Combat Repair need a repair tool (stone axe, claw hammer or nail gun) and the
  materials in an unlocked chest nearby or in their inventory. When the duty starts they tell you what is missing. A duty
  ends after **5 minutes** with nothing they can repair. Base Repair also ends at night; Combat Repair never ends at night
  and keeps going while a foe is near.

**Squad orders never reach residents:** the order wheel's roster and spoken squad orders only move followers.

---

## 8. Supply runs and runners

### Sending someone out

Pick a resource from the talk row **"I need you to go find supplies."** or from **Send on supply run** in the community
window: **Food, Water, Medical, Ammo, Materials or Fuel**. (Nobody goes out just for liquor.)

- Only a resident at home who is not injured and not on watch can go, and they must be loaded (be near the base).
- At most **3 runners** are out at once.
- Followers are not offered a supply run.

### Physical runs

Runners do not vanish: they **walk out of the base** to a real building that has what they were sent for, search it, and
walk back.

- **The destination** is **150 to 700 m** from home: a site the radio scouted for that resource first, then a building
  whose type produces it, then any scavenging site. Never an outpost, an enclave's building, or a building another task
  already uses.
- **The trip:** they run there, spend **6 game hours** searching the site, run home and walk the last stretch to their
  post. The community window shows each leg ("Walking to...", "Searching...", "On the way home...", "At the gate").
- **Near you** they are a real NPC: you can follow them, escort them, talk to them (**"Head home now."**) or watch them
  fight. **Far away** they are carried along their route on the game clock, and come back as the same person when you get
  near.
- **The haul** counts the moment they reach the edge of the base.
- With no building in range, a runner goes on an old-style **hidden run**: gone for a day, back at the next rollover.

### What they bring back

- **2 bundle units** to start with.
- **+15% per Wits star** above the first.
- **+15% per specialty star above the first** for the matching job: Mechanics for materials and fuel, Chemistry for
  medical, Cooking for food, Gardening for water.
- Trait bonuses (Tinkerer, Hoarder), **x1.25** when morale is above 75, and an outpost perk (section 11).

### Risk

- A run's dice are rolled only while the runner is **out of your sight**. Escort them, and real zombies decide instead.
- The base risk is **15%**, minus 2% per Fighting and Shooting star above the first.
- A bad roll means they come back **injured with half the haul**, or (a quarter of bad rolls) **never come back**: a
  death like any other.
- A runner who dies out of sight loses what was in their inventory, and you are told what was lost.
- Runs earn Wits XP, and specialty XP for the matching job.

### Runners with a handheld radio

Put a **Handheld Radio** (section 10) in a member's inventory before a run, and:

- **They talk to you on the way:** setting out, arriving, a good or a poor haul, trouble, heading back, almost home. Each
  line plays once a run, in their own voice through a radio filter, with the words as a message. You hear them only while
  you carry a handheld radio yourself, and not while you are within 30 m of them (then they would just talk).
- **They call when attacked:** a runner who is hurt by a raider or a zombie radios for help once a run. You get a message
  saying where they are, and a map marker follows them until the fight is over. Nothing spawns: you can go and help.
- **Distress calls:** when they reach the site, a runner may be pinned down there (35%), radioing for help. A journal
  quest marks the building. Get within **70 m** and zombies close in on them (more for a veteran: about 4 plus a share of
  your gamestage, at most 24). Clear them and the run comes home with **25% more**. Leave them for 12 game hours and the
  run ends the way bad runs do.
  - A runner rolls for distress only while carrying a radio, and the call is heard only while you carry a handheld or a
    radio station at home has power.
  - A runner you are escorting (within 70 m on arrival) does not roll for distress.
- **Calling them home:** you can turn a runner around with **Call home** in the radio window's Supply runs section, or
  with **Recall home** in the community window. Both need the runner to carry a radio (or you to be with them). Called
  over the radio, they answer "Copy that." A runner turned around mid-search brings home a share of what they found.

---

## 9. Defending the base

### The Home Status panel

While you stand inside the home zone, a **Home Status** panel shows at the top of the screen, right of the compass:

- morale as a colored dot and a word (happy, content, uneasy, miserable): green while content or happy, amber while
  uneasy, red while miserable;
- food, water, medical, ammo, materials and fuel in the stockpile, each **red when it is below what the next day's rollover
  needs**;
- the member count;
- the base's **noise**.

It hides while a menu is open, and with the HUD (F7).

### Noise and attention

Your base makes **noise**, and noise draws the dead's **attention**.

- **Steady noise:** 2 per member living at home, 6 per running generator in the zone, 1 per workstation.
- **Event noise** that halves every game hour: anything that makes heat inside the zone (a burning forge or campfire,
  crafting, torches, your gunfire, explosions, broken blocks) and your people's gunfire. Bows are silent.
- Levels: **Quiet**, **Noticeable** from 15, **Loud** from 30, **Very loud** from 50.
- **Attention** (0 to 100%) grows each game hour with noise above 15 and fades 2 an hour. It gains nothing during a blood
  moon or while an attack is open. The panel shows it once it is above 0: "Noise: Loud (42%)".
- Each **manned watch post** cuts attention gain by 15% (at most 45%). Infestations near home add attention (section 14).

### Base attacks

When attention reaches **100%** while you are home, a **horde is called**.

- **No warning by itself:** only a guard on watch sees it coming (below).
- It arrives **1 to 2 game hours** later, from one side, spawning one zombie a second 25 to 50 m outside the zone, never
  within 30 m of you.
- **Size:** about 8 zombies early, 14 at gamestage 50, 22 at gamestage 100 (at most 40), plus 5% per resident. The mix is
  vanilla's screamer-horde list for your gamestage, so ferals and radiated come when they would there.
- **Never** within 2 days of the last attack's end, from 10:00 on blood moon day until the blood moon ends at dawn, or
  while an outpost attack is being fought. A full meter waits at 100% until a horde can be called.
- **Your residents defend the whole base** during an attack: any enemy in the zone or up to 10 m outside it. Cowardly
  members still run.
- **Held** (every attacker dead): a message with what the fight cost the stockpile, and **+4 morale** ("Defended the base",
  at most +10, fading a point a day).
- **Leaving mid-fight** (more than 150 m from the zone) hands the rest of the fight to your people (see "Attacks while
  you are away" below). A reload mid-fight brings back only the attackers still standing.
- A fight that stalls (4 game hours with no kill in the last hour) ends; stragglers become ordinary zombies and it counts
  as held.
- Base attacks do not damage your blocks beyond what the zombies themselves break.

### Attacks while you are away

A horde can also be called while you are away (one per absence).

- **Within 100 m** of the claim when it arrives, you fight it live.
- **Farther away,** your people fight it alone for 2 game hours plus 1 per 10 zombies. Come within 100 m before it ends
  and the zombies they have not dealt with yet are at the walls. Leave a live fight and your people fight the rest alone.
- **The result** of a fight you missed is a roll against the **defense rating**: **held**, **damaged** or **overrun**.
  - It uses ammo bundles (1 per 10 zombies, half again when overrun).
  - Damaged: 1 or 2 residents hurt. Overrun: about two thirds hurt. Each hurt resident uses a medical bundle at once, or
    stays injured.
  - **Anyone at home can die:** about 8% for someone hurt against an average horde, more against big hordes or for novice
    fighters, less for veterans, at most 50%. Rarely, even an unhurt resident.
  - Morale **+2** held, **-5** damaged, **-12** overrun (fading a point a day), plus any death's penalty.
  - "Damaged" and "overrun" cost ammo, medicine, people and morale only, not blocks.
- **With a powered radio** you are told when the horde is called, when it hits and how it ended. Without one, you hear
  when you get home.
- **Blood moon siege:** a blood moon that passes while you were never within 100 m of the claim is settled the same way,
  15 below the defense rating.

**The defense rating** starts at **20**, and adds: +4 per fit resident; +1 per Fighting and Shooting star above 2; **+10
per manned watch post** (at most +30, only with ammo in stock); +1 per trap or turret in the zone (at most +20). It loses
15 with no ammo, gains or loses 5 for morale above 75 or below 25, and loses 1 per attacker over 20 (at most -15).

### Watch posts

A **Watch Post** is a block a guard stands on.

- **Craft** it from your backpack: 10 wood and 4 nails. It comes in every shape (hold Reload on it for the shape menu) and
  can be painted to match your floor. The guard stands **on top** of it, so use the cube, or a plate or catwalk turned
  upside down in a deck. Other shapes register but say a guard cannot stand on them.
- **Place it inside the home zone**, up to **4** posts, with two blocks of air above it. A tower top is best. It faces the
  way you look when you place it.
- **Assign a guard:** hold **E** on the post and pick **Assign guard**. The community window opens on your residents; pick
  one. **Relieve guard** ends the watch early.
- **A shift** lasts **12 game hours** and takes **3 ammo bundles** from the stockpile at its start (no shift with fewer).
  That ammo pays for the guard's refills first. After a full shift the guard **rests 12 hours**.
- **On watch** the guard stays on the post: no roaming, no chasing, no fleeing, weapon always up. They face the post's
  way, **see 50% farther**, and shoot whatever comes into range. If they have not reached the post about 30 seconds after
  setting off, they are placed on it while you are not looking.
- **Call-outs:** while you are at the base, a guard on watch calls out a horde when it is called ("a horde is gathering to
  the north!") and when they first see it, and an orange skull marker shows its side on the compass and map.
- A guard on watch cannot follow you, go on a supply run or be posted to an outpost until relieved. A guard who is hurt
  with no medicine comes down. Picking up, breaking or building over a manned post ends the watch.

### Guard Tower

The **Guard Tower** kit puts up a wooden watch tower with a Watch Post on its deck in one go.

- **Craft** it from your backpack: **782 wood and 4 nails** (the tower's blocks' worth).
- **Placing:** hold the kit and aim at the ground. A ghost of the whole tower follows your aim; **R** turns it. The ladder
  comes toward you and the guard looks the way you are looking, so stand inside the base facing out. The ghost turns red
  where the tower cannot go, and pressing tells you why.
- **Where:** wholly inside the home zone, with room for one more watch post, on fairly level ground, clear of anything
  built, crops, trees, rocks, water and people. Its legs reach down up to 3 blocks over a dip, and terrain up to 2 layers
  inside it is cut away.
- Once up it is ordinary blocks: repair, upgrade or break them like anything you built. Hold **E** on its Watch Post to
  assign a guard.
- If the spot stopped fitting by the time the tower goes up, the kit comes back to your inventory with the reason.

---

## 10. The radio station and the handheld radio

### The radio station

Craft a **Radio Station** at a **workbench** (15 forged iron, 6 electrical parts, 4 mechanical parts, 10 polymer scrap).
It is a powered block: **wire it like a light**. Hold **E** and pick **Use the radio** while it has power.

The radio window has these sections:

- **Request an air drop.** **With SC installed, vanilla's scheduled air drops never come**: the radio requests one
  instead. The sandbox **air drop frequency** is its cooldown. Turn that setting off and the radio cannot call a plane.
- **Call for survivors.** One call every **2 days**. A survivor looking for a home answers from a real building **300 to
  1500 m** from home and is marked on your map. They wait **7 days**, and at most 3 contacts are out at once. When you
  get near, they are standing in the building with a favor to ask, like any recruit.
- **Supply runs on the radio.** Each runner, where they are, and **Call home** (section 8). Distress calls are listed under
  it.
- **Outposts reporting in.** Every outpost and what it sends (section 11).
- **Scout.** Once a day, listen for chatter about a resource (food, medical, ammo, materials or fuel): the **3 nearest
  sites** within 1500 m are marked on your map for 3 days, and supply runners go there first.
- **Barter.** Once a day, trade **3 bundles of one resource for 1 of another** from the stockpile (food, water, medical,
  ammo, materials, fuel; not luxuries).
- **Raider demands** on your outposts, with **Pay** and **Refuse** (section 11).
- **Enclaves** (right-hand column): the enclaves you know, nearest first, with their standing, ally benefit and where they
  are; their open requests and when they are due; the **Allies** panel (ally barter, Send a fighter, Supply drop); and
  **Siege calls** with **On our way** and **We can't come** (section 12).

### The handheld radio

The **Handheld Radio** works by being carried anywhere in your backpack or toolbelt. It has no use action.

- **How to get one:** craft it at a **workbench** (3 electrical parts, 2 mechanical parts, 5 polymer scrap, 2 duct tape),
  find one now and then in electrical loot (Working Stiffs crates, garages, Mo' Power shelves and crates, construction
  crates), buy one when a trader stocks it, or take one from a dead raider (they rarely carry one).
- **The Radio tab:** while you carry one, a **Radio** tab shows left of the Community tab. It opens the radio station's
  window from anywhere, **relayed through your home station, which must have power**. Without power the window opens,
  says the home station is offline, and grays out the station's controls. The lists stay readable, and runners who carry a
  radio can still be called home (radio to radio).
- **For runners:** see section 8.

---

## 11. Outposts

Hold cleared buildings away from home and they send supplies to the community every day. Outposts keep working while
both the outpost and your home are unloaded.

### Establishing an outpost

1. Clear a building at least **150 m** from home.
2. Place a land claim inside it and pick **Establish outpost** on the claim's radial, or place an **Outpost Flag**, which
   establishes itself when placed.
3. It needs the **home radio station powered** and a fee of **2 materials and 1 fuel** from the stockpile.

You can hold **1 outpost, plus 1 per 4 members**, at most **4**. Outposts over the limit go idle; nothing is destroyed.
Your home and outposts are never switched off by the land claim limit; to place another claim, disband an outpost.

### What an outpost makes

What it makes depends on the building:

| Building | Sends home |
|---|---|
| Groceries, diners, farms | Food |
| Clinics, hospitals, pharmacies | Medical |
| Army sites, gun stores, police | Ammo |
| Fire stations | Medical and fuel |
| Hardware stores, industry, junkyards | Materials |
| Gas stations, refineries | Fuel |
| Water works and water towers | Water |
| Anything else | Scavenging: a little of whatever you are shortest of |

- **Every rollover** each outpost adds its yield to the stockpile, scaled by its tier, its level, the people posted there,
  what you built inside it (beds, facilities, crops) and its **mode** (quiet, normal, push).
- **Upkeep** costs materials and **hauling** costs fuel. Unpaid upkeep halves the yield; unpaid hauling cuts it to three
  quarters.
- Without power at the home radio, bundles wait at the outpost (up to 3 days' worth) and arrive once it reports in.
- More outposts of the same kind give less each.

### At the outpost (the claim's radial)

- **Outpost** opens the community window on the Outposts tab.
- **Linked storage** radios home and opens the home storage right there.
- **Outpost storage** links that building's own boxes.
- The claim's box has the packing strip.
- **Disband outpost** needs two presses. The claim stays yours.

### People, levels and perks

- **Post members** at an outpost from the Outposts tab (**Assign**): one post, and a second at level 2. Posted members are
  away from home like runners, still eat, and add yield (more when their specialty fits). While you visit, their
  stand-ins guard the claim. If a stand-in dies there, that member died.
- **Levels:** level 2 costs 4 materials and 2 fuel (held 3 days first), level 3 costs 8 materials and 4 fuel (held 7
  days, and someone must be posted there).
- **Modes:** quiet halves the yield and the risk; push gives half again as much at a higher risk.
- **Perks,** one per kind of producing outpost:
  - **Canteen** (food): hunger hurts morale less.
  - **Field clinic** (medical): one injury healed free each day.
  - **Armed escort** (ammo): supply runs are safer.
  - **Salvage yard** (materials): outpost fees and upgrades cost less.
  - **Motor pool** (fuel): supply runs bring back more.
  - **Cistern** (water): thirst hurts morale less.
- **Workshops:** posted crafters and chemists turn surplus materials and fuel into ammo and medical.
- Staffed outposts sometimes draw survivors in, and runners in distress sometimes hole up at an outpost.
- **Track on map** puts the outpost on your compass.

### Protection

- Sleepers never respawn in an outpost's building, and trader quests and rally resets pass it by.
- If its land claim goes, the outpost is **lost**, with a morale penalty that fades.

### Risk (on in this build)

- Each outpost has a **security** rating from its staff, its tier, your visits, ammo in stock, the defenses you built there
  (spikes, wire, traps, turrets, mines) and fights won.
- **Attack calls** give you **10 game hours** to get there and fight. An attack you do not attend is settled on security:
  **held**, **damaged** (no yield for 2 days, or repair it for materials) or **overrun** (the sleepers are back and the
  staff come home hurt; nobody dies in a fight you did not attend).
- **Blood moons** roll every outpost you were not standing at.
- **Neglected** outposts get squatters: something moves into the back rooms, and you must clear it again.
- **Raider demands:** past gamestage 30, the nearest hostile enclave within 2 km may demand **2 ammo and 2 food** for an
  outpost by the next day. **Pay** on the radio and they leave it alone for a few days. **Refuse** (or ignore it) and they
  raid it with their own people (section 12).
- **Good news:** now and then an outpost finds a cache too big to carry, and bundles wait in the claim's box.

---

## 12. Enclaves

You are not the only survivors. Other groups have holed up in buildings across the map: families, drifters, preppers and
raiders. Each group is an **enclave** with a name and a building of its own.

### Finding them

- There are about **0.3 enclaves per square kilometer** (at least 4, at most 36), about a quarter of them **raiders**.
  Up to 2 friendly ones may be close to where you start.
- Come within **150 m** of one to find it: "You've found The X, holed up at Y (Neutral)."
- A **powered radio** picks one up every 3 days (the nearest unknown within 2 km), and allies tell you of others.
- **Markers:** a gray fortress for a neutral enclave, green for Friendly, blue for Allied (also on the compass within 600
  m), a red skull for raiders and hostile groups (on the compass within 300 m).
- **Their building is theirs:** no zombies, no land claims, no outposts, no trader quests there. Its loot containers
  belong to them ("That belongs to <name>.").
- **Their doors are open to visitors:** every locked door of their building (doors, double doors, gates, hatches, garage
  and vault doors) is unlocked when you come near, raiders' too, so you can walk in and find them. The doors are not
  opened and do not become yours (you cannot lock them), and they stay unlocked if the group falls or moves. Their
  containers stay theirs. `EnclaveDoorsUnlocked` (section 20) turns this off.
- SC's own picks (radio contacts, favors, supply runs, scouting) never send anyone there.

### Their people

- A **leader** and 3 to 5 others (3 to 6 for raiders), made the first time you come near and kept from then on: the same
  faces, names, weapons and skills on every visit.
- They appear within about 150 m of their building and go when nobody is near. The leader stays at their spot all day, so
  you always know where to find them.
- They defend their building and its grounds.
- Deaths are real. When the leader dies, the strongest of the rest takes over. When the last of them dies, the enclave is
  gone and its building is free again.
- **Each group has a kind** from its building: growers (farms), medics (clinics), militia (military, police, gun stores),
  builders (industry, hardware), a road crew (gas stations, refineries), or scavengers.

### Standing

Standing goes from -30 to 100: **Neutral** below 30, **Friendly** from 30, **Allied** from 70. It moves with how you
answer their requests, with gifts, and with how you treat their people.

- **Hitting one of them** is a warning: they shout "Watch it!" and standing drops by 10.
- **Another hit within 2 minutes, or a death** (by you or your companion) turns the **whole enclave hostile for good**.
- **Threats:** ask the leader **"Hand over your supplies."** and confirm with **"I'm not asking."** They hand over 2 to 4
  medium bundles of what they live on, then turn hostile for good a few seconds later. **"Forget it."** takes it back.
- A **hostile enclave** shoots you on sight and will not talk. Your residents, followers and turrets fight its people.
- **Raider enclaves** attack on sight, have nothing to say, and stay home (except to raid your outposts).

### Talking to them

**The leader:** *"What do you need?"* (their requests), *"I brought you something."* (gifts), *"Let's see what you have to
trade."*, *"Tell me about this place."*, *"Would your people join ours?"* (Allied, see below) and *"Hand over your
supplies."*

**The others:** *"Where's your leader?"*, *"Can you come with me?"* (enlist) and *"Would you join us?"* (recruit).

### Trade

The leader of a group that is not hostile is a real trader (the talk row, or Trade on their radial). What they sell
depends on their kind: growers food and seeds, medics medicine, militia guns and ammo, builders tools and materials, a
road crew fuel and vehicle parts, scavengers a bit of everything. Each enclave keeps its own stock, restocked every 3 days.

| Standing | Prices |
|---|---|
| Neutral | Normal |
| Friendly | They pay you 20% more for what you sell |
| Allied | They charge you 20% less and pay 35% more |

### Enlist and recruit

- **Enlist** (*"Can you come with me?"*): they follow and fight for **2 days**, for **500 dukes** at Neutral, **250** at
  Friendly, **free** at Allied. One per enclave at a time, and they count toward your 2 followers. **"Go home now."** sends
  them back. If they die with you, standing drops by 5.
- **Recruit** (*"Would you join us?"*): at **Neutral** they want a favor first and joining costs 10 standing; at
  **Friendly** no favor, 10 standing; at **Allied** no favor and no cost. An enclave always keeps at least 2 people and
  lets one go every 7 days. They join with the same face, name, weapons and skills.

### Requests

At each day's rollover, enclaves you have found may ask for help (35% at Neutral, 45% at Friendly, 20% at Allied). At most
**3 requests are open** across the map, and at most **3 new calls for help come in a day**. With a powered radio you hear
"The radio crackles: The X is asking for help."

What they ask for, and what it is worth:

| Request | You have | Standing gained |
|---|---|---|
| **Supplies:** a Small, Medium or Large amount of a resource they do not make (packed bundles, any sizes that add up) | 4 days once you agree | +10 |
| **A gun:** any firearm or launcher (not a bow or crossbow). The better the gun, the more it is worth | 4 days | +3 to +30 |
| **A generator** (from day 10): any item that places a generator | 4 days | +15 |
| **A missing survivor:** one of theirs went out and never came back | 3 days | +15 brought home alive, +8 if you report them dead |
| **An infestation** near them to clear | 3 days | +15 |
| **A siege** (separate, about once a week; needs a powered radio) | 15 minutes | +25 |

- An offer waits **3 days** for your answer. **"Leave it with me."** agrees and adds a journal quest with a marker.
  **"I have it right here."** hands it over on the spot. **"Take one of these."** picks a gun to give. **"No, I can't help
  with that."** declines. **"Let me think about it."** leaves the offer open.
- **Letting them down** costs standing: declining (-5 / -5 / -12 at Neutral / Friendly / Allied), agreeing then failing
  (-8 / -8 / -15), or never answering (0 / -3 / -15). Letting a group down never makes it hostile, but an Allied group let
  down drops back to Friendly.
- **A missing survivor:** they tell you who and where. The first time you get near, it is decided whether they are still
  alive (80% on the day you agree, less each day after). Alive: they hide inside; talk to them (*"Your people sent me.
  Let's get you home."*) and lead them home. Dead: search their remains, then tell the leader.
- **An infestation** is done the moment it is cleared, wherever you are.
- **A siege:** "They're coming over the walls! Please, hurry!" You have **15 minutes** to get within 70 m. Then the horde
  comes over their walls from the far side, and their people fight beside you. Kill every one to win. Too late, or all of
  their people dead, and the enclave falls: its building becomes an infestation. In the radio window, **On our way** tells
  them you are coming, and **We can't come** (press twice) gives them up.

### Gifts

**"I brought you something."** lists up to four bundles you carry. Pick one and they get it: **+3 standing**, once per
enclave a day. Gifts can win a group up to Friendly (standing 69) but not to Allied: an alliance still takes their
requests.

### Allies

An enclave at **standing 70 or more** is **Allied**, and does something for your community every day:

| Kind | Benefit |
|---|---|
| Growers | **Share of the Harvest:** +1 food a day |
| Medics | **Open Pharmacy:** +1 medicine a day |
| Militia | **Munitions Manufacture:** +1 ammo a day |
| Builders | **Construction Partners:** +1 building materials a day |
| Road crew | **Filtration Service:** +1 fuel a day |
| Scavengers (one of these, rolled once) | **Comfort in Numbers** (+5 morale), **Overflow Barracks** (+3 beds), **Fight Club**, **Gun Safety Club**, **Jogging Buddies** or **Scavenging Partners** (double Fighting, Shooting, Cardio or Wits XP), **Training and Advice** (half again as much XP in everything), or **Neighborhood Watch** (no new infestations within 500 m of them, and nearby ones spread half as often) |

- Allies also lift morale: **+2 per ally** (at most +6). Losing a Friendly or Allied enclave costs **5 morale**, fading a
  point a day.
- Allies tell you about the enclave nearest to them that you do not know, when you become allies and every 7 days after.
- **Ally barter** (radio window, Allies panel): pick an ally, **Give** what you send from the stockpile, **Get** what they
  make, and **Trade** swaps **2 of yours for 1 of theirs**, once a day per ally.
- **Radio calls** (Allies panel), each ally once every 3 days:
  - **Send a fighter:** about half a game hour later, one of their best people turns up near you and follows and fights
    with you for **12 game hours**, then heads home.
  - **Supply drop:** about 20 game minutes later, a crate falls by parachute 20 to 50 m from you, holding what they make
    and a medium bundle of their resource.

### Raider enclaves raid your outposts

The raiders who demand supplies for an outpost are a real hostile enclave within 2 km of it. Refuse, and **their own
people** come to the outpost when you arrive (at most 4). Those you kill stay dead, and a group that runs out of people
stops raiding. Your home is never raided by them.

### Enclaves move

A group that has **lost half its people**, or has had an **infestation next door** for days, may move to another building
300 to 1500 m away. A Friendly or Allied group tells you where they went over the radio; anyone else just drops off your
map until you find them again. Their old building is free again. A new group moves into the area **5 days after** any
group is lost.

### A whole enclave joins

The leader of an **Allied** group at **standing 90 or more** asks **"Would your people join ours?"** when your community
has room for all of them (counting the 12-member limit). Say **"Welcome to the community."** and:

- Everyone joins with the same faces, weapons and skills, and each brings **2 units** of the group's resource.
- They make their own way to you. Each turns up at home from **6 game hours** later, while you are there. Until then the
  community window lists them as **On the way**: they already eat and need a bed, but take no orders.
- Their building is free: claim it for an outpost before the dead return.

---

## 13. Bands on the road and wild raiders

### Bands on the road

Now and then a few people walk past your part of the map: **drifters** looking for somewhere to live, or a **raider**
crew.

- Each day there is a 50% chance a new band sets out, while fewer than 3 are on the road. About a third are raiders. A band
  is **2 to 4 people** walking between two real buildings, on a route that passes within 600 m of your home (never
  through it). They walk by day and camp at night (22:00 to 06:00).
- You are told when you meet them ("Drifters on the road, heading for Miller Farmhouse." or "Raiders on the road... they
  shoot on sight!"), and a map marker follows them: gray for drifters, red for raiders.
- **Drifters** talk: *"Where are you headed?"*, *"Stay safe."*, and **"Come to our place."** when you have a home with room
  for all of them. A band that trusts you says yes; one that does not yet says yes 60% of the time (one roll per band).
  Those who join make their own way to your home, like a joining enclave.
- **Trouble:** about a third of drifter bands have zombies on them when you meet them. Kill one of those zombies and the
  band trusts you.
- **Hurt one of a drifter band** and the whole band turns on you for good.
- **Raider bands** attack on sight: once one sees you within 60 m, the whole crew comes for you until you are 180 m away.
  They never go for your outposts or your home.
- Deaths are real: a dead band member stays dead, with their loot.

### Wild raiders

SC's **raiders** spawn in the wild in every biome, as SC's survivors do, and also make up raider enclaves and raider bands.

- They are hostile to you, your members and zombies, attack on sight, and cannot be talked to or recruited.
- They are built on 0-XNPCCore's bandits: each carries one weapon rolled by tier (early, middle, late), a melee weapon
  for close range, and sometimes a throwable (a molotov early; pipe bombs and grenades later).
- They open unlocked doors; locked doors stop them.
- **Your turrets shoot them:** the powered auto turret with "target zombies" on, and the junk turret and junk sledge with
  "target enemies" on. A motion sensor set to "target zombies" trips on them.
- A dead raider drops a loot bag by tier, and now and then a handheld radio in a backpack of its own.
- Killing one pays more XP the better their weapon.
- **With WalkerSim2:** wild raiders still spawn while WalkerSim2 handles the zombies.

---

## 14. Infestations

Some buildings get overrun. An **infested** building holds about **twice the zombies**, more of them **feral**, and
**something worse** in one of its rooms, until you clear it.

- **When:** each day there is a 40% chance one more building becomes infested (at most about 0.2 per square kilometer at
  once). Buildings 300 to 2500 m from home are far likelier.
- **Getting worse:** the tier rises with the days (tier 1-2 in the first week, up to 3-5 from day 29). An infestation 3
  days old or more may **spread** to the nearest building within 300 m.
- **Inside:** about twice the zombies (1.5 times in tier 4-5 buildings), 35% of them feral at any gamestage, and one
  special: a radiated zombie early on, later a charged or infernal one. A building you already cleared becomes dangerous
  again: its rooms respawn the next time you come near them.
- **Finding them:** come within 150 m ("Infested: X. More of them, ferals, and something worse."), or a powered radio
  reports new ones within 2 km of home. Known infestations show as a purple marker on the map and the compass.
- **Clearing:** clear every sleeper room and the infestation ends: "You've cleared the infestation at X."
- **Near home:** an infestation within **500 m** of your land claim block is spotted by your people at once. Each one costs
  morale by distance (5 within 150 m, 3 to 300 m, 2 to 500 m; at most 15 in all) and draws the dead's attention to your
  base (+1 an hour each). Clear it and both go.
- An ally's **Neighborhood Watch** keeps new infestations away from them. A building lost to a siege becomes infested.

### Sleeper rooms and the zombie cap

In any building, a sleeper room that spawned nothing because too many zombies were already about (the game's zombie cap)
is no longer marked cleared for good: it is due again about a game hour later and spawns the next time you come near.

---

## 15. NPC weapons, gear and looks

SC's survivors and raiders wear the **player's body**: random faces, hair and eye colors from the game's character
catalog, and real armor pieces in their equipment slots.

### Weapons

- **Survivors carry two weapons:** one melee and one ranged. About 15% carry only a melee weapon. Ranged weapons are
  mostly bows and crossbows; firearms are rarer early and common late.
- **Better gear later:** the time and place a survivor turns up, and your gamestage, decide their tier. Weapons and armor
  follow that tier.
- They use the gun while it has ammo and switch to melee up close. **When several enemies are on them**, they keep the gun
  and back away as they shoot instead of switching to melee.
- **Shooting skill aims the gun:** they hit nearly every shot within 5 m per Shooting star (5 m at 1 star, 35 m at 7),
  fewer beyond, and aim at the chest.
- They swing melee weapons as fast as the weapon allows, at the weapon's real reach.
- **Your people hold fire** while you or another friendly is in their line of fire at close range.
- **Out of danger** they lower their arms and put their weapon out of sight.

### Ammo

- A new survivor starts with **30 rounds** of their gun's basic ammo (or **20 arrows or bolts**). What they shoot is gone.
- **Refills come from the stockpile, only at home or at an outpost:** a member whose gun is down to fewer than **10
  rounds** while inside the base or at one of your outposts gets **30 rounds** (or 20 arrows), at most once a minute. The
  stockpile pays in packing points (one ammo bundle is about 220 9 mm rounds or 165 stone arrows).
- Out in the world nobody refills them: once dry they fight with their melee weapon until you bring them home or hand them
  ammo through **Show me your inventory**.
- Members never take ammo from your chests.
- A gun put away for melee keeps its rounds, and a gun in hand is saved loaded.
- **With Locked & Loaded,** survivors and raiders whose gun takes magazines start with loaded magazines for their tier and
  reload like you. Refills go into their magazines.

### Giving a member a better weapon

Put the weapon in their inventory (**Show me your inventory**), then pick it from **Jobs and gear**, Weapons, **Carried
Weapons**. They keep it in place of their weapon of that kind.

### Healing and armor

- SC's people **heal only with items:** they carry a couple of bandages and sometimes a first aid kit, and use one at or
  below 60% health, with the bandage animation. Hand them more medical items and they use those.
- Their armor protects them at half the rating it would give you. It does not break.

### Looks

- Clothes come from vanilla's armor sets by tier (light sets likelier early, heavy ones later), or now and then a whole
  outfit from a supported armor pack, if installed.
- **With Vanity Vault:** a few survivors have a body paint (1%), a face paint (2%) or a tattoo (15%); raiders far more often,
  from wilder designs.
- **With Vanity Vault, the face comes first and the skin tone suits it.** Vanity Vault splits a look in two: the skin tone
  (Alabaster to Espresso) sets how light or dark the skin is, and the face (16 faces: 4 each with White, Native, Black and
  Asian features) sets the features. A new survivor or raider gets their face first, then a tone that fits it: a Black face
  never comes with very light skin, and a White or Asian face never with very dark skin. The tones each face can get are the
  setting `SurvivorTonesByFace` (section 20). Without Vanity Vault nothing changes.
- **Hair color follows the skin tone and the face.** Medium skin tones never get natural blond or red hair; dark tones also
  never get light brown; light tones can have any natural color. Which tones count as medium or dark differs a little
  between men and women (the same tone looks lighter on one than on the other). With Vanity Vault the face counts too: an
  Asian or Native face is treated as at least medium and a Black face as dark, so a Black face with light skin gets dark
  hair. Without Vanity Vault: Asian and Native as medium, Black as dark, White any color. Gray and white hair stay as common
  as before on every skin, about 1 in 5. Dyed colors (pink, blue, green and so on) still turn up now and then on anyone.
- **People you already know are fixed too.** A survivor whose natural hair color does not fit their skin and face gets a
  fitting one the next time they load (always the same one for that person, and never gray or white in place of a
  young-looking color); the rest of their look stays: **their face and skin tone are never changed by themselves.** If an
  older survivor's skin tone does not suit their face, `sc look <name> tone` gives that one survivor a tone that does (only
  when you ask; CONSOLE_COMMANDS.md). Dyed hair is left alone, and so is a face that came from 0-XNPCCore's character
  creator (one taken on with this version or later: a creator face a survivor took on before it is not recognized, and its
  hair color can be changed like anyone else's).

---

## 16. Voices and subtitles

### The NPC's line

When you talk to a survivor, **their line shows in a panel above your responses**, so you always see what they said.

### Recorded voices

- Survivors speak their talk lines in **recorded voices** (generated with ElevenLabs). There are **16 voices**, two for each
  race (White, Black, Asian, Native) and sex. Each survivor keeps one voice of their race and sex.
- **All 16 voices are recorded.**
- **With Vanity Vault, voices follow the face.** Vanity Vault replaces the four races with skin tones, and the features a
  player reads as White, Native, Black or Asian come from the face (section 15). So each survivor speaks with the voices
  of their face's race, with one exception: a White or Asian face with a dark skin tone (for men Bronze, Umber and
  Espresso; for women Chestnut, Umber and Espresso) speaks with Black voices. Survivors you have already met switch to a
  voice that fits their face the next time they load. The settings are `VoiceRaceByFace` and `VoiceDarkTonesMale` /
  `VoiceDarkTonesFemale` (section 20); with `VoiceRaceByFace` off, the skin tone alone decides (`VoiceRaceBySkin`), as in
  the previous version.
- Lines with a name or a number in them say only the part without it; the panel shows the whole line.
- A few of the newest lines (for example the training line after reading a magazine, and refusals about the work kit,
  field jobs and trading) are text only for now.
- Voices play through Voice Control's player with **lip sync** when Voice Control is set up on that NPC.

### Greetings, chatter and taunts

- **Greetings:** survivors greet you in a line that fits who they are to you (a stranger, a recruit, a member at home, a
  follower, one of an enclave's people), the first time each day you see them within 12 m, and when you walk up to them.
- **Chatter:** every minute and a half or so, a resident at home near you says something to nobody in particular.
- **0-XNPCCore's lines:** where Voice Control would speak one of 0-XNPCCore's job lines or order replies ("I'm out of
  ammo.", "Right behind you."), SC plays its recording in the survivor's own voice.
- **Raider taunts:** raiders call out when they spot you, hunt you, lose you, are hit, kill you, die, or fall back. One
  raider at most every 15 seconds, so a band does not all shout at once.
- **Runners on the radio** speak through a radio filter (section 8).

### Voice Control during a talk

- While you talk to one of SC's survivors, Voice Control stays quiet on them, so it does not talk over SC's line. You can
  still speak to them through Voice Control during the talk.
- **Voice Control's chat knows the enclave:** talk to one of an enclave's people through Voice Control's chat and they know
  their group, its standing with you and its open request. They send you to the talk menu for any deal.

---

## 17. The community window

**How to open it:** the **Community tab** left of the player menu's tab bar, the **ESC menu** (Community), the land claim
radial (**Community**), a member's talk (*"Let's talk about the community."*), a watch post's **Assign guard**, or an
outpost's **Outpost** radial. With the tab bar showing, the gamepad bumpers page to it like any other tab.

The window has three tabs.

### Members

- **Left:** one row per member with their status, mood, skills and specialty. Injured rows are red. A **filter** (All
  members, At base, Following, On a run, Injured, At outposts) and a **search** box narrow the list.
- **The selected member:** their health, mood, skills, specialty, traits and status (at home, following, on a run, on
  watch, holding an outpost, missing, on the way).
- **Orders:**
  - **Follow me / Go home** for a member at home or following.
  - **Recall home** for a runner (needs their radio, or you with them) or a member posted at an outpost.
  - **Call back** for a missing member.
  - **Ask to leave** (press twice).
  - **Send on supply run**, then pick Food, Water, Medical, Ammo, Materials or Fuel.
  - **Work and roles:** toggle the **Cook, Smith, Chemist** and **Crafter** roles, and **Call to work / End work** (section 7).
- **Community morale:** the number and what is helping and hurting it, part by part, the beds count, the stockpile and
  the recent days.
- **Open storage** opens linked storage.

### Outposts

Your outposts with their status, what each sends home, distance and what is waiting there; the network summary (slots,
radio, deliveries, costs); perks in force; and the buttons **Track on map**, **Assign** (post a member), **Upgrade**,
**Mode** and **Disband**.

### Activities

The round of drinks (section 4): its cost, effect and status, and **Pour a round**.

---

## 18. Blocks and items SC adds

| Name | What it is | How to get it |
|---|---|---|
| **Packing Station** | Lockable crate that packs goods into bundles and deposits them | Backpack: 30 wood, 5 forged iron, 2 duct tape |
| **Radio Station** | Powered radio: air drops, survivor calls, scouting, barter, outposts, enclaves | Workbench: 15 forged iron, 6 electrical parts, 4 mechanical parts, 10 polymer scrap |
| **Handheld Radio** | Opens the radio window from anywhere (with home power); lets runners talk to you | Workbench: 3 electrical parts, 2 mechanical parts, 5 polymer scrap, 2 duct tape; rare electrical loot; traders now and then; rare raider drop |
| **Watch Post** (any shape) | A post for a guard on watch | Backpack: 10 wood, 4 nails |
| **Guard Tower** | Kit that puts up a watch tower with a Watch Post | Backpack: 782 wood, 4 nails |
| **Outpost Flag** | A land claim that becomes an outpost when placed in a cleared building away from home | Backpack: 5 small stones, 5 cloth, 5 scrap iron |
| **Small / Medium / Large bundles** (food, water, medical, ammo, materials, fuel, luxury) | 1 / 2 / 3 stockpile units | Packing station, loot, air drops, trader quest rewards, allied supply drops |
| **Allied Supply Crate** | Supply drop from an allied enclave | Radio: an ally's Supply drop |
| **Remains** | A missing survivor's body (Search the body) | Placed by an enclave's missing-survivor request |
| **Watch Post (old cube)** | The older cube post; still works where it stands, no longer crafted | Older saves only |

---

## 19. Included: Upgradable Land Claim Block

The zip has a second mod folder, **LandClaimUpgrade**, made to pair with Survivor Community: your community lives inside your
land claim, and this lets the claim grow to take in the buildings around it. With it installed, SC's home zone is the
whole grown claim.

- **Claim Menu:** hold **E** on your own land claim block and pick **Claim Menu**. It lists the POIs next to your claim
  (within about 40 blocks of its edge) with their state: Ready, Not secured, Quest active, Need materials, Blocked or Out
  of range.
- **Claiming a POI:** clear a POI of its sleepers, then **Claim this POI**. It takes the POI's footprint and the street
  between it and your land. The price scales with its size: 25 forged iron and 50 concrete mix, plus 10 forged steel and
  100 concrete mix per 1000 blocks of footprint. A claim can reach up to 200 blocks from its block.
- **Start secure quest** adds an optional journal quest that marks what is left inside. It never resets the POI.
- **Expand into open ground** grows the claim 1.5x when no POI is in reach (up to level 5; each step costs more forged
  iron or steel and concrete mix).
- **Placing a claim inside a POI:** you cannot place one inside a POI until that POI is cleared. Placed in a cleared POI,
  the claim covers the whole POI from the start.
- **Locked doors inside your claim become yours** when you try them. Cleared POIs get a **green check mark** on the map.
- You get a "Your Land Claim" buff while standing inside your own claim.
- Settings are in `Mods\LandClaimUpgrade\Config\blocks.xml`.

It is optional: delete the `LandClaimUpgrade` folder before you start the game if you do not want it. Taken out of a save
later, its claims shrink back to the usual 41x41.

---

## 20. Settings

SC's settings are in **`Mods\SurvivorCommunity\Config\blocks.xml`**, in the block named `ConfigSurvivorCommunity`. Each
setting is a line like `<property name="MaxFollowers" value="2" />`, with a comment above it explaining it. Edit the value,
save the file, and **restart the game** for the change to take effect.

These are the ones you are most likely to change. The file has many more.

### Community

| Setting | Default | What it does |
|---|---|---|
| `MaxMembers` | 12 | Most members the community can hold |
| `MaxFollowers` | 2 | Most members following you at once |
| `PostRadius` | 8 | Residents' posts are within this many blocks of the land claim block |
| `ResidentRoamZone` | true | Residents walk about the whole base by day (false: they stay near their post) |
| `HomeKeepLoaded` / `HomeKeepLoadedRange` | true / 250 | Keep the home zone loaded while you are within this many meters |
| `AutoHomeFirstClaim` | false | true: your first land claim becomes the home by itself, as before (false: you start a community from a claim's radial) |
| `CommunityFriendlyFire` | false | true lets your shots hurt your own members |
| `DefendBaseRadius` | 22 | Residents attack enemies inside the base within this many meters of their post (0: only what they see) |
| `MembersOpenOwnerLockedDoors` | true | Members open your locked doors |
| `MembersOpenHatches` | true | Members open hatches and drawbridges |
| `StreamerNames` | false | Give your viewers' names from a text file to the new survivors SC makes (section 3) |
| `StreamerNamesFile` | StreamerNames.txt | The names file, in `%APPDATA%\7DaysToDie\SurvivorCommunity\` (or a full path) |
| `StreamerNamesFor` | survivors | Who gets the names: `survivors`, `raiders` or `all` |
| `DebugLogging` | false | More detail in the log for bug reports |

### Needs and morale

| Setting | Default | What it does |
|---|---|---|
| `FoodPerMemberDay` | 0.5 | Food units each member eats a day |
| `WaterPerMemberDay` | 1 | Water units each member drinks a day |
| `PointsPerBundleFood` (and `...Water`, `...Meds`, `...Ammo`, `...Materials`, `...Fuel`, `...Luxury`) | 400 (200, 300, 2000, 1000, 600, 300) | Trade value that makes one bundle unit |
| `LuxuryItems` | beer, Grandpa's drinks | Items that pack as luxuries |
| `DeathPenalty` / `DeathDecayPerDay` | 15 / 5 | Morale lost per death, and how fast it fades |
| `RefuseOrdersBelowMood` | 0 | Members refuse orders below this mood (0: never) |
| `LowMoraleThreshold` / `LowMoraleDays` | 25 / 2 | Below this morale for this many days, someone threatens to leave |
| `HighMoraleThreshold` | 75 | Above this morale, faster XP and bigger runs |
| `DrinksLuxury` / `DrinksFood` / `DrinksMorale` / `DrinksMinutes` | 4 / 1 / 15 / 15 | The round of drinks |

### Supply runs and radio

| Setting | Default | What it does |
|---|---|---|
| `PhysicalRuns` | true | Runners walk to a real building (false: hidden one-day runs) |
| `MaxRunners` | 3 | Most runners out at once |
| `RunMinDist` / `RunMaxDist` | 150 / 700 | How far runners go (meters) |
| `RunSiteHours` | 6 | Game hours spent searching the site |
| `RunYieldBase` / `RunRiskBase` | 2 / 0.15 | Base haul in units, and base risk |
| `RadioOnlyAirdrops` | true | Air drops only come when the radio requests them (false: vanilla's scheduled drops) |
| `RadioCallDays` | 2 | Days between calls for survivors |
| `RadioDistressChance` | 0.35 | Chance a runner gets pinned down at the site |
| `HandheldRadioTab` | true | Show the Radio tab while you carry a handheld radio |
| `RunnerRadioTalk` / `RunnerRadioVolume` / `RunnerRadioEffect` | true / 0.8 / true | Runners talk on the radio; its volume; the radio filter |
| `DistressNeedsRadio` | true | Only runners with a radio can get pinned down and call for help |
| `WindowRecallNeedsRadio` | true | The community window's Recall home needs the runner's radio |
| `RunnerCallsWhenAttacked` | true | Runners with a radio call when attacked |

### Crafting and work

| Setting | Default | What it does |
|---|---|---|
| `CraftSkillsEnabled` | true | Specialty stars set members' crafting skills |
| `WorkShiftsEnabled` | true | Residents can craft and work at home |
| `TalkHold` | true | The talk link for residents at home |
| `WorkIdleSeconds` / `WorkCallSeconds` | 90 / 180 | When a work shift ends |
| `WorkDepositOutput` | true | Work output goes into the home crates |
| `MemberWorkKit` | stone axe, stone shovel, bone knife | The work kit (empty or `none`: no kit) |
| `RepairDutyIdleMinutes` | 5 | A repair duty ends after this long with nothing to repair |
| `ReplantCrops` | true | Members replant crops they pick |
| `CraftSpecXpPerMagazineLevel` | 10 | Specialty XP per crafting magazine level (0: magazines do not help) |

### Defense

| Setting | Default | What it does |
|---|---|---|
| `HomeStatusHud` | true | The Home Status panel |
| `BaseNoise` | true | The noise meter and attention |
| `BaseAttacks` | true | Hordes attack the base when attention is full |
| `BaseAttacksAway` | true | Hordes can attack while you are away |
| `BaseSiege` | true | A blood moon you spend away is settled as a siege |
| `BaseAttackGapDays` | 2 | Days between base attacks at least |
| `WatchPosts` / `WatchMaxPosts` | true / 4 | Watch posts on, and how many |
| `WatchShiftHours` / `WatchRestHours` / `WatchShiftAmmo` | 12 / 12 / 3 | A guard's shift, rest, and ammo bundles per shift |
| `GuardTowerGhost` | glow | How the tower preview looks: glow, solid, plain or off |
| `StuckSeenWaitSeconds` | 60 | A stuck resident waits this long while you look before being moved (0: at once) |

### NPCs and voices

| Setting | Default | What it does |
|---|---|---|
| `XnpcResupply` / `XnpcResupplyBelow` | true / 10 | Stockpile refills at home and outposts, below this many rounds |
| `ShootingSkill` | true | Shooting stars decide NPC accuracy |
| `Overwhelmed` | true | NPCs keep shooting when several enemies are on them |
| `DialogLine` | true | Show the NPC's line above your responses |
| `DialogPager` | true | Split long talk pages |
| `VoiceLines` | true | Play recorded voice lines |
| `ScGreetings` / `ScChatter` | true / true | Greetings and residents' chatter |
| `RaiderTaunts` | true | Raider taunts |
| `MuteVcInTalk` | true | Voice Control stays quiet during SC's talks |
| `VoiceRaceByFace` | true | With Vanity Vault, survivors speak with the voices of their face's race; false: the skin tone decides (`VoiceRaceBySkin`) |
| `VoiceDarkTonesMale` / `VoiceDarkTonesFemale` | Bronze, Umber, Espresso / Chestnut, Umber, Espresso | Skin tones that make a White or Asian face speak with Black voices |
| `VoiceRaceBySkin` | Vanity Vault's 15 natural tones | Which race's voices each skin tone speaks in, as `Tone:Race` (`Honey:Asian`); a tone may name two races. Used only when the face gives no race, or with `VoiceRaceByFace` false |
| `SurvivorTonesByFace` | the tones that suit each face (section 15) | With Vanity Vault, the skin tones a new face of each race can get, one list per race (the comment in blocks.xml shows the format); empty: face and tone rolled apart |
| `HairGroupByFace` | true | With Vanity Vault, the face narrows the natural hair colors too (Asian and Native faces medium, Black faces dark); false: the skin tone alone |
| `SurvivorMidSkinsMale` / `SurvivorMidSkinsFemale` | Warm Ivory ... Chestnut, Asian, Native / Cool Beige ... Bronze, Asian, Native | Medium skin tones for men and for women |
| `SurvivorDarkSkinsMale` / `SurvivorDarkSkinsFemale` | Bronze, Umber, Espresso, Black / Chestnut, Umber, Espresso, Black | Dark skin tones for men and for women |
| `SurvivorMidSkinHairColors` / `SurvivorDarkSkinHairColors` | 03 LightBrown to 08 White / 04 Brown to 08 White | The natural hair colors medium and dark skins can have |
| `SurvivorGrayHairColors` | 07 Gray, 08 White | Hair colors that read as age: they stay at their usual share (1 in 5) on every skin |

### Raiders, outposts and the world

| Setting | Default | What it does |
|---|---|---|
| `WildRaidersDespiteMods` | true | Wild raiders spawn even when another mod turns enemy spawns off |
| `RaiderTurretsAsEnemies` | true | Turrets and motion sensors treat raiders as enemies |
| `RaiderRadioChance` | 0.04 | Chance a dead raider drops a handheld radio |
| `OutpostMinDistance` / `OutpostMax` | 150 / 4 | Outposts' distance from home, and the most you can hold |
| `OutpostAttacks` | true | Outpost risk: attacks, sieges, squatters, raider demands |
| `OutpostGoodNews` | true | Outposts sometimes find caches |
| `Enclaves` | true | Enclaves on the map |
| `EnclavesPerKm2` | 0.3 | How many enclaves |
| `EnclaveDoorsUnlocked` | true | Enclaves' locked doors are unlocked when you come near (false: they stay as they are) |
| `EnclaveRequestMaxOpen` / `EnclaveRequestMaxPerDay` | 3 / 3 | Open requests, and new calls for help a day |
| `EnclaveRaids` / `EnclaveMoves` / `EnclaveJoin` | true / true / true | Raider enclaves raid outposts; enclaves move; whole enclaves join |
| `EnclaveGifts` / `EnclaveBarter` / `EnclaveCalls` | true / true / true | Gifts, ally barter, ally radio calls |
| `Bands` / `BandDailyChance` / `BandRaiderShare` | true / 0.5 / 0.35 | Bands on the road, how often, how many are raiders |
| `Infestations` / `InfestDailyChance` / `InfestPerKm2` | true / 0.4 / 0.2 | Infestations, how often, how many |
| `SleeperCapFix` | true | Sleeper rooms skipped by the zombie cap spawn again later |

For console commands (testing and fixing), see [CONSOLE_COMMANDS.md](CONSOLE_COMMANDS.md).

---

## 21. Known limitations

- **Single player only.** In multiplayer, only the host can manage a community, and other players would see SC's survivors
  without their clothes.
- **Game 3.4 only.** This build does not run on 3.2 or 3.3.
- **Do not remove the mod from a save that used it.** Loading that save without SC can damage the chunks its blocks are in.
- **The two Native women's voices are not recorded yet.** They speak with another woman's voice until then. A few of the
  newest lines are text only.
- **Mining and digging clay are not resident jobs.** Residents do home jobs only; take a member along for Mine, Loot here
  or Follow + loot.
- **Breaking an enclave's containers open is not stopped,** only opening them.
- **"Damaged" and "overrun" base results do not damage blocks.** A fight without you is a simulation: residents in sight
  just stand about.
- **A move is a jump:** an enclave that moves is not seen walking.
- **No holstering yet:** survivors keep a weapon in hand (lowered and hidden when out of danger). No dismemberment for SC's
  survivors and raiders.
- **Work kit:** a member chopping wood with a zombie stuck out of reach 15 to 30 m away may switch between their weapon
  and the axe at each tree.
- **Crafted items left in a resident's inventory:** if you keep a crafting station open while a resident works at it, some
  of what they make can stay in their inventory instead of going into the crates. Nothing is lost; take it through
  **Show me your inventory**.
- **Squad orders:** a spoken squad order's answer can come from a resident, though only followers act on it.
- **Existing saves and the handheld radio:** with `DistressNeedsRadio` on, runners get no distress calls until you give
  them radios.
- **The Watch Post's item description** names the default numbers (4 posts, 12 hours, 3 bundles); after changing the
  settings it no longer matches.
- **Enlisted enclave people** do not show in SCore's companion window.
- **Wild NPC spawns:** spawns kept away from an enclave's grounds can make wild NPCs a little rarer around it.
- **If you ever uninstall** despite the warning above, load the save once with `Enclaves=false` first, or the rooms the
  enclaves kept clear stay empty until something resets those buildings.

---

## 22. Credits

- **sphereii** for 0-SCore and **Dave Taylor** for 0-XNPCCore, which this mod is built on.
- Survivor voices were generated with **ElevenLabs**.
- Made by **Elisk**.

---

Developers: the design and implementation notes are in [DEVELOPER.md](DEVELOPER.md).
