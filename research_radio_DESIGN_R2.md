# Round 27 (R2): the handheld radio - design (user decisions 2026-11-09)

The user's request: "A new 'handheld radio' item should be added. With the item in the player's inventory, the player can
open the radio station menu remotely. A new tab would be placed to the left of the community tab as well while a handheld
radio is in the player's inventory. Additionally, giving a radio to NPCs will allow them to communicate directly to you
while they are out on supply runs (dialogue would sound like they're talking from a radio, which could possibly be done
via code)."

Earlier decision this supersedes: "Radio commands only at a radio block (handheld radios later)" (2026-11-03).

## Decisions (asked and answered 2026-11-09)
1. **Opening:** tab only. A new tab LEFT of the Community tab (W1's `scCommunityTab`), shown only while a handheld radio is
   anywhere in the player's inventory (bag or toolbelt). No in-hand use action, no radial.
2. **Relays through the station:** the handheld opens the radio station's window (all its pages) from anywhere, but only
   while a radio station at home has power. Without one the window (or the tab's page) says the home station is offline.
3. **Source:** a workbench recipe AND rare loot (electronics containers, raiders) AND traders stock it now and then.
4. **Runner talk:** a member who carries a handheld radio (given by the player, in the member's own inventory) talks to the
   player during a supply run - new recorded lines (keys below), played as a radio: the member's own SC voice with a
   radio effect (band-limited, a little grit, a crackle/squelch in and out), 2D at the player wherever they are, with a
   caption naming them. Until a voice has the clip recorded, the caption shows alone (SC's usual no-recording path).
   Heard when the player carries a handheld radio (radio to radio, no station needed).
5. **Distress needs a radio:** a runner can radio for help only while carrying a radio (today any runner can while a
   station is powered). The call arrives when the player carries a handheld OR a home station is powered.
6. **Recall by radio:** from the radio window (remote or at the station) the player can tell a runner who carries a radio
   to cut the run short and head home (the same as the existing in-person recall, ScRunners.Recall).

## New voice lines (one per key, every SC voice; recorded separately in voice round vo3)
- scRadioRunOut_1 "Heading out now. I'll check in when I get there."
- scRadioRunOut_2 "On my way. Keep the channel open."
- scRadioRunArrived_1 "I'm here. Going in to look around."
- scRadioRunArrived_2 "Made it to the spot. Starting the search."
- scRadioRunFound_1 "Good haul over here. Packing it up now."
- scRadioRunFound_2 "Found what we needed. This trip was worth it."
- scRadioRunEmpty_1 "Place is picked clean. Not much to bring back."
- scRadioRunEmpty_2 "Slim pickings out here. Sorry."
- scRadioRunTrouble_1 "I've got company! I need help out here!"
- scRadioRunTrouble_2 "They've got me pinned down! Anyone hear me?"
- scRadioRunHome_1 "Done here. Heading back home."
- scRadioRunHome_2 "Starting back now. Save me some dinner."
- scRadioRunNear_1 "Almost home. Don't shoot, it's me."
- scRadioRecallAck_1 "Copy that. Cutting it short and heading home."
The captions are these texts (Localization.csv keys `<key>Said`, as SC's other spoken lines).

## Open points for the implementer to settle (and document)
- Which run events exist for physical runs (ScRunners: walking out, at the POI, looting, walking home) and for the
  simulated ("hidden at home") kind, and where each line fires; at most one line per event per run.
- How the member's carried radio is known while the body is unloaded (a simulated run, a physical runner in an unloaded
  chunk): snapshot "carries a radio" into the Member record when the run starts (and when the body is next seen).
- The radio effect: Unity AudioLowPassFilter / AudioHighPassFilter / AudioDistortionFilter on a 2D AudioSource, or a
  per-clip DSP pass; must not change how the same clip plays in person.
- The tab's looks for vanilla, SurvivorOrigins, AshfallUI and AshfallUI + SurvivorOrigins exactly as W1 did for the
  Community tab (PORT_34.md Round 19), placed left of the Community plate; AshfallUI itself is never edited.
- A placeholder icon until the user picks the final one from GPT concept sheets.
