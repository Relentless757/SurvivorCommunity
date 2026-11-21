# Next pre-release (not released yet)

## 0-XNPCCore 3.4.06 (final) and 0-SCore 3.4.25.925

- **Now needs 0-SCore 3.4.25.925 or newer**, with 0-XNPCCore 3.4.06 (final) and its Voice Control. 0-SCore 3.4.25 sees
  enemies twice as far out as before for "is an enemy near?", so SC's own distances for it are halved: residents stop
  roaming, chatting and gathering and go back to their posts for an enemy at the same distance as before. With an older
  0-SCore they would carry on until an enemy was half as far away.
- **No more doubled rounds after a save.** Since 0-XNPCCore 3.4.06 final, the rounds in a member's gun were kept twice
  every time the game was saved or her area unloaded behind you: in the gun, and again in 0-XNPCCore's own record. Taking
  her gun away then gave you the loaded gun plus the same rounds loose in her bag; she found extra rounds on a later
  draw; with Locked & Loaded she gained a magazine; and she reloaded every time she drew her gun. SC alone keeps those
  rounds again, and a save made since the update is cleaned up the first time it loads.
- **Down stays down.** A downed member whose area unloads (you walked away) is still down when you come back, as
  0-XNPCCore now keeps her. You no longer get a second "is down" message for the same fall, and her Bond no longer drops
  twice for it.
- **A dismembering killing hit kills outright.** A killing hit that dismembers a member (a headshot that takes her head
  off) or blows her apart now kills her instead of knocking her down: that is 0-XNPCCore's rule now.
- **The re-down warning.** Going down again within 10 minutes of a revive is a real death (0-XNPCCore's window, its
  `RedownWindowSeconds`). The "back on their feet" message now says so, with the minutes left, and the community window
  shows "Fragile N min" on the member's row until it runs out. `sc members` and `sc xnpc <id>` show it too.
- **Cornered, she keeps fighting.** A member with no room to back off to heal used to bandage at once, with the zombie on
  her, every time - one member did it 12 times in one fight, and each bandage stops her attacking for a few seconds. Now
  she keeps fighting while her health is above 35% and bandages at once only below it. With room to back off nothing
  changes: she backs off, then heals. `CorneredHealBelow` in `Config/blocks.xml` sets the 35%.
- **The weapon toggle is an order.** 0-XNPCCore's new "Melee -> Ranged" / "Ranged -> Melee" toggle on the order wheel now
  works like telling her "use your gun" or "use your melee weapon": she keeps that weapon until you order the other one
  (or `sc team <id> auto`). Before, the toggle and SC's own weapon choice fought over her hand. A gun order still gives way
  when the gun has no ammo, a badly hurt member still keeps her distance, and cornered while reloading she still swings
  her melee weapon until she has room. Ordering her weapon by talking sets the toggle too.
- **Residents fight what is in reach.** A resident at home sometimes walked off from the zombie in front of him toward
  one 25 m away, outside the base. Residents now finish the fight at hand, and leave enemies farther out than the base
  defense reaches (`DefendBaseRadius`, 22 m) alone unless those come at them. Guards on watch still shoot whatever comes.
- **Quiet kills.** Telling a member to take an enemy out quietly while she holds a torch no longer keeps her on her melee
  weapon for good afterwards, and SC no longer swaps her weapon in the middle of the stalk. A member sent home no longer
  carries on stalking an enemy from before. The console shows the new hunting stance as "hunting".
- After a load SC's restored gun is no longer swapped out again when her weapon preference is ranged.
- A member's fight posture no longer flickers to "no fight" and back for a moment when her target is lost for a tick.

## Survivors

- **No more life skills that do nothing.** New survivors no longer roll Fitness or Firearms Instructor: both wait for
  the gym and the range, which aren't in the game yet. Survivors who already have one keep it. (`SpecialtyWeights` in
  `Config/blocks.xml` brings them back with a weight above 0.)
- **Marksmen use their melee weapon up close.** A member whose best skill is Shooting used to keep her gun against
  everything, even a single zombie at arm's length. She still shoots at range, but against a lone zombie, or in a fight
  she isn't losing, she switches to her melee weapon once it is within 4 m (`PostureMeleeDist`), like everyone else,
  and back to the gun once it is past 5 m. Hurt, swarmed or facing raiders she keeps the gun.

## Community window

- **The day's net beside each resource.** The stockpile across the top of the community window shows, next to each
  count, what the next day's end will add or take: +N in green, -N in red. Six members and nothing growing shows -6 by
  the food; an outpost sending 2 food makes it -4. It counts the outposts (deliveries, upkeep, haulage, workshops),
  allies' bundles, the garden, wells and dew collectors, what your people eat and drink, and the Meds the injured need.
- **Days left count what comes in.** The "N days remaining" under the food and the water now subtract the outposts'
  deliveries, allies' bundles, the garden and the wells from what is used; covered entirely, it reads "+N a day spare".
- **Farm plots** are a facility now: the living crops, the food a day, the crops a food takes (and your gardener), and
  how many more crops make the next food.
- The Facilities list scrolls with the mouse wheel, a row at a time.
- A feast and rationing never run at once: no feast while the community is on rations, no rationing during a feast.
- Quieter log: the one-line-per-patch list only appears with `DebugLogging` on, at world load (the start-up summary
  line stays).

## Food

- **The garden is easier.** It makes 1 food a day per **5** crops instead of 10, and no longer needs a gardener at all.
  A good gardener at home still helps: from level 4 it is 1 food per 4 crops, from level 7 1 per 3. Only your best
  gardener counts, and there is no cap any more (it was 1 food per gardener star). `CropsPerFood` and the new
  `GardenerCropsPerFood` in `Config/blocks.xml` set it.
- **Prepare a feast.** With a cook at home and a campfire in the base, the Campfire on the Facilities tab has a new
  action: for 3 food, every member and you get +10 morale and +10 max stamina for an hour of play, +1 morale and +2
  max stamina more for each level of your cook above the first. Once a game day.
- **Enact rationing.** Also at the Campfire: at the end of the day the community eats half its food (rounded up), for
  -15 morale and -15 max stamina for an hour. Once a game day.
- The Gardening and Cooking descriptions say so, the morale breakdown lists the feast and rationing, and the day's
  report says how much food rationing spared.

## Armor

- **Armor now protects survivors and raiders fully.** SC used to halve what worn armor gave them. Two new settings in
  `Config/blocks.xml` set it instead, `SurvivorArmorResist` for SC's survivors and `RaiderArmorResist` for its raiders:
  0 (the default and the most) is the protection the same armor gives you, -0.5 the old half, -1 none. A
  survivor or raider takes a changed value the next time it spawns or the game loads. Other packs' survivors keep their
  own armor, as before.

## UI mods

- **SC works with CATUI, Steelshot UI Hybrid, SteelUI and MothUI.** Each UI mod gets a few small fixes so SC's tabs, the Home
  Status panel and SC's windows stay reachable and readable:
  - **CATUI:** the Home Status panel moves below CATUI's compass clock and enemy alert bar.
  - **Steelshot UI Hybrid:** the Community and Radio tabs move to the screen's top-left corner, between Steelshot's tab
    bar and its party list (it pushed them off the screen). The member inventory and linked storage draw over Steelshot's backdrop instead of
    under it. Steelshot's backdrop goes behind SC's two pages, the Home Status panel moves below its compass onto its
    glass plate, and the Community button in the ESC menu takes Steelshot's look.
  - **SteelUI:** SteelUI rebuilds the player menu's tab bar after SC has added its tabs, which removed them (and with
    them the only way into the radio from a handheld radio). A new small folder in the zip,
    `zzzz_SteelUI_SurvivorCommunity`, loads after SteelUI and adds them back in SteelUI's style. The member inventory
    keeps its own slot size (SteelUI's slots overlapped), and the Home Status panel moves under SteelUI's location row.
  - **MothUI:**
    - The Community and Radio tabs sit at the end of MothUI's page bar at its spacing (they landed left of their
      slots), and the pages are titled COMMUNITY and RADIO (they read SCCOMMUNITY and SCRADIO).
    - Talking to a survivor: the talk card moves to the right of MothUI's dialog card (it covered the line and the
      replies), and SC's separate line box stays hidden because MothUI's card shows the line. Enclave lines show
      their names there instead of `{enc}` and `{leader}`, and the replies page by 6 to fit the card.
    - The Home Status panel is a MothUI HUD widget: top right by default, and Edit HUD can move, resize or hide it.
    - The Community and Radio pages shrink to fit between MothUI's top bar and footer on screens too short for them.
    - The packing station and linked storage strips are glass cards of their own under MothUI's loot card, so its key
      hints stay visible.
    - SC's own windows keep their vanilla look on MothUI's glass for now.
- **MinimalistHUD + PhysicalPDA** has its own support for SC built in; SC leaves that to its author.
- Use one UI overhaul at a time: none of these runs together with another (Ashfall UI included).

## Members walk to their outposts
- **You can post a member to an outpost from anywhere.** They no longer have to be near you: they set off from home on
  foot, the way a runner leaves on a supply run, and take up the post when they reach the outpost's claim. Near you they
  walk out of the base and you can follow them; out of sight they travel on, and you can meet them on the road.
- **The journey is safe:** no dice, no distress calls, no radio calls, and they keep going even when hurt. If something
  attacks them while you are near, they fight.
- **On the way** they already hold their post, so you can't assign more people than an outpost has posts, but they add
  nothing until they arrive. The Outposts tab shows them in the garrison as "on the way to (outpost), about N h", and the
  Members tab says "On the way to (outpost)". They can't be sent on a run, posted elsewhere, put on watch or asked to
  follow until they get there.
- **They walk home too.** Recall a posted member (the x on their post, or Recall home) and they leave the claim on foot;
  recall one still on the way and they turn back. When an outpost is lost, disbanded or overrun, its staff walk home
  the same way (hurt after an overrun) and anyone heading there turns back. They are residents again only when they walk
  in at home.
- A walker who reaches the claim while you are there becomes the stand-in on the spot; recalled while you are there, the
  stand-in steps aside and the member walks off from the same place.
- **Someone posted while you are away from home** sets out for real too. SC loads the few chunks around their body for a
  moment and takes them on the road. Anyone else standing in those chunks loads for that moment as well, so a crowded
  base can cause a short hitch. Recalled from the outpost the same way, their stand-in keeps its spot until the member
  steps in there. If that fails, they travel out of sight until you next come home (logged). If their body is gone from
  home, a new posting is called off and you are told to use Call back.
- People posted by an older build are taken on the road the first time you are home, or when you recall them.
- Posting journeys always walk, whatever `PhysicalRuns` says.
- **Console:** `sc outpost post` works from anywhere and says how long the walk is; `sc outpost recall` also turns back
  someone on the way; new `sc outpost skip <member>` ends the current leg of the walk now, and `sc outpost overrun <id>`
  overruns an outpost now (only one with a room an overrun can bring back, as a real one). `sc outpost list` shows who is
  on the way and who is walking home.
- **Saves:** a save made mid-journey carries on from where they were. A posted member's record carries their body, kept
  compressed (about 4.5 K characters each, a third of a runner's).
- **Going back to an older build costs your outposts' people their bodies.** The first time you are home, this build takes
  the body of everyone posted at an outpost, including people posted by an older build, into its own record. An older
  build shows them, and everyone on the road, as missing residents. Call back brings each one home with a new body: what
  was in their bag, their current health and their buffs are lost.

## Whole-number economy
- **Every daily change to the stockpile is a whole number now.** Nothing is carried over in hidden fractions and nothing
  is owed: what the windows show ("+2 food a day") is what arrives, every day, instead of 1, 1, 1, 1, 2.
- **Food use doubles: each member eats 1 food a day** (it was half a bundle). On the first day after the update your food
  stock lasts **half as long** as it did, so the days of food left and the food part of morale drop at once, and a
  community with little food can go hungry on the first rollover. Top up the food, or send a food run, when you load.
- Water is still 1 a day per member. **Members still on their way to the base** (a whole group that joined you) eat and
  drink nothing until they arrive. Members posted at an outpost or out on a run eat and drink as before.
- **Bottomless Stomach** eats 2 food a day. **Eats Like a Bird** eats 1 like everyone, but going hungry does not lower
  their mood.
- **The cook** at home saves 1 food a day per 2 stars, rounded up (1 at 1 or 2 stars, 4 at 7), never more than half of
  what the community eats. The days of food left in the morale breakdown and the community window now count the cook.
- **The garden** makes 1 food a day per 10 living crops, at most 1 per star of all your gardeners at home.
- **Outposts make 1, 2 or 3 bundles a day by level**, whatever they make (medical and ammo too). Then: +1 for a tier 4 or
  5 building, +1 per member posted, +1 once when a posted member's specialty fits (gardeners now fit water outposts too),
  +1 built up (2 beds and 1 facility you built inside; a food outpost may count 10 crops for the facility), quiet -1 (it
  can make nothing) and push +1. Every outpost of the same kind after the best makes 1 less (never below 1). Once an
  outpost makes 2 or more, 1 of them is its second resource: a fire station at level 2 sends 1 medical and 1 fuel. There
  is no daily cap any more.
- **Upkeep is 1 building material a day per outpost at level 1 and 2, and 2 at level 3** (a level 1 outpost cost almost
  nothing before). An outpost that is damaged or has its sleepers back makes nothing and now pays nothing that day.
  **Hauling** costs 1 fuel per 5 bundles delivered that day.
- **No outpost debt.** A day the stockpile cannot pay the whole upkeep, the outposts are run-down: each makes 1 less a
  day until a day the upkeep is paid in full. A day it cannot pay the hauling, they carry on foot: at most 2 a day each.
- **Supply runs** bring whole bundles: 2, +1 at 3 Wits stars and +2 at 6, the same again for the matching specialty, +1
  while morale is above 75, +1 with a fuel outpost's motor pool, and +1 when you rescue the runner. **Hoarder** +1 (now on
  a 2-bundle run too), **Tinkerer** +1 on materials and fuel runs, **Scavenger** +1 on a run that finds 4 or more (no
  dice roll any more). A hurt runner brings half, rounded down. Mining runs are unchanged.
- **Salvage yard:** 1 building material less on every outpost fee, upgrade and repair (never below 1). Establishing an
  outpost then costs 1 material and 1 fuel, and a repair 1 (before, it lowered neither).
- **Every number in the windows is whole:** an outpost's row and its yield, a staffer's "+1 a day", "Level 2 - 2 a day",
  "(+1 built up)", the network's costs ("2 building material, 1 fuel a day", no more "about 0.29 fuel"), the water
  facility's "drunk a day" and the days left in the morale breakdown. `sc status` now shows the next rollover's food,
  water, upkeep and hauling; `sc outpost yield` shows each outpost's steps.
- **Settings:** the old fractional settings have new whole-number names (`MemberFoodPerDay`, `CookStarsPerFood`,
  `CropsPerFood`, `OutpostLevelYield`, `OutpostUpkeepByLevel`, `OutpostHaulPerFuel` and more; README section 20). A
  config that still sets an old one loads without errors, and the log says it is ignored.
- **Saves** load as they are. The hidden fractions they carried (less than one bundle each) and any outpost debt are
  dropped.

## Fixes
- **A contested outpost is yours again as soon as you clear it.** It was only checked again at the next day's rollover,
  so after you cleared it the tab kept saying contested. Now it turns active within seconds of the last sleeper, with a
  message. The selected outpost's Produces line (and `sc outpost list`) says what is still left: rooms you haven't walked
  into yet (their sleepers only wake when you do) or sleepers alive.
- **An overrun or squatters no longer re-arm rooms only a door or switch wakes.** Such a room's trigger fires once, so a
  re-armed one kept the outpost contested for good; one already stuck that way no longer counts.
- **No more "sdcs-release threw NullReferenceException" when you quit.** Freeing a survivor's body as the world unloaded
  hit a mesh Unity had already destroyed, and stopped before releasing the rest.
- **Zombies that wander into a cleared building no longer stop you making it an outpost.** A zombie or animal walking
  in after you cleared it doesn't count any more; the building's own sleepers, raiders and hostile survivors still do.
  The same goes for a favor's "clear this building" (setting `PoiClearIgnoresWanderers`).
- **Shooting levels up a third as fast.** Guns kill far more often than melee, so Shooting raced ahead: it now needs
  three times the XP of the other skills for each star (`ShootingXpMult`). Stars your people already have stay, and
  their progress toward the next one stays where it was.
- **A survivor you're recruiting no longer says "I keep hearing good things about your place."** They had no way to
  know, and it played at any morale.
- **You can see which way a guard will look.** An arrow on a Watch post points the way its guard will face: on the
  post's ghost while you place it, over the Guard Tower kit's post, and on a placed post you look at. The post's
  tooltip says it too ("... - faces east"). Setting `WatchPostArrow`.
- **The Guard Tower's paint is old wood again, as it was in 3.2.** The tower was saved in a game that had
  OcbCustomTextures installed, which stores paint in its own wider format. Without that mod the game read the paint as
  a mix of other paints, one of them missing ("Missing paint ID XML entry: 196" in the log). New towers come out
  right. For a tower you already built, stand next to it and run `sc tower repaint` in the console (F1); faces you
  painted yourself keep your paint.
- **A survivor who once walked through a trader's compound can be talked to again at night.** SCore's NPCs run the
  game's trader code, so the first time one stood inside a trader compound it took that trader's opening hours on for
  good: from closing time it refused every talk ("Failed to lock EntityAliveSDXV4 ... Target refused to be locked on
  server"). It also greeted you as the trader, despawned itself when another NPC of the same kind stood within 10 m,
  and (with 0-XNPCCore's always-open stock id) kept re-opening the compound at night. SCore's NPCs - survivors,
  members, enclave people, 0-XNPCCore hires - now never take a trader compound on; the real traders are unchanged.
- **When your community can hold fewer outposts, the newest one goes idle at once.** It used to wait for the day's
  rollover. Until then, after a member died (or Leadership dropped), the community window, the Home Status panel and
  `sc outpost yield` still counted it, and the rollover delivered and charged less than they said. Now it goes idle
  within seconds, with a message, and works again within seconds, with a message, when there is room again. An idle
  outpost that gets its slot back is checked for sleepers at once and turns contested if they came back. An outpost
  overrun by sleepers stays contested even over the limit, so clearing it still gives the "clear again" message, the
  ledger line and the Leadership credit; then it goes idle if there is no room for it.
- **An outpost's row lists everything it sends home** ("+2 meds, +1 fuel/day"); it showed only the biggest.
- **The member detail says how far along a journey or run is.** Under the mood it now reads, for example, "On the way to
  (outpost) - about 1 h, 312 m from home", "Walking home from (outpost) - ...", or where a supply runner is. The line was
  made but never shown.
- **`sc run` gives the reason it refused in words** ("They are on the road to or from an outpost."), not as a
  localization key.
- **`sc tower repaint` finds the towers built before the paint fix.** It looked for a mark in the old paint that the
  game drops as soon as a block is built (the world keeps the paint of six faces per block), so it never found one. It
  now recognizes the old paint mix on the faces themselves. Faces you painted yourself still keep your paint.
- **Survivors no longer get Vanity Vault's anime hairstyle** (Sonja_Med_02, the men's and the women's). One who already
  has it gets another style the next time the game loads them (not one wearing a face made in 0-XNPCCore's character
  creator: that stays as it was made). Any hairstyle on `SurvivorExcludeHairs`
  (Config/blocks.xml) is now swapped on survivors you already met too, not only kept from new ones.

## Enclaves speak on the radio
- **You hear enclaves now.** When an enclave contacts you, the message still shows, and now its leader's own voice says
  their words over the radio, crackle and all: an ally answering **Send a fighter** or **Supply drop** (or that their
  call couldn't get through), a raider group's demand on an outpost and its answer when you pay or refuse, a Friendly or
  Allied group moving away. The leader sounds the same as when you talk to them.
- **In person when they're near.** An ally's fighter says hello as they reach you, a leader answers aloud as their whole
  group joins you, and drifters thank you after you help them fight off the dead, and the one you talked to answers as
  their band joins you: from where they stand within 30 m, over the radio when farther.
- **Only with a radio.** You hear the radio aloud while you carry a handheld radio, or within 30 m of one of your radio
  stations that has power (at home or at an outpost). Otherwise you get the message alone, as before. Someone who walked
  out of earshot before their turn to speak comes over the radio instead, or not at all.
- **Someone always answers:** if the leader is dead or away, the next of their people speaks. A group you have never met
  gives you the message alone.
- **The radio takes turns:** a siege call goes first, then the base's horde warnings, then your runners, then the other
  enclave lines. When too many wait, the least urgent one is dropped.
- **A siege calls again halfway.** If you are not there by half of the siege's time, they radio once more: "Where are you?
  We're losing the wall! Hurry!"
- New setting `EnclaveVoiceLines` (README section 20); `sc radio enc <moment>` plays one now.

## Traits and life skills
- **Twelve new traits:** Night Owl (happier on a night watch), Light Sleeper (up at once for a horde; +1 base defense),
  Scavenger (1 more bundle from a supply run that finds 4 or more), Haggler (radio barter at 3 for 2), Fast Healer (heals an injury without medical
  supplies half the time), Bookworm (+50% from magazines), Squeamish (never the medic), Clumsy (the tools she chops, mines
  and digs with wear 50% faster) and
  Steady Hands (a fifth less aim error), each with its own icon. Iron Stomach, Gun Nut and Brawler wait for the latrine,
  range and gym, and say "(coming later)" on the card.
- **Clashing traits never come together:** Optimist and Pessimist, Cheerful and Pessimist, Hardy and Pessimist, Loner and
  People Person, Eats Like a Bird and Bottomless Stomach, Veteran and Cowardly, Brawler and Cowardly, Medic Training and
  Squeamish, Clumsy and Steady Hands.
- **Seven new life skills:** Construction (repairs stronger blocks on a Base repair duty), Logistics (opened bundles give
  more back), Bartering (cheaper radio and ally barters), Recon (scouting marks 5 sites), Entertainment (a round of drinks
  lifts morale more), and Fitness and Firearms Instructor, which wait for the gym and range. Point at the specialty row on
  a member's card to read what it does.
- Survivors you already have keep their traits and life skill; new survivors roll from the new lists. `sc traits` and
  `sc spec` show and test them all.
- Known limitation: a save loaded in an older build afterwards loses the newer life skills (those members have none).
- New settings: the trait keys in the Traits table, `SpecialtyWeights`, `LogisticsShareTop`, `BarteringCutByStars`,
  `ReconScoutCount`, `ConstructionRepairTiers`, `EntertainmentDrinksPerStar` and their XP keys (README section 20).

## The Facilities tab
- **The community window's Activities tab is now the Facilities tab.** The base's facilities are on the left; pick one
  and its details and buttons show on the right, as on the Members and Outposts tabs.
- **Defense:** the defense rating against the next horde, the noise, the dead's attention and how fast it is growing,
  and the last attack's result. **Lights out** is ordered here (below).
- **Beds** against members and the morale they give, with which beds count; **Water** made against water drunk;
  the **Workshop** (workbenches, forges, chemistry stations, cement mixers) with **Call to work**, which picks a
  resident; the **Campfire**, now home to the round of drinks and **Pour a round**; the **Radio station** and its power;
  **Watch posts** with their guards and **Assign guard**; the **Guard Tower**; and **every well and dew collector** with
  its level, its water and **Upgrade**.
- Nothing was lost from the Activities tab: the round of drinks works as before, from the campfire's entry (no campfire
  needed). The tab reads from what was counted at home, so it shows the same from anywhere.

## Base defense: Lights out, watch fatigue and salvage
- **Lights out:** an order for a quiet night, from 2 hours before dusk until dawn. Your people's noise is halved, and
  every running generator in the base is switched off until dawn (and with it whatever it powers: lights, traps,
  turrets, a radio station on it). At dawn they go back on. **Lights on** ends it early.
- **Watch fatigue:** a watch of 8 hours or more costs the guard 5 mood until they have rested after a full shift.
  Insomniacs enjoy a night watch: +3 mood while it is dark, and no tiredness. A Night Owl keeps her own +6 on a night
  watch and doesn't tire then either (the two never add up).
- **Salvage:** a held attack's corpse pile gives the stockpile 1 material per 5 dead; a fight or siege held without you
  gives the same for the horde your people faced.
- New settings `LightsOut...`, `WatchFatigue...`, `WatchNightTraits`, `WatchNightMood` and `SalvageKillsPerMaterial`
  (README section 20). `sc lightsout`, `sc watch fatigue` and `sc facility tab` in the console.

## Downed, not dead
- **A killing hit downs a member instead of killing her** (0-XNPCCore 3.4.06): she lies there unhurt with a 60-second
  countdown, and you revive her with **E** and a first aid bandage or kit. If nobody does, she bleeds out, and only then
  is it a death. This now covers **residents at home and guards on watch** too, not only followers; runners on a run
  still die at once. `DownedAtHome` false turns the home part off.
- **Medics revive at home.** A resident with the Medicine specialty runs to a downed member at home, even mid-fight, and
  gets her back up with a first aid bandage or kit from her own pack. The nearest free medic goes, one per downed member;
  a medic who is badly hurt herself, on watch or talking to you doesn't. `ResidentMedicsRevive` turns it off.
- **You're told** when a member goes down, which medic is coming or why nobody can, when she's back up and if she bleeds
  out; the community window shows "Down - 42 s" on her row.
- **Nothing undoes a downed member:** a work shift or a talk with her stays on while she is down (her countdown never
  stops), and she can't be sent home, on a run, to an outpost or a watch, or leave until she's up. She doesn't count as a
  fighter for the others, isn't sent after intruders and says nothing.
- `sc downed`, `sc down <name>` and `sc revive <name>` in the console.

## Social life
- **Small talk.** Two residents standing near each other now and then stop for a quick exchange about life at the base -
  the stockpile, the weather, the walls, the nights, food, you - and turn to each other and gesture as they talk. What
  they say fits their traits and mood. 38 pairs, spoken in all 16 survivor voices, with captions in Voice Control's
  subtitle bar.
- **You overhear them** only from 10 to 32 m away; walk up and they stop. One conversation at a time in the base, about
  every minute and a half, and the same two at most once every 10 minutes.
- **Walking over to chat.** Now and then a resident on her walk about the base heads over to a neighbor to talk; the
  neighbor waits for her.
- **The evening campfire.** With a campfire inside the base, free residents gather around it once each evening (from about
  an hour before dusk until bedtime) and during a round of drinks, for 30 to 60 seconds each, and talk. The fire shows lit
  while they are there (flames only: no fuel, no change to the block).
- **Enclaves too:** their people talk with each other and gather at a campfire in their own building.
- **Never** for anyone asleep, on watch, on a work shift, following you, away, downed, hurt and sheltering, fighting or
  with an enemy in sight, or in a talk with you; nothing during a base attack or on a blood moon night.
- **Strangers greet a group once:** when one wild survivor greets you, the others around them no longer greet you one
  after another.
- **Bedtime follows 0-XNPCCore.** SC now reads 0-XNPCCore's own `SleepAfterDuskHours` and never puts anyone to bed
  before it, so raising it there can no longer send leased members to the campfire.
- New settings `ResidentSmallTalk`, `SmallTalk...`, `CampfireGathering`, `Campfire...` and `ScGreetOnePerGroup` (README
  section 20); `SmallTalkTtsFallback` (off) lets a survivor without recordings use Voice Control's own small talk with its
  text-to-speech. `sc chat` shows and tests it all.

## 0-XNPCCore 3.4.06
- **Equipment... opens the member's inventory panel.** 0-XNPCCore 3.4.06's new equipment window (the "Equipment..." talk
  row, the hold-E ring's Equipment and Jobs and gear's Equipment) opens SC's Survivor Inventory panel for members. Hired
  strangers keep 0-XNPCCore's window. The ring no longer shows a second "Inventory" entry on members.
- **Every member has the Equipment... row again**, a guard on watch and a resident you are not linked to included. With
  3.4.06 SC's "Show me your inventory" row for them had disappeared.
- **Choose your member's ammo type.** For a gun that takes more than one kind of ammo, the line beside Ranged in the
  inventory panel shows the kind they use and their rounds of it; click for the next kind they have, right-click for the
  one before. They reload with it and the stockpile refills that kind. When it runs out they use what they have and
  switch back once they have it again. With Locked & Loaded the chosen kind goes into their magazines once one is empty;
  until then the line shows it as "loose". Picking it through 0-XNPCCore's order wheel or by voice counts too.
- **"Use the weapons I gave you." is gone.** 3.4.06 removed the weapon menu it lived in; drop the weapon on the panel's
  Ranged or Melee slot instead.
- **No debug Remove on members.** 0-XNPCCore's debug Admin page no longer offers "Remove (debug)..." on SC's people: it
  deleted the body without a death and left a member with no body behind.
- **Fixed:** with 3.4.06 every load logged two errors about a missing "What are you carrying?" patch; it is now skipped
  quietly.
- **Fixed:** closing the member panel could make 0-XNPCCore's equipment window pop up the next time you closed a chest.
- **Fixed:** a resident you were talking to could lose the talk link while 0-XNPCCore's equipment or gift window was open.
- **Fixed:** with 3.4.06 two more of SC's patches were off, each with an error at every load: a member who read a
  crafting magazine as training shook her head and said "I already know what that one teaches." (her XP was still
  paid), and a resident given Follow + loot, Loot here, Mine or Dig clay took the job instead of refusing it. Both
  work again.

## 0-XNPCCore 3.4.06: Bond, gifts, weapon perks and the party
- **Bond.** Each member now has a Bond with you, from -15 to +15, that adds to their mood: a gift raises it by its points
  (0-XNPCCore's count, at most 6 a day), being revived +3 (by anyone), a blood moon survived with you +4 (with you when it
  starts and when it ends), going down -2, an enemy on them that you kill +1 (at most 3 such kills a day). It fades 1 a
  day. A down and its revive count once each, with or without Voice Control. It shows on the talk card under their
  mood and in the community window beside their mood word.
- **SC owns their standing.** 0-XNPCCore no longer keeps its own trust and affinity for your members, and their events and
  deaths no longer change your trust with it, so they never change what your paid companions cost. Its **Your progress**
  page reads "Trust: ... Feels: ..." from the member's Bond and mood, and Voice Control's `vc standing` on a member shows
  the same.
- **Gifts.** Members take 0-XNPCCore's gifts: first aid kits and bandages (used when hurt, and counted before a medical
  restock), painkillers (used as a last resort, for 40 health), drinks (a follower puts out fire with one), and ammo for
  their gun (loose rounds, loaded into Locked & Loaded magazines). They refuse coins: members are never paid (enclave
  people you enlisted refuse them too).
- **Followers carry drinks.** At home a follower with no drink left is given 2 boiled water from the Water stockpile, at
  most every 12 in-game hours, so they can put out fire on themselves.
- **Weapon perks from the stars.** Shooting stars give 0-XNPCCore's gun and bow perks and Fighting stars its melee perks:
  rank 1 at 3-4 stars, 2 at 5-6, 3 at 7, on top of the damage per star. They show on the Your progress page; perk books
  still add their own bonuses. Every member now keeps a Progression for it, crafting on or off (with both
  `MemberWeaponPerks` and `CraftSkillsEnabled` off, none, as before).
- **The party cap.** "Come with me." (talk, voice, window) is refused while 0-XNPCCore's party of 8 is full, paid
  companions included: "Your party is full (8/8)." `MaxFollowers` (2) still comes first.
- New settings: `MemberBond`, `BondCap`, `BondDecayPerDay`, `BondGiftPerPoint`, `BondRevive`, `BondBloodMoon`,
  `BondDowned`, `BondKillOnHer`, `BondKillPerDay`, `BondLoyalAt`, `MemberRefuseCoins`, `MedicPainkillers`, `PainkillerHeal`, `MemberDrinks`,
  `MemberDrinksBelow`, `MemberDrinkItem`, `MemberDrinksCooldownHours`, `MemberWeaponPerks`, `SharePartyCap`.
- New console commands: `sc bond [name]`, `sc bond <name> add|set <n>`, `sc bond <name> event <gift|revive|bloodmoon|down|kill>`,
  `sc bond decay`, `sc perks [name] [apply]`, `sc party`.

## Members' gear
- **Fixed: a member's tools and weapons no longer change quality.** Taking or swapping a member's gun or melee weapon,
  or loading the game, re-rolled the quality of every tool they carry, and of the weapons SC gave them. Their work-kit
  tools and those weapons now keep one quality each, and every other item they carry keeps its own when you swap their
  weapons. Weapons and tools you hand over keep exactly the quality they had.

## Loot
- **Store crates always hold a resource bundle.** Shamway (food or water), Pop-N-Pills (medical), Shotgun Messiah and
  Savage Country (ammo), Pass-N-Gas (fuel), and construction, Working Stiffs and Mo' Power (materials) crates each
  hold one bundle of their store's resource: Small 80% of the time, Medium 17%, Large 3%. Savage Country, Working
  Stiffs and Mo' Power crates had none before; the others had one only 6-32% of the time.
- **Air drops always hold at least one bundle:** a Medium food, medical, ammo, materials or fuel bundle, on top of the
  usual chances.

## Polish
- **The member inventory panel closes when that member takes a hit,** and a message says why. Your own fight, a hit on
  someone else, or their bleeding leave it open.
- **Linked storage says when the base has no storage boxes** at all, instead of "No box has room for that." An outpost's
  storage with no boxes, and a base only partly loaded, get their own message.
- **Spoken "dismiss", "pick up" and "hire":** when one of SC's people answers with their own line, the subtitle now shows
  that line too, not Voice Control's own reply.
- **The Watch Post's description names the numbers your settings use:** how many posts, the shift's hours, how much
  farther the guard sees, the ammo bundles a shift takes and how much each manned post slows the dead's attention.
- **Voice clips use less memory:** SC keeps at most 24 MB of its voice clips loaded (`VoiceClipCacheMB`) besides the
  48-clip limit, and drops a clip nobody has needed for 10 minutes (`VoiceClipIdleMinutes`). A line that is playing is
  never cut.
- **`sc mags`** in the console lists members' Locked & Loaded magazines against their tier's count. A member who got
  extra magazines before the duplication fix keeps them; `sc mags <name> trim` shows which ones a trim would remove, and
  `sc mags <name> trim confirm` removes them.

## Voices
- **The order wheel answers in their own voice.** Picking an order on 0-XNPCCore's order wheel no longer has the survivor
  say the slot's name ("Close", "Far", "Jump down", "Aggressive", "Loot here"): she gives that order's reply in her
  recorded voice ("I'll stay close.", "Weapons free."). "Come here" leaves her "On my way." alone, and "Whatever" on the
  Emotes page is done without a word. Roles, emotes and dances answer this way too. An
  order she does not carry out (your party is full, or you are not her leader) keeps the slot's name.
  `VoiceWheelAcks` turns it off.
- **One line at a time.** When one order makes two lines at once (Come here's "On my way." and then "Coming right
  back.", a gift's thanks and then its reply), the second no longer cuts off the first; anything else, a call for help
  when she goes down included, is always heard. `VoiceOneLineSeconds`.
- **SC greets all of its people.** Voice Control no longer greets a runner you meet on the road or one on the way to the
  base (they greet you with a member's line that fits away from home, never "Welcome home, boss."), nor an outpost
  stand-in, a member out of sight on a posting or a run, or someone hostile to you (they say nothing). Where a voice has no recording
  of the greeting, Voice Control greets as before. `ScGreetingsOwnAll`.
- **Refusals said aloud.** A resident refusing a field job, a member refusing to fight with her work kit, and "Come with
  me." when your party has no room are spoken too, when she is within 30 m and neither asleep nor down,
  and Voice Control's "yes" to the refused order is neither said nor shown. `VoiceSpokenRefusals`. A
  spoken "follow me" with no room in the party now answers "You've already got a full crew with you. Ask me again when
  there's room." instead of "I can't right now.".
- **Lines that name an item play a recording too:** "Now I know how to make Forged Iron." plays "Now I know how to make
  something new.", and the caption still names the item.
- **105 more lines per voice, recorded in all 16 voices** (709 in all): 0-XNPCCore 3.4.06's gift, downed, crafting,
  patrol, ammo and book lines (with three ways to say thanks for a gift and three to call out when going down), the role
  and emote replies, Voice Control's go-there refusals, SC's own answers that Voice Control spoke in its voice (dismiss,
  hire, pick-up, follow, trade, the training line), a mining runner's radio lines, and an enclave's siege calls, its
  half-time call and its request announcements on the radio.
- **Fixed:** "I need fuel for that." no longer plays the recording of "I need more materials for that.".
- **Fixed:** Voice Control 3.4.06's "I'm in your way - point past me." and "I'm not hired yet - what have you got to
  offer?" play their recordings again: punctuation no longer decides whether a line matches.
- `sc voice barks` shows the new switches and this session's counts.

# Survivor Community 0.1.0 pre-release 3

Everything that changed since pre-release 2 of this 7 Days to Die 3.4 test build; section numbers refer to the included `README.txt`.

## Highlights
- **Members fight as a team.** Each member weighs their side against the enemies around them: in a fight their side can win they use their melee weapon, otherwise they keep their distance and shoot. At home, backing off never takes them out of the base.
- **Hurt members look after themselves.** They restock first aid from the stockpile's Meds, back away before bandaging and slowly regain health out of combat. Badly hurt at home with nothing to heal with, they shelter indoors or kite with their gun.
- **A real inventory panel for members.** "Show me your inventory" opens a Survivor Inventory panel where you equip a member's gun, melee weapon, armor and job tools by dropping items on slots.
- **Mining runs.** Send a resident out to mine stone and ore or dig clay; the haul lands in your home crates as real items.
- **Bundles can be opened** into an assortment of goods, so supplies can come back out of the stockpile.
- **Members sleep at night** in the base's beds, through 0-XNPCCore 3.4.05's routine.
- **Starting a community is your choice**, from a land claim's radial, and the land claim block has a new desk-and-locker model.
- **Fixed a bug that stopped your player data from saving** while an enclave request quest was in your journal.

## Combat
- **Team strength.** Once a second each member counts their side within 15 m (their Fighting stars, health and weapon, the other members, and you) against the awake enemies there; during a horde at the base, the whole defended area counts. A healthy member alone against one walker is a matched fight; alone against a feral, or two members against four walkers, is not.
- **You count only while you fight.** You count double, but only for 5 seconds after you hit an enemy near the member; standing by, or only being chased, you count for nothing.
- **One posture per member** decides their weapon and moves (section 15). They change weapons at most once every 5 seconds, unless they can't swing at the zombie.
  - **Melee:** a matched fight, at least 60% health (70% to go back to it), not swarmed. They fight with their melee weapon and don't back away; holding a gun, they shoot until the zombie is within 4 m, then switch once. A member whose best skill is Shooting stays on the gun while it has ammo.
  - **Kite:** any other fight with a gun that has ammo: outmatched, swarmed, hurt, raiders or anyone armed, a cop or a demolition zombie, or your gun order. They shoot, back off from anything within 2.5 m and don't switch to melee up close.
  - **Hold:** on watch or on a Stay order they keep their post and their gun. **Shelter:** badly hurt (see below).
- **A swing they can't take means the gun.** When a wall, a fence post or bars block the swing, the gun comes out at once and they kite that zombie for 45 seconds (90, then 180 if it happens again).
- **Backing off stays inside the base.** A member with a gun backs off from an enemy within 2.5 m and shoots from 2.5 to 4 m. At home each step back (10, 6 or 3 m) ends on a floor inside the home zone, clear of obstacles and out of every zombie's reach.
- With no such spot, or when a zombie keeps pace, they stop after about 1.5 seconds and shoot. Away from home they back off the way 0-XNPCCore does.
- **Cornered.** Cornered and reloading, or cornered for 3 seconds without a shot, a member swings their melee weapon until there is room again, never at a cop, an armed enemy or a zombie they can't hit. If no swing lands in 2.5 seconds, the gun comes back.
- **Your weapon orders stick.** Ordering a member's gun (the weapon menu, or by voice) keeps them on it until you order their melee weapon, and the other way round.
- **Cowardly members** hang back with their gun and run only from a fight going badly; at home they retreat inside rather than leaving the base.
- **Intruders.** A zombie inside the base that is after nobody now gets the two nearest free residents sent to fight it. One wandering past outside, or more than 6 m above or below the base's levels, is left alone.
- **Who residents go after.** At home, residents fight only an enemy that is after a member or you (plus the intruders above). During a base attack they still defend the whole base, and guards on watch keep shooting from their posts.
- `sc team <name>` shows a member's count, posture and the reason for it.

## Healing and getting hurt
- **Restock from the stockpile.** A member at home who runs out of first aid bandages and kits takes 1 medical unit from the stockpile and is topped up to 1 first aid kit, 2 first aid bandages and 2 cloth bandages (only what they lack), even mid-fight, at most once a day.
- **Before a supply or mining run** they restock the same way if they carry fewer than 2 first aid bandages and kits. The medical units tonight's injured need are never taken, and nothing is given when their pack is full.
- **Emergency restock.** A member at home with no first aid bandage or kit, nothing still healing them and under half health is restocked at once, even if they were restocked earlier that day (at most once an in-game hour).
- `MedsResupply` false lets you manage their supplies by hand. `sc meds` shows who was restocked and why not, and the day's summary lists restocks separately.
- **No bandaging with a zombie on them.** While an enemy after them is within 3 m, a member first backs away (10, 6 or 3 m; inside the base at home, near you when following) until nothing is within 4 m, then uses the item.
- With no room to back off, in a shelter, on watch or told to stay, or when the zombie keeps up, they use it at once, and after 6 seconds anyway. Under half health they use a first aid kit before a bandage.
- **Slow recovery out of combat.** Members regain 0.2 health a second, up to 60% of their health, while nothing threatens them: no zombie within 30 m that is after them or sees them, and none at all within 12 m.
- **Badly hurt with nothing to heal with** (below 35% health), a member stops fighting. At home they no longer run out of the base:
  - with a gun or bow that has ammo they **kite**: they back away from any zombie within 8 m, a step at a time inside the base, out to about 15 m, shooting between steps;
  - without one, or with raiders or bandits about, they **retreat inside**: they run to a roofed room away from the enemy, shut the doors they passed behind them and hold there, fighting only what reaches them.
- **Nobody shelters for good.** Sheltering ends at 45% health or after 20 seconds with no enemy about. A member restocked while sheltering comes out to bandage once nothing has been after them for 5 seconds; with no medicine they come out after 2 minutes once it is safe.
- While that hurt, a member is never walked to a fight, at home or away. (Until now, a member who wasn't Cowardly fought on at any health.)
- **Away from home** a badly hurt follower with a gun kites within 20 m of you instead of running off; without one they run as before. Guards on watch hold their post.

## Members' gear and the inventory panel
- **The Survivor Inventory panel.** "Show me your inventory" on a member now opens SC's own panel on the left instead of the looting screen. It shows the member's card, Weapons (Ranged, Melee), Armor (Head, Chest, Hands, Feet), Job tools (their best tool for Chop, Mine, Dig, Butcher and Repair), their Pack and their Bag (ammo and magazines).
- **Weapons:** drop a gun or bow on Ranged, or a melee weapon on Melee, and they use it; the one it replaces comes back to you. Take the gun out of Ranged and they fight melee only; Melee only swaps, since a member always keeps a melee weapon.
- **Armor:** drop a piece on its slot and they wear it, and their old piece comes back to you; drag one off and they go without.
- **Job tools** you drop go into their pack, and they use the best one for each job. You can take back tools you gave them; the work kit stays.
- **What you hand over comes back exactly.** A weapon or armor piece keeps its quality, mods and wear (it doesn't wear down while they have it), and no loaded round or seated magazine is lost in a swap.
- Items you handed over drop in their backpack if they die, and go into their pack if they leave the community.
- When a slot refuses an item, a message says why (a launcher, dual pistols, an unfinished test item, a tool in a weapon slot, armor for another slot).
- **The panel moves.** Drag its title bar and it stays where you put it; right-click the bar or type `sc ui reset` to put it back. `MemberInventoryPanel` false brings back the looting screen.
- **Weapons from any mod** can be handed over: anything 0-XNPCCore accepts as a weapon, except launchers, dual pistols and tools. Items a mod marks as unfinished test items (such as Combat Sandbox's unfinished IZY Classic guns) are refused.
- **"Use the weapons I gave you."** This row is back in 0-XNPCCore's weapon menu, so a member takes a gun you handed over even when it is unloaded.
- **Wardrobe.** 0-XNPCCore 3.4.05's Wardrobe ("Dress your hires") now really dresses SC's people: members, recruits, enclave and band people. Its picks and the panel's armor slots work together without duplicating items, and raiders are never dressed by you.

## Guns and magazines
- **Guns come out loaded (Locked & Loaded).** The magazine a member takes out when putting their gun away goes straight back in the next time they draw that gun, so it comes out ready to fire, with no magazine duplicated or lost.
- **Weapon attachments.** Survivors' and raiders' weapons can now carry attachments, as loot weapons do (`SurvivorWeaponModChance`, `RaiderWeaponModChance`, `WeaponModTags`). They stay on when the member switches weapons or the area reloads, and the gun you loot from a body has the same ones.
- A survivor's gun with a flashlight lets 0-XNPCCore 3.4.05 turn the light on in the dark.

## Home, land claims and enclaves
- **The community home is opt-in.** No land claim becomes home by itself any more: hold E on a land claim block you own and pick **Start a community here**. **Set as community home** on another claim moves it later, and **Linked storage** shows on the radial once you have a home.
- A save that already has a home keeps it, and `AutoHomeFirstClaim` true brings back the old way.
- Until you have a home, a survivor you ask to join says, in their own voice, that you need a home for your people first, and a message tells you how to start one. If you pick up the home's land claim block, nobody new joins until you set another claim as home or put one back on the same spot.
- **New land claim block.** It is now a desk with a lit lamp and the claim ledger beside a tall steel locker, built from the game's own furniture and lit like the blocks around it. It takes a 3 x 2 x 1 space, turns to face you as you place it, and can be turned but not tipped over.
- **Outpost Claim Block.** The Outpost Flag is now called the Outpost Claim Block. It shares the new model and has an icon you can read.
- **Linked storage past 90 slots.** Without RE Inventory, linked storage opens in its own scrolling window with room for 360 slots, so every stack and free slot shows (9 columns, or 8 with AshfallUI so it fits the screen). With RE Inventory, its grid shows linked storage as before.
- With AshfallUI, the linked storage strip sits under the grid, so AshfallUI's filter tabs can be clicked.
- **Enclave doors.** Every locked door of a living enclave's building, raiders' too, is unlocked as you come near, so you can walk in and find them. The doors aren't opened and don't become yours, and their containers stay theirs (`EnclaveDoorsUnlocked`).

## Runs, jobs and crafting
- **Mining runs.** **Mine** and **Dig clay** now sit beside the resources in the supply-run list (the talk row and the community window). A resident with a mining tool (any pickaxe, an auger or the work kit's stone axe) or a shovel walks to a natural spot 150 to 700 m from home, works it for 6 game hours and walks back.
- A mining run has a supply run's risk, escort, distress calls and radio. Without a tool, the resident refuses and says why.
- The haul is real items: for a whole run with an iron tool, about 180 stone and 90 ore from a vein, or about 240 clay. A stone tool brings 60% of that, steel 140%, an auger 180%, and Wits, traits and morale add to it.
- The haul goes into the home crates, onto matching stacks first. What doesn't fit stays in their inventory, and if you are away it waits until you are home; they won't go again while the last haul is still waiting.
- Asked to Mine or Dig clay as a home job, a resident now points you to a mining run. `sc run <name> mine` or `clay` sends one for testing.
- **Hurt runners turn back.** A runner below 35% health with nothing to heal with gives up the job and heads home as if recalled, bringing what they found so far and telling you so. They come home injured unless patched up on the way (`RunHomeBelowHealth`).
- If a distress call has them pinned down, the rescue still counts when you are within 70 m of where they called from; otherwise the call ends.
- **Runners go around trouble.** A runner who would run from a fight (Cowardly, or badly hurt with nothing to heal with) no longer runs from a zombie and then walks straight back into it: they step around a threat in their way or hold back and shoot, and after 30 seconds of waiting they move on.
- **The work kit stays put.** A zombie lingering out of reach no longer makes a member swap between their weapon and the axe at every tree.
- **Work shifts.** What a resident makes at a crafting station on a work shift now goes into the home crates like the rest, instead of sometimes staying in their inventory.

## Openable bundles
- Hold a bundle and click, or pick **Open** on it, and one bundle of the stack becomes a random assortment of its resource's everyday goods (section 5). The goods go into your inventory, and anything that doesn't fit drops at your feet.
- An opened bundle is worth about three quarters of what packing it took (`BundleOpenValueShare` 0.75), never more than 10% over, with at least one item and each item one stack at most. With three or more craftsman stars in the community it holds a little less, so opening never gives back what packing took.
- A bundle you **Take** at the packing station opens the same way, which is how supplies come back out of the stockpile. The lists are settings (`BundleOpenFood` and the rest), and `BundleOpen` false turns opening off.

## Looks, names and voices
- **Streamer names** (off by default). List your viewers in `%APPDATA%\7DaysToDie\SurvivorCommunity\StreamerNames.txt`, set `StreamerNames` to true, and the new survivors SC makes take those names in order, matched to their sex (`Sarah, female`). Details in section 3.
- Each name is given once per save, except that the name of an NPC you never met who leaves the area goes back on the list. Nobody already in your world is renamed, `StreamerNamesFor` can give names to raiders too, SC rereads the file when it changes, and `sc names` shows the list.
- **With Vanity Vault, the face comes first and the skin tone suits it.** New survivors and raiders get their face first, then a skin tone that fits it.
- **Voices follow the face.** Each survivor speaks with the voices of their face's race, and a White or Asian face with a dark skin tone speaks with Black voices. Survivors you already know switch to a fitting voice the next time they load.
- **Hair color suits the skin and face**, with or without Vanity Vault: medium skin tones never get natural blond or red, and dark tones not light brown either. Gray and white hair stay at about 1 in 5 on everyone, and dyed colors still turn up on anyone.
- **People you already know** get a fitting natural hair color the next time they load (always the same one, and never gray in place of a young color). Their face and skin tone are never changed on their own; `sc look <name> tone` gives one survivor a tone that suits their face, only when you ask.
- A survivor whose look needs assets your game no longer has is shown with a stand-in look, but their saved look is kept.
- **All 16 voices are complete.** The two Native women's voices are recorded, so nobody borrows another voice any more.
- **Horde warnings are voiced.** At home, the guard on watch says the horde is gathering, says when they first see it and calls a new all clear, in person within 30 m or over your handheld radio. While you are away, the radio from home speaks the call, the arrival and the result while you carry a handheld.
- `HordeVoiceLines` false keeps the messages only, and `sc radio horde` plays one to test. The "you need a home first" reply is voiced too; a mining runner's radio lines and a few of the newest refusals are text only for now.

## Sleep and routine
- **Members sleep at night.** About an hour after dusk, residents at home go to bed in the base's beds through 0-XNPCCore 3.4.05's resident routine, and they get up at dawn. The day stays SC's: no station work or strolling.
- Only as many sleep as there are beds, and the same members get them each night; with no bed in the base, everyone keeps SC's own night routine. Guards on watch, members on a work shift and anyone you are talking to stay up, and nobody goes to bed on a blood moon night.
- A fight or running from danger still comes first, and sleeping members no longer talk in their sleep.
- Enclave people sleep the same way in their own building; a hostile enclave's never do.
- `SleepAtNight` false keeps everyone on SC's own night routine; `sc sleep` shows who is in bed and why not.

## 0-XNPCCore and Voice Control 3.4.05
- SC works with 0-XNPCCore and Voice Control 3.4.05: the subtitles of SC's voice lines show, and members' power attacks work with melee weapons from weapon packs that use the game's plain melee action.
- **Torches.** Members carry a torch again, so 0-XNPCCore's night torch and left-hand torch pose work for the community. A member holding a torch no longer ends up treating it as their weapon.
- 0-XNPCCore's own bandits, pickpockets and custom NPCs keep their torch while SC is installed.
- **"Follow me" by voice.** Saying it through Voice Control to a member at home now works: they nod and follow, or say why they can't.
- Wardrobe, gun lights and sleeping at night (above) also come with 3.4.05.

## Windows and UI
- **The game's own look without AshfallUI.** The community window, the radio window, the survivor card and the Home Status panel use the game's own look when AshfallUI isn't installed, and AshfallUI's look when it is.
- **Packing and linked storage strips** line up with the box's grid and fit both the game's grid and RE Inventory's narrower one. The buttons have short labels (Food, Water, Meds, Ammo, Mats, Fuel, Lux) with the full text on hover, and the stock line wraps.
- **Morale colors.** The community window's morale bars, the Home Status dot, the survivor card's mood word and the morale buff icon are green from 50, amber below 50 and red below 25.
- **The talk card moves.** If your UI mod puts the survivor card over the responses, drag it by its name bar; it remembers its place (right-click the bar or `sc ui reset` to put it back).
- On the survivor card, a long name no longer runs under the portrait.

## Fixes
- **Player saves.** While an enclave request quest (a siege included) was in your journal, every autosave of your player data failed, so your inventory, XP, position and quests stopped saving. Fixed, and SC now also stops any other mod's quest from breaking the player save this way.
- A save hit by that bug saves normally again once loaded with this version; what happened after its last good save isn't recovered.
- **Duplicate magazines (Locked & Loaded).** Members no longer gain an extra magazine every time they change weapons or the world loads. Magazines a member already picked up this way are not taken away.
- **Station crafting.** A member crafting at a station you have open no longer sends the output into your inventory or counts it as your crafting (no crafting XP for you). Their crafts wait at the station while its output is full, and count for the member once per item.
- **Members walking out of the base.** In a base built inside a POI, a member at home no longer walks out of the base when a zombie comes for them and wanders back later.
- **Escorted members** walk to their post instead of vanishing from beside you as you step into the base.
- **Swinging in reach.** A resident defending the base swings at a zombie already in reach instead of walking into it.
- Closing a runner's rescue quest no longer throws an error.

## New and changed settings
All in `Mods\SurvivorCommunity\Config\blocks.xml` (section 20). Restart the game after a change.

| Setting | Default | What it does |
|---|---|---|
| `AutoHomeFirstClaim` | false | true: your first land claim becomes the home by itself, as before |
| `EnclaveDoorsUnlocked` | true | Enclaves' locked doors are unlocked as you come near |
| `StationWalkIn` / `StationWalkInMetres` | true / 30 | A member you escort home within this many meters walks to their post |
| `OutpostAttacks` | true | Outpost risk (attacks, blood moon rolls, squatters, raider demands). Already true in pre-release 2's file, though its notes said off; now the notes and the built-in default agree |
| `StreamerNames` / `StreamerNamesFile` / `StreamerNamesFor` | false / StreamerNames.txt / survivors | Viewers' names for new survivors; the names file; who gets them (`survivors`, `raiders` or `all`) |
| `BundleOpen` / `BundleOpenValueShare` | true / 0.75 | Bundles can be opened; what an opened bundle is worth, as a share of its packing value (at most 0.85) |
| `BundleOpenFood` (and `...Water`, `...Meds`, `...Ammo`, `...Materials`, `...Fuel`, `...Luxury`) | everyday supplies | What each kind opens into, as `item:weight` lists |
| `RunHomeBelowHealth` | 0.35 | A runner this hurt with nothing to heal with heads home (0: never) |
| `MiningRuns` / `MiningRunSiteHours` | true / 0 | Offer Mine and Dig clay as runs; game hours at the spot (0: same as `RunSiteHours`) |
| `MiningStonePerHour` / `MiningOrePerHour` / `DigClayPerHour` | 30 / 15 / 40 | Haul per site hour with an iron tool and 1 Wits star |
| `MiningToolTierMults` | 0.6,1,1.4,1.8 | The haul by the tool's tier |
| `HordeVoiceLines` | true | Horde warnings are spoken as well as shown |
| `TeamMelee` / `TeamRadius` | true / 15 | Team strength decides melee or kite; how far around a member it counts (m) |
| `TeamMargin` / `TeamLeaveMargin` | 1 / 0.8 | A fight is matched at this many times the enemies, and stops being matched below the second |
| `TeamLowHealth` / `TeamLowHealthBack` | 0.6 / 0.7 | Below this share of health the gun again; melee again from the second |
| `TeamPlayerWeight` / `TeamPlayerFightingSeconds` | 2 / 5 | How much you count while fighting, and for how long after your hit |
| `TeamSwapHoldSeconds` / `PostureDwellSeconds` / `PostureMeleeDist` | 5 / 5 / 4 | Time between weapon changes; time before a melee/kite switch; how close a zombie gets before a matched member switches to melee (m) |
| `TeamThreatTags` | walker:1, feral:2, radiated:3 ... | How much each kind of enemy counts |
| `InvestigateIntruders` / `InvestigateResponders` / `InvestigateRadius` | true / 2 / 0 | Residents go after idle zombies in the base; how many go; from how far (0: anywhere in the zone) |
| `InvestigateEnterMargin` / `InvestigateHeight` | 0 / 6 | How far outside the zone, and how far above or below the base's levels, an idle zombie still counts (m) |
| `BackOffHome` / `BackOffSteps` | true / 10,6,3 | At home, back-offs end inside the base; the steps tried (m) |
| `BackupKeepDistanceMax` / `BackOffNoPointSeconds` / `StandOffShootSeconds` | 2.5 / 1 / 1.5 | Back off from an enemy this close (m); shoot this long with no spot; shoot this long after a step that gained nothing |
| `FleeBelowHealth` | 0.35 | Below this share of health with nothing to heal with, a member stops fighting (0: off) |
| `RetreatHome` / `RetreatCowards` | true / true | Badly hurt members kite or shelter inside instead of running off; Cowardly members at home shelter too |
| `RetreatHealedAt` / `RetreatClearSeconds` / `RetreatMaxSeconds` | 0.45 / 20 / 120 | A retreat ends at this health share or after this long with no enemy about; a shelter also ends after the third once it is safe (0: never by time) |
| `RetreatFightBackRange` / `ShelterMinThreatDist` | 2.5 / 12 | A sheltering member fights back only at an enemy this close (m); how far the threat must be before a long shelter ends (m) |
| `StandOffMin` / `StandOffMax` / `StandOffFollowerLeash` | 8 / 15 / 20 | A badly hurt member kites from a zombie within the first, out to about the second; a hurt follower kites within the third of you (m) |
| `MedsResupply` | true | Members restock their medical supplies from the stockpile (false: you manage them by hand) |
| `MedsResupplyBelow` / `MedsResupplyRunBelow` | 1 / 2 | Restock at home below this many first aid bandages and kits; before a run, below the second |
| `MedsResupplyMix` | medicalFirstAidKit:1, medicalFirstAidBandage:2, medicalBandage:2 | What 1 medical unit tops them up to |
| `MedsResupplyCooldownHours` / `MedsResupplyKeepForInjured` | 24 / true | In-game hours between restocks at home; never take the units tonight's injured need |
| `MedsResupplyEmergencyBelow` / `MedsResupplyEmergencyCooldownHours` | 0.5 / 1 | Under this health share with no heal item, restock inside the cooldown; at most once per this many hours |
| `MedicSafeDistance` / `MedicMaxWait` / `HealBackOff` | 3 / 6 / true | No bandaging with an enemy this close (m); bandage anyway after this many seconds; back off first |
| `RegenOutOfCombat` / `RegenRatePerSecond` / `RegenCapShare` | true / 0.2 / 0.6 | Slow health regeneration out of combat, per second, up to this share of health |
| `RegenEnemyRadius` / `RegenNearRadius` | 30 / 12 | A threat within the first that is after them or sees them, or any enemy within the second, holds it (m) |
| `SleepAtNight` / `SleepAfterDuskHours` / `SleepZoneHeight` | true / 1 / 15 | Sleep in beds at night; hours after dusk before bed; how far above and below the claim block beds count (m) |
| `MemberInventoryPanel` | true | The Survivor Inventory panel (false: the looting screen) |
| `SurvivorWeaponModChance` / `RaiderWeaponModChance` | 0.5 / 0.5 | Chance of a weapon's first attachment; each further one is half as likely (0: none) |
| `WeaponModTags` | sideAttachment, scope, ... | Which kinds of attachment can roll |
| `VoiceRaceByFace` / `VoiceDarkTonesMale` / `VoiceDarkTonesFemale` | true / Bronze, Umber, Espresso / Chestnut, Umber, Espresso | With Vanity Vault, voices follow the face; tones that give a White or Asian face Black voices |
| `VoiceRaceBySkin` | Vanity Vault's 15 natural tones | Each skin tone's voice race, used when the face gives none |
| `SurvivorTonesByFace` / `HairGroupByFace` | the tones that suit each face / true | The skin tones a new face can get; the face narrows natural hair colors too |
| `SurvivorMidSkinsMale` / `...Female`, `SurvivorDarkSkinsMale` / `...Female` | see blocks.xml | Medium and dark skin tones for men and for women |
| `SurvivorMidSkinHairColors` / `SurvivorDarkSkinHairColors` / `SurvivorGrayHairColors` | 03 LightBrown to 08 White / 04 Brown to 08 White / 07 Gray, 08 White | Natural hair colors for medium and dark skins; the colors that read as age |

## Known issues and not yet tested
- The new combat and healing behavior (team strength, postures, back-offs, sheltering, backing off to heal and the magazine re-seat on draw) has had only limited play-testing. If a member stands still, flips weapons or won't heal, `sc team <name>`, `sc retreat <name>` and `sc meds <name>` say why; please add their output to your report.
- Sleeping at night, mining runs, openable bundles and streamer names have had little play-testing so far.
- A badly hurt member who still has a melee weapon in hand when a zombie comes gets no step back until their gun comes out.
- Cornered with no room, no clear shot and a zombie they can't swing at, a member keeps their gun and waits for an opening.

## Updating from pre-release 2
- Delete the old `Mods\SurvivorCommunity` folder before extracting the new one. A new save made for testing is still best.
- An existing save keeps its community home.
- Land claim blocks placed before the update take on the new model where they stand. It is wider than the old block, so it may overlap something built right beside it.
- Bug reports: [open an issue](https://github.com/Relentless757/SurvivorCommunity/issues) with what happened and your log, `%USERPROFILE%\AppData\LocalLow\The Fun Pimps\7 Days To Die\Player.log`.
