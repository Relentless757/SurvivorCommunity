Survivor Community 0.1.0 - pre-release for 7 Days to Die 3.4
=============================================================

A community-building mod in the spirit of State of Decay. Survivors you recruit live in your land claim, have skills,
traits and a specialty, share a morale that depends on how well the base is supplied, and can be stationed, taken along,
sent on supply runs or set to craft. Out in the world there are survivor and raider enclaves, raider bands on the roads,
infestations, outposts to set up and radio contacts to trade with.

THIS IS A TEST BUILD. Expect bugs. Please use a new save made for testing, not a save you care about.


Requirements
------------
- 7 Days to Die 3.4 (this build is for 3.4 only).
- 0-SCore 3.4.14 or newer.
- 0-XNPCCore 3.4.03 (its Voice Control DLL is included in the 0-XNPCCore folder).
- Easy Anti-Cheat off (this is a Harmony mod).
- Single player only for now.


Install
-------
1. Install 0-SCore and 0-XNPCCore first.
2. Extract this zip into your 7 Days to Die "Mods" folder, so you get Mods\SurvivorCommunity\ModInfo.xml and
   Mods\LandClaimUpgrade\ModInfo.xml.
3. Start a NEW game.

Do not remove the mod from a save that used it. It adds blocks (radio station, guard tower, watch post, packing station),
and loading that save without the mod can damage the chunks those blocks are in.


Included: Upgradable Land Claim Block
-------------------------------------
The zip has a second mod folder, LandClaimUpgrade, made to pair with Survivor Community: your community lives inside your
land claim, and this lets the claim grow to take in the buildings around it.
- Hold E on your own land claim block and pick "Claim Menu". It lists the POIs next to your claim. Clear a POI of its
  sleepers, then claim it (the price scales with its size). "Start secure quest" marks what is left inside. With no POI
  in reach, "Expand into open ground" grows the claim 1.5x instead.
- Locked doors inside your claim become yours when you try them. Cleared POIs get a green check mark on the map.
- Settings are in Mods\LandClaimUpgrade\Config\blocks.xml.
It is optional: delete the LandClaimUpgrade folder before you start the game if you don't want it. Taken out of a save
later, its claims shrink back to the usual 41x41.


Works with (optional)
---------------------
- Locked & Loaded: survivors and raiders use magazines.
- Sonja's Vanity Vault 3.3: body paints, face paints and tattoos on survivors and raiders.
- WalkerSim2: wild raiders still spawn while WalkerSim2 handles the zombies.
- IZY Classic: raiders can carry its guns.


Getting started
---------------
- Place a land claim. Your first land claim becomes the community's home. Its radial menu has "Set as community home"
  and "Community".
- Talk to a survivor and pick "I could use someone like you in my community." They ask a favor first; once it is done
  they join for free.
- The community window opens from the ESC menu (Community), from the land claim's radial menu, and from its own tab.
- Followers and residents can craft through 0-XNPCCore's crafting. Talk to a resident at home and pick
  "I have work for you." to give them a work shift. Their specialty stars decide what they can make.


Reporting bugs
--------------
Open an issue on the GitHub page with what you did, what you expected and what happened. Please attach your log:
  %USERPROFILE%\AppData\LocalLow\The Fun Pimps\7 Days To Die\Player.log
For more detail, set DebugLogging to true in Mods\SurvivorCommunity\Config\blocks.xml before playing.


Credits
-------
- sphereii for 0-SCore and Dave Taylor for 0-XNPCCore, which this mod is built on.
- Survivor voices were generated with ElevenLabs.
- Made by Elisk.
